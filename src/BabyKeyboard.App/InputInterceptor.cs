using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using BabyKeyboard.Core;

namespace BabyKeyboard.App;

internal readonly record struct InputSignal(int VirtualKey, int X = 0, int Y = 0, bool Click = false,
    SoundPreset Sound = SoundPreset.Crystal);
internal sealed record InputSnapshot(UnlockPhase Phase, double Progress, int HeldCount,
    SoundPreset Sound = SoundPreset.Crystal, long SoundRevision = 0);

internal sealed class InputInterceptor : IDisposable
{
    private readonly Channel<InputSignal> signals = Channel.CreateBounded<InputSignal>(
        new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly ManualResetEventSlim ready = new(false);
    private readonly Native.HookProc keyboardCallback, mouseCallback;
    private readonly HashSet<PointerButton> inheritedButtons = [];
    private Thread? thread;
    private uint threadId;
    private nint keyboardHook, mouseHook;
    private UnlockController controller = new();
    private InputSnapshot snapshot = new(UnlockPhase.Playing, 0, 0);
    private Exception? error;
    private long pulse = Stopwatch.GetTimestamp();
    private bool completeNotified;
    private int stopping;
    private SoundPreset selectedSound;
    private long soundRevision;
    internal bool SimulateMouseHookFailure { get; init; }
    public event Action? Unlocked;
    public event Action<Exception>? Failed;

    public InputInterceptor()
    {
        keyboardCallback = Keyboard;
        mouseCallback = Mouse;
    }

    public InputSnapshot Snapshot => Volatile.Read(ref snapshot);
    public long LastPulse => Interlocked.Read(ref pulse);
    public bool TryRead(out InputSignal signal) => signals.Reader.TryRead(out signal);
    private static double Now => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    public void Start()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "Input protection" };
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("输入保护启动超时。");
        if (error is not null) throw new InvalidOperationException("未能启动键鼠保护。", error);
    }

    private void Run()
    {
        nuint timer = 0;
        try
        {
            threadId = Native.GetCurrentThreadId();
            Native.PeekMessage(out _, 0, 0, 0, 0);
            List<int> held = [];
            for (int vk = 8; vk < 255; vk++)
                if (vk is not (0x10 or 0x11 or 0x12) && Native.GetAsyncKeyState(vk) < 0) held.Add(vk);
            foreach (var (vk, button) in new[] { (1, PointerButton.Left), (2, PointerButton.Right),
                (4, PointerButton.Middle), (5, PointerButton.Back), (6, PointerButton.Forward) })
                if (Native.GetAsyncKeyState(vk) < 0) inheritedButtons.Add(button);
            controller = new UnlockController(held, inheritedButtons);
            keyboardHook = Native.SetWindowsHookEx(Native.WhKeyboardLl, keyboardCallback, Native.GetModuleHandle(null), 0);
            if (keyboardHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (SimulateMouseHookFailure) throw new InvalidOperationException("测试：鼠标钩子安装失败。");
            mouseHook = Native.SetWindowsHookEx(Native.WhMouseLl, mouseCallback, Native.GetModuleHandle(null), 0);
            if (mouseHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            timer = Native.SetTimer(0, 0, 16, 0);
            if (timer == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            Update();
            ready.Set();
            if (Volatile.Read(ref stopping) != 0) return;
            int result;
            while ((result = Native.GetMessage(out var message, 0, 0, 0)) > 0)
            {
                if (message.Id == 0x8021) Thread.Sleep(Timeout.Infinite);
                Update();
                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }
            if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (Exception ex)
        {
            error = ex;
            Failed?.Invoke(ex);
        }
        finally
        {
            if (keyboardHook != 0) Native.UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
            keyboardHook = mouseHook = 0;
            if (timer != 0) Native.KillTimer(0, timer);
            ready.Set();
        }
    }

    private void Update()
    {
        Interlocked.Exchange(ref pulse, Stopwatch.GetTimestamp());
        double now = Now;
        controller.Tick(now);
        Volatile.Write(ref snapshot, new(controller.Phase, controller.Progress(now), controller.HeldCount, selectedSound, soundRevision));
        if (controller.Phase == UnlockPhase.Complete && !completeNotified)
        {
            completeNotified = true;
            Unlocked?.Invoke();
        }
    }

    private nint Keyboard(int code, nint wParam, nint lParam)
    {
        if (code < 0) return Native.CallNextHookEx(0, code, wParam, lParam);
        try
        {
            var data = Marshal.PtrToStructure<Native.KeyboardData>(lParam);
            var key = new PhysicalKey((int)data.Vk, (int)data.Scan, (data.Flags & 1) != 0);
            bool inherited = controller.IsInheritedKey(key.VirtualKey);
            int message = (int)wParam;
            if (message is Native.WmKeyDown or Native.WmSysKeyDown)
            {
                if (controller.KeyDown(key, Now))
                {
                    var choice = SoundPresets.FromNumpad(key);
                    if (choice is { } sound) { selectedSound = sound; soundRevision++; }
                    // Selection is durable in the snapshot even if visual events are dropped during a burst.
                    int labelKey = choice is { } digit ? 0x61 + (int)digit : key.VirtualKey;
                    signals.Writer.TryWrite(new(labelKey, Sound: selectedSound));
                }
            }
            else if (message is Native.WmKeyUp or Native.WmSysKeyUp)
                controller.KeyUp(key, Now);
            Update();
            // Balance a down event that Windows received before protection was installed.
            if (inherited && message is Native.WmKeyUp or Native.WmSysKeyUp)
                return Native.CallNextHookEx(0, code, wParam, lParam);
        }
        catch (Exception ex) { Fail(ex); }
        return 1;
    }

    private nint Mouse(int code, nint wParam, nint lParam)
    {
        if (code < 0 || (int)wParam == 0x200) return Native.CallNextHookEx(0, code, wParam, lParam);
        try
        {
            var data = Marshal.PtrToStructure<Native.MouseData>(lParam);
            int message = (int)wParam;
            PointerButton? button = message switch
            {
                0x201 or 0x202 => PointerButton.Left, 0x204 or 0x205 => PointerButton.Right,
                0x207 or 0x208 => PointerButton.Middle,
                0x20B or 0x20C => (data.Data >> 16) == 1 ? PointerButton.Back : PointerButton.Forward,
                _ => null
            };
            if (button is { } b)
            {
                bool down = message is 0x201 or 0x204 or 0x207 or 0x20B;
                if (down)
                {
                    if (controller.ButtonDown(b, Now))
                        signals.Writer.TryWrite(new(0, data.Position.X, data.Position.Y, true, selectedSound));
                }
                else
                {
                    bool inherited = inheritedButtons.Remove(b);
                    controller.ButtonUp(b, Now);
                    Update();
                    if (inherited) return Native.CallNextHookEx(0, code, wParam, lParam);
                }
            }
            Update();
        }
        catch (Exception ex) { Fail(ex); }
        return 1;
    }

    private void Fail(Exception ex)
    {
        if (Interlocked.Exchange(ref stopping, 1) != 0) return;
        Failed?.Invoke(ex);
        Native.PostThreadMessage(threadId, Native.WmQuit, 0, 0);
    }

    public bool Stop()
    {
        Interlocked.Exchange(ref stopping, 1);
        if (thread is null || !thread.IsAlive) return true;
        Native.PostThreadMessage(threadId, Native.WmQuit, 0, 0);
        return thread.Join(TimeSpan.FromSeconds(2));
    }

    internal void SimulateHang() => Native.PostThreadMessage(threadId, 0x8021, 0, 0);

    public void Dispose()
    {
        if (!Stop()) Environment.Exit(5); // OS tears down hooks; guardian restores the cursor.
        ready.Dispose();
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using BabyKeyboard.App;

namespace BabyKeyboard.Tests;

internal static partial class Program
{
    private const nuint Tag = 0x42414259;
    private static readonly HashSet<(int Vk, bool Extended)> InjectedDown = [];

    private static void RunAudioCheck()
    {
        Test("Native PCM output opens and keeps playing soft tones", () =>
        {
            using var player = new TonePlayer();
            for (int i = 0; i < 40 && !player.Available; i++) Thread.Sleep(50);
            Check(player.Available, "No usable default PCM output device.");
            foreach (int key in new[] { 65, 68, 71 }) { player.Play(key); Thread.Sleep(180); }
            Check(player.Available, "The audio driver rejected playback buffers.");
        });
    }

    static partial void RunIntegration(string appPath)
    {
        appPath = System.IO.Path.GetFullPath(appPath);
        nint previous = Native.GetForegroundWindow();
        Native.GetCursorPos(out var cursor);
        Native.GetClipCursor(out var baseline);
        bool unrestricted = baseline == Native.VirtualBounds;
        using var fixture = new SafetyFixture();
        using var observer = new HookObserver();
        try
        {
            Test("Native protection blocks key, mouse, wheel, Win, Alt+Tab and Alt+F4 events", () =>
            {
                using var child = new ProtectedRun(appPath, fixture, 30);
                fixture.Focus();
                Check(ForegroundIsOurs(), "A safe input target could not be focused; no input was sent.");
                observer.Reset();
                Tap(65); Tap(0x1B); Tap(0x14); Tap(0x5B, true);
                Down(0xA4); Tap(0x09); Up(0xA4);
                Down(0xA4); Tap(0x73); Up(0xA4);
                Mouse(0x2); Mouse(0x4); Mouse(0x8); Mouse(0x10); Mouse(0x800, 120);
                for (int i = 0; i < 350; i++) Tap(65 + i % 26);
                Thread.Sleep(350);
                Check(observer.KeyboardEvents == 0, $"Leaked keyboard events: {observer.KeyboardEvents}");
                Check(observer.MouseEvents == 0, $"Leaked mouse events: {observer.MouseEvents}");
                Check(!child.Process.HasExited, "Protection ended under burst input.");
                Native.GetClipCursor(out var clipped);
                Check(clipped == Native.Monitors()[0].Bounds.Inset(8), "Cursor was not clipped to the protected area.");

                Down(13); Thread.Sleep(1200); Up(13); Thread.Sleep(100);
                Check(!child.Process.HasExited, "A short Enter incorrectly unlocked.");
                Down(13); Thread.Sleep(400); Tap(65); Thread.Sleep(2900); Up(13);
                Thread.Sleep(100); Check(!child.Process.HasExited, "Other-key cancellation failed.");
                Down(13, true); Thread.Sleep(3200);
                Check(!child.Process.HasExited, "Exited before the held Enter was released.");
                Up(13, true);
                child.WaitForExit();
                Check(child.Process.ExitCode == 0, "Unexpected exit code.");
                Check(observer.KeyboardEvents == 0, "The final Enter leaked to the background.");
                CheckRestored(baseline);
                fixture.Focus(); observer.Reset(); Tap(65); Mouse(0x2); Mouse(0x4); Thread.Sleep(100);
                Check(observer.KeyboardEvents == 2 && observer.MouseEvents == 2, "Input did not resume after exit.");
            });

            Test("An Enter held at launch must be released before it can unlock", () =>
            {
                fixture.Focus(); Check(ForegroundIsOurs());
                Down(13);
                using var child = new ProtectedRun(appPath, fixture, 20);
                for (int i = 0; i < 32; i++) { Down(13); Thread.Sleep(100); }
                Check(!child.Process.HasExited, "The startup Enter triggered unlock.");
                Up(13); Thread.Sleep(100);
                Down(13); Thread.Sleep(3200); Up(13);
                child.WaitForExit(); Check(child.Process.ExitCode == 0); CheckRestored(baseline);
            });

            Test("Audio failure leaves the protected interaction usable", () =>
            {
                using var child = new ProtectedRun(appPath, fixture, 10, "--test-audio-failure");
                fixture.Focus(); observer.Reset(); Tap(66); Thread.Sleep(100);
                Check(observer.KeyboardEvents == 0 && !child.Process.HasExited);
                Down(13); Thread.Sleep(3200); Up(13);
                child.WaitForExit(); Check(child.Process.ExitCode == 0); CheckRestored(baseline);
            });

            Test("Guardian recovers from a frozen UI", () =>
            {
                using var child = new ProtectedRun(appPath, fixture, 15, "--test-ui-hang-after-ms", "1500");
                var elapsed = Stopwatch.StartNew();
                child.WaitForExit(10000);
                Check(elapsed.Elapsed.TotalSeconds < 9, "Guardian recovery exceeded its expected bound.");
                Check(child.Process.ExitCode != 0); CheckRestored(baseline);
                fixture.Focus(); observer.Reset(); Tap(67); Thread.Sleep(100);
                Check(observer.KeyboardEvents == 2, "Keyboard hook survived the frozen process.");
            });

            Test("Guardian recovers from a frozen input thread", () =>
            {
                using var child = new ProtectedRun(appPath, fixture, 15, "--test-input-hang-after-ms", "1500");
                child.WaitForExit(10000); Check(child.Process.ExitCode != 0); CheckRestored(baseline);
            });

            Test("Abrupt process death releases hooks and restores the cursor", () =>
            {
                using var child = new ProtectedRun(appPath, fixture, 10);
                child.Process.Kill(); child.Process.WaitForExit(3000);
                CheckRestored(baseline);
                fixture.Focus(); observer.Reset(); Tap(68); Thread.Sleep(100);
                Check(observer.KeyboardEvents == 2);
            });

            Test("Partial hook installation failure leaves no keyboard hook behind", () =>
            {
                fixture.Focus();
                using var child = new ProtectedRun(appPath, fixture, 10, ["--test-hooks-failure"], expectReady: false);
                child.WaitForExit(6000); Check(child.Process.ExitCode == 1); CheckRestored(baseline);
                fixture.Focus(); observer.Reset(); Tap(69); Thread.Sleep(100);
                Check(observer.KeyboardEvents == 2);
            });

            Test("Smoke-test deadline exits even without an unlock gesture", () =>
            {
                using var child = new ProtectedRun(appPath, fixture, 5);
                child.WaitForExit(7500); CheckRestored(baseline);
            });

            Test("Windows session-lock notification exits and restores input", () =>
            {
                using var child = new ProtectedRun(appPath, fixture, 10);
                child.Process.Refresh();
                Check(child.Process.MainWindowHandle != 0, "No session-notification window.");
                Check(PostMessage(child.Process.MainWindowHandle, 0x2B1, 7, 0), "Could not simulate the OS lock notification.");
                child.WaitForExit(); Check(child.Process.ExitCode == 0); CheckRestored(baseline);
            });
        }
        finally
        {
            fixture.Focus(); ReleaseInjected();
            Native.RestoreClip(baseline, unrestricted);
            SetCursorPos(cursor.X, cursor.Y);
            if (previous != 0) Native.SetForegroundWindow(previous);
        }
    }

    private static bool ForegroundIsOurs()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        return pid == Environment.ProcessId;
    }

    private static void CheckRestored(Native.Rect baseline)
    {
        for (int i = 0; i < 30; i++)
        {
            Native.GetClipCursor(out var current);
            if (current == baseline) return;
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("Original cursor bounds were not restored.");
    }

    private static void Tap(int vk, bool extended = false) { Down(vk, extended); Up(vk, extended); }
    private static void Down(int vk, bool extended = false)
    {
        SendKey(vk, false, extended); InjectedDown.Add((vk, extended));
    }
    private static void Up(int vk, bool extended = false)
    {
        SendKey(vk, true, extended); InjectedDown.Remove((vk, extended));
    }
    private static void ReleaseInjected()
    {
        foreach (var key in InjectedDown.ToArray()) Up(key.Vk, key.Extended);
    }
    private static void SendKey(int vk, bool up, bool extended)
    {
        var input = new OsInput { Type = 1, Union = new InputUnion { Key = new KeyInput
            { Vk = (ushort)vk, Flags = (up ? 2u : 0u) | (extended ? 1u : 0u), Extra = Tag } } };
        Check(SendInput(1, [input], Marshal.SizeOf<OsInput>()) == 1, "SendInput failed.");
    }
    private static void Mouse(uint flags, uint data = 0)
    {
        var input = new OsInput { Type = 0, Union = new InputUnion { Mouse = new MouseInput
            { Flags = flags, Data = data, Extra = Tag } } };
        Check(SendInput(1, [input], Marshal.SizeOf<OsInput>()) == 1, "Mouse input injection failed.");
    }

    private sealed class ProtectedRun : IDisposable
    {
        internal Process Process { get; }
        private readonly SafetyFixture fixture;
        internal ProtectedRun(string app, SafetyFixture fixture, int seconds, params string[] extra)
            : this(app, fixture, seconds, extra, true) { }

        internal ProtectedRun(string app, SafetyFixture fixture, int seconds, string[] extra, bool expectReady)
        {
            this.fixture = fixture;
            fixture.Focus();
            string eventName = $"Local\\BabyKeyboard-Test-{Guid.NewGuid():N}";
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
            var info = new ProcessStartInfo(app)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            if (!System.IO.File.Exists(System.IO.Path.ChangeExtension(app, ".runtimeconfig.json")))
            {
                // Published single-file builds must run without relying on the development SDK environment.
                info.Environment.Remove("DOTNET_ROOT");
                info.Environment.Remove("DOTNET_ROOT_X64");
                info.Environment.Remove("DOTNET_HOST_PATH");
            }
            info.ArgumentList.Add("--smoke-test"); info.ArgumentList.Add(seconds.ToString());
            info.ArgumentList.Add("--ready-event"); info.ArgumentList.Add(eventName);
            foreach (string arg in extra) info.ArgumentList.Add(arg);
            Process = Process.Start(info) ?? throw new InvalidOperationException("Could not start test app.");
            if (!expectReady) return;
            try
            {
                for (int i = 0; i < 160; i++)
                {
                    if (ready.WaitOne(50)) return;
                    if (Process.HasExited) throw new InvalidOperationException($"App exited before ready: {Process.ExitCode}");
                }
                throw new TimeoutException("App did not become ready.");
            }
            catch { Dispose(); throw; }
        }

        internal void WaitForExit(int milliseconds = 4500)
        {
            Check(Process.WaitForExit(milliseconds), "App did not exit in time.");
            Thread.Sleep(200); // Let the independent guardian finish restoring cursor state.
        }
        public void Dispose()
        {
            fixture.Focus(); ReleaseInjected();
            if (!Process.HasExited) { Process.Kill(); Process.WaitForExit(3000); }
            Thread.Sleep(250);
            Process.Dispose();
        }
    }

    private sealed class SafetyFixture : IDisposable
    {
        private readonly Thread thread;
        private readonly ManualResetEventSlim ready = new(false);
        private Window? window;
        private bool allowClose;
        internal SafetyFixture()
        {
            thread = new Thread(() =>
            {
                window = new Window
                {
                    Title = "宝宝键盘 · 自动验收", Width = 580, Height = 170, ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new TextBlock { Text = "正在验证键鼠保护与自动解除。\n测试结束后会自动关闭。",
                        FontSize = 19, Margin = new Thickness(30), TextWrapping = TextWrapping.Wrap }
                };
                window.Closing += (_, e) => e.Cancel = !allowClose;
                window.Show(); ready.Set(); Dispatcher.Run();
            }) { IsBackground = true, Name = "Safe integration input target" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Check(ready.Wait(5000), "Test fixture did not start."); Focus();
        }
        internal void Focus()
        {
            window!.Dispatcher.Invoke(() =>
            {
                uint foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
                uint current = Native.GetCurrentThreadId();
                bool attached = foreground != current && AttachThreadInput(current, foreground, true);
                try { window.Activate(); Native.SetForegroundWindow(new WindowInteropHelper(window).Handle); }
                finally { if (attached) AttachThreadInput(current, foreground, false); }
            });
        }
        public void Dispose()
        {
            window!.Dispatcher.Invoke(() => { allowClose = true; window.Close(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send); });
            thread.Join(2000); ready.Dispose();
        }
    }

    private sealed class HookObserver : IDisposable
    {
        private readonly Thread thread;
        private readonly Native.HookProc keyboard, mouse;
        private readonly ManualResetEventSlim ready = new(false);
        private uint threadId;
        private int keyboardEvents, mouseEvents;
        internal int KeyboardEvents => Volatile.Read(ref keyboardEvents);
        internal int MouseEvents => Volatile.Read(ref mouseEvents);
        internal void Reset() { Interlocked.Exchange(ref keyboardEvents, 0); Interlocked.Exchange(ref mouseEvents, 0); }
        internal HookObserver()
        {
            keyboard = (code, wParam, lParam) =>
            {
                if (code >= 0 && Marshal.PtrToStructure<Native.KeyboardData>(lParam).Extra == Tag)
                    Interlocked.Increment(ref keyboardEvents);
                return Native.CallNextHookEx(0, code, wParam, lParam);
            };
            mouse = (code, wParam, lParam) =>
            {
                if (code >= 0 && (int)wParam != 0x200 && Marshal.PtrToStructure<Native.MouseData>(lParam).Extra == Tag)
                    Interlocked.Increment(ref mouseEvents);
                return Native.CallNextHookEx(0, code, wParam, lParam);
            };
            thread = new Thread(() =>
            {
                threadId = Native.GetCurrentThreadId();
                Native.PeekMessage(out _, 0, 0, 0, 0);
                nint kh = Native.SetWindowsHookEx(Native.WhKeyboardLl, keyboard, Native.GetModuleHandle(null), 0);
                nint mh = Native.SetWindowsHookEx(Native.WhMouseLl, mouse, Native.GetModuleHandle(null), 0);
                ready.Set();
                try
                {
                    while (Native.GetMessage(out var message, 0, 0, 0) > 0)
                    { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
                }
                finally { Native.UnhookWindowsHookEx(kh); Native.UnhookWindowsHookEx(mh); }
            }) { IsBackground = true, Name = "Downstream test observer" };
            thread.Start(); Check(ready.Wait(5000));
        }
        public void Dispose()
        {
            Native.PostThreadMessage(threadId, Native.WmQuit, 0, 0); thread.Join(2000); ready.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct OsInput { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KeyInput Key;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput
    {
        public ushort Vk, Scan; public uint Flags, Time; public nuint Extra;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    {
        public int X, Y; public uint Data, Flags, Time; public nuint Extra;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, OsInput[] inputs, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
}

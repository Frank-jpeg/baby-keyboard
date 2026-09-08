using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BabyKeyboard.Core;

namespace BabyKeyboard.App;

internal sealed class AppRuntime
{
    private readonly AppOptions options;
    private readonly Application application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    private readonly List<GuardWindow> windows = [];
    private readonly UnlockController previewUnlock = new();
    private readonly Stopwatch age = Stopwatch.StartNew();
    private readonly nint previousForeground = Native.GetForegroundWindow();
    private readonly DispatcherTimer pulseTimer;
    private InputInterceptor? input;
    private TonePlayer? audio;
    private GuardianClient? guardian;
    private Native.Rect originalClip;
    private bool unrestricted, capturedClip, stopped, displayUpdateQueued;
    private volatile bool active;
    private int exitRequested, exitCode;
    private long uiPulse = Stopwatch.GetTimestamp();
    private long reportedUiPulse, reportedInputPulse;
    private double lastFrame;

    internal AppRuntime(AppOptions options)
    {
        this.options = options;
        pulseTimer = new DispatcherTimer(DispatcherPriority.Send, application.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(100) };
        pulseTimer.Tick += (_, _) => PulseUi();
        application.Startup += (_, _) => Start();
        application.Exit += (_, _) => Stop();
        application.SessionEnding += (_, _) => RequestExit();
        application.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            exitCode = 1;
            RequestExit();
        };
    }

    internal int Run()
    {
        try { application.Run(); }
        finally { Stop(); }
        return exitCode;
    }

    private void Start()
    {
        try
        {
            var monitors = Native.Monitors();
            if (!options.Preview)
            {
                capturedClip = Native.GetClipCursor(out originalClip);
                if (!capturedClip) throw new InvalidOperationException("无法保存光标状态。");
                unrestricted = originalClip == Native.VirtualBounds;
                guardian = new GuardianClient(originalClip, unrestricted, Healthy,
                    () => { exitCode = 1; RequestExit(); }, options.TimeoutSeconds);
            }
            audio = new TonePlayer(options.AudioFailure);
            foreach (var monitor in options.Preview ? monitors.Take(1) : monitors)
            {
                var window = MakeWindow(monitor);
                windows.Add(window);
                window.Show();
            }
            if (options.Preview) InstallPreviewInput(windows[0]);
            else
            {
                input = new InputInterceptor { SimulateMouseHookFailure = options.HooksFailure };
                input.Unlocked += () => RequestExit();
                input.Failed += _ => { exitCode = 1; RequestExit(); };
                input.Start();
                RestrictCursor(monitors[0]);
            }
            active = true;
            Interlocked.Exchange(ref uiPulse, Stopwatch.GetTimestamp());
            pulseTimer.Start();
            CompositionTarget.Rendering += Render;
            windows[0].Activate();
            if (options.ReadyEvent is not null)
            {
                using var ready = EventWaitHandle.OpenExisting(options.ReadyEvent);
                ready.Set();
            }
        }
        catch (Exception ex)
        {
            exitCode = 1;
            Stop();
            if (!options.Test)
                MessageBox.Show("未能进入保护模式，电脑输入已恢复。\n\n" + ex.Message,
                    "宝宝键盘", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private GuardWindow MakeWindow(Native.Monitor monitor)
    {
        var window = new GuardWindow(monitor, options.Preview, () => RequestExit(), QueueDisplayUpdate);
        if (options.Preview) window.Closed += (_, _) => RequestExit();
        return window;
    }

    private bool Healthy()
    {
        if (!active) return age.Elapsed.TotalSeconds < 5;
        long now = Stopwatch.GetTimestamp();
        long ui = Interlocked.Read(ref uiPulse), hook = input?.LastPulse ?? ui;
        if (ui == reportedUiPulse || hook == reportedInputPulse) return false;
        reportedUiPulse = ui;
        reportedInputPulse = hook;
        return Stopwatch.GetElapsedTime(ui, now).TotalMilliseconds < 1000 &&
            Stopwatch.GetElapsedTime(hook, now).TotalMilliseconds < 1000;
    }

    private void PulseUi()
    {
        Interlocked.Exchange(ref uiPulse, Stopwatch.GetTimestamp());
        if (options.Test && age.Elapsed.TotalSeconds >= options.TimeoutSeconds) { RequestExit(); return; }
        // Windows may clear a process's clip when another window is activated (including notifications).
        // Buttons remain globally intercepted during this interval; promptly restore the cursor boundary.
        if (active && !options.Preview && windows.Count > 0)
        {
            var expected = windows[0].Monitor.Bounds.Inset(8);
            if (!Native.GetClipCursor(out var current) || current != expected)
            {
                if (!Native.ClipCursor(ref expected)) { exitCode = 1; RequestExit(); return; }
            }
        }
        if (options.UiHangAfterMs > 0 && age.ElapsedMilliseconds >= options.UiHangAfterMs)
            Thread.Sleep(Timeout.Infinite);
        if (options.InputHangAfterMs > 0 && age.ElapsedMilliseconds >= options.InputHangAfterMs)
            input?.SimulateHang();
    }

    private void Render(object? sender, EventArgs e)
    {
        double now = age.Elapsed.TotalSeconds;
        if (now - lastFrame < 1.0 / 65 || stopped) return;
        lastFrame = now;
        int budget = 32;
        while (budget-- > 0 && input is not null && input.TryRead(out var signal))
        {
            foreach (var window in windows)
            {
                if (signal.Click)
                {
                    var r = window.Monitor.Bounds;
                    if (signal.X >= r.Left && signal.X < r.Right && signal.Y >= r.Top && signal.Y < r.Bottom)
                        window.Scene.AddClick(window.PointFromScreen(new Point(signal.X, signal.Y)));
                }
                else window.Scene.AddKey(signal.VirtualKey);
            }
            audio?.Play(signal.VirtualKey);
        }
        InputSnapshot snapshot;
        if (options.Preview)
        {
            double ms = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
            previewUnlock.Tick(ms);
            snapshot = new(previewUnlock.Phase, previewUnlock.Progress(ms), previewUnlock.HeldCount);
            if (previewUnlock.Phase == UnlockPhase.Complete) RequestExit();
        }
        else snapshot = input?.Snapshot ?? new(UnlockPhase.Playing, 0, 0);
        foreach (var window in windows)
        {
            window.Scene.Input = snapshot;
            window.Scene.SoundAvailable = audio?.Available == true;
            window.Scene.Advance();
        }
    }

    private void InstallPreviewInput(GuardWindow window)
    {
        static double Now() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
        window.PreviewKeyDown += (_, e) =>
        {
            int key = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
            if (key == 0x1B) { RequestExit(); return; }
            if (previewUnlock.KeyDown(new(key), Now())) { window.Scene.AddKey(key); audio?.Play(key); }
            e.Handled = true;
        };
        window.PreviewKeyUp += (_, e) =>
        {
            previewUnlock.KeyUp(new(KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key)), Now());
            e.Handled = true;
        };
        window.PreviewMouseDown += (_, e) =>
        {
            var button = e.ChangedButton == MouseButton.Left ? PointerButton.Left : PointerButton.Right;
            previewUnlock.ButtonDown(button, Now());
            window.Scene.AddClick(e.GetPosition(window.Scene));
            audio?.Play(0);
        };
        window.PreviewMouseUp += (_, e) => previewUnlock.ButtonUp(
            e.ChangedButton == MouseButton.Left ? PointerButton.Left : PointerButton.Right, Now());
    }

    private static void RestrictCursor(Native.Monitor monitor)
    {
        var bounds = monitor.Bounds.Inset(8);
        if (!Native.ClipCursor(ref bounds)) throw new InvalidOperationException("无法限制鼠标区域。");
    }

    private void QueueDisplayUpdate()
    {
        if (stopped || displayUpdateQueued || options.Preview) return;
        displayUpdateQueued = true;
        application.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            displayUpdateQueued = false;
            if (stopped) return;
            try
            {
                var monitors = Native.Monitors();
                // Rebuild on a topology change so each monitor has exactly one correctly scaled window.
                if (monitors.Count != windows.Count || monitors.Where((m, i) => m.Bounds != windows[i].Monitor.Bounds).Any())
                {
                    foreach (var window in windows) { window.AllowClose = true; window.Close(); }
                    windows.Clear();
                    foreach (var monitor in monitors)
                    {
                        var window = MakeWindow(monitor);
                        windows.Add(window); window.Show();
                    }
                }
                else for (int i = 0; i < monitors.Count; i++) windows[i].FitToMonitor(monitors[i]);
                RestrictCursor(monitors[0]);
            }
            catch { exitCode = 1; RequestExit(); }
        }));
    }

    private void RequestExit()
    {
        if (Interlocked.Exchange(ref exitRequested, 1) != 0 || application.Dispatcher.HasShutdownStarted) return;
        application.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Stop));
    }

    private void Stop()
    {
        if (stopped) return;
        stopped = true;
        pulseTimer.Stop();
        CompositionTarget.Rendering -= Render;
        try { input?.Dispose(); }
        finally
        {
            if (capturedClip) Native.RestoreClip(originalClip, unrestricted);
            audio?.Dispose();
            foreach (var window in windows.ToArray())
            {
                window.AllowClose = true;
                try { window.Close(); } catch (InvalidOperationException) { }
            }
            windows.Clear();
            guardian?.Dispose();
            if (!application.Dispatcher.HasShutdownStarted) application.Shutdown(exitCode);
            if (previousForeground != 0) Native.SetForegroundWindow(previousForeground);
        }
    }
}

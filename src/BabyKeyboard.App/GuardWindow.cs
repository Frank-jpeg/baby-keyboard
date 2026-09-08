using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BabyKeyboard.Core;

namespace BabyKeyboard.App;

internal sealed class GuardWindow : Window
{
    private readonly Action sessionEnded, displaysChanged;
    private readonly bool protectedWindow;
    private HwndSource? source;
    private bool registered;
    internal SceneView Scene { get; } = new();
    internal Native.Monitor Monitor { get; private set; }
    internal bool AllowClose { get; set; }
    internal nint Handle => new WindowInteropHelper(this).Handle;
    internal Action<PhysicalKey, bool>? PreviewKeyboard { get; set; }
    internal Func<int, long>? TestQuery { get; init; }

    internal GuardWindow(Native.Monitor monitor, bool preview, Action sessionEnded, Action displaysChanged)
    {
        Monitor = monitor;
        this.sessionEnded = sessionEnded;
        this.displaysChanged = displaysChanged;
        protectedWindow = !preview;
        Title = preview ? "宝宝键盘 · 画面预览" : "宝宝键盘";
        Content = Scene;
        Scene.Preview = preview;
        Background = System.Windows.Media.Brushes.Black;
        WindowStartupLocation = preview ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual;
        Width = preview ? 1280 : monitor.Bounds.Width;
        Height = preview ? 800 : monitor.Bounds.Height;
        if (!preview)
        {
            Left = monitor.Bounds.Left; Top = monitor.Bounds.Top;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            Topmost = true; ShowInTaskbar = monitor.Primary; ShowActivated = monitor.Primary;
        }
        SourceInitialized += (_, _) => InitializeSource();
        Loaded += (_, _) => { if (protectedWindow) FitToMonitor(Monitor); };
        Closing += (_, e) => { if (protectedWindow && !AllowClose) e.Cancel = true; };
        Closed += (_, _) =>
        {
            if (registered) Native.WTSUnRegisterSessionNotification(Handle);
            source?.RemoveHook(WindowMessages);
        };
    }

    private void InitializeSource()
    {
        source = HwndSource.FromHwnd(Handle);
        source.AddHook(WindowMessages);
        if (protectedWindow && Monitor.Primary)
        {
            registered = Native.WTSRegisterSessionNotification(Handle, 0);
            if (!registered) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法启用锁屏应急解除。");
        }
    }

    internal void FitToMonitor(Native.Monitor monitor)
    {
        Monitor = monitor;
        var r = monitor.Bounds;
        if (!Native.SetWindowPos(Handle, -1, r.Left, r.Top, r.Width, r.Height, 0x10 | 0x40))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法覆盖显示器。");
    }

    private nint WindowMessages(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x8031 && TestQuery is not null)
        {
            handled = true;
            return (nint)TestQuery((int)wParam);
        }
        if (!protectedWindow && PreviewKeyboard is not null &&
            message is Native.WmKeyDown or Native.WmKeyUp or Native.WmSysKeyDown or Native.WmSysKeyUp)
        {
            var key = new PhysicalKey((int)wParam, (int)((lParam.ToInt64() >> 16) & 0xFF), (lParam.ToInt64() & (1 << 24)) != 0);
            PreviewKeyboard(key, message is Native.WmKeyDown or Native.WmSysKeyDown);
            handled = true;
            return 0;
        }
        if (!protectedWindow) return 0;
        if (message == 0x2B1 && (int)wParam is 2 or 4 or 6 or 7) sessionEnded();
        else if (message == 0x218 && (int)wParam == 4) sessionEnded();
        else if (message == 0x7E || message == 0x2E0) displaysChanged();
        return 0;
    }
}

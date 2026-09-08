using System.Runtime.InteropServices;

namespace BabyKeyboard.App;

internal static class Native
{
    internal const int WhKeyboardLl = 13, WhMouseLl = 14;
    internal const uint WmQuit = 0x12, WmTimer = 0x113;
    internal const int WmKeyDown = 0x100, WmKeyUp = 0x101, WmSysKeyDown = 0x104, WmSysKeyUp = 0x105;
    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    internal delegate bool MonitorProc(nint monitor, nint dc, ref Rect rect, nint data);

    [StructLayout(LayoutKind.Sequential)]
    internal record struct Point(int X, int Y);
    [StructLayout(LayoutKind.Sequential)]
    internal record struct Rect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public Rect Inset(int margin) => new(Left + margin, Top + margin, Right - margin, Bottom - margin);
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardData { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseData { public Point Position; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public nint Hwnd; public uint Id; public nuint WParam; public nint LParam;
        public uint Time; public Point Position; public uint Private;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size; public Rect Bounds, Work; public uint Flags;
    }
    internal record Monitor(Rect Bounds, bool Primary);

    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int kind, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] internal static extern uint SetErrorMode(uint mode);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PeekMessage(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nuint SetTimer(nint window, nuint id, uint interval, nint proc);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool KillTimer(nint window, nuint id);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClipCursor(ref Rect rect);
    [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReleaseClip(nint rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClipCursor(out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumDisplayMonitors(nint dc, nint rect, MonitorProc proc, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
    [DllImport("wtsapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WTSUnRegisterSessionNotification(nint hwnd);

    internal static List<Monitor> Monitors()
    {
        List<Monitor> result = [];
        MonitorProc callback = (nint handle, nint dc, ref Rect rect, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(handle, ref info)) result.Add(new(info.Bounds, (info.Flags & 1) != 0));
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0) || result.Count == 0)
            throw new InvalidOperationException("无法获取显示器信息。");
        return result.OrderByDescending(m => m.Primary).ToList();
    }

    internal static Rect VirtualBounds => new(GetSystemMetrics(76), GetSystemMetrics(77),
        GetSystemMetrics(76) + GetSystemMetrics(78), GetSystemMetrics(77) + GetSystemMetrics(79));

    internal static void RestoreClip(Rect original, bool wasUnrestricted)
    {
        if (wasUnrestricted) ReleaseClip(0);
        else ClipCursor(ref original);
    }
}

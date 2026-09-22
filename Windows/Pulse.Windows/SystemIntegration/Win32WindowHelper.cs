namespace Pulse.Windows.SystemIntegration;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

public static class Win32WindowHelper
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const int MA_NOACTIVATEANDEAT = 4;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Configures window to be a floating non-activating tool window that never steals focus.
    /// </summary>
    public static void ApplyFloatingWindowStyles(Window window)
    {
        var helper = new WindowInteropHelper(window);
        var hWnd = helper.EnsureHandle();

        var exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));

        // Tell DWM explicitly to NEVER round or draw composition border/shadow on this transparent overlay window
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_DONOTROUND = 1;
        int doNotRound = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref doNotRound, sizeof(int));

        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

        var source = HwndSource.FromHwnd(hWnd);
        source?.AddHook(WndProc);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    /// <summary>
    /// Applies Windows 11 immersive dark mode and rounded corners to the window frame.
    /// </summary>
    public static void EnableImmersiveDarkMode(Window window)
    {
        if (window.AllowsTransparency) return; // Never apply DWM window framing/shadows to transparent layered windows

        var helper = new WindowInteropHelper(window);
        var hWnd = helper.EnsureHandle();

        int trueVal = 1;
        if (DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueVal, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref trueVal, sizeof(int));
        }

        int roundVal = DWMWCP_ROUND;
        DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundVal, sizeof(int));
    }

    /// <summary>
    /// Disables Windows 11 immersive dark mode for light themes.
    /// </summary>
    public static void DisableImmersiveDarkMode(Window window)
    {
        if (window.AllowsTransparency) return; // Never apply to transparent layered windows

        var helper = new WindowInteropHelper(window);
        var hWnd = helper.EnsureHandle();

        int falseVal = 0;
        if (DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref falseVal, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref falseVal, sizeof(int));
        }
    }

    [DllImport("user32.dll")]
    public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Forces the window into the foreground and restores it if minimized.
    /// </summary>
    public static void BringToFront(Window window)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            var hWnd = helper.EnsureHandle();
            SwitchToThisWindow(hWnd, true);
            SetForegroundWindow(hWnd);
        }
        catch { }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }
}

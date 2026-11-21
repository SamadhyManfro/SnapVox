using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SnapVox.Platform.Windows;

/// <summary>Window tweaks that Avalonia doesn't expose.</summary>
[SupportedOSPlatform("windows")]
internal static partial class WinWindowStyle
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    private const uint MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000;

    /// <summary>Lets clicks and scrolling go through the window to the app underneath.</summary>
    public static void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        long style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        style = enabled ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED : style & ~WS_EX_TRANSPARENT;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (nint)style);
    }

    /// <summary>Hides the window from Alt+Tab and (optionally) keeps it from stealing focus when clicked.</summary>
    public static void SetToolWindow(IntPtr hwnd, bool noActivate)
    {
        long style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        if (noActivate)
            style |= WS_EX_NOACTIVATE;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (nint)style);
    }

    /// <summary>
    /// Leaves the window out of screenshots (Windows 10 2004+). Our own dimming/island never show up in the
    /// screen capture, so a new selection always reads what is really on screen — even after scrolling.
    /// </summary>
    public static void ExcludeFromCapture(IntPtr hwnd) => SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

    /// <summary>Puts the window on top of the other "always on top" windows without activating it.</summary>
    public static void BringToTop(IntPtr hwnd)
    {
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        SetWindowPos(hwnd, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Sends a mouse-wheel turn to whatever is under the cursor (vertical or horizontal).</summary>
    public static void SendWheel(int delta, bool horizontal) =>
        mouse_event(horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, 0, 0, delta, UIntPtr.Zero);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extraInfo);

    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(IntPtr hWnd, int index);
    [LibraryImport("user32.dll")] private static partial nint SetWindowLongPtrW(IntPtr hWnd, int index, nint value);
}

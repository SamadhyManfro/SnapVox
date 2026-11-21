using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace SnapVox.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static partial class WinCursor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    public static PixelPoint GetPosition() =>
        GetCursorPos(out var p) ? new PixelPoint(p.X, p.Y) : new PixelPoint(0, 0);
}

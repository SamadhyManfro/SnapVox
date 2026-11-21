using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace SnapVox.Platform.Windows;

/// <summary>Captures every monitor at once with GDI (BitBlt). Fast and dependency-free.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WinScreenCapture : IScreenCapture
{
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    public ScreenSnapshot CaptureAll()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN), h = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = CreateCompatibleBitmap(screenDc, w, h);
        IntPtr old = SelectObject(memDc, hBmp);
        try
        {
            BitBlt(memDc, 0, 0, w, h, screenDc, x, y, SRCCOPY | CAPTUREBLT);
            SelectObject(memDc, old);

            var info = new BITMAPINFOHEADER
            {
                biSize = 40, // size of the header only (without the color table)
                biWidth = w,
                biHeight = -h, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            var pixels = new byte[w * h * 4];
            unsafe
            {
                fixed (byte* p = pixels)
                    GetDIBits(memDc, hBmp, 0, (uint)h, p, ref info, 0);
            }

            return new ScreenSnapshot { VirtualBounds = new PixelRect(x, y, w, h), Pixels = pixels };
        }
        finally
        {
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
        // BITMAPINFO has a color table after this; with 32 bits and BI_RGB it isn't used.
        public uint bmiColors;
    }

    [LibraryImport("user32.dll")] private static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll")] private static partial IntPtr GetDC(IntPtr hWnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [LibraryImport("gdi32.dll")] private static partial IntPtr CreateCompatibleDC(IntPtr hDc);
    [LibraryImport("gdi32.dll")] private static partial IntPtr CreateCompatibleBitmap(IntPtr hDc, int w, int h);
    [LibraryImport("gdi32.dll")] private static partial IntPtr SelectObject(IntPtr hDc, IntPtr obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(IntPtr obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteDC(IntPtr hDc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte* bits, ref BITMAPINFOHEADER info, uint usage);
}

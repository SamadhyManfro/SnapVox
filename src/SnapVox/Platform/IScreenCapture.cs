using Avalonia;
using Avalonia.Media.Imaging;

namespace SnapVox.Platform;

/// <summary>
/// Capture of the whole screen (all monitors).
/// BGRA pixels, in physical coordinates of the virtual desktop.
/// </summary>
public sealed class ScreenSnapshot
{
    public required PixelRect VirtualBounds { get; init; }
    public required byte[] Pixels { get; init; }

    public int Stride => VirtualBounds.Width * 4;

    /// <summary>Crops a rectangle (in physical screen pixels) as raw BGRA pixels.</summary>
    public (byte[] Pixels, int Width, int Height) CropBgra(PixelRect region)
    {
        var r = region.Intersect(VirtualBounds);
        if (r.Width <= 0 || r.Height <= 0)
            throw new ArgumentException("Region is outside the screen.", nameof(region));

        var dst = new byte[r.Width * r.Height * 4];
        int srcX = r.X - VirtualBounds.X, srcY = r.Y - VirtualBounds.Y, rowBytes = r.Width * 4;
        for (int y = 0; y < r.Height; y++)
            Buffer.BlockCopy(Pixels, (srcY + y) * Stride + srcX * 4, dst, y * rowBytes, rowBytes);
        return (dst, r.Width, r.Height);
    }

    /// <summary>Creates a bitmap from BGRA pixels.</summary>
    public static WriteableBitmap ToBitmap(byte[] bgra, int width, int height)
    {
        var bmp = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using var fb = bmp.Lock();
        for (int y = 0; y < height; y++)
            System.Runtime.InteropServices.Marshal.Copy(bgra, y * width * 4, fb.Address + y * fb.RowBytes, width * 4);
        return bmp;
    }
}

public interface IScreenCapture
{
    ScreenSnapshot CaptureAll();
}

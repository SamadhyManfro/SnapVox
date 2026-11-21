using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SnapVox.UI;

/// <summary>
/// Draws the SnapVox logo in code (lines of text with one word highlighted in blue,
/// no text inside). 46×46 unit grid.
/// </summary>
internal static class LogoRenderer
{
    private static readonly IBrush Tile = new SolidColorBrush(Color.Parse("#0B0B0D"));
    private static readonly IBrush Line = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#4DA3FF"));

    public static Bitmap Render(int size)
    {
        var bmp = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            double s = size / 46.0;
            using (ctx.PushTransform(Matrix.CreateScale(s, s)))
            {
                ctx.DrawRectangle(Tile, null, new RoundedRect(new Rect(0, 0, 46, 46), 13));
                ctx.DrawRectangle(Line, null, new RoundedRect(new Rect(10, 13, 26, 3), 1.5));
                ctx.DrawRectangle(Line, null, new RoundedRect(new Rect(10, 21.5, 9, 3), 1.5));
                ctx.DrawRectangle(Accent, null, new RoundedRect(new Rect(21, 19, 15, 8), 2.5));
                ctx.DrawRectangle(Line, null, new RoundedRect(new Rect(10, 30, 19, 3), 1.5));
            }
        }
        return bmp;
    }

    public static WindowIcon CreateWindowIcon(int size) => new(Render(size));
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SnapVox.UI;

namespace SnapVox.DevTools;

/// <summary>
/// Visual test without touching the user's screen: draws the overlay into PNG images.
/// Usage: SnapVox.exe --render-test &lt;folder&gt;
/// Produces: 1_dim.png (dimmed only), 2_drag.png (dragging), 3_frozen.png (frozen photo).
/// Test background: white, so the dimming can be measured.
/// </summary>
internal static class RenderTest
{
    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var size = new Size(800, 500);
        var selection = new Rect(200, 150, 400, 200);

        // Test "frozen" photo: light blue, to tell it apart from the white background.
        var snap = new RenderTargetBitmap(new PixelSize(400, 200));
        using (var c = snap.CreateDrawingContext())
            c.DrawRectangle(new SolidColorBrush(Color.Parse("#CFE6FF")), null, new Rect(0, 0, 400, 200));

        Save(outDir, "1_dim.png", size, s => { });
        Save(outDir, "2_drag.png", size, s => s.SetSelectionForTest(selection));
        Save(outDir, "3_frozen.png", size, s => { s.SetSelectionForTest(selection); s.Freeze(snap); });
        return 0;
    }

    private static void Save(string dir, string name, Size size, Action<SelectionOverlay.OverlaySurface> setup)
    {
        var surface = new SelectionOverlay.OverlaySurface();
        setup(surface);

        // White background simulating the screen, with the overlay on top.
        var root = new Panel
        {
            Width = size.Width,
            Height = size.Height,
            Background = Brushes.White,
            Children = { surface },
        };
        root.Measure(size);
        root.Arrange(new Rect(size));

        using var bmp = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
        bmp.Render(root);
        bmp.Save(Path.Combine(dir, name));
    }
}

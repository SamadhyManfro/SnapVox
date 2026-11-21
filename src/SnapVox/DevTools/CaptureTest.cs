using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SnapVox.Platform;

namespace SnapVox.DevTools;

/// <summary>
/// Checks that our windows are left out of the screen capture: shows a red square (with and without the
/// exclusion), captures the screen and reports the pixel color. Usage: SnapVox.exe --capture-test &lt;file&gt;
/// </summary>
internal static class CaptureTest
{
    public static int Run(string reportPath)
    {
        var lines = new List<string>();
        foreach (bool exclude in new[] { false, true })
        {
            var w = new Window
            {
                WindowDecorations = Avalonia.Controls.WindowDecorations.None,
                Background = Brushes.Red,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Width = 300,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint(200, 200),
            };
            w.Opened += (_, _) => { if (exclude) PlatformServices.ExcludeFromCapture(w); };
            w.Show();
            for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(50); }

            var shot = PlatformServices.CreateScreenCapture().CaptureAll();
            var (px, _, _) = shot.CropBgra(new PixelRect(350, 350, 1, 1));
            lines.Add($"exclude={exclude}: R={px[2]} G={px[1]} B={px[0]} → {(px[2] > 200 && px[1] < 60 && px[0] < 60 ? "RED (visible in capture)" : "not in capture")}");
            w.Close();
            Dispatcher.UIThread.RunJobs();
        }
        File.WriteAllLines(reportPath, lines);
        return 0;
    }
}

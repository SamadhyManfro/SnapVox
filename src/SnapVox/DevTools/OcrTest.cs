using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SnapVox.Ocr;
using SnapVox.Platform;

namespace SnapVox.DevTools;

/// <summary>
/// OCR + language test without touching the screen: renders images with known texts, recognizes them and writes a report.
/// Usage: SnapVox.exe --ocr-test &lt;folder&gt;   →   &lt;folder&gt;\ocr-report.txt
/// </summary>
internal static class OcrTest
{
    private static readonly (string Name, string Text, double FontSize)[] Samples =
    [
        ("pt", "A nova atualização do sistema traz melhorias de desempenho e recursos de acessibilidade. Agora é possível ouvir qualquer texto da tela.", 15),
        ("en", "The new system update brings performance improvements and accessibility features. Now you can listen to any text on the screen.", 15),
        ("es", "La nueva actualización del sistema trae mejoras de rendimiento y funciones de accesibilidad. Ahora puedes escuchar cualquier texto.", 15),
        ("fr", "La nouvelle mise à jour du système apporte des améliorations de performance et des fonctions d'accessibilité.", 15),
        ("pt-small", "Texto pequeno de interface: configurações, ações, atenção e informação não são problema.", 11),
    ];

    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var recognizer = new TextRecognizer(PlatformServices.CreateOcrEngine(), PlatformServices.CreateLanguageDetector());
        var report = new StringBuilder();
        report.AppendLine("OCR instalados: " + string.Join(", ", recognizer.OcrLanguages));

        foreach (var (name, text, fontSize) in Samples)
        {
            var (pixels, w, h) = Render(text, fontSize, Path.Combine(outDir, $"ocr_{name}.png"));
            var result = recognizer.RecognizeAsync(pixels, w, h).GetAwaiter().GetResult();
            int boxesInside = result.Words.Count(wd => wd.Box.X >= 0 && wd.Box.Y >= 0 && wd.Box.Right <= w + 1 && wd.Box.Bottom <= h + 1);

            report.AppendLine();
            report.AppendLine($"[{name}] detected language={result.LanguageCode} · OCR used={result.OcrLanguageTag} · " +
                              $"{result.WordCount} words · boxes inside the image={boxesInside}/{result.WordCount}");
            report.AppendLine("  esperado:   " + text);
            report.AppendLine("  reconhecido: " + result.Text.Replace(Environment.NewLine, " "));
            if (result.Words.FirstOrDefault() is { } firstWord)
                report.AppendLine($"  1st word \"{firstWord.Text}\" at {firstWord.Box}");
        }

        File.WriteAllText(Path.Combine(outDir, "ocr-report.txt"), report.ToString());
        return 0;
    }

    /// <summary>Draws the text on a white background (like a page) and returns the BGRA pixels.</summary>
    private static (byte[], int, int) Render(string text, double fontSize, string pngPath)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.Parse("#202124")),
            TextWrapping = TextWrapping.Wrap,
            Width = 520,
            Margin = new Thickness(16),
        };
        var root = new Border { Background = Brushes.White, Child = block };
        root.Measure(Size.Infinity);
        root.Arrange(new Rect(root.DesiredSize));

        var size = new PixelSize((int)Math.Ceiling(root.DesiredSize.Width), (int)Math.Ceiling(root.DesiredSize.Height));
        using var bmp = new RenderTargetBitmap(size);
        bmp.Render(root);
        bmp.Save(pngPath);

        int stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        return (pixels, size.Width, size.Height);
    }
}

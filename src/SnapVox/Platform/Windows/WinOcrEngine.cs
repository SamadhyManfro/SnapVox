using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using Avalonia;
using SnapVox.Ocr;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using OcrLine = SnapVox.Ocr.OcrLine;
using OcrWord = SnapVox.Ocr.OcrWord;
using WinLanguage = Windows.Globalization.Language;

namespace SnapVox.Platform.Windows;

/// <summary>Native Windows OCR (the same as PowerToys Text Extractor): free, offline and fast.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WinOcrEngine : IOcrEngine
{
    // Queried every time: a language may have been installed from Settings without restarting the app.
    public IReadOnlyList<string> AvailableLanguages =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    public async Task<OcrOutput> RecognizeAsync(byte[] bgra, int width, int height, string? languageTag)
    {
        var engine = languageTag is null
            ? OcrEngine.TryCreateFromUserProfileLanguages()
            : OcrEngine.TryCreateFromLanguage(new WinLanguage(languageTag));
        engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
            throw new InvalidOperationException("No OCR language installed in Windows.");

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            bgra.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(bitmap).AsTask().ConfigureAwait(false);

        var lines = result.Lines
            .Select(line => new OcrLine(line.Words
                .Select(w => new OcrWord(w.Text, new Rect(w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height)))
                .ToList()))
            .Where(l => l.Words.Count > 0)
            .ToList();

        return new OcrOutput(lines, LanguageCode: null, OcrLanguageTag: engine.RecognizerLanguage.LanguageTag);
    }
}

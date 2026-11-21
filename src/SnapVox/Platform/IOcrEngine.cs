using SnapVox.Ocr;

namespace SnapVox.Platform;

/// <summary>Text recognition (OCR). Image in BGRA, 4 bytes per pixel.</summary>
public interface IOcrEngine
{
    /// <summary>Installed OCR languages (BCP-47 tags, e.g. "pt-BR", "en-US").</summary>
    IReadOnlyList<string> AvailableLanguages { get; }

    /// <param name="languageTag">Recognizer language; null = the user's profile language.</param>
    Task<OcrOutput> RecognizeAsync(byte[] bgra, int width, int height, string? languageTag);
}

/// <summary>Detects the language of a text.</summary>
public interface ILanguageDetector
{
    /// <summary>Most likely 2-letter ISO code (e.g. "pt"), or null if it couldn't tell.</summary>
    string? Detect(string text) => DetectAll(text).FirstOrDefault();

    /// <summary>Candidates from most to least likely (2-letter codes, no repeats).</summary>
    IReadOnlyList<string> DetectAll(string text);
}

using Avalonia;
using SnapVox.Platform;

namespace SnapVox.Ocr;

/// <summary>
/// Puts the text recognition together:
/// 1) upscales the image 2× when it is small (the OCR makes fewer mistakes on screen text);
/// 2) recognizes with the Windows profile language;
/// 3) detects the text's language;
/// 4) if the detected language has its own OCR installed and it is different, recognizes again with it (better accents).
/// </summary>
internal sealed partial class TextRecognizer
{
    private const int MaxUpscaledSide = 4000;
    private const int MinCharsToDetect = 12;

    private readonly IOcrEngine _ocr;
    private readonly ILanguageDetector _detector;

    public TextRecognizer(IOcrEngine ocr, ILanguageDetector detector)
    {
        _ocr = ocr;
        _detector = detector;
    }

    public IReadOnlyList<string> OcrLanguages => _ocr.AvailableLanguages;

    public async Task<OcrOutput> RecognizeAsync(byte[] bgra, int width, int height)
    {
        int scale = Math.Max(width, height) * 2 <= MaxUpscaledSide ? 2 : 1;
        var (pixels, w, h) = scale == 2 ? Upscale2x(bgra, width, height) : (bgra, width, height);

        var first = await _ocr.RecognizeAsync(pixels, w, h, languageTag: null).ConfigureAwait(false);
        var result = first;

        string? lang = first.Text.Length >= MinCharsToDetect ? DetectLanguage(first.Text, Prefix(first.OcrLanguageTag)) : null;
        lang ??= Prefix(first.OcrLanguageTag);

        // Recognize again with the OCR of the detected language, if it exists and is different.
        var better = _ocr.AvailableLanguages.FirstOrDefault(t => Prefix(t) == lang);
        if (lang is not null && better is not null && Prefix(first.OcrLanguageTag) != lang)
        {
            var second = await _ocr.RecognizeAsync(pixels, w, h, better).ConfigureAwait(false);
            if (second.WordCount >= first.WordCount * 0.8)
                result = second;
        }

        // Boxes go back to the original size of the selection.
        var lines = result.Lines
            .Select(l => new OcrLine(l.Words.Select(wd => wd with { Box = Divide(wd.Box, scale) }).ToList()))
            .ToList();

        return new OcrOutput(lines, lang, result.OcrLanguageTag);
    }

    private static string? Prefix(string? tag) => tag?.Split('-')[0].ToLowerInvariant();

    /// <summary>
    /// Detects the language with two safeguards:
    /// 1) strips symbols (•, →, emojis…) that confuse the detector — with them it sometimes gave no answer at all;
    /// 2) when only the English OCR exists, text in another language comes out with garbled accents ("padräo") and the
    ///    detector thinks it's English. If the "English" text has letters English doesn't use, take the next
    ///    candidate — preferably a language the user uses (chosen voice, installed language or system language).
    /// </summary>
    private string? DetectLanguage(string text, string? ocrLanguage)
    {
        string clean = SymbolsRegex().Replace(text, " ");
        var candidates = _detector.DetectAll(clean);
        if (candidates.Count == 0)
        {
            // Accents garbled by the OCR ("padräo") sometimes make the detector give up: try without accents.
            candidates = _detector.DetectAll(StripAccents(clean));
        }
        if (candidates.Count == 0)
        {
            // The Windows detector gives no answer for short texts / texts full of names. Plan B:
            // common words of each language; and, if the English OCR garbled accents, a language the user uses.
            if (StopwordGuess(clean) is { } guess)
                return guess;
            if (ocrLanguage == "en" && NonEnglishLetters(clean) >= 2)
            {
                var preferred = PreferredLanguages();
                preferred.Remove("en");
                return Settings.AppSettings.Current.LastVoiceLanguage is { } last && preferred.Contains(last)
                    ? last
                    : preferred.FirstOrDefault();
            }
            return null;
        }

        string top = candidates[0];
        if (top == "en" && ocrLanguage == "en" && NonEnglishLetters(clean) >= 2)
        {
            // Only switch to a language the user uses; otherwise keep English (e.g. "café, résumé" in English text).
            var preferred = PreferredLanguages();
            preferred.Remove("en");
            return candidates.Skip(1).FirstOrDefault(preferred.Contains) ?? top;
        }
        return top;
    }

    /// <summary>Diagnostics only (--detect).</summary>
    internal string? DetectForTest(string text, string ocrLanguage) => DetectLanguage(text, ocrLanguage);

    /// <summary>Very common words per language (no accents, lowercase).</summary>
    private static readonly Dictionary<string, HashSet<string>> Stopwords = new()
    {
        ["pt"] = ["de", "do", "da", "dos", "das", "que", "para", "com", "nao", "uma", "um", "os", "as", "em", "no", "na", "por", "se", "mais", "e", "ou", "voce", "isso", "esta", "sao", "tambem", "como", "ao", "pelo", "pela"],
        ["en"] = ["the", "and", "of", "to", "is", "in", "that", "for", "with", "on", "you", "this", "are", "it", "be", "as", "at", "or", "by", "from", "your", "can", "will", "an", "not"],
        ["es"] = ["el", "la", "los", "las", "que", "de", "y", "en", "un", "una", "por", "con", "para", "es", "del", "al", "lo", "como", "pero", "su", "sus", "mas", "esta"],
        ["fr"] = ["le", "la", "les", "et", "des", "du", "un", "une", "est", "que", "pour", "dans", "avec", "sur", "pas", "ce", "qui", "vous", "nous", "au", "aux"],
        ["de"] = ["der", "die", "das", "und", "ist", "nicht", "ein", "eine", "zu", "mit", "den", "von", "auf", "fur", "sie", "es", "ich", "im", "dem", "auch"],
        ["it"] = ["il", "lo", "la", "gli", "le", "e", "di", "che", "per", "con", "un", "una", "non", "sono", "del", "della", "nel", "al", "come", "anche"],
    };

    /// <summary>Language with the most common words in the text (needs at least 2 and a clear win).</summary>
    private static string? StopwordGuess(string text)
    {
        var words = StripAccents(text).ToLowerInvariant()
            .Split([' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '-'], StringSplitOptions.RemoveEmptyEntries);
        var scores = Stopwords.Select(kv => (Lang: kv.Key, Score: words.Count(kv.Value.Contains)))
                              .OrderByDescending(x => x.Score).ToList();
        return scores[0].Score >= 2 && scores[0].Score > scores[1].Score ? scores[0].Lang : null;
    }

    private static string StripAccents(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s.Normalize(System.Text.NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }

    private static int NonEnglishLetters(string s) =>
        s.Count(c => char.IsLetter(c) && c > 127);

    /// <summary>Languages the user uses: chosen voices, installed reading languages and the system language.</summary>
    private HashSet<string> PreferredLanguages()
    {
        var set = new HashSet<string>(Settings.AppSettings.Current.Voices.Keys);
        if (Settings.AppSettings.Current.LastVoiceLanguage is { } last)
            set.Add(last);
        foreach (var t in _ocr.AvailableLanguages)
            set.Add(Prefix(t)!);
        set.Add(System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName);
        return set;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[^\p{L}\p{N}\s.,;:!?'’\-()]")]
    private static partial System.Text.RegularExpressions.Regex SymbolsRegex();

    private static Rect Divide(Rect r, int s) => new(r.X / s, r.Y / s, r.Width / s, r.Height / s);

    /// <summary>2× bilinear upscaling (BGRA).</summary>
    private static (byte[], int, int) Upscale2x(byte[] src, int w, int h)
    {
        int W = w * 2, H = h * 2;
        var dst = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            double sy = Math.Max(0, (y + 0.5) / 2 - 0.5);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, h - 1);
            double fy = sy - y0;
            for (int x = 0; x < W; x++)
            {
                double sx = Math.Max(0, (x + 0.5) / 2 - 0.5);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, w - 1);
                double fx = sx - x0;
                int i00 = (y0 * w + x0) * 4, i01 = (y0 * w + x1) * 4, i10 = (y1 * w + x0) * 4, i11 = (y1 * w + x1) * 4;
                int d = (y * W + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    double top = src[i00 + c] + (src[i01 + c] - src[i00 + c]) * fx;
                    double bottom = src[i10 + c] + (src[i11 + c] - src[i10 + c]) * fx;
                    dst[d + c] = (byte)(top + (bottom - top) * fy + 0.5);
                }
            }
        }
        return (dst, W, H);
    }
}

using SnapVox.EdgeTts;

namespace SnapVox.Speech;

/// <summary>
/// Microsoft online voices (Edge "Read aloud"), the default voices ("· Microsoft").
/// The protocol lives in the SnapVox.EdgeTts library (LGPL); here we only join the audio and link the words to the text.
/// Not an official API: if it fails, the app falls back to Piper and then to the Windows voice.
/// </summary>
internal sealed class EdgeTtsEngine : ITtsEngine
{
    private const int MaxCharsPerRequest = 3000;

    public string Name => "Edge";

    public async Task<SpeechAudio> SynthesizeAsync(string text, string languageCode, string? voice, CancellationToken ct)
    {
        voice ??= VoiceCatalog.ForLanguage(languageCode).FirstOrDefault(v => v.Engine == VoiceEngine.Edge)?.Id
                  ?? "en-US-AriaNeural";

        var audio = new MemoryStream();
        var words = new List<WordTiming>();
        var offset = TimeSpan.Zero;

        // Long texts go in chunks; each chunk's timings are offset by the end of the previous one.
        foreach (var (chunk, chunkStart) in Split(text))
        {
            var (mp3, boundaries) = await EdgeTtsClient.SynthesizeAsync(chunk, voice, ct).ConfigureAwait(false);
            audio.Write(mp3);
            foreach (var w in AlignToText(chunk, boundaries))
                words.Add(w with { Start = w.Start + offset, CharIndex = w.CharIndex + chunkStart });
            offset += TimeSpan.FromSeconds(mp3.Length / 6000.0); // 48 kbit/s constante → bytes / 6000 = segundos
        }

        return new SpeechAudio(audio.ToArray(), "audio/mpeg", words, voice);
    }

    /// <summary>The service only returns the word; we find it in the text, always moving forward.</summary>
    internal static List<WordTiming> AlignToText(string text, IReadOnlyList<EdgeWordBoundary> boundaries)
    {
        var words = new List<WordTiming>();
        int cursor = 0;
        foreach (var b in boundaries)
        {
            if (string.IsNullOrWhiteSpace(b.Text))
                continue;
            int at = text.IndexOf(b.Text, cursor, StringComparison.OrdinalIgnoreCase);
            if (at < 0 || at - cursor > 200)
                continue; // not found nearby: skip it instead of jumping far ahead
            words.Add(new WordTiming(b.Start, b.Duration, at, b.Text.Length, b.Text));
            cursor = at + b.Text.Length;
        }
        return words;
    }

    /// <summary>Splits the text into chunks of up to ~3000 characters, preferably at the end of a sentence.</summary>
    private static IEnumerable<(string Chunk, int Start)> Split(string text)
    {
        int start = 0;
        while (start < text.Length)
        {
            int len = Math.Min(MaxCharsPerRequest, text.Length - start);
            if (start + len < text.Length)
            {
                int cut = text.LastIndexOfAny(['.', '!', '?', '\n'], start + len - 1, len);
                if (cut <= start)
                    cut = text.LastIndexOf(' ', start + len - 1, len);
                if (cut > start)
                    len = cut - start + 1;
            }
            yield return (text.Substring(start, len), start);
            start += len;
        }
    }
}

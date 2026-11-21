namespace SnapVox.Speech;

/// <summary>When a word starts being spoken. <see cref="CharIndex"/> points into the text that was sent.</summary>
public sealed record WordTiming(TimeSpan Start, TimeSpan Duration, int CharIndex, int CharLength, string Text);

/// <summary>Generated audio (MP3 or WAV) and the timing of each word.</summary>
public sealed record SpeechAudio(byte[] Audio, string MimeType, IReadOnlyList<WordTiming> Words, string VoiceName);

/// <summary>Voice engine (Edge online, Piper or Windows voices offline).</summary>
public interface ITtsEngine
{
    string Name { get; }

    /// <param name="text">Text to speak.</param>
    /// <param name="languageCode">2-letter ISO code (e.g. "pt").</param>
    /// <param name="voice">Voice name; null = the language's default voice.</param>
    Task<SpeechAudio> SynthesizeAsync(string text, string languageCode, string? voice, CancellationToken ct);
}

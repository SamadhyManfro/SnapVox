using SnapVox.Ocr;
using SnapVox.Platform;

namespace SnapVox.Speech;

/// <summary>
/// Reads the recognized text aloud and tells, at any moment, which OCR word is being spoken.
/// Uses the voice chosen for the language (default: Edge/Microsoft). Automatic fallback chain:
/// chosen voice (Edge/Microsoft) → the language's Piper voice → the Windows voice.
/// </summary>
internal sealed class ReadAloud : IDisposable
{
    // Piper stays loaded between readings (loading the model is the slow part).
    private static readonly PiperTts Piper = new();
    private static readonly ITtsEngine Edge = new EdgeTtsEngine();
    private static readonly ITtsEngine WindowsVoice = PlatformServices.CreateOfflineTts();

    private readonly IAudioPlayer _player = PlatformServices.CreateAudioPlayer();

    /// <summary>Download percentage of the Piper voice (only the first time the voice is used).</summary>
    public event Action<VoiceInfo, int>? DownloadProgress;

    private IReadOnlyList<OcrWord> _ocrWords = [];
    private int[] _wordCharStart = [];
    private List<(TimeSpan Start, int WordIndex)> _timeline = [];

    /// <summary>Finished reading (on another thread).</summary>
    public event Action? Ended;

    public bool UsedOfflineVoice { get; private set; }
    public string VoiceName { get; private set; } = "";
    public bool IsPlaying => _player.IsPlaying;
    public int WordCount => _ocrWords.Count;

    public double Rate
    {
        get => _player.Rate;
        set => _player.Rate = Math.Clamp(value, 0.5, 2.0);
    }

    public ReadAloud()
    {
        _player.Ended += () => Ended?.Invoke();
    }

    /// <summary>Audio generated for the last reading (to save as MP3).</summary>
    public SpeechAudio? LastAudio { get; private set; }

    /// <summary>Language of the text being read.</summary>
    public string Language { get; private set; } = "en";

    /// <summary>Voice chosen when the audio was generated (to know whether the user changed it later).</summary>
    public string? RequestedVoiceId { get; private set; }

    /// <summary>Has the chosen voice for this language changed since the audio was generated?</summary>
    public bool VoiceChoiceChanged => VoiceCatalog.ChosenFor(Language)?.Id != RequestedVoiceId;

    /// <summary>Generates the audio and starts playing (from word <paramref name="fromWordIndex"/>, if given).</summary>
    public async Task StartAsync(OcrOutput ocr, CancellationToken ct, int fromWordIndex = 0)
    {
        _ocrWords = ocr.Words.ToList();
        string text = BuildText(_ocrWords, out _wordCharStart);
        Language = ocr.LanguageCode ?? "en";
        RequestedVoiceId = VoiceCatalog.ChosenFor(Language)?.Id;

        var audio = await SynthesizeWithFallbackAsync(text, Language, ct).ConfigureAwait(false);
        LastAudio = audio; // for "Save MP3" (without the extra silence at the start)
        audio = AudioPadding.AddLeadingSilence(audio); // avoids cutting the start of the first word

        ct.ThrowIfCancellationRequested();
        _timeline = MapTimeline(_ocrWords, _wordCharStart, audio.Words);
        Log.Info($"Voice {audio.VoiceName}: {audio.Audio.Length / 1024} KB, {audio.Words.Count} word timings, " +
                 $"{_timeline.Select(t => t.WordIndex).Distinct().Count()}/{_ocrWords.Count} OCR words aligned.");

        await _player.LoadAsync(audio.Audio, audio.MimeType).ConfigureAwait(false);
        if (fromWordIndex > 0 && _timeline.FirstOrDefault(t => t.WordIndex >= fromWordIndex) is { } from && from != default)
            _player.Position = from.Start - TimeSpan.FromMilliseconds(150);
        _player.Play();
    }

    /// <summary>Tries the chosen voice; if it fails, the language's Piper voice; last, the Windows voice.</summary>
    private async Task<SpeechAudio> SynthesizeWithFallbackAsync(string text, string lang, CancellationToken ct)
    {
        var chosen = VoiceCatalog.ChosenFor(lang);
        var piper = chosen?.Engine == VoiceEngine.Piper ? chosen : VoiceCatalog.PiperFallbackFor(lang);

        var attempts = new List<(string Label, Func<Task<SpeechAudio>> Run, VoiceInfo? Voice)>();
        if (chosen?.Engine == VoiceEngine.Edge)
            attempts.Add(("Edge", () => Edge.SynthesizeAsync(text, lang, chosen.Id, ct), chosen));
        if (piper is not null)
            attempts.Add(("Piper", () =>
            {
                Piper.DownloadProgress = new Progress<int>(p => DownloadProgress?.Invoke(piper, p));
                return Piper.SynthesizeAsync(text, lang, piper.Id, ct);
            }, piper));
        if (Settings.AppSettings.Current.WindowsVoiceFallback)
            attempts.Add(("Windows", () => WindowsVoice.SynthesizeAsync(text, lang, null, ct), null));

        Exception? last = null;
        foreach (var (label, run, voice) in attempts)
        {
            try
            {
                var audio = await run().ConfigureAwait(false);
                VoiceName = voice?.DisplayName ?? audio.VoiceName;
                UsedOfflineVoice = label == "Windows";
                Log.Info($"Reading with {label}: {VoiceName}");
                return audio;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Info($"Voice {label} failed, trying the next one: {ex.Message}");
            }
        }
        throw new InvalidOperationException("No voice available.", last);
    }

    public void Play() => _player.Play();
    public void Pause() => _player.Pause();

    public TimeSpan Position => _player.Position;
    public TimeSpan Duration => _player.Duration;

    /// <summary>Jumps to a point in the audio (0 = start, 1 = end).</summary>
    public void Seek(double fraction)
    {
        if (Duration > TimeSpan.Zero)
            _player.Position = TimeSpan.FromTicks((long)(Duration.Ticks * Math.Clamp(fraction, 0, 0.999)));
    }

    /// <summary>Index of the OCR word being spoken now (−1 before the first one).</summary>
    public int CurrentWordIndex
    {
        get
        {
            var pos = _player.Position;
            int lo = 0, hi = _timeline.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_timeline[mid].Start <= pos) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found < 0 ? -1 : _timeline[found].WordIndex;
        }
    }

    public OcrWord? WordAt(int index) => index >= 0 && index < _ocrWords.Count ? _ocrWords[index] : null;

    /// <summary>Jumps to the previous (−1) or next (+1) word.</summary>
    public void Step(int direction)
    {
        if (_timeline.Count == 0)
            return;
        int current = Math.Max(0, CurrentWordIndex);
        var target = direction > 0
            ? _timeline.FirstOrDefault(t => t.WordIndex > current)
            : _timeline.LastOrDefault(t => t.WordIndex < current);
        if (target != default)
            _player.Position = target.Start;
    }

    public void Restart()
    {
        _player.Position = TimeSpan.Zero;
        _player.Play();
    }

    /// <summary>Links each word timing from the voice to the OCR word (and so to its box on screen).</summary>
    internal static List<(TimeSpan Start, int WordIndex)> MapTimeline(
        IReadOnlyList<OcrWord> words, int[] starts, IReadOnlyList<WordTiming> timings) =>
        timings
            .Select(t => (t.Start, WordIndex: WordAtChar(words, starts, t.CharIndex)))
            .Where(t => t.WordIndex >= 0)
            .OrderBy(t => t.Start)
            .ToList();

    /// <summary>Joins the words with spaces and keeps where each one starts in the text.</summary>
    internal static string BuildText(IReadOnlyList<OcrWord> words, out int[] starts)
    {
        starts = new int[words.Count];
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < words.Count; i++)
        {
            if (i > 0)
                sb.Append(' ');
            starts[i] = sb.Length;
            sb.Append(words[i].Text);
        }
        return sb.ToString();
    }

    private static int WordAtChar(IReadOnlyList<OcrWord> words, int[] starts, int charIndex)
    {
        int idx = Array.BinarySearch(starts, charIndex);
        if (idx < 0)
            idx = ~idx - 1;
        return idx >= 0 && idx < words.Count && charIndex < starts[idx] + words[idx].Text.Length + 1 ? idx : -1;
    }

    public void Dispose() => _player.Dispose();
}

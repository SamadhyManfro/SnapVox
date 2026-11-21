using System.Text.RegularExpressions;
using SherpaOnnx;

namespace SnapVox.Speech;

/// <summary>
/// Piper neural voices running on the PC itself (via sherpa-onnx): free, offline and fast.
/// Piper doesn't report word timings, so we generate sentence by sentence (the start of each sentence
/// is exact) and, within the sentence, the time is spread by word length.
/// </summary>
internal sealed partial class PiperTts : ITtsEngine, IDisposable
{
    private OfflineTts? _tts;
    private string? _loadedVoice;
    private readonly object _gate = new();

    public string Name => "Piper";

    /// <summary>Downloads the model if needed (reporting the percentage).</summary>
    public IProgress<int>? DownloadProgress { get; set; }

    public async Task<SpeechAudio> SynthesizeAsync(string text, string languageCode, string? voice, CancellationToken ct)
    {
        var info = VoiceCatalog.Find(voice) is { Engine: VoiceEngine.Piper } v ? v
                 : VoiceCatalog.PiperFallbackFor(languageCode) ?? throw new NotSupportedException($"No Piper voice for '{languageCode}'.");

        await VoiceStore.DownloadAsync(info, DownloadProgress, ct).ConfigureAwait(false);
        return await Task.Run(() => Generate(text, info, ct), ct).ConfigureAwait(false);
    }

    private SpeechAudio Generate(string text, VoiceInfo voice, CancellationToken ct)
    {
        lock (_gate)
        {
            var tts = Load(voice);
            int rate = tts.SampleRate;
            var samples = new List<float>();
            var words = new List<WordTiming>();

            foreach (var (sentence, start) in Sentences(text))
            {
                ct.ThrowIfCancellationRequested();
                var audio = tts.Generate(sentence, 1.0f, 0);
                float[] s = audio.Samples;
                audio.Dispose();
                var offset = TimeSpan.FromSeconds(samples.Count / (double)rate);
                words.AddRange(EstimateWords(sentence, start, s, rate, offset));
                samples.AddRange(s);
            }

            return new SpeechAudio(ToWav(samples, rate), "audio/wav", words, voice.Id);
        }
    }

    private OfflineTts Load(VoiceInfo voice)
    {
        if (_tts is not null && _loadedVoice == voice.Id)
            return _tts;

        var files = VoiceStore.Files(voice) ?? throw new FileNotFoundException("Voice not downloaded: " + voice.Id);
        _tts?.Dispose();

        var config = new OfflineTtsConfig();
        // Note: needs sherpa-onnx 1.x. Version 2.0 removed the built-in espeak-ng (which turns
        // text into phonemes) and Piper goes silent.
        config.Model.Vits.Model = files.Model;
        config.Model.Vits.Tokens = files.Tokens;
        config.Model.Vits.DataDir = files.DataDir;
        config.Model.Vits.NoiseScale = 0.667f;
        config.Model.Vits.NoiseScaleW = 0.8f;
        config.Model.Vits.LengthScale = 1.0f;
        config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        config.Model.Provider = "cpu";
        config.Model.Debug = Environment.GetEnvironmentVariable("SNAPVOX_TTS_DEBUG") == "1" ? 1 : 0;
        config.MaxNumSentences = 1;

        _tts = new OfflineTts(config);
        _loadedVoice = voice.Id;
        Log.Info($"Piper carregado: {voice.Id} ({_tts.SampleRate} Hz)");
        return _tts;
    }

    /// <summary>Splits into sentences (at . ! ? ; : or line breaks), keeping where each one starts.</summary>
    internal static IEnumerable<(string Text, int Start)> Sentences(string text)
    {
        int start = 0;
        foreach (Match m in SentenceEnd().Matches(text))
        {
            int end = m.Index + m.Length;
            if (text[start..end].Trim().Length > 0)
                yield return (text[start..end], start);
            start = end;
        }
        if (start < text.Length && text[start..].Trim().Length > 0)
            yield return (text[start..], start);
    }

    /// <summary>
    /// Estimates when each word starts within the sentence: removes the silence at the start and end and splits
    /// the rest by word length (punctuation counts as a short pause).
    /// </summary>
    internal static IEnumerable<WordTiming> EstimateWords(string sentence, int sentenceStart, float[] samples, int rate, TimeSpan offset)
    {
        var tokens = WordRegex().Matches(sentence).Cast<Match>().ToList();
        if (tokens.Count == 0 || samples.Length == 0)
            yield break;

        const float threshold = 0.02f;
        int first = Array.FindIndex(samples, x => Math.Abs(x) > threshold);
        int last = Array.FindLastIndex(samples, x => Math.Abs(x) > threshold);
        if (first < 0) { first = 0; last = samples.Length - 1; }

        double speechStart = first / (double)rate, speechEnd = (last + 1) / (double)rate;
        double Weight(Match t)
        {
            double w = t.Value.Count(char.IsLetterOrDigit) + 1.0;
            char end = t.Value[^1];
            if (",;:".Contains(end)) w += 2.5;
            else if (".!?".Contains(end)) w += 3.5;
            return w;
        }

        double total = tokens.Sum(Weight), acc = 0;
        foreach (var t in tokens)
        {
            double w = Weight(t);
            double at = speechStart + (speechEnd - speechStart) * acc / total;
            double dur = (speechEnd - speechStart) * w / total;
            acc += w;
            yield return new WordTiming(offset + TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(dur),
                sentenceStart + t.Index, t.Length, t.Value);
        }
    }

    /// <summary>Turns the samples into 16-bit mono PCM WAV (the system player plays it directly).</summary>
    private static byte[] ToWav(List<float> samples, int rate)
    {
        using var ms = new MemoryStream(44 + samples.Count * 2);
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Count * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (float f in samples)
            w.Write((short)Math.Clamp(f * 32767f, short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _tts?.Dispose();
            _tts = null;
        }
    }

    [GeneratedRegex(@"[.!?;:]+[""'”’)\]]*(\s+|$)|\n+")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\S+")]
    private static partial Regex WordRegex();
}

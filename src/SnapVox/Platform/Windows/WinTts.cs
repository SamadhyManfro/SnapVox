using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using SnapVox.Speech;
using Windows.Media.Core;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace SnapVox.Platform.Windows;

/// <summary>Windows' own voices: work offline. Used as the last backup.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WinTts : ITtsEngine
{
    public string Name => "Windows";

    public async Task<SpeechAudio> SynthesizeAsync(string text, string languageCode, string? voice, CancellationToken ct)
    {
        using var synth = new SpeechSynthesizer();
        synth.Options.IncludeWordBoundaryMetadata = true;

        var chosen = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName == voice)
                     ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith(languageCode + "-", StringComparison.OrdinalIgnoreCase))
                     ?? SpeechSynthesizer.DefaultVoice;
        synth.Voice = chosen;

        using var stream = await synth.SynthesizeTextToStreamAsync(text).AsTask(ct).ConfigureAwait(false);

        var words = new List<WordTiming>();
        foreach (var track in stream.TimedMetadataTracks)
        {
            foreach (var cue in track.Cues.OfType<SpeechCue>())
            {
                int start = cue.StartPositionInInput ?? -1;
                int end = cue.EndPositionInInput ?? start;
                if (start < 0)
                    continue;
                words.Add(new WordTiming(cue.StartTime, cue.Duration, start, end - start + 1, cue.Text ?? ""));
            }
        }

        var bytes = new byte[stream.Size];
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)stream.Size).AsTask(ct).ConfigureAwait(false);
            reader.ReadBytes(bytes);
        }

        return new SpeechAudio(bytes, "audio/wav", words.OrderBy(w => w.Start).ToList(), chosen.DisplayName);
    }
}

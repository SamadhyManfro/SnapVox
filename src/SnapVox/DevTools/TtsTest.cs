using System.Text;
using SnapVox.Platform;
using SnapVox.Speech;

namespace SnapVox.DevTools;

/// <summary>
/// Voice test without playing sound: generates audio (Piper, Edge and Windows), saves the files and a report
/// with generation time and word alignment. Usage: SnapVox.exe --tts-test &lt;folder&gt;
/// </summary>
internal static class TtsTest
{
    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();
        const string text = "A nova atualização do sistema traz melhorias de desempenho. Agora é possível ouvir qualquer texto da tela, com 2 vozes naturais!";

        var ocrWords = text.Split(' ').Select((w, i) => new Ocr.OcrWord(w, new Avalonia.Rect(i * 50, 0, 40, 12))).ToList();
        string built = ReadAloud.BuildText(ocrWords, out int[] starts);

        var engines = new (ITtsEngine Engine, string? Voice)[]
        {
            (new PiperTts(), "pt_BR-faber-medium"),
            (new PiperTts(), "pt_BR-dii-high"),
            (new EdgeTtsEngine(), "pt-BR-FranciscaNeural"),
            (PlatformServices.CreateOfflineTts(), null),
        };

        foreach (var (engine, voice) in engines)
        {
            try
            {
                var started = DateTime.UtcNow;
                var audio = engine.SynthesizeAsync(built, "pt", voice, CancellationToken.None).GetAwaiter().GetResult();
                double ms = (DateTime.UtcNow - started).TotalMilliseconds;
                string file = Path.Combine(outDir, $"tts_{engine.Name}_{voice ?? "windows"}.{(audio.MimeType == "audio/mpeg" ? "mp3" : "wav")}");
                File.WriteAllBytes(file, audio.Audio);

                var padded = AudioPadding.AddLeadingSilence(audio);
                double shift = padded.Words.Count > 0 && audio.Words.Count > 0 ? (padded.Words[0].Start - audio.Words[0].Start).TotalMilliseconds : -1;
                File.WriteAllBytes(Path.ChangeExtension(file, null) + "_padded" + Path.GetExtension(file), padded.Audio);
                report.AppendLine($"   leading silence: +{shift:0} ms, {padded.Audio.Length - audio.Audio.Length} extra bytes");

                var timeline = ReadAloud.MapTimeline(ocrWords, starts, audio.Words);
                int covered = timeline.Select(t => t.WordIndex).Distinct().Count();
                bool ordered = timeline.Zip(timeline.Skip(1)).All(p => p.First.WordIndex <= p.Second.WordIndex);
                report.AppendLine($"[{engine.Name} {voice}] {audio.Audio.Length / 1024} KB · {ms:0} ms · " +
                                  $"aligned {covered}/{ocrWords.Count} · in order: {ordered}");
                foreach (var w in audio.Words.Take(6))
                    report.AppendLine($"   {w.Start:mm\\:ss\\.fff}  \"{w.Text}\"");
            }
            catch (Exception ex)
            {
                report.AppendLine($"[{engine.Name} {voice}] ERROR: {ex.GetType().Name}: {ex.Message}");
            }
            (engine as IDisposable)?.Dispose();
        }

        File.WriteAllText(Path.Combine(outDir, "tts-report.txt"), report.ToString());
        return 0;
    }
}

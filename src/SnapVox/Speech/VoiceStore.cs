using System.Diagnostics;

namespace SnapVox.Speech;

/// <summary>
/// Piper voice models downloaded on demand to the "voices" folder in <see cref="AppPaths.Local"/>.
/// Source: official releases of the sherpa-onnx project on GitHub (k2-fsa/sherpa-onnx, release "tts-models").
/// </summary>
internal static class VoiceStore
{
    private const string BaseUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);

    public static string Root { get; } = Path.Combine(AppPaths.Local, "voices");

    private static string FolderOf(VoiceInfo voice) => Path.Combine(Root, "vits-piper-" + voice.Id);

    /// <summary>Model files, if already downloaded.</summary>
    public static (string Model, string Tokens, string DataDir)? Files(VoiceInfo voice)
    {
        string dir = FolderOf(voice);
        string model = Path.Combine(dir, voice.Id + ".onnx");
        string tokens = Path.Combine(dir, "tokens.txt");
        string data = Path.Combine(dir, "espeak-ng-data");
        return File.Exists(model) && File.Exists(tokens) && Directory.Exists(data) ? (model, tokens, data) : null;
    }

    public static bool IsDownloaded(VoiceInfo voice) => voice.Engine != VoiceEngine.Piper || Files(voice) is not null;

    /// <summary>Downloads and extracts the model (once). <paramref name="progress"/> receives 0–100.</summary>
    public static async Task DownloadAsync(VoiceInfo voice, IProgress<int>? progress, CancellationToken ct)
    {
        if (IsDownloaded(voice))
            return;

        await DownloadLock.WaitAsync(ct).ConfigureAwait(false);
        string archive = Path.Combine(Path.GetTempPath(), $"snapvox-{voice.Id}-{Guid.NewGuid():N}.tar.bz2");
        try
        {
            if (IsDownloaded(voice))
                return;

            Log.Info($"Downloading voice {voice.Id}…");
            using (var response = await Http.GetAsync(BaseUrl + $"vits-piper-{voice.Id}.tar.bz2", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? (long)(voice.DownloadMb * 1024 * 1024);
                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = File.Create(archive);
                var buffer = new byte[256 * 1024];
                long done = 0;
                int last = -1, read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    int pct = (int)Math.Min(99, done * 100 / Math.Max(1, total));
                    if (pct != last)
                        progress?.Report(last = pct);
                }
            }

            // Extract with the system tar (Windows 10+, Linux and Mac have it; it handles .tar.bz2).
            Directory.CreateDirectory(Root);
            var tar = Process.Start(new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "tar.exe") : "tar",
                ArgumentList = { "-xjf", archive, "-C", Root },
                CreateNoWindow = true,
                UseShellExecute = false,
            })!;
            await tar.WaitForExitAsync(ct).ConfigureAwait(false);
            if (tar.ExitCode != 0 || !IsDownloaded(voice))
                throw new IOException($"Couldn't extract voice {voice.Id} (tar {tar.ExitCode}).");

            progress?.Report(100);
            Log.Info($"Voice {voice.Id} installed.");
        }
        finally
        {
            try { File.Delete(archive); } catch { /* temp file */ }
            DownloadLock.Release();
        }
    }

    /// <summary>Deletes the downloaded model (frees space).</summary>
    public static void Delete(VoiceInfo voice)
    {
        try { Directory.Delete(FolderOf(voice), recursive: true); } catch (DirectoryNotFoundException) { }
    }
}

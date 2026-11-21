using System.Diagnostics;
using System.Security.Cryptography;

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

    // SHA-256 of each published archive. A voice whose file doesn't match is never unpacked or loaded.
    private static readonly Dictionary<string, string> ArchiveHashes = new()
    {
        ["de_DE-dii-high"] = "64d46d39afb0f53a49871f5415259ffe474f443696148930b34a003612b69110",
        ["de_DE-thorsten-medium"] = "50487d9c95fdf2191f31d2588569381063ba1591dcd4c7d4bdd30f12b2191714",
        ["en_US-amy-medium"] = "9a5d1fc497f85e8022b785bff5f8105203b1e33099ee6265203efc70b0cb0264",
        ["en_US-lessac-medium"] = "9e3febfacf0abf4270172d2958bcec246032b7e88efc2720840cc80c93de334e",
        ["en_US-ryan-medium"] = "c546af78b6395b4e7c4ce1ed899438b64426a362f5d4ec5fecd090ded9ad7505",
        ["es_ES-davefx-medium"] = "a3f6beb54a9cb893279f72978a22f807a4d9fc9c7848157b524d5cc7b7f58b22",
        ["es_MX-claude-high"] = "ec33fb689c248fe64810aab564cba97babf0f506672cfd404928d46e751a4721",
        ["fr_FR-siwis-medium"] = "375909aa30842b3a4efa10b1beb1d761af792960ae6873b4d53889f96c66195b",
        ["fr_FR-tom-medium"] = "3d8258ef8466b5d5f2cc46c06e1415d3d01c9cbaeed8b6d5b3d152073672472c",
        ["it_IT-paola-medium"] = "7541f75778afa164e44e34baaef63befad7698595df26a95ca944b63ef1a16b4",
        ["it_IT-riccardo-x_low"] = "8ad0791558ae2b3e96054fc5756ae3ae2f6457fb1765bc01f43bbab85e0a7021",
        ["pt_BR-cadu-medium"] = "aba78157d4b89acc17ddef15a70f4b2474f4c189d4de6035002ac7fec9d5d303",
        ["pt_BR-dii-high"] = "76bdf009bb29e69b12336de833d0d219ffa558f4281330eb141e4a0569c108a2",
        ["pt_BR-faber-medium"] = "7add3f923ad6bc25ca8a192805fd1a64d1b3893e4611c4a9719545a825039a83",
        ["pt_BR-jeff-medium"] = "da4c870fc7b20600c74261b3e1dd1c816da2ce063e18c46e1f2cd86dea88f3f0",
        ["ar_JO-kareem-medium"] = "9ebbcea30e0fbd588f7b2cb45ee897d6aeb1bf5791cbc037a7b5a3f641e3dbce",
        ["cs_CZ-jirka-medium"] = "321aafd83dd89b3d5b2c36c5898e0710d4f472f3c191cb7f73901a89b7f8539f",
        ["da_DK-talesyntese-medium"] = "f2e2cbcdb2b21e76f93635b35105d07b37997ace434d7289d04b689a97c19bd0",
        ["el_GR-rapunzelina-low"] = "bb868070a9ba0d7220b3d7c9291a998c2f60accdb73525bd63b8751b86dd4839",
        ["fi_FI-harri-medium"] = "a7fd608e73983c366a5e0bcd13382e6529a6749f67efc406b5a7f9021e3a8b1d",
        ["hu_HU-anna-medium"] = "c67b13e57b71eca19098fe499afc35ebce8f63a0b524017c786af2025821216a",
        ["no_NO-talesyntese-medium"] = "dcb7c36b7222e290cca1fd4cdecb474e7fd651dc5b0d3d42c33d416a8e62018b",
        ["nl_NL-pim-medium"] = "e78ed301bf6fc66561a04c110754cd6928d772630a3b80b09a83f89410130171",
        ["pl_PL-gosia-medium"] = "75bd34dcbdc4dd98d763954756b4b34b4208100497c836381542e4d73dcefa9c",
        ["ro_RO-mihai-medium"] = "cfbee3cdc25cf063339f69e189f4d93cc3be6610d267567537323ced3cf87f4d",
        ["ru_RU-irina-medium"] = "1fc0f54e5e084fe287c07909f2f6e0ba6d857864cf800e3ab80286a4e8233008",
        ["sk_SK-lili-medium"] = "a00dfbca5d34733862a59d9e309f4e94a92fccc9337c2b124de9a241b604fc0d",
        ["sl_SI-artur-medium"] = "df805f31c4513864edebf7e287cf0b54d72788fc661ed73f88dc4e5aabc5efd5",
        ["sr_RS-serbski_institut-medium"] = "b8c100e5ff885d11ff96ad92a953f4b82e1cd649f9ac60a77ae4553d37507b2b",
        ["sv_SE-lisa-medium"] = "78b5f6808e513d545d9284a015075ef31f09623e6c08d6186a29a1997aa577d7",
        ["tr_TR-fahrettin-medium"] = "2ee3c18b5dc0a5faf913f995b027163befa6e7a6ad2f5c6b8a2e99fe1aad8b6d",
        ["zh_CN-huayan-medium"] = "dbdfec42b91d9cee31cce9ff4b3e9c305eb6fbf60546d071f7e46273554cce6b",
    };

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

            if (!ArchiveHashes.TryGetValue(voice.Id, out var expectedHash))
                throw new InvalidDataException($"No known hash for voice {voice.Id}.");

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

            // Stays open with read-only sharing: tar unpacks exactly the bytes that were verified.
            await using var verified = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
            string actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(verified, ct).ConfigureAwait(false));
            if (actualHash != expectedHash)
                throw new InvalidDataException($"Voice {voice.Id} failed verification.");

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

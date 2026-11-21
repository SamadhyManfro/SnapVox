using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using SnapVox.Settings;

namespace SnapVox;

/// <summary>
/// Updates through GitHub Releases: on start (if "Automatic updates" is on) it looks for a newer
/// version and asks whether to update. "Update" downloads the new installer, runs it and closes
/// SnapVox. If the release has no installer attached, it opens its page in the browser.
/// </summary>
internal static class Updater
{
    /// <summary>GitHub repository (owner/name). Change it here if the repository is renamed.</summary>
    public const string Repository = "SamadhyManfro/SnapVox";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>
    /// Public half of the release signing key (ECDSA P-256). An installer only runs if its ".sig" file
    /// was made with the private half, which never leaves the maintainer's PC and is not on GitHub.
    /// </summary>
    private const string UpdatePublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEdD7KMXCk3L3Hx+KaYz56Tnn0tQPh/7eGGfnxrgL16PDqjOXvock1spmdiGr98vtwsO/b4FRYBkHNruE7kSS0QQ==";

    private static string ReleasesUrl => $"https://github.com/{Repository}/releases";

    public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? AssetUrl, string? SignatureUrl);

    public static Version CurrentVersion =>
        typeof(Updater).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 1, 0);

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"SnapVox/{CurrentVersion}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>Newest published version, or null if there is none (or the check failed — see the Failed flag).</summary>
    public static async Task<(UpdateInfo? Update, bool Failed)> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Info($"Update: GitHub answered {(int)resp.StatusCode}.");
                return (null, true);
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
                return (null, true);
            if (version <= CurrentVersion)
                return (null, false);

            string page = root.TryGetProperty("html_url", out var html) && html.GetString() is { } h && IsReleaseUrl(h) ? h : ReleasesUrl;
            string? assetUrl = null, signatureUrl = null;
            // The portable version has nothing to install: "Update" opens the release page.
            if (!AppPaths.IsPortable && root.TryGetProperty("assets", out var assets))
            {
                string setup = $"SnapVox-Setup-{version}.exe";
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.GetProperty("name").GetString() ?? "";
                    string? url = a.GetProperty("browser_download_url").GetString();
                    if (url is null || !IsReleaseUrl(url))
                        continue;
                    if (name == setup)
                        assetUrl = url;
                    else if (name == setup + ".sig")
                        signatureUrl = url;
                }
            }
            // Without a signature there is no automatic install: "Update" opens the release page.
            if (assetUrl is null || signatureUrl is null)
                assetUrl = signatureUrl = null;
            return (new UpdateInfo(version, tag, page, assetUrl, signatureUrl), false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            Log.Info("Update: couldn't check: " + ex.Message);
            return (null, true);
        }
    }

    /// <summary>On app start: waits a bit, checks and asks (if the option is on).</summary>
    public static async void CheckOnStartup()
    {
        if (!AppSettings.Current.AutoUpdate)
            return;
        await Task.Delay(TimeSpan.FromSeconds(8));
        var (update, _) = await CheckAsync();
        if (update is null || update.Tag == AppSettings.Current.SkippedUpdate)
            return;
        Log.Info($"Update available: {update.Tag}");
        await Dispatcher.UIThread.InvokeAsync(() => UI.UpdateWindow.ShowFor(update));
    }

    /// <summary>Downloads the new version's installer to a new private folder and verifies its signature. Returns its path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<int> progress, CancellationToken ct = default)
    {
        if (update.AssetUrl is null || update.SignatureUrl is null)
            throw new InvalidOperationException("This release has no signed installer.");

        string updates = Path.Combine(AppPaths.Local, "updates");
        DeleteFolder(updates); // leftovers from a previous update
        string folder = Path.Combine(updates, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "SnapVox-Setup.exe");
        try
        {
            string signature = await Http.GetStringAsync(update.SignatureUrl, ct).ConfigureAwait(false);

            using (var resp = await Http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? 0;
                await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = File.Create(path);
                var buffer = new byte[256 * 1024];
                long done = 0;
                int read, last = -1;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != last)
                        progress.Report(last = pct);
                }
            }

            if (!IsSignatureValid(path, update.Version, signature))
                throw new InvalidDataException("The update failed signature verification.");
            return path;
        }
        catch
        {
            DeleteFolder(folder);
            throw;
        }
    }

    /// <summary>The signature covers the file's hash and its version, so an old release can't pose as a new one.</summary>
    internal static bool IsSignatureValid(string file, Version version, string signatureBase64)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            byte[] message = Encoding.UTF8.GetBytes($"SnapVox-update-v1\n{version}\n{hash}\n");
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(UpdatePublicKey), out _);
            return key.VerifyData(message, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or IOException)
        {
            return false;
        }
    }

    private static bool IsReleaseUrl(string url) =>
        url.StartsWith(ReleasesUrl + "/", StringComparison.Ordinal) && Uri.IsWellFormedUriString(url, UriKind.Absolute);

    private static void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info("Couldn't clean the update folder: " + ex.Message);
        }
    }

    /// <summary>Runs the downloaded installer and closes SnapVox so it can replace the files.</summary>
    public static void RunInstallerAndQuit(string installerPath)
    {
        Log.Info("Running the update installer.");
        // Inno Setup installer in silent mode: only shows progress and reopens SnapVox at the end.
        // It starts after a short delay: if it found SnapVox still running, silent mode would just give up.
        // The path goes through a variable read with delayed expansion, so cmd never parses it as a command.
        string system = Environment.SystemDirectory;
        var info = new ProcessStartInfo(Path.Combine(system, "cmd.exe"))
        {
            Arguments = $"/d /v:on /c \"\"{Path.Combine(system, "PING.EXE")}\" -n 4 127.0.0.1 >nul & \"!SNAPVOX_SETUP!\" /SILENT /SUPPRESSMSGBOXES /NORESTART\"",
            WorkingDirectory = system,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.Environment["SNAPVOX_SETUP"] = installerPath;
        Process.Start(info);
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var w in desktop.Windows.ToList())
                w.Close();
            desktop.Shutdown();
        }
    }

    public static void OpenPage(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>
    /// "Send a suggestion": opens a new issue in the repository, already filled in with the app and Windows version
    /// in the text (helps understand the problem). The user needs to sign in to GitHub to send it.
    /// </summary>
    public static void OpenSuggestion()
    {
        string body = $"{Localization.L.SuggestionTemplate}\n\n---\nSnapVox {CurrentVersion} · {Environment.OSVersion.VersionString}";
        string url = $"https://github.com/{Repository}/issues/new?labels=suggestion" +
                     $"&title={Uri.EscapeDataString(Localization.L.SuggestionTitle)}&body={Uri.EscapeDataString(body)}";
        OpenPage(url);
    }
}

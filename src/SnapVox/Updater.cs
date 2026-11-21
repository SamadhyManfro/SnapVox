using System.Diagnostics;
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

    public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? AssetUrl, string? AssetName);

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

            string page = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : $"https://github.com/{Repository}/releases";
            string? assetUrl = null, assetName = null;
            // The portable version has nothing to install: "Update" opens the release page.
            if (!AppPaths.IsPortable && root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.GetProperty("name").GetString() ?? "";
                    if (name.StartsWith("SnapVox-Setup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        assetName = name;
                        assetUrl = a.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }
            return (new UpdateInfo(version, tag, page, assetUrl, assetName), false);
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

    /// <summary>Downloads the new version's installer to the temp folder. Returns its path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<int> progress, CancellationToken ct = default)
    {
        string path = Path.Combine(Path.GetTempPath(), update.AssetName ?? $"SnapVox-{update.Tag}.exe");
        using var resp = await Http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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
        return path;
    }

    /// <summary>Runs the downloaded installer and closes SnapVox so it can replace the files.</summary>
    public static void RunInstallerAndQuit(string installerPath)
    {
        Log.Info("Running the update installer: " + installerPath);
        // Inno Setup installer in silent mode: only shows progress and reopens SnapVox at the end.
        // It starts after a short delay: if it found SnapVox still running, silent mode would just give up.
        Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c ping -n 4 127.0.0.1 >nul & \"{installerPath}\" /SILENT /SUPPRESSMSGBOXES /NORESTART",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
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

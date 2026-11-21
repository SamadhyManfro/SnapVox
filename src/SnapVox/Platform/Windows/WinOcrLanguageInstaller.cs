using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Media.SpeechSynthesis;

namespace SnapVox.Platform.Windows;

/// <summary>
/// Installs/removes the Windows packages of a reading language:
/// "Language.OCR" (text recognition) and "Language.TextToSpeech" (system voice).
/// Uses DISM elevated (Windows shows the permission/UAC prompt). Since an elevated process can't
/// send its output straight to ours, each DISM step writes to a temp file and
/// we read its percentage while it runs.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed partial class WinOcrLanguageInstaller : IOcrLanguageInstaller
{
    // Languages that Windows offers as an OCR package.
    public IReadOnlyList<string> InstallableLanguages { get; } =
    [
        "ar-SA", "bg-BG", "bs-Latn-BA", "cs-CZ", "da-DK", "de-DE", "el-GR", "en-GB", "en-US", "es-ES", "es-MX",
        "fi-FI", "fr-CA", "fr-FR", "hr-HR", "hu-HU", "it-IT", "ja-JP", "ko-KR", "nb-NO", "nl-NL", "pl-PL", "pt-BR",
        "pt-PT", "ro-RO", "ru-RU", "sk-SK", "sl-SI", "sr-Cyrl-RS", "sr-Latn-RS", "sv-SE", "tr-TR", "zh-CN", "zh-HK", "zh-TW",
    ];

    public IReadOnlyList<string> InstalledVoiceLanguages =>
        SpeechSynthesizer.AllVoices.Select(v => v.Language).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string Ocr(string tag) => $"Language.OCR~~~{tag}~0.0.1.0";
    private static string Voice(string tag) => $"Language.TextToSpeech~~~{tag}~0.0.1.0";

    // The voice is optional: not every OCR language has a Windows voice, so its failure doesn't fail the install.
    public Task<InstallResult> InstallAsync(string tag, IProgress<int>? progress = null) =>
        RunAsync(tag, progress, [("Add", Ocr(tag), true), ("Add", Voice(tag), false)]);

    public Task<InstallResult> InstallVoiceAsync(string tag, IProgress<int>? progress = null) =>
        RunAsync(tag, progress, [("Add", Voice(tag), true)]);

    public Task<InstallResult> RemoveAsync(string tag, IProgress<int>? progress = null) =>
        RunAsync(tag, progress, [("Remove", Voice(tag), false), ("Remove", Ocr(tag), true)]);

    private async Task<InstallResult> RunAsync(string tag, IProgress<int>? progress, (string Verb, string Capability, bool Required)[] steps)
    {
        if (!InstallableLanguages.Contains(tag))
            return InstallResult.Failed;

        string dism = Path.Combine(Environment.SystemDirectory, "dism.exe");
        var logs = steps.Select(_ => Path.Combine(Path.GetTempPath(), $"snapvox-lang-{Guid.NewGuid():N}.log")).ToArray();

        // Builds "step1 && step2 …". Optional steps never break the sequence.
        // The tag comes from the fixed list above, so there is no user text in the command.
        var cmd = new StringBuilder();
        for (int i = 0; i < steps.Length; i++)
        {
            var (verb, capability, required) = steps[i];
            string run = $"\"{dism}\" /Online /{verb}-Capability /CapabilityName:{capability} /NoRestart > \"{logs[i]}\" 2>&1";
            if (i > 0)
                cmd.Append(" && ");
            cmd.Append(required ? $"({run})" : $"({run} & ver > nul)"); // "ver" resets the error code
        }

        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c \"{cmd}\"",
            Verb = "runas", // asks for administrator permission
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null)
                return InstallResult.Failed;

            progress?.Report(0);
            int last = 0;
            while (!process.HasExited)
            {
                await Task.Delay(400).ConfigureAwait(false);
                // Each step is worth an equal share of the bar.
                double total = logs.Select(l => (ReadPercent(l) ?? 0) / 100.0).Sum() / logs.Length * 100;
                int pct = (int)Math.Min(99, total);
                if (pct != last)
                {
                    last = pct;
                    progress?.Report(pct);
                }
            }

            Log.Info($"DISM {string.Join(", ", steps.Select(s => s.Verb + " " + s.Capability))}: exit code {process.ExitCode}");
            if (process.ExitCode is 0 or 3010) // 3010 = success, restart recommended
            {
                progress?.Report(100);
                return InstallResult.Installed;
            }
            return InstallResult.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // the user declined the UAC prompt
        {
            return InstallResult.Canceled;
        }
        catch (Exception ex)
        {
            Log.Info($"DISM failed ({tag}): {ex.Message}");
            return InstallResult.Failed;
        }
        finally
        {
            foreach (var l in logs)
                try { File.Delete(l); } catch { /* temp file; doesn't matter */ }
        }
    }

    /// <summary>Last percentage written by DISM (e.g. "[====  50.0%   ]"), or null if none yet.</summary>
    internal static int? ReadPercent(string logFile)
    {
        try
        {
            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return LastPercent(reader.ReadToEnd());
        }
        catch (IOException)
        {
            return null; // the file doesn't exist yet
        }
    }

    internal static int? LastPercent(string text)
    {
        var matches = PercentRegex().Matches(text);
        if (matches.Count == 0)
            return null;
        string value = matches[^1].Groups[1].Value.Replace(',', '.');
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? (int)Math.Clamp(Math.Floor(d), 0, 100)
            : null;
    }

    [GeneratedRegex(@"(\d{1,3}(?:[.,]\d+)?)\s*%")]
    private static partial Regex PercentRegex();
}

/// <summary>Start with Windows via the user's Run key (no admin needed).</summary>
[SupportedOSPlatform("windows")]
internal sealed class WinStartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SnapVox";

    public bool IsEnabled
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

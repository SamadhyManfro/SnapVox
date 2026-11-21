using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Media.SpeechSynthesis;

namespace SnapVox.Platform.Windows;

/// <summary>
/// Installs/removes the Windows packages of a reading language:
/// "Language.OCR" (text recognition) and "Language.TextToSpeech" (system voice).
/// Uses DISM elevated (Windows shows the permission/UAC prompt). Since an elevated process can't
/// send its output straight to ours, each DISM step writes to a private named pipe and
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

    /// <summary>
    /// The package name for a language. Windows reports some installed OCR languages
    /// differently from their packages ("ko" for ko-KR, "zh-Hans-CN" for zh-CN).
    /// </summary>
    internal string? PackageTag(string tag)
    {
        var exact = InstallableLanguages.FirstOrDefault(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;
        if (tag.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";
        if (tag.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
            return "zh-TW";
        var variant = InstallableLanguages.FirstOrDefault(t => t.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase));
        if (variant is not null)
            return variant;
        string language = tag.Split('-')[0];
        return InstallableLanguages.FirstOrDefault(t => t.Split('-')[0].Equals(language, StringComparison.OrdinalIgnoreCase));
    }

    // Only the text recognition: each Windows package takes minutes to install, whatever its size,
    // and the online voices don't need the Windows voice. It stays available as a separate install.
    public Task<InstallResult> InstallAsync(string tag, IProgress<int>? progress = null) =>
        PackageTag(tag) is { } package ? RunAsync(package, progress, [("Add", Ocr(package), true)]) : Task.FromResult(InstallResult.Failed);

    // The voice is optional: if its package fails, the text recognition still counts as installed.
    public Task<InstallResult> InstallWithVoiceAsync(string tag, IProgress<int>? progress = null) =>
        PackageTag(tag) is { } package
            ? RunAsync(package, progress, [("Add", Ocr(package), true), ("Add", Voice(package), false)])
            : Task.FromResult(InstallResult.Failed);

    public Task<InstallResult> InstallVoiceAsync(string tag, IProgress<int>? progress = null) =>
        PackageTag(tag) is { } package ? RunAsync(package, progress, [("Add", Voice(package), true)]) : Task.FromResult(InstallResult.Failed);

    public Task<InstallResult> RemoveAsync(string tag, IProgress<int>? progress = null) =>
        PackageTag(tag) is { } package
            ? RunAsync(package, progress, [("Remove", Voice(package), false), ("Remove", Ocr(package), true)])
            : Task.FromResult(InstallResult.Failed);

    private async Task<InstallResult> RunAsync(string tag, IProgress<int>? progress, (string Verb, string Capability, bool Required)[] steps)
    {
        if (!InstallableLanguages.Contains(tag))
            return InstallResult.Failed;

        string dism = Path.Combine(Environment.SystemDirectory, "dism.exe");

        // DISM's output comes back through private pipes. Nothing in this elevated command comes from
        // the environment (like TEMP) or from user text: only the system folder, the fixed list above and GUIDs.
        var pipeNames = steps.Select(_ => $"snapvox-lang-{Guid.NewGuid():N}").ToArray();
        var pipes = pipeNames.Select(n => new NamedPipeServerStream(n, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)).ToArray();
        var percents = new int[steps.Length];
        using var stopReading = new CancellationTokenSource();
        var readers = pipes.Select((p, i) => ReadProgressAsync(p, percents, i, stopReading.Token)).ToArray();

        // Builds "step1 && step2 …". Optional steps never break the sequence.
        var cmd = new StringBuilder();
        for (int i = 0; i < steps.Length; i++)
        {
            var (verb, capability, required) = steps[i];
            string run = $"\"{dism}\" /Online /{verb}-Capability /CapabilityName:{capability} /NoRestart > \"\\\\.\\pipe\\{pipeNames[i]}\" 2>&1";
            if (i > 0)
                cmd.Append(" && ");
            cmd.Append(required ? $"({run})" : $"({run} & ver > nul)"); // "ver" resets the error code
        }

        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/d /c \"{cmd}\"", // /d: ignores any AutoRun command set in the registry
            WorkingDirectory = Environment.SystemDirectory,
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
                double total = percents.Sum() / (double)percents.Length;
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
            stopReading.Cancel();
            await Task.WhenAll(readers).ConfigureAwait(false);
            foreach (var pipe in pipes)
                pipe.Dispose();
        }
    }

    /// <summary>Follows one DISM step's output and keeps its highest percentage.</summary>
    private static async Task ReadProgressAsync(NamedPipeServerStream pipe, int[] percents, int index, CancellationToken ct)
    {
        try
        {
            await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            var buffer = new byte[4096];
            string tail = "";
            int read;
            while ((read = await pipe.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                tail += Encoding.ASCII.GetString(buffer, 0, read);
                if (LastPercent(tail) is { } pct && pct > Volatile.Read(ref percents[index]))
                    Volatile.Write(ref percents[index], pct);
                if (tail.Length > 64)
                    tail = tail[^64..];
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // the step ended or never started
        }
    }

    /// <summary>Last percentage written by DISM (e.g. "[====  50.0%   ]"), or null if none yet.</summary>
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

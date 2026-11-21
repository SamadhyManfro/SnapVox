using Avalonia.Threading;
using SnapVox.Localization;
using SnapVox.Platform;

namespace SnapVox;

/// <summary>
/// Installing/removing reading languages (OCR + voice) belongs to the app, not to the Settings window:
/// closing Settings cancels nothing, and reopening the window shows the current progress.
/// </summary>
internal static class LanguagePacks
{
    private static readonly IOcrLanguageInstaller Installer = PlatformServices.CreateOcrLanguageInstaller();
    private static readonly IOcrEngine Ocr = PlatformServices.CreateOcrEngine();

    /// <summary>Operation in progress (null = none).</summary>
    public static Operation? Current { get; private set; }

    /// <summary>Last result message (e.g. "Portuguese (Brazil) installed").</summary>
    public static (string Text, StatusKind Kind)? LastResult { get; private set; }

    /// <summary>Progress changed or finished (always on the UI thread).</summary>
    public static event Action? Changed;

    public enum StatusKind { Info, Success, Warning }

    public sealed record Operation(string Tag, string Name, Func<string, string> RunningText, int Percent)
    {
        public string Text => $"{RunningText(Name)} {Percent}%";
    }

    public static IReadOnlyList<string> InstallableLanguages => Installer.InstallableLanguages;
    public static IReadOnlyList<string> InstalledOcr => Ocr.AvailableLanguages;
    public static IReadOnlyList<string> InstalledVoices => Installer.InstalledVoiceLanguages;

    public static bool IsBusy => Current is not null;

    private static readonly Queue<string> Pending = new();

    /// <summary>Is the language (OCR) already installed? Accepts "pt" or "pt-BR".</summary>
    public static bool HasReadingLanguage(string tagOrCode) => OcrTagFor(tagOrCode) is not null;

    /// <summary>The installed OCR for a language. Windows may report it as "ko" or as "pt-BR".</summary>
    public static string? OcrTagFor(string tagOrCode)
    {
        string code = tagOrCode.Split('-')[0];
        return InstalledOcr.FirstOrDefault(t => t.Split('-')[0].Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A Windows voice of this language is installed.</summary>
    public static bool HasWindowsVoice(string tagOrCode)
    {
        string code = tagOrCode.Split('-')[0];
        return InstalledVoices.Any(t => t.Split('-')[0].Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The last finished operation installed a Windows voice, which only appears after a restart.</summary>
    public static bool RestartRecommended { get; private set; }

    /// <summary>
    /// Makes sure the reading language (OCR + Windows voice) is installed. Used when the user picks
    /// a voice: so the text is recognized in the right language (accents) and the voice doesn't read in the wrong one.
    /// If an installation is already running, it is queued.
    /// </summary>
    public static void EnsureInstalled(string locale)
    {
        string code = locale.Split('-')[0];
        bool wantsVoice = WithOfflineVoice.Contains(locale) && Speech.VoiceCatalog.CanInstallWindowsVoice(code);
        if ((HasReadingLanguage(locale) && !wantsVoice) || !InstallableLanguages.Contains(locale))
            return;
        if (Current?.Tag == locale || Pending.Contains(locale))
            return;
        if (IsBusy)
        {
            Pending.Enqueue(locale);
            return;
        }
        WithOfflineVoice.Remove(locale);
        if (!wantsVoice)
            Install(locale);
        else if (HasReadingLanguage(locale))
            InstallVoice(locale);
        else
            Start(locale, p => Installer.InstallWithVoiceAsync(locale, p), L.Installing, L.InstallDone, L.InstallCanceled, L.InstallFailed, installsVoice: true);
    }

    /// <summary>
    /// "Add language": gets everything the language needs. Its offline voice is downloaded now (never shipped
    /// inside the app): a Piper model when there is one, otherwise the Windows voice, in the same permission prompt.
    /// </summary>
    public static void AddLanguage(Speech.VoiceInfo voice)
    {
        _ = DownloadOfflineVoiceAsync(voice.Language);
        if (Speech.VoiceCatalog.CanInstallWindowsVoice(voice.Language))
            WithOfflineVoice.Add(voice.Locale);
        EnsureInstalled(voice.Locale);
    }

    private static async Task DownloadOfflineVoiceAsync(string code)
    {
        if (Speech.VoiceCatalog.PiperFallbackFor(code) is not { } piper || Speech.VoiceStore.IsDownloaded(piper))
            return;
        var progress = new Progress<int>(pct =>
        {
            VoiceDownloadText = L.DownloadingVoice(piper.DisplayName, pct);
            Changed?.Invoke();
        });
        try
        {
            await Speech.VoiceStore.DownloadAsync(piper, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Not fatal: the app tries the download again the first time this voice is needed.
            Log.Info($"Offline voice download failed ({piper.Id}): {ex.Message}");
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            VoiceDownloadText = null;
            Changed?.Invoke();
        });
    }

    private static readonly HashSet<string> WithOfflineVoice = [];

    /// <summary>Progress of an offline voice being downloaded in the background.</summary>
    public static string? VoiceDownloadText { get; private set; }

    /// <summary>What is running in the background right now, if anything.</summary>
    public static string? BackgroundStatus => Current?.Text ?? VoiceDownloadText;

    /// <summary>Installs the language's text recognition.</summary>
    public static void Install(string tag) =>
        Start(tag, p => Installer.InstallAsync(tag, p), L.Installing, L.InstallDone, L.InstallCanceled, L.InstallFailed);

    /// <summary>Installs the language's Windows voice.</summary>
    public static void InstallVoice(string tag) =>
        Start(tag, p => Installer.InstallVoiceAsync(tag, p), L.Installing, L.InstallDone, L.InstallCanceled, L.InstallFailed, installsVoice: true);

    /// <summary>Removes OCR + voice and takes an added language out of the voice menus.</summary>
    public static void Remove(string tag) =>
        Start(tag, p => Installer.RemoveAsync(tag, p), L.Removing, L.RemoveDone, L.RemoveCanceled, L.RemoveFailed,
            onSuccess: () => ForgetLanguage(tag));

    /// <summary>After a removal the language goes back to the "Add language" list, unless it is a built-in one.</summary>
    private static void ForgetLanguage(string tag)
    {
        string code = tag.Split('-')[0].ToLowerInvariant();
        if (HasReadingLanguage(code))
            return; // another variant of it is still installed
        Settings.AppSettings.Update(s =>
        {
            s.AddedLanguages.Remove(code);
            if (!Speech.VoiceCatalog.IsBuiltIn(code))
            {
                s.Voices.Remove(code);
                if (s.LastVoiceLanguage == code)
                    s.LastVoiceLanguage = null;
            }
        });
    }

    private static async void Start(string tag, Func<IProgress<int>, Task<InstallResult>> operation,
        Func<string, string> running, Func<string, string> done, string canceled, string failed, bool installsVoice = false, Action? onSuccess = null)
    {
        if (IsBusy)
            return;

        string name = L.LanguageName(tag);
        Current = new Operation(tag, name, running, 0);
        LastResult = null;
        Changed?.Invoke();

        var progress = new Progress<int>(pct =>
        {
            if (Current is not null && pct != Current.Percent)
            {
                Current = Current with { Percent = pct };
                Changed?.Invoke();
            }
        });

        InstallResult result;
        try
        {
            result = await operation(progress);
        }
        catch (Exception ex)
        {
            Log.Info("Reading language error: " + ex.Message);
            result = InstallResult.Failed;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Current = null;
            RestartRecommended = installsVoice && result == InstallResult.Installed;
            if (result == InstallResult.Installed)
                onSuccess?.Invoke();
            LastResult = result switch
            {
                InstallResult.Installed => (done(name), StatusKind.Success),
                InstallResult.Canceled => (canceled, StatusKind.Info),
                _ => (failed, StatusKind.Warning),
            };
            Changed?.Invoke();

            // Next one in the queue (if any).
            if (result != InstallResult.Canceled && Pending.TryDequeue(out var next))
                EnsureInstalled(next);
            else
                Pending.Clear();
        });
    }
}

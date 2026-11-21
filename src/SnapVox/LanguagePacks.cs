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
    public static bool HasReadingLanguage(string tagOrCode)
    {
        string code = tagOrCode.Split('-')[0];
        return InstalledOcr.Any(t => t.StartsWith(code + "-", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Makes sure the reading language (OCR + Windows voice) is installed. Used when the user picks
    /// a voice: so the text is recognized in the right language (accents) and the voice doesn't read in the wrong one.
    /// If an installation is already running, it is queued.
    /// </summary>
    public static void EnsureInstalled(string locale)
    {
        if (HasReadingLanguage(locale) || !InstallableLanguages.Contains(locale))
            return;
        if (Current?.Tag == locale || Pending.Contains(locale))
            return;
        if (IsBusy)
            Pending.Enqueue(locale);
        else
            Install(locale);
    }

    /// <summary>Installs OCR + voice.</summary>
    public static void Install(string tag) =>
        Start(tag, p => Installer.InstallAsync(tag, p), L.Installing, L.InstallDone, L.InstallCanceled, L.InstallFailed);

    /// <summary>Installs only the voice (for a language whose OCR was already installed).</summary>
    public static void InstallVoice(string tag) =>
        Start(tag, p => Installer.InstallVoiceAsync(tag, p), L.Installing, L.InstallDone, L.InstallCanceled, L.InstallFailed);

    /// <summary>Removes OCR + voice.</summary>
    public static void Remove(string tag) =>
        Start(tag, p => Installer.RemoveAsync(tag, p), L.Removing, L.RemoveDone, L.RemoveCanceled, L.RemoveFailed);

    private static async void Start(string tag, Func<IProgress<int>, Task<InstallResult>> operation,
        Func<string, string> running, Func<string, string> done, string canceled, string failed)
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

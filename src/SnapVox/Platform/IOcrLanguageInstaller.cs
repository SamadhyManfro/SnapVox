namespace SnapVox.Platform;

public enum InstallResult { Installed, Canceled, Failed }

/// <summary>
/// Reading languages: text recognition (OCR) package + that language's system voice.
/// Installing and removing ask for administrator permission.
/// </summary>
public interface IOcrLanguageInstaller
{
    /// <summary>All languages the system can install (BCP-47 tags).</summary>
    IReadOnlyList<string> InstallableLanguages { get; }

    /// <summary>Languages that already have a system voice installed (BCP-47 tags).</summary>
    IReadOnlyList<string> InstalledVoiceLanguages { get; }

    /// <summary>Installs OCR + voice. <paramref name="progress"/> receives 0–100.</summary>
    Task<InstallResult> InstallAsync(string languageTag, IProgress<int>? progress = null);

    /// <summary>Installs only the voice (for languages whose OCR is already installed).</summary>
    Task<InstallResult> InstallVoiceAsync(string languageTag, IProgress<int>? progress = null);

    /// <summary>Removes the language's OCR + voice.</summary>
    Task<InstallResult> RemoveAsync(string languageTag, IProgress<int>? progress = null);
}

/// <summary>Start the app together with the system.</summary>
public interface IStartupService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

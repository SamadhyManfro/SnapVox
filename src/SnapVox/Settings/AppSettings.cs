using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnapVox.Settings;

/// <summary>User settings, saved to settings.json in <see cref="AppPaths.Roaming"/>.</summary>
public sealed class AppSettings
{
    /// <summary>Chosen voice per language: 2-letter code → voice id (see VoiceCatalog). Empty = default.</summary>
    public Dictionary<string, string> Voices { get; set; } = new();

    /// <summary>Language of the last chosen voice (the "Voice" button shows that language's voice).</summary>
    public string? LastVoiceLanguage { get; set; }

    /// <summary>Where the island sits: "top", "left" or "right" (always centered; vertical on the sides).</summary>
    public string IslandPosition { get; set; } = "top";

    /// <summary>Reading speed (0.5 to 2). Changing it on the island changes it here too.</summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>"Save MP3" folder. Empty = Downloads.</summary>
    public string? SaveFolder { get; set; }

    /// <summary>Check for a new version on start and ask whether to update.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Use the Windows voice when the others fail (e.g. offline).</summary>
    public bool WindowsVoiceFallback { get; set; } = true;

    /// <summary>May the selected text be sent to Microsoft to generate the voice? Null = not asked yet.</summary>
    public bool? AllowOnlineVoices { get; set; }

    /// <summary>Version the user answered "not now" to (we don't ask about it again).</summary>
    public string? SkippedUpdate { get; set; }

    private static readonly string FilePath = Path.Combine(AppPaths.Roaming, "settings.json");

    public static AppSettings Current { get; private set; } = Load();

    /// <summary>Raised after a change is saved.</summary>
    public static event Action? Changed;

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings) ?? new();
        }
        catch (Exception ex)
        {
            Log.Info("Invalid settings, using defaults: " + ex.Message);
        }
        return new();
    }

    public static void Update(Action<AppSettings> change)
    {
        change(Current);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, SettingsJson.Default.AppSettings));
        }
        catch (Exception ex)
        {
            Log.Info("Couldn't save settings: " + ex.Message);
        }
        Changed?.Invoke();
    }
}

/// <summary>Compile-time generated serialization (AOT friendly).</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJson : JsonSerializerContext;

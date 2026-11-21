namespace SnapVox.Speech;

public enum VoiceEngine { Piper, Edge }

/// <summary>
/// A voice the user can choose. Piper runs on the PC (the model is downloaded once);
/// Edge is online (Microsoft voices).
/// </summary>
public sealed record VoiceInfo(string Id, VoiceEngine Engine, string Locale, string Name, bool Female, double DownloadMb = 0)
{
    /// <summary>2-letter language code (e.g. "pt").</summary>
    public string Language => Locale.Split('-')[0];

    /// <summary>Display name: Edge voices get "Microsoft"; Piper voices need no label.</summary>
    public string DisplayName => Engine == VoiceEngine.Edge ? $"{Name} · Microsoft" : Name;
}

/// <summary>Available voices per language. Default: Edge (Microsoft); backup: Piper; last: the Windows voice.</summary>
internal static class VoiceCatalog
{
    public static IReadOnlyList<VoiceInfo> All { get; } =
    [
        // ---- Piper (offline backup) ----
        Piper("pt_BR-faber-medium", "pt-BR", "Faber", female: false),
        Piper("pt_BR-dii-high", "pt-BR", "Dii", female: true),
        Piper("pt_BR-cadu-medium", "pt-BR", "Cadu", female: false),
        Piper("pt_BR-jeff-medium", "pt-BR", "Jeff", female: false),
        Piper("en_US-lessac-medium", "en-US", "Lessac", female: true),
        Piper("en_US-amy-medium", "en-US", "Amy", female: true),
        Piper("en_US-ryan-medium", "en-US", "Ryan", female: false),
        Piper("es_MX-claude-high", "es-MX", "Claude", female: true),
        Piper("es_ES-davefx-medium", "es-ES", "Davefx", female: false),
        Piper("fr_FR-siwis-medium", "fr-FR", "Siwis", female: true),
        Piper("fr_FR-tom-medium", "fr-FR", "Tom", female: false),
        Piper("de_DE-thorsten-medium", "de-DE", "Thorsten", female: false),
        Piper("de_DE-dii-high", "de-DE", "Dii", female: true),
        Piper("it_IT-paola-medium", "it-IT", "Paola", female: true),
        Piper("it_IT-riccardo-x_low", "it-IT", "Riccardo", female: false, mb: 25.3),

        // ---- Edge (online, Microsoft) ----
        Edge("pt-BR-FranciscaNeural", "pt-BR", "Francisca", female: true),
        Edge("pt-BR-ThalitaMultilingualNeural", "pt-BR", "Thalita", female: true),
        Edge("pt-BR-AntonioNeural", "pt-BR", "Antonio", female: false),
        Edge("en-US-AriaNeural", "en-US", "Aria", female: true),
        Edge("en-US-AvaMultilingualNeural", "en-US", "Ava", female: true),
        Edge("en-US-AndrewNeural", "en-US", "Andrew", female: false),
        Edge("es-MX-DaliaNeural", "es-MX", "Dalia", female: true),
        Edge("es-MX-JorgeNeural", "es-MX", "Jorge", female: false),
        Edge("fr-FR-DeniseNeural", "fr-FR", "Denise", female: true),
        Edge("fr-FR-HenriNeural", "fr-FR", "Henri", female: false),
        Edge("de-DE-KatjaNeural", "de-DE", "Katja", female: true),
        Edge("de-DE-ConradNeural", "de-DE", "Conrad", female: false),
        Edge("it-IT-ElsaNeural", "it-IT", "Elsa", female: true),
        Edge("it-IT-DiegoNeural", "it-IT", "Diego", female: false),
    ];

    /// <summary>Default voice per language: Edge (Microsoft).</summary>
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["pt"] = "pt-BR-FranciscaNeural",
        ["en"] = "en-US-AriaNeural",
        ["es"] = "es-MX-DaliaNeural",
        ["fr"] = "fr-FR-DeniseNeural",
        ["de"] = "de-DE-KatjaNeural",
        ["it"] = "it-IT-ElsaNeural",
    };

    /// <summary>Backup Piper voice per language (used if Edge fails).</summary>
    private static readonly Dictionary<string, string> PiperFallbacks = new()
    {
        ["pt"] = "pt_BR-faber-medium",
        ["en"] = "en_US-lessac-medium",
        ["es"] = "es_MX-claude-high",
        ["fr"] = "fr_FR-siwis-medium",
        ["de"] = "de_DE-thorsten-medium",
        ["it"] = "it_IT-paola-medium",
    };

    public static VoiceInfo? Find(string? id) => All.FirstOrDefault(v => v.Id == id);

    public static IEnumerable<string> Languages => All.Select(v => v.Language).Distinct();

    public static IEnumerable<VoiceInfo> ForLanguage(string language) => All.Where(v => v.Language == language);

    /// <summary>The language's default voice (Edge), or null if the language isn't in the catalog.</summary>
    public static VoiceInfo? DefaultFor(string language) =>
        Defaults.TryGetValue(language, out var id) ? Find(id) : null;

    /// <summary>The language's backup Piper voice.</summary>
    public static VoiceInfo? PiperFallbackFor(string language) =>
        PiperFallbacks.TryGetValue(language, out var id) ? Find(id) : null;

    /// <summary>The voice the user chose for the language, or the default.</summary>
    public static VoiceInfo? ChosenFor(string language)
    {
        var chosen = Settings.AppSettings.Current.Voices.GetValueOrDefault(language);
        return Find(chosen) ?? DefaultFor(language);
    }

    private static VoiceInfo Piper(string id, string locale, string name, bool female, double mb = 64.1) =>
        new(id, VoiceEngine.Piper, locale, name, female, mb);

    private static VoiceInfo Edge(string id, string locale, string name, bool female) =>
        new(id, VoiceEngine.Edge, locale, name, female);
}

namespace SnapVox.Speech;

public enum VoiceEngine { Piper, Edge, Windows }

/// <summary>
/// A voice the user can choose. Piper runs on the PC (the model is downloaded once);
/// Edge is online (Microsoft voices).
/// </summary>
public sealed record VoiceInfo(string Id, VoiceEngine Engine, string Locale, string Name, bool Female, double DownloadMb = 0)
{
    /// <summary>2-letter language code (e.g. "pt").</summary>
    public string Language => Locale.Split('-')[0];

    /// <summary>Display name: says whether the voice needs the internet.</summary>
    public string DisplayName => $"{Name} · {(Engine == VoiceEngine.Edge ? "Online" : "Offline")}";
}

/// <summary>Available voices per language. Default: online (Microsoft); backup: Piper; last: the Windows voice.</summary>
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
        Piper("ar_JO-kareem-medium", "ar-SA", "Kareem", female: false),
        Piper("cs_CZ-jirka-medium", "cs-CZ", "Jirka", female: false),
        Piper("da_DK-talesyntese-medium", "da-DK", "Talesyntese", female: false),
        Piper("el_GR-rapunzelina-low", "el-GR", "Rapunzelina", female: true, mb: 64),
        Piper("fi_FI-harri-medium", "fi-FI", "Harri", female: false),
        Piper("hu_HU-anna-medium", "hu-HU", "Anna", female: true),
        Piper("no_NO-talesyntese-medium", "nb-NO", "Talesyntese", female: false),
        Piper("nl_NL-pim-medium", "nl-NL", "Pim", female: false),
        Piper("pl_PL-gosia-medium", "pl-PL", "Gosia", female: true),
        Piper("ro_RO-mihai-medium", "ro-RO", "Mihai", female: false),
        Piper("ru_RU-irina-medium", "ru-RU", "Irina", female: true, mb: 64),
        Piper("sk_SK-lili-medium", "sk-SK", "Lili", female: true),
        Piper("sl_SI-artur-medium", "sl-SI", "Artur", female: false, mb: 63.9),
        Piper("sr_RS-serbski_institut-medium", "sr-Latn-RS", "Serbski", female: false, mb: 76.6),
        Piper("sv_SE-lisa-medium", "sv-SE", "Lisa", female: true),
        Piper("tr_TR-fahrettin-medium", "tr-TR", "Fahrettin", female: false),
        Piper("zh_CN-huayan-medium", "zh-CN", "Huayan", female: true),

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

        // ---- Online voices of the languages the user can add ----
        Edge("ar-SA-ZariyahNeural", "ar-SA", "Zariyah", female: true),
        Edge("ar-SA-HamedNeural", "ar-SA", "Hamed", female: false),
        Edge("bg-BG-KalinaNeural", "bg-BG", "Kalina", female: true),
        Edge("bg-BG-BorislavNeural", "bg-BG", "Borislav", female: false),
        Edge("bs-BA-VesnaNeural", "bs-Latn-BA", "Vesna", female: true),
        Edge("bs-BA-GoranNeural", "bs-Latn-BA", "Goran", female: false),
        Edge("cs-CZ-VlastaNeural", "cs-CZ", "Vlasta", female: true),
        Edge("cs-CZ-AntoninNeural", "cs-CZ", "Antonin", female: false),
        Edge("da-DK-ChristelNeural", "da-DK", "Christel", female: true),
        Edge("da-DK-JeppeNeural", "da-DK", "Jeppe", female: false),
        Edge("el-GR-AthinaNeural", "el-GR", "Athina", female: true),
        Edge("el-GR-NestorasNeural", "el-GR", "Nestoras", female: false),
        Edge("fi-FI-NooraNeural", "fi-FI", "Noora", female: true),
        Edge("fi-FI-HarriNeural", "fi-FI", "Harri", female: false),
        Edge("hr-HR-GabrijelaNeural", "hr-HR", "Gabrijela", female: true),
        Edge("hr-HR-SreckoNeural", "hr-HR", "Srecko", female: false),
        Edge("hu-HU-NoemiNeural", "hu-HU", "Noemi", female: true),
        Edge("hu-HU-TamasNeural", "hu-HU", "Tamas", female: false),
        Edge("ja-JP-NanamiNeural", "ja-JP", "Nanami", female: true),
        Edge("ja-JP-KeitaNeural", "ja-JP", "Keita", female: false),
        Edge("ko-KR-SunHiNeural", "ko-KR", "SunHi", female: true),
        Edge("ko-KR-InJoonNeural", "ko-KR", "InJoon", female: false),
        Edge("nb-NO-PernilleNeural", "nb-NO", "Pernille", female: true),
        Edge("nb-NO-FinnNeural", "nb-NO", "Finn", female: false),
        Edge("nl-NL-ColetteNeural", "nl-NL", "Colette", female: true),
        Edge("nl-NL-MaartenNeural", "nl-NL", "Maarten", female: false),
        Edge("pl-PL-ZofiaNeural", "pl-PL", "Zofia", female: true),
        Edge("pl-PL-MarekNeural", "pl-PL", "Marek", female: false),
        Edge("ro-RO-AlinaNeural", "ro-RO", "Alina", female: true),
        Edge("ro-RO-EmilNeural", "ro-RO", "Emil", female: false),
        Edge("ru-RU-SvetlanaNeural", "ru-RU", "Svetlana", female: true),
        Edge("ru-RU-DmitryNeural", "ru-RU", "Dmitry", female: false),
        Edge("sk-SK-ViktoriaNeural", "sk-SK", "Viktoria", female: true),
        Edge("sk-SK-LukasNeural", "sk-SK", "Lukas", female: false),
        Edge("sl-SI-PetraNeural", "sl-SI", "Petra", female: true),
        Edge("sl-SI-RokNeural", "sl-SI", "Rok", female: false),
        Edge("sr-RS-SophieNeural", "sr-Latn-RS", "Sophie", female: true),
        Edge("sr-RS-NicholasNeural", "sr-Latn-RS", "Nicholas", female: false),
        Edge("sv-SE-SofieNeural", "sv-SE", "Sofie", female: true),
        Edge("sv-SE-MattiasNeural", "sv-SE", "Mattias", female: false),
        Edge("tr-TR-EmelNeural", "tr-TR", "Emel", female: true),
        Edge("tr-TR-AhmetNeural", "tr-TR", "Ahmet", female: false),
        Edge("zh-CN-XiaoxiaoNeural", "zh-CN", "Xiaoxiao", female: true),
        Edge("zh-CN-YunxiNeural", "zh-CN", "Yunxi", female: false),
    ];

    // Languages whose text the English/Latin OCR can't read at all: they need their own OCR first.
    private static readonly HashSet<string> NonLatinLanguages = ["ar", "bg", "el", "ja", "ko", "ru", "zh"];

    public static bool NeedsOwnOcr(string language) => NonLatinLanguages.Contains(language);

    /// <summary>Languages that are always in the voice menus.</summary>
    public static bool IsBuiltIn(string language) => Defaults.ContainsKey(language);

    /// <summary>Can this language be read right now with some voice (Piper, online if allowed, or Windows)?</summary>
    public static bool HasUsableVoice(string language) =>
        All.Any(v => v.Language == language
                     && (v.Engine == VoiceEngine.Piper || Settings.AppSettings.Current.AllowOnlineVoices != false))
        || LanguagePacks.HasWindowsVoice(language);

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
        ["ar"] = "ar_JO-kareem-medium",
        ["cs"] = "cs_CZ-jirka-medium",
        ["da"] = "da_DK-talesyntese-medium",
        ["el"] = "el_GR-rapunzelina-low",
        ["fi"] = "fi_FI-harri-medium",
        ["hu"] = "hu_HU-anna-medium",
        ["nb"] = "no_NO-talesyntese-medium",
        ["nl"] = "nl_NL-pim-medium",
        ["pl"] = "pl_PL-gosia-medium",
        ["ro"] = "ro_RO-mihai-medium",
        ["ru"] = "ru_RU-irina-medium",
        ["sk"] = "sk_SK-lili-medium",
        ["sl"] = "sl_SI-artur-medium",
        ["sr"] = "sr_RS-serbski_institut-medium",
        ["sv"] = "sv_SE-lisa-medium",
        ["tr"] = "tr_TR-fahrettin-medium",
        ["zh"] = "zh_CN-huayan-medium",
    };

    private const string WindowsVoicePrefix = "windows-";

    // Languages Windows offers no voice package for.
    private static readonly HashSet<string> NoWindowsVoicePackage = ["bs", "sr"];

    public static VoiceInfo? Find(string? id)
    {
        if (id is not null && id.StartsWith(WindowsVoicePrefix, StringComparison.Ordinal))
            return WindowsVoice(id[WindowsVoicePrefix.Length..]);
        return All.FirstOrDefault(v => v.Id == id);
    }

    public static IEnumerable<string> Languages => All.Select(v => v.Language).Distinct();

    /// <summary>The language's voices. Languages with no Piper voice can use their installed Windows voice offline.</summary>
    public static IEnumerable<VoiceInfo> ForLanguage(string language)
    {
        var voices = All.Where(v => v.Language == language).ToList();
        if (voices.All(v => v.Engine != VoiceEngine.Piper) && WindowsVoice(language) is { } windows)
            voices.Add(windows);
        return voices;
    }

    private static VoiceInfo? WindowsVoice(string language)
    {
        var known = All.FirstOrDefault(v => v.Language == language);
        return known is not null && LanguagePacks.HasWindowsVoice(language)
            ? new VoiceInfo(WindowsVoicePrefix + language, VoiceEngine.Windows, known.Locale, "Windows", known.Female)
            : null;
    }

    /// <summary>No offline voice yet, but Windows has a voice package for this language.</summary>
    public static bool CanInstallWindowsVoice(string language) =>
        All.Any(v => v.Language == language)
        && All.Where(v => v.Language == language).All(v => v.Engine != VoiceEngine.Piper)
        && !NoWindowsVoicePackage.Contains(language)
        && !LanguagePacks.HasWindowsVoice(language);

    /// <summary>
    /// Languages shown in the voice menus: the built-in ones, those the user added or chose a voice for,
    /// and those with a reading language installed.
    /// </summary>
    public static IReadOnlyList<string> ActiveLanguages
    {
        get
        {
            var settings = Settings.AppSettings.Current;
            var active = new HashSet<string>(Defaults.Keys);
            active.UnionWith(settings.AddedLanguages);
            active.UnionWith(settings.Voices.Keys);
            active.UnionWith(LanguagePacks.InstalledOcr.Select(t => t.Split('-')[0].ToLowerInvariant()));
            return Languages.Where(active.Contains).ToList();
        }
    }

    /// <summary>Languages that can still be added.</summary>
    public static IReadOnlyList<string> LanguagesToAdd
    {
        get
        {
            var active = ActiveLanguages;
            return Languages.Where(l => !active.Contains(l)).ToList();
        }
    }

    /// <summary>The language's default voice (online), or null if the language isn't in the catalog.</summary>
    public static VoiceInfo? DefaultFor(string language) =>
        Defaults.TryGetValue(language, out var id)
            ? Find(id)
            : All.FirstOrDefault(v => v.Language == language && v.Engine == VoiceEngine.Edge);

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

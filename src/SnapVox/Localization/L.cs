using System.Globalization;

namespace SnapVox.Localization;

/// <summary>
/// UI texts. They always follow the Windows language: Portuguese if the system is in Portuguese,
/// otherwise English. (A simple dictionary instead of .resx to stay AOT friendly.)
/// </summary>
internal static class L
{
    public static readonly bool Pt =
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase)
        || CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase);

    // Tray and island
    public static string ReadArea => Pt ? "Ler área da tela" : "Read screen area";
    public static string SettingsMenu => Pt ? "Configurações" : "Settings";
    public static string Quit => Pt ? "Sair" : "Quit";
    public static string SelectText => Pt ? "Selecione o texto" : "Select the text";
    public static string Reading => Pt ? "Reconhecendo o texto…" : "Recognizing text…";
    public static string NoText => Pt ? "Nenhum texto encontrado" : "No text found";
    public static string ImproveReading => Pt ? "Melhorar leitura" : "Improve reading";
    public static string Words(int count) => Pt ? $"{count} palavras" : $"{count} words";
    public static string Ready(string hotkey) => Pt ? $"SnapVox pronto · {hotkey}" : $"SnapVox ready · {hotkey}";
    public static string HotkeyInUse(string hotkey) =>
        Pt ? $"{hotkey} já está em uso por outro programa" : $"{hotkey} is already used by another app";

    // Settings
    public static string Shortcut => Pt ? "Atalho" : "Shortcut";
    public static string Change => Pt ? "Alterar" : "Change";
    public static string IslandPosition => Pt ? "Posição da ilha" : "Island position";
    public static string Top => Pt ? "Topo" : "Top";
    public static string Left => Pt ? "Esquerda" : "Left";
    public static string Right => Pt ? "Direita" : "Right";
    public static string Voice => Pt ? "Voz" : "Voice";
    public static string VoiceAuto => Pt ? "Automática pelo idioma" : "Automatic by language";
    public static string DefaultSpeed => Pt ? "Velocidade padrão" : "Default speed";
    public static string SaveMp3To => Pt ? "Salvar MP3 em" : "Save MP3 to";
    public static string Downloads => "Downloads";
    public static string StartWithWindows => Pt ? "Iniciar com o Windows" : "Start with Windows";
    public static string OfflineVoice => Pt ? "Voz do Windows sem internet" : "Windows voice when offline";
    public static string OnlineVoices => Pt ? "Vozes online da Microsoft" : "Microsoft online voices";
    public static string PrivacyTitle => Pt ? "Usar as vozes da Microsoft?" : "Use Microsoft voices?";
    public static string PrivacyBody => Pt
        ? "Para gerar a voz, o texto selecionado é enviado à Microsoft. Evite ler senhas ou dados sensíveis. As vozes offline ficam no seu PC (baixam cerca de 64 MB uma vez). Você pode mudar isso nas configurações."
        : "To generate the voice, the selected text is sent to Microsoft. Avoid reading passwords or sensitive data. Offline voices stay on your PC (a one-time download of about 64 MB). You can change this in Settings.";
    public static string PrivacyOffline => Pt ? "Só vozes offline" : "Offline voices only";
    public static string PrivacyAccept => Pt ? "Usar Microsoft" : "Use Microsoft";
    public static string AutoUpdate => Pt ? "Atualização automática" : "Automatic updates";
    public static string InterfaceLanguage => Pt ? "Idioma da interface" : "Interface language";
    public static string SystemLanguage => Pt ? "Idioma do sistema" : "System language";
    public static string ReadingLanguages => Pt ? "Idiomas de leitura" : "Reading languages";
    public static string ReadingLanguagesHint =>
        Pt ? "Instalados automaticamente quando você escolhe uma voz. Clique num idioma para remover."
           : "Installed automatically when you choose a voice. Click a language to remove it.";
    public static string SearchLanguage => Pt ? "Instalar idioma de leitura…" : "Install a reading language…";
    public static string Install => Pt ? "Instalar" : "Install";
    public static string Installed => Pt ? "Instalado" : "Installed";
    public static string Installing(string lang) => Pt ? $"Instalando {lang}… pode levar alguns minutos" : $"Installing {lang}… this can take a few minutes";
    public static string Listening(string lang) => Pt ? $"Lendo · {lang}" : $"Reading · {lang}";
    public static string Paused => Pt ? "Pausado" : "Paused";
    public static string Finished => Pt ? "Leitura concluída" : "Finished reading";
    public static string OfflineVoiceUsed => Pt ? "Sem internet · voz do Windows" : "Offline · Windows voice";
    public static string VoiceFailed => Pt ? "Não foi possível gerar a voz" : "Couldn't generate the voice";
    public static string InstallDone(string lang) => Pt ? $"{lang} instalado" : $"{lang} installed";
    public static string InstallCanceled => Pt ? "Instalação cancelada" : "Installation canceled";
    public static string Remove => Pt ? "Remover" : "Remove";
    public static string Removing(string lang) => Pt ? $"Removendo {lang}…" : $"Removing {lang}…";
    public static string RemoveDone(string lang) => Pt ? $"{lang} removido" : $"{lang} removed";
    public static string RemoveCanceled => Pt ? "Remoção cancelada" : "Removal canceled";
    public static string RemoveFailed => Pt ? "Não foi possível remover." : "Couldn't remove.";
    public static string InstallVoice => Pt ? "Instalar voz do Windows" : "Install Windows voice";
    public static string NoVoice => Pt ? "sem voz" : "no voice";
    public static string SystemLanguageLocked =>
        Pt ? "Idioma do sistema: não pode ser removido" : "System language: can't be removed";
    public static string RestartForVoice =>
        Pt ? "Reinicie o SnapVox para usar a nova voz." : "Restart SnapVox to use the new voice.";
    public static string RestartApp => Pt ? "Reiniciar o SnapVox" : "Restart SnapVox";
    public static string NewSelection => Pt ? "Selecionar outro texto" : "Select another text";
    public static string Speed => Pt ? "Velocidade" : "Speed";
    public static string SaveMp3 => Pt ? "Salvar MP3" : "Save MP3";
    public static string SaveAs => Pt ? "Salvar como…" : "Save as…";
    public static string Close => Pt ? "Fechar" : "Close";
    public static string OtherLanguages => Pt ? "Ler em outro idioma" : "Read in another language";
    public static string PreviousWord => Pt ? "Palavra anterior" : "Previous word";
    public static string NextWord => Pt ? "Próxima palavra" : "Next word";
    public static string PlayPause => Pt ? "Tocar / pausar (Espaço)" : "Play / pause (Space)";
    public static string DragHint => Pt ? "Arraste sobre o texto na tela" : "Drag over the text on the screen";
    public static string OneMoment => Pt ? "Só um instante" : "Just a moment";
    public static string GeneratingVoice(string voice) => Pt ? $"Gerando a voz · {voice}" : $"Generating voice · {voice}";
    public static string FolderName(string? path) =>
        string.IsNullOrEmpty(path) ? Downloads : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : path;
    public static string SavedTo(string folder) => Pt ? $"MP3 salvo em {folder}" : $"MP3 saved to {folder}";
    public static string SaveFailed => Pt ? "Não foi possível salvar o MP3" : "Couldn't save the MP3";
    public static string Saving => Pt ? "Salvando o MP3…" : "Saving MP3…";
    public static string CheckUpdatesNow => Pt ? "Procurar agora" : "Check now";
    public static string UpToDate => Pt ? "Você está na versão mais nova" : "You're up to date";
    public static string UpdateAvailable(string v) => Pt ? $"Nova versão {v} disponível" : $"Version {v} is available";
    public static string UpdateQuestion => Pt ? "Quer atualizar o SnapVox agora?" : "Update SnapVox now?";
    public static string UpdateNow => Pt ? "Atualizar" : "Update";
    public static string NotNow => Pt ? "Agora não" : "Not now";
    public static string DownloadingUpdate(int pct) => Pt ? $"Baixando a atualização… {pct}%" : $"Downloading update… {pct}%";
    public static string UpdateFailed => Pt ? "Não foi possível baixar a atualização" : "Couldn't download the update";
    public static string CheckingUpdates => Pt ? "Procurando atualizações…" : "Checking for updates…";
    public static string UpdateCheckFailed => Pt ? "Não foi possível verificar agora" : "Couldn't check right now";
    public static string ChooseFolder => Pt ? "Escolher pasta do MP3" : "Choose MP3 folder";
    public static string SendSuggestion => Pt ? "Enviar sugestão" : "Send a suggestion";
    public static string SuggestionTitle => Pt ? "Sugestão: " : "Suggestion: ";
    public static string SuggestionTemplate => Pt
        ? "Descreva sua sugestão ou o problema que encontrou:\n\n"
        : "Describe your suggestion or the problem you found:\n\n";
    public static string DownloadingVoice(string voice, int pct) =>
        Pt ? $"Baixando a voz {voice}… {pct}%" : $"Downloading voice {voice}… {pct}%";
    public static string Female => Pt ? "feminina" : "female";
    public static string Male => Pt ? "masculina" : "male";
    public static string Default => Pt ? "padrão" : "default";
    public static string DownloadSize(double mb) => Pt ? $"baixa {mb:0} MB" : $"{mb:0} MB download";
    public static string VoicesOnline => Pt ? "online" : "online";
    public static string MissingVoice(string lang) =>
        Pt ? $"Sem voz em {lang}" : $"No {lang} voice";
    public static string InstallFailed => Pt ? "Não foi possível instalar. Verifique a internet e tente de novo." : "Couldn't install. Check your internet and try again.";
    public static string NoMatch => Pt ? "Nenhum idioma encontrado" : "No language found";
    public static string ComingSoon => Pt ? "em breve" : "soon";
    public static string OpenSource => Pt ? "código aberto no GitHub" : "open source on GitHub";

    /// <summary>A language's name in the UI language, e.g. "pt" → "Português" / "Portuguese".</summary>
    public static string LanguageName(string tagOrCode)
    {
        try
        {
            var culture = new CultureInfo(tagOrCode);
            string code = culture.TwoLetterISOLanguageName;
            string name = Pt ? PtNames.GetValueOrDefault(code, culture.NativeName) : culture.EnglishName.Split(" (")[0];
            name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);

            // Regional variants (pt-BR × pt-PT, en-US × en-GB…) get the country in parentheses.
            if (tagOrCode.Contains('-') && !culture.IsNeutralCulture)
            {
                var region = new RegionInfo(culture.Name);
                string country = Pt ? PtCountries.GetValueOrDefault(region.TwoLetterISORegionName, region.NativeName) : region.EnglishName;
                name += $" ({country})";
            }
            return name;
        }
        catch (Exception ex) when (ex is CultureNotFoundException or ArgumentException)
        {
            return tagOrCode;
        }
    }

    private static readonly Dictionary<string, string> PtNames = new()
    {
        ["pt"] = "português", ["en"] = "inglês", ["es"] = "espanhol", ["fr"] = "francês", ["de"] = "alemão",
        ["it"] = "italiano", ["nl"] = "holandês", ["ru"] = "russo", ["ja"] = "japonês", ["zh"] = "chinês",
        ["ko"] = "coreano", ["ar"] = "árabe", ["pl"] = "polonês", ["tr"] = "turco", ["sv"] = "sueco",
        ["da"] = "dinamarquês", ["fi"] = "finlandês", ["nb"] = "norueguês", ["cs"] = "tcheco", ["el"] = "grego",
        ["hu"] = "húngaro", ["ro"] = "romeno", ["sk"] = "eslovaco", ["sl"] = "esloveno", ["hr"] = "croata",
        ["bg"] = "búlgaro", ["sr"] = "sérvio", ["bs"] = "bósnio",
    };

    private static readonly Dictionary<string, string> PtCountries = new()
    {
        ["BR"] = "Brasil", ["PT"] = "Portugal", ["US"] = "EUA", ["GB"] = "Reino Unido", ["ES"] = "Espanha",
        ["MX"] = "México", ["FR"] = "França", ["CA"] = "Canadá", ["CN"] = "China", ["TW"] = "Taiwan", ["HK"] = "Hong Kong",
        ["RS"] = "Sérvia", ["BA"] = "Bósnia",
    };
}

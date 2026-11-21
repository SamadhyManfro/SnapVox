using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SnapVox.Localization;
using SnapVox.Ocr;
using SnapVox.Platform;
using SnapVox.Settings;
using SnapVox.Speech;
using SnapVox.UI;

namespace SnapVox;

/// <summary>Selection result: the cropped image, where it is on screen and the recognized text.</summary>
public sealed record SelectedRegion(Bitmap Image, PixelRect ScreenRect, double Scaling, OcrOutput Text);

/// <summary>
/// Runs a reading session: screen capture → dimming on every monitor → selection →
/// frozen photo → OCR + language → voice with word-by-word highlight, controlled by the island (player).
/// Dragging again at any time starts another selection. Closes with Esc or the island's ⋮ menu.
/// </summary>
internal sealed class SelectionSession
{
    private static readonly double[] Speeds = [0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0];

    private readonly IScreenCapture _capture = PlatformServices.CreateScreenCapture();
    private readonly TextRecognizer _recognizer = new(PlatformServices.CreateOcrEngine(), PlatformServices.CreateLanguageDetector());
    private readonly List<SelectionOverlay> _overlays = [];
    private readonly DispatcherTimer _tick;
    private IslandWindow? _island;
    private SelectionOverlay? _active;
    private ScreenSnapshot? _snapshot;
    private ReadAloud? _reader;
    private CancellationTokenSource? _cts;
    private OcrOutput? _ocr;
    private bool _readingStarting;
    private IHotkeyService? _escape;
    private int _lastWord = -2;
    private int _generation; // ignores results from old selections/sessions

    public SelectionSession()
    {
        // ~30 times per second: spoken word → highlight; position → progress bar.
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => OnTick());
        AppSettings.Changed += OnSettingsChanged;
    }

    public bool IsActive => _overlays.Count > 0;

    /// <summary>
    /// Opens the selection. If it is already open (reading or finished), nothing is closed: it just stops the reading and
    /// waits for a new selection in the same place.
    /// </summary>
    public void Start()
    {
        if (IsActive)
        {
            ResetForNewSelection(source: null);
            return;
        }

        _generation++;

        // 1) Capture of every monitor at the moment of the hotkey (before any window of ours shows up).
        _snapshot = _capture.CaptureAll();

        // 2) Um escurecimento por monitor.
        var screens = ScreenInfo.All;
        foreach (var screen in screens)
        {
            var overlay = new SelectionOverlay(screen);
            overlay.SelectionCompleted += OnSelectionCompleted;
            overlay.SelectionStarted += ResetForNewSelection;
            overlay.CancelRequested += Close;
            overlay.KeyPressed += OnKey;
            _overlays.Add(overlay);
            overlay.Show();
        }

        // 3) Island on the mouse's monitor, on top of everything.
        var cursor = PlatformServices.GetCursorPosition();
        var mouseScreen = screens.FirstOrDefault(s => s.Bounds.Contains(cursor)) ?? screens[0];
        _island = new IslandWindow(mouseScreen);
        _island.SaveRequested += asDialog => _ = SaveAsync(asDialog);
        _island.CloseRequested += Close;
        _island.PlayPauseClicked += TogglePlayPause;
        _island.PreviousClicked += () => _reader?.Step(-1);
        _island.NextClicked += () => _reader?.Step(+1);
        _island.SeekRequested += f => _reader?.Seek(f);
        _island.SpeedChosen += SetSpeed;
        _island.VoiceChosen += OnVoiceChosen;
        _island.NewSelectionRequested += () => ResetForNewSelection(null);
        _island.SetSpeed(AppSettings.Current.Speed);
        _island.Show();

        // Focus the dimming on the mouse's monitor so the keyboard (Space, arrows) works.
        _overlays.FirstOrDefault(o => o.TargetScreen == mouseScreen)?.Activate();

        // Esc as a system-wide hotkey while the island is open: closes even if the focus is on another window
        // (before, Esc sometimes went elsewhere and the reading kept going).
        _escape = PlatformServices.CreateHotkeyService();
        _escape.Pressed += () => Dispatcher.UIThread.Post(OnEscape);
        if (!_escape.Register(HotkeyGesture.Escape))
            Log.Info("Couldn't register the global Esc; only the dimming's Esc will work.");
        KeepIslandOnTop();
        Log.Info($"Session started on {screens.Count} monitor(s).");
    }

    /// <summary>
    /// Clicking the dimming makes Windows put it on top of everything; the island and Settings
    /// have to come back to the front right after.
    /// </summary>
    private void KeepIslandOnTop()
    {
        Dispatcher.UIThread.Post(() =>
        {
            PlatformServices.BringToTop(_island);
            PlatformServices.BringToTop(SettingsWindow.Current);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// A new selection started (dragging again, or the hotkey with the session open): stops the reading,
    /// clears the previous selection on every monitor and puts the island back in its waiting state.
    /// A fresh screen capture is taken when the next selection is released.
    /// </summary>
    private void ResetForNewSelection(SelectionOverlay? source)
    {
        _generation++;
        _cts?.Cancel();
        _cts = null;
        _tick.Stop();
        _reader?.Dispose();
        _reader = null;
        _ocr = null;
        _active = null;
        _lastWord = -2;
        _readingStarting = false;

        foreach (var o in _overlays.Where(o => o != source))
            o.ClearSelection();

        _island?.SetImproveAction(null, null);
        _island?.ShowIdle();
        KeepIslandOnTop();
    }

    private async void OnSelectionCompleted(SelectionOverlay source, PixelRect rect)
    {
        if (_snapshot is null)
            return;

        // Fresh capture of what is on screen now (our windows are excluded from capture), so a selection made
        // after scrolling the page reads the new content. Falls back to the capture taken at the hotkey.
        try
        {
            _snapshot = _capture.CaptureAll();
        }
        catch (Exception ex)
        {
            Log.Info("Fresh capture failed, using the previous one: " + ex.Message);
        }
        var (pixels, w, h) = _snapshot.CropBgra(rect);
        var image = ScreenSnapshot.ToBitmap(pixels, w, h);

        _active = source;
        source.Freeze(image);
        KeepIslandOnTop();

        _island?.ShowLoading(L.OneMoment, L.Reading); // "Recognizing text…" with the spinner
        Log.Info($"Selection: {rect} ({w}x{h} px)");

        int generation = _generation;
        _cts = new CancellationTokenSource();
        try
        {
            // ---- OCR + language ----
            var started = DateTime.UtcNow;
            var text = await Task.Run(() => _recognizer.RecognizeAsync(pixels, w, h));
            if (generation != _generation)
                return; // closed, or another selection started while the OCR was running

            Log.Info($"OCR in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms · language {text.LanguageCode ?? "?"} " +
                     $"(OCR {text.OcrLanguageTag}) · {text.WordCount} words"); // never log the text itself: it can be private

            if (text.IsEmpty)
            {
                _island?.ShowMessage(L.NoText);
                return;
            }

            string languageName = L.LanguageName(text.LanguageCode ?? "en");

            // Detected language without OCR or without a Windows voice: offer to install it (one click).
            if (text.LanguageCode is { } code)
            {
                bool Matches(string t) => t.StartsWith(code + "-", StringComparison.OrdinalIgnoreCase);
                bool hasOcr = _recognizer.OcrLanguages.Any(Matches);
                bool hasVoice = LanguagePacks.InstalledVoices.Any(Matches);
                if (!hasOcr)
                {
                    // Install the language right away (reading + Windows voice) in the chosen voice's variant (e.g. pt-BR).
                    string locale = VoiceCatalog.ChosenFor(code)?.Locale
                                    ?? LanguagePacks.InstallableLanguages.FirstOrDefault(Matches) ?? code;
                    _island?.SetImproveAction(L.ImproveReading, () => { LanguagePacks.EnsureInstalled(locale); SettingsWindow.Open(); });
                }
                else if (!hasVoice && _recognizer.OcrLanguages.FirstOrDefault(Matches) is { } tag)
                    _island?.SetImproveAction(L.MissingVoice(languageName), () => { LanguagePacks.InstallVoice(tag); SettingsWindow.Open(); });
            }

            // ---- Voice ----
            _ocr = text;
            await StartReadingAsync(fromWordIndex: 0);
        }
        catch (OperationCanceledException)
        {
            // closed halfway through
        }
        catch (Exception ex)
        {
            Log.Info("Reading error: " + ex);
            if (generation == _generation)
                _island?.ShowMessage(_reader is null ? L.NoText : L.VoiceFailed);
        }
    }

    /// <summary>
    /// Generates the voice (with the currently chosen voice) and starts reading from a given word.
    /// Used for the first reading and when the user changes voice/language or listens again with another voice.
    /// </summary>
    private async Task StartReadingAsync(int fromWordIndex)
    {
        if (_ocr is null || _cts is null)
            return;
        int generation = _generation;
        _readingStarting = true;

        _tick.Stop();
        _reader?.Dispose();
        var reader = _reader = new ReadAloud();
        reader.Ended += () => Dispatcher.UIThread.Post(OnReadingEnded);
        reader.DownloadProgress += (voice, pct) => Dispatcher.UIThread.Post(() =>
        {
            if (generation == _generation)
                _island?.ShowLoading(L.DownloadingVoice(voice.DisplayName, pct));
        });

        // Long texts take a while to generate: the player shows it is loading (spinner and running bar).
        var ocr = _ocr;
        string voiceName = VoiceCatalog.ChosenFor(ocr.LanguageCode ?? "en")?.DisplayName ?? L.LanguageName(ocr.LanguageCode ?? "en");
        _island?.ShowLoading(L.GeneratingVoice(voiceName), Title(ocr));
        var token = _cts.Token;
        try
        {
            await Task.Run(() => reader.StartAsync(ocr, token, fromWordIndex));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Info("Voice error: " + ex);
            if (generation == _generation)
                _island?.ShowMessage(L.VoiceFailed);
            return;
        }
        finally
        {
            if (generation == _generation)
                _readingStarting = false;
        }
        if (generation != _generation || reader != _reader)
            return;

        reader.Rate = AppSettings.Current.Speed;
        string lang = ocr.LanguageCode ?? "en";
        string subtitle = reader.UsedOfflineVoice
            ? $"{L.LanguageName(lang)} · {L.OfflineVoiceUsed}"
            : $"{reader.VoiceName} · {L.LanguageName(lang)}";

        _island?.ShowPlayer(Title(ocr), subtitle, lang);
        if (_island is not null)
            _island.CanSave = reader.LastAudio is not null;
        _island?.SetPlaying(true);
        _lastWord = -2;
        _tick.Start();
        KeepIslandOnTop();
    }

    /// <summary>Player title: the start of the text.</summary>
    private static string Title(OcrOutput ocr)
    {
        string text = string.Join(' ', ocr.Words.Take(14).Select(w => w.Text));
        return text.Length > 60 ? text[..60].TrimEnd() + "…" : text;
    }

    /// <summary>Voice changed in Settings while reading: continue from the current word with the new voice.</summary>
    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_reader is null || _readingStarting || !_reader.VoiceChoiceChanged)
            return;
        if (_reader.IsPlaying)
        {
            int from = Math.Max(0, _reader.CurrentWordIndex);
            Log.Info($"Voice changed while reading; generating again from word {from}.");
            _ = StartReadingAsync(from);
        }
        // Paused or finished: the new voice kicks in on ▶ / ↻.
    });

    /// <summary>
    /// Voice chosen in the player. If it belongs to another language, reads the same text in that language
    /// (fixes a wrong detection). It also installs the reading language, like in Settings.
    /// </summary>
    private void OnVoiceChosen(VoiceInfo voice)
    {
        if (_ocr is null)
            return;
        int from = _reader is { } r && r.IsPlaying ? Math.Max(0, r.CurrentWordIndex) : 0;
        bool otherLanguage = voice.Language != (_ocr.LanguageCode ?? "en");
        if (otherLanguage)
            _ocr = _ocr with { LanguageCode = voice.Language };

        _readingStarting = true; // avoids generating twice (here and in the settings-changed handler)
        AppSettings.Update(s =>
        {
            s.Voices[voice.Language] = voice.Id;
            s.LastVoiceLanguage = voice.Language;
        });
        LanguagePacks.EnsureInstalled(voice.Locale);
        _ = StartReadingAsync(from);
    }

    /// <summary>
    /// Saves the reading's audio as MP3. Without a dialog: goes to the folder from Settings (default: Downloads)
    /// with a name taken from the start of the text. With the dialog ("Save as…"): the user picks folder and name.
    /// </summary>
    private async Task SaveAsync(bool asDialog)
    {
        if (_reader?.LastAudio is not { } audio || _ocr is null || _island is null)
            return;
        var island = _island;
        var exporter = PlatformServices.CreateAudioExporter();
        string folder = string.IsNullOrWhiteSpace(AppSettings.Current.SaveFolder) || !Directory.Exists(AppSettings.Current.SaveFolder)
            ? exporter.DownloadsFolder
            : AppSettings.Current.SaveFolder!;
        string fileName = SafeFileName(Title(_ocr)) + ".mp3";
        string previous = $"{_reader.VoiceName} · {L.LanguageName(_ocr.LanguageCode ?? "en")}";

        try
        {
            Stream? target = null;
            string savedFolder = folder;
            if (asDialog)
            {
                var start = await island.StorageProvider.TryGetFolderFromPathAsync(new Uri(folder));
                var file = await island.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = L.SaveAs,
                    SuggestedFileName = fileName,
                    DefaultExtension = "mp3",
                    SuggestedStartLocation = start,
                    FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("MP3") { Patterns = ["*.mp3"] }],
                });
                if (file is null)
                    return; // cancelou
                target = await file.OpenWriteAsync();
                savedFolder = Path.GetDirectoryName(file.TryGetLocalPath() ?? folder) ?? folder;
            }

            island.SetSubtitle(L.Saving);
            byte[] mp3 = await Task.Run(() => exporter.ToMp3Async(audio.Audio, audio.MimeType));

            if (target is null)
            {
                Directory.CreateDirectory(folder);
                string path = UniquePath(Path.Combine(folder, fileName));
                await File.WriteAllBytesAsync(path, mp3);
                Log.Info($"MP3 saved: {path}");
            }
            else
            {
                await using (target)
                    await target.WriteAsync(mp3);
                Log.Info($"MP3 saved (Save as) to {savedFolder}");
            }
            island.SetSubtitle(L.SavedTo(L.FolderName(savedFolder)));
        }
        catch (Exception ex)
        {
            Log.Info("Couldn't save MP3: " + ex.Message);
            island.SetSubtitle(L.SaveFailed);
        }

        // After a few seconds, go back to showing "voice · language".
        await Task.Delay(4000);
        if (_island == island)
            island.SetSubtitle(previous);
    }

    private static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string clean = new string(title.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim().TrimEnd('…', '.');
        if (clean.Length > 50)
            clean = clean[..50].TrimEnd();
        return $"SnapVox - {clean} - {DateTime.Now:yyyy-MM-dd HHmmss}";
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;
        string dir = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private void SetSpeed(double speed)
    {
        speed = Math.Clamp(speed, 0.5, 2.0);
        if (_reader is not null)
            _reader.Rate = speed; // changes instantly, no re-generation
        _island?.SetSpeed(speed);
        AppSettings.Update(s => s.Speed = speed);
    }

    /// <summary>Keyboard (the dimming has focus): Space, ← →, ↑ ↓. Esc is handled by the global hotkey.</summary>
    private void OnKey(Key key)
    {
        switch (key)
        {
            case Key.Space:
                TogglePlayPause();
                break;
            case Key.Left:
                _reader?.Step(-1);
                break;
            case Key.Right:
                _reader?.Step(+1);
                break;
            case Key.Up or Key.Down:
            {
                double current = AppSettings.Current.Speed;
                int i = Array.FindIndex(Speeds, s => Math.Abs(s - current) < 0.01);
                if (i < 0) i = 2;
                i = Math.Clamp(i + (key == Key.Up ? 1 : -1), 0, Speeds.Length - 1);
                SetSpeed(Speeds[i]);
                break;
            }
        }
    }

    private void OnTick()
    {
        if (_reader is null)
            return;
        _island?.SetProgress(_reader.Position, _reader.Duration);
        if (_active is null)
            return;
        int index = _reader.CurrentWordIndex;
        if (index == _lastWord)
            return;
        _lastWord = index;
        _active.SetHighlight(_reader.WordAt(index)?.Box);
    }

    private void TogglePlayPause()
    {
        if (_reader is null || _readingStarting)
            return;

        if (_reader.IsPlaying)
        {
            _reader.Pause();
            _island?.SetPlaying(false);
            return;
        }

        if (_reader.VoiceChoiceChanged)
        {
            // Voice changed while paused/finished: generate again with the new voice.
            int from = _tick.IsEnabled ? Math.Max(0, _reader.CurrentWordIndex) : 0;
            _ = StartReadingAsync(from);
            return;
        }
        if (_tick.IsEnabled)
            _reader.Play();
        else
            _reader.Restart(); // finished: listen again
        _tick.Start();
        _island?.SetPlaying(true);
    }

    private void OnReadingEnded()
    {
        _tick.Stop();
        _lastWord = -2;
        _active?.SetHighlight(null);
        if (_reader is not null)
            _island?.SetProgress(_reader.Duration, _reader.Duration);
        _island?.SetPlaying(false, finished: true);
    }

    /// <summary>Esc: if Settings is in front, closes only Settings; otherwise closes everything and stops reading.</summary>
    private void OnEscape()
    {
        if (SettingsWindow.Current is { IsActive: true } settings)
        {
            settings.Close();
            return;
        }
        Close();
    }

    public void Close()
    {
        _escape?.Dispose(); // gives Esc back to other apps
        _escape = null;
        _generation++;
        _cts?.Cancel();
        _cts = null;
        _tick.Stop();
        _reader?.Dispose();
        _reader = null;
        _active = null;
        _ocr = null;
        _readingStarting = false;

        foreach (var o in _overlays)
        {
            o.StopAnimations();
            o.Close();
        }
        _overlays.Clear();
        _island?.Close();
        _island = null;
        _snapshot = null;
    }
}

/// <summary>List of monitors (uses an invisible window just to query the screens).</summary>
internal static class ScreenInfo
{
    private static Window? _probe;

    public static IReadOnlyList<Screen> All
    {
        get
        {
            _probe ??= new Window();
            return _probe.Screens.All;
        }
    }
}

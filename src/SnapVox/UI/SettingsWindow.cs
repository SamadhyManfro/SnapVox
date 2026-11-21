using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using SnapVox.Localization;
using SnapVox.Platform;
using SnapVox.Settings;

namespace SnapVox.UI;

/// <summary>
/// Settings window in the island style (black, big corners, blue light inside).
/// Items not implemented yet show up greyed out with "soon".
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static SettingsWindow? _current;

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly IBrush Black = new SolidColorBrush(Color.Parse("#0B0B0D"));
    private static readonly IBrush Hairline = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
    private static readonly IBrush ChipBg = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
    private static readonly IBrush AccentBg = new SolidColorBrush(Color.Parse("#2F7FD8"));
    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#FFB454"));
    private static readonly IBrush Success = new SolidColorBrush(Color.Parse("#6FD39A"));

    private readonly IStartupService _startup = PlatformServices.CreateStartupService();

    private readonly Border _card;
    private readonly WrapPanel _installedPanel = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _search;
    private readonly StackPanel _suggestions = new() { Spacing = 2 };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _restart;
    private readonly List<FlyoutBase> _flyouts = [];
    private bool _preview;

    /// <summary>Closes any open menu of this window (they are separate little windows and would stay on screen).</summary>
    private void HideFlyouts()
    {
        foreach (var f in _flyouts)
            f.Hide();
    }

    /// <summary>The Settings window open right now (or null).</summary>
    public static SettingsWindow? Current => _current;

    /// <summary>Opens (or brings to front) the Settings window.</summary>
    public static void Open(string? searchFor = null)
    {
        if (_current is null)
        {
            _current = new SettingsWindow();
            _current.Closed += (_, _) => _current = null;
            _current.Show();
        }
        _current.Activate();
        if (searchFor is not null)
        {
            _current._search.Text = searchFor;
            _current._search.Focus();
        }
    }

    private SettingsWindow()
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "SnapVox";
        Cursor = new Cursor(StandardCursorType.Arrow); // normal pointer (the dimmed screen behind uses the cross)

        _search = new TextBox
        {
            Watermark = L.SearchLanguage,
            FontSize = 13,
            CornerRadius = new CornerRadius(12),
            Background = ChipBg,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 10, 0, 0),
        };
        _search.TextChanged += (_, _) => RefreshSuggestions();
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && FindMatches(_search.Text).FirstOrDefault() is { } first)
                LanguagePacks.Install(first);
        };

        // After installing/removing: Windows only shows new voices to the app after it restarts.
        _restart = new Button
        {
            Content = new TextBlock { Text = L.RestartApp, FontSize = 12, Foreground = Brushes.White },
            Background = AccentBg,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 5),
            Margin = new Thickness(0, 8, 0, 0),
            IsVisible = false,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        _restart.Click += (_, _) => AppRestart.Restart();

        LanguagePacks.Changed += OnLanguagePacksChanged;
        Closed += (_, _) => LanguagePacks.Changed -= OnLanguagePacksChanged;
        Closing += (_, _) => HideFlyouts();
        Deactivated += (_, _) => { /* menus close by themselves when clicking outside (light dismiss) */ };

        var content = new StackPanel { Spacing = 0 };
        content.Children.Add(Header());
        content.Children.Add(Separator());
        content.Children.Add(Row(L.Shortcut, Inline(Kbd("Win + Ctrl + R"), SoonChip(L.Change)), soon: false));
        content.Children.Add(Row(L.IslandPosition, IslandPositionPicker(), soon: false));
        content.Children.Add(Row(L.Voice, VoicePicker(), soon: false));
        content.Children.Add(Row(L.DefaultSpeed, SpeedPicker(), soon: false));
        content.Children.Add(Row(L.SaveMp3To, SaveFolderPicker(), soon: false));
        content.Children.Add(Separator());
        content.Children.Add(Row(L.StartWithWindows, StartupToggle(), soon: false));
        content.Children.Add(Row(L.OfflineVoice, SettingToggle(() => AppSettings.Current.WindowsVoiceFallback, v => AppSettings.Update(s => s.WindowsVoiceFallback = v)), soon: false));
        content.Children.Add(Row(L.OnlineVoices, SettingToggle(() => AppSettings.Current.AllowOnlineVoices == true, v => AppSettings.Update(s => s.AllowOnlineVoices = v)), soon: false));
        content.Children.Add(Row(L.AutoUpdate, AutoUpdateControls(), soon: false));
        content.Children.Add(_updateStatus);
        content.Children.Add(Separator());
        content.Children.Add(Row(L.InterfaceLanguage,
            new TextBlock { Text = $"{L.SystemLanguage} · {L.LanguageName(CultureInfo.InstalledUICulture.Name)}", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center },
            soon: false));
        content.Children.Add(ReadingLanguagesSection());
        content.Children.Add(new TextBlock
        {
            Text = $"SnapVox 0.1 · {L.OpenSource}",
            FontSize = 11,
            Foreground = Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 16),
        });

        _card = new Border
        {
            Width = 430,
            CornerRadius = new CornerRadius(28),
            Background = Black,
            BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderThickness = new Thickness(0.5),
            ClipToBounds = true,
            Margin = new Thickness(16),
            Opacity = 0,
            RenderTransform = TransformOperations.Parse("scale(0.88)"),
            Child = new Panel
            {
                Children =
                {
                    AmbientGlow(),
                    new ScrollViewer { Content = content, MaxHeight = 820, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                },
            },
            Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(500), Easing = new BackEaseOut() },
            ],
        };
        Content = _card;

        RefreshInstalled();
        OnLanguagePacksChanged(); // reopened during an installation: show the current percentage right away
        Opened += (_, _) =>
        {
            if (_preview)
                return;
            PlatformServices.ExcludeFromCapture(this);
            CenterOnMouseScreen();
            _card.Opacity = 1;
            _card.RenderTransform = TransformOperations.Parse("scale(1)");
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    /// <summary>Development only (--render-settings): draws the window into a PNG without showing it.</summary>
    internal static void RenderPreview(string pngPath, string? search)
    {
        // The window has to really exist for styles/templates to be applied:
        // open it far outside the visible area, draw it and close it.
        var w = new SettingsWindow { _preview = true, ShowActivated = false, Position = new PixelPoint(-30000, -30000) };
        w._card.Transitions = null;
        w._card.Opacity = 1;
        w._card.RenderTransform = null;
        w.Show();
        if (search is not null)
            w._search.Text = search;
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
        }
        var size = w._card.Bounds.Size;
        using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)size.Width + 32, (int)size.Height + 32));
        bmp.Render(w);
        bmp.Save(pngPath);
        w.Close();
    }

    // ---------- sections ----------

    private Control Header()
    {
        var close = IconButton("");
        close.Click += (_, _) => Close();

        var header = new DockPanel { Margin = new Thickness(18, 16, 14, 10), Background = Brushes.Transparent };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new Image { Source = LogoRenderer.Render(80), Width = 40, Height = 40 },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "SnapVox", FontSize = 15, FontWeight = FontWeight.Medium, Foreground = Brushes.White },
                        new TextBlock { Text = L.SettingsMenu, FontSize = 12, Foreground = Muted },
                    },
                },
            },
        });
        // Drag the window by its header.
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        return header;
    }

    private Control ReadingLanguagesSection()
    {
        var section = new StackPanel { Margin = new Thickness(18, 10, 18, 0) };
        section.Children.Add(new TextBlock { Text = L.ReadingLanguages, FontSize = 13, Foreground = Brushes.White });
        section.Children.Add(new TextBlock { Text = L.ReadingLanguagesHint, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) });
        section.Children.Add(_installedPanel);
        // No search box: choosing a voice (or the island's "Improve reading" button) installs the language.
        section.Children.Add(_status);
        section.Children.Add(_restart);
        return section;
    }

    /// <summary>
    /// Chips for the installed languages. Clicking opens a menu with "Install voice" (if missing) and "Remove".
    /// The system language shows a lock and can't be removed.
    /// </summary>
    private void RefreshInstalled()
    {
        _installedPanel.Children.Clear();
        var voices = LanguagePacks.InstalledVoices;
        string systemLang = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;

        foreach (var tag in LanguagePacks.InstalledOcr)
        {
            bool isSystem = tag.StartsWith(systemLang + "-", StringComparison.OrdinalIgnoreCase);
            bool hasVoice = voices.Any(v => v.Equals(tag, StringComparison.OrdinalIgnoreCase));

            var text = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            text.Children.Add(new TextBlock { Text = isSystem ? "" : "", FontFamily = IconFont, FontSize = 11, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            text.Children.Add(new TextBlock { Text = L.LanguageName(tag), FontSize = 12, Foreground = Brushes.White });
            if (!hasVoice)
                text.Children.Add(new TextBlock { Text = "· " + L.NoVoice, FontSize = 12, Foreground = Warning });

            var chip = new Button
            {
                Content = text,
                Background = ChipBg,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
            };

            var menu = new MenuFlyout { Placement = PlacementMode.Bottom };
            if (!hasVoice)
            {
                var installVoice = new MenuItem { Header = L.InstallVoice };
                installVoice.Click += (_, _) => LanguagePacks.InstallVoice(tag);
                menu.Items.Add(installVoice);
            }
            if (isSystem)
            {
                menu.Items.Add(new MenuItem { Header = L.SystemLanguageLocked, IsEnabled = false });
            }
            else
            {
                var remove = new MenuItem { Header = L.Remove };
                remove.Click += (_, _) => LanguagePacks.Remove(tag);
                menu.Items.Add(remove);
            }
            chip.Flyout = menu;
            _flyouts.Add(menu);

            _installedPanel.Children.Add(chip);
        }
    }

    private void RefreshSuggestions()
    {
        _suggestions.Children.Clear();
        if (string.IsNullOrWhiteSpace(_search.Text))
            return;

        var matches = FindMatches(_search.Text).Take(5).ToList();
        if (matches.Count == 0)
        {
            _suggestions.Children.Add(new TextBlock { Text = L.NoMatch, FontSize = 12, Foreground = Muted, Margin = new Thickness(12, 8) });
            return;
        }

        foreach (var tag in matches)
        {
            var row = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 7),
                Margin = new Thickness(0, 4, 0, 0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new DockPanel
                {
                    Children =
                    {
                        new TextBlock { Text = L.Install, FontSize = 12, Foreground = Muted, [DockPanel.DockProperty] = Dock.Right },
                        new TextBlock { Text = L.LanguageName(tag), FontSize = 13, Foreground = Brushes.White },
                    },
                },
            };
            row.PointerEntered += (_, _) => row.Background = AccentBg;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.Click += (_, _) => LanguagePacks.Install(tag);
            _suggestions.Children.Add(row);
        }
    }

    /// <summary>Search ignoring case and accents, in Portuguese, English, native name or code.</summary>
    private IEnumerable<string> FindMatches(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        string q = Normalize(query);
        var installed = LanguagePacks.InstalledOcr.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return LanguagePacks.InstallableLanguages
            .Where(tag => !installed.Contains(tag))
            .Where(tag =>
            {
                var c = new CultureInfo(tag);
                return new[] { L.LanguageName(tag), c.EnglishName, c.NativeName, tag }.Any(n => Normalize(n).Contains(q));
            })
            .OrderBy(tag => Normalize(L.LanguageName(tag)).StartsWith(q) ? 0 : 1)
            .ThenBy(tag => L.LanguageName(tag));
    }

    /// <summary>
    /// Shows the progress of <see cref="LanguagePacks"/>, e.g.:
    /// "Installing Portuguese (Brazil)… this can take a few minutes 50%".
    /// </summary>
    private void OnLanguagePacksChanged()
    {
        if (LanguagePacks.Current is { } op)
        {
            _suggestions.Children.Clear();
            ShowStatus(op.Text, Muted);
            _restart.IsVisible = false;
            return;
        }

        if (LanguagePacks.LastResult is { } result)
        {
            var color = result.Kind switch
            {
                LanguagePacks.StatusKind.Success => Success,
                LanguagePacks.StatusKind.Warning => Warning,
                _ => Muted,
            };
            bool ok = result.Kind == LanguagePacks.StatusKind.Success;
            ShowStatus(ok ? $"{result.Text}. {L.RestartForVoice}" : result.Text, color);
            _restart.IsVisible = ok;
            if (ok)
                _search.Text = "";
        }
        RefreshInstalled();
    }

    private void ShowStatus(string text, IBrush color)
    {
        _status.Text = text;
        _status.Foreground = color;
        _status.Margin = new Thickness(2, 8, 0, 0);
        _status.IsVisible = true;
    }

    /// <summary>
    /// Voice choice per language: a menu with the languages and, in each one, its voices.
    /// Piper (runs on the PC) has no label; Edge voices show "· Microsoft".
    /// </summary>
    private Control VoicePicker()
    {
        var label = new TextBlock { Text = L.VoiceAuto + "  ", FontSize = 12, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI, Segoe Fluent Icons, Segoe MDL2 Assets") };
        var button = new Button
        {
            Content = label,
            Background = ChipBg,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(10, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        _flyouts.Add(menu);
        void Rebuild()
        {
            // The button shows the chosen voice, e.g. "Portuguese: Francisca · Microsoft".
            string shownLang = Settings.AppSettings.Current.LastVoiceLanguage
                               ?? CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
            if (Speech.VoiceCatalog.ChosenFor(shownLang) is { } shown)
                label.Text = $"{L.LanguageName(shownLang)}: {shown.DisplayName}  ";

            menu.Items.Clear();
            foreach (var lang in Speech.VoiceCatalog.Languages)
            {
                var current = Speech.VoiceCatalog.ChosenFor(lang);
                var langItem = new MenuItem { Header = $"{L.LanguageName(lang)}  —  {current?.DisplayName}" };
                var defaultId = Speech.VoiceCatalog.DefaultFor(lang)?.Id;

                bool addedSeparator = false;
                // Edge (default) first; Piper after the separator.
                foreach (var voice in Speech.VoiceCatalog.ForLanguage(lang).OrderBy(v => v.Engine == Speech.VoiceEngine.Edge ? 0 : 1))
                {
                    if (voice.Engine == Speech.VoiceEngine.Piper && !addedSeparator)
                    {
                        langItem.Items.Add(new Separator());
                        addedSeparator = true;
                    }

                    var details = new List<string> { voice.Female ? L.Female : L.Male };
                    if (voice.Id == defaultId)
                        details.Add(L.Default);
                    if (voice.Engine == Speech.VoiceEngine.Edge)
                        details.Add(L.VoicesOnline);
                    else if (!Speech.VoiceStore.IsDownloaded(voice))
                        details.Add(L.DownloadSize(voice.DownloadMb));

                    bool selected = current?.Id == voice.Id;
                    var item = new MenuItem { Header = $"{(selected ? "✓  " : "     ")}{voice.DisplayName}   ({string.Join(" · ", details)})" };
                    item.Click += (_, _) =>
                    {
                        Settings.AppSettings.Update(s =>
                        {
                            s.Voices[lang] = voice.Id;
                            s.LastVoiceLanguage = lang;
                        });
                        // Picked a voice for a language: also install that reading language (Windows asks for permission).
                        LanguagePacks.EnsureInstalled(voice.Locale);
                    };
                    langItem.Items.Add(item);
                }
                menu.Items.Add(langItem);
            }
        }

        Rebuild();
        Settings.AppSettings.Changed += Rebuild;
        Closed += (_, _) => Settings.AppSettings.Changed -= Rebuild;
        button.Flyout = menu;
        return button;
    }

    // ---------- visual building blocks ----------

    private static Control Row(string label, Control control, bool soon)
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        if (soon)
        {
            left.Children.Add(new Border
            {
                Background = ChipBg,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = L.ComingSoon, FontSize = 10, Foreground = Muted },
            });
            control.IsEnabled = false;
            control.Opacity = 0.45;
        }

        var row = new DockPanel { Margin = new Thickness(18, 7) };
        control.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        row.Children.Add(left);
        return row;
    }

    private readonly TextBlock _updateStatus = new() { FontSize = 11, Margin = new Thickness(18, -2, 18, 4), IsVisible = false, TextWrapping = TextWrapping.Wrap };

    /// <summary>"Save MP3" folder: shows its name; "Change" opens the Windows folder picker.</summary>
    private Control SaveFolderPicker()
    {
        var name = new TextBlock { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis };
        void Refresh()
        {
            name.Text = L.FolderName(AppSettings.Current.SaveFolder);
            ToolTip.SetTip(name, string.IsNullOrEmpty(AppSettings.Current.SaveFolder) ? PlatformServices.CreateAudioExporter().DownloadsFolder : AppSettings.Current.SaveFolder);
        }
        Refresh();

        var change = new Button
        {
            Content = new TextBlock { Text = L.Change, FontSize = 12, Foreground = Brushes.White },
            Background = ChipBg,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(10, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        change.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = L.ChooseFolder,
                AllowMultiple = false,
            });
            if (folders.Count > 0 && Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(folders[0]) is { } path)
            {
                AppSettings.Update(s => s.SaveFolder = path);
                Refresh();
            }
        };
        return Inline(name, change);
    }

    /// <summary>A toggle bound to a setting.</summary>
    private static Control SettingToggle(Func<bool> get, Action<bool> set)
    {
        var toggle = Toggle(get(), enabled: true);
        toggle.IsCheckedChanged += (_, _) => set(toggle.IsChecked == true);
        return toggle;
    }

    /// <summary>Automatic updates (on/off) + "Check now".</summary>
    private Control AutoUpdateControls()
    {
        var checkNow = new Button
        {
            Content = new TextBlock { Text = L.CheckUpdatesNow, FontSize = 12, Foreground = Brushes.White },
            Background = ChipBg,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(10, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
        checkNow.Click += async (_, _) =>
        {
            ShowUpdateStatus(L.CheckingUpdates, Muted);
            var (update, failed) = await Updater.CheckAsync();
            if (update is not null)
            {
                ShowUpdateStatus(L.UpdateAvailable(update.Version.ToString()), Success);
                UpdateWindow.ShowFor(update);
            }
            else
            {
                ShowUpdateStatus(failed ? L.UpdateCheckFailed : $"{L.UpToDate} ({Updater.CurrentVersion})", failed ? Warning : Muted);
            }
        };
        return Inline(checkNow, SettingToggle(() => AppSettings.Current.AutoUpdate, v => AppSettings.Update(s => s.AutoUpdate = v)));
    }

    private void ShowUpdateStatus(string text, IBrush color)
    {
        _updateStatus.Text = text;
        _updateStatus.Foreground = color;
        _updateStatus.IsVisible = true;
    }

    private Control StartupToggle()
    {
        var toggle = Toggle(_startup.IsEnabled, enabled: true);
        toggle.IsCheckedChanged += (_, _) =>
        {
            try
            {
                _startup.SetEnabled(toggle.IsChecked == true);
            }
            catch (Exception ex)
            {
                Log.Info("Couldn't change 'start with Windows': " + ex.Message);
            }
        };
        return toggle;
    }

    private static ToggleSwitch Toggle(bool on, bool enabled) => new()
    {
        IsChecked = on,
        IsEnabled = enabled,
        OnContent = null,
        OffContent = null,
        MinWidth = 0,
        Margin = new Thickness(0, -6, -10, -6),
    };

    /// <summary>Island position: Top / Left / Right (saved right away; an open island moves immediately).</summary>
    private Control IslandPositionPicker()
    {
        var options = new[] { ("top", L.Top), ("left", L.Left), ("right", L.Right) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        var border = new Border { Background = ChipBg, CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Child = bar };
        var selectedBg = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));

        void Refresh()
        {
            for (int i = 0; i < options.Length; i++)
                ((Button)bar.Children[i]).Background = AppSettings.Current.IslandPosition == options[i].Item1 ? selectedBg : Brushes.Transparent;
        }

        foreach (var (key, label) in options)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = label, FontSize = 12, Foreground = Brushes.White },
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 4),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            b.Click += (_, _) => AppSettings.Update(s => s.IslandPosition = key);
            bar.Children.Add(b);
        }
        Refresh();
        AppSettings.Changed += Refresh;
        Closed += (_, _) => AppSettings.Changed -= Refresh;
        return border;
    }

    /// <summary>Default speed (the same one the island player changes).</summary>
    private Control SpeedPicker()
    {
        var label = new TextBlock { FontSize = 12, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI, Segoe Fluent Icons, Segoe MDL2 Assets") };
        var button = new Button
        {
            Content = label,
            Background = ChipBg,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(10, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        _flyouts.Add(menu);
        void Refresh()
        {
            double current = AppSettings.Current.Speed;
            label.Text = $"{current:0.##}x  ";
            menu.Items.Clear();
            foreach (double s in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 })
            {
                var item = new MenuItem { Header = (Math.Abs(s - current) < 0.01 ? "✓  " : "     ") + $"{s:0.##}x" };
                item.Click += (_, _) => AppSettings.Update(x => x.Speed = s);
                menu.Items.Add(item);
            }
        }
        Refresh();
        AppSettings.Changed += Refresh;
        Closed += (_, _) => AppSettings.Changed -= Refresh;
        button.Flyout = menu;
        return button;
    }

    private static Control Inline(params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var c in children)
            p.Children.Add(c);
        return p;
    }

    private static Control Kbd(string text) => new Border
    {
        Background = ChipBg,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(7, 3),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = Brushes.White },
    };

    private static Control Chip(string text) => new Border
    {
        Background = ChipBg,
        CornerRadius = new CornerRadius(13),
        Padding = new Thickness(10, 4),
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI, Segoe Fluent Icons, Segoe MDL2 Assets") },
    };

    /// <summary>The shortcut's "Change": not implemented yet, but the shortcut row itself is real.</summary>
    private static Control SoonChip(string text)
    {
        var chip = Chip(text);
        chip.Opacity = 0.45;
        ToolTip.SetTip(chip, L.ComingSoon);
        return chip;
    }

    private static Control Separator() => new Border { Height = 0.5, Background = Hairline, Margin = new Thickness(18, 6) };

    private static Control AmbientGlow() => new Border
    {
        IsHitTestVisible = false,
        Background = new RadialGradientBrush
        {
            Center = new RelativePoint(0.12, 0.05, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.12, 0.05, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.25, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(110, 47, 127, 216), 0),
                new GradientStop(Color.FromArgb(0, 47, 127, 216), 1),
            },
        },
    };

    private static Button IconButton(string glyph) => new()
    {
        Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255)) },
        Width = 30,
        Height = 30,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(15),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Cursor = new Cursor(StandardCursorType.Hand),
    };

    private void CenterOnMouseScreen()
    {
        var cursor = PlatformServices.GetCursorPosition();
        var screen = Screens.ScreenFromPoint(cursor) ?? Screens.Primary;
        if (screen is null)
            return;
        var wa = screen.WorkingArea;
        int w = (int)(Bounds.Width * RenderScaling), h = (int)(Bounds.Height * RenderScaling);
        Position = new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Y + Math.Max(0, (wa.Height - h) / 2));
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString().Trim();
    }
}

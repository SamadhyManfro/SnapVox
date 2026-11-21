using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Threading;
using SnapVox.Localization;
using SnapVox.Platform;
using SnapVox.Settings;
using SnapVox.Speech;

namespace SnapVox.UI;

/// <summary>
/// SnapVox's "Dynamic Island": always the full player — app icon,
/// title, status line, equalizer, progress bar, ⏮ word / ⏯ / ⏭ word, speed, voice and ⋮.
/// States: waiting for a selection → loading (recognizing / generating the voice, animated) → playing.
/// Horizontal at the top; vertical on the sides (left/right). Always centered.
/// </summary>
internal sealed class IslandWindow : Window
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly FontFamily UiFont = new("Segoe UI, Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly Color AccentColor = Color.Parse("#4DA3FF");
    private static readonly IBrush Accent = new SolidColorBrush(AccentColor);
    private static readonly IBrush Black = new SolidColorBrush(Color.Parse("#0B0B0D"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
    private static readonly IBrush ChipBg = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#FFB454"));

    private enum State { Idle, Loading, Ready, Message }

    private readonly Screen _screen;
    private readonly Border _pill;
    private readonly Border _glow;
    private readonly Border _content;
    private readonly DispatcherTimer _anim;
    private readonly Random _random = new();

    private State _state = State.Idle;
    private string _title, _subtitle;
    private string _language = "en";
    private string? _improveLabel;
    private Action? _improveAction;
    private bool _playing, _finished;
    private double _speed = 1.0;
    private TimeSpan _position, _duration;
    private double _phase;
    private bool _preview;

    // Parts that change without rebuilding everything.
    private MarqueeText? _titleBlock, _subtitleBlock;
    private TextBlock? _playIcon, _timeA, _timeT, _speedLabel;
    private Control? _verticalLogo, _playIconHost;
    private Spinner? _spinner;
    private ProgressTrack? _progress;
    private Button? _improveButton, _playButton;
    private readonly List<Control> _needsText = [];
    private readonly List<Border> _eqBars = [];

    public event Action? CloseRequested;
    public event Action? PlayPauseClicked;
    public event Action? PreviousClicked;
    public event Action? NextClicked;
    public event Action<double>? SeekRequested;
    public event Action<double>? SpeedChosen;
    public event Action<VoiceInfo>? VoiceChosen;
    public event Action? NewSelectionRequested;
    public event Action<bool>? SaveRequested; // true = "Save as…"
    public event Action? AddLanguageRequested;

    /// <summary>There is audio to save (enables "Save MP3" in the ⋮ menu).</summary>
    public bool CanSave { get; set; }

    private bool Vertical => AppSettings.Current.IslandPosition is "left" or "right";

    public IslandWindow(Screen screen)
    {
        _screen = screen;
        _title = L.SelectText;
        _subtitle = L.DragHint;

        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(screen.WorkingArea.X, screen.WorkingArea.Y);
        Cursor = new Cursor(StandardCursorType.Arrow);

        _glow = new Border { IsHitTestVisible = false, Opacity = 0.55 };
        _content = new Border();
        _pill = new Border
        {
            Background = Black,
            BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderThickness = new Thickness(0.5),
            CornerRadius = new CornerRadius(34),
            ClipToBounds = true,
            Margin = new Thickness(8),
            Opacity = 0,
            RenderTransform = TransformOperations.Parse("scale(0.72)"),
            Child = new Panel { Children = { _glow, _content } },
            Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(520), Easing = new SoftSpring() },
            ],
        };
        Content = _pill;

        _anim = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) => Animate());

        Rebuild();
        Opened += (_, _) =>
        {
            PlatformServices.MakeToolWindow(this, noActivate: true);
            PlatformServices.ExcludeFromCapture(this);
            Place();
            _pill.Opacity = 1;
            _pill.RenderTransform = TransformOperations.Parse("scale(1)");
        };
        SizeChanged += (_, _) => Place();
        AppSettings.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            AppSettings.Changed -= OnSettingsChanged;
            _anim.Stop();
        };
    }

    // ------------------------------------------------------------------ states (used by the session)

    /// <summary>Waiting for the user to drag over the text.</summary>
    public void ShowIdle()
    {
        _state = State.Idle;
        _title = L.SelectText;
        _subtitle = L.DragHint;
        _playing = _finished = false;
        _position = _duration = TimeSpan.Zero;
        CanSave = false;
        ApplyState();
    }

    /// <summary>Loading: recognizing the text, generating the voice or downloading a voice (animated).</summary>
    public void ShowLoading(string message, string? title = null)
    {
        _state = State.Loading;
        if (title is not null)
            _title = title;
        else if (_title == L.SelectText)
            _title = L.Reading;
        _subtitle = message;
        _playing = _finished = false;
        ApplyState();
    }

    /// <summary>Ready to play: title (start of the text), "voice · language" and the language (voice menu).</summary>
    public void ShowPlayer(string title, string subtitle, string language)
    {
        _state = State.Ready;
        _title = title;
        _subtitle = subtitle;
        _language = language;
        _finished = false;
        ApplyState();
    }

    /// <summary>A message without a reading (e.g. "No text found").</summary>
    public void ShowMessage(string message)
    {
        _state = State.Message;
        _title = message;
        _subtitle = L.DragHint;
        _playing = _finished = false;
        ApplyState();
    }

    /// <summary>Changes only the status line (e.g. "MP3 saved to Downloads").</summary>
    public void SetSubtitle(string subtitle)
    {
        _subtitle = subtitle;
        if (_subtitleBlock is not null)
            _subtitleBlock.Text = subtitle;
        UpdateVerticalTip();
    }

    public void SetPlaying(bool playing, bool finished = false)
    {
        _playing = playing;
        _finished = finished;
        if (_playIcon is not null)
            _playIcon.Text = finished ? "" : playing ? "" : "";
        UpdateAnimationTimer();
    }

    public void SetProgress(TimeSpan position, TimeSpan duration)
    {
        _position = position;
        _duration = duration;
        if (_progress is not null)
            _progress.Value = duration.TotalSeconds > 0 ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1) : 0;
        if (_timeA is not null)
            _timeA.Text = Format(position);
        if (_timeT is not null)
            _timeT.Text = Format(duration);
    }

    public void SetSpeed(double speed)
    {
        _speed = speed;
        if (_speedLabel is not null)
            _speedLabel.Text = SpeedText(speed) + (Vertical ? "" : "  ");
    }

    /// <summary>Amber help button (e.g. "Improve reading"); null hides it.</summary>
    public void SetImproveAction(string? label, Action? action)
    {
        _improveLabel = label;
        _improveAction = action;
        if (_improveButton is not null)
        {
            _improveButton.IsVisible = action is not null;
            if (_improveButton.Content is TextBlock t)
                t.Text = label;
            ToolTip.SetTip(_improveButton, label);
        }
    }

    // ------------------------------------------------------------------ montagem

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        // Position changed (top/sides): rebuild the layout and reposition.
        Rebuild();
        Place();
    });

    /// <summary>Updates texts, enabled buttons and animations for the current state.</summary>
    private void ApplyState()
    {
        if (_titleBlock is not null)
            _titleBlock.Text = _title;
        if (_subtitleBlock is not null)
            _subtitleBlock.Text = _subtitle;
        UpdateVerticalTip();

        bool ready = _state == State.Ready;
        foreach (var c in _needsText)
        {
            c.IsEnabled = ready;
            c.Opacity = ready ? 1 : 0.35;
        }

        bool loading = _state == State.Loading;
        if (_playButton is not null)
        {
            // ⏯ only works with text; while loading it stays lit because of the spinner.
            _playButton.IsEnabled = ready;
            _playButton.Opacity = ready || loading ? 1 : 0.35;
        }
        if (_progress is not null)
            _progress.Indeterminate = loading;
        if (_spinner is not null)
            _spinner.IsVisible = loading;
        if (_playIconHost is not null)
            _playIconHost.IsVisible = !loading;

        SetPlaying(_playing, _finished);
        SetProgress(_position, _duration);
        UpdateAnimationTimer();
    }

    private void Rebuild()
    {
        _titleBlock = _subtitleBlock = null;
        _playIcon = _timeA = _timeT = _speedLabel = null;
        _verticalLogo = _playIconHost = null;
        _spinner = null;
        _progress = null;
        _improveButton = _playButton = null;
        _needsText.Clear();
        _eqBars.Clear();

        // Grows out of the screen edge it is attached to.
        _pill.RenderTransformOrigin = AppSettings.Current.IslandPosition switch
        {
            "left" => new RelativePoint(0, 0.5, RelativeUnit.Relative),
            "right" => new RelativePoint(1, 0.5, RelativeUnit.Relative),
            _ => new RelativePoint(0.5, 0, RelativeUnit.Relative),
        };
        _glow.Background = GlowBrush(Vertical);
        _content.Child = Vertical ? BuildVertical() : BuildHorizontal();
        SetImproveAction(_improveLabel, _improveAction);
        SetSpeed(_speed);
        ApplyState();
    }

    private Control BuildHorizontal()
    {
        var root = new StackPanel { Width = 410, Margin = new Thickness(16, 14, 16, 12) };

        // Row 1: icon, title, status, (help), equalizer, new selection, ⋮
        var top = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _improveButton = AmberButton();
        right.Children.Add(_improveButton);
        right.Children.Add(Equalizer());
        right.Children.Add(IconButton("", L.NewSelection, () => NewSelectionRequested?.Invoke()));
        right.Children.Add(MoreButton());
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        var logo = Logo(46);
        DockPanel.SetDock(logo, Dock.Left);
        top.Children.Add(logo);
        _titleBlock = new MarqueeText { FontSize = 14, FontWeight = FontWeight.Medium, Foreground = Brushes.White };
        _subtitleBlock = new MarqueeText { FontSize = 12, Foreground = Muted };
        top.Children.Add(new StackPanel
        {
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _titleBlock, _subtitleBlock },
        });
        root.Children.Add(top);

        // Row 2: time · bar · duration
        _timeA = new TextBlock { FontSize = 11, Foreground = Muted, Width = 34, VerticalAlignment = VerticalAlignment.Center };
        _timeT = new TextBlock { FontSize = 11, Foreground = Muted, Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _progress = new ProgressTrack { Height = 14, Margin = new Thickness(6, 0) };
        _progress.SeekRequested += f => SeekRequested?.Invoke(f);
        var bar = new DockPanel { Margin = new Thickness(0, 12, 0, 4) };
        DockPanel.SetDock(_timeA, Dock.Left);
        DockPanel.SetDock(_timeT, Dock.Right);
        bar.Children.Add(_timeA);
        bar.Children.Add(_timeT);
        bar.Children.Add(_progress);
        root.Children.Add(bar);

        // Row 3: speed · ⏮ ⏯ ⏭ · voice
        var controls = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        var speed = SpeedChip(62);
        var voice = VoiceChip(62);
        speed.HorizontalAlignment = HorizontalAlignment.Left;
        voice.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(speed, 0);
        Grid.SetColumn(voice, 2);
        var transport = Transport(Orientation.Horizontal);
        Grid.SetColumn(transport, 1);
        controls.Children.Add(speed);
        controls.Children.Add(transport);
        controls.Children.Add(voice);
        root.Children.Add(controls);
        return root;
    }

    private Control BuildVertical()
    {
        var col = new StackPanel { Width = 88, Margin = new Thickness(0, 16, 0, 14) };
        var logo = Logo(46);
        _verticalLogo = logo;
        col.Children.Add(Center(logo));
        _improveButton = AmberButton(compact: true);
        col.Children.Add(Center(_improveButton, new Thickness(0, 8, 0, 0)));
        col.Children.Add(Center(Equalizer(), new Thickness(0, 12, 0, 10)));
        _progress = new ProgressTrack { Width = 60, Height = 14 };
        _progress.SeekRequested += f => SeekRequested?.Invoke(f);
        col.Children.Add(Center(_progress));
        _timeA = new TextBlock { FontSize = 11, Foreground = Muted };
        col.Children.Add(Center(_timeA, new Thickness(0, 2, 0, 10)));
        col.Children.Add(Center(Transport(Orientation.Vertical)));
        col.Children.Add(Center(SpeedChip(58), new Thickness(0, 12, 0, 6)));
        col.Children.Add(Center(VoiceChip(58)));
        col.Children.Add(Center(IconButton("", L.NewSelection, () => NewSelectionRequested?.Invoke()), new Thickness(0, 14, 0, 0)));
        col.Children.Add(Center(MoreButton()));
        return col;
    }

    private void UpdateVerticalTip()
    {
        if (_verticalLogo is not null)
            ToolTip.SetTip(_verticalLogo, $"{_title}\n{_subtitle}");
    }

    private Control Transport(Orientation orientation)
    {
        var panel = new StackPanel { Orientation = orientation, Spacing = orientation == Orientation.Horizontal ? 10 : 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var prev = IconButton("", L.PreviousWord, () => PreviousClicked?.Invoke());
        var next = IconButton("", L.NextWord, () => NextClicked?.Invoke());
        _needsText.Add(prev);
        _needsText.Add(next);

        // Big button: ⏯, or the spinner while it recognizes the text / generates the voice.
        _playIcon = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 20, Foreground = new SolidColorBrush(Color.Parse("#6CB4FF")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _playIconHost = _playIcon;
        _spinner = new Spinner { Width = 22, Height = 22, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var play = new Button
        {
            Content = new Panel { Children = { _playIcon, _spinner } },
            Width = 44,
            Height = 44,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(51, 77, 163, 255)),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(play, L.PlayPause);
        play.Click += (_, _) => PlayPauseClicked?.Invoke();
        _playButton = play;

        panel.Children.Add(prev);
        panel.Children.Add(play);
        panel.Children.Add(next);
        return panel;
    }

    private Control Equalizer()
    {
        var eq = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = 16, Width = 20, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 4; i++)
        {
            var bar = new Border
            {
                Width = 3,
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = Accent,
                VerticalAlignment = VerticalAlignment.Bottom,
                Transitions = [new DoubleTransition { Property = HeightProperty, Duration = TimeSpan.FromMilliseconds(140) }],
            };
            _eqBars.Add(bar);
            eq.Children.Add(bar);
        }
        return eq;
    }

    private Button AmberButton(bool compact = false)
    {
        var b = new Button
        {
            IsVisible = false,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = compact ? new Thickness(6, 2) : new Thickness(8, 2),
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromArgb(38, 255, 180, 84)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = compact
                ? new TextBlock { Text = "", FontFamily = IconFont, FontSize = 13, Foreground = Amber }
                : new TextBlock { FontSize = 11, Foreground = Amber, MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        b.Click += (_, _) => _improveAction?.Invoke();
        return b;
    }

    private Button SpeedChip(double width)
    {
        _speedLabel = new TextBlock { FontSize = 12, Foreground = Brushes.White, FontFamily = UiFont, HorizontalAlignment = HorizontalAlignment.Center };
        var chip = Chip(_speedLabel, width);
        ToolTip.SetTip(chip, $"{L.Speed} (↑ ↓)");
        var menu = new MenuFlyout();
        void Fill()
        {
            menu.Items.Clear();
            foreach (double s in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 })
            {
                var item = new MenuItem { Header = (Math.Abs(s - _speed) < 0.01 ? "✓  " : "     ") + SpeedText(s) };
                item.Click += (_, _) => SpeedChosen?.Invoke(s);
                menu.Items.Add(item);
            }
        }
        Fill();
        menu.Opening += (_, _) => Fill();
        chip.Flyout = menu;
        return chip;
    }

    private Button VoiceChip(double width)
    {
        var label = new TextBlock { Text = Vertical ? "" : "  ", FontSize = 12, Foreground = Brushes.White, FontFamily = UiFont, HorizontalAlignment = HorizontalAlignment.Center };
        var chip = Chip(label, width);
        ToolTip.SetTip(chip, L.Voice);
        var menu = new MenuFlyout();
        menu.Opening += (_, _) => FillVoiceMenu(menu);
        FillVoiceMenu(menu);
        chip.Flyout = menu;
        _needsText.Add(chip);
        return chip;
    }

    /// <summary>Voices of the language being read (✓ on the chosen one) + "Read in another language" to fix the detection.</summary>
    private void FillVoiceMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        var chosen = VoiceCatalog.ChosenFor(_language);
        menu.Items.Add(new MenuItem { Header = L.LanguageName(_language), IsEnabled = false });
        foreach (var v in VoiceCatalog.ForLanguage(_language).OrderBy(v => v.Engine == VoiceEngine.Edge ? 0 : 1))
            menu.Items.Add(VoiceItem(v, chosen?.Id == v.Id));
        if (OfflineVoiceItem(_language) is { } installOffline)
            menu.Items.Add(installOffline);

        var others = new MenuItem { Header = L.OtherLanguages };
        foreach (var lang in VoiceCatalog.ActiveLanguages.Where(l => l != _language))
        {
            var langItem = new MenuItem { Header = L.LanguageName(lang) };
            foreach (var v in VoiceCatalog.ForLanguage(lang).OrderBy(v => v.Engine == VoiceEngine.Edge ? 0 : 1))
                langItem.Items.Add(VoiceItem(v, false));
            if (OfflineVoiceItem(lang) is { } installOther)
                langItem.Items.Add(installOther);
            others.Items.Add(langItem);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(others);

        var add = new MenuItem { Header = "+  " + L.AddLanguage };
        add.Click += (_, _) => AddLanguageRequested?.Invoke();
        menu.Items.Add(add);
    }

    /// <summary>For languages with no Piper voice: installs the Windows voice. Settings shows the progress and the restart.</summary>
    private static MenuItem? OfflineVoiceItem(string language)
    {
        if (!VoiceCatalog.CanInstallWindowsVoice(language) || VoiceCatalog.DefaultFor(language) is not { } voice)
            return null;
        var item = new MenuItem { Header = "+  " + L.InstallOfflineVoice };
        item.Click += (_, _) =>
        {
            LanguagePacks.InstallVoice(voice.Locale);
            SettingsWindow.Open();
        };
        return item;
    }

    private MenuItem VoiceItem(VoiceInfo v, bool selected)
    {
        var item = new MenuItem { Header = (selected ? "✓  " : "     ") + v.DisplayName };
        item.Click += (_, _) => VoiceChosen?.Invoke(v);
        return item;
    }

    /// <summary>⋮ menu: save MP3, save as, island position, settings, suggestion, close.</summary>
    private Button MoreButton()
    {
        var button = IconButton("", "", null);
        var menu = new MenuFlyout();
        void Fill()
        {
            menu.Items.Clear();
            var save = new MenuItem { Header = $"{L.SaveMp3}   ({L.FolderName(AppSettings.Current.SaveFolder)})", IsEnabled = CanSave };
            save.Click += (_, _) => SaveRequested?.Invoke(false);
            var saveAs = new MenuItem { Header = L.SaveAs, IsEnabled = CanSave };
            saveAs.Click += (_, _) => SaveRequested?.Invoke(true);
            menu.Items.Add(save);
            menu.Items.Add(saveAs);
            menu.Items.Add(new Separator());

            var position = new MenuItem { Header = L.IslandPosition };
            foreach (var (key, label) in new[] { ("top", L.Top), ("left", L.Left), ("right", L.Right) })
            {
                var item = new MenuItem { Header = (AppSettings.Current.IslandPosition == key ? "✓  " : "     ") + label };
                item.Click += (_, _) => AppSettings.Update(s => s.IslandPosition = key);
                position.Items.Add(item);
            }
            menu.Items.Add(position);

            var settings = new MenuItem { Header = L.SettingsMenu };
            settings.Click += (_, _) => SettingsWindow.Open();
            menu.Items.Add(settings);
            var suggestion = new MenuItem { Header = L.SendSuggestion };
            suggestion.Click += (_, _) => Updater.OpenSuggestion();
            menu.Items.Add(suggestion);
            menu.Items.Add(new Separator());
            var close = new MenuItem { Header = $"{L.Close}   (Esc)" };
            close.Click += (_, _) => CloseRequested?.Invoke();
            menu.Items.Add(close);
        }
        Fill();
        menu.Opening += (_, _) => Fill();
        button.Flyout = menu;
        return button;
    }

    // ---- animations: equalizer and "breathing" light while speaking; running bar and spinner while loading

    private void UpdateAnimationTimer()
    {
        bool run = (_state == State.Ready && _playing && !_finished) || _state == State.Loading;
        if (run && !_anim.IsEnabled)
            _anim.Start();
        else if (!run && _anim.IsEnabled)
        {
            _anim.Stop();
            foreach (var b in _eqBars)
                b.Height = 3;
            _glow.Opacity = 0.55;
        }
    }

    private int _frame;

    private void Animate()
    {
        _frame++;
        _phase += 0.04 * 2 * Math.PI / 2.6; // ~2.6 s cycle
        _glow.Opacity = 0.6 + 0.4 * (0.5 + 0.5 * Math.Sin(_phase));

        if (_state == State.Loading)
        {
            _spinner?.Advance();
            _progress?.InvalidateVisual();
            return;
        }
        if (_frame % 3 == 0) // equalizer every ~120 ms
            foreach (var b in _eqBars)
                b.Height = 3 + _random.NextDouble() * 13;
    }

    // ---- building blocks

    private static IBrush GlowBrush(bool vertical) => new RadialGradientBrush
    {
        // Blue light inside (no glow around it), like the reference player.
        Center = vertical ? new RelativePoint(0.5, 0.05, RelativeUnit.Relative) : new RelativePoint(0.12, 0.22, RelativeUnit.Relative),
        GradientOrigin = vertical ? new RelativePoint(0.5, 0.05, RelativeUnit.Relative) : new RelativePoint(0.12, 0.22, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(vertical ? 0.9 : 0.45, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(vertical ? 0.3 : 0.8, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(140, 47, 127, 216), 0),
            new GradientStop(Color.FromArgb(0, 47, 127, 216), 1),
        },
    };

    private static Control Logo(double size) =>
        new Image { Source = LogoRenderer.Render((int)(size * 2)), Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };

    private static Control Center(Control c, Thickness? margin = null)
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        if (margin is { } m)
            c.Margin = m;
        return c;
    }

    private static Button Chip(Control content, double width) => new()
    {
        Content = content,
        Width = width,
        Height = 26,
        Padding = new Thickness(6, 0),
        CornerRadius = new CornerRadius(13),
        Background = ChipBg,
        BorderThickness = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Cursor = new Cursor(StandardCursorType.Hand),
    };

    private static Button IconButton(string glyph, string tip, Action? click)
    {
        var b = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = IconFont,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(15),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        if (!string.IsNullOrEmpty(tip))
            ToolTip.SetTip(b, tip);
        if (click is not null)
            b.Click += (_, _) => click();
        return b;
    }

    private static string SpeedText(double s) => $"{s:0.##}x";

    private static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    /// <summary>Centers it at the top, or on the chosen side, of the selection's monitor.</summary>
    private void Place()
    {
        if (_preview)
            return;
        var wa = _screen.WorkingArea;
        double s = RenderScaling;
        int w = (int)(Bounds.Width * s), h = (int)(Bounds.Height * s), gap = (int)(4 * s);
        Position = AppSettings.Current.IslandPosition switch
        {
            "left" => new PixelPoint(wa.X + gap, wa.Y + (wa.Height - h) / 2),
            "right" => new PixelPoint(wa.Right - w - gap, wa.Y + (wa.Height - h) / 2),
            _ => new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Y + gap),
        };
    }

    /// <summary>Development only (--render-island): draws the island's main states into PNGs.</summary>
    internal static void RenderPreview(Screen screen, string dir)
    {
        Directory.CreateDirectory(dir);
        string saved = AppSettings.Current.IslandPosition;
        void Shot(string name, Action<IslandWindow> setup, string position)
        {
            AppSettings.Current.IslandPosition = position;
            var w = new IslandWindow(screen) { _preview = true, ShowActivated = false, Position = new PixelPoint(-30000, -30000) };
            w._pill.Transitions = null;
            w._pill.Opacity = 1;
            w._pill.RenderTransform = null;
            w.Show();
            w.Rebuild();
            setup(w);
            for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
            var size = w.Bounds.Size;
            using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
            bmp.Render(w);
            bmp.Save(Path.Combine(dir, name));
            w.Close();
        }

        void Player(IslandWindow w)
        {
            w.ShowPlayer("The new system update brings performance improvements…", "Aria · Online · English", "en");
            w.SetPlaying(true);
            w.SetProgress(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(11));
            w.SetSpeed(1.25);
            foreach (var (b, i) in w._eqBars.Select((b, i) => (b, i))) b.Height = 4 + i * 3;
        }
        void Loading(IslandWindow w)
        {
            w.ShowLoading(L.GeneratingVoice("Aria · Online"), "The new system update brings improvements…");
            w._spinner?.Advance(); w._spinner?.Advance();
        }

        Shot("1_idle.png", _ => { }, "top");
        Shot("2_loading.png", Loading, "top");
        Shot("3_player_top.png", Player, "top");
        Shot("4_improve.png", w => { Loading(w); w.SetImproveAction(L.ImproveReading, () => { }); }, "top");
        Shot("5_idle_left.png", _ => { }, "left");
        Shot("6_loading_left.png", Loading, "left");
        AppSettings.Current.IslandPosition = saved;
    }

    /// <summary>A soft spring: it settles with a tiny overshoot that always fits inside the window.</summary>
    private sealed class SoftSpring : Easing
    {
        public override double Ease(double progress) =>
            progress >= 1 ? 1 : 1 - Math.Exp(-10 * progress) * Math.Cos(8.5 * progress);
    }

    /// <summary>Clickable progress bar; while loading it shows a light running from side to side.</summary>
    private sealed class ProgressTrack : Control
    {
        private double _value;
        public event Action<double>? SeekRequested;
        public bool Indeterminate { get; set; }

        public ProgressTrack()
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        public double Value
        {
            get => _value;
            set { _value = value; InvalidateVisual(); }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!Indeterminate && Bounds.Width > 0)
                SeekRequested?.Invoke(Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1));
        }

        public override void Render(DrawingContext ctx)
        {
            double y = Bounds.Height / 2, w = Bounds.Width;
            var track = new Rect(0, y - 2, w, 4);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), null, new RoundedRect(track, 2));

            if (Indeterminate)
            {
                // A blue segment going back and forth (real time, independent of the caller).
                double t = Environment.TickCount64 % 1600 / 1600.0;
                double pos = 0.5 - 0.5 * Math.Cos(t * 2 * Math.PI);
                double seg = Math.Max(24, w * 0.28);
                double x = pos * (w - seg);
                using (ctx.PushClip(new RoundedRect(track, 2)))
                    ctx.DrawRectangle(Accent, null, new RoundedRect(new Rect(x, y - 2, seg, 4), 2));
                return;
            }

            ctx.DrawRectangle(Accent, null, new RoundedRect(new Rect(0, y - 2, w * _value, 4), 2));
            ctx.DrawEllipse(Brushes.White, null, new Point(Math.Clamp(w * _value, 5, Math.Max(5, w - 5)), y), 5, 5);
        }
    }

    /// <summary>Loading spinner (a spinning blue arc) inside the ⏯ button.</summary>
    private sealed class Spinner : Control
    {
        private double _angle;

        public void Advance()
        {
            _angle = (_angle + 24) % 360;
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            double r = Math.Min(Bounds.Width, Bounds.Height) / 2 - 2;
            var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
            var pen = new Pen(new SolidColorBrush(Color.Parse("#6CB4FF")), 2.5, lineCap: PenLineCap.Round);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                double a0 = _angle * Math.PI / 180, a1 = (_angle + 270) * Math.PI / 180;
                g.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false);
                g.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, true, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
        }
    }
}

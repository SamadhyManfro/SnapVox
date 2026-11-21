using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using SnapVox.Localization;
using SnapVox.Platform;
using SnapVox.Settings;

namespace SnapVox.UI;

/// <summary>Asks "Version X is available — update?" in the island style, and shows the download percentage.</summary>
internal sealed class UpdateWindow : Window
{
    private static UpdateWindow? _current;

    private static readonly IBrush Muted = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2F7FD8"));
    private static readonly IBrush ChipBg = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));

    private readonly Updater.UpdateInfo _update;
    private readonly TextBlock _status;
    private readonly StackPanel _buttons;
    private readonly Border _card;

    public static void ShowFor(Updater.UpdateInfo update)
    {
        if (_current is not null)
        {
            _current.Activate();
            return;
        }
        _current = new UpdateWindow(update);
        _current.Closed += (_, _) => _current = null;
        _current.Show();
    }

    private UpdateWindow(Updater.UpdateInfo update)
    {
        _update = update;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "SnapVox";
        Cursor = new Cursor(StandardCursorType.Arrow);

        _status = new TextBlock { Text = L.UpdateQuestion, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        var later = Pill(L.NotNow, ChipBg);
        later.Click += (_, _) =>
        {
            AppSettings.Update(s => s.SkippedUpdate = _update.Tag); // don't ask again about this version
            Close();
        };
        var now = Pill(L.UpdateNow, Accent);
        now.Click += (_, _) => _ = UpdateAsync();
        _buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { later, now } };

        _card = new Border
        {
            Width = 360,
            Background = new SolidColorBrush(Color.Parse("#0B0B0D")),
            BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderThickness = new Thickness(0.5),
            CornerRadius = new CornerRadius(26),
            Padding = new Thickness(18, 16),
            Margin = new Thickness(12),
            Opacity = 0,
            RenderTransform = TransformOperations.Parse("scale(0.85)"),
            Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(500), Easing = new BackEaseOut() },
            ],
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new StackPanel
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
                                    new TextBlock { Text = L.UpdateAvailable(update.Version.ToString()), FontSize = 14, FontWeight = FontWeight.Medium, Foreground = Brushes.White },
                                    new TextBlock { Text = $"SnapVox {Updater.CurrentVersion} → {update.Version}", FontSize = 11, Foreground = Muted },
                                },
                            },
                        },
                    },
                    _status,
                    _buttons,
                },
            },
        };
        Content = _card;

        Opened += (_, _) =>
        {
            PlatformServices.ExcludeFromCapture(this);
            var cursor = PlatformServices.GetCursorPosition();
            var screen = Screens.ScreenFromPoint(cursor) ?? Screens.Primary;
            if (screen is not null)
            {
                var wa = screen.WorkingArea;
                int w = (int)(Bounds.Width * RenderScaling);
                Position = new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Y + (int)(60 * RenderScaling));
            }
            _card.Opacity = 1;
            _card.RenderTransform = TransformOperations.Parse("scale(1)");
        };
    }

    private async Task UpdateAsync()
    {
        if (_update.AssetUrl is null)
        {
            // Release without an attached installer: open its page to download.
            Updater.OpenPage(_update.PageUrl);
            Close();
            return;
        }

        _buttons.IsVisible = false;
        try
        {
            var progress = new Progress<int>(p => _status.Text = L.DownloadingUpdate(p));
            string installer = await Updater.DownloadAsync(_update, progress);
            Updater.RunInstallerAndQuit(installer);
        }
        catch (Exception ex)
        {
            Log.Info("Update download failed: " + ex.Message);
            _status.Text = L.UpdateFailed;
            _buttons.IsVisible = true;
        }
    }

    private static Button Pill(string text, IBrush background) => new()
    {
        Content = new TextBlock { Text = text, FontSize = 12, Foreground = Brushes.White },
        Background = background,
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(14, 6),
        Cursor = new Cursor(StandardCursorType.Hand),
    };
}

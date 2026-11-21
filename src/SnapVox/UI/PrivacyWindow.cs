using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SnapVox.Localization;
using SnapVox.Platform;

namespace SnapVox.UI;

/// <summary>
/// Asked before the selected text is sent to Microsoft for the first time.
/// Answers true (use Microsoft voices), false (offline voices only) or null (closed without answering).
/// </summary>
internal sealed class PrivacyWindow : Window
{
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2F7FD8"));
    private static readonly IBrush ChipBg = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));

    private readonly TaskCompletionSource<bool?> _answer = new();

    public static PrivacyWindow? Current { get; private set; }

    public static Task<bool?> AskAsync()
    {
        if (Current is { } open)
            return open._answer.Task;

        var window = new PrivacyWindow();
        Current = window;
        window.Closed += (_, _) =>
        {
            Current = null;
            window._answer.TrySetResult(null);
        };
        window.Show();
        return window._answer.Task;
    }

    public static void CloseCurrent() => Current?.Close();

    private PrivacyWindow()
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
        Cursor = new Cursor(StandardCursorType.Arrow);

        var offline = Pill(L.PrivacyOffline, ChipBg);
        offline.Click += (_, _) => Answer(false);
        var online = Pill(L.PrivacyAccept, Accent);
        online.Click += (_, _) => Answer(true);

        Content = new Border
        {
            Width = 400,
            Background = new SolidColorBrush(Color.Parse("#0B0B0D")),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(0.5),
            CornerRadius = new CornerRadius(26),
            Padding = new Thickness(20, 18),
            Margin = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = L.PrivacyTitle, FontSize = 15, FontWeight = FontWeight.Medium, Foreground = Brushes.White },
                    new TextBlock { Text = L.PrivacyBody, FontSize = 12.5, LineHeight = 19, Foreground = Muted, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { offline, online },
                    },
                },
            },
        };

        Opened += (_, _) =>
        {
            PlatformServices.MakeToolWindow(this, noActivate: false);
            PlatformServices.ExcludeFromCapture(this);
            var cursor = PlatformServices.GetCursorPosition();
            if ((Screens.ScreenFromPoint(cursor) ?? Screens.Primary) is { } screen)
            {
                var area = screen.WorkingArea;
                int width = (int)(Bounds.Width * RenderScaling), height = (int)(Bounds.Height * RenderScaling);
                Position = new PixelPoint(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2);
            }
            PlatformServices.BringToTop(this);
        };
    }

    private void Answer(bool allow)
    {
        _answer.TrySetResult(allow);
        Close();
    }

    private static Button Pill(string text, IBrush background) => new()
    {
        Content = new TextBlock { Text = text, FontSize = 12, Foreground = Brushes.White },
        Background = background,
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(14, 7),
        Cursor = new Cursor(StandardCursorType.Hand),
    };
}

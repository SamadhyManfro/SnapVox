using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using SnapVox.Platform;

namespace SnapVox.UI;

/// <summary>
/// Temporary black pill in the island style, at the top of the monitor where the mouse is.
/// Shown at startup to confirm the hotkey is ready.
/// </summary>
internal sealed class IslandToast : Window
{
    private static IslandToast? _current;
    private readonly Border _pill;
    private readonly DispatcherTimer _hideTimer;

    private IslandToast(string text, bool isWarning)
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        var icon = new Image { Source = LogoRenderer.Render(56), Width = 28, Height = 28 };
        var label = new TextBlock
        {
            Text = text,
            Foreground = isWarning ? new SolidColorBrush(Color.Parse("#FFB454")) : Brushes.White,
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 6, 0),
        };

        _pill = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0B0B0D")),
            BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderThickness = new Thickness(0.5),
            CornerRadius = new CornerRadius(23),
            Height = 46,
            Padding = new Thickness(9, 0, 14, 0),
            Margin = new Thickness(12),
            Opacity = 0,
            RenderTransform = TransformOperations.Parse("scale(0.7)"),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { icon, label },
            },
            Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(250) },
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(450),
                    Easing = new BackEaseOut(),
                },
            ],
        };
        Content = _pill;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _hideTimer.Tick += (_, _) => Dismiss();
    }

    public static void Show(string text, bool isWarning = false)
    {
        _current?.Close();
        var toast = new IslandToast(text, isWarning);
        _current = toast;
        toast.Opened += (_, _) => toast.PlaceAndAnimate();
        toast.Show();
    }

    private void PlaceAndAnimate()
    {
        // Monitor where the mouse is.
        var cursor = PlatformServices.GetCursorPosition();
        var screen = Screens.ScreenFromPoint(cursor) ?? Screens.Primary;
        if (screen is not null)
        {
            var wa = screen.WorkingArea;
            double scale = screen.Scaling;
            int w = (int)(Bounds.Width * scale);
            Position = new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Y + (int)(2 * scale));
        }

        _pill.Opacity = 1;
        _pill.RenderTransform = TransformOperations.Parse("scale(1)");
        _hideTimer.Start();
    }

    private void Dismiss()
    {
        _hideTimer.Stop();
        _pill.Opacity = 0;
        _pill.RenderTransform = TransformOperations.Parse("scale(0.85)");
        DispatcherTimer.RunOnce(() =>
        {
            Close();
            if (_current == this)
                _current = null;
        }, TimeSpan.FromMilliseconds(300));
    }
}

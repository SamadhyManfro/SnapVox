using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace SnapVox.UI;

/// <summary>
/// A line of text that scrolls like a music player's title when it doesn't fit:
/// it waits a moment, slides to the left and loops, with soft fades at the edges.
/// </summary>
internal sealed class MarqueeText : Control
{
    private const double Gap = 44, PixelsPerSecond = 34, PauseSeconds = 1.6, FadeWidth = 18;

    private string _text = "";
    private TextLayout? _layout;
    private double _offset, _pause = PauseSeconds;
    private TimeSpan? _lastFrame;
    private bool _running, _attached;

    public IBrush? Foreground { get; init; }
    public double FontSize { get; init; } = 12;
    public FontWeight FontWeight { get; init; } = FontWeight.Normal;

    public MarqueeText()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        SizeChanged += (_, _) => UpdateScrolling();
    }

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value == _text)
                return;
            // A progress update ("Installing Korean · 41%") must not restart the scroll; a new message does.
            int same = Math.Min(8, Math.Min(value.Length, _text.Length));
            if (same == 0 || !value.AsSpan(0, same).SequenceEqual(_text.AsSpan(0, same)))
            {
                _offset = 0;
                _pause = PauseSeconds;
            }
            _text = value;
            _layout?.Dispose();
            _layout = null;
            InvalidateMeasure();
            InvalidateVisual();
            UpdateScrolling();
        }
    }

    private TextLayout Layout => _layout ??= new TextLayout(
        _text, new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight), FontSize, Foreground);

    private bool Overflows => Bounds.Width > 0 && Layout.WidthIncludingTrailingWhitespace > Bounds.Width + 0.5;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = Layout.WidthIncludingTrailingWhitespace;
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), Layout.Height);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _layout?.Dispose();
        _layout = null; // the inherited font is known only now
        InvalidateMeasure();
        UpdateScrolling();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _running = false;
    }

    private void UpdateScrolling()
    {
        if (!_attached)
            return;
        if (!Overflows)
        {
            _offset = 0;
            _pause = PauseSeconds;
            return;
        }
        if (_running)
            return;
        _running = true;
        _lastFrame = null;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        if (!_running)
            return;
        if (!Overflows)
        {
            _running = false;
            _offset = 0;
            _pause = PauseSeconds;
            InvalidateVisual();
            return;
        }

        double seconds = _lastFrame is { } last ? Math.Clamp((time - last).TotalSeconds, 0, 0.1) : 0;
        _lastFrame = time;
        if (_pause > 0)
        {
            _pause -= seconds;
        }
        else
        {
            _offset += PixelsPerSecond * seconds;
            if (_offset >= Layout.WidthIncludingTrailingWhitespace + Gap)
            {
                _offset = 0;
                _pause = PauseSeconds;
            }
            InvalidateVisual();
        }
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        if (!Overflows)
        {
            Layout.Draw(context, default);
            return;
        }

        double fade = Math.Min(0.25, FadeWidth / area.Width);
        var mask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(_offset > 0 ? Colors.Transparent : Colors.White, 0),
                new GradientStop(Colors.White, fade),
                new GradientStop(Colors.White, 1 - fade),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        using (context.PushOpacityMask(mask, area))
        {
            Layout.Draw(context, new Point(-_offset, 0));
            Layout.Draw(context, new Point(Layout.WidthIncludingTrailingWhitespace + Gap - _offset, 0));
        }
    }
}

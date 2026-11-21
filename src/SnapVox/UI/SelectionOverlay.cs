using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SnapVox.Platform;

namespace SnapVox.UI;

/// <summary>
/// Transparent window covering one monitor: dims the screen, lets you drag a rectangular selection and,
/// once released, shows the "frozen photo" of the selection (92% opaque) with an animated dashed border.
/// There is one per monitor (each monitor may have a different scale/DPI).
/// The mouse wheel goes through to the page underneath, so you can scroll while it reads.
/// </summary>
internal sealed class SelectionOverlay : Window
{
    private readonly OverlaySurface _surface;
    private readonly DispatcherTimer _scrollEnd;

    public Screen TargetScreen { get; }

    /// <summary>Selection finished: rectangle in physical pixels of the virtual desktop.</summary>
    public event Action<SelectionOverlay, PixelRect>? SelectionCompleted;

    /// <summary>The user pressed Esc.</summary>
    public event Action? CancelRequested;

    /// <summary>Other keys (Space, arrows…) for the player shortcuts.</summary>
    public event Action<Key>? KeyPressed;

    /// <summary>Started dragging a new selection (also on top of a reading in progress).</summary>
    public event Action<SelectionOverlay>? SelectionStarted;

    public SelectionOverlay(Screen screen)
    {
        TargetScreen = screen;

        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = new Cursor(StandardCursorType.Cross);

        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;

        _surface = new OverlaySurface();
        _surface.Completed += OnSurfaceCompleted;
        _surface.Started += () => SelectionStarted?.Invoke(this);
        _surface.Wheel += OnWheel;
        Content = _surface;

        // After the last wheel turn, the dimming catches the mouse again (to allow dragging a new selection).
        _scrollEnd = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Normal, (_, _) =>
        {
            _scrollEnd!.Stop();
            PlatformServices.SetClickThrough(this, false);
        });

        Opened += (_, _) =>
        {
            PlatformServices.MakeToolWindow(this, noActivate: false);
            PlatformServices.ExcludeFromCapture(this); // never appears in our own screen capture
            FitToScreen();
        };
        ScalingChanged += (_, _) => FitToScreen();
        Closed += (_, _) => _scrollEnd.Stop();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                CancelRequested?.Invoke();
            else
                KeyPressed?.Invoke(e.Key);
            e.Handled = true;
        };
    }

    /// <summary>
    /// Scrolling: the dimming lets the mouse through for a moment and passes this wheel turn on to the page
    /// underneath. While the person keeps scrolling, the next turns go straight to the page; shortly after
    /// they stop, the dimming takes the mouse back.
    /// </summary>
    private void OnWheel(Vector delta)
    {
        PlatformServices.SetClickThrough(this, true);
        _scrollEnd.Stop();
        _scrollEnd.Start();
        Dispatcher.UIThread.Post(() =>
        {
            if (delta.Y != 0)
                PlatformServices.SendWheel((int)Math.Round(delta.Y * 120), horizontal: false);
            if (delta.X != 0)
                PlatformServices.SendWheel((int)Math.Round(-delta.X * 120), horizontal: true);
        }, DispatcherPriority.Background);
    }

    /// <summary>Makes sure the window covers exactly the monitor, even if the DPI changed when it opened.</summary>
    private void FitToScreen()
    {
        Position = TargetScreen.Bounds.Position;
        Width = TargetScreen.Bounds.Width / RenderScaling;
        Height = TargetScreen.Bounds.Height / RenderScaling;
    }

    private void OnSurfaceCompleted(Rect dipRect)
    {
        double s = RenderScaling;
        var px = new PixelRect(
            TargetScreen.Bounds.X + (int)Math.Round(dipRect.X * s),
            TargetScreen.Bounds.Y + (int)Math.Round(dipRect.Y * s),
            Math.Max(1, (int)Math.Round(dipRect.Width * s)),
            Math.Max(1, (int)Math.Round(dipRect.Height * s)));
        SelectionCompleted?.Invoke(this, px);
    }

    /// <summary>
    /// Shows the frozen photo of the selection. The window keeps receiving the mouse: dragging again
    /// anywhere starts another selection right away.
    /// </summary>
    public void Freeze(Bitmap snapshot) => _surface.Freeze(snapshot);

    /// <summary>Back to a plain dimmed screen waiting for a selection (no photo, no highlight).</summary>
    public void ClearSelection() => _surface.Clear();

    public void StopAnimations() => _surface.Stop();

    /// <summary>Highlights a word (box in pixels of the selection image); null hides it.</summary>
    public void SetHighlight(Rect? imageBox) => _surface.SetHighlight(imageBox);

    /// <summary>Draws the dimming, the selection "hole" and the dashed border.</summary>
    internal sealed class OverlaySurface : Control, Avalonia.Rendering.ICustomHitTest
    {
        /// <summary>Only for the render test (--render-test).</summary>
        internal void SetSelectionForTest(Rect selection) => _selection = selection;

        // The whole surface receives the mouse, including the transparent selection "hole".
        public bool HitTest(Point point) => true;

        private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)); // ~55%
        private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#4DA3FF"));
        private const double Radius = 8;
        private const double SnapshotOpacity = 0.92;

        private readonly DispatcherTimer _timer;
        private Point? _start;
        private Rect _selection;
        private bool _dragging;
        private Bitmap? _snapshot;
        private double _antsOffset;
        private double _flash; // 0.2 → 0 right after the snap

        // Highlight of the spoken word: slides smoothly to its target.
        private static readonly IBrush Highlight = new SolidColorBrush(Color.FromArgb(82, 77, 163, 255));
        private Rect? _hlTarget;
        private Rect _hlCurrent;
        private double _hlAlpha;

        public event Action<Rect>? Completed;

        /// <summary>Started dragging (so whoever is reading can stop).</summary>
        public event Action? Started;

        /// <summary>Mouse wheel turned (to pass on to the page underneath).</summary>
        public event Action<Vector>? Wheel;

        public OverlaySurface()
        {
            ClipToBounds = true;
            Focusable = true;
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
        }

        public void Stop() => _timer.Stop();

        public void Freeze(Bitmap snapshot)
        {
            _snapshot = snapshot;
            _flash = 0.2;
            InvalidateVisual();
        }

        /// <summary>Clears selection/photo/highlight: back to a plain dimmed screen.</summary>
        public void Clear()
        {
            _dragging = false;
            _selection = default;
            _snapshot = null;
            _hlTarget = null;
            _hlAlpha = 0;
            _flash = 0;
            _timer.Stop();
            InvalidateVisual();
        }

        /// <summary>
        /// Highlights a word. <paramref name="imageBox"/> comes from the OCR, in pixels of the selection image;
        /// null hides the highlight.
        /// </summary>
        public void SetHighlight(Rect? imageBox)
        {
            if (imageBox is not { } box || _snapshot is null)
            {
                _hlTarget = null;
                return;
            }

            double sx = _selection.Width / _snapshot.PixelSize.Width;
            double sy = _selection.Height / _snapshot.PixelSize.Height;
            var target = new Rect(
                _selection.X + box.X * sx - 3, _selection.Y + box.Y * sy - 2,
                box.Width * sx + 6, box.Height * sy + 4);

            if (_hlAlpha <= 0.01)
                _hlCurrent = target; // first word: appears in place, without sliding
            _hlTarget = target;
        }

        private void Tick()
        {
            // Fixed dash length: only the offset changes, so the "marching ants" move without resizing.
            _antsOffset -= 0.35;
            if (_flash > 0)
                _flash = Math.Max(0, _flash - 0.022);

            if (_hlTarget is { } t)
            {
                const double k = 0.45; // how far it moves toward the target per frame
                _hlCurrent = new Rect(
                    _hlCurrent.X + (t.X - _hlCurrent.X) * k, _hlCurrent.Y + (t.Y - _hlCurrent.Y) * k,
                    _hlCurrent.Width + (t.Width - _hlCurrent.Width) * k, _hlCurrent.Height + (t.Height - _hlCurrent.Height) * k);
                _hlAlpha = Math.Min(1, _hlAlpha + 0.25);
            }
            else
            {
                _hlAlpha = Math.Max(0, _hlAlpha - 0.15);
            }
            if (_selection.Width > 0)
                InvalidateVisual();
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            Wheel?.Invoke(e.Delta);
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            // May be a new selection on top of a reading: drop the previous photo and highlight.
            _snapshot = null;
            _hlTarget = null;
            _hlAlpha = 0;
            _flash = 0;
            Started?.Invoke();
            _start = e.GetPosition(this);
            _selection = new Rect(_start.Value, new Size(0, 0));
            _dragging = true;
            e.Pointer.Capture(this);
            _timer.Start();
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!_dragging || _start is null)
                return;
            var p = e.GetPosition(this);
            p = new Point(Math.Clamp(p.X, 0, Bounds.Width), Math.Clamp(p.Y, 0, Bounds.Height));
            _selection = new Rect(_start.Value, p).Normalize();
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_dragging)
                return;
            _dragging = false;
            e.Pointer.Capture(null);

            if (_selection.Width < 6 || _selection.Height < 6)
            {
                // A click without dragging: keep waiting for a selection.
                _selection = default;
                _timer.Stop();
                InvalidateVisual();
                return;
            }
            Completed?.Invoke(_selection);
        }

        public override void Render(DrawingContext ctx)
        {
            var full = new Rect(Bounds.Size);

            if (_selection.Width <= 0 || _selection.Height <= 0)
            {
                ctx.DrawRectangle(Dim, null, full);
                return;
            }

            // Dims everything except the selection (a hole with rounded corners).
            var dimGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
            dimGeometry.Children.Add(new RectangleGeometry(full));
            dimGeometry.Children.Add(new RectangleGeometry(_selection, Radius, Radius));
            ctx.DrawGeometry(Dim, null, dimGeometry);

            var rounded = new RoundedRect(_selection, Radius);
            if (_snapshot is not null)
            {
                using (ctx.PushClip(rounded))
                {
                    using (ctx.PushOpacity(SnapshotOpacity))
                        ctx.DrawImage(_snapshot, _selection);
                    if (_flash > 0)
                        ctx.DrawRectangle(new SolidColorBrush(Colors.White, _flash), null, _selection);
                    if (_hlAlpha > 0)
                        using (ctx.PushOpacity(_hlAlpha))
                            ctx.DrawRectangle(Highlight, null, new RoundedRect(_hlCurrent, 5));
                }
            }

            // Dashed border: 6 px dash, 5 px gap (with thickness 2 → 3 and 2.5).
            var pen = new Pen(Accent, 2, new DashStyle([3, 2.5], _antsOffset), PenLineCap.Flat);
            ctx.DrawRectangle(null, pen, rounded);
        }
    }
}

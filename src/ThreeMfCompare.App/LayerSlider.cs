using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ThreeMfCompare.App;

/// <summary>
/// The heights shown, as in the slicer's preview: a vertical track beside the viewports with a handle
/// for the highest level shown at the top and one for the lowest at the bottom, each labelled (with
/// <see cref="Labels"/> where given, else its number). Drag a handle, or the wheel and the arrow keys
/// for the top one (with Shift for the bottom one, with Ctrl for both together); a click on the track
/// moves the nearer handle there. Adapted from GcodeFEM's layer slider.
/// </summary>
public sealed class LayerSlider : FrameworkElement
{
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(LayerSlider), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LowerProperty =
        DependencyProperty.Register(nameof(Lower), typeof(int), typeof(LayerSlider),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty UpperProperty =
        DependencyProperty.Register(nameof(Upper), typeof(int), typeof(LayerSlider),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LabelsProperty =
        DependencyProperty.Register(nameof(Labels), typeof(IReadOnlyList<string>), typeof(LayerSlider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The number of levels; they are counted from 1.</summary>
    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Lower
    {
        get => (int)GetValue(LowerProperty);
        set => SetValue(LowerProperty, value);
    }

    public int Upper
    {
        get => (int)GetValue(UpperProperty);
        set => SetValue(UpperProperty, value);
    }

    /// <summary>What each level is called on its handle, from level 1.</summary>
    public IReadOnlyList<string>? Labels
    {
        get => (IReadOnlyList<string>?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    const double Inset = 16;     // above the top and below the bottom of the track: room for a handle and its label
    const double FromRight = 13; // the track's middle, from the right edge
    const double Knob = 7;

    bool? draggingUpper; // which handle the mouse holds, if any
    bool? hoverUpper;

    public LayerSlider()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Width = 92;
    }

    static Brush Resource(string key, Brush fallback) => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    double Top => Inset;
    double Bottom => Math.Max(Inset, ActualHeight - Inset);

    double Y(int level) => Maximum <= 1 ? Bottom : Bottom - (level - 1) * (Bottom - Top) / (Maximum - 1);

    int Level(double y) => Maximum <= 1 ? 1 : Math.Clamp((int)Math.Round(1 + (Bottom - y) / (Bottom - Top) * (Maximum - 1)), 1, Maximum);

    string Label(int level) => Labels is { } labels && level >= 1 && level <= labels.Count ? labels[level - 1] : level.ToString(CultureInfo.CurrentCulture);

    protected override void OnRender(DrawingContext dc)
    {
        var x = ActualWidth - FromRight;
        // Transparent, so that the whole strip takes the mouse.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var track = Resource("TrackBrush", Brushes.DimGray);
        var range = Resource("PrimaryBrush", Brushes.WhiteSmoke);
        var ring = Resource("ViewportBrush", Brushes.Black);
        dc.DrawRoundedRectangle(track, null, new Rect(x - 2, Top, 4, Bottom - Top), 2, 2);
        double upper = Y(Math.Clamp(Upper, 1, Math.Max(1, Maximum))), lower = Y(Math.Clamp(Lower, 1, Math.Max(1, Maximum)));
        dc.DrawRoundedRectangle(range, null, new Rect(x - 2, upper, 4, Math.Max(0, lower - upper)), 2, 2);

        // The labels move apart when the handles come close, the top one up and the bottom one down.
        var overlap = Math.Max(0, 22 - (lower - upper)) / 2;
        Handle(dc, x, upper, upper - overlap, Label(Upper), hoverUpper == true || draggingUpper == true, range, ring);
        Handle(dc, x, lower, lower + overlap, Label(Lower), hoverUpper == false || draggingUpper == false, range, ring);
    }

    void Handle(DrawingContext dc, double x, double y, double labelY, string label, bool hot, Brush fill, Brush ring)
    {
        dc.DrawEllipse(fill, new Pen(ring, 2), new Point(x, y), hot ? Knob + 1.5 : Knob, hot ? Knob + 1.5 : Knob);
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12,
            Resource("TextBrush", Brushes.White), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var box = new Rect(x - Knob - 8 - text.Width - 14, labelY - 10, text.Width + 14, 20);
        dc.DrawRoundedRectangle(Resource("OverlayBrush", Brushes.Black), new Pen(Resource("OverlayBorderBrush", Brushes.Gray), 1), box, 5, 5);
        dc.DrawText(text, new Point(box.X + 7, box.Y + (box.Height - text.Height) / 2));
    }

    /// <summary>The handle nearer to <paramref name="y"/>; when both sit on the same level, the one on that side.</summary>
    bool NearerIsUpper(double y)
    {
        double upper = Y(Upper), lower = Y(Lower);
        if (Math.Abs(upper - lower) < 1) return y <= upper;
        return Math.Abs(y - upper) <= Math.Abs(y - lower);
    }

    void MoveTo(bool upper, int level)
    {
        if (upper) Upper = Math.Clamp(level, Lower, Maximum);
        else Lower = Math.Clamp(level, 1, Upper);
    }

    /// <summary>Both handles by the same number of levels, keeping the range: steps through the print a slab at a time.</summary>
    void MoveBoth(int by)
    {
        by = Math.Clamp(by, 1 - Lower, Maximum - Upper);
        if (by > 0) (Upper, Lower) = (Upper + by, Lower + by);
        else (Lower, Upper) = (Lower + by, Upper + by);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var y = e.GetPosition(this).Y;
        draggingUpper = NearerIsUpper(y);
        MoveTo(draggingUpper.Value, Level(y));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var y = e.GetPosition(this).Y;
        if (draggingUpper is { } upper) MoveTo(upper, Level(y));
        else
        {
            var nearer = NearerIsUpper(y);
            if (hoverUpper != nearer)
            {
                hoverUpper = nearer;
                InvalidateVisual();
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        draggingUpper = null;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        hoverUpper = null;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var step = Math.Sign(e.Delta);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) MoveBoth(step);
        else
        {
            var upper = (Keyboard.Modifiers & ModifierKeys.Shift) == 0;
            MoveTo(upper, (upper ? Upper : Lower) + step);
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int? by = e.Key switch
        {
            Key.Up or Key.Right => 1,
            Key.Down or Key.Left => -1,
            Key.PageUp => 10,
            Key.PageDown => -10,
            Key.Home => Maximum,
            Key.End => -Maximum,
            _ => null,
        };
        if (by is not { } step) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) MoveBoth(step);
        else
        {
            var upper = (Keyboard.Modifiers & ModifierKeys.Shift) == 0;
            MoveTo(upper, (upper ? Upper : Lower) + step);
        }
        e.Handled = true;
    }
}

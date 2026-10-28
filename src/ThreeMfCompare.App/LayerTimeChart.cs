using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ThreeMfCompare.App;

/// <summary>One file's line in the chart: per layer, its top and its time.</summary>
public sealed record ChartSeries(string Name, Color Colour, IReadOnlyList<(double Z, double Seconds)> Points);

/// <summary>
/// Layer time over height, one line per file in the file's colour: where one profile spends its time
/// and the other does not. The mouse shows the values at its height.
/// </summary>
public sealed class LayerTimeChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(LayerTimeChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartSeries>? Series
    {
        get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    const double Left = 52, Right = 16, Top = 12, Bottom = 30;

    Point? mouse;

    static Brush Resource(string key, Brush fallback) => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        mouse = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        mouse = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var series = Series?.Where(s => s.Points.Count > 0).ToList();
        if (series is null || series.Count == 0) return;

        var plot = new Rect(Left, Top, Math.Max(10, ActualWidth - Left - Right), Math.Max(10, ActualHeight - Top - Bottom));
        var maxZ = series.Max(s => s.Points.Max(p => p.Z));
        // The top of the time axis at the 99.5th percentile: a single slow first layer would otherwise flatten the rest.
        var times = series.SelectMany(s => s.Points.Select(p => p.Seconds)).Order().ToList();
        var maxT = Math.Max(1, NiceCeiling(times[(int)(0.995 * (times.Count - 1))] * 1.08));

        var grid = new Pen(Resource("DividerBrush", Brushes.DimGray), 1);
        var muted = Resource("TextMutedBrush", Brushes.Gray);
        foreach (var t in Ticks(maxT))
        {
            var y = plot.Bottom - t / maxT * plot.Height;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            Text(dc, Format(t), muted, new Point(plot.Left - 6, y), alignRight: true);
        }
        foreach (var z in Ticks(maxZ))
        {
            var x = plot.Left + z / maxZ * plot.Width;
            dc.DrawLine(grid, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            Text(dc, $"{z.ToString("0.#", CultureInfo.CurrentCulture)} mm", muted, new Point(x, plot.Bottom + 14), centre: true);
        }

        dc.PushClip(new RectangleGeometry(new Rect(plot.Left, plot.Top - 2, plot.Width, plot.Height + 4)));
        foreach (var s in series)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var first = true;
                foreach (var (z, seconds) in s.Points)
                {
                    var point = new Point(plot.Left + z / maxZ * plot.Width, plot.Bottom - Math.Min(seconds, maxT * 1.2) / maxT * plot.Height);
                    if (first) context.BeginFigure(point, false, false);
                    else context.LineTo(point, true, false);
                    first = false;
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(s.Colour), 1.5) { LineJoin = PenLineJoin.Round }, geometry);
        }
        dc.Pop();

        if (mouse is { } m && plot.Contains(m))
        {
            var z = (m.X - plot.Left) / plot.Width * maxZ;
            dc.DrawLine(new Pen(Resource("TextSecondaryBrush", Brushes.LightGray), 1) { DashStyle = DashStyles.Dash }, new Point(m.X, plot.Top), new Point(m.X, plot.Bottom));
            var lines = new List<(string Text, Brush Brush)> { ($"{z.ToString("0.00", CultureInfo.CurrentCulture)} mm", Resource("TextBrush", Brushes.White)) };
            foreach (var s in series)
            {
                // The layer printing that height: the first whose top is at or above it.
                var at = s.Points.FirstOrDefault(p => p.Z >= z - 1e-4);
                if (at == default) continue;
                lines.Add(($"{s.Name}: {Format(at.Seconds)}", new SolidColorBrush(s.Colour)));
            }
            var texts = lines.Select(l => Formatted(l.Text, l.Brush)).ToList();
            var width = texts.Max(t => t.Width) + 20;
            var height = texts.Sum(t => t.Height + 2) + 14;
            var box = new Rect(m.X + 12 + width > plot.Right ? m.X - 12 - width : m.X + 12, plot.Top + 6, width, height);
            dc.DrawRoundedRectangle(Resource("OverlayBrush", Brushes.Black), new Pen(Resource("OverlayBorderBrush", Brushes.Gray), 1), box, 6, 6);
            var y = box.Top + 7;
            foreach (var t in texts)
            {
                dc.DrawText(t, new Point(box.Left + 10, y));
                y += t.Height + 2;
            }
        }
    }

    static string Format(double seconds) => seconds >= 120
        ? $"{(seconds / 60).ToString("0.#", CultureInfo.CurrentCulture)} min"
        : $"{seconds.ToString("0.#", CultureInfo.CurrentCulture)} s";

    FormattedText Formatted(string text, Brush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    void Text(DrawingContext dc, string text, Brush brush, Point at, bool alignRight = false, bool centre = false)
    {
        var formatted = Formatted(text, brush);
        var x = alignRight ? at.X - formatted.Width : centre ? at.X - formatted.Width / 2 : at.X;
        dc.DrawText(formatted, new Point(x, at.Y - formatted.Height / 2));
    }

    /// <summary>Round steps from 0 to <paramref name="max"/>, about five of them.</summary>
    static IEnumerable<double> Ticks(double max)
    {
        if (max <= 0) yield break;
        var step = NiceCeiling(max / 5);
        for (var v = 0.0; v <= max + step * 1e-6; v += step) yield return v;
    }

    static double NiceCeiling(double value)
    {
        if (value <= 0) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 2, 2.5, 5, 10 })
            if (step * magnitude >= value) return step * magnitude;
        return 10 * magnitude;
    }
}

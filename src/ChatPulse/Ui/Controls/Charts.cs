using System.Windows;
using System.Windows.Media;

namespace ChatPulse.Ui.Controls;

/// <summary>
/// Column sparkline. Bars, not a line: chat is discrete events, and bars read better tiny.
/// <para>
/// Drawn in <see cref="OnRender"/> rather than composed from Shapes — a leaderboard holds dozens
/// of these and each would otherwise cost a visual tree of its own.
/// </para>
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    // IReadOnlyList rather than float[]: the XAML compiler treats an array-typed dependency
    // property as a collection property, which templates cannot set (MC4102).
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<float>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Color), typeof(Sparkline),
        new FrameworkPropertyMetadata(Color.FromRgb(0xA9, 0x70, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<float>? Values
    {
        get => (IReadOnlyList<float>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color Fill
    {
        get => (Color)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        if (values is null || values.Count == 0) return;

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var gap = Math.Clamp(w / values.Count * 0.28, 0.5, 3.0);
        var barWidth = Math.Max(1.0, (w - gap * (values.Count - 1)) / values.Count);
        var radius = Math.Min(barWidth, 3.0) / 2.0;

        for (var i = 0; i < values.Count; i++)
        {
            var v = Math.Clamp(values[i], 0f, 1f);
            var barHeight = Math.Max(h * v, h * 0.06);
            var x = i * (barWidth + gap);

            // Faded tail on the left = "older", so the eye lands on the live edge.
            var alpha = v <= 0 ? 0.13 : 0.35 + 0.65 * ((double)i / values.Count);
            var brush = new SolidColorBrush(Fill) { Opacity = alpha };
            brush.Freeze();

            dc.DrawRoundedRectangle(brush, null, new Rect(x, h - barHeight, barWidth, barHeight), radius, radius);
        }
    }
}

/// <summary>Filled area chart used where there is room for something richer than a sparkline.</summary>
public sealed class AreaChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<float>), typeof(AreaChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeColorProperty = DependencyProperty.Register(
        nameof(StrokeColor), typeof(Color), typeof(AreaChart),
        new FrameworkPropertyMetadata(Color.FromRgb(0xA9, 0x70, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<float>? Values
    {
        get => (IReadOnlyList<float>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color StrokeColor
    {
        get => (Color)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        if (values is null || values.Count < 2) return;

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var stepX = w / (values.Count - 1);
        double Y(float v) => h - (h - 2) * Math.Clamp(v, 0f, 1f) - 1;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, Y(values[0])), isFilled: true, isClosed: true);
            for (var i = 1; i < values.Count; i++)
            {
                // Horizontal control points give a smooth curve without overshooting past the data.
                var x0 = (i - 1) * stepX;
                var x1 = i * stepX;
                ctx.BezierTo(
                    new Point(x0 + stepX / 2, Y(values[i - 1])),
                    new Point(x1 - stepX / 2, Y(values[i])),
                    new Point(x1, Y(values[i])), true, false);
            }

            ctx.LineTo(new Point(w, h), false, false);
            ctx.LineTo(new Point(0, h), false, false);
        }

        geometry.Freeze();

        var fill = new LinearGradientBrush(
            Color.FromArgb(0x52, StrokeColor.R, StrokeColor.G, StrokeColor.B),
            Color.FromArgb(0x00, StrokeColor.R, StrokeColor.G, StrokeColor.B),
            new Point(0, 0), new Point(0, 1));
        fill.Freeze();

        var pen = new Pen(new SolidColorBrush(StrokeColor), 1.6) { LineJoin = PenLineJoin.Round };
        pen.Freeze();

        dc.DrawGeometry(fill, pen, geometry);
    }
}

/// <summary>Horizontal magnitude bar: how far ahead the leader is, at a glance.</summary>
public sealed class RateBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(RateBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarColorProperty = DependencyProperty.Register(
        nameof(BarColor), typeof(Color), typeof(RateBar),
        new FrameworkPropertyMetadata(Color.FromRgb(0xA9, 0x70, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Color BarColor
    {
        get => (Color)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    private static readonly SolidColorBrush Track = CreateFrozen(Color.FromRgb(0x1E, 0x1E, 0x2B));

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var r = h / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), r, r);

        var value = Math.Clamp(Fraction, 0, 1);
        if (value <= 0) return;

        var fill = new LinearGradientBrush(
            Color.FromArgb(0xA6, BarColor.R, BarColor.G, BarColor.B),
            BarColor,
            new Point(0, 0), new Point(1, 0));
        fill.Freeze();

        dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, Math.Max(w * value, h), h), r, r);
    }

    private static SolidColorBrush CreateFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>Donut for the unique-vs-repeated split.</summary>
public sealed class DonutRing : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(DonutRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingColorProperty = DependencyProperty.Register(
        nameof(RingColor), typeof(Color), typeof(DonutRing),
        new FrameworkPropertyMetadata(Color.FromRgb(0x3B, 0xE3, 0x9A), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(DonutRing),
        new FrameworkPropertyMetadata(9.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public Color RingColor
    {
        get => (Color)GetValue(RingColorProperty);
        set => SetValue(RingColorProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var stroke = Thickness;
        var radius = (size - stroke) / 2;
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);

        var trackPen = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2B)), stroke);
        trackPen.Freeze();
        dc.DrawEllipse(null, trackPen, centre, radius, radius);

        var value = Math.Clamp(Percent, 0, 100);
        if (value <= 0) return;

        var pen = new Pen(new SolidColorBrush(RingColor), stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        if (value >= 99.999)
        {
            dc.DrawEllipse(null, pen, centre, radius, radius);
            return;
        }

        var sweep = 360.0 * value / 100.0;
        var start = new Point(centre.X, centre.Y - radius);
        var endAngle = (sweep - 90) * Math.PI / 180.0;
        var end = new Point(centre.X + radius * Math.Cos(endAngle), centre.Y + radius * Math.Sin(endAngle));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}

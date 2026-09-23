using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ChatPulse.Ui.Controls;

/// <summary>
/// Pulsing dot — the strongest "this is live" signal on the window.
/// <para>
/// In an ordinary window a <see cref="DoubleAnimation"/> is affordable: WPF animates opacity on
/// the composition thread and repaints only the dirty region, so a 17px halo costs nothing.
/// </para>
/// <para>
/// Inside a window with <c>AllowsTransparency</c> it is the opposite. A layered window is
/// rendered in software and pushed whole through UpdateLayeredWindow, so a continuous animation
/// re-renders the entire surface at the monitor's refresh rate — measured at ~13% GPU and a
/// visible flicker on the overlay. Set <see cref="Animated"/> to false there and drive the pulse
/// with <see cref="Step"/> from whatever already updates that window.
/// </para>
/// </summary>
public sealed class LiveDot : Control
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(LiveDot),
        new PropertyMetadata(false, (d, _) => ((LiveDot)d).Rebuild()));

    public static readonly DependencyProperty AnimatedProperty = DependencyProperty.Register(
        nameof(Animated), typeof(bool), typeof(LiveDot),
        new PropertyMetadata(true, (d, _) => ((LiveDot)d).Rebuild()));

    public static readonly DependencyProperty DotColorProperty = DependencyProperty.Register(
        nameof(DotColor), typeof(Color), typeof(LiveDot),
        new PropertyMetadata(Color.FromRgb(0xFF, 0x3B, 0x5C), (d, _) => ((LiveDot)d).Rebuild()));

    public static readonly DependencyProperty DotSizeProperty = DependencyProperty.Register(
        nameof(DotSize), typeof(double), typeof(LiveDot),
        new PropertyMetadata(7.0, (d, _) => ((LiveDot)d).Rebuild()));

    private readonly Grid _root = new();
    private readonly System.Windows.Shapes.Ellipse _halo = new();
    private readonly System.Windows.Shapes.Ellipse _core = new();

    public LiveDot()
    {
        Focusable = false;
        _root.Children.Add(_halo);
        _root.Children.Add(_core);
        AddVisualChild(_root);
        AddLogicalChild(_root);
        Rebuild();
    }

    public bool Active
    {
        get => (bool)GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    /// <summary>False inside a layered window; then call <see cref="Step"/> instead.</summary>
    public bool Animated
    {
        get => (bool)GetValue(AnimatedProperty);
        set => SetValue(AnimatedProperty, value);
    }

    /// <summary>One period of a sine, coarse enough to be cheap and fine enough to look smooth.</summary>
    private static readonly double[] PulseAlphas = [0.08, 0.13, 0.20, 0.26, 0.30, 0.26, 0.20, 0.13];

    private int _step;

    /// <summary>Advances the pulse by one frame. Costs one repaint, not one per vsync.</summary>
    public void Step()
    {
        if (!Active || Animated) return;
        _step = (_step + 1) % PulseAlphas.Length;
        _halo.Opacity = PulseAlphas[_step];
    }

    public Color DotColor
    {
        get => (Color)GetValue(DotColorProperty);
        set => SetValue(DotColorProperty, value);
    }

    public double DotSize
    {
        get => (double)GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    private void Rebuild()
    {
        var size = DotSize;
        var color = Active ? DotColor : Color.FromRgb(0x45, 0x44, 0x58);

        _core.Width = size;
        _core.Height = size;
        _core.Fill = new SolidColorBrush(color);
        _core.HorizontalAlignment = HorizontalAlignment.Center;
        _core.VerticalAlignment = VerticalAlignment.Center;

        _halo.Width = size * 2.4;
        _halo.Height = size * 2.4;
        _halo.Fill = new SolidColorBrush(color);
        _halo.HorizontalAlignment = HorizontalAlignment.Center;
        _halo.VerticalAlignment = VerticalAlignment.Center;

        Width = size * 2.4;
        Height = size * 2.4;

        _halo.BeginAnimation(OpacityProperty, null);
        if (!Active)
        {
            _halo.Opacity = 0;
        }
        else if (Animated)
        {
            _halo.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                From = 0.08,
                To = 0.30,
                Duration = TimeSpan.FromMilliseconds(950),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
        }
        else
        {
            _halo.Opacity = PulseAlphas[_step];
        }

        InvalidateMeasure();
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _root;

    protected override Size MeasureOverride(Size availableSize)
    {
        _root.Measure(availableSize);
        return new Size(DotSize * 2.4, DotSize * 2.4);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _root.Arrange(new Rect(finalSize));
        return finalSize;
    }
}

/// <summary>App mark: rounded gradient tile with a heartbeat trace across it.</summary>
public sealed class LogoMark : FrameworkElement
{
    public static readonly DependencyProperty TileSizeProperty = DependencyProperty.Register(
        nameof(TileSize), typeof(double), typeof(LogoMark),
        new FrameworkPropertyMetadata(28.0,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double TileSize
    {
        get => (double)GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(TileSize, TileSize);

    protected override void OnRender(DrawingContext dc)
    {
        var s = TileSize;
        var background = new LinearGradientBrush(
            Color.FromRgb(0xB5, 0x7B, 0xFF), Color.FromRgb(0x6E, 0x3B, 0xE8),
            new Point(0, 0), new Point(1, 1));
        background.Freeze();

        dc.DrawRoundedRectangle(background, null, new Rect(0, 0, s, s), s / 3.4, s / 3.4);

        var pen = new Pen(Brushes.White, s * 0.085)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(s * 0.17, s * 0.55), false, false);
            ctx.LineTo(new Point(s * 0.34, s * 0.55), true, false);
            ctx.LineTo(new Point(s * 0.43, s * 0.28), true, false);
            ctx.LineTo(new Point(s * 0.57, s * 0.78), true, false);
            ctx.LineTo(new Point(s * 0.66, s * 0.47), true, false);
            ctx.LineTo(new Point(s * 0.73, s * 0.55), true, false);
            ctx.LineTo(new Point(s * 0.85, s * 0.55), true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}

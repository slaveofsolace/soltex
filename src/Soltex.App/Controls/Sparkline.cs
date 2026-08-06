using System.Windows;
using System.Windows.Media;

namespace Soltex.App.Controls;

/// <summary>
/// A bounded time-series line chart. The extra detail — gridlines, a
/// latest-value marker, and a second overlaid series — is opt-in, so compact
/// sparkline uses render exactly as before.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    private const int MaximumRenderedSamples = 120;

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable<double>),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Coral, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(2d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AutoScaleProperty = DependencyProperty.Register(
        nameof(AutoScale),
        typeof(bool),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Horizontal reference lines drawn between the scale bounds. Zero disables them.</summary>
    public static readonly DependencyProperty GridLineCountProperty = DependencyProperty.Register(
        nameof(GridLineCount),
        typeof(int),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Marks the most recent sample so the current value is locatable on a dense line.</summary>
    public static readonly DependencyProperty ShowLastPointProperty = DependencyProperty.Register(
        nameof(ShowLastPoint),
        typeof(bool),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>A second series drawn on the same scale, for paired readings such as receive and send.</summary>
    public static readonly DependencyProperty ComparisonValuesProperty = DependencyProperty.Register(
        nameof(ComparisonValues),
        typeof(IEnumerable<double>),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ComparisonStrokeProperty = DependencyProperty.Register(
        nameof(ComparisonStroke),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public bool AutoScale
    {
        get => (bool)GetValue(AutoScaleProperty);
        set => SetValue(AutoScaleProperty, value);
    }

    public int GridLineCount
    {
        get => (int)GetValue(GridLineCountProperty);
        set => SetValue(GridLineCountProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public bool ShowLastPoint
    {
        get => (bool)GetValue(ShowLastPointProperty);
        set => SetValue(ShowLastPointProperty, value);
    }

    public IEnumerable<double>? ComparisonValues
    {
        get => (IEnumerable<double>?)GetValue(ComparisonValuesProperty);
        set => SetValue(ComparisonValuesProperty, value);
    }

    public Brush ComparisonStroke
    {
        get => (Brush)GetValue(ComparisonStrokeProperty);
        set => SetValue(ComparisonStrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double[] samples = ReadSamples(Values);
        double[] comparison = ReadSamples(ComparisonValues);
        if (samples.Length < 2 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        (double minimum, double maximum) = ResolveScale(samples, comparison);
        DrawGridLines(drawingContext, minimum, maximum);

        // The primary series keeps its area fill; the comparison series is a
        // bare line so the two remain distinguishable when they overlap.
        Point[] points = ProjectPoints(samples, minimum, maximum);
        drawingContext.DrawGeometry(Fill, null, BuildAreaGeometry(points));
        drawingContext.DrawGeometry(null, CreatePen(Stroke, StrokeThickness), BuildLineGeometry(points));

        if (comparison.Length >= 2)
        {
            Point[] comparisonPoints = ProjectPoints(comparison, minimum, maximum);
            drawingContext.DrawGeometry(
                null,
                CreatePen(ComparisonStroke, Math.Max(1, StrokeThickness - 0.6)),
                BuildLineGeometry(comparisonPoints));
        }

        if (ShowLastPoint)
        {
            double radius = Math.Max(2.5, StrokeThickness + 0.8);
            drawingContext.DrawEllipse(Stroke, null, points[^1], radius, radius);
        }
    }

    private static double[] ReadSamples(IEnumerable<double>? values)
    {
        double[] samples = values?
            .Where(double.IsFinite)
            .TakeLast(MaximumRenderedSamples)
            .ToArray() ?? [];
        return samples.Length == 1 ? [samples[0], samples[0]] : samples;
    }

    private Point[] ProjectPoints(double[] samples, double minimum, double maximum)
    {
        double range = maximum - minimum;
        Point[] points = new Point[samples.Length];
        for (int index = 0; index < samples.Length; index++)
        {
            double x = index * ActualWidth / (samples.Length - 1);
            double normalized = (Math.Clamp(samples[index], minimum, maximum) - minimum) / range;
            points[index] = new Point(x, ActualHeight - (normalized * ActualHeight));
        }

        return points;
    }

    private StreamGeometry BuildAreaGeometry(Point[] points)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(0, ActualHeight), isFilled: true, isClosed: true);
            context.LineTo(points[0], isStroked: false, isSmoothJoin: false);
            context.PolyLineTo(points[1..], isStroked: false, isSmoothJoin: true);
            context.LineTo(new Point(ActualWidth, ActualHeight), isStroked: false, isSmoothJoin: false);
        }

        return geometry;
    }

    private static StreamGeometry BuildLineGeometry(Point[] points)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            context.PolyLineTo(points[1..], isStroked: true, isSmoothJoin: true);
        }

        return geometry;
    }

    private static Pen CreatePen(Brush brush, double thickness) =>
        new(brush, Math.Max(1, thickness))
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

    private void DrawGridLines(DrawingContext drawingContext, double minimum, double maximum)
    {
        if (GridLineCount <= 0)
        {
            return;
        }

        // Snap to whole device pixels so hairlines stay crisp rather than blurring across two rows.
        Pen gridPen = new(GridBrush, 1);
        gridPen.Freeze();
        int divisions = Math.Min(GridLineCount, 8) + 1;
        for (int line = 1; line < divisions; line++)
        {
            double y = Math.Round(ActualHeight * line / divisions) + 0.5;
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(ActualWidth, y));
        }
    }

    private (double Minimum, double Maximum) ResolveScale(
        double[] samples,
        double[] comparison)
    {
        if (AutoScale)
        {
            double minimum = Math.Min(0, samples.Min());
            double maximum = samples.Max();
            if (comparison.Length > 0)
            {
                minimum = Math.Min(minimum, Math.Min(0, comparison.Min()));
                maximum = Math.Max(maximum, comparison.Max());
            }

            if (maximum <= minimum)
            {
                maximum = minimum + 1;
            }

            return (minimum, maximum);
        }

        double configuredMinimum = double.IsFinite(Minimum) ? Minimum : 0;
        double configuredMaximum = double.IsFinite(Maximum) ? Maximum : 100;
        return configuredMaximum > configuredMinimum
            ? (configuredMinimum, configuredMaximum)
            : (0, 100);
    }
}

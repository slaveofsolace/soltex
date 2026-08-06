using System.Windows;
using System.Windows.Media;

namespace Soltex.App.Controls;

public sealed class Sparkline : FrameworkElement
{
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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double[] samples = Values?
            .Where(double.IsFinite)
            .TakeLast(120)
            .ToArray() ?? [];
        if (samples.Length == 1)
        {
            samples = [samples[0], samples[0]];
        }

        if (samples.Length < 2 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        (double minimum, double maximum) = ResolveScale(samples);
        double range = maximum - minimum;
        Point[] points = new Point[samples.Length];
        for (int index = 0; index < samples.Length; index++)
        {
            double x = index * ActualWidth / (samples.Length - 1);
            double normalized = (Math.Clamp(samples[index], minimum, maximum) - minimum) / range;
            double y = ActualHeight - normalized * ActualHeight;
            points[index] = new Point(x, y);
        }

        StreamGeometry fillGeometry = new();
        using (StreamGeometryContext context = fillGeometry.Open())
        {
            context.BeginFigure(new Point(0, ActualHeight), isFilled: true, isClosed: true);
            context.LineTo(points[0], isStroked: false, isSmoothJoin: false);
            context.PolyLineTo(points.Skip(1).ToArray(), isStroked: false, isSmoothJoin: true);
            context.LineTo(new Point(ActualWidth, ActualHeight), isStroked: false, isSmoothJoin: false);
        }

        StreamGeometry strokeGeometry = new();
        using (StreamGeometryContext context = strokeGeometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            context.PolyLineTo(points.Skip(1).ToArray(), isStroked: true, isSmoothJoin: true);
        }

        Pen pen = new(Stroke, Math.Max(1, StrokeThickness))
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        drawingContext.DrawGeometry(Fill, null, fillGeometry);
        drawingContext.DrawGeometry(null, pen, strokeGeometry);
    }

    private (double Minimum, double Maximum) ResolveScale(IReadOnlyList<double> samples)
    {
        if (AutoScale)
        {
            double minimum = Math.Min(0, samples.Min());
            double maximum = samples.Max();
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

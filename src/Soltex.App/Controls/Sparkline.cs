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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double[] samples = Values?
            .Where(double.IsFinite)
            .TakeLast(120)
            .Select(value => Math.Clamp(value, 0, 100))
            .ToArray() ?? [];
        if (samples.Length < 2 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        Point[] points = new Point[samples.Length];
        for (int index = 0; index < samples.Length; index++)
        {
            double x = index * ActualWidth / (samples.Length - 1);
            double y = ActualHeight - samples[index] / 100d * ActualHeight;
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
}

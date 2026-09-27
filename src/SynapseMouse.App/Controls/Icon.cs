using System.Windows;
using System.Windows.Media;

namespace SynapseMouse.App.Controls;

/// <summary>
/// Draws a 24×24 stroke icon geometry scaled to the element size with a constant on-screen stroke width.
/// </summary>
internal sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Icon), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Icon), new FrameworkPropertyMetadata(1.6, FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsNaN(Width) ? 18 : Width,
        double.IsNaN(Height) ? 18 : Height);

    protected override void OnRender(DrawingContext dc)
    {
        if (Data is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        double scale = Math.Min(ActualWidth, ActualHeight) / 24.0;
        var pen = new Pen(Stroke, StrokeThickness / scale)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        dc.PushTransform(new TranslateTransform((ActualWidth - (24 * scale)) / 2, (ActualHeight - (24 * scale)) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, pen, Data);
        dc.Pop();
        dc.Pop();
    }
}

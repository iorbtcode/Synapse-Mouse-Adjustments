using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SynapseMouse.App.Controls;

/// <summary>A press span on one timeline lane (EndMs = null while still held).</summary>
internal sealed class TimelineSpan
{
    public int Lane { get; init; }

    public bool IsOutput { get; init; }

    public double StartMs { get; init; }

    public double? EndMs { get; set; }
}

/// <summary>An instantaneous mark (wheel notch) on a lane.</summary>
internal readonly record struct TimelineMark(int Lane, bool IsOutput, double TimeMs, bool Positive);

/// <summary>
/// Scrolling event timeline for the Input Tester. Each lane shows the physical input (top, gray) and what
/// Windows actually received after Synapse (bottom, white), making debounce, remapping and
/// single→double behavior directly visible.
/// </summary>
internal sealed class InputTimeline : FrameworkElement
{
    private static readonly string[] LaneNames = { "Left", "Right", "Middle", "Side 4", "Side 5", "Wheel" };
    private static readonly Brush PhysicalBrush = Frozen(Color.FromRgb(0x6B, 0x71, 0x7C));
    private static readonly Brush OutputBrush = Brushes.White;
    private static readonly Brush GridBrush = Frozen(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0x8A, 0x90, 0x9B));
    private static readonly Pen GridPen = FrozenPen(GridBrush, 1);
    private static readonly Typeface LabelFace = new("Segoe UI");

    private IReadOnlyList<TimelineSpan> _spans = Array.Empty<TimelineSpan>();
    private IReadOnlyList<TimelineMark> _marks = Array.Empty<TimelineMark>();
    private double _nowMs;

    /// <summary>Visible history, in milliseconds.</summary>
    public double WindowMs { get; set; } = 3000;

    public void Update(IReadOnlyList<TimelineSpan> spans, IReadOnlyList<TimelineMark> marks, double nowMs)
    {
        _spans = spans;
        _marks = marks;
        _nowMs = nowMs;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (width < 100 || height < 60)
        {
            return;
        }

        const double labelWidth = 64;
        double plotWidth = width - labelWidth - 8;
        double laneHeight = height / LaneNames.Length;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int lane = 0; lane < LaneNames.Length; lane++)
        {
            double top = lane * laneHeight;
            dc.DrawLine(GridPen, new Point(labelWidth, top + laneHeight), new Point(width, top + laneHeight));
            var text = new FormattedText(LaneNames[lane], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, 11, LabelBrush, dpi);
            dc.DrawText(text, new Point(0, top + ((laneHeight - text.Height) / 2)));
        }

        // Vertical grid every 500 ms.
        double start = _nowMs - WindowMs;
        for (double t = Math.Ceiling(start / 500) * 500; t <= _nowMs; t += 500)
        {
            double x = labelWidth + ((t - start) / WindowMs * plotWidth);
            dc.DrawLine(GridPen, new Point(x, 0), new Point(x, height));
        }

        double X(double ms) => labelWidth + (Math.Clamp(ms - start, 0, WindowMs) / WindowMs * plotWidth);

        foreach (var span in _spans)
        {
            double end = span.EndMs ?? _nowMs;
            if (end < start)
            {
                continue;
            }

            double laneTop = span.Lane * laneHeight;
            double barHeight = Math.Max(3, (laneHeight - 12) / 2);
            double y = laneTop + 4 + (span.IsOutput ? barHeight + 4 : 0);
            double x1 = X(span.StartMs);
            double x2 = Math.Max(X(end), x1 + 2);
            dc.DrawRoundedRectangle(span.IsOutput ? OutputBrush : PhysicalBrush, null, new Rect(x1, y, x2 - x1, barHeight), 2, 2);
        }

        foreach (var mark in _marks)
        {
            if (mark.TimeMs < start)
            {
                continue;
            }

            double laneTop = mark.Lane * laneHeight;
            double barHeight = Math.Max(3, (laneHeight - 12) / 2);
            double y = laneTop + 4 + (mark.IsOutput ? barHeight + 4 : 0);
            double x = X(mark.TimeMs);
            var brush = mark.IsOutput ? OutputBrush : PhysicalBrush;
            dc.DrawRectangle(brush, null, new Rect(x - 1, y, 2.5, barHeight));
            double arrowY = mark.Positive ? y - 1 : y + barHeight + 1;
            dc.DrawEllipse(brush, null, new Point(x + 0.25, arrowY), 2, 2);
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}

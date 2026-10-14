using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// The term GPA's history, kept by the page day by day once tracking is on (the Mac's TrendCard chart): a smooth line
/// over its area in the accent, the goal as a dashed rule, faint rules at round GPAs along the right and the days along
/// the foot. The pointer over it reads out that day's GPA in the card's heading as the engine writes it ("Oct 12 ·
/// 3.43"), a rule and a dot marking the day.
/// </summary>
internal sealed class GradesTrendChart : Canvas
{
    private const double RightRoom = 34, FootRoom = 20, TopRoom = 6;
    private readonly List<(DateTime Day, double Gpa, string Label)> points;
    private readonly double low, high;
    private readonly TextBlock readout;
    private double goal;
    private int? hovered;

    public GradesTrendChart(List<(DateTime Day, double Gpa, string Label)> points, double low, double high, double goal, TextBlock readout)
    {
        this.points = points;
        this.low = low;
        this.high = high;
        this.goal = goal;
        this.readout = readout;
        Height = 150;
        Background = Brushes.Transparent;
        ClipToBounds = false;
        SizeChanged += (_, _) => Draw();
        MouseMove += (_, e) => Hover(e.GetPosition(this).X);
        MouseLeave += (_, _) => { if (hovered != null) { hovered = null; Draw(); Readout(); } };
        Readout();
    }

    /// <summary>The goal moved (the Goal card's buttons): its rule follows.</summary>
    public void SetGoal(double g)
    {
        goal = g;
        Draw();
        Readout();
    }

    private bool ShowsGoal => goal > 0 && goal >= low && goal <= high;

    private void Readout()
    {
        if (hovered is { } i && i < points.Count)
        {
            readout.Text = points[i].Label;
            return;
        }
        var span = $"{low.ToString("0.00", GradesKit.Inv)} – {high.ToString("0.00", GradesKit.Inv)}";
        readout.Text = ShowsGoal ? $"{span} · dashed is your goal" : span;
    }

    private double PlotWidth => Math.Max(0, ActualWidth - RightRoom);
    private double PlotHeight => Math.Max(0, ActualHeight - FootRoom - TopRoom);
    private double Span => Math.Max(1, (points[^1].Day - points[0].Day).TotalDays);
    private double X(DateTime d) => (d - points[0].Day).TotalDays / Span * PlotWidth;
    private double Y(double v) => TopRoom + (1 - (Math.Clamp(v, low, high) - low) / (high - low)) * PlotHeight;

    private void Hover(double x)
    {
        if (points.Count == 0 || PlotWidth <= 0) return;
        var near = 0;
        for (var i = 1; i < points.Count; i++)
            if (Math.Abs(X(points[i].Day) - x) < Math.Abs(X(points[near].Day) - x)) near = i;
        if (near == hovered) return;
        hovered = near;
        Draw();
        Readout();
    }

    private void Draw()
    {
        Children.Clear();
        var pw = PlotWidth;
        var ph = PlotHeight;
        if (points.Count < 2 || pw < 20 || ph < 20 || high <= low) return;

        // round GPAs along the right, a faint rule at each
        foreach (var t in Ticks())
        {
            var y = Y(t);
            Children.Add(new Line { X1 = 0, X2 = pw, Y1 = y, Y2 = y, SnapsToDevicePixels = true }.Stroke(B.Divider, 1));
            var label = K.Text(t.ToString("0.0#", GradesKit.Inv), Ty.Caption, B.Text3);
            SetLeft(label, pw + 6);
            SetTop(label, y - 8);
            Children.Add(label);
        }

        // the days along the foot
        var count = Math.Min(4, points.Count);
        for (var k = 0; k < count; k++)
        {
            var day = points[0].Day.AddDays(Span * k / Math.Max(1, count - 1)).Date;
            var label = K.Text(day.ToString(day.Year == DateTime.Today.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture), Ty.Caption, B.Text3);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var lw = label.DesiredSize.Width;
            SetLeft(label, Math.Clamp(X(day) - lw / 2, 0, Math.Max(0, pw - lw)));
            SetTop(label, TopRoom + ph + 4);
            Children.Add(label);
        }

        var at = points.Select(p => new Point(X(p.Day), Y(p.Gpa))).ToList();
        var accent = Appearance.Accent;
        var area = new Path
        {
            Data = Curve(at, TopRoom + ph),
            Fill = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, TopRoom), EndPoint = new Point(0, TopRoom + ph),
                GradientStops = { new GradientStop(K.WithAlpha(accent, 0.25), 0), new GradientStop(K.WithAlpha(accent, 0), 1) },
            },
        };
        Children.Add(area);
        var line = new Path { Data = Curve(at, null), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        line.Stroke(B.Accent, 2.2);
        Children.Add(line);

        if (ShowsGoal)
        {
            var y = Y(goal);
            Children.Add(new Line { X1 = 0, X2 = pw, Y1 = y, Y2 = y, StrokeDashArray = new DoubleCollection { 4, 4 } }.Stroke(B.Text2, 1));
        }

        if (hovered is { } h && h < at.Count)
        {
            var p = at[h];
            Children.Add(new Line { X1 = p.X, X2 = p.X, Y1 = TopRoom, Y2 = TopRoom + ph, Opacity = 0.5 }.Stroke(B.Text3, 1));
            var dot = new Ellipse { Width = 10, Height = 10 }.Fg(B.Accent);
            SetLeft(dot, p.X - 5);
            SetTop(dot, p.Y - 5);
            Children.Add(dot);
        }
    }

    /// <summary>Round GPAs inside the range, no more than five.</summary>
    private IEnumerable<double> Ticks()
    {
        var step = new[] { 0.1, 0.2, 0.25, 0.5, 1, 2 }.FirstOrDefault(s => (high - low) / s <= 5, 2);
        for (var t = Math.Ceiling(low / step - 1e-9) * step; t <= high + 1e-9; t += step) yield return Math.Round(t, 2);
    }

    /// <summary>
    /// The points joined by a monotone cubic curve (as the Mac's chart and d3's monotoneX draw it: it never overshoots a
    /// day's value); closed down to <paramref name="baseY"/> for the area under it.
    /// </summary>
    private static Geometry Curve(List<Point> p, double? baseY)
    {
        var fig = new PathFigure { StartPoint = p[0], IsClosed = baseY != null, IsFilled = baseY != null };
        var n = p.Count;
        if (n == 2) fig.Segments.Add(new LineSegment(p[1], true));
        else
        {
            var m = new double[n];
            for (var i = 1; i < n - 1; i++) m[i] = Slope3(p[i - 1], p[i], p[i + 1]);
            m[0] = Slope2(p[0], p[1], m[1]);
            m[n - 1] = Slope2(p[n - 2], p[n - 1], m[n - 2]);
            for (var i = 0; i < n - 1; i++)
            {
                var dx = (p[i + 1].X - p[i].X) / 3;
                fig.Segments.Add(new BezierSegment(
                    new Point(p[i].X + dx, p[i].Y + dx * m[i]),
                    new Point(p[i + 1].X - dx, p[i + 1].Y - dx * m[i + 1]),
                    p[i + 1], true));
            }
        }
        if (baseY is { } b)
        {
            fig.Segments.Add(new LineSegment(new Point(p[^1].X, b), false));
            fig.Segments.Add(new LineSegment(new Point(p[0].X, b), false));
        }
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }

    private static double Slope3(Point a, Point b, Point c)
    {
        var h0 = b.X - a.X;
        var h1 = c.X - b.X;
        var s0 = h0 != 0 ? (b.Y - a.Y) / h0 : 0;
        var s1 = h1 != 0 ? (c.Y - b.Y) / h1 : 0;
        var p = h0 + h1 != 0 ? (s0 * h1 + s1 * h0) / (h0 + h1) : 0;
        var r = (Math.Sign(s0) + Math.Sign(s1)) * Math.Min(Math.Min(Math.Abs(s0), Math.Abs(s1)), 0.5 * Math.Abs(p));
        return double.IsFinite(r) ? r : 0;
    }

    private static double Slope2(Point a, Point b, double t)
    {
        var h = b.X - a.X;
        return h != 0 ? (3 * (b.Y - a.Y) / h - t) / 2 : t;
    }
}

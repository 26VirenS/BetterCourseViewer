using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Models;

namespace Simpl.Screens;

/// <summary>
/// How the class did on a piece of work (the Mac's ClassBoxPlot, as the web's course-detail classPlot draws it): a box
/// plot on a track from nothing to the points possible — the whiskers from the low to the high, the box from the lower to
/// the upper quartile, a line at the median, a ring at the mean — and the student's own score as a dot in the course's
/// colour. The pointer over it reads the mark nearest to it: its name and value in a small label over it, the mark itself
/// a little larger, the others fading back. For an assignment's grade: <c>new ClassBoxPlot(stats, K.Hex(course.Color))</c>.
/// </summary>
public sealed class ClassBoxPlot : Grid
{
    private readonly ClassStats stats;
    private readonly Color tint;
    private readonly Canvas plot = new() { Height = 24, Background = Brushes.Transparent, ClipToBounds = false };
    private readonly List<(string Name, double Value, string Key)> marks = new();
    private int? lit;

    public ClassBoxPlot(ClassStats stats, Color tint)
    {
        this.stats = stats;
        this.tint = tint;
        // every mark there is, the student's own first, so one at the same place as another is read as theirs
        var all = new (string, double?, string)[]
        {
            ("You", stats.Mine, "me"), ("Low", stats.Min ?? 0, "lo"), ("Lower quartile", stats.LowerQ, "q1"), ("Median", stats.Median, "md"),
            ("Upper quartile", stats.UpperQ, "q3"), ("High", stats.Max, "hi"), ("Mean", stats.Mean, "mean"),
        };
        foreach (var (name, v, key) in all) if (v is { } x) marks.Add((name, x, key));

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = K.Text("Class", Ty.Caption, B.Text2);
        label.FontWeight = FontWeights.Medium;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 10, 0);
        Children.Add(label.Col(0));
        Children.Add(plot.Col(1));
        Margin = new Thickness(0, 6, 0, 0);
        plot.SizeChanged += (_, _) => Draw();
        plot.MouseMove += (_, e) =>
        {
            var near = Nearest(e.GetPosition(plot).X);
            if (near != lit) { lit = near; Draw(); }
        };
        plot.MouseLeave += (_, _) => { if (lit != null) { lit = null; Draw(); } };
        AutomationProperties.SetName(this, $"How the class did, out of {Num(OutOf)}: " + string.Join(", ", marks.Select(m => $"{m.Name} {Num(m.Value)}")));
    }

    /// <summary>Out of what: the points possible, or the largest number there is.</summary>
    private double OutOf => stats.Possible is > 0 ? stats.Possible.Value : Math.Max(Math.Max(stats.Max ?? 0, stats.Mine ?? 0), 1);

    private double X(double v) => Math.Clamp(v / OutOf, 0, 1) * plot.ActualWidth;

    private int? Nearest(double x)
    {
        int? best = null;
        var gap = double.MaxValue;
        for (var i = 0; i < marks.Count; i++)
        {
            var d = Math.Abs(X(marks[i].Value) - x);
            if (d < gap) { gap = d; best = i; }
        }
        return best;
    }

    private void Draw()
    {
        plot.Children.Clear();
        var w = plot.ActualWidth;
        if (w <= 0) return;
        var litKey = lit is { } li && li < marks.Count ? marks[li].Key : null;
        double Dim(string key) => litKey == null || litKey == key || (key == "box" && litKey is "q1" or "q3") ? 1 : 0.45;
        double Grow(string key) => litKey == key ? 1.35 : 1;
        const double mid = 12;
        var lo = stats.Min ?? 0;
        var hi = stats.Max ?? lo;

        // the track, nothing to the points possible
        Put(new Rectangle { Width = w, Height = 2, RadiusX = 1, RadiusY = 1, Opacity = 0.08 }.Fg(B.Text), 0, mid - 1);
        // the whiskers, low to high, with their caps
        Put(new Rectangle { Width = Math.Max(1, X(hi) - X(lo)), Height = 2, Opacity = 0.55 }.Fg(B.Text2), X(lo), mid - 1);
        Cap(X(lo), "lo");
        Cap(X(hi), "hi");
        // the box, lower to upper quartile
        if (stats.LowerQ is { } q1 && stats.UpperQ is { } q3)
        {
            var bw = Math.Max(3, X(q3) - X(q1));
            var dim = Dim("box");
            Put(new Rectangle { Width = bw, Height = 14, RadiusX = 4, RadiusY = 4, Opacity = 0.08 * dim }.Fg(B.Text), X(q1), mid - 7);
            Put(new Rectangle { Width = bw, Height = 14, RadiusX = 4, RadiusY = 4, Opacity = 0.7 * dim }.Stroke(litKey is "q1" or "q3" ? B.Text : B.Text2, 1.5), X(q1), mid - 7);
        }
        if (stats.Median is { } md)
        {
            var g = Grow("md");
            Put(new Rectangle { Width = 2 * g, Height = 14 * g, RadiusX = 1, RadiusY = 1, Opacity = 0.75 * Dim("md") }.Fg(B.Text), X(md) - g, mid - 7 * g);
        }
        if (stats.Mean is { } mean)
        {
            var s = 7 * Grow("mean");
            Put(new Ellipse { Width = s, Height = s, Opacity = Dim("mean") }.Fg(B.Solid).Stroke(B.Text, 1.5), X(mean) - s / 2, mid - s / 2);
        }
        if (stats.Mine is { } me)
        {
            var s = 12 * Grow("me");
            Put(new Ellipse { Width = s, Height = s, Fill = new SolidColorBrush(tint) }.Stroke(B.Solid, 2), X(me) - s / 2, mid - s / 2);
        }
        if (lit is { } i && i < marks.Count) Tip(marks[i], w);

        void Cap(double x, string key)
        {
            var g = Grow(key);
            Put(new Rectangle { Width = 2 * g, Height = 10 * g, RadiusX = 1, RadiusY = 1, Opacity = Dim(key) * 0.8 }.Fg(B.Text2), x - g, mid - 5 * g);
        }
    }

    /// <summary>The mark pointed at, said in a small label over it (Windows 11's tooltip surface), kept inside the plot.</summary>
    private void Tip((string Name, double Value, string Key) m, double w)
    {
        var name = K.Text(m.Name, Ty.Caption, B.Text2);
        name.FontWeight = FontWeights.Medium;
        name.VerticalAlignment = VerticalAlignment.Bottom;
        var value = K.Text(Num(m.Value), Ty.BodyStrong);
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        var tip = new Border
        {
            Child = K.H(5, name, value), Padding = new Thickness(9, 4, 9, 5), CornerRadius = new CornerRadius(6),
            Effect = K.Shadow(12, 2, 0.22), IsHitTestVisible = false,
        };
        tip.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = tip.DesiredSize;
        var cx = Math.Min(Math.Max(X(m.Value), 34), Math.Max(34, w - 34));
        Panel.SetZIndex(tip, 10);
        Put(tip, cx - size.Width / 2, -12 - size.Height / 2);
    }

    private void Put(FrameworkElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        plot.Children.Add(e);
    }

    private static string Num(double v)
    {
        if (!double.IsFinite(v)) return "—";
        if (v == Math.Round(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(GradesKit.Inv);
        return (Math.Round(v * 10) / 10).ToString("G6", GradesKit.Inv);
    }
}

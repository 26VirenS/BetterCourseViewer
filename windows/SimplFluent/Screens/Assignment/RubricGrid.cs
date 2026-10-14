using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// The rubric as a grid (the Mac's RubricGridPanel): what it is and for which assignment, its score and how many criteria,
/// Before grading or Graded once it is marked (a segmented control, unlike the Ring | Grid tabs above it), then the grid —
/// a criterion a row, its levels lined up under columns by what they are worth, the one given marked — as tall as its
/// rows, scrolling only when it is taller than the room.
/// </summary>
internal sealed class RubricGridPanel : Grid
{
    private readonly RubricModel model;
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock score, outOf, note;
    private bool marks;

    public RubricGridPanel(RubricModel model, string eyebrow, string title)
    {
        this.model = model;
        marks = model.Graded;
        Background = Brushes.Transparent;
        MouseLeftButtonUp += (_, e) => e.Handled = true; // (a press on the grid is not a press beside it)
        System.Windows.Automation.AutomationProperties.SetName(this, $"{title} rubric as a grid");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // the head: what it is, its score, how many criteria, and Before grading / Graded
        var names = K.V(5, K.Text(eyebrow, Ty.Caption, B.Text2), K.Wrapped(title, Ty.Subtitle, B.Text, 2));
        names.VerticalAlignment = VerticalAlignment.Bottom;
        score = new TextBlock { FontSize = 30, FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace, VerticalAlignment = VerticalAlignment.Bottom };
        score.Fg(B.Text);
        outOf = K.Text("", Ty.BodyStrong, B.Text2).Margin(4, 0, 0, 5);
        outOf.VerticalAlignment = VerticalAlignment.Bottom;
        var head = K.Columns("*, auto, auto, auto",
            names.Margin(0, 0, 18, 0).Col(0),
            K.H(0, score, outOf).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).Col(1),
            K.Text(model.Many, Ty.Body, B.Text2).Margin(18, 0, 0, 5).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).Col(2));
        if (model.Graded)
        {
            var shown = Segmented(new[] { "Before grading", "Graded" }, 1, i => { marks = i == 1; Fill(); });
            shown.VerticalAlignment = VerticalAlignment.Bottom;
            shown.Margin = new Thickness(18, 0, 0, 0);
            shown.ToolTip = "The rubric as it read before grading, or with your marks";
            head.Children.Add(shown.Col(3));
        }
        head.Margin = new Thickness(0, 0, 0, 16);
        Children.Add(head.Row(0));
        Children.Add(scroll.Row(1));
        note = K.Text("", Ty.Caption, B.Text2).Margin(0, 12, 0, 0);
        Children.Add(note.Row(2));
        Fill();
    }

    private void Fill()
    {
        var showMarks = marks && model.Graded;
        score.Text = RubricRing.Num(showMarks ? model.Earned : model.Possible);
        outOf.Text = showMarks ? $"/ {RubricRing.Num(model.Possible)}" : model.Possible == 1 ? "pt" : "pts";
        note.Text = showMarks ? "Your mark is outlined on each row." : "How each criterion will be graded.";
        scroll.Content = Table(showMarks);
    }

    /// <summary>The same rubric as rows and columns: a criterion a row, its levels lined up under the columns by what they
    /// are worth (the best on the left, nothing on the right), the one given marked.</summary>
    private FrameworkElement Table(bool showMarks)
    {
        var plan = new GridPlan(model);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        for (var j = 0; j < plan.Cols; j++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 90 });
        }
        var span = 1 + 2 * plan.Cols;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(K.Text("Criterion", Ty.Caption, B.Text2).Strong().Margin(0, 0, 0, 10).Row(0).Col(0));
        for (var j = 0; j < plan.Cols; j++)
        {
            var h = plan.Heads[j];
            var cell = K.H(6, new Ellipse { Width = 8, Height = 8, Fill = K.Brush(h.Tone), VerticalAlignment = VerticalAlignment.Center }, K.Text(h.Name, Ty.Caption, B.Text2).Strong());
            g.Children.Add(cell.Margin(0, 0, 0, 10).Row(0).Col(2 + 2 * j));
        }
        var row = 1;
        foreach (var c in model.Criteria)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(K.Divider().Row(row).Col(0, span));
            row++;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(CriterionCell(c, showMarks).Row(row).Col(0));
            for (var j = 0; j < plan.Cols; j++)
            {
                var i = Array.IndexOf(plan.Slots[c.Index], j);
                if (i >= 0) g.Children.Add(LevelCell(c, i, plan.Heads[j], showMarks).Row(row).Col(2 + 2 * j));
            }
            row++;
        }
        return g;
    }

    private FrameworkElement CriterionCell(RubricModel.Criterion c, bool showMarks)
    {
        var scored = showMarks && c.Score != null;
        var colour = scored ? model.Grades[c.Index] : model.Hues[c.Index];
        var name = K.Wrapped(c.Name, Ty.BodyStrong);
        var top = K.Columns("auto, *", new Ellipse { Width = 9, Height = 9, Fill = K.Brush(colour.Color), Margin = new Thickness(0, 6, 8, 0), VerticalAlignment = VerticalAlignment.Top }.Col(0), name.Col(1));
        var num = new TextBlock { Text = RubricRing.Num(scored ? c.Score ?? 0 : c.Worth), FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = K.DisplayFace };
        num.Fg(B.Text);
        var unit = K.Text(scored ? $"/ {RubricRing.Num(c.Worth)}" : c.Worth == 1 ? "pt" : "pts", Ty.Body, B.Text2).Margin(3, 0, 0, 2);
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        var v = K.V(5, top, K.H(0, num, unit));
        if (showMarks && c.Comment.Length > 0)
        {
            var comment = K.Columns("auto, *", K.Icon(SymbolRegular.Comment24, 14, B.Text2).Margin(0, 2, 6, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0),
                K.Wrapped(c.Comment, Ty.Body, B.Text2, 3).Col(1));
            comment.ToolTip = c.Comment;
            v.Children.Add(comment);
        }
        var track = K.Bar(scored ? c.Frac : 1, colour.Color, 4);
        track.MaxWidth = 160;
        track.HorizontalAlignment = HorizontalAlignment.Left;
        track.Width = 160;
        v.Children.Add(track.Margin(0, 3, 0, 0));
        v.Margin = new Thickness(0, 12, 0, 12);
        System.Windows.Automation.AutomationProperties.SetName(v, model.Spoken(c));
        return v;
    }

    private FrameworkElement LevelCell(RubricModel.Criterion c, int i, GridPlan.Head head, bool showMarks)
    {
        var l = c.Levels[i];
        var mark = showMarks && c.Mark == i;
        var dim = showMarks && c.Score != null && !mark;
        var main = head.Own ? l.Text : l.Label;
        var sub = head.Own ? "" : l.Text;
        var colour = model.Grades[c.Index];
        var pts = K.Text(l.Pts is { } p ? RubricRing.Num(p) : "–", Ty.BodyStrong);
        if (mark) pts.Foreground = K.Brush(K.Darkened(colour.Color));
        var v = K.V(4, K.H(3, pts, K.Text(l.Pts == 1 ? "pt" : "pts", Ty.Caption, B.Text2).Margin(0, 0, 0, 1).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom)));
        if (mark) v.Children.Add(RubricView.YourMark(colour, c.MarkText));
        if (main.Length > 0)
        {
            var m = K.Wrapped(main, Ty.Body);
            if (mark) m.FontWeight = FontWeights.SemiBold;
            v.Children.Add(m);
        }
        if (sub.Length > 0) v.Children.Add(K.Wrapped(sub, Ty.Caption, B.Text2));
        var cell = new Grid { Margin = new Thickness(0, 12, 0, 12), Opacity = dim ? 0.45 : 1 };
        if (mark)
        {
            cell.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = K.Brush(colour.Color), HorizontalAlignment = HorizontalAlignment.Left });
            v.Margin = new Thickness(10, 0, 0, 0);
            System.Windows.Automation.AutomationProperties.SetItemStatus(cell, c.MarkText);
        }
        cell.Children.Add(v);
        cell.ToolTip = string.Join(" — ", new[] { l.Label, l.Text }.Where(s => s.Length > 0));
        return cell;
    }

    /// <summary>
    /// A segmented control (Windows 11's, as the Photos app's editing modes): its choices side by side in one rounded
    /// box, the one chosen raised on a control's fill with its own edge, in the text's colour and heavier — a choice of how
    /// to show the same thing, which the Ring | Grid tabs over it are not.
    /// </summary>
    private static Border Segmented(IReadOnlyList<string> choices, int selected, Action<int> changed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var box = new Border { Child = row, CornerRadius = new CornerRadius(6), Padding = new Thickness(2), BorderThickness = new Thickness(1) };
        box.Bg(B.Subtle3).Stroke(B.ControlStroke);
        var items = new List<(System.Windows.Controls.Button B, TextBlock T)>();
        void Paint()
        {
            for (var i = 0; i < items.Count; i++)
            {
                var on = i == selected;
                var (b, t) = items[i];
                if (on) { b.SetResourceReference(Control.BackgroundProperty, B.Control); b.SetResourceReference(Control.BorderBrushProperty, B.ControlStroke); }
                else { b.Background = Brushes.Transparent; b.BorderBrush = Brushes.Transparent; }
                t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                t.Fg(on ? B.Text : B.Text2);
                System.Windows.Automation.AutomationProperties.SetItemStatus(b, on ? "Selected" : "");
            }
        }
        for (var i = 0; i < choices.Count; i++)
        {
            var n = i;
            var t = K.Text(choices[i], Ty.Body);
            var b = K.Subtle(t, () =>
            {
                if (n == selected) return;
                selected = n;
                Paint();
                changed(n);
            }, new Thickness(12, 4, 12, 5), 4);
            b.BorderThickness = new Thickness(1);
            System.Windows.Automation.AutomationProperties.SetName(b, choices[i]);
            items.Add((b, t));
            row.Children.Add(b);
        }
        Paint();
        return box;
    }

    /// <summary>The grid's columns: how many, which column each criterion's levels go in (by what each is worth out of its
    /// best; nothing is the last column), and each column's name and colour (the levels' own name where every criterion
    /// calls that level the same, else by rank).</summary>
    private sealed class GridPlan
    {
        public sealed record Head(string Name, bool Own, Color Tone);

        public int Cols { get; }
        public int[][] Slots { get; }
        public Head[] Heads { get; }

        public GridPlan(RubricModel m)
        {
            var cols = Math.Max(1, m.Criteria.Count == 0 ? 1 : m.Criteria.Max(c => c.Levels.Count));
            Cols = cols;
            Slots = m.Criteria.Select(c =>
            {
                var levels = c.Levels;
                var k = levels.Count;
                if (k == 0) return Array.Empty<int>();
                if (k == cols) return Enumerable.Range(0, k).ToArray();
                var vals = levels.Where(l => l.Pts != null).Select(l => l.Pts!.Value).ToList();
                var best = vals.Count > 0 ? vals.Max() : 0;
                var lo = Math.Min(0, vals.Count > 0 ? vals.Min() : 0);
                var want = levels.Select((l, i) =>
                {
                    if (l.Pts is { } p && best - lo > 1e-9) return (int)Math.Round((1 - (p - lo) / (best - lo)) * (cols - 1), MidpointRounding.AwayFromZero);
                    return (int)Math.Round((double)(i * (cols - 1)) / Math.Max(1, k - 1), MidpointRounding.AwayFromZero);
                }).ToArray();
                for (var i = 1; i < k; i++) want[i] = Math.Max(want[i], want[i - 1] + 1);
                for (var i = k - 1; i >= 0; i--) want[i] = Math.Min(want[i], cols - 1 - (k - 1 - i));
                return want;
            }).ToArray();
            List<RubricModel.Level> InColumn(int j)
            {
                var o = new List<RubricModel.Level>();
                for (var k = 0; k < m.Criteria.Count; k++)
                {
                    var at = Array.IndexOf(Slots[k], j);
                    if (at >= 0) o.Add(m.Criteria[k].Levels[at]);
                }
                return o;
            }
            var lastNone = InColumn(cols - 1).All(l => (l.Pts ?? 0) == 0);
            string[]? ladder = cols switch
            {
                1 => new[] { "Full marks" },
                2 => new[] { "Excellent" },
                3 => new[] { "Excellent", "Developing" },
                4 => new[] { "Excellent", "Good", "Developing" },
                5 => new[] { "Excellent", "Good", "Fair", "Developing" },
                _ => null,
            };
            Heads = Enumerable.Range(0, cols).Select(j =>
            {
                var names = InColumn(j).Select(l => l.Label.Trim()).ToList();
                string? shared = null;
                if (names.Count > 0 && names[0].Length <= 22 && names.All(n => n.Equals(names[0], StringComparison.OrdinalIgnoreCase))) shared = names[0];
                string name;
                if (shared != null) name = shared;
                else if (ladder != null) name = j == cols - 1 && cols > 1 ? (lastNone ? "Missing" : "Beginning") : ladder[Math.Min(j, ladder.Length - 1)];
                else name = $"Level {j + 1}";
                var t = cols > 1 ? (double)j / (cols - 1) : 0;
                string tone;
                if (j == 0) tone = "#30d158";
                else if (j == cols - 1) tone = lastNone ? "#8e8e93" : "#ff6b3d";
                else if (t <= 0.34) tone = "#0a84ff";
                else if (t < 0.55 && cols >= 5) tone = "#ffd60a";
                else tone = "#ff9f0a";
                return new Head(name, shared != null, RubricRGB.Hex(tone).Color);
            }).ToArray();
        }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

public sealed partial class TodoScreen
{
    private static readonly string[] GroupKeys = { "date", "priority", "course" };
    private static readonly string[] GroupNames = { "Date", "Priority", "Course" };
    private static readonly Color Green = K.Tone("green");
    /// <summary>The grouping's SelectorBar, made once and moved from build to build.</summary>
    private SelectorBar? groupBar;

    // ---- the progress and the grouping ----

    /// <summary>A narrow page: how much of the week is done and how it is grouped, side by side when they fit, one over the
    /// other when not — in one card over the list.</summary>
    private FrameworkElement Summary(TodoData d, double width)
    {
        FrameworkElement content = SideBySide(width)
            ? K.Columns("auto, *, auto", Progress(d).Col(0), Controls(d, right: true).Col(2))
            : K.V(14, Progress(d), Controls(d, right: false));
        return K.Card(content, new Thickness(18));
    }

    /// <summary>
    /// How much is done, as the Mac has it: the ring (its track the ring's own green, faint — the theme's grey all but
    /// vanished on a light card), and beside it what the student opens To Do to see — what is left ("17 left"), or, when
    /// there is any, what is late, in red ("1 overdue") — with how many of how many are done under it. The share stays
    /// the ring's alone: a percentage says little early in the week.
    /// </summary>
    private static FrameworkElement Progress(TodoData d)
    {
        var ring = K.Ring(d.Pct / 100.0, Green, 64, 8, tinted: true);
        var left = d.Left ?? Math.Max(0, d.Total - d.Done);
        var overdue = TodoWords.Text(d.OverdueText);
        var big = K.Text(overdue ?? TodoWords.Text(d.LeftText) ?? $"{left} left", Ty.Count);
        if (overdue != null) big.Foreground = K.Brush(K.Darkened(K.Tone("red")));
        var doneLine = TodoWords.Text(d.DoneText) ?? $"{d.Done} of {d.Total} done";
        var done = K.Text(doneLine, Ty.Body, B.Text2);
        TodoWords.Tabular(big);
        TodoWords.Tabular(done);
        var words = K.V(2, big, done);
        words.VerticalAlignment = VerticalAlignment.Center;
        var row = K.H(16, ring, words);
        AutomationProperties.SetName(row, overdue != null ? $"{overdue}, {TodoWords.Text(d.LeftText) ?? $"{left} left"}, {doneLine}" : $"{TodoWords.Text(d.LeftText) ?? $"{left} left"}, {doneLine}");
        return row;
    }

    /// <summary>The grouping (Date · Priority · Course, Windows 11's SelectorBar) over Show completed — a button, as the
    /// Dashboard's List has it.</summary>
    private FrameworkElement Controls(TodoData d, bool right)
    {
        var group = pendingGroup ?? (string.IsNullOrEmpty(d.Group) ? "date" : d.Group);
        var index = Math.Max(0, Array.IndexOf(GroupKeys, group));
        if (groupBar == null)
        {
            groupBar = new SelectorBar(GroupNames, index);
            groupBar.Changed += i => Regroup(group: GroupKeys[i]);
            AutomationProperties.SetName(groupBar, "Group by");
        }
        else
        {
            Adopt(groupBar);
            groupBar.Select(index, false);
        }
        var showing = pendingShowDone ?? d.ShowDone;
        var toggle = K.Button(showing ? "Hide completed" : $"Show completed · {d.Done}", () => Regroup(showDone: !showing),
            icon: showing ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.Eye24);
        toggle.ToolTip = showing ? "Hide completed work" : "Show completed work too";
        var align = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        groupBar.HorizontalAlignment = align;
        toggle.HorizontalAlignment = align;
        var v = K.V(10, groupBar, toggle);
        v.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        v.VerticalAlignment = VerticalAlignment.Center;
        return v;
    }

    // ---- the side column ----

    /// <summary>The week at a glance beside the list (the Mac's overview): the progress, the grouping, what is left day by
    /// day, by course and by priority — one card, its parts between hairlines. New task is the title's alone, at any
    /// width: one copy of it on the page.</summary>
    private FrameworkElement Overview(TodoData d)
    {
        var open = d.Sections.SelectMany(s => s.Rows).Where(r => !r.Done).ToList();
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 16 };
        v.Children.Add(Progress(d));
        v.Children.Add(Controls(d, right: false));
        if (open.Count > 0)
        {
            v.Children.Add(K.Divider());
            v.Children.Add(WeekLoad(open));
            v.Children.Add(K.Divider());
            v.Children.Add(CourseLoad(open));
            if (PriorityLoad(open) is { } levels)
            {
                v.Children.Add(K.Divider());
                v.Children.Add(levels);
            }
        }
        return K.Card(v, new Thickness(20));
    }

    /// <summary>A part's heading on the side card, and what is at its end.</summary>
    private static FrameworkElement CardHeading(string text, string? trailing = null)
    {
        var g = K.Columns("*, auto", K.Text(text, Ty.BodyStrong).Col(0));
        if (trailing != null) g.Children.Add(K.Text(trailing, Ty.Caption, B.Text2).Margin(8, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        return g;
    }

    /// <summary>What is left on each of the next seven days, as bars (today's in the accent), and how much is overdue.</summary>
    private static FrameworkElement WeekLoad(List<WorkRow> open)
    {
        var today = DateTime.Today;
        var now = DateTime.Now;
        var perDay = new int[7];
        var overdue = 0;
        foreach (var r in open)
        {
            if (TodoWords.Date(r.Date) is not { } date) continue;
            if (date < now)
            {
                overdue++;
                continue;
            }
            var k = (int)Math.Round((date.Date - today).TotalDays);
            if (k >= 0 && k < 7) perDay[k]++;
        }
        var most = Math.Max(perDay.Max(), 1);
        var bars = new Grid { Height = 92 };
        for (var k = 0; k < 7; k++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var day = today.AddDays(k);
            var count = K.Text(perDay[k] > 0 ? perDay[k].ToString(CultureInfo.InvariantCulture) : "", Ty.Caption, B.Text2).Strong().Center();
            var bar = new Border { Height = Math.Max(4, (double)perDay[k] / most * 46), CornerRadius = new CornerRadius(3) }.Bg(B.Accent);
            if (k > 0) bar.Opacity = 0.35;
            var shortest = CultureInfo.InvariantCulture.DateTimeFormat.GetShortestDayName(day.DayOfWeek);
            var letter = K.Text(shortest.Length > 0 ? shortest[..1] : "", Ty.Caption, k == 0 ? B.Text : B.Text2).Center();
            if (k == 0) letter.FontWeight = FontWeights.Bold;
            var col = K.V(4, count, bar, letter);
            col.VerticalAlignment = VerticalAlignment.Bottom;
            col.Margin = new Thickness(4, 0, 4, 0);
            col.Background = Brushes.Transparent;
            var spoken = $"{day.ToString("dddd", CultureInfo.InvariantCulture)}, {perDay[k]} to do";
            col.ToolTip = spoken;
            AutomationProperties.SetName(col, spoken);
            bars.Children.Add(col.Col(k));
        }
        return K.V(10, CardHeading("This week", overdue > 0 ? $"{overdue} overdue" : null), bars);
    }

    /// <summary>What is left in each course (your own tasks as theirs), the busiest first.</summary>
    private static FrameworkElement CourseLoad(List<WorkRow> open)
    {
        var names = new List<string>();
        var colors = new Dictionary<string, string?>();
        var counts = new Dictionary<string, int>();
        foreach (var r in open)
        {
            var name = TodoWords.Text(r.CourseName) ?? TodoWords.Text(r.Course) ?? "My task";
            if (counts.TryGetValue(name, out var n)) { counts[name] = n + 1; continue; }
            names.Add(name);
            colors[name] = r.Color;
            counts[name] = 1;
        }
        var most = Math.Max(counts.Values.DefaultIfEmpty(0).Max(), 1);
        var v = K.V(9, CardHeading("By course"));
        foreach (var name in names.OrderByDescending(n => counts[n]))
        {
            var color = K.Hex(colors[name]);
            var count = K.Text(counts[name].ToString(CultureInfo.InvariantCulture), Ty.BodyStrong);
            TodoWords.Tabular(count);
            var top = K.Columns("auto, *, auto", K.Dot(color, 9).Col(0), K.Text(name, Ty.Body).Margin(8, 0, 6, 0).Col(1), count.Col(2));
            var line = K.V(4, top, K.Bar((double)counts[name] / most, color, 5));
            AutomationProperties.SetName(line, $"{name}: {counts[name]} to do");
            v.Children.Add(line);
        }
        return v;
    }

    /// <summary>How much is left at each priority (none when nothing is prioritised).</summary>
    private static FrameworkElement? PriorityLoad(List<WorkRow> open)
    {
        var levels = TodoPriority.All.Where(p => p.Id > 0)
            .Select(p => (Level: p, Count: open.Count(r => (r.Pri ?? 0) == p.Id)))
            .Where(x => x.Count > 0).ToList();
        if (levels.Count == 0) return null;
        var pills = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (level, count) in levels)
        {
            var pill = Pill($"{level.Short} {count}", level.Color, SymbolRegular.Flag24);
            pill.Margin = new Thickness(0, 0, 8, 4);
            AutomationProperties.SetName(pill, $"{level.Label} priority: {count}");
            pills.Children.Add(pill);
        }
        return K.V(9, CardHeading("By priority"), pills);
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// Grades (the Mac's GradesView): the term GPA, your goal for it and how it has moved, then every course as rings inside
/// rings — its total outermost, its assignment groups inside — with each group's weight and score as a bar, and every
/// graded assignment by its letter. On a wide page the courses are a list with the one picked beside it: all its groups,
/// its latest grades and what is still to be graded; on a narrow one they are cards. A course's own grades, where scores
/// can be tried, are a click away.
/// </summary>
public sealed class GradesScreen : Screen
{
    private const double WideAt = 880, SplitAt = 900;
    private static readonly string[] Letters = { "A", "B", "C", "D", "F" };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly Loader<GradesData> data;
    private readonly Loader<CourseGradesData> detail;
    private readonly DispatcherTimer goalSave = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private double goal = 4;
    private double? goalSeen;
    private bool goalTouched;
    /// <summary>The course shown beside the list (the first until another is picked).</summary>
    private string? picked;
    /// <summary>Item Grades' letter: 0 for all, then A to F.</summary>
    private int band;
    private bool showAll;
    private FrameworkElement? commands;
    private string? lastSub;

    // what a change of the goal moves, in place (the buttons pressed keep the keyboard)
    private TextBlock? goalText;
    private ContentControl? diffHolder, gpaBarHolder;
    private Button? lower, raise;
    private GradesTrendChart? trend;

    public GradesScreen(Engine.Engine engine) : base(engine)
    {
        data = Watch(new Loader<GradesData>());
        detail = Watch(new Loader<CourseGradesData>());
        data.Changed += Arrived;
        // the goal, saved half a second after the last change (a run of presses is one save), half up to two places
        goalSave.Tick += (_, _) =>
        {
            goalSave.Stop();
            _ = this.engine.Act("setGoal", new { goal = (double)GradesKit.Half(goal, 2) });
        };
    }

    public override string Title => "Grades";

    /// <summary>The term, how many courses and how many graded items (the Mac's heading line).</summary>
    public override string? Subtitle => data.Data is { } d ? Headline(d) : null;

    /// <summary>More: Open in Canvas, Copy Link (the Mac's toolbar CanvasMenu).</summary>
    public override FrameworkElement? Commands => commands ??= GradesKit.More(engine, () => engine.SchoolUrl(Place.Grades)?.ToString(), () => "Grades");

    public override void Refresh()
    {
        data.Ensure(engine, "grades");
        EnsureDetail();
    }

    private static string Headline(GradesData d)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(d.Term)) parts.Add(d.Term!);
        parts.Add(d.Rows.Count == 1 ? "1 course" : $"{d.Rows.Count} courses");
        if (d.Items.Count > 0) parts.Add(d.Items.Count == 1 ? "1 graded item" : $"{d.Items.Count} graded items");
        return string.Join(" · ", parts);
    }

    /// <summary>The grades came: the goal as the page keeps it (unless one is being saved), the picked course's own grades read.</summary>
    private void Arrived()
    {
        if (data.Data is not { } d) return;
        if (!goalSave.IsEnabled && (goalSeen is not { } seen || Math.Abs(seen - d.Goal) > 0.0001))
        {
            goal = Math.Clamp(d.Goal, 0, 4);
            goalSeen = d.Goal;
        }
        EnsureDetail();
        // (the screenshot suite: --open grades:101 opens that course's grades; --open grading-scale the shown course's scale)
        if (Launch.Take("grades:") is { } id && d.Rows.Any(r => r.Id == id)) engine.Go(Place.Section($"courses/{id}", "grades"));
        else if (d.Rows.Count > 0 && Launch.Take("grading-scale") != null)
        {
            var r = Shown(d);
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => GradesSheets.Scale(engine, r.Id, r.Code, r.Scale ?? d.Scale, r.OwnScale == true));
        }
    }

    private GradeRow Shown(GradesData d) => d.Rows.FirstOrDefault(r => r.Id == picked) ?? d.Rows[0];

    /// <summary>The course beside the list: its own grades read (only while there is room for it).</summary>
    private void EnsureDetail()
    {
        if (Width >= SplitAt && data.Data is { Rows.Count: > 0 } d) detail.Ensure(engine, "courseGrades", new { id = Shown(d).Id });
    }

    private static int CardCols(double w) => Math.Max(1, (int)Math.Floor((Math.Max(320, w) + 16) / (300 + 16)));

    protected override bool LayoutChanges(double before, double now)
    {
        // (the course beside the list checked only while there is room for it: narrowed, nothing shows it, and its grades
        //  — a heavy read — were checked every minute all the same)
        detail.Checks = now >= SplitAt;
        if (now >= SplitAt && before < SplitAt) EnsureDetail();
        return (before >= SplitAt) != (now >= SplitAt) || (before >= WideAt) != (now >= WideAt) || CardCols(before) != CardCols(now);
    }

    protected override FrameworkElement Build()
    {
        var sub = Subtitle;
        if (sub != lastSub) { lastSub = sub; HeaderChange(); }
        trend = null;
        if (data.Data is not { } d) return K.LoadState(data.Error, () => data.Reload(engine, "grades"));
        var w = Math.Max(320, Width);
        var split = w >= SplitAt;
        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 28 };
        page.Children.Add(Summary(d, w));
        page.Children.Add(Courses(d, w, split));
        if (d.Items.Count > 0) page.Children.Add(ItemGrades(d, split ? 2 : 1));
        var fact = K.Columns("auto, *",
            K.Icon(SymbolRegular.Info24, 16, B.Text2).Margin(0, 2, 9, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0),
            K.Wrapped($"Term GPA is worked out here from the scores {engine.LmsName} reports, on a 4.0 scale with every course counting equally. It is not your school’s official GPA.", Ty.Body, B.Text2).Col(1));
        page.Children.Add(fact);
        return page;
    }

    // ---- the term: GPA, goal, trend ----

    /// <summary>The term GPA, the goal and the trend: side by side on a wide page, the trend under the other two on a narrow
    /// one. The GPA and the goal keep to 540 across on a wide page, the trend or not, so their words stay together.</summary>
    private FrameworkElement Summary(GradesData d, double w)
    {
        var pair = K.Columns("*, 18, *", GpaCard(d).Col(0), GoalCard(d).Col(2));
        var points = TrendPoints(d.Trend);
        if (points.Count < 2) return w >= WideAt ? K.Columns("540, *", pair.Col(0)) : pair;
        var a = d.MinY ?? 2.4;
        var b = d.MaxY ?? 4;
        var lo = Math.Min(a, b);
        var hi = Math.Max(a, b);
        if (hi <= lo) hi = lo + 1;
        var trendCard = TrendCard(points, lo, hi);
        if (w >= WideAt) return K.Columns("540, 18, *", pair.Col(0), trendCard.Col(2));
        return K.V(18, pair, trendCard);
    }

    /// <summary>The page's snapshots as days, oldest first (one a day), each with its readout as the engine writes it
    /// ("Oct 12 · 3.43").</summary>
    private static List<(DateTime Day, double Gpa, string Label)> TrendPoints(List<TrendPoint>? trend)
    {
        var seen = new HashSet<DateTime>();
        var list = new List<(DateTime, double, string)>();
        foreach (var p in trend ?? new())
        {
            if (!double.IsFinite(p.Gpa) || !DateTime.TryParseExact(p.Date, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var day) || !seen.Add(day)) continue;
            var date = !string.IsNullOrEmpty(p.DateText) ? p.DateText : day.ToString(day.Year == DateTime.Today.Year ? "MMM d" : "MMM d, yyyy", Inv);
            var gpa = !string.IsNullOrEmpty(p.GpaText) ? p.GpaText : GradesKit.Gpa(p.Gpa);
            list.Add((day, p.Gpa, $"{date} · {gpa}"));
        }
        return list.OrderBy(p => p.Item1).ToList();
    }

    // ---- the GPA as the engine rounds it, and the goal against it ----

    /// <summary>The GPA as it is shown — rounded once, half up to two places (3.425 is 3.43) — for everything worked out
    /// from it here (the bar, the gap to a goal being moved), so the figures on the page add up. Null without one.</summary>
    private static double? ShownGpa(GradesData d) => d.GpaShown ?? (d.Gpa is { } g ? (double)GradesKit.Half(g, 2) : null);

    /// <summary>The GPA in words: the engine's ("3.43"; "–" with none).</summary>
    private static string GpaWords(GradesData d) =>
        !string.IsNullOrEmpty(d.GpaText) ? d.GpaText! : d.Gpa is { } g ? GradesKit.Gpa(g) : "–";

    /// <summary>The goal as it is saved: the page's words for it, until it is moved here.</summary>
    private string GoalWords(GradesData d) =>
        Math.Abs(goal - d.Goal) < 0.0001 && !string.IsNullOrEmpty(d.GoalText) ? d.GoalText! : GradesKit.Gpa(goal);

    /// <summary>
    /// How far the GPA shown is from the goal, worked out from the two as they are shown (3.43 and 4.00: 0.57 below goal),
    /// signed: the engine's own line for the goal it has saved, the same sum here while the goal is being moved. Null
    /// without a GPA.
    /// </summary>
    private (string Words, bool Up)? Gap(GradesData d)
    {
        if (Math.Abs(goal - d.Goal) < 0.0001 && !string.IsNullOrEmpty(d.GoalLine))
            return (d.GoalLine!, (d.GoalGap ?? 0) >= 0);
        if (ShownGpa(d) is not { } shown) return null;
        var gap = GradesKit.Half(shown, 2) - GradesKit.Half(goal, 2);
        var words = gap < 0 ? $"{(-gap).ToString("0.00", Inv)} below goal" : gap > 0 ? $"+{gap.ToString("0.00", Inv)} above goal" : "On your goal";
        return (words, gap >= 0);
    }

    private FrameworkElement GpaCard(GradesData d)
    {
        // (what the GPA is made of, and what it leaves out — a total the instructor hides — as the engine says it)
        var scored = d.Rows.Count(r => r.Pct != null);
        var counted = !string.IsNullOrEmpty(d.GpaNote) ? d.GpaNote!
            : scored == 0 ? "No course has a score yet." : $"From {scored} {(scored == 1 ? "course" : "courses")}, each counting equally.";
        var number = new TextBlock { TextWrapping = TextWrapping.NoWrap }.Tabular();
        var main = new Run(GpaWords(d)) { FontSize = 40, FontWeight = FontWeights.SemiBold, FontFamily = K.DisplayFace };
        main.SetResourceReference(TextElement.ForegroundProperty, B.Text);
        var tail = new Run("  of 4.00") { FontSize = 14, FontFamily = K.TextFace };
        tail.SetResourceReference(TextElement.ForegroundProperty, B.Text2);
        number.Inlines.Add(main);
        number.Inlines.Add(tail);
        gpaBarHolder = new ContentControl { Focusable = false, IsTabStop = false, Content = GpaBar(d) };
        var v = K.V(10, GradesKit.CardHeading("Term GPA", "4.0 scale"), number, gpaBarHolder, K.Wrapped(counted, Ty.Caption, B.Text2));
        var card = K.Card(v, new Thickness(18));
        AutomationProperties.SetName(card, $"Term GPA {(ShownGpa(d) != null ? GpaWords(d) : "none yet")} of 4.00. {counted}");
        return card;
    }

    /// <summary>The GPA out of 4, green at or above the goal and orange under it, the goal a tick across it.</summary>
    private FrameworkElement GpaBar(GradesData d)
    {
        var shown = ShownGpa(d);
        var color = shown != null ? K.Tone(Gap(d)?.Up == true ? "green" : "orange") : Appearance.Accent;
        return GradesKit.Bar(shown / 4, color, 8, goal / 4, "grades:gpa");
    }

    private FrameworkElement GoalCard(GradesData d)
    {
        goalText = new TextBlock { Text = GoalWords(d), FontSize = 40, FontWeight = FontWeights.SemiBold, FontFamily = K.DisplayFace, VerticalAlignment = VerticalAlignment.Center }.Tabular();
        goalText.Fg(B.Text);
        diffHolder = new ContentControl { Focusable = false, IsTabStop = false, Content = Diff(d) };
        var row = K.H(14, goalText, Stepper());
        var v = K.V(10, GradesKit.CardHeading("Goal", "saved as you change it"), row, diffHolder);
        return K.Card(v, new Thickness(18));
    }

    /// <summary>The goal's stepper (two buttons in one control's frame): 0 to 4, in twentieths.</summary>
    private FrameworkElement Stepper()
    {
        lower = K.IconButton(SymbolRegular.Subtract24, () => Step(-0.05), "Lower your goal", 14, 32);
        raise = K.IconButton(SymbolRegular.Add24, () => Step(0.05), "Raise your goal", 14, 32);
        lower.IsEnabled = goal > 0;
        raise.IsEnabled = goal < 4;
        var line = new Border { Width = 1, Margin = new Thickness(0, 7, 0, 7) }.Bg(B.Divider);
        var box = new Border { Child = K.H(0, lower, line, raise), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        box.Bg(B.Control).Stroke(B.ControlStroke);
        box.ToolTip = "Raise or lower your goal";
        return box;
    }

    /// <summary>Above or below the goal, by how much (green or orange), or that there is no score yet.</summary>
    private FrameworkElement Diff(GradesData d)
    {
        if (Gap(d) is not { } gap) return K.Text("No score to compare yet", Ty.Body, B.Text2);
        var up = gap.Up;
        var color = K.Tone(up ? "green" : "orange");
        var words = K.Text(gap.Words, Ty.Body).Tabular();
        words.FontWeight = FontWeights.Medium;
        words.Foreground = K.Brush(K.Darkened(color));
        return K.H(6, K.Icon(up ? SymbolRegular.ArrowCircleUp24 : SymbolRegular.ArrowCircleDown24, 18, color, filled: true), words);
    }

    /// <summary>The goal moved: shown at once (the number, above or below, the bar's colour and tick, the trend's rule); saved soon.</summary>
    private void Step(double by)
    {
        var g = Math.Clamp((double)GradesKit.Half(goal + by, 2), 0, 4);
        if (Math.Abs(g - goal) < 0.0001) return;
        goal = g;
        if (data.Data is { } d)
        {
            if (goalText != null) goalText.Text = GoalWords(d);
            if (diffHolder != null) diffHolder.Content = Diff(d);
            if (gpaBarHolder != null) gpaBarHolder.Content = GpaBar(d);
        }
        trend?.SetGoal(goal);
        if (lower != null) lower.IsEnabled = goal > 0;
        if (raise != null) raise.IsEnabled = goal < 4;
        if (data.Data is not { } saved || (Math.Abs(g - saved.Goal) <= 0.001 && !goalTouched)) return;
        goalTouched = true;
        goalSave.Stop();
        goalSave.Start();
    }

    private FrameworkElement TrendCard(List<(DateTime Day, double Gpa, string Label)> points, double lo, double hi)
    {
        var readout = K.Text("", Ty.Caption, B.Text2).Tabular();
        trend = new GradesTrendChart(points, lo, hi, goal, readout);
        return K.Card(K.V(10, GradesKit.CardHeadingWith("Trend", readout), trend), new Thickness(18));
    }

    // ---- the courses ----

    /// <summary>The courses: a list with the one picked beside it on a wide page, cards on a narrow one.</summary>
    private FrameworkElement Courses(GradesData d, double w, bool split)
    {
        string? note = d.Rows.Count == 0 ? null : split ? "Pick a course to see all of it" : "Click a course for each assignment’s grade and what-if scores";
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        v.Children.Add(Heading.Section("Courses", note));
        if (d.Rows.Count == 0) v.Children.Add(K.Card(K.Empty(SymbolRegular.Library24, "No current courses.")));
        else if (split) v.Children.Add(CourseSplit(d, w));
        else v.Children.Add(CourseCards(d, w));
        return v;
    }

    private FrameworkElement CourseSplit(GradesData d, double w)
    {
        var shown = Shown(d);
        var list = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        for (var i = 0; i < d.Rows.Count; i++)
        {
            if (i > 0) list.Children.Add(K.Divider(76));
            list.Children.Add(CourseRow(d.Rows[i], d.Rows[i].Id == shown.Id));
        }
        var listCard = K.Card(list, new Thickness(8));
        listCard.VerticalAlignment = VerticalAlignment.Top;
        var side = Detail(shown, d.Scale);
        side.VerticalAlignment = VerticalAlignment.Top;
        return K.Columns("370, 22, *", listCard.Col(0), side.Col(2));
    }

    /// <summary>A course in the list: its rings, its code and name and how much is graded, its score and letter; the one
    /// shown beside the list marked as Windows 11 marks a list's selection.</summary>
    private FrameworkElement CourseRow(GradeRow r, bool on)
    {
        var color = K.Hex(r.Color);
        var rings = GradesKit.Rings(r.Pct, color, GradesKit.Bands(r.Cats), 3, 48, 5.5, 3.5, 1.5);
        rings.MouseEnter += (_, _) => Shell.Tour.Did(Shell.TourEvent.RingHover); // (the tour's "A quick breakdown")
        // (the course's name whole, on two lines if it needs them; why it has no score — "Hidden by instructor" — under
        //  how much is graded, in the name's column: only the "–" at the right, which leaves the name its room)
        var words = K.V(2, K.Wrapped(r.Code, Ty.BodyStrong, B.Text, 2));
        if (GradesKit.SecondName(r) is { } alt) words.Children.Add(K.Text(alt, Ty.Caption, B.Text2));
        words.Children.Add(K.Wrapped(GradesKit.GradedLine(r), Ty.Caption, B.Text3, 2));
        if (r.Letter == null && GradesKit.Why(r) is { } why) words.Children.Add(GradesKit.WhyNote(r, why).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        words.VerticalAlignment = VerticalAlignment.Center;
        var score = K.Text(GradesKit.Percent(r.PctText, r.Pct), Ty.BodyLarge, r.Pct == null ? B.Text2 : B.Text).Tabular();
        score.FontWeight = FontWeights.SemiBold;
        score.HorizontalAlignment = HorizontalAlignment.Right;
        var right = K.V(4, score);
        if (r.Letter is { } l) right.Children.Add(GradesKit.Letter(l).Align(HorizontalAlignment.Right, VerticalAlignment.Center));
        right.VerticalAlignment = VerticalAlignment.Center;
        var pill = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Center };
        if (on) pill.Bg(B.Accent);
        var g = K.Columns("auto, auto, *, auto", pill.Margin(0, 0, 7, 0).Col(0), rings.Margin(0, 0, 14, 0).Col(1), words.Col(2), right.Margin(8, 0, 0, 0).Col(3));
        var b = K.Subtle(g, () => Pick(r.Id), new Thickness(4, 10, 10, 10), 6);
        if (on) b.SetResourceReference(Control.BackgroundProperty, B.Subtle);
        b.ToolTip = GradesKit.Why(r) is { } tipWhy ? $"{r.Code}: {tipWhy}" : $"{r.Code}: its groups and its latest grades";
        AutomationProperties.SetName(b, Spoken(r));
        AutomationProperties.SetItemStatus(b, on ? "Selected" : "");
        b.ContextMenu = CourseMenu(r);
        return b;
    }

    private void Pick(string id)
    {
        if (id == picked) return;
        picked = id;
        EnsureDetail();
        Render();
    }

    private static string Spoken(GradeRow r)
    {
        var score = r.Pct != null ? GradesKit.Percent(r.PctText, r.Pct).Replace("%", " percent") : GradesKit.Why(r) ?? "no score";
        var letter = r.Letter is { } l ? $", {l}" : "";
        return $"{r.Code}, {score}{letter}, {GradesKit.GradedLine(r)}";
    }

    /// <summary>A course's menu: Open course, Course grades; Open in Canvas, Copy link.</summary>
    private ContextMenu? CourseMenu(GradeRow r)
    {
        var link = r.Url ?? $"/courses/{r.Id}/grades";
        return GradesKit.Menu(new object[]
        {
            GradesKit.Item("Open course", SymbolRegular.ArrowUpRight24, () => engine.Go(Place.Home($"courses/{r.Id}"))),
            GradesKit.Item("Course grades", SymbolRegular.DataBarVertical24, () => engine.Go(Place.Section($"courses/{r.Id}", "grades"))),
            new Separator(),
            GradesKit.Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(link, r.Code)),
            GradesKit.Item("Copy link", SymbolRegular.Link24, () => GradesKit.Copy(engine, link)),
        });
    }

    /// <summary>A narrow page's courses: cards, as many across as fit, each opening the course's own grades.</summary>
    private FrameworkElement CourseCards(GradesData d, double w)
    {
        const double gap = 16;
        var cols = CardCols(w);
        var grid = new Grid();
        for (var i = 0; i < cols; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var rows = (d.Rows.Count + cols - 1) / cols;
        for (var r = 0; r < rows; r++)
        {
            if (r > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(gap) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var k = 0; k < d.Rows.Count; k++)
        {
            var card = CourseCard(d.Rows[k]);
            Grid.SetColumn(card, (k % cols) * 2);
            Grid.SetRow(card, (k / cols) * 2);
            grid.Children.Add(card);
        }
        return grid;
    }

    /// <summary>A course's card (the Mac's CourseGradeCard): its rings, score and letter, its target, how much of it is
    /// graded, and each of its first four groups' weight and score as a bar.</summary>
    private FrameworkElement CourseCard(GradeRow r)
    {
        var color = K.Hex(r.Color);
        var rings = GradesKit.Rings(r.Pct, color, GradesKit.Bands(r.Cats), 3, 66, 7, 4.5, 2);
        Shell.Tour.Mark(rings, Shell.TourSpot.GradeRing);
        rings.MouseEnter += (_, _) => Shell.Tour.Did(Shell.TourEvent.RingHover);
        var words = K.V(2, K.Wrapped(r.Code, Ty.BodyStrong, B.Text, 2));
        if (GradesKit.SecondName(r) is { } alt) words.Children.Add(K.Text(alt, Ty.Caption, B.Text2));
        words.Children.Add(K.Wrapped(GradesKit.GradedLine(r), Ty.Caption, B.Text3, 2));
        if (r.Letter == null && GradesKit.Why(r) is { } why) words.Children.Add(GradesKit.WhyNote(r, why).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        words.VerticalAlignment = VerticalAlignment.Center;
        var score = K.Text(GradesKit.Percent(r.PctText, r.Pct), Ty.Subtitle, r.Pct == null ? B.Text2 : B.Text).Tabular();
        score.HorizontalAlignment = HorizontalAlignment.Right;
        var tags = K.H(6);
        if (r.Target is { } t) tags.Children.Add(K.Text($"Target {t}", Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        if (r.Letter is { } l) tags.Children.Add(GradesKit.Letter(l));
        tags.HorizontalAlignment = HorizontalAlignment.Right;
        var right = K.V(4, score, tags);
        right.VerticalAlignment = VerticalAlignment.Center;
        var header = K.Columns("auto, *, auto", rings.Margin(0, 0, 14, 0).Col(0), words.Col(1), right.Margin(8, 0, 0, 0).Col(2));

        FrameworkElement groups;
        var cats = r.Cats.Take(4).ToList();
        if (cats.Count == 0) groups = K.Text("No graded groups yet.", Ty.Caption, B.Text3);
        else
        {
            var colors = GradesKit.CategoryColors(cats);
            var lines = new Stack { Orientation = Orientation.Vertical, Spacing = 10 };
            for (var i = 0; i < cats.Count; i++)
                lines.Children.Add(GradesKit.CategoryLine(cats[i].Label, cats[i].Weight, cats[i].Value, cats[i].Pct, colors[i], $"grades:{r.Id}#cat{i}"));
            groups = lines;
        }
        var content = K.V(14, header, groups);
        var card = K.CardButton(content, () => engine.Go(Place.Section($"courses/{r.Id}", "grades")), 8, new Thickness(18));
        Ahead.On(card, build => engine.Prepare(Place.Section($"courses/{r.Id}", "grades"), build));
        Shell.Tour.Mark(card, Shell.TourSpot.GradeDetails); // (the tour's "What if?": a card opens every grade)
        card.VerticalContentAlignment = VerticalAlignment.Top;
        card.ToolTip = GradesKit.Why(r) is { } tipWhy ? $"{r.Code}: {tipWhy}" : $"{r.Code}: every assignment’s grade, and what-if scores";
        AutomationProperties.SetName(card, Spoken(r));
        card.ContextMenu = CourseMenu(r);
        return card;
    }

    // ---- the course picked, beside the list (the Mac's GradeCourseDetail) ----

    private const int FirstShown = 8;

    /// <summary>
    /// The course picked beside the list, in one card: its rings, score and letter and its target; each of its groups
    /// with its weight and score; then its latest grades and what is still to be graded, each opening its assignment. Its
    /// own Grades — every assignment, What-If — its home and its grading scale are a click away.
    /// </summary>
    private FrameworkElement Detail(GradeRow row, List<ScaleStep> scale)
    {
        var cg = detail.Data is { } dd && dd.Id == row.Id ? dd : null;
        var failed = cg == null && detail.Data == null && detail.Error != null;
        var color = K.Hex(row.Color);

        // its rings, code and name, score, letter and target
        var rings = GradesKit.Rings(row.Pct, color, GradesKit.Bands(row.Cats), 4, 124, 11, 7, 2.5);
        Shell.Tour.Mark(rings, Shell.TourSpot.GradeRing);
        rings.MouseEnter += (_, _) => Shell.Tour.Did(Shell.TourEvent.RingHover);
        var words = K.V(5, K.Wrapped(row.Code, Ty.Subtitle, B.Text, 2));
        if (GradesKit.SecondName(row) is { } alt) words.Children.Add(K.Wrapped(alt, Ty.Body, B.Text2, 2));
        var score = new WrapPanel { Orientation = Orientation.Horizontal };
        score.Children.Add(K.Text(GradesKit.Percent(row.PctText, row.Pct), Ty.Count, row.Pct == null ? B.Text2 : B.Text).Tabular().Margin(0, 0, 10, 0));
        if (row.Letter is { } l) score.Children.Add(GradesKit.Letter(l).Margin(0, 0, 10, 0));
        else if (GradesKit.Why(row) is { } why) score.Children.Add(GradesKit.WhyNote(row, why).Margin(0, 0, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        if (row.Target is { } t) score.Children.Add(K.Text($"Target {t}", Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        words.Children.Add(score);
        words.Children.Add(K.Text(GradesKit.GradedLine(row), Ty.Caption, B.Text3));
        words.VerticalAlignment = VerticalAlignment.Center;
        var header = K.Columns("auto, *", rings.Margin(0, 0, 22, 0).Col(0), words.Col(1));
        AutomationProperties.SetName(header, Spoken(row));

        // its own grades, its home, its grading scale
        var every = K.Button("Every grade & what-if", () => engine.Go(Place.Section($"courses/{row.Id}", "grades")), accent: true, icon: SymbolRegular.Wand24);
        every.ToolTip = "Every assignment’s grade, and scores to try";
        Shell.Tour.Mark(every, Shell.TourSpot.GradeDetails);
        var open = K.Button("Open course", () => engine.Go(Place.Home($"courses/{row.Id}")), icon: SymbolRegular.ArrowUpRight24);
        var scaleButton = K.Button("Grading scale…", () => GradesSheets.Scale(engine, row.Id, row.Code, row.Scale ?? scale, row.OwnScale == true), icon: SymbolRegular.Options24);
        scaleButton.ToolTip = row.OwnScale == true ? "Your instructor’s cutoffs for each letter" : "Set each letter’s cutoff as your instructor did";
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, -10) };
        foreach (var b in new[] { every, open, scaleButton }) actions.Children.Add(b.Margin(0, 0, 10, 10));

        var card = K.V(18, header, actions, K.Divider(), Groups(row, cg), K.Divider(), Work(cg, failed, color, scale));
        return K.Card(card, new Thickness(20));
    }

    /// <summary>Every group, with its weight and score (the four the rings show until the course's own are read).</summary>
    private FrameworkElement Groups(GradeRow row, CourseGradesData? cg)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 11 };
        v.Children.Add(GradesKit.CardHeading("Assignment groups", cg?.Weighted == true ? "Weighted" : null));
        if (cg is { Groups.Count: > 0 })
        {
            var colors = GradesKit.GroupColors(cg.Groups);
            if (cg.Weighted == true) v.Children.Add(GradesKit.WeightBar(cg.Groups, colors).Margin(0, 0, 0, 2));
            foreach (var g in cg.Groups)
                v.Children.Add(GradesKit.CategoryLine(g.Name, g.WeightText, g.Value, g.Pct, colors.TryGetValue(g.Id, out var c) ? c : GradesKit.Gray, $"grades-detail:{row.Id}#{g.Id}"));
        }
        else if (row.Cats.Count == 0) v.Children.Add(K.Text("No graded groups yet.", Ty.Body, B.Text3));
        else
        {
            var colors = GradesKit.CategoryColors(row.Cats);
            for (var i = 0; i < row.Cats.Count; i++)
                v.Children.Add(GradesKit.CategoryLine(row.Cats[i].Label, row.Cats[i].Weight, row.Cats[i].Value, row.Cats[i].Pct, colors[i], $"grades:{row.Id}#cat{i}"));
        }
        return v;
    }

    /// <summary>The latest grades, newest first, and what is still to be graded, soonest first.</summary>
    private FrameworkElement Work(CourseGradesData? cg, bool failed, Color courseColor, List<ScaleStep> scale)
    {
        if (cg == null)
        {
            if (failed) return K.Empty(SymbolRegular.Warning24, "This course’s assignments could not be read.");
            return new Grid { Margin = new Thickness(0, 24, 0, 24), Children = { K.Spinner(20) } };
        }
        var real = cg.Rows.Where(r => r.Added != true).ToList();
        var graded = real.Where(r => r.Earned != null).OrderByDescending(r => r.Due ?? "", StringComparer.Ordinal).ToList();
        var waiting = real.Where(r => r.Earned == null && r.Counted != false).OrderBy(r => r.Due ?? "9999", StringComparer.Ordinal).ToList();
        var colors = GradesKit.GroupColors(cg.Groups);
        var letters = cg.Scale.Count == 0 ? scale : cg.Scale;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 6 };
        if (graded.Count == 0 && waiting.Count == 0) v.Children.Add(K.Empty(SymbolRegular.DocumentText24, "Nothing with points in this course yet."));
        if (graded.Count > 0)
        {
            v.Children.Add(GradesKit.CardHeading("Latest grades", graded.Count > FirstShown ? $"{FirstShown} of {graded.Count}" : null));
            v.Children.Add(Lines(graded.Take(FirstShown).ToList(), cg, colors, courseColor, letters));
        }
        if (waiting.Count > 0)
        {
            v.Children.Add(GradesKit.CardHeading("Not graded yet", waiting.Count > FirstShown ? $"{FirstShown} of {waiting.Count}" : null).Margin(0, graded.Count == 0 ? 0 : 12, 0, 0));
            v.Children.Add(Lines(waiting.Take(FirstShown).ToList(), cg, colors, courseColor, letters));
        }
        return v;
    }

    private FrameworkElement Lines(List<CGRow> rows, CourseGradesData cg, Dictionary<string, Color> colors, Color courseColor, List<ScaleStep> scale)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0, Margin = new Thickness(-8, 0, -8, 0) };
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0) v.Children.Add(K.Divider(29));
            v.Children.Add(Line(rows[i], cg, colors.TryGetValue(rows[i].GroupId, out var c) ? c : courseColor, scale));
        }
        return v;
    }

    /// <summary>An assignment's line: its group's colour, its name over its group and when it was due, its badge, its letter and its score.</summary>
    private FrameworkElement Line(CGRow r, CourseGradesData cg, Color color, List<ScaleStep> scale)
    {
        var group = cg.Groups.FirstOrDefault(g => g.Id == r.GroupId)?.Name;
        var facts = string.Join(" · ", new[] { group, r.DueText }.Where(s => !string.IsNullOrEmpty(s)));
        var words = K.V(2, K.Text(r.Name, Ty.Body, r.Dropped == true ? B.Text2 : B.Text));
        if (facts.Length > 0) words.Children.Add(K.Text(facts, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var end = K.H(8);
        if (!string.IsNullOrEmpty(r.Badge)) end.Children.Add(GradesKit.Status(r.Badge!, GradesKit.Tone(r.Badge!)));
        if (LetterOf(r, scale) is { } l) end.Children.Add(GradesKit.Letter(l));
        var points = K.Text(r.ScoreText, Ty.Body, r.Effective == null ? B.Text2 : B.Text).Tabular();
        points.FontWeight = FontWeights.Medium;
        points.MinWidth = 76;
        points.TextAlignment = TextAlignment.Right;
        points.VerticalAlignment = VerticalAlignment.Center;
        end.Children.Add(points);
        var g = K.Columns("auto, *, auto", K.Dot(color, 9).Margin(0, 0, 12, 0).Col(0), words.Col(1), end.Margin(8, 0, 0, 0).Col(2));
        var b = K.Subtle(g, r.Url is { } u ? () => engine.GoTo(u, r.Name) : null, new Thickness(8, 8, 8, 8));
        if (r.Url is { } url) b.ContextMenu = GradesKit.Menu(GradesKit.LinkItems(engine, url, r.Name, "Open assignment"));
        if (r.Url != null) Ahead.On(b, build => engine.PrepareWeb(r.Url, build));
        return b;
    }

    /// <summary>An assignment's letter on the course's scale, from the score it has.</summary>
    internal static string? LetterOf(CGRow r, List<ScaleStep> scale)
    {
        if (r.Effective is not { } e || r.Possible <= 0) return null;
        var p = e / r.Possible * 100;
        return scale.FirstOrDefault(s => p >= s.Min)?.Letter ?? scale.LastOrDefault()?.Letter;
    }

    // ---- every graded assignment, by its letter (the Mac's ItemGradesCard) ----

    /// <summary>
    /// Every graded assignment across the courses, by its letter: All, A, B, C, D and F with how many each holds beside
    /// the section's heading (Windows 11's SelectorBar), the work under it in one card — the first ones, then all of it;
    /// in two columns on a wide page. A row clicked opens its assignment.
    /// </summary>
    private FrameworkElement ItemGrades(GradesData d, int columns)
    {
        var counts = d.Counts ?? d.Items.GroupBy(i => i.Band).ToDictionary(g => g.Key, g => g.Count());
        var total = Letters.Sum(l => counts.GetValueOrDefault(l));
        var choices = new List<string> { $"All {total}" };
        choices.AddRange(Letters.Select(l => $"{l} {counts.GetValueOrDefault(l)}"));
        var picker = new SelectorBar(choices, band);
        AutomationProperties.SetName(picker, "Show grades by letter");
        var holder = new ContentControl { Focusable = false, IsTabStop = false };
        picker.Changed += i =>
        {
            band = i;
            showAll = false;
            holder.Content = ItemsCard(d, columns, holder);
        };
        holder.Content = ItemsCard(d, columns, holder);
        return K.V(0, Heading.Section("Item grades", null, picker), holder);
    }

    private FrameworkElement ItemsCard(GradesData d, int columns, ContentControl holder)
    {
        var key = band == 0 ? "all" : Letters[band - 1];
        var picked = band == 0 ? d.Items : d.Items.Where(i => i.Band == key).ToList();
        var first = columns > 1 ? 16 : 12;
        var shown = showAll ? picked : picked.Take(first).ToList();
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        if (shown.Count == 0) v.Children.Add(K.Empty(SymbolRegular.Filter24, $"No {key} grades in your courses."));
        var n = Math.Max(1, columns);
        var grid = new Grid();
        for (var c = 0; c < n; c++)
        {
            if (c > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var r = 0; r < (shown.Count + n - 1) / n; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < shown.Count; i++)
        {
            var cell = K.V(0, i >= n ? K.Divider(30) : null, ItemRow(shown[i]));
            Grid.SetColumn(cell, (i % n) * 2);
            Grid.SetRow(cell, i / n);
            grid.Children.Add(cell);
        }
        v.Children.Add(grid);
        void Redo() => holder.Content = ItemsCard(d, columns, holder);
        if (picked.Count > shown.Count)
            v.Children.Add(K.Link($"Show all {picked.Count}", () => { showAll = true; Redo(); }).Margin(2, 8, 0, 2).Align(HorizontalAlignment.Left));
        else if (showAll && picked.Count > first)
            v.Children.Add(K.Link("Show fewer", () => { showAll = false; Redo(); }).Margin(2, 8, 0, 2).Align(HorizontalAlignment.Left));
        return K.Card(v, new Thickness(6));
    }

    private FrameworkElement ItemRow(GradeItem item)
    {
        var words = K.V(2, K.Text(item.Name, Ty.Body), K.Text($"{item.Course} · {item.PctText}", Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var score = K.Text(item.Score, Ty.Body).Tabular();
        score.FontWeight = FontWeights.Medium;
        score.MinWidth = 72;
        score.TextAlignment = TextAlignment.Right;
        score.VerticalAlignment = VerticalAlignment.Center;
        var g = K.Columns("auto, *, auto, auto",
            K.Dot(K.Hex(item.Color), 10).Margin(0, 0, 12, 0).Col(0), words.Col(1),
            GradesKit.Letter(item.Band).Margin(8, 0, 8, 0).Col(2), score.Col(3));
        var b = K.Subtle(g, () => engine.GoTo(item.Url, item.Name), new Thickness(8, 8, 8, 8));
        if (!string.IsNullOrEmpty(item.Url)) b.ContextMenu = GradesKit.Menu(GradesKit.LinkItems(engine, item.Url!, item.Name));
        Ahead.On(b, build => engine.PrepareWeb(item.Url, build));
        return b;
    }
}

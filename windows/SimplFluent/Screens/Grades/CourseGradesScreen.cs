using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// One course's grades, its Grades section (the Mac's CourseGradesView): the total and each assignment group as rings
/// inside rings, each group's weight and score, and every assignment's mark — by group (in two columns on a wide page),
/// or sorted by grade, due date or name. With What-If on, every mark becomes a field to type a score into and each group
/// takes what-if assignments; the total, the letter and the term GPA follow, worked out by the page with the grade rules
/// the web screens use, and stay in sight in a bar at the page's foot. Nothing is saved.
/// </summary>
public sealed class CourseGradesScreen : Screen
{
    /// <summary>How the assignments are listed: by their group, or as one list by grade, due date or name.</summary>
    private enum GradeSort { Groups, High, Low, Due, Name }

    /// <summary>A what-if assignment added to a group: its name and what it is out of (its score is tried as any other's).</summary>
    private sealed record AddedWork(string Id, string GroupId, string Name, double Possible);

    private readonly string courseId;
    private readonly Loader<CourseGradesData> grades;
    private readonly DispatcherTimer applying = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool whatIf;
    private readonly Dictionary<string, string> drafts = new();
    private readonly List<AddedWork> added = new();
    private GradeSort sort = GradeSort.Groups;
    private string? lastSub;

    // the page scrolls on its own, so What-If's bar can stay at its foot
    private readonly Grid root = new();
    private readonly ScrollViewer scroller;
    private readonly ContentControl pageHost = new() { Focusable = false, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Border barHost = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 16, 16), Visibility = Visibility.Collapsed };
    private bool barShown;

    private Wpf.Ui.Controls.Button? whatIfButton;
    private FrameworkElement? commands;

    // the score fields, one for each assignment, kept from build to build: the one being typed in keeps its words and the keyboard
    private readonly Dictionary<string, TextBox> fields = new();
    private readonly HashSet<string> placed = new();
    private string? keep;
    private bool quiet;

    public CourseGradesScreen(Engine.Engine engine, string ctx) : base(engine)
    {
        courseId = ctx.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? ctx;
        grades = Watch(new Loader<CourseGradesData>());
        grades.Changed += Arrived;
        applying.Tick += (_, _) => { applying.Stop(); Read(fresh: false, force: true); };
        scroller = new ScrollViewer { Content = pageHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(scroller);
        root.Children.Add(barHost);
        if (View is ContentControl host) host.VerticalContentAlignment = VerticalAlignment.Stretch;
        // (the screenshot suite: --what-if opens the screen with What-If on)
        whatIf = Launch.Flag("what-if");
    }

    public override bool Scrolls => false;

    /// <summary>The section's name as the course's own tabs give it (Grades, unless the course calls it otherwise).</summary>
    public override string Title =>
        engine.Sections.TryGetValue($"courses/{courseId}", out var list) && list.FirstOrDefault(s => s.Kind == "grades") is { } s ? s.Label : "Grades";

    /// <summary>The course, its name and how much of it is graded (the Mac's heading line).</summary>
    public override string? Subtitle => grades.Data is { } d ? Headline(d) : null;

    /// <summary>What-If, Sort and More, beside the title (the Mac's toolbar).</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (commands != null) return commands;
            whatIfButton = K.Button("What-If", () => SetWhatIf(!whatIf), icon: SymbolRegular.Wand24);
            whatIfButton.MinWidth = 0;
            PaintToggle();
            Wpf.Ui.Controls.Button? sortButton = null;
            sortButton = K.Button("Sort", () => SortMenu(sortButton!), icon: SymbolRegular.ArrowSort24);
            sortButton.MinWidth = 0;
            sortButton.ToolTip = "Sort the assignments";
            var more = GradesKit.More(engine, () => $"/courses/{courseId}/grades", () => grades.Data is { } d ? $"{d.Code} · {Title}" : Title);
            return commands = K.H(8, whatIfButton, sortButton, more);
        }
    }

    private void PaintToggle()
    {
        if (whatIfButton == null) return;
        whatIfButton.Appearance = whatIf ? ControlAppearance.Primary : ControlAppearance.Secondary;
        whatIfButton.IsEnabled = grades.Data != null;
        whatIfButton.ToolTip = whatIf ? "Turn What-If off: your real scores come back" : "What-If: try scores and see where your grade would land";
        AutomationProperties.SetItemStatus(whatIfButton, whatIf ? "On" : "Off");
    }

    private static string SortName(GradeSort s) => s switch
    {
        GradeSort.High => "Highest Grade", GradeSort.Low => "Lowest Grade", GradeSort.Due => "Due Date", GradeSort.Name => "Name", _ => "By Group",
    };

    private static SymbolRegular SortSymbol(GradeSort s) => s switch
    {
        GradeSort.High => SymbolRegular.ArrowDown24, GradeSort.Low => SymbolRegular.ArrowUp24, GradeSort.Due => SymbolRegular.CalendarLtr24,
        GradeSort.Name => SymbolRegular.TextFont24, _ => SymbolRegular.Grid24,
    };

    /// <summary>Sort By: the five ways, the one chosen ticked.</summary>
    private void SortMenu(UIElement at)
    {
        var items = Enum.GetValues<GradeSort>().Select(s =>
        {
            var mi = GradesKit.Item(SortName(s), SortSymbol(s), () => { if (sort == s) return; sort = s; Render(); });
            mi.IsCheckable = true;
            mi.IsChecked = s == sort;
            if (s == sort) mi.FontWeight = FontWeights.SemiBold;
            return (object)mi;
        });
        if (GradesKit.Menu(items) is { } m) GradesKit.Open(m, at);
    }

    private static string Headline(CourseGradesData d)
    {
        var real = d.Rows.Where(r => r.Added != true).ToList();
        var parts = new List<string> { d.Code };
        if (!string.IsNullOrEmpty(d.Name) && d.Name != d.Code) parts.Add(d.Name!);
        if (real.Count > 0) parts.Add($"{real.Count(r => r.Earned != null)} of {real.Count} graded");
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    // ---- reading ----

    public override void Refresh() => Read(fresh: true, force: false);

    /// <summary>
    /// The course's grades with the scores tried and the assignments added (only the latest read is shown). While an
    /// answer for something just tried is on its way, the last one stays.
    /// </summary>
    private void Read(bool fresh, bool force)
    {
        var before = grades.Data;
        var args = Args(fresh);
        if (force) grades.Reload(engine, "courseGrades", args);
        else grades.Ensure(engine, "courseGrades", args);
        if (grades.Data == null && before != null) grades.Data = before;
    }

    private object Args(bool fresh)
    {
        var tried = drafts.ToDictionary(kv => kv.Key, kv => kv.Value.Replace(",", ".").Trim());
        var extra = added.Select(a => new { id = a.Id, groupId = a.GroupId, name = a.Name, possible = a.Possible }).ToList();
        return new { id = courseId, tried, added = extra, on = whatIf, fresh };
    }

    private void Arrived()
    {
        // (the screenshot suite: --open add-what-if turns What-If on and asks for a what-if assignment in the first group)
        if (grades.Data is { Groups.Count: > 0 } d && Launch.Take("add-what-if") != null)
        {
            if (!whatIf) SetWhatIf(true);
            var g = d.Groups[0];
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => AskAdd(g));
        }
    }

    // ---- the page ----

    protected override bool LayoutChanges(double before, double now)
    {
        static bool Narrow(double w) => w + 80 < 560;
        return (before >= 800) != (now >= 800) || (before >= 900) != (now >= 900) || Narrow(before) != Narrow(now);
    }

    public override bool Escape()
    {
        if (!fields.Values.Any(f => f.IsKeyboardFocusWithin)) return false;
        Keyboard.ClearFocus();
        return true;
    }

    protected override FrameworkElement Build()
    {
        var sub = Subtitle;
        if (sub != lastSub) { lastSub = sub; HeaderChange(); }
        PaintToggle();
        if (grades.Data is not { } d)
        {
            ShowBar(null);
            return K.LoadState(grades.Error, () => Read(fresh: true, force: true));
        }
        // (the shell's own page margins, as it gives a page that scrolls with it)
        var pad = Width + 80 < 560 ? 20 : 40;
        scroller.Padding = new Thickness(pad, 0, pad, 0);
        // (the room at the foot is the page's, so it scrolls; with What-If on it is room for the bar, so the last rows,
        // their scores to type, scroll clear of it — a scroller's own padding would stand still, a strip under the bar)
        pageHost.Margin = new Thickness(0, 0, 0, whatIf ? 104 : 40);
        var w = Width;

        // the field being typed in stays as it is through the rebuild, and keeps the keyboard
        var typing = fields.FirstOrDefault(f => f.Value.IsKeyboardFocusWithin);
        keep = typing.Value != null ? typing.Key : null;
        var (selStart, selLength) = typing.Value != null ? (typing.Value.SelectionStart, typing.Value.SelectionLength) : (0, 0);
        placed.Clear();

        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 28 };
        page.Children.Add(Overview(d, w));
        page.Children.Add(Assignments(d, w));
        pageHost.Content = page;

        foreach (var id in fields.Keys.Where(k => !placed.Contains(k)).ToList()) fields.Remove(id);
        if (keep != null && fields.TryGetValue(keep, out var tb))
        {
            tb.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                tb.Focus();
                Keyboard.Focus(tb);
                var start = Math.Min(selStart, tb.Text.Length);
                tb.Select(start, Math.Min(selLength, tb.Text.Length - start));
            });
        }
        keep = null;
        ShowBar(whatIf ? d : null);
        return root;
    }

    // ---- the total and the groups ----

    /// <summary>The total (with What-If's switch under it) and the groups: side by side on a wide page, one over the other on a narrow one.</summary>
    private FrameworkElement Overview(CourseGradesData d, double w)
    {
        var colors = GradesKit.GroupColors(d.Groups);
        var total = TotalCard(d);
        var groups = GroupsCard(d, colors);
        if (w >= 800) return K.Columns("460, 18, *", total.Col(0), groups.Col(2));
        return K.V(18, total, groups);
    }

    private FrameworkElement TotalCard(CourseGradesData d)
    {
        var color = K.Hex(d.Color);
        var orange = GradesKit.Orange;
        var rings = GradesKit.Rings(d.Total, whatIf ? orange : color, GradesKit.Bands(d.Groups), 4, 140, 12, 7.5, 2.5);
        var words = K.V(6, K.Text(d.TotalText, Ty.TitleLarge).Tabular());
        if (d.Letter is { } l)
        {
            var letter = K.Text(whatIf ? $"{l} with what-if" : l, Ty.BodyLarge);
            letter.FontWeight = FontWeights.SemiBold;
            letter.Foreground = new SolidColorBrush(K.Darkened(whatIf ? orange : color));
            words.Children.Add(letter);
        }
        words.Children.Add(TargetPicker(d));
        words.VerticalAlignment = VerticalAlignment.Center;
        var head = K.Columns("auto, *", rings.Margin(0, 0, 22, 0).Col(0), words.Col(1));
        AutomationProperties.SetName(rings, $"{d.TotalText}{(d.Letter is { } sl ? ", " + sl : "")}");

        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 14 };
        v.Children.Add(head);
        if (!string.IsNullOrEmpty(d.Note)) v.Children.Add(K.Wrapped(d.Note!, Ty.Body, B.Text2));
        if (!string.IsNullOrEmpty(d.Final)) v.Children.Add(K.Wrapped(d.Final!, Ty.Caption, B.Text3));
        v.Children.Add(K.Divider());
        v.Children.Add(WhatIfControl(d));
        // (What-If on: the card takes its colour)
        var inner = new Border { Child = v, Padding = new Thickness(18), CornerRadius = new CornerRadius(7) };
        var card = K.Card(inner, new Thickness(0));
        if (whatIf)
        {
            inner.Background = new SolidColorBrush(K.WithAlpha(orange, Appearance.Dark ? 0.12 : 0.08));
            card.BorderBrush = new SolidColorBrush(K.WithAlpha(orange, 0.45));
        }
        return card;
    }

    /// <summary>The grade aimed for in this course: No Target, or a letter of its scale (a target the scale has no letter for, as Pass/Fail, still shown as chosen).</summary>
    private FrameworkElement TargetPicker(CourseGradesData d)
    {
        var tags = new List<string> { "" };
        var labels = new List<string> { "No Target" };
        if (d.Target is { } t && !d.Scale.Any(s => s.Letter == t)) { tags.Add(t); labels.Add(t); }
        foreach (var s in d.Scale) { tags.Add(s.Letter); labels.Add(s.Letter); }
        var box = new ComboBox { ItemsSource = labels, MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
        box.SelectedIndex = Math.Max(0, tags.IndexOf(d.Target ?? ""));
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0 && box.SelectedIndex < tags.Count) SetTarget(tags[box.SelectedIndex]); };
        box.ToolTip = "The grade you are aiming for in this course";
        AutomationProperties.SetName(box, "Target");
        return K.H(10, K.Text("Target", Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center), box);
    }

    private FrameworkElement GroupsCard(CourseGradesData d, Dictionary<string, Color> colors)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 10 };
        v.Children.Add(GradesKit.CardHeading("Assignment Groups", d.Weighted == true ? "Weighted" : null));
        if (d.Groups.Count == 0) v.Children.Add(K.Empty(SymbolRegular.Stack24, "No assignment groups."));
        else if (d.Weighted == true) v.Children.Add(GradesKit.WeightBar(d.Groups, colors).Margin(0, 2, 0, 0));
        var lines = new Stack { Orientation = Orientation.Vertical, Spacing = 12, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var g in d.Groups)
            lines.Children.Add(GradesKit.CategoryLine(g.Name, g.WeightText, g.Value, g.Pct, colors.TryGetValue(g.Id, out var c) ? c : GradesKit.Gray, $"course-grades:{courseId}#{g.Id}"));
        v.Children.Add(lines);
        return K.Card(v, new Thickness(18));
    }

    // ---- what-if ----

    /// <summary>What-If's switch at the foot of the total's card, and the term GPA it would make.</summary>
    private FrameworkElement WhatIfControl(CourseGradesData d)
    {
        var orange = GradesKit.Orange;
        var icon = K.Icon(SymbolRegular.Wand24, 20, orange);
        icon.Width = 26;
        var words = K.V(2, K.Text("What-If Scores", Ty.BodyStrong),
            K.Wrapped(whatIf ? "Type a score into any assignment, or add one. Nothing is saved." : "Try scores and see where your grade would land.", Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var toggle = new ToggleSwitch { IsChecked = whatIf, VerticalAlignment = VerticalAlignment.Center };
        toggle.Checked += (_, _) => SetWhatIf(true);
        toggle.Unchecked += (_, _) => SetWhatIf(false);
        toggle.ToolTip = whatIf ? "Turn What-If off: your real scores come back" : "What-If: try scores and see where your grade would land";
        AutomationProperties.SetName(toggle, "What-If Scores");
        Shell.Tour.Mark(toggle, Shell.TourSpot.WhatIfSwitch);
        var row = K.Columns("auto, *, auto", icon.Margin(0, 0, 12, 0).Col(0), words.Col(1), toggle.Margin(8, 0, 0, 0).Col(2));
        var v = K.V(10, row);
        if (whatIf && (d.GpaIf != null || drafts.Count > 0 || added.Count > 0))
        {
            var line = K.Columns("*, auto");
            if (d.GpaIf is { } g)
            {
                var t = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }.Tabular();
                K.Style(t, Ty.Body);
                t.Fg(B.Text);
                t.Inlines.Add(new Run("Term GPA would be "));
                t.Inlines.Add(new Bold(new Run(g.ToString("0.00", GradesKit.Inv))));
                if (d.Gpa is { } now)
                {
                    var was = new Run($"  (now {now.ToString("0.00", GradesKit.Inv)})");
                    was.SetResourceReference(TextElement.ForegroundProperty, B.Text2);
                    t.Inlines.Add(was);
                }
                line.Children.Add(t.Col(0));
            }
            if (drafts.Count > 0 || added.Count > 0)
            {
                var clear = K.Button("Clear", Reset, icon: SymbolRegular.ArrowUndo24);
                clear.MinWidth = 0;
                clear.ToolTip = "Clear the what-if scores";
                line.Children.Add(clear.Margin(8, 0, 0, 0).Col(1));
            }
            line.Margin = new Thickness(38, 0, 0, 0);
            v.Children.Add(line);
        }
        return v;
    }

    /// <summary>While What-If is on, the total it gives (and the term GPA it would make) at the page's foot, so it stays
    /// in sight while scores are typed far down the list.</summary>
    private FrameworkElement WhatIfBar(CourseGradesData d)
    {
        var orange = GradesKit.Orange;
        var label = K.Text("What-If", Ty.BodyStrong);
        label.Foreground = new SolidColorBrush(K.Darkened(orange));
        var row = K.H(12, K.H(6, K.Icon(SymbolRegular.Wand24, 16, orange), label), K.Text(d.TotalText, Ty.BodyStrong).Tabular());
        if (d.Letter is { } l) row.Children.Add(GradesKit.Letter(l));
        if (d.GpaIf is { } g)
        {
            row.Children.Add(new Border { Width = 1, Height = 16 }.Bg(B.Divider));
            row.Children.Add(K.Text($"Term GPA {g.ToString("0.00", GradesKit.Inv)}", Ty.Body, B.Text2).Tabular());
        }
        foreach (var c in row.Children.OfType<FrameworkElement>()) c.VerticalAlignment = VerticalAlignment.Center;
        var tint = new Border { Child = row, Padding = new Thickness(20, 12, 20, 12), CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(K.WithAlpha(orange, 0.08)) };
        var bar = new Border { Child = tint, CornerRadius = new CornerRadius(8), Effect = K.Shadow(24, 6, 0.2) };
        bar.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        AutomationProperties.SetName(bar, $"What-If: {d.TotalText}{(d.Letter is { } sl ? ", " + sl : "")}{(d.GpaIf is { } sg ? $", term GPA {sg.ToString("0.00", GradesKit.Inv)}" : "")}");
        return bar;
    }

    private void ShowBar(CourseGradesData? d)
    {
        if (d == null)
        {
            if (!barShown) return;
            barShown = false;
            Motion.FadeOut(barHost, () =>
            {
                if (barShown) return;
                barHost.BeginAnimation(UIElement.OpacityProperty, null);
                barHost.Opacity = 1;
                barHost.Visibility = Visibility.Collapsed;
            }, Motion.Normal);
            return;
        }
        barHost.Child = WhatIfBar(d);
        if (barShown) return;
        barShown = true;
        barHost.BeginAnimation(UIElement.OpacityProperty, null);
        barHost.Opacity = 1;
        barHost.Visibility = Visibility.Visible;
        Motion.Enter(barHost, 0, 24, 333);
    }

    private void SetWhatIf(bool on)
    {
        if (on == whatIf) return;
        Shell.Tour.Did(on ? Shell.TourEvent.WhatIfOn : Shell.TourEvent.WhatIfOff); // (the tour's what-if steps)
        applying.Stop();
        if (!on)
        {
            if (fields.Values.Any(f => f.IsKeyboardFocusWithin)) Keyboard.ClearFocus();
            drafts.Clear();
            added.Clear();
            fields.Clear();
        }
        whatIf = on;
        PaintToggle();
        Read(fresh: false, force: true);
        Render();
    }

    /// <summary>A score typed: tried once typing pauses (Enter tries it at once). The first one shows its row's undo straight away.</summary>
    private void Typed(string id, string text)
    {
        var real = grades.Data?.Rows.FirstOrDefault(r => r.Id == id) is { Effective: { } e } ? GradesKit.Num(e) : "";
        var current = drafts.TryGetValue(id, out var v) ? v : real;
        if (text == current) return;
        var first = !drafts.ContainsKey(id);
        drafts[id] = text;
        applying.Stop();
        applying.Start();
        if (first) Render();
    }

    private void ApplyNow()
    {
        applying.Stop();
        Read(fresh: false, force: true);
    }

    private void UseReal(CGRow r)
    {
        drafts.Remove(r.Id);
        Read(fresh: false, force: true);
        Render();
    }

    private void Remove(CGRow r)
    {
        added.RemoveAll(a => a.Id == r.Id);
        drafts.Remove(r.Id);
        Read(fresh: false, force: true);
        Render();
    }

    private void Reset()
    {
        if (fields.Values.Any(f => f.IsKeyboardFocusWithin)) Keyboard.ClearFocus();
        applying.Stop();
        drafts.Clear();
        added.Clear();
        Read(fresh: false, force: true);
        Render();
    }

    private void AskAdd(CGGroup g) => GradesSheets.AddWhatIf(engine, g, (name, possible, score) => AddWhatIf(g, name, possible, score));

    private void AddWhatIf(CGGroup g, string name, double possible, double? score)
    {
        var id = "whatif-" + Guid.NewGuid().ToString("N")[..8];
        added.Add(new AddedWork(id, g.Id, name, possible));
        drafts[id] = score is { } s ? GradesKit.Num(s) : "";
        Read(fresh: false, force: true);
    }

    /// <summary>The grade aimed for, changed: shown at once, told to the page, then everything read afresh (the page
    /// keeps a course's target with its groups).</summary>
    private async void SetTarget(string letter)
    {
        if (grades.Data is not { } d) return;
        var before = d.Target;
        string? next = letter.Length == 0 ? null : letter;
        if (before == next) return;
        d.Target = next;
        Render();
        var ok = await engine.Act("setTarget", next == null ? new { id = courseId } : (object)new { id = courseId, letter });
        if (!ok && grades.Data is { } now) { now.Target = before; Render(); }
        Read(fresh: true, force: true);
        engine.DataChanged();
    }

    // ---- the assignments ----

    /// <summary>The assignments: by group — the groups in two columns on a wide page, balanced, in their order — or as one list sorted.</summary>
    private FrameworkElement Assignments(CourseGradesData d, double w)
    {
        var colors = GradesKit.GroupColors(d.Groups);
        if (d.Rows.Count == 0 && !whatIf)
            return GradesKit.CardSection("Assignments", null, K.Empty(SymbolRegular.DocumentText24, "Nothing with points in this course yet.").Margin(10, 10, 10, 10));
        if (sort == GradeSort.Groups)
        {
            var shown = d.Groups.Where(g => whatIf || d.Rows.Any(r => r.GroupId == g.Id)).ToList();
            if (w >= 900 && shown.Count > 1)
            {
                var (left, right) = GradesKit.Balanced(shown, g => d.Rows.Count(r => r.GroupId == g.Id) + 3);
                return K.Columns("*, 22, *", GroupColumn(left, d, colors).Col(0), GroupColumn(right, d, colors).Col(2));
            }
            return GroupColumn(shown, d, colors);
        }
        var list = RowList(Sorted(d), d, colors, true);
        if (whatIf && d.Groups.Count > 0) list.Children.Add(AddMenu(d));
        return GradesKit.CardSection("All Assignments", SortName(sort), list);
    }

    private FrameworkElement GroupColumn(List<CGGroup> groups, CourseGradesData d, Dictionary<string, Color> colors)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 22, VerticalAlignment = VerticalAlignment.Top };
        foreach (var g in groups) v.Children.Add(GroupSection(g, d, colors));
        return v;
    }

    /// <summary>A group: its name, weight and score over its assignments; with What-If on, a what-if assignment to add; what the page says of how it counts.</summary>
    private FrameworkElement GroupSection(CGGroup g, CourseGradesData d, Dictionary<string, Color> colors)
    {
        var rows = d.Rows.Where(r => r.GroupId == g.Id).ToList();
        var meta = string.Join(" · ", new[] { g.WeightText, g.Value }.Where(s => !string.IsNullOrEmpty(s)));
        var body = RowList(rows, d, colors, false);
        if (whatIf) body.Children.Add(AddLink(() => AskAdd(g)).Margin(6, rows.Count == 0 ? 2 : 6, 0, 2));
        if (!string.IsNullOrEmpty(g.Detail)) body.Children.Add(K.Wrapped(g.Detail!, Ty.Caption, B.Text2).Margin(10, 8, 10, 6));
        return GradesKit.CardSection(g.Name, meta, body);
    }

    /// <summary>Add What-If Assignment (a link's look, the accent's colour).</summary>
    private static Button AddLink(Action click)
    {
        var content = K.H(8, K.Icon(SymbolRegular.AddCircle24, 16, B.AccentText), K.Text("Add What-If Assignment", Ty.Body, B.AccentText));
        var b = K.Subtle(content, click, new Thickness(8, 6, 10, 6));
        b.HorizontalAlignment = HorizontalAlignment.Left;
        return b;
    }

    /// <summary>In a sorted list, a what-if assignment is added to the group picked from a menu.</summary>
    private FrameworkElement AddMenu(CourseGradesData d)
    {
        Button? b = null;
        b = AddLink(() =>
        {
            var m = GradesKit.Menu(d.Groups.Select(g => (object)GradesKit.Item(g.Name, SymbolRegular.Stack24, () => AskAdd(g))));
            if (m != null && b != null) GradesKit.Open(m, b);
        });
        b.Margin = new Thickness(6, 6, 0, 2);
        return b;
    }

    private Stack RowList(List<CGRow> rows, CourseGradesData d, Dictionary<string, Color> colors, bool showGroup)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0) v.Children.Add(K.Divider(40));
            v.Children.Add(Row(rows[i], d, colors.TryGetValue(rows[i].GroupId, out var c) ? c : K.Hex(d.Color), showGroup));
        }
        return v;
    }

    /// <summary>An assignment's row: opens it; with What-If on, the score field takes the clicks instead.</summary>
    private FrameworkElement Row(CGRow r, CourseGradesData d, Color color, bool showGroup)
    {
        var content = RowContent(r, d, color, showGroup);
        FrameworkElement el;
        if (whatIf)
        {
            el = new Border { Child = content, Padding = new Thickness(10, 9, 10, 9), Background = Brushes.Transparent };
        }
        else
        {
            var url = r.Url;
            el = K.Subtle(content, url == null ? null : () => engine.GoTo(url, r.Name), new Thickness(10, 9, 10, 9));
        }
        el.ContextMenu = RowMenu(r);
        return el;
    }

    private FrameworkElement RowContent(CGRow r, CourseGradesData d, Color color, bool showGroup)
    {
        var tried = r.Hypothetical == true || r.Added == true;
        var group = showGroup ? d.Groups.FirstOrDefault(g => g.Id == r.GroupId)?.Name : null;
        // the group's colour as a dot; a what-if one's wand
        UIElement mark = r.Added == true ? K.Icon(SymbolRegular.Wand24, 14, GradesKit.Orange) : K.Dot(color, 10);
        var markBox = new Grid { Width = 18, VerticalAlignment = VerticalAlignment.Center, Children = { mark } };
        var words = K.V(3, K.Wrapped(r.Name, Ty.Body, r.Dropped == true ? B.Text2 : B.Text, 2));
        if (Facts(r, group) is { } f) words.Children.Add(f);
        words.VerticalAlignment = VerticalAlignment.Center;
        FrameworkElement end = whatIf && r.Counted != false ? ScoreField(r, tried) : Score(r, d);
        var g = K.Columns("auto, *, auto, auto", markBox.Margin(0, 0, 12, 0).Col(0), words.Col(1), end.Margin(8, 0, 0, 0).Col(2));
        if (whatIf) g.Children.Add(RowAction(r).Margin(6, 0, 0, 0).Col(3));
        return g;
    }

    /// <summary>Under an assignment's name: Missing, Late or Excused, Dropped, its group (in a sorted list) and when it is due.</summary>
    private static FrameworkElement? Facts(CGRow r, string? group)
    {
        var badge = string.IsNullOrEmpty(r.Badge) ? null : r.Badge;
        var line = string.Join(" · ", new[] { group, r.DueText }.Where(s => !string.IsNullOrEmpty(s)));
        if (badge == null && r.Dropped != true && line.Length == 0) return null;
        var h = new WrapPanel { Orientation = Orientation.Horizontal };
        if (badge != null) h.Children.Add(GradesKit.Status(badge, GradesKit.Tone(badge)).Margin(0, 0, 6, 0));
        if (r.Dropped == true) h.Children.Add(GradesKit.Status("Dropped").Margin(0, 0, 6, 0));
        if (line.Length > 0) h.Children.Add(K.Text(line, Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        return h;
    }

    /// <summary>An assignment's mark: its letter on the course's scale, its score, the grade the school wrote.</summary>
    private static FrameworkElement Score(CGRow r, CourseGradesData d)
    {
        var top = K.H(6);
        if (GradesScreen.LetterOf(r, d.Scale) is { } l) top.Children.Add(GradesKit.Letter(l));
        var points = K.Text(r.ScoreText, Ty.Body, r.Effective == null ? B.Text2 : B.Text).Tabular();
        points.FontWeight = FontWeights.Medium;
        points.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(points);
        top.HorizontalAlignment = HorizontalAlignment.Right;
        var v = K.V(1, top);
        if (!string.IsNullOrEmpty(r.Grade)) v.Children.Add(K.Text(r.Grade!, Ty.Caption, B.Text2).Align(HorizontalAlignment.Right));
        v.MinWidth = 120;
        v.VerticalAlignment = VerticalAlignment.Center;
        return v;
    }

    /// <summary>A what-if score typed straight into the row: the score, then what it is out of.</summary>
    private FrameworkElement ScoreField(CGRow r, bool tried)
    {
        var tb = Field(r.Id);
        if (r.Id != keep)
        {
            var text = drafts.TryGetValue(r.Id, out var v) ? v : r.Effective is { } e ? GradesKit.Num(e) : "";
            if (tb.Text != text)
            {
                quiet = true;
                tb.Text = text;
                quiet = false;
            }
        }
        tb.FontWeight = tried ? FontWeights.SemiBold : FontWeights.Normal;
        if (tried) tb.Foreground = new SolidColorBrush(K.Darkened(GradesKit.Orange));
        else tb.SetResourceReference(Control.ForegroundProperty, B.Text);
        AutomationProperties.SetName(tb, $"What-if score for {r.Name}, out of {GradesKit.Num(r.Possible)}");
        var outOf = K.Text($"/ {GradesKit.Num(r.Possible)}", Ty.Caption, B.Text2).Tabular();
        outOf.MinWidth = 44;
        outOf.VerticalAlignment = VerticalAlignment.Center;
        var h = K.H(6, tb, outOf);
        h.MinWidth = 120;
        h.HorizontalAlignment = HorizontalAlignment.Right;
        h.VerticalAlignment = VerticalAlignment.Center;
        return h;
    }

    /// <summary>The assignment's field, the same one as last time (taken from where it was).</summary>
    private TextBox Field(string id)
    {
        if (placed.Contains(id) || !fields.TryGetValue(id, out var tb))
        {
            tb = NewField(id);
            if (!placed.Contains(id)) fields[id] = tb;
        }
        placed.Add(id);
        if (tb.Parent is Panel p) p.Children.Remove(tb);
        return tb;
    }

    private TextBox NewField(string id)
    {
        var tb = new TextBox
        {
            PlaceholderText = "—", Width = 66, MinWidth = 0, TextAlignment = TextAlignment.Right, ClearButtonEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
        }.Tabular();
        tb.TextChanged += (_, _) =>
        {
            if (quiet) return;
            Typed(id, tb.Text);
            Shell.Tour.Did(Shell.TourEvent.WhatIfEdited); // (the tour's "Change a score")
        };
        Shell.Tour.Mark(tb, Shell.TourSpot.WhatIfScore);
        tb.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ApplyNow(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Keyboard.ClearFocus(); e.Handled = true; }
        };
        return tb;
    }

    /// <summary>While What-If is on, a row's own undo: remove a what-if assignment, or go back to the real score.</summary>
    private FrameworkElement RowAction(CGRow r)
    {
        Button? b = null;
        if (r.Added == true)
        {
            b = K.IconButton(SymbolRegular.SubtractCircle24, () => Remove(r), "Remove this what-if assignment", 16, 28);
            AutomationProperties.SetName(b, $"Remove {r.Name}");
        }
        else if (drafts.ContainsKey(r.Id))
        {
            b = K.IconButton(SymbolRegular.ArrowUndo24, () => UseReal(r), "Use my real score", 16, 28);
            AutomationProperties.SetName(b, $"Use my real score for {r.Name}");
        }
        if (b == null) return new Border { Width = 28, Height = 28 };
        if (b.Content is SymbolIcon icon) icon.SetResourceReference(Control.ForegroundProperty, B.Text2);
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    private ContextMenu? RowMenu(CGRow r)
    {
        var items = new List<object>();
        if (r.Url is { } open) items.Add(GradesKit.Item("Open Assignment", SymbolRegular.Open24, () => engine.GoTo(open, r.Name)));
        if (r.Added == true) items.Add(GradesKit.Item("Remove What-If Assignment", SymbolRegular.SubtractCircle24, () => Remove(r)));
        else if (drafts.ContainsKey(r.Id)) items.Add(GradesKit.Item("Use My Real Score", SymbolRegular.ArrowUndo24, () => UseReal(r)));
        if (r.Url is { } url)
        {
            items.Add(new Separator());
            items.Add(GradesKit.Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(url, r.Name)));
            items.Add(GradesKit.Item("Copy Link", SymbolRegular.Link24, () => GradesKit.Copy(engine, url)));
        }
        return GradesKit.Menu(items);
    }

    private List<CGRow> Sorted(CourseGradesData d)
    {
        static double? Pct(CGRow r) => r.Effective is { } e && r.Possible > 0 ? e / r.Possible * 100 : null;
        switch (sort)
        {
            case GradeSort.High or GradeSort.Low:
            {
                var graded = d.Rows.Where(r => Pct(r) != null);
                graded = sort == GradeSort.High ? graded.OrderByDescending(r => Pct(r)!.Value) : graded.OrderBy(r => Pct(r)!.Value);
                return graded.Concat(d.Rows.Where(r => Pct(r) == null)).ToList();
            }
            case GradeSort.Due:
                return d.Rows.OrderBy(r => r.Due ?? "9999", StringComparer.Ordinal).ToList();
            case GradeSort.Name:
                return d.Rows.OrderBy(r => r.Name, Comparer<string>.Create(GradesKit.Natural)).ToList();
            default:
                return d.Rows;
        }
    }
}

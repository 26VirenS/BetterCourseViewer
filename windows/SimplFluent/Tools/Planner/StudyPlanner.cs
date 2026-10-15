using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Screens;
using Simpl.Support;
using Simpl.Tools.Planner;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Study Planner (0.3.11), as the Mac's: the work due in the next six weeks — the same assignments, quizzes and exams
// the Dashboard, To Do and Calendar show, read from the engine's calendar — shared out as study blocks over the days
// before each is due, by its points and its kind (Tools/Planner/Model/StudyPlan.cs). A week view, today first; a list
// view; how each piece stands; the hours a day, the block length, the time of day, the days to finish early and the
// courses to include, all remembered. A block ticked is done: it stays where it was, comes off what the work needs, and
// Regenerate plans the rest round it. Add to Calendar saves the blocks as a calendar file in Downloads. Nothing is
// written to Canvas.

#pragma warning disable CA2255
internal static class StudyPlannerRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("plan", c => new StudyPlannerTool(c), c => new StudyPlannerCompact(c).View);
}
#pragma warning restore CA2255

/// <summary>
/// What the planner holds (kept while its views come and go): the work read, the settings and the blocks done (both
/// remembered on this PC: Prefs <c>SimplPlan.*</c>), the plan made of them, and which view shows.
/// </summary>
internal sealed class PlanState
{
    public static readonly PlanState Shared = new();

    private const string HoursKey = "SimplPlan.hours", BlockKey = "SimplPlan.block", TimeKey = "SimplPlan.time", FinishKey = "SimplPlan.finish",
        CoursesKey = "SimplPlan.courses", DoneKey = "SimplPlan.done", ViewKey = "SimplPlan.view";

    public List<PlanItem>? Items;
    public readonly Dictionary<string, Color> Colors = new();
    public PlanSettings Settings = new()
    {
        Hours = StudyPlan.ParseHours(Prefs.String(HoursKey)),
        BlockMinutes = PlanSettings.Blocks.Contains((int)Prefs.Double(BlockKey, 45)) ? (int)Prefs.Double(BlockKey, 45) : 45,
        Time = Enum.TryParse<PlanTime>(Prefs.String(TimeKey), true, out var t) ? t : PlanTime.Afternoon,
        FinishDaysBefore = (int)Math.Clamp(Prefs.Double(FinishKey, 1), 0, 14),
        Excluded = (Prefs.String(CoursesKey) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(),
    };
    public List<PlanBlock> Done = ReadDone();
    public StudyPlanResult? Plan;
    public bool Loading;
    public string? Error;
    public string View = Prefs.String(ViewKey) == "list" ? "list" : "week";
    private int reading;

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();

    /// <summary>The courses the work read is in, by code, for the settings' toggles.</summary>
    public IReadOnlyList<string> Courses => (Items ?? new()).Select(i => i.Course).Distinct().OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

    public PlanItem? ItemOf(string id) => Items?.FirstOrDefault(i => i.Id == id);

    private static List<PlanBlock> ReadDone()
    {
        var list = new List<PlanBlock>();
        foreach (var part in (Prefs.String(DoneKey) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = part.LastIndexOf(':');
            if (at <= 0 || !int.TryParse(part[(at + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) continue;
            if (PlanBlock.FromKey(part[..at], minutes) is { } b) list.Add(b);
        }
        return list;
    }

    private void SaveDone() => Prefs.Set(DoneKey, Done.Count == 0 ? null : string.Join(";", Done.Select(b => $"{b.Key}:{b.Minutes}")));

    /// <summary>The work due read from the engine (the calendar's next six weeks: assignments, quizzes and discussions
    /// not yet done), then the plan; read again only when asked (<paramref name="again"/>).</summary>
    public async void Load(Engine.Engine engine, bool again = false)
    {
        if (Loading) return;
        if (Items != null && !again) { Replan(); return; }
        var me = ++reading;
        Loading = true;
        Error = null;
        Raise();
        Shot.Began();
        try
        {
            var from = DateTime.Today;
            var to = from.AddDays(StudyPlan.HorizonDays);
            var d = await engine.Call<CalendarData>("calendar", new { from = Iso(from), to = Iso(to) });
            if (me != reading) return;
            if (d.Error is { Length: > 0 } err) throw new InvalidOperationException(err);
            Items = new List<PlanItem>();
            Colors.Clear();
            foreach (var ev in d.Events)
            {
                if (ev.Kind is not ("Assignment" or "Quiz" or "Discussion") || ev.Done == true || ev.Struck == true) continue;
                if (TodoWords.Date(ev.Date) is not { } due) continue;
                var parts = (ev.Sub ?? "").Split(" · ");
                var course = CourseCode(engine, parts.Length > 0 ? parts[0] : "");
                Items.Add(new PlanItem(ev.Id, ev.Title, course, StudyPlan.KindOf(ev.Kind, ev.Title), StudyPlan.PointsIn(ev.Sub), due));
                Colors[ev.Id] = K.Hex(ev.Color, K.Tone("gray"));
            }
            Loading = false;
            Replan();
        }
        catch (Exception e)
        {
            if (me != reading) return;
            Log.Write("plan: " + e.Message);
            Loading = false;
            Error = Items == null ? $"Your work could not be read ({e.Message.TrimEnd('.')})." : null;
            Raise();
        }
        finally
        {
            Shot.Ended();
        }
    }

    /// <summary>A moment as the page reads it (ISO 8601 in UTC), as the Calendar sends its span.</summary>
    private static string Iso(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A calendar's context ("F26-MATH 021 20") as the course's code the app shows elsewhere ("MATH 021"), where
    /// the engine knows the course; the context's own words otherwise.</summary>
    private static string CourseCode(Engine.Engine engine, string context)
    {
        if (context.Length == 0) return "Course";
        var c = engine.Courses.FirstOrDefault(x => string.Equals(x.Name, context, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Original, context, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Code, context, StringComparison.OrdinalIgnoreCase));
        return c?.Code is { Length: > 0 } code ? code : context;
    }

    public void Replan()
    {
        if (Items == null) return;
        var ids = Items.Select(i => i.Id).ToHashSet();
        // (a block done for work no longer due is forgotten with it)
        Done.RemoveAll(b => !ids.Contains(b.ItemId));
        Plan = StudyPlan.Make(Items, Settings, DateTime.Now, Done);
        Raise();
        Shot.Moved();
    }

    /// <summary>A block ticked (or unticked): kept as done where it was, and the rest planned round it.</summary>
    public void Mark(PlanBlock b, bool done)
    {
        Done.RemoveAll(x => x.Key == b.Key);
        if (done) Done.Add(b with { Done = true });
        SaveDone();
        Replan();
    }

    public void SetHours(int day, double hours)
    {
        var h = (double[])Settings.Hours.Clone();
        h[day] = Math.Clamp(hours, 0, 16);
        Settings = Settings with { Hours = h };
        Prefs.Set(HoursKey, StudyPlan.HoursText(h));
        Replan();
    }

    public void SetBlock(int minutes)
    {
        Settings = Settings with { BlockMinutes = minutes };
        Prefs.Set(BlockKey, (double)minutes);
        Replan();
    }

    public void SetTime(PlanTime t)
    {
        Settings = Settings with { Time = t };
        Prefs.Set(TimeKey, t.ToString().ToLowerInvariant());
        Replan();
    }

    public void SetFinish(int days)
    {
        Settings = Settings with { FinishDaysBefore = Math.Clamp(days, 0, 14) };
        Prefs.Set(FinishKey, (double)Settings.FinishDaysBefore);
        Replan();
    }

    public void ToggleCourse(string code, bool included)
    {
        var set = Settings.Excluded.ToHashSet();
        if (included) set.Remove(code); else set.Add(code);
        Settings = Settings with { Excluded = set };
        Prefs.Set(CoursesKey, set.Count == 0 ? null : string.Join(",", set));
        Replan();
    }

    public void SetView(string view)
    {
        View = view;
        Prefs.Set(ViewKey, view);
        Raise();
    }

    /// <summary>Add to Calendar: the blocks as a calendar file in Downloads, shown in File Explorer.</summary>
    public string? Export()
    {
        if (Plan == null || Items == null || Plan.Blocks.Count == 0) return null;
        try
        {
            var path = Files.Free("Study plan", "ics", Engine.FilePreview.Downloads);
            File.WriteAllText(path, StudyPlan.Ics(Plan.Blocks, Items.ToDictionary(i => i.Id), DateTime.Now), new UTF8Encoding(false));
            Files.Reveal(new[] { path });
            return path;
        }
        catch (Exception e)
        {
            Log.Write("plan export: " + e.Message);
            Error = "The calendar file could not be saved.";
            Raise();
            return null;
        }
    }

    /// <summary>The screenshot suite's start: --plan-hours 2 (every day), --plan-block 45, --plan-time evening,
    /// --plan-view list, and --plan-done "assignment_1015:2" (that work's first two blocks ticked once the plan is made).</summary>
    public void FromLaunch()
    {
        if (double.TryParse(Launch.Take("", "plan-hours"), NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
        {
            Settings = Settings with { Hours = Enumerable.Repeat(Math.Clamp(hours, 0, 16), 7).ToArray() };
            Prefs.Set(HoursKey, StudyPlan.HoursText(Settings.Hours));
        }
        if (int.TryParse(Launch.Take("", "plan-block"), out var block) && PlanSettings.Blocks.Contains(block)) SetBlock(block);
        if (Enum.TryParse<PlanTime>(Launch.Take("", "plan-time"), true, out var t)) SetTime(t);
        if (Launch.Take("", "plan-view") is { Length: > 0 } v) SetView(v == "list" ? "list" : "week");
        launchDone = Launch.Take("", "plan-done");
    }

    private string? launchDone;

    /// <summary>--plan-done, applied once the plan is first made.</summary>
    public void ApplyLaunchDone()
    {
        if (launchDone is not { } spec || Plan == null) return;
        launchDone = null;
        var at = spec.LastIndexOf(':');
        if (at <= 0 || !int.TryParse(spec[(at + 1)..], out var n)) return;
        var id = spec[..at];
        foreach (var b in Plan.Blocks.Where(b => b.ItemId == id && !b.Done).Take(n).ToList())
        {
            Done.RemoveAll(x => x.Key == b.Key);
            Done.Add(b with { Done = true });
        }
        SaveDone();
        Replan();
    }
}

/// <summary>
/// Study Planner (the Mac's StudyPlannerTool): the week (today first) or the list of blocks, each with its tick; beside
/// them how every piece of work stands, the settings, Regenerate and Add to Calendar.
/// </summary>
internal sealed class StudyPlannerTool : ContentControl
{
    private readonly ToolContext c;
    private readonly PlanState s = PlanState.Shared;
    private readonly Color tint = ToolsCenter.KindOf("plan")?.Color ?? K.Hex("#34c759");
    private string? said;

    public StudyPlannerTool(ToolContext c)
    {
        this.c = c;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        s.FromLaunch();
        Loaded += (_, _) => { s.Changed -= Render; s.Changed += Render; Render(); };
        Unloaded += (_, _) => s.Changed -= Render;
        s.Load(c.Engine);
        Render();
    }

    private void Render()
    {
        s.ApplyLaunchDone();
        FrameworkElement body;
        if (s.Plan == null)
        {
            body = s.Loading ? K.Reading("Reading what’s due…")
                : s.Error != null ? K.V(12, ToolKit.Note(s.Error), K.Button("Try again", () => s.Load(c.Engine, again: true), icon: SymbolRegular.ArrowClockwise24).Align(HorizontalAlignment.Left))
                : K.Wrapped("Nothing to plan yet.", Ty.Body, B.Text2);
            Content = FileKit.Page(Content as ScrollViewer, body);
            return;
        }
        var main = K.V(16, Switcher(), s.View == "list" ? ListView() : WeekView());
        var side = K.V(24, Work(), Settings(), Actions());
        Content = FileKit.Page(Content as ScrollViewer, ToolKit.Columns(main, side, 380, 860));
        if (Shot.Asked || Launch.IsDevelopment)
        {
            var p = s.Plan;
            var days = p.Blocks.Select(b => b.Start.Date).Distinct().Count();
            var now = $"plan: shows {p.Blocks.Count} blocks for {p.Progress.Count} items over {days} days ({s.View})";
            if (now != said) { said = now; Log.Write(now); }
        }
    }

    // ---- the views ----

    private FrameworkElement Switcher()
    {
        var bar = new SelectorBar(new[] { "Week", "List" }, s.View == "list" ? 1 : 0);
        bar.Changed += i => s.SetView(i == 1 ? "list" : "week");
        var p = s.Plan!;
        var week = p.Blocks.Where(b => b.Start.Date < p.Today.AddDays(7)).ToList();
        var words = K.Text(week.Count == 0 ? "Nothing planned this week." : $"{ToolKit.Plural(week.Count, "block")} · {StudyPlan.Duration(week.Sum(b => b.Minutes))} this week · {week.Count(b => b.Done)} done", Ty.Body, B.Text2);
        words.VerticalAlignment = VerticalAlignment.Center;
        return K.Columns("auto, *", bar.Col(0), words.Margin(16, 0, 0, 0).Col(1));
    }

    /// <summary>Seven days, today first, a column each (fewer across on a narrow page, the days flowing on).</summary>
    private FrameworkElement WeekView()
    {
        var p = s.Plan!;
        var columns = new List<UIElement>();
        for (var d = 0; d < 7; d++)
        {
            var day = p.Today.AddDays(d);
            var blocks = p.On(day).ToList();
            var minutes = blocks.Sum(b => b.Minutes);
            var head = K.V(0,
                K.Text(d == 0 ? "Today" : d == 1 ? "Tomorrow" : day.ToString("dddd", CultureInfo.InvariantCulture), Ty.BodyStrong, d == 0 ? B.AccentText : B.Text),
                K.Text($"{day.ToString("MMM d", CultureInfo.InvariantCulture)} · {StudyPlan.Duration(minutes)} of {StudyPlan.Duration((int)(s.Settings.HoursOn(day.DayOfWeek) * 60))}", Ty.Caption, B.Text2));
            var col = K.V(8, head);
            if (blocks.Count == 0) col.Children.Add(K.Wrapped("—", Ty.Body, B.Text3).Margin(0, 4, 0, 0));
            foreach (var b in blocks) col.Children.Add(BlockCard(b, compact: true));
            col.VerticalAlignment = VerticalAlignment.Top;
            columns.Add(col);
        }
        return new MoreGrid(150, 12, columns, stretch: true);
    }

    /// <summary>Every block in order, under its day.</summary>
    private FrameworkElement ListView()
    {
        var p = s.Plan!;
        var v = K.V(18);
        foreach (var day in p.Blocks.GroupBy(b => b.Start.Date).OrderBy(g => g.Key))
        {
            var apart = (int)(day.Key - p.Today).TotalDays;
            var name = apart == 0 ? "Today" : apart == 1 ? "Tomorrow" : day.Key.ToString("dddd", CultureInfo.InvariantCulture);
            var head = Heading.Section(name, $"{day.Key.ToString("MMM d", CultureInfo.InvariantCulture)} · {StudyPlan.Duration(day.Sum(b => b.Minutes))}", null, Ty.BodyStrong);
            var rows = K.V(0);
            foreach (var b in day.OrderBy(b => b.Start))
            {
                if (rows.Children.Count > 0) rows.Children.Add(K.Divider(52));
                rows.Children.Add(BlockRow(b));
            }
            v.Children.Add(K.V(0, head, K.Card(rows, new Thickness(0, 4, 0, 4))));
        }
        if (v.Children.Count == 0) v.Children.Add(K.Wrapped("Nothing is due in the next six weeks.", Ty.Body, B.Text2));
        return v;
    }

    private static string Clock(DateTime t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture).Replace("AM", "am").Replace("PM", "pm");

    /// <summary>A block in the week: its tick, its length and course, its work, its time; done, struck through.</summary>
    private FrameworkElement BlockCard(PlanBlock b, bool compact)
    {
        var it = s.ItemOf(b.ItemId);
        var color = s.Colors.GetValueOrDefault(b.ItemId, tint);
        var tick = new CheckCircle(b.Done, b.Done ? "Not done after all" : "Done");
        tick.Toggled += done => s.Mark(b, done);
        tick.VerticalAlignment = VerticalAlignment.Top;
        tick.Margin = new Thickness(0, 2, 8, 0);
        var line = K.Text($"{b.Minutes} min · {it?.Course ?? "Course"}", Ty.Caption, B.Text2);
        line.Foreground = K.Brush(K.Darkened(color));
        var title = K.Wrapped(it?.Title ?? "Study", Ty.BodyStrong, b.Done ? B.Text2 : B.Text, 2);
        if (b.Done) title.TextDecorations = TextDecorations.Strikethrough;
        var when = K.Text($"{Clock(b.Start)} – {Clock(b.End)}", Ty.Caption, B.Text2);
        var words = K.V(2, line, title, when);
        var card = K.Card(K.Columns("auto, *", tick.Col(0), words.Col(1)), new Thickness(10, 8, 10, 8));
        card.BorderBrush = K.Brush(K.WithAlpha(color, 0.5));
        System.Windows.Automation.AutomationProperties.SetName(card, $"{StudyPlan.Words(b, it)}, {Clock(b.Start)}{(b.Done ? ", done" : "")}");
        return card;
    }

    private FrameworkElement BlockRow(PlanBlock b)
    {
        var it = s.ItemOf(b.ItemId);
        var color = s.Colors.GetValueOrDefault(b.ItemId, tint);
        var tick = new CheckCircle(b.Done, b.Done ? "Not done after all" : "Done");
        tick.Toggled += done => s.Mark(b, done);
        var title = K.Text(StudyPlan.Words(b, it), Ty.Body, b.Done ? B.Text2 : B.Text);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        if (b.Done) title.TextDecorations = TextDecorations.Strikethrough;
        var due = K.Text(it == null ? "" : $"{StudyPlan.KindWord(it.Kind)} due {it.Due.ToString("ddd, MMM d", CultureInfo.InvariantCulture)}", Ty.Caption, B.Text2);
        var when = K.Text($"{Clock(b.Start)} – {Clock(b.End)}", Ty.Body, B.Text2);
        Typography.SetNumeralAlignment(when, FontNumeralAlignment.Tabular);
        var g = K.Columns("auto, auto, *, auto", tick.Margin(0, 0, 12, 0).Col(0), K.Dot(color, 8).Margin(0, 0, 10, 0).Col(1), K.V(1, title, due).Col(2), when.Margin(12, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3));
        g.Margin = new Thickness(16, 9, 16, 9);
        return g;
    }

    // ---- the side ----

    /// <summary>How every piece of work stands: what it needs, what is done, and what cannot be fitted before it is due.</summary>
    private FrameworkElement Work()
    {
        var p = s.Plan!;
        if (p.Progress.Count == 0) return Everyday.Section("Your work", K.Wrapped("Nothing is due in the next six weeks. Study blocks come from the assignments, quizzes and exams the Calendar shows.", Ty.Body, B.Text2));
        var rows = K.V(0);
        foreach (var pr in p.Progress)
        {
            if (rows.Children.Count > 0) rows.Children.Add(K.Divider(16));
            var color = s.Colors.GetValueOrDefault(pr.Item.Id, tint);
            var title = K.Text(pr.Item.Title, Ty.BodyStrong);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            var facts = K.Text($"{pr.Item.Course} · {StudyPlan.KindWord(pr.Item.Kind)} · due {pr.Item.Due.ToString("ddd, MMM d", CultureInfo.InvariantCulture)}", Ty.Caption, B.Text2);
            var done = K.Text($"{StudyPlan.Duration(pr.DoneMinutes)} of {StudyPlan.Duration(pr.NeedMinutes)} done", Ty.Caption, B.Text2);
            var v = K.V(4, K.Columns("auto, *", K.Dot(color, 8).Margin(0, 0, 8, 0).Col(0), title.Col(1)), facts, K.Bar(pr.Fraction, color).Margin(0, 2, 0, 2), done);
            if (pr.ShortMinutes > 0)
            {
                var warn = K.Wrapped($"{StudyPlan.Duration(pr.ShortMinutes)} won’t fit before it’s due — add hours or finish earlier.", Ty.Caption);
                warn.Foreground = new SolidColorBrush(FileKit.Orange);
                v.Children.Add(warn);
            }
            rows.Children.Add(new Border { Child = v, Padding = new Thickness(16, 10, 16, 10) });
        }
        return Everyday.Section("Your work", K.Card(rows, new Thickness(0, 4, 0, 4)), ToolKit.Plural(p.Progress.Count, "item"));
    }

    /// <summary>The settings: hours a day, the block, the time of day, the days to finish early, the courses.</summary>
    private FrameworkElement Settings()
    {
        var v = K.V(14);
        // hours a day: a row a weekday
        var grid = K.Columns("auto, 64, auto");
        string[] names = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        for (var i = 0; i < 7; i++)
        {
            var day = i;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = K.Text(names[i], Ty.Body, B.Text2);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, i == 0 ? 0 : 6, 12, 0);
            var box = ToolKit.Box(s.Settings.Hours[i].ToString("0.##", CultureInfo.InvariantCulture), "2");
            box.Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0);
            box.MinHeight = 30;
            System.Windows.Automation.AutomationProperties.SetName(box, $"Hours on {names[i]}");
            box.LostFocus += (_, _) => Hours(day, box.Text);
            box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; Hours(day, box.Text); } };
            var h = K.Text("h", Ty.Body, B.Text2).Margin(8, i == 0 ? 0 : 6, 0, 0);
            h.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(label.Row(i).Col(0));
            grid.Children.Add(box.Row(i).Col(1));
            grid.Children.Add(h.Row(i).Col(2));
        }
        v.Children.Add(K.V(6, K.Text("Hours a day", Ty.BodyStrong, B.Text2), grid));
        // the block
        var blocks = new SelectorBar(PlanSettings.Blocks.Select(b => $"{b} min").ToList(), Array.IndexOf(PlanSettings.Blocks, s.Settings.BlockMinutes));
        blocks.Changed += i => s.SetBlock(PlanSettings.Blocks[i]);
        v.Children.Add(K.V(6, K.Text("Block length", Ty.BodyStrong, B.Text2), blocks));
        // the time of day and the days to finish early
        var time = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 150 };
        foreach (var t in new[] { PlanTime.Morning, PlanTime.Afternoon, PlanTime.Evening }) time.Items.Add(Everyday.Item($"{t} (from {StudyPlan.StartHour(t) % 12}{(t == PlanTime.Morning ? " am" : " pm")})", t.ToString()));
        Everyday.Choose(time, s.Settings.Time.ToString());
        time.SelectionChanged += (_, _) => { if (Everyday.Chosen(time) is { } w && Enum.TryParse<PlanTime>(w, out var t) && t != s.Settings.Time) s.SetTime(t); };
        System.Windows.Automation.AutomationProperties.SetName(time, "Time of day");
        var finish = ToolKit.Box(s.Settings.FinishDaysBefore.ToString(CultureInfo.InvariantCulture), "1");
        finish.Width = 64;
        System.Windows.Automation.AutomationProperties.SetName(finish, "Days before the due date to finish");
        finish.LostFocus += (_, _) => Finish(finish.Text);
        finish.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; Finish(finish.Text); } };
        var finishRow = K.H(8, finish, K.Text(s.Settings.FinishDaysBefore == 1 ? "day before it’s due" : "days before it’s due", Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        v.Children.Add(K.V(6, K.Text("Time of day", Ty.BodyStrong, B.Text2), time));
        v.Children.Add(K.V(6, K.Text("Finish early", Ty.BodyStrong, B.Text2), finishRow));
        // the courses
        var courses = s.Courses;
        if (courses.Count > 0)
        {
            var list = K.V(4);
            foreach (var code in courses)
            {
                var cb = new CheckBox { Content = code, IsChecked = !s.Settings.Excluded.Contains(code) };
                var mine = code;
                cb.Checked += (_, _) => s.ToggleCourse(mine, true);
                cb.Unchecked += (_, _) => s.ToggleCourse(mine, false);
                list.Children.Add(cb);
            }
            v.Children.Add(K.V(6, K.Text("Courses", Ty.BodyStrong, B.Text2), list));
        }
        return Everyday.Section("Settings", K.Card(v, new Thickness(16)));
    }

    private void Hours(int day, string text)
    {
        if (double.TryParse(text.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && Math.Abs(h - s.Settings.Hours[day]) > 1e-9) s.SetHours(day, h);
    }

    private void Finish(string text)
    {
        if (int.TryParse(text, out var d) && d != s.Settings.FinishDaysBefore) s.SetFinish(d);
    }

    private FrameworkElement Actions()
    {
        var again = K.Button("Regenerate", () => s.Load(c.Engine, again: true), accent: true, icon: SymbolRegular.ArrowClockwise24);
        again.ToolTip = "Read what’s due again and plan the rest round the blocks you’ve done";
        var export = K.Button("Add to Calendar", () => s.Export(), icon: SymbolRegular.CalendarAdd24);
        export.IsEnabled = s.Plan is { Blocks.Count: > 0 };
        export.ToolTip = "Save the blocks as a calendar file (.ics) in Downloads, to open in your calendar app";
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { again, export }) { b.Margin = new Thickness(0, 0, 8, 8); row.Children.Add(b); }
        var v = K.V(6, row, FileKit.Foot("Planned on this PC from what the Calendar shows; nothing is written to Canvas."));
        if (s.Error != null) v.Children.Add(ToolKit.Note(s.Error));
        return v;
    }
}

/// <summary>Study Planner's pin, opened: today's blocks as a checklist.</summary>
internal sealed class StudyPlannerCompact
{
    private readonly PlanState s = PlanState.Shared;
    private readonly ContentControl host = StudyKit.Host();

    public FrameworkElement View { get; }

    public StudyPlannerCompact(ToolContext c)
    {
        View = K.V(10, K.Text("Today", Ty.BodyStrong, B.Text2), host);
        Everyday.Follow(View, () => { s.Changed += Render; Render(); }, () => s.Changed -= Render);
        s.Load(c.Engine);
        Render();
    }

    private void Render()
    {
        if (s.Plan == null)
        {
            StudyKit.Put(host, s.Loading ? K.Reading("Reading what’s due…") : K.Wrapped(s.Error ?? "Nothing to plan yet.", Ty.Body, B.Text2));
            return;
        }
        var today = s.Plan.On(s.Plan.Today).ToList();
        if (today.Count == 0) { StudyKit.Put(host, K.Wrapped("Nothing planned for today. Open the planner to see the week.", Ty.Body, B.Text2)); return; }
        var rows = K.V(0);
        foreach (var b in today)
        {
            if (rows.Children.Count > 0) rows.Children.Add(K.Divider(40));
            var it = s.ItemOf(b.ItemId);
            var tick = new CheckCircle(b.Done, b.Done ? "Not done after all" : "Done");
            tick.Toggled += done => s.Mark(b, done);
            var words = K.Wrapped(StudyPlan.Words(b, it), Ty.Body, b.Done ? B.Text2 : B.Text, 2);
            if (b.Done) words.TextDecorations = TextDecorations.Strikethrough;
            var g = K.Columns("auto, *", tick.Margin(0, 0, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), words.Col(1));
            g.Margin = new Thickness(8, 8, 8, 8);
            rows.Children.Add(g);
        }
        var minutes = today.Sum(b => b.Minutes);
        StudyKit.Put(host, K.V(8, K.Card(rows, new Thickness(0, 2, 0, 2)), K.Text($"{ToolKit.Plural(today.Count, "block")} · {StudyPlan.Duration(minutes)} · {today.Count(b => b.Done)} done", Ty.Caption, B.Text2)));
    }
}

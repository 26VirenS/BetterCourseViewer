using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace Simpl.Screens;

/// <summary>
/// The Calendar (the Mac's CalendarView), in Windows 11's look: a month as Windows' CalendarView draws one — rounded day
/// cells, today on the accent, the day picked outlined — each day's work and events on it in their calendar's colour, with
/// the day picked listed beside it (and a task of your own added to it); a week day by day; or the next three weeks as a
/// list. On the calendars chosen (Canvas shows ten at most). The chevrons round Today — or ← and → once the calendar has
/// been clicked — move a month or a week, sliding the way they go; Today (Ctrl+T) comes back. The list, always the three
/// weeks from today, does not page: it has none of them. An item clicked, on the grid, in the day, the week or the list,
/// opens its preview out of itself (<see cref="CalendarPeek"/>); a double-click opens it whole.
/// </summary>
public sealed partial class CalendarScreen : Screen
{
    private static readonly string[] Modes = { "month", "week", "list" };
    private static readonly string[] ModeNames = { "Month", "Week", "List" };
    /// <summary>The month with the day picked beside it (the Mac's 540 + 22 + 340), else the day under it.</summary>
    private const double MonthBeside = 902;
    /// <summary>The week as seven columns (the Mac's 770), else as seven days one under another.</summary>
    private const double WeekColumnsFrom = 770;

    private readonly Loader<CalendarData> cal;
    private readonly CalendarPeek peek;
    /// <summary>What the screen shows, kept from build to build: ← and → reach it once the calendar has been clicked.</summary>
    private readonly Grid page = new() { Focusable = true, FocusVisualStyle = null };
    /// <summary>Month, week or list, as the page keeps it (calView).</summary>
    private string mode = "month";
    private DateTime anchor, weekStart, selected;
    /// <summary>What was last read: shown while another month or week is read, and when reading it failed.</summary>
    private CalendarData? last;
    private bool modeRead, hidden;
    /// <summary>The way the last move went (the month slides in from that side).</summary>
    private int dir = 1;
    /// <summary>The span the last build showed: a build of another one slides it in.</summary>
    private string? shownSpan;
    private string? toldTitle, toldSub, shownNotice;
    private Window? hooked;
    private bool shotAsked = Launch.Get("open") == "preview";

    /// <summary>The screenshot suite's item to preview, by (part of) its name (--preview-of); null: the first on screen.</summary>
    internal static string? PreviewOf => Launch.Get("preview-of") is { Length: > 0 } w ? w : null;

    private bool previewDayPicked;
    private (CalEvent Ev, FrameworkElement At)? firstItem;

    /// <summary>The day the Calendar's New task asked a task for (the Mac's NewTaskSheet.startDay): New task's sheet takes it once.</summary>
    public static DateTime? NewTaskDay { get; set; }

    /// <summary>The day asked for, once (null when New task was asked from anywhere else).</summary>
    public static DateTime? TakeNewTaskDay()
    {
        var d = NewTaskDay;
        NewTaskDay = null;
        return d;
    }

    public CalendarScreen(Engine.Engine engine) : base(engine)
    {
        cal = Watch(new Loader<CalendarData>());
        peek = new CalendarPeek(engine);
        var today = DateTime.Today;
        anchor = MonthStart(today);
        weekStart = FirstOfWeek(today);
        selected = today;
        // the view kept last time at once (the page's answer follows); the screenshot suite's own (--cal-view week)
        var v = Launch.Get("cal-view") ?? engine.Kept<OK>("calView")?.View;
        if (v != null && Modes.Contains(v)) mode = v;
        KeyboardNavigation.SetIsTabStop(page, false);
        page.KeyDown += OnKey;
        page.PreviewMouseDown += (_, _) => { if (!page.IsKeyboardFocusWithin) page.Focus(); };
        // (the page taking the keyboard is no reason to scroll to its top)
        page.RequestBringIntoView += (_, e) => { if (e.OriginalSource == page) e.Handled = true; };
        page.Loaded += (_, _) => HookWindow();
    }

    private static CultureInfo Culture => CalendarKit.Culture;

    public override string Title => PeriodTitle;

    /// <summary>(1.2) How much the month or the week holds and how much of it is missing ("14 items · 2 missing"); the list's span.</summary>
    public override string? Subtitle => (cal.Data ?? last) is { } d ? PeriodLine(d) : null;

    public override void Refresh()
    {
        hidden = false;
        if (!modeRead)
        {
            modeRead = true;
            ReadMode();
        }
        cal.Ensure(engine, "calendar", Args());
    }

    public override void Hide()
    {
        base.Hide();
        hidden = true; // (a quick look stays over the place opened instead, until it is closed)
    }

    /// <summary>Read ahead, unseen: the month (or week, or list) it would show, nothing else.</summary>
    protected override void ReadAhead()
    {
        if (!modeRead)
        {
            modeRead = true;
            ReadMode();
        }
        cal.Ensure(engine, "calendar", Args());
    }

    public override void Release()
    {
        base.Release();
        if (hooked != null) { hooked.PreviewKeyDown -= WindowKey; hooked = null; }
    }

    public override bool Escape()
    {
        if (!peek.IsOpen) return false;
        peek.Close();
        return true;
    }

    protected override bool LayoutChanges(double before, double now) =>
        (before >= MonthBeside) != (now >= MonthBeside) || (before >= WeekColumnsFrom) != (now >= WeekColumnsFrom)
        || (before >= TodoBesideFrom) != (now >= TodoBesideFrom);

    // ---- the commands (the Mac's toolbar) ----

    private FrameworkElement? commands;
    private SelectorBar? selector;
    private Button? prevButton, nextButton;
    private FrameworkElement? pager;
    private ProgressRing? spinner;

    /// <summary>
    /// Beside the title: a spinner while the page is asked, Month · Week · List (Windows 11's SelectorBar), the chevrons
    /// round Today (a month's or a week's only), the calendars shown, and More (Open in Canvas, Copy link).
    /// </summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (commands != null) return commands;
            spinner = K.Spinner(16);
            spinner.Visibility = Visibility.Hidden;
            selector = new SelectorBar(ModeNames, Array.IndexOf(Modes, mode)) { ToolTip = "A month, a week, or the next three weeks", VerticalAlignment = VerticalAlignment.Center };
            selector.Changed += i => SetMode(Modes[i]);
            prevButton = K.IconButton(SymbolRegular.ChevronLeft24, () => Shift(-1), "Previous month", 16, 32);
            nextButton = K.IconButton(SymbolRegular.ChevronRight24, () => Shift(1), "Next month", 16, 32);
            var today = K.Button("Today", GoToday);
            today.MinWidth = 0;
            today.ToolTip = "Today (Ctrl+T)";
            var nav = K.H(4, prevButton, today, nextButton);
            nav.VerticalAlignment = VerticalAlignment.Center;
            pager = nav;
            var calendars = K.IconButton(SymbolRegular.CalendarCheckmark24, ChooseCalendars, "Choose the calendars shown", 16, 32);
            System.Windows.Automation.AutomationProperties.SetName(calendars, "Calendars");
            // (0.3.53) Canvas's Scheduler: office hours and sign-ups with a time to reserve
            var finder = K.IconButton(SymbolRegular.CalendarClock24, FindAppointment, "Find Appointment", 16, 32);
            // (0.3.60) Sync: (0.3.63) Simpl's own calendar into a calendar app, and the student's own calendars in here
            var sync = K.IconButton(SymbolRegular.ArrowSync24, OpenSync, "Simpl in Outlook, Google or your calendar app, and your own calendars here", 16, 32);
            // (0.3.63) To Do beside the week, shown or hidden (the week alone has it)
            todoButton = K.IconButton(SymbolRegular.PanelLeft24, ToggleTodo, "Hide To Do beside the week", 16, 32);
            System.Windows.Automation.AutomationProperties.SetName(sync, "Sync");
            Button? more = null;
            more = K.IconButton(SymbolRegular.MoreHorizontal24, () => MoreMenu(more!), "More", 16, 32);
            foreach (var e in new FrameworkElement[] { spinner, today, todoButton, finder, sync, calendars, more }) e.VerticalAlignment = VerticalAlignment.Center;
            commands = K.H(8, spinner, selector, nav, todoButton, finder, sync, calendars, more);
            PaintCommands();
            return commands;
        }
    }

    /// <summary>The commands as things are now: the chevrons' words for a month or a week — and for the list, which does
    /// not page, no chevrons and no Today (it always starts today) — the spinner.</summary>
    private void PaintCommands()
    {
        if (commands == null) return;
        var week = mode == "week";
        void Words(Button? b, string name, string tip)
        {
            if (b == null) return;
            b.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(b, name);
        }
        Words(prevButton, week ? "Previous week" : "Previous month", week ? "Previous week (←)" : "Previous month (←)");
        Words(nextButton, week ? "Next week" : "Next month", week ? "Next week (→)" : "Next month (→)");
        if (pager != null) pager.Visibility = mode == "list" ? Visibility.Collapsed : Visibility.Visible;
        if (todoButton != null)
        {
            todoButton.Visibility = week ? Visibility.Visible : Visibility.Collapsed;
            Words(todoButton, TodoOn ? "Hide To Do" : "Show To Do", TodoOn ? "Hide To Do beside the week" : "Show To Do beside the week, to drag work onto a time");
        }
        if (spinner != null) spinner.Visibility = cal.Loading ? Visibility.Visible : Visibility.Hidden;
        selector?.Select(Array.IndexOf(Modes, mode), false);
    }

    /// <summary>The Mac's CanvasMenu: the calendar on the school's own site, and its link.</summary>
    private void MoreMenu(FrameworkElement at)
    {
        var menu = new ContextMenu { PlacementTarget = at, Placement = PlacementMode.Bottom };
        menu.Items.Add(CalendarKit.Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen("/calendar", "Calendar")));
        menu.Items.Add(CalendarKit.Item("Copy link", SymbolRegular.Link24, () => CalendarKit.Copy(engine.Absolute("/calendar"))));
        menu.IsOpen = true;
    }

    /// <summary>The calendars shown (the Mac's popover), as a dialog over the window; each change read again at once.</summary>
    private void ChooseCalendars() =>
        CalendarsSheet.Show(engine, (cal.Data ?? last)?.Calendars ?? new List<CalendarChoice>(), Read, Window.GetWindow(page));

    /// <summary>(0.3.53) Find Appointment (the Mac's popup): a time reserved or given back there read again behind it.</summary>
    private void FindAppointment() => AppointmentsSheet.Show(engine, Read, Window.GetWindow(page));

    /// <summary>(0.3.60) Sync (the Mac's popup): a calendar linked, shown, hidden or unlinked there read again behind it.</summary>
    private void OpenSync() => SyncSheet.Show(engine, Read, Window.GetWindow(page));

    /// <summary>(0.3.53) A reserved time given back from its menu, the calendar read again after.</summary>
    private async Task GiveBack(string reservation)
    {
        if (await engine.Act("cancelReservation", new { id = reservation })) Read();
    }

    // ---- the page ----

    protected override FrameworkElement Build()
    {
        var hadFocus = page.IsKeyboardFocusWithin;
        if (cal.Data != null) last = cal.Data;
        var d = last;
        Header();
        PaintCommands();
        firstItem = null;
        page.Children.Clear();
        if (d == null)
        {
            shownSpan = null;
            page.Children.Add(K.LoadState(cal.Error, Read));
            return page;
        }
        var span = $"{mode}|{(mode == "week" ? weekStart : anchor):yyyy-MM-dd}";
        var slide = mode != "list" && shownSpan != null && shownSpan != span && shownSpan.StartsWith(mode + "|", StringComparison.Ordinal);
        var modeChanged = shownSpan != null && !shownSpan.StartsWith(mode + "|", StringComparison.Ordinal);
        shownSpan = span;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 22 };
        if (Notice(d) is { } n) v.Children.Add(NoticeBar(n.Text, n.Problem));
        else shownNotice = null;
        var byDay = d.Events.GroupBy(e => e.Day).ToDictionary(g => g.Key, g => g.ToList());
        // (the screenshot suite: --preview-of Lec01-PreQuiz picks that item's day, so its row is in the day's panel — a
        //  graded quiz is folded into "1 done" on the month's grid — and its preview is the one opened)
        if (PreviewOf is { } want && !previewDayPicked && d.Events.FirstOrDefault(e => e.Title.Contains(want, StringComparison.OrdinalIgnoreCase)) is { } pe
            && DateTime.TryParseExact(pe.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var peDay))
        {
            previewDayPicked = true;
            selected = peDay;
        }
        var content = mode switch
        {
            "week" => WeekView(byDay, slide),
            "list" => ListView(byDay),
            _ => MonthView(byDay, slide),
        };
        v.Children.Add(content);
        if (modeChanged) Motion.FadeIn(content, Motion.Medium);
        page.Children.Add(v);
        if (hadFocus) page.Dispatcher.BeginInvoke(() => { if (!page.IsKeyboardFocusWithin) page.Focus(); }, DispatcherPriority.Input);
        PreviewShot();
        return page;
    }

    /// <summary>The title and the line under it, told to the shell when they change.</summary>
    private void Header()
    {
        var t = Title;
        var s = Subtitle;
        if (t == toldTitle && s == toldSub) return;
        toldTitle = t;
        toldSub = s;
        HeaderChange();
    }

    /// <summary>"October 2026", "Upcoming"; (0.3.62) a week named by its month, as a calendar app names one ("October
    /// 2026", "Oct – Nov 2026"): the title as wide in every view, its days in the line under it.</summary>
    private string PeriodTitle => mode switch
    {
        "list" => "Upcoming",
        "week" => WeekMonths(weekStart, weekStart.AddDays(6)),
        _ => anchor.ToString("MMMM yyyy", Culture),
    };

    private static string WeekMonths(DateTime a, DateTime b)
    {
        if (a.Month == b.Month && a.Year == b.Year) return a.ToString("MMMM yyyy", Culture);
        if (a.Year == b.Year) return $"{a.ToString("MMM", Culture)} – {b.ToString("MMM yyyy", Culture)}";
        return $"{a.ToString("MMM yyyy", Culture)} – {b.ToString("MMM yyyy", Culture)}";
    }

    /// <summary>A week's days, without their year: "Oct 4 – 10", "Sep 28 – Oct 4".</summary>
    private static string WeekDays(DateTime a, DateTime b) =>
        a.Month == b.Month ? $"{a.ToString("MMM d", Culture)} – {b.Day}" : $"{a.ToString("MMM d", Culture)} – {b.ToString("MMM d", Culture)}";

    private string PeriodLine(CalendarData d)
    {
        if (mode == "list") return "The next three weeks";
        // (0.3.62) the week's days before what it holds ("Oct 25 – 31 · 16 items")
        if (mode == "week") return $"{WeekDays(weekStart, weekStart.AddDays(6))} · {PeriodCount(d)}";
        return PeriodCount(d);
    }

    private string PeriodCount(CalendarData d)
    {
        var month = anchor.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var shown = mode == "month" ? d.Events.Where(e => e.Day.StartsWith(month, StringComparison.Ordinal)).ToList() : d.Events;
        if (shown.Count == 0) return mode == "month" ? "Nothing this month" : "Nothing this week";
        var missing = shown.Count(e => e.Missing == true);
        var line = $"{shown.Count} {(shown.Count == 1 ? "item" : "items")}";
        return missing > 0 ? $"{line} · {missing} missing" : line;
    }

    // ---- the notice ----

    /// <summary>What the page says over the calendar (no calendar chosen), or why it could not be read.</summary>
    private (string Text, bool Problem)? Notice(CalendarData d)
    {
        if (cal.Data == null && cal.Error is { Length: > 0 } failure) return (failure, true);
        if (d.Error is { Length: > 0 } e) return (e, true);
        if (d.Notice is { Length: > 0 } n) return (n, false);
        return null;
    }

    /// <summary>Windows 11's InfoBar: a warning with Try again, or a word with Choose calendars….</summary>
    private FrameworkElement NoticeBar(string text, bool problem)
    {
        var icon = K.Icon(problem ? SymbolRegular.Warning24 : SymbolRegular.Info24, 16, problem ? B.Caution : B.Attention, filled: true);
        var words = K.Wrapped(text, Ty.Body, B.Text);
        words.VerticalAlignment = VerticalAlignment.Center;
        var act = problem ? K.Button("Try again", Read) : K.Button("Choose calendars…", ChooseCalendars);
        act.VerticalAlignment = VerticalAlignment.Center;
        var g = K.Columns("auto, *, auto", icon.Margin(0, 2, 12, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), words.Col(1), act.Margin(12, 0, 0, 0).Col(2));
        var bar = K.Card(g, new Thickness(16, 10, 12, 10), 4);
        bar.Bg(problem ? B.CautionBack : B.AttentionBack);
        // (it fades in as it comes, not again as the calendar is drawn again under it)
        if (text != shownNotice) Motion.FadeIn(bar);
        shownNotice = text;
        return bar;
    }

    // ---- moving ----

    private void SetMode(string v)
    {
        if (v == mode) return;
        peek.Dismiss();
        // (the week or the month of the day picked)
        if (v == "week") weekStart = FirstOfWeek(selected);
        if (v == "month") anchor = MonthStart(selected);
        mode = v;
        FocusPage();
        Render();
        _ = SaveMode(v);
    }

    /// <summary>The view kept by the page, then read for it.</summary>
    private async Task SaveMode(string v)
    {
        await engine.Act("calView", new { view = v });
        if (mode == v) Read();
    }

    /// <summary>The view the page keeps, asked once (the screen opens on the one kept from last time meanwhile).</summary>
    private async void ReadMode()
    {
        if (Launch.Get("cal-view") is { } forced && Modes.Contains(forced)) return;
        try
        {
            var v = (await engine.Call<OK>("calView")).View;
            if (v == null || !Modes.Contains(v) || v == mode) return;
            mode = v;
            cal.Ensure(engine, "calendar", Args());
            PaintCommands();
            Render();
        }
        catch { /* the view shown stays */ }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || mode == "list") return;
        if (e.Key == Key.Left) { Shift(-1); e.Handled = true; }
        else if (e.Key == Key.Right) { Shift(1); e.Handled = true; }
    }

    /// <summary>Ctrl+T, wherever the keyboard is, while the Calendar shows (the Mac's ⌘T).</summary>
    private void HookWindow()
    {
        var w = Window.GetWindow(page);
        if (w == null || w == hooked) return;
        if (hooked != null) hooked.PreviewKeyDown -= WindowKey;
        hooked = w;
        w.PreviewKeyDown += WindowKey;
    }

    private void WindowKey(object sender, KeyEventArgs e)
    {
        if (hidden || !page.IsVisible || (sender as ISheetWindow)?.Sheets.IsOpen == true) return;
        if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
        {
            GoToday();
            e.Handled = true;
        }
    }

    private void FocusPage()
    {
        if (!page.IsKeyboardFocusWithin) page.Focus();
    }

    /// <summary>A month or a week on or back; the day picked becomes today when it is in it, else its first day.</summary>
    private void Shift(int way)
    {
        if (mode == "list") return;
        var today = DateTime.Today;
        Move(way, () =>
        {
            if (mode == "week")
            {
                var next = weekStart.AddDays(7 * way);
                weekStart = next;
                selected = today >= next && today < next.AddDays(7) ? today : next;
            }
            else
            {
                var next = anchor.AddMonths(way);
                anchor = next;
                selected = today.Year == next.Year && today.Month == next.Month ? today : next;
            }
        });
    }

    private void GoToday()
    {
        var today = DateTime.Today;
        var month = MonthStart(today);
        var week = FirstOfWeek(today);
        var here = mode == "week" ? weekStart : anchor;
        var there = mode == "week" ? week : month;
        if (here == there)
        {
            FocusPage();
            selected = today;
            Render();
            return;
        }
        Move(there < here ? -1 : 1, () =>
        {
            anchor = month;
            weekStart = week;
            selected = today;
        });
    }

    /// <summary>A day picked: one of the month before or after brings its month over, the way it lies.</summary>
    private void Pick(DateTime day)
    {
        var month = MonthStart(day);
        if (mode == "month" && month != anchor)
        {
            Move(month < anchor ? -1 : 1, () =>
            {
                anchor = month;
                selected = day;
            });
            return;
        }
        FocusPage();
        selected = day;
        Render();
    }

    /// <summary>A move to another month or week, sliding in from the side it went, then read afresh.</summary>
    private void Move(int way, Action change)
    {
        peek.Dismiss();
        dir = way;
        change();
        FocusPage();
        Read();
        Render();
    }

    // ---- reading ----

    /// <summary>The span shown: the weeks the month touches, the week, or three weeks from today.</summary>
    private (DateTime From, DateTime To) Range
    {
        get
        {
            switch (mode)
            {
                case "week":
                    return (weekStart, weekStart.AddDays(7));
                case "list":
                    var s = DateTime.Today;
                    return (s, s.AddDays(21));
                default:
                    var first = FirstOfWeek(anchor);
                    return (first, first.AddDays(7 * WeekCount(anchor)));
            }
        }
    }

    private object Args()
    {
        var (s, e) = Range;
        return new { from = Iso(s), to = Iso(e) };
    }

    /// <summary>A moment as the page reads it (ISO 8601, the Mac's ISO8601DateFormatter): local midnight in UTC.</summary>
    private static string Iso(DateTime local) =>
        DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>The span read again now (a move, a calendar turned on, Try again), the spinner up meanwhile.</summary>
    private void Read()
    {
        cal.Reload(engine, "calendar", Args());
        PaintCommands();
    }

    // ---- days ----

    private static DateTime MonthStart(DateTime d) => new(d.Year, d.Month, 1);

    /// <summary>The first day of the week a day is in, as Windows is set (a Sunday, a Monday …).</summary>
    private static DateTime FirstOfWeek(DateTime d)
    {
        var s = d.Date;
        var back = ((int)s.DayOfWeek - (int)CalendarKit.FirstDayOfWeek + 7) % 7;
        return s.AddDays(-back);
    }

    /// <summary>How many weeks a month touches, counted from the first day of the week it starts in: five or six (four for
    /// a February that starts on the week's first day). A row of the next month's days alone is never drawn.</summary>
    internal static int WeekCount(DateTime month)
    {
        var first = FirstOfWeek(month);
        var days = (int)(month.AddMonths(1) - first).TotalDays;
        return Math.Clamp((days + 6) / 7, 4, 6);
    }

    /// <summary>A day as the page names it ("2026-10-08").</summary>
    private static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<CalEvent> EventsOn(Dictionary<string, List<CalEvent>> byDay, DateTime day) =>
        byDay.TryGetValue(DayKey(day), out var l) ? l : new List<CalEvent>();

    /// <summary>The days of the week in Windows' order ("SUN MON …" or "MON TUE …").</summary>
    private static string[] WeekdayNames()
    {
        var names = Culture.DateTimeFormat.AbbreviatedDayNames;
        var first = (int)CalendarKit.FirstDayOfWeek;
        return Enumerable.Range(0, 7).Select(i => names[(first + i) % 7]).ToArray();
    }

    /// <summary>"Today · 3 items", "Tomorrow · 1 item", "2 items".</summary>
    private static string CountLine(DateTime day, int n)
    {
        var today = DateTime.Today;
        var rel = day.Date == today ? "Today · " : day.Date == today.AddDays(1) ? "Tomorrow · " : "";
        return $"{rel}{n} {(n == 1 ? "item" : "items")}";
    }

    /// <summary>"Wednesday, October 8", for a screen reader.</summary>
    private static string LongDay(DateTime day) => day.ToString("dddd, MMMM d", Culture);

    private static string Spoken(DateTime day, int count) =>
        count == 0 ? $"{LongDay(day)}, nothing" : $"{LongDay(day)}, {count} {(count == 1 ? "item" : "items")}";

    /// <summary>(1.2) A task of your own for a day: New task's sheet, opened on that day.</summary>
    private void NewTask(DateTime day)
    {
        NewTaskDay = day.Date;
        engine.AskNewTask();
    }

    /// <summary>
    /// (0.3.62) The screenshot suite's plan on the week (--open cal-plan): the first piece of work still to do planned at
    /// its first free time, then moved an hour on by the page's planMove (what letting go of a dragged block asks), and the
    /// week read again — its block pictured where the move put it; the log says both steps.
    /// </summary>
    private async Task ShotPlanMoved()
    {
        Shot.Began();
        try
        {
            var todo = await engine.Call<TodoData>("todo");
            if (todo.Sections.SelectMany(s => s.Rows).FirstOrDefault(r => !r.Done && r.Custom != true) is not { } row) { Log.Write("calendar: no work to plan"); return; }
            var slots = await engine.Call<PlanSlots>("planSlots", new { id = row.Id, mins = 60 });
            if (slots.Slots is not { Count: > 0 } list || CalendarKit.Date(list[0].At) is not { } at) { Log.Write("calendar: no free time to plan"); return; }
            await engine.Call<OK>("planPut", new { id = row.Id, at = list[0].At, mins = 60 });
            var moved = at.AddHours(1);
            var ok = await engine.Act("planMove", new { id = row.Id, at = moved.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) });
            Log.Write($"calendar: plan put {at:ddd h:mm tt}, {(ok ? "moved" : "not moved")} to {moved:ddd h:mm tt}");
            Read();
            await Task.Delay(2500);
        }
        catch (Exception e) { Log.Write("calendar: plan " + e.Message); }
        finally { Shot.Ended(); }
    }

    /// <summary>The screenshot suite's preview (--open preview): the first item on screen opens its preview, once.</summary>
    private void PreviewShot()
    {
        // (0.3.53) the screenshot suite's Find Appointment (--open appointments), over the calendar once it is drawn
        if (cal.Data != null && !cal.Loading && Launch.Take("appointments") != null)
            page.Dispatcher.BeginInvoke(FindAppointment, DispatcherPriority.Background);
        // (0.3.63) and its Sync with Simpl's calendar made as it opens (--open sync-simpl), against the mock's /api/feed
        if (cal.Data != null && !cal.Loading && Launch.Take("sync-simpl") != null)
            page.Dispatcher.BeginInvoke(() => SyncSheet.Show(engine, Read, Window.GetWindow(page), make: true), DispatcherPriority.Background);
        // (0.3.60) and its Sync (--open sync)
        if (cal.Data != null && !cal.Loading && Launch.Take("sync") != null)
            page.Dispatcher.BeginInvoke(OpenSync, DispatcherPriority.Background);
        // (0.3.62) and a plan on the week, moved an hour on as a drag moves one (--open cal-plan)
        if (cal.Data != null && !cal.Loading && Launch.Take("cal-plan") != null) _ = ShotPlanMoved();
        if (!shotAsked || cal.Data == null || cal.Loading || firstItem == null) return;
        shotAsked = false;
        if (Launch.Take("preview") == null) return;
        _ = Later();

        async Task Later()
        {
            await Task.Delay(1200);
            if (hidden || firstItem is not { } f || !f.At.IsLoaded) return;
            peek.Open(f.Ev, f.At);
        }
    }
}

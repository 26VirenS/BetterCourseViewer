using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

public sealed partial class CalendarScreen
{
    /// <summary>A day of the month's height in a month of six weeks (the Mac's 112, with room for four of Windows' caption
    /// lines); the grid is always six of these tall, so a month of five weeks has taller days, and more of each name fits.</summary>
    private const double CellHeight = 120;

    /// <summary>A line of an item on the month's grid: a caption line (16) and its padding, and the gap under it.</summary>
    private const double ChipLine = 20;

    /// <summary>The shortest a day of the month is drawn (its number and three lines), however short the window: the page
    /// scrolls below that.</summary>
    private const double MinCell = 96;

    /// <summary>Room kept under the month's last week, inside the page's view.</summary>
    private const double MonthFoot = 12;

    // ---- Month ----

    /// <summary>The month's grid with the day picked beside it on a wide page, under it on a narrow one.</summary>
    private FrameworkElement MonthView(Dictionary<string, List<CalEvent>> byDay, bool slide)
    {
        var grid = MonthGrid(byDay, slide);
        var panel = DayPanel(selected, EventsOn(byDay, selected));
        if (Width >= MonthBeside) return K.Columns("*, 22, 340", grid.Col(0), panel.Col(2));
        return K.V(22, grid, panel);
    }

    /// <summary>
    /// The days of the week over the weeks the month touches (five or six) from the first day of the week it starts in —
    /// the grid always as tall, its days taller when there are fewer weeks — each a rounded cell as Windows' CalendarView
    /// has them; a new month slides in from the side the move went.
    /// </summary>
    private FrameworkElement MonthGrid(Dictionary<string, List<CalEvent>> byDay, bool slide)
    {
        var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        var names = WeekdayNames();
        for (var i = 0; i < 7; i++)
        {
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var t = K.Text(names[i].ToUpper(Culture), Ty.Caption, B.Text2).Strong();
            t.Margin = new Thickness(8, 0, 4, 0);
            head.Children.Add(t.Col(i));
        }
        var days = new Grid();
        var weeks = WeekCount(anchor);
        // (at most a six-week month's height; less where the page shows less, so the month's last week is in view, its
        //  days never shorter than MinCell)
        var most = Math.Floor(6 * CellHeight / weeks);
        var height = most;
        for (var i = 0; i < 7; i++) days.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var w = 0; w < weeks; w++) days.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        var first = FirstOfWeek(anchor);
        void Cells()
        {
            days.Children.Clear();
            for (var i = 0; i < weeks * 7; i++)
            {
                var day = first.AddDays(i);
                days.Children.Add(DayCell(day, EventsOn(byDay, day), height).Col(i % 7).Row(i / 7));
            }
        }
        Cells();
        // (the days as tall as the page shows from the grid's top down, measured on the page's own scroller — and again as
        //  the window's height changes)
        ScrollViewer? page = null;
        void Fit()
        {
            if (page is not { ViewportHeight: > 0, Content: UIElement content } || !days.IsLoaded) return;
            double top;
            try { top = days.TranslatePoint(new Point(0, 0), content).Y; }
            catch (InvalidOperationException) { return; }
            var h = Math.Clamp(Math.Floor((page.ViewportHeight - top - MonthFoot) / weeks), MinCell, most);
            if (Math.Abs(h - height) < 1) return;
            height = h;
            foreach (var r in days.RowDefinitions) r.Height = new GridLength(h);
            // (the grid's first item, for the screenshot suite's preview, is the one drawn now)
            if (firstItem is { } f && days.IsAncestorOf(f.At)) firstItem = null;
            Cells();
        }
        void Sized(object? s, SizeChangedEventArgs e) { if (e.HeightChanged) Fit(); }
        days.Loaded += (_, _) =>
        {
            for (DependencyObject? d = days; d != null; d = VisualTreeHelper.GetParent(d)) if (d is ScrollViewer sv && !ReferenceEquals(sv, days)) { page = sv; break; }
            if (page == null) return;
            page.SizeChanged -= Sized;
            page.SizeChanged += Sized;
            days.Dispatcher.BeginInvoke(Fit, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        days.Unloaded += (_, _) => { if (page != null) page.SizeChanged -= Sized; };
        // (the cells' own gaps at the grid's edges let go, so it lines up with what is under it; a month sliding in is
        // seen inside it only)
        var clip = new Border { Child = days, ClipToBounds = true, Margin = new Thickness(-2) };
        if (slide) Motion.SlideIn(days, dir > 0);
        return K.V(0, head, clip);
    }

    /// <summary>
    /// A day of the month: its number (today on the accent; the day picked outlined in it), and its items as lines in their
    /// calendar's colour — (1.2) flat in the cell, no box round each. The open ones first, as many as the day has room
    /// for, a name taking a second line while there is room for it; work handed in folded into one line ("3 done", with
    /// what did not fit: "2 more · 3 done"), so the names still to do get the room. An item clicked opens its preview;
    /// the rest of the day picks the day.
    /// </summary>
    private FrameworkElement DayCell(DateTime day, List<CalEvent> evs, double height)
    {
        var off = day.Month != anchor.Month || day.Year != anchor.Year;
        var isToday = day == DateTime.Today;
        var isSel = day == selected;
        var open = evs.Where(e => !CalendarKit.IsStruck(e)).ToList();
        var done = evs.Count - open.Count;
        // (the lines under the day's number: four in a day of 120, five in one of 144)
        var lineCount = Math.Max(2, (int)Math.Floor((height - 40 + 2) / ChipLine));
        var tail = done > 0 || open.Count > lineCount;
        var room = lineCount - (tail ? 1 : 0);
        var shown = open.Take(room).ToList();
        var spare = room - shown.Count;
        var hidden = open.Count - shown.Count;
        var num = K.Text(day.Day.ToString(Culture), Ty.Body, isToday ? B.OnAccent : isSel ? B.AccentText : off ? B.Text3 : B.Text);
        num.FontWeight = isToday || isSel ? FontWeights.SemiBold : FontWeights.Normal;
        num.HorizontalAlignment = HorizontalAlignment.Center;
        num.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Grid { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Left };
        if (isToday) badge.Children.Add(new Ellipse().Bg(B.Accent));
        badge.Children.Add(num);
        var lines = new Stack { Orientation = Orientation.Vertical, Spacing = 2 };
        for (var i = 0; i < shown.Count; i++) lines.Children.Add(Chip(shown[i], i < spare ? 2 : 1));
        if (tail)
        {
            var rest = string.Join(" · ", new[] { hidden > 0 ? $"{hidden} more" : null, done > 0 ? $"{done} done" : null }.Where(x => x != null));
            var more = K.Text(rest, Ty.Caption, B.Text2);
            more.FontWeight = FontWeights.Medium;
            more.TextTrimming = TextTrimming.CharacterEllipsis;
            lines.Children.Add(more.Margin(12, 0, 0, 0));
        }
        if (off) lines.Opacity = 0.6;
        var b = K.CardButton(K.V(2, badge, lines), () => Pick(day), 6, new Thickness(5, 4, 5, 4));
        b.VerticalContentAlignment = VerticalAlignment.Top;
        b.Margin = new Thickness(2);
        if (off) b.SetResourceReference(Control.BackgroundProperty, B.Card2);
        AutomationProperties.SetName(b, Spoken(day, evs.Count));
        if (isSel) AutomationProperties.SetItemStatus(b, "Selected");
        if (!isSel) return b;
        var ring = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), Margin = new Thickness(2), IsHitTestVisible = false };
        ring.SetResourceReference(Border.BorderBrushProperty, B.Accent);
        return new Grid { Children = { b, ring } };
    }

    /// <summary>The Mac's CalendarChip: its name after a thin bar in its calendar's colour — a red mark when it is missing;
    /// greyed once over, struck through only once handed in (or excused). The name may take <paramref name="lines"/>
    /// lines.</summary>
    private FrameworkElement Chip(CalEvent ev, int lines = 1)
    {
        var missing = ev.Missing == true;
        var grey = CalendarKit.IsPast(ev) && !missing;
        // (0.3.60) a plan has its clock for a mark; a linked calendar's event is quieter than Canvas's own
        var quiet = grey || CalendarKit.IsLinked(ev);
        FrameworkElement mark = missing
            ? (FrameworkElement)K.Icon(SymbolRegular.ErrorCircle24, 10, B.Critical, filled: true).Margin(0, 3, 4, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top)
            : CalendarKit.IsPlan(ev)
                ? K.Icon(SymbolRegular.Clock24, 10, K.WithAlpha(K.Hex(ev.Color), grey ? 0.55 : 1)).Margin(0, 3, 4, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top)
                : new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 2, 6, 2), Background = K.Brush(K.WithAlpha(K.Hex(ev.Color), quiet ? 0.55 : 1)) };
        var title = lines > 1 ? K.Wrapped(ev.Title, Ty.Caption, quiet ? B.Text2 : missing ? B.Critical : B.Text, lines) : K.Text(ev.Title, Ty.Caption, quiet ? B.Text2 : missing ? B.Critical : B.Text);
        if (lines <= 1) title.TextTrimming = TextTrimming.CharacterEllipsis;
        // (two lines broken between words only: a word too long for the line ends in "…", never split — "Composition"
        //  rather than "Compositi / on")
        else { title.TextWrapping = TextWrapping.WrapWithOverflow; title.TextTrimming = TextTrimming.CharacterEllipsis; }
        if (CalendarKit.IsStruck(ev) && !missing) title.Struck();
        var g = K.Columns("auto, *", mark.Col(0), title.Col(1));
        var b = K.Subtle(g, null, new Thickness(3, 1, 4, 1), 5);
        b.Click += (_, _) => peek.Press(ev, b);
        b.ToolTip = ev.Title;
        Item(b, ev, menu: false);
        return b;
    }

    // ---- A day ----

    /// <summary>The day picked beside the month: its name, how much is on it and New task over one card of its work and events.</summary>
    private FrameworkElement DayPanel(DateTime day, List<CalEvent> evs)
    {
        var words = K.V(2,
            K.Text(day.ToString("dddd", Culture), Ty.Subtitle),
            K.Text($"{CalendarKit.DateWords(day)} · {CountLine(day, evs.Count)}", Ty.Body, B.Text2));
        var add = K.Button("New task", () => NewTask(day), icon: SymbolRegular.Add24);
        add.ToolTip = "A task of your own, due this day (it shows in To Do)";
        var head = K.Columns("*, auto", words.Col(0), add.Margin(10, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        return K.V(10, head, DayRows(evs));
    }

    /// <summary>A day's work and events in one card, each opening its preview (a double-click opens it where it lives).</summary>
    private FrameworkElement DayRows(List<CalEvent> evs)
    {
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        if (evs.Count == 0) rows.Children.Add(K.Empty(SymbolRegular.CalendarLtr24, "Nothing on this day."));
        for (var k = 0; k < evs.Count; k++)
        {
            if (k > 0) rows.Children.Add(K.Divider(52));
            rows.Children.Add(EventRow(evs[k]));
        }
        return K.Card(rows, new Thickness(4));
    }

    /// <summary>A day of the week or of the list: its name as the section's heading ("Tue, Oct 13"), its work and events in
    /// one card under it.</summary>
    private FrameworkElement DayCard(DateTime day, List<CalEvent> evs) =>
        K.V(0, Heading.Section(CalendarKit.DateWords(day, weekday: true), CountLine(day, evs.Count)), DayRows(evs));

    /// <summary>
    /// The Mac's CalendarEventLine: its kind's symbol in its calendar's colour, its name (struck through only once handed
    /// in, greyed once over — as on the month's grid), its course, kind, points and place, whether it is missing or
    /// excused, and its time.
    /// </summary>
    private FrameworkElement EventRow(CalEvent ev)
    {
        var missing = ev.Missing == true;
        var tile = K.IconTile(CalendarKit.Glyph(ev.Kind), K.Hex(ev.Color), 32);
        var title = K.Wrapped(ev.Title, Ty.Body, CalendarKit.IsPast(ev) && !missing ? B.Text2 : B.Text, 2);
        if (CalendarKit.IsStruck(ev) && !missing) title.Struck();
        var words = K.V(2, title);
        if (!string.IsNullOrEmpty(ev.Sub)) words.Children.Add(K.Text(ev.Sub!, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var g = K.Columns("auto, *, auto, auto", tile.Margin(0, 0, 12, 0).Col(0), words.Col(1));
        if (Flag(ev) is { } f) g.Children.Add(K.Chip(f.Word, CalendarKit.Tone(f.Kind)).Margin(8, 0, 0, 0).Col(2));
        if (!string.IsNullOrEmpty(ev.Time))
        {
            var time = K.Text(ev.Time!, Ty.Body, B.Text2);
            time.TextAlignment = TextAlignment.Right;
            time.MinWidth = 58;
            g.Children.Add(time.Margin(8, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3));
        }
        var b = K.Subtle(g, null, new Thickness(8, 9, 8, 9));
        b.Click += (_, _) => peek.Press(ev, b);
        Item(b, ev, menu: true);
        return b;
    }

    /// <summary>Where a piece of work stands, as its line says it: Missing, or Excused.</summary>
    private static WorkFlag? Flag(CalEvent ev) =>
        ev.Missing == true ? new WorkFlag { Word = "Missing", Kind = "bad" } : ev.Excused == true ? new WorkFlag { Word = "Excused", Kind = "muted" } : null;

    /// <summary>
    /// An item's button made one: known by its preview (the press that opened it, pressed again, folds it back), named for
    /// a screen reader, its own menu where it has an address (open it, open it in Canvas, copy its link).
    /// </summary>
    private void Item(Button b, CalEvent ev, bool menu)
    {
        b.Tag = CalendarPeek.KeyOf(ev);
        AutomationProperties.SetName(b, string.IsNullOrEmpty(ev.Time) ? ev.Title : $"{ev.Title}, {ev.Time}");
        AutomationProperties.SetHelpText(b, "Shows a preview; a double-click opens it");
        var held = CalendarKit.Text(ev.ReservationId);
        var planned = CalendarKit.IsPlan(ev) ? CalendarKit.Text(ev.PlanId) : null;
        if (menu && (CalendarKit.Text(ev.Url) is { } || held != null || planned != null))
        {
            var m = new ContextMenu();
            if (CalendarKit.Text(ev.Url) is { } url)
            {
                m.Items.Add(CalendarKit.Item("Open", SymbolRegular.Open24, () => engine.OpenWeb(url, ev.Title)));
                m.Items.Add(new Separator());
                m.Items.Add(CalendarKit.Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(url, ev.Title)));
                m.Items.Add(CalendarKit.Item("Copy link", SymbolRegular.Link24, () => CalendarKit.Copy(engine.Absolute(url))));
            }
            // (0.3.53) a time reserved off Find Appointment: given back here, or another found
            if (held != null)
            {
                if (m.Items.Count > 0) m.Items.Add(new Separator());
                m.Items.Add(CalendarKit.Item("Give Back This Time", SymbolRegular.CalendarCancel24, () => _ = GiveBack(held)));
                m.Items.Add(CalendarKit.Item("Find Another Time…", SymbolRegular.CalendarClock24, FindAppointment));
            }
            // (0.3.60) a plan: moved (the rail under it) or cleared
            if (planned != null)
            {
                if (m.Items.Count > 0) m.Items.Add(new Separator());
                m.Items.Add(CalendarKit.Item("Move Plan…", SymbolRegular.Clock24, () => PlanRail.Open(b, planned, ev.Title, engine, Read)));
                m.Items.Add(CalendarKit.Item("Clear Plan", SymbolRegular.CalendarCancel24, () => PlanRail.Clear(planned, engine, Read)));
            }
            b.ContextMenu = m;
        }
        peek.Anchor(b, ev);
        if (PreviewOf is not { } want || ev.Title.Contains(want, StringComparison.OrdinalIgnoreCase)) firstItem ??= (ev, b);
    }

    // ---- Week ----

    /// <summary>The week over its hours on a wide page (<see cref="WeekGrid"/>), as seven days one under another on a
    /// narrow one; (0.3.63) To Do beside it on a page wide enough for both (<see cref="WithTodo"/>).</summary>
    private FrameworkElement WeekView(Dictionary<string, List<CalEvent>> byDay, bool slide)
    {
        HideDrop(); // (the days are drawn afresh: a row still dragged over one shows where it would go again as it moves)
        var days = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        FrameworkElement view;
        if (Width >= WeekColumnsFrom) view = WeekGrid(days, byDay);
        else
        {
            var v = new Stack { Orientation = Orientation.Vertical, Spacing = 18 };
            foreach (var day in days) v.Children.Add(DayCard(day, EventsOn(byDay, day)));
            view = v;
        }
        if (slide) Motion.SlideIn(view, dir > 0);
        return WithTodo(new Border { Child = view, ClipToBounds = true });
    }

    // ---- List ----

    /// <summary>The next three weeks, each day with something on it as a section of its own.</summary>
    private FrameworkElement ListView(Dictionary<string, List<CalEvent>> byDay)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 22 };
        var keys = byDay.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (keys.Count == 0) v.Children.Add(K.Card(K.Empty(SymbolRegular.CalendarLtr24, "Nothing in the next three weeks."), new Thickness(14, 4, 14, 4)));
        foreach (var k in keys)
            if (DateTime.TryParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                v.Children.Add(DayCard(day, byDay[k]));
        return v;
    }
}

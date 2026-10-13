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
    /// <summary>A day of the month's height (the Mac's 112, with room for four of Windows' caption lines).</summary>
    private const double CellHeight = 120;

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
    /// The days of the week over six weeks of days from the first day of the week the month starts in (the grid always as
    /// tall), each a rounded cell as Windows' CalendarView has them; a new month slides in from the side the move went.
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
        for (var i = 0; i < 7; i++) days.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var w = 0; w < 6; w++) days.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHeight) });
        var first = FirstOfWeek(anchor);
        for (var i = 0; i < 42; i++)
        {
            var day = first.AddDays(i);
            days.Children.Add(DayCell(day, EventsOn(byDay, day)).Col(i % 7).Row(i / 7));
        }
        // (the cells' own gaps at the grid's edges let go, so it lines up with what is under it; a month sliding in is
        // seen inside it only)
        var clip = new Border { Child = days, ClipToBounds = true, Margin = new Thickness(-2) };
        if (slide) Motion.SlideIn(days, dir > 0);
        return K.V(0, head, clip);
    }

    /// <summary>
    /// A day of the month: its number (today on the accent; the day picked outlined in it), and up to four of its items as
    /// lines in their calendar's colour — (1.2) flat in the cell, no box round each. An item clicked opens its preview;
    /// the rest of the day picks the day.
    /// </summary>
    private FrameworkElement DayCell(DateTime day, List<CalEvent> evs)
    {
        var off = day.Month != anchor.Month || day.Year != anchor.Year;
        var isToday = day == DateTime.Today;
        var isSel = day == selected;
        var fits = evs.Count > 4 ? 3 : evs.Count;
        var num = K.Text(day.Day.ToString(Culture), Ty.Body, isToday ? B.OnAccent : isSel ? B.AccentText : off ? B.Text3 : B.Text);
        num.FontWeight = isToday || isSel ? FontWeights.SemiBold : FontWeights.Normal;
        num.HorizontalAlignment = HorizontalAlignment.Center;
        num.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Grid { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Left };
        if (isToday) badge.Children.Add(new Ellipse().Bg(B.Accent));
        badge.Children.Add(num);
        var lines = new Stack { Orientation = Orientation.Vertical, Spacing = 2 };
        foreach (var ev in evs.Take(fits)) lines.Children.Add(Chip(ev));
        if (evs.Count > fits)
        {
            var more = K.Text($"{evs.Count - fits} more", Ty.Caption, B.Text2);
            more.FontWeight = FontWeights.Medium;
            lines.Children.Add(more.Margin(17, 0, 0, 0));
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

    /// <summary>The Mac's CalendarChip: a line with its calendar's colour as a dot before its name — a red mark when it is
    /// missing, greyed once done (work handed in struck through).</summary>
    private FrameworkElement Chip(CalEvent ev)
    {
        var missing = ev.Missing == true;
        var done = ev.Done == true && !missing;
        FrameworkElement mark = missing ? K.Icon(SymbolRegular.ErrorCircle24, 10, B.Critical, filled: true) : K.Dot(K.Hex(ev.Color), 7);
        var markBox = new Grid { Width = 10, Margin = new Thickness(0, 0, 5, 0), Children = { mark } };
        var title = K.Text(ev.Title, Ty.Caption, done ? B.Text2 : missing ? B.Critical : B.Text);
        if (done && CalendarKit.IsWork(ev.Kind)) title.Struck();
        var g = K.Columns("auto, *", markBox.Col(0), title.Col(1));
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
            K.Text($"{day.ToString("M", Culture)} · {CountLine(day, evs.Count)}", Ty.Body, B.Text2));
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

    /// <summary>A day of the week or of the list: its name as the section's heading, its work and events in one card under it.</summary>
    private FrameworkElement DayCard(DateTime day, List<CalEvent> evs) =>
        K.V(0, Heading.Section(LongDay(day), CountLine(day, evs.Count)), DayRows(evs));

    /// <summary>
    /// The Mac's CalendarEventLine: its kind's symbol in its calendar's colour, its name (struck through once done), its
    /// course, kind, points and place, whether it is missing or excused, and its time.
    /// </summary>
    private FrameworkElement EventRow(CalEvent ev)
    {
        var done = ev.Done == true;
        var missing = ev.Missing == true;
        var tile = K.IconTile(CalendarKit.Glyph(ev.Kind), K.Hex(ev.Color), 32);
        var title = K.Wrapped(ev.Title, Ty.Body, done ? B.Text2 : B.Text, 2);
        if (done && !missing) title.Struck();
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
        if (menu && CalendarKit.Text(ev.Url) is { } url)
        {
            var m = new ContextMenu();
            m.Items.Add(CalendarKit.Item("Open", SymbolRegular.Open24, () => engine.OpenWeb(url, ev.Title)));
            m.Items.Add(new Separator());
            m.Items.Add(CalendarKit.Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(url, ev.Title)));
            m.Items.Add(CalendarKit.Item("Copy Link", SymbolRegular.Link24, () => CalendarKit.Copy(engine.Absolute(url))));
            b.ContextMenu = m;
        }
        peek.Anchor(b, ev);
        firstItem ??= (ev, b);
    }

    // ---- Week ----

    /// <summary>The week as seven columns on a wide page, as seven days one under another on a narrow one.</summary>
    private FrameworkElement WeekView(Dictionary<string, List<CalEvent>> byDay, bool slide)
    {
        var days = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        FrameworkElement view;
        if (Width >= WeekColumnsFrom) view = WeekColumns(days, byDay);
        else
        {
            var v = new Stack { Orientation = Orientation.Vertical, Spacing = 18 };
            foreach (var day in days) v.Children.Add(DayCard(day, EventsOn(byDay, day)));
            view = v;
        }
        if (slide) Motion.SlideIn(view, dir > 0);
        return new Border { Child = view, ClipToBounds = true };
    }

    /// <summary>Seven columns in one card, a hairline between them, each as tall as the fullest.</summary>
    private FrameworkElement WeekColumns(List<DateTime> days, Dictionary<string, List<CalEvent>> byDay)
    {
        var g = new Grid();
        for (var i = 0; i < days.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (i > 0) g.Children.Add(new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true }.Bg(B.Divider).Col(i));
            g.Children.Add(WeekColumn(days[i], EventsOn(byDay, days[i])).Col(i));
        }
        return K.Card(g, new Thickness(0));
    }

    /// <summary>A day of the week: its name and number (today on the accent, the day picked ringed in it), then its items.</summary>
    private FrameworkElement WeekColumn(DateTime day, List<CalEvent> evs)
    {
        var isToday = day == DateTime.Today;
        var isSel = day == selected;
        var name = K.Text(Culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek).ToUpper(Culture), Ty.Caption, B.Text2).Strong().Center();
        var num = K.Text(day.Day.ToString(Culture), Ty.BodyLarge, isToday ? B.OnAccent : isSel ? B.AccentText : B.Text);
        num.FontWeight = isToday || isSel ? FontWeights.Bold : FontWeights.Medium;
        num.HorizontalAlignment = HorizontalAlignment.Center;
        num.VerticalAlignment = VerticalAlignment.Center;
        var circle = new Grid { Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Center };
        if (isToday) circle.Children.Add(new Ellipse().Bg(B.Accent));
        else if (isSel)
        {
            var ring = new Ellipse { StrokeThickness = 2 };
            ring.SetResourceReference(Shape.StrokeProperty, B.Accent);
            circle.Children.Add(ring);
        }
        circle.Children.Add(num);
        var head = K.Subtle(K.V(3, name, circle), () => Pick(day), new Thickness(0, 6, 0, 6), 8);
        head.HorizontalContentAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(head, Spoken(day, evs.Count));
        if (isSel) AutomationProperties.SetItemStatus(head, "Selected");
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 6, Margin = new Thickness(6), MinHeight = 380 };
        v.Children.Add(head);
        foreach (var ev in evs) v.Children.Add(WeekBlock(ev));
        return v;
    }

    /// <summary>The Mac's CalendarWeekBlock: (1.2) its name beside a bar in its calendar's colour, flat in the column, its
    /// time, and whether it is missing or excused.</summary>
    private FrameworkElement WeekBlock(CalEvent ev)
    {
        var done = ev.Done == true;
        var missing = ev.Missing == true;
        var title = K.Wrapped(ev.Title, Ty.Body, done ? B.Text2 : B.Text, 3);
        title.FontWeight = FontWeights.Medium;
        if (done && !missing) title.Struck();
        var words = K.V(3, title);
        var meta = new WrapPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrEmpty(ev.Time)) meta.Children.Add(K.Text(ev.Time!, Ty.Caption, B.Text2).Margin(0, 1, 6, 1));
        if (Flag(ev) is { } f) meta.Children.Add(K.Chip(f.Word, CalendarKit.Tone(f.Kind)));
        if (meta.Children.Count > 0) words.Children.Add(meta);
        var bar = new Border { Width = 3.5, CornerRadius = new CornerRadius(1.75), Background = K.Brush(K.Hex(ev.Color)), Margin = new Thickness(0, 0, 8, 0) };
        var g = K.Columns("auto, *", bar.Col(0), words.Col(1));
        var b = K.Subtle(g, null, new Thickness(3, 6, 6, 6), 8);
        b.Click += (_, _) => peek.Press(ev, b);
        b.ToolTip = ev.Sub ?? ev.Title;
        Item(b, ev, menu: true);
        return b;
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

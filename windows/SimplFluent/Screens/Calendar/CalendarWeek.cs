using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// (0.3.62) The week as a calendar app draws one (the web's 2.99.109 and the Mac's 1.3.167): its days side by side over
/// the hours, each item a block from its start to its end — side by side where they overlap — a piece of work due at a
/// moment a short block at that time, the day-long ones in a strip over the hours, and today's column crossed by a red
/// line at the time it is now, the time beside it in the hours. A plan's block is dragged to move it, a quarter of an hour
/// at a time and from one day to another; let go, the plan is kept there (the page's planMove). A click alone still opens
/// its preview.
/// (0.3.64) As the web's 2.99.112 draws it: five days, Monday to Friday, or all seven from Sunday; the whole day, 12 AM to
/// 11 PM, its hours scrolling inside the card under the days — first to the morning (or, later in today, to a little
/// before now), then kept where they were as the week is drawn again. A pinch (a touchpad's, which Windows sends as Ctrl
/// and the wheel, or Ctrl and the wheel itself) makes the hours taller or shorter about the time under the pointer, from
/// a day that fits the card to a few hours that fill it, kept. And what lies above or below the hours in view waits
/// squished at that edge of its day, a thin bar in its colour; a click on it scrolls to it.
/// </summary>
public sealed partial class CalendarScreen
{
    /// <summary>An hour of the week's grid as it first is (the web's 48), and the least and the most a pinch makes it.</summary>
    private const double HourDefault = 48, HourLeast = 18, HourMost = 140;
    private const double Gutter = 56;
    /// <summary>Room over the first hour and under the last, so the hours' names on their lines are not cut.</summary>
    private const double GridInset = 8;
    private const int DayEnd = 24 * 60;
    /// <summary>The minutes a plan moves by as it is dragged.</summary>
    private const int Snap = 15;
    /// <summary>The hours' window on a page that scrolls (too short to hold still).</summary>
    private const double HoursTall = 480;
    /// <summary>A peek at the edge of the hours: its height, and under the pointer.</summary>
    private const double PeekTall = 6, PeekHover = 9;
    private const string HourPref = "calHourPx", DaysPref = "calWeekDays";

    /// <summary>(0.3.64) The week's days, kept: five, Monday to Friday, or seven from Sunday (the screenshot suite's own:
    /// --cal-days 7).</summary>
    private static int WeekDays => (Launch.Get("cal-days") is { } forced ? forced == "7" : Prefs.Double(DaysPref, 5) == 7) ? 7 : 5;

    /// <summary>(0.3.64) An hour's height now — a pinch changes it — as last kept (the screenshot suite's own: --cal-hour-px 20).</summary>
    private double hourPx = Math.Clamp(Launch.Number("cal-hour-px", Prefs.Double(HourPref, HourDefault)), HourLeast, HourMost);
    /// <summary>Where the week's hours were scrolled to, kept as the week is drawn again (null: not shown yet).</summary>
    private double? weekScroll;
    /// <summary>The week's hours as drawn now (null: no week's hours on screen).</summary>
    private WeekHours? hours;
    /// <summary>A pinch's end: a moment with no more of it — the height kept, and the week drawn again for it.</summary>
    private DispatcherTimer? pinchDone;

    /// <summary>A timed item of a day of the week: the minutes into the day it starts and ends at as drawn, and its lane
    /// among those it overlaps.</summary>
    private sealed class WeekSlot(CalEvent ev, int a, int b)
    {
        public CalEvent Ev { get; } = ev;
        public int A { get; } = a;
        public int B { get; } = b;
        public int Lane { get; set; }
        public int Lanes { get; set; } = 1;
        /// <summary>(0.3.66) Where it ends while its foot is being pulled (a plan's).</summary>
        public int? Pulled { get; set; }
        /// <summary>Its block's height with hours <paramref name="px"/> tall.</summary>
        public double Height(double px) => Math.Max(20, ((Pulled ?? B) - A) / 60.0 * px - 2);
    }

    /// <summary>(0.3.64) The week's hours as drawn: what a pinch changes in place — the hours' height, their lines and
    /// names, each day's blocks, now's line — and what waits at their edges, over the hours' scroller.</summary>
    private sealed class WeekHours(ScrollViewer scroller, Grid body)
    {
        public ScrollViewer Scroller { get; } = scroller;
        public Grid Body { get; } = body;
        public List<Border> Lines { get; } = new();
        public List<(int Minute, TextBlock Text)> Names { get; } = new();
        public List<WeekDayPanel> Panels { get; } = new();
        /// <summary>Each day's timed items, and the two edges of its column (above the hours in view, and below).</summary>
        public List<List<WeekSlot>> Slots { get; } = new();
        public List<(StackPanel Up, StackPanel Down)> Edges { get; } = new();
        public Action? PaintNow { get; set; }
    }

    /// <summary>The minutes into its day an item starts at; null for one that lasts the day (or has no time).</summary>
    private static int? StartMinutes(CalEvent ev) =>
        ev.AllDay != true && !string.IsNullOrEmpty(ev.Time) && CalendarKit.Date(ev.Date) is { } d ? d.Hour * 60 + d.Minute : null;

    /// <summary>
    /// A day's timed items as drawn: from their start to their end (work half an hour, an event without one an hour) —
    /// (0.3.69) as long as they last, a quarter of an hour at least (half an hour before) — one due at 11:59 pm kept on the
    /// grid; laid out in lanes where they overlap.
    /// </summary>
    private static List<WeekSlot> DaySlots(DateTime day, List<CalEvent> evs)
    {
        var items = new List<WeekSlot>();
        foreach (var ev in evs)
        {
            if (StartMinutes(ev) is not { } start) continue;
            var end = CalendarKit.Date(ev.End) is { } e && CalendarKit.Date(ev.Date) is { } s && e > s
                ? (e.Date == day.Date ? e.Hour * 60 + e.Minute : DayEnd)
                : start + (CalendarKit.IsWork(ev.Kind) ? 30 : 60);
            var len = Math.Max(15, end - start);
            var a = Math.Max(0, Math.Min(start, DayEnd - len));
            items.Add(new WeekSlot(ev, a, Math.Min(DayEnd, a + len)));
        }
        return Lanes(items);
    }

    /// <summary>Overlapping items side by side: each a lane, the lanes of a run of overlaps sharing the day's width.</summary>
    private static List<WeekSlot> Lanes(List<WeekSlot> items)
    {
        items.Sort((x, y) => x.A != y.A ? x.A.CompareTo(y.A) : y.B.CompareTo(x.B));
        var run = new List<WeekSlot>();
        var runEnd = -1;
        void Flush()
        {
            var ends = new List<int>();
            foreach (var it in run)
            {
                var l = ends.FindIndex(e => e <= it.A);
                if (l < 0) { l = ends.Count; ends.Add(it.B); }
                else ends[l] = it.B;
                it.Lane = l;
            }
            foreach (var it in run) it.Lanes = ends.Count;
            run.Clear();
        }
        foreach (var it in items)
        {
            if (run.Count > 0 && it.A >= runEnd) Flush();
            run.Add(it);
            runEnd = Math.Max(runEnd, it.B);
        }
        if (run.Count > 0) Flush();
        return items;
    }

    /// <summary>How far down the hours a time of day is.</summary>
    private double WeekY(int minutes) => minutes / 60.0 * hourPx;

    /// <summary>"8 AM", "Noon", "1 PM": an hour as the Calendar writes it ("12 AM" for midnight).</summary>
    private static string HourName(int h) => h == 12 ? "Noon" : new DateTime(2000, 1, 1, h, 0, 0).ToString("h tt", CultureInfo.InvariantCulture);

    /// <summary>"10:30 AM", a time of day as every time in Simpl is written.</summary>
    private static string ClockWords(DateTime t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>The week's days over the whole day's hours, in one card: the days' names and the day-long ones stay over
    /// the hours, which scroll under them — (0.3.64) the page holding still (<paramref name="fit"/>), as tall as the room
    /// left them, else in a window <see cref="HoursTall"/> tall.</summary>
    private FrameworkElement WeekGrid(List<DateTime> days, Dictionary<string, List<CalEvent>> byDay, bool fit)
    {
        rings.Clear();
        ringOn = null;
        var n = days.Count;
        var evs = days.Select(d => EventsOn(byDay, d)).ToList();
        var todayAt = days.FindIndex(d => d == DateTime.Today);

        Grid Row()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gutter) });
            for (var i = 0; i < n; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }
        Border Hairline(bool across) => across
            ? new Border { Height = 1, SnapsToDevicePixels = true }.Bg(B.Divider)
            : new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true }.Bg(B.Divider);

        // the days' names and numbers, and the day-long ones — over the hours, which scroll under them
        var head = Row();
        head.Margin = new Thickness(0, 4, 0, 4);
        for (var i = 0; i < n; i++) head.Children.Add(WeekHead(days[i], evs[i].Count).Col(i + 1));
        var over = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        over.Children.Add(head);
        over.Children.Add(Hairline(true));
        var allDay = evs.Select(l => l.Where(e => StartMinutes(e) == null).ToList()).ToList();
        if (allDay.Any(l => l.Count > 0))
        {
            var strip = Row();
            var label = K.Text("all-day", Ty.Caption, B.Text2);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            strip.Children.Add(label.Margin(0, 6, 8, 6).Col(0));
            for (var i = 0; i < n; i++)
            {
                var v = new Stack { Orientation = Orientation.Vertical, Spacing = 2, Margin = new Thickness(3) };
                foreach (var ev in allDay[i]) v.Children.Add(Chip(ev));
                strip.Children.Add(v.Col(i + 1));
                strip.Children.Add(Hairline(false).Col(i + 1));
            }
            over.Children.Add(strip);
            over.Children.Add(Hairline(true));
        }

        // the hours, the whole day
        var body = Row();
        body.Height = 24 * hourPx + 2 * GridInset;
        var scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
        var w = new WeekHours(scroller, body);
        var inner = new Thickness(0, GridInset, 0, GridInset);
        // (a hairline at each hour, past the hours' names)
        var lines = new Stack { Orientation = Orientation.Vertical, Spacing = 0, Margin = inner, IsHitTestVisible = false };
        for (var h = 0; h < 24; h++)
        {
            var line = new Border { Height = hourPx, BorderThickness = new Thickness(0, 1, 0, 0), SnapsToDevicePixels = true, Opacity = 0.7 };
            line.SetResourceReference(Border.BorderBrushProperty, B.Divider);
            lines.Children.Add(line);
            w.Lines.Add(line);
        }
        body.Children.Add(lines.Col(1, n));
        // the hours' names, each on its line
        var gutter = new Grid { Margin = inner, IsHitTestVisible = false };
        for (var h = 0; h < 24; h++)
        {
            var t = K.Text(HourName(h), Ty.Caption, B.Text2);
            t.FontSize = 11;
            t.HorizontalAlignment = HorizontalAlignment.Right;
            t.VerticalAlignment = VerticalAlignment.Top;
            t.Margin = new Thickness(0, WeekY(h * 60) - 8, 8, 0);
            gutter.Children.Add(t);
            w.Names.Add((h * 60, t));
        }
        body.Children.Add(gutter.Col(0));

        // what lies above or below the hours in view, at the edges of its day, over the scroller
        var edges = Row();
        var columns = new List<Grid>();
        for (var i = 0; i < n; i++)
        {
            var cell = new Grid { ClipToBounds = false };
            if (i == todayAt) cell.Children.Add(new Border { Opacity = 0.05, IsHitTestVisible = false }.Bg(B.Accent));
            var panel = new WeekDayPanel(() => hourPx) { Margin = new Thickness(3, GridInset, 3, GridInset) };
            var slots = DaySlots(days[i], evs[i]);
            foreach (var s in slots) panel.Children.Add(WeekBlock(s, days[i], i, cell, columns));
            cell.Children.Add(panel);
            cell.Children.Add(Hairline(false));
            TodoDrops(cell, days[i], slots); // (0.3.63: a row of To Do dragged onto it is planned there; 0.3.66: onto a plan, joins it)
            columns.Add(cell);
            body.Children.Add(cell.Col(i + 1));
            w.Panels.Add(panel);
            w.Slots.Add(slots);
            var up = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 2, 4, 0) };
            var down = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 4, 2) };
            edges.Children.Add(up.Col(i + 1));
            edges.Children.Add(down.Col(i + 1));
            w.Edges.Add((up, down));
        }
        if (todayAt >= 0) w.PaintNow = NowOn(columns[todayAt], gutter, w.Names);

        var area = new Grid { Children = { scroller, edges } };
        if (!fit) area.Height = HoursTall;
        hours = w;
        StartHours(w, todayAt >= 0, over, edges);
        ShotTodoDrop(columns, days);
        FrameworkElement content = fit ? K.Rows("auto, *", over.Row(0), area.Row(1)) : K.V(0, over, area);
        return K.Card(content, new Thickness(0), 12);
    }

    /// <summary>
    /// (0.3.64) The week's hours set going: scrolled first to the morning — or, today in the week and past one o'clock, to
    /// three hours before now — else to where they were; the days' names and the edges kept clear of the scroll bar, so the
    /// columns line up; what lies past the hours in view painted at their edges as they scroll; a pinch over them; and, a
    /// row of To Do held near their top or bottom, the hours scrolling on under it.
    /// </summary>
    private void StartHours(WeekHours w, bool todayShown, FrameworkElement over, FrameworkElement edges)
    {
        var sv = w.Scroller;
        var now = DateTime.Now;
        // (the screenshot suite: --open cal-peeks scrolls the hours to one in the afternoon, what lies past them at their edges)
        if (weekScroll == null && Launch.Take("cal-peeks") != null)
        {
            weekScroll = GridInset + 13 * hourPx;
            _ = ShotPeeks();
        }
        var want = weekScroll ?? GridInset + (todayShown && now.Hour >= 13 ? now.Hour - 3 : 7.5) * hourPx;
        var placed = false;
        sv.ScrollToVerticalOffset(want);
        void BarRoom()
        {
            var bar = Math.Max(0, sv.ActualWidth - sv.ViewportWidth);
            if (Math.Abs(over.Margin.Right - bar) < 0.5) return;
            over.Margin = new Thickness(0, 0, bar, 0);
            edges.Margin = new Thickness(0, 0, bar, 0);
        }
        sv.ScrollChanged += (_, _) =>
        {
            BarRoom();
            PaintEdges(w);
            // (the first layouts, before the place asked for is reached, say nothing of where the hours were scrolled)
            if (!placed && sv.ExtentHeight > 0 && Math.Abs(sv.VerticalOffset - Math.Min(want, sv.ScrollableHeight)) <= 1) placed = true;
            if (placed && ReferenceEquals(hours, w)) weekScroll = sv.VerticalOffset;
        };
        sv.Loaded += (_, _) => sv.Dispatcher.BeginInvoke(() => { if (!placed) sv.ScrollToVerticalOffset(want); }, DispatcherPriority.Loaded);
        // (a pinch: Windows sends a touchpad's as Ctrl and the wheel)
        sv.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            e.Handled = true;
            ScaleHours(w, hourPx * Math.Exp(e.Delta * 0.0015), e.GetPosition(sv).Y);
        };
        sv.PreviewDragOver += (_, e) =>
        {
            var y = e.GetPosition(sv).Y;
            const double zone = 40;
            var step = y < zone ? -(zone - y) / 3 : y > sv.ViewportHeight - zone ? (y - (sv.ViewportHeight - zone)) / 3 : 0;
            if (step != 0) sv.ScrollToVerticalOffset(sv.VerticalOffset + step);
        };
    }

    /// <summary>
    /// (0.3.64) A pinch: the hours made <paramref name="next"/> tall — never less than lets the whole day fit the card, nor
    /// more than <see cref="HourMost"/> — about the time <paramref name="y"/> down the hours in view, which stays under the
    /// pointer; the lines, the names, the blocks and now's line follow at once, and a moment after the pinch ends the
    /// height is kept and the week drawn again, each block's words fitted to its new height.
    /// </summary>
    private void ScaleHours(WeekHours w, double next, double y)
    {
        var sv = w.Scroller;
        var least = Math.Max(HourLeast, (sv.ViewportHeight - 2 * GridInset - 2) / 24);
        var px = Math.Clamp(next, Math.Min(least, HourMost), HourMost);
        if (Math.Abs(px - hourPx) < 0.05) return;
        var at = (sv.VerticalOffset + y - GridInset) / hourPx;
        hourPx = px;
        HideDrop();
        w.Body.Height = 24 * hourPx + 2 * GridInset;
        foreach (var l in w.Lines) l.Height = hourPx;
        foreach (var (minute, t) in w.Names) t.Margin = new Thickness(0, WeekY(minute) - 8, 8, 0);
        foreach (var p in w.Panels)
        {
            p.InvalidateMeasure();
            p.InvalidateArrange();
        }
        w.PaintNow?.Invoke();
        sv.UpdateLayout();
        sv.ScrollToVerticalOffset(Math.Max(0, at * hourPx + GridInset - y));
        if (pinchDone == null)
        {
            pinchDone = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
            pinchDone.Tick += (_, _) =>
            {
                pinchDone.Stop();
                Prefs.Set(HourPref, Math.Round(hourPx));
                Render();
            };
        }
        pinchDone.Stop();
        pinchDone.Start();
    }

    /// <summary>(0.3.64) The screenshot suite's week pinched (--open cal-pinch): once it has settled, its hours made as short
    /// as they go about the middle of the view — the whole day in the card — kept, and the week drawn again; the log says
    /// how tall they went and whether the whole day is in view.</summary>
    private async Task ShotPinch()
    {
        Shot.Began();
        try
        {
            await Task.Delay(2500);
            if (hidden || hours is not { } w || !w.Scroller.IsLoaded) return;
            ScaleHours(w, HourLeast, w.Scroller.ViewportHeight / 2);
            await Task.Delay(1200); // (the pinch's end: its height kept, the week drawn again)
            var whole = hours is { } now && now.Scroller.IsLoaded && now.Scroller.ScrollableHeight < 1;
            Log.Write($"calendar: hours pinched to {hourPx:0} px, the whole day {(whole ? "in view" : "not in view")}");
        }
        finally { Shot.Ended(); }
    }

    /// <summary>(0.3.64) The screenshot suite's week scrolled to the afternoon (--open cal-peeks): once it has settled, the
    /// log says how many items wait at the hours' edges.</summary>
    private async Task ShotPeeks()
    {
        Shot.Began();
        try
        {
            await Task.Delay(2500);
            if (hidden || hours is not { } w || !w.Scroller.IsLoaded) return;
            PaintEdges(w);
            var up = w.Edges.Sum(e => e.Up.Children.Count);
            var down = w.Edges.Sum(e => e.Down.Children.Count);
            Log.Write(string.Create(CultureInfo.InvariantCulture, $"calendar: peeks: {up} above, {down} below, the hours from {(w.Scroller.VerticalOffset - GridInset) / hourPx:0.#}"));
        }
        finally { Shot.Ended(); }
    }

    /// <summary>(0.3.64) What lies above and below the hours in view, squished at the top or the bottom of its day: the
    /// nearest four each way, painted again only when they change.</summary>
    private void PaintEdges(WeekHours w)
    {
        var sv = w.Scroller;
        var top = sv.VerticalOffset;
        var bottom = top + sv.ViewportHeight;
        if (sv.ViewportHeight <= 0) return;
        for (var i = 0; i < w.Slots.Count && i < w.Edges.Count; i++)
        {
            var up = new List<WeekSlot>();
            var down = new List<WeekSlot>();
            foreach (var s in w.Slots[i])
            {
                var y = GridInset + WeekY(s.A);
                if (y + s.Height(hourPx) <= top + PeekTall) up.Add(s);
                else if (y >= bottom - PeekTall) down.Add(s);
            }
            Edge(w.Edges[i].Up, up.OrderBy(s => s.A).TakeLast(4).ToList(), sv);
            Edge(w.Edges[i].Down, down.OrderBy(s => s.A).Take(4).ToList(), sv);
        }
    }

    private void Edge(StackPanel edge, List<WeekSlot> list, ScrollViewer sv)
    {
        var key = string.Join("|", list.Select(s => s.Ev.Id));
        if (edge.Tag as string == key) return;
        edge.Tag = key;
        edge.Children.Clear();
        foreach (var s in list) edge.Children.Add(Peek(s, sv));
    }

    /// <summary>(0.3.64) An item past the hours in view, as a thin bar in its colour (its calendar's tint, its bar at the
    /// left as its block has): a click scrolls to it.</summary>
    private FrameworkElement Peek(WeekSlot s, ScrollViewer sv)
    {
        var color = K.Hex(s.Ev.Color);
        var bar = new Grid { Height = PeekTall, Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3) }.Bg(B.Solid).Stroke(B.CardStroke));
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = K.Brush(K.WithAlpha(color, 0.35)) });
        bar.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Background = K.Brush(color) });
        var when = CalendarKit.Date(s.Ev.Date) is { } at ? ClockWords(at) : s.Ev.Time;
        bar.ToolTip = string.Join(" · ", new[] { when, s.Ev.Title }.Where(x => !string.IsNullOrEmpty(x)));
        AutomationProperties.SetName(bar, $"Scroll to {s.Ev.Title}");
        bar.MouseEnter += (_, _) => Motion.To(bar, FrameworkElement.HeightProperty, PeekHover, Motion.Fast);
        bar.MouseLeave += (_, _) => Motion.To(bar, FrameworkElement.HeightProperty, PeekTall, Motion.Fast);
        bar.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            sv.ScrollToVerticalOffset(Math.Max(0, GridInset + WeekY(s.A) - 28));
        };
        return bar;
    }

    /// <summary>
    /// Now, on today's column: a red line at the minute, and the time beside it in the hours (the hour it falls near left
    /// out) — kept there as the minutes go by, while the week is on screen. What paints it, for a pinch.
    /// </summary>
    private Action NowOn(Grid column, Grid gutter, List<(int Minute, TextBlock Text)> names)
    {
        var red = K.Brush(K.Tone("red"));
        var line = new Grid { Height = 9, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        line.Children.Add(new Rectangle { Height = 1.5, Fill = red, VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = red, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-4.5, 0, 0, 0) });
        column.Children.Add(line);
        var time = K.Text("", Ty.Caption, B.Text);
        time.Foreground = red;
        time.FontSize = 11;
        time.FontWeight = FontWeights.Bold;
        time.HorizontalAlignment = HorizontalAlignment.Right;
        time.VerticalAlignment = VerticalAlignment.Top;
        gutter.Children.Add(time);

        void Paint()
        {
            var t = DateTime.Now;
            var m = t.Hour * 60 + t.Minute;
            // (the hour's name within 12 of the time, at the hours' height now, gives way to it)
            foreach (var (minute, text) in names) text.Visibility = Math.Abs(minute - m) < 12 / hourPx * 60 ? Visibility.Hidden : Visibility.Visible;
            var y = WeekY(m);
            line.Margin = new Thickness(0, GridInset + y - 4.5, 0, 0);
            time.Margin = new Thickness(0, y - 8, 8, 0);
            time.Text = t.ToString("h:mm", CultureInfo.InvariantCulture);
        }
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) => Paint();
        column.Loaded += (_, _) => { Paint(); timer.Start(); };
        column.Unloaded += (_, _) => timer.Stop();
        Paint();
        return Paint;
    }

    /// <summary>A day's name and number over its column (today in a filled circle, the day picked ringed in the accent);
    /// a click picks the day.</summary>
    private FrameworkElement WeekHead(DateTime day, int count)
    {
        var isToday = day == DateTime.Today;
        var isSel = day == selected;
        var name = K.Text(Culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek).ToUpper(Culture), Ty.Caption, isToday ? B.AccentText : B.Text2).Strong().Center();
        var num = K.Text(day.Day.ToString(Culture), Ty.BodyLarge, isToday ? B.OnAccent : isSel ? B.AccentText : B.Text);
        num.FontWeight = isToday || isSel ? FontWeights.Bold : FontWeights.Medium;
        num.HorizontalAlignment = HorizontalAlignment.Center;
        num.VerticalAlignment = VerticalAlignment.Center;
        var circle = new Grid { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
        if (isToday) circle.Children.Add(new Ellipse().Bg(B.Accent));
        else if (isSel)
        {
            var ring = new Ellipse { StrokeThickness = 2 };
            ring.SetResourceReference(Shape.StrokeProperty, B.Accent);
            circle.Children.Add(ring);
        }
        circle.Children.Add(num);
        var head = K.Subtle(K.V(2, name, circle), () => Pick(day), new Thickness(0, 4, 0, 4), 8);
        head.HorizontalContentAlignment = HorizontalAlignment.Center;
        head.Margin = new Thickness(2, 0, 2, 0);
        AutomationProperties.SetName(head, Spoken(day, count));
        if (isSel) AutomationProperties.SetItemStatus(head, "Selected");
        return head;
    }

    /// <summary>
    /// An item's block on the week, as tall as it lasts: tinted in its calendar's colour with a bar in it, its time (work
    /// "Due" at it) over its name — one line of both when it is short — struck through only once handed in, greyed once
    /// over; a plan's with a dashed edge, dragged to move it.
    /// </summary>
    private Button WeekBlock(WeekSlot s, DateTime day, int dayIndex, Grid column, List<Grid> columns)
    {
        var ev = s.Ev;
        var height = s.Height(hourPx);
        var tiny = height < 32;
        var missing = ev.Missing == true;
        var grey = CalendarKit.IsPast(ev) && !missing;
        var plan = CalendarKit.IsPlan(ev);
        var linked = CalendarKit.IsLinked(ev);
        var color = K.Hex(ev.Color);
        var clock = CalendarKit.Date(ev.Date) is { } at ? ClockWords(at) : ev.Time ?? "";
        var time = new Run(clock);

        var words = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var titleBrush = missing ? B.Critical : grey || linked ? B.Text2 : B.Text;
        if (tiny)
        {
            var line = K.Text("", Ty.Caption, titleBrush);
            line.FontWeight = FontWeights.SemiBold;
            line.Inlines.Add(new Bold(time) { Foreground = K.Brush(K.WithAlpha(color, grey ? 0.6 : 1)) });
            line.Inlines.Add(new Run(" " + ev.Title));
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            if (CalendarKit.IsStruck(ev) && !missing) line.Struck();
            words.Children.Add(line);
        }
        else
        {
            var when = K.Text("", Ty.Caption, B.Text2);
            when.FontSize = 11;
            when.FontWeight = FontWeights.SemiBold;
            when.Foreground = K.Brush(K.WithAlpha(color, grey ? 0.6 : 1));
            when.TextTrimming = TextTrimming.CharacterEllipsis;
            if (plan) when.Inlines.Add(new InlineUIContainer(K.Icon(SymbolRegular.Clock24, 10, K.WithAlpha(color, grey ? 0.6 : 1)).Margin(0, 0, 3, -1)) { BaselineAlignment = BaselineAlignment.Center });
            if (CalendarKit.IsWork(ev.Kind) && !plan) when.Inlines.Add(new Run("Due "));
            when.Inlines.Add(time);
            if (missing) when.Inlines.Add(new Run(" · Missing") { Foreground = K.Brush(K.Tone("red")) });
            else if (ev.Excused == true) when.Inlines.Add(new Run(" · Excused"));
            words.Children.Add(when);
            var lines = Math.Max(1, (int)Math.Floor((height - 22) / 16));
            var title = K.Wrapped(ev.Title, Ty.Caption, titleBrush, lines);
            title.FontWeight = FontWeights.SemiBold;
            title.TextWrapping = TextWrapping.WrapWithOverflow;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            if (CalendarKit.IsStruck(ev) && !missing) title.Struck();
            words.Children.Add(title);
        }
        words.Margin = new Thickness(9, tiny ? 2 : 3, 4, 2);

        var g = new Grid { ClipToBounds = true };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = K.Brush(K.WithAlpha(color, grey ? 0.08 : 0.16)) });
        g.Children.Add(new Border
        {
            Width = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2.5, 3, 0, 3),
            Background = K.Brush(K.WithAlpha(color, grey || linked ? 0.55 : 1)),
        });
        // (a plan: a dashed edge in its colour, time set aside rather than something set)
        if (plan)
            g.Children.Add(new Rectangle
            {
                RadiusX = 6, RadiusY = 6, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 3, 2.5 },
                Stroke = K.Brush(K.WithAlpha(color, 0.8)), IsHitTestVisible = false,
            });
        g.Children.Add(words);
        // (0.3.66, the Mac's 1.3.172) a plan's foot, pulled to make it longer or shorter, and the ring it wears while another
        // plan or a row of To Do is held over it (let go there, they are one block)
        Border? grip = null;
        if (plan)
        {
            var ring = new Rectangle { RadiusX = 6, RadiusY = 6, StrokeThickness = 2, Stroke = K.Brush(Appearance.Accent), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            g.Children.Add(ring);
            rings[s] = ring;
            var bar = new Border { Width = 22, Height = 3, CornerRadius = new CornerRadius(1.5), Background = K.Brush(K.WithAlpha(color, 0.9)), Opacity = 0, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
            grip = new Border { Height = 9, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = Cursors.SizeNS, Child = bar, Tag = GripTag };
            grip.MouseEnter += (_, _) => bar.Opacity = 1;
            grip.MouseLeave += (_, _) => bar.Opacity = 0;
            g.Children.Add(grip);
        }

        var b = K.Subtle(g, null, new Thickness(0), 6);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.VerticalContentAlignment = VerticalAlignment.Stretch;
        b.MinHeight = 0;
        b.MinWidth = 0;
        b.Click += (_, _) => Press(ev, b);
        b.ToolTip = string.Join(" · ", new[] { plan ? "Planned" : null, ev.Title, ev.Sub, plan ? "drag to move it, or its foot to make it longer" : null }.Where(x => !string.IsNullOrEmpty(x)));
        WeekDayPanel.SetSlot(b, s);
        Item(b, ev, menu: true);
        if (plan && CalendarKit.Text(ev.PlanId) is { } pid)
        {
            Draggable(b, s, pid, day, dayIndex, column, columns, time, clock);
            if (grip != null) Stretchable(b, grip, s, pid, day, time, clock);
        }
        return b;
    }

    /// <summary>
    /// A plan's block dragged to another time: (0.3.69, the Mac's 1.3.176) it follows the pointer to the point, lifted over
    /// the days beside, the quarter hour and the day it would go to outlined under it and the time on it saying which;
    /// let go, it settles there, the plan is moved there and the week read again. Escape, or a drag back to where it was, puts it back. A click without a drag is the
    /// block's own: its preview opens.
    /// </summary>
    private void Draggable(Button b, WeekSlot s, string pid, DateTime day, int dayIndex, Grid column, List<Grid> columns, Run time, string clock)
    {
        var shift = new TranslateTransform();
        b.RenderTransform = shift;
        var len = s.B - s.A;
        Point? from = null;
        var engaged = false;
        var days = 0;
        var minutes = 0;
        var grab = 0; // (0.3.66: how far into the block it was taken, in minutes — where the pointer is, for the plan it is over)
        var name = AutomationProperties.GetName(b);
        AutomationProperties.SetHelpText(b, "Shows the plan; drag it to move it, or onto another plan to group them");

        void Lift(bool on)
        {
            Panel.SetZIndex(column, on ? 5 : 0);
            b.Effect = on ? new DropShadowEffect { BlurRadius = 16, ShadowDepth = 5, Direction = 270, Opacity = 0.28, Color = Colors.Black } : null;
            Mouse.OverrideCursor = on ? Cursors.SizeAll : null;
        }
        // (0.3.69) where it would go, outlined in that day's column
        Rectangle? ghost = null;
        Grid? ghostIn = null;
        void Ghost(int d, int at)
        {
            var cell = columns[dayIndex + d];
            if (ghost == null)
            {
                var accent = Appearance.Accent;
                ghost = new Rectangle
                {
                    RadiusX = 6, RadiusY = 6, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2.5 },
                    Stroke = K.Brush(K.WithAlpha(accent, 0.75)), Fill = K.Brush(K.WithAlpha(accent, 0.1)),
                    IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top,
                };
            }
            if (ghostIn != cell)
            {
                ghostIn?.Children.Remove(ghost);
                cell.Children.Add(ghost);
                ghostIn = cell;
            }
            ghost.Height = s.Height(hourPx);
            ghost.Margin = new Thickness(4, GridInset + at / 60.0 * hourPx, 6, 0);
        }
        void NoGhost()
        {
            if (ghost != null) ghostIn?.Children.Remove(ghost);
            ghostIn = null;
        }
        void Back()
        {
            engaged = false;
            from = null;
            days = minutes = 0;
            NoGhost();
            Lift(false);
            Motion.To(shift, TranslateTransform.XProperty, 0, Motion.Fast);
            Motion.To(shift, TranslateTransform.YProperty, 0, Motion.Fast);
            time.Text = clock;
            AutomationProperties.SetName(b, name);
            Ring(null);
        }

        b.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (FromGrip(e.OriginalSource, b)) return; // (its foot: pulled, not moved — Stretchable)
            from = e.GetPosition(columns[0].Parent as IInputElement ?? b);
            grab = Math.Max(0, (int)(e.GetPosition(b).Y / hourPx * 60));
        };
        b.PreviewMouseMove += (_, e) =>
        {
            if (from is not { } f || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(columns[0].Parent as IInputElement ?? b);
            var dx = p.X - f.X;
            var dy = p.Y - f.Y;
            if (!engaged)
            {
                if (Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
                engaged = true;
                peek.Dismiss();
                Lift(true);
            }
            e.Handled = true;
            // (0.3.69) the block to the point under the pointer, kept to the week and the day's hours
            var latest = Math.Max(0, DayEnd - Math.Min(len, DayEnd));
            var xLo = columns[0].TranslatePoint(new Point(0, 0), column).X;
            var xHi = columns[^1].TranslatePoint(new Point(0, 0), column).X;
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.X = Math.Clamp(dx, xLo, xHi);
            shift.Y = Math.Clamp(dy, -s.A / 60.0 * hourPx, (latest - s.A) / 60.0 * hourPx);
            // (and the quarter hour and the day it would go to, from that)
            var at = Math.Clamp((int)Math.Round((s.A + shift.Y / hourPx * 60) / Snap) * Snap, 0, latest);
            var w = column.ActualWidth;
            var d = w > 0 ? Math.Clamp((int)Math.Round(shift.X / w), -dayIndex, columns.Count - 1 - dayIndex) : 0;
            if (d == days && at - s.A == minutes && ghostIn != null) return;
            days = d;
            minutes = at - s.A;
            var when = day.AddDays(d).AddMinutes(at);
            var onto = PlanAt(dayIndex + d, at + grab, pid);
            Ring(onto);
            if (onto == null) Ghost(d, at); else NoGhost();
            time.Text = onto != null ? $"Group with {onto.Ev.Title}" : d == 0 ? ClockWords(when) : $"{when.ToString("ddd", CultureInfo.InvariantCulture)} {ClockWords(when)}";
            AutomationProperties.SetName(b, onto != null ? $"Group with {onto.Ev.Title}" : $"Move to {when.ToString("dddd", CultureInfo.InvariantCulture)} {ClockWords(when)}");
        };
        b.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!engaged) { from = null; return; }
            // (the press a drag ends with opens nothing: the button never hears it let go)
            e.Handled = true;
            engaged = false;
            from = null;
            b.ReleaseMouseCapture();
            // (0.3.66) let go over another plan: one block with it, its group's
            NoGhost();
            if (PlanAt(dayIndex + days, s.A + minutes + grab, pid) is { } target && CalendarKit.Text(target.Ev.PlanId) is { } onto)
            {
                Ring(null);
                Lift(false);
                _ = Join(onto, target.Ev.Title);
                return;
            }
            if (days == 0 && minutes == 0) { Back(); return; }
            Lift(false);
            // (0.3.69) settled into the quarter hour and the day it goes to (the shift to another day measured on the columns
            // as they are: they are not all one width to the pixel)
            Motion.To(shift, TranslateTransform.XProperty, days == 0 ? 0 : columns[dayIndex + days].TranslatePoint(new Point(0, 0), column).X, Motion.Fast);
            Motion.To(shift, TranslateTransform.YProperty, minutes / 60.0 * hourPx, Motion.Fast);
            Panel.SetZIndex(column, 5); // (it stays where it was let go, over the days beside, until the week is drawn again)
            var moved = day.AddDays(days).AddMinutes(s.A + minutes);
            _ = Drop(moved);
        };
        b.LostMouseCapture += (_, _) => { if (engaged) Back(); };
        b.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !engaged) return;
            e.Handled = true;
            Back();
            b.ReleaseMouseCapture();
        };

        async Task Drop(DateTime moved)
        {
            var iso = DateTime.SpecifyKind(moved, DateTimeKind.Local).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            // (0.3.66) one of a repeating plan's times: the series moves by as much (the page's planMove, given the time it was)
            if (!await engine.Act("planMove", new { id = pid, at = iso, occurs = CalendarKit.Text(s.Ev.Occurs) })) Back();
            Read();
            engine.DataChanged();
        }

        async Task Join(string onto, string title)
        {
            if (!await engine.Act("planJoin", new { id = pid, onto, whole = true })) { Back(); return; }
            Said($"Grouped with {title}");
            Read();
            engine.DataChanged();
        }
    }

    /// <summary>(0.3.66) What marks a plan's foot (its grip), so a press on it is a pull, never a move.</summary>
    private const string GripTag = "plan-foot";

    /// <summary>Whether a press began on a plan's foot (inside <paramref name="block"/>).</summary>
    private static bool FromGrip(object source, DependencyObject block)
    {
        for (var o = source as DependencyObject; o != null && o != block; o = o is Visual || o is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (o is FrameworkElement { Tag: GripTag }) return true;
        return false;
    }

    /// <summary>(0.3.66) Each plan's ring on the week drawn now, lit while another plan or a row of To Do is held over it.</summary>
    private readonly Dictionary<WeekSlot, Rectangle> rings = new();
    private WeekSlot? ringOn;

    private void Ring(WeekSlot? target)
    {
        if (target == ringOn) return;
        if (ringOn != null && rings.TryGetValue(ringOn, out var was)) was.Visibility = Visibility.Collapsed;
        ringOn = target;
        if (target != null && rings.TryGetValue(target, out var now)) now.Visibility = Visibility.Visible;
    }

    /// <summary>(0.3.66) The plan of a day of the week drawn now whose block holds a minute of it (other than the plan
    /// <paramref name="not"/>): what a dragged block, or a row of To Do, let go there joins.</summary>
    private WeekSlot? PlanAt(int dayIndex, int minute, string? not)
    {
        if (hours is not { } w || dayIndex < 0 || dayIndex >= w.Slots.Count) return null;
        return w.Slots[dayIndex].FirstOrDefault(x => CalendarKit.IsPlan(x.Ev) && CalendarKit.Text(x.Ev.PlanId) is { } p && p != not && minute >= x.A && minute < (x.Pulled ?? x.B));
    }

    /// <summary>
    /// (0.3.66, the Mac's 1.3.172 and the web's 2.99.113) A plan's foot pulled: its block follows a quarter of an hour at a
    /// time, its times on it, a quarter of an hour long at least and over by midnight; let go, the plan is that long (the
    /// page's planEdit), its group's every task with it. Let go where it began, nothing changes.
    /// </summary>
    private void Stretchable(Button b, Border grip, WeekSlot s, string pid, DateTime day, Run time, string clock)
    {
        var len = s.B - s.A;
        double? from = null;
        var mins = len;
        void Relayout() => (VisualTreeHelper.GetParent(b) as UIElement)?.InvalidateMeasure();
        void Back()
        {
            from = null;
            s.Pulled = null;
            mins = len;
            time.Text = clock;
            Relayout();
        }
        grip.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // (the block's own press, a click, never hears it)
            peek.Dismiss();
            from = e.GetPosition(b).Y;
            grip.CaptureMouse();
        };
        grip.MouseMove += (_, e) =>
        {
            if (from is not { } f || e.LeftButton != MouseButtonState.Pressed) return;
            e.Handled = true;
            var dy = e.GetPosition(b).Y - f;
            var next = Math.Clamp((int)Math.Round((len + dy / hourPx * 60) / Snap) * Snap, Snap, DayEnd - s.A);
            if (next == mins) return;
            mins = next;
            s.Pulled = s.A + mins;
            time.Text = $"{clock} – {ClockWords(day.AddMinutes(s.A + mins))}";
            Relayout();
        };
        grip.MouseLeftButtonUp += async (_, e) =>
        {
            if (from == null) return;
            e.Handled = true;
            from = null;
            grip.ReleaseMouseCapture();
            if (mins == len) { Back(); return; }
            if (!await engine.Act("planEdit", new { id = pid, mins })) { Back(); return; }
            Read();
            engine.DataChanged();
        };
        grip.LostMouseCapture += (_, _) => { if (from != null) Back(); };
    }

    /// <summary>A day of the week's grid: each block at its time and as tall as it lasts at the hours' height now (a pinch
    /// changes it), side by side with those it overlaps.</summary>
    private sealed class WeekDayPanel(Func<double> px) : Panel
    {
        public static readonly DependencyProperty SlotProperty = DependencyProperty.RegisterAttached(
            "Slot", typeof(WeekSlot), typeof(WeekDayPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsParentArrange));

        public static void SetSlot(UIElement e, WeekSlot s) => e.SetValue(SlotProperty, s);

        protected override Size MeasureOverride(Size available)
        {
            var w = double.IsInfinity(available.Width) ? 120 : available.Width;
            foreach (UIElement c in InternalChildren)
                if (c.GetValue(SlotProperty) is WeekSlot s) c.Measure(new Size(Math.Max(0, w / s.Lanes - 3), s.Height(px())));
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size final)
        {
            var hour = px();
            foreach (UIElement c in InternalChildren)
            {
                if (c.GetValue(SlotProperty) is not WeekSlot s) continue;
                var lane = final.Width / s.Lanes;
                c.Arrange(new Rect(s.Lane * lane + 1, s.A / 60.0 * hour, Math.Max(0, lane - 3), s.Height(hour)));
            }
            return final;
        }
    }
}

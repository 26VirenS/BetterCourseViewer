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
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// (0.3.62) The week as a calendar app draws one (the web's 2.99.109 and the Mac's 1.3.167): its seven days side by side
/// over the hours, each item a block from its start to its end — side by side where they overlap — a piece of work due at
/// a moment a short block at that time, the day-long ones in a strip over the hours, and today's column crossed by a red
/// line at the time it is now, the time beside it in the hours. A plan's block is dragged to move it, a quarter of an
/// hour at a time and from one day to another; let go, the plan is kept there (the page's planMove). A click alone still
/// opens its preview.
/// </summary>
public sealed partial class CalendarScreen
{
    /// <summary>An hour of the week's grid (the web's 48), and the room for the hours' names before the days.</summary>
    private const double HourPx = 48;
    private const double Gutter = 56;
    /// <summary>Room over the first hour and under the last, so the hours' names on their lines are not cut.</summary>
    private const double GridInset = 8;
    private const int DayEnd = 24 * 60;
    /// <summary>The minutes a plan moves by as it is dragged.</summary>
    private const int Snap = 15;

    /// <summary>A timed item of a day of the week: the minutes into the day it starts and ends at as drawn, and its lane
    /// among those it overlaps.</summary>
    private sealed class WeekSlot(CalEvent ev, int a, int b)
    {
        public CalEvent Ev { get; } = ev;
        public int A { get; } = a;
        public int B { get; } = b;
        public int Lane { get; set; }
        public int Lanes { get; set; } = 1;
        public double Height => Math.Max(20, (B - A) / 60.0 * HourPx - 2);
    }

    /// <summary>The minutes into its day an item starts at; null for one that lasts the day (or has no time).</summary>
    private static int? StartMinutes(CalEvent ev) =>
        ev.AllDay != true && !string.IsNullOrEmpty(ev.Time) && CalendarKit.Date(ev.Date) is { } d ? d.Hour * 60 + d.Minute : null;

    /// <summary>
    /// A day's timed items as drawn: from their start to their end (work half an hour, an event without one an hour), never
    /// less than half an hour, one due at 11:59 pm kept on the grid; laid out in lanes where they overlap.
    /// </summary>
    private static List<WeekSlot> DaySlots(DateTime day, List<CalEvent> evs, int first)
    {
        var items = new List<WeekSlot>();
        foreach (var ev in evs)
        {
            if (StartMinutes(ev) is not { } start) continue;
            var end = CalendarKit.Date(ev.End) is { } e && CalendarKit.Date(ev.Date) is { } s && e > s
                ? (e.Date == day.Date ? e.Hour * 60 + e.Minute : DayEnd)
                : start + (CalendarKit.IsWork(ev.Kind) ? 30 : 60);
            var len = Math.Max(30, end - start);
            var a = Math.Max(first * 60, Math.Min(start, DayEnd - 30));
            items.Add(new WeekSlot(ev, a, Math.Min(DayEnd, Math.Max(a + 30, a + len))));
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

    /// <summary>How far down the hours a time of day is, the hours drawn from <paramref name="first"/>.</summary>
    private static double WeekY(int minutes, int first) => (minutes - first * 60) / 60.0 * HourPx;

    /// <summary>"8 AM", "Noon", "1 PM": an hour as the Calendar writes it.</summary>
    private static string HourName(int h) => h == 12 ? "Noon" : new DateTime(2000, 1, 1, h, 0, 0).ToString("h tt", CultureInfo.InvariantCulture);

    /// <summary>"10:30 AM", a time of day as every time in Simpl is written.</summary>
    private static string ClockWords(DateTime t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>The week's seven days over the hours, in one card.</summary>
    private FrameworkElement WeekGrid(List<DateTime> days, Dictionary<string, List<CalEvent>> byDay)
    {
        var evs = days.Select(d => EventsOn(byDay, d)).ToList();
        // (from 8 in the morning, or the hour of the week's earliest item, to midnight)
        var earliest = evs.SelectMany(l => l).Select(StartMinutes).Where(m => m != null).Select(m => m!.Value / 60).DefaultIfEmpty(8).Min();
        var first = Math.Min(8, earliest);
        var hours = DayEnd / 60 - first;
        var span = hours * HourPx;
        var todayAt = days.FindIndex(d => d == DateTime.Today);

        Grid Row()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gutter) });
            for (var i = 0; i < 7; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }
        Border Hairline(bool across) => across
            ? new Border { Height = 1, SnapsToDevicePixels = true }.Bg(B.Divider)
            : new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true }.Bg(B.Divider);

        // the days' names and numbers
        var head = Row();
        head.Margin = new Thickness(0, 4, 0, 4);
        for (var i = 0; i < 7; i++) head.Children.Add(WeekHead(days[i], evs[i].Count).Col(i + 1));

        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        rows.Children.Add(head);
        rows.Children.Add(Hairline(true));

        // the day-long ones, over the hours
        var allDay = evs.Select(l => l.Where(e => StartMinutes(e) == null).ToList()).ToList();
        if (allDay.Any(l => l.Count > 0))
        {
            var strip = Row();
            var label = K.Text("all-day", Ty.Caption, B.Text2);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            strip.Children.Add(label.Margin(0, 6, 8, 6).Col(0));
            for (var i = 0; i < 7; i++)
            {
                var v = new Stack { Orientation = Orientation.Vertical, Spacing = 2, Margin = new Thickness(3) };
                foreach (var ev in allDay[i]) v.Children.Add(Chip(ev));
                strip.Children.Add(v.Col(i + 1));
                strip.Children.Add(Hairline(false).Col(i + 1));
            }
            rows.Children.Add(strip);
            rows.Children.Add(Hairline(true));
        }

        // the hours
        var body = Row();
        body.Height = span + 2 * GridInset;
        var inner = new Thickness(0, GridInset, 0, GridInset);
        // (a hairline at each hour, past the hours' names)
        var lines = new Stack { Orientation = Orientation.Vertical, Spacing = 0, Margin = inner, IsHitTestVisible = false };
        for (var h = 0; h < hours; h++)
        {
            var line = new Border { Height = HourPx, BorderThickness = new Thickness(0, 1, 0, 0), SnapsToDevicePixels = true, Opacity = 0.7 };
            line.SetResourceReference(Border.BorderBrushProperty, B.Divider);
            lines.Children.Add(line);
        }
        body.Children.Add(lines.Col(1, 7));
        // the hours' names, each on its line
        var gutter = new Grid { Margin = inner, IsHitTestVisible = false };
        var names = new List<(int Minute, TextBlock Text)>();
        for (var h = 0; h < hours; h++)
        {
            var t = K.Text(HourName(first + h), Ty.Caption, B.Text2);
            t.FontSize = 11;
            t.HorizontalAlignment = HorizontalAlignment.Right;
            t.VerticalAlignment = VerticalAlignment.Top;
            t.Margin = new Thickness(0, h * HourPx - 8, 8, 0);
            gutter.Children.Add(t);
            names.Add(((first + h) * 60, t));
        }
        body.Children.Add(gutter.Col(0));

        var columns = new List<Grid>();
        for (var i = 0; i < 7; i++)
        {
            var cell = new Grid { ClipToBounds = false };
            if (i == todayAt) cell.Children.Add(new Border { Opacity = 0.05, IsHitTestVisible = false }.Bg(B.Accent));
            var panel = new WeekDayPanel(first) { Margin = new Thickness(3, GridInset, 3, GridInset) };
            foreach (var s in DaySlots(days[i], evs[i], first)) panel.Children.Add(WeekBlock(s, days[i], i, cell, columns, first));
            cell.Children.Add(panel);
            cell.Children.Add(Hairline(false));
            columns.Add(cell);
            body.Children.Add(cell.Col(i + 1));
        }
        rows.Children.Add(body);

        if (todayAt >= 0) NowOn(columns[todayAt], gutter, names, first);
        return K.Card(rows, new Thickness(0), 12);
    }

    /// <summary>
    /// Now, on today's column: a red line at the minute, and the time beside it in the hours (the hour it falls near left
    /// out) — kept there as the minutes go by, while the week is on screen.
    /// </summary>
    private static void NowOn(Grid column, Grid gutter, List<(int Minute, TextBlock Text)> names, int first)
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
            var on = m >= first * 60 && m < DayEnd;
            line.Visibility = time.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (minute, text) in names) text.Visibility = on && Math.Abs(minute - m) < 15 ? Visibility.Hidden : Visibility.Visible;
            if (!on) return;
            var y = WeekY(m, first);
            line.Margin = new Thickness(0, GridInset + y - 4.5, 0, 0);
            time.Margin = new Thickness(0, y - 8, 8, 0);
            time.Text = t.ToString("h:mm", CultureInfo.InvariantCulture);
        }
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) => Paint();
        column.Loaded += (_, _) => { Paint(); timer.Start(); };
        column.Unloaded += (_, _) => timer.Stop();
        Paint();
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
    private Button WeekBlock(WeekSlot s, DateTime day, int dayIndex, Grid column, List<Grid> columns, int first)
    {
        var ev = s.Ev;
        var tiny = s.Height < 32;
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
            var lines = Math.Max(1, (int)Math.Floor((s.Height - 22) / 16));
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

        var b = K.Subtle(g, null, new Thickness(0), 6);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.VerticalContentAlignment = VerticalAlignment.Stretch;
        b.MinHeight = 0;
        b.MinWidth = 0;
        b.Click += (_, _) => peek.Press(ev, b);
        b.ToolTip = string.Join(" · ", new[] { plan ? "Planned" : null, ev.Title, ev.Sub, plan ? "drag to move it" : null }.Where(x => !string.IsNullOrEmpty(x)));
        WeekDayPanel.SetSlot(b, s);
        Item(b, ev, menu: true);
        if (plan && CalendarKit.Text(ev.PlanId) is { } pid) Draggable(b, s, pid, day, dayIndex, column, columns, first, time, clock);
        return b;
    }

    /// <summary>
    /// A plan's block dragged to another time: it follows the pointer a quarter of an hour at a time — the time on it
    /// saying where it would go — and across to another day, lifted over the days beside; let go, the plan is moved there
    /// and the week read again. Escape, or a drag back to where it was, puts it back. A click without a drag is the
    /// block's own: its preview opens.
    /// </summary>
    private void Draggable(Button b, WeekSlot s, string pid, DateTime day, int dayIndex, Grid column, List<Grid> columns, int first, Run time, string clock)
    {
        var shift = new TranslateTransform();
        b.RenderTransform = shift;
        var len = s.B - s.A;
        var top = first * 60;
        Point? from = null;
        var engaged = false;
        var days = 0;
        var minutes = 0;
        var name = AutomationProperties.GetName(b);
        AutomationProperties.SetHelpText(b, "Shows a preview; drag it to move the plan");

        void Lift(bool on)
        {
            Panel.SetZIndex(column, on ? 5 : 0);
            b.Effect = on ? new DropShadowEffect { BlurRadius = 16, ShadowDepth = 5, Direction = 270, Opacity = 0.28, Color = Colors.Black } : null;
            Mouse.OverrideCursor = on ? Cursors.SizeAll : null;
        }
        void Back()
        {
            engaged = false;
            from = null;
            days = minutes = 0;
            Lift(false);
            Motion.To(shift, TranslateTransform.XProperty, 0, Motion.Fast);
            Motion.To(shift, TranslateTransform.YProperty, 0, Motion.Fast);
            time.Text = clock;
            AutomationProperties.SetName(b, name);
        }

        b.PreviewMouseLeftButtonDown += (_, e) => from = e.GetPosition(columns[0].Parent as IInputElement ?? b);
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
            var at = (int)Math.Round((s.A + dy / HourPx * 60) / Snap) * Snap;
            at = Math.Clamp(at, top, Math.Max(top, DayEnd - Math.Min(len, DayEnd - top)));
            var w = column.ActualWidth;
            var d = w > 0 ? Math.Clamp((int)Math.Round(dx / w), -dayIndex, 6 - dayIndex) : 0;
            if (d == days && at - s.A == minutes) return;
            days = d;
            minutes = at - s.A;
            // (the shift to another day measured on the columns as they are: they are not all one width to the pixel)
            var x = d == 0 ? 0 : columns[dayIndex + d].TranslatePoint(new Point(0, 0), column).X;
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.X = x;
            shift.Y = minutes / 60.0 * HourPx;
            var when = day.AddDays(d).AddMinutes(at);
            time.Text = d == 0 ? ClockWords(when) : $"{when.ToString("ddd", CultureInfo.InvariantCulture)} {ClockWords(when)}";
            AutomationProperties.SetName(b, $"Move to {when.ToString("dddd", CultureInfo.InvariantCulture)} {ClockWords(when)}");
        };
        b.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!engaged) { from = null; return; }
            // (the press a drag ends with opens nothing: the button never hears it let go)
            e.Handled = true;
            engaged = false;
            from = null;
            b.ReleaseMouseCapture();
            if (days == 0 && minutes == 0) { Back(); return; }
            Lift(false);
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
            if (!await engine.Act("planMove", new { id = pid, at = iso })) Back();
            Read();
            engine.DataChanged();
        }
    }

    /// <summary>A day of the week's grid: each block at its time and as tall as it lasts, side by side with those it
    /// overlaps, the hours drawn from <see cref="first"/>.</summary>
    private sealed class WeekDayPanel(int first) : Panel
    {
        public static readonly DependencyProperty SlotProperty = DependencyProperty.RegisterAttached(
            "Slot", typeof(WeekSlot), typeof(WeekDayPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsParentArrange));

        public static void SetSlot(UIElement e, WeekSlot s) => e.SetValue(SlotProperty, s);

        protected override Size MeasureOverride(Size available)
        {
            var w = double.IsInfinity(available.Width) ? 120 : available.Width;
            foreach (UIElement c in InternalChildren)
                if (c.GetValue(SlotProperty) is WeekSlot s) c.Measure(new Size(Math.Max(0, w / s.Lanes - 3), s.Height));
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size final)
        {
            foreach (UIElement c in InternalChildren)
            {
                if (c.GetValue(SlotProperty) is not WeekSlot s) continue;
                var lane = final.Width / s.Lanes;
                c.Arrange(new Rect(s.Lane * lane + 1, WeekY(s.A, first), Math.Max(0, lane - 3), s.Height));
            }
            return final;
        }
    }
}

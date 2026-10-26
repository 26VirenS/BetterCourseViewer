using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// (0.3.66, the Mac's 1.3.172 and the web's 2.99.113 and 2.99.114) A plan's popup, out of its block on the calendar: when
/// it is, what it is for (or the tasks of its group, each opened with a click and taken out with ×), its alert and its
/// notes; Open opens its work, and Edit turns the popup to the plan's settings — a name of its own, the day, a whole day
/// or its times, how it repeats, an alert, a colour, notes — with Delete plan to let it go (its group with it). Done keeps
/// them (native-app.js planEdit): a repeating plan's day changed from one of its later times moves the series by as much.
/// </summary>
internal static class PlanSheet
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    public static async void Show(Engine.Engine engine, CalEvent ev, Window? over, Action changed, bool editing = false)
    {
        if (CalendarKit.Text(ev.PlanId) is not { } pid) return;
        PlanDetail d;
        try { d = await engine.Call<PlanDetail>("planDetail", new { id = pid }); }
        catch { return; } // (the plan gone since the calendar was read: the calendar reads again on its own)

        var holder = new ContentControl { Focusable = false };
        var problem = K.Wrapped("", Ty.Caption, B.Caution);
        problem.Visibility = Visibility.Collapsed;
        var s = new Sheet { Title = "Plan", Body = K.V(12, holder, problem), Width = 420, Floats = true, Light = true, Name = "Plan" };
        var url = CalendarKit.Text(d.Url);
        var open = s.Add("Open", SheetButtonKind.Secondary, () => { if (url != null) engine.OpenWeb(url, d.Title); });
        // (what these do is set below, once everything it uses is made)
        var delete = s.Add("Delete plan", SheetButtonKind.Secondary, closes: false);
        var cancel = s.Add("Cancel", SheetButtonKind.Secondary, closes: false);
        var edit = s.Add("Edit", SheetButtonKind.Primary, closes: false);
        var done = s.Add("Done", SheetButtonKind.Primary, closes: false);

        // the settings as Edit changes them
        var day = DateTime.Today;
        var allDay = false;
        var start = 9 * 60;
        var mins = 60;
        var repeat = "";
        int? alert = null;
        string? tint = null;
        var nameBox = new Wpf.Ui.Controls.TextBox { MinHeight = 32 };
        var notesBox = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Notes", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        void Say(string? why)
        {
            problem.Text = why ?? "";
            problem.Visibility = string.IsNullOrEmpty(why) ? Visibility.Collapsed : Visibility.Visible;
        }

        void Buttons(bool on)
        {
            open.Visible = !on && d.Tasks.Count == 0 && url != null;
            edit.Visible = !on;
            delete.Visible = on;
            cancel.Visible = on;
            done.Visible = on;
        }

        // ---- the plan as it is ----

        void View()
        {
            Say(null);
            var kind = d.Tasks.Count > 0 ? $"Plan · {d.Tasks.Count} tasks" : "Plan";
            if (CalendarKit.Text(d.Repeat) is { } r && d.Repeats.FirstOrDefault(x => x.Key == r) is { } rw) kind += $" · {rw.Label.ToLowerInvariant()}";
            var head = K.V(2, K.Text(kind, Ty.Caption, B.Text2).Strong(), K.Wrapped(Shown(d), Ty.BodyStrong));
            var facts = new Grid();
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Fact(string k, UIElement v)
            {
                var row = facts.RowDefinitions.Count;
                facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var key = K.Text(k, Ty.Caption, B.Text2).Strong();
                key.HorizontalAlignment = HorizontalAlignment.Right;
                key.Margin = new Thickness(0, 2, 14, 6);
                facts.Children.Add(key.Row(row).Col(0));
                if (v is FrameworkElement f) f.Margin = new Thickness(0, 0, 0, 6);
                facts.Children.Add(v.Row(row).Col(1));
            }
            Fact("When", K.Wrapped(WhenWords(ev), Ty.Body));
            if (d.Tasks.Count == 0)
            {
                var forWords = CalendarKit.Text(d.Name) != null ? $"{d.Title}{(CalendarKit.Text(d.Course) is { } c1 ? $" · {c1}" : "")}" : CalendarKit.Text(d.Course) ?? "—";
                Fact("For", K.Columns("auto, *", K.Dot(K.Hex(d.Color), 9).Margin(0, 0, 7, 0).Col(0), K.Wrapped(forWords, Ty.Body).Col(1)));
                if (CalendarKit.Text(d.DueWords) is { } due) Fact("Due", K.Text(due, Ty.Body));
            }
            if (d.Alert is { } a) Fact("Alert", K.Text(d.Alerts.FirstOrDefault(x => x.Minutes == a)?.Label ?? $"{a} minutes before", Ty.Body));
            if (CalendarKit.Text(d.Notes) is { } n) Fact("Notes", K.Wrapped(n, Ty.Body));
            var body = K.V(14, head, facts);
            if (d.Tasks.Count > 0)
            {
                var list = new Stack { Orientation = Orientation.Vertical, Spacing = 2 };
                foreach (var m in d.Tasks) list.Children.Add(Member(m));
                body.Children.Add(K.Card(list, new Thickness(4), 8));
                body.Children.Add(K.Text("Take a task out with ×: it goes back to To Do.", Ty.Caption, B.Text2));
            }
            holder.Content = body;
            Buttons(false);
        }

        // a task of a group: what it is and when it is due, opened with a click; × takes it out (back to To Do)
        FrameworkElement Member(PlanTask m)
        {
            var words = K.V(1, K.Text(m.Title, Ty.Body), K.Text(string.Join(" · ", new[] { m.Course, m.DueWords }.Where(x => !string.IsNullOrEmpty(x))), Ty.Caption, B.Text2));
            var openIt = K.Subtle(K.Columns("auto, *", K.Dot(K.Hex(m.Color), 9).Margin(0, 0, 9, 0).Col(0), words.Col(1)), () =>
            {
                if (CalendarKit.Text(m.Url) is not { } u) return;
                s.Close();
                engine.OpenWeb(u, m.Title);
            }, new Thickness(8, 5, 8, 5), 6);
            openIt.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(openIt, m.Title);
            var take = K.IconButton(Wpf.Ui.Controls.SymbolRegular.Dismiss24, () => _ = TakeOut(m), $"Take {m.Title} out of the group", 12, 28);
            take.ToolTip = "Take it out";
            take.VerticalAlignment = VerticalAlignment.Center;
            return K.Columns("*, auto", openIt.Col(0), take.Margin(4, 0, 2, 0).Col(1));
        }

        async Task TakeOut(PlanTask m)
        {
            if (!await engine.Act("planClear", new { id = m.Id })) { Say("It could not be taken out."); return; }
            changed();
            var rest = d.Tasks.FirstOrDefault(x => x.Id != m.Id);
            if (rest == null) { s.Close(); return; }
            try { d = await engine.Call<PlanDetail>("planDetail", new { id = rest.Id }); }
            catch { s.Close(); return; }
            View();
        }

        // ---- Edit ----

        void Edit()
        {
            Say(null);
            day = (CalendarKit.Date(ev.Date) ?? DateTime.Today).Date;
            var time = CalendarKit.Date(d.At) ?? day;
            var into = (time.Hour * 60 + time.Minute) / 15 * 15;
            allDay = d.AllDay == true;
            start = Math.Min(1425, allDay && into == 0 ? 9 * 60 : into);
            mins = Math.Max(15, Math.Min(d.Mins, 1440 - start));
            repeat = d.Repeat ?? "";
            alert = d.Alert;
            tint = CalendarKit.Text(d.Tint);
            nameBox.PlaceholderText = $"Work on {d.Title}";
            nameBox.Text = d.Name ?? "";
            notesBox.Text = d.Notes ?? "";

            var dayPick = new DatePicker { SelectedDate = day, MinWidth = 160 };
            dayPick.SelectedDateChanged += (_, _) => { if (dayPick.SelectedDate is { } p) day = p.Date; };
            var whole = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = allDay };
            var startBox = new ComboBox { MinWidth = 112 };
            var endBox = new ComboBox { MinWidth = 150 };
            for (var m = 0; m < 1440; m += 15) startBox.Items.Add(new ComboBoxItem { Content = Clock(m), Tag = m });
            void FillEnd()
            {
                endBox.Items.Clear();
                for (var m = start + 15; m <= 1440; m += 15) endBox.Items.Add(new ComboBoxItem { Content = $"{(m == 1440 ? "Midnight" : Clock(m))} ({Length(m - start)})", Tag = m });
                endBox.SelectedItem = endBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == start + mins);
            }
            startBox.SelectedItem = startBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == start);
            FillEnd();
            startBox.SelectionChanged += (_, _) =>
            {
                if (startBox.SelectedItem is not ComboBoxItem { Tag: int v }) return;
                start = v;
                mins = Math.Min(mins, 1440 - start);
                FillEnd();
            };
            endBox.SelectionChanged += (_, _) => { if (endBox.SelectedItem is ComboBoxItem { Tag: int v }) mins = Math.Max(15, v - start); };
            var times = K.H(8, startBox, K.Text("to", Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center), endBox);
            times.Visibility = allDay ? Visibility.Collapsed : Visibility.Visible;
            whole.Checked += (_, _) => { allDay = true; times.Visibility = Visibility.Collapsed; };
            whole.Unchecked += (_, _) => { allDay = false; times.Visibility = Visibility.Visible; };

            var repeatBox = new ComboBox { MinWidth = 160 };
            foreach (var r in d.Repeats) repeatBox.Items.Add(new ComboBoxItem { Content = r.Label, Tag = r.Key });
            repeatBox.SelectedItem = repeatBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == repeat) ?? repeatBox.Items.Cast<ComboBoxItem>().FirstOrDefault();
            repeatBox.SelectionChanged += (_, _) => { if (repeatBox.SelectedItem is ComboBoxItem { Tag: string v }) repeat = v; };
            var alertBox = new ComboBox { MinWidth = 160 };
            foreach (var a in d.Alerts) alertBox.Items.Add(new ComboBoxItem { Content = a.Label, Tag = a.Minutes });
            alertBox.SelectedItem = alertBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int?)i.Tag == alert) ?? alertBox.Items.Cast<ComboBoxItem>().FirstOrDefault();
            alertBox.SelectionChanged += (_, _) => { if (alertBox.SelectedItem is ComboBoxItem i) alert = (int?)i.Tag; };

            var swatches = new Stack { Orientation = Orientation.Horizontal, Spacing = 4 };
            var rings = new List<(string? Tint, Ellipse Ring)>();
            void Paint() { foreach (var (t, ring) in rings) ring.Opacity = t == tint ? 1 : 0; }
            void Swatch(string? t, string shown)
            {
                var ring = new Ellipse { Width = 22, Height = 22, StrokeThickness = 2, IsHitTestVisible = false };
                ring.SetResourceReference(Shape.StrokeProperty, B.Text);
                var g = new Grid { Width = 22, Height = 22, Children = { new Ellipse { Width = 16, Height = 16, Fill = K.Brush(K.Hex(shown)) }, ring } };
                var b = K.Subtle(g, () => { tint = t; Paint(); }, new Thickness(2), 12);
                AutomationProperties.SetName(b, t == null ? "The course’s colour" : $"Colour {shown}");
                if (t == null) b.ToolTip = "The course’s colour";
                rings.Add((t, ring));
                swatches.Children.Add(b);
            }
            Swatch(null, d.Color ?? "#0a84ff");
            foreach (var t in d.Tints) Swatch(t, t);
            Paint();

            var form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Field(string k, FrameworkElement v)
            {
                var row = form.RowDefinitions.Count;
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var key = K.Text(k, Ty.Body, B.Text2);
                key.HorizontalAlignment = HorizontalAlignment.Right;
                key.VerticalAlignment = VerticalAlignment.Center;
                key.Margin = new Thickness(0, 0, 14, 8);
                v.Margin = new Thickness(0, 0, 0, 8);
                v.HorizontalAlignment = HorizontalAlignment.Left;
                form.Children.Add(key.Row(row).Col(0));
                form.Children.Add(v.Row(row).Col(1));
            }
            Field("Day", dayPick);
            Field("All day", whole);
            var timeRow = form.RowDefinitions.Count;
            Field("Time", times);
            var timeKey = form.Children.Cast<UIElement>().First(c => Grid.GetRow(c) == timeRow && Grid.GetColumn(c) == 0);
            whole.Checked += (_, _) => timeKey.Visibility = Visibility.Collapsed;
            whole.Unchecked += (_, _) => timeKey.Visibility = Visibility.Visible;
            timeKey.Visibility = times.Visibility;
            Field("Repeat", repeatBox);
            Field("Alert", alertBox);
            Field("Colour", swatches);
            holder.Content = K.V(12, K.Text("Edit plan", Ty.Caption, B.Text2).Strong(), nameBox, form, notesBox);
            Buttons(true);
            nameBox.Focus();
        }

        async Task Save()
        {
            var at = allDay ? day : day.AddMinutes(start);
            var args = new JsonObject
            {
                ["id"] = d.Id,
                ["name"] = nameBox.Text.Trim(),
                ["at"] = Iso(at),
                ["allDay"] = allDay,
                ["repeat"] = repeat,
                ["notes"] = notesBox.Text.Trim(),
                ["alert"] = alert,
                ["tint"] = tint,
            };
            if (!allDay) args["mins"] = mins;
            if (CalendarKit.Text(ev.Occurs) is { } occurs) args["occurs"] = occurs; // (one of a repeating plan's times: the series moves by as much)
            done.Enabled = false;
            try
            {
                var r = await engine.Call<OK>("planEdit", args);
                if (r.Ok == false) { Say("The plan could not be saved."); return; }
            }
            catch (Exception e)
            {
                Say(e.Message);
                return;
            }
            finally { done.Enabled = true; }
            s.Close();
            changed();
        }

        async Task Delete()
        {
            if (!await engine.Act("planClear", new { id = d.Id, group = true })) { Say("The plan could not be deleted."); return; }
            s.Close();
            changed();
        }

        delete.Click = () => _ = Delete();
        cancel.Click = View;
        edit.Click = Edit;
        done.Click = () => _ = Save();
        if (editing) Edit(); else View();
        Dialogs.Show(s, over);
    }

    /// <summary>Its own name, or its group's tasks' one after another, or its work's.</summary>
    private static string Shown(PlanDetail d) =>
        CalendarKit.Text(d.Name) ?? (d.Tasks.Count > 0 ? string.Join(" + ", d.Tasks.Select(t => t.Title)) : d.Title);

    /// <summary>The block's own time ("Wed, Oct 28 · 9:00 AM – 10:30 AM", or "· all day").</summary>
    private static string WhenWords(CalEvent ev)
    {
        if (CalendarKit.Date(ev.Date) is not { } a) return ev.Time ?? "";
        var day = a.ToString("ddd, MMM d", En);
        if (ev.AllDay == true) return $"{day} · all day";
        var from = a.ToString("h:mm tt", En);
        return CalendarKit.Date(ev.End) is { } b && b > a ? $"{day} · {from} – {b.ToString("h:mm tt", En)}" : $"{day} · {from}";
    }

    /// <summary>"4:15 PM", <paramref name="minutes"/> into a day.</summary>
    private static string Clock(int minutes) => DateTime.Today.AddMinutes(minutes).ToString("h:mm tt", En);

    /// <summary>"45m", "1h", "1h 15m".</summary>
    private static string Length(int m) => m < 60 ? $"{m}m" : m % 60 == 0 ? $"{m / 60}h" : $"{m / 60}h {m % 60}m";

    private static string Iso(DateTime local) =>
        DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", En);
}

/// <summary>(0.3.66) What native-app.js planDetail answers: a plan's settings, its group's tasks (none for a plan alone),
/// and the repeats, alerts and colours Edit offers.</summary>
internal sealed class PlanDetail
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string Title { get; set; } = "";
    public string? Course { get; set; }
    public string? Color { get; set; }
    public string? Tint { get; set; }
    public string? At { get; set; }
    public int Mins { get; set; } = 60;
    public bool? AllDay { get; set; }
    public string? Repeat { get; set; }
    public int? Alert { get; set; }
    public string? Notes { get; set; }
    public string? DueWords { get; set; }
    public string? Url { get; set; }
    public List<PlanTask> Tasks { get; set; } = new();
    public List<PlanRepeat> Repeats { get; set; } = new();
    public List<PlanAlert> Alerts { get; set; } = new();
    public List<string> Tints { get; set; } = new();
}

internal sealed class PlanTask
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Course { get; set; }
    public string? Color { get; set; }
    public string? DueWords { get; set; }
    public string? Url { get; set; }
}

internal sealed class PlanRepeat
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

internal sealed class PlanAlert
{
    public int? Minutes { get; set; }
    public string Label { get; set; } = "";
}

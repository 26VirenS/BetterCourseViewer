using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// (0.3.53) Find Appointment (the Mac's AppointmentsPopup, the web's sheet): Canvas's Scheduler — office hours,
/// conferences, any times a teacher opened — read through the page (native-app.js `appointments`). The class first; then
/// each group as a card: its place, how long a time is and how many each student may hold, the time held (Give Back), and
/// its days, each opening to its times. A press on an open time reserves it; Canvas's own words when it will not. A time
/// reserved goes on the calendar (<c>changed</c>).
/// </summary>
internal static class AppointmentsSheet
{
    public static void Show(Engine.Engine engine, Action changed, Window? over)
    {
        AppointmentsData? data = null;
        string? course = null;
        // (the days opened to their times, a day key each: the day held, else the first with a time open, to begin with)
        var open = new HashSet<string>();
        string? busy = null;
        string? said = null;
        var saidBad = false;
        var host = new Border();

        string? Picked()
        {
            if (data == null) return null;
            if (course != null && data.Classes.Any(c => c.Code == course)) return course;
            return data.Classes.FirstOrDefault()?.Code;
        }

        FrameworkElement Said(string text, bool bad)
        {
            var icon = K.Icon(bad ? SymbolRegular.Warning24 : SymbolRegular.CheckmarkCircle24, 16, bad ? B.Caution : B.Success, filled: true);
            var line = K.Columns("auto, *", icon.Margin(0, 1, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), K.Wrapped(text, Ty.Body).Col(1));
            return new Border { Child = line, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8) }.Bg(bad ? B.CautionBack : B.SuccessBack);
        }

        string? Meta(AppointmentGroup g)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(g.Location)) parts.Add(g.Location!);
            if ((g.Minutes ?? 0) > 0) parts.Add($"{g.Minutes} min each");
            if ((g.Max ?? 0) > 0) parts.Add(g.Max == 1 ? "one time each" : $"{g.Max} times each");
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        FrameworkElement Classes(List<AppointmentClass> list)
        {
            var flow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, -6, -6) };
            var picked = Picked();
            foreach (var c in list)
            {
                var on = c.Code == picked;
                var name = K.Text(c.Name, Ty.Body);
                if (on) name.Strong();
                var b = K.Subtle(K.H(6, K.Dot(K.Hex(c.Color), 8), name), () => { course = c.Code; Paint(); }, new Thickness(11, 5, 11, 6), 14);
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                if (on) b.Background = K.Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)64 : (byte)44, Support.Appearance.Accent.R, Support.Appearance.Accent.G, Support.Appearance.Accent.B));
                else b.Bg(B.Subtle);
                b.Margin = new Thickness(0, 0, 6, 6);
                b.ToolTip = c.Name;
                System.Windows.Automation.AutomationProperties.SetName(b, c.Name + (on ? ", chosen" : ""));
                flow.Children.Add(b);
            }
            var label = K.Text("Class", Ty.Body, B.Text2).Strong().Margin(0, 6, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top);
            return K.Columns("auto, *", label.Col(0), flow.Col(1));
        }

        FrameworkElement Held(AppointmentHeld h)
        {
            var words = K.Text($"Your time: {h.When}", Ty.Body);
            words.FontWeight = FontWeights.Medium;
            var give = K.Button("Give Back", () => _ = GiveBack(h));
            give.MinWidth = 0;
            give.IsEnabled = busy == null;
            give.ToolTip = "Give this time back, so someone else can take it";
            var spin = busy != null && busy == h.ReservationId ? K.Spinner(14).Margin(0, 0, 8, 0) : null;
            var row = K.Columns("auto, *, auto, auto",
                K.Icon(SymbolRegular.CheckmarkCircle24, 16, B.Success, filled: true).Margin(0, 0, 10, 0).Col(0),
                words.Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
                spin?.Col(2), give.Col(3));
            return new Border { Child = row, Padding = new Thickness(12, 6, 8, 6), CornerRadius = new CornerRadius(8) }.Bg(B.SuccessBack);
        }

        FrameworkElement Slot(AppointmentGroup g, AppointmentDay day, AppointmentSlot s)
        {
            var full = g.Full == true;
            var canPick = s.Open && !full && busy == null;
            var tip = s.Reserved ? "Your time" : !s.Open ? "Taken" : full ? "You hold the most times this allows: give one back first" : $"Reserve {day.Label} at {s.Time}";
            var time = K.Text(s.Time, Ty.Body, s.Reserved ? B.OnAccent : s.Open && !full ? B.Text : B.Text2);
            time.FontWeight = FontWeights.Medium;
            System.Windows.Documents.Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
            if (!s.Open && !s.Reserved) time.Struck();
            var inner = K.H(5,
                s.Reserved ? K.Icon(SymbolRegular.Checkmark24, 11, B.OnAccent) : null,
                time,
                busy == s.Id ? K.Spinner(12) : null);
            var b = K.Subtle(inner, canPick ? () => _ = Reserve(day, s) : null, new Thickness(12, 5, 12, 6), 14);
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            if (s.Reserved) b.Bg(B.Success);
            else if (s.Open && !full) b.Background = K.Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)52 : (byte)34, Support.Appearance.Accent.R, Support.Appearance.Accent.G, Support.Appearance.Accent.B));
            else b.Bg(B.Subtle);
            // (a time that cannot be taken is greyed by its words, not dimmed: the one held stays bright)
            b.IsHitTestVisible = canPick;
            b.Focusable = canPick;
            b.Margin = new Thickness(0, 0, 6, 6);
            b.ToolTip = tip;
            ToolTipService.SetShowOnDisabled(b, true);
            System.Windows.Automation.AutomationProperties.SetName(b, $"{s.Time}, {(s.Reserved ? "your time" : s.Open ? "open" : "taken")}");
            return b;
        }

        FrameworkElement Day(AppointmentGroup g, AppointmentDay day)
        {
            var isOpen = open.Contains(day.Key);
            var label = K.Text(day.Label, Ty.BodyStrong);
            var sub = K.Text(day.Sub, Ty.Body, day.Held == true ? B.Success : B.Text2);
            var chevron = K.Icon(isOpen ? SymbolRegular.ChevronDown20 : SymbolRegular.ChevronRight20, 12, B.Text3);
            var head = K.Columns("auto, *, auto", label.Margin(0, 0, 10, 0).Col(0), sub.Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1), chevron.Col(2));
            var toggle = K.Subtle(head, () => { if (!open.Remove(day.Key)) open.Add(day.Key); Paint(); }, new Thickness(12, 9, 12, 9), 6);
            System.Windows.Automation.AutomationProperties.SetName(toggle, $"{day.Label}, {day.Sub}, {(isOpen ? "open" : "closed")}");
            if (!isOpen) return toggle;
            var flow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 6, 6) };
            foreach (var s in day.Slots) flow.Children.Add(Slot(g, day, s));
            return K.V(0, toggle, flow);
        }

        FrameworkElement Card(AppointmentGroup g)
        {
            var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = K.Brush(K.Hex(g.Color)), Margin = new Thickness(0, 5, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            var words = K.V(2, K.Wrapped(g.Title, Ty.BodyStrong));
            if (Meta(g) is { } m) words.Children.Add(K.Wrapped(m, Ty.Body, B.Text2));
            var stack = K.V(12, K.Columns("auto, *", swatch.Col(0), words.Col(1)));
            if (!string.IsNullOrWhiteSpace(g.Description)) stack.Children.Add(K.Wrapped(g.Description!, Ty.Body, B.Text2, 4));
            if (g.GroupSignup == true)
            {
                stack.Children.Add(K.Wrapped($"A sign-up for your group: reserve it as the group in {engine.LmsName}.", Ty.Body, B.Text2));
                var go = K.Button($"Open in {engine.LmsName}", () => engine.OpenWebScreen(g.Url ?? "/calendar", g.Title));
                go.HorizontalAlignment = HorizontalAlignment.Left;
                stack.Children.Add(go);
            }
            else
            {
                foreach (var h in g.Mine) stack.Children.Add(Held(h));
                if (g.Days.Count > 0)
                {
                    var days = K.V(0);
                    for (var i = 0; i < g.Days.Count; i++)
                    {
                        if (i > 0) days.Children.Add(K.Divider(12));
                        days.Children.Add(Day(g, g.Days[i]));
                    }
                    stack.Children.Add(new Border { Child = days, CornerRadius = new CornerRadius(8), Padding = new Thickness(0) }.Bg(B.Card2).Stroke(B.CardStroke));
                }
            }
            return K.Card(stack, new Thickness(16), 8);
        }

        void Paint()
        {
            if (data == null)
            {
                host.Child = K.Spinner(24).Margin(0, 80, 0, 80);
                return;
            }
            var list = K.V(16);
            if (said != null) list.Children.Add(Said(said, saidBad));
            if (data.Error != null) list.Children.Add(Said(data.Error, true));
            if (data.Groups.Count == 0)
            {
                if (data.Error == null)
                    list.Children.Add(K.V(8,
                        K.Icon(SymbolRegular.CalendarCheckmark24, 30, B.Text3),
                        K.Text("Nothing to sign up for right now", Ty.BodyStrong).Center(),
                        K.Wrapped("Office hours and conferences your teachers open will wait here.", Ty.Body, B.Text2).Center()).Margin(0, 40, 0, 40));
            }
            else
            {
                if (data.Classes.Count > 1) list.Children.Add(Classes(data.Classes));
                var picked = Picked();
                foreach (var g in data.Groups.Where(g => picked == null || g.Codes.Contains(picked))) list.Children.Add(Card(g));
                list.Children.Add(K.Wrapped("A time you reserve goes on the calendar; the open ones wait here.", Ty.Caption, B.Text2));
            }
            host.Child = list;
        }

        async Task Load()
        {
            AppointmentsData got;
            try { got = await engine.Call<AppointmentsData>("appointments", new { force = true }); }
            catch { got = new AppointmentsData { Error = "Appointments could not be loaded." }; }
            data = got;
            foreach (var g in got.Groups)
            {
                if (g.Days.Any(d => open.Contains(d.Key))) continue;
                var pick = g.Days.FirstOrDefault(d => d.Held == true) ?? g.Days.FirstOrDefault(d => (d.Open ?? 0) > 0) ?? g.Days.FirstOrDefault();
                if (pick != null) open.Add(pick.Key);
            }
            Paint();
        }

        void Say(string text, bool bad)
        {
            said = text;
            saidBad = bad;
        }

        async Task Reserve(AppointmentDay day, AppointmentSlot s)
        {
            busy = s.Id;
            Paint();
            ActReply? r = null;
            try { r = await engine.Call<ActReply>("reserveAppointment", new { id = s.Id }); } catch { }
            busy = null;
            if (r?.Ok == true)
            {
                Say($"Reserved: {day.Label} at {s.Time}. It is on your calendar.", false);
                changed();
            }
            else Say($"{engine.LmsName} would not reserve that time: {r?.Error ?? "no answer"}.", true);
            await Load();
        }

        async Task GiveBack(AppointmentHeld h)
        {
            if (h.ReservationId is not { } id)
            {
                // (held, Canvas says, but not by which event: its own page can undo it)
                engine.OpenWebScreen("/calendar", "Calendar");
                return;
            }
            busy = id;
            Paint();
            ActReply? r = null;
            try { r = await engine.Call<ActReply>("cancelReservation", new { id }); } catch { }
            busy = null;
            if (r?.Ok == true)
            {
                Say("Your time is given back.", false);
                changed();
            }
            else Say($"{engine.LmsName} would not give it back: {r?.Error ?? "no answer"}.", true);
            await Load();
        }

        var lede = K.Text("Office hours and sign-ups with a time to reserve", Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom);
        var body = new ScrollViewer
        {
            Content = host, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0),
        };
        Paint();
        var sheet = new Sheet { Title = "Find Appointment", Body = K.V(12, lede, body), Width = 580, Floats = true };
        sheet.Add("Done", SheetButtonKind.Primary);
        Dialogs.Show(sheet, over);
        _ = Load();
    }
}

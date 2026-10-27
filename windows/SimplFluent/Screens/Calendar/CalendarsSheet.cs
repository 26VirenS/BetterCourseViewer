using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

/// <summary>
/// The calendars shown (the Mac's CalendarsPopover), as a Windows 11 dialog over the window: each with its colour, by
/// where it comes from — Canvas's (your own courses first, the rest under Other Calendars; ten at most, as Canvas allows:
/// the others wait, greyed, until one is turned off), (0.3.81) then each calendar account's, then those linked by their
/// address — and, under them, Sync, where accounts and links are added or taken away. Each change is kept at once and the
/// calendar read again behind the dialog.
/// </summary>
internal static class CalendarsSheet
{
    public static void Show(Engine.Engine engine, List<CalendarChoice> choices, Action changed, Action openSync, Window? over)
    {
        var on = new HashSet<string>(choices.Where(c => c.On).Select(c => c.Code));
        var own = choices.Where(c => c.Own == true).ToList();
        var other = choices.Where(c => c.Own != true).ToList();
        var links = new List<SyncLink>();
        var count = K.Text("", Ty.Body, B.Text2);
        var foot = K.Wrapped("", Ty.Caption, B.Text2);
        var boxes = new List<(CalendarChoice Choice, CheckBox Box)>();
        var quiet = false;
        var list = new Stack { Orientation = Orientation.Vertical, Spacing = 2 };

        int Shown() => on.Count + CalendarAccounts.ShownCount + links.Count(l => l.On);

        void Count() => count.Text = $"{Shown()} shown";

        void Paint()
        {
            var full = on.Count >= 10;
            quiet = true;
            foreach (var (c, b) in boxes)
            {
                b.IsChecked = on.Contains(c.Code);
                b.IsEnabled = on.Contains(c.Code) || !full;
            }
            quiet = false;
            foot.Text = full ? "Ten at most: turn one off to show another."
                : engine.OnBrightspace ? "At most 10 of these show at once." : "Canvas shows at most 10 of these at once.";
            foot.Fg(full ? B.Caution : B.Text2);
            Count();
        }

        void Set(CalendarChoice c, bool value)
        {
            if (quiet) return;
            if (value && !on.Contains(c.Code) && on.Count >= 10) { Paint(); return; }
            if (value) on.Add(c.Code); else on.Remove(c.Code);
            Paint();
            var codes = choices.Select(x => x.Code).Where(on.Contains).ToList();
            _ = Save(codes);
        }

        async Task Save(List<string> codes)
        {
            await engine.Act("setCalendars", new { codes });
            changed();
        }

        FrameworkElement Label(string? color, string name, string? detail = null, bool warn = false, string? why = null)
        {
            var words = K.H(6, K.Text(name, Ty.Body));
            if (!string.IsNullOrEmpty(detail)) words.Children.Add(K.Text(detail, Ty.Caption, B.Text2));
            if (warn)
            {
                var mark = K.Icon(SymbolRegular.Warning24, 12, B.Critical, filled: true);
                mark.VerticalAlignment = VerticalAlignment.Center;
                mark.ToolTip = why;
                words.Children.Add(mark);
            }
            return K.Columns("auto, *", K.Dot(K.Hex(color), 11).Margin(0, 0, 8, 0).Col(0), words.Col(1));
        }

        CheckBox Box(FrameworkElement label, string name, bool isOn)
        {
            var b = new CheckBox { Content = label, IsChecked = isOn, ToolTip = name, VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 28, Padding = new Thickness(8, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            return b;
        }

        CheckBox CanvasRow(CalendarChoice c)
        {
            var b = Box(Label(c.Color, c.Name), c.Name, on.Contains(c.Code));
            b.Checked += (_, _) => Set(c, true);
            b.Unchecked += (_, _) => Set(c, false);
            boxes.Add((c, b));
            return b;
        }

        CheckBox AccountRow(AccountCalendar c, CalendarAccount a)
        {
            var b = Box(Label(c.Color, c.Name, c.Group), c.Name, c.On);
            b.IsEnabled = !a.SignedOut;
            void Turn(bool value)
            {
                CalendarAccounts.Show(c.Id, a.Id, value);
                Count();
                changed();
            }
            b.Checked += (_, _) => Turn(true);
            b.Unchecked += (_, _) => Turn(false);
            return b;
        }

        CheckBox LinkRow(SyncLink l)
        {
            var b = Box(Label(l.Color, l.Name, warn: l.Error.Length > 0, why: l.Error), l.Name, l.On);
            async Task Turn(bool value)
            {
                l.On = value;
                Count();
                await engine.Act("calShow", new { id = l.Id, on = value });
                changed();
            }
            b.Checked += (_, _) => _ = Turn(true);
            b.Unchecked += (_, _) => _ = Turn(false);
            return b;
        }

        // a section's name — the place its calendars come from — and who, for an account
        FrameworkElement Heading(string title, string? detail, bool first)
        {
            var h = K.H(6, K.Text(title.ToUpper(CultureInfo.InvariantCulture), Ty.Caption, B.Text2).Strong());
            if (!string.IsNullOrEmpty(detail))
            {
                var who = K.Text(detail, Ty.Caption, B.Text3);
                who.TextTrimming = TextTrimming.CharacterEllipsis;
                h.Children.Add(who);
            }
            return h.Margin(0, first ? 0 : 12, 0, 4);
        }

        void Build()
        {
            list.Children.Clear();
            boxes.Clear();
            if (choices.Count == 0 && CalendarAccounts.Accounts.Count == 0 && links.Count == 0)
            {
                list.Children.Add(K.Text("No calendars to show.", Ty.Body, B.Text2));
                Paint();
                return;
            }
            if (choices.Count > 0)
            {
                list.Children.Add(Heading(engine.OnBrightspace ? "Brightspace" : "Canvas", null, true));
                foreach (var c in own) list.Children.Add(CanvasRow(c));
                if (other.Count > 0)
                {
                    list.Children.Add(K.Text("Other Calendars", Ty.Caption, B.Text2).Strong().Margin(0, own.Count == 0 ? 0 : 8, 0, 4));
                    foreach (var c in other) list.Children.Add(CanvasRow(c));
                }
                list.Children.Add(foot.Margin(0, 4, 0, 0));
            }
            var firstAccount = true;
            foreach (var a in CalendarAccounts.Accounts)
            {
                list.Children.Add(Heading(CalendarKinds.Name(a.Kind), a.Name, choices.Count == 0 && firstAccount));
                firstAccount = false;
                if (a.Calendars.Count == 0)
                    list.Children.Add(K.Text(a.SignedOut ? "Sign in again in Sync to choose its calendars." : "Reading its calendars…", Ty.Caption, B.Text2));
                foreach (var c in a.Calendars) list.Children.Add(AccountRow(c, a));
            }
            if (links.Count > 0)
            {
                list.Children.Add(Heading("Linked by Address", null, choices.Count == 0 && CalendarAccounts.Accounts.Count == 0));
                foreach (var l in links) list.Children.Add(LinkRow(l));
            }
            Paint();
        }

        async Task ReadLinks()
        {
            try
            {
                var d = await engine.Call<SyncData>("calSync");
                links = d.Links;
                Build();
            }
            catch (Exception e) { Log.Write("calendars: links not read: " + e.Message); }
        }

        // (the title the popup's own, beside its ×: how many are shown under it)
        var head = count.Align(HorizontalAlignment.Left, VerticalAlignment.Bottom);
        var body = new ScrollViewer
        {
            Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0),
        };
        Sheet? s = null;
        var sync = K.Subtle(K.H(8, K.Icon(SymbolRegular.ArrowSync24, 14, B.AccentText), K.Text("Add or Remove Calendars in Sync…", Ty.Body, B.AccentText)),
            () =>
            {
                s?.Close();
                // (once this has gone, so the two are never over the window together)
                System.Windows.Application.Current.Dispatcher.InvokeAsync(openSync, System.Windows.Threading.DispatcherPriority.Background);
            }, new Thickness(4, 4, 8, 4), 6);
        sync.HorizontalAlignment = HorizontalAlignment.Left;
        System.Windows.Automation.AutomationProperties.SetName(sync, "Add or remove calendars in Sync");
        var all = K.V(12, head, body, K.Divider(), sync);
        Build();
        // (an account's calendars read for the first time, or signed out of, shown as it lands)
        void Moved() => list.Dispatcher.InvokeAsync(Build);
        CalendarAccounts.Changed += Moved;
        s = new Sheet { Title = "Calendars", Body = all, Width = 400, Floats = true, Light = true };
        s.Add("Done", SheetButtonKind.Primary);
        s.Closed += () => CalendarAccounts.Changed -= Moved;
        Dialogs.Show(s, over);
        _ = ReadLinks();
    }
}

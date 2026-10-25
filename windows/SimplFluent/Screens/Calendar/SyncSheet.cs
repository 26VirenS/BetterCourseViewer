using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// (0.3.60) Sync (the Mac's SyncPopup, the web's sheet), read through the page (native-app.js <c>calSync</c>): Canvas's
/// own calendar feed handed to a calendar app — Outlook's and Google's add-by-address pages open in the browser, the
/// calendar app Windows opens webcal links in subscribes straight away, or the link is copied; the student's own
/// calendars linked by their private address (the app reads the file, the page parses it: background.js and
/// lib/ics.js), each shown or hidden on the Calendar or unlinked; and Send plans, which puts each plan on the Canvas
/// calendar so the feed carries it out with everything else. What changes here is read again behind it (<c>changed</c>).
/// </summary>
internal static class SyncSheet
{
    public static void Show(Engine.Engine engine, Action changed, Window? over)
    {
        SyncData? data = null;
        string? said = null;
        var saidBad = false;
        var linking = false;
        string? unlinking = null;
        var showWhere = false;
        var draft = "";
        var host = new Border();

        FrameworkElement Said(string text, bool bad)
        {
            var icon = K.Icon(bad ? SymbolRegular.Warning24 : SymbolRegular.CheckmarkCircle24, 16, bad ? B.Caution : B.Success, filled: true);
            var line = K.Columns("auto, *", icon.Margin(0, 1, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), K.Wrapped(text, Ty.Body).Col(1));
            return new Border { Child = line, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8) }.Bg(bad ? B.CautionBack : B.SuccessBack);
        }

        void Say(string text, bool bad)
        {
            said = text;
            saidBad = bad;
        }

        FrameworkElement Section(string title, UIElement body) =>
            K.V(8, K.Text(title.ToUpper(CultureInfo.InvariantCulture), Ty.Caption, B.Text2).Strong(), body);

        // ---- Canvas out ----

        Button AppButton(string title, SymbolRegular icon, Action act, string tip)
        {
            var words = K.H(10, K.Icon(icon, 16, B.AccentText), K.Text(title, Ty.Body));
            var b = K.Subtle(words, act, new Thickness(14, 0, 14, 0), 8);
            b.Height = 40;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Bg(B.Subtle);
            b.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(b, title);
            return b;
        }

        FrameworkElement FeedCard(string? feed)
        {
            if (FeedLinks.From(feed, $"{engine.LmsName} (Simpl)") is not { } links)
                return K.Wrapped($"{engine.LmsName} has no calendar feed for your account here.", Ty.Body, B.Text2);
            Button? outlook = null;
            outlook = AppButton("Outlook", SymbolRegular.CalendarAdd24, () =>
            {
                var menu = new ContextMenu { PlacementTarget = outlook, Placement = PlacementMode.Bottom };
                menu.Items.Add(CalendarKit.Item("School or Work Account (Microsoft 365)", SymbolRegular.Building24, () => Simpl.Engine.Engine.OpenExternally(links.Office)));
                menu.Items.Add(CalendarKit.Item("Personal Account (Outlook.com)", SymbolRegular.Person24, () => Simpl.Engine.Engine.OpenExternally(links.Outlook)));
                menu.IsOpen = true;
            }, "Add it to Outlook: your school's account or a personal one");
            var google = AppButton("Google Calendar", SymbolRegular.CalendarAdd24, () => Simpl.Engine.Engine.OpenExternally(links.Google), "Opens Google Calendar's page to add it");
            var app = AppButton("Calendar App", SymbolRegular.CalendarAdd24, () =>
            {
                // (no app on this PC takes webcal links: the link copied instead, and said so)
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(links.Webcal.ToString()) { UseShellExecute = true }); }
                catch
                {
                    CalendarKit.Copy(links.Https);
                    Say("No calendar app on this PC opens calendar links, so the link is copied: paste it where your calendar app adds one by address.", true);
                    Paint();
                }
            }, "Subscribes in the calendar app Windows opens calendar links in");
            var copy = AppButton("Copy Link", SymbolRegular.Copy24, () =>
            {
                CalendarKit.Copy(links.Https);
                Say("Calendar link copied. Paste it where your calendar app adds one by address.", false);
                Paint();
            }, "Copy the feed's address");
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -8, -8) };
            foreach (var b in new[] { outlook, google, app, copy })
            {
                b.Margin = new Thickness(0, 0, 8, 8);
                grid.Children.Add(b);
            }
            var note = K.Wrapped("Every course's work and events, kept up to date: new work and moved due dates follow on their own. Google and Outlook look for changes every few hours.", Ty.Caption, B.Text2);
            return K.Card(K.V(10, grid, note), new Thickness(14), 8);
        }

        // ---- calendars in ----

        string SubLine(SyncLink l)
        {
            if (l.Error.Length > 0) return l.Error;
            if (CalendarKit.Date(l.Read) is not { } at) return "Not read yet";
            var ago = DateTime.Now - at;
            var when = ago.TotalSeconds < 60 ? "just now"
                : ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes} min ago"
                : ago.TotalHours < 24 ? $"{(int)ago.TotalHours} hr ago"
                : CalendarKit.DateWords(at.Date);
            return $"Read {when} · {(l.Count == 1 ? "1 event" : $"{l.Count} events")}";
        }

        FrameworkElement LinkRow(SyncLink l)
        {
            var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = K.Brush(K.Hex(l.Color)), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var words = K.V(2, K.Text(l.Name, Ty.Body), K.Wrapped(SubLine(l), Ty.Caption, l.Error.Length > 0 ? B.Critical : B.Text2, 2));
            words.VerticalAlignment = VerticalAlignment.Center;
            if (unlinking == l.Id)
            {
                var gone = K.Button("Unlink", () => _ = Unlink(l));
                gone.Foreground = K.Brush(K.Tone("red"));
                var keep = K.Button("Cancel", () => { unlinking = null; Paint(); });
                var ask = K.V(8,
                    K.Wrapped($"Unlink {l.Name}? Its events leave Simpl's Calendar. The calendar itself is not touched.", Ty.Body),
                    K.H(8, gone, keep));
                return new Border { Child = K.Columns("auto, *", swatch.Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 4, 12, 0).Col(0), ask.Col(1)), Padding = new Thickness(14, 10, 14, 10) };
            }
            var show = new ToggleSwitch { IsChecked = l.On, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(show, $"Show {l.Name} on the calendar");
            show.ToolTip = $"Show {l.Name} on the calendar";
            show.Click += async (_, _) =>
            {
                var on = show.IsChecked == true;
                l.On = on;
                await engine.Act("calShow", new { id = l.Id, on });
                changed();
            };
            var x = K.IconButton(SymbolRegular.Dismiss24, () => { unlinking = l.Id; Paint(); }, $"Unlink {l.Name}", 14, 28);
            x.Margin = new Thickness(6, 0, 0, 0);
            var row = K.Columns("auto, *, auto, auto", swatch.Col(0), words.Col(1), show.Col(2), x.Col(3));
            return new Border { Child = row, Padding = new Thickness(14, 10, 10, 10) };
        }

        FrameworkElement LinksCard(List<SyncLink> list)
        {
            var v = K.V(0);
            foreach (var l in list)
            {
                v.Children.Add(LinkRow(l));
                v.Children.Add(K.Divider(38));
            }
            var box = new TextBox { PlaceholderText = "Paste a calendar link (.ics or webcal://)", Text = draft, MinHeight = 32 };
            box.TextChanged += (_, _) => draft = box.Text;
            box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; _ = Link(); } };
            System.Windows.Automation.AutomationProperties.SetName(box, "A calendar's link");
            var go = K.Button(linking ? "Linking…" : "Link", () => _ = Link(), accent: true);
            go.IsEnabled = !linking;
            go.Margin = new Thickness(8, 0, 0, 0);
            v.Children.Add(new Border { Child = K.Columns("*, auto", box.Col(0), go.Col(1)), Padding = new Thickness(14) });
            if (list.Count == 0) v.Children.Insert(0, K.Wrapped("Link Google, iCloud or Outlook to see your own events beside your classes, and plan around them.", Ty.Body, B.Text2).Margin(14, 12, 14, 0));
            return K.Card(v, new Thickness(0), 8);
        }

        FrameworkElement Where()
        {
            var chevron = K.Icon(showWhere ? SymbolRegular.ChevronDown20 : SymbolRegular.ChevronRight20, 12, B.Text2);
            var toggle = K.Subtle(K.H(8, chevron, K.Text("Where is my calendar's link?", Ty.Body)), () => { showWhere = !showWhere; Paint(); }, new Thickness(4, 4, 8, 4), 6);
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            System.Windows.Automation.AutomationProperties.SetName(toggle, $"Where is my calendar's link? {(showWhere ? "open" : "closed")}");
            if (!showWhere) return toggle;
            FrameworkElement Line(string name, string words)
            {
                var t = new TextBlock { TextWrapping = TextWrapping.Wrap };
                t.Inlines.Add(new System.Windows.Documents.Run(name + ": ") { FontWeight = FontWeights.SemiBold });
                t.Inlines.Add(new System.Windows.Documents.Run(words));
                K.Style(t, Ty.Body);
                return t;
            }
            return K.V(6, toggle,
                K.V(6,
                    Line("Google", "Settings → your calendar → Integrate calendar → Secret address in iCal format."),
                    Line("iCloud", "Calendar → the share button beside the calendar → Public Calendar → Copy Link."),
                    Line("Outlook", "Settings → Calendar → Shared calendars → Publish a calendar → the ICS link."),
                    K.Wrapped("The link stays on this PC, and Simpl reads it every half hour. Anyone with a private link can read that calendar, so keep it to yourself.", Ty.Caption, B.Text2)).Margin(24, 0, 0, 0));
        }

        FrameworkElement Plans(SyncData d)
        {
            var tile = K.IconTile(SymbolRegular.Clock24, Support.Appearance.Accent, 30);
            var words = K.V(2,
                K.Text("Send plans to your calendar app", Ty.Body),
                K.Wrapped($"Each plan goes on your {engine.LmsName} calendar too, so the feed above carries it out with everything else.", Ty.Caption, B.Text2));
            words.VerticalAlignment = VerticalAlignment.Center;
            var send = new ToggleSwitch { IsChecked = d.Send, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(send, "Send plans to your calendar app");
            send.Click += async (_, _) =>
            {
                var on = send.IsChecked == true;
                d.Send = on;
                await engine.Act("planSend", new { on });
                Say(on ? "Plans go to your calendar now." : "Plans stay in Simpl now.", false);
                Paint();
                changed();
            };
            var row = K.Columns("auto, *, auto", tile.Margin(0, 0, 12, 0).Col(0), words.Col(1), send.Col(2));
            return K.Card(row, new Thickness(14), 8);
        }

        void Paint()
        {
            if (data == null)
            {
                host.Child = K.Spinner(24).Margin(0, 80, 0, 80);
                return;
            }
            var list = K.V(18,
                Section("Your Canvas work, in your calendar app", FeedCard(data.Feed)),
                Section("Your calendars, in Simpl", LinksCard(data.Links)));
            if (said != null) list.Children.Add(Said(said, saidBad));
            list.Children.Add(Where());
            if (data.CanSend) list.Children.Add(Section("Plans", Plans(data)));
            list.Children.Add(K.Wrapped("Pull any piece of work to the right on To Do or the Dashboard to plan a time for it.", Ty.Caption, B.Text2));
            host.Child = list;
        }

        async Task Load()
        {
            try { data = await engine.Call<SyncData>("calSync"); }
            catch { data ??= new SyncData(); }
            Paint();
        }

        async Task Link()
        {
            var url = draft.Trim();
            if (url.Length == 0 || linking) return;
            linking = true;
            Paint();
            CalLinkReply? r = null;
            try { r = await engine.Call<CalLinkReply>("calLink", new { url }); } catch { }
            linking = false;
            if (r?.Ok == true)
            {
                draft = "";
                Say($"{r.Name ?? "Your calendar"} is linked: {(r.Count == 1 ? "1 event" : $"{r.Count ?? 0} events")} read.", false);
                changed();
            }
            else Say(r?.Error ?? "That calendar could not be linked.", true);
            await Load();
        }

        async Task Unlink(SyncLink l)
        {
            await engine.Act("calUnlink", new { id = l.Id });
            unlinking = null;
            changed();
            await Load();
        }

        var lede = K.Text("Your Canvas work in your calendar app, and your calendars here", Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom);
        var body = new ScrollViewer
        {
            Content = host, MaxHeight = 600, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0),
        };
        Paint();
        var sheet = new Sheet { Title = "Sync", Body = K.V(12, lede, body), Width = 580, Floats = true };
        sheet.Add("Done", SheetButtonKind.Primary);
        Dialogs.Show(sheet, over);
        _ = Load();
    }
}

/// <summary>Canvas's calendar feed as each calendar app takes one: Windows' own by webcal, Google's and Outlook's
/// add-by-address pages (the Mac's FeedLinks).</summary>
internal sealed record FeedLinks(Uri Https, Uri Webcal, Uri Google, Uri Office, Uri Outlook)
{
    public static FeedLinks? From(string? feed, string name)
    {
        if (string.IsNullOrWhiteSpace(feed)) return null;
        var plain = System.Text.RegularExpressions.Regex.Replace(feed.Trim(), "^webcal:", "https:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var webcal = System.Text.RegularExpressions.Regex.Replace(plain, "^https?:", "webcal:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        string Enc(string s) => Uri.EscapeDataString(s);
        if (!Uri.TryCreate(plain, UriKind.Absolute, out var h) || !Uri.TryCreate(webcal, UriKind.Absolute, out var w)) return null;
        return new FeedLinks(h, w,
            new Uri($"https://calendar.google.com/calendar/r?cid={Enc(webcal)}"),
            new Uri($"https://outlook.office.com/calendar/0/addfromweb?url={Enc(plain)}&name={Enc(name)}"),
            new Uri($"https://outlook.live.com/calendar/0/addfromweb?url={Enc(plain)}&name={Enc(name)}"));
    }
}

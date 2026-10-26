using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// (0.3.60) Sync (the Mac's SyncPopup, the web's sheet), read through the page (native-app.js <c>calSync</c>): (0.3.63)
/// Simpl's own calendar — the work, the student's own tasks and the plans, as one calendar named Simpl, sealed and kept on
/// simplcourses.com (the app sends it: Engine/Bridge.cs feedSend) — handed to a calendar app: Outlook's and Google's
/// add-by-address pages open in the browser, the calendar app Windows opens webcal links in subscribes straight away, or
/// the private link is copied; and the student's own calendars linked by their private address (the app reads the file,
/// the page parses it: background.js and lib/ics.js), each shown or hidden on the Calendar or unlinked. Canvas's own feed
/// and Send plans are gone: the plans are in Simpl's calendar of themselves. What changes here is read again behind it
/// (<c>changed</c>). <c>make</c>: the screenshot suite's (--open sync-simpl), Simpl's calendar made as it opens.
/// </summary>
internal static class SyncSheet
{
    public static void Show(Engine.Engine engine, Action changed, Window? over, bool make = false)
    {
        SyncData? data = null;
        var turning = false;
        var stopping = false;
        string? said = null;
        var saidBad = false;
        var linking = false;
        // (0.3.62) what came of a link, said under its field (reading it, linked, or why not)
        string? linkSaid = null, linkFaded = null;
        var linkBad = false;
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

        FrameworkElement SimplCard(SimplFeed f)
        {
            if (!f.On || FeedLinks.From(f.Link, "Simpl") is not { } links)
            {
                var tile = K.IconTile(SymbolRegular.CalendarAdd24, Support.Appearance.Accent, 36);
                var words = K.V(3,
                    K.Text("Your work, your tasks and your plans", Ty.BodyStrong),
                    K.Wrapped("As one calendar, Simpl, in Outlook, Google Calendar or your calendar app, kept up to date. It is sealed: only your private link opens it.", Ty.Body, B.Text2));
                var intro = K.Columns("auto, *", tile.Margin(0, 0, 12, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), words.Col(1));
                var make_ = K.Button(turning ? "Making It…" : "Make My Simpl Calendar", () => _ = Make(), accent: true);
                make_.IsEnabled = !turning;
                make_.HorizontalAlignment = HorizontalAlignment.Stretch;
                make_.MinHeight = 36;
                var off = K.V(12, intro, make_);
                if (f.Problem.Length > 0) off.Children.Add(Status(f.Problem, SymbolRegular.Warning24, B.Critical));
                return K.Card(off, new Thickness(14), 8);
            }
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
                Say("Your Simpl calendar's link is copied. Paste it where your calendar app adds one by address.", false);
                Paint();
            }, "Copy your Simpl calendar's private link");
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -8, -8) };
            foreach (var b in new[] { outlook, google, app, copy })
            {
                b.Margin = new Thickness(0, 0, 8, 8);
                grid.Children.Add(b);
            }
            FrameworkElement status = f.Problem.Length > 0 ? Status(f.Problem, SymbolRegular.Warning24, B.Critical)
                : CalendarKit.Date(f.Sent) is { } at ? Status($"Up to date · sent {Ago(at)} · {(f.Count == 1 ? "1 event" : $"{f.Count} events")}", SymbolRegular.CheckmarkCircle24, B.Success)
                : K.H(8, K.Spinner(14), K.Text("Sending it…", Ty.Caption, B.Text2));
            var note = K.Wrapped("Your work, your own tasks and your plans, sent again while Simpl is open. It is sealed: only this private link opens it, so keep it to yourself. Google and Outlook look for changes every few hours.", Ty.Caption, B.Text2);
            FrameworkElement stop;
            if (stopping)
            {
                var yes = K.Button("Stop Sharing", () => _ = Stop());
                yes.Foreground = K.Brush(K.Tone("red"));
                yes.IsEnabled = !turning;
                var no = K.Button("Cancel", () => { stopping = false; Paint(); });
                stop = K.V(8,
                    K.Wrapped("Stop sharing your Simpl calendar? Its private link stops working, and calendar apps that have it show nothing more from Simpl. You can make a new one at any time.", Ty.Body),
                    K.H(8, yes, no));
            }
            else
            {
                var ask = K.Subtle(K.Text("Stop Sharing This Calendar…", Ty.Body, B.Critical), () => { stopping = true; Paint(); }, new Thickness(4, 4, 8, 4), 6);
                ask.HorizontalAlignment = HorizontalAlignment.Left;
                System.Windows.Automation.AutomationProperties.SetName(ask, "Stop sharing this calendar");
                stop = ask;
            }
            return K.Card(K.V(10, grid, status, note, K.Divider(0), stop), new Thickness(14), 8);
        }

        FrameworkElement Status(string text, SymbolRegular icon, string color)
        {
            var words = K.Wrapped(text, Ty.Caption, color);
            words.VerticalAlignment = VerticalAlignment.Center;
            return K.Columns("auto, *", K.Icon(icon, 15, color, filled: true).Margin(2, 0, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), words.Col(1));
        }

        static string Ago(DateTime at)
        {
            var ago = DateTime.Now - at;
            return ago.TotalSeconds < 60 ? "just now"
                : ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes} min ago"
                : ago.TotalHours < 24 ? $"{(int)ago.TotalHours} hr ago"
                : CalendarKit.DateWords(at.Date);
        }

        // (0.3.63) Simpl's calendar made — a new private link — and sent at once; or why not
        async Task Make()
        {
            if (turning) return;
            turning = true;
            Paint();
            SimplFeed? f = null;
            try { f = await engine.Call<SimplFeed>("feedOn"); }
            catch (Exception e) { Log.Write("sync: simpl calendar " + e.Message); }
            Log.Write(f is { On: true, Problem.Length: 0 } ? $"sync: simpl calendar made, {f.Count} events sent" : $"sync: simpl calendar not made: {f?.Problem}");
            turning = false;
            if (data != null) data.Simpl = f ?? new SimplFeed { Problem = "Simpl could not reach simplcourses.com. Try again in a moment." };
            if (f is { On: true, Problem.Length: 0 }) Say("Your Simpl calendar is made. Add it to your calendar app above.", false);
            Paint();
        }

        // (0.3.63) Simpl's calendar forgotten on simplcourses.com: its link opens nothing any more
        async Task Stop()
        {
            turning = true;
            Paint();
            SimplFeed? f = null;
            try { f = await engine.Call<SimplFeed>("feedOff"); }
            catch (Exception e) { Log.Write("sync: simpl calendar off " + e.Message); }
            turning = false;
            stopping = false;
            if (data != null) data.Simpl = f ?? new SimplFeed();
            Say("Your Simpl calendar is no longer shared.", false);
            Paint();
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
            var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = K.Brush(K.Hex(l.Color)), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
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
            var field = K.V(8, K.Columns("*, auto", box.Col(0), go.Col(1)));
            if (linking || linkSaid != null) field.Children.Add(LinkStatus());
            v.Children.Add(new Border { Child = field, Padding = new Thickness(14) });
            if (list.Count == 0) v.Children.Insert(0, K.Wrapped("Link Google, iCloud or Outlook to see your own events beside your classes, and plan around them.", Ty.Body, B.Text2).Margin(14, 12, 14, 0));
            return K.Card(v, new Thickness(0), 8);
        }

        // reading it (a spinner), linked (a tick), or why not (in red), in the card under the field
        FrameworkElement LinkStatus()
        {
            FrameworkElement mark = linking
                ? K.Spinner(14)
                : K.Icon(linkBad ? SymbolRegular.Warning24 : SymbolRegular.CheckmarkCircle24, 15, linkBad ? B.Critical : B.Success, filled: true);
            var words = K.Wrapped(linking ? "Reading the calendar…" : linkSaid ?? "", Ty.Caption, linking ? B.Text2 : linkBad ? B.Critical : B.Text);
            words.VerticalAlignment = VerticalAlignment.Center;
            var line = K.Columns("auto, *", mark.Margin(2, 0, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), words.Col(1));
            if (!linking && linkFaded != linkSaid) Motion.FadeIn(line); // (once, as it is said: not again as the sheet is drawn again)
            linkFaded = linking ? null : linkSaid;
            return line;
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

        void Paint()
        {
            if (data == null)
            {
                host.Child = K.Spinner(24).Margin(0, 80, 0, 80);
                return;
            }
            var list = K.V(18,
                Section("Simpl, in your calendar app", SimplCard(data.Simpl)),
                Section("Your calendars, in Simpl", LinksCard(data.Links)));
            if (said != null) list.Children.Add(Said(said, saidBad));
            list.Children.Add(Where());
            list.Children.Add(K.Wrapped("Drag work from To Do beside the calendar onto a day or a time to plan it, or pull it to the right on the Dashboard.", Ty.Caption, B.Text2));
            host.Child = list;
        }

        async Task Load()
        {
            try { data = await engine.Call<SyncData>("calSync"); }
            catch { data ??= new SyncData(); }
            Paint();
            if (make && !data.Simpl.On) { make = false; await Make(); }
        }

        async Task Link()
        {
            var url = draft.Trim();
            if (url.Length == 0 || linking) return;
            linking = true;
            linkSaid = null;
            Paint();
            CalLinkReply? r = null;
            string? why = null;
            try { r = await engine.Call<CalLinkReply>("calLink", new { url }); }
            catch (Exception e) { why = e.Message; }
            linking = false;
            if (r?.Ok == true)
            {
                draft = "";
                linkSaid = $"{r.Name ?? "Your calendar"} is linked: {(r.Count == 1 ? "1 event" : $"{r.Count ?? 0} events")} read.";
                linkBad = false;
                changed();
            }
            else
            {
                linkSaid = r?.Error ?? (string.IsNullOrWhiteSpace(why) ? "That calendar could not be linked." : why);
                linkBad = true;
            }
            await Load();
        }

        async Task Unlink(SyncLink l)
        {
            await engine.Act("calUnlink", new { id = l.Id });
            unlinking = null;
            changed();
            await Load();
        }

        var lede = K.Text("Simpl in your calendar app, and your calendars here", Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom);
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

/// <summary>A calendar's address (0.3.63: Simpl's own calendar's private link) as each calendar app takes one: Windows'
/// own by webcal, Google's and Outlook's add-by-address pages.</summary>
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

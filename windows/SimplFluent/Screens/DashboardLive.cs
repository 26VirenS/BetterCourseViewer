using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// The Dashboard brought up to date by a live check (Shell/LiveCheck.cs), part by part and never built again whole: a
/// counter's number rolls to its new value (its line and its face with it), a card of a course is drawn again in its
/// place, a row of a list comes in, goes or is drawn again where it was — the rows around it untouched — and the rail's
/// grades and week load are drawn again on their own. Each part notes, as it is built, the pieces a check may change
/// (<see cref="PartRefs"/>); a part whose shape changed (a counter more, the list emptied) is built again alone, in its
/// place. Under Windows' "Animation effects" off (and in pictures) everything simply changes.
/// </summary>
public sealed partial class DashboardScreen
{
    /// <summary>The part being built, noting its pieces (null: not building one).</summary>
    private PartRefs? noting;
    /// <summary>Each part's pieces, as it was last built.</summary>
    private readonly Dictionary<string, PartRefs> refs = new();

    private sealed class PartRefs
    {
        public readonly Dictionary<string, TileRefs> Tiles = new();
        public TodayRefs? Today;
        public double RailWidth;
        public FrameworkElement? Sky, Load;
        public string? SkySaid, LoadSaid;
        public UpcomingRefs? Upcoming;
        public CardsRefs? Cards;
        public ActivityRefs? Activity;
    }

    /// <summary>A counter's card: its number's holder, its words, and what it shows.</summary>
    private sealed class TileRefs
    {
        public Button Tile;
        public readonly Grid Number;
        public readonly Stack Content;
        public (int? Value, string? Note, bool Kept, bool Alert, string Label) Shown;

        public TileRefs(Button tile, Grid number, Stack content, (int? Value, string? Note, bool Kept, bool Alert, string Label) shown)
        {
            Tile = tile;
            Number = number;
            Content = content;
            Shown = shown;
        }
    }

    /// <summary>A list of rows with a divider between each and the next (Today's work, a day of Coming up, the activity).</summary>
    private sealed class RowList
    {
        public readonly Panel Panel;
        public readonly double Indent;
        /// <summary>Its rows in order: their ids (made unique), how they look, and their elements.</summary>
        public List<(string Id, string Look, FrameworkElement Row)> Rows = new();
        /// <summary>The divider above each row but the first, by its id.</summary>
        public Dictionary<string, FrameworkElement> Dividers = new();

        public RowList(Panel panel, double indent)
        {
            Panel = panel;
            Indent = indent;
        }
    }

    private sealed record TodayRefs(Stack Section, FrameworkElement Heading, string HeadSaid, RowList? Rows, bool Narrow)
    {
        public FrameworkElement Heading { get; set; } = Heading;
        public string HeadSaid { get; set; } = HeadSaid;
    }

    private sealed record DayRefs(string Key, FrameworkElement Block, TextBlock Count, RowList Rows);

    private sealed record UpcomingRefs(Stack View, FrameworkElement Heading, string HeadSaid, Stack Days, bool Wide, List<DayRefs> Blocks)
    {
        public FrameworkElement Heading { get; set; } = Heading;
        public string HeadSaid { get; set; } = HeadSaid;
        public List<DayRefs> Blocks { get; set; } = Blocks;
    }

    private sealed record CardsRefs(double CardWidth, List<(string Id, string Look, FrameworkElement Card)> Cards);

    private sealed record ActivityRefs(Stack View, FrameworkElement Heading, string? HeadSaid, RowList Rows)
    {
        public FrameworkElement Heading { get; set; } = Heading;
        public string? HeadSaid { get; set; } = HeadSaid;
    }

    /// <summary>Rows put in a list, a divider between each and the next, and noted for a check.</summary>
    private static RowList RowsInto(Panel panel, double indent, IEnumerable<(string Id, string Look, FrameworkElement Row)> rows)
    {
        var list = new RowList(panel, indent);
        var made = rows.ToList();
        var ids = LiveRules.Unique(made.Select(r => (r.Id, r.Look)).ToList()).Select(u => u.Id).ToList();
        for (var k = 0; k < made.Count; k++)
        {
            if (k > 0)
            {
                var divider = K.Divider(indent);
                panel.Children.Add(divider);
                list.Dividers[ids[k]] = divider;
            }
            panel.Children.Add(made[k].Row);
            list.Rows.Add((ids[k], made[k].Look, made[k].Row));
        }
        return list;
    }

    // ---- how each thing looks, to tell what changed ----

    private string TodayLook(WorkRow r, bool narrow) => $"{Json.Write(r)}|{Done(r.Id, r.Done)}|{narrow}";
    private string DashLook(DashRow r, string day, bool wide) => $"{Json.Write(r)}|{Done(r.Id, r.Done)}|{day}|{wide}";
    private string ActivityLook(DashActivityRow r) => $"{Json.Write(r)}|{r.Unread == true && !seen.Contains(r.Id)}";
    private string CardLook(DashCourse c, double cardW) =>
        $"{Json.Write(c)}|{(progress.Data == null ? "?" : progress.Data.TryGetValue(c.Id, out var p) ? Json.Write(p) : "-")}|{Math.Round(cardW)}";
    private static string TodaySaid(Today d) => $"{d.List.Heading}|{d.List.Note}|{d.List.Empty}";
    private static string UpcomingSaid(DashListData l) => $"{l.Total}|{l.Done}|{l.HideDone}";
    private string SkySaid() => Json.Write(skyFresh.Data ?? skyKept.Data);
    private static string LoadSaid(Today d) => Json.Write(new { d.Load, d.LoadCaption, d.Idle, d.HasCourses });
    private static string DayKey(DashDay day) => $"{day.Title}|{day.Date}";

    // ---- what a check reads, and when it waits ----

    /// <summary>A counter's panel open: the check waits until it folds back (its rows are what the student is reading).</summary>
    public override string? LiveHold => openKey != null ? "a counter's panel" : base.LiveHold;

    /// <summary>What a check reads besides Today, the counts, the courses' cards and the grades: the view on show's — the
    /// List's planner, or the Activity, or the cards' handed-in counts — never another view's; nor the copies kept from
    /// the last visit, which are not the school's.</summary>
    private void ChecksForView()
    {
        countsQuick.Checks = false;
        skyKept.Checks = false;
        progress.Checks = View == 0;
        list.Checks = View == 1;
        activity.Checks = View == 2;
    }

    /// <summary>
    /// A live check's new answers, shown part by part (the loaders hold them already, and what they held before): the
    /// greeting and the date; each counter whose number, line or face changed; Today's rows; the rail's grades and week
    /// load; the view's cards, days and rows. What changed, for the log ("counter:today,list(~1)").
    /// </summary>
    public override string LiveApplied(IReadOnlyList<ILiveRead> changed)
    {
        var d = today.Data;
        if (d == null || root == null || shownParts.Count == 0)
        {
            // (nothing built yet: built as any first answer is)
            Render();
            return "page";
        }
        var said = new List<string>();
        bool Has(ILiveRead r) => changed.Contains(r);
        // (what the student is looking at kept where it is while rows come and go above it)
        var anchor = ScrollAnchor.At(root);
        if (Title != lastTitle || Subtitle != lastSubtitle)
        {
            lastTitle = Title;
            lastSubtitle = Subtitle;
            HeaderChange();
            said.Add("heading");
        }
        // (the parts as the page was built: at its width then — a resize since that crossed none of its steps built nothing)
        var (specs, _, _) = Specs(d, builtWidth);
        if (!specs.Keys.OrderBy(k => k).SequenceEqual(shownParts.OrderBy(k => k)))
        {
            // (the page laid out otherwise than it was — never from a check: its width and view are the student's)
            Render();
            return "page";
        }
        if (Has(today) || Has(countsFresh)) CountersAgain(d, specs["counters"], said);
        var railBuilt = false;
        if (Has(today))
        {
            TodayRows(d, specs, said);
            railBuilt = WeekLoadAgain(d, specs["rail"], said);
        }
        if (Has(skyFresh) && !railBuilt) SkylineAgain(specs["rail"], said);
        if ((View == 0 && (Has(courses) || Has(progress))) || (View == 1 && Has(list)) || (View == 2 && Has(activity))) MainAgain(specs["main"], said);
        // (every part on the page shows what its answers say now: kept as it is, its new answers its inputs, so the next
        //  build — a resize, a tick — takes it as it is)
        foreach (var k in shownParts)
            if (parts.TryGetValue(k, out var p) && specs.TryGetValue(k, out var spec)) parts[k] = (spec.Inputs, p.Part);
        if (said.Count > 0) anchor?.Hold();
        Shot.Moved();
        return said.Count == 0 ? "nothing on show" : string.Join(",", said);
    }

    /// <summary>
    /// What the student is looking at held still while a check changes the page above it — a row in or out over where the
    /// page is scrolled to moved everything under it by its height: an element at the top of the view, found before the
    /// change, and the page scrolled by however far it moves while the rows fold and unfold (well under a second; a turn
    /// of the wheel or a key lets go at once). At the page's top nothing is held: a row coming in there is seen to come.
    /// </summary>
    private sealed class ScrollAnchor
    {
        private readonly ScrollViewer scroller;
        private readonly UIElement mark;
        private readonly double top;

        private ScrollAnchor(ScrollViewer scroller, UIElement mark, double top)
        {
            this.scroller = scroller;
            this.mark = mark;
            this.top = top;
        }

        public static ScrollAnchor? At(FrameworkElement page)
        {
            ScrollViewer? s = null;
            for (DependencyObject? p = page; p != null; p = VisualTreeHelper.GetParent(p)) if (p is ScrollViewer sv) { s = sv; break; }
            if (s is not { IsVisible: true } || s.VerticalOffset < 1 || s.ViewportHeight < 1) return null;
            // (the first thing of the page's at the view's top: a few places tried, across and down)
            foreach (var y in new[] { 8.0, 48, 96 })
                foreach (var x in new[] { 0.5, 0.25, 0.75 })
                {
                    if (s.InputHitTest(new Point(s.ViewportWidth * x, y)) is not UIElement hit || !page.IsAncestorOf(hit)) continue;
                    return new ScrollAnchor(s, hit, hit.TranslatePoint(new Point(0, 0), s).Y);
                }
            return null;
        }

        public void Hold()
        {
            var until = Environment.TickCount64 + 800;
            EventHandler? moved = null;
            System.Windows.Input.MouseWheelEventHandler? wheel = null;
            System.Windows.Input.KeyEventHandler? key = null;
            void Done()
            {
                scroller.LayoutUpdated -= moved;
                scroller.PreviewMouseWheel -= wheel;
                scroller.PreviewKeyDown -= key;
            }
            moved = (_, _) =>
            {
                // (the time up, or what was held gone with its row: let go)
                if (Environment.TickCount64 > until || PresentationSource.FromVisual(mark) == null || !scroller.IsAncestorOf(mark)) { Done(); return; }
                var d = mark.TranslatePoint(new Point(0, 0), scroller).Y - top;
                if (Math.Abs(d) >= 0.5) scroller.ScrollToVerticalOffset(scroller.VerticalOffset + d);
            };
            wheel = (_, _) => Done();
            key = (_, _) => Done();
            scroller.LayoutUpdated += moved;
            scroller.PreviewMouseWheel += wheel;
            scroller.PreviewKeyDown += key;
        }
    }

    /// <summary>The keyboard given to what took the place of the element that had it (a row drawn again, a card replaced),
    /// once it is drawn: replaced, it was lost to the window.</summary>
    private static void Refocus(FrameworkElement fresh) => fresh.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
    {
        if (fresh.Focusable && fresh.IsVisible) { fresh.Focus(); return; }
        var stack = new Stack<DependencyObject>();
        stack.Push(fresh);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (!ReferenceEquals(d, fresh) && d is UIElement { Focusable: true, IsVisible: true, IsEnabled: true } u) { u.Focus(); return; }
            for (var i = VisualTreeHelper.GetChildrenCount(d) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
    });

    // ---- the counters ----

    private void CountersAgain(Today d, PartSpec spec, List<string> said)
    {
        if (!refs.TryGetValue("counters", out var r)) { Again("counters", spec, said); return; }
        var layout = CounterLayout(Math.Max(320, builtWidth));
        var items = d.Counters.OrderBy(c => Array.IndexOf(Order, c.Key) is var i && i < 0 ? 99 : i).ToList();
        // (the same counters in the same places, wearing the same names: only their numbers, lines and faces change)
        var same = items.Count == r.Tiles.Count && items.All(c => r.Tiles.TryGetValue(c.Key, out var t) && t.Shown.Label == NameOf(c, layout.Full))
            && items.Select(c => c.Key).SequenceEqual(r.Tiles.Keys);
        if (!same) { Again("counters", spec, said); return; }
        foreach (var c in items)
        {
            var t = r.Tiles[c.Key];
            var n = Count(c, layout.Full);
            var alert = c.Tone == "red" && (n.Value ?? 0) > 0;
            var now = (n.Value, n.Note, n.Kept, alert, NameOf(c, layout.Full));
            if (now == t.Shown) continue;
            said.Add($"counter:{c.Key}");
            if (alert != t.Shown.Alert)
            {
                // (late work, or none: the card's face changes — that one card drawn again, where it was)
                var was = noting;
                noting = r;
                try
                {
                    var fresh = Tile(c, layout.Cols, layout.Full);
                    if (c.Key == "next") Shell.Tour.Mark(fresh, Shell.TourSpot.CounterNext);
                    Shell.AppearanceMode.Place(fresh, c.Key);
                    Swap(t.Tile, fresh);
                    tiles[c.Key] = fresh;
                }
                finally { noting = was; }
                continue;
            }
            if (n.Value != t.Shown.Value || n.Kept != t.Shown.Kept)
                Roll(t.Number, Number(n, alert), n.Value is { } v && t.Shown.Value is { } w ? v.CompareTo(w) : 0);
            if (n.Note != t.Shown.Note && t.Content.Children.Count > 2)
            {
                t.Content.Children.RemoveAt(2);
                t.Content.Children.Insert(2, new NoteLines(n.Note));
            }
            var spoken = Spoken(c.Key, c.Label, n.Value);
            t.Tile.ToolTip = string.IsNullOrEmpty(n.Note) ? spoken : $"{spoken}\n{n.Note}";
            System.Windows.Automation.AutomationProperties.SetName(t.Tile, spoken);
            System.Windows.Automation.AutomationProperties.SetHelpText(t.Tile, n.Note ?? "");
            t.Shown = now;
        }
    }

    /// <summary>
    /// A counter's new number, rolled in: the old one moves a little up and fades as the new one rises into its place
    /// (counting up; down, the other way) — a short ease-out, the new one a touch slower so it settles last. With
    /// Windows' Animation effects off, and in pictures, it simply changes.
    /// </summary>
    private static void Roll(Grid holder, TextBlock fresh, int direction)
    {
        // (one still rolling out goes at once)
        while (holder.Children.Count > 1) holder.Children.RemoveAt(0);
        var old = holder.Children.Count > 0 ? holder.Children[0] : null;
        if (old == null || direction == 0 || !Motion.On)
        {
            holder.Children.Clear();
            holder.Children.Add(fresh);
            return;
        }
        const double rise = 8;
        var target = fresh.Opacity;
        var outShift = new TranslateTransform();
        old.RenderTransform = outShift;
        var inShift = new TranslateTransform(0, direction > 0 ? rise : -rise);
        fresh.RenderTransform = inShift;
        fresh.Opacity = 0;
        holder.Children.Add(fresh);
        Motion.FromTo(outShift, TranslateTransform.YProperty, 0, direction > 0 ? -rise : rise, Motion.Normal, Motion.Out);
        Motion.FromTo(old, UIElement.OpacityProperty, old.Opacity, 0, Motion.Normal, Motion.Out, 0, () => holder.Children.Remove(old));
        Motion.FromTo(inShift, TranslateTransform.YProperty, inShift.Y, 0, Motion.Medium, Motion.Out);
        Motion.FromTo(fresh, UIElement.OpacityProperty, 0, target, Motion.Medium, Motion.Out);
    }

    // ---- the rail: Today's work, the grades, the week's load ----

    /// <summary>Today's rows brought up to date where they show (beside the view, or under it on a narrow page).</summary>
    private void TodayRows(Today d, Dictionary<string, PartSpec> specs, List<string> said)
    {
        var key = refs.TryGetValue("today", out var own) && own.Today != null && shownParts.Contains("today") ? "today" : "rail";
        if (!refs.TryGetValue(key, out var r) || r.Today is not { } t) return; // (not on show: the List's two columns)
        var rows = d.List.Rows;
        if (rows.Count == 0 || t.Rows == null)
        {
            if (rows.Count == 0 && t.Rows == null && t.HeadSaid == TodaySaid(d)) return;
            // (emptied, or filled from empty: the section drawn again — alone, or as the part it is)
            if (key == "today") { Again("today", specs["today"], said); return; }
            var was = noting;
            noting = r;
            try { Swap(t.Section, TodaySection(d, t.Narrow)); }
            finally { noting = was; }
            said.Add("today");
            return;
        }
        if (t.HeadSaid != TodaySaid(d))
        {
            var heading = Heading.Section(d.List.Heading.Length > 0 ? d.List.Heading : "Today", d.List.Note);
            Swap(t.Heading, heading);
            t.Heading = heading;
            t.HeadSaid = TodaySaid(d);
            said.Add("today-heading");
        }
        var diff = Rows(t.Rows, rows.Select(w => (w.Id, TodayLook(w, t.Narrow), (Func<FrameworkElement>)(() => TodayLine(w, t.Narrow)))).ToList());
        if (diff != null) said.Add($"today({diff.Said})");
    }

    /// <summary>The week's load drawn again, alone, when what it shows changed; the rail built again when it comes or goes.</summary>
    private bool WeekLoadAgain(Today d, PartSpec rail, List<string> said)
    {
        if (!refs.TryGetValue("rail", out var r) || r.LoadSaid == LoadSaid(d)) return false;
        var fresh = WeekLoad(d);
        if (r.Load == null || fresh == null) { Again("rail", rail, said); return true; }
        Swap(r.Load, fresh);
        r.Load = fresh;
        r.LoadSaid = LoadSaid(d);
        said.Add("week-load");
        return false;
    }

    /// <summary>The grades skyline drawn again, alone (its towers where they are now — they rise only the first time).</summary>
    private void SkylineAgain(PartSpec rail, List<string> said)
    {
        if (!refs.TryGetValue("rail", out var r) || r.SkySaid == SkySaid()) return;
        var fresh = Skyline(r.RailWidth);
        if (r.Sky == null || fresh == null) { Again("rail", rail, said); return; }
        Swap(r.Sky, fresh);
        r.Sky = fresh;
        r.SkySaid = SkySaid();
        said.Add("grades");
    }

    // ---- the view: the cards, Coming up, the activity ----

    private void MainAgain(PartSpec main, List<string> said)
    {
        if (!refs.TryGetValue("main", out var r)) { Again("main", main, said); return; }
        switch (View)
        {
            case 0: CardsAgain(r, main, said); break;
            case 1: UpcomingAgain(r, main, said); break;
            default: ActivityAgain(r, main, said); break;
        }
    }

    /// <summary>The cards whose course changed drawn again, each where it was; the same courses in the same order, or the
    /// view is built again.</summary>
    private void CardsAgain(PartRefs r, PartSpec main, List<string> said)
    {
        var c = courses.Data;
        if (c == null || r.Cards is not { } cr || c.Rows.Count != cr.Cards.Count || !c.Rows.Select(x => x.Id).SequenceEqual(cr.Cards.Select(x => x.Id)))
        {
            Again("main", main, said);
            return;
        }
        for (var k = 0; k < c.Rows.Count; k++)
        {
            var look = CardLook(c.Rows[k], cr.CardWidth);
            if (look == cr.Cards[k].Look) continue;
            var fresh = CourseCard(c.Rows[k], cr.CardWidth);
            Swap(cr.Cards[k].Card, fresh);
            cr.Cards[k] = (c.Rows[k].Id, look, fresh);
            said.Add($"card:{c.Rows[k].Id}");
        }
    }

    /// <summary>Coming up brought up to date: its heading, each day's rows (a day new or gone with them), each day's count.</summary>
    private void UpcomingAgain(PartRefs r, PartSpec main, List<string> said)
    {
        var l = list.Data;
        if (l == null || l.Days.Count == 0 || r.Upcoming is not { } u)
        {
            Again("main", main, said);
            return;
        }
        if (u.HeadSaid != UpcomingSaid(l))
        {
            var heading = UpcomingHeading(l);
            Swap(u.Heading, heading);
            u.Heading = heading;
            u.HeadSaid = UpcomingSaid(l);
        }
        var had = u.Blocks.ToDictionary(b => b.Key);
        var blocks = new List<DayRefs>();
        var newDays = new List<FrameworkElement>();
        int added = 0, removed = 0, changed = 0, days = 0;
        foreach (var day in l.Days)
        {
            if (had.Remove(DayKey(day), out var b))
            {
                var diff = Rows(b.Rows, day.Rows.Select(w => (w.Id, DashLook(w, day.Title, u.Wide), (Func<FrameworkElement>)(() => WorkLine(w, day.Title, u.Wide)))).ToList());
                if (diff != null) { added += diff.Added; removed += diff.Removed; changed += diff.Changed; }
                b.Count.Text = Items(day.Rows.Count);
                blocks.Add(b);
            }
            else
            {
                var fresh = DayBlock(day, u.Wide);
                blocks.Add(fresh);
                newDays.Add(fresh.Block);
                added += day.Rows.Count;
                days++;
            }
        }
        // (a day with nothing left in it goes as a row does)
        removed += had.Values.Sum(b => b.Rows.Rows.Count);
        days += had.Count;
        var want = blocks.Select(b => (UIElement)b.Block).ToList();
        var gone = had.Values.Select(b => b.Block).ToList();
        Fold(gone, () =>
        {
            Reconcile(u.Days, want);
            foreach (var n in newDays) Grow(n, u.Days);
        });
        u.Blocks = blocks;
        var parts = new List<string>();
        if (added > 0) parts.Add($"+{added}");
        if (removed > 0) parts.Add($"-{removed}");
        if (changed > 0) parts.Add($"~{changed}");
        if (days > 0) parts.Add($"{days} days");
        said.Add(parts.Count == 0 ? "list" : $"list({string.Join(" ", parts)})");
    }

    /// <summary>The activity brought up to date: its "n new", and its rows.</summary>
    private void ActivityAgain(PartRefs r, PartSpec main, List<string> said)
    {
        var a = activity.Data;
        if (a == null || a.Rows.Count == 0 || r.Activity is not { } act)
        {
            Again("main", main, said);
            return;
        }
        if (act.HeadSaid != ActivitySaid(a))
        {
            var heading = Heading.Section("Recent activity", ActivitySaid(a));
            Swap(act.Heading, heading);
            act.Heading = heading;
            act.HeadSaid = ActivitySaid(a);
        }
        var diff = Rows(act.Rows, a.Rows.Select(x => (x.Id, ActivityLook(x), (Func<FrameworkElement>)(() => ActivityRow(x)))).ToList());
        said.Add(diff == null ? "activity" : $"activity({diff.Said})");
    }

    // ---- the pieces ----

    /// <summary>A part built again on its own, in its place (its shape changed: a counter more, a list emptied).</summary>
    private void Again(string key, PartSpec spec, List<string> said)
    {
        var old = parts.TryGetValue(key, out var was) ? was.Part : null;
        var fresh = Make(key, spec);
        if (old != null && !ReferenceEquals(old, fresh)) Swap(old, fresh);
        said.Add(key);
    }

    /// <summary>One element for another, in the same place in the same parent (its grid cell too).</summary>
    private static void Swap(FrameworkElement old, FrameworkElement fresh)
    {
        if (old.IsKeyboardFocusWithin) Refocus(fresh);
        Grid.SetRow(fresh, Grid.GetRow(old));
        Grid.SetColumn(fresh, Grid.GetColumn(old));
        Grid.SetRowSpan(fresh, Grid.GetRowSpan(old));
        Grid.SetColumnSpan(fresh, Grid.GetColumnSpan(old));
        switch (old.Parent)
        {
            case Panel p:
                var at = p.Children.IndexOf(old);
                if (at < 0) return;
                p.Children.RemoveAt(at);
                p.Children.Insert(at, fresh);
                break;
            case Decorator dec: dec.Child = fresh; break;
            case ContentControl cc: cc.Content = fresh; break;
        }
    }

    /// <summary>
    /// A list's rows brought up to date by their ids: a row as it was is left as it is (its element untouched), one that
    /// changed is drawn again in its place, a new one comes in where it belongs, one gone goes — folding away as it fades,
    /// before the rows under it close up. The dividers stay with the rows under them. Null when nothing changed.
    /// </summary>
    private static RowDiff? Rows(RowList list, IReadOnlyList<(string Id, string Look, Func<FrameworkElement> Make)> next)
    {
        var before = list.Rows.Select(r => (r.Id, r.Look)).ToList();
        var diff = LiveRules.Rows(before, next.Select(n => (n.Id, n.Look)).ToList());
        if (diff.Same(before)) return null;
        var old = list.Rows.ToDictionary(r => r.Id, r => r.Row);
        var rows = new List<(string Id, string Look, FrameworkElement Row)>();
        var fresh = new List<FrameworkElement>();
        FrameworkElement? focusTo = null;
        var ids = LiveRules.Unique(next.Select(n => (n.Id, n.Look)).ToList()).ToList();
        for (var k = 0; k < next.Count; k++)
        {
            var (id, change) = diff.Rows[k];
            var row = change == RowChange.Kept ? old[id] : next[k].Make();
            if (change == RowChange.Added) fresh.Add(row);
            // (a row drawn again that had the keyboard: its new self has it)
            if (change == RowChange.Changed && old[id].IsKeyboardFocusWithin) focusTo = row;
            rows.Add((id, ids[k].Look, row));
        }
        var gone = diff.Gone.Select(id => old[id]).Concat(diff.Gone.Where(list.Dividers.ContainsKey).Select(id => list.Dividers[id])).ToList();
        // the rows and the dividers between them, as they will be: a row keeps the divider above it (the first has none)
        var dividers = new Dictionary<string, FrameworkElement>();
        var want = new List<UIElement>();
        for (var k = 0; k < rows.Count; k++)
        {
            if (k > 0)
            {
                var keep = diff.Rows[k].Change != RowChange.Added && list.Dividers.TryGetValue(rows[k].Id, out var dv) ? dv : K.Divider(list.Indent);
                dividers[rows[k].Id] = keep;
                want.Add(keep);
            }
            want.Add(rows[k].Row);
        }
        list.Rows = rows;
        list.Dividers = dividers;
        Fold(gone, () =>
        {
            Reconcile(list.Panel, want);
            foreach (var f in fresh) Grow(f, list.Panel);
            if (focusTo != null) Refocus(focusTo);
        });
        return diff;
    }

    /// <summary>A panel's children made the ones wanted, in order, moving as few as it can: those not wanted go, the new
    /// come in where they belong, and those that stay are left where they are.</summary>
    private static void Reconcile(Panel panel, IReadOnlyList<UIElement> want)
    {
        var keep = new HashSet<UIElement>(want);
        for (var i = panel.Children.Count - 1; i >= 0; i--)
            if (!keep.Contains(panel.Children[i])) panel.Children.RemoveAt(i);
        for (var i = 0; i < want.Count; i++)
        {
            var w = want[i];
            if (i < panel.Children.Count && ReferenceEquals(panel.Children[i], w)) continue;
            var at = panel.Children.IndexOf(w);
            if (at >= 0) panel.Children.RemoveAt(at);
            else if (w is FrameworkElement { Parent: Panel other } && !ReferenceEquals(other, panel)) other.Children.Remove(w);
            panel.Children.Insert(Math.Min(i, panel.Children.Count), w);
        }
    }

    /// <summary>
    /// What goes from a list folds away as it fades — its height to nothing, a fifth of a second, easing out — then
    /// <paramref name="then"/> (the list closes up with nothing left to jump). With motion off, at once.
    /// </summary>
    private static void Fold(IReadOnlyList<FrameworkElement> going, Action then)
    {
        if (going.Count == 0 || !Motion.On) { then(); return; }
        const double ms = 200;
        var left = going.Count;
        foreach (var g in going)
        {
            g.ClipToBounds = true;
            g.MinHeight = 0;
            Motion.FromTo(g, FrameworkElement.HeightProperty, g.ActualHeight, 0, ms, Motion.Out);
            Motion.FromTo(g, UIElement.OpacityProperty, g.Opacity, 0, ms, Motion.Out, 0, () => { if (--left == 0) then(); });
        }
    }

    /// <summary>A new row (or day) unfolds into its place as it fades in — its height from nothing to its own over a fifth of
    /// a second, easing out — so the rows under it make room rather than jump. With motion off, it is simply there.</summary>
    private static void Grow(FrameworkElement e, Panel list)
    {
        if (!Motion.On) return;
        const double ms = 200;
        var width = list.ActualWidth > 0 ? list.ActualWidth : double.PositiveInfinity;
        e.Measure(new Size(width, double.PositiveInfinity));
        var h = e.DesiredSize.Height;
        if (h <= 0) return;
        e.ClipToBounds = true;
        e.MinHeight = 0;
        e.Opacity = 0;
        Motion.FromTo(e, FrameworkElement.HeightProperty, 0, h, ms, Motion.Out, 0, () =>
        {
            e.BeginAnimation(FrameworkElement.HeightProperty, null);
            e.ClearValue(FrameworkElement.HeightProperty);
            e.ClipToBounds = false;
        });
        Motion.FromTo(e, UIElement.OpacityProperty, 0, 1, Motion.Medium, Motion.Out);
    }
}

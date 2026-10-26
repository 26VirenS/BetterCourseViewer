using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;

namespace Simpl.Shell;

/// <summary>
/// The page area: a deck of pages, one per screen whose page is kept — its own scroller, so each keeps where it was
/// scrolled — the one showing on top, the others folded away (not laid out, not drawn) until they are shown again.
/// Pages come and go in one motion: deeper (a piece of work opened) slides in a little from the right as it fades in,
/// Back the reverse, and another place in the pane crossfades with a small lift — 220 ms, decelerating hard
/// (<see cref="Motion.Out"/>), on the pages' transforms and opacity only, from wherever a page is when another move
/// interrupts it. A page comes in with what it shows (built from the answers kept), never as an empty frame filled
/// after; one with nothing read yet waits a moment for its first answer. Under Windows' "Animation effects" off,
/// pages simply change.
/// What is kept, against memory: the Dashboard's page always, and (0.3.76) the main places' (<see cref="MainPlaces"/>),
/// built ahead while the window is left alone; of the rest, the last <see cref="KeptPages"/> shown, and any with
/// something of the student's in it still. Every other screen is let go — its page, its tree, the screen —
/// and made again from the answers kept (AnswerCache: in memory, and sealed on disk) when it is next shown.
/// </summary>
public sealed partial class AppShell
{
    /// <summary>How many screens besides the Dashboard and the main places keep their pages (the one showing among them).</summary>
    private const int KeptPages = 3;

    /// <summary>(0.3.76) The places the pane leads to most, kept as the Dashboard is and built ahead (<see cref="WarmMainPages"/>);
    /// (0.3.77) Tools with them.</summary>
    private static readonly Place[] MainPlaces = { Place.Calendar, Place.Grades, Place.Notifications, Place.Inbox, Place.Tools };

    /// <summary>The main places' pages built ahead, as far as they will be (or never to be: pictures, --no-prefetch) — the
    /// perf walk's --perf-warmed waits for it.</summary>
    internal static bool WarmedUp { get; private set; }

    /// <summary>When something was last pressed, typed or moved over in the window.</summary>
    private long lastInput = Environment.TickCount64;

    /// <summary>A screen's page in the deck: its scroller (or the screen itself, for one that scrolls its own parts),
    /// moved and faded as a whole.</summary>
    private sealed class Leaf
    {
        public readonly Screen Screen;
        public readonly Grid Root = new();
        public readonly TranslateTransform Shift = new();
        public ScrollViewer? Scroller;
        /// <summary>The move under way on it (0: none).</summary>
        public int Turn;
        /// <summary>Prepared ahead of a click (laid out, unseen), and when.</summary>
        public long PreparedAt;

        public Leaf(Screen screen)
        {
            Screen = screen;
            Root.RenderTransform = Shift;
        }
    }

    /// <summary>How a page comes in.</summary>
    private enum Move { None, Deeper, Back, Across }

    private readonly Grid deck = new() { ClipToBounds = true };
    private readonly Dictionary<Screen, Leaf> leaves = new();
    /// <summary>The titles of screens let go, for the breadcrumb.</summary>
    private readonly Dictionary<string, string> titles = new();
    private Location? shownAt0;
    /// <summary>The page last brought in (the one showing, unless one is waiting for its first answer).</summary>
    private Leaf? shownLeaf;
    private int turn;
    private DispatcherTimer? preparedSweep;

    /// <summary>The screen for a place, or a route on it: the one kept, else made (<paramref name="make"/>) and kept.</summary>
    private Screen? ScreenAt(Location loc, bool make)
    {
        if (loc.Path.Count > 0)
        {
            var key = loc.Path[^1].ToString();
            if (routeScreens.TryGetValue(key, out var s)) return s;
            if (!make) return null;
            return routeScreens[key] = Driven(ScreenFactory.For(engine, loc.Path[^1]));
        }
        var id = loc.Place.Identity;
        if (placeScreens.TryGetValue(id, out var p)) return p;
        if (!make) return null;
        return placeScreens[id] = Driven(ScreenFactory.For(engine, loc.Place));
    }

    /// <summary>A screen of the window's own, looked after by the live check: what it shows that is only old is checked as
    /// it is shown, not read again.</summary>
    private static Screen Driven(Screen s)
    {
        s.LiveDriven = LiveCheck.Enabled;
        return s;
    }

    /// <summary>The place and what is pushed on it, shown: its screen (kept, or made again from the answers kept), built
    /// at once if it must be, brought in with the move that took it there; then the header.</summary>
    private void Navigate()
    {
        if (engine.NavVersion == lastNav && current != null) return;
        lastNav = engine.NavVersion;
        var loc = engine.Nav.Current;
        var next = ScreenAt(loc, make: true)!;
        var move = MoveOf(shownAt0, loc, engine.LastMove);
        shownAt0 = loc;
        if (next != current)
        {
            var was = current;
            if (was != null) { was.Hide(); was.HeaderChanged -= Header; }
            current = next;
            current.HeaderChanged += Header;
            var stamp = Perf.Ticks();
            current.SetWidth(ContentWidth, WindowActualWidth);
            var kept = current.Ready;
            // (built at once, so the page comes in with what it shows — unless the page area has not been laid out yet,
            //  the shell just made: then once it has, at its width, as before)
            current.Show(now: page.ActualWidth > 0);
            Perf.Opened(ScreenName(current), kept ? "kept" : current.Placeholder ? "read" : "cached", stamp);
            Switch(shownLeaf, Mount(current), was == null ? Move.None : move);
        }
        else
        {
            current.SetWidth(ContentWidth, WindowActualWidth);
            current.Show();
            // (the same screen with something else in it — a search typed further: from its top)
            if (leaves.TryGetValue(current, out var same)) same.Scroller?.ScrollToTop();
        }
        shownAt[current] = ++showings;
        Trim();
        Header();
        // (a screen shown again with what it shows a minute old: checked at once)
        live.Poke();
    }

    private static string ScreenName(Screen s) => s.GetType().Name.Replace("Screen", "");

    /// <summary>Deeper or back within a place, or something opened on another (deeper); another place (across).</summary>
    private static Move MoveOf(Location? from, Location to, NavMove how)
    {
        if (from == null) return Move.None;
        if (from.Place.Identity == to.Place.Identity)
        {
            if (to.Path.Count > from.Path.Count) return Move.Deeper;
            if (to.Path.Count < from.Path.Count) return Move.Back;
            return how == NavMove.Back ? Move.Back : Move.Deeper;
        }
        if (to.Path.Count > 0 && how != NavMove.Back) return Move.Deeper;
        if (from.Path.Count > 0 && how == NavMove.Back) return Move.Back;
        return Move.Across;
    }

    // ---- the deck ----

    /// <summary>The screen's page, in the deck: its scroller and the screen's content in it, sized for the page now.</summary>
    private Leaf Mount(Screen s)
    {
        if (!leaves.TryGetValue(s, out var leaf)) leaves[s] = leaf = new Leaf(s);
        var v = s.View;
        if (s.Scrolls)
        {
            leaf.Scroller ??= new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            // (a reading width: as wide as the page up to it, from the left — sized, so it never shrinks to its words)
            if (s.MaxContentWidth is { } mw) { v.MaxWidth = mw; v.Width = Math.Min(mw, ContentWidth); v.HorizontalAlignment = HorizontalAlignment.Left; }
            else { v.ClearValue(FrameworkElement.WidthProperty); v.ClearValue(FrameworkElement.MaxWidthProperty); v.HorizontalAlignment = HorizontalAlignment.Stretch; }
            // (the room at the page's foot is the page's own, so it scrolls: a scroller's padding would stand still under it)
            v.Margin = new Thickness(0, 0, 0, Foot);
            leaf.Scroller.Padding = SidePadding;
            if (!ReferenceEquals(leaf.Scroller.Content, v)) { Detach(v); leaf.Scroller.Content = v; }
            if (leaf.Root.Children.Count != 1 || !ReferenceEquals(leaf.Root.Children[0], leaf.Scroller)) { leaf.Root.Children.Clear(); leaf.Root.Children.Add(leaf.Scroller); }
        }
        else
        {
            v.Margin = new Thickness(0);
            if (!ReferenceEquals(v.Parent, leaf.Root)) { Detach(v); leaf.Root.Children.Clear(); leaf.Root.Children.Add(v); }
        }
        if (leaf.Root.Parent == null) deck.Children.Add(leaf.Root);
        return leaf;
    }

    private static void Detach(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case ContentControl c: c.Content = null; break;
            case Decorator d: d.Child = null; break;
        }
    }

    private Thickness SidePadding => page.ActualWidth < 560 ? new Thickness(20, 0, 20, 0) : new Thickness(40, 0, 40, 0);

    /// <summary>The pages' widths and room, as the page area's size now (the one showing; the rest as they are shown).</summary>
    private void PageWidths()
    {
        if (current is { Scrolls: true, MaxContentWidth: { } mw }) current.View.Width = Math.Min(mw, ContentWidth);
        current?.SetWidth(ContentWidth, WindowActualWidth);
        if (current != null && leaves.TryGetValue(current, out var leaf) && leaf.Scroller != null)
        {
            leaf.Scroller.Padding = SidePadding;
            current.View.Margin = new Thickness(0, 0, 0, Foot);
        }
    }

    // ---- the move ----

    /// <summary>
    /// The page shown, brought in as <paramref name="move"/> says and the one before it taken out — or, the page having
    /// nothing to show yet, a moment from now (its first answer, or 300 ms), so it never comes in empty and fills after.
    /// </summary>
    private void Switch(Leaf? from, Leaf to, Move move)
    {
        var mine = ++turn;
        to.PreparedAt = 0;
        // (above everything else in the deck, taking the pointer; the one going lets go of it at once)
        Panel.SetZIndex(to.Root, mine);
        to.Root.IsHitTestVisible = true;
        if (from != null && from != to) from.Root.IsHitTestVisible = false;
        var animate = move != Move.None && from != null && from != to && Motion.On;
        if (animate && to.Screen.Placeholder)
        {
            // nothing to show yet: the page before stays while the first answer comes, 300 ms at most
            to.Root.Visibility = Visibility.Hidden;
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(300) };
            void Go()
            {
                timer.Stop();
                to.Screen.Built -= Built;
                if (mine == turn) Run(from, to, move, mine);
            }
            void Built() { if (!to.Screen.Placeholder) Go(); }
            to.Screen.Built += Built;
            timer.Tick += (_, _) => Go();
            timer.Start();
            return;
        }
        Run(from, to, animate ? move : Move.None, mine);
    }

    private void Run(Leaf? from, Leaf to, Move move, int mine)
    {
        shownLeaf = to;
        to.Root.Visibility = Visibility.Visible;
        // (any other page still up — one that waited for its answer and was passed by — folds away at once)
        foreach (var l in leaves.Values)
            if (l != to && l != from && l.PreparedAt == 0 && l.Root.Visibility != Visibility.Collapsed) Away(l);
        if (move == Move.None || from == null || HasWindow(to.Root) || HasWindow(from.Root))
        {
            Rest(to);
            if (from != null && from != to) Away(from);
            FadeHeader(false);
            return;
        }
        // where each starts (a page caught mid-move starts from where it is) and where it ends
        var (inX, inY, outX) = move switch
        {
            Move.Deeper => (32.0, 0.0, -16.0),
            Move.Back => (-16.0, 0.0, 32.0),
            _ => (0.0, 8.0, 0.0),
        };
        if (to.Turn == 0)
        {
            Still(to);
            to.Root.Opacity = 0;
            to.Shift.X = inX;
            to.Shift.Y = inY;
        }
        to.Turn = mine;
        from.Turn = mine;
        Cache(to.Root, true);
        Cache(from.Root, true);
        var ms = Motion.Page;
        Motion.To(to.Root, UIElement.OpacityProperty, 1, ms, Motion.Out);
        Motion.To(to.Shift, TranslateTransform.XProperty, 0, ms, Motion.Out);
        Motion.To(to.Shift, TranslateTransform.YProperty, 0, ms, Motion.Out, 0, () => { if (to.Turn == mine) Rest(to); });
        // (the page going fades quicker than the one coming arrives, so the two are never both half there for long)
        Motion.To(from.Root, UIElement.OpacityProperty, 0, ms * 0.6, Motion.Out);
        Motion.To(from.Shift, TranslateTransform.XProperty, outX, ms, Motion.Out, 0, () =>
        {
            if (from.Turn != mine) return;
            Away(from);
            Trim();
        });
        FadeHeader(true);
    }

    /// <summary>No move left on a page: shown as it stands.</summary>
    private static void Rest(Leaf l)
    {
        Still(l);
        l.Turn = 0;
        Cache(l.Root, false);
    }

    /// <summary>A page gone from view: folded away, as it stands, ready to come back.</summary>
    private void Away(Leaf l)
    {
        Rest(l);
        if (l.Screen != current) l.Root.Visibility = Visibility.Collapsed;
    }

    private static void Still(Leaf l)
    {
        l.Root.BeginAnimation(UIElement.OpacityProperty, null);
        l.Root.Opacity = 1;
        l.Shift.BeginAnimation(TranslateTransform.XProperty, null);
        l.Shift.BeginAnimation(TranslateTransform.YProperty, null);
        l.Shift.X = 0;
        l.Shift.Y = 0;
    }

    /// <summary>
    /// While it moves, a page is drawn once into a picture of itself and the picture moved (where the graphics card does
    /// the work): a page of a thousand parts costs no more to move than one, so every frame of the move is drawn.
    /// </summary>
    private static void Cache(FrameworkElement e, bool on)
    {
        if (!on) { if (e.CacheMode != null) e.CacheMode = null; return; }
        if (RenderCapability.Tier >> 16 < 2 || e.CacheMode != null) return;
        var scale = VisualTreeHelper.GetDpi(e).DpiScaleX;
        e.CacheMode = new BitmapCache(scale) { SnapsToDevicePixels = true };
    }

    /// <summary>The title comes in with the page (faded in over the same move), not before it.</summary>
    private void FadeHeader(bool animate)
    {
        if (!animate) { titleHost.BeginAnimation(UIElement.OpacityProperty, null); titleHost.Opacity = 1; return; }
        Motion.FromTo(titleHost, UIElement.OpacityProperty, 0, 1, Motion.Page, Motion.Out);
    }

    /// <summary>A window of its own inside a page (a web view): it cannot move or fade with the page, so the page changes at once.</summary>
    private static bool HasWindow(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        var seen = 0;
        while (stack.Count > 0 && seen++ < 6000)
        {
            var d = stack.Pop();
            if (d is HwndHost) return true;
            var n = VisualTreeHelper.GetChildrenCount(d);
            for (var i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
        return false;
    }

    // ---- what is kept ----

    /// <summary>
    /// The screens kept, against memory: the one showing; the Dashboard; the last <see cref="KeptPages"/> shown; any
    /// still busy with something of the student's; a page still on its way out, or prepared a moment ago. The rest are
    /// let go.
    /// </summary>
    private void Trim()
    {
        var recent = shownAt.Where(kv => kv.Key is not DashboardScreen).OrderByDescending(kv => kv.Value).Take(KeptPages).Select(kv => kv.Key).ToHashSet();
        var now = Environment.TickCount64;
        foreach (var s in placeScreens.Values.Concat(routeScreens.Values).ToList())
        {
            if (s == current || s is DashboardScreen || recent.Contains(s) || s.Busy || IsMain(s)) continue;
            if (leaves.TryGetValue(s, out var l) && (l.Turn != 0 || (l.PreparedAt != 0 && now - l.PreparedAt < 2000))) continue;
            Release(s);
        }
    }

    /// <summary>One of the main places' screens (kept).</summary>
    private bool IsMain(Screen s) => placeScreens.Any(kv => kv.Value == s && MainPlaces.Contains(kv.Key));

    /// <summary>
    /// (0.3.76) The main places — To Do &amp; Calendar, Grades, Notifications, the Inbox, (0.3.77) Tools — built ahead and
    /// laid out, unseen, one at a time once the start has settled: each as soon as its answers are in (Prefetcher reads
    /// them ahead), and only while nobody is using the window — nothing pressed, typed or moved over for a second and a
    /// half, no page coming in. Their first opening is then a page shown, as a kept page is — not a page built and laid
    /// out while the click waits (To Do &amp; Calendar's took 300 ms and more, the window still meanwhile). (0.3.77) Reads
    /// under way no longer hold it back: with the read-ahead's and the live check's going, 0.3.76's wait for none at all
    /// seldom came in time, and the pages were left to be built as they were clicked. Not in the screenshot suite's
    /// pictures, nor with --no-prefetch, nor in a perf walk but one given --perf-warmed (built while the walk times its
    /// steps, a page would be counted in the step it fell in).
    /// </summary>
    private async void WarmMainPages()
    {
        if (Shot.Asked || Launch.Flag("no-prefetch") || Launch.Get("perf-walk") != null && !Launch.Flag("perf-warmed")) { WarmedUp = true; return; }
        try
        {
            InputManager.Current.PostProcessInput += (_, a) =>
            {
                if (a.StagingItem.Input is MouseEventArgs or KeyboardEventArgs or TouchEventArgs or StylusEventArgs) lastInput = Environment.TickCount64;
            };
            while (!engine.CountsLive) await Task.Delay(250);
            await Task.Delay(1500);
            var built = new List<string>();
            var at = Environment.TickCount64;
            foreach (var place in MainPlaces)
            {
                if (place == Place.Inbox && engine.OnBrightspace) continue;
                // (a minute at most for a quiet moment: someone busy in the window has it as before)
                if (!await LeftAlone(60_000)) continue;
                var s = ScreenAt(new Location(place), make: true)!;
                if (s == current || leaves.ContainsKey(s)) continue;
                s.SetWidth(ContentWidth, WindowActualWidth);
                if (!s.Ready) s.Prefetch();
                for (var i = 0; i < 40 && !s.Ready; i++) await Task.Delay(250);
                if (!await LeftAlone(30_000)) continue;
                // (looked at again as it is built: a press may have come while it waited its turn)
                var made = await Dispatcher.InvokeAsync(() =>
                {
                    if (!Alone() || s == current || leaves.ContainsKey(s)) return false;
                    BuildAhead(s);
                    return leaves.ContainsKey(s);
                }, DispatcherPriority.ApplicationIdle);
                if (made) built.Add(s.Title);
                await Task.Delay(400);
            }
            Log.Write($"warm: {built.Count} main pages built ahead in {(Environment.TickCount64 - at) / 1000.0:0.0} s{(built.Count > 0 ? " (" + string.Join(", ", built) + ")" : "")}");
        }
        catch (Exception e) { Log.Write("warm: " + e.Message); }
        finally { WarmedUp = true; }
    }

    /// <summary>The app's own screens up, and nobody using the window this moment: nothing pressed, typed or moved over for
    /// a second and a half, no page coming in.</summary>
    private bool Alone() => engine.Phase == Phase.Native && Environment.TickCount64 - lastInput >= 1500 && shownLeaf?.Turn is null or 0;

    /// <summary>Waits for <see cref="Alone"/>, <paramref name="limitMs"/> at most: whether it came.</summary>
    private async Task<bool> LeftAlone(int limitMs)
    {
        for (var waited = 0; !Alone(); waited += 250)
        {
            if (waited >= limitMs) return false;
            await Task.Delay(250);
        }
        return true;
    }

    /// <summary>A screen let go: its page out of the deck, its tree and the screen itself — its title kept for the breadcrumb.</summary>
    private void Release(Screen s)
    {
        foreach (var kv in routeScreens.Where(kv => kv.Value == s).ToList()) { titles[kv.Key] = s.Title; routeScreens.Remove(kv.Key); }
        foreach (var kv in placeScreens.Where(kv => kv.Value == s).ToList()) placeScreens.Remove(kv.Key);
        shownAt.Remove(s);
        if (leaves.Remove(s, out var leaf))
        {
            if (shownLeaf == leaf) shownLeaf = null;
            Still(leaf);
            Cache(leaf.Root, false);
            leaf.Root.Visibility = Visibility.Collapsed;
            // (taken out of the window once nothing else is happening: taking a page's tree out walks all of it, which
            //  is no part of bringing the next page in)
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                deck.Children.Remove(leaf.Root);
                if (leaf.Scroller != null) leaf.Scroller.Content = null;
                leaf.Root.Children.Clear();
            });
        }
        s.Hide();
        s.Release();
    }

    /// <summary>The title a step of the breadcrumb shows: its screen's, or the one it had when let go.</summary>
    private string CrumbTitle(Route r) =>
        routeScreens.TryGetValue(r.ToString(), out var rs) ? rs.Title : titles.TryGetValue(r.ToString(), out var t) ? t : ScreenFactory.TitleOf(r);

    // ---- ahead of a click ----

    /// <summary>The engine's word (Kit.Ahead): a place, or a screen opened on one, about to be shown.</summary>
    private void OnPrepare(Place place, Route? route, bool build) =>
        Prepare(route != null ? new Location(place, new[] { route }) : new Location(place), build);

    /// <summary>
    /// A pointer resting on the way to a screen: what it shows is read (<paramref name="build"/> false); pressed, about
    /// to let go: also built and laid out, unseen, so the click brings it in at once. The build waits for the press to
    /// have drawn (the button shows it is pressed at once); the click, a tenth of a second or so later, finds it built.
    /// </summary>
    private void Prepare(Location loc, bool build)
    {
        try
        {
            var s = ScreenAt(loc, make: build);
            if (s == null)
            {
                // (only resting on it: read into the answers kept, through a screen made for it and let go after)
                if (!PrefetchBusy) _ = prefetch.One(loc.Place, loc.Path.Count > 0 ? loc.Path[^1] : null);
                return;
            }
            if (s == current) return;
            s.SetWidth(ContentWidth, WindowActualWidth);
            if (!s.Ready) s.Prefetch();
            if (build) Dispatcher.BeginInvoke(DispatcherPriority.Background, () => BuildAhead(s));
        }
        catch (Exception e) { Log.Write("prepare: " + e.Message); }
    }

    /// <summary>A screen pressed on the way to: built (from what it has read) and its page laid out, unseen.</summary>
    private void BuildAhead(Screen s)
    {
        try
        {
            if (s == current || !placeScreens.ContainsValue(s) && !routeScreens.ContainsValue(s)) return;
            if (!s.Ready) s.RenderNow();
            if (s.Placeholder) return;
            var leaf = Mount(s);
            if (leaf.Turn == 0 && leaf.Root.Visibility != Visibility.Visible)
            {
                // (laid out at the next pass, never drawn until it is shown)
                leaf.Root.Visibility = Visibility.Hidden;
                Panel.SetZIndex(leaf.Root, -1);
            }
            leaf.PreparedAt = Environment.TickCount64;
            SweepPreparedSoon();
        }
        catch (Exception e) { Log.Write("prepare: " + e.Message); }
    }

    /// <summary>A page prepared and never clicked through to: folded away again after a moment.</summary>
    private void SweepPreparedSoon()
    {
        if (preparedSweep == null)
        {
            preparedSweep = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2500) };
            preparedSweep.Tick += (_, _) =>
            {
                preparedSweep.Stop();
                var now = Environment.TickCount64;
                foreach (var l in leaves.Values.ToList())
                    if (l.PreparedAt != 0 && now - l.PreparedAt >= 2000 && l.Screen != current)
                    {
                        l.PreparedAt = 0;
                        l.Root.Visibility = Visibility.Collapsed;
                    }
                Trim();
            };
        }
        preparedSweep.Stop();
        preparedSweep.Start();
    }

    /// <summary>The look changed: every kept screen built again — the one showing now, the rest as they are shown.</summary>
    private void RestyleScreens()
    {
        foreach (var s in placeScreens.Values.Concat(routeScreens.Values)) s.Restyle();
    }
}

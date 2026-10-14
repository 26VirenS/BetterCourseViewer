using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Shell;

/// <summary>The parts of the window the tour lights (the Mac's TourSpot).</summary>
public enum TourSpot { Pane, Account, Search, DashboardRow, GradesRow, ToolsRow, FirstCourse, GradeRing, GradeDetails, WhatIfSwitch, WhatIfScore, ToolPin, CounterNext, CounterPanel, Preview, Counters }

/// <summary>What a step may wait for that the window's own state does not show (the Mac's TourEvent).</summary>
public enum TourEvent { RingHover, WhatIfOn, WhatIfEdited, WhatIfOff, CourseHover, Pinned, CounterOpened }

/// <summary>
/// The tour's registry (the Mac's MacTour and TourFrames): each lit part says where it is (<see cref="Mark"/>), and
/// what is done that a step may wait for is told (<see cref="Did"/>). Screens call these; the overlay reads them.
/// </summary>
public static class Tour
{
    private static readonly Dictionary<TourSpot, List<WeakReference<FrameworkElement>>> Spots = new();
    internal static TourEvent? LastEvent;
    internal static int Events;
    public static event Action? Changed;

    /// <summary>An element is a lit part (the first of its kind on screen — the topmost, then the leftmost — is lit).</summary>
    public static T Mark<T>(this T e, TourSpot spot) where T : FrameworkElement
    {
        if (!Spots.TryGetValue(spot, out var list)) Spots[spot] = list = new();
        list.RemoveAll(w => !w.TryGetTarget(out var x) || x == e);
        list.Add(new WeakReference<FrameworkElement>(e));
        return e;
    }

    /// <summary>Something done that a step may wait for (nothing while the tour is off).</summary>
    public static void Did(TourEvent e)
    {
        LastEvent = e;
        Events++;
        Changed?.Invoke();
    }

    /// <summary>
    /// A step's part, out of sight in its scrolling list (below the fold, or under what floats at the page's foot — the
    /// What-If bar), scrolled to stand a little above the list's middle, as the Mac's tour scrolls to its part. The part
    /// taken is the first in the list. True once there is one to show (scrolled or already in sight).
    /// </summary>
    internal static bool Reveal(TourSpot spot, FrameworkElement over)
    {
        if (!Spots.TryGetValue(spot, out var list)) return false;
        var source = PresentationSource.FromVisual(over);
        FrameworkElement? first = null;
        ScrollViewer? within = null;
        var firstAt = double.MaxValue;
        foreach (var w in list.ToList())
        {
            if (!w.TryGetTarget(out var e) || !e.IsVisible || e.ActualWidth < 2 || source == null || PresentationSource.FromVisual(e) != source) continue;
            ScrollViewer? sv = null;
            for (DependencyObject? p = VisualTreeHelper.GetParent(e); p != null; p = VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer found) { sv = found; break; }
            if (sv == null) return true; // (not in a list: where it is, it is seen)
            double at;
            try { at = e.TranslatePoint(new Point(0, 0), sv).Y + sv.VerticalOffset; } catch { continue; }
            if (at < firstAt) { firstAt = at; first = e; within = sv; }
        }
        if (first == null || within == null) return false;
        var top = firstAt - within.VerticalOffset;
        if (top >= 24 && top + first.ActualHeight <= within.ViewportHeight - 120) return true;
        within.ScrollToVerticalOffset(Math.Max(0, firstAt - within.ViewportHeight * 0.4));
        return true;
    }

    /// <summary>Where a part is in an element's coordinates, or null when none is on screen.</summary>
    internal static Rect? Find(TourSpot spot, FrameworkElement over)
    {
        if (!Spots.TryGetValue(spot, out var list)) return null;
        Rect? best = null;
        foreach (var w in list.ToList())
        {
            if (!w.TryGetTarget(out var e)) { list.Remove(w); continue; }
            if (!e.IsVisible || e.ActualWidth < 2 || PresentationSource.FromVisual(e) == null || PresentationSource.FromVisual(over) != PresentationSource.FromVisual(e)) continue;
            Rect r;
            try { r = new Rect(e.TranslatePoint(new Point(0, 0), over), new Size(e.ActualWidth, e.ActualHeight)); } catch { continue; }
            // (cut to what its scrolling list shows)
            for (DependencyObject? p = VisualTreeHelper.GetParent(e); p != null; p = VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer sv)
                {
                    var vr = new Rect(sv.TranslatePoint(new Point(0, 0), over), new Size(sv.ActualWidth, sv.ActualHeight));
                    r.Intersect(vr);
                    break;
                }
            if (r.IsEmpty || r.Width < 2 || r.Height < 2) continue;
            if (best is { } b && (b.Y < r.Y - 1 || (Math.Abs(b.Y - r.Y) <= 1 && b.X <= r.X))) continue;
            best = r;
        }
        return best;
    }
}

/// <summary>
/// The guided tour over the window (the Mac's TourOverlay; the web's welcome.js), as a Windows 11 TeachingTip: one part
/// lit at a time, the whole window dimmed round it — the title bar's buttons too, all but Windows' own caption buttons —
/// the part ringed in the accent, and a callout beside it: its count, what it is, Skip tour and × to leave, Back and a
/// Next that always works. A step may suggest something to try (click Grades) and moves on once it is done, but never
/// waits for it. Six steps, from the Dashboard — the counters, Grades, a course, Tools, Search — to where a bug is
/// reported. It comes once, after the app first shows with nothing over it; Escape, Skip tour or × end it, and that
/// counts as seen; the account menu's Take the tour runs it again. Its last step's Done says it was followed to the end
/// (<see cref="Completed"/>: Simpl then asks, once, to be kept on the taskbar).
/// </summary>
public sealed class TourOverlay : Grid
{
    private sealed record Step(string Id, TourSpot? Spot, string Title, string Body, string? Doing = null, Func<bool>? Done = null, string NextLabel = "Next");

    /// <summary>Windows' caption buttons at the title bar's right (minimise, maximise, close): left lit and working
    /// through the tour.</summary>
    private const double CaptionButtons = 138;

    private readonly Engine.Engine engine;
    private readonly MainWindow window;
    private readonly Path veil = new();
    // (the light and its ring are one shape, so one move carries both: the dim is the window less it — and less
    //  Windows' caption buttons, which stay as they are)
    private readonly RectangleGeometry whole = new(), captions = new(), light = new() { RadiusX = 10, RadiusY = 10 };
    private readonly CombinedGeometry bare, dim;
    private readonly Path ring = new() { StrokeThickness = 2, IsHitTestVisible = false, Opacity = 0, Visibility = Visibility.Collapsed };
    private bool ringOn;
    private bool revealed;
    private readonly Border card = new();
    private readonly Canvas layer = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private List<Step> steps = new();
    private int? index;
    private bool advancing;
    private int seenEvents;
    private Rect? lastHole;

    public bool On => index != null;

    /// <summary>The last step's Done pressed: the tour followed to its end (not left early).</summary>
    public event Action? Completed;

    public TourOverlay(Engine.Engine engine, MainWindow window)
    {
        this.engine = engine;
        this.window = window;
        Visibility = Visibility.Collapsed;
        veil.Fill = K.Brush(Color.FromArgb(Appearance.Dark ? (byte)128 : (byte)72, 0, 0, 0));
        veil.MouseLeftButtonDown += (_, e) => { Nudge(); e.Handled = true; };
        bare = new CombinedGeometry(GeometryCombineMode.Exclude, whole, captions);
        dim = new CombinedGeometry(GeometryCombineMode.Exclude, bare, light);
        veil.Data = bare;
        ring.Data = light;
        ring.SetResourceReference(Shape.StrokeProperty, B.Accent);
        ring.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6, Color = Appearance.Accent };
        layer.Children.Add(veil);
        layer.Children.Add(ring);
        layer.Children.Add(card);
        Children.Add(layer);
        timer.Tick += (_, _) => Tick();
        Tour.Changed += () => { if (On) Tick(); };
        PreviewKeyDown += OnKey;
        Focusable = true;
    }

    // ---- the steps (the Mac's, and the web's welcome.js) ----

    private List<Step> Steps()
    {
        var nav = engine.Nav;
        bool At(PlaceKind k) => nav.Current.Place.Kind == k && nav.Current.Path.Count == 0;
        var s = new List<Step>
        {
            new("dashboard", TourSpot.Counters, "Everything due, at a glance", "Each counter opens what it counts: work due today, tomorrow and in the next 7 days, anything overdue, new announcements and this week’s grades."),
            new("grades", TourSpot.GradesRow, "Grades", "All your grades and your GPA in one place.", "Click Grades to look", () => At(PlaceKind.Grades)),
        };
        if (engine.Courses.Count > 0)
            s.Add(new("courses", TourSpot.FirstCourse, "Your courses", "Each course opens to its home, with its sections under it here."));
        s.Add(new("tools", TourSpot.ToolsRow, "Tools", "A PDF editor, a file converter, calculators, flashcards, a citation maker and more. Pin one to keep it in the title bar.", "Click Tools to look", () => At(PlaceKind.Tools)));
        s.Add(new("search", TourSpot.Search, "Search everything", "Find anything in your courses. Type / for commands, or press Ctrl+K."));
        s.Add(new("report", TourSpot.Account, "Something wrong? Tell us here", "Report a bug or ask for something new from your account menu. You can take this tour again from the same menu.", NextLabel: "Done"));
        return s;
    }

    // ---- on and off ----

    public void Start(int at = 0)
    {
        WorkPreview.Dismiss();
        // (from the start, on the Dashboard; one begun part-way, as the screenshot suite does, stays where it is opened)
        if (at == 0 && (engine.Nav.Current.Place != Place.Dashboard || engine.Nav.Current.Path.Count > 0)) engine.Go(Place.Dashboard);
        steps = Steps();
        index = null;
        lastHole = null;
        // (laid out first, so the first step's callout pops where it belongs)
        Visibility = Visibility.Visible;
        UpdateLayout();
        Show(Math.Clamp(at, 0, steps.Count - 1));
        timer.Start();
        Motion.FadeIn(this, Motion.Medium);
    }

    private void Show(int i)
    {
        // (from a part lit to the next part, the callout goes with the light, as the Mac's does; a step with nothing lit
        // pops in place)
        var shown = index != null && card.ActualHeight > 0;
        index = i;
        revealed = false;
        seenEvents = Tour.Events;
        advancing = false;
        BuildCard();
        if (!Arrange(animate: shown, step: true)) Motion.Pop(card);
        Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
        Shot.Moved();
    }

    private void Finish()
    {
        index = null;
        timer.Stop();
        Prefs.Set("tour:done", true);
        Motion.FadeOut(this, () => Visibility = Visibility.Collapsed, Motion.Normal);
    }

    /// <summary>Left before its end (Skip tour, ×, Escape): it is over, and counts as seen — the account menu's Take the
    /// tour brings it back.</summary>
    public void Skip()
    {
        if (index == null) return;
        Finish();
    }

    private void Next()
    {
        if (index is not { } i) return;
        if (i + 1 < steps.Count) { Show(i + 1); return; }
        Finish();
        Completed?.Invoke();
    }

    private void Back()
    {
        if (index is { } i && i > 0) Show(i - 1);
    }

    /// <summary>Once, after the window first appears with nothing over it for a moment (never in the screenshot suite,
    /// whose student has used the app, unless it asks with --open tour or tour:4).</summary>
    public async void AutoStart()
    {
        if (Launch.Take("tour") is { } at)
        {
            await Task.Delay(3000);
            Start((int.TryParse(at.Trim(':', ' '), out var n) ? n : 1) - 1);
            return;
        }
        if (Prefs.Bool("tour:done") || Launch.IsDevelopment) return;
        var calm = 0;
        // (as long as the app is up: a setup or a sign-in that takes a while still has the tour after it, as on the Mac)
        for (var ticks = 0; !Prefs.Bool("tour:done") && !On; ticks++)
        {
            await Task.Delay(500);
            var setUp = engine.Snapshot?.SetupDone != false || SetupSheet.Seen;
            var free = engine.Phase == Phase.Native && setUp && !window.Sheets.IsOpen && !engine.Login.Covers && engine.Query.Length == 0 && !WorkPreview.IsOpen;
            var ready = free && (engine.Courses.Count > 0 || ticks >= 20);
            calm = ready ? calm + 1 : 0;
            if (calm >= 5) { Start(); return; }
        }
    }

    // ---- each beat: was the step's suggestion taken, and where is its part now ----

    private void Tick()
    {
        if (index is not { } i || i >= steps.Count) return;
        Arrange(animate: true);
        var s = steps[i];
        if (s.Done == null || advancing || !s.Done()) return;
        advancing = true;
        var at = i;
        _ = Task.Delay(450).ContinueWith(_ => Dispatcher.BeginInvoke(() => { if (index == at) Next(); }));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (index == null) return;
        if (e.Key is Key.Right or Key.Enter) { Next(); e.Handled = true; }
        else if (e.Key == Key.Left) { Back(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Skip(); e.Handled = true; }
    }

    private void Nudge()
    {
        if (!Motion.On) return;
        var s = new ScaleTransform(1, 1);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = s;
        var a = new System.Windows.Media.Animation.DoubleAnimation(1, 1.03, Motion.Ms(120)) { AutoReverse = true, EasingFunction = Motion.Standard };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // ---- the callout (Windows 11's TeachingTip) ----

    private void BuildCard()
    {
        var s = steps[index!.Value];
        var i = index.Value;
        var last = i == steps.Count - 1;
        var count = K.Text($"{i + 1} of {steps.Count}", Ty.Caption, B.Text2);
        var close = K.IconButton(SymbolRegular.Dismiss24, Skip, "Close the tour (Esc)", 12, 28);
        close.Margin = new Thickness(0, -6, -10, 0);
        var top = K.Columns("*, auto", count.Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), close.Col(1));
        var title = K.Wrapped(s.Title, Ty.Subtitle);
        var body = K.Wrapped(s.Body, Ty.Body, B.Text2);
        var v = K.V(8, top, title, body);
        if (s.Doing != null)
        {
            var doing = K.Text(s.Doing, Ty.BodyStrong, B.AccentText);
            v.Children.Add(K.H(8, K.Icon(SymbolRegular.CursorClick24, 16, B.AccentText), doing).Margin(0, 4, 0, 0));
        }
        v.Children.Add(K.Bar((i + 1.0) / steps.Count, Appearance.Accent, 3).Margin(0, 6, 0, 2));
        var buttons = K.Columns("auto, *, auto");
        if (!last)
        {
            var skip = K.Button("Skip tour", Skip);
            skip.MinWidth = 0;
            skip.ToolTip = "End the tour (Esc). Take it again from your account menu.";
            buttons.Children.Add(skip.Col(0));
        }
        var moves = K.H(8);
        if (i > 0) moves.Children.Add(K.Button("Back", Back));
        moves.Children.Add(K.Button(s.NextLabel, Next, accent: true));
        buttons.Children.Add(moves.Col(2));
        v.Children.Add(buttons.Margin(0, 2, 0, 0));
        card.Child = v;
        card.Width = 340;
        card.Padding = new Thickness(18, 14, 18, 16);
        card.CornerRadius = new CornerRadius(8);
        card.Effect = K.Shadow(36, 10, 0.3);
        card.Bg(B.Flyout).Stroke(B.FlyoutStroke);
    }

    /// <summary>The light, its ring and the callout where the step's part is now; true when they travelled there together
    /// from the part lit before (a new step's card then goes with them rather than popping).</summary>
    private bool Arrange(bool animate, bool step = false)
    {
        if (index is not { } i) return false;
        var s = steps[i];
        var size = new Size(ActualWidth, ActualHeight);
        if (size.Width < 10) return false;
        // (the step's part brought into sight first, once a step, when it waits out of sight in its list)
        if (!revealed && s.Spot is { } wanted) revealed = Tour.Reveal(wanted, this);
        Rect? hole = s.Spot is { } spot ? Tour.Find(spot, this) : null;
        if (hole is { } h) { h.Inflate(6, 6); hole = h; }
        whole.Rect = new Rect(size);
        captions.Rect = new Rect(Math.Max(0, size.Width - CaptionButtons), 0, Math.Min(CaptionButtons, size.Width), MainWindow.TitleBarHeight);
        if (!step && hole == lastHole && card.ActualHeight > 0 && animate) return false;
        var from = lastHole;
        lastHole = hole;
        // the dim, less the light: from one part to the next the light glides as the callout does (Motion.Slow); with
        // nothing lit on one side, the dim changes at once and the ring fades
        var travel = animate && Motion.On && from != null && hole != null;
        if (hole is { } r)
        {
            if (!travel) { light.BeginAnimation(RectangleGeometry.RectProperty, null); light.Rect = r; }
            else if (r != from) light.BeginAnimation(RectangleGeometry.RectProperty, new System.Windows.Media.Animation.RectAnimation(r, Motion.Ms(Motion.Slow)) { EasingFunction = Motion.Decelerate });
            veil.Data = dim;
        }
        else veil.Data = bare;
        Ring(hole != null);
        // the callout beside the lit part: to its right on the window's left side, else under it, over it; in the middle
        // with none — never up in the title bar
        card.Measure(new Size(card.Width, double.PositiveInfinity));
        var cs = card.DesiredSize;
        const double gap = 14, margin = 14, top = MainWindow.TitleBarHeight + 8;
        Point at;
        if (hole is not { } hr) at = new Point((size.Width - cs.Width) / 2, Math.Max(top, (size.Height - cs.Height) / 2));
        else if (hr.Right < size.Width * 0.4 && hr.Right + gap + cs.Width <= size.Width - margin)
            at = new Point(hr.Right + gap, Math.Clamp(hr.Y + hr.Height / 2 - cs.Height / 2, top, Math.Max(top, size.Height - cs.Height - margin)));
        else if (hr.Bottom + gap + cs.Height <= size.Height - margin)
            at = new Point(Math.Clamp(hr.X + hr.Width / 2 - cs.Width / 2, margin, Math.Max(margin, size.Width - cs.Width - margin)), hr.Bottom + gap);
        else if (hr.Y - gap - cs.Height >= top)
            at = new Point(Math.Clamp(hr.X + hr.Width / 2 - cs.Width / 2, margin, Math.Max(margin, size.Width - cs.Width - margin)), hr.Y - gap - cs.Height);
        else at = new Point((size.Width - cs.Width) / 2, hr.Y + hr.Height / 2 > size.Height / 2 ? top : size.Height - cs.Height - margin);
        // (a callout not placed yet is placed, never slid from nowhere)
        var placed = !double.IsNaN(Canvas.GetLeft(card)) && !double.IsNaN(Canvas.GetTop(card));
        if (placed && (step ? travel : animate && Motion.On))
        {
            Motion.To(card, Canvas.LeftProperty, at.X, Motion.Slow, Motion.Decelerate);
            Motion.To(card, Canvas.TopProperty, at.Y, Motion.Slow, Motion.Decelerate);
        }
        else
        {
            card.BeginAnimation(Canvas.LeftProperty, null);
            card.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(card, at.X);
            Canvas.SetTop(card, at.Y);
        }
        return travel && placed;
    }

    /// <summary>The ring faded in where a part is lit, and out (where it was) when none is (Motion.Normal).</summary>
    private void Ring(bool on)
    {
        if (on == ringOn) return;
        ringOn = on;
        if (on) ring.Visibility = Visibility.Visible;
        Motion.To(ring, OpacityProperty, on ? 1 : 0, Motion.Normal, Motion.Decelerate, 0, () => { if (!ringOn) ring.Visibility = Visibility.Collapsed; });
    }
}

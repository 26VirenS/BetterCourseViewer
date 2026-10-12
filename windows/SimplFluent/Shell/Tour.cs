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
public enum TourSpot { Pane, Account, Search, DashboardRow, GradesRow, ToolsRow, FirstCourse, GradeRing, GradeDetails, WhatIfSwitch, WhatIfScore, ToolPin, CounterNext, CounterPanel, Preview }

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
/// lit at a time, the window dimmed round it and the part ringed in the accent, and a callout beside it — its count,
/// what it is, what to do — with Next where the step waits for nothing. A step that asks for something (click Grades,
/// hover a ring, turn on what-if) waits for it and moves on once it is done. It comes once, after the app first shows
/// with nothing over it; the account menu's Take the tour runs it again. It ends on its last step, whose Done says it
/// was followed to the end (<see cref="Completed"/>: Simpl then asks, once, to be kept on the taskbar).
/// </summary>
public sealed class TourOverlay : Grid
{
    private sealed record Step(string Id, TourSpot? Spot, string Title, string Body, string? Doing = null, string Gesture = "click", Func<bool>? Done = null, string NextLabel = "Next");

    private readonly Engine.Engine engine;
    private readonly MainWindow window;
    private readonly Path veil = new();
    private readonly Border ring = new() { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10), IsHitTestVisible = false };
    private readonly Border card = new();
    private readonly Canvas layer = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private List<Step> steps = new();
    private int? index;
    private bool advancing;
    private DateTime since;
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
        veil.Fill = new SolidColorBrush(Color.FromArgb(Appearance.Dark ? (byte)128 : (byte)72, 0, 0, 0));
        veil.MouseLeftButtonDown += (_, e) => { Nudge(); e.Handled = true; };
        ring.SetResourceReference(Border.BorderBrushProperty, B.Accent);
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

    // ---- the steps (the Mac's: welcome.js's setup run, but the Simpl switch's) ----

    private List<Step> Steps()
    {
        var nav = engine.Nav;
        bool At(PlaceKind k) => nav.Current.Place.Kind == k && nav.Current.Path.Count == 0;
        bool Event(TourEvent e) => Tour.Events > seenEvents && Tour.LastEvent == e;
        var s = new List<Step>
        {
            new("report", TourSpot.Account, "Report a bug", "Found a bug or want something added? Tell us here — it’s in your account’s menu."),
            new("grades", TourSpot.GradesRow, "Grades", "All your grades in one place.", "Click Grades", Done: () => At(PlaceKind.Grades)),
            new("ring", TourSpot.GradeRing, "A quick breakdown", "Each ring shows what makes up the grade.", "Hover over a ring", "hover", () => Event(TourEvent.RingHover)),
            new("details", TourSpot.GradeDetails, "What if?", "See every grade, and try out scores.", "Click it", Done: () => nav.Current.Place is { Kind: PlaceKind.Section, Arg: "grades" }),
            new("whatif", TourSpot.WhatIfSwitch, "Try what-if scores", "See how new scores would change your grade.", "Turn on What-If Scores", Done: () => Event(TourEvent.WhatIfOn)),
            new("score", TourSpot.WhatIfScore, "Change a score", "Type any score. Nothing is saved.", "Change a score", "type", () => Event(TourEvent.WhatIfEdited)),
            new("real", TourSpot.WhatIfSwitch, "Back to the real scores", "Turn it off to go back to your real scores.", "Turn What-If off", Done: () => Event(TourEvent.WhatIfOff)),
        };
        if (engine.Courses.Count > 0)
            s.Add(new("courses", TourSpot.FirstCourse, "Your courses", "Your classes are listed here, each with its sections.", "Hover over a course", "hover", () => Event(TourEvent.CourseHover)));
        s.Add(new("tools", TourSpot.ToolsRow, "Tools and widgets", "Find a PDF Editor, File Converter, Calculators, Flashcards, Citation Generator & more.", "Click Tools", Done: () => At(PlaceKind.Tools)));
        s.Add(new("pin", TourSpot.ToolPin, "Pin the graphing calculator", "Pin it to keep it at the top, one click away.", "Click the graphing calculator’s pin", Done: () => Event(TourEvent.Pinned)));
        s.Add(new("dashboard", TourSpot.DashboardRow, "Back to the Dashboard", "Everything due, at a glance.", "Click Dashboard", Done: () => At(PlaceKind.Dashboard)));
        s.Add(new("cards", TourSpot.CounterNext, "The cards open", "Click a card to see what’s in it.", "Click Next 7 days", Done: () => Event(TourEvent.CounterOpened)));
        s.Add(new("look", TourSpot.CounterPanel, "A quick look", "Click anything to preview it here.", "Click an item", Done: () => WorkPreview.IsOpen || nav.Current.Path.Count > 0));
        s.Add(new("close", TourSpot.Preview, "Close it", "You’ll be right where you were.", "Close it", Done: () => !WorkPreview.IsOpen && nav.Current.Path.Count == 0));
        s.Add(new("search", TourSpot.Search, "Search everything", "Find anything in your courses. Type / for commands."));
        s.Add(new("end", null, "You’re all set", "Replay it any time from your account’s menu ▸ Take the tour.", NextLabel: "Done"));
        return s;
    }

    // ---- on and off ----

    public void Start(int at = 0)
    {
        WorkPreview.Dismiss();
        if (engine.Nav.Current.Place != Place.Dashboard || engine.Nav.Current.Path.Count > 0) engine.Go(Place.Dashboard);
        steps = Steps();
        Show(Math.Clamp(at, 0, steps.Count - 1));
        Visibility = Visibility.Visible;
        timer.Start();
        Motion.FadeIn(this, Motion.Medium);
    }

    private void Show(int i)
    {
        index = i;
        since = DateTime.UtcNow;
        seenEvents = Tour.Events;
        advancing = false;
        lastHole = null;
        BuildCard();
        Arrange(animate: false);
        Motion.Pop(card);
        Dispatcher.BeginInvoke(() => { if (steps[i].Done == null) Focus(); }, DispatcherPriority.Input);
        Shot.Moved();
    }

    private void Finish()
    {
        index = null;
        timer.Stop();
        Prefs.Set("tour:done", true);
        Motion.FadeOut(this, () => Visibility = Visibility.Collapsed, Motion.Normal);
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

    // ---- each beat: is the step's thing done, and where is its part now ----

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
        if (index is not { } i || steps[i].Done != null) return;
        if (e.Key is Key.Right or Key.Enter) { Next(); e.Handled = true; }
        else if (e.Key == Key.Left) { Back(); e.Handled = true; }
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
        var count = K.Text($"{i + 1} of {steps.Count}", Ty.Caption, B.Text2);
        var title = K.Wrapped(s.Title, Ty.Subtitle);
        var body = K.Wrapped(s.Body, Ty.Body, B.Text2);
        var v = K.V(8, count, title, body);
        var waiting = s.Done != null;
        if (s.Doing != null)
        {
            var sym = s.Gesture switch { "hover" => SymbolRegular.CursorHover24, "type" => SymbolRegular.Keyboard24, _ => SymbolRegular.CursorClick24 };
            var doing = K.Text(s.Doing, Ty.BodyStrong, B.AccentText);
            v.Children.Add(K.H(8, K.Icon(sym, 16, B.AccentText), doing).Margin(0, 4, 0, 0));
        }
        v.Children.Add(K.Bar((i + 1.0) / steps.Count, Appearance.Accent, 3).Margin(0, 6, 0, 2));
        var buttons = K.H(8);
        if (i > 0) buttons.Children.Add(K.Button("Back", Back));
        // (a step that waits shows Next only once it has waited a while, in case the thing it asks for cannot be done)
        var next = K.Button(s.NextLabel, Next, accent: true);
        next.Visibility = waiting ? Visibility.Collapsed : Visibility.Visible;
        buttons.Children.Add(next);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        v.Children.Add(buttons);
        card.Tag = next;
        card.Child = v;
        card.Width = 320;
        card.Padding = new Thickness(18, 14, 18, 16);
        card.CornerRadius = new CornerRadius(8);
        card.Effect = K.Shadow(36, 10, 0.3);
        card.Bg(B.Flyout).Stroke(B.FlyoutStroke);
    }

    private void Arrange(bool animate)
    {
        if (index is not { } i) return;
        var s = steps[i];
        var size = new Size(ActualWidth, ActualHeight);
        if (size.Width < 10) return;
        // (a step waiting a while: Next appears, in case what it asks cannot be done here)
        if (card.Tag is UIElement nx && s.Done != null && (DateTime.UtcNow - since).TotalSeconds > 6) nx.Visibility = Visibility.Visible;
        Rect? hole = s.Spot is { } spot ? Tour.Find(spot, this) : null;
        if (hole is { } h) { h.Inflate(6, 6); hole = h; }
        if (hole == lastHole && card.ActualHeight > 0 && animate) return;
        lastHole = hole;
        // the dim, less the light
        var all = new RectangleGeometry(new Rect(size));
        veil.Data = hole is { } hh ? new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(hh, 10, 10)) : all;
        if (hole is { } r)
        {
            ring.Visibility = Visibility.Visible;
            ring.Width = r.Width;
            ring.Height = r.Height;
            Canvas.SetLeft(ring, r.X);
            Canvas.SetTop(ring, r.Y);
        }
        else ring.Visibility = Visibility.Collapsed;
        // the callout beside the lit part: to its right on the window's left side, else under it, over it; in the middle with none
        card.Measure(new Size(320, double.PositiveInfinity));
        var cs = card.DesiredSize;
        const double gap = 14, margin = 14;
        Point at;
        if (hole is not { } hr) at = new Point((size.Width - cs.Width) / 2, (size.Height - cs.Height) / 2);
        else if (hr.Right < size.Width * 0.4 && hr.Right + gap + cs.Width <= size.Width - margin)
            at = new Point(hr.Right + gap, Math.Clamp(hr.Y + hr.Height / 2 - cs.Height / 2, margin, Math.Max(margin, size.Height - cs.Height - margin)));
        else if (hr.Bottom + gap + cs.Height <= size.Height - margin)
            at = new Point(Math.Clamp(hr.X + hr.Width / 2 - cs.Width / 2, margin, Math.Max(margin, size.Width - cs.Width - margin)), hr.Bottom + gap);
        else if (hr.Y - gap - cs.Height >= margin)
            at = new Point(Math.Clamp(hr.X + hr.Width / 2 - cs.Width / 2, margin, Math.Max(margin, size.Width - cs.Width - margin)), hr.Y - gap - cs.Height);
        else at = new Point((size.Width - cs.Width) / 2, hr.Y + hr.Height / 2 > size.Height / 2 ? margin : size.Height - cs.Height - margin);
        if (animate && Motion.On)
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
    }
}

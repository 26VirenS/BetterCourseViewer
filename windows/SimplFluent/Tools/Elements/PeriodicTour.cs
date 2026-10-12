using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

/// <summary>What a tour step may wait for that the tool's own state does not show (the Mac's PTTourEvent): the element
/// page's Orbitals; the Lewis maker's preset chosen, Draw, a resonance structure; the molecule builder's model turned.</summary>
public enum PTTourEvent { Orbitals, Preset, Draw, Resonance, Turned }

/// <summary>What a step asks for, done.</summary>
internal enum PTTourAct { Hover, Colour, Open, Typed, Page, Event }

/// <summary>
/// A step of the Periodic Table's tour (the Mac's PTTourStep), shown the first time the whole table opens and from its
/// Tour button. As the app's own tour, each step asks for one thing — hover an element, pick a colour view, open an
/// element, see its orbitals, go to the Lewis builder, choose a preset, draw, look at a resonance structure, go to the 3D
/// builder, turn the model, search — and moves on by itself once it is done; a Next shows only if a step is left undone a
/// while.
/// </summary>
internal sealed record PTTourStep(string Title, string Text, string Doing, string Gesture, PTTourAct Act, string Target,
    PTPage? Page = null, PTTourEvent? Event = null, int? Element = null, string? Lewis = null, string? Molecule = null)
{
    public static readonly PTTourStep[] All =
    {
        new("The periodic table", "Point at any element to see its card up top.", "Hover an element", "hover", PTTourAct.Hover, "ptGrid"),
        new("Colour by a property", "Each view colours the table; arrows show its trend.", "Click Electronegativity", "click", PTTourAct.Colour, "ptColours"),
        new("Every element, in depth", "Its page has a 3D atom, its electrons and every fact.", "Click an element", "click", PTTourAct.Open, "ptGrid"),
        new("Orbitals", "See the shapes its electrons fill. Drag to turn it.", "Click Orbitals", "click", PTTourAct.Event, "ptModel", Event: PTTourEvent.Orbitals, Element: 6),
        new("Lewis structures", "Draw any molecule’s bonds and lone pairs.", "Click Lewis structures", "click", PTTourAct.Page, "ptPageLewis", Page: PTPage.Lewis),
        new("Start from a preset", "Or pick a centre atom and what surrounds it.", "Choose a preset", "click", PTTourAct.Event, "lewisPreset", Event: PTTourEvent.Preset),
        new("Draw your own", "Place atoms, drag to bond, click a bond to change it.", "Click Draw", "click", PTTourAct.Event, "lewisWay", Event: PTTourEvent.Draw),
        new("Resonance", "Equivalent structures sit side by side.", "Click a structure", "click", PTTourAct.Event, "lewisResonance", Event: PTTourEvent.Resonance, Lewis: "NO₃⁻"),
        new("Build it in 3D", "Open in 3D sends any Lewis structure here too.", "Click Molecule builder", "click", PTTourAct.Page, "ptPageMolecules", Page: PTPage.Molecules),
        new("Turn it", "Scroll to zoom; click an atom for its shape.", "Drag the model", "drag", PTTourAct.Event, "molViewer", Event: PTTourEvent.Turned, Molecule: "Ethanol"),
        new("Find anything", "A symbol, name or number; Return opens it.", "Search for an element", "type", PTTourAct.Typed, "ptSearch"),
    };

    /// <summary>The steps this copy of the app can show: the Lewis maker's and the molecule builder's only once their pages
    /// are here (Tools/Chemistry).</summary>
    public static List<PTTourStep> Available() => All.Where(s =>
    {
        var lewis = s.Page == PTPage.Lewis || s.Target.StartsWith("lewis", StringComparison.Ordinal);
        var molecules = s.Page == PTPage.Molecules || s.Target == "molViewer";
        return (!lewis || PTChemistry.LewisView != null) && (!molecules || PTChemistry.MoleculeView != null);
    }).ToList();
}

/// <summary>
/// The tour's word with the parts that keep their own state (the Mac's PTTour and PTTourFrames): where each control it
/// can light is (<see cref="TourTarget{T}"/>, for the table's views and Tools/Chemistry's), what was just done
/// (<see cref="Did"/>), and a Lewis structure handed to the Lewis maker for a step.
/// </summary>
public static class PTTour
{
    private static readonly Dictionary<string, List<WeakReference<FrameworkElement>>> Targets = new();

    /// <summary>Whether the tour is on (nothing done is told it otherwise).</summary>
    internal static bool Active;
    internal static PTTourEvent? Event;
    internal static event Action? Changed;

    /// <summary>Something a step may be waiting for, done in the tool (the Lewis maker's preset, Draw, a resonance
    /// structure; the molecule builder's model turned).</summary>
    public static void Did(PTTourEvent e)
    {
        if (!Active) return;
        Event = e;
        Changed?.Invoke();
    }

    internal static void ClearEvent() => Event = null;

    /// <summary>A control the tour can light, by name ("lewisPreset", "lewisWay", "lewisResonance", "molViewer" in
    /// Tools/Chemistry); the first of a name on screen — the topmost, then the leftmost — is lit.</summary>
    public static T TourTarget<T>(this T e, string id) where T : FrameworkElement
    {
        if (!Targets.TryGetValue(id, out var list)) Targets[id] = list = new();
        list.RemoveAll(w => !w.TryGetTarget(out var x) || ReferenceEquals(x, e));
        list.Add(new WeakReference<FrameworkElement>(e));
        return e;
    }

    /// <summary>Where a control is in an element's coordinates, cut to what its scrolling list shows; null when none is on
    /// screen.</summary>
    internal static Rect? Find(string id, FrameworkElement over)
    {
        if (!Targets.TryGetValue(id, out var list)) return null;
        Rect? best = null;
        foreach (var w in list.ToList())
        {
            if (!w.TryGetTarget(out var e)) { list.Remove(w); continue; }
            if (!e.IsVisible || e.ActualWidth < 2 || PresentationSource.FromVisual(e) == null || PresentationSource.FromVisual(over) != PresentationSource.FromVisual(e)) continue;
            Rect r;
            try { r = new Rect(e.TranslatePoint(new Point(0, 0), over), new Size(e.ActualWidth, e.ActualHeight)); } catch { continue; }
            for (DependencyObject? p = VisualTreeHelper.GetParent(e); p != null; p = VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer sv)
                {
                    r.Intersect(new Rect(sv.TranslatePoint(new Point(0, 0), over), new Size(sv.ActualWidth, sv.ActualHeight)));
                    break;
                }
            if (r.IsEmpty || r.Width < 2 || r.Height < 2) continue;
            if (best is { } b && (b.Y < r.Y - 1 || (Math.Abs(b.Y - r.Y) <= 1 && b.X <= r.X))) continue;
            best = r;
        }
        return best;
    }

    /// <summary>A structure for the Lewis maker to show for a step (the Mac's showLewis).</summary>
    internal static void ShowLewis(string name)
    {
        try { PTChemistry.OpenLewis?.Invoke(name); } catch (Exception e) { Log.Write($"ptable tour lewis: {e.Message}"); }
    }
}

/// <summary>
/// The guided tour over the tool (the Mac's PTTourOverlay and PTTourCard), in the app tour's look — Windows 11's
/// TeachingTip: the window dimmed but for the control lit, ringed in the accent, and the step's card beside it (under it
/// when there is room, else over it): where it is ("3 of 11"), what it is, what to do with its gesture pulsing, a bar for
/// how far — ticked for a moment once done, a Next only when the step has been left undone a while. Presses go through the
/// dim to the tool, so what the step asks can be done.
/// </summary>
internal sealed class PTTourOverlay
{
    private const double CardWidth = 300;
    private readonly FrameworkElement tool;
    private readonly Path veil = new() { IsHitTestVisible = false };
    // (the light and its ring are one shape, so one move carries both: the dim is the tool's window less it)
    private readonly RectangleGeometry whole = new(), light = new() { RadiusX = 10, RadiusY = 10 };
    private readonly CombinedGeometry dim;
    private readonly Path ring = new() { StrokeThickness = 2.5, IsHitTestVisible = false, Opacity = 0, Visibility = Visibility.Collapsed };
    private bool ringOn;
    private readonly Border card = new() { Width = CardWidth, Padding = new Thickness(18, 14, 18, 16), CornerRadius = new CornerRadius(8) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Canvas? layer;
    private PTTourStep? step;
    private int index = -1;
    private Rect? lastHole;
    private Size lastSize;

    public PTTourOverlay(FrameworkElement tool)
    {
        this.tool = tool;
        dim = new CombinedGeometry(GeometryCombineMode.Exclude, whole, light);
        veil.Data = whole;
        ring.Data = light;
        ring.SetResourceReference(Shape.StrokeProperty, B.Accent);
        ring.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6, Color = Appearance.Accent };
        card.Effect = K.Shadow(36, 10, 0.3);
        card.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        card.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, B.Text);
        timer.Tick += (_, _) => Arrange(true);
    }

    public void Show(int i, int count, PTTourStep s, bool stuck, bool done, Action next)
    {
        layer ??= PTLayer.Of(tool);
        if (layer == null) return;
        veil.Fill = new SolidColorBrush(Color.FromArgb(Appearance.Dark ? (byte)128 : (byte)72, 0, 0, 0));
        var fresh = veil.Parent == null;
        if (fresh)
        {
            layer.Children.Add(veil);
            layer.Children.Add(ring);
            layer.Children.Add(card);
            Motion.FadeIn(veil, Motion.Medium);
        }
        var newStep = i != index;
        index = i;
        step = s;
        card.Child = Card(i, count, s, stuck, done, next);
        // (a new step: from a control lit to the next, the card goes with the light; else it pops in place)
        if (!Arrange(animate: !fresh, force: true, newStep: newStep) && newStep) Motion.Pop(card);
        timer.Start();
        Shot.Moved();
    }

    public void Hide()
    {
        timer.Stop();
        foreach (var e in new UIElement[] { veil, ring, card }) if (VisualTreeHelper.GetParent(e) is Panel p) p.Children.Remove(e);
        index = -1;
        lastHole = null;
        ringOn = false;
        ring.BeginAnimation(UIElement.OpacityProperty, null);
        ring.Opacity = 0;
        ring.Visibility = Visibility.Collapsed;
    }

    private static FrameworkElement Card(int i, int count, PTTourStep s, bool stuck, bool done, Action next)
    {
        var v = K.V(8, K.Text($"{i + 1} of {count}", Ty.Caption, B.Text2).Tabular(), K.Wrapped(s.Title, Ty.Subtitle), K.Wrapped(s.Text, Ty.Body, B.Text2));
        FrameworkElement doing;
        if (done)
        {
            // (done: a green tick in its place for a moment)
            var green = K.Tone("green");
            var t = K.Text(i == count - 1 ? "All done" : "Nice", Ty.BodyStrong);
            t.Foreground = new SolidColorBrush(green);
            doing = K.H(7, K.Icon(SymbolRegular.CheckmarkCircle24, 16, green, filled: true), t);
            Motion.Pop(doing);
        }
        else
        {
            var sym = s.Gesture switch { "hover" => SymbolRegular.CursorHover24, "drag" => SymbolRegular.HandDraw24, "type" => SymbolRegular.Keyboard24, _ => SymbolRegular.CursorClick24 };
            var icon = K.Icon(sym, 16, B.AccentText);
            if (Motion.On)
                icon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.35, Motion.Ms(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Motion.Standard });
            doing = K.H(7, icon, K.Text(s.Doing, Ty.BodyStrong, B.AccentText));
        }
        doing.Margin = new Thickness(0, 4, 0, 0);
        v.Children.Add(doing);
        v.Children.Add(K.Bar((i + (done ? 1.0 : 0.0)) / count, Appearance.Accent, 3).Margin(0, 6, 0, 2));
        if (stuck)
        {
            var n = K.Button(i == count - 1 ? "Done" : "Next", next, accent: true);
            n.HorizontalAlignment = HorizontalAlignment.Right;
            v.Children.Add(n);
        }
        System.Windows.Automation.AutomationProperties.SetName(v, $"Tour, step {i + 1} of {count}: {s.Title}. {s.Doing}");
        return v;
    }

    /// <summary>The dim's hole round the control lit, its ring, and the card under it when there is room, else over it (none
    /// lit yet: the tool dimmed whole, the card at its middle). True when the light and the card travelled together from
    /// the control lit before (a new step's card then goes with them rather than popping).</summary>
    private bool Arrange(bool animate, bool force = false, bool newStep = false)
    {
        if (layer == null || step == null || card.Parent == null) return false;
        var size = new Size(layer.ActualWidth, layer.ActualHeight);
        if (size.Width < 10) return false;
        Rect hole;
        if (PTTour.Find(step.Target, layer) is { } found) { found.Inflate(8, 8); hole = found; }
        else
        {
            Point mid;
            try { mid = tool.TranslatePoint(new Point(tool.ActualWidth / 2, tool.ActualHeight / 2), layer); } catch { mid = new Point(size.Width / 2, size.Height / 2); }
            hole = new Rect(mid.X - 1, mid.Y - 1, 2, 2);
        }
        if (hole == lastHole && size == lastSize && !force) return false;
        var from = lastHole is { Width: > 4 } was ? was : (Rect?)null;
        lastHole = hole;
        lastSize = size;
        whole.Rect = new Rect(size);
        // from one control to the next the light glides as the card does (Motion.Slow); with none lit on one side, the dim
        // changes at once and the ring fades
        var lit = hole.Width > 4;
        var travel = animate && Motion.On && lit && from != null;
        if (lit)
        {
            if (!travel) { light.BeginAnimation(RectangleGeometry.RectProperty, null); light.Rect = hole; }
            else if (hole != from) light.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(hole, Motion.Ms(Motion.Slow)) { EasingFunction = Motion.Decelerate });
            veil.Data = dim;
        }
        else veil.Data = whole;
        Ring(lit);
        card.Measure(new Size(CardWidth, double.PositiveInfinity));
        var h = card.DesiredSize.Height;
        var below = hole.Bottom + h + 16 < size.Height || hole.Top - h - 16 < 0;
        var x = Math.Min(Math.Max(hole.X + hole.Width / 2 - CardWidth / 2, 16), Math.Max(16, size.Width - CardWidth - 16));
        var y = below ? Math.Min(hole.Bottom + 10, size.Height - h - 8) : Math.Max(hole.Top - h - 10, 8);
        // (a card not placed yet is placed, never slid from nowhere)
        var placed = !double.IsNaN(Canvas.GetLeft(card)) && !double.IsNaN(Canvas.GetTop(card));
        if (placed && (newStep ? travel : animate && Motion.On))
        {
            Motion.To(card, Canvas.LeftProperty, x, Motion.Slow, Motion.Decelerate);
            Motion.To(card, Canvas.TopProperty, y, Motion.Slow, Motion.Decelerate);
        }
        else
        {
            card.BeginAnimation(Canvas.LeftProperty, null);
            card.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
        }
        return travel && placed;
    }

    /// <summary>The ring faded in round a control lit, and out (where it was) when none is (Motion.Normal).</summary>
    private void Ring(bool on)
    {
        if (on == ringOn) return;
        ringOn = on;
        if (on) ring.Visibility = Visibility.Visible;
        Motion.To(ring, UIElement.OpacityProperty, on ? 1 : 0, Motion.Normal, Motion.Decelerate, 0, () => { if (!ringOn) ring.Visibility = Visibility.Collapsed; });
    }
}

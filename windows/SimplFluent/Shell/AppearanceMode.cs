using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Shell;

// (0.3.31, the Mac's Shell/AppearanceMode.swift) Appearance, from the account menu (Settings > Appearance is gone): the
// Dashboard shown, the whole window darkened but for the navigation pane and the Dashboard's cards — each of those with a
// photo button on it — and a bar risen from the foot of the window, in its middle, for the theme's colours and the
// courses' colours. Done, or Escape, puts it away.
// (0.3.61, the web's 2.99.108 and the Mac's 1.3.166) A photo uploaded opens where it went: its place the only one lit,
// outlined, the photo dragged on it to move it and the wheel to zoom it, a tip over it and its settings round it (under a
// card, over it where there is no room under; beside the pane) — the zoom, Ink (a photo of one's own), Reset, Replace…,
// Remove and Done. The bar at the foot steps aside meanwhile. Done or Escape ends it, the photo kept as it was left.

/// <summary>Appearance on or off, and the places that take a photo (the pane and each Dashboard card), as they say where
/// they are.</summary>
public static class AppearanceMode
{
    private static readonly Dictionary<string, WeakReference<FrameworkElement>> Places = new();

    /// <summary>A place that takes a photo: "side" (the navigation pane), or a Dashboard card's key.</summary>
    public static T Place<T>(this T e, string slot) where T : FrameworkElement
    {
        Places[slot] = new WeakReference<FrameworkElement>(e);
        return e;
    }

    /// <summary>Where each place is in an element's coordinates, the ones on screen.</summary>
    internal static IEnumerable<(string Slot, Rect At)> Where(FrameworkElement over)
    {
        var source = PresentationSource.FromVisual(over);
        foreach (var (slot, w) in Places.ToList())
        {
            if (!w.TryGetTarget(out var e) || !e.IsVisible || e.ActualWidth < 2 || source == null || PresentationSource.FromVisual(e) != source) continue;
            Rect r;
            try { r = new Rect(e.TranslatePoint(new Point(0, 0), over), new Size(e.ActualWidth, e.ActualHeight)); } catch { continue; }
            // (cut to what its scrolling list shows)
            for (DependencyObject? p = VisualTreeHelper.GetParent(e); p != null; p = VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer sv)
                {
                    r.Intersect(new Rect(sv.TranslatePoint(new Point(0, 0), over), new Size(sv.ActualWidth, sv.ActualHeight)));
                    break;
                }
            if (r.IsEmpty || r.Width < 2 || r.Height < 2) continue;
            yield return (slot, r);
        }
    }
}

/// <summary>
/// Over the whole window while Appearance is on: everything darkened but the navigation pane and the Dashboard's cards,
/// a photo button on each of those, and the bar at the foot. A press anywhere else does nothing; Done or Escape ends it.
/// </summary>
public sealed class AppearanceOverlay : Grid
{
    private readonly Engine.Engine engine;
    private readonly MainWindow window;
    private readonly Path veil = new();
    private readonly RectangleGeometry whole = new();
    private readonly GeometryGroup holes = new() { FillRule = FillRule.Nonzero };
    private readonly Canvas badges = new();
    private readonly Dictionary<string, Button> badgeOf = new();
    private readonly Border bar = new();
    private readonly ContentControl barBody = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private string tab = Launch.Get("appearance-tab") == "courses" ? "courses" : "theme";

    // (0.3.61) the photo in its place: the place edited, its outline (taking the drag and the wheel), its tip and its settings
    private string? editing;
    private Rect? editAt;
    private readonly Canvas editLayer = new() { Visibility = Visibility.Collapsed };
    private readonly Border halo = new() { IsHitTestVisible = false, BorderThickness = new Thickness(6) };
    private readonly Border ring = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(2), Cursor = Cursors.SizeAll, Focusable = true };
    private readonly Border tip = new() { IsHitTestVisible = false, CornerRadius = new CornerRadius(13), Padding = new Thickness(11, 4, 12, 5) };
    private readonly Border editBar = new() { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10) };
    private (double X, double Y) pan;
    private double zoom = 1;
    private Point? dragFrom;
    private (double X, double Y) panFrom;
    private Slider? zoomSlider;
    private Wpf.Ui.Controls.Button? resetButton;
    /// <summary>What the settings were drawn for (the place, a photo of one's own, its Ink off): drawn again only when that
    /// changes — else only the zoom and Reset follow, the slider kept under the pointer that holds it.</summary>
    private (string Slot, bool Own, bool Plain)? paintedFor;
    private bool syncing;
    /// <summary>The wheel, the slider and the arrow keys: the picture kept once they rest (the web's 260 ms).</summary>
    private readonly DispatcherTimer rest = new() { Interval = TimeSpan.FromMilliseconds(260) };

    public bool On { get; private set; }

    public AppearanceOverlay(Engine.Engine engine, MainWindow window)
    {
        this.engine = engine;
        this.window = window;
        Visibility = Visibility.Collapsed;
        // (the window less the lit places; a press on the lit places is held too — only the buttons and the bar take one)
        Background = Brushes.Transparent;
        veil.Data = new CombinedGeometry(GeometryCombineMode.Exclude, whole, holes);
        veil.IsHitTestVisible = false;
        var layer = new Canvas();
        layer.Children.Add(veil);
        Children.Add(layer);
        Children.Add(badges);
        Children.Add(editLayer);
        BuildEditor();
        bar.HorizontalAlignment = HorizontalAlignment.Center;
        bar.VerticalAlignment = VerticalAlignment.Bottom;
        bar.Margin = new Thickness(24, 0, 24, 22);
        bar.MaxWidth = 780;
        bar.CornerRadius = new CornerRadius(12);
        bar.BorderThickness = new Thickness(1);
        bar.Padding = new Thickness(16, 14, 16, 14);
        bar.SetResourceReference(Border.BackgroundProperty, B.Flyout);
        bar.SetResourceReference(Border.BorderBrushProperty, B.FlyoutStroke);
        bar.Effect = K.Shadow(32, 10, 0.3);
        bar.Child = barBody;
        Children.Add(bar);
        timer.Tick += (_, _) => Tick();
        PreviewKeyDown += (_, e) =>
        {
            if (!On) return;
            if (editing != null) { EditKey(e); return; }
            if (e.Key == Key.Escape) { End(); e.Handled = true; }
        };
        Focusable = true;
        Appearance.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            if (!On) return;
            veil.Fill = Dim();
            Build();
            if (editing is { } slot)
            {
                // (the picture as kept, unless it is being dragged or zoomed still)
                if (dragFrom == null && !rest.IsEnabled) { pan = Appearance.Pan(slot); zoom = Appearance.Zoom(slot); }
                if (paintedFor == Painted(slot)) SyncEdit(); else PaintEdit();
            }
            Tick(force: true);
        });
        engine.PropertyChanged += (_, e) => { if (On && e.PropertyName == nameof(Engine.Engine.Courses) && tab == "courses") Dispatcher.BeginInvoke(Build); };
    }

    private static Brush Dim() => K.Brush(Color.FromArgb(Appearance.Dark ? (byte)150 : (byte)110, 0, 0, 0));

    /// <summary>On, over the Dashboard (where its cards are).</summary>
    public void Begin()
    {
        if (On) return;
        if (engine.Nav.Current.Place != Place.Dashboard || engine.Nav.Current.Path.Count > 0) engine.Go(Place.Dashboard);
        foreach (var p in FloatPopup.Over(window)) p.Hide(); // (the popups floating over the window step aside)
        On = true;
        veil.Fill = Dim();
        Build();
        Visibility = Visibility.Visible;
        UpdateLayout();
        Tick(force: true);
        timer.Start();
        Motion.FadeIn(this, Motion.Medium);
        Motion.Pop(bar);
        Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
        Log.Write("appearance: on");
        Shot.Moved();
        // (the screenshot suite: --photo-edit today opens that place's photo in its place)
        if (Launch.Take("", "photo-edit") is { Length: > 0 } slot) Dispatcher.BeginInvoke(() => Edit(slot), DispatcherPriority.ApplicationIdle);
    }

    public void End()
    {
        if (!On) return;
        EndEdit(quiet: true);
        On = false;
        timer.Stop();
        Visibility = Visibility.Collapsed;
        badges.Children.Clear();
        badgeOf.Clear();
        foreach (var p in FloatPopup.Over(window)) p.Show();
        Log.Write("appearance: off");
    }

    // ---- the lit places and their photo buttons ----

    private Rect? lastPane, lastCards;

    private void Tick(bool force = false)
    {
        if (!On) return;
        whole.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
        if (editing != null)
        {
            TickEdit(force);
            return;
        }
        var pane = Tour.Find(TourSpot.Pane, this);
        var cards = Tour.Find(TourSpot.Counters, this) is { } c ? Rect.Inflate(c, 8, 8) : (Rect?)null;
        if (force || pane != lastPane || cards != lastCards)
        {
            lastPane = pane;
            lastCards = cards;
            holes.Children.Clear();
            if (pane is { } p) holes.Children.Add(new RectangleGeometry(p));
            if (cards is { } k) holes.Children.Add(new RectangleGeometry(k, 10, 10));
        }
        var seen = new HashSet<string>();
        foreach (var (slot, at) in AppearanceMode.Where(this))
        {
            if (slot != "side" && cards == null) continue;
            seen.Add(slot);
            if (!badgeOf.TryGetValue(slot, out var b))
            {
                b = Badge(slot);
                badgeOf[slot] = b;
                badges.Children.Add(b);
                Motion.Pop(b);
            }
            Canvas.SetLeft(b, at.X + at.Width / 2 - 21);
            Canvas.SetTop(b, at.Y + at.Height / 2 - 21);
        }
        foreach (var gone in badgeOf.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            badges.Children.Remove(badgeOf[gone]);
            badgeOf.Remove(gone);
        }
    }

    /// <summary>A place's photo button: a photo of your own, a drawing, zoomed or put back, or taken away.</summary>
    private Button Badge(string slot)
    {
        var name = Appearance.Slots.FirstOrDefault(s => s.Key == slot).Name ?? slot;
        var face = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), BorderThickness = new Thickness(2), BorderBrush = K.Brush(Color.FromArgb(220, 255, 255, 255)), Effect = K.Shadow(10, 3, 0.35) };
        face.SetResourceReference(Border.BackgroundProperty, B.Accent);
        face.Child = K.Icon(Appearance.Photo(slot) == null ? SymbolRegular.ImageAdd24 : SymbolRegular.Image24, 20, Colors.White);
        var b = K.Subtle(face, null, new Thickness(0), 21);
        b.ToolTip = slot == "side" ? "Navigation pane photo" : $"{name} photo";
        System.Windows.Automation.AutomationProperties.SetName(b, b.ToolTip.ToString()!);
        b.Click += (_, e) => { e.Handled = true; PhotoMenu(slot, b); };
        return b;
    }

    private void PhotoMenu(string slot, FrameworkElement at)
    {
        var menu = new ContextMenu { PlacementTarget = at, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = 220 };
        MenuItem Item(string text, SymbolRegular icon, Action act)
        {
            var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = icon } };
            mi.Click += (_, _) => act();
            return mi;
        }
        menu.Items.Add(Item("Upload photo…", SymbolRegular.FolderOpen24, () => Choose(slot)));
        var drawing = new MenuItem { Header = "Drawing", Icon = new SymbolIcon { Symbol = SymbolRegular.PaintBrush24 } };
        foreach (var r in Appearance.ReadyThemes.Where(r => r.Scene != null))
        {
            var scene = r.Scene!;
            var mi = new MenuItem { Header = scene };
            mi.Click += (_, _) => { Scenes.Forget(); Appearance.SetScene(slot, scene); };
            drawing.Items.Add(mi);
        }
        menu.Items.Add(drawing);
        if (Scenes.Of(slot) != null)
        {
            menu.Items.Add(new Separator());
            // (0.3.61) moved and zoomed in its place, its settings round it
            menu.Items.Add(Item("Move and zoom…", SymbolRegular.ArrowMove24, () => Edit(slot)));
            // (0.3.59) a photo of one's own: inked, or (off) shown as it is, the words on it white-bordered
            if (Appearance.PhotoKind(slot) == "photo" && Appearance.Photo(slot) != null)
            {
                var ink = new MenuItem { Header = "Ink photo", IsCheckable = true, IsChecked = !Appearance.Plain(slot), Icon = new SymbolIcon { Symbol = SymbolRegular.Image24 } };
                ink.Click += (_, _) => Appearance.SetPlain(slot, !ink.IsChecked);
                menu.Items.Add(ink);
            }
            menu.Items.Add(Item("Remove photo", SymbolRegular.Delete24, () => { Appearance.RemovePhoto(slot); Appearance.SetKind(slot, "none"); Scenes.Forget(); }));
        }
        menu.IsOpen = true;
    }

    private void Choose(string slot)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.heic;*.webp|All files|*.*" };
        if (dlg.ShowDialog(window) != true) return;
        Scenes.Forget();
        if (!Appearance.SetPhoto(slot, dlg.FileName)) { Log.Write("appearance: the picture could not be read"); return; }
        // (0.3.61) and it opens where it went, to be placed
        Edit(slot);
    }

    // ---- (0.3.61) the photo in its place ----

    private void BuildEditor()
    {
        halo.SetResourceReference(Border.BorderBrushProperty, B.Accent);
        halo.Opacity = 0.22;
        ring.SetResourceReference(Border.BorderBrushProperty, B.Accent);
        ring.FocusVisualStyle = null;
        tip.Background = K.Brush(Color.FromArgb(220, 28, 28, 28));
        tip.Effect = K.Shadow(14, 3, 0.22);
        var words = K.Text("Drag to move · scroll to zoom", Ty.Caption);
        words.Foreground = Brushes.White;
        words.FontWeight = FontWeights.SemiBold;
        tip.Child = K.H(6, K.Icon(SymbolRegular.ArrowMove24, 14, Colors.White), words);
        editBar.SetResourceReference(Border.BackgroundProperty, B.Flyout);
        editBar.SetResourceReference(Border.BorderBrushProperty, B.FlyoutStroke);
        editBar.Effect = K.Shadow(32, 10, 0.3);
        editLayer.Children.Add(halo);
        editLayer.Children.Add(ring);
        editLayer.Children.Add(tip);
        editLayer.Children.Add(editBar);

        // the photo follows the pointer, a share of the place for each step of it (Scenes.Preview), kept as it is let go
        ring.MouseLeftButtonDown += (_, e) =>
        {
            if (editing == null) return;
            e.Handled = true;
            ring.Focus();
            dragFrom = e.GetPosition(this);
            panFrom = pan;
            ring.CaptureMouse();
        };
        ring.MouseMove += (_, e) =>
        {
            if (dragFrom is not { } from || editAt is not { } at || editing is not { } slot) return;
            var p = e.GetPosition(this);
            pan = (Math.Clamp(panFrom.X + (p.X - from.X) / Math.Max(1, at.Width), -0.5, 0.5),
                   Math.Clamp(panFrom.Y + (p.Y - from.Y) / Math.Max(1, at.Height), -0.5, 0.5));
            Scenes.Preview(slot, zoom, pan);
        };
        ring.MouseLeftButtonUp += (_, e) =>
        {
            if (dragFrom == null) return;
            e.Handled = true;
            dragFrom = null;
            ring.ReleaseMouseCapture();
            Keep();
        };
        ring.LostMouseCapture += (_, _) =>
        {
            if (dragFrom == null) return;
            dragFrom = null;
            Keep();
        };
        ring.MouseWheel += (_, e) =>
        {
            e.Handled = true;
            ZoomTo(zoom * Math.Pow(1.1, e.Delta / 120.0));
        };
        rest.Tick += (_, _) => Keep();
    }

    /// <summary>A place's photo opened in its place: only it lit, outlined; its settings round it.</summary>
    private void Edit(string slot)
    {
        if (!On || !Appearance.Slots.Any(s => s.Key == slot)) return;
        if (editing != null && editing != slot) EndEdit(quiet: true);
        var fresh = editing == null;
        editing = slot;
        editAt = null;
        pan = Appearance.Pan(slot);
        zoom = Appearance.Zoom(slot);
        PaintEdit();
        badges.Visibility = Visibility.Collapsed;
        bar.Visibility = Visibility.Hidden; // (the bar at the foot steps aside)
        editLayer.Visibility = Visibility.Visible;
        UpdateLayout();
        Tick(force: true);
        if (fresh)
        {
            Motion.Pop(editBar);
            Motion.FadeIn(tip, Motion.Medium);
            Motion.FadeIn(ring, Motion.Fast);
        }
        Dispatcher.BeginInvoke(() => ring.Focus(), DispatcherPriority.Input);
        Log.Write($"appearance: {slot} photo in its place");
        Shot.Moved();
    }

    /// <summary>The place let go: the photo kept as it was left, the other places and the bar back.</summary>
    private void EndEdit(bool quiet = false)
    {
        if (editing == null) return;
        if (rest.IsEnabled || dragFrom != null) { dragFrom = null; Keep(); }
        editing = null;
        editAt = null;
        paintedFor = null;
        editLayer.Visibility = Visibility.Collapsed;
        badges.Visibility = Visibility.Visible;
        bar.Visibility = Visibility.Visible;
        if (quiet) return;
        Motion.Pop(bar);
        Tick(force: true);
        Focus();
    }

    /// <summary>The picture kept where it is and as large (once: the look is worn again for it).</summary>
    private void Keep()
    {
        rest.Stop();
        if (editing is not { } slot) return;
        var (x, y) = Appearance.Pan(slot);
        if (Math.Abs(x - pan.X) < 0.0005 && Math.Abs(y - pan.Y) < 0.0005 && Math.Abs(Appearance.Zoom(slot) - zoom) < 0.0005) return;
        Appearance.SetPlace(slot, pan.X, pan.Y, zoom);
    }

    private void ZoomTo(double z)
    {
        if (editing is not { } slot) return;
        zoom = Math.Round(Math.Clamp(z, 0.5, 3), 3);
        Scenes.Preview(slot, zoom, pan);
        SyncEdit();
        rest.Stop();
        rest.Start();
    }

    /// <summary>The settings following the picture: the slider at its zoom, Reset there while it is moved or zoomed.</summary>
    private void SyncEdit()
    {
        if (zoomSlider != null && Math.Abs(zoomSlider.Value - zoom) > 0.0005) { syncing = true; zoomSlider.Value = zoom; syncing = false; }
        if (resetButton != null) resetButton.IsEnabled = Math.Abs(pan.X) > 0.0005 || Math.Abs(pan.Y) > 0.0005 || Math.Abs(zoom - 1) > 0.0005;
    }

    private static (string, bool, bool) Painted(string slot) =>
        (slot, Appearance.PhotoKind(slot) == "photo" && Appearance.Photo(slot) != null, Appearance.Plain(slot));

    /// <summary>Escape or Enter ends it; the arrows nudge the photo (Shift: further), + and − zoom it.</summary>
    private void EditKey(KeyEventArgs e)
    {
        if (editing is not { } slot) return;
        switch (e.Key)
        {
            case Key.Escape:
                EndEdit();
                e.Handled = true;
                return;
            case Key.Enter when Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase:
                EndEdit();
                e.Handled = true;
                return;
            case Key.OemPlus or Key.Add:
                ZoomTo(zoom * 1.1);
                e.Handled = true;
                return;
            case Key.OemMinus or Key.Subtract:
                ZoomTo(zoom / 1.1);
                e.Handled = true;
                return;
        }
        if (Keyboard.FocusedElement is Slider) return;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.05 : 0.01;
        (double X, double Y)? by = e.Key switch { Key.Left => (-step, 0), Key.Right => (step, 0), Key.Up => (0, -step), Key.Down => (0, step), _ => null };
        if (by is not { } d) return;
        e.Handled = true;
        pan = (Math.Clamp(pan.X + d.X, -0.5, 0.5), Math.Clamp(pan.Y + d.Y, -0.5, 0.5));
        Scenes.Preview(slot, zoom, pan);
        SyncEdit();
        rest.Stop();
        rest.Start();
    }

    /// <summary>The settings: the place's name and what it wears, the zoom, Ink (a photo of one's own), Reset, Replace…,
    /// Remove and Done — in a row under a card, in a column beside the pane.</summary>
    private void PaintEdit()
    {
        if (editing is not { } slot) return;
        var side = slot == "side";
        paintedFor = Painted(slot);
        var own = paintedFor.Value.Own;
        var name = side ? "Navigation pane" : Appearance.Slots.FirstOrDefault(s => s.Key == slot).Name ?? slot;
        var title = K.V(1, K.Text(name, Ty.BodyStrong),
            K.Text(own ? (Appearance.Plain(slot) ? "Your photo, as it is" : "Your photo, inked") : "A drawing", Ty.Caption, B.Text2));
        title.VerticalAlignment = VerticalAlignment.Center;

        var slider = new Slider { Minimum = 0.5, Maximum = 3, Value = zoom, Width = side ? 150 : 110, VerticalAlignment = VerticalAlignment.Center, SmallChange = 0.05, LargeChange = 0.25 };
        System.Windows.Automation.AutomationProperties.SetName(slider, "Zoom");
        slider.ValueChanged += (_, e) => { if (!syncing) ZoomTo(e.NewValue); };
        zoomSlider = slider;
        Button Glyph(SymbolRegular icon, string what, Action act)
        {
            var b = K.Subtle(K.Icon(icon, 16), act, new Thickness(6), 4);
            b.ToolTip = what;
            System.Windows.Automation.AutomationProperties.SetName(b, what);
            return b;
        }
        var zoomRow = K.H(4, Glyph(SymbolRegular.ZoomOut24, "Zoom out", () => ZoomTo(zoom / 1.15)), slider, Glyph(SymbolRegular.ZoomIn24, "Zoom in", () => ZoomTo(zoom * 1.15)));
        zoomRow.VerticalAlignment = VerticalAlignment.Center;

        FrameworkElement? ink = null;
        if (own)
        {
            var toggle = new ToggleSwitch { Content = "Ink", IsChecked = !Appearance.Plain(slot), VerticalAlignment = VerticalAlignment.Center };
            toggle.ToolTip = "On: the photo drawn in two tones of the page. Off: the photo as it is, the words on it bordered in white.";
            toggle.Click += (_, _) => { Keep(); Appearance.SetPlain(slot, toggle.IsChecked != true); };
            ink = toggle;
        }

        var reset = K.Button("Reset", () => { rest.Stop(); Appearance.ResetPicture(slot); });
        reset.ToolTip = "Put the photo back where it started";
        resetButton = reset;
        var replace = K.Button("Replace…", () => { Keep(); Choose(slot); });
        var remove = K.Button("Remove", () =>
        {
            EndEdit();
            Appearance.RemovePhoto(slot);
            Appearance.SetKind(slot, "none");
            Scenes.Forget();
        });
        var acts = K.H(8, reset, replace, remove);
        acts.VerticalAlignment = VerticalAlignment.Center;
        var done = K.Button("Done", () => EndEdit(), accent: true);
        done.VerticalAlignment = VerticalAlignment.Center;

        FrameworkElement Rule() => side
            ? new Border { Height = 1, Margin = new Thickness(0, 2, 0, 2) }.Bg(B.Divider)
            : new Border { Width = 1, Height = 24, VerticalAlignment = VerticalAlignment.Center }.Bg(B.Divider);
        if (side)
        {
            done.HorizontalAlignment = HorizontalAlignment.Right;
            editBar.Child = K.V(10, title, Rule(), zoomRow, ink == null ? null : Rule(), ink, Rule(), acts, done);
        }
        else editBar.Child = K.H(12, title, Rule(), zoomRow, ink == null ? null : Rule(), ink, Rule(), acts, done);
        SyncEdit();
    }

    /// <summary>The edited place found again (it moves with the window and is built again with the look): only it lit,
    /// its outline on it and its settings and tip round it, kept inside the window.</summary>
    private void TickEdit(bool force)
    {
        if (editing is not { } slot) return;
        var side = slot == "side";
        foreach (var (s, at) in AppearanceMode.Where(this)) if (s == slot) { editAt = at; break; }
        if (editAt is not { } r) return;
        holes.Children.Clear();
        holes.Children.Add(side ? new RectangleGeometry(r) : new RectangleGeometry(Rect.Inflate(r, 6, 6), 14, 14));
        lastPane = lastCards = null;

        // (just outside a card; on the pane's own edge)
        var o = side ? 0 : 3;
        PutAt(ring, Rect.Inflate(r, o, o));
        ring.CornerRadius = new CornerRadius(side ? 0 : 8 + o);
        PutAt(halo, Rect.Inflate(r, o + 4, o + 4));
        halo.CornerRadius = new CornerRadius(side ? 0 : 8 + o + 4);
        halo.Visibility = side ? Visibility.Collapsed : Visibility.Visible;

        editBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double W = ActualWidth, H = ActualHeight, m = 12, g = 12;
        double bw = editBar.DesiredSize.Width, bh = editBar.DesiredSize.Height, tw = tip.DesiredSize.Width, th = tip.DesiredSize.Height;
        double X(double x, double w) => Math.Min(Math.Max(x, m), Math.Max(m, W - w - m));
        double Y(double y, double h) => Math.Min(Math.Max(y, m), Math.Max(m, H - h - m));
        if (side)
        {
            var bx = X(r.Right + 16, bw);
            var by = Y(r.Top + r.Height / 2 - bh / 2, bh);
            Canvas.SetLeft(editBar, bx);
            Canvas.SetTop(editBar, by);
            Canvas.SetLeft(tip, X(bx, tw));
            Canvas.SetTop(tip, Y(by - 8 - th, th));
            return;
        }
        // (under the card, centred on it — over it where there is no room under; the tip on its other side)
        var below = r.Bottom + g + bh + m <= H || r.Top - g - bh < m;
        Canvas.SetLeft(editBar, X(r.Left + r.Width / 2 - bw / 2, bw));
        Canvas.SetTop(editBar, Y(below ? r.Bottom + g : r.Top - g - bh, bh));
        Canvas.SetLeft(tip, X(r.Left + r.Width / 2 - tw / 2, tw));
        Canvas.SetTop(tip, Y(below ? r.Top - g - th : r.Bottom + g, th));
    }

    private static void PutAt(FrameworkElement e, Rect r)
    {
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = Math.Max(0, r.Width);
        e.Height = Math.Max(0, r.Height);
    }

    // ---- the bar ----

    private void Build()
    {
        var themeTab = Segment("Theme", tab == "theme", () => { tab = "theme"; Build(); });
        var coursesTab = Segment("Courses", tab == "courses", () => { tab = "courses"; Build(); });
        var tabs = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(2), Child = K.H(2, themeTab, coursesTab) }.Bg(B.Control2);
        var look = new ComboBox { MinWidth = 150, ItemsSource = new[] { "Light", "Dark", "Use system setting" } };
        look.SelectedIndex = Appearance.Look switch { "light" => 0, "dark" => 1, _ => 2 };
        look.SelectionChanged += (_, _) => Appearance.Look = look.SelectedIndex switch { 0 => "light", 1 => "dark", _ => "system" };
        look.ToolTip = "Light, dark, or the mode Windows uses for apps";
        var done = K.Button("Done", End, accent: true);
        var head = K.Columns("auto, *, auto, 12, auto", tabs.Col(0), look.Col(2), done.Col(4));
        var body = tab == "theme" ? ThemeRow() : CoursesRow();
        var holder = new Border { MinHeight = 64, Child = body, Margin = new Thickness(0, 12, 0, 0) };
        barBody.Content = K.V(0, head, holder);
    }

    private static Button Segment(string text, bool on, Action pick)
    {
        var t = K.Text(text, on ? Ty.BodyStrong : Ty.Body, on ? B.OnAccent : B.Text);
        var face = new Border { Padding = new Thickness(14, 5, 14, 6), CornerRadius = new CornerRadius(5), Child = t };
        if (on) face.SetResourceReference(Border.BackgroundProperty, B.Accent);
        var b = K.Subtle(face, pick, new Thickness(0), 5);
        System.Windows.Automation.AutomationProperties.SetName(b, text + (on ? ", chosen" : ""));
        return b;
    }

    /// <summary>The ready themes, then the colours (Windows' own, the web's, one of your own).</summary>
    private FrameworkElement ThemeRow()
    {
        var themes = K.H(8);
        foreach (var r in Appearance.ReadyThemes)
        {
            var on = Appearance.ThemeName == r.Name;
            var color = r.Accent is { } h ? K.Hex(h) : Appearance.Accent;
            var swatch = new Border
            {
                Width = 78, Height = 48, CornerRadius = new CornerRadius(6), ClipToBounds = true,
                Child = r.Scene is { } sc ? Scenes.Swatch(sc, "Card0", color) : ColourKit.DefaultPage(),
            };
            swatch.Loaded += (_, _) => swatch.Clip = new RectangleGeometry(new Rect(0, 0, swatch.ActualWidth, swatch.ActualHeight), 6, 6);
            var ring = new Border { Padding = new Thickness(2), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2), Child = swatch };
            if (on) ring.SetResourceReference(Border.BorderBrushProperty, B.Accent); else ring.BorderBrush = Brushes.Transparent;
            var label = K.Text(r.Name, Ty.Caption).Center();
            if (on) label.FontWeight = FontWeights.SemiBold;
            var tile = K.V(4, ring, label);
            var b = K.Subtle(tile, () => { Scenes.Forget(); Appearance.Wear(r); }, new Thickness(2), 8);
            b.ToolTip = r.Accent == null ? "Windows’ own accent colour, no drawing" : $"{r.Name}: its colour and drawing throughout";
            System.Windows.Automation.AutomationProperties.SetName(b, r.Name + " theme" + (on ? ", worn" : ""));
            themes.Children.Add(b);
        }
        var colours = K.H(0);
        colours.Children.Add(ColourDot(null, ColourKit.SystemAccent(), "Windows accent"));
        foreach (var c in Appearance.Colours) colours.Children.Add(ColourDot(c.Hex, K.Hex(c.Hex), c.Name));
        colours.Children.Add(CustomDot());
        // (the themes on a line of their own, the colours under them: every theme whole, no scroll bar)
        return K.V(10, themes, colours);
    }

    private FrameworkElement ColourDot(string? hex, Color colour, string name) =>
        ColourKit.Dot(K.Brush(colour), Appearance.AccentHex == hex, name, () => Appearance.SetAccent(hex), size: 28);

    private FrameworkElement CustomDot()
    {
        var hex = Appearance.AccentHex;
        var own = hex != null && Appearance.Colours.All(c => !string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase))
                  && Appearance.ReadyThemes.All(r => !string.Equals(r.Accent, hex, StringComparison.OrdinalIgnoreCase));
        Brush fill = own && Appearance.Hex(hex) is { } c ? K.Brush(c) : ColourKit.Hues();
        return ColourKit.Dot(fill, own, "Custom colour…",
            () => ColourKit.Custom(window, Appearance.Accent, "Custom colour", null, picked => Appearance.SetAccent(ColourKit.Code(picked))),
            own ? null : K.Icon(SymbolRegular.Add24, 14, Colors.White), size: 28);
    }

    /// <summary>Each course with its colour; pressed, Canvas's palette and any colour of your own — saved to the school
    /// as the student's own colour for the course (setColor), as the web's and Canvas's are.</summary>
    private FrameworkElement CoursesRow()
    {
        if (engine.Courses.Count == 0) return K.Wrapped("Your courses show here once they have loaded.", Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
        var row = K.H(8);
        foreach (var c in engine.Courses)
        {
            var course = c;
            var sq = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), Background = K.Brush(K.Hex(course.Color ?? "#888888")) };
            var chip = new Border { Padding = new Thickness(10, 7, 12, 7), CornerRadius = new CornerRadius(16), Child = K.H(8, sq, K.Text(course.Code, Ty.Body)) }.Bg(B.Control2);
            Button? b = null;
            b = K.Subtle(chip, () => CourseMenu(course, b!), new Thickness(0), 16);
            b.ToolTip = $"{course.Code}’s colour";
            row.Children.Add(b);
        }
        row.VerticalAlignment = VerticalAlignment.Center;
        return new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Canvas's own course colours.</summary>
    private static readonly string[] Palette = { "#BD3C14", "#FF2717", "#E71F63", "#8F3E97", "#65499D", "#4554A4", "#1770AB", "#0B9BE3",
                                                 "#06A3B7", "#009688", "#009606", "#8D9900", "#D97900", "#FD5D10", "#F06291", "#111111" };

    private void CourseMenu(CourseRow course, FrameworkElement at)
    {
        var popup = new System.Windows.Controls.Primitives.Popup { PlacementTarget = at, Placement = System.Windows.Controls.Primitives.PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, VerticalOffset = -8 };
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 8 };
        foreach (var hex in Palette)
        {
            var h = hex;
            var on = string.Equals(course.Color, h, StringComparison.OrdinalIgnoreCase);
            grid.Children.Add(ColourKit.Dot(K.Brush(K.Hex(h)), on, h, () => { popup.IsOpen = false; _ = SaveColour(course, h); }, size: 24));
        }
        var any = K.Button("Any colour…", () =>
        {
            popup.IsOpen = false;
            ColourKit.Custom(window, K.Hex(course.Color ?? "#1770AB"), $"{course.Code}’s colour", "Use colour", picked => _ = SaveColour(course, ColourKit.Code(picked)));
        });
        var card = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Effect = K.Shadow(24, 8, 0.28), Margin = new Thickness(12),
            Child = K.V(10, K.Text(course.Code, Ty.BodyStrong), grid, any) };
        card.SetResourceReference(Border.BackgroundProperty, B.Flyout);
        card.SetResourceReference(Border.BorderBrushProperty, B.FlyoutStroke);
        popup.Child = card;
        popup.IsOpen = true;
    }

    private async Task SaveColour(CourseRow course, string hex)
    {
        if (!await engine.Act("setColor", new { id = course.Id, hex })) { Log.Write("appearance: the course colour was not saved"); return; }
        try { await engine.Call<OK>("refresh"); } catch { /* read again as it is */ }
        await engine.LoadSidebar();
        if (On) Build();
    }
}

/// <summary>The colour pieces Appearance's bar shares (once Settings' own): Windows 11's swatches, every hue, Custom
/// colour's spectrum, and a theme's Default tile.</summary>
internal static class ColourKit
{
    internal static Color SystemAccent() => Wpf.Ui.Appearance.ApplicationAccentColorManager.SystemAccent;

    internal static string Code(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    /// <summary>
    /// Custom colour (Windows 11's colour picker, the Mac's wheel), in a sheet over the window: a spectrum — every hue
    /// across, light to deep down — dragged or clicked 1:1 under the pointer, a sample of the colour picked, and its hex
    /// code, which can be typed. Its button hands the colour on — (0.3.52) or, with no button named (the theme's own colour),
    /// the colour is handed on as it is picked: once the pointer rests a moment, as the drag ends and as the sheet closes,
    /// never on every step of a drag (each one drew the whole window again), so what is picked is kept with nothing pressed.
    /// </summary>
    internal static void Custom(Window owner, Color? from, string title, string? action, Action<Color> use)
    {
        var (h0, l0) = from is { } f ? HueShade(f) : (210.0, 0.5);
        double hue = h0, shade = l0;
        Color? exact = from;
        Color Picked() => exact ?? Hsl(hue, 0.85, shade);
        const double W = 320, H = 150;
        var spectrum = new Grid { Width = W, Height = H, Cursor = Cursors.Cross, ClipToBounds = true };
        spectrum.Children.Add(new Border { Background = Hues(), CornerRadius = new CornerRadius(6) });
        var depth = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        depth.GradientStops.Add(new GradientStop(Color.FromArgb(220, 255, 255, 255), 0));
        depth.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5));
        depth.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.5));
        depth.GradientStops.Add(new GradientStop(Color.FromArgb(220, 0, 0, 0), 1));
        spectrum.Children.Add(new Border { Background = depth, CornerRadius = new CornerRadius(6) });
        var marker = new Ellipse { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Effect = K.Shadow(4, 0, 0.5) };
        spectrum.Children.Add(marker);
        var sample = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(8) }.Stroke(B.CardStroke);
        var code = new TextBox { Width = 120, MaxLength = 7, PlaceholderText = "#RRGGBB" };
        var typing = false;
        var live = action == null;
        Color? handed = from;
        void Hand()
        {
            var c = Picked();
            if (handed == c) return;
            handed = c;
            use(c);
        }
        var rest = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(160) };
        rest.Tick += (_, _) => { rest.Stop(); Hand(); };
        void Soon() { if (!live) return; rest.Stop(); rest.Start(); }
        void Paint()
        {
            var c = Picked();
            sample.Background = K.Brush(c);
            marker.Margin = new Thickness(hue / 360 * W - 8, (0.85 - shade) / 0.7 * H - 8, 0, 0);
            if (!typing) code.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        void At(Point p)
        {
            exact = null;
            hue = Math.Clamp(p.X / W, 0, 1) * 360;
            shade = 0.85 - Math.Clamp(p.Y / H, 0, 1) * 0.7;
            Paint();
            Soon();
        }
        spectrum.MouseLeftButtonDown += (_, e) => { spectrum.CaptureMouse(); At(e.GetPosition(spectrum)); };
        spectrum.MouseMove += (_, e) => { if (spectrum.IsMouseCaptured) At(e.GetPosition(spectrum)); };
        spectrum.MouseLeftButtonUp += (_, _) => { spectrum.ReleaseMouseCapture(); if (live) { rest.Stop(); Hand(); } };
        code.TextChanged += (_, _) =>
        {
            if (Appearance.Hex(code.Text.Trim()) is not { } c || code.Text.Trim().TrimStart('#').Length != 6) return;
            typing = true;
            exact = c;
            (hue, shade) = HueShade(c);
            Paint();
            typing = false;
            Soon();
        };
        Paint();
        var s = new Sheet
        {
            Title = title,
            Width = 400,
            Body = K.V(14, spectrum, K.Columns("auto, 16, *, auto", sample.Col(0),
                K.V(2, K.Wrapped("Drag across for the hue, up and down for the shade.", Ty.Caption, B.Text2, 3)).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(2),
                K.V(4, K.Text("Hex", Ty.Caption, B.Text2), code).Col(3))),
        };
        if (live)
        {
            s.Closed += () => { rest.Stop(); Hand(); }; // (closed mid-pick: what was shown is kept)
            s.Add("Close", SheetButtonKind.Close);
        }
        else
        {
            s.Add(action!, SheetButtonKind.Primary, () => use(Picked()));
            s.Add("Cancel", SheetButtonKind.Close);
        }
        Dialogs.Show(s, owner);
    }

    /// <summary>The Default theme's tile: a small page in the theme's own neutral colours — the pane with its mark in
    /// the accent, two counters and a line — as the other tiles show their drawings (the Mac's ThemeSwatch).</summary>
    internal static FrameworkElement DefaultPage()
    {
        Border Bar(double w, double h, string brush) => new Border { Width = w, Height = h, CornerRadius = new CornerRadius(h / 2), HorizontalAlignment = HorizontalAlignment.Left }.Bg(brush);
        var mark = new Border { Width = 3, Height = 8, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, 3, 0) }.Bg(B.Accent);
        var pane = K.V(5, K.H(0, mark, Bar(14, 4, B.StrongStroke)).Margin(4, 10, 0, 0), Bar(16, 4, B.Divider).Margin(7, 0, 0, 0), Bar(12, 4, B.Divider).Margin(7, 0, 0, 0));
        pane.VerticalAlignment = VerticalAlignment.Stretch;
        var paneHolder = new Border { Child = pane, Width = 32 }.Bg(B.LayerAlt);
        Border Counter() => new Border { Height = 20, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), Child = Bar(10, 3, B.StrongStroke).Margin(4, 4, 0, 0) }.Bg(B.Card).Stroke(B.CardStroke);
        var counters = K.Columns("*, *", Counter().Col(0), Counter().Col(1));
        var page = K.V(6, counters, Bar(44, 4, B.Divider), Bar(30, 4, B.Divider));
        page.Margin = new Thickness(6, 10, 4, 0);
        var g = K.Columns("auto, *", paneHolder.Col(0), page.Col(1));
        return new Border { Child = g }.Bg(B.Solid);
    }

    /// <summary>A swatch (Windows 11's): its colour in a square, the one worn checked and ringed.</summary>
    internal static FrameworkElement Dot(Brush fill, bool on, string name, Action pick, UIElement? inside = null, double size = 40)
    {
        var dot = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(4), Background = fill };
        if (on) dot.Child = K.Icon(SymbolRegular.Checkmark24, size * 0.45, Colors.White);
        else if (inside != null) dot.Child = inside;
        var ring = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), Child = dot, Margin = new Thickness(0, 0, 6, 6) };
        if (on) ring.SetResourceReference(Border.BorderBrushProperty, B.Text); else ring.BorderBrush = Brushes.Transparent;
        var b = K.Subtle(ring, pick, new Thickness(0), 6);
        b.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(b, name + (on ? ", chosen" : ""));
        return b;
    }

    /// <summary>Every hue, left to right (the custom swatch's face, the spectrum's ground).</summary>
    internal static LinearGradientBrush Hues()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (var i = 0; i <= 6; i++) g.GradientStops.Add(new GradientStop(Hsl(i * 60, 1, 0.5), i / 6.0));
        g.Freeze();
        return g;
    }

    /// <summary>A colour from hue (0–360), saturation and lightness (0–1).</summary>
    internal static Color Hsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = (h % 360) switch
        {
            < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        static byte B8(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        return Color.FromRgb(B8(r + m), B8(g + m), B8(b + m));
    }

    /// <summary>A colour's hue and shade, as the spectrum places them (its lightness kept within the spectrum's).</summary>
    internal static (double Hue, double Shade) HueShade(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var d = max - min;
        double h = 0;
        if (d > 1e-6)
        {
            if (max == r) h = 60 * ((g - b) / d % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, Math.Clamp(l, 0.15, 0.85));
    }
}

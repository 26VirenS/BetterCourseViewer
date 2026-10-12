using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Components.Glass;

namespace GlassShowcase;

/// <summary>
/// The showcase: Simpl's Dashboard as the backdrop, and over it the glass Simpl for Windows will draw — the Mac's
/// "Unread announcements" popover, a toolbar with the Dashboard's Cards · List · Activity, a tab bar, a panel of
/// switches and sliders whose knobs turn into lenses, and a glass bubble to drag and fling. The Tune panel changes the
/// glass live (frost, refraction, dispersion, bevel, colour, rim); Dark and Light, and the backdrop's drift, at its top.
/// </summary>
public sealed class ShowcaseWindow : Window
{
    private readonly Scene scene = new();
    private readonly Canvas glassLayer = new();
    private readonly List<LiquidGlass> panels = new();
    private bool dark = true;
    private static readonly FontFamily Face = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public ShowcaseWindow()
    {
        Title = "Liquid Glass for Windows — Simpl";
        Width = 1360;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Black;
        var root = new Grid();
        root.Children.Add(scene);
        root.Children.Add(glassLayer);
        Content = root;
        Loaded += (_, _) => Build();
        SizeChanged += (_, _) => Layout();
    }

    private Brush Ink(double a = 0.92) => new SolidColorBrush(dark ? Color.FromArgb((byte)(a * 255), 255, 255, 255) : Color.FromArgb((byte)(a * 255), 0, 0, 0));
    private Brush Ink2 => new SolidColorBrush(dark ? Color.FromArgb(150, 235, 235, 245) : Color.FromArgb(150, 60, 60, 67));

    private TextBlock T(string s, double size, Brush? b = null, FontWeight? w = null, FontFamily? f = null) => new()
    {
        Text = s, FontSize = size, Foreground = b ?? Ink(), FontWeight = w ?? FontWeights.Normal, FontFamily = f ?? Face,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private TextBlock Glyph(string glyph, double size, Brush b) => new() { Text = glyph, FontFamily = Icons, FontSize = size, Foreground = b, VerticalAlignment = VerticalAlignment.Center };

    private LiquidGlass Glass(UIElement content, double radius, Thickness padding)
    {
        var g = new LiquidGlass { Backdrop = scene, CornerRadius = radius, Content = content, Padding = padding };
        if (dark) g.Dark(); else g.Light();
        panels.Add(g);
        return g;
    }

    private readonly Dictionary<string, FrameworkElement> placed = new();

    private void Place(string key, FrameworkElement e, double x, double y)
    {
        placed[key] = e;
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        glassLayer.Children.Add(e);
    }

    private void Build()
    {
        glassLayer.Children.Clear();
        panels.Clear();
        placed.Clear();
        scene.Dark = dark;
        Place("popover", Popover(), 60, 150);
        Place("toolbar", Toolbar(), 0, 18);
        Place("controls", ControlsPanel(), 0, 150);
        Place("tabbar", TabBar(), 0, 0);
        Place("tune", TunePanel(), 0, 0);
        Place("lens", Lens(), 560, 420);
        Layout();
    }

    private void Layout()
    {
        var w = glassLayer.ActualWidth > 0 ? glassLayer.ActualWidth : Width;
        var h = glassLayer.ActualHeight > 0 ? glassLayer.ActualHeight : Height;
        if (placed.TryGetValue("toolbar", out var tb)) Canvas.SetLeft(tb, Math.Max(20, (w - 760) / 2));
        if (placed.TryGetValue("controls", out var cp)) Canvas.SetLeft(cp, w - 360 - 40);
        if (placed.TryGetValue("tabbar", out var tab)) { Canvas.SetLeft(tab, Math.Max(20, (w - 420) / 2)); Canvas.SetTop(tab, h - 96); }
        if (placed.TryGetValue("tune", out var tune)) { Canvas.SetLeft(tune, w - 360 - 40); Canvas.SetTop(tune, 470); }
    }

    // MARK: - The Mac's popover (the screenshot's): Unread announcements

    private FrameworkElement Popover()
    {
        var orange = new SolidColorBrush(Color.FromRgb(255, 149, 0));
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
        var close = Glyph("", 15, Ink2);
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Glyph("", 20, orange));
        title.Children.Add(new TextBlock { Width = 12 });
        title.Children.Add(T("Unread announcements", 21, Ink(), FontWeights.SemiBold, Display));
        head.Children.Add(title);

        var count = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 18) };
        count.Children.Add(T("0", 52, Ink(), FontWeights.Bold, Display));
        var caught = T("All caught up", 17, Ink2);
        caught.Margin = new Thickness(22, 14, 0, 0);
        count.Children.Add(caught);

        var list = new StackPanel();
        list.Children.Add(new TextBlock { Text = "Read recently", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ink2, FontFamily = Face, Margin = new Thickness(12, 0, 0, 12) });
        (string Title, string Sub)[] rows =
        {
            ("This Week in WRI 010", "Posted Oct 11 · read"),
            ("Week 8 (Oct 12 - 16, 2026) announcement", "Posted Oct 9 · read"),
            ("Algebra workshop: Exponentials and Logarithms -- Today", "Posted Oct 8 · read"),
            ("Graders for Exam 1", "Posted Oct 6 · read"),
        };
        foreach (var (t, s) in rows)
        {
            var row = new DockPanel { Margin = new Thickness(12, 0, 0, 22) };
            var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(200, 196, 134, 64)), Margin = new Thickness(0, 2, 18, 2) };
            DockPanel.SetDock(bar, Dock.Left);
            row.Children.Add(bar);
            var chev = Glyph("", 13, Ink2);
            DockPanel.SetDock(chev, Dock.Right);
            row.Children.Add(chev);
            var words = new StackPanel();
            var tt = T(t, 18, Ink(0.62));
            tt.TextWrapping = TextWrapping.Wrap;
            tt.TextTrimming = TextTrimming.None;
            words.Children.Add(tt);
            words.Children.Add(T(s, 15, Ink2));
            row.Children.Add(words);
            list.Children.Add(row);
        }
        var body = new StackPanel { Width = 520 };
        body.Children.Add(head);
        body.Children.Add(count);
        body.Children.Add(list);
        var g = Glass(body, 34, new Thickness(30, 26, 30, 10));
        g.Interactive = true;
        g.PressGrowth = 0.012;
        return Draggable(g);
    }

    // MARK: - The toolbar: Back and Forward, Refresh, the Dashboard's views, Search

    private FrameworkElement Toolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(IconCapsule(new[] { "", "" }));
        bar.Children.Add(Gap(10));
        bar.Children.Add(IconCapsule(new[] { "" }));
        bar.Children.Add(Gap(18));
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 150 };
        title.Children.Add(T("Dashboard", 16, Ink(), FontWeights.SemiBold));
        title.Children.Add(T("Liquid Glass", 12, Ink2));
        bar.Children.Add(title);
        var seg = new GlassSegmented(new[] { "Cards", "List", "Activity" }, 82, 1, darkText: !dark);
        bar.Children.Add(Glass(seg, 20, new Thickness(3)));
        bar.Children.Add(Gap(10));
        bar.Children.Add(IconCapsule(new[] { "" }));
        bar.Children.Add(Gap(10));
        var search = new StackPanel { Orientation = Orientation.Horizontal, Width = 150 };
        search.Children.Add(Glyph("", 14, Ink2));
        search.Children.Add(Gap(8));
        search.Children.Add(T("Search", 14, Ink2));
        var sg = Glass(search, 20, new Thickness(14, 9, 14, 9));
        sg.Interactive = true;
        bar.Children.Add(sg);
        return bar;
    }

    private static FrameworkElement Gap(double w) => new Border { Width = w };

    /// <summary>Icon buttons in one capsule of glass (Back and Forward share one, as on the Mac).</summary>
    private FrameworkElement IconCapsule(string[] glyphs)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var gl in glyphs)
        {
            var b = new Border { Width = 36, Height = 34, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = Glyph(gl, 15, Ink()) };
            ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
            row.Children.Add(b);
        }
        var g = Glass(row, 20, new Thickness(3, 1, 3, 1));
        g.Interactive = true;
        return g;
    }

    // MARK: - The tab bar: a lens slides between the tabs

    private FrameworkElement TabBar()
    {
        var seg = new GlassSegmented(new[] { "Today", "Courses", "To Do", "Grades", "Search" }, 82, 0, darkText: !dark);
        var g = Glass(seg, 30, new Thickness(6, 8, 6, 8));
        return g;
    }

    // MARK: - Switches and sliders

    private FrameworkElement ControlsPanel()
    {
        var p = new StackPanel { Width = 300 };
        p.Children.Add(T("Controls", 19, Ink(), FontWeights.SemiBold, Display));
        p.Children.Add(Gap(0));
        (string Label, bool On)[] switches = { ("Hide completed", true), ("Reminders", false), ("Beta updates", true) };
        foreach (var (label, isOn) in switches)
        {
            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var t = new GlassToggle(isOn);
            DockPanel.SetDock(t, Dock.Right);
            row.Children.Add(t);
            row.Children.Add(T(label, 15));
            p.Children.Add(row);
        }
        var sl = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        sl.Children.Add(T("Text size", 13, Ink2));
        sl.Children.Add(new GlassSlider(0.42, 300));
        p.Children.Add(sl);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var plain = Glass(T("Cancel", 14, Ink(), FontWeights.Medium), 18, new Thickness(18, 8, 18, 8));
        plain.Interactive = true;
        buttons.Children.Add(plain);
        buttons.Children.Add(Gap(10));
        var prom = Glass(T("Hand In", 14, Brushes.White, FontWeights.SemiBold), 18, new Thickness(18, 8, 18, 8));
        prom.Tint = Color.FromRgb(0, 122, 255);
        prom.TintAmount = 0.62;
        prom.Saturation = 1.5;
        prom.Interactive = true;
        buttons.Children.Add(prom);
        p.Children.Add(buttons);
        return Draggable(Glass(p, 28, new Thickness(22, 18, 22, 20)));
    }

    // MARK: - The lens: drag it, fling it (it heads where its momentum takes it, and springs back inside the window)

    private FrameworkElement Lens()
    {
        var g = new LiquidGlass { Backdrop = scene, Capsule = true, Width = 150, Height = 150, BlurRadius = 3, Refraction = 26, Bevel = 44, Dispersion = 0.5, TintAmount = 0.04, Saturation = 1.3, Rim = 0.6, Glow = 0.08, ShadowOpacity = 0.3, Interactive = true, PressGrowth = 0.06 };
        panels.Add(g);
        var sx = new Spring(560, 0.45, 0.78);
        var sy = new Spring(420, 0.45, 0.78);
        sx.Changed += v => Canvas.SetLeft(g, v);
        sy.Changed += v => Canvas.SetTop(g, v);
        Point grab = default;
        var samples = new List<(DateTime T, Point P)>();
        g.Cursor = Cursors.SizeAll;
        g.MouseLeftButtonDown += (_, e) => { grab = e.GetPosition(g); samples.Clear(); g.CaptureMouse(); e.Handled = true; };
        g.MouseMove += (_, e) =>
        {
            if (!g.IsMouseCaptured) return;
            var p = e.GetPosition(glassLayer);
            sx.Snap(p.X - grab.X);
            sy.Snap(p.Y - grab.Y);
            samples.Add((DateTime.UtcNow, p));
            if (samples.Count > 6) samples.RemoveAt(0);
        };
        g.MouseLeftButtonUp += (_, _) =>
        {
            if (!g.IsMouseCaptured) return;
            g.ReleaseMouseCapture();
            double vx = 0, vy = 0;
            if (samples.Count >= 2)
            {
                var (t0, p0) = samples[0];
                var (t1, p1) = samples[^1];
                var dt = Math.Max((t1 - t0).TotalSeconds, 0.008);
                vx = (p1.X - p0.X) / dt;
                vy = (p1.Y - p0.Y) / dt;
            }
            // where the flick is going, kept inside the window, reached on a spring with the flick's own velocity
            var tx = Math.Clamp(sx.Value + Spring.Project(vx), 10, glassLayer.ActualWidth - 160);
            var ty = Math.Clamp(sy.Value + Spring.Project(vy), 10, glassLayer.ActualHeight - 160);
            sx.To(tx, vx);
            sy.To(ty, vy);
        };
        return g;
    }

    /// <summary>A panel moved by dragging it anywhere but its controls.</summary>
    private FrameworkElement Draggable(LiquidGlass g)
    {
        Point grab = default;
        var dragging = false;
        g.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInControl(d, g)) return;
            grab = e.GetPosition(g);
            dragging = true;
            g.CaptureMouse();
        };
        g.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(glassLayer);
            Canvas.SetLeft(g, p.X - grab.X);
            Canvas.SetTop(g, p.Y - grab.Y);
        };
        g.MouseLeftButtonUp += (_, _) => { if (dragging) { dragging = false; g.ReleaseMouseCapture(); } };
        return g;
    }

    private static bool IsInControl(DependencyObject d, DependencyObject stop)
    {
        for (var x = d; x != null && x != stop; x = VisualTreeHelper.GetParent(x))
            if (x is GlassToggle or GlassSlider or GlassSegmented or Slider or Button or CheckBox) return true;
        return false;
    }

    // MARK: - Tune: the glass, live

    private FrameworkElement TunePanel()
    {
        var p = new StackPanel { Width = 300 };
        var head = new DockPanel();
        var mode = new GlassSegmented(new[] { "Dark", "Light" }, 64, dark ? 0 : 1, darkText: !dark);
        mode.Changed += i => { dark = i == 0; Build(); };
        DockPanel.SetDock(mode, Dock.Right);
        head.Children.Add(mode);
        head.Children.Add(T("Tune", 19, Ink(), FontWeights.SemiBold, Display));
        p.Children.Add(head);
        var status = T(GlassEffect.Available ? "Glass shader: on" : "Glass shader: off (fallback)", 12, Ink2);
        status.Margin = new Thickness(0, 2, 0, 6);
        p.Children.Add(status);
        void Knob(string label, double min, double max, double value, Action<double> set)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var shown = T(value.ToString("0.##"), 12, Ink2);
            shown.Width = 40;
            shown.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(shown, Dock.Right);
            row.Children.Add(shown);
            var name = T(label, 13, Ink());
            name.Width = 86;
            DockPanel.SetDock(name, Dock.Left);
            row.Children.Add(name);
            var s = new GlassSlider((value - min) / (max - min), 170);
            s.ValueChanged += f => { var v = min + f * (max - min); shown.Text = v.ToString("0.##"); set(v); };
            row.Children.Add(s);
            p.Children.Add(row);
        }
        var first = panels.FirstOrDefault(x => x.Backdrop == scene) ?? new LiquidGlass().Dark();
        Knob("Frost", 0, 60, first.BlurRadius, v => Each(g => g.BlurRadius = v));
        Knob("Refraction", 0, 40, first.Refraction, v => Each(g => g.Refraction = v));
        Knob("Dispersion", 0, 1, first.Dispersion, v => Each(g => g.Dispersion = v));
        Knob("Bevel", 2, 60, first.Bevel, v => Each(g => g.Bevel = v));
        Knob("Colour", 0.5, 2.2, first.Saturation, v => Each(g => g.Saturation = v));
        Knob("Tint", 0, 0.9, first.TintAmount, v => Each(g => g.TintAmount = v));
        Knob("Rim", 0, 1.2, first.Rim, v => Each(g => g.Rim = v));
        var drift = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var dt = new GlassToggle(true);
        dt.Toggled += on => scene.Animate(on);
        DockPanel.SetDock(dt, Dock.Right);
        drift.Children.Add(dt);
        drift.Children.Add(T("Moving backdrop", 13));
        p.Children.Add(drift);
        return Draggable(Glass(p, 28, new Thickness(20, 16, 20, 16)));
    }

    /// <summary>Every glass panel over the backdrop (not the knobs' and thumbs' lenses), changed alike.</summary>
    private void Each(Action<LiquidGlass> change)
    {
        foreach (var g in panels.Where(g => g.Backdrop == scene && g.Width is not 150)) change(g);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Simpl.Engine;

namespace Simpl.Shell;

/// <summary>
/// Over the school's own page while it is what the window shows (the part of the Mac's LoginLayer that sits over the
/// page): the note that says what to do there in the middle of a sign-in ("Finish signing in below"), Start Over on the
/// school's sign-in pages, and — the window having no title bar of its own — its caption buttons. A window of its own,
/// owned by the main window and kept over its top band: the web view underneath is a window too, so nothing drawn in the
/// main window can show over it; what is transparent here lets the pointer through to the page.
/// </summary>
public sealed class LoginOverlay : Window
{
    /// <summary>The band at the top of the main window this covers (DIPs).</summary>
    public const double BandH = 96;

    private readonly MainWindow main;
    private readonly Engine.Engine engine;
    private readonly Grid root = new();
    private readonly FrameworkElement note, startOver;
    private readonly StackPanel captions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    private readonly List<(Border Box, TextBlock Glyph, bool Close)> captionButtons = new();
    private readonly List<TextBlock> inkTexts = new(), ink2Texts = new(), accentTexts = new();
    private readonly List<Border> materials = new(), edges = new();
    private TextBlock? maxGlyph;
    private bool dark = true;

    private static readonly FontFamily TextFace = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily IconFace = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public LoginOverlay(MainWindow main, Engine.Engine engine)
    {
        this.main = main;
        this.engine = engine;
        Owner = main;
        Title = "Simpl";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Content = root;
        note = BuildNote();
        startOver = BuildStartOver();
        BuildCaptions();
        root.Children.Add(note);
        root.Children.Add(startOver);
        root.Children.Add(captions);
        main.LocationChanged += (_, _) => Place();
        main.SizeChanged += (_, _) => Place();
        main.StateChanged += (_, _) => Update();
        main.IsVisibleChanged += (_, _) => Update();
        main.Closed += (_, _) => Close();
    }

    // ---- what shows ----

    /// <summary>What the engine and the sign-in say now: shown over the page, or put away.</summary>
    public void Update()
    {
        var login = engine.Login;
        var pageShown = engine.Phase == Phase.Web && !login.Covers;
        var show = pageShown && main.IsVisible && main.IsLoaded && main.WindowState != WindowState.Minimized;
        if (!show)
        {
            if (IsVisible) Hide();
            return;
        }
        dark = Screens.AppView.Dark(engine.Snapshot?.Dark);
        Paint();
        note.Visibility = login.Handoff ? Visibility.Visible : Visibility.Collapsed;
        startOver.Visibility = !login.Handoff && login.OnSchoolPage ? Visibility.Visible : Visibility.Collapsed;
        if (maxGlyph != null) maxGlyph.Text = main.WindowState == WindowState.Maximized ? "" : "";
        Place();
        if (!IsVisible) Show();
    }

    /// <summary>Over the top band of the main window's content, wherever it is.</summary>
    private void Place()
    {
        try
        {
            var src = PresentationSource.FromVisual(main);
            if (src?.CompositionTarget == null) return;
            var (originPx, size) = main.ContentBounds();
            var dip = src.CompositionTarget.TransformFromDevice.Transform(originPx);
            Left = dip.X;
            Top = dip.Y;
            Width = Math.Max(1, size.Width);
            Height = Math.Max(1, Math.Min(BandH, size.Height));
            if (note is FrameworkElement n) n.Width = Math.Max(240, Math.Min(520, Width - 24));
        }
        catch { /* (the main window on its way out) */ }
    }

    // ---- the pieces ----

    /// <summary>Over the school's page in the middle of a sign-in: what to do there, and that Simpl carries on by itself (the Mac's HandoffNote).</summary>
    private FrameworkElement BuildNote()
    {
        var icon = new TextBlock { Text = "", FontFamily = IconFace, FontSize = 19, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        accentTexts.Add(icon);
        var title = new TextBlock { Text = "Finish signing in below", FontFamily = TextFace, FontSize = 14, FontWeight = FontWeights.SemiBold };
        inkTexts.Add(title);
        var sub = new TextBlock { Text = "Simpl carries on once you’re through.", FontFamily = TextFace, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
        ink2Texts.Add(sub);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        words.Children.Add(title);
        words.Children.Add(sub);
        var restart = Pill("Start Over", null, 22, 12, () => engine.Login.Restart());
        restart.Margin = new Thickness(0, 0, 6, 0);
        var x = new TextBlock { Text = "", FontFamily = IconFace, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        ink2Texts.Add(x);
        var close = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Faint, Child = x, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Hide" };
        close.MouseEnter += (_, _) => close.Background = Wash(0.08);
        close.MouseLeave += (_, _) => close.Background = Faint;
        close.MouseLeftButtonUp += (_, e) => { engine.Login.Handoff = false; e.Handled = true; };
        var row = new Grid();
        for (var i = 0; i < 4; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(words, 1);
        Grid.SetColumn(restart, 2);
        Grid.SetColumn(close, 3);
        row.Children.Add(icon);
        row.Children.Add(words);
        row.Children.Add(restart);
        row.Children.Add(close);
        var capsule = new Border { CornerRadius = new CornerRadius(24), Padding = new Thickness(16, 10, 10, 10), BorderThickness = new Thickness(1), Child = row, Background = Faint };
        edges.Add(capsule);
        return Shadowed(capsule, 24, 28, 5, 0.22, HorizontalAlignment.Center, new Thickness(0, 12, 0, 0));
    }

    /// <summary>On the school's sign-in pages, top right: Start Over — the sign-in begun again from Canvas (the Mac's StartOverButton).</summary>
    private FrameworkElement BuildStartOver()
    {
        var pill = Pill("Start Over", "", 30, 15, () => engine.Login.Restart(), material: false);
        pill.ToolTip = "Begin signing in again from the start";
        // (right of the caption buttons' band: the Mac's sits 14 from the window's top right)
        return Shadowed(pill, 15, 20, 3, 0.2, HorizontalAlignment.Right, new Thickness(0, 14, 14 + 3 * MainWindow.CaptionButtonW, 0));
    }

    /// <summary>Minimize, maximize (or restore) and close, as Windows 11 draws them — the window's own are not drawn while the page shows.</summary>
    private void BuildCaptions()
    {
        captions.Children.Add(Caption("", false, () => SystemCommands.MinimizeWindow(main)));
        var max = Caption("", false, () => { if (main.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(main); else SystemCommands.MaximizeWindow(main); });
        maxGlyph = (TextBlock)max.Child;
        captions.Children.Add(max);
        captions.Children.Add(Caption("", true, () => SystemCommands.CloseWindow(main)));
    }

    private Border Caption(string glyph, bool close, Action act)
    {
        var g = new TextBlock { Text = glyph, FontFamily = IconFace, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Width = MainWindow.CaptionButtonW, Height = MainWindow.CaptionButtonH, Background = Faint, Child = g };
        b.MouseEnter += (_, _) =>
        {
            b.Background = close ? new SolidColorBrush(Color.FromRgb(196, 43, 28)) : Wash(0.08);
            if (close) g.Foreground = Brushes.White;
        };
        b.MouseLeave += (_, _) => { b.Background = Faint; g.Foreground = new SolidColorBrush(Ink(0.9)); };
        b.MouseLeftButtonUp += (_, e) => { act(); e.Handled = true; };
        captionButtons.Add((b, g, close));
        return b;
    }

    /// <summary>A bordered button (the Mac's, a capsule of material): its words, a symbol before them; a wash under the
    /// pointer. One with a shadow twin under it (<see cref="Shadowed"/>) leaves the material to the twin.</summary>
    private Border Pill(string label, string? glyph, double h, double fontSize, Action click, bool material = true)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (glyph != null)
        {
            var g = new TextBlock { Text = glyph, FontFamily = IconFace, FontSize = fontSize - 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            inkTexts.Add(g);
            row.Children.Add(g);
        }
        var t = new TextBlock { Text = label, FontFamily = TextFace, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center };
        inkTexts.Add(t);
        row.Children.Add(t);
        var veil = new Border { CornerRadius = new CornerRadius(h / 2) };
        var inside = new Grid();
        inside.Children.Add(veil);
        inside.Children.Add(row);
        var pad = h >= 30 ? 14 : 10;
        var b = new Border { Height = h, CornerRadius = new CornerRadius(h / 2), Padding = new Thickness(pad, 0, pad, 0), BorderThickness = new Thickness(1), Child = inside, VerticalAlignment = VerticalAlignment.Center, Background = Faint };
        if (material) materials.Add(b);
        edges.Add(b);
        b.MouseEnter += (_, _) => veil.Background = Wash(0.06);
        b.MouseLeave += (_, _) => veil.Background = null;
        b.MouseLeftButtonDown += (_, e) => { veil.Background = Wash(0.12); e.Handled = true; };
        b.MouseLeftButtonUp += (_, e) => { veil.Background = null; click(); e.Handled = true; };
        return b;
    }

    /// <summary>A shape with its shadow behind it (on a twin underneath, so the words on it throw none).</summary>
    private FrameworkElement Shadowed(Border shape, double radius, double blur, double depth, double opacity, HorizontalAlignment align, Thickness margin)
    {
        var shadow = new Border { CornerRadius = new CornerRadius(radius), Effect = new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity, Color = Colors.Black } };
        materials.Add(shadow);
        var g = new Grid { HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Top, Margin = margin };
        g.Children.Add(shadow);
        g.Children.Add(shape);
        return g;
    }

    // ---- colours (Theme's, for WPF) ----

    private Color Ink(double a = 1) => dark ? Color.FromArgb((byte)(217 * a), 255, 255, 255) : Color.FromArgb((byte)(217 * a), 0, 0, 0);
    private Color Ink2 => dark ? Color.FromArgb(140, 255, 255, 255) : Color.FromArgb(128, 0, 0, 0);
    private Color Accent => dark ? Color.FromRgb(10, 132, 255) : Color.FromRgb(0, 122, 255);
    private Color Page(double a) => dark ? Color.FromArgb((byte)(255 * a), 22, 22, 24) : Color.FromArgb((byte)(255 * a), 245, 245, 247);
    private Color Edge => dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(16, 0, 0, 0);
    private SolidColorBrush Wash(double a) => new(dark ? Color.FromArgb((byte)(255 * a), 255, 255, 255) : Color.FromArgb((byte)(255 * a), 0, 0, 0));
    /// <summary>All but transparent: a transparent pixel of a layered window lets the pointer through to the page; this one does not.</summary>
    private static readonly SolidColorBrush Faint = new(Color.FromArgb(1, 0, 0, 0));

    private void Paint()
    {
        foreach (var m in materials) m.Background = new SolidColorBrush(Page(0.88));
        foreach (var e in edges) e.BorderBrush = new SolidColorBrush(Edge);
        foreach (var t in inkTexts) t.Foreground = new SolidColorBrush(Ink());
        foreach (var t in ink2Texts) t.Foreground = new SolidColorBrush(Ink2);
        foreach (var t in accentTexts) t.Foreground = new SolidColorBrush(Accent);
        foreach (var (_, g, _) in captionButtons) g.Foreground = new SolidColorBrush(Ink(0.9));
    }
}

using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Simpl.Engine;
using Simpl.Graphics;
using Simpl.Screens;
using Simpl.Support;
using Simpl.UI;

namespace Simpl.Shell;

/// <summary>
/// The window (the Mac's main window): the school's site in the engine's web view underneath, and over it the app's
/// own screens, drawn by Simpl's renderer with Simpl's glass (<see cref="GlassHost"/>). As on the Mac, the window has
/// no title bar of its own: the toolbar floats in the band at the top, the sidebar runs the window's full height, and
/// Windows' own caption buttons (minimize, maximize, close) sit at the top right. The web view is what shows only while
/// signing in (Phase.Web). The first time, the school is chosen first.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly Grid root = new();
    private readonly GlassHost host = new();
    private Engine.Engine? engine;
    private AppView? view;
    private SchoolPicker? picker;
    private LoginLayer? login;
    private LoginOverlay? overlay;
    private HwndSource? source;
    private string? phaseLogged;

    /// <summary>The caption buttons' band: Windows 11's (46 × 32 each, at the top right).</summary>
    public const float CaptionButtonW = 46, CaptionButtonH = 32;
    private bool darkFrame = true;

    public MainWindow()
    {
        Title = "Simpl";
        // (light or dark from the first frame — as asked at launch, else as Windows is set — so nothing flashes dark first)
        var dark = AppView.Dark(null);
        host.Ui.T.Dark = dark;
        Frame(dark);
        var size = (Launch.Get("window-size") ?? "1280x820").Split('x');
        Width = size.Length == 2 && double.TryParse(size[0], out var w) ? w : 1280;
        Height = size.Length == 2 && double.TryParse(size[1], out var h) ? h : 820;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // the window's own frame: no title bar (the toolbar's band is the drag area), edges that size it
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = Ui.Caption,
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
            NonClientFrameEdges = NonClientFrameEdges.None,
        });
        Content = root;
        root.Children.Add(host);
        host.Build = ui =>
        {
            if (view != null)
            {
                // (signing in: the app's own card or "Logging you in" in place of the screens; "Stay logged in?" over them)
                if (login is { Covers: true }) login.Frame(ui); else view.Frame(ui);
                login?.StayAlert(ui);
            }
            else picker?.Frame(ui);
            CaptionButtons(ui);
        };
        host.Ground = () => view?.Ground ?? AppView.DefaultGround(host.Ui.T);
        host.Chrome = ChromeAt;
        host.MaxToggle = () => { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); };
        Shot.Frame = () => host.IsVisible ? host.Pixels() : null;
        SourceInitialized += (_, _) =>
        {
            source = (HwndSource)PresentationSource.FromVisual(this)!;
            Frame(darkFrame);
        };
        StateChanged += (_, _) =>
        {
            // (maximized, a window with its own frame runs past the screen by its edges: pulled back in)
            root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            Phased();
            host.Invalidate();
        };
        Loaded += (_, _) => Begin();
    }

    private void Begin()
    {
        Shot.ArmIfAsked();
        if (Launch.DevBaseUrl is { } dev) { Open(dev.Host); return; }
        if (Prefs.String("school") is { Length: > 0 } school) { Open(school); return; }
        picker = new SchoolPicker(host, Open);
        host.Invalidate();
    }

    /// <summary>The school chosen: its site in the engine underneath, the app's screens over it.</summary>
    private void Open(string schoolHost)
    {
        picker = null;
        if (Launch.DevBaseUrl == null) Prefs.Set("school", schoolHost);
        engine = new Engine.Engine(schoolHost);
        root.Children.Insert(0, engine.Web);
        view = new AppView(engine, host);
        login = new LoginLayer(engine) { ChangeSchool = ChangeSchool };
        overlay = new LoginOverlay(this, engine);
        engine.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Engine.Engine.Phase) || e.PropertyName?.StartsWith("Login", StringComparison.Ordinal) == true) Phased();
            host.Invalidate();
        };
        // what opens over the window, for now as the school's own pages in windows of their own
        engine.ToolRequested += t => { if (WebWindow.Address(engine.BaseUrl, t) is { } u) new WebWindow(u, t.Title, this).Show(); };
        engine.QuizRequested += q => new WebWindow(new Uri(engine.BaseUrl, $"/courses/{q.Course}/quizzes/{q.Quiz}"), q.Title, this).Show();
        engine.AltaRequested += a => new WebWindow(new Uri(engine.BaseUrl, $"/courses/{a.Course}/assignments/{a.Assignment}"), a.Title, this).Show();
        engine.MacmillanRequested += a => new WebWindow(new Uri(engine.BaseUrl, $"/courses/{a.Course}/assignments/{a.Assignment}"), a.Title, this).Show();
        // (the web view's own window is made later, on top: the screens go back over it)
        engine.Web.CoreWebView2InitializationCompleted += (_, _) => Phased();
        Phased();
        engine.Start();
    }

    /// <summary>Another school (the card's "Change school…"): this one forgotten, its engine let go, the picker again.</summary>
    private void ChangeSchool()
    {
        if (engine == null) return;
        Prefs.Remove("school");
        var old = engine;
        engine = null;
        view = null;
        login = null;
        overlay?.Close();
        overlay = null;
        root.Children.Remove(old.Web);
        try { old.Web.Dispose(); } catch { }
        picker = new SchoolPicker(host, Open);
        host.Visibility = Visibility.Visible;
        host.BringToTop();
        host.Invalidate();
    }

    /// <summary>The window's own frame, light or dark with the screens: its background (seen round the page while a page shows) and its title bar's colour for Windows.</summary>
    private void Frame(bool dark)
    {
        darkFrame = dark;
        Background = new SolidColorBrush(dark ? Color.FromRgb(22, 22, 24) : Color.FromRgb(245, 245, 247));
        if (source != null)
        {
            var on = dark ? 1 : 0;
            DwmSetWindowAttribute(source.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        }
    }

    private void Phased()
    {
        if (engine == null) return;
        var covers = engine.Login.Covers;
        var dark = AppView.Dark(engine.Snapshot?.Dark);
        if (dark != darkFrame) Frame(dark);
        // (a page showing: set in from the window's edges, which then size it — the page is a window of its own, and
        // would take the pointer there otherwise)
        var pageShows = engine.Phase == Phase.Web && !covers && WindowState != WindowState.Maximized;
        engine.Web.Margin = pageShows ? new Thickness(6) : new Thickness(0);
        if (Launch.IsDevelopment)
        {
            var now = $"phase {engine.Phase}" + (covers ? $", sign-in {engine.Login.Phase}" : "") + (engine.Login.Handoff ? ", handoff" : "") + (engine.Login.AskStay ? ", stay?" : "");
            if (now != phaseLogged) { phaseLogged = now; Log.Write(now); }
        }
        // (signing in: the school's page is what shows — unless the app's own sign-in card or "Logging you in" covers it;
        // otherwise the app's screens, over it)
        var screens = engine.Phase != Phase.Web || covers;
        host.Visibility = screens ? Visibility.Visible : Visibility.Collapsed;
        if (screens)
        {
            host.BringToTop();
            host.Invalidate();
            if (covers) host.TakeFocus();
        }
        overlay?.Update();
    }

    /// <summary>Where the window's content is on screen (pixels) and how big it is (DIPs), for the windows kept over it.</summary>
    internal (Point Origin, Size Size) ContentBounds() => (root.PointToScreen(new Point(0, 0)), new Size(root.ActualWidth, root.ActualHeight));

    // ---- the window's own chrome: the drag band, the edges, the caption buttons ----

    private float UiWidth => (float)(host.ActualWidth);

    private R MinRect => new(UiWidth - CaptionButtonW * 3, 0, CaptionButtonW, CaptionButtonH);
    private R MaxRect => new(UiWidth - CaptionButtonW * 2, 0, CaptionButtonW, CaptionButtonH);
    private R CloseRect => new(UiWidth - CaptionButtonW, 0, CaptionButtonW, CaptionButtonH);

    /// <summary>What a point (DIPs, in the screens) is to the window: an edge that sizes it (not maximized), its maximize
    /// button (Windows' snap layouts), the band at the top with nothing under the pointer (a drag moves it); else the screens' (0).</summary>
    private int ChromeAt(Vector2 p)
    {
        const int HTCAPTION = 2, HTMAXBUTTON = 9, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        if (WindowState != WindowState.Maximized)
        {
            const float edge = 6;
            bool l = p.X < edge, r = p.X > UiWidth - edge, t = p.Y < edge, b = p.Y > (float)host.ActualHeight - edge;
            if (t && l) return HTTOPLEFT;
            if (t && r) return HTTOPRIGHT;
            if (b && l) return HTBOTTOMLEFT;
            if (b && r) return HTBOTTOMRIGHT;
            if (l) return HTLEFT;
            if (r) return HTRIGHT;
            if (t) return HTTOP;
            if (b) return HTBOTTOM;
        }
        if (MaxRect.Contains(p)) return HTMAXBUTTON;
        return p.Y < Ui.Caption && !host.Ui.HitsAt(p) ? HTCAPTION : 0;
    }

    /// <summary>Minimize, maximize (or restore) and close, as Windows 11 draws them, over everything at the top right.</summary>
    private void CaptionButtons(Ui ui)
    {
        ui.Layer(Ui.Overlay + 5);
        var t = ui.T;
        var ink = t.Ink(0.9f);
        var hoverWash = t.Dark ? new Vortice.Mathematics.Color4(1, 1, 1, 0.08f) : new Vortice.Mathematics.Color4(0, 0, 0, 0.06f);
        var min = ui.Press("win:min", MinRect);
        if (min.Hovered) ui.Fill(MinRect, hoverWash);
        ui.Glyph("", MinRect.CenterX, MinRect.CenterY, 10, ink);
        if (min.Clicked) SystemCommands.MinimizeWindow(this);
        // (the maximize button is hit-tested as the window's own, for snap layouts: its hover and its press come from there)
        if (host.MaxHover) ui.Fill(MaxRect, hoverWash);
        ui.Glyph(WindowState == WindowState.Maximized ? "" : "", MaxRect.CenterX, MaxRect.CenterY, 10, ink);
        var close = ui.Press("win:close", CloseRect);
        if (close.Hovered) ui.Fill(CloseRect, new Vortice.Mathematics.Color4(0.77f, 0.17f, 0.11f, 1));
        ui.Glyph("", CloseRect.CenterX, CloseRect.CenterY, 10, close.Hovered ? new Vortice.Mathematics.Color4(1, 1, 1, 1) : ink);
        if (close.Clicked) SystemCommands.CloseWindow(this);
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

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
    public bool MaxHover { get; private set; }

    public MainWindow()
    {
        Title = "Simpl";
        Background = new SolidColorBrush(Color.FromRgb(22, 22, 24));
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
        Shot.Frame = () => host.IsVisible ? host.Pixels() : null;
        SourceInitialized += (_, _) =>
        {
            source = (HwndSource)PresentationSource.FromVisual(this)!;
            source.AddHook(Hook);
            var hwnd = source.Handle;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        };
        StateChanged += (_, _) =>
        {
            // (maximized, a window with its own frame runs past the screen by its edges: pulled back in)
            root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
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
        login = new LoginLayer(engine);
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

    private void Phased()
    {
        if (engine == null) return;
        var covers = engine.Login.Covers;
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

    /// <summary>Whether a point (DIPs, in the screens) is the window's to answer: the band at the top with nothing under the pointer, the maximize button (Windows' snap layouts), the edges.</summary>
    private bool ChromeAt(Vector2 p)
    {
        if (WindowState != WindowState.Maximized && (p.X < 6 || p.Y < 6 || p.X > UiWidth - 6 || p.Y > host.ActualHeight - 6)) return true;
        if (MaxRect.Contains(p)) return true;
        return p.Y < Ui.Caption && !host.Ui.HitsAt(p);
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
        // (the maximize button is hit-tested by the window itself, for snap layouts: its hover comes from there)
        if (MaxHover) ui.Fill(MaxRect, hoverWash);
        ui.Glyph(WindowState == WindowState.Maximized ? "" : "", MaxRect.CenterX, MaxRect.CenterY, 10, ink);
        var close = ui.Press("win:close", CloseRect);
        if (close.Hovered) ui.Fill(CloseRect, new Vortice.Mathematics.Color4(0.77f, 0.17f, 0.11f, 1));
        ui.Glyph("", CloseRect.CenterX, CloseRect.CenterY, 10, close.Hovered ? new Vortice.Mathematics.Color4(1, 1, 1, 1) : ink);
        if (close.Clicked) SystemCommands.CloseWindow(this);
    }

    /// <summary>The window's messages: the maximize button answered as Windows' own (snap layouts on hover, a click maximizes or restores).</summary>
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2, WM_NCMOUSEMOVE = 0x00A0, WM_NCMOUSELEAVE = 0x02A2, WM_MOUSEMOVE = 0x0200;
        const int HTMAXBUTTON = 9;
        switch (msg)
        {
            case WM_NCHITTEST:
            {
                var p = PointFromScreen(new Point((short)(lParam.ToInt64() & 0xffff), (short)((lParam.ToInt64() >> 16) & 0xffff)));
                var v = new Vector2((float)p.X - (float)root.Margin.Left, (float)p.Y - (float)root.Margin.Top);
                if (MaxRect.Contains(v) && host.IsVisible)
                {
                    handled = true;
                    return (IntPtr)HTMAXBUTTON;
                }
                break;
            }
            case WM_NCMOUSEMOVE:
                SetMaxHover(wParam.ToInt64() == HTMAXBUTTON);
                break;
            case WM_NCMOUSELEAVE:
            case WM_MOUSEMOVE:
                SetMaxHover(false);
                break;
            case WM_NCLBUTTONDOWN:
                if (wParam.ToInt64() == HTMAXBUTTON) { handled = true; return IntPtr.Zero; }
                break;
            case WM_NCLBUTTONUP:
                if (wParam.ToInt64() == HTMAXBUTTON)
                {
                    handled = true;
                    if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this);
                    return IntPtr.Zero;
                }
                break;
        }
        return IntPtr.Zero;
    }

    private void SetMaxHover(bool on)
    {
        if (MaxHover == on) return;
        MaxHover = on;
        host.Invalidate();
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Graphics;
using Simpl.Screens;
using Simpl.Support;

namespace Simpl.Shell;

/// <summary>
/// The window (the Mac's main window): the school's site in the engine's web view underneath, and over it the app's
/// own screens, drawn by Simpl's renderer with Simpl's glass (<see cref="GlassHost"/>). The web view is what shows
/// only while signing in (Phase.Web). The first time, the school is chosen first.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly Grid root = new();
    private readonly GlassHost host = new();
    private Engine.Engine? engine;
    private AppView? view;
    private SchoolPicker? picker;

    public MainWindow()
    {
        Title = "Simpl";
        Background = new SolidColorBrush(Color.FromRgb(20, 20, 22));
        var size = (Launch.Get("window-size") ?? "1280x820").Split('x');
        Width = size.Length == 2 && double.TryParse(size[0], out var w) ? w : 1280;
        Height = size.Length == 2 && double.TryParse(size[1], out var h) ? h : 820;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = root;
        root.Children.Add(host);
        host.Build = ui =>
        {
            if (view != null) view.Frame(ui);
            else picker?.Frame(ui);
        };
        host.Ground = () => view?.Ground ?? AppView.DefaultGround(host.Ui.T);
        Shot.Frame = () => host.IsVisible ? host.Pixels() : null;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
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
        engine.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Engine.Engine.Phase)) Phased();
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
        // (signing in: the school's page is what shows; otherwise the app's screens, over it)
        host.Visibility = engine.Phase == Phase.Web ? Visibility.Collapsed : Visibility.Visible;
        if (host.Visibility == Visibility.Visible)
        {
            host.BringToTop();
            host.Invalidate();
        }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

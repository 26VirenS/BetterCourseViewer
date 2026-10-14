using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Simpl.Shell;

/// <summary>
/// A tool, or a page of the school's own, in a window of its own, signed in as the app is (the engine's web data) — the
/// Mac's ToolWindow, as Windows 11 draws one: Mica round the page, the page's title in the title bar, and Back, Forward
/// and Refresh at the title bar's left (Alt+←, Alt+→, F5), More at its right (Open in browser, Copy link), a thin line
/// under the title bar while a page loads. An external tool opens by the school's one-time launch (the engine's
/// toolLaunch, as the Mac's window asks it): the tool alone, at the top of the window, not inside a second copy of the
/// school's page — the school's own page for it only when the launch cannot be had. A tool that will not open says why
/// in the window, with Try again.
/// </summary>
public sealed class WebWindow : FluentWindow
{
    private const double BarHeight = 48;

    private sealed class LaunchAnswer
    {
        public string Url { get; set; } = "";
        public string? Name { get; set; }
    }

    /// <summary>
    /// The page, on the white a browser gives a page that sets no background of its own: many tools and plain pages set
    /// none, and their default black text cannot be read on the window's dark Mica. The page is told the student prefers
    /// light, too (<see cref="LightPages"/>), so a tool with a dark look of its own does not take it up. (It shows only
    /// once the page is asked for; "Opening …" and the failure are drawn over its row before that.)
    /// </summary>
    private readonly WebView2 web = new() { DefaultBackgroundColor = System.Drawing.Color.White, Visibility = Visibility.Hidden };
    private readonly TitleBar bar;
    private readonly Engine.Engine? engine;
    private readonly ToolLaunch? launch;
    private readonly string name;
    private readonly Button back, forward, refresh, more;
    /// <summary>The line under the title bar while a page loads.</summary>
    private readonly ProgressBar loading = new() { IsIndeterminate = true, Height = 2, MinHeight = 2, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    /// <summary>Over the page's place until it shows: "Opening …", or why it could not open.</summary>
    private readonly ContentControl state = new() { Focusable = false };
    /// <summary>The address the window opens (known at once for a page; the launch's answer for a tool).</summary>
    private Uri? start;
    private bool ready, opening;

    /// <summary>A page of the school's (or another site's), at its address.</summary>
    public WebWindow(Uri url, string title, Window? owner) : this(null, null, url, title, owner) { }

    /// <summary>A launch (a tool, a page): a tool by the school's one-time launch, else at its address.</summary>
    public WebWindow(Engine.Engine engine, ToolLaunch t, Window? owner) : this(engine, t, null, t.Title, owner) { }

    private WebWindow(Engine.Engine? engine, ToolLaunch? launch, Uri? url, string title, Window? owner)
    {
        this.engine = engine;
        this.launch = launch;
        start = url;
        name = string.IsNullOrEmpty(title) ? "Simpl" : title;
        Title = name;
        Owner = owner;
        WindowBackdropType = Appearance.Backdrop;
        ExtendsContentIntoTitleBar = true;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        if (Shot.Asked || Fx.Opaque) SetResourceReference(BackgroundProperty, B.Window);
        Placement.Restore(this, "web", 1100, 760, 480, 360);
        if (owner != null) WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // the title bar: Back, Forward and Refresh at its left (outside the drag area), the page's title, More at its right
        back = BarButton(SymbolRegular.ArrowLeft24, "Back (Alt+Left)", () => { if (web.CoreWebView2?.CanGoBack == true) web.CoreWebView2.GoBack(); });
        forward = BarButton(SymbolRegular.ArrowRight24, "Forward (Alt+Right)", () => { if (web.CoreWebView2?.CanGoForward == true) web.CoreWebView2.GoForward(); });
        refresh = BarButton(SymbolRegular.ArrowClockwise24, "Refresh (F5)", Refresh);
        more = K.IconButton(SymbolRegular.MoreHorizontal24, () => More(), "More", 16, 40);
        more.Height = 36;
        more.VerticalAlignment = VerticalAlignment.Center;
        var lead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 0, 6), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Children = { back, forward, refresh } };
        bar = new TitleBar
        {
            Title = Title, Height = BarHeight, Margin = new Thickness(8 + 3 * 40 + 4, 0, 0, 0), TrailingContent = more,
            Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 16, Height = 16 },
        };
        loading.SetResourceReference(Control.ForegroundProperty, B.Accent);
        loading.Background = Brushes.Transparent;
        // (with Windows' animation effects off, a still line in place of the running one)
        if (!Motion.On) { loading.IsIndeterminate = false; loading.Value = 100; loading.Opacity = 0.6; }
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(bar);
        root.Children.Add(lead);
        // (on the title bar's foot, not over the page: the page is a window of its own, and nothing drawn here goes over it)
        root.Children.Add(loading);
        Grid.SetRow(web, 1);
        root.Children.Add(web);
        Grid.SetRow(state, 1);
        root.Children.Add(state);
        Content = root;
        Paint();

        SizeChanged += (_, _) => Placement.Save(this, "web");
        PreviewKeyDown += OnKey;
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1) { back.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); e.Handled = true; }
            if (e.ChangedButton == MouseButton.XButton2) { forward.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); e.Handled = true; }
        };
        Loaded += (_, _) => Open();
    }

    private static Button BarButton(SymbolRegular icon, string tip, Action click)
    {
        var b = K.IconButton(icon, click, tip, 14, 40);
        b.Height = 36;
        ToolTipService.SetShowOnDisabled(b, true);
        return b;
    }

    /// <summary>The page opened: the web view made (once), the tool's launch asked for (a tool), then its address loaded.</summary>
    private async void Open()
    {
        if (opening) return;
        opening = true;
        state.Content = Waiting();
        try
        {
            if (!ready)
            {
                var env = await Engine.Engine.WebEnvironment();
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                web.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                web.CoreWebView2.DocumentTitleChanged += (_, _) =>
                {
                    if (string.IsNullOrEmpty(web.CoreWebView2.DocumentTitle)) return;
                    Title = web.CoreWebView2.DocumentTitle;
                    bar.Title = Title;
                };
                web.CoreWebView2.DownloadStarting += (_, e) => { e.Handled = true; FilePreview.Downloading(e); };
                web.CoreWebView2.NavigationStarting += (_, _) => { loading.Visibility = Visibility.Visible; Paint(); };
                web.CoreWebView2.NavigationCompleted += (_, _) => { loading.Visibility = Visibility.Collapsed; Paint(); LightPages.Tell(web.CoreWebView2, "web window"); };
                web.CoreWebView2.HistoryChanged += (_, _) => Paint();
                // (the tool's page in light, whatever the app's appearance or Windows': its title bar stays as the app is)
                await LightPages.Apply(web.CoreWebView2);
                ready = true;
            }
            start ??= await Launch();
            if (start == null) throw new InvalidOperationException("Simpl could not find where this tool opens.");
            state.Content = null;
            web.Visibility = Visibility.Visible;
            web.CoreWebView2.Navigate(start.ToString());
        }
        catch (Exception e)
        {
            web.Visibility = Visibility.Hidden;
            state.Content = Failed(e.Message);
        }
        opening = false;
        Paint();
    }

    /// <summary>
    /// Where a launch opens: an address as it is; a tool by the school's one-time launch (the engine's toolLaunch, as the
    /// Mac's window asks it) — the tool on its own page — and, when that cannot be had, the school's own page for it.
    /// </summary>
    private async Task<Uri?> Launch()
    {
        if (launch == null || engine == null) return null;
        if (launch.Args.TryGetValue("page", out _)) return Address(engine.BaseUrl, launch);
        if (launch.Args.ContainsKey("course"))
        {
            try
            {
                var a = await engine.Call<LaunchAnswer>("toolLaunch", launch.Args.ToDictionary(p => p.Key, p => (object)p.Value));
                if (Uri.TryCreate(a.Url, UriKind.Absolute, out var u))
                {
                    if (a.Name is { Length: > 0 } n && Title == name) { Title = n; bar.Title = n; }
                    return u;
                }
            }
            catch (Exception e) { Log.Write($"WebWindow: toolLaunch {e.Message}"); }
        }
        return Address(engine.BaseUrl, launch);
    }

    private void Refresh()
    {
        if (web.Visibility == Visibility.Visible && web.CoreWebView2 != null) web.CoreWebView2.Reload();
        else Open();
    }

    /// <summary>Back and Forward as the page's history stands; Refresh and More once there is a page.</summary>
    private void Paint()
    {
        var core = ready ? web.CoreWebView2 : null;
        back.IsEnabled = core?.CanGoBack == true;
        forward.IsEnabled = core?.CanGoForward == true;
        refresh.IsEnabled = !opening;
        more.IsEnabled = Current() != null;
    }

    /// <summary>The address showing now (or the one to open).</summary>
    private Uri? Current()
    {
        if (ready && web.Visibility == Visibility.Visible && Uri.TryCreate(web.CoreWebView2.Source, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") return u;
        return start;
    }

    /// <summary>More: the page in the browser Windows has, or its address on the clipboard.</summary>
    private void More()
    {
        if (Current() is not { } u) return;
        var menu = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = 200 };
        MenuItem Item(string text, SymbolRegular icon, Action act)
        {
            var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = icon } };
            mi.Click += (_, _) => act();
            return mi;
        }
        menu.Items.Add(Item("Open in browser", SymbolRegular.Globe24, () => Engine.Engine.OpenExternally(u)));
        menu.Items.Add(Item("Copy link", SymbolRegular.Link24, () => { try { Clipboard.SetText(u.ToString()); } catch { /* the clipboard busy */ } }));
        menu.IsOpen = true;
    }

    /// <summary>Alt+← and Alt+→ walk the page's history, F5 (or Ctrl+R) loads it again.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (alt && key == Key.Left && back.IsEnabled) { web.CoreWebView2?.GoBack(); e.Handled = true; }
        else if (alt && key == Key.Right && forward.IsEnabled) { web.CoreWebView2?.GoForward(); e.Handled = true; }
        else if (key == Key.F5 || ctrl && key == Key.R) { Refresh(); e.Handled = true; }
    }

    /// <summary>While the tool's launch is asked for: a spinner and what is opening.</summary>
    private FrameworkElement Waiting()
    {
        var v = K.V(12, K.Spinner(28), K.Text($"Opening {name}…", Ty.Body, B.Text2).Center());
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        return new Grid { Children = { v } };
    }

    /// <summary>Why it could not open, in the window (it stays open), and Try again.</summary>
    private FrameworkElement Failed(string why)
    {
        var again = K.Button("Try again", Open, accent: true);
        again.HorizontalAlignment = HorizontalAlignment.Center;
        var v = K.V(12,
            K.Icon(SymbolRegular.PuzzlePiece24, 40, B.Text3),
            K.Text($"{name} could not open", Ty.Subtitle).Center(),
            K.Wrapped(why, Ty.Body, B.Text2).Center(),
            again.Margin(0, 4, 0, 0));
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.MaxWidth = 440;
        v.Margin = new Thickness(24);
        Motion.FadeIn(v);
        return new Grid { Children = { v } };
    }

    /// <summary>Where a launch leads on the school's site: its own page for the tool (the launch's fallback), or the page.</summary>
    public static Uri? Address(Uri baseUrl, ToolLaunch t)
    {
        var a = t.Args;
        string? path = null;
        if (a.TryGetValue("page", out var page)) return Uri.TryCreate(baseUrl, page, out var u) ? u : null;
        if (a.TryGetValue("course", out var c))
        {
            if (a.TryGetValue("tool", out var tool)) path = $"/courses/{c}/external_tools/{tool}";
            else if (a.TryGetValue("assignment", out var id)) path = $"/courses/{c}/assignments/{id}";
            else if (a.TryGetValue("moduleItem", out var item)) path = $"/courses/{c}/modules/items/{item}";
            else if (a.TryGetValue("url", out var raw)) path = $"/courses/{c}/external_tools/retrieve?url={Uri.EscapeDataString(raw)}";
        }
        return path != null ? new Uri(baseUrl, path) : null;
    }
}

/// <summary>
/// An outside tool's page in light, whatever Simpl's appearance or Windows' (the tool windows, Macmillan Learning's
/// popup): the tools are made for light, and a page with a dark look of its own came out unreadable in dark mode. The
/// page — and each frame of it in a process of its own, a tool framed in the school's page or the tool's own frames — is
/// told the student prefers light (prefers-color-scheme: light), on the white its web view already has; Simpl's own title
/// bar and buttons round it stay as the app is. Told per web view, through the browser's own DevTools protocol: the
/// colour scheme a WebView2 profile prefers is shared by every web view of Simpl's, and the school's pages shown in the
/// app follow the app's appearance.
/// </summary>
internal static class LightPages
{
    private const string Light = "{\"features\":[{\"name\":\"prefers-color-scheme\",\"value\":\"light\"}]}";
    private const string AutoAttach = "{\"autoAttach\":true,\"waitForDebuggerOnStart\":false,\"flatten\":true}";

    /// <summary>The web view's page, and every frame attached to it from now on, told the student prefers light.</summary>
    public static async Task Apply(CoreWebView2 core)
    {
        try
        {
            // (the screenshot suite: a profile that prefers dark, as a Windows in dark mode has it, so the picture shows
            //  the tool's page in light all the same)
            if (Shot.Asked && Launch.Get("web-scheme") == "dark") core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            core.GetDevToolsProtocolEventReceiver("Target.attachedToTarget").DevToolsProtocolEventReceived += async (_, e) =>
            {
                try
                {
                    // (a frame — not a worker, which has no colour scheme to be told)
                    var said = System.Text.Json.Nodes.JsonNode.Parse(e.ParameterObjectAsJson);
                    if (said?["sessionId"]?.GetValue<string>() is not { Length: > 0 } session || said["targetInfo"]?["type"]?.GetValue<string>() is not ("iframe" or "page")) return;
                    await core.CallDevToolsProtocolMethodForSessionAsync(session, "Emulation.setEmulatedMedia", Light);
                    await core.CallDevToolsProtocolMethodForSessionAsync(session, "Target.setAutoAttach", AutoAttach);
                }
                catch (Exception x) { Log.Write("light page: a frame: " + x.Message); }
            };
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", Light);
            await core.CallDevToolsProtocolMethodAsync("Target.setAutoAttach", AutoAttach);
        }
        catch (Exception x) { Log.Write("light page: " + x.Message); }
    }

    /// <summary>(the screenshot suite) Whether the page shown prefers light, in simpl.log, for CI to read.</summary>
    public static async void Tell(CoreWebView2? core, string what)
    {
        if (!Shot.Asked || core == null) return;
        try
        {
            var dark = await core.ExecuteScriptAsync("matchMedia('(prefers-color-scheme: dark)').matches");
            Log.Write($"{what}: the page prefers {(dark == "true" ? "dark" : "light")} (its profile: {core.Profile.PreferredColorScheme})");
        }
        catch (Exception x) { Log.Write($"{what}: {x.Message}"); }
    }
}

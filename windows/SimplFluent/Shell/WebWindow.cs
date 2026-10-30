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
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Shell;

/// <summary>
/// A tool, or a page of the school's own, in a window of its own, signed in as the app is (the engine's web data) — the
/// Mac's ToolWindow, as Windows 11 draws one: Mica round the page, the page's title in the title bar, and Back, Forward
/// and Refresh at the title bar's left (Alt+←, Alt+→, F5), More at its right (Open in browser, Copy link), a thin line
/// under the title bar while a page loads. An external tool opens by the school's one-time launch (the engine's
/// toolLaunch, as the Mac's window asks it): the tool alone, at the top of the window, not inside a second copy of the
/// school's page — the school's own page for it only when the launch cannot be had. A tool that will not open says why
/// in the window, with Try again. A window the page opens (window.open, a link to a new window) opens as another of
/// these, its page in light too and the page that opened it still its opener.
/// </summary>
public sealed class WebWindow : FluentWindow
{
    private const double BarHeight = 48;

    private sealed class LaunchAnswer
    {
        public string Url { get; set; } = "";
        public string? Name { get; set; }
        /// <summary>False when the school would not give the tool's one-time launch and the address is its own page for it.</summary>
        public bool Sessionless { get; set; } = true;
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
    /// <summary>(0.3.91) Developer mode's Record: the page's code, data and a picture saved each time it changes
    /// (<see cref="PageRecorder"/>), and while it records, how many steps it has saved, beside it.</summary>
    private readonly Button rec;
    private readonly TextBlock recSaid = K.Text("", Ty.Caption, B.Critical);
    private PageRecorder? recorder;
    /// <summary>(a picture run's --record: the recording begun by itself once the page has loaded)</summary>
    private bool recordAsked = Simpl.Support.Launch.Flag("record");
    /// <summary>(0.3.92) On Macmillan Learning's pages, (0.3.93) and ALEKS's, Simpl Look: Simpl's look over the page
    /// (<see cref="PageLook"/>), or the page as the tool draws it.</summary>
    private readonly Button lookButton;
    private PageLook? look;
    /// <summary>The line under the title bar while a page loads.</summary>
    private readonly ProgressBar loading = new() { IsIndeterminate = true, Height = 2, MinHeight = 2, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    /// <summary>Over the page's place until it shows: "Opening …", or why it could not open.</summary>
    private readonly ContentControl state = new() { Focusable = false };
    /// <summary>The address the window opens (known at once for a page; the launch's answer for a tool).</summary>
    private Uri? start;
    private bool ready, opening;
    /// <summary>(0.3.24) The school's own page for a tool, opened because its one-time launch could not be had: once it
    /// has loaded, the tool's launch on it is sent here, in this window, rather than left behind Canvas's "Load … in a new
    /// window" button. Null once done (or for anything else).</summary>
    private Uri? loadOnPage;
    /// <summary>(0.3.98) When Canvas's "Load … in a new window" was pressed for the student: the window it opens is shown
    /// in this one instead.</summary>
    private DateTime pressedAt = DateTime.MinValue;
    private int loadTries;
    /// <summary>A window a page asked for, still to be given this one's web view (<see cref="Opened"/>).</summary>
    private (CoreWebView2NewWindowRequestedEventArgs Asked, CoreWebView2Deferral Deferral)? asked;

    /// <summary>A page of the school's (or another site's), at its address.</summary>
    public WebWindow(Uri url, string title, Window? owner) : this(null, null, url, title, owner) { }

    /// <summary>A launch (a tool, a page): a tool by the school's one-time launch, else at its address.</summary>
    public WebWindow(Engine.Engine engine, ToolLaunch t, Window? owner) : this(engine, t, null, t.Title, owner) { }

    /// <summary>A window a page asked for: its web view handed to the page as the new window once it is set to light.</summary>
    private WebWindow(CoreWebView2NewWindowRequestedEventArgs e, CoreWebView2Deferral deferral, Window? owner)
        : this(null, null, Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) ? u : null, u?.Host ?? "", owner) => asked = (e, deferral);

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
        rec = K.IconButton(SymbolRegular.Record24, ToggleRecord, "Record this page", 16, 40);
        rec.Height = 36;
        rec.VerticalAlignment = VerticalAlignment.Center;
        recSaid.VerticalAlignment = VerticalAlignment.Center;
        recSaid.Margin = new Thickness(0, 0, 4, 0);
        lookButton = K.IconButton(SymbolRegular.Sparkle24, ToggleLook, "Simpl Look", 16, 40);
        lookButton.Height = 36;
        lookButton.VerticalAlignment = VerticalAlignment.Center;
        var lead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 0, 6), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Children = { back, forward, refresh } };
        bar = new TitleBar
        {
            Title = Title, Height = BarHeight, Margin = new Thickness(8 + 3 * 40 + 4, 0, 0, 0),
            TrailingContent = new StackPanel { Orientation = Orientation.Horizontal, Children = { recSaid, rec, lookButton, more } },
            Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 16, Height = 16 },
        };
        loading.SetResourceReference(Control.ForegroundProperty, B.Accent);
        loading.Background = Brushes.Transparent;
        // (with Windows' animation effects off, a still line in place of the running one)
        if (!Motion.On) { loading.IsIndeterminate = false; loading.Value = 100; loading.Opacity = 0.6; }
        else K.OnlyWhileShown(loading);
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
        PaintRecord();
        DevMode.Changed += DevChanged;
        Closed += (_, _) =>
        {
            DevMode.Changed -= DevChanged;
            recorder?.Stop(reveal: false); // (the window closed: the recording ends where it is)
        };
    }

    /// <summary>Developer mode turned on or off: Record shown or hidden (and a recording ended with it).</summary>
    private void DevChanged()
    {
        if (!DevMode.On) recorder?.Stop(reveal: false);
        PaintRecord();
    }

    private void ToggleRecord()
    {
        if (recorder == null) { System.Media.SystemSounds.Beep.Play(); return; }
        recorder.Toggle(Title);
    }

    /// <summary>Record (developer mode, once there is a page) — red Stop and "Recording · N saved" while it records.</summary>
    private void PaintRecord()
    {
        var on = recorder?.On == true;
        rec.Visibility = DevMode.On && (on || ready) ? Visibility.Visible : Visibility.Collapsed;
        rec.Content = on ? K.Icon(SymbolRegular.RecordStop24, 16, B.Critical, filled: true) : K.Icon(SymbolRegular.Record24, 16);
        var tip = on
            ? $"Stop recording: {recorder!.Steps} saved, {PageRecorder.Size(recorder.Bytes)} (developer mode)"
            : "Record this page: its code, data and a screenshot each time it changes, into a folder in Downloads (developer mode)";
        rec.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(rec, on ? "Stop recording" : "Record");
        recSaid.Text = on ? $"Recording · {recorder!.Steps} saved" : "";
        recSaid.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
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
                web.CoreWebView2.NavigationCompleted += (_, e) =>
                {
                    loading.Visibility = Visibility.Collapsed;
                    Paint();
                    LightPages.Tell(web.CoreWebView2, "web window");
                    if (e.IsSuccess) LoadToolOnPage();
                    if (recordAsked && e.IsSuccess && DevMode.On && recorder is { On: false } r) { recordAsked = false; _ = r.Start(Title); }
                };
                web.CoreWebView2.HistoryChanged += (_, _) => Paint();
                // (a window the page opens — "Open in a new window", a sign-in's popup — is another of these, in light: left
                //  to the web view, it was a bare window of the browser's, dark when Windows is)
                web.CoreWebView2.NewWindowRequested += (_, e) =>
                {
                    // (0.3.98: the tool's own window, opened by Canvas's Load button pressed for the student: here instead)
                    if (DateTime.UtcNow - pressedAt < TimeSpan.FromSeconds(6) && Uri.TryCreate(e.Uri, UriKind.Absolute, out var to) && to.Scheme.StartsWith("http", StringComparison.Ordinal))
                    {
                        pressedAt = DateTime.MinValue;
                        e.Handled = true;
                        web.CoreWebView2.Navigate(to.ToString());
                        return;
                    }
                    Popup(e);
                };
                // (the tool's page in light, whatever the app's appearance or Windows': its title bar stays as the app is)
                await LightPages.Apply(web.CoreWebView2, "web window");
                recorder = new PageRecorder(web.CoreWebView2);
                recorder.Changed += PaintRecord;
                look = await PageLook.Attach(web.CoreWebView2);
                ready = true;
                PaintRecord();
            }
            if (asked is { } a)
            {
                // (the page that asked fills this web view itself: it is handed over, set to light, before it has loaded)
                asked = null;
                Opened(a.Asked, a.Deferral);
                state.Content = null;
                web.Visibility = Visibility.Visible;
                opening = false;
                Paint();
                return;
            }
            start ??= await Launch();
            if (start == null) throw new InvalidOperationException("Simpl could not find where this tool opens.");
            // (in a picture run, where it opens: CI checks a school's tool opens on its borderless launch address)
            if (Shot.Asked) Log.Write($"web window: opens {start.PathAndQuery}");
            state.Content = null;
            web.Visibility = Visibility.Visible;
            web.CoreWebView2.Navigate(start.ToString());
        }
        catch (Exception e)
        {
            // (a window asked for that could not be made: the page's request let go, so the page is not left waiting)
            if (asked is { } a) { asked = null; a.Deferral.Complete(); }
            web.Visibility = Visibility.Hidden;
            state.Content = Failed(e.Message);
        }
        opening = false;
        Paint();
    }

    /// <summary>A window the page asked for opened as another of these (owned as this one is), the request held until it
    /// has its web view; one that cannot be made lets the request go, and the web view opens it as it would have.</summary>
    private void Popup(CoreWebView2NewWindowRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            new WebWindow(e, deferral, Owner).Show();
        }
        catch (Exception x)
        {
            Log.Write("web window: a window the page asked for: " + x.Message);
            deferral.Complete();
        }
    }

    /// <summary>This window's web view given to the page that asked for a window, and the request let go.</summary>
    private void Opened(CoreWebView2NewWindowRequestedEventArgs e, CoreWebView2Deferral deferral)
    {
        try
        {
            e.NewWindow = web.CoreWebView2;
            e.Handled = true;
        }
        catch (Exception x) { Log.Write("web window: handing over a window: " + x.Message); }
        finally { deferral.Complete(); }
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
                    // (the school's own page for the tool, the one-time launch refused: its launch sent on from it)
                    if (!a.Sessionless) { loadOnPage = u; loadTries = 0; }
                    return u;
                }
            }
            catch (Exception e) { Log.Write($"WebWindow: toolLaunch {e.Message}"); }
            // (the school's own page for the tool: its launch sent on from it once it loads, LoadToolOnPage)
            var page = Address(engine.BaseUrl, launch);
            loadOnPage = page;
            loadTries = 0;
            return page;
        }
        return Address(engine.BaseUrl, launch);
    }

    /// <summary>
    /// (0.3.24) Canvas's form that launches a tool, on the school's page for it: a tool Canvas opens "in a new window"
    /// waits there behind a "Load … in a new window" button, its form aimed at a new window. That form is sent here
    /// instead, in this window — a page of its own already, framed by nothing — so the tool opens at once, as its one-time
    /// launch would have opened it. Sent with the form's own fields, as the button sends them; only on the page this
    /// window opened for the tool (not one the student moves to after), and once. (0.3.98) Found too where real Canvas
    /// puts it: in a frame of Canvas's own on the page, or aimed at a window of its own name (Canvas's window launch,
    /// <c>data-tool-launch-type="window"</c>) — sent to the top of this window; and with no form, Canvas's "Load … in a new
    /// window" button pressed, the window it opens shown here. Any page of the school's (not only the very address
    /// asked for: Canvas may move on to another first), for the first few pages.
    /// </summary>
    internal const string SendToolForm = """
        (function () {
          var find = function (doc, depth) {
            var f = doc.getElementById('tool_form') || doc.querySelector('form[data-tool-launch-type]');
            if (f && f.action) return f;
            if (depth > 2) return null;
            var frames = doc.querySelectorAll('iframe');
            for (var i = 0; i < frames.length; i++) {
              try { var d = frames[i].contentDocument, g = d && find(d, depth + 1); if (g) return g; } catch (e) { /* another site's frame */ }
            }
            return null;
          };
          var f = find(document, 0);
          if (!f) {
            var bs = document.querySelectorAll('button, a.btn, [role="button"]');
            for (var j = 0; j < bs.length; j++) {
              var w = (bs[j].textContent || '').replace(/\s+/g, ' ').trim();
              if (/^load\b.*\bin a new (window|tab)$/i.test(w) && !bs[j].getAttribute('data-simpl-sent')) { bs[j].setAttribute('data-simpl-sent', '1'); bs[j].click(); return 'pressed'; }
            }
            return 'none';
          }
          if (f.getAttribute('data-simpl-sent')) return 'sent';
          var target = (f.getAttribute('target') || '').toLowerCase();
          var kind = (f.getAttribute('data-tool-launch-type') || '').toLowerCase();
          var windowed = kind === 'window' || kind === 'new_window' || !!f.querySelector('.load_tab') || /in a new (window|tab)/i.test(f.textContent || '');
          if (target && target !== '_blank' && target !== '_top' && target !== '_self' && !windowed) return 'framed';
          f.setAttribute('data-simpl-sent', '1');
          f.setAttribute('target', '_top');
          HTMLFormElement.prototype.submit.call(f);
          return 'sent';
        })()
        """;

    private async void LoadToolOnPage()
    {
        if (loadOnPage is not { } page || web.CoreWebView2 is not { } core) return;
        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var here) || !here.Host.Equals(page.Host, StringComparison.OrdinalIgnoreCase))
        {
            // (the page went elsewhere first — the school's sign-in: tried again when it comes back, a few times at most)
            if (++loadTries > 4) loadOnPage = null;
            return;
        }
        try
        {
            var said = (await core.ExecuteScriptAsync(SendToolForm)).Trim('"');
            // (no form on the page yet: Canvas may still be drawing it; another look a moment on, a few times, then left)
            if (said == "none" && ++loadTries <= 8) { await Task.Delay(700); if (loadOnPage != null) LoadToolOnPage(); return; }
            loadOnPage = null;
            if (said == "pressed") pressedAt = DateTime.UtcNow;
            Log.Write($"web window: the school's page for the tool, its launch {said switch { "sent" => "sent on in this window", "pressed" => "pressed (Canvas's Load button), opened in this window", "framed" => "framed in the page as it is", _ => "not found" }}");
        }
        catch (Exception x) { loadOnPage = null; Log.Write("web window: sending the tool's launch: " + x.Message); }
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
        PaintLook();
    }

    private async void ToggleLook()
    {
        if (look == null || PageLook.For(Current()) is not { } k) return;
        await look.Set(k, !look.On(k));
        PaintLook();
    }

    /// <summary>Simpl Look, on a page of Macmillan's or ALEKS's: lit while that tool's look is on.</summary>
    private void PaintLook()
    {
        var k = PageLook.For(Current());
        var on = k != null && look?.On(k) != false;
        lookButton.Visibility = look != null && k != null ? Visibility.Visible : Visibility.Collapsed;
        lookButton.Content = K.Icon(SymbolRegular.Sparkle24, 16, on ? B.Accent : B.Text, filled: on);
        var title = k?.Title ?? "the tool";
        lookButton.ToolTip = on
            ? $"Simpl's look is on: {title}'s page laid out as an Alta question is. Click to show it as {title} draws it."
            : $"Show {title}'s page in Simpl's look, laid out as an Alta question is";
        System.Windows.Automation.AutomationProperties.SetName(lookButton, on ? "Simpl Look, on" : "Simpl Look, off");
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
/// An outside tool's page in light, whatever Simpl's appearance or Windows' (the tool windows, Macmillan
/// Learning's page among them): the tools are made for light, and a page with a dark look of its own came out unreadable in dark mode. The
/// page — and each frame of it in a process of its own, a tool framed in the school's page or the tool's own frames — is
/// told the student prefers light (prefers-color-scheme: light), on the white its web view already has; Simpl's own title
/// bar and buttons round it stay as the app is. Told per web view, through the browser's own DevTools protocol: the
/// colour scheme a WebView2 profile prefers is shared by every web view of Simpl's, and the school's pages shown in the
/// app follow the app's appearance.
/// </summary>
internal static class LightPages
{
    private const string Light = "{\"features\":[{\"name\":\"prefers-color-scheme\",\"value\":\"light\"}]}";
    private const string AutoAttach = "{\"autoAttach\":true,\"waitForDebuggerOnStart\":true,\"flatten\":true}";

    /// <summary>
    /// The web view's page, and every frame attached to it from now on, told the student prefers light. A frame in a
    /// process of its own is held at its start until it has been told (waitForDebuggerOnStart), then let go: let run at
    /// once, a tool's script that reads the colour scheme once as it starts could read dark before it was told. Every
    /// target held is let go whatever happens (a worker too, which has no colour scheme to be told). In the screenshot
    /// suite each of the page's frames says in simpl.log, after <paramref name="what"/>, which it prefers once loaded.
    /// </summary>
    public static async Task Apply(CoreWebView2 core, string what)
    {
        try
        {
            // (the screenshot suite: a profile that prefers dark, as a Windows in dark mode has it, so the picture shows
            //  the tool's page in light all the same)
            if (Shot.Asked && Launch.Get("web-scheme") == "dark") core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            if (Shot.Asked) core.FrameCreated += (_, e) => e.Frame.NavigationCompleted += (_, _) => TellFrame(e.Frame, what);
            core.GetDevToolsProtocolEventReceiver("Target.attachedToTarget").DevToolsProtocolEventReceived += async (_, e) =>
            {
                string? session = null;
                var waiting = false;
                try
                {
                    var said = System.Text.Json.Nodes.JsonNode.Parse(e.ParameterObjectAsJson);
                    session = said?["sessionId"]?.GetValue<string>();
                    waiting = said?["waitingForDebugger"]?.GetValue<bool>() == true;
                    if (session is not { Length: > 0 } || said!["targetInfo"]?["type"]?.GetValue<string>() is not ("iframe" or "page")) return;
                    await core.CallDevToolsProtocolMethodForSessionAsync(session, "Emulation.setEmulatedMedia", Light);
                    await core.CallDevToolsProtocolMethodForSessionAsync(session, "Target.setAutoAttach", AutoAttach);
                }
                catch (Exception x) { Log.Write("light page: a frame: " + x.Message); }
                finally
                {
                    if (waiting && session is { Length: > 0 })
                    {
                        try { await core.CallDevToolsProtocolMethodForSessionAsync(session, "Runtime.runIfWaitingForDebugger", "{}"); }
                        catch (Exception x) { Log.Write("light page: a frame let go: " + x.Message); }
                    }
                }
            };
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", Light);
            await core.CallDevToolsProtocolMethodAsync("Target.setAutoAttach", AutoAttach);
        }
        catch (Exception x) { Log.Write("light page: " + x.Message); }
    }

    /// <summary>(the screenshot suite) Whether a frame of the page prefers light, in simpl.log, for CI to read.</summary>
    private static async void TellFrame(CoreWebView2Frame frame, string what)
    {
        try
        {
            var dark = await frame.ExecuteScriptAsync("matchMedia('(prefers-color-scheme: dark)').matches");
            Log.Write($"{what}: a frame prefers {(dark == "true" ? "dark" : "light")}");
        }
        catch (Exception x) { Log.Write($"{what}: a frame: {x.Message}"); }
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

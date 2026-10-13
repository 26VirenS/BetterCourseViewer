using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

/// <summary>
/// The school's own page for a place the app has no screen of its own for yet, inside the window — signed in as the app
/// is (the engine's web data) — with a bar over it that says so and opens it in a window of its own or the browser.
/// Links that lead to one of the app's own screens open that screen instead.
/// </summary>
public sealed class WebScreen : Screen
{
    private static readonly List<WeakReference<WebScreen>> All = new();
    private static bool covered;
    private readonly Uri? url;
    private readonly Func<string> title;
    private readonly Grid root = new();
    private readonly Grid holder = new();
    private WebView2? web;
    /// <summary>No update goes in while the page shows (Support/Updates.cs): what the student is doing on it is the school's.</summary>
    private Updates.Holding? updateHold;

    public WebScreen(Engine.Engine engine, Uri? url, string title) : this(engine, url, () => title) { }

    public WebScreen(Engine.Engine engine, Uri? url, Func<string> title) : base(engine)
    {
        this.url = url;
        this.title = title;
        All.Add(new WeakReference<WebScreen>(this));
    }

    public override string Title => title();
    public override bool Scrolls => false;

    public override FrameworkElement? Commands
    {
        get
        {
            if (url == null) return null;
            var open = K.Button("Open in Browser", () => Simpl.Engine.Engine.OpenExternally(url), icon: SymbolRegular.Open24);
            var reload = K.IconButton(SymbolRegular.ArrowClockwise24, () => web?.Reload(), "Reload");
            return K.H(8, reload, open);
        }
    }

    /// <summary>A sheet or the pane over the window: the pages (each a window of its own) step aside meanwhile.</summary>
    public static void SheetOver(bool over)
    {
        covered = over;
        foreach (var w in All.ToList())
        {
            if (!w.TryGetTarget(out var s)) { All.Remove(w); continue; }
            if (s.web != null) s.web.Visibility = over ? Visibility.Hidden : Visibility.Visible;
        }
    }

    public override void Refresh() { if (web == null) Render(); }

    protected override FrameworkElement Build()
    {
        if (url == null) return K.LoadState("This page has no address.", () => { });
        var note = K.Columns("auto, *",
            K.Icon(SymbolRegular.Globe24, 16, B.Text2).Margin(0, 0, 10, 0).Col(0),
            K.Text($"{engine.LmsName}’s own page — Simpl’s screen for it is on its way.", Ty.Caption, B.Text2).Col(1));
        note.Margin = new Thickness(40, 0, 40, 10);
        root.RowDefinitions.Clear();
        root.Children.Clear();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(note.Row(0));
        var frame = new Border { Child = holder, CornerRadius = new CornerRadius(8, 0, 0, 0), BorderThickness = new Thickness(1, 1, 0, 0), ClipToBounds = true };
        frame.Stroke(B.CardStroke, 1);
        frame.BorderThickness = new Thickness(1, 1, 0, 0);
        root.Children.Add(frame.Row(1));
        if (web == null) Start();
        return root;
    }

    private async void Start()
    {
        updateHold ??= Updates.Hold($"a page of {engine.LmsName}’s", $"when you leave this {engine.LmsName} page");
        web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
        holder.Children.Add(web);
        if (covered) web.Visibility = Visibility.Hidden;
        try
        {
            var env = await Simpl.Engine.Engine.WebEnvironment();
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, e) => { e.Handled = true; engine.GoTo(e.Uri, ""); };
            core.DownloadStarting += (_, e) => { e.Handled = true; Simpl.Engine.FilePreview.Downloading(e); };
            core.NavigationStarting += (_, e) =>
            {
                // a link to one of the app's own screens opens that screen
                if (!e.IsUserInitiated || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) || !engine.IsSchool(u)) return;
                if (engine.NativeRoute(e.Uri, "") is { } || engine.TabName(e.Uri) is { } || engine.QuizLaunchFor(e.Uri, "") is { })
                {
                    e.Cancel = true;
                    engine.OpenWeb(e.Uri, "");
                }
            };
            core.Navigate(url!.ToString());
        }
        catch (Exception e)
        {
            Log.Write("web screen: " + e.Message);
            holder.Children.Clear();
            holder.Children.Add(K.LoadState("The page could not open: " + e.Message, () => { }));
        }
    }

    public override void Hide()
    {
        base.Hide();
        updateHold?.Dispose();
        updateHold = null;
        try { web?.Dispose(); } catch { }
        web = null;
        holder.Children.Clear();
    }
}

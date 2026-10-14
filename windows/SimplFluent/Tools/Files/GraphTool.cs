using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// The graphing calculator, as the web's and the Mac's (GraphTool.swift): Desmos, embedded, not reimplemented — their
// calculator page in a web view of its own. Each web view is kept for as long as the app runs, so a graph survives the
// tool closing or its pin's flyout folding. Where Desmos cannot load, a plain message and the way out to desmos.com
// instead of a blank pane.

#pragma warning disable CA2255
internal static class GraphRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("graph", c => new GraphTool(c), c => new GraphCompact(c));
}
#pragma warning restore CA2255

/// <summary>
/// A kept Desmos (the Mac's GraphWeb): its web view, made — and Desmos asked for — the first time it is shown, and
/// whether it loaded. The whole tool's is Windows' own web view (a window of its own inside the app's, so the fastest);
/// the pin's flyout's is WebView2's composition control, the one kind a see-through flyout can show. One web view shows
/// in one place at a time: the pane shown last holds it, and gives it back to the one before when it goes.
/// </summary>
internal sealed class GraphWeb
{
    public static readonly Uri Desmos = new("https://www.desmos.com/calculator");
    public static readonly GraphWeb Full = new(compact: false);
    public static readonly GraphWeb Mini = new(compact: true);

    private static Task<CoreWebView2Environment>? environment;
    private readonly bool compact;
    private readonly List<GraphPane> holders = new();
    private FrameworkElement? view;
    private CoreWebView2? core;
    private bool starting;

    private GraphWeb(bool compact) => this.compact = compact;

    public bool Loading { get; private set; } = true;
    public bool Failed { get; private set; }
    public bool Compact => compact;

    /// <summary>Loaded, failed, or covered (the panes show it, or why not).</summary>
    public event Action? Changed;

    /// <summary>The web view, made the first time it is asked for.</summary>
    public FrameworkElement View
    {
        get
        {
            if (view != null) return view;
            if (compact)
            {
                var w = new WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.White };
                view = w;
                Start(e => w.EnsureCoreWebView2Async(e), () => w.CoreWebView2);
            }
            else
            {
                var w = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.White };
                view = w;
                Start(e => w.EnsureCoreWebView2Async(e), () => w.CoreWebView2);
            }
            return view;
        }
    }

    /// <summary>
    /// Desmos asked for in the app's web browser (the engine's own data and settings, as the Mac's default data store):
    /// its links to elsewhere (its help, a sign-in) open in the student's browser.
    /// </summary>
    private async void Start(Func<CoreWebView2Environment, Task> ensure, Func<CoreWebView2?> get)
    {
        if (starting) return;
        starting = true;
        try
        {
            environment ??= Engine.Engine.WebEnvironment();
            await ensure(await environment);
            core = get();
            if (core == null) throw new InvalidOperationException("The web view did not start.");
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess) Finished();
                else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) CouldNotLoad();
            };
            core.NavigationStarting += (_, e) =>
            {
                if (!e.IsUserInitiated || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return;
                if (u.Host.EndsWith("desmos.com", StringComparison.OrdinalIgnoreCase)) return;
                e.Cancel = true;
                Engine.Engine.OpenExternally(u);
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)) Engine.Engine.OpenExternally(u);
            };
            core.Navigate(Desmos.ToString());
        }
        catch (Exception e)
        {
            Log.Write("graph: " + e.Message);
            CouldNotLoad();
        }
        finally
        {
            starting = false;
        }
    }

    /// <summary>Desmos asked for again: an empty graph (New Graph, Try Again).</summary>
    public void Reload()
    {
        Failed = false;
        Loading = true;
        Changed?.Invoke();
        if (core != null) core.Navigate(Desmos.ToString());
        else if (view is WebView2 w) Start(e => w.EnsureCoreWebView2Async(e), () => w.CoreWebView2);
        else if (view is WebView2CompositionControl c) Start(e => c.EnsureCoreWebView2Async(e), () => c.CoreWebView2);
    }

    private void Finished()
    {
        Loading = false;
        Failed = false;
        Changed?.Invoke();
        Shot.Moved();
    }

    private void CouldNotLoad()
    {
        Loading = false;
        Failed = true;
        Changed?.Invoke();
    }

    /// <summary>A pane shown: the web view moves into it (and has its pace again).</summary>
    public void Hold(GraphPane p)
    {
        holders.Remove(p);
        holders.Add(p);
        Place();
        Level(low: false);
    }

    /// <summary>A pane gone: the web view goes back to the one shown before it, if one still shows; shown nowhere, the
    /// graph is kept (it survives the tool closing) but its page keeps to little memory until it is shown again.</summary>
    public void Release(GraphPane p)
    {
        if (!holders.Remove(p)) return;
        p.Dispatcher.BeginInvoke(Place, System.Windows.Threading.DispatcherPriority.Loaded);
        if (holders.Count == 0) Level(low: true);
    }

    /// <summary>The window away a while (Support.MemoryTidy): both kept graphs keep to little memory; back, the one
    /// shown has its pace again.</summary>
    public static void Lean(bool on)
    {
        foreach (var g in new[] { Full, Mini }) g.Level(on || g.holders.Count == 0);
    }

    private void Level(bool low)
    {
        if (core == null) return;
        try { core.MemoryUsageTargetLevel = low ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal; }
        catch (Exception e) { Log.Write("graph: " + e.Message); }
    }

    private void Place()
    {
        if (holders.LastOrDefault() is not { } top) return;
        var v = View;
        if (ReferenceEquals(top.Host.Child, v)) return;
        FileKit.Detach(v);
        top.Host.Child = v;
        top.Paint();
    }
}

/// <summary>
/// Desmos, or why it is not here (the Mac's GraphPane): the web view while it loaded, a spinner while it loads, and
/// "Desmos could not load" with Try Again and Open desmos.com where it could not. The whole tool's web view, a window of
/// its own, steps aside while a sheet is over the window (as the school's pages do).
/// </summary>
internal sealed class GraphPane : Grid
{
    private readonly GraphWeb web;
    private readonly FrameworkElement spinner = K.Spinner(28);
    private readonly ContentControl failed = new() { Focusable = false, Visibility = Visibility.Collapsed };
    private SheetHost? sheets;

    public Border Host { get; } = new() { Background = Brushes.White };

    public GraphPane(GraphWeb web)
    {
        this.web = web;
        Children.Add(Host);
        Children.Add(spinner);
        Children.Add(failed);
        failed.Content = Unavailable();
        Loaded += (_, _) =>
        {
            web.Changed -= Paint;
            web.Changed += Paint;
            if (!web.Compact && Window.GetWindow(this) is ISheetWindow w)
            {
                sheets = w.Sheets;
                sheets.OpenChanged -= SheetChanged;
                sheets.OpenChanged += SheetChanged;
            }
            web.Hold(this);
            Paint();
            Settle();
        };
        Unloaded += (_, _) =>
        {
            web.Changed -= Paint;
            if (sheets != null) sheets.OpenChanged -= SheetChanged;
            sheets = null;
            web.Release(this);
        };
    }

    private void SheetChanged(bool _) => Paint();

    /// <summary>
    /// The whole tool's web view, a window of its own, follows the pane only when the page is laid out again — not while
    /// what holds it slides or grows into place (a sheet arriving, the page's entrance): laid out again once those are done.
    /// </summary>
    private void Settle()
    {
        if (web.Compact) return;
        var n = 0;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        t.Tick += (_, _) =>
        {
            n++;
            if (!IsLoaded || n > 6) { t.Stop(); return; }
            if (n % 2 == 1) Host.Margin = new Thickness(0.01);
            else Host.Margin = new Thickness(0);
        };
        t.Start();
    }

    /// <summary>Over this pane: a dialog over its popup that it is not in (the whole tool is in a popup of its own,
    /// Shell/FloatPopup.cs, so nothing of Simpl's window can come over it).</summary>
    private bool Covered()
    {
        if (web.Compact) return false;
        if (sheets is not { IsOpen: true } s) return false;
        // (in the sheet that is up, it is on top)
        for (DependencyObject? d = this; d != null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, s)) return false;
        return true;
    }

    public void Paint()
    {
        var holds = ReferenceEquals(Host.Child, web.View);
        spinner.Visibility = web.Loading && !web.Failed ? Visibility.Visible : Visibility.Collapsed;
        failed.Visibility = web.Failed ? Visibility.Visible : Visibility.Collapsed;
        // (the whole tool's web view is a window of its own, drawn over everything here: it waits out of sight while
        // Desmos loads, so the spinner shows, and while something is over it)
        var hide = web.Failed || Covered() || (!web.Compact && web.Loading);
        Host.Visibility = hide ? Visibility.Hidden : Visibility.Visible;
        if (!holds) Host.Visibility = Visibility.Hidden;
    }

    private FrameworkElement Unavailable()
    {
        var again = K.Button("Try again", () => web.Reload(), accent: true);
        var open = K.Button("Open desmos.com", () => Engine.Engine.OpenExternally(GraphWeb.Desmos));
        var v = K.V(12,
            K.Icon(SymbolRegular.WifiWarning24, 40, B.Text3),
            K.Text("Desmos could not load", Ty.Subtitle).Center(),
            K.Wrapped("Check your connection, or open desmos.com in the browser instead.", Ty.Body, B.Text2).Center(),
            K.H(8, again, open).Align(HorizontalAlignment.Center));
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.MaxWidth = 360;
        v.Margin = new Thickness(16);
        var g = new Grid { Children = { v } };
        g.Bg(B.Card);
        return g;
    }
}

/// <summary>Graphing calculator (the Mac's GraphTool): Desmos, filling the page under the tool's heading.</summary>
internal sealed class GraphTool : Grid
{
    public GraphTool(ToolContext c)
    {
        Margin = new Thickness(40, 4, 40, 24);
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 240 });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // (the web view is a window of its own and keeps its square corners: it sits a little inside the rounded edge)
        var frame = new Border { Child = new GraphPane(GraphWeb.Full), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Background = Brushes.White, SnapsToDevicePixels = true };
        frame.Stroke(B.CardStroke);
        Children.Add(frame.Row(0));
        var newGraph = FileKit.Link("New graph", () => GraphWeb.Full.Reload(), SymbolRegular.ArrowCounterclockwise24, "Start again with an empty graph");
        var browser = FileKit.Link("Open in browser", () => Engine.Engine.OpenExternally(GraphWeb.Desmos), SymbolRegular.Globe24, "Open desmos.com in your browser");
        var foot = K.Columns("auto, *, auto, auto",
            K.Text("Powered by Desmos", Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0),
            newGraph.Col(2), browser.Margin(4, 0, 0, 0).Col(3));
        foot.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(foot.Row(1));
    }
}

/// <summary>The graphing calculator's pin, opened (the Mac's GraphCompact): a small Desmos, portrait, that keeps its graph while folded.</summary>
internal sealed class GraphCompact : Border
{
    public GraphCompact(ToolContext c)
    {
        Height = 460;
        Child = new GraphPane(GraphWeb.Mini);
        CornerRadius = new CornerRadius(6);
        Background = Brushes.White;
        SizeChanged += (_, e) => Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 6, 6);
    }
}

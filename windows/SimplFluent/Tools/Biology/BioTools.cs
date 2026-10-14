using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// The biology toolkit's place among the tools (spec §2, §3.2), the Mac's BioTools.swift: its ten tools' names, lines,
// symbols and colours (the Tools page's "Biology" section shows them, ToolsScreen.cs); each tool's registration — one
// line in the tool's own file, BioTools.Register — with its pages in the header (the Periodic Table's pattern,
// remembered per tool); what a launch asks of a tool (--tool cell --tool-state plant:cutaway, for the screenshot suite);
// the hand-off from one tool to another; and the self-check in the log (--bio-check). Each tool's view is in a file of
// its own (CellExplorerTool.cs …): a tool's builder changes that file, never this one.

/// <summary>A page of a biology tool, picked in its header: its id (what the tool switches on, and what a launch's state
/// names: "plant"), its title, its symbol and its tooltip.</summary>
public sealed record BioPage(string Id, string Title, SymbolRegular Symbol, string Help = "");

/// <summary>The biology tools as the app knows them.</summary>
public static class BioTools
{
    /// <summary>The ten, in the Tools page's order (spec §2.1: names, lines, symbols and tile colours). The Cell Explorer's
    /// symbol is GridCircles24, not §2.1's ShapeOrganic24: that one's code is above U+FFFF, which WPF-UI 4.3 does not draw
    /// (README, Icons) — a grid of circles, as the Mac's fallback circle.hexagongrid is.</summary>
    public static readonly ToolsScreen.Kind[] Kinds =
    {
        new("cell", "Cell Explorer", "Animal, plant and bacterial cells in 3D.", SymbolRegular.GridCircles24, "#2fb67c"),
        new("dna", "DNA & RNA", "The double helix, nucleotides and RNA, in 3D.", SymbolRegular.Ribbon24, "#7d5cf5"),
        new("gene", "Gene Expression", "Transcribe and translate a gene, step by step.", SymbolRegular.Translate24, "#f2994a"),
        new("biomol", "Biomolecule Builder", "Build proteins, nucleic acids, sugars and lipids.", SymbolRegular.PuzzleCube24, "#e8508a"),
        new("cross", "Genetics Crosses", "Punnett squares, probability and chi-square.", SymbolRegular.Grid24, "#3b82f6"),
        new("divide", "Cell Division", "Mitosis and meiosis, stage by stage, with counts.", SymbolRegular.ArrowSplit24, "#14b8a6"),
        new("popgen", "Hardy–Weinberg", "Allele frequencies, equilibrium and evolution.", SymbolRegular.PeopleCommunity24, "#8bbf3f"),
        new("biolab", "Bio Lab Calculator", "Dilutions, microscopy, stats and the AP formula sheet.", SymbolRegular.Beaker24, "#c27c4a"),
        new("enzyme", "Enzyme Lab", "How temperature, pH and inhibitors change rate.", SymbolRegular.Flash24, "#d9a406"),
        new("energy", "Photosynthesis & Respiration", "Where each step happens and what goes in and out.", SymbolRegular.LeafTwo24, "#65a30d"),
    };

    /// <summary>One of the biology toolkit's ten (they have a section of their own on the Tools page).</summary>
    public static bool IsBiology(string key) => Kinds.Any(k => k.Key == key);

    public static ToolsScreen.Kind? Kind(string key) => Kinds.FirstOrDefault(k => k.Key == key);

    /// <summary>The short name for pins and tooltips (spec §2.1: Cells, DNA, Genes …), as ToolsCenter.ShortName has it.</summary>
    public static string ShortName(string key) => ToolsCenter.ShortName(key);

    /// <summary>The pin flyout's width (spec §3.2: gene 420, cross 400, biolab 380, popgen 360, the rest 330), as
    /// ToolsCenter.FlyoutWidth has it.</summary>
    public static double FlyoutWidth(string key) => ToolsCenter.FlyoutWidth(key);

    private static readonly Dictionary<string, IReadOnlyList<BioPage>> pages = new();

    /// <summary>A tool's pages, as its own file registered them (none: a tool of one page).</summary>
    public static IReadOnlyList<BioPage> Pages(string key) => pages.GetValueOrDefault(key) ?? Array.Empty<BioPage>();

    /// <summary>
    /// A biology tool registered (from a module initializer in its own file): its whole view, made with what the launch
    /// asked of it (null, mostly); its pages, shown in its header when there are two or more; and its quick version for
    /// its pin's flyout (null: its tile, its line and Open, BioQuickOpen). A launch's page is shown before the view is made.
    /// </summary>
    public static void Register(string key, Func<ToolContext, BioLaunchState?, FrameworkElement> full, IReadOnlyList<BioPage>? toolPages = null,
        Func<ToolContext, BioLaunchState?, FrameworkElement>? quick = null)
    {
        pages[key] = toolPages ?? Array.Empty<BioPage>();
        ToolRegistry.Register(key,
            c =>
            {
                SelfCheckIfAsked();
                var state = BioLaunchState.Take(key, quick: false);
                if (state?.Page is { } p) BioNav.Set(key, p, remember: false);
                return full(c, state);
            },
            c =>
            {
                var state = BioLaunchState.Take(key, quick: true);
                return quick != null ? quick(c, state) : new BioQuickOpen(c);
            },
            Pages(key).Count > 1 ? c => new BioPages(c) : null);
    }

    /// <summary>
    /// Another tool opened with something handed to it (spec §3.2: "Open in Gene Expression" hands over
    /// ["sequence"] = "ATG…"; the tool takes it once with c.TakeInfo("sequence")): on the Tools page, the other in this
    /// one's place ("Tools › Gene Expression", Back returning here); elsewhere, the sheet or flyout this one is in closed,
    /// and the other opened over the window.
    /// </summary>
    public static void Open(ToolContext from, string key, Dictionary<string, string>? info = null)
    {
        var engine = from.Engine;
        var owner = from.Owner;
        if (from.InPage)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => ToolsCenter.Show(key, engine, null, info), DispatcherPriority.Background);
            return;
        }
        from.Close();
        Application.Current?.Dispatcher.BeginInvoke(() => ToolsCenter.PopUp(key, engine, owner, null, info), DispatcherPriority.Background);
    }

    private static int selfChecked;

    /// <summary>(--bio-check) The self-check's lines in the log, once a launch, off the UI thread: `bio-check name value
    /// OK|FAIL` and a summary (spec §10.1).</summary>
    public static void SelfCheckIfAsked()
    {
        if (!Launch.Flag("bio-check") || Interlocked.Exchange(ref selfChecked, 1) == 1) return;
        Shot.Began();
        Task.Run(() =>
        {
            try
            {
                var r = BioSelfCheck.Run();
                foreach (var l in r.Lines) Log.Write(l);
                Log.Write(r.Summary);
            }
            catch (Exception e) { Log.Write("bio-check could not run: " + e.Message); }
        }).ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(Shot.Ended), TaskScheduler.Default);
    }
}

// ---- what a launch asks of a tool ----

/// <summary>
/// What a launch asked of a biology tool (spec §9 Screenshots and launch flags): <c>--tool &lt;key&gt; --tool-state &lt;state&gt;</c>,
/// the state free-form, its parts split at ":" — <c>plant:cutaway</c>, <c>helix:style=atoms:seq=ATGC</c>. A first part that
/// names one of the tool's pages opens that page (BioTools.Register does it); the rest is the tool's to read:
/// <c>Has("cutaway")</c>, <c>Value("style")</c>. The specification's own flags read the same way: <c>--bio-seq ATGC</c> is
/// <c>Value("seq")</c>, <c>--bio-step 4</c> is <c>Value("step")</c> (any <c>--bio-&lt;name&gt; &lt;value&gt;</c>), and
/// <c>--bio-page</c> picks the page. Only the tool the launch opened gets one (by --tool, or its pin by --pin-open), and
/// only the first time it is made.
/// </summary>
public sealed class BioLaunchState
{
    public string Key { get; }
    public string Raw { get; }
    public IReadOnlyList<string> Parts { get; }

    private BioLaunchState(string key, string raw)
    {
        Key = key;
        Raw = raw;
        Parts = raw.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>The page the state names, if --bio-page or its first part is one of the tool's.</summary>
    public string? Page
    {
        get
        {
            var ids = BioTools.Pages(Key).Select(p => p.Id).ToList();
            if (Launch.Get("bio-page") is { } p && ids.Contains(p)) return p;
            if (Parts.Count > 0 && ids.Contains(Parts[0])) return Parts[0];
            return null;
        }
    }

    /// <summary>A bare part ("cutaway").</summary>
    public bool Has(string word) => Parts.Any(p => string.Equals(p, word, StringComparison.OrdinalIgnoreCase));

    /// <summary>A "key=value" part, or the flag --bio-&lt;key&gt;.</summary>
    public string? Value(string key)
    {
        foreach (var p in Parts)
        {
            var eq = p.IndexOf('=');
            if (eq > 0 && string.Equals(p[..eq], key, StringComparison.OrdinalIgnoreCase)) return p[(eq + 1)..];
        }
        return Launch.Get("bio-" + key);
    }

    public override string ToString() => Raw;

    private static readonly HashSet<string> Taken = new();

    /// <summary>Whether the launch has any --bio-… flag (but the self-check's and the foundation demo's).</summary>
    private static bool BioFlags => Environment.GetCommandLineArgs().Any(a => a.StartsWith("--bio-", StringComparison.OrdinalIgnoreCase)
        && !a.StartsWith("--bio-check", StringComparison.OrdinalIgnoreCase) && !a.StartsWith("--bio-demo", StringComparison.OrdinalIgnoreCase));

    /// <summary>The launch's state for this tool, if the launch opened it (--tool &lt;key&gt; for the whole tool, --pin-open
    /// &lt;key&gt; for its quick version) with a state or any --bio-… flag; once.</summary>
    public static BioLaunchState? Take(string key, bool quick)
    {
        var opened = quick ? Launch.Get("pin-open") : Launch.Get("tool");
        if (opened != key) return null;
        var raw = Launch.Get("tool-state") ?? "";
        if (raw.Length == 0 && !BioFlags) return null;
        lock (Taken) if (!Taken.Add((quick ? "quick:" : "") + key)) return null;
        Log.Write($"{key}: launched with state \"{raw}\"");
        return new BioLaunchState(key, raw);
    }
}

// ---- pages and settings ----

/// <summary>Which page each biology tool shows, chosen in its header: remembered per tool (Prefs SimplBio.&lt;key&gt;.page).</summary>
public static class BioNav
{
    private static readonly Dictionary<string, string> shown = new();

    /// <summary>A tool's page changed (its key).</summary>
    public static event Action<string>? Changed;

    /// <summary>The page shown: the one chosen this run (or by the launch), else the one remembered, else the first.</summary>
    public static string Page(string key)
    {
        var ids = BioTools.Pages(key).Select(p => p.Id).ToList();
        if (shown.TryGetValue(key, out var p) && ids.Contains(p)) return p;
        if (BioPrefs.String(key, "page") is { } saved && ids.Contains(saved)) return saved;
        return ids.FirstOrDefault() ?? "";
    }

    public static void Set(string key, string page, bool remember = true)
    {
        if (remember) BioPrefs.Set(key, "page", page);
        if (shown.TryGetValue(key, out var was) && was == page) return;
        shown[key] = page;
        Changed?.Invoke(key);
    }
}

/// <summary>A biology tool's remembered settings (spec §3.2): Prefs <c>SimplBio.&lt;key&gt;.&lt;field&gt;</c>, on this PC only.</summary>
public static class BioPrefs
{
    public static string Name(string key, string field) => $"SimplBio.{key}.{field}";
    public static string? String(string key, string field) => Prefs.String(Name(key, field));
    public static bool Bool(string key, string field, bool fallback = false) => Prefs.Bool(Name(key, field), fallback);
    public static double Double(string key, string field, double fallback = 0) => Prefs.Double(Name(key, field), fallback);
    public static void Set(string key, string field, object? value) => Prefs.Set(Name(key, field), value);
}

/// <summary>
/// A biology tool's pages in its header, left of its pin (the Periodic Table's PTHeaderControls): their names where the
/// header has room, else their symbols; the page shown in the accent.
/// </summary>
internal sealed class BioPages : ContentControl
{
    private readonly string key;
    private Grid? headerGrid;
    private bool? squished;
    private string? drawn;

    public BioPages(ToolContext c)
    {
        key = c.Key;
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
        Draw(force: true);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        Detach();
        BioNav.Changed += OnNav;
        headerGrid = HeaderGrid();
        if (headerGrid != null) headerGrid.SizeChanged += HeaderSized;
        Draw(force: true);
    }

    private void Detach()
    {
        BioNav.Changed -= OnNav;
        if (headerGrid != null) headerGrid.SizeChanged -= HeaderSized;
    }

    private void OnNav(string k) { if (k == key) Draw(force: false); }

    private void HeaderSized(object? s, SizeChangedEventArgs e) => Draw(force: false);

    /// <summary>The header row this sits in: the first grid above with a column that takes what is left (the title's).</summary>
    private Grid? HeaderGrid()
    {
        for (DependencyObject? p = VisualTreeHelper.GetParent(this); p != null; p = VisualTreeHelper.GetParent(p))
            if (p is Grid g && g.ColumnDefinitions.Any(cd => cd.Width.IsStar)) return g;
        return null;
    }

    private void Draw(bool force)
    {
        var squish = !Room();
        var page = BioNav.Page(key);
        if (!force && squish == squished && page == drawn) return;
        squished = squish;
        drawn = page;
        var row = K.H(6);
        foreach (var p in BioTools.Pages(key))
        {
            var id = p.Id;
            var b = PTKit.Choice(p.Title, page == id, () => BioNav.Set(key, id), p.Symbol, squish);
            b.ToolTip = p.Help.Length > 0 ? p.Help : p.Title;
            System.Windows.Automation.AutomationProperties.SetName(b, page == id ? $"{p.Title}, shown" : p.Title);
            row.Children.Add(b);
        }
        Content = row;
    }

    /// <summary>Whether the pages' names fit beside the title and the pin.</summary>
    private bool Room()
    {
        if (headerGrid is not { ActualWidth: > 0 } g || BioTools.Kind(key) is not { } kind) return true;
        static double Words(string s, Ty ty, bool strong = false)
        {
            var t = K.Text(s, ty);
            if (strong) t.FontWeight = FontWeights.SemiBold;
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return t.DesiredSize.Width;
        }
        var need = BioTools.Pages(key).Sum(p => Words(p.Title, Ty.Body, true) + 52) + 6 * BioTools.Pages(key).Count + 24 + 48 + 16;
        var title = Math.Max(Words(kind.Name, Ty.Title), Words(kind.Note, Ty.Body));
        // (the title and its line, the tile, and the sheet's Done)
        return g.ActualWidth - (title + 190) >= need;
    }
}

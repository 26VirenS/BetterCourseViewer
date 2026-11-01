using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// The tools pinned to the title bar (the Mac's ToolsCenter.pinned): their keys — the web's own, so a pin means the same
/// thing everywhere — kept in the app's preferences, in the order pinned. The window shows them; a pin pressed opens
/// its tool (<see cref="ToolsScreen.OpenTool"/>).
/// </summary>
public static class ToolsPins
{
    private const string Key = "pinnedTools";

    /// <summary>A tool was pinned or unpinned: whatever shows the pins draws them again.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<string> Pinned
    {
        get
        {
            var seen = new HashSet<string>();
            return (Prefs.String(Key) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(k => ToolsScreen.Kinds.Any(t => t.Key == k) && seen.Add(k)).ToList();
        }
    }

    public static bool IsPinned(string key) => Pinned.Contains(key);

    public static void Toggle(string key)
    {
        var list = Pinned.ToList();
        var pinned = !list.Remove(key);
        if (pinned) list.Add(key);
        Save(list);
        if (pinned && key == "graph") Shell.Tour.Did(Shell.TourEvent.Pinned); // (the tour's "Pin the graphing calculator")
    }

    /// <summary>A pin moved along the title bar (its menu's Move Left and Move Right).</summary>
    public static void Move(string key, int step)
    {
        var list = Pinned.ToList();
        var i = list.IndexOf(key);
        if (i < 0) return;
        var j = Math.Clamp(i + step, 0, list.Count - 1);
        if (i == j) return;
        (list[i], list[j]) = (list[j], list[i]);
        Save(list);
    }

    private static void Save(List<string> list)
    {
        Prefs.Set(Key, list.Count == 0 ? null : string.Join(",", list));
        Changed?.Invoke();
    }
}

/// <summary>
/// Tools (the Mac's ToolsView and ToolsGrid): the things Simpl does on its own, a tile each — its symbol on a wash of its
/// colour, its name and a one-line note, and its pin — under a heading for what they are for: Study (flashcards, the focus
/// timer, citations, Grade needed, the Word Counter), Math &amp; science (the calculators, the unit converter, Statistics, the Matrix Calculator), Files (the file converter,
/// Merge &amp; split PDFs, the PDF annotator, Image to text), Chemistry (the Periodic Table, Lewis Structures, the Molecule
/// Builder, Atoms &amp; Orbitals and the calculators: Tools/Chemistry/ChemTools.cs), Physics (Kinematics &amp; Projectiles and
/// the rest: Tools/Physics/PhysTools.cs) and Biology (the biology toolkit's ten, Tools/Biology) — and (0.3.105) Community:
/// the Simpl Community tools the student added (Tools/Community), and a tile to browse for more. A tile opens its tool in
/// a popup over this page (Tools/ToolHost.cs, ToolPopup), the page and the rest of the window still at hand under it.
/// </summary>
public sealed class ToolsScreen : Screen
{
    /// <summary>A tool: its key (the web's), its name, the line under it, its symbol and its colour (the Mac's ToolKind).</summary>
    public sealed record Kind(string Key, string Name, string Note, SymbolRegular Symbol, string Hex)
    {
        public Color Color => K.Hex(Hex);
    }

    /// <summary>Every tool: the everyday tools of Study, Math &amp; science and Files, the Chemistry section's
    /// (Tools/Chemistry/ChemTools.cs), the Physics section's (Tools/Physics/PhysTools.cs), then the biology toolkit's ten
    /// (Tools/Biology/BioTools.cs).</summary>
    public static readonly Kind[] Kinds = Everyday.Concat(Simpl.Tools.ChemTools.Kinds).Concat(Simpl.Tools.PhysTools.Kinds).Concat(Simpl.Tools.BioTools.Kinds).ToArray();

    /// <summary>The everyday tools (the Mac's ToolKind), each note short enough for one line on its tile. (The Periodic
    /// Table, once among them, leads the Chemistry section.)</summary>
    private static Kind[] Everyday => new Kind[]
    {
        new("cite", "Citation generator", "MLA, APA and Chicago.", SymbolRegular.TextQuote24, "#30b0c7"),
        new("pomo", "Focus timer", "Focus, then take a break.", SymbolRegular.Timer24, "#ff9500"),
        new("calc", "Calculator", "Solves it, step by step.", SymbolRegular.Calculator24, "#ff9f0a"),
        new("graph", "Graphing calculator", "Desmos, right here.", SymbolRegular.DataLine24, "#5856d6"),
        new("need", "Grade needed", "The score your goal needs.", SymbolRegular.TargetArrow24, "#ff375f"),
        new("conv", "File converter", "Word, PDF and images.", SymbolRegular.ArrowSync24, "#34c759"),
        new("units", "Unit converter", "Lengths, weights and more.", SymbolRegular.Ruler24, "#64d2ff"),
        new("pdfx", "Merge & split PDFs", "Join PDFs or take pages out.", SymbolRegular.DocumentCopy24, "#bf5af2"),
        new("mark", "PDF annotator", "Highlight and add notes.", SymbolRegular.Highlight24, "#e5a500"),
        new("ocr", "Image to text", "Copy the words in a picture.", SymbolRegular.ScanText24, "#00b3a4"),
        new("fc", "Flashcards", "Flip, learn, test, match.", SymbolRegular.SquareMultiple24, "#0a84ff"),
        // (tools) the tools program's: each at the end of its group below
        new("words", "Word counter", "Words, characters, reading time.", SymbolRegular.TextWordCount24, "#8e8e93"),
        new("stats", "Statistics", "Mean, SD, regression, z and t.", SymbolRegular.DataHistogram24, "#ff2d55"),
        // (a table of cells, not the Grid24 the biology Crosses tile wears a few rows below)
        new("matrix", "Matrix calculator", "Determinant, inverse, solve, RREF.", SymbolRegular.TableSimple24, "#5e5ce6"),
        new("shrink", "Compress files", "Shrink images and PDFs to fit.", SymbolRegular.ArrowMinimize24, "#ff9500"),
        new("plan", "Study planner", "Study blocks from your due dates.", SymbolRegular.CalendarClock24, "#34c759"),
        new("verbs", "Verb conjugations", "Conjugate verbs in 5 languages.", SymbolRegular.BookLetter24, "#30b0c7"),
    };

    /// <summary>The Tools page's sections in its order (the Mac's ToolGroup): what each set of tools is for, the study
    /// tools first (the chemistry tools come after, under "Chemistry", the physics tools under "Physics", then the
    /// biology toolkit's ten under "Biology").</summary>
    public static readonly (string Title, string[] Keys)[] Groups =
    {
        ("Study", new[] { "fc", "cite", "pomo", "need", "words", "plan", "verbs" }),
        ("Math & science", new[] { "calc", "graph", "units", "stats", "matrix" }),
        ("Files", new[] { "mark", "pdfx", "conv", "ocr", "shrink" }),
    };

    /// <summary>Where the graphing calculator is (the Mac's GraphWeb.desmos), while it has no view of its own here.</summary>
    private static readonly Uri Desmos = new("https://www.desmos.com/calculator");

    public ToolsScreen(Engine.Engine engine) : base(engine) { }

    public override string Title => "Tools";

    public override string? Subtitle => "Pin a tool to keep it in the title bar: its button there opens a quick version of it.";

    public override bool Scrolls => false;

    /// <summary>The focus timer's tile, while a session goes: its phase and what is left, counted down each second.</summary>
    private TextBlock? timerLine;
    private readonly System.Windows.Threading.DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private Simpl.Tools.ITimerState? watched;

    private static string TimerWords(Simpl.Tools.ITimerState t) => $"{t.PhaseName} · {Simpl.Tools.PinBar.Clock(t.Remaining)}{(t.Paused ? " · Paused" : "")}";

    private void TimerChanged() => Render(); // (started, paused, a phase over: the tile again)

    private void Tick()
    {
        if (timerLine == null || Simpl.Tools.ToolRegistry.Timer is not { Active: true } t) return;
        timerLine.Text = TimerWords(t);
        timerLine.Foreground = K.Brush(t.PhaseColor);
    }

    public override void Refresh()
    {
        if (!ReferenceEquals(watched, Simpl.Tools.ToolRegistry.Timer))
        {
            if (watched != null) watched.Changed -= TimerChanged;
            watched = Simpl.Tools.ToolRegistry.Timer;
            if (watched != null) watched.Changed += TimerChanged;
        }
        clock.Tick -= OnClock;
        clock.Tick += OnClock;
        clock.Start();
        // (a pin changed anywhere — here, a sheet, the title bar — shows on the tiles while the page is up)
        ToolsPins.Changed -= PinsChanged;
        ToolsPins.Changed += PinsChanged;
        // (0.3.105) the Community tools added or removed — from the browser, a tile's menu — show in their section at once
        Simpl.Tools.CommunityTools.Changed -= CommunityChanged;
        Simpl.Tools.CommunityTools.Changed += CommunityChanged;
        // (the screenshot suite: --community-add s-ohms adds that Community tool as Add does, --community-open opens it
        //  once it is in; --community-browse yes|science|"tip" opens the browser over this page, at that group or search)
        _ = Simpl.Tools.CommunityTools.OnLaunch(engine);
        if (Launch.Take("", "community-browse") is { Length: > 0 } browse)
            Application.Current.Dispatcher.BeginInvoke(() => Simpl.Tools.CommunityBrowser.Show(engine, null, browse), System.Windows.Threading.DispatcherPriority.Loaded);
        // (the screenshot suite: --tool calc opens the Calculator in its popup over this page, as the Mac's -SimplTool)
        if (Launch.Take("", "tool") is { Length: > 0 } k && Kinds.FirstOrDefault(t => t.Key == k) is { } kind)
            Application.Current.Dispatcher.BeginInvoke(() => OpenTool(engine, kind), System.Windows.Threading.DispatcherPriority.Loaded);
        // (a development run's --bio-demo: the biology toolkit's foundation seen working, Tools/Biology/BioDemo.cs)
        else if (Launch.IsDevelopment && Launch.Take("", "bio-demo") != null)
            Application.Current.Dispatcher.BeginInvoke(() => Simpl.Tools.BioDemo.Show(View != null ? Window.GetWindow(View) : null), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnClock(object? sender, EventArgs e) => Tick();

    /// <summary>Nothing to read ahead (and a read ahead never forgets the tool last open).</summary>
    protected override void ReadAhead() { }

    private void PinsChanged() => Render();

    public override void Hide()
    {
        base.Hide();
        ToolsPins.Changed -= PinsChanged;
        Simpl.Tools.CommunityTools.Changed -= CommunityChanged;
        clock.Stop();
        if (watched != null) { watched.Changed -= TimerChanged; watched = null; }
    }

    protected override FrameworkElement Build()
    {
        timerLine = null;
        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 12 };
        // (a heading for what each set of tools is for, its tiles under it — every section as many across as the page
        //  holds, the same for all of them, so the columns line up down the page; a section's last row may be short)
        FrameworkElement Heading(string title, string? line)
        {
            var h = K.V(2, K.Text(title, Ty.Subtitle));
            if (line != null) h.Children.Add(K.Wrapped(line, Ty.Body, B.Text2));
            h.Margin = new Thickness(0, page.Children.Count == 0 ? 0 : 18, 0, 0);
            System.Windows.Automation.AutomationProperties.SetHeadingLevel(h.Children[0], System.Windows.Automation.AutomationHeadingLevel.Level2);
            return h;
        }
        foreach (var (title, keys) in Groups)
        {
            page.Children.Add(Heading(title, null));
            var kinds = keys.Select(k2 => Kinds.FirstOrDefault(t => t.Key == k2)).OfType<Kind>().ToList();
            page.Children.Add(Tiles(kinds));
        }
        // (the chemistry tools, then the physics tools, then the biology toolkit's ten, each under a heading of their own,
        //  as the Mac's Tools page has them)
        var chemistry = Heading("Chemistry", "The periodic table, Lewis structures, molecules and atoms in 3D, and the calculators: equations, moles, gases, solutions.");
        page.Children.Add(chemistry);
        page.Children.Add(Tiles(Simpl.Tools.ChemTools.Kinds));
        var physics = Heading("Physics", "Motion and projectiles, vectors, and DC circuits built and read.");
        page.Children.Add(physics);
        page.Children.Add(Tiles(Simpl.Tools.PhysTools.Kinds));
        var biology = Heading("Biology", "Cells, DNA, genes and biomolecules in 3D, and the maths of crosses, populations and the lab.");
        page.Children.Add(biology);
        page.Children.Add(Tiles(Simpl.Tools.BioTools.Kinds));
        // (0.3.105) Simpl Community: the tools added from it and a tile to browse for more — there with none added too
        var community = Heading("Community", "Tools other students made and shared in Simpl Community. Add one and it stays here, working offline too.");
        page.Children.Add(community);
        communityHost = new ContentControl { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = CommunityTiles() };
        page.Children.Add(communityHost);
        var sv = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        sv.SizeChanged += (_, _) => sv.Padding = sv.ActualWidth < 560 ? new Thickness(20, 0, 20, 32) : new Thickness(40, 0, 40, 40);
        sv.Padding = new Thickness(40, 0, 40, 40);
        // (the screenshot suite: --tools-scroll chemistry, physics or biology, the page scrolled to that section, as the
        //  Mac's -SimplToolsScroll)
        var scrollTo = Launch.Take("", "tools-scroll")?.Trim().ToLowerInvariant() switch { "chemistry" => chemistry, "physics" => physics, "biology" => biology, "community" => community, _ => null };
        if (scrollTo != null)
            sv.Loaded += (_, _) => sv.Dispatcher.BeginInvoke(() => sv.ScrollToVerticalOffset(Math.Max(0, scrollTo.TranslatePoint(new Point(0, 0), page).Y - 8)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        // (--bio-check: the biology toolkit's self-check in the log, as the Mac's Tools page starts it)
        Simpl.Tools.BioTools.SelfCheckIfAsked();
        return sv;
    }

    /// <summary>The narrowest a tile is: three across from about 900 of the page's width.</summary>
    private const double TileWidth = 280;

    /// <summary>A section's tiles: as many across as the page holds — one count for every section on the page (it once
    /// went from three across to two between sections, keeping an even section's rows full), so the columns line up.</summary>
    private FrameworkElement Tiles(IEnumerable<Kind> kinds) => new MoreGrid(TileWidth, 16, kinds.Select(k => (UIElement)Tile(k)), stretch: true);

    /// <summary>A tool's tile: its symbol on a wash of its colour, its name and line, and its pin at its corner.</summary>
    private FrameworkElement Tile(Kind kind)
    {
        // (a long name — Photosynthesis & Respiration — on two lines rather than cut short; the name keeps clear of the
        //  pin in the corner, the note under it has the tile's width)
        var words = K.V(4, K.Wrapped(kind.Name, Ty.BodyStrong, maxLines: 2).Margin(0, 0, 36, 0), K.Wrapped(kind.Note, Ty.Body, B.Text2));
        if (kind.Key == "pomo" && Simpl.Tools.ToolRegistry.Timer is { Active: true } t)
        {
            timerLine = K.Text(TimerWords(t), Ty.BodyStrong);
            timerLine.Foreground = K.Brush(t.PhaseColor);
            Simpl.Tools.FontFeatureExtensions.FontFeatures(timerLine);
            words.Children.Add(timerLine);
        }
        var g = K.Columns("auto, *", K.IconTile(kind.Symbol, kind.Color, 44).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 0, 14, 0).Col(0), words.Col(1));
        var card = K.CardButton(g, () => OpenTool(engine, kind), 8, new Thickness(16));
        card.VerticalContentAlignment = VerticalAlignment.Top;
        card.MinHeight = 84;
        System.Windows.Automation.AutomationProperties.SetName(card, $"{kind.Name}. {kind.Note}");
        var on = ToolsPins.IsPinned(kind.Key);
        card.ContextMenu = MoreKit.Menu(
            ("Open", SymbolRegular.Open24, () => OpenTool(engine, kind)),
            (on ? "Unpin from title bar" : "Pin to title bar", on ? SymbolRegular.PinOff24 : SymbolRegular.Pin24, () => ToolsPins.Toggle(kind.Key)));
        var pin = Pin(kind, on);
        if (kind.Key == "graph") Shell.Tour.Mark(pin, Shell.TourSpot.ToolPin);
        return new Grid { Children = { card, pin } };
    }

    // ---- (0.3.105) Simpl Community ----

    private ContentControl? communityHost;
    /// <summary>The Community tools the section shows (their ids and versions), so it is drawn again only when they change.</summary>
    private string communityDrawn = "";

    private static string CommunityNow() => string.Join(",", Simpl.Tools.CommunityTools.Added.Select(t => $"{t.Id}@{t.Version}"));

    /// <summary>A Community tool added or removed: its section drawn again where it is (the page keeps its scroll).</summary>
    private void CommunityChanged()
    {
        if (communityHost == null || CommunityNow() == communityDrawn) return;
        communityHost.Content = CommunityTiles();
        Support.Shot.Moved();
    }

    /// <summary>The Community tools added, a tile each, and Browse Community after them.</summary>
    private FrameworkElement CommunityTiles()
    {
        communityDrawn = CommunityNow();
        var tiles = Simpl.Tools.CommunityTools.Added.Select(t => (UIElement)CommunityTile(t)).Append(BrowseTile()).ToList();
        if (Launch.IsDevelopment || Support.Shot.Asked) Support.Log.Write($"tools: community section shows {tiles.Count - 1} added");
        return new MoreGrid(TileWidth, 16, tiles, stretch: true);
    }

    /// <summary>
    /// A Community tool's tile, as Simpl's own are — its symbol on a wash of its colour, its name, its line, who made it —
    /// with no pin (the title bar's pins are Simpl's own tools): its corner's ⋯, and its context menu, open it or remove it.
    /// </summary>
    private FrameworkElement CommunityTile(Simpl.Tools.CommunityTool t)
    {
        var kind = Simpl.Tools.CommunityTools.Kind(t);
        var by = t.Maker.Length > 0 ? $"by {t.Maker}" : "Simpl Community";
        var words = K.V(4, K.Wrapped(kind.Name, Ty.BodyStrong, maxLines: 2).Margin(0, 0, 36, 0), K.Wrapped(kind.Note, Ty.Body, B.Text2), K.Text(by, Ty.Caption, B.Text3));
        var g = K.Columns("auto, *", K.IconTile(kind.Symbol, kind.Color, 44).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 0, 14, 0).Col(0), words.Col(1));
        var card = K.CardButton(g, () => OpenTool(engine, kind), 8, new Thickness(16));
        card.VerticalContentAlignment = VerticalAlignment.Top;
        card.MinHeight = 84;
        System.Windows.Automation.AutomationProperties.SetName(card, $"{kind.Name}. {kind.Note} From Simpl Community, {by}.");
        ContextMenu Menu() => MoreKit.Menu(
            ("Open", SymbolRegular.Open24, () => OpenTool(engine, kind)),
            null,
            ("Remove…", SymbolRegular.Delete24, () => AskRemove(t)));
        card.ContextMenu = Menu();
        Button? more = null;
        more = K.IconButton(SymbolRegular.MoreHorizontal20, () => MoreKit.Open(Menu(), more!), $"More for {kind.Name}", 16, 32);
        more.HorizontalAlignment = HorizontalAlignment.Right;
        more.VerticalAlignment = VerticalAlignment.Top;
        more.Margin = new Thickness(0, 10, 10, 0);
        return new Grid { Children = { card, more } };
    }

    /// <summary>A Community tool taken off the page, once the student says so: its values forgotten with it.</summary>
    private void AskRemove(Simpl.Tools.CommunityTool t)
    {
        if (t.Id is not { } id) return;
        Shell.Dialogs.Confirm(View != null ? Window.GetWindow(View) : null, $"Remove “{t.Name}”?",
            "It comes off your Tools page, and the values you put in it are forgotten. You can add it again from Simpl Community.",
            "Remove", () => Simpl.Tools.CommunityTools.Remove(id));
    }

    /// <summary>The tile that opens Simpl Community's browser.</summary>
    private FrameworkElement BrowseTile()
    {
        var words = K.V(4, K.Wrapped("Browse Community", Ty.BodyStrong, maxLines: 2), K.Wrapped("Find tools other students made, and add them here.", Ty.Body, B.Text2));
        var g = K.Columns("auto, *", K.IconTile(SymbolRegular.PeopleCommunity24, Appearance.Accent, 44).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 0, 14, 0).Col(0), words.Col(1));
        var card = K.CardButton(g, () => Simpl.Tools.CommunityBrowser.Show(engine, View != null ? Window.GetWindow(View) : null), 8, new Thickness(16));
        card.VerticalContentAlignment = VerticalAlignment.Top;
        card.MinHeight = 84;
        System.Windows.Automation.AutomationProperties.SetName(card, "Browse Community. Find tools other students made, and add them here.");
        return card;
    }

    /// <summary>A tile's pin: filled in the tool's colour while it is pinned.</summary>
    private static Button Pin(Kind kind, bool on)
    {
        var icon = on ? K.Icon(SymbolRegular.Pin24, 15, kind.Color, filled: true) : K.Icon(SymbolRegular.Pin24, 15, B.Text2);
        var b = K.Subtle(icon, () => ToolsPins.Toggle(kind.Key), new Thickness(0), 16);
        b.Width = 32;
        b.Height = 32;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.HorizontalAlignment = HorizontalAlignment.Right;
        b.VerticalAlignment = VerticalAlignment.Top;
        b.Margin = new Thickness(0, 10, 10, 0);
        if (on) b.Background = K.Brush(K.WithAlpha(kind.Color, 0.2));
        b.ToolTip = on ? $"Unpin {kind.Name} from the title bar" : $"Pin {kind.Name} to the title bar";
        System.Windows.Automation.AutomationProperties.SetName(b, on ? $"Unpin {kind.Name}" : $"Pin {kind.Name}");
        return b;
    }

    /// <summary>A tool opened by its key (a command, a link): in its popup over where you are.</summary>
    public static void OpenTool(Engine.Engine engine, string key)
    {
        if (Kinds.FirstOrDefault(k => k.Key == key) is { } kind) OpenTool(engine, kind);
    }

    /// <summary>A tool opened in its popup over where you are — from its tile, a command alike; the graphing calculator,
    /// while it has no view of its own here, in a window.</summary>
    public static void OpenTool(Engine.Engine engine, Kind kind)
    {
        if (kind.Key == "graph" && !Simpl.Tools.ToolRegistry.Has("graph")) { engine.OpenBrowser(Desmos, kind.Name); return; }
        Simpl.Tools.ToolsCenter.Show(kind.Key, engine);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

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
/// Tools (the Mac's ToolsView and ToolsGrid): the things Simpl does on its own — a citation generator, a focus timer, a
/// calculator, a graphing calculator, the periodic table, Grade needed, a file converter, Merge &amp; split PDFs, a PDF
/// annotator, Image to text, flashcards and a unit converter — a tile each: its symbol on a wash of its colour, its name
/// and line, and its pin. A tile opens its tool in place, under the page's header ("Tools › Calculator", its pin beside
/// the title); the tool stays open while you go elsewhere, and Escape or the breadcrumb goes back to the tiles.
/// </summary>
public sealed class ToolsScreen : Screen
{
    /// <summary>A tool: its key (the web's), its name, the line under it, its symbol and its colour (the Mac's ToolKind).</summary>
    public sealed record Kind(string Key, string Name, string Note, SymbolRegular Symbol, string Hex)
    {
        public Color Color => K.Hex(Hex);
    }

    public static readonly Kind[] Kinds =
    {
        new("cite", "Citation generator", "Cite a source in MLA, APA or Chicago.", SymbolRegular.TextQuote24, "#30b0c7"),
        new("pomo", "Focus timer", "Focus for a while, then take a break.", SymbolRegular.Timer24, "#ff9500"),
        new("calc", "Calculator", "Scientific, with the keys you know.", SymbolRegular.Calculator24, "#ff9f0a"),
        new("graph", "Graphing calculator", "Desmos, right here.", SymbolRegular.DataLine24, "#5856d6"),
        new("ptable", "Periodic table", "Every element and its facts, in place.", SymbolRegular.Molecule24, "#30d158"),
        new("need", "Grade needed", "What you need on the final to hit your goal.", SymbolRegular.TargetArrow24, "#ff375f"),
        new("conv", "File converter", "Word, PDF and images, any way round.", SymbolRegular.ArrowSync24, "#34c759"),
        new("units", "Unit converter", "Lengths, weights, temperatures and more.", SymbolRegular.Ruler24, "#64d2ff"),
        new("pdfx", "Merge & split PDFs", "Join pages from PDFs into one, or take pages out.", SymbolRegular.DocumentCopy24, "#bf5af2"),
        new("mark", "PDF annotator", "Highlight and add notes, saved in the file.", SymbolRegular.Highlight24, "#e5a500"),
        new("ocr", "Image to text", "Read the words off a picture or a scan.", SymbolRegular.ScanText24, "#00b3a4"),
        new("fc", "Flashcards", "Make a set. Flip, learn, test, match.", SymbolRegular.SquareMultiple24, "#0a84ff"),
    };

    /// <summary>Where the graphing calculator is (the Mac's GraphWeb.desmos), while it has no view of its own here.</summary>
    private static readonly Uri Desmos = new("https://www.desmos.com/calculator");

    private string? shown;
    private FrameworkElement? toolView;

    public ToolsScreen(Engine.Engine engine) : base(engine)
    {
        Simpl.Tools.ToolsCenter.OpenChanged += () =>
        {
            HeaderChange();
            Render();
        };
    }

    private Kind? Open => Simpl.Tools.ToolsCenter.Open is { } k ? Kinds.FirstOrDefault(t => t.Key == k) : null;

    public override string Title => Open?.Name ?? "Tools";

    public override string? Subtitle => Open is { } k
        ? k.Key == "ptable" ? null : k.Note
        : "Handy things, right here. Pin one and it sits in the title bar on every screen.";

    /// <summary>The tiles, before the tool open: "Tools › Calculator".</summary>
    public override IReadOnlyList<(string Title, Action Go)> Before =>
        Open == null ? Array.Empty<(string, Action)>() : new[] { ("Tools", (Action)(() => Simpl.Tools.ToolsCenter.Open = null)) };

    /// <summary>The tool's own controls (the Periodic Table's search) and its pin.</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (Open is not { } k) return null;
            var c = Context(k);
            var row = K.H(8);
            if (Simpl.Tools.ToolRegistry.Header(c) is { } extra) { extra.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(extra); }
            row.Children.Add(Simpl.Tools.ToolHeader.PinButton(k, squished: k.Key == "ptable"));
            return row;
        }
    }

    public override bool Scrolls => false;

    public override bool Escape()
    {
        if (Open == null) return false;
        Simpl.Tools.ToolsCenter.Open = null;
        return true;
    }

    public override void Refresh()
    {
        // (a pin changed anywhere — here, a sheet, the title bar — shows on the tiles while the page is up)
        ToolsPins.Changed -= PinsChanged;
        ToolsPins.Changed += PinsChanged;
        // (the screenshot suite: --tool calc opens the Calculator, as the Mac's -SimplTool)
        if (Launch.Take("", "tool") is { Length: > 0 } k && Kinds.Any(t => t.Key == k)) Simpl.Tools.ToolsCenter.Open = k;
    }

    private void PinsChanged()
    {
        if (Open == null) Render();
    }

    public override void Hide()
    {
        base.Hide();
        ToolsPins.Changed -= PinsChanged;
    }

    private Simpl.Tools.ToolContext Context(Kind k) => new() { Engine = engine, Key = k.Key, Owner = Window.GetWindow(View) };

    protected override FrameworkElement Build()
    {
        if (Open is { } k)
        {
            // (the tool kept as it was while it stays open: only a different tool is built afresh)
            if (shown != k.Key || toolView == null)
            {
                shown = k.Key;
                toolView = Simpl.Tools.ToolRegistry.Full(Context(k)) ?? Simpl.Tools.ToolPopup.Missing(k);
                Motion.FadeIn(toolView, Motion.Normal);
            }
            if (toolView.Parent is ContentControl old) old.Content = null;
            return toolView;
        }
        shown = null;
        toolView = null;
        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 22 };
        var grid = new MoreGrid(300, 16, Kinds.Select(k2 => (UIElement)Tile(k2)), stretch: true);
        page.Children.Add(grid);
        if (ToolsPins.Pinned.Count == 0)
            page.Children.Add(MoreKit.Note(SymbolRegular.Pin24, "Press a tile’s pin to keep that tool in the title bar. Its button there opens a quick version of it."));
        var sv = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        sv.SizeChanged += (_, _) => sv.Padding = sv.ActualWidth < 560 ? new Thickness(20, 0, 20, 32) : new Thickness(40, 0, 40, 40);
        sv.Padding = new Thickness(40, 0, 40, 40);
        return sv;
    }

    /// <summary>A tool's tile: its symbol on a wash of its colour, its name and line, and its pin at its corner.</summary>
    private FrameworkElement Tile(Kind kind)
    {
        var words = K.V(4, K.Text(kind.Name, Ty.BodyStrong), K.Wrapped(kind.Note, Ty.Body, B.Text2));
        if (kind.Key == "pomo" && Simpl.Tools.ToolRegistry.Timer is { Active: true } t)
        {
            var live = K.Text($"{t.PhaseName} · {Simpl.Tools.PinBar.Clock(t.Remaining)}{(t.Paused ? " · Paused" : "")}", Ty.BodyStrong);
            live.Foreground = new SolidColorBrush(t.PhaseColor);
            words.Children.Add(live);
        }
        var g = K.Columns("auto, *", K.IconTile(kind.Symbol, kind.Color, 48).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 0, 14, 0).Col(0), words.Margin(0, 0, 36, 0).Col(1));
        var card = K.CardButton(g, () => OpenTool(engine, kind, View), 8, new Thickness(18));
        card.VerticalContentAlignment = VerticalAlignment.Top;
        card.MinHeight = 104;
        System.Windows.Automation.AutomationProperties.SetName(card, $"{kind.Name}. {kind.Note}");
        var on = ToolsPins.IsPinned(kind.Key);
        card.ContextMenu = MoreKit.Menu(
            ("Open", SymbolRegular.Open24, () => OpenTool(engine, kind, View)),
            (on ? "Unpin from Title Bar" : "Pin to Title Bar", on ? SymbolRegular.PinOff24 : SymbolRegular.Pin24, () => ToolsPins.Toggle(kind.Key)));
        var pin = Pin(kind, on);
        if (kind.Key == "graph") Shell.Tour.Mark(pin, Shell.TourSpot.ToolPin);
        return new Grid { Children = { card, pin } };
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
        if (on) b.Background = new SolidColorBrush(K.WithAlpha(kind.Color, 0.2));
        b.ToolTip = on ? $"Unpin {kind.Name} from the title bar" : $"Pin {kind.Name} to the title bar";
        System.Windows.Automation.AutomationProperties.SetName(b, on ? $"Unpin {kind.Name}" : $"Pin {kind.Name}");
        return b;
    }

    /// <summary>A tool opened by its key (a command, a link): on the Tools page.</summary>
    public static void OpenTool(Engine.Engine engine, string key, FrameworkElement? from = null)
    {
        if (Kinds.FirstOrDefault(k => k.Key == key) is { } kind) OpenTool(engine, kind, from);
    }

    /// <summary>A tool opened on the Tools page; the graphing calculator, while it has no view of its own here, in a window.</summary>
    public static void OpenTool(Engine.Engine engine, Kind kind, FrameworkElement? from = null)
    {
        if (kind.Key == "graph" && !Simpl.Tools.ToolRegistry.Has("graph")) { engine.OpenBrowser(Desmos, kind.Name); return; }
        if (engine.Nav.Current.Place.Kind == Engine.PlaceKind.Tools && engine.Nav.Current.Path.Count == 0) Simpl.Tools.ToolsCenter.Open = kind.Key;
        else Simpl.Tools.ToolsCenter.Show(kind.Key, engine);
    }
}

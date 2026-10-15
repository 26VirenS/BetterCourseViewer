using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

/// <summary>
/// What a tool is given where it opens (the Mac's PinnedToolPopover and ToolPopup between them): the engine, whether it
/// is the quick version in a pin's flyout, the files and details handed to it (from a pin's flyout, a command), how to
/// open the whole tool or close what it is in, and how to tell its heading what it shows.
/// </summary>
public sealed class ToolContext
{
    public required Engine.Engine Engine { get; init; }
    public required string Key { get; init; }
    /// <summary>The quick version, in a pin's flyout or pulled out of it (the Mac's FocusTimerCompact, CalculatorCompact …).</summary>
    public bool Quick { get; init; }
    /// <summary>The window it is in (for its dialogs): the tool's popup, a pin's window or Simpl's.</summary>
    public Window? Owner { get; init; }

    /// <summary>From the quick version: the whole tool over the window, with details or files for it.</summary>
    public Action<Dictionary<string, string>?, IReadOnlyList<string>?> OpenFull { get; init; } = (_, _) => { };

    /// <summary>
    /// (0.3.5) Set by the quick version as it is made: what was begun in it, for the whole tool its flyout's (or its
    /// pulled-out window's) Open full screen opens — the details the tool takes as it is made (TakeInfo: Grade needed's
    /// numbers, the element searched for, a link to cite, a codon…; "carry": "pin" for the calculator and the unit
    /// converter, whose whole tool then goes on from the pin's own state). Null, nothing begun: the tool opens as its tile
    /// opens it. The focus timer, Flashcards' sets and the lab's numbers are the whole tool's already; the graph's drawing
    /// goes by Desmos itself (GraphWeb.CarryOver).
    /// </summary>
    public Func<Dictionary<string, string>?> Carry { get; set; } = () => null;

    /// <summary>The flyout (the quick version) or the popup (the whole tool) it is in, closed — a popup's a moment later,
    /// so another tool handed something (BioTools.Open) takes its place instead.</summary>
    public Action Close { get; init; } = () => { };

    /// <summary>
    /// The heading told which of the tool's own pages shows: its name for the title, after the tool's ("Tool › Page"), and
    /// the way back to the tool's first page (the breadcrumb's tool part); null, the tool's first page again.
    /// </summary>
    public Action<string?, Action?> ShowPage { get; init; } = (_, _) => { };

    /// <summary>The heading told whether the tool's line under its name ("Make a set. Flip, learn, test, match.") is worth
    /// its room now: not while a set is studied.</summary>
    public Action<bool> ShowNote { get; init; } = _ => { };

    /// <summary>The files handed to the tool, taken once (the Mac's ToolsCenter.takeFiles).</summary>
    public IReadOnlyList<string> TakeFiles() => ToolsCenter.TakeFiles(Key);

    /// <summary>A detail handed to the tool, taken once (the Mac's ToolsCenter.takeInfo).</summary>
    public string? TakeInfo(string name) => ToolsCenter.TakeInfo(name);
}

/// <summary>
/// The tools' views by key (the web's keys: cite, pomo, calc, graph, ptable, need, conv, units, pdfx, mark, ocr, fc; the
/// tools program's words and stats (Tools/Everyday); the
/// Chemistry section's lewis, molecule and atoms beside ptable, and its calculators (balance), ChemTools; the Physics
/// section's motion and vectors, PhysTools; and the biology toolkit's cell, dna, gene, biomol, cross, divide, popgen, biolab, enzyme,
/// energy, through BioTools.Register). Each tool's file registers its own from a module initializer, so a tool added is
/// a file added.
/// </summary>
public static class ToolRegistry
{
    private static readonly Dictionary<string, Func<ToolContext, FrameworkElement>> full = new();
    private static readonly Dictionary<string, Func<ToolContext, FrameworkElement>> quick = new();
    private static readonly Dictionary<string, Func<ToolContext, FrameworkElement>> header = new();

    /// <param name="key">The tool's key.</param>
    /// <param name="full">The whole tool (on the Tools page, or over the window).</param>
    /// <param name="quick">Its quick version, for its pin's flyout (null: the flyout offers the whole tool).</param>
    /// <param name="headerControls">Controls beside its pin in the page header (the Periodic Table's search and tour).</param>
    public static void Register(string key, Func<ToolContext, FrameworkElement> full, Func<ToolContext, FrameworkElement>? quick = null,
        Func<ToolContext, FrameworkElement>? headerControls = null)
    {
        ToolRegistry.full[key] = full;
        if (quick != null) ToolRegistry.quick[key] = quick;
        if (headerControls != null) header[key] = headerControls;
    }

    public static bool Has(string key) => full.ContainsKey(key);

    public static FrameworkElement? Full(ToolContext c) => Make(full, c);
    public static FrameworkElement? Quick(ToolContext c) => Make(quick, c);
    public static FrameworkElement? Header(ToolContext c) => Make(header, c);

    private static FrameworkElement? Make(Dictionary<string, Func<ToolContext, FrameworkElement>> d, ToolContext c)
    {
        if (!d.TryGetValue(c.Key, out var f)) return null;
        try { return f(c); }
        catch (Exception e)
        {
            Log.Write($"tool {c.Key}: {e}");
            return K.Wrapped($"This tool could not open: {e.Message}", Ty.Body, B.Text2).Margin(4);
        }
    }

    /// <summary>The focus timer's state while a session goes, for its pin (set by the focus timer).</summary>
    public static ITimerState? Timer { get; set; }
}

/// <summary>A running focus timer as its pin shows it (the Mac's FocusTimer.shared, as PinnedTimerLabel reads it).</summary>
public interface ITimerState
{
    bool Active { get; }
    bool Running { get; }
    bool Paused { get; }
    string PhaseName { get; }
    Color PhaseColor { get; }
    string NextPhaseName { get; }
    TimeSpan Remaining { get; }
    /// <summary>What is left of the phase, 1 to 0.</summary>
    double Fraction { get; }
    void Toggle();
    void Skip();
    void Reset();
    /// <summary>Started, paused, a phase over, stopped.</summary>
    event Action? Changed;
}

/// <summary>
/// What the tools share across the window (the Mac's ToolsCenter): a tool opened whole, and files and details handed to
/// it to take when it opens.
/// </summary>
public static class ToolsCenter
{
    private static List<string> handoff = new();
    private static string? handoffTool;
    private static Dictionary<string, string> handoffInfo = new();

    /// <summary>
    /// A tool opened whole from anywhere — its tile on the Tools page, a pin's menu or flyout, a command, a link — with
    /// files or details for it: in a popup over where you are (ToolPopup), the screen and the rest of the window still at
    /// hand under it; the tool's popup already open comes forward, taking what was handed to it.
    /// </summary>
    public static void Show(string key, Engine.Engine engine, IReadOnlyList<string>? files = null, Dictionary<string, string>? info = null)
    {
        if (KindOf(key) == null) return;
        Hand(key, files, info);
        ToolPopup.Show(key, engine, Application.Current?.MainWindow);
    }

    /// <summary>
    /// (0.3.5) A pin's flyout's (or its pulled-out window's) Open full screen: the tool whole in its popup over the window,
    /// as its tile opens it (<see cref="Show"/>: as large as the window, over the pane, or the size last given it), taking
    /// what was begun in the quick version (ToolContext.Carry) — its popup, if it is open already, made again to take it
    /// (ToolPopup.Show). The graph's drawing goes by Desmos itself (GraphWeb.CarryOver).
    /// </summary>
    public static void FullScreen(string key, Engine.Engine engine, Dictionary<string, string>? carried)
    {
        if (key == "graph") GraphWeb.CarryOver();
        if (Shot.Asked || Launch.IsDevelopment)
            Log.Write($"pin full screen: {key} (carried: {(carried is { Count: > 0 } ? string.Join(", ", carried.Keys.Order(StringComparer.Ordinal)) : "nothing")})");
        Show(key, engine, null, carried);
    }

    /// <summary>A tool opened whole by a tool (BioTools.Open, the Biomolecule Builder's molecule for the Molecule
    /// Builder): from a tool's popup (<paramref name="owner"/>), that popup turns into the other tool in place; from
    /// anywhere else, as <see cref="Show"/>.</summary>
    public static void PopUp(string key, Engine.Engine engine, Window? owner, IReadOnlyList<string>? files = null, Dictionary<string, string>? info = null)
    {
        if (KindOf(key) == null) return;
        Hand(key, files, info);
        if (!ToolPopup.Change(owner, key)) ToolPopup.Show(key, engine, Application.Current?.MainWindow);
    }

    /// <summary>
    /// A tool handing something to another (BioTools.Open; the chemistry tools' hand-offs — an element's See it in 3D,
    /// Lewis Structures' Open in Molecule Builder): from the tool's popup, the popup turns into the other where it is (the
    /// tool asks its popup closed first, and the other takes its place on the next turn); from a pin's flyout, the flyout
    /// closes and the other opens over the window. The other takes <paramref name="info"/> once with ToolContext.TakeInfo.
    /// </summary>
    public static void HandTo(ToolContext from, string key, Dictionary<string, string>? info = null)
    {
        var engine = from.Engine;
        var owner = from.Owner;
        from.Close();
        Application.Current?.Dispatcher.BeginInvoke(() => PopUp(key, engine, owner, null, info), DispatcherPriority.Background);
    }

    /// <summary>Files or details are waiting for this tool (it takes them as it is made).</summary>
    public static bool Handed(string key) => (handoffTool == key && handoff.Count > 0) || handoffInfo.Count > 0;

    private static void Hand(string key, IReadOnlyList<string>? files, Dictionary<string, string>? info)
    {
        handoff = files?.ToList() ?? new();
        handoffTool = handoff.Count == 0 ? null : key;
        handoffInfo = info ?? new();
    }

    public static IReadOnlyList<string> TakeFiles(string key)
    {
        if (handoffTool != key || handoff.Count == 0) return Array.Empty<string>();
        var f = handoff;
        handoff = new();
        handoffTool = null;
        return f;
    }

    public static string? TakeInfo(string name) => handoffInfo.Remove(name, out var v) ? v : null;

    /// <summary>A tool's short name (the Mac's ToolKind.shortName), where room is tight; its flyout and its window say its
    /// whole name, as its tile does.</summary>
    public static string ShortName(string key) => key switch
    {
        "cite" => "Cite", "pomo" => "Focus", "calc" => "Calculator", "graph" => "Graphing", "ptable" => "Elements",
        "need" => "Grade needed", "conv" => "Convert", "units" => "Units", "pdfx" => "Merge & split", "mark" => "Mark up",
        "ocr" => "Image to text", "fc" => "Flashcards", "words" => "Words", "stats" => "Stats", "lewis" => "Lewis", "molecule" => "Molecules", "atoms" => "Atoms",
        "balance" => "Balance",
        "cell" => "Cells", "dna" => "DNA", "gene" => "Genes", "biomol" => "Biomolecules", "cross" => "Crosses", "divide" => "Division",
        "popgen" => "Hardy–Weinberg", "biolab" => "Lab maths", "enzyme" => "Enzymes", "energy" => "Energy",
        "motion" => "Motion", "vectors" => "Vectors", _ => key,
    };

    /// <summary>The width of its pin's flyout (the Mac's popoverWidth).</summary>
    public static double FlyoutWidth(string key) => key switch
    {
        // (the periodic table's a little wider, and its padding less: squares big enough to point at)
        "ptable" => 460, "calc" or "gene" => 420, "cite" or "cross" => 400, "biolab" => 380, "graph" or "popgen" or "balance" or "words" or "stats" => 360, "fc" or "motion" or "vectors" => 340, _ => 330,
    };

    /// <summary>The room inside its pin's flyout round its content.</summary>
    public static double FlyoutPadding(string key) => key == "ptable" ? 10 : 16;

    /// <summary>The files a tool takes from its pin's flyout (null: none), as an open dialog's filter.</summary>
    public static string? Accepts(string key) => key switch
    {
        "conv" => "All files|*.*",
        "pdfx" or "mark" => "PDF documents|*.pdf",
        "ocr" => "Pictures and PDFs|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.heic;*.webp;*.pdf",
        _ => null,
    };

    public static ToolsScreen.Kind? KindOf(string key) => ToolsScreen.Kinds.FirstOrDefault(k => k.Key == key);
}

// ---- what the tools share (the Mac's ToolPage, ToolColumns, ToolField, ToolNote, ToolDropZone, ToolFiles) ----

/// <summary>The pieces every tool is made of, so they look one family.</summary>
public static class ToolKit
{
    /// <summary>A tool's scrolling body: the popup's side margins (its heading's), its content as wide as the popup
    /// allows up to its widest, and centred past that (the Mac's ToolPage).</summary>
    public static ScrollViewer Page(UIElement content, double maxWidth = 1480, double spacing = 20)
    {
        if (content is FrameworkElement fe) Fit(fe, maxWidth);
        var sv = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(28, 8, 28, 32), Focusable = false,
        };
        sv.SizeChanged += (_, _) => sv.Padding = sv.ActualWidth < 560 ? new Thickness(16, 8, 16, 24) : new Thickness(28, 8, 28, 32);
        return sv;
    }

    /// <summary>A tool's scrolling body made of parts one under another.</summary>
    public static ScrollViewer Page(double spacing, params UIElement?[] parts) => Page(K.V(spacing, parts));

    /// <summary>
    /// A page's content given the page's whole width up to its widest (a page kept, with new content, too): its columns
    /// share out the page, not what their content happens to ask for.
    /// </summary>
    public static void Fit(FrameworkElement content, double maxWidth)
    {
        content.MaxWidth = maxWidth;
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>
    /// Two columns side by side on a wide window (the work and its side panel), one over the other on a narrow one — the
    /// side first there when it holds the tool's answer (<paramref name="sideFirst"/>), so the answer is never under its
    /// inputs. The breakpoints are for the tool's popup, as large as the page beside the navigation pane less a margin
    /// (about 960 of content in a 1366 window, 870 in a 1280 one).
    /// </summary>
    public static FrameworkElement Columns(UIElement main, UIElement side, double sideWidth = 380, double breakpoint = 860, double spacing = 28, bool sideFirst = false)
    {
        if (main is FrameworkElement m) m.VerticalAlignment = VerticalAlignment.Top;
        if (side is FrameworkElement s) s.VerticalAlignment = VerticalAlignment.Top;
        return new ToolColumns(main, side, sideWidth, breakpoint, spacing, sideFirst);
    }

    /// <summary>A labelled field: its label over it, a hint under it, a word after it (a unit, "%").</summary>
    public static FrameworkElement Field(string label, TextBox box, string? hint = null, string? suffix = null, double? width = null)
    {
        var row = suffix == null ? (UIElement)box : K.Columns("*, auto", box.Col(0), K.Text(suffix, Ty.Body, B.Text2).Margin(8, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1));
        if (row is FrameworkElement r) { r.HorizontalAlignment = width == null ? HorizontalAlignment.Stretch : HorizontalAlignment.Left; if (width is { } w) r.Width = w; }
        var v = K.V(6, K.Text(label, Ty.BodyStrong, B.Text2), row);
        if (!string.IsNullOrEmpty(hint)) v.Children.Add(K.Wrapped(hint, Ty.Caption, B.Text2));
        return v;
    }

    /// <summary>A text box as the tools have them.</summary>
    public static TextBox Box(string text = "", string placeholder = "", Action<string>? changed = null)
    {
        var b = new TextBox { Text = text, PlaceholderText = placeholder, ClearButtonEnabled = false, MinHeight = 32 };
        if (changed != null) b.TextChanged += (_, _) => changed(b.Text);
        return b;
    }

    /// <summary>A short line that says something went wrong or needs doing.</summary>
    public static FrameworkElement Note(string text, SymbolRegular symbol = SymbolRegular.Warning24, Color? tint = null)
    {
        var icon = K.Icon(symbol, 16, tint ?? Color.FromRgb(0xf7, 0x63, 0x0c), filled: true);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 8, 0);
        return K.Columns("auto, *", icon.Col(0), K.Wrapped(text, Ty.Body).Col(1));
    }

    /// <summary>
    /// Where a tool takes its files: dropped on it from File Explorer, or chosen with a press — a dashed box in the tool's
    /// colour, brighter while files are over it.
    /// </summary>
    public static FrameworkElement DropZone(string title, string? sub, string filter, bool multiple, Color tint, Action<IReadOnlyList<string>> onFiles,
        SymbolRegular symbol = SymbolRegular.ArrowDownload24, double height = 170)
    {
        var dash = new Rectangle { RadiusX = 12, RadiusY = 12, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3.3 }, Stroke = new SolidColorBrush(K.WithAlpha(tint, 0.4)), Fill = new SolidColorBrush(K.WithAlpha(tint, 0.05)) };
        var words = K.V(10, K.Icon(symbol, 30, tint).Align(HorizontalAlignment.Center), K.Text(title, Ty.BodyStrong).Center());
        if (sub != null) words.Children.Add(K.Wrapped(sub, Ty.Body, B.Text2).Center());
        words.VerticalAlignment = VerticalAlignment.Center;
        words.Margin = new Thickness(20);
        var g = new Grid { MinHeight = height, Children = { dash, words } };
        var b = new Button { Content = g, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Cursor = Cursors.Hand, AllowDrop = true };
        b.SetResourceReference(FrameworkElement.StyleProperty, "SubtleButton");
        K.SetCorner(b, new CornerRadius(12));
        void Over(bool on)
        {
            dash.Stroke = new SolidColorBrush(K.WithAlpha(tint, on ? 0.8 : 0.4));
            dash.Fill = new SolidColorBrush(K.WithAlpha(tint, on ? 0.14 : 0.05));
        }
        b.Click += (_, e) =>
        {
            e.Handled = true;
            var files = Files.Choose(filter, multiple, Window.GetWindow(b));
            if (files.Count > 0) onFiles(files);
        };
        b.DragEnter += (_, e) => { var ok = Dropped(e, filter).Count > 0; e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None; Over(ok); e.Handled = true; };
        b.DragOver += (_, e) => { e.Effects = Dropped(e, filter).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        b.DragLeave += (_, _) => Over(false);
        b.Drop += (_, e) =>
        {
            Over(false);
            var ok = Dropped(e, filter);
            if (ok.Count > 0) onFiles(multiple ? ok : new[] { ok[0] });
            e.Handled = true;
        };
        System.Windows.Automation.AutomationProperties.SetName(b, title);
        return b;
    }

    private static IReadOnlyList<string> Dropped(DragEventArgs e, string filter) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] paths ? Files.Filter(paths, filter) : Array.Empty<string>();

    /// <summary>"1 term", "3 terms".</summary>
    public static string Plural(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

    /// <summary>A section's card of a tool: its heading over its content.</summary>
    public static FrameworkElement Section(string title, UIElement content, UIElement? trailing = null) =>
        K.V(10, trailing == null ? K.Text(title, Ty.BodyStrong) : K.Columns("*, auto", K.Text(title, Ty.BodyStrong).Col(0), trailing.Col(1)), K.Card(content, new Thickness(16)));

    /// <summary>
    /// A hairline under a tool's heading that shows only while the tool scrolls beneath it (Windows 11's scroll edge):
    /// <paramref name="line"/> fades in once <paramref name="body"/>'s own page scroller — as tall as most of the tool, not
    /// a list inside it — has left its top, and out again at the top.
    /// </summary>
    public static void ScrollEdge(UIElement line, FrameworkElement body)
    {
        line.Opacity = 0;
        var shown = false;
        body.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (e.OriginalSource is not ScrollViewer sv || sv.ActualHeight < body.ActualHeight * 0.5) return;
            var on = sv.VerticalOffset > 0.5;
            if (on == shown) return;
            shown = on;
            Motion.To(line, UIElement.OpacityProperty, on ? 1 : 0, Motion.Fast);
        }));
    }

    /// <summary>A tool's view in its popup, with the scroll edge along its top (under the popup's heading), opening at
    /// its top: whatever takes the focus as a tool comes up (a card, a field) is not let scroll the tool down to it, past
    /// its title and its first controls.</summary>
    public static FrameworkElement Edged(FrameworkElement body)
    {
        var line = K.Divider();
        line.VerticalAlignment = VerticalAlignment.Top;
        line.IsHitTestVisible = false;
        ScrollEdge(line, body);
        var g = new Grid { Children = { body, line } };
        void Top(object? sender, RoutedEventArgs e)
        {
            g.Loaded -= Top;
            g.Dispatcher.BeginInvoke(() => { if (PageScroller(body) is { } sv) sv.ScrollToTop(); }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
        g.Loaded += Top;
        return g;
    }

    /// <summary>A tool's own page scroller: the first scroller in it as tall as most of the tool (not a list inside it).</summary>
    private static ScrollViewer? PageScroller(FrameworkElement body)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(body);
        while (queue.Count > 0)
        {
            var d = queue.Dequeue();
            if (d is ScrollViewer sv && sv.ActualHeight >= body.ActualHeight * 0.5) return sv;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) queue.Enqueue(VisualTreeHelper.GetChild(d, i));
        }
        return null;
    }
}

/// <summary>
/// The work and its side panel (the Mac's ToolColumns): side by side when the width given holds the breakpoint, the
/// side at its own width and the work taking the rest, one over the other below that. The choice is made from the width
/// offered as it is measured, never from its own size, and it takes the whole of that width either way — so its width
/// cannot feed back into the choice, and it lands the same whatever order the layout happens in.
/// </summary>
internal sealed class ToolColumns : Panel
{
    private readonly UIElement main, side;
    private readonly double sideWidth, breakpoint, spacing;
    private readonly bool sideFirst;

    public ToolColumns(UIElement main, UIElement side, double sideWidth, double breakpoint, double spacing, bool sideFirst = false)
    {
        this.main = main;
        this.side = side;
        this.sideWidth = sideWidth;
        this.breakpoint = breakpoint;
        this.spacing = spacing;
        this.sideFirst = sideFirst;
        Children.Add(main);
        Children.Add(side);
    }

    private bool Wide(double width) => !double.IsInfinity(width) && width >= breakpoint;

    private double MainWidth(double width) => Math.Max(0, width - spacing - sideWidth);

    protected override Size MeasureOverride(Size available)
    {
        var w = available.Width;
        if (Wide(w))
        {
            main.Measure(new Size(MainWidth(w), available.Height));
            side.Measure(new Size(sideWidth, available.Height));
            return new Size(w, Math.Max(main.DesiredSize.Height, side.DesiredSize.Height));
        }
        main.Measure(new Size(w, double.PositiveInfinity));
        side.Measure(new Size(w, double.PositiveInfinity));
        // (no width to fill, as in a row: as wide as the wider of the two)
        var width = double.IsInfinity(w) ? Math.Max(main.DesiredSize.Width, side.DesiredSize.Width) : w;
        return new Size(width, main.DesiredSize.Height + spacing + side.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var w = final.Width;
        if (Wide(w))
        {
            main.Arrange(new Rect(0, 0, MainWidth(w), final.Height));
            side.Arrange(new Rect(MainWidth(w) + spacing, 0, sideWidth, final.Height));
        }
        else
        {
            // (one over the other: the work, then its panel — or the panel first, when it holds the answer)
            var first = sideFirst ? side : main;
            var second = sideFirst ? main : side;
            var top = first.DesiredSize.Height;
            first.Arrange(new Rect(0, 0, w, top));
            second.Arrange(new Rect(0, top + spacing, w, second.DesiredSize.Height));
        }
        return final;
    }
}

/// <summary>Windows' own dialogs for a tool's files, and the checks on what was dropped (the Mac's ToolFiles).</summary>
public static class Files
{
    public static IReadOnlyList<string> Choose(string filter, bool multiple, Window? owner = null, string? title = null)
    {
        var d = new OpenFileDialog { Filter = filter, Multiselect = multiple, CheckFileExists = true };
        if (title != null) d.Title = title;
        return d.ShowDialog(owner) == true ? d.FileNames : Array.Empty<string>();
    }

    public static string? SaveAs(string name, string filter, Window? owner = null, string? title = null)
    {
        var d = new SaveFileDialog { FileName = name, Filter = filter, AddExtension = true, OverwritePrompt = true };
        if (title != null) d.Title = title;
        return d.ShowDialog(owner) == true ? d.FileName : null;
    }

    public static string? ChooseFolder(string title, Window? owner = null)
    {
        var d = new OpenFolderDialog { Title = title };
        return d.ShowDialog(owner) == true ? d.FolderName : null;
    }

    /// <summary>The files a filter takes ("PDF documents|*.pdf"; anything for *.*).</summary>
    public static IReadOnlyList<string> Filter(IEnumerable<string> paths, string filter)
    {
        var exts = filter.Split('|').Where((_, i) => i % 2 == 1).SelectMany(p => p.Split(';')).Select(p => p.Trim().TrimStart('*').ToLowerInvariant()).ToList();
        var any = exts.Contains(".*") || exts.Count == 0;
        return paths.Where(File.Exists).Where(p => any || exts.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant())).ToList();
    }

    /// <summary>A name for a new file that does not take the place of one already in the folder.</summary>
    public static string Free(string name, string ext, string folder)
    {
        var path = System.IO.Path.Combine(folder, $"{name}.{ext}");
        for (var n = 2; File.Exists(path); n++) path = System.IO.Path.Combine(folder, $"{name} {n}.{ext}");
        return path;
    }

    public static string SizeText(string path)
    {
        double size = File.Exists(path) ? new FileInfo(path).Length : 0;
        string[] units = { "bytes", "KB", "MB", "GB" };
        var u = 0;
        while (size >= 1000 && u < units.Length - 1) { size /= 1000; u++; }
        return u == 0 ? $"{size:0} {units[u]}" : $"{size:0.#} {units[u]}";
    }

    /// <summary>The finished file shown in File Explorer.</summary>
    public static void Reveal(IEnumerable<string> paths)
    {
        var first = paths.FirstOrDefault();
        if (first == null) return;
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{first}\""); } catch (Exception e) { Log.Write($"reveal: {e.Message}"); }
    }

    /// <summary>A file opened in its own app.</summary>
    public static void OpenInApp(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Log.Write($"open: {e.Message}"); }
    }
}

// ---- a tool opened whole, in a popup over the window (the Mac's ToolPopup) ----

/// <summary>
/// A tool opened whole (the Mac's ToolPopup) — from its tile on the Tools page, a pin's menu or flyout, a command, a link
/// or another tool handing it something — in a popup floating over where you are (Shell/FloatPopup.cs): as large as the
/// window under its title bar less a margin — over the pane too, so the tool has the window's whole width — and resized
/// with the window; sized by its edges and corners, the size kept for that tool the next time it opens. Nothing is
/// dimmed: the Tools page (or whatever shows) behind it, the title bar and its search still at hand. Its heading: the
/// tool's tile and name (and the page of its own that shows, "Tool › Page", the name going back to its first page), its
/// line, its own controls (a biology tool's pages, the Periodic Table's search and tour), its pin and ×; the popup is
/// moved by it. It
/// opens at the tool's top, a hairline under the heading once the tool scrolls beneath it; its own pages, and another
/// tool it hands something to, change inside it. One popup a tool: opened again, the one there comes forward.
/// </summary>
public sealed class ToolPopup
{
    private static readonly List<ToolPopup> Open = new();

    private readonly Engine.Engine engine;
    private readonly Sheet sheet;
    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private string key;
    private bool closingSoon;

    private ToolPopup(string key, Engine.Engine engine)
    {
        this.key = key;
        this.engine = engine;
        sheet = new Sheet { Floats = true, Large = true, Beside = true, Bare = true, OwnClose = true, Body = host, Name = ToolsCenter.KindOf(key)?.Name, SizeKey = SizeKeyOf(key) };
        sheet.Closed += () => Open.Remove(this);
    }

    /// <summary>The tool's popup over the window: the one already open comes forward (made again when something was
    /// handed to it, so it takes it); else a new one.</summary>
    public static void Show(string key, Engine.Engine engine, Window? owner)
    {
        if (ToolsCenter.KindOf(key) == null) return;
        if (Open.FirstOrDefault(p => p.key == key) is { } there)
        {
            there.closingSoon = false;
            if (ToolsCenter.Handed(key)) there.Make(key, fade: true);
            there.sheet.Window?.Activate();
            return;
        }
        owner ??= Application.Current?.MainWindow;
        if (owner == null) return;
        var p = new ToolPopup(key, engine);
        Open.Add(p);
        Simpl.Shell.FloatPopup.Show(p.sheet, owner);
        p.Make(key, fade: false);
        if (Shot.Asked || Launch.IsDevelopment) Log.Write($"tool popup open: {key}");
    }

    /// <summary>A tool handing something to another from inside its popup (<paramref name="from"/>, the popup's window):
    /// the popup turns into the other tool where it is — another popup of that tool closing — and comes forward. False
    /// when <paramref name="from"/> is not a tool's popup.</summary>
    public static bool Change(Window? from, string key)
    {
        if (from == null || Open.FirstOrDefault(p => ReferenceEquals(p.sheet.Window, from)) is not { } p) return false;
        p.closingSoon = false;
        foreach (var other in Open.Where(o => o != p && o.key == key).ToList()) other.sheet.Close();
        p.Make(key, fade: true);
        p.sheet.Window?.Activate();
        // (the keyboard to the new tool's first control, as a popup opening gives it: the button pressed to hand over went
        //  with the old tool, and the keys went nowhere — Tab starting again at the window)
        p.host.Dispatcher.BeginInvoke(() =>
        {
            if (!p.host.IsKeyboardFocusWithin && p.host.Content is FrameworkElement g) SheetHost.FocusFirst(p.sheet, g);
        }, DispatcherPriority.Input);
        if (Shot.Asked || Launch.IsDevelopment) Log.Write($"tool popup changed to: {key}");
        return true;
    }

    /// <summary>The tool made in the popup, with its heading: the popup is in place first, so the tool's dialogs (and a
    /// hand-off) know the window it is in.</summary>
    private void Make(string toolKey, bool fade)
    {
        if (ToolsCenter.KindOf(toolKey) is not { } kind) return;
        key = toolKey;
        var window = sheet.Window;
        // (Close: the popup closed a moment later — a tool handing something to another closes itself first, then asks
        //  for the other, which takes this popup's place instead; what the tool tells its heading as it is made is kept
        //  for the heading made after it)
        var told = new ToolHeading.Told();
        var c = new ToolContext { Engine = engine, Key = toolKey, Owner = window, Close = CloseSoon, ShowPage = told.Page, ShowNote = told.Note };
        var body = ToolRegistry.Full(c) ?? Missing(kind);
        var close = K.IconButton(SymbolRegular.Dismiss24, () => sheet.Close(), $"Close {kind.Name} (Esc)", 16, 36);
        var head = ToolHeader.Make(kind, c, close);
        told.Heading = head;
        head.Margin = new Thickness(28, 16, 14, 12);
        var g = K.Rows("auto, *", head.Row(0), ToolKit.Edged(body).Row(1));
        g.Bg(B.Window);
        host.Content = g;
        sheet.Handle = head;
        sheet.Name = kind.Name;
        sheet.SizeKey = SizeKeyOf(toolKey);
        if (window != null) window.Title = kind.Name;
        if (fade) Motion.FadeIn(g, Motion.Medium);
        Shot.Moved();
    }

    /// <summary>What the size the student gives a tool's popup is kept under (Shell/FloatPopup.cs): each tool its own.</summary>
    private static string SizeKeyOf(string toolKey) => $"tool:{toolKey}";

    /// <summary>The tool asked its popup closed: closed once whatever it asks next has had its turn (a hand-off to another
    /// tool, BioTools.Open, comes on the next turn and keeps it).</summary>
    private void CloseSoon()
    {
        closingSoon = true;
        Application.Current?.Dispatcher.BeginInvoke(() => { if (closingSoon) sheet.Close(); }, DispatcherPriority.ContextIdle);
    }

    /// <summary>A tool not here yet: says so.</summary>
    public static FrameworkElement Missing(ToolsScreen.Kind kind)
    {
        var v = K.V(14, K.IconTile(kind.Symbol, kind.Color, 56).Align(HorizontalAlignment.Center),
            K.Text(kind.Name, Ty.Subtitle).Center(),
            K.Wrapped($"{kind.Name} is on its way to Simpl for Windows.", Ty.Body, B.Text2).Center());
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.Margin = new Thickness(40, 0, 40, 60);
        return new Grid { Children = { v } };
    }
}

/// <summary>
/// A tool's heading in its popup: its tile, its name — and the page of its own that shows, after it ("Tool › Page", the
/// tool's name going back to its first page) — and its line, its own controls (a biology tool's pages, the Periodic
/// Table's search and tour), its pin and the popup's ×. Where the popup is too narrow for its controls beside its name,
/// they go on a line of their own under it.
/// </summary>
public sealed class ToolHeading : Grid
{
    private readonly ToolsScreen.Kind kind;
    /// <summary>The tool's name, and the page of its own after it — that page's name cut short with "…" where the popup is
    /// too narrow for both, the tool's name (the way back) kept whole.</summary>
    private readonly DockPanel titleRow = new() { LastChildFill = true };
    private readonly TextBlock note;
    private readonly FrameworkElement? extra;
    private readonly FrameworkElement tile, words, pin;
    private readonly FrameworkElement? close;
    private bool noteWanted = true;
    private bool? below;

    internal ToolHeading(ToolsScreen.Kind kind, ToolContext c, UIElement? close)
    {
        this.kind = kind;
        note = K.Text(kind.Note, Ty.Body, B.Text2);
        words = K.V(2, titleRow, note);
        Page(null, null);
        Note(true);
        foreach (var w in new[] { "auto", "*", "auto", "auto", "auto" }) ColumnDefinitions.Add(new ColumnDefinition { Width = K.Length(w) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tile = K.IconTile(kind.Symbol, kind.Color, 44).Margin(0, 0, 14, 0);
        Children.Add(tile.Col(0));
        words.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(words.Col(1));
        if (ToolRegistry.Header(c) is { } e) { extra = e; e.VerticalAlignment = VerticalAlignment.Center; Children.Add(e.Col(2)); }
        // (a header full of the tool's own controls — a biology tool's pages — the pin a symbol alone)
        pin = ToolHeader.PinButton(kind, squished: ToolHeader.Squished(kind.Key));
        Children.Add(pin.Col(3));
        if (close is FrameworkElement x) { this.close = x; x.Margin = new Thickness(8, 0, 0, 0); x.VerticalAlignment = VerticalAlignment.Center; Children.Add(x.Col(4)); }
        Lay(false);
        SizeChanged += (_, e2) => { if (e2.WidthChanged) Fits(); };
    }

    /// <summary>The tool's own controls beside its name while they fit there, else on a line under it.</summary>
    private void Fits()
    {
        if (extra == null || ActualWidth <= 0) return;
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (var e in new[] { tile, titleRow, extra, pin, close }) e?.Measure(infinite);
        var need = tile.DesiredSize.Width + Math.Min(titleRow.DesiredSize.Width, 260) + 24 + extra.DesiredSize.Width + pin.DesiredSize.Width + (close?.DesiredSize.Width ?? 0);
        Lay(need > ActualWidth);
    }

    private void Lay(bool under)
    {
        if (extra == null || below == under) return;
        below = under;
        if (under)
        {
            Grid.SetRow(extra, 1);
            Grid.SetColumn(extra, 0);
            Grid.SetColumnSpan(extra, 5);
            extra.HorizontalAlignment = HorizontalAlignment.Left;
            extra.Margin = new Thickness(0, 12, 0, 0);
        }
        else
        {
            Grid.SetRow(extra, 0);
            Grid.SetColumn(extra, 2);
            Grid.SetColumnSpan(extra, 1);
            extra.HorizontalAlignment = HorizontalAlignment.Right;
            extra.Margin = new Thickness(12, 0, 12, 0);
        }
    }

    /// <summary>The page of the tool's own that shows, after its name; null, its first page.</summary>
    public void Page(string? name, Action? back)
    {
        titleRow.Children.Clear();
        if (name == null)
        {
            titleRow.Children.Add(K.Text(kind.Name, Ty.Title));
            Note(noteWanted);
            return;
        }
        var t = K.Text(kind.Name, Ty.Title, back != null ? B.Text2 : B.Text);
        if (back != null)
        {
            var b = K.Subtle(t, back, new Thickness(2, 0, 2, 0));
            b.Cursor = Cursors.Hand;
            b.ToolTip = $"Back to the {kind.Name.ToLowerInvariant()}";
            titleRow.Children.Add(b);
        }
        else titleRow.Children.Add(t);
        titleRow.Children.Add(K.Icon(SymbolRegular.ChevronRight20, 16, B.Text2).Margin(10, 6, 10, 0));
        var shown = K.Text(name, Ty.Title);
        shown.TextTrimming = TextTrimming.CharacterEllipsis;
        shown.ToolTip = name;
        titleRow.Children.Add(shown);
        note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the name shown, or not (the Periodic Table has none).</summary>
    public void Note(bool on)
    {
        noteWanted = on;
        note.Visibility = on && kind.Key != "ptable" && kind.Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>What a tool tells its heading (ToolContext.ShowPage, ShowNote) — kept from before the heading is made (the
    /// tool is made first), and passed on once it is.</summary>
    internal sealed class Told
    {
        private string? page;
        private Action? back;
        private bool note = true;
        private ToolHeading? heading;

        public ToolHeading? Heading
        {
            get => heading;
            set
            {
                heading = value;
                heading?.Note(note);
                if (page != null) heading?.Page(page, back);
            }
        }

        public void Page(string? name, Action? toFirst)
        {
            page = name;
            back = toFirst;
            heading?.Page(name, toFirst);
        }

        public void Note(bool on)
        {
            note = on;
            heading?.Note(on);
        }
    }
}

/// <summary>A tool's heading and its pin, where the tool opens whole.</summary>
public static class ToolHeader
{
    public static ToolHeading Make(ToolsScreen.Kind kind, ToolContext c, UIElement? close) => new(kind, c, close);

    /// <summary>A tool whose own controls fill its heading (a biology tool's pages): its pin a symbol alone.</summary>
    public static bool Squished(string key) => BioTools.Pages(key).Count > 1;

    /// <summary>
    /// A tool's pin in its heading, named for what a press does (the Mac's "Pin to Toolbar" / "Unpin from Toolbar"): "Pin
    /// to title bar", or "Unpin" while it is pinned — or, in a heading full of the tool's own controls, its symbol alone.
    /// </summary>
    public static FrameworkElement PinButton(ToolsScreen.Kind kind, bool squished = false)
    {
        var host = new ContentControl { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        void Draw()
        {
            var on = ToolsPins.IsPinned(kind.Key);
            var b = K.Button(on ? "Unpin" : "Pin to title bar", () => ToolsPins.Toggle(kind.Key), icon: on ? SymbolRegular.PinOff24 : SymbolRegular.Pin24);
            if (squished)
            {
                b.Icon = null;
                b.Content = K.Icon(on ? SymbolRegular.PinOff24 : SymbolRegular.Pin24, 16, B.Text);
                b.MinWidth = 0;
                b.Padding = new Thickness(10, 6, 10, 6);
            }
            b.ToolTip = on ? $"Take {kind.Name} out of the title bar" : $"Keep {kind.Name} in the title bar on every screen";
            System.Windows.Automation.AutomationProperties.SetName(b, on ? $"Unpin {kind.Name}" : $"Pin {kind.Name} to the title bar");
            host.Content = b;
        }
        Draw();
        void Changed() => Draw();
        host.Loaded += (_, _) => { ToolsPins.Changed -= Changed; ToolsPins.Changed += Changed; Draw(); };
        host.Unloaded += (_, _) => ToolsPins.Changed -= Changed;
        return host;
    }
}

// ---- the pins in the title bar (the Mac's PinnedToolButton, PinnedTimerLabel, PinnedToolPopover) ----

/// <summary>
/// The tools pinned to the title bar, on every screen: a button each in its tool's colour; a press opens a flyout with
/// the tool's quickest use under its name — (0.3.5) Open full screen at its top right opens the whole tool with what was
/// begun in it. A running focus timer is live in its pin (it borrows a pin while it runs if the timer is not pinned). A
/// pin's menu opens it, pops it out — the quick version in a small window of its own (ToolFloat) — moves or unpins it.
/// The same pins sit in the heading of a popup (a quiz, which covers the title bar, Knewton Alta, Macmillan: inPopup),
/// their flyouts over it; the whole tool opens in its popup over the window either way, above them (ToolPopup).
/// </summary>
public sealed class PinBar : StackPanel
{
    private readonly Engine.Engine engine;
    private readonly bool inPopup;
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private ITimerState? timer;
    private Popup? flyout;
    private string? flyoutKey;
    /// <summary>The flyout stays until it is closed by hand (the screenshot suite's --pin-open: another copy of Simpl's
    /// window coming forward would otherwise fold it away before its picture).</summary>
    private bool flyoutKept;
    private Window? flyoutWindow;
    /// <summary>(0.3.5) The open flyout's Open full screen, as its button presses it (the screenshot suite's --pin-full).</summary>
    private Action? flyoutFull;

    public PinBar(Engine.Engine engine, bool inPopup = false)
    {
        this.engine = engine;
        this.inPopup = inPopup;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0, 0, 6, 0);
        // (the screenshot suite: --pin calc,ptable pins those tools first)
        if (Launch.Take("", "pin") is { Length: > 0 } pinned) Prefs.Set("pinnedTools", pinned);
        tick.Tick += (_, _) => { if (timer?.Active == true) Draw(); };
        // (heard only while it is on screen: a popup's pins go with the popup, never kept alive by the pins' event)
        Loaded += (_, _) => { ToolsPins.Changed -= Draw; ToolsPins.Changed += Draw; Hook(); Draw(); };
        Unloaded += (_, _) =>
        {
            ToolsPins.Changed -= Draw;
            if (timer != null) timer.Changed -= Draw;
            timer = null;
            tick.Stop();
            CloseFlyout("gone");
        };
        Draw();
        // (the screenshot suite: --pin-open ptable opens that pin's flyout a moment after launch, and keeps it open: CI
        //  starts three copies of Simpl at once, and another's window coming forward would close a flyout that closes when
        //  its window is left, so the picture showed the bar without it)
        if (!inPopup && Launch.Take("", "pin-open") is { Length: > 0 } k)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                OpenFlyout(k, kept: true);
            };
            t.Start();
        }
        // (CI's check that a flyout stays open while it is used: --pin-try graph opens that pin's flyout as a press does —
        //  not kept — then clicks into it and types, as a student does, and says in the log whether it is still open
        //  five seconds on; run with no other copy of Simpl beside it)
        if (!inPopup && Launch.Take("", "pin-try") is { Length: > 0 } tk)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                OpenFlyout(tk, kept: false);
                PinTry(tk);
            };
            t.Start();
        }
        // (the screenshot suite: --pin-full need opens that pin's flyout — kept, as --pin-open's — and presses its Open full
        //  screen two seconds on; the graph's four seconds after its Desmos has loaded, and had --pin-graph drawn in it)
        if (!inPopup && Launch.Take("", "pin-full") is { Length: > 0 } fk)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
            var ticks = 0;
            var loaded = -1;
            t.Tick += (_, _) =>
            {
                ticks++;
                if (ticks == 5) OpenFlyout(fk, kept: true);
                if (ticks <= 5) return;
                if (loaded < 0 && (fk != "graph" || !GraphWeb.Mini.Loading || ticks > 65)) loaded = ticks;
                if (loaded < 0 || ticks - loaded < (fk == "graph" ? 8 : 4)) return;
                t.Stop();
                if (flyoutKey == fk && flyoutFull is { } press) press();
                else Log.Write($"pin full screen: no flyout for {fk}");
            };
            t.Start();
        }
        // (the screenshot suite: --pin-popout calc pulls that pin's quick version out into its window of its own, a few
        //  seconds after launch — over a quiz's popup, say, as a student keeps it beside the questions)
        if (!inPopup && Launch.Take("", "pin-popout") is { Length: > 0 } pk)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (Shot.Asked) Log.Write($"pin popped out: {pk}");
                ToolFloat.Show(pk, engine, Window.GetWindow(this) ?? Application.Current?.MainWindow);
            };
            t.Start();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);

    /// <summary>--pin-try: the open flyout used as a student uses it — a click in it (Desmos's graph; the heading beside
    /// the sets), the keyboard moved into it and a key typed — then, five seconds on, whether it is still open.</summary>
    private void PinTry(string key)
    {
        void After(double s, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(s) };
            t.Tick += (_, _) => { t.Stop(); a(); };
            t.Start();
        }
        After(2, () =>
        {
            if (flyout?.Child is not FrameworkElement card || !card.IsLoaded) { Log.Write($"pin try: no flyout for {key}"); return; }
            // (Desmos's graph, in the flyout's middle; else the heading's empty middle, beside the tool's name — a row
            //  pressed would open the whole tool, which closes the flyout as it should)
            var at = key == "graph"
                ? card.PointToScreen(new Point(card.ActualWidth / 2, card.ActualHeight / 2))
                : card.PointToScreen(new Point(card.ActualWidth * 0.62, ToolsCenter.FlyoutPadding(key) + 14));
            SetCursorPos((int)at.X, (int)at.Y);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // (left down)
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // (left up)
            Log.Write($"pin try: clicked in the {key} flyout");
            After(0.8, () =>
            {
                if (flyout?.Child is FrameworkElement c2 && !c2.IsKeyboardFocusWithin) c2.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                keybd_event(0x58, 0, 0, UIntPtr.Zero); // (x)
                keybd_event(0x58, 0, 0x0002, UIntPtr.Zero);
                Log.Write($"pin try: typed in the {key} flyout");
                After(5, () => Log.Write(flyout != null && flyoutKey == key ? $"pin flyout still open: {key}" : $"pin flyout gone: {key}"));
            });
        });
    }

    /// <summary>The flyout's own window (a popup is one) in front: the keyboard gone into a part of the flyout that is a
    /// window of its own — not the student leaving for another app.</summary>
    private bool FlyoutInFront()
    {
        if (flyout?.Child is not Visual card || PresentationSource.FromVisual(card) is not System.Windows.Interop.HwndSource src) return false;
        var front = GetForegroundWindow();
        return front == src.Handle || IsChild(src.Handle, front);
    }

    private void Hook()
    {
        if (ReferenceEquals(timer, ToolRegistry.Timer)) return;
        if (timer != null) timer.Changed -= Draw;
        timer = ToolRegistry.Timer;
        if (timer != null) timer.Changed += Draw;
    }

    private List<string> Pins()
    {
        var pins = ToolsPins.Pinned.ToList();
        if (ToolRegistry.Timer?.Active == true && !pins.Contains("pomo")) pins.Add("pomo");
        return pins;
    }

    /// <summary>The pins as drawn, by key, and what they were drawn for (the keys, and whether the timer was live).</summary>
    private readonly Dictionary<string, Button> buttons = new();
    private string drawn = "";
    private bool drawnOnce;
    /// <summary>The pins taken out, on their way (the bar is drawn again once they have gone).</summary>
    private HashSet<string>? going;

    /// <summary>The pins drawn again when they change (pinned, unpinned, moved, the timer started or stopped); each second
    /// of a running timer only its pin's face is (the buttons stay, so a flyout or a menu stays where it hangs). A pin
    /// taken out leaves first, and the rest close up after; a pin just pinned comes in where it lands; a pin moved is
    /// simply in its new place.</summary>
    private void Draw() => Draw(again: false);

    private void Draw(bool again)
    {
        Hook();
        var live = timer?.Active == true;
        if (live && !tick.IsEnabled) tick.Start();
        if (!live && tick.IsEnabled) tick.Stop();
        if (going != null) return;
        var pins = Pins();
        var now = string.Join(",", pins) + (live ? "|live" : "");
        if (now == drawn && !again)
        {
            if (live && buttons.TryGetValue("pomo", out var tb) && ToolsCenter.KindOf("pomo") is { } pk) Dress(tb, pk);
            return;
        }
        var leaving = buttons.Keys.Where(k => !pins.Contains(k)).ToList();
        if (leaving.Count > 0 && Motion.On)
        {
            going = leaving.ToHashSet();
            foreach (var k in leaving) Leave(buttons[k], k == leaving[^1] ? Gone : null);
            return;
        }
        var before = drawnOnce ? buttons.Keys.ToHashSet() : null;
        drawn = now;
        drawnOnce = true;
        Children.Clear();
        buttons.Clear();
        foreach (var key in pins)
            if (ToolsCenter.KindOf(key) is { } kind)
            {
                var b = buttons[key] = Pin(kind);
                Children.Add(b);
                if (before != null && !before.Contains(key)) Arrive(b);
            }
        if (flyout != null && flyoutKey != null)
        {
            if (buttons.TryGetValue(flyoutKey, out var nb)) flyout.PlacementTarget = nb; else CloseFlyout("unpinned");
        }
    }

    /// <summary>The pins taken out have gone: the bar drawn as things are now (one pinned again meanwhile comes in anew).</summary>
    private void Gone()
    {
        if (going == null) return;
        foreach (var k in going) buttons.Remove(k);
        going = null;
        Draw(again: true);
    }

    /// <summary>A pin just pinned: its symbol up from a touch smaller, fading in (Motion.Medium), so the eye finds where it
    /// went. Its face only — not the button and its hover fill.</summary>
    private static void Arrive(Button b)
    {
        if (b.Content is not FrameworkElement face) return;
        var s = new ScaleTransform(0.9, 0.9);
        face.RenderTransformOrigin = new Point(0.5, 0.5);
        face.RenderTransform = s;
        Motion.FromTo(s, ScaleTransform.ScaleXProperty, 0.9, 1, Motion.Medium, Motion.Decelerate);
        Motion.FromTo(s, ScaleTransform.ScaleYProperty, 0.9, 1, Motion.Medium, Motion.Decelerate);
        Motion.FadeIn(face, Motion.Normal);
    }

    /// <summary>A pin taken out: its arrival backwards, quicker — the pin fading, its symbol shrinking a touch (Motion.Normal).</summary>
    private static void Leave(Button b, Action? done)
    {
        b.IsHitTestVisible = false;
        if (b.Content is FrameworkElement face)
        {
            if (face.RenderTransform is not ScaleTransform { IsFrozen: false } s)
            {
                s = new ScaleTransform(1, 1);
                face.RenderTransformOrigin = new Point(0.5, 0.5);
                face.RenderTransform = s;
            }
            Motion.To(s, ScaleTransform.ScaleXProperty, 0.9, Motion.Normal, Motion.Decelerate);
            Motion.To(s, ScaleTransform.ScaleYProperty, 0.9, Motion.Normal, Motion.Decelerate);
        }
        Motion.To(b, OpacityProperty, 0, Motion.Normal, Motion.Decelerate, 0, done);
    }

    private Button Pin(ToolsScreen.Kind kind)
    {
        var b = K.Subtle(new Grid(), () => OpenFlyout(kind.Key), new Thickness(6, 0, 6, 0), 4);
        b.Height = 32;
        b.MinWidth = 36;
        b.VerticalAlignment = VerticalAlignment.Center;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(b, kind.Name);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.ContextMenu = Menu(kind);
        b.ContextMenuOpening += (_, _) => b.ContextMenu = Menu(kind); // (filled as it opens: the timer's state then)
        Dress(b, kind);
        return b;
    }

    /// <summary>A pin's face: its tool's symbol in its colour; the running timer's, what is left of the phase on a small
    /// ring and in figures, in the phase's colour.</summary>
    private void Dress(Button b, ToolsScreen.Kind kind)
    {
        var live = kind.Key == "pomo" && timer?.Active == true;
        if (live && timer != null)
        {
            var c = timer.PhaseColor;
            var ring = K.Ring(timer.Fraction, c, 15, 2.5);
            var clock = K.Text(Clock(timer.Remaining), Ty.BodyStrong);
            clock.Foreground = new SolidColorBrush(c);
            clock.FontFeatures();
            var row = K.H(6, ring, clock);
            if (timer.Paused)
            {
                var pause = Glyph(SymbolRegular.Pause24, 10);
                pause.SetResourceReference(TextBlock.ForegroundProperty, B.Text2);
                row.Children.Add(pause);
            }
            b.Content = row;
            b.ToolTip = $"Focus timer: {timer.PhaseName.ToLowerInvariant()}{(timer.Paused ? ", paused" : "")}";
        }
        else
        {
            b.Content = Face(kind);
            b.ToolTip = kind.Name;
        }
    }

    /// <summary>
    /// A tool's pin: its symbol, white, on a rounded square of its colour (its tile on the Tools page, solid), so each
    /// stands out on the title bar, light or dark. The symbol is drawn as words in the symbol font: an icon element's
    /// own colour is lost in the title bar (it came out black), a TextBlock's is kept.
    /// </summary>
    private static FrameworkElement Face(ToolsScreen.Kind kind)
    {
        var glyph = Glyph(kind.Symbol, 14);
        glyph.Foreground = Brushes.White;
        return new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(kind.Color),
            Child = glyph, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true,
        };
    }

    /// <summary>A filled symbol as words in the symbol font, for the title bar (see <see cref="Face"/>).</summary>
    private static TextBlock Glyph(SymbolRegular symbol, double size)
    {
        var t = new TextBlock
        {
            Text = symbol.Swap().GetString(), FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center,
        };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FluentSystemIconsFilled");
        return t;
    }

    /// <summary>A pin's menu (Windows' sentence case): open the tool, pop it out, the running timer's own, move it, unpin
    /// it — unpinning is here, a deliberate choice, not a button in the flyout a slip can press.</summary>
    private ContextMenu Menu(ToolsScreen.Kind kind)
    {
        var m = new ContextMenu();
        System.Windows.Controls.MenuItem Item(string text, Action a, bool enabled = true)
        {
            var i = new System.Windows.Controls.MenuItem { Header = text, IsEnabled = enabled };
            i.Click += (_, _) => a();
            return i;
        }
        m.Items.Add(Item($"Open {kind.Name}", () => OpenWhole(kind.Key, null, null)));
        if (ToolRegistry.Has(kind.Key) && ToolsCenter.Accepts(kind.Key) == null) m.Items.Add(Item("Pop out", () => PopOut(kind.Key)));
        if (kind.Key == "pomo" && timer?.Active == true)
        {
            var t = timer;
            m.Items.Add(new Separator());
            m.Items.Add(Item(t.Running ? "Pause" : "Resume", t.Toggle));
            m.Items.Add(Item($"Skip to {t.NextPhaseName.ToLowerInvariant()}", t.Skip));
            m.Items.Add(Item("Cancel session", t.Reset));
        }
        m.Items.Add(new Separator());
        var pins = ToolsPins.Pinned;
        if (ToolsPins.IsPinned(kind.Key))
        {
            m.Items.Add(Item("Move left", () => ToolsPins.Move(kind.Key, -1), pins.FirstOrDefault() != kind.Key));
            m.Items.Add(Item("Move right", () => ToolsPins.Move(kind.Key, 1), pins.LastOrDefault() != kind.Key));
            m.Items.Add(Item("Unpin from title bar", () => ToolsPins.Toggle(kind.Key)));
        }
        else m.Items.Add(Item("Pin to title bar", () => ToolsPins.Toggle(kind.Key)));
        return m;
    }

    public static string Clock(TimeSpan t)
    {
        var s = Math.Max(0, (int)Math.Ceiling(t.TotalSeconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    /// <summary>The whole tool, from a pin's menu or its flyout: in its popup over the window, where you are.</summary>
    private void OpenWhole(string key, Dictionary<string, string>? info, IReadOnlyList<string>? files)
    {
        CloseFlyout("opened");
        ToolsCenter.Show(key, engine, files, info);
    }

    /// <summary>The quick version pulled out of the flyout into a small window of its own (ToolFloat) — (0.3.5) from the
    /// pin's menu, since Open full screen took Pop out's place in the flyout's heading.</summary>
    private void PopOut(string key)
    {
        CloseFlyout("popped out");
        ToolFloat.Show(key, engine, Window.GetWindow(this) ?? Application.Current?.MainWindow);
    }

    /// <summary>
    /// A pin's flyout: the tool's whole name, (0.3.5) Open full screen at its right, and its quickest use — under its pin,
    /// over whatever is up (a flyout is a window of its own). It folds back on a press anywhere else in the window, the
    /// window left, moved or resized, Escape, or its pin pressed again; never while the keyboard moves inside it.
    /// </summary>
    public void OpenFlyout(string key) => OpenFlyout(key, kept: false);

    private void OpenFlyout(string key, bool kept)
    {
        if (flyout != null) { var same = flyoutKey == key; CloseFlyout("pin pressed"); if (same) return; }
        if (ToolsCenter.KindOf(key) is not { } kind) return;
        var anchor = buttons.GetValueOrDefault(key);
        var owner = Window.GetWindow(this);
        void Full(Dictionary<string, string>? info, IReadOnlyList<string>? files) => OpenWhole(key, info, files);
        var c = new ToolContext { Engine = engine, Key = key, Quick = true, Owner = owner, OpenFull = Full, Close = () => CloseFlyout("closed by the tool") };

        void FullScreen()
        {
            var carried = c.Carry();
            CloseFlyout("opened full screen");
            ToolsCenter.FullScreen(key, engine, carried);
        }
        var head = QuickHead(kind, FullScreen);

        FrameworkElement body;
        if (ToolsCenter.Accepts(key) is { } filter)
        {
            var (title, sub, many) = key switch
            {
                "conv" => ("Click to add files", "Documents, PDFs, pictures and data, turned into one another.", true),
                "pdfx" => ("Click to add PDFs", "Put PDFs together, pull pages out, or turn them.", true),
                "mark" => ("Click to add a PDF", "Highlight, underline and add notes; saved in the PDF.", false),
                _ => ("Click to add a picture", "The words in a picture or a scan, as text you can copy.", false),
            };
            body = ToolKit.DropZone(title, sub, filter, many, kind.Color, files => Full(null, files), height: 140);
        }
        else body = ToolRegistry.Quick(c) ?? K.Wrapped(ToolRegistry.Has(key) ? $"Open {kind.Name} to use it." : $"{kind.Name} is on its way to Simpl for Windows.", Ty.Body, B.Text2);

        var card = new Border
        {
            Width = ToolsCenter.FlyoutWidth(key), Padding = new Thickness(ToolsCenter.FlyoutPadding(key)), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Child = K.V(14, head, body), Margin = new Thickness(12, 4, 12, 20), Effect = K.Shadow(24, 6, 0.22),
        };
        card.Bg(B.Flyout);
        card.SetResourceReference(Border.BorderBrushProperty, B.FlyoutStroke);
        card.SetResourceReference(TextElement.ForegroundProperty, B.Text);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        // (Escape folds it back, the keyboard back on its pin — an open drop-down inside it closes first)
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || (Keyboard.FocusedElement is System.Windows.Controls.ComboBox { IsDropDownOpen: true })) return;
            e.Handled = true;
            CloseFlyout("Escape");
            anchor?.Focus();
        };
        // (its right edge under its pin's: the pins sit at the window's right, so the flyout opens towards the page)
        var target = (FrameworkElement?)anchor ?? this;
        // (it stays open by itself — StaysOpen — and folds back as Fold says: a light-dismiss popup also folds when the
        //  keyboard or the mouse goes into a part of its own that is a window of its own, Desmos's)
        flyout = new Popup
        {
            Child = card, StaysOpen = true, AllowsTransparency = true, PlacementTarget = target,
            Placement = PlacementMode.Bottom, HorizontalOffset = target.ActualWidth - (card.Width + 24) + 12, PopupAnimation = PopupAnimation.None,
        };
        flyoutKey = key;
        flyoutKept = kept;
        flyoutFull = FullScreen;
        flyout.IsOpen = true;
        Watch(owner);
        if (Shot.Asked || Launch.IsDevelopment) Log.Write($"pin flyout open: {key}");
        if (Motion.On)
        {
            var t = new TranslateTransform(0, -12);
            card.RenderTransform = t;
            card.Opacity = 0;
            Motion.To(t, TranslateTransform.YProperty, 0, Motion.Medium, Motion.Decelerate);
            Motion.To(card, OpacityProperty, 1, Motion.Fast);
        }
        card.Loaded += (_, _) => card.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        Shot.Moved();
    }

    /// <summary>
    /// A flyout's (or a pulled-out window's) heading: the tool's tile and whole name, and (0.3.5) Open full screen at its
    /// right — the tool whole in its popup over the window, as its tile opens it, with what was begun in the quick version
    /// (ToolsCenter.FullScreen) — where Pop out was (the owner, of the Mac's: "it should be full screen"; Pop out is in the
    /// pin's menu). The name is the heading's title alone: its ›, which opened the whole tool too, is gone, so Open full
    /// screen is the one way from here to the whole tool.
    /// </summary>
    internal static FrameworkElement QuickHead(ToolsScreen.Kind kind, Action fullScreen)
    {
        var name = K.Text(kind.Name, Ty.BodyStrong);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(4, 0, 8, 0);
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(name, System.Windows.Automation.AutomationHeadingLevel.Level1);
        var full = K.IconButton(SymbolRegular.FullScreenMaximize24, fullScreen, "Open full screen", 16, 30);
        System.Windows.Automation.AutomationProperties.SetHelpText(full, $"Opens {kind.Name} whole, over the window, with what you began here");
        return K.Columns("auto, *, auto", K.IconTile(kind.Symbol, kind.Color, 28).Margin(0, 0, 10, 0).Col(0), name.Col(1), full.Col(2));
    }

    /// <summary>What folds the flyout back while it is open: a press in the window outside it (not on its own pin, which
    /// folds it itself), the window left (to another app; not to the flyout), moved or resized.</summary>
    private void Watch(Window? w)
    {
        Unwatch();
        if (w == null) return;
        flyoutWindow = w;
        w.PreviewMouseDown += OnWindowPress;
        w.Deactivated += OnWindowLeft;
        w.LocationChanged += OnWindowMoved;
        w.SizeChanged += OnWindowSized;
    }

    private void Unwatch()
    {
        if (flyoutWindow is not { } w) return;
        w.PreviewMouseDown -= OnWindowPress;
        w.Deactivated -= OnWindowLeft;
        w.LocationChanged -= OnWindowMoved;
        w.SizeChanged -= OnWindowSized;
        flyoutWindow = null;
    }

    private void OnWindowPress(object? sender, MouseButtonEventArgs e)
    {
        if (flyoutKept || flyoutKey == null) return;
        var source = e.OriginalSource as DependencyObject;
        // (a press inside the flyout reaches the window too — a popup hands its content's events to its parent — and is
        //  the flyout's own; its own pin pressed: the pin's click folds it, rather than folding here and opening again there)
        if (flyout?.Child is { } card && Within(source, card)) return;
        if (buttons.GetValueOrDefault(flyoutKey) is { } pin && Within(source, pin)) return;
        CloseFlyout("pressed elsewhere");
    }

    private static bool Within(DependencyObject? d, DependencyObject root)
    {
        for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, root)) return true;
        return false;
    }

    private void OnWindowLeft(object? sender, EventArgs e)
    {
        if (flyoutKept) return;
        // (the keyboard or the pointer gone into the flyout itself — a window of its own inside it, Desmos — is not leaving)
        Dispatcher.BeginInvoke(() =>
        {
            if (flyout?.Child is UIElement card && (card.IsKeyboardFocusWithin || card.IsMouseOver || FlyoutInFront())) return;
            if (flyoutWindow is { IsActive: false }) CloseFlyout("window left");
        }, DispatcherPriority.Input);
    }

    private void OnWindowMoved(object? sender, EventArgs e) { if (!flyoutKept) CloseFlyout("window moved"); }

    private void OnWindowSized(object? sender, SizeChangedEventArgs e) { if (!flyoutKept) CloseFlyout("window resized"); }

    public void CloseFlyout() => CloseFlyout("closed");

    private void CloseFlyout(string why)
    {
        Unwatch();
        if (flyout == null) return;
        var f = flyout;
        var key = flyoutKey;
        flyout = null;
        flyoutKey = null;
        flyoutKept = false;
        flyoutFull = null;
        f.IsOpen = false;
        if (Shot.Asked || Launch.IsDevelopment) Log.Write($"pin flyout closed: {key} ({why})");
    }
}

/// <summary>
/// A pin's quick version pulled out of its flyout (the Mac's ToolFloat): a small window of its own that stays above
/// Simpl's window and its popups — and above other apps, as Windows' Calculator does kept on top — wherever it is moved,
/// until its × closes it. (0.3.5) Its heading's Open full screen, as the flyout's, opens the whole tool with what was
/// begun in it, and closes the window (kept on top, it would sit over the tool). One a tool: pulled out again, the one
/// there comes forward. Where it was is remembered for next time.
/// </summary>
public sealed class ToolFloat : FluentWindow
{
    private static readonly Dictionary<string, ToolFloat> Open = new();
    private readonly string key;

    private ToolFloat(ToolsScreen.Kind kind, Engine.Engine engine, Window? owner)
    {
        key = kind.Key;
        Title = kind.Name;
        Owner = owner;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowBackdropType = Appearance.Backdrop;
        ExtendsContentIntoTitleBar = true;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        SetResourceReference(BackgroundProperty, B.Window);
        Width = ToolsCenter.FlyoutWidth(kind.Key) + 32;
        MaxHeight = 760;
        var c = new ToolContext { Engine = engine, Key = kind.Key, Quick = true, Owner = this, OpenFull = (info, files) => ToolsCenter.Show(kind.Key, engine, files, info), Close = Close };
        var body = ToolRegistry.Quick(c) ?? K.Wrapped($"Open {kind.Name} to use it.", Ty.Body, B.Text2);
        var bar = new TitleBar { Title = "", ShowMinimize = false, ShowMaximize = false, Height = 32 };
        var head = PinBar.QuickHead(kind, () =>
        {
            var carried = c.Carry();
            Close();
            ToolsCenter.FullScreen(kind.Key, engine, carried);
        });
        var content = K.V(14, head, body);
        content.Margin = new Thickness(16, 0, 16, 16);
        Content = K.Rows("auto, auto", bar.Row(0), content.Row(1));
        // (where it was left; the first time, at the window's top right, under the pins and clear of a popup's heading —
        //  its Close and its own pins stay in sight)
        var place = "float-" + kind.Key;
        var first = Prefs.String($"window:{place}") == null;
        Placement.Restore(this, place, Width, 400, 200, 120);
        Width = ToolsCenter.FlyoutWidth(kind.Key) + 32;
        ClearValue(HeightProperty);
        SizeToContent = SizeToContent.Height;
        if (first && owner is { IsLoaded: true } o)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = o.Left + Math.Max(0, o.ActualWidth - Width - 24);
            Top = o.Top + 112;
        }
        LocationChanged += (_, _) => Placement.Save(this, place);
        Closed += (_, _) => Open.Remove(key);
    }

    public static void Show(string key, Engine.Engine engine, Window? owner)
    {
        if (Open.TryGetValue(key, out var there)) { there.Activate(); return; }
        if (ToolsCenter.KindOf(key) is not { } kind) return;
        var w = new ToolFloat(kind, engine, owner);
        Open[key] = w;
        w.Show();
    }
}

internal static class FontFeatureExtensions
{
    /// <summary>Figures of one width (a clock that does not shake as it counts).</summary>
    public static void FontFeatures(this TextBlock t) => Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
}

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
/// What a tool is given where it opens (the Mac's ToolScreen, PinnedToolPopover and ToolPopup between them): the engine,
/// whether it is the quick version in a pin's flyout, the files and details handed to it (from a pin's flyout, a
/// command), and how to open the whole tool or close the flyout.
/// </summary>
public sealed class ToolContext
{
    public required Engine.Engine Engine { get; init; }
    public required string Key { get; init; }
    /// <summary>The quick version, in a pin's flyout (the Mac's FocusTimerCompact, CalculatorCompact …).</summary>
    public bool Quick { get; init; }
    /// <summary>The window it is in (for its dialogs).</summary>
    public Window? Owner { get; init; }

    /// <summary>From the quick version: the whole tool over the window, with details or files for it.</summary>
    public Action<Dictionary<string, string>?, IReadOnlyList<string>?> OpenFull { get; init; } = (_, _) => { };

    /// <summary>The flyout (the quick version) or the sheet (the whole tool, over the window) it is in, closed.</summary>
    public Action Close { get; init; } = () => { };

    /// <summary>The files handed to the tool, taken once (the Mac's ToolsCenter.takeFiles).</summary>
    public IReadOnlyList<string> TakeFiles() => ToolsCenter.TakeFiles(Key);

    /// <summary>A detail handed to the tool, taken once (the Mac's ToolsCenter.takeInfo).</summary>
    public string? TakeInfo(string name) => ToolsCenter.TakeInfo(name);
}

/// <summary>
/// The tools' views by key (the web's keys: cite, pomo, calc, graph, ptable, need, conv, units, pdfx, mark, ocr, fc; and
/// the biology toolkit's cell, dna, gene, biomol, cross, divide, popgen, biolab, enzyme, energy, through BioTools.Register).
/// Each tool's file registers its own from a module initializer, so a tool added is a file added.
/// </summary>
public static class ToolRegistry
{
    private static readonly Dictionary<string, Func<ToolContext, FrameworkElement>> full = new();
    private static readonly Dictionary<string, Func<ToolContext, FrameworkElement>> quick = new();
    private static readonly Dictionary<string, Func<ToolContext, FrameworkElement>> header = new();

    /// <param name="key">The tool's key.</param>
    /// <param name="full">The whole tool (on the Tools page, or over the window).</param>
    /// <param name="quick">Its quick version, for its pin's flyout (null: the flyout offers the whole tool).</param>
    /// <param name="headerControls">Controls beside its pin in the page header (the Periodic Table's search).</param>
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

    /// <summary>A tool opened whole from anywhere (a pin's flyout, a command), with files or details for it: over the
    /// window, as from its tile.</summary>
    public static void Show(string key, Engine.Engine engine, IReadOnlyList<string>? files = null, Dictionary<string, string>? info = null) =>
        PopUp(key, engine, null, files, info);

    /// <summary>A pinned tool opened whole, over the window where you are (the Mac's popUp): the screen stays under it.</summary>
    public static void PopUp(string key, Engine.Engine engine, Window? owner, IReadOnlyList<string>? files = null, Dictionary<string, string>? info = null)
    {
        Hand(key, files, info);
        ToolPopup.Show(key, engine, owner);
    }

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

    /// <summary>A tool's short name, for its pin's flyout (the Mac's ToolKind.shortName).</summary>
    public static string ShortName(string key) => key switch
    {
        "cite" => "Cite", "pomo" => "Focus", "calc" => "Calculator", "graph" => "Graphing", "ptable" => "Elements",
        "need" => "Grade needed", "conv" => "Convert", "units" => "Units", "pdfx" => "Merge & split", "mark" => "Mark up",
        "ocr" => "Image to text", "fc" => "Flashcards",
        "cell" => "Cells", "dna" => "DNA", "gene" => "Genes", "biomol" => "Biomolecules", "cross" => "Crosses", "divide" => "Division",
        "popgen" => "Hardy–Weinberg", "biolab" => "Lab maths", "enzyme" => "Enzymes", "energy" => "Energy", _ => key,
    };

    /// <summary>The width of its pin's flyout (the Mac's popoverWidth).</summary>
    public static double FlyoutWidth(string key) => key switch
    {
        "calc" or "ptable" or "gene" => 420, "cite" or "cross" => 400, "biolab" => 380, "graph" or "popgen" => 360, "fc" => 340, _ => 330,
    };

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
    /// <summary>A tool's scrolling body: the page's side margins, its content as wide as the window allows up to its
    /// widest, and centred past that (the Mac's ToolPage).</summary>
    public static ScrollViewer Page(UIElement content, double maxWidth = 1480, double spacing = 20)
    {
        if (content is FrameworkElement fe) Fit(fe, maxWidth);
        var sv = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(40, 8, 40, 40), Focusable = false,
        };
        sv.SizeChanged += (_, _) => sv.Padding = sv.ActualWidth < 560 ? new Thickness(20, 8, 20, 32) : new Thickness(40, 8, 40, 40);
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

    /// <summary>Two columns side by side on a wide window (the work and its side panel), one over the other on a narrow one.</summary>
    public static FrameworkElement Columns(UIElement main, UIElement side, double sideWidth = 380, double breakpoint = 900, double spacing = 28)
    {
        if (main is FrameworkElement m) m.VerticalAlignment = VerticalAlignment.Top;
        if (side is FrameworkElement s) s.VerticalAlignment = VerticalAlignment.Top;
        return new ToolColumns(main, side, sideWidth, breakpoint, spacing);
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

    public ToolColumns(UIElement main, UIElement side, double sideWidth, double breakpoint, double spacing)
    {
        this.main = main;
        this.side = side;
        this.sideWidth = sideWidth;
        this.breakpoint = breakpoint;
        this.spacing = spacing;
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
            var top = main.DesiredSize.Height;
            main.Arrange(new Rect(0, 0, w, top));
            side.Arrange(new Rect(0, top + spacing, w, side.DesiredSize.Height));
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

// ---- a tool opened whole over the window (the Mac's ToolPopup) ----

/// <summary>A pinned tool opened whole, over the window, in a sheet nearly the window's size: its heading's Done, or
/// Escape, closes it.</summary>
public static class ToolPopup
{
    public static void Show(string key, Engine.Engine engine, Window? owner)
    {
        if (ToolsCenter.KindOf(key) is not { } kind) return;
        var win = owner ?? Application.Current.MainWindow;
        var w = Math.Max(760, (win?.ActualWidth ?? 1280) - 70);
        var h = Math.Max(560, (win?.ActualHeight ?? 860) - 90);
        var sheet = new Sheet { Bare = true, Width = w, Height = h };
        // (Close: the sheet folded away — a tool handing something to another closes itself first, BioTools.Open)
        var c = new ToolContext { Engine = engine, Key = key, Owner = win, Close = () => sheet.Close() };
        var body = ToolRegistry.Full(c) ?? Missing(kind);
        var done = K.Button("Done", () => sheet.Close());
        done.ToolTip = $"Close {kind.Name} (Esc)";
        var head = ToolHeader.Make(kind, c, done);
        head.Margin = new Thickness(40, 20, 40, 12);
        var g = K.Rows("auto, *", head.Row(0), body.Row(1));
        g.Bg(B.Window);
        sheet.Body = g;
        // (one Done, the heading's: no strip of buttons at the foot; Escape still closes it, as a sheet with no Close does)
        Dialogs.Show(sheet, win);
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

/// <summary>A tool's heading over it when opened whole: Done, its tile, its name and line, its own controls, its pin.</summary>
public static class ToolHeader
{
    public static FrameworkElement Make(ToolsScreen.Kind kind, ToolContext c, UIElement? leading)
    {
        var words = K.V(2, K.Text(kind.Name, Ty.Title));
        if (kind.Key != "ptable") words.Children.Add(K.Text(kind.Note, Ty.Body, B.Text2));
        var row = K.Columns("auto, auto, *, auto, auto");
        if (leading != null) { ((FrameworkElement)leading).Margin = new Thickness(0, 0, 14, 0); ((FrameworkElement)leading).VerticalAlignment = VerticalAlignment.Center; row.Children.Add(leading.Col(0)); }
        row.Children.Add(K.IconTile(kind.Symbol, kind.Color, 44).Margin(0, 0, 14, 0).Col(1));
        words.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(words.Col(2));
        if (ToolRegistry.Header(c) is { } extra) { extra.Margin = new Thickness(12, 0, 12, 0); extra.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(extra.Col(3)); }
        // (a header full of the tool's own controls — the Periodic Table's, a biology tool's pages — the pin a symbol alone)
        row.Children.Add(PinButton(kind, squished: kind.Key == "ptable" || BioTools.Pages(kind.Key).Count > 1).Col(4));
        return row;
    }

    /// <summary>A tool's pin in its header: "Pin to Title Bar", or "Pinned" in the accent while it is pinned.</summary>
    public static FrameworkElement PinButton(ToolsScreen.Kind kind, bool squished = false)
    {
        var host = new ContentControl { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        void Draw()
        {
            var on = ToolsPins.IsPinned(kind.Key);
            var b = K.Button(on ? "Pinned" : "Pin to Title Bar", () => ToolsPins.Toggle(kind.Key), accent: on, icon: SymbolRegular.Pin24);
            if (squished)
            {
                // (a header full of the tool's own controls: the pin a symbol alone, the size of theirs)
                b.Icon = null;
                b.Content = K.Icon(SymbolRegular.Pin24, 16, on ? B.OnAccent : B.Text, filled: on);
                b.MinWidth = 0;
                b.Padding = new Thickness(10, 6, 10, 6);
            }
            b.ToolTip = on ? "In the title bar on every screen. Press to take it out." : $"Keep {kind.Name} in the title bar on every screen";
            System.Windows.Automation.AutomationProperties.SetName(b, on ? $"Unpin {kind.Name}" : $"Pin {kind.Name}");
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
/// the tool's quickest use, and its name opens the whole tool. A running focus timer is live in its pin (it borrows a
/// pin while it runs if the timer is not pinned). A pin's menu opens, moves or unpins it.
/// </summary>
public sealed class PinBar : StackPanel
{
    private readonly Engine.Engine engine;
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private ITimerState? timer;
    private Popup? flyout;
    private string? flyoutKey;

    public PinBar(Engine.Engine engine)
    {
        this.engine = engine;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0, 0, 6, 0);
        // (the screenshot suite: --pin calc,ptable pins those tools first)
        if (Launch.Take("", "pin") is { Length: > 0 } pinned) Prefs.Set("pinnedTools", pinned);
        ToolsPins.Changed += Draw;
        tick.Tick += (_, _) => { if (timer?.Active == true) Draw(); };
        Loaded += (_, _) => { Hook(); Draw(); };
        Draw();
        // (the screenshot suite: --pin-open ptable opens that pin's flyout a moment after launch)
        if (Launch.Take("", "pin-open") is { Length: > 0 } k)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            t.Tick += (_, _) => { t.Stop(); OpenFlyout(k); };
            t.Start();
        }
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
            if (buttons.TryGetValue(flyoutKey, out var nb)) flyout.PlacementTarget = nb; else CloseFlyout();
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

    private ContextMenu Menu(ToolsScreen.Kind kind)
    {
        var m = new ContextMenu();
        System.Windows.Controls.MenuItem Item(string text, Action a, bool enabled = true)
        {
            var i = new System.Windows.Controls.MenuItem { Header = text, IsEnabled = enabled };
            i.Click += (_, _) => a();
            return i;
        }
        m.Items.Add(Item($"Open {kind.Name}", () => ToolsCenter.Show(kind.Key, engine)));
        if (kind.Key == "pomo" && timer?.Active == true)
        {
            var t = timer;
            m.Items.Add(new Separator());
            m.Items.Add(Item(t.Running ? "Pause" : "Resume", t.Toggle));
            m.Items.Add(Item($"Skip to {t.NextPhaseName}", t.Skip));
            m.Items.Add(Item("Cancel Session", t.Reset));
        }
        m.Items.Add(new Separator());
        var pins = ToolsPins.Pinned;
        if (ToolsPins.IsPinned(kind.Key))
        {
            m.Items.Add(Item("Move Left", () => ToolsPins.Move(kind.Key, -1), pins.FirstOrDefault() != kind.Key));
            m.Items.Add(Item("Move Right", () => ToolsPins.Move(kind.Key, 1), pins.LastOrDefault() != kind.Key));
            m.Items.Add(Item("Unpin from Title Bar", () => ToolsPins.Toggle(kind.Key)));
        }
        else m.Items.Add(Item("Pin to Title Bar", () => ToolsPins.Toggle(kind.Key)));
        return m;
    }

    public static string Clock(TimeSpan t)
    {
        var s = Math.Max(0, (int)Math.Ceiling(t.TotalSeconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    /// <summary>A pin's flyout: the tool's name (a press opens the whole tool), its pin, and its quickest use.</summary>
    public void OpenFlyout(string key)
    {
        if (flyout != null) { var same = flyoutKey == key; CloseFlyout(); if (same) return; }
        if (ToolsCenter.KindOf(key) is not { } kind) return;
        var anchor = buttons.GetValueOrDefault(key);
        var owner = Window.GetWindow(this);
        void Full(Dictionary<string, string>? info, IReadOnlyList<string>? files)
        {
            CloseFlyout();
            ToolsCenter.PopUp(key, engine, owner, files, info);
        }
        var c = new ToolContext { Engine = engine, Key = key, Quick = true, Owner = owner, OpenFull = Full, Close = CloseFlyout };

        var name = K.Subtle(K.H(4, K.Text(ToolsCenter.ShortName(key), Ty.BodyStrong), K.Icon(SymbolRegular.ChevronRight20, 12, B.Text2)), () => Full(null, null), new Thickness(4, 2, 4, 2));
        name.ToolTip = $"Open {kind.Name}";
        var on = ToolsPins.IsPinned(key);
        var pin = K.IconButton(on ? SymbolRegular.PinOff24 : SymbolRegular.Pin24, () => { ToolsPins.Toggle(key); CloseFlyout(); }, on ? "Unpin from the title bar" : "Pin to the title bar", 16, 30);
        var grow = K.IconButton(SymbolRegular.ArrowMaximize24, () => Full(null, null), $"Open {kind.Name} full size", 16, 30);
        var head = K.Columns("auto, auto, *, auto, auto", K.IconTile(kind.Symbol, kind.Color, 28).Margin(0, 0, 10, 0).Col(0), name.Col(1), pin.Col(3), grow.Col(4));

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
            Width = ToolsCenter.FlyoutWidth(key), Padding = new Thickness(16), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Child = K.V(14, head, body), Margin = new Thickness(12, 4, 12, 20), Effect = K.Shadow(24, 6, 0.22),
        };
        card.Bg(B.Flyout);
        card.SetResourceReference(Border.BorderBrushProperty, B.FlyoutStroke);
        card.SetResourceReference(TextElement.ForegroundProperty, B.Text);
        // (its right edge under its pin's: the pins sit at the window's right, so the flyout opens towards the page)
        var target = (FrameworkElement?)anchor ?? this;
        flyout = new Popup
        {
            Child = card, StaysOpen = false, AllowsTransparency = true, PlacementTarget = target,
            Placement = PlacementMode.Bottom, HorizontalOffset = target.ActualWidth - (card.Width + 24) + 12, PopupAnimation = PopupAnimation.None,
        };
        flyoutKey = key;
        flyout.Closed += (_, _) => { if (flyout?.IsOpen != true) { flyout = null; flyoutKey = null; } };
        flyout.IsOpen = true;
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

    public void CloseFlyout()
    {
        if (flyout == null) return;
        var f = flyout;
        flyout = null;
        flyoutKey = null;
        f.IsOpen = false;
    }
}

internal static class FontFeatureExtensions
{
    /// <summary>Figures of one width (a clock that does not shake as it counts).</summary>
    public static void FontFeatures(this TextBlock t) => Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
}

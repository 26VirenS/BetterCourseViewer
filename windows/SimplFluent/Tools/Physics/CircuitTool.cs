using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Circuit Builder (tools; the Mac's CircuitTool): a grid canvas, parts dragged in from a palette — battery, resistor,
// wire, switch, lamp, ammeter, voltmeter, ground — turned with R, removed with Delete, wired by dragging from one
// terminal to another; the circuit solved as it changes (Tools/Physics/Model/CircuitModel.cs), the current drawn along
// every part and a lamp lit by its power, a readout of every part's V, I and P, and why it can't be solved when it
// can't. Quick-build chips, the circuit kept and saved or opened as JSON, a PNG of it to Downloads; and a calculator
// strip for Ohm's law and resistors in series and parallel, which is also its pin's flyout.

#pragma warning disable CA2255
internal static class CircuitToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("circuit", c => new CircuitTool(c), c => new CircuitQuick(c));
}
#pragma warning restore CA2255

/// <summary>Circuit Builder whole.</summary>
internal sealed class CircuitTool : ContentControl
{
    private const string Tool = "circuit";
    private readonly ToolContext ctx;
    private readonly CircuitCanvas canvas;
    private readonly Border canvasCard;
    private readonly Stack readout = K.V(2);
    private readonly ContentControl editor = new() { Focusable = false };
    private readonly TextBlock problem = K.Wrapped("", Ty.Body);
    private readonly TextBlock saved = K.Wrapped("", Ty.Caption, B.Text2);
    private string? presetName;
    private string said = "";

    private CircuitDoc Doc => canvas.Doc;

    public CircuitTool(ToolContext c)
    {
        ctx = c;
        Focusable = false;
        // (what the launch asked — --tool circuit --tool-state series — else the circuit it had, else the series one)
        var state = PhysTools.LaunchState(Tool);
        CircuitDoc? doc = null;
        if (CircuitSolver.Preset(state) is { } preset) { doc = preset; presetName = state!.Trim().ToLowerInvariant(); }
        else if (PhysPrefs.String(Tool, "doc") is { Length: > 0 } json)
        {
            try { doc = Json.Read<CircuitDoc>(json); } catch { doc = null; }
            if (doc != null && doc.Parts.Count == 0) doc = null;
        }
        if (doc == null) { doc = CircuitSolver.Preset("series")!; presetName = "series"; }
        canvas = new CircuitCanvas(doc) { Height = 380 };
        canvas.Changed += Changed;
        canvas.SelectionChanged += () => { Editor(); Changed(); };
        canvasCard = K.Card(canvas, new Thickness(0));
        canvasCard.ClipToBounds = true;
        canvasCard.AllowDrop = true;

        // (the palette: a press adds a part where there is room, a drag puts it where it lands)
        var palette = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var kind in CircuitParts.Palette)
        {
            var k = kind;
            var b = K.Button(CircuitParts.Name(k), () => AddPart(k, null), icon: Symbol(k));
            b.MinWidth = 0;
            b.Margin = new Thickness(0, 0, 8, 8);
            b.ToolTip = $"Add a {CircuitParts.Name(k).ToLowerInvariant()}: press, or drag it onto the grid";
            Point? down = null;
            b.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(b);
            b.PreviewMouseLeftButtonUp += (_, _) => down = null;
            b.PreviewMouseMove += (_, e) =>
            {
                if (down is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(b);
                if (Math.Abs(p.X - d.X) < 5 && Math.Abs(p.Y - d.Y) < 5) return;
                down = null;
                DragDrop.DoDragDrop(b, new DataObject(CircuitCanvas.DragFormat, k.ToString()), DragDropEffects.Copy);
            };
            palette.Children.Add(b);
        }
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, title) in new[] { ("series", "Series: 9 V + 100 Ω + 200 Ω"), ("parallel", "Parallel: 9 V, 100 Ω ∥ 200 Ω"), ("divider", "Voltage divider") })
        {
            var n = name;
            var chip = K.Button(title, () => { canvas.Doc = CircuitSolver.Preset(n)!; presetName = n; canvas.Select(null); Changed(); });
            chip.MinWidth = 0;
            chip.Margin = new Thickness(0, 0, 8, 8);
            chips.Children.Add(chip);
        }
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[]
        {
            K.Button("Rotate", () => canvas.RotateSelected(), icon: SymbolRegular.ArrowRotateClockwise24).Tip("Turn the part picked a quarter turn (R)"),
            K.Button("Delete", () => canvas.DeleteSelected(), icon: SymbolRegular.Delete24).Tip("Remove the part or wire picked (Delete)"),
            K.Button("Clear", () => { canvas.Doc = new CircuitDoc(); presetName = null; canvas.Select(null); Changed(); }, icon: SymbolRegular.Eraser24),
            K.Button("Save…", SaveAs, icon: SymbolRegular.Save24).Tip("Save the circuit as a JSON file"),
            K.Button("Open…", OpenFile, icon: SymbolRegular.FolderOpen24).Tip("Open a circuit saved as JSON"),
            K.Button("Export PNG", ExportPng, icon: SymbolRegular.Image24).Tip("A picture of the circuit, into Downloads"),
        })
        {
            b.MinWidth = 0;
            b.Margin = new Thickness(0, 0, 8, 8);
            actions.Children.Add(b);
        }
        var hint = K.Wrapped("Drag a part in, or press it. Drag from one terminal to another to wire them. Click a part to pick it: R turns it, Delete removes it; double-click a switch to flip it. Arrow keys nudge it.", Ty.Caption, B.Text2);
        var work = K.V(10, K.Text("Parts", Ty.BodyStrong, B.Text2), palette, K.Text("Quick build", Ty.BodyStrong, B.Text2), chips, canvasCard, hint, actions, saved);
        problem.Visibility = Visibility.Collapsed;
        var side = K.V(16,
            ToolKit.Section("Picked", editor),
            K.V(8, K.Text("Readings", Ty.BodyStrong), problem, K.Card(readout, new Thickness(8, 6, 8, 6))),
            ToolKit.Section("Calculator", new CircuitCalc(compact: false)));
        Content = ToolKit.Page(ToolKit.Columns(work, side, 400, 860));
        Editor();
        Changed();
    }

    private static SymbolRegular Symbol(PartKind k) => k switch
    {
        PartKind.Battery => SymbolRegular.Battery1024, PartKind.Resistor => SymbolRegular.LineHorizontal120, PartKind.Wire => SymbolRegular.Line24,
        PartKind.Switch => SymbolRegular.ToggleLeft24, PartKind.Lamp => SymbolRegular.Lightbulb24, PartKind.Ammeter => SymbolRegular.Gauge24,
        PartKind.Voltmeter => SymbolRegular.Pulse24, _ => SymbolRegular.ArrowDown24,
    };

    /// <summary>A part added: where it was dropped, else at the first cell with room for it.</summary>
    private void AddPart(PartKind kind, (int Col, int Row)? at)
    {
        var doc = Doc;
        var taken = doc.Parts.SelectMany(p => CircuitParts.Body(p).Concat(CircuitParts.Terminals(p))).ToHashSet();
        bool Free(int c, int r)
        {
            var probe = new CircuitPart { Kind = kind, Col = c, Row = r };
            return !CircuitParts.Body(probe).Concat(CircuitParts.Terminals(probe)).Any(taken.Contains);
        }
        var (col, row) = at ?? (3, 2);
        if (at == null)
        {
            var found = false;
            for (var r = 2; r <= 14 && !found; r += 2)
                for (var c = 3; c <= 24 && !found; c += 5)
                    if (Free(c, r)) { col = c; row = r; found = true; }
        }
        var part = doc.Add(kind, col, row);
        presetName = null;
        canvas.Select(part.Id);
        Changed();
        canvas.Focus();
    }

    /// <summary>The circuit changed: solved again, kept, drawn, read out.</summary>
    private void Changed()
    {
        var doc = Doc;
        var s = CircuitSolver.Solve(doc);
        canvas.Solution = s;
        PhysPrefs.Set(Tool, "doc", doc.Parts.Count == 0 ? null : Json.Write(doc));
        readout.Children.Clear();
        var head = K.Columns("*, 72, 72, 72", K.Text("Part", Ty.Caption, B.Text2).Col(0), K.Text("V", Ty.Caption, B.Text2).Col(1), K.Text("I", Ty.Caption, B.Text2).Col(2), K.Text("P", Ty.Caption, B.Text2).Col(3));
        head.Margin = new Thickness(8, 2, 8, 2);
        readout.Children.Add(head);
        var lines = new List<string>();
        foreach (var p in doc.Parts)
        {
            if (p.Kind == PartKind.Ground) continue;
            var r = s.Of(p.Id);
            var name = doc.NameOf(p);
            var words = p.Kind switch
            {
                PartKind.Battery => $"{name} · {CircuitMath.VoltsText(p.Value)}",
                PartKind.Resistor or PartKind.Lamp => $"{name} · {CircuitMath.OhmsText(p.Value)}",
                PartKind.Switch => $"{name} · {(p.Closed ? "closed" : "open")}",
                _ => name,
            };
            var row = K.Columns("*, 72, 72, 72",
                K.Text(words, Ty.Body).Col(0),
                Tab(CircuitMath.VoltsText(Math.Abs(r.Volts))).Col(1), Tab(CircuitMath.AmpsText(Math.Abs(r.Amps))).Col(2), Tab(CircuitMath.WattsText(Math.Abs(r.Watts))).Col(3));
            var id = p.Id;
            var b = K.Subtle(row, () => canvas.Select(id), new Thickness(8, 5, 8, 5));
            if (canvas.Selected == id) b.Bg(B.Subtle);
            AutomationProperties.SetName(b, $"{words}: {CircuitMath.VoltsText(Math.Abs(r.Volts))}, {CircuitMath.AmpsText(Math.Abs(r.Amps))}, {CircuitMath.WattsText(Math.Abs(r.Watts))}");
            readout.Children.Add(b);
            lines.Add($"{name} {CircuitMath.VoltsText(Math.Abs(r.Volts))} {CircuitMath.AmpsText(Math.Abs(r.Amps))}");
        }
        if (doc.Parts.Count == 0) readout.Children.Add(K.Wrapped("Add parts to the grid, or pick a quick build.", Ty.Body, B.Text2).Margin(8, 4, 8, 4));
        var note = s.Problem ?? (s.Unpowered > 0 ? $"{ToolKit.Plural(s.Unpowered, "part")} {(s.Unpowered == 1 ? "isn't" : "aren't")} in a circuit with a battery — wire them in." : null);
        if (note == null && doc.Parts.Count > 0 && !doc.Parts.Any(p => p.Kind == PartKind.Battery)) note = "No battery yet: nothing flows.";
        problem.Text = note ?? "";
        problem.Visibility = note == null ? Visibility.Collapsed : Visibility.Visible;
        problem.Fg(s.Problem != null ? B.Critical : B.Text2);
        canvas.InvalidateVisual();
        Shot.Moved();
        var now = $"{presetName ?? "own"}: {string.Join(", ", lines)}{(s.Problem != null ? " · unsolvable" : "")}";
        if (now != said)
        {
            said = now;
            if (Shot.Asked) Log.Write($"circuit: shows {PhysMath.Ascii(now)}");
        }
    }

    private static TextBlock Tab(string s)
    {
        var t = K.Text(s, Ty.Body, B.Text2);
        t.TextAlignment = TextAlignment.Right;
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>The part picked: its value to change, a switch's state, and its turn and removal.</summary>
    private void Editor()
    {
        var doc = Doc;
        if (canvas.SelectedLink is { } link)
        {
            var a = doc.Part(link.A);
            var b = doc.Part(link.B);
            editor.Content = K.V(8, K.Text($"Wire: {(a != null ? doc.NameOf(a) : "?")} to {(b != null ? doc.NameOf(b) : "?")}", Ty.BodyStrong), K.Wrapped("Delete removes it.", Ty.Caption, B.Text2));
            return;
        }
        if (canvas.Selected is not { } id || doc.Part(id) is not { } part)
        {
            editor.Content = K.Wrapped("Click a part on the grid to change its value, turn it or remove it.", Ty.Body, B.Text2);
            return;
        }
        var v = K.V(10, K.Text(doc.NameOf(part), Ty.BodyStrong));
        if (CircuitParts.Unit(part.Kind) is { } unit)
        {
            var box = ToolKit.Box(CircuitMath.Plain(part.Value), "", t =>
            {
                if (PhysMath.Number(t) is { } n && n >= 0 && Math.Abs(n - part.Value) > 1e-12) { part.Value = n; presetName = null; Changed(); }
            });
            box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { canvas.Focus(); e.Handled = true; } };
            AutomationProperties.SetName(box, $"{doc.NameOf(part)} in {(unit == "V" ? "volts" : "ohms")}");
            v.Children.Add(ToolKit.Field(unit == "V" ? "Volts" : "Ohms", box, suffix: unit, width: 180));
        }
        if (part.Kind == PartKind.Switch)
        {
            var sw = new ToggleSwitch { Content = part.Closed ? "Closed" : "Open", IsChecked = part.Closed };
            sw.Checked += (_, _) => { part.Closed = true; sw.Content = "Closed"; presetName = null; Changed(); };
            sw.Unchecked += (_, _) => { part.Closed = false; sw.Content = "Open"; presetName = null; Changed(); };
            AutomationProperties.SetName(sw, "The switch closed");
            v.Children.Add(sw);
        }
        if (part.Kind == PartKind.Ground) v.Children.Add(K.Wrapped("The circuit's 0 V: voltages are measured from here.", Ty.Caption, B.Text2));
        editor.Content = v;
    }

    private void SaveAs()
    {
        if (Files.SaveAs("Circuit.json", "Circuit (JSON)|*.json", Window.GetWindow(this) ?? ctx.Owner) is not { } path) return;
        try
        {
            File.WriteAllText(path, Json.Write(Doc));
            saved.Text = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception e) { saved.Text = "Couldn't save the file."; Log.Write($"circuit: save: {e.Message}"); }
    }

    private void OpenFile()
    {
        var files = Files.Choose("Circuit (JSON)|*.json", false, Window.GetWindow(this) ?? ctx.Owner);
        if (files.Count == 0) return;
        try
        {
            var doc = Json.Read<CircuitDoc>(File.ReadAllText(files[0]));
            if (doc == null || doc.Parts.Count == 0) { saved.Text = "That file isn't a circuit."; return; }
            foreach (var p in doc.Parts) p.Turn &= 3;
            canvas.Doc = doc;
            presetName = null;
            canvas.Select(null);
            saved.Text = $"Opened {Path.GetFileName(files[0])}.";
            Changed();
        }
        catch (Exception e) { saved.Text = "That file isn't a circuit."; Log.Write($"circuit: open: {e.Message}"); }
    }

    /// <summary>The grid as a picture, into Downloads (as the file tools put their files there).</summary>
    private void ExportPng()
    {
        try
        {
            int w = (int)Math.Ceiling(canvasCard.ActualWidth), h = (int)Math.Ceiling(canvasCard.ActualHeight);
            if (w < 10 || h < 10) return;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(canvasCard), null, new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            var path = Files.Free("Circuit", "png", Simpl.Engine.FilePreview.Downloads);
            using (var fs = File.Create(path)) enc.Save(fs);
            saved.Text = $"Saved {Path.GetFileName(path)} in Downloads.";
            Log.Write($"circuit: exported {Path.GetFileName(path)}");
        }
        catch (Exception e) { saved.Text = "Couldn't save the picture."; Log.Write($"circuit: export: {e.Message}"); }
    }
}

/// <summary>
/// The grid the circuit is built on: parts drawn by kind and turn with their terminals, the wires between terminals,
/// the current flowing along every part (arrows that move with Windows' animations on), a lamp's glow, every part's
/// value and reading under it, and the part or wire picked outlined in the accent. Parts are dragged, dropped in from
/// the palette, wired by dragging from a terminal, turned with R and removed with Delete.
/// </summary>
internal sealed class CircuitCanvas : FrameworkElement
{
    public const string DragFormat = "simpl/circuit-part";
    public const double Cell = 24, Origin = 12;

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(CircuitCanvas),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty Text2BrushProperty = DependencyProperty.Register(nameof(Text2Brush), typeof(Brush), typeof(CircuitCanvas),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(CircuitCanvas),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush Text2Brush { get => (Brush)GetValue(Text2BrushProperty); set => SetValue(Text2BrushProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    private static readonly Typeface Face = new(K.SmallFace, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Strong = new(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Color Glow = Color.FromRgb(0xff, 0xd6, 0x0a), Flow = Color.FromRgb(0xf7, 0x8c, 0x1a);

    public CircuitDoc Doc { get; set; }
    public CircuitSolution Solution { get; set; } = new();
    public int? Selected { get; private set; }
    public CircuitLink? SelectedLink { get; private set; }

    /// <summary>The circuit was changed on the grid (a part moved, turned, removed or dropped in; a wire made).</summary>
    public event Action? Changed;
    public event Action? SelectionChanged;

    private (int Id, int DCol, int DRow)? dragging;
    private (int Part, int Terminal)? wiring;
    private Point pointer;
    private (int Part, int Terminal)? hoverTerminal;
    private readonly DispatcherTimer flow = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private double phase = 0.25;

    public CircuitCanvas(CircuitDoc doc)
    {
        Doc = doc;
        SetResourceReference(TextBrushProperty, B.Text);
        SetResourceReference(Text2BrushProperty, B.Text2);
        SetResourceReference(AccentBrushProperty, B.Accent);
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;
        Cursor = Cursors.Arrow;
        AutomationProperties.SetName(this, "The circuit on its grid");
        flow.Tick += (_, _) =>
        {
            if (!Solution.Readings.Values.Any(r => Math.Abs(r.Amps) > 1e-9)) return;
            phase = (phase + 0.012) % 1;
            InvalidateVisual();
        };
        Loaded += (_, _) => { if (Motion.On) flow.Start(); };
        Unloaded += (_, _) => flow.Stop();
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseLeave += (_, _) => { if (hoverTerminal != null) { hoverTerminal = null; InvalidateVisual(); } };
        KeyDown += OnKey;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += OnDrop;
    }

    // ---- selection and editing ----

    public void Select(int? id)
    {
        SelectedLink = null;
        Selected = id;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void RotateSelected()
    {
        if (Selected is not { } id || Doc.Part(id) is not { } p) return;
        p.Turn = (p.Turn + 1) & 3;
        Recorner(p.Id);
        Changed?.Invoke();
    }

    public void DeleteSelected()
    {
        if (SelectedLink is { } link) { Doc.Links.Remove(link); SelectedLink = null; SelectionChanged?.Invoke(); Changed?.Invoke(); return; }
        if (Selected is not { } id) return;
        Doc.Remove(id);
        Selected = null;
        SelectionChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>The wires to a part that moved or turned, turned again where they must.</summary>
    private void Recorner(int id)
    {
        foreach (var l in Doc.Links.Where(l => l.Touches(id)))
        {
            if (Doc.Part(l.A) is not { } a || Doc.Part(l.B) is not { } b) continue;
            var ta = CircuitParts.Terminals(a);
            var tb = CircuitParts.Terminals(b);
            if (l.ATerminal >= ta.Length || l.BTerminal >= tb.Length) continue;
            var corner = CircuitParts.Corner(Doc, ta[l.ATerminal], tb[l.BTerminal]);
            l.CornerCol = corner?.Col;
            l.CornerRow = corner?.Row;
        }
    }

    // ---- geometry ----

    private static Point At(int col, int row) => new(Origin + col * Cell, Origin + row * Cell);
    private static Point At((int Col, int Row) cell) => At(cell.Col, cell.Row);
    private static (int Col, int Row) CellOf(Point p) => ((int)Math.Round((p.X - Origin) / Cell), (int)Math.Round((p.Y - Origin) / Cell));

    private (int Part, int Terminal)? TerminalAt(Point p)
    {
        foreach (var part in Doc.Parts)
        {
            var ts = CircuitParts.Terminals(part);
            for (var t = 0; t < ts.Length; t++)
            {
                var c = At(ts[t]);
                if ((c - p).Length <= 9) return (part.Id, t);
            }
        }
        return null;
    }

    private Rect Bounds(CircuitPart p)
    {
        var c = At(p.Col, p.Row);
        if (p.Kind == PartKind.Ground)
        {
            var (uc, ur) = CircuitParts.Direction(p.Turn + 3);
            var r = new Rect(c.X - 14, c.Y - 14, 28, 28);
            r.Union(new Point(c.X + uc * Cell, c.Y + ur * Cell));
            return r;
        }
        return (p.Turn & 1) == 0 ? new Rect(c.X - 40, c.Y - 18, 80, 36) : new Rect(c.X - 18, c.Y - 40, 36, 80);
    }

    private CircuitPart? PartAt(Point p) => Doc.Parts.LastOrDefault(part => Bounds(part).Contains(p));

    /// <summary>A wire's points: its two terminals, and its corner between them.</summary>
    private List<Point>? Path(CircuitLink l)
    {
        if (Doc.Part(l.A) is not { } a || Doc.Part(l.B) is not { } b) return null;
        var ta = CircuitParts.Terminals(a);
        var tb = CircuitParts.Terminals(b);
        if (l.ATerminal >= ta.Length || l.BTerminal >= tb.Length) return null;
        var pts = new List<Point> { At(ta[l.ATerminal]) };
        if (l.CornerCol is { } cc && l.CornerRow is { } cr) pts.Add(At(cc, cr));
        pts.Add(At(tb[l.BTerminal]));
        return pts;
    }

    private CircuitLink? LinkAt(Point p)
    {
        foreach (var l in Doc.Links)
        {
            if (Path(l) is not { } pts) continue;
            for (var i = 0; i + 1 < pts.Count; i++)
                if (Distance(p, pts[i], pts[i + 1]) <= 6) return l;
        }
        return null;
    }

    private static double Distance(Point p, Point a, Point b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared;
        var t = len2 < 1e-9 ? 0 : Math.Clamp(Vector.Multiply(p - a, ab) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    // ---- the mouse and the keyboard ----

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        pointer = p;
        // (a double-click on a switch flips it)
        if (e.ClickCount == 2 && PartAt(p) is { Kind: PartKind.Switch } sw)
        {
            sw.Closed = !sw.Closed;
            Select(sw.Id);
            Changed?.Invoke();
            e.Handled = true;
            return;
        }
        if (TerminalAt(p) is { } term)
        {
            wiring = term;
            CaptureMouse();
            e.Handled = true;
            InvalidateVisual();
            return;
        }
        if (PartAt(p) is { } part)
        {
            Select(part.Id);
            var cell = CellOf(p);
            dragging = (part.Id, part.Col - cell.Col, part.Row - cell.Row);
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (LinkAt(p) is { } link)
        {
            Selected = null;
            SelectedLink = link;
            SelectionChanged?.Invoke();
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (Selected != null || SelectedLink != null) Select(null);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        pointer = p;
        if (dragging is { } d && Doc.Part(d.Id) is { } part)
        {
            var cell = CellOf(p);
            var col = Math.Max(0, cell.Col + d.DCol);
            var row = Math.Max(0, cell.Row + d.DRow);
            if (col != part.Col || row != part.Row) { part.Col = col; part.Row = row; InvalidateVisual(); }
            return;
        }
        var hover = TerminalAt(p);
        if (wiring != null || hover != hoverTerminal)
        {
            hoverTerminal = hover;
            Cursor = hover != null ? Cursors.Cross : Cursors.Arrow;
            InvalidateVisual();
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        if (dragging is { } d)
        {
            dragging = null;
            ReleaseMouseCapture();
            Recorner(d.Id);
            Changed?.Invoke();
            return;
        }
        if (wiring is { } from)
        {
            wiring = null;
            ReleaseMouseCapture();
            if (TerminalAt(p) is { } to && (to.Part != from.Part || to.Terminal != from.Terminal))
            {
                var a = Doc.Part(from.Part);
                var b = Doc.Part(to.Part);
                if (a != null && b != null)
                {
                    var corner = CircuitParts.Corner(Doc, CircuitParts.Terminals(a)[from.Terminal], CircuitParts.Terminals(b)[to.Terminal]);
                    if (Doc.Connect(from.Part, from.Terminal, to.Part, to.Terminal, corner) != null) Changed?.Invoke();
                }
            }
            InvalidateVisual();
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.R) { RotateSelected(); e.Handled = true; }
        else if (e.Key is Key.Delete or Key.Back) { DeleteSelected(); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Selected is { } id && Doc.Part(id) is { } p)
        {
            p.Col = Math.Max(0, p.Col + (e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0));
            p.Row = Math.Max(0, p.Row + (e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0));
            Recorner(p.Id);
            Changed?.Invoke();
            e.Handled = true;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not string word || !Enum.TryParse<PartKind>(word, out var kind)) return;
        var cell = CellOf(e.GetPosition(this));
        var part = Doc.Add(kind, Math.Max(2, cell.Col), Math.Max(1, cell.Row));
        Select(part.Id);
        Changed?.Invoke();
        Focus();
        e.Handled = true;
    }

    // ---- drawing ----

    private void Text(DrawingContext dc, string s, Point at, Brush brush, Typeface face, double size, bool centre = true)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(centre ? at.X - ft.Width / 2 : at.X, at.Y - ft.Height / 2));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width < 10 || size.Height < 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        // (the grid's dots)
        var dot = Text2Brush.Clone();
        dot.Opacity = 0.35;
        dot.Freeze();
        for (var y = Origin; y < size.Height; y += Cell)
            for (var x = Origin; x < size.Width; x += Cell)
                dc.DrawRectangle(dot, null, new Rect(x - 0.75, y - 0.75, 1.5, 1.5));
        var wire = new Pen(TextBrush, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        wire.Freeze();
        var picked = new Pen(AccentBrush, 3.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        picked.Freeze();
        // (the wires)
        foreach (var l in Doc.Links)
        {
            if (Path(l) is not { } pts) continue;
            var pen = ReferenceEquals(l, SelectedLink) ? picked : wire;
            for (var i = 0; i + 1 < pts.Count; i++) dc.DrawLine(pen, pts[i], pts[i + 1]);
        }
        // (the parts)
        foreach (var p in Doc.Parts) DrawPart(dc, p, wire);
        // (the terminals, the one under the pointer and the one a wire is being drawn from larger)
        foreach (var p in Doc.Parts)
        {
            var ts = CircuitParts.Terminals(p);
            for (var t = 0; t < ts.Length; t++)
            {
                var hot = (hoverTerminal is { } h && h.Part == p.Id && h.Terminal == t) || (wiring is { } w && w.Part == p.Id && w.Terminal == t);
                dc.DrawEllipse(hot ? AccentBrush : TextBrush, null, At(ts[t]), hot ? 5.5 : 3.5, hot ? 5.5 : 3.5);
            }
        }
        // (a wire being drawn)
        if (wiring is { } from && Doc.Part(from.Part) is { } fp)
        {
            var start = At(CircuitParts.Terminals(fp)[from.Terminal]);
            var end = hoverTerminal is { } ht && Doc.Part(ht.Part) is { } hp ? At(CircuitParts.Terminals(hp)[ht.Terminal]) : pointer;
            var preview = new Pen(AccentBrush, 2) { DashStyle = new DashStyle(new[] { 3.0, 2.5 }, 0), StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(preview, start, end);
        }
        if (Doc.Parts.Count == 0)
            Text(dc, "Drag parts here from the palette, or pick a quick build.", new Point(size.Width / 2, size.Height / 2), Text2Brush, Face, 13);
    }

    private void DrawPart(DrawingContext dc, CircuitPart p, Pen wire)
    {
        var c = At(p.Col, p.Row);
        var r = Solution.Of(p.Id);
        var selected = Selected == p.Id;
        if (selected)
        {
            var b = Bounds(p);
            b.Inflate(4, 4);
            dc.DrawRoundedRectangle(null, new Pen(AccentBrush, 1.5), b, 6, 6);
        }
        dc.PushTransform(new TranslateTransform(c.X, c.Y));
        dc.PushTransform(new RotateTransform(90 * (p.Turn & 3)));
        var fill = TextBrush;
        switch (p.Kind)
        {
            case PartKind.Battery:
                dc.DrawLine(wire, new Point(-48, 0), new Point(-8, 0));
                dc.DrawLine(wire, new Point(8, 0), new Point(48, 0));
                dc.DrawLine(new Pen(TextBrush, 2), new Point(-8, -16), new Point(-8, 16));
                dc.DrawLine(new Pen(TextBrush, 5), new Point(8, -8), new Point(8, 8));
                break;
            case PartKind.Resistor:
            {
                dc.DrawLine(wire, new Point(-48, 0), new Point(-30, 0));
                dc.DrawLine(wire, new Point(30, 0), new Point(48, 0));
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(-30, 0), false, false);
                    for (var i = 0; i < 6; i++) g.LineTo(new Point(-25 + i * 10, i % 2 == 0 ? -8 : 8), true, true);
                    g.LineTo(new Point(30, 0), true, true);
                }
                geo.Freeze();
                dc.DrawGeometry(null, wire, geo);
                break;
            }
            case PartKind.Wire:
                dc.DrawLine(wire, new Point(-48, 0), new Point(48, 0));
                break;
            case PartKind.Switch:
                dc.DrawLine(wire, new Point(-48, 0), new Point(-14, 0));
                dc.DrawLine(wire, new Point(14, 0), new Point(48, 0));
                dc.DrawEllipse(fill, null, new Point(-14, 0), 3, 3);
                dc.DrawEllipse(fill, null, new Point(14, 0), 3, 3);
                dc.DrawLine(wire, new Point(-14, 0), p.Closed ? new Point(14, 0) : new Point(10, -18));
                break;
            case PartKind.Lamp:
            {
                dc.DrawLine(wire, new Point(-48, 0), new Point(-16, 0));
                dc.DrawLine(wire, new Point(16, 0), new Point(48, 0));
                var glow = r.Brightness;
                if (glow > 0.01)
                {
                    dc.DrawEllipse(K.Brush(K.WithAlpha(Glow, 0.35 * glow)), null, new Point(0, 0), 26, 26);
                    dc.DrawEllipse(K.Brush(K.WithAlpha(Glow, glow)), null, new Point(0, 0), 16, 16);
                }
                dc.DrawEllipse(null, wire, new Point(0, 0), 16, 16);
                var cross = new Pen(TextBrush, 1.5);
                dc.DrawLine(cross, new Point(-11.3, -11.3), new Point(11.3, 11.3));
                dc.DrawLine(cross, new Point(-11.3, 11.3), new Point(11.3, -11.3));
                break;
            }
            case PartKind.Ammeter:
            case PartKind.Voltmeter:
                dc.DrawLine(wire, new Point(-48, 0), new Point(-18, 0));
                dc.DrawLine(wire, new Point(18, 0), new Point(48, 0));
                dc.DrawEllipse(null, wire, new Point(0, 0), 18, 18);
                break;
            case PartKind.Ground:
                dc.DrawLine(wire, new Point(0, -24), new Point(0, -6));
                dc.DrawLine(wire, new Point(-12, -6), new Point(12, -6));
                dc.DrawLine(wire, new Point(-8, 2), new Point(8, 2));
                dc.DrawLine(wire, new Point(-4, 10), new Point(4, 10));
                break;
        }
        // (the current along the part: arrows from terminal 0 to terminal 1, or back, moving when animations are on)
        if (CircuitParts.TwoTerminals(p.Kind) && Math.Abs(r.Amps) > 1e-9)
        {
            var dir = r.Amps > 0 ? 1 : -1;
            var a = Math.Abs(r.Amps);
            var head = a >= 1 ? 7.0 : a >= 0.1 ? 6 : a >= 0.01 ? 5 : 4;
            var brush = K.Brush(Flow);
            for (var k = 0; k < 2; k++)
            {
                var t = (phase + k * 0.5) % 1;
                var x = (-44 + 88 * t) * dir;
                var y = p.Kind is PartKind.Lamp or PartKind.Ammeter or PartKind.Voltmeter ? -26.0 : -16;
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(x + head * dir, y), true, true);
                    g.LineTo(new Point(x - head * 0.6 * dir, y - head * 0.6), false, false);
                    g.LineTo(new Point(x - head * 0.6 * dir, y + head * 0.6), false, false);
                }
                geo.Freeze();
                dc.DrawGeometry(brush, null, geo);
            }
        }
        dc.Pop();
        dc.Pop();
        // (the meter's letter, the battery's +, and the words under the part, upright whatever its turn)
        if (p.Kind is PartKind.Ammeter or PartKind.Voltmeter) Text(dc, p.Kind == PartKind.Ammeter ? "A" : "V", c, TextBrush, Strong, 14);
        if (p.Kind == PartKind.Battery)
        {
            var (dcol, drow) = CircuitParts.Direction(p.Turn);
            Text(dc, "+", new Point(c.X - dcol * 22 - drow * 14, c.Y - drow * 22 + dcol * 14), Text2Brush, Strong, 13);
        }
        var below = (p.Turn & 1) == 0 ? new Point(c.X, c.Y + 26) : new Point(c.X + 34, c.Y - 6);
        var words = p.Kind switch
        {
            PartKind.Battery => CircuitMath.VoltsText(p.Value),
            PartKind.Resistor or PartKind.Lamp => CircuitMath.OhmsText(p.Value),
            PartKind.Switch => p.Closed ? "closed" : "open",
            _ => "",
        };
        var reading = p.Kind switch
        {
            PartKind.Ammeter => CircuitMath.AmpsText(Math.Abs(r.Amps)),
            PartKind.Voltmeter => CircuitMath.VoltsText(Math.Abs(r.Volts)),
            PartKind.Resistor or PartKind.Lamp => Math.Abs(r.Amps) > 1e-9 ? $"{CircuitMath.VoltsText(Math.Abs(r.Volts))} · {CircuitMath.AmpsText(Math.Abs(r.Amps))}" : "",
            PartKind.Battery => Math.Abs(r.Amps) > 1e-9 ? CircuitMath.AmpsText(Math.Abs(r.Amps)) : "",
            _ => "",
        };
        var centre = (p.Turn & 1) == 0;
        if (words.Length > 0) Text(dc, words, below, TextBrush, Face, 11, centre);
        if (reading.Length > 0) Text(dc, reading, new Point(below.X, below.Y + 13), Text2Brush, Face, 11, centre);
    }
}

/// <summary>
/// The calculator strip (the Circuit Builder's, and its pin's flyout whole): Ohm's law from any two of V, I and R, and
/// the resistance of a list of resistors in series and in parallel.
/// </summary>
internal sealed class CircuitCalc : ContentControl
{
    public CircuitCalc(bool compact)
    {
        Focusable = false;
        var kept = (PhysPrefs.String("circuit", compact ? "quickCalc" : "calc") ?? "").Split('|');
        string Kept(int i) => i < kept.Length ? kept[i] : "";
        var ohm = Everyday.Figures("—", compact ? 22 : 24, FontWeights.SemiBold);
        var ohmLine = K.Wrapped("Fill in any two of V, I and R.", Ty.Caption, B.Text2);
        var eq = Everyday.Figures("—", compact ? 18 : 20, FontWeights.SemiBold);
        var eqLine = K.Wrapped("Resistances, separated by commas: 100, 200 or 1k, 2.2k.", Ty.Caption, B.Text2);
        TextBox v = null!, i = null!, r = null!, list = null!;
        v = MotionWords.Box(Kept(0), "V", Update);
        i = MotionWords.Box(Kept(1), "I", Update);
        r = MotionWords.Box(Kept(2), "R", Update);
        list = MotionWords.Box(Kept(3) is { Length: > 0 } l ? l : "100, 200", "100, 200", Update);
        AutomationProperties.SetName(v, "Volts");
        AutomationProperties.SetName(i, "Amps");
        AutomationProperties.SetName(r, "Ohms");
        AutomationProperties.SetName(list, "Resistors in series or parallel");
        void Update()
        {
            if (v == null || i == null || r == null || list == null) return;
            PhysPrefs.Set("circuit", compact ? "quickCalc" : "calc", $"{v.Text.Trim()}|{i.Text.Trim()}|{r.Text.Trim()}|{list.Text.Trim()}");
            double? pv = PhysMath.Number(v.Text), pi = PhysMath.Number(i.Text);
            var ohms = CircuitMath.Ohms(r.Text);
            double? pr = ohms.Count > 0 ? ohms[0] : null;
            var given = new[] { pv, pi, pr }.Count(q => q != null);
            var o = CircuitMath.Ohm(pv, pi, pr);
            if (o is { } ans)
            {
                ohm.Text = ans.Which switch { "V" => $"V = {CircuitMath.VoltsText(ans.Value)}", "I" => $"I = {CircuitMath.AmpsText(ans.Value)}", _ => $"R = {CircuitMath.OhmsText(ans.Value)}" };
                ohmLine.Text = ans.Which switch { "V" => "V = IR", "I" => "I = V / R", _ => "R = V / I" };
            }
            else
            {
                ohm.Text = "—";
                ohmLine.Text = given == 2 ? "Can't divide by zero." : given < 2 ? "Fill in any two of V, I and R." : "Leave one of V, I and R blank.";
            }
            AutomationProperties.SetName(ohm, ohm.Text == "—" ? "No answer yet" : ohm.Text);
            var rs = CircuitMath.Ohms(list.Text);
            if (rs.Count >= 2)
            {
                eq.Text = $"Series {CircuitMath.OhmsText(CircuitMath.Series(rs))} · Parallel {CircuitMath.OhmsText(CircuitMath.Parallel(rs))}";
                eqLine.Text = $"{ToolKit.Plural(rs.Count, "resistor")}: {string.Join(", ", rs.Select(CircuitMath.OhmsText))}";
            }
            else
            {
                eq.Text = "—";
                eqLine.Text = "Two resistances or more, separated by commas: 100, 200 or 1k, 2.2k.";
            }
        }
        MotionWords.Copyable(ohm, () => $"{ohm.Text} ({ohmLine.Text})");
        MotionWords.Copyable(eq, () => eq.Text);
        var fields = K.Columns("*, 8, *, 8, *", ToolKit.Field("V", v, suffix: "V").Col(0), ToolKit.Field("I", i, suffix: "A").Col(2), ToolKit.Field("R", r, suffix: "Ω").Col(4));
        Update();
        Content = K.V(compact ? 8 : 10,
            K.Text("Ohm's law", Ty.BodyStrong, B.Text2), fields, Everyday.Shrinks(ohm), ohmLine,
            K.Text("Series and parallel", Ty.BodyStrong, B.Text2).Margin(0, 6, 0, 0), list, Everyday.Shrinks(eq), eqLine);
    }
}

/// <summary>Circuit Builder in its pin's flyout (the Mac's CircuitCompact): the calculator strip — Ohm's law and
/// resistors in series and parallel; Open full screen opens the builder.</summary>
internal sealed class CircuitQuick : ContentControl
{
    public CircuitQuick(ToolContext c)
    {
        Focusable = false;
        Content = new CircuitCalc(compact: true);
        c.Carry = () => null;
    }
}

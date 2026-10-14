using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// Cell Explorer (spec §4.1, §4.1.1, §5.1): animal, plant and bacterial cells in 3D, each built procedurally from §5.1.5's
// recipes at a fixed seed (Model/CellExplorerBuild.cs, the same picture on every launch and on the Mac), with labels and
// leader lines, a card for any part pressed, the cutaway, explode, teaching and true scale; a patch of membrane up close;
// and the three compared. This file is the tool itself — its pages in the header, what a launch asks of it, the options
// it remembers — and the pieces its pages share: text with a bold term, a part's swatch, the scale bar under a 3D view,
// and what the pages read of the shared 3D view (its camera, its turning world). The pages are CellExplorerCellPage.cs,
// CellExplorerMembranePage.cs and CellExplorerComparePage.cs; what they show lives in CellExplorerModel, so it outlives
// them.

#pragma warning disable CA2255
internal static class CellExplorerToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("cell", (c, state) => new CellExplorerTool(c, state), CellExplorerTool.Pages);
}
#pragma warning restore CA2255

/// <summary>
/// Cell Explorer (spec §4.1): the page chosen in its header — a cell (animal, plant, bacterium), the membrane up close, or
/// the three compared. Each page is made once and kept while the tool is open, so a cell's camera and options stay as
/// they were when the student comes back to it.
/// </summary>
internal sealed class CellExplorerTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("animal", "Animal", SymbolRegular.AnimalCat24, "An animal cell"),
        new("plant", "Plant", SymbolRegular.LeafOne24, "A plant cell"),
        new("bacterium", "Bacterium", SymbolRegular.Pill24, "A bacterial cell (E. coli)"),
        new("membrane", "Membrane", SymbolRegular.Layer24, "The plasma membrane up close"),
        new("compare", "Compare", SymbolRegular.SplitVertical24, "Animal, plant and bacterial cells side by side"),
    };

    private readonly ToolContext c;
    private readonly ContentControl host = new() { Focusable = false };
    private CellPage? cellPage;
    private CellMembranePage? membranePage;
    private CellComparePage? comparePage;
    private string? shown;

    public CellExplorerTool(ToolContext c, BioLaunchState? state)
    {
        this.c = c;
        CellExplorerSettings.Load();
        if (state != null) CellExplorerSettings.Apply(state);
        Children.Add(host);
        Show();
        void Nav(string key) { if (key == "cell") Show(); }
        Loaded += (_, _) => { BioNav.Changed -= Nav; BioNav.Changed += Nav; Show(); };
        Unloaded += (_, _) => BioNav.Changed -= Nav;
    }

    private void Show()
    {
        var page = BioNav.Page("cell");
        if (page == shown) return;
        shown = page;
        FrameworkElement view = page switch
        {
            "membrane" => membranePage ??= new CellMembranePage(c),
            "compare" => comparePage ??= new CellComparePage(),
            _ => cellPage ??= new CellPage(c),
        };
        if (view is CellPage cp) cp.Kind = CEKinds.FromId(page) ?? CEKind.Animal;
        host.Content = view;
    }
}

/// <summary>The options Cell Explorer remembers (spec §3.2: Prefs SimplBio.cell.&lt;field&gt;, the Mac's names) — the
/// cutaway, the labels, the scale, the bacterium's wall, each cell's hidden parts and the membrane page's switches — read
/// once a run; and what a
/// launch asks of it (spec §9: the pictures' states).</summary>
internal static class CellExplorerSettings
{
    private static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        CellExplorerModel.Cutaway = BioPrefs.Bool("cell", "cutaway", true);
        CellExplorerModel.Labels = BioPrefs.Bool("cell", "labels", true);
        CellExplorerModel.TrueScale = BioPrefs.Bool("cell", "trueScale", false);
        CellExplorerModel.GramPositive = BioPrefs.Bool("cell", "gramPositive", false);
        CellExplorerModel.MembraneLabels = BioPrefs.Bool("cell", "membrane.labels", true);
        CellExplorerModel.MembraneCholesterol = BioPrefs.Bool("cell", "membrane.cholesterol", true);
        CellExplorerModel.MembraneSugars = BioPrefs.Bool("cell", "membrane.sugars", true);
        CellExplorerModel.MembraneTransport = BioPrefs.Bool("cell", "membrane.transport", true);
        CellExplorerModel.MembraneOsmosis = BioPrefs.Bool("cell", "membrane.osmosis", false);
        // (each cell's parts hidden, as last left; never set: the "(Nice)" parts that start off)
        foreach (var k in Enum.GetValues<CEKind>())
        {
            string? saved;
            try { saved = BioPrefs.String("cell", "hidden." + k.Id()); }
            catch (InvalidOperationException) { saved = null; }
            if (saved == null) continue;
            var s = CellExplorerModel.Cell(k);
            s.Hidden.Clear();
            foreach (var p in saved.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (CEParts.Part(k, p) != null) s.Hidden.Add(p);
        }
    }

    /// <summary>A cell's hidden parts, kept for next time (the Mac's "hidden.animal").</summary>
    public static void SaveHidden(CEKind k) =>
        BioPrefs.Set("cell", "hidden." + k.Id(), string.Join(",", CellExplorerModel.Cell(k).Hidden.OrderBy(h => h, StringComparer.Ordinal)));

    /// <summary>The options the student changed, kept for next time.</summary>
    public static void Save()
    {
        BioPrefs.Set("cell", "cutaway", CellExplorerModel.Cutaway);
        BioPrefs.Set("cell", "labels", CellExplorerModel.Labels);
        BioPrefs.Set("cell", "trueScale", CellExplorerModel.TrueScale);
        BioPrefs.Set("cell", "gramPositive", CellExplorerModel.GramPositive);
        BioPrefs.Set("cell", "membrane.labels", CellExplorerModel.MembraneLabels);
        BioPrefs.Set("cell", "membrane.cholesterol", CellExplorerModel.MembraneCholesterol);
        BioPrefs.Set("cell", "membrane.sugars", CellExplorerModel.MembraneSugars);
        BioPrefs.Set("cell", "membrane.transport", CellExplorerModel.MembraneTransport);
        BioPrefs.Set("cell", "membrane.osmosis", CellExplorerModel.MembraneOsmosis);
    }

    /// <summary>
    /// A launch's state (never remembered), in the Mac's words: the page first (animal, plant, bacterium, membrane, compare,
    /// or --bio-cell), then any of <c>nocut</c> (or <c>whole</c>), <c>cutaway</c>, <c>true</c>, <c>teaching</c>,
    /// <c>nolabels</c> (or <c>labels=off</c>), <c>explode=0.6</c>, <c>gram+</c> (or <c>gram=positive</c>), <c>show=flagellum</c>,
    /// <c>hide=capsule</c>, <c>select=mito</c>, <c>isolate=mito</c>, <c>inside=chloroplast</c> (or <c>isolate=…:inside</c>),
    /// <c>quiz=find|name</c>; on the membrane page <c>osmosis</c>, <c>still</c> (the carrier not at work), <c>nolabels</c>,
    /// <c>popc</c> and <c>select=integral:channel</c>.
    /// </summary>
    public static void Apply(BioLaunchState s)
    {
        if (s.Page == null && CEKinds.FromId(s.Value("cell")) is { } asked) BioNav.Set("cell", asked.Id(), remember: false);
        var page = BioNav.Page("cell");
        if (s.Has("whole") || s.Has("nocut") || s.Value("cutaway") == "off") CellExplorerModel.Cutaway = false;
        if (s.Has("cut") || s.Has("cutaway") || s.Value("cutaway") == "on") CellExplorerModel.Cutaway = true;
        if (s.Has("true") || s.Value("scale") == "true") CellExplorerModel.TrueScale = true;
        if (s.Has("teaching") || s.Value("scale") == "teaching") CellExplorerModel.TrueScale = false;
        if (s.Value("labels") is { } labels) CellExplorerModel.Labels = labels != "off";
        if (s.Has("nolabels")) CellExplorerModel.Labels = false;
        if (double.TryParse(s.Value("explode"), NumberStyles.Float, CultureInfo.InvariantCulture, out var e)) CellExplorerModel.Explode = Math.Clamp(e, 0, 1);
        if (s.Value("gram") == "positive" || s.Has("gram+") || s.Has("gram-positive")) CellExplorerModel.GramPositive = true;
        if (page == "membrane")
        {
            if (s.Has("osmosis")) CellExplorerModel.MembraneOsmosis = true;
            if (s.Has("still")) CellExplorerModel.MembraneTransport = false;
            if (s.Has("nolabels")) CellExplorerModel.MembraneLabels = false;
            if (s.Has("popc")) CellExplorerModel.MembranePopc = true;
            // ("select=integral:channel" arrives split at its colon: the part after "select=" rejoined)
            var i = s.Parts.ToList().FindIndex(p => p.StartsWith("select=", StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                var id = s.Parts[i]["select=".Length..];
                if (i + 1 < s.Parts.Count && CEMembrane.Part(id + ":" + s.Parts[i + 1]) != null) id += ":" + s.Parts[i + 1];
                if (CEMembrane.Part(id) != null) CellExplorerModel.MembraneSelected = id;
            }
            return;
        }
        if (CEKinds.FromId(page) is not { } kind) return;
        foreach (var p in (s.Value("show") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) CellExplorerModel.Cell(kind).Hidden.Remove(p);
        foreach (var p in (s.Value("hide") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) CellExplorerModel.Cell(kind).Hidden.Add(p);
        if (s.Value("inside") is { } ins && CEScene.CanLookInside(ins) && CEParts.Part(kind, ins) != null) CellExplorerModel.Isolate(kind, ins, lookInside: true);
        else if (s.Value("isolate") is { } iso && CEParts.Part(kind, iso) != null) CellExplorerModel.Isolate(kind, iso, s.Has("inside"));
        else if (s.Value("select") is { } sel && CEParts.Part(kind, sel) != null)
        {
            CellExplorerModel.Select(kind, sel);
            CellExplorerModel.CardAsked = true;
        }
        if (s.Value("quiz") is { } quiz)
        {
            // (a picture's quiz: the same questions every time)
            var parts = CEParts.All(kind).Where(p => p.DefaultVisible).Select(p => p.Id).ToList();
            CellExplorerModel.StartQuiz(kind, quiz == "name" ? CEQuizMode.Name : CEQuizMode.Find, parts, seed: 7);
        }
    }
}

/// <summary>The pieces Cell Explorer's pages share.</summary>
internal static class CellUI
{
    /// <summary>Words in which <c>**a term**</c> is bold (the specification's tables mark the term to stress so).</summary>
    public static TextBlock Rich(string text, Ty ty = Ty.Body, string brush = B.Text)
    {
        var t = K.Wrapped("", ty, brush);
        var parts = text.Split("**");
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            t.Inlines.Add(new Run(parts[i]) { FontWeight = i % 2 == 1 ? FontWeights.SemiBold : t.FontWeight });
        }
        System.Windows.Automation.AutomationProperties.SetName(t, Plain(text));
        return t;
    }

    /// <summary>The words without their bold marks.</summary>
    public static string Plain(string text) => text.Replace("**", "", StringComparison.Ordinal);

    /// <summary>A labelled row of a card ("Function", "Structure"): its label small over its words (a term in bold).</summary>
    public static FrameworkElement Fact(string label, string text)
    {
        var v = K.V(3, K.Text(label, Ty.Caption, B.Text2).Strong(), Rich(text));
        System.Windows.Automation.AutomationProperties.SetName(v, $"{label}: {Plain(text)}");
        return v;
    }

    /// <summary>A part's colour as a small rounded square, edged so a pale one shows.</summary>
    public static Border Swatch(Color c, double size = 12)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(3), Background = K.Brush(c), BorderThickness = new Thickness(0.5), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BorderBrushProperty, B.StrongStroke);
        return b;
    }

    /// <summary>A switch with its words at the left (the molecule builder's).</summary>
    public static FrameworkElement Toggle(string text, ToggleSwitch s, string tip)
    {
        s.VerticalAlignment = VerticalAlignment.Center;
        s.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(s, text);
        var words = K.Text(text, Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
        words.ToolTip = tip;
        return K.Columns("*, auto", words.Col(0), s.Col(1));
    }

    /// <summary>The 3D view's card (the chemistry builder's, BioSceneCard): a top bar, the scene with what lies over it (the
    /// scale bar, a banner), and the hint under it.</summary>
    public static Border SceneCard(UIElement bar, UIElement sceneHost, string hint = "Drag to turn · scroll to zoom · click a part to learn about it")
    {
        var foot = K.Text(hint, Ty.Caption, B.Text2);
        foot.HorizontalAlignment = HorizontalAlignment.Center;
        foot.Margin = new Thickness(12);
        if (bar is FrameworkElement fb) fb.Margin = new Thickness(16);
        return K.Card(K.Rows("auto, *, auto", bar.Row(0), sceneHost.Row(1), foot.Row(2)), new Thickness(0));
    }

    /// <summary>
    /// Escape for a page (spec §4.1 Interactions: Escape deselects, or leaves Isolate): the tool sits in a sheet that closes
    /// on Escape before anything inside it hears the key, so the page listens at its window instead, and takes the key only
    /// while it is shown and has something to let go of (<paramref name="letGo"/> returns whether it did); otherwise the
    /// sheet closes as usual.
    /// </summary>
    public static void Escape(FrameworkElement page, Func<bool> letGo)
    {
        Window? win = null;
        void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Escape || !page.IsVisible) return;
            if (letGo()) e.Handled = true;
        }
        page.Loaded += (_, _) =>
        {
            if (win != null) win.PreviewKeyDown -= OnKey;
            win = Window.GetWindow(page);
            if (win != null) win.PreviewKeyDown += OnKey;
        };
        page.Unloaded += (_, _) =>
        {
            if (win != null) win.PreviewKeyDown -= OnKey;
            win = null;
        };
    }

    /// <summary>"2,400" (thousands grouped, no decimals past what a number this size needs).</summary>
    public static string Number(double v) =>
        v >= 100 ? v.ToString("#,0", CultureInfo.InvariantCulture) : v >= 10 ? v.ToString("0.#", CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>
/// What a page reads of the shared 3D view (BioSceneView) that it has no property for: its camera (how far it is, for the
/// scale bar) and its turning world (a layer drawn in the scene's frame, for what a page moves frame by frame — the
/// membrane's water). Read through the view's own children (it is a Grid round a Viewport3D whose world turns), so the
/// shared view is left as it is; if its insides change, these find nothing and the page goes without.
/// </summary>
internal static class BioSceneReach
{
    public static Viewport3D? Viewport(BioSceneView v) => v.Children.OfType<Viewport3D>().FirstOrDefault();

    public static PerspectiveCamera? Camera(BioSceneView v) => Viewport(v)?.Camera as PerspectiveCamera;

    /// <summary>The world's turn (the first model visual that turns).</summary>
    public static RotateTransform3D? Turn(BioSceneView v) =>
        Viewport(v)?.Children.OfType<ModelVisual3D>().Select(m => m.Transform).OfType<RotateTransform3D>().FirstOrDefault();

    /// <summary>A layer turning with the world, added once (null if the view's insides are not as expected).</summary>
    public static ModelVisual3D? Layer(BioSceneView v)
    {
        if (Viewport(v) is not { } vp || Turn(v) is not { } turn) return null;
        var layer = new ModelVisual3D { Transform = turn };
        vp.Children.Add(layer);
        return layer;
    }

    /// <summary>How many view pixels a scene unit at the turning centre spans now (the view's 40° vertical field of view).</summary>
    public static double PixelsPerUnit(BioSceneView v)
    {
        if (Camera(v) is not { } cam || v.ActualHeight <= 0) return 0;
        var depth = cam.Position.Z - (Turn(v)?.CenterZ ?? 0);
        return depth <= 0 ? 0 : v.ActualHeight / (2 * depth * Math.Tan(20 * Math.PI / 180));
    }
}

/// <summary>
/// A scale bar at the bottom left of a 3D view (spec §4.1 item 7): the first of 1, 5 or 10 µm that comes out 60–160 long
/// at the current zoom, else the nearest other length that does (a bacterium seen close needs 500 nm; the membrane is in
/// nm), always shown, following the camera; with a "Not to scale" badge beside it in teaching scale.
/// </summary>
internal sealed class CellScaleBar : Grid
{
    private readonly BioSceneView scene;
    private readonly string unit;
    private readonly Path bar = new() { StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat, SnapsToDevicePixels = true };
    private readonly TextBlock words = K.Text("", Ty.Caption);
    private readonly ContentControl badge = new() { Focusable = false, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly double[] preferred, others;
    private PerspectiveCamera? camera;
    private string? notToScale;

    /// <summary>A bar in µm (1, 5 or 10 first) or, for the membrane, nm (5, 10 or 20 first).</summary>
    public CellScaleBar(BioSceneView scene, string unit)
    {
        this.scene = scene;
        this.unit = unit;
        (preferred, others) = unit == "nm" ? (new[] { 5.0, 10, 20 }, new[] { 1.0, 2, 50 }) : (new[] { 1.0, 5, 10 }, new[] { 0.1, 0.2, 0.5, 2, 20, 50, 100 });
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(18, 0, 0, 14);
        bar.SetResourceReference(Shape.StrokeProperty, B.Text);
        words.FontWeight = FontWeights.SemiBold;
        words.Margin = new Thickness(0, 0, 0, 3);
        var stack = K.V(0, words, bar);
        stack.VerticalAlignment = VerticalAlignment.Bottom;
        badge.VerticalAlignment = VerticalAlignment.Bottom;
        // (on a solid plate, as the labels are: the card's own fill is nearly clear in dark mode, and a bacterium's fimbriae
        //  ran across "1 µm" and "Not to scale")
        var plate = K.Card(K.H(0, stack, badge), new Thickness(10, 6, 10, 7), 9);
        plate.Bg(B.Solid);
        Children.Add(plate);
        Loaded += (_, _) => Watch();
        Unloaded += (_, _) => { if (camera != null) camera.Changed -= OnCamera; camera = null; };
        scene.SizeChanged += (_, _) => Update();
    }

    /// <summary>Teaching scale's badge beside the bar (null: none).</summary>
    public string? NotToScale
    {
        get => notToScale;
        set
        {
            notToScale = value;
            badge.Content = value == null ? null : K.Chip(value, K.Tone("orange"), strong: true);
        }
    }

    private void Watch()
    {
        if (camera != null) camera.Changed -= OnCamera;
        camera = BioSceneReach.Camera(scene);
        if (camera != null) camera.Changed += OnCamera;
        Update();
    }

    private void OnCamera(object? sender, EventArgs e) => Update();

    public void Update()
    {
        var ppu = BioSceneReach.PixelsPerUnit(scene);
        if (ppu <= 0) { Visibility = Visibility.Hidden; return; }
        Visibility = Visibility.Visible;
        var best = Length(ppu, preferred, others);
        var w = Math.Clamp(best * ppu, 8, 400);
        bar.Data = Geometry.Parse(string.Create(CultureInfo.InvariantCulture, $"M0,0 L0,6 M0,3 L{w:0.##},3 M{w:0.##},0 L{w:0.##},6"));
        var shown = Words(best, unit);
        words.Text = shown;
        System.Windows.Automation.AutomationProperties.SetName(this, $"Scale bar: {shown}" + (notToScale == null ? "" : $", {notToScale.ToLowerInvariant()}"));
    }

    /// <summary>The bar's length (the Mac's choice): the first preferred length that comes out 60–160 long at this zoom, else
    /// the shortest of all that does, else the one nearest 110 long.</summary>
    public static double Length(double ppu, double[] preferred, double[] others)
    {
        static bool Fits(double px) => px is >= 60 and <= 160;
        foreach (var l in preferred) if (Fits(l * ppu)) return l;
        var all = preferred.Concat(others).OrderBy(l => l).ToArray();
        foreach (var l in all) if (Fits(l * ppu)) return l;
        return all.MinBy(l => Math.Abs(l * ppu - 110));
    }

    /// <summary>"5 µm", "500 nm" (under a micrometre in nanometres), "10 nm".</summary>
    public static string Words(double l, string unit)
    {
        if (unit == "µm" && l < 1) return $"{Math.Round(l * 1000)} nm";
        return (l == Math.Round(l) ? ((int)l).ToString(CultureInfo.InvariantCulture) : l.ToString("G", CultureInfo.InvariantCulture)) + " " + unit;
    }
}

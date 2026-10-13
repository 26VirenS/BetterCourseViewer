using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// The Membrane page (spec §4.1.1): a 60 × 60 nm patch of plasma membrane in 3D, cut on its two near sides to show the
/// bilayer (Model/CellMembraneModel.cs) — phospholipids with their heads and tails, cholesterol, integral proteins (a
/// channel and a carrier among them), peripheral proteins, glycoproteins and glycolipids — its twelve parts labelled and
/// listed, each with a card. Switches show or hide cholesterol, the sugar chains and the other proteins; Osmosis (spec
/// §4.1 Nice) sends water through the aquaporin and rocks the carrier open to one side then the other, with an arrow for
/// the water's way (still, with Windows' animations off). "See a real phospholipid" opens POPC atom by atom over the page.
/// </summary>
internal sealed class CellMembranePage : Grid
{
    private readonly ToolContext c;
    private readonly BioSceneView scene = new("3D model of a patch of plasma membrane, 60 by 60 nanometres, cut open on two sides") { PickSelects = false };
    private readonly Stack right = K.V(14);
    private readonly ToggleSwitch cholesterol = new(), sugars = new(), proteins = new(), osmosis = new();
    private readonly Dictionary<string, (Button Button, Border Wash, TextBlock Name)> rows = new();
    private readonly Grid overlay = new() { Visibility = Visibility.Collapsed };
    private readonly CEMembrane.Model moving = CEMembrane.Moving();
    private ModelVisual3D? layer;
    private Model3DGroup? water;
    private readonly List<(GeometryModel3D Bead, TranslateTransform3D Move, ScaleTransform3D Size, double Phase)> beads = new();
    private readonly List<QuaternionRotation3D> carrierTurns = new();
    private TranslateTransform3D? soluteMove;
    private ScaleTransform3D? soluteSize;
    private bool animating;
    private bool syncing;
    private string rightSignature = "";
    private BioSceneView? popcScene;

    public CellMembranePage(ToolContext c)
    {
        this.c = c;
        scene.Picked += id =>
        {
            if (CEMembrane.PartOf(id) is { } p) Select(p);
            else Select(null);
        };
        scene.HoverChanged += id => scene.ToolTip = CEMembrane.PartOf(id) is { } p ? CEMembrane.Part(p)?.Name : null;

        // (the left column: the parts, then what to show)
        var left = K.V(14, BioUI.Heading("Parts"), BioUI.Caption("Press a part to learn about it."));
        var list = K.V(2);
        foreach (var p in CEMembrane.Parts) list.Children.Add(Row(p));
        left.Children.Add(list);
        left.Children.Add(BioUI.Heading("Show").Margin(0, 6, 0, 0));
        void Wire(ToggleSwitch s, Action<bool> set)
        {
            s.Checked += (_, _) => { if (!syncing) { set(true); CellExplorerModel.Tell(); } };
            s.Unchecked += (_, _) => { if (!syncing) { set(false); CellExplorerModel.Tell(); } };
        }
        Wire(cholesterol, v => CellExplorerModel.MembraneCholesterol = v);
        Wire(sugars, v => CellExplorerModel.MembraneSugars = v);
        Wire(proteins, v => CellExplorerModel.MembraneProteins = v);
        Wire(osmosis, v => CellExplorerModel.MembraneOsmosis = v);
        left.Children.Add(CellUI.Toggle("Cholesterol", cholesterol, "Cholesterol among the tails, its OH at the heads"));
        left.Children.Add(CellUI.Toggle("Sugar chains", sugars, "The glycoproteins' and glycolipids' sugars, on the outside face"));
        left.Children.Add(CellUI.Toggle("Other proteins", proteins, "The integral proteins besides the channel and the carrier"));
        left.Children.Add(CellUI.Toggle("Osmosis", osmosis, Motion.On ? "Water through the aquaporin (a channel), and the carrier rocking open to one side, then the other"
            : "Where water goes through the aquaporin (still: animations are off in Windows)"));
        left.Children.Add(BioUI.Legend(new[]
        {
            (CEMembrane.Water.ToColor(), "Water (osmosis)"),
            (CEMembrane.Hydroxyl.ToColor(), "Cholesterol's OH group"),
        }));

        // (the middle: the patch in its card, a scale bar in nanometres)
        var turnButtons = BioSceneCard.Buttons(scene);
        var chip = K.Chip("Drawn to scale", K.Tone("green"), strong: true);
        chip.ToolTip = "Every part at its real size, in nanometres";
        var bar = K.Columns("*, auto", K.H(10, K.Text("Plasma membrane · 60 × 60 nm", Ty.Subtitle), chip).Col(0), turnButtons.Col(1));
        var host = new Grid { ClipToBounds = true };
        host.Children.Add(scene);
        host.Children.Add(new CellScaleBar(scene, "nm"));
        var card = CellUI.SceneCard(bar, host, "Drag to turn · scroll to zoom · click a part to learn about it");

        var work = BioUI.Work(left, card, right);
        Children.Add(work);
        Children.Add(overlay);
        CellUI.Escape(this, LetGo);

        void Changed() => Refresh();
        void Look() { rightSignature = ""; Recolour(); Refresh(); }
        Loaded += (_, _) =>
        {
            CellExplorerModel.Changed -= Changed;
            CellExplorerModel.Changed += Changed;
            Appearance.Changed -= Look;
            Appearance.Changed += Look;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            CellExplorerModel.Changed -= Changed;
            Appearance.Changed -= Look;
            Animate(false);
        };
    }

    private FrameworkElement Row(CEMembranePart p)
    {
        var id = p.Id;
        var name = K.Text(p.Name, Ty.Body);
        var face = K.Columns("auto, *", CellUI.Swatch(p.Colour.ToColor()).Margin(0, 0, 10, 0).Col(0), name.Col(1));
        var b = K.Subtle(face, () => Select(id), new Thickness(8, 5, 8, 6), 4);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.ToolTip = p.Function;
        b.GotKeyboardFocus += (_, _) =>
        {
            if (System.Windows.Input.InputManager.Current.MostRecentInputDevice is System.Windows.Input.KeyboardDevice && CellExplorerModel.MembraneSelected != id) Select(id);
        };
        var wash = new Border { CornerRadius = new CornerRadius(4), Opacity = 0, IsHitTestVisible = false };
        wash.SetResourceReference(Border.BackgroundProperty, B.Accent);
        rows[id] = (b, wash, name);
        return new Grid { Children = { wash, b } };
    }

    private void Recolour()
    {
        foreach (var (id, r) in rows)
            if (CEMembrane.Part(id) is { } p && r.Button.Content is Grid g && g.Children[0] is Border swatch) swatch.Background = K.Brush(p.Colour.ToColor());
    }

    private static void Select(string? part)
    {
        if (CellExplorerModel.MembraneSelected == part) return;
        CellExplorerModel.MembraneSelected = part;
        CellExplorerModel.Tell();
    }

    private bool LetGo()
    {
        if (overlay.Visibility == Visibility.Visible) { ClosePopc(); return true; }
        if (CellExplorerModel.MembraneSelected == null) return false;
        Select(null);
        return true;
    }

    // ---- drawing ----

    private void Refresh()
    {
        syncing = true;
        try
        {
            cholesterol.IsChecked = CellExplorerModel.MembraneCholesterol;
            sugars.IsChecked = CellExplorerModel.MembraneSugars;
            proteins.IsChecked = CellExplorerModel.MembraneProteins;
            osmosis.IsChecked = CellExplorerModel.MembraneOsmosis;
        }
        finally { syncing = false; }
        var dark = Appearance.Dark;
        var chol = CellExplorerModel.MembraneCholesterol;
        var sug = CellExplorerModel.MembraneSugars;
        var pro = CellExplorerModel.MembraneProteins;
        // (the carrier moves with the osmosis animation: it is drawn by the page then, not in the scene)
        var moves = CellExplorerModel.MembraneOsmosis && Motion.On;
        scene.Show($"membrane|{dark}|{chol}|{sug}|{pro}|{moves}", () => CEMembrane.Build(dark, chol, sug, pro, moves).Content, "membrane");
        var sel = CellExplorerModel.MembraneSelected;
        scene.Selection = sel == null ? null : sel.StartsWith("side:", StringComparison.Ordinal) ? sel : "part:" + sel;
        scene.Accessibility = "3D model of a patch of plasma membrane, cut open on two sides; 12 kinds of parts" +
            (sel != null && CEMembrane.Part(sel) is { } sp ? $"; selected: {sp.Name.ToLowerInvariant()}" : "");
        foreach (var (id, r) in rows)
        {
            var on = id == sel;
            r.Wash.Opacity = on ? 0.16 : 0;
            r.Name.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
        Osmosis(dark);
        Right();
    }

    private void Right()
    {
        var sel = CellExplorerModel.MembraneSelected;
        var sig = $"{sel}|{CellExplorerModel.MembraneOsmosis}|{Appearance.Dark}";
        if (sig == rightSignature) return;
        rightSignature = sig;
        right.Children.Clear();
        if (sel != null && CEMembrane.Part(sel) is { } p)
        {
            var rows = new List<UIElement> { CellUI.Fact("What it does", p.Function), CellUI.Fact("Structure", p.Structure) };
            if (p.Mixup is { } mix) rows.Add(CellUI.Fact("Common mix-up", mix));
            if (p.Id.StartsWith("bilayer", StringComparison.Ordinal) || p.Id is "glycolipid" or "cholesterol")
            {
                var popc = K.Button("See a real phospholipid", ShowPopc, icon: SymbolRegular.Molecule20);
                popc.ToolTip = "POPC, atom by atom";
                rows.Add(popc);
            }
            var close = K.Link("Close card", () => Select(null), Ty.Caption);
            close.HorizontalAlignment = HorizontalAlignment.Left;
            close.ToolTip = "Escape";
            rows.Add(close);
            var card = BioUI.Card(p.Name, p.Colour.ToColor(), "Plasma membrane", rows.ToArray());
            right.Children.Add(card);
            if (Motion.On) Motion.FadeIn(card, 180);
        }
        else
        {
            var see = ChemKit.Wide("See a real phospholipid", ShowPopc, icon: SymbolRegular.Molecule20);
            see.ToolTip = "POPC, atom by atom, as the Biomolecule Builder draws it";
            right.Children.Add(BioUI.Card("The plasma membrane", BioColors.Tool("cell"), "The fluid mosaic model",
                K.Wrapped("A double layer of phospholipids, heads outward and tails inward, with proteins drifting in it like a mosaic. It controls what enters and leaves the cell.", Ty.Body),
                BioUI.Facts(new (string, string, Color?)[]
                {
                    ("Thickness", "about 4–5 nm; up to about 10 nm counting the proteins that stick out", null),
                    ("This patch", "60 × 60 nm, drawn to scale", null),
                    ("Cholesterol", "12% of the lipid places", null),
                }),
                see));
        }
        if (CellExplorerModel.MembraneOsmosis)
            right.Children.Add(ChemKit.Card(K.V(8, K.Text("Osmosis", Ty.BodyStrong),
                K.Wrapped("Water crosses the membrane by osmosis, mostly through aquaporins: channel proteins with a pore just wide enough for water molecules in single file. It moves toward the side with more dissolved solute (here the cytoplasm), down its own concentration gradient, with no energy used.", Ty.Body),
                K.Wrapped("The carrier binds a solute on one side and changes shape to release it on the other. A channel does not change shape.", Ty.Caption, B.Text2))));
    }

    // ---- osmosis (the page's own layer, turning with the scene) ----

    private void Osmosis(bool dark)
    {
        var on = CellExplorerModel.MembraneOsmosis;
        layer ??= BioSceneReach.Layer(scene);
        if (layer == null) return;
        if (!on)
        {
            layer.Content = null;
            Animate(false);
            return;
        }
        var group = new Model3DGroup();
        beads.Clear();
        carrierTurns.Clear();
        // (water: beads down the channel, outside to inside)
        var waterMat = BioWpf.Material(new BioMaterial(CEMembrane.Water.HexFor(dark), Roughness: 0.3));
        var bead = BioWpf.Mesh(BioShapes.Icosphere(0.3, 1));
        water = new Model3DGroup();
        const int count = 9;
        for (var k = 0; k < count; k++)
        {
            var move = new TranslateTransform3D();
            var size = new ScaleTransform3D(1, 1, 1);
            var g = new GeometryModel3D(bead, waterMat) { Transform = new Transform3DGroup { Children = { size, move } } };
            water.Children.Add(g);
            beads.Add((g, move, size, (double)k / count));
        }
        group.Children.Add(water);
        // (the arrow: the water's net way, in front of the near cut face)
        var (from, to) = moving.Arrow;
        var head = from + (to - from) * 0.78;
        var arrow = new BioMesh();
        arrow.Append(BioShapes.Cylinder(from, head, 0.32, 16));
        arrow.Append(BioPath.Tube(new[] { head, to }, 0.9, radii: new[] { 0.9, 0.02 }, sides: 16, smooth: false));
        group.Children.Add(new GeometryModel3D(BioWpf.Mesh(arrow), waterMat));
        // (the carrier, rocking: its seven helices, and its solute)
        if (Motion.On)
        {
            var helix = BioWpf.Mesh(BioShapes.Cylinder(new BioVec(0, -2.25, 0), new BioVec(0, 2.25, 0), 0.5, 12));
            var carrierMat = BioWpf.Material(new BioMaterial(CEMembrane.CarrierColour.HexFor(dark), Roughness: 0.4));
            foreach (var (centre, outsideQ, _) in moving.Carrier)
            {
                var turn = new QuaternionRotation3D(Q(outsideQ));
                carrierTurns.Add(turn);
                var t = new Transform3DGroup { Children = { new RotateTransform3D(turn), new TranslateTransform3D(centre.X, centre.Y, centre.Z) } };
                group.Children.Add(new GeometryModel3D(helix, carrierMat) { Transform = t });
            }
            soluteMove = new TranslateTransform3D();
            soluteSize = new ScaleTransform3D(1, 1, 1);
            var soluteMat = BioWpf.Material(new BioMaterial(dark ? "#ffd60a" : "#c79a00", Roughness: 0.35));
            group.Children.Add(new GeometryModel3D(BioWpf.Mesh(BioShapes.Icosphere(0.45, 1)), soluteMat) { Transform = new Transform3DGroup { Children = { soluteSize, soluteMove } } });
        }
        else
        {
            soluteMove = null;
            soluteSize = null;
        }
        layer.Content = group;
        Place(0);
        Animate(Motion.On);
    }

    private static Quaternion Q(CEQuat q) => new(q.X, q.Y, q.Z, q.W);

    private void Animate(bool on)
    {
        if (on == animating) return;
        animating = on;
        if (on) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs r) Place(r.RenderingTime.TotalSeconds);
    }

    /// <summary>Everything that moves, at a moment (seconds): the water a bead every half second, four seconds through; the
    /// carrier open outside, closing, open inside, every five seconds, its solute carried across meanwhile. With Windows'
    /// animations off, a still picture (the moment 0).</summary>
    private void Place(double seconds)
    {
        var (outside, inside) = (moving.ChannelPath.In, moving.ChannelPath.Out);
        foreach (var (_, move, size, phase) in beads)
        {
            var t = Motion.On ? (seconds / 4.5 + phase) % 1 : phase;
            // (from 3 nm above the channel, on the outside, to 3 nm below it, in the cytoplasm)
            var u = -0.35 + 1.7 * t;
            var p = outside + (inside - outside) * u;
            move.OffsetX = p.X;
            move.OffsetY = p.Y;
            move.OffsetZ = p.Z;
            var s = Math.Clamp(Math.Min(t, 1 - t) / 0.12, 0, 1);
            size.ScaleX = size.ScaleY = size.ScaleZ = s;
        }
        if (carrierTurns.Count == moving.Carrier.Count && soluteMove != null && soluteSize != null)
        {
            // (one cycle: open outside 0–0.3, closing 0.3–0.5, open inside 0.5–0.8, opening outside again 0.8–1)
            var cycle = seconds / 5 % 1;
            var shut = cycle < 0.3 ? 0 : cycle < 0.5 ? Smooth((cycle - 0.3) / 0.2) : cycle < 0.8 ? 1 : 1 - Smooth((cycle - 0.8) / 0.2);
            for (var k = 0; k < carrierTurns.Count; k++)
            {
                var (_, a, b) = moving.Carrier[k];
                carrierTurns[k].Quaternion = Quaternion.Slerp(Q(a), Q(b), shut);
            }
            // (the solute: comes in from outside, rides across while the carrier turns, leaves inside)
            var path = moving.CarrierPath;
            BioVec at;
            double size;
            if (cycle < 0.3) { at = path[0] + (path[1] - path[0]) * Smooth(cycle / 0.3); size = Math.Min(1, cycle / 0.08); }
            else if (cycle < 0.5) { at = path[1] + (path[2] - path[1]) * Smooth((cycle - 0.3) / 0.2); size = 1; }
            else if (cycle < 0.8) { at = path[2] + (path[3] - path[2]) * Smooth((cycle - 0.5) / 0.3); size = Math.Min(1, (0.8 - cycle) / 0.08); }
            else { at = path[0]; size = 0; }
            soluteMove.OffsetX = at.X;
            soluteMove.OffsetY = at.Y;
            soluteMove.OffsetZ = at.Z;
            soluteSize.ScaleX = soluteSize.ScaleY = soluteSize.ScaleZ = Math.Clamp(size, 0, 1);
        }
    }

    private static double Smooth(double t) => CEGeo.Smoothstep(0, 1, t);

    // ---- a real phospholipid (spec §4.1.1: POPC, §6.9) ----

    /// <summary>POPC atom by atom over the page (a sheet of its own would wait for this one to close): its choline,
    /// phosphate and glycerol head, its saturated palmitate tail and its kinked oleate tail, labelled; and the Biomolecule
    /// Builder's own view of it.</summary>
    private void ShowPopc()
    {
        if (BioData.Molecule("POV") is not { } popc) return;
        overlay.Children.Clear();
        var smoke = new Border { Background = K.Brush(Color.FromArgb(Appearance.Dark ? (byte)140 : (byte)102, 0, 0, 0)) };
        smoke.MouseLeftButtonDown += (_, e) => { e.Handled = true; ClosePopc(); };
        popcScene = new BioSceneView("3D model of POPC, a phospholipid: a choline and phosphate head, glycerol, and two fatty-acid tails") { PickSelects = false };
        var dark = Appearance.Dark;
        popcScene.Show($"popc|{dark}", () => PopcScene(popc), "popc", () => BioMoleculeScene.Symbols(popc.Model(hydrogens: false), BioAtomStyle.BallStick, hydrogens: false));
        var close = K.Button("Done", ClosePopc, accent: true);
        close.ToolTip = "Back to the membrane (Esc)";
        var builder = K.Button("Open in Biomolecule Builder", () => BioTools.Open(c, "biomol", new Dictionary<string, string> { ["molecule"] = "POV" }), icon: SymbolRegular.Open20);
        builder.MinWidth = 0;
        var head = K.Columns("*, auto, auto", K.V(2, K.Text("POPC, a real phospholipid", Ty.Subtitle), K.Text("C₄₂H₈₂NO₈P · the commonest phospholipid in animal membranes", Ty.Body, B.Text2)).Col(0),
            builder.Margin(0, 0, 10, 0).Col(1), close.Col(2));
        head.Margin = new Thickness(20, 16, 20, 8);
        var foot = BioUI.Caption("The head (choline and phosphate) is polar and faces the water; the two fatty-acid tails are nonpolar. The oleate tail's cis double bond puts a kink in it, which keeps the membrane fluid.");
        foot.Margin = new Thickness(20, 6, 20, 16);
        var body = K.Rows("auto, *, auto", head.Row(0), popcScene.Row(1), foot.Row(2));
        var card = K.Card(body, new Thickness(0), 12);
        card.Width = Math.Min(860, Math.Max(480, ActualWidth - 120));
        card.Height = Math.Min(620, Math.Max(380, ActualHeight - 80));
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.Bg(B.Solid);
        overlay.Children.Add(smoke);
        overlay.Children.Add(card);
        overlay.Visibility = Visibility.Visible;
        if (Motion.On) { Motion.FadeIn(smoke, 180); Motion.Pop(card); }
        Dispatcher.BeginInvoke(() => close.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ClosePopc()
    {
        overlay.Visibility = Visibility.Collapsed;
        overlay.Children.Clear();
        popcScene = null;
    }

    /// <summary>POPC as the Biomolecule Builder draws it (ball and stick, no hydrogens), its parts labelled at their atoms.</summary>
    private static BioSceneContent PopcScene(BioMolecule popc)
    {
        var model = popc.Model(hydrogens: false);
        var items = BioMoleculeScene.Items(model, BioAtomStyle.BallStick, hydrogens: false);
        BioVec At(string name) => model.Atoms.FirstOrDefault(a => a.Name == name)?.Pos ?? BioVec.Zero;
        var labels = new List<BioLabel3D>
        {
            new("popc:choline", "Choline (+)", At("N"), Area: 6),
            new("popc:phosphate", "Phosphate (−)", At("P"), Area: 6),
            new("popc:glycerol", "Glycerol", At("C2"), Area: 5),
            new("popc:oleate", "Oleate tail: cis double bond (kink)", (At("C29") + At("C210")) / 2, Area: 4),
            new("popc:palmitate", "Palmitate tail (saturated)", At("C310"), Area: 4),
        };
        return new BioSceneContent { Items = items, Labels = labels };
    }
}

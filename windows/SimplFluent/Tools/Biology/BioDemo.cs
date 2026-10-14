using System.Windows;
using System.Windows.Controls;
using Simpl.Kit;
using Simpl.Shell;
using Simpl.Support;

namespace Simpl.Tools;

/// <summary>
/// The biology toolkit's foundation, seen working (a development run's: --place tools --bio-demo): the shared 3D view
/// with a stretch of B-DNA atom by atom, its backbones as tubes, its H-bonds dashed and a cut-open translucent shell
/// beside it; labels with leader lines; the step-through under it selecting a part at each step (the rest dimmed); a
/// sequence in groups of ten; a card and a legend — so the screenshot suite pictures the pieces every tool is built
/// from, light and dark, before any tool uses them. Not a tool: nothing in the app opens it.
/// </summary>
public static class BioDemo
{
    private const string Sequence = "ATGCGTACGTTA";

    public static void Show(Window? owner)
    {
        var win = owner ?? Application.Current?.MainWindow;
        var sheet = new Sheet { Bare = true, Width = Math.Max(760, (win?.ActualWidth ?? 1280) - 70), Height = Math.Max(560, (win?.ActualHeight ?? 860) - 90) };
        var done = K.Button("Done", () => sheet.Close());
        done.Margin = new Thickness(0, 0, 14, 0);
        done.VerticalAlignment = VerticalAlignment.Center;
        var words = K.V(2, K.Text("Biology foundation", Ty.Title), K.Text("The shared 3D view, labels, step-through and sequence text", Ty.Body, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var tile = K.IconTile(Wpf.Ui.Controls.SymbolRegular.Ribbon24, BioColors.Tool("dna"), 44).Margin(0, 0, 14, 0);
        var head = K.Columns("auto, auto, *", done.Col(0), tile.Col(1), words.Col(2));
        head.Margin = new Thickness(40, 20, 40, 12);
        var g = K.Rows("auto, *", head.Row(0), Body().Row(1));
        g.Bg(B.Window);
        sheet.Body = g;
        Dialogs.Show(sheet, win);
        if (sheet.Card is { } card && win != null) card.MaxWidth = Math.Max(card.MaxWidth, win.ActualWidth - 48);
    }

    private static FrameworkElement Body()
    {
        var scene = new BioSceneView("3D model of B-DNA, 12 base pairs, beside a cut-open shell") { PickSelects = true };
        var steps = new BioStepController(new[]
        {
            new BioStep(0, "The double helix", "Two strands of nucleotides wind round each other, 10.5 base pairs a turn.", Group: 1),
            new BioStep(1, "Strand 1", "Strand 1 runs 5′ to 3′ up the helix.", Group: 1),
            new BioStep(2, "Strand 2", "Strand 2 runs 5′ to 3′ the other way: the strands are antiparallel.", Group: 2),
            new BioStep(3, "Base pairs", "A pairs with T by two hydrogen bonds; G pairs with C by three.", Group: 2),
        });
        var dark = Appearance.Dark;
        scene.Show($"demo|{dark}", () => Scene(dark), "demo");
        void Select()
        {
            scene.Selection = steps.Index switch { 1 => "strand:1", 2 => "strand:2", 3 => "hbond", _ => null };
        }
        void StepChanged() => Select();
        scene.Loaded += (_, _) => { steps.Changed -= StepChanged; steps.Changed += StepChanged; };
        scene.Unloaded += (_, _) => steps.Changed -= StepChanged;
        BioStepKeys.Attach(scene, steps);
        var bar = K.Columns("*, auto", K.Text("B-DNA · 12 bp", Ty.Subtitle).Col(0), BioSceneCard.Buttons(scene).Col(1));
        var card = BioSceneCard.Make(bar, scene);
        var centre = K.Rows("*, auto", card.Row(0), new BioStepControls(steps, new Dictionary<int, string> { [1] = "The helix", [2] = "The pairs" }).Margin(0, 14, 0, 0).Row(1));

        var left = K.V(14, BioUI.Heading("Bases"),
            BioUI.Legend("ATGC".Select(b => (BioColors.Base(b), $"{b} · {Name(b)}"))),
            BioUI.Caption("Colours always come with their letters. Drag to turn, scroll to zoom, press a label or a part."));
        var right = K.V(14,
            BioUI.Card("Strand 1", BioPalette.Strand1.ToColor(), "5′→3′, with its complement under it",
                BioUI.SequenceText(Sequence),
                BioUI.SequenceText(BioSeq.Complement(Sequence), fivePrimeFirst: false)),
            BioUI.Facts(new (string, string, System.Windows.Media.Color?)[]
            {
                ("GC content", $"{BioSeq.GcContent(Sequence) * 100:0}%", null),
                ("Base pairs", $"{Sequence.Length}", null),
                ("Rise", "3.40 Å per bp", null),
            }),
            BioUI.Card("Selected", null, null, BioUI.FactRow("What a press picks", "A part's id: atom:12, strand:1 … The step-through selects one at each step.")));
        return BioUI.Work(left, centre, right);
    }

    private static string Name(char b) => b switch { 'A' => "adenine", 'T' => "thymine", 'G' => "guanine", _ => "cytosine" };

    /// <summary>The demo's scene, made off the UI thread from values.</summary>
    private static BioSceneContent Scene(bool dark)
    {
        var d = BioBDNA.Build(Sequence);
        var items = BioMoleculeScene.Items(d.Model, BioAtomStyle.BallStick);
        items.Add(new BioSceneItem("strand:1", BioPath.Tube(d.Phosphates(1), 0.6, sides: 12), new BioMaterial(BioPalette.Strand1, dark)));
        items.Add(new BioSceneItem("strand:2", BioPath.Tube(d.Phosphates(2), 0.6, sides: 12), new BioMaterial(BioPalette.Strand2, dark)));
        var dashes = new BioMesh();
        foreach (var h in d.HBonds) dashes.Append(BioMoleculeMeshes.Dashes(d.Atoms[h.A].Pos, d.Atoms[h.B].Pos));
        items.Add(new BioSceneItem("hbond", dashes, new BioMaterial(BioPalette.HBond, dark)));
        // (a translucent shell, cut open: the cutaway and the see-through drawing every cell uses)
        var shell = BioShapes.Radial(BioShapes.CapsuleRadius(5, 10), 24, 48, BioCut.Octant, BioShapes.CapsuleRadius(4.4, 10)).Translated(new BioVec(24, 20, 0));
        items.Add(new BioSceneItem("shell", shell, new BioMaterial("#2fb67c", Alpha: 0.4, Roughness: 0.2), Order: 1));
        items.Add(new BioSceneItem("shell:core", BioShapes.Icosphere(2.5, 2).Translated(new BioVec(24, 20, 0)), new BioMaterial("#e5a500", Roughness: 0.5)));
        var top = d.Sequence.Length - 1;
        var labels = new List<BioLabel3D>
        {
            new("strand:1", "Strand 1 (5′ end)", d.Pos(1, 0, "P") ?? BioVec.Zero, Area: 3),
            new("strand:2", "Strand 2 (5′ end)", d.Pos(2, top, "P") ?? BioVec.Zero, Area: 3),
            new("hbond", "Hydrogen bonds", d.Pos(1, 5, "N1") ?? BioVec.Zero, Area: 2),
            new($"atom:{d.Index(1, 3, "C1'") ?? 0}", "Deoxyribose", d.Pos(1, 3, "C1'") ?? BioVec.Zero),
            new("shell", "Cut-open shell", new BioVec(24 + 5, 20 + 7, 0), new BioVec(1, 0.3, 0), Area: 4),
            new("shell:core", "Inside", new BioVec(24, 20, 0), Area: 1),
        };
        return new BioSceneContent { Items = items, Labels = labels };
    }
}

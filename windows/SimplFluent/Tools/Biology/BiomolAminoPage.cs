using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace Simpl.Tools;

/// <summary>
/// The Amino acids page (spec §4.4 Amino acids): the 20 in a grid grouped by AP/Campbell class (nonpolar, polar, acidic,
/// basic) in their class colours, each with its name, its three- and one-letter codes and a sketch of its R group; the
/// one chosen in 3D (its default rotamer, §6.3) with its card — class, polarity, side-chain pKa, charge at pH 7.4,
/// hydropathy, mass, codons and its note — and "Colour by Part" showing its amino group, α-carbon, carboxyl group and R
/// group. The Chirality switch shows the mirror image (the D-form, never in proteins) and the CORN rule says which is which.
/// </summary>
internal sealed class BiomolAminoPage : BiomolPageView
{
    private readonly Dictionary<char, (Border Wash, Border Mark, Button Button)> tiles = new();
    private readonly ToggleSwitch mirror = new();
    private readonly BiomolViewControls view = new();
    private bool syncing;

    protected override string HydrogenKey => "amino";

    public BiomolAminoPage(ToolContext c) : base(c, "3D model of an amino acid")
    {
        Left.Children.Add(BioUI.Heading("The 20 amino acids"));
        Left.Children.Add(BioUI.Caption("Grouped by their R group, as AP Biology groups them. Press one to see it in 3D."));
        foreach (var cls in BioAAClasses.All)
        {
            var members = BioAminoAcids.All.Where(a => a.Group == cls).ToList();
            var head = K.H(8, K.Dot(BioColors.Of(cls), 10), K.Text($"{cls.Label()} · {members.Count}", Ty.Caption, B.Text2).Strong());
            head.Margin = new Thickness(2, 4, 0, 0);
            Left.Children.Add(head);
            var grid = K.Columns("*, 6, *");
            for (var k = 0; k < members.Count; k++)
            {
                if (k % 2 == 0)
                {
                    if (k > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
                var tile = Tile(members[k]);
                Grid.SetRow(tile, grid.RowDefinitions.Count - 1);
                Grid.SetColumn(tile, k % 2 == 0 ? 0 : 2);
                grid.Children.Add(tile);
            }
            Left.Children.Add(grid);
        }
        Left.Children.Add(BioUI.Heading("Chirality"));
        Left.Children.Add(BiomolKit.Toggle("Show the mirror image (D-form)", mirror, "The D-form is the L-form's mirror image (z → −z). Proteins use only L-amino acids."));
        BiomolKit.OnToggle(mirror, () => syncing, on => { BiomoleculeBuilderModel.Mirrored = on; BiomolPrefs.Changed(); });
        Left.Children.Add(view);
        view.Molecule = () =>
        {
            var aa = Current;
            return (BiomolAminoAcid.Model(aa.One, BiomoleculeBuilderModel.Ph7, Mirrored), (Mirrored ? "D-" : "L-") + aa.Name.ToLowerInvariant());
        };
        view.Open = OpenInBuilder;
    }

    private static BioAminoAcid Current => BioAminoAcids.ByOne.GetValueOrDefault(BiomoleculeBuilderModel.AminoAcid) ?? BioAminoAcids.All[0];

    private static bool Mirrored => BiomoleculeBuilderModel.Mirrored && Current.IsChiral;

    /// <summary>An amino acid's tile: its R group's sketch, its name and codes.</summary>
    private FrameworkElement Tile(BioAminoAcid aa)
    {
        var wash = new Border { CornerRadius = new CornerRadius(6), Opacity = 0.14, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        wash.SetResourceReference(Border.BackgroundProperty, B.Accent);
        var mark = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        mark.SetResourceReference(Border.BackgroundProperty, B.Accent);
        var name = K.Text(aa.Name, Ty.Body);
        name.FontSize = 13;
        name.FontWeight = FontWeights.SemiBold;
        var code = K.Text($"{aa.Three} · ", Ty.Caption, B.Text2);
        var letter = new System.Windows.Documents.Run(aa.One.ToString()) { FontWeight = FontWeights.Bold, Foreground = K.Brush(K.Darkened(BioColors.Of(aa.Group))) };
        code.Inlines.Add(letter);
        var words = K.V(0, name, code);
        words.VerticalAlignment = VerticalAlignment.Center;
        var sketch = new BiomolSketchView(BiomolAminoAcid.Sketch(aa.One), 38);
        var face = K.Columns("auto, *", sketch.Margin(0, 0, 6, 0).Col(0), words.Col(1));
        face.Margin = new Thickness(6, 4, 4, 4);
        var b = K.Subtle(new Grid { Children = { wash, face, mark } }, () =>
        {
            BiomoleculeBuilderModel.AminoAcid = aa.One;
            Picked = null;
            Stage.View.Selection = null;
            BiomolPrefs.Changed();
        }, new Thickness(0), 6);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.ToolTip = $"{aa.Name} ({aa.Three}, {aa.One}) · {aa.Group.Label()}";
        System.Windows.Automation.AutomationProperties.SetName(b, $"{aa.Name}, {aa.Three}, {aa.One}, {aa.Group.Label()}");
        tiles[aa.One] = (wash, mark, b);
        return b;
    }

    protected override void Draw()
    {
        var aa = Current;
        syncing = true;
        try
        {
            foreach (var (letter, (wash, mark, button)) in tiles)
            {
                var on = letter == aa.One;
                wash.Visibility = mark.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, on ? "shown" : "");
            }
            BiomolKit.Set(mirror, Mirrored);
            mirror.IsEnabled = aa.IsChiral;
        }
        finally { syncing = false; }
        var mirrored = Mirrored;
        var ph7 = BiomoleculeBuilderModel.Ph7;
        var model = BiomolAminoAcid.Model(aa.One, ph7, mirrored);
        view.Sync("amino", model.Atoms.Count, colourHint: ColourHint());
        var look = LookFor("amino", model.Atoms.Count);
        var name = mirrored ? $"D-{aa.Name.ToLowerInvariant()}" : aa.Name;
        Title(name, mirrored ? "The mirror image (D-form): not used in proteins" : $"{aa.Three} · {aa.One} · {aa.Group.Label()} · {(ph7 ? "pH 7 form (zwitterion)" : "neutral form")}");
        Stage.View.Accessibility = $"3D model of {name}, {look.Style.Label().ToLowerInvariant()}, {(ph7 ? "as it is at pH 7" : "in its neutral form")}; parts: amino group, α-carbon, carboxyl group, R group";
        var one = aa.One;
        var chem = Chem;
        Stage.Show($"amino|{one}|{mirrored}|{look.Key}", () =>
        {
            var content = BiomolScenes.AminoAcid(one, look, mirrored, chem);
            var m = BiomolAminoAcid.Model(one, look.Ph7, mirrored);
            return new BiomolScene { Content = content, Marks = look.Labels ? BiomolScenes.ChargeMarks(m, BiomolAminoAcid.Charges(one, look.Ph7), look, chem) : new List<BiomolMark>() };
        }, $"amino|{one}");
        DrawFacts();
    }

    private static string? ColourHint() => BiomoleculeBuilderModel.ColourBy switch
    {
        BiomolColourBy.Part => "Amino group blue, α-carbon violet, carboxyl group red, R group orange.",
        BiomolColourBy.Class => "The R group in its class's colour; the backbone grey.",
        _ => null,
    };

    protected override void DrawFacts()
    {
        Right.Children.Clear();
        var aa = Current;
        var mirrored = Mirrored;
        var colour = BioColors.Of(aa.Group);
        var polarity = aa.One switch
        {
            'H' => "Basic, but mostly uncharged at cell pH (its pKa ≈ 6.0 is near 7)",
            _ => aa.Group switch
            {
                BioAAClass.Nonpolar => "Nonpolar: a hydrophobic R group",
                BioAAClass.Polar => "Polar but uncharged: its R group can H-bond with water",
                BioAAClass.Acidic => "Charged (−) at cell pH: an acidic R group",
                _ => "Charged (+) at cell pH: a basic R group",
            },
        };
        var rows = new List<UIElement?>
        {
            BioUI.FactRow("Class", $"{aa.Group.Label()} (AP Biology) · {aa.Lehninger} (Lehninger)", colour),
            BioUI.FactRow("Polarity", polarity),
            BioUI.FactRow("Side-chain pKa", aa.PKa is { } k ? $"≈ {BiomolKit.N(k, "0.00")} (can shift inside a protein)" : "None to speak of (no ionisable side chain)"),
            BioUI.FactRow("Charge at pH 7.4", $"R group {aa.ChargeText.Replace("-", "−")}. As a free amino acid (–NH₃⁺ and –COO⁻): net {BiomolText.Signed(aa.Charge)}"),
            BioUI.FactRow("Hydropathy", $"{BiomolKit.Signed(aa.Hydropathy, "0.0")} on the Kyte–Doolittle scale ({(aa.Hydropathy > 0 ? "hydrophobic" : "hydrophilic")})"),
            BioUI.FactRow("Molar mass", $"{BiomolKit.N(aa.Mass, "0.00")} g/mol free · {BiomolKit.N(aa.ResidueMass, "0.00")} g/mol in a chain (less one H₂O)"),
            BioUI.FactRow("Formula", BiomolText.Formula(aa.Formula) + " (neutral form)"),
            BioUI.FactRow("Codons", $"{string.Join(", ", aa.Codons)} ({aa.Codons.Count} codon{(aa.Codons.Count == 1 ? "" : "s")})"),
        };
        if (aa.One == 'G') rows.Add(BioUI.FactRow("Not chiral", "Its R group is just H, so its α-carbon carries two H atoms: it has no mirror image form."));
        if (aa.One == 'P') rows.Add(BioUI.FactRow("A ring", "Its R group bonds back to the backbone N, closing a ring. It has no N–H to H-bond with, so it breaks α-helices."));
        rows.Add(BioUI.FactRow("Note", aa.Note));
        Right.Children.Add(BioUI.Card(aa.Name, colour, $"{aa.Three} · {aa.One}", rows.ToArray()));

        // (the CORN rule, as this model has it)
        if (aa.IsChiral)
        {
            var clockwise = BiomolAminoAcid.CornClockwise(BiomolAminoAcid.Model(aa.One, BiomoleculeBuilderModel.Ph7, mirrored));
            var here = clockwise == true ? "In this model CO → R → N runs clockwise: the L-form." : "In this model CO → R → N runs counter-clockwise: the D-form.";
            Right.Children.Add(BioUI.Card(mirrored ? "D-form (not used in proteins)" : "L-form (as in proteins)", null, null,
                K.Wrapped("The CORN rule: look from the H on the α-carbon toward the α-carbon. In an L-amino acid the carboxyl (CO), the R group (R) and the amino group (N) run clockwise.", Ty.Body),
                K.Wrapped(here, Ty.Body, B.Text2)));
        }
        else Right.Children.Add(BioUI.Card("Achiral", null, null, K.Wrapped("Glycine has no L- or D-form: its mirror image is the same molecule.", Ty.Body)));

        if (PickedAtom(Picked) is { } id && BiomolAminoAcid.Model(aa.One, BiomoleculeBuilderModel.Ph7, mirrored) is { } m && id < m.Atoms.Count && BiomolAminoAcid.Template(aa.One) is { } t)
        {
            var a = m.Atoms[id];
            Right.Children.Insert(0, PickedCard($"{aa.Three} · {a.Name}", a.Element, BiomolAminoAcid.Part(a.Name, t).Name));
        }
        Right.Children.Add(Legend());
    }

    private static FrameworkElement Legend()
    {
        var dark = Simpl.Support.Appearance.Dark;
        IEnumerable<(Color, string)> items = BiomoleculeBuilderModel.ColourBy switch
        {
            BiomolColourBy.Part => new[] { BiomolParts.Amino, BiomolParts.Alpha, BiomolParts.Carboxyl, BiomolParts.RGroup }.Select(p => (K.Hex(p.Colour.HexFor(dark)), p.Name)),
            BiomolColourBy.Class => BioAAClasses.All.Select(c => (BioColors.Of(c), $"R group: {c.Label()}")).Append((K.Hex(BiomolParts.Grey.HexFor(dark)), "Backbone")),
            _ => new[] { "C", "H", "N", "O", "S" }.Select(s => (MBChem.ColorOf(s), $"{s} · {MBChem.Element(s)?.Name ?? s}")),
        };
        var v = K.V(8, K.Text($"Colour by {BiomoleculeBuilderModel.ColourBy.Label().ToLowerInvariant()}", Ty.Caption, B.Text2).Strong(), BioUI.Legend(items));
        if (BiomoleculeBuilderModel.Ph7) v.Children.Add(BioUI.Caption("+ and − mark the charged groups at pH 7."));
        return v;
    }
}

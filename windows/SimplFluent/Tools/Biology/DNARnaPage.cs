using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// DNA & RNA's RNA page (spec §4.2 RNA, §6.11): four views chosen at the left — an mRNA's parts as a labelled strip (cap,
// UTRs, the coding sequence from AUG to its stop with its codons read, the poly-A tail); yeast tRNA-Phe's cloverleaf
// (acceptor stem up with its 3′ CCA end on the right, D arm left, anticodon arm down, T arm right, the anticodon G34 A35
// A36 reading the codon 5′-UUC-3′ antiparallel, the real molecule's modified bases as badges, each base pressable); the
// same tRNA in 3D, its L shape from PDB 1EHZ, coloured by arm, with the 7 nm from anticodon to 3′ end; and the
// ribosomes, 70S and 80S, with their subunits and rRNAs.

internal sealed class DnaRnaPage : Grid
{
    private readonly ToolContext c;
    private readonly BioSceneView scene = new("3D model of yeast tRNA-Phe") { PickSelects = false };
    private readonly DnaCloverleafDrawing clover = new();
    private readonly DnaRibosomeDrawing ribosomes = new();
    private readonly ContentControl body = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock title = K.Text("", Ty.Subtitle);
    private readonly TextBlock foot = K.Text("", Ty.Caption, B.Text2);
    private readonly ContentControl trailing = new() { Focusable = false };
    private readonly Stack left = K.V(14), right = K.V(14);
    private string? leftKey, rightKey, sceneKey, mrnaKey;
    private FrameworkElement? sceneButtons;
    private readonly DnaToggle modifiedSwitch = new("Modified bases", v => DNAToolModel.ShowModified = v, "The real molecule’s modified bases as badges: D, Ψ, T, m¹A, yW …");

    public DnaRnaPage(ToolContext c)
    {
        this.c = c;
        scene.Picked += id => { DNAToolModel.TrnaArmSelected = id; DNAToolModel.Tell(); };
        clover.Picked += i => { DNAToolModel.TrnaSelected = i; DNAToolModel.Tell(); };
        var bar = K.Columns("*, auto", title.Col(0), trailing.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        bar.Margin = new Thickness(16);
        foot.HorizontalAlignment = HorizontalAlignment.Center;
        foot.Margin = new Thickness(12);
        foot.TextWrapping = TextWrapping.Wrap;
        foot.TextAlignment = TextAlignment.Center;
        var card = K.Card(K.Rows("auto, *, auto", bar.Row(0), body.Row(1), foot.Row(2)), new Thickness(0));
        Children.Add(BioUI.Work(left, card, right));

        void Changed() => Refresh();
        void Theme() { leftKey = rightKey = sceneKey = mrnaKey = null; Refresh(); }
        Loaded += (_, _) =>
        {
            DNAToolModel.Changed -= Changed;
            DNAToolModel.Changed += Changed;
            Appearance.Changed -= Theme;
            Appearance.Changed += Theme;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            DNAToolModel.Changed -= Changed;
            Appearance.Changed -= Theme;
        };
        Refresh();
    }

    private void Refresh()
    {
        var view = DNAToolModel.RnaView;
        var lk = $"{view}|{DNAToolModel.ShowModified}|{(view == DnaRnaView.TrnaModel ? DNAToolModel.TrnaArmSelected : "")}|{Appearance.Dark}";
        if (lk != leftKey) { leftKey = lk; Left(view); }
        switch (view)
        {
            case DnaRnaView.Mrna:
                title.Text = "mRNA";
                foot.Text = "The teaching gene’s mature mRNA (made up for this tool), 5′→3′: cap and tail not to scale";
                trailing.Content = null;
                var mk = $"mrna|{Appearance.Dark}";
                if (mk != mrnaKey || body.Content is not FrameworkElement { Tag: "mrna" }) { mrnaKey = mk; body.Content = MrnaStrip(); }
                break;
            case DnaRnaView.Cloverleaf:
                title.Text = "tRNA cloverleaf · yeast tRNA-Phe";
                foot.Text = "Press a base to read about it (← → move along the chain) · the anticodon pairs with its codon antiparallel";
                trailing.Content = null;
                clover.Modified = DNAToolModel.ShowModified;
                clover.Selected = DNAToolModel.TrnaSelected;
                body.Content = clover;
                break;
            case DnaRnaView.TrnaModel:
                title.Text = "tRNA in 3D · its L shape";
                foot.Text = "Drag to turn · scroll to zoom · press an arm · from PDB 1EHZ, its 76 phosphates";
                trailing.Content = sceneButtons ??= BioSceneCard.Buttons(scene);
                body.Content = scene;
                var key = $"trna3d|{Appearance.Dark}";
                if (key != sceneKey)
                {
                    sceneKey = key;
                    var dark = Appearance.Dark;
                    scene.Show(key, () => DnaTrna.Model(dark), "trna3d");
                }
                scene.Selection = DNAToolModel.TrnaArmSelected;
                scene.Accessibility = "3D model of yeast tRNA-Phe: an L shape, the acceptor stem at one end and the anticodon at the other, about 7 nm apart"
                                      + (DNAToolModel.TrnaArmSelected is { } a ? $"; selected: {a.Replace("arm:", "")}" : "");
                break;
            default:
                title.Text = "Ribosomes";
                foot.Text = "Svedberg values are not additive: 30S + 50S make a 70S ribosome";
                trailing.Content = null;
                body.Content = ribosomes;
                break;
        }
        var rk = $"{view}|{DNAToolModel.TrnaSelected}|{DNAToolModel.TrnaArmSelected}|{Appearance.Dark}";
        if (rk != rightKey) { rightKey = rk; Right(view); }
    }

    // ---- the left column ----

    private void Left(DnaRnaView view)
    {
        left.Children.Clear();
        left.Children.Add(BioUI.Heading("Show"));
        foreach (var (v, sub) in new[]
                 {
                     (DnaRnaView.Mrna, "Cap, UTRs, coding sequence, tail"),
                     (DnaRnaView.Cloverleaf, "Transfer RNA in 2D"),
                     (DnaRnaView.TrnaModel, "Transfer RNA’s real shape"),
                     (DnaRnaView.Ribosomes, "rRNA, 70S and 80S"),
                 })
        {
            var x = v;
            left.Children.Add(DnaWpf.Choice(v.Label(), sub, view == v, () => DNAToolModel.RnaView = x));
        }
        if (view == DnaRnaView.Cloverleaf)
        {
            left.Children.Add(BioUI.Heading("Options"));
            left.Children.Add(modifiedSwitch.Sync(DNAToolModel.ShowModified));
            left.Children.Add(BioUI.Heading("Colours"));
            left.Children.Add(DnaWpf.BaseLegend("AUGC", strands: false));
            left.Children.Add(BioUI.Caption("Rungs are base pairs (a dotted rung: G·U, a wobble pair). The cloverleaf is the 2D map; in the cell the tRNA folds into an L (see tRNA in 3D)."));
        }
        else if (view == DnaRnaView.TrnaModel)
        {
            left.Children.Add(BioUI.Heading("Arms"));
            // (the arms as a pressable list: the model's parts for the keyboard and Narrator, each in its colour)
            foreach (var arm in DnaTrna.Arms)
            {
                var id = $"arm:{arm.Key}";
                left.Children.Add(DnaWpf.Choice(arm.Name, arm.Positions, DNAToolModel.TrnaArmSelected == id,
                    () => { DNAToolModel.TrnaArmSelected = DNAToolModel.TrnaArmSelected == id ? null : id; DNAToolModel.Tell(); }, arm.Colour.ToColor()));
            }
            left.Children.Add(DnaWpf.Choice("Anticodon to 3′ end", "≈ 7 nm", DNAToolModel.TrnaArmSelected == "ruler",
                () => { DNAToolModel.TrnaArmSelected = DNAToolModel.TrnaArmSelected == "ruler" ? null : "ruler"; DNAToolModel.Tell(); }));
            left.Children.Add(BioUI.Caption("A tube through the 76 phosphates of the real molecule (PDB 1EHZ); base pairs as rungs. The anticodon and A76 are the balls."));
        }
        else if (view == DnaRnaView.Mrna)
        {
            left.Children.Add(BioUI.Heading("Colours"));
            left.Children.Add(DnaWpf.BaseLegend("AUGC", strands: false));
            left.Children.Add(BioUI.Caption("RNA has U where DNA has T. The mRNA matches the gene’s coding strand, with U for T."));
        }
    }

    // ---- the mRNA strip ----

    private FrameworkElement MrnaStrip()
    {
        var wrap = new WrapPanel { Margin = new Thickness(16, 0, 16, 8) };
        wrap.Children.Add(End("5′"));
        foreach (var region in DnaMrna.Regions()) wrap.Children.Add(Region(region));
        wrap.Children.Add(End("3′"));
        var codons = DnaMrna.Codons();
        var protein = string.Concat(codons.Where(x => x.AminoAcid != '*').Select(x => x.AminoAcid));
        var v = K.V(16, wrap,
            K.Wrapped($"The ribosome starts at the first AUG it meets scanning from the cap — not at the mRNA’s first nucleotide — and reads codons 5′→3′ until the stop: {protein}, then the stop codon (a release factor, not a tRNA).", Ty.Body, B.Text2).Margin(16, 0, 16, 16));
        return new ScrollViewer { Content = v, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Tag = "mrna" };
    }

    private static FrameworkElement End(string s)
    {
        var t = K.Text(s, Ty.BodyStrong, B.Text2);
        t.VerticalAlignment = VerticalAlignment.Bottom;
        t.Margin = new Thickness(2, 0, 6, 12);
        return t;
    }

    private static FrameworkElement Region(DnaMrnaRegion r)
    {
        var tint = r.Kind switch { "cap" => K.Hex("#8e5cd9"), "cds" => K.Tone("blue"), "tail" => BioColors.Base('A'), _ => K.Tone("gray") };
        var head = K.Text(r.Name, Ty.Caption).Strong();
        head.Foreground = K.Brush(K.Darkened(tint));
        FrameworkElement letters;
        if (r.Kind == "cds")
        {
            var row = new WrapPanel();
            var codons = BioSeq.Codons(r.Letters);
            for (var i = 0; i < codons.Count; i++)
            {
                var cod = codons[i];
                var aa = BioCode.AminoAcid(cod) ?? '?';
                var start = i == 0;
                var stop = aa == '*';
                var name = stop ? "stop" : BioAminoAcids.ByOne.TryGetValue(aa, out var a) ? a.Three : $"{aa}";
                var t = Letters(cod);
                var under = K.Text(start ? "Met (start)" : name, Ty.Caption, B.Text2);
                under.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new Border { Child = K.V(1, t, under), Padding = new Thickness(5, 2, 5, 3), CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 3, 3) };
                if (start) cell.Background = K.Brush(K.WithAlpha(K.Tone("green"), 0.18));
                else if (stop) cell.Background = K.Brush(K.WithAlpha(K.Tone("red"), 0.16));
                cell.ToolTip = start ? "AUG: the start codon (Met)" : stop ? $"{cod}: a stop codon — read by a release factor, no amino acid" : $"{cod} → {name}";
                row.Children.Add(cell);
            }
            letters = row;
        }
        else if (r.Kind == "cap")
        {
            var t = new TextBlock { Text = r.Letters, FontFamily = BioUI.Mono, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = K.Brush(K.Darkened(tint)) };
            letters = K.V(1, t, K.Text("joined 5′–5′", Ty.Caption, B.Text2));
        }
        else
        {
            var t = Letters(r.Letters + (r.Kind == "tail" ? "…" : ""));
            // (the poly-A signal marked in the 3′ UTR)
            var at = r.Letters.IndexOf("AAUAAA", StringComparison.Ordinal);
            letters = at >= 0 ? K.V(1, t, K.Text("holds the poly-A signal AAUAAA", Ty.Caption, B.Text2)) : t;
        }
        var bar = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = K.Brush(tint), Margin = new Thickness(0, 2, 0, 4) };
        var v = K.V(2, head, bar, letters);
        var box = new Border { Child = v, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 6, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        box.Stroke(B.CardStroke);
        box.ToolTip = r.About;
        System.Windows.Automation.AutomationProperties.SetName(box, $"{r.Name}: {r.Letters}. {r.About}");
        return box;
    }

    private static TextBlock Letters(string s)
    {
        var t = new TextBlock { FontFamily = BioUI.Mono, FontSize = 15, TextWrapping = TextWrapping.Wrap };
        foreach (var ch in s)
        {
            var r = new Run($"{ch}") { FontWeight = FontWeights.SemiBold };
            if (ch is 'A' or 'C' or 'G' or 'U') r.Foreground = K.Brush(BioColors.Base(ch));
            else r.SetResourceReference(TextElement.ForegroundProperty, B.Text2);
            t.Inlines.Add(r);
        }
        return t;
    }

    // ---- the right column ----

    private void Right(DnaRnaView view)
    {
        right.Children.Clear();
        switch (view)
        {
            case DnaRnaView.Mrna:
                foreach (var r in DnaMrna.Regions())
                    right.Children.Add(BioUI.Card(r.Name, null, null, BioUI.FactRow("What it is", r.About)));
                right.Children.Add(K.Link("Transcribe and translate it in Gene Expression", () => BioTools.Open(c, "gene")).Align(HorizontalAlignment.Left));
                break;
            case DnaRnaView.Cloverleaf:
                right.Children.Add(BioUI.Facts(new (string, string, Color?)[]
                {
                    ("Nucleotides", "76", null),
                    ("Base pairs", "21 (G4·U69 a wobble pair)", null),
                    ("Anticodon", "G34 A35 A36", null),
                    ("Reads", "5′-UUC-3′ (and UUU): Phe", null),
                    ("Modified bases", $"{BioData.Trna.Modified.Count} in the real molecule", null),
                }));
                // (the base pressed first, so its card is the first thing in sight beside the cloverleaf)
                if (DNAToolModel.TrnaSelected is { } i && i is >= 1 and <= 76) right.Children.Insert(0, BaseCard(i));
                right.Children.Add(BioUI.Card("Anticodon meets codon", BioColors.Base('G'), "Antiparallel, like DNA’s strands",
                    BioUI.FactRow("Pairing", "The anticodon, written 3′-AAG-5′, pairs with the codon 5′-UUC-3′: C with G34, U with A35, U with A36."),
                    BioUI.FactRow("Wobble", "G34 can also pair with U, so this one tRNA reads UUU too — both codons mean phenylalanine."),
                    BioUI.FactRow("Charged", "The enzyme phenylalanyl-tRNA synthetase attaches phenylalanine to A76’s 3′ (or 2′) OH at the CCA end.")));
                break;
            case DnaRnaView.TrnaModel:
                var (_, _, dist) = DnaTrna.Reach();
                right.Children.Add(BioUI.Facts(new (string, string, Color?)[]
                {
                    ("Shape", "An L, about 7 nm on each arm", null),
                    ("Anticodon to 3′ end", $"≈ 7 nm ({dist:0} Å)", null),
                    ("Source", "PDB 1EHZ (yeast tRNA-Phe)", null),
                }));
                var arm = DNAToolModel.TrnaArmSelected;
                if (arm == "ruler")
                    right.Children.Add(BioUI.Card("Anticodon to 3′ end", null, $"≈ 7 nm ({dist:0} Å)",
                        BioUI.FactRow("Why so far", "The anticodon pairs with the mRNA in the ribosome’s small subunit, while the amino acid at the 3′ end reaches the large subunit, where peptide bonds are made.")));
                else if (arm != null && DnaTrna.Arms.FirstOrDefault(a => "arm:" + a.Key == arm) is { } a)
                    right.Children.Add(BioUI.Card(a.Name, a.Colour.ToColor(), a.Positions, BioUI.FactRow("What it does", a.About)));
                else
                    right.Children.Add(BioUI.Card("Press an arm", null, null, BioUI.FactRow("The L", "The acceptor stem stacks on the T arm to make one limb of the L; the anticodon arm stacks on the D arm to make the other. The D and T loops meet at the corner.")));
                break;
            default:
                right.Children.Add(BioUI.Card("rRNA and ribosomes", null, null,
                    BioUI.FactRow("Bacterial", "30S (16S rRNA + about 21 proteins) + 50S (23S + 5S rRNA + about 33 proteins) → 70S."),
                    BioUI.FactRow("Eukaryotic", "40S (18S rRNA) + 60S (28S + 5.8S + 5S rRNA) → 80S."),
                    BioUI.FactRow("Not additive", "Svedberg units measure how fast a particle settles in a centrifuge, which depends on its shape as well as its mass: a 30S and a 50S subunit make a 70S ribosome, not an 80S one."),
                    BioUI.FactRow("Organelles", "Mitochondria and chloroplasts have 70S-type ribosomes, like bacteria."),
                    BioUI.FactRow("rRNA’s job", "The ribosome’s RNA, not its proteins, joins the amino acids: the ribosome is a ribozyme.")));
                right.Children.Add(K.Link("See a ribosome at work in Gene Expression", () => BioTools.Open(c, "gene")).Align(HorizontalAlignment.Left));
                break;
        }
    }

    private static FrameworkElement BaseCard(int i)
    {
        var seq = BioData.TrnaSequence;
        var b = seq[i - 1];
        var arm = DnaTrna.ArmOf(i);
        var rows = new List<UIElement?>
        {
            BioUI.FactRow("Where", $"{arm.Name} ({arm.Positions})"),
            BioUI.FactRow("Role", Cap(DnaTrna.Role(i))),
        };
        if (DnaTrna.Partner(i) is { } j)
            rows.Add(BioUI.FactRow("Pairs with", $"{seq[j - 1]}{j}{(DnaTrna.IsWobble(i, j) ? " — a G·U wobble pair (two hydrogen bonds)" : seq[i - 1] is 'G' or 'C' ? " — G·C, three hydrogen bonds" : " — A·U, two hydrogen bonds")}"));
        else rows.Add(BioUI.FactRow("Pairs with", "Nothing: it is in a loop or a single-stranded stretch."));
        if (DnaTrna.Modification(i) is { } mod) rows.Add(BioUI.FactRow("In the real molecule", $"Modified: {mod} (written here as its parent base, {b})."));
        return BioUI.Card($"{b}{i}", BioColors.Base(b), Cap(DnaNucleotides.BaseName(b)), rows.ToArray());
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>
/// Yeast tRNA-Phe's cloverleaf (spec §4.2 layout rules; DnaTrna.Cloverleaf): the backbone, the paired bases as rungs (a
/// dotted rung for G·U), the bases lettered in their colours, the anticodon with the codon it reads under it and the
/// amino acid's place at A76, the arms named, and the real molecule's modified bases as badges when asked. A press (or
/// ← → with the keyboard focus) selects a base; Escape lets go.
/// </summary>
internal sealed class DnaCloverleafDrawing : DnaDrawing
{
    private const double S = 30, Cx = 350, Cy = 424;
    private readonly (Dictionary<int, (double X, double Y)> Bases, (double X, double Y)[] Codon, Dictionary<string, (double X, double Y)> Centres) layout = DnaTrna.Cloverleaf();
    private int? hovered;
    private bool modified;
    private int? selected;

    public event Action<int?>? Picked;

    public bool Modified { get => modified; set { if (modified == value) return; modified = value; InvalidateVisual(); } }
    public int? Selected { get => selected; set { if (selected == value) return; selected = value; InvalidateVisual(); Describe(); } }

    public DnaCloverleafDrawing()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Arrow;
        Describe();
    }

    private void Describe()
    {
        var s = "Cloverleaf of yeast tRNA-Phe, 76 nucleotides: the acceptor stem at the top with the 3′ CCA end, the D arm at the left, the anticodon arm at the bottom with the anticodon G34 A35 A36 pairing with the codon 5′-UUC-3′, the T arm at the right.";
        if (selected is { } i) s += $" Selected: {BioData.TrnaSequence[i - 1]}{i}.";
        System.Windows.Automation.AutomationProperties.SetName(this, s);
    }

    protected override Size Design => new(760, 790);

    private static Point P((double X, double Y) u) => new(Cx + u.X * S, Cy - u.Y * S);

    private int? BaseAt(Point viewPoint)
    {
        var d = ToDesign(viewPoint);
        int? best = null;
        var bestDist = 15.0;
        foreach (var (i, u) in layout.Bases)
        {
            var dist = (P(u) - d).Length;
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = BaseAt(e.GetPosition(this));
        if (h == hovered) return;
        hovered = h;
        Cursor = h != null ? Cursors.Hand : Cursors.Arrow;
        ToolTip = h is { } i ? $"{BioData.TrnaSequence[i - 1]}{i} · {DnaTrna.ArmOf(i).Name}" : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (hovered == null) return;
        hovered = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var i = BaseAt(e.GetPosition(this));
        Picked?.Invoke(i == selected ? null : i);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int? next = e.Key switch
        {
            Key.Right or Key.Down => Math.Min(76, (selected ?? 0) + 1),
            Key.Left or Key.Up => Math.Max(1, (selected ?? 2) - 1),
            Key.Home => 1,
            Key.End => 76,
            _ => null,
        };
        if (e.Key == Key.Escape) { Picked?.Invoke(null); e.Handled = true; return; }
        if (next == null) return;
        Picked?.Invoke(next);
        e.Handled = true;
    }

    protected override void Draw(DrawingContext dc)
    {
        var seq = BioData.TrnaSequence;
        var b = layout.Bases;
        var codon = layout.Codon;
        var text = TextColour;
        var text2 = Text2Colour;
        var accent = Appearance.Accent;
        const double r = DnaTrna.BaseRadius * S;

        // (the backbone, 5′→3′)
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(P(b[1]), false, false);
            for (var i = 2; i <= 76; i++) g.LineTo(P(b[i]), true, true);
        }
        geo.Freeze();
        dc.DrawGeometry(null, PenOf(LineColour, 2), geo);

        // (the anticodon, lit as one band behind its three bases)
        var band = new StreamGeometry();
        using (var g = band.Open())
        {
            g.BeginFigure(P(b[34]), false, false);
            g.LineTo(P(b[35]), true, true);
            g.LineTo(P(b[36]), true, true);
        }
        band.Freeze();
        var bandPen = new Pen(K.Brush(K.WithAlpha(accent, Dark ? 0.38 : 0.26)), 2 * r + 12) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        bandPen.Freeze();
        dc.DrawGeometry(null, bandPen, band);

        // (the codon's strand under the anticodon: written 3′ … 5′ left to right, so it pairs antiparallel)
        Point c0 = P(codon[0]), c2 = P(codon[2]);
        dc.DrawLine(PenOf(BioPalette.Rna.ToColor(Dark), 2.5), new Point(c0.X - 46, c0.Y), new Point(c2.X + 46, c2.Y));
        Say(dc, "…", c0.X - 58, c0.Y, 14, text2);
        Say(dc, "…", c2.X + 58, c2.Y, 14, text2);
        Say(dc, "3′", c0.X - 80, c0.Y, 14.5, text, strong: true);
        Say(dc, "5′", c2.X + 80, c2.Y, 14.5, text, strong: true);
        Say(dc, "mRNA codon 5′-UUC-3′ (here read right to left) → Phe", (c0.X + c2.X) / 2, c0.Y + 30, 14, text, strong: true);

        // (the rungs: a pair's bases joined; a dotted rung for G·U)
        foreach (var (i, j) in BioData.Trna.Pairs)
        {
            Point pi = P(b[i]), pj = P(b[j]);
            var v = pj - pi;
            v.Normalize();
            var wobble = DnaTrna.IsWobble(i, j);
            dc.DrawLine(PenOf(text2, wobble ? 2 : 2.6, dashed: wobble), pi + v * r, pj - v * r);
        }
        for (var k = 0; k < 3; k++)
        {
            Point pa = P(b[DnaTrna.Codon[k].Pairs]), pc = P(codon[k]);
            var v = pc - pa;
            v.Normalize();
            dc.DrawLine(PenOf(text2, 2.6), pa + v * r, pc - v * r);
        }

        // (the amino acid's place: Phe on A76's 3′ OH, when the tRNA is charged)
        var a76 = P(b[76]);
        var phe = new Point(a76.X + r + 40, a76.Y);
        dc.DrawLine(PenOf(text2, 2, dashed: true), a76 + new Vector(r + 4, 0), phe - new Vector(20, 0));
        dc.DrawRoundedRectangle(K.Brush(K.WithAlpha(BioPalette.Nonpolar.ToColor(Dark), 0.9)), null, new Rect(phe.X - 20, phe.Y - 12, 40, 24), 12, 12);
        Say(dc, "Phe", phe.X, phe.Y, 13, Colors.White, strong: true);
        Say(dc, "joins A76’s 3′ end when charged", phe.X + 28, phe.Y, 13, text2, align: 0);
        dc.DrawEllipse(null, PenOf(accent, 2.5), a76, r + 4, r + 4);

        // (the bases, lettered in their colours; the codon's too)
        var ink = Color.FromRgb(0x1c, 0x1c, 0x1e);
        for (var i = 1; i <= 76; i++)
        {
            var p = P(b[i]);
            var ch = seq[i - 1];
            var col = Base(ch);
            dc.DrawEllipse(K.Brush(col), null, p, r, r);
            if (hovered == i) dc.DrawEllipse(null, PenOf(text, 1.5), p, r + 2, r + 2);
            Say(dc, $"{ch}", p.X, p.Y, 14.5, DnaWpf.WhiteOn(BioColors.Hex(col)) ? Colors.White : ink, strong: true, mono: true);
        }
        for (var k = 0; k < 3; k++)
        {
            var p = P(codon[k]);
            var ch = DnaTrna.Codon[k].Base;
            var col = Base(ch);
            dc.DrawEllipse(K.Brush(col), null, p, r, r);
            Say(dc, $"{ch}", p.X, p.Y, 14.5, DnaWpf.WhiteOn(BioColors.Hex(col)) ? Colors.White : ink, strong: true, mono: true);
        }
        if (selected is { } s && b.TryGetValue(s, out var su))
        {
            dc.DrawEllipse(null, PenOf(accent, 3), P(su), r + 4, r + 4);
            if (DnaTrna.Partner(s) is { } partner) dc.DrawEllipse(null, PenOf(accent, 1.6, dashed: true), P(b[partner]), r + 4, r + 4);
        }

        // (the ends, and the arms' names)
        var p1 = P(b[1]);
        Say(dc, "5′", p1.X - 30, p1.Y, 14.5, text, strong: true);
        Say(dc, "3′", a76.X, a76.Y - 30, 14.5, text, strong: true);
        Say(dc, "Acceptor stem", P(b[4]).X - 30, P(b[4]).Y, 14.5, text, align: 1, strong: true);
        var d = layout.Centres["d"];
        Say(dc, "D arm", P(d).X, P(d).Y - 66, 14.5, text, strong: true);
        // (the T arm named over its stem, clear of its loop's numbers and badges)
        Say(dc, "T arm (TΨC)", (P(b[61]).X + P(b[65]).X) / 2, P(b[63]).Y - r - 15, 14.5, text, strong: true);
        Say(dc, "Anticodon arm", P(b[29]).X - 30, P(b[29]).Y, 14.5, text, align: 1, strong: true);
        var p36 = P(b[36]);
        Say(dc, "Anticodon", p36.X + r + 10, p36.Y + 22, 14.5, K.Darkened(accent), align: 0, strong: true);
        var v47 = P(b[47]);
        Say(dc, "Variable loop", v47.X + r + 10, v47.Y, 14.5, text, align: 0, strong: true);

        // (numbers every ten, and the real molecule's modified bases as badges — a badge carries its base's number)
        var mods = modified ? BioData.Trna.Modified.Where(m => b.ContainsKey(m.Position)).ToList() : new List<(string Label, int Position)>();
        foreach (var i in DnaTrna.Numbered)
        {
            if (mods.Any(m => m.Position == i)) continue;
            var at = P(DnaTrna.Beside(i, b, codon, DnaTrna.NumberDistance, DnaTrna.NumberClearance));
            Say(dc, $"{i}", at.X, at.Y, 12, text2);
        }
        foreach (var (label, pos) in mods)
        {
            var at = P(DnaTrna.Beside(pos, b, codon, DnaTrna.BadgeDistance, DnaTrna.BadgeClearance));
            var words = Words(DnaTrna.Numbered.Contains(pos) ? $"{label}{pos}" : label, 11.5, Colors.White, strong: true);
            var w = Math.Max(18, words.Width + 10);
            dc.DrawRoundedRectangle(K.Brush(Color.FromRgb(0x8e, 0x5c, 0xd9)), null, new Rect(at.X - w / 2, at.Y - 9, w, 18), 9, 9);
            dc.DrawText(words, new Point(at.X - words.Width / 2, at.Y - words.Height / 2));
        }
    }
}

/// <summary>The ribosomes (spec §4.2): bacterial 70S and eukaryotic 80S, each its large subunit over its small one, their
/// rRNAs named, and the sum that is not one.</summary>
internal sealed class DnaRibosomeDrawing : DnaDrawing
{
    public DnaRibosomeDrawing()
    {
        System.Windows.Automation.AutomationProperties.SetName(this,
            "Ribosomes. Bacterial 70S: a 50S subunit (23S and 5S rRNA, about 33 proteins) over a 30S subunit (16S rRNA, about 21 proteins). Eukaryotic 80S: a 60S subunit (28S, 5.8S and 5S rRNA) over a 40S subunit (18S rRNA).");
    }

    protected override Size Design => new(720, 520);

    protected override void Draw(DrawingContext dc)
    {
        One(dc, DnaRibosomes.Bacterial, 190, 0.86, K.Tone("blue"), K.Tone("green"));
        One(dc, DnaRibosomes.Eukaryotic, 530, 1.0, K.Tone("purple"), K.Tone("orange"));
        Say(dc, "30S + 50S → 70S, not 80S: Svedberg units are not additive", 360, 490, 14, TextColour, strong: true);
    }

    private void One(DrawingContext dc, DnaRibosomes.Ribosome r, double cx, double scale, Color large, Color small)
    {
        var text = TextColour;
        var text2 = Text2Colour;
        Say(dc, r.Name, cx, 40, 26, text, strong: true);
        Say(dc, r.Where, cx, 72, 12, text2);
        var ly = 190;
        var sy = 300;
        dc.DrawEllipse(K.Brush(K.WithAlpha(large, Dark ? 0.55 : 0.4)), PenOf(large, 2), new Point(cx, ly), 120 * scale, 78 * scale);
        dc.DrawEllipse(K.Brush(K.WithAlpha(small, Dark ? 0.55 : 0.4)), PenOf(small, 2), new Point(cx, sy + 4), 104 * scale, 52 * scale);
        // (the mRNA threading between the subunits)
        dc.DrawLine(PenOf(BioPalette.Rna.ToColor(Dark), 3), new Point(cx - 150, sy - 34), new Point(cx + 150, sy - 34));
        Say(dc, "mRNA", cx + 150, sy - 48, 11, text2, align: 1);
        Say(dc, r.Large.Name, cx, ly - 18, 22, text, strong: true);
        Say(dc, r.Large.Rna, cx, ly + 10, 13, text);
        if (r.Large.Proteins.Length > 0) Say(dc, r.Large.Proteins, cx, ly + 30, 12, text2);
        Say(dc, r.Small.Name, cx, sy + 2, 20, text, strong: true);
        Say(dc, r.Small.Rna, cx, sy + 26, 13, text);
        if (r.Small.Proteins.Length > 0) Say(dc, r.Small.Proteins, cx, 380, 12, text2);
        Say(dc, $"{r.Small.Name} + {r.Large.Name} → {r.Name}", cx, 420, 15, text, strong: true);
    }
}

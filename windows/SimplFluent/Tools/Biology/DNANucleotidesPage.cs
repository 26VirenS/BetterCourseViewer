using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// DNA & RNA's Nucleotides page (spec §4.2 Nucleotides): the four DNA nucleotides and the four RNA ones side by side,
// and ATP, in 3D from their CCD coordinates — coloured by part (phosphate, sugar, base) or by element, each part named
// with a leader line, the sugar's carbons numbered 1′–5′ and the base's ring atoms 1–9, and the 2′ position and the
// 5-methyl marked to tell DNA from RNA — and, as the page's other view, the purines and pyrimidines and the two
// Watson–Crick pairs drawn flat with their H-bonds between the exact atoms (§6.5). A press on a part says what it is.

internal sealed class DnaNucleotidesPage : Grid
{
    private readonly BioSceneView scene = new("3D model of a nucleotide") { PickSelects = false };
    private readonly DnaPairsDrawing pairs = new();
    private readonly ContentControl body = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock title = K.Text("", Ty.Subtitle);
    // (the nucleotide's name under its code: on one line with the view's switch it was cut short)
    private readonly TextBlock subtitle = K.Text("", Ty.Body, B.Text2);
    private readonly TextBlock foot = K.Text("", Ty.Caption, B.Text2);
    private readonly ContentControl buttons = new() { Focusable = false };
    private readonly SelectorBar viewBar;
    private readonly Stack left = K.V(14), right = K.V(14);
    private string? shownOptions, leftKey, rightKey;
    private int generation;
    private FrameworkElement? sceneButtons;
    // (the switches, made once: the left column is laid out afresh, but a switch is never replaced under the pointer)
    private readonly DnaToggle uracilSwitch = new("A·U (RNA) instead of A·T", v => DNAToolModel.PairWithUracil = v, "U pairs with A just as T does: U is T without its 5-methyl group");
    private readonly DnaToggle partsSwitch = new("Colour by part", v => DNAToolModel.ShowParts = v, "Phosphate, sugar and base in their own colours; off: the elements’ colours");
    private readonly DnaToggle numbersSwitch = new("Number the carbons", v => DNAToolModel.ShowNumbers = v, "1′–5′ on the sugar, 1–9 on the base’s rings");
    private readonly DnaToggle differencesSwitch = new("DNA vs RNA marks", v => DNAToolModel.ShowDifferences = v, "The 2′ position, and thymine’s 5-methyl");
    private readonly DnaToggle hydrogensSwitch = new("Hydrogen atoms", v => DNAToolModel.ShowHydrogens = v);
    private readonly DnaToggle ph7Switch = new("Phosphate as at pH 7", v => DNAToolModel.AtPh7 = v, "Its acidic H atoms gone, as in the cell");

    public DnaNucleotidesPage()
    {
        viewBar = new SelectorBar(new[] { "3D", "Base pairs (2D)" }, DNAToolModel.ShowPairs ? 1 : 0);
        viewBar.Changed += i => DNAToolModel.ShowPairs = i == 1;
        scene.Picked += Pick;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        var words = K.V(2, title, subtitle);
        words.VerticalAlignment = VerticalAlignment.Center;
        var bar = K.Columns("*, auto, auto", words.Col(0), viewBar.Margin(8, 0, 8, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1), buttons.Col(2));
        bar.Margin = new Thickness(16);
        foot.HorizontalAlignment = HorizontalAlignment.Center;
        foot.Margin = new Thickness(12);
        var card = K.Card(K.Rows("auto, *, auto", bar.Row(0), body.Row(1), foot.Row(2)), new Thickness(0));
        Children.Add(BioUI.Work(left, card, right));

        void Changed() => Refresh();
        void Theme() { shownOptions = leftKey = rightKey = null; pairs.InvalidateVisual(); Refresh(); }
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

    private static void Pick(string? id)
    {
        string? part = id switch
        {
            null => null,
            "part:glycosidic" => "glycosidic",
            "diff:2" or "diff:5" => id,
            _ when id.StartsWith("part:", StringComparison.Ordinal) => id.Split(':')[1],
            _ => null,
        };
        DNAToolModel.NucleotidePart = part;
        DNAToolModel.Tell();
    }

    private void Refresh()
    {
        var code = DNAToolModel.Nucleotide;
        viewBar.Select(DNAToolModel.ShowPairs ? 1 : 0, false);
        var lk = $"{code}|{DNAToolModel.ShowParts}|{DNAToolModel.ShowNumbers}|{DNAToolModel.ShowDifferences}|{DNAToolModel.ShowHydrogens}|{DNAToolModel.AtPh7}|{DNAToolModel.ShowPairs}|{DNAToolModel.PairWithUracil}|{Appearance.Dark}";
        if (lk != leftKey) { leftKey = lk; Left(); }
        if (DNAToolModel.ShowPairs)
        {
            title.Text = "Purines and pyrimidines";
            subtitle.Text = DNAToolModel.PairWithUracil ? "A·U and G·C, drawn flat" : "A·T and G·C, drawn flat";
            foot.Text = "C1′ to C1′ is 10.9 Å in both pairs: a purine with a pyrimidine is always the same width";
            pairs.Uracil = DNAToolModel.PairWithUracil;
            body.Content = pairs;
            buttons.Content = null;
        }
        else
        {
            var mol = BioData.Molecule(code);
            title.Text = DnaNucleotides.Short(code);
            subtitle.Text = DnaNucleotides.Name(code) + (mol?.Formula is { } formula ? " · " + BiomolText.Formula(formula) : "");
            foot.Text = "Drag to turn · scroll to zoom · press a part to learn about it";
            body.Content = scene;
            buttons.Content = sceneButtons ??= BioSceneCard.Buttons(scene);
            var o = new DnaNucleotideOptions(code, DNAToolModel.ShowParts, DNAToolModel.ShowNumbers, DNAToolModel.ShowDifferences, DNAToolModel.ShowHydrogens,
                DNAToolModel.AtPh7, Appearance.Dark);
            if (o.Key != shownOptions)
            {
                shownOptions = o.Key;
                generation++;
                var paint = DnaWpf.Paint();
                List<DnaLetter>? numbers = null;
                scene.Show($"nt|{o.Key}|{generation}", () =>
                {
                    var (content, nums) = DnaNucleotides.Build(o, paint);
                    numbers = nums;
                    return content;
                }, frameKey: $"nt|{code}", billboards: () => DnaWpf.Billboards(numbers, o.Dark));
            }
            scene.Selection = DNAToolModel.NucleotidePart switch
            {
                null => null,
                "glycosidic" => "part:glycosidic",
                "diff:2" or "diff:5" => DNAToolModel.NucleotidePart,
                var p => $"part:{p}",
            };
            scene.Accessibility = $"3D model of {DnaNucleotides.Name(code).ToLowerInvariant()} ({mol?.Formula ?? ""}): a phosphate group, "
                                  + $"{(DnaNucleotides.IsRibose(code) ? "ribose" : "deoxyribose")} and {DnaNucleotides.BaseName(DnaNucleotides.Base(code))}"
                                  + (DNAToolModel.NucleotidePart is { } sel ? $"; selected: {sel}" : "");
        }
        var rk = $"{code}|{DNAToolModel.ShowPairs}|{DNAToolModel.NucleotidePart}|{Appearance.Dark}";
        if (rk != rightKey) { rightKey = rk; Right(); }
    }

    // ---- the left column ----

    private void Left()
    {
        left.Children.Clear();
        left.Children.Add(BioUI.Heading("Nucleotide"));
        FrameworkElement Column(string head, string[] codes)
        {
            var v = K.V(6, K.Text(head, Ty.Caption, B.Text2).Strong());
            foreach (var code in codes)
            {
                var b = DnaNucleotides.Base(code);
                var x = code;
                v.Children.Add(DnaWpf.Choice(DnaNucleotides.Short(code), DnaNucleotides.BaseName(b), DNAToolModel.Nucleotide == code && !DNAToolModel.ShowPairs,
                    () => { DNAToolModel.ShowPairs = false; DNAToolModel.Nucleotide = x; DNAToolModel.Tell(); }, BioColors.Base(b)));
            }
            return v;
        }
        left.Children.Add(K.Columns("*, 8, *", Column("DNA", DnaNucleotides.DnaCodes).Col(0), Column("RNA", DnaNucleotides.RnaCodes).Col(2)));
        left.Children.Add(DnaWpf.Choice("ATP", "adenosine triphosphate, the energy carrier", DNAToolModel.Nucleotide == "ATP" && !DNAToolModel.ShowPairs,
            () => { DNAToolModel.ShowPairs = false; DNAToolModel.Nucleotide = "ATP"; DNAToolModel.Tell(); }, BioColors.Base('A')));
        left.Children.Add(BioUI.Heading("Show"));
        if (DNAToolModel.ShowPairs)
        {
            left.Children.Add(uracilSwitch.Sync(DNAToolModel.PairWithUracil));
            left.Children.Add(BioUI.Caption("Purines (A and G) have two rings; pyrimidines (C, T and U) have one. Each pair is a purine with a pyrimidine, held by hydrogen bonds between the exact atoms drawn: two in A·T, three in G·C."));
        }
        else
        {
            left.Children.Add(partsSwitch.Sync(DNAToolModel.ShowParts));
            left.Children.Add(numbersSwitch.Sync(DNAToolModel.ShowNumbers));
            left.Children.Add(differencesSwitch.Sync(DNAToolModel.ShowDifferences));
            left.Children.Add(hydrogensSwitch.Sync(DNAToolModel.ShowHydrogens));
            left.Children.Add(ph7Switch.Sync(DNAToolModel.AtPh7));
            left.Children.Add(BioUI.Heading("Colours"));
            var code = DNAToolModel.Nucleotide;
            if (DNAToolModel.ShowParts)
                left.Children.Add(BioUI.Legend(new[]
                {
                    (DnaNucleotides.PartColour(DnaPart.Phosphate, code).ToColor(), code == "ATP" ? "Phosphate groups" : "Phosphate group"),
                    (DnaNucleotides.PartColour(DnaPart.Sugar, code).ToColor(), DnaNucleotides.IsRibose(code) ? "Ribose (sugar)" : "Deoxyribose (sugar)"),
                    (DnaNucleotides.PartColour(DnaPart.Base, code).ToColor(), $"Base: {DnaNucleotides.BaseName(DnaNucleotides.Base(code))}"),
                }));
            else
                left.Children.Add(BioUI.Legend(new[] { "C", "N", "O", "P", "H" }.Select(e => (K.Hex(BioMoleculeScene.CpkHex(e)), $"{e} · {MBChem.Element(e)?.Name ?? e}"))));
        }
    }

    // ---- the right column ----

    private void Right()
    {
        right.Children.Clear();
        var code = DNAToolModel.Nucleotide;
        var b = DnaNucleotides.Base(code);
        var mol = BioData.Molecule(code);
        if (!DNAToolModel.ShowPairs)
        {
            right.Children.Add(BioUI.Facts(new (string, string, Color?)[]
            {
                ("Formula", mol?.Formula is { } f ? BiomolText.Formula(f) : "—", null),
                ("Molar mass", mol?.Mass is { } m ? $"{m:0.00} g/mol" : "—", null),
                ("Sugar", DnaNucleotides.IsRibose(code) ? "Ribose (2′–OH)" : "Deoxyribose (2′–H)", null),
                ("Base", $"{Cap(DnaNucleotides.BaseName(b))}, a {(DnaNucleotides.IsPurine(b) ? "purine" : "pyrimidine")}", BioColors.Base(b)),
                ("Found in", code == "ATP" ? "Every cell, as its energy carrier (and RNA’s A)" : DnaNucleotides.IsRibose(code) ? "RNA" : "DNA", null),
            }));
            // (the parts as a list too: the 3D view's parts for the keyboard and Narrator)
            var list = K.V(6);
            foreach (var (key, name) in new[]
                     {
                         ("phosphate", code == "ATP" ? "Phosphate groups" : "Phosphate group"), ("sugar", DnaNucleotides.IsRibose(code) ? "Ribose" : "Deoxyribose"),
                         ("base", $"Base: {DnaNucleotides.BaseName(b)}"), ("glycosidic", "Glycosidic bond"),
                     })
            {
                var k = key;
                var swatch = key switch
                {
                    "phosphate" => DnaNucleotides.PartColour(DnaPart.Phosphate, code).ToColor(),
                    "sugar" => DnaNucleotides.PartColour(DnaPart.Sugar, code).ToColor(),
                    "base" => DnaNucleotides.PartColour(DnaPart.Base, code).ToColor(),
                    _ => (Color?)null,
                };
                list.Children.Add(DnaWpf.Choice(name, null, DNAToolModel.NucleotidePart == key,
                    () => { DNAToolModel.NucleotidePart = DNAToolModel.NucleotidePart == k ? null : k; DNAToolModel.Tell(); }, swatch));
            }
            right.Children.Add(BioUI.Card("Parts", null, null, list));
            if (DNAToolModel.NucleotidePart is { } part) right.Children.Add(PartCard(part, code));
        }
        right.Children.Add(BioUI.Card("DNA vs RNA", null, null,
            BioUI.FactRow("Sugar", "DNA: deoxyribose — its 2′ carbon has only H. RNA: ribose — its 2′ carbon has an OH. The difference is at 2′, not 3′."),
            BioUI.FactRow("Bases", "DNA has T; RNA has U in its place. U is T without the methyl group on carbon 5."),
            BioUI.FactRow("Strands", "DNA is usually double-stranded (a double helix); RNA is usually single-stranded, folding on itself (as tRNA does)."),
            BioUI.FactRow("Stability", "DNA lasts: with no 2′–OH, its backbone resists breaking. RNA’s 2′–OH can attack its own backbone, so RNA is shorter-lived — fine for a working copy.")));
        if (DNAToolModel.ShowPairs)
        {
            right.Children.Add(BioUI.Card("Purines and pyrimidines", null, null,
                BioUI.FactRow("Purines: two rings", "Adenine (A) and guanine (G): a six-membered ring fused to a five-membered one, numbered 1–9."),
                BioUI.FactRow("Pyrimidines: one ring", "Cytosine (C), thymine (T) and uracil (U): one six-membered ring, numbered 1–6."),
                BioUI.FactRow("Pairs", "A·T (or A·U in RNA) by two hydrogen bonds; G·C by three. In RNA, G can also pair with U by two (a ‘wobble’ pair).")));
        }
        else
        {
            var go = K.Link("See the base pairs in 2D", () => { DNAToolModel.ShowPairs = true; DNAToolModel.Tell(); });
            right.Children.Add(BioUI.Card("Purines and pyrimidines", null, $"{Cap(DnaNucleotides.BaseName(b))} is a {(DnaNucleotides.IsPurine(b) ? "purine: two rings" : "pyrimidine: one ring")}",
                BioUI.FactRow("Why it matters", "Each base pair is one purine and one pyrimidine, so every pair is the same width."), go));
        }
    }

    private static FrameworkElement PartCard(string part, string code)
    {
        var b = DnaNucleotides.Base(code);
        var ribose = DnaNucleotides.IsRibose(code);
        return part switch
        {
            "phosphate" => BioUI.Card(code == "ATP" ? "Three phosphate groups" : "Phosphate group", DnaNucleotides.PartColour(DnaPart.Phosphate, code).ToColor(), null,
                BioUI.FactRow("What it does", code == "ATP"
                    ? "ATP’s three phosphates (α, β and γ) are joined by phosphoanhydride bonds. Breaking off the last one releases the energy cells spend. In a strand, a nucleotide keeps only its α phosphate."
                    : "In a strand it links this sugar’s 5′ carbon to the 3′ carbon of the sugar before it — a phosphodiester bond — making the backbone."),
                BioUI.FactRow("Charge", code == "ATP" ? "About −4 at pH 7." : "−2 at pH 7 as a single nucleotide; −1 for each link in a strand, which makes DNA an acid and negatively charged.")),
            "sugar" => BioUI.Card(ribose ? "Ribose" : "Deoxyribose", DnaNucleotides.PartColour(DnaPart.Sugar, code).ToColor(), "A five-carbon sugar",
                BioUI.FactRow("Its carbons", "Numbered 1′ to 5′ (with primes, to tell them from the base’s atoms). The base joins C1′, the phosphate joins C5′, and the next nucleotide joins at C3′ — so a strand has a 5′ end and a 3′ end."),
                BioUI.FactRow("The 2′ position", ribose ? "Ribose has an OH on its 2′ carbon." : "Deoxyribose has only H on its 2′ carbon: “deoxy” means an oxygen missing.")),
            "base" => BioUI.Card(Cap(DnaNucleotides.BaseName(b)), BioColors.Base(b), DnaNucleotides.IsPurine(b) ? "A purine: two rings" : "A pyrimidine: one ring",
                BioUI.FactRow("Pairs with", b switch
                {
                    'A' => "T in DNA, U in RNA, by two hydrogen bonds.",
                    'T' => "A, by two hydrogen bonds. T is found only in DNA.",
                    'U' => "A, by two hydrogen bonds (and G in a wobble pair). U is found only in RNA.",
                    'G' => "C, by three hydrogen bonds.",
                    _ => "G, by three hydrogen bonds.",
                }),
                BioUI.FactRow("Its ring atoms", DnaNucleotides.IsPurine(b) ? "Numbered 1–9 round its two rings." : "Numbered 1–6 round its ring.")),
            "glycosidic" => BioUI.Card("Glycosidic bond", null, $"C1′–{DnaNucleotides.GlycosidicN(b)}",
                BioUI.FactRow("What it joins", $"The sugar’s 1′ carbon to the base’s {DnaNucleotides.GlycosidicN(b)} ({(DnaNucleotides.IsPurine(b) ? "N9 in a purine" : "N1 in a pyrimidine")}).")),
            "diff:2" => BioUI.Card("The 2′ position", null, ribose ? "OH: ribose (RNA)" : "H only: deoxyribose (DNA)",
                BioUI.FactRow("DNA vs RNA", "The one difference between the two sugars: ribose has an OH on its 2′ carbon, deoxyribose does not.")),
            _ => BioUI.Card(b == 'T' ? "The 5-methyl group" : "No methyl on carbon 5", null, b == 'T' ? "Thymine (DNA)" : "Uracil (RNA)",
                BioUI.FactRow("T vs U", "Thymine is 5-methyluracil: the same ring with a CH₃ on carbon 5. RNA uses uracil in its place.")),
        };
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>
/// The Watson–Crick pairs drawn flat (spec §4.2 Purines vs pyrimidines; §6.7's pairs projected onto their plane): A·T (or
/// A·U) over G·C, skeletal — the rings tinted their base's colour, N and O lettered, the H atoms that bond shown — the
/// H-bonds dashed from each donor's H to its acceptor with their lengths, the sugars' C1′ marked, the major groove's side up.
/// </summary>
internal sealed class DnaPairsDrawing : DnaDrawing
{
    private bool uracil;

    public bool Uracil
    {
        get => uracil;
        set { if (uracil == value) return; uracil = value; InvalidateVisual(); Describe(); }
    }

    public DnaPairsDrawing()
    {
        Describe();
    }

    private void Describe()
    {
        string Bonds(DnaPairDrawing d) => string.Join("; ", d.HBonds.Select(h => $"{d.Atoms[h.Donor].Name} to {d.Atoms[h.Acceptor].Name}, {h.Distance:0.00} ångströms"));
        DnaPairDrawing a = DnaNucleotides.Pair2D(uracil ? "AU" : "AT"), g = DnaNucleotides.Pair2D("GC");
        System.Windows.Automation.AutomationProperties.SetName(this,
            $"Base pairs drawn flat. {a.Name}: {a.HBonds.Count} hydrogen bonds ({Bonds(a)}). G·C: 3 hydrogen bonds ({Bonds(g)}). Purines A and G have two rings; pyrimidines have one.");
    }

    protected override Size Design => new(640, 700);

    private const double Scale = 31;

    protected override void Draw(DrawingContext dc)
    {
        Panel(dc, DnaNucleotides.Pair2D(uracil ? "AU" : "AT"), 0);
        Panel(dc, DnaNucleotides.Pair2D("GC"), 350);
    }

    private void Panel(DrawingContext dc, DnaPairDrawing d, double top)
    {
        var text = TextColour;
        var text2 = Text2Colour;
        var cx = 320.0;
        // (the pair's y runs from −1.7 Å, the minor groove's side, to 5.5 Å, the major's: drawn under its two lines of words)
        var oy = top + 56 + 5.55 * Scale;
        Point P(int i) => new(cx + d.Atoms[i].X * Scale, oy - d.Atoms[i].Y * Scale);
        var count = d.HBonds.Count;
        Say(dc, $"{d.Name} pair — {count} hydrogen bonds", cx, top + 18, 17, text, strong: true);
        Say(dc, "major groove side", cx, top + 40, 11, text2);
        Say(dc, "minor groove side", cx, oy + 1.75 * Scale + 16, 11, text2);

        // (the rings, tinted their base's colour)
        foreach (var (b, ring) in d.Rings)
        {
            if (ring.Any(i => i < 0)) continue;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(ring[0]), true, true);
                foreach (var i in ring.Skip(1)) g.LineTo(P(i), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(K.Brush(K.WithAlpha(Base(b), Dark ? 0.30 : 0.20)), null, geo);
        }

        // (bonds, trimmed where a letter stands)
        bool Lettered(int i) => d.Atoms[i].Element != "C" || d.Atoms[i].Name == "C1'";
        double Trim(int i) => d.Atoms[i].Element == "H" ? 6 : Lettered(i) ? 8.5 : 0;
        var bondPen = PenOf(text, 2.1);
        var thin = PenOf(text, 1.5);
        foreach (var (a, b, order) in d.Bonds)
        {
            Point pa = P(a), pb = P(b);
            var v = pb - pa;
            var len = v.Length;
            if (len < 1) continue;
            v /= len;
            var s = pa + v * Trim(a);
            var e = pb - v * Trim(b);
            if (order >= 2)
            {
                var nrm = new Vector(-v.Y, v.X) * 2.4;
                dc.DrawLine(thin, s + nrm, e + nrm);
                dc.DrawLine(thin, s - nrm, e - nrm);
            }
            else dc.DrawLine(d.Atoms[a].Element == "H" || d.Atoms[b].Element == "H" ? thin : bondPen, s, e);
        }

        // (the H-bonds: dashed from the donor's H to the acceptor, with the donor–acceptor distance)
        var hPen = PenOf(BioPalette.HBond.ToColor(Dark), 2.2, dashed: true);
        foreach (var (_, h, acceptor, distance) in d.HBonds)
        {
            Point ph = P(h), pa = P(acceptor);
            var v = pa - ph;
            v.Normalize();
            dc.DrawLine(hPen, ph + v * 6, pa - v * 9);
            var mid = new Point((ph.X + pa.X) / 2, (ph.Y + pa.Y) / 2);
            Say(dc, $"{distance:0.00} Å", mid.X, mid.Y - 11, 11, text2);
        }

        // (the letters: N blue, O red, H grey; C1′ where each sugar joins)
        var nColour = Dark ? Color.FromRgb(0x8a, 0xa4, 0xff) : Color.FromRgb(0x30, 0x50, 0xd8);
        var oColour = Dark ? Color.FromRgb(0xff, 0x7a, 0x6b) : Color.FromRgb(0xd0, 0x30, 0x20);
        for (var i = 0; i < d.Atoms.Count; i++)
        {
            var a = d.Atoms[i];
            var p = P(i);
            if (a.Name == "C1'") { Say(dc, "C1′", p.X, p.Y, 12, text2, strong: true); continue; }
            switch (a.Element)
            {
                case "N": Say(dc, "N", p.X, p.Y, 15, nColour, strong: true); break;
                case "O": Say(dc, "O", p.X, p.Y, 15, oColour, strong: true); break;
                case "H": Say(dc, "H", p.X, p.Y, 11, text2); break;
            }
        }

        // (the bases' names under them, and where the sugars are)
        var purine = d.Atoms.First().Base;
        var pyrimidine = d.Atoms.Last().Base;
        var names = oy + 1.75 * Scale + 38;
        Say(dc, $"{BaseTitle(purine)} ({purine}) · purine: two rings", cx - 10, names, 13, K.Darkened(Base(purine)), align: 1, strong: true);
        Say(dc, $"{BaseTitle(pyrimidine)} ({pyrimidine}) · pyrimidine: one ring", cx + 10, names, 13, K.Darkened(Base(pyrimidine)), align: 0, strong: true);
        var left = d.Atoms.FindIndex(a => a.Name == "C1'");
        var right = d.Atoms.FindLastIndex(a => a.Name == "C1'");
        if (left >= 0) Say(dc, "to sugar", P(left).X, P(left).Y + 17, 11, text2);
        if (right >= 0) Say(dc, "to sugar", P(right).X, P(right).Y + 17, 11, text2);
    }

    private static string BaseTitle(char b) => b switch { 'A' => "Adenine", 'G' => "Guanine", 'C' => "Cytosine", 'T' => "Thymine", _ => "Uracil" };
}

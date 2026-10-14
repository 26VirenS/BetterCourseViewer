using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// The Biomolecule Builder's other three pages (spec §4.4): Nucleic acids (the nucleotides, and a DNA strand built
// nucleotide by nucleotide with its phosphodiester bonds), Carbohydrates (monosaccharides with their ring carbons
// numbered and their α or β, disaccharides with their glycosidic bonds split and joined by water, polysaccharides as
// hexagon glyphs) and Lipids (glycerol, saturated and unsaturated fatty acids, a triglyceride, a phospholipid,
// cholesterol).

/// <summary>The Nucleic acids page (§4.4 Nucleic acids): the nine nucleotides of §6.6, or a strand of up to 12 nt built as
/// strand 1 of the B-DNA model, each nucleotide added by dehydration, its 5′ phosphate and 3′-OH ends and phosphodiester
/// bonds labelled, the complementary strand added on request and the whole opened in DNA &amp; RNA.</summary>
internal sealed class BiomolNucleicPage : BiomolPageView
{
    private readonly SelectorBar viewBar = new(new[] { "Nucleotides", "Build a strand" }, 0);
    private readonly BiomolList list = new();
    private readonly FrameworkElement nucleotidePanel, strandPanel;
    private readonly Wpf.Ui.Controls.TextBox strandBox;
    private readonly TextBlock message = BioUI.Caption("");
    private readonly ToggleSwitch complement = new();
    private readonly BiomolViewControls view = new();
    private readonly DispatcherTimer typed = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private (string Key, Func<BiomolScene> Build)? during;
    private int motionGen;
    private bool syncing;

    private static bool StrandShown => BiomoleculeBuilderModel.NucleicView == "strand";
    private static string Strand => BiomoleculeBuilderModel.Strand;

    protected override string HydrogenKey => StrandShown ? "strand" : "nucleic";

    private static readonly IReadOnlyDictionary<string, string> FullNames = new Dictionary<string, string>
    {
        ["DA"] = "Deoxyadenosine monophosphate", ["DC"] = "Deoxycytidine monophosphate", ["DG"] = "Deoxyguanosine monophosphate",
        ["DT"] = "Thymidine monophosphate", ["A"] = "Adenosine monophosphate", ["C"] = "Cytidine monophosphate",
        ["G"] = "Guanosine monophosphate", ["U"] = "Uridine monophosphate", ["ATP"] = "Adenosine triphosphate",
    };

    public BiomolNucleicPage(ToolContext c) : base(c, "3D model of a nucleotide")
    {
        viewBar.Changed += i =>
        {
            if (syncing) return;
            Stage.Stop();
            BiomoleculeBuilderModel.NucleicView = i == 0 ? "nucleotides" : "strand";
            Forget();
            BiomolPrefs.Changed();
        };
        list.Heading("In DNA (deoxyribonucleotides)");
        foreach (var code in new[] { "DA", "DC", "DG", "DT" }) Row(code);
        list.Heading("In RNA (ribonucleotides)");
        foreach (var code in new[] { "A", "C", "G", "U" }) Row(code);
        list.Heading("The cell's energy carrier");
        Row("ATP");
        list.Picked += k =>
        {
            BiomoleculeBuilderModel.Nucleotide = k;
            Forget();
            BiomolPrefs.Changed();
        };
        nucleotidePanel = K.V(10, list, BioUI.Caption("Each is a phosphate, a sugar and a base. Sugar carbons are numbered 1′ to 5′."));

        strandBox = ToolKit.Box(Strand, "A, C, G and T, as ATGCGT");
        strandBox.FontFamily = BioUI.Mono;
        strandBox.CharacterCasing = CharacterCasing.Upper;
        strandBox.ToolTip = "DNA letters 5′→3′, up to 12 (Ctrl+F comes here)";
        System.Windows.Automation.AutomationProperties.SetName(strandBox, "DNA strand, 5 prime to 3 prime, up to 12 nucleotides");
        strandBox.TextChanged += (_, _) => { if (!syncing) { typed.Stop(); typed.Start(); } };
        strandBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { typed.Stop(); ApplyTyped(); e.Handled = true; } };
        typed.Tick += (_, _) => { typed.Stop(); ApplyTyped(); };
        message.Visibility = Visibility.Collapsed;
        message.SetResourceReference(TextBlock.ForegroundProperty, B.Critical);
        var bases = K.Columns("*, 4, *, 4, *, 4, *");
        var col = 0;
        foreach (var b in "ACGT")
        {
            var letter = b;
            var face = BiomolKit.Letter(b.ToString(), BioColors.Base(b), 28);
            face.HorizontalAlignment = HorizontalAlignment.Center;
            var button = K.Subtle(face, () => Append(letter), new Thickness(2), 6);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.ToolTip = $"Add {BiomolNucleotide.BaseName(b).ToLowerInvariant()} ({b}) at the 3′ end";
            System.Windows.Automation.AutomationProperties.SetName(button, $"Add {BiomolNucleotide.BaseName(b)}");
            bases.Children.Add(button.Col(col));
            col += 2;
        }
        var remove = K.Button("Remove last", RemoveLast, icon: SymbolRegular.Backspace20);
        remove.MinWidth = 0;
        remove.ToolTip = "Take the 3′ nucleotide off (hydrolysis of its phosphodiester bond)";
        BiomolKit.OnToggle(complement, () => syncing, on => { Stage.Stop(); BiomoleculeBuilderModel.Complement = on; BiomolPrefs.Changed(); });
        var open = ChemKit.Wide("Open in DNA & RNA", () => BiomolHandOver.ToTool(C, "dna", "sequence", new Dictionary<string, string> { ["sequence"] = Strand }), icon: SymbolRegular.Ribbon20);
        open.ToolTip = "The strand and its complement in the DNA & RNA tool";
        strandPanel = K.V(12, BioUI.Heading("Build a strand"), BioUI.Caption("Type up to 12 nucleotides of DNA, 5′ to 3′, or add them one at a time: each joins the 3′ end."),
            strandBox, message, bases, remove, BiomolKit.Toggle("Add the complementary strand", complement, "Strand 2, antiparallel, paired A·T and G·C"), open);

        Left.Children.Add(viewBar);
        Left.Children.Add(nucleotidePanel);
        Left.Children.Add(strandPanel);
        Left.Children.Add(view);
        view.Molecule = () => StrandShown
            ? (BiomolStrand.Build(Strand, BiomoleculeBuilderModel.Complement).Model, $"DNA {Strand}")
            : (BioData.Molecule(BiomoleculeBuilderModel.Nucleotide)?.Model(BiomoleculeBuilderModel.Ph7) ?? new BioMoleculeModel(), BiomolNucleotide.ShortName(BiomoleculeBuilderModel.Nucleotide));
        view.Open = OpenInBuilder;
    }

    private void Row(string code)
    {
        var b = BiomolNucleotide.BaseLetter(code);
        list.Row(code, BiomolNucleotide.ShortName(code), FullNames.GetValueOrDefault(code), BiomolKit.Letter(b.ToString(), BioColors.Base(b)));
    }

    public override bool FocusField()
    {
        if (!StrandShown) return false;
        strandBox.Focus();
        strandBox.SelectAll();
        return true;
    }

    private void Forget()
    {
        Picked = null;
        Stage.View.Selection = null;
    }

    private void SetBox(string text)
    {
        syncing = true;
        strandBox.Text = text;
        strandBox.CaretIndex = text.Length;
        syncing = false;
    }

    private void ApplyTyped()
    {
        var (seq, msg) = BiomolInput.Dna(strandBox.Text);
        if (seq.Length == 0) msg = "Type at least one base: A, C, G or T.";
        message.Text = msg ?? "";
        message.Visibility = msg == null ? Visibility.Collapsed : Visibility.Visible;
        if (seq.Length > 0) Change(seq);
    }

    private void Append(char b)
    {
        if (Strand.Length >= BiomolStrand.MaxLength)
        {
            message.Text = $"A strand here is at most {BiomolStrand.MaxLength} nucleotides.";
            message.Visibility = Visibility.Visible;
            return;
        }
        Change(Strand + b);
        SetBox(Strand);
    }

    private void RemoveLast()
    {
        if (Strand.Length <= 1) return;
        Change(Strand[..^1]);
        SetBox(Strand);
    }

    /// <summary>The strand changed: a nucleotide added at the 3′ end plays its dehydration, one taken off its hydrolysis.</summary>
    private void Change(string seq)
    {
        var old = Strand;
        if (seq == old) return;
        BiomoleculeBuilderModel.Strand = seq;
        Forget();
        if (StrandShown && Stage.CanPlay && old.Length >= 1 && seq.Length >= 1)
        {
            var both = BiomoleculeBuilderModel.Complement;
            var look = StrandLook();
            var gen = ++motionGen;
            void Ended()
            {
                if (gen == motionGen) during = null;
                if (IsLoaded) Draw();
            }
            if (seq.Length == old.Length + 1 && seq.StartsWith(old, StringComparison.Ordinal))
            {
                var d = BiomolStrand.Build(seq, both);
                during = ($"{StrandKey(seq, look)}|adding", () => StrandScene(d, look, d.Sequence.Length - 1));
                Stage.Play(BiomolScenes.StrandAdded(d, look, Chem), StrandKey(seq, look), Ended);
            }
            else if (old.Length == seq.Length + 1 && old.StartsWith(seq, StringComparison.Ordinal))
            {
                var dOld = BiomolStrand.Build(old, both);
                var d = BiomolStrand.Build(seq, both);
                during = ($"{StrandKey(seq, look)}|removing", () => StrandScene(d, look, null));
                Stage.Play(BiomolScenes.StrandRemoved(dOld, look, Chem), StrandKey(seq, look), Ended);
            }
        }
        BiomolPrefs.Changed();
    }

    private static BiomolLook StrandLook() => BiomoleculeBuilderModel.Look(false, Dark);

    private static string StrandKey(string seq, BiomolLook look) => $"strand|{seq}|{BiomoleculeBuilderModel.Complement}|{look.Key}";

    private static BiomolScene StrandScene(BioDuplex d, BiomolLook look, int? hiddenPair)
    {
        var c = BiomolScenes.Strand(d, look, new BiomolScenes.StrandOptions(hiddenPair), Chem);
        var b = BioV.BoundingSphere(d.Atoms.Select(a => a.Pos).ToList());
        return new BiomolScene { Content = new BioSceneContent { Items = c.Items, Labels = c.Labels, Frame = new BioSphere(b.Centre, b.Radius + 2.5) } };
    }

    protected override void Draw()
    {
        var strand = StrandShown;
        syncing = true;
        try
        {
            viewBar.Select(strand ? 1 : 0, false);
            nucleotidePanel.Visibility = strand ? Visibility.Collapsed : Visibility.Visible;
            strandPanel.Visibility = strand ? Visibility.Visible : Visibility.Collapsed;
            list.Select(BiomoleculeBuilderModel.Nucleotide);
            BiomolKit.Set(complement, BiomoleculeBuilderModel.Complement);
            if (!strandBox.IsKeyboardFocusWithin && BiomolInput.Dna(strandBox.Text).Sequence != Strand) SetBox(Strand);
        }
        finally { syncing = false; }
        if (strand)
        {
            var seq = Strand;
            var both = BiomoleculeBuilderModel.Complement;
            var d = BiomolStrand.Build(seq, both);
            view.Sync("strand", d.Atoms.Count, noHydrogens: "The strand is built from the B-DNA model's heavy atoms (no H).",
                noPh7: "Its phosphates are drawn without H, as at pH 7.", colourHint: StrandHint());
            var look = StrandLook();
            Title(both ? $"DNA · {seq.Length} base pairs" : $"DNA strand · {seq.Length} nucleotide{(seq.Length == 1 ? "" : "s")}", both ? $"5′-{seq}-3′ and its complement, 3′-{BioSeq.Complement(seq)}-5′" : $"5′-{seq}-3′, built as one strand of B-DNA");
            Stage.View.Accessibility = $"3D model of a DNA strand, 5 prime {string.Join(" ", seq.ToCharArray())} 3 prime{(both ? ", with its complementary strand" : "")}";
            if (during is { } dd) Stage.Show(dd.Key, dd.Build, $"strand|{seq}|{both}");
            else Stage.Show(StrandKey(seq, look), () => StrandScene(d, look, null), $"strand|{seq}|{both}");
        }
        else
        {
            var code = BiomoleculeBuilderModel.Nucleotide;
            var mol = BioData.Molecule(code);
            var atoms = mol?.Model(BiomoleculeBuilderModel.Ph7).Atoms.Count ?? 0;
            view.Sync("nucleic", atoms, colourHint: NucleotideHint(code));
            var look = LookFor("nucleic", atoms);
            var b = BiomolNucleotide.BaseLetter(code);
            Title(BiomolNucleotide.ShortName(code), $"{FullNames.GetValueOrDefault(code)} · {(BiomolNucleotide.IsDeoxy(code) ? "DNA" : (code == "ATP" ? "energy" : "RNA"))}");
            Stage.View.Accessibility = $"3D model of {FullNames.GetValueOrDefault(code)}: a phosphate, a {(BiomolNucleotide.IsDeoxy(code) ? "deoxyribose" : "ribose")} sugar and the base {BiomolNucleotide.BaseName(b).ToLowerInvariant()}";
            var chem = Chem;
            Stage.Show($"nt|{code}|{look.Key}", () =>
            {
                var content = BiomolScenes.Nucleotide(code, look, chem);
                var m = BioData.Molecule(code)?.Model(look.Ph7) ?? new BioMoleculeModel();
                return new BiomolScene { Content = content, Marks = look.Labels ? BiomolScenes.CarbonMarks(m, true, look, chem) : new List<BiomolMark>() };
            }, $"nt|{code}");
        }
        DrawFacts();
    }

    private static string? StrandHint() => BiomoleculeBuilderModel.ColourBy switch
    {
        BiomolColourBy.Part => "Phosphate violet, sugar teal, each base in its letter's colour.",
        BiomolColourBy.Class => "Each strand's backbone in its colour; bases by letter.",
        _ => null,
    };

    private static string? NucleotideHint(string code) => BiomoleculeBuilderModel.ColourBy switch
    {
        BiomolColourBy.Part => $"Phosphate violet, sugar teal, base in {BiomolNucleotide.BaseName(BiomolNucleotide.BaseLetter(code)).ToLowerInvariant()}'s colour.",
        BiomolColourBy.Class => "The base in its letter's colour; the rest grey.",
        _ => null,
    };

    protected override void DrawFacts()
    {
        Right.Children.Clear();
        if (StrandShown) StrandFacts(); else NucleotideFacts();
    }

    private void NucleotideFacts()
    {
        var code = BiomoleculeBuilderModel.Nucleotide;
        if (BioData.Molecule(code) is not { } mol) return;
        var b = BiomolNucleotide.BaseLetter(code);
        var deoxy = BiomolNucleotide.IsDeoxy(code);
        var purine = BiomolNucleotide.IsPurine(b);
        Right.Children.Add(BioUI.Card(BiomolNucleotide.ShortName(code), BioColors.Base(b), FullNames.GetValueOrDefault(code),
            BioUI.FactRow("Sugar", deoxy ? "Deoxyribose: only H at 2′ (DNA)" : "Ribose: an –OH at 2′ (RNA, ATP)"),
            BioUI.FactRow("Base", $"{BiomolNucleotide.BaseName(b)}, a {(purine ? "purine (two rings)" : "pyrimidine (one ring)")}; pairs with {PairOf(b, deoxy)}"),
            BioUI.FactRow("Phosphate", code == "ATP" ? "Three (α, β, γ) on the 5′ carbon: the bonds between them carry the energy ATP gives up" : "One, on the 5′ carbon"),
            BioUI.FactRow("Found in", deoxy ? "DNA" : (code == "ATP" ? "Every cell, as its energy currency (and RNA as AMP)" : "RNA")),
            BioUI.FactRow("Formula", $"{BiomolText.Formula(mol.Formula ?? "")} · {BiomolKit.N(mol.Mass ?? 0, "0.00")} g/mol (neutral form)"),
            BioUI.FactRow("At pH 7", code == "ATP" ? "ATP⁴⁻: its phosphates' four OH have lost their H" : "Its phosphate is −2: both its OH have lost their H"),
            BioUI.FactRow("Numbering", "Sugar carbons 1′ to 5′: the base joins at 1′, the phosphate at 5′, and the next nucleotide's phosphate at the 3′-OH."),
            BiomolKit.Action("See it in DNA & RNA", SymbolRegular.Ribbon20, () => BiomolHandOver.ToTool(C, "dna", "nucleotides", new Dictionary<string, string> { ["nucleotide"] = code }), "DNA & RNA ▸ Nucleotides")));
        if (deoxy || code is "A" or "C" or "G" or "U")
            Right.Children.Add(BioUI.Card("DNA or RNA?", null, null,
                K.Wrapped("Two differences: the sugar (deoxyribose has H at 2′ where ribose has –OH, not at 3′) and one base (DNA has thymine, T; RNA has uracil, U, which is T without its methyl group).", Ty.Body)));
        if (PickedAtom(Picked) is { } id && mol.Model(BiomoleculeBuilderModel.Ph7) is { } m && id < m.Atoms.Count)
            Right.Children.Add(PickedCard($"{BiomolNucleotide.ShortName(code)} · {m.Atoms[id].Name}", m.Atoms[id].Element, BiomolNucleotide.Part(m.Atoms[id].Name).Name));
        Right.Children.Add(Legend(b));
    }

    private static string PairOf(char b, bool dna) => b switch
    {
        'A' => dna ? "T (two H-bonds)" : "U (two H-bonds)",
        'T' or 'U' => "A (two H-bonds)",
        'G' => "C (three H-bonds)",
        _ => "G (three H-bonds)",
    };

    private void StrandFacts()
    {
        var seq = Strand;
        var both = BiomoleculeBuilderModel.Complement;
        var n = seq.Length;
        var parts = new List<UIElement?> { BioUI.SequenceText(seq, groupsPerLine: 2) };
        if (both) parts.Add(BioUI.SequenceText(BioSeq.Complement(seq), fivePrimeFirst: false, groupsPerLine: 2));
        Right.Children.Add(BioUI.Card(both ? "Double strand" : "Strand", BioPalette.Strand1.ToColor(), both ? "Strand 1 over its complement, antiparallel" : "Written 5′ to 3′", parts.ToArray()));
        var d = BiomolStrand.Build(seq, both);
        Right.Children.Add(BioUI.Facts(new (string, string, Color?)[]
        {
            ("Nucleotides", both ? $"{n} + {n}" : $"{n}", null),
            ("Phosphodiester bonds", both ? $"{Math.Max(n - 1, 0)} + {Math.Max(n - 1, 0)}" : $"{Math.Max(n - 1, 0)}", null),
            ("Water released", $"{(both ? 2 : 1) * Math.Max(n - 1, 0)} H₂O", null),
            ("GC content", $"{BiomolKit.N(BioSeq.GcContent(seq) * 100, "0")}%", null),
            both ? ("Base-pair H-bonds", $"{d.HBonds.Count}", null) : ("Ends", "5′ phosphate · 3′-OH", null),
        }));
        Right.Children.Add(BioUI.Card("The phosphodiester bond", K.Hex(BiomolParts.BondHighlight.HexFor(Dark)), "C3′–O3′–P–O5′–C5′",
            BioUI.FactRow("How it forms", "The new nucleotide's phosphate bonds to the 3′-OH of the strand's last sugar, and water is released (dehydration synthesis)."),
            BioUI.FactRow("In cells", "The new nucleotide arrives as a triphosphate (dNTP); two of its phosphates leave as pyrophosphate, which pays for the bond."),
            BioUI.FactRow("Direction", "Nucleotides join only at the 3′ end, so a strand grows 5′ → 3′.")));
        if (PickedAtom(Picked) is { } id && id < d.Atoms.Count)
        {
            var a = d.Atoms[id];
            var number = a.Strand == 1 ? a.Pair + 1 : n - a.Pair;
            Right.Children.Add(PickedCard($"Strand {a.Strand} · {a.Base} {number} · {a.Name}", a.Element, BiomolNucleotide.Part(a.Name).Name));
        }
        Right.Children.Add(Legend(null));
    }

    private static FrameworkElement Legend(char? b)
    {
        var dark = Dark;
        var items = new List<(Color, string)>();
        switch (BiomoleculeBuilderModel.ColourBy)
        {
            case BiomolColourBy.Part:
                items.Add((K.Hex(BiomolParts.Phosphate.Colour.HexFor(dark)), "Phosphate"));
                items.Add((K.Hex(BiomolParts.Sugar.Colour.HexFor(dark)), "Sugar"));
                if (b is { } x) items.Add((BioColors.Base(x), $"Base: {BiomolNucleotide.BaseName(x)}"));
                else items.AddRange("ACGT".Select(c => (BioColors.Base(c), $"{c} · {BiomolNucleotide.BaseName(c)}")));
                break;
            case BiomolColourBy.Class:
                if (b == null) { items.Add((BioPalette.Strand1.ToColor(), "Strand 1 backbone")); items.Add((BioPalette.Strand2.ToColor(), "Strand 2 backbone")); }
                else items.Add((K.Hex(BiomolParts.Grey.HexFor(dark)), "Phosphate and sugar"));
                if (b is { } y) items.Add((BioColors.Base(y), $"Base: {BiomolNucleotide.BaseName(y)}"));
                else items.AddRange("ACGT".Select(c => (BioColors.Base(c), $"{c} · {BiomolNucleotide.BaseName(c)}")));
                break;
            default:
                items.AddRange(new[] { "C", "H", "N", "O", "P" }.Select(s => (MBChem.ColorOf(s), $"{s} · {MBChem.Element(s)?.Name ?? s}")));
                break;
        }
        items.Add((K.Hex(BiomolParts.BondHighlight.HexFor(dark)), "Phosphodiester bond (glowing)"));
        return K.V(8, K.Text($"Colour by {BiomoleculeBuilderModel.ColourBy.Label().ToLowerInvariant()}", Ty.Caption, B.Text2).Strong(), BioUI.Legend(items));
    }
}

/// <summary>The Carbohydrates page (§4.4 Carbohydrates): the five monosaccharides with their ring carbons numbered 1–6
/// and their anomeric –OH's face; the four disaccharides with their glycosidic bond glowing and labelled, split by water
/// and joined again; and the five polysaccharides as hexagon glyphs.</summary>
internal sealed class BiomolCarbPage : BiomolPageView
{
    private readonly BiomolList list = new();
    private readonly Wpf.Ui.Controls.Button split;
    private readonly ToggleSwitch anomers = new();
    private readonly FrameworkElement anomerPanel;
    private bool syncing;

    /// <summary>α- and β-glucose shown side by side (asked for, and one of them chosen).</summary>
    private static bool CompareShown => BiomoleculeBuilderModel.CompareAnomers && BiomoleculeBuilderModel.CarbView == "mono" && BiomoleculeBuilderModel.Monosaccharide is "GLC" or "BGC";
    private readonly FrameworkElement splitPanel;
    private readonly BiomolViewControls view = new();
    private readonly ContentControl polyHost = new() { Focusable = false };
    private readonly Border polyCard;
    private readonly TextBlock polyTitle = K.Text("", Ty.Subtitle), polySub = K.Text("", Ty.Body, B.Text2), linkShown = K.Text("", Ty.BodyStrong);
    private BiomolPolysaccharide? polyShown;
    private (string Key, Func<BiomolScene> Build)? during;
    private int motionGen;

    private static string Kind => BiomoleculeBuilderModel.CarbView;

    protected override string HydrogenKey => "carbs";

    private static readonly IReadOnlyDictionary<string, string> Captions = new Dictionary<string, string>
    {
        ["GLC"] = "C₆H₁₂O₆ · ring of 6", ["BGC"] = "C₆H₁₂O₆ · ring of 6", ["GAL"] = "C₆H₁₂O₆ · glucose's C4 epimer",
        ["FRU"] = "C₆H₁₂O₆ · ring of 5", ["BDR"] = "C₅H₁₀O₅ · the sugar of RNA",
        ["MAL"] = "glucose α(1→4) glucose", ["CBI"] = "glucose β(1→4) glucose", ["SUC"] = "glucose α(1→2)β fructose", ["LAT"] = "galactose β(1→4) glucose",
    };

    public BiomolCarbPage(ToolContext c) : base(c, "3D model of a sugar")
    {
        list.Heading("Monosaccharides");
        foreach (var code in BiomolSugar.Monosaccharides) list.Row("mono:" + code, BiomolSugar.ShortName(code), Captions.GetValueOrDefault(code), Glyph(code));
        list.Heading("Disaccharides");
        foreach (var code in BiomolSugar.Disaccharides) list.Row("di:" + code, BiomolSugar.ShortName(code), Captions.GetValueOrDefault(code), Glyph(code));
        list.Heading("Polysaccharides (drawn flat)");
        foreach (var p in BiomolPolysaccharides.All) list.Row("poly:" + p.Key(), p.Title(), p == BiomolPolysaccharide.Chitin ? "β(1→4) N-acetylglucosamine" : (p is BiomolPolysaccharide.Cellulose ? "β(1→4) glucose" : "α(1→4) glucose"), Glyph(p.Key()));
        list.Picked += k =>
        {
            Stage.Stop();
            var parts = k.Split(':');
            BiomoleculeBuilderModel.CarbView = parts[0];
            if (parts[0] == "mono") BiomoleculeBuilderModel.Monosaccharide = parts[1];
            else if (parts[0] == "di") { BiomoleculeBuilderModel.Disaccharide = parts[1]; BiomoleculeBuilderModel.Split = false; }
            else if (BiomolPrefs.Poly(parts[1]) is { } p) BiomoleculeBuilderModel.Polysaccharide = p;
            Picked = null;
            Stage.View.Selection = null;
            BiomolPrefs.Changed();
        };
        split = ChemKit.Wide("Split with water", SplitOrJoin, icon: SymbolRegular.Water20);
        splitPanel = K.V(8, BioUI.Heading("Dehydration and hydrolysis"), split, BioUI.Caption("Hydrolysis: water splits the glycosidic bond. Dehydration synthesis: the bond forms and water is released."));
        BiomolKit.OnToggle(anomers, () => syncing, on =>
        {
            BiomoleculeBuilderModel.CompareAnomers = on;
            Picked = null;
            Stage.View.Selection = null;
            BiomolPrefs.Changed();
        });
        anomerPanel = K.V(8, BioUI.Heading("α or β"), BiomolKit.Toggle("α- and β-glucose side by side", anomers, "The two forms of glucose next to each other, to compare C1"));
        Left.Children.Add(list);
        Left.Children.Add(anomerPanel);
        Left.Children.Add(splitPanel);
        Left.Children.Add(view);
        view.Molecule = () => Kind switch
        {
            "mono" => (BioData.Molecule(BiomoleculeBuilderModel.Monosaccharide)?.Model() ?? new BioMoleculeModel(), BiomolSugar.ShortName(BiomoleculeBuilderModel.Monosaccharide)),
            "di" => (BiomoleculeBuilderModel.Split && BioData.Molecule(BiomoleculeBuilderModel.Disaccharide) is { } m && BiomolSugar.Split(m) is { } s ? s.Model
                : BioData.Molecule(BiomoleculeBuilderModel.Disaccharide)?.Model() ?? new BioMoleculeModel(), BiomolSugar.ShortName(BiomoleculeBuilderModel.Disaccharide)),
            _ => null,
        };
        view.Open = OpenInBuilder;

        var words = K.V(2, polyTitle, polySub);
        words.Margin = new Thickness(16, 16, 16, 0);
        linkShown.Margin = new Thickness(16, 0, 16, 16);
        linkShown.HorizontalAlignment = HorizontalAlignment.Center;
        polyHost.Margin = new Thickness(12);
        var hint = K.Text("Press a link (or use ← →) to see its linkage", Ty.Caption, B.Text2);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Margin = new Thickness(12);
        polyCard = K.Card(K.Rows("auto, *, auto, auto", words.Row(0), polyHost.Row(1), linkShown.Row(2), hint.Row(3)), new Thickness(0));
        polyCard.Visibility = Visibility.Collapsed;
        Centre.Children.Add(polyCard);
    }

    /// <summary>A small hexagon (a ring of 6) or pentagon (a ring of 5) glyph for a sugar's row.</summary>
    private static FrameworkElement Glyph(string code)
    {
        var five = code is "FRU" or "BDR";
        var colour = code is "cellulose" ? BioPalette.Polar.ToColor() : (code is "chitin" ? BioPalette.Nonpolar.ToColor() : BioPalette.Strand2.ToColor());
        var g = new System.Windows.Shapes.Polygon { Stroke = K.Brush(colour), StrokeThickness = 1.4, Fill = K.Brush(K.WithAlpha(colour, 0.2)), Width = 22, Height = 22 };
        var n = five ? 5 : 6;
        for (var i = 0; i < n; i++)
        {
            var a = -Math.PI / 2 + i * 2 * Math.PI / n;
            g.Points.Add(new Point(11 + 9 * Math.Cos(a), 11 + 9 * Math.Sin(a)));
        }
        g.VerticalAlignment = VerticalAlignment.Center;
        return g;
    }

    /// <summary>The disaccharide split by water, or joined with water released: the two sugars moving apart or together.</summary>
    private void SplitOrJoin()
    {
        Stage.Stop();
        var code = BiomoleculeBuilderModel.Disaccharide;
        var toSplit = !BiomoleculeBuilderModel.Split;
        BiomoleculeBuilderModel.Split = toSplit;
        Picked = null;
        Stage.View.Selection = null;
        if (Stage.CanPlay)
        {
            var look = SugarLook(code, toSplit);
            var gen = ++motionGen;
            during = ($"di-moving|{code}|{toSplit}", () => new BiomolScene { Content = new BioSceneContent { Frame = BiomolScenes.DisaccharideFrame(code) } });
            Stage.Play(BiomolScenes.DisaccharideMotion(code, toSplit, look, Chem), DiKey(code, toSplit, look), () =>
            {
                if (gen == motionGen) during = null;
                if (IsLoaded) Draw();
            });
        }
        BiomolPrefs.Changed();
    }

    private static int Atoms(string code, bool split) =>
        BioData.Molecule(code) is { } m ? (split && BiomolSugar.Split(m) is { } s ? s.Model.Atoms.Count : m.Atoms.Count) : 0;

    private static BiomolLook SugarLook(string code, bool split) => LookFor("carbs", Atoms(code, split));

    private static string DiKey(string code, bool split, BiomolLook look) => $"di|{code}|{split}|{look.Key}";

    protected override void Draw()
    {
        var kind = Kind;
        list.Select(kind switch
        {
            "mono" => "mono:" + BiomoleculeBuilderModel.Monosaccharide,
            "di" => "di:" + BiomoleculeBuilderModel.Disaccharide,
            _ => "poly:" + BiomoleculeBuilderModel.Polysaccharide.Key(),
        });
        splitPanel.Visibility = kind == "di" ? Visibility.Visible : Visibility.Collapsed;
        anomerPanel.Visibility = kind == "mono" && BiomoleculeBuilderModel.Monosaccharide is "GLC" or "BGC" ? Visibility.Visible : Visibility.Collapsed;
        syncing = true;
        BiomolKit.Set(anomers, BiomoleculeBuilderModel.CompareAnomers);
        syncing = false;
        split.Content = BiomoleculeBuilderModel.Split ? "Join (dehydration synthesis)" : "Split with water (hydrolysis)";
        split.Icon = new SymbolIcon { Symbol = BiomoleculeBuilderModel.Split ? SymbolRegular.Merge20 : SymbolRegular.Water20 };
        split.ToolTip = BiomoleculeBuilderModel.Split ? "Join the two sugars again: the glycosidic bond forms and water is released" : "Add water across the glycosidic bond: the two sugars come apart";
        SceneCard.Visibility = kind == "poly" ? Visibility.Collapsed : Visibility.Visible;
        polyCard.Visibility = kind == "poly" ? Visibility.Visible : Visibility.Collapsed;
        // (a flat drawing has no atoms to style)
        view.Visibility = kind == "poly" ? Visibility.Collapsed : Visibility.Visible;
        const string noPh7 = "Sugars have no groups charged at pH 7.";
        switch (kind)
        {
            case "poly":
            {
                view.Sync("carbs", 0, noPh7: noPh7, noAtoms: true);
                var p = BiomoleculeBuilderModel.Polysaccharide;
                if (polyShown != p)
                {
                    polyShown = p;
                    var drawing = new BiomolPolyView(p);
                    drawing.LinkChosen += text => linkShown.Text = text == null ? "" : $"This link: {text}";
                    polyHost.Content = drawing;
                    linkShown.Text = "";
                }
                polyTitle.Text = p.Title();
                polySub.Text = p == BiomolPolysaccharide.Chitin ? "β(1→4) N-acetylglucosamine, every other sugar flipped" : (p == BiomolPolysaccharide.Cellulose ? "β(1→4) glucose, every other sugar flipped" : "α-glucose, α(1→4) and α(1→6) links");
                break;
            }
            case "di":
            {
                var code = BiomoleculeBuilderModel.Disaccharide;
                var splitNow = BiomoleculeBuilderModel.Split;
                var atoms = Atoms(code, splitNow);
                view.Sync("carbs", atoms, noPh7: noPh7, colourHint: BiomoleculeBuilderModel.ColourBy == BiomolColourBy.Element ? null : "Each sugar in its colour; the water's atoms pink.");
                var look = SugarLook(code, splitNow);
                var (first, second) = BiomolSugar.Residues[code];
                Title(BiomolSugar.ShortName(code), splitNow ? $"Split by water: {first.ToLowerInvariant()} + {second.ToLowerInvariant()}" : $"{first} {BiomolSugar.Linkage[code]} {second.ToLowerInvariant()} · C₁₂H₂₂O₁₁");
                Stage.View.Accessibility = splitNow ? $"3D model of {BiomolSugar.ShortName(code).ToLowerInvariant()} split by water into {first.ToLowerInvariant()} and {second.ToLowerInvariant()}" : $"3D model of {BiomolSugar.ShortName(code).ToLowerInvariant()}: {first.ToLowerInvariant()} and {second.ToLowerInvariant()} joined by a {BiomolSugar.Linkage[code]} glycosidic bond";
                var chem = Chem;
                if (during is { } d) Stage.Show(d.Key, d.Build, $"di|{code}");
                else Stage.Show(DiKey(code, splitNow, look), () =>
                {
                    var s = BiomolScenes.Disaccharide(code, splitNow, look, chem);
                    return look.Labels ? s : new BiomolScene { Content = s.Content };
                }, $"di|{code}");
                break;
            }
            default:
            {
                var code = BiomoleculeBuilderModel.Monosaccharide;
                var atoms = Atoms(code, false);
                view.Sync("carbs", atoms, noPh7: noPh7, colourHint: BiomoleculeBuilderModel.ColourBy == BiomolColourBy.Element ? null : "Ring slate, the anomeric –OH pink, the CH₂OH teal, the other –OH groups red.");
                var look = LookFor("carbs", atoms);
                var mol = BioData.Molecule(code);
                var chem = Chem;
                if (CompareShown)
                {
                    Title("α- and β-glucose", "Side by side: the one difference is which face of the ring C1's –OH is on");
                    Stage.View.Accessibility = "3D models of alpha-glucose and beta-glucose side by side, their ring carbons numbered";
                    Stage.Show($"anomers|{look.Key}", () =>
                    {
                        var s = BiomolScenes.AnomerPair(look, chem);
                        return look.Labels ? s : new BiomolScene { Content = s.Content };
                    }, "anomers");
                    break;
                }
                Title(BiomolSugar.ShortName(code), mol?.Title ?? "");
                Stage.View.Accessibility = $"3D model of {mol?.Title ?? code}, its ring carbons numbered";
                Stage.Show($"mono|{code}|{look.Key}", () =>
                {
                    var s = BiomolScenes.Monosaccharide(code, look, chem);
                    return look.Labels ? s : new BiomolScene { Content = s.Content };
                }, $"mono|{code}");
                break;
            }
        }
        DrawFacts();
    }

    protected override void DrawFacts()
    {
        Right.Children.Clear();
        var dark = Dark;
        switch (Kind)
        {
            case "poly":
            {
                var p = BiomoleculeBuilderModel.Polysaccharide;
                var layout = p.LayoutOf();
                Right.Children.Add(BioUI.Card(p.Title(), null, null, K.Wrapped(p.Summary(), Ty.Body),
                    BioUI.FactRow("Links", string.Join(", ", layout.Links.Where(l => l.Kind != BiomolPolysaccharides.LinkKind.Gap).Select(l => l.Text).Distinct())),
                    BioUI.FactRow("Drawn here", $"{layout.Glyphs.Count} sugars; a real chain has hundreds to thousands.")));
                Right.Children.Add(BioUI.Card("α or β decides it", null, null,
                    K.Wrapped("Starch and glycogen are α(1→4) chains, which coil and which we digest. Cellulose is β(1→4): the same glucose, but each link flips the next sugar over, so the chains run straight and pack into fibres we cannot digest.", Ty.Body)));
                Right.Children.Add(BioUI.Legend(new[]
                {
                    (p == BiomolPolysaccharide.Chitin ? BioPalette.Nonpolar.ToColor() : (p == BiomolPolysaccharide.Cellulose ? BioPalette.Polar.ToColor() : BioPalette.Strand2.ToColor()), p == BiomolPolysaccharide.Chitin ? "N-acetylglucosamine (hexagon)" : "Glucose (hexagon)"),
                    (BioPalette.Acidic.ToColor(), "O: the ring O, and the glycosidic O on each link"),
                    (Simpl.Support.Appearance.Accent, "The link pressed"),
                }));
                break;
            }
            case "di":
            {
                var code = BiomoleculeBuilderModel.Disaccharide;
                if (BioData.Molecule(code) is not { } mol || BiomolSugar.GlycosidicOf(mol) is not { } g) return;
                var (first, second) = BiomolSugar.Residues[code];
                var about = code switch
                {
                    "MAL" => "Malt sugar; the repeat of starch's α(1→4) chains.",
                    "CBI" => "The repeat of cellulose: β(1→4).",
                    "SUC" => "Table sugar. Both anomeric carbons (glucose's C1 and fructose's C2) are in the bond.",
                    _ => "Milk sugar. Lactase splits it; without it, lactose passes undigested.",
                };
                Right.Children.Add(BioUI.Card(BiomolSugar.ShortName(code), K.Hex(BiomolSugar.ResidueColours[0].HexFor(dark)), $"{first} + {second.ToLowerInvariant()}",
                    BioUI.FactRow("Glycosidic bond", $"{BiomolSugar.Linkage[code]}: {g.Anomeric}–{g.O}–{g.Other.Replace("'", "′")} ({first.ToLowerInvariant()}'s C1 to {second.ToLowerInvariant()}'s C{new string(g.Other.Where(char.IsDigit).ToArray())})"),
                    BioUI.FactRow("Formula", $"{BiomolText.Formula(mol.Formula ?? "")} · {BiomolKit.N(mol.Mass ?? 0, "0.00")} g/mol"),
                    BioUI.FactRow("Water", "One H₂O released as the bond formed (dehydration synthesis); one used to split it (hydrolysis)."),
                    BioUI.FactRow("About it", about)));
                if (BiomoleculeBuilderModel.Split)
                    Right.Children.Add(BioUI.Card("Hydrolysed", K.Hex(BiomolParts.BondHighlight.HexFor(dark)), "C₁₂H₂₂O₁₁ + H₂O → 2 C₆H₁₂O₆",
                        K.Wrapped($"The water's –OH went to {first.ToLowerInvariant()}'s C1 and its H to the O left on {second.ToLowerInvariant()}: two monosaccharides again (pink: the water's atoms).", Ty.Body)));
                Right.Children.Add(Legend(true));
                break;
            }
            default:
            {
                var code = BiomoleculeBuilderModel.Monosaccharide;
                if (BioData.Molecule(code) is not { } mol) return;
                var ring = BiomolSugar.RingOf(mol);
                var alpha = ring != null && BiomolSugar.IsAlpha(mol, ring);
                var about = code switch
                {
                    "GLC" => "Its C1 –OH is on the opposite face of the ring from C6 (trans): α. Starch and glycogen are made of it.",
                    "BGC" => "Its C1 –OH is on the same face of the ring as C6 (cis): β. Cellulose is made of it.",
                    "GAL" => "Glucose's twin but at C4, where its –OH points the other way. With glucose it makes lactose.",
                    "FRU" => "A ketose: its anomeric carbon is C2, and its ring has five atoms. With glucose it makes sucrose.",
                    _ => "The sugar of RNA and ATP; DNA's deoxyribose lacks the O on C2. Its ring has five atoms.",
                };
                Right.Children.Add(BioUI.Card(BiomolSugar.ShortName(code), K.Hex(BioPalette.Strand2.HexFor(dark)), mol.Title,
                    BioUI.FactRow("Formula", $"{BiomolText.Formula(mol.Formula ?? "")} · {BiomolKit.N(mol.Mass ?? 0, "0.00")} g/mol"),
                    BioUI.FactRow("Ring", ring == null ? "—" : $"{ring.Atoms.Count} atoms ({ring.Atoms.Count - 1} C and the ring O): a {(ring.Atoms.Count == 6 ? "pyranose" : "furanose")}"),
                    BioUI.FactRow("Anomer", ring == null ? "—" : $"{(alpha ? "α" : "β")}: the C{new string(ring.Anomeric.Where(char.IsDigit).ToArray())} –OH on {(alpha ? "the opposite face to" : "the same face as")} C{new string(ring.Reference.Where(char.IsDigit).ToArray())}"),
                    BioUI.FactRow("About it", about)));
                Right.Children.Add(BioUI.Card("α or β glucose?", null, null,
                    K.Wrapped("The one difference is at C1: α-glucose has its C1 –OH on the opposite face of the ring from C6 (trans), β-glucose on the same face (cis). That decides starch (α) or cellulose (β).", Ty.Body),
                    BioUI.Caption("Ring carbons are numbered 1 to 6 (1′ to 5′ is for nucleotides).")));
                // (side by side, a pick names its sugar: "alpha:…" or "beta:…")
                var pickedCode = CompareShown && Picked is { } pk ? (pk.StartsWith("beta:", StringComparison.Ordinal) ? "BGC" : "GLC") : code;
                if (PickedAtom(Picked) is { } id && BioData.Molecule(pickedCode) is { } pm && pm.Model() is { } m && id < m.Atoms.Count)
                    Right.Children.Add(PickedCard($"{BiomolSugar.ShortName(pickedCode)} · {m.Atoms[id].Name}", m.Atoms[id].Element, BiomolScenes.SugarPart(m.Atoms[id].Name, pm, BiomolSugar.RingOf(pm)).Name));
                Right.Children.Add(Legend(false));
                break;
            }
        }
    }

    private static FrameworkElement Legend(bool di)
    {
        var dark = Dark;
        var items = new List<(Color, string)>();
        if (BiomoleculeBuilderModel.ColourBy == BiomolColourBy.Element)
            items.AddRange(new[] { "C", "H", "O" }.Select(s => (MBChem.ColorOf(s), $"{s} · {MBChem.Element(s)?.Name ?? s}")));
        else if (di)
        {
            var (first, second) = BiomolSugar.Residues[BiomoleculeBuilderModel.Disaccharide];
            items.Add((K.Hex(BiomolSugar.ResidueColours[0].HexFor(dark)), $"{first} (the C1 side)"));
            items.Add((K.Hex(BiomolSugar.ResidueColours[1].HexFor(dark)), second));
            items.Add((K.Hex(BiomolParts.BondHighlight.HexFor(dark)), "The water's atoms"));
        }
        else
            items.AddRange(new[] { BiomolScenes.RingPart, BiomolScenes.AnomericPart, BiomolScenes.ReferencePart, BiomolScenes.HydroxylPart }.Select(p => (K.Hex(p.Colour.HexFor(dark)), p.Name)));
        if (di) items.Add((K.Hex(BiomolParts.BondHighlight.HexFor(dark)), "Glycosidic bond (glowing)"));
        return K.V(8, K.Text($"Colour by {BiomoleculeBuilderModel.ColourBy.Label().ToLowerInvariant()}", Ty.Caption, B.Text2).Strong(), BioUI.Legend(items));
    }
}

/// <summary>The Lipids page (§4.4 Lipids): glycerol, palmitic and stearic acids (saturated, straight), oleic acid (its cis
/// C9=C10 kink), the two side by side, tristearin (3 ester bonds), POPC (a hydrophilic head and hydrophobic tails) and
/// cholesterol (four fused rings); and "Make a bilayer", the Cell Explorer's Membrane page.</summary>
internal sealed class BiomolLipidPage : BiomolPageView
{
    private readonly BiomolList list = new();
    private readonly BiomolViewControls view = new();

    protected override string HydrogenKey => "lipids";

    private static readonly IReadOnlyDictionary<string, string> Captions = new Dictionary<string, string>
    {
        ["GOL"] = "The backbone of fats and phospholipids", ["PLM"] = "C16:0 · saturated", ["STE"] = "C18:0 · saturated",
        ["OLA"] = "C18:1 cis-9 · unsaturated", ["STE+OLA"] = "Stearic and oleic acids side by side", ["TGL"] = "A triglyceride (a fat)",
        ["POV"] = "A phospholipid of membranes", ["CLR"] = "A steroid: four fused rings",
    };

    public BiomolLipidPage(ToolContext c) : base(c, "3D model of a lipid")
    {
        list.Heading("Building blocks");
        foreach (var code in new[] { "GOL", "PLM", "STE", "OLA" }) list.Row(code, BiomoleculeBuilderModel.LipidTitle(code), Captions[code], Swatch(code));
        list.Heading("Compare");
        list.Row("STE+OLA", "Saturated vs unsaturated", Captions["STE+OLA"], Swatch("STE+OLA"));
        list.Heading("Lipids");
        foreach (var code in new[] { "TGL", "POV", "CLR" }) list.Row(code, BiomoleculeBuilderModel.LipidTitle(code), Captions[code], Swatch(code));
        list.Picked += k =>
        {
            BiomoleculeBuilderModel.Lipid = k;
            Picked = null;
            Stage.View.Selection = null;
            BiomolPrefs.Changed();
        };
        var bilayer = ChemKit.Wide("Make a bilayer", () => BiomolHandOver.ToTool(C, "cell", "membrane"), icon: SymbolRegular.Layer20);
        bilayer.ToolTip = "Phospholipids in a membrane: the Cell Explorer's Membrane page";
        Left.Children.Add(list);
        Left.Children.Add(K.V(8, bilayer, BioUI.Caption("In a membrane the tails face inward and the heads outward, toward the water.")));
        Left.Children.Add(view);
        view.Molecule = () =>
        {
            var code = BiomoleculeBuilderModel.Lipid;
            if (code == "STE+OLA")
            {
                var m = BioData.Molecule("STE")?.Model(BiomoleculeBuilderModel.Ph7).Moved(new BioVec(-4.5, 0, 0)) ?? new BioMoleculeModel();
                if (BioData.Molecule("OLA")?.Model(BiomoleculeBuilderModel.Ph7).Moved(new BioVec(4.5, 0, 0)) is { } o) m.Append(o);
                return (m, "stearic and oleic acids");
            }
            return (BioData.Molecule(code)?.Model(BiomoleculeBuilderModel.Ph7) ?? new BioMoleculeModel(), BiomolLipid.ShortName(code).ToLowerInvariant());
        };
        view.Open = OpenInBuilder;
    }

    private static FrameworkElement Swatch(string code)
    {
        var dark = Dark;
        var head = code is "GOL" ? BiomolParts.Hydrophilic : BiomolParts.Hydrophobic;
        var g = new Grid { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = K.Brush(K.Hex(BiomolParts.Hydrophilic.Colour.HexFor(dark))), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center });
        if (code != "GOL")
        {
            var tails = code is "TGL" ? 3 : (code is "POV" or "STE+OLA" ? 2 : 1);
            for (var k = 0; k < tails; k++)
            {
                var x = 11 + (k - (tails - 1) / 2.0) * 5;
                var kink = code is "OLA" || (code is "POV" or "STE+OLA") && k == 1;
                var line = new System.Windows.Shapes.Polyline { Stroke = K.Brush(K.Hex(head.Colour.HexFor(dark))), StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round };
                line.Points.Add(new Point(x, 9));
                line.Points.Add(new Point(x, 15));
                line.Points.Add(new Point(kink ? x + 4 : x, 21));
                g.Children.Add(line);
            }
        }
        return g;
    }

    protected override void Draw()
    {
        var code = BiomoleculeBuilderModel.Lipid;
        list.Select(code);
        var atoms = code == "STE+OLA" ? (BioData.Molecule("STE")?.Atoms.Count ?? 0) + (BioData.Molecule("OLA")?.Atoms.Count ?? 0) : BioData.Molecule(code)?.Atoms.Count ?? 0;
        var noPh7 = code is "GOL" or "TGL" or "CLR" ? "No groups charged at pH 7." : null;
        view.Sync("lipids", atoms, noPh7: noPh7, colourHint: BiomoleculeBuilderModel.ColourBy == BiomolColourBy.Element ? null : "Hydrophilic head blue, hydrophobic tails tan.");
        var look = LookFor("lipids", atoms);
        var title = BiomoleculeBuilderModel.LipidTitle(code);
        Title(title, code == "STE+OLA" ? "Stearic acid (saturated) beside oleic acid (cis double bond)" : BioData.Molecule(code)?.Title ?? "");
        Stage.View.Accessibility = $"3D model of {title.ToLowerInvariant()}, its head up and its tails down";
        var chem = Chem;
        Stage.Show($"lipid|{code}|{look.Key}", () => new BiomolScene { Content = BiomolScenes.Lipid(code, look, chem) }, $"lipid|{code}");
        DrawFacts();
    }

    protected override void DrawFacts()
    {
        Right.Children.Clear();
        var code = BiomoleculeBuilderModel.Lipid;
        var dark = Dark;
        var mol = code == "STE+OLA" ? null : BioData.Molecule(code);
        var rows = new List<UIElement?>();
        if (mol != null) rows.Add(BioUI.FactRow("Formula", $"{BiomolText.Formula(mol.Formula ?? "")} · {BiomolKit.N(mol.Mass ?? 0, "0.00")} g/mol"));
        switch (code)
        {
            case "GOL":
                rows.Add(BioUI.FactRow("What it is", "A three-carbon alcohol with an –OH on each carbon. Fats and phospholipids are built on it: each –OH can join a fatty acid by an ester bond."));
                break;
            case "PLM" or "STE":
                rows.Add(BioUI.FactRow("Saturated", "Every C–C bond in its tail is single, so the chain is straight (and holds as many H as it can)."));
                rows.Add(BioUI.FactRow("Head and tail", "A carboxyl group (–COOH, or –COO⁻ at pH 7) at one end, a hydrocarbon tail at the other."));
                break;
            case "OLA":
            {
                var kink = BiomolLipid.KinkAngle(mol!) ?? 0;
                rows.Add(BioUI.FactRow("Unsaturated", $"One cis double bond, C9=C10, bends the chain by about {BiomolKit.N(kink, "0")}°."));
                rows.Add(BioUI.FactRow("Head and tail", "A carboxyl group (–COOH, or –COO⁻ at pH 7) at one end, a hydrocarbon tail at the other."));
                break;
            }
            case "TGL":
                rows.Add(BioUI.FactRow("Made of", "Glycerol + 3 stearic acids, joined by 3 ester bonds: 3 H₂O released (dehydration synthesis)."));
                rows.Add(BioUI.FactRow("What it is for", "Long-term energy storage: a fat is almost all C–H bonds and does not mix with water."));
                break;
            case "POV":
                rows.Add(BioUI.FactRow("Head", "Hydrophilic: choline (+) and phosphate (−) on glycerol."));
                rows.Add(BioUI.FactRow("Tails", "Hydrophobic: a palmitate (saturated, straight) and an oleate (cis double bond, kinked), each joined to glycerol by an ester bond."));
                rows.Add(BioUI.FactRow("In a membrane", "Two layers, tails inward and heads outward toward the water. Colour by Part shows the head blue and the tails tan."));
                break;
            case "CLR":
                rows.Add(BioUI.FactRow("What it is", "A steroid: four fused rings (three of six carbons, one of five), a short tail and one –OH."));
                rows.Add(BioUI.FactRow("In a membrane", "It sits among the phospholipids' tails with its –OH at the level of their heads, and steadies the membrane's fluidity."));
                break;
        }
        if (code != "STE+OLA") Right.Children.Add(BioUI.Card(BiomolLipid.ShortName(code), K.Hex(BiomolParts.Hydrophobic.Colour.HexFor(dark)), Captions[code], rows.ToArray()));
        if (code is "PLM" or "STE" or "OLA" or "STE+OLA" or "POV" or "TGL")
        {
            var ola = BioData.Molecule("OLA") is { } o ? BiomolLipid.KinkAngle(o) ?? 0 : 0;
            Right.Children.Add(BioUI.Card("Saturated vs unsaturated", K.Hex(BioPalette.Strand.HexFor(dark)), null,
                BioUI.FactRow("Saturated", "No C=C double bonds: straight chains that pack tightly, so the fat is solid at room temperature (butter, lard)."),
                BioUI.FactRow("Unsaturated", $"One or more cis double bonds: each kinks the chain (here {BiomolKit.N(ola, "0")}°), so the chains pack loosely and the fat is liquid (oils)."),
                BioUI.FactRow("Trans fats", "A trans double bond keeps the chain straight, so trans fats pack like saturated ones.")));
        }
        if (PickedAtom(Picked) is { } id)
        {
            var codes = code == "STE+OLA" ? new[] { "STE", "OLA" } : new[] { code };
            var k = Picked!.StartsWith("mol:1:", StringComparison.Ordinal) ? 1 : 0;
            if (k < codes.Length && BioData.Molecule(codes[k]) is { } mol2 && mol2.Model(BiomoleculeBuilderModel.Ph7) is { } m && id < m.Atoms.Count)
                Right.Children.Add(PickedCard($"{BiomolLipid.ShortName(codes[k])} · {m.Atoms[id].Name}", m.Atoms[id].Element, BiomolLipid.Detail(m.Atoms[id].Name, mol2)));
        }
        var items = new List<(Color, string)>();
        if (BiomoleculeBuilderModel.ColourBy == BiomolColourBy.Element)
            items.AddRange(new[] { "C", "H", "O", "N", "P" }.Select(s => (MBChem.ColorOf(s), $"{s} · {MBChem.Element(s)?.Name ?? s}")));
        else
        {
            items.Add((K.Hex(BiomolParts.Hydrophilic.Colour.HexFor(dark)), "Hydrophilic head"));
            items.Add((K.Hex(BiomolParts.Hydrophobic.Colour.HexFor(dark)), "Hydrophobic tail"));
        }
        items.Add((K.Hex(BiomolParts.BondHighlight.HexFor(dark)), "Ester bond (glowing)"));
        items.Add((BioPalette.Strand.ToColor(), "cis double bond (glowing)"));
        Right.Children.Add(K.V(8, K.Text($"Colour by {BiomoleculeBuilderModel.ColourBy.Label().ToLowerInvariant()}", Ty.Caption, B.Text2).Strong(), BioUI.Legend(items)));
    }
}

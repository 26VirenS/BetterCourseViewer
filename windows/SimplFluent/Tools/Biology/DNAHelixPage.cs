using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// DNA & RNA's Double helix page (spec §4.2 Double helix): B-DNA in 3D for the strand on the Sequence page (its first 40
// base pairs) — the cartoon, ball and stick or space filling — with the overlays (the 5′ and 3′ ends with arrowheads
// pointing 5′→3′, the grooves, three measurements, the H-bonds), a press on a base selecting its pair (both nucleotides
// lit, the card saying how they pair) and a press on the backbone explaining the phosphodiester bonds, and the Unzip
// slider opening the helix from its bottom end. The left column holds the strand as a row of pressable base pairs (the
// scene's parts list, for the keyboard and Narrator), the style, the overlays and the slider; the right its facts.

internal sealed class DnaHelixPage : Grid
{
    private readonly BioSceneView scene = new("3D model of B-DNA") { PickSelects = false };
    private readonly TextBlock title = K.Text("", Ty.Subtitle);
    private readonly ContentControl strip = new() { Focusable = false };
    private readonly Stack right = K.V(14);
    private readonly SelectorBar styleBar;
    private readonly ToggleSwitch ends = new(), grooves = new(), measure = new(), hbonds = new();
    private readonly Slider unzip = new() { Minimum = 0, Maximum = 1, SmallChange = 0.05, LargeChange = 0.1 };
    private readonly TextBlock unzipValue = K.Text("", Ty.BodyStrong);
    private readonly Wpf.Ui.Controls.Button unzipButton;
    private readonly DispatcherTimer throttle = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly DispatcherTimer player = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private double pendingUnzip;
    private (double From, double To, DateTime Start)? playing;
    private bool syncing;
    private string? shownOptions, stripKey, cardsKey;
    private readonly List<(Border Edge, System.Windows.Controls.Button Button, string Name)> pairViews = new();
    private int generation;
    private BioDuplex? duplex;
    private string duplexSeq = "";

    public DnaHelixPage()
    {
        styleBar = new SelectorBar(new[] { DnaStyle.Cartoon, DnaStyle.Atoms, DnaStyle.SpaceFill }.Select(s => s.Label()).ToList(), (int)DNAToolModel.Style);
        styleBar.Changed += i => DNAToolModel.Style = (DnaStyle)i;
        Wire(ends, v => DNAToolModel.ShowEnds = v, "Show the 5′ and 3′ ends");
        Wire(grooves, v => DNAToolModel.ShowGrooves = v, "Show the grooves");
        Wire(measure, v => DNAToolModel.ShowMeasure = v, "Show the measurements");
        Wire(hbonds, v => DNAToolModel.ShowHBonds = v, "Show the hydrogen bonds");
        unzip.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            playing = null;
            player.Stop();
            pendingUnzip = e.NewValue;
            if (!throttle.IsEnabled) throttle.Start();
        };
        System.Windows.Automation.AutomationProperties.SetName(unzip, "Unzip the helix");
        unzip.ToolTip = "Open the helix from its bottom end (← → when it has focus)";
        throttle.Tick += (_, _) => { throttle.Stop(); DNAToolModel.Unzip = pendingUnzip; };
        unzipButton = K.Button("Unzip", PlayUnzip, icon: SymbolRegular.Play20);
        unzipButton.MinWidth = 0;
        player.Tick += (_, _) => StepUnzip();
        scene.Picked += id => DNAToolModel.Select(Normalise(id));

        var left = K.V(14,
            BioUI.Heading("Strand 1, 5′→3′"),
            BioUI.Caption("Each column is a base pair. Press one to select it in the model."),
            strip,
            K.Link("Edit the sequence", () => BioNav.Set("dna", "sequence")).Align(HorizontalAlignment.Left),
            BioUI.Heading("Style"),
            styleBar,
            BioUI.Heading("Show"),
            Row("5′ and 3′ ends", ends, "Labels at both ends of both strands, and arrowheads pointing 5′→3′"),
            Row("Grooves", grooves, "The major and minor grooves as see-through bands"),
            Row("Measurements", measure, "2 nm wide, 0.34 nm a base pair, 3.6 nm a turn"),
            Row("Hydrogen bonds", hbonds, "A·T pairs have 2, G·C pairs 3"),
            BioUI.Heading("Unzip"),
            K.Columns("*, auto", unzip.Col(0), unzipValue.Margin(10, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1)),
            K.Columns("auto, *", unzipButton.Col(0), K.Link("See replication", () => BioNav.Set("dna", "replication")).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1)),
            BioUI.Caption("Base pair k of N opens once the slider passes k/N: its two nucleotides move apart along the line between their sugars and its hydrogen bonds fade — as helicase opens the helix at a replication fork."),
            BioUI.Heading("Colours"),
            DnaWpf.BaseLegend());
        var card = DnaWpf.SceneCard(title, BioSceneCard.Buttons(scene), scene, "Drag to turn · scroll to zoom · press a base pair or the backbone");
        Children.Add(BioUI.Work(left, card, right));

        void Changed() => Refresh();
        void Theme() { shownOptions = null; Refresh(); }
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
            throttle.Stop();
            player.Stop();
            playing = null;
        };
        Refresh();
    }

    private static void Wire(ToggleSwitch s, Action<bool> set, string name)
    {
        s.Checked += (_, _) => set(true);
        s.Unchecked += (_, _) => set(false);
        s.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(s, name);
    }

    private static FrameworkElement Row(string text, ToggleSwitch s, string tip)
    {
        var row = K.Columns("*, auto", K.Text(text, Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), s.Col(1));
        row.ToolTip = tip;
        return row;
    }

    /// <summary>A press turned into what is selected: a base or its H-bonds → its pair; the backbone → its nucleotide; an
    /// end's label → its strand's arrowheads; the rest as it is.</summary>
    private static string? Normalise(string? id)
    {
        if (id == null) return null;
        var p = id.Split(':');
        if (p[0] == "bp" && p.Length >= 2)
            return p.Length >= 4 && p[3] == "bb" ? $"bp:{p[1]}:{p[2]}" : $"bp:{p[1]}";
        if (p[0] == "end" && p.Length >= 2) return $"arrow:{p[1]}";
        return id;
    }

    // ---- unzip, played ----

    private void PlayUnzip()
    {
        var from = DNAToolModel.Unzip;
        var to = from >= 0.999 ? 0 : 1;
        if (!Motion.On)
        {
            DNAToolModel.Unzip = to;
            return;
        }
        playing = (from, to, DateTime.UtcNow);
        player.Start();
    }

    private void StepUnzip()
    {
        if (playing is not { } p) { player.Stop(); return; }
        var seconds = 3.2 * Math.Abs(p.To - p.From);
        var t = seconds <= 0 ? 1 : Math.Min(1, (DateTime.UtcNow - p.Start).TotalSeconds / seconds);
        // (quantised a little: the scene is made again at most ~25 times a second)
        DNAToolModel.Unzip = Math.Round((p.From + (p.To - p.From) * t) * 50) / 50;
        if (t >= 1) { playing = null; player.Stop(); Refresh(); }
    }

    // ---- drawing ----

    private void Refresh()
    {
        var seq = DNAToolModel.HelixSequence;
        if (duplex == null || duplexSeq != seq) { duplex = BioBDNA.Build(seq); duplexSeq = seq; }
        var n = seq.Length;
        if (DNAToolModel.Selected is { } sel && DNAToolModel.SelectedPair(sel) is { } k0 && k0 >= n) DNAToolModel.Selected = null;
        syncing = true;
        try
        {
            styleBar.Select((int)DNAToolModel.Style, false);
            if (ends.IsChecked != DNAToolModel.ShowEnds) ends.IsChecked = DNAToolModel.ShowEnds;
            if (grooves.IsChecked != DNAToolModel.ShowGrooves) grooves.IsChecked = DNAToolModel.ShowGrooves;
            if (measure.IsChecked != DNAToolModel.ShowMeasure) measure.IsChecked = DNAToolModel.ShowMeasure;
            if (hbonds.IsChecked != DNAToolModel.ShowHBonds) hbonds.IsChecked = DNAToolModel.ShowHBonds;
            if (!throttle.IsEnabled && Math.Abs(unzip.Value - DNAToolModel.Unzip) > 1e-9) unzip.Value = DNAToolModel.Unzip;
        }
        finally { syncing = false; }
        var open = Enumerable.Range(0, n).Count(k => DnaHelix.Opening(k, n, DNAToolModel.Unzip) > 0);
        unzipValue.Text = $"{open} of {n} open";
        unzipButton.Content = playing != null ? "Unzipping…" : DNAToolModel.Unzip >= 0.999 ? "Zip up" : "Unzip";
        unzipButton.Icon = new SymbolIcon { Symbol = DNAToolModel.Unzip >= 0.999 ? SymbolRegular.ArrowReset20 : SymbolRegular.Play20 };
        unzipButton.ToolTip = DNAToolModel.Unzip >= 0.999 ? "Close the helix again" : "Open the helix from its bottom end, pair by pair";
        title.Text = $"B-DNA · {n} bp · {DNAToolModel.Style.Label()}";

        // (the strip made again only for a new strand or look; a selection just moves its ring, so the keyboard's focus stays)
        var stripNow = $"{seq}|{Appearance.Dark}";
        if (stripNow != stripKey) { stripKey = stripNow; strip.Content = Strip(seq); }
        var selectedPair = DNAToolModel.SelectedPair(DNAToolModel.Selected);
        for (var k = 0; k < pairViews.Count; k++)
        {
            var (edge, button, name) = pairViews[k];
            edge.BorderBrush = selectedPair == k ? K.Brush(Appearance.Accent) : Brushes.Transparent;
            System.Windows.Automation.AutomationProperties.SetName(button, selectedPair == k ? name + ", selected" : name);
        }

        var o = new DnaHelixOptions(seq, DNAToolModel.Style, DNAToolModel.ShowEnds, DNAToolModel.ShowGrooves, DNAToolModel.ShowMeasure,
            DNAToolModel.ShowHBonds, DNAToolModel.Unzip, Appearance.Dark);
        if (o.Key != shownOptions)
        {
            shownOptions = o.Key;
            // (a new key each time the options change — never one already shown — so a change back while another is
            //  being made is made again, not lost)
            generation++;
            var paint = DnaWpf.Paint();
            List<DnaLetter>? letters = null;
            scene.Show($"helix|{o.Key}|{generation}", () =>
            {
                var made = DnaHelix.Build(o, paint);
                letters = made.Letters;
                return made.Content;
            }, frameKey: $"helix|{seq}", billboards: () => DnaWpf.Billboards(letters, o.Dark));
        }
        scene.Selection = DNAToolModel.Selected;
        scene.Accessibility = $"3D model of B-DNA, {n} base pairs, {DNAToolModel.Style.Label().ToLowerInvariant()}; right-handed, 10.5 base pairs a turn, "
                              + $"the strands antiparallel{(open > 0 ? $"; {open} pairs unzipped" : "")}{(SelectionWords() is { } w ? "; selected: " + w : "")}";
        var cardsNow = $"{seq}|{DNAToolModel.Sequence.Length}|{DNAToolModel.Selected}|{Appearance.Dark}";
        if (cardsNow != cardsKey) { cardsKey = cardsNow; Cards(seq); }
    }

    /// <summary>The strand as pressable base pairs: strand 1's base over strand 2's, coloured and lettered.</summary>
    private FrameworkElement Strip(string seq)
    {
        var wrap = new WrapPanel();
        pairViews.Clear();
        for (var k = 0; k < seq.Length; k++)
        {
            var at = k;
            char b1 = seq[k], b2 = BioSeq.Pair(b1, false);
            var face = new StackPanel { Children = { Tile(b1), Tile(b2) } };
            var edge = new Border { Child = face, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(5), Padding = new Thickness(1), BorderBrush = Brushes.Transparent };
            var b = K.Subtle(edge, () => DNAToolModel.Select(DNAToolModel.Selected == $"bp:{at}" ? null : $"bp:{at}"), new Thickness(0), 5);
            b.MinWidth = 0;
            b.MinHeight = 0;
            b.Margin = new Thickness(0, 0, 1, 3);
            b.ToolTip = $"Base pair {k + 1}: {b1}·{b2}";
            var name = $"Base pair {k + 1}, {DnaNucleotides.BaseName(b1)} with {DnaNucleotides.BaseName(b2)}";
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            pairViews.Add((edge, b, name));
            wrap.Children.Add(b);
        }
        return K.V(4, wrap, BioUI.Caption("Read left to right: the top row is strand 1, 5′→3′; strand 2 under it runs 3′→5′."));
    }

    private static Border Tile(char b)
    {
        var colour = BioColors.Base(b);
        var white = DnaWpf.WhiteOn(BioColors.Hex(colour));
        var t = new TextBlock
        {
            Text = $"{b}", FontFamily = BioUI.Mono, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Foreground = white ? Brushes.White : K.Brush(Color.FromRgb(0x1c, 0x1c, 0x1e)),
        };
        return new Border { Width = 18, Height = 17, CornerRadius = new CornerRadius(3), Background = K.Brush(colour), Child = t, Margin = new Thickness(0, 0, 0, 1) };
    }

    private string? SelectionWords()
    {
        var sel = DNAToolModel.Selected;
        if (sel == null) return null;
        var seq = DNAToolModel.HelixSequence;
        if (DNAToolModel.SelectedPair(sel) is { } k && k < seq.Length)
            return DNAToolModel.SelectedStrand(sel) is { } s ? $"strand {s}’s backbone at base pair {k + 1}" : $"base pair {k + 1}, {seq[k]}·{BioSeq.Pair(seq[k], false)}";
        return sel switch
        {
            "groove:major" => "the major groove",
            "groove:minor" => "the minor groove",
            "arrow:1" => "strand 1’s direction",
            "arrow:2" => "strand 2’s direction",
            _ when sel.StartsWith("measure:", StringComparison.Ordinal) => "a measurement",
            _ => null,
        };
    }

    // ---- the right column ----

    private void Cards(string seq)
    {
        right.Children.Clear();
        var n = seq.Length;
        var total = DNAToolModel.Sequence.Length;
        right.Children.Add(BioUI.Facts(new (string, string, Color?)[]
        {
            ("Base pairs", total > n ? $"{n} of {total} drawn" : $"{n}", null),
            ("Per turn", "10.5 bp (34.3° each)", null),
            ("Rise", "0.34 nm a base pair", null),
            ("Pitch", "3.57 nm a turn", null),
            ("Width", "≈ 2 nm", null),
            ("Hand", "Right-handed", null),
            ("Strands", "Antiparallel", null),
            ("GC content", $"{BioSeq.GcContent(seq) * 100:0.#}%", null),
        }));
        right.Children.Add(SelectionCard(seq));
        right.Children.Add(BioUI.Caption("Watson and Crick’s 1953 model, and many textbooks, give 10 bp and 3.4 nm a turn. DNA in solution averages 10.4–10.5 bp a turn, which this model uses (the Arnott B-DNA fiber model, 3.40 Å a base pair)."));
    }

    private FrameworkElement SelectionCard(string seq)
    {
        var sel = DNAToolModel.Selected;
        var n = seq.Length;
        if (sel != null && DNAToolModel.SelectedPair(sel) is { } k && k < n)
        {
            char b1 = seq[k], b2 = BioSeq.Pair(b1, false);
            if (DNAToolModel.SelectedStrand(sel) is { } s)
            {
                var number = s == 1 ? k + 1 : n - k;
                var b = s == 1 ? b1 : b2;
                return BioUI.Card("Sugar–phosphate backbone", (s == 1 ? BioPalette.Strand1 : BioPalette.Strand2).ToColor(),
                    $"Strand {s}, nucleotide {number} from its 5′ end ({b})",
                    BioUI.FactRow("What it is", "Each nucleotide’s sugar (deoxyribose) is joined to the next nucleotide’s phosphate. The chain sugar–phosphate–sugar–phosphate is the backbone; the bases point inward from it."),
                    BioUI.FactRow("Phosphodiester bond", "The oxygen on one sugar’s 3′ carbon (O3′) bonds to the phosphate on the next sugar’s 5′ carbon: 1.58 Å in this model. So every strand has a 5′ end (a phosphate) and a 3′ end (an OH)."),
                    BioUI.FactRow("Direction", s == 1 ? "Strand 1 runs 5′→3′ up the helix; strand 2 runs 5′→3′ down it. The strands are antiparallel." : "Strand 2 runs 5′→3′ down the helix; strand 1 runs 5′→3′ up it. The strands are antiparallel."),
                    PairButtons(k, n));
            }
            List<(string A, string B, double Distance)> bonds = duplex != null ? DnaHelix.PairBonds(duplex, k) : new();
            var lines = string.Join("\n", bonds.Select(x => Bond(b1, x.A, b2, x.B) + $"  ({x.Distance:0.00} Å)"));
            var p1 = DnaNucleotides.IsPurine(b1);
            return BioUI.Card($"Base pair {k + 1} · {b1}·{b2}", BioColors.Base(b1), $"{Cap(DnaNucleotides.BaseName(b1))} (strand 1) with {DnaNucleotides.BaseName(b2)} (strand 2)",
                BioUI.FactRow("Hydrogen bonds", $"{bonds.Count} — {(bonds.Count == 3 ? "G·C pairs have three, so they hold a little tighter" : "A·T pairs have two")}"),
                BioUI.FactRow("Which atoms bond", lines),
                BioUI.FactRow("Purine or pyrimidine", $"{Cap(DnaNucleotides.BaseName(p1 ? b1 : b2))} is a purine (two rings); {DnaNucleotides.BaseName(p1 ? b2 : b1)} is a pyrimidine (one ring). A purine always pairs with a pyrimidine, so every pair is the same width and the helix stays 2 nm wide."),
                BioUI.FactRow("Each nucleotide", "Both are lit: each base with its sugar and phosphate. The sugars’ C1′ atoms are 10.9 Å apart."),
                PairButtons(k, n));
        }
        if (sel is "groove:major" or "groove:minor")
        {
            var major = sel == "groove:major";
            var (minorW, majorW) = DnaHelix.GrooveWidths();
            return BioUI.Card(major ? "Major groove" : "Minor groove", K.Hex(major ? "#e8508a" : "#f2994a"), major ? "The wide side between the backbones" : "The narrow side between the backbones",
                BioUI.FactRow("Where", major ? $"About {DnaHelix.MajorArc:0}° of the way round from one backbone to the other, at the same height." : $"About {DnaHelix.MinorArc:0}° of the way round from one backbone to the other, at the same height."),
                BioUI.FactRow("How wide", $"{(major ? majorW : minorW):0.0} Å in this model, measured phosphate to phosphate less 5.8 Å for the phosphate groups (textbooks: {(major ? "11.7" : "5.7")} Å). Some books quote the raw phosphate–phosphate width instead, about {(major ? "22" : "12")} Å."),
                BioUI.FactRow("Why it matters", major ? "The edges of the base pairs are easiest to tell apart from the major groove, so most proteins that read DNA, such as transcription factors, fit into it." : "Less of each base pair’s edge shows here, so fewer proteins read the sequence from this side."));
        }
        if (sel is "arrow:1" or "arrow:2")
        {
            var s = sel == "arrow:1" ? 1 : 2;
            return BioUI.Card($"Strand {s} · 5′→3′", (s == 1 ? BioPalette.Strand1 : BioPalette.Strand2).ToColor(), s == 1 ? "Its 5′ end at the bottom, its 3′ end at the top" : "Its 5′ end at the top, its 3′ end at the bottom",
                BioUI.FactRow("Antiparallel", "The arrowheads point 5′→3′ along each backbone, and the two strands point opposite ways."),
                BioUI.FactRow("5′ end", "The phosphate on the first sugar’s 5′ carbon."),
                BioUI.FactRow("3′ end", "The OH on the last sugar’s 3′ carbon. New nucleotides are only ever added at the 3′ end, so DNA is built 5′→3′."),
                BioUI.FactRow("Written down", "A strand is written 5′→3′; its partner, written under it, reads 3′→5′."));
        }
        if (sel != null && sel.StartsWith("measure:", StringComparison.Ordinal))
        {
            var (head, body) = sel switch
            {
                "measure:width" => ("2 nm wide", "The double helix is about 2 nm (20 Å) across. A purine always pairs with a pyrimidine, so every base pair is the same width."),
                "measure:rise" => ("0.34 nm between base pairs", "Each base pair sits 0.34 nm (3.4 Å) above the one below it and is turned 34.3° further round."),
                _ => ("≈3.6 nm per turn (10.5 bp)", "10.5 base pairs a turn × 0.34 nm = 3.57 nm. Watson and Crick’s 1953 model had 10 bp and 3.4 nm a turn; DNA in solution averages 10.4–10.5 bp."),
            };
            return BioUI.Card(head, null, "A measurement", BioUI.FactRow("What it shows", body));
        }
        return BioUI.Card("Press a part", null, null,
            BioUI.FactRow("A base pair", "Press a base (or a column at the left) to light both nucleotides and see how the bases pair."),
            BioUI.FactRow("The backbone", "Press the backbone to see how the nucleotides are joined."),
            BioUI.FactRow("Moving round", "Drag to turn, scroll to zoom, double-click a part to zoom to it. Escape lets go."));
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>"A N6–H ··· T O4": the donor (with its H) first.</summary>
    private static string Bond(char b1, string a1, char b2, string a2)
    {
        static bool Donor(char b, string atom) => (b, atom) is ('A', "N6") or ('T', "N3") or ('U', "N3") or ('G', "N1") or ('G', "N2") or ('C', "N4");
        return Donor(b1, a1) ? $"{b1} {a1}–H ··· {b2} {a2}" : $"{b2} {a2}–H ··· {b1} {a1}";
    }

    /// <summary>The previous and next base pair, for the keyboard.</summary>
    private static FrameworkElement PairButtons(int k, int n)
    {
        var back = K.Button("Previous pair", () => DNAToolModel.Select($"bp:{Math.Max(0, k - 1)}"), icon: SymbolRegular.ChevronLeft20);
        var next = K.Button("Next pair", () => DNAToolModel.Select($"bp:{Math.Min(n - 1, k + 1)}"), icon: SymbolRegular.ChevronRight20);
        back.MinWidth = 0;
        next.MinWidth = 0;
        back.IsEnabled = k > 0;
        next.IsEnabled = k < n - 1;
        return K.H(8, back, next);
    }
}

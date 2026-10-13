using System.Globalization;

namespace Simpl.Tools;

// Gene Expression's step-through (spec §4.3 Transcribe & translate, §3.6), the Mac's GeneExpressionProcess.swift: a gene
// made into a protein — the gene; transcription's initiation, a step for each nucleotide and termination; in a eukaryote
// the 5′ cap, the poly-A tail, splicing and export; translation's initiation, a step for each codon and termination; and
// the result. Each step has its title, its explanation (naming the molecule at work) and the scene at its end (a
// GXFrame). Between two steps the view draws GXScene.Make: the two frames mixed, each value in its own part of the move —
// a tRNA comes in, the peptide bond forms, then the ribosome moves on — so the order of events reads in the motion. Plain
// .NET (the unit tests drive it).

/// <summary>What a step is.</summary>
public enum GXStage { Gene, TxInit, TxElong, TxEnd, Cap, Tail, Splice, Export, TlInit, TlElong, TlEnd, NoStart, Result }

public static class GXStages
{
    /// <summary>The stage group the number keys jump to: 1 the gene, 2 transcription, 3 RNA processing, 4 translation, 5 the result.</summary>
    public static int Group(this GXStage s) => s switch
    {
        GXStage.Gene => 1,
        GXStage.TxInit or GXStage.TxElong or GXStage.TxEnd => 2,
        GXStage.Cap or GXStage.Tail or GXStage.Splice or GXStage.Export => 3,
        GXStage.TlInit or GXStage.TlElong or GXStage.TlEnd or GXStage.NoStart => 4,
        _ => 5,
    };

    public static bool IsTranscription(this GXStage s) => s is GXStage.TxInit or GXStage.TxElong or GXStage.TxEnd;
    public static bool IsTranslation(this GXStage s) => s >= GXStage.TlInit && s <= GXStage.NoStart;
}

/// <summary>The scene at the end of a step: every value the drawing needs, as numbers that mix between two steps.</summary>
public record struct GXFrame
{
    public GXFrame() { }

    public GXStage Stage { get; set; } = GXStage.Gene;
    /// <summary>1: the whole gene in view; 0: bases at full size.</summary>
    public double Fit { get; set; }
    /// <summary>The column at the view's centre.</summary>
    public double Focus { get; set; }
    /// <summary>Transcription factors on the TATA box (in a prokaryote, the σ factor with the polymerase).</summary>
    public double Factors { get; set; }
    /// <summary>RNA polymerase on the DNA (0…1), and the column of the last nucleotide it added (TxStart − 1 before the first).</summary>
    public double Polymerase { get; set; }
    public double PolX { get; set; }
    /// <summary>The DNA opened into a bubble round the polymerase.</summary>
    public double Bubble { get; set; }
    /// <summary>Nucleotides of RNA made.</summary>
    public double Made { get; set; }
    /// <summary>The RNA let go of the template (its 3′ end no longer paired in the bubble).</summary>
    public double Released { get; set; }
    /// <summary>A prokaryote's terminator hairpin.</summary>
    public double Hairpin { get; set; }
    public double Cap { get; set; }
    public double Tail { get; set; }
    public double Splice { get; set; }
    /// <summary>The mRNA moved down from the nucleus's lane to the ribosome's.</summary>
    public double Export { get; set; }
    /// <summary>The small subunit, and its centre as a mature-mRNA index (it scans from the cap to the AUG).</summary>
    public double Small { get; set; }
    public double SmallAt { get; set; }
    /// <summary>The large subunit joined.</summary>
    public double Large { get; set; }
    /// <summary>The initiator tRNA in the P site.</summary>
    public double Initiator { get; set; }
    /// <summary>The codon in the P site (0 = the start codon).</summary>
    public double PSite { get; set; }
    /// <summary>Amino acids in the chain.</summary>
    public int Chain { get; set; }
    /// <summary>A release factor in the A site.</summary>
    public double ReleaseFactor { get; set; }
    /// <summary>The polypeptide let go and the ribosome's subunits apart.</summary>
    public double Freed { get; set; }
    /// <summary>The result laid out.</summary>
    public double Result { get; set; }

    /// <summary>Two frames mixed <paramref name="t"/> of the way from <paramref name="a"/> to the later <paramref name="b"/>,
    /// each value in its own part of the move (where b is going decides which): at termination the RNA lets go before the
    /// polymerase leaves; at translation's start the small subunit binds and scans, then the initiator tRNA comes, then the
    /// large subunit joins.</summary>
    public static GXFrame Mix(GXFrame a, GXFrame b, double t)
    {
        double W(double t0, double t1) => Math.Min(Math.Max((t - t0) / (t1 - t0), 0), 1);
        double pFit = t, pFocus = t, pRelease = t, pPol = t, pBubble = t, pExport = t, pSmall = t, pSmallAt = t;
        double pInit = t, pLarge = t, pRF = t, pFreed = t, pHairpin = t;
        switch (b.Stage)
        {
            case GXStage.TxEnd:
                pRelease = W(0, 0.5);
                pHairpin = W(0, 0.5);
                pPol = W(0.3, 1);
                pBubble = W(0.3, 1);
                break;
            case GXStage.TlInit:
                pExport = W(0, 0.25);
                pSmall = W(0.2, 0.3);
                pSmallAt = W(0.3, 0.6);
                pInit = W(0.6, 0.8);
                pLarge = W(0.8, 1);
                pFit = W(0, 0.4);
                pFocus = W(0, 0.6);
                break;
            case GXStage.TlEnd:
                pRF = W(0, 0.35);
                pFreed = W(0.65, 1);
                break;
        }
        static double M(double x, double y, double p) => x + (y - x) * p;
        return new GXFrame
        {
            Stage = t < 0.5 ? a.Stage : b.Stage,
            Fit = M(a.Fit, b.Fit, pFit),
            Focus = M(a.Focus, b.Focus, pFocus),
            Factors = M(a.Factors, b.Factors, pPol),
            Polymerase = M(a.Polymerase, b.Polymerase, pPol),
            PolX = M(a.PolX, b.PolX, pPol),
            Bubble = M(a.Bubble, b.Bubble, pBubble),
            Made = M(a.Made, b.Made, t),
            Released = M(a.Released, b.Released, pRelease),
            Hairpin = M(a.Hairpin, b.Hairpin, pHairpin),
            Cap = M(a.Cap, b.Cap, t),
            Tail = M(a.Tail, b.Tail, t),
            Splice = M(a.Splice, b.Splice, t),
            Export = M(a.Export, b.Export, pExport),
            Small = M(a.Small, b.Small, pSmall),
            SmallAt = M(a.SmallAt, b.SmallAt, pSmallAt),
            Large = M(a.Large, b.Large, pLarge),
            Initiator = M(a.Initiator, b.Initiator, pInit),
            PSite = M(a.PSite, b.PSite, t),
            Chain = t < 0.5 ? a.Chain : b.Chain,
            ReleaseFactor = M(a.ReleaseFactor, b.ReleaseFactor, pRF),
            Freed = M(a.Freed, b.Freed, pFreed),
            Result = M(a.Result, b.Result, t),
        };
    }
}

/// <summary>A gene's whole step-through: its steps (for BioStepController) and each step's frame.</summary>
public sealed class GXProcess
{
    public GXGene Gene { get; }
    public IReadOnlyList<BioStep> Steps { get; }
    public IReadOnlyList<GXFrame> Frames { get; }

    /// <summary>A poly-A tail is 50–250 A long; this many are drawn, then "…".</summary>
    public const int TailDrawn = 20;
    /// <summary>The step-through takes this many nucleotides at most (§3.7), and says so.</summary>
    public const int Cap = 600;
    /// <summary>In a bacterium, a ribosome is sketched on the RNA once this many nucleotides past the start codon's A are
    /// made (the start codon then clear of the polymerase).</summary>
    public const int CouplingLag = 13;
    /// <summary>The number keys' groups.</summary>
    public static readonly IReadOnlyDictionary<int, string> Groups = new Dictionary<int, string>
    {
        [1] = "The gene", [2] = "Transcription", [3] = "RNA processing", [4] = "Translation", [5] = "Result",
    };

    public GXProcess(GXGene g)
    {
        Gene = g;
        var steps = new List<BioStep>();
        var frames = new List<GXFrame>();
        void Add(string title, string text, GXFrame fr, params string[] notice)
        {
            steps.Add(new BioStep(steps.Count, title, text, fr.Stage.Group(), notice));
            frames.Add(fr);
        }
        var euk = g.Cell == GXCell.Eukaryote;
        var pol = euk ? "RNA polymerase II" : "RNA polymerase";
        var n = g.PreMRNA.Length;
        var span = g.ColumnSpan;
        var tata = g.Marks.FirstOrDefault(m => m.Kind == GXRegionKind.Tata);
        const string sameAsCoding = "Only the template strand is read; the mRNA will match the coding strand, with U for T.";
        Steps = steps;
        Frames = frames;

        // 1. The gene
        var f = new GXFrame
        {
            Stage = GXStage.Gene, Fit = 1, Focus = (span.Lo + span.Hi) / 2, PolX = g.TxStart - 1,
            SmallAt = euk ? -2 : (g.CdsStart ?? 0) - 7,
        };
        var geneText = g.Source switch
        {
            GXSource.Teaching => euk
                ? $"A made-up teaching gene: a promoter with its TATA box, the start site (+1), two exons, one intron and a poly-A signal. {sameAsCoding}"
                : $"A made-up teaching gene as a bacterium would have it: a promoter, the start site (+1) and one coding sequence, with no intron. {sameAsCoding}",
            GXSource.Hbb => $"Human β-globin's coding sequence, {n} nt: the real gene's 3 exons joined (its promoter and 2 introns are not shown). {sameAsCoding}",
            _ => $"Your sequence, {n} nt, read as the coding strand and transcribed whole from +1 (no promoter shown). {sameAsCoding}",
        };
        Add("The gene", geneText, f, "regions");

        // 2. Initiation of transcription
        f.Stage = GXStage.TxInit;
        f.Fit = 0;
        f.Factors = 1;
        f.Polymerase = 1;
        f.Bubble = 1;
        f.Focus = tata != null ? (tata.Range.Lo + g.TxStart) / 2.0 + 2 : g.TxStart + 6;
        const string bubble = "The DNA opens into a bubble about 12–14 bp wide at the start site (+1).";
        string initText;
        if (euk)
            initText = tata != null
                ? $"Transcription factors bind the TATA box, and RNA polymerase II binds the promoter. {bubble}"
                : $"Transcription factors and RNA polymerase II bind the promoter (not shown here). {bubble}";
        else
            initText = $"RNA polymerase, led by its σ factor, binds the promoter (its −35 and −10 boxes{(g.Source == GXSource.Teaching ? "" : ", not shown")}). {bubble}";
        if (tata != null) Add("Initiation", initText, f, "tata", "plus1"); else Add("Initiation", initText, f, "plus1");

        // 3. Elongation, a nucleotide a step
        int? startPre = g.CdsStart is { } cs ? g.MatureToPre[cs] : null;
        int? stopPre = g.HasStop && g.CdsStart is { } s1 && g.Codons.Count > 0 ? g.MatureToPre[s1 + g.Codons[^1].Index * 3] : null;
        int? polyA = g.Marks.FirstOrDefault(m => m.Kind == GXRegionKind.PolyA) is { } pa ? pa.Range.Lo - g.TxStart : null;
        var intronStarts = g.Introns.Select(s => s.Lo - g.TxStart).ToHashSet();
        int? coupled = !euk && startPre is { } sp ? sp + CouplingLag : null;
        for (var k = 1; k <= n; k++)
        {
            var i = k - 1;
            var col = g.TxStart + i;
            var tb = g.Template[col];
            var rb = g.PreMRNA[i];
            var reads = $"{pol} reads the template's {tb} and adds {rb}";
            string text;
            string[] notice = Array.Empty<string>();
            if (k == 1)
            {
                text = $"{pol} starts the RNA at +1: it reads the template's {tb} and lays down {rb}, the RNA's 5′ end. Each new nucleotide joins the RNA's 3′ end, so the RNA grows 5′→3′.";
                notice = new[] { "plus1" };
            }
            else if (intronStarts.Contains(i))
            {
                text = $"{reads}: the intron begins (GU…). It is copied into the pre-mRNA too, and cut out later.";
                notice = new[] { "intron" };
            }
            else if (i == startPre)
            {
                text = $"{reads}, the first base of the start codon (AUG). Nothing is translated yet: this is still transcription.";
                notice = new[] { "start" };
            }
            else if (i == stopPre)
            {
                text = $"{reads}, the first base of the stop codon. RNA polymerase does not stop here: only ribosomes read codons.";
                notice = new[] { "stop" };
            }
            else if (i == polyA)
            {
                text = $"{reads}: AAUAAA, the poly-A signal, is being copied. It marks where the RNA will be cut.";
                notice = new[] { "polyA" };
            }
            else if (k == coupled)
            {
                text = $"{reads}. In bacteria a ribosome can already bind this RNA and start translating while transcription goes on.";
                notice = new[] { "coupled" };
            }
            else
            {
                text = (k % 3) switch
                {
                    0 => $"{reads} opposite it, at the RNA's 3′ end. It moves along the template 3′→5′, so the RNA grows 5′→3′.",
                    1 => $"A free nucleotide, {rb}TP, pairs with the template's {tb}, and {pol} joins it to the RNA's 3′ end. Behind the polymerase the DNA closes and the RNA peels away.",
                    _ => $"{reads}. The new RNA matches the coding strand above ({g.Coding[col]} there), with U for T.",
                };
            }
            f.Stage = GXStage.TxElong;
            f.Made = k;
            f.PolX = col;
            f.Focus = col;
            Add($"Elongation · nucleotide {k} of {n}", text, f, notice);
        }

        // 4. Termination of transcription
        f.Stage = GXStage.TxEnd;
        f.Released = 1;
        f.Polymerase = 0;
        f.Bubble = 0;
        f.Factors = 0;
        f.PolX += 5;
        f.Hairpin = euk ? 0 : 1;
        f.Focus = g.TxEnd - 6;
        string termText;
        if (!euk)
            termText = "The RNA folds into a terminator hairpin (drawn as a sketch), which makes RNA polymerase let go. The RNA and the DNA separate, and the DNA closes.";
        else if (polyA != null)
            termText = "Past the AAUAAA poly-A signal the RNA is cut (in real genes 10–35 nt past it) and let go. RNA polymerase II falls off and the DNA closes. This pre-mRNA still has its intron.";
        else
            termText = "The RNA is cut past a poly-A signal (after the part shown) and let go. RNA polymerase II falls off and the DNA closes.";
        if (euk) Add("Termination", termText, f, "polyA"); else Add("Termination", termText, f);

        // 5–8. RNA processing (eukaryote)
        if (euk)
        {
            f.Stage = GXStage.Cap;
            f.Cap = 1;
            f.Focus = g.TxStart + 8;
            Add("5′ cap", "A 7-methylguanosine (m⁷G) cap is added to the RNA's 5′ end, joined 5′ to 5′. It protects the mRNA, and it is where the ribosome's small subunit will bind.", f, "cap");

            f.Stage = GXStage.Tail;
            f.Tail = 1;
            f.Focus = g.Column(n - 1) + 6;
            Add("Poly-A tail", $"Poly-A polymerase adds 50–250 A's to the 3′ end, with no DNA template (about 200; {TailDrawn} are drawn). The tail protects the mRNA and helps it leave the nucleus.", f, "tail");

            f.Stage = GXStage.Splice;
            f.Splice = 1;
            string spliceText;
            if (g.Junctions.Count > 0)
            {
                f.Focus = g.ColumnOfMature(g.Junctions[0]);
                if (g.SplitCodon is { } sc)
                {
                    var cut = sc.Codon[..sc.Cut] + " | " + sc.Codon[sc.Cut..];
                    spliceText = $"The spliceosome cuts out the intron (it begins GU and ends AG) and joins exon 1 to exon 2. Codon {sc.Index + 1}, {sc.Codon}, is split across the junction ({cut}), so splicing must be exact to the base.";
                }
                else spliceText = "The spliceosome cuts out the intron (it begins GU and ends AG) and joins the exons on either side, exact to the base.";
            }
            else
            {
                f.Fit = 1;
                f.Focus = g.ColumnOfMature(g.Mature.Length / 2.0);
                spliceText = g.Source == GXSource.Hbb
                    ? "The real HBB gene has 3 exons and 2 introns, and the spliceosome cuts the introns out at this step. This tool shows only the coding sequence, so here there is nothing to cut."
                    : "Your sequence has no intron marked, so the spliceosome has nothing to cut. Most human genes have several introns removed at this step.";
            }
            Add("Splicing", spliceText, f, "gu-ag", "junction");

            f.Stage = GXStage.Export;
            f.Export = 1;
            f.Fit = 1;
            f.Focus = g.ColumnOfMature(g.Mature.Length / 2.0);
            Add("Export", "The mature mRNA (cap, exons and tail) leaves the nucleus through a nuclear pore. In the cytoplasm, ribosomes will read it.", f);
        }

        // 9–11. Translation
        var protein = g.Protein;
        if (g.CdsStart is not { } s0 || protein.Length == 0)
        {
            f.Stage = GXStage.NoStart;
            f.Export = 1;
            f.Fit = 1;
            f.Focus = g.ColumnOfMature(g.Mature.Length / 2.0);
            Add("No start codon", "This mRNA has no AUG, so a ribosome has nowhere to start and no protein is made. Try a sequence with ATG (DNA) or AUG (mRNA) in it.", f);
            f.Stage = GXStage.Result;
            f.Result = 1;
            Add("Result", "No protein: without a start codon there is no reading frame to translate.", f);
            return;
        }
        f.Stage = GXStage.TlInit;
        f.Export = 1;
        f.Fit = 0;
        f.Small = 1;
        f.SmallAt = s0 + 1;
        f.Large = 1;
        f.Initiator = 1;
        f.PSite = 0;
        f.Chain = 1;
        f.Focus = g.CodonColumn(0) + 1.5;
        string initTl;
        if (euk)
        {
            var kozak = g.Kozak != null ? " (in a Kozak context, GCCACCAUGG)" : "";
            initTl = $"The small subunit binds the 5′ cap and scans to the first AUG{kozak}. The initiator tRNA (anticodon 3′-UAC-5′, carrying Met) pairs with AUG in the P site, then the large subunit joins.";
        }
        else initTl = "The small subunit binds the Shine–Dalgarno sequence just upstream of the AUG (bacteria have no cap). The initiator tRNA (anticodon 3′-UAC-5′, carrying formyl-Met) pairs in the P site, then the large subunit joins.";
        if (euk) Add("Translation initiation", initTl, f, g.Kozak != null ? "kozak" : "start");
        else Add("Translation initiation", initTl, f, "sd");

        var sense = g.Codons.Where(c => c.Aa != '*').ToList();
        for (var j = 1; j < sense.Count; j++)
        {
            var c = sense[j];
            var three = BioAminoAcids.ByOne.TryGetValue(c.Aa, out var aa) ? aa.Three : c.Aa.ToString();
            var ac = BioCode.Anticodon3To5(c.Codon);
            string text;
            if (j == 1)
                text = $"A tRNA with anticodon 3′-{ac}-5′ brings {three} to codon {c.Codon} in the A site. A peptide bond joins it to Met (the large subunit's rRNA catalyses it); then the ribosome moves one codon toward 3′ and the empty tRNA moves to the E site.";
            else if (c.Aa == 'M')
                text = "Codon AUG in the A site: inside a gene, AUG simply codes Met. Its tRNA (anticodon 3′-UAC-5′) brings it, a peptide bond joins it to the chain, and the ribosome moves on one codon.";
            else
                text = $"Codon {c.Codon} in the A site: the tRNA with anticodon 3′-{ac}-5′ brings {three}. A peptide bond adds it to the chain's C-terminal end, and the ribosome moves on one codon (3 nt) toward 3′.";
            f.Stage = GXStage.TlElong;
            f.PSite = j;
            f.Chain = j + 1;
            f.Focus = g.CodonColumn(j) + 1.5;
            Add($"Elongation · codon {j + 1}: {c.Codon} → {three}", text, f);
        }

        f.Stage = GXStage.TlEnd;
        if (g.HasStop)
        {
            var stop = g.Codons[^1];
            var name = BioCode.StopNames.TryGetValue(stop.Codon, out var nm) ? $" (“{nm}”)" : "";
            f.ReleaseFactor = 1;
            f.Freed = 1;
            f.Focus = g.CodonColumn(stop.Index) - 1;
            Add("Termination", $"Stop codon {stop.Codon}{name} reaches the A site. A release factor, a protein and not a tRNA, binds it; the polypeptide is let go and the ribosome's subunits separate.", f, "stop");
        }
        else Add("No stop codon", "The mRNA ends before any stop codon, so no release factor comes and the ribosome stalls at the end. Cells destroy such mRNAs; the chain is shown as far as it got.", f);

        // 12. The result
        f.Stage = GXStage.Result;
        f.Result = 1;
        f.ReleaseFactor = 0;
        if (g.HasStop)
        {
            f.Small = 0;
            f.Large = 0;
        }
        f.Focus = g.CodonColumn((sense.Count - 1) / 2.0);
        var shown = protein.Length > 30 ? protein[..30] + "…" : protein;
        var met = euk ? "AUG started it and also coded its first Met (many proteins later lose it)." : "AUG started it and coded its first amino acid (formyl-Met in bacteria).";
        Add("Result", $"The protein, N- to C-terminus: {shown} ({protein.Length} amino acid{(protein.Length == 1 ? "" : "s")}). {met}", f);
    }

    /// <summary>The steps of a stage, if it has any (the elongation runs are what Run and Skip work on).</summary>
    public (int Lo, int Hi)? StepsOf(GXStage stage)
    {
        var a = -1;
        var b = -1;
        for (var i = 0; i < Frames.Count; i++)
            if (Frames[i].Stage == stage)
            {
                if (a < 0) a = i;
                b = i;
            }
        return a < 0 ? null : (a, b);
    }

    /// <summary>The protein made so far at a step (N→C).</summary>
    public string ChainAt(int i) => i >= 0 && i < Frames.Count ? Gene.Protein[..Math.Min(Frames[i].Chain, Gene.Protein.Length)] : "";

    /// <summary>The RNA made so far at a step (5′→3′).</summary>
    public string RnaAt(int i) => i >= 0 && i < Frames.Count ? Gene.PreMRNA[..Math.Min((int)Frames[i].Made, Gene.PreMRNA.Length)] : "";
}

// ---- the scene between two steps ----

/// <summary>A tRNA in the ribosome's lane.</summary>
/// <param name="Codon">The codon it pairs with (its anticodon under it).</param>
/// <param name="Lift">0 docked on its codon … 1 far above (coming in or leaving).</param>
/// <param name="Charged">Carries its amino acid (or the chain) on top.</param>
public readonly record struct GXTRNA(int Codon, double Lift, double Alpha, bool Charged, bool Initiator);

/// <summary>An amino acid of the growing chain.</summary>
/// <param name="Index">0 = the N-terminal Met.</param>
/// <param name="X">The codon it is drawn over (it came from that codon).</param>
/// <param name="OnTRNA">0 in the chain's row; 1 on top of its tRNA.</param>
/// <param name="Bonded">Joined to the one before it by a peptide bond (drawn as a line), 0…1.</param>
public readonly record struct GXResidue(int Index, char Aa, double X, double OnTRNA, double Alpha, double Bonded);

/// <summary>
/// What the view draws at a moment: a frame (two steps' frames mixed), and the translation's pieces worked out from it
/// — the tRNAs in their sites, the chain's amino acids, the release factor — and the nucleotide on its way in.
/// </summary>
public sealed class GXScene
{
    public GXFrame Frame;
    /// <summary>The P site's codon (fractional while the ribosome moves).</summary>
    public double Ribosome;
    public List<GXTRNA> Trnas = new();
    public List<GXResidue> Residues = new();
    /// <summary>The release factor's way in: 1 away … 0 in the A site; null when there is none.</summary>
    public double? ReleaseLift;
    /// <summary>The nucleotide on its way in (its pre-mRNA index), and how far it has come.</summary>
    public int? Incoming;
    public double IncomingProgress;

    /// <summary>The same scene, value for value (a move played back is the move played forward).</summary>
    public bool SameAs(GXScene o) => Frame == o.Frame && Ribosome == o.Ribosome && Trnas.SequenceEqual(o.Trnas) && Residues.SequenceEqual(o.Residues)
        && ReleaseLift == o.ReleaseLift && Incoming == o.Incoming && IncomingProgress == o.IncomingProgress;

    /// <summary>The scene at rest at a step.</summary>
    public static GXScene Rest(GXProcess p, int i)
    {
        var f = p.Frames[Math.Max(0, Math.Min(i, p.Frames.Count - 1))];
        var protein = p.Gene.Protein;
        var at = (int)Math.Round(f.PSite, MidpointRounding.AwayFromZero);
        var trnas = new List<GXTRNA>();
        var residues = new List<GXResidue>();
        double? rf = null;
        void Row(int upTo, bool onTRNA)
        {
            for (var k = 0; k < Math.Min(upTo, protein.Length); k++)
                residues.Add(new GXResidue(k, protein[k], k, onTRNA && k == upTo - 1 ? 1 : 0, 1, k > 0 ? 1 : 0));
        }
        switch (f.Stage)
        {
            case GXStage.TlInit:
                trnas.Add(new GXTRNA(0, 0, 1, true, true));
                Row(1, true);
                break;
            case GXStage.TlElong:
                trnas.Add(new GXTRNA(at, 0, 1, true, false));
                if (at >= 1) trnas.Add(new GXTRNA(at - 1, 0.15, 0.55, false, at == 1));
                Row(at + 1, true);
                break;
            case GXStage.TlEnd:
                if (p.Gene.HasStop)
                {
                    rf = 0;
                    Row(protein.Length, false);
                }
                else
                {
                    trnas.Add(new GXTRNA(at, 0, 1, true, at == 0));
                    if (at >= 1) trnas.Add(new GXTRNA(at - 1, 0.15, 0.55, false, at == 1));
                    Row(at + 1, true);
                }
                break;
            case GXStage.Result:
                Row(protein.Length, false);
                break;
        }
        return new GXScene { Frame = f, Ribosome = f.PSite, Trnas = trnas, Residues = residues, ReleaseLift = rf };
    }

    /// <summary>The scene <paramref name="t"/> of the way between two neighbouring steps (either way: back plays the move in reverse).</summary>
    public static GXScene Make(GXProcess p, int a, int b, double t)
    {
        int lo = Math.Max(0, Math.Min(Math.Min(a, b), p.Frames.Count - 1)), hi = Math.Max(0, Math.Min(Math.Max(a, b), p.Frames.Count - 1));
        var u = Math.Min(Math.Max(a <= b ? t : 1 - t, 0), 1);
        if (lo == hi || u >= 1) return Rest(p, hi);
        if (u <= 0) return Rest(p, lo);
        GXFrame fa = p.Frames[lo], fb = p.Frames[hi];
        var s = Rest(p, hi);
        s.Frame = GXFrame.Mix(fa, fb, u);
        var protein = p.Gene.Protein;
        double W(double t0, double t1) => Math.Min(Math.Max((u - t0) / (t1 - t0), 0), 1);
        static double Ease(double x) => 1 - Math.Pow(1 - x, 3);
        var pp = (int)Math.Round(fa.PSite, MidpointRounding.AwayFromZero);
        switch ((fa.Stage, fb.Stage))
        {
            case (GXStage.TxInit, GXStage.TxElong) or (GXStage.TxElong, GXStage.TxElong):
                // (the next nucleotide drops in from the free NTPs)
                s.Incoming = (int)fa.Made;
                s.IncomingProgress = u;
                break;
            case (GXStage.TlInit, GXStage.TlElong) or (GXStage.TlElong, GXStage.TlElong):
            {
                // (a) the charged tRNA comes into the A site; (b) the peptide bond moves the chain onto its amino acid;
                // (c) the ribosome moves one codon on, the empty tRNA now in the E site; the E site's old tRNA leaves in (a)
                double u1 = W(0, 0.35), u2 = W(0.35, 0.65), u3 = W(0.65, 1);
                var ts = new List<GXTRNA>
                {
                    new(pp + 1, 1 - Ease(u1), u1, true, false),
                    new(pp, 0.15 * u3, 1 - 0.45 * u3, u2 < 0.5, pp == 0),
                };
                if (pp >= 1) ts.Add(new GXTRNA(pp - 1, 0.15 + 0.85 * u1, 0.55 * (1 - u1), false, pp == 1));
                s.Trnas = ts;
                var rs = new List<GXResidue>();
                for (var k = 0; k <= Math.Min(pp + 1, protein.Length - 1); k++)
                {
                    if (k < pp) rs.Add(new GXResidue(k, protein[k], k, 0, 1, k > 0 ? 1 : 0));
                    else if (k == pp) rs.Add(new GXResidue(k, protein[k], k, 1 - u2, 1, k > 0 ? 1 : 0));
                    else rs.Add(new GXResidue(k, protein[k], k, 1, u1, u2));
                }
                s.Residues = rs;
                s.Ribosome = fa.PSite + u3;
                var fr = s.Frame;
                fr.Chain = u2 < 0.5 ? fa.Chain : fb.Chain;
                s.Frame = fr;
                break;
            }
            case (GXStage.TlInit, GXStage.TlEnd) or (GXStage.TlElong, GXStage.TlEnd):
                if (p.Gene.HasStop)
                {
                    // (a release factor into the A site; the chain let go; the subunits part and the last tRNA leaves)
                    double u1 = W(0, 0.35), u2 = W(0.35, 0.65), u3 = W(0.65, 1);
                    s.ReleaseLift = 1 - Ease(u1);
                    var ts = new List<GXTRNA> { new(pp, u3, 1 - u3, u2 < 0.5, pp == 0) };
                    if (pp >= 1) ts.Add(new GXTRNA(pp - 1, 0.15 + 0.85 * u1, 0.55 * (1 - u1), false, pp == 1));
                    s.Trnas = ts;
                    s.Residues = Enumerable.Range(0, Math.Min(pp + 1, protein.Length))
                        .Select(k => new GXResidue(k, protein[k], k, k == pp ? 1 - u2 : 0, 1, k > 0 ? 1 : 0)).ToList();
                }
                else
                {
                    s = Rest(p, lo);
                    s.Frame = GXFrame.Mix(fa, fb, u);
                }
                s.Ribosome = fa.PSite;
                break;
            case (_, GXStage.TlInit):
            {
                // (the initiator tRNA with its Met comes into the P site as the small subunit reaches the AUG)
                var ini = s.Frame.Initiator;
                s.Trnas = new List<GXTRNA> { new(0, 1 - Ease(ini), ini, true, true) };
                s.Residues = protein.Length == 0 ? new List<GXResidue>() : new List<GXResidue> { new(0, protein[0], 0, 1, ini, 0) };
                break;
            }
            case (GXStage.TlEnd, GXStage.Result):
                s.Residues = Enumerable.Range(0, protein.Length).Select(k => new GXResidue(k, protein[k], k, 0, 1, k > 0 ? 1 : 0)).ToList();
                if (p.Gene.HasStop) s.ReleaseLift = W(0, 0.5);
                break;
        }
        return s;
    }
}

// ---- the self-check ----

public static partial class BioSelfCheck
{
    public static void GeneProcessChecks(BioCheckReport r)
    {
        var g = GXGene.Teaching(GXCell.Eukaryote);
        var p = new GXProcess(g);
        var t = BioGenes.Teaching;
        var tx = p.StepsOf(GXStage.TxElong);
        var tl = p.StepsOf(GXStage.TlElong);
        static int Count((int Lo, int Hi)? r) => r is { } x ? x.Hi - x.Lo + 1 : 0;
        r.Check("gx.steps.teaching", $"{p.Steps.Count}",
            p.Steps.Count == 99 && Count(tx) == 81 && Count(tl) == 8 && p.Steps.All(s => s.Title.Length <= 40)
            && p.Frames.Select(f => f.Stage.Group()).SequenceEqual(p.Steps.Select(s => s.Group ?? 0)) && p.Steps[^1].Group == 5);
        // (one nucleotide a step, each the template base's partner, joined at the 3′ end; at the end, the pre-mRNA)
        var each = true;
        if (tx is { } txr)
            for (var i = txr.Lo; i <= txr.Hi; i++)
            {
                var k = (int)p.Frames[i].Made;
                var col = g.TxStart + k - 1;
                each = each && p.Frames[i].PolX == col && g.PreMRNA[k - 1] == BioSeq.Pair(g.Template[col], rna: true) && p.RnaAt(i).Length == k;
            }
        var lastTx = tx?.Hi ?? 0;
        r.Check("gx.steps.transcription", $"{p.RnaAt(lastTx).Length}nt", each && p.RnaAt(lastTx) == t.PreMRNA);
        // (processing in a eukaryote: cap, tail, splicing, export, in that order)
        var order = p.Frames.Select(f => f.Stage).Where(s => s is GXStage.Cap or GXStage.Tail or GXStage.Splice or GXStage.Export).ToList();
        r.Check("gx.steps.processing", string.Join(",", order.Select(s => (int)s)),
            order.SequenceEqual(new[] { GXStage.Cap, GXStage.Tail, GXStage.Splice, GXStage.Export }));
        // (one codon a step from the AUG; the chain at each step; MAFSDWQKG at the end, and termination by a release factor)
        var lastTl = tl?.Hi ?? 0;
        var endIdx = p.StepsOf(GXStage.TlEnd)?.Lo ?? 0;
        var tlLo = tl?.Lo ?? 0;
        r.Check("gx.steps.translation", p.ChainAt(lastTl),
            p.ChainAt(lastTl) == t.Protein && p.ChainAt((tl?.Lo ?? 1) - 1) == "M" && p.Frames[endIdx].ReleaseFactor == 1
            && p.Steps[endIdx].Text.Contains("UAA") && p.Steps[^1].Text.Contains("MAFSDWQKG")
            && p.Steps[tlLo].Title == "Elongation · codon 2: GCC → Ala");
        // (the scene: the initiator in the P site; during a codon's step the tRNA comes in first, the peptide bond next,
        // the move last; back is the same move in reverse)
        var e = tlLo;
        var first = GXScene.Rest(p, e - 1);
        var s1 = GXScene.Make(p, e, e + 1, 0.2);
        var s2 = GXScene.Make(p, e, e + 1, 0.5);
        var s3 = GXScene.Make(p, e, e + 1, 0.75);
        var back = GXScene.Make(p, e + 1, e, 0.25);
        var rest = GXScene.Rest(p, e + 1);
        r.Check("gx.scene.elongation", string.Format(CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2}", s1.Ribosome, s2.Ribosome, s3.Ribosome),
            first.Trnas.FirstOrDefault().Initiator && first.Residues.Select(x => x.Aa).SequenceEqual("M") && first.Residues.FirstOrDefault().OnTRNA == 1
            && s1.Ribosome == 1 && s1.Trnas.Count > 0 && s1.Trnas[0].Alpha > 0 && s1.Trnas[0].Alpha < 1 && s1.Residues.Count > 0 && s1.Residues[^1].Bonded == 0
            && s2.Ribosome == 1 && (s2.Residues.Where(x => x.Index == 1).Select(x => (double?)x.OnTRNA).FirstOrDefault() ?? 0) < 1 && s3.Ribosome > 1 && s3.Ribosome < 2
            && back.SameAs(s3) && rest.Ribosome == 2 && rest.Trnas.Select(x => x.Codon).SequenceEqual(new[] { 2, 1 }) && rest.Residues.Select(x => x.Aa).SequenceEqual("MAF")
            && rest.Residues.Where(x => x.OnTRNA == 1).Select(x => x.Index).SequenceEqual(new[] { 2 }));
        var nt = GXScene.Make(p, 2, 3, 0.4);
        r.Check("gx.scene.transcription", $"{nt.Incoming ?? -1}", nt.Incoming == 1 && Math.Abs(nt.IncomingProgress - 0.4) < 1e-9 && GXScene.Rest(p, 3).Incoming == null);
        // (a prokaryote: no processing, the intron left out of the DNA, the same protein)
        var pk = new GXProcess(GXGene.Teaching(GXCell.Prokaryote));
        r.Check("gx.steps.prokaryote", $"{pk.Steps.Count}",
            pk.Steps.Count == 1 + 1 + 54 + 1 + 1 + 8 + 1 + 1 && !pk.Frames.Any(f => f.Stage.Group() == 3)
            && pk.Steps[^1].Text.Contains("MAFSDWQKG") && pk.Steps.Any(s => s.Notices.SequenceEqual(new[] { "coupled" })));
        // (β-globin: 444 nucleotides, then 147 codons; a sequence without AUG makes nothing)
        var h = new GXProcess(GXGene.Hbb(GXCell.Eukaryote));
        r.Check("gx.steps.HBB", $"{h.Steps.Count}", Count(h.StepsOf(GXStage.TxElong)) == 444 && Count(h.StepsOf(GXStage.TlElong)) == 146
            && h.ChainAt(h.StepsOf(GXStage.TlElong)?.Hi ?? 0) == BioGenes.HbbProtein && h.Steps.All(s => s.Title.Length <= 40));
        var none = GXGene.Own("CCCGGGTTT", GXCell.Eukaryote) is { } ng ? new GXProcess(ng) : null;
        r.Check("gx.steps.no-start", none != null ? $"{none.Steps.Count}" : "nil",
            none != null && none.Steps.Any(s => s.Title == "No start codon") && none.Frames[^1].Stage == GXStage.Result);
    }
}

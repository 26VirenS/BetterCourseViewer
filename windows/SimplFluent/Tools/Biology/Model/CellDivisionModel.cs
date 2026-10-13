using System.Globalization;
using System.Text.RegularExpressions;

namespace Simpl.Tools;

// Cell Division's own model (spec §4.6, §5.2), the Mac's CellDivisionModel.swift: mitosis and meiosis step by step for
// the model organism (2n = 4: a long pair of 7 units and a short pair of 4, one homolog of each from the mother, red, and
// one from the father, blue; 2n = 6 adds a pair of 5 units), each step's keyframe (CellDivisionKeyframes.cs) and its
// counts — chromosomes (counted by centromeres), DNA molecules (chromatids), ploidy (complete sets, not chromatids), DNA
// amount (c), whether homologs are present and whether sisters are still joined — counted from the keyframe itself, so
// what is drawn and what is counted cannot disagree, and worded as the specification's tables word them (checked against
// them, below). Crossing over, independent assortment and nondisjunction change the keyframes and so the counts. Also
// the Compare page's paired steps and table (§5.2.3) and the Cell cycle page's wheel, checkpoints and cyclins (§4.6).
// What the student set and where each step-through is live in CellDivisionModel, a static class, so they outlive the
// view (the tools' rule: a view subscribes to Changed on Loaded and lets go on Unloaded). Plain .NET, no WPF, so the unit
// tests reach it.

public enum DivisionProcess { Mitosis, Meiosis }

/// <summary>Nondisjunction (spec §4.6, Should): the short pair fails to separate in meiosis I (its homologs go to one
/// pole), or in one cell in meiosis II (one chromosome's sister chromatids go to one pole).</summary>
public enum DivisionNondisjunction { None, MeiosisI, MeiosisII }

public static class DivisionNames
{
    public static string Title(this DivisionProcess p) => p == DivisionProcess.Mitosis ? "Mitosis" : "Meiosis";

    public static string Title(this DivisionNondisjunction n) => n switch
    {
        DivisionNondisjunction.MeiosisI => "In meiosis I",
        DivisionNondisjunction.MeiosisII => "In meiosis II",
        _ => "None",
    };

    /// <summary>The Mac's raw value ("none", "meiosisI", "meiosisII"): the self-check's lines and the remembered choice.</summary>
    public static string Raw(this DivisionNondisjunction n) => n switch
    {
        DivisionNondisjunction.MeiosisI => "meiosisI",
        DivisionNondisjunction.MeiosisII => "meiosisII",
        _ => "none",
    };
}

/// <summary>What the student set.</summary>
public sealed record DivisionOptions
{
    public bool Plant { get; init; }
    /// <summary>n: 2 (2n = 4) or 3 (2n = 6).</summary>
    public int Pairs { get; init; } = 2;
    public bool CrossingOver { get; init; } = true;
    /// <summary>Where on the long pair's long arm the chiasma is, from the centromere: 0.2…0.8 of the arm.</summary>
    public double Chiasma { get; init; } = 0.5;
    /// <summary>Per pair: its father's homolog faces the top pole in metaphase I (independent assortment; the long pair is
    /// the reference and is never flipped).</summary>
    public bool[] Flipped { get; init; } = { false, false, false };
    public DivisionNondisjunction Nondisjunction { get; init; }

    public const double ChiasmaMin = 0.2, ChiasmaMax = 0.8;

    public bool Equals(DivisionOptions? other) =>
        other is not null && Plant == other.Plant && Pairs == other.Pairs && CrossingOver == other.CrossingOver && Chiasma == other.Chiasma
        && Flipped.SequenceEqual(other.Flipped) && Nondisjunction == other.Nondisjunction;

    public override int GetHashCode() => HashCode.Combine(Plant, Pairs, CrossingOver, Chiasma, string.Concat(Flipped.Select(f => f ? '1' : '0')), Nondisjunction);
}

/// <summary>A step: its title (≤ 40 characters), what happens (plain for the controls and Narrator; Rich keeps the
/// specification's bold key terms for the stage card), its stage group (the number keys) and what to notice.</summary>
public sealed record DivisionStage(DivStageKey Key, string Title, string Text, string Rich, int Group, IReadOnlyList<string> Notice);

/// <summary>The counts panel at a step (spec §4.6, §5.2), worded as the specification's tables word them. Values for a
/// human cell (2n = 46) beside the model's. Listing: when cells (or poles) differ, as after nondisjunction, what the listed
/// values are, in order.</summary>
public sealed record DivisionCounts(string Chromosomes, string ChromosomesHuman, string Chromatids, string ChromatidsHuman, string Ploidy,
    string Dna, string Homologs, string Sisters, string? Listing);

/// <summary>What a set of chromatids (a cell, a pole, a nucleus) holds.</summary>
public sealed class DivTally
{
    public readonly int N;
    public readonly int Chromosomes;
    public readonly int Chromatids;
    /// <summary>DNA, in chromosome units (a chromatid of the long pair is 7).</summary>
    public readonly double Units;
    /// <summary>Chromosomes and chromatids of each pair.</summary>
    public readonly int[] PerPair;
    public readonly int[] PerPairChromatids;
    /// <summary>Each pair's homologs present (0 the mother's, 1 the father's).</summary>
    public readonly HashSet<int>[] Homologs;
    public readonly int Joined;

    public DivTally(IEnumerable<DivChromatidKey> keys, int pairs)
    {
        N = pairs;
        PerPair = new int[pairs];
        PerPairChromatids = new int[pairs];
        Homologs = Enumerable.Range(0, pairs).Select(_ => new HashSet<int>()).ToArray();
        var existing = keys.Where(k => k.Exists).ToList();
        Chromatids = existing.Count;
        foreach (var k in existing)
        {
            Units += DivGeometry.Pairs[k.Id.Pair].Units;
            PerPairChromatids[k.Id.Pair] += 1;
            Homologs[k.Id.Pair].Add(k.Id.Homolog);
            if (k.Joined) Joined++;
        }
        // (a chromosome is a centromere: two sisters still joined count once, a chromatid on its own once)
        foreach (var unit in existing.Select(k => k.Id.Chromosome).Distinct().OrderBy(u => u))
        {
            var members = existing.Where(k => k.Id.Chromosome == unit).ToList();
            var together = members.Count(k => k.Joined);
            var count = (together >= 2 ? 1 : together) + members.Count(k => !k.Joined);
            Chromosomes += count;
            PerPair[unit / 2] += count;
        }
    }

    /// <summary>Complete sets: "n", "2n", "4n", or with one pair off by some, "n+1", "n−1" (aneuploid).</summary>
    public string Ploidy
    {
        get
        {
            var b = PerPair[0];
            if (PerPair.All(x => x == b)) return Sets(b);
            var d = PerPair.Skip(1).Sum(x => x - b);
            return Sets(b) + (d > 0 ? $"+{d}" : (d < 0 ? $"−{-d}" : "±"));
        }
    }

    public static string Sets(int m) => m == 1 ? "n" : $"{m}n";

    /// <summary>DNA relative to one haploid set of unreplicated chromosomes (1c).</summary>
    public double C => Units / DivGeometry.Pairs.Take(N).Sum(p => p.Units);

    /// <summary>The same cell in a human (23 pairs): each of the 23 as the long pair, the short pair's difference added.</summary>
    public int HumanChromosomes => PerPair[0] * 23 + PerPair.Skip(1).Sum(x => x - PerPair[0]);
    public int HumanChromatids => PerPairChromatids[0] * 23 + PerPairChromatids.Skip(1).Sum(x => x - PerPairChromatids[0]);

    /// <summary>Pairs with both homologs here.</summary>
    public int BothHomologs => Homologs.Count(h => h.Count == 2);

    public bool Same(DivTally o) => Chromosomes == o.Chromosomes && Chromatids == o.Chromatids && Ploidy == o.Ploidy && Math.Abs(Units - o.Units) < 1e-9;
}

/// <summary>A step of the Compare page: mitosis's step beside meiosis's (spec §4.6: prophase ↔ prophase I … telophase ↔
/// telophase II). Mitosis is over after its fifth; it stays on its two cells while meiosis II runs. Rows are the §5.2.3
/// rows the step is about.</summary>
public sealed record DivCompareStep(string Title, string Text, int Mitosis, int Meiosis, int Group, int[] Rows);

/// <summary>A phase of the cell cycle (spec §4.6: G1 about 11 h, S about 8 h, G2 about 4 h, M about 1 h for a typical 24 h
/// human cell, Alberts; G0 off G1).</summary>
public sealed record DivCyclePhase(string Id, string Name, double Hours, string Title, string Text);

/// <summary>A checkpoint: a gate on the wheel (degrees clockwise from the top), what it checks, the cyclin–CDK part, and
/// what goes wrong when it fails (the cancer view).</summary>
public sealed record DivCheckpoint(string Id, string Name, string Short, double Angle, string[] Checks, string Role, string Cyclin, string Cancer);

/// <summary>A labelled part's card: its name, what it is, and a common mix-up.</summary>
public sealed record DivPartInfo(string Title, string What, string? MixUp);

/// <summary>A cell at the end of a process: its chromatids, its ploidy and its chromosome count.</summary>
public sealed record DivProduct(int Cell, List<DivChromatidKey> Chromatids, DivTally Tally);

/// <summary>A process worked out for some options: its stages, keyframes and counts.</summary>
public sealed class DivisionSequence
{
    public required DivisionProcess Process { get; init; }
    public required DivisionOptions Options { get; init; }
    public required IReadOnlyList<DivisionStage> Stages { get; init; }
    public required IReadOnlyList<DivisionKeyframe> Keyframes { get; init; }
    public required IReadOnlyList<DivisionCounts> Counts { get; init; }

    public IReadOnlyList<DivisionFrame> Frames => Keyframes.Select(k => k.Frame).ToList();

    public List<BioStep> Steps => Stages.Select((s, i) => new BioStep(i, s.Title, s.Text, s.Group, s.Notice)).ToList();

    /// <summary>A badge's words: the ploidy of that cell at that step ("2n", "n", "n+1").</summary>
    public string Badge(int step, int cell) => new DivTally(Keyframes[step].Frame.Chromatids.Where(c => c.Cell == cell), Options.Pairs).Ploidy;

    /// <summary>The cells it ends with.</summary>
    public List<DivProduct> Products
    {
        get
        {
            if (Keyframes.Count == 0) return new();
            var keys = Keyframes[^1].Frame.Chromatids.Where(c => c.Exists);
            return keys.GroupBy(k => k.Cell).OrderBy(g => g.Key)
                .Select(g => new DivProduct(g.Key, g.OrderBy(k => k.Id).ToList(), new DivTally(g, Options.Pairs))).ToList();
        }
    }
}

public static class DivisionModel
{
    // ---- the specification's tables (copied out of it by a script, as written) ----

    /// <summary>§5.2.1 as the specification writes it: Step | What happens | Chromosomes in the cell | DNA molecules (chromatids) | Ploidy | DNA.</summary>
    public static readonly string[][] MitosisTable =
    {
        new[] { "G1", "Cell grows, makes proteins and organelles. Chromosomes are long, uncondensed threads (chromatin).", "4 (46)", "4 (46)", "2n", "2c" },
        new[] { "S", "DNA replication: each chromosome becomes two sister chromatids.", "4 (46)", "8 (92)", "2n", "4c" },
        new[] { "G2", "More growth; preparation for division; centrosome has duplicated.", "4 (46)", "8 (92)", "2n", "4c" },
        new[] { "Prophase", "Chromatin condenses into visible chromosomes (sisters joined at the centromere); spindle starts forming as centrosomes move apart; nucleolus disappears.", "4", "8", "2n", "4c" },
        new[] { "Prometaphase", "Nuclear envelope breaks down; kinetochore microtubules attach to each chromatid's kinetochore (sisters to opposite poles).", "4", "8", "2n", "4c" },
        new[] { "Metaphase", "Chromosomes line up singly on the metaphase plate. Spindle checkpoint: all attached?", "4", "8", "2n", "4c" },
        new[] { "Anaphase", "Sister chromatids separate; each is now a chromosome and moves to opposite poles.", "8 (92)", "8", "4n in one cell (two diploid sets moving apart)", "4c" },
        new[] { "Telophase", "Nuclear envelopes re-form; chromosomes decondense.", "8 (in two nuclei)", "8", "2n per nucleus", "4c total" },
        new[] { "Cytokinesis", "Animal: cleavage furrow (actin–myosin ring). Plant: cell plate from Golgi vesicles.", "4 per cell", "4 per cell", "2n", "2c" },
        new[] { "Result", "Two genetically identical diploid cells.", "", "", "", "" },
    };

    /// <summary>§5.2.2 as the specification writes it: Step | What happens | Chromosomes per cell | DNA molecules per cell | Ploidy | DNA per cell.</summary>
    public static readonly string[][] MeiosisTable =
    {
        new[] { "Interphase (G1, S, G2)", "As mitosis: DNA replicates **once**, before meiosis I.", "4", "4 → 8", "2n", "2c → 4c" },
        new[] { "Prophase I", "Chromosomes condense; **homologs pair (synapsis)** to form **tetrads (bivalents)**, 2 here (23 in humans); **crossing over** between non-sister chromatids at chiasmata; spindle forms; envelope breaks down.", "4 (as 2 tetrads)", "8", "2n", "4c" },
        new[] { "Metaphase I", "**Homologous pairs** line up at the plate (pairs, not single chromosomes). The orientation of each pair is random (**independent assortment**).", "4", "8", "2n", "4c" },
        new[] { "Anaphase I", "**Homologs separate** to opposite poles; **sister chromatids stay together**.", "4 (2 to each pole)", "8", "2n → two sets of n", "4c" },
        new[] { "Telophase I + cytokinesis", "Two cells.", "2 per cell", "4 per cell", "**n (haploid)**", "2c" },
        new[] { "(no S phase)", "No DNA replication between meiosis I and II.", "", "", "", "" },
        new[] { "Prophase II", "Spindle forms in each cell.", "2", "4", "n", "2c" },
        new[] { "Metaphase II", "Chromosomes line up singly at each plate (like mitosis).", "2", "4", "n", "2c" },
        new[] { "Anaphase II", "**Sister chromatids separate.**", "4 (2 to each pole)", "4", "n per pole", "2c" },
        new[] { "Telophase II + cytokinesis", "Four haploid cells, genetically different (crossing over and independent assortment).", "2 per cell", "2 per cell", "n", "1c" },
    };

    /// <summary>§5.2.3 as the specification writes it: Mitosis | Meiosis.</summary>
    public static readonly string[][] CompareTable =
    {
        new[] { "Purpose", "Growth, repair, asexual reproduction", "Making gametes (sexual reproduction)" },
        new[] { "DNA replications", "1", "1" },
        new[] { "Divisions", "1", "2" },
        new[] { "Synapsis of homologs / tetrads", "No", "Yes (prophase I)" },
        new[] { "Crossing over", "No (normally)", "Yes (prophase I)" },
        new[] { "What lines up at metaphase", "Individual chromosomes (metaphase)", "Homologous pairs (metaphase I); individual chromosomes (metaphase II)" },
        new[] { "What separates at anaphase", "Sister chromatids", "Homologs (anaphase I); sister chromatids (anaphase II)" },
        new[] { "Daughter cells", "2, diploid (2n), identical to the parent", "4, haploid (n), genetically different" },
        new[] { "Where in humans", "Body (somatic) cells", "Ovaries and testes" },
    };

    /// <summary>The tables' text without their bold marks.</summary>
    public static string Plain(string s) => s.Replace("**", "");

    /// <summary>The definitions on the counts panel's info buttons (spec §5.2, §4.6).</summary>
    public static readonly (string Id, string Label, string Rule)[] Definitions =
    {
        ("chromosomes", "Chromosomes", "One DNA molecule with its proteins or, after replication, two identical sister chromatids joined at a centromere. Either way it counts as one chromosome. Count chromosomes by counting centromeres. A replicated chromosome is still ONE chromosome."),
        ("chromatids", "DNA molecules (chromatids)", "Each chromatid is one DNA molecule. A chromosome is one before S phase and two (sister chromatids) after it, until they separate."),
        ("ploidy", "Ploidy", "Ploidy = number of complete sets of chromosomes. Count sets, not chromatids. n = haploid (one set), 2n = diploid (two sets). A cell after meiosis I is haploid even though each chromosome still has two chromatids."),
        ("dna", "DNA amount", "c: DNA amount relative to one haploid set of unreplicated chromosomes (1c). A diploid G1 cell is 2c, and after S it is 4c."),
        ("homologs", "Homologous pairs present?", "Homologous chromosomes: one from each parent, same genes in the same order (alleles may differ). Present in every diploid cell, in mitosis too. They pair up only in meiosis I."),
        ("sisters", "Sister chromatids attached?", "Sister chromatids: the two identical copies made in S phase, joined until anaphase (mitosis) or anaphase II (meiosis)."),
    };

    /// <summary>"8 (in two nuclei)" → "8" and "in two nuclei" (a single bracket at the end only), for the counts' rows.</summary>
    public static (string Main, string? Note) Split(string s)
    {
        var open = s.IndexOf(" (", StringComparison.Ordinal);
        if (!s.EndsWith(')') || s.Count(ch => ch == '(') != 1 || open < 0) return (s, null);
        return (s[..open], s[(open + 2)..^1]);
    }

    // ---- steps ----

    /// <summary>The stage groups the number keys jump to.</summary>
    public static Dictionary<int, string> Groups(DivisionProcess p) => p == DivisionProcess.Mitosis
        ? new() { [1] = "Interphase", [2] = "Prophase", [3] = "Metaphase", [4] = "Anaphase", [5] = "Telophase", [6] = "Cytokinesis" }
        : new() { [1] = "Interphase", [2] = "Prophase I", [3] = "Metaphase I", [4] = "Anaphase I", [5] = "Telophase I", [6] = "Prophase II", [7] = "Metaphase II", [8] = "Anaphase II", [9] = "Telophase II" };

    public static List<DivisionStage> Stages(DivisionProcess p, DivisionOptions o)
    {
        static DivisionStage Stage(DivStageKey key, string title, string rich, int group, params string[] notice) => new(key, title, Plain(rich), rich, group, notice);
        int n = o.Pairs, two = 2 * n, four = 4 * n;
        var m = MitosisTable;
        var e = MeiosisTable;
        if (p == DivisionProcess.Mitosis)
        {
            var cyto = o.Plant
                ? "A **cell plate** from Golgi vesicles grows from the middle outward into a new wall. Result: two genetically identical diploid cells."
                : "A **cleavage furrow** (an actin–myosin ring) pinches the cell in two. Result: two genetically identical diploid cells.";
            return new()
            {
                Stage(DivStageKey.G1, "G1", m[0][1], 1, $"Each chromosome is one DNA molecule now: {two} chromosomes, {two} DNA molecules.",
                    "Homologous chromosomes are here too (one red and one blue of each pair); in mitosis they never pair."),
                Stage(DivStageKey.S, "S phase", m[1][1], 1, "DNA replicates here, in S phase of interphase, never in prophase.",
                    $"Still {two} chromosomes: a replicated chromosome is still ONE chromosome. DNA molecules double to {four}."),
                Stage(DivStageKey.G2, "G2", m[2][1], 1, "4c: twice G1's DNA, with the same number of chromosomes.",
                    o.Plant ? "Plant cells have no centrioles: their spindle forms without centrosomes." : "Two centrosomes now, each with a pair of centrioles."),
                Stage(DivStageKey.Prophase, "Prophase", m[3][1], 2, $"Count centromeres to count chromosomes: still {two}.",
                    "Each chromosome is two sister chromatids joined at its centromere."),
                Stage(DivStageKey.Prometaphase, "Prometaphase", m[4][1], 2, "Each chromosome's two kinetochores face opposite poles."),
                Stage(DivStageKey.Metaphase, "Metaphase", m[5][1], 3, "Chromosomes line up one by one, not in pairs.",
                    "Spindle checkpoint: anaphase waits until every kinetochore is attached."),
                Stage(DivStageKey.Anaphase, "Anaphase", m[6][1], 4, $"Sisters separate: each chromatid is now a chromosome, so the count doubles to {four}.",
                    "The DNA amount does not change: still 4c in the cell."),
                Stage(DivStageKey.Telophase, "Telophase", m[7][1], 5, "Two nuclei, each with a complete diploid set (2n).",
                    o.Plant ? "A cell plate starts in the middle." : "The cleavage furrow starts to pinch the cell."),
                Stage(DivStageKey.Cytokinesis, "Cytokinesis", cyto, 6, $"Two cells, each 2n = {two} and 2c: identical to each other and to the parent cell."),
            };
        }
        bool ndI = o.Nondisjunction == DivisionNondisjunction.MeiosisI, ndII = o.Nondisjunction == DivisionNondisjunction.MeiosisII;
        var prophaseI = e[1][1].Replace("2 here (23 in humans)", $"{n} here (23 in humans)");
        var proNotice = new List<string> { $"Homologs pair up (synapsis): {n} tetrads, each 2 chromosomes and 4 chromatids." };
        proNotice.Add(o.CrossingOver ? "Crossing over: two non-sister chromatids swap the part beyond the chiasma." : "Crossing over is off: no chiasma, no swapped parts.");
        var anaNotice = new List<string> { "Homologs separate; each keeps both its chromatids.", "Still 4c in the cell: nothing has been copied or lost." };
        if (ndI) anaNotice.Insert(0, "Nondisjunction: the short pair's homologs both go to the top pole.");
        var telINotice = new List<string> { "Each cell is haploid (n) although every chromosome still has two chromatids.", "Ploidy counts sets, not chromatids." };
        if (ndI) telINotice = new List<string> { "After nondisjunction one cell has n+1 chromosomes and the other n−1.", telINotice[^1] };
        var anaIINotice = new List<string> { "Now the sister chromatids separate, as in mitosis's anaphase." };
        if (ndII) anaIINotice.Insert(0, "Nondisjunction: in the top cell one chromosome's sisters fail to separate.");
        var telIINotice = new List<string> { "Four haploid cells, each 1c." };
        if (o.Nondisjunction == DivisionNondisjunction.None)
            telIINotice.Add(o.CrossingOver ? "All four are genetically different: crossing over and independent assortment."
                : "Without crossing over, the cells come in identical pairs; independent assortment still mixes the pairs.");
        else
            telIINotice.Add("Gametes labelled n+1 and n−1 have one chromosome too many or too few. An n+1 gamete with an extra chromosome 21 joining a normal one makes trisomy 21 (Down syndrome, 47 chromosomes).");
        // (the specification's sentence is the usual case; without crossing over, or with nondisjunction, it says what is
        //  different — the Mac's)
        var telII = o.Nondisjunction != DivisionNondisjunction.None
            ? $"Four cells, but nondisjunction left {(ndI ? "all four" : "two of them")} with a chromosome too many (n+1) or too few (n−1)."
            : !o.CrossingOver
                ? "Four haploid cells. Without crossing over they come in two identical pairs; independent assortment decided which homologs went together."
                : e[9][1];
        return new()
        {
            Stage(DivStageKey.G1, "G1", "As in mitosis, the cell grows and makes proteins and organelles. Chromosomes are long, uncondensed threads (chromatin).", 1,
                $"Diploid: {two} chromosomes, {n} homologous pairs (one of each pair from each parent)."),
            Stage(DivStageKey.S, "S phase", "DNA replicates **once**, before meiosis I: each chromosome becomes two sister chromatids.", 1,
                "This is the only S phase: there is none between meiosis I and II.", $"Still {two} chromosomes; {four} DNA molecules."),
            Stage(DivStageKey.G2, "G2", "More growth and preparation for division; the centrosome has duplicated.", 1, "2n and 4c, as before mitosis."),
            Stage(DivStageKey.Prophase1, "Prophase I", prophaseI, 2, proNotice.ToArray()),
            Stage(DivStageKey.Metaphase1, "Metaphase I", e[2][1], 3, "Pairs line up, not single chromosomes.",
                "Which homolog faces which pole is random for each pair: independent assortment."),
            Stage(DivStageKey.Anaphase1, "Anaphase I", e[3][1], 4, anaNotice.ToArray()),
            Stage(DivStageKey.Telophase1, "Telophase I + cytokinesis", "Two cells, each **haploid (n)**: one chromosome of each pair, still made of two chromatids.", 5, telINotice.ToArray()),
            Stage(DivStageKey.Prophase2, "Prophase II", "No DNA replication between meiosis I and II. " + e[6][1], 6, "No S phase before meiosis II: each cell still has 2c."),
            Stage(DivStageKey.Metaphase2, "Metaphase II", e[7][1], 7, "Single chromosomes line up, as in mitosis."),
            Stage(DivStageKey.Anaphase2, "Anaphase II", e[8][1], 8, anaIINotice.ToArray()),
            Stage(DivStageKey.Telophase2, "Telophase II + cytokinesis", telII, 9, telIINotice.ToArray()),
        };
    }

    // ---- counts ----

    public static DivisionCounts Counts(DivisionKeyframe kf, DivisionProcess process, int n)
    {
        var keys = kf.Frame.Chromatids.Where(c => c.Exists).ToList();
        var whole = new DivTally(keys, n);
        var cellKeys = keys.GroupBy(k => k.Cell).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        var cells = cellKeys.Select(k => new DivTally(k, n)).ToList();
        List<DivTally> Parts(List<DivChromatidKey> ks) => ks.GroupBy(k => k.Part).OrderBy(g => g.Key).Select(g => new DivTally(g, n)).ToList();
        static int Chr(DivTally t, bool human) => human ? t.HumanChromosomes : t.Chromosomes;
        static int Cds(DivTally t, bool human) => human ? t.HumanChromatids : t.Chromatids;
        static string Word(string p) => p == "2n" ? "diploid" : (p == "n" ? "haploid" : p);

        Func<bool, string> chromosomes = h => $"{Chr(whole, h)}";
        Func<bool, string> chromatids = h => $"{Cds(whole, h)}";
        var ploidy = whole.Ploidy;
        var dna = CText(whole.C);
        string? listing = null;

        switch (kf.Style)
        {
            case DivCountStyle.Cell:
                break;
            case DivCountStyle.Tetrads:
            {
                var tetrads = Enumerable.Range(0, n).Count(p => whole.Homologs[p].Count == 2);
                chromosomes = h => $"{Chr(whole, h)} (as {(h ? 23 : tetrads)} tetrads)";
                break;
            }
            case DivCountStyle.Anaphase:
            {
                var poles = Parts(keys);
                ploidy = poles.Count == 2 && poles[0].Ploidy == poles[1].Ploidy
                    ? $"{whole.Ploidy} in one cell (two {Word(poles[0].Ploidy)} sets moving apart)"
                    : $"{whole.Ploidy} in one cell ({string.Join(" and ", poles.Select(x => x.Ploidy))} moving apart)";
                break;
            }
            case DivCountStyle.TwoNuclei:
            {
                var nuclei = Parts(keys);
                chromosomes = h => $"{Chr(whole, h)} (in two nuclei)";
                ploidy = nuclei.Select(x => x.Ploidy).Distinct().Count() == 1 ? $"{nuclei[0].Ploidy} per nucleus" : string.Join(" and ", nuclei.Select(x => x.Ploidy));
                dna = CText(whole.C) + " total";
                break;
            }
            case DivCountStyle.AnaphaseI:
            {
                var poles = Parts(keys);
                if (poles.Count == 2 && poles[0].Same(poles[1]))
                {
                    chromosomes = h => $"{Chr(whole, h)} ({Chr(poles[0], h)} to each pole)";
                    ploidy = $"{whole.Ploidy} → two sets of {poles[0].Ploidy}";
                }
                else
                {
                    chromosomes = h => $"{Chr(whole, h)} ({string.Join(" and ", poles.Select(x => $"{Chr(x, h)}"))} to the poles)";
                    ploidy = $"{whole.Ploidy} → " + string.Join(" and ", poles.Select(x => x.Ploidy));
                }
                break;
            }
            case DivCountStyle.PerCell or DivCountStyle.HaploidCells or DivCountStyle.PerCellPlain:
            {
                var suffix = kf.Style == DivCountStyle.PerCellPlain ? "" : " per cell";
                if (cells.Count > 0 && cells.All(c => c.Same(cells[0])))
                {
                    var first = cells[0];
                    chromosomes = h => $"{Chr(first, h)}{suffix}";
                    chromatids = h => $"{Cds(first, h)}{suffix}";
                    ploidy = first.Ploidy + (kf.Style == DivCountStyle.HaploidCells && first.Ploidy == "n" ? " (haploid)" : "");
                    dna = CText(first.C);
                }
                else
                {
                    chromosomes = h => string.Join(" · ", cells.Select(c => $"{Chr(c, h)}"));
                    chromatids = h => string.Join(" · ", cells.Select(c => $"{Cds(c, h)}"));
                    ploidy = string.Join(" · ", cells.Select(c => c.Ploidy));
                    dna = string.Join(" · ", cells.Select(c => CText(c.C)));
                    listing = cells.Count == 2 ? "Top cell · bottom cell" : "Top left · top right · bottom left · bottom right";
                }
                break;
            }
            case DivCountStyle.AnaphaseII:
            {
                string CellChromosomes(int i, bool h)
                {
                    var poles = Parts(cellKeys[i]);
                    if (poles.Count == 2 && poles[0].Same(poles[1])) return $"{Chr(cells[i], h)} ({Chr(poles[0], h)} to each pole)";
                    return $"{Chr(cells[i], h)} ({string.Join(" and ", poles.Select(x => $"{Chr(x, h)}"))} to the poles)";
                }
                string CellPloidy(int i)
                {
                    var poles = Parts(cellKeys[i]);
                    if (poles.Count == 2 && poles[0].Ploidy == poles[1].Ploidy) return $"{poles[0].Ploidy} per pole";
                    return string.Join(" and ", poles.Select(x => x.Ploidy));
                }
                var indices = Enumerable.Range(0, cells.Count).ToList();
                var texts = indices.Select(i => CellChromosomes(i, false)).ToList();
                if (texts.Distinct().Count() == 1 && cells.Count > 0 && cells.All(c => c.Same(cells[0])) && indices.Select(CellPloidy).Distinct().Count() == 1)
                {
                    var first = cells[0];
                    chromosomes = h => CellChromosomes(0, h);
                    chromatids = h => $"{Cds(first, h)}";
                    ploidy = CellPloidy(0);
                    dna = CText(first.C);
                }
                else
                {
                    chromosomes = h => string.Join(" · ", indices.Select(i => CellChromosomes(i, h)));
                    chromatids = h => string.Join(" · ", cells.Select(c => $"{Cds(c, h)}"));
                    ploidy = string.Join(" · ", indices.Select(CellPloidy));
                    dna = string.Join(" · ", cells.Select(c => CText(c.C)));
                    listing = "Top cell · bottom cell";
                }
                break;
            }
        }

        // (homologs: present in every diploid cell, in mitosis too; paired only in meiosis I)
        string homologs;
        var perCellStyle = kf.Style is DivCountStyle.PerCell or DivCountStyle.HaploidCells or DivCountStyle.PerCellPlain or DivCountStyle.AnaphaseII;
        string Presence(DivTally t)
        {
            var b = t.BothHomologs;
            return b == n ? "Yes" : (b == 0 ? "No" : $"{b} of {n} pairs");
        }
        if (kf.Paired) homologs = "Yes, paired as tetrads";
        else if (perCellStyle)
        {
            var each = cells.Select(Presence).ToList();
            homologs = each.Distinct().Count() <= 1 ? (each.FirstOrDefault() ?? "No") : string.Join(" · ", each);
        }
        else if (whole.BothHomologs == n)
            homologs = process == DivisionProcess.Mitosis ? "Yes, but they don't pair" : (kf.Style == DivCountStyle.AnaphaseI ? "Yes, separating" : "Yes");
        else homologs = Presence(whole);

        // (sisters: joined from S until anaphase, or anaphase II)
        string sisters;
        var copied = kf.Frame.Chromatids.Any(c => c.Exists && c.Id.Sister == 1);
        if (whole.Chromatids > 0 && whole.Joined == whole.Chromatids)
            sisters = kf.Style == DivCountStyle.AnaphaseI ? "Yes, they stay together" : "Yes";
        else if (whole.Joined == 0)
            sisters = !copied ? "Not copied yet" : (kf.Style is DivCountStyle.Anaphase or DivCountStyle.AnaphaseII ? "No: separated, each now a chromosome" : "No: one chromatid each");
        else sisters = "Partly: one chromosome's sisters did not separate";

        return new DivisionCounts(chromosomes(false), chromosomes(true), chromatids(false), chromatids(true), ploidy, dna, homologs, sisters, listing);
    }

    /// <summary>"2c", or "1.4c" for an aneuploid cell (rounded half-up, as every bio tool rounds: 1.25 is "1.3c").</summary>
    public static string CText(double c) =>
        Math.Abs(c - Math.Round(c, MidpointRounding.AwayFromZero)) < 1e-9
            ? $"{(int)Math.Round(c, MidpointRounding.AwayFromZero)}c"
            : BioRound.Fixed(c, 1) + "c";

    // ---- sequences ----

    public static DivisionSequence Sequence(DivisionProcess p, DivisionOptions o)
    {
        var kfs = new DivisionBuilder(p, o).Keyframes();
        return new DivisionSequence
        {
            Process = p, Options = o, Stages = Stages(p, o), Keyframes = kfs,
            Counts = kfs.Select(k => Counts(k, p, o.Pairs)).ToList(),
        };
    }

    /// <summary>Independent assortment's count (spec §4.6): "2ⁿ = 4 possible assortments; humans 2²³ ≈ 8.4 million".</summary>
    public static string Assortments(int pairs) =>
        $"2ⁿ = {1 << pairs} possible assortments; humans 2²³ ≈ {((1 << 23) / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture)} million";

    // ---- the parts' cards (a label pressed) ----

    /// <summary>What a labelled part is (spec §4.6 Labels; §5.2's definitions), and the mix-up students make about it.</summary>
    public static readonly Dictionary<string, DivPartInfo> Parts = new()
    {
        ["nucleus"] = new("Nucleus", "Holds the chromosomes, inside the nuclear envelope. It goes as mitosis starts and two form again at telophase.", null),
        ["envelope"] = new("Nuclear envelope", "A double membrane round the nucleus. It breaks down in prometaphase (late prophase I in meiosis) and re-forms in telophase.", null),
        ["nucleolus"] = new("Nucleolus", "Where the nucleus makes ribosomal RNA. It disappears in prophase and returns in telophase.", "It is not a chromosome and not a second nucleus."),
        ["chromatin"] = new("Chromosomes as chromatin", "In interphase each chromosome is a long, loose thread of DNA and protein (chromatin), too thin to see in a light microscope.", "The chromosomes are there all the time: they only condense, and become visible, for division."),
        ["replication"] = new("DNA replication (S phase)", "Each chromosome's DNA is copied: one chromosome becomes two identical sister chromatids, joined at the centromere.", "Replication happens in S phase, never in prophase, and it doubles the DNA, not the number of chromosomes."),
        ["sisters"] = new("Sister chromatids", "The two identical copies made in S phase, joined at the centromere until anaphase (mitosis) or anaphase II (meiosis).", "Sister chromatids are copies of one chromosome. Homologous chromosomes are two different chromosomes, one from each parent."),
        ["homologs"] = new("Homologous chromosomes", "One from each parent, with the same genes in the same order (their alleles may differ). Every diploid cell has them, in mitosis too; they pair up only in meiosis I.", "Homologs are not sister chromatids: they were never copies of each other."),
        ["chromosome"] = new("Chromosome", "One DNA molecule with its proteins, or after replication two sister chromatids joined at a centromere. Either way it counts as one chromosome.", "Count chromosomes by counting centromeres. A replicated chromosome is still ONE chromosome."),
        ["daughterChromosome"] = new("A chromatid becomes a chromosome", "When sister chromatids separate, each has its own centromere, so each now counts as a chromosome: the count doubles while the DNA stays the same.", null),
        ["centromere"] = new("Centromere", "The narrow waist where sister chromatids are held together. A kinetochore forms on each side of it, where spindle microtubules attach.", "One centromere means one chromosome."),
        ["kinetochore"] = new("Kinetochore microtubules", "Spindle fibres that attach to the kinetochores at each centromere and pull chromosomes, or chromatids, toward the poles.", null),
        ["spindle"] = new("Spindle", "Microtubules that move the chromosomes: kinetochore microtubules attach to them, polar microtubules overlap in the middle and push the poles apart.", null),
        ["polar"] = new("Polar microtubules", "Spindle fibres from the two poles that overlap in the middle; sliding past each other, they push the poles apart and lengthen the cell.", null),
        ["centrosome"] = new("Centrosome", "An animal cell's microtubule-organising centre, with a pair of centrioles at right angles. It duplicates before division, and the two move apart to become the spindle's poles.", "Plant cells have no centrioles: their spindle forms without centrosomes."),
        ["pole"] = new("Spindle pole (plant)", "Plant cells have no centrosomes or centrioles: their spindle forms from broad microtubule-organising regions at each pole.", null),
        ["plate"] = new("Metaphase plate", "The plane midway between the poles where the chromosomes line up. It is a position, not a structure.", "Mitosis lines up single chromosomes; meiosis I lines up homologous pairs."),
        ["furrow"] = new("Cleavage furrow", "In animal cells a ring of actin and myosin under the membrane tightens and pinches the cell in two.", "Plant cells have a wall and cannot pinch: they build a cell plate instead."),
        ["cellplate"] = new("Cell plate", "In plant cells, Golgi vesicles carrying wall material gather in the middle and fuse from the middle outward into a new cell wall between the two cells.", null),
        ["membrane"] = new("Cell membrane", "The plasma membrane round the cell. In an animal cell the cleavage furrow pinches it in two.", null),
        ["wall"] = new("Cell wall", "A plant cell's rigid wall outside its membrane. It keeps the cell's shape, so a plant cell divides by building a new wall between the daughters.", null),
        ["daughter"] = new("Daughter cells", "Mitosis gives two cells with the same chromosomes as the parent and as each other: diploid (2n) and 2c, ready to start G1.", null),
        ["tetrad"] = new("Tetrad (bivalent)", "A pair of homologous chromosomes joined along their length in prophase I: 2 chromosomes, 4 chromatids.", "A tetrad is 2 chromosomes, not 4: count its centromeres."),
        ["chiasma"] = new("Chiasma", "Where two non-sister chromatids of a pair crossed over and swapped the parts beyond it, in prophase I. It holds the homologs together until anaphase I.", "Crossing over is between non-sister chromatids; sister chromatids are identical, so swapping between them would change nothing."),
        ["recombinant"] = new("Recombinant chromatid", "A chromatid that crossed over: beyond the chiasma it carries the other parent's DNA. It makes gametes genetically different.", null),
        ["haploid"] = new("Haploid cell (n)", "One complete set of chromosomes: one of each pair. After meiosis I each cell is haploid, although each chromosome still has two chromatids.", "Ploidy counts sets, not chromatids."),
        ["noS"] = new("No S phase between meiosis I and II", "DNA is copied once only, before meiosis I. Meiosis II separates the sister chromatids made then.", null),
        ["gamete"] = new("Gametes (n)", "Meiosis gives four haploid cells (eggs or sperm in animals), 1c each, genetically different from each other.", null),
        ["nondisjunction"] = new("Nondisjunction", "Chromosomes fail to separate: a homologous pair in meiosis I, or one chromosome's sister chromatids in meiosis II. Gametes get one too many (n+1) or one too few (n−1).", "An n+1 gamete with an extra chromosome 21 joining a normal gamete gives trisomy 21 (Down syndrome): 47 chromosomes."),
    };

    // ---- compare (spec §4.6, §5.2.3) ----

    /// <summary>Mitosis's steps (0 G1 … 8 cytokinesis) beside meiosis's (0 G1 … 10 telophase II).</summary>
    public static readonly DivCompareStep[] CompareSteps =
    {
        new("Interphase", "Both copy their DNA once, in S phase, before they divide: 4 chromosomes, 8 chromatids, 4c.", 2, 2, 1, new[] { 1 }),
        new("Prophase · Prophase I", "Mitosis: each chromosome condenses on its own. Meiosis I: homologs pair as tetrads and cross over.", 3, 3, 2, new[] { 3, 4 }),
        new("Metaphase · Metaphase I", "Mitosis: single chromosomes line up. Meiosis I: homologous pairs line up.", 5, 4, 3, new[] { 5 }),
        new("Anaphase · Anaphase I", "Mitosis: sister chromatids separate. Meiosis I: homologs separate and sisters stay together.", 6, 5, 4, new[] { 6 }),
        new("Telophase, cytokinesis · Telophase I", "Mitosis ends with 2 identical diploid cells. Meiosis I ends with 2 haploid cells whose chromosomes still have 2 chromatids.", 8, 6, 5, new[] { 2 }),
        new("Prophase II", "Mitosis is over. Meiosis goes on to a second division, with no S phase first: a spindle forms in each cell.", 8, 7, 6, new[] { 1, 2 }),
        new("Metaphase II", "In meiosis II single chromosomes line up, as in mitosis's metaphase.", 8, 8, 6, new[] { 5 }),
        new("Anaphase II", "Now meiosis separates sister chromatids, as mitosis did in its anaphase.", 8, 9, 6, new[] { 6 }),
        new("Telophase II · result", "Mitosis: 2 diploid cells, identical to the parent. Meiosis: 4 haploid cells, genetically different.", 8, 10, 6, new[] { 0, 7, 8 }),
    };

    public static readonly Dictionary<int, string> CompareGroups = new() { [1] = "Interphase", [2] = "Prophase", [3] = "Metaphase", [4] = "Anaphase", [5] = "Telophase", [6] = "Meiosis II" };

    public static List<BioStep> CompareBioSteps => CompareSteps.Select((s, i) => new BioStep(i, s.Title, s.Text, s.Group)).ToList();

    // ---- the cell cycle (spec §4.6) ----

    public static readonly DivCyclePhase[] CyclePhases =
    {
        new("g1", "G1", 11, "G1: growth", "The cell grows and makes proteins and organelles. Chromosomes are uncondensed threads: 46 chromosomes, 2c."),
        new("s", "S", 8, "S: DNA synthesis", "Every chromosome is copied into two sister chromatids. Still 46 chromosomes; now 92 chromatids; 2c becomes 4c."),
        new("g2", "G2", 4, "G2: growth and preparation", "More growth; the centrosome has duplicated; the copied DNA is checked. 46 chromosomes, 4c."),
        new("m", "M", 1, "M: mitosis and cytokinesis", "Prophase, prometaphase, metaphase, anaphase and telophase, then cytokinesis: two cells, each 46 chromosomes and 2c."),
    };

    public static readonly DivCyclePhase G0 = new("g0", "G0", 0, "G0: resting",
        "A state outside the cycle, entered from G1. Many cells stay here for good (most neurons and muscle cells); others wait for a signal and return to G1.");

    /// <summary>The wheel: each phase's start and end, degrees clockwise from the top, M centred at the top and G1 after it.</summary>
    public static List<(DivCyclePhase Phase, double Start, double End)> CycleArcs
    {
        get
        {
            var total = CyclePhases.Sum(p => p.Hours);
            var m = CyclePhases[3];
            var start = -m.Hours / total * 360 / 2 + m.Hours / total * 360;
            var arcs = new List<(DivCyclePhase, double, double)>();
            foreach (var p in CyclePhases.Take(3))
            {
                var sweep = p.Hours / total * 360;
                arcs.Add((p, start, start + sweep));
                start += sweep;
            }
            arcs.Add((m, start, start + m.Hours / total * 360));
            return arcs;
        }
    }

    public static readonly DivCheckpoint[] Checkpoints =
    {
        new("g1s", "G1/S checkpoint (restriction point)", "G1/S", 172.5,
            new[] { "Is the cell big enough?", "Are there enough nutrients?", "Are growth factors telling it to divide?", "Is its DNA undamaged?" },
            "Passing it commits the cell to copying its DNA. If not, the cell waits, or leaves the cycle for G0.",
            "G1/S cyclins (such as cyclin E) rise late in G1; bound to their CDK they push the cell into S phase, then are destroyed.",
            "Tumour suppressors such as p53 and Rb hold the cell here. When they are lost, and oncogenes such as a mutant Ras keep signalling, cells copy damaged DNA and divide without growth signals."),
        new("g2m", "G2/M checkpoint", "G2/M", 352.5,
            new[] { "Has all the DNA been copied?", "Was it copied correctly, with any damage repaired?" },
            "Only a cell whose DNA is fully and correctly copied goes into mitosis.",
            "M cyclin (cyclin B) builds up through S and G2; with its CDK (together called MPF) it pushes the cell into mitosis.",
            "Cells go into mitosis with damaged or unfinished DNA, and their mutations pile up."),
        new("spindle", "Spindle assembly checkpoint (in M)", "Spindle", 0,
            new[] { "Is every kinetochore attached to the spindle?" },
            "Anaphase waits until every chromosome is attached and lined up, so each daughter gets one copy of each.",
            "Once every kinetochore is attached, cyclin B is destroyed and the sisters are released: anaphase begins.",
            "Chromosomes are pulled apart before all are attached, so daughter cells get too many or too few (aneuploidy)."),
    };

    /// <summary>CDKs and cyclins in a sentence (each gate's card ends with it).</summary>
    public const string CyclinRule = "CDKs (cyclin-dependent kinases) are there all through the cycle but work only bound to a cyclin. Cyclin levels rise and fall each cycle: each cyclin–CDK complex pushes the cell past one point, then its cyclin is destroyed.";

    /// <summary>The G1/S cyclin's level (an illustrative shape, 0…1) at a time in a 24 h cycle that starts with G1: it peaks
    /// at the G1/S boundary (11 h).</summary>
    public static double CyclinG1S(double t) => Math.Exp(-((t - 11) / 1.8) * ((t - 11) / 1.8));

    /// <summary>The M cyclin's level (illustrative, 0…1): it builds through S and G2, peaks in M and drops at anaphase.</summary>
    public static double CyclinM(double t)
    {
        if (t < 11) return 0.04;
        if (t < 23.5) return 0.04 + 0.96 * Math.Pow((t - 11) / 12.5, 1.4);
        return 1 - 0.96 * DivMath.Smoothstep(23.5, 24, t);
    }
}

/// <summary>The page a step-through is on, and which process it shows.</summary>
public enum DivisionPage { Mitosis, Meiosis, Compare, Cycle }

/// <summary>Cell Division's state, kept across its views: what the student set, each page's step-through, and what is
/// selected. The views remember the settings in Prefs (SimplBio.divide.…) and hand them in with <see cref="Set"/>.</summary>
public static class CellDivisionModel
{
    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();

    private static DivisionSequence? mitosis, meiosis;

    public static DivisionOptions Options { get; private set; } = new();

    /// <summary>The worked-out processes for the options set (made again when they change).</summary>
    public static DivisionSequence Mitosis => mitosis ??= DivisionModel.Sequence(DivisionProcess.Mitosis, Options);
    public static DivisionSequence Meiosis => meiosis ??= DivisionModel.Sequence(DivisionProcess.Meiosis, Options);

    public static DivisionSequence Of(DivisionProcess p) => p == DivisionProcess.Mitosis ? Mitosis : Meiosis;

    public static readonly BioStepController MitosisSteps = new(DivisionModel.Sequence(DivisionProcess.Mitosis, new DivisionOptions()).Steps);
    public static readonly BioStepController MeiosisSteps = new(DivisionModel.Sequence(DivisionProcess.Meiosis, new DivisionOptions()).Steps);
    public static readonly BioStepController CompareSteps = new(DivisionModel.CompareBioSteps);

    public static BioStepController StepsOf(DivisionProcess p) => p == DivisionProcess.Mitosis ? MitosisSteps : MeiosisSteps;

    /// <summary>Labels with leader lines on the drawing.</summary>
    public static bool ShowLabels { get; set; } = true;
    /// <summary>What is picked on a process's drawing, its card shown: a labelled part ("centromere", "tetrad" …: a key of
    /// DivisionModel.Parts) or a chromatid ("chromatid:3", by its index). Put away when the page changes.</summary>
    public static string? Selected { get; set; }
    /// <summary>The cell cycle page's choice: a phase ("g1" …, "g0") or a checkpoint ("g1s", "g2m", "spindle"); none: how
    /// to read the wheel.</summary>
    public static string? CycleSelected { get; set; }
    /// <summary>The cell cycle page's "cancer" view: what goes wrong when the checkpoints fail.</summary>
    public static bool Cancer { get; set; }

    /// <summary>New options: both processes worked out again, each step-through kept on its step (and its text new).</summary>
    public static void Set(DivisionOptions o)
    {
        var pairs = Math.Clamp(o.Pairs, 2, 3);
        var chiasma = Math.Clamp(o.Chiasma, DivisionOptions.ChiasmaMin, DivisionOptions.ChiasmaMax);
        var flipped = o.Flipped.Length == 3 ? o.Flipped : o.Flipped.Concat(new[] { false, false, false }).Take(3).ToArray();
        // (the long pair is the reference: never flipped)
        flipped = new[] { false, flipped[1], flipped[2] };
        var clean = o with { Pairs = pairs, Chiasma = chiasma, Flipped = flipped };
        if (clean.Equals(Options) && mitosis != null) return;
        Options = clean;
        mitosis = null;
        meiosis = null;
        MitosisSteps.SetSteps(Mitosis.Steps, keepIndex: true);
        MeiosisSteps.SetSteps(Meiosis.Steps, keepIndex: true);
        Tell();
    }

    /// <summary>The steps' text as the options have it (the first time a view is made).</summary>
    public static void Prime()
    {
        if (mitosis != null) return;
        MitosisSteps.SetSteps(Mitosis.Steps, keepIndex: true);
        MeiosisSteps.SetSteps(Meiosis.Steps, keepIndex: true);
    }

    /// <summary>Independent assortment: a pair's homologs swap which pole they face in metaphase I.</summary>
    public static void Flip(int pair)
    {
        if (pair < 1 || pair > 2) return;
        var f = Options.Flipped.ToArray();
        f[pair] = !f[pair];
        Set(Options with { Flipped = f });
    }
}

public static partial class BioSelfCheck
{
    /// <summary>Cell Division's own bio-check lines: the counts at every step against §5.2's tables, the keyframes against
    /// §5.2.4's rules, crossing over, independent assortment, nondisjunction, and the cell cycle's wheel.</summary>
    static partial void DivideChecks(BioCheckReport r)
    {
        var o = new DivisionOptions();
        var mit = DivisionModel.Sequence(DivisionProcess.Mitosis, o);
        var mei = DivisionModel.Sequence(DivisionProcess.Meiosis, o);
        static string P(string s) => DivisionModel.Plain(s);

        // (§5.2.1, a step a line: the model's counts, the human's where the table has them)
        static bool Cell(string table, string model, string human)
        {
            var plainTable = P(table);
            if (Regex.IsMatch(plainTable, @"^\d+ \(\d+\)$")) return plainTable == $"{model} ({human})";
            return plainTable == model;
        }
        for (var i = 0; i < mit.Counts.Count; i++)
        {
            var c = mit.Counts[i];
            var row = DivisionModel.MitosisTable[i];
            var ok = Cell(row[2], c.Chromosomes, c.ChromosomesHuman) && Cell(row[3], c.Chromatids, c.ChromatidsHuman)
                && P(row[4]) == c.Ploidy && P(row[5]) == c.Dna && row[0].StartsWith(mit.Stages[i].Title[..1], StringComparison.Ordinal);
            r.Check($"divide.mitosis.{mit.Stages[i].Key.Raw()}", $"{c.Chromosomes}|{c.Chromatids}|{c.Ploidy}|{c.Dna}", ok);
        }
        r.Check("divide.mitosis.steps", $"{mit.Stages.Count}", mit.Stages.Count == DivisionModel.MitosisTable.Length - 1);

        // (§5.2.2: the interphase row is G1 → G2; the rest a step each; "(no S phase)": no new chromatids between telophase
        //  I and prophase II)
        var mc = mei.Counts;
        var inter = DivisionModel.MeiosisTable[0];
        r.Check("divide.meiosis.interphase", $"{mc[0].Chromosomes}|{mc[0].Chromatids}→{mc[2].Chromatids}|{mc[0].Dna}→{mc[2].Dna}",
            mc[0].Chromosomes == inter[2] && mc[2].Chromosomes == inter[2] && $"{mc[0].Chromatids} → {mc[2].Chromatids}" == inter[3]
            && mc[0].Ploidy == inter[4] && mc[2].Ploidy == inter[4] && $"{mc[0].Dna} → {mc[2].Dna}" == inter[5]
            && mc[1].Chromatids == "8" && mc[1].Dna == "4c");
        foreach (var (step, row) in new[] { (3, 1), (4, 2), (5, 3), (6, 4), (7, 6), (8, 7), (9, 8), (10, 9) })
        {
            var c = mc[step];
            var x = DivisionModel.MeiosisTable[row];
            var ok = P(x[2]) == c.Chromosomes && P(x[3]) == c.Chromatids && P(x[4]) == c.Ploidy && P(x[5]) == c.Dna;
            r.Check($"divide.meiosis.{mei.Stages[step].Key.Raw()}", $"{c.Chromosomes}|{c.Chromatids}|{c.Ploidy}|{c.Dna}", ok);
        }
        var frames = mei.Frames;
        var telI = new DivTally(frames[6].Chromatids.Where(c => c.Cell == 0), 2);
        var proII = new DivTally(frames[7].Chromatids.Where(c => c.Cell == 0), 2);
        r.Check("divide.meiosis.noS", $"{telI.Chromatids}→{proII.Chromatids}", telI.Chromatids == proII.Chromatids && telI.Units == proII.Units);
        r.Check("divide.meiosis.haploidReplicated", $"{telI.Ploidy}/{telI.Chromatids / Math.Max(telI.Chromosomes, 1)}", telI.Ploidy == "n" && telI.Chromatids == 2 * telI.Chromosomes);
        r.Check("divide.meiosis.tetradsHuman", mc[3].ChromosomesHuman, mc[3].ChromosomesHuman == "46 (as 23 tetrads)");
        r.Check("divide.homologs.mitosis", mit.Counts[5].Homologs, mit.Counts[5].Homologs.StartsWith("Yes", StringComparison.Ordinal));
        r.Check("divide.homologs.afterMeiosisI", mc[6].Homologs, mc[6].Homologs == "No");
        r.Check("divide.sisters", $"{mit.Counts[0].Sisters}|{mit.Counts[5].Sisters}|{mit.Counts[6].Sisters}|{mc[5].Sisters}|{mc[9].Sisters}",
            mit.Counts[0].Sisters == "Not copied yet" && mit.Counts[5].Sisters == "Yes" && mit.Counts[6].Sisters.StartsWith("No", StringComparison.Ordinal)
            && mc[5].Sisters.StartsWith("Yes", StringComparison.Ordinal) && mc[9].Sisters.StartsWith("No", StringComparison.Ordinal));

        // (2n = 6: the same rules, three pairs)
        var six = new DivisionOptions { Pairs = 3 };
        var mit6 = DivisionModel.Sequence(DivisionProcess.Mitosis, six);
        var mei6 = DivisionModel.Sequence(DivisionProcess.Meiosis, six);
        r.Check("divide.six", $"{mit6.Counts[5].Chromosomes}|{mit6.Counts[6].Chromosomes}|{mei6.Counts[3].Chromosomes}|{mei6.Counts[10].Chromosomes}",
            mit6.Counts[5].Chromosomes == "6" && mit6.Counts[6].Chromosomes == "12" && mei6.Counts[3].Chromosomes == "6 (as 3 tetrads)"
            && mei6.Counts[10].Chromosomes == "3 per cell" && mei6.Counts[10].Dna == "1c" && mit6.Counts[0].ChromosomesHuman == "46");

        // (§5.2.4's geometry)
        r.Near("divide.thread.long", DivGeometry.ThreadLength(DivGeometry.Pairs[0], DivGeometry.Wiggle), 0.9, 0.002, 4);
        var straight = new DivChromatidKey(new DivChromatidID(1, 0, 0)) { K = 1 };
        r.Near("divide.condensed.short", DivGeometry.Length(DivGeometry.Shape(straight, DivGeometry.Pairs[1], 0.5).Points), 0.24, 1e-6, 4);
        r.Near("divide.condensed.long", DivGeometry.Span(DivGeometry.Pairs[0], 1), 0.42, 1e-9, 4);
        r.Near("divide.sisters.apart", 2 * DivGeometry.SisterOffset(1), 0.055 + 0.012, 1e-9, 4);
        var zeroExtents = Enumerable.Repeat((0.0, 0.0), 4).ToList();
        var spaced = DivisionBuilder.Plate(zeroExtents, 0.22, 2).Select((x, i) => Math.Abs(x - (i - 1.5) * 0.22) < 1e-12).All(b => b);
        r.Check("divide.plate.spacing", "0.22", spaced);
        var mframes = mit.Frames;
        var meta = mframes[5];
        var onPlate = meta.Chromatids.All(c => Math.Abs(c.C.Y) < 1e-12 && Math.Abs(Math.Sin(c.A2)) < 1e-12);
        var xs = meta.Chromatids.Select(c => c.C.X).OrderBy(x => x).ToList();
        var clear = xs.Zip(xs.Skip(1)).All(p => p.Second - p.First < 1e-12 || p.Second - p.First >= 0.22 - 1e-12);
        r.Check("divide.metaphase.plate", "y0,φ0", onPlate && clear && meta.Poles[0].P == new DivPoint(0, 0.82) && meta.Poles[1].P == new DivPoint(0, -0.82));
        var ana = mframes[6];
        r.Check("divide.anaphase", "±0.62,V55", ana.Chromatids.All(c => Math.Abs(Math.Abs(c.C.Y) - 0.62) < 1e-12 && Math.Abs(Math.Abs(DivMath.Turn(c.A1, c.A2)) - DivMath.Radians(110)) < 1e-9));
        var tel = mframes[7];
        // (the furrow: the cell 0.2 wide where it crosses y = 0, the waist, and its outline drawn pinched there)
        var pinched = DivShape.Pinched(1, 0.1);
        var waist = tel.Cells.SelectMany(c => Enumerable.Range(0, DivCellKey.Samples).Where(i => c.Membrane[i] > 0.5).Select(c.Point))
            .Where(p => Math.Abs(p.Y) < 0.12).Select(p => Math.Abs(p.X)).DefaultIfEmpty(1).Min();
        r.Check("divide.telophase", "waist 0.2, drawn " + waist.ToString("0.000", CultureInfo.InvariantCulture),
            tel.Nuclei[0].C == new DivPoint(0, 0.62) && tel.Nuclei[1].C == new DivPoint(0, -0.62) && tel.Nuclei.All(nu => nu.R == 0.32)
            && pinched.Inside(new DivPoint(0.0999, 0)) && !pinched.Inside(new DivPoint(0.1001, 0)) && waist < 0.25);
        var m1 = frames[4];
        var homologYs = m1.Chromatids.Select(c => Math.Round(c.C.Y * 1000, MidpointRounding.AwayFromZero) / 1000).ToHashSet();
        r.Check("divide.metaphaseI", string.Join(",", homologYs.OrderBy(y => y).Select(y => y.ToString(CultureInfo.InvariantCulture))),
            homologYs.SetEquals(new[] { 0.06, -0.06 }));
        r.Near("divide.telophaseI.cells", frames[6].Bounds.MaxY, 0.9 + 0.72, 0.01, 3);
        var m2 = frames[8];
        r.Check("divide.metaphaseII.poles", "±0.55",
            m2.Poles.Select(p => p.P).SequenceEqual(new[] { new DivPoint(-0.55, 0.9), new DivPoint(0.55, 0.9), new DivPoint(-0.55, -0.9), new DivPoint(0.55, -0.9) })
            && m2.Chromatids.All(c => Math.Abs(c.C.X) < 1e-12 && Math.Abs(Math.Cos(c.A2)) < 1e-12));
        r.Near("divide.telophaseII.cells", frames[10].Bounds.MaxX, 0.62 + 0.48, 0.01, 3);
        r.Check("divide.mix.ends", "0,1", mframes[5].Mixed(mframes[6], 0).Same(mframes[5]) && mframes[5].Mixed(mframes[6], 1).Same(mframes[6]));

        // (crossing over: the long pair's two non-sister chromatids, swapped beyond the chiasma; four different gametes;
        //  without it, gametes in identical pairs)
        static string Signature(DivProduct p) => string.Join(",", p.Chromatids.Select(c => $"{c.Id.Pair}{c.Id.Homolog}{(c.Recombined > 0.5 ? "r" : "")}"));
        var recombinants = frames[5].Chromatids.Where(c => c.Recombined > 0.5).ToList();
        r.Check("divide.crossing.recombinants", string.Join(",", recombinants.Select(c => $"{c.Id.Pair}{c.Id.Homolog}{c.Id.Sister}")),
            recombinants.Count == 2 && recombinants.Select(c => c.Id.Homolog).ToHashSet().SetEquals(new[] { 0, 1 }) && recombinants.All(c => c.Id.Pair == 0));
        var crossed = mei.Products.Select(Signature).ToHashSet();
        var noCross = o with { CrossingOver = false };
        var plainGametes = DivisionModel.Sequence(DivisionProcess.Meiosis, noCross).Products.Select(Signature).ToHashSet();
        r.Check("divide.crossing.gametes", $"{crossed.Count}/{plainGametes.Count}", crossed.Count == 4 && plainGametes.Count == 2);

        // (independent assortment: over the 2ⁿ⁻¹ ways to flip the other pairs, 2ⁿ different gametes)
        foreach (var n in new[] { 2, 3 })
        {
            var kinds = new HashSet<string>();
            for (var mask = 0; mask < 1 << (n - 1); mask++)
            {
                var x = noCross with { Pairs = n, Flipped = new[] { false }.Concat(Enumerable.Range(1, 2).Select(p => (mask & (1 << (p - 1))) != 0)).ToArray() };
                foreach (var p in DivisionModel.Sequence(DivisionProcess.Meiosis, x).Products) kinds.Add(Signature(p));
            }
            r.Check($"divide.assortment.{2 * n}", $"{kinds.Count}", kinds.Count == 1 << n);
        }
        r.Same("divide.assortment.text", DivisionModel.Assortments(2), "2ⁿ = 4 possible assortments; humans 2²³ ≈ 8.4 million");

        // (nondisjunction: n+1, n+1, n−1, n−1 after meiosis I; n+1, n−1, n, n after meiosis II)
        foreach (var (nd, want) in new[] { (DivisionNondisjunction.MeiosisI, new[] { "n+1", "n+1", "n−1", "n−1" }), (DivisionNondisjunction.MeiosisII, new[] { "n+1", "n−1", "n", "n" }) })
        {
            var s = DivisionModel.Sequence(DivisionProcess.Meiosis, o with { Nondisjunction = nd });
            var got = s.Products.Select(p => p.Tally.Ploidy).ToList();
            r.Check($"divide.nondisjunction.{nd.Raw()}", string.Join(",", got), got.SequenceEqual(want) && s.Counts[10].Listing != null);
        }

        // (the cell cycle: 24 h, drawn in proportion)
        var arcs = DivisionModel.CycleArcs;
        var sweeps = arcs.Select(a => a.End - a.Start).ToList();
        r.Check("divide.cycle", string.Join(",", sweeps.Select(s => s.ToString("0.0", CultureInfo.InvariantCulture))),
            DivisionModel.CyclePhases.Sum(p => p.Hours) == 24 && sweeps.Zip(new[] { 165.0, 120, 60, 15 }).All(z => Math.Abs(z.First - z.Second) < 1e-9)
            && Math.Abs(arcs[0].Start - 7.5) < 1e-9 && DivisionModel.Checkpoints.Length == 3);
        // (every label, in every case, has a card; every step's labels fit (≤ 14) and point at something shown)
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var hidden = new List<string>();
        foreach (var p in new[] { DivisionProcess.Mitosis, DivisionProcess.Meiosis })
            foreach (var plant in new[] { false, true })
                foreach (var nd in new[] { DivisionNondisjunction.None, DivisionNondisjunction.MeiosisI, DivisionNondisjunction.MeiosisII })
                {
                    var s = DivisionModel.Sequence(p, new DivisionOptions { Plant = plant, Nondisjunction = nd });
                    for (var i = 0; i < s.Keyframes.Count; i++)
                    {
                        var kf = s.Keyframes[i];
                        var shapes = kf.Frame.Shapes(0.5);
                        foreach (var l in kf.Labels)
                        {
                            if (!DivisionModel.Parts.ContainsKey(l.Part)) missing.Add(l.Part);
                            if (!DivPainter.Anchor(l.Anchor, kf.Frame, shapes).Visible) hidden.Add($"{p.ToString().ToLowerInvariant()}{i + 1}:{l.Part}");
                        }
                        if (kf.Labels.Count > BioLabelLayout.MaxLabels) hidden.Add($"{p.ToString().ToLowerInvariant()}{i + 1}:too-many");
                        if (kf.Labels.Select(l => l.Id).Distinct().Count() != kf.Labels.Count) hidden.Add($"{p.ToString().ToLowerInvariant()}{i + 1}:same-label");
                    }
                }
        r.Check("divide.labels", missing.Count == 0 && hidden.Count == 0 ? "all" : string.Join(",", missing.Concat(hidden)), missing.Count == 0 && hidden.Count == 0);
        // (§3.6: a step's title is at most 40 characters, its text one to three sentences)
        var titles = mit.Stages.Select(s => s.Title).Concat(mei.Stages.Select(s => s.Title)).Concat(DivisionModel.CompareSteps.Select(s => s.Title));
        var longest = titles.Max(t => t.Length);
        r.Check("divide.titles", $"{longest}", longest <= 40 && mit.Stages.Concat(mei.Stages).All(s => s.Text.Length > 0 && (s.Text.EndsWith('.') || s.Text.EndsWith('?'))));
        r.Check("divide.compare", $"{DivisionModel.CompareSteps.Length}/{DivisionModel.CompareTable.Length}",
            DivisionModel.CompareTable.Length == 9 && DivisionModel.CompareSteps.All(s => s.Mitosis < mit.Stages.Count && s.Meiosis < mei.Stages.Count));
    }
}

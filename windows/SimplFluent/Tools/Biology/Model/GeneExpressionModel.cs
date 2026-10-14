using System.Globalization;

namespace Simpl.Tools;

// Gene Expression's own model (spec §4.3, §6.1, §6.12), the Mac's GeneExpressionModel.swift: the genes it works on — the
// made-up teaching gene, human β-globin's coding sequence and a sequence of one's own — with what they are transcribed
// and translated into; the codon table's grid and wheel and its lookup both ways; the reading frames; and the tool's
// state, kept in a static class so it outlives the view being built again (a view subscribes to Changed on Loaded and
// lets go on Unloaded). The step-through's steps and scenes are in GeneExpressionProcess.cs and the mutation classifier
// in GeneExpressionMutations.cs. Plain .NET, no WPF, so the unit tests reach it; its bio-check lines (GeneChecks) are the
// Mac's, name for name and value for value.

// ---- genes ----

/// <summary>What kind of cell the step-through shows (§4.3 Cell type).</summary>
public enum GXCell
{
    /// <summary>A nucleus, RNA processing (cap, tail, splicing), export, and a ribosome that scans from the cap.</summary>
    Eukaryote,
    /// <summary>No nucleus and no introns; the ribosome binds the Shine–Dalgarno site and can start before transcription ends.</summary>
    Prokaryote,
}

/// <summary>Which gene the tool works on (§4.3 Gene presets).</summary>
public enum GXSource { Teaching, Hbb, Own }

public static class GXNames
{
    public static string Label(this GXCell c) => c == GXCell.Eukaryote ? "Eukaryote" : "Prokaryote";
    public static string Key(this GXCell c) => c == GXCell.Eukaryote ? "eukaryote" : "prokaryote";

    public static string Label(this GXSource s) => s switch
    {
        GXSource.Teaching => "Teaching gene",
        GXSource.Hbb => "Human β-globin (HBB)",
        _ => "Type your own",
    };

    /// <summary>"teaching", "hbb", "own" (the Mac's raw values, and a launch's gene=…).</summary>
    public static string Key(this GXSource s) => s switch { GXSource.Teaching => "teaching", GXSource.Hbb => "hbb", _ => "own" };

    public static GXSource? Source(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "teaching" => GXSource.Teaching, "hbb" => GXSource.Hbb, "own" => GXSource.Own, _ => null,
    };

    public static GXCell? Cell(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "eukaryote" => GXCell.Eukaryote, "prokaryote" => GXCell.Prokaryote, _ => null,
    };

    public static BioSeqKind? Kind(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "dna" => BioSeqKind.Dna, "rna" => BioSeqKind.Rna, "protein" => BioSeqKind.Protein, _ => null,
    };
}

/// <summary>Columns <c>Lo</c>…<c>Hi</c>, both included (0-based).</summary>
public readonly record struct GXSpan(int Lo, int Hi)
{
    public int Count => Hi - Lo + 1;
    public bool Contains(int i) => i >= Lo && i <= Hi;
    public override string ToString() => $"{Lo}...{Hi}";
}

/// <summary>What a stretch of a gene is: a band (promoter, UTR, exon, intron, coding sequence) or a boxed mark.</summary>
public enum GXRegionKind { Promoter, Utr5, Exon, Intron, Utr3, Cds, Transcribed, Tata, Start, Stop, PolyA }

/// <summary>A stretch of a gene's coding strand: a band (promoter, UTR, exon, intron, coding sequence) or a boxed mark
/// (TATA box, start and stop codons, poly-A signal). Columns are 0-based and inclusive.</summary>
public sealed record GXRegion(GXRegionKind Kind, string Name, GXSpan Range);

/// <summary>A codon of the reading frame, from the start codon on.</summary>
/// <param name="Index">0 for the start codon.</param>
/// <param name="Mature">Its first base's index in the mature mRNA.</param>
/// <param name="Codon">RNA letters, 5′→3′.</param>
/// <param name="Aa">One letter; '*' for a stop, 'X' for a codon with N.</param>
public sealed record GXCodon(int Index, int Mature, string Codon, char Aa);

/// <summary>
/// A gene as the step-through draws it: its coding strand by columns, its parts, and what it is transcribed and
/// translated into. A column is a base pair's place left to right; the RNA made from it is drawn under it.
/// </summary>
public sealed class GXGene
{
    public GXSource Source { get; }
    public GXCell Cell { get; }
    public string Title { get; }
    /// <summary>The gene in a sentence (the teaching gene says it is made up).</summary>
    public string Note { get; }
    /// <summary>The coding (sense) strand, 5′→3′, DNA letters.</summary>
    public string Coding { get; }
    /// <summary>Bands: promoter, UTRs, exons, introns, coding sequence.</summary>
    public IReadOnlyList<GXRegion> Regions { get; }
    /// <summary>Boxes: TATA box, start codon, stop codon, poly-A signal.</summary>
    public IReadOnlyList<GXRegion> Marks { get; }
    /// <summary>The first column transcribed (+1) and the last.</summary>
    public int TxStart { get; }
    public int TxEnd { get; }
    /// <summary>Introns, in columns.</summary>
    public IReadOnlyList<GXSpan> Introns { get; }

    /// <summary>The template strand, written 3′→5′ under the coding strand.</summary>
    public string Template { get; }
    /// <summary>The RNA made from columns TxStart…TxEnd, 5′→3′, U for T.</summary>
    public string PreMRNA { get; }
    /// <summary>The mature mRNA: introns out (cap and tail not counted).</summary>
    public string Mature { get; }
    /// <summary>Each mature base's index in the pre-mRNA.</summary>
    public IReadOnlyList<int> MatureToPre { get; }
    /// <summary>The first AUG of the mature mRNA, where the ribosome starts (null: none).</summary>
    public int? CdsStart { get; }
    /// <summary>The reading frame's codons, from the AUG to the stop (included) or the last whole codon.</summary>
    public IReadOnlyList<GXCodon> Codons { get; }
    /// <summary>The protein, N→C, without the stop.</summary>
    public string Protein { get; }
    /// <summary>The frame ends in a stop codon (false: it runs to the end of the mRNA).</summary>
    public bool HasStop { get; }

    public GXGene(GXSource source, GXCell cell, string title, string note, string coding, IReadOnlyList<GXRegion> regions,
        IReadOnlyList<GXRegion> marks, int txStart, int txEnd, IReadOnlyList<GXSpan> introns)
    {
        Source = source;
        Cell = cell;
        Title = title;
        Note = note;
        Coding = coding;
        Regions = regions;
        Marks = marks;
        TxStart = txStart;
        TxEnd = Math.Max(txStart, txEnd);
        Introns = introns;
        Template = new string(coding.Select(b => BioSeq.Pair(b, rna: false)).ToArray());
        var pre = coding.Length == 0 ? "" : new string(Enumerable.Range(txStart, TxEnd - txStart + 1).Select(i => coding[i] == 'T' ? 'U' : coding[i]).ToArray());
        PreMRNA = pre;
        var keep = new List<int>();
        for (var i = 0; i < pre.Length; i++)
            if (!introns.Any(s => s.Contains(txStart + i))) keep.Add(i);
        MatureToPre = keep;
        var m = new string(keep.Select(i => pre[i]).ToArray());
        Mature = m;
        var at = m.IndexOf("AUG", StringComparison.Ordinal);
        CdsStart = at >= 0 ? at : null;
        var cs = new List<GXCodon>();
        var stop = false;
        if (CdsStart is { } s0)
        {
            for (var j = s0; j + 3 <= m.Length; j += 3)
            {
                var c = m.Substring(j, 3);
                var aa = BioCode.AminoAcid(c) ?? 'X';
                cs.Add(new GXCodon(cs.Count, j, c, aa));
                if (aa == '*')
                {
                    stop = true;
                    break;
                }
            }
        }
        Codons = cs;
        Protein = new string(cs.Where(c => c.Aa != '*').Select(c => c.Aa).ToArray());
        HasStop = stop;
    }

    /// <summary>The gene a source names, as the cell has it (Own: the cleaned sequence, DNA letters).</summary>
    public static GXGene? Make(GXSource source, GXCell cell, string own = "") => source switch
    {
        GXSource.Teaching => Teaching(cell),
        GXSource.Hbb => Hbb(cell),
        _ => Own(own, cell),
    };

    /// <summary>§6.12's teaching gene: in a eukaryote as written (promoter with TATA box, two exons, an intron, a poly-A
    /// signal); in a prokaryote with its intron left out of the DNA (bacterial genes have none) and its exons one coding
    /// sequence.</summary>
    public static GXGene Teaching(GXCell cell)
    {
        var g = BioGenes.Teaching;
        var all = g.Coding;
        static GXSpan Cols(BioGenes.Part p) => new(p.Start - 1, p.End - 1);
        var kinds = new Dictionary<string, GXRegionKind>
        {
            ["Promoter"] = GXRegionKind.Promoter, ["5′ UTR"] = GXRegionKind.Utr5, ["Exon 1"] = GXRegionKind.Exon,
            ["Intron"] = GXRegionKind.Intron, ["Exon 2"] = GXRegionKind.Exon, ["3′ UTR"] = GXRegionKind.Utr3,
        };
        var parts = g.Parts.Select(p => new GXRegion(kinds.GetValueOrDefault(p.Name, GXRegionKind.Transcribed), p.Name, Cols(p))).ToList();
        var introns = parts.Where(p => p.Kind == GXRegionKind.Intron).Select(p => p.Range).ToList();
        var start = new GXRegion(GXRegionKind.Start, "Start codon", Cols(g.StartCodon));
        var stop = new GXRegion(GXRegionKind.Stop, "Stop codon", Cols(g.StopCodon));
        if (cell == GXCell.Eukaryote)
        {
            return new GXGene(GXSource.Teaching, cell, "Teaching gene",
                "Made up for this tool: a promoter with a TATA box, two exons, one intron and a poly-A signal.",
                all, parts,
                new[] { new GXRegion(GXRegionKind.Tata, "TATA box", Cols(g.Tata)), start, stop, new GXRegion(GXRegionKind.PolyA, "Poly-A signal", Cols(g.PolyASignal)) },
                g.StartSite - 1, all.Length - 1, introns);
        }
        // (as a bacterium would have it: the intron out of the DNA itself, the exons one coding sequence)
        int Shift(int c) => c - introns.Where(s => s.Hi < c).Sum(s => s.Count);
        GXSpan ShiftSpan(GXSpan r) => new(Shift(r.Lo), Shift(r.Hi));
        var coding = new string(Enumerable.Range(0, all.Length).Where(i => !introns.Any(s => s.Contains(i))).Select(i => all[i]).ToArray());
        var exons = parts.Where(p => p.Kind == GXRegionKind.Exon).ToList();
        var regions = parts.Where(p => p.Kind is GXRegionKind.Promoter or GXRegionKind.Utr5).ToList();
        if (exons.Count > 0)
            regions.Add(new GXRegion(GXRegionKind.Cds, "Coding sequence", new GXSpan(Shift(exons[0].Range.Lo), Shift(exons[^1].Range.Hi))));
        regions.AddRange(parts.Where(p => p.Kind == GXRegionKind.Utr3).Select(p => p with { Range = ShiftSpan(p.Range) }));
        return new GXGene(GXSource.Teaching, cell, "Teaching gene",
            "Made up for this tool, shown as a bacterial gene: its intron is left out (bacterial genes have none).",
            coding, regions,
            new[] { start with { Range = ShiftSpan(start.Range) }, stop with { Range = ShiftSpan(stop.Range) } },
            g.StartSite - 1, coding.Length - 1, Array.Empty<GXSpan>());
    }

    /// <summary>Human β-globin's coding sequence (§6.12): the real gene's 3 exons joined, without its promoter, UTRs or introns.</summary>
    public static GXGene Hbb(GXCell cell)
    {
        var c = BioGenes.HbbCDS;
        var n = c.Length;
        return new GXGene(GXSource.Hbb, cell, "Human β-globin (HBB)",
            "Human β-globin's coding sequence (NM_000518.5), its 3 exons joined; the promoter, UTRs and 2 introns are not shown.",
            c, new[] { new GXRegion(GXRegionKind.Cds, "Coding sequence (3 exons joined)", new GXSpan(0, n - 1)) },
            new[] { new GXRegion(GXRegionKind.Start, "Start codon", new GXSpan(0, 2)), new GXRegion(GXRegionKind.Stop, "Stop codon", new GXSpan(n - 3, n - 1)) },
            0, n - 1, Array.Empty<GXSpan>());
    }

    /// <summary>A sequence of one's own (already cleaned, DNA letters), read as the coding strand and transcribed whole;
    /// its coding sequence runs from its first ATG to the first stop in that frame. Null when it is empty.</summary>
    public static GXGene? Own(string letters, GXCell cell)
    {
        var c = BioSeq.ToDna(letters.ToUpperInvariant());
        var n = c.Length;
        if (n == 0) return null;
        var at = c.IndexOf("ATG", StringComparison.Ordinal);
        var regions = new List<GXRegion>();
        var marks = new List<GXRegion>();
        if (at >= 0)
        {
            var s = at;
            int? end = null;
            for (var j = s; j + 3 <= n; j += 3)
                if (BioCode.IsStop(c.Substring(j, 3)))
                {
                    end = j + 2;
                    break;
                }
            var last = end ?? (s + (n - s) / 3 * 3 - 1);
            if (s > 0) regions.Add(new GXRegion(GXRegionKind.Utr5, "5′ UTR", new GXSpan(0, s - 1)));
            regions.Add(new GXRegion(GXRegionKind.Cds, "Coding sequence", new GXSpan(s, Math.Max(s, last))));
            if (last < n - 1) regions.Add(new GXRegion(GXRegionKind.Utr3, "3′ UTR", new GXSpan(last + 1, n - 1)));
            marks.Add(new GXRegion(GXRegionKind.Start, "Start codon", new GXSpan(s, s + 2)));
            if (end is { } e) marks.Add(new GXRegion(GXRegionKind.Stop, "Stop codon", new GXSpan(e - 2, e)));
        }
        else regions.Add(new GXRegion(GXRegionKind.Transcribed, "Transcribed", new GXSpan(0, n - 1)));
        return new GXGene(GXSource.Own, cell, "Your sequence",
            "Your sequence, read as the coding strand and transcribed whole; translation starts at its first AUG.",
            c, regions, marks, 0, n - 1, Array.Empty<GXSpan>());
    }

    // ---- where things are ----

    /// <summary>The coding sequence in DNA letters, from the start codon to the stop (the Mutations page's starting point).</summary>
    public string CdsDNA => BioSeq.ToDna(string.Concat(Codons.Select(c => c.Codon)));

    /// <summary>The mRNA after the coding sequence's stop (its 3′ UTR), DNA letters — where a stop-loss reads on into.</summary>
    public string DownstreamDNA
    {
        get
        {
            if (CdsStart is not { } s || !HasStop) return "";
            var from = s + Codons.Count * 3;
            return from < Mature.Length ? BioSeq.ToDna(Mature[from..]) : "";
        }
    }

    /// <summary>The column a pre-mRNA base is drawn at, <paramref name="splice"/> of the way through splicing: bases after an
    /// intron close up leftward by its length (an intron's own bases stay where they were, to loop away).</summary>
    public double Column(int pre, double splice = 0)
    {
        var col = TxStart + pre;
        var before = Introns.Where(s => s.Hi < col).Sum(s => s.Count);
        return col - splice * before;
    }

    /// <summary>The column of a mature-mRNA base once spliced (the mature mRNA lies contiguous from +1).</summary>
    public double ColumnOfMature(double m) => TxStart + m;

    /// <summary>The column of a codon's middle base on the mature mRNA (codon 0 = the start codon).</summary>
    public double CodonColumn(double j) => ColumnOfMature((CdsStart ?? 0) + 3 * j + 1);

    /// <summary>Whether a pre-mRNA base is in an intron.</summary>
    public bool InIntron(int pre) => Introns.Any(s => s.Contains(TxStart + pre));

    /// <summary>The columns the scene spans: from the cap's place left of +1 to the end of the poly-A tail (20 drawn).</summary>
    public (double Lo, double Hi) ColumnSpan
    {
        get
        {
            var left = Math.Min(0, TxStart - 2.0);
            var right = Math.Max(Coding.Length - 1.0, Column(PreMRNA.Length - 1) + GXProcess.TailDrawn + 1);
            return (left, right);
        }
    }

    /// <summary>The mature mRNA's base that starts a Kozak context GCCACCAUGG around the start codon, if it has one.</summary>
    public int? Kozak
    {
        get
        {
            if (CdsStart is not { } s || s < 6 || s + 4 > Mature.Length || Mature.Substring(s - 6, 10) != "GCCACCAUGG") return null;
            return s - 6;
        }
    }

    /// <summary>Exon junctions in the mature mRNA (the index of exon 2's first base, and so on).</summary>
    public IReadOnlyList<int> Junctions
    {
        get
        {
            var o = new List<int>();
            foreach (var intron in Introns)
            {
                // (the mature bases before the intron)
                var before = MatureToPre.Count(i => TxStart + i < intron.Lo);
                if (before > 0 && before < Mature.Length) o.Add(before);
            }
            return o;
        }
    }

    /// <summary>A codon split across an exon junction: its index, its bases and where the junction falls in it (1 or 2).</summary>
    public (int Index, string Codon, int Cut)? SplitCodon
    {
        get
        {
            if (CdsStart is not { } s) return null;
            foreach (var j in Junctions.Where(j => j > s))
            {
                int into = (j - s) % 3, k = (j - s) / 3;
                if (into != 0 && k < Codons.Count) return (k, Codons[k].Codon, into);
            }
            return null;
        }
    }
}

// ---- the codon table (§6.1) ----

/// <summary>A view of the codon table.</summary>
public enum GXCodonView { Grid, Wheel }

/// <summary>What a lookup asks for: a codon (DNA or RNA letters), an amino acid (name, three or one letter), the stops,
/// or nothing it can read.</summary>
public enum GXQueryKind { Codon, AminoAcid, Stop, Empty, Invalid }

/// <param name="Codon">RNA letters (Codon).</param>
/// <param name="Aa">The amino acid's one letter (AminoAcid).</param>
/// <param name="Why">What is wrong (Invalid).</param>
public sealed record GXQuery(GXQueryKind Kind, string Codon = "", char Aa = ' ', string Why = "")
{
    public static readonly GXQuery Empty = new(GXQueryKind.Empty);
    public static readonly GXQuery Stop = new(GXQueryKind.Stop);
    public static GXQuery OfCodon(string c) => new(GXQueryKind.Codon, Codon: c);
    public static GXQuery OfAminoAcid(char a) => new(GXQueryKind.AminoAcid, Aa: a);
    public static GXQuery Invalid(string why) => new(GXQueryKind.Invalid, Why: why);
}

/// <summary>A sector of the wheel: angles in degrees clockwise from 12 o'clock.</summary>
/// <param name="Ring">0 first base, 1 second, 2 third.</param>
/// <param name="Prefix">The codon's first one, two or three bases.</param>
public readonly record struct GXSector(int Ring, string Prefix, double From, double To)
{
    public double Mid => (From + To) / 2;
}

/// <summary>The codon table's grid and wheel, and its lookup (§4.3 Codon table).</summary>
public static class GXCodonTable
{
    /// <summary>The codon in a block of the grid: rows are the first base (U, C, A, G top to bottom), columns the second,
    /// and the third base runs U, C, A, G down inside each block.</summary>
    public static string Codon(int first, int second, int third) => new(new[] { BioCode.Bases[first], BioCode.Bases[second], BioCode.Bases[third] });

    /// <summary>The grid as §6.1 lays it out: 16 rows (4 per first base, third base within), 4 columns (second base).</summary>
    public static List<List<string>> GridRows =>
        Enumerable.Range(0, 4).SelectMany(first => Enumerable.Range(0, 4).Select(third => Enumerable.Range(0, 4).Select(second => Codon(first, second, third)).ToList())).ToList();

    /// <summary>RNA letters, or DNA coding-strand letters (T for U) when the table is switched to them.</summary>
    public static string Letters(string s, bool dna) => dna ? BioSeq.ToDna(s) : s;

    /// <summary>The wheel's ring radii as fractions of its radius (§4.3): first base between 0.18 and 0.36, second to 0.56,
    /// third to 0.78; the amino acids' labels at 0.90.</summary>
    public static readonly double[] RingRadii = { 0.18, 0.36, 0.56, 0.78 };
    public const double LabelRadius = 0.90;

    /// <summary>The sectors of a ring, in U, C, A, G order clockwise from 12 o'clock: 4 of 90°, 16 of 22.5°, 64 of 5.625°.</summary>
    public static List<GXSector> Sectors(int ring)
    {
        var r = Math.Min(Math.Max(ring, 0), 2);
        var count = new[] { 4, 16, 64 }[r];
        var width = 360.0 / count;
        var o = new List<GXSector>();
        for (var k = 0; k < count; k++)
        {
            var prefix = "";
            var rest = k;
            for (var level = r; level >= 0; level--)
            {
                var p = (int)Math.Pow(4, level);
                prefix += BioCode.Bases[rest / p];
                rest %= p;
            }
            o.Add(new GXSector(r, prefix, k * width, (k + 1) * width));
        }
        return o;
    }

    /// <summary>The amino acids round the wheel: runs of the same amino acid within each second-base block, with their angles.</summary>
    public static List<(char Aa, List<string> Codons, double From, double To)> WheelLabels
    {
        get
        {
            var o = new List<(char Aa, List<string> Codons, double From, double To)>();
            foreach (var s in Sectors(2))
            {
                var aa = BioCode.AminoAcid(s.Prefix) ?? '?';
                if (o.Count > 0 && o[^1].Aa == aa && o[^1].Codons[^1][..2] == s.Prefix[..2])
                {
                    var last = o[^1];
                    o[^1] = (aa, last.Codons.Append(s.Prefix).ToList(), last.From, s.To);
                }
                else o.Add((aa, new List<string> { s.Prefix }, s.From, s.To));
            }
            return o;
        }
    }

    /// <summary>The codon (or the first one or two bases) at a point of the wheel: <paramref name="r"/> as a fraction of the
    /// radius, <paramref name="angle"/> in degrees clockwise from 12 o'clock. Past the third ring (the amino acids' band)
    /// it is the codon under it.</summary>
    public static string? Hit(double r, double angle)
    {
        var a = (angle % 360 + 360) % 360;
        if (r < RingRadii[0] || r > 1) return null;
        var ring = r < RingRadii[1] ? 0 : (r < RingRadii[2] ? 1 : 2);
        foreach (var s in Sectors(ring)) if (a >= s.From && a < s.To) return s.Prefix;
        return null;
    }

    /// <summary>Reads the lookup field: three bases are a codon ("AUG", "atg"); "stop", "*" or "ter" the stops; anything
    /// else an amino acid by name ("lysine"), three letters ("Lys") or one letter ("K").</summary>
    public static GXQuery Parse(string text)
    {
        var q = text.Trim();
        if (q.Length == 0) return GXQuery.Empty;
        var up = new string(q.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        var bare = up.Replace("5′", "").Replace("3′", "").Replace("5'", "").Replace("3'", "");
        if (bare.Length == 3 && bare.All(c => "ACGTU".Contains(c)))
        {
            if (bare.Contains('T') && bare.Contains('U')) return GXQuery.Invalid("A codon has U (RNA) or T (DNA), not both.");
            return GXQuery.OfCodon(bare.Replace('T', 'U'));
        }
        if (bare is "STOP" or "*" or "TER" or "END") return GXQuery.Stop;
        if (BioAminoAcids.Find(q) is { } aa) return GXQuery.OfAminoAcid(aa.One);
        if (bare.Length < 3 && bare.All(c => "ACGTU".Contains(c))) return GXQuery.Invalid("A codon is three bases.");
        return GXQuery.Invalid($"No codon or amino acid “{q}”.");
    }

    /// <summary>The codons a lookup lights (RNA letters).</summary>
    public static HashSet<string> Matches(GXQuery q) => q.Kind switch
    {
        GXQueryKind.Codon => new HashSet<string> { q.Codon },
        GXQueryKind.AminoAcid => BioCode.Codons(q.Aa).ToHashSet(),
        GXQueryKind.Stop => BioCode.Codons('*').ToHashSet(),
        _ => new HashSet<string>(),
    };

    /// <summary>The class words a mutation card and a codon card use ("acidic, charged").</summary>
    public static string ClassWords(BioAminoAcid aa) => aa.Group switch
    {
        BioAAClass.Nonpolar => "nonpolar",
        BioAAClass.Polar => "polar, uncharged",
        BioAAClass.Acidic => "acidic, charged",
        _ => aa.One == 'H' ? "basic, mostly uncharged at pH 7.4" : "basic, charged",
    };
}

// ---- reading frames (§4.3) ----

/// <summary>An amino acid of a reading frame, under the codon it comes from.</summary>
/// <param name="Aa">One letter; '*' a stop.</param>
/// <param name="Codon">The codon as read, 5′→3′ on its own strand (RNA letters).</param>
/// <param name="Column">The forward strand's first column the codon covers (0-based; a reverse frame's codon covers the
/// same three columns, read the other way).</param>
/// <param name="Number">The codon's number in its frame (1-based).</param>
public sealed record GXFrameCell(char Aa, string Codon, int Column, int Number);

/// <summary>A frame: +1, +2, +3 on the strand as typed; −1, −2, −3 on its reverse complement.</summary>
public sealed record GXFrameRow(int Frame, IReadOnlyList<GXFrameCell> Cells)
{
    public string Label => Frame > 0 ? $"+{Frame}" : $"−{-Frame}";
}

/// <summary>A reading-frame preset: its name, its sequence and whether it is DNA.</summary>
public sealed record GXFramesPreset(string Name, string Sequence, bool Dna);

public static class GXReadingFrames
{
    /// <summary>The three forward frames and, for DNA, the three reverse ones, each amino acid placed under its codon's columns.</summary>
    public static List<GXFrameRow> Rows(string sequence, bool reverse)
    {
        var s = sequence.ToUpperInvariant();
        var n = s.Length;
        var o = new List<GXFrameRow>();
        static string Rna(string c) => c.Replace('T', 'U');
        for (var f = 0; f < 3; f++)
        {
            var cells = new List<GXFrameCell>();
            for (var i = f; i + 3 <= n; i += 3)
            {
                var c = Rna(s.Substring(i, 3));
                cells.Add(new GXFrameCell(BioCode.AminoAcid(c) ?? '?', c, i, cells.Count + 1));
            }
            o.Add(new GXFrameRow(f + 1, cells));
        }
        if (!reverse) return o;
        var rc = BioSeq.ReverseComplement(BioSeq.ToDna(s));
        for (var f = 0; f < 3; f++)
        {
            var cells = new List<GXFrameCell>();
            for (var i = f; i + 3 <= n; i += 3)
            {
                var c = Rna(rc.Substring(i, 3));
                // (reverse-complement positions i…i+2 are forward columns n−3−i…n−1−i)
                cells.Add(new GXFrameCell(BioCode.AminoAcid(c) ?? '?', c, n - 3 - i, cells.Count + 1));
            }
            o.Add(new GXFrameRow(-(f + 1), cells));
        }
        return o;
    }

    /// <summary>The presets: the teaching gene's mature mRNA (its ORF in frame +2), its whole gene as DNA, and HBB's coding sequence.</summary>
    public static IReadOnlyList<GXFramesPreset> Presets => new[]
    {
        new GXFramesPreset("Teaching gene mRNA", BioGenes.Teaching.MatureMRNA, false),
        new GXFramesPreset("Teaching gene DNA (112 bp)", BioGenes.Teaching.Coding, true),
        new GXFramesPreset("β-globin coding sequence", BioGenes.HbbCDS, true),
    };
}

// ---- what the tool keeps ----

/// <summary>Which coding sequence the Mutations page starts from.</summary>
public enum GXMutationGene { Teaching, Hbb, Current }

public static class GXMutationGenes
{
    public static string Label(this GXMutationGene g) => g switch { GXMutationGene.Teaching => "Teaching gene", GXMutationGene.Hbb => "β-globin", _ => "Your gene" };
    public static string Key(this GXMutationGene g) => g switch { GXMutationGene.Teaching => "teaching", GXMutationGene.Hbb => "hbb", _ => "current" };
    public static GXMutationGene? Parse(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "teaching" => GXMutationGene.Teaching, "hbb" => GXMutationGene.Hbb, "current" => GXMutationGene.Current, _ => null,
    };
}

/// <summary>
/// Gene Expression's state, shared by its pages and kept while the tool is closed (the Mac's GXStore): the gene and the
/// step-through, the codon table's view and lookup, the reading frames' sequence, and the mutation. Choices are
/// remembered on this PC (Prefs SimplBio.gene.*, through <see cref="Recall"/> and <see cref="Remember"/>, which the
/// tool's file sets to BioPrefs; unset — the unit tests — nothing is kept).
/// </summary>
public static class GeneExpressionModel
{
    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();

    /// <summary>A remembered choice by its field ("source"), or null.</summary>
    public static Func<string, string?> Recall { get; set; } = _ => null;

    /// <summary>A choice remembered (null forgets it).</summary>
    public static Action<string, string?> Remember { get; set; } = (_, _) => { };

    private static string? Read(string field)
    {
        try { return Recall(field); } catch { return null; }
    }

    private static void Keep(string field, string? value)
    {
        try { Remember(field, value); } catch { /* (kept for this run only) */ }
    }

    private static bool loaded;

    /// <summary>The remembered choices read, once (on first use, after the tool's file has set Recall).</summary>
    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        source = GXNames.Source(Read("source")) ?? GXSource.Teaching;
        cell = GXNames.Cell(Read("cell")) ?? GXCell.Eukaryote;
        ownText = Read("own") ?? DefaultOwn;
        ownKind = GXNames.Kind(Read("ownKind")) is BioSeqKind.Rna ? BioSeqKind.Rna : BioSeqKind.Dna;
        codonView = Read("codonView") == "wheel" ? GXCodonView.Wheel : GXCodonView.Grid;
        dnaLetters = Read("dnaLetters") == "true";
        framesText = Read("frames") ?? BioGenes.Teaching.MatureMRNA;
        framesKind = GXNames.Kind(Read("framesKind")) is BioSeqKind.Dna ? BioSeqKind.Dna : BioSeqKind.Rna;
        minCodons = int.TryParse(Read("minORF"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? Math.Clamp(m, 2, 300) : 10;
        showReverse = Read("reverse") != "false";
        mutationGene = GXMutationGenes.Parse(Read("mutationGene")) ?? GXMutationGene.Teaching;
        Rebuild(tell: false);
        RefreshFrames(tell: false);
    }

    /// <summary>"Type your own"'s first sequence: a short gene with a start, a reading frame and a stop.</summary>
    public const string DefaultOwn = "ATGGCCATTGTAATGGGCCGCTGAAAGGGTGCCCGATAG";

    // ---- transcribe & translate ----

    private static GXSource source;
    private static GXCell cell;
    private static string ownText = DefaultOwn;
    private static BioSeqKind ownKind = BioSeqKind.Dna;
    private static GXProcess? process;

    /// <summary>The step-through of the gene shown (its steps replaced when the gene changes).</summary>
    public static BioStepController Steps { get; } = new(Array.Empty<BioStep>());

    public static GXSource Source
    {
        get { Load(); return source; }
        set { Load(); if (source == value) return; source = value; Keep("source", value.Key()); Rebuild(); }
    }

    public static GXCell Cell
    {
        get { Load(); return cell; }
        set { Load(); if (cell == value) return; cell = value; Keep("cell", value.Key()); Rebuild(); }
    }

    /// <summary>"Type your own": coding-strand DNA or mRNA, as typed.</summary>
    public static string OwnText
    {
        get { Load(); return ownText; }
        set { Load(); if (ownText == value) return; ownText = value; Keep("own", value); if (source == GXSource.Own) Rebuild(); else Tell(); }
    }

    public static BioSeqKind OwnKind
    {
        get { Load(); return ownKind; }
        set { Load(); if (ownKind == value) return; ownKind = value; Keep("ownKind", value.Key()); if (source == GXSource.Own) Rebuild(); else Tell(); }
    }

    /// <summary>The step-through of the gene shown (null: "Type your own" has nothing to use yet).</summary>
    public static GXProcess? Process { get { Load(); return process; } }

    /// <summary>"Type your own" cleaned (§3.7), kept to the step-through's 600 nt.</summary>
    public static BioSequenceInput OwnInput => new(OwnText, OwnKind, GXProcess.Cap);

    /// <summary>The gene and its steps made again (another gene, cell type or sequence): the step-through starts over.</summary>
    private static void Rebuild(bool tell = true)
    {
        GXGene? gene = source == GXSource.Own
            ? (OwnInput.Sequence is { } s ? GXGene.Own(s, cell) : null)
            : GXGene.Make(source, cell);
        process = gene == null ? null : new GXProcess(gene);
        Steps.SetSteps(process?.Steps ?? (IReadOnlyList<BioStep>)Array.Empty<BioStep>());
        if (tell) Tell();
    }

    // ---- codon table ----

    private static GXCodonView codonView;
    private static bool dnaLetters;
    private static string lookup = "";
    private static string? picked;

    public static GXCodonView CodonView
    {
        get { Load(); return codonView; }
        set { Load(); if (codonView == value) return; codonView = value; Keep("codonView", value == GXCodonView.Wheel ? "wheel" : "grid"); Tell(); }
    }

    /// <summary>The table in DNA coding-strand letters (T for U), clearly titled so.</summary>
    public static bool DnaLetters
    {
        get { Load(); return dnaLetters; }
        set { Load(); if (dnaLetters == value) return; dnaLetters = value; Keep("dnaLetters", value ? "true" : "false"); Tell(); }
    }

    public static string Lookup
    {
        get => lookup;
        set { if (lookup == value) return; lookup = value; Tell(); }
    }

    /// <summary>The codon pressed in the table (its card on the right).</summary>
    public static string? Picked
    {
        get => picked;
        set { if (picked == value) return; picked = value; Tell(); }
    }

    // ---- reading frames ----

    private static string framesText = "";
    private static BioSeqKind framesKind = BioSeqKind.Rna;
    private static int minCodons = 10;
    private static bool showReverse = true;
    private static List<GXFrameRow> frameRows = new();
    private static List<BioORF> orfs = new();
    private static int? pickedOrf;

    public static string FramesText
    {
        get { Load(); return framesText; }
        set { Load(); if (framesText == value) return; framesText = value; Keep("frames", value); RefreshFrames(); }
    }

    public static BioSeqKind FramesKind
    {
        get { Load(); return framesKind; }
        set { Load(); if (framesKind == value) return; framesKind = value; Keep("framesKind", value.Key()); RefreshFrames(); }
    }

    /// <summary>The ORF finder's shortest, in codons (stop included).</summary>
    public static int MinCodons
    {
        get { Load(); return minCodons; }
        set
        {
            Load();
            var v = Math.Clamp(value, 2, 300);
            if (minCodons == v) return;
            minCodons = v;
            Keep("minORF", v.ToString(CultureInfo.InvariantCulture));
            RefreshFrames();
        }
    }

    public static bool ShowReverse
    {
        get { Load(); return showReverse; }
        set { Load(); if (showReverse == value) return; showReverse = value; Keep("reverse", value ? "true" : "false"); RefreshFrames(); }
    }

    /// <summary>The rows and ORFs of the sequence (worked out when it changes, not on every draw).</summary>
    public static IReadOnlyList<GXFrameRow> FrameRows { get { Load(); return frameRows; } }
    public static IReadOnlyList<BioORF> Orfs { get { Load(); return orfs; } }

    public static int? PickedOrf
    {
        get => pickedOrf;
        set { if (pickedOrf == value) return; pickedOrf = value; Tell(); }
    }

    public static BioSequenceInput FramesInput => new(FramesText, FramesKind, 3000);

    private static void RefreshFrames(bool tell = true)
    {
        pickedOrf = null;
        if (FramesInput.Sequence is not { } s)
        {
            frameRows = new();
            orfs = new();
        }
        else
        {
            var dna = framesKind == BioSeqKind.Dna;
            frameRows = GXReadingFrames.Rows(s, dna && showReverse);
            orfs = BioSeq.Orfs(s, Math.Max(minCodons, 1), dna && showReverse);
        }
        if (tell) Tell();
    }

    // ---- mutations ----

    private static GXMutationGene mutationGene;
    private static GXEdit? edit;

    public static GXMutationGene MutationGene
    {
        get { Load(); return mutationGene; }
        set { Load(); if (mutationGene == value) return; mutationGene = value; Keep("mutationGene", value.Key()); edit = null; Tell(); }
    }

    public static GXEdit? Edit
    {
        get => edit;
        set { if (Equals(edit, value)) return; edit = value; Tell(); }
    }

    /// <summary>The coding sequence the Mutations page works on (the current gene's own, when it has one and it is chosen).</summary>
    public static GXMutationSource MutationSource
    {
        get
        {
            switch (MutationGene)
            {
                case GXMutationGene.Teaching: return GXMutations.TeachingSource;
                case GXMutationGene.Hbb: return GXMutations.HbbSource;
                default:
                    if (Source != GXSource.Own || Process?.Gene is not { } g || g.Codons.Count == 0) return GXMutations.TeachingSource;
                    return new GXMutationSource("Your gene", g.CdsDNA, g.DownstreamDNA, false);
            }
        }
    }

    /// <summary>Whether "Your gene" can be offered (a sequence of one's own with a coding sequence).</summary>
    public static bool HasOwnCDS => Source == GXSource.Own && Process?.Gene.Codons.Count > 0;

    /// <summary>The mutation classified, if one is made.</summary>
    public static GXMutationResult? Result
    {
        get
        {
            if (Edit is not { } e) return null;
            var s = MutationSource;
            return GXMutations.Classify(s.Cds, e, s.Downstream, s.MatureNumbering);
        }
    }

    /// <summary>The sickle-cell preset: β-globin with c.20A&gt;T.</summary>
    public static void Sickle()
    {
        MutationGene = GXMutationGene.Hbb;
        Edit = GXMutations.SickleEdit;
    }

    // ---- what a launch or another tool asks ----

    private static bool launchApplied;

    /// <summary>
    /// (--tool gene --tool-state &lt;page&gt;:&lt;…&gt;) Applied once a launch: gene=teaching|hbb|own, seq=&lt;letters&gt;,
    /// prokaryote, step=&lt;n&gt; (1-based), wheel, dna, find=&lt;codon or amino acid&gt;, codon=…, min=&lt;codons&gt;, sickle,
    /// mut=&lt;c.…&gt;, mutgene=teaching|hbb. A seq goes to the reading frames when the page is theirs, else to "Type your own".
    /// </summary>
    public static void ApplyLaunch(string page, Func<string, string?> value, Func<string, bool> has)
    {
        if (launchApplied) return;
        launchApplied = true;
        Load();
        if (value("seq") is { Length: > 0 } seq)
        {
            var up = seq.ToUpperInvariant();
            var rna = up.Contains('U') && !up.Contains('T');
            if (page == "frames")
            {
                FramesKind = rna ? BioSeqKind.Rna : BioSeqKind.Dna;
                FramesText = seq;
            }
            else
            {
                OwnKind = rna ? BioSeqKind.Rna : BioSeqKind.Dna;
                OwnText = seq;
                Source = GXSource.Own;
            }
        }
        if (GXNames.Source(value("gene")) is { } g) Source = g;
        if (has("prokaryote") || value("cell")?.ToLowerInvariant() == "prokaryote") Cell = GXCell.Prokaryote;
        if (has("eukaryote") || value("cell")?.ToLowerInvariant() == "eukaryote") Cell = GXCell.Eukaryote;
        if (has("wheel") || value("view")?.ToLowerInvariant() == "wheel") CodonView = GXCodonView.Wheel;
        if (has("grid") || value("view")?.ToLowerInvariant() == "grid") CodonView = GXCodonView.Grid;
        if (has("dna")) DnaLetters = true;
        if ((value("find") ?? value("codon")) is { } f)
        {
            Lookup = f;
            if (page == "codons") PickFromLookup();
        }
        if (int.TryParse(value("min"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var min)) MinCodons = min;
        if (GXMutationGenes.Parse(value("mutgene")) is { } mg) MutationGene = mg;
        if (has("sickle")) Sickle();
        if (value("mut") is { } mut) Edit = GXEdit.Parse(mut, MutationSource.Cds).Edit;
        if (int.TryParse(value("step"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1)
            Steps.Go(Math.Min(n, Steps.Steps.Count) - 1, animated: false);
    }

    /// <summary>The codon a lookup names, picked (Return in the lookup): a codon itself, an amino acid's first codon, UAA for the stops.</summary>
    public static void PickFromLookup()
    {
        var q = GXCodonTable.Parse(Lookup);
        switch (q.Kind)
        {
            case GXQueryKind.Codon: Picked = q.Codon; break;
            case GXQueryKind.AminoAcid: Picked = BioCode.Codons(q.Aa).FirstOrDefault(); break;
            case GXQueryKind.Stop: Picked = "UAA"; break;
        }
    }

    /// <summary>
    /// What another tool handed over, taken once (spec §3.2): "sequence" (a coding strand: "Transcribe" on the DNA &amp;
    /// RNA tool's Sequence page), "codon" (a lookup), "mutation" ("sickle", or c.…), "page". Returns the page to show,
    /// if one was asked.
    /// </summary>
    public static string? TakeHandOff(Func<string, string?> take)
    {
        Load();
        string? page = null;
        if (take("sequence") is { Length: > 0 } seq)
        {
            var up = seq.ToUpperInvariant();
            OwnKind = up.Contains('U') && !up.Contains('T') ? BioSeqKind.Rna : BioSeqKind.Dna;
            OwnText = seq;
            Source = GXSource.Own;
            page = "express";
        }
        if (take("codon") is { Length: > 0 } c)
        {
            Lookup = c;
            PickFromLookup();
            page = "codons";
        }
        if (take("mutation") is { } m)
        {
            if (m.Equals("sickle", StringComparison.OrdinalIgnoreCase)) Sickle();
            else Edit = GXEdit.Parse(m, MutationSource.Cds).Edit;
            page = "mutations";
        }
        if (take("page") is { } p && p is "express" or "codons" or "frames" or "mutations") page = p;
        return page;
    }
}

// ---- the self-check ----

public static partial class BioSelfCheck
{
    /// <summary>Gene Expression's own bio-check lines (§10.1 item 10, §10.2), the Mac's geneChecks line for line: the genes
    /// as the step-through builds them, the steps' ends, the codon table's grid, wheel and lookup, the reading frames and
    /// the mutation classifier against §6.12's table and the sickle-cell mutation.</summary>
    static partial void GeneChecks(BioCheckReport r)
    {
        GeneGeneChecks(r);
        GeneProcessChecks(r);
        GeneCodonChecks(r);
        GeneFrameChecks(r);
        GeneMutationChecks(r);
    }

    /// <summary>A number as Swift writes a Double ("55.0", "0.5"), so a line reads the same on both.</summary>
    internal static string SwiftDouble(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e15 ? v.ToString("0.0", CultureInfo.InvariantCulture) : v.ToString("R", CultureInfo.InvariantCulture);

    public static void GeneGeneChecks(BioCheckReport r)
    {
        var g = GXGene.Teaching(GXCell.Eukaryote);
        var t = BioGenes.Teaching;
        r.Check("gx.teaching", $"{g.Coding.Length}nt,+1@{g.TxStart + 1}",
            g.Coding == t.Coding && g.TxStart == 31 && g.PreMRNA == t.PreMRNA && g.Mature == t.MatureMRNA
            && g.Template == t.Template && g.Introns.SequenceEqual(new[] { new GXSpan(55, 81) }));
        r.Check("gx.teaching.cds", $"AUG@{(g.CdsStart ?? -1) + 1},{g.Protein}",
            g.CdsStart == 13 && g.Protein == t.Protein && g.HasStop && g.Codons.Count == 10 && g.CdsDNA == t.Cds && g.Codons.LastOrDefault()?.Codon == "UAA");
        r.Same("gx.teaching.kozak", g.Kozak is { } k ? g.Mature.Substring(k, 10) : "none", "GCCACCAUGG");
        var split = g.SplitCodon;
        r.Check("gx.teaching.split-codon", split is { } sc ? $"{sc.Index + 1}:{sc.Codon}|{sc.Cut}" : "none",
            split?.Index == 3 && split?.Codon == "AGC" && split?.Cut == 2 && g.Junctions.SequenceEqual(new[] { 24 }));
        r.Check("gx.teaching.columns", SwiftDouble(g.Column(24, 1)), g.Column(23, 1) == 54 && g.Column(51, 1) == 55
            && g.ColumnOfMature(24) == 55 && g.CodonColumn(0) == 45);
        var p = GXGene.Teaching(GXCell.Prokaryote);
        r.Check("gx.teaching.prokaryote", $"{p.Coding.Length}nt,{p.Protein}",
            p.Coding.Length == 85 && p.Introns.Count == 0 && p.Mature == t.MatureMRNA && p.Protein == t.Protein
            && p.Marks.Select(m => m.Range).SequenceEqual(new[] { new GXSpan(44, 46), new GXSpan(71, 73) }) && !p.Marks.Any(m => m.Kind == GXRegionKind.Tata));
        var h = GXGene.Hbb(GXCell.Eukaryote);
        r.Check("gx.hbb", $"{h.Protein.Length}aa", h.Protein == BioGenes.HbbProtein && h.HasStop && h.CdsStart == 0 && h.CdsDNA == BioGenes.HbbCDS);
        var o = GXGene.Own("ccATGAAATAGgg", GXCell.Eukaryote);
        var none = GXGene.Own("CCCGGG", GXCell.Eukaryote);
        var open = GXGene.Own("AUGGCC", GXCell.Eukaryote);
        r.Check("gx.own", (o?.Protein ?? "nil") + "," + (none?.Protein ?? "nil") + "," + (open?.Protein ?? "nil"),
            o?.Protein == "MK" && o.CdsStart == 2 && o.Marks.Select(m => m.Range).SequenceEqual(new[] { new GXSpan(2, 4), new GXSpan(8, 10) })
            && o.Regions.Select(x => x.Kind).SequenceEqual(new[] { GXRegionKind.Utr5, GXRegionKind.Cds, GXRegionKind.Utr3 })
            && none?.CdsStart == null && none?.Protein == "" && open?.Protein == "MA" && open?.HasStop == false && GXGene.Own("", GXCell.Eukaryote) == null);
    }

    public static void GeneCodonChecks(BioCheckReport r)
    {
        // (the grid reads §6.1's table row by row: UUU UCU UAU UGU / UUC UCC UAC UGC / …)
        var rows = GXCodonTable.GridRows;
        var tableOrder = rows.Select(row => string.Join(" ", row.Select(c => $"{c}={BioCode.AminoAcid(c) ?? '?'}"))).ToList();
        r.Check("gx.codon.grid", $"{rows.Count}x{rows.FirstOrDefault()?.Count ?? 0}",
            rows.Count == 16 && rows.All(x => x.Count == 4) && rows.SelectMany(x => x).Distinct().Count() == 64
            && tableOrder[0] == "UUU=F UCU=S UAU=Y UGU=C" && tableOrder[3] == "UUG=L UCG=S UAG=* UGG=W"
            && tableOrder[11] == "AUG=M ACG=T AAG=K AGG=R" && tableOrder[15] == "GUG=V GCG=A GAG=E GGG=G");
        // (the wheel: U C A G clockwise from 12 o'clock in every ring, read from the centre out)
        var third = GXCodonTable.Sectors(2);
        var entries = BioCode.Entries.Select(e => e.Codon).ToList();
        var second = GXCodonTable.Sectors(1);
        r.Check("gx.codon.wheel", $"{string.Concat(GXCodonTable.Sectors(0).Select(s => s.Prefix))},{third.Count}",
            GXCodonTable.Sectors(0).Select(s => s.Prefix).SequenceEqual(new[] { "U", "C", "A", "G" }) && second.Count == 16
            && third.Select(s => s.Prefix).SequenceEqual(entries) && Math.Abs(third[35].From - 196.875) < 1e-9 && third[35].Prefix == "AUG"
            && second[4].Prefix == "CU" && Math.Abs(second[4].From - 90) < 1e-9);
        var labels = GXCodonTable.WheelLabels;
        var leu = labels.Where(l => l.Aa == 'L').Select(l => l.Codons.Count).ToList();
        r.Check("gx.codon.wheel-labels", $"{labels.Count}", labels.Sum(l => l.Codons.Count) == 64 && leu.SequenceEqual(new[] { 2, 4 })
            && labels.Count > 0 && labels[0].Aa == 'F' && labels[0].From == 0 && Math.Abs(labels[0].To - 11.25) < 1e-9);
        var hits = new[] { GXCodonTable.Hit(0.27, 10), GXCodonTable.Hit(0.46, 100), GXCodonTable.Hit(0.67, 199), GXCodonTable.Hit(0.9, 359.9), GXCodonTable.Hit(0.1, 0) };
        r.Same("gx.codon.wheel-hit", string.Join(",", hits.Select(x => x ?? "-")), "U,CU,AUG,GGG,-");
        // (lookup both ways: a codon to its amino acid, an amino acid to its codons)
        var q = new[] { "AUG", "atg", "UGA", "Lys", "leucine", "W", "stop", "ATU", "XYZ", "AU" }.Select(GXCodonTable.Parse).ToList();
        var ok = q[0] == GXQuery.OfCodon("AUG") && q[1] == GXQuery.OfCodon("AUG") && q[2] == GXQuery.OfCodon("UGA") && q[3] == GXQuery.OfAminoAcid('K')
            && q[4] == GXQuery.OfAminoAcid('L') && q[5] == GXQuery.OfAminoAcid('W') && q[6] == GXQuery.Stop
            && q[7].Kind == GXQueryKind.Invalid && q[8].Kind == GXQueryKind.Invalid && q[9].Kind == GXQueryKind.Invalid;
        r.Check("gx.codon.lookup", string.Join("/", GXCodonTable.Matches(q[4]).OrderBy(c => c, StringComparer.Ordinal)),
            ok && GXCodonTable.Matches(q[4]).Count == 6 && GXCodonTable.Matches(q[3]).SetEquals(new[] { "AAA", "AAG" }) && GXCodonTable.Matches(q[6]).Count == 3);
        r.Same("gx.codon.dna-letters", GXCodonTable.Letters("AUG", true) + "," + GXCodonTable.Letters("UUU", false), "ATG,UUU");
        r.Same("gx.anticodon", $"3′-{BioCode.Anticodon3To5("AUG")}-5′,5′-{BioCode.Anticodon5To3("AUG")}-3′", "3′-UAC-5′,5′-CAU-3′");
        r.Same("gx.class-words", string.Join(";", new[] { BioAminoAcids.Find("Glu"), BioAminoAcids.Find("Val"), BioAminoAcids.Find("His") }.Where(a => a != null).Select(a => GXCodonTable.ClassWords(a!))),
            "acidic, charged;nonpolar;basic, mostly uncharged at pH 7.4");
    }

    public static void GeneFrameChecks(BioCheckReport r)
    {
        var mrna = BioGenes.Teaching.MatureMRNA;
        var rows = GXReadingFrames.Rows(mrna, reverse: false);
        var frames = rows.Select(x => new string(x.Cells.Select(c => c.Aa).ToArray())).ToList();
        // (frame +2 of ACUCAGCGCCACCAUGG…: CUC AGC GCC ACC AUG GCC … UAA GCA AUA AAG)
        r.Check("gx.frames.mRNA", frames[1], rows.Count == 3 && frames[1] == "LSATMAFSDWQKG*AIK"
            && rows.All(row => row.Cells.All(c => c.Column % 3 == row.Frame - 1)));
        var orfs = BioSeq.Orfs(mrna, minCodons: 10, reverseStrand: false);
        var first = orfs.FirstOrDefault();
        r.Check("gx.frames.ORF", first is { } o ? $"{o.FrameLabel}@{o.Start}-{o.End}" : "none",
            orfs.Count == 1 && first?.Frame == 2 && first.Start == 14 && first.End == 43 && first.Protein == "MAFSDWQKG");
        // (six frames on its DNA copy: the reverse frames' codons placed back on forward columns; +2 still the longest)
        var dna = BioSeq.ToDna(mrna);
        var six = GXReadingFrames.Rows(dna, reverse: true);
        var rc = BioSeq.ReverseComplement(dna);
        var n = dna.Length;
        var placed = six.Where(x => x.Frame < 0).All(row => row.Cells.All(c =>
            BioSeq.ReverseComplement(dna.Substring(c.Column, 3)).Replace('T', 'U') == c.Codon));
        var all = BioSeq.Orfs(dna, minCodons: 10, reverseStrand: true);
        r.Check("gx.frames.six", $"{six.Count},{all.FirstOrDefault()?.FrameLabel ?? "none"}",
            six.Count == 6 && placed && six[3].Cells.FirstOrDefault()?.Column == n - 3 && new string(six[3].Cells.Select(c => c.Aa).ToArray()) == BioSeq.Translate(rc)
            && all.FirstOrDefault()?.Frame == 2 && all.FirstOrDefault()?.Start == 14);
        var small = GXReadingFrames.Rows("ATGGCCTAA", reverse: true).Select(x => new string(x.Cells.Select(c => c.Aa).ToArray()));
        r.Same("gx.frames.small", string.Join(",", small), "MA*,WP,GL,LGH,*A,RP");
    }
}

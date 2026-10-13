namespace Simpl.Tools;

// The biology toolkit's shared datasets (spec §3.3, §6), the Mac's BioData.swift: parsed from the text in
// BioDataText.cs the first time each is asked for and kept (Lazy: parsed once, safely, from any thread). The tools read
// them from here: BioData.Molecule("ALA"), BioData.Bdna, BioData.Ubiquitin, BioData.Trna, BioGenes.Teaching … The
// genetic code and the amino acids' facts are in BioGeneticCode.cs.

/// <summary>The toolkit's datasets.</summary>
public static class BioData
{
    // ---- molecule blocks (§6.3, §6.6, §6.8, §6.9) ----

    private static readonly Lazy<List<BioMolecule>> residues = new(() => BioBlockParser.Parse(BioDataText.ResidueText));
    private static readonly Lazy<List<BioMolecule>> nucleotides = new(() => BioBlockParser.Parse(BioDataText.NucleotideText));
    private static readonly Lazy<List<BioMolecule>> sugars = new(() => BioBlockParser.Parse(BioDataText.SugarText));
    private static readonly Lazy<List<BioMolecule>> lipids = new(() => BioBlockParser.Parse(BioDataText.LipidText));

    /// <summary>§6.3: the 20 residue templates, in the specification's order (Ala, Arg, Asn, …, Val).</summary>
    public static IReadOnlyList<BioMolecule> Residues => residues.Value;
    /// <summary>§6.6: dAMP, dCMP, dGMP, dTMP, AMP, CMP, GMP, UMP and ATP (codes DA DC DG DT A C G U ATP).</summary>
    public static IReadOnlyList<BioMolecule> Nucleotides => nucleotides.Value;
    /// <summary>§6.8: α- and β-glucose, galactose, fructose, ribose, maltose, cellobiose, sucrose, lactose (GLC BGC GAL
    /// FRU BDR MAL CBI SUC LAT).</summary>
    public static IReadOnlyList<BioMolecule> Sugars => sugars.Value;
    /// <summary>§6.9: glycerol, palmitic, stearic and oleic acids, tristearin, POPC, cholesterol (GOL PLM STE OLA TGL POV CLR).</summary>
    public static IReadOnlyList<BioMolecule> Lipids => lipids.Value;

    private static readonly Lazy<Dictionary<string, BioMolecule>> byCode = new(() =>
    {
        var d = new Dictionary<string, BioMolecule>();
        foreach (var m in Residues.Concat(Nucleotides).Concat(Sugars).Concat(Lipids)) d.TryAdd(m.Code, m);
        return d;
    });

    /// <summary>A molecule block by its code: "ALA" (or "Ala"), "DA", "ATP", "GLC", "POV". One letter "A" is AMP, so
    /// one-letter amino acids go through <see cref="Residue"/>.</summary>
    public static BioMolecule? Molecule(string code) =>
        byCode.Value.GetValueOrDefault(code) ?? byCode.Value.GetValueOrDefault(code.ToUpperInvariant());

    /// <summary>An amino acid's template by its three-letter ("Lys", "LYS") or one-letter ("K") code.</summary>
    public static BioMolecule? Residue(string code)
    {
        if (code.Length == 1 && BioAminoAcids.ByOne.GetValueOrDefault(char.ToUpperInvariant(code[0])) is { } aa)
            return Residues.FirstOrDefault(r => r.Code == aa.Three.ToUpperInvariant());
        return Residues.FirstOrDefault(r => r.Code == code.ToUpperInvariant());
    }

    // ---- B-DNA (§6.7) ----

    /// <summary>An atom of the B-DNA model's strand-I nucleotide, in helical coordinates: x along the axis (Å), r from it
    /// (Å), θ about it (degrees). Its element is its name's first letter (C, N, O or P: the helix has no H).</summary>
    public sealed record HelixAtom(string Name, double X, double R, double Theta)
    {
        public string Element => Name[..1];
    }

    private static readonly Lazy<(List<HelixAtom> Backbone, Dictionary<char, List<HelixAtom>> Bases)> bdna = new(() =>
    {
        var backbone = new List<HelixAtom>();
        var bases = new Dictionary<char, List<HelixAtom>>();
        char? current = null;
        foreach (var line in BioText.Lines(BioDataText.BdnaText))
        {
            if (line.StartsWith("# base ", StringComparison.Ordinal))
            {
                current = line.Length > 7 ? line[7] : null;
                continue;
            }
            if (line.StartsWith('#')) continue;
            var f = BioText.Words(line);
            if (f.Length != 4 || BioText.Number(f[1]) is not { } x || BioText.Number(f[2]) is not { } r || BioText.Number(f[3]) is not { } t) continue;
            var a = new HelixAtom(f[0], x, r, t);
            if (current is { } c) (bases.TryGetValue(c, out var l) ? l : bases[c] = new List<HelixAtom>()).Add(a);
            else backbone.Add(a);
        }
        return (backbone, bases);
    });

    /// <summary>§6.7: the sugar–phosphate backbone (the same for every base) and each base's atoms.</summary>
    public static (List<HelixAtom> Backbone, Dictionary<char, List<HelixAtom>> Bases) Bdna => bdna.Value;

    private static readonly Lazy<List<(int Strand, int Pair, string Name, BioVec Pos)>> bdnaTests = new(() =>
    {
        var list = new List<(int, int, string, BioVec)>();
        foreach (var line in BioText.Lines(BioDataText.BdnaTestText))
        {
            // "strand 1 bp 0 P     X    0.000  Y    0.000  Z    8.910"
            var f = BioText.Words(line);
            if (f.Length != 11 || f[0] != "strand" || BioText.Int(f[1]) is not { } s || BioText.Int(f[3]) is not { } k
                || BioText.Number(f[6]) is not { } x || BioText.Number(f[8]) is not { } y || BioText.Number(f[10]) is not { } z) continue;
            list.Add((s, k, f[4], new BioVec(x, y, z)));
        }
        return list;
    });

    /// <summary>§6.7's test vectors: strand, base pair, atom, and where it is in the app's frame.</summary>
    public static IReadOnlyList<(int Strand, int Pair, string Name, BioVec Pos)> BdnaTests => bdnaTests.Value;

    /// <summary>An atom of a base pair drawn in 2D (§6.7): its base, name and place (Å; origin midway between the C1′ atoms,
    /// +x toward the pyrimidine's C1′, +y toward the major groove).</summary>
    public sealed record PairAtom(char Base, string Name, double X, double Y);

    private static readonly Lazy<Dictionary<string, List<PairAtom>>> basePairs2D = new(() =>
    {
        var out_ = new Dictionary<string, List<PairAtom>>();
        string pair = "";
        var b = '?';
        foreach (var line in BioText.Lines(BioDataText.BasePairText))
        {
            if (line.StartsWith("# A·T", StringComparison.Ordinal)) { pair = "AT"; continue; }
            if (line.StartsWith("# G·C", StringComparison.Ordinal)) { pair = "GC"; continue; }
            if (line.StartsWith("# ", StringComparison.Ordinal) && line.Length > 2 && "ATGC".Contains(line[2])) { b = line[2]; continue; }
            var f = BioText.Words(line);
            if (f.Length != 3 || BioText.Number(f[1]) is not { } x || BioText.Number(f[2]) is not { } y || pair.Length == 0) continue;
            (out_.TryGetValue(pair, out var l) ? l : out_[pair] = new List<PairAtom>()).Add(new PairAtom(b, f[0], x, y));
        }
        return out_;
    });

    /// <summary>§6.7: the A·T and G·C pairs in 2D, keyed "AT" and "GC".</summary>
    public static IReadOnlyDictionary<string, List<PairAtom>> BasePairs2D => basePairs2D.Value;

    // ---- proteins (§6.10) ----

    /// <summary>A residue of a protein's trace: its chain, number, one-letter amino acid, secondary structure (H helix, E
    /// strand, C coil), CA atom and (ubiquitin) side-chain centroid, in Å.</summary>
    public sealed record TraceResidue(char Chain, int I, char Aa, char Ss, BioVec Ca, BioVec? Centroid = null);

    /// <summary>A heme of haemoglobin: its chain, the iron and the four pyrrole N atoms around it (NA, NB, NC, ND).</summary>
    public sealed record Heme(char Chain, BioVec Fe, BioVec[] N);

    private static readonly Lazy<List<TraceResidue>> ubiquitin = new(() =>
    {
        var list = new List<TraceResidue>();
        foreach (var line in BioText.Lines(BioDataText.UbiquitinText))
        {
            var f = BioText.Words(line);
            if (f.Length != 9 || BioText.Int(f[0]) is not { } i || f[1].Length == 0 || f[2].Length == 0) continue;
            var v = f.Skip(3).Select(w => BioText.Number(w)).Where(d => d != null).Select(d => d!.Value).ToArray();
            if (v.Length != 6) continue;
            list.Add(new TraceResidue('A', i, f[1][0], f[2][0], new BioVec(v[0], v[1], v[2]), new BioVec(v[3], v[4], v[5])));
        }
        return list;
    });

    /// <summary>§6.10: ubiquitin (1UBQ), 76 residues, with side-chain centroids.</summary>
    public static IReadOnlyList<TraceResidue> Ubiquitin => ubiquitin.Value;

    private static readonly Lazy<(List<TraceResidue> Residues, List<Heme> Hemes)> hemoglobin = new(() =>
    {
        var rs = new List<TraceResidue>();
        var hs = new List<Heme>();
        foreach (var line in BioText.Lines(BioDataText.HemoglobinText))
        {
            if (line.StartsWith('#')) continue;
            var f = BioText.Words(line);
            if (f.Length == 17 && f[1] == "HEM" && f[0].Length > 0)
            {
                var v = f.Skip(2).Select(w => BioText.Number(w)).Where(d => d != null).Select(d => d!.Value).ToArray();
                if (v.Length != 15) continue;
                var p = Enumerable.Range(0, 5).Select(k => new BioVec(v[3 * k], v[3 * k + 1], v[3 * k + 2])).ToArray();
                hs.Add(new Heme(f[0][0], p[0], p[1..]));
            }
            else if (f.Length == 7 && f[0].Length > 0 && BioText.Int(f[1]) is { } i && f[2].Length > 0 && f[3].Length > 0
                     && BioText.Number(f[4]) is { } x && BioText.Number(f[5]) is { } y && BioText.Number(f[6]) is { } z)
                rs.Add(new TraceResidue(f[0][0], i, f[2][0], f[3][0], new BioVec(x, y, z)));
        }
        return (rs, hs);
    });

    /// <summary>§6.10: deoxy-haemoglobin (2HHB): chains A and C α (141), B and D β (146), and the four hemes.</summary>
    public static (List<TraceResidue> Residues, List<Heme> Hemes) Hemoglobin => hemoglobin.Value;

    // ---- transfer RNA (§6.11) ----

    /// <summary>A nucleotide of yeast tRNA-Phe: its number, base, P atom (C4′ for G1) in Å, and its modification's code (the
    /// PDB's, "2MG") where the real molecule has one.</summary>
    public sealed record TRNANucleotide(int I, char Base, BioVec P, string? Modified = null);

    /// <summary>An element of the cloverleaf: its name, positions and pairs as the specification's table writes them.</summary>
    public sealed record TRNAElement(string Name, string Positions, string Pairs);

    public sealed record TRNA(List<TRNANucleotide> Nucleotides, List<(int I, int J)> Pairs, List<TRNAElement> Elements, List<(string Label, int Position)> Modified);

    private static readonly Lazy<TRNA> trna = new(() =>
    {
        var ns = new List<TRNANucleotide>();
        foreach (var line in BioText.Lines(BioDataText.TrnaText))
        {
            if (line.StartsWith('#')) continue;
            var f = BioText.Words(line);
            if (f.Length < 5 || BioText.Int(f[0]) is not { } i || f[1].Length == 0 || BioText.Number(f[2]) is not { } x
                || BioText.Number(f[3]) is not { } y || BioText.Number(f[4]) is not { } z) continue;
            ns.Add(new TRNANucleotide(i, f[1][0], new BioVec(x, y, z), f.Length >= 6 ? f[5].Trim('[', ']') : null));
        }
        var elements = new List<TRNAElement>();
        var pairs = new List<(int, int)>();
        foreach (var line in BioText.Lines(BioDataText.TrnaStructureText))
        {
            var c = line.Split('\t');
            if (c.Length != 3) continue;
            elements.Add(new TRNAElement(c[0], c[1], c[2]));
            // ("G1–C72", "G4·U69 (wobble)": a pair is two numbered bases joined by – or ·)
            foreach (var token in c[2].Split(", "))
            {
                var part = BioText.Words(token).FirstOrDefault() ?? "";
                var sides = part.Split('–', '·');
                if (sides.Length != 2 || sides[0].Length < 1 || sides[1].Length < 1 || BioText.Int(sides[0][1..]) is not { } i || BioText.Int(sides[1][1..]) is not { } j) continue;
                pairs.Add((Math.Min(i, j), Math.Max(i, j)));
            }
        }
        var modified = new List<(string, int)>();
        foreach (var entry in BioDataText.TrnaModifiedText.Split(", "))
        {
            var word = BioText.Words(entry).FirstOrDefault() ?? "";
            var d = 0;
            while (d < word.Length && word[word.Length - 1 - d] is >= '0' and <= '9') d++;
            if (d == 0 || BioText.Int(word[^d..]) is not { } n) continue;
            modified.Add((word[..^d], n));
        }
        return new TRNA(ns, pairs, elements, modified);
    });

    /// <summary>§6.11: yeast tRNA-Phe — its 76 nucleotides, its base pairs (i &lt; j), its elements and its modified bases
    /// ("m²G", 10).</summary>
    public static TRNA Trna => trna.Value;

    /// <summary>§6.11: yeast tRNA-Phe's sequence, 76 nt.</summary>
    public static string TrnaSequence => BioDataText.TrnaSequence;

    /// <summary>§6.4: the peptide builder's test vectors and the β-sheet's transforms, as text (the Biomolecule Builder reads them).</summary>
    public static string PeptideTestText => BioDataText.PeptideTestText;
    public static string SheetTransformText => BioDataText.SheetTransformText;
}

// ---- example genes (§6.12) ----

/// <summary>The example genes: the made-up teaching gene (labelled so in the UI) and human β-globin's coding sequence.</summary>
public static class BioGenes
{
    /// <summary>A stretch of the teaching gene: its name and its 1-based positions (inclusive) on the 112-nt coding strand.</summary>
    public sealed record Part(string Name, int Start, int End);

    /// <summary>A mutation test vector: the mutation, the mutant CDS (changed bases as the specification bolds them), the
    /// protein, and its class.</summary>
    public sealed record Mutation(string Name, string Cds, string Protein, string Kind);

    /// <summary>The teaching gene (§6.12): coding strand 5′→3′, its parts, its marks, and what it becomes.</summary>
    public sealed class TeachingGene
    {
        /// <summary>The coding (sense) strand, 5′→3′, 112 nt.</summary>
        public required string Coding { get; init; }
        /// <summary>Promoter 1–31, 5′ UTR 32–44, Exon 1 45–55, Intron 56–82, Exon 2 83–101, 3′ UTR 102–112.</summary>
        public required List<Part> Parts { get; init; }
        /// <summary>TATA box 5–11.</summary>
        public Part Tata { get; } = new("TATA box", 5, 11);
        /// <summary>The transcription start site (+1).</summary>
        public int StartSite => 32;
        /// <summary>The start codon ATG, 45–47.</summary>
        public Part StartCodon { get; } = new("Start codon", 45, 47);
        /// <summary>The stop codon TAA, 99–101.</summary>
        public Part StopCodon { get; } = new("Stop codon", 99, 101);
        /// <summary>The poly-A signal AATAAA, 104–109.</summary>
        public Part PolyASignal { get; } = new("Poly-A signal", 104, 109);
        /// <summary>The template strand written 3′→5′ under the coding strand.</summary>
        public string Template => BioDataText.TeachingTemplate;
        public string PreMRNA => BioDataText.TeachingPreMRNA;
        /// <summary>Mature mRNA (cap and tail not shown).</summary>
        public string MatureMRNA => BioDataText.TeachingMatureMRNA;
        public string Cds => BioDataText.TeachingCDS;
        /// <summary>MAFSDWQKG.</summary>
        public string Protein => BioDataText.TeachingProtein;
        /// <summary>Read from the AUG with the intron kept: MAFR*.</summary>
        public string UnsplicedProtein => BioDataText.TeachingUnspliced;
        /// <summary>The mutation test vectors.</summary>
        public required List<Mutation> Mutations { get; init; }
    }

    private static readonly Lazy<TeachingGene> teaching = new(() =>
    {
        var names = new[] { "Promoter", "5′ UTR", "Exon 1", "Intron", "Exon 2", "3′ UTR" };
        var chunks = BioText.Words(BioText.Lines(BioDataText.TeachingGeneText).FirstOrDefault() ?? "");
        var parts = new List<Part>();
        var at = 1;
        for (var k = 0; k < chunks.Length; k++)
        {
            parts.Add(new Part(k < names.Length ? names[k] : $"Part {k + 1}", at, at + chunks[k].Length - 1));
            at += chunks[k].Length;
        }
        var mutations = BioText.Lines(BioDataText.TeachingMutationText)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(c => c.Length == 4).Select(c => new Mutation(c[0], c[1], c[2], c[3])).ToList();
        return new TeachingGene { Coding = string.Concat(chunks), Parts = parts, Mutations = mutations };
    });

    public static TeachingGene Teaching => teaching.Value;

    /// <summary>Human β-globin (HBB), its coding sequence only (the real gene's 3 exons joined; NM_000518.5), 444 nt.</summary>
    public static string HbbCDS => BioDataText.HbbCDS;
    /// <summary>Its protein (NP_000509.1), 147 aa with the initiator Met.</summary>
    public static string HbbProtein => BioDataText.HbbProtein;
    /// <summary>The sickle-cell mutation: c.20A&gt;T, codon 7 GAG → GTG (counting ATG as codon 1), p.Glu7Val = the classic Glu6Val.</summary>
    public static (int Position, char From, char To, string ProteinStart) Sickle => (20, 'A', 'T', BioDataText.HbbSickleStart);
}

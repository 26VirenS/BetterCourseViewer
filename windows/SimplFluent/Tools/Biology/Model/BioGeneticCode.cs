namespace Simpl.Tools;

// The standard genetic code (spec §6.1, NCBI table 1) and the 20 amino acids' facts (§6.2), read from the text both
// apps share (the Mac's BioGeneticCode.swift). The tools use AUG as the only start codon and say so (UUG and CUG are
// rare alternatives); a codon with N in it reads as X.

/// <summary>The standard genetic code.</summary>
public static class BioCode
{
    /// <summary>The order of the bases everywhere a table is drawn: U, C, A, G.</summary>
    public static readonly char[] Bases = { 'U', 'C', 'A', 'G' };

    private static readonly Lazy<List<(string Codon, char Aa)>> entries = new(() =>
        BioDataText.CodeText.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('='))
            .Where(p => p.Length == 2 && p[0].Length == 3 && p[1].Length > 0)
            .Select(p => (p[0], p[1][0])).ToList());

    private static readonly Lazy<Dictionary<string, char>> table = new(() =>
    {
        var d = new Dictionary<string, char>();
        foreach (var (c, a) in Entries) d.TryAdd(c, a);
        return d;
    });

    /// <summary>The 64 codons (RNA letters) and their amino acids (one letter; '*' a stop), in U C A G order.</summary>
    public static IReadOnlyList<(string Codon, char Aa)> Entries => entries.Value;

    /// <summary>The amino acid a codon codes ("AUG" → M, "UAA" → *); DNA letters too ("ATG"). X for a codon with N; null
    /// for anything that is not three bases.</summary>
    public static char? AminoAcid(string codon)
    {
        var c = codon.ToUpperInvariant().Replace('T', 'U');
        if (c.Length != 3 || !c.All(x => "UCAGN".Contains(x))) return null;
        if (c.Contains('N')) return 'X';
        return table.Value.TryGetValue(c, out var a) ? a : null;
    }

    /// <summary>The codons of an amino acid (one letter; '*' for the stops), RNA letters, in table order.</summary>
    public static List<string> Codons(char aa)
    {
        var a = char.ToUpperInvariant(aa);
        return Entries.Where(e => e.Aa == a).Select(e => e.Codon).ToList();
    }

    public static bool IsStart(string codon) => codon.ToUpperInvariant().Replace('T', 'U') == "AUG";
    public static bool IsStop(string codon) => AminoAcid(codon) == '*';

    /// <summary>The stop codons' old names: UAA ochre, UAG amber, UGA opal.</summary>
    public static readonly IReadOnlyDictionary<string, string> StopNames = new Dictionary<string, string> { ["UAA"] = "ochre", ["UAG"] = "amber", ["UGA"] = "opal" };

    /// <summary>The anticodon that pairs with a codon, written 3′→5′ as it sits under the codon (codon 5′-AUG-3′ → 3′-UAC-5′).</summary>
    public static string Anticodon3To5(string codon) => new(codon.ToUpperInvariant().Replace('T', 'U').Select(b => BioSeq.Pair(b, rna: true)).ToArray());

    /// <summary>The same anticodon written 5′→3′, as tRNA tables write it (5′-CAU-3′ for AUG).</summary>
    public static string Anticodon5To3(string codon) => new(Anticodon3To5(codon).Reverse().ToArray());
}

/// <summary>An amino acid's group as AP Biology and Campbell group them (the colours of §3.8).</summary>
public enum BioAAClass { Nonpolar, Polar, Acidic, Basic }

public static class BioAAClasses
{
    public static readonly BioAAClass[] All = { BioAAClass.Nonpolar, BioAAClass.Polar, BioAAClass.Acidic, BioAAClass.Basic };

    public static string Label(this BioAAClass c) => c switch
    {
        BioAAClass.Nonpolar => "Nonpolar",
        BioAAClass.Polar => "Polar (uncharged)",
        BioAAClass.Acidic => "Acidic (−)",
        _ => "Basic (+)",
    };
}

/// <summary>One of the 20 amino acids (§6.2).</summary>
/// <param name="Three">"Gly".</param>
/// <param name="One">'G'.</param>
/// <param name="Lehninger">Lehninger's group ("Nonpolar, aliphatic").</param>
/// <param name="PKa">The side chain's pKa (Lehninger), null when it has none to speak of.</param>
/// <param name="Charge">The side chain's charge at pH 7.4 as a whole number (His 0).</param>
/// <param name="ChargeText">The charge as the table writes it ("≈0 (about 4% +1 at pH 7.4, 9% at 7.0)").</param>
/// <param name="Hydropathy">Kyte–Doolittle hydropathy.</param>
/// <param name="Mass">The free amino acid's molar mass (g/mol).</param>
/// <param name="ResidueMass">Its mass as a residue in a chain (free − 18.02).</param>
/// <param name="CodonCount">How many codons code it.</param>
public sealed record BioAminoAcid(string Name, string Three, char One, BioAAClass Group, string Lehninger, double? PKa, int Charge, string ChargeText,
    double Hydropathy, double Mass, double ResidueMass, string Formula, int CodonCount, string Note)
{
    public List<string> Codons => BioCode.Codons(One);
    /// <summary>Glycine's side chain is H: the only amino acid that is not chiral.</summary>
    public bool IsChiral => One != 'G';
}

public static class BioAminoAcids
{
    private static readonly Lazy<List<BioAminoAcid>> all = new(() =>
    {
        var list = new List<BioAminoAcid>();
        foreach (var line in BioText.Lines(BioDataText.AminoAcidText))
        {
            var c = line.Split('\t').Select(x => x.Trim(' ')).ToArray();
            if (c.Length != 13 || c[2].Length == 0) continue;
            var group = c[3].StartsWith("Nonpolar", StringComparison.Ordinal) ? BioAAClass.Nonpolar
                : c[3].StartsWith("Polar", StringComparison.Ordinal) ? BioAAClass.Polar
                : c[3].StartsWith("Acidic", StringComparison.Ordinal) ? BioAAClass.Acidic : BioAAClass.Basic;
            double? Number(string s) => BioText.Number(s.Replace('−', '-').Replace("+", ""));
            var charge = c[6].StartsWith('≈') ? 0 : (int)(Number(c[6]) ?? 0);
            list.Add(new BioAminoAcid(c[0], c[1], c[2][0], group, c[4], Number(c[5]), charge, c[6], Number(c[7]) ?? 0, Number(c[8]) ?? 0,
                Number(c[9]) ?? 0, c[10], BioText.Int(c[11]) ?? 0, c[12]));
        }
        return list;
    });

    private static readonly Lazy<Dictionary<char, BioAminoAcid>> byOne = new(() =>
    {
        var d = new Dictionary<char, BioAminoAcid>();
        foreach (var a in All) d.TryAdd(a.One, a);
        return d;
    });

    /// <summary>The 20, in §6.2's order (Gly, Ala, Val, Leu, Ile, Met, Pro, Phe, Trp, Tyr, Ser, Thr, Cys, Asn, Gln, Lys,
    /// Arg, His, Asp, Glu).</summary>
    public static IReadOnlyList<BioAminoAcid> All => all.Value;

    public static IReadOnlyDictionary<char, BioAminoAcid> ByOne => byOne.Value;

    /// <summary>An amino acid by its name ("lysine"), three letters ("Lys") or one letter ("K"), any case.</summary>
    public static BioAminoAcid? Find(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0) return null;
        if (q.Length == 1) return ByOne.GetValueOrDefault(char.ToUpperInvariant(q[0]));
        return All.FirstOrDefault(a => a.Three.ToLowerInvariant() == q || a.Name.ToLowerInvariant() == q || a.Name.ToLowerInvariant().StartsWith(q + " ", StringComparison.Ordinal));
    }

    /// <summary>A peptide's mass: the residues' masses plus one water (18.02 g/mol).</summary>
    public static double PeptideMass(string protein) => protein.Select(c => ByOne.GetValueOrDefault(c)?.ResidueMass ?? 0).Sum() + 18.02;

    /// <summary>A peptide's net charge at pH 7 by the textbook count: +1 per K and R, −1 per D and E (His counted as 0;
    /// the two ends' +1 and −1 cancel).</summary>
    public static int NetCharge(string protein) => protein.Sum(c => c is 'K' or 'R' ? 1 : (c is 'D' or 'E' ? -1 : 0));

    /// <summary>GRAVY: the mean Kyte–Doolittle hydropathy (&gt; 0 is hydrophobic).</summary>
    public static double Gravy(string protein)
    {
        var v = protein.Where(c => ByOne.ContainsKey(c)).Select(c => ByOne[c].Hydropathy).ToList();
        return v.Count == 0 ? 0 : v.Sum() / v.Count;
    }
}

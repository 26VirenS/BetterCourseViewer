using System.Text;

namespace Simpl.Tools;

// The shared sequence model (spec §3.7), the Mac's BioSequence.swift, used by DNA & RNA, Gene Expression and the
// Biomolecule Builder: a typed sequence cleaned the same way everywhere (whitespace, digits and a FASTA header dropped;
// U in DNA or T in RNA offered as a switch, never converted silently; other letters refused with their positions), and
// the operations the tools show — complement, reverse complement, transcription, translation in any frame on either
// strand, ORFs, GC content, mass, the Chargaff check and a melting temperature.

/// <summary>What a sequence is.</summary>
public enum BioSeqKind { Dna, Rna, Protein }

public static class BioSeqKinds
{
    public static string Label(this BioSeqKind k) => k switch { BioSeqKind.Dna => "DNA", BioSeqKind.Rna => "RNA", _ => "Protein" };

    /// <summary>The letters it takes (N for an unknown base; X and * in a protein).</summary>
    public static string Alphabet(this BioSeqKind k) => k switch
    {
        BioSeqKind.Dna => "ACGTN",
        BioSeqKind.Rna => "ACGUN",
        _ => "ACDEFGHIKLMNPQRSTVWYX*",
    };

    /// <summary>"dna", "rna", "protein" (the Mac's raw values).</summary>
    public static string Key(this BioSeqKind k) => k switch { BioSeqKind.Dna => "dna", BioSeqKind.Rna => "rna", _ => "protein" };
}

/// <summary>A sequence as typed or pasted, cleaned (§3.7): the letters kept, what was wrong with them, and what to say.</summary>
public sealed class BioSequenceInput
{
    public BioSeqKind Kind { get; }
    /// <summary>What was typed, as typed.</summary>
    public string Original { get; }
    /// <summary>The sequence's letters, uppercased, without whitespace, digits or a FASTA header — bad letters included,
    /// so their positions are where they stand.</summary>
    public string Letters { get; }
    /// <summary>Each letter that does not belong, with its 1-based positions in <see cref="Letters"/>.</summary>
    public IReadOnlyList<(char Letter, List<int> Positions)> Invalid { get; }
    /// <summary>The kind it looks like instead: RNA when DNA has U (and no T), DNA when RNA has T (and no U).</summary>
    public BioSeqKind? Suggestion { get; }
    /// <summary>Cut to the tool's cap.</summary>
    public bool Truncated { get; }
    public int? Cap { get; }

    /// <summary>The sequence when every letter belongs (null otherwise).</summary>
    public string? Sequence => Invalid.Count == 0 && Letters.Length > 0 ? Letters : null;
    public bool IsValid => Invalid.Count == 0;

    /// <summary>The inline message: the letters that do not belong and where they are, or the switch to offer, or the cap.</summary>
    public string? Message
    {
        get
        {
            if (Suggestion is { } s) return $"This looks like {s.Label()}. Switch?";
            if (Invalid.Count > 0)
            {
                var what = string.Join("; ", Invalid.Select(item =>
                    $"“{item.Letter}” at " + string.Join(", ", item.Positions.Take(6)) + (item.Positions.Count > 6 ? "…" : "")));
                return $"{Kind.Label()} uses only {AlphabetText(Kind)}. Not allowed: {what}.";
            }
            if (Truncated && Cap is { } cap) return $"Only the first {cap} letters are used.";
            return null;
        }
    }

    public static string AlphabetText(BioSeqKind kind) => kind switch
    {
        BioSeqKind.Dna => "A, C, G, T and N",
        BioSeqKind.Rna => "A, C, G, U and N",
        _ => "the 20 amino-acid letters, X and *",
    };

    /// <summary>The text cleaned as <paramref name="kind"/>, kept to <paramref name="cap"/> letters if one is given.</summary>
    public BioSequenceInput(string text, BioSeqKind kind, int? cap = null)
    {
        Kind = kind;
        Original = text;
        Cap = cap;
        var lines = text.Split('\n').ToList();
        // (a FASTA header line, ">name …", dropped)
        if (lines.Count > 0 && lines[0].Trim().StartsWith('>')) lines.RemoveAt(0);
        var kept = new string(string.Concat(lines).ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        // (a pasted 5′- and -3′, or 5'- and -3', taken off its ends — before the digits go, or its 5 and 3 would go first
        // and leave the ′ and - behind as letters that do not belong)
        foreach (var prefix in new[] { "5′-", "5'-", "5′", "5'" })
            if (kept.StartsWith(prefix, StringComparison.Ordinal)) { kept = kept[prefix.Length..]; break; }
        foreach (var suffix in new[] { "-3′", "-3'", "3′", "3'" })
            if (kept.EndsWith(suffix, StringComparison.Ordinal)) { kept = kept[..^suffix.Length]; break; }
        kept = new string(kept.Where(c => !char.IsNumber(c)).ToArray());
        var cut = false;
        if (cap is { } c0 && kept.Length > c0)
        {
            kept = kept[..c0];
            cut = true;
        }
        Letters = kept;
        Truncated = cut;
        var bad = new Dictionary<char, List<int>>();
        var order = new List<char>();
        var alphabet = kind.Alphabet();
        for (var i = 0; i < kept.Length; i++)
        {
            var ch = kept[i];
            if (alphabet.Contains(ch)) continue;
            if (!bad.ContainsKey(ch)) { order.Add(ch); bad[ch] = new List<int>(); }
            bad[ch].Add(i + 1);
        }
        Invalid = order.Select(ch => (ch, bad[ch])).ToList();
        if (kind == BioSeqKind.Dna && kept.Contains('U') && !kept.Contains('T') && kept.All(ch => BioSeqKind.Rna.Alphabet().Contains(ch))) Suggestion = BioSeqKind.Rna;
        else if (kind == BioSeqKind.Rna && kept.Contains('T') && !kept.Contains('U') && kept.All(ch => BioSeqKind.Dna.Alphabet().Contains(ch))) Suggestion = BioSeqKind.Dna;
        else Suggestion = null;
    }
}

/// <summary>An open reading frame (§4.3): an AUG and the codons after it in the same frame up to and including the first stop.</summary>
/// <param name="Frame">+1, +2, +3 on the forward strand; −1, −2, −3 on the reverse complement.</param>
/// <param name="Start">1-based on the forward strand: the first base of the start codon (on the reverse strand Start &gt; End,
/// as ORFfinder writes them).</param>
/// <param name="End">The last base of the stop (or of the last whole codon when there is no stop).</param>
/// <param name="Codons">Codons, the stop included.</param>
/// <param name="Protein">The protein, N→C, without the stop.</param>
/// <param name="HasStop">False: no stop before the sequence ends ("open").</param>
public sealed record BioORF(int Frame, int Start, int End, int Codons, string Protein, bool HasStop)
{
    public string FrameLabel => Frame > 0 ? $"+{Frame}" : $"−{-Frame}";
}

/// <summary>Counts of a nucleic acid's bases.</summary>
public struct BioBaseCounts
{
    public int A, C, G, T, U, N;
    public readonly int Total => A + C + G + T + U + N;

    /// <summary>G + C over the known bases.</summary>
    public readonly double GcFraction
    {
        get
        {
            var known = A + C + G + T + U;
            return known > 0 ? (double)(G + C) / known : 0;
        }
    }
}

/// <summary>The operations on nucleic-acid and protein letters.</summary>
public static class BioSeq
{
    /// <summary>A base's Watson–Crick partner (A–T or A–U, G–C; N stays N).</summary>
    public static char Pair(char b, bool rna) => b switch
    {
        'A' => rna ? 'U' : 'T',
        'T' or 'U' => 'A',
        'G' => 'C',
        'C' => 'G',
        _ => b,
    };

    /// <summary>Each base's partner, in the same order: the strand written 3′→5′ under this one.</summary>
    public static string Complement(string s, bool rna = false) => new(s.Select(b => Pair(b, rna)).ToArray());

    public static string Reverse(string s) => new(s.Reverse().ToArray());

    /// <summary>The other strand read 5′→3′ (not the same as the complement, which runs 3′→5′).</summary>
    public static string ReverseComplement(string s, bool rna = false) => new(s.Reverse().Select(b => Pair(b, rna)).ToArray());

    /// <summary>The mRNA a coding (sense) strand gives: the same letters with U for T (transcription copies the template).</summary>
    public static string Transcribe(string codingStrand) => codingStrand.Replace('T', 'U');

    /// <summary>The template strand under a coding strand, written 3′→5′ left to right.</summary>
    public static string TemplateStrand(string coding) => Complement(coding);

    /// <summary>RNA letters back to DNA (U → T).</summary>
    public static string ToDna(string s) => s.Replace('U', 'T');

    /// <summary>The codons of a frame (0, 1 or 2 bases in), whole codons only.</summary>
    public static List<string> Codons(string s, int frame = 0)
    {
        var out_ = new List<string>();
        for (var i = Math.Max(frame, 0); i + 3 <= s.Length; i += 3) out_.Add(s.Substring(i, 3));
        return out_;
    }

    /// <summary>The amino acids of a frame (0, 1 or 2 bases in) on the forward strand, or on the reverse complement: one
    /// letter each, * for a stop, X for a codon with N, ? for anything else.</summary>
    public static string Translate(string s, int frame = 0, bool reverse = false)
    {
        var seq = reverse ? ReverseComplement(ToDna(s)) : s;
        return new string(Codons(seq, frame).Select(c => BioCode.AminoAcid(c) ?? '?').ToArray());
    }

    /// <summary>The protein from the first AUG (or ATG) to the first stop in its frame, without the stop (null: no AUG).</summary>
    public static string? ProteinFromFirstStart(string s)
    {
        var rna = s.ToUpperInvariant().Replace('T', 'U');
        var from = rna.IndexOf("AUG", StringComparison.Ordinal);
        if (from < 0) return null;
        var b = new StringBuilder();
        foreach (var c in Codons(rna[from..]))
        {
            var aa = BioCode.AminoAcid(c) ?? '?';
            if (aa == '*') break;
            b.Append(aa);
        }
        return b.ToString();
    }

    /// <summary>Every ORF of at least <paramref name="minCodons"/> codons (stop included; an open one counts its codons) on
    /// the forward frames and, if asked, the reverse ones — longest first. Each stop ends one ORF, begun at the first AUG
    /// after the previous stop in its frame (ORFfinder's way).</summary>
    public static List<BioORF> Orfs(string s, int minCodons = 10, bool reverseStrand = true)
    {
        var n = s.Length;
        var found = new List<BioORF>();
        void Scan(string seq, bool reverse)
        {
            for (var f = 0; f < 3; f++)
            {
                var cs = Codons(seq, f);
                int? start = null;
                var protein = new StringBuilder();
                void Emit(int lastCodon, bool stop)
                {
                    if (start is not { } st) return;
                    var count = lastCodon - st + 1;
                    if (count < minCodons) return;
                    // (positions on the scanned strand, 1-based, then back to the forward strand)
                    int a = f + st * 3 + 1, b = f + lastCodon * 3 + 3;
                    var (s1, e1) = reverse ? (n - a + 1, n - b + 1) : (a, b);
                    found.Add(new BioORF(reverse ? -(f + 1) : f + 1, s1, e1, count, protein.ToString(), stop));
                }
                for (var k = 0; k < cs.Count; k++)
                {
                    var c = cs[k];
                    var aa = BioCode.AminoAcid(c) ?? '?';
                    if (start == null)
                    {
                        if (c is "AUG" or "ATG")
                        {
                            start = k;
                            protein.Clear().Append('M');
                        }
                        continue;
                    }
                    if (aa == '*')
                    {
                        Emit(k, true);
                        start = null;
                        protein.Clear();
                    }
                    else protein.Append(aa);
                }
                if (start != null) Emit(cs.Count - 1, false);
            }
        }
        var fwd = s.ToUpperInvariant();
        Scan(fwd, false);
        if (reverseStrand) Scan(ReverseComplement(ToDna(fwd)), true);
        // (longest first; then by frame and start, as the Mac sorts them)
        static bool Before(BioORF x, BioORF y) =>
            x.Codons != y.Codons ? x.Codons > y.Codons : (Math.Abs(x.Frame) != Math.Abs(y.Frame) ? x.Frame > y.Frame : x.Start < y.Start);
        return found.OrderBy(o => o, Comparer<BioORF>.Create((x, y) => Before(x, y) ? -1 : Before(y, x) ? 1 : 0)).ToList();
    }

    public static BioBaseCounts Counts(string s)
    {
        var k = new BioBaseCounts();
        foreach (var c in s)
        {
            switch (c)
            {
                case 'A': k.A++; break;
                case 'C': k.C++; break;
                case 'G': k.G++; break;
                case 'T': k.T++; break;
                case 'U': k.U++; break;
                default: k.N++; break;
            }
        }
        return k;
    }

    /// <summary>GC content, 0…1.</summary>
    public static double GcContent(string s) => Counts(s).GcFraction;

    /// <summary>Nucleoside monophosphate masses (g/mol): the DNA ones of §4.2, the RNA ones of §6.6.</summary>
    public static readonly IReadOnlyDictionary<char, double> MonophosphateMass = new Dictionary<char, double> { ['A'] = 331.22, ['C'] = 307.20, ['G'] = 347.22, ['T'] = 322.21 };
    public static readonly IReadOnlyDictionary<char, double> RnaMonophosphateMass = new Dictionary<char, double> { ['A'] = 347.22, ['C'] = 323.20, ['G'] = 363.22, ['U'] = 324.18 };

    /// <summary>One strand's molar mass (g/mol): the monophosphates' masses minus 18.02 for each phosphodiester bond (an
    /// N counted as the four's mean). "About 650 g/mol per base pair" is the quick lab estimate, for the sodium salt.</summary>
    public static double Mass(string s, bool rna = false)
    {
        var table = rna ? RnaMonophosphateMass : MonophosphateMass;
        var mean = table.Values.Sum() / table.Count;
        var sum = s.Sum(c => table.TryGetValue(c, out var m) ? m : mean);
        return s.Length == 0 ? 0 : sum - 18.02 * (s.Length - 1);
    }

    /// <summary>A rough melting temperature for a short DNA primer in 50 mM salt (°C): Wallace's 2·(A+T) + 4·(G+C) for 14
    /// nt or fewer, else 64.9 + 41·(G + C − 16.4)/N.</summary>
    public static double MeltingTemperature(string s)
    {
        var k = Counts(s);
        double n = k.A + k.C + k.G + k.T;
        if (n <= 0) return 0;
        if (n <= 14) return 2 * (k.A + k.T) + 4 * (k.G + k.C);
        return 64.9 + 41 * (k.G + k.C - 16.4) / n;
    }

    /// <summary>The Chargaff check on double-stranded DNA made from this strand and its complement: A = T and G = C, so
    /// %A + %G = 50%. Returns the duplex's counts and %A + %G.</summary>
    public static (BioBaseCounts Counts, double PurinePercent) Chargaff(string s)
    {
        BioBaseCounts one = Counts(s), two = Counts(Complement(s));
        var both = new BioBaseCounts { A = one.A + two.A, C = one.C + two.C, G = one.G + two.G, T = one.T + two.T, N = one.N + two.N };
        var known = both.A + both.C + both.G + both.T;
        return (both, known > 0 ? (double)(both.A + both.G) / known * 100 : 0);
    }

    /// <summary>The sequence in groups of <paramref name="size"/> (10) with the 1-based position of each line's first
    /// letter: the display of §3.7, <paramref name="perLine"/> groups to a line.</summary>
    public static List<(int Start, List<string> Groups)> Lines(string s, int size = 10, int perLine = 6)
    {
        var out_ = new List<(int, List<string>)>();
        var i = 0;
        while (i < s.Length)
        {
            var groups = new List<string>();
            var lineStart = i;
            for (var g = 0; g < perLine && i < s.Length; g++)
            {
                var j = Math.Min(i + size, s.Length);
                groups.Add(s[i..j]);
                i = j;
            }
            out_.Add((lineStart + 1, groups));
        }
        return out_;
    }
}

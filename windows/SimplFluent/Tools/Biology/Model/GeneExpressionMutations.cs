using System.Globalization;

namespace Simpl.Tools;

// Gene Expression's mutations (spec §4.3 Mutations, §6.12, §7 items 16–17), the Mac's GeneExpressionMutations.swift: a
// change to a coding sequence — one base substituted, or 1–6 bases inserted or deleted — written the way genetics
// writes it (c.20A>T, c.6_7insA, c.7del, c.13_15del), and what it does to the protein: silent, missense (conservative or
// not, with the charge change), nonsense (where it is cut and how much is kept), stop-loss, frameshift (only for
// insertions and deletions that are not a multiple of 3), in-frame insertion or deletion, and start-loss. The original
// and the mutant are lined up base for base (a gap where a base is missing) so the view can show both, codon boxes and
// all. Plain .NET.

/// <summary>A change to a coding sequence. Positions are 0-based on the original (HGVS c. numbers are 1-based from the A of ATG).</summary>
public abstract record GXEdit
{
    /// <summary>At most this many bases inserted or deleted at once.</summary>
    public const int MaxLength = 6;

    /// <summary>The sequence changed.</summary>
    public abstract string Apply(string s);

    /// <summary>Bases gained (+) or lost (−).</summary>
    public abstract int LengthChange { get; }

    /// <summary>The first base it touches (an insertion: the base after the gap).</summary>
    public abstract int Position { get; }

    /// <summary>Whether it fits the sequence: a base there, 1–6 bases of A, C, G, T, an insertion between two bases.</summary>
    public abstract bool Fits(string s);

    /// <summary>The change as genetics writes it: c.20A&gt;T, c.6_7insA, c.7del, c.13_15del.</summary>
    public abstract string Hgvs(string s);

    /// <summary>The change in words ("A inserted after c.6", "c.13–15 (GAT) deleted").</summary>
    public abstract string Words(string s);

    private static bool Acgt(char c) => c is 'A' or 'C' or 'G' or 'T';

    /// <summary>One base substituted.</summary>
    public sealed record Substitute(int At, char To) : GXEdit
    {
        public override string Apply(string s) => At >= 0 && At < s.Length ? s[..At] + To + s[(At + 1)..] : s;
        public override int LengthChange => 0;
        public override int Position => At;
        public override bool Fits(string s) => At >= 0 && At < s.Length && Acgt(To) && s[At] != To;
        public override string Hgvs(string s) => $"c.{At + 1}{(At >= 0 && At < s.Length ? s[At] : '?')}>{To}";
        public override string Words(string s) => $"c.{At + 1} {(At >= 0 && At < s.Length ? s[At] : '?')} → {To}";
    }

    /// <summary>Bases inserted after base <c>After</c>.</summary>
    public sealed record Insert(int After, string Bases) : GXEdit
    {
        public override string Apply(string s)
        {
            var at = Math.Min(Math.Max(After + 1, 0), s.Length);
            return s.Insert(at, Bases);
        }
        public override int LengthChange => Bases.Length;
        public override int Position => After + 1;
        public override bool Fits(string s) => After >= 0 && After < s.Length - 1 && Bases.Length is >= 1 and <= MaxLength && Bases.All(Acgt);
        public override string Hgvs(string s) => $"c.{After + 1}_{After + 2}ins{Bases}";
        public override string Words(string s) => $"{Bases} inserted after c.{After + 1}";
    }

    /// <summary>Bases <c>From</c>…<c>To</c> (inclusive) deleted.</summary>
    public sealed record Delete(int From, int To) : GXEdit
    {
        public override string Apply(string s)
        {
            int a = Math.Max(From, 0), b = Math.Min(To, s.Length - 1);
            return a <= b ? s.Remove(a, b - a + 1) : s;
        }
        public override int LengthChange => -(To - From + 1);
        public override int Position => From;
        public override bool Fits(string s) => From >= 0 && To < s.Length && From <= To && To - From + 1 <= MaxLength;
        public override string Hgvs(string s) => From == To ? $"c.{From + 1}del" : $"c.{From + 1}_{To + 1}del";
        public override string Words(string s)
        {
            var bases = From >= 0 && To < s.Length && From <= To ? s[From..(To + 1)] : "";
            return From == To ? $"c.{From + 1} ({bases}) deleted" : $"c.{From + 1}–{To + 1} ({bases}) deleted";
        }
    }

    /// <summary>
    /// Reads a change typed as genetics writes it — c.20A&gt;T, c.6_7insA, c.7del, c.7delC, c.13_15del, c.13_15delGAT,
    /// c.5dup, c.13_15dup ("c." optional, any case) — checked against the sequence: the base named must be the base there.
    /// A duplication is an insertion of a copy. Nothing typed: neither an edit nor an error.
    /// </summary>
    public static (GXEdit? Edit, string? Error) Parse(string text, string s)
    {
        var t = new string(text.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (t.StartsWith("C.", StringComparison.Ordinal)) t = t[2..];
        if (t.Length == 0) return (null, null);
        var n = s.Length;
        string Base(int i) => i >= 0 && i < n ? s[i].ToString() : "?";
        bool InRange(int p) => p >= 1 && p <= n;
        static int? Number(string x) => x.Length > 0 && x.All(char.IsAsciiDigit) && int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
        const string usage = "Write a change as c.20A>T, c.6_7insA, c.7del or c.13_15del.";
        // (a substitution: 20A>T)
        var gt = t.IndexOf('>');
        if (gt >= 0)
        {
            string left = t[..gt], right = t[(gt + 1)..];
            if (left.Length < 2 || right.Length != 1 || Number(left[..^1]) is not { } p || !Acgt(left[^1]) || !Acgt(right[0]))
                return (null, "Write a substitution as c.20A>T.");
            char reff = left[^1], alt = right[0];
            if (!InRange(p)) return (null, $"c.{p} is outside the coding sequence (1–{n}).");
            if (Base(p - 1) != reff.ToString()) return (null, $"c.{p} is {Base(p - 1)}, not {reff}.");
            if (reff == alt) return (null, "That changes nothing.");
            return (new Substitute(p - 1, alt), null);
        }
        // (positions, then what happens there)
        var digits = new string(t.TakeWhile(c => char.IsAsciiDigit(c) || c == '_').ToArray());
        var rest = t[digits.Length..];
        var ps = digits.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(Number).Where(x => x != null).Select(x => x!.Value).ToList();
        if (ps.Count == 0 || ps.Count > 2 || !ps.All(InRange))
            return (null, ps.Count == 0 ? usage : $"Positions run from 1 to {n}.");
        int a = ps[0], b = ps.Count == 2 ? ps[1] : ps[0];
        if (rest.StartsWith("INS", StringComparison.Ordinal))
        {
            var bases = rest[3..];
            if (ps.Count != 2 || b != a + 1) return (null, "An insertion goes between two neighbouring bases: c.6_7insA.");
            if (bases.Length is < 1 or > MaxLength || !bases.All(Acgt)) return (null, $"Insert 1–{MaxLength} bases of A, C, G and T.");
            if (a >= n) return (null, "An insertion goes between two bases of the coding sequence.");
            return (new Insert(a - 1, bases), null);
        }
        if (rest.StartsWith("DUP", StringComparison.Ordinal))
        {
            if (b < a || b - a + 1 > MaxLength) return (null, $"Duplicate 1–{MaxLength} bases.");
            if (b >= n) return (null, "A duplication of the last base falls after the coding sequence.");
            return (new Insert(b - 1, s[(a - 1)..b]), null);
        }
        if (rest.StartsWith("DEL", StringComparison.Ordinal))
        {
            var named = rest[3..];
            if (b < a || b - a + 1 > MaxLength) return (null, $"Delete 1–{MaxLength} bases at once.");
            var there = s[(a - 1)..b];
            if (named.Length > 0 && named != there) return (null, $"c.{a}{(a == b ? "" : $"_{b}")} is {there}, not {named}.");
            return (new Delete(a - 1, b - 1), null);
        }
        return (null, usage);
    }
}

/// <summary>A coding sequence read in frame from its first base: its codons (DNA letters) and its protein up to the first stop.</summary>
public sealed class GXTranslation
{
    public IReadOnlyList<string> Codons { get; }
    /// <summary>One letter each, as far as the first stop (not included).</summary>
    public string Protein { get; }
    /// <summary>The codon index of the first stop, if any.</summary>
    public int? StopAt { get; }

    public GXTranslation(string s)
    {
        var cs = BioSeq.Codons(s);
        Codons = cs;
        var p = new System.Text.StringBuilder();
        int? stop = null;
        for (var k = 0; k < cs.Count; k++)
        {
            var aa = BioCode.AminoAcid(cs[k]) ?? 'X';
            if (aa == '*')
            {
                stop = k;
                break;
            }
            p.Append(aa);
        }
        Protein = p.ToString();
        StopAt = stop;
    }

    /// <summary>The amino acid of codon k ('*' a stop; a space past the end).</summary>
    public char Aa(int k) => k >= 0 && k < Codons.Count ? BioCode.AminoAcid(Codons[k]) ?? 'X' : ' ';
}

/// <summary>A base of the original and the mutant lined up: one side is null where the other has a base the first lacks.</summary>
public readonly record struct GXAlignedBase(char? Original, char? Mutant, int? OriginalIndex, int? MutantIndex)
{
    /// <summary>Substituted, inserted or deleted here.</summary>
    public bool Changed => Original != Mutant;
}

/// <summary>What a mutation does to the protein.</summary>
public enum GXMutationKind { None, Silent, Missense, Nonsense, StopLoss, Frameshift, InFrameInsertion, InFrameDeletion, StartLoss }

public static class GXMutationKinds
{
    public static string Title(this GXMutationKind k) => k switch
    {
        GXMutationKind.None => "No change",
        GXMutationKind.Silent => "Silent",
        GXMutationKind.Missense => "Missense",
        GXMutationKind.Nonsense => "Nonsense",
        GXMutationKind.StopLoss => "Stop-loss",
        GXMutationKind.Frameshift => "Frameshift",
        GXMutationKind.InFrameInsertion => "In-frame insertion",
        GXMutationKind.InFrameDeletion => "In-frame deletion",
        _ => "Start-loss",
    };

    /// <summary>The Mac's raw value ("inFrameDeletion"), for a line of the self-check.</summary>
    public static string Key(this GXMutationKind k)
    {
        var n = k.ToString();
        return char.ToLowerInvariant(n[0]) + n[1..];
    }
}

/// <summary>What a mutation does to the protein.</summary>
/// <param name="Hgvs">The change as genetics writes it (c.20A&gt;T).</param>
/// <param name="Change">The protein change, short: Ala2Val, Phe3Phe, Gln7*, Phe3fs, Asp5 lost.</param>
/// <param name="Lines">What happens, a sentence or two each.</param>
/// <param name="Aligned">The mutant base for base under the original.</param>
/// <param name="From">A missense's first amino acid.</param>
/// <param name="To">A missense's second.</param>
/// <param name="Conservative">Whether a missense keeps the class.</param>
/// <param name="ChangedOriginal">The original codons whose amino acid is changed or lost.</param>
/// <param name="ChangedMutant">The mutant codons whose amino acid is new.</param>
/// <param name="Kept">A nonsense mutation's share of the protein kept (0…1).</param>
/// <param name="ReadThrough">What the mutant makes after a stop-loss, reading on past the old stop (the 3′ UTR's codons).</param>
public sealed record GXMutationResult(GXMutationKind Kind, GXEdit Edit, string Hgvs, string Change, IReadOnlyList<string> Lines,
    GXTranslation Original, GXTranslation Mutant, IReadOnlyList<GXAlignedBase> Aligned, BioAminoAcid? From, BioAminoAcid? To,
    bool? Conservative, IReadOnlySet<int> ChangedOriginal, IReadOnlySet<int> ChangedMutant, double? Kept, string? ReadThrough);

/// <summary>A gene's coding sequence for the Mutations page, and the 3′ UTR after it.</summary>
public sealed record GXMutationSource(string Name, string Cds, string Downstream, bool MatureNumbering);

public static class GXMutations
{
    /// <summary>
    /// Classifies a change to a coding sequence (DNA letters, ATG … stop). <paramref name="downstream"/> is the mRNA after
    /// the stop (its 3′ UTR, DNA letters) that a stop-loss reads on into; <paramref name="matureNumbering"/> adds the mature
    /// protein's numbers (β-globin loses its first Met, so codon 7's Glu is the classic Glu6).
    /// </summary>
    public static GXMutationResult Classify(string cds, GXEdit edit, string downstream = "", bool matureNumbering = false)
    {
        var o = cds.ToUpperInvariant();
        var m = edit.Apply(o);
        GXTranslation ot = new(o), mt = new(m);
        var aligned = Align(o, edit);
        var pos = edit.Position;
        var c = Math.Max(0, Math.Min(pos, Math.Max(o.Length - 1, 0))) / 3;
        var len = Math.Abs(edit.LengthChange);
        static string Three(char a) => a == '*' ? "*" : (BioAminoAcids.ByOne.TryGetValue(a, out var x) ? x.Three : a.ToString());
        static string Name(char a) => a == '*' ? "a stop" : (BioAminoAcids.ByOne.TryGetValue(a, out var x) ? x.Three : a.ToString());
        static string S(int n) => n == 1 ? "" : "s";
        var total = ot.Protein.Length;
        var kind = GXMutationKind.None;
        var change = "";
        var lines = new List<string>();
        BioAminoAcid? from = null, to = null;
        bool? conservative = null;
        var changedO = new HashSet<int>();
        var changedM = new HashSet<int>();
        double? kept = null;
        string? readThrough = null;
        string? Mature(int k, char a, string b) => matureNumbering && k >= 1 ? $"In the mature protein (its first Met removed) this is {Three(a)}{k}{b}." : null;

        if (m == o)
            lines.Add("The sequence is the same as before.");
        else if (pos <= 2 && !(m.Length >= 3 && m[..3] == "ATG"))
        {
            // (start-loss: the ribosome would start at the next AUG instead)
            kind = GXMutationKind.StartLoss;
            change = "Met1 lost";
            changedO = Enumerable.Range(0, Math.Max(ot.Codons.Count, 1)).ToHashSet();
            int? next = null;
            for (var i = 1; i + 3 <= m.Length && m.Length >= 4; i++)
                if (m.Substring(i, 3) == "ATG")
                {
                    next = i;
                    break;
                }
            var firstCodon = m.Length >= 3 ? m[..3] : m;
            lines.Add($"Start-loss: the start codon ATG becomes {firstCodon}, so translation cannot begin there (AUG is the only start codon this tool uses).");
            if (next is { } ni)
            {
                var q = new GXTranslation(m[ni..]).Protein;
                if (q.Length > 0 && ot.Protein.EndsWith(q, StringComparison.Ordinal))
                    lines.Add($"The ribosome would start at the next AUG, c.{ni + 1}, in the original frame: the protein would lack its first {total - q.Length} amino acid{S(total - q.Length)}.");
                else
                    lines.Add($"The ribosome would start at the next AUG, c.{ni + 1}, in a different frame: an unrelated chain of {q.Length} amino acid{S(q.Length)} ({(q.Length > 12 ? q[..12] : q)}{(q.Length > 12 ? "…" : "")}).");
            }
            else lines.Add("There is no other AUG in the coding sequence: no protein would be made from it.");
        }
        else if (len % 3 != 0)
        {
            // (frameshift: every codon after the change is read in a new frame)
            kind = GXMutationKind.Frameshift;
            var p = CommonPrefix(ot.Protein + (ot.StopAt != null ? "*" : ""), mt.Protein + (mt.StopAt != null ? "*" : ""));
            change = p < ot.Protein.Length ? $"{Three(ot.Protein[p])}{p + 1}fs" : "fs";
            changedO = Enumerable.Range(c, Math.Max(ot.Codons.Count, c + 1) - c).ToHashSet();
            var mEnd = mt.StopAt is { } ms ? ms + 1 : mt.Codons.Count;
            changedM = Enumerable.Range(c, Math.Max(mEnd - c, 0)).ToHashSet();
            var what = edit.LengthChange > 0 ? "inserted" : "deleted";
            lines.Add($"Frameshift: {len} base{S(len)} {what}, not a multiple of 3, so every codon after it is read in a new frame.");
            var shown = mt.Protein.Length > 24 ? mt.Protein[..24] + "…" : mt.Protein;
            if (mt.StopAt is { } stop)
                lines.Add($"The new frame's protein, {shown}, meets a stop at codon {stop + 1}: {mt.Protein.Length} amino acids instead of {total}.");
            else
                lines.Add($"The new frame's protein, {shown}, has no stop codon before the coding sequence ends (it would read on into the 3′ UTR).");
        }
        else if (edit.LengthChange != 0)
        {
            // (in-frame: whole codons added or removed, the rest unchanged)
            var ins = edit.LengthChange > 0;
            kind = ins ? GXMutationKind.InFrameInsertion : GXMutationKind.InFrameDeletion;
            string oa = ot.Protein + (ot.StopAt != null ? "*" : ""), ma = mt.Protein + (mt.StopAt != null ? "*" : "");
            var p = CommonPrefix(oa, ma);
            var sfx = Math.Min(CommonSuffix(oa, ma), Math.Min(oa.Length, ma.Length) - p);
            string lost = oa[p..(oa.Length - sfx)], gained = ma[p..(ma.Length - sfx)];
            changedO = Enumerable.Range(p, lost.Length).ToHashSet();
            changedM = Enumerable.Range(p, gained.Length).ToHashSet();
            string List(string a, int k) => a.Length == 0 ? "" : (a.Length == 1 ? $"{Three(a[0])}{k + 1}" : $"{Three(a[0])}{k + 1}–{Three(a[^1])}{k + a.Length}");
            var codons = len / 3;
            // (where the old stop now falls: a stop before it cuts the protein short)
            var shiftedStop = (ot.StopAt ?? ot.Codons.Count) + edit.LengthChange / 3;
            var gainedThree = string.Join("–", gained.Select(Three));
            if (mt.StopAt is { } ms && ms < shiftedStop)
            {
                change = $"stop at codon {ms + 1}";
                kept = (double)mt.Protein.Length / Math.Max(total, 1);
                lines.Add($"In-frame {(ins ? "insertion" : "deletion")}: {len} bases, a multiple of 3, but it makes a stop codon at codon {ms + 1}, so the protein is cut short as a nonsense mutation would: {mt.Protein.Length} of {total} amino acids kept.");
            }
            else if (lost.Length > 0 && gained.Length == 0)
            {
                change = $"{List(lost, p)} lost";
                lines.Add($"In-frame deletion: {len} bases ({codons} codon{S(codons)}), a multiple of 3, so {(lost.Length == 1 ? "one amino acid is" : $"{lost.Length} amino acids are")} removed ({change}) and the rest of the protein is unchanged.");
            }
            else if (lost.Length == 0 && gained.Length > 0)
            {
                var after = p >= 1 ? $" after {Three(oa[p - 1])}{p}" : "";
                change = gainedThree + " inserted" + after;
                lines.Add($"In-frame insertion: {len} bases ({codons} codon{S(codons)}), a multiple of 3, so {(gained.Length == 1 ? "one amino acid is" : $"{gained.Length} amino acids are")} added ({gainedThree}{after}) and the rest of the protein is unchanged.");
            }
            else
            {
                change = $"{List(lost, p)} → {gainedThree}";
                lines.Add($"In-frame {(ins ? "insertion" : "deletion")}: {len} bases, a multiple of 3, across a codon boundary: {change}. The rest of the protein is unchanged.");
            }
            if (MatureNote(lost.Length > 0 ? lost[0] : null, p, matureNumbering) is { } line) lines.Add(line);
        }
        else
        {
            // (one base substituted: one codon changed)
            string oc = c < ot.Codons.Count ? ot.Codons[c] : "", mc = c < mt.Codons.Count ? mt.Codons[c] : "";
            char oa = ot.Aa(c), ma = mt.Aa(c);
            string ocR = oc.Replace('T', 'U'), mcR = mc.Replace('T', 'U');
            changedO = new HashSet<int> { c };
            changedM = new HashSet<int> { c };
            if (ot.StopAt is { } st && c > st)
            {
                // (past the stop: not translated)
                kind = GXMutationKind.Silent;
                change = "after the stop";
                lines.Add("This base lies past the stop codon, in the 3′ UTR: it is not translated, so the protein is unchanged.");
            }
            else if (oa == ma)
            {
                kind = GXMutationKind.Silent;
                change = oa == '*' ? $"*{c + 1}*" : $"{Three(oa)}{c + 1}{Three(ma)}";
                lines.Add(oa == '*'
                    ? $"Silent: the stop codon {ocR} becomes {mcR}, still a stop. The protein is unchanged."
                    : $"Silent: codon {ocR} → {mcR} still codes {Three(oa)} (most amino acids have more than one codon). The protein is unchanged.");
            }
            else if (ma == '*')
            {
                kind = GXMutationKind.Nonsense;
                change = $"{Three(oa)}{c + 1}*";
                kept = (double)c / Math.Max(total, 1);
                var pct = (int)Math.Round((double)c / Math.Max(total, 1) * 100, MidpointRounding.AwayFromZero);
                lines.Add($"Nonsense: codon {ocR} ({Three(oa)}) → {mcR}, a stop codon, so translation ends at codon {c + 1}. The protein is {c} amino acid{S(c)} instead of {total}: {pct}% kept.");
                changedO = Enumerable.Range(c, Math.Max(ot.Codons.Count, c + 1) - c).ToHashSet();
                if (Mature(c, oa, "*") is { } line) lines.Add(line);
            }
            else if (oa == '*')
            {
                kind = GXMutationKind.StopLoss;
                change = $"*{c + 1}{Three(ma)} (reads on)";
                var tail = new GXTranslation(m + downstream.ToUpperInvariant());
                var extra = tail.Protein.Length > total + 1 ? tail.Protein[(total + 1)..] : "";
                readThrough = ma + extra;
                var after = downstream.Length == 0
                    ? "translation reads on past the old stop into the 3′ UTR (not included here) until it meets another stop."
                    : (tail.StopAt != null
                        ? $"translation reads on into the 3′ UTR and adds {Three(ma)}{(extra.Length == 0 ? "" : " and " + string.Join("–", extra.Select(Three)))} before the next stop."
                        : $"translation reads on into the 3′ UTR (adding {string.Join("–", (ma + extra).Select(Three))}) and runs off its end without another stop.");
                lines.Add($"Stop-loss: the stop codon {ocR} → {mcR} now codes {Three(ma)}, so {after}");
            }
            else
            {
                kind = GXMutationKind.Missense;
                change = $"{Three(oa)}{c + 1}{Three(ma)}";
                var a = BioAminoAcids.ByOne.GetValueOrDefault(oa);
                var b = BioAminoAcids.ByOne.GetValueOrDefault(ma);
                from = a;
                to = b;
                if (a != null && b != null)
                {
                    conservative = a.Group == b.Group;
                    lines.Add($"Missense: codon {ocR} → {mcR} changes {a.Three} to {b.Three}: {a.Three} ({GXCodonTable.ClassWords(a)}) → {b.Three} ({GXCodonTable.ClassWords(b)}).");
                    lines.Add(a.Group == b.Group
                        ? $"Conservative: both are {a.Group.Label().ToLowerInvariant()}, so the protein may still fold and work."
                        : "Not conservative: the side chain's class changes, which can change how the protein folds or what it binds.");
                    if (a.Charge != b.Charge) lines.Add($"Charge at cell pH: {ChargeWord(a.Charge)} → {ChargeWord(b.Charge)}.");
                }
                else lines.Add($"Missense: codon {ocR} → {mcR} changes {Name(oa)} to {Name(ma)}.");
                if (Mature(c, oa, Three(ma)) is { } line) lines.Add(line);
            }
        }
        return new GXMutationResult(kind, edit, edit.Hgvs(o), change, lines, ot, mt, aligned, from, to, conservative, changedO, changedM, kept, readThrough);
    }

    private static string? MatureNote(char? a, int k, bool on)
    {
        if (!on || a is not { } x || k < 1) return null;
        return $"In the mature protein (its first Met removed) the first change is at {(BioAminoAcids.ByOne.TryGetValue(x, out var aa) ? aa.Three : x.ToString())}{k}.";
    }

    /// <summary>"negative (−1)", "uncharged (0)", "positive (+1)".</summary>
    public static string ChargeWord(int q) => q < 0 ? "negative (−1)" : (q > 0 ? "positive (+1)" : "uncharged (0)");

    private static int CommonPrefix(string a, string b)
    {
        var n = 0;
        while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
        return n;
    }

    private static int CommonSuffix(string a, string b)
    {
        var n = 0;
        while (n < a.Length && n < b.Length && a[a.Length - 1 - n] == b[b.Length - 1 - n]) n++;
        return n;
    }

    /// <summary>The original and the mutant lined up base for base: a gap in the mutant where bases were deleted, in the
    /// original where they were inserted.</summary>
    public static List<GXAlignedBase> Align(string o, GXEdit edit)
    {
        var outs = new List<GXAlignedBase>();
        switch (edit)
        {
            case GXEdit.Substitute sub:
                for (var i = 0; i < o.Length; i++) outs.Add(new GXAlignedBase(o[i], i == sub.At ? sub.To : o[i], i, i));
                break;
            case GXEdit.Insert ins:
            {
                var mi = 0;
                for (var i = 0; i < o.Length; i++)
                {
                    outs.Add(new GXAlignedBase(o[i], o[i], i, mi));
                    mi++;
                    if (i == ins.After)
                        foreach (var x in ins.Bases)
                        {
                            outs.Add(new GXAlignedBase(null, x, null, mi));
                            mi++;
                        }
                }
                break;
            }
            case GXEdit.Delete del:
            {
                var mi = 0;
                for (var i = 0; i < o.Length; i++)
                {
                    if (i >= del.From && i <= del.To) outs.Add(new GXAlignedBase(o[i], null, i, null));
                    else
                    {
                        outs.Add(new GXAlignedBase(o[i], o[i], i, mi));
                        mi++;
                    }
                }
                break;
            }
        }
        return outs;
    }

    public static GXMutationSource TeachingSource => new("Teaching gene", BioGenes.Teaching.Cds, "GCAATAAAGCC", false);
    public static GXMutationSource HbbSource => new("Human β-globin (HBB)", BioGenes.HbbCDS, "", true);

    /// <summary>The sickle-cell mutation (§6.12): HBB c.20A&gt;T.</summary>
    public static GXEdit SickleEdit => new GXEdit.Substitute(BioGenes.Sickle.Position - 1, BioGenes.Sickle.To);

    /// <summary>The teaching gene's six test vectors (§6.12) as edits, in the table's order.</summary>
    public static IReadOnlyList<(string Label, GXEdit Edit)> TeachingVectors => new (string, GXEdit)[]
    {
        ("c.9C>T (silent)", new GXEdit.Substitute(8, 'T')),
        ("c.5C>T (missense)", new GXEdit.Substitute(4, 'T')),
        ("c.19C>T (nonsense)", new GXEdit.Substitute(18, 'T')),
        ("A inserted after c.6 (frameshift)", new GXEdit.Insert(5, "A")),
        ("c.7 deleted (frameshift)", new GXEdit.Delete(6, 6)),
        ("c.13_15 deleted (in-frame)", new GXEdit.Delete(12, 14)),
    };

    /// <summary>One of each kind for a coding sequence: §6.12's table for the teaching gene; for β-globin the sickle-cell
    /// mutation and its neighbours HbC, HbE and a β⁰-thalassaemia nonsense mutation; a few for one's own.</summary>
    public static IReadOnlyList<(string Label, GXEdit Edit)> Examples(GXMutationGene gene, int length) => gene switch
    {
        GXMutationGene.Teaching => TeachingVectors.Concat(new (string, GXEdit)[]
        {
            ("c.3G>A (start-loss)", new GXEdit.Substitute(2, 'A')),
            ("c.28T>C (stop-loss)", new GXEdit.Substitute(27, 'C')),
            ("c.6_7insGGC (in-frame insertion)", new GXEdit.Insert(5, "GGC")),
        }).ToList(),
        GXMutationGene.Hbb => new (string, GXEdit)[]
        {
            ("Sickle cell, HbS: c.20A>T (missense)", SickleEdit),
            ("HbC: c.19G>A (missense)", new GXEdit.Substitute(18, 'A')),
            ("HbE: c.79G>A (missense)", new GXEdit.Substitute(78, 'A')),
            ("β⁰-thalassaemia: c.118C>T (nonsense)", new GXEdit.Substitute(117, 'T')),
            ("c.9T>C (silent)", new GXEdit.Substitute(8, 'C')),
            ("c.20del (frameshift)", new GXEdit.Delete(19, 19)),
            ("c.19_21del (in-frame deletion)", new GXEdit.Delete(18, 20)),
        },
        _ => OwnExamples(length),
    };

    private static List<(string Label, GXEdit Edit)> OwnExamples(int n)
    {
        var o = new List<(string, GXEdit)>();
        if (n >= 6) o.Add(("c.4del (frameshift)", new GXEdit.Delete(3, 3)));
        if (n >= 9) o.Add(("c.4_6del (in-frame deletion)", new GXEdit.Delete(3, 5)));
        if (n >= 6) o.Add(("c.3G>A (start-loss)", new GXEdit.Substitute(2, 'A')));
        return o;
    }
}

// ---- the self-check ----

public static partial class BioSelfCheck
{
    public static void GeneMutationChecks(BioCheckReport r)
    {
        var cds = BioGenes.Teaching.Cds;
        // (§6.12's table: each edit makes the listed mutant CDS and protein, and is classified as listed)
        var want = new[] { GXMutationKind.Silent, GXMutationKind.Missense, GXMutationKind.Nonsense, GXMutationKind.Frameshift, GXMutationKind.Frameshift, GXMutationKind.InFrameDeletion };
        var changes = new[] { "Phe3Phe", "Ala2Val", "Gln7*", "Phe3fs", "Phe3fs", "Asp5 lost" };
        var table = BioGenes.Teaching.Mutations;
        var vectors = GXMutations.TeachingVectors;
        for (var k = 0; k < vectors.Count && k < table.Count; k++)
        {
            var v = vectors[k];
            var res = GXMutations.Classify(cds, v.Edit, GXMutations.TeachingSource.Downstream);
            var mutant = v.Edit.Apply(cds);
            var listed = table[k];
            var protein = res.Mutant.Protein + (res.Mutant.StopAt != null ? "*" : "");
            var proteinOK = listed.Protein.Contains('…')
                ? res.Mutant.Protein.StartsWith(listed.Protein.Split('…')[0], StringComparison.Ordinal) && res.Mutant.StopAt == null
                : protein == listed.Protein || protein == listed.Protein + "*";
            r.Check($"gx.mutation.{k + 1}", $"{res.Hgvs}:{res.Kind.Key()}:{res.Change}",
                mutant == listed.Cds.Replace("**", "") && proteinOK && res.Kind == want[k] && res.Change == changes[k]);
        }
        // (the HGVS-style field reads each back to the same edit)
        var typed = new[] { "c.9C>T", "c.5C>T", "c.19C>T", "c.6_7insA", "c.7del", "c.13_15del" }.Select(x => GXEdit.Parse(x, cds).Edit).ToList();
        r.Check("gx.mutation.parse", string.Join(",", typed.Where(x => x != null).Select(x => x!.Hgvs(cds))),
            typed.SequenceEqual(vectors.Select(x => (GXEdit?)x.Edit)) && GXEdit.Parse("c.9A>T", cds).Error != null
            && Equals(GXEdit.Parse("c.13_15delGAT", cds).Edit, new GXEdit.Delete(12, 14)) && GXEdit.Parse("c.13_15delGGG", cds).Edit == null
            && Equals(GXEdit.Parse("c.4dup", cds).Edit, new GXEdit.Insert(3, "G")) && GXEdit.Parse("c.6_8insA", cds).Edit == null
            && GXEdit.Parse("c.99C>T", cds).Edit == null);
        // (the sickle-cell mutation: HBB c.20A>T, GAG → GTG, Glu7Val = Glu6Val mature, acidic −1 → nonpolar 0)
        var hbb = GXMutations.HbbSource;
        var s = GXMutations.Classify(hbb.Cds, GXMutations.SickleEdit, matureNumbering: true);
        r.Check("gx.mutation.sickle", $"{s.Hgvs}:{s.Kind.Key()}:{s.Change}",
            s.Hgvs == "c.20A>T" && s.Kind == GXMutationKind.Missense && s.Change == "Glu7Val" && s.Conservative == false
            && s.From?.Charge == -1 && s.To?.Charge == 0 && s.Mutant.Protein.StartsWith(BioGenes.Sickle.ProteinStart, StringComparison.Ordinal)
            && s.Mutant.Protein.Length == 147 && s.Lines.Any(l => l.Contains("Glu (acidic, charged) → Val (nonpolar)"))
            && s.Lines.Any(l => l.Contains("Glu6Val")) && s.Lines.Any(l => l.Contains("negative (−1) → uncharged (0)")));
        // (the other classes: start-loss, stop-loss into the 3′ UTR, an in-frame insertion, a stop to another stop)
        var start = GXMutations.Classify(cds, new GXEdit.Substitute(2, 'A'));
        var stop = GXMutations.Classify(cds, new GXEdit.Substitute(27, 'C'), GXMutations.TeachingSource.Downstream);
        var ins = GXMutations.Classify(cds, new GXEdit.Insert(5, "GGC"));
        var stopStop = GXMutations.Classify(cds, new GXEdit.Substitute(29, 'G'));
        var stopIns = GXMutations.Classify(cds, new GXEdit.Insert(5, "TAA"));
        r.Check("gx.mutation.classes", string.Join(",", new[] { start, stop, ins, stopStop, stopIns }.Select(x => x.Kind.Key())),
            start.Kind == GXMutationKind.StartLoss && stop.Kind == GXMutationKind.StopLoss && stop.ReadThrough == "QAIK" && ins.Kind == GXMutationKind.InFrameInsertion
            && ins.Change == "Gly inserted after Ala2" && ins.Mutant.Protein == "MAGFSDWQKG" && stopStop.Kind == GXMutationKind.Silent
            && stopIns.Kind == GXMutationKind.InFrameInsertion && stopIns.Kept is { } kk && Math.Abs(kk - 2.0 / 9) < 1e-9);
        var nonsense = GXMutations.Classify(cds, new GXEdit.Substitute(18, 'T'));
        r.Check("gx.mutation.nonsense-kept", (nonsense.Kept ?? -1).ToString("F3", CultureInfo.InvariantCulture), nonsense.Kept is { } nk && Math.Abs(nk - 6.0 / 9) < 1e-9);
        // (aligned base for base: a deletion leaves gaps in the mutant, an insertion in the original)
        var del = GXMutations.Align(cds, new GXEdit.Delete(12, 14));
        var add = GXMutations.Align(cds, new GXEdit.Insert(5, "A"));
        r.Check("gx.mutation.align", $"{del.Count},{add.Count}",
            del.Count == 30 && del.Count(x => x.Mutant == null) == 3 && add.Count == 31 && add[6].Original == null && add[6].Mutant == 'A'
            && add[^1].MutantIndex == 30);
    }
}

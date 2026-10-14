namespace Simpl.Tools;

// Crosses, worked exactly (spec §4.5, §5.3, §5.6.2), the Mac's BioGenetics.swift: each gene's alleles and how they show
// (complete, incomplete and codominance, ABO, X-linked, a lethal homozygote), a parent's gametes as the product of its
// genes' alleles (independent assortment), the offspring as every pair of gametes, and the results the Cross page shows —
// genotypes and phenotypes in their conventional orders, their ratios kept apart (confusing them is a named exam error),
// daughters and sons apart for an X-linked gene, the dead left out of the born. Probabilities are fractions, never
// rounded. At most one X-linked gene in a cross (two on one X would be linked, not assorting independently).

/// <summary>How a gene's alleles show (§5.3.1).</summary>
public enum BioInheritance
{
    Complete, Incomplete, Codominant, Abo, XRecessive, XDominant,
    /// <summary>The dominant allele's homozygote dies (yellow mice: A^Y A^Y), so A^Y a × A^Y a is born 2 : 1.</summary>
    LethalDominantHomozygote,
    /// <summary>The recessive homozygote dies.</summary>
    LethalRecessive,
}

public static class BioInheritances
{
    public static string Label(this BioInheritance p) => p switch
    {
        BioInheritance.Complete => "Complete dominance",
        BioInheritance.Incomplete => "Incomplete dominance",
        BioInheritance.Codominant => "Codominance",
        BioInheritance.Abo => "ABO blood groups",
        BioInheritance.XRecessive => "X-linked recessive",
        BioInheritance.XDominant => "X-linked dominant",
        BioInheritance.LethalDominantHomozygote => "Lethal homozygote (dominant)",
        _ => "Lethal recessive",
    };

    public static bool IsXLinked(this BioInheritance p) => p is BioInheritance.XRecessive or BioInheritance.XDominant;

    /// <summary>Shown as dominant or recessive (the phenotypic ratio's "dominant traits first").</summary>
    public static bool HasDominance(this BioInheritance p) =>
        p is BioInheritance.Complete or BioInheritance.LethalDominantHomozygote or BioInheritance.LethalRecessive || p.IsXLinked();
}

/// <summary>How one gene shows in an individual: its label, whether it shows the dominant trait (complete, lethal and
/// X-linked genes: these come first in a phenotypic ratio), whether it dies, and (X-linked recessive) whether it is an
/// unaffected female with one recessive allele.</summary>
public sealed record BioPhenotype(string Label, bool Dominant, bool Dies = false, bool Carrier = false);

/// <summary>A sex, for X-linked genes.</summary>
public enum BioSex { Female, Male }

/// <summary>A gene of a cross: its letter, how it is inherited, its alleles (dominant first) and, if wanted, its phenotypes' names.</summary>
public sealed class BioGene
{
    /// <summary>The gene's letter, uppercase ('A').</summary>
    public char Letter { get; set; }
    public BioInheritance Pattern { get; set; }
    /// <summary>Its alleles' symbols in order, dominant first: "A", "a"; "C^R", "C^W"; "I^A", "I^B", "i"; "X^A", "X^a", "Y".</summary>
    public List<string> Alleles { get; set; }
    /// <summary>Its phenotypes' names, if not the usual ("A_" and "aa"; red, pink and white; A, B, AB and O; …): for a
    /// complete, X-linked or lethal gene [dominant, recessive]; for incomplete dominance [homozygote 1, heterozygote,
    /// homozygote 2].</summary>
    public List<string>? TraitNames { get; set; }

    public BioGene(char letter, BioInheritance pattern = BioInheritance.Complete, IEnumerable<string>? alleles = null, IEnumerable<string>? traitNames = null)
    {
        Letter = char.ToUpperInvariant(letter);
        Pattern = pattern;
        Alleles = alleles?.ToList() ?? DefaultAlleles(Letter, pattern);
        TraitNames = traitNames?.ToList();
    }

    public static List<string> DefaultAlleles(char l, BioInheritance p)
    {
        var low = char.ToLowerInvariant(l);
        return p switch
        {
            BioInheritance.Complete or BioInheritance.LethalDominantHomozygote or BioInheritance.LethalRecessive => new() { $"{l}", $"{low}" },
            BioInheritance.Incomplete or BioInheritance.Codominant => new() { $"{l}^R", $"{l}^W" },
            BioInheritance.Abo => new() { "I^A", "I^B", "i" },
            _ => new() { $"X^{l}", $"X^{low}", "Y" },
        };
    }

    /// <summary>The Y "allele" of an X-linked gene (its last).</summary>
    public int? YIndex => Pattern.IsXLinked() ? Alleles.Count - 1 : null;

    /// <summary>A genotype's text: "Aa", "C^R C^W", "I^A i", "X^A Y".</summary>
    public string Text(int[] g)
    {
        var syms = g.Select(i => Alleles[i]).ToList();
        return syms.All(s => s.Length == 1) ? string.Concat(syms) : string.Join(" ", syms);
    }

    /// <summary>How a genotype (two allele indices, sorted) shows.</summary>
    public BioPhenotype Phenotype(int[] g)
    {
        var i = g.Length > 0 ? g[0] : 0;
        var j = g.Length > 1 ? g[1] : i;
        string Named(int k, string fallback) => (TraitNames?.Count ?? 0) > k ? TraitNames![k] : fallback;
        var low = char.ToLowerInvariant(Letter);
        switch (Pattern)
        {
            case BioInheritance.Complete:
                return i == 0 ? new BioPhenotype(Named(0, $"{Letter}_"), true) : new BioPhenotype(Named(1, $"{low}{low}"), false);
            case BioInheritance.LethalDominantHomozygote:
                if (i == 0 && j == 0) return new BioPhenotype("dies", true, Dies: true);
                return i == 0 ? new BioPhenotype(Named(0, $"{Letter}_"), true) : new BioPhenotype(Named(1, $"{low}{low}"), false);
            case BioInheritance.LethalRecessive:
                if (i == 0) return new BioPhenotype(Named(0, $"{Letter}_"), true);
                return new BioPhenotype("dies", false, Dies: true);
            case BioInheritance.Incomplete:
            {
                var k = i == j ? (i == 0 ? 0 : 2) : 1;
                return new BioPhenotype(Named(k, new[] { "red", "pink", "white" }[k]), false);
            }
            case BioInheritance.Codominant:
            {
                var sups = new SortedSet<int> { i, j }.Select(x => Superscript(Alleles[x]));
                return new BioPhenotype(Named(i == j ? (i == 0 ? 0 : 2) : 1, string.Concat(sups)), false);
            }
            case BioInheritance.Abo:
            {
                var s = new SortedSet<int> { i, j };
                bool Is(params int[] set) => s.SetEquals(set);
                var label = Is(0) || Is(0, 2) ? "A" : (Is(1) || Is(1, 2) ? "B" : (Is(0, 1) ? "AB" : "O"));
                return new BioPhenotype(label, false);
            }
            case BioInheritance.XRecessive:
            {
                var xs = new[] { i, j }.Where(x => x != YIndex).ToList();
                var affected = !xs.Contains(0);
                var carrier = !affected && xs.Count == 2 && xs.Contains(1);
                return new BioPhenotype(affected ? Named(1, "affected") : Named(0, "unaffected"), !affected, Carrier: carrier);
            }
            default:
            {
                var affected = i == 0 || j == 0;
                return new BioPhenotype(affected ? Named(0, "affected") : Named(1, "unaffected"), affected);
            }
        }
    }

    /// <summary>"C^R" → "R"; "A" → "A".</summary>
    public static string Superscript(string allele)
    {
        var r = allele.IndexOf('^');
        return r >= 0 ? allele[(r + 1)..] : allele;
    }
}

/// <summary>What the Probability page asks of one gene: AA, Aa, aa, A_ or any (for a two-allele gene).</summary>
public enum BioTarget { HomDominant, Heterozygous, HomRecessive, Dominant, Any }

public static class BioTargets
{
    public static string Text(this BioTarget t) => t switch
    {
        BioTarget.HomDominant => "AA", BioTarget.Heterozygous => "Aa", BioTarget.HomRecessive => "aa", BioTarget.Dominant => "A_", _ => "any",
    };

    /// <summary>The genotypes (allele indices, sorted) it means for a two-allele gene.</summary>
    public static int[][] Genotypes(this BioTarget t) => t switch
    {
        BioTarget.HomDominant => new[] { new[] { 0, 0 } },
        BioTarget.Heterozygous => new[] { new[] { 0, 1 } },
        BioTarget.HomRecessive => new[] { new[] { 1, 1 } },
        BioTarget.Dominant => new[] { new[] { 0, 0 }, new[] { 0, 1 } },
        _ => new[] { new[] { 0, 0 }, new[] { 0, 1 }, new[] { 1, 1 } },
    };
}

/// <summary>A cross: its genes and its two parents' genotypes (per gene, two allele indices — an X-linked father's an X
/// and Y). Parent 1 is the mother when a gene is X-linked.</summary>
public sealed class BioCross
{
    public List<BioGene> Genes { get; }
    public int[][] Parent1 { get; }
    public int[][] Parent2 { get; }

    public BioCross(List<BioGene> genes, int[][] parent1, int[][] parent2)
    {
        Genes = genes;
        Parent1 = parent1.Select(p => p.OrderBy(x => x).ToArray()).ToArray();
        Parent2 = parent2.Select(p => p.OrderBy(x => x).ToArray()).ToArray();
    }

    /// <summary>Two int lists in lexicographic order (Swift's lexicographicallyPrecedes).</summary>
    public static int Lex(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++) if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
        return a.Count.CompareTo(b.Count);
    }

    /// <summary>Two genotypes in canonical order: gene by gene, each gene's sorted pair lexicographically.</summary>
    public static int Compare(int[][] a, int[][] b)
    {
        for (var k = 0; k < Math.Min(a.Length, b.Length); k++)
        {
            var c = Lex(a[k], b[k]);
            if (c != 0) return c;
        }
        return 0;
    }

    private static string Key(int[] g) => string.Join(",", g);
    private static string Key(int[][] g) => string.Join("|", g.Select(Key));

    /// <summary>A parent's gametes (§5.6.2): the Cartesian product of its genes' alleles (1/2 each from a heterozygote, 1
    /// from a homozygote), identical gametes merged, in canonical order. A gamete is one allele index per gene.</summary>
    public static List<(int[] Gamete, BioFrac P)> Gametes(int[][] genotype)
    {
        var out_ = new List<(int[], BioFrac)> { (Array.Empty<int>(), BioFrac.One) };
        foreach (var g in genotype)
        {
            var choices = g.Length == 2 && g[0] != g[1]
                ? new[] { (g[0], new BioFrac(1, 2)), (g[1], new BioFrac(1, 2)) }
                : new[] { (g.Length > 0 ? g[0] : 0, BioFrac.One) };
            var next = new List<(int[], BioFrac)>();
            foreach (var (partial, p) in out_)
                foreach (var (a, q) in choices) next.Add((partial.Append(a).ToArray(), p * q));
            out_ = next;
        }
        var merged = new Dictionary<string, (int[] G, BioFrac P)>();
        foreach (var (g, p) in out_)
            merged[Key(g)] = merged.TryGetValue(Key(g), out var old) ? (g, old.P + p) : (g, p);
        return merged.Values.OrderBy(v => v.G, Comparer<int[]>.Create((x, y) => Lex(x, y))).Select(v => (v.G, v.P)).ToList();
    }

    public List<(int[] Gamete, BioFrac P)> Gametes1 => Gametes(Parent1);
    public List<(int[] Gamete, BioFrac P)> Gametes2 => Gametes(Parent2);

    /// <summary>The offspring genotype from two gametes: each gene's allele pair, sorted (dominant first, X before Y).</summary>
    public int[][] Child(int[] a, int[] b) => a.Zip(b, (x, y) => new[] { Math.Min(x, y), Math.Max(x, y) }).ToArray();

    /// <summary>The Punnett square: parent 1's gametes down the side, parent 2's across the top.</summary>
    public int[][][][] Square
    {
        get
        {
            var g2 = Gametes2;
            return Gametes1.Select(g1 => g2.Select(x => Child(g1.Gamete, x.Gamete)).ToArray()).ToArray();
        }
    }

    /// <summary>The offspring's sex, when a gene is X-linked (a Y from the father makes a son).</summary>
    public BioSex? Sex(int[][] genotype)
    {
        for (var k = 0; k < Genes.Count; k++)
            if (Genes[k].Pattern.IsXLinked()) return genotype[k].Contains(Genes[k].YIndex ?? -1) ? BioSex.Male : BioSex.Female;
        return null;
    }

    public bool HasXLinked => Genes.Any(g => g.Pattern.IsXLinked());

    /// <summary>Every offspring genotype and its probability (merged), in canonical order.</summary>
    public List<(int[][] Genotype, BioFrac P)> Offspring
    {
        get
        {
            var d = new Dictionary<string, (int[][] G, BioFrac P)>();
            var g2 = Gametes2;
            foreach (var (a, p1) in Gametes1)
                foreach (var (b, p2) in g2)
                {
                    var c = Child(a, b);
                    var k = Key(c);
                    d[k] = d.TryGetValue(k, out var old) ? (c, old.P + p1 * p2) : (c, p1 * p2);
                }
            return d.Values.OrderBy(v => v.G, Comparer<int[][]>.Create(Compare)).Select(v => (v.G, v.P)).ToList();
        }
    }

    public string GenotypeText(int[][] g)
    {
        var parts = Genes.Zip(g, (gene, x) => gene.Text(x)).ToList();
        return parts.All(p => !p.Contains(' ') && !p.Contains('^')) ? string.Concat(parts) : string.Join(" ", parts);
    }

    public List<BioPhenotype> Phenotypes(int[][] g) => Genes.Zip(g, (gene, x) => gene.Phenotype(x)).ToList();

    public string PhenotypeText(int[][] g)
    {
        var ps = Phenotypes(g);
        var symbolic = Genes.Zip(ps).All(t => t.First.Pattern == BioInheritance.Complete && t.First.TraitNames == null && !t.Second.Label.Contains(' '));
        return symbolic ? string.Concat(ps.Select(p => p.Label)) : string.Join(", ", ps.Select(p => p.Label));
    }

    /// <summary>The results, for everyone, or for daughters or sons alone (probabilities then within that sex).</summary>
    public BioCrossResult Result(BioSex? only = null)
    {
        var gametes1 = Gametes1;
        var gametes2 = Gametes2;
        int n1 = gametes1.Count, n2 = gametes2.Count;
        var cells = new Dictionary<string, int>();
        foreach (var g1 in gametes1)
            foreach (var g2 in gametes2)
            {
                var k = Key(Child(g1.Gamete, g2.Gamete));
                cells[k] = cells.GetValueOrDefault(k) + 1;
            }
        var outs = Offspring;
        if (only is { } s) outs = outs.Where(o => Sex(o.Genotype) == s).ToList();
        var total = outs.Aggregate(BioFrac.Zero, (t, o) => t + o.P);
        if (total.IsZero) return new BioCrossResult(new(), new(), n1 * n2, BioFrac.Zero);
        var rows = new List<BioCrossResult.GenotypeRow>();
        foreach (var o in outs)
        {
            var ps = Phenotypes(o.Genotype);
            rows.Add(new BioCrossResult.GenotypeRow(GenotypeText(o.Genotype), o.Genotype, cells.GetValueOrDefault(Key(o.Genotype)), o.P / total,
                PhenotypeText(o.Genotype), ps.Any(p => p.Dies), ps.Any(p => p.Carrier)));
        }
        // (the phenotypes of the born: in order of dominant traits shown, most first, then by gene in the order each
        // gene's phenotypes first appear among the genotypes)
        var born = rows.Where(r => !r.Dies).ToList();
        var bornTotal = born.Aggregate(BioFrac.Zero, (t, r) => t + r.P);
        var firstSeen = Enumerable.Range(0, Genes.Count).Select(_ => new Dictionary<string, int>()).ToArray();
        foreach (var r in rows)
        {
            var ps = Phenotypes(r.Genotype);
            for (var k = 0; k < ps.Count; k++) firstSeen[k].TryAdd(ps[k].Label, firstSeen[k].Count);
        }
        var groups = new Dictionary<string, (BioFrac P, int Dominants, int[] Seen, BioFrac Carriers)>();
        var order = new List<string>();
        foreach (var r in born)
        {
            var ps = Phenotypes(r.Genotype);
            var dominantCount = Genes.Zip(ps).Count(t => t.First.Pattern.HasDominance() && t.Second.Dominant);
            var seen = ps.Select((p, k) => firstSeen[k].GetValueOrDefault(p.Label)).ToArray();
            if (!groups.TryGetValue(r.Phenotype, out var old)) { order.Add(r.Phenotype); old = (BioFrac.Zero, 0, Array.Empty<int>(), BioFrac.Zero); }
            groups[r.Phenotype] = (old.P + r.P, -dominantCount, seen, old.Carriers + (r.Carrier ? r.P : BioFrac.Zero));
        }
        var sorted = order.OrderBy(name => name, Comparer<string>.Create((a, b) =>
        {
            var x = groups[a];
            var y = groups[b];
            return x.Dominants != y.Dominants ? x.Dominants.CompareTo(y.Dominants) : Lex(x.Seen, y.Seen);
        })).ToList();
        var phen = sorted.Select(name =>
        {
            var g = groups[name];
            var p = bornTotal.IsZero ? BioFrac.Zero : g.P / bornTotal;
            var c = bornTotal.IsZero ? BioFrac.Zero : g.Carriers / bornTotal;
            return new BioCrossResult.PhenotypeRow(name, p, c);
        }).ToList();
        return new BioCrossResult(rows, phen, n1 * n2, bornTotal);
    }

    /// <summary>The chance of one gene's genotype in the offspring (that gene's own small Punnett square).</summary>
    public BioFrac Probability(int gene, int[][] genotypes)
    {
        var a = Gametes(new[] { Parent1[gene] });
        var b = Gametes(new[] { Parent2[gene] });
        var p = BioFrac.Zero;
        foreach (var (x, px) in a)
            foreach (var (y, py) in b)
            {
                var pair = new[] { Math.Min(x[0], y[0]), Math.Max(x[0], y[0]) };
                if (genotypes.Any(g => g.SequenceEqual(pair))) p = p + px * py;
            }
        return p;
    }

    /// <summary>The product rule (§4.5 Probability): the chance of a genotype asked gene by gene (AA, Aa, aa, A_ or any, for
    /// a two-allele gene), each gene's factor and their product.</summary>
    public (List<BioFrac> Factors, BioFrac Product) Probability(IReadOnlyList<BioTarget> targets)
    {
        var fs = new List<BioFrac>();
        for (var k = 0; k < targets.Count && k < Genes.Count; k++) fs.Add(Probability(k, targets[k].Genotypes()));
        return (fs, fs.Aggregate(BioFrac.One, (a, b) => a * b));
    }

    // ---- typed genotypes ----

    /// <summary>
    /// A cross typed as text: "AaBb × aaBb", "AaBb x aaBb", "I^A i × I^B i", "X^A X^a × X^a Y", "C^RC^W x C^RC^W". Genes are
    /// taken in the order they first appear; "aA" is read as "Aa". Inheritance is guessed from the symbols (X^ X-linked
    /// recessive, I^A/I^B ABO, A^Y with a plain recessive a lethal homozygote, another ^ incomplete dominance, else
    /// complete) and can be changed after.
    /// </summary>
    public static (BioCross? Cross, string? Error) Parse(string text)
    {
        var sides = text.Replace("×", " x ").Split(" x ").Select(s => s.Trim(' ', '\t')).Where(s => s.Length > 0).ToList();
        if (sides.Count != 2) return (null, "Write two genotypes with × between them, like AaBb × aaBb.");
        var t1 = Tokens(sides[0]);
        var t2 = Tokens(sides[1]);
        if (t1.Count == 0 || t2.Count == 0) return (null, "Each parent needs its alleles, like Aa.");
        var all = t1.Concat(t2).ToList();
        // (each gene by its key: its letter, or "X:a" for an X-linked gene's letter, or "I" for ABO)
        var order = new List<string>();
        var symbols = new Dictionary<string, List<string>>();
        foreach (var tok in all)
        {
            var k = GeneKey(tok, all);
            if (!symbols.ContainsKey(k)) { order.Add(k); symbols[k] = new List<string>(); }
            if (!symbols[k].Contains(tok)) symbols[k].Add(tok);
        }
        var genes = new List<BioGene>();
        foreach (var k in order)
        {
            var syms = symbols[k];
            if (k.StartsWith("X:", StringComparison.Ordinal)) genes.Add(new BioGene(k[2], BioInheritance.XRecessive));
            else if (k == "I" && syms.Any(s => s is "I^A" or "I^B")) genes.Add(new BioGene('I', BioInheritance.Abo));
            else if (syms.Any(s => s.Contains('^')))
            {
                // (superscripts: incomplete dominance, the alleles as typed, an uppercase plain one first, else typed order)
                var sorted = syms.Select((s, i) => (s, i)).OrderBy(x => Rank(x.s)).ThenBy(x => x.i).Select(x => x.s).ToList();
                var plainLow = syms.Any(s => !s.Contains('^') && s == s.ToLowerInvariant());
                // (a ^Y allele over a plain recessive, as the yellow mouse's A^Y over a: dominant for its colour, lethal
                //  when homozygous — so A^Y a × A^Y a is born 2 yellow : 1 non-yellow, as the Yellow mice example)
                if (plainLow && sorted.Count == 2 && sorted[0] == $"{k[0]}^Y")
                    genes.Add(new BioGene(k[0], BioInheritance.LethalDominantHomozygote, sorted, k[0] == 'A' ? new[] { "yellow", "non-yellow" } : null));
                else genes.Add(new BioGene(k[0], plainLow ? BioInheritance.Complete : BioInheritance.Incomplete, sorted));
            }
            else genes.Add(new BioGene(k[0], BioInheritance.Complete));
        }
        List<int>[]? Genotype(List<string> toks)
        {
            var out_ = Enumerable.Range(0, genes.Count).Select(_ => new List<int>()).ToArray();
            foreach (var tok in toks)
            {
                var gi = order.IndexOf(GeneKey(tok, all));
                if (gi < 0) return null;
                var ai = genes[gi].Alleles.IndexOf(tok);
                if (ai < 0) return null;
                out_[gi].Add(ai);
            }
            return out_;
        }
        if (Genotype(t1) is not { } g1 || Genotype(t2) is not { } g2) return (null, "Those alleles could not be read.");
        var gs1 = g1.ToList();
        var gs2 = g2.ToList();
        // (a lone Y token joins the X-linked gene)
        var xi = genes.FindIndex(g => g.Pattern.IsXLinked());
        var yi = order.IndexOf("Y");
        if (xi >= 0 && yi >= 0)
        {
            foreach (var g in new[] { gs1, gs2 })
                if (g[yi].Count > 0) g[xi].Add(genes[xi].Alleles.Count - 1);
            genes.RemoveAt(yi);
            gs1.RemoveAt(yi);
            gs2.RemoveAt(yi);
        }
        for (var k = 0; k < genes.Count; k++)
            if (gs1[k].Count != 2 || gs2[k].Count != 2)
                return (null, $"Each parent needs two alleles of {genes[k].Letter} (an X-linked gene: X X for the mother, X Y for the father).");
        return (new BioCross(genes, gs1.Select(g => g.ToArray()).ToArray(), gs2.Select(g => g.ToArray()).ToArray()), null);
    }

    /// <summary>A genotype's allele tokens: "AaBb" → A a B b; "C^RC^W" → C^R C^W; "X^AY" → X^A Y.</summary>
    public static List<string> Tokens(string s)
    {
        var c = s.Where(ch => !char.IsWhiteSpace(ch)).ToArray();
        var out_ = new List<string>();
        var i = 0;
        while (i < c.Length)
        {
            if (!char.IsLetter(c[i])) { i++; continue; }
            if (i + 2 < c.Length && c[i + 1] == '^')
            {
                out_.Add(new string(new[] { c[i], '^', c[i + 2] }));
                i += 3;
            }
            else
            {
                out_.Add(c[i].ToString());
                i++;
            }
        }
        return out_;
    }

    /// <summary>Which gene an allele token belongs to.</summary>
    private static string GeneKey(string tok, List<string> all)
    {
        if (tok.StartsWith("X^", StringComparison.Ordinal)) return "X:" + tok[2..].ToUpperInvariant();
        if (tok == "Y" && all.Any(t => t.StartsWith("X^", StringComparison.Ordinal))) return "Y";
        if ((tok == "i" || tok.StartsWith("I^", StringComparison.Ordinal)) && all.Any(t => t is "I^A" or "I^B")) return "I";
        return tok[..1].ToUpperInvariant();
    }

    /// <summary>Dominant first: an uppercase symbol before a lowercase one, then as typed.</summary>
    private static int Rank(string tok) => tok.Length == 0 ? 2 : (char.IsUpper(tok[0]) ? 0 : 1);
}

/// <summary>A cross's results: its genotypes (all, the dead marked) and its born phenotypes, with their ratios.</summary>
public sealed class BioCrossResult
{
    /// <summary>A genotype: its text, its alleles, the cells of the Punnett square with it, its share of the offspring (of
    /// this sex, when split), its phenotype, and whether it dies or is a carrier.</summary>
    public sealed record GenotypeRow(string Text, int[][] Genotype, int Count, BioFrac P, string Phenotype, bool Dies, bool Carrier);

    /// <summary>A phenotype of the born: its text, its share of the born, and the share of the born that show it and are
    /// carriers (X-linked recessive).</summary>
    public sealed record PhenotypeRow(string Text, BioFrac P, BioFrac Carriers);

    public List<GenotypeRow> Genotypes { get; }
    public List<PhenotypeRow> Phenotypes { get; }
    public int SquareSize { get; }
    /// <summary>The share of offspring born alive (1 unless a homozygote is lethal).</summary>
    public BioFrac BornFraction { get; }

    public BioCrossResult(List<GenotypeRow> genotypes, List<PhenotypeRow> phenotypes, int squareSize, BioFrac bornFraction)
    {
        Genotypes = genotypes;
        Phenotypes = phenotypes;
        SquareSize = squareSize;
        BornFraction = bornFraction;
    }

    /// <summary>"1 : 2 : 1": every genotype, the dead included.</summary>
    public int[] GenotypicRatio => BioRatio.Whole(Genotypes.Select(g => g.P).ToList());
    /// <summary>The born genotypes' ratio ("2 : 1" for A^Y a × A^Y a).</summary>
    public int[] BornGenotypicRatio => BioRatio.Whole(Genotypes.Where(g => !g.Dies).Select(g => g.P).ToList());
    public int[] PhenotypicRatio => BioRatio.Whole(Phenotypes.Select(p => p.P).ToList());

    /// <summary>"1 AA : 2 Aa : 1 aa".</summary>
    public string GenotypicRatioText => string.Join(" : ", GenotypicRatio.Zip(Genotypes, (n, g) => $"{n} {g.Text}"));

    /// <summary>"9 A_B_ : 3 A_bb : 3 aaB_ : 1 aabb".</summary>
    public string PhenotypicRatioText => string.Join(" : ", PhenotypicRatio.Zip(Phenotypes, (n, p) => $"{n} {p.Text}"));
}

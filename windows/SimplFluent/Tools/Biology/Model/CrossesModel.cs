using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simpl.Tools;

// Genetics Crosses' own model (spec §4.5, §5.3), the Mac's CrossesModel.swift line for line: the cross as the Cross page
// edits it — one to three genes, each with its letter, alleles and pattern from §5.3.1's table, and each parent's
// genotypes — and what the pages show of it: the gametes, the Punnett square tinted by phenotype, the forked-line
// diagram, the product and sum rules and the chance over several children, and the chi-square test with its null
// hypothesis written out. The arithmetic is BioCross's (BioGenetics.cs), exact throughout. What the tool keeps between
// its views is CrossesModel at the foot; the pedigree and its mode checker are PedigreeModel.cs. Plain .NET, no WPF, so
// the unit tests reach it.

// ---- examples (§5.3.1) ----

/// <summary>A gene from §5.3.1's table, ready to cross: the gene, the parents it starts from, and a line on what it shows.</summary>
public sealed record CrossExample(string Id, string Title, BioGene Gene, int[] Parent1, int[] Parent2, string Note);

public static class CrossExamples
{
    public static readonly IReadOnlyList<CrossExample> All = new CrossExample[]
    {
        new("pea", "Pea seed shape", new BioGene('R', BioInheritance.Complete, traitNames: new[] { "round", "wrinkled" }),
            new[] { 0, 1 }, new[] { 0, 1 }, "R (round) is dominant to r (wrinkled): RR and Rr are round, rr is wrinkled."),
        new("snapdragon", "Snapdragon colour", new BioGene('C', BioInheritance.Incomplete, new[] { "C^R", "C^W" }, new[] { "red", "pink", "white" }),
            new[] { 0, 1 }, new[] { 0, 1 }, "Incomplete dominance: the heterozygote is in between, so C^R C^W is pink."),
        new("mn", "MN blood group", new BioGene('L', BioInheritance.Codominant, new[] { "L^M", "L^N" }, new[] { "M", "MN", "N" }),
            new[] { 0, 1 }, new[] { 0, 1 }, "Codominance: L^M L^N makes both the M and the N antigen."),
        new("roan", "Roan cattle", new BioGene('C', BioInheritance.Codominant, new[] { "C^R", "C^W" }, new[] { "red", "roan", "white" }),
            new[] { 0, 1 }, new[] { 0, 1 }, "Codominance: a roan coat has red hairs and white hairs side by side, not pink ones."),
        new("abo", "ABO blood groups", new BioGene('I', BioInheritance.Abo),
            new[] { 0, 2 }, new[] { 1, 2 }, "I^A and I^B are codominant and both dominant to i: I^A i is type A, I^A I^B is AB, ii is O."),
        new("colourblind", "Red–green colour blindness", new BioGene('B', BioInheritance.XRecessive, traitNames: new[] { "normal vision", "colour-blind" }),
            new[] { 0, 1 }, new[] { 0, 2 }, "X-linked recessive: a son's only X comes from his mother, so a carrier mother passes it to half her sons."),
        new("haemophilia", "Haemophilia A", new BioGene('H', BioInheritance.XRecessive, traitNames: new[] { "unaffected", "haemophilia" }),
            new[] { 0, 1 }, new[] { 0, 2 }, "X-linked recessive: males are hemizygous, so one X^h is enough to show it."),
        new("rickets", "Hypophosphataemic rickets", new BioGene('R', BioInheritance.XDominant, traitNames: new[] { "rickets", "unaffected" }),
            new[] { 1, 1 }, new[] { 0, 2 }, "X-linked dominant: an affected father passes it to every daughter and to no son."),
        new("yellow", "Yellow mice", new BioGene('A', BioInheritance.LethalDominantHomozygote, new[] { "A^Y", "a" }, new[] { "yellow", "non-yellow" }),
            new[] { 0, 1 }, new[] { 0, 1 }, "A^Y A^Y dies before birth, so A^Y a × A^Y a is born 2 yellow : 1 non-yellow."),
        new("lethalRecessive", "A recessive lethal", new BioGene('A', BioInheritance.LethalRecessive),
            new[] { 0, 1 }, new[] { 0, 1 }, "aa dies before birth, so Aa × Aa is born 1 AA : 2 Aa, all showing the dominant trait."),
    };

    public static CrossExample? Find(string? id) => All.FirstOrDefault(e => e.Id == id);
}

// ---- the cross being edited ----

/// <summary>
/// The cross on the Cross page: its genes (one to three, at most one X-linked) and the two parents' genotypes per gene
/// (allele indices, sorted). Parent 1 is the mother when a gene is X-linked; parent 2 the father, an X and the Y. Changed
/// on a copy (<see cref="Copy"/>), as the Mac's struct is; its genes are never changed in place, only replaced.
/// </summary>
public sealed class CrossSetup : IEquatable<CrossSetup>
{
    public List<BioGene> Genes { get; }
    public List<int[]> Parent1 { get; }
    public List<int[]> Parent2 { get; }
    /// <summary>The example each gene was set from, if any (its menu ticks it).</summary>
    public List<string?> Examples { get; }

    public const int MaxGenes = 3;
    /// <summary>The letters a gene may take: not I (ABO's), X or Y (the X-linked notation's).</summary>
    public static readonly char[] Letters = "ABCDEFGHJKLMNOPQRSTUVWZ".ToCharArray();

    public CrossSetup(IEnumerable<BioGene> genes, IEnumerable<int[]> parent1, IEnumerable<int[]> parent2, IEnumerable<string?>? examples = null)
    {
        Genes = genes.ToList();
        Parent1 = parent1.Select(p => p.OrderBy(x => x).ToArray()).ToList();
        Parent2 = parent2.Select(p => p.OrderBy(x => x).ToArray()).ToList();
        Examples = examples?.ToList() ?? Enumerable.Repeat<string?>(null, Genes.Count).ToList();
    }

    public CrossSetup(BioCross c) : this(c.Genes, c.Parent1, c.Parent2) { }

    /// <summary>Aa × Aa: where the page starts.</summary>
    public static CrossSetup Standard => new(new[] { new BioGene('A') }, new[] { new[] { 0, 1 } }, new[] { new[] { 0, 1 } });

    public CrossSetup Copy() => new(Genes, Parent1.Select(p => p.ToArray()), Parent2.Select(p => p.ToArray()), Examples);

    public BioCross Cross => new(Genes.ToList(), Parent1.ToArray(), Parent2.ToArray());

    /// <summary>The X-linked gene, if there is one.</summary>
    public int? XIndex
    {
        get
        {
            var i = Genes.FindIndex(g => g.Pattern.IsXLinked());
            return i >= 0 ? i : null;
        }
    }

    /// <summary>The cross as it is written: "AaBb × aaBb", "X^A X^a × X^A Y".</summary>
    public string Text
    {
        get
        {
            var c = Cross;
            return c.GenotypeText(c.Parent1) + " × " + c.GenotypeText(c.Parent2);
        }
    }

    /// <summary>The genotypes a parent may have for a gene (what its menu lists): an X-linked gene's mother X X and father
    /// X Y; never a lethal homozygote (it dies before it could be a parent).</summary>
    public List<int[]> Genotypes(int k, int parent)
    {
        if (k < 0 || k >= Genes.Count) return new();
        var g = Genes[k];
        var outs = new List<int[]>();
        if (g.Pattern.IsXLinked())
            outs = parent == 1 ? new() { new[] { 0, 0 }, new[] { 0, 1 }, new[] { 1, 1 } } : new() { new[] { 0, 2 }, new[] { 1, 2 } };
        else
            for (var i = 0; i < g.Alleles.Count; i++)
                for (var j = i; j < g.Alleles.Count; j++) outs.Add(new[] { i, j });
        if (g.Pattern == BioInheritance.LethalDominantHomozygote) outs.RemoveAll(x => x.SequenceEqual(new[] { 0, 0 }));
        if (g.Pattern == BioInheritance.LethalRecessive) outs.RemoveAll(x => x.SequenceEqual(new[] { 1, 1 }));
        return outs;
    }

    /// <summary>Whether gene k may take a pattern: one X-linked gene at a time (two on one X would be linked), one ABO gene.</summary>
    public bool Allowed(BioInheritance p, int k)
    {
        var others = Genes.Where((_, i) => i != k).ToList();
        if (p.IsXLinked() && others.Any(g => g.Pattern.IsXLinked())) return false;
        if (p == BioInheritance.Abo && others.Any(g => g.Pattern == BioInheritance.Abo || g.Letter == 'I')) return false;
        return true;
    }

    /// <summary>The letters gene k may change to (none for ABO, whose gene is I).</summary>
    public List<char> FreeLetters(int k)
    {
        var used = Genes.Where((_, i) => i != k).Select(g => g.Letter).ToHashSet();
        return Letters.Where(l => !used.Contains(l)).ToList();
    }

    private char FreeLetter(int? except = null)
    {
        var used = Genes.Where((_, i) => i != except).Select(g => g.Letter).ToHashSet();
        return Letters.FirstOrDefault(l => !used.Contains(l), 'Z');
    }

    private static int Family(BioInheritance x) => x switch
    {
        BioInheritance.Complete or BioInheritance.LethalDominantHomozygote or BioInheritance.LethalRecessive => 0,
        BioInheritance.Incomplete or BioInheritance.Codominant => 1,
        BioInheritance.Abo => 2,
        _ => 3,
    };

    /// <summary>A gene's pattern changed: its alleles become the pattern's own, and each parent keeps what it can (dominant
    /// stays dominant, recessive recessive; an X-linked father keeps his X).</summary>
    public void SetPattern(BioInheritance p, int k)
    {
        if (k < 0 || k >= Genes.Count || Genes[k].Pattern == p || !Allowed(p, k)) return;
        var old = Genes[k];
        var letter = old.Letter;
        if (p == BioInheritance.Abo) letter = 'I';
        else if (old.Pattern == BioInheritance.Abo) letter = FreeLetter(k);
        // (within one way of writing alleles — A and a, or C^R and C^W — the gene keeps its own symbols and names: A^Y and
        //  a stay A^Y and a when the yellow allele is made lethal)
        var fresh = Family(old.Pattern) == Family(p) ? new BioGene(letter, p, old.Alleles, old.TraitNames) : new BioGene(letter, p);
        Parent1[k] = Remap(Parent1[k], old, fresh, 1);
        Parent2[k] = Remap(Parent2[k], old, fresh, 2);
        Genes[k] = fresh;
        Examples[k] = null;
        KeepAlive(k);
    }

    /// <summary>An allele's rank: 0 dominant, 1 recessive, 2 the Y.</summary>
    private static int Rank(int i, BioGene g)
    {
        if (g.Pattern.IsXLinked()) return i;
        if (g.Pattern == BioInheritance.Abo) return i == 2 ? 1 : 0;
        return Math.Min(i, 1);
    }

    private static int[] Remap(int[] genotype, BioGene old, BioGene fresh, int parent)
    {
        var ranks = genotype.Select(i => Rank(i, old)).ToList();
        var xs = ranks.Where(r => r < 2).ToList();
        if (fresh.Pattern.IsXLinked())
        {
            if (parent == 1)
            {
                var a = xs.Count > 0 ? xs[0] : 0;
                return new[] { a, xs.Count > 1 ? xs[1] : a }.OrderBy(x => x).ToArray();
            }
            return new[] { xs.Count > 0 ? xs.Min() : 0, 2 };
        }
        // (parent 1's dominant allele becomes I^A, parent 2's I^B: Aa × Aa becomes I^A i × I^B i)
        if (fresh.Pattern == BioInheritance.Abo) return ranks.Select(r => r == 0 ? (parent == 1 ? 0 : 1) : 2).OrderBy(x => x).ToArray();
        var first = xs.Count > 0 ? xs[0] : 0;
        return new[] { first, xs.Count > 1 ? xs[1] : first }.OrderBy(x => x).ToArray();
    }

    /// <summary>A lethal homozygote can't be a parent: it becomes the heterozygote.</summary>
    private void KeepAlive(int k)
    {
        List<int[]> ok1 = Genotypes(k, 1), ok2 = Genotypes(k, 2);
        static bool Has(List<int[]> l, int[] g) => l.Any(x => x.SequenceEqual(g));
        if (!Has(ok1, Parent1[k])) Parent1[k] = Has(ok1, new[] { 0, 1 }) ? new[] { 0, 1 } : (ok1.FirstOrDefault() ?? new[] { 0, 1 });
        if (!Has(ok2, Parent2[k])) Parent2[k] = Has(ok2, new[] { 0, 1 }) ? new[] { 0, 1 } : (ok2.FirstOrDefault() ?? new[] { 0, 1 });
    }

    /// <summary>A gene's letter changed: its alleles follow ("C^R" → "D^R", "X^a" → "X^d"; ABO keeps I).</summary>
    public void SetLetter(char l, int k)
    {
        if (k < 0 || k >= Genes.Count || Genes[k].Pattern == BioInheritance.Abo || !FreeLetters(k).Contains(l)) return;
        var old = Genes[k];
        var low = char.ToLowerInvariant(l);
        var alleles = old.Alleles.Select(a =>
        {
            if (old.Pattern.IsXLinked())
            {
                if (a == "Y") return a;
                var sup = BioGene.Superscript(a);
                return "X^" + (sup.Length > 0 && char.IsUpper(sup[0]) ? l : low);
            }
            if (a.Length == 0 || char.ToUpperInvariant(a[0]) != old.Letter) return a;
            return (char.IsUpper(a[0]) ? l : low) + a[1..];
        });
        Genes[k] = new BioGene(l, old.Pattern, alleles, old.TraitNames);
    }

    /// <summary>A gene more (up to three): the next free letter, complete dominance, both parents heterozygous.</summary>
    public void AddGene()
    {
        if (Genes.Count >= MaxGenes) return;
        Genes.Add(new BioGene(FreeLetter()));
        Parent1.Add(new[] { 0, 1 });
        Parent2.Add(new[] { 0, 1 });
        Examples.Add(null);
    }

    public void RemoveGene(int k)
    {
        if (Genes.Count <= 1 || k < 0 || k >= Genes.Count) return;
        Genes.RemoveAt(k);
        Parent1.RemoveAt(k);
        Parent2.RemoveAt(k);
        Examples.RemoveAt(k);
    }

    /// <summary>Gene k set from an example (a free letter if its own is taken); false if the pattern can't be had here.</summary>
    public bool ApplyExample(CrossExample e, int k)
    {
        if (k < 0 || k >= Genes.Count || !Allowed(e.Gene.Pattern, k)) return false;
        Genes[k] = new BioGene(e.Gene.Letter, e.Gene.Pattern, e.Gene.Alleles, e.Gene.TraitNames);
        Parent1[k] = e.Parent1.OrderBy(x => x).ToArray();
        Parent2[k] = e.Parent2.OrderBy(x => x).ToArray();
        Examples[k] = e.Id;
        if (Genes.Where((_, i) => i != k).Any(g => g.Letter == e.Gene.Letter))
        {
            SetLetter(FreeLetter(k), k);
            Examples[k] = e.Id;
        }
        return true;
    }

    /// <summary>The test cross: parent 2 made the homozygous recessive for every gene, the way to read an unknown genotype
    /// from its offspring. Returns a line for each gene that has no homozygous recessive to cross with.</summary>
    public List<string> TestCross()
    {
        var notes = new List<string>();
        for (var k = 0; k < Genes.Count; k++)
        {
            var g = Genes[k];
            switch (g.Pattern)
            {
                case BioInheritance.XRecessive or BioInheritance.XDominant: Parent2[k] = new[] { 1, 2 }; break;
                case BioInheritance.Abo: Parent2[k] = new[] { 2, 2 }; break;
                case BioInheritance.LethalRecessive:
                    notes.Add($"{g.Text(new[] { 1, 1 })} dies before birth, so gene {g.Letter} can't be test-crossed; it was left as it was.");
                    break;
                case BioInheritance.Incomplete or BioInheritance.Codominant:
                    Parent2[k] = new[] { 1, 1 };
                    notes.Add($"Gene {g.Letter} has no recessive allele; {g.Text(new[] { 1, 1 })} is used, and every genotype shows anyway.");
                    break;
                default: Parent2[k] = new[] { 1, 1 }; break;
            }
        }
        return notes;
    }

    /// <summary>A cross typed as text ("AaBb × aaBb", "X^A X^a × X^a Y", "I^A i × I^B i", "C^R C^W x C^R C^W"), patterns
    /// guessed by BioCross.Parse; with an X-linked gene the mother (X X) is put first.</summary>
    public static (CrossSetup? Setup, string? Error) Parse(string text)
    {
        var r = BioCross.Parse(text);
        if (r.Cross is not { } c) return (null, r.Error ?? "That cross could not be read.");
        if (c.Genes.Count > MaxGenes) return (null, "Up to three genes at a time.");
        var xs = Enumerable.Range(0, c.Genes.Count).Where(i => c.Genes[i].Pattern.IsXLinked()).ToList();
        if (xs.Count > 1) return (null, "One X-linked gene at a time: two genes on the X are linked, so they don't assort independently.");
        if (xs.Count == 1)
        {
            var x = xs[0];
            var y = c.Genes[x].Alleles.Count - 1;
            int ys1 = c.Parent1[x].Count(a => a == y), ys2 = c.Parent2[x].Count(a => a == y);
            if (ys1 > 1 || ys2 > 1) return (null, "Nobody has two Y chromosomes: write the father as X Y.");
            if (ys1 == ys2)
                return (null, ys1 == 0 ? "One parent must be the father, written X Y (like X^A Y)." : "Only one parent can be the father (X Y): the other is the mother, X X.");
            if (ys1 == 1) c = new BioCross(c.Genes, c.Parent2, c.Parent1);
        }
        // (a gene written with superscripts has two alleles: a third is ABO's alone; one seen in both parents, as in
        //  C^R C^R × C^R C^R, gets its partner, so the gene can still be changed and asked about)
        for (var k = 0; k < c.Genes.Count; k++)
        {
            var g = c.Genes[k];
            if (g.Pattern == BioInheritance.Abo || g.Pattern.IsXLinked()) continue;
            if (g.Alleles.Count > 2) return (null, $"Gene {g.Letter} has more than two alleles: only ABO (I^A, I^B, i) has three here.");
            if (g.Alleles.Count == 1)
            {
                var only = g.Alleles[0];
                var sup = BioGene.Superscript(only);
                var other = only.Contains('^') ? $"{g.Letter}^{(sup == "W" ? "R" : "W")}" : (only == only.ToUpperInvariant() ? only.ToLowerInvariant() : only.ToUpperInvariant());
                var genes = c.Genes.ToList();
                var lowFirst = only == only.ToLowerInvariant() && !only.Contains('^');
                genes[k] = new BioGene(g.Letter, g.Pattern, lowFirst ? new[] { other, only } : new[] { only, other }, g.TraitNames);
                // (the indices move if the new allele went first)
                var shift = genes[k].Alleles[0] == other ? 1 : 0;
                var kk = k;
                var p1 = c.Parent1.Select((p, i) => i == kk ? p.Select(a => a + shift).ToArray() : p).ToArray();
                var p2 = c.Parent2.Select((p, i) => i == kk ? p.Select(a => a + shift).ToArray() : p).ToArray();
                c = new BioCross(genes, p1, p2);
            }
        }
        return (new CrossSetup(c), null);
    }

    // ---- remembered between launches ----

    private sealed class Stored
    {
        [JsonPropertyName("genes")] public List<StoredGene> Genes { get; set; } = new();
        [JsonPropertyName("p1")] public List<int[]> P1 { get; set; } = new();
        [JsonPropertyName("p2")] public List<int[]> P2 { get; set; } = new();
        [JsonPropertyName("examples")] public List<string?> Examples { get; set; } = new();
    }

    private sealed class StoredGene
    {
        [JsonPropertyName("letter")] public string Letter { get; set; } = "";
        [JsonPropertyName("pattern")] public string Pattern { get; set; } = "";
        [JsonPropertyName("alleles")] public List<string> Alleles { get; set; } = new();
        // (left out when there are none, as the Mac's encoder leaves out a nil)
        [JsonPropertyName("traits"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Traits { get; set; }
    }

    /// <summary>The setup as text for Prefs (SimplBio.cross.setup).</summary>
    public string Encoded => JsonSerializer.Serialize(new Stored
    {
        Genes = Genes.Select(g => new StoredGene { Letter = g.Letter.ToString(), Pattern = g.Pattern.ToString(), Alleles = g.Alleles.ToList(), Traits = g.TraitNames?.ToList() }).ToList(),
        P1 = Parent1.ToList(), P2 = Parent2.ToList(), Examples = Examples.ToList(),
    });

    /// <summary>A remembered setup, if it still makes sense (null otherwise).</summary>
    public static CrossSetup? Decode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return null;
        Stored? s;
        try { s = JsonSerializer.Deserialize<Stored>(encoded); } catch (JsonException) { return null; }
        if (s == null || s.Genes.Count is < 1 or > MaxGenes || s.P1.Count != s.Genes.Count || s.P2.Count != s.Genes.Count) return null;
        var genes = new List<BioGene>();
        foreach (var g in s.Genes)
        {
            if (!Enum.TryParse<BioInheritance>(g.Pattern, out var p) || g.Letter.Length == 0 || g.Alleles.Count < 2) return null;
            genes.Add(new BioGene(g.Letter[0], p, g.Alleles, g.Traits));
        }
        if (s.P1.Concat(s.P2).Any(x => x == null || x.Length != 2)) return null;
        var setup = new CrossSetup(genes, s.P1, s.P2, s.Examples.Count == genes.Count ? s.Examples : null);
        for (var k = 0; k < genes.Count; k++)
            if (!setup.Genotypes(k, 1).Any(x => x.SequenceEqual(setup.Parent1[k])) || !setup.Genotypes(k, 2).Any(x => x.SequenceEqual(setup.Parent2[k]))) return null;
        if (genes.Count(g => g.Pattern.IsXLinked()) > 1) return null;
        return setup;
    }

    // ---- equality (the Mac's Equatable struct) ----

    public static bool SameGene(BioGene a, BioGene b) =>
        a.Letter == b.Letter && a.Pattern == b.Pattern && a.Alleles.SequenceEqual(b.Alleles)
        && (a.TraitNames == null ? b.TraitNames == null : b.TraitNames != null && a.TraitNames.SequenceEqual(b.TraitNames));

    public bool Equals(CrossSetup? o) =>
        o != null && Genes.Count == o.Genes.Count && Genes.Zip(o.Genes).All(z => SameGene(z.First, z.Second))
        && Parent1.Count == o.Parent1.Count && Parent1.Zip(o.Parent1).All(z => z.First.SequenceEqual(z.Second))
        && Parent2.Count == o.Parent2.Count && Parent2.Zip(o.Parent2).All(z => z.First.SequenceEqual(z.Second))
        && Examples.SequenceEqual(o.Examples);

    public override bool Equals(object? obj) => obj is CrossSetup s && Equals(s);
    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);
}

// ---- what the Cross page draws ----

/// <summary>A gamete as a parent's row or column of the square shows it: its alleles and its chance.</summary>
public sealed record CrossGamete(string Text, BioFrac P);

public static class CrossText
{
    /// <summary>A gamete's alleles: "AB", "A X^B", "I^A b".</summary>
    public static string GameteText(this BioCross c, int[] g)
    {
        var parts = c.Genes.Zip(g, (gene, i) => gene.Alleles[i]).ToList();
        return parts.All(p => p.Length == 1) ? string.Concat(parts) : string.Join(" ", parts);
    }

    /// <summary>For a reader: the superscripts' carets read out ("C R, C W"), and "_" as "blank".</summary>
    public static string Spoken(string s) => s.Replace("^", " ", StringComparison.Ordinal).Replace("_", " blank ", StringComparison.Ordinal);

    /// <summary>For words drawn plain (a tooltip, a menu header): each superscript as a modifier letter where Unicode has one.</summary>
    public static string Plain(string s)
    {
        var sup = new Dictionary<char, string>
        {
            ['A'] = "ᴬ", ['B'] = "ᴮ", ['D'] = "ᴰ", ['E'] = "ᴱ", ['G'] = "ᴳ", ['H'] = "ᴴ", ['I'] = "ᴵ", ['J'] = "ᴶ", ['K'] = "ᴷ", ['L'] = "ᴸ", ['M'] = "ᴹ", ['N'] = "ᴺ",
            ['O'] = "ᴼ", ['P'] = "ᴾ", ['R'] = "ᴿ", ['T'] = "ᵀ", ['U'] = "ᵁ", ['V'] = "ⱽ", ['W'] = "ᵂ", ['C'] = "ᶜ", ['F'] = "ᶠ", ['S'] = "ˢ", ['X'] = "ˣ", ['Y'] = "ʸ", ['Z'] = "ᶻ",
            ['a'] = "ᵃ", ['b'] = "ᵇ", ['c'] = "ᶜ", ['d'] = "ᵈ", ['e'] = "ᵉ", ['f'] = "ᶠ", ['g'] = "ᵍ", ['h'] = "ʰ", ['i'] = "ⁱ", ['j'] = "ʲ", ['k'] = "ᵏ", ['l'] = "ˡ", ['m'] = "ᵐ",
            ['n'] = "ⁿ", ['o'] = "ᵒ", ['p'] = "ᵖ", ['r'] = "ʳ", ['s'] = "ˢ", ['t'] = "ᵗ", ['u'] = "ᵘ", ['v'] = "ᵛ", ['w'] = "ʷ", ['x'] = "ˣ", ['y'] = "ʸ", ['z'] = "ᶻ",
        };
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '^' && i + 1 < s.Length && sup.TryGetValue(s[i + 1], out var m)) { sb.Append(m); i++; }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}

/// <summary>The Punnett square (§4.5): parent 1's gametes down the left, parent 2's across the top, each cell the
/// offspring's genotype in canonical order, with its phenotype's place in the phenotypic ratio (for its tint) — null for
/// a cell that dies before birth.</summary>
public sealed class CrossPunnett
{
    public sealed record Cell(int[][] Genotype, string Text, string Phenotype, int? Tint, bool Dies, bool Carrier, BioSex? Sex);

    public List<CrossGamete> Rows { get; }
    public List<CrossGamete> Columns { get; }
    public Cell[][] Cells { get; }

    public CrossPunnett(BioCross c)
    {
        var g1 = c.Gametes1;
        var g2 = c.Gametes2;
        Rows = g1.Select(g => new CrossGamete(c.GameteText(g.Gamete), g.P)).ToList();
        Columns = g2.Select(g => new CrossGamete(c.GameteText(g.Gamete), g.P)).ToList();
        var order = c.Result().Phenotypes.Select(p => p.Text).ToList();
        Cells = g1.Select(a => g2.Select(b =>
        {
            var g = c.Child(a.Gamete, b.Gamete);
            var ps = c.Phenotypes(g);
            var text = c.PhenotypeText(g);
            var dies = ps.Any(p => p.Dies);
            var i = order.IndexOf(text);
            return new Cell(g, c.GenotypeText(g), dies ? "dies" : text, dies || i < 0 ? (int?)null : i, dies, ps.Any(p => p.Carrier), c.Sex(g));
        }).ToArray()).ToArray();
    }

    public int CellCount => Rows.Count * Columns.Count;

    /// <summary>A genotype's key, to find every cell with it.</summary>
    public static string Key(int[][] g) => string.Join("|", g.Select(x => string.Join(",", x)));
}

/// <summary>The forked-line (branch) diagram: each gene's own outcomes from its own small cross, multiplied along every
/// path — the product rule drawn out, clearer than a 64-cell square for three genes. By phenotype (the born) or by genotype.</summary>
public sealed class CrossForkedLine
{
    public sealed record Branch(string Label, BioFrac P, bool Dies);

    /// <summary>A path's end: the branch taken at each gene, its label, its chance.</summary>
    public sealed record Leaf(int[] Path, string Label, BioFrac P, bool Dies);

    /// <summary>Each gene's outcomes, in order.</summary>
    public List<List<Branch>> Levels { get; } = new();
    public List<Leaf> Leaves { get; } = new();
    public bool ByGenotype { get; }

    public CrossForkedLine(BioCross c, bool genotypes)
    {
        ByGenotype = genotypes;
        for (var k = 0; k < c.Genes.Count; k++)
        {
            var one = new BioCross(new List<BioGene> { c.Genes[k] }, new[] { c.Parent1[k] }, new[] { c.Parent2[k] });
            if (genotypes) Levels.Add(one.Result().Genotypes.Select(g => new Branch(g.Text, g.P, g.Dies)).ToList());
            else if (c.Genes[k].Pattern.IsXLinked())
            {
                // (a son or a daughter half the time: each sex's phenotypes at half their share within it)
                var half = new BioFrac(1, 2);
                var d = one.Result(BioSex.Female).Phenotypes.Select(p => new Branch("♀ " + p.Text, p.P * half, false));
                var s = one.Result(BioSex.Male).Phenotypes.Select(p => new Branch("♂ " + p.Text, p.P * half, false));
                Levels.Add(d.Concat(s).ToList());
            }
            else Levels.Add(one.Result().Phenotypes.Select(p => new Branch(p.Text, p.P, false)).ToList());
        }
        void Walk(int k, int[] path)
        {
            if (k == Levels.Count)
            {
                var bs = path.Select((b, i) => Levels[i][b]).ToList();
                var labels = bs.Select(b => b.Label).ToList();
                var symbolic = labels.All(l => !l.Contains(' ') && !l.Contains('^'));
                Leaves.Add(new Leaf(path, symbolic ? string.Concat(labels) : string.Join(", ", labels), bs.Aggregate(BioFrac.One, (p, b) => p * b.P), bs.Any(b => b.Dies)));
                return;
            }
            for (var i = 0; i < Levels[k].Count; i++) Walk(k + 1, path.Append(i).ToArray());
        }
        if (Levels.Count > 0) Walk(0, Array.Empty<int>());
    }

    /// <summary>The leaves' share that is born (1 unless a gene is lethal, by genotype).</summary>
    public BioFrac Total => Leaves.Aggregate(BioFrac.Zero, (t, l) => t + l.P);
}

// ---- probability (§4.5 Probability) ----

/// <summary>What the Probability page can ask of one gene: a genotype, or a group of them (A_, a blood type, daughters,
/// affected), or anything. <see cref="Single"/>: one genotype (these exclude each other, so the sum rule can add them).</summary>
public sealed record CrossTarget(string Id, string Label, IReadOnlyList<int[]> Genotypes, bool Single)
{
    public bool IsAny => Id == "any";

    public bool Has(int[] g) => Genotypes.Any(x => x.SequenceEqual(g));
}

/// <summary>A chance worked out exactly where the fraction fits, else as a decimal.</summary>
public readonly record struct CrossChance(BioFrac? Exact, double Value)
{
    /// <summary>"37/64 (57.8%)", or "57.8%" when the fraction is too long to read.</summary>
    public string Text
    {
        get
        {
            var pct = CrossProbability.Percent(Value);
            if (Exact is { } f && f.Den <= 1_000_000) return f.Den == 1 ? $"{f.Num} ({pct})" : $"{f.Text} ({pct})";
            return pct;
        }
    }
}

public static class CrossProbability
{
    /// <summary>A gene's chips: its genotypes, then its groups, then any.</summary>
    public static List<CrossTarget> Targets(BioGene g)
    {
        var outs = new List<CrossTarget>();
        void Add(string id, string label, IEnumerable<int[]> set, bool single) => outs.Add(new CrossTarget(id, label, set.ToList(), single));
        switch (g.Pattern)
        {
            case BioInheritance.Abo:
                for (var i = 0; i < 3; i++)
                    for (var j = i; j < 3; j++) Add($"g{i}{j}", g.Text(new[] { i, j }), new[] { new[] { i, j } }, true);
                Add("typeA", "type A", new[] { new[] { 0, 0 }, new[] { 0, 2 } }, false);
                Add("typeB", "type B", new[] { new[] { 1, 1 }, new[] { 1, 2 } }, false);
                Add("typeAB", "type AB", new[] { new[] { 0, 1 } }, false);
                Add("typeO", "type O", new[] { new[] { 2, 2 } }, false);
                break;
            case BioInheritance.XRecessive or BioInheritance.XDominant:
            {
                var all = new[] { new[] { 0, 0 }, new[] { 0, 1 }, new[] { 1, 1 }, new[] { 0, 2 }, new[] { 1, 2 } };
                foreach (var gt in all) Add($"g{gt[0]}{gt[1]}", g.Text(gt), new[] { gt }, true);
                Add("daughter", "a daughter", new[] { new[] { 0, 0 }, new[] { 0, 1 }, new[] { 1, 1 } }, false);
                Add("son", "a son", new[] { new[] { 0, 2 }, new[] { 1, 2 } }, false);
                var recessive = g.Pattern == BioInheritance.XRecessive;
                var affectedLabel = g.Phenotype(recessive ? new[] { 1, 1 } : new[] { 0, 0 }).Label;
                var unaffectedLabel = g.Phenotype(recessive ? new[] { 0, 0 } : new[] { 1, 1 }).Label;
                var affected = all.Where(x => g.Phenotype(x).Label == affectedLabel).ToList();
                Add("affected", affectedLabel, affected, false);
                Add("unaffected", unaffectedLabel, all.Where(x => !affected.Contains(x)), false);
                break;
            }
            default:
                foreach (var gt in new[] { new[] { 0, 0 }, new[] { 0, 1 }, new[] { 1, 1 } }) Add($"g{gt[0]}{gt[1]}", g.Text(gt), new[] { gt }, true);
                if (g.Pattern.HasDominance()) Add("dominant", g.Alleles[0] + "_", new[] { new[] { 0, 0 }, new[] { 0, 1 } }, false);
                break;
        }
        outs.Add(new CrossTarget("any", "any", Array.Empty<int[]>(), false));
        return outs;
    }

    /// <summary>One gene's factor: its own small Punnett square's share for the target — among the born when the gene has a
    /// lethal genotype (the dead are never counted among the offspring).</summary>
    public static BioFrac Factor(BioCross c, int k, CrossTarget t)
    {
        if (k < 0 || k >= c.Genes.Count) return BioFrac.Zero;
        if (t.IsAny) return BioFrac.One;
        var g = c.Genes[k];
        var all = new List<int[]>();
        foreach (var a in BioCross.Gametes(new[] { c.Parent1[k] }))
            foreach (var b in BioCross.Gametes(new[] { c.Parent2[k] }))
            {
                var pair = new[] { Math.Min(a.Gamete[0], b.Gamete[0]), Math.Max(a.Gamete[0], b.Gamete[0]) };
                if (!all.Any(x => x.SequenceEqual(pair))) all.Add(pair);
            }
        var alive = all.Where(x => !g.Phenotype(x).Dies).ToArray();
        var pAlive = c.Probability(k, alive);
        if (pAlive.IsZero) return BioFrac.Zero;
        return c.Probability(k, t.Genotypes.Where(x => alive.Any(y => y.SequenceEqual(x))).ToArray()) / pAlive;
    }

    /// <summary>The product rule: one factor a gene, and their product.</summary>
    public static (List<BioFrac> Factors, BioFrac Product) Product(BioCross c, IReadOnlyList<CrossTarget> targets)
    {
        var fs = targets.Select((t, k) => (t, k)).Where(x => x.k < c.Genes.Count).Select(x => Factor(c, x.k, x.t)).ToList();
        return (fs, fs.Aggregate(BioFrac.One, (a, b) => a * b));
    }

    /// <summary>The sum rule for one gene: outcomes that exclude each other add.</summary>
    public static (List<BioFrac> Terms, BioFrac Total) Sum(BioCross c, int k, IReadOnlyList<CrossTarget> targets)
    {
        var ts = targets.Select(t => Factor(c, k, t)).ToList();
        return (ts, ts.Aggregate(BioFrac.Zero, (a, b) => a + b));
    }

    /// <summary>The offspring asked for, written as one genotype: "aaB_Cc", or "C^R C^W, any B".</summary>
    public static string TargetText(BioCross c, IReadOnlyList<CrossTarget> targets)
    {
        var indexed = targets.Select((t, k) => (t, k)).Where(x => x.k < c.Genes.Count).ToList();
        var named = indexed.Where(x => !x.t.IsAny).ToList();
        if (named.Count == 0) return "any offspring";
        var labels = named.Select(x => x.t.Label).ToList();
        var symbolic = labels.All(l => !l.Contains(' ') && !l.Contains('^'));
        var s = symbolic ? string.Concat(labels) : string.Join(", ", labels);
        var anys = indexed.Where(x => x.t.IsAny).Select(x => c.Genes[x.k].Letter.ToString()).ToList();
        if (anys.Count > 0) s += $" (any {string.Join(", ", anys)})";
        return s;
    }

    /// <summary>1 − (1 − p)ⁿ: at least one of n children.</summary>
    public static CrossChance AtLeastOne(BioFrac p, int n)
    {
        var none = Power(BioFrac.One - p, n);
        return new CrossChance(none is { } x ? BioFrac.One - x : null, 1 - Math.Pow(1 - p.Double, n));
    }

    /// <summary>C(n, k) pᵏ (1 − p)ⁿ⁻ᵏ: exactly k of n children.</summary>
    public static CrossChance Exactly(int k, int n, BioFrac p)
    {
        if (k < 0 || k > n) return new CrossChance(BioFrac.Zero, 0);
        var ways = Choose(n, k);
        BioFrac? exact = null;
        if (Power(p, k) is { } a && Power(BioFrac.One - p, n - k) is { } b && Multiply(a, b) is { } ab) exact = Multiply(ab, new BioFrac(ways, 1));
        var value = ways * Math.Pow(p.Double, k) * Math.Pow(1 - p.Double, n - k);
        return new CrossChance(exact, value);
    }

    public static long Choose(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        long r = 1;
        for (var i = 0; i < Math.Min(k, n - k); i++) r = r * (n - i) / (i + 1);
        return r;
    }

    /// <summary>fⁿ exactly, or null if it would not fit in 64 bits.</summary>
    public static BioFrac? Power(BioFrac f, int n)
    {
        long num = 1, den = 1;
        try
        {
            for (var i = 0; i < Math.Max(n, 0); i++)
            {
                num = checked(num * f.Num);
                den = checked(den * f.Den);
            }
        }
        catch (OverflowException) { return null; }
        return new BioFrac(num, den);
    }

    /// <summary>a × b exactly, or null if it would not fit in 64 bits.</summary>
    public static BioFrac? Multiply(BioFrac a, BioFrac b)
    {
        long g1 = Math.Max(BioFrac.Gcd(a.Num, b.Den), 1), g2 = Math.Max(BioFrac.Gcd(b.Num, a.Den), 1);
        try { return new BioFrac(checked(a.Num / g1 * (b.Num / g2)), checked(a.Den / g2 * (b.Den / g1))); }
        catch (OverflowException) { return null; }
    }

    /// <summary>"57.8%"; "&lt; 0.1%" for a sliver; "100%" only when it is.</summary>
    public static string Percent(double v)
    {
        if (v <= 0) return "0%";
        if (v >= 1) return "100%";
        if (v < 0.001) return "< 0.1%";
        if (v > 0.999) return "> 99.9%";
        return HWFormat.Fixed(v * 100, 1) + "%";
    }
}

// ---- chi-square (§4.5 Chi-square, §5.3.3) ----

/// <summary>A row of the chi-square page: a category's name, its part of the expected ratio and its observed count, as typed.</summary>
public sealed class ChiCategory
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("ratio")] public string Ratio { get; set; } = "";
    [JsonPropertyName("observed")] public string Observed { get; set; } = "";

    public ChiCategory() { }

    public ChiCategory(int id, string name, string ratio, string observed) { Id = id; Name = name; Ratio = ratio; Observed = observed; }

    public ChiCategory Copy() => new(Id, Name, Ratio, Observed);
}

/// <summary>The test worked out the way it is written on paper: the null hypothesis, expected = ratio × total, each
/// category's (o − e)²/e, χ², df = categories − 1, the critical value at p = 0.05 from the AP table, the p-value, and the
/// conclusion in AP's words (reject or fail to reject — never "accept", never "prove").</summary>
public sealed record ChiSquareWork(IReadOnlyList<string> Names, IReadOnlyList<double> Ratio, IReadOnlyList<double> Observed, double Total, BioChiSquare.Result Result)
{
    public double[] Expected => Result.Expected;

    /// <summary>"9:3:3:1".</summary>
    public string RatioText => string.Join(":", Ratio.Select(Number));

    public string NullHypothesis => NullHypothesisFor(RatioText);

    /// <summary>The null hypothesis for a ratio, written out (§4.5): what the test assumes before any count is in.</summary>
    public static string NullHypothesisFor(string ratioText) =>
        $"There is no statistically significant difference between the observed and expected counts; any difference is due to chance. The data fit a {ratioText} ratio.";

    /// <summary>The expected ratio as typed ("9:3:3:1"), if every part is a number above 0.</summary>
    public static string? RatioTextOf(IReadOnlyList<ChiCategory> rows)
    {
        var parts = rows.Select(r => BioText.Number(r.Ratio.Trim())).ToList();
        if (parts.Count < 2 || !parts.All(x => (x ?? 0) > 0)) return null;
        return string.Join(":", parts.Select(x => Number(x ?? 0)));
    }

    /// <summary>Some expected count is under 5, where the test is unreliable.</summary>
    public bool LowExpected => Expected.Any(e => e < 5);

    /// <summary>"p = 0.925", "p &lt; 0.001".</summary>
    public string PText => Result.P < 0.001 ? "p < 0.001" : "p = " + HWFormat.Fixed(Result.P, 3);

    /// <summary>What the p-value means, in a sentence.</summary>
    public string Meaning
    {
        get
        {
            var share = Result.P < 0.001 ? "less than 0.1% of the time" : (Result.P >= 0.995 ? "almost every time" : $"about {CrossProbability.Percent(Result.P)} of the time");
            return $"If the null hypothesis is true, chance alone gives a difference at least this large {share}.";
        }
    }

    /// <summary>The conclusion in AP's words, and what it means for the ratio.</summary>
    public string ConclusionDetail => Result.Reject
        ? $"The difference is too big to put down to chance at p = 0.05: the data do not fit a {RatioText} ratio."
        : $"The difference could be chance: the data are consistent with a {RatioText} ratio (this does not prove it).";

    /// <summary>A number as typed: 9 → "9", 0.5 → "0.5".</summary>
    public static string Number(double v) => v == Math.Round(v) && Math.Abs(v) < 1e12 ? ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture) : HWFormat.G(v);

    /// <summary>The work for the rows as typed, or what is wrong with them.</summary>
    public static (ChiSquareWork? Work, List<string> Problems) Make(IReadOnlyList<ChiCategory> rows)
    {
        var problems = new List<string>();
        if (rows.Count < 2) return (null, new List<string> { "A chi-square test needs at least two categories." });
        var ratio = new List<double>();
        var observed = new List<double>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var name = r.Name.Trim().Length == 0 ? $"Category {i + 1}" : r.Name;
            var e = BioText.Number(r.Ratio.Trim());
            if (e == null || e <= 0) problems.Add($"{name}: the expected ratio needs a number above 0.");
            var typed = r.Observed.Trim();
            var o = BioText.Number(typed);
            if (typed.Length == 0) problems.Add($"{name}: enter the observed count.");
            else if (o == null || o < 0 || o != Math.Round(o.Value)) problems.Add($"{name}: an observed count is a whole number, 0 or more.");
            ratio.Add(e ?? 0);
            observed.Add(o ?? 0);
        }
        var total = observed.Sum();
        if (problems.Count == 0 && total <= 0) problems.Add("The observed counts add up to 0.");
        if (problems.Count > 0) return (null, problems);
        var expected = BioChiSquare.Expected(ratio, total);
        var names = rows.Select((r, i) => r.Name.Trim().Length == 0 ? $"Category {i + 1}" : r.Name).ToList();
        return (new ChiSquareWork(names, ratio, observed, total, BioChiSquare.Test(observed, expected)), problems);
    }

    /// <summary>A cross's born phenotypes and their ratio as the rows (the observed counts left for the student, or kept from
    /// <paramref name="keeping"/> where a category of the same name had one).</summary>
    public static List<ChiCategory> Rows(BioCrossResult r, IReadOnlyList<ChiCategory>? keeping = null) =>
        r.Phenotypes.Zip(r.PhenotypicRatio).Select((z, i) =>
        {
            var kept = keeping?.FirstOrDefault(c => c.Name == z.First.Text)?.Observed ?? "";
            return new ChiCategory(i, z.First.Text, z.Second.ToString(System.Globalization.CultureInfo.InvariantCulture), kept);
        }).ToList();
}

// ---- what the tool keeps ----

/// <summary>
/// Genetics Crosses' state, kept across its views (the Mac's CrossesState): the Cross page's setup, what is typed and
/// what was wrong with it, the test cross's notes, the diagram; the Probability page's cross and chips, the sum rule's and
/// the children's; the Chi-square page's rows; the pedigree. Remembered between launches through <see cref="Persist"/>
/// (the view hands it the tool's Prefs: SimplBio.cross.*).
/// </summary>
public static class CrossesModel
{
    public enum Diagram { Square, Forked }

    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();

    private static Func<string, string?>? read;
    private static Action<string, string>? write;

    /// <summary>Where the tool's settings are kept (Prefs SimplBio.cross.&lt;field&gt;), read back the first time.</summary>
    public static void Persist(Func<string, string?> read, Action<string, string> write)
    {
        if (CrossesModel.read != null) return;
        CrossesModel.read = read;
        CrossesModel.write = write;
        Setup = CrossSetup.Decode(read("setup")) ?? CrossSetup.Standard;
        Typed = Setup.Text;
        ProbSetup = CrossSetup.Decode(read("probSetup")) ?? CrossSetup.Parse("AaBbCc × AaBbCc").Setup ?? CrossSetup.Standard;
        ProbTyped = ProbSetup.Text;
        ProbTargets = new();
        FitTargets();
        // (the product rule's example from §4.5: aaB_Cc)
        if (ProbSetup.Text == "AaBbCc × AaBbCc") ProbTargets = new() { "g11", "dominant", "g01" };
        List<ChiCategory>? rows = null;
        try { rows = read("chiRows") is { } j ? JsonSerializer.Deserialize<List<ChiCategory>>(j) : null; } catch (JsonException) { }
        if (rows is { Count: >= 2 })
        {
            ChiRows = rows;
            ChiSource = read("chiSource") ?? "Your own";
        }
        Pedigree = Pedigree.Decode(read("pedigree")) ?? Pedigree.Examples[0].Pedigree;
    }

    private static void Save(string field, string value) => write?.Invoke(field, value);

    // ---- Cross ----

    public static CrossSetup Setup { get; private set; } = CrossSetup.Standard;
    /// <summary>The typed cross, as the field holds it.</summary>
    public static string Typed { get; set; } = "Aa × Aa";
    public static string? TypedError { get; private set; }
    /// <summary>What the test cross could not do.</summary>
    public static List<string> Notes { get; private set; } = new();
    /// <summary>The square or the forked line; null until chosen (the forked line for three genes, else the square).</summary>
    public static Diagram? ChosenDiagram { get; private set; }
    public static bool ForkedByGenotype { get; private set; }
    public static bool WhyShown { get; private set; }

    public static Diagram ShownDiagram => ChosenDiagram ?? (Setup.Genes.Count >= 3 ? Diagram.Forked : Diagram.Square);

    public static void SetSetup(CrossSetup s)
    {
        if (s.Equals(Setup)) return;
        var old = Setup;
        Setup = s;
        Typed = s.Text;
        TypedError = null;
        Save("setup", s.Encoded);
        // (the chi-square categories follow the cross they were filled from, keeping counts already typed)
        if (ChiSource == old.Text)
        {
            ChiRows = ChiSquareWork.Rows(s.Cross.Result(), ChiRows);
            ChiSource = s.Text;
            ChiShape++;
            SaveChi();
        }
        Tell();
    }

    /// <summary>The setup changed by <paramref name="edit"/> on a copy.</summary>
    public static void Edit(Action<CrossSetup> edit)
    {
        var s = Setup.Copy();
        edit(s);
        SetSetup(s);
    }

    /// <summary>The typed cross put in place (if it changed), or what is wrong with it said.</summary>
    public static void ApplyTyped()
    {
        var t = Typed.Trim();
        if (t == Setup.Text) { TypedError = null; Tell(); return; }
        var r = CrossSetup.Parse(t);
        if (r.Setup is { } s)
        {
            Notes = new();
            SetSetup(s);
        }
        else
        {
            TypedError = r.Error;
            Tell();
        }
    }

    public static void TestCross()
    {
        var s = Setup.Copy();
        Notes = s.TestCross();
        SetSetup(s);
        Tell();
    }

    public static void SetDiagram(Diagram d) { ChosenDiagram = d; Tell(); }
    public static void SetForkedByGenotype(bool on) { ForkedByGenotype = on; Tell(); }
    public static void SetWhyShown(bool on) { WhyShown = on; Tell(); }

    // ---- Probability ----

    public static CrossSetup ProbSetup { get; private set; } = CrossSetup.Parse("AaBbCc × AaBbCc").Setup ?? CrossSetup.Standard;
    public static string ProbTyped { get; set; } = "AaBbCc × AaBbCc";
    public static string? ProbError { get; private set; }
    /// <summary>The chip chosen for each gene (CrossTarget ids).</summary>
    public static List<string> ProbTargets { get; private set; } = new() { "g11", "dominant", "g01" };
    public static int SumGene { get; private set; }
    public static HashSet<string> SumTargets { get; private set; } = new() { "g00", "g11" };
    public static int Children { get; private set; } = 3;
    public static int ExactlyK { get; private set; } = 1;

    public static void SetProbSetup(CrossSetup s)
    {
        if (s.Equals(ProbSetup)) return;
        ProbSetup = s;
        ProbTyped = s.Text;
        ProbError = null;
        FitTargets();
        Save("probSetup", s.Encoded);
        Tell();
    }

    /// <summary>Each gene's chip kept if it still exists, else "any".</summary>
    private static void FitTargets()
    {
        var outs = new List<string>();
        for (var k = 0; k < ProbSetup.Genes.Count; k++)
        {
            var ids = CrossProbability.Targets(ProbSetup.Genes[k]).Select(t => t.Id).ToList();
            var old = k < ProbTargets.Count ? ProbTargets[k] : (ids.Contains("g11") ? "g11" : "any");
            outs.Add(ids.Contains(old) ? old : "any");
        }
        ProbTargets = outs;
        if (SumGene >= ProbSetup.Genes.Count) SumGene = 0;
    }

    public static void ApplyProbTyped()
    {
        var t = ProbTyped.Trim();
        if (t == ProbSetup.Text) { ProbError = null; Tell(); return; }
        var r = CrossSetup.Parse(t);
        if (r.Setup is { } s) SetProbSetup(s);
        else
        {
            ProbError = r.Error;
            Tell();
        }
    }

    public static void SetProbTarget(int k, string id)
    {
        if (k < 0 || k >= ProbTargets.Count) return;
        ProbTargets[k] = id;
        Tell();
    }

    public static void SetSumGene(int k) { SumGene = Math.Clamp(k, 0, Math.Max(ProbSetup.Genes.Count - 1, 0)); Tell(); }

    public static void ToggleSumTarget(string id)
    {
        if (!SumTargets.Remove(id)) SumTargets.Add(id);
        Tell();
    }

    public static void SetChildren(int n) { Children = Math.Clamp(n, 1, 20); Tell(); }
    public static void SetExactlyK(int k) { ExactlyK = Math.Clamp(k, 0, Children); Tell(); }

    /// <summary>The chips chosen, as targets.</summary>
    public static List<CrossTarget> ChosenTargets => ProbSetup.Genes.Select((g, k) =>
    {
        var all = CrossProbability.Targets(g);
        var id = k < ProbTargets.Count ? ProbTargets[k] : "any";
        return all.FirstOrDefault(t => t.Id == id) ?? all[^1];
    }).ToList();

    // ---- Chi-square ----

    public static readonly ChiCategory[] MendelSeeds = { new(0, "round", "3", "5474"), new(1, "wrinkled", "1", "1850") };

    public static readonly ChiCategory[] MendelDihybrid =
    {
        new(0, "round yellow", "9", "315"), new(1, "round green", "3", "108"), new(2, "wrinkled yellow", "3", "101"), new(3, "wrinkled green", "1", "32"),
    };

    public static List<ChiCategory> ChiRows { get; private set; } = MendelSeeds.Select(r => r.Copy()).ToList();
    /// <summary>Where the rows came from ("Mendel's seed shapes", "AaBb × AaBb").</summary>
    public static string ChiSource { get; private set; } = "Mendel's seed shapes";
    /// <summary>Counts the times the rows were replaced, added to or taken from (the fields are made again; a row typed in
    /// keeps its field).</summary>
    public static int ChiShape { get; private set; }

    public static void SetChiRows(IEnumerable<ChiCategory> rows, string source)
    {
        ChiRows = rows.Select(r => r.Copy()).ToList();
        ChiSource = source;
        ChiShape++;
        SaveChi();
        Tell();
    }

    public static void FillChiFromCross() => SetChiRows(ChiSquareWork.Rows(Setup.Cross.Result()), Setup.Text);

    public static void AddChiRow()
    {
        var id = ChiRows.Count == 0 ? 0 : ChiRows.Max(r => r.Id) + 1;
        ChiRows.Add(new ChiCategory(id, "", "1", ""));
        ChiSource = "Your own";
        ChiShape++;
        SaveChi();
        Tell();
    }

    public static void RemoveChiRow(int id)
    {
        if (ChiRows.Count <= 2) return;
        ChiRows.RemoveAll(r => r.Id == id);
        ChiSource = "Your own";
        ChiShape++;
        SaveChi();
        Tell();
    }

    /// <summary>A row's field typed in.</summary>
    public static void EditChiRow(int id, string? name = null, string? ratio = null, string? observed = null)
    {
        if (ChiRows.FirstOrDefault(r => r.Id == id) is not { } row) return;
        if (name != null) row.Name = name;
        if (ratio != null) row.Ratio = ratio;
        if (observed != null) row.Observed = observed;
        SaveChi();
        Tell();
    }

    private static void SaveChi()
    {
        Save("chiRows", JsonSerializer.Serialize(ChiRows));
        Save("chiSource", ChiSource);
    }

    // ---- Pedigree ----

    public static Pedigree Pedigree { get; private set; } = Pedigree.Examples[0].Pedigree;
    public static int? Selected { get; private set; }
    /// <summary>The mode whose ruling-out people are ringed.</summary>
    public static PedigreeMode? ShownMode { get; private set; }

    public static void SetPedigree(Pedigree p)
    {
        if (p.Equals(Pedigree)) { Tell(); return; }
        Pedigree = p;
        if (Selected is { } s && p.Person(s) == null) Selected = null;
        Save("pedigree", p.Encoded);
        Tell();
    }

    /// <summary>The pedigree changed by <paramref name="edit"/> on a copy.</summary>
    public static void EditPedigree(Action<Pedigree> edit)
    {
        var p = Pedigree.Copy();
        edit(p);
        SetPedigree(p);
    }

    public static void Select(int? id) { Selected = id; Tell(); }
    public static void ShowMode(PedigreeMode? m) { ShownMode = m; Tell(); }

    // ---- a launch's state, a hand-off ----

    private static bool launched;

    /// <summary>What a launch asked (--tool cross --tool-state …, --bio-cross "AaBb x AaBb"), once: <paramref name="value"/>
    /// reads a "name=value" part, <paramref name="has"/> a bare one.</summary>
    public static void ApplyLaunch(Func<string, string?> value, Func<string, bool> has)
    {
        if (launched) return;
        launched = true;
        if (value("cross") is { } c)
        {
            Typed = c;
            ApplyTyped();
        }
        if (value("view") is { } v) ChosenDiagram = v == "forked" ? Diagram.Forked : v == "square" ? Diagram.Square : ChosenDiagram;
        if (has("forked")) ChosenDiagram = Diagram.Forked;
        if (has("square")) ChosenDiagram = Diagram.Square;
        if (has("genotypes")) ForkedByGenotype = true;
        if (has("testcross")) TestCross();
        if (has("why")) WhyShown = true;
        if (value("prob") is { } p)
        {
            ProbTyped = p;
            ApplyProbTyped();
        }
        if (value("target") is { } t)
        {
            var parts = t.Split(',').Select(x => x.Trim()).ToList();
            for (var k = 0; k < ProbSetup.Genes.Count && k < parts.Count; k++)
                if (CrossProbability.Targets(ProbSetup.Genes[k]).FirstOrDefault(x => x.Label == parts[k] || x.Id == parts[k]) is { } hit && k < ProbTargets.Count)
                    ProbTargets[k] = hit.Id;
        }
        if (BioText.Int(value("children") ?? "") is { } n) Children = Math.Clamp(n, 1, 20);
        if (has("fromcross")) FillChiFromCross();
        if (value("obs") is { } o)
        {
            var counts = o.Split(',').Select(x => x.Trim()).ToList();
            var rows = ChiRows.Select(r => r.Copy()).ToList();
            var source = ChiSource;
            if (counts.Count == 4 && rows.Count != 4) { rows = MendelDihybrid.Select(r => r.Copy()).ToList(); source = "Mendel's dihybrid cross"; }
            for (var i = 0; i < rows.Count && i < counts.Count; i++) rows[i].Observed = counts[i];
            SetChiRows(rows, source);
        }
        if (Pedigree.Examples.FirstOrDefault(e => e.Id == value("example")) is { } ex)
        {
            Pedigree = ex.Pedigree;
            Selected = null;
        }
        if (value("mode")?.ToLowerInvariant() is { } m)
            ShownMode = PedigreeModes.All.Cast<PedigreeMode?>().FirstOrDefault(x => x!.Value.Key().ToLowerInvariant() == m || x!.Value.Short().ToLowerInvariant() == m);
        if (BioText.Int(value("select") ?? "") is { } sel && Pedigree.Person(sel) != null) Selected = sel;
        Tell();
    }

    /// <summary>What another tool or the pin handed over: a cross (["cross"] = "Aa × Aa"), or chi-square rows
    /// (["chi"] = "round:3:5474;wrinkled:1:1850"); the page they belong on, if any.</summary>
    public static string? TakeHandoff(Func<string, string?> take)
    {
        string? page = null;
        if (take("cross") is { } c)
        {
            Typed = c;
            ApplyTyped();
            page = "cross";
        }
        if (take("chi") is { } chi)
        {
            var rows = chi.Split(';', StringSplitOptions.RemoveEmptyEntries).Select((item, i) =>
            {
                var f = item.Split(':');
                return f.Length >= 2 ? new ChiCategory(i, f[0], f[1], f.Length > 2 ? f[2] : "") : null;
            }).Where(r => r != null).Select(r => r!).ToList();
            if (rows.Count >= 2)
            {
                SetChiRows(rows, "Handed over");
                page = "chi";
            }
        }
        return page;
    }
}

public static partial class BioSelfCheck
{
    /// <summary>Genetics Crosses' own bio-check lines (the Mac's crossChecks, line for line): §5.3.2 through the Cross page's
    /// own path (typed, picked, the examples and the test cross), the square, the forked line, the product and sum rules,
    /// the chi-square page and the pedigree.</summary>
    static partial void CrossChecks(BioCheckReport r)
    {
        static CrossSetup? Setup(string text) => CrossSetup.Parse(text).Setup;
        static string Join(IEnumerable<int> xs, string sep = ":") => string.Join(sep, xs);

        // (§5.3.2, every row, through the page's typed cross)
        var rows = new (string Text, string Geno, string Pheno, string Ratio)[]
        {
            ("Aa × Aa", "1 AA : 2 Aa : 1 aa", "3 A_ : 1 aa", "3:1"),
            ("Aa × aa", "1 Aa : 1 aa", "1 A_ : 1 aa", "1:1"),
            ("AA × aa", "1 Aa", "1 A_", "1"),
            ("C^R C^W × C^R C^W", "1 C^R C^R : 2 C^R C^W : 1 C^W C^W", "1 red : 2 pink : 1 white", "1:2:1"),
            ("AaBb × AaBb", "1 AABB : 2 AABb : 1 AAbb : 2 AaBB : 4 AaBb : 2 Aabb : 1 aaBB : 2 aaBb : 1 aabb", "9 A_B_ : 3 A_bb : 3 aaB_ : 1 aabb", "9:3:3:1"),
            ("AaBb × aabb", "1 AaBb : 1 Aabb : 1 aaBb : 1 aabb", "1 A_B_ : 1 A_bb : 1 aaB_ : 1 aabb", "1:1:1:1"),
            ("I^A i × I^B i", "1 I^A I^B : 1 I^A i : 1 I^B i : 1 ii", "1 AB : 1 A : 1 B : 1 O", "1:1:1:1"),
            ("I^A I^B × I^A I^B", "1 I^A I^A : 2 I^A I^B : 1 I^B I^B", "1 A : 2 AB : 1 B", "1:2:1"),
        };
        foreach (var (text, geno, pheno, ratio) in rows)
        {
            var name = "cross.page." + text.Replace(" ", "", StringComparison.Ordinal);
            if (Setup(text) is not { } s) { r.Check(name, "unparsed", false); continue; }
            var res = s.Cross.Result();
            r.Check(name, Join(res.PhenotypicRatio), res.GenotypicRatioText == geno && res.PhenotypicRatioText == pheno && Join(res.PhenotypicRatio) == ratio && s.Text == text);
        }
        if (Setup("AaBbCc × AaBbCc") is { } tri)
        {
            var res = tri.Cross.Result();
            r.Check("cross.page.AaBbCc", $"{res.Genotypes.Count}|" + Join(res.PhenotypicRatio),
                res.Genotypes.Count == 27 && res.PhenotypicRatio.SequenceEqual(new[] { 27, 9, 9, 9, 3, 3, 3, 1 }) && res.Phenotypes.Count == 8);
        }
        else r.Check("cross.page.AaBbCc", "unparsed", false);
        // (X-linked, the mother first even when typed second; daughters and sons apart, and the carriers among them)
        if (Setup("X^a Y × X^A X^a") is { } xa)
        {
            BioCrossResult d = xa.Cross.Result(BioSex.Female), m = xa.Cross.Result(BioSex.Male);
            r.Check("cross.page.X-carrier×affected", xa.Text + "|" + d.GenotypicRatioText + "|" + m.GenotypicRatioText,
                xa.Text == "X^A X^a × X^a Y" && d.GenotypicRatioText == "1 X^A X^a : 1 X^a X^a" && d.PhenotypicRatioText == "1 unaffected : 1 affected"
                && d.Phenotypes.FirstOrDefault()?.Carriers == new BioFrac(1, 2) && m.GenotypicRatioText == "1 X^A Y : 1 X^a Y" && m.PhenotypicRatio.SequenceEqual(new[] { 1, 1 }));
        }
        else r.Check("cross.page.X-carrier×affected", "unparsed", false);
        if (Setup("X^A X^a × X^A Y") is { } xn)
        {
            BioCrossResult d = xn.Cross.Result(BioSex.Female), m = xn.Cross.Result(BioSex.Male);
            var sonsAffected = m.Phenotypes.FirstOrDefault(p => p.Text == "affected")?.P;
            r.Check("cross.page.X-carrier×normal", d.PhenotypicRatioText + "|carriers" + (d.Phenotypes.FirstOrDefault()?.Carriers.Text ?? "-") + "|sons" + (sonsAffected?.Text ?? "-"),
                d.GenotypicRatioText == "1 X^A X^A : 1 X^A X^a" && d.Phenotypes.Count == 1 && d.Phenotypes[0].Text == "unaffected"
                && d.Phenotypes[0].Carriers == new BioFrac(1, 2) && m.GenotypicRatioText == "1 X^A Y : 1 X^a Y" && sonsAffected == new BioFrac(1, 2));
        }
        else r.Check("cross.page.X-carrier×normal", "unparsed", false);
        // (the lethal row, by switching the typed gene's pattern as the page's menu does)
        if (Setup("A^Y a × A^Y a") is { } y)
        {
            y.SetPattern(BioInheritance.LethalDominantHomozygote, 0);
            var res = y.Cross.Result();
            r.Check("cross.page.lethal", res.GenotypicRatioText + "|born" + BioRatio.Text(res.BornGenotypicRatio) + "|" + res.PhenotypicRatioText,
                res.GenotypicRatioText == "1 A^Y A^Y : 2 A^Y a : 1 aa" && res.Genotypes.FirstOrDefault()?.Dies == true && res.BornGenotypicRatio.SequenceEqual(new[] { 2, 1 })
                && res.PhenotypicRatio.SequenceEqual(new[] { 2, 1 }) && y.Parent1[0].SequenceEqual(new[] { 0, 1 }));
        }
        else r.Check("cross.page.lethal", "unparsed", false);
        // (the examples give §5.3.1's phenotypes)
        static BioCrossResult? Example(string id)
        {
            if (CrossExamples.Find(id) is not { } e) return null;
            var s = CrossSetup.Standard;
            return s.ApplyExample(e, 0) ? s.Cross.Result() : null;
        }
        r.Check("cross.examples", $"{CrossExamples.All.Count}",
            Example("snapdragon")?.PhenotypicRatioText == "1 red : 2 pink : 1 white"
            && Example("mn")?.PhenotypicRatioText == "1 M : 2 MN : 1 N"
            && Example("roan")?.PhenotypicRatioText == "1 red : 2 roan : 1 white"
            && Example("abo")?.PhenotypicRatioText == "1 AB : 1 A : 1 B : 1 O"
            && Example("yellow")?.PhenotypicRatioText == "2 yellow : 1 non-yellow"
            && Example("lethalRecessive")?.BornGenotypicRatio.SequenceEqual(new[] { 1, 2 }) == true
            && Example("pea")?.PhenotypicRatioText == "3 round : 1 wrinkled"
            && Example("rickets")?.Phenotypes.FirstOrDefault()?.Text == "rickets");
        // (the pickers: a gene added, its pattern changed and its letter; only valid genotypes offered)
        var picked = CrossSetup.Standard;
        picked.AddGene();
        picked.SetPattern(BioInheritance.XRecessive, 1);
        var xMenus = picked.Genotypes(1, 1).Select(g => picked.Genes[1].Text(g)).Concat(picked.Genotypes(1, 2).Select(g => picked.Genes[1].Text(g))).ToList();
        r.Check("cross.pickers", picked.Text + "|" + string.Join(",", xMenus),
            picked.Text == "Aa X^B X^b × Aa X^B Y" && xMenus.SequenceEqual(new[] { "X^B X^B", "X^B X^b", "X^b X^b", "X^B Y", "X^b Y" })
            && !picked.Allowed(BioInheritance.XDominant, 0) && picked.Allowed(BioInheritance.Abo, 0) && CrossSetup.Standard.Genotypes(0, 1).Count == 3);
        var lethal = CrossSetup.Standard;
        lethal.SetPattern(BioInheritance.LethalDominantHomozygote, 0);
        var abo = CrossSetup.Standard;
        abo.SetPattern(BioInheritance.Abo, 0);
        var lettered = CrossSetup.Standard;
        if (CrossExamples.Find("snapdragon") is { } snap) lettered.ApplyExample(snap, 0);
        lettered.SetLetter('D', 0);
        r.Check("cross.pickers.patterns", abo.Text + "|" + lettered.Text,
            lethal.Genotypes(0, 1).Count == 2 && abo.Text == "I^A i × I^B i" && lettered.Text == "D^R D^W × D^R D^W"
            && lettered.Cross.Result().PhenotypicRatioText == "1 red : 2 pink : 1 white");
        // (the test cross: AaBb × AaBb becomes AaBb × aabb, 1 : 1 : 1 : 1)
        if (Setup("AaBb × AaBb") is { } tc)
        {
            tc.TestCross();
            r.Check("cross.test-cross", tc.Text + "|" + BioRatio.Text(tc.Cross.Result().PhenotypicRatio),
                tc.Text == "AaBb × aabb" && tc.Cross.Result().PhenotypicRatio.SequenceEqual(new[] { 1, 1, 1, 1 }));
        }
        else r.Check("cross.test-cross", "unparsed", false);
        // (typed mistakes are said, not guessed)
        var twoX = CrossSetup.Parse("X^A X^a X^B X^b × X^A Y X^B Y");
        var four = CrossSetup.Parse("AaBbCcDd × AaBbCcDd");
        var noFather = CrossSetup.Parse("X^A X^a × X^A X^a");
        r.Check("cross.parse-errors", "said", twoX.Setup == null && twoX.Error != null && four.Setup == null && noFather.Setup == null && noFather.Error != null
            && CrossSetup.Parse("C^R C^W × C^B C^B").Setup == null);
        // (a superscript allele seen alone gets its partner, so the gene can still be changed and asked about)
        var lone = CrossSetup.Parse("C^R C^R × C^R C^R").Setup;
        r.Check("cross.parse-lone-allele", lone == null ? "-" : string.Join(",", lone.Genes[0].Alleles),
            lone != null && lone.Genes[0].Alleles.SequenceEqual(new[] { "C^R", "C^W" }) && lone.Cross.Result().GenotypicRatioText == "1 C^R C^R"
            && CrossProbability.Targets(lone.Genes[0]).Count == 4);
        // (remembered and read back)
        r.Check("cross.remembered", $"{picked.Encoded.Length}chars", picked.Equals(CrossSetup.Decode(picked.Encoded)) && CrossSetup.Decode("{}") == null);

        // (the square: 4 × 4 for AaBb × AaBb, each cell in canonical order and tinted by its phenotype's place)
        if (Setup("AaBb × AaBb") is { } di)
        {
            var sq = new CrossPunnett(di.Cross);
            var tints = sq.Cells.SelectMany(row => row.Where(c => c.Tint != null).Select(c => c.Tint!.Value)).ToHashSet();
            r.Check("cross.square", $"{sq.Rows.Count}x{sq.Columns.Count}," + string.Join(",", sq.Rows.Select(x => x.Text)),
                sq.CellCount == 16 && sq.Rows.Select(x => x.Text).SequenceEqual(new[] { "AB", "Ab", "aB", "ab" }) && sq.Cells[0][0].Text == "AABB" && sq.Cells[3][3].Text == "aabb"
                && sq.Cells[0][3].Text == "AaBb" && sq.Cells[3][3].Tint == 3 && tints.SetEquals(new[] { 0, 1, 2, 3 }) && sq.Rows.All(x => x.P == new BioFrac(1, 4)));
        }
        if (Setup("AaBbCc × AaBbCc") is { } s3 && Setup("X^A X^a × X^a Y") is { } x3 && Setup("A^Y a × A^Y a") is { } y3)
        {
            y3.SetPattern(BioInheritance.LethalDominantHomozygote, 0);
            CrossPunnett sq = new(s3.Cross), xs = new(x3.Cross), ys = new(y3.Cross);
            r.Check("cross.square.kinds", $"{sq.CellCount}," + string.Join(",", xs.Cells.SelectMany(row => row.Select(c => c.Text))),
                sq.CellCount == 64 && xs.Columns.Select(c => c.Text).SequenceEqual(new[] { "X^a", "Y" }) && xs.Cells[0][1].Text == "X^A Y" && xs.Cells[0][1].Sex == BioSex.Male
                && xs.Cells[0][0].Carrier && ys.Cells[0][0].Dies && ys.Cells[0][0].Tint == null);
        }
        // (the forked line: AaBbCc × AaBbCc's eight phenotypes, 27/64 … 1/64, adding to 1; by genotype, 27 branches)
        if (Setup("AaBbCc × AaBbCc") is { } f3)
        {
            CrossForkedLine f = new(f3.Cross, false), g = new(f3.Cross, true);
            r.Check("cross.forked-line", string.Join(",", f.Leaves.Select(l => l.P.Num)),
                f.Leaves.Select(l => l.P).SequenceEqual(new[] { 27, 9, 9, 3, 9, 3, 3, 1 }.Select(n => new BioFrac(n, 64))) && f.Leaves[0].Label == "A_B_C_" && f.Leaves[^1].Label == "aabbcc"
                && f.Total == BioFrac.One && g.Leaves.Count == 27 && g.Total == BioFrac.One && g.Leaves[13].Label == "AaBbCc" && g.Leaves[13].P == new BioFrac(8, 64));
        }
        if (Setup("X^A X^a × X^A Y") is { } fx)
        {
            var f = new CrossForkedLine(fx.Cross, false);
            r.Check("cross.forked-line.X", string.Join(",", f.Leaves.Select(l => l.Label + "=" + l.P.Text)),
                f.Leaves.Select(l => l.Label).SequenceEqual(new[] { "♀ unaffected", "♂ unaffected", "♂ affected" })
                && f.Leaves.Select(l => l.P).SequenceEqual(new[] { new BioFrac(1, 2), new BioFrac(1, 4), new BioFrac(1, 4) }));
        }

        // (probability: §5.3.2's last row by the chips, the sum rule, and over several children)
        if (Setup("AaBbCc × AaBbCc") is { } pc)
        {
            var c = pc.Cross;
            CrossTarget? Chip(int k, string label) => CrossProbability.Targets(c.Genes[k]).FirstOrDefault(t => t.Label == label);
            if (Chip(0, "aa") is { } a && Chip(1, "B_") is { } b && Chip(2, "Cc") is { } cc)
            {
                var p = CrossProbability.Product(c, new[] { a, b, cc });
                var asked = CrossProbability.TargetText(c, new[] { a, b, cc });
                r.Check("cross.probability", asked + "=" + string.Join("×", p.Factors.Select(x => x.Text)) + "=" + p.Product.Text,
                    p.Product == new BioFrac(3, 32) && p.Factors.SequenceEqual(new[] { new BioFrac(1, 4), new BioFrac(3, 4), new BioFrac(1, 2) })
                    && asked == "aaB_Cc" && p.Product.PercentText == "9.4%");
            }
            else r.Check("cross.probability", "no-chips", false);
            if (Chip(0, "AA") is { } aa && Chip(0, "aa") is { } rec)
            {
                var sum = CrossProbability.Sum(c, 0, new[] { aa, rec });
                r.Check("cross.sum-rule", string.Join("+", sum.Terms.Select(x => x.Text)) + "=" + sum.Total.Text, sum.Total == new BioFrac(1, 2));
            }
        }
        CrossChance one = CrossProbability.AtLeastOne(new BioFrac(1, 4), 3), two = CrossProbability.Exactly(2, 3, new BioFrac(1, 4));
        var big = CrossProbability.AtLeastOne(new BioFrac(3, 128), 20);
        r.Check("cross.children", (one.Exact?.Text ?? "-") + "," + (two.Exact?.Text ?? "-") + "," + big.Text,
            one.Exact == new BioFrac(37, 64) && two.Exact == new BioFrac(9, 64) && Math.Abs(big.Value - (1 - Math.Pow(125.0 / 128, 20))) < 1e-12 && big.Exact == null);
        if (Setup("A^Y a × A^Y a") is { } yb)
        {
            yb.SetPattern(BioInheritance.LethalDominantHomozygote, 0);
            var het = CrossProbability.Targets(yb.Genes[0]).FirstOrDefault(t => t.Id == "g01");
            var f = het == null ? (BioFrac?)null : CrossProbability.Factor(yb.Cross, 0, het);
            r.Check("cross.probability.born", f?.Text ?? "-", f == new BioFrac(2, 3));
        }

        // (chi-square from the cross: Mendel's 315 : 108 : 101 : 32 against 9 : 3 : 3 : 1)
        if (Setup("AaBb × AaBb") is { } chiCross)
        {
            var chiRows = ChiSquareWork.Rows(chiCross.Cross.Result());
            var obs = new[] { 315, 108, 101, 32 };
            for (var i = 0; i < chiRows.Count && i < obs.Length; i++) chiRows[i].Observed = obs[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            var w = ChiSquareWork.Make(chiRows).Work;
            r.Check("cross.chi-square", $"{HWFormat.Fixed(w?.Result.Chi2 ?? -1, 3)},df{w?.Result.Df ?? 0},{HWFormat.Fixed(w?.Result.Critical ?? 0, 2)}",
                w != null && Math.Abs(w.Result.Chi2 - 0.470) < 0.001 && w.Result.Df == 3 && w.Result.Critical == 7.81 && !w.Result.Reject
                && w.NullHypothesis.Contains("The data fit a 9:3:3:1 ratio.", StringComparison.Ordinal) && w.NullHypothesis.StartsWith("There is no statistically significant difference", StringComparison.Ordinal)
                && w.Result.Conclusion.Contains("fail to reject the null hypothesis", StringComparison.Ordinal) && w.Expected.SequenceEqual(new[] { 312.75, 104.25, 104.25, 34.75 }));
            var words = string.Join(" ", w?.Result.Conclusion ?? "", w?.ConclusionDetail ?? "", w?.Meaning ?? "", w?.NullHypothesis ?? "").ToLowerInvariant();
            r.Check("cross.chi-square.words", "no-accept-no-prove", !words.Contains("accept the null", StringComparison.Ordinal) && !words.Contains("proves", StringComparison.Ordinal) && !words.Contains("proven", StringComparison.Ordinal));
            var bad = chiRows.Select(x => x.Copy()).ToList();
            bad[0].Observed = "3.5";
            bad[1].Ratio = "0";
            var made = ChiSquareWork.Make(bad);
            r.Check("cross.chi-square.problems", $"{made.Problems.Count}", made.Work == null && made.Problems.Count == 2);
        }
        // (prefilled from the cross: its phenotypes and ratio, counts already typed kept by name; H₀ before any count)
        if (Setup("Aa × Aa") is { } mono)
        {
            var filled = ChiSquareWork.Rows(mono.Cross.Result(), new[] { new ChiCategory(5, "A_", "9", "75") });
            r.Check("cross.chi-square.from-cross", string.Join(",", filled.Select(x => $"{x.Name}={x.Ratio}/{x.Observed}")),
                filled.Select(x => x.Name).SequenceEqual(new[] { "A_", "aa" }) && filled.Select(x => x.Ratio).SequenceEqual(new[] { "3", "1" }) && filled[0].Observed == "75"
                && filled[1].Observed.Length == 0 && ChiSquareWork.RatioTextOf(filled) == "3:1"
                && ChiSquareWork.NullHypothesisFor("3:1").EndsWith("The data fit a 3:1 ratio.", StringComparison.Ordinal) && ChiSquareWork.Make(filled).Work == null);
        }
        var reject = ChiSquareWork.Make(new[] { new ChiCategory(0, "A", "3", "50"), new ChiCategory(1, "a", "1", "50") }).Work;
        r.Check("cross.chi-square.reject", HWFormat.Fixed(reject?.Result.Chi2 ?? 0, 2),
            reject?.Result.Reject == true && reject.Result.Conclusion.Contains("so we reject the null hypothesis", StringComparison.Ordinal) && reject.PText == "p < 0.001");

        PedigreeChecks(r);
    }
}

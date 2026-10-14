using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simpl.Tools;

// The Crosses tool's pedigree (spec §4.5 Pedigree), the Mac's PedigreeModel.swift line for line: a family drawn
// generation by generation — squares for males, circles for females, filled when affected, half-filled for a known
// carrier — and its mode checker: for autosomal dominant, autosomal recessive, X-linked dominant and X-linked recessive,
// whether some set of genotypes explains everyone, and if not, the people that rule the mode out. Full penetrance, no new
// mutations; at most 30 people. The search is a backtracking one over each person's genotypes with constraint
// propagation over each family (two parents and their children). Plain .NET, no WPF, so the unit tests reach it.

/// <summary>A person in a pedigree: whether they are male, affected, or known to carry one recessive allele without
/// showing it (drawn half-filled), and their parents if drawn.</summary>
public sealed class PedigreePerson
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("male")] public bool Male { get; set; }
    [JsonPropertyName("affected")] public bool Affected { get; set; }
    [JsonPropertyName("carrier")] public bool Carrier { get; set; }
    [JsonPropertyName("father")] public int? Father { get; set; }
    [JsonPropertyName("mother")] public int? Mother { get; set; }

    public PedigreePerson() { }

    public PedigreePerson(int id, bool male, bool affected = false, bool carrier = false, int? father = null, int? mother = null)
    {
        Id = id;
        Male = male;
        Affected = affected;
        Carrier = carrier;
        Father = father;
        Mother = mother;
    }

    public PedigreePerson Copy() => new(Id, Male, Affected, Carrier, Father, Mother);

    public bool Same(PedigreePerson o) => Id == o.Id && Male == o.Male && Affected == o.Affected && Carrier == o.Carrier && Father == o.Father && Mother == o.Mother;
}

/// <summary>A placed person: generation (0 at the top), slot across (half slots between partners), and label ("II-3").</summary>
public sealed record PedigreeSpot(int Id, int Generation, double X, string Label);

/// <summary>
/// A pedigree: its people and its couples (each [male, female]). It grows from the first person down — a partner for
/// someone without one, a child for a couple — so it lays out as a family tree. Changed on a copy (<see cref="Copy"/>),
/// as the Mac's struct is.
/// </summary>
public sealed class Pedigree : IEquatable<Pedigree>
{
    [JsonPropertyName("people")] public List<PedigreePerson> People { get; set; } = new();
    [JsonPropertyName("couples")] public List<int[]> Couples { get; set; } = new();
    [JsonPropertyName("nextID")] public int NextId { get; set; }

    public const int MaxPeople = 30;

    public Pedigree() { }

    public Pedigree(IEnumerable<PedigreePerson> people, IEnumerable<int[]> couples)
    {
        People = people.ToList();
        Couples = couples.ToList();
        NextId = (People.Count == 0 ? -1 : People.Max(p => p.Id)) + 1;
    }

    /// <summary>One couple, neither affected: where a pedigree starts.</summary>
    public static Pedigree Start => new(new[] { new PedigreePerson(0, true), new PedigreePerson(1, false) }, new[] { new[] { 0, 1 } });

    public Pedigree Copy() => new() { People = People.Select(p => p.Copy()).ToList(), Couples = Couples.Select(c => c.ToArray()).ToList(), NextId = NextId };

    public PedigreePerson? Person(int id) => People.FirstOrDefault(p => p.Id == id);

    public int? Partner(int id)
    {
        foreach (var c in Couples)
            if (c.Contains(id)) return c[0] == id ? c[1] : c[0];
        return null;
    }

    public List<int> Children(int id) => People.Where(p => p.Father == id || p.Mother == id).Select(p => p.Id).ToList();

    /// <summary>Whether someone joined the family as a partner (no parents drawn, and not the first person).</summary>
    public bool MarriedIn(int id) => Person(id) is { Father: null, Mother: null } && id != People.FirstOrDefault()?.Id;

    [JsonIgnore] public bool IsFull => People.Count >= MaxPeople;

    /// <summary>A partner of the other sex for someone without one.</summary>
    public int? AddPartner(int id)
    {
        if (IsFull || Person(id) is not { } p || Partner(id) != null) return null;
        var n = NextId++;
        People.Add(new PedigreePerson(n, !p.Male));
        Couples.Add(p.Male ? new[] { id, n } : new[] { n, id });
        return n;
    }

    /// <summary>A child of a couple (someone and their partner).</summary>
    public int? AddChild(int id, bool male, bool affected = false)
    {
        if (IsFull || Partner(id) is not { } mate || Person(id) is not { } p) return null;
        var n = NextId++;
        People.Add(new PedigreePerson(n, male, affected, false, p.Male ? id : mate, p.Male ? mate : id));
        return n;
    }

    /// <summary>Whether someone may go: everyone but the first person.</summary>
    public bool CanRemove(int id) => id != People.FirstOrDefault()?.Id && Person(id) != null;

    /// <summary>Someone taken out, with their descendants, and a partner who joined only through them.</summary>
    public void Remove(int id)
    {
        if (!CanRemove(id)) return;
        var gone = new HashSet<int> { id };
        var stack = new Stack<int>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            var x = stack.Pop();
            foreach (var c in Children(x))
                if (gone.Add(c)) stack.Push(c);
            // (a partner with no family of their own goes with them)
            if (Partner(x) is { } m && MarriedIn(m) && gone.Add(m)) stack.Push(m);
        }
        People.RemoveAll(p => gone.Contains(p.Id));
        Couples.RemoveAll(c => c.Any(gone.Contains));
    }

    /// <summary>Sex can change only for someone without a partner (a couple is a father and a mother).</summary>
    public bool CanChangeSex(int id) => Partner(id) == null;

    public void Update(int id, Action<PedigreePerson> change)
    {
        if (Person(id) is not { } p) return;
        change(p);
        if (p.Affected) p.Carrier = false;
    }

    // ---- layout ----

    /// <summary>Everyone placed: each generation a row, each person's children centred under their couple, partners side
    /// by side, numbered left to right in each generation (I-1, I-2, II-1 …).</summary>
    public List<PedigreeSpot> Layout()
    {
        var outs = new List<PedigreeSpot>();
        if (People.Count == 0) return outs;
        var root = People[0].Id;
        var spots = new Dictionary<int, (int Gen, double X)>();
        List<int> Kids(int id) => Children(id);
        double Width(int id)
        {
            double block = Partner(id) == null ? 1 : 2;
            var w = Kids(id).Sum(Width);
            return Math.Max(block, w);
        }
        void Place(int id, int gen, double x0)
        {
            var w = Width(id);
            var ks = Kids(id);
            var kw = ks.Sum(Width);
            var x = x0 + (w - kw) / 2;
            var centres = new List<double>();
            foreach (var k in ks)
            {
                Place(k, gen + 1, x);
                centres.Add(spots.TryGetValue(k, out var s) ? s.X : x);
                x += Width(k);
            }
            var mate = Partner(id);
            var centre = centres.Count == 0 ? x0 + w / 2 : (centres[0] + centres[^1]) / 2;
            centre = mate != null ? Math.Min(Math.Max(centre, x0 + 1), x0 + w - 1) : Math.Min(Math.Max(centre, x0 + 0.5), x0 + w - 0.5);
            if (mate is { } m && Person(id) is { } me && Person(m) is { } them)
            {
                // (the man on the left of a couple, as most textbooks draw it)
                var left = me.Male || !them.Male;
                spots[id] = (gen, left ? centre - 0.5 : centre + 0.5);
                spots[m] = (gen, left ? centre + 0.5 : centre - 0.5);
            }
            else spots[id] = (gen, centre);
        }
        Place(root, 0, 0);
        string[] roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        foreach (var g in spots.Values.Select(v => v.Gen).Distinct().OrderBy(g => g))
        {
            var row = spots.Where(kv => kv.Value.Gen == g).OrderBy(kv => kv.Value.X).ThenBy(kv => kv.Key).ToList();
            var name = g < roman.Length ? roman[g] : $"{g + 1}";
            for (var i = 0; i < row.Count; i++) outs.Add(new PedigreeSpot(row[i].Key, g, row[i].Value.X, $"{name}-{i + 1}"));
        }
        return outs;
    }

    /// <summary>Each person's label ("II-3"), by id.</summary>
    [JsonIgnore] public Dictionary<int, string> Labels => Layout().ToDictionary(s => s.Id, s => s.Label);

    // ---- examples ----

    public sealed record Example(string Id, string Title, Pedigree Pedigree, string Note);

    public static readonly IReadOnlyList<Example> Examples = new Example[]
    {
        new("ar", "Two unaffected parents, an affected child", Build(p =>
        {
            var a = p.AddChild(0, false);
            p.AddChild(0, true, true);
            p.AddChild(0, false, true);
            if (a is { } x) p.AddPartner(x);
        }), "An affected child of two unaffected parents: the allele was hidden in both, so it is recessive."),
        new("ad", "Affected in every generation", Build(p =>
        {
            p.Update(0, x => x.Affected = true);
            if (p.AddChild(0, true, true) is { } son && p.AddPartner(son) is { } wife)
            {
                p.Update(wife, x => x.Affected = true);
                p.AddChild(son, false);
                p.AddChild(son, true, true);
            }
            p.AddChild(0, false);
        }), "Two affected parents with an unaffected daughter: each must carry a hidden normal allele, so the trait is dominant."),
        new("xr", "Affected sons of unaffected parents", Build(p =>
        {
            p.AddChild(0, true, true);
            if (p.AddChild(0, false) is { } d && p.AddPartner(d) != null)
            {
                p.AddChild(d, true, true);
                p.AddChild(d, false);
                p.AddChild(d, true);
            }
        }), "Only sons are affected, through unaffected mothers: X-linked recessive fits best, though autosomal recessive can't be ruled out."),
        new("xd", "An affected father's daughters", Build(p =>
        {
            p.Update(0, x => x.Affected = true);
            if (p.AddChild(0, false, true) is { } d && p.AddPartner(d) is { } h)
            {
                p.Update(h, x => x.Affected = true);
                p.AddChild(d, true);
                p.AddChild(d, false, true);
            }
            p.AddChild(0, true);
            p.AddChild(0, false, true);
        }), "An affected father passes it to every daughter and to no son: the pattern of an X-linked dominant trait."),
        new("start", "One couple", Start, "Add partners and children to draw your own."),
    };

    private static Pedigree Build(Action<Pedigree> make)
    {
        var p = Start;
        make(p);
        return p;
    }

    // ---- remembered ----

    [JsonIgnore] public string Encoded => JsonSerializer.Serialize(this);

    public static Pedigree? Decode(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        Pedigree? p;
        try { p = JsonSerializer.Deserialize<Pedigree>(s); } catch (JsonException) { return null; }
        if (p == null || p.People.Count == 0 || p.People.Count > MaxPeople) return null;
        var ids = p.People.Select(x => x.Id).ToHashSet();
        if (ids.Count != p.People.Count || !p.Couples.All(c => c != null && c.Length == 2 && ids.Contains(c[0]) && ids.Contains(c[1]))) return null;
        // (parents always come before their children, as the editor adds them: no loops to lay out)
        var earlier = new HashSet<int>();
        foreach (var x in p.People)
        {
            if (x.Father is { } f && !earlier.Contains(f)) return null;
            if (x.Mother is { } m && !earlier.Contains(m)) return null;
            earlier.Add(x.Id);
        }
        return p;
    }

    public bool Equals(Pedigree? o) =>
        o != null && NextId == o.NextId && People.Count == o.People.Count && People.Zip(o.People).All(z => z.First.Same(z.Second))
        && Couples.Count == o.Couples.Count && Couples.Zip(o.Couples).All(z => z.First.SequenceEqual(z.Second));

    public override bool Equals(object? obj) => obj is Pedigree p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(People.Count, Couples.Count, NextId);
}

// ---- the mode checker ----

/// <summary>A mode of inheritance the checker tries.</summary>
public enum PedigreeMode { AutosomalDominant, AutosomalRecessive, XDominant, XRecessive }

public static class PedigreeModes
{
    public static readonly PedigreeMode[] All = Enum.GetValues<PedigreeMode>();

    /// <summary>The Mac's raw value (a launch's name for it).</summary>
    public static string Key(this PedigreeMode m) => m switch
    {
        PedigreeMode.AutosomalDominant => "autosomalDominant", PedigreeMode.AutosomalRecessive => "autosomalRecessive",
        PedigreeMode.XDominant => "xDominant", _ => "xRecessive",
    };

    public static string Title(this PedigreeMode m) => m switch
    {
        PedigreeMode.AutosomalDominant => "Autosomal dominant", PedigreeMode.AutosomalRecessive => "Autosomal recessive",
        PedigreeMode.XDominant => "X-linked dominant", _ => "X-linked recessive",
    };

    public static string Short(this PedigreeMode m) => m switch
    {
        PedigreeMode.AutosomalDominant => "AD", PedigreeMode.AutosomalRecessive => "AR", PedigreeMode.XDominant => "XD", _ => "XR",
    };

    public static bool XLinked(this PedigreeMode m) => m is PedigreeMode.XDominant or PedigreeMode.XRecessive;
    public static bool Dominant(this PedigreeMode m) => m is PedigreeMode.AutosomalDominant or PedigreeMode.XDominant;
}

/// <summary>A mode's verdict: consistent, or ruled out — by whom (the people whose phenotypes together rule it out), and
/// why (a sentence for each conflict found).</summary>
public sealed record PedigreeVerdict(PedigreeMode Mode, bool Consistent, IReadOnlyList<int> Culprits, IReadOnlyList<string> Reasons);

public static class PedigreeChecker
{
    // (a person's genotypes as a set of bits: bit g for g disease alleles, 0–2; a male's X-linked: 0 or 1)

    private sealed record Family(int Father, int Mother, List<int> Kids);

    /// <summary>Every mode's verdict.</summary>
    public static List<PedigreeVerdict> Check(Pedigree p) => PedigreeModes.All.Select(m => Verdict(p, m)).ToList();

    public static PedigreeVerdict Verdict(Pedigree p, PedigreeMode mode)
    {
        var everyone = p.People.Select(x => x.Id).ToHashSet();
        if (Consistent(p, mode, everyone)) return new PedigreeVerdict(mode, true, Array.Empty<int>(), Array.Empty<string>());
        // (the people who rule it out: a smallest set whose phenotypes alone can't be explained — found by letting each
        //  person's phenotype go in turn and keeping it only if the rest is then explainable — then the next such set among
        //  the others, until what is left fits)
        var labels = p.Labels;
        var order = p.Layout().Select(s => s.Id).ToList();
        var culprits = new List<int>();
        var reasons = new List<string>();
        var relaxed = new HashSet<int>();
        for (var round = 0; round < Pedigree.MaxPeople; round++)
        {
            var s = everyone.Where(x => !relaxed.Contains(x)).ToHashSet();
            if (Consistent(p, mode, s)) break;
            for (var i = order.Count - 1; i >= 0; i--)
            {
                var id = order[i];
                if (!s.Contains(id)) continue;
                var without = s.Where(x => x != id).ToHashSet();
                if (!Consistent(p, mode, without)) s = without;
            }
            var set = order.Where(s.Contains).ToList();
            if (set.Count == 0) break;
            culprits.AddRange(set.Where(x => !culprits.Contains(x)));
            reasons.Add(Reason(p, mode, set, labels));
            relaxed.UnionWith(set);
        }
        return new PedigreeVerdict(mode, false, culprits, reasons);
    }

    /// <summary>The genotypes a person can have in a mode, from their phenotype (or every genotype, if their phenotype is
    /// not counted), as bits.</summary>
    public static int Domain(PedigreePerson x, PedigreeMode mode, bool constrained)
    {
        var all = mode.XLinked() && x.Male ? 0b011 : 0b111;
        if (!constrained) return all;
        var d = 0;
        for (var g = 0; g < 3; g++)
        {
            if ((all & (1 << g)) == 0) continue;
            var shows = mode switch
            {
                PedigreeMode.AutosomalDominant or PedigreeMode.XDominant => g >= 1,
                PedigreeMode.AutosomalRecessive => g == 2,
                _ => x.Male ? g == 1 : g == 2,
            };
            bool ok;
            if (x.Affected) ok = shows;
            else if (x.Carrier) ok = !shows && g == 1 && !(mode.XLinked() && x.Male);
            else ok = !shows;
            if (ok) d |= 1 << g;
        }
        return d;
    }

    /// <summary>Whether a parent with genotype g can pass on allele a (0 normal, 1 disease).</summary>
    private static bool Passes(int g, int a) => g switch { 0 => a == 0, 1 => a is 0 or 1, _ => a == 1 };

    /// <summary>Whether a child's genotype can come from these parents' (Mendelian transmission; a son's X from his mother).</summary>
    public static bool Transmits(int father, int mother, int child, bool male, PedigreeMode mode)
    {
        if (mode.XLinked()) return male ? Passes(mother, child) : Passes(mother, child - father);
        return (Passes(father, 0) && Passes(mother, child)) || (Passes(father, 1) && Passes(mother, child - 1));
    }

    /// <summary>Whether some genotypes explain everyone, counting only the phenotypes of <paramref name="constrained"/>.</summary>
    public static bool Consistent(Pedigree p, PedigreeMode mode, HashSet<int> constrained)
    {
        var d = new Dictionary<int, int>();
        foreach (var x in p.People) d[x.Id] = Domain(x, mode, constrained.Contains(x.Id));
        // (each family: two parents and their children)
        var families = new Dictionary<(int, int), Family>();
        var fams = new List<Family>();
        foreach (var x in p.People)
        {
            if (x.Father is not { } f || x.Mother is not { } m || !d.ContainsKey(f) || !d.ContainsKey(m)) continue;
            if (!families.TryGetValue((f, m), out var fam))
            {
                fam = new Family(f, m, new List<int>());
                families[(f, m)] = fam;
                fams.Add(fam);
            }
            fam.Kids.Add(x.Id);
        }
        var males = p.People.ToDictionary(x => x.Id, x => x.Male);
        return Search(d, fams, males, mode);
    }

    private static bool Propagate(Dictionary<int, int> d, List<Family> fams, Dictionary<int, bool> males, PedigreeMode mode)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var fam in fams)
            {
                int df = d[fam.Father], dm = d[fam.Mother];
                int sf = 0, sm = 0;
                var sk = new int[fam.Kids.Count];
                for (var f = 0; f < 3; f++)
                {
                    if ((df & (1 << f)) == 0) continue;
                    for (var m = 0; m < 3; m++)
                    {
                        if ((dm & (1 << m)) == 0) continue;
                        var fits = new int[fam.Kids.Count];
                        var all = true;
                        for (var i = 0; i < fam.Kids.Count && all; i++)
                        {
                            var k = fam.Kids[i];
                            for (var c = 0; c < 3; c++)
                                if ((d[k] & (1 << c)) != 0 && Transmits(f, m, c, males[k], mode)) fits[i] |= 1 << c;
                            if (fits[i] == 0) all = false;
                        }
                        if (!all) continue;
                        sf |= 1 << f;
                        sm |= 1 << m;
                        for (var i = 0; i < fits.Length; i++) sk[i] |= fits[i];
                    }
                }
                if (sf == 0) return false;
                if (sf != df) { d[fam.Father] = sf; changed = true; }
                if (sm != dm) { d[fam.Mother] = sm; changed = true; }
                for (var i = 0; i < fam.Kids.Count; i++)
                {
                    var k = fam.Kids[i];
                    if (sk[i] == d[k]) continue;
                    if (sk[i] == 0) return false;
                    d[k] = sk[i];
                    changed = true;
                }
            }
        }
        return d.Values.All(v => v != 0);
    }

    private static bool Search(Dictionary<int, int> d, List<Family> fams, Dictionary<int, bool> males, PedigreeMode mode)
    {
        if (!Propagate(d, fams, males, mode)) return false;
        // (the undecided person with the fewest genotypes left, the lowest id first)
        int? pick = null;
        var fewest = int.MaxValue;
        foreach (var (id, dom) in d)
        {
            var n = BitOperations.PopCount((uint)dom);
            if (n <= 1) continue;
            if (n < fewest || (n == fewest && id < pick)) { pick = id; fewest = n; }
        }
        if (pick is not { } who) return true;
        for (var v = 0; v < 3; v++)
        {
            if ((d[who] & (1 << v)) == 0) continue;
            var e = new Dictionary<int, int>(d) { [who] = 1 << v };
            if (Search(e, fams, males, mode)) return true;
        }
        return false;
    }

    /// <summary>Why a set of people rules a mode out, in a sentence: the textbook's reasons where they fit, else a general one.</summary>
    public static string Reason(Pedigree p, PedigreeMode mode, IReadOnlyList<int> set, IReadOnlyDictionary<int, string> labels)
    {
        string Name(int id) => labels.TryGetValue(id, out var l) ? l : "?";
        var people = set.Select(p.Person).Where(x => x != null).Select(x => x!).ToList();
        if (people.Count == 1 && people[0].Carrier)
        {
            var x = people[0];
            if (mode.XLinked() && x.Male) return $"{Name(x.Id)} is marked as a carrier, but a male has one X: he either shows it or doesn't have it.";
            if (mode.Dominant()) return $"{Name(x.Id)} is marked as a carrier, but a dominant allele always shows (full penetrance).";
        }
        // (a child and both parents)
        if (people.Count == 3 && people.FirstOrDefault(c => c.Father is { } cf && set.Contains(cf) && c.Mother is { } cm && set.Contains(cm)) is { } child
            && child.Father is { } f && child.Mother is { } m && p.Person(f) is { } father && p.Person(m) is { } mother)
        {
            if (mode.Dominant() && child.Affected && !father.Affected && !mother.Affected)
                return $"{Name(child.Id)} is affected but neither parent ({Name(f)}, {Name(m)}) is: a dominant allele would have come from an affected parent.";
            if (!mode.Dominant() && !child.Affected && father.Affected && mother.Affected && !mode.XLinked())
                return $"{Name(f)} and {Name(m)} are both affected, so both are aa and every child would be too — but {Name(child.Id)} is not.";
            if (mode == PedigreeMode.XRecessive && !child.Affected && father.Affected && mother.Affected)
                return $"Both parents ({Name(f)}, {Name(m)}) are affected, so every child gets the recessive X from each — but {Name(child.Id)} is not affected.";
        }
        if (people.Count == 2)
        {
            PedigreePerson a = people[0], b = people[^1];
            var (parent, kid) = a.Id == b.Father || a.Id == b.Mother ? (a, b) : (b, a);
            if (kid.Father == parent.Id || kid.Mother == parent.Id)
            {
                var isMother = kid.Mother == parent.Id;
                if (mode == PedigreeMode.XRecessive && isMother && parent.Affected && kid.Male && !kid.Affected)
                    return $"{Name(parent.Id)} is an affected mother, so she passes the recessive X to every son — but her son {Name(kid.Id)} is not affected.";
                if (mode == PedigreeMode.XRecessive && !isMother && !parent.Affected && !kid.Male && kid.Affected)
                    return $"{Name(kid.Id)} is an affected daughter, so she got a recessive X from her father — but {Name(parent.Id)} is not affected.";
                if (mode == PedigreeMode.XDominant && !isMother && parent.Affected && !kid.Male && !kid.Affected)
                    return $"{Name(parent.Id)} is an affected father, so every daughter gets his X — but {Name(kid.Id)} is not affected.";
                if (mode == PedigreeMode.XDominant && isMother && !parent.Affected && kid.Male && kid.Affected)
                    return $"{Name(kid.Id)} is an affected son, whose only X came from his mother — but {Name(parent.Id)} is not affected.";
            }
        }
        return $"No genotypes explain {string.Join(", ", set.Select(Name))} together under {mode.Title().ToLowerInvariant()} inheritance.";
    }
}

public static partial class BioSelfCheck
{
    /// <summary>The pedigree's lines (the Mac's pedigreeChecks): the layout's labels, and each example's verdicts with the
    /// people that rule modes out.</summary>
    private static void PedigreeChecks(BioCheckReport r)
    {
        static Dictionary<PedigreeMode, PedigreeVerdict> Verdicts(Pedigree? p) => p == null ? new() : PedigreeChecker.Check(p).ToDictionary(v => v.Mode);
        static Pedigree? Ex(string id) => Pedigree.Examples.FirstOrDefault(e => e.Id == id)?.Pedigree;
        static string Summary(Dictionary<PedigreeMode, PedigreeVerdict> v, Dictionary<int, string> labels) => string.Join(";", PedigreeModes.All.Select(m =>
            !v.TryGetValue(m, out var x) ? $"{m.Short()}?" : x.Consistent ? $"{m.Short()}ok" : $"{m.Short()}out(" + string.Join(",", x.Culprits.Where(labels.ContainsKey).Select(c => labels[c])) + ")"));
        static Dictionary<int, string> Labels(string id) => Ex(id)?.Labels ?? new();
        static bool Is(Dictionary<PedigreeMode, PedigreeVerdict> v, PedigreeMode m, bool consistent) => v.TryGetValue(m, out var x) && x.Consistent == consistent;

        // (an affected child of unaffected parents: only autosomal recessive is left; X-linked recessive falls to an
        //  affected daughter of an unaffected father)
        var ar = Verdicts(Ex("ar"));
        var arL = Labels("ar");
        HashSet<string> ArOut(PedigreeMode m) => (ar.TryGetValue(m, out var x) ? x.Culprits : Array.Empty<int>()).Where(arL.ContainsKey).Select(c => arL[c]).ToHashSet();
        r.Check("pedigree.AR", Summary(ar, arL),
            Is(ar, PedigreeMode.AutosomalRecessive, true) && Is(ar, PedigreeMode.AutosomalDominant, false) && Is(ar, PedigreeMode.XDominant, false)
            && Is(ar, PedigreeMode.XRecessive, false) && ArOut(PedigreeMode.XRecessive).Contains("I-1") && ArOut(PedigreeMode.AutosomalDominant).Contains("I-1")
            && ArOut(PedigreeMode.AutosomalDominant).Contains("I-2"));
        var ad = Verdicts(Ex("ad"));
        r.Check("pedigree.AD", Summary(ad, Labels("ad")),
            Is(ad, PedigreeMode.AutosomalDominant, true) && Is(ad, PedigreeMode.AutosomalRecessive, false) && Is(ad, PedigreeMode.XDominant, false) && Is(ad, PedigreeMode.XRecessive, false));
        var xr = Verdicts(Ex("xr"));
        r.Check("pedigree.XR", Summary(xr, Labels("xr")),
            Is(xr, PedigreeMode.XRecessive, true) && Is(xr, PedigreeMode.AutosomalRecessive, true) && Is(xr, PedigreeMode.AutosomalDominant, false) && Is(xr, PedigreeMode.XDominant, false)
            && xr.TryGetValue(PedigreeMode.XDominant, out var xdv) && xdv.Reasons.FirstOrDefault()?.Contains("affected son", StringComparison.Ordinal) == true);
        var xd = Verdicts(Ex("xd"));
        r.Check("pedigree.XD", Summary(xd, Labels("xd")),
            Is(xd, PedigreeMode.XDominant, true) && Is(xd, PedigreeMode.AutosomalDominant, true) && Is(xd, PedigreeMode.XRecessive, false) && Is(xd, PedigreeMode.AutosomalRecessive, false));

        // (the layout: generations numbered left to right, children under their parents, nobody on top of anybody)
        if (Ex("xr") is { } e)
        {
            var spots = e.Layout();
            var overlap = spots.GroupBy(s => s.Generation).Any(row =>
            {
                var xs = row.Select(s => s.X).OrderBy(x => x).ToList();
                return xs.Zip(xs.Skip(1)).Any(z => z.Second - z.First < 0.99);
            });
            r.Check("pedigree.layout", string.Join(",", spots.Select(s => s.Label).OrderBy(l => l, StringComparer.Ordinal)),
                spots.Count == e.People.Count && !overlap && spots.Where(s => s.Generation == 0).Select(s => s.Label).OrderBy(l => l, StringComparer.Ordinal).SequenceEqual(new[] { "I-1", "I-2" })
                && spots.Select(s => s.Label).Distinct().Count() == spots.Count);
        }

        // (editing: a child needs a couple; removing someone takes their descendants and their partner who married in)
        var p = Pedigree.Start;
        var kid = p.AddChild(0, true);
        var mate = kid is { } k1 ? p.AddPartner(k1) : null;
        var grandchild = kid is { } k2 ? p.AddChild(k2, false, true) : null;
        var alone = p.AddChild(grandchild ?? -1, true);
        var before = p.People.Count;
        if (kid is { } k3) p.Remove(k3);
        r.Check("pedigree.edit", $"{before}->{p.People.Count}", kid != null && mate != null && grandchild != null && alone == null && before == 5 && p.People.Count == 2
            && p.Couples.Count == 1 && p.Couples[0].SequenceEqual(new[] { 0, 1 }) && !p.CanRemove(0) && p.Equals(Pedigree.Decode(p.Encoded)));

        // (a carrier marked in a dominant mode rules it out by itself; a lone affected child of a carrier mother and an
        //  unaffected father fits X-linked recessive)
        var c = Pedigree.Start;
        c.Update(1, x => x.Carrier = true);
        if (c.AddChild(0, true, true) is { } son)
        {
            var v = PedigreeChecker.Check(c).ToDictionary(x => x.Mode);
            r.Check("pedigree.carrier", Summary(v, c.Labels), Is(v, PedigreeMode.XRecessive, true) && v[PedigreeMode.AutosomalDominant].Culprits.SequenceEqual(new[] { 1 })
                && Is(v, PedigreeMode.XDominant, false) && Is(v, PedigreeMode.AutosomalRecessive, true) && son > 1);
        }
        // (thirty people still check at once: a chain of five generations)
        var big = Pedigree.Start;
        var line = 0;
        for (var g = 0; g < 9; g++)
        {
            if (big.AddChild(line, g % 2 == 0, g % 3 == 0) is not { } a) break;
            big.AddChild(line, g % 2 == 1);
            if (big.AddPartner(a) != null) line = a;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var bigV = PedigreeChecker.Check(big);
        var ms = watch.Elapsed.TotalMilliseconds;
        r.Check("pedigree.size", $"{big.People.Count}people", big.People.Count >= 25 && bigV.Count == 4 && ms < 2000);
    }
}

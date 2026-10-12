using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Simpl.Tools;

// The Lewis structure builder's model (the Mac's LewisMaker.swift, its first half). A molecule is a graph — atoms (an
// element and where it is drawn), bonds (two atoms and an order) and a total charge — made either quickly (a central atom,
// the atoms round it and a charge) or drawn freely: chains, branches, rings and two-atom molecules alike, of any
// main-group element.
//
// For any graph its electrons are placed (LewisSolver): the total is the valence electrons less the charge; every
// assignment of bond orders the atoms allow is tried, outer atoms filled first and the rest on inner atoms (expanded to
// twelve from period 3 down), an odd electron left single; formal charges worked out; and the structures ranked — by the
// strict octet rule or by the smallest formal charges (textbooks differ: SO₄²⁻), negative charge on the more
// electronegative atoms. The best tie are its resonance structures (their average bond orders worked out), a close second
// minor contributors. Metals of groups 1 and 2 with nonmetals are ionic: cations and anions, each in brackets with its
// charge. Every atom's VSEPR shape; the polarity from the molecule in 3D (MoleculeModel's VSEPR model); warnings (a
// period 2 atom over eight, hydrogen with two bonds, too few electrons, unconnected atoms); and notes in place of a
// structure where a Lewis picture would mislead (transition-metal complexes, diborane) or is known to be wrong (O₂).

/// <summary>The main-group elements as the builders know them (the Mac's LChem).</summary>
internal static class LChem
{
    /// <summary>The main-group elements, as the palette lays them out: groups 1, 2 and 13–18, periods 1–6.</summary>
    public static readonly string?[][] Palette =
    {
        new[] { "H", null, null, null, null, null, null, "He" },
        new[] { "Li", "Be", "B", "C", "N", "O", "F", "Ne" },
        new[] { "Na", "Mg", "Al", "Si", "P", "S", "Cl", "Ar" },
        new[] { "K", "Ca", "Ga", "Ge", "As", "Se", "Br", "Kr" },
        new[] { "Rb", "Sr", "In", "Sn", "Sb", "Te", "I", "Xe" },
        new[] { "Cs", "Ba", "Tl", "Pb", "Bi", "Po", "At", "Rn" },
    };

    public static ChemElement? Element(string s) => ChemElements.Of(s);

    public static string Name(string s) => Element(s)?.Name ?? s;

    public static int Valence(string s)
    {
        if (s == "H") return 1;
        if (s == "He") return 2;
        if (Element(s)?.Group is not { } g) return 4;
        return g <= 2 ? g : (g >= 13 ? g - 10 : 2);
    }

    public static double En(string s)
    {
        if (Element(s)?.En is { } v) return v;
        return s switch { "He" => 4.5, "Ne" => 4.4, "Ar" => 3.6, "Kr" => 3.0, "Xe" => 2.6, "Rn" => 2.2, _ => 2.0 };
    }

    public static int Period(string s) => Element(s)?.Period ?? 2;

    /// <summary>The most electrons it may have round it: 2 for H and He, 4 for Be, 6 for B and Al, 8 in period 2, 12 below.</summary>
    public static int MaxElectrons(string s) => s switch
    {
        "H" or "He" => 2,
        "Be" => 4,
        "B" or "Al" => 6,
        _ => Period(s) <= 2 ? 8 : 12,
    };

    /// <summary>The electrons it aims for: its duet or octet (or Be's four, B's and Al's six).</summary>
    public static int Target(string s) => Math.Min(MaxElectrons(s), 8);

    /// <summary>A metal of group 1 or 2 (hydrogen and beryllium apart): ionic with nonmetals.</summary>
    public static bool IonicMetal(string s) => s != "H" && s != "Be" && Element(s)?.Group is { } g && g <= 2;

    public static bool Transition(string s) => Element(s)?.Block is "d" or "f";

    /// <summary>Its CPK colour; null (the text colour) for the pale ones, which do not read on a light page, and carbon.</summary>
    public static Color? ColorOf(string s)
    {
        if (s == "H" || s == "C") return null;
        if (Element(s)?.Cpk is not { Length: 6 } hex || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
        double r = ((v >> 16) & 0xff) / 255.0, g = ((v >> 8) & 0xff) / 255.0, b = (v & 0xff) / 255.0;
        if (0.2126 * r + 0.7152 * g + 0.0722 * b > 0.6) return null;
        return Color.FromRgb((byte)((v >> 16) & 0xff), (byte)((v >> 8) & 0xff), (byte)(v & 0xff));
    }

    /// <summary>The VSEPR shape and bond angle by bonded atoms and lone pairs (an odd electron counted as a group).</summary>
    public static (string Name, string Angle) Shape(int b, int l) => (b, l) switch
    {
        (0, _) => ("A lone atom", "—"),
        (1, _) => ("Linear (terminal)", "—"),
        (2, 0) => ("Linear", "180°"),
        (2, 1) => ("Bent", "about 117°"),
        (2, 2) => ("Bent", "about 104.5°"),
        (2, 3) => ("Linear", "180°"),
        (3, 0) => ("Trigonal planar", "120°"),
        (3, 1) => ("Trigonal pyramidal", "about 107°"),
        (3, 2) => ("T-shaped", "about 90°"),
        (4, 0) => ("Tetrahedral", "109.5°"),
        (4, 1) => ("Seesaw", "about 90° and 120°"),
        (4, 2) => ("Square planar", "90°"),
        (5, 0) => ("Trigonal bipyramidal", "90° and 120°"),
        (5, 1) => ("Square pyramidal", "about 90°"),
        (6, 0) => ("Octahedral", "90°"),
        _ => ($"{b + l} electron groups", "—"),
    };

    private const string Subs = "₀₁₂₃₄₅₆₇₈₉", Sups = "⁰¹²³⁴⁵⁶⁷⁸⁹";

    public static string Subscript(int n) => string.Concat(n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => c is >= '0' and <= '9' ? Subs[c - '0'] : c));

    public static string Superscript(int n) => string.Concat(n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => c is >= '0' and <= '9' ? Sups[c - '0'] : c));

    public static string Subscripted(int n) => n > 1 ? Subscript(n) : "";

    /// <summary>
    /// Conventional order: carbon compounds by Hill (C, H, then by symbol); others by rising electronegativity, hydrogen
    /// last after an element of groups 13–15 (NH₃, PH₃, BH₃) and first otherwise (H₂O, HCl).
    /// </summary>
    public static string Formula(IEnumerable<string> symbols, int charge)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in symbols) counts[s] = counts.GetValueOrDefault(s) + 1;
        List<string> order;
        if (counts.ContainsKey("C"))
        {
            order = new[] { "C", "H" }.Where(counts.ContainsKey).ToList();
            order.AddRange(counts.Keys.Where(k => k != "C" && k != "H").OrderBy(k => k, StringComparer.Ordinal));
        }
        else
        {
            order = counts.Keys.OrderBy(k => En(k)).ThenBy(k => k, StringComparer.Ordinal).ToList();
            if (counts.ContainsKey("H"))
            {
                var others = order.Where(k => k != "H").ToList();
                var hLast = others.Count == 1 && Element(others[0])?.Group is 13 or 14 or 15;
                order = hLast ? others.Append("H").ToList() : others.Prepend("H").ToList();
            }
        }
        return string.Concat(order.Select(k => k + Subscripted(counts.GetValueOrDefault(k)))) + ChargeText(charge);
    }

    public static string ChargeText(int q)
    {
        if (q == 0) return "";
        return (Math.Abs(q) > 1 ? Superscript(Math.Abs(q)) : "") + (q > 0 ? "⁺" : "⁻");
    }
}

// ---- the graph ----

internal sealed class LAtom
{
    public int Id { get; init; }
    public string Symbol { get; set; } = "C";
    /// <summary>Where it is drawn, from the canvas's centre.</summary>
    public Point Pos { get; set; }

    public LAtom Copy() => new() { Id = Id, Symbol = Symbol, Pos = Pos };
}

internal sealed class LBond
{
    public int Id { get; init; }
    public int A { get; set; }
    public int B { get; set; }
    /// <summary>The order drawn when the orders are the student's own (<see cref="LGraph.Manual"/>).</summary>
    public int Order { get; set; } = 1;

    public int Other(int x) => x == A ? B : A;
    public LBond Copy() => new() { Id = Id, A = A, B = B, Order = Order };
}

/// <summary>A molecule: its atoms, its bonds, its total charge (the Mac's LGraph).</summary>
internal sealed class LGraph
{
    public List<LAtom> Atoms { get; set; } = new();
    public List<LBond> Bonds { get; set; } = new();
    public int Charge { get; set; }
    /// <summary>The bonds' orders set by hand (a bond's order cycled): its electrons placed round them, as they are.</summary>
    public bool Manual { get; set; }
    public int NextId { get; set; } = 1;

    /// <summary>
    /// What the electrons depend on — its atoms, bonds, charge and whether its orders are the student's — and not where
    /// it is drawn: a molecule moved about is not worked out again.
    /// </summary>
    public string TopologyKey
    {
        get
        {
            var k = new StringBuilder();
            k.Append(Charge).Append('|').Append(Manual).Append('|');
            foreach (var a in Atoms) k.Append(a.Id).Append(a.Symbol).Append(',');
            k.Append('|');
            foreach (var b in Bonds) k.Append(b.Id).Append(':').Append(b.A).Append('-').Append(b.B).Append('x').Append(b.Order).Append(',');
            return k.ToString();
        }
    }

    public LGraph Copy() => new()
    {
        Atoms = Atoms.Select(a => a.Copy()).ToList(), Bonds = Bonds.Select(b => b.Copy()).ToList(), Charge = Charge, Manual = Manual, NextId = NextId,
    };

    public LAtom? Atom(int id) => Atoms.FirstOrDefault(a => a.Id == id);
    public int Degree(int id) => Bonds.Count(b => b.A == id || b.B == id);
    public LBond? Bond(int x, int y) => Bonds.FirstOrDefault(b => (b.A == x && b.B == y) || (b.A == y && b.B == x));

    public int Add(string symbol, Point p)
    {
        Atoms.Add(new LAtom { Id = NextId, Symbol = symbol, Pos = p });
        NextId++;
        return NextId - 1;
    }

    public void Connect(int x, int y, int order = 1)
    {
        if (x == y || Bond(x, y) != null) return;
        Bonds.Add(new LBond { Id = NextId, A = x, B = y, Order = order });
        NextId++;
    }

    public void Remove(int atom)
    {
        Atoms.RemoveAll(a => a.Id == atom);
        Bonds.RemoveAll(b => b.A == atom || b.B == atom);
    }

    /// <summary>Its pieces: each set of atoms joined by bonds.</summary>
    public List<List<int>> Components(ISet<int>? skip = null)
    {
        skip ??= new HashSet<int>();
        var seen = new HashSet<int>();
        var outs = new List<List<int>>();
        foreach (var a in Atoms)
        {
            if (seen.Contains(a.Id) || skip.Contains(a.Id)) continue;
            var part = new List<int>();
            var stack = new List<int> { a.Id };
            seen.Add(a.Id);
            while (stack.Count > 0)
            {
                var x = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                part.Add(x);
                foreach (var b in Bonds)
                {
                    if (b.A != x && b.B != x) continue;
                    var y = b.Other(x);
                    if (!seen.Contains(y) && !skip.Contains(y))
                    {
                        seen.Add(y);
                        stack.Add(y);
                    }
                }
            }
            outs.Add(part);
        }
        return outs;
    }

    /// <summary>The smallest ring (three to eight atoms), in order round it, if there is one.</summary>
    public List<int>? SmallestRing()
    {
        List<int>? best = null;
        foreach (var b in Bonds)
        {
            // (the shortest way from one end of the bond to the other without it)
            var prev = new Dictionary<int, int> { [b.A] = b.A };
            var queue = new Queue<int>();
            queue.Enqueue(b.A);
            while (queue.Count > 0)
            {
                var x = queue.Dequeue();
                foreach (var e in Bonds)
                {
                    if ((e.A != x && e.B != x) || e.Id == b.Id) continue;
                    var y = e.Other(x);
                    if (!prev.ContainsKey(y))
                    {
                        prev[y] = x;
                        queue.Enqueue(y);
                    }
                }
            }
            if (!prev.ContainsKey(b.B)) continue;
            var path = new List<int> { b.B };
            while (path[^1] != b.A && prev.TryGetValue(path[^1], out var p)) path.Add(p);
            if (path.Count >= 3 && path.Count <= 8 && path.Count < (best?.Count ?? 99)) best = path;
        }
        return best;
    }

    /// <summary>
    /// Laid out afresh: a ring (if any) as a regular polygon, branches pointing away from where they come from, then
    /// springs along the bonds and every atom pushing the others away.
    /// </summary>
    public void Tidy()
    {
        if (Atoms.Count == 0) return;
        const double bondLength = 78;
        var ring = SmallestRing();
        var placed = new Dictionary<int, Point>();
        var heading = new Dictionary<int, double>();
        var byDegree = Atoms.OrderByDescending(a => Degree(a.Id)).ToList();
        double offset = 0;
        foreach (var start in byDegree)
        {
            if (placed.ContainsKey(start.Id)) continue;
            var queue = new Queue<int>();
            var part = new HashSet<int>(Components().FirstOrDefault(c => c.Contains(start.Id)) ?? new List<int> { start.Id });
            if (ring != null && part.Contains(ring[0]))
            {
                double m = ring.Count;
                var radius = bondLength / (2 * Math.Sin(Math.PI / m));
                for (var k = 0; k < ring.Count; k++)
                {
                    var t = -Math.PI / 2 + k * 2 * Math.PI / m;
                    placed[ring[k]] = new Point(offset + radius * Math.Cos(t), radius * Math.Sin(t));
                    heading[ring[k]] = t;
                    queue.Enqueue(ring[k]);
                }
            }
            else
            {
                placed[start.Id] = new Point(offset, 0);
                queue.Enqueue(start.Id);
            }
            while (queue.Count > 0)
            {
                var x = queue.Dequeue();
                var next = Bonds.Where(b => b.A == x || b.B == x).Select(b => b.Other(x)).Where(y => !placed.ContainsKey(y)).ToList();
                double c = next.Count;
                for (var k = 0; k < next.Count; k++)
                {
                    double t;
                    if (heading.TryGetValue(x, out var h))
                    {
                        var step = 2 * Math.PI / (c + 1);
                        t = h + (k - (c - 1) / 2) * step;
                    }
                    else
                    {
                        var first = next.Count % 2 == 0 ? 0 : -Math.PI / 2;
                        t = first + k * 2 * Math.PI / c;
                    }
                    var p = placed.TryGetValue(x, out var px) ? px : new Point();
                    placed[next[k]] = new Point(p.X + bondLength * Math.Cos(t), p.Y + bondLength * Math.Sin(t));
                    heading[next[k]] = t;
                    queue.Enqueue(next[k]);
                }
            }
            offset += 220;
        }
        var pts = Atoms.Select(a => placed.TryGetValue(a.Id, out var q) ? q : new Point()).ToArray();
        var index = new Dictionary<int, int>();
        for (var i = 0; i < Atoms.Count; i++) index[Atoms[i].Id] = i;
        for (var it = 0; it < 400; it++)
        {
            var f = new Vector[pts.Length];
            foreach (var b in Bonds)
            {
                if (!index.TryGetValue(b.A, out var i) || !index.TryGetValue(b.B, out var j)) continue;
                double dx = pts[j].X - pts[i].X, dy = pts[j].Y - pts[i].Y;
                var d = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.01);
                var k = (d - 78) * 0.1;
                f[i].X += dx / d * k;
                f[i].Y += dy / d * k;
                f[j].X -= dx / d * k;
                f[j].Y -= dy / d * k;
            }
            for (var i = 0; i < pts.Length; i++)
            {
                for (var j = i + 1; j < pts.Length; j++)
                {
                    double dx = pts[j].X - pts[i].X, dy = pts[j].Y - pts[i].Y;
                    var d2 = Math.Max(dx * dx + dy * dy, 25);
                    var k = 2600 / d2;
                    var d = Math.Sqrt(d2);
                    f[i].X -= dx / d * k;
                    f[i].Y -= dy / d * k;
                    f[j].X += dx / d * k;
                    f[j].Y += dy / d * k;
                }
            }
            for (var i = 0; i < pts.Length; i++)
                pts[i] = new Point(pts[i].X + Math.Clamp(f[i].X, -6, 6), pts[i].Y + Math.Clamp(f[i].Y, -6, 6));
        }
        double cx = pts.Average(p => p.X), cy = pts.Average(p => p.Y);
        for (var i = 0; i < Atoms.Count; i++) Atoms[i].Pos = new Point(pts[i].X - cx, pts[i].Y - cy);
    }
}

// ---- placing the electrons ----

/// <summary>One structure: each bond's order, each atom's lone (non-bonding) electrons, and its formal charge.</summary>
internal sealed class LStructure
{
    public Dictionary<int, int> Orders { get; set; } = new();
    public Dictionary<int, int> Lone { get; set; } = new();
    public Dictionary<int, int> Formal { get; set; } = new();
    public double[] Score { get; set; } = Array.Empty<double>();

    public int Order(int bond) => Orders.TryGetValue(bond, out var o) ? o : 1;
    public int LoneOf(int atom) => Lone.GetValueOrDefault(atom);
    public int BondSum(LGraph g, int id) => g.Bonds.Where(b => b.A == id || b.B == id).Sum(b => Order(b.Id));
    public int Electrons(LGraph g, int id) => 2 * BondSum(g, id) + LoneOf(id);

    /// <summary>Its bond orders and lone electrons, as a key.</summary>
    public string Key =>
        string.Join(",", Orders.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")) + "|" + string.Join(",", Lone.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

    public LStructure Copy() => new() { Orders = new(Orders), Lone = new(Lone), Formal = new(Formal), Score = (double[])Score.Clone() };

    public bool SameElectrons(LStructure o) => Same(Orders, o.Orders) && Same(Lone, o.Lone);

    private static bool Same(Dictionary<int, int> a, Dictionary<int, int> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && v == p.Value);
}

/// <summary>An ion of an ionic compound, or the whole of a charged molecule: its atoms, drawn in brackets with its charge.</summary>
internal sealed record LIon(List<int> Atoms, int Charge);

internal sealed class LResult
{
    public List<LStructure> Structures { get; set; } = new();
    /// <summary>How many of the first are equivalent (resonance structures of the same weight).</summary>
    public int Equivalent { get; set; }
    public Dictionary<int, double> AverageOrder { get; } = new();
    public int TotalElectrons { get; set; }
    public List<LIon> Ions { get; } = new();
    public bool Ionic { get; set; }
    public List<string> Warnings { get; } = new();
    /// <summary>In place of a structure (or beside it): what a Lewis structure cannot show here.</summary>
    public List<string> Notes { get; } = new();
    public bool Drawable { get; set; } = true;

    public LStructure? Major => Structures.FirstOrDefault();
}

/// <summary>Scores compared as the Mac's lexicographicallyPrecedes: element by element, a shorter one first when it is a prefix.</summary>
internal sealed class LScoreOrder : IComparer<double[]>
{
    public static readonly LScoreOrder Instance = new();

    public int Compare(double[]? x, double[]? y)
    {
        x ??= Array.Empty<double>();
        y ??= Array.Empty<double>();
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var c = x[i].CompareTo(y[i]);
            if (c != 0) return c;
        }
        return x.Length.CompareTo(y.Length);
    }

    public static bool Equal(double[] a, double[] b) => a.Length == b.Length && a.SequenceEqual(b);
}

/// <summary>Where a molecule's electrons go (the Mac's LewisSolver).</summary>
internal static class LewisSolver
{
    public enum Mode { Octet, Formal }

    public static string Label(Mode m) => m == Mode.Octet ? "Octet rule" : "Formal charge";

    public static LResult Solve(LGraph g, Mode mode)
    {
        var r = new LResult();
        if (g.Atoms.Count == 0) return r;
        var symbols = g.Atoms.Select(a => a.Symbol).ToList();
        r.TotalElectrons = symbols.Sum(LChem.Valence) - g.Charge;

        // (what a Lewis structure cannot show: said instead)
        if (symbols.FirstOrDefault(LChem.Transition) is { } tm)
        {
            r.Notes.Add($"{LChem.Name(tm)} is a transition metal: its complexes bond through d orbitals and coordinate bonds, which Lewis structures do not describe. Ligand-field or molecular-orbital pictures do.");
            r.Drawable = false;
            return r;
        }
        var hydrogenBridges = g.Atoms.Where(a => a.Symbol == "H" && g.Degree(a.Id) >= 2).ToList();
        var isDiborane = symbols.Count(s => s == "B") == 2 && symbols.Count(s => s == "H") == 6 && symbols.Count == 8;
        if (isDiborane || (hydrogenBridges.Count > 0 && symbols.Contains("B")))
        {
            r.Notes.Add("Diborane (B₂H₆) is electron-deficient: its B–H–B bridges are three-centre two-electron bonds, which a Lewis structure, with two electrons to each bond between two atoms, cannot show.");
            r.Drawable = false;
            return r;
        }
        foreach (var h in hydrogenBridges) r.Warnings.Add($"Hydrogen can only make one bond ({g.Degree(h.Id)} drawn).");
        if (symbols.Count == 2 && symbols.All(s => s == "O"))
            r.Notes.Add("O₂'s Lewis structure pairs every electron, but oxygen is paramagnetic: it has two unpaired electrons, which molecular-orbital theory explains and a Lewis structure cannot.");

        // (ionic: each group 1 or 2 metal a cation; the rest, without them, anions)
        var metals = g.Atoms.Where(a => LChem.IonicMetal(a.Symbol)).ToList();
        var nonmetals = g.Atoms.Where(a => !LChem.IonicMetal(a.Symbol) && LChem.En(a.Symbol) >= 1.9).ToList();
        var parts = new List<(List<int> Atoms, int Charge)>();
        if (metals.Count > 0 && nonmetals.Count > 0)
        {
            r.Ionic = true;
            var cationCharge = metals.Sum(m => LChem.Valence(m.Symbol));
            foreach (var m in metals) parts.Add((new List<int> { m.Id }, LChem.Valence(m.Symbol)));
            var rest = g.Components(new HashSet<int>(metals.Select(m => m.Id)));
            var left = g.Charge - cationCharge;
            var anions = new List<(List<int> Atoms, int Charge)>();
            var poly = new List<List<int>>();
            foreach (var c in rest)
            {
                if (c.Count == 1 && g.Atom(c[0]) is { } a)
                {
                    var q = -(LChem.Target(a.Symbol) - LChem.Valence(a.Symbol));
                    anions.Add((c, q));
                    left -= q;
                }
                else poly.Add(c);
            }
            for (var k = 0; k < poly.Count; k++) anions.Add((poly[k], k == 0 ? left : 0));
            if (poly.Count == 0 && left != 0 && anions.Count > 0) anions[^1] = (anions[^1].Atoms, anions[^1].Charge + left);
            parts.AddRange(anions);
        }
        else
        {
            var comps = g.Components();
            if (comps.Count > 1) r.Warnings.Add($"Some atoms are not connected to the rest ({comps.Count} separate pieces).");
            for (var k = 0; k < comps.Count; k++) parts.Add((comps[k], k == 0 ? g.Charge : 0));
        }

        // (each part solved on its own; the structures combined part by part)
        var combined = new List<LStructure> { new() };
        foreach (var (atoms, q) in parts)
        {
            var inPart = new HashSet<int>(atoms);
            var sub = new LGraph
            {
                Atoms = g.Atoms.Where(a => inPart.Contains(a.Id)).ToList(),
                Bonds = g.Bonds.Where(b => inPart.Contains(b.A) && inPart.Contains(b.B)).ToList(),
                Charge = q, Manual = g.Manual, NextId = g.NextId,
            };
            var found = SolvePart(sub, mode);
            if (found.Count == 0)
            {
                r.Warnings.Add(g.Manual ? "These bond orders leave too few electrons, or more than an atom can hold." : "No arrangement of electrons fits: too few electrons for these bonds, or too many for the atoms to hold.");
                if (!g.Manual) r.Drawable = false;
                continue;
            }
            var next = new List<LStructure>();
            foreach (var bas in combined)
            {
                foreach (var s in found.Take(8))
                {
                    var m = bas.Copy();
                    foreach (var p in s.Orders) m.Orders[p.Key] = p.Value;
                    foreach (var p in s.Lone) m.Lone[p.Key] = p.Value;
                    foreach (var p in s.Formal) m.Formal[p.Key] = p.Value;
                    var left = bas.Score.Length == 0 ? new double[s.Score.Length] : bas.Score;
                    m.Score = left.Zip(s.Score, (x, y) => x + y).ToArray();
                    next.Add(m);
                }
            }
            combined = next.OrderBy(s => s.Score, LScoreOrder.Instance).Take(8).ToList();
            if (r.Ionic || g.Charge != 0) r.Ions.Add(new LIon(atoms, q));
        }
        r.Structures = combined.Where(s => s.Score.Length > 0).ToList();
        if (r.Structures.FirstOrDefault()?.Score is { } best) r.Equivalent = r.Structures.TakeWhile(s => LScoreOrder.Equal(s.Score, best)).Count();

        // (average bond orders over the equivalent structures)
        var eq = r.Structures.Take(Math.Max(r.Equivalent, 1)).ToList();
        foreach (var b in g.Bonds)
            if (eq.Count > 0) r.AverageOrder[b.Id] = eq.Average(s => (double)s.Order(b.Id));

        // (checks)
        if (r.Major is { } st)
        {
            foreach (var a in g.Atoms)
            {
                var e = st.Electrons(g, a.Id);
                if (LChem.Period(a.Symbol) == 2 && e > 8) r.Warnings.Add($"{a.Symbol} has {e} electrons round it: period 2 atoms hold at most eight.");
                if (e > LChem.MaxElectrons(a.Symbol) && LChem.Period(a.Symbol) > 2) r.Warnings.Add($"{a.Symbol} has {e} electrons round it, more than it can hold.");
            }
            if (g.Manual)
            {
                var placed = g.Atoms.Sum(a => st.LoneOf(a.Id)) + 2 * g.Bonds.Sum(b => st.Order(b.Id));
                if (placed != r.TotalElectrons) r.Warnings.Add($"Electrons drawn: {placed}; the molecule has {r.TotalElectrons}.");
            }
        }
        return r;
    }

    /// <summary>A part's structures, best first: the equivalent best ones, then close minor contributors.</summary>
    private static List<LStructure> SolvePart(LGraph g, Mode mode)
    {
        var total = g.Atoms.Sum(a => LChem.Valence(a.Symbol)) - g.Charge;
        if (total < 0) return new();
        var edges = g.Bonds;
        var maxBonds = new Dictionary<int, int>();
        foreach (var a in g.Atoms) maxBonds[a.Id] = LChem.MaxElectrons(a.Symbol) / 2;
        var results = new List<LStructure>();
        var orders = Enumerable.Repeat(1, edges.Count).ToArray();
        var sums = new Dictionary<int, int>();
        foreach (var e in edges)
        {
            sums[e.A] = sums.GetValueOrDefault(e.A) + 1;
            sums[e.B] = sums.GetValueOrDefault(e.B) + 1;
        }
        var tried = 0;
        void Consider()
        {
            tried++;
            var byId = new Dictionary<int, int>();
            for (var k = 0; k < edges.Count; k++) byId[edges[k].Id] = orders[k];
            if (Place(g, byId, total, mode) is { } s) results.Add(s);
        }
        // (every order each bond may take, its atoms' bond counts kept within what they can make)
        void Walk(int k, int used)
        {
            if (tried > 60000) return;
            if (k == edges.Count)
            {
                Consider();
                return;
            }
            var e = edges[k];
            var roomA = maxBonds.GetValueOrDefault(e.A, 4) - sums.GetValueOrDefault(e.A) + 1;
            var roomB = maxBonds.GetValueOrDefault(e.B, 4) - sums.GetValueOrDefault(e.B) + 1;
            var cap = Math.Min(3, Math.Min(roomA, roomB));
            for (var o = 1; o <= Math.Max(1, cap); o++)
            {
                if (2 * (used + o - 1) > total - 2 * edges.Count) break;
                orders[k] = o;
                sums[e.A] = sums.GetValueOrDefault(e.A) + o - 1;
                sums[e.B] = sums.GetValueOrDefault(e.B) + o - 1;
                Walk(k + 1, used + o - 1);
                sums[e.A] -= o - 1;
                sums[e.B] -= o - 1;
            }
            orders[k] = 1;
        }
        if (g.Manual)
        {
            orders = edges.Select(e => e.Order).ToArray();
            Consider();
        }
        else Walk(0, 0);
        var sorted = results.OrderBy(s => s.Score, LScoreOrder.Instance).ToList();
        var unique = new List<LStructure>();
        foreach (var s in sorted)
            if (!unique.Any(u => u.SameElectrons(s))) unique.Add(s);
        if (unique.Count == 0) return new();
        var best = unique[0].Score;
        var equivalent = unique.TakeWhile(s => LScoreOrder.Equal(s.Score, best)).Count();
        // (close minor contributors: the same octets, formal charges at most two more)
        var minor = unique.Skip(equivalent).Where(s => s.Score.Length > 1 && s.Score[0] == best[0] && s.Score[1] <= best[1] + 2.01);
        return unique.Take(equivalent).Concat(minor).Take(8).ToList();
    }

    /// <summary>
    /// The electrons placed round given bond orders: outer atoms first to their octets (the more electronegative first),
    /// then inner atoms, then inner atoms of period 3 and below past eight, then any atom with room; an odd electron on
    /// the least electronegative atom with room. Null if they do not fit.
    /// </summary>
    private static LStructure? Place(LGraph g, Dictionary<int, int> orders, int total, Mode mode)
    {
        var s = new LStructure { Orders = orders };
        var bonded = 2 * orders.Values.Sum();
        var left = total - bonded;
        if (left < 0) return null;
        var odd = left % 2 == 1;
        if (odd) left -= 1;
        var pairs = left / 2;
        foreach (var a in g.Atoms) s.Lone[a.Id] = 0;
        var outer = g.Atoms.Where(a => g.Degree(a.Id) <= 1).OrderByDescending(a => LChem.En(a.Symbol)).ToList();
        var inner = g.Atoms.Where(a => g.Degree(a.Id) >= 2).OrderByDescending(a => LChem.En(a.Symbol)).ToList();
        void Give(IEnumerable<LAtom> list, Func<string, int> limit)
        {
            foreach (var a in list)
            {
                if (pairs <= 0) break;
                var room = Math.Max(0, (limit(a.Symbol) - s.Electrons(g, a.Id)) / 2);
                var k = Math.Min(room, pairs);
                s.Lone[a.Id] = s.LoneOf(a.Id) + 2 * k;
                pairs -= k;
            }
        }
        Give(outer, LChem.Target);
        Give(inner, LChem.Target);
        Give(inner, LChem.MaxElectrons);
        Give(outer.Concat(inner).ToList(), LChem.MaxElectrons);
        if (pairs != 0) return null;
        if (odd)
        {
            var order = inner.OrderBy(a => LChem.En(a.Symbol)).Concat(outer.OrderBy(a => LChem.En(a.Symbol)));
            if (order.FirstOrDefault(a => s.Electrons(g, a.Id) + 1 <= LChem.MaxElectrons(a.Symbol)) is not { } a1) return null;
            s.Lone[a1.Id] = s.LoneOf(a1.Id) + 1;
        }
        foreach (var a in g.Atoms) if (s.Electrons(g, a.Id) > LChem.MaxElectrons(a.Symbol)) return null;
        foreach (var a in g.Atoms) s.Formal[a.Id] = LChem.Valence(a.Symbol) - s.LoneOf(a.Id) - s.BondSum(g, a.Id);

        // (ranked: octets kept (and, by the strict rule, none past eight); formal charges small; negative charge on the
        // more electronegative atoms; no like charges side by side)
        double octet = 0;
        foreach (var a in g.Atoms)
        {
            var e = s.Electrons(g, a.Id);
            if (e < LChem.Target(a.Symbol)) octet += 1;
            if (e > 8 && mode == Mode.Octet) octet += 1;
        }
        var fc = s.Formal;
        double absFc = fc.Values.Sum(Math.Abs);
        double placement = 0;
        foreach (var a in g.Atoms)
        {
            double q = fc.GetValueOrDefault(a.Id);
            var en = LChem.En(a.Symbol);
            if (q < 0) placement += -q * (4.0 - en);
            else if (q > 0) placement += q * en;
        }
        double like = 0;
        foreach (var b in g.Bonds)
        {
            int x = fc.GetValueOrDefault(b.A), y = fc.GetValueOrDefault(b.B);
            if (x != 0 && y != 0 && (x > 0) == (y > 0)) like += 1;
        }
        var p = Math.Round(placement * 100, MidpointRounding.AwayFromZero) / 100;
        // (octets first in both — formal charges are minimised among structures that keep them; the modes differ in
        // whether more than eight round an atom counts against it)
        s.Score = new[] { octet, absFc, like, p };
        return s;
    }
}

// ---- shapes and polarity ----

internal static class LGeometry
{
    /// <summary>An atom's bonded atoms and lone pairs (a single electron counted as one).</summary>
    public static (int Bonded, int Lone) Domains(LGraph g, LStructure s, int id)
    {
        var l = s.LoneOf(id);
        return (g.Degree(id), l / 2 + l % 2);
    }

    private static readonly Dictionary<string, double> Dipoles = new();

    /// <summary>
    /// The molecule built in 3D (MoleculeModel's VSEPR layout), for its polarity — once for each molecule and structure,
    /// not every time the builder draws.
    /// </summary>
    public static double Dipole(LGraph g, LStructure s)
    {
        var key = g.TopologyKey + "|" + s.Key;
        if (Dipoles.TryGetValue(key, out var d)) return d;
        var m = new MoleculeModel(empty: true);
        m.Load(g, s);
        if (Dipoles.Count > 300) Dipoles.Clear();
        Dipoles[key] = m.Dipole;
        return m.Dipole;
    }
}

/// <summary>The last molecule's electrons, kept: worked out again only when its atoms, bonds or charge, or the ranking, change.</summary>
internal sealed class LewisMemo
{
    private string key = "";
    private LResult last = new();

    public LResult Result(LGraph g, LewisSolver.Mode mode)
    {
        var k = g.TopologyKey + "|" + mode;
        if (k != key)
        {
            key = k;
            last = LewisSolver.Solve(g, mode);
        }
        return last;
    }
}

// ---- presets ----

internal static class LewisPresets
{
    internal sealed class Preset
    {
        public string Name { get; init; } = "";
        public int Charge { get; init; }
        /// <summary>A central atom and the atoms round it (the quick way).</summary>
        public (string Central, string[] Around)? Quick { get; init; }
        /// <summary>Atoms in order, each with the index of an earlier one it bonds to (-1 for none).</summary>
        public (string Symbol, int To)[] Atoms { get; init; } = Array.Empty<(string, int)>();
        /// <summary>Extra bonds (ring closures): two indices and an order.</summary>
        public (int A, int B, int Order)[] Extra { get; init; } = Array.Empty<(int, int, int)>();

        public LGraph Build()
        {
            var g = new LGraph();
            var ids = new List<int>();
            for (var k = 0; k < Atoms.Length; k++)
            {
                var angle = k * 1.1;
                var id = g.Add(Atoms[k].Symbol, new Point(60 * Math.Cos(angle), 60 * Math.Sin(angle)));
                ids.Add(id);
                if (Atoms[k].To >= 0 && Atoms[k].To < ids.Count - 1) g.Connect(ids[Atoms[k].To], id);
            }
            foreach (var e in Extra)
                if (e.A < ids.Count && e.B < ids.Count) g.Connect(ids[e.A], ids[e.B], e.Order);
            return g;
        }
    }

    private static Preset Q(string name, string central, string around, int charge = 0) =>
        new() { Name = name, Charge = charge, Quick = (central, around.Split(' ')) };

    private static Preset D(string name, string atoms, int charge = 0, params (int, int, int)[] extra) =>
        new()
        {
            Name = name, Charge = charge, Extra = extra,
            Atoms = atoms.Split(' ').Select(t => { var p = t.Split(':'); return (p[0], int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)); }).ToArray(),
        };

    public static readonly IReadOnlyList<Preset> All = new[]
    {
        Q("H₂O", "O", "H H"),
        Q("CO₂", "C", "O O"),
        Q("NH₃", "N", "H H H"),
        Q("CH₄", "C", "H H H H"),
        Q("HCN", "C", "H N"),
        Q("CH₂O", "C", "H H O"),
        Q("O₃", "O", "O O"),
        Q("SO₂", "S", "O O"),
        Q("NO₃⁻", "N", "O O O", -1),
        Q("CO₃²⁻", "C", "O O O", -2),
        Q("SO₄²⁻", "S", "O O O O", -2),
        Q("NH₄⁺", "N", "H H H H", 1),
        Q("BF₃", "B", "F F F"),
        Q("PCl₅", "P", "Cl Cl Cl Cl Cl"),
        Q("SF₆", "S", "F F F F F F"),
        Q("XeF₄", "Xe", "F F F F"),
        Q("ClO₂ (radical)", "Cl", "O O"),
        D("H₂", "H:-1 H:0"),
        D("N₂", "N:-1 N:0"),
        D("O₂", "O:-1 O:0"),
        D("CO", "C:-1 O:0"),
        D("HCl", "H:-1 Cl:0"),
        D("NO (radical)", "N:-1 O:0"),
        D("NO₂ (radical)", "O:-1 N:0 O:1"),
        D("C₂H₆", "C:-1 C:0 H:0 H:0 H:0 H:1 H:1 H:1"),
        D("CH₃OH", "C:-1 O:0 H:0 H:0 H:0 H:1"),
        D("H₂O₂", "O:-1 O:0 H:0 H:1"),
        D("Ethanol (C₂H₆O)", "C:-1 C:0 O:1 H:0 H:0 H:0 H:1 H:1 H:2"),
        D("Dimethyl ether (C₂H₆O)", "C:-1 O:0 C:1 H:0 H:0 H:0 H:2 H:2 H:2"),
        D("Benzene", "C:-1 C:0 C:1 C:2 C:3 C:4 H:0 H:1 H:2 H:3 H:4 H:5", 0, (5, 0, 1)),
        D("N₂O", "N:-1 N:0 O:1"),
        D("OCN⁻", "O:-1 C:0 N:1", -1),
        D("NaCl", "Na:-1 Cl:-1"),
        D("MgCl₂", "Mg:-1 Cl:-1 Cl:-1"),
        D("NaNO₃", "Na:-1 N:-1 O:1 O:1 O:1"),
        D("B₂H₆", "B:-1 H:0 B:1 H:2 H:2 H:0 H:0 H:0", 0, (7, 2, 1)),
        D("Fe(CO)₅", "Fe:-1 C:0 O:1 C:0 O:3 C:0 O:5 C:0 O:7 C:0 O:9"),
    };

    /// <summary>The presets in groups, for the dropdown.</summary>
    public static readonly IReadOnlyList<(string Title, IReadOnlyList<string> Names)> Sections = MakeSections();

    private static List<(string, IReadOnlyList<string>)> MakeSections()
    {
        string[] special = { "B₂H₆", "Fe(CO)₅" }, ionic = { "NaCl", "MgCl₂", "NaNO₃" };
        List<string> simple = new(), two = new(), bigger = new(), ions = new(), radicals = new(), ionicOut = new(), specialOut = new();
        foreach (var p in All)
        {
            if (special.Contains(p.Name)) specialOut.Add(p.Name);
            else if (ionic.Contains(p.Name)) ionicOut.Add(p.Name);
            else if (p.Name.Contains("radical")) radicals.Add(p.Name);
            else if (p.Charge != 0) ions.Add(p.Name);
            else if (p.Quick != null) simple.Add(p.Name);
            else if (p.Atoms.Length <= 2) two.Add(p.Name);
            else bigger.Add(p.Name);
        }
        var groups = new List<(string, IReadOnlyList<string>)>
        {
            ("Simple molecules", simple), ("Two atoms", two), ("Bigger molecules", bigger), ("Ions", ions),
            ("Radicals", radicals), ("Ionic compounds", ionicOut), ("Special cases", specialOut),
        };
        return groups.Where(g => g.Item2.Count > 0).ToList();
    }

    public static Preset? Named(string name) => All.FirstOrDefault(p => p.Name == name);

    /// <summary>
    /// A preset by what someone typed for it: its name as it is ("NO₃⁻"), in plain letters and digits ("NO3-", "so4 2-",
    /// "co2"), or the formula in its brackets ("C2H6O").
    /// </summary>
    public static Preset? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var want = Plain(text);
        return All.FirstOrDefault(p => Plain(p.Name) == want)
               ?? All.FirstOrDefault(p => Plain(Bare(p.Name)) == want)
               ?? All.FirstOrDefault(p => Inner(p.Name) is { } i && Plain(i) == want);
    }

    private static string Bare(string name) => name.IndexOf('(') is var i and > 0 && name.EndsWith(')') ? name[..i] : name;

    private static string? Inner(string name) => name.IndexOf('(') is var i and > 0 && name.EndsWith(')') ? name[(i + 1)..^1] : null;

    /// <summary>A name or formula in plain lower-case letters, digits and signs (sub- and superscripts as digits).</summary>
    public static string Plain(string s)
    {
        var b = new StringBuilder();
        foreach (var c in s)
        {
            var sub = "₀₁₂₃₄₅₆₇₈₉".IndexOf(c);
            var sup = "⁰¹²³⁴⁵⁶⁷⁸⁹".IndexOf(c);
            if (sub >= 0) b.Append((char)('0' + sub));
            else if (sup >= 0) b.Append((char)('0' + sup));
            else if (c is '⁺') b.Append('+');
            else if (c is '⁻' or '−' or '–') b.Append('-');
            else if (char.IsWhiteSpace(c) || c == '^') continue;
            else b.Append(char.ToLowerInvariant(c));
        }
        return b.ToString();
    }
}

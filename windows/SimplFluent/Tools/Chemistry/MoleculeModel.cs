using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Simpl.Tools;

// The 3D molecule builder's model (the Mac's MoleculeBuilder.swift, its MoleculeModel): atoms added to the one selected
// with a single, double or triple bond, hydrogens filled in, presets, and a molecule from the Lewis maker — shaped as
// VSEPR has it: each atom's bonds and lone pairs (modelled as points of their own) pushing apart while the bonds keep
// their lengths, so water bends, ammonia forms a pyramid and methane a tetrahedron.

internal sealed class MBAtom
{
    public int Id { get; init; }
    public string Symbol { get; set; } = "C";
    public Vector3D Pos { get; set; }
    /// <summary>Its lone electrons as the Lewis builder placed them (else worked out from its bonds).</summary>
    public int? Lone { get; set; }
}

internal sealed class MBBond
{
    public int Id { get; init; }
    public int A { get; set; }
    public int B { get; set; }
    public int Order { get; set; }
}

internal sealed class MBLonePair
{
    public int Atom { get; init; }
    public Vector3D Pos { get; set; }
}

internal static class MBChem
{
    /// <summary>The bond counts an atom may have (its usual first).</summary>
    public static readonly Dictionary<string, int[]> Allowed = new(StringComparer.Ordinal)
    {
        ["H"] = new[] { 1 }, ["C"] = new[] { 4 }, ["N"] = new[] { 3, 4 }, ["O"] = new[] { 2 }, ["F"] = new[] { 1 }, ["Cl"] = new[] { 1, 3, 5, 7 },
        ["Br"] = new[] { 1, 3, 5 }, ["I"] = new[] { 1, 3, 5, 7 }, ["S"] = new[] { 2, 4, 6 }, ["P"] = new[] { 3, 5 }, ["B"] = new[] { 3 },
        ["Si"] = new[] { 4 }, ["Se"] = new[] { 2, 4, 6 }, ["Xe"] = new[] { 2, 4, 6, 8 },
    };

    public static int Usual(string s) => Allowed.TryGetValue(s, out var a) ? a[0] : 4;
    public static int Most(string s) => Allowed.TryGetValue(s, out var a) ? a.Max() : 4;
    public static ChemElement? Element(string s) => ChemElements.Of(s);
    public static int Valence(string s) => LChem.Valence(s);
    public static double Covalent(string s) => (Element(s)?.Covalent ?? 75) / 100;
    public static double Vdw(string s) => (Element(s)?.VanDerWaals ?? 170) / 100;

    /// <summary>Its CPK colour (hydrogen a near-white that shows on a light page).</summary>
    public static Color ColorOf(string s)
    {
        if (s == "H") return Color.FromRgb(242, 242, 242);
        var hex = Element(s)?.Cpk is { Length: 6 } h ? h : "c8c8c8";
        var v = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromRgb((byte)((v >> 16) & 0xff), (byte)((v >> 8) & 0xff), (byte)(v & 0xff));
    }
}

/// <summary>How the model is drawn: ball and stick, space filling, or sticks.</summary>
internal enum MBStyle { BallStick, SpaceFill, Sticks }

/// <summary>
/// The molecule being built (the Mac's MoleculeModel, an ObservableObject kept by the Periodic Table for as long as the
/// tool lives): one for the app, so it is still there when the builder opens again. <see cref="Changed"/> says it changed.
/// </summary>
internal sealed class MoleculeModel
{
    private static MoleculeModel? shared;

    /// <summary>The builder's molecule (water at first).</summary>
    public static MoleculeModel Shared => shared ??= new MoleculeModel();

    public static string StyleLabel(MBStyle s) => s switch { MBStyle.BallStick => "Ball & stick", MBStyle.SpaceFill => "Space filling", _ => "Sticks" };

    public static readonly string[] Presets =
    {
        "Water", "Ammonia", "Methane", "Carbon dioxide", "Formaldehyde", "Ethylene", "Acetylene", "Ethanol",
        "Acetic acid", "Hydrogen peroxide", "Benzene", "Sulfur hexafluoride",
    };

    public static readonly IReadOnlyList<(string Title, IReadOnlyList<string> Names)> PresetSections = new (string, IReadOnlyList<string>)[]
    {
        ("Small molecules", new[] { "Water", "Ammonia", "Methane", "Carbon dioxide", "Formaldehyde", "Hydrogen peroxide", "Sulfur hexafluoride" }),
        ("Carbon compounds", new[] { "Ethylene", "Acetylene", "Ethanol", "Acetic acid", "Benzene" }),
    };

    /// <summary>The presets' formulas, so a preset can be asked for by its formula as well as its name.</summary>
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["Water"] = new[] { "H2O" }, ["Ammonia"] = new[] { "NH3", "H3N" }, ["Methane"] = new[] { "CH4" }, ["Carbon dioxide"] = new[] { "CO2" },
        ["Formaldehyde"] = new[] { "CH2O", "H2CO" }, ["Ethylene"] = new[] { "C2H4", "Ethene" }, ["Acetylene"] = new[] { "C2H2", "Ethyne" },
        ["Ethanol"] = new[] { "C2H6O", "C2H5OH", "CH3CH2OH" }, ["Acetic acid"] = new[] { "C2H4O2", "CH3COOH" },
        ["Hydrogen peroxide"] = new[] { "H2O2" }, ["Benzene"] = new[] { "C6H6" }, ["Sulfur hexafluoride"] = new[] { "SF6" },
    };

    public List<MBAtom> Atoms { get; private set; } = new();
    public List<MBBond> Bonds { get; private set; } = new();
    public List<MBLonePair> LonePairs { get; private set; } = new();
    private int? selected;
    private int bondOrder = 1;
    private MBStyle style = MBStyle.BallStick;
    private bool showLonePairs = true, showLabels = true;
    // (on from the start: the model turns slowly until it is switched off — still when Windows' animation effects are off)
    private bool autoRotate = Kit.Motion.On;
    private string? message;
    private int nextId = 1;

    /// <summary>Anything about it changed (its atoms, the view's choices, the message).</summary>
    public event Action? Changed;

    public string Title { get; private set; } = "";
    /// <summary>Raised when the model is new (a preset, the Lewis maker's): the view frames it afresh.</summary>
    public int Framing { get; private set; }

    public int? Selected { get => selected; set { selected = value; Notify(); } }
    public int BondOrder { get => bondOrder; set { bondOrder = value; Notify(); } }
    public MBStyle Style { get => style; set { style = value; Notify(); } }
    public bool ShowLonePairs { get => showLonePairs; set { showLonePairs = value; Notify(); } }
    public bool ShowLabels { get => showLabels; set { showLabels = value; Notify(); } }
    public bool AutoRotate { get => autoRotate; set { autoRotate = value; Notify(); } }
    public string? Message { get => message; private set => message = value; }

    public MoleculeModel(bool empty = false) { if (!empty) LoadPreset("Water"); }

    private void Notify() => Changed?.Invoke();

    // ---- questions ----

    public MBAtom? Atom(int id) => Atoms.FirstOrDefault(a => a.Id == id);
    public int BondSum(int id) => Bonds.Where(b => b.A == id || b.B == id).Sum(b => b.Order);
    public List<int> Neighbours(int id) => Bonds.Where(b => b.A == id || b.B == id).Select(b => b.A == id ? b.B : b.A).ToList();

    public int LonePairCount(MBAtom a)
    {
        if (a.Lone is { } l) return l / 2;
        if (a.Symbol is "H" or "B") return 0;
        return Math.Max(0, (MBChem.Valence(a.Symbol) - BondSum(a.Id)) / 2);
    }

    /// <summary>Hill order: carbon, hydrogen, then the rest by symbol (all by symbol with no carbon).</summary>
    public string Formula
    {
        get
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var a in Atoms) counts[a.Symbol] = counts.GetValueOrDefault(a.Symbol) + 1;
            var keys = counts.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var order = counts.ContainsKey("C") ? new[] { "C", "H" }.Where(counts.ContainsKey).Concat(keys.Where(k => k != "C" && k != "H")).ToList() : keys;
            return string.Concat(order.Select(k => k + LChem.Subscripted(counts[k])));
        }
    }

    public double MolarMass => Atoms.Select(a => MBChem.Element(a.Symbol)?.Mass ?? 0).Sum();

    /// <summary>Atoms whose bonds are not one of their allowed counts.</summary>
    public List<string> ValenceIssues => Atoms.Select(a =>
    {
        var n = BondSum(a.Id);
        var ok = MBChem.Allowed.TryGetValue(a.Symbol, out var al) ? al : new[] { MBChem.Usual(a.Symbol) };
        if (ok.Contains(n)) return null;
        return $"{a.Symbol} has {n} bond{(n == 1 ? "" : "s")}, wants {string.Join(" or ", ok)}";
    }).OfType<string>().ToList();

    /// <summary>The dipole: each bond toward its more electronegative atom, each lone pair outward; polar if they do not cancel.</summary>
    public double Dipole
    {
        get
        {
            var v = new Vector3D();
            foreach (var b in Bonds)
            {
                if (Atom(b.A) is not { } x || Atom(b.B) is not { } y) continue;
                var d = y.Pos - x.Pos;
                var len = d.Length;
                if (len <= 1e-6) continue;
                v += d / len * ((MBChem.Element(y.Symbol)?.En ?? 2.5) - (MBChem.Element(x.Symbol)?.En ?? 2.5));
            }
            foreach (var p in LonePairs)
            {
                if (Atom(p.Atom) is not { } a) continue;
                var d = p.Pos - a.Pos;
                var len = d.Length;
                if (len > 1e-6) v += d / len * 0.6;
            }
            return v.Length;
        }
    }

    /// <summary>The angles between an atom's bonds, as the model has them ("H–O–H 104.6°").</summary>
    public List<string> Angles(int id)
    {
        if (Atom(id) is not { } c) return new();
        var ns = Neighbours(id).Select(Atom).OfType<MBAtom>().ToList();
        var outs = new HashSet<string>();
        for (var i = 0; i < ns.Count; i++)
        {
            for (var j = i + 1; j < ns.Count; j++)
            {
                var u = Unit(ns[i].Pos - c.Pos);
                var v = Unit(ns[j].Pos - c.Pos);
                var a = Math.Acos(Math.Clamp(Vector3D.DotProduct(u, v), -1, 1)) * 180 / Math.PI;
                outs.Add($"{ns[i].Symbol}–{c.Symbol}–{ns[j].Symbol} {a.ToString("0.0", CultureInfo.InvariantCulture)}°");
            }
        }
        return outs.OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    /// <summary>The shape round an atom, by its bonded atoms and lone pairs.</summary>
    public string Shape(int id) => Atom(id) is { } a ? LChem.Shape(Neighbours(id).Count, LonePairCount(a)).Name : "";

    internal static Vector3D Unit(Vector3D v)
    {
        var l = v.Length;
        return l > 0 ? v / l : new Vector3D(double.NaN, double.NaN, double.NaN);
    }

    // ---- changes ----

    public void Select(int? id)
    {
        selected = selected == id ? null : id;
        Notify();
    }

    /// <summary>An atom bonded to the selected one (or the first; or alone, the molecule empty).</summary>
    public void Add(string symbol)
    {
        message = null;
        if (Atoms.Count == 0)
        {
            var a = new MBAtom { Id = nextId, Symbol = symbol, Pos = new Vector3D() };
            nextId++;
            Atoms = new List<MBAtom> { a };
            selected = a.Id;
            Relax();
            Notify();
            return;
        }
        if (Atom(selected ?? Atoms[0].Id) is not { } anchor) return;
        var order = bondOrder;
        if (BondSum(anchor.Id) + order > MBChem.Most(anchor.Symbol))
        {
            message = $"{MBChem.Element(anchor.Symbol)?.Name ?? anchor.Symbol} has no room for another {(order == 1 ? "" : order == 2 ? "double " : "triple ")}bond.";
            Notify();
            return;
        }
        if (order > MBChem.Most(symbol))
        {
            message = $"{MBChem.Element(symbol)?.Name ?? symbol} cannot make a {(order == 2 ? "double" : "triple")} bond.";
            Notify();
            return;
        }
        Place(symbol, anchor.Id, order);
        Relax();
        Notify();
    }

    private int Place(string symbol, int anchorId, int order)
    {
        if (Atom(anchorId) is not { } anchor) return -1;
        // (out the way the anchor's other bonds leave open)
        var away = new Vector3D();
        foreach (var n in Neighbours(anchorId))
            if (Atom(n) is { } x) away -= Unit(x.Pos - anchor.Pos + new Vector3D(1e-6, 0, 0));
        double seed = nextId;
        var jitter = new Vector3D(Math.Sin(seed * 1.7), Math.Cos(seed * 2.3), Math.Sin(seed * 3.1)) * 0.35;
        var dir = away + jitter;
        if (dir.Length < 1e-3) dir = new Vector3D(1, 0.2, 0.1);
        dir = Unit(dir);
        var length = (MBChem.Covalent(anchor.Symbol) + MBChem.Covalent(symbol)) * BondFactor(order);
        var a = new MBAtom { Id = nextId, Symbol = symbol, Pos = anchor.Pos + dir * length };
        nextId++;
        Atoms.Add(a);
        Bonds.Add(new MBBond { Id = nextId, A = anchorId, B = a.Id, Order = order });
        nextId++;
        return a.Id;
    }

    public void FillHydrogen()
    {
        FillHydrogenQuietly();
        Notify();
    }

    private void FillHydrogenQuietly()
    {
        message = null;
        foreach (var a in Atoms.ToList())
        {
            if (a.Symbol == "H") continue;
            var room = MBChem.Usual(a.Symbol) - BondSum(a.Id);
            while (room > 0)
            {
                Place("H", a.Id, 1);
                room--;
            }
        }
        Relax();
    }

    public void DeleteSelected()
    {
        if (selected is not { } id) return;
        Atoms.RemoveAll(a => a.Id == id);
        Bonds.RemoveAll(b => b.A == id || b.B == id);
        selected = null;
        message = null;
        Relax();
        Notify();
    }

    public void Clear()
    {
        ClearQuietly();
        Notify();
    }

    private void ClearQuietly()
    {
        Atoms = new();
        Bonds = new();
        LonePairs = new();
        selected = null;
        message = null;
        Title = "";
        Framing++;
    }

    // ---- presets and the Lewis maker ----

    public void LoadPreset(string name)
    {
        ClearQuietly();
        Title = name;
        List<int> Chain(params (string Symbol, int Order)[] spec)
        {
            var ids = new List<int>();
            for (var k = 0; k < spec.Length; k++)
            {
                if (k == 0)
                {
                    var a = new MBAtom { Id = nextId, Symbol = spec[0].Symbol, Pos = new Vector3D() };
                    nextId++;
                    Atoms.Add(a);
                    ids.Add(a.Id);
                }
                else ids.Add(Place(spec[k].Symbol, ids[k - 1], spec[k].Order));
            }
            return ids;
        }
        switch (name)
        {
            case "Water": Chain(("O", 0)); break;
            case "Ammonia": Chain(("N", 0)); break;
            case "Methane": Chain(("C", 0)); break;
            case "Carbon dioxide":
            {
                var c = Chain(("C", 0));
                Place("O", c[0], 2);
                Place("O", c[0], 2);
                break;
            }
            case "Formaldehyde": Chain(("C", 0), ("O", 2)); break;
            case "Ethylene": Chain(("C", 0), ("C", 2)); break;
            case "Acetylene": Chain(("C", 0), ("C", 3)); break;
            case "Ethanol": Chain(("C", 0), ("C", 1), ("O", 1)); break;
            case "Acetic acid":
            {
                var c = Chain(("C", 0), ("C", 1), ("O", 1));
                Place("O", c[1], 2);
                break;
            }
            case "Hydrogen peroxide": Chain(("O", 0), ("O", 1)); break;
            case "Benzene":
            {
                var ring = new List<int>();
                for (var k = 0; k < 6; k++)
                {
                    var t = k * Math.PI / 3;
                    var atom = new MBAtom { Id = nextId, Symbol = "C", Pos = new Vector3D(1.4 * Math.Cos(t), 1.4 * Math.Sin(t), 0) };
                    nextId++;
                    Atoms.Add(atom);
                    ring.Add(atom.Id);
                }
                for (var k = 0; k < 6; k++)
                {
                    Bonds.Add(new MBBond { Id = nextId, A = ring[k], B = ring[(k + 1) % 6], Order = k % 2 == 0 ? 2 : 1 });
                    nextId++;
                }
                // (each hydrogen straight out from its carbon, in the ring's plane)
                for (var k = 0; k < 6; k++)
                {
                    var t = k * Math.PI / 3;
                    var h = new MBAtom { Id = nextId, Symbol = "H", Pos = new Vector3D(2.48 * Math.Cos(t), 2.48 * Math.Sin(t), 0) };
                    nextId++;
                    Atoms.Add(h);
                    Bonds.Add(new MBBond { Id = nextId, A = ring[k], B = h.Id, Order = 1 });
                    nextId++;
                }
                break;
            }
            case "Sulfur hexafluoride":
            {
                var s = Chain(("S", 0));
                for (var k = 0; k < 6; k++) Place("F", s[0], 1);
                break;
            }
        }
        if (name != "Benzene" && name != "Sulfur hexafluoride" && name != "Carbon dioxide") FillHydrogenQuietly();
        selected = Atoms.FirstOrDefault()?.Id;
        Relax(900);
        Framing++;
        Notify();
    }

    /// <summary>
    /// The Lewis builder's molecule: its atoms where they were drawn (lifted into 3D), its bonds with their orders, each
    /// atom's lone electrons as placed — then shaped.
    /// </summary>
    public void Load(LGraph g, LStructure st)
    {
        ClearQuietly();
        Title = LChem.Formula(g.Atoms.Select(a => a.Symbol), g.Charge);
        var map = new Dictionary<int, int>();
        foreach (var a in g.Atoms)
        {
            var z = Math.Sin(a.Id * 1.7) * 0.4;
            Atoms.Add(new MBAtom { Id = nextId, Symbol = a.Symbol, Pos = new Vector3D(a.Pos.X / 55, -a.Pos.Y / 55, z), Lone = st.LoneOf(a.Id) });
            map[a.Id] = nextId;
            nextId++;
        }
        foreach (var b in g.Bonds)
        {
            if (!map.TryGetValue(b.A, out var x) || !map.TryGetValue(b.B, out var y)) continue;
            Bonds.Add(new MBBond { Id = nextId, A = x, B = y, Order = st.Orders.TryGetValue(b.Id, out var o) ? o : b.Order });
            nextId++;
        }
        selected = Atoms.FirstOrDefault()?.Id;
        Relax(900);
        Framing++;
        Notify();
    }

    /// <summary>
    /// Begun from what someone asked for (the Periodic Table, a launch flag): a preset by its name or formula ("Methane",
    /// "CH4"); else a Lewis preset ("XeF4", "NO3-") with its electrons placed; else an element, with hydrogens filled in.
    /// False when it is none of those.
    /// </summary>
    public bool Start(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var want = LewisPresets.Plain(text);
        var preset = Presets.FirstOrDefault(p => LewisPresets.Plain(p) == want || Aliases.TryGetValue(p, out var al) && al.Any(x => LewisPresets.Plain(x) == want));
        if (preset != null) { LoadPreset(preset); return true; }
        if (LewisPresets.Find(text) is { } lp)
        {
            var g = LewisState.GraphOf(lp);
            if (LewisSolver.Solve(g, LewisSolver.Mode.Formal) is { Drawable: true, Major: { } major } && g.Atoms.Count > 1)
            {
                Load(g, major);
                return true;
            }
        }
        var symbol = text.Trim();
        if (ChemElements.Of(symbol) is { } e)
        {
            ClearQuietly();
            Title = e.Name;
            Atoms.Add(new MBAtom { Id = nextId, Symbol = e.Symbol, Pos = new Vector3D() });
            nextId++;
            // (hydrogens only on an element the builder knows the bonds of: a metal or a noble gas stays alone)
            if (MBChem.Allowed.ContainsKey(e.Symbol)) FillHydrogenQuietly();
            selected = Atoms[0].Id;
            Relax(900);
            Notify();
            return true;
        }
        return false;
    }

    // ---- the shape ----

    private static double BondFactor(int order) => order >= 3 ? 0.78 : (order == 2 ? 0.87 : 1);

    /// <summary>
    /// VSEPR by forces: bonds springs at their lengths; round each atom, its bonds and lone pairs pushing apart (lone pairs
    /// harder); atoms not bonded kept from crowding. The lone pairs are kept for drawing.
    /// </summary>
    public void Relax(int iterations = 500)
    {
        var n = Atoms.Count;
        if (n == 0) { LonePairs = new(); return; }
        var p = Atoms.Select(a => a.Pos).ToArray();
        var index = new Dictionary<int, int>();
        for (var i = 0; i < n; i++) index[Atoms[i].Id] = i;
        var lp = new List<MBLonePair>();
        foreach (var a in Atoms)
        {
            var k = LonePairCount(a);
            for (var j = 0; j < k; j++)
            {
                double t = a.Id * 7 + j * 3;
                var d = Unit(new Vector3D(Math.Sin(t), Math.Cos(t * 1.3), Math.Sin(t * 0.7) + 0.3));
                lp.Add(new MBLonePair { Atom = a.Id, Pos = a.Pos + d * 0.55 });
            }
        }
        var q = lp.Select(x => x.Pos).ToArray();
        var lpOf = new Dictionary<int, List<int>>();
        for (var k = 0; k < lp.Count; k++)
        {
            if (!lpOf.TryGetValue(lp[k].Atom, out var l)) lpOf[lp[k].Atom] = l = new List<int>();
            l.Add(k);
        }
        var pairs = new List<(int I, int J, double Target)>();
        foreach (var b in Bonds)
        {
            if (!index.TryGetValue(b.A, out var i) || !index.TryGetValue(b.B, out var j)) continue;
            pairs.Add((i, j, (MBChem.Covalent(Atoms[i].Symbol) + MBChem.Covalent(Atoms[j].Symbol)) * BondFactor(b.Order)));
        }
        var bonded = new HashSet<int>();
        var neighbours = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        foreach (var (i, j, _) in pairs)
        {
            bonded.Add(i * 10000 + j);
            bonded.Add(j * 10000 + i);
            if (!neighbours[i].Contains(j)) neighbours[i].Add(j);
            if (!neighbours[j].Contains(i)) neighbours[j].Add(i);
        }
        var f = new Vector3D[n];
        var g = new Vector3D[q.Length];
        for (var step = 0; step < iterations; step++)
        {
            Array.Clear(f);
            Array.Clear(g);
            foreach (var (i, j, target) in pairs)
            {
                var d = p[j] - p[i];
                var len = Math.Max(d.Length, 1e-6);
                var force = d / len * (len - target) * 0.8;
                f[i] += force;
                f[j] -= force;
            }
            for (var k = 0; k < q.Length; k++)
            {
                if (!index.TryGetValue(lp[k].Atom, out var a)) continue;
                var d = q[k] - p[a];
                var len = Math.Max(d.Length, 1e-6);
                var force = d / len * (len - 0.55) * 0.9;
                g[k] -= force;
                f[a] += force;
            }
            // (round each atom: its domains — bonded atoms and lone pairs — as directions, pushed apart)
            for (var c = 0; c < n; c++)
            {
                var dirs = neighbours[c].Select(x => (Dir: p[x] - p[c], Lone: false, Index: x)).ToList();
                if (lpOf.TryGetValue(Atoms[c].Id, out var mine)) foreach (var k in mine) dirs.Add((q[k] - p[c], true, k));
                if (dirs.Count <= 1) continue;
                var units = dirs.Select(x => Unit(x.Dir + new Vector3D(1e-9, 0, 0))).ToArray();
                for (var x = 0; x < dirs.Count; x++)
                {
                    for (var y = x + 1; y < dirs.Count; y++)
                    {
                        var diff = units[x] - units[y];
                        var weight = (dirs[x].Lone ? 1.25 : 1) * (dirs[y].Lone ? 1.25 : 1);
                        var push = diff / (diff.LengthSquared + 0.05) * 0.22 * weight;
                        if (dirs[x].Lone) g[dirs[x].Index] += push * 0.55; else f[dirs[x].Index] += push * dirs[x].Dir.Length;
                        if (dirs[y].Lone) g[dirs[y].Index] -= push * 0.55; else f[dirs[y].Index] -= push * dirs[y].Dir.Length;
                    }
                }
            }
            // (atoms not bonded kept apart)
            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    if (bonded.Contains(i * 10000 + j)) continue;
                    var d = p[j] - p[i];
                    var len = Math.Max(d.Length, 0.3);
                    if (len < 3.2)
                    {
                        var force = d / len * (0.25 / (len * len));
                        f[i] -= force;
                        f[j] += force;
                    }
                }
            }
            var rate = step < iterations / 2 ? 0.08 : 0.04;
            for (var i = 0; i < n; i++)
            {
                var m = f[i] * rate;
                if (m.Length > 0.2) m = Unit(m) * 0.2;
                p[i] += m;
            }
            for (var k = 0; k < q.Length; k++)
            {
                var m = g[k] * rate;
                if (m.Length > 0.2) m = Unit(m) * 0.2;
                q[k] += m;
            }
        }
        // (centred on the middle)
        var centre = new Vector3D();
        foreach (var x in p) centre += x;
        centre /= n;
        for (var i = 0; i < n; i++) Atoms[i].Pos = p[i] - centre;
        for (var k = 0; k < lp.Count; k++) lp[k].Pos = q[k] - centre;
        LonePairs = lp;
    }
}

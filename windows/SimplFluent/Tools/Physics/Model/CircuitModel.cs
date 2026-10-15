using System.Globalization;
using System.Text.Json.Serialization;

namespace Simpl.Tools;

// The Circuit Builder's maths (0.3.14), the Mac's CircuitModel line for line: a circuit as its parts on a grid and the
// wires between their terminals, saved as JSON; the nodes the terminals make; the DC solver (modified nodal analysis:
// node voltages and the current through every voltage source, resistors by their conductance, an ammeter, a wire and a
// closed switch as 0 V sources so their current is read, a voltmeter as a gigaohm) with what it reads on every part;
// the quick-build circuits; and the calculator strip's Ohm's law and series/parallel sums. Plain .NET, no WPF, so the
// unit tests check it on any machine.

/// <summary>A part in the palette.</summary>
public enum PartKind { Battery, Resistor, Wire, Switch, Lamp, Ammeter, Voltmeter, Ground }

/// <summary>
/// A part placed on the grid: where its middle is (in cells), its quarter turns clockwise, its value (volts for a
/// battery, ohms for a resistor or a lamp), and whether a switch is closed. A two-terminal part spans four cells, its
/// terminals two cells either side of its middle (terminal 0 to the left, or on top once turned; a battery's + is
/// terminal 0); a ground has one terminal a cell above it.
/// </summary>
public sealed class CircuitPart
{
    public int Id { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PartKind Kind { get; set; }
    public int Col { get; set; }
    public int Row { get; set; }
    public int Turn { get; set; }
    public double Value { get; set; }
    public bool Closed { get; set; } = true;
}

/// <summary>A wire from one part's terminal to another's, with the corner it turns at (none: straight).</summary>
public sealed class CircuitLink
{
    public int A { get; set; }
    public int ATerminal { get; set; }
    public int B { get; set; }
    public int BTerminal { get; set; }
    public int? CornerCol { get; set; }
    public int? CornerRow { get; set; }

    public bool Joins(int part, int terminal) => (A == part && ATerminal == terminal) || (B == part && BTerminal == terminal);
    public bool Touches(int part) => A == part || B == part;
}

/// <summary>A circuit: its parts and the wires between them (what the tool saves and loads, as JSON).</summary>
public sealed class CircuitDoc
{
    public List<CircuitPart> Parts { get; set; } = new();
    public List<CircuitLink> Links { get; set; } = new();

    public CircuitPart? Part(int id) => Parts.FirstOrDefault(p => p.Id == id);

    public int NextId => Parts.Count == 0 ? 1 : Parts.Max(p => p.Id) + 1;

    /// <summary>A part added at a cell, with its kind's usual value.</summary>
    public CircuitPart Add(PartKind kind, int col, int row, int turn = 0, double? value = null, bool closed = true)
    {
        var p = new CircuitPart { Id = NextId, Kind = kind, Col = col, Row = row, Turn = turn & 3, Value = value ?? CircuitParts.DefaultValue(kind), Closed = closed };
        Parts.Add(p);
        return p;
    }

    /// <summary>A part taken out, with every wire to it.</summary>
    public void Remove(int id)
    {
        Parts.RemoveAll(p => p.Id == id);
        Links.RemoveAll(l => l.Touches(id));
    }

    /// <summary>A wire between two terminals (not the same one, and not one already there); null when it can't be.</summary>
    public CircuitLink? Connect(int a, int aTerminal, int b, int bTerminal, (int Col, int Row)? corner = null)
    {
        if (a == b && aTerminal == bTerminal) return null;
        if (Links.Any(l => (l.A == a && l.ATerminal == aTerminal && l.B == b && l.BTerminal == bTerminal) || (l.A == b && l.ATerminal == bTerminal && l.B == a && l.BTerminal == aTerminal))) return null;
        var link = new CircuitLink { A = a, ATerminal = aTerminal, B = b, BTerminal = bTerminal, CornerCol = corner?.Col, CornerRow = corner?.Row };
        Links.Add(link);
        return link;
    }

    /// <summary>"Battery 1", "Resistor 2": each part named by its kind and its place among its kind.</summary>
    public string NameOf(CircuitPart part)
    {
        var n = Parts.Where(p => p.Kind == part.Kind).TakeWhile(p => p.Id != part.Id).Count() + 1;
        return $"{CircuitParts.Name(part.Kind)} {n}";
    }
}

/// <summary>What each kind of part is and where its terminals are.</summary>
public static class CircuitParts
{
    public static readonly PartKind[] Palette = { PartKind.Battery, PartKind.Resistor, PartKind.Wire, PartKind.Switch, PartKind.Lamp, PartKind.Ammeter, PartKind.Voltmeter, PartKind.Ground };

    public static string Name(PartKind k) => k switch
    {
        PartKind.Battery => "Battery", PartKind.Resistor => "Resistor", PartKind.Wire => "Wire", PartKind.Switch => "Switch",
        PartKind.Lamp => "Lamp", PartKind.Ammeter => "Ammeter", PartKind.Voltmeter => "Voltmeter", _ => "Ground",
    };

    /// <summary>What the part's value is: volts, ohms, or nothing to set.</summary>
    public static string? Unit(PartKind k) => k switch { PartKind.Battery => "V", PartKind.Resistor or PartKind.Lamp => "Ω", _ => null };

    public static double DefaultValue(PartKind k) => k switch { PartKind.Battery => 9, PartKind.Resistor => 100, PartKind.Lamp => 50, _ => 0 };

    /// <summary>The power at which a lamp is at its brightest (the drawing's glow is its share of this).</summary>
    public const double LampFullWatts = 0.5;

    /// <summary>A voltmeter's own resistance, so it reads the voltage and takes next to no current.</summary>
    public const double VoltmeterOhms = 1e9;

    public static bool TwoTerminals(PartKind k) => k != PartKind.Ground;

    /// <summary>The direction a part's terminal 1 lies from its middle, by its turn: right, down, left, up.</summary>
    public static (int DCol, int DRow) Direction(int turn) => (turn & 3) switch { 0 => (1, 0), 1 => (0, 1), 2 => (-1, 0), _ => (0, -1) };

    /// <summary>The cells a part's terminals sit on: two for most parts, one for a ground (above it, turning with it).</summary>
    public static (int Col, int Row)[] Terminals(CircuitPart p)
    {
        var (dc, dr) = Direction(p.Turn);
        if (p.Kind == PartKind.Ground)
        {
            var (uc, ur) = Direction(p.Turn + 3);
            return new[] { (p.Col + uc, p.Row + ur) };
        }
        return new[] { (p.Col - 2 * dc, p.Row - 2 * dr), (p.Col + 2 * dc, p.Row + 2 * dr) };
    }

    /// <summary>The cells a part's body covers (its terminals left out): where a wire must not be routed through.</summary>
    public static IEnumerable<(int Col, int Row)> Body(CircuitPart p)
    {
        if (p.Kind == PartKind.Ground) { yield return (p.Col, p.Row); yield break; }
        var (dc, dr) = Direction(p.Turn);
        for (var i = -1; i <= 1; i++) yield return (p.Col + i * dc, p.Row + i * dr);
    }

    /// <summary>The corner a wire turns at between two terminals that are not in line: across first, then down, unless
    /// that corner lies on a part; then down first.</summary>
    public static (int Col, int Row)? Corner(CircuitDoc doc, (int Col, int Row) a, (int Col, int Row) b)
    {
        if (a.Col == b.Col || a.Row == b.Row) return null;
        var bodies = doc.Parts.SelectMany(Body).ToHashSet();
        var first = (b.Col, a.Row);
        return bodies.Contains(first) ? (a.Col, b.Row) : first;
    }
}

/// <summary>What a part reads: the voltage across it (terminal 0 less terminal 1), the current through it from terminal
/// 0 to terminal 1, and the power.</summary>
public sealed record PartReading(double Volts, double Amps, double Watts)
{
    /// <summary>A lamp's glow, 0 to 1: its power's share of what lights it fully.</summary>
    public double Brightness => Math.Clamp(Math.Abs(Watts) / CircuitParts.LampFullWatts, 0, 1);
}

/// <summary>The circuit solved: a reading for every part, or why it can't be, and how many parts no battery reaches.</summary>
public sealed class CircuitSolution
{
    public Dictionary<int, PartReading> Readings { get; } = new();
    /// <summary>Why the circuit can't be solved (a shorted battery, batteries against each other), or null.</summary>
    public string? Problem { get; set; }
    /// <summary>Parts in no circuit with a battery.</summary>
    public int Unpowered { get; set; }

    public PartReading Of(int id) => Readings.TryGetValue(id, out var r) ? r : new PartReading(0, 0, 0);
}

/// <summary>The DC solver: modified nodal analysis over the nodes the terminals and wires make.</summary>
public static class CircuitSolver
{
    private const double Tiny = 1e-13;

    /// <summary>The node each terminal is in: terminals on one cell are joined, and so are the two ends of every wire.</summary>
    public static Dictionary<(int Part, int Terminal), int> Nodes(CircuitDoc doc)
    {
        var keys = new List<(int Part, int Terminal)>();
        var index = new Dictionary<(int, int), int>();
        foreach (var p in doc.Parts)
            for (var t = 0; t < CircuitParts.Terminals(p).Length; t++) { index[(p.Id, t)] = keys.Count; keys.Add((p.Id, t)); }
        var parent = Enumerable.Range(0, keys.Count).ToArray();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        var byCell = new Dictionary<(int, int), int>();
        foreach (var p in doc.Parts)
        {
            var ts = CircuitParts.Terminals(p);
            for (var t = 0; t < ts.Length; t++)
            {
                var i = index[(p.Id, t)];
                if (byCell.TryGetValue(ts[t], out var other)) Union(i, other); else byCell[ts[t]] = i;
            }
        }
        foreach (var l in doc.Links)
            if (index.TryGetValue((l.A, l.ATerminal), out var a) && index.TryGetValue((l.B, l.BTerminal), out var b)) Union(a, b);
        // (nodes numbered in the order they are met)
        var number = new Dictionary<int, int>();
        var nodes = new Dictionary<(int Part, int Terminal), int>();
        foreach (var k in keys)
        {
            var root = Find(index[k]);
            if (!number.TryGetValue(root, out var n)) { n = number.Count; number[root] = n; }
            nodes[k] = n;
        }
        return nodes;
    }

    public static CircuitSolution Solve(CircuitDoc doc)
    {
        var s = new CircuitSolution();
        var nodes = Nodes(doc);
        // (the parts that join their two nodes: everything but an open switch; a ground joins nothing)
        var edges = new List<(CircuitPart Part, int A, int B)>();
        foreach (var p in doc.Parts)
        {
            if (!CircuitParts.TwoTerminals(p.Kind)) continue;
            if (p.Kind == PartKind.Switch && !p.Closed) continue;
            edges.Add((p, nodes[(p.Id, 0)], nodes[(p.Id, 1)]));
        }
        // (the circuit may be several circuits: each solved on its own)
        var nodeCount = nodes.Count == 0 ? 0 : nodes.Values.Max() + 1;
        var comp = Enumerable.Repeat(-1, nodeCount).ToArray();
        var adj = new List<int>[nodeCount];
        for (var i = 0; i < nodeCount; i++) adj[i] = new List<int>();
        foreach (var (_, a, b) in edges) { adj[a].Add(b); adj[b].Add(a); }
        var components = 0;
        for (var i = 0; i < nodeCount; i++)
        {
            if (comp[i] >= 0) continue;
            var stack = new Stack<int>();
            stack.Push(i);
            comp[i] = components;
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                foreach (var m in adj[n]) if (comp[m] < 0) { comp[m] = components; stack.Push(m); }
            }
            components++;
        }
        var grounds = doc.Parts.Where(p => p.Kind == PartKind.Ground).Select(p => nodes[(p.Id, 0)]).ToList();
        for (var c = 0; c < components; c++)
        {
            var mine = edges.Where(e => comp[e.A] == c).ToList();
            if (mine.Count == 0) continue;
            var battery = mine.FirstOrDefault(e => e.Part.Kind == PartKind.Battery);
            if (battery.Part == null)
            {
                s.Unpowered += mine.Count;
                foreach (var e in mine) s.Readings[e.Part.Id] = new PartReading(0, 0, 0);
                continue;
            }
            // (a battery with both ends in one node is shorted: nothing to solve)
            if (mine.Any(e => e.Part.Kind == PartKind.Battery && e.A == e.B))
            {
                s.Problem = "Can't solve: a battery is shorted — a wire runs straight across it.";
                continue;
            }
            var reference = grounds.FirstOrDefault(g => comp[g] == c, -1);
            if (reference < 0) reference = battery.B;
            var index = new Dictionary<int, int>();
            for (var n = 0; n < nodeCount; n++) if (comp[n] == c && n != reference) index[n] = index.Count;
            var sources = mine.Where(e => e.Part.Kind is PartKind.Battery or PartKind.Wire or PartKind.Switch or PartKind.Ammeter || (e.Part.Kind is PartKind.Resistor or PartKind.Lamp && e.Part.Value <= 0)).ToList();
            var n0 = index.Count;
            var size = n0 + sources.Count;
            var a = new double[size, size];
            var z = new double[size];
            int Row(int node) => node == reference ? -1 : index[node];
            foreach (var (p, na, nb) in mine)
            {
                if (sources.Any(e => e.Part.Id == p.Id)) continue;
                var g = p.Kind == PartKind.Voltmeter ? 1 / CircuitParts.VoltmeterOhms : 1 / p.Value;
                int i = Row(na), j = Row(nb);
                if (i >= 0) a[i, i] += g;
                if (j >= 0) a[j, j] += g;
                if (i >= 0 && j >= 0) { a[i, j] -= g; a[j, i] -= g; }
            }
            for (var k = 0; k < sources.Count; k++)
            {
                var (p, na, nb) = sources[k];
                int i = Row(na), j = Row(nb), row = n0 + k;
                if (i >= 0) { a[i, row] += 1; a[row, i] += 1; }
                if (j >= 0) { a[j, row] -= 1; a[row, j] -= 1; }
                z[row] = p.Kind == PartKind.Battery ? p.Value : 0;
            }
            var x = Gauss(a, z, size);
            if (x == null)
            {
                s.Problem = "Can't solve: two batteries are wired straight against each other, or a battery is shorted.";
                continue;
            }
            double V(int node) => node == reference ? 0 : x[index[node]];
            foreach (var (p, na, nb) in mine)
            {
                var volts = V(na) - V(nb);
                var k = sources.FindIndex(e => e.Part.Id == p.Id);
                var amps = k >= 0 ? x[n0 + k] : volts * (p.Kind == PartKind.Voltmeter ? 1 / CircuitParts.VoltmeterOhms : 1 / p.Value);
                s.Readings[p.Id] = new PartReading(Clean(volts), Clean(amps), Clean(volts * amps));
            }
        }
        // (an open switch reads the voltage across its gap and no current)
        foreach (var p in doc.Parts.Where(p => p.Kind == PartKind.Switch && !p.Closed))
            s.Readings[p.Id] = new PartReading(0, 0, 0);
        foreach (var p in doc.Parts) if (!s.Readings.ContainsKey(p.Id)) s.Readings[p.Id] = new PartReading(0, 0, 0);
        return s;
    }

    private static double Clean(double v) => Math.Abs(v) < 1e-12 ? 0 : v;

    /// <summary>Gaussian elimination with partial pivoting; null when the system is singular.</summary>
    public static double[]? Gauss(double[,] a, double[] z, int n)
    {
        var m = (double[,])a.Clone();
        var b = (double[])z.Clone();
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < Tiny) return null;
            if (pivot != col)
            {
                for (var k = 0; k < n; k++) (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }
            for (var r = col + 1; r < n; r++)
            {
                var f = m[r, col] / m[col, col];
                if (f == 0) continue;
                for (var k = col; k < n; k++) m[r, k] -= f * m[col, k];
                b[r] -= f * b[col];
            }
        }
        var x = new double[n];
        for (var r = n - 1; r >= 0; r--)
        {
            var sum = b[r];
            for (var k = r + 1; k < n; k++) sum -= m[r, k] * x[k];
            x[r] = sum / m[r, r];
        }
        return x;
    }

    /// <summary>The quick-build circuits: "series" (9 V, 100 Ω and 200 Ω with an ammeter), "parallel" (9 V, 100 Ω ∥
    /// 200 Ω) and "divider" (12 V across 1 kΩ and 2 kΩ, a voltmeter across the 2 kΩ, grounded); null for another word.</summary>
    public static CircuitDoc? Preset(string? name)
    {
        var d = new CircuitDoc();
        switch (name?.Trim().ToLowerInvariant())
        {
            case "series":
            {
                var bat = d.Add(PartKind.Battery, 4, 7, 1, 9);
                var r1 = d.Add(PartKind.Resistor, 9, 3, 0, 100);
                var r2 = d.Add(PartKind.Resistor, 14, 7, 1, 200);
                var am = d.Add(PartKind.Ammeter, 9, 11, 0);
                d.Connect(bat.Id, 0, r1.Id, 0, (4, 3));
                d.Connect(r1.Id, 1, r2.Id, 0, (14, 3));
                d.Connect(r2.Id, 1, am.Id, 1, (14, 11));
                d.Connect(am.Id, 0, bat.Id, 1, (4, 11));
                return d;
            }
            case "parallel":
            {
                var bat = d.Add(PartKind.Battery, 4, 7, 1, 9);
                var r1 = d.Add(PartKind.Resistor, 10, 7, 1, 100);
                var r2 = d.Add(PartKind.Resistor, 15, 7, 1, 200);
                d.Connect(bat.Id, 0, r1.Id, 0);
                d.Connect(r1.Id, 0, r2.Id, 0);
                d.Connect(r2.Id, 1, r1.Id, 1);
                d.Connect(r1.Id, 1, bat.Id, 1);
                return d;
            }
            case "divider":
            {
                var bat = d.Add(PartKind.Battery, 4, 7, 1, 12);
                var r1 = d.Add(PartKind.Resistor, 10, 4, 1, 1000);
                var r2 = d.Add(PartKind.Resistor, 10, 10, 1, 2000);
                var vm = d.Add(PartKind.Voltmeter, 15, 10, 1);
                var gnd = d.Add(PartKind.Ground, 4, 13, 0);
                d.Connect(bat.Id, 0, r1.Id, 0, (4, 2));
                d.Connect(r1.Id, 1, r2.Id, 0);
                d.Connect(r2.Id, 1, bat.Id, 1, (4, 12));
                d.Connect(r2.Id, 0, vm.Id, 0);
                d.Connect(r2.Id, 1, vm.Id, 1);
                d.Connect(gnd.Id, 0, bat.Id, 1);
                return d;
            }
            default:
                return null;
        }
    }

    public static readonly string[] Presets = { "series", "parallel", "divider" };
}

/// <summary>The calculator strip's sums: Ohm's law, and the resistance of resistors in series and in parallel.</summary>
public static class CircuitMath
{
    /// <summary>Ohm's law from any two of V, I and R: the third, and which it was ("V", "I" or "R"); null with fewer or more than two.</summary>
    public static (double Value, string Which)? Ohm(double? v, double? i, double? r)
    {
        var given = new[] { v, i, r }.Count(q => q != null);
        if (given != 2) return null;
        if (v == null) return (i!.Value * r!.Value, "V");
        if (i == null) return r!.Value == 0 ? null : (v.Value / r.Value, "I");
        return i.Value == 0 ? null : (v.Value / i.Value, "R");
    }

    public static double Series(IEnumerable<double> ohms) => ohms.Sum();

    /// <summary>1 / Σ(1/R); 0 when any is 0 (a short), and 0 for none.</summary>
    public static double Parallel(IEnumerable<double> ohms)
    {
        var list = ohms.ToList();
        if (list.Count == 0) return 0;
        if (list.Any(r => r <= 0)) return 0;
        return 1 / list.Sum(r => 1 / r);
    }

    /// <summary>Resistances as typed: "100, 200", "1k 2.2k", "4.7M", "470 Ω"; the ones that aren't numbers left out.</summary>
    public static List<double> Ohms(string? text)
    {
        var list = new List<double>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        foreach (var raw in text.Split(new[] { ',', ';', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = raw.Replace("Ω", "").Replace("ohm", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (t.Length == 0) continue;
            var scale = 1.0;
            var last = t[^1];
            if (last is 'k' or 'K') { scale = 1e3; t = t[..^1]; }
            else if (last == 'M') { scale = 1e6; t = t[..^1]; }
            else if (last == 'm') { scale = 1e-3; t = t[..^1]; }
            if (PhysMath.Number(t) is { } v) list.Add(v * scale);
        }
        return list;
    }

    /// <summary>"100 Ω", "1 kΩ", "66.67 Ω", "4.7 MΩ".</summary>
    public static string OhmsText(double r)
    {
        var a = Math.Abs(r);
        if (a >= 1e6) return PhysMath.WithUnit(r / 1e6, "MΩ");
        if (a >= 1e3) return PhysMath.WithUnit(r / 1e3, "kΩ");
        return PhysMath.WithUnit(r, "Ω");
    }

    /// <summary>"0.03 A", "0.135 A", "2.5 µA" (amps down to a milliamp, as the worked examples are written).</summary>
    public static string AmpsText(double i)
    {
        var a = Math.Abs(i);
        if (a > 0 && a < 1e-3) return PhysMath.WithUnit(i * 1e6, "µA");
        return PhysMath.WithUnit(i, "A");
    }

    /// <summary>"9 V", "0.5 mV".</summary>
    public static string VoltsText(double v) => Math.Abs(v) > 0 && Math.Abs(v) < 1e-2 ? PhysMath.WithUnit(v * 1e3, "mV") : PhysMath.WithUnit(v, "V");

    /// <summary>"0.27 W", "90 mW".</summary>
    public static string WattsText(double w) => Math.Abs(w) > 0 && Math.Abs(w) < 1e-2 ? PhysMath.WithUnit(w * 1e3, "mW") : PhysMath.WithUnit(w, "W");

    public static string Plain(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}

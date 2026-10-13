namespace Simpl.Tools;

// B-DNA, built atom by atom from the Arnott fiber model (spec §6.7), the Mac's BioDNA.swift: base pair k sits 3.40·k Å
// up the axis and is turned 360°/10.5 per pair, so the helix is right-handed with 10.5 bp a turn. Strand 1 runs 5′→3′
// up (+Y); strand 2, the dyad image of strand 1, runs 5′→3′ down. Shared by DNA & RNA (the double helix, unzipping) and
// the Biomolecule Builder (building a strand). The app's frame keeps the hand: X = r·sin θ, Y = x, Z = r·cos θ.

/// <summary>A B-DNA duplex (or one strand of it), heavy atoms only.</summary>
public sealed class BioDuplex
{
    /// <summary>An atom of the helix: its strand (1 or 2), base pair (0 at strand 1's 5′ end), base, name, element and
    /// place (Å).</summary>
    public sealed record Atom(int Strand, int Pair, char Base, string Name, string Element, BioVec Pos);

    /// <summary>Strand 1's sequence, 5′→3′ (A, C, G, T).</summary>
    public required string Sequence { get; init; }
    public required List<Atom> Atoms { get; init; }
    /// <summary>Bonds by atom index (inside each nucleotide by §6.6's bond list, and O3′–P between nucleotides), with orders.</summary>
    public required List<(int A, int B, int Order)> Bonds { get; init; }
    /// <summary>The Watson–Crick H-bonds of each pair (§6.5), by atom index: strand 1's atom, then strand 2's.</summary>
    public required List<(int Pair, int A, int B)> HBonds { get; init; }
    public bool HasStrand2 { get; init; }

    private Dictionary<(int, int, string), int>? index;

    /// <summary>The index of an atom (strand, pair, name), if it is in the helix.</summary>
    public int? Index(int strand, int pair, string name)
    {
        if (index == null)
        {
            var d = new Dictionary<(int, int, string), int>();
            for (var i = 0; i < Atoms.Count; i++) d.TryAdd((Atoms[i].Strand, Atoms[i].Pair, Atoms[i].Name), i);
            index = d;
        }
        return index.TryGetValue((strand, pair, name), out var k) ? k : null;
    }

    public BioVec? Pos(int strand, int pair, string name) => Index(strand, pair, name) is { } i ? Atoms[i].Pos : null;

    /// <summary>A strand's P atoms, 5′→3′ (the cartoon's backbone spline goes through them).</summary>
    public List<BioVec> Phosphates(int strand)
    {
        var ps = Atoms.Where(a => a.Strand == strand && a.Name == "P").OrderBy(a => a.Pair).Select(a => a.Pos).ToList();
        if (strand != 1) ps.Reverse();
        return ps;
    }

    /// <summary>The atoms and bonds as a 3D view draws them, each atom tagged "strand 1 · A 3" (its strand, base and number
    /// from its own 5′ end), and named.</summary>
    public BioMoleculeModel Model
    {
        get
        {
            var m = new BioMoleculeModel();
            var n = Sequence.Length;
            foreach (var a in Atoms)
            {
                var number = a.Strand == 1 ? a.Pair + 1 : n - a.Pair;
                m.Add(a.Element, a.Pos, a.Name, $"strand {a.Strand} · {a.Base} {number}");
            }
            foreach (var b in Bonds) m.AddBond(b.A, b.B, b.Order);
            return m;
        }
    }
}

public static class BioBDNA
{
    /// <summary>Rise per base pair (Å).</summary>
    public const double Rise = 3.40;
    /// <summary>Twist per base pair (degrees): 10.5 bp a turn.</summary>
    public const double Twist = 360.0 / 10.5;
    /// <summary>The dyad that makes strand 2 from strand 1: (c − x, d − θ).</summary>
    public const double DyadC = 4.160;
    public const double DyadD = -169.596;

    /// <summary>Helical coordinates (x along the axis, r, θ in degrees) in the app's frame, Y up: X = r·sin θ, Y = x, Z = r·cos θ.</summary>
    public static BioVec AppFrame(double x, double r, double theta)
    {
        var t = theta * Math.PI / 180;
        return new BioVec(r * Math.Sin(t), x, r * Math.Cos(t));
    }

    /// <summary>Where an atom of the table goes: strand 1's for base pair k, or strand 2's (the dyad image) for pair k.</summary>
    public static BioVec Place(BioData.HelixAtom a, int strand, int pair)
    {
        double k = pair;
        if (strand == 1) return AppFrame(a.X + k * Rise, a.R, a.Theta + k * Twist);
        return AppFrame(DyadC - a.X + k * Rise, a.R, DyadD - a.Theta + k * Twist);
    }

    /// <summary>The helix for strand 1's sequence (5′→3′; A, C, G and T, anything else skipped), both strands or strand 1
    /// alone. Each strand keeps its 5′-terminal phosphate (labelled "5′ phosphate" by the tools).</summary>
    public static BioDuplex Build(string sequence, bool bothStrands = true)
    {
        var seq = new string(sequence.ToUpperInvariant().Where(c => "ACGT".Contains(c)).ToArray());
        var table = BioData.Bdna;
        var atoms = new List<BioDuplex.Atom>();
        var bonds = new List<(int, int, int)>();
        var hb = new List<(int, int, int)>();
        // (each nucleotide's own bonds from its deoxynucleotide's block, §6.6, where both atoms are in the helix)
        var blocks = new Dictionary<char, List<BioBondRecord>>
        {
            ['A'] = BioData.Molecule("DA")?.Bonds ?? new(), ['C'] = BioData.Molecule("DC")?.Bonds ?? new(),
            ['G'] = BioData.Molecule("DG")?.Bonds ?? new(), ['T'] = BioData.Molecule("DT")?.Bonds ?? new(),
        };
        var at = new Dictionary<(int, int, string), int>();

        void AddNucleotide(int strand, int k, char b)
        {
            var names = new Dictionary<string, int>();
            foreach (var e in table.Backbone.Concat(table.Bases.GetValueOrDefault(b) ?? new List<BioData.HelixAtom>()))
            {
                var i = atoms.Count;
                atoms.Add(new BioDuplex.Atom(strand, k, b, e.Name, e.Element, Place(e, strand, k)));
                names[e.Name] = i;
                at[(strand, k, e.Name)] = i;
            }
            foreach (var bd in blocks.GetValueOrDefault(b) ?? new List<BioBondRecord>())
                if (names.TryGetValue(bd.A, out var x) && names.TryGetValue(bd.B, out var y)) bonds.Add((x, y, bd.Order));
        }

        for (var k = 0; k < seq.Length; k++)
        {
            AddNucleotide(1, k, seq[k]);
            if (bothStrands) AddNucleotide(2, k, BioSeq.Pair(seq[k], rna: false));
        }
        // (the phosphodiester bonds: strand 1 O3′(k)–P(k+1); strand 2 O3′(k+1)–P(k))
        for (var k = 0; k < seq.Length - 1; k++)
        {
            if (at.TryGetValue((1, k, "O3'"), out var o) && at.TryGetValue((1, k + 1, "P"), out var p)) bonds.Add((o, p, 1));
            if (bothStrands && at.TryGetValue((2, k + 1, "O3'"), out var o2) && at.TryGetValue((2, k, "P"), out var p2)) bonds.Add((o2, p2, 1));
        }
        // (the Watson–Crick H-bonds, §6.5: strand 1's atom, strand 2's atom)
        if (bothStrands)
            for (var k = 0; k < seq.Length; k++)
                foreach (var (x, y) in HBondAtoms(seq[k]))
                    if (at.TryGetValue((1, k, x), out var i) && at.TryGetValue((2, k, y), out var j)) hb.Add((k, i, j));
        return new BioDuplex { Sequence = seq, Atoms = atoms, Bonds = bonds, HBonds = hb, HasStrand2 = bothStrands };
    }

    /// <summary>The atoms joined by Watson–Crick H-bonds when strand 1 has <paramref name="b"/> (strand 1's atom first): A·T 2, G·C 3.</summary>
    public static (string, string)[] HBondAtoms(char b) => b switch
    {
        'A' => new[] { ("N6", "O4"), ("N1", "N3") },
        'T' => new[] { ("O4", "N6"), ("N3", "N1") },
        'G' => new[] { ("N1", "N3"), ("O6", "N4"), ("N2", "O2") },
        'C' => new[] { ("N3", "N1"), ("N4", "O6"), ("O2", "N2") },
        _ => Array.Empty<(string, string)>(),
    };
}

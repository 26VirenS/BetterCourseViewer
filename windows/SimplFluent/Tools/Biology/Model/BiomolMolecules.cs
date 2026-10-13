namespace Simpl.Tools;

// The Biomolecule Builder's monomers and small molecules (spec §4.4, §6.3, §6.6, §6.8, §6.9), the Mac's
// BiomolMolecules.swift: an amino acid in its pH 7 or neutral form, its parts (amino group, α-carbon, carboxyl group, R
// group), its chirality by the CORN rule and a 2D sketch of its R group; a nucleotide's phosphate, sugar and base; a DNA
// strand built from the B-DNA model with its phosphodiester bonds; a sugar's ring, its numbered carbons, whether it is α
// or β, a disaccharide's glycosidic bond and its hydrolysis into two monosaccharides; a lipid's hydrophilic and
// hydrophobic parts, its ester bonds and its cis double bond; and the polysaccharides drawn as hexagon glyphs. Plain
// arithmetic on the shared data (BioData).

/// <summary>A part of a molecule, as "Colour by Part" colours it and a label names it.</summary>
public sealed record BiomolPart(string Id, string Name, BioHexPair Colour);

public static class BiomolParts
{
    // amino acids
    public static readonly BiomolPart Amino = new("amino", "Amino group", new("#1f6fd1", "#5ea8ff"));
    public static readonly BiomolPart Alpha = new("alpha", "α-carbon", new("#7d5cf5", "#a58bff"));
    public static readonly BiomolPart Carboxyl = new("carboxyl", "Carboxyl group", new("#d63a34", "#ff6961"));
    public static readonly BiomolPart RGroup = new("r", "R group (side chain)", new("#e07800", "#ffa133"));
    // peptides
    public static readonly BiomolPart Backbone = new("backbone", "Backbone", new("#5b6b8c", "#a3b1cf"));
    public static readonly BiomolPart SideChain = new("side", "R groups (side chains)", new("#e07800", "#ffa133"));
    // nucleotides
    public static readonly BiomolPart Phosphate = new("phosphate", "Phosphate", new("#7d5cf5", "#a58bff"));
    public static readonly BiomolPart Sugar = new("sugar", "Sugar", new("#3aa3b8", "#6fd3e6"));
    public static readonly BiomolPart Base = new("base", "Base", new("#2e9e48", "#30d158"));
    // lipids
    public static readonly BiomolPart Hydrophilic = new("head", "Hydrophilic head", new("#1f6fd1", "#5ea8ff"));
    public static readonly BiomolPart Hydrophobic = new("tail", "Hydrophobic tail", new("#c08a2b", "#f0b44a"));
    /// <summary>Neutral grey (a peptide's backbone when coloured by class).</summary>
    public static readonly BioHexPair Grey = new("#8e8e93", "#aeaeb2");
    /// <summary>Highlighted bonds (peptide, glycosidic, ester, phosphodiester).</summary>
    public static readonly BioHexPair BondHighlight = new("#e8508a", "#ff7aa8");
}

/// <summary>A molecule block's bond graph by atom name.</summary>
public static class BiomolGraph
{
    /// <summary>Each H atom's heavy atom (by the bonds), so parts can follow their heavy atoms.</summary>
    public static Dictionary<string, string> HeavyNeighbour(BioMolecule m)
    {
        var out_ = new Dictionary<string, string>();
        var element = new Dictionary<string, string>();
        foreach (var a in m.Atoms) element.TryAdd(a.Name, a.Element);
        foreach (var b in m.Bonds)
        {
            if (element.GetValueOrDefault(b.A) == "H" && element.GetValueOrDefault(b.B) is { } eb && eb != "H") out_[b.A] = b.B;
            if (element.GetValueOrDefault(b.B) == "H" && element.GetValueOrDefault(b.A) is { } ea && ea != "H") out_[b.B] = b.A;
        }
        return out_;
    }

    public static Dictionary<string, List<string>> Neighbours(BioMolecule m)
    {
        var adj = new Dictionary<string, List<string>>();
        foreach (var b in m.Bonds)
        {
            (adj.TryGetValue(b.A, out var la) ? la : adj[b.A] = new List<string>()).Add(b.B);
            (adj.TryGetValue(b.B, out var lb) ? lb : adj[b.B] = new List<string>()).Add(b.A);
        }
        return adj;
    }
}

// ---- amino acids (§6.2, §6.3) ----

public static class BiomolAminoAcid
{
    /// <summary>The template by one-letter code.</summary>
    public static BioMolecule? Template(char one) => BioData.Residue(one.ToString());

    /// <summary>The atoms of a free amino acid in a form: the zwitterion (pH 7: NH₃⁺, COO⁻, ionised side chains) or the
    /// neutral form textbooks draw (NH₂, COOH, and Lys, Arg and His uncharged).</summary>
    public static List<BioAtomRecord> Atoms(BioMolecule t, bool ph7)
    {
        if (ph7) return t.AtomsOf(ph7: true);
        var removed = BiomolPeptideBuilder.NeutralRemovals.GetValueOrDefault(t.Code) ?? new HashSet<string>();
        return t.AtomsOf(ph7: false).Where(a => !removed.Contains(a.Name)).ToList();
    }

    /// <summary>The free amino acid as a 3D view draws it (each atom named; tagged with its three-letter code); mirrored
    /// (z → −z) it is the D-form.</summary>
    public static BioMoleculeModel Model(char one, bool ph7, bool mirrored = false)
    {
        if (Template(one) is not { } t) return new BioMoleculeModel();
        var tag = BioAminoAcids.ByOne.GetValueOrDefault(char.ToUpperInvariant(one))?.Three ?? t.Code;
        var m = new BioMoleculeModel();
        var ids = new Dictionary<string, int>();
        foreach (var a in Atoms(t, ph7)) ids[a.Name] = m.Add(a.Element, a.Pos, a.Name, tag);
        foreach (var b in t.Bonds)
            if (ids.TryGetValue(b.A, out var x) && ids.TryGetValue(b.B, out var y)) m.AddBond(x, y, b.Order);
        return mirrored ? m.Mirrored() : m;
    }

    /// <summary>The charges a form carries, by atom name (pH 7: N +1, OXT −1, and the side chain's; neutral: none).</summary>
    public static Dictionary<string, int> Charges(char one, bool ph7)
    {
        if (!ph7 || Template(one) is not { } t) return new Dictionary<string, int>();
        var c = new Dictionary<string, int>(t.Ph7?.Charges ?? new Dictionary<string, int>());
        c.TryAdd("N", 1);
        c.TryAdd("OXT", -1);
        return c;
    }

    /// <summary>The part an atom of an amino acid belongs to: the amino group (N and its H), the α-carbon (CA and its H;
    /// Gly's other H is its R group), the carboxyl group (C, O, OXT and HXT) or the R group (the rest; Pro's ring, which
    /// bonds back to its N).</summary>
    public static BiomolPart Part(string name, BioMolecule t)
    {
        var heavy = BiomolGraph.HeavyNeighbour(t).GetValueOrDefault(name) ?? name;
        switch (heavy)
        {
            case "N": return BiomolParts.Amino;
            case "C" or "O" or "OXT": return BiomolParts.Carboxyl;
            case "CA":
                if (t.Code == "GLY" && name == GlycineRGroupH(t)) return BiomolParts.RGroup;
                return BiomolParts.Alpha;
            default: return BiomolParts.RGroup;
        }
    }

    /// <summary>Glycine's "R group": the H on CA where every other amino acid has its CB (z &lt; 0 in the template's frame).</summary>
    public static string GlycineRGroupH(BioMolecule t)
    {
        var heavy = BiomolGraph.HeavyNeighbour(t);
        return t.Atoms.Where(a => a.Element == "H" && heavy.GetValueOrDefault(a.Name) == "CA").OrderBy(a => a.Pos.Z).FirstOrDefault()?.Name ?? "HA3";
    }

    /// <summary>The CORN rule for a model: looking from the H on Cα toward Cα, do CO → R → N run clockwise (the L-form)?
    /// Null for glycine (achiral) or a model without those atoms.</summary>
    public static bool? CornClockwise(BioMoleculeModel m)
    {
        BioVec? Pos(string n) => m.Atoms.FirstOrDefault(a => a.Name == n)?.Pos;
        if (Pos("CA") is not { } ca || Pos("C") is not { } c || Pos("N") is not { } n || Pos("CB") is not { } cb || Pos("HA") is not { } ha) return null;
        var view = ca - ha;
        BioVec a = c - ca, b = cb - ca, d = n - ca;
        // (seen looking along `view`, a turn from u to v is clockwise when (u × v)·view > 0)
        double first = BioV.Dot(BioV.Cross(a, b), view), second = BioV.Dot(BioV.Cross(b, d), view);
        return first > 0 && second > 0;
    }

    /// <summary>The R group's heavy atoms projected on their best-fit plane, with CA as the stub it hangs from (and Pro's N,
    /// to show the ring closing on the backbone): 2D points one bond ≈ 1 unit apart, CA at the bottom, the R group above.</summary>
    public static BiomolSketch Sketch(char one)
    {
        if (Template(one) is not { } t) return new BiomolSketch(new(), new());
        if (t.Code == "GLY")
            return new BiomolSketch(new() { new("CA", "C", 0, 0, true), new("HA3", "H", 0, 1, false) }, new() { new(0, 1, 1) });
        var keep = t.Atoms.Where(a => a.Element != "H" && (a.Name == "CA" || (t.Code == "PRO" && a.Name == "N") || Part(a.Name, t) == BiomolParts.RGroup)).ToList();
        var pts = keep.Select(a => a.Pos).ToList();
        var c = BiomolMath.Centroid(pts);
        var cov = new double[3, 3];
        foreach (var p in pts)
        {
            double[] d = { p.X - c.X, p.Y - c.Y, p.Z - c.Z };
            for (var r = 0; r < 3; r++)
                for (var s = 0; s < 3; s++) cov[r, s] += d[r] * d[s];
        }
        var (_, vecs) = BiomolMath.EigenSymmetric3(cov);
        BioVec e1 = vecs[0], e2 = vecs[1];
        var flat = pts.Select(p => (X: BioV.Dot(p - c, e1), Y: BioV.Dot(p - c, e2))).ToList();
        // (turned so the R group stands up from CA)
        var caIndex = keep.FindIndex(a => a.Name == "CA");
        if (caIndex < 0) return new BiomolSketch(new(), new());
        var ca = flat[caIndex];
        double sx = 0, sy = 0;
        for (var k = 0; k < flat.Count; k++)
        {
            if (k == caIndex) continue;
            sx += flat[k].X;
            sy += flat[k].Y;
        }
        double others = Math.Max(flat.Count - 1, 1);
        var midX = flat.Count > 1 ? sx / others : ca.X;
        var midY = flat.Count > 1 ? sy / others : ca.Y + 1;
        var angle = Math.Atan2(midX - ca.X, midY - ca.Y);
        flat = flat.Select(p =>
        {
            double x = p.X - ca.X, y = p.Y - ca.Y;
            return ((x * Math.Cos(angle) - y * Math.Sin(angle)) / 1.5, (x * Math.Sin(angle) + y * Math.Cos(angle)) / 1.5);
        }).ToList();
        var index = new Dictionary<string, int>();
        for (var k = 0; k < keep.Count; k++) index.TryAdd(keep[k].Name, k);
        var atoms = keep.Select((a, k) => new BiomolSketch.Atom(a.Name, a.Element, flat[k].X, flat[k].Y, a.Name == "CA")).ToList();
        var bonds = t.Bonds.Where(b => index.ContainsKey(b.A) && index.ContainsKey(b.B)).Select(b => new BiomolSketch.Bond(index[b.A], index[b.B], b.Order)).ToList();
        return new BiomolSketch(atoms, bonds);
    }
}

/// <summary>A small 2D drawing of a group of atoms (an R group): points in bond lengths, bonds with their orders.</summary>
public sealed record BiomolSketch(List<BiomolSketch.Atom> Atoms, List<BiomolSketch.Bond> Bonds)
{
    /// <param name="Attachment">The atom the group hangs from (drawn as a stub).</param>
    public sealed record Atom(string Name, string Element, double X, double Y, bool Attachment);

    public sealed record Bond(int A, int B, int Order);

    /// <summary>Its extent: (minX, minY, maxX, maxY).</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds
    {
        get
        {
            if (Atoms.Count == 0) return (0, 0, 1, 1);
            return (Atoms.Min(a => a.X), Atoms.Min(a => a.Y), Atoms.Max(a => a.X), Atoms.Max(a => a.Y));
        }
    }
}

public static class BiomolMath
{
    /// <summary>The mean of some points (the origin for none).</summary>
    public static BioVec Centroid(IReadOnlyList<BioVec> pts)
    {
        if (pts.Count == 0) return BioVec.Zero;
        var s = BioVec.Zero;
        foreach (var p in pts) s += p;
        return s / pts.Count;
    }

    /// <summary>The eigenvalues (largest first) and unit eigenvectors of a symmetric 3×3 matrix (Jacobi rotations).</summary>
    public static (double[] Values, BioVec[] Vectors) EigenSymmetric3(double[,] m)
    {
        var a = (double[,])m.Clone();
        var v = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (var it = 0; it < 50; it++)
        {
            int p = 0, q = 1;
            var big = Math.Abs(a[0, 1]);
            if (Math.Abs(a[0, 2]) > big) { big = Math.Abs(a[0, 2]); p = 0; q = 2; }
            if (Math.Abs(a[1, 2]) > big) { big = Math.Abs(a[1, 2]); p = 1; q = 2; }
            if (big < 1e-12) break;
            var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
            var t = (theta >= 0 ? 1.0 : -1.0) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
            var c = 1 / Math.Sqrt(t * t + 1);
            var s = t * c;
            for (var k = 0; k < 3; k++)
            {
                double akp = a[k, p], akq = a[k, q];
                a[k, p] = c * akp - s * akq;
                a[k, q] = s * akp + c * akq;
            }
            for (var k = 0; k < 3; k++)
            {
                double apk = a[p, k], aqk = a[q, k];
                a[p, k] = c * apk - s * aqk;
                a[q, k] = s * apk + c * aqk;
            }
            for (var k = 0; k < 3; k++)
            {
                double vkp = v[k, p], vkq = v[k, q];
                v[k, p] = c * vkp - s * vkq;
                v[k, q] = s * vkp + c * vkq;
            }
        }
        var order = new[] { 0, 1, 2 }.OrderByDescending(i => a[i, i]).ToArray();
        return (order.Select(i => a[i, i]).ToArray(), order.Select(i => BioV.Unit(new BioVec(v[0, i], v[1, i], v[2, i]))).ToArray());
    }
}

// ---- nucleotides and a strand (§6.6, §6.7) ----

public static class BiomolNucleotide
{
    /// <summary>The nine, in §6.6's order.</summary>
    public static readonly string[] Codes = { "DA", "DC", "DG", "DT", "A", "C", "G", "U", "ATP" };

    public static readonly HashSet<string> PhosphateNames = new()
    {
        "P", "OP1", "OP2", "OP3", "HOP2", "HOP3", "O5'", "PA", "PB", "PG", "O1A", "O2A", "O3A",
        "O1B", "O2B", "O3B", "O1G", "O2G", "O3G", "HOG2", "HOG3", "HOB2", "HOA2",
    };

    /// <summary>The part an atom belongs to: the phosphate (with the 5′ O that joins it to the sugar), the sugar (its
    /// primed atoms) or the base.</summary>
    public static BiomolPart Part(string name)
    {
        if (PhosphateNames.Contains(name)) return BiomolParts.Phosphate;
        if (name.Contains('\'')) return BiomolParts.Sugar;
        return BiomolParts.Base;
    }

    /// <summary>The base's letter (A C G T U) for a code.</summary>
    public static char BaseLetter(string code) => code switch
    {
        "DA" or "A" or "ATP" => 'A',
        "DC" or "C" => 'C',
        "DG" or "G" => 'G',
        "DT" => 'T',
        _ => 'U',
    };

    public static bool IsDeoxy(string code) => code.StartsWith('D');

    public static string BaseName(char b) => b switch
    {
        'A' => "Adenine",
        'G' => "Guanine",
        'C' => "Cytosine",
        'T' => "Thymine",
        _ => "Uracil",
    };

    public static bool IsPurine(char b) => b is 'A' or 'G';

    /// <summary>"dAMP", "ATP": the short name.</summary>
    public static string ShortName(string code) => code switch
    {
        "DA" => "dAMP",
        "DC" => "dCMP",
        "DG" => "dGMP",
        "DT" => "dTMP",
        "A" => "AMP",
        "C" => "CMP",
        "G" => "GMP",
        "U" => "UMP",
        _ => code,
    };
}

/// <summary>A DNA strand built as strand 1 of the B-DNA model (§6.7, heavy atoms only), and its complementary strand if asked.</summary>
public static class BiomolStrand
{
    /// <summary>The longest strand built here (§4.4: up to 12 nt).</summary>
    public const int MaxLength = 12;
    public const string DefaultSequence = "ATGCGT";

    public static BioDuplex Build(string sequence, bool complement) =>
        BioBDNA.Build(new string(sequence.ToUpperInvariant().Where(c => "ACGT".Contains(c)).Take(MaxLength).ToArray()), complement);

    /// <summary>The phosphodiester linkages of a strand, 5′→3′: C3′–O3′ of one nucleotide, P–O5′–C5′ of the next (atom indices).</summary>
    public static List<int[]> Linkages(BioDuplex d, int strand)
    {
        var n = d.Sequence.Length;
        var out_ = new List<int[]>();
        for (var k = 0; k < n - 1; k++)
        {
            // (strand 1 runs up the pairs; strand 2 runs down them)
            var (a, b) = strand == 1 ? (k, k + 1) : (n - 1 - k, n - 2 - k);
            var idx = new[] { (a, "C3'"), (a, "O3'"), (b, "P"), (b, "O5'"), (b, "C5'") }.Select(x => d.Index(strand, x.Item1, x.Item2)).ToList();
            if (idx.All(i => i != null)) out_.Add(idx.Select(i => i!.Value).ToArray());
        }
        return out_;
    }

    /// <summary>Where the water a linkage releases appears: off the O3′–P bond, away from the helix's axis.</summary>
    public static BioVec WaterSite(BioDuplex d, int[] linkage)
    {
        BioVec o = d.Atoms[linkage[1]].Pos, p = d.Atoms[linkage[2]].Pos;
        var mid = (o + p) / 2;
        var out_ = BioV.Unit(new BioVec(mid.X, 0, mid.Z));
        return mid + out_ * 2.4;
    }
}

// ---- sugars (§6.8) ----

public static class BiomolSugar
{
    public static readonly string[] Monosaccharides = { "GLC", "BGC", "GAL", "FRU", "BDR" };
    public static readonly string[] Disaccharides = { "MAL", "CBI", "SUC", "LAT" };

    public static string ShortName(string code) => code switch
    {
        "GLC" => "α-Glucose",
        "BGC" => "β-Glucose",
        "GAL" => "Galactose",
        "FRU" => "Fructose",
        "BDR" => "Ribose",
        "MAL" => "Maltose",
        "CBI" => "Cellobiose",
        "SUC" => "Sucrose",
        "LAT" => "Lactose",
        _ => code,
    };

    /// <summary>The linkage a disaccharide's label shows.</summary>
    public static readonly IReadOnlyDictionary<string, string> Linkage = new Dictionary<string, string> { ["MAL"] = "α(1→4)", ["CBI"] = "β(1→4)", ["SUC"] = "α(1→2)β", ["LAT"] = "β(1→4)" };

    /// <summary>A disaccharide's two monosaccharides: the one whose anomeric carbon makes the bond (unprimed atoms) and the
    /// other (primed).</summary>
    public static readonly IReadOnlyDictionary<string, (string First, string Second)> Residues = new Dictionary<string, (string, string)>
    {
        ["MAL"] = ("Glucose", "Glucose"), ["CBI"] = ("Glucose", "Glucose"), ["SUC"] = ("Glucose", "Fructose"), ["LAT"] = ("Galactose", "Glucose"),
    };

    /// <summary>Each residue's colour when coloured by part.</summary>
    public static readonly BioHexPair[] ResidueColours = { new("#1f6fd1", "#5ea8ff"), new("#2e9e78", "#4fd1a5") };

    /// <summary>A sugar ring: its atoms in order round it, its ring O, its anomeric carbon (the ring carbon beside the ring O
    /// that carries an O outside the ring) and that O, and the reference carbon (the CH₂OH carbon on the ring O's other
    /// neighbour: C6, or ribose's C5).</summary>
    public sealed record Ring(List<string> Atoms, string RingO, string Anomeric, string AnomericO, string Reference, string ReferenceOn);

    /// <summary>The ring of the atoms whose names are primed (<paramref name="primed"/>) or not, in a molecule.</summary>
    public static Ring? RingOf(BioMolecule m, bool primed = false)
    {
        var adj = BiomolGraph.Neighbours(m);
        var element = new Dictionary<string, string>();
        foreach (var a in m.Atoms) element.TryAdd(a.Name, a.Element);
        bool Mine(string n) => n.Contains('\'') == primed && element.GetValueOrDefault(n) != "H";
        List<string>? best = null;
        foreach (var o in m.Atoms)
        {
            if (o.Element != "O" || !Mine(o.Name)) continue;
            var cs = (adj.GetValueOrDefault(o.Name) ?? new List<string>()).Where(x => element.GetValueOrDefault(x) == "C" && Mine(x)).ToList();
            if (cs.Count != 2) continue;
            // (the shortest way from one carbon to the other not through the O)
            var prev = new Dictionary<string, string> { [cs[0]] = "" };
            var queue = new Queue<string>();
            queue.Enqueue(cs[0]);
            while (queue.Count > 0 && !prev.ContainsKey(cs[1]))
            {
                var x = queue.Dequeue();
                foreach (var y in adj.GetValueOrDefault(x) ?? new List<string>())
                    if (y != o.Name && Mine(y) && !prev.ContainsKey(y))
                    {
                        prev[y] = x;
                        queue.Enqueue(y);
                    }
            }
            if (!prev.ContainsKey(cs[1])) continue;
            var path = new List<string> { cs[1] };
            while (prev.TryGetValue(path[^1], out var p) && p.Length > 0) path.Add(p);
            path.Reverse();
            var ring = new List<string> { o.Name };
            ring.AddRange(path);
            if (best == null || ring.Count < best.Count) best = ring;
        }
        if (best is not { } r) return null;
        var ringO = r[0];
        var sides = new[] { r[1], r[^1] };
        string? Exo(string c, string el) => (adj.GetValueOrDefault(c) ?? new List<string>()).FirstOrDefault(x => element.GetValueOrDefault(x) == el && !r.Contains(x));
        var anomeric = sides.FirstOrDefault(s => Exo(s, "O") != null);
        if (anomeric == null || Exo(anomeric, "O") is not { } aO) return null;
        var other = sides.FirstOrDefault(s => s != anomeric) ?? sides[1];
        if (Exo(other, "C") is not { } refC) return null;
        return new Ring(r, ringO, anomeric, aO, refC, other);
    }

    /// <summary>Whether the anomeric O is on the other face of the ring from the reference CH₂OH (trans: α) or the same face
    /// (cis: β) — the one difference between α- and β-glucose, and between starch and cellulose.</summary>
    public static bool IsAlpha(BioMolecule m, Ring r)
    {
        var pts = r.Atoms.Select(m.Pos).Where(p => p != null).Select(p => p!.Value).ToList();
        var normal = BiomolProteins.Plane(pts).Normal;
        if (m.Pos(r.Anomeric) is not { } a || m.Pos(r.AnomericO) is not { } ao || m.Pos(r.ReferenceOn) is not { } c || m.Pos(r.Reference) is not { } refP) return false;
        return BioV.Dot(ao - a, normal) * BioV.Dot(refP - c, normal) < 0;
    }

    /// <summary>A disaccharide's glycosidic bond: the O bonded to one primed and one unprimed carbon, the unprimed residue's
    /// anomeric carbon on it, and the other carbon.</summary>
    public sealed record Glycosidic(string O, string Anomeric, string Other);

    public static Glycosidic? GlycosidicOf(BioMolecule m)
    {
        var adj = BiomolGraph.Neighbours(m);
        foreach (var o in m.Atoms)
        {
            if (o.Element != "O") continue;
            var cs = (adj.GetValueOrDefault(o.Name) ?? new List<string>()).Where(n => m.Atom(n)?.Element == "C").ToList();
            if (cs.Count != 2) continue;
            var primed = cs.Where(c => c.Contains('\'')).ToList();
            var plain = cs.Where(c => !c.Contains('\'')).ToList();
            if (primed.Count == 1 && plain.Count == 1) return new Glycosidic(o.Name, plain[0], primed[0]);
        }
        return null;
    }

    /// <summary>Which residue an atom of a disaccharide belongs to: 0 for the unprimed one, 1 for the primed one (which keeps
    /// the glycosidic O when the bond is hydrolysed); an H goes with its heavy atom.</summary>
    public static int ResidueOf(string name, BioMolecule m, string? glycosidicO)
    {
        var heavy = BiomolGraph.HeavyNeighbour(m).GetValueOrDefault(name) ?? name;
        if (heavy == glycosidicO) return 1;
        return heavy.Contains('\'') ? 1 : 0;
    }

    /// <summary>A disaccharide split by hydrolysis (§4.4): each residue's atoms moved <paramref name="apart"/>/2 along the
    /// glycosidic C–O bond (the first residue back, the second on), an H on the freed glycosidic O (now an OH) and an OH on
    /// the first residue's anomeric carbon (the O 1.43 Å out along the former C–O direction) — the water's three atoms,
    /// named "O (water)" and "H (water)". Each atom tagged with its residue's name.</summary>
    public static (BioMoleculeModel Model, List<int> Residue, List<int> NewAtoms)? Split(BioMolecule m, double apart = 4)
    {
        if (GlycosidicOf(m) is not { } g || m.Pos(g.Anomeric) is not { } c1 || m.Pos(g.O) is not { } og) return null;
        var d = BioV.Unit(og - c1);
        (string First, string Second) names = Residues.TryGetValue(m.Code, out var nm) ? nm : ("Sugar 1", "Sugar 2");
        var model = new BioMoleculeModel();
        var ids = new Dictionary<string, int>();
        var res = new List<int>();
        foreach (var a in m.Atoms)
        {
            var r = ResidueOf(a.Name, m, g.O);
            ids[a.Name] = model.Add(a.Element, a.Pos + d * (r == 0 ? -apart / 2 : apart / 2), a.Name, r == 0 ? names.First : names.Second);
            res.Add(r);
        }
        foreach (var b in m.Bonds)
        {
            if ((b.A == g.Anomeric && b.B == g.O) || (b.A == g.O && b.B == g.Anomeric)) continue;
            if (ids.TryGetValue(b.A, out var x) && ids.TryGetValue(b.B, out var y)) model.AddBond(x, y, b.Order);
        }
        if (!ids.TryGetValue(g.Anomeric, out var ci) || !ids.TryGetValue(g.O, out var oi)) return null;
        BioVec c1n = model.Atoms[ci].Pos, ogn = model.Atoms[oi].Pos;
        var h1 = model.Add("H", ogn - d * 0.97, "H (water)", names.Second);
        model.AddBond(oi, h1);
        var o1 = model.Add("O", c1n + d * 1.43, "O (water)", names.First);
        model.AddBond(ci, o1);
        // (its H anti to the ring O across the new C–O bond)
        var ringO = RingOf(m, primed: false)?.RingO;
        var refP = ringO != null && ids.TryGetValue(ringO, out var ri) ? model.Atoms[ri].Pos : c1n + new BioVec(0, 1, 0);
        var h2 = model.Add("H", BioV.Place(refP, c1n, model.Atoms[o1].Pos, 0.97, 109.5, 180), "H (water)", names.First);
        model.AddBond(o1, h2);
        res.AddRange(new[] { 1, 0, 0 });
        return (model, res, new List<int> { h1, o1, h2 });
    }
}

// ---- lipids (§6.9) ----

public static class BiomolLipid
{
    public static readonly string[] Codes = { "GOL", "PLM", "STE", "OLA", "TGL", "POV", "CLR" };

    public static string ShortName(string code) => code switch
    {
        "GOL" => "Glycerol",
        "PLM" => "Palmitic acid",
        "STE" => "Stearic acid",
        "OLA" => "Oleic acid",
        "TGL" => "Tristearin",
        "POV" => "POPC",
        "CLR" => "Cholesterol",
        _ => code,
    };

    /// <summary>The finer part an atom belongs to (its label): carboxyl group, hydrocarbon tail, glycerol, ester linkage,
    /// phosphate, choline, hydroxyl group or the steroid rings. H atoms go with their heavy atom.</summary>
    public static string Detail(string name, BioMolecule m)
    {
        var n = BiomolGraph.HeavyNeighbour(m).GetValueOrDefault(name) ?? name;
        switch (m.Code)
        {
            case "GOL": return "Glycerol";
            case "PLM" or "STE" or "OLA": return n is "C1" or "O1" or "O2" ? "Carboxyl group" : "Hydrocarbon tail";
            case "TGL":
                if (n is "CG1" or "CG2" or "CG3") return "Glycerol";
                if (n is "OG1" or "OG2" or "OG3" or "CA1" or "OA1" or "CB1" or "OB1" or "CC1" or "OC1") return "Ester bond";
                return "Stearate tail";
            case "POV":
                if (n is "N" or "C11" or "C12" or "C13" or "C14" or "C15") return "Choline";
                if (n is "P" or "O11" or "O12" or "O13" or "O14") return "Phosphate";
                if (n is "C1" or "C2" or "C3") return "Glycerol";
                if (n is "O21" or "C21" or "O22" or "O31" or "C31" or "O32") return "Ester bond";
                return n.StartsWith("C2", StringComparison.Ordinal) ? "Oleate tail" : "Palmitate tail";
            case "CLR":
                if (n == "O1") return "Hydroxyl group";
                if (n.StartsWith('C') && int.TryParse(n[1..], out var k) && k >= 20) return "Hydrocarbon tail";
                return "Four fused rings";
            default: return "";
        }
    }

    /// <summary>Hydrophilic (the head: carboxyl, glycerol's OH, ester, phosphate, choline, cholesterol's OH) or hydrophobic.</summary>
    public static bool IsHydrophilic(string detail) => detail is "Carboxyl group" or "Glycerol" or "Ester bond" or "Phosphate" or "Choline" or "Hydroxyl group";

    /// <summary>The ester bonds (acyl C to the glycerol O): tristearin's three, POPC's two.</summary>
    public static (string, string)[] EsterBonds(string code) => code switch
    {
        "TGL" => new[] { ("CA1", "OG1"), ("CB1", "OG2"), ("CC1", "OG3") },
        "POV" => new[] { ("C21", "O21"), ("C31", "O31") },
        _ => Array.Empty<(string, string)>(),
    };

    /// <summary>The cis double bonds that kink a chain: oleic acid's C9=C10, POPC's oleate C29=C210 (cholesterol's C5=C6 is
    /// in a ring).</summary>
    public static (string, string)[] CisBonds(string code) => code switch
    {
        "OLA" => new[] { ("C9", "C10") },
        "POV" => new[] { ("C29", "C210") },
        _ => Array.Empty<(string, string)>(),
    };

    /// <summary>The chain's bend at the double bond: 180° less the angle C1–C9–C18 (0 straight on).</summary>
    public static double? KinkAngle(BioMolecule m)
    {
        if (m.Pos("C1") is not { } c1 || m.Pos("C9") is not { } c9 || m.Pos("C18") is not { } c18) return null;
        return 180 - BioV.Angle(c1, c9, c18);
    }
}

// ---- polysaccharides (2D) ----

/// <summary>The polysaccharides drawn as hexagon glyphs (§4.4).</summary>
public enum BiomolPolysaccharide { Amylose, Amylopectin, Glycogen, Cellulose, Chitin }

/// <summary>Where each glucose (or N-acetylglucosamine) of a polysaccharide sits, which way up, and the links between them
/// with their linkage, in glyph spacings.</summary>
public static class BiomolPolysaccharides
{
    public static readonly BiomolPolysaccharide[] All = { BiomolPolysaccharide.Amylose, BiomolPolysaccharide.Amylopectin, BiomolPolysaccharide.Glycogen, BiomolPolysaccharide.Cellulose, BiomolPolysaccharide.Chitin };

    /// <summary>"amylose" (the Mac's raw value).</summary>
    public static string Key(this BiomolPolysaccharide p) => p.ToString().ToLowerInvariant();

    public static string Title(this BiomolPolysaccharide p) => p switch
    {
        BiomolPolysaccharide.Amylose => "Amylose (starch)",
        BiomolPolysaccharide.Amylopectin => "Amylopectin (starch)",
        BiomolPolysaccharide.Glycogen => "Glycogen",
        BiomolPolysaccharide.Cellulose => "Cellulose",
        _ => "Chitin",
    };

    public static string Short(this BiomolPolysaccharide p) => p.ToString();

    /// <summary>What it is, in a sentence or two.</summary>
    public static string Summary(this BiomolPolysaccharide p) => p switch
    {
        BiomolPolysaccharide.Amylose => "α-glucose joined α(1→4). The α links make the chain coil into a helix, about six glucose a turn. Plants store energy as starch.",
        BiomolPolysaccharide.Amylopectin => "α(1→4) chains with an α(1→6) branch every 24–30 glucose. The other part of starch.",
        BiomolPolysaccharide.Glycogen => "Like amylopectin but more branched: an α(1→6) branch every 8–12 glucose, so enzymes can free glucose quickly. Animals store it in liver and muscle.",
        BiomolPolysaccharide.Cellulose => "β-glucose joined β(1→4), every other glucose flipped, so the chains run straight. Neighbouring chains H-bond into strong microfibrils: plant cell walls.",
        _ => "Like cellulose, β(1→4), but each sugar is N-acetylglucosamine (NAc). Insect and crab exoskeletons and fungal cell walls.",
    };

    /// <param name="Flipped">Drawn upside down (cellulose's and chitin's every other sugar).</param>
    /// <param name="Tag">A tag on the glyph ("NAc").</param>
    public sealed record Glyph(double X, double Y, bool Flipped = false, string? Tag = null);

    public enum LinkKind { Chain, Branch, HBond, Gap }

    public sealed record Link(int From, int To, string Text, LinkKind Kind);

    public sealed record Layout(List<Glyph> Glyphs, List<Link> Links, List<(double X, double Y, string Text)> Notes, double Width, double Height);

    public static Layout LayoutOf(this BiomolPolysaccharide p)
    {
        var g = new List<Glyph>();
        var l = new List<Link>();
        var notes = new List<(double, double, string)>();
        List<int> Chain(int count, double x0, double y0, string linkText, double wave = 0, bool flip = false, string? tag = null)
        {
            var ids = new List<int>();
            for (var k = 0; k < count; k++)
            {
                var y = y0 + wave * Math.Sin(k * 2 * Math.PI / 6);
                g.Add(new Glyph(x0 + k, y, flip && k % 2 == 1, tag));
                ids.Add(g.Count - 1);
                if (k > 0) l.Add(new Link(g.Count - 2, g.Count - 1, linkText, LinkKind.Chain));
            }
            return ids;
        }
        switch (p)
        {
            case BiomolPolysaccharide.Amylose:
                Chain(12, 0, 0.5, "α(1→4)", wave: 0.35);
                notes.Add((5.5, 1.45, "coils into a helix (≈ 6 glucose a turn)"));
                return new Layout(g, l, notes, 12, 1.8);
            case BiomolPolysaccharide.Amylopectin:
            {
                var main = Chain(12, 0, 0, "α(1→4)");
                // (a gap stands for the 24–30 residues between branches)
                var k = l.FindIndex(x => x.From == main[3]);
                if (k >= 0) l[k] = new Link(main[3], main[4], "≈ 24–30 glucose", LinkKind.Gap);
                var branch = Chain(5, 8.6, 1.35, "α(1→4)");
                l.Add(new Link(main[8], branch[0], "α(1→6) branch", LinkKind.Branch));
                notes.Add((3.5, -0.85, "branch every 24–30 glucose"));
                return new Layout(g, l, notes, 13.6, 2.6);
            }
            case BiomolPolysaccharide.Glycogen:
            {
                var main = Chain(11, 0, 0, "α(1→4)");
                var b1 = Chain(5, 2.6, 1.35, "α(1→4)");
                var b2 = Chain(4, 6.6, 1.35, "α(1→4)");
                var b3 = Chain(3, 4.2, 2.7, "α(1→4)");
                l.Add(new Link(main[2], b1[0], "α(1→6) branch", LinkKind.Branch));
                l.Add(new Link(main[6], b2[0], "α(1→6) branch", LinkKind.Branch));
                l.Add(new Link(b1[1], b3[0], "α(1→6) branch", LinkKind.Branch));
                notes.Add((5, -0.85, "branch every 8–12 glucose"));
                return new Layout(g, l, notes, 11, 4);
            }
            case BiomolPolysaccharide.Cellulose:
            {
                var rows = new List<List<int>>();
                for (var r = 0; r < 3; r++) rows.Add(Chain(8, 0, r * 1.25, "β(1→4)", flip: true));
                for (var r = 0; r < 2; r++)
                    for (var k = 0; k < 8; k += 2) l.Add(new Link(rows[r][k], rows[r + 1][k], "H-bond", LinkKind.HBond));
                notes.Add((3.5, -0.85, "straight chains H-bonded into a microfibril"));
                return new Layout(g, l, notes, 8, 3.6);
            }
            default:
                Chain(8, 0, 0.4, "β(1→4)", flip: true, tag: "NAc");
                notes.Add((3.5, -0.55, "N-acetylglucosamine, β(1→4)"));
                return new Layout(g, l, notes, 8, 1.6);
        }
    }
}

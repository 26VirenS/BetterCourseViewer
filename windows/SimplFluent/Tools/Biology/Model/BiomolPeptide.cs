using System.Globalization;

namespace Simpl.Tools;

// The Biomolecule Builder's peptide builder (spec §6.4), the Mac's BiomolPeptide.swift, exactly as the specification's
// script builds one: the backbone placed atom by atom by NeRF from each residue's φ and ψ (ω 180°, Engh & Huber's
// lengths and angles), each side chain put in its residue's frame from its §6.3 template (the β rotamer for Asp and Asn
// in a strand), a deterministic clash relief that tries each side chain's rotamers in turn, and proline's rule (a Pro
// four or more into a helix breaks it). Then the backbone H-bonds that hold helices and sheets, found from the atoms
// (N–H···O=C closer than 3.3 Å, the angle at H over 140°), the helix's own measures (residues a turn, rise, radius:
// §6.4's verification) and the β-sheet demo (three antiparallel strands from §6.4's transforms). Plain .NET, so the unit
// tests check it against the specification's test vectors (BiomoleculeBuilderModel.cs's BiomolChecks).

/// <summary>The secondary structure a residue is built in: its φ and ψ (IUPAC-IUB 1970, as Ramachandran references
/// tabulate them). Coil, α-helix and β-strand are the chips under the sequence; the rest are in their menu.</summary>
public enum BiomolSS { Coil, Helix, Strand, Parallel, Helix310, LeftHelix }

public static class BiomolSSes
{
    public static readonly BiomolSS[] All = { BiomolSS.Coil, BiomolSS.Helix, BiomolSS.Strand, BiomolSS.Parallel, BiomolSS.Helix310, BiomolSS.LeftHelix };

    /// <summary>(φ, ψ) in degrees.</summary>
    public static (double Phi, double Psi) Angles(this BiomolSS s) => s switch
    {
        BiomolSS.Helix => (-57, -47),
        BiomolSS.Strand => (-139, 135),
        BiomolSS.Parallel => (-119, 113),
        BiomolSS.Helix310 => (-49, -26),
        BiomolSS.LeftHelix => (57, 47),
        _ => (-75, 145),
    };

    public static string Label(this BiomolSS s) => s switch
    {
        BiomolSS.Helix => "α-helix",
        BiomolSS.Strand => "β-strand",
        BiomolSS.Parallel => "Parallel β-strand",
        BiomolSS.Helix310 => "3₁₀ helix",
        BiomolSS.LeftHelix => "Left-handed α (rare, Gly)",
        _ => "Coil",
    };

    /// <summary>What the chip under a residue shows (C, H, E, P, G, L).</summary>
    public static char Letter(this BiomolSS s) => s switch
    {
        BiomolSS.Helix => 'H',
        BiomolSS.Strand => 'E',
        BiomolSS.Parallel => 'P',
        BiomolSS.Helix310 => 'G',
        BiomolSS.LeftHelix => 'L',
        _ => 'C',
    };

    public static BiomolSS? FromLetter(char c)
    {
        var u = char.ToUpperInvariant(c);
        foreach (var s in All) if (s.Letter() == u) return s;
        return null;
    }

    /// <summary>The cartoon draws it as a helix ribbon (H), a strand arrow (E) or a coil tube (C).</summary>
    public static char Cartoon(this BiomolSS s) => s switch
    {
        BiomolSS.Helix or BiomolSS.Helix310 or BiomolSS.LeftHelix => 'H',
        BiomolSS.Strand or BiomolSS.Parallel => 'E',
        _ => 'C',
    };

    public static bool IsStrand(this BiomolSS s) => s.Cartoon() == 'E';
    public static bool IsHelix(this BiomolSS s) => s.Cartoon() == 'H';

    /// <summary>"CCHHH…" for the structures, residue by residue.</summary>
    public static string Text(IEnumerable<BiomolSS> ss) => new(ss.Select(Letter).ToArray());

    /// <summary>The structures a text names (anything unreadable is coil), padded with coil to <paramref name="count"/>.</summary>
    public static BiomolSS[] Parse(string text, int count)
    {
        var read = text.Select(c => FromLetter(c) ?? BiomolSS.Coil).ToList();
        return Enumerable.Range(0, count).Select(i => i < read.Count ? read[i] : BiomolSS.Coil).ToArray();
    }
}

/// <summary>A backbone H-bond: residue <c>Donor</c>'s N–H to residue <c>Acceptor</c>'s C=O, by atom index, and the N···O
/// distance (Å).</summary>
public sealed record BiomolHBond(int Donor, int Acceptor, int N, int H, int O, double Distance);

/// <summary>A built peptide: its atoms (every atom, H included), bonds, the structure each residue was built in, and its
/// backbone H-bonds.</summary>
public sealed class BiomolPeptide
{
    /// <summary>An atom: its residue (0 = the N-terminus), its PDB name ("CA", "NZ", "HB2"), its element and where it is.</summary>
    public sealed record Atom(int Residue, string Name, string Element, BioVec Pos);

    /// <summary>A bond by atom index; <c>Peptide</c> marks the C of one residue to the N of the next.</summary>
    public sealed record Bond(int A, int B, int Order, bool Peptide = false);

    /// <summary>A residue's N, CA, C and O (atom indices) and its amide H (null for proline and the first residue's own).</summary>
    public sealed record Backbone(int N, int CA, int C, int O, int? H);

    /// <summary>One-letter codes, N→C.</summary>
    public string Sequence { get; init; } = "";
    /// <summary>The structure asked for, residue by residue.</summary>
    public BiomolSS[] Asked { get; init; } = Array.Empty<BiomolSS>();
    /// <summary>What was built: as asked, but where a proline broke a helix (it and the residue before it are coil).</summary>
    public BiomolSS[] Built { get; init; } = Array.Empty<BiomolSS>();
    public double[] Phi { get; init; } = Array.Empty<double>();
    public double[] Psi { get; init; } = Array.Empty<double>();
    /// <summary>The prolines (residue indices) that broke a helix.</summary>
    public List<int> ProlineBreaks { get; init; } = new();
    /// <summary>The pH 7 form (N-terminus +1, C-terminus −1, ionised side chains) or the neutral one.</summary>
    public bool Ph7 { get; init; }
    public List<Atom> Atoms { get; set; } = new();
    public List<Bond> Bonds { get; set; } = new();
    public List<Backbone> Backbones { get; set; } = new();
    public List<BiomolHBond> HBonds { get; set; } = new();
    /// <summary>The rotamer each side chain ended with (its Lovell name; null for Gly, Ala and Pro).</summary>
    public List<string?> Rotamers { get; set; } = new();

    public int Count => Sequence.Length;

    /// <summary>"Lys 12": a residue's three-letter code and its number from the N-terminus (1-based).</summary>
    public string ResidueName(int i)
    {
        if (i < 0 || i >= Sequence.Length) return "";
        return $"{BioAminoAcids.ByOne.GetValueOrDefault(Sequence[i])?.Three ?? Sequence[i].ToString()} {i + 1}";
    }

    /// <summary>The CA atoms, N→C.</summary>
    public List<BioVec> CaTrace => Backbones.Select(b => Atoms[b.CA].Pos).ToList();

    /// <summary>What a 3D view draws: the atoms (with or without H), each tagged with its residue ("Lys 12") and named; the
    /// index maps the model's atom ids back to <see cref="Atoms"/>. Bond orders as the templates have them.</summary>
    public (BioMoleculeModel Model, List<int> Index) Model(bool hydrogens = true)
    {
        var m = new BioMoleculeModel();
        var map = new Dictionary<int, int>();
        var index = new List<int>();
        for (var k = 0; k < Atoms.Count; k++)
        {
            var a = Atoms[k];
            if (!hydrogens && a.Element == "H") continue;
            map[k] = m.Add(a.Element, a.Pos, a.Name, ResidueName(a.Residue));
            index.Add(k);
        }
        foreach (var b in Bonds)
            if (map.TryGetValue(b.A, out var x) && map.TryGetValue(b.B, out var y)) m.AddBond(x, y, b.Order);
        return (m, index);
    }

    /// <summary>The atom of a residue by its name.</summary>
    public int? AtomIndex(int residue, string name)
    {
        for (var k = 0; k < Atoms.Count; k++)
            if (Atoms[k].Residue == residue && Atoms[k].Name == name) return k;
        return null;
    }

    /// <summary>Every atom moved by a turn (a 3×3 matrix, rows) and a shift: S2 = R·S1 + t (§6.4's sheet).</summary>
    public BiomolPeptide Transformed(double[][] r, BioVec t) => new()
    {
        Sequence = Sequence, Asked = Asked, Built = Built, Phi = Phi, Psi = Psi, ProlineBreaks = ProlineBreaks, Ph7 = Ph7,
        Atoms = Atoms.Select(a =>
        {
            var p = a.Pos;
            var q = new BioVec(r[0][0] * p.X + r[0][1] * p.Y + r[0][2] * p.Z, r[1][0] * p.X + r[1][1] * p.Y + r[1][2] * p.Z, r[2][0] * p.X + r[2][1] * p.Y + r[2][2] * p.Z);
            return a with { Pos = q + t };
        }).ToList(),
        Bonds = Bonds, Backbones = Backbones, HBonds = HBonds, Rotamers = Rotamers,
    };

    public BiomolPeptide Moved(BioVec v) => Transformed(new[] { new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 } }, v);
}

public static class BiomolPeptideBuilder
{
    // Engh & Huber (1991): lengths in Å, angles in degrees.
    public const double NCA = 1.458, CAC = 1.525, CN = 1.329, CO = 1.231, NH = 1.01;
    public const double AngNCAC = 111.2, AngCACN = 116.2, AngCNCA = 121.7, AngCACO = 120.1;

    /// <summary>The peptide's longest (spec §4.4: up to 40 residues, about 800 atoms with H).</summary>
    public const int MaxResidues = 40;
    /// <summary>The default sequence (19 residues).</summary>
    public const string DefaultSequence = "MKTAYIAKQRQISFVKSHF";

    /// <summary>Atoms the builder makes itself (a template's are skipped).</summary>
    public static readonly HashSet<string> BackboneNames = new() { "N", "CA", "C", "O", "OXT", "H", "H2", "H3", "HXT" };
    /// <summary>A side chain's atoms "beyond CB" exclude these (the clash relief's measure).</summary>
    public static readonly HashSet<string> NearBackbone = new() { "N", "CA", "C", "O", "OXT", "CB", "H", "H2", "H3", "HA", "HA2", "HA3" };
    /// <summary>The neutral form's side chains as textbooks draw them: the CCD keeps Lys, Arg and His in their +1 form, so
    /// the neutral form leaves out the extra H (Lys –NH₂, Arg's neutral guanidine =NH, His's neutral Nε2–H ring).</summary>
    public static readonly IReadOnlyDictionary<string, HashSet<string>> NeutralRemovals = new Dictionary<string, HashSet<string>>
    {
        ["LYS"] = new() { "HZ3" }, ["ARG"] = new() { "HH22" }, ["HIS"] = new() { "HD1" },
    };

    /// <summary>The side-chain atoms a template contributes in a form: never the builder's own, never what the form leaves out.</summary>
    public static List<BioAtomRecord> SideChainAtoms(BioMolecule t, bool ph7)
    {
        var removed = ph7 ? new HashSet<string>(t.Ph7?.Remove ?? new List<string>()) : (NeutralRemovals.GetValueOrDefault(t.Code) ?? new HashSet<string>());
        return t.Atoms.Where(a => a.Role == BioAtomRole.Always && !BackboneNames.Contains(a.Name) && !removed.Contains(a.Name)).ToList();
    }

    /// <summary>The structures actually built: a Pro at position 4 or later of a helix segment (counted from 1) makes it and
    /// the residue before it coil (−75°/145°), which breaks the helix there (§6.4 step 7). Returns the breaking prolines.</summary>
    public static (BiomolSS[] Built, List<int> Breaks) ApplyProlineRule(string sequence, BiomolSS[] asked)
    {
        var built = asked.ToArray();
        var breaks = new List<int>();
        var i = 0;
        while (i < asked.Length)
        {
            var kind = asked[i];
            if (kind != BiomolSS.Helix && kind != BiomolSS.Helix310) { i++; continue; }
            var j = i;
            while (j < asked.Length && asked[j] == kind) j++;
            for (var p = i; p < j; p++)
                if (sequence[p] == 'P' && p - i >= 3) breaks.Add(p);
            i = j;
        }
        foreach (var p in breaks)
        {
            built[p] = BiomolSS.Coil;
            built[p - 1] = BiomolSS.Coil;
        }
        return (built, breaks);
    }

    /// <summary>The one-letter codes of the 20 in a text (other letters skipped), at most <see cref="MaxResidues"/>.</summary>
    public static string Clean(string text) =>
        new(text.ToUpperInvariant().Where(c => BioAminoAcids.ByOne.ContainsKey(c)).Take(MaxResidues).ToArray());

    /// <summary>The peptide for a sequence of one-letter codes (other letters skipped; at most <see cref="MaxResidues"/>),
    /// each residue in its structure (<paramref name="ss"/>, padded with coil), in the pH 7 or the neutral form;
    /// <paramref name="relieve"/> runs the clash relief.</summary>
    public static BiomolPeptide Build(string text, IReadOnlyList<BiomolSS> ss, bool ph7 = true, bool relieve = true)
    {
        var seq = Clean(text);
        var n = seq.Length;
        var asked = Enumerable.Range(0, n).Select(i => i < ss.Count ? ss[i] : BiomolSS.Coil).ToArray();
        var (built, breaks) = ApplyProlineRule(seq, asked);
        var phi = built.Select(s => s.Angles().Phi).ToArray();
        var psi = built.Select(s => s.Angles().Psi).ToArray();
        // (proline's ring holds its own φ near −65°: in a strand's −139° or a left-handed helix's +57° its CD would sit on
        // the C before it, so a Pro there is built with the coil's −75°, its ψ as asked)
        for (var i = 0; i < n; i++)
            if (seq[i] == 'P' && (phi[i] < -100 || phi[i] > 0)) phi[i] = BiomolSS.Coil.Angles().Phi;
        var peptide = new BiomolPeptide { Sequence = seq, Asked = asked, Built = built, Phi = phi, Psi = psi, ProlineBreaks = breaks, Ph7 = ph7 };
        if (n == 0) return peptide;

        // 1–2. The backbone by NeRF.
        var N = new List<BioVec> { BioVec.Zero };
        var CA = new List<BioVec> { new(NCA, 0, 0) };
        var t0 = (180 - AngNCAC) * Math.PI / 180;
        var C = new List<BioVec> { CA[0] + CAC * new BioVec(Math.Cos(t0), Math.Sin(t0), 0) };
        for (var i = 1; i < n; i++)
        {
            N.Add(BioV.Place(N[i - 1], CA[i - 1], C[i - 1], CN, AngCACN, psi[i - 1]));
            CA.Add(BioV.Place(CA[i - 1], C[i - 1], N[i], NCA, AngCNCA, 180));
            C.Add(BioV.Place(C[i - 1], N[i], CA[i], CAC, AngNCAC, phi[i]));
        }

        var atoms = new List<BiomolPeptide.Atom>();
        var names = Enumerable.Range(0, n).Select(_ => new Dictionary<string, int>()).ToList();
        void Add(int i, string name, string element, BioVec p)
        {
            names[i][name] = atoms.Count;
            atoms.Add(new BiomolPeptide.Atom(i, name, element, p));
        }
        var templates = new List<BioMolecule>();
        for (var i = 0; i < n; i++)
        {
            if (BioData.Residue(seq[i].ToString()) is not { } t0r) continue;
            var t = t0r;
            // (a strand's Asp and Asn take their β rotamer, χ1 first)
            if (built[i].IsStrand() && t.BetaRotamer is { } beta)
                for (var k = 0; k < beta.Chis.Length && k < t.Chis.Count; k++) t = t.SettingChi(t.Chis[k].Index, beta.Chis[k]);
            templates.Add(t);
            Add(i, "N", "N", N[i]);
            Add(i, "CA", "C", CA[i]);
            Add(i, "C", "C", C[i]);
            // 3. O trans to the next N, in the peptide plane; the last residue's OXT.
            Add(i, "O", "O", BioV.Place(N[i], CA[i], C[i], CO, AngCACO, psi[i] + 180));
            if (i == n - 1) Add(i, "OXT", "O", BioV.Place(N[i], CA[i], C[i], 1.25, 117.0, psi[i]));
            // 5. The side chain in the residue's frame.
            var f = Frame(N[i], CA[i], C[i]);
            foreach (var a in SideChainAtoms(t, ph7)) Add(i, a.Name, a.Element, f.Apply(a.Pos));
            // 4. The N-terminus's H atoms from the template (H, H2 and at pH 7 H3; Pro: H and at pH 7 H2); else the amide
            // H on the bisector of C–N–CA's outside (none on Pro).
            if (i == 0)
            {
                var hs = t.Code == "PRO" ? (ph7 ? new[] { "H", "H2" } : new[] { "H" }) : (ph7 ? new[] { "H", "H2", "H3" } : new[] { "H", "H2" });
                foreach (var h in hs)
                    if (t.Pos(h) is { } p) Add(i, h, "H", f.Apply(p));
            }
            else if (t.Code != "PRO")
                Add(i, "H", "H", N[i] + NH * BioV.Unit(BioV.Unit(N[i] - C[i - 1]) + BioV.Unit(N[i] - CA[i])));
            // (the neutral C-terminus's H on OXT, where the template has it against its own C=O)
            if (i == n - 1 && !ph7 && names[i].TryGetValue("O", out var o) && names[i].TryGetValue("OXT", out var oxt)
                && t.Pos("O") is { } to && t.Pos("C") is { } tc && t.Pos("OXT") is { } tx && t.Pos("HXT") is { } th)
            {
                double len = BioV.Distance(tx, th), ang = BioV.Angle(tc, tx, th), tor = BioV.Dihedral(to, tc, tx, th);
                Add(i, "HXT", "H", BioV.Place(atoms[o].Pos, C[i], atoms[oxt].Pos, len, ang, tor));
            }
        }
        if (templates.Count != n) return peptide;

        // Bonds: each residue's from its template (where both atoms are there), and the peptide bonds.
        var bonds = new List<BiomolPeptide.Bond>();
        for (var i = 0; i < n; i++)
        {
            foreach (var b in templates[i].Bonds)
                if (names[i].TryGetValue(b.A, out var x) && names[i].TryGetValue(b.B, out var y)) bonds.Add(new BiomolPeptide.Bond(x, y, b.Order));
            if (i > 0 && names[i - 1].TryGetValue("C", out var c) && names[i].TryGetValue("N", out var nn)) bonds.Add(new BiomolPeptide.Bond(c, nn, 1, true));
        }
        peptide.Atoms = atoms;
        peptide.Bonds = bonds;
        peptide.Backbones = Enumerable.Range(0, n).Select(i => new BiomolPeptide.Backbone(names[i].GetValueOrDefault("N"), names[i].GetValueOrDefault("CA"),
            names[i].GetValueOrDefault("C"), names[i].GetValueOrDefault("O"), i == 0 ? null : (names[i].TryGetValue("H", out var h) ? h : null))).ToList();
        peptide.Rotamers = templates.Select((t, i) => t.Chis.Count == 0 ? null : (built[i].IsStrand() && t.BetaRotamer != null ? t.BetaRotamer.Name : t.Rotamer)).ToList();

        // 6. Clash relief.
        if (relieve) BiomolClashRelief.Run(peptide, templates, names);
        peptide.HBonds = HBonds(peptide);
        return peptide;
    }

    /// <summary>A residue's frame (§6.3, §6.4 step 5): origin CA, x toward C, N in the xy-plane with y &gt; 0, z = x × y.</summary>
    public static BioFrame Frame(BioVec n, BioVec ca, BioVec c)
    {
        var ex = BioV.Unit(c - ca);
        var v = n - ca;
        var ey = BioV.Unit(v - BioV.Dot(v, ex) * ex);
        return new BioFrame(ca, ex, ey, BioV.Cross(ex, ey));
    }

    // ---- H-bonds ----

    /// <summary>The backbone H-bonds: every N–H (the atom named H on a residue's N) to every other residue's C=O O, where
    /// N···O is under 3.3 Å and the angle N–H···O over 140° (the β-sheet search's criterion, §6.4). Residues next to each
    /// other never count.</summary>
    public static List<BiomolHBond> HBonds(BiomolPeptide p, double maxDistance = 3.3, double minAngle = 140) =>
        HBonds(new[] { p }, new[] { p }, true, maxDistance, minAngle);

    /// <summary>The H-bonds from the N–H of <paramref name="donors"/>' residues to the C=O of <paramref name="acceptors"/>'
    /// (between strands: <paramref name="sameChain"/> false).</summary>
    public static List<BiomolHBond> HBonds(IReadOnlyList<BiomolPeptide> donors, IReadOnlyList<BiomolPeptide> acceptors, bool sameChain, double maxDistance = 3.3, double minAngle = 140)
    {
        var out_ = new List<BiomolHBond>();
        foreach (var d in donors)
            for (var i = 0; i < d.Count; i++)
            {
                if (d.AtomIndex(i, "H") is not { } h) continue;
                BioVec nPos = d.Atoms[d.Backbones[i].N].Pos, hPos = d.Atoms[h].Pos;
                foreach (var a in acceptors)
                    for (var j = 0; j < a.Count; j++)
                    {
                        if (sameChain && Math.Abs(i - j) < 2) continue;
                        var oPos = a.Atoms[a.Backbones[j].O].Pos;
                        var dist = BioV.Distance(nPos, oPos);
                        if (dist >= maxDistance || BioV.Angle(nPos, hPos, oPos) <= minAngle) continue;
                        out_.Add(new BiomolHBond(i, j, d.Backbones[i].N, h, a.Backbones[j].O, dist));
                    }
            }
        return out_;
    }

    // ---- the helix's measures ----

    /// <summary>A helix's residues per turn, rise per residue (Å; negative for a left-handed helix) and CA radius (Å), fitted
    /// to its CA atoms as §6.4's verification does: the axis from the mean of the local axes (each the cross product of two
    /// neighbouring CA bisectors), the rise along it, the turn from a circle fitted to the CAs seen down it.</summary>
    public static (double PerTurn, double Rise, double Radius) HelixParameters(IReadOnlyList<BioVec> ca)
    {
        var n = ca.Count;
        if (n < 5) return (0, 0, 0);
        var axis = BioVec.Zero;
        for (var i = 1; i < n - 2; i++)
        {
            var bis1 = (ca[i - 1] - ca[i]) + (ca[i + 1] - ca[i]);
            var bis2 = (ca[i] - ca[i + 1]) + (ca[i + 2] - ca[i + 1]);
            axis += BioV.Unit(BioV.Cross(bis1, bis2));
        }
        axis = BioV.Unit(axis);
        var z = ca.Select(p => BioV.Dot(p, axis)).ToList();
        var rise = (z[n - 1] - z[0]) / (n - 1);
        var e1 = BioV.Unit(BioV.Cross(axis, Math.Abs(axis.X) < 0.9 ? new BioVec(1, 0, 0) : new BioVec(0, 1, 0)));
        var e2 = BioV.Cross(axis, e1);
        var xy = ca.Select(p =>
        {
            var q = p - BioV.Dot(p, axis) * axis;
            return (X: BioV.Dot(q, e1), Y: BioV.Dot(q, e2));
        }).ToList();
        // (the circle: least squares on 2x·cx + 2y·cy + k = x² + y²)
        var m = new double[3, 3];
        var v = new double[3];
        foreach (var (x, y) in xy)
        {
            double[] row = { 2 * x, 2 * y, 1 };
            var b = x * x + y * y;
            for (var r = 0; r < 3; r++)
            {
                for (var c = 0; c < 3; c++) m[r, c] += row[r] * row[c];
                v[r] += row[r] * b;
            }
        }
        var sol = Solve3(m, v);
        double cx = sol[0], cy = sol[1];
        var radius = xy.Select(p => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy))).Sum() / n;
        var angles = xy.Select(p => Math.Atan2(p.Y - cy, p.X - cx)).ToArray();
        for (var k = 1; k < n; k++)
        {
            while (angles[k] - angles[k - 1] > Math.PI) angles[k] -= 2 * Math.PI;
            while (angles[k] - angles[k - 1] < -Math.PI) angles[k] += 2 * Math.PI;
        }
        var twist = (angles[n - 1] - angles[0]) / (n - 1) * 180 / Math.PI;
        if (Math.Abs(twist) <= 1e-9) return (0, rise, radius);
        return (360 / Math.Abs(twist), rise * (twist < 0 ? -1 : 1), radius);
    }

    /// <summary>Ax = b for a 3×3 system (Cramer's rule; zeros if singular).</summary>
    public static double[] Solve3(double[,] a, double[] b)
    {
        static double Det(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        var d = Det(a);
        if (Math.Abs(d) <= 1e-12) return new double[3];
        return Enumerable.Range(0, 3).Select(k =>
        {
            var m = (double[,])a.Clone();
            for (var r = 0; r < 3; r++) m[r, k] = b[r];
            return Det(m) / d;
        }).ToArray();
    }
}

// ---- clash relief (§6.4 step 6) ----

/// <summary>The side chains' clash relief, as the specification's script does it: (a) twice, the residues with the most
/// side-chain heavy atoms beyond CB first (ties by index), each that comes within 3.0 Å of another residue's heavy atom
/// tries its rotamers in order and keeps the one that leaves it farthest away (the first on ties); (b) each pair of side
/// chains closer than 2.8 Å tries every pair of their rotamers. Proline is never turned.</summary>
public static class BiomolClashRelief
{
    private sealed record Side(int Residue, int[] Heavy, List<(int[] Atoms, int[] Moving)> Chis, List<double[]> Rotamers);

    public static void Run(BiomolPeptide p, IReadOnlyList<BioMolecule> templates, IReadOnlyList<Dictionary<string, int>> names)
    {
        var n = p.Count;
        var sides = new Side?[n];
        for (var i = 0; i < n; i++)
        {
            var t = templates[i];
            if (t.Chis.Count == 0 || t.Code == "PRO") continue;
            var adjacent = new Dictionary<string, List<string>>();
            foreach (var b in t.Bonds)
            {
                if (!names[i].ContainsKey(b.A) || !names[i].ContainsKey(b.B)) continue;
                (adjacent.TryGetValue(b.A, out var la) ? la : adjacent[b.A] = new List<string>()).Add(b.B);
                (adjacent.TryGetValue(b.B, out var lb) ? lb : adjacent[b.B] = new List<string>()).Add(b.A);
            }
            var chis = new List<(int[], int[])>();
            foreach (var c in t.Chis)
            {
                var idx = c.Atoms.Where(a => names[i].ContainsKey(a)).Select(a => names[i][a]).ToArray();
                if (idx.Length != 4) continue;
                // (everything reached from the bond's far atom without crossing back over it)
                var far = new HashSet<string> { c.Atoms[2] };
                var stack = new Stack<string>();
                stack.Push(c.Atoms[2]);
                while (stack.Count > 0)
                {
                    var x = stack.Pop();
                    foreach (var y in adjacent.GetValueOrDefault(x) ?? new List<string>())
                        if (y != c.Atoms[1] && far.Add(y)) stack.Push(y);
                }
                far.Remove(c.Atoms[2]);
                chis.Add((idx, far.Where(f => names[i].ContainsKey(f)).Select(f => names[i][f]).OrderBy(k => k).ToArray()));
            }
            var heavy = names[i].Where(kv => !BiomolPeptideBuilder.NearBackbone.Contains(kv.Key) && p.Atoms[kv.Value].Element != "H").Select(kv => kv.Value).OrderBy(k => k).ToArray();
            sides[i] = new Side(i, heavy, chis, Rotamers(t, p.Built[i].IsStrand()));
        }
        var heavyAll = Enumerable.Range(0, p.Atoms.Count).Where(k => p.Atoms[k].Element != "H").ToArray();

        void Set(Side s, double[] target)
        {
            for (var k = 0; k < s.Chis.Count && k < target.Length; k++)
            {
                var chi = s.Chis[k];
                BioVec a = p.Atoms[chi.Atoms[0]].Pos, b = p.Atoms[chi.Atoms[1]].Pos, c = p.Atoms[chi.Atoms[2]].Pos, d = p.Atoms[chi.Atoms[3]].Pos;
                var turn = target[k] - BioV.Dihedral(a, b, c, d);
                var axis = c - b;
                foreach (var m in chi.Moving) p.Atoms[m] = p.Atoms[m] with { Pos = c + BioV.Rotate(p.Atoms[m].Pos - c, axis, turn) };
            }
        }
        double Mind(Side s)
        {
            if (s.Heavy.Length == 0) return 9;
            var best = double.MaxValue;
            foreach (var k in s.Heavy)
            {
                var q = p.Atoms[k].Pos;
                foreach (var j in heavyAll)
                {
                    if (p.Atoms[j].Residue == s.Residue) continue;
                    var d = q - p.Atoms[j].Pos;
                    var d2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
                    if (d2 < best) best = d2;
                }
            }
            return Math.Sqrt(best);
        }
        double PairMin(Side a, Side b)
        {
            if (a.Heavy.Length == 0 || b.Heavy.Length == 0) return 9;
            var best = double.MaxValue;
            foreach (var x in a.Heavy)
                foreach (var y in b.Heavy) best = Math.Min(best, BioV.Distance(p.Atoms[x].Pos, p.Atoms[y].Pos));
            return best;
        }
        var chosen = new int[n];

        // (a) Each crowded side chain on its own, twice.
        for (var pass = 0; pass < 2; pass++)
        {
            var order = Enumerable.Range(0, n).OrderByDescending(i => sides[i]?.Heavy.Length ?? 0).ThenBy(i => i).ToList();
            foreach (var i in order)
            {
                if (sides[i] is not { } s || Mind(s) >= 3.0) continue;
                (double M, int K)? best = null;
                for (var k = 0; k < s.Rotamers.Count; k++)
                {
                    Set(s, s.Rotamers[k]);
                    var m = Mind(s);
                    if (best == null || m > best.Value.M + 1e-9) best = (m, k);
                }
                if (best is { } bb)
                {
                    Set(s, s.Rotamers[bb.K]);
                    chosen[i] = bb.K;
                }
            }
        }
        // (b) Pairs of side chains still closer than 2.8 Å.
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                if (sides[i] is not { } si || sides[j] is not { } sj || PairMin(si, sj) >= 2.8) continue;
                (double M, int A, int B)? best = null;
                for (var a = 0; a < si.Rotamers.Count; a++)
                    for (var b = 0; b < sj.Rotamers.Count; b++)
                    {
                        Set(si, si.Rotamers[a]);
                        Set(sj, sj.Rotamers[b]);
                        var m = Math.Min(Mind(si), Mind(sj));
                        if (best == null || m > best.Value.M + 1e-9) best = (m, a, b);
                    }
                if (best is { } bb)
                {
                    Set(si, si.Rotamers[bb.A]);
                    Set(sj, sj.Rotamers[bb.B]);
                    chosen[i] = bb.A;
                    chosen[j] = bb.B;
                }
            }
        // (the rotamers' names, as chosen)
        for (var i = 0; i < n; i++)
        {
            if (sides[i] == null) continue;
            p.Rotamers[i] = RotamerNames(templates[i], p.Built[i].IsStrand())[chosen[i]];
        }
    }

    /// <summary>A residue's rotamers in the order the relief tries them: its default (the template's χ angles; the β rotamer
    /// for Asp and Asn in a strand), then every other, most common first, without repeating the first.</summary>
    public static List<double[]> Rotamers(BioMolecule t, bool beta) => List(t, beta).Select(r => r.Chis).ToList();

    public static List<string> RotamerNames(BioMolecule t, bool beta) => List(t, beta).Select(r => r.Name).ToList();

    private static List<BioRotamer> List(BioMolecule t, bool beta)
    {
        var @base = new BioRotamer(t.Rotamer ?? "default", t.Chis.Select(c => c.Degrees).ToArray());
        var first = beta ? (t.BetaRotamer ?? @base) : @base;
        var rest = (beta && t.BetaRotamer != null ? new List<BioRotamer> { @base } : new List<BioRotamer>()).Concat(t.Alternatives);
        var key = first.Chis.Select(x => (int)Math.Round(x, MidpointRounding.AwayFromZero)).ToArray();
        return new[] { first }.Concat(rest.Where(r => !r.Chis.Select(x => (int)Math.Round(x, MidpointRounding.AwayFromZero)).SequenceEqual(key))).ToList();
    }
}

// ---- the β-sheet demo (§6.4) ----

/// <summary>Three antiparallel strands from §6.4's transforms: S1 built as a β-strand from the origin, S2 = R·S1 + t (its
/// residue 6 opposite S1's residue 0), S3 = S1 + Δ, and the backbone H-bonds between them.</summary>
public sealed class BiomolSheet
{
    public required List<BiomolPeptide> Strands { get; init; }
    /// <summary>H-bonds between neighbouring strands: (donor strand, acceptor strand, the bond).</summary>
    public required List<(int Donor, int Acceptor, BiomolHBond Bond)> HBonds { get; init; }

    private static readonly Lazy<(double[][] R, BioVec T, BioVec Delta)> transforms = new(() =>
    {
        var r = new double[][] { new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 } };
        BioVec t = BioVec.Zero, d = BioVec.Zero;
        foreach (var line in BioText.Lines(BioData.SheetTransformText))
        {
            var numbers = line.Replace('−', '-').Split(new[] { ' ', ',', '[', ']', '=' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? (double?)x : null).Where(x => x != null).Select(x => x!.Value).ToArray();
            if (line.StartsWith('R') && numbers.Length == 9) r = new[] { numbers[0..3], numbers[3..6], numbers[6..9] };
            if (line.StartsWith('t') && numbers.Length >= 3) t = new BioVec(numbers[0], numbers[1], numbers[2]);
            if (line.StartsWith('Δ') && numbers.Length >= 3) d = new BioVec(numbers[0], numbers[1], numbers[2]);
        }
        return (r, t, d);
    });

    /// <summary>R (rows), t and Δ, read from the specification's text.</summary>
    public static (double[][] R, BioVec T, BioVec Delta) Transforms => transforms.Value;

    /// <summary>The demo for a 7-residue sequence (Gly₇ for the test vectors; the view uses Ala₇ so the R groups show).</summary>
    public static BiomolSheet Build(string sequence = "AAAAAAA", bool ph7 = true)
    {
        var s1 = BiomolPeptideBuilder.Build(sequence, Enumerable.Repeat(BiomolSS.Strand, 7).ToArray(), ph7);
        var tr = Transforms;
        var s2 = s1.Transformed(tr.R, tr.T);
        var s3 = s1.Moved(tr.Delta);
        var strands = new List<BiomolPeptide> { s1, s2, s3 };
        var hb = new List<(int, int, BiomolHBond)>();
        foreach (var (a, b) in new[] { (0, 1), (1, 0), (1, 2), (2, 1) })
            foreach (var h in BiomolPeptideBuilder.HBonds(new[] { strands[a] }, new[] { strands[b] }, false)) hb.Add((a, b, h));
        return new BiomolSheet { Strands = strands, HBonds = hb };
    }
}

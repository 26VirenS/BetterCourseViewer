namespace Simpl.Tools;

// The double helix page's scene (spec §4.2 Double helix, §6.7), as plain values: B-DNA built atom by atom by BioBDNA
// (right-handed, 10.5 bp a turn, strand 1 5′→3′ up the +Y axis, strand 2 down it), then drawn as a cartoon — each
// backbone a tube through its strand's P atoms, each base a rounded box from its C1′ toward its partner's, the purine's
// longer (58% of the C1′–C1′ distance less the gap) than the pyrimidine's (42%), with 2 or 3 white H-bond dashes across
// the 1 Å gap between them — or atom by atom (ball and stick, or space filling), with the overlays laid over either: the
// 5′ and 3′ ends with arrowheads pointing 5′→3′ (antiparallel), the major and minor grooves as translucent helical bands,
// three measurements as dimension lines, and the H-bonds. Unzipping opens it from its bottom end: pair k's nucleotides
// move apart once the slider passes k/N. What a press picks is named so that "bp:5" is both nucleotides of base pair 5,
// "bp:5:1" strand 1's nucleotide of it, and "bp:5:1:base" / "bp:5:1:bb" its base and its sugar–phosphate backbone.

/// <summary>What the helix is drawn from: the sequence (strand 1, 5′→3′, A C G T), the style, the overlays, how far it is
/// unzipped (0…1) and light or dark.</summary>
public sealed record DnaHelixOptions(string Sequence, DnaStyle Style = DnaStyle.Cartoon, bool Ends = true, bool Grooves = false,
    bool Measure = false, bool HBonds = true, double Unzip = 0, bool Dark = false)
{
    /// <summary>What the scene depends on (a 3D view's key).</summary>
    public string Key => $"{Sequence}|{Style}|{Ends}|{Grooves}|{Measure}|{HBonds}|{Unzip:0.000}|{Dark}";
}

/// <summary>A cartoon base: its nucleotide (pair, strand, base), its box's centre and axes (along the C1′–C1′ line, across
/// it in the base's plane, and up its thickness), its length and where it starts (its C1′).</summary>
public sealed record DnaBaseBox(int Pair, int Strand, char Base, BioVec Start, BioVec Centre, BioVec Along, BioVec Across, BioVec Up, double Length)
{
    public bool IsPurine => Base is 'A' or 'G';
}

/// <summary>A word drawn flat toward the viewer over the scene (a base's letter, "5′"): what it says, where, how tall
/// (Å), how far toward the viewer it is lifted, what a press on it picks, and the colour it sits on (null: the page).</summary>
public sealed record DnaLetter(string Text, BioVec Anchor, double Size, double Lift, string? Id, string? Behind);

/// <summary>Element colours and radii for the atom styles (the view hands in the chemistry tables', MBChem's).</summary>
public sealed record DnaAtomPaint(Func<string, string> Hex, Func<string, DnaStyle, double> Radius)
{
    /// <summary>CPK-like colours and the chemistry builder's radii for C, N, O and P (the unit tests' stand-in).</summary>
    public static readonly DnaAtomPaint Plain = new(
        e => e switch { "C" => "#909090", "N" => "#3050f8", "O" => "#ff0d0d", "P" => "#ff8000", _ => "#ffffff" },
        (e, s) => s == DnaStyle.SpaceFill
            ? 0.9 * e switch { "C" => 1.70, "N" => 1.55, "O" => 1.52, "P" => 1.80, _ => 1.2 }
            : 0.24 + 0.36 * e switch { "C" => 0.76, "N" => 0.71, "O" => 0.66, "P" => 1.07, _ => 0.31 });
}

/// <summary>The helix's scene and the words over it.</summary>
public sealed record DnaHelixScene(BioSceneContent Content, List<DnaLetter> Letters, BioDuplex Duplex);

public static class DnaHelix
{
    /// <summary>§4.2's cartoon: backbone tube radius, base box thickness and width, the gap where the H-bonds go, and the
    /// purine's and pyrimidine's shares of the C1′–C1′ distance less the gap.</summary>
    public const double TubeRadius = 1.1, BoxThickness = 1.8, BoxWidth = 4.2, Gap = 1.0, PurineShare = 0.58, PyrimidineShare = 0.42;

    /// <summary>How far an opened pair's two nucleotides move apart (Å), along their C1′–C1′ line.</summary>
    public const double Separation = 8.0;

    /// <summary>Where the groove bands sit (Å from the axis): inside the phosphates, over the bases' edges.</summary>
    public const double GrooveRadius = 8.2;

    /// <summary>The backbone atoms of a nucleotide (sugar and phosphate); the rest of its atoms are its base's.</summary>
    public static readonly HashSet<string> BackboneNames = new() { "P", "OP1", "OP2", "O5'", "C5'", "C4'", "O4'", "C3'", "O3'", "C2'", "C1'" };

    /// <summary>The arc between the two backbones at the same height on the minor groove's side (degrees, §6.7: about
    /// 148°); the major groove's is the rest of the turn (about 212°). From the dyad: strand 2's backbone lies at
    /// d − (360°/10.5)/3.40 Å · c from strand 1's.</summary>
    public static double MinorArc
    {
        get
        {
            var a = (BioBDNA.DyadD - BioBDNA.Twist / BioBDNA.Rise * BioBDNA.DyadC) % 360;
            return a < 0 ? a + 360 : a;
        }
    }

    public static double MajorArc => 360 - MinorArc;

    /// <summary>The height of base pair k's plane on the axis (the dyad's: c/2 up from pair 0, then one rise a pair).</summary>
    public static double PairHeight(int k) => BioBDNA.DyadC / 2 + k * BioBDNA.Rise;

    /// <summary>Strand 1's backbone angle at a height (degrees): its P atoms turn 360°/10.5 every 3.40 Å.</summary>
    public static double Strand1Angle(double y) => BioBDNA.Twist / BioBDNA.Rise * y;

    /// <summary>How far base pair <paramref name="pair"/> of <paramref name="count"/> has opened at <paramref name="unzip"/>:
    /// 0 shut, 1 fully apart. It starts to open once the slider passes pair/count (§4.2) and is apart one pair's worth later.</summary>
    public static double Opening(int pair, int count, double unzip) => Math.Clamp(unzip * count - pair, 0, 1);

    /// <summary>The direction from strand 1's C1′ to strand 2's in a pair (as built, before any opening).</summary>
    public static BioVec PairDirection(BioDuplex d, int pair) =>
        d.Pos(1, pair, "C1'") is { } a && d.Pos(2, pair, "C1'") is { } b ? BioV.Unit(b - a) : new BioVec(1, 0, 0);

    /// <summary>The duplex unzipped: each pair's two nucleotides moved apart along their C1′–C1′ line, half the
    /// separation each, eased as they go.</summary>
    public static BioDuplex Opened(BioDuplex d, double unzip)
    {
        if (unzip <= 0 || !d.HasStrand2) return d;
        var n = d.Sequence.Length;
        var shift = new BioVec[n];
        for (var k = 0; k < n; k++) shift[k] = PairDirection(d, k) * (Separation / 2 * BioStepTiming.Ease(Opening(k, n, unzip)));
        var atoms = d.Atoms.Select(a => a with { Pos = a.Pos + (a.Strand == 1 ? -shift[a.Pair] : shift[a.Pair]) }).ToList();
        return new BioDuplex { Sequence = d.Sequence, Atoms = atoms, Bonds = d.Bonds, HBonds = d.HBonds, HasStrand2 = true };
    }

    /// <summary>A strand's backbone path, 5′→3′: its P atoms (the 5′ phosphate first), then the last sugar's O3′ (its 3′ end).</summary>
    public static List<BioVec> BackbonePath(BioDuplex d, int strand)
    {
        var n = d.Sequence.Length;
        var pts = new List<BioVec>();
        for (var i = 0; i < n; i++)
            if (d.Pos(strand, strand == 1 ? i : n - 1 - i, "P") is { } p) pts.Add(p);
        if (d.Pos(strand, strand == 1 ? n - 1 : 0, "O3'") is { } o) pts.Add(o);
        return pts;
    }

    /// <summary>The pair a strand's i-th nucleotide from its 5′ end belongs to.</summary>
    public static int PairOf(int strand, int i, int n) => strand == 1 ? i : n - 1 - i;

    private static readonly Lazy<double> c1Span = new(() =>
    {
        var d = BioBDNA.Build("A");
        return d.Pos(1, 0, "C1'") is { } a && d.Pos(2, 0, "C1'") is { } b ? BioV.Distance(a, b) : 10.86;
    });

    /// <summary>The C1′–C1′ distance of a base pair as built (the same for every pair: about 10.86 Å, §6.5).</summary>
    public static double C1Span => c1Span.Value;

    /// <summary>The cartoon's bases: from each C1′ toward its partner's, the purine's box 58% of the C1′–C1′ distance less
    /// the gap, the pyrimidine's 42%, 1.8 Å thick and 4.2 Å wide.</summary>
    public static List<DnaBaseBox> Boxes(BioDuplex d)
    {
        var out_ = new List<DnaBaseBox>();
        var n = d.Sequence.Length;
        for (var k = 0; k < n; k++)
        {
            if (d.Pos(1, k, "C1'") is not { } c1) continue;
            var b1 = d.Sequence[k];
            if (!d.HasStrand2 || d.Pos(2, k, "C1'") is not { } c2)
            {
                // (one strand alone: its base points at the axis, a purine's length)
                var inward = BioV.Unit(new BioVec(-c1.X, 0, -c1.Z));
                out_.Add(Box(k, 1, b1, c1, inward, PurineShare * (C1Span - Gap)));
                continue;
            }
            var b2 = BioSeq.Pair(b1, rna: false);
            var u = BioV.Unit(c2 - c1);
            var space = BioV.Distance(c1, c2) - Gap;
            // (as built the C1′ atoms are 10.86 Å apart in every pair; once opened, the boxes keep their size and the gap grows)
            var rest = Math.Min(space, C1Span - Gap);
            var purine1 = b1 is 'A' or 'G';
            out_.Add(Box(k, 1, b1, c1, u, rest * (purine1 ? PurineShare : PyrimidineShare)));
            out_.Add(Box(k, 2, b2, c2, -u, rest * (purine1 ? PyrimidineShare : PurineShare)));
        }
        return out_;
    }

    private static DnaBaseBox Box(int pair, int strand, char b, BioVec start, BioVec along, double length)
    {
        var axis = new BioVec(0, 1, 0);
        var across = BioV.Unit(BioV.Cross(along, axis));
        if (BioV.Length(across) < 1e-6) across = new BioVec(1, 0, 0);
        var up = BioV.Cross(across, along);
        return new DnaBaseBox(pair, strand, b, start, start + along * (length / 2), along, across, up, length);
    }

    /// <summary>The H-bond dashes' ends for a pair (§4.2: A·T 2, G·C 3): short lines across the gap, side by side across
    /// the boxes' width.</summary>
    public static List<(BioVec A, BioVec B)> CartoonHBonds(DnaBaseBox one, DnaBaseBox two)
    {
        var count = one.Base is 'G' or 'C' ? 3 : 2;
        var from = one.Start + one.Along * one.Length;
        var to = two.Start + two.Along * two.Length;
        var u = BioV.Unit(to - from);
        if (BioV.Length(u) < 1e-9) u = one.Along;
        var offsets = count == 3 ? new[] { -1.3, 0, 1.3 } : new[] { -0.8, 0.8 };
        return offsets.Select(o => (from - u * 0.3 + one.Across * o, to + u * 0.3 + one.Across * o)).ToList();
    }

    /// <summary>A rounded box: half extents along x, y and z, its edges and corners rounded to <paramref name="corner"/>
    /// (a cube's faces gridded, each point pushed out from the box's core by the corner radius: flat faces, quarter-round
    /// edges), smooth normals, faces outward.</summary>
    public static BioMesh RoundedBox(double hx, double hy, double hz, double corner)
    {
        var rc = Math.Min(corner, Math.Min(hx, Math.Min(hy, hz)) * 0.95);
        var half = new[] { hx, hy, hz };
        var inner = half.Select(h => h - rc).ToArray();
        double[] Samples(int axis)
        {
            double h = half[axis], i = inner[axis];
            return new[] { -h, -(i + (h - i) * 2 / 3), -(i + (h - i) / 3), -i, i, i + (h - i) / 3, i + (h - i) * 2 / 3, h };
        }
        var m = new BioMesh();
        for (var axis = 0; axis < 3; axis++)
            foreach (var sign in new[] { -1.0, 1.0 })
            {
                int b = (axis + 1) % 3, c = (axis + 2) % 3;
                double[] sb = Samples(b), sc = Samples(c);
                var start = m.VertexCount;
                foreach (var vb in sb)
                    foreach (var vc in sc)
                    {
                        var p = new double[3];
                        p[axis] = sign * half[axis];
                        p[b] = vb;
                        p[c] = vc;
                        var core = new double[3];
                        for (var j = 0; j < 3; j++) core[j] = Math.Clamp(p[j], -inner[j], inner[j]);
                        var n = BioV.Unit(new BioVec(p[0] - core[0], p[1] - core[1], p[2] - core[2]));
                        m.Vertex(new BioVec(core[0], core[1], core[2]) + n * rc, n);
                    }
                var face = new double[3];
                face[axis] = sign;
                var outward = new BioVec(face[0], face[1], face[2]);
                int cols = sc.Length;
                for (var i = 0; i < sb.Length - 1; i++)
                    for (var j = 0; j < cols - 1; j++)
                    {
                        int a0 = start + i * cols + j, a1 = start + i * cols + j + 1, b0 = start + (i + 1) * cols + j, b1 = start + (i + 1) * cols + j + 1;
                        var pa = BioMesh.D(m.Positions[a0]);
                        var ccw = BioV.Dot(BioV.Cross(BioMesh.D(m.Positions[b0]) - pa, BioMesh.D(m.Positions[b1]) - pa), outward) >= 0;
                        if (ccw) { m.Triangle(a0, b0, b1); m.Triangle(a0, b1, a1); }
                        else { m.Triangle(a0, b1, b0); m.Triangle(a0, a1, b1); }
                    }
            }
        return m;
    }

    /// <summary>
    /// A tube along a sampled path, cut into pieces at the samples <paramref name="cuts"/> (each piece pressable on its own:
    /// a nucleotide's stretch of backbone, an arm of a tRNA) that meet without a seam — one set of rotation-minimising
    /// frames for the whole path, so neighbouring pieces share their rings. Only the path's two ends are capped.
    /// </summary>
    public static List<BioMesh> TubePieces(IReadOnlyList<BioVec> path, double radius, IReadOnlyList<int> cuts, int sides = 12)
    {
        var pieces = new List<BioMesh>();
        if (path.Count < 2 || cuts.Count < 2) return pieces;
        var frames = BioPath.Frames(path);
        var ns = Math.Max(3, sides);
        var rings = frames.Select(f =>
        {
            var s = BioV.Cross(f.T, f.R);
            return Enumerable.Range(0, ns).Select(j => f.R * Math.Cos(2 * Math.PI * j / ns) + s * Math.Sin(2 * Math.PI * j / ns)).ToArray();
        }).ToList();
        var last = path.Count - 1;
        for (var p = 0; p + 1 < cuts.Count; p++)
        {
            int from = Math.Clamp(cuts[p], 0, last), to = Math.Clamp(cuts[p + 1], 0, last);
            if (to <= from) { pieces.Add(new BioMesh()); continue; }
            var m = new BioMesh();
            for (var k = from; k <= to; k++)
                foreach (var nrm in rings[k]) m.Vertex(path[k] + nrm * radius, nrm);
            for (var k = 0; k < to - from; k++)
                for (var j = 0; j < ns; j++)
                {
                    int a = k * ns + j, b = (k + 1) * ns + j, c = (k + 1) * ns + (j + 1) % ns, d = k * ns + (j + 1) % ns;
                    // (as BioPath.Tube winds its sides: r → s turns counter-clockwise about the tangent, so a–d–c faces out)
                    m.Triangle(a, d, c);
                    m.Triangle(a, c, b);
                }
            foreach (var (k, sign) in new[] { (from, -1.0), (to, 1.0) })
            {
                if ((sign < 0 && k != 0) || (sign > 0 && k != last)) continue;
                var t = frames[k].T * sign;
                for (var j = 0; j < ns; j++)
                    m.FlatTriangle(path[k], path[k] + rings[k][j] * radius, path[k] + rings[k][(j + 1) % ns] * radius, t);
            }
            pieces.Add(m);
        }
        return pieces;
    }

    /// <summary>A cone from its base's centre to its tip (an arrowhead along a backbone).</summary>
    public static BioMesh Cone(BioVec from, BioVec to, double radius, int sides = 16) =>
        BioPath.Tube(new[] { from, to }, radius, new[] { radius, 0.0 }, sides, smooth: false);

    // ---- the scene ----

    /// <summary>The helix's scene: built off the UI thread from values (the view hands in the element colours and radii).</summary>
    public static DnaHelixScene Build(DnaHelixOptions o, DnaAtomPaint? paint = null)
    {
        var seq = DNAToolModel.HelixOf(o.Sequence);
        var built = BioBDNA.Build(seq);
        var n = seq.Length;
        var d = Opened(built, o.Unzip);
        var items = new List<BioSceneItem>();
        var labels = new List<BioLabel3D>();
        var letters = new List<DnaLetter>();
        var dark = o.Dark;
        var s1 = new BioMaterial(BioPalette.Strand1, dark, roughness: 0.4);
        var s2 = new BioMaterial(BioPalette.Strand2, dark, roughness: 0.4);
        var paths = new[] { BackbonePath(d, 1), BackbonePath(d, 2) };
        const int samples = 6;
        var splines = paths.Select(p => BioPath.CatmullRom(p, samples)).ToArray();

        if (o.Style == DnaStyle.Cartoon)
        {
            // (each strand's tube, a piece a nucleotide: from its phosphate to the next one's, the last to its 3′ end)
            for (var s = 1; s <= 2; s++)
            {
                var pieces = TubePieces(splines[s - 1], TubeRadius, Enumerable.Range(0, n + 1).Select(i => i * samples).ToList());
                for (var i = 0; i < pieces.Count; i++)
                    items.Add(new BioSceneItem($"bp:{PairOf(s, i, n)}:{s}:bb", pieces[i], s == 1 ? s1 : s2));
            }
            var boxes = Boxes(d);
            var meshes = new Dictionary<int, BioMesh>();
            foreach (var bx in boxes)
            {
                var key = (int)Math.Round(bx.Length * 1000);
                if (!meshes.TryGetValue(key, out var unit)) meshes[key] = unit = RoundedBox(bx.Length / 2, BoxThickness / 2, BoxWidth / 2, 0.55);
                var frame = new BioFrame(bx.Centre, bx.Along, bx.Up, bx.Across);
                items.Add(new BioSceneItem($"bp:{bx.Pair}:{bx.Strand}:base", unit.Transformed(frame), new BioMaterial(BioPalette.Base(bx.Base), dark, roughness: 0.45)));
                // (the sugar's stub: from the base's C1′ to the nearest point of its own piece of backbone)
                var spline = splines[bx.Strand - 1];
                var i = bx.Strand == 1 ? bx.Pair : n - 1 - bx.Pair;
                var near = spline.Skip(i * samples).Take(samples + 1).OrderBy(p => BioV.Distance(p, bx.Start)).FirstOrDefault(bx.Start);
                items.Add(new BioSceneItem($"bp:{bx.Pair}:{bx.Strand}:bb", BioShapes.Cylinder(bx.Start, near, 0.55, 10), bx.Strand == 1 ? s1 : s2));
                letters.Add(new DnaLetter($"{bx.Base}", bx.Centre, 2.0, BoxWidth / 2 + 0.25, $"bp:{bx.Pair}:{bx.Strand}:base", BioPalette.Base(bx.Base).HexFor(dark)));
            }
            if (o.HBonds)
            {
                var white = dark ? "#f2f2f7" : "#ffffff";
                foreach (var k in Enumerable.Range(0, n))
                {
                    var open = Opening(k, n, o.Unzip);
                    var alpha = Math.Round((1 - open) * 10) / 10;
                    if (alpha < 0.05) continue;
                    var one = boxes.FirstOrDefault(b => b.Pair == k && b.Strand == 1);
                    var two = boxes.FirstOrDefault(b => b.Pair == k && b.Strand == 2);
                    if (one == null || two == null) continue;
                    var dashes = new BioMesh();
                    foreach (var (a, b) in CartoonHBonds(one, two)) dashes.Append(BioMoleculeMeshes.Dashes(a, b, 0.17, 0.4, 0.25));
                    items.Add(new BioSceneItem($"bp:{k}:hb", dashes, new BioMaterial(white, Alpha: alpha, Roughness: 0.3, Emission: 0.25)));
                }
            }
        }
        else
        {
            AddAtoms(items, letters, d, o, paint ?? DnaAtomPaint.Plain);
        }

        if (o.Ends) AddEnds(items, labels, letters, paths, splines, samples, n, o.Style, s1, s2);
        if (o.Grooves) AddGrooves(labels, items, n, o.Unzip, dark);
        if (o.Measure) AddMeasures(items, labels, n, dark);

        // (the two parts every picture names: a base pair in the middle, and the backbone beside it)
        var m = Math.Max(0, Math.Min(n - 1, n / 2));
        if (n > 0)
        {
            var b1 = seq[m];
            var pairCentre = d.Pos(1, m, "C1'") is { } a && d.Pos(2, m, "C1'") is { } b ? (a + b) / 2 : new BioVec(0, PairHeight(m), 0);
            labels.Add(new BioLabel3D($"bp:{m}", $"Base pair ({b1}·{BioSeq.Pair(b1, false)})", pairCentre, Area: 3));
            var mb = Math.Max(0, Math.Min(n - 1, m - 2));
            if (d.Pos(1, mb, "P") is { } p) labels.Add(new BioLabel3D($"bp:{mb}:1:bb", "Sugar–phosphate backbone", p, Area: 4));
            if (o.Style != DnaStyle.Cartoon && !o.Ends && d.Pos(1, 0, "P") is { } p5) labels.Add(new BioLabel3D("bp:0:1:bb", "5′ phosphate", p5, Area: 1));
        }

        // (the camera frames the helix as built — unzipping or an overlay does not move it)
        var pts = built.Atoms.Select(a => a.Pos).ToList();
        var (c, r) = BioV.BoundingSphere(pts);
        var frame3 = new BioSphere(c, r + 3);
        return new DnaHelixScene(new BioSceneContent { Items = items, Labels = labels, Frame = frame3 }, letters, d);
    }

    /// <summary>Ball and stick or space filling: the heavy atoms (CPK), the bonds (ball and stick), the H-bonds as dashes
    /// between the donor and acceptor atoms (§6.5), each base's letter over it.</summary>
    private static void AddAtoms(List<BioSceneItem> items, List<DnaLetter> letters, BioDuplex d, DnaHelixOptions o, DnaAtomPaint paint)
    {
        var count = d.Atoms.Count;
        var space = o.Style == DnaStyle.SpaceFill;
        // (§3.4: under 300k triangles a scene — icospheres of 320 triangles while they fit, else of 80)
        var unit = space
            ? BioShapes.Icosphere(1, count * 320 <= 270_000 ? 2 : 1)
            : BioShapes.Icosphere(1, count <= 400 ? 2 : 1);
        string Id(BioDuplex.Atom a) => $"bp:{a.Pair}:{a.Strand}:{(BackboneNames.Contains(a.Name) ? "bb" : "base")}";
        var hexes = new Dictionary<string, string>();
        var radii = new Dictionary<string, double>();
        foreach (var a in d.Atoms)
        {
            if (!hexes.ContainsKey(a.Element)) { hexes[a.Element] = paint.Hex(a.Element); radii[a.Element] = paint.Radius(a.Element, o.Style); }
            var rr = radii[a.Element];
            var mesh = unit.Transformed(new BioFrame(a.Pos, new BioVec(rr, 0, 0), new BioVec(0, rr, 0), new BioVec(0, 0, rr)));
            items.Add(new BioSceneItem(Id(a), mesh, new BioMaterial(hexes[a.Element], Roughness: 0.35)));
        }
        if (!space)
        {
            var bond = new BioMaterial("#9e9e9e", Roughness: 0.35);
            foreach (var (x, y, order) in d.Bonds)
            {
                BioDuplex.Atom a = d.Atoms[x], b = d.Atoms[y];
                var len = BioV.Distance(a.Pos, b.Pos);
                if (len < 1e-6) continue;
                var dir = (b.Pos - a.Pos) / len;
                var side = BioV.Cross(dir, new BioVec(0, 1, 0));
                if (BioV.Length(side) < 0.1) side = BioV.Cross(dir, new BioVec(1, 0, 0));
                side = BioV.Unit(side);
                var id = Id(a);
                if (order >= 2)
                {
                    foreach (var off in new[] { -0.11, 0.11 })
                        items.Add(new BioSceneItem(id, BioShapes.Cylinder(a.Pos + side * off, b.Pos + side * off, 0.07, 8, caps: false), bond));
                }
                else items.Add(new BioSceneItem(id, BioShapes.Cylinder(a.Pos, b.Pos, 0.11, 8, caps: false), bond));
            }
        }
        if (o.HBonds)
        {
            var n = d.Sequence.Length;
            foreach (var (pair, x, y) in d.HBonds)
            {
                var alpha = Math.Round((1 - Opening(pair, n, o.Unzip)) * 10) / 10;
                if (alpha < 0.05) continue;
                items.Add(new BioSceneItem($"bp:{pair}:hb", BioMoleculeMeshes.Dashes(d.Atoms[x].Pos, d.Atoms[y].Pos, 0.08, 0.3, 0.2),
                    new BioMaterial(BioPalette.HBond.HexFor(o.Dark), Alpha: alpha)));
            }
        }
        // (each base's letter over its rings: bases always show their letter)
        foreach (var g in d.Atoms.Where(a => !BackboneNames.Contains(a.Name)).GroupBy(a => (a.Pair, a.Strand)))
        {
            var list = g.ToList();
            var centre = list.Aggregate(BioVec.Zero, (acc, a) => acc + a.Pos) / list.Count;
            letters.Add(new DnaLetter($"{list[0].Base}", centre, 1.7, space ? 2.2 : 1.2, $"bp:{g.Key.Pair}:{g.Key.Strand}:base", BioPalette.Base(list[0].Base).HexFor(o.Dark)));
        }
    }

    /// <summary>The 5′ and 3′ ends: an arrowhead every few nucleotides along each backbone, pointing 5′→3′ (the two point
    /// opposite ways), "5′" and "3′" at each end, and the ends labelled.</summary>
    private static void AddEnds(List<BioSceneItem> items, List<BioLabel3D> labels, List<DnaLetter> letters, List<BioVec>[] paths, List<BioVec>[] splines,
        int samples, int n, DnaStyle style, BioMaterial s1, BioMaterial s2)
    {
        var atoms = style != DnaStyle.Cartoon;
        for (var s = 1; s <= 2; s++)
        {
            var spline = splines[s - 1];
            var path = paths[s - 1];
            if (path.Count < 2) continue;
            // (atom by atom there is no tube: a thin trace through the phosphates carries the arrowheads)
            if (atoms) items.Add(new BioSceneItem($"arrow:{s}", BioPath.Tube(spline, 0.3, sides: 8, smooth: false), s == 1 ? s1 : s2));
            var every = n <= 6 ? 2 : 4;
            for (var i = 1; i < n; i += every)
            {
                var mid = i * samples + samples / 2;
                if (mid + 1 >= spline.Count) break;
                var dir = BioV.Unit(spline[mid + 1] - spline[mid - 1]);
                var cone = atoms ? Cone(spline[mid] - dir * 1.0, spline[mid] + dir * 1.6, 1.25) : Cone(spline[mid] - dir * 1.4, spline[mid] + dir * 1.8, 2.0);
                items.Add(new BioSceneItem($"arrow:{s}", cone, s == 1 ? s1 : s2));
            }
            var five = path[0];
            var three = path[^1];
            labels.Add(new BioLabel3D($"end:{s}:5", atoms && s == 1 ? "Strand 1 · 5′ end (5′ phosphate)" : $"Strand {s} · 5′ end", five, Area: 2));
            labels.Add(new BioLabel3D($"end:{s}:3", $"Strand {s} · 3′ end", three, Area: 2));
            // ("5′" and "3′" a little beyond each end, out from the axis)
            BioVec Out(BioVec p, BioVec beyond)
            {
                var radial = BioV.Unit(new BioVec(p.X, 0, p.Z));
                return p + radial * 2.6 + beyond * 2.2;
            }
            var startDir = BioV.Unit(path[0] - path[1]);
            var endDir = BioV.Unit(path[^1] - path[^2]);
            letters.Add(new DnaLetter("5′", Out(five, startDir), 2.4, 0.5, $"end:{s}:5", null));
            letters.Add(new DnaLetter("3′", Out(three, endDir), 2.4, 0.5, $"end:{s}:3", null));
        }
    }

    /// <summary>The grooves (§6.7): at each height, the arc between the backbones on the narrow side (about 148°, the
    /// minor groove) and on the wide side (about 212°, the major), as translucent helical bands over the shut part of
    /// the helix, each labelled where it faces the viewer.</summary>
    private static void AddGrooves(List<BioLabel3D> labels, List<BioSceneItem> items, int n, double unzip, bool dark)
    {
        var shut = Enumerable.Range(0, n).FirstOrDefault(k => Opening(k, n, unzip) <= 0, n);
        if (shut >= n) return;
        var y0 = Math.Max(shut * BioBDNA.Rise, 1.0);
        var y1 = (n - 1) * BioBDNA.Rise + 2.6;
        if (y1 - y0 < 1) return;
        // (clear of the backbones, so each band reads as the gap between them)
        const double margin = 24;
        var minor = MinorArc;
        var bands = new (string Id, string Name, double From, double To, BioHexPair Colour)[]
        {
            ("groove:minor", "Minor groove", margin, minor - margin, new BioHexPair("#f2994a", "#ffb36b")),
            ("groove:major", "Major groove", minor + margin, 360 - margin, new BioHexPair("#e8508a", "#ff7aa8")),
        };
        var rows = Math.Max(2, (int)Math.Ceiling((y1 - y0) / 0.6));
        foreach (var (id, name, from, to, colour) in bands)
        {
            var cols = Math.Max(4, (int)Math.Ceiling((to - from) / 8));
            var mesh = new BioMesh();
            for (var i = 0; i <= rows; i++)
            {
                var y = y0 + (y1 - y0) * i / rows;
                var t0 = Strand1Angle(y);
                for (var j = 0; j <= cols; j++)
                {
                    var t = (t0 + from + (to - from) * j / cols) * Math.PI / 180;
                    var radial = new BioVec(Math.Sin(t), 0, Math.Cos(t));
                    mesh.Vertex(radial * GrooveRadius + new BioVec(0, y, 0), radial);
                }
            }
            for (var i = 0; i < rows; i++)
                for (var j = 0; j < cols; j++)
                {
                    int a = i * (cols + 1) + j, b = a + 1, c = a + cols + 1, e = c + 1;
                    mesh.Triangle(a, b, e);
                    mesh.Triangle(a, e, c);
                }
            items.Add(new BioSceneItem(id, mesh, new BioMaterial(colour.HexFor(dark), Alpha: 0.2, Roughness: 0.2, DoubleSided: true), Order: 1));
            // (the label where the band's middle faces the viewer — along +z, where θ is a whole turn — nearest the middle height)
            var centre = (from + to) / 2;
            var ym = (y0 + y1) / 2;
            var best = ym;
            var bestOff = double.MaxValue;
            for (var j = -3; j <= 12; j++)
            {
                var y = (360.0 * j - centre) * BioBDNA.Rise / BioBDNA.Twist;
                if (y < y0 + 1 || y > y1 - 1) continue;
                if (Math.Abs(y - ym) < bestOff) { bestOff = Math.Abs(y - ym); best = y; }
            }
            var ta = (Strand1Angle(best) + centre) * Math.PI / 180;
            var dirA = new BioVec(Math.Sin(ta), 0, Math.Cos(ta));
            labels.Add(new BioLabel3D(id, name, dirA * GrooveRadius + new BioVec(0, best, 0), dirA, Area: 5));
        }
    }

    /// <summary>The measurements (§4.2) as dimension lines beside the helix: its 2 nm width under it, 0.34 nm between two
    /// base pairs at its right, and one turn (3.57 nm, 10.5 bp) at its left.</summary>
    private static void AddMeasures(List<BioSceneItem> items, List<BioLabel3D> labels, int n, bool dark)
    {
        var mat = new BioMaterial(dark ? "#d1d1d6" : "#48484a", Roughness: 0.6);
        void Dimension(string id, BioVec a, BioVec b, BioVec tick)
        {
            var mesh = BioShapes.Cylinder(a, b, 0.12, 10);
            mesh.Append(BioShapes.Cylinder(a - tick * 0.9, a + tick * 0.9, 0.12, 10));
            mesh.Append(BioShapes.Cylinder(b - tick * 0.9, b + tick * 0.9, 0.12, 10));
            var dir = BioV.Unit(b - a);
            mesh.Append(Cone(a + dir * 1.2, a, 0.45, 12));
            mesh.Append(Cone(b - dir * 1.2, b, 0.45, 12));
            items.Add(new BioSceneItem(id, mesh, mat));
        }
        var bottom = -3.2;
        Dimension("measure:width", new BioVec(-10, bottom, 0), new BioVec(10, bottom, 0), new BioVec(0, 1, 0));
        labels.Add(new BioLabel3D("measure:width", "2 nm wide", new BioVec(0, bottom, 0), Area: 6));
        var k = Math.Max(0, Math.Min(n - 2, n / 2 - 1));
        double ya = PairHeight(k), yb = PairHeight(k + 1);
        Dimension("measure:rise", new BioVec(13, ya, 0), new BioVec(13, yb, 0), new BioVec(1, 0, 0));
        // (guides from the two base pairs out to the line)
        var guides = BioShapes.Cylinder(new BioVec(6, ya, 0), new BioVec(12.6, ya, 0), 0.06, 8);
        guides.Append(BioShapes.Cylinder(new BioVec(6, yb, 0), new BioVec(12.6, yb, 0), 0.06, 8));
        items.Add(new BioSceneItem("measure:rise", guides, mat));
        labels.Add(new BioLabel3D("measure:rise", "0.34 nm between base pairs", new BioVec(13, (ya + yb) / 2, 0), Area: 6));
        var pitch = 10.5 * BioBDNA.Rise;
        double y0 = PairHeight(0), y1 = y0 + pitch;
        Dimension("measure:pitch", new BioVec(-14, y0, 0), new BioVec(-14, y1, 0), new BioVec(1, 0, 0));
        labels.Add(new BioLabel3D("measure:pitch", "≈3.6 nm per turn (10.5 bp)", new BioVec(-14, (y0 + y1) / 2, 0), Area: 6));
    }

    // ---- the facts the cards show ----

    /// <summary>A pair's Watson–Crick H-bonds as built (§6.5): donor and acceptor atoms (strand 1's first) and their distance.</summary>
    public static List<(string A, string B, double Distance)> PairBonds(BioDuplex d, int pair)
    {
        var out_ = new List<(string, string, double)>();
        if (pair < 0 || pair >= d.Sequence.Length) return out_;
        foreach (var (x, y) in BioBDNA.HBondAtoms(d.Sequence[pair]))
            if (d.Pos(1, pair, x) is { } a && d.Pos(2, pair, y) is { } b) out_.Add((x, y, BioV.Distance(a, b)));
        return out_;
    }

    /// <summary>The narrowest phosphate–phosphate distances across the minor and major grooves less 5.8 Å for the
    /// phosphate groups (§6.7's convention: about 6.6 and 11.6 Å in this model), measured on a long stretch.</summary>
    public static (double Minor, double Major) GrooveWidths()
    {
        var d = BioBDNA.Build(new string('A', 30));
        double minor = double.MaxValue, major = double.MaxValue;
        for (var i = 8; i < 22; i++)
            for (var j = 0; j < 30; j++)
            {
                if (d.Pos(1, i, "P") is not { } a || d.Pos(2, j, "P") is not { } b) continue;
                var dist = BioV.Distance(a, b);
                // (across the minor groove strand 2's nearest phosphates are a few pairs below; across the major, above)
                if (j - i is >= -7 and <= -2) minor = Math.Min(minor, dist);
                if (j - i is >= 1 and <= 6) major = Math.Min(major, dist);
            }
        return (minor - 5.8, major - 5.8);
    }
}

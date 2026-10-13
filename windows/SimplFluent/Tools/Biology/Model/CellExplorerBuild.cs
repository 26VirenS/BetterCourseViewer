namespace Simpl.Tools;

// The Cell Explorer's three cells built from their recipes (spec §4.1 Rendering, §5.1, §5.1.5), the same picture on
// every launch and on the Mac (CellExplorerBuild.swift, draw for draw): each cell's SplitMix64 (seed 0xC311 animal, 0x91A7
// plant, 0xBAC7 bacterium) places its fixed structures first — the "Golgi side" g, the nucleus, the rough ER's sheets,
// the Golgi stack and the centrosome (the plant: its central vacuole, then the nucleus against the wall) — then the free
// organelles in §5.1's table order, each candidate centre drawn uniformly in the cell (x, y, z: three draws), kept when
// it is far enough inside the membrane (and, in the plant, outside the vacuole) and its bounding sphere ×1.05 overlaps
// nothing placed, given up after 200 tries; then its turn (BioRandom.Rotation: three draws) where its shape has one. The
// cutaway (§4.1 item 4) removes the octant x, y, z > 0 of the membrane, the nuclear envelope and the wall, capping each
// cut wall in a lighter tint; organelles whose centre is in it are left out and those crossing it drawn whole — but a
// mitochondrion or a chloroplast crossing it is cut lengthwise, its inside facing the camera. Teaching scale draws the
// tiny things larger (ribosomes 2–4×, vesicles 1.5×, filaments 3–7×) and fewer of the many; true scale draws them at
// their real sizes, with 20,000 ribosomes (the teaching ones and more from a second stream, so the rest of the cell is
// the same). Every mesh is then turned by the view's turn (a three-quarter view into the cut) and kept, per picture, in
// CEBuildCache.

/// <summary>What a cell is built with.</summary>
public readonly record struct CEBuildOptions(CEKind Kind, bool TrueScale = false, bool Cutaway = true, bool GramPositive = false);

/// <summary>A piece of an organelle as drawn: its mesh (in the scene's frame) and how it is shaded.</summary>
public sealed record CEPiece(BioMesh Mesh, BioHexPair Colour, double Alpha, double Roughness, int Order);

/// <summary>One organelle in the cell (one mitochondrion, one ribosome, the membrane).</summary>
public sealed class CEInstance
{
    public required string Part { get; init; }
    public required int Index { get; init; }
    /// <summary>Its centre and bounding radius (the scene's frame).</summary>
    public BioVec Centre { get; set; }
    public double Radius { get; set; }
    /// <summary>The point whose place decides how far it moves when the cell is exploded: its own centre, or the organelle
    /// it belongs to (a bound ribosome's sheet, a Golgi vesicle's stack, the nucleolus's nucleus).</summary>
    public BioVec ExplodeFrom { get; set; }
    /// <summary>One of the cell's own shells (membrane, wall, capsule, the cytoplasm's cut faces): never moved, faded when exploded.</summary>
    public bool Shell { get; init; }
    /// <summary>Fixed to the shells (the wall's channels, the bacterium's flagella and pili): never moved.</summary>
    public bool Fixed { get; init; }
    public List<CEPiece> Pieces { get; set; } = new();
    /// <summary>Its frame in the cell's own coordinates (Look inside rebuilds it cut).</summary>
    public BioFrame? Frame { get; init; }

    public string Id => $"organelle:{Part}:{Index}";
}

/// <summary>Where a part's label points: an instance, the point, and (for a convex shell) its outward normal.</summary>
public readonly record struct CEAnchor(int Instance, BioVec Point, BioVec? Normal);

/// <summary>A built cell.</summary>
public sealed class CEModel
{
    public CEModel(CEBuildOptions options) => Options = options;

    public CEBuildOptions Options { get; }
    public List<CEInstance> Instances { get; } = new();
    /// <summary>Instances placed (the model's count) and drawn (fewer with the cutaway) of each part.</summary>
    public Dictionary<string, int> Placed { get; } = new();
    public Dictionary<string, int> Shown { get; } = new();
    /// <summary>Counts of kinds within a part: "ribosome.free", "ribosome.er", "vesicle.golgi".</summary>
    public Dictionary<string, int> Extra { get; } = new();
    /// <summary>Placements that ran out of tries (§4.1: the count can come out lower).</summary>
    public Dictionary<string, int> GaveUp { get; } = new();
    public Dictionary<string, CEAnchor> Anchors { get; set; } = new();
    /// <summary>For the explode slider (the geometric mean of the cell's semi-axes) and the camera.</summary>
    public double CellRadius { get; set; } = 1;
    public double FrameRadius { get; set; } = 1;
    /// <summary>The cell's surface area (µm²) and volume (µm³), for SA:V.</summary>
    public double Area { get; set; }
    public double Volume { get; set; }
    /// <summary>The turn from the cell's frame to the scene's.</summary>
    public BioFrame Display { get; set; } = BioFrame.Identity;
    /// <summary>The camera's direction in the cell's frame (from the cell toward it).</summary>
    public BioVec View { get; set; } = new(0, 0, 1);

    public int Triangles => Instances.Sum(i => i.Pieces.Sum(p => p.Mesh.TriangleCount));

    /// <summary>The parts in this cell that have anything drawn.</summary>
    public HashSet<string> PartsShown => Instances.Where(i => i.Pieces.Count > 0).Select(i => i.Part).ToHashSet();

    internal static void Bump(Dictionary<string, int> d, string key, int by = 1) => d[key] = d.GetValueOrDefault(key) + by;
}

/// <summary>Each picture's cell, built once (the last six kept); safe from any thread — two asking for the same cell at
/// once share one build.</summary>
public static class CEBuildCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<CEBuildOptions, Lazy<CEModel>> Models = new();
    private static readonly List<CEBuildOptions> Order = new();

    public static CEModel Model(CEBuildOptions o)
    {
        Lazy<CEModel> made;
        lock (Gate)
        {
            if (!Models.TryGetValue(o, out made!))
            {
                made = new Lazy<CEModel>(() => CEBuild.Build(o), LazyThreadSafetyMode.ExecutionAndPublication);
                Models[o] = made;
                Order.Add(o);
                while (Order.Count > 6) { Models.Remove(Order[0]); Order.RemoveAt(0); }
            }
        }
        return made.Value;
    }

    /// <summary>A cell already built, if it is kept (what the view can say of it before the build it asked for is done).</summary>
    public static CEModel? Built(CEBuildOptions o)
    {
        lock (Gate) return Models.TryGetValue(o, out var m) && m.IsValueCreated ? m.Value : null;
    }
}

/// <summary>Bounding spheres placed so far, for the overlap test (×1.05): the large in a list, the small in a grid.</summary>
public sealed class CEPlacer
{
    private readonly List<(BioVec C, double R, bool BlocksSer)> big = new();
    private readonly Dictionary<long, List<(BioVec C, double R)>> grid = new();
    private readonly double cell;
    public const double Small = 0.3;
    /// <summary>The largest small ball in the grid so far: how far round a ball the grid is searched (only as far as anything
    /// in it could overlap, so the answers are the same as searching to <see cref="Small"/>).</summary>
    private double largestSmall;

    public CEPlacer(double cell) => this.cell = cell;

    private static long Key(int i, int j, int k) => ((long)(i + 4096) << 26) | ((long)(j + 4096) << 13) | (long)(k + 4096);
    private int Index(double v) => (int)Math.Floor(v / cell);

    public void Add(BioVec c, double r, bool blocksSer = true)
    {
        if (r > Small) { big.Add((c, r, blocksSer)); return; }
        largestSmall = Math.Max(largestSmall, r);
        var key = Key(Index(c.X), Index(c.Y), Index(c.Z));
        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new();
        list.Add((c, r));
    }

    /// <summary>Whether a ball would overlap anything placed (their radii summed ×1.05).</summary>
    public bool Overlaps(BioVec c, double r)
    {
        foreach (var b in big) if (BioV.Distance(b.C, c) < (b.R + r) * 1.05) return true;
        var reach = (r + largestSmall) * 1.05;
        var n = (int)Math.Ceiling(reach / cell);
        int i0 = Index(c.X), j0 = Index(c.Y), k0 = Index(c.Z);
        for (var i = i0 - n; i <= i0 + n; i++)
            for (var j = j0 - n; j <= j0 + n; j++)
                for (var k = k0 - n; k <= k0 + n; k++)
                {
                    if (!grid.TryGetValue(Key(i, j, k), out var list)) continue;
                    foreach (var s in list) if (BioV.Distance(s.C, c) < (s.R + r) * 1.05) return true;
                }
        return false;
    }

    /// <summary>Whether a point is within <paramref name="gap"/> of an organelle that stops the smooth ER growing (§5.1.5).</summary>
    public bool NearSer(BioVec p, double gap)
    {
        foreach (var b in big) if (b.BlocksSer && BioV.Distance(b.C, p) < b.R + gap) return true;
        return false;
    }
}

public static partial class CEBuild
{
    /// <summary>The view's turn (a three-quarter view into the cut): 32° about y (the right side toward the camera), then 22°
    /// about x (the top toward it).</summary>
    public static readonly BioFrame Display = BioFrame.Rotation(new BioVec(0, 1, 0), -32).Then(BioFrame.Rotation(new BioVec(1, 0, 0), 22));

    public static CEModel Build(CEBuildOptions o)
    {
        var b = new CEBuilder(o);
        switch (o.Kind)
        {
            case CEKind.Animal: b.BuildAnimal(); break;
            case CEKind.Plant: b.BuildPlant(); break;
            default: b.BuildBacterium(); break;
        }
        return b.Finish();
    }

    /// <summary>A mitochondrion or chloroplast cut lengthwise (its inside toward the camera), or the nucleus with its pores,
    /// nucleolus and chromatin cut open — Look inside (§5.1.5), in the scene's frame.</summary>
    public static List<CEInstance> LookInside(CEModel m, string part) => new CEBuilder(m.Options) { Model = m }.LookInside(part);
}

/// <summary>The cell being built (in the cell's own frame until <see cref="Finish"/> turns it into the scene's).</summary>
public sealed partial class CEBuilder
{
    private readonly CEBuildOptions o;
    internal BioRandom Rng;
    private readonly CEPlacer placer;
    public CEModel Model { get; set; }
    private readonly Dictionary<string, CEPart> parts;
    /// <summary>The cell's inside (how far inside its membrane's inner wall a point is, for a margin) and its sampling box.</summary>
    private Func<BioVec, double, bool> inside = (_, _) => true;
    private Func<BioVec, double, bool> outsideVacuole = (_, _) => true;
    private BioVec box = new(1, 1, 1);
    /// <summary>The membrane's inner wall (a radius function) and a signed distance for the cell's outer surface.</summary>
    private BioRadius membraneInner = _ => 1;
    private Func<BioVec, double> surface = p => BioV.Length(p) - 1;
    /// <summary>The organelles' margin from the membrane (and the plant's from the vacuole).</summary>
    private double margin = 0.4;
    private BioVec nucleusCentre;
    private double nucleusRadius;
    private readonly BioVec cameraDirection;

    public CEBuilder(CEBuildOptions o)
    {
        this.o = o;
        Rng = new BioRandom(o.Kind.Seed());
        placer = new CEPlacer(o.Kind == CEKind.Bacterium ? 0.1 : 0.5);
        Model = new CEModel(o);
        parts = CEParts.All(o.Kind).ToDictionary(p => p.Id);
        var d = CEBuild.Display;
        // (the camera looks along −z in the scene: its direction in the cell's frame is the turn undone on +z)
        cameraDirection = new BioVec(d.X.Z, d.Y.Z, d.Z.Z);
        Model.Display = d;
        Model.View = cameraDirection;
    }

    // ---- adding ----

    /// <summary>The colour for a piece's role in a part.</summary>
    private BioHexPair Colour(string part, CERole role)
    {
        var b = parts.TryGetValue(part, out var p) ? p.Colour : new BioHexPair("#8e8e93", "#aeaeb2");
        return role.Kind switch
        {
            "cap" => CEGeo.Lighter(b),
            "dark" => CEGeo.Darker(b, 0.25),
            "named" => role.Name == "matrix" ? CEGeo.Lighter(b, 0.55) : CEParts.Colour(o.Kind, part, role.Name ?? "") ?? b,
            "colour" => role.Own ?? b,
            _ => b,
        };
    }

    /// <summary>An organelle added (its shape already placed in the cell's frame); its index among the instances.</summary>
    private int Add(string part, CEShape shape, BioVec centre, double radius, BioVec? explodeFrom = null, bool shell = false, bool isFixed = false,
        BioFrame? frame = null, bool count = true)
    {
        var index = Model.Placed.GetValueOrDefault(part);
        if (count) CEModel.Bump(Model.Placed, part);
        var partAlpha = parts.TryGetValue(part, out var p) ? p.Alpha ?? 1 : 1;
        var inst = new CEInstance
        {
            Part = part, Index = index, Centre = centre, Radius = radius, ExplodeFrom = explodeFrom ?? centre, Shell = shell, Fixed = isFixed, Frame = frame,
        };
        inst.Pieces = shape.Pieces.Select(x => new CEPiece(x.Mesh, Colour(part, x.Role), x.Alpha ?? partAlpha, x.Roughness, x.Order)).ToList();
        if (inst.Pieces.Count > 0 && count) CEModel.Bump(Model.Shown, part);
        Model.Instances.Add(inst);
        return Model.Instances.Count - 1;
    }

    /// <summary>One counted but not drawn (its centre in the cutaway's wedge).</summary>
    private void Skip(string part) => CEModel.Bump(Model.Placed, part);

    private bool Cut => o.Cutaway;
    private bool Hidden(BioVec c) => Cut && CEGeo.InWedge(c);
    private bool Crossing(BioVec c, double r) => Cut && !CEGeo.InWedge(c) && CEGeo.WedgeDistance(c) < r;

    /// <summary>A point drawn uniformly in the sampling box (three draws: x, y, z).</summary>
    private BioVec Sample()
    {
        var x = Rng.Uniform(-box.X, box.X);
        var y = Rng.Uniform(-box.Y, box.Y);
        var z = Rng.Uniform(-box.Z, box.Z);
        return new BioVec(x, y, z);
    }

    /// <summary>A free organelle's centre (§4.1 Rendering): up to 200 candidates; kept when its bounding sphere is the margin
    /// inside the membrane (and outside the vacuole) and overlaps nothing placed. Null: given up.</summary>
    private BioVec? PlaceFree(string part, double r, double? m = null, Func<BioVec, bool>? accept = null)
    {
        var gap = m ?? margin;
        for (var k = 0; k < 200; k++)
        {
            var p = Sample();
            if (!inside(p, r + gap) || !outsideVacuole(p, r + gap)) continue;
            if (accept != null && !accept(p)) continue;
            if (placer.Overlaps(p, r)) continue;
            placer.Add(p, r);
            return p;
        }
        CEModel.Bump(Model.GaveUp, part);
        return null;
    }

    private double RibosomeRadius
    {
        get
        {
            var teaching = o.Kind == CEKind.Bacterium ? 0.020 : 0.06;
            return o.TrueScale ? teaching / (parts.TryGetValue("ribosome", out var p) ? p.Factor : 1) : teaching;
        }
    }

    /// <summary>A ribosome's mesh: an icosahedron (20 triangles) in teaching scale; at true size a tetrahedron, there being
    /// 20,000 of them, each smaller than a pixel at the cell's distance (the Mac draws octahedra; Viewport3D draws on one
    /// thread, so Windows keeps the cell nearer 250k triangles).</summary>
    private BioMesh RibosomeMesh => o.TrueScale ? CEGeo.Tetrahedron(RibosomeRadius) : BioShapes.Icosphere(RibosomeRadius, 0);

    /// <summary>A filament's drawn radius: the teaching one, or the true one.</summary>
    private double Filament(string part, double teaching) => o.TrueScale ? teaching / (parts.TryGetValue(part, out var p) ? p.Factor : 1) : teaching;

    // ---- the cell's shells ----

    /// <summary>One of the cell's own shells (§5.1.5 Membranes and walls), cut by the octant with the cut faces lighter.</summary>
    private void CellShell(string part, BioRadius outer, BioRadius inner, int rings, int segments, int order, bool darkOuter = false, double capAlpha = 1)
    {
        var sh = CERecipes.Shell(outer, inner, Cut, rings, segments);
        var s = new CEShape();
        var body = parts.TryGetValue(part, out var p) ? p.Colour : new BioHexPair("#8fae5a", "#b3d17c");
        // (the wall's outer face only a little darker)
        s.Add(sh.Outer, darkOuter ? CERole.Colour(CEGeo.Darker(body, 0.15)) : CERole.Body, roughness: darkOuter ? 0.7 : 0.25, order: order);
        s.Add(sh.Inner, roughness: darkOuter ? 0.6 : 0.25, order: order);
        s.Add(sh.Caps, CERole.Cap, alpha: capAlpha, roughness: 0.6, order: order);
        Add(part, s, BioVec.Zero, outer(new BioVec(1, 0, 0)), shell: true);
    }

    // ---- the nucleus (§5.1.5) ----

    /// <summary>The nucleus: its envelope with 110 pores, the nucleolus and the chromatin.</summary>
    internal void Nucleus(BioVec n, double r)
    {
        nucleusCentre = n;
        nucleusRadius = r;
        var pores = CERecipes.PoreDirections();
        var kept = pores.Where(d => !(Cut && d.X > 0 && d.Y > 0 && d.Z > 0)).ToList();
        Add("nucleus", CERecipes.Envelope(r, kept, Cut).Placed(new BioFrame(n)), n, r);
        foreach (var d in pores)
        {
            if (Cut && d.X > 0 && d.Y > 0 && d.Z > 0) { Skip("pore"); continue; }
            var s = new CEShape();
            s.Add(CERecipes.PoreRing.Transformed(BioFrame.Along(d, n + d * (r - 0.031))), roughness: 0.4);
            Add("pore", s, n + d * r, 0.09, explodeFrom: n);
        }
        // (the nucleolus: r 0.28·R, 0.25·R toward +x+y, a blob of amplitude 0.08)
        var nc = n + BioV.Unit(new BioVec(1, 1, 0)) * (0.25 * r);
        var ns = new CEShape();
        ns.Add(BioShapes.Radial(BioShapes.Blob(BioShapes.SphereRadius(0.28 * r), new BioBlob(unchecked(o.Kind.Seed() + 1), 0.08)), 16, 32).Translated(nc), roughness: 0.6);
        Add("nucleolus", ns, nc, 0.28 * r * 1.08, explodeFrom: n);
        Chromatin(n, r, nc, 0.28 * r);
        // (the nucleus and its rough ER, as one ball that free organelles keep out of)
        var sheets = o.Kind == CEKind.Animal ? 5 : 3;
        placer.Add(n, r + 0.35 + 0.42 * (sheets - 1) + 0.25, blocksSer: false);
    }

    /// <summary>Chromatin (§5.1.1): 12 random walks inside the nucleus (each from a point in it, 28 steps of 0.3 µm turning at
    /// most 60°, kept off the envelope and the nucleolus), drawn as tubes r 0.06 µm.</summary>
    private void Chromatin(BioVec n, double r, BioVec nc, double rn)
    {
        for (var c = 0; c < 12; c++)
        {
            var p = n + Rng.InBall() * (r - 0.5);
            if (BioV.Distance(p, nc) < rn + 0.15) p = n + BioV.Unit(n - nc) * (r * 0.45);
            var d = Rng.UnitVector();
            var pts = new List<BioVec> { p };
            for (var k = 0; k < 28; k++)
            {
                var nd = CEGeo.Cone(d, 60, ref Rng);
                var q = p + nd * 0.3;
                if (BioV.Distance(q, n) > r - 0.2)
                {
                    var nn = BioV.Unit(q - n);
                    nd = BioV.Unit(nd - nn * (2 * BioV.Dot(nd, nn)));
                    q = p + nd * 0.3;
                }
                if (BioV.Distance(q, nc) < rn + 0.12)
                {
                    var nn = BioV.Unit(q - nc);
                    nd = BioV.Unit(nd - nn * (2 * BioV.Dot(nd, nn)));
                    q = p + nd * 0.3;
                }
                if (BioV.Distance(q, n) > r - 0.15 || BioV.Distance(q, nc) < rn + 0.08) q = p;
                pts.Add(q);
                p = q;
                d = nd;
            }
            var tube = BioPath.Tube(pts, 0.06, sides: 6, samples: 2);
            if (Cut) tube = CEGeo.Filtered(tube, x => !CEGeo.InWedge(x, n));
            var s = new CEShape();
            s.Add(tube, roughness: 0.6);
            var centre = pts.Aggregate(BioVec.Zero, (a, x) => a + x) / pts.Count;
            Add("chromatin", s, centre, pts.Max(x => BioV.Distance(x, centre)), explodeFrom: n);
        }
    }

    // ---- rough ER (§5.1.5) ----

    /// <summary>Sheet i's mid-surface radius about the nucleus centre (the folds, and sheet 0's lip down to the envelope).</summary>
    private double SheetRadius(int i, double theta, double phi)
    {
        var r = nucleusRadius;
        var fold = 0.12 * Math.Sin(5 * phi + i) * Math.Sin(3 * theta);
        if (i == 0)
        {
            // (sheet 0's inner edge touches the outer nuclear membrane: along its first 12% it comes down to it)
            var lip = CEGeo.Smoothstep(0, 0.12, (theta - 0.25 * Math.PI) / (0.6 * Math.PI));
            return r + (0.35 + fold) * lip;
        }
        return r + 0.35 + 0.42 * i + fold;
    }

    private static readonly (double Lo, double Hi) SheetTheta = (0.25 * Math.PI, 0.85 * Math.PI);
    private static (double Lo, double Hi) SheetPhi(int i) => (0.37 * i * Math.PI, 0.37 * i * Math.PI + 1.1 * Math.PI);

    /// <summary>Whether a point of the rough ER is left out: in the Golgi's cone (25° about g), in the cutaway's wedges (the
    /// nucleus's and the cell's), or in the plant's vacuole.</summary>
    private bool RerRemoved(BioVec p, BioVec? g)
    {
        if (g is { } gg && BioV.Angle(p - nucleusCentre, gg) < 25) return true;
        if (Cut && (CEGeo.InWedge(p, nucleusCentre) || CEGeo.InWedge(p))) return true;
        if (o.Kind == CEKind.Plant && !outsideVacuole(p, 0.1)) return true;
        return false;
    }

    /// <summary>The rough ER: <paramref name="sheets"/> curved sheets round the nucleus (each a spherical-shell patch, 0.06 µm
    /// thick, its rim rounded), studded with ribosomes on its outer face: §5.1.5's 500 a sheet in the animal cell; in the
    /// plant, whose table gives 1,200 ribosomes in all, 100 a sheet (and 900 free).</summary>
    private void RoughEr(int sheets, BioVec? g)
    {
        var n = nucleusCentre;
        var perSheet = o.Kind == CEKind.Animal ? 500 : 100;
        for (var i = 0; i < sheets; i++)
        {
            var phi = SheetPhi(i);
            var sheet = i;
            var m = BioShapes.ShellPatch(SheetTheta, phi, 0.06, (t, p) => SheetRadius(sheet, t, p), rows: 20, columns: 44).Translated(n);
            m = CEGeo.Filtered(m, x => !RerRemoved(x, g));
            var s = new CEShape();
            s.Add(m, roughness: 0.45);
            var midT = (SheetTheta.Lo + SheetTheta.Hi) / 2;
            var midP = (phi.Lo + phi.Hi) / 2;
            var centre = n + BioShapes.Direction(midT, midP) * (SheetRadius(i, midT, midP) * 0.6);
            Add("rer", s, centre, nucleusRadius + 2.5);
            var stream = Rng;
            BoundRibosomes(i, perSheet, centre, g, ref stream);
            Rng = stream;
            if (o.TrueScale)
            {
                // (true scale: the sheets' share of the 20,000, from a second stream so the rest of the cell is the same)
                var extra = new BioRandom(unchecked((o.Kind.Seed() ^ 0x5CA1E000UL) + (ulong)i));
                var share = (o.Kind == CEKind.Animal ? 12_500 : 5_000) / sheets;
                BoundRibosomes(i, share - perSheet, centre, g, ref extra);
            }
        }
    }

    /// <summary>A sheet's ribosomes: at seeded points on its outer face (two draws each, θ then φ), 0.04 µm out (scaled with
    /// the ribosome); a point where the sheet is not (the Golgi's cone, the plant's vacuole) drawn again — tested where a
    /// teaching-scale ribosome would sit, so true scale makes the same draws and the rest of the cell stays as it was; one
    /// in the cutaway's wedge counted but not drawn.</summary>
    private void BoundRibosomes(int i, int count, BioVec centre, BioVec? g, ref BioRandom r)
    {
        var n = nucleusCentre;
        var phi = SheetPhi(i);
        var theta = SheetTheta;
        var offset = 0.03 + RibosomeRadius * (0.04 / 0.06);
        var teachingOffset = 0.03 + (o.Kind == CEKind.Bacterium ? 0.020 : 0.06) * (0.04 / 0.06);
        var mesh = RibosomeMesh;
        int placedHere = 0, tries = 0;
        while (placedHere < count && tries < count * 20)
        {
            tries++;
            var t = theta.Lo + (theta.Hi - theta.Lo) * r.Uniform();
            var p = phi.Lo + (phi.Hi - phi.Lo) * r.Uniform();
            var dir = BioShapes.Direction(t, p);
            var pos = n + dir * (SheetRadius(i, t, p) + offset);
            if (g is { } gg && BioV.Angle(pos - n, gg) < 25) continue;
            if (o.Kind == CEKind.Plant && !outsideVacuole(n + dir * (SheetRadius(i, t, p) + teachingOffset), 0.1)) continue;
            placedHere++;
            CEModel.Bump(Model.Extra, "ribosome.er");
            if (RerRemoved(pos, null)) { Skip("ribosome"); continue; }
            var sh = new CEShape();
            sh.Add(mesh.Translated(pos), roughness: 0.5);
            Add("ribosome", sh, pos, RibosomeRadius, explodeFrom: centre);
        }
    }

    // ---- smooth ER (§5.1.5) ----

    /// <summary>From 6 seeded points on the outer sheet's rim, branching tubes: segments 0.35 µm long turning up to 35°, a 15%
    /// chance of a branch (turned 60°), 160 segments in all, a tip stopping within 0.6 µm of the membrane or another
    /// organelle (and, in the plant, 0.3 µm of the vacuole). Tubes r 0.05 µm, their joints smoothed by spheres.</summary>
    private void SmoothEr(int sheets, BioVec? g)
    {
        var n = nucleusCentre;
        var i = sheets - 1;
        var theta = SheetTheta;
        var phi = SheetPhi(i);
        var tips = new List<(BioVec P, BioVec D, int Tree)>();
        var tries = 0;
        while (tips.Count < 6 && tries < 100)
        {
            tries++;
            var t = 4 * Rng.Uniform();
            var f = t - Math.Floor(t);
            double th, ph;
            switch ((int)t)
            {
                case 0: th = theta.Lo; ph = phi.Lo + f * (phi.Hi - phi.Lo); break;
                case 1: th = theta.Hi; ph = phi.Lo + f * (phi.Hi - phi.Lo); break;
                case 2: th = theta.Lo + f * (theta.Hi - theta.Lo); ph = phi.Lo; break;
                default: th = theta.Lo + f * (theta.Hi - theta.Lo); ph = phi.Hi; break;
            }
            var p = n + BioShapes.Direction(th, ph) * SheetRadius(i, th, ph);
            if (g is { } gg && BioV.Angle(p - n, gg) < 25) continue;
            tips.Add((p, BioV.Unit(p - n), tips.Count));
        }
        var segments = Enumerable.Range(0, tips.Count).Select(_ => new List<(BioVec A, BioVec B)>()).ToList();
        var total = 0;
        while (total < 160 && tips.Count > 0)
        {
            var next = new List<(BioVec P, BioVec D, int Tree)>();
            foreach (var tip in tips)
            {
                if (total >= 160) break;
                var d = CEGeo.Cone(tip.D, 35, ref Rng);
                var end = tip.P + d * 0.35;
                // (a tip stops near the membrane, another organelle or the vacuole)
                if (!inside(end, 0.6) || placer.NearSer(end, 0.6) || !outsideVacuole(end, 0.3)) continue;
                segments[tip.Tree].Add((tip.P, end));
                placer.Add((tip.P + end) / 2, 0.2);
                total++;
                next.Add((end, d, tip.Tree));
                if (Rng.Uniform() < 0.15) next.Add((end, CEGeo.Turned(d, 60, ref Rng), tip.Tree));
            }
            tips = next;
        }
        const double r = 0.05;
        var joint = BioShapes.Icosphere(r, 0);
        foreach (var tree in segments)
        {
            if (tree.Count == 0) continue;
            var m = new BioMesh();
            var mids = new List<BioVec>();
            foreach (var (a, b) in tree)
            {
                var mid = (a + b) / 2;
                if (Hidden(mid)) continue;
                mids.Add(mid);
                m.Append(BioShapes.Cylinder(a, b, r, 8, caps: false));
                m.Append(joint.Translated(a));
                m.Append(joint.Translated(b));
            }
            var s = new CEShape();
            s.Add(m, roughness: 0.45);
            var c = mids.Count == 0 ? tree[0].A : mids.Aggregate(BioVec.Zero, (acc, x) => acc + x) / mids.Count;
            Add("ser", s, c, mids.Count == 0 ? 0.5 : mids.Max(x => BioV.Distance(x, c)));
        }
        Model.Extra["ser.segments"] = total;
    }

    // ---- Golgi (§5.1.5) ----

    /// <summary>The animal's Golgi stack at G, its axis a (cis face toward the nucleus), with its 24 vesicles (60% on the
    /// trans side, the rest round the rims).</summary>
    private void GolgiStack(BioVec gc, BioVec a, BioVec side)
    {
        var f = new BioFrame(gc, side, a, BioV.Cross(side, a));
        Add("golgi", CERecipes.Golgi(5, 1.6, 0.9, 0.07, 0.14).Placed(f), gc, 1.0, frame: f);
        placer.Add(gc + a * 0.1, 1.05);
        for (var k = 0; k < 24; k++)
        {
            var r = Rng.Uniform(0.06, 0.09);
            BioVec local;
            if (k < 14)
            {
                var x = Rng.Uniform(-0.65, 0.65);
                var z = Rng.Uniform(-0.35, 0.35);
                local = new BioVec(x, 2 * 0.14 + (x * x + z * z) / 4.4 + 0.063 + r + Rng.Uniform(0.02, 0.2), z);
            }
            else
            {
                var psi = Rng.Uniform(0, 2 * Math.PI);
                var j = Rng.Int(5);
                var s = 1 - 0.08 * Math.Abs(j - 2.0);
                double c = Math.Cos(psi), sn = Math.Sin(psi);
                var x = 0.8 * s * (c < 0 ? -1 : 1) * Math.Sqrt(Math.Abs(c)) * 1.12 + c * r;
                var z = 0.45 * s * (sn < 0 ? -1 : 1) * Math.Sqrt(Math.Abs(sn)) * 1.12 + sn * r;
                local = new BioVec(x, (j - 2.0) * 0.14 + (x * x + z * z) / 4.4, z);
            }
            Vesicle(f.Apply(local), r, gc, golgi: true);
        }
    }

    /// <summary>A vesicle (an icosphere; ×1.5 in teaching scale).</summary>
    private void Vesicle(BioVec p, double r, BioVec? explodeFrom = null, bool golgi = false)
    {
        CEModel.Bump(Model.Extra, golgi ? "vesicle.golgi" : "vesicle.free");
        if (Hidden(p)) { Skip("vesicle"); return; }
        var drawn = o.TrueScale ? r / 1.5 : r;
        var s = new CEShape();
        s.Add(BioShapes.Icosphere(drawn, 1).Translated(p), roughness: 0.4);
        Add("vesicle", s, p, drawn, explodeFrom: explodeFrom);
    }

    /// <summary>The plant's Golgi: 6 small stacks (dictyosomes) of 4 cisternae, 0.8 µm across, placed as free organelles,
    /// each with 3 vesicles on its trans face.</summary>
    private void Dictyosomes()
    {
        for (var k = 0; k < 6; k++)
        {
            if (PlaceFree("golgi", 0.6) is not { } p) continue;
            var rot = Rng.Rotation();
            var f = CEGeo.Frame(rot, p);
            var ves = new List<(BioVec, double)>();
            for (var v = 0; v < 3; v++)
            {
                var r = Rng.Uniform(0.05, 0.07);
                var x = Rng.Uniform(-0.3, 0.3);
                var y = 0.2 + r + Rng.Uniform(0.0, 0.1);
                var z = Rng.Uniform(-0.15, 0.15);
                ves.Add((f.Apply(new BioVec(x, y, z)), r));
            }
            if (Hidden(p)) Skip("golgi");
            else Add("golgi", CERecipes.Golgi(4, 0.8, 0.45, 0.06, 0.11, 5, 20).Placed(f), p, 0.5, frame: f);
            foreach (var (v, r) in ves) Vesicle(v, r, p, golgi: true);
        }
    }

    // ---- centrosome (§5.1.5) ----

    private BioVec Centrosome(BioVec c)
    {
        var rot = Rng.Rotation();
        var a1 = BioV.Rotate(new BioVec(0, 1, 0), rot.Axis, rot.Degrees);
        var a2 = BioV.Rotate(new BioVec(1, 0, 0), rot.Axis, rot.Degrees);
        var m = c + a2 * 0.175;
        var s = new CEShape();
        s.Add(CERecipes.Centriole.Transformed(BioFrame.Along(a1, c)), roughness: 0.45);
        s.Add(CERecipes.Centriole.Transformed(BioFrame.Along(a2, c + a2 * 0.35)), roughness: 0.45);
        // (the pericentriolar cloud: 80 small translucent spheres r 0.05 within r 0.4)
        var cloud = new BioMesh();
        var ball = BioShapes.Icosphere(0.05, 0);
        for (var k = 0; k < 80; k++) cloud.Append(ball.Translated(m + Rng.InBall() * 0.4));
        s.Add(cloud, alpha: 0.3, roughness: 0.4, order: 2);
        placer.Add(m, 0.5);
        if (Hidden(m)) Skip("centrosome");
        else Add("centrosome", s, m, 0.5, frame: BioFrame.Along(a1, c));
        return m;
    }

    // ---- free organelles ----

    private void Mitochondria(int count)
    {
        for (var k = 0; k < count; k++)
        {
            if (PlaceFree("mito", 1.1) is not { } p) continue;
            var rot = Rng.Rotation();
            var axis = BioV.Rotate(new BioVec(0, 1, 0), rot.Axis, rot.Degrees);
            if (Hidden(p)) { Skip("mito"); continue; }
            var half = Crossing(p, 1.1);
            var f = half ? CEGeo.Frame(axis, cameraDirection, p) : CEGeo.Frame(rot, p);
            Add("mito", CERecipes.Mitochondrion(half).Placed(f), p, 1.1, frame: f);
        }
    }

    private void Lysosomes(int count)
    {
        for (var k = 0; k < count; k++)
        {
            if (PlaceFree("lysosome", 0.3) is not { } p) continue;
            if (Hidden(p)) { Skip("lysosome"); continue; }
            Add("lysosome", CERecipes.Lysosome.Placed(new BioFrame(p)), p, 0.3);
        }
    }

    private void Peroxisomes(int count)
    {
        for (var k = 0; k < count; k++)
        {
            if (PlaceFree("peroxisome", 0.25) is not { } p) continue;
            var rot = Rng.Rotation();
            if (Hidden(p)) { Skip("peroxisome"); continue; }
            Add("peroxisome", CERecipes.Peroxisome.Placed(CEGeo.Frame(rot, p)), p, 0.25);
        }
    }

    private void FreeVesicles(int count)
    {
        for (var k = 0; k < count; k++)
        {
            var r = Rng.Uniform(0.08, 0.15);
            if (PlaceFree("vesicle", r) is not { } p) continue;
            Vesicle(p, r);
        }
    }

    /// <summary>A free ribosome at a placed centre.</summary>
    private void FreeRibosome(BioVec p, BioMesh mesh)
    {
        CEModel.Bump(Model.Extra, "ribosome.free");
        if (Hidden(p)) { Skip("ribosome"); return; }
        var s = new CEShape();
        s.Add(mesh.Translated(p), roughness: 0.5);
        Add("ribosome", s, p, RibosomeRadius);
    }

    /// <summary>Free ribosomes (placed at the teaching size in either scale, so the rest of the cell is the same).</summary>
    private void FreeRibosomes(int count, Func<BioVec, bool>? accept = null, double? m = null)
    {
        var mesh = RibosomeMesh;
        var teachingR = o.Kind == CEKind.Bacterium ? 0.020 : 0.06;
        for (var k = 0; k < count; k++)
        {
            if (PlaceFree("ribosome", teachingR, m, accept) is not { } p) continue;
            FreeRibosome(p, mesh);
        }
    }

    /// <summary>True scale's free ribosomes beyond the teaching ones, to 20,000 in all with the bound: from a second stream,
    /// placed last of all (so nothing else in the cell moves).</summary>
    private void TrueScaleRibosomes(Func<BioVec, bool>? accept = null, double? m = null)
    {
        if (!o.TrueScale) return;
        var more = Math.Max(0, 20_000 - Model.Extra.GetValueOrDefault("ribosome.er") - Model.Extra.GetValueOrDefault("ribosome.free"));
        var saved = Rng;
        Rng = new BioRandom(o.Kind.Seed() ^ 0x5CA1EF00UL);
        var mesh = RibosomeMesh;
        for (var k = 0; k < more; k++)
        {
            if (PlaceFree("ribosome.true", RibosomeRadius, m, accept) is not { } p) continue;
            FreeRibosome(p, mesh);
        }
        Rng = saved;
    }

    // ---- cytoskeleton ----

    /// <summary>Microtubules (§5.1.5): from the pericentriolar cloud's surface to the cortex, each a cubic Bézier bent up to
    /// 25°, its direction drawn again (up to 50 times) while its straight line would pass through the nucleus.</summary>
    private void Microtubules(BioVec m, int count)
    {
        var r = Filament("microtubule", 0.05);
        for (var k = 0; k < count; k++)
        {
            var d = Rng.UnitVector();
            for (var t = 0; t < 50; t++)
            {
                var along = BioV.Dot(nucleusCentre - m, d);
                if (along <= 0 || BioV.Length(nucleusCentre - m - d * along) > nucleusRadius + 0.3) break;
                d = Rng.UnitVector();
            }
            var p0 = m + d * 0.4;
            var s = 0.4;
            while (inside(m + d * (s + 0.1), 0.3) && s < 40) s += 0.1;
            var p3 = m + d * s;
            var bend = 25 * Rng.Uniform();
            var phi = 2 * Math.PI * Rng.Uniform();
            var e1 = CEGeo.Perpendicular(d);
            var e2 = BioV.Cross(d, e1);
            var off = (e1 * Math.Cos(phi) + e2 * Math.Sin(phi)) * (BioV.Distance(p0, p3) / 3 * Math.Tan(bend * Math.PI / 180));
            var pts = CEGeo.Bezier(p0, p0 + (p3 - p0) / 3 + off, p0 + (p3 - p0) * (2.0 / 3) + off, p3, 14);
            FilamentTube("microtubule", pts, r);
        }
    }

    /// <summary>A filament as a tube (its triangles in the cutaway's wedge left out).</summary>
    private void FilamentTube(string part, List<BioVec> pts, double r, bool closed = false)
    {
        var m = BioPath.Tube(pts, r, sides: 6, samples: 2, closed: closed);
        if (Cut) m = CEGeo.Filtered(m, x => !CEGeo.InWedge(x));
        var c = pts.Aggregate(BioVec.Zero, (a, x) => a + x) / pts.Count;
        var s = new CEShape();
        s.Add(m, roughness: 0.5);
        if (m.IsEmpty) { Skip(part); return; }
        Add(part, s, c, pts.Max(x => BioV.Distance(x, c)));
    }

    /// <summary>The actin cortex (§5.1.5): straight segments 0.4–0.9 µm long, each tangent to the membrane and
    /// <paramref name="depth"/> inside it, at random orientations — draws: a direction (two), the depth, the length, the angle
    /// in the tangent plane.</summary>
    private void Actin(int count, (double Lo, double Hi) depth)
    {
        var r = Filament("actin", 0.025);
        for (var k = 0; k < count; k++)
        {
            var placed = false;
            for (var t = 0; t < 20; t++)
            {
                var u = Rng.UnitVector();
                var dep = Rng.Uniform(depth.Lo, depth.Hi);
                var len = Rng.Uniform(0.4, 0.9);
                var angle = Rng.Uniform(0, 2 * Math.PI);
                var s = u * membraneInner(u);
                var n = CEGeo.Gradient(surface, s);
                var p = s - n * dep;
                var tangent = BioV.Rotate(CEGeo.Perpendicular(n), n, angle * 180 / Math.PI);
                BioVec a = p - tangent * (len / 2), b = p + tangent * (len / 2);
                if (!outsideVacuole(p, 0.1) || !inside(a, 0.05) || !inside(b, 0.05)) continue;
                placed = true;
                if (Hidden(p)) { Skip("actin"); break; }
                var sh = new CEShape();
                sh.Add(BioShapes.Cylinder(a, b, r, 6, caps: true), roughness: 0.5);
                Add("actin", sh, p, len / 2);
                break;
            }
            if (!placed) CEModel.Bump(Model.GaveUp, "actin");
        }
    }

    /// <summary>Intermediate filaments (§5.1.1, Nice): 30 wavy tubes from the nuclear envelope out to the cortex.</summary>
    private void IntermediateFilaments(int count)
    {
        var r = Filament("intermediate", 0.035);
        for (var k = 0; k < count; k++)
        {
            var d = Rng.UnitVector();
            var phase = Rng.Uniform(0, 2 * Math.PI);
            var az = Rng.Uniform(0, 2 * Math.PI);
            var start = nucleusCentre + d * (nucleusRadius + 0.15);
            var s = nucleusRadius + 0.15;
            while (inside(nucleusCentre + d * (s + 0.1), 0.3) && s < 40) s += 0.1;
            var end = nucleusCentre + d * s;
            var e1 = CEGeo.Perpendicular(d);
            var e2 = BioV.Cross(d, e1);
            var e = e1 * Math.Cos(az) + e2 * Math.Sin(az);
            var pts = Enumerable.Range(0, 16).Select(i =>
            {
                var t = i / 15.0;
                return start + (end - start) * t + e * (0.25 * Math.Sin(2 * Math.PI * 2.5 * t + phase) * Math.Sin(Math.PI * t));
            }).ToList();
            FilamentTube("intermediate", pts, r);
        }
    }

    // ---- finishing ----

    /// <summary>The labels' anchors, the counts, and every mesh turned into the scene's frame.</summary>
    internal CEModel Finish()
    {
        Anchors();
        var d = Model.Display;
        foreach (var inst in Model.Instances)
        {
            inst.Centre = d.Apply(inst.Centre);
            inst.ExplodeFrom = d.Apply(inst.ExplodeFrom);
            inst.Pieces = inst.Pieces.Select(p => p with { Mesh = p.Mesh.Transformed(d) }).ToList();
        }
        Model.Anchors = Model.Anchors.ToDictionary(kv => kv.Key,
            kv => new CEAnchor(kv.Value.Instance, d.Apply(kv.Value.Point), kv.Value.Normal is { } nrm ? d.ApplyVector(nrm) : null));
        return Model;
    }
}

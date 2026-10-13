namespace Simpl.Tools;

// The three cells, recipe by recipe (spec §5.1.1–5.1.3, §5.1.5), in the order their SplitMix64 draws are made (the Mac's
// CellExplorerCells.swift, draw for draw); where each part's label points (§3.5: an instance the camera can see —
// through the translucent membrane, or the cutaway); and Look inside. Plain .NET; see CellExplorerBuild.cs for the
// placement rule.

public sealed partial class CEBuilder
{
    // ---- animal (§5.1.1) ----

    internal void BuildAnimal()
    {
        const double r = 3.2;
        var n = new BioVec(1.0, 0.5, -0.5);
        var blob = new BioBlob(o.Kind.Seed(), 0.06);
        var ell = BioShapes.EllipsoidRadius(11, 8, 9.5);
        BioRadius pm = u => ell(u) * blob.Factor(u);
        BioRadius pmIn = u => pm(u) - 0.08;
        inside = (p, m) =>
        {
            var d = BioV.Length(p);
            return d < 1e-9 || d <= pmIn(p / d) - m;
        };
        membraneInner = pmIn;
        surface = p =>
        {
            var d = BioV.Length(p);
            return d < 1e-9 ? -1 : d - pm(p / d);
        };
        box = new BioVec(11, 8, 9.5) * 1.07;
        margin = 0.4;
        Model.CellRadius = Math.Pow(11 * 8 * 9.5, 1.0 / 3);
        Model.FrameRadius = 11.6;
        (Model.Area, Model.Volume) = CEGeo.AreaVolume(BioShapes.Radial(pm, 48, 96));

        // (fixed placements: the Golgi side g, then the membrane, nucleus, rough ER, Golgi and centrosome)
        var g = Rng.UnitVector();
        var u = CEGeo.Perpendicular(g);
        CellShell("pm", pm, pmIn, 40, 80, 8);
        Nucleus(n, r);
        RoughEr(5, g);
        GolgiStack(n + g * (r + 2.4), g, u);
        var m = Centrosome(n + BioV.Unit(g + u * 0.6) * (r + 1.1));
        // (then the free organelles, in §5.1.1's order)
        SmoothEr(5, g);
        Mitochondria(24);
        Lysosomes(12);
        Peroxisomes(8);
        FreeVesicles(30);
        FreeRibosomes(1500);
        Microtubules(m, 40);
        Actin(220, (0.15, 0.35));
        IntermediateFilaments(30);
        AnimalFlagellum(pm);
        TrueScaleRibosomes();
        if (Cut)
        {
            var s = new CEShape();
            s.Add(CERecipes.CutFaces(pmIn, null), alpha: 0.35, roughness: 0.8, order: 5);
            Add("cytoplasm", s, BioVec.Zero, 11, shell: true);
        }
        else Skip("cytoplasm");
    }

    /// <summary>A flagellum (§5.1.1, Nice; off until ticked): 10 µm long, 0.25 µm across, beating in a plane, from a basal
    /// body under the membrane.</summary>
    private void AnimalFlagellum(BioRadius pm)
    {
        var d0 = BioV.Unit(new BioVec(-1, -0.25, 0.35));
        var b = d0 * pm(d0);
        var n = CEGeo.Gradient(surface, b);
        var e = CEGeo.Perpendicular(n);
        var pts = Enumerable.Range(0, 44).Select(k =>
        {
            var s = 10.0 * k / 43;
            return b - n * 0.05 + n * s + e * (0.9 * CEGeo.Smoothstep(0, 3, s) * Math.Sin(2 * Math.PI * s / 4));
        }).ToList();
        var sh = new CEShape();
        sh.Add(BioPath.Tube(pts, 0.125, sides: 10, samples: 2), roughness: 0.45);
        sh.Add(BioShapes.Cylinder(b - n * 0.55, b - n * 0.1, 0.1, 12), CERole.Dark, roughness: 0.45);
        Add("flagellum", sh, b + n * 5, 5.2, isFixed: true);
    }

    // ---- plant (§5.1.2) ----

    internal void BuildPlant()
    {
        const double r = 3.0;
        var n = new BioVec(-9, 9.2, 0);
        var half = new BioVec(22, 13, 13);
        double Sdf(BioVec p) => CEGeo.RoundedBoxSdf(p, half, 3);
        var wallOut = BioShapes.RoundedBoxRadius(half, 3);
        var wallIn = BioShapes.RoundedBoxRadius(half, 3, -0.5);
        var pmOut = BioShapes.RoundedBoxRadius(half, 3, -0.52);
        var pmIn = BioShapes.RoundedBoxRadius(half, 3, -0.60);
        var vacBox = BioShapes.RoundedBoxRadius(new BioVec(18.5, 9.5, 9.5), 6);
        var vacBlob = new BioBlob(o.Kind.Seed(), 0.03);
        BioRadius vac = u => vacBox(u) * vacBlob.Factor(u);
        inside = (p, m) => Sdf(p) <= -0.60 - m;
        outsideVacuole = (p, m) =>
        {
            if (BioV.Distance(p, n) < r + 0.4) return true;
            var d = BioV.Length(p);
            return d >= 1e-9 && d >= vac(p / d) + m;
        };
        membraneInner = pmIn;
        surface = Sdf;
        box = half;
        margin = 0.3;
        Model.CellRadius = Math.Pow(22 * 13 * 13, 1.0 / 3);
        Model.FrameRadius = 22.5;
        (Model.Area, Model.Volume) = CEGeo.AreaVolume(BioShapes.Radial(pmOut, 48, 96));

        _ = Rng.UnitVector(); // (the "Golgi side" g: drawn as in every cell; the plant's stacks are free organelles)
        CellShell("wall", wallOut, wallIn, 48, 96, 0, darkOuter: true);
        CellShell("pm", pmOut, pmIn, 48, 96, 8);
        // (the central vacuole first: a rounded box with a blob's ripple, dented by the nucleus)
        var v = BioShapes.Radial(vac, 40, 80);
        for (var i = 0; i < v.Positions.Count; i++)
        {
            var p = BioMesh.D(v.Positions[i]);
            if (BioV.Distance(p, n) < r + 0.4) v.Positions[i] = BioMesh.F(n + BioV.Unit(p - n) * (r + 0.4));
        }
        v = BioShapes.SmoothNormals(v);
        var vs = new CEShape();
        vs.Add(v, roughness: 0.2, order: 4);
        Add("vacuole", vs, BioVec.Zero, 19);
        // (then the nucleus against the wall, and the rest)
        Nucleus(n, r);
        RoughEr(3, null);
        Plasmodesmata(30, half);
        Chloroplasts(40);
        Mitochondria(18);
        SmoothEr(3, null);
        Dictyosomes();
        Peroxisomes(6);
        // (§5.1.2's 1,200 ribosomes: 300 on the rough ER's three sheets, 900 free)
        FreeRibosomes(900);
        CorticalMicrotubules(30, half);
        Actin(120, (0.3, 1.5));
        Amyloplasts(4);
        TrueScaleRibosomes();
        if (Cut)
        {
            var s = new CEShape();
            s.Add(CERecipes.CutFaces(pmIn, vac), alpha: 0.35, roughness: 0.8, order: 5);
            Add("cytoplasm", s, BioVec.Zero, 20, shell: true);
        }
        else Skip("cytoplasm");
    }

    /// <summary>Plasmodesmata: 30 channels (r 0.06 µm) through the wall, at seeded points on its mid-surface, along its normal.</summary>
    private void Plasmodesmata(int count, BioVec half)
    {
        var mid = BioShapes.RoundedBoxRadius(half, 3, -0.25);
        for (var k = 0; k < count; k++)
        {
            var u = Rng.UnitVector();
            var p = u * mid(u);
            var n = CEGeo.Gradient(x => CEGeo.RoundedBoxSdf(x, half, 3), p);
            if (Hidden(p)) { Skip("plasmodesma"); continue; }
            var s = new CEShape();
            s.Add(BioShapes.Cylinder(p - n * 0.36, p + n * 0.36, 0.06, 10), roughness: 0.5);
            Add("plasmodesma", s, p + n * 0.36, 0.36, isFixed: true);
        }
    }

    /// <summary>Chloroplasts (§5.1.2): 40 on the cytoplasm layer's mid-surface (a rounded box 20 × 11 × 11, corner 4.5), at
    /// least 5.2 µm apart and 6.0 µm from the nucleus, their thin axis along that surface's normal (two draws for the point;
    /// one for the turn in its plane once kept).</summary>
    private void Chloroplasts(int count)
    {
        var mh = new BioVec(20, 11, 11);
        var mid = BioShapes.RoundedBoxRadius(mh, 4.5);
        var centres = new List<BioVec>();
        for (var k = 0; k < count; k++)
        {
            (BioVec P, BioVec N)? found = null;
            for (var t = 0; t < 200; t++)
            {
                var u = Rng.UnitVector();
                var p = u * mid(u);
                if (BioV.Distance(p, nucleusCentre) < 6.0) continue;
                if (centres.Any(c => BioV.Distance(c, p) < 5.2)) continue;
                found = (p, CEGeo.Gradient(x => CEGeo.RoundedBoxSdf(x, mh, 4.5), p));
                break;
            }
            if (found is not { } fp) { CEModel.Bump(Model.GaveUp, "chloroplast"); continue; }
            centres.Add(fp.P);
            placer.Add(fp.P, 2.5, blocksSer: false);
            var angle = Rng.Uniform(0, 360);
            var x = BioV.Rotate(CEGeo.Perpendicular(fp.N), fp.N, angle);
            var f = new BioFrame(fp.P, x, fp.N, BioV.Cross(x, fp.N));
            if (Hidden(fp.P)) { Skip("chloroplast"); continue; }
            var halfCut = Crossing(fp.P, 2.5);
            var frame = halfCut ? CEGeo.Frame(fp.N, cameraDirection, fp.P) : f;
            Add("chloroplast", CERecipes.Chloroplast(halfCut, halfCut, o.TrueScale ? 0.01 : 0.02).Placed(frame), fp.P, 2.5, frame: f);
        }
    }

    /// <summary>Cortical microtubules (§5.1.2): 30 hoops round the long axis, just inside the membrane — each at a position
    /// along it (one draw), from an angle (one) round an arc (one).</summary>
    private void CorticalMicrotubules(int count, BioVec half)
    {
        var r = Filament("microtubule", 0.05);
        for (var k = 0; k < count; k++)
        {
            var x = -20 + 40 * (k + 0.5) / count + Rng.Uniform(-0.4, 0.4);
            var a0 = Rng.Uniform(0, 2 * Math.PI);
            var span = Rng.Uniform(0.5 * Math.PI, 1.5 * Math.PI);
            var pts = Enumerable.Range(0, 28).Select(i =>
            {
                var a = a0 + span * i / 27;
                double lo = 0, hi = 15;
                for (var b = 0; b < 40; b++)
                {
                    var midR = (lo + hi) / 2;
                    if (CEGeo.RoundedBoxSdf(new BioVec(x, midR * Math.Cos(a), midR * Math.Sin(a)), half, 3) < -0.75) lo = midR; else hi = midR;
                }
                return new BioVec(x, lo * Math.Cos(a), lo * Math.Sin(a));
            }).ToList();
            FilamentTube("microtubule", pts, r);
        }
    }

    /// <summary>Amyloplasts (§5.1.2, Nice; off until ticked): starch-storing plastids, two membranes round three starch grains.</summary>
    private void Amyloplasts(int count)
    {
        for (var k = 0; k < count; k++)
        {
            if (PlaceFree("amyloplast", 1.0) is not { } p) continue;
            var rot = Rng.Rotation();
            if (Hidden(p)) { Skip("amyloplast"); continue; }
            var s = new CEShape();
            s.Add(BioShapes.Spheroid(1.0, 0.7, 0.7, 12, 24), alpha: 0.45, roughness: 0.3, order: 3);
            s.Add(BioShapes.Spheroid(0.95, 0.66, 0.66, 10, 20), alpha: 0.4, roughness: 0.3, order: 2);
            foreach (var c in new[] { new BioVec(-0.45, 0.05, 0), new BioVec(0.35, -0.1, 0.15), new BioVec(0.1, 0.2, -0.25) })
                s.Add(BioShapes.Spheroid(0.38, 0.3, 0.3, 8, 16).Translated(c), CERole.Colour(new BioHexPair("#f8f5ea", "#ece6d2")), roughness: 0.6);
            var f = CEGeo.Frame(rot, p);
            Add("amyloplast", s.Placed(f), p, 1.0, frame: f);
        }
    }

    // ---- bacterium (§5.1.3) ----

    internal void BuildBacterium()
    {
        var gramPositive = o.GramPositive;
        static BioRadius Shape(double r) => CEGeo.CapsuleXRadius(r, 1.0);
        var pmR = gramPositive ? (0.43, 0.44) : (0.432, 0.442);
        var wallR = gramPositive ? (0.45, 0.5) : (0.46, 0.48);
        var pmIn = Shape(pmR.Item1);
        inside = (p, m) => CEGeo.CapsuleXSdf(p, 0.5, pmR.Item1) <= -m;
        membraneInner = pmIn;
        surface = p => CEGeo.CapsuleXSdf(p, 0.5, 0.5);
        box = new BioVec(1.0, 0.5, 0.5);
        margin = 0.01;
        Model.CellRadius = Math.Pow(1.0 * 0.5 * 0.5, 1.0 / 3);
        Model.FrameRadius = 1.2;
        Model.Area = 2 * Math.PI * 0.5 * 1.0 + 4 * Math.PI * 0.25;
        Model.Volume = Math.PI * 0.25 * 1.0 + 4.0 / 3 * Math.PI * 0.125;

        CellShell("capsule", Shape(0.65), Shape(0.502), 24, 48, 9, capAlpha: 0.45);
        if (!gramPositive) CellShell("outer", Shape(0.5), Shape(0.488), 24, 48, 0);
        CellShell("wall", Shape(wallR.Item2), Shape(wallR.Item1), 24, 48, 0);
        CellShell("pm", Shape(pmR.Item2), pmIn, 24, 48, 0);
        Nucleoid();
        const double a = 0.65, b = 0.32;
        static bool OutsideNucleoid(BioVec p, double m)
        {
            double x = p.X / (a + m), y = p.Y / (b + m), z = p.Z / (b + m);
            return x * x + y * y + z * z >= 1;
        }
        // (plasmids: three loops, r 0.07–0.1 µm — the radius drawn first, then the place, then the turn)
        for (var k = 0; k < 3; k++)
        {
            var r = Rng.Uniform(0.07, 0.1);
            if (PlaceFree("plasmid", r + 0.02, 0.01, x => OutsideNucleoid(x, r + 0.02)) is not { } p) continue;
            var rot = Rng.Rotation();
            if (Hidden(p)) { Skip("plasmid"); continue; }
            var loop = Enumerable.Range(0, 24).Select(i =>
            {
                var t = 2 * Math.PI * i / 24;
                return new BioVec(r * Math.Cos(t), 0.015 * Math.Sin(3 * t), r * Math.Sin(t));
            }).ToList();
            var s = new CEShape();
            s.Add(BioPath.Tube(loop, 0.01, sides: 6, samples: 2, closed: true), roughness: 0.5);
            Add("plasmid", s.Placed(CEGeo.Frame(rot, p)), p, r);
        }
        FreeRibosomes(1500, p => OutsideNucleoid(p, 0.025), 0.005);
        Flagella(6);
        Fimbriae(120);
        SexPilus();
        TrueScaleRibosomes(p => OutsideNucleoid(p, 0.015), 0.005);
        if (Cut)
        {
            var s = new CEShape();
            s.Add(CERecipes.CutFaces(pmIn, null), alpha: 0.35, roughness: 0.8, order: 5);
            Add("cytoplasm", s, BioVec.Zero, 1, shell: true);
        }
        else Skip("cytoplasm");
    }

    /// <summary>The nucleoid (§5.1.5): a random walk from the centre, 600 steps of 0.035 µm each turning at most 50° (two
    /// draws), reflecting off the ellipsoid 0.65 × 0.32 × 0.32 µm; a tube r 0.012 µm through a Catmull–Rom spline of its points.</summary>
    private void Nucleoid()
    {
        const double a = 0.65, b = 0.32;
        static bool Outside(BioVec p) => (p.X / a) * (p.X / a) + (p.Y / b) * (p.Y / b) + (p.Z / b) * (p.Z / b) > 1;
        var p = BioVec.Zero;
        var d = Rng.UnitVector();
        var pts = new List<BioVec> { p };
        for (var k = 0; k < 600; k++)
        {
            var nd = CEGeo.Cone(d, 50, ref Rng);
            var q = p + nd * 0.035;
            if (Outside(q))
            {
                var n = BioV.Unit(new BioVec(p.X / (a * a), p.Y / (b * b), p.Z / (b * b)));
                nd = BioV.Unit(nd - n * (2 * BioV.Dot(nd, n)));
                q = p + nd * 0.035;
                if (Outside(q)) q = p;
            }
            pts.Add(q);
            p = q;
            d = nd;
        }
        var s = new CEShape();
        s.Add(BioPath.Tube(pts, 0.012, sides: 6, samples: 2), roughness: 0.55);
        Add("nucleoid", s, BioVec.Zero, 0.65);
    }

    /// <summary>A point on the bacterium's outer surface (r 0.5) along a direction, and its outward normal.</summary>
    private static (BioVec P, BioVec N) EnvelopePoint(BioVec u)
    {
        var p = u * CEGeo.CapsuleXRadius(0.5, 1.0)(u);
        return (p, CEGeo.Gradient(x => CEGeo.CapsuleXSdf(x, 0.5, 0.5), p));
    }

    /// <summary>Flagella (§5.1.5): six, each from a point on the surface — a hook bending from the normal to a direction 70°
    /// toward the rear pole (−x), then a left-handed helix about it (radius 0.2 µm, pitch 2.3 µm, 6 µm long), and a
    /// basal-body disc at the plasma membrane.</summary>
    private void Flagella(int count)
    {
        var r = Filament("flagellum", 0.03);
        for (var k = 0; k < count; k++)
        {
            var (p, n) = EnvelopePoint(Rng.UnitVector());
            var t = new BioVec(-1, 0, 0) - n * BioV.Dot(new BioVec(-1, 0, 0), n);
            t = BioV.Length(t) < 1e-6 ? CEGeo.Perpendicular(n) : BioV.Unit(t);
            var d = BioV.Unit(n * Math.Cos(70 * Math.PI / 180) + t * Math.Sin(70 * Math.PI / 180));
            var ph = p + n * 0.06 + d * 0.06;
            var hook = CEGeo.Bezier(p, p + n * 0.05, ph - d * 0.03, ph, 7);
            var helix = FlagellumHelix(ph, d);
            if (Hidden(p)) { Skip("flagellum"); continue; }
            var s = new CEShape();
            s.Add(BioPath.Tube(hook, r * 1.15, sides: 6, samples: 2), roughness: 0.45);
            s.Add(BioPath.Tube(helix, r, sides: 6, samples: 2), roughness: 0.45);
            s.Add(BioShapes.Cylinder(p - n * 0.068, p - n * 0.048, 0.022, 12), CERole.Dark, roughness: 0.45);
            s.Add(BioShapes.Cylinder(p - n * 0.058, p, 0.008, 6), CERole.Dark, roughness: 0.45);
            Add("flagellum", s, helix[helix.Count / 3], 3.2, isFixed: true);
        }
    }

    /// <summary>A flagellum's filament (§5.1.5): a helix about the axis d from the hook's end, radius 0.2 µm (growing to it
    /// over the first half-turn, so it starts on the hook), pitch 2.3 µm, 6 µm long; left-handed, E. coli's swimming form
    /// (e₂ = d × e₁ would give right-handed: the sine's sign is swapped). 24 points a turn.</summary>
    public static List<BioVec> FlagellumHelix(BioVec ph, BioVec d)
    {
        var e1 = CEGeo.Perpendicular(d);
        var e2 = BioV.Cross(d, e1);
        var turns = 6 / 2.3;
        var end = 2 * Math.PI * turns;
        var n = (int)Math.Ceiling(turns * 24);
        return Enumerable.Range(0, n + 1).Select(k =>
        {
            var a = end * k / n;
            var rh = 0.2 * CEGeo.Smoothstep(0, Math.PI, a);
            return ph + d * (a * 2.3 / (2 * Math.PI)) + e1 * (rh * (Math.Cos(a) - 1)) - e2 * (rh * Math.Sin(a));
        }).ToList();
    }

    /// <summary>Fimbriae: 120 straight hairs 0.4–0.9 µm long, normal to the surface (two draws for where, one for the length).</summary>
    private void Fimbriae(int count)
    {
        var r = Filament("fimbria", 0.012);
        for (var k = 0; k < count; k++)
        {
            var (p, n) = EnvelopePoint(Rng.UnitVector());
            var len = Rng.Uniform(0.4, 0.9);
            if (Hidden(p)) { Skip("fimbria"); continue; }
            var s = new CEShape();
            s.Add(BioShapes.Cylinder(p - n * 0.005, p + n * len, r, 6), roughness: 0.5);
            Add("fimbria", s, p + n * (len / 2), len / 2, isFixed: true);
        }
    }

    /// <summary>The sex pilus (§5.1.3, Nice): one tube 2 µm long, slightly curved.</summary>
    private void SexPilus()
    {
        var r = Filament("sexpilus", 0.0135);
        var (p, n) = EnvelopePoint(Rng.UnitVector());
        var side = CEGeo.Perpendicular(n);
        var end = p + n * 1.95 + side * 0.35;
        var pts = CEGeo.Bezier(p, p + n * 0.7, p + n * 1.4 + side * 0.15, end, 16);
        if (Hidden(p)) { Skip("sexpilus"); return; }
        var s = new CEShape();
        s.Add(BioPath.Tube(pts, r, sides: 8, samples: 2), roughness: 0.5);
        Add("sexpilus", s, pts[8], 1.0, isFixed: true);
    }

    // ---- where the labels point (§3.5) ----

    /// <summary>Whether a point inside the cell can be seen from the camera: not behind an opaque wall (unless the ray leaves
    /// through the cutaway), nor behind the nucleus (unless through its cut).</summary>
    private bool CanSee(BioVec p)
    {
        var v = cameraDirection;
        // (an opaque wall: the plant's, the bacterium's envelope)
        if (o.Kind != CEKind.Animal)
        {
            var t = 0.0;
            while (inside(p + v * t, 0) && t < 60) t += 0.02 * Model.CellRadius;
            if (!(Cut && CEGeo.InWedge(p + v * t))) return false;
        }
        // (the nucleus, opaque but for its cut octant)
        if (nucleusRadius > 0)
        {
            var n = nucleusCentre;
            var r = nucleusRadius;
            var w = p - n;
            double b = BioV.Dot(w, v), c = BioV.Dot(w, w) - r * r;
            var disc = b * b - c;
            if (disc > 0)
            {
                double t1 = -b - Math.Sqrt(disc), t2 = -b + Math.Sqrt(disc);
                // (inside the nucleus: seen only through its cut, where the ray leaves it)
                if (c < 0) return Cut && CEGeo.InWedge(p + v * t2, n);
                if (t1 > 0 && !(Cut && CEGeo.InWedge(p + v * t1, n) && CEGeo.InWedge(p + v * t2, n))) return false;
            }
        }
        return true;
    }

    /// <summary>A point's score as a label's anchor: seen first, then nearer the camera.</summary>
    private double Score(BioVec p) => (CanSee(p) ? 100 : 0) + BioV.Dot(p, cameraDirection) / Model.CellRadius;

    /// <summary>Each part's anchor: for a convex outer shell, a point on it facing the camera (with its normal, §3.5 item 1);
    /// for what lies under an opaque wall, a point on its cut face; for the rest, the instance (or a point of it) the camera
    /// sees best.</summary>
    internal void Anchors()
    {
        var v = cameraDirection;
        var best = new Dictionary<string, (double Score, CEAnchor Anchor)>();
        void Offer(string part, int inst, BioVec p, BioVec? normal = null, double? score = null)
        {
            var sc = score ?? Score(p);
            if (!best.TryGetValue(part, out var was) || sc > was.Score) best[part] = (sc, new CEAnchor(inst, p, normal));
        }
        // (a direction like the camera's, moved off the cut octant toward `away`)
        BioVec Facing(BioVec away)
        {
            var d = v;
            var k = 0;
            while (d.X > 0 && d.Y > 0 && d.Z > 0 && k < 40) { d = BioV.Unit(d + away * 0.15); k++; }
            return BioV.Unit(d + away * 0.25);
        }
        // (the vertex of an instance's meshes nearest the camera, and its normal)
        (BioVec, BioVec)? Front(CEInstance inst)
        {
            (BioVec, BioVec)? bestP = null;
            var bestD = double.NegativeInfinity;
            foreach (var piece in inst.Pieces)
                for (var k = 0; k < piece.Mesh.Positions.Count; k++)
                {
                    var p = BioMesh.D(piece.Mesh.Positions[k]);
                    var dd = BioV.Dot(p, v);
                    if (dd > bestD) { bestD = dd; bestP = (p, BioMesh.D(piece.Mesh.Normals[k])); }
                }
            return bestP;
        }
        // (a point on a shell's cut face on the plane z = 0, at direction (cos a, sin a) between radii)
        void CutFace(string part, int i, double a, BioRadius rIn, BioRadius rOut)
        {
            if (!Cut) return;
            var d = new BioVec(Math.Cos(a), Math.Sin(a), 0);
            Offer(part, i, d * ((rIn(d) + rOut(d)) / 2), new BioVec(0, 0, 1), 1);
        }
        var bacteriumPm = o.GramPositive ? (0.43, 0.44) : (0.432, 0.442);
        var bacteriumWall = o.GramPositive ? (0.45, 0.5) : (0.46, 0.48);
        static BioRadius CapsuleX(double r) => CEGeo.CapsuleXRadius(r, 1.0);
        for (var i = 0; i < Model.Instances.Count; i++)
        {
            var inst = Model.Instances[i];
            if (inst.Pieces.Count == 0) continue;
            var part = inst.Part;
            switch (o.Kind, part)
            {
                case (CEKind.Animal, "pm"):
                {
                    var d = Facing(new BioVec(-1, 0.2, 0));
                    var p = d * (membraneInner(d) + 0.08);
                    Offer(part, i, p, CEGeo.Gradient(surface, p), 1);
                    break;
                }
                case (CEKind.Plant, "pm"):
                    CutFace(part, i, 0.62, membraneInner, x => membraneInner(x) + 0.08);
                    break;
                case (CEKind.Bacterium, "pm"):
                    CutFace(part, i, 0.5, CapsuleX(bacteriumPm.Item1), CapsuleX(bacteriumPm.Item2));
                    break;
                case (CEKind.Bacterium, "wall") when !o.GramPositive:
                    CutFace(part, i, 0.95, CapsuleX(bacteriumWall.Item1), CapsuleX(bacteriumWall.Item2));
                    break;
                case (CEKind.Plant, "wall") or (CEKind.Bacterium, "wall") or (CEKind.Bacterium, "capsule") or (CEKind.Bacterium, "outer"):
                {
                    var away = part == "wall" ? new BioVec(-1, -0.6, 0) : part == "capsule" ? new BioVec(-1, 0.5, 0) : new BioVec(-0.4, -1, 0);
                    var d = Facing(away);
                    var plantShape = o.Kind == CEKind.Plant;
                    var f = plantShape ? BioShapes.RoundedBoxRadius(new BioVec(22, 13, 13), 3) : CapsuleX(part == "capsule" ? 0.65 : 0.5);
                    var p = d * f(d);
                    var radius = part == "capsule" ? 0.65 : 0.5;
                    Func<BioVec, double> sdf = plantShape ? x => CEGeo.RoundedBoxSdf(x, new BioVec(22, 13, 13), 3) : x => CEGeo.CapsuleXSdf(x, 0.5, radius);
                    Offer(part, i, p, CEGeo.Gradient(sdf, p), 1);
                    break;
                }
                case (_, "cytoplasm"):
                {
                    // (its cut face on the plane z = 0, between the membrane and the vacuole, or toward the centre)
                    var d = BioV.Unit(new BioVec(0.62, 0.78, 0));
                    var outer = membraneInner(d);
                    Offer(part, i, d * (o.Kind == CEKind.Plant ? outer - 1.5 : outer * 0.55), new BioVec(0, 0, 1), 1);
                    break;
                }
                case (_, "vacuole"):
                    if (Front(inst) is { } fv) Offer(part, i, fv.Item1, fv.Item2, 1);
                    break;
                case (_, "nucleus"):
                {
                    var n = nucleusCentre;
                    var r = nucleusRadius;
                    // (cut: the back of the inner membrane, seen through the cut)
                    if (Cut) Offer(part, i, n - v * (r - 0.065), score: 1);
                    else
                    {
                        var d = Facing(new BioVec(-1, 0, 0));
                        Offer(part, i, n + d * r, d, 1);
                    }
                    break;
                }
                case (_, "pore"):
                {
                    var d = BioV.Unit(inst.Centre - nucleusCentre);
                    // (the pores above the rough ER's sheets, facing the camera)
                    Offer(part, i, inst.Centre, score: (Math.Acos(Math.Min(Math.Max(d.Y, -1), 1)) < 0.22 * Math.PI ? 10 : 0) + BioV.Dot(d, v));
                    break;
                }
                case (_, "rer"):
                    foreach (var piece in inst.Pieces)
                    {
                        var m = piece.Mesh;
                        for (var k = 0; k < m.Positions.Count; k += Math.Max(1, m.Positions.Count / 60))
                        {
                            var p = BioMesh.D(m.Positions[k]);
                            Offer(part, i, p, score: Score(p) + BioV.Distance(p, nucleusCentre));
                        }
                    }
                    break;
                case (_, "ser") or (_, "chromatin") or (_, "microtubule") or (_, "intermediate"):
                    foreach (var piece in inst.Pieces)
                    {
                        var m = piece.Mesh;
                        for (var k = 0; k < m.Positions.Count; k += Math.Max(1, m.Positions.Count / 12)) Offer(part, i, BioMesh.D(m.Positions[k]));
                    }
                    break;
                case (_, "flagellum") or (_, "fimbria") or (_, "sexpilus") or (_, "plasmodesma"):
                    Offer(part, i, inst.Centre, score: BioV.Dot(BioV.Unit(inst.Centre), v));
                    break;
                default:
                    Offer(part, i, inst.Centre);
                    break;
            }
        }
        Model.Anchors = best.ToDictionary(kv => kv.Key, kv => kv.Value.Anchor);
    }

    // ---- look inside ----

    /// <summary>One mitochondrion or chloroplast (the one its label points to) cut lengthwise, its inside toward the camera; or
    /// the nucleus cut open with its pores, nucleolus and chromatin. In the scene's frame.</summary>
    internal List<CEInstance> LookInside(string part)
    {
        var d = Model.Display;
        List<CEInstance> out_;
        switch (part)
        {
            case "mito" or "chloroplast":
            {
                if (!Model.Anchors.TryGetValue(part, out var a) || Model.Instances[a.Instance].Frame is not { } f) return new List<CEInstance>();
                var half = CEGeo.Frame(f.Y, cameraDirection, f.Origin);
                var shape = part == "mito" ? CERecipes.Mitochondrion(true) : CERecipes.Chloroplast(true, true, o.TrueScale ? 0.01 : 0.02);
                var b = new CEBuilder(o);
                b.Add(part, shape.Placed(half), f.Origin, Model.Instances[a.Instance].Radius, frame: f);
                out_ = b.Model.Instances;
                break;
            }
            case "nucleus":
            {
                // (built again cut, from the same draws: the Golgi side first, as the cell did)
                var b = new CEBuilder(o with { Cutaway = true });
                _ = b.Rng.UnitVector();
                b.Nucleus(o.Kind == CEKind.Animal ? new BioVec(1.0, 0.5, -0.5) : new BioVec(-9, 9.2, 0), o.Kind == CEKind.Animal ? 3.2 : 3.0);
                out_ = b.Model.Instances;
                break;
            }
            default:
                return new List<CEInstance>();
        }
        foreach (var inst in out_)
        {
            inst.Centre = d.Apply(inst.Centre);
            inst.ExplodeFrom = inst.Centre;
            inst.Pieces = inst.Pieces.Select(p => p with { Mesh = p.Mesh.Transformed(d) }).ToList();
        }
        return out_;
    }
}

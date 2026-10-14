namespace Simpl.Tools;

// The Cell Explorer's Membrane page (spec §4.1.1), the Mac's CellMembraneModel.swift: a square patch of plasma membrane
// 60 nm × 60 nm, in nanometres, cut on its two near sides to show the bilayer. Phospholipids on a hexagonal grid 0.8 nm
// apart in each leaflet (jittered ±0.08 nm), head centres at ±1.95 nm (a bilayer about 4 nm thick), each a head (r 0.45 nm;
// phosphatidylcholine #e8a33d, the others #e8c13d) and two wavy tails (r 0.22 nm, 1.75 nm long, a sine of 0.12 nm along
// them), half the lipids with one tail kinked 30° at its middle (unsaturated); 12% of the slots cholesterol (a rigid rod
// 0.5 × 0.2 × 1.6 nm among the tails, its OH bead at the heads' level); six transmembrane proteins, each seven α-helix
// cylinders (r 0.5 nm, 4.5 nm long) round a circle of radius 1.2 nm — one a channel (an aquaporin's open pore), one a
// carrier (a closed bundle that rocks open to one side then the other), two glycoproteins (branched chains of hexagonal
// sugar beads on the outside); three peripheral proteins (blobs r 1.5 nm) on the inner face; four glycolipids (three sugar
// beads). Everything inside the patch is hidden under the heads, so tails and cholesterol rods are drawn only near the cut
// sides, where they show. Plain .NET.

/// <summary>A part of the membrane, as its card tells it.</summary>
public sealed record CEMembranePart(string Id, string Name, BioHexPair Colour, string Function, string Structure, string? Mixup = null);

/// <summary>A unit quaternion (w, x, y, z): the carrier's helices' turns, as the view animates them.</summary>
public readonly record struct CEQuat(double W, double X, double Y, double Z)
{
    public static CEQuat FromAxis(BioVec axis, double degrees)
    {
        var u = BioV.Unit(axis);
        var h = degrees * Math.PI / 360;
        return new CEQuat(Math.Cos(h), u.X * Math.Sin(h), u.Y * Math.Sin(h), u.Z * Math.Sin(h));
    }

    public CEQuat Times(CEQuat q) => new(W * q.W - X * q.X - Y * q.Y - Z * q.Z,
        W * q.X + X * q.W + Y * q.Z - Z * q.Y,
        W * q.Y - X * q.Z + Y * q.W + Z * q.X,
        W * q.Z + X * q.Y - Y * q.X + Z * q.W);

    public BioVec Rotate(BioVec v)
    {
        var q = new BioVec(X, Y, Z);
        var t = BioV.Cross(q, v) * 2;
        return v + t * W + BioV.Cross(q, t);
    }

    /// <summary>Its axis and angle (degrees).</summary>
    public (BioVec Axis, double Degrees) AxisAngle
    {
        get
        {
            var w = Math.Min(Math.Max(W, -1), 1);
            var angle = 2 * Math.Acos(w);
            var s = Math.Sqrt(1 - w * w);
            return s < 1e-9 ? (new BioVec(0, 1, 0), 0) : (new BioVec(X / s, Y / s, Z / s), angle * 180 / Math.PI);
        }
    }

    /// <summary>A rotation frame's quaternion (its axes the turned x, y, z).</summary>
    public static CEQuat Of(BioFrame f)
    {
        double m00 = f.X.X, m11 = f.Y.Y, m22 = f.Z.Z;
        double m01 = f.Y.X, m02 = f.Z.X, m10 = f.X.Y, m12 = f.Z.Y, m20 = f.X.Z, m21 = f.Y.Z;
        var tr = m00 + m11 + m22;
        if (tr > 0)
        {
            var s = Math.Sqrt(tr + 1) * 2;
            return new CEQuat(s / 4, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s);
        }
        if (m00 > m11 && m00 > m22)
        {
            var s = Math.Sqrt(1 + m00 - m11 - m22) * 2;
            return new CEQuat((m21 - m12) / s, s / 4, (m01 + m10) / s, (m02 + m20) / s);
        }
        if (m11 > m22)
        {
            var s = Math.Sqrt(1 + m11 - m00 - m22) * 2;
            return new CEQuat((m02 - m20) / s, (m01 + m10) / s, s / 4, (m12 + m21) / s);
        }
        var s2 = Math.Sqrt(1 + m22 - m00 - m11) * 2;
        return new CEQuat((m10 - m01) / s2, (m02 + m20) / s2, (m12 + m21) / s2, s2 / 4);
    }
}

public static class CEMembrane
{
    /// <summary>The patch's seed (SplitMix64): every launch the same patch.</summary>
    public const ulong Seed = 0x4D454D42;

    public static readonly BioHexPair PC = new("#e8a33d", "#f0b257");
    public static readonly BioHexPair OtherHead = new("#e8c13d", "#f0cd57");
    public static readonly BioHexPair Tail = new("#d9cc9f", "#8f8463");
    public static readonly BioHexPair CholesterolColour = new("#c4a23a", "#e3c55c");
    public static readonly BioHexPair Hydroxyl = new("#d63a34", "#ff6961");
    public static readonly BioHexPair IntegralColour = new("#4f7fd6", "#7aa7ff");
    public static readonly BioHexPair ChannelColour = new("#2f9fb8", "#5cc8de");
    public static readonly BioHexPair CarrierColour = new("#9b6bd1", "#c39cf5");
    public static readonly BioHexPair PeripheralColour = new("#d4884a", "#f0a868");
    public static readonly BioHexPair Sugar = new("#3fae5f", "#6fd88f");
    public static readonly BioHexPair Water = new("#3b82f6", "#6ea8ff");

    /// <summary>The parts with labels (§4.1.1's twelve), in the parts list's order.</summary>
    public static readonly IReadOnlyList<CEMembranePart> Parts = new CEMembranePart[]
    {
        new("bilayer", "Phospholipid bilayer", PC,
            "Two layers of phospholipids, heads outward and tails inward, form the barrier: small nonpolar molecules (O₂, CO₂) slip through, while ions and large polar molecules need proteins to cross.",
            "About 4–5 nm; up to about 10 nm counting the proteins that stick out.",
            "It is fluid, not fixed: lipids and many proteins drift sideways (the fluid mosaic model)."),
        new("bilayer:heads", "Hydrophilic head (polar)", PC,
            "Each phospholipid's head (a phosphate group, often with choline) is polar, so the heads face the water on both sides: outside the cell and the cytosol.",
            "Drawn as a bead 0.9 nm across; phosphatidylcholine darker, other phospholipids lighter."),
        new("bilayer:tails", "Hydrophobic tails (nonpolar)", Tail,
            "Two fatty-acid tails per phospholipid, nonpolar, so they hide from water in the middle of the bilayer. A kink from a cis double bond (an unsaturated tail) stops them packing tightly and keeps the membrane fluid.",
            "About 1.75 nm long; the hydrophobic core is about 3 nm thick.",
            "Tails point inward and heads outward in both layers, never the other way round."),
        new("cholesterol", "Cholesterol", CholesterolColour,
            "A rigid ring-shaped lipid tucked among the tails, its OH at the heads' level. It steadies fluidity: less fluid when warm, and stops the tails packing solid when cold.",
            "In animal membranes (plants have similar sterols). Here 12% of the lipid places.",
            "Cholesterol sits inside the bilayer, among the tails, not on its surface."),
        new("integral", "Integral (transmembrane) protein", IntegralColour,
            "Spans the whole bilayer. Its middle is nonpolar (it sits among the tails) and its ends are polar. Integral proteins work as channels, carriers, receptors and enzymes.",
            "Many cross the bilayer as a bundle of α-helices; here seven helices, each 4.5 nm long."),
        new("integral:channel", "Channel protein", ChannelColour,
            "A pore that lets one kind of small molecule or ion through by facilitated diffusion, down its concentration gradient, with no energy used. Aquaporins like this one let water through: osmosis.",
            "Helices round an open centre.",
            "A channel does not change shape to pass each molecule; a carrier does."),
        new("integral:carrier", "Carrier protein", CarrierColour,
            "Binds a particular molecule and changes shape to carry it across, open to one side then the other. Passive carriers move it down its gradient; pumps use ATP to move it against (active transport).",
            "A closed bundle that alternates its opening."),
        new("peripheral", "Peripheral protein", PeripheralColour,
            "Sits on one face of the membrane (here the cytoplasm side), held by the heads or by integral proteins; often an enzyme, or an anchor for the cytoskeleton.",
            "Does not enter the hydrophobic core."),
        new("glycoprotein", "Glycoprotein", Sugar,
            "A membrane protein with a chain of sugars on the outside face: cell recognition (self and non-self), signalling and cells sticking together.",
            "Branched chains of sugar rings on the extracellular side only."),
        new("glycolipid", "Glycolipid", Sugar,
            "A lipid with a short sugar chain on the outside face: cell recognition, like the A and B blood-group markers.",
            "Its sugars face outside the cell."),
        new("side:outside", "Extracellular side", new BioHexPair("#8e8e93", "#aeaeb2"),
            "Outside the cell. The sugar chains of glycoproteins and glycolipids face this side.",
            "Watery: the heads face it."),
        new("side:inside", "Cytoplasm side", new BioHexPair("#8e8e93", "#aeaeb2"),
            "Inside the cell, the cytosol. Peripheral proteins and the cytoskeleton attach on this side.",
            "Watery: the heads face it too."),
    };

    public static CEMembranePart? Part(string id) => Parts.FirstOrDefault(p => p.Id == id);

    /// <summary>The part an object of the patch belongs to ("part:integral:protein:5" → "integral", "part:bilayer:heads:pc" →
    /// "bilayer:heads"), or null.</summary>
    public static string? PartOf(string? id)
    {
        if (id == null) return null;
        var s = id.StartsWith("part:", StringComparison.Ordinal) ? id[5..] : id;
        return Parts.Select(p => p.Id).Where(p => s == p || s.StartsWith(p + ":", StringComparison.Ordinal))
            .OrderByDescending(p => p.Length).FirstOrDefault() is { } best
            // ("integral:protein:5" is an integral protein, not a channel)
            ? best : null;
    }

    /// <summary>The patch's turn for the view (its two cut sides, +x and +z, toward the camera; seen from a little above).</summary>
    public static readonly BioFrame Display = BioFrame.Rotation(new BioVec(0, 1, 0), -30).Then(BioFrame.Rotation(new BioVec(1, 0, 0), 26));

    /// <summary>The integral proteins' places in the patch (x, z in nm): the channel, the carrier, two glycoproteins, two more.</summary>
    public static readonly (string Role, double X, double Z)[] Proteins =
    {
        ("channel", 11, 22), ("carrier", -13, 20), ("glyco", -6, -9), ("glyco", 17, -13), ("plain", -21, -19), ("plain", 3, 5),
    };

    public static readonly (double X, double Z)[] PeripheralPlaces = { (5, 26.5), (-19, 2), (15, -16) };
    public static readonly (double X, double Z)[] GlycolipidPlaces = { (-22, 5), (6, -21), (23, 7), (-1, 25.5) };
    public const double Half = 30;
    public const double HeadY = 1.95;

    /// <summary>The built patch: its scene, its counts, and what the view animates — the carrier's helices (each one's centre
    /// and its turn in the two states, open outside and open inside, in the scene's frame), where water beads enter and
    /// leave the channel, the carrier's solute path, the osmosis arrow, and the membrane's normal (the scene's up).</summary>
    public sealed class Model
    {
        public BioSceneContent Content { get; set; } = new();
        public int Lipids { get; set; }
        public int Cholesterol { get; set; }
        public int Tails { get; set; }
        public int Triangles { get; set; }
        public List<(BioVec Centre, CEQuat Outside, CEQuat Inside)> Carrier { get; } = new();
        public (BioVec In, BioVec Out) ChannelPath { get; set; }
        public List<BioVec> CarrierPath { get; set; } = new();
        public (BioVec From, BioVec To) Arrow { get; set; }
        public BioVec Up { get; set; } = new(0, 1, 0);
    }

    /// <summary>The patch, with the parts shown and, while the carrier is animated (in the view), the carrier left out of the
    /// batched scene.</summary>
    public static Model Build(bool dark, bool cholesterol = true, bool glyco = true, bool proteins = true, bool carrierAnimated = false)
    {
        var rng = new BioRandom(Seed);
        var out_ = new Model();
        var items = new List<BioSceneItem>();
        var d = Display;
        void Put(string id, BioMesh m, BioHexPair c, double alpha = 1, double roughness = 0.45, int order = 0)
        {
            if (m.IsEmpty) return;
            items.Add(new BioSceneItem(id, m.Transformed(d), new BioMaterial(c.HexFor(dark), alpha, roughness), order));
        }
        const double edge = 1.7;
        static bool NearCut(double x, double z) => x > Half - edge || z > Half - edge;
        var head = BioShapes.Icosphere(0.45, 0);
        // (cholesterol's OH: a bead 0.3 nm across among the heads, an octahedron)
        var bead = CEGeo.Octahedron(0.15);
        BioMesh pcHeads = new(), otherHeads = new(), tails = new(), cholRods = new(), cholOh = new();
        var glycoLipids = GlycolipidPlaces.Select(_ => new BioMesh()).ToArray();
        var glycoSugar = GlycolipidPlaces.Select(_ => new BioMesh()).ToArray();
        // (the glycolipids: the top leaflet's slots nearest their places)
        var glycoSlot = new HashSet<int>();
        var rowStep = 0.8 * Math.Sqrt(3.0) / 2;
        foreach (var (leaflet, sign) in new[] { (0, 1.0), (1, -1.0) })
        {
            var j = 0;
            var z = -Half + 0.4;
            while (z < Half - 0.2)
            {
                var x = -Half + 0.4 + (j % 2 == 1 ? 0.4 : 0);
                for (; x < Half - 0.2; x += 0.8)
                {
                    var jx = rng.Uniform(-0.08, 0.08);
                    var jz = rng.Uniform(-0.08, 0.08);
                    var roll = rng.Uniform();
                    var isPc = rng.Uniform() < 0.5;
                    var kinked = rng.Uniform() < 0.5;
                    var azimuth = rng.Uniform(0, 2 * Math.PI);
                    double px = x + jx, pz = z + jz;
                    // (no lipid where a transmembrane protein sits)
                    if (Proteins.Any(p => Hypot(p.X - px, p.Z - pz) < 2.3)) continue;
                    var y = HeadY * sign;
                    if (roll < 0.12)
                    {
                        // (cholesterol: its OH bead at the heads' level, its rod among the tails)
                        out_.Cholesterol++;
                        if (cholesterol)
                        {
                            cholOh.Append(bead.Translated(new BioVec(px, sign * 1.72, pz)));
                            if (NearCut(px, pz))
                            {
                                var rot = BioFrame.Rotation(new BioVec(0, 1, 0), azimuth * 180 / Math.PI, new BioVec(px, sign * (1.72 - 0.95), pz));
                                cholRods.Append(CEGeo.Box(new BioVec(0.25, 0.8, 0.1)).Transformed(rot));
                            }
                        }
                        continue;
                    }
                    out_.Lipids++;
                    var h = head.Translated(new BioVec(px, y, pz));
                    // (a glycolipid in the top leaflet, at the slot nearest each of its places)
                    int? glycoIndex = null;
                    if (leaflet == 0)
                        for (var k = 0; k < GlycolipidPlaces.Length; k++)
                        {
                            var g = GlycolipidPlaces[k];
                            if (Hypot(g.X - px, g.Z - pz) < 0.5 && glycoSlot.Add(k)) glycoIndex = k;
                        }
                    if (glycoIndex is { } gi)
                    {
                        glycoLipids[gi].Append(h);
                        for (var s = 0; s < 3; s++)
                        {
                            var c = new BioVec(px + 0.15 * (s % 2), y + 0.45 + 0.35 + 0.62 * s, pz);
                            glycoSugar[gi].Append(Hexagon(c, new BioVec(1, 0.2 * s, 0.3)));
                        }
                    }
                    else if (isPc) pcHeads.Append(h);
                    else otherHeads.Append(h);
                    if (NearCut(px, pz) || glycoIndex != null)
                    {
                        out_.Tails += 2;
                        var t = LipidTails(new BioVec(px, y, pz), sign, azimuth, kinked);
                        if (glycoIndex is { } g2) glycoLipids[g2].Append(t); else tails.Append(t);
                    }
                }
                z += rowStep;
                j++;
            }
        }
        Put("part:bilayer:heads:pc", pcHeads, PC, roughness: 0.4);
        Put("part:bilayer:heads:other", otherHeads, OtherHead, roughness: 0.4);
        Put("part:bilayer:tails", tails, Tail, roughness: 0.55);
        if (cholesterol)
        {
            Put("part:cholesterol", cholRods, CholesterolColour, roughness: 0.4);
            Put("part:cholesterol", cholOh, Hydroxyl, roughness: 0.4);
        }
        for (var k = 0; k < GlycolipidPlaces.Length; k++)
        {
            Put($"part:glycolipid:{k}", glycoLipids[k], OtherHead, roughness: 0.4);
            if (glyco) Put($"part:glycolipid:{k}", glycoSugar[k], Sugar, roughness: 0.45);
        }
        // (the transmembrane proteins: seven helices round a circle r 1.2 nm)
        var glycoCount = 0;
        for (var k = 0; k < Proteins.Length; k++)
        {
            var p = Proteins[k];
            var c = new BioVec(p.X, 0, p.Z);
            var (id, colour) = p.Role switch
            {
                "channel" => ("part:integral:channel", ChannelColour),
                "carrier" => ("part:integral:carrier", CarrierColour),
                _ => ($"part:integral:protein:{k}", IntegralColour),
            };
            var bundle = new BioMesh();
            for (var hx = 0; hx < 7; hx++)
            {
                var a = 2 * Math.PI * hx / 7;
                var at = c + new BioVec(1.2 * Math.Cos(a), 0, 1.2 * Math.Sin(a));
                var helix = BioShapes.Cylinder(new BioVec(0, -2.25, 0), new BioVec(0, 2.25, 0), 0.5, 12);
                // (a closed protein's helices lean in a little; the channel's stand straight round its pore; the carrier's
                // splay open toward the outside)
                var tilt = p.Role == "channel" ? 0.0 : p.Role == "carrier" ? 12 : 6;
                var tangent = new BioVec(-Math.Sin(a), 0, Math.Cos(a));
                var sideways = p.Role == "carrier" ? -tilt : tilt;
                helix = helix.Transformed(BioFrame.Rotation(tangent, sideways, at));
                bundle.Append(helix);
                if (p.Role == "carrier") out_.Carrier.Add((d.Apply(at), Turn(tangent, -tilt), Turn(tangent, tilt)));
            }
            if (p.Role == "carrier" && carrierAnimated) continue;
            if (p.Role == "channel")
            {
                // (the pore's mouths: a ring at each face, standing out of the heads)
                var ring = BioShapes.Torus(1.25, 0.5, 28, 10);
                bundle.Append(ring.Translated(c + new BioVec(0, 2.75, 0)));
                bundle.Append(ring.Translated(c + new BioVec(0, -2.75, 0)));
            }
            else if (p.Role != "carrier")
            {
                // (a closed protein's loops and domains, outside and inside: they stick out past the heads)
                var cap = BioShapes.Radial(BioShapes.Blob(BioShapes.EllipsoidRadius(1.9, 1.0, 1.9), new BioBlob(unchecked(Seed + (ulong)k), 0.1)), 12, 24);
                bundle.Append(cap.Translated(c + new BioVec(0, 2.95, 0)));
                bundle.Append(cap.Translated(c + new BioVec(0, -2.95, 0)));
            }
            if (proteins || p.Role is "channel" or "carrier") Put(id, bundle, colour, roughness: 0.4);
            if (p.Role == "glyco")
            {
                if (glyco) Put($"part:glycoprotein:{glycoCount}", GlycoChain(c + new BioVec(0.4, 4.1, 0.2), ref rng), Sugar, roughness: 0.45);
                glycoCount++;
            }
        }
        // (peripheral proteins: blobs r 1.5 nm touching the inner face)
        for (var k = 0; k < PeripheralPlaces.Length; k++)
        {
            var p = PeripheralPlaces[k];
            var m = BioShapes.Radial(BioShapes.Blob(BioShapes.SphereRadius(1.5), new BioBlob(unchecked(Seed + 10 + (ulong)k), 0.1)), 16, 32);
            Put($"part:peripheral:{k}", m.Translated(new BioVec(p.X, -HeadY - 0.45 - 1.45, p.Z)), PeripheralColour, roughness: 0.5);
        }
        // (the paths the view animates: water down the channel, the carrier's solute, the osmosis arrow)
        var ch = Proteins[0];
        var ca = Proteins[1];
        out_.ChannelPath = (d.Apply(new BioVec(ch.X, 4.2, ch.Z)), d.Apply(new BioVec(ch.X, -4.2, ch.Z)));
        out_.CarrierPath = new[] { new BioVec(ca.X, 4.5, ca.Z), new BioVec(ca.X, 0.4, ca.Z), new BioVec(ca.X, -0.4, ca.Z), new BioVec(ca.X, -4.5, ca.Z) }.Select(d.Apply).ToList();
        out_.Arrow = (d.Apply(new BioVec(ch.X, 5.5, Half + 1.2)), d.Apply(new BioVec(ch.X, -5.5, Half + 1.2)));
        out_.Up = d.ApplyVector(new BioVec(0, 1, 0));
        out_.Triangles = items.Sum(i => i.Mesh.TriangleCount);
        out_.Content = new BioSceneContent { Items = items, Labels = Labels(), Frame = new BioSphere(BioVec.Zero, 29) };
        return out_;
    }

    private static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

    /// <summary>What the view moves, without building the patch (the same as <see cref="Build"/>'s): the carrier's helices
    /// in their two states, the water's way down the channel, the carrier's solute path, the osmosis arrow, the membrane's up.</summary>
    public static Model Moving()
    {
        var d = Display;
        var out_ = new Model();
        var ca = Proteins[1];
        var c = new BioVec(ca.X, 0, ca.Z);
        for (var hx = 0; hx < 7; hx++)
        {
            var a = 2 * Math.PI * hx / 7;
            var at = c + new BioVec(1.2 * Math.Cos(a), 0, 1.2 * Math.Sin(a));
            var tangent = new BioVec(-Math.Sin(a), 0, Math.Cos(a));
            out_.Carrier.Add((d.Apply(at), Turn(tangent, -12), Turn(tangent, 12)));
        }
        var ch = Proteins[0];
        out_.ChannelPath = (d.Apply(new BioVec(ch.X, 4.2, ch.Z)), d.Apply(new BioVec(ch.X, -4.2, ch.Z)));
        out_.CarrierPath = new[] { new BioVec(ca.X, 4.5, ca.Z), new BioVec(ca.X, 0.4, ca.Z), new BioVec(ca.X, -0.4, ca.Z), new BioVec(ca.X, -4.5, ca.Z) }.Select(d.Apply).ToList();
        out_.Arrow = (d.Apply(new BioVec(ch.X, 5.5, Half + 1.2)), d.Apply(new BioVec(ch.X, -5.5, Half + 1.2)));
        out_.Up = d.ApplyVector(new BioVec(0, 1, 0));
        return out_;
    }

    /// <summary>The patch's turn after a helix's tilt (for the carrier's animation).</summary>
    public static CEQuat Turn(BioVec axis, double degrees) => CEQuat.Of(Display).Times(CEQuat.FromAxis(axis, degrees));

    /// <summary>Two tails hanging from a head toward the bilayer's middle, wavy, one kinked 30° at its middle if asked (each a
    /// four-sided tube through six points: the Mac's has five sides and eight points; Viewport3D draws on one thread, so
    /// Windows keeps the patch under 300k triangles).</summary>
    public static BioMesh LipidTails(BioVec h, double sign, double azimuth, bool kinked)
    {
        var m = new BioMesh();
        var side = new BioVec(Math.Cos(azimuth), 0, Math.Sin(azimuth));
        var across = new BioVec(-Math.Sin(azimuth), 0, Math.Cos(azimuth));
        var signs = new[] { -1.0, 1.0 };
        for (var k = 0; k < 2; k++)
        {
            var s = signs[k];
            var top = h + side * (0.22 * s) - new BioVec(0, sign * 0.4, 0);
            var dir = new BioVec(0, -sign, 0);
            var pts = new List<BioVec>();
            var p = top;
            const int n = 6;
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / (n - 1);
                if (kinked && k == 1 && i == n / 2) dir = BioV.Rotate(dir, across, 30 * s);
                pts.Add(p + across * (0.12 * Math.Sin(2 * Math.PI * 1.5 * t)));
                p += dir * (1.75 / (n - 1));
            }
            m.Append(BioPath.Tube(pts, 0.22, sides: 4, samples: 1));
        }
        return m;
    }

    /// <summary>A sugar ring: a flat hexagonal bead (r 0.35 nm).</summary>
    public static BioMesh Hexagon(BioVec c, BioVec axis) => BioShapes.Cylinder(c - BioV.Unit(axis) * 0.12, c + BioV.Unit(axis) * 0.12, 0.35, 6);

    /// <summary>A glycoprotein's sugar chain: 8 hexagon beads, a branch of 3 off the second.</summary>
    public static BioMesh GlycoChain(BioVec from, ref BioRandom rng)
    {
        var m = new BioMesh();
        var p = from;
        var branchFrom = from;
        for (var i = 0; i < 5; i++)
        {
            m.Append(Hexagon(p, CEGeo.Cone(new BioVec(1, 0, 0), 40, ref rng)));
            if (i == 1) branchFrom = p;
            var dir = CEGeo.Cone(new BioVec(0, 1, 0), 25, ref rng);
            p += dir * 0.7;
        }
        p = branchFrom + BioV.Unit(new BioVec(0.8, 0.6, 0.2)) * 0.7;
        for (var i = 0; i < 3; i++)
        {
            m.Append(Hexagon(p, CEGeo.Cone(new BioVec(0, 0, 1), 40, ref rng)));
            p += BioV.Unit(new BioVec(0.7, 0.7, 0.1)) * 0.7;
        }
        return m;
    }

    /// <summary>The twelve labels (§4.1.1), at points the camera sees: the cut faces for the bilayer, a head and the tails;
    /// the proteins' tops; the sides in front of the near cut face.</summary>
    public static List<BioLabel3D> Labels()
    {
        var d = Display;
        BioLabel3D L(string id, string text, BioVec p, double area, BioVec? normal = null) =>
            new(id, text, d.Apply(p), normal is { } n ? d.ApplyVector(n) : null, area);
        var ch = Proteins[0];
        var ca = Proteins[1];
        var g = Proteins[2];
        var plain = Proteins[5];
        return new List<BioLabel3D>
        {
            L("part:bilayer", "Phospholipid bilayer", new BioVec(-4, 0.2, Half - 0.6), 12, new BioVec(0, 0, 1)),
            L("part:bilayer:heads", "Hydrophilic head (polar)", new BioVec(-16, HeadY, Half - 0.5), 11, new BioVec(0, 0.4, 1)),
            L("part:bilayer:tails", "Hydrophobic tails (nonpolar)", new BioVec(Half - 0.5, -0.9, -6), 10, new BioVec(1, 0, 0)),
            L("part:cholesterol", "Cholesterol", new BioVec(Half - 0.4, 1.0, 12), 7, new BioVec(1, 0, 0)),
            L("part:integral", "Integral (transmembrane) protein", new BioVec(plain.X, 3.9, plain.Z), 9),
            L("part:integral:channel", "Channel protein", new BioVec(ch.X, 3.2, ch.Z + 1.25), 9),
            L("part:integral:carrier", "Carrier protein", new BioVec(ca.X, 2.4, ca.Z), 9),
            L("part:peripheral", "Peripheral protein", new BioVec(PeripheralPlaces[0].X, -HeadY - 1.9, PeripheralPlaces[0].Z + 1.4), 8),
            L("part:glycoprotein", "Glycoprotein", new BioVec(g.X + 0.4, 5.6, g.Z + 0.2), 8),
            L("part:glycolipid", "Glycolipid", new BioVec(GlycolipidPlaces[3].X, HeadY + 2.0, GlycolipidPlaces[3].Z), 7),
            L("side:outside", "Extracellular side", new BioVec(-22, 6.5, Half + 1), 6),
            L("side:inside", "Cytoplasm side", new BioVec(-22, -7.5, Half + 1), 6),
        };
    }
}

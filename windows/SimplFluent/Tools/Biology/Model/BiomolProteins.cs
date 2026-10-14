namespace Simpl.Tools;

// The Biomolecule Builder's proteins as cartoons (spec §4.4 Proteins items 4 and 6, §6.10), the Mac's
// BiomolProteins.swift: a chain's CA trace drawn with helices as ribbons (1.6 Å wide, 0.4 Å thick), strands as flat arrows
// (2.0 Å wide, the arrowhead 3.2 Å on the last residue) and the rest as tubes (radius 0.4 Å), coloured as §3.8 has them;
// ubiquitin (1UBQ) with its side-chain centroids for the tertiary level, deoxy-haemoglobin (2HHB) with its four chains
// and four hemes for the quaternary level, and the seeded random coil ubiquitin unfolds toward when it is denatured.
// Values only (meshes and materials); the view batches them.

public static class BiomolCartoon
{
    /// <summary>A run of residues drawn one way: H (helix ribbon), E (strand arrow) or C (coil tube), first and last inclusive.</summary>
    public sealed record Segment(char Kind, int First, int Last)
    {
        public int Count => Last - First + 1;
    }

    /// <summary>The cartoon's sizes (Å), §4.4.</summary>
    public const double HelixWidth = 1.6, HelixThickness = 0.4, StrandWidth = 2.0, StrandThickness = 0.4, ArrowWidth = 3.2, CoilRadius = 0.4;

    /// <summary>The runs of a structure string (H, E, C; anything else is coil). A helix shorter than 3 residues or a strand
    /// shorter than 2 is drawn as coil.</summary>
    public static List<Segment> Segments(IReadOnlyList<char> ss)
    {
        static List<Segment> Runs(IReadOnlyList<char> s)
        {
            var out_ = new List<Segment>();
            var i = 0;
            while (i < s.Count)
            {
                var j = i;
                while (j + 1 < s.Count && s[j + 1] == s[i]) j++;
                out_.Add(new Segment(s[i], i, j));
                i = j + 1;
            }
            return out_;
        }
        var kinds = ss.Select(c => c is 'H' or 'E' ? c : 'C').ToArray();
        foreach (var r in Runs(kinds))
            if ((r.Kind == 'H' && r.Count < 3) || (r.Kind == 'E' && r.Count < 2))
                for (var k = r.First; k <= r.Last; k++) kinds[k] = 'C';
        return Runs(kinds);
    }

    /// <summary>Each segment's mesh, in the order of <paramref name="segments"/>.</summary>
    public static List<BioMesh> Meshes(IReadOnlyList<BioVec> ca, IReadOnlyList<Segment> segments)
    {
        var n = ca.Count;
        if (n < 2) return segments.Select(_ => new BioMesh()).ToList();
        var helixAxes = Axes(ca);
        var strandSides = StrandGuides(ca);
        return segments.Select(s =>
        {
            var pts = Enumerable.Range(s.First, s.Count).Select(i => ca[i]).ToList();
            switch (s.Kind)
            {
                case 'H':
                {
                    // (one way along the helix, then a three-residue moving average)
                    var along = ca[s.Last] - ca[s.First];
                    var guides = Enumerable.Range(s.First, s.Count).Select(i => helixAxes[i]).Select(g => BioV.Dot(g, along) < 0 ? -g : g).ToList();
                    return BioPath.Ribbon(pts, HelixWidth, HelixThickness, Smooth(guides), samples: 6, elliptical: true);
                }
                case 'E':
                {
                    var guides = Smooth(Enumerable.Range(s.First, s.Count).Select(i => strandSides[i]).ToList());
                    return BioPath.Ribbon(pts, StrandWidth, StrandThickness, guides, samples: 6, arrow: (Math.Max(pts.Count - 2, 0), ArrowWidth));
                }
                default:
                {
                    int lo = Math.Max(s.First - 1, 0), hi = Math.Min(s.Last + 1, n - 1);
                    var tube = Enumerable.Range(lo, hi - lo + 1).Select(i => ca[i]).ToList();
                    return tube.Count >= 2 ? BioPath.Tube(tube, CoilRadius, sides: 10, samples: 6) : new BioMesh();
                }
            }
        }).ToList();
    }

    /// <summary>Each residue's local helix axis (the cross product of its CA bisector and the next one's), the ends copied in.</summary>
    public static List<BioVec> Axes(IReadOnlyList<BioVec> ca)
    {
        var n = ca.Count;
        if (n < 4) return ca.Select(_ => new BioVec(0, 1, 0)).ToList();
        var out_ = Enumerable.Repeat(new BioVec(0, 1, 0), n).ToList();
        BioVec Bisector(int i) => (ca[i - 1] - ca[i]) + (ca[i + 1] - ca[i]);
        for (var i = 1; i < n - 2; i++) out_[i] = BioV.Unit(BioV.Cross(Bisector(i), Bisector(i + 1)));
        out_[0] = out_[1];
        out_[n - 2] = out_[n - 3];
        out_[n - 1] = out_[n - 3];
        return out_;
    }

    /// <summary>Each residue's direction across a strand, in the sheet's plane: the strand's tangent crossed with its pleat
    /// (the CA's offset from its neighbours' midpoint, which alternates side, its sign turned to agree).</summary>
    public static List<BioVec> StrandGuides(IReadOnlyList<BioVec> ca)
    {
        var n = ca.Count;
        if (n < 3) return ca.Select(_ => new BioVec(1, 0, 0)).ToList();
        var pleat = Enumerable.Repeat(BioVec.Zero, n).ToList();
        for (var i = 1; i < n - 1; i++) pleat[i] = (ca[i] - (ca[i - 1] + ca[i + 1]) / 2) * (i % 2 == 0 ? 1 : -1);
        pleat[0] = pleat[1];
        pleat[n - 1] = pleat[n - 2];
        pleat = Smooth(pleat);
        return Enumerable.Range(0, n).Select(i =>
        {
            var t = BioV.Unit(ca[Math.Min(i + 1, n - 1)] - ca[Math.Max(i - 1, 0)]);
            return BioV.Unit(BioV.Cross(t, pleat[i]));
        }).ToList();
    }

    /// <summary>A three-point moving average (the ends average two).</summary>
    public static List<BioVec> Smooth(IReadOnlyList<BioVec> v)
    {
        if (v.Count < 3) return v.ToList();
        return Enumerable.Range(0, v.Count).Select(i =>
        {
            int lo = Math.Max(i - 1, 0), hi = Math.Min(i + 1, v.Count - 1);
            return BiomolMath.Centroid(Enumerable.Range(lo, hi - lo + 1).Select(k => v[k]).ToList());
        }).ToList();
    }

    /// <summary>A segment's colour: helix pink, strand amber, coil grey (§3.8).</summary>
    public static string Hex(char kind, bool dark) => BioPalette.Secondary(kind).HexFor(dark);

    /// <summary>What a label calls a segment.</summary>
    public static string Name(char kind) => kind == 'H' ? "α-helix" : (kind == 'E' ? "β-strand" : "Coil (loop)");
}

// ---- ubiquitin and haemoglobin (§6.10) ----

/// <summary>Which side chains the tertiary level shows plainly (the rest faded).</summary>
public enum BiomolCentroids { All, Core, Surface }

public static class BiomolProteins
{
    /// <summary>Haemoglobin's chains: α₁ (A), β₁ (B), α₂ (C), β₂ (D), each in its colour (§4.4).</summary>
    public static readonly IReadOnlyDictionary<char, string> ChainHex = new Dictionary<char, string> { ['A'] = "#e5484d", ['B'] = "#3e63dd", ['C'] = "#f5a3a6", ['D'] = "#9fb4f5" };
    public static readonly IReadOnlyDictionary<char, string> ChainName = new Dictionary<char, string> { ['A'] = "α₁", ['B'] = "β₁", ['C'] = "α₂", ['D'] = "β₂" };
    /// <summary>A heme's disk (radius 5 Å, 0.8 Å thick) and its iron (radius 0.7 Å).</summary>
    public const string HemeHex = "#8b1a1a", IronHex = "#e07800";
    public const double HemeRadius = 5.0, HemeThickness = 0.8, IronRadius = 0.7;
    /// <summary>The side-chain centroids' spheres (Å).</summary>
    public const double CentroidRadius = 1.6;

    public static string Label(this BiomolCentroids s) => s == BiomolCentroids.All ? "All" : (s == BiomolCentroids.Core ? "Hydrophobic core" : "Surface");

    /// <summary>Ubiquitin's CA trace.</summary>
    public static List<BioVec> UbiquitinCA => BioData.Ubiquitin.Select(r => r.Ca).ToList();

    /// <summary>One chain of haemoglobin, N→C.</summary>
    public static List<BioData.TraceResidue> Chain(char c) => BioData.Hemoglobin.Residues.Where(r => r.Chain == c).ToList();

    /// <summary>A residue's class (its colour) by its one-letter code.</summary>
    public static BioAAClass AaClass(char aa) => BioAminoAcids.ByOne.GetValueOrDefault(aa)?.Group ?? BioAAClass.Nonpolar;

    /// <summary>The tertiary level: ubiquitin's cartoon ("ss:&lt;k&gt;"), its side-chain centroids ("res:&lt;i&gt;", 1-based,
    /// coloured by class; faded but for the nonpolar ones in the core view, faded but for the polar and charged ones in the
    /// surface view), and its labels.</summary>
    public static BioSceneContent Tertiary(bool dark, BiomolCentroids show, bool labels = true)
    {
        var u = BioData.Ubiquitin;
        var ca = u.Select(r => r.Ca).ToList();
        var segs = BiomolCartoon.Segments(u.Select(r => r.Ss).ToList());
        var meshes = BiomolCartoon.Meshes(ca, segs);
        var items = new List<BioSceneItem>();
        var ribbonAlpha = show == BiomolCentroids.All ? 1.0 : 0.3;
        for (var k = 0; k < segs.Count; k++)
            items.Add(new BioSceneItem($"ss:{k}", meshes[k], new BioMaterial(BiomolCartoon.Hex(segs[k].Kind, dark), Alpha: ribbonAlpha, Roughness: 0.45), 1));
        var ball = BioShapes.Icosphere(CentroidRadius, 2);
        foreach (var r in u)
        {
            if (r.Centroid is not { } c) continue;
            var cls = AaClass(r.Aa);
            var plain = show switch { BiomolCentroids.Core => cls == BioAAClass.Nonpolar, BiomolCentroids.Surface => cls != BioAAClass.Nonpolar, _ => true };
            items.Add(new BioSceneItem($"res:{r.I}", ball.Translated(c), new BioMaterial(BioPalette.AminoAcid(cls).HexFor(dark), Alpha: plain ? 1 : 0.14, Roughness: 0.4), plain ? 0 : 2));
        }
        var ls = new List<BioLabel3D>();
        if (labels && u.Count > 0)
        {
            var first = u[0];
            var last = u[^1];
            ls.Add(new BioLabel3D($"res:{first.I}", "N-terminus (Met1)", first.Centroid ?? first.Ca));
            ls.Add(new BioLabel3D($"res:{last.I}", "C-terminus (Gly76)", last.Centroid ?? last.Ca));
            var helices = segs.Select((s, k) => (s, k)).Where(x => x.s.Kind == 'H').ToList();
            if (helices.Count > 0)
            {
                var h = helices.Aggregate((a, b) => b.s.Count > a.s.Count ? b : a);
                ls.Add(new BioLabel3D($"ss:{h.k}", "α-helix", ca[(h.s.First + h.s.Last) / 2], Area: 4));
            }
            var strands = segs.Select((s, k) => (s, k)).Where(x => x.s.Kind == 'E').ToList();
            if (strands.Count > 0)
            {
                var e = strands.Aggregate((a, b) => b.s.Count > a.s.Count ? b : a);
                ls.Add(new BioLabel3D($"ss:{e.k}", "β-sheet (5 strands)", ca[(e.s.First + e.s.Last) / 2], Area: 4));
            }
            if (show == BiomolCentroids.Core && u.FirstOrDefault(r => r.I == 43) is { Centroid: { } lc }) ls.Add(new BioLabel3D("res:43", "Leu43 (core)", lc));
            if (show == BiomolCentroids.Surface && u.FirstOrDefault(r => r.I == 48) is { Centroid: { } kc }) ls.Add(new BioLabel3D("res:48", "Lys48 (surface)", kc));
        }
        return new BioSceneContent { Items = items, Labels = ls };
    }

    /// <summary>The quaternary level: haemoglobin's four chains as cartoons in their colours ("chain:A:ss:&lt;k&gt;"), each heme
    /// a disk through its four pyrrole N with its iron ("heme:A:disk", "heme:A:iron"), and β6 Glu marked on both β chains
    /// ("glu6:B") — the residue the sickle-cell mutation changes.</summary>
    public static BioSceneContent Quaternary(bool dark, bool labels = true)
    {
        var items = new List<BioSceneItem>();
        var ls = new List<BioLabel3D>();
        var hemeCentres = BioData.Hemoglobin.Hemes.Select(h => Plane(h.N).Centre).ToList();
        foreach (var c in new[] { 'A', 'B', 'C', 'D' })
        {
            var rs = Chain(c);
            var ca = rs.Select(r => r.Ca).ToList();
            var segs = BiomolCartoon.Segments(rs.Select(r => r.Ss).ToList());
            var meshes = BiomolCartoon.Meshes(ca, segs);
            for (var k = 0; k < segs.Count; k++)
                items.Add(new BioSceneItem($"chain:{c}:ss:{k}", meshes[k], new BioMaterial(ChainHex.GetValueOrDefault(c) ?? "#8e8e93", Roughness: 0.45)));
            if (labels && rs.Count > 0)
            {
                // (a third of the way along the chain, or the nearest residue to it 12 Å clear of every heme: β₂'s third
                //  sat on its heme, and its label's leader and the heme's ended on one point)
                var third = Math.Min(rs.Count / 3, rs.Count - 1);
                var mid = Enumerable.Range(0, rs.Count).OrderBy(i => Math.Abs(i - third)).Select(i => rs[i])
                    .FirstOrDefault(r => hemeCentres.All(h => BioV.Distance(r.Ca, h) >= 12)) ?? rs[third];
                ls.Add(new BioLabel3D($"chain:{c}", $"{ChainName.GetValueOrDefault(c)} chain ({(c is 'A' or 'C' ? "α" : "β")}, {rs.Count} aa)", mid.Ca, Area: 10));
            }
            if ((c == 'B' || c == 'D') && rs.FirstOrDefault(r => r.I == 6) is { } glu)
            {
                items.Add(new BioSceneItem($"glu6:{c}", BioShapes.Icosphere(1.4, 2).Translated(glu.Ca), new BioMaterial(BioPalette.Acidic.HexFor(dark), Roughness: 0.4)));
                if (labels && c == 'B') ls.Add(new BioLabel3D("glu6:B", "β6 Glu (Val in sickle cell)", glu.Ca, Area: 2));
            }
        }
        var hemes = BioData.Hemoglobin.Hemes;
        for (var k = 0; k < hemes.Count; k++)
        {
            var h = hemes[k];
            var (centre, normal) = Plane(h.N);
            var disk = BioShapes.Cylinder(centre - normal * (HemeThickness / 2), centre + normal * (HemeThickness / 2), HemeRadius, 32);
            items.Add(new BioSceneItem($"heme:{h.Chain}:disk", disk, new BioMaterial(HemeHex, Roughness: 0.5)));
            items.Add(new BioSceneItem($"heme:{h.Chain}:iron", BioShapes.Icosphere(IronRadius, 2).Translated(h.Fe), new BioMaterial(IronHex, Roughness: 0.3)));
            if (labels && (k == 0 || k == 3)) ls.Add(new BioLabel3D($"heme:{h.Chain}", "Heme (Fe²⁺)", centre + normal * 0.4, Area: 3));
        }
        return new BioSceneContent { Items = items, Labels = ls };
    }

    /// <summary>Ubiquitin denatured (§4.4 Denature): its chain as the random coil it unfolds into, each stretch a tube in the
    /// colour of the structure it had ("coil:&lt;k&gt;"), and its side chains' spheres by class, the same meshes the
    /// unfolding ends on; framed as the unfolding is.</summary>
    public static BioSceneContent Denatured(bool dark, bool labels = true)
    {
        var (parts, frame) = DenatureMeshes(dark);
        var items = parts.Select((p, k) => new BioSceneItem($"coil:{k}", p.Coil, new BioMaterial(p.Hex, Roughness: 0.45))).ToList();
        var ls = new List<BioLabel3D>();
        var coil = RandomCoil(UbiquitinCA);
        if (labels && coil.Count > 1)
        {
            ls.Add(new BioLabel3D("coil:0", "N-terminus (Met1)", coil[0], Area: 3));
            ls.Add(new BioLabel3D("coil", "Random coil: the same chain, unfolded", coil[coil.Count / 2], Area: 10));
            ls.Add(new BioLabel3D("coil:end", "C-terminus (Gly76)", coil[^1], Area: 3));
        }
        return new BioSceneContent { Items = items, Labels = ls, Frame = frame };
    }

    /// <summary>How far from ubiquitin's centre (the mean of its CA atoms) its side chains sit on average (Å): the nonpolar
    /// ones (glycine, whose "side chain" is its CA, left out), and the polar and charged ones — the hydrophobic core in
    /// numbers.</summary>
    public static (double Nonpolar, double Others, int NonpolarCount, int OthersCount) CoreDistances()
    {
        var u = BioData.Ubiquitin;
        var centre = BiomolMath.Centroid(u.Select(r => r.Ca).ToList());
        var np = u.Where(r => r.Aa != 'G' && AaClass(r.Aa) == BioAAClass.Nonpolar && r.Centroid != null).Select(r => BioV.Distance(r.Centroid!.Value, centre)).ToList();
        var other = u.Where(r => AaClass(r.Aa) != BioAAClass.Nonpolar && r.Centroid != null).Select(r => BioV.Distance(r.Centroid!.Value, centre)).ToList();
        return (np.Count == 0 ? 0 : np.Average(), other.Count == 0 ? 0 : other.Average(), np.Count, other.Count);
    }

    /// <summary>The best-fit plane through some points: their centroid and a unit normal (Newell's method round them in order).</summary>
    public static (BioVec Centre, BioVec Normal) Plane(IReadOnlyList<BioVec> pts)
    {
        if (pts.Count == 0) return (BioVec.Zero, new BioVec(0, 0, 1));
        var c = BiomolMath.Centroid(pts);
        var n = BioVec.Zero;
        for (var i = 0; i < pts.Count; i++) n += BioV.Cross(pts[i] - c, pts[(i + 1) % pts.Count] - c);
        var u = BioV.Unit(n);
        return (c, BioV.Length(u) > 0 ? u : new BioVec(0, 0, 1));
    }

    // ---- denaturing (Nice) ----

    /// <summary>The extended random coil ubiquitin unfolds toward: a seeded walk (SplitMix64, seed 76) of 3.8 Å steps from
    /// the first CA, each step's direction a fresh random unit vector (two draws) added to 0.55 × the last step's, kept when
    /// no earlier CA but the last two is closer than 4.5 Å (40 tries, else the last try), then centred where the native
    /// protein's centre is.</summary>
    public static List<BioVec> RandomCoil(IReadOnlyList<BioVec> native, ulong seed = 76)
    {
        if (native.Count < 2) return native.ToList();
        var g = new BioRandom(seed);
        var pts = new List<BioVec> { native[0] };
        var last = g.UnitVector();
        for (var i = 1; i < native.Count; i++)
        {
            var chosen = pts[^1] + last * 3.8;
            var chosenDir = last;
            for (var t = 0; t < 40; t++)
            {
                var d = BioV.Unit(last * 0.55 + g.UnitVector());
                var p = pts[^1] + d * 3.8;
                chosen = p;
                chosenDir = d;
                var clear = pts.Take(Math.Max(pts.Count - 2, 0)).All(q => BioV.Distance(q, p) >= 4.5);
                if (clear) break;
            }
            pts.Add(chosen);
            last = chosenDir;
        }
        var shift = BiomolMath.Centroid(native) - BiomolMath.Centroid(pts);
        return pts.Select(p => p + shift).ToList();
    }

    /// <summary>Denaturing as matching meshes (the same vertices in the same order, so one morphs into the other): each
    /// native segment as a tube through its CAs in its structure's colour, and the side-chain centroids by class, in the
    /// native fold and in the random coil (each centroid kept at its native offset from its CA).</summary>
    public static (List<(string Hex, BioMesh Native, BioMesh Coil)> Parts, BioSphere Frame) DenatureMeshes(bool dark)
    {
        var u = BioData.Ubiquitin;
        var native = u.Select(r => r.Ca).ToList();
        var coil = RandomCoil(native);
        var segs = BiomolCartoon.Segments(u.Select(r => r.Ss).ToList());
        var parts = new List<(string, BioMesh, BioMesh)>();
        var n = native.Count;
        foreach (var s in segs)
        {
            int lo = Math.Max(s.First - 1, 0), hi = Math.Min(s.Last + 1, n - 1);
            if (hi <= lo) continue;
            var radius = s.Kind == 'C' ? 0.45 : 0.75;
            parts.Add((BiomolCartoon.Hex(s.Kind, dark),
                BioPath.Tube(native.GetRange(lo, hi - lo + 1), radius, sides: 10, samples: 6, caps: false),
                BioPath.Tube(coil.GetRange(lo, hi - lo + 1), radius, sides: 10, samples: 6, caps: false)));
        }
        var ball = BioShapes.Icosphere(1.3, 1);
        foreach (var cls in BioAAClasses.All)
        {
            BioMesh a = new(), b = new();
            for (var k = 0; k < u.Count; k++)
            {
                var r = u[k];
                if (AaClass(r.Aa) != cls) continue;
                var off = (r.Centroid ?? r.Ca) - r.Ca;
                a.Append(ball.Translated(r.Ca + off));
                b.Append(ball.Translated(coil[k] + off));
            }
            if (!a.IsEmpty) parts.Add((BioPalette.AminoAcid(cls).HexFor(dark), a, b));
        }
        var s2 = BioV.BoundingSphere(native.Concat(coil).ToList());
        return (parts, new BioSphere(s2.Centre, s2.Radius + 2));
    }
}

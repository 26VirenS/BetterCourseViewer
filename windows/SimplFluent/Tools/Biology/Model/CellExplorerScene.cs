using System.Globalization;

namespace Simpl.Tools;

// A built cell made into a scene the shared 3D view draws (BioSceneContent), the Mac's CellExplorerScene.swift: the parts
// hidden in the parts list left out, one part alone when isolated (§4.1 item 3) or seen inside (a mitochondrion or
// chloroplast cut lengthwise, the nucleus cut open, with its insides labelled), every organelle moved out by the explode
// slider (§4.1 item 5: explode × 0.6 × cell radius × (dist / cell radius)^0.5 along the line from the cell's centre to its
// own, the nucleus moving least) while the membrane and wall fade to 15% between 0 and 0.3, each part's label at its
// anchor, and the light or dark colours. And the Compare page's three cells side by side at their true relative sizes
// (§4.1 item 8). Plain .NET.

/// <summary>What the view asks of a built cell.</summary>
public sealed record CEView(double Explode = 0, IReadOnlyList<string>? Hidden = null, string? Isolate = null, bool LookInside = false, bool Dark = false)
{
    public bool IsHidden(string part) => Hidden?.Contains(part) == true;

    /// <summary>What the scene depends on, as words (the 3D view builds again when they change).</summary>
    public string Key => string.Join("|", Explode.ToString("0.###", CultureInfo.InvariantCulture), string.Join(",", Hidden ?? Array.Empty<string>()),
        Isolate ?? "-", LookInside ? "inside" : "-", Dark ? "dark" : "light");
}

public static class CEScene
{
    /// <summary>How far an organelle moves when exploded (§4.1 item 5).</summary>
    public static BioVec Displacement(CEInstance inst, double explode, double cellRadius)
    {
        if (explode <= 0 || inst.Shell || inst.Fixed) return BioVec.Zero;
        var dist = BioV.Length(inst.ExplodeFrom);
        if (dist <= 1e-9) return BioVec.Zero;
        return inst.ExplodeFrom / dist * (explode * 0.6 * cellRadius * Math.Sqrt(dist / cellRadius));
    }

    /// <summary>The shells' opacity, as a fraction of their own: 1 at explode 0, 15% from 0.3 on.</summary>
    public static double ShellFade(double explode) => 1 - 0.85 * Math.Min(Math.Max(explode, 0) / 0.3, 1);

    /// <summary>Whether a part can be looked inside.</summary>
    public static bool CanLookInside(string part) => part is "mito" or "chloroplast" or "nucleus";

    /// <summary>A part with more instances than this is drawn as one item a material (see <see cref="Content"/>).</summary>
    public const int MergeAbove = 40;

    /// <summary>A mesh's triangles added to another, moved.</summary>
    private static void AppendMoved(BioMesh into, BioMesh m, BioVec d)
    {
        var start = into.Positions.Count;
        var f = BioMesh.F(d);
        foreach (var p in m.Positions) into.Positions.Add(p + f);
        into.Normals.AddRange(m.Normals);
        foreach (var i in m.Indices) into.Indices.Add(i + start);
    }

    public static BioSceneContent Content(CEModel m, CEView v)
    {
        var explode = v.Isolate == null ? Math.Min(Math.Max(v.Explode, 0), 1) : 0;
        var fade = ShellFade(explode);
        var items = new List<BioSceneItem>();
        var labels = new List<BioLabel3D>();
        BioMaterial Material(CEPiece p, bool shell) => new(p.Colour.HexFor(v.Dark), shell ? p.Alpha * fade : p.Alpha, p.Roughness);
        if (v.Isolate is { } inside && v.LookInside && CanLookInside(inside))
        {
            var (instances, insideLabels) = CEBuild.LookInsideScene(m, inside);
            foreach (var inst in instances)
            {
                if (v.IsHidden(inst.Part) && inst.Part != inside) continue;
                foreach (var p in inst.Pieces) items.Add(new BioSceneItem(inst.Id, p.Mesh, Material(p, false), p.Order));
            }
            return new BioSceneContent { Items = items, Labels = insideLabels };
        }
        var moved = new Dictionary<int, BioVec>();
        // (a part of many instances — ribosomes, pores, actin — drawn as one item a material, "organelle:ribosome", but for the
        // instance its label points to, kept on its own so Zoom to has one to go to: the shared view draws each selected
        // item as a model of its own, and thousands would make it crawl)
        var counts = m.Instances.GroupBy(i => i.Part).ToDictionary(g => g.Key, g => g.Count());
        var merged = new Dictionary<(string Part, BioMaterial Material, int Order), BioMesh>();
        var mergedOrder = new List<(string Part, BioMaterial Material, int Order)>();
        var anchored = m.Anchors.Values.Select(a => a.Instance).ToHashSet();
        for (var i = 0; i < m.Instances.Count; i++)
        {
            var inst = m.Instances[i];
            if (v.IsHidden(inst.Part)) continue;
            if (v.Isolate is { } iso && inst.Part != iso) continue;
            var d = Displacement(inst, explode, m.CellRadius);
            moved[i] = d;
            var many = counts[inst.Part] > MergeAbove && !anchored.Contains(i);
            foreach (var p in inst.Pieces)
            {
                var mat = Material(p, inst.Shell);
                if (!many)
                {
                    items.Add(new BioSceneItem(inst.Id, CEGeo.Moved(p.Mesh, d), mat, p.Order));
                    continue;
                }
                var key = (inst.Part, mat, p.Order);
                if (!merged.TryGetValue(key, out var mesh)) { merged[key] = mesh = new BioMesh(); mergedOrder.Add(key); }
                AppendMoved(mesh, p.Mesh, d);
            }
        }
        foreach (var key in mergedOrder) items.Add(new BioSceneItem("organelle:" + key.Part, merged[key], key.Material, key.Order));
        foreach (var part in CEParts.All(m.Options.Kind))
        {
            if (v.IsHidden(part.Id)) continue;
            if (v.Isolate is { } iso && iso != part.Id) continue;
            if (!m.Anchors.TryGetValue(part.Id, out var a) || !moved.TryGetValue(a.Instance, out var d)) continue;
            labels.Add(new BioLabel3D("organelle:" + part.Id, part.Label, a.Point + d, a.Normal, part.Priority));
        }
        BioSphere? frame = v.Isolate == null ? new BioSphere(BioVec.Zero, m.FrameRadius * (1 + 0.45 * explode)) : null;
        return new BioSceneContent { Items = items, Labels = labels, Frame = frame };
    }

    /// <summary>"3D model of an animal cell, cut open; 17 kinds of parts; selected: mitochondria" (§9 Accessibility).</summary>
    public static string Accessibility(CEModel? m, CEKind kind, bool cutaway, IReadOnlyCollection<string> hidden, string? selected)
    {
        var kinds = m != null ? m.PartsShown.Count(p => !hidden.Contains(p)) : CEParts.All(kind).Count(p => p.DefaultVisible);
        var s = $"3D model of {kind.Phrase()}";
        if (cutaway) s += ", cut open";
        s += $"; {kinds} kinds of parts";
        if (selected != null && CEParts.Part(kind, selected) is { } p) s += $"; selected: {p.NameInSentence}";
        return s;
    }
}

public static partial class CEBuild
{
    /// <summary>Look inside as a scene: the instances, and labels for what is inside (the membranes, cristae and matrix of a
    /// mitochondrion; a chloroplast's envelope, grana, stroma, lamellae, starch and DNA; the nucleus's envelope, pores,
    /// nucleolus, chromatin and nucleoplasm).</summary>
    public static (List<CEInstance> Instances, List<BioLabel3D> Labels) LookInsideScene(CEModel m, string part)
    {
        var instances = LookInside(m, part);
        var labels = new List<BioLabel3D>();
        var d = m.Display;
        switch (part)
        {
            case "mito" or "chloroplast":
            {
                if (!m.Anchors.TryGetValue(part, out var a) || m.Instances[a.Instance].Frame is not { } f) break;
                var half = CEGeo.Frame(f.Y, m.View, f.Origin);
                void Label(string id, string text, BioVec local, double priority) => labels.Add(new BioLabel3D(id, text, d.Apply(half.Apply(local)), Area: priority));
                if (part == "mito")
                {
                    Label("organelle:mito", "Outer membrane", new BioVec(-0.443, -0.35, -0.03), 9);
                    Label("inside:mito:inner", "Inner membrane", new BioVec(0.39, -0.75, -0.02), 8);
                    Label("inside:mito:cristae", "Cristae", new BioVec(0.12, 0.026, -0.15), 10);
                    Label("inside:mito:matrix", "Matrix", new BioVec(0, 0.36, -0.37), 7);
                }
                else
                {
                    Label("organelle:chloroplast", "Envelope (two membranes)", new BioVec(-2.47, 0, -0.02), 9);
                    Label("inside:chloroplast:granum", "Granum (thylakoid stack)", new BioVec(0, 0.243, 0), 10);
                    Label("inside:chloroplast:stroma", "Stroma", new BioVec(0.9, -0.62, -0.9), 8);
                    if (OuterLamella() is { } lam) Label("inside:chloroplast:lamella", "Stroma lamella", lam, 6);
                    Label("inside:chloroplast:starch", "Starch grain", new BioVec(1.5, 0.15, -0.85), 5);
                    Label("inside:chloroplast:dna", "DNA", new BioVec(-0.02, 0, -0.6), 4);
                }
                break;
            }
            case "nucleus":
            {
                var nb = new CEBuilder(m.Options with { Cutaway = true, GramPositive = false });
                _ = nb.Rng.UnitVector();
                var n = m.Options.Kind == CEKind.Animal ? new BioVec(1.0, 0.5, -0.5) : new BioVec(-9, 9.2, 0);
                var r = m.Options.Kind == CEKind.Animal ? 3.2 : 3.0;
                nb.Nucleus(n, r);
                nb.Anchors();
                foreach (var (id, text, pr) in new[] { ("nucleus", "Nuclear envelope", 10.0), ("pore", "Nuclear pore", 8.0), ("nucleolus", "Nucleolus", 9.0), ("chromatin", "Chromatin", 7.0) })
                {
                    if (!nb.Model.Anchors.TryGetValue(id, out var a)) continue;
                    labels.Add(new BioLabel3D("organelle:" + id, text, d.Apply(a.Point), a.Normal is { } nrm ? d.ApplyVector(nrm) : null, pr));
                }
                labels.Add(new BioLabel3D("inside:nucleus:nucleoplasm", "Nucleoplasm", d.Apply(n + new BioVec(0.35 * r, 0.55 * r, 0)), d.ApplyVector(new BioVec(0, 0, 1)), 6));
                break;
            }
        }
        return (instances, labels);
    }

    /// <summary>The middle of a stroma lamella toward the chloroplast's −x side, away from the middle granum that "Granum"
    /// names (the first one, next to it, put both leaders' ends on one point), in a chloroplast's frame (as
    /// CERecipes.Chloroplast lays them).</summary>
    public static BioVec? OuterLamella()
    {
        var kept = CERecipes.GranaCentres.Where(c => c.Z <= 0.3).ToList();
        BioVec? best = null;
        for (var i = 0; i < kept.Count; i++)
            for (var j = i + 1; j < kept.Count; j++)
                if (Math.Abs(BioV.Distance(kept[i], kept[j]) - 1.0) < 0.05)
                {
                    var mid = (kept[i] + kept[j]) / 2;
                    if (BioV.Length(mid) < 1.2 && (best is not { } b || mid.X < b.X - 1e-9)) best = mid;
                }
        return best is { } m ? m + new BioVec(0, 0.1, 0) : null;
    }

    // ---- the Compare page's three cells (§4.1 item 8) ----

    /// <summary>The three cells side by side at their true relative sizes — a bacterium 2 µm long, an animal cell 20 µm, a
    /// plant cell 40 µm — each simplified (its outline, its nucleus or nucleoid, its larger organelles), picked as
    /// "cell:&lt;kind&gt;", labelled with its size.</summary>
    public static BioSceneContent CompareScene(bool dark)
    {
        var items = new List<BioSceneItem>();
        var labels = new List<BioLabel3D>();
        var turn = BioFrame.Rotation(new BioVec(0, 1, 0), -18).Then(BioFrame.Rotation(new BioVec(1, 0, 0), 14));
        void Put(CEKind kind, BioMesh m, BioHexPair hex, double scale, double x, double alpha = 1, double roughness = 0.45, int order = 0)
        {
            var f = new BioFrame(new BioVec(x, 0, 0), new BioVec(scale, 0, 0), new BioVec(0, scale, 0), new BioVec(0, 0, scale)).Then(turn);
            items.Add(new BioSceneItem("cell:" + kind.Id(), m.Transformed(f), new BioMaterial(hex.HexFor(dark), alpha, roughness), order));
        }
        static BioHexPair Colour(CEKind k, string id) => CEParts.Part(k, id)?.Colour ?? new BioHexPair("#8e8e93", "#aeaeb2");
        // (the bacterium, 2 µm, at x −34)
        const double bx = -34;
        Put(CEKind.Bacterium, BioShapes.Radial(CEGeo.CapsuleXRadius(0.5, 1.0), 12, 24), Colour(CEKind.Bacterium, "outer"), 1, bx);
        Put(CEKind.Bacterium, BioShapes.Radial(CEGeo.CapsuleXRadius(0.65, 1.0), 12, 24), Colour(CEKind.Bacterium, "capsule"), 1, bx, alpha: 0.3, order: 9);
        for (var k = 0; k < 3; k++)
        {
            var a = k * 2 * Math.PI / 3;
            var b = new BioVec(-0.9, 0.35 * Math.Cos(a), 0.35 * Math.Sin(a));
            var pts = Enumerable.Range(0, 40).Select(i =>
            {
                var t = i / 39.0 * 2 * Math.PI * 2.6;
                return b + new BioVec(-t * 2.3 / (2 * Math.PI), 0.2 * (Math.Cos(t) - 1) * Math.Cos(a), -0.2 * Math.Sin(t));
            }).ToList();
            Put(CEKind.Bacterium, BioPath.Tube(pts, 0.04, sides: 6, samples: 2), Colour(CEKind.Bacterium, "flagellum"), 1, bx);
        }
        // (the animal cell, 20 µm across, at x −19)
        const double ax = -19, aScale = 20.0 / 22;
        var blob = new BioBlob(CEKind.Animal.Seed(), 0.06);
        var ell = BioShapes.EllipsoidRadius(11, 8, 9.5);
        Put(CEKind.Animal, BioShapes.Radial(u => ell(u) * blob.Factor(u), 24, 48), Colour(CEKind.Animal, "pm"), aScale, ax, alpha: 0.35, roughness: 0.25, order: 8);
        Put(CEKind.Animal, BioShapes.Sphere(3.2, 16, 32).Translated(new BioVec(1.0, 0.5, -0.5)), Colour(CEKind.Animal, "nucleus"), aScale, ax);
        var animal = CEBuildCache.Model(new CEBuildOptions(CEKind.Animal, Cutaway: false));
        var mitos = new BioMesh();
        var mito = BioShapes.Capsule(0.45, 1.3, 6, 12);
        foreach (var inst in animal.Instances.Where(i => i.Part == "mito")) if (inst.Frame is { } f) mitos.Append(mito.Transformed(f));
        Put(CEKind.Animal, mitos, Colour(CEKind.Animal, "mito"), aScale, ax);
        // (the plant cell, 40 µm long, at x 15)
        const double px = 15, pScale = 40.0 / 44;
        Put(CEKind.Plant, BioShapes.Radial(BioShapes.RoundedBoxRadius(new BioVec(22, 13, 13), 3), 24, 48), Colour(CEKind.Plant, "wall"), pScale, px, alpha: 0.4, roughness: 0.6, order: 8);
        Put(CEKind.Plant, BioShapes.Radial(BioShapes.RoundedBoxRadius(new BioVec(18.5, 9.5, 9.5), 6), 20, 40), Colour(CEKind.Plant, "vacuole"), pScale, px, alpha: 0.35, roughness: 0.2, order: 4);
        Put(CEKind.Plant, BioShapes.Sphere(3.0, 16, 32).Translated(new BioVec(-9, 9.2, 0)), Colour(CEKind.Plant, "nucleus"), pScale, px);
        var plant = CEBuildCache.Model(new CEBuildOptions(CEKind.Plant, Cutaway: false));
        var chloro = new BioMesh();
        var lens = BioShapes.Lens(5.0, 2.4, 6, 12);
        foreach (var inst in plant.Instances.Where(i => i.Part == "chloroplast")) if (inst.Frame is { } f) chloro.Append(lens.Transformed(f));
        Put(CEKind.Plant, chloro, Colour(CEKind.Plant, "chloroplast"), pScale, px);
        // (labels over each)
        foreach (var (k, x, top, text) in new[] { (CEKind.Bacterium, bx, 0.7, "Bacterium · about 2 µm"), (CEKind.Animal, ax, 8.2 * aScale, "Animal cell · about 20 µm"),
                     (CEKind.Plant, px, 13.4 * pScale, "Plant cell · about 40 µm") })
            labels.Add(new BioLabel3D("cell:" + k.Id(), text, turn.Apply(new BioVec(x, top, 0)), Area: 1));
        return new BioSceneContent { Items = items, Labels = labels, Frame = new BioSphere(turn.Apply(BioVec.Zero), 30) };
    }
}

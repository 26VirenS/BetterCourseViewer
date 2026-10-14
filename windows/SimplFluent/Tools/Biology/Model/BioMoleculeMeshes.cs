namespace Simpl.Tools;

// Atoms and bonds as scene items (spec §3.1 Reuse, §3.4), the Mac's BioMoleculeMeshes.swift: every atomic biomolecule —
// amino acids, peptides, nucleotides, B-DNA, sugars, lipids — drawn the way the chemistry builder draws a molecule (its
// radii per style, its bonds by order, CPK colours), but batched: one item per atom ("atom:<id>", so a click names it)
// and bonds that are not picked, all merged by material in the view. Spheres are UV spheres up to 300 atoms and
// icospheres above (§4.4 Rendering limits). The element colours and radii come from the chemistry tables, handed in by
// the caller (BioScene.cs's BioMoleculeScene), so this stays plain arithmetic.

/// <summary>How atoms are drawn (the chemistry builder's three styles).</summary>
public enum BioAtomStyle { BallStick, SpaceFill, Sticks }

public static class BioAtomStyles
{
    public static string Label(this BioAtomStyle s) => s switch { BioAtomStyle.BallStick => "Ball & stick", BioAtomStyle.SpaceFill => "Space filling", _ => "Sticks" };
}

public static class BioMoleculeMeshes
{
    /// <summary>A unit sphere for atoms: UV (14 × 28) for small models, an icosphere (2: 320 triangles) for big ones.</summary>
    public static readonly BioMesh SmallSphere = BioShapes.Sphere(1, 14, 28);
    public static readonly BioMesh BigSphere = BioShapes.Icosphere(1, 2);

    /// <summary>The drawn radius of an atom (Å), as MoleculeSceneView.Radius has it: ball and stick 0.24 + 0.36 × covalent
    /// radius, space filling 0.9 × van der Waals radius, sticks 0.13.</summary>
    public static double Radius(BioAtomStyle style, double covalent, double vdw) => style switch
    {
        BioAtomStyle.BallStick => 0.24 + 0.36 * covalent,
        BioAtomStyle.SpaceFill => vdw * 0.9,
        _ => 0.13,
    };

    /// <summary>The scene items for a model: its atoms (each "atom:&lt;id&gt;", coloured by <paramref name="color"/>, its radius
    /// by <paramref name="radius"/>) and its bonds (ball and stick: grey cylinders, double and triple side by side; sticks:
    /// each half in its atom's colour; space filling: none). <paramref name="skip"/> leaves atoms out (the H atoms, when
    /// they are hidden).</summary>
    public static List<BioSceneItem> Items(BioMoleculeModel model, BioAtomStyle style, Func<BioMoleculeModel.Atom, double> radius,
        Func<BioMoleculeModel.Atom, string> color, string bondHex = "#9e9e9e", double roughness = 0.35, Func<BioMoleculeModel.Atom, bool>? skip = null)
    {
        var unit = model.Atoms.Count > 300 ? BigSphere : SmallSphere;
        var out_ = new List<BioSceneItem>();
        var drawn = new HashSet<int>();
        foreach (var a in model.Atoms)
        {
            if (skip?.Invoke(a) == true) continue;
            drawn.Add(a.Id);
            var r = radius(a);
            var mesh = unit.Transformed(new BioFrame(a.Pos, new BioVec(r, 0, 0), new BioVec(0, r, 0), new BioVec(0, 0, r)));
            out_.Add(new BioSceneItem($"atom:{a.Id}", mesh, new BioMaterial(color(a), Roughness: roughness)));
        }
        if (style == BioAtomStyle.SpaceFill) return out_;
        var segments = model.Atoms.Count > 300 ? 10 : 16;
        foreach (var b in model.Bonds)
        {
            if (!drawn.Contains(b.A) || !drawn.Contains(b.B)) continue;
            BioMoleculeModel.Atom x = model.Atoms[b.A], y = model.Atoms[b.B];
            var d = y.Pos - x.Pos;
            var len = BioV.Length(d);
            if (len <= 1e-6) continue;
            var dir = d / len;
            var reference = BioV.Cross(dir, new BioVec(0, 0, 1));
            if (BioV.Length(reference) < 0.1) reference = BioV.Cross(dir, new BioVec(0, 1, 0));
            var perp = BioV.Unit(reference);
            if (style == BioAtomStyle.Sticks)
            {
                var mid = (x.Pos + y.Pos) / 2;
                out_.Add(new BioSceneItem(null, BioShapes.Cylinder(x.Pos, mid, 0.13, segments, caps: false), new BioMaterial(color(x), Roughness: roughness)));
                out_.Add(new BioSceneItem(null, BioShapes.Cylinder(mid, y.Pos, 0.13, segments, caps: false), new BioMaterial(color(y), Roughness: roughness)));
                continue;
            }
            var thick = b.Order == 1 ? 0.09 : 0.06;
            var offsets = b.Order == 1 ? new[] { 0.0 } : (b.Order == 2 ? new[] { -0.11, 0.11 } : new[] { -0.16, 0, 0.16 });
            foreach (var o in offsets)
                out_.Add(new BioSceneItem(null, BioShapes.Cylinder(x.Pos + perp * o, y.Pos + perp * o, thick, segments, caps: false), new BioMaterial(bondHex, Roughness: roughness)));
        }
        return out_;
    }

    /// <summary>A dashed line as short cylinders (an H-bond: radius 0.08 Å in atom views; §4.2), <paramref name="dash"/> long
    /// with <paramref name="gap"/> between.</summary>
    public static BioMesh Dashes(BioVec a, BioVec b, double radius = 0.08, double dash = 0.3, double gap = 0.2)
    {
        var len = BioV.Distance(a, b);
        if (len <= 1e-6) return new BioMesh();
        var dir = (b - a) / len;
        var m = new BioMesh();
        var s = 0.0;
        while (s < len)
        {
            var e = Math.Min(s + dash, len);
            m.Append(BioShapes.Cylinder(a + dir * s, a + dir * e, radius, 8));
            s = e + gap;
        }
        return m;
    }
}

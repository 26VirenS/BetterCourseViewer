using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Simpl.Kit;

namespace Simpl.Tools;

// The biology scenes' WPF side (spec §3.4), the Mac's BioSceneKit.swift: a BioMesh made a frozen MeshGeometry3D; a
// BioMaterial made a frozen WPF material as the molecule builder makes its own (a diffuse colour and a soft specular
// highlight; translucent ones with their back faces too, drawn after the opaque — Viewport3D sorts nothing); a scene's
// items batched into one mesh per material, keeping which vertices and triangles are whose for picking (§3.4: fewer
// than 300k triangles a scene, as Viewport3D draws on one thread); molecules drawn with the chemistry builder's CPK
// colours and radii (MBChem); and text that faces the viewer and follows its anchor (an atom's symbol).

/// <summary>Meshes and materials as WPF draws them.</summary>
public static class BioWpf
{
    public static Point3D P(BioVec v) => new(v.X, v.Y, v.Z);
    public static Vector3D V(BioVec v) => new(v.X, v.Y, v.Z);
    public static BioVec B(Point3D p) => new(p.X, p.Y, p.Z);
    public static BioVec B(Vector3D v) => new(v.X, v.Y, v.Z);

    /// <summary>A mesh as WPF geometry, frozen (so it is drawn without being watched, and can be shared).</summary>
    public static MeshGeometry3D Mesh(BioMesh m)
    {
        var positions = new Point3DCollection(m.Positions.Count);
        foreach (var p in m.Positions) positions.Add(new Point3D(p.X, p.Y, p.Z));
        var normals = new Vector3DCollection(m.Normals.Count);
        foreach (var n in m.Normals) normals.Add(new Vector3D(n.X, n.Y, n.Z));
        var indices = new Int32Collection(m.Indices.Count);
        foreach (var i in m.Indices) indices.Add(i);
        var g = new MeshGeometry3D { Positions = positions, Normals = normals, TriangleIndices = indices };
        g.Freeze();
        return g;
    }

    private static readonly Dictionary<(BioMaterial, double), Material> Materials = new();

    /// <summary>
    /// A material as the chemistry scenes make theirs (MoleculeSceneView.Mat): its colour, a soft white highlight that is
    /// stronger and tighter the smoother it is (roughness 0.35, the molecules', gives the builder's own), its glow, and
    /// its opacity (times <paramref name="fade"/>: a scene dimmed round its selection). Frozen and kept, one per kind.
    /// </summary>
    public static Material Material(BioMaterial m, double fade = 1)
    {
        var key = (m, fade);
        if (Materials.TryGetValue(key, out var made)) return made;
        if (Materials.Count > 2048) Materials.Clear();
        var c = K.Hex(m.Hex);
        var alpha = Math.Clamp(m.Alpha * fade, 0, 1);
        var g = new MaterialGroup();
        if (m.Unlit)
        {
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(Colors.Black) { Opacity = alpha }));
            g.Children.Add(new EmissiveMaterial(new SolidColorBrush(c) { Opacity = alpha }));
        }
        else
        {
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c) { Opacity = alpha }));
            if (alpha >= 0.999)
            {
                var rough = Math.Clamp(m.Roughness, 0, 1);
                var shine = (byte)Math.Round(255 * Math.Clamp(0.33 * (1 - rough) / 0.65, 0, 0.5));
                g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(shine, 0xff, 0xff, 0xff)), 20 + 60 * (1 - rough)));
            }
            if (m.Emission > 0) g.Children.Add(new EmissiveMaterial(new SolidColorBrush(c) { Opacity = Math.Clamp(m.Emission, 0, 1) * alpha }));
        }
        g.Freeze();
        Materials[key] = g;
        return g;
    }

    /// <summary>A scene's items batched (§3.4): one mesh per material and stacking order, in the order first met; each
    /// with the map from its vertices and triangles back to the objects they came from. Items <paramref name="skip"/>
    /// names are left out (the selected, drawn on their own). Plain arithmetic: run it off the UI thread.</summary>
    public static List<(BioMaterial Material, int Order, BioMeshBatch Batch)> Batches(IEnumerable<BioSceneItem> items, Func<string?, bool>? skip = null)
    {
        var groups = new Dictionary<(BioMaterial, int), BioMeshBatch>();
        var keys = new List<(BioMaterial, int)>();
        foreach (var it in items)
        {
            if (skip?.Invoke(it.Id) == true) continue;
            var key = (it.Material, it.Order);
            if (!groups.TryGetValue(key, out var b)) { groups[key] = b = new BioMeshBatch(); keys.Add(key); }
            b.Add(it.Mesh, it.Id ?? "");
        }
        return keys.Where(k => !groups[k].Mesh.IsEmpty).Select(k => (k.Item1, k.Item2, groups[k])).ToList();
    }
}

/// <summary>Something drawn flat toward the viewer that follows a point of the scene as it turns (an atom's symbol, a
/// tag): its model (made facing +z, centred on the origin), the point, how far toward the viewer it is lifted, the object
/// a press on it picks (if any), and how far it is set right and up from the point on the view, whichever way the model
/// is turned (a charge's sign at its atom's upper right).</summary>
public sealed record BioBillboard(Model3D Model, BioVec Anchor, double Lift = 0, string? Id = null, double ShiftX = 0, double ShiftY = 0);

/// <summary>A few letters drawn in 2D over a 3D scene, on top of the whole model, following a point of it as it turns
/// (a strand's "3′", a carbon's "2′"): what it says, the point, how tall it is in the scene's units (drawn 11–17 px
/// whatever the zoom), and — for an atom's number — the atom's centre and radius, so it is set just outside the atom's
/// disc on the screen whichever way the model is turned. A charge's sign (<paramref name="Charge"/>, in its own colour)
/// sits on its atom's disc's edge at the upper right, as a charge is written (N⁺, O⁻), 18–26 px, over the labels too.</summary>
public sealed record BioMark(string Text, BioVec Anchor, double Size, BioVec? Atom = null, double AtomRadius = 0, Color? Colour = null, bool Charge = false);

/// <summary>Billboards as the molecule builder draws its labels.</summary>
public static class BioBillboards
{
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    /// <summary>Words flat toward the viewer, <paramref name="size"/> tall (scene units): white and glowing on a dark atom, or
    /// in a colour (the molecule builder's atom labels).</summary>
    public static GeometryModel3D Text(string s, double size, Color colour, bool glow = true)
    {
        var brush0 = new SolidColorBrush(colour);
        brush0.Freeze();
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 100, brush0, 1);
        var geo = ft.BuildGeometry(new Point(0, 0));
        var bounds = geo.Bounds.IsEmpty ? new Rect(0, 0, 60, 70) : geo.Bounds;
        var drawing = new DrawingGroup();
        // (a flat sign — "−", a dash — would be stretched to fill the whole quad, a bar many times its size: it is drawn
        //  inside the height of a capital instead, where the font puts it)
        var capHeight = Face.CapsHeight * 100;
        if (bounds.Height < 0.4 * capHeight)
        {
            bounds = Rect.Union(bounds, new Rect(bounds.X, ft.Baseline - capHeight, bounds.Width, capHeight));
            drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(bounds)));
        }
        drawing.Children.Add(new GeometryDrawing(brush0, null, geo));
        var brush = new DrawingBrush(drawing) { Stretch = Stretch.Fill };
        brush.Freeze();
        double w = bounds.Width / bounds.Height * size, h = size;
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(-w / 2, -h / 2, 0));
        mesh.Positions.Add(new Point3D(w / 2, -h / 2, 0));
        mesh.Positions.Add(new Point3D(w / 2, h / 2, 0));
        mesh.Positions.Add(new Point3D(-w / 2, h / 2, 0));
        for (var k = 0; k < 4; k++) mesh.Normals.Add(new Vector3D(0, 0, 1));
        mesh.TextureCoordinates.Add(new Point(0, 1));
        mesh.TextureCoordinates.Add(new Point(1, 1));
        mesh.TextureCoordinates.Add(new Point(1, 0));
        mesh.TextureCoordinates.Add(new Point(0, 0));
        foreach (var i in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(i);
        mesh.Freeze();
        var m = new MaterialGroup();
        m.Children.Add(new DiffuseMaterial(brush));
        if (glow) m.Children.Add(new EmissiveMaterial(brush));
        m.Freeze();
        return new GeometryModel3D(mesh, m);
    }
}

/// <summary>Molecules in a biology scene: a BioMoleculeModel drawn with the chemistry builder's CPK colours and radii
/// (MBChem), batched, its atoms picked as "atom:&lt;id&gt;"; and the atoms' symbols as the builder labels them.</summary>
public static class BioMoleculeScene
{
    /// <summary>An atom's CPK colour as "#rrggbb" (H white, C grey, O red, N blue, P orange, S yellow).</summary>
    public static string CpkHex(string symbol) => BioColors.Hex(MBChem.ColorOf(symbol));

    /// <summary>An atom's drawn radius (Å) in a style, from the chemistry tables.</summary>
    public static double Radius(string symbol, BioAtomStyle style) => BioMoleculeMeshes.Radius(style, MBChem.Covalent(symbol), MBChem.Vdw(symbol));

    /// <summary>A molecule's items: CPK colours unless <paramref name="color"/> gives an atom another (by part or class), H
    /// atoms left out when <paramref name="hydrogens"/> is off. Safe off the UI thread.</summary>
    public static List<BioSceneItem> Items(BioMoleculeModel model, BioAtomStyle style, bool hydrogens = true, Func<BioMoleculeModel.Atom, string?>? color = null)
    {
        var cpk = new Dictionary<string, string>();
        var radii = new Dictionary<string, double>();
        foreach (var a in model.Atoms)
        {
            if (cpk.ContainsKey(a.Element)) continue;
            cpk[a.Element] = CpkHex(a.Element);
            radii[a.Element] = Radius(a.Element, style);
        }
        return BioMoleculeMeshes.Items(model, style, a => radii.GetValueOrDefault(a.Element, 0.3), a => color?.Invoke(a) ?? cpk.GetValueOrDefault(a.Element, "#c8c8c8"),
            skip: a => !hydrogens && a.Element == "H");
    }

    /// <summary>The atoms' symbols, facing the viewer on each atom's near side (the chemistry builder's labels), as
    /// billboards for a scene's extras. On the UI thread (they are drawn text).</summary>
    public static List<BioBillboard> Symbols(BioMoleculeModel model, BioAtomStyle style, bool hydrogens = true)
    {
        if (style == BioAtomStyle.Sticks) return new List<BioBillboard>();
        var out_ = new List<BioBillboard>();
        foreach (var a in model.Atoms)
        {
            if (!hydrogens && a.Element == "H") continue;
            var r = Radius(a.Element, style);
            var dark = a.Element is "H" or "F" or "Cl";
            out_.Add(new BioBillboard(BioBillboards.Text(a.Element, Math.Max(0.32, r * 0.75) * 0.7, dark ? Color.FromRgb(0x1f, 0x1f, 0x1f) : Colors.White, glow: !dark),
                a.Pos, r * 1.03, $"atom:{a.Id}"));
        }
        return out_;
    }
}

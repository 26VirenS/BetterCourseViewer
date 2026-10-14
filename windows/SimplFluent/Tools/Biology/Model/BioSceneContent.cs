namespace Simpl.Tools;

// What a biology 3D scene is made of, as plain values (spec §3.4), the Mac's BioSceneContent.swift: pickable objects as
// meshes with their materials, labels anchored to them, and the ball the camera frames. Values only — no WPF — so a tool
// can make its scene off the UI thread and the shared view (BioSceneView.cs) batches it into one frozen
// MeshGeometry3D per material, keeping which vertices and triangles are whose for picking.

/// <summary>How a surface is shaded (as the chemistry scenes are).</summary>
/// <param name="Hex">"#rrggbb".</param>
/// <param name="Alpha">1 opaque; membranes 0.25–0.45 (drawn after the opaque parts, both sides: Viewport3D does not sort).</param>
/// <param name="Roughness">0.35 for molecules; 0.35–0.6 for organelles; 0.2 for membranes (WPF: a stronger, tighter
/// highlight the smoother it is).</param>
/// <param name="Emission">A glow of its own colour, 0…1 (the selected object's is 0.3).</param>
/// <param name="Unlit">Unlit, its colour as is (marks, leader dots).</param>
/// <param name="DoubleSided">Drawn from both sides (always so when translucent).</param>
public readonly record struct BioMaterial(string Hex, double Alpha = 1, double Roughness = 0.35, double Emission = 0, bool Unlit = false, bool DoubleSided = false)
{
    /// <summary>The same in a colour pair's light or dark value.</summary>
    public BioMaterial(BioHexPair pair, bool dark, double alpha = 1, double roughness = 0.35) : this(pair.HexFor(dark), alpha, roughness) { }

    public bool IsTranslucent => Alpha < 0.999;
}

/// <summary>A thing in a scene: its id (what a click on it picks — "atom:12", "residue:7", "organelle:mito:3"; null for
/// what is not picked), its mesh and its material. <c>Order</c> stacks translucent things (outer shells last: higher).</summary>
public sealed record BioSceneItem(string? Id, BioMesh Mesh, BioMaterial Material, int Order = 0);

/// <summary>A label with a leader line (spec §3.5), anchored in the scene.</summary>
/// <param name="Id">The object it names (pressing the label picks it).</param>
/// <param name="Normal">For a convex outer shell: shown only while this outward normal faces the camera. Without one, the
/// label is shown while a ray from the camera reaches its object first (or within 2% of the anchor's distance).</param>
/// <param name="Area">How much it matters when there are more than 14 (its object's size).</param>
/// <param name="Always">Shown whatever covers its anchor (while the anchor is in front of the camera): a strand's ends,
/// which a question about the helix must always find named.</param>
public sealed record BioLabel3D(string Id, string Text, BioVec Anchor, BioVec? Normal = null, double Area = 1, bool Always = false);

/// <summary>A ball: what a camera frames.</summary>
public readonly record struct BioSphere(BioVec Centre, double Radius);

/// <summary>A scene, as values.</summary>
public sealed class BioSceneContent
{
    public List<BioSceneItem> Items { get; init; } = new();
    public List<BioLabel3D> Labels { get; init; } = new();
    /// <summary>The ball the camera frames; null: worked out from the items.</summary>
    public BioSphere? Frame { get; init; }

    /// <summary>The frame asked for, or one round every item.</summary>
    public BioSphere Framing
    {
        get
        {
            if (Frame is { } f) return f;
            var pts = new List<BioVec>();
            foreach (var it in Items)
            {
                var (c, r) = it.Mesh.Bounds;
                pts.AddRange(new[] { c + new BioVec(r, 0, 0), c - new BioVec(r, 0, 0), c + new BioVec(0, r, 0), c - new BioVec(0, r, 0), c + new BioVec(0, 0, r), c - new BioVec(0, 0, r) });
            }
            var s = BioV.BoundingSphere(pts);
            return new BioSphere(s.Centre, s.Radius);
        }
    }

    /// <summary>The scene's outermost points: for each of <paramref name="directions"/> directions spread evenly over a
    /// sphere, the vertex that reaches furthest that way — the corners of the hull round the scene, near enough (a 256th
    /// of the sphere apart, about 1% of its size at worst) for its outline on the view, wherever the camera stands. The 3D
    /// view frames the model and keeps its labels off it by these (BioOutline): a box round it, seen in perspective, was
    /// wider than a round cell by its near corners and left the corners beside the cell empty.</summary>
    public List<BioVec> OuterPoints(int directions = 256)
    {
        var dirs = new System.Numerics.Vector3[directions];
        var turn = Math.PI * (3 - Math.Sqrt(5));
        for (var i = 0; i < directions; i++)
        {
            var y = 1 - 2 * (i + 0.5) / directions;
            var r = Math.Sqrt(1 - y * y);
            dirs[i] = new System.Numerics.Vector3((float)(Math.Cos(turn * i) * r), (float)y, (float)(Math.Sin(turn * i) * r));
        }
        var best = new float[directions];
        var at = new System.Numerics.Vector3[directions];
        Array.Fill(best, float.MinValue);
        foreach (var it in Items)
            foreach (var q in it.Mesh.Positions)
                for (var k = 0; k < directions; k++)
                {
                    var t = System.Numerics.Vector3.Dot(q, dirs[k]);
                    if (t > best[k]) { best[k] = t; at[k] = q; }
                }
        return Enumerable.Range(0, directions).Where(k => best[k] > float.MinValue).Select(k => new BioVec(at[k].X, at[k].Y, at[k].Z)).Distinct().ToList();
    }

    /// <summary>The items whose id is <paramref name="id"/> or begins with <c>id:</c> (so "organelle:mito" names every mitochondrion).</summary>
    public List<BioSceneItem> ItemsMatching(string id) => Items.Where(i => Matches(i.Id, id)).ToList();

    public static bool Matches(string? itemId, string id) => itemId != null && (itemId == id || itemId.StartsWith(id + ":", StringComparison.Ordinal));
}

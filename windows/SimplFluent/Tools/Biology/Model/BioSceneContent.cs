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
public sealed record BioLabel3D(string Id, string Text, BioVec Anchor, BioVec? Normal = null, double Area = 1);

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

    /// <summary>The items whose id is <paramref name="id"/> or begins with <c>id:</c> (so "organelle:mito" names every mitochondrion).</summary>
    public List<BioSceneItem> ItemsMatching(string id) => Items.Where(i => Matches(i.Id, id)).ToList();

    public static bool Matches(string? itemId, string id) => itemId != null && (itemId == id || itemId.StartsWith(id + ":", StringComparison.Ordinal));
}

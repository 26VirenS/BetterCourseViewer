import Foundation

// What a biology 3D scene is made of, as plain values (spec §3.4): pickable objects as meshes with their materials,
// labels anchored to them, and the ball the camera frames. Values only — no SceneKit — so a tool can make its scene off
// the main thread and the shared view (BioSceneView.swift) batches it into one geometry per material, keeping which
// triangles are whose for picking.

/// How a surface is shaded (physically based, as the chemistry scenes are).
struct BioMaterial: Hashable, Sendable {
    /// "#rrggbb".
    var hex: String
    /// 1 opaque; membranes 0.25–0.45 (drawn after the opaque parts, not writing depth, both sides).
    var alpha: Double = 1
    /// 0.35 for molecules; 0.35–0.6 for organelles; 0.2 for membranes.
    var roughness: Double = 0.35
    /// A glow of its own colour, 0…1 (the selected object's is 0.3).
    var emission: Double = 0
    /// Unlit, its colour as is (marks, leader dots).
    var unlit: Bool = false
    /// Drawn from both sides (always so when translucent).
    var doubleSided: Bool = false

    init(_ hex: String, alpha: Double = 1, roughness: Double = 0.35, emission: Double = 0, unlit: Bool = false, doubleSided: Bool = false) {
        self.hex = hex
        self.alpha = alpha
        self.roughness = roughness
        self.emission = emission
        self.unlit = unlit
        self.doubleSided = doubleSided
    }

    /// The same in a colour pair's light or dark value.
    init(_ pair: BioHexPair, dark: Bool, alpha: Double = 1, roughness: Double = 0.35) {
        self.init(pair.hex(dark: dark), alpha: alpha, roughness: roughness)
    }

    var isTranslucent: Bool { alpha < 0.999 }
}

/// A thing in a scene: its id (what a click on it picks — "atom:12", "residue:7", "organelle:mito:3"; nil for what is
/// not picked), its mesh and its material. `order` stacks translucent things (outer shells last: higher).
struct BioSceneItem: Sendable {
    var id: String?
    var mesh: BioMesh
    var material: BioMaterial
    var order: Int = 0

    init(id: String?, mesh: BioMesh, material: BioMaterial, order: Int = 0) {
        self.id = id
        self.mesh = mesh
        self.material = material
        self.order = order
    }
}

/// A label with a leader line (spec §3.5), anchored in the scene.
struct BioLabel3D: Equatable, Sendable {
    /// The object it names (pressing the label picks it).
    let id: String
    let text: String
    let anchor: BioVec
    /// For a convex outer shell: shown only while this outward normal faces the camera. Without one, the label is shown
    /// while a ray from the camera reaches its object first (or within 2% of the anchor's distance).
    var normal: BioVec?
    /// How much it matters when there are more than 14 (its object's size).
    var area: Double = 1

    init(id: String, text: String, anchor: BioVec, normal: BioVec? = nil, area: Double = 1) {
        self.id = id
        self.text = text
        self.anchor = anchor
        self.normal = normal
        self.area = area
    }
}

/// A scene, as values.
struct BioSceneContent: Sendable {
    var items: [BioSceneItem] = []
    var labels: [BioLabel3D] = []
    /// The ball the camera frames (centre, radius); nil: worked out from the items.
    var frame: BioSphere?

    init(items: [BioSceneItem] = [], labels: [BioLabel3D] = [], frame: BioSphere? = nil) {
        self.items = items
        self.labels = labels
        self.frame = frame
    }

    /// The frame asked for, or one round every item.
    var framing: BioSphere {
        if let frame { return frame }
        var pts: [BioVec] = []
        for it in items {
            let b = it.mesh.bounds
            pts += [b.centre + BioVec(b.radius, 0, 0), b.centre - BioVec(b.radius, 0, 0), b.centre + BioVec(0, b.radius, 0),
                    b.centre - BioVec(0, b.radius, 0), b.centre + BioVec(0, 0, b.radius), b.centre - BioVec(0, 0, b.radius)]
        }
        let s = BioV.boundingSphere(pts)
        return BioSphere(centre: s.centre, radius: s.radius)
    }

    /// The items whose id is `id` or begins with `id:` (so "organelle:mito" names every mitochondrion).
    func items(matching id: String) -> [BioSceneItem] { items.filter { BioSceneContent.matches($0.id, id) } }

    static func matches(_ itemID: String?, _ id: String) -> Bool {
        guard let itemID else { return false }
        return itemID == id || itemID.hasPrefix(id + ":")
    }
}

/// A ball: what a camera frames.
struct BioSphere: Equatable, Sendable {
    var centre: BioVec
    var radius: Double
}

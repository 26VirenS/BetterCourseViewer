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
    /// (1.3.79) The scene's ball and outline, worked out once off the main thread as the scene is made (`measured()`).
    var shape: BioSceneShape?

    init(items: [BioSceneItem] = [], labels: [BioLabel3D] = [], frame: BioSphere? = nil) {
        self.items = items
        self.labels = labels
        self.frame = frame
    }

    /// The scene with its ball and outline worked out (`shape`), for the view to frame and label it by.
    func measured() -> BioSceneContent {
        var c = self
        c.shape = BioSceneShape(framing: framing, outline: outline())
        return c
    }

    /// (1.3.79) Points on the outside of what is drawn: the vertex farthest along each of 26 directions (the axes, and
    /// the diagonals of a cube's edges and corners), from at most about 4,000 of each item's vertices. The camera frames
    /// them and the labels sit just beside them, so a long thin model — a helix, a chain — is framed and labelled by its
    /// own shape: framed and labelled by the ball round it, a helix stood small in a wide card, its labels out at the
    /// card's edges.
    func outline() -> [BioVec] {
        var best = [Float](repeating: -.infinity, count: BioSceneContent.directions.count)
        var at = [SIMD3<Float>](repeating: .zero, count: BioSceneContent.directions.count)
        for it in items where !it.mesh.positions.isEmpty {
            let ps = it.mesh.positions
            for i in stride(from: 0, to: ps.count, by: max(1, ps.count / 4096)) {
                let p = ps[i]
                for k in BioSceneContent.directions.indices {
                    let d = (p * BioSceneContent.directions[k]).sum()
                    if d > best[k] { best[k] = d; at[k] = p }
                }
            }
        }
        var out: [BioVec] = []
        for k in best.indices where best[k].isFinite {
            let p = BioVec(Double(at[k].x), Double(at[k].y), Double(at[k].z))
            if !out.contains(p) { out.append(p) }
        }
        return out
    }

    /// The farthest of `points` along each of the 26 directions `outline()` uses (an outline grown by more points: the
    /// texts a view adds beside a model).
    static func extremes(of points: [BioVec]) -> [BioVec] {
        var out: [BioVec] = []
        for d in directions {
            let dir = BioVec(Double(d.x), Double(d.y), Double(d.z))
            if let p = points.max(by: { BioV.dot($0, dir) < BioV.dot($1, dir) }), !out.contains(p) { out.append(p) }
        }
        return out
    }

    /// The axes and the diagonals of a cube's edges and corners, each of length 1.
    static let directions: [SIMD3<Float>] = {
        var dirs: [SIMD3<Float>] = []
        for x in -1...1 { for y in -1...1 { for z in -1...1 where x != 0 || y != 0 || z != 0 {
            let d = SIMD3<Float>(Float(x), Float(y), Float(z))
            dirs.append(d / (d * d).sum().squareRoot())
        } } }
        return dirs
    }()

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

/// (1.3.79) A scene's ball (its frame, or the ball round its items) and its outline (`BioSceneContent.outline()`).
struct BioSceneShape: Sendable {
    let framing: BioSphere
    let outline: [BioVec]
}

/// (1.3.80) How every 3D view frames its model — the biology scenes (BioSceneView), the element's atom, orbitals and
/// electron cloud (Atom3D) and the molecule builder: the model, with its columns of labels beside it where it has them,
/// fills 84% of the view's limiting side (its height, or its width), on its first frame, on Reset view and each time
/// the view is sized again — a tool's popup resized by its edges. Worked out for a camera whose field of view is up and
/// down (SceneKit's), looking along −z at `aim`. Before, a biology scene was framed by a ball round it within the
/// narrower of the two (a helix's ball, round its length, is as wide as the helix is long, and the label columns had
/// already taken most of the width), and the chemistry views stood their camera at a set distance whatever the view's
/// shape: DNA & RNA's helix stood in 40% of its view's height, small in the middle.
enum SceneFill {
    /// The share of the view's limiting side the model, with its labels, fills.
    static let share = 0.84
    /// The least share the model itself is left with where its labels want more room beside it than the view has
    /// (they then come in over its edges): a camera stood back for them no further than where the model, unlabelled,
    /// fills this much (`limits(…, share: least)`). A cell with sixteen long labels in a popup's middle column stood in
    /// 46% of it.
    static let least = 0.75

    /// The tangents of the half-angles the model may fill, across and up and down, for a camera of `fov` degrees up and
    /// down and a view `width` × `height` points: `share` of the height, and `share` of the width less `beside` points
    /// at each side for a column of labels — so the model and its labels together fill `share` of the width — the model
    /// keeping 46% of the width at least where its labels would leave it less (they then come in over its edges).
    static func limits(fov: Double, width: Double, height: Double, beside: Double = 0, share: Double = SceneFill.share) -> (across: Double, up: Double) {
        let t = tan(fov / 2 * Double.pi / 180)
        guard width > 1, height > 1 else { return (share * t, share * t) }
        let room = max(share * width - 2 * beside, 0.46 * width)
        return (t * room / height, share * t)
    }

    /// How far from `aim` the camera stands, looking along −z, for each of `points` — at its own depth — to fall within
    /// `limits`; `turning`: wherever a turn of the model about the up axis through `aim` takes it (a point `r` from the
    /// axis reaches furthest across where its line of sight grazes its circle, and highest at the circle's front).
    static func distance(_ points: [BioVec], aim: BioVec, limits: (across: Double, up: Double), turning: Bool = false) -> Double {
        var d = 0.0
        for p in points {
            let q = p - aim
            if turning {
                let r = (q.x * q.x + q.z * q.z).squareRoot()
                d = max(d, r / sin(atan(limits.across)), r + abs(q.y) / limits.up)
            } else {
                d = max(d, q.z + abs(q.x) / limits.across, q.z + abs(q.y) / limits.up)
            }
        }
        return d
    }

    /// How much of a view `width` × `height` a model fills (its points' furthest reach from the middle, across and up and
    /// down, as shares of each side), seen from `distance` along +z from `aim` through a camera of `fov` degrees up and
    /// down — for a picture's console to say how a model was framed.
    static func filled(_ points: [BioVec], aim: BioVec, distance d: Double, fov: Double, width: Double, height: Double) -> (across: Double, up: Double) {
        let t = tan(fov / 2 * Double.pi / 180)
        guard width > 1, height > 1, t > 0 else { return (0, 0) }
        var across = 0.0, up = 0.0
        for p in points {
            let q = p - aim
            guard d - q.z > 1e-9 else { continue }
            across = max(across, abs(q.x) / (d - q.z) / (t * width / height))
            up = max(up, abs(q.y) / (d - q.z) / t)
        }
        return (across, up)
    }

    /// How far from its centre the camera stands for a ball of `radius` to fall within `limits`.
    static func distance(radius: Double, limits: (across: Double, up: Double)) -> Double {
        max(radius, 1e-6) / sin(atan(min(limits.across, limits.up)))
    }

    /// The middle of the box round `points` (nil for none): where the camera looks, for the model to stand in the
    /// middle of its view.
    static func middle(_ points: [BioVec]) -> BioVec? {
        guard var lo = points.first else { return nil }
        var hi = lo
        for p in points {
            lo = BioVec(min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z))
            hi = BioVec(max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z))
        }
        return (lo + hi) / 2
    }
}

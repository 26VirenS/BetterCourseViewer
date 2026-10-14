import Foundation

// The Cell Explorer's organelles as meshes (spec §5.1.5's recipes, exact geometry): the cut shells (their outer and inner
// walls and the cut faces, each shaded on its own), the nucleus's envelope with its pores, the rough ER's sheets, a
// Golgi stack's curved cisternae, the mitochondrion with its cristae (whole, or cut lengthwise), the chloroplast with its
// grana and lamellae, lysosomes and peroxisomes, the centrioles' nine triplets, and the small things the cell is full of.
// Each organelle is made once in its own frame (its long or thin axis along y) and placed by CellExplorerBuild.swift.
// Lengths in µm. Foundation only.

enum CEGeo {
    static func smoothstep(_ e0: Double, _ e1: Double, _ x: Double) -> Double {
        let t = min(max((x - e0) / (e1 - e0), 0), 1)
        return t * t * (3 - 2 * t)
    }

    static func vec(_ p: SIMD3<Float>) -> BioVec { BioVec(Double(p.x), Double(p.y), Double(p.z)) }

    /// Whether a point is in the cutaway's wedge about `c`: the octant x, y, z > 0 (§3.4 item 3).
    static func inWedge(_ p: BioVec, _ c: BioVec = BioVec(0, 0, 0)) -> Bool { p.x > c.x && p.y > c.y && p.z > c.z }

    /// How far a point is from the wedge about `c` (0 inside it).
    static func wedgeDistance(_ p: BioVec, _ c: BioVec = BioVec(0, 0, 0)) -> Double {
        let d = p - c
        if d.x > 0 && d.y > 0 && d.z > 0 { return 0 }
        return BioV.length(BioVec(min(d.x, 0), min(d.y, 0), min(d.z, 0)))
    }

    /// The triangles whose centroid `keep` accepts, the vertices no triangle uses dropped.
    static func filtered(_ m: BioMesh, _ keep: (BioVec) -> Bool) -> BioMesh {
        var out = BioMesh()
        var map = [Int32](repeating: -1, count: m.positions.count)
        for t in stride(from: 0, to: m.indices.count - 2, by: 3) {
            let a = Int(m.indices[t]), b = Int(m.indices[t + 1]), c = Int(m.indices[t + 2])
            let centroid = (vec(m.positions[a]) + vec(m.positions[b]) + vec(m.positions[c])) / 3
            guard keep(centroid) else { continue }
            for i in [a, b, c] {
                if map[i] < 0 {
                    map[i] = Int32(out.positions.count)
                    out.positions.append(m.positions[i])
                    out.normals.append(m.normals[i])
                }
                out.indices.append(UInt32(map[i]))
            }
        }
        return out
    }

    /// A mesh moved (its positions only: quicker than a frame, for the explode slider).
    static func moved(_ m: BioMesh, _ d: BioVec) -> BioMesh {
        guard d.x != 0 || d.y != 0 || d.z != 0 else { return m }
        var out = m
        let f = SIMD3<Float>(Float(d.x), Float(d.y), Float(d.z))
        for i in 0..<out.positions.count { out.positions[i] += f }
        return out
    }

    /// A shell made by BioShapes.radial (centred on the origin) split into its outer wall, its inner wall and its cut
    /// faces, by which way each triangle faces: out, in, or across.
    static func splitShell(_ m: BioMesh) -> (outer: BioMesh, inner: BioMesh, caps: BioMesh) {
        var outer = BioMesh(), inner = BioMesh(), caps = BioMesh()
        for t in stride(from: 0, to: m.indices.count - 2, by: 3) {
            let ids = [Int(m.indices[t]), Int(m.indices[t + 1]), Int(m.indices[t + 2])]
            let c = ids.reduce(BioVec(0, 0, 0)) { $0 + vec(m.positions[$1]) } / 3
            let n = ids.reduce(BioVec(0, 0, 0)) { $0 + vec(m.normals[$1]) }
            let facing = BioV.dot(BioV.unit(n), BioV.unit(c))
            var piece = BioMesh()
            for i in ids { piece.positions.append(m.positions[i]); piece.normals.append(m.normals[i]) }
            piece.indices = [0, 1, 2]
            if facing > 0.1 { outer.append(piece) } else if facing < -0.1 { inner.append(piece) } else { caps.append(piece) }
        }
        return (outer, inner, caps)
    }

    /// A unit vector at right angles to `d`.
    static func perpendicular(_ d: BioVec) -> BioVec {
        BioV.unit(BioV.cross(d, abs(d.y) < 0.9 ? BioVec(0, 1, 0) : BioVec(1, 0, 0)))
    }

    /// A direction within `degrees` of `d`, uniform over that cone: two draws (cos of the angle from d, then the
    /// azimuth about it).
    static func cone(_ d: BioVec, _ degrees: Double, _ rng: inout BioRandom) -> BioVec {
        let cmax = cos(degrees * .pi / 180)
        let c = 1 - rng.uniform() * (1 - cmax)
        let phi = 2 * Double.pi * rng.uniform()
        let s = max(0, 1 - c * c).squareRoot()
        let e1 = perpendicular(d), e2 = BioV.cross(BioV.unit(d), e1)
        return BioV.unit(BioV.unit(d) * c + (e1 * cos(phi) + e2 * sin(phi)) * s)
    }

    /// `d` turned by exactly `degrees` toward an azimuth drawn about it (one draw).
    static func turned(_ d: BioVec, _ degrees: Double, _ rng: inout BioRandom) -> BioVec {
        let phi = 2 * Double.pi * rng.uniform()
        let e1 = perpendicular(d), e2 = BioV.cross(BioV.unit(d), e1)
        let a = degrees * .pi / 180
        return BioV.unit(BioV.unit(d) * cos(a) + (e1 * cos(phi) + e2 * sin(phi)) * sin(a))
    }

    /// A frame turned by a random rotation (BioRandom.rotation's), at a point.
    static func frame(_ r: (axis: BioVec, degrees: Double), at p: BioVec) -> BioFrame {
        BioFrame.rotation(axis: r.axis, degrees: r.degrees, at: p)
    }

    /// A frame at `p` with y along `y` and z as near `towardZ` as y allows (a cut organelle's cut half facing it).
    static func frame(y: BioVec, towardZ z: BioVec, at p: BioVec) -> BioFrame {
        let yy = BioV.unit(y)
        var zz = z - yy * BioV.dot(z, yy)
        zz = BioV.length(zz) > 1e-6 ? BioV.unit(zz) : perpendicular(yy)
        return BioFrame(origin: p, x: BioV.cross(yy, zz), y: yy, z: zz)
    }

    /// "#rrggbb" mixed toward another by t.
    static func mix(_ a: String, _ b: String, _ t: Double) -> String {
        let x = BioPalette.rgb(a), y = BioPalette.rgb(b)
        func byte(_ v: Double) -> String {
            let s = String(min(max(Int((v * 255).rounded()), 0), 255), radix: 16)
            return s.count == 1 ? "0" + s : s
        }
        return "#" + byte(x.r + (y.r - x.r) * t) + byte(x.g + (y.g - x.g) * t) + byte(x.b + (y.b - x.b) * t)
    }

    /// A lighter tint of a colour pair (a cut face, §5.1.5 "Membranes and walls").
    static func lighter(_ p: BioHexPair, _ t: Double = 0.45) -> BioHexPair {
        BioHexPair(mix(p.light, "#ffffff", t), mix(p.dark, "#ffffff", t * 0.7))
    }

    /// A darker shade (the plant wall's outer face, a lysosome's contents).
    static func darker(_ p: BioHexPair, _ t: Double = 0.2) -> BioHexPair {
        BioHexPair(mix(p.light, "#000000", t), mix(p.dark, "#000000", t * 0.8))
    }

    /// A rounded box's signed distance (half extents, corner radius).
    static func roundedBoxSDF(_ p: BioVec, half: BioVec, corner: Double) -> Double {
        let rc = min(corner, half.x, half.y, half.z)
        let q = BioVec(abs(p.x) - (half.x - rc), abs(p.y) - (half.y - rc), abs(p.z) - (half.z - rc))
        let outside = BioV.length(BioVec(max(q.x, 0), max(q.y, 0), max(q.z, 0)))
        return outside + min(max(q.x, max(q.y, q.z)), 0) - rc
    }

    /// A capsule along x (its axis from −half to +half), radius r: signed distance.
    static func capsuleXSDF(_ p: BioVec, half: Double, r: Double) -> Double {
        let x = min(max(p.x, -half), half)
        return BioV.length(p - BioVec(x, 0, 0)) - r
    }

    /// A capsule along x's radius function (BioShapes' capsule stands along y).
    static func capsuleXRadius(_ r: Double, length: Double) -> (BioVec) -> Double {
        let f = BioShapes.capsuleRadius(r, length: length)
        return { d in f(BioVec(d.y, d.x, d.z)) }
    }

    /// The outward unit normal of a surface given as a signed distance, numerically.
    static func gradient(_ f: (BioVec) -> Double, at p: BioVec, h: Double = 1e-4) -> BioVec {
        let g = BioVec(f(p + BioVec(h, 0, 0)) - f(p - BioVec(h, 0, 0)),
                       f(p + BioVec(0, h, 0)) - f(p - BioVec(0, h, 0)),
                       f(p + BioVec(0, 0, h)) - f(p - BioVec(0, 0, h)))
        return BioV.unit(g)
    }

    /// Points along a cubic Bézier.
    static func bezier(_ p0: BioVec, _ p1: BioVec, _ p2: BioVec, _ p3: BioVec, points n: Int) -> [BioVec] {
        (0..<n).map { i in
            let t = Double(i) / Double(max(n - 1, 1)), u = 1 - t
            return p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t)
        }
    }

    /// The area and enclosed volume of a closed mesh (the divergence theorem).
    static func areaVolume(_ m: BioMesh) -> (area: Double, volume: Double) {
        var a = 0.0, v = 0.0
        for t in stride(from: 0, to: m.indices.count - 2, by: 3) {
            let p0 = vec(m.positions[Int(m.indices[t])]), p1 = vec(m.positions[Int(m.indices[t + 1])]), p2 = vec(m.positions[Int(m.indices[t + 2])])
            a += BioV.length(BioV.cross(p1 - p0, p2 - p0)) / 2
            v += BioV.dot(p0, BioV.cross(p1, p2)) / 6
        }
        return (a, abs(v))
    }

    /// A box (8 corners, flat faces): a peroxisome's crystalline core.
    static func box(half h: BioVec) -> BioMesh {
        var m = BioMesh()
        let c = { (x: Double, y: Double, z: Double) in BioVec(x * h.x, y * h.y, z * h.z) }
        m.flatQuad(c(1, -1, -1), c(1, 1, -1), c(1, 1, 1), c(1, -1, 1), facing: BioVec(1, 0, 0))
        m.flatQuad(c(-1, -1, -1), c(-1, -1, 1), c(-1, 1, 1), c(-1, 1, -1), facing: BioVec(-1, 0, 0))
        m.flatQuad(c(-1, 1, -1), c(-1, 1, 1), c(1, 1, 1), c(1, 1, -1), facing: BioVec(0, 1, 0))
        m.flatQuad(c(-1, -1, -1), c(1, -1, -1), c(1, -1, 1), c(-1, -1, 1), facing: BioVec(0, -1, 0))
        m.flatQuad(c(-1, -1, 1), c(1, -1, 1), c(1, 1, 1), c(-1, 1, 1), facing: BioVec(0, 0, 1))
        m.flatQuad(c(-1, -1, -1), c(-1, 1, -1), c(1, 1, -1), c(1, -1, -1), facing: BioVec(0, 0, -1))
        return m
    }

    /// A flat strip from a to b (a stroma lamella): `width` across (along `side`), `thickness` thick.
    static func slab(from a: BioVec, to b: BioVec, side: BioVec, width: Double, thickness: Double) -> BioMesh {
        let along = BioV.unit(b - a)
        let s = BioV.unit(side - along * BioV.dot(side, along)) * (width / 2)
        let up = BioV.unit(BioV.cross(along, s)) * (thickness / 2)
        var m = BioMesh()
        var corners: [BioVec] = []
        for end in [a, b] {
            corners.append(end + s + up)
            corners.append(end - s + up)
            corners.append(end - s - up)
            corners.append(end + s - up)
        }
        let mid = (a + b) / 2
        for (i, j, k, l) in [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (0, 3, 2, 1), (4, 5, 6, 7)] {
            let face = (corners[i] + corners[j] + corners[k] + corners[l]) / 4
            m.flatQuad(corners[i], corners[j], corners[k], corners[l], facing: face - mid)
        }
        return m
    }

    /// An octahedron (8 triangles): a ribosome drawn at its true size, where there are thousands (§4.1 item 7).
    static func octahedron(radius r: Double) -> BioMesh {
        var m = BioMesh()
        let v = [BioVec(1, 0, 0), BioVec(-1, 0, 0), BioVec(0, 1, 0), BioVec(0, -1, 0), BioVec(0, 0, 1), BioVec(0, 0, -1)]
        for p in v { m.vertex(p * r, p) }
        for (a, b, c) in [(0, 2, 4), (4, 2, 1), (1, 2, 5), (5, 2, 0), (4, 3, 0), (1, 3, 4), (5, 3, 1), (0, 3, 5)] {
            m.triangle(UInt32(a), UInt32(b), UInt32(c))
        }
        return m
    }
}

// MARK: - Organelles in their own frames (§5.1.5)

/// One organelle's look: its pieces, each a mesh and how it is shaded (filled in with the part's colours when placed).
/// Shapes kept by a key, for a recipe that makes the same shape many times (cells are built off the main thread, and
/// two at once, so behind a lock).
final class CEShapeShelf: @unchecked Sendable {
    private let lock = NSLock()
    private var shapes: [String: CEShape] = [:]

    func take(_ key: String) -> CEShape? {
        lock.lock()
        defer { lock.unlock() }
        return shapes[key]
    }

    func put(_ key: String, _ shape: CEShape) {
        lock.lock()
        defer { lock.unlock() }
        shapes[key] = shape
    }
}

struct CEShape: Sendable {
    enum Role: Sendable {
        /// The part's own colour.
        case body
        /// A cut face: a lighter tint.
        case cap
        /// A darker shade (the wall's outer face, a lysosome's contents, a peroxisome's core).
        case dark
        /// Another colour the part names (a nucleus's nucleoplasm, a chloroplast's grana or stroma).
        case named(String)
        /// A colour of its own.
        case colour(BioHexPair)
    }

    struct Piece: Sendable {
        var mesh: BioMesh
        var role: Role = .body
        /// nil: the part's own (opaque, or §5.1's translucency where it has one).
        var alpha: Double? = nil
        var roughness = 0.5
        var order = 0
    }

    var pieces: [Piece] = []

    mutating func add(_ m: BioMesh, _ role: Role = .body, alpha: Double? = nil, roughness: Double = 0.5, order: Int = 0) {
        guard !m.isEmpty else { return }
        pieces.append(Piece(mesh: m, role: role, alpha: alpha, roughness: roughness, order: order))
    }

    /// Placed by a frame.
    func placed(_ f: BioFrame) -> CEShape {
        CEShape(pieces: pieces.map { var p = $0; p.mesh = $0.mesh.transformed(f); return p })
    }

    var triangles: Int { pieces.reduce(0) { $0 + $1.mesh.triangleCount } }
}

enum CERecipes {
    // MARK: Shells

    /// A shell (its outer wall at `outer`, its inner at `inner`), cut by the cutaway's octant if asked, as three
    /// pieces: outer wall, inner wall, cut faces (§5.1.5 "Membranes and walls": the cut face a lighter tint).
    static func shell(outer: @escaping (BioVec) -> Double, inner: @escaping (BioVec) -> Double, cut: Bool,
                      rings: Int, segments: Int) -> (outer: BioMesh, inner: BioMesh, caps: BioMesh) {
        let m = BioShapes.radial(rings: rings, segments: segments, cut: cut ? .octant : nil, inner: inner, radius: outer)
        return CEGeo.splitShell(m)
    }

    /// A box-like star-shaped surface (a rounded box, the plant's wall) meshed on a box's six faces rather than a
    /// sphere's latitudes and longitudes, so its edges and corners run along grid lines instead of across them: each
    /// face of the box `half` (the shape's own proportions) is an n × n grid, every grid point sent along its direction
    /// to `radius`. Cut, the octant x, y, z > 0 is left out (its boundaries are grid lines: n even).
    static func boxSurface(half h: BioVec, n: Int, cut: Bool, radius: (BioVec) -> Double, inward: Bool = false) -> BioMesh {
        let faces: [(n: BioVec, u: BioVec, v: BioVec)] = [
            (BioVec(1, 0, 0), BioVec(0, 1, 0), BioVec(0, 0, 1)), (BioVec(-1, 0, 0), BioVec(0, 0, 1), BioVec(0, 1, 0)),
            (BioVec(0, 1, 0), BioVec(0, 0, 1), BioVec(1, 0, 0)), (BioVec(0, -1, 0), BioVec(1, 0, 0), BioVec(0, 0, 1)),
            (BioVec(0, 0, 1), BioVec(1, 0, 0), BioVec(0, 1, 0)), (BioVec(0, 0, -1), BioVec(0, 1, 0), BioVec(1, 0, 0)),
        ]
        let m = max(2, n + n % 2)
        var mesh = BioMesh()
        for f in faces {
            func q(_ s: Double, _ t: Double) -> BioVec {
                let p = f.n + f.u * s + f.v * t
                return BioVec(p.x * h.x, p.y * h.y, p.z * h.z)
            }
            func point(_ s: Double, _ t: Double) -> BioVec {
                let d = BioV.unit(q(s, t))
                return d * radius(d)
            }
            let base = UInt32(mesh.positions.count)
            let e = 1e-4
            for j in 0...m {
                for i in 0...m {
                    let s = -1 + 2 * Double(i) / Double(m), t = -1 + 2 * Double(j) / Double(m)
                    let p = point(s, t)
                    var nrm = BioV.cross(point(s + e, t) - point(s - e, t), point(s, t + e) - point(s, t - e))
                    if BioV.dot(nrm, p) < 0 { nrm = -nrm }
                    mesh.vertex(p, inward ? -nrm : nrm)
                }
            }
            for j in 0..<m {
                for i in 0..<m {
                    let s = -1 + 2 * (Double(i) + 0.5) / Double(m), t = -1 + 2 * (Double(j) + 0.5) / Double(m)
                    if cut && CEGeo.inWedge(q(s, t)) { continue }
                    let a = base + UInt32(j * (m + 1) + i), b = a + 1, c = a + UInt32(m + 1) + 1, d = a + UInt32(m + 1)
                    if inward {
                        mesh.triangle(a, c, b)
                        mesh.triangle(a, d, c)
                    } else {
                        mesh.triangle(a, b, c)
                        mesh.triangle(a, c, d)
                    }
                }
            }
        }
        return mesh
    }

    /// The cut faces of a box-meshed shell (boxSurface's), between its outer and inner walls, through the very grid
    /// points its walls' cut edges have: on the plane x = 0 from +y to +z, on y = 0 from +z to +x, on z = 0 from +x to +y.
    static func boxCaps(half h: BioVec, n: Int, outer: (BioVec) -> Double, inner: (BioVec) -> Double) -> BioMesh {
        let m = max(2, n + n % 2) / 2
        var mesh = BioMesh()
        // (each plane's edge: along one face from its axis to the box's edge, then down the next face to the other axis)
        let edges: [(facing: BioVec, a: (Double) -> BioVec, b: (Double) -> BioVec)] = [
            (BioVec(1, 0, 0), { k in BioVec(0, 1, k) }, { k in BioVec(0, k, 1) }),
            (BioVec(0, 1, 0), { k in BioVec(k, 0, 1) }, { k in BioVec(1, 0, k) }),
            (BioVec(0, 0, 1), { k in BioVec(1, k, 0) }, { k in BioVec(k, 1, 0) }),
        ]
        for e in edges {
            var dirs: [BioVec] = []
            for i in 0...m { dirs.append(e.a(Double(i) / Double(m))) }
            for i in stride(from: m - 1, through: 0, by: -1) { dirs.append(e.b(Double(i) / Double(m))) }
            let ds = dirs.map { BioV.unit(BioVec($0.x * h.x, $0.y * h.y, $0.z * h.z)) }
            for k in 0..<(ds.count - 1) {
                let d0 = ds[k], d1 = ds[k + 1]
                mesh.flatQuad(d0 * outer(d0), d1 * outer(d1), d1 * inner(d1), d0 * inner(d0), facing: e.facing)
            }
        }
        return mesh
    }

    /// The cut faces of a cell's inside, between `inner` (the vacuole, or nothing: the centre) and `outer` (the
    /// membrane's inner wall), on the three planes of the octant cut: the cytoplasm's "tinted cap" (§5.1.1).
    static func cutFaces(outer: (BioVec) -> Double, inner: ((BioVec) -> Double)?, steps: Int = 24) -> BioMesh {
        var m = BioMesh()
        let half = Double.pi / 2
        // (each face: an edge of the cut swept from the plane's one axis to its other)
        let faces: [(dir: (Double) -> BioVec, facing: BioVec)] = [
            ({ a in BioVec(0, cos(a), sin(a)) }, BioVec(1, 0, 0)),
            ({ a in BioVec(cos(a), sin(a), 0) }, BioVec(0, 0, 1)),
            ({ a in BioVec(sin(a), 0, cos(a)) }, BioVec(0, 1, 0)),
        ]
        for f in faces {
            for k in 0..<steps {
                let a0 = half * Double(k) / Double(steps), a1 = half * Double(k + 1) / Double(steps)
                let d0 = f.dir(a0), d1 = f.dir(a1)
                let o0 = d0 * outer(d0), o1 = d1 * outer(d1)
                if let inner {
                    let i0 = d0 * inner(d0), i1 = d1 * inner(d1)
                    m.flatQuad(o0, o1, i1, i0, facing: f.facing)
                } else {
                    m.flatTriangle(BioVec(0, 0, 0), o0, o1, facing: f.facing)
                }
            }
        }
        return m
    }

    // MARK: Nucleus

    /// §5.1.5 Nucleus: the pores' directions, M points on the Fibonacci lattice.
    static func poreDirections(_ m: Int = 110) -> [BioVec] {
        (0..<m).map { i in
            let y = 1 - (Double(i) + 0.5) * 2 / Double(m)
            let rho = max(0, 1 - y * y).squareRoot()
            let phi = Double(i) * 2.39996323
            return BioVec(rho * cos(phi), y, rho * sin(phi))
        }
    }

    /// The nuclear envelope about the origin: two membranes (outer at R, inner 0.05 µm in, each drawn 0.012 µm thick)
    /// with a hole of radius 0.06 µm through both at each pore (triangles whose centroid is that near a pore's axis
    /// left out), cut if asked; and the nucleoplasm inside, translucent.
    static func envelope(radius R: Double, pores: [BioVec], cut: Bool) -> CEShape {
        var s = CEShape()
        // (1.3.78) the pores sorted by height: a triangle need only be checked against those within the hole's angle of
        // it in height — a few of the 110 — when they are directions (a pore is that near only if its direction is
        // within asin(0.06 / |c|) of the centroid's, and heights differ by no more than the angle). The same holes; it
        // was a quarter of a plant cell's build in a debug build.
        let byHeight = pores.sorted { $0.y < $1.y }
        let heights = byHeight.map(\.y)
        let directions = pores.allSatisfy { abs(BioV.length($0) - 1) < 1e-9 }
        func holed(_ m: BioMesh) -> BioMesh {
            CEGeo.filtered(m) { c in
                let r = BioV.length(c)
                var from = 0, to = byHeight.count
                if directions && r > 0.06 {
                    let w = asin(0.06 / r) + 1e-9, uy = c.y / r
                    var lo = 0, hi = heights.count
                    while lo < hi {
                        let mid = (lo + hi) / 2
                        if heights[mid] < uy - w { lo = mid + 1 } else { hi = mid }
                    }
                    from = lo
                    to = lo
                    while to < heights.count && heights[to] <= uy + w { to += 1 }
                }
                for p in byHeight[from..<to] {
                    let along = BioV.dot(c, p)
                    guard along > 0 else { continue }
                    if BioV.length(c - p * along) < 0.06 { return false }
                }
                return true
            }
        }
        for (o, i) in [(R, R - 0.012), (R - 0.05, R - 0.062)] {
            let sh = shell(outer: BioShapes.sphere(o), inner: BioShapes.sphere(i), cut: cut, rings: 40, segments: 80)
            s.add(holed(sh.outer), roughness: 0.45)
            s.add(holed(sh.inner), roughness: 0.45)
            s.add(sh.caps, .cap, alpha: 1, roughness: 0.5)
        }
        let plasm = BioShapes.radial(rings: 20, segments: 40, cut: cut ? .octant : nil, radius: BioShapes.sphere(R - 0.07))
        let parts = CEGeo.splitShell(plasm)
        s.add(parts.outer, .named("nucleoplasm"), alpha: 0.3, roughness: 0.3, order: 1)
        s.add(parts.caps, .named("nucleoplasm"), alpha: 0.42, roughness: 0.3, order: 1)
        return s
    }

    /// A nuclear pore's ring (torus R 0.07, r 0.02), its axis along y.
    static let poreRing = BioShapes.torus(ring: 0.07, pipe: 0.02, ringSegments: 10, pipeSegments: 4)

    // MARK: Golgi

    /// A Golgi stack about the origin, its axis along +y (cis face at −y, toward the nucleus): `count` cisternae
    /// (§5.1.5: a rounded-rectangle footprint `long` × `short` for the middle one, scaled by 1 − 0.08·|j − mid|,
    /// 0.07 µm thick with rims swollen ×1.8 in the outer 15%, bent into a bowl by d²/(2·2.2 µm), `spacing` apart).
    static func golgi(count: Int, long: Double, short: Double, thickness: Double, spacing: Double, rings: Int = 8, segments: Int = 36) -> CEShape {
        var s = CEShape()
        let mid = Double(count - 1) / 2
        for j in 0..<count {
            let scale = 1 - 0.08 * abs(Double(j) - mid)
            s.add(cisterna(a: long / 2 * scale, b: short / 2 * scale, y: (Double(j) - mid) * spacing, halfThickness: thickness / 2,
                           rings: rings, segments: segments), roughness: 0.45)
        }
        return s
    }

    /// One cisterna: a superellipse footprint (exponent 4: a rounded rectangle) of semi-axes a (x) and b (z), lifted
    /// into a bowl, its two faces meeting at a rounded rim.
    static func cisterna(a: Double, b: Double, y y0: Double, halfThickness: Double, rings: Int = 8, segments: Int = 36) -> BioMesh {
        func foot(_ r: Double, _ psi: Double) -> (Double, Double) {
            let c = cos(psi), s = sin(psi)
            return (a * r * (c < 0 ? -1 : 1) * abs(c).squareRoot(), b * r * (s < 0 ? -1 : 1) * abs(s).squareRoot())
        }
        func half(_ r: Double) -> Double {
            let swell = 1 + 0.8 * CEGeo.smoothstep(0.78, 0.88, r)
            let edge = r <= 0.9 ? 1 : max(0, 1 - pow((r - 0.9) / 0.1, 2)).squareRoot()
            return halfThickness * swell * edge
        }
        var m = BioMesh()
        for side in [1.0, -1.0] {
            var g = BioMesh()
            for i in 0...rings {
                let r = Double(i) / Double(rings)
                for j in 0..<segments {
                    let (x, z) = foot(r, 2 * Double.pi * Double(j) / Double(segments))
                    let d2 = x * x + z * z
                    g.vertex(BioVec(x, y0 + d2 / (2 * 2.2) + side * half(r), z), BioVec(0, side, 0))
                }
            }
            for i in 0..<rings {
                for j in 0..<segments {
                    let p = UInt32(i * segments + j), q = UInt32(i * segments + (j + 1) % segments)
                    let r = UInt32((i + 1) * segments + j), t = UInt32((i + 1) * segments + (j + 1) % segments)
                    g.triangle(p, r, t)
                    g.triangle(p, t, q)
                }
            }
            g = BioShapes.smoothNormals(g)
            // (the top faces +y, the bottom −y)
            let probe = g.normals[segments * (rings / 2)]
            if Double(probe.y) * side < 0 { g = g.flipped() }
            m.append(g)
        }
        return m
    }

    // MARK: Mitochondrion

    /// §5.1.5 Mitochondrion, its long axis along y: the outer membrane (capsule r 0.45, cylinder 1.3: 2.2 µm long)
    /// translucent, the inner (r 0.40) opaque. Whole, the cristae are hidden inside the opaque inner membrane and are
    /// left out; cut lengthwise (the half z > 0 removed), both membranes are shells with their cut faces, the inner's
    /// inside is the matrix's tint, and the nine cristae show.
    static func mitochondrion(half: Bool, fine: Bool = false) -> CEShape {
        let (rings, segments) = fine ? (24, 48) : (12, 24)
        var s = CEShape()
        let L = 1.3
        if !half {
            s.add(BioShapes.capsule(radius: 0.45, length: L, rings: 10, segments: 20), alpha: 0.45, roughness: 0.3, order: 3)
            s.add(BioShapes.capsule(radius: 0.40, length: L, rings: 10, segments: 20), roughness: 0.55)
            return s
        }
        let o = CEGeo.splitShell(BioShapes.radial(rings: rings, segments: segments, cut: .half, inner: BioShapes.capsuleRadius(0.435, length: L),
                                                   radius: BioShapes.capsuleRadius(0.45, length: L)))
        s.add(o.outer, alpha: 0.45, roughness: 0.3, order: 3)
        s.add(o.inner, alpha: 0.45, roughness: 0.3, order: 3)
        s.add(o.caps, .cap, alpha: 1)
        let i = CEGeo.splitShell(BioShapes.radial(rings: rings, segments: segments, cut: .half, inner: BioShapes.capsuleRadius(0.38, length: L),
                                                  radius: BioShapes.capsuleRadius(0.40, length: L)))
        s.add(i.outer, roughness: 0.55)
        s.add(i.inner, .named("matrix"), roughness: 0.6)
        s.add(i.caps, .cap)
        var cristae = BioMesh()
        for k in 0..<9 {
            let y = -0.55 * L + 1.1 * L * Double(k + 1) / 10
            cristae.append(crista(y: y, mirrored: k % 2 == 1, half: true))
        }
        s.add(cristae, .dark, roughness: 0.55)
        return s
    }

    /// A crista at height y: a flat sheet across the inner membrane (radius 0.39, into the membrane) from its attached
    /// side to 75% of the inner diameter, 0.05 µm thick, its free edge rounded; only z ≤ 0 when cut.
    static func crista(y: Double, mirrored: Bool, half: Bool) -> BioMesh {
        let r = 0.39, free = -0.40 + 0.75 * 0.80
        let a0 = acos(free / r)
        let start = half ? Double.pi : a0, end = 2 * Double.pi - a0
        var outline: [(Double, Double)] = []
        let n = 14
        for k in 0...n {
            let a = start + (end - start) * Double(k) / Double(n)
            outline.append((r * cos(a), r * sin(a)))
        }
        // (the free edge, along x = free back up to z = 0 or the circle's top; then, cut, along z = 0)
        if half {
            outline.append((free, 0))
        }
        let sign = mirrored ? -1.0 : 1.0
        let pts = outline.map { BioVec(sign * -$0.0, 0, $0.1) }
        let ht = 0.025
        var m = BioMesh()
        let centre = pts.reduce(BioVec(0, 0, 0), +) / Double(pts.count)
        for k in 0..<pts.count {
            let a = pts[k], b = pts[(k + 1) % pts.count]
            m.flatTriangle(centre + BioVec(0, y + ht, 0), a + BioVec(0, y + ht, 0), b + BioVec(0, y + ht, 0), facing: BioVec(0, 1, 0))
            m.flatTriangle(centre + BioVec(0, y - ht, 0), a + BioVec(0, y - ht, 0), b + BioVec(0, y - ht, 0), facing: BioVec(0, -1, 0))
            let out = BioV.cross(b - a, BioVec(0, 1, 0))
            let facing = BioV.dot(out, (a + b) / 2 - centre) >= 0 ? out : -out
            m.flatQuad(a + BioVec(0, y + ht, 0), b + BioVec(0, y + ht, 0), b + BioVec(0, y - ht, 0), a + BioVec(0, y - ht, 0), facing: facing)
        }
        // (the free edge rounded: a rod along it)
        let zTop = half ? 0.0 : (r * r - free * free).squareRoot()
        let zBottom = -(r * r - free * free).squareRoot()
        m.append(BioShapes.cylinder(from: BioVec(sign * -free, y, zBottom), to: BioVec(sign * -free, y, zTop), radius: ht, segments: 6, caps: false))
        return m
    }

    // MARK: Chloroplast

    /// The 14 grana's centres (§5.1.5): the hexagonal lattice (spacing 1.0 µm) points nearest the lens's centre, inside
    /// 80% of its radius, on its mid-plane (x, z).
    static let granaCentres: [BioVec] = {
        var pts: [BioVec] = []
        for i in -4...4 {
            for j in -4...4 {
                let p = BioVec(Double(i) + 0.5 * Double(j), 0, Double(j) * 3.0.squareRoot() / 2)
                if BioV.length(p) <= 2.0 + 1e-9 { pts.append(p) }
            }
        }
        pts.sort { a, b in
            let la = BioV.length(a), lb = BioV.length(b)
            if abs(la - lb) > 1e-9 { return la < lb }
            return atan2(a.z, a.x) < atan2(b.z, b.x)
        }
        return Array(pts.prefix(14))
    }()

    /// §5.1.5 Chloroplast, its thin axis along y: the envelope (two lenses: 5.0 × 2.4 µm, the inner 0.96 of it),
    /// translucent green; the stroma's tint; 14 grana, each 10 thylakoid discs (r 0.25, 0.035 thick, 0.05 apart) —
    /// at a cell's distance a granum is drawn as one stack the discs' size, `detail` draws every disc; stroma lamellae
    /// joining neighbouring grana at alternating heights (0.12 µm wide, 0.02 thick); two starch grains; and, in
    /// detail, 70S ribosomes and a loop of DNA. Cut lengthwise (z > 0 removed), the envelope is two shells and only the
    /// grana on the kept side are drawn.
    static func chloroplast(detail: Bool, half: Bool, ribosomeRadius: Double, fine: Bool = false) -> CEShape {
        // (1.3.78) made once for each way of asking, then only placed: a plant cell's sixty chloroplasts are two shapes)
        let key = "\(detail)|\(half)|\(ribosomeRadius)|\(fine)"
        if let made = madeChloroplasts.take(key) { return made }
        let made = makeChloroplast(detail: detail, half: half, ribosomeRadius: ribosomeRadius, fine: fine)
        madeChloroplasts.put(key, made)
        return made
    }

    /// The chloroplasts made so far, by how they were asked for.
    private static let madeChloroplasts = CEShapeShelf()

    private static func makeChloroplast(detail: Bool, half: Bool, ribosomeRadius: Double, fine: Bool) -> CEShape {
        let (rings, segments) = fine ? (28, 56) : (16, 32)
        var s = CEShape()
        let lens = { (scale: Double) in BioShapes.lensRadius(diameter: 5.0 * scale, thickness: 2.4 * scale) }
        if half {
            let o = CEGeo.splitShell(BioShapes.radial(rings: rings, segments: segments, cut: .half, inner: lens(0.985), radius: lens(1)))
            s.add(o.outer, alpha: 0.35, roughness: 0.3, order: 3)
            s.add(o.caps, .cap, alpha: 1)
            let i = CEGeo.splitShell(BioShapes.radial(rings: rings, segments: segments, cut: .half, inner: lens(0.945), radius: lens(0.96)))
            s.add(i.outer, alpha: 0.4, roughness: 0.3, order: 2)
            s.add(i.inner, .named("stroma"), alpha: 0.55, roughness: 0.4, order: 2)
            s.add(i.caps, .cap, alpha: 1)
        } else {
            s.add(BioShapes.lens(diameter: 5.0, thickness: 2.4, rings: 10, segments: 20), alpha: 0.35, roughness: 0.3, order: 3)
            s.add(BioShapes.lens(diameter: 4.8, thickness: 2.304, rings: 8, segments: 16), .named("stroma"), alpha: 0.45, roughness: 0.4, order: 2)
        }
        let kept = granaCentres.filter { !half || $0.z <= 0.3 }
        var grana = BioMesh()
        for c in kept {
            if detail {
                for k in 0..<10 {
                    let y = -0.225 + 0.05 * Double(k)
                    grana.append(BioShapes.cylinder(from: c + BioVec(0, y - 0.0175, 0), to: c + BioVec(0, y + 0.0175, 0), radius: 0.25, segments: 12))
                }
            } else {
                grana.append(BioShapes.cylinder(from: c + BioVec(0, -0.2425, 0), to: c + BioVec(0, 0.2425, 0), radius: 0.25, segments: 8))
            }
        }
        s.add(grana, .named("grana"), roughness: 0.5)
        // (lamellae: each pair of neighbours once, alternating heights)
        var lamellae = BioMesh()
        var k = 0
        for i in 0..<kept.count {
            for j in (i + 1)..<kept.count where abs(BioV.distance(kept[i], kept[j]) - 1.0) < 0.05 {
                let y = k % 2 == 0 ? 0.1 : -0.1
                let a = kept[i], b = kept[j]
                let dir = BioV.unit(b - a)
                lamellae.append(CEGeo.slab(from: a + dir * 0.22 + BioVec(0, y, 0), to: b - dir * 0.22 + BioVec(0, y, 0),
                                           side: BioV.cross(dir, BioVec(0, 1, 0)), width: 0.12, thickness: 0.02))
                k += 1
            }
        }
        s.add(lamellae, .named("grana"), roughness: 0.5)
        // (starch grains: white ellipsoids 0.5 × 0.3 µm, between the grana)
        var starch = BioMesh()
        for p in [BioVec(1.5, 0, -0.85), BioVec(-1.5, 0, 0.85)] where !half || p.z <= 0 {
            starch.append(BioShapes.spheroid(0.25, 0.15, 0.25, rings: detail ? 8 : 6, segments: detail ? 16 : 12).translated(p))
        }
        s.add(starch, .colour(BioHexPair("#f4f1e6", "#e9e4d2")), roughness: 0.6)
        if detail {
            var dots = BioMesh()
            var rng = BioRandom(seed: 0xC4_1A)
            let dot = BioShapes.icosphere(radius: ribosomeRadius, subdivisions: 0)
            var n = 0
            while n < 40 {
                let p = BioVec(rng.uniform(-1.8, 1.8), rng.uniform(-0.5, 0.5), rng.uniform(-1.8, 1.8))
                if half && p.z > 0 { continue }
                if BioV.length(BioVec(p.x, 0, p.z)) > 2.0 { continue }
                if granaCentres.contains(where: { BioV.length(BioVec(p.x - $0.x, 0, p.z - $0.z)) < 0.32 }) { continue }
                dots.append(dot.translated(p))
                n += 1
            }
            s.add(dots, .colour(BioHexPair("#2c3e8f", "#7b8fe8")), roughness: 0.5)
            let loop = (0..<16).map { i -> BioVec in
                let t = 2 * Double.pi * Double(i) / 16
                return BioVec(-0.2 + 0.18 * cos(t), 0.03 * sin(3 * t), -0.6 + 0.18 * sin(t))
            }
            s.add(BioPath.tube(loop, radius: 0.012, sides: 6, samples: 2, closed: true), .colour(BioHexPair("#9b6bd1", "#c39cf5")), roughness: 0.5)
        }
        return s
    }

    // MARK: Small organelles

    /// §5.1.5 Lysosome: r 0.3, its single membrane translucent, its contents a darker sphere r 0.22.
    static let lysosome: CEShape = {
        var s = CEShape()
        s.add(BioShapes.sphere(radius: 0.3, rings: 10, segments: 20), alpha: 0.4, roughness: 0.3, order: 2)
        s.add(BioShapes.sphere(radius: 0.22, rings: 8, segments: 16), .dark, roughness: 0.55)
        return s
    }()

    /// §5.1.5 Peroxisome: r 0.25, translucent, with a cube (edge 0.18) for the crystalline core.
    static let peroxisome: CEShape = {
        var s = CEShape()
        s.add(BioShapes.sphere(radius: 0.25, rings: 10, segments: 20), alpha: 0.4, roughness: 0.3, order: 2)
        s.add(CEGeo.box(half: BioVec(0.09, 0.09, 0.09)), .dark, roughness: 0.4)
        return s
    }()

    /// A centriole along y (§5.1.5 Centrosome): 9 triplets of tubes (r 0.012, 0.45 long) at radii 0.07, 0.085 and
    /// 0.1, each triplet's blade turned 30° from the radius (the pinwheel).
    static let centriole: BioMesh = {
        var m = BioMesh()
        let mid = 0.085
        for k in 0..<9 {
            let a = Double(k) * 40 * .pi / 180
            let b = BioVec(cos(a), 0, sin(a)) * mid
            let w = BioVec(cos(a + .pi / 6), 0, sin(a + .pi / 6))
            let bw = BioV.dot(b, w)
            for rho in [0.07, 0.085, 0.1] {
                // (the point of the blade at that radius: |b + s·w| = ρ)
                let disc = max(0, bw * bw - mid * mid + rho * rho)
                let s = rho >= mid ? -bw + disc.squareRoot() : -bw - disc.squareRoot()
                let p = b + w * (abs(rho - mid) < 1e-9 ? 0 : s)
                m.append(BioShapes.cylinder(from: p + BioVec(0, -0.225, 0), to: p + BioVec(0, 0.225, 0), radius: 0.012, segments: 6))
            }
        }
        return m
    }()
}

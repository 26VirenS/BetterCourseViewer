import Foundation

// The biology toolkit's procedural geometry (spec §3.4): triangle meshes with smooth normals for everything its 3D
// scenes draw — spheres and icospheres, cylinders, capsules, tubes and ribbons along a path (Catmull–Rom through the
// points, rotation-minimising frames), tori, spheroids, superellipsoids, rounded boxes, lenses, seeded blobs and
// spherical-shell patches — and the cutaway done in geometry, not with clip planes (so both apps cut the same way): a
// star-shaped solid or shell made with a wedge left out (the octant facing the camera, or a half) and caps where it was
// cut, showing the wall's thickness. Pure arithmetic (Foundation only): BioSceneKit.swift turns a mesh into SceneKit
// geometry, many meshes of one material merged into one geometry with a map back to the object each triangle is from.
// Winding is counter-clockwise seen from the side the normals face.

/// A triangle mesh.
struct BioMesh: Sendable {
    var positions: [SIMD3<Float>] = []
    var normals: [SIMD3<Float>] = []
    var indices: [UInt32] = []

    var vertexCount: Int { positions.count }
    var triangleCount: Int { indices.count / 3 }
    var isEmpty: Bool { indices.isEmpty }

    @discardableResult
    mutating func vertex(_ p: BioVec, _ n: BioVec) -> UInt32 {
        positions.append(SIMD3<Float>(Float(p.x), Float(p.y), Float(p.z)))
        let u = BioV.unit(n)
        normals.append(SIMD3<Float>(Float(u.x), Float(u.y), Float(u.z)))
        return UInt32(positions.count - 1)
    }

    mutating func triangle(_ a: UInt32, _ b: UInt32, _ c: UInt32) {
        indices.append(a)
        indices.append(b)
        indices.append(c)
    }

    /// A flat triangle facing `facing` (its winding chosen so), with that normal at each corner.
    mutating func flatTriangle(_ a: BioVec, _ b: BioVec, _ c: BioVec, facing n: BioVec) {
        let ccw = BioV.dot(BioV.cross(b - a, c - a), n) >= 0
        let i = vertex(a, n), j = vertex(ccw ? b : c, n), k = vertex(ccw ? c : b, n)
        triangle(i, j, k)
    }

    /// A flat quad a–b–c–d (in order round it) facing `facing`.
    mutating func flatQuad(_ a: BioVec, _ b: BioVec, _ c: BioVec, _ d: BioVec, facing n: BioVec) {
        flatTriangle(a, b, c, facing: n)
        flatTriangle(a, c, d, facing: n)
    }

    /// Another mesh's triangles added to this one.
    mutating func append(_ m: BioMesh) {
        let base = UInt32(positions.count)
        positions += m.positions
        normals += m.normals
        indices += m.indices.map { $0 + base }
    }

    /// The mesh placed by a frame (positions through it; normals by its cofactor, so a scaled frame shades right; a
    /// mirroring frame keeps the winding outward).
    func transformed(_ f: BioFrame) -> BioMesh {
        var out = BioMesh()
        out.positions.reserveCapacity(positions.count)
        out.normals.reserveCapacity(normals.count)
        for p in positions { let q = f.apply(BioVec(Double(p.x), Double(p.y), Double(p.z))); out.positions.append(SIMD3<Float>(Float(q.x), Float(q.y), Float(q.z))) }
        for n in normals { let q = f.applyNormal(BioVec(Double(n.x), Double(n.y), Double(n.z))); out.normals.append(SIMD3<Float>(Float(q.x), Float(q.y), Float(q.z))) }
        if f.determinant < 0 {
            out.indices.reserveCapacity(indices.count)
            for t in stride(from: 0, to: indices.count - 2, by: 3) {
                out.indices += [indices[t], indices[t + 2], indices[t + 1]]
            }
        } else {
            out.indices = indices
        }
        return out
    }

    func translated(_ v: BioVec) -> BioMesh { transformed(BioFrame(origin: v)) }
    func scaled(_ s: Double) -> BioMesh { transformed(BioFrame(origin: BioVec(0, 0, 0), x: BioVec(s, 0, 0), y: BioVec(0, s, 0), z: BioVec(0, 0, s))) }

    /// The same surface seen from the other side: normals turned round, winding reversed (an inner wall; an outline's
    /// inverted hull).
    func flipped() -> BioMesh {
        var out = self
        out.normals = normals.map { -$0 }
        out.indices = []
        out.indices.reserveCapacity(indices.count)
        for t in stride(from: 0, to: indices.count - 2, by: 3) { out.indices += [indices[t], indices[t + 2], indices[t + 1]] }
        return out
    }

    /// The bounding sphere of its vertices.
    var bounds: (centre: BioVec, radius: Double) {
        BioV.boundingSphere(positions.map { BioVec(Double($0.x), Double($0.y), Double($0.z)) })
    }
}

/// Where a mesh goes: an origin and three axes (each may be scaled; a negative determinant mirrors).
struct BioFrame {
    var origin: BioVec
    var x: BioVec
    var y: BioVec
    var z: BioVec

    init(origin: BioVec = BioVec(0, 0, 0), x: BioVec = BioVec(1, 0, 0), y: BioVec = BioVec(0, 1, 0), z: BioVec = BioVec(0, 0, 1)) {
        self.origin = origin
        self.x = x
        self.y = y
        self.z = z
    }

    static let identity = BioFrame()

    /// A frame at `origin` whose y axis points along `dir` (things made along y — cylinders, capsules — stood that way).
    static func along(_ dir: BioVec, at origin: BioVec) -> BioFrame {
        let y = BioV.unit(dir)
        let ref = abs(y.y) < 0.9 ? BioVec(0, 1, 0) : BioVec(1, 0, 0)
        let x = BioV.unit(BioV.cross(ref, y))
        let z = BioV.cross(x, y)
        return BioFrame(origin: origin, x: x, y: y, z: z)
    }

    /// A turn of `degrees` about `axis` (right-handed), at `origin`, scaled by `scale`.
    static func rotation(axis: BioVec, degrees: Double, at origin: BioVec = BioVec(0, 0, 0), scale: Double = 1) -> BioFrame {
        BioFrame(origin: origin, x: BioV.rotate(BioVec(1, 0, 0), axis: axis, degrees: degrees) * scale,
                 y: BioV.rotate(BioVec(0, 1, 0), axis: axis, degrees: degrees) * scale,
                 z: BioV.rotate(BioVec(0, 0, 1), axis: axis, degrees: degrees) * scale)
    }

    func apply(_ p: BioVec) -> BioVec { origin + p.x * x + p.y * y + p.z * z }
    /// A direction (no origin).
    func applyVector(_ v: BioVec) -> BioVec { v.x * x + v.y * y + v.z * z }
    /// A normal: by the cofactor matrix (det · M⁻ᵀ), so it stays at right angles to a scaled surface.
    func applyNormal(_ n: BioVec) -> BioVec {
        let s: Double = determinant < 0 ? -1 : 1
        return BioV.unit((n.x * BioV.cross(y, z) + n.y * BioV.cross(z, x) + n.z * BioV.cross(x, y)) * s)
    }

    var determinant: Double { BioV.dot(x, BioV.cross(y, z)) }

    /// This frame after another: `other.apply(self.apply(p))`.
    func then(_ other: BioFrame) -> BioFrame {
        BioFrame(origin: other.apply(origin), x: other.applyVector(x), y: other.applyVector(y), z: other.applyVector(z))
    }
}

/// The cutaway (spec §3.4 item 3): what is left out of a star-shaped solid or shell, in its own frame.
enum BioCut: String {
    /// The octant x > 0, y > 0, z > 0 (the front upper right, toward the camera).
    case octant
    /// The half z > 0 (a slice; a mitochondrion cut lengthwise).
    case half

    /// Whether a point (in the shape's frame, about its centre) is in the part left out.
    func removes(_ p: BioVec) -> Bool {
        switch self {
        case .octant: return p.x > 0 && p.y > 0 && p.z > 0
        case .half: return p.z > 0
        }
    }
}

// MARK: - Shapes

enum BioShapes {
    /// The direction at polar angle θ from +y and azimuth φ from +z toward +x.
    static func direction(_ theta: Double, _ phi: Double) -> BioVec {
        BioVec(sin(theta) * sin(phi), cos(theta), sin(theta) * cos(phi))
    }

    /// A star-shaped surface about the origin: the point at distance `radius(d)` along each direction d, on a grid of
    /// `rings` × `segments` (rings even and segments a multiple of 4 when cut, so the cut runs along grid lines). With
    /// `inner`, a shell: its inner wall at `inner(d)`, facing in. With `cut`, the wedge is left out and the cut faces
    /// capped — between the walls for a shell, to the centre for a solid.
    static func radial(rings: Int = 24, segments: Int = 48, cut: BioCut? = nil, inner: ((BioVec) -> Double)? = nil,
                       radius: @escaping (BioVec) -> Double) -> BioMesh {
        let nr = max(2, cut == nil ? rings : rings + rings % 2)
        let ns = max(3, cut == nil ? segments : (segments + 3) / 4 * 4)
        func point(_ r: (BioVec) -> Double, _ t: Double, _ p: Double) -> BioVec { direction(t, p) * r(direction(t, p)) }
        func normal(_ r: (BioVec) -> Double, _ t: Double, _ p: Double) -> BioVec {
            let e = 1e-4
            let dt = point(r, t + e, p) - point(r, t - e, p)
            let dp = point(r, t, p + e) - point(r, t, p - e)
            let n = BioV.cross(dt, dp)
            let pos = point(r, t, p)
            if BioV.length(n) < 1e-12 { return BioV.unit(pos.y >= 0 ? BioVec(0, 1, 0) : BioVec(0, -1, 0)) }
            return BioV.dot(n, pos) >= 0 ? n : -n
        }
        func surface(_ r: @escaping (BioVec) -> Double, inward: Bool) -> BioMesh {
            var m = BioMesh()
            for i in 0...nr {
                let t = Double.pi * Double(i) / Double(nr)
                for j in 0..<ns {
                    let p = 2 * Double.pi * Double(j) / Double(ns)
                    let tt = i == 0 ? 1e-6 : (i == nr ? Double.pi - 1e-6 : t)
                    m.vertex(point(r, t, p), normal(r, tt, p))
                }
            }
            for i in 0..<nr {
                for j in 0..<ns {
                    let tm = Double.pi * (Double(i) + 0.5) / Double(nr), pm = 2 * Double.pi * (Double(j) + 0.5) / Double(ns)
                    if let c = cut, c.removes(direction(tm, pm)) { continue }
                    let a = UInt32(i * ns + j), d = UInt32(i * ns + (j + 1) % ns)
                    let b = UInt32((i + 1) * ns + j), cc = UInt32((i + 1) * ns + (j + 1) % ns)
                    // (a pole's row is one point: its quads are triangles)
                    if i < nr - 1 { m.triangle(a, b, cc) }
                    if i > 0 { m.triangle(a, cc, d) }
                }
            }
            return inward ? m.flipped() : m
        }
        var mesh = surface(radius, inward: false)
        if let inner { mesh.append(surface(inner, inward: true)) }
        guard let cut else { return mesh }
        // (the caps: along each edge of the cut, from the outer wall to the inner, or to the centre)
        var edges: [(points: [(Double, Double)], facing: BioVec)] = []
        let half = nr / 2, quarter = ns / 4
        switch cut {
        case .octant:
            edges.append(((0...half).map { (Double.pi * Double($0) / Double(nr), 0) }, BioVec(1, 0, 0)))
            edges.append(((0...half).map { (Double.pi * Double($0) / Double(nr), Double.pi / 2) }, BioVec(0, 0, 1)))
            edges.append(((0...quarter).map { (Double.pi / 2, 2 * Double.pi * Double($0) / Double(ns)) }, BioVec(0, 1, 0)))
        case .half:
            edges.append(((0...nr).map { (Double.pi * Double($0) / Double(nr), Double.pi / 2) }, BioVec(0, 0, 1)))
            edges.append(((0...nr).map { (Double.pi * Double($0) / Double(nr), 3 * Double.pi / 2) }, BioVec(0, 0, 1)))
        }
        for e in edges {
            for k in 0..<(e.points.count - 1) {
                let (t0, p0) = e.points[k], (t1, p1) = e.points[k + 1]
                let o0 = point(radius, t0, p0), o1 = point(radius, t1, p1)
                if let inner {
                    mesh.flatQuad(o0, o1, point(inner, t1, p1), point(inner, t0, p0), facing: e.facing)
                } else {
                    mesh.flatTriangle(o0, o1, BioVec(0, 0, 0), facing: e.facing)
                }
            }
        }
        return mesh
    }

    // MARK: Radius functions (star-shaped about the origin)

    /// A sphere's.
    static func sphere(_ r: Double) -> (BioVec) -> Double { { _ in r } }

    /// An ellipsoid's, semi-axes a, b, c along x, y, z.
    static func ellipsoid(_ a: Double, _ b: Double, _ c: Double) -> (BioVec) -> Double {
        { d in 1 / (d.x * d.x / (a * a) + d.y * d.y / (b * b) + d.z * d.z / (c * c)).squareRoot() }
    }

    /// A capsule's along y: radius r, its cylinder `length` long (so length + 2r overall).
    static func capsuleRadius(_ r: Double, length: Double) -> (BioVec) -> Double {
        let h = length / 2
        return { d in
            let side = (d.x * d.x + d.z * d.z).squareRoot()
            if side > 1e-9 {
                let t = r / side
                if abs(t * d.y) <= h { return t }
            }
            let dc = abs(d.y) * h
            return dc + (dc * dc - h * h + r * r).squareRoot()
        }
    }

    /// A superellipsoid's: semi-axes a, b, c; e1 shapes it north–south, e2 east–west (1 and 1 is an ellipsoid; near 0
    /// a box).
    static func superellipsoid(_ a: Double, _ b: Double, _ c: Double, e1: Double, e2: Double) -> (BioVec) -> Double {
        { d in
            let xz = pow(pow(abs(d.x / a), 2 / e2) + pow(abs(d.z / c), 2 / e2), e2 / e1)
            let f = xz + pow(abs(d.y / b), 2 / e1)
            return f > 0 ? pow(f, -e1 / 2) : 0
        }
    }

    /// A rounded box's: half extents and corner radius (by its signed distance; `level` < 0 is a wall that far in).
    static func roundedBoxRadius(_ half: BioVec, corner: Double, level: Double = 0) -> (BioVec) -> Double {
        let rc = min(corner, half.x, half.y, half.z)
        let inner = BioVec(half.x - rc, half.y - rc, half.z - rc)
        let sdf: (BioVec) -> Double = { p in
            let q = BioVec(abs(p.x) - inner.x, abs(p.y) - inner.y, abs(p.z) - inner.z)
            let outside = BioV.length(BioVec(max(q.x, 0), max(q.y, 0), max(q.z, 0)))
            return outside + min(max(q.x, max(q.y, q.z)), 0) - rc
        }
        return byDistance(sdf, level: level, reach: BioV.length(half) * 1.5)
    }

    /// A lens's (two spherical caps): `diameter` across in x and z, `thickness` in y.
    static func lensRadius(diameter: Double, thickness: Double, level: Double = 0) -> (BioVec) -> Double {
        let c = (diameter * diameter - thickness * thickness) / (4 * thickness)
        let rho = c + thickness / 2
        let sdf: (BioVec) -> Double = { p in
            max(BioV.length(p - BioVec(0, -c, 0)) - rho, BioV.length(p - BioVec(0, c, 0)) - rho)
        }
        return byDistance(sdf, level: level, reach: diameter)
    }

    /// The radius along each direction where a signed distance reaches `level`, found by bisection (48 halvings).
    static func byDistance(_ sdf: @escaping (BioVec) -> Double, level: Double = 0, reach: Double) -> (BioVec) -> Double {
        { d in
            var lo = 0.0, hi = reach
            for _ in 0..<48 {
                let mid = (lo + hi) / 2
                if sdf(d * mid) < level { lo = mid } else { hi = mid }
            }
            return (lo + hi) / 2
        }
    }

    // MARK: Solids

    /// A UV sphere.
    static func sphere(radius r: Double, rings: Int = 16, segments: Int = 32) -> BioMesh {
        radial(rings: rings, segments: segments, radius: sphere(r))
    }

    /// A spheroid (or any ellipsoid), semi-axes along x, y, z.
    static func spheroid(_ a: Double, _ b: Double, _ c: Double, rings: Int = 20, segments: Int = 40, cut: BioCut? = nil) -> BioMesh {
        radial(rings: rings, segments: segments, cut: cut, radius: ellipsoid(a, b, c))
    }

    /// A capsule along y: radius r, cylinder `length` (24 × 12 by default).
    static func capsule(radius r: Double, length: Double, rings: Int = 12, segments: Int = 24, cut: BioCut? = nil) -> BioMesh {
        radial(rings: rings, segments: segments, cut: cut, radius: capsuleRadius(r, length: length))
    }

    static func roundedBox(_ half: BioVec, corner: Double, rings: Int = 24, segments: Int = 48, cut: BioCut? = nil) -> BioMesh {
        radial(rings: rings, segments: segments, cut: cut, radius: roundedBoxRadius(half, corner: corner))
    }

    static func lens(diameter: Double, thickness: Double, rings: Int = 16, segments: Int = 32, cut: BioCut? = nil) -> BioMesh {
        radial(rings: rings, segments: segments, cut: cut, radius: lensRadius(diameter: diameter, thickness: thickness))
    }

    static func superellipsoid(_ a: Double, _ b: Double, _ c: Double, e1: Double, e2: Double, rings: Int = 20, segments: Int = 40) -> BioMesh {
        radial(rings: rings, segments: segments, radius: superellipsoid(a, b, c, e1: e1, e2: e2))
    }

    /// An icosphere: an icosahedron's faces split `subdivisions` times (1: 80 triangles; 2: 320), for the many small
    /// spheres (ribosomes, vesicles, atoms in a big model).
    static func icosphere(radius r: Double, subdivisions: Int = 1) -> BioMesh {
        let t = (1 + 5.0.squareRoot()) / 2
        var v: [BioVec] = [BioVec(-1, t, 0), BioVec(1, t, 0), BioVec(-1, -t, 0), BioVec(1, -t, 0),
                           BioVec(0, -1, t), BioVec(0, 1, t), BioVec(0, -1, -t), BioVec(0, 1, -t),
                           BioVec(t, 0, -1), BioVec(t, 0, 1), BioVec(-t, 0, -1), BioVec(-t, 0, 1)].map { BioV.unit($0) }
        var f: [(Int, Int, Int)] = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4),
                                    (11, 10, 2), (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8),
                                    (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
        for _ in 0..<max(0, subdivisions) {
            var mid: [Int: Int] = [:]
            func middle(_ a: Int, _ b: Int) -> Int {
                let key = min(a, b) * 100_000 + max(a, b)
                if let m = mid[key] { return m }
                v.append(BioV.unit((v[a] + v[b]) / 2))
                mid[key] = v.count - 1
                return v.count - 1
            }
            var next: [(Int, Int, Int)] = []
            for (a, b, c) in f {
                let ab = middle(a, b), bc = middle(b, c), ca = middle(c, a)
                next += [(a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca)]
            }
            f = next
        }
        var m = BioMesh()
        for p in v { m.vertex(p * r, p) }
        for (a, b, c) in f {
            // (wound to face out)
            let n = BioV.cross(v[b] - v[a], v[c] - v[a])
            if BioV.dot(n, v[a]) >= 0 { m.triangle(UInt32(a), UInt32(b), UInt32(c)) } else { m.triangle(UInt32(a), UInt32(c), UInt32(b)) }
        }
        return m
    }

    /// A cylinder from a to b (a bond, a centriole's tube), its ends capped if asked.
    static func cylinder(from a: BioVec, to b: BioVec, radius r: Double, segments: Int = 16, caps: Bool = true) -> BioMesh {
        let len = BioV.distance(a, b)
        guard len > 1e-9 else { return BioMesh() }
        var m = BioMesh()
        let n = max(3, segments)
        for k in 0...1 {
            for j in 0..<n {
                let p = 2 * Double.pi * Double(j) / Double(n)
                let d = BioVec(sin(p), 0, cos(p))
                m.vertex(BioVec(d.x * r, Double(k) * len, d.z * r), d)
            }
        }
        for j in 0..<n {
            let a0 = UInt32(j), a1 = UInt32((j + 1) % n), b0 = UInt32(n + j), b1 = UInt32(n + (j + 1) % n)
            m.triangle(a0, a1, b1)
            m.triangle(a0, b1, b0)
        }
        if caps {
            for k in 0...1 {
                let y = Double(k) * len
                let facing = BioVec(0, k == 0 ? -1 : 1, 0)
                for j in 0..<n {
                    let p0 = 2 * Double.pi * Double(j) / Double(n), p1 = 2 * Double.pi * Double(j + 1) / Double(n)
                    m.flatTriangle(BioVec(0, y, 0), BioVec(sin(p0) * r, y, cos(p0) * r), BioVec(sin(p1) * r, y, cos(p1) * r), facing: facing)
                }
            }
        }
        return m.transformed(BioFrame.along(b - a, at: a))
    }

    /// A torus about the y axis: the ring's radius and the tube's.
    static func torus(ring R: Double, pipe r: Double, ringSegments: Int = 32, pipeSegments: Int = 12) -> BioMesh {
        var m = BioMesh()
        let nu = max(3, ringSegments), nv = max(3, pipeSegments)
        for i in 0..<nu {
            let u = 2 * Double.pi * Double(i) / Double(nu)
            let c = BioVec(sin(u) * R, 0, cos(u) * R)
            let out = BioVec(sin(u), 0, cos(u))
            for j in 0..<nv {
                let v = 2 * Double.pi * Double(j) / Double(nv)
                let n = out * cos(v) + BioVec(0, 1, 0) * sin(v)
                m.vertex(c + n * r, n)
            }
        }
        for i in 0..<nu {
            for j in 0..<nv {
                let a = UInt32(i * nv + j), b = UInt32(((i + 1) % nu) * nv + j)
                let c = UInt32(((i + 1) % nu) * nv + (j + 1) % nv), d = UInt32(i * nv + (j + 1) % nv)
                m.triangle(a, b, c)
                m.triangle(a, c, d)
            }
        }
        return m
    }

    /// A curved patch of a spherical shell about the origin (the rough ER's sheets, §5.1.5): polar angles θ from +y,
    /// azimuths φ from +z toward +x, its mid-surface at `radius(θ, φ)`, `thickness` thick, its two walls joined at the
    /// rim; the thickness fades to a fifth over the last `rimFade` of each range (a rounded rim).
    static func shellPatch(theta: ClosedRange<Double>, phi: ClosedRange<Double>, thickness: Double, rimFade: Double = 0.05,
                           rows: Int = 16, columns: Int = 32, radius: (Double, Double) -> Double) -> BioMesh {
        let nr = max(1, rows), nc = max(1, columns)
        func fade(_ u: Double) -> Double {
            let e = min(u, 1 - u)
            return rimFade > 0 && e < rimFade ? 0.2 + 0.8 * (e / rimFade) : 1
        }
        func at(_ i: Int, _ j: Int, _ side: Double) -> BioVec {
            let u = Double(i) / Double(nr), v = Double(j) / Double(nc)
            let t = theta.lowerBound + (theta.upperBound - theta.lowerBound) * u
            let p = phi.lowerBound + (phi.upperBound - phi.lowerBound) * v
            let r = radius(t, p) + side * thickness / 2 * fade(u) * fade(v)
            return direction(t, p) * r
        }
        var m = BioMesh()
        for (side, outward) in [(1.0, true), (-1.0, false)] {
            var grid = BioMesh()
            for i in 0...nr { for j in 0...nc { grid.vertex(at(i, j, side), BioVec(0, 0, 0)) } }
            for i in 0..<nr {
                for j in 0..<nc {
                    let a = UInt32(i * (nc + 1) + j), d = UInt32(i * (nc + 1) + j + 1)
                    let b = UInt32((i + 1) * (nc + 1) + j), c = UInt32((i + 1) * (nc + 1) + j + 1)
                    grid.triangle(a, b, c)
                    grid.triangle(a, c, d)
                }
            }
            grid = smoothNormals(grid)
            // (the grid as built faces out — away from the origin; the inner wall turned to face in)
            if BioV.dot(BioVec(Double(grid.normals[0].x), Double(grid.normals[0].y), Double(grid.normals[0].z)), at(0, 0, side)) < 0 {
                grid = grid.flipped()
            }
            m.append(outward ? grid : grid.flipped())
        }
        // (the rim: the two walls joined along the four edges, facing out of the patch)
        let centre = at(nr / 2, nc / 2, 0)
        func rim(_ pts: [(Int, Int)]) {
            for k in 0..<(pts.count - 1) {
                let (i0, j0) = pts[k], (i1, j1) = pts[k + 1]
                let o0 = at(i0, j0, 1), o1 = at(i1, j1, 1), n0 = at(i0, j0, -1), n1 = at(i1, j1, -1)
                let mid = (o0 + o1 + n0 + n1) / 4
                let along = o1 - o0, across = o0 - n0
                var facing = BioV.cross(along, across)
                if BioV.dot(facing, mid - centre) < 0 { facing = -facing }
                m.flatQuad(o0, o1, n1, n0, facing: facing)
            }
        }
        rim((0...nc).map { (0, $0) })
        rim((0...nc).map { (nr, $0) })
        rim((0...nr).map { ($0, 0) })
        rim((0...nr).map { ($0, nc) })
        return m
    }

    /// A seeded blob's radius function (§3.4): a base shape's radius times 1 + Σ aₖ·sin(fₖ·(u·dₖ) + φₖ).
    static func blob(_ base: @escaping (BioVec) -> Double, _ blob: BioBlob) -> (BioVec) -> Double {
        { d in base(d) * blob.factor(d) }
    }

    /// Normals made smooth again from the triangles round each vertex (area-weighted), for meshes whose normals were
    /// not worked out as they were made.
    static func smoothNormals(_ mesh: BioMesh) -> BioMesh {
        var acc = [BioVec](repeating: BioVec(0, 0, 0), count: mesh.positions.count)
        func v(_ i: UInt32) -> BioVec { let p = mesh.positions[Int(i)]; return BioVec(Double(p.x), Double(p.y), Double(p.z)) }
        for t in stride(from: 0, to: mesh.indices.count - 2, by: 3) {
            let a = mesh.indices[t], b = mesh.indices[t + 1], c = mesh.indices[t + 2]
            let n = BioV.cross(v(b) - v(a), v(c) - v(a))
            acc[Int(a)] += n
            acc[Int(b)] += n
            acc[Int(c)] += n
        }
        var out = mesh
        out.normals = acc.map { let u = BioV.unit($0); return SIMD3<Float>(Float(u.x), Float(u.y), Float(u.z)) }
        return out
    }
}

/// A seeded blob's ripples (§3.4): `terms` sines of the direction, their total amplitude `amplitude` (≤ 0.12). Drawn
/// from SplitMix64 in this order, term by term: dₖ (`unitVector`, two draws), fₖ = 1.5…3.5 (one), φₖ = 0…2π (one),
/// a weight 0.5…1 (one); the weights are then scaled to add up to `amplitude`.
struct BioBlob {
    let terms: [(d: BioVec, f: Double, phase: Double, a: Double)]

    init(seed: UInt64, amplitude: Double, terms n: Int = 4) {
        var rng = BioRandom(seed: seed)
        var raw: [(BioVec, Double, Double, Double)] = []
        for _ in 0..<max(1, n) {
            let d = rng.unitVector()
            let f = rng.uniform(1.5, 3.5)
            let p = rng.uniform(0, 2 * Double.pi)
            let w = rng.uniform(0.5, 1)
            raw.append((d, f, p, w))
        }
        let total = raw.reduce(0) { $0 + $1.3 }
        terms = raw.map { (d: $0.0, f: $0.1, phase: $0.2, a: total > 0 ? $0.3 / total * amplitude : 0) }
    }

    /// 1 + Σ aₖ·sin(fₖ·(u·dₖ) + φₖ) for a unit direction u.
    func factor(_ u: BioVec) -> Double {
        1 + terms.reduce(0) { $0 + $1.a * sin($1.f * BioV.dot(u, $1.d) + $1.phase) }
    }
}

// MARK: - Along a path

enum BioPath {
    /// Points on a Catmull–Rom spline through `points` (uniform; the ends' tangents from their neighbours), `samples`
    /// to each span, the last point included; closed, it comes back to the first.
    static func catmullRom(_ points: [BioVec], samples: Int = 6, closed: Bool = false) -> [BioVec] {
        let n = points.count
        guard n >= 2 else { return points }
        func p(_ i: Int) -> BioVec {
            if closed { return points[((i % n) + n) % n] }
            if i < 0 { return points[0] * 2 - points[1] }
            if i >= n { return points[n - 1] * 2 - points[n - 2] }
            return points[i]
        }
        var out: [BioVec] = []
        let spans = closed ? n : n - 1
        let s = max(1, samples)
        for i in 0..<spans {
            let p0 = p(i - 1), p1 = p(i), p2 = p(i + 1), p3 = p(i + 2)
            for k in 0..<s {
                let t = Double(k) / Double(s), t2 = t * t, t3 = t2 * t
                let a = p1 * 2
                let b = (p2 - p0) * t
                let c = (p0 * 2 - p1 * 5 + p2 * 4 - p3) * t2
                let d = (p1 * 3 - p0 - p2 * 3 + p3) * t3
                out.append((a + b + c + d) * 0.5)
            }
        }
        out.append(closed ? points[0] : points[n - 1])
        return out
    }

    /// Tangents of a polyline (central differences).
    static func tangents(_ pts: [BioVec], closed: Bool = false) -> [BioVec] {
        let n = pts.count
        return (0..<n).map { i in
            let a = closed ? pts[(i - 1 + n) % n] : pts[max(i - 1, 0)]
            let b = closed ? pts[(i + 1) % n] : pts[min(i + 1, n - 1)]
            let t = BioV.unit(b - a)
            return BioV.length(t) > 0 ? t : BioVec(0, 1, 0)
        }
    }

    /// Rotation-minimising frames along a polyline (Wang et al.'s double reflection): each point's tangent and a normal
    /// that turns as little as it can. Closed, the twist left at the end is shared out along it.
    static func frames(_ pts: [BioVec], closed: Bool = false, start: BioVec? = nil) -> [(t: BioVec, r: BioVec)] {
        let n = pts.count
        guard n >= 2 else { return pts.map { _ in (BioVec(0, 1, 0), BioVec(1, 0, 0)) } }
        let ts = tangents(pts, closed: closed)
        var r0 = start ?? BioV.cross(ts[0], abs(ts[0].y) < 0.9 ? BioVec(0, 1, 0) : BioVec(1, 0, 0))
        r0 = BioV.unit(r0 - ts[0] * BioV.dot(r0, ts[0]))
        // (closed, the frame is carried on round to the first point again, to see how far it has twisted)
        let steps = closed ? n : n - 1
        var out: [(BioVec, BioVec)] = [(ts[0], r0)]
        for i in 0..<steps {
            let p0 = pts[i], p1 = pts[(i + 1) % n], t1 = ts[(i + 1) % n]
            let v1 = p1 - p0
            let c1 = BioV.dot(v1, v1)
            let (ti, ri) = out[i]
            guard c1 > 1e-18 else { out.append((t1, ri)); continue }
            let rl = ri - v1 * (2 / c1 * BioV.dot(v1, ri))
            let tl = ti - v1 * (2 / c1 * BioV.dot(v1, ti))
            let v2 = t1 - tl
            let c2 = BioV.dot(v2, v2)
            let r = c2 > 1e-18 ? rl - v2 * (2 / c2 * BioV.dot(v2, rl)) : rl
            out.append((t1, BioV.unit(r)))
        }
        if closed {
            // (the twist that would be left where the loop meets itself, shared out along it)
            let rn = out[n].1
            let angle = atan2(BioV.dot(BioV.cross(rn, r0), ts[0]), BioV.dot(rn, r0)) * 180 / .pi
            out = (0..<n).map { k in (out[k].0, BioV.rotate(out[k].1, axis: out[k].0, degrees: angle * Double(k) / Double(n))) }
        }
        return out.map { (t: $0.0, r: $0.1) }
    }

    /// A tube along a path (§3.4): a circle of `sides` swept along a Catmull–Rom spline through the points (or the
    /// points themselves, `smooth` off), radius `radius` (or per input point, `radii`), its ends capped if open.
    static func tube(_ points: [BioVec], radius: Double, radii: [Double]? = nil, sides: Int = 10, samples: Int = 6,
                     closed: Bool = false, smooth: Bool = true, caps: Bool = true) -> BioMesh {
        guard points.count >= 2 else { return BioMesh() }
        let path = smooth ? catmullRom(points, samples: samples, closed: closed) : (closed ? points + [points[0]] : points)
        let fr = frames(closed ? Array(path.dropLast()) : path, closed: closed)
        let frs = closed ? fr + [fr[0]] : fr
        func rad(_ k: Int) -> Double {
            guard let radii, radii.count == points.count else { return radius }
            let u = Double(k) / Double(max(path.count - 1, 1)) * Double(closed ? points.count : points.count - 1)
            let i = min(Int(u), radii.count - 1), j = closed ? (i + 1) % radii.count : min(i + 1, radii.count - 1)
            let f = u - Double(i)
            return radii[i] * (1 - f) + radii[j] * f
        }
        var m = BioMesh()
        let ns = max(3, sides)
        for (k, c) in path.enumerated() {
            let (t, r) = frs[k]
            let s = BioV.cross(t, r)
            for j in 0..<ns {
                let a = 2 * Double.pi * Double(j) / Double(ns)
                let n = r * cos(a) + s * sin(a)
                m.vertex(c + n * rad(k), n)
            }
        }
        for k in 0..<(path.count - 1) {
            for j in 0..<ns {
                let a = UInt32(k * ns + j), b = UInt32((k + 1) * ns + j)
                let c = UInt32((k + 1) * ns + (j + 1) % ns), d = UInt32(k * ns + (j + 1) % ns)
                // (r → s is counter-clockwise about t, so a–d–c faces out)
                m.triangle(a, d, c)
                m.triangle(a, c, b)
            }
        }
        if caps && !closed {
            for (k, sign) in [(0, -1.0), (path.count - 1, 1.0)] {
                let (t, r) = frs[k]
                let s = BioV.cross(t, r)
                for j in 0..<ns {
                    let a0 = 2 * Double.pi * Double(j) / Double(ns), a1 = 2 * Double.pi * Double(j + 1) / Double(ns)
                    m.flatTriangle(path[k], path[k] + (r * cos(a0) + s * sin(a0)) * rad(k), path[k] + (r * cos(a1) + s * sin(a1)) * rad(k), facing: t * sign)
                }
            }
        }
        return m
    }

    /// A ribbon along a path (§3.4, the protein cartoon's helices and strands): a flat band `width` wide and
    /// `thickness` thick (an ellipse across, if `elliptical`), its broad face turned by `guides` (one side direction per
    /// input point, e.g. from CA toward the helix axis) or by rotation-minimising frames; `arrow` makes the band widen
    /// to its width at that input point and narrow to a point at the end (a β-strand's arrowhead).
    static func ribbon(_ points: [BioVec], guides: [BioVec]? = nil, width: Double, thickness: Double, samples: Int = 6,
                       arrow: (from: Int, width: Double)? = nil, elliptical: Bool = false) -> BioMesh {
        guard points.count >= 2 else { return BioMesh() }
        let path = catmullRom(points, samples: samples)
        let ts = tangents(path)
        let rmf = frames(path)
        let spanSamples = max(1, samples)
        func side(_ k: Int) -> BioVec {
            var u = rmf[k].r
            if let guides, guides.count == points.count {
                let i = min(k / spanSamples, guides.count - 1), j = min(i + 1, guides.count - 1)
                let f = Double(k % spanSamples) / Double(spanSamples)
                u = guides[i] * (1 - f) + guides[j] * f
            }
            let t = ts[k]
            let o = u - t * BioV.dot(u, t)
            return BioV.length(o) > 1e-9 ? BioV.unit(o) : rmf[k].r
        }
        func halfWidth(_ k: Int) -> Double {
            guard let arrow else { return width / 2 }
            let startK = arrow.from * spanSamples
            guard k >= startK else { return width / 2 }
            let span = Double(max(path.count - 1 - startK, 1))
            return arrow.width / 2 * (1 - Double(k - startK) / span)
        }
        var m = BioMesh()
        if elliptical {
            let ns = 12
            for k in 0..<path.count {
                let u = side(k), v = BioV.cross(ts[k], u)
                let hw = halfWidth(k), ht = thickness / 2
                for j in 0..<ns {
                    let a = 2 * Double.pi * Double(j) / Double(ns)
                    let p = path[k] + u * (cos(a) * hw) + v * (sin(a) * ht)
                    let n = u * (cos(a) / max(hw, 1e-6)) + v * (sin(a) / max(ht, 1e-6))
                    m.vertex(p, n)
                }
            }
            for k in 0..<(path.count - 1) {
                for j in 0..<ns {
                    let a = UInt32(k * ns + j), b = UInt32((k + 1) * ns + j)
                    let c = UInt32((k + 1) * ns + (j + 1) % ns), d = UInt32(k * ns + (j + 1) % ns)
                    m.triangle(a, d, c)
                    m.triangle(a, c, b)
                }
            }
            return m
        }
        // (a flat band: four faces, each with its own normals, and the two ends closed)
        var corners: [[BioVec]] = []
        for k in 0..<path.count {
            let u = side(k), v = BioV.cross(ts[k], u)
            let hw = halfWidth(k), ht = thickness / 2
            corners.append([path[k] + u * hw + v * ht, path[k] - u * hw + v * ht, path[k] - u * hw - v * ht, path[k] + u * hw - v * ht])
        }
        for face in 0..<4 {
            let i0 = face, i1 = (face + 1) % 4
            for k in 0..<(path.count - 1) {
                let a = corners[k][i0], b = corners[k][i1], c = corners[k + 1][i1], d = corners[k + 1][i0]
                let mid = (a + b + c + d) / 4
                m.flatQuad(a, b, c, d, facing: mid - (path[k] + path[k + 1]) / 2)
            }
        }
        let first = corners[0], last = corners[path.count - 1]
        m.flatQuad(first[0], first[1], first[2], first[3], facing: -ts[0])
        m.flatQuad(last[0], last[1], last[2], last[3], facing: ts[path.count - 1])
        return m
    }
}

// MARK: - Many objects in one geometry

/// Meshes of one material merged into one, with the range of triangles each object's mesh took (so a hit on a
/// triangle names its object): the batching of §3.4.
struct BioMeshBatch {
    private(set) var mesh = BioMesh()
    /// Each object's first triangle and triangle count, in order.
    private(set) var ranges: [(id: String, first: Int, count: Int)] = []

    mutating func add(_ m: BioMesh, id: String) {
        guard !m.isEmpty else { return }
        ranges.append((id, mesh.triangleCount, m.triangleCount))
        mesh.append(m)
    }

    /// The object a triangle (by index in the merged mesh) belongs to.
    func object(atTriangle t: Int) -> String? {
        var lo = 0, hi = ranges.count - 1
        while lo <= hi {
            let mid = (lo + hi) / 2
            let r = ranges[mid]
            if t < r.first { hi = mid - 1 } else if t >= r.first + r.count { lo = mid + 1 } else { return r.id }
        }
        return nil
    }
}

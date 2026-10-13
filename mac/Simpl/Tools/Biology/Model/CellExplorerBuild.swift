import Foundation

// The Cell Explorer's three cells built from their recipes (spec §4.1 Rendering, §5.1, §5.1.5), the same picture on
// every launch: each cell's SplitMix64 (seed 0xC311 animal, 0x91A7 plant, 0xBAC7 bacterium) places its fixed structures
// first — the "Golgi side" g, the nucleus, the rough ER's sheets, the Golgi stack and the centrosome (the plant: its
// central vacuole, then the nucleus against the wall) — then the free organelles in §5.1's table order, each candidate
// centre drawn uniformly in the cell (x, y, z: three draws), kept when it is far enough inside the membrane (and, in
// the plant, outside the vacuole) and its bounding sphere ×1.05 overlaps nothing placed, given up after 200 tries; then
// its turn (BioRandom.rotation: three draws) where its shape has one. The cutaway (§4.1 item 4) removes the octant
// x, y, z > 0 of the membrane, the nuclear envelope and the wall, capping each cut wall in a lighter tint; organelles
// whose centre is in it are left out and those crossing it drawn whole — but a mitochondrion or a chloroplast crossing it
// is cut lengthwise, its inside facing the camera. Teaching scale draws the tiny things larger (ribosomes 2–4×, vesicles
// 1.5×, filaments 3–7×) and fewer of the many; true scale draws them at their real sizes, with 20,000 ribosomes (the
// teaching ones and more from a second stream, so the rest of the cell is the same). Every mesh is then turned by the
// view's turn (a three-quarter view into the cut) and kept, per picture, in CEBuildCache.

/// What a cell is built with.
struct CEBuildOptions: Hashable, Sendable {
    var kind: CEKind
    var trueScale = false
    var cutaway = true
    var gramPositive = false
}

/// A piece of an organelle as drawn: its mesh (in the scene's frame) and how it is shaded.
struct CEPiece: Sendable {
    var mesh: BioMesh
    var colour: BioHexPair
    var alpha: Double
    var roughness: Double
    var order: Int
}

/// One organelle in the cell (one mitochondrion, one ribosome, the membrane).
struct CEInstance: Sendable {
    let part: String
    let index: Int
    /// Its centre and bounding radius (the scene's frame).
    var centre: BioVec
    var radius: Double
    /// The point whose place decides how far it moves when the cell is exploded: its own centre, or the organelle it
    /// belongs to (a bound ribosome's sheet, a Golgi vesicle's stack, the nucleolus's nucleus).
    var explodeFrom: BioVec
    /// One of the cell's own shells (membrane, wall, capsule, the cytoplasm's cut faces): never moved, faded when
    /// exploded.
    var shell = false
    /// Fixed to the shells (the wall's channels, the bacterium's flagella and pili): never moved.
    var fixed = false
    var pieces: [CEPiece] = []
    /// Its frame in the cell's own coordinates (Look inside rebuilds it cut).
    var frame: BioFrame?

    var id: String { "organelle:\(part):\(index)" }
}

/// Where a part's label points: an instance, the point, and (for a convex shell) its outward normal.
struct CEAnchor: Sendable {
    let instance: Int
    let point: BioVec
    let normal: BioVec?
}

/// A built cell.
struct CEModel: Sendable {
    let options: CEBuildOptions
    var instances: [CEInstance] = []
    /// Instances placed (the model's count) and drawn (fewer with the cutaway) of each part.
    var placed: [String: Int] = [:]
    var shown: [String: Int] = [:]
    /// Counts of kinds within a part: "ribosome.free", "ribosome.er", "vesicle.golgi".
    var extra: [String: Int] = [:]
    /// Placements that ran out of tries (§4.1: the count can come out lower).
    var gaveUp: [String: Int] = [:]
    var anchors: [String: CEAnchor] = [:]
    /// For the explode slider (the geometric mean of the cell's semi-axes) and the camera.
    var cellRadius = 1.0
    var frameRadius = 1.0
    /// The cell's surface area (µm²) and volume (µm³), for SA:V.
    var area = 0.0
    var volume = 0.0
    /// The turn from the cell's frame to the scene's.
    var display = BioFrame.identity
    /// The camera's direction in the cell's frame (from the cell toward it).
    var view = BioVec(0, 0, 1)

    var triangles: Int { instances.reduce(0) { $0 + $1.pieces.reduce(0) { $0 + $1.mesh.triangleCount } } }

    /// The parts in this cell that have anything drawn.
    var partsShown: Set<String> { Set(instances.filter { !$0.pieces.isEmpty }.map(\.part)) }
}

/// Each picture's cell, built once (a few kept); a second ask while it is being built waits for that build.
final class CEBuildCache: @unchecked Sendable {
    static let shared = CEBuildCache()
    private let condition = NSCondition()
    private var models: [CEBuildOptions: CEModel] = [:]
    private var order: [CEBuildOptions] = []
    private var building: Set<CEBuildOptions> = []

    func model(_ o: CEBuildOptions) -> CEModel {
        condition.lock()
        while models[o] == nil && building.contains(o) { condition.wait() }
        if let m = models[o] {
            condition.unlock()
            return m
        }
        building.insert(o)
        condition.unlock()
        let m = CEBuild.build(o)
        condition.lock()
        models[o] = m
        order.removeAll { $0 == o }
        order.append(o)
        while order.count > 4 { models[order.removeFirst()] = nil }
        building.remove(o)
        condition.broadcast()
        condition.unlock()
        return m
    }
}

/// Bounding spheres placed so far, for the overlap test (×1.05): the large in a list, the small in a grid.
struct CEPlacer {
    private var big: [(c: BioVec, r: Double, blocksSER: Bool)] = []
    private var grid: [Int64: [(c: BioVec, r: Double)]] = [:]
    let cell: Double
    static let small = 0.3

    init(cell: Double) { self.cell = cell }

    private func key(_ i: Int, _ j: Int, _ k: Int) -> Int64 { (Int64(i + 4096) << 26) | (Int64(j + 4096) << 13) | Int64(k + 4096) }
    private func index(_ v: Double) -> Int { Int((v / cell).rounded(.down)) }

    mutating func add(_ c: BioVec, _ r: Double, blocksSER: Bool = true) {
        if r > CEPlacer.small {
            big.append((c, r, blocksSER))
        } else {
            grid[key(index(c.x), index(c.y), index(c.z)), default: []].append((c, r))
        }
    }

    /// Whether a ball would overlap anything placed (their radii summed ×1.05).
    func overlaps(_ c: BioVec, _ r: Double) -> Bool {
        for b in big where BioV.distance(b.c, c) < (b.r + r) * 1.05 { return true }
        let reach = (r + CEPlacer.small) * 1.05
        let n = Int((reach / cell).rounded(.up))
        let i0 = index(c.x), j0 = index(c.y), k0 = index(c.z)
        for i in (i0 - n)...(i0 + n) {
            for j in (j0 - n)...(j0 + n) {
                for k in (k0 - n)...(k0 + n) {
                    guard let list = grid[key(i, j, k)] else { continue }
                    for s in list where BioV.distance(s.c, c) < (s.r + r) * 1.05 { return true }
                }
            }
        }
        return false
    }

    /// Whether a point is within `gap` of an organelle that stops the smooth ER growing (§5.1.5).
    func nearSER(_ p: BioVec, gap: Double) -> Bool {
        for b in big where b.blocksSER && BioV.distance(b.c, p) < b.r + gap { return true }
        return false
    }
}

enum CEBuild {
    /// The view's turn (a three-quarter view into the cut): 32° about y (the right side toward the camera), then 22°
    /// about x (the top toward it).
    static let display = BioFrame.rotation(axis: BioVec(0, 1, 0), degrees: -32).then(BioFrame.rotation(axis: BioVec(1, 0, 0), degrees: 22))

    static func build(_ o: CEBuildOptions) -> CEModel {
        var b = CEBuilder(o)
        switch o.kind {
        case .animal: b.buildAnimal()
        case .plant: b.buildPlant()
        case .bacterium: b.buildBacterium()
        }
        #if DEBUG
        // (§4.1: a placement that ran out of tries leaves the count lower — said in debug builds)
        if !b.model.gaveUp.isEmpty { print("cell-explorer \(o.kind.rawValue): gave up placing \(b.model.gaveUp.sorted { $0.key < $1.key })") }
        #endif
        return b.finish()
    }
}

/// The cell being built (in the cell's own frame until `finish` turns it into the scene's).
struct CEBuilder {
    let o: CEBuildOptions
    var rng: BioRandom
    var placer: CEPlacer
    var model: CEModel
    let parts: [String: CEPart]
    /// The cell's inside (how far inside its membrane's inner wall a point is, for a margin) and its sampling box.
    var inside: (BioVec, Double) -> Bool = { _, _ in true }
    var outsideVacuole: (BioVec, Double) -> Bool = { _, _ in true }
    var box = BioVec(1, 1, 1)
    /// The membrane's inner wall (a radius function) and a signed distance for the cell's outer surface.
    var membraneInner: (BioVec) -> Double = { _ in 1 }
    var surface: (BioVec) -> Double = { BioV.length($0) - 1 }
    /// The organelles' margin from the membrane (and the plant's from the vacuole).
    var margin = 0.4
    var nucleusCentre = BioVec(0, 0, 0)
    var nucleusRadius = 0.0
    var cameraDirection: BioVec
    /// The plant's chloroplasts' frames: the smooth ER keeps off their lenses (not their bounding spheres, which would
    /// fill the thin layer of cytoplasm).
    var lenses: [BioFrame] = []

    /// Whether a point is within `gap` of a chloroplast's lens (5.0 × 2.4 µm, as an oblate spheroid).
    func nearLens(_ p: BioVec, gap: Double) -> Bool {
        lenses.contains { f in
            let q = p - f.origin
            let x = BioV.dot(q, f.x), y = BioV.dot(q, f.y), z = BioV.dot(q, f.z)
            let a = 2.5 + gap, c = 1.2 + gap
            return (x * x + z * z) / (a * a) + y * y / (c * c) < 1
        }
    }

    init(_ o: CEBuildOptions) {
        self.o = o
        rng = BioRandom(seed: o.kind.seed)
        placer = CEPlacer(cell: o.kind == .bacterium ? 0.1 : 0.5)
        model = CEModel(options: o)
        parts = Dictionary(uniqueKeysWithValues: CEParts.all(o.kind).map { ($0.id, $0) })
        let d = CEBuild.display
        // (the camera looks along −z in the scene: its direction in the cell's frame is the turn undone on +z)
        cameraDirection = BioVec(d.x.z, d.y.z, d.z.z)
        model.display = d
        model.view = cameraDirection
    }

    // MARK: Adding

    /// The colour for a piece's role in a part.
    func colour(_ part: String, _ role: CEShape.Role) -> BioHexPair {
        let base = parts[part]?.colour ?? BioHexPair("#8e8e93", "#aeaeb2")
        switch role {
        case .body: return base
        case .cap: return CEGeo.lighter(base)
        case .dark: return CEGeo.darker(base, 0.25)
        case .named(let n):
            if n == "matrix" { return CEGeo.lighter(base, 0.55) }
            return CEParts.colour(o.kind, part, n) ?? base
        case .colour(let c): return c
        }
    }

    /// An organelle added (its shape already placed in the cell's frame).
    @discardableResult
    mutating func add(_ part: String, _ shape: CEShape, centre: BioVec, radius: Double, explodeFrom: BioVec? = nil,
                      shell: Bool = false, fixed: Bool = false, frame: BioFrame? = nil, count: Bool = true) -> Int {
        let index = model.placed[part, default: 0]
        if count { model.placed[part, default: 0] += 1 }
        let partAlpha = parts[part]?.alpha ?? 1
        var inst = CEInstance(part: part, index: index, centre: centre, radius: radius, explodeFrom: explodeFrom ?? centre,
                              shell: shell, fixed: fixed, frame: frame)
        inst.pieces = shape.pieces.map { p in
            CEPiece(mesh: p.mesh, colour: colour(part, p.role), alpha: p.alpha ?? partAlpha, roughness: p.roughness, order: p.order)
        }
        if !inst.pieces.isEmpty && count { model.shown[part, default: 0] += 1 }
        model.instances.append(inst)
        return model.instances.count - 1
    }

    /// One counted but not drawn (its centre in the cutaway's wedge).
    mutating func skip(_ part: String) { model.placed[part, default: 0] += 1 }

    var cut: Bool { o.cutaway }
    func hidden(_ c: BioVec) -> Bool { cut && CEGeo.inWedge(c) }
    func crossing(_ c: BioVec, _ r: Double) -> Bool { cut && !CEGeo.inWedge(c) && CEGeo.wedgeDistance(c) < r }

    /// A point drawn uniformly in the sampling box (three draws: x, y, z).
    mutating func sample() -> BioVec {
        let x = rng.uniform(-box.x, box.x)
        let y = rng.uniform(-box.y, box.y)
        let z = rng.uniform(-box.z, box.z)
        return BioVec(x, y, z)
    }

    /// A free organelle's centre (§4.1 Rendering): up to 200 candidates; kept when its bounding sphere is `margin`
    /// inside the membrane (and outside the vacuole) and overlaps nothing placed. nil: given up.
    mutating func placeFree(_ part: String, radius r: Double, margin m: Double? = nil, accept: ((BioVec) -> Bool)? = nil) -> BioVec? {
        let gap = m ?? margin
        for _ in 0..<200 {
            let p = sample()
            guard inside(p, r + gap), outsideVacuole(p, r + gap) else { continue }
            if let accept, !accept(p) { continue }
            if placer.overlaps(p, r) { continue }
            placer.add(p, r)
            return p
        }
        model.gaveUp[part, default: 0] += 1
        return nil
    }

    var ribosomeRadius: Double {
        let teaching = o.kind == .bacterium ? 0.020 : 0.06
        return o.trueScale ? teaching / (parts["ribosome"]?.factor ?? 1) : teaching
    }

    /// A ribosome's mesh: an icosahedron (20 triangles) in teaching scale; at true size an octahedron, there being
    /// 20,000.
    var ribosomeMesh: BioMesh {
        o.trueScale ? CEGeo.octahedron(radius: ribosomeRadius) : BioShapes.icosphere(radius: ribosomeRadius, subdivisions: 0)
    }

    /// A filament's drawn radius: the teaching one, or the true one.
    func filament(_ part: String, teaching r: Double) -> Double { o.trueScale ? r / (parts[part]?.factor ?? 1) : r }

    // MARK: The cell's shells

    /// One of the cell's own shells (§5.1.5 Membranes and walls), cut by the octant with the cut faces lighter.
    mutating func cellShell(_ part: String, outer: @escaping (BioVec) -> Double, inner: @escaping (BioVec) -> Double,
                            rings: Int, segments: Int, order: Int, darkOuter: Bool = false, capAlpha: Double = 1) {
        let sh = CERecipes.shell(outer: outer, inner: inner, cut: cut, rings: rings, segments: segments)
        var s = CEShape()
        s.add(sh.outer, darkOuter ? .dark : .body, roughness: darkOuter ? 0.7 : 0.25, order: order)
        s.add(sh.inner, roughness: darkOuter ? 0.6 : 0.25, order: order)
        s.add(sh.caps, .cap, alpha: capAlpha, roughness: 0.6, order: order)
        if darkOuter {
            // (the wall's outer face only a little darker)
            s.pieces[0].role = .colour(CEGeo.darker(parts[part]?.colour ?? BioHexPair("#8fae5a", "#b3d17c"), 0.15))
        }
        add(part, s, centre: BioVec(0, 0, 0), radius: outer(BioVec(1, 0, 0)), shell: true)
    }

    // MARK: The nucleus (§5.1.5)

    /// The nucleus: its envelope with 110 pores, the nucleolus and the chromatin.
    mutating func nucleus(_ N: BioVec, _ R: Double) {
        nucleusCentre = N
        nucleusRadius = R
        let pores = CERecipes.poreDirections()
        let kept = pores.filter { !(cut && $0.x > 0 && $0.y > 0 && $0.z > 0) }
        add("nucleus", CERecipes.envelope(radius: R, pores: kept, cut: cut).placed(BioFrame(origin: N)), centre: N, radius: R)
        for d in pores {
            if cut && d.x > 0 && d.y > 0 && d.z > 0 { skip("pore"); continue }
            var s = CEShape()
            s.add(CERecipes.poreRing.transformed(BioFrame.along(d, at: N + d * (R - 0.031))), roughness: 0.4)
            add("pore", s, centre: N + d * R, radius: 0.09, explodeFrom: N)
        }
        // (the nucleolus: r 0.28·R, 0.25·R toward +x+y, a blob of amplitude 0.08)
        let nc = N + BioV.unit(BioVec(1, 1, 0)) * (0.25 * R)
        var s = CEShape()
        s.add(BioShapes.radial(rings: 16, segments: 32, radius: BioShapes.blob(BioShapes.sphere(0.28 * R),
                                                                                BioBlob(seed: o.kind.seed &+ 1, amplitude: 0.08))).translated(nc), roughness: 0.6)
        add("nucleolus", s, centre: nc, radius: 0.28 * R * 1.08, explodeFrom: N)
        chromatin(N, R, nucleolus: nc, nucleolusRadius: 0.28 * R)
        // (the nucleus and its rough ER, as one ball that free organelles keep out of)
        let sheets = o.kind == .animal ? 5 : 3
        placer.add(N, R + 0.35 + 0.42 * Double(sheets - 1) + 0.25, blocksSER: false)
    }

    /// Chromatin (§5.1.1): 12 random walks inside the nucleus (each from a point in it, 28 steps of 0.3 µm turning at most
    /// 60°, kept off the envelope and the nucleolus), drawn as tubes r 0.06 µm.
    mutating func chromatin(_ N: BioVec, _ R: Double, nucleolus nc: BioVec, nucleolusRadius rn: Double) {
        for _ in 0..<12 {
            var p = N + rng.inBall() * (R - 0.5)
            if BioV.distance(p, nc) < rn + 0.15 { p = N + BioV.unit(N - nc) * (R * 0.45) }
            var d = rng.unitVector()
            var pts = [p]
            for _ in 0..<28 {
                var nd = CEGeo.cone(d, 60, &rng)
                var q = p + nd * 0.3
                if BioV.distance(q, N) > R - 0.2 {
                    let n = BioV.unit(q - N)
                    nd = BioV.unit(nd - n * (2 * BioV.dot(nd, n)))
                    q = p + nd * 0.3
                }
                if BioV.distance(q, nc) < rn + 0.12 {
                    let n = BioV.unit(q - nc)
                    nd = BioV.unit(nd - n * (2 * BioV.dot(nd, n)))
                    q = p + nd * 0.3
                }
                if BioV.distance(q, N) > R - 0.15 || BioV.distance(q, nc) < rn + 0.08 { q = p }
                pts.append(q)
                p = q
                d = nd
            }
            var tube = BioPath.tube(pts, radius: 0.06, sides: 6, samples: 2)
            if cut { tube = CEGeo.filtered(tube) { !CEGeo.inWedge($0, N) } }
            var s = CEShape()
            s.add(tube, roughness: 0.6)
            let c = pts.reduce(BioVec(0, 0, 0), +) / Double(pts.count)
            add("chromatin", s, centre: c, radius: pts.map { BioV.distance($0, c) }.max() ?? 1, explodeFrom: N)
        }
    }

    // MARK: Rough ER (§5.1.5)

    /// Sheet i's mid-surface radius about the nucleus centre (the folds, and sheet 0's lip down to the envelope).
    func sheetRadius(_ i: Int, _ theta: Double, _ phi: Double) -> Double {
        let R = nucleusRadius
        let fold = 0.12 * sin(5 * phi + Double(i)) * sin(3 * theta)
        if i == 0 {
            // (sheet 0's inner edge touches the outer nuclear membrane: along its first 12% it comes down to it)
            let lip = CEGeo.smoothstep(0, 0.12, (theta - 0.25 * .pi) / (0.6 * .pi))
            return R + (0.35 + fold) * lip
        }
        return R + 0.35 + 0.42 * Double(i) + fold
    }

    static let sheetTheta = (0.25 * Double.pi)...(0.85 * Double.pi)
    static func sheetPhi(_ i: Int) -> ClosedRange<Double> { (0.37 * Double(i) * .pi)...(0.37 * Double(i) * .pi + 1.1 * .pi) }

    /// Whether a point of the rough ER is left out: in the Golgi's cone (25° about g), in the cutaway's wedges (the
    /// nucleus's and the cell's), or in the plant's vacuole.
    func rerRemoved(_ p: BioVec, golgi g: BioVec?) -> Bool {
        if let g, BioV.angle(p - nucleusCentre, g) < 25 { return true }
        if cut && (CEGeo.inWedge(p, nucleusCentre) || CEGeo.inWedge(p)) { return true }
        if o.kind == .plant && !outsideVacuole(p, 0.1) { return true }
        return false
    }

    /// The rough ER: `sheets` curved sheets round the nucleus (each a spherical-shell patch, 0.06 µm thick, its rim
    /// rounded), studded with ribosomes on its outer face: §5.1.5's 500 a sheet in the animal cell; in the plant, whose
    /// table gives 1,200 ribosomes in all, 100 a sheet (and 900 free).
    mutating func roughER(sheets: Int, golgi g: BioVec?) {
        let N = nucleusCentre
        let perSheet = o.kind == .animal ? 500 : 100
        for i in 0..<sheets {
            let phi = CEBuilder.sheetPhi(i)
            var m = BioShapes.shellPatch(theta: CEBuilder.sheetTheta, phi: phi, thickness: 0.06, rows: 20, columns: 44) { t, p in
                self.sheetRadius(i, t, p)
            }.translated(N)
            m = CEGeo.filtered(m) { !rerRemoved($0, golgi: g) }
            var s = CEShape()
            s.add(m, roughness: 0.45)
            let midT = (CEBuilder.sheetTheta.lowerBound + CEBuilder.sheetTheta.upperBound) / 2
            let midP = (phi.lowerBound + phi.upperBound) / 2
            let centre = N + BioShapes.direction(midT, midP) * (sheetRadius(i, midT, midP) * 0.6)
            add("rer", s, centre: centre, radius: nucleusRadius + 2.5)
            var stream = rng
            boundRibosomes(sheet: i, count: perSheet, centre: centre, golgi: g, rng: &stream)
            rng = stream
            if o.trueScale {
                // (true scale: the sheets' share of the 20,000, from a second stream so the rest of the cell is the same)
                var extra = BioRandom(seed: o.kind.seed ^ 0x5CA1_E000 &+ UInt64(i))
                let share = (o.kind == .animal ? 12_500 : 5_000) / sheets
                boundRibosomes(sheet: i, count: share - perSheet, centre: centre, golgi: g, rng: &extra)
            }
        }
    }

    /// A sheet's ribosomes: at seeded points on its outer face (two draws each, θ then φ), 0.04 µm out (scaled with the
    /// ribosome); a point where the sheet is not (the Golgi's cone, the plant's vacuole) drawn again; one in the
    /// cutaway's wedge counted but not drawn.
    mutating func boundRibosomes(sheet i: Int, count: Int, centre: BioVec, golgi g: BioVec?, rng r: inout BioRandom) {
        let N = nucleusCentre, phi = CEBuilder.sheetPhi(i), theta = CEBuilder.sheetTheta
        let offset = 0.03 + ribosomeRadius * (0.04 / 0.06)
        let mesh = ribosomeMesh
        var placedHere = 0, tries = 0
        while placedHere < count && tries < count * 20 {
            tries += 1
            let t = theta.lowerBound + (theta.upperBound - theta.lowerBound) * r.uniform()
            let p = phi.lowerBound + (phi.upperBound - phi.lowerBound) * r.uniform()
            // (decided on the sheet itself, so the draws are the same at either scale)
            let onSheet = N + BioShapes.direction(t, p) * sheetRadius(i, t, p)
            let pos = N + BioShapes.direction(t, p) * (sheetRadius(i, t, p) + offset)
            if let g, BioV.angle(onSheet - N, g) < 25 { continue }
            if o.kind == .plant && !outsideVacuole(onSheet, 0.1) { continue }
            placedHere += 1
            model.extra["ribosome.er", default: 0] += 1
            if rerRemoved(onSheet, golgi: nil) { skip("ribosome"); continue }
            var sh = CEShape()
            sh.add(mesh.translated(pos), roughness: 0.5)
            add("ribosome", sh, centre: pos, radius: ribosomeRadius, explodeFrom: centre)
        }
    }

    // MARK: Smooth ER (§5.1.5)

    /// From 6 seeded points on the outer sheet's rim, branching tubes: segments 0.35 µm long turning up to 35°, a 15%
    /// chance of a branch (turned 60°), 160 segments in all, a tip stopping within 0.6 µm of the membrane or another
    /// organelle (and, in the plant, 0.3 µm of the vacuole). Tubes r 0.05 µm, their joints smoothed by spheres.
    mutating func smoothER(sheets: Int, golgi g: BioVec?) {
        let N = nucleusCentre, i = sheets - 1
        let theta = CEBuilder.sheetTheta, phi = CEBuilder.sheetPhi(i)
        var tips: [(p: BioVec, d: BioVec, tree: Int)] = []
        var tries = 0
        while tips.count < 6 && tries < 400 {
            tries += 1
            let t = 4 * rng.uniform()
            let f = t - t.rounded(.down)
            let (th, ph): (Double, Double)
            switch Int(t) {
            case 0: (th, ph) = (theta.lowerBound, phi.lowerBound + f * (phi.upperBound - phi.lowerBound))
            case 1: (th, ph) = (theta.upperBound, phi.lowerBound + f * (phi.upperBound - phi.lowerBound))
            case 2: (th, ph) = (theta.lowerBound + f * (theta.upperBound - theta.lowerBound), phi.lowerBound)
            default: (th, ph) = (theta.lowerBound + f * (theta.upperBound - theta.lowerBound), phi.upperBound)
            }
            let p = N + BioShapes.direction(th, ph) * sheetRadius(i, th, ph)
            if let g, BioV.angle(p - N, g) < 25 { continue }
            // (a seed only where the sheet is: not in the plant's vacuole)
            if !outsideVacuole(p, 0.3) || !inside(p, 0.3) { continue }
            tips.append((p, BioV.unit(p - N), tips.count))
        }
        var segments: [[(a: BioVec, b: BioVec)]] = Array(repeating: [], count: tips.count)
        var total = 0
        while total < 160 && !tips.isEmpty {
            var next: [(p: BioVec, d: BioVec, tree: Int)] = []
            for tip in tips {
                guard total < 160 else { break }
                let d = CEGeo.cone(tip.d, 35, &rng)
                let end = tip.p + d * 0.35
                // (a tip stops near the membrane, another organelle or the vacuole)
                if !inside(end, 0.6) || placer.nearSER(end, gap: 0.6) || !outsideVacuole(end, 0.3) || nearLens(end, gap: 0.3) { continue }
                segments[tip.tree].append((tip.p, end))
                placer.add((tip.p + end) / 2, 0.2)
                total += 1
                next.append((end, d, tip.tree))
                if rng.uniform() < 0.15 {
                    next.append((end, CEGeo.turned(d, 60, &rng), tip.tree))
                }
            }
            tips = next
        }
        let r = 0.05
        let joint = BioShapes.icosphere(radius: r, subdivisions: 0)
        for tree in segments where !tree.isEmpty {
            var m = BioMesh()
            var mids: [BioVec] = []
            for seg in tree {
                let mid = (seg.a + seg.b) / 2
                if hidden(mid) { continue }
                mids.append(mid)
                m.append(BioShapes.cylinder(from: seg.a, to: seg.b, radius: r, segments: 8, caps: false))
                m.append(joint.translated(seg.a))
                m.append(joint.translated(seg.b))
            }
            var s = CEShape()
            s.add(m, roughness: 0.45)
            let c = mids.isEmpty ? tree[0].a : mids.reduce(BioVec(0, 0, 0), +) / Double(mids.count)
            add("ser", s, centre: c, radius: mids.map { BioV.distance($0, c) }.max() ?? 0.5)
        }
        model.extra["ser.segments"] = total
    }

    // MARK: Golgi (§5.1.5)

    /// The animal's Golgi stack at G, its axis a (cis face toward the nucleus), with its 24 vesicles (60% on the
    /// trans side, the rest round the rims).
    mutating func golgiStack(at G: BioVec, axis a: BioVec, side: BioVec) {
        let f = BioFrame(origin: G, x: side, y: a, z: BioV.cross(side, a))
        add("golgi", CERecipes.golgi(count: 5, long: 1.6, short: 0.9, thickness: 0.07, spacing: 0.14).placed(f), centre: G, radius: 1.0,
            frame: f)
        placer.add(G + a * 0.1, 1.05)
        for k in 0..<24 {
            let r = rng.uniform(0.06, 0.09)
            var local: BioVec
            if k < 14 {
                let x = rng.uniform(-0.65, 0.65), z = rng.uniform(-0.35, 0.35)
                local = BioVec(x, 2 * 0.14 + (x * x + z * z) / 4.4 + 0.063 + r + rng.uniform(0.02, 0.2), z)
            } else {
                let psi = rng.uniform(0, 2 * .pi)
                let j = rng.int(below: 5)
                let s = 1 - 0.08 * abs(Double(j) - 2)
                let c = cos(psi), sn = sin(psi)
                let x = 0.8 * s * (c < 0 ? -1 : 1) * abs(c).squareRoot() * 1.12 + c * r
                let z = 0.45 * s * (sn < 0 ? -1 : 1) * abs(sn).squareRoot() * 1.12 + sn * r
                local = BioVec(x, (Double(j) - 2) * 0.14 + (x * x + z * z) / 4.4, z)
            }
            vesicle(at: f.apply(local), radius: r, explodeFrom: G, golgi: true)
        }
    }

    /// A vesicle (an icosphere; ×1.5 in teaching scale).
    mutating func vesicle(at p: BioVec, radius r: Double, explodeFrom: BioVec? = nil, golgi: Bool = false) {
        model.extra[golgi ? "vesicle.golgi" : "vesicle.free", default: 0] += 1
        if hidden(p) { skip("vesicle"); return }
        let drawn = o.trueScale ? r / 1.5 : r
        var s = CEShape()
        s.add(BioShapes.icosphere(radius: drawn, subdivisions: 1).translated(p), roughness: 0.4)
        add("vesicle", s, centre: p, radius: drawn, explodeFrom: explodeFrom)
    }

    /// The plant's Golgi: 6 small stacks (dictyosomes) of 4 cisternae, 0.8 µm across, placed as free organelles, each
    /// with 3 vesicles on its trans face.
    mutating func dictyosomes() {
        for _ in 0..<6 {
            guard let p = placeFree("golgi", radius: 0.6) else { continue }
            let rot = rng.rotation()
            let f = CEGeo.frame(rot, at: p)
            var ves: [(BioVec, Double)] = []
            for _ in 0..<3 {
                let r = rng.uniform(0.05, 0.07)
                ves.append((f.apply(BioVec(rng.uniform(-0.3, 0.3), 0.2 + r + rng.uniform(0.0, 0.1), rng.uniform(-0.15, 0.15))), r))
            }
            if hidden(p) { skip("golgi") } else {
                add("golgi", CERecipes.golgi(count: 4, long: 0.8, short: 0.45, thickness: 0.06, spacing: 0.11, rings: 5, segments: 20).placed(f),
                    centre: p, radius: 0.5, frame: f)
            }
            for (v, r) in ves { vesicle(at: v, radius: r, explodeFrom: p, golgi: true) }
        }
    }

    // MARK: Centrosome (§5.1.5)

    mutating func centrosome(at C: BioVec) -> BioVec {
        let rot = rng.rotation()
        let a1 = BioV.rotate(BioVec(0, 1, 0), axis: rot.axis, degrees: rot.degrees)
        let a2 = BioV.rotate(BioVec(1, 0, 0), axis: rot.axis, degrees: rot.degrees)
        let M = C + a2 * 0.175
        var s = CEShape()
        s.add(CERecipes.centriole.transformed(BioFrame.along(a1, at: C)), roughness: 0.45)
        s.add(CERecipes.centriole.transformed(BioFrame.along(a2, at: C + a2 * 0.35)), roughness: 0.45)
        // (the pericentriolar cloud: 80 small translucent spheres r 0.05 within r 0.4)
        var cloud = BioMesh()
        let ball = BioShapes.icosphere(radius: 0.05, subdivisions: 0)
        for _ in 0..<80 { cloud.append(ball.translated(M + rng.inBall() * 0.4)) }
        s.add(cloud, alpha: 0.3, roughness: 0.4, order: 2)
        placer.add(M, 0.5)
        if hidden(M) { skip("centrosome") } else { add("centrosome", s, centre: M, radius: 0.5, frame: BioFrame.along(a1, at: C)) }
        return M
    }

    // MARK: Free organelles

    mutating func mitochondria(_ count: Int) {
        for _ in 0..<count {
            guard let p = placeFree("mito", radius: 1.1) else { continue }
            let rot = rng.rotation()
            let axis = BioV.rotate(BioVec(0, 1, 0), axis: rot.axis, degrees: rot.degrees)
            if hidden(p) { skip("mito"); continue }
            let half = crossing(p, 1.1)
            let f = half ? CEGeo.frame(y: axis, towardZ: cameraDirection, at: p) : CEGeo.frame(rot, at: p)
            add("mito", CERecipes.mitochondrion(half: half).placed(f), centre: p, radius: 1.1, frame: f)
        }
    }

    mutating func lysosomes(_ count: Int) {
        for _ in 0..<count {
            guard let p = placeFree("lysosome", radius: 0.3) else { continue }
            if hidden(p) { skip("lysosome"); continue }
            add("lysosome", CERecipes.lysosome.placed(BioFrame(origin: p)), centre: p, radius: 0.3)
        }
    }

    mutating func peroxisomes(_ count: Int) {
        for _ in 0..<count {
            guard let p = placeFree("peroxisome", radius: 0.25) else { continue }
            let rot = rng.rotation()
            if hidden(p) { skip("peroxisome"); continue }
            add("peroxisome", CERecipes.peroxisome.placed(CEGeo.frame(rot, at: p)), centre: p, radius: 0.25)
        }
    }

    mutating func freeVesicles(_ count: Int) {
        for _ in 0..<count {
            let r = rng.uniform(0.08, 0.15)
            guard let p = placeFree("vesicle", radius: r) else { continue }
            vesicle(at: p, radius: r)
        }
    }

    /// A free ribosome at a placed centre.
    mutating func freeRibosome(at p: BioVec, mesh: BioMesh) {
        model.extra["ribosome.free", default: 0] += 1
        if hidden(p) { skip("ribosome"); return }
        var s = CEShape()
        s.add(mesh.translated(p), roughness: 0.5)
        add("ribosome", s, centre: p, radius: ribosomeRadius)
    }

    /// Free ribosomes (placed at the teaching size in either scale, so the rest of the cell is the same).
    mutating func freeRibosomes(_ count: Int, accept: ((BioVec) -> Bool)? = nil, margin m: Double? = nil) {
        let mesh = ribosomeMesh
        let teachingR = o.kind == .bacterium ? 0.020 : 0.06
        for _ in 0..<count {
            guard let p = placeFree("ribosome", radius: teachingR, margin: m, accept: accept) else { continue }
            freeRibosome(at: p, mesh: mesh)
        }
    }

    /// True scale's free ribosomes beyond the teaching ones, to 20,000 in all with the bound: from a second stream,
    /// placed last of all (so nothing else in the cell moves).
    mutating func trueScaleRibosomes(accept: ((BioVec) -> Bool)? = nil, margin m: Double? = nil) {
        guard o.trueScale else { return }
        let more = max(0, 20_000 - (model.extra["ribosome.er"] ?? 0) - (model.extra["ribosome.free"] ?? 0))
        let saved = rng
        rng = BioRandom(seed: o.kind.seed ^ 0x5CA1_EF00)
        let mesh = ribosomeMesh
        for _ in 0..<more {
            guard let p = placeFree("ribosome.true", radius: ribosomeRadius, margin: m, accept: accept) else { continue }
            freeRibosome(at: p, mesh: mesh)
        }
        rng = saved
    }

    // MARK: Cytoskeleton

    /// Microtubules (§5.1.5): from the pericentriolar cloud's surface to the cortex, each a cubic Bézier bent up to 25°,
    /// its direction drawn again (up to 50 times) while its straight line would pass through the nucleus.
    mutating func microtubules(from M: BioVec, count: Int) {
        let r = filament("microtubule", teaching: 0.05)
        for _ in 0..<count {
            var d = rng.unitVector()
            for _ in 0..<50 {
                let along = BioV.dot(nucleusCentre - M, d)
                if along <= 0 || BioV.length(nucleusCentre - M - d * along) > nucleusRadius + 0.3 { break }
                d = rng.unitVector()
            }
            let p0 = M + d * 0.4
            var s = 0.4
            while inside(M + d * (s + 0.1), 0.3) && s < 40 { s += 0.1 }
            let p3 = M + d * s
            let bend = 25 * rng.uniform()
            let phi = 2 * Double.pi * rng.uniform()
            let e1 = CEGeo.perpendicular(d), e2 = BioV.cross(d, e1)
            let off = (e1 * cos(phi) + e2 * sin(phi)) * (BioV.distance(p0, p3) / 3 * tan(bend * .pi / 180))
            let pts = CEGeo.bezier(p0, p0 + (p3 - p0) / 3 + off, p0 + (p3 - p0) * (2.0 / 3) + off, p3, points: 14)
            filamentTube("microtubule", pts, radius: r)
        }
    }

    /// A filament as a tube (its triangles in the cutaway's wedge left out).
    mutating func filamentTube(_ part: String, _ pts: [BioVec], radius r: Double, closed: Bool = false) {
        var m = BioPath.tube(pts, radius: r, sides: 6, samples: 2, closed: closed)
        if cut { m = CEGeo.filtered(m) { !CEGeo.inWedge($0) } }
        let c = pts.reduce(BioVec(0, 0, 0), +) / Double(pts.count)
        var s = CEShape()
        s.add(m, roughness: 0.5)
        if m.isEmpty { skip(part); return }
        add(part, s, centre: c, radius: pts.map { BioV.distance($0, c) }.max() ?? 0.5)
    }

    /// The actin cortex (§5.1.5): straight segments 0.4–0.9 µm long, each tangent to the membrane and `depth` inside
    /// it, at random orientations — draws: a direction (two), the depth, the length, the angle in the tangent plane.
    mutating func actin(_ count: Int, depth: ClosedRange<Double>) {
        let r = filament("actin", teaching: 0.025)
        for _ in 0..<count {
            var placed = false
            for _ in 0..<20 {
                let u = rng.unitVector()
                let dep = rng.uniform(depth.lowerBound, depth.upperBound)
                let len = rng.uniform(0.4, 0.9)
                let angle = rng.uniform(0, 2 * .pi)
                let s = u * membraneInner(u)
                let n = CEGeo.gradient(surface, at: s)
                let p = s - n * dep
                let t = BioV.rotate(CEGeo.perpendicular(n), axis: n, degrees: angle * 180 / .pi)
                let a = p - t * (len / 2), b = p + t * (len / 2)
                guard outsideVacuole(p, 0.1), inside(a, 0.05), inside(b, 0.05) else { continue }
                placed = true
                if hidden(p) { skip("actin"); break }
                var sh = CEShape()
                sh.add(BioShapes.cylinder(from: a, to: b, radius: r, segments: 6, caps: true), roughness: 0.5)
                add("actin", sh, centre: p, radius: len / 2)
                break
            }
            if !placed { model.gaveUp["actin", default: 0] += 1 }
        }
    }

    /// Intermediate filaments (§5.1.1, Nice): 30 wavy tubes from the nuclear envelope out to the cortex.
    mutating func intermediateFilaments(_ count: Int) {
        let r = filament("intermediate", teaching: 0.035)
        for _ in 0..<count {
            let d = rng.unitVector()
            let phase = rng.uniform(0, 2 * .pi)
            let az = rng.uniform(0, 2 * .pi)
            let start = nucleusCentre + d * (nucleusRadius + 0.15)
            var s = nucleusRadius + 0.15
            while inside(nucleusCentre + d * (s + 0.1), 0.3) && s < 40 { s += 0.1 }
            let end = nucleusCentre + d * s
            let e1 = CEGeo.perpendicular(d), e2 = BioV.cross(d, e1)
            let e = e1 * cos(az) + e2 * sin(az)
            let pts = (0..<16).map { k -> BioVec in
                let t = Double(k) / 15
                return start + (end - start) * t + e * (0.25 * sin(2 * .pi * 2.5 * t + phase) * sin(.pi * t))
            }
            filamentTube("intermediate", pts, radius: r)
        }
    }

    // MARK: Finishing

    /// The labels' anchors, the counts, and every mesh turned into the scene's frame.
    mutating func finish() -> CEModel {
        anchors()
        let d = model.display
        for i in model.instances.indices {
            model.instances[i].centre = d.apply(model.instances[i].centre)
            model.instances[i].explodeFrom = d.apply(model.instances[i].explodeFrom)
            for j in model.instances[i].pieces.indices {
                model.instances[i].pieces[j].mesh = model.instances[i].pieces[j].mesh.transformed(d)
            }
        }
        for (k, a) in model.anchors {
            model.anchors[k] = CEAnchor(instance: a.instance, point: d.apply(a.point), normal: a.normal.map { d.applyVector($0) })
        }
        return model
    }
}

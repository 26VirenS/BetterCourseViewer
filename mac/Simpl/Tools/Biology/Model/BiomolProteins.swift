import Foundation

// The Biomolecule Builder's proteins as cartoons (spec §4.4 Proteins items 4 and 6, §6.10): a chain's CA trace drawn
// with helices as ribbons (1.6 Å wide, 0.4 Å thick), strands as flat arrows (2.0 Å wide, the arrowhead 3.2 Å on the
// last residue) and the rest as tubes (radius 0.4 Å), coloured as §3.8 has them; ubiquitin (1UBQ) with its side-chain
// centroids for the tertiary level, deoxy-haemoglobin (2HHB) with its four chains and four hemes for the quaternary
// level, and the seeded random coil ubiquitin unfolds toward when it is denatured. Values only (meshes and
// materials); the view batches them.

enum BiomolCartoon {
    /// A run of residues drawn one way: H (helix ribbon), E (strand arrow) or C (coil tube), first and last inclusive.
    struct Segment: Equatable, Sendable {
        let kind: Character
        let first: Int
        let last: Int
        var count: Int { last - first + 1 }
    }

    /// The cartoon's sizes (Å), §4.4.
    struct Sizes: Sendable {
        var helixWidth = 1.6
        var helixThickness = 0.4
        var strandWidth = 2.0
        var strandThickness = 0.4
        var arrowWidth = 3.2
        var coilRadius = 0.4

        static let standard = Sizes()
    }

    /// The runs of a structure string (H, E, C; anything else is coil). A helix shorter than 3 residues or a strand
    /// shorter than 2 is drawn as coil.
    static func segments(_ ss: [Character]) -> [Segment] {
        func runs(_ s: [Character]) -> [Segment] {
            var out: [Segment] = []
            var i = 0
            while i < s.count {
                var j = i
                while j + 1 < s.count && s[j + 1] == s[i] { j += 1 }
                out.append(Segment(kind: s[i], first: i, last: j))
                i = j + 1
            }
            return out
        }
        var kinds = ss.map { $0 == "H" || $0 == "E" ? $0 : Character("C") }
        for r in runs(kinds) where (r.kind == "H" && r.count < 3) || (r.kind == "E" && r.count < 2) {
            for k in r.first...r.last { kinds[k] = "C" }
        }
        return runs(kinds)
    }

    /// Each segment's mesh, in the order of `segments`.
    static func meshes(_ ca: [BioVec], segments: [Segment], sizes: Sizes = .standard) -> [BioMesh] {
        let n = ca.count
        guard n >= 2 else { return segments.map { _ in BioMesh() } }
        let helixAxes = axes(ca)
        let strandSides = strandGuides(ca)
        return segments.map { s in
            switch s.kind {
            case "H":
                let pts = Array(ca[s.first...s.last])
                var guides = (s.first...s.last).map { helixAxes[$0] }
                // (one way along the helix, then a three-residue moving average)
                let along = ca[s.last] - ca[s.first]
                guides = guides.map { BioV.dot($0, along) < 0 ? -$0 : $0 }
                guides = smooth(guides)
                return BioPath.ribbon(pts, guides: guides, width: sizes.helixWidth, thickness: sizes.helixThickness, samples: 6, elliptical: true)
            case "E":
                let pts = Array(ca[s.first...s.last])
                let guides = smooth((s.first...s.last).map { strandSides[$0] })
                return BioPath.ribbon(pts, guides: guides, width: sizes.strandWidth, thickness: sizes.strandThickness, samples: 6,
                                      arrow: (from: max(pts.count - 2, 0), width: sizes.arrowWidth))
            default:
                let pts = Array(ca[max(s.first - 1, 0)...min(s.last + 1, n - 1)])
                return pts.count >= 2 ? BioPath.tube(pts, radius: sizes.coilRadius, sides: 10, samples: 6) : BioMesh()
            }
        }
    }

    /// Each residue's local helix axis (the cross product of its CA bisector and the next one's), the ends copied in.
    static func axes(_ ca: [BioVec]) -> [BioVec] {
        let n = ca.count
        guard n >= 4 else { return ca.map { _ in BioVec(0, 1, 0) } }
        var out = [BioVec](repeating: BioVec(0, 1, 0), count: n)
        func bisector(_ i: Int) -> BioVec { (ca[i - 1] - ca[i]) + (ca[i + 1] - ca[i]) }
        for i in 1..<(n - 2) { out[i] = BioV.unit(BioV.cross(bisector(i), bisector(i + 1))) }
        out[0] = out[1]
        out[n - 2] = out[n - 3]
        out[n - 1] = out[n - 3]
        return out
    }

    /// Each residue's direction across a strand, in the sheet's plane: the strand's tangent crossed with its pleat (the
    /// CA's offset from its neighbours' midpoint, which alternates side, its sign turned to agree).
    static func strandGuides(_ ca: [BioVec]) -> [BioVec] {
        let n = ca.count
        guard n >= 3 else { return ca.map { _ in BioVec(1, 0, 0) } }
        var pleat = [BioVec](repeating: BioVec(0, 0, 0), count: n)
        for i in 1..<(n - 1) { pleat[i] = (ca[i] - (ca[i - 1] + ca[i + 1]) / 2) * (i % 2 == 0 ? 1 : -1) }
        pleat[0] = pleat[1]
        pleat[n - 1] = pleat[n - 2]
        pleat = smooth(pleat)
        return (0..<n).map { i in
            let t = BioV.unit(ca[min(i + 1, n - 1)] - ca[max(i - 1, 0)])
            return BioV.unit(BioV.cross(t, pleat[i]))
        }
    }

    /// A three-point moving average (the ends average two).
    static func smooth(_ v: [BioVec]) -> [BioVec] {
        guard v.count >= 3 else { return v }
        return v.indices.map { i in
            let lo = max(i - 1, 0), hi = min(i + 1, v.count - 1)
            return BiomolMath.centroid(Array(v[lo...hi]))
        }
    }

    /// A segment's colour: helix pink, strand amber, coil grey (§3.8).
    static func hex(_ kind: Character, dark: Bool) -> String { BioPalette.secondary(kind).hex(dark: dark) }

    /// What a label calls a segment.
    static func name(_ kind: Character) -> String { kind == "H" ? "α-helix" : (kind == "E" ? "β-strand" : "Coil (loop)") }
}

// MARK: - Ubiquitin and haemoglobin (§6.10)

enum BiomolProteins {
    /// Haemoglobin's chains: α₁ (A), β₁ (B), α₂ (C), β₂ (D), each in its colour (§4.4).
    static let chainHex: [Character: String] = ["A": "#e5484d", "B": "#3e63dd", "C": "#f5a3a6", "D": "#9fb4f5"]
    static let chainName: [Character: String] = ["A": "α₁", "B": "β₁", "C": "α₂", "D": "β₂"]
    /// A heme's disk (radius 5 Å, 0.8 Å thick) and its iron (radius 0.7 Å).
    static let hemeHex = "#8b1a1a"
    static let ironHex = "#e07800"
    static let hemeRadius = 5.0, hemeThickness = 0.8, ironRadius = 0.7
    /// The side-chain centroids' spheres (Å).
    static let centroidRadius = 1.6

    /// Which side chains the tertiary level shows plainly (the rest faded).
    enum Show: String, CaseIterable, Sendable {
        case all, core, surface
        var label: String { self == .all ? "All" : (self == .core ? "Hydrophobic core" : "Surface") }
    }

    /// Ubiquitin's structure string (H, E, C) and its CA trace.
    static var ubiquitinSS: [Character] { BioData.ubiquitin.map(\.ss) }
    static var ubiquitinCA: [BioVec] { BioData.ubiquitin.map(\.ca) }

    /// One chain of haemoglobin, N→C.
    static func chain(_ c: Character) -> [BioData.TraceResidue] { BioData.hemoglobin.residues.filter { $0.chain == c } }

    /// A residue's class (its colour) by its one-letter code.
    static func aaClass(_ aa: Character) -> BioAAClass { BioAminoAcids.byOne[aa]?.group ?? .nonpolar }

    /// The tertiary level: ubiquitin's cartoon ("ss:<k>"), its side-chain centroids ("res:<i>", 1-based, coloured by
    /// class; faded but for the nonpolar ones in the core view, faded but for the polar and charged ones in the surface
    /// view), and its labels.
    static func tertiary(dark: Bool, show: Show, labels: Bool = true) -> BioSceneContent {
        let u = BioData.ubiquitin
        let ca = u.map(\.ca)
        let segs = BiomolCartoon.segments(u.map(\.ss))
        let meshes = BiomolCartoon.meshes(ca, segments: segs)
        var items: [BioSceneItem] = []
        let ribbonAlpha = show == .all ? 1.0 : 0.3
        for (k, s) in segs.enumerated() {
            items.append(BioSceneItem(id: "ss:\(k)", mesh: meshes[k],
                                      material: BioMaterial(BiomolCartoon.hex(s.kind, dark: dark), alpha: ribbonAlpha, roughness: 0.45), order: 1))
        }
        let ball = BioShapes.icosphere(radius: centroidRadius, subdivisions: 2)
        for r in u {
            guard let c = r.centroid else { continue }
            let cls = aaClass(r.aa)
            let plain: Bool
            switch show {
            case .all: plain = true
            case .core: plain = cls == .nonpolar
            case .surface: plain = cls != .nonpolar
            }
            items.append(BioSceneItem(id: "res:\(r.i)", mesh: ball.translated(c),
                                      material: BioMaterial(BioPalette.aminoAcid(cls).hex(dark: dark), alpha: plain ? 1 : 0.14, roughness: 0.4),
                                      order: plain ? 0 : 2))
        }
        var ls: [BioLabel3D] = []
        if labels {
            if let first = u.first, let last = u.last {
                ls.append(BioLabel3D(id: "res:\(first.i)", text: "N-terminus (Met1)", anchor: first.centroid ?? first.ca))
                ls.append(BioLabel3D(id: "res:\(last.i)", text: "C-terminus (Gly76)", anchor: last.centroid ?? last.ca))
            }
            if let h = segs.enumerated().filter({ $0.element.kind == "H" }).max(by: { $0.element.count < $1.element.count }) {
                ls.append(BioLabel3D(id: "ss:\(h.offset)", text: "α-helix", anchor: ca[(h.element.first + h.element.last) / 2], area: 4))
            }
            if let e = segs.enumerated().filter({ $0.element.kind == "E" }).max(by: { $0.element.count < $1.element.count }) {
                ls.append(BioLabel3D(id: "ss:\(e.offset)", text: "β-sheet (5 strands)", anchor: ca[(e.element.first + e.element.last) / 2], area: 4))
            }
            if show == .core, let leu = u.first(where: { $0.i == 43 }), let c = leu.centroid {
                ls.append(BioLabel3D(id: "res:43", text: "Leu43 (core)", anchor: c))
            }
            if show == .surface, let lys = u.first(where: { $0.i == 48 }), let c = lys.centroid {
                ls.append(BioLabel3D(id: "res:48", text: "Lys48 (surface)", anchor: c))
            }
        }
        return BioSceneContent(items: items, labels: ls)
    }

    /// The quaternary level: haemoglobin's four chains as cartoons in their colours ("chain:A:ss:<k>"), each heme a
    /// disk through its four pyrrole N with its iron ("heme:A:disk", "heme:A:iron"), and β6 Glu marked on both β chains
    /// ("glu6:B") — the residue the sickle-cell mutation changes.
    static func quaternary(dark: Bool, labels: Bool = true) -> BioSceneContent {
        var items: [BioSceneItem] = []
        var ls: [BioLabel3D] = []
        for c in ["A", "B", "C", "D"].map({ Character($0) }) {
            let rs = chain(c)
            let ca = rs.map(\.ca)
            let segs = BiomolCartoon.segments(rs.map(\.ss))
            let meshes = BiomolCartoon.meshes(ca, segments: segs)
            for (k, _) in segs.enumerated() {
                items.append(BioSceneItem(id: "chain:\(c):ss:\(k)", mesh: meshes[k], material: BioMaterial(chainHex[c] ?? "#8e8e93", roughness: 0.45)))
            }
            if labels, let mid = rs.dropFirst(rs.count / 3).first {
                ls.append(BioLabel3D(id: "chain:\(c)", text: "\(chainName[c] ?? "") chain (\(c == "A" || c == "C" ? "α" : "β"), \(rs.count) aa)",
                                     anchor: mid.ca, area: 10))
            }
            if c == "B" || c == "D", let glu = rs.first(where: { $0.i == 6 }) {
                items.append(BioSceneItem(id: "glu6:\(c)", mesh: BioShapes.icosphere(radius: 1.4, subdivisions: 2).translated(glu.ca),
                                          material: BioMaterial(BioPalette.acidic.hex(dark: dark), roughness: 0.4)))
                if labels && c == "B" { ls.append(BioLabel3D(id: "glu6:B", text: "β6 Glu (Val in sickle cell)", anchor: glu.ca, area: 2)) }
            }
        }
        for (k, h) in BioData.hemoglobin.hemes.enumerated() {
            let (centre, normal) = plane(h.n)
            let disk = BioShapes.cylinder(from: centre - normal * (hemeThickness / 2), to: centre + normal * (hemeThickness / 2),
                                          radius: hemeRadius, segments: 32)
            items.append(BioSceneItem(id: "heme:\(h.chain):disk", mesh: disk, material: BioMaterial(hemeHex, roughness: 0.5)))
            items.append(BioSceneItem(id: "heme:\(h.chain):iron", mesh: BioShapes.icosphere(radius: ironRadius, subdivisions: 2).translated(h.fe),
                                      material: BioMaterial(ironHex, roughness: 0.3)))
            if labels && (k == 0 || k == 3) { ls.append(BioLabel3D(id: "heme:\(h.chain)", text: "Heme (Fe²⁺)", anchor: centre + normal * 0.4, area: 3)) }
        }
        return BioSceneContent(items: items, labels: ls)
    }

    /// The best-fit plane through some points: their centroid and a unit normal (Newell's method round them in order).
    static func plane(_ pts: [BioVec]) -> (centre: BioVec, normal: BioVec) {
        guard !pts.isEmpty else { return (BioVec(0, 0, 0), BioVec(0, 0, 1)) }
        let c = BiomolMath.centroid(pts)
        var n = BioVec(0, 0, 0)
        for i in pts.indices { n += BioV.cross(pts[i] - c, pts[(i + 1) % pts.count] - c) }
        let u = BioV.unit(n)
        return (c, BioV.length(u) > 0 ? u : BioVec(0, 0, 1))
    }

    // MARK: Denaturing (Nice)

    /// The extended random coil ubiquitin unfolds toward: a seeded walk (SplitMix64, seed 76) of 3.8 Å steps from the
    /// first CA, each step's direction a fresh random unit vector (two draws) added to 0.55 × the last step's, kept when
    /// no earlier CA but the last two is closer than 4.5 Å (40 tries, else the last try), then centred where the native
    /// protein's centre is.
    static func randomCoil(_ native: [BioVec], seed: UInt64 = 76) -> [BioVec] {
        guard native.count >= 2 else { return native }
        var g = BioRandom(seed: seed)
        var pts = [native[0]]
        var last = g.unitVector()
        for _ in 1..<native.count {
            var chosen = pts[pts.count - 1] + last * 3.8
            var chosenDir = last
            for _ in 0..<40 {
                let d = BioV.unit(last * 0.55 + g.unitVector())
                let p = pts[pts.count - 1] + d * 3.8
                chosen = p
                chosenDir = d
                let clear = pts.dropLast(2).allSatisfy { BioV.distance($0, p) >= 4.5 }
                if clear { break }
            }
            pts.append(chosen)
            last = chosenDir
        }
        let shift: BioVec = BiomolMath.centroid(native) - BiomolMath.centroid(pts)
        return pts.map { $0 + shift }
    }

    /// Denaturing as matching meshes (the same vertices in the same order, so one morphs into the other): each native
    /// segment as a tube through its CAs in its structure's colour, and the side-chain centroids by class, in the
    /// native fold and in the random coil (each centroid kept at its native offset from its CA).
    static func denatureMeshes(dark: Bool) -> (parts: [(hex: String, native: BioMesh, coil: BioMesh)], frame: BioSphere) {
        let u = BioData.ubiquitin
        let native = u.map(\.ca)
        let coil = randomCoil(native)
        let segs = BiomolCartoon.segments(u.map(\.ss))
        var parts: [(hex: String, native: BioMesh, coil: BioMesh)] = []
        let n = native.count
        for s in segs {
            let lo = max(s.first - 1, 0), hi = min(s.last + 1, n - 1)
            guard hi > lo else { continue }
            let radius = s.kind == "C" ? 0.45 : 0.75
            parts.append((BiomolCartoon.hex(s.kind, dark: dark),
                          BioPath.tube(Array(native[lo...hi]), radius: radius, sides: 10, samples: 6, caps: false),
                          BioPath.tube(Array(coil[lo...hi]), radius: radius, sides: 10, samples: 6, caps: false)))
        }
        let ball = BioShapes.icosphere(radius: 1.3, subdivisions: 1)
        for cls in BioAAClass.allCases {
            var a = BioMesh(), b = BioMesh()
            for (k, r) in u.enumerated() where aaClass(r.aa) == cls {
                let off = (r.centroid ?? r.ca) - r.ca
                a.append(ball.translated(r.ca + off))
                b.append(ball.translated(coil[k] + off))
            }
            if !a.isEmpty { parts.append((BioPalette.aminoAcid(cls).hex(dark: dark), a, b)) }
        }
        let s = BioV.boundingSphere(native + coil)
        return (parts, BioSphere(centre: s.centre, radius: s.radius + 2))
    }
}

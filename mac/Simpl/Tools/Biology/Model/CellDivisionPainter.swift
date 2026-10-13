import Foundation

// Cell Division's drawing as a list (spec §4.6 Drawing rules): a frame turned into what to draw, back to front — the
// cells' cytoplasm, the plant's walls, the membranes, the nuclei (a double envelope, dashed while it breaks down or
// forms) and nucleoli, the furrow's ring and the cell plate, the metaphase plate (dashed: an imaginary plane), the
// spindle (polar and kinetochore microtubules, broad poles in a plant), the centrosomes with their centrioles and
// asters, and the chromatids (outlined, the mother's red and the father's blue, a recombinant part in the other
// parent's colour, a dot at each chromosome's centromere). Everything is in cell units except line widths given in
// points; the view (CellDivisionCanvas.swift) maps units to points, inks to colours, and strokes. Labels' anchors and
// the pointer's hits are found here too. Foundation only.

/// What a part is drawn in (the view gives each its colour in light and dark).
enum DivInk: Equatable {
    case cytoplasm, membrane, wall, nucleoplasm, envelope, nucleolus, furrow, cellPlate, plate, microtubule, aster
    case centrosome, centriole, maternal, paternal, edge, centromere

    /// A homolog's colour: the mother's (0) red, the father's (1) blue.
    static func parent(_ homolog: Int) -> DivInk { homolog == 0 ? .maternal : .paternal }
}

/// A run of a chromatid in one colour (or part way from one to another, as crossing over recolours it).
struct DivRun: Equatable {
    var from: Int
    var to: Int
    var ink: DivInk
    var other: DivInk
    var mix: Double
}

/// A chromatid to draw.
struct DivChromatidPaint: Equatable {
    var index: Int
    var points: [DivPoint]
    /// Its width in cell units.
    var width: Double
    var runs: [DivRun]
    var alpha: Double
    /// How condensed (its sheen shows as it condenses).
    var k: Double
}

enum DivPrimitive: Equatable {
    /// Closed shapes filled as one (where they overlap, filled once).
    case fill([[DivPoint]], DivInk, alpha: Double)
    /// Lines `width` points wide, dashed if asked (stroked as one, so where they meet they are not drawn twice).
    case line([[DivPoint]], DivInk, width: Double, alpha: Double, dashed: Bool)
    /// Lines `width` cell units wide, round ends.
    case band([[DivPoint]], DivInk, width: Double, alpha: Double)
    /// A disc, its radius in cell units.
    case disc(DivPoint, Double, DivInk, alpha: Double)
    case chromatid(DivChromatidPaint)
}

enum DivPainter {
    /// A frame as what to draw, and its chromatids' shapes (for labels and the pointer). `fade` multiplies every
    /// opacity (a crossfade under Reduce Motion draws two frames).
    static func paint(_ f: DivisionFrame, chiasma: Double, fade: Double = 1) -> (primitives: [DivPrimitive], shapes: [DivChromatidShape]) {
        var out: [DivPrimitive] = []
        let shapes = f.shapes(chiasma: chiasma)

        // (the cells: one fill, then their walls and membranes where the outline is membrane)
        out.append(.fill(f.cells.map(outline), .cytoplasm, alpha: fade))
        // (runs of one opacity stroked together)
        let runs = Dictionary(grouping: f.cells.flatMap(membraneRuns), by: \.alpha).sorted { $0.key < $1.key }
        if f.plant {
            for (a, rs) in runs { out.append(.band(rs.map(\.points), .wall, width: 0.034, alpha: a * fade)) }
        }
        for (a, rs) in runs { out.append(.line(rs.map(\.points), .membrane, width: f.plant ? 1.2 : 1.7, alpha: a * fade, dashed: false)) }

        // (nuclei: nucleoplasm, nucleolus, then the envelope's two membranes)
        for n in f.nuclei where n.alpha > 0.01 || n.nucleolusAlpha > 0.01 {
            if n.alpha > 0.01 { out.append(.disc(n.c, n.r, .nucleoplasm, alpha: n.alpha * fade)) }
            if n.nucleolusAlpha > 0.01, n.nucleolus > 0.005 { out.append(.disc(n.nucleolusCentre, n.nucleolus, .nucleolus, alpha: n.nucleolusAlpha * fade)) }
            guard n.alpha > 0.01 else { continue }
            let dashed = n.dashed > 0.5
            out.append(.line([circle(n.c, n.r), circle(n.c, n.r - 0.022)], .envelope, width: 1.2, alpha: n.alpha * (dashed ? 0.8 : 1) * fade, dashed: dashed))
        }

        // (the furrow's actin–myosin ring, seen edge on: a dot at each side of the waist)
        for fu in f.furrows where fu.alpha > 0.01 {
            for s in [-1.0, 1.0] { out.append(.disc(fu.point(s) + DivPoint(s * 0.02, 0), 0.03, .furrow, alpha: fu.alpha * fade)) }
        }
        // (the cell plate: vesicles fusing along a line, from the middle out)
        for cp in f.cellPlates where cp.alpha > 0.01 && cp.half > 0.01 {
            out.append(.line([[cp.point(-1), cp.point(1)]], .cellPlate, width: 2.4, alpha: cp.alpha * fade, dashed: false))
            let count = max(1, Int(cp.half / 0.05))
            for i in -count...count {
                out.append(.disc(cp.point(Double(i) / Double(count)), 0.017, .cellPlate, alpha: cp.alpha * 0.9 * fade))
            }
        }

        // (the spindles: the metaphase plate, the polar microtubules overlapping in the middle)
        for s in f.spindles {
            let a = f.poles[s.a].p, b = f.poles[s.b].p
            let axis = b - a
            guard axis.length > 0.05 else { continue }
            let u = axis * (1 / axis.length), v = DivPoint(-u.y, u.x)
            let mid = a.mixed(b, 0.5)
            if s.plate > 0.01 {
                out.append(.line([[mid - v * (s.plateLength / 2), mid + v * (s.plateLength / 2)]], .plate, width: 1.3, alpha: s.plate * 0.85 * fade, dashed: true))
            }
            if s.polar > 0.01 {
                var fibres: [[DivPoint]] = []
                for (from, to, flip) in [(a, b, 1.0), (b, a, -1.0)] {
                    for j in -2...2 {
                        let spread = Double(j) * 0.075 + (flip > 0 ? 0 : 0.035)
                        let end = from.mixed(to, 0.6) + v * spread
                        fibres.append(bowed(poleStart(from, toward: end, v: v, plant: f.plant), end, v: v, bow: spread * 0.5))
                    }
                }
                out.append(.line(fibres, .microtubule, width: 1, alpha: s.polar * 0.5 * fade, dashed: false))
            }
        }
        // (kinetochore microtubules: from a pole to each chromatid's kinetochore)
        for (i, c) in f.chromatids.enumerated() where c.pole >= 0 && c.fibre > 0.01 && c.alpha > 0.01 && f.poles.indices.contains(c.pole) {
            let p = f.poles[c.pole].p, k = shapes[i].kinetochore
            let d = k - p
            guard d.length > 0.02 else { continue }
            let v = DivPoint(-d.y, d.x) * (1 / d.length)
            let broad = across(pole: c.pole, f) ?? v
            let fibres = [-1.0, 1.0].map { s -> [DivPoint] in
                let end = k + v * (s * 0.011)
                return [poleStart(p, toward: end, v: broad, plant: f.plant), end]
            }
            out.append(.line(fibres, .microtubule, width: 1.1, alpha: c.fibre * c.alpha * 0.85 * fade, dashed: false))
        }
        // (centrosomes: the pericentriolar cloud, two centrioles at right angles, and the aster's rays)
        for p in f.poles {
            if p.aster > 0.01 {
                let rays = (0..<12).map { j -> [DivPoint] in
                    let d = DivPoint.dir(Double(j) * .pi / 6 + 0.2)
                    return [p.p + d * 0.055, p.p + d * 0.17]
                }
                out.append(.line(rays, .aster, width: 0.9, alpha: p.aster * 0.45 * fade, dashed: false))
            }
            guard p.centrosome > 0.01 else { continue }
            out.append(.disc(p.p, 0.055, .centrosome, alpha: p.centrosome * 0.35 * fade))
            out.append(.band([[p.p + DivPoint(-0.032, 0.014), p.p + DivPoint(0.012, 0.014)], [p.p + DivPoint(0.026, -0.03), p.p + DivPoint(0.026, 0.006)]],
                             .centriole, width: 0.019, alpha: p.centrosome * fade))
        }

        // (the chromatids, then a dot at each chromosome's centromere)
        for (i, c) in f.chromatids.enumerated() where c.alpha > 0.01 {
            var pts = shapes[i].points
            var distal = shapes[i].distalStart
            if c.grow < 0.999 {
                pts = trimmed(pts, c.grow)
                if let d = distal, d >= pts.count { distal = nil }
            }
            guard pts.count >= 2 else { continue }
            let own = DivInk.parent(c.id.homolog), other = DivInk.parent(1 - c.id.homolog)
            var runs = [DivRun(from: 0, to: pts.count - 1, ink: own, other: own, mix: 0)]
            if let d = distal, c.recombined > 0.001, d > 0, d < pts.count {
                runs = [DivRun(from: 0, to: d, ink: own, other: own, mix: 0),
                        DivRun(from: d - 1, to: pts.count - 1, ink: own, other: other, mix: c.recombined)]
            }
            out.append(.chromatid(DivChromatidPaint(index: i, points: pts, width: shapes[i].width, runs: runs, alpha: c.alpha * fade, k: c.k)))
        }
        for (i, c) in f.chromatids.enumerated() where c.alpha > 0.01 && c.k > 0.25 {
            // (one dot per chromosome: joined sisters share theirs, on their axis)
            if c.joined && c.id.sister == 1 { continue }
            let at = c.joined ? c.c : shapes[i].points[shapes[i].centromere]
            let r = 0.017 + 0.008 * c.k
            out.append(.disc(at, r, .centromere, alpha: c.alpha * DivMath.smoothstep(0.25, 0.6, c.k) * fade))
        }
        return (out, shapes)
    }

    /// Where a cell's outline is membrane: runs of it, each with one opacity (as cells part, the line between them
    /// appears).
    static func membraneRuns(_ c: DivCellKey) -> [(points: [DivPoint], alpha: Double)] {
        let n = DivCellKey.samples
        func level(_ i: Int) -> Double { (min(c.membrane[i % n], c.membrane[(i + 1) % n]) * 20).rounded() / 20 }
        // (start where the level changes, so a run is never cut at sample 0)
        let start = (0..<n).first { level($0) != level(($0 + n - 1) % n) } ?? 0
        var out: [(points: [DivPoint], alpha: Double)] = []
        var current: [DivPoint] = []
        var alpha = level(start)
        for step in 0..<n {
            let i = (start + step) % n
            let a = level(i)
            if a != alpha {
                if alpha > 0.02, current.count > 1 { out.append((current, alpha)) }
                current = []
                alpha = a
            }
            if current.isEmpty { current.append(c.point(i)) }
            current.append(c.point((i + 1) % n))
        }
        if alpha > 0.02, current.count > 1 { out.append((current, alpha)) }
        // (a run that stops where its cell meets another part of the same cell reaches on to the line between them,
        // so the outline has no gap where the two parts' runs meet)
        guard out.count > 1 || (out.first?.points.count ?? 0) <= n else { return out }
        return out.map { run in
            var pts = run.points
            if pts.count > 2 {
                pts[0] = reach(pts[0], pts[0] - pts[1])
                pts[pts.count - 1] = reach(pts[pts.count - 1], pts[pts.count - 1] - pts[pts.count - 2])
            }
            return (pts, run.alpha)
        }
    }

    /// From the end of a membrane, on along it to where cells divide (x = 0 or y = 0), if that is near.
    static func reach(_ p: DivPoint, _ d: DivPoint) -> DivPoint {
        guard d.length > 0 else { return p }
        let u = d * (1 / d.length)
        var t = 0.0
        for (pos, dir) in [(p.x, u.x), (p.y, u.y)] where abs(dir) > 1e-6 {
            let hit = -pos / dir
            if hit > 0, hit < 0.08 { t = t == 0 ? hit : min(t, hit) }
        }
        return p + u * t
    }

    /// A cell's outline for its fill: its samples, with the corner where membrane meets the line to another part of
    /// the same cell put in, so the parts' fills meet without a sliver between them.
    static func outline(_ c: DivCellKey) -> [DivPoint] {
        let n = DivCellKey.samples
        var out: [DivPoint] = []
        for i in 0..<n {
            let j = (i + 1) % n
            out.append(c.point(i))
            let here = c.membrane[i] >= 0.5, next = c.membrane[j] >= 0.5
            if here && !next {
                out.append(reach(c.point(i), c.point(i) - c.point((i + n - 1) % n)))
            } else if !here && next {
                out.append(reach(c.point(j), c.point(j) - c.point((j + 1) % n)))
            }
        }
        return out
    }

    static func circle(_ c: DivPoint, _ r: Double, segments: Int = 96) -> [DivPoint] {
        (0...segments).map { c + DivPoint.dir(2 * .pi * Double($0) / Double(segments)) * r }
    }

    /// A microtubule bowed out from the spindle's axis (the spindle's barrel shape).
    static func bowed(_ a: DivPoint, _ b: DivPoint, v: DivPoint, bow: Double) -> [DivPoint] {
        (0...10).map { j in
            let t = Double(j) / 10
            return a.mixed(b, t) + v * (bow * sin(.pi * t))
        }
    }

    /// Where a microtubule leaves its pole: the centrosome in an animal cell; spread along a broad pole in a plant
    /// (no centrosome: the plant's microtubule-organising regions).
    static func poleStart(_ p: DivPoint, toward end: DivPoint, v: DivPoint, plant: Bool) -> DivPoint {
        guard plant else { return p }
        let d = end - p
        return p + v * max(-0.14, min(0.14, (d.x * v.x + d.y * v.y) * 0.35))
    }

    /// Across a pole's spindle (the spindle there that is most drawn), for a plant's broad poles.
    static func across(pole j: Int, _ f: DivisionFrame) -> DivPoint? {
        let s = f.spindles.filter { ($0.a == j || $0.b == j) && f.poles[$0.a].p.distance(f.poles[$0.b].p) > 0.05 }
            .max { $0.polar + $0.plate < $1.polar + $1.plate }
        guard let s else { return nil }
        let d = f.poles[s.b].p - f.poles[s.a].p
        return DivPoint(-d.y, d.x) * (1 / d.length)
    }

    /// The first part of a line, as a fraction of its length.
    static func trimmed(_ pts: [DivPoint], _ f: Double) -> [DivPoint] {
        guard f > 0, pts.count > 1 else { return [] }
        let total = DivGeometry.length(pts)
        var want = total * min(f, 1)
        var out = [pts[0]]
        for (a, b) in zip(pts, pts.dropFirst()) {
            let d = a.distance(b)
            if d >= want {
                out.append(a.mixed(b, d > 0 ? want / d : 0))
                return out
            }
            want -= d
            out.append(b)
        }
        return out
    }

    // MARK: Labels and the pointer

    /// Where a label points in a frame as drawn, and whether its part is shown.
    static func anchor(_ a: DivAnchor, _ f: DivisionFrame, _ shapes: [DivChromatidShape]) -> (point: DivPoint, visible: Bool) {
        switch a {
        case .point(let p):
            return (p, true)
        case .centromere(let i):
            guard shapes.indices.contains(i) else { return (.zero, false) }
            return (shapes[i].points[shapes[i].centromere], f.chromatids[i].alpha > 0.3)
        case .arm(let i, let t):
            guard shapes.indices.contains(i) else { return (.zero, false) }
            let s = shapes[i]
            let j = s.centromere + Int((t * Double(s.points.count - 1 - s.centromere)).rounded())
            return (s.points[min(j, s.points.count - 1)], f.chromatids[i].alpha > 0.3)
        case .shortArm(let i, let t):
            guard shapes.indices.contains(i) else { return (.zero, false) }
            let s = shapes[i]
            let j = s.centromere - Int((t * Double(s.centromere)).rounded())
            return (s.points[max(j, 0)], f.chromatids[i].alpha > 0.3)
        case .nucleus(let j, let angle):
            guard f.nuclei.indices.contains(j) else { return (.zero, false) }
            let n = f.nuclei[j]
            return (n.c + DivPoint.dir(angle) * n.r, n.alpha > 0.2)
        case .nucleolus(let j):
            guard f.nuclei.indices.contains(j) else { return (.zero, false) }
            let n = f.nuclei[j]
            return (n.nucleolusCentre, n.nucleolusAlpha > 0.3 && n.nucleolus > 0.02)
        case .pole(let j):
            guard f.poles.indices.contains(j) else { return (.zero, false) }
            return (f.poles[j].p, f.plant || f.poles[j].centrosome > 0.3)
        case .cell(let j, let sample):
            guard f.cells.indices.contains(j) else { return (.zero, false) }
            return (f.cells[j].point(sample % DivCellKey.samples), true)
        case .fibre(let i, let t):
            guard f.chromatids.indices.contains(i) else { return (.zero, false) }
            let c = f.chromatids[i]
            guard c.pole >= 0, f.poles.indices.contains(c.pole) else { return (.zero, false) }
            return (f.poles[c.pole].p.mixed(shapes[i].kinetochore, t), c.fibre > 0.3 && c.alpha > 0.3)
        case .polar(let s, let t):
            guard f.spindles.indices.contains(s) else { return (.zero, false) }
            let sp = f.spindles[s]
            let a = f.poles[sp.a].p, b = f.poles[sp.b].p
            let d = b - a
            let v = d.length > 0 ? DivPoint(-d.y, d.x) * (1 / d.length) : DivPoint(1, 0)
            return (a.mixed(b, t) + v * 0.15, sp.polar > 0.2)
        case .plate(let s, let t):
            guard f.spindles.indices.contains(s) else { return (.zero, false) }
            let sp = f.spindles[s]
            let a = f.poles[sp.a].p, b = f.poles[sp.b].p
            let d = b - a
            let v = d.length > 0 ? DivPoint(-d.y, d.x) * (1 / d.length) : DivPoint(1, 0)
            return (a.mixed(b, 0.5) + v * (t * sp.plateLength / 2), sp.plate > 0.3)
        case .cellPlate(let j, let t):
            guard f.cellPlates.indices.contains(j) else { return (.zero, false) }
            return (f.cellPlates[j].point(t), f.cellPlates[j].alpha > 0.2)
        case .furrow(let j, let t):
            guard f.furrows.indices.contains(j) else { return (.zero, false) }
            return (f.furrows[j].point(t), f.furrows[j].alpha > 0.2)
        }
    }

    /// The chromatid under a point (cell units), if one is within `tolerance` of its line.
    static func hit(_ p: DivPoint, _ f: DivisionFrame, _ shapes: [DivChromatidShape], tolerance: Double) -> Int? {
        var best: (i: Int, d: Double)?
        for (i, s) in shapes.enumerated() where f.chromatids[i].alpha > 0.3 {
            let d = zip(s.points, s.points.dropFirst()).map { distance(p, $0.0, $0.1) }.min() ?? .infinity
            if d <= s.width / 2 + tolerance, d < (best?.d ?? .infinity) { best = (i, d) }
        }
        return best?.i
    }

    /// From a point to a segment.
    static func distance(_ p: DivPoint, _ a: DivPoint, _ b: DivPoint) -> Double {
        let ab = b - a, ap = p - a
        let l2 = ab.x * ab.x + ab.y * ab.y
        let t = l2 > 0 ? DivMath.clamp01((ap.x * ab.x + ap.y * ab.y) / l2) : 0
        return p.distance(a + ab * t)
    }
}

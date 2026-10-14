import Foundation

// Cell Division's drawing as values (spec §4.6 Drawing rules, §5.2.4): everything in "cell units" — the animal cell a
// circle of radius 1.0 and the plant cell a rounded square 1.9 × 1.9, centred at the origin, y up — so the view only
// maps units to points. A keyframe (`DivisionFrame`) holds every part's state at the end of a step; between two steps
// the view draws `DivisionFrame.mixed` (positions, turns, condensation and opacities eased together), and each
// chromatid is drawn from its state by `DivGeometry.shape`: a wavy thread at condensation 0, a capsule 0.055 wide at 1,
// with its sister 0.012 from it, pinched together at the centromere. Cells are outlines sampled round a centre, so one
// cell can pinch and part into two (or four) by easing the samples. Foundation only.

/// A point in cell units (y up).
struct DivPoint: Hashable {
    var x: Double
    var y: Double

    init(_ x: Double, _ y: Double) {
        self.x = x
        self.y = y
    }

    static let zero = DivPoint(0, 0)
    static func + (a: DivPoint, b: DivPoint) -> DivPoint { DivPoint(a.x + b.x, a.y + b.y) }
    static func - (a: DivPoint, b: DivPoint) -> DivPoint { DivPoint(a.x - b.x, a.y - b.y) }
    static func * (a: DivPoint, s: Double) -> DivPoint { DivPoint(a.x * s, a.y * s) }

    var length: Double { (x * x + y * y).squareRoot() }
    var angle: Double { atan2(y, x) }

    /// The unit vector at an angle (radians, from +x, counterclockwise).
    static func dir(_ a: Double) -> DivPoint { DivPoint(cos(a), sin(a)) }

    func mixed(_ o: DivPoint, _ t: Double) -> DivPoint { DivPoint(x + (o.x - x) * t, y + (o.y - y) * t) }
    func distance(_ o: DivPoint) -> Double { (self - o).length }
}

/// A box in cell units.
struct DivRect: Equatable {
    var minX: Double
    var minY: Double
    var maxX: Double
    var maxY: Double

    static let empty = DivRect(minX: .infinity, minY: .infinity, maxX: -.infinity, maxY: -.infinity)

    var width: Double { maxX - minX }
    var height: Double { maxY - minY }
    var centre: DivPoint { DivPoint((minX + maxX) / 2, (minY + maxY) / 2) }

    func adding(_ p: DivPoint) -> DivRect {
        DivRect(minX: min(minX, p.x), minY: min(minY, p.y), maxX: max(maxX, p.x), maxY: max(maxY, p.y))
    }

    func union(_ o: DivRect) -> DivRect {
        DivRect(minX: min(minX, o.minX), minY: min(minY, o.minY), maxX: max(maxX, o.maxX), maxY: max(maxY, o.maxY))
    }

    func mixed(_ o: DivRect, _ t: Double) -> DivRect {
        DivRect(minX: DivMath.mix(minX, o.minX, t), minY: DivMath.mix(minY, o.minY, t),
                maxX: DivMath.mix(maxX, o.maxX, t), maxY: DivMath.mix(maxY, o.maxY, t))
    }
}

enum DivMath {
    static func mix(_ a: Double, _ b: Double, _ t: Double) -> Double { a + (b - a) * t }

    /// Between two angles the short way round (radians).
    static func mixAngle(_ a: Double, _ b: Double, _ t: Double) -> Double { a + turn(from: a, to: b) * t }

    /// The signed turn from one angle to another, −π…π.
    static func turn(from a: Double, to b: Double) -> Double {
        var d = (b - a).truncatingRemainder(dividingBy: 2 * .pi)
        if d > .pi { d -= 2 * .pi } else if d < -.pi { d += 2 * .pi }
        return d
    }

    static func clamp01(_ x: Double) -> Double { min(max(x, 0), 1) }

    static func smoothstep(_ e0: Double, _ e1: Double, _ x: Double) -> Double {
        let t = clamp01((x - e0) / (e1 - e0))
        return t * t * (3 - 2 * t)
    }

    static func radians(_ degrees: Double) -> Double { degrees * .pi / 180 }
}

// MARK: - Chromosomes

/// A homologous pair of the model organism (spec §4.6): the long pair (7 units, centromere 40% along), the short pair
/// (4 units, 50%), and with 2n = 6 a third pair of 5 units (its centromere, which the spec leaves open, at 35%).
struct DivPairInfo: Equatable {
    let units: Double
    /// Where the centromere is, as a fraction of the length from the short arm's end.
    let centromere: Double
    let name: String
}

/// A chromatid's place in the model: its pair (0 long, 1 short, 2 the third), its homolog (0 from the mother, 1 from
/// the father) and which sister it is (0, 1; sister 1 is made in S phase).
struct DivChromatidID: Hashable, Comparable {
    let pair: Int
    let homolog: Int
    let sister: Int

    var index: Int { (pair * 2 + homolog) * 2 + sister }
    var chromosome: Int { pair * 2 + homolog }

    static func < (a: DivChromatidID, b: DivChromatidID) -> Bool { a.index < b.index }

    static func at(_ index: Int) -> DivChromatidID { DivChromatidID(pair: index / 4, homolog: (index / 2) % 2, sister: index % 2) }
}

/// A chromatid at a keyframe: how it is drawn and, for the counts, what it is.
struct DivChromatidKey: Equatable {
    let id: DivChromatidID
    /// Its centromere.
    var c: DivPoint = .zero
    /// The short (p) arm's direction and the long (q) arm's, radians: opposite when it is straight, a V in anaphase.
    var a1: Double = .pi
    var a2: Double = 0
    /// 0 an interphase thread … 1 fully condensed.
    var k: Double = 0
    /// Across its chromosome's axis (left of the way from the short arm's end to the long arm's): ± for sisters side
    /// by side, 0 for a chromatid on its own.
    var offset: Double = 0
    /// Beyond the chiasma, how far across it lies on the other homolog's side (crossing over in prophase I and
    /// metaphase I, where non-sister chromatids cross).
    var cross: Double = 0
    /// Beyond the chiasma, coloured as the other parent: 0…1 (a recombinant chromatid).
    var recombined: Double = 0
    var alpha: Double = 1
    /// How much of it is drawn, from the short arm's end (1: all; its copy grows along it in S phase).
    var grow: Double = 1
    /// The spindle pole its kinetochore faces (−1: none), and its kinetochore microtubules' opacity.
    var pole: Int = -1
    var fibre: Double = 0
    /// The nucleus it is gathered into as a new nucleus forms (−1: none).
    var nucleus: Int = -1
    /// Its thread's wiggle (sisters share their chromosome's).
    var seed: Double = 0
    /// For the counts: whether it exists yet (sister 1 is made in S), whether it is still joined to its sister, the
    /// cell it is in and the part of that cell (a pole or a nucleus; −1: the cell as a whole).
    var exists = true
    var joined = false
    var cell = 0
    var part = -1

    init(id: DivChromatidID) { self.id = id }
}

/// A chromatid as drawn: its line from the short arm's end through the centromere to the long arm's end.
struct DivChromatidShape {
    var points: [DivPoint]
    /// The centromere's point.
    var centromere: Int
    /// The first point beyond the chiasma, where a recombinant chromatid's swapped part starts.
    var distalStart: Int?
    var width: Double
    /// Where its kinetochore microtubules attach: the centromere, on the side facing its pole.
    var kinetochore: DivPoint
}

enum DivGeometry {
    /// The model's three pairs.
    static let pairs = [DivPairInfo(units: 7, centromere: 0.4, name: "long"),
                        DivPairInfo(units: 4, centromere: 0.5, name: "short"),
                        DivPairInfo(units: 5, centromere: 0.35, name: "middle")]

    /// A unit of chromosome condensed: the long pair's 7 are 0.42, the short pair's 4 are 0.24.
    static let unit = 0.06
    /// A unit as an interphase thread, measured along it: the long pair's 7 are 0.9.
    static let threadUnit = 0.9 / 7
    /// A thread's span end to end, as a part of its length along itself (its wiggles take the rest).
    static let threadSpan = 0.5
    /// A chromatid's width condensed, and as a thread.
    static let width = 0.055
    static let threadWidth = 0.016
    /// Between sister chromatids.
    static let gap = 0.012
    /// The thread's wave, end to end.
    static let wave = 0.075
    /// Points along a chromatid, every so far.
    static let step = 0.008

    static func chromatidWidth(_ k: Double) -> Double { DivMath.mix(threadWidth, width, DivMath.clamp01(k)) }

    /// A sister's offset from its chromosome's axis: half a chromatid's width and half the gap.
    static func sisterOffset(_ k: Double) -> Double { (chromatidWidth(k) + gap) / 2 }

    /// The chromosome's span end to end: its thread's at 0, its condensed length at 1.
    static func span(_ pair: DivPairInfo, _ k: Double) -> Double {
        DivMath.mix(pair.units * threadUnit * threadSpan, pair.units * unit, DivMath.clamp01(k))
    }

    /// The thread's wiggle at condensation 0: set so the long pair's thread is 0.9 long along itself (the spec's
    /// interphase length), found once.
    static let wiggle: Double = {
        var lo = 0.0, hi = 0.08
        for _ in 0..<40 {
            let mid = (lo + hi) / 2
            if threadLength(pairs[0], amplitude: mid) < 0.9 { lo = mid } else { hi = mid }
        }
        return (lo + hi) / 2
    }()

    /// The long-way length of a thread drawn with a given wiggle (for `wiggle`).
    static func threadLength(_ pair: DivPairInfo, amplitude: Double) -> Double {
        var key = DivChromatidKey(id: DivChromatidID(pair: 0, homolog: 0, sister: 0))
        key.k = 0
        return length(shape(key, pair: pair, chiasma: 0.5, amplitude: amplitude).points)
    }

    static func length(_ points: [DivPoint]) -> Double {
        zip(points, points.dropFirst()).reduce(0) { $0 + $1.0.distance($1.1) }
    }

    /// The chromatid drawn from its state. `chiasma`: where along the long arm (a fraction of it, from the
    /// centromere) a crossing over swaps its part.
    static func shape(_ c: DivChromatidKey, pair: DivPairInfo, chiasma: Double, amplitude: Double? = nil) -> DivChromatidShape {
        let k = DivMath.clamp01(c.k)
        let length = span(pair, k)
        let l1 = length * pair.centromere, l2 = length - l1
        let amp = (amplitude ?? wiggle) * pow(1 - k, 1.3)
        let w = chromatidWidth(k)
        let d1 = DivPoint.dir(c.a1), d2 = DivPoint.dir(c.a2)
        // (left of the way along it: from the short arm's end in to the centromere, then out along the long arm)
        let n1 = DivPoint(d1.y, -d1.x), n2 = DivPoint(-d2.y, d2.x)
        var n0 = n1 + n2
        n0 = n0.length < 1e-6 ? n2 : n0 * (1 / n0.length)
        let sc = chiasma * l2
        func across(_ s: Double, long: Bool) -> Double {
            let taper = min(1, s / 0.03)
            let wig = amp * sin(2 * .pi * s / wave + c.seed) * taper
            // (sisters held together at the centromere: the X's waist)
            // (and a little apart toward their ends, so a condensed chromosome reads as an X)
            let pinch = 1 - 0.55 * k * exp(-(s / 0.035) * (s / 0.035)) + 0.3 * k * k * pow(s / max(long ? l2 : l1, 0.01), 2)
            var o = c.offset * pinch
            if long, c.cross != 0 { o += c.cross * DivMath.smoothstep(sc - 0.02, sc + 0.02, s) }
            return wig + o
        }
        var pts: [DivPoint] = []
        let m1 = max(3, Int((l1 / step).rounded(.up)))
        for i in stride(from: m1, to: 0, by: -1) {
            let s = l1 * Double(i) / Double(m1)
            pts.append(c.c + d1 * s + n1 * across(s, long: false))
        }
        let centre = pts.count
        let o0 = across(0, long: false)
        pts.append(c.c + n0 * o0)
        let m2 = max(3, Int((l2 / step).rounded(.up)))
        var distal: Int?
        for i in 1...m2 {
            let s = l2 * Double(i) / Double(m2)
            if distal == nil, c.recombined > 0, s >= sc { distal = pts.count }
            pts.append(c.c + d2 * s + n2 * across(s, long: true))
        }
        let side: Double = c.offset > 0 ? 1 : (c.offset < 0 ? -1 : 0)
        let kinetochore = c.c + n0 * (o0 + side * w * 0.5)
        return DivChromatidShape(points: pts, centromere: centre, distalStart: distal, width: w, kinetochore: kinetochore)
    }
}

// MARK: - Cells

/// A shape a cell (or part of one) fills.
enum DivShape: Hashable {
    case disk(DivPoint, Double)
    /// The animal cell pinched by its cleavage furrow at y = 0: a circle of radius r whose width there is 2·waist.
    case pinched(Double, Double)
    /// A plant cell: a rounded box (centre, half width, half height, corner radius).
    case box(DivPoint, Double, Double, Double)

    func inside(_ p: DivPoint) -> Bool {
        switch self {
        case .disk(let c, let r):
            return p.distance(c) <= r
        case .pinched(let r, let waist):
            guard abs(p.y) <= r else { return false }
            let g = 1 - (1 - waist / r) * exp(-(p.y / 0.3) * (p.y / 0.3))
            return abs(p.x) <= (r * r - p.y * p.y).squareRoot() * g
        case .box(let c, let hw, let hh, let corner):
            let dx = abs(p.x - c.x) - (hw - corner), dy = abs(p.y - c.y) - (hh - corner)
            if dx <= 0 || dy <= 0 { return abs(p.x - c.x) <= hw && abs(p.y - c.y) <= hh }
            return (dx * dx + dy * dy).squareRoot() <= corner
        }
    }

    var bounds: DivRect {
        switch self {
        case .disk(let c, let r): return DivRect(minX: c.x - r, minY: c.y - r, maxX: c.x + r, maxY: c.y + r)
        case .pinched(let r, _): return DivRect(minX: -r, minY: -r, maxX: r, maxY: r)
        case .box(let c, let hw, let hh, _): return DivRect(minX: c.x - hw, minY: c.y - hh, maxX: c.x + hw, maxY: c.y + hh)
        }
    }
}

/// A cell, or the part of one that becomes a daughter: a shape kept to one side of x = 0 and/or y = 0 if asked. The
/// cut is a new wall (or membrane) once the cells have parted (`wallX`, `wallY`), else just where two parts of one cell
/// meet (not drawn).
struct DivRegion: Hashable {
    var shape: DivShape
    var keepX = 0
    var keepY = 0
    var wallX = false
    var wallY = false

    func inside(_ p: DivPoint) -> Bool {
        shape.inside(p) && (keepX == 0 || Double(keepX) * p.x >= 0) && (keepY == 0 || Double(keepY) * p.y >= 0)
    }

    /// Its centre of area (found on a grid).
    var centroid: DivPoint {
        let b = shape.bounds
        let n = 56
        var sx = 0.0, sy = 0.0, count = 0.0
        for i in 0..<n {
            for j in 0..<n {
                let p = DivPoint(b.minX + b.width * (Double(i) + 0.5) / Double(n), b.minY + b.height * (Double(j) + 0.5) / Double(n))
                if inside(p) {
                    sx += p.x
                    sy += p.y
                    count += 1
                }
            }
        }
        return count > 0 ? DivPoint(sx / count, sy / count) : b.centre
    }
}

/// A cell's outline at a keyframe: its distance from a centre in each of `samples` directions (from straight down,
/// counterclockwise), and whether the outline there is membrane (or wall), 1, or only where it meets another part of
/// the same cell, 0 (not drawn). Easing the distances and the centre between keyframes pinches one cell into two.
struct DivCellKey: Equatable {
    var centre: DivPoint
    var radii: [Double]
    var membrane: [Double]

    static let samples = 144

    static func angle(_ i: Int) -> Double { -.pi / 2 + 2 * .pi * Double(i) / Double(samples) }

    init(centre: DivPoint, radii: [Double], membrane: [Double]) {
        self.centre = centre
        self.radii = radii
        self.membrane = membrane
    }

    /// The outline of a region, round its centre of area. `side` −1 or 1: only the outline to the left (x ≤ its
    /// centre) or the right is drawn — two slots tracing the same cell, each drawing half of it, become two cells later
    /// by easing apart, with the outline drawn once meanwhile.
    init(_ region: DivRegion, side: Int = 0) {
        let g = region.centroid
        var radii: [Double] = []
        var membrane: [Double] = []
        for i in 0..<DivCellKey.samples {
            let d = DivPoint.dir(DivCellKey.angle(i))
            var t = 0.0
            let dt = 0.02
            while t < 4, region.inside(g + d * (t + dt)) { t += dt }
            var lo = t, hi = t + dt
            for _ in 0..<22 {
                let mid = (lo + hi) / 2
                if region.inside(g + d * mid) { lo = mid } else { hi = mid }
            }
            let q = g + d * (lo + 0.003)
            let wall: Bool
            if !region.shape.inside(q) { wall = true } else if region.keepX != 0, Double(region.keepX) * q.x < 0 { wall = region.wallX } else { wall = region.wallY }
            // (a cut not drawn reaches a little past the line, so the parts' fills meet without a seam)
            radii.append(lo + (wall ? 0 : 0.004))
            let mine = side == 0 || Double(side) * d.x >= -1e-9
            membrane.append(wall && mine ? 1 : 0)
        }
        self.init(centre: g, radii: radii, membrane: membrane)
    }

    /// The outline of a region, worked out once (a cell keeps its shape over several steps) and kept.
    static func cached(_ region: DivRegion, side: Int = 0) -> DivCellKey { DivOutlineCache.shared.outline(region, side: side) }

    func point(_ i: Int) -> DivPoint { centre + DivPoint.dir(DivCellKey.angle(i)) * radii[i] }

    var bounds: DivRect { (0..<radii.count).reduce(DivRect.empty) { $0.adding(point($1)) } }

    func mixed(_ o: DivCellKey, _ t: Double) -> DivCellKey {
        DivCellKey(centre: centre.mixed(o.centre, t),
                   radii: zip(radii, o.radii).map { DivMath.mix($0, $1, t) },
                   membrane: zip(membrane, o.membrane).map { DivMath.mix($0, $1, t) })
    }
}

/// Cells' outlines already worked out, for any thread (the self-check runs off the main thread).
final class DivOutlineCache: @unchecked Sendable {
    static let shared = DivOutlineCache()
    private struct Key: Hashable {
        let region: DivRegion
        let side: Int
    }
    private let lock = NSLock()
    private var outlines: [Key: DivCellKey] = [:]

    func outline(_ region: DivRegion, side: Int) -> DivCellKey {
        let key = Key(region: region, side: side)
        lock.lock()
        let known = outlines[key]
        lock.unlock()
        if let known { return known }
        let made = DivCellKey(region, side: side)
        lock.lock()
        outlines[key] = made
        lock.unlock()
        return made
    }
}

/// A nucleus: its envelope (solid, or dashed as it breaks down or forms), its nucleolus, and how firmly it gathers its
/// chromosomes inside it (a new nucleus pulls its decondensing chromosomes in).
struct DivNucleusKey: Equatable {
    var c: DivPoint = .zero
    var r: Double = 0.55
    var alpha: Double = 0
    var dashed: Double = 0
    var nucleolus: Double = 0
    var nucleolusAlpha: Double = 0
    var confine: Double = 0

    func mixed(_ o: DivNucleusKey, _ t: Double) -> DivNucleusKey {
        DivNucleusKey(c: c.mixed(o.c, t), r: DivMath.mix(r, o.r, t), alpha: DivMath.mix(alpha, o.alpha, t),
                      dashed: DivMath.mix(dashed, o.dashed, t), nucleolus: DivMath.mix(nucleolus, o.nucleolus, t),
                      nucleolusAlpha: DivMath.mix(nucleolusAlpha, o.nucleolusAlpha, t), confine: DivMath.mix(confine, o.confine, t))
    }

    /// The nucleolus sits a little off the nucleus's centre.
    var nucleolusCentre: DivPoint { c + DivPoint(0.28, -0.22) * r }
}

/// A spindle pole: where it is, the point it swings round as it moves (two keyframes with the same pivot move it round
/// it, as centrosomes move round the nucleus), and how much of a centrosome (animal cells) and its aster are drawn.
struct DivPoleKey: Equatable {
    var p: DivPoint = .zero
    var pivot: DivPoint = .zero
    var centrosome: Double = 0
    var aster: Double = 0

    func mixed(_ o: DivPoleKey, _ t: Double) -> DivPoleKey {
        var q: DivPoint
        if pivot == o.pivot {
            let a = p - pivot, b = o.p - pivot
            let r = DivMath.mix(a.length, b.length, t)
            let ang = DivMath.mixAngle(a.angle, b.angle, t)
            q = pivot + DivPoint.dir(ang) * r
        } else {
            q = p.mixed(o.p, t)
        }
        return DivPoleKey(p: q, pivot: pivot.mixed(o.pivot, t), centrosome: DivMath.mix(centrosome, o.centrosome, t),
                          aster: DivMath.mix(aster, o.aster, t))
    }
}

/// A spindle between two poles: its polar microtubules (overlapping in the middle) and its metaphase plate (an
/// imaginary plane, drawn dashed across the middle).
struct DivSpindleKey: Equatable {
    let a: Int
    let b: Int
    var polar: Double = 0
    var plate: Double = 0
    var plateLength: Double = 1.5

    func mixed(_ o: DivSpindleKey, _ t: Double) -> DivSpindleKey {
        DivSpindleKey(a: a, b: b, polar: DivMath.mix(polar, o.polar, t), plate: DivMath.mix(plate, o.plate, t),
                      plateLength: DivMath.mix(plateLength, o.plateLength, t))
    }
}

/// A line where a cell divides: a plant's cell plate (Golgi vesicles fusing from the middle outward, `half` its
/// half-length so far), or an animal's cleavage furrow (its actin–myosin ring, drawn at the waist ± `half`).
struct DivLineKey: Equatable {
    var c: DivPoint = .zero
    var horizontal = true
    var half: Double = 0
    var alpha: Double = 0

    func mixed(_ o: DivLineKey, _ t: Double) -> DivLineKey {
        DivLineKey(c: c.mixed(o.c, t), horizontal: horizontal, half: DivMath.mix(half, o.half, t), alpha: DivMath.mix(alpha, o.alpha, t))
    }

    func point(_ f: Double) -> DivPoint { horizontal ? c + DivPoint(f * half, 0) : c + DivPoint(0, f * half) }
}

/// Everything drawn at the end of a step, in cell units.
struct DivisionFrame: Equatable {
    var plant: Bool
    var cells: [DivCellKey]
    var nuclei: [DivNucleusKey]
    var poles: [DivPoleKey]
    var spindles: [DivSpindleKey]
    var cellPlates: [DivLineKey]
    var furrows: [DivLineKey]
    var chromatids: [DivChromatidKey]
    /// What the view should keep in sight (the cells).
    var bounds: DivRect

    /// Part way to another keyframe (the same parts, eased together).
    func mixed(_ o: DivisionFrame, _ t: Double) -> DivisionFrame {
        if t <= 0 { return self }
        if t >= 1 { return o }
        var f = self
        f.cells = zip(cells, o.cells).map { $0.mixed($1, t) }
        f.nuclei = zip(nuclei, o.nuclei).map { $0.mixed($1, t) }
        f.poles = zip(poles, o.poles).map { $0.mixed($1, t) }
        f.spindles = zip(spindles, o.spindles).map { $0.mixed($1, t) }
        f.cellPlates = zip(cellPlates, o.cellPlates).map { $0.mixed($1, t) }
        f.furrows = zip(furrows, o.furrows).map { $0.mixed($1, t) }
        f.chromatids = zip(chromatids, o.chromatids).map { a, b in
            var m = a
            m.c = a.c.mixed(b.c, t)
            m.a1 = DivMath.mixAngle(a.a1, b.a1, t)
            m.a2 = DivMath.mixAngle(a.a2, b.a2, t)
            m.k = DivMath.mix(a.k, b.k, t)
            m.offset = DivMath.mix(a.offset, b.offset, t)
            m.cross = DivMath.mix(a.cross, b.cross, t)
            m.recombined = DivMath.mix(a.recombined, b.recombined, t)
            m.alpha = DivMath.mix(a.alpha, b.alpha, t)
            m.grow = DivMath.mix(a.grow, b.grow, t)
            // (a fibre to the same pole fades between its opacities; one changing pole fades out, then in)
            if a.pole == b.pole {
                m.fibre = DivMath.mix(a.fibre, b.fibre, t)
            } else {
                m.fibre = t < 0.5 ? a.fibre * (1 - 2 * t) : b.fibre * (2 * t - 1)
            }
            m.pole = t < 0.5 ? a.pole : b.pole
            m.nucleus = t < 0.5 ? a.nucleus : b.nucleus
            return m
        }
        f.bounds = bounds.mixed(o.bounds, t)
        return f
    }

    /// Each chromatid drawn, gathered into its nucleus as that nucleus forms (`confine`): the chromatids of a nucleus
    /// are drawn closer to its centre, together, until they fit inside it.
    func shapes(chiasma: Double) -> [DivChromatidShape] {
        var out = chromatids.map { DivGeometry.shape($0, pair: DivGeometry.pairs[$0.id.pair], chiasma: chiasma) }
        for (j, n) in nuclei.enumerated() where n.confine > 0.001 {
            let members = chromatids.indices.filter { chromatids[$0].nucleus == j && chromatids[$0].alpha > 0.01 }
            guard !members.isEmpty else { continue }
            var far = 0.0
            for i in members { for p in out[i].points { far = max(far, p.distance(n.c)) } }
            guard far > 0 else { continue }
            let fit = min(1, 0.84 * n.r / far)
            let s = DivMath.mix(1, fit, DivMath.clamp01(n.confine))
            for i in members {
                out[i].points = out[i].points.map { n.c + ($0 - n.c) * s }
                out[i].kinetochore = n.c + (out[i].kinetochore - n.c) * s
            }
        }
        return out
    }
}

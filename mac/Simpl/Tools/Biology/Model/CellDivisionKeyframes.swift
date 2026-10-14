import Foundation

// Cell Division's keyframes (spec §5.2.4, in cell units, the spindle along y): where every chromatid, cell, nucleus,
// pole and fibre is at the end of each step of mitosis and of meiosis, for the model organism (2n = 4, or 2n = 6), in an
// animal or a plant cell, with crossing over, independent assortment and nondisjunction as set. The rules are the
// specification's — interphase threads at seeded places in a nucleus of radius 0.55, the centrosomes from (0.62, 0.62)
// round to the poles at (0, ±0.82), chromosomes on the plate with their centromeres on y = 0 and their chromatids along
// it (φ = 0), anaphase chromatids at (xᵢ·0.6, ±0.62) bent into a V 55° either side, telophase at (xᵢ·0.5, ±0.62) in new
// nuclei of radius 0.32, homologs at ±0.06 in metaphase I, two cells at (0, ±0.9) of radius 0.72, meiosis II's spindles
// across each cell with poles at (±0.55, ±0.9), four cells at (±0.62, ±0.9) of radius 0.48 — with two choices of ours
// where the rules leave room or would overlap:
// - On a plate, xᵢ = (i − (k − 1)/2)·0.22 (0.16 in meiosis II) is the least spacing: chromosomes that lie along the
//   plate are longer than that (0.42 and 0.24), so they are spread to clear each other (by 0.08), as far as the cell
//   allows; each one's long arm points outward.
// - In prophase I the homologs pair so that the two non-sister chromatids that meet are 0.05 apart (centre to centre),
//   the tetrad's four chromatids all seen (as in metaphase I, where homologs at ±0.06 put them 0.053 apart).
// Plant cells divide inside their box: the daughters are its halves (and quarters), centred at (0, ±0.475).

/// How a step's counts are worded (the specification's tables word each step its own way).
enum DivCountStyle: Equatable {
    /// One cell: "4", "8", "2n", "4c".
    case cell
    /// Prophase I: "4 (as 2 tetrads)".
    case tetrads
    /// Mitosis's anaphase: "8", "4n in one cell (two diploid sets moving apart)".
    case anaphase
    /// Mitosis's telophase: "8 (in two nuclei)", "2n per nucleus", "4c total".
    case twoNuclei
    /// After a division: "4 per cell".
    case perCell
    /// Telophase I: "2 per cell", "n (haploid)".
    case haploidCells
    /// Prophase II, metaphase II: per cell, said plainly ("2", "n").
    case perCellPlain
    /// Anaphase I: "4 (2 to each pole)", "2n → two sets of n".
    case anaphaseI
    /// Anaphase II: "4 (2 to each pole)", "n per pole" (per cell).
    case anaphaseII
}

/// Where a label's leader line points, worked out from the frame as drawn.
enum DivAnchor: Equatable {
    case point(DivPoint)
    /// A chromatid's centromere (by its index).
    case centromere(Int)
    /// Part way along a chromatid's long arm (0 at the centromere, 1 at its end).
    case arm(Int, Double)
    /// Part way along a chromatid's short arm.
    case shortArm(Int, Double)
    /// A nucleus's envelope, at an angle (radians).
    case nucleus(Int, Double)
    case nucleolus(Int)
    case pole(Int)
    /// A cell's outline at a sample.
    case cell(Int, Int)
    /// Part way from a pole (0) to a chromatid's kinetochore (1).
    case fibre(Int, Double)
    /// A spindle's polar microtubules, part way from its first pole.
    case polar(Int, Double)
    /// A spindle's metaphase plate, part way along it (−1…1).
    case plate(Int, Double)
    /// A cell plate or a furrow, part way along it (−1…1).
    case cellPlate(Int, Double)
    case furrow(Int, Double)
}

/// A label on the drawing: what it names (`part`, for its card), its words, and where it points.
struct DivLabelDef: Equatable {
    let part: String
    let text: String
    let anchor: DivAnchor
    var id: String { part + ":" + text }
}

/// A badge in a cell after a division ("2n", "n", "n+1").
struct DivBadgeDef: Equatable {
    let cell: Int
    let at: DivPoint
}

/// One step's keyframe, its labels and how its counts are worded.
struct DivisionKeyframe {
    var frame: DivisionFrame
    var labels: [DivLabelDef]
    var badges: [DivBadgeDef] = []
    var style: DivCountStyle
    /// Homologs paired as tetrads (prophase I, metaphase I).
    var paired = false
}

/// The steps of each process, in order.
enum DivStageKey: String, CaseIterable {
    case g1, s, g2, prophase, prometaphase, metaphase, anaphase, telophase, cytokinesis
    case prophase1, metaphase1, anaphase1, telophase1, prophase2, metaphase2, anaphase2, telophase2

    static let mitosis: [DivStageKey] = [.g1, .s, .g2, .prophase, .prometaphase, .metaphase, .anaphase, .telophase, .cytokinesis]
    static let meiosis: [DivStageKey] = [.g1, .s, .g2, .prophase1, .metaphase1, .anaphase1, .telophase1, .prophase2, .metaphase2, .anaphase2, .telophase2]
}

struct DivisionBuilder {
    let process: DivisionProcess
    let o: DivisionOptions

    var meiosis: Bool { process == .meiosis }
    var n: Int { o.pairs }
    /// The daughters' centres after the first division, (0, ±D); meiosis II's four cells at (±Q, ±D).
    var D: Double { o.plant ? 0.475 : 0.9 }
    var Q: Double { o.plant ? 0.475 : 0.62 }
    /// Room along a plate across the whole cell, and across a daughter cell in meiosis II.
    var room: Double { o.plant ? 1.76 : 1.84 }
    var room2: Double { o.plant ? 0.84 : 1.3 }
    /// Pole slots: mitosis 0 top, 1 bottom; meiosis 0 top (meiosis II: top cell's left), 1 top cell's right, 2 bottom
    /// (bottom cell's left), 3 bottom cell's right.
    var top: Int { 0 }
    var bottom: Int { meiosis ? 2 : 1 }

    /// Each chromosome's place in interphase: its centromere and its long arm's direction (seeded, inside the nucleus,
    /// apart from the others), the same every launch and on every platform.
    let layout: [(c: DivPoint, phi: Double)]
    /// Prophase I: a seeded turn for each tetrad (0…2π).
    let tetradAngles: [Double]
    /// Prophase II: a seeded turn for each chromosome (0…2π).
    let prophase2: [Double]

    init(_ process: DivisionProcess, _ o: DivisionOptions) {
        self.process = process
        self.o = o
        layout = DivisionBuilder.interphaseLayout(pairs: o.pairs)
        var rng = BioRandom(seed: 0xD1_7E7A_D000 + UInt64(o.pairs))
        tetradAngles = (0..<o.pairs).map { _ in 2 * .pi * rng.uniform() }
        var r2 = BioRandom(seed: 0xD1_0002_0000 + UInt64(o.pairs))
        prophase2 = (0..<(2 * o.pairs)).map { _ in 2 * .pi * r2.uniform() }
    }

    /// Seeded places for the 2n interphase threads: centromeres within 0.34 of the centre, both ends within 0.47 (the
    /// nucleus is 0.55), centromeres at least 0.14 apart.
    static func interphaseLayout(pairs n: Int) -> [(c: DivPoint, phi: Double)] {
        var rng = BioRandom(seed: 0xC311_D1F1_0000 + UInt64(n))
        var placed: [(c: DivPoint, phi: Double)] = []
        for u in 0..<(2 * n) {
            let pair = DivGeometry.pairs[u / 2]
            let span = DivGeometry.span(pair, 0)
            let l1 = span * pair.centromere, l2 = span - l1
            var chosen: (c: DivPoint, phi: Double)?
            for _ in 0..<500 {
                let r = 0.34 * rng.uniform().squareRoot(), a = 2 * .pi * rng.uniform(), phi = 2 * .pi * rng.uniform()
                let c = DivPoint(r * cos(a), r * sin(a))
                let e1 = c + DivPoint.dir(phi + .pi) * l1, e2 = c + DivPoint.dir(phi) * l2
                guard e1.length <= 0.47, e2.length <= 0.47, placed.allSatisfy({ $0.c.distance(c) >= 0.14 }) else { continue }
                chosen = (c, phi)
                break
            }
            placed.append(chosen ?? (DivPoint(0, 0), 0))
        }
        return placed
    }

    // MARK: Steps

    var stages: [DivStageKey] { meiosis ? DivStageKey.meiosis : DivStageKey.mitosis }

    func keyframes() -> [DivisionKeyframe] { stages.map { keyframe($0) } }

    func keyframe(_ s: DivStageKey) -> DivisionKeyframe {
        switch s {
        case .g1, .s, .g2: return interphase(s)
        case .prophase: return prophase()
        case .prometaphase: return prometaphase()
        case .metaphase: return metaphase()
        case .anaphase: return anaphase()
        case .telophase: return telophase()
        case .cytokinesis: return cytokinesis()
        case .prophase1: return prophase1()
        case .metaphase1: return metaphase1()
        case .anaphase1: return anaphase1()
        case .telophase1: return telophase1()
        case .prophase2: return prophase2Frame()
        case .metaphase2: return metaphase2()
        case .anaphase2: return anaphase2()
        case .telophase2: return telophase2()
        }
    }

    // MARK: Parts

    /// The chromatids, in index order, unplaced.
    func blank() -> [DivChromatidKey] {
        (0..<(4 * n)).map { i in
            var k = DivChromatidKey(id: DivChromatidID.at(i))
            k.seed = Double(k.id.chromosome) * 1.7 + 0.3
            return k
        }
    }

    func index(_ pair: Int, _ homolog: Int, _ sister: Int) -> Int { (pair * 2 + homolog) * 2 + sister }

    /// The whole cell (before it divides), as its parts that become daughters.
    func wholeCells(pinch: Double? = nil) -> [DivCellKey] {
        let shape: DivShape
        if o.plant {
            shape = .box(.zero, 0.95, 0.95, 0.14)
        } else if let w = pinch {
            shape = .pinched(1, w)
        } else {
            shape = .disk(.zero, 1)
        }
        if meiosis {
            // (an animal cell's halves each traced by two slots, each drawing its own side, until meiosis II parts them;
            // a plant cell, which does not move as it divides, in quarters whose new walls appear)
            return [(-1, 1), (1, 1), (-1, -1), (1, -1)].map {
                o.plant ? DivCellKey.cached(DivRegion(shape: shape, keepX: $0.0, keepY: $0.1))
                        : DivCellKey.cached(DivRegion(shape: shape, keepY: $0.1), side: $0.0)
            }
        }
        return [1, -1].map { DivCellKey.cached(DivRegion(shape: shape, keepY: $0)) }
    }

    /// Two daughter cells (top, bottom): mitosis's after cytokinesis, meiosis's after telophase I.
    func twoCells() -> [DivCellKey] {
        if meiosis {
            return [(-1, 1), (1, 1), (-1, -1), (1, -1)].map { s in
                o.plant
                    ? DivCellKey.cached(DivRegion(shape: .box(.zero, 0.95, 0.95, 0.14), keepX: s.0, keepY: s.1, wallY: true))
                    : DivCellKey.cached(DivRegion(shape: .disk(DivPoint(0, Double(s.1) * D), 0.72)), side: s.0)
            }
        }
        return [1, -1].map { s in
            o.plant
                ? DivCellKey.cached(DivRegion(shape: .box(.zero, 0.95, 0.95, 0.14), keepY: s, wallY: true))
                : DivCellKey.cached(DivRegion(shape: .disk(DivPoint(0, Double(s) * D), 0.72)))
        }
    }

    /// Meiosis's four cells after telophase II.
    func fourCells() -> [DivCellKey] {
        [(-1, 1), (1, 1), (-1, -1), (1, -1)].map { s in
            o.plant
                ? DivCellKey.cached(DivRegion(shape: .box(.zero, 0.95, 0.95, 0.14), keepX: s.0, keepY: s.1, wallX: true, wallY: true))
                : DivCellKey.cached(DivRegion(shape: .disk(DivPoint(Double(s.0) * Q, Double(s.1) * D), 0.48)))
        }
    }

    var slots: Int { meiosis ? 4 : 2 }

    /// The interphase nucleus (the other slots hidden in it).
    func oneNucleus(alpha: Double = 1, dashed: Double = 0, nucleolus: Double = 0.12, nucleolusAlpha: Double = 1, confine: Double = 1) -> [DivNucleusKey] {
        (0..<slots).map { i in
            DivNucleusKey(c: .zero, r: 0.55, alpha: i == 0 ? alpha : 0, dashed: dashed, nucleolus: nucleolus,
                          nucleolusAlpha: i == 0 ? nucleolusAlpha : 0, confine: i == 0 ? confine : 0)
        }
    }

    /// Nuclei at given places (each slot its own), all with the same look.
    func nuclei(_ centres: [DivPoint], r: Double, alpha: Double, dashed: Double = 0, nucleolus: Double = 0, nucleolusAlpha: Double = 0, confine: Double = 0) -> [DivNucleusKey] {
        centres.map { DivNucleusKey(c: $0, r: r, alpha: alpha, dashed: dashed, nucleolus: nucleolus, nucleolusAlpha: nucleolusAlpha, confine: confine) }
    }

    /// Poles: animal cells draw a centrosome at each (a plant's poles have none).
    func pole(_ p: DivPoint, pivot: DivPoint = .zero, centrosome: Double = 1, aster: Double = 0) -> DivPoleKey {
        DivPoleKey(p: p, pivot: pivot, centrosome: o.plant ? 0 : centrosome, aster: o.plant ? 0 : aster)
    }

    /// The poles before meiosis II: the top and bottom slots placed, their partners hidden with them.
    func poles(top t: DivPoleKey, bottom b: DivPoleKey) -> [DivPoleKey] {
        guard meiosis else { return [t, b] }
        var t2 = t, b2 = b
        t2.centrosome = 0
        t2.aster = 0
        b2.centrosome = 0
        b2.aster = 0
        return [t, t2, b, b2]
    }

    func spindles(polar: Double = 0, plate: Double = 0, polar2: Double = 0, plate2: Double = 0) -> [DivSpindleKey] {
        if meiosis {
            let l2 = o.plant ? 0.78 : 1.1
            return [DivSpindleKey(a: 0, b: 2, polar: polar, plate: plate, plateLength: 1.8),
                    DivSpindleKey(a: 0, b: 1, polar: polar2, plate: plate2, plateLength: l2),
                    DivSpindleKey(a: 2, b: 3, polar: polar2, plate: plate2, plateLength: l2)]
        }
        return [DivSpindleKey(a: 0, b: 1, polar: polar, plate: plate, plateLength: 1.8)]
    }

    /// Plant cell plates: the first division's along y = 0; meiosis II's along x = 0 in each cell.
    func cellPlates(first: Double = 0, firstAlpha: Double = 0, second: Double = 0, secondAlpha: Double = 0) -> [DivLineKey] {
        guard o.plant else { return [] }
        var out = [DivLineKey(c: .zero, horizontal: true, half: first, alpha: firstAlpha)]
        if meiosis {
            out.append(DivLineKey(c: DivPoint(0, D), horizontal: false, half: second, alpha: secondAlpha))
            out.append(DivLineKey(c: DivPoint(0, -D), horizontal: false, half: second, alpha: secondAlpha))
        }
        return out
    }

    /// The animal cleavage furrow's ring (mitosis's telophase).
    func furrows(half: Double = 0, alpha: Double = 0) -> [DivLineKey] {
        guard !o.plant, !meiosis else { return [] }
        return [DivLineKey(c: .zero, horizontal: true, half: half, alpha: alpha)]
    }

    func frame(cells: [DivCellKey], nuclei: [DivNucleusKey], poles: [DivPoleKey], spindles: [DivSpindleKey],
               cellPlates: [DivLineKey], furrows: [DivLineKey], chromatids: [DivChromatidKey]) -> DivisionFrame {
        let b = cells.reduce(DivRect.empty) { $0.union($1.bounds) }
        return DivisionFrame(plant: o.plant, cells: cells, nuclei: nuclei, poles: poles, spindles: spindles,
                             cellPlates: cellPlates, furrows: furrows, chromatids: chromatids, bounds: b)
    }

    // MARK: Plates

    /// Places along a plate, centred on 0, for chromosomes reaching `back` and `forward` along it from their
    /// centromeres: the specification's spacing at least, spread to clear each other by `gap` as far as `room` allows
    /// (closer, down to the specification's spacing, when the room runs short).
    static func plate(_ extents: [(back: Double, forward: Double)], spacing: Double, gap: Double = 0.08, room: Double) -> [Double] {
        guard !extents.isEmpty else { return [] }
        let k = extents.count
        var extra: [Double] = []
        for i in 0..<(k - 1) { extra.append(max(0, extents[i].forward + extents[i + 1].back + gap - spacing)) }
        let fixed = extents[0].back + extents[k - 1].forward + Double(k - 1) * spacing
        let wanted = extra.reduce(0, +)
        let lambda = wanted > 0 ? min(1, max(0, (room - fixed) / wanted)) : 1
        var xs = [0.0]
        for i in 0..<(k - 1) { xs.append(xs[i] + spacing + extra[i] * lambda) }
        let lo = xs[0] - extents[0].back, hi = xs[k - 1] + extents[k - 1].forward
        let shift = -(lo + hi) / 2
        return xs.map { $0 + shift }
    }

    /// A chromosome's reach either side of its centromere, condensed, when its long arm points forward or back.
    static func extent(pair p: Int, longForward: Bool) -> (back: Double, forward: Double) {
        let info = DivGeometry.pairs[p]
        let l = DivGeometry.span(info, 1)
        let l1 = l * info.centromere, l2 = l - l1
        return longForward ? (l1, l2) : (l2, l1)
    }

    /// Mitosis's metaphase plate: the chromosomes in order (long, …, long; mother's and father's alternating), each
    /// one's centromere x and its long arm's direction (outward).
    var mitosisPlate: [Int: (x: Double, a2: Double)] {
        let order: [Int] = n == 3 ? [0, 5, 2, 3, 4, 1] : [0, 3, 2, 1] // (chromosome = pair·2 + homolog)
        let k = order.count
        let outward = (0..<k).map { $0 >= k / 2 }
        let ext = (0..<k).map { DivisionBuilder.extent(pair: order[$0] / 2, longForward: outward[$0]) }
        let xs = DivisionBuilder.plate(ext, spacing: 0.22, room: room)
        var out: [Int: (x: Double, a2: Double)] = [:]
        for i in 0..<k { out[order[i]] = (xs[i], outward[i] ? 0 : .pi) }
        return out
    }

    /// Metaphase I's plate: a tetrad per pair, in pair order, long arms outward.
    var meiosisPlate: [Int: (x: Double, a2: Double)] {
        let k = n
        let order = Array(0..<n)
        let outward = (0..<k).map { $0 >= k / 2 }
        let ext = (0..<k).map { DivisionBuilder.extent(pair: order[$0], longForward: outward[$0]) }
        let xs = DivisionBuilder.plate(ext, spacing: 0.22, room: room)
        var out: [Int: (x: Double, a2: Double)] = [:]
        for i in 0..<k { out[order[i]] = (xs[i], outward[i] ? 0 : .pi) }
        return out
    }

    /// The homolog of a pair that faces the top pole in metaphase I (the mother's, unless the pair is flipped).
    func upper(_ p: Int) -> Int { o.flipped.indices.contains(p) && o.flipped[p] ? 1 : 0 }

    /// The V of a chromatid pulled toward a direction: its arms trail 55° either side of straight back, each arm
    /// taking the side nearer where it pointed.
    static func vArms(motion d: Double, from a2: Double, spread: Double = 55) -> (a1: Double, a2: Double) {
        let c1 = d + .pi + DivMath.radians(spread), c2 = d + .pi - DivMath.radians(spread)
        return abs(DivMath.turn(from: a2, to: c1)) <= abs(DivMath.turn(from: a2, to: c2)) ? (c2, c1) : (c1, c2)
    }

    // MARK: Interphase (mitosis and meiosis alike)

    func interphase(_ s: DivStageKey) -> DivisionKeyframe {
        let k: Double = s == .g2 ? 0.1 : 0
        var ks = blank()
        for i in ks.indices {
            let id = ks[i].id
            let l = layout[id.chromosome]
            ks[i].c = l.c
            ks[i].a2 = l.phi
            ks[i].a1 = l.phi + .pi
            ks[i].k = k
            ks[i].nucleus = 0
            if s == .g1 {
                if id.sister == 1 {
                    ks[i].exists = false
                    ks[i].alpha = 0
                    ks[i].grow = 0
                }
            } else {
                ks[i].offset = (id.sister == 0 ? 1 : -1) * DivGeometry.sisterOffset(k)
                ks[i].joined = true
            }
        }
        let ps: [DivPoleKey]
        if s == .g2 {
            ps = poles(top: pole(DivPoint(0.70, 0.55), aster: 0.3), bottom: pole(DivPoint(0.62, 0.62), aster: 0.3))
        } else {
            ps = poles(top: pole(DivPoint(0.62, 0.62), aster: 0.25), bottom: pole(DivPoint(0.62, 0.62), centrosome: 0))
        }
        let f = frame(cells: wholeCells(), nuclei: oneNucleus(), poles: ps, spindles: spindles(), cellPlates: cellPlates(),
                      furrows: furrows(), chromatids: ks)
        let l0 = index(0, 0, 0), s0 = index(1, 1, 0)
        var labels = [DivLabelDef(part: "nucleus", text: "Nucleus", anchor: .nucleus(0, DivMath.radians(200))),
                      DivLabelDef(part: "envelope", text: "Nuclear envelope", anchor: .nucleus(0, DivMath.radians(-35))),
                      DivLabelDef(part: "nucleolus", text: "Nucleolus", anchor: .nucleolus(0))]
        switch s {
        case .g1: labels.append(DivLabelDef(part: "chromatin", text: "Chromosomes (chromatin)", anchor: .arm(l0, 0.8)))
        case .s: labels.append(DivLabelDef(part: "replication", text: "DNA copied (S phase)", anchor: .arm(l0, 0.8)))
        default: labels.append(DivLabelDef(part: "sisters", text: "Sister chromatids", anchor: .arm(l0, 0.8)))
        }
        labels.append(DivLabelDef(part: "homologs", text: "Homolog (other parent)", anchor: .arm(index(0, 1, 0), 0.7)))
        labels.append(DivLabelDef(part: "chromatin", text: "Short chromosome", anchor: .arm(s0, 0.9)))
        if !o.plant { labels.append(DivLabelDef(part: "centrosome", text: s == .g2 ? "2 centrosomes + centrioles" : "Centrosome + centrioles", anchor: .pole(top))) }
        labels.append(o.plant ? DivLabelDef(part: "wall", text: "Cell wall", anchor: .cell(0, 112))
                              : DivLabelDef(part: "membrane", text: "Cell membrane", anchor: .cell(0, 100)))
        return DivisionKeyframe(frame: f, labels: labels, style: .cell)
    }

    /// The chromatids condensing in place (mitosis's prophase).
    func condensed(_ k: Double) -> [DivChromatidKey] {
        var ks = blank()
        for i in ks.indices {
            let id = ks[i].id
            let l = layout[id.chromosome]
            ks[i].c = l.c
            ks[i].a2 = l.phi
            ks[i].a1 = l.phi + .pi
            ks[i].k = k
            ks[i].offset = (id.sister == 0 ? 1 : -1) * DivGeometry.sisterOffset(k)
            ks[i].joined = true
            ks[i].nucleus = 0
        }
        return ks
    }

    // MARK: Mitosis

    func prophase() -> DivisionKeyframe {
        let ks = condensed(0.7)
        let ps = poles(top: pole(DivPoint(0.5, 0.72), aster: 0.8), bottom: pole(DivPoint(-0.5, 0.72), aster: 0.8))
        let f = frame(cells: wholeCells(), nuclei: oneNucleus(nucleolus: 0.07, nucleolusAlpha: 0.2), poles: ps,
                      spindles: spindles(polar: 0.5), cellPlates: cellPlates(), furrows: furrows(), chromatids: ks)
        let l0 = index(0, 0, 0)
        var labels = [DivLabelDef(part: "chromosome", text: "Chromosome (condensing)", anchor: .arm(index(1, 0, 0), 0.9)),
                      DivLabelDef(part: "sisters", text: "Sister chromatids", anchor: .arm(l0, 0.85)),
                      DivLabelDef(part: "centromere", text: "Centromere", anchor: .centromere(index(0, 1, 0))),
                      DivLabelDef(part: "envelope", text: "Nuclear envelope", anchor: .nucleus(0, DivMath.radians(-40))),
                      DivLabelDef(part: "spindle", text: "Spindle (forming)", anchor: .polar(0, 0.5))]
        if !o.plant { labels.append(DivLabelDef(part: "centrosome", text: "Centrosomes moving apart", anchor: .pole(bottom))) }
        return DivisionKeyframe(frame: f, labels: labels, style: .cell)
    }

    /// Which pole each chromatid faces from metaphase on: the sister whose side is up goes to the top pole.
    func mitosisPole(_ id: DivChromatidID, a2: Double) -> Int {
        let up = cos(a2) > 0 ? 0 : 1
        return id.sister == up ? top : bottom
    }

    func prometaphase() -> DivisionKeyframe {
        let plate = mitosisPlate
        var ks = condensed(1)
        for i in ks.indices {
            let id = ks[i].id
            let l = layout[id.chromosome]
            let target = plate[id.chromosome]!.a2
            // (on the way to the plate: half way across, the spindle's middle closer)
            ks[i].c = DivPoint(DivMath.mix(l.c.x, plate[id.chromosome]!.x, 0.5), l.c.y * 0.55)
            ks[i].a2 = DivMath.mixAngle(l.phi, target, 0.5)
            ks[i].a1 = ks[i].a2 + .pi
            ks[i].pole = mitosisPole(id, a2: target)
            ks[i].fibre = 0.8
            ks[i].nucleus = -1
        }
        let ps = poles(top: pole(DivPoint(0, 0.82), aster: 1), bottom: pole(DivPoint(0, -0.82), aster: 1))
        let f = frame(cells: wholeCells(), nuclei: oneNucleus(alpha: 0.5, dashed: 1, nucleolus: 0.04, nucleolusAlpha: 0, confine: 0),
                      poles: ps, spindles: spindles(polar: 1), cellPlates: cellPlates(), furrows: furrows(), chromatids: ks)
        let l0 = index(0, 0, 0)
        var labels = [DivLabelDef(part: "envelope", text: "Envelope breaking down", anchor: .nucleus(0, DivMath.radians(-30))),
                      DivLabelDef(part: "kinetochore", text: "Kinetochore microtubules", anchor: .fibre(l0, 0.5)),
                      DivLabelDef(part: "centromere", text: "Centromere (kinetochore)", anchor: .centromere(index(1, 0, 0))),
                      DivLabelDef(part: "chromosome", text: "Chromosome", anchor: .arm(index(0, 1, 0), 0.9))]
        labels.append(o.plant ? DivLabelDef(part: "pole", text: "Pole (no centrioles)", anchor: .pole(top))
                              : DivLabelDef(part: "centrosome", text: "Centrosome (spindle pole)", anchor: .pole(top)))
        return DivisionKeyframe(frame: f, labels: labels, style: .cell)
    }

    func metaphase() -> DivisionKeyframe {
        let plate = mitosisPlate
        var ks = condensed(1)
        for i in ks.indices {
            let id = ks[i].id
            let p = plate[id.chromosome]!
            ks[i].c = DivPoint(p.x, 0)
            ks[i].a2 = p.a2
            ks[i].a1 = p.a2 + .pi
            ks[i].pole = mitosisPole(id, a2: p.a2)
            ks[i].fibre = 1
            ks[i].nucleus = -1
        }
        let ps = poles(top: pole(DivPoint(0, 0.82), aster: 1), bottom: pole(DivPoint(0, -0.82), aster: 1))
        let f = frame(cells: wholeCells(), nuclei: oneNucleus(alpha: 0, dashed: 1, nucleolus: 0, nucleolusAlpha: 0, confine: 0),
                      poles: ps, spindles: spindles(polar: 1, plate: 1), cellPlates: cellPlates(), furrows: furrows(), chromatids: ks)
        let l0 = index(0, 0, 0)
        var labels = [DivLabelDef(part: "plate", text: "Metaphase plate", anchor: .plate(0, -1)),
                      DivLabelDef(part: "kinetochore", text: "Kinetochore microtubules", anchor: .fibre(index(1, 1, 0), 0.45)),
                      DivLabelDef(part: "spindle", text: "Spindle", anchor: .polar(0, 0.3)),
                      DivLabelDef(part: "sisters", text: "Sister chromatids", anchor: .arm(l0, 0.8)),
                      DivLabelDef(part: "centromere", text: "Centromere", anchor: .centromere(index(0, 1, 0)))]
        labels.append(o.plant ? DivLabelDef(part: "pole", text: "Spindle pole", anchor: .pole(top))
                              : DivLabelDef(part: "centrosome", text: "Centrosome", anchor: .pole(top)))
        return DivisionKeyframe(frame: f, labels: labels, style: .cell)
    }

    /// Mitosis's anaphase (`f` 0.6) and telophase (0.5): each chromatid's centromere at (xᵢ·f, ±0.62), a V — 55°
    /// either side in anaphase, closing to 25° as it gathers into its new nucleus in telophase.
    func separated(_ f: Double, k: Double, spread: Double = 55) -> [DivChromatidKey] {
        let plate = mitosisPlate
        var ks = blank()
        for i in ks.indices {
            let id = ks[i].id
            let p = plate[id.chromosome]!
            let pole = mitosisPole(id, a2: p.a2)
            let up = pole == top
            let arms = DivisionBuilder.vArms(motion: up ? .pi / 2 : -.pi / 2, from: p.a2, spread: spread)
            ks[i].c = DivPoint(p.x * f, up ? 0.62 : -0.62)
            ks[i].a1 = arms.a1
            ks[i].a2 = arms.a2
            ks[i].k = k
            ks[i].pole = pole
            ks[i].part = up ? 0 : 1
        }
        return ks
    }

    func anaphase() -> DivisionKeyframe {
        var ks = separated(0.6, k: 1)
        for i in ks.indices { ks[i].fibre = 1 }
        let ps = poles(top: pole(DivPoint(0, 0.82), aster: 1), bottom: pole(DivPoint(0, -0.82), aster: 1))
        let ns = nuclei([DivPoint(0, 0.62), DivPoint(0, -0.62)], r: 0.32, alpha: 0)
        let f = frame(cells: wholeCells(), nuclei: ns, poles: ps, spindles: spindles(polar: 1), cellPlates: cellPlates(),
                      furrows: furrows(), chromatids: ks)
        let l0 = index(0, 0, 0)
        let labels = [DivLabelDef(part: "daughterChromosome", text: "Daughter chromosome", anchor: .arm(l0, 0.85)),
                      DivLabelDef(part: "kinetochore", text: "Kinetochore microtubules", anchor: .fibre(index(1, 0, 0), 0.5)),
                      DivLabelDef(part: "polar", text: "Polar microtubules", anchor: .polar(0, 0.55)),
                      o.plant ? DivLabelDef(part: "pole", text: "Spindle pole", anchor: .pole(top))
                              : DivLabelDef(part: "centrosome", text: "Centrosome", anchor: .pole(top))]
        return DivisionKeyframe(frame: f, labels: labels, style: .anaphase)
    }

    func telophase() -> DivisionKeyframe {
        var ks = separated(0.5, k: 0.4, spread: 25)
        for i in ks.indices { ks[i].nucleus = ks[i].part }
        // (the centrosomes beside the new nuclei, which now fill the poles' places)
        let ps = poles(top: pole(DivPoint(0.38, 0.82), aster: 0.4), bottom: pole(DivPoint(0.38, -0.82), aster: 0.4))
        let ns = nuclei([DivPoint(0, 0.62), DivPoint(0, -0.62)], r: 0.32, alpha: 1, nucleolus: 0.05, nucleolusAlpha: 0.7, confine: 1)
        let f = frame(cells: wholeCells(pinch: 0.1), nuclei: ns, poles: ps, spindles: spindles(polar: 0.35),
                      cellPlates: cellPlates(first: 0.55, firstAlpha: 1), furrows: furrows(half: 0.1, alpha: 1), chromatids: ks)
        var labels = [DivLabelDef(part: "envelope", text: "Envelope re-forming", anchor: .nucleus(0, DivMath.radians(150))),
                      DivLabelDef(part: "chromosome", text: "Chromosomes decondensing", anchor: .nucleus(1, DivMath.radians(-150)))]
        labels.append(o.plant ? DivLabelDef(part: "cellplate", text: "Cell plate", anchor: .cellPlate(0, 0.9))
                              : DivLabelDef(part: "furrow", text: "Cleavage furrow", anchor: .furrow(0, 1)))
        labels.append(DivLabelDef(part: "nucleus", text: "Two nuclei", anchor: .nucleus(1, DivMath.radians(-20))))
        return DivisionKeyframe(frame: f, labels: labels, style: .twoNuclei)
    }

    func cytokinesis() -> DivisionKeyframe {
        var ks = blank()
        let plate = mitosisPlate
        let nr = o.plant ? 0.3 : 0.32
        for i in ks.indices {
            let id = ks[i].id
            let p = plate[id.chromosome]!
            let up = mitosisPole(id, a2: p.a2) == top
            let centre = DivPoint(0, up ? D : -D)
            let l = layout[id.chromosome]
            ks[i].c = centre + l.c * (nr / 0.55)
            ks[i].a2 = l.phi
            ks[i].a1 = l.phi + .pi
            ks[i].k = 0
            ks[i].cell = up ? 0 : 1
            ks[i].nucleus = up ? 0 : 1
        }
        let ps = poles(top: pole(DivPoint(0.38, D + 0.38), aster: 0.25), bottom: pole(DivPoint(0.38, -D - 0.38), aster: 0.25))
        let ns = nuclei([DivPoint(0, D), DivPoint(0, -D)], r: nr, alpha: 1, nucleolus: 0.08, nucleolusAlpha: 1, confine: 1)
        let f = frame(cells: twoCells(), nuclei: ns, poles: ps, spindles: spindles(), cellPlates: cellPlates(first: 0.95, firstAlpha: 0.55),
                      furrows: furrows(), chromatids: ks)
        let side = o.plant ? 0.6 : 0.52
        let labels = [DivLabelDef(part: "daughter", text: "Daughter cell (2n)", anchor: .cell(0, 110)),
                      DivLabelDef(part: "daughter", text: "Identical daughter (2n)", anchor: .cell(1, 40)),
                      DivLabelDef(part: "nucleus", text: "Nucleus", anchor: .nucleus(1, DivMath.radians(-160))),
                      o.plant ? DivLabelDef(part: "cellplate", text: "New cell wall", anchor: .cellPlate(0, 0.85))
                              : DivLabelDef(part: "furrow", text: "Membranes sealed", anchor: .cell(0, 0))]
        return DivisionKeyframe(frame: f, labels: labels, badges: [DivBadgeDef(cell: 0, at: DivPoint(side, D)), DivBadgeDef(cell: 1, at: DivPoint(side, -D))],
                                style: .perCell)
    }

    // MARK: Meiosis I

    /// A tetrad laid out: homolog A (`upper`) on one side of the axis and B on the other, `h` from it; sisters `s`
    /// either side of their homolog's axis; on the long pair, with crossing over, the two chromatids that meet cross
    /// beyond the chiasma.
    func tetrad(_ ks: inout [DivChromatidKey], pair p: Int, centre: DivPoint, a2: Double, k: Double, h: Double, crossing: Bool) {
        let a = upper(p), b = 1 - a
        // (A on the side the normal points to when its long arm points +x; the other way round when it points −x,
        // so A is the one facing the top pole either way)
        let sign: Double = cos(a2) >= 0 ? 1 : -1
        let nrm = DivPoint(-sin(a2), cos(a2))
        let s = DivGeometry.sisterOffset(k)
        let inner = 2 * (h - s)
        for homolog in [a, b] {
            let hs = (homolog == a ? 1.0 : -1.0) * sign
            for sister in 0...1 {
                let i = index(p, homolog, sister)
                ks[i].c = centre + nrm * (hs * h)
                ks[i].a2 = a2
                ks[i].a1 = a2 + .pi
                ks[i].k = k
                ks[i].offset = (sister == 0 ? 1 : -1) * s
                ks[i].joined = true
                // (the chromatid facing the other homolog: A's on its −hs side, B's likewise toward A)
                let facing = (sister == 0 ? 1.0 : -1.0) == -hs
                if crossing, p == 0, facing {
                    ks[i].cross = -hs * inner
                    ks[i].recombined = 1
                } else {
                    ks[i].cross = 0
                }
            }
        }
    }

    var crossing: Bool { o.crossingOver }

    /// Where crossing over left its mark: the recombinant chromatids keep their swapped part after prophase I.
    func keepRecombined(_ ks: inout [DivChromatidKey]) {
        guard crossing else { return }
        for homolog in 0...1 { ks[index(0, homolog, recombinantSister(homolog))].recombined = 1 }
    }

    func prophase1() -> DivisionKeyframe {
        var ks = condensed(0.7)
        let plate = meiosisPlate
        for p in 0..<n {
            // (the tetrads round the nucleus's middle, apart, each lying across its spoke and turned by a seed, then
            // the short way to match how it will lie on the plate)
            let spoke = 0.6 + 2 * .pi * Double(p) / Double(n)
            let centre = DivPoint.dir(spoke) * (n == 2 ? 0.17 : 0.22)
            var a2 = spoke + .pi / 2 + (tetradAngles[p] - .pi) * 0.15
            let target = plate[p]!.a2
            if abs(DivMath.turn(from: a2, to: target)) > .pi / 2 { a2 += .pi }
            let s = DivGeometry.sisterOffset(0.7)
            tetrad(&ks, pair: p, centre: centre, a2: a2, k: 0.7, h: 0.025 + s, crossing: crossing)
        }
        let ps = poles(top: pole(DivPoint(0.5, 0.72), aster: 0.8), bottom: pole(DivPoint(-0.5, 0.72), aster: 0.8))
        let f = frame(cells: wholeCells(), nuclei: oneNucleus(alpha: 0.6, dashed: 1, nucleolus: 0.05, nucleolusAlpha: 0, confine: 1),
                      poles: ps, spindles: spindles(polar: 0.5), cellPlates: cellPlates(), furrows: furrows(), chromatids: ks)
        let a = upper(0)
        var labels = [DivLabelDef(part: "tetrad", text: "Tetrad (bivalent)", anchor: .arm(index(1, upper(1), 0), 0.9)),
                      DivLabelDef(part: "homologs", text: "Homologs paired (synapsis)", anchor: .shortArm(index(0, 1 - a, 0), 0.9)),
                      DivLabelDef(part: "sisters", text: "Sister chromatids", anchor: .arm(index(0, a, 0), 0.95)),
                      DivLabelDef(part: "envelope", text: "Envelope breaking down", anchor: .nucleus(0, DivMath.radians(-40))),
                      DivLabelDef(part: "spindle", text: "Spindle forming", anchor: .polar(0, 0.5))]
        if crossing {
            labels.append(DivLabelDef(part: "chiasma", text: "Chiasma (crossing over)", anchor: .arm(index(0, a, 1), o.chiasma)))
        }
        return DivisionKeyframe(frame: f, labels: labels, style: .tetrads, paired: true)
    }

    func metaphase1() -> DivisionKeyframe {
        var ks = condensed(1)
        let plate = meiosisPlate
        for p in 0..<n {
            let pl = plate[p]!
            tetrad(&ks, pair: p, centre: DivPoint(pl.x, 0), a2: pl.a2, k: 1, h: 0.06, crossing: crossing)
            let a = upper(p)
            for homolog in 0...1 {
                for sister in 0...1 {
                    let i = index(p, homolog, sister)
                    // (nondisjunction in meiosis I: the short pair's homologs both face the top pole)
                    let toTop = homolog == a || (o.nondisjunction == .meiosisI && p == 1)
                    ks[i].pole = toTop ? top : bottom
                    ks[i].fibre = 1
                    ks[i].nucleus = -1
                }
            }
        }
        let ps = poles(top: pole(DivPoint(0, 0.82), aster: 1), bottom: pole(DivPoint(0, -0.82), aster: 1))
        let f = frame(cells: wholeCells(), nuclei: oneNucleus(alpha: 0, dashed: 1, nucleolus: 0, nucleolusAlpha: 0, confine: 0),
                      poles: ps, spindles: spindles(polar: 1, plate: 1), cellPlates: cellPlates(), furrows: furrows(), chromatids: ks)
        let a = upper(0)
        var labels = [DivLabelDef(part: "tetrad", text: "Homologous pair (tetrad)", anchor: .arm(index(n - 1, upper(n - 1), 0), 0.9)),
                      DivLabelDef(part: "plate", text: "Metaphase plate", anchor: .plate(0, -1)),
                      DivLabelDef(part: "kinetochore", text: "Kinetochore microtubules", anchor: .fibre(index(0, 1 - a, 1), 0.45))]
        labels.append(o.plant ? DivLabelDef(part: "pole", text: "Spindle pole", anchor: .pole(top))
                              : DivLabelDef(part: "centrosome", text: "Centrosome", anchor: .pole(top)))
        if crossing { labels.append(DivLabelDef(part: "chiasma", text: "Chiasma", anchor: .arm(index(0, a, 1), o.chiasma))) }
        return DivisionKeyframe(frame: f, labels: labels, style: .cell, paired: true)
    }

    /// Anaphase I (`f` 0.6) and telophase I: each homolog, both chromatids together, at (xᵢ·f, ±y), a V; with
    /// nondisjunction in meiosis I the short pair's homologs both go up, side by side.
    func homologsApart(f: Double, y: Double, k: Double) -> [DivChromatidKey] {
        var ks = blank()
        let plate = meiosisPlate
        for p in 0..<n {
            let pl = plate[p]!
            let a = upper(p)
            for homolog in 0...1 {
                let ndUp = o.nondisjunction == .meiosisI && p == 1
                let up = homolog == a || ndUp
                let dx: Double = ndUp ? (homolog == a ? -0.075 : 0.075) : 0
                let arms = DivisionBuilder.vArms(motion: up ? .pi / 2 : -.pi / 2, from: pl.a2)
                for sister in 0...1 {
                    let i = index(p, homolog, sister)
                    ks[i].c = DivPoint(pl.x * f + dx, up ? y : -y)
                    ks[i].a1 = arms.a1
                    ks[i].a2 = arms.a2
                    ks[i].k = k
                    ks[i].offset = (sister == 0 ? 1 : -1) * DivGeometry.sisterOffset(k)
                    ks[i].joined = true
                    ks[i].pole = up ? top : bottom
                    ks[i].part = up ? 0 : 1
                }
            }
        }
        keepRecombined(&ks)
        return ks
    }

    func anaphase1() -> DivisionKeyframe {
        var ks = homologsApart(f: 0.6, y: 0.62, k: 1)
        for i in ks.indices { ks[i].fibre = 1 }
        let ps = poles(top: pole(DivPoint(0, 0.82), aster: 1), bottom: pole(DivPoint(0, -0.82), aster: 1))
        let ns = (0..<4).map { i in DivNucleusKey(c: DivPoint(0, i < 2 ? D : -D), r: o.plant ? 0.36 : 0.4, alpha: 0, dashed: 1) }
        let f = frame(cells: wholeCells(), nuclei: ns, poles: ps, spindles: spindles(polar: 1), cellPlates: cellPlates(),
                      furrows: furrows(), chromatids: ks)
        let a = upper(0)
        var labels = [DivLabelDef(part: "homologs", text: "Homologs separating", anchor: .arm(index(0, 1 - a, 0), 0.9)),
                      DivLabelDef(part: "sisters", text: "Sisters stay together", anchor: .arm(index(n - 1, upper(n - 1), 0), 0.9)),
                      DivLabelDef(part: "kinetochore", text: "Kinetochore microtubules", anchor: .fibre(index(0, a, 0), 0.5))]
        if crossing { labels.append(DivLabelDef(part: "recombinant", text: "Recombinant chromatid", anchor: .arm(index(0, a, recombinantSister(a)), 0.95))) }
        if o.nondisjunction == .meiosisI { labels.append(DivLabelDef(part: "nondisjunction", text: "Nondisjunction", anchor: .arm(index(1, 1 - upper(1), 0), 0.9))) }
        return DivisionKeyframe(frame: f, labels: labels, style: .anaphaseI)
    }

    /// The sister of a homolog of the long pair that crossed over: the one facing the other homolog in the tetrad (as
    /// `tetrad` lays it out, the same in prophase I and on the plate).
    func recombinantSister(_ homolog: Int) -> Int {
        let hs: Double = (homolog == upper(0) ? 1 : -1) * (cos(meiosisPlate[0]!.a2) >= 0 ? 1 : -1)
        return -hs > 0 ? 0 : 1
    }

    func telophase1() -> DivisionKeyframe {
        var ks = homologsApart(f: 0.55, y: D + 0.08, k: 0.85)
        for i in ks.indices {
            let up = ks[i].part == 0
            ks[i].cell = up ? 0 : 2
            ks[i].part = -1
            ks[i].pole = -1
        }
        let ps = poles(top: pole(DivPoint(0, o.plant ? D + 0.3 : D + 0.46), aster: 0.25),
                       bottom: pole(DivPoint(0, o.plant ? -D - 0.3 : -D - 0.46), aster: 0.25))
        let r = o.plant ? 0.36 : 0.4
        let ns = (0..<4).map { i in DivNucleusKey(c: DivPoint(0, i < 2 ? D : -D), r: r, alpha: i % 2 == 0 ? 0.55 : 0, dashed: 1) }
        let f = frame(cells: twoCells(), nuclei: ns, poles: ps, spindles: spindles(),
                      cellPlates: cellPlates(first: 0.95, firstAlpha: 0.75), furrows: furrows(), chromatids: ks)
        let side = o.plant ? 0.6 : 0.52
        var labels = [DivLabelDef(part: "haploid", text: "Haploid cell (n)", anchor: .cell(0, 100)),
                      DivLabelDef(part: "sisters", text: "Still two chromatids each", anchor: .arm(index(0, upper(0), 0), 0.9)),
                      DivLabelDef(part: "envelope", text: "Nuclear envelope (brief)", anchor: .nucleus(2, DivMath.radians(-150)))]
        labels.append(o.plant ? DivLabelDef(part: "cellplate", text: "Cell plate → new wall", anchor: .cellPlate(0, 0.85))
                              : DivLabelDef(part: "furrow", text: "Cleavage furrow (closed)", anchor: .cell(1, 0)))
        return DivisionKeyframe(frame: f, labels: labels,
                                badges: [DivBadgeDef(cell: 0, at: DivPoint(side, D)), DivBadgeDef(cell: 2, at: DivPoint(side, -D))],
                                style: .haploidCells)
    }

    // MARK: Meiosis II

    /// The chromosomes each cell has after meiosis I, in plate order: (pair, homolog), long arm down for the first
    /// half, up for the rest.
    func secondDivisionContents() -> [(cell: Int, chromosomes: [(pair: Int, homolog: Int)])] {
        var topCell: [(pair: Int, homolog: Int)] = [], bottomCell: [(pair: Int, homolog: Int)] = []
        for p in 0..<n {
            let a = upper(p)
            if o.nondisjunction == .meiosisI && p == 1 {
                topCell.append((p, a))
                topCell.append((p, 1 - a))
            } else {
                topCell.append((p, a))
                bottomCell.append((p, 1 - a))
            }
        }
        return [(0, topCell), (2, bottomCell)]
    }

    /// Meiosis II's plate in a cell: each chromosome's place along it (relative to the cell's centre) and its long
    /// arm's direction (down for the first half, up for the rest).
    func secondPlate(_ list: [(pair: Int, homolog: Int)]) -> [(y: Double, a2: Double)] {
        let k = list.count
        let outward = (0..<k).map { $0 >= k / 2 }
        let ext = (0..<k).map { DivisionBuilder.extent(pair: list[$0].pair, longForward: outward[$0]) }
        let ys = DivisionBuilder.plate(ext, spacing: 0.16, room: room2)
        return (0..<k).map { (ys[$0], outward[$0] ? .pi / 2 : -.pi / 2) }
    }

    /// The left and right pole slots of a cell (0 top, 2 bottom).
    func sidePoles(_ cell: Int) -> (left: Int, right: Int) { cell == 0 ? (0, 1) : (2, 3) }

    /// Meiosis II's chromatids placed by a rule for each chromosome: (cell, its place in the plate, its pair and
    /// homolog) → each sister's key.
    func secondDivision(_ place: (Int, Int, Int, Int, (y: Double, a2: Double), inout DivChromatidKey) -> Void) -> [DivChromatidKey] {
        var ks = blank()
        for (cell, list) in secondDivisionContents() {
            let plate = secondPlate(list)
            for (j, ch) in list.enumerated() {
                for sister in 0...1 {
                    let i = index(ch.pair, ch.homolog, sister)
                    ks[i].cell = cell
                    ks[i].joined = true
                    place(cell, j, ch.pair, sister, plate[j], &ks[i])
                }
            }
        }
        keepRecombined(&ks)
        return ks
    }

    func secondPoles(apart: Double, aster: Double) -> [DivPoleKey] {
        [0, 1, 2, 3].map { i in
            let cy = i < 2 ? D : -D
            let x: Double = i % 2 == 0 ? -1 : 1
            let p = apart >= 1 ? DivPoint(x * 0.55, cy) : DivPoint(x * 0.32, cy + (i < 2 ? 1 : -1) * (o.plant ? 0.25 : 0.42))
            return pole(p, pivot: DivPoint(0, cy), aster: aster)
        }
    }

    func prophase2Frame() -> DivisionKeyframe {
        let counts = Dictionary(uniqueKeysWithValues: secondDivisionContents().map { ($0.cell, $0.chromosomes.count) })
        let ks = secondDivision { cell, j, _, sister, _, key in
            // (round the cell's centre like the spokes of a wheel, each lying across its spoke, turned a little by a seed)
            let cy = cell == 0 ? D : -D
            let k = max(counts[cell] ?? 1, 1)
            let jitter = prophase2[min((cell == 0 ? 0 : n) + j, prophase2.count - 1)]
            let spoke = (cell == 0 ? 0.3 : 1.9) + 2 * .pi * Double(j) / Double(k)
            let r = k == 1 ? 0.04 : (o.plant ? 0.15 : 0.21)
            key.c = DivPoint(r * cos(spoke), cy + r * sin(spoke))
            key.a2 = spoke + .pi / 2 + (jitter - .pi) * 0.12
            key.a1 = key.a2 + .pi
            key.k = 1
            key.offset = (sister == 0 ? 1 : -1) * DivGeometry.sisterOffset(1)
        }
        let r = o.plant ? 0.36 : 0.4
        let ns = (0..<4).map { i in DivNucleusKey(c: DivPoint(0, i < 2 ? D : -D), r: r, alpha: 0, dashed: 1) }
        let f = frame(cells: twoCells(), nuclei: ns, poles: secondPoles(apart: 0, aster: 0.8), spindles: spindles(polar2: 0.5),
                      cellPlates: cellPlates(first: 0.95, firstAlpha: 0), furrows: furrows(), chromatids: ks)
        let first = secondDivisionContents()[0].chromosomes[0]
        var labels = [DivLabelDef(part: "spindle", text: "Spindle (each cell)", anchor: .polar(1, 0.5)),
                      DivLabelDef(part: "chromosome", text: "Still 2 chromatids", anchor: .arm(index(first.pair, first.homolog, 0), 0.9)),
                      DivLabelDef(part: "noS", text: "No new DNA copy", anchor: .cell(2, 20))]
        if !o.plant { labels.append(DivLabelDef(part: "centrosome", text: "Centrosomes", anchor: .pole(1))) }
        return DivisionKeyframe(frame: f, labels: labels, style: .perCellPlain)
    }

    func metaphase2() -> DivisionKeyframe {
        let ks = secondDivision { cell, _, pair, sister, place, key in
            let cy = cell == 0 ? D : -D
            let poles = sidePoles(cell)
            key.c = DivPoint(0, cy + place.y)
            key.a2 = place.a2
            key.a1 = place.a2 + .pi
            key.k = 1
            key.offset = (sister == 0 ? 1 : -1) * DivGeometry.sisterOffset(1)
            // (the sister on the left — the normal of an arm pointing up faces left — goes left; nondisjunction in
            // meiosis II: the top cell's short chromosome has both kinetochores facing its left pole)
            let left = (sister == 0) == (place.a2 > 0)
            let nd = o.nondisjunction == .meiosisII && cell == 0 && pair == 1
            key.pole = nd || left ? poles.left : poles.right
            key.fibre = 1
        }
        let f = frame(cells: twoCells(), nuclei: (0..<4).map { i in DivNucleusKey(c: DivPoint(0, i < 2 ? D : -D), r: 0.4, alpha: 0, dashed: 1) },
                      poles: secondPoles(apart: 1, aster: 1), spindles: spindles(polar2: 1, plate2: 1),
                      cellPlates: cellPlates(first: 0.95, firstAlpha: 0), furrows: furrows(), chromatids: ks)
        let first = secondDivisionContents()[0].chromosomes[0]
        var labels = [DivLabelDef(part: "plate", text: "Metaphase plate", anchor: .plate(1, 0.95)),
                      DivLabelDef(part: "kinetochore", text: "Kinetochore microtubules", anchor: .fibre(index(first.pair, first.homolog, 0), 0.45)),
                      DivLabelDef(part: "sisters", text: "Sister chromatids", anchor: .arm(index(first.pair, first.homolog, 1), 0.9))]
        labels.append(o.plant ? DivLabelDef(part: "pole", text: "Spindle pole", anchor: .pole(3))
                              : DivLabelDef(part: "centrosome", text: "Centrosome", anchor: .pole(3)))
        return DivisionKeyframe(frame: f, labels: labels, style: .perCellPlain)
    }

    /// Anaphase II (`fx` 0.42 from the plate, `fy` 0.6) and telophase II: each chromatid at its pole's side, a V; with
    /// nondisjunction in meiosis II the top cell's short chromosome goes whole to its left pole.
    func sistersApart(x fx: Double, fy: Double, k: Double, joinedND: Bool) -> [DivChromatidKey] {
        secondDivision { cell, _, pair, sister, place, key in
            let cy = cell == 0 ? D : -D
            let nd = o.nondisjunction == .meiosisII && cell == 0 && pair == 1
            let left = nd || ((sister == 0) == (place.a2 > 0))
            let arms = DivisionBuilder.vArms(motion: left ? .pi : 0, from: place.a2)
            key.c = DivPoint(left ? -fx : fx, cy + place.y * fy + (nd ? (sister == 0 ? 0.07 : -0.07) : 0))
            key.a1 = arms.a1
            key.a2 = arms.a2
            key.k = k
            key.joined = nd && joinedND
            key.offset = nd && joinedND ? (sister == 0 ? 1 : -1) * DivGeometry.sisterOffset(k) : 0
            let poles = sidePoles(cell)
            key.pole = left ? poles.left : poles.right
            key.part = left ? 0 : 1
        }
    }

    func anaphase2() -> DivisionKeyframe {
        var ks = sistersApart(x: 0.42, fy: 0.6, k: 1, joinedND: true)
        for i in ks.indices { ks[i].fibre = 1 }
        let f = frame(cells: twoCells(), nuclei: (0..<4).map { i in DivNucleusKey(c: DivPoint(i % 2 == 0 ? -Q : Q, i < 2 ? D : -D), r: 0.26, alpha: 0) },
                      poles: secondPoles(apart: 1, aster: 1), spindles: spindles(polar2: 1),
                      cellPlates: cellPlates(first: 0.95, firstAlpha: 0), furrows: furrows(), chromatids: ks)
        let first = secondDivisionContents()[0].chromosomes[0]
        var labels = [DivLabelDef(part: "daughterChromosome", text: "Sisters separating", anchor: .arm(index(first.pair, first.homolog, 0), 0.9)),
                      DivLabelDef(part: "kinetochore", text: "Kinetochore microtubules", anchor: .fibre(index(first.pair, first.homolog, 1), 0.5)),
                      DivLabelDef(part: "polar", text: "Polar microtubules", anchor: .polar(2, 0.6))]
        if o.nondisjunction == .meiosisII { labels.append(DivLabelDef(part: "nondisjunction", text: "Nondisjunction", anchor: .arm(index(1, upper(1), 0), 0.9))) }
        return DivisionKeyframe(frame: f, labels: labels, style: .anaphaseII)
    }

    func telophase2() -> DivisionKeyframe {
        var ks = sistersApart(x: Q, fy: 0.4, k: 0.4, joinedND: false)
        for i in ks.indices {
            let cell = ks[i].cell + (ks[i].part == 1 ? 1 : 0)
            ks[i].cell = cell
            ks[i].nucleus = cell
            ks[i].part = -1
            ks[i].pole = -1
        }
        let nr = 0.26
        let centres = [DivPoint(-Q, D), DivPoint(Q, D), DivPoint(-Q, -D), DivPoint(Q, -D)]
        let ns = nuclei(centres, r: nr, alpha: 1, nucleolus: 0.05, nucleolusAlpha: 0.8, confine: 1)
        let ps = [0, 1, 2, 3].map { i in pole(centres[i] + DivPoint(i % 2 == 0 ? -0.24 : 0.24, i < 2 ? 0.3 : -0.3), pivot: DivPoint(0, i < 2 ? D : -D), aster: 0.2) }
        let f = frame(cells: fourCells(), nuclei: ns, poles: ps, spindles: spindles(),
                      cellPlates: cellPlates(first: 0.95, firstAlpha: 0, second: D, secondAlpha: 0.75), furrows: furrows(), chromatids: ks)
        let dy = o.plant ? 0.3 : 0.32
        let badges = (0..<4).map { DivBadgeDef(cell: $0, at: centres[$0] + DivPoint(0, $0 < 2 ? dy : -dy)) }
        // (1.3.78: with nondisjunction the cells are not all n — the label said "Four haploid cells (n)" over n + 1 and n − 1)
        let cells: String
        switch o.nondisjunction {
        case .none: cells = "Four haploid cells (n)"
        case .meiosisI: cells = "Four cells, each n + 1 or n − 1"
        case .meiosisII: cells = "Four cells: n + 1, n − 1, n and n"
        }
        var labels = [DivLabelDef(part: "gamete", text: cells, anchor: .cell(0, 100)),
                      DivLabelDef(part: "nucleus", text: "Nucleus", anchor: .nucleus(3, DivMath.radians(-30)))]
        if crossing { labels.append(DivLabelDef(part: "recombinant", text: "Recombinant chromosome", anchor: .nucleus(0, DivMath.radians(200)))) }
        return DivisionKeyframe(frame: f, labels: labels, badges: badges, style: .perCell)
    }
}

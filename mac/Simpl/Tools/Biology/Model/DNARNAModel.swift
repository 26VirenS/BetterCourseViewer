import Foundation

// The RNA page's model (spec §4.2 RNA, §6.11, §6.12): an mRNA as a labelled strip (5′ cap, 5′ UTR, the coding
// sequence from AUG to its stop, 3′ UTR, poly-A tail — the teaching gene's mature mRNA, labelled as made up); yeast
// tRNA-Phe's cloverleaf laid out by §4.2's rules (stems straight ladders, 1.0 unit between successive bases and 1.6
// between partners; the acceptor stem up, the D arm left, the anticodon arm down, the T arm right, all from a central
// junction; each loop a circle through its closing pair of radius (m + 1)/(2π) × 1.05; the variable loop between the
// anticodon and T stems on the lower right), with the codon UUC pairing antiparallel under the anticodon GAA; its 3D
// L-shape from the P atoms of PDB 1EHZ, each arm in its colour, the pairs as rungs, and the ruler from the anticodon to
// the 3′ end (72 Å, about 7 nm); and the ribosomes' facts.
//
// 3D ids: "arm:<arm>:<i>" (nucleotide i's stretch of the tube) and "arm:<arm>:rung<i>" (the pair begun at i), so
// selecting "arm:<arm>" lights a whole arm.

/// An arm of the tRNA (and the unpaired bases joining them).
enum DNATRNAArm: String, CaseIterable, Sendable {
    case acceptor, d, anticodon, variable, t, connector

    var name: String {
        switch self {
        case .acceptor: return "Acceptor stem"
        case .d: return "D arm"
        case .anticodon: return "Anticodon arm"
        case .variable: return "Variable loop"
        case .t: return "T arm (TΨC)"
        case .connector: return "Connectors"
        }
    }

    /// §4.2's colours: acceptor violet, D green, anticodon blue, variable grey, T orange (the connectors grey too).
    var colour: BioHexPair {
        switch self {
        case .acceptor: return BioPalette.strand1
        case .d: return BioHexPair("#2e9e48", "#30d158")
        case .anticodon: return BioHexPair("#1f6fd1", "#5ea8ff")
        case .variable, .connector: return BioPalette.coil
        case .t: return BioHexPair("#e07800", "#ffa133")
        }
    }

    /// The arm nucleotide i (1–76) is in.
    static func of(_ i: Int) -> DNATRNAArm {
        switch i {
        case 1...7, 66...76: return .acceptor
        case 10...25: return .d
        case 27...43: return .anticodon
        case 44...48: return .variable
        case 49...65: return .t
        default: return .connector
        }
    }
}

/// The cloverleaf: where each base goes (units: 1.0 between successive stem bases; y up), its pairs and its loops.
struct DNACloverleaf {
    struct Base: Equatable {
        /// 1–76.
        let i: Int
        let base: Character
        var x: Double
        var y: Double
    }

    struct Loop: Equatable {
        let name: String
        let cx: Double
        let cy: Double
        let radius: Double
        /// The unpaired bases on it.
        let members: ClosedRange<Int>
    }

    /// Index i − 1 is base i.
    var bases: [Base]
    /// (i, j, a G·U wobble pair).
    var pairs: [(i: Int, j: Int, wobble: Bool)]
    var loops: [Loop]
    /// The codon 5′-UUC-3′ pairing with the anticodon, base by base: its letter, where it goes, and its partner.
    var codon: [(letter: Character, x: Double, y: Double, partner: Int)]

    func base(_ i: Int) -> Base { bases[i - 1] }
}

/// A region of the mRNA strip: its name and 1-based positions (inclusive) on the mature mRNA.
struct DNAMRNARegion: Equatable {
    let name: String
    let start: Int
    let end: Int
}

enum DNARNA {
    // MARK: The mRNA strip

    /// The teaching gene's mature mRNA (§6.12, made up; cap and tail drawn, not spelled).
    static let mrna = BioGenes.teaching.matureMRNA
    /// The coding sequence's place on it: its AUG starts at nucleotide 14.
    static let cdsStart: Int = {
        let cds = BioSeq.transcribe(codingStrand: BioGenes.teaching.cds)
        guard let r = mrna.range(of: cds) else { return 1 }
        return mrna.distance(from: mrna.startIndex, to: r.lowerBound) + 1
    }()
    static var cdsEnd: Int { cdsStart + BioGenes.teaching.cds.count - 1 }

    static var regions: [DNAMRNARegion] {
        [DNAMRNARegion(name: "5′ UTR", start: 1, end: cdsStart - 1),
         DNAMRNARegion(name: "Coding sequence", start: cdsStart, end: cdsEnd),
         DNAMRNARegion(name: "3′ UTR", start: cdsEnd + 1, end: mrna.count)]
    }

    /// The coding sequence's codons: position of each, the codon, and its amino acid (* for the stop).
    static var codons: [(start: Int, codon: String, aa: Character)] {
        let cds = Array(Array(mrna)[(cdsStart - 1)...(cdsEnd - 1)])
        return stride(from: 0, to: cds.count - 2, by: 3).map { k in
            let c = String(cds[k..<(k + 3)])
            return (cdsStart + k, c, BioCode.aminoAcid(c) ?? "?")
        }
    }

    // MARK: The cloverleaf

    /// The junction's half-size: each stem's inner pair this far from the middle.
    static let junction = 3.0
    /// Between successive bases along a stem, and between partners.
    static let stemStep = 1.0
    static let pairGap = 1.6

    /// A loop of m unpaired bases: a circle through its closing pair, radius (m + 1)/(2π) × 1.05 (§4.2).
    static func loopRadius(_ m: Int) -> Double { Double(m + 1) / (2 * .pi) * 1.05 }

    static let cloverleaf: DNACloverleaf = {
        let seq = Array(BioData.trnaSequence)
        var pos = [(Double, Double)](repeating: (0, 0), count: 77)
        // (each stem: its pairs from the junction outward, the way it points, and the side its 5′ strand is on)
        let stems: [(pairs: [(Int, Int)], d: (Double, Double), p5: (Double, Double))] = [
            ([(7, 66), (6, 67), (5, 68), (4, 69), (3, 70), (2, 71), (1, 72)], (0, 1), (-1, 0)),
            ([(10, 25), (11, 24), (12, 23), (13, 22)], (-1, 0), (0, 1)),
            ([(27, 43), (28, 42), (29, 41), (30, 40), (31, 39)], (0, -1), (-1, 0)),
            ([(49, 65), (50, 64), (51, 63), (52, 62), (53, 61)], (1, 0), (0, -1)),
        ]
        var loops: [DNACloverleaf.Loop] = []
        let loopNames = ["", "D loop", "Anticodon loop", "TΨC loop"]
        for (s, stem) in stems.enumerated() {
            for (k, pr) in stem.pairs.enumerated() {
                let cx = stem.d.0 * (junction + Double(k) * stemStep), cy = stem.d.1 * (junction + Double(k) * stemStep)
                pos[pr.0] = (cx + stem.p5.0 * pairGap / 2, cy + stem.p5.1 * pairGap / 2)
                pos[pr.1] = (cx - stem.p5.0 * pairGap / 2, cy - stem.p5.1 * pairGap / 2)
            }
            guard s > 0, let outer = stem.pairs.last else { continue }
            // (the loop beyond the outer pair: from its 5′ base the long way round to its 3′ base)
            let m = outer.1 - outer.0 - 1
            let rr = loopRadius(m)
            let mid = ((pos[outer.0].0 + pos[outer.1].0) / 2, (pos[outer.0].1 + pos[outer.1].1) / 2)
            let off = (rr * rr - (pairGap / 2) * (pairGap / 2)).squareRoot()
            let c = (mid.0 + stem.d.0 * off, mid.1 + stem.d.1 * off)
            let a5 = atan2(pos[outer.0].1 - c.1, pos[outer.0].0 - c.0), a3 = atan2(pos[outer.1].1 - c.1, pos[outer.1].0 - c.0)
            var short = a3 - a5
            while short > .pi { short -= 2 * .pi }
            while short <= -.pi { short += 2 * .pi }
            let sweep = short > 0 ? short - 2 * .pi : short + 2 * .pi
            for t in 1...m {
                let a = a5 + sweep * Double(t) / Double(m + 1)
                pos[outer.0 + t] = (c.0 + rr * cos(a), c.1 + rr * sin(a))
            }
            loops.append(DNACloverleaf.Loop(name: loopNames[s], cx: c.0, cy: c.1, radius: rr, members: (outer.0 + 1)...(outer.1 - 1)))
        }
        // (the joins: U8 and A9 between the acceptor stem and the D arm, G26 between the D and anticodon arms, and the
        // variable loop 44–48 bulging to the lower right between the anticodon and T stems — each spaced evenly along a
        // gentle curve pushed away from the junction)
        func bezier(_ a: (Double, Double), _ b: (Double, Double), _ ctrl: (Double, Double), count: Int) -> [(Double, Double)] {
            let fine = (0...400).map { k -> (Double, Double) in
                let t = Double(k) / 400, u = 1 - t
                return (u * u * a.0 + 2 * u * t * ctrl.0 + t * t * b.0, u * u * a.1 + 2 * u * t * ctrl.1 + t * t * b.1)
            }
            var lengths = [0.0]
            for k in 1..<fine.count { lengths.append(lengths[k - 1] + hypot(fine[k].0 - fine[k - 1].0, fine[k].1 - fine[k - 1].1)) }
            let total = lengths.last ?? 0
            return (1...count).map { n in
                let want = total * Double(n) / Double(count + 1)
                let k = lengths.firstIndex { $0 >= want } ?? fine.count - 1
                return fine[k]
            }
        }
        func outwardControl(_ a: (Double, Double), _ b: (Double, Double), bulge: Double) -> (Double, Double) {
            let mid = ((a.0 + b.0) / 2, (a.1 + b.1) / 2)
            let len = max(hypot(mid.0, mid.1), 1e-9)
            return (mid.0 + mid.0 / len * bulge, mid.1 + mid.1 / len * bulge)
        }
        for (k, p) in bezier(pos[7], pos[10], outwardControl(pos[7], pos[10], bulge: 0.6), count: 2).enumerated() { pos[8 + k] = p }
        pos[26] = bezier(pos[25], pos[27], outwardControl(pos[25], pos[27], bulge: 0.6), count: 1)[0]
        // (the variable loop: its curve pushed out until its bases are about 1 unit apart)
        var bulge = 1.0
        for _ in 0..<40 {
            let pts = [pos[43]] + bezier(pos[43], pos[49], outwardControl(pos[43], pos[49], bulge: bulge), count: 5) + [pos[49]]
            let step = zip(pts, pts.dropFirst()).map { hypot($1.0 - $0.0, $1.1 - $0.1) }.reduce(0, +) / Double(pts.count - 1)
            bulge += (1.05 - step) * 1.5
        }
        for (k, p) in bezier(pos[43], pos[49], outwardControl(pos[43], pos[49], bulge: bulge), count: 5).enumerated() { pos[44 + k] = p }
        // (the discriminator and the CCA end, sticking up on the right of the acceptor stem)
        let top = pos[72]
        for (k, i) in (73...76).enumerated() { pos[i] = (top.0 + 0.2 * Double(k + 1), top.1 + stemStep * Double(k + 1)) }

        let bases = (1...76).map { DNACloverleaf.Base(i: $0, base: seq[$0 - 1], x: pos[$0].0, y: pos[$0].1) }
        let pairs = BioData.trna.pairs.map { pr -> (i: Int, j: Int, wobble: Bool) in
            let a = seq[pr.0 - 1], b = seq[pr.1 - 1]
            return (pr.0, pr.1, (a == "G" && b == "U") || (a == "U" && b == "G"))
        }
        // (the codon under the anticodon G34 A35 A36: each partner below its base, so the codon reads 5′-UUC-3′ from
        // right to left — antiparallel)
        let floor = (34...36).map { pos[$0].1 }.min() ?? 0
        let codon = (34...36).map { i -> (letter: Character, x: Double, y: Double, partner: Int) in
            (BioSeq.pair(seq[i - 1], rna: true), pos[i].0, floor - pairGap, i)
        }
        return DNACloverleaf(bases: bases, pairs: pairs, loops: loops, codon: codon)
    }()

    /// The element a base is in, as the specification's table names it (its card), or the connectors.
    static func element(of i: Int) -> (name: String, positions: String, pairs: String) {
        let els = BioData.trna.elements
        func named(_ test: (String) -> Bool, _ fallback: String) -> (name: String, positions: String, pairs: String) {
            let e = els.first { test($0.name) }
            return (e?.name ?? fallback, e?.positions ?? "", e?.pairs ?? "")
        }
        switch i {
        case 34...36: return named({ $0 == "Anticodon" }, "Anticodon")
        case 1...7, 66...72: return named({ $0.hasPrefix("Acceptor") }, "Acceptor stem")
        case 73...76: return named({ $0.hasPrefix("Discriminator") }, "Discriminator + CCA end")
        case 10...25: return named({ $0.hasPrefix("D stem") }, "D stem / D loop")
        case 27...43: return named({ $0.hasPrefix("Anticodon stem") }, "Anticodon stem / loop")
        case 44...48: return named({ $0.hasPrefix("Variable") }, "Variable loop")
        case 49...65: return named({ $0.hasPrefix("T stem") }, "T stem / TΨC loop")
        default: return ("Connectors", "8–9, 26", "U8 and A9 join the acceptor stem to the D arm; G26 joins the D arm to the anticodon arm")
        }
    }

    // MARK: The L-shape in 3D

    /// The measured distance from the anticodon (34–36's middle) to the 3′ end (A76), Å.
    static let anticodonTo3Prime: Double = {
        let ns = BioData.trna.nucleotides
        guard ns.count == 76 else { return 0 }
        let c = (ns[33].p + ns[34].p + ns[35].p) / 3
        return BioV.distance(c, ns[75].p)
    }()

    /// The P atoms turned (a rotation, so the fold keeps its hand) to show the L as books draw it: the elbow (where
    /// the D and T loops meet) at the top left, the acceptor arm running right to the 3′ end, the anticodon arm down.
    static let lPoints: [BioVec] = {
        let ps = BioData.trna.nucleotides.map(\.p)
        guard ps.count == 76 else { return ps }
        let elbow = (ps[17] + ps[18] + ps[54] + ps[55]) / 4
        let anticodon = (ps[33] + ps[34] + ps[35]) / 3
        let e1 = BioV.unit(ps[75] - elbow)
        let down = anticodon - elbow
        let e2 = BioV.unit(-(down - e1 * BioV.dot(e1, down)))
        let e3 = BioV.cross(e1, e2)
        return ps.map { p in BioVec(BioV.dot(p, e1), BioV.dot(p, e2), BioV.dot(p, e3)) }
    }()

    static func lShape(dark: Bool) -> DNAScene {
        guard lPoints.count == 76 else { return DNAScene(content: BioSceneContent(), marks: []) }
        let pts = lPoints
        let samples = 6
        let path = BioPath.catmullRom(pts, samples: samples)
        // (nucleotide i's stretch: half a span either side of its P)
        let ranges = (0..<76).map { k in max(0, k * samples - samples / 2)...min(path.count - 1, k * samples + samples / 2) }
        let pieces = DNAMesh.tubePieces(path, radius: 1.5, sides: 12, ranges: ranges)
        var items: [BioSceneItem] = []
        for (k, mesh) in pieces.enumerated() {
            let arm = DNATRNAArm.of(k + 1)
            items.append(BioSceneItem(id: "arm:\(arm.rawValue):\(k + 1)", mesh: mesh, material: BioMaterial(arm.colour.hex(dark: dark), roughness: 0.45)))
        }
        // (each pair a rung between its P atoms pulled 35% toward each other — a cartoon of the paired bases)
        let rungHex = dark ? "#d1d1d6" : "#8e8e93"
        for (i, j) in BioData.trna.pairs {
            let a = pts[i - 1], b = pts[j - 1]
            let arm = DNATRNAArm.of(i)
            items.append(BioSceneItem(id: "arm:\(arm.rawValue):rung\(i)", mesh: BioShapes.cylinder(from: a + (b - a) * 0.175, to: b + (a - b) * 0.175, radius: 0.55, segments: 10),
                                      material: BioMaterial(rungHex, roughness: 0.5)))
        }
        // (the ruler: anticodon to 3′ end)
        let anticodon = (pts[33] + pts[34] + pts[35]) / 3
        let end = pts[75]
        let ink = dark ? "#d1d1d6" : "#3a3a3c"
        var ruler = DNAMesh.line(anticodon, end, radius: 0.18)
        let dir = BioV.unit(end - anticodon)
        var side = BioV.cross(dir, BioVec(0, 0, 1))
        if BioV.length(side) < 0.1 { side = BioV.cross(dir, BioVec(0, 1, 0)) }
        side = BioV.unit(side)
        for p in [anticodon, end] { ruler.append(DNAMesh.line(p - side * 1.6, p + side * 1.6, radius: 0.18)) }
        items.append(BioSceneItem(id: nil, mesh: ruler, material: BioMaterial(ink, unlit: true)))
        // (the words beside the ruler, running away from it: centred on a point beside a slanting ruler, it crossed
        // their middle)
        let marks = [DNATextMark(text: "≈7 nm (\(Int(anticodonTo3Prime.rounded())) Å)", pos: (anticodon + end) / 2 + side * 3, size: 3.4, hex: ink,
                                 align: side.x < 0 ? .trailing : .leading, onTop: true)]

        func near(_ arm: DNATRNAArm) -> (Int, BioVec)? {
            let members = (1...76).filter { DNATRNAArm.of($0) == arm }
            guard !members.isEmpty else { return nil }
            let c = members.map { pts[$0 - 1] }.reduce(BioVec(0, 0, 0), +) / Double(members.count)
            guard let i = members.min(by: { BioV.distance(pts[$0 - 1], c) < BioV.distance(pts[$1 - 1], c) }) else { return nil }
            return (i, pts[i - 1])
        }
        var labels: [BioLabel3D] = []
        for arm in [DNATRNAArm.acceptor, .d, .anticodon, .variable, .t] {
            if let (_, p) = near(arm) { labels.append(BioLabel3D(id: "arm:\(arm.rawValue)", text: arm.name, anchor: p, area: 2)) }
        }
        labels.append(BioLabel3D(id: "arm:anticodon:35", text: "Anticodon (GAA)", anchor: pts[34], area: 3))
        labels.append(BioLabel3D(id: "arm:acceptor:76", text: "Amino acid attaches here (3′ CCA)", anchor: pts[75], area: 3))
        return DNAScene(content: BioSceneContent(items: items, labels: labels), marks: marks)
    }

    /// The arm a 3D id names ("arm:d:14" → .d).
    static func arm(_ id: String?) -> DNATRNAArm? {
        guard let id else { return nil }
        let f = id.split(separator: ":")
        guard f.count >= 2, f[0] == "arm" else { return nil }
        return DNATRNAArm(rawValue: String(f[1]))
    }

    // MARK: Ribosomes (§4.2: rRNA and ribosomes; Svedberg values are not additive)

    static let ribosomes: [(name: String, whole: String, small: String, large: String)] = [
        ("Bacteria", "70S", "30S: 16S rRNA + about 21 proteins", "50S: 23S and 5S rRNA + about 33 proteins"),
        ("Eukaryotes (cytosol)", "80S", "40S: 18S rRNA", "60S: 28S, 5.8S and 5S rRNA"),
    ]

    // MARK: Checks

    static func checks(_ r: inout BioCheckReport) {
        // (the mRNA strip: the coding sequence from its AUG at 14 to its stop, read to MAFSDWQKG)
        let protein = String(codons.map(\.aa).filter { $0 != "*" })
        r.check("dna.mRNA", "start\(cdsStart),\(protein)", cdsStart == 14 && codons.first?.codon == "AUG" && codons.last?.aa == "*"
                && protein == BioGenes.teaching.protein && regions.map { $0.end - $0.start + 1 }.reduce(0, +) == mrna.count
                && !mrna.contains("T"))

        let cl = cloverleaf
        func dist(_ a: DNACloverleaf.Base, _ b: DNACloverleaf.Base) -> Double { hypot(a.x - b.x, a.y - b.y) }
        // (stems: partners 1.6 apart, successive pairs 1.0)
        let pairsOK = cl.pairs.allSatisfy { abs(dist(cl.base($0.i), cl.base($0.j)) - pairGap) < 1e-9 }
        let stemRuns: [[Int]] = [Array(1...7), Array(66...72), Array(10...13), Array(22...25), Array(27...31), Array(39...43), Array(49...53), Array(61...65)]
        let stepsOK = stemRuns.allSatisfy { run in zip(run, run.dropFirst()).allSatisfy { abs(dist(cl.base($0), cl.base($1)) - stemStep) < 1e-9 } }
        r.check("dna.cloverleaf.stems", "\(cl.pairs.count)pairs,\(cl.pairs.filter(\.wobble).count)wobble",
                pairsOK && stepsOK && cl.pairs.count == 21 && cl.pairs.filter(\.wobble).map(\.i) == [4])
        // (the arms' directions: acceptor up, D left, anticodon down, T right)
        let dirs = cl.base(1).y > cl.base(7).y && cl.base(13).x < cl.base(10).x && cl.base(31).y < cl.base(27).y && cl.base(53).x > cl.base(49).x
            && cl.base(76).y > cl.base(72).y && cl.base(76).x > cl.base(1).x
        // (loops: radius (m + 1)/(2π) × 1.05, the closing pair and every loop base on the circle)
        let loopsOK = cl.loops.count == 3 && cl.loops.allSatisfy { l in
            let m = l.members.count
            let on = (l.members.lowerBound - 1...l.members.upperBound + 1).allSatisfy { abs(hypot(cl.base($0).x - l.cx, cl.base($0).y - l.cy) - l.radius) < 1e-9 }
            return abs(l.radius - loopRadius(m)) < 1e-12 && on
        }
        r.check("dna.cloverleaf.arms", "loops\(cl.loops.map { String(format: "%.3f", $0.radius) }.joined(separator: "/"))", dirs && loopsOK)
        // (no two bases closer than 0.8 units — a 7-base loop's radius from §4.2's formula spaces them 0.84 apart, so
        // the drawing's bases are 0.76 across; the variable loop's bases about 1 unit apart)
        var closest = 99.0
        for a in cl.bases { for b in cl.bases where b.i > a.i { closest = min(closest, dist(a, b)) } }
        let variable = (43...49).map { cl.base($0) }
        let vSteps = zip(variable, variable.dropFirst()).map { dist($0, $1) }
        r.check("dna.cloverleaf.spacing", String(format: "closest%.2f,variable%.2f-%.2f", closest, vSteps.min() ?? 0, vSteps.max() ?? 0),
                closest >= 0.8 && (vSteps.min() ?? 0) > 0.85 && (vSteps.max() ?? 9) < 1.3 && variable.dropFirst().dropLast().allSatisfy { $0.x > 0 && $0.y < 0 })
        // (the anticodon G34 A35 A36 left to right at the bottom; the codon under it C U U, so 5′-UUC-3′ read right to left)
        let ac = (34...36).map { cl.base($0) }
        let codon = String(cl.codon.map(\.letter))
        r.check("dna.cloverleaf.anticodon", "\(String(ac.map(\.base)))/\(codon)", String(ac.map(\.base)) == "GAA" && ac[0].x < ac[1].x && ac[1].x < ac[2].x
                && codon == "CUU" && String(codon.reversed()) == "UUC" && BioCode.aminoAcid("UUC") == "F"
                && cl.codon.allSatisfy { c in c.y < cl.base(c.partner).y })
        // (3D: 72 Å from the anticodon to the 3′ end; each arm's colour; a rung per pair; the labels name items)
        r.near("dna.tRNA.ruler", anticodonTo3Prime, 72, 1.0, digits: 1)
        let lp = lPoints
        let turned = BioV.distance((lp[33] + lp[34] + lp[35]) / 3, lp[75])
        let elbow = (lp[17] + lp[18] + lp[54] + lp[55]) / 4
        let acL = (lp[33] + lp[34] + lp[35]) / 3
        let arms = BioV.angle(lp[75] - elbow, acL - elbow)
        r.check("dna.tRNA.L-turned", String(format: "%.2f,arms%.0f°", turned, arms), abs(turned - anticodonTo3Prime) < 1e-9
                && lp[75].x > elbow.x && acL.y < elbow.y && abs(lp[75].y - elbow.y) < 1e-9 && arms > 70 && arms < 110)
        let l = lShape(dark: false)
        let tube = l.content.items.filter { ($0.id ?? "").split(separator: ":").count == 3 && !($0.id ?? "").contains("rung") }
        let rungs = l.content.items.filter { ($0.id ?? "").contains("rung") }
        let labelled = l.content.labels.allSatisfy { lab in l.content.items.contains { BioSceneContent.matches($0.id, lab.id) } }
        let colours = Set(tube.map(\.material.hex))
        r.check("dna.tRNA.L", "\(tube.count)pieces,\(rungs.count)rungs,\(colours.count)colours", tube.count == 76 && rungs.count == 21 && colours.count == 5
                && labelled && arm("arm:d:14") == .d && DNATRNAArm.of(35) == .anticodon && DNATRNAArm.of(76) == .acceptor)
    }
}

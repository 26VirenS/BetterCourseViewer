import Foundation

// DNA & RNA's own model (spec §4.2): what the tool works out without a window, Foundation only, so the Mac app's
// workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of Biology/Model. The pieces each
// page draws are in files of their own beside this one — the double helix (DNAHelixModel.swift), the nucleotides and
// the bases in 2D (DNANucleotideModel.swift), a sequence's facts (DNASequenceModel.swift), the replication fork's steps
// (DNAReplicationModel.swift) and RNA: the mRNA strip, the tRNA cloverleaf and its 3D L-shape (DNARNAModel.swift).
// This file holds what they share — the atoms' look handed in by the view, text placed in a scene — and the tool's
// self-check lines (`dnaChecks`), which the self-check (BioSelfCheck.run) calls with the other tools'.

/// What DNA & RNA starts with and keeps to.
enum DNAModel {
    /// The helix's sequence until the student types another (§4.2): 20 bp.
    static let defaultSequence = "ATGCGTACGTTAGCCATGCA"
    /// The most base pairs the helix draws (§4.2: 1–40).
    static let helixMax = 40
    /// The Sequence page's cap (§3.7: 3,000 nt for DNA & RNA).
    static let sequenceCap = 3000

    /// The bases the helix can draw from a sequence: A, C, G and T, the first 40 (an N, or anything else, left out).
    static func helixBases(_ s: String) -> String {
        String(s.uppercased().filter { "ACGT".contains($0) }.prefix(helixMax))
    }

    /// A random strand of `length` bases from a seed (the Sequence page's "Random": 20 bp seeded by the time).
    static func random(length: Int = 20, seed: UInt64) -> String {
        var r = BioRandom(seed: seed)
        let bases = Array("ACGT")
        return String((0..<max(length, 0)).map { _ in bases[r.int(below: 4)] })
    }

    /// A base's name ("adenine").
    static func baseName(_ b: Character) -> String {
        switch b {
        case "A": return "adenine"
        case "C": return "cytosine"
        case "G": return "guanine"
        case "T": return "thymine"
        case "U": return "uracil"
        default: return "unknown base"
        }
    }

    /// A (adenine) and G (guanine) are purines, with two rings; C, T and U are pyrimidines, with one (§7 item 6).
    static func isPurine(_ b: Character) -> Bool { b == "A" || b == "G" }
}

/// How atoms look, handed in by the view from the chemistry tables (MBChem: CPK colours, covalent and van der Waals
/// radii), so the scenes made here draw atoms exactly as the chemistry builder does.
struct DNAAtomLook: Sendable {
    var hex: [String: String]
    var covalent: [String: Double]
    var vdw: [String: Double]

    /// The elements the nucleic acids have.
    static let elements = ["H", "C", "N", "O", "P"]

    /// Close to the chemistry tables' values, for the checks (no window to ask MBChem in).
    static let standard = DNAAtomLook(hex: ["H": "#f2f2f2", "C": "#909090", "N": "#3050f8", "O": "#ff0d0d", "P": "#ff8000"],
                                      covalent: ["H": 0.31, "C": 0.76, "N": 0.71, "O": 0.66, "P": 1.07],
                                      vdw: ["H": 1.2, "C": 1.7, "N": 1.55, "O": 1.52, "P": 1.8])

    func colour(_ element: String) -> String { hex[element] ?? "#c8c8c8" }

    /// The drawn radius, as the chemistry builder has it for the style.
    func radius(_ element: String, _ style: BioAtomStyle) -> Double {
        BioMoleculeMeshes.radius(style: style, covalent: covalent[element] ?? 0.75, vdw: vdw[element] ?? 1.7)
    }
}

/// Words placed in a 3D scene — a base's letter, a strand's 5′ and 3′, a measurement, a carbon's number — which the
/// view makes text facing the camera (the chemistry builder's atom labels).
struct DNATextMark: Equatable, Sendable {
    enum Align: Sendable { case leading, centre, trailing }

    var text: String
    var pos: BioVec
    /// Its height (Å).
    var size: Double
    var hex: String
    var align: Align = .centre
    /// Drawn over whatever is in front of it (callouts outside the model); else hidden behind nearer things.
    var onTop = false
    /// Shown only while the camera is within this many ångströms (a base's letter: 60 Å).
    var nearOnly: Double? = nil
    /// Moved this far toward the camera, so it shows on the near side of what it labels.
    var lift = 0.0
    var bold = true
}

/// A scene of the tool's: its items and labels (BioSceneContent), and the words placed in it.
struct DNAScene: Sendable {
    var content: BioSceneContent
    var marks: [DNATextMark]
}

// MARK: - Shared geometry

enum DNAMesh {
    /// A cone from the middle of its base to its tip (an arrowhead along a backbone), its base capped.
    static func cone(from base: BioVec, to tip: BioVec, radius r: Double, segments: Int = 18) -> BioMesh {
        let len = BioV.distance(base, tip)
        guard len > 1e-9 else { return BioMesh() }
        var m = BioMesh()
        let n = max(3, segments)
        for j in 0..<n {
            let p0 = 2 * Double.pi * Double(j) / Double(n), p1 = 2 * Double.pi * Double(j + 1) / Double(n), pm = (p0 + p1) / 2
            // (each side face its own tip vertex, so the normals lean as the face does)
            func normal(_ p: Double) -> BioVec { BioV.unit(BioVec(sin(p) * len, r, cos(p) * len)) }
            let a = m.vertex(BioVec(sin(p0) * r, 0, cos(p0) * r), normal(p0))
            let b = m.vertex(BioVec(sin(p1) * r, 0, cos(p1) * r), normal(p1))
            let t = m.vertex(BioVec(0, len, 0), normal(pm))
            // (counter-clockwise seen from outside: a → b → tip, with φ running from +z toward +x)
            m.triangle(a, b, t)
            m.flatTriangle(BioVec(0, 0, 0), BioVec(sin(p0) * r, 0, cos(p0) * r), BioVec(sin(p1) * r, 0, cos(p1) * r), facing: BioVec(0, -1, 0))
        }
        return m.transformed(BioFrame.along(tip - base, at: base))
    }

    /// A tube along a path already sampled (no spline of its own), cut into pieces: each range of sample indices
    /// (inclusive) a mesh of its own, all sharing the path's rotation-minimising frames, so the pieces meet without a
    /// seam and each can be picked and coloured on its own (a nucleotide's stretch of backbone, an arm of tRNA).
    static func tubePieces(_ path: [BioVec], radius: Double, sides: Int = 12, ranges: [ClosedRange<Int>],
                           capStart: Bool = true, capEnd: Bool = true) -> [BioMesh] {
        guard path.count >= 2 else { return ranges.map { _ in BioMesh() } }
        let frames = BioPath.frames(path)
        let ns = max(3, sides)
        func ring(_ k: Int, into m: inout BioMesh) {
            let (t, r) = frames[k]
            let s = BioV.cross(t, r)
            for j in 0..<ns {
                let a = 2 * Double.pi * Double(j) / Double(ns)
                let n = r * cos(a) + s * sin(a)
                m.vertex(path[k] + n * radius, n)
            }
        }
        func cap(_ k: Int, sign: Double, into m: inout BioMesh) {
            let (t, r) = frames[k]
            let s = BioV.cross(t, r)
            for j in 0..<ns {
                let a0 = 2 * Double.pi * Double(j) / Double(ns), a1 = 2 * Double.pi * Double(j + 1) / Double(ns)
                m.flatTriangle(path[k], path[k] + (r * cos(a0) + s * sin(a0)) * radius, path[k] + (r * cos(a1) + s * sin(a1)) * radius, facing: t * sign)
            }
        }
        return ranges.map { range in
            var m = BioMesh()
            let lo = max(range.lowerBound, 0), hi = min(range.upperBound, path.count - 1)
            guard hi > lo else { return m }
            for k in lo...hi { ring(k, into: &m) }
            for k in 0..<(hi - lo) {
                for j in 0..<ns {
                    let a = UInt32(k * ns + j), b = UInt32((k + 1) * ns + j)
                    let c = UInt32((k + 1) * ns + (j + 1) % ns), d = UInt32(k * ns + (j + 1) % ns)
                    m.triangle(a, d, c)
                    m.triangle(a, c, b)
                }
            }
            if capStart && lo == 0 { cap(0, sign: -1, into: &m) }
            if capEnd && hi == path.count - 1 { cap(hi, sign: 1, into: &m) }
            return m
        }
    }

    /// A box with rounded edges, centred at the origin with these half extents (a base in the helix's cartoon).
    static func roundedBox(_ half: BioVec, corner: Double) -> BioMesh {
        BioShapes.radial(rings: 12, segments: 24, radius: BioShapes.roundedBoxRadius(half, corner: corner))
    }

    /// A straight line drawn as a thin cylinder (a measurement's dimension line, its ticks).
    static func line(_ a: BioVec, _ b: BioVec, radius: Double = 0.12) -> BioMesh {
        BioShapes.cylinder(from: a, to: b, radius: radius, segments: 8)
    }
}

// MARK: - The self-check

extension BioSelfCheck {
    /// DNA & RNA's own `bio-check` lines (§10.1 item 5 and §4.2's numbers): the helix's cartoon and grooves, the
    /// nucleotides' parts, the bases in 2D, a sequence's facts, replication's steps, the cloverleaf's layout and the
    /// tRNA's L-shape.
    static func dnaChecks(_ r: inout BioCheckReport) {
        DNAHelix.checks(&r)
        DNANucleotides.checks(&r)
        DNASequenceFacts.checks(&r)
        DNAReplication.checks(&r)
        DNARNA.checks(&r)
        // (the random strand: seeded, so the same seed gives the same strand, 20 bases of A, C, G and T)
        let a = DNAModel.random(seed: 42), b = DNAModel.random(seed: 42), c = DNAModel.random(seed: 43)
        r.check("dna.random", a, a == b && a != c && a.count == 20 && a.allSatisfy { "ACGT".contains($0) })
        let kept = String(DNAModel.helixBases("atgnnc" + String(repeating: "A", count: 50)).prefix(6))
        r.same("dna.helix-bases", kept + "/\(DNAModel.helixBases(String(repeating: "G", count: 60)).count)", "ATGCAA/40")
    }
}

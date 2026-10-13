import Foundation

// The Biomolecule Builder's monomers and small molecules (spec §4.4, §6.3, §6.6, §6.8, §6.9): an amino acid in its pH 7
// or neutral form, its parts (amino group, α-carbon, carboxyl group, R group), its chirality by the CORN rule and a 2D
// sketch of its R group; a nucleotide's phosphate, sugar and base; a DNA strand built from the B-DNA model with its
// phosphodiester bonds; a sugar's ring, its numbered carbons, whether it is α or β, a disaccharide's glycosidic bond
// and its hydrolysis into two monosaccharides; a lipid's hydrophilic and hydrophobic parts, its ester bonds and its cis
// double bond; and the polysaccharides drawn as hexagon glyphs. Plain arithmetic on the shared data (BioData).

/// A part of a molecule, as "Colour by Part" colours it and a label names it.
struct BiomolPart: Hashable, Sendable {
    let id: String
    let name: String
    let colour: BioHexPair
}

enum BiomolParts {
    // Amino acids
    static let amino = BiomolPart(id: "amino", name: "Amino group", colour: BioHexPair("#1f6fd1", "#5ea8ff"))
    static let alpha = BiomolPart(id: "alpha", name: "α-carbon", colour: BioHexPair("#7d5cf5", "#a58bff"))
    static let carboxyl = BiomolPart(id: "carboxyl", name: "Carboxyl group", colour: BioHexPair("#d63a34", "#ff6961"))
    static let rGroup = BiomolPart(id: "r", name: "R group (side chain)", colour: BioHexPair("#e07800", "#ffa133"))
    // Peptides
    static let backbone = BiomolPart(id: "backbone", name: "Backbone", colour: BioHexPair("#5b6b8c", "#a3b1cf"))
    static let sideChain = BiomolPart(id: "side", name: "R groups (side chains)", colour: BioHexPair("#e07800", "#ffa133"))
    // Nucleotides
    static let phosphate = BiomolPart(id: "phosphate", name: "Phosphate", colour: BioHexPair("#7d5cf5", "#a58bff"))
    static let sugar = BiomolPart(id: "sugar", name: "Sugar", colour: BioHexPair("#3aa3b8", "#6fd3e6"))
    static let base = BiomolPart(id: "base", name: "Base", colour: BioHexPair("#2e9e48", "#30d158"))
    // Lipids
    static let hydrophilic = BiomolPart(id: "head", name: "Hydrophilic head", colour: BioHexPair("#1f6fd1", "#5ea8ff"))
    static let hydrophobic = BiomolPart(id: "tail", name: "Hydrophobic tail", colour: BioHexPair("#c08a2b", "#f0b44a"))
    // Neutral grey (a peptide's backbone when coloured by class)
    static let grey = BioHexPair("#8e8e93", "#aeaeb2")
    /// Highlighted bonds (peptide, glycosidic, ester, phosphodiester) and the cis double bond.
    static let bondHighlight = BioHexPair("#e8508a", "#ff7aa8")
}

/// An H atom's heavy atom (by the bonds), so parts can follow their heavy atoms.
enum BiomolGraph {
    static func heavyNeighbour(_ m: BioMolecule) -> [String: String] {
        var out: [String: String] = [:]
        let element = Dictionary(m.atoms.map { ($0.name, $0.element) }, uniquingKeysWith: { a, _ in a })
        for b in m.bonds {
            if element[b.a] == "H", element[b.b] != "H" { out[b.a] = b.b }
            if element[b.b] == "H", element[b.a] != "H" { out[b.b] = b.a }
        }
        return out
    }

    static func neighbours(_ m: BioMolecule) -> [String: [String]] {
        var adj: [String: [String]] = [:]
        for b in m.bonds {
            adj[b.a, default: []].append(b.b)
            adj[b.b, default: []].append(b.a)
        }
        return adj
    }
}

// MARK: - Amino acids (§6.2, §6.3)

enum BiomolAminoAcid {
    /// The template by one-letter code.
    static func template(_ one: Character) -> BioMolecule? { BioData.residue(String(one)) }

    /// The atoms of a free amino acid in a form: the zwitterion (pH 7: NH₃⁺, COO⁻, ionised side chains) or the neutral
    /// form textbooks draw (NH₂, COOH, and Lys, Arg and His uncharged).
    static func atoms(_ t: BioMolecule, ph7: Bool) -> [BioAtomRecord] {
        if ph7 { return t.atoms(ph7: true) }
        let removed = BiomolPeptideBuilder.neutralRemovals[t.code] ?? []
        return t.atoms(ph7: false).filter { !removed.contains($0.name) }
    }

    /// The free amino acid as a 3D view draws it (each atom named; tag its three-letter code); mirrored (z → −z) it is
    /// the D-form.
    static func model(_ one: Character, ph7: Bool, mirrored: Bool = false) -> BioMoleculeModel {
        guard let t = template(one) else { return BioMoleculeModel() }
        let tag = BioAminoAcids.byOne[one]?.three ?? t.code
        var m = BioMoleculeModel()
        var ids: [String: Int] = [:]
        for a in atoms(t, ph7: ph7) { ids[a.name] = m.add(a.element, a.pos, name: a.name, tag: tag) }
        for b in t.bonds { if let x = ids[b.a], let y = ids[b.b] { m.bond(x, y, order: b.order) } }
        return mirrored ? m.mirrored() : m
    }

    /// The charges a form carries, by atom name (pH 7: N +1, OXT −1, and the side chain's; neutral: none).
    static func charges(_ one: Character, ph7: Bool) -> [String: Int] {
        guard ph7, let t = template(one) else { return [:] }
        var c = t.ph7?.charges ?? [:]
        if c["N"] == nil { c["N"] = 1 }
        if c["OXT"] == nil { c["OXT"] = -1 }
        return c
    }

    /// The part an atom of an amino acid belongs to: the amino group (N and its H), the α-carbon (CA and its H; Gly's
    /// other H is its R group), the carboxyl group (C, O, OXT and HXT) or the R group (the rest; Pro's ring, which bonds
    /// back to its N).
    static func part(_ name: String, in t: BioMolecule) -> BiomolPart {
        let heavy = BiomolGraph.heavyNeighbour(t)[name] ?? name
        switch heavy {
        case "N": return BiomolParts.amino
        case "C", "O", "OXT": return BiomolParts.carboxyl
        case "CA":
            if t.code == "GLY" && name == glycineRGroupH(t) { return BiomolParts.rGroup }
            return BiomolParts.alpha
        default: return BiomolParts.rGroup
        }
    }

    /// Glycine's "R group": the H on CA where every other amino acid has its CB (z < 0 in the template's frame).
    static func glycineRGroupH(_ t: BioMolecule) -> String {
        let hs = t.atoms.filter { $0.element == "H" && BiomolGraph.heavyNeighbour(t)[$0.name] == "CA" }
        return hs.min(by: { $0.pos.z < $1.pos.z })?.name ?? "HA3"
    }

    /// The CORN rule for a model: looking from the H on Cα toward Cα, do CO → R → N run clockwise (the L-form)? nil for
    /// glycine (achiral) or a model without those atoms.
    static func cornClockwise(_ m: BioMoleculeModel) -> Bool? {
        func pos(_ n: String) -> BioVec? { m.atoms.first { $0.name == n }?.pos }
        guard let ca = pos("CA"), let c = pos("C"), let n = pos("N"), let cb = pos("CB"), let ha = pos("HA") else { return nil }
        let view = ca - ha
        let a = c - ca, b = cb - ca, d = n - ca
        // (seen looking along `view`, a turn from u to v is clockwise when (u × v)·view > 0)
        let first = BioV.dot(BioV.cross(a, b), view), second = BioV.dot(BioV.cross(b, d), view)
        return first > 0 && second > 0
    }

    // MARK: The R group in 2D

    /// The R group's heavy atoms projected on their best-fit plane, with CA as the stub it hangs from (and Pro's N, to
    /// show the ring closing on the backbone): 2D points one bond ≈ 1 unit apart, CA at the bottom, the R group above.
    static func sketch(_ one: Character) -> BiomolSketch {
        guard let t = template(one) else { return BiomolSketch(atoms: [], bonds: []) }
        if t.code == "GLY" {
            return BiomolSketch(atoms: [.init(name: "CA", element: "C", x: 0, y: 0, attachment: true), .init(name: "HA3", element: "H", x: 0, y: 1, attachment: false)],
                                bonds: [.init(a: 0, b: 1, order: 1)])
        }
        let keep = t.atoms.filter { a in
            a.element != "H" && (a.name == "CA" || (t.code == "PRO" && a.name == "N") || part(a.name, in: t) == BiomolParts.rGroup)
        }
        let pts = keep.map(\.pos)
        let c = BiomolMath.centroid(pts)
        var cov = [[Double]](repeating: [0, 0, 0], count: 3)
        for p in pts {
            let d = [p.x - c.x, p.y - c.y, p.z - c.z]
            for r in 0..<3 { for s in 0..<3 { cov[r][s] += d[r] * d[s] } }
        }
        let (_, vecs) = BiomolMath.eigenSymmetric3(cov)
        let e1 = vecs[0], e2 = vecs[1]
        var flat = pts.map { (BioV.dot($0 - c, e1), BioV.dot($0 - c, e2)) }
        // (turned so the R group stands up from CA)
        guard let caIndex = keep.firstIndex(where: { $0.name == "CA" }) else { return BiomolSketch(atoms: [], bonds: []) }
        let ca = flat[caIndex]
        var sx = 0.0, sy = 0.0
        for (k, p) in flat.enumerated() where k != caIndex {
            sx += p.0
            sy += p.1
        }
        let others = Double(max(flat.count - 1, 1))
        let midX: Double = flat.count > 1 ? sx / others : ca.0
        let midY: Double = flat.count > 1 ? sy / others : ca.1 + 1
        let angle = atan2(midX - ca.0, midY - ca.1)
        flat = flat.map { p in
            let x = p.0 - ca.0, y = p.1 - ca.1
            return ((x * cos(angle) - y * sin(angle)) / 1.5, (x * sin(angle) + y * cos(angle)) / 1.5)
        }
        let index = Dictionary(keep.enumerated().map { ($0.element.name, $0.offset) }, uniquingKeysWith: { a, _ in a })
        let atoms = keep.enumerated().map { k, a in BiomolSketch.Atom(name: a.name, element: a.element, x: flat[k].0, y: flat[k].1, attachment: a.name == "CA") }
        let bonds: [BiomolSketch.Bond] = t.bonds.compactMap { b in
            guard let x = index[b.a], let y = index[b.b] else { return nil }
            return BiomolSketch.Bond(a: x, b: y, order: b.order)
        }
        return BiomolSketch(atoms: atoms, bonds: bonds)
    }
}

/// A small 2D drawing of a group of atoms (an R group): points in bond lengths, bonds with their orders.
struct BiomolSketch: Sendable {
    struct Atom: Sendable {
        let name: String
        let element: String
        let x: Double
        let y: Double
        /// The atom the group hangs from (drawn as a stub).
        let attachment: Bool
    }

    struct Bond: Sendable {
        let a: Int
        let b: Int
        let order: Int
    }

    let atoms: [Atom]
    let bonds: [Bond]

    /// Its extent: (minX, minY, maxX, maxY).
    var bounds: (Double, Double, Double, Double) {
        guard let f = atoms.first else { return (0, 0, 1, 1) }
        var b = (f.x, f.y, f.x, f.y)
        for a in atoms { b = (min(b.0, a.x), min(b.1, a.y), max(b.2, a.x), max(b.3, a.y)) }
        return b
    }
}

enum BiomolMath {
    /// The mean of some points (the origin for none).
    static func centroid(_ pts: [BioVec]) -> BioVec {
        guard !pts.isEmpty else { return BioVec(0, 0, 0) }
        var s = BioVec(0, 0, 0)
        for p in pts { s += p }
        return s / Double(pts.count)
    }

    /// The eigenvalues (largest first) and unit eigenvectors of a symmetric 3×3 matrix (Jacobi rotations).
    static func eigenSymmetric3(_ m: [[Double]]) -> (values: [Double], vectors: [BioVec]) {
        var a = m
        var v: [[Double]] = [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
        for _ in 0..<50 {
            var p = 0, q = 1
            var big = abs(a[0][1])
            if abs(a[0][2]) > big { big = abs(a[0][2]); p = 0; q = 2 }
            if abs(a[1][2]) > big { big = abs(a[1][2]); p = 1; q = 2 }
            if big < 1e-12 { break }
            let theta = (a[q][q] - a[p][p]) / (2 * a[p][q])
            let t = (theta >= 0 ? 1.0 : -1.0) / (abs(theta) + (theta * theta + 1).squareRoot())
            let c = 1 / (t * t + 1).squareRoot(), s = t * c
            for k in 0..<3 {
                let akp = a[k][p], akq = a[k][q]
                a[k][p] = c * akp - s * akq
                a[k][q] = s * akp + c * akq
            }
            for k in 0..<3 {
                let apk = a[p][k], aqk = a[q][k]
                a[p][k] = c * apk - s * aqk
                a[q][k] = s * apk + c * aqk
            }
            for k in 0..<3 {
                let vkp = v[k][p], vkq = v[k][q]
                v[k][p] = c * vkp - s * vkq
                v[k][q] = s * vkp + c * vkq
            }
        }
        let order = [0, 1, 2].sorted { a[$0][$0] > a[$1][$1] }
        return (order.map { a[$0][$0] }, order.map { BioV.unit(BioVec(v[0][$0], v[1][$0], v[2][$0])) })
    }
}

// MARK: - Nucleotides and a strand (§6.6, §6.7)

enum BiomolNucleotide {
    /// The nine, in §6.6's order.
    static let codes = ["DA", "DC", "DG", "DT", "A", "C", "G", "U", "ATP"]

    static let phosphateNames: Set<String> = ["P", "OP1", "OP2", "OP3", "HOP2", "HOP3", "O5'", "PA", "PB", "PG", "O1A", "O2A", "O3A",
                                              "O1B", "O2B", "O3B", "O1G", "O2G", "O3G", "HOG2", "HOG3", "HOB2", "HOA2"]

    /// The part an atom belongs to: the phosphate (with the 5′ O that joins it to the sugar), the sugar (its primed
    /// atoms) or the base.
    static func part(_ name: String) -> BiomolPart {
        if phosphateNames.contains(name) { return BiomolParts.phosphate }
        if name.contains("'") { return BiomolParts.sugar }
        return BiomolParts.base
    }

    /// The base's letter (A C G T U) for a code.
    static func baseLetter(_ code: String) -> Character {
        switch code {
        case "DA", "A", "ATP": return "A"
        case "DC", "C": return "C"
        case "DG", "G": return "G"
        case "DT": return "T"
        default: return "U"
        }
    }

    static func isDeoxy(_ code: String) -> Bool { code.hasPrefix("D") }

    static func baseName(_ b: Character) -> String {
        switch b {
        case "A": return "Adenine"
        case "G": return "Guanine"
        case "C": return "Cytosine"
        case "T": return "Thymine"
        default: return "Uracil"
        }
    }

    static func isPurine(_ b: Character) -> Bool { b == "A" || b == "G" }

    /// "dAMP", "ATP": the short name.
    static func shortName(_ code: String) -> String {
        switch code {
        case "DA": return "dAMP"
        case "DC": return "dCMP"
        case "DG": return "dGMP"
        case "DT": return "dTMP"
        case "A": return "AMP"
        case "C": return "CMP"
        case "G": return "GMP"
        case "U": return "UMP"
        default: return code
        }
    }
}

/// A DNA strand built as strand 1 of the B-DNA model (§6.7, heavy atoms only), and its complementary strand if asked.
enum BiomolStrand {
    /// The longest strand built here (§4.4: up to 12 nt).
    static let maxLength = 12
    static let defaultSequence = "ATGCGT"

    static func build(_ sequence: String, complement: Bool) -> BioDuplex {
        let seq = String(sequence.uppercased().filter { "ACGT".contains($0) }.prefix(maxLength))
        return BioBDNA.build(seq, bothStrands: complement)
    }

    /// The phosphodiester linkages of a strand, 5′→3′: C3′–O3′ of one nucleotide, P–O5′–C5′ of the next (atom indices).
    static func linkages(_ d: BioDuplex, strand: Int) -> [[Int]] {
        let n = d.sequence.count
        guard n > 1 else { return [] }
        return (0..<(n - 1)).compactMap { k -> [Int]? in
            // (strand 1 runs up the pairs; strand 2 runs down them)
            let (a, b) = strand == 1 ? (k, k + 1) : (n - 1 - k, n - 2 - k)
            let names: [(Int, String)] = [(a, "C3'"), (a, "O3'"), (b, "P"), (b, "O5'"), (b, "C5'")]
            let idx = names.compactMap { d.index(strand: strand, pair: $0.0, name: $0.1) }
            return idx.count == 5 ? idx : nil
        }
    }

    /// Where the water a linkage releases appears: off the O3′–P bond, away from the helix's axis.
    static func waterSite(_ d: BioDuplex, linkage: [Int]) -> BioVec {
        let o = d.atoms[linkage[1]].pos, p = d.atoms[linkage[2]].pos
        let mid = (o + p) / 2
        let out = BioV.unit(BioVec(mid.x, 0, mid.z))
        return mid + out * 2.4
    }
}

// MARK: - Sugars (§6.8)

enum BiomolSugar {
    static let monosaccharides = ["GLC", "BGC", "GAL", "FRU", "BDR"]
    static let disaccharides = ["MAL", "CBI", "SUC", "LAT"]

    static func shortName(_ code: String) -> String {
        switch code {
        case "GLC": return "α-Glucose"
        case "BGC": return "β-Glucose"
        case "GAL": return "Galactose"
        case "FRU": return "Fructose"
        case "BDR": return "Ribose"
        case "MAL": return "Maltose"
        case "CBI": return "Cellobiose"
        case "SUC": return "Sucrose"
        case "LAT": return "Lactose"
        default: return code
        }
    }

    /// The linkage a disaccharide's label shows.
    static let linkage: [String: String] = ["MAL": "α(1→4)", "CBI": "β(1→4)", "SUC": "α(1→2)β", "LAT": "β(1→4)"]
    /// A disaccharide's two monosaccharides: the one whose anomeric carbon makes the bond (unprimed atoms) and the other
    /// (primed).
    static let residues: [String: (first: String, second: String)] = [
        "MAL": ("Glucose", "Glucose"), "CBI": ("Glucose", "Glucose"), "SUC": ("Glucose", "Fructose"), "LAT": ("Galactose", "Glucose"),
    ]
    /// Each residue's colour when coloured by part.
    static let residueColours = [BioHexPair("#1f6fd1", "#5ea8ff"), BioHexPair("#2e9e78", "#4fd1a5")]

    /// A sugar ring: its atoms in order round it, its ring O, its anomeric carbon (the ring carbon beside the ring O that
    /// carries an O outside the ring) and that O, and the reference carbon (the CH₂OH carbon on the ring O's other
    /// neighbour: C6, or ribose's C5).
    struct Ring: Equatable, Sendable {
        let atoms: [String]
        let ringO: String
        let anomeric: String
        let anomericO: String
        let reference: String
        let referenceOn: String
    }

    /// The ring of the atoms whose names are primed (`primed`) or not, in a molecule.
    static func ring(_ m: BioMolecule, primed: Bool = false) -> Ring? {
        let adj = BiomolGraph.neighbours(m)
        let element = Dictionary(m.atoms.map { ($0.name, $0.element) }, uniquingKeysWith: { a, _ in a })
        func mine(_ n: String) -> Bool { n.contains("'") == primed && element[n] != "H" }
        var best: [String]?
        for o in m.atoms where o.element == "O" && mine(o.name) {
            let cs = (adj[o.name] ?? []).filter { element[$0] == "C" && mine($0) }
            guard cs.count == 2 else { continue }
            // (the shortest way from one carbon to the other not through the O)
            var prev: [String: String] = [cs[0]: ""]
            var queue = [cs[0]]
            while !queue.isEmpty, prev[cs[1]] == nil {
                let x = queue.removeFirst()
                for y in adj[x] ?? [] where y != o.name && mine(y) && prev[y] == nil {
                    prev[y] = x
                    queue.append(y)
                }
            }
            guard prev[cs[1]] != nil else { continue }
            var path = [cs[1]]
            while let p = prev[path[path.count - 1]], !p.isEmpty { path.append(p) }
            let ring = [o.name] + path.reversed()
            if best == nil || ring.count < best!.count { best = ring }
        }
        guard let r = best else { return nil }
        let ringO = r[0]
        let sides = [r[1], r[r.count - 1]]
        func exo(_ c: String, _ el: String) -> String? {
            (adj[c] ?? []).first { element[$0] == el && !r.contains($0) }
        }
        guard let anomeric = sides.first(where: { exo($0, "O") != nil }), let aO = exo(anomeric, "O") else { return nil }
        let other = sides.first { $0 != anomeric } ?? sides[1]
        guard let ref = exo(other, "C") else { return nil }
        return Ring(atoms: r, ringO: ringO, anomeric: anomeric, anomericO: aO, reference: ref, referenceOn: other)
    }

    /// Whether the anomeric O is on the other face of the ring from the reference CH₂OH (trans: α) or the same face
    /// (cis: β) — the one difference between α- and β-glucose, and between starch and cellulose.
    static func isAlpha(_ m: BioMolecule, _ r: Ring) -> Bool {
        let pts = r.atoms.compactMap { m.pos($0) }
        let normal = BiomolProteins.plane(pts).normal
        guard let a = m.pos(r.anomeric), let ao = m.pos(r.anomericO), let c = m.pos(r.referenceOn), let ref = m.pos(r.reference) else { return false }
        return BioV.dot(ao - a, normal) * BioV.dot(ref - c, normal) < 0
    }

    /// The ring carbons' numbers as a sugar is numbered (1–6, primes dropped: "1′–5′" is for nucleotides).
    static func carbonNumbers(_ m: BioMolecule) -> [String: String] {
        var out: [String: String] = [:]
        for a in m.atoms where a.element == "C" {
            let digits = a.name.filter { $0.isNumber }
            if !digits.isEmpty { out[a.name] = digits }
        }
        return out
    }

    /// A disaccharide's glycosidic bond: the O bonded to one primed and one unprimed carbon, the unprimed residue's
    /// anomeric carbon on it, and the other carbon.
    struct Glycosidic: Equatable, Sendable {
        let o: String
        let anomeric: String
        let other: String
    }

    static func glycosidic(_ m: BioMolecule) -> Glycosidic? {
        let adj = BiomolGraph.neighbours(m)
        for o in m.atoms where o.element == "O" {
            let cs = (adj[o.name] ?? []).filter { n in m.atom(n)?.element == "C" }
            guard cs.count == 2 else { continue }
            let primed = cs.filter { $0.contains("'") }, plain = cs.filter { !$0.contains("'") }
            if primed.count == 1 && plain.count == 1 { return Glycosidic(o: o.name, anomeric: plain[0], other: primed[0]) }
        }
        return nil
    }

    /// Which residue an atom of a disaccharide belongs to: 0 for the unprimed one, 1 for the primed one (which keeps the
    /// glycosidic O when the bond is hydrolysed); an H goes with its heavy atom.
    static func residue(_ name: String, in m: BioMolecule, glycosidicO: String?) -> Int {
        let heavy = BiomolGraph.heavyNeighbour(m)[name] ?? name
        if heavy == glycosidicO { return 1 }
        return heavy.contains("'") ? 1 : 0
    }

    /// A disaccharide split by hydrolysis (§4.4): each residue's atoms moved `apart`/2 along the glycosidic C–O bond
    /// (the first residue back, the second on), an H on the freed glycosidic O (now an OH) and an OH on the first
    /// residue's anomeric carbon (the O 1.43 Å out along the former C–O direction) — the water's three atoms, named
    /// "O (water)" and "H (water)". Each atom tagged with its residue's name.
    static func split(_ m: BioMolecule, apart: Double = 4) -> (model: BioMoleculeModel, residue: [Int], newAtoms: [Int])? {
        guard let g = glycosidic(m), let c1 = m.pos(g.anomeric), let og = m.pos(g.o) else { return nil }
        let d = BioV.unit(og - c1)
        let names = residues[m.code] ?? ("Sugar 1", "Sugar 2")
        var model = BioMoleculeModel()
        var ids: [String: Int] = [:]
        var res: [Int] = []
        for a in m.atoms {
            let r = residue(a.name, in: m, glycosidicO: g.o)
            ids[a.name] = model.add(a.element, a.pos + d * (r == 0 ? -apart / 2 : apart / 2), name: a.name, tag: r == 0 ? names.first : names.second)
            res.append(r)
        }
        for b in m.bonds where !(Set([b.a, b.b]) == Set([g.anomeric, g.o])) {
            if let x = ids[b.a], let y = ids[b.b] { model.bond(x, y, order: b.order) }
        }
        guard let ci = ids[g.anomeric], let oi = ids[g.o] else { return nil }
        let c1n = model.atoms[ci].pos, ogn = model.atoms[oi].pos
        var new: [Int] = []
        let h1 = model.add("H", ogn - d * 0.97, name: "H (water)", tag: names.second)
        model.bond(oi, h1)
        let o1 = model.add("O", c1n + d * 1.43, name: "O (water)", tag: names.first)
        model.bond(ci, o1)
        // (its H anti to the ring O across the new C–O bond)
        let ringO = ring(m, primed: false)?.ringO
        let ref = ringO.flatMap { ids[$0] }.map { model.atoms[$0].pos } ?? (c1n + BioVec(0, 1, 0))
        let h2 = model.add("H", BioV.place(ref, c1n, model.atoms[o1].pos, length: 0.97, angle: 109.5, torsion: 180), name: "H (water)", tag: names.first)
        model.bond(o1, h2)
        new = [h1, o1, h2]
        res += [1, 0, 0]
        return (model, res, new)
    }
}

// MARK: - Lipids (§6.9)

enum BiomolLipid {
    static let codes = ["GOL", "PLM", "STE", "OLA", "TGL", "POV", "CLR"]

    static func shortName(_ code: String) -> String {
        switch code {
        case "GOL": return "Glycerol"
        case "PLM": return "Palmitic acid"
        case "STE": return "Stearic acid"
        case "OLA": return "Oleic acid"
        case "TGL": return "Tristearin"
        case "POV": return "POPC"
        case "CLR": return "Cholesterol"
        default: return code
        }
    }

    /// The finer part an atom belongs to (its label): carboxyl group, hydrocarbon tail, glycerol, ester linkage,
    /// phosphate, choline, hydroxyl group or the steroid rings. H atoms go with their heavy atom.
    static func detail(_ name: String, in m: BioMolecule) -> String {
        let n = BiomolGraph.heavyNeighbour(m)[name] ?? name
        switch m.code {
        case "GOL": return "Glycerol"
        case "PLM", "STE", "OLA": return ["C1", "O1", "O2"].contains(n) ? "Carboxyl group" : "Hydrocarbon tail"
        case "TGL":
            if ["CG1", "CG2", "CG3"].contains(n) { return "Glycerol" }
            if ["OG1", "OG2", "OG3", "CA1", "OA1", "CB1", "OB1", "CC1", "OC1"].contains(n) { return "Ester bond" }
            return "Stearate tail"
        case "POV":
            if ["N", "C11", "C12", "C13", "C14", "C15"].contains(n) { return "Choline" }
            if ["P", "O11", "O12", "O13", "O14"].contains(n) { return "Phosphate" }
            if ["C1", "C2", "C3"].contains(n) { return "Glycerol" }
            if ["O21", "C21", "O22", "O31", "C31", "O32"].contains(n) { return "Ester bond" }
            return n.hasPrefix("C2") ? "Oleate tail" : "Palmitate tail"
        case "CLR":
            if n == "O1" { return "Hydroxyl group" }
            if let k = Int(n.dropFirst()), n.hasPrefix("C"), k >= 20 { return "Hydrocarbon tail" }
            return "Four fused rings"
        default: return ""
        }
    }

    /// Hydrophilic (the head: carboxyl, glycerol's OH, ester, phosphate, choline, cholesterol's OH) or hydrophobic.
    static func isHydrophilic(_ detail: String) -> Bool {
        ["Carboxyl group", "Glycerol", "Ester bond", "Phosphate", "Choline", "Hydroxyl group"].contains(detail)
    }

    /// The ester bonds (acyl C to the glycerol O): tristearin's three, POPC's two.
    static func esterBonds(_ code: String) -> [(String, String)] {
        switch code {
        case "TGL": return [("CA1", "OG1"), ("CB1", "OG2"), ("CC1", "OG3")]
        case "POV": return [("C21", "O21"), ("C31", "O31")]
        default: return []
        }
    }

    /// The cis double bonds that kink a chain: oleic acid's C9=C10, POPC's oleate C29=C210 (cholesterol's C5=C6 is in
    /// a ring).
    static func cisBonds(_ code: String) -> [(String, String)] {
        switch code {
        case "OLA": return [("C9", "C10")]
        case "POV": return [("C29", "C210")]
        default: return []
        }
    }

    /// The chain's angle at the double bond: the C8→C9 and C10→C11 directions (degrees; 0 straight on).
    static func kinkAngle(_ m: BioMolecule) -> Double? {
        guard let c1 = m.pos("C1"), let c9 = m.pos("C9"), let c18 = m.pos("C18") else { return nil }
        return 180 - BioV.angle(c1, c9, c18)
    }
}

// MARK: - Polysaccharides (2D)

/// The polysaccharides drawn as hexagon glyphs (§4.4): where each glucose (or N-acetylglucosamine) sits, which way up,
/// and the links between them with their linkage, in glyph spacings.
enum BiomolPolysaccharide: String, CaseIterable, Sendable {
    case amylose, amylopectin, glycogen, cellulose, chitin

    var title: String {
        switch self {
        case .amylose: return "Amylose (starch)"
        case .amylopectin: return "Amylopectin (starch)"
        case .glycogen: return "Glycogen"
        case .cellulose: return "Cellulose"
        case .chitin: return "Chitin"
        }
    }

    var short: String { rawValue.prefix(1).uppercased() + rawValue.dropFirst() }

    /// What it is, in a sentence or two.
    var summary: String {
        switch self {
        case .amylose: return "α-glucose joined α(1→4). The α links make the chain coil into a helix, about six glucose a turn. Plants store energy as starch."
        case .amylopectin: return "α(1→4) chains with an α(1→6) branch every 24–30 glucose. The other part of starch."
        case .glycogen: return "Like amylopectin but more branched: an α(1→6) branch every 8–12 glucose, so enzymes can free glucose quickly. Animals store it in liver and muscle."
        case .cellulose: return "β-glucose joined β(1→4), every other glucose flipped, so the chains run straight. Neighbouring chains H-bond into strong microfibrils: plant cell walls."
        case .chitin: return "Like cellulose, β(1→4), but each sugar is N-acetylglucosamine (NAc). Insect and crab exoskeletons and fungal cell walls."
        }
    }

    struct Glyph: Equatable, Sendable {
        let x: Double
        let y: Double
        /// Drawn upside down (cellulose's and chitin's every other sugar).
        var flipped = false
        /// A tag on the glyph ("NAc").
        var tag: String?
    }

    enum LinkKind: Sendable { case chain, branch, hBond, gap }

    struct Link: Equatable, Sendable {
        let from: Int
        let to: Int
        let text: String
        let kind: LinkKind
    }

    struct Layout: Sendable {
        let glyphs: [Glyph]
        let links: [Link]
        let notes: [(x: Double, y: Double, text: String)]
        let width: Double
        let height: Double
    }

    var layout: Layout {
        var g: [Glyph] = []
        var l: [Link] = []
        var notes: [(x: Double, y: Double, text: String)] = []
        func chain(_ count: Int, x0: Double, y0: Double, wave: Double = 0, flip: Bool = false, tag: String? = nil, linkText: String) -> [Int] {
            var ids: [Int] = []
            for k in 0..<count {
                let y = y0 + wave * sin(Double(k) * 2 * .pi / 6)
                g.append(Glyph(x: x0 + Double(k), y: y, flipped: flip && k % 2 == 1, tag: tag))
                ids.append(g.count - 1)
                if k > 0 { l.append(Link(from: g.count - 2, to: g.count - 1, text: linkText, kind: .chain)) }
            }
            return ids
        }
        switch self {
        case .amylose:
            _ = chain(12, x0: 0, y0: 0.5, wave: 0.35, linkText: "α(1→4)")
            notes.append((5.5, 1.45, "coils into a helix (≈ 6 glucose a turn)"))
            return Layout(glyphs: g, links: l, notes: notes, width: 12, height: 1.8)
        case .amylopectin:
            let main = chain(12, x0: 0, y0: 0, linkText: "α(1→4)")
            // (a gap stands for the 24–30 residues between branches)
            if let k = l.firstIndex(where: { $0.from == main[3] }) { l[k] = Link(from: main[3], to: main[4], text: "≈ 24–30 glucose", kind: .gap) }
            let branch = chain(5, x0: 8.6, y0: 1.35, linkText: "α(1→4)")
            l.append(Link(from: main[8], to: branch[0], text: "α(1→6) branch", kind: .branch))
            notes.append((3.5, -0.85, "branch every 24–30 glucose"))
            return Layout(glyphs: g, links: l, notes: notes, width: 13.6, height: 2.6)
        case .glycogen:
            let main = chain(11, x0: 0, y0: 0, linkText: "α(1→4)")
            let b1 = chain(5, x0: 2.6, y0: 1.35, linkText: "α(1→4)")
            let b2 = chain(4, x0: 6.6, y0: 1.35, linkText: "α(1→4)")
            let b3 = chain(3, x0: 4.2, y0: 2.7, linkText: "α(1→4)")
            l.append(Link(from: main[2], to: b1[0], text: "α(1→6) branch", kind: .branch))
            l.append(Link(from: main[6], to: b2[0], text: "α(1→6) branch", kind: .branch))
            l.append(Link(from: b1[1], to: b3[0], text: "α(1→6) branch", kind: .branch))
            notes.append((5, -0.85, "branch every 8–12 glucose"))
            return Layout(glyphs: g, links: l, notes: notes, width: 11, height: 4)
        case .cellulose:
            var rows: [[Int]] = []
            for r in 0..<3 { rows.append(chain(8, x0: 0, y0: Double(r) * 1.25, flip: true, linkText: "β(1→4)")) }
            for r in 0..<2 {
                for k in stride(from: 0, to: 8, by: 2) { l.append(Link(from: rows[r][k], to: rows[r + 1][k], text: "H-bond", kind: .hBond)) }
            }
            notes.append((3.5, -0.85, "straight chains H-bonded into a microfibril"))
            return Layout(glyphs: g, links: l, notes: notes, width: 8, height: 3.6)
        case .chitin:
            _ = chain(8, x0: 0, y0: 0.4, flip: true, tag: "NAc", linkText: "β(1→4)")
            notes.append((3.5, -0.55, "N-acetylglucosamine, β(1→4)"))
            return Layout(glyphs: g, links: l, notes: notes, width: 8, height: 1.6)
        }
    }
}

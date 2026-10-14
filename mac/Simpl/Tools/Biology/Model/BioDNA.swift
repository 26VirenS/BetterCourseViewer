import Foundation

// B-DNA, built atom by atom from the Arnott fiber model (spec §6.7): base pair k sits 3.40·k Å up the axis and is
// turned 360°/10.5 per pair, so the helix is right-handed with 10.5 bp a turn. Strand 1 runs 5′→3′ up (+Y); strand 2,
// the dyad image of strand 1, runs 5′→3′ down. Shared by DNA & RNA (the double helix, unzipping) and the Biomolecule
// Builder (building a strand). The app's frame keeps the hand: X = r·sin θ, Y = x, Z = r·cos θ.

/// A B-DNA duplex (or one strand of it), heavy atoms only.
struct BioDuplex {
    /// An atom of the helix: its strand (1 or 2), base pair (0 at strand 1's 5′ end), base, name and place (Å).
    struct Atom: Equatable {
        let strand: Int
        let pair: Int
        let base: Character
        let name: String
        let element: String
        let pos: BioVec
    }

    /// Strand 1's sequence, 5′→3′ (A, C, G, T).
    let sequence: String
    let atoms: [Atom]
    /// Bonds by atom index (inside each nucleotide by §6.6's bond list, and O3′–P between nucleotides), with orders.
    let bonds: [(a: Int, b: Int, order: Int)]
    /// The Watson–Crick H-bonds of each pair (§6.5), by atom index: donor or acceptor on each base.
    let hBonds: [(pair: Int, a: Int, b: Int)]
    let hasStrand2: Bool

    /// The index of an atom (strand, pair, name), if it is in the helix.
    func index(strand: Int, pair: Int, name: String) -> Int? {
        atoms.firstIndex { $0.strand == strand && $0.pair == pair && $0.name == name }
    }

    func pos(strand: Int, pair: Int, name: String) -> BioVec? { index(strand: strand, pair: pair, name: name).map { atoms[$0].pos } }

    /// A strand's P atoms, 5′→3′ (the cartoon's backbone spline goes through them).
    func phosphates(strand: Int) -> [BioVec] {
        let ps = atoms.filter { $0.strand == strand && $0.name == "P" }.sorted { $0.pair < $1.pair }.map(\.pos)
        return strand == 1 ? ps : ps.reversed()
    }

    /// The atoms and bonds as a 3D view draws them, each atom tagged "strand 1 · A 3" (its strand, base and number
    /// from its own 5′ end), and named.
    var model: BioMoleculeModel {
        var m = BioMoleculeModel()
        let n = sequence.count
        for a in atoms {
            let number = a.strand == 1 ? a.pair + 1 : n - a.pair
            m.add(a.element, a.pos, name: a.name, tag: "strand \(a.strand) · \(a.base) \(number)")
        }
        for b in bonds { m.bond(b.a, b.b, order: b.order) }
        return m
    }
}

enum BioBDNA {
    /// Rise per base pair (Å).
    static let rise = 3.40
    /// Twist per base pair (degrees): 10.5 bp a turn.
    static let twist = 360.0 / 10.5
    /// The dyad that makes strand 2 from strand 1: (c − x, d − θ).
    static let dyadC = 4.160
    static let dyadD = -169.596

    /// Helical coordinates (x along the axis, r, θ in degrees) in the app's frame, Y up: X = r·sin θ, Y = x, Z = r·cos θ.
    static func appFrame(x: Double, r: Double, theta: Double) -> BioVec {
        let t = theta * .pi / 180
        return BioVec(r * sin(t), x, r * cos(t))
    }

    /// Where an atom of the table goes: strand 1's for base pair k, or strand 2's (the dyad image) for pair k.
    static func place(_ a: BioData.HelixAtom, strand: Int, pair k: Int) -> BioVec {
        let kk = Double(k)
        if strand == 1 { return appFrame(x: a.x + kk * rise, r: a.r, theta: a.theta + kk * twist) }
        return appFrame(x: dyadC - a.x + kk * rise, r: a.r, theta: dyadD - a.theta + kk * twist)
    }

    /// The helix for strand 1's sequence (5′→3′; A, C, G and T, anything else skipped), both strands or strand 1 alone.
    /// Each strand keeps its 5′-terminal phosphate (labelled "5′ phosphate" by the tools).
    static func build(_ sequence: String, bothStrands: Bool = true) -> BioDuplex {
        let seq = String(sequence.uppercased().filter { "ACGT".contains($0) })
        let table = BioData.bdna
        var atoms: [BioDuplex.Atom] = []
        var bonds: [(Int, Int, Int)] = []
        var hb: [(Int, Int, Int)] = []
        // (each nucleotide's own bonds from its deoxynucleotide's block, §6.6, where both atoms are in the helix)
        let codes: [Character: String] = ["A": "DA", "C": "DC", "G": "DG", "T": "DT"]
        let blocks = codes.mapValues { BioData.molecule($0)?.bonds ?? [] }
        var at: [String: Int] = [:]
        func key(_ s: Int, _ k: Int, _ name: String) -> String { "\(s)/\(k)/\(name)" }

        func addNucleotide(strand: Int, pair k: Int, base: Character) {
            let entries = table.backbone + (table.bases[base] ?? [])
            var names: [String: Int] = [:]
            for e in entries {
                let i = atoms.count
                atoms.append(BioDuplex.Atom(strand: strand, pair: k, base: base, name: e.name, element: e.element, pos: place(e, strand: strand, pair: k)))
                names[e.name] = i
                at[key(strand, k, e.name)] = i
            }
            for b in blocks[base] ?? [] {
                if let x = names[b.a], let y = names[b.b] { bonds.append((x, y, b.order)) }
            }
        }

        let bases = Array(seq)
        for (k, b) in bases.enumerated() {
            addNucleotide(strand: 1, pair: k, base: b)
            if bothStrands { addNucleotide(strand: 2, pair: k, base: BioSeq.pair(b, rna: false)) }
        }
        // (the phosphodiester bonds: strand 1 O3′(k)–P(k+1); strand 2 O3′(k+1)–P(k))
        if bases.count > 1 {
            for k in 0..<(bases.count - 1) {
                if let o = at[key(1, k, "O3'")], let p = at[key(1, k + 1, "P")] { bonds.append((o, p, 1)) }
                if bothStrands, let o = at[key(2, k + 1, "O3'")], let p = at[key(2, k, "P")] { bonds.append((o, p, 1)) }
            }
        }
        // (the Watson–Crick H-bonds, §6.5: strand 1's atom, strand 2's atom)
        if bothStrands {
            for (k, b) in bases.enumerated() {
                for (x, y) in hBondAtoms(b) {
                    if let i = at[key(1, k, x)], let j = at[key(2, k, y)] { hb.append((k, i, j)) }
                }
            }
        }
        return BioDuplex(sequence: seq, atoms: atoms, bonds: bonds.map { (a: $0.0, b: $0.1, order: $0.2) },
                         hBonds: hb.map { (pair: $0.0, a: $0.1, b: $0.2) }, hasStrand2: bothStrands)
    }

    /// The atoms joined by Watson–Crick H-bonds when strand 1 has `base` (strand 1's atom first): A·T 2, G·C 3.
    static func hBondAtoms(_ base: Character) -> [(String, String)] {
        switch base {
        case "A": return [("N6", "O4"), ("N1", "N3")]
        case "T": return [("O4", "N6"), ("N3", "N1")]
        case "G": return [("N1", "N3"), ("O6", "N4"), ("N2", "O2")]
        case "C": return [("N3", "N1"), ("N4", "O6"), ("O2", "N2")]
        default: return []
        }
    }
}

import Foundation

// The Biomolecule Builder's peptide builder (spec §6.4), exactly as the specification's script builds one: the backbone
// placed atom by atom by NeRF from each residue's φ and ψ (ω 180°, Engh & Huber's lengths and angles), each side chain
// put in its residue's frame from its §6.3 template (the β rotamer for Asp and Asn in a strand), a deterministic clash
// relief that tries each side chain's rotamers in turn, and proline's rule (a Pro four or more into a helix breaks it).
// Then the backbone H-bonds that hold helices and sheets, found from the atoms (N–H···O=C closer than 3.3 Å, the angle
// at H over 140°), the helix's own measures (residues a turn, rise, radius: §6.4's verification) and the β-sheet demo
// (three antiparallel strands from §6.4's transforms). Foundation only, so the Models job checks it against the
// specification's test vectors (BiomoleculeBuilderModel.swift's `biomolChecks`).

/// The secondary structure a residue is built in: its φ and ψ (IUPAC-IUB 1970, as Ramachandran references tabulate
/// them). Coil, α-helix and β-strand are the chips under the sequence; the rest are in their menu.
enum BiomolSS: String, CaseIterable, Sendable {
    case coil, helix, strand, parallel, helix310, leftHelix

    /// (φ, ψ) in degrees.
    var angles: (phi: Double, psi: Double) {
        switch self {
        case .coil: return (-75, 145)
        case .helix: return (-57, -47)
        case .strand: return (-139, 135)
        case .parallel: return (-119, 113)
        case .helix310: return (-49, -26)
        case .leftHelix: return (57, 47)
        }
    }

    var label: String {
        switch self {
        case .coil: return "Coil"
        case .helix: return "α-helix"
        case .strand: return "β-strand"
        case .parallel: return "Parallel β-strand"
        case .helix310: return "3₁₀ helix"
        case .leftHelix: return "Left-handed α (rare, Gly)"
        }
    }

    /// What the chip under a residue shows.
    var letter: Character {
        switch self {
        case .coil: return "C"
        case .helix: return "H"
        case .strand: return "E"
        case .parallel: return "P"
        case .helix310: return "G"
        case .leftHelix: return "L"
        }
    }

    init?(letter: Character) {
        guard let s = BiomolSS.allCases.first(where: { $0.letter == Character(letter.uppercased()) }) else { return nil }
        self = s
    }

    /// The cartoon draws it as a helix ribbon (H), a strand arrow (E) or a coil tube (C).
    var cartoon: Character {
        switch self {
        case .helix, .helix310, .leftHelix: return "H"
        case .strand, .parallel: return "E"
        case .coil: return "C"
        }
    }

    var isStrand: Bool { cartoon == "E" }
    var isHelix: Bool { cartoon == "H" }

    /// "C C H H H …" as one string ("CCHHH…"), and back (anything unreadable is coil; padded with coil to `count`).
    static func text(_ ss: [BiomolSS]) -> String { String(ss.map(\.letter)) }
    static func parse(_ text: String, count: Int) -> [BiomolSS] {
        let read = text.map { BiomolSS(letter: $0) ?? .coil }
        return (0..<count).map { $0 < read.count ? read[$0] : .coil }
    }
}

/// A backbone H-bond: residue `donor`'s N–H to residue `acceptor`'s C=O, by atom index, and the N···O distance (Å).
struct BiomolHBond: Equatable, Sendable {
    let donor: Int
    let acceptor: Int
    let n: Int
    let h: Int
    let o: Int
    let distance: Double
}

/// A built peptide: its atoms (every atom, H included), bonds, the structure each residue was built in, and its
/// backbone H-bonds.
struct BiomolPeptide: Sendable {
    struct Atom: Equatable, Sendable {
        /// Its residue (0 = the N-terminus).
        let residue: Int
        /// Its PDB name ("CA", "NZ", "HB2").
        let name: String
        let element: String
        var pos: BioVec
    }

    struct Bond: Equatable, Sendable {
        let a: Int
        let b: Int
        let order: Int
        /// A peptide bond (C of one residue to N of the next).
        var peptide = false
    }

    /// One-letter codes, N→C.
    let sequence: [Character]
    /// The structure asked for, residue by residue.
    let asked: [BiomolSS]
    /// What was built: as asked, but where a proline broke a helix (it and the residue before it are coil).
    let built: [BiomolSS]
    let phi: [Double]
    let psi: [Double]
    /// The prolines (residue indices) that broke a helix.
    let prolineBreaks: [Int]
    /// The pH 7 form (N-terminus +1, C-terminus −1, ionised side chains) or the neutral one.
    let ph7: Bool
    var atoms: [Atom]
    var bonds: [Bond]
    /// Each residue's N, CA, C and O (atom indices) and its amide H (nil for proline and the first residue's own).
    var backbone: [(n: Int, ca: Int, c: Int, o: Int, h: Int?)]
    var hBonds: [BiomolHBond] = []
    /// The rotamer each side chain ended with (its Lovell name; nil for Gly, Ala and Pro).
    var rotamers: [String?] = []

    var count: Int { sequence.count }

    /// "Lys 12": a residue's three-letter code and its number from the N-terminus (1-based).
    func residueName(_ i: Int) -> String {
        guard i >= 0, i < sequence.count else { return "" }
        return "\(BioAminoAcids.byOne[sequence[i]]?.three ?? String(sequence[i])) \(i + 1)"
    }

    /// The CA atoms, N→C.
    var caTrace: [BioVec] { backbone.map { atoms[$0.ca].pos } }

    /// What a 3D view draws: the atoms (with or without H), each tagged with its residue ("Lys 12") and named; ids are
    /// the indices in `atoms` when H is kept, so a pick names the atom. Bond orders as the templates have them.
    func model(hydrogens: Bool = true) -> (model: BioMoleculeModel, index: [Int]) {
        var m = BioMoleculeModel()
        var map: [Int: Int] = [:]
        var index: [Int] = []
        for (k, a) in atoms.enumerated() where hydrogens || a.element != "H" {
            map[k] = m.add(a.element, a.pos, name: a.name, tag: residueName(a.residue))
            index.append(k)
        }
        for b in bonds {
            if let x = map[b.a], let y = map[b.b] { m.bond(x, y, order: b.order) }
        }
        return (m, index)
    }

    /// The atom of a residue by its name.
    func atom(_ residue: Int, _ name: String) -> Int? {
        atoms.firstIndex { $0.residue == residue && $0.name == name }
    }

    /// Every atom moved by a turn (a 3×3 matrix, rows) and a shift: S2 = R·S1 + t (§6.4's sheet).
    func transformed(_ r: [[Double]], _ t: BioVec) -> BiomolPeptide {
        var out = self
        out.atoms = atoms.map { a in
            let p = a.pos
            let q = BioVec(r[0][0] * p.x + r[0][1] * p.y + r[0][2] * p.z,
                           r[1][0] * p.x + r[1][1] * p.y + r[1][2] * p.z,
                           r[2][0] * p.x + r[2][1] * p.y + r[2][2] * p.z)
            return Atom(residue: a.residue, name: a.name, element: a.element, pos: q + t)
        }
        return out
    }

    func moved(by v: BioVec) -> BiomolPeptide { transformed([[1, 0, 0], [0, 1, 0], [0, 0, 1]], v) }
}

enum BiomolPeptideBuilder {
    // Engh & Huber (1991): lengths in Å, angles in degrees.
    static let nCA = 1.458, caC = 1.525, cN = 1.329, cO = 1.231, nH = 1.01
    static let angNCAC = 111.2, angCACN = 116.2, angCNCA = 121.7, angCACO = 120.1

    /// The peptide's longest (spec §4.4: up to 40 residues, about 800 atoms with H).
    static let maxResidues = 40
    /// The default sequence (19 residues).
    static let defaultSequence = "MKTAYIAKQRQISFVKSHF"

    /// Atoms the builder makes itself (a template's are skipped).
    static let backboneNames: Set<String> = ["N", "CA", "C", "O", "OXT", "H", "H2", "H3", "HXT"]
    /// A side chain's atoms "beyond CB" exclude these (the clash relief's measure).
    static let nearBackbone: Set<String> = ["N", "CA", "C", "O", "OXT", "CB", "H", "H2", "H3", "HA", "HA2", "HA3"]
    /// The neutral form's side chains as textbooks draw them: the CCD keeps Lys, Arg and His in their +1 form, so the
    /// neutral form leaves out the extra H (Lys –NH₂, Arg's neutral guanidine =NH, His's neutral Nε2–H ring).
    static let neutralRemovals: [String: Set<String>] = ["LYS": ["HZ3"], "ARG": ["HH22"], "HIS": ["HD1"]]

    /// The side-chain atoms a template contributes in a form: never the builder's own, never what the form leaves out.
    static func sideChainAtoms(_ t: BioMolecule, ph7: Bool) -> [BioAtomRecord] {
        let removed: Set<String> = ph7 ? Set(t.ph7?.remove ?? []) : (neutralRemovals[t.code] ?? [])
        return t.atoms.filter { $0.role == .always && !backboneNames.contains($0.name) && !removed.contains($0.name) }
    }

    /// The structures actually built: a Pro at position 4 or later of a helix segment (counted from 1) makes it and the
    /// residue before it coil (−75°/145°), which breaks the helix there (§6.4 step 7). Returns the breaking prolines.
    static func applyProlineRule(_ sequence: [Character], _ asked: [BiomolSS]) -> (built: [BiomolSS], breaks: [Int]) {
        var built = asked
        var breaks: [Int] = []
        var i = 0
        while i < asked.count {
            let kind = asked[i]
            guard kind == .helix || kind == .helix310 else { i += 1; continue }
            var j = i
            while j < asked.count && asked[j] == kind { j += 1 }
            for p in i..<j where sequence[p] == "P" && p - i >= 3 { breaks.append(p) }
            i = j
        }
        for p in breaks {
            built[p] = .coil
            built[p - 1] = .coil
        }
        return (built, breaks)
    }

    /// The peptide for a sequence of one-letter codes (other letters skipped; at most `maxResidues`), each residue in
    /// its structure (`ss`, padded with coil), in the pH 7 or the neutral form; `relieve` runs the clash relief.
    static func build(_ text: String, ss: [BiomolSS], ph7: Bool = true, relieve: Bool = true) -> BiomolPeptide {
        let seq = Array(text.uppercased().filter { BioAminoAcids.byOne[$0] != nil }.prefix(maxResidues))
        let n = seq.count
        let asked = (0..<n).map { $0 < ss.count ? ss[$0] : .coil }
        let (built, breaks) = applyProlineRule(seq, asked)
        var phi = built.map(\.angles.phi)
        let psi = built.map(\.angles.psi)
        // (proline's ring holds its own φ near −65°: in a strand's −139° or a left-handed helix's +57° its CD would sit
        // on the C before it, so a Pro there is built with the coil's −75°, its ψ as asked)
        for i in 0..<n where seq[i] == "P" && (phi[i] < -100 || phi[i] > 0) { phi[i] = BiomolSS.coil.angles.phi }
        var peptide = BiomolPeptide(sequence: seq, asked: asked, built: built, phi: phi, psi: psi, prolineBreaks: breaks, ph7: ph7,
                                    atoms: [], bonds: [], backbone: [])
        guard n > 0 else { return peptide }

        // 1–2. The backbone by NeRF.
        var N = [BioVec(0, 0, 0)], CA = [BioVec(nCA, 0, 0)]
        let t0 = (180 - angNCAC) * .pi / 180
        var C = [CA[0] + caC * BioVec(cos(t0), sin(t0), 0)]
        if n > 1 {
            for i in 1..<n {
                N.append(BioV.place(N[i - 1], CA[i - 1], C[i - 1], length: cN, angle: angCACN, torsion: psi[i - 1]))
                CA.append(BioV.place(CA[i - 1], C[i - 1], N[i], length: nCA, angle: angCNCA, torsion: 180))
                C.append(BioV.place(C[i - 1], N[i], CA[i], length: caC, angle: angNCAC, torsion: phi[i]))
            }
        }

        var atoms: [BiomolPeptide.Atom] = []
        var names: [[String: Int]] = Array(repeating: [:], count: n)
        func add(_ i: Int, _ name: String, _ element: String, _ p: BioVec) {
            names[i][name] = atoms.count
            atoms.append(BiomolPeptide.Atom(residue: i, name: name, element: element, pos: p))
        }
        var templates: [BioMolecule] = []
        for i in 0..<n {
            guard var t = BioData.residue(String(seq[i])) else { continue }
            // (a strand's Asp and Asn take their β rotamer, χ1 first)
            if built[i].isStrand, let beta = t.betaRotamer {
                for (k, chi) in beta.chis.enumerated() where k < t.chis.count { t = t.settingChi(t.chis[k].index, to: chi) }
            }
            templates.append(t)
            add(i, "N", "N", N[i])
            add(i, "CA", "C", CA[i])
            add(i, "C", "C", C[i])
            // 3. O trans to the next N, in the peptide plane; the last residue's OXT.
            add(i, "O", "O", BioV.place(N[i], CA[i], C[i], length: cO, angle: angCACO, torsion: psi[i] + 180))
            if i == n - 1 { add(i, "OXT", "O", BioV.place(N[i], CA[i], C[i], length: 1.25, angle: 117.0, torsion: psi[i])) }
            // 5. The side chain in the residue's frame.
            let f = frame(n: N[i], ca: CA[i], c: C[i])
            for a in sideChainAtoms(t, ph7: ph7) { add(i, a.name, a.element, f.apply(a.pos)) }
            // 4. The N-terminus's H atoms from the template (H, H2 and at pH 7 H3; Pro: H and at pH 7 H2); else the
            // amide H on the bisector of C–N–CA's outside (none on Pro).
            if i == 0 {
                let hs = t.code == "PRO" ? ["H"] + (ph7 ? ["H2"] : []) : ["H", "H2"] + (ph7 ? ["H3"] : [])
                for h in hs { if let p = t.pos(h) { add(i, h, "H", f.apply(p)) } }
            } else if t.code != "PRO" {
                add(i, "H", "H", N[i] + nH * BioV.unit(BioV.unit(N[i] - C[i - 1]) + BioV.unit(N[i] - CA[i])))
            }
            // (the neutral C-terminus's H on OXT, where the template has it against its own C=O)
            if i == n - 1 && !ph7, let o = names[i]["O"], let oxt = names[i]["OXT"],
               let to = t.pos("O"), let tc = t.pos("C"), let tx = t.pos("OXT"), let th = t.pos("HXT") {
                let len = BioV.distance(tx, th), ang = BioV.angle(tc, tx, th), tor = BioV.dihedral(to, tc, tx, th)
                add(i, "HXT", "H", BioV.place(atoms[o].pos, C[i], atoms[oxt].pos, length: len, angle: ang, torsion: tor))
            }
        }
        guard templates.count == n else { return peptide }

        // Bonds: each residue's from its template (where both atoms are there), and the peptide bonds.
        var bonds: [BiomolPeptide.Bond] = []
        for i in 0..<n {
            for b in templates[i].bonds {
                if let x = names[i][b.a], let y = names[i][b.b] { bonds.append(BiomolPeptide.Bond(a: x, b: y, order: b.order)) }
            }
            if i > 0, let c = names[i - 1]["C"], let nn = names[i]["N"] { bonds.append(BiomolPeptide.Bond(a: c, b: nn, order: 1, peptide: true)) }
        }
        peptide.atoms = atoms
        peptide.bonds = bonds
        peptide.backbone = (0..<n).map { i in (n: names[i]["N"] ?? 0, ca: names[i]["CA"] ?? 0, c: names[i]["C"] ?? 0, o: names[i]["O"] ?? 0,
                                               h: i == 0 ? nil : names[i]["H"]) }
        peptide.rotamers = templates.enumerated().map { i, t in
            t.chis.isEmpty ? nil : (built[i].isStrand && t.betaRotamer != nil ? t.betaRotamer?.name : t.rotamer)
        }

        // 6. Clash relief.
        if relieve { BiomolClashRelief.run(&peptide, templates: templates, names: names) }
        peptide.hBonds = hBonds(in: peptide)
        return peptide
    }

    /// A residue's frame (§6.3, §6.4 step 5): origin CA, x toward C, N in the xy-plane with y > 0, z = x × y.
    static func frame(n: BioVec, ca: BioVec, c: BioVec) -> BioFrame {
        let ex = BioV.unit(c - ca)
        let v = n - ca
        let ey = BioV.unit(v - BioV.dot(v, ex) * ex)
        return BioFrame(origin: ca, x: ex, y: ey, z: BioV.cross(ex, ey))
    }

    // MARK: H-bonds

    /// The backbone H-bonds: every N–H (the atom named H on a residue's N) to every other residue's C=O O, where N···O
    /// is under 3.3 Å and the angle N–H···O over 140° (the β-sheet search's criterion, §6.4). Residues next to each other
    /// never count.
    static func hBonds(in p: BiomolPeptide, maxDistance: Double = 3.3, minAngle: Double = 140) -> [BiomolHBond] {
        hBonds(donors: [p], acceptors: [p], sameChain: true, maxDistance: maxDistance, minAngle: minAngle)
    }

    /// The H-bonds from the N–H of `donors`' residues to the C=O of `acceptors`' (between strands: sameChain false).
    static func hBonds(donors: [BiomolPeptide], acceptors: [BiomolPeptide], sameChain: Bool, maxDistance: Double = 3.3,
                       minAngle: Double = 140) -> [BiomolHBond] {
        var out: [BiomolHBond] = []
        for d in donors {
            for i in 0..<d.count {
                guard let h = d.atom(i, "H") else { continue }
                let nPos = d.atoms[d.backbone[i].n].pos, hPos = d.atoms[h].pos
                for a in acceptors {
                    for j in 0..<a.count {
                        if sameChain && abs(i - j) < 2 { continue }
                        let oPos = a.atoms[a.backbone[j].o].pos
                        let dist = BioV.distance(nPos, oPos)
                        guard dist < maxDistance, BioV.angle(nPos, hPos, oPos) > minAngle else { continue }
                        out.append(BiomolHBond(donor: i, acceptor: j, n: d.backbone[i].n, h: h, o: a.backbone[j].o, distance: dist))
                    }
                }
            }
        }
        return out
    }

    // MARK: The helix's measures

    /// A helix's residues per turn, rise per residue (Å; negative for a left-handed helix) and CA radius (Å), fitted to
    /// its CA atoms as §6.4's verification does: the axis from the mean of the local axes (each the cross product of two
    /// neighbouring CA bisectors), the rise along it, the turn from a circle fitted to the CAs seen down it.
    static func helixParameters(_ ca: [BioVec]) -> (perTurn: Double, rise: Double, radius: Double) {
        let n = ca.count
        guard n >= 5 else { return (0, 0, 0) }
        var axis = BioVec(0, 0, 0)
        for i in 1..<(n - 2) {
            let bis1 = (ca[i - 1] - ca[i]) + (ca[i + 1] - ca[i])
            let bis2 = (ca[i] - ca[i + 1]) + (ca[i + 2] - ca[i + 1])
            axis += BioV.unit(BioV.cross(bis1, bis2))
        }
        axis = BioV.unit(axis)
        let z = ca.map { BioV.dot($0, axis) }
        let rise = (z[n - 1] - z[0]) / Double(n - 1)
        let e1 = BioV.unit(BioV.cross(axis, abs(axis.x) < 0.9 ? BioVec(1, 0, 0) : BioVec(0, 1, 0)))
        let e2 = BioV.cross(axis, e1)
        let xy = ca.map { p -> (Double, Double) in
            let q = p - BioV.dot(p, axis) * axis
            return (BioV.dot(q, e1), BioV.dot(q, e2))
        }
        // (the circle: least squares on 2x·cx + 2y·cy + k = x² + y²)
        var m = [[Double]](repeating: [0, 0, 0], count: 3)
        var v = [0.0, 0.0, 0.0]
        for (x, y) in xy {
            let row = [2 * x, 2 * y, 1.0], b = x * x + y * y
            for r in 0..<3 {
                for c in 0..<3 { m[r][c] += row[r] * row[c] }
                v[r] += row[r] * b
            }
        }
        let sol = solve3(m, v)
        let cx = sol[0], cy = sol[1]
        let radius = xy.map { (($0.0 - cx) * ($0.0 - cx) + ($0.1 - cy) * ($0.1 - cy)).squareRoot() }.reduce(0, +) / Double(n)
        var angles = xy.map { atan2($0.1 - cy, $0.0 - cx) }
        for k in 1..<n {
            while angles[k] - angles[k - 1] > .pi { angles[k] -= 2 * .pi }
            while angles[k] - angles[k - 1] < -.pi { angles[k] += 2 * .pi }
        }
        let twist = (angles[n - 1] - angles[0]) / Double(n - 1) * 180 / .pi
        guard abs(twist) > 1e-9 else { return (0, rise, radius) }
        return (360 / abs(twist), rise * (twist < 0 ? -1 : 1), radius)
    }

    /// Ax = b for a 3×3 system (Cramer's rule; zeros if singular).
    static func solve3(_ a: [[Double]], _ b: [Double]) -> [Double] {
        func det(_ m: [[Double]]) -> Double {
            m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0])
                + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0])
        }
        let d = det(a)
        guard abs(d) > 1e-12 else { return [0, 0, 0] }
        return (0..<3).map { k in
            var m = a
            for r in 0..<3 { m[r][k] = b[r] }
            return det(m) / d
        }
    }
}

// MARK: - Clash relief (§6.4 step 6)

/// The side chains' clash relief, as the specification's script does it: (a) twice, the residues with the most side-
/// chain heavy atoms beyond CB first (ties by index), each that comes within 3.0 Å of another residue's heavy atom tries
/// its rotamers in order and keeps the one that leaves it farthest away (the first on ties); (b) each pair of side
/// chains closer than 2.8 Å tries every pair of their rotamers. Proline is never turned.
enum BiomolClashRelief {
    struct Side {
        let residue: Int
        /// Heavy atoms beyond CB.
        let heavy: [Int]
        /// Each χ: its four atoms (indices) and the atoms it turns (beyond its bond).
        let chis: [(atoms: [Int], moving: [Int])]
        /// Its rotamers to try: the default (or the β rotamer in a strand), then the rest, most common first.
        let rotamers: [[Double]]
    }

    static func run(_ p: inout BiomolPeptide, templates: [BioMolecule], names: [[String: Int]]) {
        let n = p.count
        var sides: [Side?] = Array(repeating: nil, count: n)
        for i in 0..<n {
            let t = templates[i]
            guard !t.chis.isEmpty, t.code != "PRO" else { continue }
            var adjacent: [String: [String]] = [:]
            for b in t.bonds where names[i][b.a] != nil && names[i][b.b] != nil {
                adjacent[b.a, default: []].append(b.b)
                adjacent[b.b, default: []].append(b.a)
            }
            var chis: [(atoms: [Int], moving: [Int])] = []
            for c in t.chis {
                let idx = c.atoms.compactMap { names[i][$0] }
                guard idx.count == 4 else { continue }
                // (everything reached from the bond's far atom without crossing back over it)
                var far: Set<String> = [c.atoms[2]]
                var stack = [c.atoms[2]]
                while let x = stack.popLast() {
                    for y in adjacent[x] ?? [] where y != c.atoms[1] && !far.contains(y) {
                        far.insert(y)
                        stack.append(y)
                    }
                }
                far.remove(c.atoms[2])
                chis.append((idx, far.compactMap { names[i][$0] }.sorted()))
            }
            let heavy = names[i].filter { !BiomolPeptideBuilder.nearBackbone.contains($0.key) && p.atoms[$0.value].element != "H" }.map(\.value).sorted()
            sides[i] = Side(residue: i, heavy: heavy, chis: chis, rotamers: rotamers(t, beta: p.built[i].isStrand))
        }
        let heavyAll = p.atoms.indices.filter { p.atoms[$0].element != "H" }

        func set(_ s: Side, _ target: [Double]) {
            for (k, chi) in s.chis.enumerated() where k < target.count {
                let a = p.atoms[chi.atoms[0]].pos, b = p.atoms[chi.atoms[1]].pos, c = p.atoms[chi.atoms[2]].pos, d = p.atoms[chi.atoms[3]].pos
                let turn = target[k] - BioV.dihedral(a, b, c, d)
                let axis = c - b
                for m in chi.moving { p.atoms[m].pos = c + BioV.rotate(p.atoms[m].pos - c, axis: axis, degrees: turn) }
            }
        }
        func mind(_ s: Side) -> Double {
            guard !s.heavy.isEmpty else { return 9 }
            var best = Double.greatestFiniteMagnitude
            for k in s.heavy {
                let q = p.atoms[k].pos
                for j in heavyAll where p.atoms[j].residue != s.residue {
                    let d = q - p.atoms[j].pos
                    let d2 = d.x * d.x + d.y * d.y + d.z * d.z
                    if d2 < best { best = d2 }
                }
            }
            return best.squareRoot()
        }
        func pairMin(_ a: Side, _ b: Side) -> Double {
            guard !a.heavy.isEmpty, !b.heavy.isEmpty else { return 9 }
            var best = Double.greatestFiniteMagnitude
            for x in a.heavy { for y in b.heavy { best = min(best, BioV.distance(p.atoms[x].pos, p.atoms[y].pos)) } }
            return best
        }
        var chosen: [Int] = Array(repeating: 0, count: n)

        // (a) Each crowded side chain on its own, twice.
        for _ in 0..<2 {
            let order = (0..<n).sorted { a, b in
                let ca = sides[a]?.heavy.count ?? 0, cb = sides[b]?.heavy.count ?? 0
                return ca != cb ? ca > cb : a < b
            }
            for i in order {
                guard let s = sides[i], mind(s) < 3.0 else { continue }
                var best: (m: Double, k: Int)?
                for (k, r) in s.rotamers.enumerated() {
                    set(s, r)
                    let m = mind(s)
                    if best == nil || m > best!.m + 1e-9 { best = (m, k) }
                }
                if let best {
                    set(s, s.rotamers[best.k])
                    chosen[i] = best.k
                }
            }
        }
        // (b) Pairs of side chains still closer than 2.8 Å.
        for i in 0..<n {
            for j in (i + 1)..<n + 1 where j < n {
                guard let si = sides[i], let sj = sides[j], pairMin(si, sj) < 2.8 else { continue }
                var best: (m: Double, a: Int, b: Int)?
                for (a, ra) in si.rotamers.enumerated() {
                    for (b, rb) in sj.rotamers.enumerated() {
                        set(si, ra)
                        set(sj, rb)
                        let m = min(mind(si), mind(sj))
                        if best == nil || m > best!.m + 1e-9 { best = (m, a, b) }
                    }
                }
                if let best {
                    set(si, si.rotamers[best.a])
                    set(sj, sj.rotamers[best.b])
                    chosen[i] = best.a
                    chosen[j] = best.b
                }
            }
        }
        // (the rotamers' names, as chosen)
        for i in 0..<n {
            guard sides[i] != nil else { continue }
            let t = templates[i]
            p.rotamers[i] = rotamerNames(t, beta: p.built[i].isStrand)[chosen[i]]
        }
    }

    /// A residue's rotamers in the order the relief tries them: its default (the template's χ angles; the β rotamer for
    /// Asp and Asn in a strand), then every other, most common first, without repeating the first.
    static func rotamers(_ t: BioMolecule, beta: Bool) -> [[Double]] { list(t, beta: beta).map(\.chis) }

    static func rotamerNames(_ t: BioMolecule, beta: Bool) -> [String] { list(t, beta: beta).map(\.name) }

    private static func list(_ t: BioMolecule, beta: Bool) -> [BioRotamer] {
        let base = BioRotamer(name: t.rotamer ?? "default", chis: t.chis.map(\.degrees))
        let first = beta ? (t.betaRotamer ?? base) : base
        let rest = (beta && t.betaRotamer != nil ? [base] : []) + t.alternatives
        let key = first.chis.map { Int($0.rounded()) }
        return [first] + rest.filter { $0.chis.map { Int($0.rounded()) } != key }
    }
}

// MARK: - The β-sheet demo (§6.4)

/// Three antiparallel strands from §6.4's transforms: S1 built as a β-strand from the origin, S2 = R·S1 + t (its
/// residue 6 opposite S1's residue 0), S3 = S1 + Δ, and the backbone H-bonds between them.
struct BiomolSheet: Sendable {
    let strands: [BiomolPeptide]
    /// H-bonds between neighbouring strands: (donor strand, acceptor strand, the bond).
    let hBonds: [(donor: Int, acceptor: Int, bond: BiomolHBond)]

    /// R (rows), t and Δ, read from the specification's text.
    static let transforms: (r: [[Double]], t: BioVec, delta: BioVec) = {
        var r: [[Double]] = [], t = BioVec(0, 0, 0), d = BioVec(0, 0, 0)
        for line in BioData.sheetTransformText.split(separator: "\n").map(String.init) {
            let numbers = line.replacingOccurrences(of: "−", with: "-")
                .split(whereSeparator: { " ,[]=".contains($0) }).compactMap { Double($0) }
            if line.hasPrefix("R"), numbers.count == 9 { r = [Array(numbers[0..<3]), Array(numbers[3..<6]), Array(numbers[6..<9])] }
            if line.hasPrefix("t"), numbers.count >= 3 { t = BioVec(numbers[0], numbers[1], numbers[2]) }
            if line.hasPrefix("Δ"), numbers.count >= 3 { d = BioVec(numbers[0], numbers[1], numbers[2]) }
        }
        return (r, t, d)
    }()

    /// The demo for a 7-residue sequence (Gly₇ for the test vectors; the view uses Ala₇ so the R groups show).
    static func build(_ sequence: String = "AAAAAAA", ph7: Bool = true) -> BiomolSheet {
        let s1 = BiomolPeptideBuilder.build(sequence, ss: Array(repeating: .strand, count: 7), ph7: ph7)
        let tr = transforms
        let s2 = s1.transformed(tr.r, tr.t)
        let s3 = s1.moved(by: tr.delta)
        var hb: [(donor: Int, acceptor: Int, bond: BiomolHBond)] = []
        let strands = [s1, s2, s3]
        for (a, b) in [(0, 1), (1, 0), (1, 2), (2, 1)] {
            for h in BiomolPeptideBuilder.hBonds(donors: [strands[a]], acceptors: [strands[b]], sameChain: false) {
                hb.append((a, b, h))
            }
        }
        return BiomolSheet(strands: strands, hBonds: hb)
    }
}

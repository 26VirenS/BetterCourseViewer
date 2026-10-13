import Foundation

// The Nucleotides page's model (spec §4.2 Nucleotides, §6.5, §6.6): the eight nucleotides — dAMP, dCMP, dGMP, dTMP
// and AMP, CMP, GMP, UMP — and ATP in 3D from their CCD coordinates, each atom sorted into its part (phosphate group,
// sugar, nitrogenous base), coloured by part if asked, labelled with leader lines ("Phosphate group", "Deoxyribose (no
// O on 2′)" or "Ribose (2′–OH)", "Nitrogenous base", "Glycosidic bond (C1′–N)"), its sugar's carbons numbered 1′ to
// 5′ and its base's ring atoms 1 to 9 (where "5′ and 3′" come from), and DNA's and RNA's differences lit: the 2′
// position (H against OH) and the 5-methyl (T has it; U does not). And the bases in 2D, from the B-DNA model's pairs
// (§6.7): purines with two rings, pyrimidines with one, and the Watson–Crick pairs with their H-bonds between the exact
// atoms; A·U is the A·T drawing without the methyl, an H on C5 instead (§6.5).
//
// Ids: "part:phosphate:a<i>", "part:sugar:a<i>", "part:base:a<i>" (an atom), "part:glyco" (the glycosidic bond),
// "diff:2prime" and "diff:methyl" (the lit differences).

/// A part of a nucleotide.
enum DNANucleotidePart: String, CaseIterable, Sendable {
    case phosphate, sugar, base, glyco
}

/// What the Nucleotides page's 3D view is drawn with.
struct DNANucleotideOptions: Hashable, Sendable {
    /// The CCD code: DA DC DG DT A C G U ATP.
    var code = "DA"
    var style: BioAtomStyle = .ballStick
    var hydrogens = true
    /// As at pH 7: the phosphate's OH hydrogens gone (−2; ATP −4).
    var ph7 = true
    /// Coloured by part (phosphate, sugar, base) rather than by element.
    var parts = true
    /// The sugar's carbons 1′–5′ and the base's ring atoms 1–9.
    var numbers = false
    /// DNA against RNA: the 2′ position and the 5-methyl lit.
    var compare = false
    var dark = false

    var key: String { "nt|\(code)|\(style.rawValue)|\(hydrogens)|\(ph7)|\(parts)|\(numbers)|\(compare)|\(dark)" }
}

/// What a nucleotide is called and what it is.
struct DNANucleotideInfo: Equatable {
    let code: String
    /// "dAMP".
    let short: String
    /// "deoxyadenosine monophosphate".
    let name: String
    let base: Character
    /// A deoxyribonucleotide (DNA's), else a ribonucleotide (RNA's; ATP too).
    let isDNA: Bool
    var isTriphosphate: Bool { code == "ATP" }
}

/// A base or a base pair in 2D (Å, from §6.7: the origin midway between the C1′ atoms, +x toward the pyrimidine,
/// +y toward the major groove).
struct DNABase2D: Equatable {
    struct Atom: Equatable {
        /// The base it belongs to (A, C, G, T, U).
        let base: Character
        let name: String
        let element: String
        var x: Double
        var y: Double
    }

    struct HBond: Equatable {
        /// Atom indices: the donor's H, the donor and the acceptor.
        let h: Int
        let donor: Int
        let acceptor: Int
        /// Donor to acceptor, heavy atom to heavy atom, in the B-DNA model (Å).
        let distance: Double
        /// The same in the flat drawing.
        let flat: Double
        /// "A N6–H → T O4".
        let text: String
    }

    var atoms: [Atom] = []
    var bonds: [(a: Int, b: Int, order: Int)] = []
    var hBonds: [HBond] = []
    /// Each ring's atoms in order round it (a purine two, a pyrimidine one).
    var rings: [(base: Character, atoms: [Int])] = []

    static func == (a: DNABase2D, b: DNABase2D) -> Bool { a.atoms == b.atoms && a.hBonds == b.hBonds }

    func index(_ base: Character, _ name: String) -> Int? { atoms.firstIndex { $0.base == base && $0.name == name } }
}

enum DNANucleotides {
    /// The chooser's two columns and ATP.
    static let dna = ["DA", "DC", "DG", "DT"]
    static let rna = ["A", "C", "G", "U"]
    static let all = dna + rna + ["ATP"]

    static func info(_ code: String) -> DNANucleotideInfo {
        switch code {
        case "DA": return DNANucleotideInfo(code: code, short: "dAMP", name: "deoxyadenosine monophosphate", base: "A", isDNA: true)
        case "DC": return DNANucleotideInfo(code: code, short: "dCMP", name: "deoxycytidine monophosphate", base: "C", isDNA: true)
        case "DG": return DNANucleotideInfo(code: code, short: "dGMP", name: "deoxyguanosine monophosphate", base: "G", isDNA: true)
        case "DT": return DNANucleotideInfo(code: code, short: "dTMP", name: "thymidine monophosphate", base: "T", isDNA: true)
        case "A": return DNANucleotideInfo(code: code, short: "AMP", name: "adenosine monophosphate", base: "A", isDNA: false)
        case "C": return DNANucleotideInfo(code: code, short: "CMP", name: "cytidine monophosphate", base: "C", isDNA: false)
        case "G": return DNANucleotideInfo(code: code, short: "GMP", name: "guanosine monophosphate", base: "G", isDNA: false)
        case "U": return DNANucleotideInfo(code: code, short: "UMP", name: "uridine monophosphate", base: "U", isDNA: false)
        default: return DNANucleotideInfo(code: "ATP", short: "ATP", name: "adenosine triphosphate", base: "A", isDNA: false)
        }
    }

    /// The colours of the parts: the phosphate orange, deoxyribose violet, ribose orange-red (the base its own).
    static let phosphateHex = BioHexPair("#ef9a1a", "#ffb547")
    static let deoxyriboseHex = BioPalette.strand1
    static let riboseHex = BioHexPair("#e8553a", "#ff7a5c")
    static let highlightHex = BioHexPair("#5e5ce6", "#7d7aff")

    /// The part an atom (by its CCD name) belongs to: the phosphate group (P, its oxygens with the bridging O5′, and
    /// their H; ATP's three), the sugar (the primed atoms), or the base (the rest).
    static func part(_ name: String) -> DNANucleotidePart {
        if name == "O5'" { return .phosphate }
        let phosphate: Set<String> = ["P", "OP1", "OP2", "OP3", "HOP2", "HOP3", "PA", "PB", "PG", "O1A", "O2A", "O3A", "O1B", "O2B",
                                      "O3B", "O1G", "O2G", "O3G", "HOA2", "HOB2", "HOG2", "HOG3"]
        if phosphate.contains(name) { return .phosphate }
        if name.contains("'") { return .sugar }
        return .base
    }

    /// The base's atom bonded to C1′: N9 in a purine, N1 in a pyrimidine.
    static func glycosidicN(_ base: Character) -> String { DNAModel.isPurine(base) ? "N9" : "N1" }

    /// A ring atom's number (N1 → "1" … N9 → "9"; C1′ → "1′" … C5′ → "5′"), nil for any other atom.
    static func number(_ name: String) -> String? {
        let sugar = ["C1'": "1′", "C2'": "2′", "C3'": "3′", "C4'": "4′", "C5'": "5′"]
        if let s = sugar[name] { return s }
        let ring: Set<String> = ["N1", "C2", "N3", "C4", "C5", "C6", "N7", "C8", "N9"]
        return ring.contains(name) ? String(name.dropFirst()) : nil
    }

    // MARK: The 3D view

    static func build(_ o: DNANucleotideOptions, look: DNAAtomLook = .standard) -> DNAScene {
        guard let mol = BioData.molecule(o.code) else { return DNAScene(content: BioSceneContent(), marks: []) }
        let info = info(o.code)
        let model = mol.model(ph7: o.ph7, hydrogens: o.hydrogens)
        let sugarHex = (info.isDNA ? deoxyriboseHex : riboseHex).hex(dark: o.dark)
        let baseHex = BioPalette.base(info.base).hex(dark: o.dark)
        func partHex(_ p: DNANucleotidePart) -> String {
            switch p {
            case .phosphate: return phosphateHex.hex(dark: o.dark)
            case .sugar: return sugarHex
            case .base, .glyco: return baseHex
            }
        }
        var items = BioMoleculeMeshes.items(model, style: o.style, radius: { look.radius($0.element, o.style) },
                                            color: { a in o.parts ? partHex(part(a.name)) : look.colour(a.element) })
        for j in items.indices {
            guard let id = items[j].id, id.hasPrefix("atom:"), let i = Int(id.dropFirst(5)) else { continue }
            items[j].id = "part:\(part(model.atoms[i].name).rawValue):a\(i)"
        }
        func p(_ name: String) -> BioVec? { model.atoms.first { $0.name == name }?.pos }
        func centroid(_ names: [String]) -> BioVec? {
            let ps = names.compactMap { p($0) }
            return ps.isEmpty ? nil : ps.reduce(BioVec(0, 0, 0), +) / Double(ps.count)
        }
        var labels: [BioLabel3D] = []
        var marks: [DNATextMark] = []

        // (the glycosidic bond, C1′–N, as a pickable bond of its own just over the drawn one)
        let nGly = glycosidicN(info.base)
        if let c1 = p("C1'"), let nn = p(nGly) {
            if o.style != .spaceFill {
                let r = o.style == .sticks ? 0.17 : 0.13
                items.append(BioSceneItem(id: "part:glyco", mesh: BioShapes.cylinder(from: c1, to: nn, radius: r, segments: 16),
                                          material: BioMaterial(o.parts ? (o.dark ? "#f2f2f7" : "#3a3a3c") : "#9e9e9e", roughness: 0.35)))
                labels.append(BioLabel3D(id: "part:glyco", text: "Glycosidic bond (C1′–\(nGly))", anchor: (c1 + nn) / 2, area: 1))
            }
        }
        let sugarRing = ["C1'", "C2'", "C3'", "C4'", "O4'"]
        let baseRing = ["N1", "C2", "N3", "C4", "C5", "C6", "N7", "C8", "N9"]
        if let a = p(info.isTriphosphate ? "PB" : "P") {
            labels.append(BioLabel3D(id: "part:phosphate", text: info.isTriphosphate ? "Triphosphate (α, β, γ)" : "Phosphate group", anchor: a, area: 3))
        }
        if let c = centroid(sugarRing) {
            labels.append(BioLabel3D(id: "part:sugar", text: info.isDNA ? "Deoxyribose (no O on 2′)" : "Ribose (2′–OH)", anchor: c, area: 3))
        }
        if let c = centroid(baseRing) {
            labels.append(BioLabel3D(id: "part:base", text: "Nitrogenous base", anchor: c, area: 3))
        }

        // (DNA against RNA: the 2′ position — H only, or OH — and the 5-methyl that T has and U lacks)
        if o.compare {
            let lit = highlightHex.hex(dark: o.dark)
            if info.isDNA, let c2 = p("C2'") {
                items.append(BioSceneItem(id: "diff:2prime", mesh: BioShapes.sphere(radius: 1.35, rings: 14, segments: 28).translated(c2),
                                          material: BioMaterial(lit, alpha: 0.32, roughness: 0.2), order: 3))
                labels.append(BioLabel3D(id: "diff:2prime", text: "2′: H only (no OH)", anchor: c2 + BioVec(0, 0, 1.35), area: 2))
            } else if let o2 = p("O2'") {
                items.append(BioSceneItem(id: "diff:2prime", mesh: BioShapes.sphere(radius: 1.35, rings: 14, segments: 28).translated(o2),
                                          material: BioMaterial(lit, alpha: 0.32, roughness: 0.2), order: 3))
                labels.append(BioLabel3D(id: "diff:2prime", text: "2′–OH (ribose)", anchor: o2 + BioVec(0, 0, 1.35), area: 2))
            }
            if info.base == "T", let c7 = p("C7") {
                items.append(BioSceneItem(id: "diff:methyl", mesh: BioShapes.sphere(radius: 1.6, rings: 14, segments: 28).translated(c7),
                                          material: BioMaterial(lit, alpha: 0.32, roughness: 0.2), order: 3))
                labels.append(BioLabel3D(id: "diff:methyl", text: "5-methyl (T has it)", anchor: c7 + BioVec(0, 0, 1.6), area: 2))
            } else if info.base == "U", let c5 = p("C5") {
                items.append(BioSceneItem(id: "diff:methyl", mesh: BioShapes.sphere(radius: 1.3, rings: 14, segments: 28).translated(c5),
                                          material: BioMaterial(lit, alpha: 0.32, roughness: 0.2), order: 3))
                labels.append(BioLabel3D(id: "diff:methyl", text: "No 5-methyl (U)", anchor: c5 + BioVec(0, 0, 1.3), area: 2))
            }
        }

        // (the numbers, just outside each ring atom, on the side away from its ring's middle)
        if o.numbers {
            let ink = o.dark ? "#ffffff" : "#1c1c1e"
            let sugarMid = centroid(sugarRing)
            let baseMid = centroid(["N1", "C2", "N3", "C4", "C5", "C6"]) ?? centroid(baseRing)
            for a in model.atoms {
                guard let text = number(a.name) else { continue }
                let mid = (part(a.name) == .sugar ? sugarMid : (a.name == "N7" || a.name == "C8" || a.name == "N9" ? centroid(["C4", "C5", "N7", "C8", "N9"]) : baseMid)) ?? a.pos
                let out = BioV.unit(a.pos - mid)
                let r = look.radius(a.element, o.style)
                marks.append(DNATextMark(text: text, pos: a.pos + out * (r + 0.42), size: 0.6, hex: ink, lift: 0.3))
            }
        }
        return DNAScene(content: BioSceneContent(items: items, labels: labels), marks: marks)
    }

    // MARK: The bases in 2D

    /// The Watson–Crick H-bonds of each pair (§6.5): donor base and atom, acceptor base and atom.
    static func hBondSpec(_ pair: String) -> [(Character, String, Character, String)] {
        switch pair {
        case "GC": return [("G", "N1", "C", "N3"), ("C", "N4", "G", "O6"), ("G", "N2", "C", "O2")]
        case "AU": return [("A", "N6", "U", "O4"), ("U", "N3", "A", "N1")]
        default: return [("A", "N6", "T", "O4"), ("T", "N3", "A", "N1")]
        }
    }

    /// A Watson–Crick pair in 2D: "AT", "GC" or "AU" (the A·T drawing without C7 and its H atoms, with an H5 on C5
    /// 1.08 Å along the C7 direction).
    static func pair2D(_ which: String) -> DNABase2D {
        let source = which == "GC" ? "GC" : "AT"
        var atoms = (BioData.basePairs2D[source] ?? []).map { a in
            DNABase2D.Atom(base: a.base, name: a.name, element: element(a.name), x: a.x, y: a.y)
        }
        if which == "AU" {
            if let c5 = atoms.first(where: { $0.base == "T" && $0.name == "C5" }), let c7 = atoms.first(where: { $0.base == "T" && $0.name == "C7" }) {
                let dx = c7.x - c5.x, dy = c7.y - c5.y, len = (dx * dx + dy * dy).squareRoot()
                atoms.removeAll { $0.base == "T" && ["C7", "H71", "H72", "H73"].contains($0.name) }
                atoms.append(DNABase2D.Atom(base: "T", name: "H5", element: "H", x: c5.x + dx / len * 1.08, y: c5.y + dy / len * 1.08))
            }
            atoms = atoms.map { a in a.base == "T" ? DNABase2D.Atom(base: "U", name: a.name, element: a.element, x: a.x, y: a.y) : a }
        }
        var out = DNABase2D(atoms: atoms)
        out.bonds = bonds(for: atoms)
        out.rings = rings(for: atoms)
        // (each H-bond's length from the B-DNA model in 3D, as §6.5 measures it; the drawing's flat projection is within
        // 0.02 Å of it)
        let duplex = BioBDNA.build(which == "GC" ? "G" : "A")
        func length3D(_ db: Character, _ dn: String, _ ab: Character, _ an: String) -> Double? {
            func strand(_ b: Character) -> Int { DNAModel.isPurine(b) ? 1 : 2 }
            guard let p = duplex.pos(strand: strand(db), pair: 0, name: dn), let q = duplex.pos(strand: strand(ab), pair: 0, name: an) else { return nil }
            return BioV.distance(p, q)
        }
        for (db, dn, ab, an) in hBondSpec(which) {
            guard let d = out.index(db, dn), let a = out.index(ab, an) else { continue }
            // (the donor's H that points at the acceptor)
            let hs = out.bonds.compactMap { b -> Int? in
                let other = b.a == d ? b.b : (b.b == d ? b.a : nil)
                return other.flatMap { atoms[$0].element == "H" ? $0 : nil }
            }
            func dist(_ i: Int, _ j: Int) -> Double { ((atoms[i].x - atoms[j].x) * (atoms[i].x - atoms[j].x) + (atoms[i].y - atoms[j].y) * (atoms[i].y - atoms[j].y)).squareRoot() }
            guard let h = hs.min(by: { dist($0, a) < dist($1, a) }) else { continue }
            out.hBonds.append(DNABase2D.HBond(h: h, donor: d, acceptor: a, distance: length3D(db, dn, ab, an) ?? dist(d, a),
                                              flat: dist(d, a), text: "\(db) \(dn)–H → \(ab) \(an)"))
        }
        return out
    }

    /// One base in 2D, as it sits in its pair, moved so its rings' middle is at the origin (C1′ kept: where the sugar
    /// joins).
    static func base2D(_ b: Character) -> DNABase2D {
        let pair = (b == "G" || b == "C") ? pair2D("GC") : (b == "U" ? pair2D("AU") : pair2D("AT"))
        let atoms = pair.atoms.filter { $0.base == b }
        let ringNames: Set<String> = ["N1", "C2", "N3", "C4", "C5", "C6", "N7", "C8", "N9"]
        let ringAtoms = atoms.filter { ringNames.contains($0.name) }
        let cx = ringAtoms.map(\.x).reduce(0, +) / Double(max(ringAtoms.count, 1))
        let cy = ringAtoms.map(\.y).reduce(0, +) / Double(max(ringAtoms.count, 1))
        let moved = atoms.map { DNABase2D.Atom(base: $0.base, name: $0.name, element: $0.element, x: $0.x - cx, y: $0.y - cy) }
        var out = DNABase2D(atoms: moved)
        out.bonds = bonds(for: moved)
        out.rings = rings(for: moved)
        return out
    }

    private static func element(_ name: String) -> String { String(name.prefix(1)) }

    /// The bonds among these atoms, by each base's nucleotide block (§6.6), with their orders.
    private static func bonds(for atoms: [DNABase2D.Atom]) -> [(a: Int, b: Int, order: Int)] {
        var out: [(a: Int, b: Int, order: Int)] = []
        for base in Set(atoms.map(\.base)) {
            guard let block = BioData.molecule(base == "U" ? "U" : "D" + String(base)) else { continue }
            var idx: [String: Int] = [:]
            for (i, a) in atoms.enumerated() where a.base == base { idx[a.name] = i }
            for b in block.bonds {
                if let x = idx[b.a], let y = idx[b.b] { out.append((x, y, b.order)) }
            }
        }
        return out.sorted { $0.a != $1.a ? $0.a < $1.a : $0.b < $1.b }
    }

    /// The rings: a purine's six- and five-membered rings, a pyrimidine's one.
    private static func rings(for atoms: [DNABase2D.Atom]) -> [(base: Character, atoms: [Int])] {
        var out: [(base: Character, atoms: [Int])] = []
        for base in Array(Set(atoms.map(\.base))).sorted() {
            func i(_ name: String) -> Int? { atoms.firstIndex { $0.base == base && $0.name == name } }
            let six = ["N1", "C2", "N3", "C4", "C5", "C6"].compactMap(i)
            if six.count == 6 { out.append((base, six)) }
            let five = ["C4", "C5", "N7", "C8", "N9"].compactMap(i)
            if five.count == 5 { out.append((base, five)) }
        }
        return out
    }

    // MARK: Checks

    static func checks(_ r: inout BioCheckReport) {
        // (every atom of every nucleotide in a part; the parts' heavy atoms as the chemistry has them)
        var counts: [String] = []
        var ok = true
        for code in all {
            guard let m = BioData.molecule(code) else { ok = false; continue }
            let heavy = m.atoms(ph7: false, hydrogens: false)
            let ph = heavy.filter { part($0.name) == .phosphate }.count
            let su = heavy.filter { part($0.name) == .sugar }.count
            let ba = heavy.filter { part($0.name) == .base }.count
            let i = info(code)
            let wantPh = i.isTriphosphate ? 13 : 5
            let wantSu = i.isDNA ? 7 : 8
            let wantBa: [Character: Int] = ["A": 10, "C": 8, "G": 11, "T": 9, "U": 8]
            if ph != wantPh || su != wantSu || ba != wantBa[i.base] { ok = false }
            // (deoxy: no O2′; ribo: O2′; T has C7; U none)
            if i.isDNA == (m.atom("O2'") != nil) { ok = false }
            if (i.base == "T") != (m.atom("C7") != nil) { ok = false }
            counts.append("\(i.short)\(ph)/\(su)/\(ba)")
        }
        r.check("dna.nucleotide-parts", counts.joined(separator: ","), ok && counts.count == 9)
        // (the DNA monophosphates' masses are those the Sequence page sums)
        let masses = ["DA": "A", "DC": "C", "DG": "G", "DT": "T"].map { code, b -> Double in
            abs((BioData.molecule(code)?.mass ?? 0) - (BioSeq.monophosphateMass[Character(b)] ?? 0))
        }
        r.check("dna.nucleotide-masses", String(format: "%.3f", masses.max() ?? 1), (masses.max() ?? 1) < 0.01)

        // (the 3D view: labels name items; numbers 1′–5′ and the base's ring; the differences lit)
        let dt = build(DNANucleotideOptions(code: "DT", numbers: true, compare: true))
        let labelled = dt.content.labels.allSatisfy { l in dt.content.items.contains { BioSceneContent.matches($0.id, l.id) } }
        let nums = Set(dt.marks.map(\.text))
        r.check("dna.nucleotide-view", "\(dt.content.labels.count)labels,\(dt.marks.count)numbers",
                labelled && dt.content.labels.count == 6 && nums == Set(["1′", "2′", "3′", "4′", "5′", "1", "2", "3", "4", "5", "6"]))
        let atp = build(DNANucleotideOptions(code: "ATP", numbers: true))
        r.check("dna.nucleotide-ATP", "\(atp.marks.count)numbers", atp.marks.count == 14 && atp.content.labels.contains { $0.text.hasPrefix("Triphosphate") })

        // (2D: two rings for a purine, one for a pyrimidine; the H-bonds' lengths as §6.5 measures them)
        let at = pair2D("AT"), gc = pair2D("GC"), au = pair2D("AU")
        let rings = ["A", "G", "C", "T", "U"].map { b in base2D(Character(b)).rings.count }
        r.check("dna.rings", rings.map(String.init).joined(separator: ","), rings == [2, 2, 1, 1, 1])
        let atd = at.hBonds.map(\.distance), gcd = gc.hBonds.map(\.distance)
        r.check("dna.2D.AT", atd.map { String(format: "%.2f", $0) }.joined(separator: ","),
                atd.count == 2 && abs(atd[0] - 2.84) < 0.01 && abs(atd[1] - 2.91) < 0.01)
        r.check("dna.2D.GC", gcd.map { String(format: "%.2f", $0) }.joined(separator: ","),
                gcd.count == 3 && abs(gcd[0] - 2.87) < 0.01 && abs(gcd[1] - 2.79) < 0.01 && abs(gcd[2] - 3.00) < 0.01)
        // (A·U: no methyl, an H5 1.08 Å from C5; the same two H-bonds)
        var h5d = 0.0
        if let h = au.index("U", "H5"), let c = au.index("U", "C5") {
            let dx: Double = au.atoms[h].x - au.atoms[c].x
            let dy: Double = au.atoms[h].y - au.atoms[c].y
            h5d = (dx * dx + dy * dy).squareRoot()
        }
        r.check("dna.2D.AU", String(format: "H5-C5=%.3f", h5d), abs(h5d - 1.08) < 1e-6 && au.index("U", "C7") == nil
                && au.hBonds.count == 2 && au.atoms.count == at.atoms.count - 3)
        // (each H-bond drawn from the donor's H nearest the acceptor, which is bonded to the donor)
        let hOK = (at.hBonds + gc.hBonds + au.hBonds).allSatisfy { hb in
            let all = [at, gc, au].first { $0.hBonds.contains(hb) } ?? at
            return all.atoms[hb.h].element == "H" && all.bonds.contains { ($0.a == hb.h && $0.b == hb.donor) || ($0.b == hb.h && $0.a == hb.donor) }
        }
        r.check("dna.2D.donor-H", "\(at.hBonds.count + gc.hBonds.count + au.hBonds.count)", hOK)
        let flatOff = (at.hBonds + gc.hBonds).map { abs($0.flat - $0.distance) }.max() ?? 1
        r.check("dna.2D.flat", String(format: "%.3f", flatOff), flatOff < 0.02)
    }
}

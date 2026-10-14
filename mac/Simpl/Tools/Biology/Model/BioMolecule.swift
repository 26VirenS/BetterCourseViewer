import Foundation

// The molecule blocks of the biology toolkit's data (spec §3.3): a residue's or a molecule's atoms (name, element,
// position in Å), its bonds with their orders, its aromatic rings, the rotamer and χ angles of an amino acid, and its
// pH 7 form — read from the text both apps share (BioDataText.swift), and turned into what the 3D views draw
// (BioMoleculeModel: atoms and bonds, as the chemistry builder's MoleculeModel has them, with no VSEPR — the
// coordinates are already right).

/// Where an atom of a template belongs: always, or only at a chain's end, or only in the pH 7 form.
enum BioAtomRole: String, Equatable {
    case always
    /// "# N-terminus only" (and Pro's "free Pro / N-terminus only"): the first residue's extra amine H.
    case nTerminus
    /// "# C-terminus only": OXT and HXT, the last residue's carboxyl.
    case cTerminus
    /// "# pH 7 form only": the extra H on N of the zwitterion.
    case ph7Only
}

/// An atom of a molecule block.
struct BioAtomRecord: Equatable {
    /// Its PDB/CCD name ("CA", "O5'", "H5''").
    let name: String
    /// Its element symbol ("C", "Fe").
    let element: String
    let pos: BioVec
    var role: BioAtomRole = .always
    /// The comment after `#`, if any.
    var comment: String = ""
}

/// A bond between two atoms of a block, by name; order 1, 2 or 3.
struct BioBondRecord: Equatable {
    let a: String
    let b: String
    let order: Int
}

/// A χ angle of an amino acid: its four atoms and its value in the template (degrees).
struct BioChi: Equatable {
    /// 1…4.
    let index: Int
    let atoms: [String]
    let degrees: Double
}

/// A rotamer of an amino acid: its name (Lovell 2000) and χ angles in degrees.
struct BioRotamer: Equatable {
    let name: String
    let chis: [Double]
}

/// A block's pH 7 form, as its "pH 7:" line says: atoms to add (those marked "pH 7 form only"), atoms to remove, and
/// the charges that result ("N becomes +1", "OXT becomes −1", "NZ is +1").
struct BioPH7: Equatable {
    let text: String
    var remove: [String] = []
    var add: [String] = []
    var charges: [String: Int] = [:]
}

/// A molecule block (§3.3): a residue template (§6.3) or a molecule (§6.6, §6.8, §6.9).
struct BioMolecule {
    enum Kind: String { case residue, molecule }

    let kind: Kind
    /// "ALA", "DA", "GLC", "POV".
    let code: String
    /// "Alanine", "dAMP (deoxyadenosine monophosphate)", "maltose: glucose α(1→4) glucose".
    let title: String
    /// "C10 H14 N5 O6 P" (molecules only).
    var formula: String?
    /// Molar mass in g/mol (molecules only).
    var mass: Double?
    var atoms: [BioAtomRecord] = []
    var bonds: [BioBondRecord] = []
    var aromatic: [String] = []
    /// The default rotamer's name ("mtt180") and its χ angles as the template has them.
    var rotamer: String?
    var chis: [BioChi] = []
    /// Asp and Asn: the rotamer used in a β-strand.
    var betaRotamer: BioRotamer?
    /// Every other rotamer, most common first (the peptide builder's clash relief tries them in turn).
    var alternatives: [BioRotamer] = []
    /// Pro: its ring, which is never rotated.
    var ring: String?
    var ph7: BioPH7?
    var notes: [String] = []

    func atom(_ name: String) -> BioAtomRecord? { atoms.first { $0.name == name } }
    func index(_ name: String) -> Int? { atoms.firstIndex { $0.name == name } }
    func pos(_ name: String) -> BioVec? { atom(name)?.pos }

    /// The atoms of a form: the pH 7 form (zwitterion, ionised groups) or the neutral one; with or without H; with or
    /// without the chain-end atoms (a free amino acid has both ends).
    func atoms(ph7: Bool = true, hydrogens: Bool = true, nTerminus: Bool = true, cTerminus: Bool = true) -> [BioAtomRecord] {
        let removed = Set(ph7 ? (self.ph7?.remove ?? []) : [])
        return atoms.filter { a in
            if !hydrogens && a.element == "H" { return false }
            switch a.role {
            case .ph7Only: if !ph7 { return false }
            case .nTerminus: if !nTerminus { return false }
            case .cTerminus: if !cTerminus { return false }
            case .always: break
            }
            return !removed.contains(a.name)
        }
    }

    /// The value of χ`index` as the atoms now lie (degrees), nil if the residue has no such χ.
    func chiValue(_ index: Int) -> Double? {
        guard let c = chis.first(where: { $0.index == index }) else { return nil }
        let p = c.atoms.compactMap { pos($0) }
        guard p.count == 4 else { return nil }
        return BioV.dihedral(p[0], p[1], p[2], p[3])
    }

    /// The block with χ`index` turned to `degrees` (§6.3): every atom on the far side of the χ bond — found by walking
    /// the bonds from its far atom without crossing it — turned about the bond. Set χ1 first, then χ2 and so on.
    func settingChi(_ index: Int, to degrees: Double) -> BioMolecule {
        guard let c = chis.first(where: { $0.index == index }), let now = chiValue(index),
              let b = pos(c.atoms[1]), let cc = pos(c.atoms[2]) else { return self }
        var adjacent: [String: [String]] = [:]
        for bond in bonds {
            adjacent[bond.a, default: []].append(bond.b)
            adjacent[bond.b, default: []].append(bond.a)
        }
        var far: Set<String> = [c.atoms[2]]
        var stack = [c.atoms[2]]
        while let x = stack.popLast() {
            for y in adjacent[x] ?? [] where y != c.atoms[1] && !far.contains(y) {
                far.insert(y)
                stack.append(y)
            }
        }
        far.remove(c.atoms[2])
        let axis = cc - b
        let turn = degrees - now
        var out = self
        out.atoms = atoms.map { a in
            guard far.contains(a.name) else { return a }
            return BioAtomRecord(name: a.name, element: a.element, pos: cc + BioV.rotate(a.pos - cc, axis: axis, degrees: turn),
                                 role: a.role, comment: a.comment)
        }
        return out
    }

    /// What a 3D view draws: the atoms of the form asked for, and the bonds between them; each atom tagged with its
    /// name (and `tag`, e.g. a residue "Lys 12").
    func model(ph7: Bool = true, hydrogens: Bool = true, nTerminus: Bool = true, cTerminus: Bool = true, tag: String = "") -> BioMoleculeModel {
        let chosen = atoms(ph7: ph7, hydrogens: hydrogens, nTerminus: nTerminus, cTerminus: cTerminus)
        var m = BioMoleculeModel()
        var ids: [String: Int] = [:]
        for a in chosen { ids[a.name] = m.add(a.element, a.pos, name: a.name, tag: tag) }
        for b in bonds {
            if let x = ids[b.a], let y = ids[b.b] { m.bond(x, y, order: b.order) }
        }
        return m
    }
}

/// Atoms and bonds for a 3D view (spec §3.1: the chemistry builder's MoleculeModel's shape, plus a name and a tag for
/// each atom — residue and part — and no VSEPR). Ids are the atoms' indices.
struct BioMoleculeModel {
    struct Atom: Equatable {
        let id: Int
        /// Its element ("C"): its colour and radius come from the chemistry builder's tables (MBChem).
        var element: String
        var pos: BioVec
        /// Its name in its block ("CA", "O5'").
        var name: String = ""
        /// What it belongs to ("Lys 12", "phosphate", "strand 1"), for picking and "Colour by Part".
        var tag: String = ""
    }

    struct Bond: Equatable {
        let a: Int
        let b: Int
        var order: Int
    }

    var atoms: [Atom] = []
    var bonds: [Bond] = []

    @discardableResult
    mutating func add(_ element: String, _ pos: BioVec, name: String = "", tag: String = "") -> Int {
        atoms.append(Atom(id: atoms.count, element: element, pos: pos, name: name, tag: tag))
        return atoms.count - 1
    }

    mutating func bond(_ a: Int, _ b: Int, order: Int = 1) {
        bonds.append(Bond(a: a, b: b, order: order))
    }

    /// Another model's atoms and bonds added to this one (its ids shifted); returns the first new id.
    @discardableResult
    mutating func append(_ other: BioMoleculeModel) -> Int {
        let base = atoms.count
        for a in other.atoms { atoms.append(Atom(id: base + a.id, element: a.element, pos: a.pos, name: a.name, tag: a.tag)) }
        for b in other.bonds { bonds.append(Bond(a: base + b.a, b: base + b.b, order: b.order)) }
        return base
    }

    /// The bounding sphere of the atoms' centres (Å), for framing.
    var bounds: (centre: BioVec, radius: Double) { BioV.boundingSphere(atoms.map(\.pos)) }

    /// Every atom moved by `offset`.
    func moved(by offset: BioVec) -> BioMoleculeModel {
        var m = self
        m.atoms = atoms.map { Atom(id: $0.id, element: $0.element, pos: $0.pos + offset, name: $0.name, tag: $0.tag) }
        return m
    }

    /// The model mirrored through the xy-plane (z → −z): an L-amino acid's D image (§4.4 Chirality).
    func mirrored() -> BioMoleculeModel {
        var m = self
        m.atoms = atoms.map { Atom(id: $0.id, element: $0.element, pos: BioVec($0.pos.x, $0.pos.y, -$0.pos.z), name: $0.name, tag: $0.tag) }
        return m
    }

    /// The bonds' lengths outside [lo, hi] Å, as "CA–CB 2.10".
    func oddBonds(lo: Double = 0.9, hi: Double = 1.9) -> [String] {
        bonds.compactMap { b in
            let d = BioV.distance(atoms[b.a].pos, atoms[b.b].pos)
            return d < lo || d > hi ? "\(atoms[b.a].name)–\(atoms[b.b].name) \(String(format: "%.2f", d))" : nil
        }
    }
}

// MARK: - The block parser (§3.3)

enum BioBlockParser {
    /// The keys a keyed line starts with (the rest of a line is an atom).
    private static let keys = ["bonds", "aromatic", "rotamer", "chi1", "chi2", "chi3", "chi4", "beta-strand rotamer",
                               "alternatives", "ring", "pH 7", "note"]

    /// Every block in a text of blocks, in order.
    static func parse(_ text: String) -> [BioMolecule] {
        var out: [BioMolecule] = []
        var current: BioMolecule?
        // (each keyed line gathered whole first: a line starting with a space or a tab carries on the one before)
        var keyed: [(key: String, value: String)] = []

        func finish() {
            guard var m = current else { return }
            for (k, v) in keyed { apply(k, v, to: &m) }
            out.append(m)
            current = nil
            keyed = []
        }

        for raw in text.split(separator: "\n", omittingEmptySubsequences: false).map(String.init) {
            if raw.trimmingCharacters(in: .whitespaces).isEmpty { continue }
            if raw.hasPrefix("@molecule ") || raw.hasPrefix("@residue ") {
                finish()
                current = header(raw)
                continue
            }
            guard current != nil else { continue }
            if raw.hasPrefix(" ") || raw.hasPrefix("\t") {
                if !keyed.isEmpty { keyed[keyed.count - 1].value += " " + raw.trimmingCharacters(in: .whitespaces) }
                continue
            }
            if let (k, v) = keyedLine(raw) {
                keyed.append((k, v))
                continue
            }
            if let a = atomLine(raw) { current?.atoms.append(a) }
        }
        finish()
        return out
    }

    /// "@molecule DA dAMP (deoxyadenosine monophosphate)   (C10 H14 N5 O6 P, M = 331.222 g/mol, CCD DA)".
    private static func header(_ line: String) -> BioMolecule {
        let isResidue = line.hasPrefix("@residue ")
        var rest = String(line.drop(while: { $0 != " " })).trimmingCharacters(in: .whitespaces)
        let code = String(rest.prefix(while: { $0 != " " }))
        rest = String(rest.dropFirst(code.count)).trimmingCharacters(in: .whitespaces)
        var formula: String?
        var mass: Double?
        // (the facts in the last parenthesis, after three spaces: "(C10 H14 N5 O6 P, M = 331.222 g/mol, CCD DA)")
        if let r = rest.range(of: "   (", options: .backwards), rest.hasSuffix(")") {
            let facts = String(rest[r.upperBound...].dropLast())
            if facts.contains("M = ") {
                let parts = facts.components(separatedBy: ", ")
                formula = parts.first
                if let m = parts.first(where: { $0.hasPrefix("M = ") }) {
                    mass = Double(m.dropFirst(4).replacingOccurrences(of: " g/mol", with: ""))
                }
                rest = String(rest[..<r.lowerBound]).trimmingCharacters(in: .whitespaces)
            }
        }
        return BioMolecule(kind: isResidue ? .residue : .molecule, code: code, title: rest, formula: formula, mass: mass)
    }

    /// A keyed line's key and value: "bonds: …", "alternatives (most common first): …".
    private static func keyedLine(_ line: String) -> (String, String)? {
        for k in keys where line.hasPrefix(k) {
            var rest = line.dropFirst(k.count)
            if rest.hasPrefix(" (") { rest = rest.drop(while: { $0 != ")" }).dropFirst() }
            guard rest.hasPrefix(":") else { continue }
            return (k, String(rest.dropFirst()).trimmingCharacters(in: .whitespaces))
        }
        return nil
    }

    /// "CA   C     0.000    0.000    0.000   # comment".
    private static func atomLine(_ line: String) -> BioAtomRecord? {
        var body = line
        var comment = ""
        if let h = line.range(of: "#") {
            comment = String(line[h.upperBound...]).trimmingCharacters(in: .whitespaces)
            body = String(line[..<h.lowerBound])
        }
        let f = body.split(whereSeparator: { $0 == " " || $0 == "\t" }).map(String.init)
        guard f.count >= 5, let x = Double(f[2]), let y = Double(f[3]), let z = Double(f[4]) else { return nil }
        var role = BioAtomRole.always
        if comment.contains("pH 7 form only") { role = .ph7Only }
        else if comment.contains("N-terminus only") { role = .nTerminus }
        else if comment.contains("C-terminus only") { role = .cTerminus }
        return BioAtomRecord(name: f[0], element: f[1], pos: BioVec(x, y, z), role: role, comment: comment)
    }

    /// A bond token split at the first -, = or # that is not its first character.
    static func bond(_ token: String) -> BioBondRecord? {
        let chars = Array(token)
        guard chars.count >= 3 else { return nil }
        for i in 1..<(chars.count - 1) {
            let c = chars[i]
            if c == "-" || c == "=" || c == "#" {
                return BioBondRecord(a: String(chars[..<i]), b: String(chars[(i + 1)...]), order: c == "-" ? 1 : (c == "=" ? 2 : 3))
            }
        }
        return nil
    }

    private static func numbers(_ s: String) -> [Double] {
        s.replacingOccurrences(of: "−", with: "-").split(whereSeparator: { " ,[]".contains($0) }).compactMap { Double($0) }
    }

    private static func apply(_ key: String, _ value: String, to m: inout BioMolecule) {
        switch key {
        case "bonds":
            m.bonds += value.split(separator: " ").compactMap { bond(String($0)) }
        case "aromatic":
            m.aromatic = value.split(separator: " ").map(String.init)
        case "rotamer":
            m.rotamer = value.split(separator: " ").first.map(String.init)
        case "chi1", "chi2", "chi3", "chi4":
            // "N-CA-CB-CG = -67"
            let sides = value.components(separatedBy: "=")
            guard sides.count == 2, let deg = Double(sides[1].trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "−", with: "-")) else { return }
            let atoms = sides[0].trimmingCharacters(in: .whitespaces).components(separatedBy: "-")
            guard atoms.count == 4, let i = Int(key.dropFirst(3)) else { return }
            m.chis.append(BioChi(index: i, atoms: atoms, degrees: deg))
            m.chis.sort { $0.index < $1.index }
        case "beta-strand rotamer":
            // "m-20 (chi2 at -75, in its range) chis [-70, -75]"
            let name = value.split(separator: " ").first.map(String.init) ?? ""
            if let r = value.range(of: "chis [") {
                m.betaRotamer = BioRotamer(name: name, chis: numbers(String(value[r.upperBound...])))
            }
        case "alternatives":
            m.alternatives = value.split(separator: ";").compactMap { entry in
                let f = entry.split(separator: " ").map(String.init)
                guard let name = f.first, f.count >= 2 else { return nil }
                return BioRotamer(name: name, chis: f.dropFirst().compactMap { Double($0.replacingOccurrences(of: "−", with: "-")) })
            }
        case "ring":
            m.ring = value
        case "pH 7":
            m.ph7 = ph7(value, atoms: m.atoms)
        case "note":
            m.notes.append(value)
        default:
            break
        }
    }

    /// "add H3 on N (N becomes +1); remove HXT (OXT becomes −1); remove HOP2 and HOP3 (phosphate −2)".
    private static func ph7(_ text: String, atoms: [BioAtomRecord]) -> BioPH7 {
        var p = BioPH7(text: text)
        p.add = atoms.filter { $0.role == .ph7Only }.map(\.name)
        let names = Set(atoms.map(\.name))
        func name(_ w: String) -> String? {
            let n = w.trimmingCharacters(in: CharacterSet(charactersIn: "("))
            return names.contains(n) ? n : nil
        }
        let words = text.replacingOccurrences(of: ";", with: " ; ").split(separator: " ").map(String.init)
        var i = 0
        while i < words.count {
            if words[i] == "remove" {
                var j = i + 1
                while j < words.count, !words[j].hasPrefix("("), words[j] != ";" {
                    if words[j] != "and" { p.remove.append(words[j]) }
                    j += 1
                }
                i = j
                continue
            }
            // ("OXT becomes −1", "NZ is +1", "NH2 carries the +1")
            if i + 2 < words.count, words[i + 1] == "becomes" || words[i + 1] == "is", let n = name(words[i]), let c = charge(words[i + 2]) {
                p.charges[n] = c
            }
            if i + 3 < words.count, words[i + 1] == "carries", words[i + 2] == "the", let n = name(words[i]), let c = charge(words[i + 3]) {
                p.charges[n] = c
            }
            i += 1
        }
        return p
    }

    private static func charge(_ w: String) -> Int? {
        let s = w.trimmingCharacters(in: CharacterSet(charactersIn: "();,")).replacingOccurrences(of: "−", with: "-")
        guard s.hasPrefix("+") || s.hasPrefix("-") else { return nil }
        return Int(s)
    }
}

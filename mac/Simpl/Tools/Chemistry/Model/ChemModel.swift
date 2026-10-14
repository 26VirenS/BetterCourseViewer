import Foundation

// (1.3.80) What the chemistry tools keep and read that needs no window — Foundation only, so the Models job checks it
// (scripts/dev/swift-checks/main.swift): which tool a launch's flags mean now that the Periodic Table's old pages are
// tools of their own; the Lewis maker's molecule as it was left; and the molecule builder's. Each is kept as lines of
// plain text in the app's settings (Chemistry/ChemTools.swift, ChemPrefs), read back whole or not at all.

/// The launch flags the screenshot suite has always used for the Periodic Table's pages, read as the tools they are now.
enum ChemLaunch {
    /// The tool `-SimplTool <tool>` opens: `ptable` with `-SimplPTPage lewis` is Lewis Structures (`lewis`), with
    /// `-SimplPTPage molecules` the Molecule Builder (`molecule`), and with `-SimplPTModel` (an element's 3D view, which
    /// was on its page) Atoms & Orbitals (`atoms`); any other tool is itself.
    static func tool(_ tool: String?, page: String?, model: String?) -> String? {
        guard let tool, !tool.isEmpty else { return nil }
        guard tool == "ptable" else { return tool }
        switch page {
        case "lewis": return "lewis"
        case "molecules": return "molecule"
        default: break
        }
        if let model, !model.isEmpty { return "atoms" }
        return tool
    }

    /// An element's number stepped by `by`, kept between 1 and `count` (the first and last stay where they are).
    static func step(_ number: Int, by: Int, count: Int) -> Int {
        guard count > 0 else { return number }
        return min(max(number + by, 1), count)
    }
}

/// (1.3.80) The Lewis maker's molecule as it was left, kept between openings: how it was made (the quick way or drawn),
/// the quick way's choices, the ranking, and the graph — each atom where it was drawn, each bond with its order. As
/// text, a line each:
///
///     way quick
///     mode formal
///     central O
///     count H 2
///     charge 0
///     preset H₂O
///     manual 0
///     atom 1 O 0 0
///     atom 2 H 90 0
///     bond 3 1 2 1
///
/// A line it does not know is passed over (a later version's); one it knows but cannot read, or a graph that does not
/// hold together (a bond to an atom not there, an id twice, an order outside 1–3), and the whole draft is not read.
struct ChemLewisDraft: Equatable {
    struct Atom: Equatable {
        var id: Int
        var symbol: String
        var x: Double
        var y: Double
    }

    struct Bond: Equatable {
        var id: Int
        var a: Int
        var b: Int
        var order: Int
    }

    /// "quick" or "draw".
    var way = "quick"
    /// The ranking: "formal" or "octet".
    var mode = "formal"
    var central = "O"
    /// The quick way's atoms round the centre, by symbol (0 to 6 of each).
    var counts: [String: Int] = [:]
    var charge = 0
    var preset: String? = nil
    /// The bonds' orders set by hand.
    var manual = false
    var atoms: [Atom] = []
    var bonds: [Bond] = []

    init(way: String = "quick", mode: String = "formal", central: String = "O", counts: [String: Int] = [:], charge: Int = 0,
         preset: String? = nil, manual: Bool = false, atoms: [Atom] = [], bonds: [Bond] = []) {
        self.way = way
        self.mode = mode
        self.central = central
        self.counts = counts
        self.charge = charge
        self.preset = preset
        self.manual = manual
        self.atoms = atoms
        self.bonds = bonds
    }

    /// The next id the graph gives: one past the largest it holds.
    var nextID: Int { (atoms.map(\.id) + bonds.map(\.id)).max().map { $0 + 1 } ?? 1 }

    var text: String {
        var lines = ["way \(way)", "mode \(mode)", "central \(central)"]
        for s in counts.keys.sorted() where (counts[s] ?? 0) > 0 { lines.append("count \(s) \(counts[s] ?? 0)") }
        lines.append("charge \(charge)")
        if let preset, !preset.isEmpty { lines.append("preset \(preset)") }
        lines.append("manual \(manual ? 1 : 0)")
        for a in atoms { lines.append("atom \(a.id) \(a.symbol) \(Self.number(a.x)) \(Self.number(a.y))") }
        for b in bonds { lines.append("bond \(b.id) \(b.a) \(b.b) \(b.order)") }
        return lines.joined(separator: "\n")
    }

    /// The draft in `text`; nil when there is no molecule in it or it does not hold together.
    init?(text: String) {
        var ids = Set<Int>()
        for raw in text.split(separator: "\n", omittingEmptySubsequences: true) {
            let line = String(raw).trimmingCharacters(in: .whitespaces)
            let f = line.split(separator: " ").map(String.init)
            guard let head = f.first else { continue }
            switch head {
            case "way":
                guard f.count == 2, ["quick", "draw"].contains(f[1]) else { return nil }
                way = f[1]
            case "mode":
                guard f.count == 2, ["formal", "octet"].contains(f[1]) else { return nil }
                mode = f[1]
            case "central":
                guard f.count == 2, Self.isSymbol(f[1]) else { return nil }
                central = f[1]
            case "count":
                guard f.count == 3, Self.isSymbol(f[1]), let n = Int(f[2]), (0...6).contains(n) else { return nil }
                counts[f[1]] = n
            case "charge":
                guard f.count == 2, let q = Int(f[1]), (-4...4).contains(q) else { return nil }
                charge = q
            case "preset":
                let name = String(line.dropFirst("preset".count)).trimmingCharacters(in: .whitespaces)
                preset = name.isEmpty ? nil : name
            case "manual":
                guard f.count == 2, f[1] == "0" || f[1] == "1" else { return nil }
                manual = f[1] == "1"
            case "atom":
                guard f.count == 5, let id = Int(f[1]), id > 0, Self.isSymbol(f[2]), let x = Double(f[3]), let y = Double(f[4]),
                      x.isFinite, y.isFinite, ids.insert(id).inserted else { return nil }
                atoms.append(Atom(id: id, symbol: f[2], x: x, y: y))
            case "bond":
                guard f.count == 5, let id = Int(f[1]), id > 0, let a = Int(f[2]), let b = Int(f[3]), let o = Int(f[4]),
                      a != b, (1...3).contains(o), ids.insert(id).inserted else { return nil }
                bonds.append(Bond(id: id, a: a, b: b, order: o))
            default:
                continue
            }
        }
        guard !atoms.isEmpty else { return nil }
        let atomIDs = Set(atoms.map(\.id))
        guard bonds.allSatisfy({ atomIDs.contains($0.a) && atomIDs.contains($0.b) }) else { return nil }
        // (two bonds between the same two atoms: not a graph the maker draws)
        let pairs = bonds.map { Set([$0.a, $0.b]) }
        guard Set(pairs).count == pairs.count else { return nil }
    }

    /// An element's symbol as the maker writes one: a capital and up to two small letters.
    static func isSymbol(_ s: String) -> Bool {
        guard let first = s.first, first.isUppercase, first.isASCII, s.count <= 3 else { return false }
        return s.dropFirst().allSatisfy { $0.isLowercase && $0.isASCII }
    }

    /// A position, short: whole numbers as they are, else to a tenth of a point (what the eye can see of a drawing).
    static func number(_ v: Double) -> String {
        let r = (v * 10).rounded() / 10
        return r == r.rounded() ? String(Int(r)) : String(r)
    }
}

/// (1.3.80) The molecule builder's molecule as it was left, kept between openings: its title, whether its shape is the
/// one it came with (a molecule handed over by the Biomolecule Builder, not laid out by VSEPR), each atom where it is
/// (in ångströms, from the middle) with the lone electrons the Lewis maker gave it, and its bonds by the atoms' places in
/// the list. As text, a line each:
///
///     title Water
///     fixed 0
///     atom O 0 0 0 4
///     atom H 0.758 -0.587 0
///     bond 0 1 1
///
/// An atom's last number, its lone electrons, is left out where the builder works them out from its bonds. Read whole
/// or not at all, as the Lewis draft is.
struct ChemMoleculeDraft: Equatable {
    struct Atom: Equatable {
        var symbol: String
        var x: Double
        var y: Double
        var z: Double
        var lone: Int? = nil
    }

    struct Bond: Equatable {
        var a: Int
        var b: Int
        var order: Int
    }

    var title = ""
    var fixed = false
    var atoms: [Atom] = []
    var bonds: [Bond] = []

    init(title: String = "", fixed: Bool = false, atoms: [Atom] = [], bonds: [Bond] = []) {
        self.title = title
        self.fixed = fixed
        self.atoms = atoms
        self.bonds = bonds
    }

    var text: String {
        var lines: [String] = []
        if !title.isEmpty { lines.append("title \(title)") }
        lines.append("fixed \(fixed ? 1 : 0)")
        for a in atoms {
            var line = "atom \(a.symbol) \(Self.number(a.x)) \(Self.number(a.y)) \(Self.number(a.z))"
            if let lone = a.lone { line += " \(lone)" }
            lines.append(line)
        }
        for b in bonds { lines.append("bond \(b.a) \(b.b) \(b.order)") }
        return lines.joined(separator: "\n")
    }

    /// The molecule in `text`; nil when it has no atoms or does not hold together.
    init?(text: String) {
        for raw in text.split(separator: "\n", omittingEmptySubsequences: true) {
            let line = String(raw).trimmingCharacters(in: .whitespaces)
            let f = line.split(separator: " ").map(String.init)
            guard let head = f.first else { continue }
            switch head {
            case "title":
                title = String(line.dropFirst("title".count)).trimmingCharacters(in: .whitespaces)
            case "fixed":
                guard f.count == 2, f[1] == "0" || f[1] == "1" else { return nil }
                fixed = f[1] == "1"
            case "atom":
                guard f.count == 5 || f.count == 6, ChemLewisDraft.isSymbol(f[1]),
                      let x = Double(f[2]), let y = Double(f[3]), let z = Double(f[4]), x.isFinite, y.isFinite, z.isFinite else { return nil }
                var lone: Int?
                if f.count == 6 {
                    guard let l = Int(f[5]), (0...8).contains(l) else { return nil }
                    lone = l
                }
                atoms.append(Atom(symbol: f[1], x: x, y: y, z: z, lone: lone))
            case "bond":
                guard f.count == 4, let a = Int(f[1]), let b = Int(f[2]), let o = Int(f[3]), a != b, a >= 0, b >= 0, (1...3).contains(o) else { return nil }
                bonds.append(Bond(a: a, b: b, order: o))
            default:
                continue
            }
        }
        guard !atoms.isEmpty, bonds.allSatisfy({ $0.a < atoms.count && $0.b < atoms.count }) else { return nil }
    }

    /// A coordinate to a thousandth of an ångström (what the builder's picture can show), trailing zeros dropped.
    static func number(_ v: Double) -> String {
        let r = (v * 1000).rounded() / 1000
        if r == r.rounded() { return String(Int(r)) }
        var s = String(format: "%.3f", r)
        while s.hasSuffix("0") { s.removeLast() }
        return s
    }
}

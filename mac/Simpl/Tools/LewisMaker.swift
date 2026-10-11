import AppKit
import SwiftUI

// (1.3.54) The Lewis structure builder, in the full Periodic Table. A molecule is a graph — atoms (an element and where it
// is drawn), bonds (two atoms and an order) and a total charge — made either quickly (a central atom, the atoms round it
// and a charge) or drawn freely (atoms placed, bonded by dragging, bonds' orders cycled by a click, atoms and bonds
// erased): chains, branches, rings and two-atom molecules alike, of any main-group element.
//
// For any graph its electrons are placed (LewisSolver): the total is the valence electrons less the charge; every
// assignment of bond orders the atoms allow is tried, outer atoms filled first and the rest on inner atoms (expanded to
// twelve from period 3 down), an odd electron left single; formal charges worked out; and the structures ranked — by the
// strict octet rule or by the smallest formal charges (textbooks differ: SO₄²⁻), negative charge on the more
// electronegative atoms. The best tie are its resonance structures (shown side by side, their average bond orders worked
// out), a close second minor contributors. Metals of groups 1 and 2 with nonmetals are ionic: cations and anions, each in
// brackets with its charge. Every atom's VSEPR shape; the polarity from the molecule in 3D (MoleculeModel's VSEPR model);
// warnings (a period 2 atom over eight, hydrogen with two bonds, too few electrons, unconnected atoms); and notes in place
// of a structure where a Lewis picture would mislead (transition-metal complexes, diborane) or is known to be wrong (O₂).

// MARK: - Elements

enum LChem {
    /// The main-group elements, as the palette lays them out: groups 1, 2 and 13–18, periods 1–6.
    static let palette: [[String?]] = [
        ["H", nil, nil, nil, nil, nil, nil, "He"],
        ["Li", "Be", "B", "C", "N", "O", "F", "Ne"],
        ["Na", "Mg", "Al", "Si", "P", "S", "Cl", "Ar"],
        ["K", "Ca", "Ga", "Ge", "As", "Se", "Br", "Kr"],
        ["Rb", "Sr", "In", "Sn", "Sb", "Te", "I", "Xe"],
        ["Cs", "Ba", "Tl", "Pb", "Bi", "Po", "At", "Rn"],
    ]

    static func element(_ s: String) -> PTElement? { PeriodicTable.elements.first { $0.symbol == s } }

    static func valence(_ s: String) -> Int {
        if s == "H" { return 1 }
        if s == "He" { return 2 }
        guard let g = element(s)?.group else { return 4 }
        return g <= 2 ? g : (g >= 13 ? g - 10 : 2)
    }

    static func en(_ s: String) -> Double {
        if let v = element(s)?.en { return v }
        return ["He": 4.5, "Ne": 4.4, "Ar": 3.6, "Kr": 3.0, "Xe": 2.6, "Rn": 2.2][s] ?? 2.0
    }

    static func period(_ s: String) -> Int { element(s)?.period ?? 2 }

    /// The most electrons it may have round it: 2 for H and He, 4 for Be, 6 for B and Al, 8 in period 2, 12 below.
    static func maxElectrons(_ s: String) -> Int {
        switch s {
        case "H", "He": return 2
        case "Be": return 4
        case "B", "Al": return 6
        default: return period(s) <= 2 ? 8 : 12
        }
    }

    /// The electrons it aims for: its duet or octet (or Be's four, B's and Al's six).
    static func target(_ s: String) -> Int { min(maxElectrons(s), 8) }

    /// A metal of group 1 or 2 (hydrogen and beryllium apart): ionic with nonmetals.
    static func ionicMetal(_ s: String) -> Bool {
        guard s != "H", s != "Be", let g = element(s)?.group else { return false }
        return g <= 2
    }

    static func transition(_ s: String) -> Bool { ["d", "f"].contains(element(s)?.block ?? "") }

    /// Its CPK colour, the primary colour for the pale ones (which do not read on a light page) and carbon.
    static func color(_ s: String) -> Color {
        if s == "H" || s == "C" { return .primary }
        guard let hex = element(s)?.extra?.cpk, let v = UInt32(hex, radix: 16) else { return .primary }
        let r = Double((v >> 16) & 0xff) / 255, g = Double((v >> 8) & 0xff) / 255, b = Double(v & 0xff) / 255
        if 0.2126 * r + 0.7152 * g + 0.0722 * b > 0.6 { return .primary }
        return Color(red: r, green: g, blue: b)
    }

    /// The VSEPR shape and bond angle by bonded atoms and lone pairs (an odd electron counted as a group).
    static func shape(bonded b: Int, lone l: Int) -> (name: String, angle: String) {
        switch (b, l) {
        case (0, _): return ("A lone atom", "—")
        case (1, _): return ("Linear (terminal)", "—")
        case (2, 0): return ("Linear", "180°")
        case (2, 1): return ("Bent", "about 117°")
        case (2, 2): return ("Bent", "about 104.5°")
        case (2, 3): return ("Linear", "180°")
        case (3, 0): return ("Trigonal planar", "120°")
        case (3, 1): return ("Trigonal pyramidal", "about 107°")
        case (3, 2): return ("T-shaped", "about 90°")
        case (4, 0): return ("Tetrahedral", "109.5°")
        case (4, 1): return ("Seesaw", "about 90° and 120°")
        case (4, 2): return ("Square planar", "90°")
        case (5, 0): return ("Trigonal bipyramidal", "90° and 120°")
        case (5, 1): return ("Square pyramidal", "about 90°")
        case (6, 0): return ("Octahedral", "90°")
        default: return ("\(b + l) electron groups", "—")
        }
    }

    static let sub: [Character: Character] = ["0": "₀", "1": "₁", "2": "₂", "3": "₃", "4": "₄", "5": "₅", "6": "₆", "7": "₇", "8": "₈", "9": "₉"]

    static func subscripted(_ n: Int) -> String { n > 1 ? String(String(n).map { sub[$0] ?? $0 }) : "" }

    /// Conventional order: carbon compounds by Hill (C, H, then by symbol); others by rising electronegativity, hydrogen
    /// last after an element of groups 13–15 (NH₃, PH₃, BH₃) and first otherwise (H₂O, HCl).
    static func formula(_ symbols: [String], charge: Int) -> String {
        var counts: [String: Int] = [:]
        for s in symbols { counts[s, default: 0] += 1 }
        var order: [String]
        if counts["C"] != nil {
            order = ["C", "H"].filter { counts[$0] != nil } + counts.keys.filter { $0 != "C" && $0 != "H" }.sorted()
        } else {
            order = counts.keys.sorted { en($0) == en($1) ? $0 < $1 : en($0) < en($1) }
            if counts["H"] != nil {
                let others = order.filter { $0 != "H" }
                let hLast = others.count == 1 && [13, 14, 15].contains(element(others[0])?.group ?? 0)
                order = hLast ? others + ["H"] : ["H"] + others
            }
        }
        let body = order.map { $0 + subscripted(counts[$0] ?? 0) }.joined()
        return body + chargeText(charge)
    }

    static func chargeText(_ q: Int) -> String {
        guard q != 0 else { return "" }
        return (abs(q) > 1 ? PTConfig.superscript(abs(q)) : "") + (q > 0 ? "⁺" : "⁻")
    }
}

// MARK: - The graph

struct LAtom: Identifiable, Equatable {
    let id: Int
    var symbol: String
    /// Where it is drawn, from the canvas's centre.
    var pos: CGPoint
}

struct LBond: Identifiable, Equatable {
    let id: Int
    var a: Int
    var b: Int
    /// The order drawn when the orders are the student's own (`LGraph.manual`).
    var order: Int = 1

    func other(_ x: Int) -> Int { x == a ? b : a }
}

struct LGraph: Equatable {
    var atoms: [LAtom] = []
    var bonds: [LBond] = []
    var charge = 0
    /// The bonds' orders set by hand (a bond's order cycled): its electrons placed round them, as they are.
    var manual = false
    var nextID = 1

    func atom(_ id: Int) -> LAtom? { atoms.first { $0.id == id } }
    func degree(_ id: Int) -> Int { bonds.filter { $0.a == id || $0.b == id }.count }
    func bond(_ x: Int, _ y: Int) -> LBond? { bonds.first { ($0.a == x && $0.b == y) || ($0.a == y && $0.b == x) } }

    mutating func add(_ symbol: String, at p: CGPoint) -> Int {
        atoms.append(LAtom(id: nextID, symbol: symbol, pos: p))
        nextID += 1
        return nextID - 1
    }

    mutating func connect(_ x: Int, _ y: Int, order: Int = 1) {
        guard x != y, bond(x, y) == nil else { return }
        bonds.append(LBond(id: nextID, a: x, b: y, order: order))
        nextID += 1
    }

    mutating func remove(atom id: Int) {
        atoms.removeAll { $0.id == id }
        bonds.removeAll { $0.a == id || $0.b == id }
    }

    /// Its pieces: each set of atoms joined by bonds.
    func components(skipping skip: Set<Int> = []) -> [[Int]] {
        var seen = Set<Int>()
        var out: [[Int]] = []
        for a in atoms where !seen.contains(a.id) && !skip.contains(a.id) {
            var part: [Int] = []
            var stack = [a.id]
            seen.insert(a.id)
            while let x = stack.popLast() {
                part.append(x)
                for b in bonds where b.a == x || b.b == x {
                    let y = b.other(x)
                    if !seen.contains(y) && !skip.contains(y) {
                        seen.insert(y)
                        stack.append(y)
                    }
                }
            }
            out.append(part)
        }
        return out
    }

    /// The smallest ring (three to eight atoms), in order round it, if there is one.
    func smallestRing() -> [Int]? {
        var best: [Int]?
        for b in bonds {
            // (the shortest way from one end of the bond to the other without it)
            var prev: [Int: Int] = [b.a: b.a]
            var queue = [b.a]
            while !queue.isEmpty {
                let x = queue.removeFirst()
                for e in bonds where (e.a == x || e.b == x) && e.id != b.id {
                    let y = e.other(x)
                    if prev[y] == nil {
                        prev[y] = x
                        queue.append(y)
                    }
                }
            }
            guard prev[b.b] != nil else { continue }
            var path = [b.b]
            while let last = path.last, last != b.a, let p = prev[last] { path.append(p) }
            if path.count >= 3 && path.count <= 8 && path.count < (best?.count ?? 99) { best = path }
        }
        return best
    }

    /// Laid out afresh: a ring (if any) as a regular polygon, branches pointing away from where they come from, then
    /// springs along the bonds and every atom pushing the others away.
    mutating func tidy() {
        guard !atoms.isEmpty else { return }
        let bondLength: CGFloat = 78
        let ring = smallestRing()
        var placed: [Int: CGPoint] = [:]
        var heading: [Int: Double] = [:]
        let byDegree = atoms.sorted { degree($0.id) > degree($1.id) }
        var offset: CGFloat = 0
        for start in byDegree where placed[start.id] == nil {
            var queue: [Int] = []
            let part = Set(components().first { $0.contains(start.id) } ?? [start.id])
            if let ring, part.contains(ring[0]) {
                let m = Double(ring.count)
                let radius = bondLength / CGFloat(2 * sin(Double.pi / m))
                for (k, id) in ring.enumerated() {
                    let t = -Double.pi / 2 + Double(k) * 2 * .pi / m
                    placed[id] = CGPoint(x: offset + radius * CGFloat(cos(t)), y: radius * CGFloat(sin(t)))
                    heading[id] = t
                    queue.append(id)
                }
            } else {
                placed[start.id] = CGPoint(x: offset, y: 0)
                queue.append(start.id)
            }
            while !queue.isEmpty {
                let x = queue.removeFirst()
                let next = bonds.filter { $0.a == x || $0.b == x }.map { $0.other(x) }.filter { placed[$0] == nil }
                let c = Double(next.count)
                for (k, y) in next.enumerated() {
                    var t: Double
                    if let h = heading[x] {
                        let step = 2 * Double.pi / (c + 1)
                        t = h + (Double(k) - (c - 1) / 2) * step
                    } else {
                        let first: Double = next.count % 2 == 0 ? 0 : -Double.pi / 2
                        t = first + Double(k) * 2 * .pi / c
                    }
                    let p = placed[x] ?? .zero
                    placed[y] = CGPoint(x: p.x + bondLength * CGFloat(cos(t)), y: p.y + bondLength * CGFloat(sin(t)))
                    heading[y] = t
                    queue.append(y)
                }
            }
            offset += 220
        }
        var p = atoms.map { placed[$0.id] ?? .zero }
        let index = Dictionary(uniqueKeysWithValues: atoms.enumerated().map { ($0.element.id, $0.offset) })
        for _ in 0..<400 {
            var f = Array(repeating: CGVector(dx: 0, dy: 0), count: p.count)
            for b in bonds {
                guard let i = index[b.a], let j = index[b.b] else { continue }
                let dx = p[j].x - p[i].x, dy = p[j].y - p[i].y
                let d = max(sqrt(dx * dx + dy * dy), 0.01)
                let k = (d - 78) * 0.1
                f[i].dx += dx / d * k
                f[i].dy += dy / d * k
                f[j].dx -= dx / d * k
                f[j].dy -= dy / d * k
            }
            for i in 0..<p.count {
                for j in (i + 1)..<p.count {
                    let dx = p[j].x - p[i].x, dy = p[j].y - p[i].y
                    let d2 = max(dx * dx + dy * dy, 25)
                    let k = 2600 / d2
                    let d = sqrt(d2)
                    f[i].dx -= dx / d * k
                    f[i].dy -= dy / d * k
                    f[j].dx += dx / d * k
                    f[j].dy += dy / d * k
                }
            }
            for i in 0..<p.count {
                p[i].x += max(-6, min(6, f[i].dx))
                p[i].y += max(-6, min(6, f[i].dy))
            }
        }
        let cx = p.map(\.x).reduce(0, +) / CGFloat(p.count), cy = p.map(\.y).reduce(0, +) / CGFloat(p.count)
        for i in atoms.indices { atoms[i].pos = CGPoint(x: p[i].x - cx, y: p[i].y - cy) }
    }
}

// MARK: - Placing the electrons

/// One structure: each bond's order, each atom's lone (non-bonding) electrons, and its formal charge.
struct LStructure: Equatable {
    var orders: [Int: Int] = [:]
    var lone: [Int: Int] = [:]
    var formal: [Int: Int] = [:]
    var score: [Double] = []

    func bondSum(_ g: LGraph, _ id: Int) -> Int { g.bonds.filter { $0.a == id || $0.b == id }.map { orders[$0.id] ?? 1 }.reduce(0, +) }
    func electrons(_ g: LGraph, _ id: Int) -> Int { 2 * bondSum(g, id) + (lone[id] ?? 0) }
}

/// An ion of an ionic compound, or the whole of a charged molecule: its atoms, drawn in brackets with its charge.
struct LIon: Equatable {
    let atoms: [Int]
    let charge: Int
}

struct LResult {
    var structures: [LStructure] = []
    /// How many of the first are equivalent (resonance structures of the same weight).
    var equivalent = 0
    var averageOrder: [Int: Double] = [:]
    var totalElectrons = 0
    var ions: [LIon] = []
    var ionic = false
    var warnings: [String] = []
    /// In place of a structure (or beside it): what a Lewis structure cannot show here.
    var notes: [String] = []
    var drawable = true

    var major: LStructure? { structures.first }
}

enum LewisSolver {
    enum Mode: String, CaseIterable, Identifiable {
        case octet, formal
        var id: String { rawValue }
        var label: String { self == .octet ? "Octet rule" : "Formal charge" }
    }

    static func solve(_ g: LGraph, mode: Mode) -> LResult {
        var r = LResult()
        guard !g.atoms.isEmpty else { return r }
        let symbols = g.atoms.map(\.symbol)
        r.totalElectrons = symbols.map(LChem.valence).reduce(0, +) - g.charge

        // (what a Lewis structure cannot show: said instead)
        if let tm = symbols.first(where: LChem.transition) {
            r.notes.append("\(LChem.element(tm)?.name ?? tm) is a transition metal: its complexes bond through d orbitals and coordinate bonds, which Lewis structures do not describe. Ligand-field or molecular-orbital pictures do.")
            r.drawable = false
            return r
        }
        let hydrogenBridges = g.atoms.filter { $0.symbol == "H" && g.degree($0.id) >= 2 }
        let isDiborane = symbols.filter { $0 == "B" }.count == 2 && symbols.filter { $0 == "H" }.count == 6 && symbols.count == 8
        if isDiborane || (!hydrogenBridges.isEmpty && symbols.contains("B")) {
            r.notes.append("Diborane (B₂H₆) is electron-deficient: its B–H–B bridges are three-centre two-electron bonds, which a Lewis structure, with two electrons to each bond between two atoms, cannot show.")
            r.drawable = false
            return r
        }
        for h in hydrogenBridges { r.warnings.append("Hydrogen can only make one bond (\(g.degree(h.id)) drawn).") }
        if symbols.sorted() == ["O", "O"] {
            r.notes.append("O₂'s Lewis structure pairs every electron, but oxygen is paramagnetic: it has two unpaired electrons, which molecular-orbital theory explains and a Lewis structure cannot.")
        }

        // (ionic: each group 1 or 2 metal a cation; the rest, without them, anions)
        let metals = g.atoms.filter { LChem.ionicMetal($0.symbol) }
        let nonmetals = g.atoms.filter { !LChem.ionicMetal($0.symbol) && LChem.en($0.symbol) >= 1.9 }
        var parts: [([Int], Int)] = []
        if !metals.isEmpty && !nonmetals.isEmpty {
            r.ionic = true
            let cationCharge = metals.map { LChem.valence($0.symbol) }.reduce(0, +)
            for m in metals { parts.append(([m.id], LChem.valence(m.symbol))) }
            let rest = g.components(skipping: Set(metals.map(\.id)))
            var left = g.charge - cationCharge
            var anions: [([Int], Int)] = []
            var poly: [[Int]] = []
            for c in rest {
                if c.count == 1, let a = g.atom(c[0]) {
                    let q = -(LChem.target(a.symbol) - LChem.valence(a.symbol))
                    anions.append((c, q))
                    left -= q
                } else {
                    poly.append(c)
                }
            }
            for (k, c) in poly.enumerated() { anions.append((c, k == 0 ? left : 0)) }
            if poly.isEmpty && left != 0 && !anions.isEmpty { anions[anions.count - 1].1 += left }
            parts += anions
        } else {
            let comps = g.components()
            if comps.count > 1 { r.warnings.append("Some atoms are not connected to the rest (\(comps.count) separate pieces).") }
            for (k, c) in comps.enumerated() { parts.append((c, k == 0 ? g.charge : 0)) }
        }

        // (each part solved on its own; the structures combined part by part)
        var combined: [LStructure] = [LStructure()]
        for part in parts {
            let atoms = part.0, q = part.1
            let sub = LGraph(atoms: g.atoms.filter { atoms.contains($0.id) }, bonds: g.bonds.filter { atoms.contains($0.a) && atoms.contains($0.b) },
                             charge: q, manual: g.manual, nextID: g.nextID)
            let found = solvePart(sub, mode: mode)
            if found.isEmpty {
                r.warnings.append(g.manual ? "These bond orders leave too few electrons, or more than an atom can hold." : "No arrangement of electrons fits: too few electrons for these bonds, or too many for the atoms to hold.")
                if !g.manual { r.drawable = false }
                continue
            }
            var next: [LStructure] = []
            for base in combined {
                for s in found.prefix(8) {
                    var m = base
                    m.orders.merge(s.orders) { $1 }
                    m.lone.merge(s.lone) { $1 }
                    m.formal.merge(s.formal) { $1 }
                    let left = base.score.isEmpty ? Array(repeating: 0.0, count: s.score.count) : base.score
                    m.score = zip(left, s.score).map { $0 + $1 }
                    next.append(m)
                }
            }
            combined = Array(next.sorted { $0.score.lexicographicallyPrecedes($1.score) }.prefix(8))
            if r.ionic || g.charge != 0 { r.ions.append(LIon(atoms: atoms, charge: q)) }
        }
        r.structures = combined.filter { !$0.score.isEmpty }
        if let best = r.structures.first?.score {
            r.equivalent = r.structures.prefix(while: { $0.score == best }).count
        }

        // (average bond orders over the equivalent structures)
        let eq = r.structures.prefix(max(r.equivalent, 1))
        for b in g.bonds {
            let v = eq.map { Double($0.orders[b.id] ?? 1) }
            if !v.isEmpty { r.averageOrder[b.id] = v.reduce(0, +) / Double(v.count) }
        }

        // (checks)
        if let s = r.major {
            for a in g.atoms {
                let e = s.electrons(g, a.id)
                if LChem.period(a.symbol) == 2 && e > 8 { r.warnings.append("\(a.symbol) has \(e) electrons round it: period 2 atoms hold at most eight.") }
                if e > LChem.maxElectrons(a.symbol) && LChem.period(a.symbol) > 2 { r.warnings.append("\(a.symbol) has \(e) electrons round it, more than it can hold.") }
            }
            if g.manual {
                var lonePlaced = 0
                for a in g.atoms { lonePlaced += s.lone[a.id] ?? 0 }
                var bondPlaced = 0
                for b in g.bonds { bondPlaced += s.orders[b.id] ?? 1 }
                let placed: Int = lonePlaced + 2 * bondPlaced
                if placed != r.totalElectrons { r.warnings.append("Electrons drawn: \(placed); the molecule has \(r.totalElectrons).") }
            }
        }
        return r
    }

    /// A part's structures, best first: the equivalent best ones, then close minor contributors.
    private static func solvePart(_ g: LGraph, mode: Mode) -> [LStructure] {
        let total = g.atoms.map { LChem.valence($0.symbol) }.reduce(0, +) - g.charge
        guard total >= 0 else { return [] }
        let edges = g.bonds
        var maxBonds: [Int: Int] = [:]
        for a in g.atoms { maxBonds[a.id] = LChem.maxElectrons(a.symbol) / 2 }
        var results: [LStructure] = []
        var orders = Array(repeating: 1, count: edges.count)
        var sums: [Int: Int] = [:]
        for e in edges {
            sums[e.a, default: 0] += 1
            sums[e.b, default: 0] += 1
        }
        var tried = 0
        func consider() {
            tried += 1
            var byID: [Int: Int] = [:]
            for (k, e) in edges.enumerated() { byID[e.id] = orders[k] }
            if let s = place(g, orders: byID, total: total, mode: mode) { results.append(s) }
        }
        // (every order each bond may take, its atoms' bond counts kept within what they can make)
        func walk(_ k: Int, _ used: Int) {
            if tried > 60000 { return }
            if k == edges.count {
                consider()
                return
            }
            let e = edges[k]
            let roomA: Int = (maxBonds[e.a] ?? 4) - (sums[e.a] ?? 0) + 1
            let roomB: Int = (maxBonds[e.b] ?? 4) - (sums[e.b] ?? 0) + 1
            let cap = min(3, roomA, roomB)
            for o in 1...max(1, cap) {
                if 2 * (used + o - 1) > total - 2 * edges.count { break }
                orders[k] = o
                sums[e.a, default: 0] += o - 1
                sums[e.b, default: 0] += o - 1
                walk(k + 1, used + o - 1)
                sums[e.a, default: 0] -= o - 1
                sums[e.b, default: 0] -= o - 1
            }
            orders[k] = 1
        }
        if g.manual {
            orders = edges.map { $0.order }
            consider()
        } else {
            walk(0, 0)
        }
        results.sort { $0.score.lexicographicallyPrecedes($1.score) }
        var unique: [LStructure] = []
        for s in results where !unique.contains(where: { $0.orders == s.orders && $0.lone == s.lone }) { unique.append(s) }
        guard let best = unique.first?.score else { return [] }
        let equivalent = unique.prefix(while: { $0.score == best }).count
        // (close minor contributors: the same octets, formal charges at most two more)
        let minor = unique.dropFirst(equivalent).filter { $0.score.count > 1 && $0.score[0] == best[0] && $0.score[1] <= best[1] + 2.01 }
        return Array((Array(unique.prefix(equivalent)) + Array(minor)).prefix(8))
    }

    /// The electrons placed round given bond orders: outer atoms first to their octets (the more electronegative first),
    /// then inner atoms, then inner atoms of period 3 and below past eight, then any atom with room; an odd electron on
    /// the least electronegative atom with room. Nil if they do not fit.
    private static func place(_ g: LGraph, orders: [Int: Int], total: Int, mode: Mode) -> LStructure? {
        var s = LStructure(orders: orders)
        let bonded = 2 * orders.values.reduce(0, +)
        var left = total - bonded
        guard left >= 0 else { return nil }
        let odd = left % 2 == 1
        if odd { left -= 1 }
        var pairs = left / 2
        for a in g.atoms { s.lone[a.id] = 0 }
        let outer = g.atoms.filter { g.degree($0.id) <= 1 }.sorted { LChem.en($0.symbol) > LChem.en($1.symbol) }
        let inner = g.atoms.filter { g.degree($0.id) >= 2 }.sorted { LChem.en($0.symbol) > LChem.en($1.symbol) }
        func give(_ list: [LAtom], upTo limit: (String) -> Int) {
            for a in list where pairs > 0 {
                let room = max(0, (limit(a.symbol) - s.electrons(g, a.id)) / 2)
                let k = min(room, pairs)
                s.lone[a.id, default: 0] += 2 * k
                pairs -= k
            }
        }
        give(outer, upTo: LChem.target)
        give(inner, upTo: LChem.target)
        give(inner, upTo: LChem.maxElectrons)
        give(outer + inner, upTo: LChem.maxElectrons)
        guard pairs == 0 else { return nil }
        if odd {
            let order = inner.sorted { LChem.en($0.symbol) < LChem.en($1.symbol) } + outer.sorted { LChem.en($0.symbol) < LChem.en($1.symbol) }
            guard let a = order.first(where: { s.electrons(g, $0.id) + 1 <= LChem.maxElectrons($0.symbol) }) else { return nil }
            s.lone[a.id, default: 0] += 1
        }
        for a in g.atoms where s.electrons(g, a.id) > LChem.maxElectrons(a.symbol) { return nil }
        for a in g.atoms { s.formal[a.id] = LChem.valence(a.symbol) - (s.lone[a.id] ?? 0) - s.bondSum(g, a.id) }

        // (ranked: octets kept (and, by the strict rule, none past eight); formal charges small; negative charge on the
        // more electronegative atoms; no like charges side by side)
        var octet = 0.0
        for a in g.atoms {
            let e = s.electrons(g, a.id)
            if e < LChem.target(a.symbol) { octet += 1 }
            if e > 8 && mode == .octet { octet += 1 }
        }
        let fc = s.formal
        let absFC = Double(fc.values.map { abs($0) }.reduce(0, +))
        var placement = 0.0
        for a in g.atoms {
            let q = Double(fc[a.id] ?? 0)
            let en = LChem.en(a.symbol)
            if q < 0 { placement += -q * (4.0 - en) } else if q > 0 { placement += q * en }
        }
        var like = 0.0
        for b in g.bonds {
            let x = fc[b.a] ?? 0, y = fc[b.b] ?? 0
            if x != 0 && y != 0 && (x > 0) == (y > 0) { like += 1 }
        }
        let p = (placement * 100).rounded() / 100
        // (octets first in both — formal charges are minimised among structures that keep them; the modes differ in
        // whether more than eight round an atom counts against it)
        s.score = [octet, absFC, like, p]
        return s
    }
}

// MARK: - Shapes and polarity

enum LGeometry {
    /// An atom's bonded atoms and lone pairs (a single electron counted as one).
    static func domains(_ g: LGraph, _ s: LStructure, _ id: Int) -> (bonded: Int, lone: Int) {
        let l = s.lone[id] ?? 0
        return (g.degree(id), l / 2 + l % 2)
    }

    /// The molecule built in 3D (MoleculeModel's VSEPR forces), for its polarity.
    static func dipole(_ g: LGraph, _ s: LStructure) -> Double {
        let m = MoleculeModel(empty: true)
        m.load(graph: g, structure: s)
        return m.dipole
    }
}

// MARK: - The builder

struct LewisMaker: View {
    var openIn3D: (LGraph, LStructure) -> Void

    enum Way: String, CaseIterable, Identifiable {
        case quick, draw
        var id: String { rawValue }
        var label: String { self == .quick ? "Quick" : "Draw" }
    }

    enum Tool: String, CaseIterable, Identifiable {
        case draw, move, erase
        var id: String { rawValue }
        var label: String { self == .draw ? "Draw" : (self == .move ? "Move" : "Erase") }
        var symbol: String { self == .draw ? "pencil" : (self == .move ? "hand.draw" : "eraser") }
    }

    @State private var way: Way = .quick
    @State private var tool: Tool = .draw
    @State private var element = "C"
    @State private var graph = LGraph()
    @State private var selected: Int?
    @State private var shown = 0
    @State private var mode: LewisSolver.Mode = .formal
    // (the quick way's own choices)
    @State private var central = "O"
    @State private var counts: [String: Int] = ["H": 2]
    @State private var charge = 0
    @State private var preset: String? = "H₂O"

    private static let outers = ["H", "O", "N", "C", "F", "Cl", "Br", "I", "S"]

    var body: some View {
        let r = LewisSolver.solve(graph, mode: mode)
        PTWorkLayout {
            controls
        } centre: {
            drawing(r)
        } right: {
            facts(r)
        }
        .onAppear {
            if graph.atoms.isEmpty { buildQuick() }
            // (the screenshot suite: -SimplLewisPreset "NO₃⁻")
            if let p = UserDefaults.standard.string(forKey: "SimplLewisPreset") { load(p) }
        }
        .onChange(of: graph) { _, _ in shown = 0 }
    }

    // MARK: Controls

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            Picker("", selection: $way) {
                ForEach(Way.allCases) { w in Text(w.label).tag(w) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            heading("Preset")
            PresetMenu(sections: LewisPresets.sections, selected: preset) { name in load(name) }
            if way == .quick { quickControls } else { drawControls }
            heading("Charge")
            HStack {
                Text("Total charge").font(.sCallout)
                Spacer()
                stepper(value: graph.charge, min: -4, max: 4) { v in
                    graph.charge = v
                    charge = v
                    preset = nil
                }
            }
            heading("Rank by")
            Picker("", selection: $mode) {
                ForEach(LewisSolver.Mode.allCases) { m in Text(m.label).tag(m) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            Text(mode == .octet ? "Every atom kept to its octet (none past eight)." : "The smallest formal charges, period 3 atoms and below holding up to twelve.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var quickControls: some View {
        VStack(alignment: .leading, spacing: 14) {
            heading("Central atom")
            LElementPicker(selected: central) { s in
                central = s
                buildQuick()
            }
            heading("Surrounding atoms")
            VStack(spacing: 0) {
                ForEach(Self.outers, id: \.self) { s in
                    HStack {
                        Text(s).font(.sCallout.weight(.bold)).foregroundStyle(LChem.color(s))
                        Spacer()
                        stepper(value: counts[s] ?? 0, min: 0, max: 6) { v in
                            counts[s] = v
                            buildQuick()
                        }
                    }
                    .padding(.vertical, 4)
                    .padding(.horizontal, 12)
                }
            }
            .card(radius: 14)
        }
    }

    private var drawControls: some View {
        VStack(alignment: .leading, spacing: 14) {
            heading("Element")
            LElementPicker(selected: element) { s in
                element = s
                // (an atom selected takes the element picked)
                if let id = selected, let i = graph.atoms.firstIndex(where: { $0.id == id }) {
                    graph.atoms[i].symbol = s
                    preset = nil
                }
            }
            Picker("", selection: $tool) {
                ForEach(Tool.allCases) { t in Label(t.label, systemImage: t.symbol).tag(t) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            Text(tool == .draw ? "Click to place an atom (bonded to the one selected). Drag from an atom to another to bond them, or to empty space for a new bonded atom. Click a bond to make it double, triple, then single again."
                 : (tool == .move ? "Drag atoms to move them." : "Click an atom or a bond to remove it."))
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 8) {
                Button("Tidy") { withAnimation(Motion.gentle) { graph.tidy() } }
                if graph.manual {
                    Button("Auto bonds") { graph.manual = false }
                        .help("Let the builder choose the bond orders again")
                }
                Spacer()
                Button("Clear") {
                    graph = LGraph(charge: graph.charge)
                    selected = nil
                    preset = nil
                }
            }
            .controlSize(.small)
        }
    }

    private func stepper(value: Int, min lo: Int, max hi: Int, set: @escaping (Int) -> Void) -> some View {
        HStack(spacing: 8) {
            Button { set(Swift.max(lo, value - 1)) } label: { Image(systemName: "minus").frame(width: 22, height: 22) }
                .buttonStyle(.plain)
                .background(Color.primary.opacity(0.08), in: RoundedRectangle(cornerRadius: 6))
                .disabled(value <= lo)
            Text(value > 0 && lo < 0 ? "+\(value)" : "\(value)").font(.sCallout.monospacedDigit().weight(.semibold)).frame(width: 26)
            Button { set(Swift.min(hi, value + 1)) } label: { Image(systemName: "plus").frame(width: 22, height: 22) }
                .buttonStyle(.plain)
                .background(Color.primary.opacity(0.08), in: RoundedRectangle(cornerRadius: 6))
                .disabled(value >= hi)
        }
    }

    private func heading(_ t: String) -> some View {
        Text(t.uppercased()).font(.system(size: 11.5, weight: .semibold)).tracking(0.6).foregroundStyle(.secondary)
    }

    // MARK: Building

    private func buildQuick() {
        var g = LGraph(charge: charge)
        let c = g.add(central, at: .zero)
        let around = Self.outers.flatMap { s in Array(repeating: s, count: counts[s] ?? 0) }
        var outer: [Int] = []
        for (k, s) in around.enumerated() {
            let a0 = -Double.pi / 2 + Double(k) / Double(max(around.count, 1)) * 2 * .pi
            let t = g.add(s, at: CGPoint(x: 90 * cos(a0), y: 90 * sin(a0)))
            g.connect(c, t)
            outer.append(t)
        }
        // (1.3.57) the atoms round it set out as its shape is drawn on paper — water bent, ammonia a fan under its lone
        // pair — once the electrons show how many lone pairs the centre has
        let lone = LewisSolver.solve(g, mode: mode).major?.lone[c] ?? 0
        let angles = Self.quickAngles(bonded: outer.count, lonePairs: (lone + 1) / 2)
        for (k, id) in outer.enumerated() {
            let a = angles[k] * .pi / 180
            if let i = g.atoms.firstIndex(where: { $0.id == id }) { g.atoms[i].pos = CGPoint(x: 90 * cos(a), y: 90 * sin(a)) }
        }
        graph = g
        selected = c
        preset = LewisPresets.all.first { p in
            guard let q = p.quick else { return false }
            return q.0 == central && p.charge == charge && q.1.sorted() == around.sorted()
        }?.name
    }

    /// Directions (degrees, y down) for the atoms round a centre, by how many there are and its lone pairs.
    static func quickAngles(bonded n: Int, lonePairs l: Int) -> [Double] {
        switch (n, l) {
        case (1, _): return [0]
        case (2, 0): return [180, 0]
        case (2, _): return [142, 38]
        case (3, 0): return [-90, 30, 150]
        case (3, 1): return [150, 90, 30]
        case (3, _): return [-90, 90, 180]
        case (4, 1): return [-90, 90, 150, 210]
        case (4, _): return [-90, 0, 90, 180]
        case (6, _): return [-90, 90, 30, 150, 210, 330]
        default:
            return (0..<n).map { -90 + Double($0) * 360 / Double(max(n, 1)) }
        }
    }

    private func load(_ name: String) {
        guard let p = LewisPresets.all.first(where: { $0.name == name }) else { return }
        withAnimation(Motion.snappy) {
            preset = name
            if let q = p.quick {
                way = .quick
                central = q.0
                var c: [String: Int] = [:]
                for s in q.1 { c[s, default: 0] += 1 }
                counts = c
                charge = p.charge
                buildQuick()
                preset = name
            } else {
                way = .draw
                var g = p.build()
                g.charge = p.charge
                g.tidy()
                graph = g
                selected = nil
                charge = p.charge
            }
        }
    }

    // MARK: The drawing

    private func drawing(_ r: LResult) -> some View {
        let s = r.structures.indices.contains(shown) ? r.structures[shown] : r.major
        return VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline) {
                Text(LChem.formula(graph.atoms.map(\.symbol), charge: graph.charge)).font(.system(size: 22, weight: .bold))
                if r.ionic { Text("ionic").font(.sCallout).foregroundStyle(.secondary) }
                Spacer()
                if r.structures.count > 1 {
                    Text(shown == 0 ? "Major contributor" : (shown < r.equivalent ? "Equivalent structure" : "Minor contributor"))
                        .font(.sCaption.weight(.semibold))
                        .foregroundStyle(shown < max(r.equivalent, 1) ? Color.green : Color.orange)
                }
            }
            .padding(16)
            ZStack {
                if r.drawable {
                    LewisEditorCanvas(graph: $graph, structure: s, result: r, tool: tool, element: element, selected: $selected, editable: way == .draw) {
                        preset = nil
                        if way == .quick { way = .draw }
                    }
                } else {
                    VStack(spacing: 12) {
                        Image(systemName: "exclamationmark.bubble").font(.system(size: 34)).foregroundStyle(.secondary)
                        ForEach(r.notes, id: \.self) { n in
                            Text(n).font(.sBody).multilineTextAlignment(.center).frame(maxWidth: 460)
                        }
                    }
                    .padding(30)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            if r.drawable && r.structures.count > 1 { resonance(r) }
            HStack(spacing: 16) {
                HStack(spacing: 5) {
                    Circle().fill(Color.yellow).frame(width: 7, height: 7)
                    Text("Lone electrons")
                }
                HStack(spacing: 5) {
                    Circle().fill(Color.orange).frame(width: 7, height: 7)
                    Text("Unpaired")
                }
                Text("Small numbers: formal charges")
            }
            .font(.sCaption)
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity)
            .padding(12)
        }
        .card(radius: 20)
    }

    /// The resonance structures side by side, ↔ between them; a press shows one large.
    private func resonance(_ r: LResult) -> some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                ForEach(r.structures.indices, id: \.self) { k in
                    if k > 0 { Text("↔").font(.system(size: 18, weight: .semibold)).foregroundStyle(.secondary) }
                    Button { withAnimation(Motion.snappy) { shown = k } } label: {
                        VStack(spacing: 2) {
                            LewisStructureCanvas(graph: graph, structure: r.structures[k], result: r, scale: 0.42, selected: nil, preview: nil)
                                .frame(width: 130, height: 92)
                            Text(k == 0 ? "major" : (k < r.equivalent ? "equivalent" : "minor"))
                                .font(.sCaption2)
                                .foregroundStyle(k < max(r.equivalent, 1) ? Color.green : Color.orange)
                        }
                        .padding(4)
                        .background(k == shown ? Theme.accent.opacity(0.14) : Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(.horizontal, 14)
        }
        .frame(height: 118)
    }

    // MARK: Facts

    private func facts(_ r: LResult) -> some View {
        let s = r.structures.indices.contains(shown) ? r.structures[shown] : r.major
        let bondingPairs = s.map { st in graph.bonds.map { st.orders[$0.id] ?? 1 }.reduce(0, +) } ?? 0
        let loneElectrons = s.map { st in graph.atoms.map { st.lone[$0.id] ?? 0 }.reduce(0, +) } ?? 0
        let radicals = s.map { st in graph.atoms.filter { (st.lone[$0.id] ?? 0) % 2 == 1 }.count } ?? 0
        let polar = polarity(r, s)
        return VStack(alignment: .leading, spacing: 14) {
            VStack(spacing: 0) {
                row("Valence electrons", "\(r.totalElectrons)")
                row("Bonding pairs", "\(bondingPairs)")
                row("Lone pairs", "\(loneElectrons / 2)", .yellow)
                if radicals > 0 { row("Unpaired electrons", "\(radicals)", .orange) }
                row("Resonance structures", resonanceText(r))
                row("Polarity", polar, polar == "Polar" ? .orange : (polar == "Nonpolar" ? .green : .primary), last: true)
            }
            .card(radius: 14)
            ForEach(r.notes, id: \.self) { n in
                Label(n, systemImage: "info.circle").font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            ForEach(r.warnings, id: \.self) { w in
                Label(w, systemImage: "exclamationmark.triangle.fill").font(.sCaption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
            }
            if let s, r.drawable {
                selectedAtom(s)
                atomTable(s)
                averages(r)
            }
            Button {
                if let s { openIn3D(graph, s) }
            } label: {
                Label("Open in 3D builder", systemImage: "rotate.3d").frame(maxWidth: .infinity)
            }
            .glassButton(prominent: true)
            .controlSize(.large)
            .disabled(s == nil || !r.drawable || graph.atoms.count < 2)
        }
    }

    private func polarity(_ r: LResult, _ s: LStructure?) -> String {
        guard let s, r.drawable, graph.atoms.count > 1 else { return "—" }
        if r.ionic { return "Ionic" }
        return LGeometry.dipole(graph, s) > 0.3 ? "Polar" : "Nonpolar"
    }

    private func resonanceText(_ r: LResult) -> String {
        guard r.structures.count > 1 else { return "1" }
        let eq = max(r.equivalent, 1)
        return "\(eq) equivalent" + (r.structures.count > eq ? " + \(r.structures.count - eq) minor" : "")
    }

    @ViewBuilder
    private func selectedAtom(_ s: LStructure) -> some View {
        let id = selected ?? graph.atoms.max(by: { graph.degree($0.id) < graph.degree($1.id) })?.id
        if let id, let a = graph.atom(id) {
            let d = LGeometry.domains(graph, s, id)
            let shape = LChem.shape(bonded: d.bonded, lone: d.lone)
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text(a.symbol).font(.sHeadline).foregroundStyle(LChem.color(a.symbol))
                    Text(LChem.element(a.symbol)?.name ?? "").font(.sCallout).foregroundStyle(.secondary)
                    Spacer()
                    Text(selected == nil ? "most bonded atom" : "selected").font(.sCaption).foregroundStyle(.secondary)
                }
                Text("Electron groups: \(d.bonded + d.lone) (\(d.bonded) bonded, \(d.lone) lone)").font(.sCallout)
                Text("Shape: \(shape.name)").font(.sCallout.weight(.semibold)).foregroundStyle(Theme.accent)
                Text("Bond angle: \(shape.angle)").font(.sCallout)
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card(radius: 14)
        }
    }

    private func atomTable(_ s: LStructure) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            heading("Every atom")
            ForEach(graph.atoms) { a in
                atomRow(a, s)
            }
        }
        .padding(14)
        .card(radius: 14)
    }

    private func atomRow(_ a: LAtom, _ s: LStructure) -> some View {
        let e = s.electrons(graph, a.id)
        let fc = s.formal[a.id] ?? 0
        let lone = s.lone[a.id] ?? 0
        let word = status(e, LChem.target(a.symbol), a.symbol)
        return HStack(spacing: 8) {
            Text(a.symbol).font(.sCallout.weight(.bold)).foregroundStyle(LChem.color(a.symbol)).frame(width: 26, alignment: .leading)
            Text("\(e) e⁻\(fc != 0 ? " · FC \(fc > 0 ? "+" : "")\(fc)" : "")\(lone % 2 == 1 ? " · radical" : "")").font(.sCaption)
            Spacer()
            Text(word.0).font(.sCaption.weight(.semibold)).foregroundStyle(word.1)
        }
        .padding(.vertical, 2)
        .background(selected == a.id ? Theme.accent.opacity(0.1) : Color.clear, in: RoundedRectangle(cornerRadius: 6))
        .contentShape(Rectangle())
        .onTapGesture { selected = a.id }
    }

    private func status(_ e: Int, _ want: Int, _ sym: String) -> (String, Color) {
        if e == want { return (want == 2 ? "Duet" : (want == 8 ? "Octet" : "Full"), .green) }
        if e > want { return (LChem.period(sym) > 2 ? "Expanded" : "Over", LChem.period(sym) > 2 ? Theme.accent : Color.red) }
        return ("Short", .orange)
    }

    @ViewBuilder
    private func averages(_ r: LResult) -> some View {
        let eq = r.structures.prefix(max(r.equivalent, 1))
        let varying = graph.bonds.filter { b in Set(eq.map { $0.orders[b.id] ?? 1 }).count > 1 }
        if !varying.isEmpty {
            VStack(alignment: .leading, spacing: 4) {
                heading("Average bond orders")
                ForEach(varying) { b in
                    Text("\(graph.atom(b.a)?.symbol ?? "")–\(graph.atom(b.b)?.symbol ?? "")  \(String(format: "%.2f", r.averageOrder[b.id] ?? 1))")
                        .font(.sCaption.monospacedDigit())
                }
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card(radius: 14)
        }
    }

    private func row(_ k: String, _ v: String, _ color: Color = .primary, last: Bool = false) -> some View {
        VStack(spacing: 0) {
            HStack {
                Text(k).font(.sCallout).foregroundStyle(.secondary)
                Spacer()
                Text(v).font(.sCallout.weight(.semibold)).foregroundStyle(color).multilineTextAlignment(.trailing)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 9)
            if !last { Divider().padding(.leading, 14) }
        }
    }
}

// MARK: - Presets

enum LewisPresets {
    struct Preset {
        let name: String
        var charge = 0
        /// A central atom and the atoms round it (the quick way).
        var quick: (String, [String])? = nil
        /// Atoms in order, each with the index of an earlier one it bonds to (-1 for none); then extra bonds (ring
        /// closures: two indices and an order).
        var atoms: [(String, Int)] = []
        var extra: [(Int, Int, Int)] = []

        func build() -> LGraph {
            var g = LGraph()
            var ids: [Int] = []
            for (k, a) in atoms.enumerated() {
                let angle = Double(k) * 1.1
                let id = g.add(a.0, at: CGPoint(x: 60 * cos(angle), y: 60 * sin(angle)))
                ids.append(id)
                if a.1 >= 0 && a.1 < ids.count - 1 { g.connect(ids[a.1], id) }
            }
            for e in extra where e.0 < ids.count && e.1 < ids.count { g.connect(ids[e.0], ids[e.1], order: e.2) }
            return g
        }
    }

    /// The presets in groups, for the dropdown.
    static let sections: [(String, [String])] = {
        let special = ["B₂H₆", "Fe(CO)₅"], ionic = ["NaCl", "MgCl₂", "NaNO₃"]
        var simple: [String] = [], two: [String] = [], bigger: [String] = [], ions: [String] = [], radicals: [String] = []
        var ionicOut: [String] = [], specialOut: [String] = []
        for p in all {
            if special.contains(p.name) { specialOut.append(p.name) }
            else if ionic.contains(p.name) { ionicOut.append(p.name) }
            else if p.name.contains("radical") { radicals.append(p.name) }
            else if p.charge != 0 { ions.append(p.name) }
            else if p.quick != nil { simple.append(p.name) }
            else if p.atoms.count <= 2 { two.append(p.name) }
            else { bigger.append(p.name) }
        }
        let groups: [(String, [String])] = [("Simple molecules", simple), ("Two atoms", two), ("Bigger molecules", bigger), ("Ions", ions),
                                         ("Radicals", radicals), ("Ionic compounds", ionicOut), ("Special cases", specialOut)]
        return groups.filter { !$0.1.isEmpty }
    }()

    static let all: [Preset] = [
        Preset(name: "H₂O", quick: ("O", ["H", "H"])),
        Preset(name: "CO₂", quick: ("C", ["O", "O"])),
        Preset(name: "NH₃", quick: ("N", ["H", "H", "H"])),
        Preset(name: "CH₄", quick: ("C", ["H", "H", "H", "H"])),
        Preset(name: "HCN", quick: ("C", ["H", "N"])),
        Preset(name: "CH₂O", quick: ("C", ["H", "H", "O"])),
        Preset(name: "O₃", quick: ("O", ["O", "O"])),
        Preset(name: "SO₂", quick: ("S", ["O", "O"])),
        Preset(name: "NO₃⁻", charge: -1, quick: ("N", ["O", "O", "O"])),
        Preset(name: "CO₃²⁻", charge: -2, quick: ("C", ["O", "O", "O"])),
        Preset(name: "SO₄²⁻", charge: -2, quick: ("S", ["O", "O", "O", "O"])),
        Preset(name: "NH₄⁺", charge: 1, quick: ("N", ["H", "H", "H", "H"])),
        Preset(name: "BF₃", quick: ("B", ["F", "F", "F"])),
        Preset(name: "PCl₅", quick: ("P", ["Cl", "Cl", "Cl", "Cl", "Cl"])),
        Preset(name: "SF₆", quick: ("S", ["F", "F", "F", "F", "F", "F"])),
        Preset(name: "XeF₄", quick: ("Xe", ["F", "F", "F", "F"])),
        Preset(name: "ClO₂ (radical)", quick: ("Cl", ["O", "O"])),
        Preset(name: "H₂", atoms: [("H", -1), ("H", 0)]),
        Preset(name: "N₂", atoms: [("N", -1), ("N", 0)]),
        Preset(name: "O₂", atoms: [("O", -1), ("O", 0)]),
        Preset(name: "CO", atoms: [("C", -1), ("O", 0)]),
        Preset(name: "HCl", atoms: [("H", -1), ("Cl", 0)]),
        Preset(name: "NO (radical)", atoms: [("N", -1), ("O", 0)]),
        Preset(name: "NO₂ (radical)", atoms: [("O", -1), ("N", 0), ("O", 1)]),
        Preset(name: "C₂H₆", atoms: [("C", -1), ("C", 0), ("H", 0), ("H", 0), ("H", 0), ("H", 1), ("H", 1), ("H", 1)]),
        Preset(name: "CH₃OH", atoms: [("C", -1), ("O", 0), ("H", 0), ("H", 0), ("H", 0), ("H", 1)]),
        Preset(name: "H₂O₂", atoms: [("O", -1), ("O", 0), ("H", 0), ("H", 1)]),
        Preset(name: "Ethanol (C₂H₆O)", atoms: [("C", -1), ("C", 0), ("O", 1), ("H", 0), ("H", 0), ("H", 0), ("H", 1), ("H", 1), ("H", 2)]),
        Preset(name: "Dimethyl ether (C₂H₆O)", atoms: [("C", -1), ("O", 0), ("C", 1), ("H", 0), ("H", 0), ("H", 0), ("H", 2), ("H", 2), ("H", 2)]),
        Preset(name: "Benzene", atoms: [("C", -1), ("C", 0), ("C", 1), ("C", 2), ("C", 3), ("C", 4), ("H", 0), ("H", 1), ("H", 2), ("H", 3), ("H", 4), ("H", 5)],
               extra: [(5, 0, 1)]),
        Preset(name: "N₂O", atoms: [("N", -1), ("N", 0), ("O", 1)]),
        Preset(name: "OCN⁻", charge: -1, atoms: [("O", -1), ("C", 0), ("N", 1)]),
        Preset(name: "NaCl", atoms: [("Na", -1), ("Cl", -1)]),
        Preset(name: "MgCl₂", atoms: [("Mg", -1), ("Cl", -1), ("Cl", -1)]),
        Preset(name: "NaNO₃", atoms: [("Na", -1), ("N", -1), ("O", 1), ("O", 1), ("O", 1)]),
        Preset(name: "B₂H₆", atoms: [("B", -1), ("H", 0), ("B", 1), ("H", 2), ("H", 2), ("H", 0), ("H", 0), ("H", 0)], extra: [(7, 2, 1)]),
        Preset(name: "Fe(CO)₅", atoms: [("Fe", -1), ("C", 0), ("O", 1), ("C", 0), ("O", 3), ("C", 0), ("O", 5), ("C", 0), ("O", 7), ("C", 0), ("O", 9)]),
    ]
}

// MARK: - Drawing a structure

/// A structure drawn: its bonds (one to three lines), its atoms, their lone electrons as dots (in pairs; an odd one
/// alone, orange) where the bonds leave room, their formal charges small beside them, and brackets with charges round
/// ions.
struct LewisStructureCanvas: View {
    let graph: LGraph
    let structure: LStructure?
    let result: LResult
    /// The largest scale it is drawn at (smaller when it would not fit).
    var scale: CGFloat = 1
    var selected: Int?
    /// A bond being drawn: from an atom to the pointer.
    var preview: (CGPoint, CGPoint)?
    /// A fixed view (scale and middle) instead of one fitted to the drawing: the editor's, held still while it is used.
    var view: Fit?

    struct Fit: Equatable {
        var scale: CGFloat
        var centre: CGPoint
    }

    /// The scale and middle that fit a drawing in a size: drawn round its own origin at full scale when it fits there
    /// (so drawing atoms one by one doesn't shift it), otherwise centred on its atoms and shrunk to fit.
    static func fit(_ g: LGraph, _ size: CGSize, maxScale: CGFloat) -> Fit {
        guard !g.atoms.isEmpty, size.width > 1, size.height > 1 else { return Fit(scale: maxScale, centre: .zero) }
        // (room round the atoms for their lone electrons, brackets and charges)
        let pad: CGFloat = 52
        var x0 = CGFloat.infinity, x1 = -CGFloat.infinity, y0 = CGFloat.infinity, y1 = -CGFloat.infinity
        for a in g.atoms {
            x0 = min(x0, a.pos.x - pad)
            x1 = max(x1, a.pos.x + pad)
            y0 = min(y0, a.pos.y - pad)
            y1 = max(y1, a.pos.y + pad)
        }
        x1 += 14
        let halfW: CGFloat = size.width / 2 / maxScale, halfH: CGFloat = size.height / 2 / maxScale
        if x0 >= -halfW && x1 <= halfW && y0 >= -halfH && y1 <= halfH { return Fit(scale: maxScale, centre: .zero) }
        let sx: CGFloat = size.width / (x1 - x0), sy: CGFloat = size.height / (y1 - y0)
        return Fit(scale: min(maxScale, sx, sy), centre: CGPoint(x: (x0 + x1) / 2, y: (y0 + y1) / 2))
    }

    /// (1.3.57) The editor's view: as large as fits, up to half again full size, so a small molecule isn't lost in a
    /// big space; drawn round the origin while that fits, so drawing atoms one by one doesn't shift it.
    static func editorFit(_ g: LGraph, _ size: CGSize) -> Fit {
        let big = fit(g, size, maxScale: 1.5)
        if big.centre == .zero { return big }
        return fit(g, size, maxScale: 1)
    }

    var body: some View {
        Canvas { ctx, size in
            let f = view ?? LewisStructureCanvas.fit(graph, size, maxScale: scale)
            LewisStructureCanvas.draw(&ctx, size: size, graph: graph, structure: structure, result: result, scale: f.scale,
                                      centre: f.centre, selected: selected, preview: preview)
        }
    }

    static func point(_ p: CGPoint, _ size: CGSize, _ scale: CGFloat, _ centre: CGPoint) -> CGPoint {
        let x: CGFloat = size.width / 2 + (p.x - centre.x) * scale
        let y: CGFloat = size.height / 2 + (p.y - centre.y) * scale
        return CGPoint(x: x, y: y)
    }

    static func draw(_ ctx: inout GraphicsContext, size: CGSize, graph g: LGraph, structure s: LStructure?, result r: LResult, scale: CGFloat,
                     centre: CGPoint, selected: Int?, preview: (CGPoint, CGPoint)?) {
        let atomR: CGFloat = 16 * scale
        let line = max(scale, 0.6)
        func at(_ id: Int) -> CGPoint? { g.atom(id).map { point($0.pos, size, scale, centre) } }
        if let pv = preview {
            var path = Path()
            path.move(to: point(pv.0, size, scale, centre))
            path.addLine(to: point(pv.1, size, scale, centre))
            ctx.stroke(path, with: .color(Theme.accent.opacity(0.7)), style: StrokeStyle(lineWidth: 2, lineCap: .round, dash: [5, 4]))
        }
        // (bonds)
        for b in g.bonds {
            guard let p = at(b.a), let q = at(b.b) else { continue }
            let dx = q.x - p.x, dy = q.y - p.y
            let len = max(sqrt(dx * dx + dy * dy), 0.01)
            let ux = dx / len, uy = dy / len
            let order = s?.orders[b.id] ?? b.order
            let gap: CGFloat = 4.5 * line
            let offsets: [CGFloat] = order <= 1 ? [0] : (order == 2 ? [-gap, gap] : [-gap * 1.6, 0, gap * 1.6])
            for o in offsets {
                var path = Path()
                let ax: CGFloat = ux * atomR - uy * o, ay: CGFloat = uy * atomR + ux * o
                let bx: CGFloat = -ux * atomR - uy * o, by: CGFloat = -uy * atomR + ux * o
                path.move(to: CGPoint(x: p.x + ax, y: p.y + ay))
                path.addLine(to: CGPoint(x: q.x + bx, y: q.y + by))
                ctx.stroke(path, with: .color(.primary.opacity(0.75)), style: StrokeStyle(lineWidth: 2.4 * line, lineCap: .round))
            }
        }
        // (atoms, their formal charges and their lone electrons)
        for a in g.atoms {
            let p = point(a.pos, size, scale, centre)
            if selected == a.id {
                ctx.fill(Path(ellipseIn: CGRect(x: p.x - 22, y: p.y - 22, width: 44, height: 44)), with: .color(Theme.accent.opacity(0.18)))
            }
            ctx.draw(Text(a.symbol).font(.system(size: 30 * scale, weight: .bold)).foregroundColor(LChem.color(a.symbol)), at: p)
            guard let s else { continue }
            let alone = r.ions.contains { $0.atoms == [a.id] && $0.charge != 0 }
            if let fc = s.formal[a.id], fc != 0, !alone {
                let t = (fc > 0 ? "+" : "−") + (abs(fc) > 1 ? "\(abs(fc))" : "")
                ctx.draw(Text(t).font(.system(size: 13 * max(scale, 0.7), weight: .bold)).foregroundColor(.secondary),
                         at: CGPoint(x: p.x + 20 * scale, y: p.y - 19 * scale))
            }
            let lone = s.lone[a.id] ?? 0
            guard lone > 0 else { continue }
            var bondAngles: [Double] = []
            for b in g.bonds where b.a == a.id || b.b == a.id {
                if let o = g.atom(b.other(a.id)) { bondAngles.append(atan2(Double(o.pos.y - a.pos.y), Double(o.pos.x - a.pos.x))) }
            }
            let angles = freeAngles(lone / 2 + lone % 2, from: bondAngles)
            for (k, ang) in angles.enumerated() {
                let single = k == angles.count - 1 && lone % 2 == 1
                let d: CGFloat = 25 * scale
                let m = CGPoint(x: p.x + d * CGFloat(cos(ang)), y: p.y + d * CGFloat(sin(ang)))
                let dot: CGFloat = 3 * line
                let offsets: [CGFloat] = single ? [0] : [-5 * line, 5 * line]
                for o in offsets {
                    let x = m.x - CGFloat(sin(ang)) * o, y = m.y + CGFloat(cos(ang)) * o
                    ctx.fill(Path(ellipseIn: CGRect(x: x - dot, y: y - dot, width: 2 * dot, height: 2 * dot)), with: .color(single ? .orange : .yellow))
                }
            }
        }
        // (brackets round ions and charged molecules, the charge at the top right)
        for ion in r.ions where ion.charge != 0 {
            let pts = ion.atoms.compactMap { at($0) }
            guard let minX = pts.map(\.x).min(), let maxX = pts.map(\.x).max(), let minY = pts.map(\.y).min(), let maxY = pts.map(\.y).max() else { continue }
            let pad: CGFloat = 36 * scale
            let w: CGFloat = maxX - minX + 2 * pad, h: CGFloat = maxY - minY + 2 * pad
            let rect = CGRect(x: minX - pad, y: minY - pad, width: w, height: h)
            if ion.atoms.count > 1 || ion.charge < 0 {
                var path = Path()
                path.move(to: CGPoint(x: rect.minX + 7, y: rect.minY))
                path.addLine(to: CGPoint(x: rect.minX, y: rect.minY))
                path.addLine(to: CGPoint(x: rect.minX, y: rect.maxY))
                path.addLine(to: CGPoint(x: rect.minX + 7, y: rect.maxY))
                path.move(to: CGPoint(x: rect.maxX - 7, y: rect.minY))
                path.addLine(to: CGPoint(x: rect.maxX, y: rect.minY))
                path.addLine(to: CGPoint(x: rect.maxX, y: rect.maxY))
                path.addLine(to: CGPoint(x: rect.maxX - 7, y: rect.maxY))
                ctx.stroke(path, with: .color(.secondary), lineWidth: 1.6)
            }
            // (plain digits and signs: superscript ones draw too small to read)
            let q = ion.charge
            let sign: String = q > 0 ? "+" : "−"
            let qt: String = (abs(q) > 1 ? "\(abs(q))" : "") + sign
            ctx.draw(Text(qt).font(.system(size: 19 * max(scale, 0.7), weight: .bold)).foregroundColor(.primary),
                     at: CGPoint(x: rect.maxX + 10, y: rect.minY + 4))
        }
    }

    /// Directions for lone electrons round an atom: as far from its bonds, and from each other, as they can be.
    static func freeAngles(_ count: Int, from bonds: [Double]) -> [Double] {
        var taken = bonds
        var out: [Double] = []
        let candidates = (0..<24).map { Double($0) * .pi / 12 - .pi / 2 }
        for _ in 0..<count {
            var bestAngle = candidates[0]
            var bestGap = -1.0
            for c in candidates {
                let g = taken.map { t in abs(atan2(sin(c - t), cos(c - t))) }.min() ?? .pi
                if g > bestGap {
                    bestGap = g
                    bestAngle = c
                }
            }
            out.append(bestAngle)
            taken.append(bestAngle)
        }
        return out
    }
}

/// The drawing, editable: in Draw, a click places an atom (bonded to the one selected) or selects one, a drag from an
/// atom bonds it to another (or to a new atom where it is let go), a click on a bond cycles its order; in Move, atoms
/// are dragged; in Erase, a click removes an atom or a bond. A change to the quick way's drawing makes it a drawing.
struct LewisEditorCanvas: View {
    @Binding var graph: LGraph
    let structure: LStructure?
    let result: LResult
    let tool: LewisMaker.Tool
    let element: String
    @Binding var selected: Int?
    let editable: Bool
    var edited: () -> Void
    @State private var dragFrom: Int?
    @State private var dragAt: CGPoint?
    @State private var moving: Int?
    /// The view held still while the pointer is down, so what is under it doesn't move.
    @State private var held: LewisStructureCanvas.Fit?

    private var preview: (CGPoint, CGPoint)? {
        guard let id = dragFrom, let a = graph.atom(id), let p = dragAt else { return nil }
        return (a.pos, p)
    }

    var body: some View {
        GeometryReader { geo in
            let size = geo.size
            LewisStructureCanvas(graph: graph, structure: structure, result: result, scale: 1, selected: selected, preview: preview,
                                 view: held ?? LewisStructureCanvas.editorFit(graph, size))
                .contentShape(Rectangle())
                .gesture(
                    DragGesture(minimumDistance: 0)
                        .onChanged { v in changed(v, size) }
                        .onEnded { v in ended(v, size) }
                )
        }
    }

    /// From the view's points to the drawing's.
    private func local(_ p: CGPoint, _ size: CGSize) -> CGPoint {
        let f = held ?? LewisStructureCanvas.editorFit(graph, size)
        let x: CGFloat = f.centre.x + (p.x - size.width / 2) / f.scale
        let y: CGFloat = f.centre.y + (p.y - size.height / 2) / f.scale
        return CGPoint(x: x, y: y)
    }

    private func atomAt(_ p: CGPoint) -> Int? {
        var best: (Int, CGFloat)?
        for a in graph.atoms {
            let d = hypot(a.pos.x - p.x, a.pos.y - p.y)
            if d < 22 && (best == nil || d < best!.1) { best = (a.id, d) }
        }
        return best?.0
    }

    private func bondAt(_ p: CGPoint) -> Int? {
        for b in graph.bonds {
            guard let x = graph.atom(b.a)?.pos, let y = graph.atom(b.b)?.pos else { continue }
            let dx = y.x - x.x, dy = y.y - x.y
            let l2 = max(dx * dx + dy * dy, 0.01)
            let dot: CGFloat = (p.x - x.x) * dx + (p.y - x.y) * dy
            let t: CGFloat = max(0, min(1, dot / l2))
            let c = CGPoint(x: x.x + t * dx, y: x.y + t * dy)
            if hypot(c.x - p.x, c.y - p.y) < 8 && t > 0.15 && t < 0.85 { return b.id }
        }
        return nil
    }

    private func changed(_ v: DragGesture.Value, _ size: CGSize) {
        if held == nil { held = LewisStructureCanvas.editorFit(graph, size) }
        guard hypot(v.translation.width, v.translation.height) > 4 else { return }
        let start = local(v.startLocation, size), now = local(v.location, size)
        if tool == .move || !editable {
            if moving == nil { moving = atomAt(start) }
            if let id = moving, let i = graph.atoms.firstIndex(where: { $0.id == id }) { graph.atoms[i].pos = now }
        } else if tool == .draw {
            if dragFrom == nil { dragFrom = atomAt(start) }
            if dragFrom != nil { dragAt = now }
        }
    }

    private func ended(_ v: DragGesture.Value, _ size: CGSize) {
        let start = local(v.startLocation, size), end = local(v.location, size)
        let click = hypot(v.translation.width, v.translation.height) <= 4
        let from = dragFrom, wasMoving = moving != nil
        dragFrom = nil
        dragAt = nil
        moving = nil
        held = nil
        if wasMoving { return }
        if click {
            if let id = atomAt(start) {
                if tool == .erase && editable {
                    graph.remove(atom: id)
                    if selected == id { selected = nil }
                    edited()
                } else {
                    selected = selected == id ? nil : id
                }
                return
            }
            if let b = bondAt(start), let i = graph.bonds.firstIndex(where: { $0.id == b }) {
                if tool == .erase && editable {
                    graph.bonds.remove(at: i)
                    edited()
                    return
                }
                // (a bond's order cycled: the orders the student's own from now on, starting from those shown)
                if !graph.manual {
                    for k in graph.bonds.indices { graph.bonds[k].order = structure?.orders[graph.bonds[k].id] ?? graph.bonds[k].order }
                    graph.manual = true
                }
                graph.bonds[i].order = graph.bonds[i].order % 3 + 1
                edited()
                return
            }
            if tool == .draw {
                let anchor = selected
                let id = graph.add(element, at: start)
                if let anchor, graph.atom(anchor) != nil { graph.connect(anchor, id) }
                selected = id
                edited()
            } else {
                selected = nil
            }
            return
        }
        guard tool == .draw, let from else { return }
        if let to = atomAt(end) {
            if to != from {
                graph.connect(from, to)
                edited()
            }
        } else {
            let id = graph.add(element, at: end)
            graph.connect(from, id)
            selected = id
            edited()
        }
    }
}

/// (1.3.55) A dropdown of presets in named groups, showing the one loaded (or a placeholder for one's own).
struct PresetMenu: View {
    let sections: [(String, [String])]
    let selected: String?
    var placeholder = "Your own"
    let pick: (String) -> Void

    var body: some View {
        Menu {
            ForEach(sections.indices, id: \.self) { k in
                Section(sections[k].0) {
                    ForEach(sections[k].1, id: \.self) { name in
                        Button { pick(name) } label: { item(name) }
                    }
                }
            }
        } label: {
            HStack(spacing: 8) {
                Text(selected ?? placeholder).font(.sBody.weight(.semibold)).lineLimit(1)
                Spacer(minLength: 4)
                Image(systemName: "chevron.up.chevron.down").font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary)
            }
            .padding(.horizontal, 12)
            .frame(maxWidth: .infinity, minHeight: 34)
            .contentShape(Rectangle())
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .background(Color.primary.opacity(0.07), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }

    @ViewBuilder
    private func item(_ name: String) -> some View {
        if name == selected { Label(name, systemImage: "checkmark") } else { Text(name) }
    }
}

/// (1.3.55) An element shown as a card (symbol, name, valence electrons, how many it can hold) with a menu of the
/// main-group elements, group by group, to change it.
struct LElementPicker: View {
    let selected: String
    let pick: (String) -> Void

    /// The palette's columns as groups: 1, 2, then 13 to 18.
    static let groups: [(String, [String])] = {
        let names = ["Group 1", "Group 2", "Group 13", "Group 14", "Group 15", "Group 16", "Group 17", "Group 18"]
        var out: [(String, [String])] = []
        for (col, name) in names.enumerated() {
            var members: [String] = []
            for row in LChem.palette where col < row.count {
                if let s = row[col] { members.append(s) }
            }
            out.append((name, members))
        }
        return out
    }()

    private func name(_ s: String) -> String { LChem.element(s)?.name ?? s }

    var body: some View {
        let colour: Color = LChem.color(selected)
        let detail: String = "\(LChem.valence(selected)) valence e⁻ · max \(LChem.maxElectrons(selected))"
        Menu {
            ForEach(Self.groups.indices, id: \.self) { k in
                Section(Self.groups[k].0) {
                    ForEach(Self.groups[k].1, id: \.self) { s in
                        Button { pick(s) } label: { item(s) }
                    }
                }
            }
        } label: {
            HStack(spacing: 12) {
                Text(selected)
                    .font(.system(size: 20, weight: .bold))
                    .foregroundStyle(colour)
                    .frame(width: 44, height: 44)
                    .background(colour.opacity(0.16), in: RoundedRectangle(cornerRadius: 11, style: .continuous))
                VStack(alignment: .leading, spacing: 2) {
                    Text(name(selected)).font(.sBody.weight(.semibold)).lineLimit(1)
                    Text(detail).font(.sCaption).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.8)
                }
                Spacer(minLength: 4)
                Image(systemName: "chevron.up.chevron.down").font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary)
            }
            .padding(10)
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .card(radius: 14)
        .help("Change the element")
    }

    @ViewBuilder
    private func item(_ s: String) -> some View {
        let text = "\(s)  \(name(s))"
        if s == selected { Label(text, systemImage: "checkmark") } else { Text(text) }
    }
}

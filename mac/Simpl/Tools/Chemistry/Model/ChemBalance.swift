import Foundation

// (1.3.89) The Equation Balancer's arithmetic — Foundation only, checked by the Models job: an equation read into its
// species (either side of the arrow, split at the pluses that are not charges, coefficients and phase labels passed
// over), the matrix of each element's and the charge's count in each species, its null space found exactly with
// fractions (Gaussian elimination over Int64 numerators and denominators, every step checked for overflow), and the
// one solution scaled to the smallest whole numbers. Molar Mass & Stoichiometry's Reaction tab balances through here
// too, and uses the same species and coefficients for its yields.

/// An exact fraction, always in lowest terms with a positive denominator; every operation says when Int64 would
/// overflow rather than giving a wrong answer.
struct ChemFraction: Equatable {
    let num: Int64
    let den: Int64

    static let zero = ChemFraction(0, 1)
    static let one = ChemFraction(1, 1)

    /// The fraction `n / d`, brought to lowest terms.
    init(_ n: Int64, _ d: Int64) {
        precondition(d != 0, "a fraction's denominator is never 0")
        let g = Self.gcd(abs(n), abs(d))
        let sign: Int64 = d < 0 ? -1 : 1
        num = g == 0 ? 0 : sign * n / g
        den = g == 0 ? 1 : sign * d / g
    }

    private init(normalised n: Int64, _ d: Int64) {
        num = n
        den = d
    }

    var isZero: Bool { num == 0 }

    static func gcd(_ a: Int64, _ b: Int64) -> Int64 {
        var (x, y) = (a, b)
        while y != 0 { (x, y) = (y, x % y) }
        return x
    }

    struct Overflow: Error {}

    static func + (a: ChemFraction, b: ChemFraction) throws -> ChemFraction {
        let (p, o1) = a.num.multipliedReportingOverflow(by: b.den)
        let (q, o2) = b.num.multipliedReportingOverflow(by: a.den)
        let (n, o3) = p.addingReportingOverflow(q)
        let (d, o4) = a.den.multipliedReportingOverflow(by: b.den)
        guard !(o1 || o2 || o3 || o4) else { throw Overflow() }
        return ChemFraction(n, d)
    }

    static func - (a: ChemFraction, b: ChemFraction) throws -> ChemFraction {
        try a + ChemFraction(normalised: -b.num, b.den)
    }

    static func * (a: ChemFraction, b: ChemFraction) throws -> ChemFraction {
        let (n, o1) = a.num.multipliedReportingOverflow(by: b.num)
        let (d, o2) = a.den.multipliedReportingOverflow(by: b.den)
        guard !(o1 || o2) else { throw Overflow() }
        return ChemFraction(n, d)
    }

    static func / (a: ChemFraction, b: ChemFraction) throws -> ChemFraction {
        guard !b.isZero else { throw Overflow() }
        let (n, o1) = a.num.multipliedReportingOverflow(by: b.den)
        let (d, o2) = a.den.multipliedReportingOverflow(by: b.num)
        guard !(o1 || o2) else { throw Overflow() }
        return ChemFraction(n, d)
    }
}

/// One species of an equation: its text as typed (without its coefficient and phase label), its formula, which side
/// it is on, and the coefficient typed before it, if any.
struct ChemSpecies: Equatable {
    let text: String
    let formula: ChemFormula
    let reactant: Bool
    let typedCoefficient: Int?
}

/// Why an equation could not be balanced, with the plain line the tool shows.
enum ChemBalanceError: Error, Equatable {
    /// Nothing typed yet.
    case empty
    /// No arrow, or more than one.
    case noArrow
    /// A side with nothing on it.
    case emptySide
    /// A token that is not a formula.
    case unreadable(String)
    /// Elements that appear on one side only.
    case oneSide([String])
    /// No whole-number solution with every coefficient positive (`hint`: the species that would need 0 or less).
    case impossible(String?)
    /// A null space of more than one dimension: two equations in one.
    case manyWays
    /// The exact arithmetic outgrew Int64.
    case tooBig

    var message: String {
        switch self {
        case .empty: return "Type an equation, like Fe + O2 → Fe2O3."
        case .noArrow: return "Put one arrow (→, -> or =) between the reactants and the products."
        case .emptySide: return "Both sides of the arrow need something on them."
        case .unreadable(let token): return "Can't read `\(token)` — is it a real formula?"
        case .oneSide(let names):
            let list = names.map { "`\($0)`" }.joined(separator: ", ")
            return "This can't be balanced — check the formulas (an element on only one side? \(list))"
        case .impossible(let hint):
            if let hint { return "This can't be balanced as written — `\(hint)` would need a coefficient of 0 or less. Check the formulas." }
            return "This can't be balanced as written — check the formulas."
        case .manyWays: return "More than one way to balance this — split it into separate equations."
        case .tooBig: return "The numbers got too big to balance exactly — try a simpler equation."
        }
    }
}

/// A row of the atom count table: an element (or the charge), its total on the left and on the right.
struct ChemCountRow: Equatable {
    let label: String
    let left: Int
    let right: Int

    var equal: Bool { left == right }
}

/// A balanced equation: each species with its coefficient, the atom count table, the mass on each side, and whether
/// the coefficients found differ from the ones typed.
struct ChemBalanced: Equatable {
    let species: [ChemSpecies]
    let coefficients: [Int]
    /// The coefficients typed were not these (or none were typed): "balanced for you".
    let changed: Bool

    var reactants: [(species: ChemSpecies, coefficient: Int)] { zip(species, coefficients).filter { $0.0.reactant }.map { ($0, $1) } }
    var products: [(species: ChemSpecies, coefficient: Int)] { zip(species, coefficients).filter { !$0.0.reactant }.map { ($0, $1) } }

    /// The equation as plain text, "2H2 + O2 -> 2H2O" (a coefficient of 1 left out).
    func text(arrow: String = "->") -> String {
        func side(_ r: Bool) -> String {
            zip(species, coefficients).filter { $0.0.reactant == r }.map { ($1 == 1 ? "" : String($1)) + $0.text }.joined(separator: " + ")
        }
        return "\(side(true)) \(arrow) \(side(false))"
    }

    /// Each element's atoms on each side, in order of first appearance, and the charge last when any species has one.
    var counts: [ChemCountRow] {
        var order: [String] = []
        for s in species { for e in s.formula.elements where !order.contains(e) { order.append(e) } }
        var rows = order.map { e -> ChemCountRow in
            var l = 0, r = 0
            for (s, c) in zip(species, coefficients) {
                let n = (s.formula.counts[e] ?? 0) * c
                if s.reactant { l += n } else { r += n }
            }
            return ChemCountRow(label: e, left: l, right: r)
        }
        if species.contains(where: { $0.formula.charge != 0 }) {
            var l = 0, r = 0
            for (s, c) in zip(species, coefficients) {
                if s.reactant { l += s.formula.charge * c } else { r += s.formula.charge * c }
            }
            rows.append(ChemCountRow(label: "Charge", left: l, right: r))
        }
        return rows
    }

    /// The total mass of each side, in g for the coefficients as moles (equal once balanced: the mass check).
    var massLeft: Double { zip(species, coefficients).filter { $0.0.reactant }.reduce(0) { $0 + Double($1.1) * $1.0.formula.molarMass } }
    var massRight: Double { zip(species, coefficients).filter { !$0.0.reactant }.reduce(0) { $0 + Double($1.1) * $1.0.formula.molarMass } }
}

enum ChemBalancer {
    /// The equation in `raw`, read: its species on each side of the arrow.
    static func read(_ raw: String) -> Result<[ChemSpecies], ChemBalanceError> {
        var s = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !s.isEmpty else { return .failure(.empty) }
        for arrow in ["⟶", "→", "⇌", "⇒", "-->", "==>", "=>", "<->", "<=>"] { s = s.replacingOccurrences(of: arrow, with: "->") }
        if !s.contains("->") { s = s.replacingOccurrences(of: "=", with: "->") }
        let sides = s.components(separatedBy: "->")
        guard sides.count == 2 else { return .failure(.noArrow) }
        var out: [ChemSpecies] = []
        for (k, side) in sides.enumerated() {
            let tokens = split(side)
            guard !tokens.isEmpty else { return .failure(.emptySide) }
            for token in tokens {
                var t = ChemFormula.stripPhase(token)
                var typed: Int?
                var digits = ""
                while let c = t.first, c.isNumber, c.isASCII { digits.append(c); t.removeFirst() }
                if !digits.isEmpty {
                    guard let n = Int(digits), n > 0 else { return .failure(.unreadable(token)) }
                    typed = n
                }
                t = t.trimmingCharacters(in: .whitespaces)
                switch ChemFormula.parse(t) {
                case .success(let f): out.append(ChemSpecies(text: f.text, formula: f, reactant: k == 0, typedCoefficient: typed))
                case .failure: return .failure(.unreadable(token.trimmingCharacters(in: .whitespaces)))
                }
            }
        }
        return .success(out)
    }

    /// A side's species: split at each `+` that is a separator — one with a space before it, or one glued between
    /// two formulas — and not at a charge's `+`, which follows its formula closely and is followed by a space, the
    /// end, or a phase label ("Cu + Ag+ -> Cu2+ + Ag").
    static func split(_ side: String) -> [String] {
        let chars = Array(side)
        var parts: [String] = []
        var cur = ""
        for (i, ch) in chars.enumerated() {
            guard ch == "+" else { cur.append(ch); continue }
            let prevSpace = i == 0 || chars[i - 1].isWhitespace
            let atEnd = i + 1 >= chars.count
            let nextSpace = !atEnd && chars[i + 1].isWhitespace
            let nextPhase = !atEnd && chars[i + 1] == "("
            let afterCaret = cur.lastIndex(where: { !$0.isNumber }).map { cur[$0] == "^" } ?? false
            let isCharge = !prevSpace && (atEnd || nextSpace || nextPhase || afterCaret)
            if isCharge { cur.append(ch) } else { parts.append(cur); cur = "" }
        }
        parts.append(cur)
        return parts.map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }

    /// The equation balanced: the smallest whole coefficients that make every element's atoms, and the charge, the
    /// same on both sides.
    static func balance(_ raw: String) -> Result<ChemBalanced, ChemBalanceError> {
        switch read(raw) {
        case .failure(let e): return .failure(e)
        case .success(let species): return balance(species)
        }
    }

    static func balance(_ species: [ChemSpecies]) -> Result<ChemBalanced, ChemBalanceError> {
        guard species.contains(where: { $0.reactant }), species.contains(where: { !$0.reactant }) else { return .failure(.emptySide) }
        var elements: [String] = []
        for s in species { for e in s.formula.elements where !elements.contains(e) { elements.append(e) } }
        // (an element on one side only can never balance)
        let oneSide = elements.filter { e in
            let left = species.contains { $0.reactant && ($0.formula.counts[e] ?? 0) > 0 }
            let right = species.contains { !$0.reactant && ($0.formula.counts[e] ?? 0) > 0 }
            return left != right
        }
        guard oneSide.isEmpty else { return .failure(.oneSide(oneSide)) }

        // (the matrix: a row per element, and one for the charge when any species has one; reactants count positive,
        // products negative, so a balanced equation is a null vector)
        var rows: [[ChemFraction]] = elements.map { e in
            species.map { s in ChemFraction(Int64((s.formula.counts[e] ?? 0) * (s.reactant ? 1 : -1)), 1) }
        }
        if species.contains(where: { $0.formula.charge != 0 }) {
            rows.append(species.map { s in ChemFraction(Int64(s.formula.charge * (s.reactant ? 1 : -1)), 1) })
        }
        let n = species.count
        do {
            let (reduced, pivots) = try rref(rows, columns: n)
            let free = (0..<n).filter { !pivots.contains($0) }
            guard !free.isEmpty else { return .failure(.impossible(nil)) }
            guard free.count == 1 else { return .failure(.manyWays) }
            let f = free[0]
            var x = Array(repeating: ChemFraction.zero, count: n)
            x[f] = .one
            for (i, p) in pivots.enumerated() { x[p] = try ChemFraction.zero - reduced[i][f] }
            // (whole numbers: times the denominators' lcm, over the numerators' gcd)
            var lcm: Int64 = 1
            for v in x {
                let g = ChemFraction.gcd(lcm, v.den)
                let (m, o) = (lcm / g).multipliedReportingOverflow(by: v.den)
                guard !o else { return .failure(.tooBig) }
                lcm = m
            }
            var ints: [Int64] = []
            for v in x {
                let (m, o) = v.num.multipliedReportingOverflow(by: lcm / v.den)
                guard !o else { return .failure(.tooBig) }
                ints.append(m)
            }
            var g: Int64 = 0
            for v in ints { g = ChemFraction.gcd(g, abs(v)) }
            if g > 1 { ints = ints.map { $0 / g } }
            if ints.allSatisfy({ $0 <= 0 }) { ints = ints.map { -$0 } }
            if let bad = ints.firstIndex(where: { $0 <= 0 }) { return .failure(.impossible(species[bad].text)) }
            guard ints.allSatisfy({ $0 <= Int64(Int32.max) }) else { return .failure(.tooBig) }
            let coefficients = ints.map { Int($0) }
            let changed = zip(species, coefficients).contains { ($0.typedCoefficient ?? 1) != $1 }
            return .success(ChemBalanced(species: species, coefficients: coefficients, changed: changed))
        } catch {
            return .failure(.tooBig)
        }
    }

    /// The matrix in reduced row echelon form, and the column of each pivot row's pivot.
    static func rref(_ m: [[ChemFraction]], columns: Int) throws -> ([[ChemFraction]], [Int]) {
        var a = m
        var pivots: [Int] = []
        var r = 0
        for c in 0..<columns where r < a.count {
            guard let p = (r..<a.count).first(where: { !a[$0][c].isZero }) else { continue }
            a.swapAt(r, p)
            let lead = a[r][c]
            for j in 0..<columns { a[r][j] = try a[r][j] / lead }
            for i in 0..<a.count where i != r && !a[i][c].isZero {
                let k = a[i][c]
                for j in 0..<columns { a[i][j] = try a[i][j] - k * a[r][j] }
            }
            pivots.append(c)
            r += 1
        }
        return (a, pivots)
    }
}

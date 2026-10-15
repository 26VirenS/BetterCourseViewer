import Foundation

// (tools) Molar Mass & Stoichiometry's arithmetic — Foundation only, checked by the Models job: an amount of a
// substance in grams, moles, particles or litres of gas at STP turned into the other three; a balanced reaction
// (ChemBalance.swift) with what is given of each reactant — the limiting reagent, each product's theoretical yield,
// what is left of the reactants in excess, and a percent yield; and an empirical formula from each element's share,
// with the molecular formula when the molar mass is known.

enum ChemStoich {
    static let avogadro = 6.02214076e23
    /// A mole of any gas at STP, in litres.
    static let stpLitres = 22.4

    // MARK: Convert

    /// How an amount is given.
    enum AmountUnit: String, CaseIterable {
        case grams, moles, particles, litres

        var label: String {
            switch self {
            case .grams: return "grams"
            case .moles: return "moles"
            case .particles: return "molecules / formula units"
            case .litres: return "litres of gas at STP"
            }
        }

        var symbol: String {
            switch self {
            case .grams: return "g"
            case .moles: return "mol"
            case .particles: return "particles"
            case .litres: return "L"
            }
        }
    }

    /// One amount in every way: grams, moles, particles and litres of gas at STP.
    struct Amounts: Equatable {
        let grams: Double
        let moles: Double
        let particles: Double
        let litres: Double

        func value(_ u: AmountUnit) -> Double {
            switch u {
            case .grams: return grams
            case .moles: return moles
            case .particles: return particles
            case .litres: return litres
            }
        }
    }

    /// `value` of a substance of `molarMass`, given in `unit`, in every unit (nil for a mass of nothing).
    static func convert(_ value: Double, _ unit: AmountUnit, molarMass: Double) -> Amounts? {
        guard molarMass > 0, value.isFinite else { return nil }
        let moles: Double
        switch unit {
        case .grams: moles = value / molarMass
        case .moles: moles = value
        case .particles: moles = value / avogadro
        case .litres: moles = value / stpLitres
        }
        return Amounts(grams: moles * molarMass, moles: moles, particles: moles * avogadro, litres: moles * stpLitres)
    }

    // MARK: Reaction

    /// What is given of a reactant: its amount, in grams or moles.
    struct Given: Equatable {
        let value: Double
        let grams: Bool
    }

    /// A species' amount in moles and grams.
    struct Amount: Equatable {
        let species: ChemSpecies
        let moles: Double
        let grams: Double
    }

    /// The reaction worked out from what is given: which reactant runs out first, how far the reaction goes (moles
    /// of the equation as written), each product's theoretical yield, and what is left of the other reactants given.
    struct Reaction: Equatable {
        let limiting: ChemSpecies
        let extent: Double
        let yields: [Amount]
        let excess: [Amount]
    }

    /// The reaction of `balanced` with `given` (by each reactant's place among the species; reactants not given are
    /// taken to be in excess). Nil until at least one reactant is given.
    static func reaction(_ balanced: ChemBalanced, given: [Int: Given]) -> Reaction? {
        var best: (index: Int, extent: Double)?
        var moles: [Int: Double] = [:]
        for (i, s) in balanced.species.enumerated() where s.reactant {
            guard let g = given[i], g.value >= 0 else { continue }
            let n = g.grams ? g.value / s.formula.molarMass : g.value
            moles[i] = n
            let extent = n / Double(balanced.coefficients[i])
            if best == nil || extent < best!.extent { best = (i, extent) }
        }
        guard let best else { return nil }
        var yields: [Amount] = []
        var excess: [Amount] = []
        for (i, s) in balanced.species.enumerated() {
            let c = Double(balanced.coefficients[i])
            if !s.reactant {
                let n = c * best.extent
                yields.append(Amount(species: s, moles: n, grams: n * s.formula.molarMass))
            } else if i != best.index, let n = moles[i] {
                let left = max(0, n - c * best.extent)
                excess.append(Amount(species: s, moles: left, grams: left * s.formula.molarMass))
            }
        }
        return Reaction(limiting: balanced.species[best.index], extent: best.extent, yields: yields, excess: excess)
    }

    /// The percent yield: what was made over what could have been.
    static func percentYield(actual: Double, theoretical: Double) -> Double? {
        guard theoretical > 0, actual >= 0, actual.isFinite else { return nil }
        return actual / theoretical * 100
    }

    /// "H2=4g,O2=16g" (the launch's -SimplMolesGiven, and what the tool keeps): each species' given amount, by its
    /// text as typed in the equation.
    static func givens(_ s: String?) -> [String: Given] {
        var out: [String: Given] = [:]
        for (key, raw) in ChemLaunchValues.parse(s) {
            var t = raw.trimmingCharacters(in: .whitespaces)
            var grams = true
            if t.lowercased().hasSuffix("mol") { grams = false; t = String(t.dropLast(3)) } else if t.lowercased().hasSuffix("g") { t = String(t.dropLast()) }
            guard let v = ChemNumber.number(t) else { continue }
            out[key] = Given(value: v, grams: grams)
        }
        return out
    }

    // MARK: Empirical

    enum EmpiricalError: Error, Equatable {
        case empty
        case unknown(String)
        case noRatio

        var message: String {
            switch self {
            case .empty: return "Give each element with its mass % (or grams)."
            case .unknown(let s): return "Can't read `\(s)` — is it an element's symbol?"
            case .noRatio: return "These don't come to whole-number ratios — check the percentages (they should add to 100)."
            }
        }
    }

    struct Empirical: Equatable {
        /// Each element with its count, in the order given.
        let empirical: [(symbol: String, count: Int)]
        let empiricalMass: Double
        /// The molecular formula's multiple of the empirical one, when a molar mass was given.
        let multiple: Int?

        var empiricalText: String { Self.text(empirical) }
        var molecularText: String? { multiple.map { k in Self.text(empirical.map { ($0.symbol, $0.count * k) }) } }

        static func text(_ parts: [(symbol: String, count: Int)]) -> String {
            parts.map { $0.count == 1 ? $0.symbol : "\($0.symbol)\($0.count)" }.joined()
        }

        static func == (a: Empirical, b: Empirical) -> Bool {
            a.empiricalText == b.empiricalText && a.empiricalMass == b.empiricalMass && a.multiple == b.multiple
        }
    }

    /// The empirical formula from each element's share (mass % or grams: the same arithmetic): moles of each, over
    /// the smallest, then times 2 to 6 until every ratio is within 0.08 of a whole number. With `molarMass`, the
    /// molecular formula as the nearest whole multiple.
    static func empirical(_ parts: [(symbol: String, amount: Double)], molarMass: Double?) -> Result<Empirical, EmpiricalError> {
        let used = parts.filter { !$0.symbol.trimmingCharacters(in: .whitespaces).isEmpty || $0.amount != 0 }
        guard !used.isEmpty else { return .failure(.empty) }
        var moles: [(String, Double)] = []
        for p in used {
            let sym = p.symbol.trimmingCharacters(in: .whitespaces)
            guard let m = ChemElements.mass(sym) else { return .failure(.unknown(sym)) }
            guard p.amount > 0, p.amount.isFinite else { return .failure(.empty) }
            moles.append((sym, p.amount / m))
        }
        guard let least = moles.map(\.1).min(), least > 0 else { return .failure(.empty) }
        let ratios = moles.map { $0.1 / least }
        var counts: [Int]?
        for k in 1...6 {
            let scaled = ratios.map { $0 * Double(k) }
            if scaled.allSatisfy({ abs($0 - $0.rounded()) <= 0.08 }) {
                counts = scaled.map { Int($0.rounded()) }
                break
            }
        }
        guard let counts else { return .failure(.noRatio) }
        let formula = zip(moles, counts).map { (symbol: $0.0, count: $1) }
        let mass = formula.reduce(0.0) { $0 + Double($1.count) * (ChemElements.mass($1.symbol) ?? 0) }
        var multiple: Int?
        if let molarMass, molarMass > 0, mass > 0 { multiple = max(1, Int((molarMass / mass).rounded())) }
        return .success(Empirical(empirical: formula, empiricalMass: mass, multiple: multiple))
    }

    /// "C=40,H=6.71,O=53.3" (the launch's -SimplMolesEmpirical, and what the tool keeps): each element's share, in order.
    static func shares(_ s: String?) -> [(symbol: String, amount: Double)] {
        var out: [(symbol: String, amount: Double)] = []
        for part in (s ?? "").split(separator: ",") {
            let kv = part.split(separator: "=", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) }
            guard kv.count == 2, let v = ChemNumber.number(kv[1]) else { continue }
            out.append((symbol: kv[0], amount: v))
        }
        return out
    }
}

import Foundation

// (tools) Solutions & pH's arithmetic — Foundation only, checked by the Models job: molarity from any two of moles,
// volume and concentration (with mass % and ppm); a dilution, M₁V₁ = M₂V₂; pH, pOH, [H⁺] and [OH⁻] from any one of them
// (Kw = 1.0 × 10⁻¹⁴, 25 °C); the pH of a strong or weak acid or base from its concentration (a weak one's quadratic
// solved exactly, with its percent ionised); a titration's equivalence volume, its pH at the points that matter and
// its whole curve, each point from the charge balance; and a buffer's pH by Henderson–Hasselbalch.

enum ChemSolution {
    /// Water's ion product at 25 °C.
    static let kw = 1.0e-14

    /// The fields and the one-blank rule are the gas laws' (ChemGas.Field, ChemGas.blank), and so is what goes wrong.
    typealias SolveError = ChemGas.SolveError

    // MARK: Molarity

    /// Moles, litres and molarity, the one left blank solved from the other two (`fields` named "moles", "volume",
    /// "molarity"; in mol, L and mol/L).
    static func molarity(_ fields: [ChemGas.Field]) -> Result<(String, Double), SolveError> {
        switch ChemGas.blank(of: fields) {
        case .failure(let e): return .failure(e)
        case .success(let which):
            func v(_ name: String) -> Double { fields.first { $0.name == name }?.value ?? 0 }
            for f in fields where !f.isBlank && (f.value ?? 0) <= 0 { return .failure(.impossible("\(f.name.capitalized) must be more than 0.")) }
            switch which {
            case "moles": return .success((which, v("molarity") * v("volume")))
            case "volume": return .success((which, v("moles") / v("molarity")))
            default: return .success((which, v("moles") / v("volume")))
            }
        }
    }

    /// The solute's share of the solution's mass, taking the solution as water-like (1.00 g/mL): `grams` of solute in
    /// `litres` of solution.
    static func massPercent(grams: Double, litres: Double) -> Double? {
        guard grams >= 0, litres > 0 else { return nil }
        let solution = litres * 1000
        guard solution > 0 else { return nil }
        return grams / solution * 100
    }

    /// Parts per million as mg of solute per litre of a dilute water-like solution.
    static func ppm(grams: Double, litres: Double) -> Double? {
        guard grams >= 0, litres > 0 else { return nil }
        return grams * 1000 / litres
    }

    // MARK: Dilution

    /// M₁V₁ = M₂V₂ for the one blank of the four (`fields` named "M1", "V1", "M2", "V2"; the volumes in any one unit).
    static func dilution(_ fields: [ChemGas.Field]) -> Result<(String, Double), SolveError> {
        switch ChemGas.blank(of: fields) {
        case .failure(let e): return .failure(e)
        case .success(let which):
            func v(_ name: String) -> Double { fields.first { $0.name == name }?.value ?? 0 }
            for f in fields where !f.isBlank && (f.value ?? 0) <= 0 { return .failure(.impossible("\(f.name) must be more than 0.")) }
            // (a dilution never ends stronger than the stock: said plainly rather than with a volume under the stock's)
            if which == "V2", v("M2") > v("M1") { return .failure(.impossible("The final concentration can't be more than the stock's.")) }
            switch which {
            case "M1": return .success((which, v("M2") * v("V2") / v("V1")))
            case "V1": return .success((which, v("M2") * v("V2") / v("M1")))
            case "M2": return .success((which, v("M1") * v("V1") / v("V2")))
            default: return .success((which, v("M1") * v("V1") / v("M2")))
            }
        }
    }

    // MARK: pH

    /// pH, pOH, [H⁺] and [OH⁻] together.
    struct Acidity: Equatable {
        let pH: Double
        let pOH: Double
        let h: Double
        let oh: Double

        init(h: Double) {
            self.h = h
            oh = ChemSolution.kw / h
            pH = -log10(h)
            pOH = 14 - pH
        }

        /// Acidic, neutral or basic, in a word.
        var word: String { abs(pH - 7) < 0.005 ? "neutral" : (pH < 7 ? "acidic" : "basic") }
    }

    enum Given: String, CaseIterable {
        case pH, pOH, h, oh

        var label: String {
            switch self {
            case .pH: return "pH"
            case .pOH: return "pOH"
            case .h: return "[H⁺]"
            case .oh: return "[OH⁻]"
            }
        }
    }

    /// The other three from any one (nil for a concentration of nothing or less).
    static func acidity(_ given: Given, _ value: Double) -> Acidity? {
        guard value.isFinite else { return nil }
        switch given {
        case .pH: return Acidity(h: pow(10, -value))
        case .pOH: return Acidity(h: pow(10, -(14 - value)))
        case .h: return value > 0 ? Acidity(h: value) : nil
        case .oh: return value > 0 ? Acidity(h: kw / value) : nil
        }
    }

    enum Kind: String, CaseIterable {
        case strongAcid, strongBase, weakAcid, weakBase

        var label: String {
            switch self {
            case .strongAcid: return "strong acid"
            case .strongBase: return "strong base"
            case .weakAcid: return "weak acid (Ka)"
            case .weakBase: return "weak base (Kb)"
            }
        }

        var weak: Bool { self == .weakAcid || self == .weakBase }
        var acid: Bool { self == .strongAcid || self == .weakAcid }
        /// The constant's name for a weak one.
        var constant: String { self == .weakBase ? "Kb" : "Ka" }
    }

    /// The pH of a solution from what is in it: a strong acid or base, with water's own ions counted (so a very dilute
    /// one never goes past 7), or a weak one by its Ka or Kb, the quadratic solved exactly, with its percent ionised.
    static func fromConcentration(_ kind: Kind, c: Double, k: Double?) -> (acidity: Acidity, ionised: Double?)? {
        guard c > 0, c.isFinite else { return nil }
        switch kind {
        case .strongAcid:
            return (Acidity(h: (c + (c * c + 4 * kw).squareRoot()) / 2), nil)
        case .strongBase:
            let oh = (c + (c * c + 4 * kw).squareRoot()) / 2
            return (Acidity(h: kw / oh), nil)
        case .weakAcid, .weakBase:
            guard let k, k > 0, k.isFinite else { return nil }
            // (x² + Kx − KC = 0: the ion made, exactly, not the 5 % approximation)
            let x = (-k + (k * k + 4 * k * c).squareRoot()) / 2
            let ionised = x / c * 100
            return (kind == .weakAcid ? Acidity(h: x) : Acidity(h: kw / x), ionised)
        }
    }

    // MARK: Titration

    enum TitrationKind: String, CaseIterable {
        case strongAcidStrongBase, weakAcidStrongBase, weakBaseStrongAcid

        var label: String {
            switch self {
            case .strongAcidStrongBase: return "strong acid – strong base"
            case .weakAcidStrongBase: return "weak acid – strong base"
            case .weakBaseStrongAcid: return "weak base – strong acid"
            }
        }

        var weak: Bool { self != .strongAcidStrongBase }
        /// The constant's name for a weak analyte.
        var constant: String { self == .weakBaseStrongAcid ? "Kb" : "Ka" }
        /// The analyte is an acid, titrated with a base.
        var acidAnalyte: Bool { self != .weakBaseStrongAcid }
    }

    struct Titration: Equatable {
        /// The titrant volume at equivalence, in the analyte volume's unit.
        let equivalence: Double
        let start: Double
        let half: Double
        let atEquivalence: Double
        /// At twice the equivalence volume.
        let after: Double
        /// 200 points from no titrant to twice the equivalence volume.
        let curve: [(volume: Double, pH: Double)]

        static func == (a: Titration, b: Titration) -> Bool {
            a.equivalence == b.equivalence && a.start == b.start && a.half == b.half && a.atEquivalence == b.atEquivalence && a.after == b.after
                && a.curve.count == b.curve.count && zip(a.curve, b.curve).allSatisfy { $0.volume == $1.volume && $0.pH == $1.pH }
        }
    }

    /// The titration of `analyteVolume` of a `analyte` molar analyte with a `titrant` molar titrant (`k` the weak
    /// analyte's Ka or Kb): its equivalence volume and the pH at the start, half-way to equivalence, at it and past it,
    /// and its curve. Nil for a concentration or volume of nothing.
    static func titration(_ kind: TitrationKind, analyte: Double, analyteVolume: Double, titrant: Double, k: Double?) -> Titration? {
        guard analyte > 0, analyteVolume > 0, titrant > 0, !kind.weak || (k ?? 0) > 0 else { return nil }
        let veq = analyte * analyteVolume / titrant
        func at(_ v: Double) -> Double { titrationPH(kind, analyte: analyte, analyteVolume: analyteVolume, titrant: titrant, titrantVolume: v, k: k) }
        let curve = (0..<200).map { i -> (volume: Double, pH: Double) in
            let v = 2 * veq * Double(i) / 199
            return (v, at(v))
        }
        return Titration(equivalence: veq, start: at(0), half: at(veq / 2), atEquivalence: at(veq), after: at(2 * veq), curve: curve)
    }

    /// The pH with `titrantVolume` added: the one [H⁺] that balances the charges — the strong ions as they are, a weak
    /// acid's anion by Ka/(Ka + [H⁺]), a weak base's cation by [H⁺]/([H⁺] + Kw/Kb) — found by bisection on the pH.
    static func titrationPH(_ kind: TitrationKind, analyte: Double, analyteVolume: Double, titrant: Double, titrantVolume: Double, k: Double?) -> Double {
        let total = analyteVolume + titrantVolume
        guard total > 0 else { return 7 }
        let ca = analyte * analyteVolume / total, ct = titrant * titrantVolume / total
        let ka = k ?? 0
        func balance(_ h: Double) -> Double {
            switch kind {
            case .strongAcidStrongBase: return ct + h - kw / h - ca
            case .weakAcidStrongBase: return ct + h - kw / h - ca * ka / (ka + h)
            case .weakBaseStrongAcid:
                let conjugate = kw / ka
                return ca * h / (h + conjugate) + h - kw / h - ct
            }
        }
        // (the balance rises with [H⁺]: positive at pH −2, negative at pH 16, so the root is between)
        var lo = -2.0, hi = 16.0
        for _ in 0..<80 {
            let mid = (lo + hi) / 2
            if balance(pow(10, -mid)) > 0 { lo = mid } else { hi = mid }
        }
        return (lo + hi) / 2
    }

    // MARK: Buffer

    static func pKa(_ ka: Double) -> Double? { ka > 0 && ka.isFinite ? -log10(ka) : nil }

    /// Henderson–Hasselbalch: pH = pKa + log([A⁻]/[HA]) (nil without both parts).
    static func bufferPH(pKa: Double, base: Double, acid: Double) -> Double? {
        guard base > 0, acid > 0, pKa.isFinite else { return nil }
        return pKa + log10(base / acid)
    }
}

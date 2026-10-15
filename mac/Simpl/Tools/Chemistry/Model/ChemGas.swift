import Foundation

// (tools) Gas Laws' arithmetic — Foundation only, checked by the Models job: pressure, volume, amount and
// temperature in their units and in the ones PV = nRT is solved in (atm, L, mol, K; R = 0.082057 L·atm/(mol·K)); the
// ideal gas law solved for whichever one of the four is left blank; and the combined law P₁V₁/T₁ = P₂V₂/T₂ solved for
// the one of six left blank, with a pair held constant by Boyle's, Charles's or Gay-Lussac's law.

enum ChemGas {
    /// The gas constant in L·atm/(mol·K).
    static let R = 0.082057

    // MARK: Units

    enum PressureUnit: String, CaseIterable {
        case atm, kPa, mmHg, bar, psi

        var label: String { self == .mmHg ? "mmHg (Torr)" : rawValue }

        /// One of this unit in atmospheres.
        var atm: Double {
            switch self {
            case .atm: return 1
            case .kPa: return 1 / 101.325
            case .mmHg: return 1 / 760
            case .bar: return 1 / 1.01325
            case .psi: return 1 / 14.6959
            }
        }

        /// The unit a launch flag or a student names: "Torr" is mmHg, "kpa" kPa.
        static func named(_ s: String?) -> PressureUnit? {
            guard let s = s?.trimmingCharacters(in: .whitespaces).lowercased() else { return nil }
            if s == "torr" { return .mmHg }
            return allCases.first { $0.rawValue.lowercased() == s }
        }
    }

    enum VolumeUnit: String, CaseIterable {
        case L, mL, m3

        var label: String { self == .m3 ? "m³" : rawValue }

        /// One of this unit in litres.
        var litres: Double {
            switch self {
            case .L: return 1
            case .mL: return 0.001
            case .m3: return 1000
            }
        }

        static func named(_ s: String?) -> VolumeUnit? {
            guard let s = s?.trimmingCharacters(in: .whitespaces).lowercased() else { return nil }
            if s == "m³" || s == "m^3" { return .m3 }
            return allCases.first { $0.rawValue.lowercased() == s }
        }
    }

    enum TemperatureUnit: String, CaseIterable {
        case K, C, F

        var label: String {
            switch self {
            case .K: return "K"
            case .C: return "°C"
            case .F: return "°F"
            }
        }

        /// `v` of this unit in kelvin.
        func kelvin(_ v: Double) -> Double {
            switch self {
            case .K: return v
            case .C: return v + 273.15
            case .F: return (v - 32) * 5 / 9 + 273.15
            }
        }

        /// `k` kelvin in this unit.
        func from(kelvin k: Double) -> Double {
            switch self {
            case .K: return k
            case .C: return k - 273.15
            case .F: return (k - 273.15) * 9 / 5 + 32
            }
        }

        static func named(_ s: String?) -> TemperatureUnit? {
            guard let s = s?.trimmingCharacters(in: .whitespaces).lowercased() else { return nil }
            switch s {
            case "k", "kelvin": return .K
            case "c", "°c", "celsius": return .C
            case "f", "°f", "fahrenheit": return .F
            default: return nil
            }
        }
    }

    // MARK: The ideal gas law

    enum Variable: String, CaseIterable {
        case P, V, n, T
    }

    enum SolveError: Error, Equatable {
        /// Every field has a value: nothing to solve.
        case nothingBlank
        /// Two or more blank.
        case tooManyBlank
        /// A field that is not a number, named.
        case unreadable(String)
        /// A value that can't be: a temperature at or below absolute zero, a pressure, volume or amount of nothing.
        case impossible(String)

        var message: String {
            switch self {
            case .nothingBlank: return "Leave one field blank — it is the one that gets solved."
            case .tooManyBlank: return "Fill in all but one."
            case .unreadable(let f): return "Can't read \(f) — is it a number?"
            case .impossible(let f): return f
            }
        }
    }

    /// A field as typed: its text and its value in the law's own unit (nil when blank; nil value with text when it
    /// cannot be read).
    struct Field: Equatable {
        let name: String
        let text: String
        let value: Double?

        var isBlank: Bool { text.trimmingCharacters(in: .whitespaces).isEmpty }
    }

    /// PV = nRT solved for the one blank field, in atm, L, mol and K: which it is and its value.
    static func ideal(P: Field, V: Field, n: Field, T: Field) -> Result<(Variable, Double), SolveError> {
        switch blank(of: [P, V, n, T]) {
        case .failure(let e): return .failure(e)
        case .success(let which):
            let p = P.value ?? 0, v = V.value ?? 0, m = n.value ?? 0, t = T.value ?? 0
            if !T.isBlank, t <= 0 { return .failure(.impossible("The temperature must be above absolute zero (0 K).")) }
            for (f, x) in [(P, p), (V, v), (n, m)] where !f.isBlank && x <= 0 {
                return .failure(.impossible("\(f.name) must be more than 0."))
            }
            switch which {
            case "P": return .success((.P, m * R * t / v))
            case "V": return .success((.V, m * R * t / p))
            case "n": return .success((.n, p * v / (R * t)))
            default: return .success((.T, p * v / (m * R)))
            }
        }
    }

    /// The one blank field among `fields` (by name), or why there is not exactly one; a field with text that is no
    /// number is said first.
    static func blank(of fields: [Field]) -> Result<String, SolveError> {
        if let bad = fields.first(where: { !$0.isBlank && $0.value == nil }) { return .failure(.unreadable(bad.name)) }
        let blanks = fields.filter(\.isBlank)
        guard !blanks.isEmpty else { return .failure(.nothingBlank) }
        guard blanks.count == 1 else { return .failure(.tooManyBlank) }
        return .success(blanks[0].name)
    }

    // MARK: The combined law

    /// A pair held constant: Boyle's law (T), Charles's (P), Gay-Lussac's (V); nil for the whole combined law.
    enum Constant: String, CaseIterable {
        case T, P, V

        var law: String {
            switch self {
            case .T: return "Boyle (T constant)"
            case .P: return "Charles (P constant)"
            case .V: return "Gay-Lussac (V constant)"
            }
        }

        /// The fields the law leaves out, both states' of the constant quantity.
        var hidden: [String] { ["\(rawValue)1", "\(rawValue)2"] }
    }

    /// P₁V₁/T₁ = P₂V₂/T₂ solved for the one blank field of the six (named P1, V1, T1, P2, V2, T2), in atm, L and K; with
    /// a `constant`, that pair is left out and the four others solved.
    static func combined(_ fields: [String: Field], constant: Constant?) -> Result<(String, Double), SolveError> {
        let names = ["P1", "V1", "T1", "P2", "V2", "T2"].filter { !(constant?.hidden.contains($0) ?? false) }
        let used = names.compactMap { fields[$0] }
        guard used.count == names.count else { return .failure(.tooManyBlank) }
        switch blank(of: used) {
        case .failure(let e): return .failure(e)
        case .success(let which):
            func v(_ name: String) -> Double { constant?.hidden.contains(name) == true ? 1 : (fields[name]?.value ?? 0) }
            for f in used where !f.isBlank && (f.value ?? 0) <= 0 {
                return .failure(.impossible(f.name.hasPrefix("T") ? "\(f.name) must be above absolute zero (0 K)." : "\(f.name) must be more than 0."))
            }
            let p1 = v("P1"), v1 = v("V1"), t1 = v("T1"), p2 = v("P2"), v2 = v("V2"), t2 = v("T2")
            switch which {
            case "P1": return .success((which, p2 * v2 * t1 / (t2 * v1)))
            case "V1": return .success((which, p2 * v2 * t1 / (t2 * p1)))
            case "T1": return .success((which, p1 * v1 * t2 / (p2 * v2)))
            case "P2": return .success((which, p1 * v1 * t2 / (t1 * v2)))
            case "V2": return .success((which, p1 * v1 * t2 / (t1 * p2)))
            default: return .success((which, p2 * v2 * t1 / (p1 * v1)))
            }
        }
    }

    // MARK: The piston drawing

    /// How full the cylinder is drawn for a volume `litres`: a tenth of a litre a little way up, a hundred litres
    /// nearly to the top (a log scale, so 1 L sits a third of the way).
    static func pistonHeight(litres: Double) -> Double {
        guard litres > 0, litres.isFinite else { return 0.5 }
        return min(max((log10(litres) + 1) / 3, 0.12), 0.95)
    }

    /// How many dots are drawn for `moles` in `litres`: a few for a thin gas, up to sixty for a dense one.
    static func pistonDots(moles: Double, litres: Double) -> Int {
        guard moles > 0, litres > 0, moles.isFinite, litres.isFinite else { return 12 }
        return min(max(Int((moles / litres * 30).rounded()), 4), 60)
    }

    /// How warm the gas is drawn, 0 (cold, 150 K and under) to 1 (hot, 600 K and over).
    static func pistonWarmth(kelvin: Double) -> Double {
        guard kelvin.isFinite else { return 0.4 }
        return min(max((kelvin - 150) / 450, 0), 1)
    }
}

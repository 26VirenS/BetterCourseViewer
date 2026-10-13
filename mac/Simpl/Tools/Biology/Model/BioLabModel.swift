import Foundation

// Bio Lab Calculator's own model (spec §4.8, §5.5): lab mathematics and the AP formula sheet, worked the way it is
// written on paper — the formula, the numbers substituted with their units, the answer with units and sensible
// significant figures, and the steps (unit conversions first). Foundation only, so the Mac app's workflow (Models,
// scripts/dev/swift-checks) compiles and checks it with the rest of Biology/Model.
//
// This file holds what every calculator shares: units and their menus (the AP sheet's prefixes), numbers as typed
// (with their significant figures), numbers as shown (rounded half up on the decimal digits, not on the binary value,
// so 7.305 shows as 7.31), and a calculator as values — its fields, its "solve for" modes and its working — so one card
// draws any of them. The calculators themselves are in BioLabCalculators.swift.

// MARK: - Units

/// What a field measures: its unit menu.
enum BioLabQuantity: String, CaseIterable {
    case concentration, volume, mass, length, time, amount, massConcentration
    /// A dilution's strengths (C₁V₁ = C₂V₂ in any matching units): molar, percent, "×" stock or mass per volume.
    case solution
    /// A serial dilution's starting strength: carried through unchanged (cells/mL stay cells/mL).
    case stock

    var units: [BioLabUnit] {
        switch self {
        case .concentration: return BioLabUnit.molar
        case .volume: return [BioLabUnit("L", "litres", 1), BioLabUnit("mL", "millilitres", 1e-3), BioLabUnit("µL", "microlitres", 1e-6)]
        case .mass: return [BioLabUnit("g", "grams", 1), BioLabUnit("mg", "milligrams", 1e-3), BioLabUnit("µg", "micrograms", 1e-6),
                            BioLabUnit("ng", "nanograms", 1e-9)]
        case .length: return [BioLabUnit("m", "metres", 1), BioLabUnit("cm", "centimetres", 1e-2), BioLabUnit("mm", "millimetres", 1e-3),
                              BioLabUnit("µm", "micrometres", 1e-6), BioLabUnit("nm", "nanometres", 1e-9)]
        case .time: return [BioLabUnit("s", "seconds", 1), BioLabUnit("min", "minutes", 60), BioLabUnit("h", "hours", 3600),
                            BioLabUnit("d", "days", 86_400)]
        case .amount: return [BioLabUnit("mol", "moles", 1), BioLabUnit("mmol", "millimoles", 1e-3), BioLabUnit("µmol", "micromoles", 1e-6),
                              BioLabUnit("nmol", "nanomoles", 1e-9)]
        case .massConcentration: return [BioLabUnit("µg/mL", "micrograms per millilitre", 1e-3), BioLabUnit("ng/µL", "nanograms per microlitre", 1e-3),
                                         BioLabUnit("mg/mL", "milligrams per millilitre", 1), BioLabUnit("g/L", "grams per litre", 1)]
        case .solution: return BioLabUnit.molar + [BioLabUnit("%", "percent", 1, group: "percent"), BioLabUnit("×", "times (a concentrated stock)", 1, group: "times"),
                                                   BioLabUnit("mg/mL", "milligrams per millilitre", 1, group: "mass"),
                                                   BioLabUnit("µg/mL", "micrograms per millilitre", 1e-3, group: "mass")]
        case .stock: return [BioLabUnit("M", "molar", 1, group: "M"), BioLabUnit("mM", "millimolar", 1, group: "mM"),
                             BioLabUnit("µM", "micromolar", 1, group: "µM"), BioLabUnit("mg/mL", "milligrams per millilitre", 1, group: "mg/mL"),
                             BioLabUnit("cells/mL", "cells per millilitre", 1, group: "cells/mL"), BioLabUnit("CFU/mL", "colony-forming units per millilitre", 1, group: "CFU/mL")]
        }
    }

    func unit(_ symbol: String?) -> BioLabUnit? { units.first { $0.symbol == symbol } }
}

/// A unit in a field's menu: its size in its kind's base unit (1 mL = 0.001 L). Units convert only within a group (a
/// molar strength never becomes a percent).
struct BioLabUnit: Hashable, Identifiable {
    let symbol: String
    let name: String
    let factor: Double
    var group = ""

    var id: String { symbol }

    init(_ symbol: String, _ name: String, _ factor: Double, group: String = "") {
        self.symbol = symbol
        self.name = name
        self.factor = factor
        self.group = group
    }

    static let molar = [BioLabUnit("M", "molar (mol/L)", 1, group: "molar"), BioLabUnit("mM", "millimolar", 1e-3, group: "molar"),
                        BioLabUnit("µM", "micromolar", 1e-6, group: "molar"), BioLabUnit("nM", "nanomolar", 1e-9, group: "molar")]
}

/// A metric prefix as the AP sheet lists it.
struct BioLabPrefix {
    let name: String
    let symbol: String
    let exponent: Int
}

// MARK: - Numbers as typed

/// A number typed into a calculator: its value, and how many significant figures it was written with (nil: exact — a
/// count, a lens's power, a dilution written as a power of ten or a ratio).
struct BioLabNumber: Equatable {
    let value: Double
    let sigFigs: Int?
}

/// Reading what a student types: "0.5", "250", "1.5e9", "1.5 × 10^9", "1.5x10^9", "10^-6", "10⁻⁶", "1:10", "1/10",
/// "3,000", "−2.4", "100%".
enum BioLabParse {
    private static let superscripts: [Character: Character] = ["⁰": "0", "¹": "1", "²": "2", "³": "3", "⁴": "4", "⁵": "5", "⁶": "6",
                                                               "⁷": "7", "⁸": "8", "⁹": "9", "⁻": "-", "⁺": "+"]

    /// The text made plain: superscripts written with ^, one kind of minus and of times, no spaces, commas, % or case.
    static func normalise(_ text: String) -> String {
        var out = ""
        var inSuper = false
        for ch in text {
            if let plain = superscripts[ch] {
                if !inSuper && out.last != "^" { out.append("^") }
                out.append(plain)
                inSuper = true
                continue
            }
            inSuper = false
            switch ch {
            case "−", "–", "—": out.append("-")
            case "×", "·", "*", "X": out.append("x")
            case " ", ",", "%", "\u{00a0}", "\u{2009}", "_": continue
            default: out.append(contentsOf: ch.lowercased())
            }
        }
        return out
    }

    /// A number, or nil if the text is not one.
    static func number(_ text: String) -> BioLabNumber? {
        let s = normalise(text)
        guard !s.isEmpty else { return nil }
        // (a ratio "1:10" or a fraction "1/10": exact)
        for sep in [":", "/"] where s.contains(sep) {
            let parts = s.components(separatedBy: sep)
            guard parts.count == 2, let a = number(parts[0])?.value, let b = number(parts[1])?.value, b != 0 else { return nil }
            return BioLabNumber(value: a / b, sigFigs: nil)
        }
        var mantissa = s
        var exponent = "0"
        var power = false
        if let r = s.range(of: "x10^") {
            mantissa = String(s[..<r.lowerBound])
            exponent = String(s[r.upperBound...])
        } else if s.hasPrefix("10^") {
            mantissa = "1"
            exponent = String(s.dropFirst(3))
            power = true
        } else if let i = s.firstIndex(of: "e") {
            mantissa = String(s[..<i])
            exponent = String(s[s.index(after: i)...])
        }
        let allowed = Set("0123456789.-+")
        guard !mantissa.isEmpty, mantissa.allSatisfy({ allowed.contains($0) }), exponent.allSatisfy({ allowed.contains($0) }),
              let m = Double(mantissa), let e = Double(exponent), e == e.rounded() else { return nil }
        let value = m * pow(10, e)
        guard value.isFinite else { return nil }
        // (a power of ten written as one is exact; so is a bare zero, a start time or a pressure potential of 0)
        if power || (m == 0 && !mantissa.contains(".")) { return BioLabNumber(value: value, sigFigs: nil) }
        return BioLabNumber(value: value, sigFigs: sigFigs(mantissa))
    }

    /// The significant figures of a written number: every digit from the first that is not zero, and trailing zeros
    /// only when there is a decimal point ("250" has 2, "250." has 3, "0.0500" has 3).
    static func sigFigs(_ written: String) -> Int {
        let s = written.filter { $0.isNumber || $0 == "." }
        let point = s.contains(".")
        let digits = s.filter(\.isNumber)
        let lead = digits.drop { $0 == "0" }
        if lead.isEmpty {
            // (a zero: its decimal places, at least one)
            let after = s.split(separator: ".", omittingEmptySubsequences: false)
            return max(1, point && after.count == 2 ? after[1].count : 1)
        }
        if point { return lead.count }
        return max(1, lead.reversed().drop { $0 == "0" }.count)
    }

    /// The decimal places as written ("7.00" has 2).
    static func decimals(_ text: String) -> Int {
        let s = normalise(text)
        guard let dot = s.firstIndex(of: ".") else { return 0 }
        return s[s.index(after: dot)...].prefix { $0.isNumber }.count
    }

    /// Numbers separated by commas, spaces, semicolons, colons or new lines ("12.1, 13.4", "9:3:3:1"), and what was
    /// not a number.
    static func list(_ text: String) -> (numbers: [BioLabNumber], bad: [String]) {
        let tokens = text.components(separatedBy: CharacterSet(charactersIn: ",;:\n\t ")).filter { !$0.isEmpty }
        var out: [BioLabNumber] = []
        var bad: [String] = []
        for t in tokens {
            if let n = number(t) { out.append(n) } else { bad.append(t) }
        }
        return (out, bad)
    }

    /// Names separated by commas or new lines.
    static func names(_ text: String) -> [String] {
        text.components(separatedBy: CharacterSet(charactersIn: ",;\n")).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }
}

// MARK: - Numbers as shown

/// Numbers written for a student: rounded half up on their decimal digits (7.305 → 7.31, though the binary value is
/// 7.30499…), a true minus sign, thousands grouped, and powers of ten as "1.07 × 10¹¹" below 0.001 and from 100,000 up.
enum BioLabFormat {
    private static let superDigits: [Character] = ["⁰", "¹", "²", "³", "⁴", "⁵", "⁶", "⁷", "⁸", "⁹"]

    /// −6 → "⁻⁶".
    static func superscript(_ n: Int) -> String {
        (n < 0 ? "⁻" : "") + String(String(abs(n)).compactMap { $0.wholeNumberValue.map { superDigits[$0] } })
    }

    /// 10ⁿ written as one ("10⁻⁴").
    static func power(_ e: Int) -> String { "10" + superscript(e) }

    /// |a| as decimal digits rounded half up at the 10^place digit: the digits and the power of ten of the first. (15
    /// significant digits of the binary value first, which drops its noise.)
    private static func digits(_ a: Double, place: Int) -> (digits: [Int], exponent: Int) {
        let s = String(format: "%.14e", a)
        guard let ei = s.firstIndex(of: "e"), var exp = Int(s[s.index(after: ei)...]) else { return ([0], place) }
        var ds = s[..<ei].compactMap { $0.wholeNumberValue }
        let keep = exp - place + 1
        if keep <= 0 { return keep == 0 && (ds.first ?? 0) >= 5 ? ([1], place) : ([0], place) }
        if keep < ds.count {
            let up = ds[keep] >= 5
            ds = Array(ds.prefix(keep))
            if up {
                var i = keep - 1
                while i >= 0 {
                    if ds[i] == 9 { ds[i] = 0; i -= 1 } else { ds[i] += 1; break }
                }
                if i < 0 {
                    ds.insert(1, at: 0)
                    exp += 1
                }
            }
        } else {
            ds += Array(repeating: 0, count: keep - ds.count)
        }
        return (ds, exp)
    }

    /// The power of ten of a number's first digit as written to 15 figures.
    private static func exponent(_ a: Double) -> Int {
        let s = String(format: "%.14e", a)
        guard let ei = s.firstIndex(of: "e") else { return 0 }
        return Int(s[s.index(after: ei)...]) ?? 0
    }

    private static func grouped(_ int: String) -> String {
        guard int.count >= 4 else { return int }
        var out = ""
        for (i, ch) in int.enumerated() {
            if i > 0 && (int.count - i) % 3 == 0 { out.append(",") }
            out.append(ch)
        }
        return out
    }

    private static func render(_ ds: [Int], _ e: Int, last p: Int, negative: Bool, scientific: Bool) -> String {
        let sign = negative ? "−" : ""
        if scientific {
            let m = ds.map(String.init)
            return sign + m[0] + (m.count > 1 ? "." + m[1...].joined() : "") + " × " + power(e)
        }
        func digit(_ place: Int) -> String {
            let i = e - place
            return i >= 0 && i < ds.count ? String(ds[i]) : "0"
        }
        var int = ""
        if e >= 0 { for place in stride(from: e, through: 0, by: -1) { int += digit(place) } } else { int = "0" }
        var frac = ""
        if p < 0 { for place in stride(from: -1, through: p, by: -1) { frac += digit(place) } }
        return sign + grouped(int) + (frac.isEmpty ? "" : "." + frac)
    }

    /// A number to `digits` significant figures ("7.31", "0.450", "1,006", "8.4 × 10⁵").
    static func sig(_ v: Double, _ digits: Int) -> String {
        guard v.isFinite else { return "—" }
        guard v != 0 else { return "0" }
        let a = abs(v), d = max(digits, 1)
        let r = Self.digits(a, place: exponent(a) - d + 1)
        var ds = r.digits
        let e = r.exponent
        if ds.count > d { ds = Array(ds.prefix(d)) }
        if ds.allSatisfy({ $0 == 0 }) { return "0" }
        return render(ds, e, last: e - d + 1, negative: v < 0, scientific: e >= 5 || e < -3)
    }

    /// A number to a fixed number of decimal places ("12.64", "7.00").
    static func fixed(_ v: Double, decimals: Int) -> String {
        guard v.isFinite else { return "—" }
        let n = max(decimals, 0)
        let (ds, e) = digits(abs(v), place: -n)
        let zero = ds.allSatisfy { $0 == 0 }
        return render(ds, e, last: -n, negative: v < 0 && !zero, scientific: false)
    }

    /// A number in working lines: up to 4 significant figures, trailing zeros dropped ("0.25", "12.57", "1 × 10⁻⁶").
    static func work(_ v: Double, digits: Int = 4) -> String { trimmed(sig(v, digits)) }

    /// Up to 6 significant figures, trailing zeros dropped: a typed number shown back as it means.
    static func plain(_ v: Double) -> String { trimmed(sig(v, 6)) }

    private static func trimmed(_ s: String) -> String {
        if let x = s.range(of: " × ") {
            return trimmed(String(s[..<x.lowerBound])) + s[x.lowerBound...]
        }
        guard s.contains(".") else { return s }
        var t = s
        while t.hasSuffix("0") { t.removeLast() }
        if t.hasSuffix(".") { t.removeLast() }
        return t
    }

    /// A dilution factor: a power of ten as one ("10⁻⁴"), else to 3 significant figures.
    static func dilution(_ f: Double, digits: Int = 3) -> String {
        guard f > 0, f.isFinite else { return "—" }
        let l = log10(f)
        if abs(l - l.rounded()) < 1e-9 { return power(Int(l.rounded())) }
        return sig(f, digits)
    }

    /// A step dilution as a ratio ("1:10"; 1:4 for 0.25).
    static func ratio(_ f: Double, digits: Int = 3) -> String {
        guard f > 0, f.isFinite else { return "—" }
        return "1:" + trimmed(sig(1 / f, max(digits, 3)))
    }
}

// MARK: - A calculator as values

/// A preset a field offers (a solute's molar mass, an objective lens): its words and the text it puts in the field.
struct BioLabPreset: Hashable {
    let label: String
    let text: String
}

/// A choice a field offers (dsDNA, RNA, ssDNA).
struct BioLabChoice: Hashable, Identifiable {
    let id: String
    let label: String
}

/// One of a calculator's fields.
struct BioLabField: Identifiable {
    enum Kind { case number, list, text, choice }

    let id: String
    /// As in the formula ("C₁").
    let symbol: String
    let label: String
    var kind: Kind
    /// A unit menu (nil: a fixed unit or none).
    var quantity: BioLabQuantity?
    /// The fixed unit ("g/mol", "×"), or the menu's first choice ("mL").
    var unit: String?
    var initial: String
    /// A count, a lens's power, a dilution: not a measurement, so it never limits the answer's precision.
    var exact: Bool
    /// Left empty, it is left out.
    var optional: Bool
    /// The answer's unit menu reads "per min" (a rate).
    var per: Bool
    var presets: [BioLabPreset]
    var choices: [BioLabChoice]
    var hint: String?

    init(_ id: String, _ symbol: String, _ label: String, kind: Kind = .number, quantity: BioLabQuantity? = nil, unit: String? = nil,
         initial: String = "", exact: Bool = false, optional: Bool = false, per: Bool = false, presets: [BioLabPreset] = [],
         choices: [BioLabChoice] = [], hint: String? = nil) {
        self.id = id
        self.symbol = symbol
        self.label = label
        self.kind = kind
        self.quantity = quantity
        self.unit = unit
        self.initial = initial
        self.exact = exact
        self.optional = optional
        self.per = per
        self.presets = presets
        self.choices = choices
        self.hint = hint
    }

    /// The unit menu's units (none for a fixed unit).
    var units: [BioLabUnit] { quantity?.units ?? [] }
    /// The unit it starts in.
    var initialUnit: BioLabUnit? { quantity.flatMap { q in q.unit(unit) ?? q.units.first } }
}

/// What a calculator solves for: the formula rearranged, the fields typed in, and the field it fills (if one).
struct BioLabMode: Identifiable {
    let id: String
    let title: String
    let formula: String
    let inputs: [String]
    let unknown: String?

    init(_ id: String, _ title: String, _ formula: String, inputs: [String], unknown: String? = nil) {
        self.id = id
        self.title = title
        self.formula = formula
        self.inputs = inputs
        self.unknown = unknown
    }
}

/// What a student typed into one calculator (remembered between launches).
struct BioLabEntry: Codable, Equatable {
    var mode: String
    var texts: [String: String]
    var units: [String: String]
}

/// Something wrong with what was typed: the field to mark (if one) and why, in a sentence.
struct BioLabProblem: Error, Equatable {
    let field: String?
    let message: String

    init(_ field: String?, _ message: String) {
        self.field = field
        self.message = message
    }
}

/// An answer: its symbol and words, its value in its unit, and how it is written.
struct BioLabAnswer: Equatable {
    let symbol: String
    let label: String
    /// In `unit`.
    let value: Double
    let unit: String
    /// The number as shown ("5.0").
    let text: String
    /// The same in a handier unit ("= 450 µm").
    var alt: String? = nil
    /// The field it fills (the unknown), if one.
    var field: String? = nil

    /// "5.0 mL", "400×", "2.0% w/v".
    var full: String {
        if unit.isEmpty { return text }
        if unit == "×" || unit.hasPrefix("%") { return text + unit }
        return text + " " + unit
    }
}

/// A table in a calculator's answer (the tubes of a serial dilution, the terms of a chi-square).
struct BioLabTable: Equatable {
    let columns: [String]
    let rows: [[String]]
}

/// A point of a calculator's chart.
struct BioLabPoint: Equatable, Identifiable {
    let x: Double
    let y: Double
    var id: Double { x }
}

/// A line on a calculator's chart.
struct BioLabSeries: Equatable, Identifiable {
    let name: String
    let points: [BioLabPoint]
    var dashed = false
    var id: String { name }
}

/// A group's statistics, for the error-bar plot.
struct BioLabSummary: Equatable {
    let name: String
    let values: [Double]
    let mean: Double
    let median: Double
    let modes: [Double]
    let range: Double
    let sd: Double
    let se: Double
    var low: Double { mean - 2 * se }
    var high: Double { mean + 2 * se }
}

/// A serial dilution's tube.
struct BioLabTube: Equatable {
    let label: String
    let factor: String
    let concentration: String
    /// How far along the dilution it is, 0 (the stock) to 1 (the last), for its shading.
    let depth: Double
}

/// A calculator's working: the answers, the formula solved for the unknown, the numbers substituted, the steps, notes,
/// a table or a chart if it has one, and how it was rounded.
struct BioLabResult {
    var answers: [BioLabAnswer]
    var formula: String
    var working: String
    var steps: [String]
    var notes: [String] = []
    var table: BioLabTable? = nil
    var series: [BioLabSeries] = []
    var marks: [BioLabPoint] = []
    var summaries: [BioLabSummary] = []
    var tubes: [BioLabTube] = []
    /// A scale bar: what it stands for, and its length on the image in mm.
    var scaleBar: (label: String, millimetres: Double)? = nil
    var precision = ""
    /// A table's row to light (the chi-square's df).
    var highlight: Int? = nil
}

enum BioLabOutcome {
    case result(BioLabResult)
    case problem(BioLabProblem)

    var result: BioLabResult? { if case .result(let r) = self { return r } else { return nil } }
    var problem: BioLabProblem? { if case .problem(let p) = self { return p } else { return nil } }
}

/// A calculator: a card on one of the tool's pages.
struct BioLabCalculator: Identifiable {
    let id: String
    let page: String
    let title: String
    /// What it is for, in a line.
    let line: String
    /// The formula as the AP sheet writes it.
    let formula: String
    /// The modes picker's words ("Solve for", "Shape").
    var modeLabel = "Solve for"
    let fields: [BioLabField]
    let modes: [BioLabMode]
    let compute: (BioLabValues) throws -> BioLabResult

    func field(_ id: String) -> BioLabField? { fields.first { $0.id == id } }
    func mode(_ id: String?) -> BioLabMode { modes.first { $0.id == id } ?? modes[0] }

    /// The fields a mode shows, in the calculator's order.
    func fields(in mode: BioLabMode) -> [BioLabField] {
        fields.filter { mode.inputs.contains($0.id) || mode.unknown == $0.id }
    }

    /// Its fields as they start (the specification's test vector, where it has one).
    var defaultEntry: BioLabEntry {
        var units: [String: String] = [:]
        for f in fields { if let u = f.initialUnit { units[f.id] = u.symbol } }
        return BioLabEntry(mode: modes[0].id, texts: Dictionary(uniqueKeysWithValues: fields.map { ($0.id, $0.initial) }), units: units)
    }

    /// The working for what was typed, rounded to `digits` significant figures at most (the setting, 2–6).
    func run(_ entry: BioLabEntry, digits: Int = 3) -> BioLabOutcome {
        let v = BioLabValues(calc: self, entry: entry, setting: digits)
        do {
            var r = try compute(v)
            if r.precision.isEmpty { r.precision = v.precisionNote }
            return .result(r)
        } catch let p as BioLabProblem {
            return .problem(p)
        } catch {
            return .problem(BioLabProblem(nil, "Something went wrong: \(error)"))
        }
    }
}

/// What a calculator reads its fields through: numbers in base units, units, lists and choices — noting, as it goes,
/// the least precise measurement, which sets how far the answer is rounded (spec §5.5: never more than the least
/// precise input plus one significant figure).
final class BioLabValues {
    let calc: BioLabCalculator
    let mode: BioLabMode
    let entry: BioLabEntry
    /// The setting: 2–6 significant figures.
    let setting: Int
    private(set) var least: Int?
    private(set) var leastShown: String?

    init(calc: BioLabCalculator, entry: BioLabEntry, setting: Int) {
        self.calc = calc
        self.entry = entry
        self.setting = min(max(setting, 2), 6)
        mode = calc.mode(entry.mode)
    }

    /// Significant figures for the answers: the setting, but never more than the least precise measurement plus one.
    var digits: Int { min(setting, (least ?? setting) + 1) }

    /// How the answers were rounded, in a sentence.
    var precisionNote: String {
        guard let least, let shown = leastShown else { return "Rounded to \(setting) significant figures (the setting; every input is exact)." }
        if least + 1 < setting {
            return "Rounded to \(digits) significant figures: the least precise measurement, \(shown), has \(least), and an answer keeps at most one more."
        }
        return "Rounded to \(digits) significant figures (the setting)."
    }

    private func field(_ id: String) -> BioLabField {
        calc.field(id) ?? BioLabField(id, id, id)
    }

    /// Its symbol, for messages.
    func symbol(_ id: String) -> String { field(id).symbol }

    func text(_ id: String) -> String { (entry.texts[id] ?? field(id).initial).trimmingCharacters(in: .whitespacesAndNewlines) }

    /// The unit chosen in a field's menu (or its first).
    func unit(_ id: String) -> BioLabUnit? {
        let f = field(id)
        guard let q = f.quantity else { return nil }
        return q.unit(entry.units[id]) ?? f.initialUnit
    }

    /// A field's unit as written ("mL", "g/mol", "").
    func unitText(_ id: String) -> String { unit(id)?.symbol ?? field(id).unit ?? "" }

    private func note(_ id: String, _ n: BioLabNumber) {
        guard !field(id).exact, let s = n.sigFigs else { return }
        if least == nil || s < least! {
            least = s
            leastShown = shown(id)
        }
    }

    /// The number typed, as typed (not converted).
    func typed(_ id: String) throws -> BioLabNumber {
        let t = text(id)
        guard !t.isEmpty else { throw BioLabProblem(id, "Type a number for \(symbol(id)).") }
        guard let n = BioLabParse.number(t) else { throw BioLabProblem(id, "“\(t)” is not a number. Try 0.5, 250 or 1.5 × 10^9.") }
        return n
    }

    /// A field's number in its kind's base unit (litres, grams, metres, seconds, molar), noted for precision.
    func value(_ id: String) throws -> Double {
        let n = try typed(id)
        note(id, n)
        return n.value * (unit(id)?.factor ?? 1)
    }

    /// The same, and more than zero.
    func positive(_ id: String, _ message: String? = nil) throws -> Double {
        let x = try value(id)
        guard x > 0 else { throw BioLabProblem(id, message ?? "\(symbol(id)) must be more than 0.") }
        return x
    }

    /// The same, and zero or more.
    func nonNegative(_ id: String) throws -> Double {
        let x = try value(id)
        guard x >= 0 else { throw BioLabProblem(id, "\(symbol(id)) can't be negative.") }
        return x
    }

    /// A field that may be left empty.
    func optionalValue(_ id: String) throws -> Double? {
        text(id).isEmpty ? nil : try value(id)
    }

    /// A whole number (a count of steps, cycles).
    func whole(_ id: String, _ range: ClosedRange<Int>) throws -> Int {
        let x = try value(id)
        guard x == x.rounded(), x >= Double(range.lowerBound), x <= Double(range.upperBound) else {
            throw BioLabProblem(id, "\(symbol(id)) must be a whole number from \(range.lowerBound) to \(range.upperBound).")
        }
        return Int(x)
    }

    /// A list of numbers (at least `min` of them), noted for precision.
    func list(_ id: String, min: Int = 1) throws -> [Double] {
        let (ns, bad) = BioLabParse.list(text(id))
        if !bad.isEmpty { throw BioLabProblem(id, "Not numbers: \(bad.prefix(4).map { "“\($0)”" }.joined(separator: ", ")).") }
        guard ns.count >= min else {
            throw BioLabProblem(id, min == 1 ? "Type at least one number for \(symbol(id))." : "Type at least \(min) numbers for \(symbol(id)), separated by commas.")
        }
        if !field(id).exact, let s = ns.compactMap(\.sigFigs).min() {
            if least == nil || s < least! {
                least = s
                leastShown = "\(BioLabFormat.plain(ns.first { $0.sigFigs == s }?.value ?? 0)) in \(symbol(id))"
            }
        }
        return ns.map(\.value)
    }

    /// The most decimal places among a list's numbers.
    func listDecimals(_ id: String) -> Int {
        text(id).components(separatedBy: CharacterSet(charactersIn: ",;:\n\t ")).filter { !$0.isEmpty }.map(BioLabParse.decimals).max() ?? 0
    }

    func names(_ id: String) -> [String] { BioLabParse.names(text(id)) }

    func choice(_ id: String) -> String {
        let f = field(id)
        let t = text(id)
        return f.choices.contains { $0.id == t } ? t : (f.choices.first?.id ?? t)
    }

    /// A field as typed with its unit, tidied ("50 mL", "1 × 10⁻⁷ M", "400×").
    func shown(_ id: String) -> String {
        let t = text(id)
        let u = unitText(id)
        let number: String
        if let n = BioLabParse.number(t) {
            number = n.sigFigs.map { BioLabFormat.sig(n.value, $0) } ?? BioLabFormat.plain(n.value)
        } else {
            number = t
        }
        if u.isEmpty { return number }
        return u == "×" ? number + "×" : number + " " + u
    }

    /// A base-unit value written in a unit ("0.25 L").
    func written(_ base: Double, in unit: BioLabUnit?) -> String {
        guard let unit else { return BioLabFormat.work(base) }
        return BioLabFormat.work(base / unit.factor) + " " + unit.symbol
    }

    /// A field as typed if it is already in `target`, else converted to it ("5.0 mL", or "0.005 L").
    func shown(_ id: String, in target: BioLabUnit?) -> String {
        guard let target, let u = unit(id), u.symbol != target.symbol, let base = try? value(id) else { return shown(id) }
        return written(base, in: target)
    }

    /// The conversion of a field to another unit of its kind, as a step ("250 mL = 0.25 L"), nil if it is already in it.
    func conversion(_ id: String, to target: BioLabUnit?) -> String? {
        guard let from = unit(id), let target, from.symbol != target.symbol, let base = try? value(id) else { return nil }
        return "\(shown(id)) = \(written(base, in: target))"
    }

    /// The unit chosen for the unknown's answer (or its first).
    func out(_ id: String) -> BioLabUnit? { unit(id) }

    /// The answer for an unknown field: a base-unit value in the unit its menu has, rounded, with the same in a handier
    /// unit of its kind if that reads better ("8.0 × 10⁻⁵ M = 80 µM"), or in the unit asked for (`alsoIn`).
    func answer(_ id: String, base: Double, digits d: Int? = nil, label: String? = nil, unitText fixed: String? = nil,
                alsoIn: String? = nil) -> BioLabAnswer {
        let f = field(id)
        let dg = d ?? digits
        guard let u = out(id) else {
            return BioLabAnswer(symbol: f.symbol, label: label ?? f.label, value: base, unit: fixed ?? f.unit ?? "", text: BioLabFormat.sig(base, dg), field: id)
        }
        let v = base / u.factor
        var alt: String?
        let a = abs(v)
        let others = f.units.filter { $0.group == u.group && $0.symbol != u.symbol }
        if let alsoIn, let other = others.first(where: { $0.symbol == alsoIn }) {
            alt = "= " + BioLabFormat.sig(base / other.factor, dg) + " " + other.symbol
        } else if a < 0.1 || a >= 10_000, let better = others.first(where: { (1..<1000).contains(abs(base / $0.factor)) })
                    ?? others.first(where: { (0.1..<10_000).contains(abs(base / $0.factor)) }) {
            alt = "= " + BioLabFormat.sig(base / better.factor, dg) + " " + better.symbol
        }
        return BioLabAnswer(symbol: f.symbol, label: label ?? f.label, value: v, unit: fixed ?? (f.per ? "per \(u.symbol)" : u.symbol),
                            text: BioLabFormat.sig(v, dg), alt: alt, field: id)
    }
}

// MARK: - Self-check (spec §10.1 item 9: every row of §5.5)

extension BioSelfCheck {
    /// A calculator run on these texts (the rest as it starts), in a mode.
    private static func lab(_ id: String, _ mode: String? = nil, _ texts: [String: String] = [:], units: [String: String] = [:],
                            digits: Int = 3) -> BioLabResult? {
        guard let c = BioLab.calculator(id) else { return nil }
        var e = c.defaultEntry
        if let mode { e.mode = mode }
        for (k, t) in texts { e.texts[k] = t }
        for (k, u) in units { e.units[k] = u }
        return c.run(e, digits: digits).result
    }

    /// Bio Lab Calculator's own `bio-check` lines: the parsing and rounding, then §5.5's test vectors row by row.
    static func biolabChecks(_ r: inout BioCheckReport) {
        // (every calculator, in every mode, works its starting example out; its fields exist; its page is one of the six)
        let pages = Set(BioLab.pages.map(\.id))
        var broken: [String] = []
        for c in BioLab.all {
            if !pages.contains(c.page) { broken.append("\(c.id):page") }
            for m in c.modes {
                if (m.inputs + [m.unknown].compactMap { $0 }).contains(where: { c.field($0) == nil }) { broken.append("\(c.id).\(m.id):field") }
                var e = c.defaultEntry
                e.mode = m.id
                if let p = c.run(e).problem { broken.append("\(c.id).\(m.id):\(p.message)") }
            }
        }
        r.check("biolab.calculators", broken.isEmpty ? "\(BioLab.all.count)" : broken.prefix(3).joined(separator: ","), broken.isEmpty && BioLab.all.count == 26)

        // (reading: powers of ten, ratios, superscripts and significant figures as written)
        let parsed = ["1.5 × 10^9", "1.5x10^9", "10⁻⁶", "1:10", "3,000", "−2.4", "1e-7"].map { BioLabParse.number($0)?.value ?? .nan }
        r.check("biolab.parse", parsed.map { BioLabFormat.plain($0) }.joined(separator: ","),
                parsed == [1.5e9, 1.5e9, 1e-6, 0.1, 3000, -2.4, 1e-7] && BioLabParse.number("abc") == nil && BioLabParse.number("0x10") == nil)
        let sf = ["250", "250.", "0.0500", "1.0", "58.44", "1e-7"].map { BioLabParse.number($0)?.sigFigs ?? 0 }
        r.check("biolab.sigfigs", sf.map(String.init).joined(separator: ","), sf == [2, 3, 3, 2, 4, 1] && BioLabParse.number("10^-6")?.sigFigs == nil)
        // (rounding on the decimal digits: 7.305 is 7.30499… in binary, and must still show as 7.31)
        let shown = [BioLabFormat.sig(7.305, 3), BioLabFormat.sig(0.125 * 58.44, 3), BioLabFormat.sig(1.073741824e11, 3), BioLabFormat.sig(8.0386e-5, 3),
                     BioLabFormat.sig(1006.2, 4), BioLabFormat.sig(0.45, 3), BioLabFormat.sig(9.996, 3), BioLabFormat.sig(-2.45145, 3),
                     BioLabFormat.fixed(12.6447, decimals: 2), BioLabFormat.sig(840_000, 2), BioLabFormat.dilution(pow(0.1, 4))]
        r.same("biolab.format", shown.joined(separator: "|"), "7.31|7.31|1.07 × 10¹¹|8.04 × 10⁻⁵|1,006|0.450|10.0|−2.45|12.64|8.4 × 10⁵|10⁻⁴")
        r.check("biolab.prefixes", BioLab.prefixes.map { "\($0.symbol)\($0.exponent)" }.joined(separator: ","),
                BioLab.prefixes.map(\.exponent) == [9, 6, 3, -1, -2, -3, -6, -9, -12] && BioLab.prefixes.map(\.symbol).joined() == "GMkdcmµnp")
        let masses = ["NaCl", "C6H12O6", "C12H22O11"].map { BioLabFormat.fixed(BioLab.molarMass($0) ?? 0, decimals: 2) }
        r.same("biolab.molar-masses", masses.joined(separator: ","), "58.44,180.16,342.30")

        // §5.5, row by row
        r.near("biolab.molarity", lab("molarity")?.answers.first?.value ?? 0, 7.305, 0.0005, digits: 4)
        r.same("biolab.molarity-shown", lab("molarity", nil, ["M": "0.500", "V": "250."])?.answers.first?.full ?? "-", "7.31 g")
        let dil = lab("dilution")
        r.same("biolab.dilution", (dil?.answers.map(\.full) ?? []).joined(separator: "+"), "5.0 mL+45 mL")
        r.near("biolab.serial", lab("serial")?.answers[1].value ?? 0, 1e-4, 1e-12, digits: 8)
        r.same("biolab.plate", lab("plate")?.answers.first?.full ?? "-", "1.5 × 10⁹ CFU/mL")
        r.near("biolab.percent", lab("percent")?.answers.first?.value ?? 0, 2, 1e-9, digits: 2)
        r.same("biolab.magnification-total", lab("totalMag")?.answers.first?.full ?? "-", "400×")
        let fov = lab("fov")?.answers.first
        r.check("biolab.field-of-view", "\(fov?.full ?? "-")\(fov?.alt ?? "")", abs((fov?.value ?? 0) - 0.45) < 1e-9 && fov?.alt == "= 450 µm")
        r.near("biolab.specimen", lab("specimen")?.answers.first?.value ?? 0, 90, 1e-9, digits: 2)
        r.near("biolab.magnification", lab("magnification")?.answers.first?.value ?? 0, 3000, 1e-6, digits: 2)
        let sphere = lab("sav", "sphere")?.answers.first, cube = lab("sav", "cube")?.answers.first
        r.check("biolab.sa-v", "\(sphere?.full ?? "-"),\(cube?.full ?? "-")", sphere?.text == "3.0" && cube?.text == "3.0"
                && abs((lab("sav", "cylinder", ["r": "1", "h": "2"])?.answers.first?.value ?? 0) - 3) < 1e-9
                && abs((lab("sav", "rect")?.answers.first?.value ?? 0) - 22.0 / 6.0) < 1e-9)
        r.near("biolab.water-sucrose", lab("water")?.answers.first?.value ?? 0, -2.45, 0.005, digits: 4)
        r.near("biolab.water-NaCl", lab("water", nil, ["i": "2"])?.answers.first?.value ?? 0, -4.90, 0.005, digits: 4)
        r.near("biolab.hemocytometer", lab("hemocytometer")?.answers.first?.value ?? 0, 8.4e5, 1e-6, digits: 0)
        let stats = lab("stats", nil, ["b": ""])
        r.check("biolab.mean", stats?.answers.first?.text ?? "-", stats?.answers.first?.text == "12.64")
        r.near("biolab.sd", stats?.answers[1].value ?? 0, 0.6656, 0.00005, digits: 4)
        r.near("biolab.se", stats?.answers[2].value ?? 0, 0.2977, 0.00005, digits: 4)
        r.same("biolab.error-bars", stats?.table?.rows.last?.last ?? "-", "12.04 to 13.24")
        r.near("biolab.rate", lab("rate")?.answers.first?.value ?? 0, 0.22, 1e-9, digits: 3)
        r.near("biolab.growth-exponential", lab("growth", "exponential")?.answers.first?.value ?? 0, 50, 1e-9, digits: 1)
        r.near("biolab.growth-logistic", lab("growth", "logistic")?.answers.first?.value ?? 0, 45, 1e-9, digits: 1)
        r.near("biolab.simpson", lab("simpson")?.answers.first?.value ?? 0, 0.62, 1e-9, digits: 3)
        let beer = lab("beer", nil, ["A": "0.500"])?.answers.first
        r.check("biolab.beer-lambert", "\(beer?.full ?? "-")\(beer?.alt ?? "")", abs((beer?.value ?? 0) - 8.04e-5) < 0.005e-5 && beer?.alt == "= 80.4 µM")
        r.near("biolab.a260", lab("a260")?.answers.first?.value ?? 0, 125, 1e-9, digits: 1)
        r.same("biolab.pcr", lab("pcr")?.answers.first?.full ?? "-", "1.07 × 10¹¹ copies")
        r.near("biolab.centrifuge", lab("centrifuge")?.answers.first?.value ?? 0, 1006, 0.5, digits: 1)
        r.near("biolab.bacteria", lab("bacteria")?.answers.first?.value ?? 0, 6400, 1e-6, digits: 0)
        r.same("biolab.pH", lab("ph")?.answers.first?.text ?? "-", "7.00")
        r.near("biolab.q10", lab("q10")?.answers.first?.value ?? 0, 2.2, 1e-9, digits: 3)
        // (Mendel's dihybrid peas against 9:3:3:1: χ² = 0.470 with df 3, so we fail to reject the null hypothesis)
        let chi = lab("chisq")
        r.check("biolab.chi-square", chi?.answers.first?.text ?? "-", abs((chi?.answers.first?.value ?? 0) - 0.470) < 0.0005
                && chi?.answers[1].text == "3" && chi?.notes.first?.contains("fail to reject") == true)
    }
}

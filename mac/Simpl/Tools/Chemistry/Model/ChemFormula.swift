import Foundation

// (tools) What the chemistry calculators share and need no window for — Foundation only, so the Models job checks it
// (scripts/dev/swift-checks/main.swift): a chemical formula read into its elements and their counts (parentheses,
// hydrates, charges, phase labels), each element's atomic mass from the Periodic Table's own data, a formula's molar
// mass with its breakdown, numbers written with sensible significant figures and no float noise, and the launch flags'
// "key=value,key=value" lists. The Equation Balancer (ChemBalance.swift), Molar Mass & Stoichiometry, Gas Laws and
// Solutions & pH all read formulas and masses through here.

/// Each element's atomic mass by its symbol, from the Periodic Table's table (PeriodicData.json: the symbol in column
/// 1, the mass in column 3 — the IUPAC 2021 weights, as the table shows them).
enum ChemElements {
    static let masses: [String: Double] = {
        var out: [String: Double] = [:]
        guard let rows = (try? JSONSerialization.jsonObject(with: Data(PeriodicData.json.utf8))) as? [[Any]] else { return out }
        for r in rows where r.count > 3 {
            if let s = r[1] as? String, let m = r[3] as? Double { out[s] = m }
        }
        return out
    }()

    static func mass(_ symbol: String) -> Double? { masses[symbol] }

    /// Whether `s` names an element: a capital and up to two small letters that the table has.
    static func isElement(_ s: String) -> Bool { masses[s] != nil }
}

/// Why a formula could not be read: nothing typed, or a token that is not a formula ("XyZ", "Xx2", "H0").
enum ChemFormulaError: Error, Equatable {
    case empty
    case unreadable(String)

    /// The plain line the tools show.
    var message: String {
        switch self {
        case .empty: return "Type a formula."
        case .unreadable(let token): return "Can't read `\(token)` — is it a real formula?"
        }
    }
}

/// A chemical formula read: "Ca(OH)2", "CuSO4·5H2O", "Fe^3+", "SO4^2-", "Na+(aq)" — its elements with their counts,
/// its charge, and how it is written with subscripts and a superscript charge ("Ca(OH)₂", "Fe³⁺").
struct ChemFormula: Equatable {
    /// As given, trimmed, without a phase label.
    let text: String
    /// The formula without its charge ("Fe" of "Fe^3+").
    let body: String
    /// Each element's count of atoms.
    let counts: [String: Int]
    /// The elements in the order they first appear.
    let elements: [String]
    /// The ion's charge (0 for a molecule); an electron, "e-", is −1 with no atoms.
    let charge: Int

    /// Every atom in it.
    var atoms: Int { counts.values.reduce(0, +) }

    /// The formula typeset: counts as subscripts, the charge superscript ("Ca(OH)₂", "CuSO₄·5H₂O", "SO₄²⁻", "e⁻").
    var pretty: String {
        var out = ""
        var prev: Character = " "
        // (a count's digits go under: those after an element or a group's close, and the digits that go on from
        // them — "C12H22O11"; a hydrate's multiplier after its dot stays on the line, "CuSO₄·5H₂O")
        var under = false
        for ch in body {
            if let d = ch.wholeNumberValue, ch.isASCII, prev.isLetter || prev == ")" || prev == "]" || under {
                out.append(Self.subscripts[d])
                under = true
            } else {
                out.append(ch)
                under = false
            }
            prev = ch
        }
        if charge != 0 {
            let n = abs(charge)
            if n > 1 { out += String(n).map { Self.superscripts[$0.wholeNumberValue ?? 0] } }
            out += charge > 0 ? "⁺" : "⁻"
        }
        return out
    }

    /// The charge as plain text: "3+", "-", "" for none.
    var chargeText: String {
        guard charge != 0 else { return "" }
        return (abs(charge) > 1 ? String(abs(charge)) : "") + (charge > 0 ? "+" : "-")
    }

    static let subscripts: [Character] = Array("₀₁₂₃₄₅₆₇₈₉")
    static let superscripts: [Character] = Array("⁰¹²³⁴⁵⁶⁷⁸⁹")

    // MARK: Reading

    /// The formula in `raw`: counts may be typed as digits or subscripts, a charge as `^3+`, `3+`, `+`, `²⁺` or `⁻`,
    /// a hydrate with `·`, `•`, `*` or `.`, and a phase label `(s)`, `(l)`, `(g)` or `(aq)` at the end is passed over.
    static func parse(_ raw: String) -> Result<ChemFormula, ChemFormulaError> {
        var s = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !s.isEmpty else { return .failure(.empty) }
        s = stripPhase(s)
        s = normalised(s)
        s = s.filter { !$0.isWhitespace }
        guard !s.isEmpty else { return .failure(.empty) }
        let token = raw.trimmingCharacters(in: .whitespacesAndNewlines)

        // (the charge: after a caret, or a sign at the very end with its number before it)
        var body = s
        var charge = 0
        if let caret = s.firstIndex(of: "^") {
            body = String(s[..<caret])
            guard let q = readCharge(String(s[s.index(after: caret)...])) else { return .failure(.unreadable(token)) }
            charge = q
        } else if let last = s.last, last == "+" || last == "-" {
            var i = s.index(before: s.endIndex)
            while i > s.startIndex, s[s.index(before: i)].isNumber { i = s.index(before: i) }
            // (the digits before the sign are the charge's when a bare element is before them — "Fe3+", "Cu2+" — and
            // a count when more is, "MnO4-", "NH4+": the ion's charge is then one; "SO4^2-" says its charge with ^)
            let stem = String(s[..<i])
            if i < s.index(before: s.endIndex), !ChemElements.isElement(stem) {
                i = s.index(before: s.endIndex)
            }
            guard let q = readCharge(String(s[i...])) else { return .failure(.unreadable(token)) }
            body = String(s[..<i])
            charge = q
        }
        if body == "e" || body == "e⁻" {
            guard charge == -1 else { return .failure(.unreadable(token)) }
            return .success(ChemFormula(text: s, body: body, counts: [:], elements: [], charge: -1))
        }
        guard !body.isEmpty, body.first?.isNumber != true else { return .failure(.unreadable(token)) }

        // (a hydrate: each part after a dot, with its own multiplier, added on)
        var counts: [String: Int] = [:]
        var order: [String] = []
        let parts = body.split(whereSeparator: { "·•*.".contains($0) }).map(String.init)
        guard !parts.isEmpty, body.first.map({ !"·•*.".contains($0) }) == true else { return .failure(.unreadable(token)) }
        for (k, part) in parts.enumerated() {
            var chars = Array(part)
            var times = 1
            if k > 0 {
                var digits = ""
                while let c = chars.first, c.isNumber { digits.append(c); chars.removeFirst() }
                if !digits.isEmpty {
                    guard let n = Int(digits), n > 0 else { return .failure(.unreadable(token)) }
                    times = n
                }
            }
            var i = 0
            guard let group = readGroup(chars, &i, close: nil), i == chars.count else { return .failure(.unreadable(token)) }
            // (the order of first appearance, as the part reads)
            for sym in group.order {
                if counts[sym] == nil { order.append(sym) }
                counts[sym, default: 0] += (group.counts[sym] ?? 0) * times
            }
        }
        guard !counts.isEmpty else { return .failure(.unreadable(token)) }
        return .success(ChemFormula(text: s, body: body, counts: counts, elements: order, charge: charge))
    }

    /// A formula's molar mass in g/mol (every element in it is one the table has, so it always has one).
    var molarMass: Double {
        counts.reduce(0.0) { $0 + Double($1.value) * (ChemElements.mass($1.key) ?? 0) }
    }

    /// The elements of a group: between `(`/`[` and its close, or the whole string when `close` is nil.
    private static func readGroup(_ chars: [Character], _ i: inout Int, close: Character?) -> (counts: [String: Int], order: [String])? {
        var counts: [String: Int] = [:]
        var order: [String] = []
        func add(_ sym: String, _ n: Int) {
            if counts[sym] == nil { order.append(sym) }
            counts[sym, default: 0] += n
        }
        while i < chars.count {
            let c = chars[i]
            if let close, c == close {
                i += 1
                return (counts, order)
            }
            if c == "(" || c == "[" {
                i += 1
                guard let inner = readGroup(chars, &i, close: c == "(" ? ")" : "]") else { return nil }
                let n = readCount(chars, &i)
                guard n > 0 else { return nil }
                for sym in inner.order { add(sym, (inner.counts[sym] ?? 0) * n) }
                continue
            }
            guard c.isLetter, c.isUppercase, c.isASCII else { return nil }
            var sym = String(c)
            i += 1
            while i < chars.count, chars[i].isLetter, chars[i].isLowercase, chars[i].isASCII, sym.count < 3 { sym.append(chars[i]); i += 1 }
            guard ChemElements.isElement(sym) else { return nil }
            let n = readCount(chars, &i)
            guard n > 0 else { return nil }
            add(sym, n)
        }
        return close == nil ? (counts, order) : nil
    }

    /// The count after an element or a group: the digits there, 1 for none, 0 for a "0" (not a count).
    private static func readCount(_ chars: [Character], _ i: inout Int) -> Int {
        var digits = ""
        while i < chars.count, chars[i].isNumber, chars[i].isASCII { digits.append(chars[i]); i += 1 }
        if digits.isEmpty { return 1 }
        return Int(digits) ?? 0
    }

    /// A charge's text: "3+", "+", "2-", "-", or "+3"/"-2" (as some type them).
    private static func readCharge(_ s: String) -> Int? {
        guard !s.isEmpty else { return nil }
        var t = s
        var sign = 0
        if let last = t.last, last == "+" || last == "-" {
            sign = last == "+" ? 1 : -1
            t.removeLast()
        } else if let first = t.first, first == "+" || first == "-" {
            sign = first == "+" ? 1 : -1
            t.removeFirst()
        }
        guard sign != 0 else { return nil }
        if t.isEmpty { return sign }
        guard let n = Int(t), n > 0, n < 100 else { return nil }
        return sign * n
    }

    /// A phase label at the end, "(s)", "(l)", "(g)" or "(aq)", taken off.
    static func stripPhase(_ s: String) -> String {
        var t = s.trimmingCharacters(in: .whitespaces)
        for label in ["(s)", "(l)", "(g)", "(aq)"] where t.lowercased().hasSuffix(label) {
            t = String(t.dropLast(label.count)).trimmingCharacters(in: .whitespaces)
            break
        }
        return t
    }

    /// Subscript digits as plain digits, superscript charges as "^…": "H₂O" → "H2O", "Fe³⁺" → "Fe^3+", "SO₄²⁻" → "SO4^2-".
    static func normalised(_ s: String) -> String {
        var out = ""
        var inSuper = false
        for ch in s {
            if let i = subscripts.firstIndex(of: ch) {
                out.append(Character(String(i)))
                inSuper = false
            } else if let i = superscripts.firstIndex(of: ch) {
                if !inSuper { out.append("^"); inSuper = true }
                out.append(Character(String(i)))
            } else if ch == "⁺" || ch == "⁻" {
                if !inSuper { out.append("^") }
                out.append(ch == "⁺" ? "+" : "-")
                inSuper = false
            } else if ch == "−" || ch == "–" {
                out.append("-")
                inSuper = false
            } else {
                out.append(ch)
                inSuper = false
            }
        }
        return out
    }
}

/// A formula's molar mass with its breakdown: each element, how many atoms, its atomic mass, their subtotal and its
/// share of the whole (the percent composition).
struct ChemMolarRow: Equatable {
    let symbol: String
    let count: Int
    let atomicMass: Double
    let subtotal: Double
    let percent: Double
}

enum ChemMolar {
    /// The rows in order of first appearance, and the total in g/mol.
    static func breakdown(_ f: ChemFormula) -> (rows: [ChemMolarRow], total: Double) {
        let total = f.molarMass
        let rows = f.elements.map { sym -> ChemMolarRow in
            let n = f.counts[sym] ?? 0
            let m = ChemElements.mass(sym) ?? 0
            let sub = Double(n) * m
            return ChemMolarRow(symbol: sym, count: n, atomicMass: m, subtotal: sub, percent: total > 0 ? sub / total * 100 : 0)
        }
        return (rows, total)
    }
}

// MARK: - Numbers as the chemistry tools show them

/// Numbers written for a chemistry answer: to a few significant figures, rounded half up on the decimal value (the
/// house rule, Double.bioHalfUp), trailing zeros dropped, never with float noise, and very large or small ones as
/// "3.343e23" — which `pretty` writes "3.343 × 10²³".
enum ChemNumber {
    /// `v` to `figures` significant figures (1 to 15): 18.015 → "18.02", 0.55506 → "0.5551", 1.0 → "1",
    /// 3.3428e23 → "3.343e23", 1e-11 → "1e-11".
    static func sig(_ v: Double, _ figures: Int = 4) -> String {
        guard v.isFinite else { return "—" }
        guard v != 0 else { return "0" }
        let n = max(1, min(figures, 15))
        let a = abs(v)
        var e = Int(log10(a).rounded(.down))
        var m = a / pow(10, Double(e))
        if m >= 10 { m /= 10; e += 1 } else if m < 1 { m *= 10; e -= 1 }
        var mantissa = m.bioHalfUp(n - 1)
        if mantissa >= 10 { mantissa /= 10; e += 1 }
        let sign = v < 0 ? "-" : ""
        if e >= 7 || e <= -5 {
            return sign + trimmed(String(format: "%.\(n - 1)f", mantissa)) + "e\(e)"
        }
        // (the value back from its rounded mantissa, printed at the decimals its figures reach past the point)
        let places = max(0, n - 1 - e)
        let value = mantissa * pow(10, Double(e))
        return sign + trimmed(String(format: "%.\(places)f", value.bioHalfUp(places)))
    }

    /// `v` at `places` decimals, rounded half up: "18.015", "2.88", "7.00" (trailing zeros kept — a pH is 7.00).
    static func fixed(_ v: Double, _ places: Int) -> String {
        guard v.isFinite else { return "—" }
        let s = String(halfUp: "%.\(max(0, places))f", v)
        // (a "-0.00" is 0.00)
        return s.hasPrefix("-") && Double(s) == 0 ? String(s.dropFirst()) : s
    }

    /// "3.343e23" written "3.343 × 10²³"; a plain number as it is.
    static func pretty(_ s: String) -> String {
        guard let e = s.firstIndex(where: { $0 == "e" || $0 == "E" }) else { return s }
        let mantissa = String(s[..<e]), exp = String(s[s.index(after: e)...])
        guard let k = Int(exp) else { return s }
        let digits = String(abs(k)).map { ChemFormula.superscripts[$0.wholeNumberValue ?? 0] }
        return "\(mantissa == "1" ? "" : "\(mantissa) × ")10\(k < 0 ? "⁻" : "")\(String(digits))"
    }

    /// A number typed: commas, a Unicode minus, "×10^" or "x10^" for a power of ten ("1.8 × 10^-5", "1.8e-5").
    static func number(_ s: String) -> Double? {
        var t = s.replacingOccurrences(of: ",", with: "").replacingOccurrences(of: "−", with: "-").replacingOccurrences(of: "–", with: "-")
            .trimmingCharacters(in: .whitespaces)
        for form in ["×10^", "x10^", "X10^", "*10^", "× 10^", "x 10^", "* 10^", "×10", "x10"] {
            if let r = t.range(of: form) { t.replaceSubrange(r, with: "e"); break }
        }
        t = t.filter { !$0.isWhitespace }
        guard let v = Double(t), v.isFinite else { return nil }
        return v
    }

    /// Trailing zeros after the point gone, and the point with them: "18.020" → "18.02", "1.000" → "1".
    static func trimmed(_ s: String) -> String {
        guard s.contains(".") else { return s }
        var t = s
        while t.hasSuffix("0") { t.removeLast() }
        if t.hasSuffix(".") { t.removeLast() }
        return t == "-0" ? "0" : t
    }
}

/// The screenshot suite's "key=value,key=value" flags (-SimplGasValues "P=1 atm,V=22.4 L"), and a value with a unit.
enum ChemLaunchValues {
    static func parse(_ s: String?) -> [String: String] {
        var out: [String: String] = [:]
        for part in (s ?? "").split(separator: ",") {
            let kv = part.split(separator: "=", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) }
            guard kv.count == 2, !kv[0].isEmpty else { continue }
            out[kv[0]] = kv[1]
        }
        return out
    }

    /// "22.4 L" as its number and its unit ("22.4", "L"); "22.4" alone has no unit.
    static func split(_ s: String) -> (number: String, unit: String?) {
        let t = s.trimmingCharacters(in: .whitespaces)
        guard let space = t.firstIndex(where: { $0.isWhitespace }) else { return (t, nil) }
        return (String(t[..<space]), String(t[t.index(after: space)...]).trimmingCharacters(in: .whitespaces))
    }
}

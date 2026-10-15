import Foundation

// (tools) What the study and maths tools share of their arithmetic — the Word Counter's, Statistics' and the Matrix
// Calculator's (Tools/Model, Foundation only: compiled and checked on their own by the Models job, as the biology and
// chemistry models are): the numbers a student types read back, and every number shown written to sensible figures —
// as many as the number has to six, rounded half up on its decimal digits (the house rule, Double.bioHalfUp), never
// the binary value's noise (0.30000000000000004), a minus as "−".

enum StudyMath {
    /// A number typed: commas and spaces between digits passed over, "−" read as a minus, a percent sign dropped.
    static func number(_ s: String) -> Double? {
        var t = s.trimmingCharacters(in: .whitespacesAndNewlines)
        t = t.replacingOccurrences(of: ",", with: "").replacingOccurrences(of: " ", with: "")
        t = t.replacingOccurrences(of: "−", with: "-").replacingOccurrences(of: "–", with: "-")
        if t.hasSuffix("%") { t.removeLast() }
        guard !t.isEmpty, let v = Double(t), v.isFinite else { return nil }
        return v
    }

    /// Every number in a text, in order: separated by spaces, commas, semicolons, tabs or new lines (a column pasted
    /// from a spreadsheet, or a row of it). What is not a number is passed over.
    static func numbers(_ text: String) -> [Double] {
        let seps = CharacterSet(charactersIn: " ,;\t\r\n")
        return text.components(separatedBy: seps).compactMap { number($0) }
    }

    /// A number shown: to `sig` significant figures at most, trailing zeros dropped ("2.138", "5", "0.7746"), rounded
    /// half up on its decimal digits; very large and very small ones in scientific form ("3.343e23").
    static func text(_ v: Double, sig: Int = 6) -> String {
        guard v.isFinite else { return v.isNaN ? "—" : (v > 0 ? "∞" : "−∞") }
        if v == 0 { return "0" }
        let a = abs(v)
        if a >= 1e12 || a < 1e-5 {
            let s = String(format: "%.\(max(sig - 1, 0))e", v)
            return tidyExponent(s)
        }
        let e = Int(floor(log10(a)))
        let places = max(sig - 1 - e, 0)
        let r = halfUp(v, places)
        var s = String(format: "%.\(places)f", r)
        if s.contains(".") {
            while s.hasSuffix("0") { s.removeLast() }
            if s.hasSuffix(".") { s.removeLast() }
        }
        if s == "-0" { s = "0" }
        return s.replacingOccurrences(of: "-", with: "−")
    }

    /// A number shown at a set number of decimals ("5.00", "0.050"), rounded half up.
    static func fixed(_ v: Double, _ places: Int) -> String {
        guard v.isFinite else { return "—" }
        var s = String(format: "%.\(places)f", halfUp(v, places))
        if s.hasPrefix("-"), Double(s) == 0 { s.removeFirst() }
        return s.replacingOccurrences(of: "-", with: "−")
    }

    /// `v` rounded half up on its decimal digits at `places` decimals (3.425 → 3.43, 0.125 → 0.13): the number is read
    /// to 15 significant figures first, which drops the binary value's noise.
    static func halfUp(_ v: Double, _ places: Int) -> Double {
        guard v.isFinite, v != 0, (0...15).contains(places), var d = Decimal(string: String(format: "%.14e", v)) else { return v }
        var r = Decimal()
        NSDecimalRound(&r, &d, places, .plain)
        return Double(r.description) ?? v
    }

    /// "3.343000e+23" as "3.343e23".
    private static func tidyExponent(_ s: String) -> String {
        guard let eAt = s.firstIndex(where: { $0 == "e" || $0 == "E" }) else { return s }
        var mantissa = String(s[s.startIndex..<eAt])
        var exponent = String(s[s.index(after: eAt)...])
        if mantissa.contains(".") {
            while mantissa.hasSuffix("0") { mantissa.removeLast() }
            if mantissa.hasSuffix(".") { mantissa.removeLast() }
        }
        let negative = exponent.hasPrefix("-")
        exponent = exponent.trimmingCharacters(in: CharacterSet(charactersIn: "+-"))
        while exponent.count > 1 && exponent.hasPrefix("0") { exponent.removeFirst() }
        return "\(mantissa)e\(negative ? "−" : "")\(exponent)".replacingOccurrences(of: "-", with: "−")
    }

    /// A length of time as words: "2 min 15 s", "45 s", "1 h 3 min".
    static func duration(seconds: Double) -> String {
        guard seconds.isFinite, seconds > 0 else { return "0 s" }
        let total = Int(seconds.rounded())
        if total < 60 { return "\(max(total, 1)) s" }
        let h = total / 3600, m = (total % 3600) / 60, s = total % 60
        if h > 0 { return m > 0 ? "\(h) h \(m) min" : "\(h) h" }
        return s > 0 ? "\(m) min \(s) s" : "\(m) min"
    }
}

/// (tools) What the study and maths tools keep from one opening to the next, on this Mac only: UserDefaults
/// `SimplStudy.<key>.<field>` — the Word Counter's text and target, Statistics' data and tab, the Matrix Calculator's
/// matrices and operation. A picture's launch (-SimplShotFile) keeps them in a suite of its own that starts empty, as
/// ChemPrefs and BioPrefs do, so each picture shows its tool as its launch asks and nothing more.
enum StudyPrefs {
    private static let store: UserDefaults = {
        guard UserDefaults.standard.string(forKey: "SimplShotFile") != nil else { return .standard }
        let name = "SimplStudyPicture"
        guard let d = UserDefaults(suiteName: name) else { return .standard }
        d.removePersistentDomain(forName: name)
        return d
    }()

    static func key(_ tool: String, _ field: String) -> String { "SimplStudy.\(tool).\(field)" }
    static func string(_ tool: String, _ field: String) -> String? { store.string(forKey: key(tool, field)) }
    static func int(_ tool: String, _ field: String) -> Int? { string(tool, field).flatMap { Int($0) } }

    /// Kept, only when it has changed (nil: forgotten).
    static func set(_ tool: String, _ field: String, _ value: String?) {
        guard store.string(forKey: key(tool, field)) != value else { return }
        store.set(value, forKey: key(tool, field))
    }

    /// Several fields at once, each kept only when it has changed: a view watches them as one list.
    static func remember(_ tool: String, _ fields: [String], _ values: [String]) {
        for (field, value) in zip(fields, values) { set(tool, field, value) }
    }

    static func bool(_ tool: String, _ field: String, default d: Bool) -> Bool {
        store.object(forKey: key(tool, field)) == nil ? d : store.bool(forKey: key(tool, field))
    }

    static func set(_ tool: String, _ field: String, _ value: Bool) {
        guard store.object(forKey: key(tool, field)) == nil || store.bool(forKey: key(tool, field)) != value else { return }
        store.set(value, forKey: key(tool, field))
    }

    /// Everything the study and maths tools keep, forgotten (the Developer window's Interface page).
    static func forgetAll() {
        for name in store.dictionaryRepresentation().keys where name.hasPrefix("SimplStudy.") {
            store.removeObject(forKey: name)
        }
    }
}

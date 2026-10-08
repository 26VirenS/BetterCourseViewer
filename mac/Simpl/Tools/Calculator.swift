import AppKit
import SwiftUI

// The scientific calculator, as the web's: Apple's scientific keys, key for key — the memory keys and brackets, 2nd, the
// powers and roots, the logs, the trig and hyperbolic functions with their inverses under 2nd, e, EE, π, Rand, Rad/Deg,
// and the number pad with AC, +/−, %, and the four operators — but algebraic: what is typed stays on the line, the whole
// of it (2 + 3 × 4², sin(30) + √(16), (2 + 3)²), with its result worked out underneath as it grows, and = makes the
// result the line to carry on from (the sum it came from kept small above it). A function goes in front of its argument;
// x², x³, ¹/x, x! and % follow their number; two things side by side multiply (2π, 2(3 + 4)). Nothing typed can make it
// fall over: a line that cannot be worked out says Error.

/// One thing on the calculator's line.
enum CalcTok: Equatable {
    case num(String) // the digits as typed: "12", "3.5", "2E−4", "0."
    case constant(String) // "π" or "e"
    case fn(String) // a function and its bracket: "sin", "sin⁻¹", "log", "log₂", "√", "∛", "ln" …
    case open
    case close
    case comma
    case op(String) // "+", "−", "×", "÷", "^"
    case post(String) // "²", "³", "⁻¹", "!", "%"
    case rootOp // "^(1÷": the y-th root of x

    var text: String {
        switch self {
        case .num(let s): return s
        case .constant(let c): return c
        case .fn(let f): return f + "("
        case .open: return "("
        case .close: return ")"
        case .comma: return ", "
        case .op(let o): return o
        case .post(let p): return p
        case .rootOp: return "^(1÷"
        }
    }

    /// Opens a bracket (a function's, a root's, a plain one).
    var opens: Bool {
        switch self {
        case .open, .fn, .rootOp: return true
        default: return false
        }
    }
}

/// What a line works out to.
enum CalcResult: Equatable {
    case empty
    case incomplete // an operator or a bracket still wants a number: nothing to say yet
    case bad // it cannot be worked out (a division by zero, a root of a negative)
    case ok(Double, String)
}

/// The calculator's state and its keys. A value: a view keeps one in its state.
struct CalcEngine {
    struct Past: Identifiable, Equatable {
        let id = UUID()
        let sum: String
        let value: Double
    }

    var toks: [CalcTok] = []
    /// The line is a result: a number starts afresh, an operator carries on from it.
    var fresh = false
    var freshValue: Double = 0
    /// The sum a result came from.
    var sub = ""
    var mem: Double = 0
    var deg = true
    var second = false
    var err = false
    /// The sums worked out with =, newest first.
    var history: [Past] = []

    // MARK: Keys

    mutating func press(_ key: String) {
        err = false
        if key.count == 1, let c = key.first, c.isASCII, c.isNumber {
            digit(key)
            return
        }
        switch key {
        case ".": point()
        case "ee":
            if fresh { carryOn() }
            if case .num(let s)? = toks.last, !s.contains("E") { toks[toks.count - 1] = .num(s + "E") }
        case "back": back()
        case "neg": negate()
        case "x2", "x3", "inv", "fact", "pct":
            if fresh { carryOn() }
            if endsValue { toks.append(.post(CalcEngine.post[key] ?? "")) }
        case "ac":
            toks = []
            sub = ""
            fresh = false
        case "+", "-", "*", "/", "^", "ypow", "root": operate(key)
        case "(":
            if fresh { startFresh([.open]) } else { toks.append(.open) }
        case ")":
            if openCount > 0 && endsValue { toks.append(.close) }
        case "comma":
            if endsValue && openCount > 0 { toks.append(.comma) }
        case "=": equals()
        case "pi", "e":
            let c: CalcTok = .constant(key == "pi" ? "π" : "e")
            if fresh { startFresh([c]) } else { toks.append(c) }
        case "rand":
            let s = CalcEngine.plain((Double.random(in: 0..<1) * 1e6).rounded() / 1e6)
            if fresh { startFresh([.num(s)]) } else { toks.append(.num(s)) }
        case "mc": mem = 0
        case "mplus", "mminus":
            if case .ok(let v, _) = evaluate() { mem += key == "mplus" ? v : -v }
        case "mr":
            let t = CalcEngine.tokens(of: mem)
            if fresh { startFresh(t) } else { toks.append(contentsOf: t) }
        case "rad": deg.toggle()
        case "second": second.toggle()
        default:
            if let f = CalcEngine.functions[key] {
                if fresh {
                    // a result gets wrapped: sin(result)
                    toks = f + toks + [CalcTok.close]
                    fresh = false
                    sub = ""
                } else {
                    toks.append(contentsOf: f)
                }
            }
        }
    }

    private mutating func digit(_ d: String) {
        if fresh { startFresh([.num(d)]); return }
        if case .num(let s)? = toks.last {
            toks[toks.count - 1] = .num(s + d)
        } else {
            toks.append(.num(d))
        }
    }

    private mutating func point() {
        if fresh { startFresh([.num("0.")]); return }
        if case .num(let s)? = toks.last {
            if !s.contains(".") && !s.contains("E") { toks[toks.count - 1] = .num(s + ".") }
        } else {
            toks.append(.num("0."))
        }
    }

    private mutating func back() {
        if fresh { startFresh([]); return }
        guard let last = toks.last else { return }
        if case .num(let s) = last, s.count > 1 {
            toks[toks.count - 1] = .num(String(s.dropLast()))
        } else {
            toks.removeLast()
        }
    }

    /// +/−: a minus in front of the number being typed, on or off (or the exponent's sign, after EE).
    private mutating func negate() {
        if fresh { carryOn() }
        guard case .num(let s)? = toks.last else {
            if toks.isEmpty || endsOp { toks.append(.op("−")) }
            return
        }
        if s.hasSuffix("E") { toks[toks.count - 1] = .num(s + "−"); return }
        if s.hasSuffix("E−") { toks[toks.count - 1] = .num(String(s.dropLast())); return }
        let i = toks.count - 1
        if i > 0, toks[i - 1] == .op("−"), CalcEngine.wantsValue(i - 1 > 0 ? toks[i - 2] : nil) {
            toks.remove(at: i - 1)
        } else {
            toks.insert(.op("−"), at: i)
        }
    }

    private mutating func operate(_ key: String) {
        let o: CalcTok
        switch key {
        case "+": o = .op("+")
        case "-": o = .op("−")
        case "*": o = .op("×")
        case "/": o = .op("÷")
        case "root": o = .rootOp
        default: o = .op("^")
        }
        if fresh { carryOn() }
        guard let last = toks.last else {
            if o == .op("−") { toks.append(o) }
            return
        }
        if case .op(let p) = last {
            // 2 × − 3; otherwise the operator is swapped
            if o == .op("−") && (p == "×" || p == "÷" || p == "^") {
                toks.append(o)
            } else {
                toks[toks.count - 1] = o
            }
            return
        }
        switch last {
        case .open, .comma, .fn, .rootOp:
            if o == .op("−") { toks.append(o) }
            return
        default:
            toks.append(o)
        }
    }

    private mutating func equals() {
        if fresh { return } // (a result already: = again changes nothing)
        let r = evaluate()
        guard case .ok(let v, let closed) = r else {
            err = r == .bad
            return
        }
        sub = closed + " ="
        history.insert(Past(sum: closed, value: v), at: 0)
        if history.count > 60 { history.removeLast(history.count - 60) }
        toks = CalcEngine.tokens(of: v)
        freshValue = v
        fresh = true
    }

    /// A value put on the line (a result from the history, the memory).
    mutating func insert(_ v: Double) {
        let t = CalcEngine.tokens(of: v)
        if fresh || toks.isEmpty {
            startFresh(t)
        } else if endsValue {
            toks.append(.op("×"))
            toks.append(contentsOf: t)
        } else {
            toks.append(contentsOf: t)
        }
    }

    private mutating func startFresh(_ t: [CalcTok]) {
        toks = t
        fresh = false
        sub = ""
    }

    private mutating func carryOn() {
        fresh = false
        sub = ""
    }

    // MARK: Reading the line

    /// A number is wanted next.
    private var endsOp: Bool {
        CalcEngine.wantsValue(toks.last)
    }

    /// Something a mark or an operator can follow.
    private var endsValue: Bool {
        guard let last = toks.last else { return false }
        switch last {
        case .num(let s): return s.last.map { $0.isNumber || $0 == "." } ?? false
        case .constant, .close, .post: return true
        default: return false
        }
    }

    private var openCount: Int {
        toks.reduce(0) { n, t in t.opens ? n + 1 : (t == .close ? n - 1 : n) }
    }

    /// Whether a minus after `t` is a sign (at the start, after an operator or a bracket) rather than a subtraction.
    static func wantsValue(_ t: CalcTok?) -> Bool {
        guard let t else { return true }
        switch t {
        case .op, .open, .comma, .fn, .rootOp: return true
        default: return false
        }
    }

    /// The line as shown: binary operators spaced, a sign close to its number.
    var lineText: String {
        var out = ""
        var prev: CalcTok?
        for t in toks {
            if case .op(let o) = t, !CalcEngine.wantsValue(prev) {
                out += " \(o) "
            } else {
                out += t.text
            }
            prev = t
        }
        return out
    }

    /// The display: the line, and the small line above it — the result as the line grows, or the sum a result came from.
    var shown: (line: String, sub: String) {
        if toks.isEmpty { return ("0", err ? "Error" : "") }
        if fresh { return (CalcEngine.format(freshValue), sub) }
        switch evaluate() {
        case .ok(let v, _): return (lineText, "= " + CalcEngine.format(v))
        case .bad: return (lineText, "Error")
        default: return (lineText, err ? "Error" : "")
        }
    }

    /// The value showing: a result, or what the line works out to.
    var value: Double? {
        if fresh { return freshValue }
        if case .ok(let v, _) = evaluate() { return v }
        return nil
    }

    func evaluate() -> CalcResult {
        if toks.isEmpty { return .empty }
        var list: [CalcTok] = []
        for t in toks {
            if t == .rootOp {
                list.append(contentsOf: [.op("^"), .open, .num("1"), .op("÷")])
            } else {
                list.append(t)
            }
        }
        let open = max(0, openCount)
        list.append(contentsOf: Array(repeating: CalcTok.close, count: open))
        let closed = lineText + String(repeating: ")", count: open)
        var p = CalcParser(toks: list, deg: deg)
        do {
            let v = try p.expression()
            if p.i < list.count { return .bad }
            return v.isFinite ? .ok(v, closed) : .bad
        } catch CalcParser.Fail.incomplete {
            return .incomplete
        } catch {
            return .bad
        }
    }

    // MARK: Tables and numbers

    static let post: [String: String] = ["x2": "²", "x3": "³", "inv": "⁻¹", "fact": "!", "pct": "%"]

    /// What each function key puts on the line.
    static let functions: [String: [CalcTok]] = [
        "sin": [.fn("sin")], "cos": [.fn("cos")], "tan": [.fn("tan")],
        "asin": [.fn("sin⁻¹")], "acos": [.fn("cos⁻¹")], "atan": [.fn("tan⁻¹")],
        "sinh": [.fn("sinh")], "cosh": [.fn("cosh")], "tanh": [.fn("tanh")],
        "asinh": [.fn("sinh⁻¹")], "acosh": [.fn("cosh⁻¹")], "atanh": [.fn("tanh⁻¹")],
        "ln": [.fn("ln")], "log10": [.fn("log")], "log2": [.fn("log₂")], "logy": [.fn("log")],
        "sqrt": [.fn("√")], "cbrt": [.fn("∛")],
        "ex": [.constant("e"), .op("^"), .open], "10x": [.num("10"), .op("^"), .open], "2x": [.num("2"), .op("^"), .open],
    ]

    /// The digits of a number as typed, read (nil while it is still being typed: "2E", "2E−").
    static func number(_ s: String) -> Double? {
        var t = s.replacingOccurrences(of: "−", with: "-").replacingOccurrences(of: "E", with: "e")
        if t.hasSuffix("e") || t.hasSuffix("e-") { return nil }
        if t.hasPrefix(".") { t = "0" + t }
        if t.hasSuffix(".") { t += "0" }
        return Double(t)
    }

    /// A value as it goes back on the line: plain digits (E for a very large or small one), a sign in front.
    static func tokens(of v: Double) -> [CalcTok] {
        let digits = plain(abs(v))
        return v < 0 ? [.op("−"), .num(digits)] : [.num(digits)]
    }

    /// Up to twelve figures, no grouping; E-notation past 10¹⁵ or under 10⁻⁹.
    static func plain(_ v: Double) -> String {
        let a = abs(v)
        if a != 0 && (a >= 1e15 || a < 1e-9) {
            var s = String(format: "%.11e", v)
            s = trimExponent(s)
            return s.replacingOccurrences(of: "e+", with: "E").replacingOccurrences(of: "e-", with: "E−").replacingOccurrences(of: "e", with: "E")
        }
        return plainFormatter.string(from: NSNumber(value: v)) ?? String(v)
    }

    /// A value for the clipboard: plain digits with ASCII signs, for pasting anywhere.
    static func ascii(_ v: Double) -> String {
        plain(v).replacingOccurrences(of: "−", with: "-")
    }

    /// The number for the display: up to twelve figures, thousands grouped, very large or small ones in e-notation.
    static func format(_ v: Double) -> String {
        guard v.isFinite else { return "Error" }
        let x = v == 0 ? 0 : v
        let a = abs(x)
        if a != 0 && (a >= 1e15 || a < 1e-9) {
            return trimExponent(String(format: "%.8e", x)).replacingOccurrences(of: "-", with: "−")
        }
        return (groupedFormatter.string(from: NSNumber(value: x)) ?? String(x)).replacingOccurrences(of: "-", with: "−")
    }

    /// "1.50000000e+20" → "1.5e+20".
    private static func trimExponent(_ s: String) -> String {
        guard let e = s.firstIndex(where: { $0 == "e" || $0 == "E" }) else { return s }
        var mantissa = String(s[s.startIndex..<e])
        let exponent = String(s[e...])
        if mantissa.contains(".") {
            while mantissa.hasSuffix("0") { mantissa.removeLast() }
            if mantissa.hasSuffix(".") { mantissa.removeLast() }
        }
        return mantissa + exponent
    }

    private static let plainFormatter: NumberFormatter = {
        let f = NumberFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.numberStyle = .decimal
        f.usesGroupingSeparator = false
        f.usesSignificantDigits = true
        f.minimumSignificantDigits = 1
        f.maximumSignificantDigits = 12
        return f
    }()

    private static let groupedFormatter: NumberFormatter = {
        let f = NumberFormatter()
        f.locale = Locale(identifier: "en_US")
        f.numberStyle = .decimal
        f.usesGroupingSeparator = true
        f.usesSignificantDigits = true
        f.minimumSignificantDigits = 1
        f.maximumSignificantDigits = 12
        return f
    }()
}

/// The line worked out: the usual precedence, ^ from the right, − in front of a number, two things side by side multiplied.
struct CalcParser {
    enum Fail: Error { case incomplete, bad }

    let toks: [CalcTok]
    let deg: Bool
    var i = 0

    init(toks: [CalcTok], deg: Bool) {
        self.toks = toks
        self.deg = deg
    }

    private func peekOp() -> String? {
        guard i < toks.count, case .op(let o) = toks[i] else { return nil }
        return o
    }

    private func startsValue() -> Bool {
        guard i < toks.count else { return false }
        switch toks[i] {
        case .num, .constant, .fn, .open: return true
        default: return false
        }
    }

    mutating func expression() throws -> Double {
        var v = try term()
        while let o = peekOp(), o == "+" || o == "−" {
            i += 1
            let r = try term()
            v = o == "+" ? v + r : v - r
        }
        return v
    }

    private mutating func term() throws -> Double {
        var v = try unary()
        while true {
            if let o = peekOp(), o == "×" || o == "÷" {
                i += 1
                let r = try unary()
                v = o == "×" ? v * r : v / r
            } else if startsValue() {
                let r = try unary()
                v *= r
            } else {
                return v
            }
        }
    }

    private mutating func unary() throws -> Double {
        if let o = peekOp(), o == "−" {
            i += 1
            let r = try unary()
            return -r
        }
        if let o = peekOp(), o == "+" {
            i += 1
            return try unary()
        }
        return try power()
    }

    private mutating func power() throws -> Double {
        let b = try postfix()
        if let o = peekOp(), o == "^" {
            i += 1
            let e = try unary()
            return pow(b, e)
        }
        return b
    }

    private mutating func postfix() throws -> Double {
        var v = try atom()
        while i < toks.count, case .post(let p) = toks[i] {
            i += 1
            switch p {
            case "!": v = CalcParser.factorial(v)
            case "%": v /= 100
            case "²": v *= v
            case "³": v = v * v * v
            case "⁻¹": v = 1 / v
            default: break
            }
        }
        return v
    }

    private mutating func atom() throws -> Double {
        guard i < toks.count else { throw Fail.incomplete }
        let t = toks[i]
        switch t {
        case .num(let s):
            i += 1
            guard let v = CalcEngine.number(s) else { throw Fail.incomplete }
            return v
        case .constant(let c):
            i += 1
            return c == "π" ? Double.pi : M_E
        case .fn(let f):
            i += 1
            let first = try expression()
            var args = [first]
            while i < toks.count, toks[i] == .comma {
                i += 1
                let next = try expression()
                args.append(next)
            }
            guard i < toks.count, toks[i] == .close else { throw Fail.incomplete }
            i += 1
            return try apply(f, args)
        case .open:
            i += 1
            let v = try expression()
            guard i < toks.count, toks[i] == .close else { throw Fail.incomplete }
            i += 1
            return v
        default:
            throw Fail.bad
        }
    }

    private func toRad(_ x: Double) -> Double { deg ? x * Double.pi / 180 : x }
    private func fromRad(_ x: Double) -> Double { deg ? x * 180 / Double.pi : x }
    /// sin(π) is 0, not 1.2e-16 (π being what a Double can hold of it).
    private func snap(_ x: Double) -> Double { abs(x) < 1e-14 ? 0 : x }

    private func apply(_ f: String, _ args: [Double]) throws -> Double {
        let x = args[0]
        switch f {
        case "sin": return snap(sin(toRad(x)))
        case "cos": return snap(cos(toRad(x)))
        case "tan": return snap(tan(toRad(x)))
        case "sin⁻¹": return fromRad(asin(x))
        case "cos⁻¹": return fromRad(acos(x))
        case "tan⁻¹": return fromRad(atan(x))
        case "sinh": return sinh(x)
        case "cosh": return cosh(x)
        case "tanh": return tanh(x)
        case "sinh⁻¹": return asinh(x)
        case "cosh⁻¹": return acosh(x)
        case "tanh⁻¹": return atanh(x)
        case "ln": return log(x)
        case "log": return args.count > 1 ? log(x) / log(args[1]) : log10(x)
        case "log₂": return log2(x)
        case "√": return x.squareRoot()
        case "∛": return cbrt(x)
        default: throw Fail.bad
        }
    }

    /// x! — and Γ(x + 1) for a number that is not whole.
    static func factorial(_ n: Double) -> Double {
        if n < 0 && n == n.rounded() { return .nan }
        if n == n.rounded() {
            if n > 170 { return .infinity }
            var r = 1.0
            var k = 2.0
            while k <= n {
                r *= k
                k += 1
            }
            return r
        }
        return tgamma(n + 1)
    }
}

// MARK: - The keys

/// A key of the calculator: its id (what it presses), what it shows, and its kind (a digit, an operator, a top key, a
/// function).
struct CalcKey: Identifiable, Hashable {
    let id: String
    let label: String
    var kind: Kind = .fn
    var wide = false

    enum Kind { case num, op, top, fn }

    /// The scientific layout: five rows of ten, as Apple's.
    static let scientific: [[CalcKey]] = [
        [k("(", "("), k(")", ")"), k("mc", "mc"), k("mplus", "m+"), k("mminus", "m−"), k("mr", "mr"), k("ac", "AC", .top), k("neg", "+/−", .top), k("pct", "%", .top), k("/", "÷", .op)],
        [k("second", "2ⁿᵈ"), k("x2", "x²"), k("x3", "x³"), k("^", "xʸ"), k("ex", "eˣ"), k("10x", "10ˣ"), k("7", "7", .num), k("8", "8", .num), k("9", "9", .num), k("*", "×", .op)],
        [k("inv", "¹⁄x"), k("sqrt", "²√x"), k("cbrt", "³√x"), k("root", "ʸ√x"), k("ln", "ln"), k("log10", "log₁₀"), k("4", "4", .num), k("5", "5", .num), k("6", "6", .num), k("-", "−", .op)],
        [k("fact", "x!"), k("sin", "sin"), k("cos", "cos"), k("tan", "tan"), k("e", "e"), k("ee", "EE"), k("1", "1", .num), k("2", "2", .num), k("3", "3", .num), k("+", "+", .op)],
        [k("rad", "Rad"), k("sinh", "sinh"), k("cosh", "cosh"), k("tanh", "tanh"), k("pi", "π"), k("rand", "Rand"), CalcKey(id: "0", label: "0", kind: .num, wide: true), k(".", ".", .num), k("=", "=", .op)],
    ]

    /// The basic layout, for the pin's popover: the number pad with brackets, a root, a square and π.
    static let basic: [[CalcKey]] = [
        [k("(", "("), k(")", ")"), k("sqrt", "√"), k("x2", "x²"), k("pi", "π")],
        [k("ac", "AC", .top), k("neg", "+/−", .top), k("pct", "%", .top), k("^", "xʸ"), k("/", "÷", .op)],
        [k("7", "7", .num), k("8", "8", .num), k("9", "9", .num), k("back", "⌫", .top), k("*", "×", .op)],
        [k("4", "4", .num), k("5", "5", .num), k("6", "6", .num), k("ln", "ln"), k("-", "−", .op)],
        [k("1", "1", .num), k("2", "2", .num), k("3", "3", .num), k("sin", "sin"), k("+", "+", .op)],
        [CalcKey(id: "0", label: "0", kind: .num, wide: true), k(".", ".", .num), k("cos", "cos"), k("=", "=", .op)],
    ]

    private static func k(_ id: String, _ label: String, _ kind: Kind = .fn) -> CalcKey {
        CalcKey(id: id, label: label, kind: kind)
    }

    /// What a key under 2nd becomes: [id, label].
    static let alternates: [String: (String, String)] = [
        "ex": ("ypow", "yˣ"), "10x": ("2x", "2ˣ"), "ln": ("logy", "logᵧ"), "log10": ("log2", "log₂"),
        "sin": ("asin", "sin⁻¹"), "cos": ("acos", "cos⁻¹"), "tan": ("atan", "tan⁻¹"),
        "sinh": ("asinh", "sinh⁻¹"), "cosh": ("acosh", "cosh⁻¹"), "tanh": ("atanh", "tanh⁻¹"),
    ]

    static let titles: [String: String] = [
        "mc": "Memory clear", "mplus": "Memory add", "mminus": "Memory subtract", "mr": "Memory recall", "ac": "All clear",
        "neg": "Change sign", "pct": "Percent", "second": "Second functions", "x2": "Squared", "x3": "Cubed", "^": "To the power of",
        "ex": "e to the x", "10x": "10 to the x", "inv": "One over x", "sqrt": "Square root", "cbrt": "Cube root",
        "root": "The y-th root of x", "ln": "Natural log", "log10": "Log base 10", "fact": "Factorial", "ee": "Times ten to the",
        "pi": "Pi", "rand": "A random number between 0 and 1", "/": "Divide", "*": "Multiply", "-": "Subtract", "+": "Add",
        "=": "Equals", "back": "Delete", "e": "Euler’s number",
    ]
}

/// A key's face: a digit dark, an operator in orange, the top keys lighter, the functions quiet.
private struct CalcKeyStyle: ButtonStyle {
    let kind: CalcKey.Kind
    var on = false
    var height: CGFloat

    func makeBody(configuration: Configuration) -> some View {
        let shape = RoundedRectangle(cornerRadius: height * 0.32, style: .continuous)
        return configuration.label
            .font(.system(size: kind == .fn ? height * 0.36 : height * 0.44, weight: kind == .fn ? .medium : .regular))
            .foregroundStyle(kind == .op || on ? Color.white : Color.primary)
            .frame(maxWidth: .infinity, minHeight: height, maxHeight: height)
            .background(fill(pressed: configuration.isPressed), in: shape)
            .contentShape(shape)
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
            .animation(Motion.snappy, value: configuration.isPressed)
    }

    private func fill(pressed: Bool) -> Color {
        if on { return Color.orange.opacity(pressed ? 0.7 : 0.9) }
        switch kind {
        case .op: return Color.orange.opacity(pressed ? 0.7 : 1)
        case .num: return Color.primary.opacity(pressed ? 0.2 : 0.11)
        case .top: return Color.primary.opacity(pressed ? 0.26 : 0.17)
        case .fn: return Color.primary.opacity(pressed ? 0.15 : 0.06)
        }
    }
}

/// The keys, laid out in a grid; 2nd turns the keys that have one to their other function.
struct CalcKeypad: View {
    @Binding var eng: CalcEngine
    var rows: [[CalcKey]] = CalcKey.scientific
    var keyHeight: CGFloat = 46
    var spacing: CGFloat = 7
    var pressed: () -> Void = {}

    var body: some View {
        Grid(horizontalSpacing: spacing, verticalSpacing: spacing) {
            ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                GridRow {
                    ForEach(row) { key in
                        keyButton(key)
                            .gridCellColumns(key.wide ? 2 : 1)
                    }
                }
            }
        }
    }

    private func keyButton(_ key: CalcKey) -> some View {
        let alt = eng.second ? CalcKey.alternates[key.id] : nil
        let id = alt?.0 ?? key.id
        let label = key.id == "rad" ? (eng.deg ? "Rad" : "Deg") : (alt?.1 ?? key.label)
        let title = key.id == "rad" ? (eng.deg ? "Switch to radians" : "Switch to degrees") : (CalcKey.titles[id] ?? label)
        return Button {
            eng.press(id)
            pressed()
        } label: {
            Text(label)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
        }
        .buttonStyle(CalcKeyStyle(kind: key.kind, on: key.id == "second" && eng.second, height: keyHeight))
        .help(title)
        .accessibilityLabel(title)
    }
}

/// The display: the small line over the large one, both kept to their ends.
struct CalcDisplay: View {
    let eng: CalcEngine
    var big: CGFloat = 52

    var body: some View {
        let s = eng.shown
        VStack(alignment: .trailing, spacing: 4) {
            HStack(spacing: 8) {
                if !eng.deg { badge("Rad") }
                if eng.mem != 0 { badge("M") }
                if eng.second { badge("2nd") }
                Spacer(minLength: 8)
                Text(s.sub.isEmpty ? " " : s.sub)
                    .font(.system(size: big * 0.4).monospacedDigit())
                    .foregroundStyle(s.sub == "Error" ? Color.red : Color.secondary)
                    .lineLimit(1)
                    .truncationMode(.head)
                    .textSelection(.enabled)
            }
            Text(s.line)
                .font(.system(size: big, weight: .light, design: .rounded).monospacedDigit())
                .lineLimit(1)
                .minimumScaleFactor(0.35)
                .truncationMode(.head)
                .frame(maxWidth: .infinity, alignment: .trailing)
                .textSelection(.enabled)
                .contentTransition(.numericText())
        }
        .accessibilityElement(children: .combine)
    }

    private func badge(_ text: String) -> some View {
        Text(text)
            .font(.sCaption.weight(.semibold))
            .padding(.horizontal, 7)
            .padding(.vertical, 2)
            .foregroundStyle(.orange)
            .background(Color.orange.opacity(0.15), in: Capsule())
    }
}

/// What the keyboard presses: digits, . + − × ÷ ^ ( ) % ! , Return for =, Delete, Escape for AC.
func calcKeyFor(_ press: KeyPress) -> String? {
    if press.modifiers.contains(.command) || press.modifiers.contains(.control) { return nil }
    if press.key == .return { return "=" }
    if press.key == .delete || press.key == .deleteForward { return "back" }
    if press.key == .escape { return "ac" }
    let c = press.characters
    if c.count == 1, let ch = c.first, ch.isASCII, ch.isNumber { return c }
    switch c {
    case "=": return "="
    case "%": return "pct"
    case "!": return "fact"
    case ",": return "comma"
    case "(", ")", ".", "+", "-", "*", "/", "^": return c
    case "x", "X": return "*"
    case "p": return "pi"
    case "e": return "e"
    default: return nil
    }
}

// MARK: - The calculator as a tool

/// Calculator: the display over Apple's scientific keys, the keyboard on it; beside it on a wide window, the sums worked
/// out with = (a press puts a result back on the line), and Copy.
struct CalculatorTool: View {
    @State private var eng = CalcEngine()
    @FocusState private var focused: Bool

    var body: some View {
        ToolPage {
            ToolColumns(sideWidth: 360, breakpoint: 1020) {
                calculator
            } side: {
                historyPanel
            }
        }
        .onAppear { focused = true }
    }

    private var calculator: some View {
        VStack(spacing: 16) {
            CalcDisplay(eng: eng, big: 60)
                .padding(.horizontal, 22)
                .padding(.vertical, 18)
                .card(radius: 22)
            CalcKeypad(eng: $eng, keyHeight: 52, spacing: 8) { focused = true }
            HStack(spacing: 10) {
                Button {
                    if let v = eng.value { copyToPasteboard(CalcEngine.ascii(v)) }
                } label: {
                    Label("Copy Result", systemImage: "doc.on.doc")
                }
                .glassButton()
                .disabled(eng.value == nil)
                Spacer()
                Text("Type on the keyboard too: Return is =, Delete takes the last thing off, Escape clears.")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: 820)
        .focusable()
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress { press in
            guard let key = calcKeyFor(press) else { return .ignored }
            eng.press(key)
            return .handled
        }
    }

    private var historyPanel: some View {
        PageSection(title: "History", trailing: nil) {
            if !eng.history.isEmpty {
                Button("Clear") { withAnimation(Motion.gentle) { eng.history.removeAll() } }
                    .buttonStyle(.link)
                    .font(.sCallout)
            }
        } content: {
            if eng.history.isEmpty {
                Text("Sums worked out with = show here. A press puts the result back on the line.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                VStack(spacing: 0) {
                    ForEach(Array(eng.history.enumerated()), id: \.element.id) { i, past in
                        if i > 0 { RowDivider(inset: 12) }
                        RowLink {
                            eng.insert(past.value)
                            focused = true
                        } label: {
                            VStack(alignment: .trailing, spacing: 2) {
                                Text(past.sum + " =")
                                    .font(.sCallout.monospacedDigit())
                                    .foregroundStyle(.secondary)
                                    .lineLimit(2)
                                Text(CalcEngine.format(past.value))
                                    .font(.sTitle3.monospacedDigit())
                            }
                            .frame(maxWidth: .infinity, alignment: .trailing)
                        }
                        .contextMenu {
                            Button("Copy Result") { copyToPasteboard(CalcEngine.ascii(past.value)) }
                            Button("Copy Sum") { copyToPasteboard(past.sum + " = " + CalcEngine.format(past.value)) }
                        }
                    }
                }
                .padding(6)
                .card()
            }
        }
    }
}

/// The calculator's pin, opened: the display and the basic keys, the keyboard on it.
struct CalculatorCompact: View {
    @State private var eng = CalcEngine()
    @FocusState private var focused: Bool

    var body: some View {
        VStack(spacing: 12) {
            CalcDisplay(eng: eng, big: 38)
                .padding(.horizontal, 6)
            CalcKeypad(eng: $eng, rows: CalcKey.basic, keyHeight: 38, spacing: 6) { focused = true }
        }
        .focusable()
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress { press in
            guard let key = calcKeyFor(press) else { return .ignored }
            if key == "ac" { return .ignored } // (Escape closes the popover)
            eng.press(key)
            return .handled
        }
        .onAppear { focused = true }
    }
}

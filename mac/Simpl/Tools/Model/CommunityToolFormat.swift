import Foundation

// (1.3.211) Simpl Community tools, format 1 — the Mac's own copy of the format. docs/community-tools.md is the spec and
// site/src/tool-format.js the reference, mirrored here step for step: a tool checked as the apps check one (leniently,
// so the keys a later format adds are passed over), its formulas read and worked out, and its numbers written as every
// copy writes them. Foundation only, so the Models job checks it against the reference's answers
// (docs/community-tools-vectors.json, scripts/dev/swift-checks/CommunityToolChecks.swift): one that differs fails the
// run. A tool is data, never code — nothing here reaches the network, the files or anything else of the app's, and a
// formula cannot loop.

/// A JSON value as JSON.parse gives it, for a tool read as leniently as the reference reads one: a number is never a
/// true or a false, and a key that is missing is not one that is null.
enum ToolJSON: Hashable {
    case null
    case bool(Bool)
    case number(Double)
    case string(String)
    case array([ToolJSON])
    case object([String: ToolJSON])
    /// A number too large for a double (JSON.parse's Infinity): never a number a tool takes.
    case other

    subscript(key: String) -> ToolJSON? {
        if case .object(let o) = self { return o[key] }
        return nil
    }

    var stringValue: String? {
        if case .string(let s) = self { return s }
        return nil
    }

    var arrayValue: [ToolJSON]? {
        if case .array(let a) = self { return a }
        return nil
    }

    /// The reference's isNum: a finite number.
    var finite: Double? {
        if case .number(let n) = self, n.isFinite { return n }
        return nil
    }
}

extension ToolJSON: Codable {
    init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if c.decodeNil() {
            self = .null
        } else if let b = try? c.decode(Bool.self) {
            self = .bool(b)
        } else if let n = try? c.decode(Double.self) {
            self = .number(n)
        } else if let s = try? c.decode(String.self) {
            self = .string(s)
        } else if let a = try? c.decode([ToolJSON].self) {
            self = .array(a)
        } else if let o = try? c.decode([String: ToolJSON].self) {
            self = .object(o)
        } else {
            self = .other
        }
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.singleValueContainer()
        switch self {
        case .null, .other: try c.encodeNil()
        case .bool(let b): try c.encode(b)
        case .number(let n):
            if n.isFinite { try c.encode(n) } else { try c.encodeNil() }
        case .string(let s): try c.encode(s)
        case .array(let a): try c.encode(a)
        case .object(let o): try c.encode(o)
        }
    }
}

/// A formula read into its parts (the reference's parse tree).
indirect enum ToolExpr {
    case number(Double)
    case name(String)
    case unary(String, ToolExpr)
    case binary(String, ToolExpr, ToolExpr)
    case cond(ToolExpr, ToolExpr, ToolExpr)
    case call(String, [ToolExpr])
}

/// Why a formula could not be read (the reference's SyntaxErr).
struct ToolSyntaxError: Error {
    let message: String
}

enum ToolFormat {
    static let format = 1
    static let groups = ["study", "math", "science", "money", "everyday"]
    static let symbols = ["bolt", "percent", "function", "sum", "divide", "ruler", "scale", "thermometer", "clock", "calendar", "money", "chart",
                          "atom", "flask", "leaf", "heart", "book", "pencil", "globe", "car", "drop", "flame", "gauge", "star", "lightbulb",
                          "music", "dice", "triangle"]
    /// Each function's arity: the fewest and the most values it takes.
    static let functions: [String: (fewest: Int, most: Int)] = [
        "abs": (1, 1), "sqrt": (1, 1), "cbrt": (1, 1), "exp": (1, 1), "ln": (1, 1), "log": (1, 1), "log2": (1, 1),
        "sin": (1, 1), "cos": (1, 1), "tan": (1, 1), "asin": (1, 1), "acos": (1, 1), "atan": (1, 1), "atan2": (2, 2),
        "floor": (1, 1), "ceil": (1, 1), "round": (1, 2), "trunc": (1, 1), "sign": (1, 1), "min": (1, 16), "max": (1, 16),
        "pow": (2, 2), "hypot": (2, 2), "rad": (1, 1), "deg": (1, 1), "clamp": (3, 3), "fact": (1, 1),
    ]
    /// (Math.E's double, written out: 2.718281828459045)
    static let constants: [String: Double] = ["pi": Double.pi, "e": 2.718281828459045, "true": 1, "false": 0]

    enum Limits {
        static let json = 32768, name = 40, maker = 30, summary = 60, label = 32, unit = 12, note = 400, inputs = 16, outputs = 12
        static let options = 12, expr = 300, args = 16, tableCols = 6, tableRows = 100, colName = 24, cell = 48, tableTitle = 40
        static let decimals = 10
    }

    /// 10⁰ … 10¹⁰, exact: every copy uses this table rather than pow(10, n).
    static let pow10: [Double] = [1, 10, 100, 1000, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10]

    /// What the Mac draws for each of the format's symbols (SF Symbols, as the spec maps them).
    static let sfSymbols: [String: String] = [
        "bolt": "bolt.fill", "percent": "percent", "function": "function", "sum": "sum", "divide": "divide", "ruler": "ruler",
        "scale": "scalemass", "thermometer": "thermometer.medium", "clock": "clock", "calendar": "calendar", "money": "dollarsign.circle",
        "chart": "chart.bar", "atom": "atom", "flask": "flask", "leaf": "leaf", "heart": "heart", "book": "book", "pencil": "pencil",
        "globe": "globe", "car": "car", "drop": "drop", "flame": "flame", "gauge": "gauge.medium", "star": "star",
        "lightbulb": "lightbulb", "music": "music.note", "dice": "dice", "triangle": "triangle",
    ]

    static func sfSymbol(_ symbol: String) -> String { sfSymbols[symbol] ?? "puzzlepiece.extension" }

    /// The words of a tool of a later format, refused as one: the apps say "Needs a newer Simpl" for it.
    static let newerMessage = "This tool needs a newer Simpl."

    // MARK: - A checked tool

    struct Option: Hashable {
        let label: String
        let value: Double
    }

    enum Kind: String {
        case number, choice, toggle
    }

    /// An input as checked: a number's unit, range and step (its default clamped into the range), a choice's options
    /// (its default one of their values), a toggle's default.
    struct Input: Hashable {
        let id: String
        let kind: Kind
        let label: String
        var unit = ""
        var min: Double?
        var max: Double?
        var step: Double = 1
        var options: [Option] = []
        /// A number's or a choice's default.
        var number: Double = 0
        /// A toggle's default.
        var on = false
    }

    /// A result as checked: its formula, and the one that hides it when it gives 0.
    struct Output: Hashable {
        let id: String?
        let label: String
        let value: String
        let unit: String
        let decimals: Int
        let when: String?
    }

    struct Table: Hashable {
        let title: String
        let columns: [String]
        let rows: [[String]]
    }

    /// A tool as checked (the reference's `{ tool }`): text cleaned and cut, defaults filled in.
    struct Tool: Hashable {
        /// The site's id for it, if it has one.
        var siteID: String?
        var version: Double?
        var name: String
        var maker: String
        var summary: String
        var group: String
        var symbol: String
        var color: String
        var inputs: [Input]
        var outputs: [Output]
        var table: Table?
        var note: String?
        var created: String?

        /// The key the Mac keeps it under: the site's id (a tool from the gallery always has one).
        var key: String { siteID ?? name }
    }

    /// Why a tool was refused: its code (SC-T-…), a plain message, and where in the tool.
    struct Refusal: Error, Hashable {
        let code: String
        let message: String
        let path: String

        var needsNewer: Bool { code == "SC-T-FORMAT" && message == ToolFormat.newerMessage }
    }

    /// A result worked out: its label, its value, its text, its unit and whether it is shown.
    struct Answer: Hashable {
        let label: String
        let value: Double
        let text: String
        let unit: String
        let shown: Bool
    }

    // MARK: - The language: tokens

    struct Token {
        enum Kind { case num, name, op, end }
        let kind: Kind
        var num: Double = 0
        var text = ""
        let at: Int
    }

    /// The operators, longest first where one starts another.
    static let ops = ["<=", ">=", "==", "!=", "&&", "||", "+", "-", "*", "/", "%", "^", "(", ")", ",", "?", ":", "<", ">", "!"]
    private static let opUnits: [[UInt16]] = ops.map { Array($0.utf16) }

    private static func isDigit(_ c: UInt16) -> Bool { c >= 0x30 && c <= 0x39 }
    private static func isLower(_ c: UInt16) -> Bool { c >= 0x61 && c <= 0x7A }

    /// Where a number that starts at `start` ends — `12`, `0.5`, `.5`, `6.02e23`, `1e-3`, never `1.` — or nil.
    private static func numberEnd(_ u: [UInt16], _ start: Int) -> Int? {
        func digits(_ k: Int) -> Int {
            var j = k
            while j < u.count, isDigit(u[j]) { j += 1 }
            return j
        }
        var i = start
        if i < u.count, isDigit(u[i]) {
            i = digits(i)
            if i + 1 < u.count, u[i] == 0x2E, isDigit(u[i + 1]) { i = digits(i + 1) }
        } else if i + 1 < u.count, u[i] == 0x2E, isDigit(u[i + 1]) {
            i = digits(i + 1)
        } else {
            return nil
        }
        if i < u.count, u[i] == 0x65 || u[i] == 0x45 {
            var j = i + 1
            if j < u.count, u[j] == 0x2B || u[j] == 0x2D { j += 1 }
            if j < u.count, isDigit(u[j]) { i = digits(j) }
        }
        return i
    }

    /// A formula's tokens (read a UTF-16 unit at a time, as JavaScript reads a string).
    static func tokenize(_ src: String) throws -> [Token] {
        let u = Array(src.utf16)
        var out: [Token] = []
        var i = 0
        while i < u.count {
            let c = u[i]
            if c == 0x20 || c == 0x09 {
                i += 1
                continue
            }
            if let end = numberEnd(u, i) {
                let text = String(decoding: u[i..<end], as: UTF16.self)
                // (Number("1e400") is Infinity, Number("1e-400") 0: what a double cannot hold)
                let v = Double(text) ?? (text.contains("e-") || text.contains("E-") ? 0 : Double.infinity)
                out.append(Token(kind: .num, num: v, text: text, at: i))
                i = end
                continue
            }
            if isLower(c) {
                var j = i + 1
                while j < u.count, isLower(u[j]) || isDigit(u[j]) || u[j] == 0x5F { j += 1 }
                out.append(Token(kind: .name, text: String(decoding: u[i..<j], as: UTF16.self), at: i))
                i = j
                continue
            }
            if let k = opUnits.firstIndex(where: { op in i + op.count <= u.count && Array(u[i..<(i + op.count)]) == op }) {
                out.append(Token(kind: .op, text: ops[k], at: i))
                i += opUnits[k].count
                continue
            }
            let ch = String(decoding: [c], as: UTF16.self)
            throw ToolSyntaxError(message: "“\(ch)” can’t be used in a formula")
        }
        out.append(Token(kind: .end, at: u.count))
        return out
    }

    // MARK: - The language: grammar (lowest to highest)
    //   cond  := or ('?' cond ':' cond)?
    //   or    := and ('||' and)*            and := eq ('&&' eq)*
    //   eq    := rel (('=='|'!=') rel)*     rel := add (('<'|'<='|'>'|'>=') add)*
    //   add   := mul (('+'|'-') mul)*       mul := unary (('*'|'/'|'%') unary)*
    //   unary := ('-'|'+'|'!') unary | pow  pow := primary ('^' unary)?
    //   primary := number | name | name '(' cond (',' cond)* ')' | '(' cond ')'

    private final class Parser {
        private let toks: [Token]
        private var p = 0

        init(_ toks: [Token]) {
            self.toks = toks
        }

        private var peek: Token { toks[p] }

        private func isOp(_ ops: [String]) -> Bool { peek.kind == .op && ops.contains(peek.text) }

        @discardableResult
        private func take() -> Token {
            let t = toks[p]
            p += 1
            return t
        }

        private func expect(_ op: String) throws {
            guard isOp([op]) else { throw ToolSyntaxError(message: "Expected “\(op)”\(peek.kind == .end ? " at the end" : "")") }
            take()
        }

        func whole() throws -> ToolExpr {
            let tree = try cond()
            if peek.kind != .end { throw ToolSyntaxError(message: "“\(peek.text)” is out of place") }
            return tree
        }

        private func binary(_ next: () throws -> ToolExpr, _ ops: [String]) throws -> ToolExpr {
            var a = try next()
            while isOp(ops) {
                let op = take().text
                let b = try next()
                a = .binary(op, a, b)
            }
            return a
        }

        private func cond() throws -> ToolExpr {
            let c = try orExpr()
            if isOp(["?"]) {
                take()
                let a = try cond()
                try expect(":")
                let b = try cond()
                return .cond(c, a, b)
            }
            return c
        }

        private func orExpr() throws -> ToolExpr { try binary(andExpr, ["||"]) }
        private func andExpr() throws -> ToolExpr { try binary(eqExpr, ["&&"]) }
        private func eqExpr() throws -> ToolExpr { try binary(relExpr, ["==", "!="]) }
        private func relExpr() throws -> ToolExpr { try binary(addExpr, ["<", "<=", ">", ">="]) }
        private func addExpr() throws -> ToolExpr { try binary(mulExpr, ["+", "-"]) }
        private func mulExpr() throws -> ToolExpr { try binary(unary, ["*", "/", "%"]) }

        private func unary() throws -> ToolExpr {
            if isOp(["-", "+", "!"]) {
                let op = take().text
                let a = try unary()
                return .unary(op, a)
            }
            return try pow()
        }

        private func pow() throws -> ToolExpr {
            let a = try primary()
            if isOp(["^"]) {
                take()
                let b = try unary()
                return .binary("^", a, b)
            }
            return a
        }

        private func primary() throws -> ToolExpr {
            let tk = peek
            if tk.kind == .num {
                take()
                return .number(tk.num)
            }
            if tk.kind == .name {
                take()
                if isOp(["("]) {
                    take()
                    var args: [ToolExpr] = []
                    if !isOp([")"]) {
                        args.append(try cond())
                        while isOp([","]) {
                            take()
                            args.append(try cond())
                        }
                    }
                    try expect(")")
                    return .call(tk.text, args)
                }
                return .name(tk.text)
            }
            if isOp(["("]) {
                take()
                let e = try cond()
                try expect(")")
                return e
            }
            throw ToolSyntaxError(message: tk.kind == .end ? "The formula stops too soon" : "“\(tk.text)” is out of place")
        }
    }

    /// A formula read into its tree, or why it cannot be.
    static func parse(_ src: String) throws -> ToolExpr {
        if jsTrim(src).isEmpty { throw ToolSyntaxError(message: "The formula is empty") }
        if src.utf16.count > Limits.expr { throw ToolSyntaxError(message: "A formula can be \(Limits.expr) characters at most") }
        return try Parser(try tokenize(src)).whole()
    }

    /// Every name a formula reads, in the order it first reads them, and a check of every function it calls (its name
    /// and how many values it is given).
    static func names(in node: ToolExpr) throws -> [String] {
        var out: [String] = []
        try collectNames(node, &out)
        return out
    }

    private static func collectNames(_ node: ToolExpr, _ out: inout [String]) throws {
        switch node {
        case .number: break
        case .name(let n):
            if !out.contains(n) { out.append(n) }
        case .unary(_, let a): try collectNames(a, &out)
        case .binary(_, let a, let b):
            try collectNames(a, &out)
            try collectNames(b, &out)
        case .cond(let c, let a, let b):
            try collectNames(c, &out)
            try collectNames(a, &out)
            try collectNames(b, &out)
        case .call(let f, let args):
            guard let ar = functions[f] else { throw ToolSyntaxError(message: "There is no function “\(f)”") }
            if args.count < ar.fewest || args.count > ar.most {
                let how = ar.fewest == ar.most ? "\(ar.fewest)" : "\(ar.fewest) to \(ar.most)"
                throw ToolSyntaxError(message: "\(f)() takes \(how) value\(ar.most == 1 ? "" : "s")")
            }
            for a in args { try collectNames(a, &out) }
        }
    }

    // MARK: - The language: values

    static func truthy(_ x: Double) -> Bool { x != 0 && !x.isNaN }

    /// Math.max of two (NaN when either is; +0 above −0).
    static func jsMax(_ a: Double, _ b: Double) -> Double {
        if a.isNaN || b.isNaN { return .nan }
        if a == 0 && b == 0 { return a.sign == .minus && b.sign == .minus ? -0.0 : 0.0 }
        return a > b ? a : b
    }

    /// Math.min of two (NaN when either is; −0 below +0).
    static func jsMin(_ a: Double, _ b: Double) -> Double {
        if a.isNaN || b.isNaN { return .nan }
        if a == 0 && b == 0 { return a.sign == .minus || b.sign == .minus ? -0.0 : 0.0 }
        return a < b ? a : b
    }

    /// x^y as every copy works it out (JavaScript's Math.pow, made explicit where C's pow differs).
    static func power(_ x: Double, _ y: Double) -> Double {
        if y.isNaN { return .nan }
        if y == 0 { return 1 }
        if x.isNaN { return .nan }
        if x.magnitude == 1 && !y.isFinite { return .nan }
        return pow(x, y)
    }

    /// x rounded to n decimals, halves away from zero: sign(x) · floor(|x| · 10ⁿ + 0.5) / 10ⁿ.
    static func roundTo(_ x: Double, _ n: Double = 0) -> Double {
        if !x.isFinite || n.isNaN { return n.isNaN ? .nan : x }
        let t = n.rounded(.towardZero)
        let d = Int(Swift.min(Double(Limits.decimals), Swift.max(0, t)))
        let f = pow10[d]
        let a = x.magnitude * f
        if a >= 4503599627370496 { return x } // (2^52: already whole at this many decimals)
        let r = (a + 0.5).rounded(.down) / f
        return x < 0 ? -r : r
    }

    private static func factorial(_ n: Double) -> Double {
        if !n.isFinite || n < 0 || n > 170 || n.rounded(.towardZero) != n { return .nan }
        var r = 1.0
        var i = 2.0
        while i <= n {
            r *= i
            i += 1
        }
        return r
    }

    /// A function worked out on its values (each checked for how many it takes when the tool was).
    private static func call(_ f: String, _ x: [Double]) -> Double {
        func at(_ i: Int) -> Double { i < x.count ? x[i] : .nan }
        switch f {
        case "abs": return at(0).magnitude
        case "sqrt": return at(0).squareRoot()
        case "cbrt": return cbrt(at(0))
        case "exp": return exp(at(0))
        case "ln": return log(at(0))
        case "log": return log10(at(0))
        case "log2": return log2(at(0))
        case "sin": return sin(at(0))
        case "cos": return cos(at(0))
        case "tan": return tan(at(0))
        case "asin": return asin(at(0))
        case "acos": return acos(at(0))
        case "atan": return atan(at(0))
        case "atan2": return atan2(at(0), at(1))
        case "floor": return at(0).rounded(.down)
        case "ceil": return at(0).rounded(.up)
        case "trunc": return at(0).rounded(.towardZero)
        case "round": return roundTo(at(0), x.count > 1 ? x[1] : 0)
        case "sign":
            let v = at(0)
            return v > 0 ? 1 : (v < 0 ? -1 : v) // (0, −0 and NaN as they are)
        case "min":
            guard let first = x.first, !x.contains(where: { $0.isNaN }) else { return .nan }
            return x.dropFirst().reduce(first) { a, b in b < a ? b : a }
        case "max":
            guard let first = x.first, !x.contains(where: { $0.isNaN }) else { return .nan }
            return x.dropFirst().reduce(first) { a, b in b > a ? b : a }
        case "pow": return power(at(0), at(1))
        case "hypot": return hypot(at(0), at(1))
        case "rad": return (at(0) * Double.pi) / 180
        case "deg": return (at(0) * 180) / Double.pi
        case "clamp":
            let v = at(0), lo = at(1), hi = at(2)
            if v.isNaN || lo.isNaN || hi.isNaN { return .nan }
            return jsMin(jsMax(v, lo), hi)
        case "fact": return factorial(at(0))
        default: return .nan
        }
    }

    /// A formula's tree worked out over the names' values (a name it does not know is NaN).
    static func evaluate(_ node: ToolExpr, _ env: [String: Double]) -> Double {
        switch node {
        case .number(let v): return v
        case .name(let n): return env[n] ?? .nan
        case .unary(let op, let a):
            let x = evaluate(a, env)
            if op == "-" { return -x }
            if op == "+" { return x }
            return truthy(x) ? 0 : 1
        case .cond(let c, let a, let b):
            return truthy(evaluate(c, env)) ? evaluate(a, env) : evaluate(b, env)
        case .call(let f, let args):
            return call(f, args.map { evaluate($0, env) })
        case .binary(let op, let l, let r):
            let a = evaluate(l, env)
            let b = evaluate(r, env)
            switch op {
            case "+": return a + b
            case "-": return a - b
            case "*": return a * b
            case "/": return a / b
            case "%": return a.truncatingRemainder(dividingBy: b) // (the sign of the dividend, as C's fmod)
            case "^": return power(a, b)
            case "<": return a < b ? 1 : 0
            case "<=": return a <= b ? 1 : 0
            case ">": return a > b ? 1 : 0
            case ">=": return a >= b ? 1 : 0
            case "==": return a == b ? 1 : 0
            case "!=": return a != b ? 1 : 0
            case "&&": return truthy(a) && truthy(b) ? 1 : 0
            case "||": return truthy(a) || truthy(b) ? 1 : 0
            default: return .nan
            }
        }
    }

    // MARK: - Numbers as a tool shows them

    private static let superscripts: [Character: Character] = [
        "0": "⁰", "1": "¹", "2": "²", "3": "³", "4": "⁴", "5": "⁵", "6": "⁶", "7": "⁷", "8": "⁸", "9": "⁹", "-": "⁻",
    ]

    /// The exponents at which the reference's Math.pow(10, n) — V8's, the fdlibm port — is a double above or below
    /// 10ⁿ's nearest (each by one): the reference divides by its power, so its m × 10ⁿ is this copy's only when this
    /// copy divides by the same double.
    private static let pow10Above: Set<Int> = [-307, -303, -278, -268, -246, -207, -163, -148, -145, -106, -104, -88, -76, -57, -39, -29, -24, -23,
                                               -21, -20, 29, 34, 39, 49, 58, 61, 111, 126, 165, 166, 169, 187, 210, 215, 225, 273, 274, 275]
    private static let pow10Below: Set<Int> = [-306, -222, -166, -138, -128, -122, -111, -101, -63, -32, -17, -5, -4, 26, 36, 42, 70, 95, 96,
                                               136, 142, 191, 218, 232, 242, 244, 260, 279, 284, 290, 294]

    /// Math.pow(10, n) as the reference has it, for a whole n.
    static func referencePow10(_ n: Int) -> Double {
        guard let nearest = Double("1e\(n)") else { return n < 0 ? 0 : .infinity }
        if pow10Above.contains(n) { return nearest.nextUp }
        if pow10Below.contains(n) { return nearest.nextDown }
        return nearest
    }

    /// JavaScript's toFixed: x with d decimals, the nearest — an exact half going up, away from zero (C's printf takes it
    /// to the even one). Exact halves only come of a double with few binary places (as formatNumber's numbers have), so
    /// they are seen in its expansion to 40 decimals.
    static func fixed(_ x: Double, _ d: Int) -> String {
        guard x.isFinite else { return x.isNaN ? "NaN" : (x > 0 ? "Infinity" : "-Infinity") }
        if x < 0 { return "-" + fixed(-x, d) }
        let v = x.magnitude
        let wide = String(format: "%.40f", v)
        let parts = wide.split(separator: ".", maxSplits: 1, omittingEmptySubsequences: false)
        if parts.count == 2 {
            let whole = Array(parts[0]), frac = Array(parts[1])
            let rest = frac.dropFirst(d)
            if rest.first == "5" && rest.dropFirst().allSatisfy({ $0 == "0" }) {
                var digits = whole + Array(frac.prefix(d))
                var i = digits.count - 1
                while i >= 0 {
                    if digits[i] == "9" {
                        digits[i] = "0"
                        i -= 1
                    } else {
                        digits[i] = Character(String((digits[i].wholeNumberValue ?? 0) + 1))
                        break
                    }
                }
                if i < 0 { digits.insert("1", at: 0) }
                let w = digits.count - d
                let head = String(digits[0..<w]), tail = String(digits[w...])
                return d == 0 ? head : "\(head).\(tail)"
            }
        }
        return String(format: "%.\(d)f", v)
    }

    private static func trimZeros(_ s: String) -> String {
        guard s.contains(".") else { return s }
        var t = s
        while t.hasSuffix("0") { t.removeLast() }
        if t.hasSuffix(".") { t.removeLast() }
        return t
    }

    /// The whole part grouped by threes with commas, from five digits.
    private static func group(_ int: String) -> String {
        guard int.count >= 5 else { return int }
        var out = ""
        let n = int.count
        for (i, c) in int.enumerated() {
            if i > 0 && (n - i) % 3 == 0 { out.append(",") }
            out.append(c)
        }
        return out
    }

    /// A result as text: "—" when it is not a number; from 1e12 up, or under 1e-4 and rounding to 0 at `decimals`, as
    /// "m × 10ⁿ" (m to 3 decimals); otherwise rounded to `decimals` (4 by default) halves away from zero, trailing zeros
    /// dropped, the whole part grouped by threes with commas from 10,000.
    static func formatNumber(_ v: Double, _ decimals: Int = 4) -> String {
        guard v.isFinite else { return "—" }
        let d = Swift.min(Limits.decimals, Swift.max(0, decimals))
        if v == 0 { return "0" }
        let a = v.magnitude
        let r = roundTo(v, Double(d))
        if a >= 1e12 || (r == 0 && a < 1e-4) {
            var ex = Int(log10(a).rounded(.down))
            var m = v / referencePow10(ex)
            if m.magnitude >= 10 {
                m /= 10
                ex += 1
            }
            if m.magnitude < 1 {
                m *= 10
                ex -= 1
            }
            m = roundTo(m, 3)
            if m.magnitude >= 10 {
                m = roundTo(m / 10, 3)
                ex += 1
            }
            let exponent = String(String(ex).map { superscripts[$0] ?? $0 })
            return "\(trimZeros(fixed(m, 3))) × 10\(exponent)"
        }
        if r == 0 { return "0" }
        let s = trimZeros(fixed(r.magnitude, d))
        let parts = s.split(separator: ".", maxSplits: 1, omittingEmptySubsequences: false)
        let int = String(parts[0])
        let frac = parts.count > 1 ? String(parts[1]) : ""
        return "\(r < 0 ? "-" : "")\(group(int))\(frac.isEmpty ? "" : ".\(frac)")"
    }

    /// A number as JavaScript's String(n) writes it (a table's cell, a tool's size): the shortest digits that read
    /// back as it — Swift's own description finds them — placed as JavaScript places them ("90", "0.000001", "1e-7",
    /// "1e+21").
    static func jsString(_ v: Double) -> String {
        if v.isNaN { return "NaN" }
        if v.isInfinite { return v > 0 ? "Infinity" : "-Infinity" }
        if v == 0 { return "0" }
        if v < 0 { return "-" + jsString(-v) }
        let desc = "\(v)"
        var mantissa = Substring(desc)
        var exp = 0
        if let e = desc.firstIndex(where: { $0 == "e" || $0 == "E" }) {
            mantissa = desc[desc.startIndex..<e]
            exp = Int(desc[desc.index(after: e)...]) ?? 0
        }
        var digits: String
        let point: Int
        if let dot = mantissa.firstIndex(of: ".") {
            point = mantissa.distance(from: mantissa.startIndex, to: dot)
            digits = String(mantissa[mantissa.startIndex..<dot]) + String(mantissa[mantissa.index(after: dot)...])
        } else {
            digits = String(mantissa)
            point = digits.count
        }
        var n = point + exp
        while digits.count > 1 && digits.hasPrefix("0") {
            digits.removeFirst()
            n -= 1
        }
        while digits.count > 1 && digits.hasSuffix("0") { digits.removeLast() }
        let k = digits.count
        if k <= n && n <= 21 { return digits + String(repeating: "0", count: n - k) }
        if 0 < n && n <= 21 {
            let i = digits.index(digits.startIndex, offsetBy: n)
            return String(digits[digits.startIndex..<i]) + "." + String(digits[i...])
        }
        if -6 < n && n <= 0 { return "0." + String(repeating: "0", count: -n) + digits }
        let e = n - 1
        let es = e >= 0 ? "+\(e)" : "\(e)"
        if k == 1 { return "\(digits)e\(es)" }
        return "\(digits.prefix(1)).\(digits.dropFirst())e\(es)"
    }

    /// A value as JSON.stringify writes it: for a tool's size, counted in UTF-16 units as JavaScript counts it.
    static func jsonText(_ v: ToolJSON) -> String {
        switch v {
        case .null, .other: return "null"
        case .bool(let b): return b ? "true" : "false"
        case .number(let n): return n.isFinite ? jsString(n) : "null"
        case .string(let s): return quote(s)
        case .array(let a): return "[" + a.map(jsonText).joined(separator: ",") + "]"
        case .object(let o): return "{" + o.map { "\(quote($0.key)):\(jsonText($0.value))" }.joined(separator: ",") + "}"
        }
    }

    private static func quote(_ s: String) -> String {
        var out = "\""
        for u in s.unicodeScalars {
            switch u.value {
            case 0x22: out += "\\\""
            case 0x5C: out += "\\\\"
            case 0x08: out += "\\b"
            case 0x0C: out += "\\f"
            case 0x0A: out += "\\n"
            case 0x0D: out += "\\r"
            case 0x09: out += "\\t"
            case 0..<0x20: out += String(format: "\\u%04x", u.value)
            default: out.unicodeScalars.append(u)
            }
        }
        return out + "\""
    }

    // MARK: - Text, as the reference cleans it

    /// JavaScript's white space (\s, and what trim() takes off): never U+0085.
    private static func isJSSpace(_ u: Unicode.Scalar) -> Bool {
        switch u.value {
        case 0x09...0x0D, 0x20, 0xA0, 0x1680, 0x2000...0x200A, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000, 0xFEFF: return true
        default: return false
        }
    }

    /// JavaScript's trim().
    static func jsTrim(_ s: String) -> String {
        let u = Array(s.unicodeScalars)
        guard let first = u.firstIndex(where: { !isJSSpace($0) }), let last = u.lastIndex(where: { !isJSSpace($0) }) else { return "" }
        var out = String.UnicodeScalarView()
        out.append(contentsOf: u[first...last])
        return String(out)
    }

    /// Text as a tool keeps it: a string's control characters dropped, its runs of white space one space, trimmed (not
    /// a string: nothing).
    static func clean(_ v: ToolJSON?) -> String {
        guard let s = v?.stringValue else { return "" }
        var out = String.UnicodeScalarView()
        var gap = false
        for u in s.unicodeScalars {
            if u.value < 0x20 || u.value == 0x7F || isJSSpace(u) {
                gap = true
                continue
            }
            if gap && !out.isEmpty { out.append(" ") }
            gap = false
            out.append(u)
        }
        return String(out)
    }

    /// Text cut at `n` UTF-16 units, as JavaScript's slice cuts it.
    static func cut(_ s: String, _ n: Int) -> String {
        let u = s.utf16
        guard u.count > n else { return s }
        return String(decoding: Array(u.prefix(n)), as: UTF16.self)
    }

    private static func isID(_ s: String) -> Bool {
        let u = Array(s.utf8)
        guard (1...16).contains(u.count), (0x61...0x7A).contains(u[0]) else { return false }
        return u.dropFirst().allSatisfy { (0x61...0x7A).contains($0) || (0x30...0x39).contains($0) || $0 == 0x5F }
    }

    private static func isSiteID(_ s: String) -> Bool {
        let u = Array(s.utf8)
        return (3...24).contains(u.count) && u.allSatisfy { (0x61...0x7A).contains($0) || (0x30...0x39).contains($0) || $0 == 0x2D }
    }

    private static func isDate(_ s: String) -> Bool {
        let u = Array(s.utf8)
        guard u.count == 10, u[4] == 0x2D, u[7] == 0x2D else { return false }
        return u.enumerated().allSatisfy { i, c in i == 4 || i == 7 || (0x30...0x39).contains(c) }
    }

    private static func isColor(_ s: String) -> Bool {
        let u = Array(s.utf8)
        guard u.count == 7, u[0] == 0x23 else { return false }
        return u.dropFirst().allSatisfy { (0x30...0x39).contains($0) || (0x61...0x66).contains($0) }
    }

    /// Whether `t.format > 1`, as JavaScript compares it (a number, or a string read as one).
    private static func later(_ v: ToolJSON?) -> Bool {
        switch v {
        case .number(let n)?: return n > Double(format)
        case .string(let s)?:
            let t = jsTrim(s)
            if t.isEmpty { return false }
            if let n = Double(t) { return n > Double(format) }
            return t == "Infinity" || t == "+Infinity"
        default: return false
        }
    }

    /// What JavaScript takes for an object here: an object's keys, or none of an array's (typeof [] is 'object').
    private static func objectLike(_ v: ToolJSON?) -> [String: ToolJSON]? {
        switch v {
        case .object(let o)?: return o
        case .array?: return [:]
        default: return nil
        }
    }

    private static let topKeys: Set<String> = ["format", "id", "version", "name", "maker", "summary", "group", "symbol", "color", "inputs", "outputs", "table", "note", "created"]
    private static let inputKeys: Set<String> = ["id", "kind", "label", "unit", "default", "min", "max", "step", "options"]
    private static let outputKeys: Set<String> = ["id", "label", "value", "unit", "decimals", "when"]

    private static func fail(_ code: String, _ message: String, _ path: String) -> Result<Tool, Refusal> {
        .failure(Refusal(code: code, message: message, path: path))
    }

    // MARK: - A tool: checked

    /// A tool sent as text: refused when it is too big or cannot be read, else checked.
    static func check(text: String, strict: Bool = false) -> Result<Tool, Refusal> {
        if text.utf16.count > Limits.json { return fail("SC-T-BIG", "That tool is too big.", "") }
        guard let raw = try? JSONDecoder().decode(ToolJSON.self, from: Data(text.utf8)) else { return fail("SC-T-JSON", "That tool could not be read.", "") }
        return checked(raw, strict: strict)
    }

    /// A tool checked against format 1: the tool (text trimmed and cut, defaults filled in), or why it is refused.
    /// `strict` (the site, on a tool sent in) refuses keys format 1 does not have; the apps read leniently and pass over
    /// them, so a later format's extra keys do not stop an older app.
    static func check(_ raw: ToolJSON, strict: Bool = false) -> Result<Tool, Refusal> {
        if jsonText(raw).utf16.count > Limits.json { return fail("SC-T-BIG", "That tool is too big.", "") }
        return checked(raw, strict: strict)
    }

    private static func checked(_ raw: ToolJSON, strict: Bool) -> Result<Tool, Refusal> {
        guard case .object(let t) = raw else { return fail("SC-T-JSON", "That tool could not be read.", "") }
        guard t["format"] == .number(Double(format)) else {
            return fail("SC-T-FORMAT", later(t["format"]) ? newerMessage : "That is not a Simpl tool.", "format")
        }
        if strict, let k = t.keys.sorted().first(where: { !topKeys.contains($0) }) { return fail("SC-T-KEY", "“\(k)” is not part of a tool.", k) }

        let name = cut(clean(t["name"]), Limits.name)
        if name.isEmpty { return fail("SC-T-NAME", "Give the tool a name.", "name") }
        let summary = cut(clean(t["summary"]), Limits.summary)
        if summary.isEmpty { return fail("SC-T-SUMMARY", "Say in a line what the tool does.", "summary") }
        guard let group = t["group"]?.stringValue, groups.contains(group) else {
            return fail("SC-T-GROUP", "The group must be one of: \(groups.joined(separator: ", ")).", "group")
        }
        guard let symbol = t["symbol"]?.stringValue, symbols.contains(symbol) else { return fail("SC-T-SYMBOL", "That symbol is not one of Simpl’s.", "symbol") }
        let color = t["color"]?.stringValue?.lowercased() ?? ""
        if !isColor(color) { return fail("SC-T-COLOUR", "The colour must be like #3478f6.", "color") }

        var seen = Set<String>()
        // (an id taken: nil and no refusal for an optional one that is absent)
        func takeID(_ v: ToolJSON?, _ path: String, optional: Bool) -> (id: String?, refusal: Refusal?) {
            if optional && (v == nil || v == .null) { return (nil, nil) }
            guard let id = v?.stringValue, isID(id) else {
                return (nil, Refusal(code: "SC-T-ID", message: "A name in formulas is a lower-case letter, then letters, digits or _ (16 at most).", path: path))
            }
            if constants[id] != nil || functions[id] != nil { return (nil, Refusal(code: "SC-T-ID", message: "“\(id)” is a name Simpl keeps for itself.", path: path)) }
            if seen.contains(id) { return (nil, Refusal(code: "SC-T-ID", message: "“\(id)” is used twice.", path: path)) }
            seen.insert(id)
            return (id, nil)
        }

        guard let rawInputs = t["inputs"]?.arrayValue, (1...Limits.inputs).contains(rawInputs.count) else {
            return fail("SC-T-INPUTS", "A tool has 1 to \(Limits.inputs) inputs.", "inputs")
        }
        var inputs: [Input] = []
        for (i, rawInput) in rawInputs.enumerated() {
            let path = "inputs.\(i)"
            guard let o = objectLike(rawInput) else { return fail("SC-T-INPUT", "That input could not be read.", path) }
            if strict {
                if case .array(let a) = rawInput, !a.isEmpty { return fail("SC-T-KEY", "“0” is not part of an input.", "\(path).0") }
                if let k = o.keys.sorted().first(where: { !inputKeys.contains($0) }) { return fail("SC-T-KEY", "“\(k)” is not part of an input.", "\(path).\(k)") }
            }
            let taken = takeID(o["id"], "\(path).id", optional: false)
            if let r = taken.refusal { return .failure(r) }
            guard let id = taken.id else { return fail("SC-T-ID", "That input has no name.", "\(path).id") }
            let label = cut(clean(o["label"]), Limits.label)
            if label.isEmpty { return fail("SC-T-LABEL", "Every input needs a label.", "\(path).label") }
            switch o["kind"]?.stringValue ?? "" {
            case "number":
                for k in ["default", "min", "max", "step"] {
                    if let v = o[k], v != .null, v.finite == nil { return fail("SC-T-NUMBER", "The input’s \(k) must be a number.", "\(path).\(k)") }
                }
                let min = o["min"]?.finite, max = o["max"]?.finite
                if let lo = min, let hi = max, lo > hi { return fail("SC-T-RANGE", "The input’s min is above its max.", path) }
                if let step = o["step"]?.finite, step <= 0 { return fail("SC-T-NUMBER", "The input’s step must be above 0.", "\(path).step") }
                let def = jsMin(max ?? .infinity, jsMax(min ?? -.infinity, o["default"]?.finite ?? 0))
                inputs.append(Input(id: id, kind: .number, label: label, unit: cut(clean(o["unit"]), Limits.unit), min: min, max: max,
                                    step: o["step"]?.finite ?? 1, number: def))
            case "choice":
                guard let rawOptions = o["options"]?.arrayValue, (2...Limits.options).contains(rawOptions.count) else {
                    return fail("SC-T-OPTIONS", "A choice has 2 to \(Limits.options) options.", "\(path).options")
                }
                var options: [Option] = []
                for (j, op) in rawOptions.enumerated() {
                    let ol = cut(clean(op["label"]), Limits.label)
                    guard !ol.isEmpty, let value = op["value"]?.finite else {
                        return fail("SC-T-OPTIONS", "Every option needs a label and a number for its value.", "\(path).options.\(j)")
                    }
                    if options.contains(where: { $0.value == value }) { return fail("SC-T-OPTIONS", "Two options have the same value.", "\(path).options.\(j)") }
                    options.append(Option(label: ol, value: value))
                }
                var def = options[0].value
                if let d = o["default"]?.finite, options.contains(where: { $0.value == d }) { def = d }
                inputs.append(Input(id: id, kind: .choice, label: label, options: options, number: def))
            case "toggle":
                inputs.append(Input(id: id, kind: .toggle, label: label, on: o["default"] == .bool(true)))
            default:
                return fail("SC-T-KIND", "An input is a number, a choice or a toggle.", "\(path).kind")
            }
        }

        guard let rawOutputs = t["outputs"]?.arrayValue, (1...Limits.outputs).contains(rawOutputs.count) else {
            return fail("SC-T-OUTPUTS", "A tool has 1 to \(Limits.outputs) results.", "outputs")
        }
        var known = Set(inputs.map(\.id)).union(constants.keys)
        let laterIDs = Set(rawOutputs.compactMap { $0["id"]?.stringValue })
        var outputs: [Output] = []
        for (i, rawOutput) in rawOutputs.enumerated() {
            let path = "outputs.\(i)"
            guard let o = objectLike(rawOutput) else { return fail("SC-T-OUTPUT", "That result could not be read.", path) }
            if strict {
                if case .array(let a) = rawOutput, !a.isEmpty { return fail("SC-T-KEY", "“0” is not part of a result.", "\(path).0") }
                if let k = o.keys.sorted().first(where: { !outputKeys.contains($0) }) { return fail("SC-T-KEY", "“\(k)” is not part of a result.", "\(path).\(k)") }
            }
            let label = cut(clean(o["label"]), Limits.label)
            if label.isEmpty { return fail("SC-T-LABEL", "Every result needs a label.", "\(path).label") }
            var decimals = 4
            if let dv = o["decimals"], dv != .null {
                guard let d = dv.finite, d.rounded(.towardZero) == d, d >= 0, d <= Double(Limits.decimals) else {
                    return fail("SC-T-DECIMALS", "Decimals are a whole number from 0 to \(Limits.decimals).", "\(path).decimals")
                }
                decimals = Int(d)
            }
            // the value reads the inputs and the results before this one; `when` may read this one's value too
            func formula(_ k: String) -> (ok: String?, refusal: Refusal?) {
                let src = o[k]?.stringValue ?? ""
                do {
                    let tree = try parse(src)
                    for n in try names(in: tree) where !known.contains(n) {
                        let why = laterIDs.contains(n) ? "“\(n)” is used before it is worked out." : "There is nothing called “\(n)”."
                        return (nil, Refusal(code: "SC-T-NAME-USE", message: why, path: "\(path).\(k)"))
                    }
                    return (jsTrim(src), nil)
                } catch {
                    let why = (error as? ToolSyntaxError)?.message ?? "That formula could not be read"
                    return (nil, Refusal(code: "SC-T-FORMULA", message: "\(why).", path: "\(path).\(k)"))
                }
            }
            let value = formula("value")
            if let r = value.refusal { return .failure(r) }
            let taken = takeID(o["id"], "\(path).id", optional: true)
            if let r = taken.refusal { return .failure(r) }
            if let id = taken.id { known.insert(id) }
            var when: String?
            if let w = o["when"], w != .null, w != .string("") {
                let got = formula("when")
                if let r = got.refusal { return .failure(r) }
                when = got.ok
            }
            outputs.append(Output(id: taken.id, label: label, value: value.ok ?? "", unit: cut(clean(o["unit"]), Limits.unit), decimals: decimals,
                                  when: when?.isEmpty == false ? when : nil))
        }

        var table: Table?
        if let tv = t["table"], tv != .null {
            guard let tb = objectLike(tv), let rawColumns = tb["columns"]?.arrayValue, let rawRows = tb["rows"]?.arrayValue else {
                return fail("SC-T-TABLE", "That table could not be read.", "table")
            }
            let columns = rawColumns.map { cut(clean($0), Limits.colName) }
            if columns.isEmpty || columns.count > Limits.tableCols || columns.contains(where: { $0.isEmpty }) {
                return fail("SC-T-TABLE", "A table has 1 to \(Limits.tableCols) named columns.", "table.columns")
            }
            if rawRows.isEmpty || rawRows.count > Limits.tableRows { return fail("SC-T-TABLE", "A table has 1 to \(Limits.tableRows) rows.", "table.rows") }
            var rows: [[String]] = []
            for (i, r) in rawRows.enumerated() {
                guard let cells = r.arrayValue, cells.count == columns.count else { return fail("SC-T-TABLE", "Every row has one cell per column.", "table.rows.\(i)") }
                rows.append(cells.map { (c: ToolJSON) -> String in
                    // (a number is written as text, as String(n) writes it; JSON.parse's Infinity is a number too)
                    switch c {
                    case .number(let n): return cut(clean(.string(jsString(n))), Limits.cell)
                    case .other: return "Infinity"
                    default: return cut(clean(c), Limits.cell)
                    }
                })
            }
            table = Table(title: cut(clean(tb["title"]), Limits.tableTitle), columns: columns, rows: rows)
        }

        let note = clean(t["note"])
        var version: Double?
        if let v = t["version"]?.finite, v.rounded(.towardZero) == v, v > 0 { version = v }
        let siteID = t["id"]?.stringValue.flatMap { isSiteID($0) ? $0 : nil }
        let created = t["created"]?.stringValue.flatMap { isDate($0) ? $0 : nil }
        return .success(Tool(siteID: siteID, version: version, name: name, maker: cut(clean(t["maker"]), Limits.maker), summary: summary,
                             group: group, symbol: symbol, color: color, inputs: inputs, outputs: outputs, table: table,
                             note: note.isEmpty ? nil : cut(note, Limits.note), created: created))
    }

    // MARK: - A tool: worked out

    /// What an input is worth in a formula: a number clamped to its range (not a number: its default), a choice's value
    /// (one not offered: its default), a toggle's 1 or 0 (a true or a 1 is on; nothing given, its default).
    static func inputValue(_ input: Input, _ given: ToolJSON?) -> Double {
        switch input.kind {
        case .toggle:
            guard let g = given else { return input.on ? 1 : 0 }
            return g == .bool(true) || g == .number(1) ? 1 : 0
        case .choice:
            if let g = given?.finite, input.options.contains(where: { $0.value == g }) { return g }
            return input.number
        case .number:
            let v = given?.finite ?? input.number
            return jsMin(input.max ?? .infinity, jsMax(input.min ?? -.infinity, v))
        }
    }

    /// A checked tool's results for these inputs (by id): each its label, its value and text, its unit and whether it
    /// is shown. Results are worked out in order; a hidden one still has its value.
    static func run(_ tool: Tool, given: [String: ToolJSON] = [:]) -> [Answer] {
        var env = constants
        for input in tool.inputs { env[input.id] = inputValue(input, given[input.id]) }
        return tool.outputs.map { o in
            let value = (try? parse(o.value)).map { evaluate($0, env) } ?? .nan
            if let id = o.id { env[id] = value }
            var shown = true
            if let w = o.when { shown = truthy((try? parse(w)).map { evaluate($0, env) } ?? .nan) }
            return Answer(label: o.label, value: value, text: formatNumber(value, o.decimals), unit: o.unit, shown: shown)
        }
    }
}

// MARK: - A checked tool as JSON (as the reference writes it: what is kept, and what the checks compare)

extension ToolFormat.Input {
    var json: ToolJSON {
        var o: [String: ToolJSON] = ["id": .string(id), "kind": .string(kind.rawValue), "label": .string(label)]
        switch kind {
        case .number:
            o["unit"] = .string(unit)
            o["default"] = .number(number)
            if let min { o["min"] = .number(min) }
            if let max { o["max"] = .number(max) }
            o["step"] = .number(step)
        case .choice:
            o["options"] = .array(options.map { .object(["label": .string($0.label), "value": .number($0.value)]) })
            o["default"] = .number(number)
        case .toggle:
            o["default"] = .bool(on)
        }
        return .object(o)
    }
}

extension ToolFormat.Output {
    var json: ToolJSON {
        var o: [String: ToolJSON] = ["label": .string(label), "value": .string(value), "unit": .string(unit), "decimals": .number(Double(decimals))]
        if let id { o["id"] = .string(id) }
        if let when { o["when"] = .string(when) }
        return .object(o)
    }
}

extension ToolFormat.Tool {
    var json: ToolJSON {
        var o: [String: ToolJSON] = [
            "format": .number(Double(ToolFormat.format)), "name": .string(name), "maker": .string(maker), "summary": .string(summary),
            "group": .string(group), "symbol": .string(symbol), "color": .string(color),
            "inputs": .array(inputs.map(\.json)), "outputs": .array(outputs.map(\.json)),
        ]
        if let siteID { o["id"] = .string(siteID) }
        if let version { o["version"] = .number(version) }
        if let table {
            o["table"] = .object(["title": .string(table.title), "columns": .array(table.columns.map { .string($0) }),
                                  "rows": .array(table.rows.map { .array($0.map { .string($0) }) })])
        }
        if let note { o["note"] = .string(note) }
        if let created { o["created"] = .string(created) }
        return .object(o)
    }
}

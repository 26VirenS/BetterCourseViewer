import Foundation

// (tools) The Matrix Calculator's arithmetic (Foundation only, checked by the Models job against the specification's
// vectors): matrices of up to 6 × 6 whose cells are exact fractions when what was typed is whole numbers, fractions or
// short decimals (Int64 numerators and denominators, a sum or product that would overflow falling back to a decimal),
// decimals otherwise; sums, differences, products, a scalar multiple, the transpose, the determinant and inverse by
// elimination, the rank and the reduced row echelon form with every row operation listed, the trace, A·x = b solved
// through the augmented matrix, powers, and the eigenvalues — real and complex — by reduction to Hessenberg form and
// the shifted QR algorithm (EISPACK's hqr as Numerical Recipes gives it).

/// An exact fraction: numerator over a positive denominator, in lowest terms. An operation whose result does not fit
/// in 62 bits gives nil, and the matrix arithmetic goes on in decimals.
struct Fraction: Hashable, CustomStringConvertible {
    let num: Int64
    let den: Int64

    /// The largest magnitude kept exact (well inside Int64, so every intermediate product is checked, not trusted).
    private static let limit: UInt64 = 1 << 62

    init?(_ n: Int64, _ d: Int64 = 1) {
        guard d != 0, n.magnitude < Fraction.limit, d.magnitude < Fraction.limit else { return nil }
        var a = n.magnitude, b = d.magnitude
        while b != 0 { (a, b) = (b, a % b) }
        let g = Int64(max(a, 1))
        let sign: Int64 = (n < 0) != (d < 0) ? -1 : 1
        num = sign * (n.magnitude == 0 ? 0 : Int64(n.magnitude) / g)
        den = Int64(d.magnitude) / g
    }

    /// A fraction read from what was typed: "3", "-2", "3/4", "0.25", "1,5" (nil: not exact, or not a number at
    /// all — "1e5", "π", "abc").
    init?(text: String) {
        let t = text.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "−", with: "-")
        guard !t.isEmpty, !t.lowercased().contains("e") else { return nil }
        if let slash = t.firstIndex(of: "/") {
            guard let n = Int64(t[t.startIndex..<slash].trimmingCharacters(in: .whitespaces)),
                  let d = Int64(t[t.index(after: slash)...].trimmingCharacters(in: .whitespaces)), d != 0 else { return nil }
            self.init(n, d)
            return
        }
        if let n = Int64(t) {
            self.init(n, 1)
            return
        }
        let parts = t.replacingOccurrences(of: ",", with: ".").split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 2, parts[1].count <= 12, parts[1].allSatisfy(\.isNumber) else { return nil }
        let whole = parts[0].isEmpty || parts[0] == "-" ? "0" : String(parts[0])
        guard let w = Int64(whole), let frac = Int64(parts[1]) else { return nil }
        var den: Int64 = 1
        for _ in 0..<parts[1].count { den *= 10 }
        let negative = t.hasPrefix("-")
        guard w.magnitude < Fraction.limit else { return nil }
        let scaled = Int64(w.magnitude).multipliedReportingOverflow(by: den)
        guard !scaled.overflow else { return nil }
        let n = scaled.partialValue.addingReportingOverflow(frac)
        guard !n.overflow else { return nil }
        self.init(negative ? -n.partialValue : n.partialValue, den)
    }

    static let zero = Fraction(0)!
    static let one = Fraction(1)!

    var isZero: Bool { num == 0 }
    var double: Double { Double(num) / Double(den) }

    /// "3/2", "−2", "0".
    var description: String {
        let n = num < 0 ? "−\(num.magnitude)" : "\(num)"
        return den == 1 ? n : "\(n)/\(den)"
    }

    static func + (a: Fraction, b: Fraction) -> Fraction? {
        let (x, o1) = a.num.multipliedReportingOverflow(by: b.den)
        let (y, o2) = b.num.multipliedReportingOverflow(by: a.den)
        let (s, o3) = x.addingReportingOverflow(y)
        let (d, o4) = a.den.multipliedReportingOverflow(by: b.den)
        guard !o1, !o2, !o3, !o4 else { return nil }
        return Fraction(s, d)
    }

    static prefix func - (a: Fraction) -> Fraction { Fraction(-a.num, a.den) ?? .zero }
    static func - (a: Fraction, b: Fraction) -> Fraction? { a + (-b) }

    static func * (a: Fraction, b: Fraction) -> Fraction? {
        let (n, o1) = a.num.multipliedReportingOverflow(by: b.num)
        let (d, o2) = a.den.multipliedReportingOverflow(by: b.den)
        guard !o1, !o2 else { return nil }
        return Fraction(n, d)
    }

    static func / (a: Fraction, b: Fraction) -> Fraction? {
        guard !b.isZero else { return nil }
        return a * (Fraction(b.den, b.num) ?? .zero)
    }
}

/// A matrix cell: an exact fraction while it can be, a decimal once anything inexact has come in.
enum MNum: Hashable {
    case exact(Fraction)
    case approx(Double)

    static let zero = MNum.exact(.zero)
    static let one = MNum.exact(.one)

    var double: Double {
        switch self {
        case .exact(let f): return f.double
        case .approx(let d): return d
        }
    }

    var isExact: Bool { if case .exact = self { return true } else { return false } }
    var isZero: Bool {
        switch self {
        case .exact(let f): return f.isZero
        case .approx(let d): return abs(d) < 1e-12
        }
    }

    /// What was typed, read: a fraction when it is exact, else a decimal (nil: not a number).
    init?(text: String) {
        if let f = Fraction(text: text) { self = .exact(f); return }
        guard let d = StudyMath.number(text) else { return nil }
        self = .approx(d)
    }

    /// Shown as a fraction where it is exact (unless decimals are asked for), else to six figures.
    func text(decimals: Bool = false) -> String {
        if case .exact(let f) = self, !decimals { return f.description }
        return StudyMath.text(double)
    }

    private static func join(_ a: MNum, _ b: MNum, exact: (Fraction, Fraction) -> Fraction?, approx: (Double, Double) -> Double) -> MNum {
        if case .exact(let x) = a, case .exact(let y) = b, let r = exact(x, y) { return .exact(r) }
        return .approx(approx(a.double, b.double))
    }

    static func + (a: MNum, b: MNum) -> MNum { join(a, b, exact: +, approx: +) }
    static func - (a: MNum, b: MNum) -> MNum { join(a, b, exact: -, approx: -) }
    static func * (a: MNum, b: MNum) -> MNum { join(a, b, exact: *, approx: *) }
    /// Division by zero gives nil.
    static func / (a: MNum, b: MNum) -> MNum? {
        guard !b.isZero else { return nil }
        return join(a, b, exact: /, approx: /)
    }
    static prefix func - (a: MNum) -> MNum {
        switch a {
        case .exact(let f): return .exact(-f)
        case .approx(let d): return .approx(-d)
        }
    }
}

/// A matrix: its cells row by row.
struct Matrix: Hashable {
    let rows: Int
    let cols: Int
    var cells: [MNum]

    init(rows: Int, cols: Int, cells: [MNum]) {
        self.rows = rows
        self.cols = cols
        self.cells = cells.count == rows * cols ? cells : [MNum](repeating: .zero, count: rows * cols)
    }

    /// A matrix read from text: rows set apart by ";" or new lines, cells by spaces, commas or tabs ("1 2;3 4"); nil
    /// when a row is a different length from the first, a cell is not a number, or there is nothing.
    init?(text: String) {
        let lines = text.replacingOccurrences(of: ";", with: "\n").split(whereSeparator: \.isNewline)
            .map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        guard !lines.isEmpty else { return nil }
        var cells: [MNum] = []
        var width = 0
        for (i, line) in lines.enumerated() {
            let parts = line.replacingOccurrences(of: ",", with: " ").replacingOccurrences(of: "\t", with: " ").split(separator: " ").map(String.init)
            if i == 0 { width = parts.count } else if parts.count != width { return nil }
            for p in parts {
                guard let n = MNum(text: p) else { return nil }
                cells.append(n)
            }
        }
        guard width > 0 else { return nil }
        self.init(rows: lines.count, cols: width, cells: cells)
    }

    static func identity(_ n: Int) -> Matrix {
        var m = Matrix(rows: n, cols: n, cells: [])
        for i in 0..<n { m[i, i] = .one }
        return m
    }

    subscript(r: Int, c: Int) -> MNum {
        get { cells[r * cols + c] }
        set { cells[r * cols + c] = newValue }
    }

    var isSquare: Bool { rows == cols }
    var isExact: Bool { cells.allSatisfy(\.isExact) }
    var doubles: [[Double]] { (0..<rows).map { r in (0..<cols).map { self[r, $0].double } } }

    /// Written as it is read: rows set apart by ";", cells by spaces.
    var text: String { (0..<rows).map { r in (0..<cols).map { self[r, $0].text().replacingOccurrences(of: "−", with: "-") }.joined(separator: " ") }.joined(separator: ";") }

    func row(_ r: Int) -> [MNum] { Array(cells[(r * cols)..<((r + 1) * cols)]) }
}

/// The result of one row operation in an elimination.
struct RowStep: Hashable {
    /// "R₂ ← R₂ − 4R₁", "R₁ ↔ R₂", "R₂ ← R₂ ÷ 3".
    let text: String
    let after: Matrix
}

enum MatrixMath {
    /// The matrix with rows r and s swapped.
    static func swapped(_ m: Matrix, _ r: Int, _ s: Int) -> Matrix {
        var out = m
        for c in 0..<m.cols { (out[r, c], out[s, c]) = (m[s, c], m[r, c]) }
        return out
    }

    static func sub(_ i: Int) -> String {
        let digits: [Character] = ["₀", "₁", "₂", "₃", "₄", "₅", "₆", "₇", "₈", "₉"]
        return String("\(i)".map { digits[Int(String($0))!] })
    }

    /// The reduced row echelon form, every row operation listed with the matrix after it, and the pivot columns.
    static func rref(_ m: Matrix) -> (result: Matrix, steps: [RowStep], pivots: [Int]) {
        var a = m
        var steps: [RowStep] = []
        var pivots: [Int] = []
        var r = 0
        for c in 0..<a.cols where r < a.rows {
            // (the pivot: an exact matrix takes the first non-zero; a decimal one the largest, for the sake of its
            // rounding)
            var p = -1
            if a.isExact {
                for i in r..<a.rows where !a[i, c].isZero { p = i; break }
            } else {
                var best = 1e-12
                for i in r..<a.rows where abs(a[i, c].double) > best { best = abs(a[i, c].double); p = i }
            }
            guard p >= 0 else { continue }
            if p != r {
                a = swapped(a, p, r)
                steps.append(RowStep(text: "R\(sub(r + 1)) ↔ R\(sub(p + 1))", after: a))
            }
            let pivot = a[r, c]
            if pivot.double != 1 {
                for j in 0..<a.cols { a[r, j] = (a[r, j] / pivot) ?? .approx(.nan) }
                steps.append(RowStep(text: "R\(sub(r + 1)) ← R\(sub(r + 1)) ÷ \(pivot.text())", after: a))
            }
            for i in 0..<a.rows where i != r && !a[i, c].isZero {
                let f = a[i, c]
                for j in 0..<a.cols { a[i, j] = a[i, j] - f * a[r, j] }
                let neg = f.double < 0
                let mag = (neg ? -f : f).text()
                let times = mag == "1" ? "" : mag
                steps.append(RowStep(text: "R\(sub(i + 1)) ← R\(sub(i + 1)) \(neg ? "+" : "−") \(times)R\(sub(r + 1))", after: a))
            }
            pivots.append(c)
            r += 1
        }
        return (a, steps, pivots)
    }

    static func rank(_ m: Matrix) -> Int { rref(m).pivots.count }

    /// The determinant by elimination (nil: not square).
    static func determinant(_ m: Matrix) -> MNum? {
        guard m.isSquare else { return nil }
        var a = m
        var det = MNum.one
        let n = a.rows
        for c in 0..<n {
            var p = -1
            if a.isExact {
                for i in c..<n where !a[i, c].isZero { p = i; break }
            } else {
                var best = 1e-12
                for i in c..<n where abs(a[i, c].double) > best { best = abs(a[i, c].double); p = i }
            }
            guard p >= 0 else { return .zero }
            if p != c {
                a = swapped(a, p, c)
                det = -det
            }
            let pivot = a[c, c]
            det = det * pivot
            for i in (c + 1)..<n where !a[i, c].isZero {
                let f = (a[i, c] / pivot) ?? .approx(.nan)
                for j in c..<n { a[i, j] = a[i, j] - f * a[c, j] }
            }
        }
        return det
    }

    /// The inverse by Gauss–Jordan on [A | I] (nil: not square, or singular).
    static func inverse(_ m: Matrix) -> Matrix? {
        guard m.isSquare else { return nil }
        let n = m.rows
        var aug = Matrix(rows: n, cols: 2 * n, cells: [])
        for r in 0..<n {
            for c in 0..<n { aug[r, c] = m[r, c] }
            aug[r, n + r] = .one
        }
        let red = rref(aug)
        guard red.pivots.count >= n, Array(red.pivots.prefix(n)) == Array(0..<n) else { return nil }
        var inv = Matrix(rows: n, cols: n, cells: [])
        for r in 0..<n { for c in 0..<n { inv[r, c] = red.result[r, n + c] } }
        return inv
    }

    static func transpose(_ m: Matrix) -> Matrix {
        var t = Matrix(rows: m.cols, cols: m.rows, cells: [])
        for r in 0..<m.rows { for c in 0..<m.cols { t[c, r] = m[r, c] } }
        return t
    }

    static func trace(_ m: Matrix) -> MNum? {
        guard m.isSquare else { return nil }
        return (0..<m.rows).reduce(MNum.zero) { $0 + m[$1, $1] }
    }

    static func add(_ a: Matrix, _ b: Matrix) -> Matrix? {
        guard a.rows == b.rows, a.cols == b.cols else { return nil }
        return Matrix(rows: a.rows, cols: a.cols, cells: zip(a.cells, b.cells).map { $0 + $1 })
    }

    static func subtract(_ a: Matrix, _ b: Matrix) -> Matrix? {
        guard a.rows == b.rows, a.cols == b.cols else { return nil }
        return Matrix(rows: a.rows, cols: a.cols, cells: zip(a.cells, b.cells).map { $0 - $1 })
    }

    static func multiply(_ a: Matrix, _ b: Matrix) -> Matrix? {
        guard a.cols == b.rows else { return nil }
        var p = Matrix(rows: a.rows, cols: b.cols, cells: [])
        for r in 0..<a.rows {
            for c in 0..<b.cols {
                var s = MNum.zero
                for k in 0..<a.cols { s = s + a[r, k] * b[k, c] }
                p[r, c] = s
            }
        }
        return p
    }

    static func scale(_ a: Matrix, by k: MNum) -> Matrix {
        Matrix(rows: a.rows, cols: a.cols, cells: a.cells.map { $0 * k })
    }

    /// Aⁿ for a whole n: the identity for 0, the inverse's power for a negative n (nil: not square, or a negative
    /// power of a singular matrix; a power past 60 is not attempted).
    static func power(_ a: Matrix, _ n: Int) -> Matrix? {
        guard a.isSquare, n.magnitude <= 60 else { return nil }
        if n == 0 { return .identity(a.rows) }
        guard let base = n > 0 ? a : inverse(a) else { return nil }
        var result = base
        for _ in 1..<n.magnitude { result = multiply(result, base) ?? result }
        return result
    }

    /// How A·x = b came out.
    enum Solution: Hashable {
        case unique(Matrix)
        case noSolution
        case many(free: Int)
    }

    /// A·x = b solved through the augmented matrix [A | b] (nil: b is not a column with a row for each of A's).
    static func solve(_ a: Matrix, _ b: Matrix) -> Solution? {
        guard b.cols == 1, b.rows == a.rows else { return nil }
        var aug = Matrix(rows: a.rows, cols: a.cols + 1, cells: [])
        for r in 0..<a.rows {
            for c in 0..<a.cols { aug[r, c] = a[r, c] }
            aug[r, a.cols] = b[r, 0]
        }
        let red = rref(aug)
        if red.pivots.contains(a.cols) { return .noSolution }
        if red.pivots.count < a.cols { return .many(free: a.cols - red.pivots.count) }
        var x = Matrix(rows: a.cols, cols: 1, cells: [])
        for (i, c) in red.pivots.enumerated() { x[c, 0] = red.result[i, a.cols] }
        return .unique(x)
    }

    // MARK: Eigenvalues

    /// A complex number, for an eigenvalue.
    struct Complex: Hashable {
        let re: Double
        let im: Double

        /// "3", "2 + 3i", "−1.5 − 2i", "±i" written by `pairText`.
        var text: String {
            if im == 0 { return StudyMath.text(re) }
            let imag = abs(im) == 1 ? "" : StudyMath.text(abs(im))
            if re == 0 { return "\(im < 0 ? "−" : "")\(imag)i" }
            return "\(StudyMath.text(re)) \(im < 0 ? "−" : "+") \(imag)i"
        }
    }

    /// The eigenvalues of a square matrix, real ones first in rising order and complex pairs after them (nil: not
    /// square, or the iteration did not settle).
    static func eigenvalues(_ m: Matrix) -> [Complex]? {
        guard m.isSquare, m.rows > 0 else { return nil }
        let n = m.rows
        if n == 1 { return [Complex(re: m[0, 0].double, im: 0)] }
        // (1-based, as the algorithm is written)
        var a = [[Double]](repeating: [Double](repeating: 0, count: n + 1), count: n + 1)
        for i in 1...n { for j in 1...n { a[i][j] = m[i - 1, j - 1].double } }
        hessenberg(&a, n)
        guard var values = hqr(&a, n) else { return nil }
        // (what the rounding left of a real or a whole number: cleaned to the matrix's own scale)
        let scale = max(m.cells.map { abs($0.double) }.max() ?? 1, 1)
        values = values.map { v in
            var re = v.re, im = v.im
            if abs(im) < 1e-9 * scale { im = 0 }
            if abs(re - re.rounded()) < 1e-9 * scale { re = re.rounded() }
            if abs(im - im.rounded()) < 1e-9 * scale { im = im.rounded() }
            return Complex(re: re, im: im)
        }
        return values.sorted { x, y in
            if (x.im == 0) != (y.im == 0) { return x.im == 0 }
            if x.re != y.re { return x.re < y.re }
            return x.im > y.im
        }
    }

    /// The eigenvalues as shown: a conjugate pair written once, "2 ± 3i".
    static func pairText(_ values: [Complex]) -> [String] {
        var out: [String] = []
        var used = Set<Int>()
        for (i, v) in values.enumerated() where !used.contains(i) {
            used.insert(i)
            if v.im != 0, let j = values.indices.first(where: { !used.contains($0) && values[$0].re == v.re && values[$0].im == -v.im }) {
                used.insert(j)
                let imag = abs(v.im) == 1 ? "" : StudyMath.text(abs(v.im))
                out.append(v.re == 0 ? "±\(imag)i" : "\(StudyMath.text(v.re)) ± \(imag)i")
            } else {
                out.append(v.text)
            }
        }
        return out
    }

    /// Reduction to upper Hessenberg form by elimination with pivoting (Numerical Recipes' elmhes), 1-based; what is
    /// left under the subdiagonal is cleared for hqr.
    private static func hessenberg(_ a: inout [[Double]], _ n: Int) {
        if n > 2 {
            for m in 2..<n {
                var x = 0.0
                var i = m
                for j in m...n where abs(a[j][m - 1]) > abs(x) {
                    x = a[j][m - 1]
                    i = j
                }
                if i != m {
                    for j in (m - 1)...n {
                        let held = a[i][j]
                        a[i][j] = a[m][j]
                        a[m][j] = held
                    }
                    for j in 1...n {
                        let held = a[j][i]
                        a[j][i] = a[j][m]
                        a[j][m] = held
                    }
                }
                if x != 0 {
                    for i in (m + 1)...n {
                        var y = a[i][m - 1]
                        if y != 0 {
                            y /= x
                            a[i][m - 1] = y
                            for j in m...n { a[i][j] -= y * a[m][j] }
                            for j in 1...n { a[j][m] += y * a[j][i] }
                        }
                    }
                }
            }
        }
        for i in 1...n { for j in 1...n where j < i - 1 { a[i][j] = 0 } }
    }

    /// The eigenvalues of an upper Hessenberg matrix by the shifted QR algorithm (Numerical Recipes' hqr), 1-based;
    /// nil when 30 iterations do not find one.
    private static func hqr(_ a: inout [[Double]], _ n: Int) -> [Complex]? {
        var wr = [Double](repeating: 0, count: n + 1), wi = [Double](repeating: 0, count: n + 1)
        var anorm = 0.0
        for i in 1...n { for j in max(i - 1, 1)...n { anorm += abs(a[i][j]) } }
        var nn = n
        var t = 0.0
        var p = 0.0, q = 0.0, r = 0.0, s = 0.0, w = 0.0, x = 0.0, y = 0.0, z = 0.0
        while nn >= 1 {
            var its = 0
            var l = 1
            repeat {
                l = 1
                var k = nn
                while k >= 2 {
                    s = abs(a[k - 1][k - 1]) + abs(a[k][k])
                    if s == 0 { s = anorm }
                    if abs(a[k][k - 1]) + s == s {
                        a[k][k - 1] = 0
                        l = k
                        break
                    }
                    k -= 1
                }
                x = a[nn][nn]
                if l == nn {
                    wr[nn] = x + t
                    wi[nn] = 0
                    nn -= 1
                } else {
                    y = a[nn - 1][nn - 1]
                    w = a[nn][nn - 1] * a[nn - 1][nn]
                    if l == nn - 1 {
                        p = 0.5 * (y - x)
                        q = p * p + w
                        z = abs(q).squareRoot()
                        x += t
                        if q >= 0 {
                            z = p + (p >= 0 ? abs(z) : -abs(z))
                            wr[nn - 1] = x + z
                            wr[nn] = wr[nn - 1]
                            if z != 0 { wr[nn] = x - w / z }
                            wi[nn - 1] = 0
                            wi[nn] = 0
                        } else {
                            wr[nn - 1] = x + p
                            wr[nn] = x + p
                            wi[nn - 1] = -z
                            wi[nn] = z
                        }
                        nn -= 2
                    } else {
                        if its == 30 { return nil }
                        if its == 10 || its == 20 {
                            t += x
                            for i in 1...nn { a[i][i] -= x }
                            s = abs(a[nn][nn - 1]) + abs(a[nn - 1][nn - 2])
                            x = 0.75 * s
                            y = x
                            w = -0.4375 * s * s
                        }
                        its += 1
                        var m = nn - 2
                        while m >= l {
                            z = a[m][m]
                            r = x - z
                            s = y - z
                            p = (r * s - w) / a[m + 1][m] + a[m][m + 1]
                            q = a[m + 1][m + 1] - z - r - s
                            r = a[m + 2][m + 1]
                            s = abs(p) + abs(q) + abs(r)
                            p /= s
                            q /= s
                            r /= s
                            if m == l { break }
                            let u = abs(a[m][m - 1]) * (abs(q) + abs(r))
                            let v = abs(p) * (abs(a[m - 1][m - 1]) + abs(z) + abs(a[m + 1][m + 1]))
                            if u + v == v { break }
                            m -= 1
                        }
                        for i in (m + 2)...nn {
                            a[i][i - 2] = 0
                            if i != m + 2 { a[i][i - 3] = 0 }
                        }
                        for k in m...(nn - 1) {
                            if k != m {
                                p = a[k][k - 1]
                                q = a[k + 1][k - 1]
                                r = 0
                                if k != nn - 1 { r = a[k + 2][k - 1] }
                                x = abs(p) + abs(q) + abs(r)
                                if x != 0 {
                                    p /= x
                                    q /= x
                                    r /= x
                                }
                            }
                            let root = (p * p + q * q + r * r).squareRoot()
                            s = p >= 0 ? root : -root
                            if s != 0 {
                                if k == m {
                                    if l != m { a[k][k - 1] = -a[k][k - 1] }
                                } else {
                                    a[k][k - 1] = -s * x
                                }
                                p += s
                                x = p / s
                                y = q / s
                                z = r / s
                                q /= p
                                r /= p
                                for j in k...nn {
                                    p = a[k][j] + q * a[k + 1][j]
                                    if k != nn - 1 {
                                        p += r * a[k + 2][j]
                                        a[k + 2][j] -= p * z
                                    }
                                    a[k + 1][j] -= p * y
                                    a[k][j] -= p * x
                                }
                                let mmin = nn < k + 3 ? nn : k + 3
                                for i in l...mmin {
                                    p = x * a[i][k] + y * a[i][k + 1]
                                    if k != nn - 1 {
                                        p += z * a[i][k + 2]
                                        a[i][k + 2] -= p * r
                                    }
                                    a[i][k + 1] -= p * q
                                    a[i][k] -= p
                                }
                            }
                        }
                    }
                }
            } while l < nn - 1
        }
        return (1...n).map { Complex(re: wr[$0], im: wi[$0]) }
    }
}

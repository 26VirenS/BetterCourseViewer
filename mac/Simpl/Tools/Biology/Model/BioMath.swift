import Foundation

// The biology toolkit's shared arithmetic (spec §5.6, §6.4): vectors in ångströms or micrometres, the dihedral angle
// with the IUPAC sign and NeRF placement (the peptide builder's and the χ angles'), SplitMix64 (every seeded picture,
// the same on Windows), exact fractions (the crosses' ratios) and the chi-square test with its p-value. Foundation
// only — no simd, no AppKit — so the Models job (scripts/dev/swift-checks) checks it on its own.

/// A point or a direction in a molecule (Å) or a cell (µm).
typealias BioVec = SIMD3<Double>

/// Vector arithmetic on `BioVec`, by name (the standard library's SIMD3 has no dot or cross of its own, and simd is
/// not on every platform the checks run on).
enum BioV {
    static func dot(_ a: BioVec, _ b: BioVec) -> Double { a.x * b.x + a.y * b.y + a.z * b.z }
    static func cross(_ a: BioVec, _ b: BioVec) -> BioVec {
        BioVec(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x)
    }
    static func length(_ a: BioVec) -> Double { dot(a, a).squareRoot() }
    static func distance(_ a: BioVec, _ b: BioVec) -> Double { length(a - b) }
    /// `a` made one long (a zero vector stays zero).
    static func unit(_ a: BioVec) -> BioVec {
        let l = length(a)
        return l > 1e-12 ? a / l : BioVec(0, 0, 0)
    }
    /// The angle between two directions, in degrees.
    static func angle(_ a: BioVec, _ b: BioVec) -> Double {
        let c = dot(unit(a), unit(b))
        return acos(min(max(c, -1), 1)) * 180 / .pi
    }
    /// The angle a–b–c at b, in degrees.
    static func angle(_ a: BioVec, _ b: BioVec, _ c: BioVec) -> Double { angle(a - b, c - b) }

    /// The dihedral (torsion) angle a–b–c–d in degrees, with the IUPAC sign (§6.4): looking down b→c, positive when
    /// the near bond a–b must turn clockwise to eclipse the far bond c–d. A sign error here makes every right-handed
    /// helix left-handed.
    static func dihedral(_ a: BioVec, _ b: BioVec, _ c: BioVec, _ d: BioVec) -> Double {
        let b0 = a - b
        let b1 = unit(c - b)
        let b2 = d - c
        let v = b0 - dot(b0, b1) * b1
        let w = b2 - dot(b2, b1) * b1
        return atan2(dot(cross(b1, v), w), dot(v, w)) * 180 / .pi
    }

    /// NeRF (§6.4): the atom D bonded to C, `length` from it, with the angle B–C–D `angle`° and the dihedral A–B–C–D
    /// `torsion`°.
    static func place(_ a: BioVec, _ b: BioVec, _ c: BioVec, length: Double, angle: Double, torsion: Double) -> BioVec {
        let bc = unit(c - b)
        let n = unit(cross(b - a, bc))
        let m = cross(n, bc)
        let t = angle * .pi / 180, x = torsion * .pi / 180
        let d = BioVec(-length * cos(t), length * sin(t) * cos(x), length * sin(t) * sin(x))
        return c + d.x * bc + d.y * m + d.z * n
    }

    /// `v` turned about the unit axis `axis` (through the origin) by `degrees`, right-handed (Rodrigues).
    static func rotate(_ v: BioVec, axis: BioVec, degrees: Double) -> BioVec {
        let k = unit(axis)
        let t = degrees * .pi / 180
        return v * cos(t) + cross(k, v) * sin(t) + k * dot(k, v) * (1 - cos(t))
    }

    /// The centre and radius of a ball holding every point (the centre of their bounds; for framing a camera).
    static func boundingSphere(_ points: [BioVec]) -> (centre: BioVec, radius: Double) {
        guard let first = points.first else { return (BioVec(0, 0, 0), 1) }
        var lo = first, hi = first
        for p in points {
            lo = BioVec(min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z))
            hi = BioVec(max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z))
        }
        let c = (lo + hi) / 2
        let r = points.map { distance($0, c) }.max() ?? 1
        return (c, max(r, 1e-6))
    }
}

// MARK: - Numbers as shown

extension Double {
    /// This number rounded half up on its decimal digits at `places` decimals: 3.425 → 3.43, 0.125 → 0.13, 2.5 → 3 and
    /// −2.5 → −3 (a half goes away from zero). It is the one rounding the apps use for every number they show, the same
    /// on the Mac and on Windows: "%.2f" alone rounds the binary value (3.425 is held as 3.42499…, so 3.42) and a true
    /// tie to even (0.125 → 0.12). The number is read to 15 significant figures first, which drops the binary value's
    /// noise; what comes back prints exactly at `places` decimals.
    func bioHalfUp(_ places: Int) -> Double {
        guard isFinite, self != 0, (0...15).contains(places), var d = Decimal(string: String(format: "%.14e", self)) else { return self }
        var r = Decimal()
        NSDecimalRound(&r, &d, places, .plain)
        return Double(r.description) ?? self
    }
}

extension String {
    /// `String(format:)` with every fixed-point number in it ("%.2f", "%+.1f", "%.0f%%") rounded half up on its decimal
    /// digits first (`Double.bioHalfUp`): how each biology tool writes a number it shows. Every other conversion is
    /// String(format:)'s own.
    init(halfUp format: String, _ args: CVarArg...) {
        // (each argument's decimals if its conversion is an f, in order; nil for any other)
        var places: [Int?] = []
        var i = format.startIndex
        while let pct = format[i...].firstIndex(of: "%") {
            var j = format.index(after: pct)
            guard j < format.endIndex else { break }
            if format[j] == "%" {
                i = format.index(after: j)
                continue
            }
            while j < format.endIndex, "+- #0123456789".contains(format[j]) { j = format.index(after: j) }
            var precision: Int?
            if j < format.endIndex, format[j] == "." {
                j = format.index(after: j)
                var n = 0
                while j < format.endIndex, let digit = format[j].wholeNumberValue {
                    n = n * 10 + digit
                    j = format.index(after: j)
                }
                precision = n
            }
            while j < format.endIndex, "hlLqjzt".contains(format[j]) { j = format.index(after: j) }
            guard j < format.endIndex else { break }
            places.append(format[j] == "f" || format[j] == "F" ? (precision ?? 6) : nil)
            i = format.index(after: j)
        }
        let rounded: [CVarArg] = args.enumerated().map { k, a in
            if k < places.count, let p = places[k], type(of: a) == Double.self, let d = a as? Double { return d.bioHalfUp(p) }
            return a
        }
        self.init(format: format, arguments: rounded)
    }
}

// MARK: - SplitMix64 (§5.6.3)

/// SplitMix64: the toolkit's seeded randomness, bit for bit the same on the Mac and on Windows (every operation wraps).
/// A cell built from its seed looks the same on every launch and on both platforms, so every draw is made in a fixed
/// order — the helpers below say exactly how many numbers each takes, in what order.
struct BioRandom {
    var state: UInt64

    init(seed: UInt64) { state = seed }

    mutating func next() -> UInt64 {
        state &+= 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }

    /// A double in [0, 1): the top 53 bits (one draw).
    mutating func uniform() -> Double { Double(next() >> 11) * (1.0 / 9_007_199_254_740_992.0) }

    /// lo + (hi − lo)·u (one draw).
    mutating func uniform(_ lo: Double, _ hi: Double) -> Double { lo + (hi - lo) * uniform() }

    /// An integer in 0..<n: ⌊u·n⌋ (one draw; 0 when n ≤ 0).
    mutating func int(below n: Int) -> Int {
        guard n > 0 else { return 0 }
        return min(n - 1, Int(uniform() * Double(n)))
    }

    /// A direction uniform on the unit sphere (two draws: z = 2u₁ − 1, then φ = 2π·u₂; x = ρ·cos φ, y = ρ·sin φ).
    mutating func unitVector() -> BioVec {
        let z = 2 * uniform() - 1
        let phi = 2 * Double.pi * uniform()
        let rho = max(0, 1 - z * z).squareRoot()
        return BioVec(rho * cos(phi), rho * sin(phi), z)
    }

    /// A point uniform in the unit ball (three draws a try: x, y, z each 2u − 1, kept when inside).
    mutating func inBall() -> BioVec {
        while true {
            let p = BioVec(2 * uniform() - 1, 2 * uniform() - 1, 2 * uniform() - 1)
            if BioV.dot(p, p) <= 1 { return p }
        }
    }

    /// A random turn: its axis (`unitVector`, two draws) and its angle in degrees (360·u, one draw), in that order.
    mutating func rotation() -> (axis: BioVec, degrees: Double) {
        let axis = unitVector()
        return (axis, 360 * uniform())
    }
}

// MARK: - Exact fractions (§5.6.1)

/// A fraction kept exact (num/den, reduced, den > 0): the crosses' probabilities and ratios, never rounded until shown.
struct BioFrac: Hashable, Comparable, CustomStringConvertible {
    let num: Int64
    let den: Int64

    init(_ num: Int64, _ den: Int64 = 1) {
        precondition(den != 0, "a fraction over zero")
        let g = BioFrac.gcd(num, den)
        let s: Int64 = den < 0 ? -1 : 1
        self.num = s * num / max(g, 1)
        self.den = s * den / max(g, 1)
    }

    init(_ n: Int, _ d: Int = 1) { self.init(Int64(n), Int64(d)) }

    static let zero = BioFrac(0, 1)
    static let one = BioFrac(1, 1)

    static func gcd(_ a: Int64, _ b: Int64) -> Int64 {
        var x = a.magnitude, y = b.magnitude
        while y != 0 { (x, y) = (y, x % y) }
        return Int64(x)
    }

    static func + (a: BioFrac, b: BioFrac) -> BioFrac { BioFrac(a.num * b.den + b.num * a.den, a.den * b.den) }
    static func - (a: BioFrac, b: BioFrac) -> BioFrac { BioFrac(a.num * b.den - b.num * a.den, a.den * b.den) }
    static func * (a: BioFrac, b: BioFrac) -> BioFrac { BioFrac(a.num * b.num, a.den * b.den) }
    static func / (a: BioFrac, b: BioFrac) -> BioFrac { BioFrac(a.num * b.den, a.den * b.num) }
    static func < (a: BioFrac, b: BioFrac) -> Bool { a.num * b.den < b.num * a.den }

    var double: Double { Double(num) / Double(den) }
    var isZero: Bool { num == 0 }

    /// "3/32"; a whole number as itself ("1", "0").
    var text: String { den == 1 ? "\(num)" : "\(num)/\(den)" }
    var description: String { text }

    /// As a percent with one decimal: "9.4%" (3/32), "50.0%".
    var percentText: String { String(halfUp: "%.1f%%", double * 100) }
}

/// Ratios of whole counts (the crosses' "9 : 3 : 3 : 1").
enum BioRatio {
    /// The counts divided by their greatest common divisor, as a whole: [2, 4, 2] → [1, 2, 1].
    static func reduce(_ counts: [Int]) -> [Int] {
        let g = counts.reduce(Int64(0)) { BioFrac.gcd($0, Int64($1)) }
        guard g > 1 else { return counts }
        return counts.map { $0 / Int(g) }
    }

    /// The smallest whole numbers in the proportion of `fracs` (1/4, 1/2, 1/4 → 1, 2, 1).
    static func whole(_ fracs: [BioFrac]) -> [Int] {
        guard !fracs.isEmpty else { return [] }
        var l: Int64 = 1
        for f in fracs { l = l / BioFrac.gcd(l, f.den) * f.den }
        return reduce(fracs.map { Int($0.num * (l / $0.den)) })
    }

    /// "1 : 2 : 1".
    static func text(_ counts: [Int]) -> String { counts.map(String.init).joined(separator: " : ") }
}

// MARK: - Chi-square (§5.3.3, §5.3.4, §5.6.4)

/// The chi-square goodness-of-fit test as the AP sheet has it: χ² = Σ (o − e)²/e, df = categories − 1 (or what the
/// caller says: the Hardy–Weinberg test uses 1), the critical value at p = 0.05 and the p-value, and the conclusion in
/// AP's words — "reject" or "fail to reject" the null hypothesis, never "accept" or "prove".
enum BioChiSquare {
    /// The p column heads of the critical-value table.
    static let tableP: [Double] = [0.10, 0.05, 0.01, 0.001]

    /// §5.3.4: critical values by df (1–10), one for each of `tableP`.
    static let table: [Int: [Double]] = {
        var t: [Int: [Double]] = [:]
        for line in BioData.chiSquareText.split(separator: "\n") {
            let cells = line.split(separator: "\t").compactMap { Double($0.trimmingCharacters(in: .whitespaces)) }
            guard cells.count == 5 else { continue }
            t[Int(cells[0])] = Array(cells.dropFirst())
        }
        return t
    }()

    /// The tabled critical value at p (0.10, 0.05, 0.01 or 0.001) for df 1–10, else computed (§5.6.4's bisection).
    static func critical(df: Int, p: Double = 0.05) -> Double {
        if let row = table[df], let i = tableP.firstIndex(where: { abs($0 - p) < 1e-12 }) { return row[i] }
        return criticalComputed(df: df, p: p)
    }

    /// The χ² at which the p-value is p: bisection on [0, 200], 200 iterations (§5.6.4).
    static func criticalComputed(df: Int, p: Double) -> Double {
        var lo = 0.0, hi = 200.0
        for _ in 0..<200 {
            let mid = (lo + hi) / 2
            if pValue(mid, df: df) > p { lo = mid } else { hi = mid }
        }
        return (lo + hi) / 2
    }

    /// p = Q(df/2, χ²/2), the regularized upper incomplete gamma function (Numerical Recipes).
    static func pValue(_ chi2: Double, df: Int) -> Double {
        guard df > 0 else { return .nan }
        guard chi2 > 0 else { return 1 }
        return gammaQ(Double(df) / 2, chi2 / 2)
    }

    /// ln Γ(x) for x > 0: Lanczos, g = 7, n = 9 (the same series Windows uses, so both get the same p-values).
    static func lnGamma(_ x: Double) -> Double {
        let g = 7.0
        let c: [Double] = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
                           -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
                           1.5056327351493116e-7]
        if x < 0.5 {
            // (the reflection formula)
            return log(Double.pi / abs(sin(Double.pi * x))) - lnGamma(1 - x)
        }
        let z = x - 1
        var a = c[0]
        let t = z + g + 0.5
        for i in 1..<9 { a += c[i] / (z + Double(i)) }
        return 0.5 * log(2 * Double.pi) + (z + 0.5) * log(t) - t + log(a)
    }

    /// The regularized lower incomplete gamma P(a, x) by its series (for x < a + 1).
    static func gammaPSeries(_ a: Double, _ x: Double) -> Double {
        var ap = a
        var sum = 1.0 / a
        var del = sum
        for _ in 0..<10_000 {
            ap += 1
            del *= x / ap
            sum += del
            if abs(del) < abs(sum) * 1e-15 { break }
        }
        return sum * exp(-x + a * log(x) - lnGamma(a))
    }

    /// The regularized upper incomplete gamma Q(a, x) by its continued fraction (modified Lentz; for x ≥ a + 1).
    static func gammaQFraction(_ a: Double, _ x: Double) -> Double {
        let tiny = 1e-300, eps = 1e-15
        var b = x + 1 - a
        var c = 1 / tiny
        var d = 1 / b
        var h = d
        var i = 1.0
        while i < 10_000 {
            let an = -i * (i - a)
            b += 2
            d = an * d + b
            if abs(d) < tiny { d = tiny }
            c = b + an / c
            if abs(c) < tiny { c = tiny }
            d = 1 / d
            let del = d * c
            h *= del
            if abs(del - 1) < eps { break }
            i += 1
        }
        return exp(-x + a * log(x) - lnGamma(a)) * h
    }

    static func gammaQ(_ a: Double, _ x: Double) -> Double {
        if x < a + 1 { return 1 - gammaPSeries(a, x) }
        return gammaQFraction(a, x)
    }

    /// A test's working, as the student writes it out.
    struct Result {
        let observed: [Double]
        let expected: [Double]
        /// Each category's (o − e)²/e.
        let terms: [Double]
        let chi2: Double
        let df: Int
        /// The critical value at p = 0.05 for this df.
        let critical: Double
        let p: Double
        /// χ² above the critical value: the null hypothesis is rejected (p < 0.05).
        var reject: Bool { chi2 > critical }

        /// "χ² = 2.13 < 7.81 (df = 3, p = 0.05), so we fail to reject the null hypothesis." (AP's words.)
        var conclusion: String {
            let a = String(halfUp: "%.2f", chi2), b = String(halfUp: "%.2f", critical)
            return reject
                ? "χ² = \(a) > \(b) (df = \(df), p = 0.05), so we reject the null hypothesis."
                : "χ² = \(a) < \(b) (df = \(df), p = 0.05), so we fail to reject the null hypothesis."
        }
    }

    /// The test of observed counts against expected ones (df = categories − 1 unless given).
    static func test(observed: [Double], expected: [Double], df: Int? = nil) -> Result {
        let n = min(observed.count, expected.count)
        var terms: [Double] = []
        for i in 0..<n {
            let e = expected[i]
            terms.append(e > 0 ? (observed[i] - e) * (observed[i] - e) / e : 0)
        }
        let chi2 = terms.reduce(0, +)
        let f = df ?? max(n - 1, 1)
        return Result(observed: Array(observed.prefix(n)), expected: Array(expected.prefix(n)), terms: terms, chi2: chi2,
                      df: f, critical: critical(df: f), p: pValue(chi2, df: f))
    }

    /// Expected counts for a ratio (9 : 3 : 3 : 1) and a total.
    static func expected(ratio: [Double], total: Double) -> [Double] {
        let s = ratio.reduce(0, +)
        return ratio.map { s > 0 ? $0 / s * total : 0 }
    }
}

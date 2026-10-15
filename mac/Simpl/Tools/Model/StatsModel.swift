import Foundation

// (tools) Statistics' arithmetic (Foundation only, checked by the Models job against the specification's vectors): a
// data set described — its mean, median, modes, quartiles (the medians of the lower and upper halves, the median
// itself left out when n is odd), standard deviations and variances (sample and population), standard error, the
// outliers past 1.5 IQR, and the histogram's bins by Sturges' rule; linear regression; the normal distribution by an
// erf of the tool's own (a power series near zero, a continued fraction beyond it: within 1e-12 of the C library's),
// its inverse (Acklam's approximation, refined once); Student's t by the regularized incomplete beta (Numerical
// Recipes' continued fraction, betacf) and its inverse; and the t-tests — one sample, Welch's two-sample, paired —
// with their 95% confidence intervals.

enum Stats {
    // MARK: Describing a data set

    /// Everything said about a data set.
    struct Description: Equatable {
        let sorted: [Double]
        let n: Int
        let mean: Double
        let median: Double
        /// The values that come up most, when any comes up more than once; none when every value is on its own.
        let modes: [Double]
        let min: Double
        let max: Double
        let range: Double
        let q1: Double
        let q3: Double
        let iqr: Double
        let sampleSD: Double
        let populationSD: Double
        let sampleVariance: Double
        let populationVariance: Double
        /// The standard error of the mean: the sample SD over √n.
        let standardError: Double
        let sum: Double
        let sumOfSquares: Double
        /// The values more than 1.5 IQR below Q1 or above Q3.
        let outliers: [Double]
    }

    /// A data set described (nil: no numbers).
    static func describe(_ data: [Double]) -> Description? {
        guard !data.isEmpty else { return nil }
        let s = data.sorted()
        let n = s.count
        let sum = s.reduce(0, +)
        let mean = sum / Double(n)
        let ss = s.reduce(0) { $0 + $1 * $1 }
        let devs = s.reduce(0) { $0 + ($1 - mean) * ($1 - mean) }
        let popVar = devs / Double(n)
        let sampVar = n > 1 ? devs / Double(n - 1) : 0
        let q = quartiles(sorted: s)
        let iqr = q.q3 - q.q1
        let low = q.q1 - 1.5 * iqr, high = q.q3 + 1.5 * iqr
        return Description(sorted: s, n: n, mean: mean, median: q.median, modes: modes(sorted: s), min: s[0], max: s[n - 1], range: s[n - 1] - s[0],
                           q1: q.q1, q3: q.q3, iqr: iqr, sampleSD: sampVar.squareRoot(), populationSD: popVar.squareRoot(),
                           sampleVariance: sampVar, populationVariance: popVar, standardError: n > 1 ? sampVar.squareRoot() / Double(n).squareRoot() : 0,
                           sum: sum, sumOfSquares: ss, outliers: s.filter { $0 < low || $0 > high })
    }

    /// The median of sorted values.
    static func median(sorted s: [Double]) -> Double {
        guard !s.isEmpty else { return .nan }
        let n = s.count
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2
    }

    /// The quartiles: the medians of the lower and upper halves of the sorted values, the median itself left out of
    /// both when n is odd (Tukey's hinges as most textbooks and the TI calculators take them).
    static func quartiles(sorted s: [Double]) -> (q1: Double, median: Double, q3: Double) {
        let n = s.count
        guard n > 1 else { return (s.first ?? .nan, s.first ?? .nan, s.first ?? .nan) }
        let half = n / 2
        return (median(sorted: Array(s[0..<half])), median(sorted: s), median(sorted: Array(s[(n - half)...])))
    }

    /// The modes: every value that comes up as often as the most frequent one, when that is more than once.
    static func modes(sorted s: [Double]) -> [Double] {
        var counts: [Double: Int] = [:]
        for v in s { counts[v, default: 0] += 1 }
        guard let top = counts.values.max(), top > 1 else { return [] }
        return counts.filter { $0.value == top }.map(\.key).sorted()
    }

    /// A histogram's bin: its edges and how many values fall in it (the last bin takes the greatest value too).
    struct Bin: Equatable {
        let lower: Double
        let upper: Double
        let count: Int
    }

    /// The histogram by Sturges' rule: ⌈log₂ n⌉ + 1 bins of equal width from the least value to the greatest; one bin
    /// when every value is the same.
    static func histogram(_ data: [Double]) -> [Bin] {
        guard let lo = data.min(), let hi = data.max() else { return [] }
        guard hi > lo else { return [Bin(lower: lo, upper: hi, count: data.count)] }
        let k = Int(ceil(log2(Double(data.count)))) + 1
        let width = (hi - lo) / Double(k)
        var counts = [Int](repeating: 0, count: k)
        for v in data {
            let i = Swift.min(Int((v - lo) / width), k - 1)
            counts[i] += 1
        }
        var bins: [Bin] = []
        for i in 0..<k {
            let lower = lo + Double(i) * width
            let upper: Double = i == k - 1 ? hi : lo + Double(i + 1) * width
            bins.append(Bin(lower: lower, upper: upper, count: counts[i]))
        }
        return bins
    }

    // MARK: Linear regression

    struct Regression: Equatable {
        let n: Int
        let slope: Double
        let intercept: Double
        let r: Double
        let meanX: Double
        let meanY: Double

        var rSquared: Double { r * r }
        func predict(_ x: Double) -> Double { slope * x + intercept }
    }

    /// The least-squares line through the points (nil: fewer than two points, the columns of different lengths, or
    /// every x the same).
    static func regression(x: [Double], y: [Double]) -> Regression? {
        let n = x.count
        guard n >= 2, y.count == n else { return nil }
        let mx = x.reduce(0, +) / Double(n), my = y.reduce(0, +) / Double(n)
        var sxx = 0.0, syy = 0.0, sxy = 0.0
        for i in 0..<n {
            sxx += (x[i] - mx) * (x[i] - mx)
            syy += (y[i] - my) * (y[i] - my)
            sxy += (x[i] - mx) * (y[i] - my)
        }
        guard sxx > 0 else { return nil }
        let slope = sxy / sxx
        let r = syy > 0 ? sxy / (sxx * syy).squareRoot() : 1
        return Regression(n: n, slope: slope, intercept: my - slope * mx, r: r, meanX: mx, meanY: my)
    }

    // MARK: The normal distribution

    /// The error function, the tool's own so the Mac and Windows agree to the last figure shown: the power series
    /// 2/√π Σ (−1)ⁿ x²ⁿ⁺¹ / (n! (2n+1)) up to |x| = 2.5, past it 1 − erfc by the continued fraction
    /// erfc(x) = e^(−x²)/√π · 1/(x + ½/(x + 1/(x + 3⁄2/(x + …)))). Within 1e-12 of the C library's everywhere.
    static func erf(_ x: Double) -> Double {
        if x.isNaN { return .nan }
        if x < 0 { return -erf(-x) }
        if x > 6 { return 1 }
        if x <= 2.5 {
            var term = x, sum = x
            var n = 0.0
            while true {
                n += 1
                term *= -x * x / n
                let add = term / (2 * n + 1)
                sum += add
                if abs(add) < 1e-17 * abs(sum) || n > 200 { break }
            }
            return 2 / Double.pi.squareRoot() * sum
        }
        return 1 - erfc(x)
    }

    /// 1 − erf(x), kept exact where erf is nearly 1 (the tails).
    static func erfc(_ x: Double) -> Double {
        if x.isNaN { return .nan }
        if x < 2.5 { return 1 - erf(x) }
        var f = x
        for k in stride(from: 80, through: 1, by: -1) { f = x + Double(k) / 2 / f }
        return exp(-x * x) / (Double.pi.squareRoot() * f)
    }

    /// P(X < x) for X ~ N(μ, σ²).
    static func normalCDF(_ x: Double, mean: Double = 0, sd: Double = 1) -> Double {
        guard sd > 0 else { return x < mean ? 0 : 1 }
        return 0.5 * erfc(-(x - mean) / sd / 2.0.squareRoot())
    }

    /// The density of N(μ, σ²) at x, for the curve drawn.
    static func normalPDF(_ x: Double, mean: Double = 0, sd: Double = 1) -> Double {
        guard sd > 0 else { return 0 }
        let z = (x - mean) / sd
        return exp(-0.5 * z * z) / (sd * (2 * Double.pi).squareRoot())
    }

    /// The x with P(X < x) = p (nil: p not strictly between 0 and 1): Acklam's rational approximation, then one step
    /// of Halley's method on erfc, which makes it exact to the double.
    static func normalInverse(_ p: Double, mean: Double = 0, sd: Double = 1) -> Double? {
        guard p > 0, p < 1, sd > 0 else { return nil }
        let a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00]
        let b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01]
        let c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00]
        let d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00]
        let low = 0.02425
        var x: Double
        if p < low {
            let q = (-2 * log(p)).squareRoot()
            x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1)
        } else if p <= 1 - low {
            let q = p - 0.5, r = q * q
            x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1)
        } else {
            let q = (-2 * log(1 - p)).squareRoot()
            x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1)
        }
        let e = 0.5 * erfc(-x / 2.0.squareRoot()) - p
        let u = e * (2 * Double.pi).squareRoot() * exp(x * x / 2)
        x -= u / (1 + x * u / 2)
        return mean + sd * x
    }

    // MARK: Student's t

    /// ln Γ(x) for x > 0 (Lanczos, as Numerical Recipes' gammln).
    static func logGamma(_ x: Double) -> Double {
        let cof = [76.18009172947146, -86.50532032941677, 24.01409824083091, -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5]
        var y = x
        var tmp = x + 5.5
        tmp -= (x + 0.5) * log(tmp)
        var ser = 1.000000000190015
        for c in cof {
            y += 1
            ser += c / y
        }
        return -tmp + log(2.5066282746310005 * ser / x)
    }

    /// The regularized incomplete beta function I_x(a, b) (Numerical Recipes' betai, by its continued fraction).
    static func incompleteBeta(_ a: Double, _ b: Double, _ x: Double) -> Double {
        if x <= 0 { return 0 }
        if x >= 1 { return 1 }
        let bt = exp(logGamma(a + b) - logGamma(a) - logGamma(b) + a * log(x) + b * log(1 - x))
        if x < (a + 1) / (a + b + 2) { return bt * betacf(a, b, x) / a }
        return 1 - bt * betacf(b, a, 1 - x) / b
    }

    /// The continued fraction for the incomplete beta function (Numerical Recipes' betacf, by Lentz's method).
    static func betacf(_ a: Double, _ b: Double, _ x: Double) -> Double {
        let fpmin = 1e-300, eps = 1e-15
        let qab = a + b, qap = a + 1, qam = a - 1
        var c = 1.0
        var d = 1 - qab * x / qap
        if abs(d) < fpmin { d = fpmin }
        d = 1 / d
        var h = d
        for m in 1...300 {
            let m = Double(m), m2 = 2 * m
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2))
            d = 1 + aa * d
            if abs(d) < fpmin { d = fpmin }
            c = 1 + aa / c
            if abs(c) < fpmin { c = fpmin }
            d = 1 / d
            h *= d * c
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
            d = 1 + aa * d
            if abs(d) < fpmin { d = fpmin }
            c = 1 + aa / c
            if abs(c) < fpmin { c = fpmin }
            d = 1 / d
            let del = d * c
            h *= del
            if abs(del - 1) < eps { break }
        }
        return h
    }

    /// P(T < t) for Student's t with `df` degrees of freedom.
    static func tCDF(_ t: Double, df: Double) -> Double {
        guard df > 0 else { return .nan }
        let x = df / (df + t * t)
        let tail = 0.5 * incompleteBeta(df / 2, 0.5, x)
        return t >= 0 ? 1 - tail : tail
    }

    /// The two-tailed p for a t statistic: P(|T| ≥ |t|).
    static func tTwoTailed(_ t: Double, df: Double) -> Double {
        guard df > 0 else { return .nan }
        return incompleteBeta(df / 2, 0.5, df / (df + t * t))
    }

    /// The density of Student's t at t, for the curve drawn.
    static func tPDF(_ t: Double, df: Double) -> Double {
        guard df > 0 else { return 0 }
        return exp(logGamma((df + 1) / 2) - logGamma(df / 2)) / (df * Double.pi).squareRoot() * pow(1 + t * t / df, -(df + 1) / 2)
    }

    /// The t with P(T < t) = p (nil: p not strictly between 0 and 1): by bisection on the CDF, to the double.
    static func tQuantile(_ p: Double, df: Double) -> Double? {
        guard p > 0, p < 1, df > 0 else { return nil }
        if p == 0.5 { return 0 }
        if p < 0.5 { return tQuantile(1 - p, df: df).map { -$0 } }
        var lo = 0.0, hi = 1.0
        while tCDF(hi, df: df) < p && hi < 1e12 { hi *= 2 }
        for _ in 0..<200 {
            let mid = (lo + hi) / 2
            if mid == lo || mid == hi { break }
            if tCDF(mid, df: df) < p { lo = mid } else { hi = mid }
        }
        return (lo + hi) / 2
    }

    /// The t past which a two-tailed test at level `alpha` rejects: t with P(|T| ≥ t) = alpha.
    static func tCritical(alpha: Double, df: Double) -> Double? { tQuantile(1 - alpha / 2, df: df) }

    // MARK: t-tests

    /// A t-test's answer.
    struct TTest: Equatable {
        let t: Double
        let df: Double
        /// P(|T| ≥ |t|).
        let pTwoTailed: Double
        /// P(T ≥ |t|): the one-tailed p in the direction found.
        var pOneTailed: Double { pTwoTailed / 2 }
        /// What was tested: a sample's mean, or the difference between two.
        let estimate: Double
        let standardError: Double
        /// The 95% confidence interval for `estimate` (for a one-sample test, the mean's; for two samples, the
        /// difference's; for a paired test, the mean difference's).
        let ciLow: Double
        let ciHigh: Double
    }

    /// A summary of a sample: how many, their mean and sample SD.
    struct Sample: Equatable {
        let n: Int
        let mean: Double
        let sd: Double

        init?(n: Int, mean: Double, sd: Double) {
            guard n >= 2, sd >= 0, mean.isFinite, sd.isFinite else { return nil }
            self.n = n
            self.mean = mean
            self.sd = sd
        }

        init?(_ data: [Double]) {
            guard let d = Stats.describe(data), d.n >= 2 else { return nil }
            self.init(n: d.n, mean: d.mean, sd: d.sampleSD)
        }

        var variance: Double { sd * sd }
    }

    /// One sample's mean against μ₀ (nil: the sample has no spread).
    static func oneSample(_ s: Sample, mu0: Double) -> TTest? {
        guard s.sd > 0 else { return nil }
        let se = s.sd / Double(s.n).squareRoot()
        let df = Double(s.n - 1)
        let t = (s.mean - mu0) / se
        guard let crit = tCritical(alpha: 0.05, df: df) else { return nil }
        return TTest(t: t, df: df, pTwoTailed: tTwoTailed(t, df: df), estimate: s.mean, standardError: se, ciLow: s.mean - crit * se, ciHigh: s.mean + crit * se)
    }

    /// Two samples' means, their variances not assumed equal (Welch's t-test, with the Welch–Satterthwaite degrees of
    /// freedom).
    static func welch(_ a: Sample, _ b: Sample) -> TTest? {
        let va = a.variance / Double(a.n), vb = b.variance / Double(b.n)
        guard va + vb > 0 else { return nil }
        let se = (va + vb).squareRoot()
        let df = (va + vb) * (va + vb) / (va * va / Double(a.n - 1) + vb * vb / Double(b.n - 1))
        let diff = a.mean - b.mean
        let t = diff / se
        guard let crit = tCritical(alpha: 0.05, df: df) else { return nil }
        return TTest(t: t, df: df, pTwoTailed: tTwoTailed(t, df: df), estimate: diff, standardError: se, ciLow: diff - crit * se, ciHigh: diff + crit * se)
    }

    /// Two measurements of the same things (before and after): the differences' mean against zero.
    static func paired(_ x: [Double], _ y: [Double]) -> TTest? {
        guard x.count == y.count, x.count >= 2 else { return nil }
        let d = zip(x, y).map { $0 - $1 }
        guard let s = Sample(d) else { return nil }
        return oneSample(s, mu0: 0)
    }
}

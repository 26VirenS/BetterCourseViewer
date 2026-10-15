using System.Globalization;

namespace Simpl.Tools;

// The Statistics tool's maths (tools; the Mac's StatsMath): describing a sample, a straight line through points, the
// normal and t distributions (erf to full precision, the regularized incomplete beta by its continued fraction), and
// the t-tests — with no window in it, so the tests can run it as it is. Both apps give the same numbers on the shared
// spec's vectors.

/// <summary>What a sample comes to.</summary>
internal sealed record Description(int N, double Mean, double Median, double[] Modes, double Min, double Max, double Q1, double Q3,
    double SampleSd, double PopSd, double Sum, double SumSquares, double[] Sorted)
{
    public double Range => Max - Min;
    public double Iqr => Q3 - Q1;
    public double SampleVar => SampleSd * SampleSd;
    public double PopVar => PopSd * PopSd;
    /// <summary>The standard error of the mean, s/√n.</summary>
    public double Se => SampleSd / Math.Sqrt(N);
    /// <summary>Past the fences at 1.5 IQR from the quartiles.</summary>
    public double[] Outliers => Sorted.Where(v => v < Q1 - 1.5 * Iqr || v > Q3 + 1.5 * Iqr).ToArray();
}

/// <summary>A histogram's bins (Sturges' count): the first bin's start, each bin's width, and the counts.</summary>
internal sealed record Histogram(double Start, double Width, int[] Counts);

/// <summary>The straight line through points by least squares.</summary>
internal sealed record Regression(int N, double Slope, double Intercept, double R, double MeanX, double MeanY)
{
    public double R2 => R * R;
    public double Predict(double x) => Slope * x + Intercept;
}

/// <summary>A t-test's answer: the statistic, its degrees of freedom, the two-tailed p, the estimate the test is about
/// (a mean, or a difference of means) with its 95% confidence interval, and the standard error it stands on.</summary>
internal sealed record TResult(double T, double Df, double P, double Estimate, double CiLow, double CiHigh, double Se)
{
    /// <summary>The one-tailed p, in the direction the statistic points.</summary>
    public double OneTailed => P / 2;
}

/// <summary>The Statistics tool's sums.</summary>
internal static class StatsMath
{
    // ---- reading numbers ----

    /// <summary>The numbers in a field: separated by spaces, commas, semicolons, tabs or line breaks (a column pasted from
    /// a spreadsheet); what is not a number is skipped and counted.</summary>
    public static double[] Numbers(string? text, out int skipped)
    {
        skipped = 0;
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<double>();
        var list = new List<double>();
        foreach (var token in text.Split(new[] { ' ', ',', ';', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Replace("−", "-");
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)) list.Add(v);
            else skipped++;
        }
        return list.ToArray();
    }

    public static double[] Numbers(string? text) => Numbers(text, out _);

    // ---- describing a sample ----

    public static Description? Describe(double[] data)
    {
        if (data.Length == 0) return null;
        var sorted = data.OrderBy(v => v).ToArray();
        var n = sorted.Length;
        var sum = sorted.Sum();
        var mean = sum / n;
        var ss = sorted.Sum(v => (v - mean) * (v - mean));
        var popSd = Math.Sqrt(ss / n);
        var sampleSd = n > 1 ? Math.Sqrt(ss / (n - 1)) : 0;
        var (q1, q3) = Quartiles(sorted);
        return new Description(n, mean, Median(sorted), Modes(sorted), sorted[0], sorted[n - 1], q1, q3, sampleSd, popSd, sum, sorted.Sum(v => v * v), sorted);
    }

    public static double Median(ReadOnlySpan<double> sorted)
    {
        var n = sorted.Length;
        if (n == 0) return double.NaN;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }

    /// <summary>The quartiles as the medians of the lower and upper halves, the median itself left out of both when n
    /// is odd (Moore and McCabe's way, the one most textbooks teach): 2,4,4,4,5,5,7,9 has Q1 4 and Q3 6.</summary>
    public static (double Q1, double Q3) Quartiles(double[] sorted)
    {
        var n = sorted.Length;
        if (n == 0) return (double.NaN, double.NaN);
        if (n == 1) return (sorted[0], sorted[0]);
        var half = n / 2;
        var lower = new ReadOnlySpan<double>(sorted, 0, half);
        var upper = new ReadOnlySpan<double>(sorted, n % 2 == 1 ? half + 1 : half, half);
        return (Median(lower), Median(upper));
    }

    /// <summary>The values that appear most, when any appears more than once (none: no mode).</summary>
    public static double[] Modes(double[] sorted)
    {
        var counts = new Dictionary<double, int>();
        foreach (var v in sorted) counts[v] = counts.GetValueOrDefault(v) + 1;
        var most = counts.Count == 0 ? 0 : counts.Values.Max();
        if (most < 2) return Array.Empty<double>();
        return counts.Where(p => p.Value == most).Select(p => p.Key).OrderBy(v => v).ToArray();
    }

    /// <summary>Sturges' bins, ⌈log₂ n + 1⌉ of them from the least value to the greatest (one bin for a sample all of one
    /// value); the greatest value goes in the last bin.</summary>
    public static Histogram Bins(Description d)
    {
        var k = Math.Max(1, (int)Math.Ceiling(Math.Log2(d.N) + 1));
        if (d.Range <= 0) return new Histogram(d.Min, 1, new[] { d.N });
        var width = d.Range / k;
        var counts = new int[k];
        foreach (var v in d.Sorted) counts[Math.Min(k - 1, (int)((v - d.Min) / width))]++;
        return new Histogram(d.Min, width, counts);
    }

    // ---- the line through points ----

    public static Regression? Linear(double[] x, double[] y)
    {
        var n = Math.Min(x.Length, y.Length);
        if (n < 2) return null;
        double mx = 0, my = 0;
        for (var i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
        mx /= n; my /= n;
        double sxx = 0, syy = 0, sxy = 0;
        for (var i = 0; i < n; i++)
        {
            sxx += (x[i] - mx) * (x[i] - mx);
            syy += (y[i] - my) * (y[i] - my);
            sxy += (x[i] - mx) * (y[i] - my);
        }
        if (sxx <= 0) return null;
        var slope = sxy / sxx;
        var r = syy <= 0 ? 1 : sxy / Math.Sqrt(sxx * syy);
        return new Regression(n, slope, my - slope * mx, Math.Clamp(r, -1, 1), mx, my);
    }

    // ---- the normal distribution ----

    /// <summary>The error function to full double precision: its series near zero, the continued fraction for erfc
    /// further out (closer than the 1.5e-7 of Abramowitz and Stegun's 7.1.26).</summary>
    public static double Erf(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (Math.Abs(x) < 2.5)
        {
            // erf x = 2/√π Σ (−1)ⁿ x^(2n+1) / (n! (2n+1))
            double sum = 0, term = x;
            var x2 = x * x;
            for (var n = 0; n < 200; n++)
            {
                var add = term / (2 * n + 1);
                sum += add;
                if (Math.Abs(add) < 1e-17 * Math.Abs(sum)) break;
                term *= -x2 / (n + 1);
            }
            return 2 / Math.Sqrt(Math.PI) * sum;
        }
        return x > 0 ? 1 - Erfc(x) : Erfc(-x) - 1;
    }

    /// <summary>erfc x for x ≥ 2.5 by Lentz's evaluation of its continued fraction (1 − erf x for smaller x).</summary>
    public static double Erfc(double x)
    {
        if (x < 2.5) return 1 - Erf(x);
        // erfc x = e^(−x²)/√π · 1/(x + ½/(x + 1/(x + 3/2/(x + 2/(x + …)))))
        const double tiny = 1e-300;
        double f = x, c = x, d = 0;
        for (var n = 1; n < 300; n++)
        {
            var a = n / 2.0;
            d = x + a * d; if (Math.Abs(d) < tiny) d = tiny; d = 1 / d;
            c = x + a / c; if (Math.Abs(c) < tiny) c = tiny;
            var delta = c * d;
            f *= delta;
            if (Math.Abs(delta - 1) < 1e-16) break;
        }
        return Math.Exp(-x * x) / Math.Sqrt(Math.PI) / f;
    }

    /// <summary>Φ(z), the standard normal's lower tail.</summary>
    public static double NormalCdf(double z) => z < -4 ? 0.5 * Erfc(-z / Math.Sqrt(2)) : 0.5 * (1 + Erf(z / Math.Sqrt(2)));

    public static double NormalCdf(double x, double mu, double sigma) => NormalCdf((x - mu) / sigma);

    public static double NormalPdf(double z) => Math.Exp(-0.5 * z * z) / Math.Sqrt(2 * Math.PI);

    /// <summary>Φ⁻¹(p): Acklam's rational approximation, then one step of Halley's method on the exact Φ.</summary>
    public static double NormalInverse(double p)
    {
        if (!(p > 0 && p < 1)) return p <= 0 ? double.NegativeInfinity : p >= 1 ? double.PositiveInfinity : double.NaN;
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
        const double low = 0.02425, high = 1 - 0.02425;
        double x;
        if (p < low)
        {
            var q = Math.Sqrt(-2 * Math.Log(p));
            x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        else if (p <= high)
        {
            var q = p - 0.5;
            var r = q * q;
            x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        else
        {
            var q = Math.Sqrt(-2 * Math.Log(1 - p));
            x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        var e = NormalCdf(x) - p;
        var u = e * Math.Sqrt(2 * Math.PI) * Math.Exp(x * x / 2);
        return x - u / (1 + x * u / 2);
    }

    // ---- the t distribution, through the incomplete beta ----

    /// <summary>ln Γ(x) by Lanczos' series (Numerical Recipes' gammln).</summary>
    public static double LnGamma(double x)
    {
        double[] cof = { 76.18009172947146, -86.50532032941677, 24.01409824083091, -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
        double y = x, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        var ser = 1.000000000190015;
        for (var j = 0; j < 6; j++) ser += cof[j] / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }

    /// <summary>The continued fraction for the incomplete beta (Numerical Recipes' betacf), by Lentz's method.</summary>
    private static double BetaCf(double a, double b, double x)
    {
        const int maxIt = 300;
        const double eps = 3e-14, tiny = 1e-300;
        double qab = a + b, qap = a + 1, qam = a - 1;
        double c = 1, d = 1 - qab * x / qap;
        if (Math.Abs(d) < tiny) d = tiny;
        d = 1 / d;
        var h = d;
        for (var m = 1; m <= maxIt; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d; if (Math.Abs(d) < tiny) d = tiny;
            c = 1 + aa / c; if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d; if (Math.Abs(d) < tiny) d = tiny;
            c = 1 + aa / c; if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d;
            var del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < eps) break;
        }
        return h;
    }

    /// <summary>I_x(a, b), the regularized incomplete beta (Numerical Recipes' betai).</summary>
    public static double IncompleteBeta(double a, double b, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var bt = Math.Exp(LnGamma(a + b) - LnGamma(a) - LnGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
        return x < (a + 1) / (a + b + 2) ? bt * BetaCf(a, b, x) / a : 1 - bt * BetaCf(b, a, 1 - x) / b;
    }

    /// <summary>P(|T| &gt; |t|) on df degrees of freedom: the two-tailed p.</summary>
    public static double TTwoTailed(double t, double df)
    {
        if (df <= 0 || double.IsNaN(t)) return double.NaN;
        if (double.IsInfinity(t)) return 0;
        return IncompleteBeta(df / 2, 0.5, df / (df + t * t));
    }

    /// <summary>P(T &lt; t): the lower tail.</summary>
    public static double TCdf(double t, double df)
    {
        var two = TTwoTailed(t, df);
        return t >= 0 ? 1 - two / 2 : two / 2;
    }

    /// <summary>The t with P(|T| &gt; t) = p (the critical value for a two-tailed p, a 95% interval's at p 0.05).</summary>
    public static double TCritical(double p, double df) => TQuantile(1 - p / 2, df);

    /// <summary>The t with P(T &lt; t) = p, by bisection (the lower tail climbs steadily).</summary>
    public static double TQuantile(double p, double df)
    {
        if (!(p > 0 && p < 1) || df <= 0) return p <= 0 ? double.NegativeInfinity : p >= 1 ? double.PositiveInfinity : double.NaN;
        double lo = -1, hi = 1;
        while (TCdf(lo, df) > p) lo *= 2;
        while (TCdf(hi, df) < p) hi *= 2;
        for (var i = 0; i < 200 && hi - lo > 1e-13 * Math.Max(1, Math.Abs(hi)); i++)
        {
            var mid = (lo + hi) / 2;
            if (TCdf(mid, df) < p) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    /// <summary>The t distribution's density, for its curve.</summary>
    public static double TPdf(double t, double df) =>
        Math.Exp(LnGamma((df + 1) / 2) - LnGamma(df / 2)) / Math.Sqrt(df * Math.PI) * Math.Pow(1 + t * t / df, -(df + 1) / 2);

    // ---- the t-tests ----

    /// <summary>A sample's mean against μ₀ (from its numbers).</summary>
    public static TResult? OneSample(double[] data, double mu0)
    {
        if (Describe(data) is not { N: > 1 } d) return null;
        return OneSample(d.Mean, d.SampleSd, d.N, mu0);
    }

    /// <summary>A sample's mean against μ₀ (from its mean, sample SD and size).</summary>
    public static TResult? OneSample(double mean, double sd, int n, double mu0)
    {
        if (n < 2 || sd <= 0 || !double.IsFinite(mean) || !double.IsFinite(mu0)) return null;
        var se = sd / Math.Sqrt(n);
        var t = (mean - mu0) / se;
        double df = n - 1;
        var crit = TCritical(0.05, df);
        return new TResult(t, df, TTwoTailed(t, df), mean, mean - crit * se, mean + crit * se, se);
    }

    /// <summary>Two samples' means, Welch's way (their variances not assumed equal): the estimate is A's mean less B's.</summary>
    public static TResult? Welch(double[] a, double[] b)
    {
        if (Describe(a) is not { N: > 1 } da || Describe(b) is not { N: > 1 } db) return null;
        double va = da.SampleVar / da.N, vb = db.SampleVar / db.N;
        if (va + vb <= 0) return null;
        var se = Math.Sqrt(va + vb);
        var df = (va + vb) * (va + vb) / (va * va / (da.N - 1) + vb * vb / (db.N - 1));
        var diff = da.Mean - db.Mean;
        var t = diff / se;
        var crit = TCritical(0.05, df);
        return new TResult(t, df, TTwoTailed(t, df), diff, diff - crit * se, diff + crit * se, se);
    }

    /// <summary>Pairs' differences (A less B) against zero.</summary>
    public static TResult? Paired(double[] a, double[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        if (n < 2 || a.Length != b.Length) return null;
        var diffs = new double[n];
        for (var i = 0; i < n; i++) diffs[i] = a[i] - b[i];
        return OneSample(diffs, 0);
    }

    // ---- numbers as words ----

    /// <summary>A number to at most six significant figures, no float noise, a long one in e-notation; the minus is the
    /// typographic one.</summary>
    public static string Sig(double v, int figures = 6)
    {
        if (double.IsNaN(v)) return "—";
        if (double.IsPositiveInfinity(v)) return "∞";
        if (double.IsNegativeInfinity(v)) return "−∞";
        if (v == 0) return "0";
        var a = Math.Abs(v);
        string s;
        if (a >= 1e9 || a < 1e-4) s = v.ToString("0." + new string('#', Math.Max(0, figures - 1)) + "e+0", CultureInfo.InvariantCulture);
        else
        {
            var rounded = double.Parse(v.ToString("G" + figures, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            s = rounded.ToString("0.############", CultureInfo.InvariantCulture);
        }
        return s.Replace("-", "−");
    }

    /// <summary>A p-value: "0.0500", "< 0.0001".</summary>
    public static string PText(double p)
    {
        if (double.IsNaN(p)) return "—";
        if (p < 0.0001) return "< 0.0001";
        return p.ToString("0.0000", CultureInfo.InvariantCulture);
    }

    /// <summary>A number for the clipboard: ASCII signs.</summary>
    public static string Ascii(double v) => Sig(v).Replace("−", "-");
}

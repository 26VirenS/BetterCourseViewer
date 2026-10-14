using System.Globalization;

namespace Simpl.Tools;

// The biology toolkit's shared arithmetic (spec §5.6, §6.4), the Mac's BioMath.swift line for line: vectors in
// ångströms or micrometres, the dihedral angle with the IUPAC sign and NeRF placement (the peptide builder's and the
// χ angles'), SplitMix64 (every seeded picture, bit for bit the Mac's), exact fractions (the crosses' ratios) and the
// chi-square test with its p-value. Plain .NET — no WPF — so the unit tests check it on any machine.

/// <summary>A point or a direction in a molecule (Å) or a cell (µm).</summary>
public readonly record struct BioVec(double X, double Y, double Z)
{
    public static readonly BioVec Zero = new(0, 0, 0);

    public static BioVec operator +(BioVec a, BioVec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static BioVec operator -(BioVec a, BioVec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static BioVec operator -(BioVec a) => new(-a.X, -a.Y, -a.Z);
    public static BioVec operator *(BioVec a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static BioVec operator *(double s, BioVec a) => new(a.X * s, a.Y * s, a.Z * s);
    public static BioVec operator /(BioVec a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:0.###}, {Y:0.###}, {Z:0.###})");
}

/// <summary>Vector arithmetic on <see cref="BioVec"/>, by name (the Mac's BioV).</summary>
public static class BioV
{
    public static double Dot(BioVec a, BioVec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static BioVec Cross(BioVec a, BioVec b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static double Length(BioVec a) => Math.Sqrt(Dot(a, a));

    public static double Distance(BioVec a, BioVec b) => Length(a - b);

    /// <summary><paramref name="a"/> made one long (a zero vector stays zero).</summary>
    public static BioVec Unit(BioVec a)
    {
        var l = Length(a);
        return l > 1e-12 ? a / l : BioVec.Zero;
    }

    /// <summary>The angle between two directions, in degrees.</summary>
    public static double Angle(BioVec a, BioVec b)
    {
        var c = Dot(Unit(a), Unit(b));
        return Math.Acos(Math.Min(Math.Max(c, -1), 1)) * 180 / Math.PI;
    }

    /// <summary>The angle a–b–c at b, in degrees.</summary>
    public static double Angle(BioVec a, BioVec b, BioVec c) => Angle(a - b, c - b);

    /// <summary>
    /// The dihedral (torsion) angle a–b–c–d in degrees, with the IUPAC sign (§6.4): looking down b→c, positive when the
    /// near bond a–b must turn clockwise to eclipse the far bond c–d. A sign error here makes every right-handed helix
    /// left-handed.
    /// </summary>
    public static double Dihedral(BioVec a, BioVec b, BioVec c, BioVec d)
    {
        var b0 = a - b;
        var b1 = Unit(c - b);
        var b2 = d - c;
        var v = b0 - Dot(b0, b1) * b1;
        var w = b2 - Dot(b2, b1) * b1;
        return Math.Atan2(Dot(Cross(b1, v), w), Dot(v, w)) * 180 / Math.PI;
    }

    /// <summary>NeRF (§6.4): the atom D bonded to C, <paramref name="length"/> from it, with the angle B–C–D
    /// <paramref name="angle"/>° and the dihedral A–B–C–D <paramref name="torsion"/>°.</summary>
    public static BioVec Place(BioVec a, BioVec b, BioVec c, double length, double angle, double torsion)
    {
        var bc = Unit(c - b);
        var n = Unit(Cross(b - a, bc));
        var m = Cross(n, bc);
        double t = angle * Math.PI / 180, x = torsion * Math.PI / 180;
        var d = new BioVec(-length * Math.Cos(t), length * Math.Sin(t) * Math.Cos(x), length * Math.Sin(t) * Math.Sin(x));
        return c + d.X * bc + d.Y * m + d.Z * n;
    }

    /// <summary><paramref name="v"/> turned about the axis (through the origin) by <paramref name="degrees"/>, right-handed (Rodrigues).</summary>
    public static BioVec Rotate(BioVec v, BioVec axis, double degrees)
    {
        var k = Unit(axis);
        var t = degrees * Math.PI / 180;
        return v * Math.Cos(t) + Cross(k, v) * Math.Sin(t) + k * Dot(k, v) * (1 - Math.Cos(t));
    }

    /// <summary>The centre and radius of a ball holding every point (the centre of their bounds; for framing a camera).</summary>
    public static (BioVec Centre, double Radius) BoundingSphere(IReadOnlyList<BioVec> points)
    {
        if (points.Count == 0) return (BioVec.Zero, 1);
        BioVec lo = points[0], hi = points[0];
        foreach (var p in points)
        {
            lo = new BioVec(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
            hi = new BioVec(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
        }
        var c = (lo + hi) / 2;
        var r = 0.0;
        foreach (var p in points) r = Math.Max(r, Distance(p, c));
        return (c, Math.Max(r, 1e-6));
    }
}

// ---- SplitMix64 (§5.6.3) ----

/// <summary>
/// SplitMix64: the toolkit's seeded randomness, bit for bit the same on Windows and on the Mac (every operation wraps).
/// A cell built from its seed looks the same on every launch and on both platforms, so every draw is made in a fixed
/// order — the helpers below say exactly how many numbers each takes, in what order (the Mac's BioRandom).
/// </summary>
public struct BioRandom
{
    public ulong State;

    public BioRandom(ulong seed) => State = seed;

    public ulong Next()
    {
        unchecked
        {
            State += 0x9E3779B97F4A7C15UL;
            var z = State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>A double in [0, 1): the top 53 bits (one draw).</summary>
    public double Uniform() => (Next() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>lo + (hi − lo)·u (one draw).</summary>
    public double Uniform(double lo, double hi) => lo + (hi - lo) * Uniform();

    /// <summary>An integer in 0..&lt;n: ⌊u·n⌋ (one draw; 0 when n ≤ 0).</summary>
    public int Int(int below)
    {
        if (below <= 0) return 0;
        return Math.Min(below - 1, (int)(Uniform() * below));
    }

    /// <summary>A direction uniform on the unit sphere (two draws: z = 2u₁ − 1, then φ = 2π·u₂; x = ρ·cos φ, y = ρ·sin φ).</summary>
    public BioVec UnitVector()
    {
        var z = 2 * Uniform() - 1;
        var phi = 2 * Math.PI * Uniform();
        var rho = Math.Sqrt(Math.Max(0, 1 - z * z));
        return new BioVec(rho * Math.Cos(phi), rho * Math.Sin(phi), z);
    }

    /// <summary>A point uniform in the unit ball (three draws a try: x, y, z each 2u − 1, kept when inside).</summary>
    public BioVec InBall()
    {
        while (true)
        {
            var x = 2 * Uniform() - 1;
            var y = 2 * Uniform() - 1;
            var z = 2 * Uniform() - 1;
            var p = new BioVec(x, y, z);
            if (BioV.Dot(p, p) <= 1) return p;
        }
    }

    /// <summary>A random turn: its axis (<see cref="UnitVector"/>, two draws) and its angle in degrees (360·u, one draw), in that order.</summary>
    public (BioVec Axis, double Degrees) Rotation()
    {
        var axis = UnitVector();
        return (axis, 360 * Uniform());
    }
}

// ---- exact fractions (§5.6.1) ----

/// <summary>A fraction kept exact (num/den, reduced, den &gt; 0): the crosses' probabilities and ratios, never rounded until shown.</summary>
public readonly struct BioFrac : IEquatable<BioFrac>, IComparable<BioFrac>
{
    public long Num { get; }
    public long Den { get; }

    public BioFrac(long num, long den = 1)
    {
        if (den == 0) throw new DivideByZeroException("a fraction over zero");
        var g = Gcd(num, den);
        long s = den < 0 ? -1 : 1;
        Num = s * num / Math.Max(g, 1);
        Den = s * den / Math.Max(g, 1);
    }

    public static readonly BioFrac Zero = new(0, 1);
    public static readonly BioFrac One = new(1, 1);

    public static long Gcd(long a, long b)
    {
        ulong x = (ulong)Math.Abs(a), y = (ulong)Math.Abs(b);
        while (y != 0) (x, y) = (y, x % y);
        return (long)x;
    }

    public static BioFrac operator +(BioFrac a, BioFrac b) => new(a.Num * b.Den + b.Num * a.Den, a.Den * b.Den);
    public static BioFrac operator -(BioFrac a, BioFrac b) => new(a.Num * b.Den - b.Num * a.Den, a.Den * b.Den);
    public static BioFrac operator *(BioFrac a, BioFrac b) => new(a.Num * b.Num, a.Den * b.Den);
    public static BioFrac operator /(BioFrac a, BioFrac b) => new(a.Num * b.Den, a.Den * b.Num);
    public static bool operator <(BioFrac a, BioFrac b) => a.Num * b.Den < b.Num * a.Den;
    public static bool operator >(BioFrac a, BioFrac b) => b < a;
    public static bool operator ==(BioFrac a, BioFrac b) => a.Equals(b);
    public static bool operator !=(BioFrac a, BioFrac b) => !a.Equals(b);

    // (reduced, so equal fractions have equal parts; default(BioFrac), 0/0, is read as zero)
    public bool Equals(BioFrac o) => Num == o.Num && Math.Max(Den, 1) == Math.Max(o.Den, 1);
    public override bool Equals(object? obj) => obj is BioFrac f && Equals(f);
    public override int GetHashCode() => HashCode.Combine(Num, Math.Max(Den, 1));
    public int CompareTo(BioFrac o) => this < o ? -1 : (o < this ? 1 : 0);

    public double Double => Den == 0 ? 0 : (double)Num / Den;
    public bool IsZero => Num == 0;

    /// <summary>"3/32"; a whole number as itself ("1", "0").</summary>
    public string Text => Den <= 1 ? $"{Num}" : $"{Num}/{Den}";

    public override string ToString() => Text;

    /// <summary>As a percent with one decimal: "9.4%" (3/32), "50.0%".</summary>
    public string PercentText => (Double * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}

/// <summary>Ratios of whole counts (the crosses' "9 : 3 : 3 : 1").</summary>
public static class BioRatio
{
    /// <summary>The counts divided by their greatest common divisor, as a whole: [2, 4, 2] → [1, 2, 1].</summary>
    public static int[] Reduce(IReadOnlyList<int> counts)
    {
        long g = 0;
        foreach (var c in counts) g = BioFrac.Gcd(g, c);
        if (g <= 1) return counts.ToArray();
        return counts.Select(c => (int)(c / g)).ToArray();
    }

    /// <summary>The smallest whole numbers in the proportion of the fractions (1/4, 1/2, 1/4 → 1, 2, 1).</summary>
    public static int[] Whole(IReadOnlyList<BioFrac> fracs)
    {
        if (fracs.Count == 0) return Array.Empty<int>();
        long l = 1;
        foreach (var f in fracs) l = l / BioFrac.Gcd(l, f.Den) * f.Den;
        return Reduce(fracs.Select(f => (int)(f.Num * (l / f.Den))).ToArray());
    }

    /// <summary>"1 : 2 : 1".</summary>
    public static string Text(IEnumerable<int> counts) => string.Join(" : ", counts);
}

// ---- chi-square (§5.3.3, §5.3.4, §5.6.4) ----

/// <summary>
/// The chi-square goodness-of-fit test as the AP sheet has it: χ² = Σ (o − e)²/e, df = categories − 1 (or what the
/// caller says: the Hardy–Weinberg test uses 1), the critical value at p = 0.05 and the p-value, and the conclusion in
/// AP's words — "reject" or "fail to reject" the null hypothesis, never "accept" or "prove".
/// </summary>
public static class BioChiSquare
{
    /// <summary>The p column heads of the critical-value table.</summary>
    public static readonly double[] TableP = { 0.10, 0.05, 0.01, 0.001 };

    private static readonly Lazy<Dictionary<int, double[]>> table = new(() =>
    {
        var t = new Dictionary<int, double[]>();
        foreach (var line in BioText.Lines(BioDataText.ChiSquareText))
        {
            var cells = line.Split('\t', StringSplitOptions.RemoveEmptyEntries).Select(c => BioText.Number(c.Trim())).Where(d => d != null).Select(d => d!.Value).ToArray();
            if (cells.Length != 5) continue;
            t[(int)cells[0]] = cells.Skip(1).ToArray();
        }
        return t;
    });

    /// <summary>§5.3.4: critical values by df (1–10), one for each of <see cref="TableP"/>.</summary>
    public static IReadOnlyDictionary<int, double[]> Table => table.Value;

    /// <summary>The tabled critical value at p (0.10, 0.05, 0.01 or 0.001) for df 1–10, else computed (§5.6.4's bisection).</summary>
    public static double Critical(int df, double p = 0.05)
    {
        var i = Array.FindIndex(TableP, x => Math.Abs(x - p) < 1e-12);
        if (Table.TryGetValue(df, out var row) && i >= 0) return row[i];
        return CriticalComputed(df, p);
    }

    /// <summary>The χ² at which the p-value is p: bisection on [0, 200], 200 iterations (§5.6.4).</summary>
    public static double CriticalComputed(int df, double p)
    {
        double lo = 0, hi = 200;
        for (var k = 0; k < 200; k++)
        {
            var mid = (lo + hi) / 2;
            if (PValue(mid, df) > p) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    /// <summary>p = Q(df/2, χ²/2), the regularized upper incomplete gamma function (Numerical Recipes).</summary>
    public static double PValue(double chi2, int df)
    {
        if (df <= 0) return double.NaN;
        if (chi2 <= 0) return 1;
        return GammaQ(df / 2.0, chi2 / 2);
    }

    private static readonly double[] Lanczos =
    {
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
        12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
    };

    /// <summary>ln Γ(x) for x &gt; 0: Lanczos, g = 7, n = 9 (the Mac's series too, so both get the same p-values).</summary>
    public static double LnGamma(double x)
    {
        const double g = 7.0;
        // (the reflection formula)
        if (x < 0.5) return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LnGamma(1 - x);
        var z = x - 1;
        var a = Lanczos[0];
        var t = z + g + 0.5;
        for (var i = 1; i < 9; i++) a += Lanczos[i] / (z + i);
        return 0.5 * Math.Log(2 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    /// <summary>The regularized lower incomplete gamma P(a, x) by its series (for x &lt; a + 1).</summary>
    public static double GammaPSeries(double a, double x)
    {
        var ap = a;
        var sum = 1.0 / a;
        var del = sum;
        for (var n = 0; n < 10_000; n++)
        {
            ap += 1;
            del *= x / ap;
            sum += del;
            if (Math.Abs(del) < Math.Abs(sum) * 1e-15) break;
        }
        return sum * Math.Exp(-x + a * Math.Log(x) - LnGamma(a));
    }

    /// <summary>The regularized upper incomplete gamma Q(a, x) by its continued fraction (modified Lentz; for x ≥ a + 1).</summary>
    public static double GammaQFraction(double a, double x)
    {
        const double tiny = 1e-300, eps = 1e-15;
        var b = x + 1 - a;
        var c = 1 / tiny;
        var d = 1 / b;
        var h = d;
        var i = 1.0;
        while (i < 10_000)
        {
            var an = -i * (i - a);
            b += 2;
            d = an * d + b;
            if (Math.Abs(d) < tiny) d = tiny;
            c = b + an / c;
            if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d;
            var del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < eps) break;
            i += 1;
        }
        return Math.Exp(-x + a * Math.Log(x) - LnGamma(a)) * h;
    }

    public static double GammaQ(double a, double x) => x < a + 1 ? 1 - GammaPSeries(a, x) : GammaQFraction(a, x);

    /// <summary>A test's working, as the student writes it out.</summary>
    public sealed record Result(double[] Observed, double[] Expected, double[] Terms, double Chi2, int Df, double Critical, double P)
    {
        /// <summary>χ² above the critical value: the null hypothesis is rejected (p &lt; 0.05).</summary>
        public bool Reject => Chi2 > Critical;

        /// <summary>"χ² = 2.13 &lt; 7.81 (df = 3, p = 0.05), so we fail to reject the null hypothesis." (AP's words.)</summary>
        public string Conclusion
        {
            get
            {
                string a = Chi2.ToString("0.00", CultureInfo.InvariantCulture), b = Critical.ToString("0.00", CultureInfo.InvariantCulture);
                return Reject
                    ? $"χ² = {a} > {b} (df = {Df}, p = 0.05), so we reject the null hypothesis."
                    : $"χ² = {a} < {b} (df = {Df}, p = 0.05), so we fail to reject the null hypothesis.";
            }
        }
    }

    /// <summary>The test of observed counts against expected ones (df = categories − 1 unless given).</summary>
    public static Result Test(IReadOnlyList<double> observed, IReadOnlyList<double> expected, int? df = null)
    {
        var n = Math.Min(observed.Count, expected.Count);
        var terms = new double[n];
        for (var i = 0; i < n; i++)
        {
            var e = expected[i];
            terms[i] = e > 0 ? (observed[i] - e) * (observed[i] - e) / e : 0;
        }
        var chi2 = terms.Sum();
        var f = df ?? Math.Max(n - 1, 1);
        return new Result(observed.Take(n).ToArray(), expected.Take(n).ToArray(), terms, chi2, f, Critical(f), PValue(chi2, f));
    }

    /// <summary>Expected counts for a ratio (9 : 3 : 3 : 1) and a total.</summary>
    public static double[] Expected(IReadOnlyList<double> ratio, double total)
    {
        var s = ratio.Sum();
        return ratio.Select(r => s > 0 ? r / s * total : 0).ToArray();
    }
}

/// <summary>Reading the shared text: its lines (a Windows checkout's carriage returns dropped), and numbers as the
/// specification writes them (a minus sign "−" read as "-").</summary>
public static class BioText
{
    /// <summary>The text's lines, empty ones dropped unless asked (the Mac's split(separator: "\n")).</summary>
    public static List<string> Lines(string text, bool keepEmpty = false)
    {
        var out_ = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (l.Length == 0 && !keepEmpty) continue;
            out_.Add(l);
        }
        return out_;
    }

    /// <summary>Words split at spaces, empty ones dropped (the Mac's split(separator: " ")).</summary>
    public static string[] Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A number as Swift's Double(_:) reads it (invariant; "−" taken as "-" only when asked), else null.</summary>
    public static double? Number(string s, bool unicodeMinus = false)
    {
        if (unicodeMinus) s = s.Replace('−', '-');
        if (s.Length == 0 || char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])) return null;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>A whole number as Swift's Int(_:) reads it ("+1", "-1", "12"), else null.</summary>
    public static int? Int(string s) =>
        s.Length > 0 && !char.IsWhiteSpace(s[0]) && !char.IsWhiteSpace(s[^1]) && int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i) ? i : null;
}

/// <summary>
/// Numbers shown to a student, rounded the one way every bio tool rounds on both apps (uifix/conventions.md): decimal
/// half-up — the number as it reads in decimal (15 significant digits, so 3.425 is 3.425, not the 3.42499… a double
/// holds), a 5 rounded up, away from zero: 3.425 → "3.43", 0.125 → "0.13", 0.575 → "0.58". .NET's custom formats
/// ("0.00") round this way; its standard ones ("F2", "N2") and Math.Round(x, 2) do not (they round the double's exact
/// value, or a tie to even), so a tool's numbers go through here or a custom format.
/// </summary>
public static class BioRound
{
    /// <summary>The value rounded half-up at <paramref name="decimals"/> places (0–15).</summary>
    public static double Value(double x, int decimals) =>
        double.IsFinite(x) ? double.Parse(Fixed(x, decimals), NumberStyles.Float, CultureInfo.InvariantCulture) : x;

    /// <summary>"%.{decimals}f", rounded half-up: "0.13", "12", "-2.45"; a value that rounds to zero is "0.00", never "-0.00".</summary>
    public static string Fixed(double x, int decimals)
    {
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";
        var d = Math.Clamp(decimals, 0, 15);
        var s = x.ToString(d > 0 ? "0." + new string('0', d) : "0", CultureInfo.InvariantCulture);
        return s.StartsWith('-') && s.AsSpan(1).IndexOfAnyExcept("0.") < 0 ? s[1..] : s;
    }

    /// <summary>Up to <paramref name="decimals"/> places, trailing zeros dropped ("0.##"): "2.5", "3", "0.13".</summary>
    public static string Upto(double x, int decimals)
    {
        var s = Fixed(x, decimals);
        return s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
    }
}

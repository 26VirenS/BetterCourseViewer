using System.Globalization;

namespace Simpl.Tools;

// The chemistry calculators' numbers (the Equation Balancer, Molar Mass & Stoichiometry, Gas Laws, Solutions & pH):
// read from a field, and written with sensible figures — never float noise like 0.30000000000000004. Plain .NET, no
// WPF, so the unit tests check them on any machine.

/// <summary>A number as the chemistry tools read and write one.</summary>
public static class ChemNum
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A field's number: "1,000", "−3.5", "6.02e23"; null for anything else (a blank is null, not zero).</summary>
    public static double? Read(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Replace(",", "").Replace("−", "-").Replace("×10^", "e").Trim();
        return double.TryParse(t, NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v : null;
    }

    /// <summary>
    /// A value to so many significant figures (at most 6), written out — "18.015", "0.5551", "4.92" — and in e-notation
    /// when very large or very small: "3.343e23", "1e-11". Trailing zeros go; a result never shows the float's noise.
    /// </summary>
    public static string Sig(double v, int figures = 4)
    {
        if (!double.IsFinite(v)) return "—";
        if (v == 0) return "0";
        var f = Math.Clamp(figures, 1, 6);
        var a = Math.Abs(v);
        if (a >= 1e7 || a < 1e-4)
        {
            var e = v.ToString("0." + new string('#', f - 1) + "e+0", Inv);
            return e.Replace("e+", "e");
        }
        var g = v.ToString("G" + f, Inv);
        return decimal.TryParse(g, NumberStyles.Float, Inv, out var d) ? d.ToString("0.############", Inv) : g;
    }

    /// <summary>A value to so many decimal places, rounded half-up ("18.015", "7.00"); never "-0.00".</summary>
    public static string Fixed(double v, int decimals)
    {
        if (!double.IsFinite(v)) return "—";
        var d = Math.Clamp(decimals, 0, 10);
        var s = v.ToString(d > 0 ? "0." + new string('0', d) : "0", Inv);
        return s.StartsWith('-') && s.AsSpan(1).IndexOfAnyExcept("0.") < 0 ? s[1..] : s;
    }

    /// <summary>A value with its unit: "18.015 g/mol", "4.92 atm".</summary>
    public static string With(double v, string unit, int figures = 4) => $"{Sig(v, figures)} {unit}";

    /// <summary>How many significant figures a typed number has ("0.0820" has three; "100" three), for an answer that
    /// matches its input's precision: at least 3, at most 6.</summary>
    public static int FiguresOf(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return 4;
        var t = typed.Trim().Replace(",", "");
        var e = t.IndexOfAny(new[] { 'e', 'E' });
        if (e >= 0) t = t[..e];
        var digits = t.Where(char.IsDigit).ToList();
        var mantissa = string.Concat(digits).TrimStart('0');
        return Math.Clamp(mantissa.Length, 3, 6);
    }

    /// <summary>The greatest common divisor of two whole numbers (0 for 0 and 0).</summary>
    public static long Gcd(long a, long b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

    /// <summary>The least common multiple of two whole numbers, checked against overflow.</summary>
    public static long Lcm(long a, long b)
    {
        if (a == 0 || b == 0) return 0;
        return checked(Math.Abs(a) / Gcd(a, b) * Math.Abs(b));
    }
}

/// <summary>
/// An exact fraction over 64-bit integers, kept reduced with a positive denominator, every sum checked for overflow
/// (an equation whose coefficients would need more is reported, never balanced wrongly) — the balancer's arithmetic.
/// </summary>
public readonly record struct ChemFraction(long Num, long Den)
{
    public static readonly ChemFraction Zero = new(0, 1);
    public static readonly ChemFraction One = new(1, 1);

    public static ChemFraction Of(long n, long d = 1)
    {
        if (d == 0) throw new DivideByZeroException();
        if (d < 0) { n = checked(-n); d = checked(-d); }
        var g = ChemNum.Gcd(n, d);
        if (g > 1) { n /= g; d /= g; }
        return new ChemFraction(n, d);
    }

    public bool IsZero => Num == 0;
    public int Sign => Math.Sign(Num);

    public static ChemFraction operator +(ChemFraction a, ChemFraction b) => Of(checked(a.Num * b.Den + b.Num * a.Den), checked(a.Den * b.Den));
    public static ChemFraction operator -(ChemFraction a, ChemFraction b) => Of(checked(a.Num * b.Den - b.Num * a.Den), checked(a.Den * b.Den));
    public static ChemFraction operator *(ChemFraction a, ChemFraction b) => Of(checked(a.Num * b.Num), checked(a.Den * b.Den));
    public static ChemFraction operator /(ChemFraction a, ChemFraction b) => Of(checked(a.Num * b.Den), checked(a.Den * b.Num));
    public static ChemFraction operator -(ChemFraction a) => new(checked(-a.Num), a.Den);

    public override string ToString() => Den == 1 ? Num.ToString(CultureInfo.InvariantCulture) : $"{Num}/{Den}";
}

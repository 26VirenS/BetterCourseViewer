using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Simpl.Tools;

// The Bio Lab Calculator's numbers (spec §5.5): a number as a student types it — "0.50", "3,000", "1.5e9", "1 × 10^-7",
// "10⁻⁶" — with its significant figures counted as on paper; a result written to so many significant figures, never more
// than the least precise input plus one; and the units each quantity can be typed in (the SI prefixes as menus), with
// the metric prefix table of the AP sheet. Plain .NET, no WPF: the calculators and the unit tests use it.

/// <summary>A number as typed: its value, its significant figures (0: exact — a count, a magnification, a dilution
/// factor written as a power of ten), and its decimal places (for a mean written one place past its data).</summary>
public readonly record struct LabNumber(double Value, int Figures, int Decimals)
{
    /// <summary>An exact number: it does not limit how many figures an answer may have.</summary>
    public bool Exact => Figures == 0;
}

public static class LabNumbers
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string Superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹";

    // "−1,234.5e-6", "1.5 × 10^9", "1.5x10^9", "10^-6" (after superscripts are read as ^ and digits)
    private static readonly Regex PlainRx = new(@"^(?<s>[+-]?)(?<m>\d[\d,]*\.?\d*|\.\d+)(?:[eE](?<e>[+-]?\d+))?$", RegexOptions.CultureInvariant);
    private static readonly Regex TimesRx = new(@"^(?<s>[+-]?)(?<m>\d[\d,]*\.?\d*|\.\d+)[x*]10\^?(?<e>[+-]?\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex PowerRx = new(@"^(?<s>[+-]?)10\^(?<e>[+-]?\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex GroupedRx = new(@"^\d{1,3}(,\d{3})+(\.\d*)?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// A typed number, or null. Its significant figures are counted as on paper: with a decimal point, every digit from
    /// the first that is not zero ("0.050" has 2, "2.0" has 2); without one, trailing zeros are not counted ("250" has 2,
    /// "3,000" has 1). <paramref name="exact"/> (a count, a setting): no limit on the answer's figures. Zero, and a power
    /// of ten written alone ("10^-6"), are exact.
    /// </summary>
    public static LabNumber? Parse(string? text, bool exact = false)
    {
        if (text == null) return null;
        var s = Normal(text);
        if (s.Length == 0) return null;
        // (a ratio "1:10" or a fraction "1/10": exact, as a dilution is written)
        foreach (var sep in new[] { ':', '/' })
        {
            var at = s.IndexOf(sep);
            if (at < 0) continue;
            if (s.IndexOf(sep, at + 1) >= 0 || Parse(s[..at]) is not { } top || Parse(s[(at + 1)..]) is not { } bottom || bottom.Value == 0) return null;
            return new LabNumber(top.Value / bottom.Value, 0, 0);
        }
        string sign, mant;
        int exp;
        Match m;
        if ((m = PowerRx.Match(s)).Success)
        {
            if (!int.TryParse(m.Groups["e"].Value, NumberStyles.AllowLeadingSign, Inv, out exp) || Math.Abs(exp) > 300) return null;
            var p = Math.Pow(10, exp) * (m.Groups["s"].Value == "-" ? -1 : 1);
            return new LabNumber(p, 0, Math.Max(0, -exp));
        }
        if ((m = TimesRx.Match(s)).Success || (m = PlainRx.Match(s)).Success)
        {
            sign = m.Groups["s"].Value;
            mant = m.Groups["m"].Value;
            exp = 0;
            if (m.Groups["e"].Success && !int.TryParse(m.Groups["e"].Value, NumberStyles.AllowLeadingSign, Inv, out exp)) return null;
            if (Math.Abs(exp) > 300) return null;
        }
        else return null;
        // (commas only as thousands: "3,000", "12,500.5" — never "1,5")
        if (mant.Contains(',') && !GroupedRx.IsMatch(mant)) return null;
        mant = mant.Replace(",", "");
        if (!double.TryParse(mant, NumberStyles.Float, Inv, out var mv)) return null;
        var value = mv * Math.Pow(10, exp) * (sign == "-" ? -1 : 1);
        if (!double.IsFinite(value)) return null;
        var dot = mant.IndexOf('.');
        var decimals = (dot < 0 ? 0 : mant.Length - dot - 1) - exp;
        if (exact || value == 0) return new LabNumber(value, 0, Math.Max(0, decimals));
        return new LabNumber(value, Figures(mant), decimals);
    }

    /// <summary>A mantissa's significant figures ("0.050" → 2, "250" → 2, "250." → 3, "2.0" → 2).</summary>
    public static int Figures(string mantissa)
    {
        var digits = mantissa.Replace(",", "");
        var hasDot = digits.Contains('.');
        digits = digits.Replace(".", "").TrimStart('0');
        if (!hasDot) digits = digits.TrimEnd('0');
        return Math.Max(1, digits.Length);
    }

    /// <summary>The text as the parser reads it: no spaces, the minus sign and × read as - and x, superscript powers as ^.</summary>
    private static string Normal(string text)
    {
        var b = new StringBuilder();
        var inSup = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch)) continue;
            var sup = Superscripts.IndexOf(ch);
            var supSign = ch is '⁻' or '⁺';
            if (sup >= 0 || supSign)
            {
                if (!inSup) b.Append('^');
                inSup = true;
                b.Append(sup >= 0 ? (char)('0' + sup) : ch == '⁻' ? '-' : '+');
                continue;
            }
            inSup = false;
            b.Append(ch switch { '−' or '–' => '-', '×' or 'X' or '·' => 'x', _ => ch });
        }
        return b.ToString();
    }

    /// <summary>A list of numbers ("12.1, 13.4 11.8; 12.9"), or null with the first word that is not a number.</summary>
    public static (List<LabNumber>? Numbers, string? Bad) ParseList(string? text, bool exact = false)
    {
        var list = new List<LabNumber>();
        foreach (var w in (text ?? "").Split(new[] { ',', ';', ' ', '\t', '\r', '\n', ':' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Parse(w, exact) is not { } n) return (null, w);
            list.Add(n);
        }
        return (list, null);
    }

    /// <summary>
    /// How many significant figures an answer gets: the setting (3 unless changed, 2–6), but never more than the least
    /// precise measured input plus one. Exact inputs do not count.
    /// </summary>
    public static int Allowed(int setting, IEnumerable<LabNumber> inputs)
    {
        var least = inputs.Where(n => !n.Exact).Select(n => n.Figures).DefaultIfEmpty(int.MaxValue).Min();
        return least == int.MaxValue ? setting : Math.Min(setting, least + 1);
    }

    // ---- writing numbers ----

    /// <summary>The power of ten from which an answer is written in scientific notation (10⁵: "8.4 × 10⁵ cells/mL").</summary>
    public const int ScientificFrom = 5;

    /// <summary>"⁻⁴", "¹¹".</summary>
    public static string Sup(int n)
    {
        var b = new StringBuilder();
        foreach (var ch in n.ToString(Inv)) b.Append(ch == '-' ? '⁻' : Superscripts[ch - '0']);
        return b.ToString();
    }

    /// <summary>A minus sign for a hyphen (the typographer's −).</summary>
    public static string Minus(string s) => s.Replace('-', '−');

    /// <summary>
    /// A value to so many significant figures, as on paper: "7.31", "0.450", "5.0", "−2.45"; "8.04 × 10⁻⁵" and "1.5 × 10⁹"
    /// from 10⁵ up and below 0.001; and a whole-number answer below that keeps its whole digits ("1,006", "6,400",
    /// "3,000" — never "1,010"). <paramref name="trim"/>: trailing zeros dropped (an exact answer: "0.62", "8.4 × 10⁵", "400").
    /// </summary>
    public static string Sig(double v, int figures, bool trim = false)
    {
        if (!double.IsFinite(v)) return "—";
        if (v == 0) return "0";
        figures = Math.Clamp(figures, 1, 15);
        var neg = v < 0;
        var a = Math.Abs(v);
        string body;
        if (a >= 1e20 || a < 1e-20)
        {
            body = a.ToString("E" + (figures - 1), Inv);
            var parts = body.Split('E');
            var mant = parts[0];
            if (trim && mant.Contains('.')) mant = mant.TrimEnd('0').TrimEnd('.');
            body = $"{mant} × 10{Sup(int.Parse(parts[1], Inv))}";
            return (neg ? "−" : "") + body;
        }
        var (m, e) = Mantissa(a, figures);
        if (e >= ScientificFrom || e < -3)
        {
            var mant = m.ToString("F" + (figures - 1), Inv);
            if (trim && mant.Contains('.')) mant = mant.TrimEnd('0').TrimEnd('.');
            body = $"{mant} × 10{Sup(e)}";
        }
        else if (e >= figures - 1)
        {
            // (a whole-number answer: its whole digits kept)
            var whole = Math.Round((decimal)a, 0, MidpointRounding.AwayFromZero);
            body = whole.ToString("N0", Inv);
        }
        else
        {
            var decimals = figures - 1 - e;
            var r = Math.Round((decimal)a, decimals, MidpointRounding.AwayFromZero);
            body = r.ToString("N" + decimals, Inv);
            if (trim && body.Contains('.')) body = body.TrimEnd('0').TrimEnd('.');
        }
        return (neg ? "−" : "") + body;
    }

    /// <summary>The value's mantissa rounded to so many figures (1 ≤ m &lt; 10) and its power of ten, in decimal (so 7.305
    /// rounds to 7.31, as typed, not to the binary 7.30499…).</summary>
    private static (decimal M, int E) Mantissa(double a, int figures)
    {
        var d = (decimal)a;
        var e = (int)Math.Floor(Math.Log10(a));
        var m = d / Pow10(e);
        // (Log10 can land one off at an exact power)
        if (m >= 10) { m /= 10; e++; }
        if (m < 1) { m *= 10; e--; }
        m = Math.Round(m, figures - 1, MidpointRounding.AwayFromZero);
        if (m >= 10) { m /= 10; e++; }
        return (m, e);
    }

    private static decimal Pow10(int e)
    {
        decimal p = 1;
        if (e >= 0) for (var i = 0; i < e; i++) p *= 10;
        else for (var i = 0; i < -e; i++) p /= 10;
        return p;
    }

    /// <summary>A value to so many decimal places ("12.64"; a mean one place past its data).</summary>
    public static string Fixed(double v, int decimals)
    {
        if (!double.IsFinite(v)) return "—";
        decimals = Math.Clamp(decimals, 0, 12);
        if (Math.Abs(v) >= 1e20) return Sig(v, 6, trim: true);
        var r = Math.Round((decimal)v, decimals, MidpointRounding.AwayFromZero);
        return Minus(r.ToString("N" + decimals, Inv));
    }

    /// <summary>A working value, as a step writes it: up to six figures, trailing zeros dropped ("0.25", "7.305", "1 × 10⁻⁴").</summary>
    public static string Plain(double v)
    {
        if (!double.IsFinite(v)) return "—";
        if (v == 0) return "0";
        var a = Math.Abs(v);
        var e = (int)Math.Floor(Math.Log10(a));
        if (e >= 6 || e < -3) return Sig(v, 6, trim: true);
        var r = Math.Round((decimal)v, Math.Clamp(5 - e, 0, 12), MidpointRounding.AwayFromZero);
        var s = r.ToString(r == Math.Round(r) ? "N0" : "N" + Math.Clamp(5 - e, 0, 12), Inv);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return Minus(s);
    }

    /// <summary>A typed number written back neatly: "1e-7" → "1 × 10⁻⁷", "-2" → "−2"; anything else as typed.</summary>
    public static string Pretty(string text)
    {
        var t = text.Trim();
        var s = Normal(t);
        Match m;
        if ((m = PowerRx.Match(s)).Success) return Minus(m.Groups["s"].Value) + "10" + Sup(int.Parse(m.Groups["e"].Value, Inv));
        if (((m = TimesRx.Match(s)).Success || (m = PlainRx.Match(s)).Success) && m.Groups["e"].Success)
            return Minus(m.Groups["s"].Value) + m.Groups["m"].Value + " × 10" + Sup(int.Parse(m.Groups["e"].Value, Inv));
        return Minus(t.Replace(" ", ""));
    }
}

// ---- units ----

/// <summary>What a quantity measures (its unit menu).</summary>
public enum LabDim
{
    /// <summary>A plain number (no unit menu).</summary>
    Number,
    Concentration,
    Volume,
    Mass,
    Amount,
    MolarMass,
    Length,
    Time,
    Temperature,
    Pressure,
    MassConcentration,
    Absorptivity,
    PathLength,
    Speed,
    /// <summary>A unit that is only a word carried through (cells/mL, individuals): no conversion between them.</summary>
    Label,
    /// <summary>"per day", "per hour": the time a rate is per (a word carried through).</summary>
    PerTime,
}

/// <summary>A unit: its symbol, its name, and how it stands to its quantity's base unit (so many base units each, and an
/// offset: the kelvin's only).</summary>
public sealed record LabUnit(string Symbol, string Name, double Factor, double Offset = 0)
{
    public double ToBase(double v) => v * Factor + Offset;
    public double FromBase(double b) => (b - Offset) / Factor;
}

public static class LabUnits
{
    private static LabUnit U(string symbol, string name, double factor, double offset = 0) => new(symbol, name, factor, offset);

    /// <summary>Each quantity's units, the base unit first where there is one (M, L, g, mol, m, min, °C, bar, µg/mL).</summary>
    public static readonly IReadOnlyDictionary<LabDim, LabUnit[]> All = new Dictionary<LabDim, LabUnit[]>
    {
        [LabDim.Number] = new[] { U("", "", 1) },
        [LabDim.Concentration] = new[] { U("M", "molar (mol/L)", 1), U("mM", "millimolar", 1e-3), U("µM", "micromolar", 1e-6), U("nM", "nanomolar", 1e-9) },
        [LabDim.Volume] = new[] { U("L", "litres", 1), U("mL", "millilitres", 1e-3), U("µL", "microlitres", 1e-6) },
        [LabDim.Mass] = new[] { U("g", "grams", 1), U("kg", "kilograms", 1e3), U("mg", "milligrams", 1e-3), U("µg", "micrograms", 1e-6), U("ng", "nanograms", 1e-9) },
        [LabDim.Amount] = new[] { U("mol", "moles", 1), U("mmol", "millimoles", 1e-3), U("µmol", "micromoles", 1e-6), U("nmol", "nanomoles", 1e-9) },
        [LabDim.MolarMass] = new[] { U("g/mol", "grams per mole", 1) },
        [LabDim.Length] = new[] { U("m", "metres", 1), U("cm", "centimetres", 1e-2), U("mm", "millimetres", 1e-3), U("µm", "micrometres", 1e-6), U("nm", "nanometres", 1e-9) },
        [LabDim.Time] = new[] { U("min", "minutes", 1), U("s", "seconds", 1.0 / 60), U("h", "hours", 60), U("days", "days", 1440) },
        [LabDim.Temperature] = new[] { U("°C", "degrees Celsius", 1), U("K", "kelvin", 1, -273) },
        [LabDim.Pressure] = new[] { U("bar", "bars", 1), U("MPa", "megapascals", 10), U("kPa", "kilopascals", 0.01) },
        [LabDim.MassConcentration] = new[] { U("µg/mL", "micrograms per millilitre", 1), U("ng/µL", "nanograms per microlitre", 1), U("mg/mL", "milligrams per millilitre", 1000) },
        [LabDim.Absorptivity] = new[] { U("M⁻¹cm⁻¹", "per molar per centimetre", 1), U("mM⁻¹cm⁻¹", "per millimolar per centimetre", 1000) },
        [LabDim.PathLength] = new[] { U("cm", "centimetres", 1), U("mm", "millimetres", 0.1) },
        [LabDim.Speed] = new[] { U("rpm", "revolutions per minute", 1) },
        [LabDim.Label] = new[] { U("M", "molar", 1), U("mM", "millimolar", 1), U("µM", "micromolar", 1), U("cells/mL", "cells per millilitre", 1), U("CFU/mL", "colony-forming units per millilitre", 1), U("µg/mL", "micrograms per millilitre", 1), U("mL", "millilitres", 1), U("mg", "milligrams", 1), U("cm", "centimetres", 1), U("µmol", "micromoles", 1), U("individuals", "individuals", 1), U("units", "units", 1) },
        [LabDim.PerTime] = new[] { U("day", "per day", 1), U("year", "per year", 1), U("hour", "per hour", 1), U("minute", "per minute", 1), U("generation", "per generation", 1) },
    };

    public static IReadOnlyList<LabUnit> Of(LabDim d) => All[d];

    /// <summary>A quantity's unit by its symbol (its first, if none has it).</summary>
    public static LabUnit Find(LabDim d, string? symbol) => All[d].FirstOrDefault(u => u.Symbol == symbol) ?? All[d][0];

    /// <summary>The unit of a quantity a value reads best in (1 to 1,000 of it), or null when there is no choice.</summary>
    public static LabUnit? Nice(LabDim d, double baseValue)
    {
        if (d is LabDim.Number or LabDim.Label or LabDim.PerTime or LabDim.Temperature || !double.IsFinite(baseValue) || baseValue == 0) return null;
        var units = All[d].Where(u => u.Offset == 0).OrderByDescending(u => u.Factor).ToList();
        if (units.Count < 2) return null;
        foreach (var u in units)
        {
            var x = Math.Abs(u.FromBase(baseValue));
            if (x >= 1 && x < 1000) return u;
        }
        return null;
    }

    /// <summary>§5.5's metric prefix table (the AP sheet's): giga G 10⁹ … pico p 10⁻¹² — its name, symbol, power.</summary>
    public static readonly IReadOnlyList<(string Name, string Symbol, int Power)> Prefixes = LabSpecText.Prefixes.Split(',')
        .Select(e => e.Trim().Split(' '))
        .Select(p => (p[0], p[1], PowerOf(p[2])))
        .ToList();

    /// <summary>"10⁻¹²" → −12.</summary>
    private static int PowerOf(string s)
    {
        var n = LabNumbers.Parse(s);
        return n is { } x ? (int)Math.Round(Math.Log10(x.Value)) : 0;
    }
}

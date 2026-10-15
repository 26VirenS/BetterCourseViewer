using System.Globalization;

namespace Simpl.Tools;

// The physics tools' numbers (tools), the Mac's PhysMath line for line: a field's number read, and a result written to
// four significant figures with no float noise ("40.77 m", "2.883 s", "0.03 A"), the minus the typeset "−". Plain .NET
// — no WPF — so the unit tests check it on any machine.

/// <summary>Reading and writing the physics tools' numbers.</summary>
public static class PhysMath
{
    /// <summary>A field's number ("20", "−9.81", "1,000", "45°", "2e3"), or null: a unit or degree sign after it is let be.</summary>
    public static double? Number(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Replace("−", "-").Replace(",", "").Replace("°", "").Trim();
        // (a unit typed after the number — "20 m/s" — is dropped: the number is what is wanted)
        var end = 0;
        while (end < t.Length && (char.IsDigit(t[end]) || t[end] is '.' or '-' or '+' || (t[end] is 'e' or 'E' && end > 0))) end++;
        t = t[..end];
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    }

    /// <summary>A number to so many significant figures (four, as a physics answer is written), its trailing zeros
    /// dropped, e-notation only when very large or small: "40.77", "0.03", "25", "1.5e+07". The minus is "−".</summary>
    public static string Text(double v, int figures = 4)
    {
        if (!double.IsFinite(v)) return "—";
        if (v == 0 || Math.Abs(v) < 1e-12) return "0";
        var a = Math.Abs(v);
        string s;
        if (a >= 1e9 || a < 1e-4) s = v.ToString("0." + new string('#', Math.Max(0, figures - 1)) + "e+0", CultureInfo.InvariantCulture);
        else
        {
            var rounded = v.ToString("G" + figures, CultureInfo.InvariantCulture);
            s = decimal.TryParse(rounded, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d.ToString("0.############################", CultureInfo.InvariantCulture) : rounded;
            if (s == "-0") s = "0";
        }
        return s.Replace("-", "−");
    }

    /// <summary>"40.77 m", "53.13°" (a degree sign closes up).</summary>
    public static string WithUnit(double v, string unit, int figures = 4) => unit == "°" ? Text(v, figures) + "°" : $"{Text(v, figures)} {unit}";

    /// <summary>For the clipboard: the typeset minus back to ASCII.</summary>
    public static string Ascii(string s) => s.Replace("−", "-");

    public const double DegToRad = Math.PI / 180;
    public const double RadToDeg = 180 / Math.PI;

    /// <summary>Metres as feet, metres per second as miles per hour: the imperial display of a result.</summary>
    public const double FeetPerMetre = 1 / 0.3048, MphPerMetrePerSecond = 1 / 0.44704;
}

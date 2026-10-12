using System.Globalization;
using System.Text.RegularExpressions;

namespace Simpl.Shell;

/// <summary>
/// A sum typed in the search box, worked out (the Mac's QuickSum, hub.js quickAnswers): "12*4", "(3+4)/2", "2^10",
/// "15% of 80". A date or a number with dashes ("10-12") is not one.
/// </summary>
internal static class PaletteSum
{
    private const string Ops = "+-*/^×÷−()";
    private const string Allowed = "0123456789.+-*/^×÷−()% ";
    private const string Dashed = "0123456789.- ";
    private static readonly Regex PercentOf = new(@"^([0-9]+(?:\.[0-9]+)?)\s*%\s*(?:of|×|\*)\s*([0-9]+(?:\.[0-9]+)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The answer, and the line under it ("12*4 =").</summary>
    public static (string Value, string Sub)? Answer(string raw)
    {
        var q = raw.Trim();
        if (q.Length == 0 || q.Length > 80) return null;
        if (Percent(q) is { } p) return p;
        if (!q.Any(char.IsAsciiDigit) || !q.Any(c => Ops.Contains(c)) || !q.All(c => Allowed.Contains(c)) || q.All(c => Dashed.Contains(c))) return null;
        var v = new Parser(q).Parse();
        if (v is not { } d || !double.IsFinite(d)) return null;
        return (Format(d), $"{q} =");
    }

    private static (string Value, string Sub)? Percent(string q)
    {
        var m = PercentOf.Match(q);
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ||
            !double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) return null;
        return (Format(a / 100 * b), $"{m.Groups[1].Value}% of {m.Groups[2].Value}");
    }

    public static string Format(double v)
    {
        if (v == Math.Round(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("G10", CultureInfo.InvariantCulture);
    }

    /// <summary>+ − × ÷ ^ ( ) and a percent sign after a number, in the usual order.</summary>
    private sealed class Parser
    {
        private readonly char[] c;
        private int i;

        public Parser(string s)
        {
            c = s.Where(ch => ch != ' ').Select(ch => ch switch { '×' => '*', '÷' => '/', '−' => '-', _ => ch }).ToArray();
        }

        public double? Parse()
        {
            var v = Expr();
            return v != null && i == c.Length ? v : null;
        }

        private bool At(char ch) => i < c.Length && c[i] == ch;

        private double? Expr()
        {
            if (Term() is not { } v) return null;
            while (At('+') || At('-'))
            {
                var op = c[i++];
                if (Term() is not { } r) return null;
                v = op == '+' ? v + r : v - r;
            }
            return v;
        }

        private double? Term()
        {
            if (Power() is not { } v) return null;
            while (At('*') || At('/'))
            {
                var op = c[i++];
                if (Power() is not { } r) return null;
                v = op == '*' ? v * r : v / r;
            }
            return v;
        }

        private double? Power()
        {
            if (Unary() is not { } b) return null;
            if (At('^'))
            {
                i++;
                if (Power() is not { } e) return null;
                return Math.Pow(b, e);
            }
            return b;
        }

        private double? Unary()
        {
            if (At('-')) { i++; return Unary() is { } v ? -v : null; }
            if (At('+')) { i++; return Unary(); }
            return Postfix();
        }

        private double? Postfix()
        {
            if (Primary() is not { } v) return null;
            while (At('%')) { i++; v /= 100; }
            return v;
        }

        private double? Primary()
        {
            if (i >= c.Length) return null;
            if (c[i] == '(')
            {
                i++;
                var v = Expr();
                if (v == null || !At(')')) return null;
                i++;
                return v;
            }
            var start = i;
            while (i < c.Length && (char.IsAsciiDigit(c[i]) || c[i] == '.')) i++;
            if (i == start) return null;
            return double.TryParse(new string(c, start, i - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? d : null;
        }
    }
}

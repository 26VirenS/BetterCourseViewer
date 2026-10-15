using System.Globalization;
using System.Text;

namespace Simpl.Tools;

// A chemical formula as a student types it, read into its atoms (the Equation Balancer's and Molar Mass &
// Stoichiometry's reader): elements with their counts, parentheses and brackets "Ca(OH)2", hydrates "CuSO4·5H2O" (or
// *5H2O), a charge "Fe^3+", "Fe3+", "SO4^2-", "e-"-less ions "H+", a phase label "(aq)" and a leading coefficient (read
// and set aside). Plain .NET, no WPF, so the unit tests check it on any machine.

/// <summary>A piece of a formula as it is typeset: a run of words, a subscript, or a superscript (the charge).</summary>
public readonly record struct ChemRun(string Text, bool Sub = false, bool Sup = false);

/// <summary>
/// A formula read: its text as written (coefficient and phase set aside), its atoms by element in order of first
/// appearance with their counts, its charge, its phase and the coefficient it was given.
/// </summary>
public sealed class ChemFormula
{
    public string Text { get; }
    public IReadOnlyList<(string Symbol, int Count)> Atoms { get; }
    public int Charge { get; }
    public string? Phase { get; }
    public int Coefficient { get; }

    private ChemFormula(string text, IReadOnlyList<(string, int)> atoms, int charge, string? phase, int coefficient)
    {
        Text = text;
        Atoms = atoms;
        Charge = charge;
        Phase = phase;
        Coefficient = coefficient;
    }

    /// <summary>How many of an element it has (0 for none).</summary>
    public int Count(string symbol) => Atoms.FirstOrDefault(a => a.Symbol == symbol).Count;

    /// <summary>Atoms in all.</summary>
    public int AtomCount => Atoms.Sum(a => a.Count);

    /// <summary>Its molar mass (g/mol) from the elements' atomic masses, charge ignored.</summary>
    public double MolarMass => Atoms.Sum(a => (ChemElements.Of(a.Symbol)?.Mass ?? 0) * a.Count);

    /// <summary>The charge as written after a formula: "", "+", "2+", "3-".</summary>
    public string ChargeText => Charge == 0 ? "" : (Math.Abs(Charge) == 1 ? "" : Math.Abs(Charge).ToString(CultureInfo.InvariantCulture)) + (Charge > 0 ? "+" : "-");

    /// <summary>The formula as runs for typesetting: counts as subscripts, the charge as a superscript.</summary>
    public IReadOnlyList<ChemRun> Runs => Typeset(Text);

    /// <summary>The formula as plain text with Unicode subscripts and superscripts ("H₂O", "SO₄²⁻"), for the clipboard.</summary>
    public string Pretty => string.Concat(Runs.Select(r => r.Sub ? Subscript(r.Text) : r.Sup ? Superscript(r.Text) : r.Text));

    public override string ToString() => Text;

    /// <summary>
    /// A formula read, or null with what could not be read in <paramref name="bad"/>: an unknown element ("Xy"), a
    /// bracket left open, nothing at all. The coefficient in front and a phase label after are read and set aside.
    /// </summary>
    public static ChemFormula? Parse(string? typed, out string? bad)
    {
        try { return ParseCore(typed, out bad); }
        catch (OverflowException) { bad = (typed ?? "").Trim(); return null; }
    }

    private static ChemFormula? ParseCore(string? typed, out string? bad)
    {
        bad = null;
        var s = (typed ?? "").Trim();
        if (s.Length == 0) { bad = ""; return null; }
        var coefficient = 1;
        var i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i > 0 && i < s.Length && s[i] != '+' && s[i] != '-')
        {
            if (!int.TryParse(s[..i], NumberStyles.None, CultureInfo.InvariantCulture, out coefficient) || coefficient < 1) { bad = s; return null; }
            s = s[i..].TrimStart();
        }
        string? phase = null;
        foreach (var p in new[] { "(s)", "(l)", "(g)", "(aq)" })
            if (s.EndsWith(p, StringComparison.OrdinalIgnoreCase)) { phase = p[1..^1].ToLowerInvariant(); s = s[..^p.Length].TrimEnd(); break; }
        if (s.Length == 0) { bad = typed!.Trim(); return null; }
        var body = s;
        var charge = 0;
        // (the charge at the end: ^3+, ^{2-}, +, -; digits before the sign are its size on a single element's ion,
        //  Fe3+ or Cu2+, and a count on a polyatomic one, MnO4- or NH4+ — written SO4^2- when the charge is more than one)
        var (cut, magnitude, sign) = ChargeAt(body);
        if (sign != 0)
        {
            // (a formula that is only a charge is not one)
            if (cut == 0 || !body[..cut].Any(char.IsLetter)) { bad = typed!.Trim(); return null; }
            charge = sign * magnitude;
            body = body[..cut];
        }
        var atoms = new List<(string Symbol, int Count)>();
        void Add(string symbol, int n)
        {
            var at = atoms.FindIndex(a => a.Symbol == symbol);
            if (at < 0) atoms.Add((symbol, n));
            else atoms[at] = (symbol, checked(atoms[at].Count + n));
        }
        // (a hydrate's parts, each with its own multiplier: CuSO4·5H2O, Na2CO3*10H2O, CaCl2.2H2O)
        var parts = SplitHydrate(body);
        foreach (var part in parts)
        {
            var text = part;
            var mult = 1;
            var m = 0;
            while (m < text.Length && char.IsDigit(text[m])) m++;
            if (m > 0)
            {
                if (m == text.Length || !int.TryParse(text[..m], NumberStyles.None, CultureInfo.InvariantCulture, out mult) || mult < 1) { bad = part; return null; }
                text = text[m..];
            }
            var pos = 0;
            var counts = Group(text, ref pos, out var wrong);
            if (counts == null || pos != text.Length) { bad = wrong ?? part; return null; }
            foreach (var (symbol, n) in counts) Add(symbol, checked(n * mult));
        }
        if (atoms.Count == 0) { bad = typed!.Trim(); return null; }
        return new ChemFormula(s, atoms, charge, phase, coefficient);
    }

    /// <summary>A formula read, or null (the reason not wanted).</summary>
    public static ChemFormula? Parse(string? typed) => Parse(typed, out _);

    /// <summary>Where a formula's charge begins, its size and its sign (0 for none): "Fe3+" is iron 3+, "MnO4-" is
    /// permanganate 1−, "SO4^2-" sulfate 2−, "Na^+" sodium 1+.</summary>
    private static (int Cut, int Magnitude, int Sign) ChargeAt(string body)
    {
        var j = body.Length;
        if (j > 0 && body[j - 1] == '}') j--;
        if (j == 0 || (body[j - 1] != '+' && body[j - 1] != '-')) return (body.Length, 0, 0);
        var sign = body[j - 1] == '+' ? 1 : -1;
        j--;
        var k = j;
        while (k > 0 && char.IsDigit(body[k - 1])) k--;
        var marked = k > 0 && body[k - 1] is '^' or '{';
        var digitsAreCharge = k < j && (marked || IsOneElement(body[..k]));
        var cut = digitsAreCharge ? k : j;
        var magnitude = digitsAreCharge ? int.Parse(body[k..j], CultureInfo.InvariantCulture) : 1;
        if (cut > 0 && body[cut - 1] == '{') cut--;
        if (cut > 0 && body[cut - 1] == '^') cut--;
        return (cut, magnitude, sign);
    }

    private static bool IsOneElement(string s) =>
        s.Length is 1 or 2 && char.IsUpper(s[0]) && (s.Length == 1 || char.IsLower(s[1])) && ChemElements.Of(s) != null;

    private static readonly char[] Dots = { '·', '•', '∙', '*', '⋅' };

    private static List<string> SplitHydrate(string body)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            // (a full stop between a formula and a number is a hydrate's dot too: CuSO4.5H2O)
            var dot = Dots.Contains(c) || (c == '.' && i + 1 < body.Length && (char.IsDigit(body[i + 1]) || char.IsUpper(body[i + 1])));
            if (dot) { parts.Add(sb.ToString()); sb.Clear(); }
            else if (!char.IsWhiteSpace(c)) sb.Append(c);
        }
        parts.Add(sb.ToString());
        return parts;
    }

    /// <summary>Elements and groups read from <paramref name="pos"/> to a closing bracket or the end, each with its count.</summary>
    private static List<(string Symbol, int Count)>? Group(string s, ref int pos, out string? wrong)
    {
        wrong = null;
        var list = new List<(string, int)>();
        void Add(List<(string Symbol, int Count)> into, string symbol, int n)
        {
            var at = into.FindIndex(a => a.Symbol == symbol);
            if (at < 0) into.Add((symbol, n));
            else into[at] = (symbol, checked(into[at].Count + n));
        }
        while (pos < s.Length)
        {
            var c = s[pos];
            if (c is ')' or ']') return list;
            if (c is '(' or '[')
            {
                var open = pos;
                pos++;
                var inner = Group(s, ref pos, out wrong);
                if (inner == null) return null;
                if (pos >= s.Length || (s[pos] != ')' && s[pos] != ']')) { wrong = s[open..]; return null; }
                pos++;
                var n = Number(s, ref pos);
                foreach (var (symbol, count) in inner) Add(list, symbol, checked(count * n));
                continue;
            }
            if (char.IsUpper(c))
            {
                var start = pos;
                pos++;
                while (pos < s.Length && char.IsLower(s[pos])) pos++;
                var symbol = s[start..pos];
                if (ChemElements.Of(symbol) == null)
                {
                    // ("Cl" read as "C" + "l"? no: an element is its whole symbol; a word that is no element is the thing to say)
                    wrong = symbol;
                    return null;
                }
                var n = Number(s, ref pos);
                Add(list, symbol, n);
                continue;
            }
            wrong = s[pos..];
            return null;
        }
        return list;
    }

    private static int Number(string s, ref int pos)
    {
        var start = pos;
        while (pos < s.Length && char.IsDigit(s[pos])) pos++;
        if (pos == start) return 1;
        return int.TryParse(s[start..pos], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : throw new OverflowException();
    }

    /// <summary>The formula's text as runs: a count after an element or a bracket is a subscript, the charge at the
    /// end a superscript, a hydrate's dot and multiplier words.</summary>
    public static IReadOnlyList<ChemRun> Typeset(string text)
    {
        var runs = new List<ChemRun>();
        var s = text;
        // (the charge first, from the end, read as Parse reads it)
        var sup = "";
        var (cut, magnitude, sign) = ChargeAt(s);
        if (sign != 0 && cut > 0)
        {
            sup = (magnitude == 1 ? "" : magnitude.ToString(CultureInfo.InvariantCulture)) + (sign < 0 ? "−" : "+");
            s = s[..cut];
        }
        var sb = new StringBuilder();
        var i = 0;
        var afterDot = true; // (the first digits are a hydrate's multiplier or a coefficient: words, not a subscript)
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsDigit(c))
            {
                var start = i;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                var digits = s[start..i];
                var subscript = !afterDot && start > 0 && (char.IsLetter(s[start - 1]) || s[start - 1] is ')' or ']');
                if (subscript)
                {
                    if (sb.Length > 0) { runs.Add(new ChemRun(sb.ToString())); sb.Clear(); }
                    runs.Add(new ChemRun(digits, Sub: true));
                }
                else sb.Append(digits);
                afterDot = false;
                continue;
            }
            if (Dots.Contains(c) || c == '.') { sb.Append('·'); afterDot = true; }
            else { sb.Append(c); afterDot = false; }
            i++;
        }
        if (sb.Length > 0) runs.Add(new ChemRun(sb.ToString()));
        if (sup.Length > 0) runs.Add(new ChemRun(sup, Sup: true));
        return runs;
    }

    private const string Subs = "₀₁₂₃₄₅₆₇₈₉";
    private const string Sups = "⁰¹²³⁴⁵⁶⁷⁸⁹";

    public static string Subscript(string digits) => string.Concat(digits.Select(c => char.IsDigit(c) ? Subs[c - '0'] : c));

    public static string Superscript(string s) => string.Concat(s.Select(c => char.IsDigit(c) ? Sups[c - '0'] : c == '+' ? '⁺' : c is '-' or '−' ? '⁻' : c));
}

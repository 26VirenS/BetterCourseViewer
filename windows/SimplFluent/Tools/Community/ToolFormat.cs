using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Simpl.Tools;

// Simpl Community tools, format 1 — this app's own copy of the format (docs/community-tools.md is the spec; the
// reference is the site's tool-format.js, and every copy must give the answers docs/community-tools-vectors.json holds:
// windows/SimplFluent.Tests/CommunityToolFormatTests.cs checks this one against them). Mirrored from the reference line
// for line, in its order, with its edge cases: a tool checked (leniently: keys format 1 does not have are passed over),
// the little language of its formulas (tokens, grammar, values), and a number as a tool shows it.
//
// A community tool is data, never code: its inputs, its results (formulas over the inputs and the results before them),
// an optional table and a note. Nothing here reads a file, the network or anything of the student's; a formula cannot
// loop, and there are no names in it but the tool's own, pi, e, true, false and the functions below. Pure: no WPF.

/// <summary>A tool refused: its code (SC-T-…), a plain message, and where in the tool ("outputs.2.value").</summary>
public sealed record ToolRefusal(string Code, string Message, string Path);

/// <summary>A tool checked: the tool (cleaned, its defaults filled in), or why not.</summary>
public sealed record ToolCheck(CommunityTool? Tool, ToolRefusal? Refusal)
{
    public bool Ok => Tool != null;
}

public enum ToolInputKind { Number, Choice, Toggle }

public sealed record ToolOption(string Label, double Value);

/// <summary>An input as checked: a number (its unit, default — clamped into the range — min, max and step), a choice
/// (its options and the default among them) or a toggle (its default, on or off).</summary>
public sealed class ToolInput
{
    public required string Id { get; init; }
    public required ToolInputKind Kind { get; init; }
    public required string Label { get; init; }
    public string Unit { get; init; } = "";
    /// <summary>A number's default, a choice's default value, a toggle's 1 or 0.</summary>
    public double Default { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double Step { get; init; } = 1;
    public IReadOnlyList<ToolOption> Options { get; init; } = Array.Empty<ToolOption>();
    public bool DefaultOn => Default != 0;
}

/// <summary>A result as checked: its formula (and the tree it parses to), its unit and decimals, and the formula that
/// hides it when it is 0 (or not a number).</summary>
public sealed class ToolOutput
{
    public string? Id { get; init; }
    public required string Label { get; init; }
    public required string Value { get; init; }
    public string Unit { get; init; } = "";
    public int Decimals { get; init; } = 4;
    public string? When { get; init; }
    internal ToolFormat.Node ValueTree { get; init; } = null!;
    internal ToolFormat.Node? WhenTree { get; init; }
}

public sealed record ToolTable(string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>A checked format 1 tool.</summary>
public sealed class CommunityTool
{
    public string? Id { get; init; }
    public double? Version { get; init; }
    public required string Name { get; init; }
    public string Maker { get; init; } = "";
    public required string Summary { get; init; }
    public required string Group { get; init; }
    public required string Symbol { get; init; }
    public required string Color { get; init; }
    public required IReadOnlyList<ToolInput> Inputs { get; init; }
    public required IReadOnlyList<ToolOutput> Outputs { get; init; }
    public ToolTable? Table { get; init; }
    public string? Note { get; init; }
    public string? Created { get; init; }
}

/// <summary>A result worked out: its label, its value and its text, its unit, and whether it is shown.</summary>
public sealed record ToolResult(string Label, double Value, string Text, string Unit, bool Shown);

public static class ToolFormat
{
    public const int Format = 1;
    public static readonly string[] Groups = { "study", "math", "science", "money", "everyday" };
    public static readonly string[] Symbols =
    {
        "bolt", "percent", "function", "sum", "divide", "ruler", "scale", "thermometer", "clock", "calendar", "money", "chart", "atom", "flask",
        "leaf", "heart", "book", "pencil", "globe", "car", "drop", "flame", "gauge", "star", "lightbulb", "music", "dice", "triangle",
    };

    /// <summary>Each function's arity: (fewest, most).</summary>
    public static readonly IReadOnlyDictionary<string, (int Fewest, int Most)> Functions = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
    {
        ["abs"] = (1, 1), ["sqrt"] = (1, 1), ["cbrt"] = (1, 1), ["exp"] = (1, 1), ["ln"] = (1, 1), ["log"] = (1, 1), ["log2"] = (1, 1),
        ["sin"] = (1, 1), ["cos"] = (1, 1), ["tan"] = (1, 1), ["asin"] = (1, 1), ["acos"] = (1, 1), ["atan"] = (1, 1), ["atan2"] = (2, 2),
        ["floor"] = (1, 1), ["ceil"] = (1, 1), ["round"] = (1, 2), ["trunc"] = (1, 1), ["sign"] = (1, 1), ["min"] = (1, 16), ["max"] = (1, 16),
        ["pow"] = (2, 2), ["hypot"] = (2, 2), ["rad"] = (1, 1), ["deg"] = (1, 1), ["clamp"] = (3, 3), ["fact"] = (1, 1),
    };

    public static readonly IReadOnlyDictionary<string, double> Constants = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["pi"] = Math.PI, ["e"] = Math.E, ["true"] = 1, ["false"] = 0,
    };

    public static class Limits
    {
        public const int Json = 32768, Name = 40, Maker = 30, Summary = 60, Label = 32, Unit = 12, Note = 400, Inputs = 16, Outputs = 12, Options = 12;
        public const int Expr = 300, Args = 16, TableCols = 6, TableRows = 100, ColName = 24, Cell = 48, TableTitle = 40, Decimals = 10;
    }

    /// <summary>10^0 … 10^10, exact: every app uses this table rather than pow(10, n).</summary>
    public static readonly double[] Pow10 = { 1, 10, 100, 1000, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10 };

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
    private static bool IsLower(char c) => c is >= 'a' and <= 'z';

    /// <summary>^[a-z][a-z0-9_]{0,15}$</summary>
    private static bool IsId(string s)
    {
        if (s.Length < 1 || s.Length > 16 || !IsLower(s[0])) return false;
        for (var i = 1; i < s.Length; i++) if (!(IsLower(s[i]) || IsDigit(s[i]) || s[i] == '_')) return false;
        return true;
    }

    /// <summary>^#[0-9a-f]{6}$</summary>
    private static bool IsColour(string s)
    {
        if (s.Length != 7 || s[0] != '#') return false;
        for (var i = 1; i < 7; i++) if (!(IsDigit(s[i]) || s[i] is >= 'a' and <= 'f')) return false;
        return true;
    }

    // ---- the language: tokens -------------------------------------------------------------------------------------

    internal enum Tok { Num, Name, Op, End }

    internal readonly record struct Token(Tok T, double Num, string Text);

    private static readonly string[] Ops = { "<=", ">=", "==", "!=", "&&", "||", "+", "-", "*", "/", "%", "^", "(", ")", ",", "?", ":", "<", ">", "!" };

    /// <summary>A formula that cannot be read: its message.</summary>
    public sealed class SyntaxError : Exception
    {
        public SyntaxError(string message) : base(message) { }
    }

    /// <summary>NUM: ^(?:\d+(?:\.\d+)?|\.\d+)(?:[eE][+-]?\d+)? — its length at <paramref name="i"/>, 0 for none.</summary>
    private static int NumberAt(string s, int i)
    {
        var n = s.Length;
        var j = i;
        if (IsDigit(s[j]))
        {
            while (j < n && IsDigit(s[j])) j++;
            if (j + 1 < n && s[j] == '.' && IsDigit(s[j + 1]))
            {
                j++;
                while (j < n && IsDigit(s[j])) j++;
            }
        }
        else if (s[j] == '.' && j + 1 < n && IsDigit(s[j + 1]))
        {
            j++;
            while (j < n && IsDigit(s[j])) j++;
        }
        else return 0;
        if (j < n && s[j] is 'e' or 'E')
        {
            var k = j + 1;
            if (k < n && s[k] is '+' or '-') k++;
            if (k < n && IsDigit(s[k]))
            {
                while (k < n && IsDigit(s[k])) k++;
                j = k;
            }
        }
        return j - i;
    }

    internal static List<Token> Tokenize(string src)
    {
        var o = new List<Token>();
        var i = 0;
        while (i < src.Length)
        {
            var c = src[i];
            if (c is ' ' or '\t') { i++; continue; }
            var len = NumberAt(src, i);
            if (len > 0)
            {
                var text = src.Substring(i, len);
                o.Add(new Token(Tok.Num, double.Parse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture), text));
                i += len;
                continue;
            }
            if (IsLower(c))
            {
                var j = i + 1;
                while (j < src.Length && (IsLower(src[j]) || IsDigit(src[j]) || src[j] == '_')) j++;
                o.Add(new Token(Tok.Name, 0, src[i..j]));
                i = j;
                continue;
            }
            var op = Ops.FirstOrDefault(x => src.AsSpan(i).StartsWith(x, StringComparison.Ordinal));
            if (op != null) { o.Add(new Token(Tok.Op, 0, op)); i += op.Length; continue; }
            throw new SyntaxError($"“{c}” can’t be used in a formula");
        }
        o.Add(new Token(Tok.End, 0, ""));
        return o;
    }

    // ---- the language: grammar (lowest to highest) ------------------------------------------------------------------
    //   cond  := or ('?' cond ':' cond)?
    //   or    := and ('||' and)*            and := eq ('&&' eq)*
    //   eq    := rel (('=='|'!=') rel)*     rel := add (('<'|'<='|'>'|'>=') add)*
    //   add   := mul (('+'|'-') mul)*       mul := unary (('*'|'/'|'%') unary)*
    //   unary := ('-'|'+'|'!') unary | pow  pow := primary ('^' unary)?
    //   primary := number | name | name '(' cond (',' cond)* ')' | '(' cond ')'

    internal abstract class Node { }
    internal sealed class NumNode(double v) : Node { public readonly double V = v; }
    internal sealed class NameNode(string n) : Node { public readonly string N = n; }
    internal sealed class UnNode(string op, Node a) : Node { public readonly string Op = op; public readonly Node A = a; }
    internal sealed class BinNode(string op, Node a, Node b) : Node { public readonly string Op = op; public readonly Node A = a, B = b; }
    internal sealed class CondNode(Node c, Node a, Node b) : Node { public readonly Node C = c, A = a, B = b; }
    internal sealed class CallNode(string f, List<Node> args) : Node { public readonly string F = f; public readonly List<Node> Args = args; }

    /// <summary>A token as the reference's messages say it: an operator as it is, a number as JavaScript writes it.</summary>
    private static string Said(Token t) => t.T == Tok.Num ? JsNumber(t.Num) : t.Text;

    internal static Node Parse(string? src)
    {
        if (src == null || JsTrim(src).Length == 0) throw new SyntaxError("The formula is empty");
        if (src.Length > Limits.Expr) throw new SyntaxError($"A formula can be {Limits.Expr} characters at most");
        var toks = Tokenize(src);
        var p = 0;
        Token Peek() => toks[p];
        bool IsOp(params string[] ops) => Peek().T == Tok.Op && Array.IndexOf(ops, Peek().Text) >= 0;
        Token Take() => toks[p++];
        void Expect(string op)
        {
            if (!IsOp(op)) throw new SyntaxError($"Expected “{op}”{(Peek().T == Tok.End ? " at the end" : "")}");
            Take();
        }
        Node Binary(Func<Node> next, string[] ops)
        {
            var a = next();
            while (IsOp(ops)) { var op = Take().Text; a = new BinNode(op, a, next()); }
            return a;
        }
        Node Primary()
        {
            var tk = Peek();
            if (tk.T == Tok.Num) { Take(); return new NumNode(tk.Num); }
            if (tk.T == Tok.Name)
            {
                Take();
                if (IsOp("("))
                {
                    Take();
                    var args = new List<Node>();
                    if (!IsOp(")")) { args.Add(Cond()); while (IsOp(",")) { Take(); args.Add(Cond()); } }
                    Expect(")");
                    return new CallNode(tk.Text, args);
                }
                return new NameNode(tk.Text);
            }
            if (IsOp("(")) { Take(); var e = Cond(); Expect(")"); return e; }
            throw new SyntaxError(tk.T == Tok.End ? "The formula stops too soon" : $"“{Said(tk)}” is out of place");
        }
        Node Pow() { var a = Primary(); if (IsOp("^")) { Take(); return new BinNode("^", a, Unary()); } return a; }
        Node Unary() { if (IsOp("-", "+", "!")) { var op = Take().Text; return new UnNode(op, Unary()); } return Pow(); }
        Node Mul() => Binary(Unary, new[] { "*", "/", "%" });
        Node Add() => Binary(Mul, new[] { "+", "-" });
        Node Rel() => Binary(Add, new[] { "<", "<=", ">", ">=" });
        Node Eq() => Binary(Rel, new[] { "==", "!=" });
        Node And() => Binary(Eq, new[] { "&&" });
        Node Or() => Binary(And, new[] { "||" });
        Node Cond()
        {
            var c = Or();
            if (IsOp("?")) { Take(); var a = Cond(); Expect(":"); var b = Cond(); return new CondNode(c, a, b); }
            return c;
        }
        var tree = Cond();
        if (Peek().T != Tok.End) throw new SyntaxError($"“{Said(Peek())}” is out of place");
        return tree;
    }

    /// <summary>Every name a formula reads, in the order it reads them, and a check of every function it calls (one of
    /// <see cref="Functions"/>' own, given as many values as it takes).</summary>
    private static void NamesIn(Node node, List<string> o, HashSet<string> seen)
    {
        switch (node)
        {
            case NameNode n: if (seen.Add(n.N)) o.Add(n.N); break;
            case UnNode u: NamesIn(u.A, o, seen); break;
            case BinNode b: NamesIn(b.A, o, seen); NamesIn(b.B, o, seen); break;
            case CondNode c: NamesIn(c.C, o, seen); NamesIn(c.A, o, seen); NamesIn(c.B, o, seen); break;
            case CallNode call:
                if (!Functions.TryGetValue(call.F, out var ar)) throw new SyntaxError($"There is no function “{call.F}”");
                if (call.Args.Count < ar.Fewest || call.Args.Count > ar.Most)
                    throw new SyntaxError($"{call.F}() takes {(ar.Fewest == ar.Most ? $"{ar.Fewest}" : $"{ar.Fewest} to {ar.Most}")} value{(ar.Most == 1 ? "" : "s")}");
                foreach (var a in call.Args) NamesIn(a, o, seen);
                break;
        }
    }

    /// <summary>Why a formula cannot be used, or null: it does not parse (SC-T-FORMULA), or it reads a name that is not
    /// among <paramref name="names"/> nor pi, e, true, false (SC-T-NAME-USE; one among <paramref name="later"/> is
    /// "used before it is worked out").</summary>
    public static (string Code, string Message)? FormulaProblem(string? src, IEnumerable<string> names, IEnumerable<string>? later = null)
    {
        try
        {
            var tree = Parse(src);
            var known = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (var c in Constants.Keys) known.Add(c);
            var soon = new HashSet<string>(later ?? Array.Empty<string>(), StringComparer.Ordinal);
            var read = new List<string>();
            NamesIn(tree, read, new HashSet<string>(StringComparer.Ordinal));
            foreach (var n in read)
                if (!known.Contains(n)) return ("SC-T-NAME-USE", soon.Contains(n) ? $"“{n}” is used before it is worked out." : $"There is nothing called “{n}”.");
            return null;
        }
        catch (SyntaxError e) { return ("SC-T-FORMULA", $"{e.Message}."); }
    }

    // ---- the language: values ---------------------------------------------------------------------------------------

    /// <summary>True is anything but 0 and NaN.</summary>
    public static bool Truthy(double x) => x != 0 && !double.IsNaN(x);

    private static bool AnyNaN(IEnumerable<double> xs) => xs.Any(double.IsNaN);

    /// <summary>x^y as every app works it out (JavaScript's Math.pow, made explicit where C's pow differs).</summary>
    public static double Power(double x, double y)
    {
        if (double.IsNaN(y)) return double.NaN;
        if (y == 0) return 1;
        if (double.IsNaN(x)) return double.NaN;
        if (Math.Abs(x) == 1 && !double.IsFinite(y)) return double.NaN;
        return Math.Pow(x, y);
    }

    /// <summary>x rounded to n decimals, halves away from zero: sign(x) · floor(|x| · 10ⁿ + 0.5) / 10ⁿ.</summary>
    public static double RoundTo(double x, double n = 0)
    {
        if (!double.IsFinite(x) || double.IsNaN(n)) return double.IsNaN(n) ? double.NaN : x;
        var d = (int)Math.Min(Limits.Decimals, Math.Max(0, Math.Truncate(n)));
        var f = Pow10[d];
        var a = Math.Abs(x) * f;
        if (a >= 4503599627370496) return x; // (2^52: already whole at this many decimals)
        var r = Math.Floor(a + 0.5) / f;
        return x < 0 ? -r : r;
    }

    private static double Factorial(double n)
    {
        if (!double.IsFinite(n) || n < 0 || n > 170 || Math.Truncate(n) != n) return double.NaN;
        double r = 1;
        for (var i = 2; i <= n; i++) r *= i;
        return r;
    }

    /// <summary>JavaScript's Math.hypot (V8's): infinite if either is, NaN if either is NaN, else scaled by the larger and
    /// summed with Kahan's compensation.</summary>
    private static double Hypot(double x, double y)
    {
        var values = new[] { Math.Abs(x), Math.Abs(y) };
        double max = 0;
        var nan = false;
        foreach (var v in values)
        {
            if (double.IsNaN(v)) nan = true;
            else if (v > max) max = v;
        }
        if (double.IsPositiveInfinity(max)) return double.PositiveInfinity;
        if (nan) return double.NaN;
        if (max == 0) return 0;
        double sum = 0, compensation = 0;
        foreach (var v in values)
        {
            var n = v / max;
            var summand = n * n - compensation;
            var preliminary = sum + summand;
            compensation = preliminary - sum - summand;
            sum = preliminary;
        }
        return Math.Sqrt(sum) * max;
    }

    private static double MinOf(IReadOnlyList<double> xs)
    {
        if (AnyNaN(xs)) return double.NaN;
        var a = xs[0];
        for (var i = 1; i < xs.Count; i++) if (xs[i] < a) a = xs[i];
        return a;
    }

    private static double MaxOf(IReadOnlyList<double> xs)
    {
        if (AnyNaN(xs)) return double.NaN;
        var a = xs[0];
        for (var i = 1; i < xs.Count; i++) if (xs[i] > a) a = xs[i];
        return a;
    }

    /// <summary>A function by its name (one of <see cref="Functions"/>' own; any other is NaN, never found elsewhere).</summary>
    private static double Call(string f, IReadOnlyList<double> a) => f switch
    {
        "abs" => Math.Abs(a[0]), "sqrt" => Math.Sqrt(a[0]), "cbrt" => Math.Cbrt(a[0]), "exp" => Math.Exp(a[0]),
        "ln" => Math.Log(a[0]), "log" => Math.Log10(a[0]), "log2" => Math.Log2(a[0]),
        "sin" => Math.Sin(a[0]), "cos" => Math.Cos(a[0]), "tan" => Math.Tan(a[0]),
        "asin" => Math.Asin(a[0]), "acos" => Math.Acos(a[0]), "atan" => Math.Atan(a[0]), "atan2" => Math.Atan2(a[0], a[1]),
        "floor" => Math.Floor(a[0]), "ceil" => Math.Ceiling(a[0]), "trunc" => Math.Truncate(a[0]),
        "round" => RoundTo(a[0], a.Count > 1 ? a[1] : 0),
        // (0, −0 and NaN as they are: never Math.Sign, which throws on NaN)
        "sign" => a[0] > 0 ? 1 : a[0] < 0 ? -1 : a[0],
        "min" => MinOf(a), "max" => MaxOf(a),
        "pow" => Power(a[0], a[1]), "hypot" => Hypot(a[0], a[1]),
        "rad" => a[0] * Math.PI / 180, "deg" => a[0] * 180 / Math.PI,
        "clamp" => AnyNaN(a) ? double.NaN : Math.Min(Math.Max(a[0], a[1]), a[2]),
        "fact" => Factorial(a[0]),
        _ => double.NaN,
    };

    internal static double Evaluate(Node node, IReadOnlyDictionary<string, double> env)
    {
        switch (node)
        {
            case NumNode n: return n.V;
            case NameNode n: return env.TryGetValue(n.N, out var v) ? v : double.NaN;
            case UnNode u:
            {
                var a = Evaluate(u.A, env);
                return u.Op == "-" ? -a : u.Op == "+" ? a : Truthy(a) ? 0 : 1;
            }
            case CondNode c: return Truthy(Evaluate(c.C, env)) ? Evaluate(c.A, env) : Evaluate(c.B, env);
            case CallNode call:
                if (!Functions.ContainsKey(call.F)) return double.NaN;
                return Call(call.F, call.Args.Select(x => Evaluate(x, env)).ToList());
            case BinNode b:
            {
                var a = Evaluate(b.A, env);
                var y = Evaluate(b.B, env);
                return b.Op switch
                {
                    "+" => a + y,
                    "-" => a - y,
                    "*" => a * y,
                    "/" => a / y,
                    "%" => a % y, // (the sign of the dividend, as C's fmod)
                    "^" => Power(a, y),
                    "<" => a < y ? 1 : 0,
                    "<=" => a <= y ? 1 : 0,
                    ">" => a > y ? 1 : 0,
                    ">=" => a >= y ? 1 : 0,
                    "==" => a == y ? 1 : 0,
                    "!=" => a != y ? 1 : 0,
                    "&&" => Truthy(a) && Truthy(y) ? 1 : 0,
                    "||" => Truthy(a) || Truthy(y) ? 1 : 0,
                    _ => double.NaN,
                };
            }
            default: return double.NaN;
        }
    }

    /// <summary>A formula worked out with these names (pi, e, true and false besides): NaN when it cannot be read.</summary>
    public static double Evaluate(string formula, IReadOnlyDictionary<string, double>? names = null)
    {
        var env = new Dictionary<string, double>(Constants, StringComparer.Ordinal);
        if (names != null) foreach (var (k, v) in names) env[k] = v;
        try { return Evaluate(Parse(formula), env); }
        catch (SyntaxError) { return double.NaN; }
    }

    // ---- numbers as a tool shows them -------------------------------------------------------------------------------

    private const string Superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹";

    private static string TrimZeros(string s) => s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;

    /// <summary>The whole part grouped by threes with commas once it has five digits or more.</summary>
    private static string Grouped(string whole)
    {
        if (whole.Length < 5) return whole;
        var sb = new StringBuilder();
        for (var i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0) sb.Append(',');
            sb.Append(whole[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// JavaScript's toFixed for a finite number below 1e21: the integer nearest v · 10^d worked out from v's exact value,
    /// a tie going to the larger (away from zero, the sign put back after) — where .NET's "F" sends a tie to the even
    /// digit (2^52 and up, a result is shown as it is, and its exact value can end in a 5 just past the decimals).
    /// </summary>
    private static string Fixed(double v, int d)
    {
        var negative = v < 0;
        var bits = BitConverter.DoubleToInt64Bits(Math.Abs(v));
        var exponent = (int)((bits >> 52) & 0x7ff);
        var mantissa = bits & 0xfffffffffffffL;
        if (exponent == 0) exponent = 1; else mantissa |= 1L << 52;
        var e = exponent - 1075;
        var scaled = new System.Numerics.BigInteger(mantissa) * System.Numerics.BigInteger.Pow(10, d);
        System.Numerics.BigInteger n;
        if (e >= 0) n = scaled << e;
        else
        {
            var den = System.Numerics.BigInteger.One << -e;
            n = (2 * scaled + den) / (2 * den);
        }
        var digits = n.ToString(CultureInfo.InvariantCulture).PadLeft(d + 1, '0');
        var s = d == 0 ? digits : digits[..^d] + "." + digits[^d..];
        return negative ? "-" + s : s;
    }

    /// <summary>
    /// A result as text: "—" when it is not a number; from 1e12 up, or under 1e-4 and rounding to 0 at
    /// <paramref name="decimals"/>, as "m × 10ⁿ" (m to 3 decimals); otherwise rounded to <paramref name="decimals"/>
    /// (default 4) halves away from zero, trailing zeros dropped, the whole part grouped by threes with commas from 10,000.
    /// </summary>
    public static string FormatNumber(double v, int decimals = 4)
    {
        if (!double.IsFinite(v)) return "—";
        var d = Math.Min(Limits.Decimals, Math.Max(0, decimals));
        if (v == 0) return "0";
        var a = Math.Abs(v);
        var r = RoundTo(v, d);
        if (a >= 1e12 || (r == 0 && a < 1e-4))
        {
            var ex = (int)Math.Floor(Math.Log10(a));
            var m = v / Math.Pow(10, ex);
            if (Math.Abs(m) >= 10) { m /= 10; ex += 1; }
            if (Math.Abs(m) < 1) { m *= 10; ex -= 1; }
            m = RoundTo(m, 3);
            if (Math.Abs(m) >= 10) { m = RoundTo(m / 10, 3); ex += 1; }
            var power = new StringBuilder();
            foreach (var c in ex.ToString(CultureInfo.InvariantCulture)) power.Append(c == '-' ? '⁻' : Superscripts[c - '0']);
            return $"{TrimZeros(Fixed(m, 3))} × 10{power}";
        }
        if (r == 0) return "0";
        var s = TrimZeros(Fixed(Math.Abs(r), d));
        var dot = s.IndexOf('.');
        var whole = dot < 0 ? s : s[..dot];
        var frac = dot < 0 ? "" : s[(dot + 1)..];
        return $"{(r < 0 ? "-" : "")}{Grouped(whole)}{(frac.Length > 0 ? "." + frac : "")}";
    }

    /// <summary>A number as JavaScript's String(n) writes it (a table's number cell, a message's token).</summary>
    public static string JsNumber(double x)
    {
        if (double.IsNaN(x)) return "NaN";
        if (x == 0) return "0";
        if (double.IsInfinity(x)) return x > 0 ? "Infinity" : "-Infinity";
        if (x < 0) return "-" + JsNumber(-x);
        // (the shortest digits that read back as x, and where the point goes: x = 0.digits × 10^n)
        var r = x.ToString("R", CultureInfo.InvariantCulture);
        var exp = 0;
        var e = r.IndexOfAny(new[] { 'E', 'e' });
        if (e >= 0) { exp = int.Parse(r[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); r = r[..e]; }
        var dot = r.IndexOf('.');
        var whole = dot < 0 ? r : r[..dot];
        var digits = whole + (dot < 0 ? "" : r[(dot + 1)..]);
        var n = whole.Length + exp;
        var lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
        digits = digits[lead..];
        n -= lead;
        digits = digits.TrimEnd('0');
        if (digits.Length == 0) return "0";
        var k = digits.Length;
        if (k <= n && n <= 21) return digits + new string('0', n - k);
        if (0 < n && n <= 21) return digits[..n] + "." + digits[n..];
        if (-6 < n && n <= 0) return "0." + new string('0', -n) + digits;
        var e1 = n - 1;
        var sign = e1 < 0 ? "-" : "+";
        return (k == 1 ? digits : digits[0] + "." + digits[1..]) + "e" + sign + Math.Abs(e1).ToString(CultureInfo.InvariantCulture);
    }

    // ---- text as a tool keeps it ------------------------------------------------------------------------------------

    /// <summary>JavaScript's white space (\s, and what trim takes away): the line ends, the Unicode spaces, the BOM.</summary>
    private static bool IsJsSpace(char c) => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or (char)0x00a0 or (char)0x1680 or (char)0x2028
        or (char)0x2029 or (char)0x202f or (char)0x205f or (char)0x3000 or (char)0xfeff || c is >= (char)0x2000 and <= (char)0x200a;

    private static string JsTrim(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && IsJsSpace(s[a])) a++;
        while (b > a && IsJsSpace(s[b - 1])) b--;
        return s[a..b];
    }

    /// <summary>Text kept well-formed: a lone half of a surrogate pair (JSON's \u escapes can carry one in) is U+FFFD.</summary>
    private static string WellFormed(string s)
    {
        if (!s.Any(char.IsSurrogate)) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); continue; }
            sb.Append(char.IsSurrogate(c) ? (char)0xfffd : c);
        }
        return sb.ToString();
    }

    /// <summary>Text as a tool keeps it: well-formed, control characters as spaces, every run of white space one space,
    /// trimmed.</summary>
    public static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = WellFormed(s);
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var c in s)
        {
            if (c <= '\u001f' || c == '\u007f' || IsJsSpace(c)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Text cut at its limit, counted as JavaScript counts a string — never between a pair's two halves: a
    /// character that would straddle the limit is left out.</summary>
    private static string Cut(string s, int most) => s.Length <= most ? s : char.IsHighSurrogate(s[most - 1]) ? s[..(most - 1)] : s[..most];

    // ---- a tool: checked, then worked out ---------------------------------------------------------------------------

    private static readonly HashSet<string> Reserved = new(Constants.Keys.Concat(Functions.Keys), StringComparer.Ordinal);

    private static ToolCheck Fail(string code, string message, string path) => new(null, new ToolRefusal(code, message, path));

    /// <summary>A property of an object (an array, a string or a number has none of a tool's).</summary>
    private static JsonElement? Prop(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    /// <summary>JavaScript's `v != null`: there, and not null.</summary>
    private static bool Given(JsonElement? e) => e is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };

    /// <summary>A JSON number as JavaScript reads it (one too large for a double is infinite).</summary>
    private static double? Number(JsonElement? e)
    {
        if (e is not { ValueKind: JsonValueKind.Number } n) return null;
        if (n.TryGetDouble(out var d)) return d;
        return double.TryParse(n.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

    /// <summary>isNum: a number, and finite.</summary>
    private static double? Finite(JsonElement? e) => Number(e) is { } d && double.IsFinite(d) ? d : null;

    /// <summary>A JSON string as JavaScript reads it: one holding a lone half of a surrogate pair, which .NET's reader
    /// will not give as a string, read from its own text (its half made U+FFFD by <see cref="Clean"/>).</summary>
    private static string? Str(JsonElement? e)
    {
        if (e is not { ValueKind: JsonValueKind.String } s) return null;
        try { return s.GetString(); }
        catch (InvalidOperationException) { return Unescaped(s.GetRawText()); }
    }

    /// <summary>A JSON string literal's text, its escapes undone (a \u escape as the UTF-16 unit it names, whatever it is).</summary>
    private static string Unescaped(string literal)
    {
        var sb = new StringBuilder(literal.Length);
        for (var i = 1; i < literal.Length - 1; i++)
        {
            var c = literal[i];
            if (c != '\\' || i + 1 >= literal.Length - 1) { sb.Append(c); continue; }
            var x = literal[++i];
            switch (x)
            {
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u' when i + 4 < literal.Length && int.TryParse(literal.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code):
                    sb.Append((char)code);
                    i += 4;
                    break;
                default: sb.Append(x); break;
            }
        }
        return sb.ToString();
    }

    private static string CleanOf(JsonElement? e) => Clean(Str(e));

    /// <summary>How long JSON.stringify would write this (the 32 KB a tool may be).</summary>
    internal static long JsonLength(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
            {
                long n = 2, count = 0;
                foreach (var p in e.EnumerateObject()) { n += StringLength(p.Name) + 1 + JsonLength(p.Value); count++; }
                return n + Math.Max(0, count - 1);
            }
            case JsonValueKind.Array:
            {
                long n = 2, count = 0;
                foreach (var x in e.EnumerateArray()) { n += JsonLength(x); count++; }
                return n + Math.Max(0, count - 1);
            }
            case JsonValueKind.String: return StringLength(Str(e) ?? "");
            case JsonValueKind.Number: return Number(e) is { } d && double.IsFinite(d) ? JsNumber(d).Length : 4;
            case JsonValueKind.True: return 4;
            case JsonValueKind.False: return 5;
            default: return 4;
        }
    }

    private static long StringLength(string s)
    {
        long n = 2;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c is '"' or '\\' or '\b' or '\f' or '\n' or '\r' or '\t') n += 2;
            else if (c < ' ') n += 6;
            else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { n += 2; i++; }
            else if (char.IsSurrogate(c)) n += 6;
            else n += 1;
        }
        return n;
    }

    /// <summary>A tool sent as text, checked against format 1 (the reference's validateTool given a string).</summary>
    public static ToolCheck Validate(string raw)
    {
        if (raw.Length > Limits.Json) return Fail("SC-T-BIG", "That tool is too big.", "");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (Exception) { return Fail("SC-T-JSON", "That tool could not be read.", ""); }
        using (doc) return CheckRead(doc.RootElement);
    }

    /// <summary>
    /// A tool checked against format 1, leniently (as an app reads one: keys format 1 does not have are passed over, so a
    /// later format's extra keys do not stop this one): the tool, cleaned — text trimmed, defaults filled in — or why
    /// not, with a code (SC-T-…). One that needs a later format is refused as "This tool needs a newer Simpl."
    /// </summary>
    public static ToolCheck Validate(JsonElement raw)
    {
        try { if (JsonLength(raw) > Limits.Json) return Fail("SC-T-BIG", "That tool is too big.", ""); }
        catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException) { return Fail("SC-T-JSON", "That tool could not be read.", ""); }
        return CheckRead(raw);
    }

    /// <summary>The check, a string JSON holds that .NET cannot read (half a surrogate pair) refused as unreadable.</summary>
    private static ToolCheck CheckRead(JsonElement raw)
    {
        try { return Check(raw); }
        catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException)
        {
            return Fail("SC-T-JSON", "That tool could not be read.", "");
        }
    }

    private static ToolCheck Check(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object) return Fail("SC-T-JSON", "That tool could not be read.", "");
        var format = Prop(t, "format");
        if (Number(format) is not 1.0)
            return Fail("SC-T-FORMAT", Later(format) ? "This tool needs a newer Simpl." : "That is not a Simpl tool.", "format");

        var name = Cut(CleanOf(Prop(t, "name")), Limits.Name);
        if (name.Length == 0) return Fail("SC-T-NAME", "Give the tool a name.", "name");
        var summary = Cut(CleanOf(Prop(t, "summary")), Limits.Summary);
        if (summary.Length == 0) return Fail("SC-T-SUMMARY", "Say in a line what the tool does.", "summary");
        var group = Str(Prop(t, "group")) is { } g && Groups.Contains(g) ? g : null;
        if (group == null) return Fail("SC-T-GROUP", $"The group must be one of: {string.Join(", ", Groups)}.", "group");
        var symbol = Str(Prop(t, "symbol")) is { } sy && Symbols.Contains(sy) ? sy : null;
        if (symbol == null) return Fail("SC-T-SYMBOL", "That symbol is not one of Simpl’s.", "symbol");
        var color = Str(Prop(t, "color"))?.ToLowerInvariant() ?? "";
        if (!IsColour(color)) return Fail("SC-T-COLOUR", "The colour must be like #3478f6.", "color");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        ToolCheck? TakeId(JsonElement? id, string path, bool optional, out string? ok)
        {
            ok = null;
            if (!Given(id) && optional) return null;
            if (Str(id) is not { } s || !IsId(s)) return Fail("SC-T-ID", "A name in formulas is a lower-case letter, then letters, digits or _ (16 at most).", path);
            if (Reserved.Contains(s)) return Fail("SC-T-ID", $"“{s}” is a name Simpl keeps for itself.", path);
            if (!seen.Add(s)) return Fail("SC-T-ID", $"“{s}” is used twice.", path);
            ok = s;
            return null;
        }

        var rawInputs = Prop(t, "inputs");
        if (rawInputs is not { ValueKind: JsonValueKind.Array } ins || ins.GetArrayLength() < 1 || ins.GetArrayLength() > Limits.Inputs)
            return Fail("SC-T-INPUTS", $"A tool has 1 to {Limits.Inputs} inputs.", "inputs");
        var inputs = new List<ToolInput>();
        var i = 0;
        foreach (var raw in ins.EnumerateArray())
        {
            var path = $"inputs.{i++}";
            if (raw.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return Fail("SC-T-INPUT", "That input could not be read.", path);
            if (TakeId(Prop(raw, "id"), $"{path}.id", false, out var id) is { } idErr) return idErr;
            var label = Cut(CleanOf(Prop(raw, "label")), Limits.Label);
            if (label.Length == 0) return Fail("SC-T-LABEL", "Every input needs a label.", $"{path}.label");
            var kind = Str(Prop(raw, "kind"));
            if (kind == "number")
            {
                foreach (var k in new[] { "default", "min", "max", "step" })
                    if (Given(Prop(raw, k)) && Finite(Prop(raw, k)) == null) return Fail("SC-T-NUMBER", $"The input’s {k} must be a number.", $"{path}.{k}");
                var min = Finite(Prop(raw, "min"));
                var max = Finite(Prop(raw, "max"));
                var step = Finite(Prop(raw, "step"));
                if (min is { } a && max is { } b && a > b) return Fail("SC-T-RANGE", "The input’s min is above its max.", path);
                if (step is { } st && st <= 0) return Fail("SC-T-NUMBER", "The input’s step must be above 0.", $"{path}.step");
                var lo = min ?? double.NegativeInfinity;
                var hi = max ?? double.PositiveInfinity;
                var def = Math.Min(hi, Math.Max(lo, Finite(Prop(raw, "default")) ?? 0));
                inputs.Add(new ToolInput { Id = id!, Kind = ToolInputKind.Number, Label = label, Unit = Cut(CleanOf(Prop(raw, "unit")), Limits.Unit), Default = def, Min = min, Max = max, Step = step ?? 1 });
            }
            else if (kind == "choice")
            {
                var rawOptions = Prop(raw, "options");
                if (rawOptions is not { ValueKind: JsonValueKind.Array } opts || opts.GetArrayLength() < 2 || opts.GetArrayLength() > Limits.Options)
                    return Fail("SC-T-OPTIONS", $"A choice has 2 to {Limits.Options} options.", $"{path}.options");
                var options = new List<ToolOption>();
                var j = 0;
                foreach (var o in opts.EnumerateArray())
                {
                    var ol = Cut(CleanOf(Prop(o, "label")), Limits.Label);
                    var ov = Finite(Prop(o, "value"));
                    if (ol.Length == 0 || ov == null) return Fail("SC-T-OPTIONS", "Every option needs a label and a number for its value.", $"{path}.options.{j}");
                    if (options.Any(x => x.Value == ov)) return Fail("SC-T-OPTIONS", "Two options have the same value.", $"{path}.options.{j}");
                    options.Add(new ToolOption(ol, ov.Value));
                    j++;
                }
                var given = Number(Prop(raw, "default"));
                var def = given is { } gd && options.Any(o => o.Value == gd) ? gd : options[0].Value;
                inputs.Add(new ToolInput { Id = id!, Kind = ToolInputKind.Choice, Label = label, Options = options, Default = def });
            }
            else if (kind == "toggle")
            {
                inputs.Add(new ToolInput { Id = id!, Kind = ToolInputKind.Toggle, Label = label, Default = Prop(raw, "default") is { ValueKind: JsonValueKind.True } ? 1 : 0 });
            }
            else return Fail("SC-T-KIND", "An input is a number, a choice or a toggle.", $"{path}.kind");
        }

        var rawOutputs = Prop(t, "outputs");
        if (rawOutputs is not { ValueKind: JsonValueKind.Array } outs || outs.GetArrayLength() < 1 || outs.GetArrayLength() > Limits.Outputs)
            return Fail("SC-T-OUTPUTS", $"A tool has 1 to {Limits.Outputs} results.", "outputs");
        var known = new HashSet<string>(inputs.Select(x => x.Id), StringComparer.Ordinal);
        foreach (var c in Constants.Keys) known.Add(c);
        var laterIds = outs.EnumerateArray().Select(o => Str(Prop(o, "id"))).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var outputs = new List<ToolOutput>();
        i = 0;
        foreach (var raw in outs.EnumerateArray())
        {
            var path = $"outputs.{i++}";
            if (raw.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return Fail("SC-T-OUTPUT", "That result could not be read.", path);
            var label = Cut(CleanOf(Prop(raw, "label")), Limits.Label);
            if (label.Length == 0) return Fail("SC-T-LABEL", "Every result needs a label.", $"{path}.label");
            var decimals = 4;
            if (Given(Prop(raw, "decimals")))
            {
                if (Finite(Prop(raw, "decimals")) is not { } dd || Math.Truncate(dd) != dd || dd < 0 || dd > Limits.Decimals)
                    return Fail("SC-T-DECIMALS", $"Decimals are a whole number from 0 to {Limits.Decimals}.", $"{path}.decimals");
                decimals = (int)dd;
            }
            // the value reads the inputs and the results before this one; `when` may read this one's value too
            ToolCheck? Formula(string k, out string? ok)
            {
                ok = null;
                var src = Str(Prop(raw, k)) ?? "";
                if (FormulaProblem(src, known, laterIds) is { } p) return Fail(p.Code, p.Message, $"{path}.{k}");
                ok = JsTrim(src);
                return null;
            }
            if (Formula("value", out var value) is { } valueErr) return valueErr;
            if (TakeId(Prop(raw, "id"), $"{path}.id", true, out var oid) is { } oidErr) return oidErr;
            if (oid != null) known.Add(oid);
            string? when = null;
            if (Given(Prop(raw, "when")) && Str(Prop(raw, "when")) != "" && Formula("when", out when) is { } whenErr) return whenErr;
            outputs.Add(new ToolOutput
            {
                Id = oid, Label = label, Value = value!, Unit = Cut(CleanOf(Prop(raw, "unit")), Limits.Unit), Decimals = decimals,
                When = string.IsNullOrEmpty(when) ? null : when, ValueTree = Parse(value), WhenTree = string.IsNullOrEmpty(when) ? null : Parse(when),
            });
        }

        ToolTable? table = null;
        if (Given(Prop(t, "table")))
        {
            var tb = Prop(t, "table")!.Value;
            if (tb.ValueKind != JsonValueKind.Object || Prop(tb, "columns") is not { ValueKind: JsonValueKind.Array } cols || Prop(tb, "rows") is not { ValueKind: JsonValueKind.Array } rws)
                return Fail("SC-T-TABLE", "That table could not be read.", "table");
            var columns = cols.EnumerateArray().Select(c => Cut(CleanOf(c), Limits.ColName)).ToList();
            if (columns.Count < 1 || columns.Count > Limits.TableCols || columns.Any(c => c.Length == 0)) return Fail("SC-T-TABLE", $"A table has 1 to {Limits.TableCols} named columns.", "table.columns");
            if (rws.GetArrayLength() < 1 || rws.GetArrayLength() > Limits.TableRows) return Fail("SC-T-TABLE", $"A table has 1 to {Limits.TableRows} rows.", "table.rows");
            var rows = new List<IReadOnlyList<string>>();
            i = 0;
            foreach (var r in rws.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() != columns.Count) return Fail("SC-T-TABLE", "Every row has one cell per column.", $"table.rows.{i}");
                rows.Add(r.EnumerateArray().Select(c => Cut(Clean(c.ValueKind == JsonValueKind.Number ? JsNumber(Number(c) ?? double.NaN) : Str(c)), Limits.Cell)).ToList());
                i++;
            }
            table = new ToolTable(Cut(CleanOf(Prop(tb, "title")), Limits.TableTitle), columns, rows);
        }

        var note = CleanOf(Prop(t, "note"));
        var created = Str(Prop(t, "created"));
        var version = Number(Prop(t, "version"));
        return new ToolCheck(new CommunityTool
        {
            Id = Str(Prop(t, "id")) is { } tid && IsSiteId(tid) ? tid : null,
            Version = version is { } v && double.IsFinite(v) && Math.Truncate(v) == v && v > 0 ? v : null,
            Name = name, Maker = Cut(CleanOf(Prop(t, "maker")), Limits.Maker), Summary = summary, Group = group, Symbol = symbol, Color = color,
            Inputs = inputs, Outputs = outputs, Table = table,
            Note = note.Length > 0 ? Cut(note, Limits.Note) : null,
            Created = created != null && IsDate(created) ? created : null,
        }, null);
    }

    /// <summary>JavaScript's `t.format > 1`, for a number or a number written as text: a later format than this.</summary>
    private static bool Later(JsonElement? format)
    {
        if (Number(format) is { } n) return n > Format;
        if (Str(format) is { } s)
        {
            var trimmed = JsTrim(s);
            if (trimmed.Length == 0) return false;
            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > Format;
        }
        return false;
    }

    /// <summary>^[a-z0-9-]{3,24}$: an id the site gave.</summary>
    public static bool IsSiteId(string s) => s.Length is >= 3 and <= 24 && s.All(c => IsLower(c) || IsDigit(c) || c == '-');

    /// <summary>^\d{4}-\d{2}-\d{2}$</summary>
    private static bool IsDate(string s) => s.Length == 10 && s[4] == '-' && s[7] == '-' && s.Where((c, k) => k != 4 && k != 7).All(IsDigit);

    /// <summary>What an input is worth in a formula: a number clamped to its range (not a number: its default), a choice's
    /// value (one not offered: its default), a toggle's 1 or 0. <paramref name="given"/> is what was put in: a double, a
    /// bool, anything else (not a number); <paramref name="has"/> false when nothing was.</summary>
    public static double InputValue(ToolInput input, object? given, bool has = true)
    {
        if (input.Kind == ToolInputKind.Toggle) return (!has ? input.DefaultOn : given is true || given is 1.0) ? 1 : 0;
        if (input.Kind == ToolInputKind.Choice) return given is double c && input.Options.Any(o => o.Value == c) ? c : input.Default;
        var v = given is double d && double.IsFinite(d) ? d : input.Default;
        return Math.Min(input.Max ?? double.PositiveInfinity, Math.Max(input.Min ?? double.NegativeInfinity, v));
    }

    /// <summary>A checked tool's results for these inputs (by id; one not there takes its default): each its label, its
    /// value and text, its unit and whether it is shown.</summary>
    public static IReadOnlyList<ToolResult> Run(CommunityTool tool, IReadOnlyDictionary<string, object?>? given = null)
    {
        var env = new Dictionary<string, double>(Constants, StringComparer.Ordinal);
        foreach (var input in tool.Inputs)
        {
            object? g = null;
            var has = given != null && given.TryGetValue(input.Id, out g);
            env[input.Id] = InputValue(input, g, has);
        }
        var results = new List<ToolResult>(tool.Outputs.Count);
        foreach (var o in tool.Outputs)
        {
            var value = Evaluate(o.ValueTree, env);
            if (o.Id != null) env[o.Id] = value;
            var shown = o.WhenTree == null || Truthy(Evaluate(o.WhenTree, env));
            results.Add(new ToolResult(o.Label, value, FormatNumber(value, o.Decimals), o.Unit, shown));
        }
        return results;
    }

    /// <summary>A checked tool written as format 1 JSON (the reference's checked tool: what this app keeps).</summary>
    public static JsonObject ToJson(CommunityTool t)
    {
        static JsonNode Num(double v) => JsonValue.Create(v)!;
        var o = new JsonObject { ["format"] = Format };
        if (t.Id != null) o["id"] = t.Id;
        if (t.Version is { } ver) o["version"] = Num(ver);
        o["name"] = t.Name;
        o["maker"] = t.Maker;
        o["summary"] = t.Summary;
        o["group"] = t.Group;
        o["symbol"] = t.Symbol;
        o["color"] = t.Color;
        var inputs = new JsonArray();
        foreach (var x in t.Inputs)
        {
            var i = new JsonObject { ["id"] = x.Id, ["kind"] = x.Kind.ToString().ToLowerInvariant(), ["label"] = x.Label };
            switch (x.Kind)
            {
                case ToolInputKind.Number:
                    i["unit"] = x.Unit;
                    i["default"] = Num(x.Default);
                    if (x.Min is { } min) i["min"] = Num(min);
                    if (x.Max is { } max) i["max"] = Num(max);
                    i["step"] = Num(x.Step);
                    break;
                case ToolInputKind.Choice:
                    i["options"] = new JsonArray(x.Options.Select(op => (JsonNode)new JsonObject { ["label"] = op.Label, ["value"] = Num(op.Value) }).ToArray());
                    i["default"] = Num(x.Default);
                    break;
                default:
                    i["default"] = x.DefaultOn;
                    break;
            }
            inputs.Add(i);
        }
        o["inputs"] = inputs;
        var outputs = new JsonArray();
        foreach (var x in t.Outputs)
        {
            var r = new JsonObject();
            if (x.Id != null) r["id"] = x.Id;
            r["label"] = x.Label;
            r["value"] = x.Value;
            r["unit"] = x.Unit;
            r["decimals"] = x.Decimals;
            if (x.When != null) r["when"] = x.When;
            outputs.Add(r);
        }
        o["outputs"] = outputs;
        if (t.Table is { } tb)
            o["table"] = new JsonObject
            {
                ["title"] = tb.Title,
                ["columns"] = new JsonArray(tb.Columns.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
                ["rows"] = new JsonArray(tb.Rows.Select(row => (JsonNode)new JsonArray(row.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray())).ToArray()),
            };
        if (t.Note != null) o["note"] = t.Note;
        if (t.Created != null) o["created"] = t.Created;
        return o;
    }
}

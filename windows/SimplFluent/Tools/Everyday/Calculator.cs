using System.Globalization;
using System.Windows.Input;
using Simpl.Support;

namespace Simpl.Tools;

// The scientific calculator, as the web's (the Mac's Calculator.swift): Apple's scientific keys, key for key — the memory
// keys and brackets, 2nd, the powers and roots, the logs, the trig and hyperbolic functions with their inverses under 2nd,
// e, EE, π, Rand, Rad/Deg, and the number pad with AC, +/−, %, and the four operators — but algebraic: what is typed
// stays on the line, the whole of it (2 + 3 × 4², sin(30) + √(16), (2 + 3)²), with its result worked out underneath as it
// grows, and = makes the result the line to carry on from (the sum it came from kept small above it). A function goes in
// front of its argument; x², x³, ¹/x, x! and % follow their number; two things side by side multiply (2π, 2(3 + 4)).
// Nothing typed can make it fall over: a line that cannot be worked out says Error.

#pragma warning disable CA2255
internal static class CalculatorRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("calc", c => new CalculatorTool(c).View, c => new CalculatorCompact(c).View);
}
#pragma warning restore CA2255

internal enum CalcTokKind { Num, Constant, Fn, Open, Close, Comma, Op, Post, RootOp }

/// <summary>One thing on the calculator's line (the Mac's CalcTok).</summary>
internal readonly record struct CalcTok(CalcTokKind Kind, string S = "")
{
    /// <summary>The digits as typed: "12", "3.5", "2E−4", "0.".</summary>
    public static CalcTok Num(string s) => new(CalcTokKind.Num, s);
    /// <summary>"π" or "e".</summary>
    public static CalcTok Constant(string c) => new(CalcTokKind.Constant, c);
    /// <summary>A function and its bracket: "sin", "sin⁻¹", "log", "log₂", "√", "∛", "ln" …</summary>
    public static CalcTok Fn(string f) => new(CalcTokKind.Fn, f);
    public static readonly CalcTok Open = new(CalcTokKind.Open);
    public static readonly CalcTok Close = new(CalcTokKind.Close);
    public static readonly CalcTok Comma = new(CalcTokKind.Comma);
    /// <summary>"+", "−", "×", "÷", "^".</summary>
    public static CalcTok Op(string o) => new(CalcTokKind.Op, o);
    /// <summary>"²", "³", "⁻¹", "!", "%".</summary>
    public static CalcTok Post(string p) => new(CalcTokKind.Post, p);
    /// <summary>"^(1÷": the y-th root of x.</summary>
    public static readonly CalcTok RootOp = new(CalcTokKind.RootOp);

    public string Text => Kind switch
    {
        CalcTokKind.Fn => S + "(",
        CalcTokKind.Open => "(",
        CalcTokKind.Close => ")",
        CalcTokKind.Comma => ", ",
        CalcTokKind.RootOp => "^(1÷",
        _ => S,
    };

    /// <summary>Opens a bracket (a function's, a root's, a plain one).</summary>
    public bool Opens => Kind is CalcTokKind.Open or CalcTokKind.Fn or CalcTokKind.RootOp;
}

internal enum CalcOutcome { Empty, Incomplete, Bad, Ok }

/// <summary>
/// What a line works out to: nothing yet, an operator or a bracket still wanting a number (nothing to say yet), a line
/// that cannot be worked out (a division by zero, a root of a negative), or its value and the line with its brackets closed.
/// </summary>
internal readonly record struct CalcResult(CalcOutcome Kind, double Value = 0, string Closed = "");

/// <summary>
/// The calculator's state and its keys (the Mac's CalcEngine). The whole tool keeps one and the pin's flyout another, each
/// kept as it was left while the view comes and goes.
/// </summary>
internal sealed class CalcEngine
{
    /// <summary>The whole tool's (in its popup, from its tile or its pin).</summary>
    public static CalcEngine Full { get; } = new();
    /// <summary>The pin's flyout's.</summary>
    public static CalcEngine Quick { get; } = new();

    public sealed record Past(string Sum, double Value);

    public List<CalcTok> Toks { get; private set; } = new();
    /// <summary>The line is a result: a number starts afresh, an operator carries on from it.</summary>
    public bool Fresh { get; private set; }
    public double FreshValue { get; private set; }
    /// <summary>The sum a result came from.</summary>
    public string Sub { get; private set; } = "";
    public double Mem { get; private set; }
    public bool Deg { get; private set; } = true;
    public bool Second { get; private set; }
    public bool Err { get; private set; }
    /// <summary>The sums worked out with =, newest first.</summary>
    public List<Past> History { get; } = new();

    /// <summary>A key pressed, a value put in, the history cleared.</summary>
    public event Action? Changed;

    /// <summary>(0.3.5) As it starts: nothing typed, no sums, no memory, in degrees — the pin's then carries nothing to
    /// the whole tool.</summary>
    public bool Untouched => Toks.Count == 0 && History.Count == 0 && Mem == 0 && Deg && !Second && !Fresh;

    /// <summary>(0.3.5) Another's line, sums, memory and Rad/Deg taken on — the whole tool's taking the pin's, its flyout
    /// opened full screen — so the whole calculator goes on from where the pin's was.</summary>
    public void Take(CalcEngine from)
    {
        Toks = from.Toks.ToList();
        Fresh = from.Fresh;
        FreshValue = from.FreshValue;
        Sub = from.Sub;
        Mem = from.Mem;
        Deg = from.Deg;
        Second = from.Second;
        Err = from.Err;
        History.Clear();
        History.AddRange(from.History);
        Changed?.Invoke();
    }

    // ---- keys ----

    public void Press(string key)
    {
        PressKey(key);
        Changed?.Invoke();
    }

    private void PressKey(string key)
    {
        Err = false;
        if (key.Length == 1 && key[0] is >= '0' and <= '9')
        {
            Digit(key);
            return;
        }
        switch (key)
        {
            case ".": Point(); break;
            case "ee":
                if (Fresh) CarryOn();
                if (Last is { Kind: CalcTokKind.Num } n && !n.S.Contains('E')) SetLast(CalcTok.Num(n.S + "E"));
                break;
            case "back": Back(); break;
            case "neg": Negate(); break;
            case "x2" or "x3" or "inv" or "fact" or "pct":
                if (Fresh) CarryOn();
                if (EndsValue) Toks.Add(CalcTok.Post(PostMarks[key]));
                break;
            case "ac":
                Toks = new();
                Sub = "";
                Fresh = false;
                break;
            case "+" or "-" or "*" or "/" or "^" or "ypow" or "root": Operate(key); break;
            case "(":
                if (Fresh) StartFresh(new() { CalcTok.Open }); else Toks.Add(CalcTok.Open);
                break;
            case ")":
                if (OpenCount > 0 && EndsValue) Toks.Add(CalcTok.Close);
                break;
            case "comma":
                if (EndsValue && OpenCount > 0) Toks.Add(CalcTok.Comma);
                break;
            case "=": Equal(); break;
            case "pi" or "e":
                var c = CalcTok.Constant(key == "pi" ? "π" : "e");
                if (Fresh) StartFresh(new() { c }); else Toks.Add(c);
                break;
            case "rand":
                var s = Plain(Math.Round(Random.Shared.NextDouble() * 1e6, MidpointRounding.AwayFromZero) / 1e6);
                if (Fresh) StartFresh(new() { CalcTok.Num(s) }); else Toks.Add(CalcTok.Num(s));
                break;
            case "mc": Mem = 0; break;
            case "mplus" or "mminus":
                if (Evaluate() is { Kind: CalcOutcome.Ok } r) Mem += key == "mplus" ? r.Value : -r.Value;
                break;
            case "mr":
                var t = TokensOf(Mem);
                if (Fresh) StartFresh(t); else Toks.AddRange(t);
                break;
            case "rad": Deg = !Deg; break;
            case "second": Second = !Second; break;
            default:
                if (Functions.TryGetValue(key, out var f))
                {
                    if (Fresh)
                    {
                        // a result gets wrapped: sin(result)
                        Toks = f.Concat(Toks).Append(CalcTok.Close).ToList();
                        Fresh = false;
                        Sub = "";
                    }
                    else Toks.AddRange(f);
                }
                break;
        }
    }

    private CalcTok? Last => Toks.Count > 0 ? Toks[^1] : null;

    private void SetLast(CalcTok t) => Toks[^1] = t;

    private void Digit(string d)
    {
        if (Fresh) { StartFresh(new() { CalcTok.Num(d) }); return; }
        if (Last is { Kind: CalcTokKind.Num } n) SetLast(CalcTok.Num(n.S + d));
        else Toks.Add(CalcTok.Num(d));
    }

    private void Point()
    {
        if (Fresh) { StartFresh(new() { CalcTok.Num("0.") }); return; }
        if (Last is { Kind: CalcTokKind.Num } n)
        {
            if (!n.S.Contains('.') && !n.S.Contains('E')) SetLast(CalcTok.Num(n.S + "."));
        }
        else Toks.Add(CalcTok.Num("0."));
    }

    private void Back()
    {
        if (Fresh) { StartFresh(new()); return; }
        if (Last is not { } last) return;
        if (last.Kind == CalcTokKind.Num && last.S.Length > 1) SetLast(CalcTok.Num(last.S[..^1]));
        else Toks.RemoveAt(Toks.Count - 1);
    }

    /// <summary>+/−: a minus in front of the number being typed, on or off (or the exponent's sign, after EE).</summary>
    private void Negate()
    {
        if (Fresh) CarryOn();
        if (Last is not { Kind: CalcTokKind.Num } n)
        {
            if (Toks.Count == 0 || EndsOp) Toks.Add(CalcTok.Op("−"));
            return;
        }
        var s = n.S;
        if (s.EndsWith('E')) { SetLast(CalcTok.Num(s + "−")); return; }
        if (s.EndsWith("E−", StringComparison.Ordinal)) { SetLast(CalcTok.Num(s[..^1])); return; }
        var i = Toks.Count - 1;
        if (i > 0 && Toks[i - 1] == CalcTok.Op("−") && WantsValue(i - 1 > 0 ? Toks[i - 2] : null)) Toks.RemoveAt(i - 1);
        else Toks.Insert(i, CalcTok.Op("−"));
    }

    private void Operate(string key)
    {
        var o = key switch
        {
            "+" => CalcTok.Op("+"),
            "-" => CalcTok.Op("−"),
            "*" => CalcTok.Op("×"),
            "/" => CalcTok.Op("÷"),
            "root" => CalcTok.RootOp,
            _ => CalcTok.Op("^"),
        };
        var minus = o == CalcTok.Op("−");
        if (Fresh) CarryOn();
        if (Last is not { } last)
        {
            if (minus) Toks.Add(o);
            return;
        }
        if (last.Kind == CalcTokKind.Op)
        {
            // 2 × − 3; otherwise the operator is swapped
            if (minus && last.S is "×" or "÷" or "^") Toks.Add(o);
            else SetLast(o);
            return;
        }
        switch (last.Kind)
        {
            case CalcTokKind.Open or CalcTokKind.Comma or CalcTokKind.Fn or CalcTokKind.RootOp:
                if (minus) Toks.Add(o);
                return;
            default:
                Toks.Add(o);
                break;
        }
    }

    private void Equal()
    {
        if (Fresh) return; // (a result already: = again changes nothing)
        var r = Evaluate();
        if (r.Kind != CalcOutcome.Ok)
        {
            Err = r.Kind == CalcOutcome.Bad;
            return;
        }
        Sub = r.Closed + " =";
        History.Insert(0, new Past(r.Closed, r.Value));
        if (History.Count > 60) History.RemoveRange(60, History.Count - 60);
        Toks = TokensOf(r.Value);
        FreshValue = r.Value;
        Fresh = true;
    }

    /// <summary>A value put on the line (a result from the history).</summary>
    public void Insert(double v)
    {
        var t = TokensOf(v);
        if (Fresh || Toks.Count == 0) StartFresh(t);
        else if (EndsValue)
        {
            Toks.Add(CalcTok.Op("×"));
            Toks.AddRange(t);
        }
        else Toks.AddRange(t);
        Changed?.Invoke();
    }

    public void ClearHistory()
    {
        History.Clear();
        Changed?.Invoke();
    }

    private void StartFresh(List<CalcTok> t)
    {
        Toks = t;
        Fresh = false;
        Sub = "";
    }

    private void CarryOn()
    {
        Fresh = false;
        Sub = "";
    }

    /// <summary>Something for Escape to clear (else Escape is the window's: back to the tiles, the sheet closed).</summary>
    public bool HasLine => Toks.Count > 0 || Err || Sub.Length > 0;

    // ---- reading the line ----

    /// <summary>A number is wanted next.</summary>
    private bool EndsOp => WantsValue(Last);

    /// <summary>Something a mark or an operator can follow.</summary>
    private bool EndsValue => Last switch
    {
        null => false,
        { Kind: CalcTokKind.Num } n => n.S.Length > 0 && (char.IsDigit(n.S[^1]) || n.S[^1] == '.'),
        { Kind: CalcTokKind.Constant or CalcTokKind.Close or CalcTokKind.Post } => true,
        _ => false,
    };

    private int OpenCount => Toks.Aggregate(0, (n, t) => t.Opens ? n + 1 : t == CalcTok.Close ? n - 1 : n);

    /// <summary>Whether a minus after <paramref name="t"/> is a sign (at the start, after an operator or a bracket) rather than a subtraction.</summary>
    public static bool WantsValue(CalcTok? t) => t is not { } x || x.Kind is CalcTokKind.Op or CalcTokKind.Open or CalcTokKind.Comma or CalcTokKind.Fn or CalcTokKind.RootOp;

    /// <summary>The line as shown: binary operators spaced, a sign close to its number.</summary>
    public string LineText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            CalcTok? prev = null;
            foreach (var t in Toks)
            {
                if (t.Kind == CalcTokKind.Op && !WantsValue(prev)) sb.Append(' ').Append(t.S).Append(' ');
                else sb.Append(t.Text);
                prev = t;
            }
            return sb.ToString();
        }
    }

    /// <summary>The display: the line, and the small line above it — the result as the line grows, or the sum a result came from.</summary>
    public (string Line, string Sub) Shown
    {
        get
        {
            if (Toks.Count == 0) return ("0", Err ? "Error" : "");
            if (Fresh) return (Format(FreshValue), Sub);
            var r = Evaluate();
            return r.Kind switch
            {
                CalcOutcome.Ok => (LineText, "= " + Format(r.Value)),
                CalcOutcome.Bad => (LineText, "Error"),
                _ => (LineText, Err ? "Error" : ""),
            };
        }
    }

    /// <summary>The value showing: a result, or what the line works out to.</summary>
    public double? Value
    {
        get
        {
            if (Fresh) return FreshValue;
            return Evaluate() is { Kind: CalcOutcome.Ok } r ? r.Value : null;
        }
    }

    public CalcResult Evaluate()
    {
        if (Toks.Count == 0) return new(CalcOutcome.Empty);
        var list = new List<CalcTok>();
        foreach (var t in Toks)
        {
            if (t == CalcTok.RootOp) list.AddRange(new[] { CalcTok.Op("^"), CalcTok.Open, CalcTok.Num("1"), CalcTok.Op("÷") });
            else list.Add(t);
        }
        var open = Math.Max(0, OpenCount);
        for (var k = 0; k < open; k++) list.Add(CalcTok.Close);
        var closed = LineText + new string(')', open);
        var p = new CalcParser(list, Deg);
        try
        {
            var v = p.Expression();
            if (p.At < list.Count) return new(CalcOutcome.Bad);
            return double.IsFinite(v) ? new(CalcOutcome.Ok, v, closed) : new(CalcOutcome.Bad);
        }
        catch (CalcParser.Fail f) { return new(f.Incomplete ? CalcOutcome.Incomplete : CalcOutcome.Bad); }
    }

    // ---- the keyboard, and a line typed in ----

    /// <summary>What the keyboard's characters press: digits, . + − × ÷ ^ ( ) % ! , = x for ×, p for π, e.</summary>
    public static string? KeyFor(string text)
    {
        if (text.Length != 1) return null;
        var c = text[0];
        if (c is >= '0' and <= '9') return text;
        return c switch
        {
            '=' => "=",
            '%' => "pct",
            '!' => "fact",
            ',' => "comma",
            '(' or ')' or '.' or '+' or '-' or '*' or '/' or '^' => text,
            'x' or 'X' or '×' => "*",
            '÷' => "/",
            '−' => "-",
            'p' or 'π' => "pi",
            'e' => "e",
            '²' => "x2",
            '³' => "x3",
            _ => null,
        };
    }

    /// <summary>What the keyboard's other keys press: Enter for =, Backspace and Delete take the last thing off, Escape clears.</summary>
    public static string? KeyFor(Key key) => key switch
    {
        Key.Enter => "=",
        Key.Back or Key.Delete => "back",
        Key.Escape => "ac",
        _ => null,
    };

    private static readonly (string Word, string Key)[] Words =
    {
        ("sinh", "sinh"), ("cosh", "cosh"), ("tanh", "tanh"), ("asin", "asin"), ("acos", "acos"), ("atan", "atan"),
        ("sin", "sin"), ("cos", "cos"), ("tan", "tan"), ("sqrt", "sqrt"), ("√", "sqrt"), ("cbrt", "cbrt"), ("∛", "cbrt"),
        ("ln", "ln"), ("log2", "log2"), ("log", "log10"), ("pi", "pi"),
    };

    /// <summary>A line typed in key by key ("sin(30)+√(16)=", the screenshot suite's --calc-expr): a function's bracket comes with it.</summary>
    public void Feed(string line)
    {
        var i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i])) { i++; continue; }
            var word = Words.FirstOrDefault(w => string.CompareOrdinal(line, i, w.Word, 0, w.Word.Length) == 0);
            if (word.Word != null)
            {
                PressKey(word.Key);
                i += word.Word.Length;
                if (word.Key != "pi" && i < line.Length && line[i] == '(') i++;
                continue;
            }
            if (KeyFor(line[i].ToString()) is { } k) PressKey(k);
            i++;
        }
        Changed?.Invoke();
    }

    /// <summary>The screenshot suite's line for the calculator first opened (--calc-expr "2^10=").</summary>
    public static void FeedLaunch(CalcEngine e)
    {
        if (Launch.Take("", "calc-expr") is { Length: > 0 } line) e.Feed(line);
    }

    // ---- tables and numbers ----

    private static readonly Dictionary<string, string> PostMarks = new() { ["x2"] = "²", ["x3"] = "³", ["inv"] = "⁻¹", ["fact"] = "!", ["pct"] = "%" };

    /// <summary>What each function key puts on the line.</summary>
    private static readonly Dictionary<string, CalcTok[]> Functions = new()
    {
        ["sin"] = new[] { CalcTok.Fn("sin") }, ["cos"] = new[] { CalcTok.Fn("cos") }, ["tan"] = new[] { CalcTok.Fn("tan") },
        ["asin"] = new[] { CalcTok.Fn("sin⁻¹") }, ["acos"] = new[] { CalcTok.Fn("cos⁻¹") }, ["atan"] = new[] { CalcTok.Fn("tan⁻¹") },
        ["sinh"] = new[] { CalcTok.Fn("sinh") }, ["cosh"] = new[] { CalcTok.Fn("cosh") }, ["tanh"] = new[] { CalcTok.Fn("tanh") },
        ["asinh"] = new[] { CalcTok.Fn("sinh⁻¹") }, ["acosh"] = new[] { CalcTok.Fn("cosh⁻¹") }, ["atanh"] = new[] { CalcTok.Fn("tanh⁻¹") },
        ["ln"] = new[] { CalcTok.Fn("ln") }, ["log10"] = new[] { CalcTok.Fn("log") }, ["log2"] = new[] { CalcTok.Fn("log₂") }, ["logy"] = new[] { CalcTok.Fn("log") },
        ["sqrt"] = new[] { CalcTok.Fn("√") }, ["cbrt"] = new[] { CalcTok.Fn("∛") },
        ["ex"] = new[] { CalcTok.Constant("e"), CalcTok.Op("^"), CalcTok.Open },
        ["10x"] = new[] { CalcTok.Num("10"), CalcTok.Op("^"), CalcTok.Open },
        ["2x"] = new[] { CalcTok.Num("2"), CalcTok.Op("^"), CalcTok.Open },
    };

    /// <summary>The digits of a number as typed, read (null while it is still being typed: "2E", "2E−").</summary>
    public static double? Number(string s)
    {
        var t = s.Replace("−", "-").Replace("E", "e");
        if (t.EndsWith('e') || t.EndsWith("e-", StringComparison.Ordinal)) return null;
        if (t.StartsWith('.')) t = "0" + t;
        if (t.EndsWith('.')) t += "0";
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>A value as it goes back on the line: plain digits (E for a very large or small one), a sign in front.</summary>
    public static List<CalcTok> TokensOf(double v)
    {
        var digits = Plain(Math.Abs(v));
        return v < 0 ? new() { CalcTok.Op("−"), CalcTok.Num(digits) } : new() { CalcTok.Num(digits) };
    }

    /// <summary>Up to twelve figures, no grouping; E-notation past 10¹⁵ or under 10⁻⁹.</summary>
    public static string Plain(double v)
    {
        var a = Math.Abs(v);
        if (a != 0 && (a >= 1e15 || a < 1e-9))
            return v.ToString("0.###########e+00", CultureInfo.InvariantCulture).Replace("e+", "E").Replace("e-", "E−");
        return Significant(v, 12, false);
    }

    /// <summary>A value for the clipboard: plain digits with ASCII signs, for pasting anywhere.</summary>
    public static string Ascii(double v) => Plain(v).Replace("−", "-");

    /// <summary>The number for the display: up to twelve figures, thousands grouped, very large or small ones in e-notation.</summary>
    public static string Format(double v)
    {
        if (!double.IsFinite(v)) return "Error";
        var x = v == 0 ? 0 : v;
        var a = Math.Abs(x);
        if (a != 0 && (a >= 1e15 || a < 1e-9)) return x.ToString("0.########e+00", CultureInfo.InvariantCulture).Replace("-", "−");
        return Significant(x, 12, true).Replace("-", "−");
    }

    /// <summary>A number to so many significant figures, written out in full (no exponent), its thousands grouped or not.</summary>
    public static string Significant(double v, int figures, bool grouped)
    {
        if (v == 0 || !double.IsFinite(v)) return v == 0 ? "0" : v.ToString(CultureInfo.InvariantCulture);
        var rounded = v.ToString("G" + figures, CultureInfo.InvariantCulture);
        if (!decimal.TryParse(rounded, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return rounded;
        return d.ToString(grouped ? "#,0.############################" : "0.############################", CultureInfo.InvariantCulture);
    }
}

/// <summary>The line worked out (the Mac's CalcParser): the usual precedence, ^ from the right, − in front of a number, two things side by side multiplied.</summary>
internal sealed class CalcParser
{
    public sealed class Fail : Exception
    {
        public bool Incomplete { get; }
        public Fail(bool incomplete) => Incomplete = incomplete;
    }

    private readonly List<CalcTok> toks;
    private readonly bool deg;

    public int At { get; private set; }

    public CalcParser(List<CalcTok> toks, bool deg)
    {
        this.toks = toks;
        this.deg = deg;
    }

    private string? PeekOp() => At < toks.Count && toks[At].Kind == CalcTokKind.Op ? toks[At].S : null;

    private bool StartsValue() => At < toks.Count && toks[At].Kind is CalcTokKind.Num or CalcTokKind.Constant or CalcTokKind.Fn or CalcTokKind.Open;

    public double Expression()
    {
        var v = Term();
        while (PeekOp() is "+" or "−")
        {
            var o = PeekOp();
            At++;
            var r = Term();
            v = o == "+" ? v + r : v - r;
        }
        return v;
    }

    private double Term()
    {
        var v = Unary();
        while (true)
        {
            if (PeekOp() is "×" or "÷")
            {
                var o = PeekOp();
                At++;
                var r = Unary();
                v = o == "×" ? v * r : v / r;
            }
            else if (StartsValue()) v *= Unary();
            else return v;
        }
    }

    private double Unary()
    {
        if (PeekOp() == "−") { At++; return -Unary(); }
        if (PeekOp() == "+") { At++; return Unary(); }
        return Power();
    }

    private double Power()
    {
        var b = Postfix();
        if (PeekOp() == "^")
        {
            At++;
            var e = Unary();
            return Math.Pow(b, e);
        }
        return b;
    }

    private double Postfix()
    {
        var v = Atom();
        while (At < toks.Count && toks[At].Kind == CalcTokKind.Post)
        {
            var p = toks[At].S;
            At++;
            v = p switch
            {
                "!" => Factorial(v),
                "%" => v / 100,
                "²" => v * v,
                "³" => v * v * v,
                "⁻¹" => 1 / v,
                _ => v,
            };
        }
        return v;
    }

    private double Atom()
    {
        if (At >= toks.Count) throw new Fail(true);
        var t = toks[At];
        switch (t.Kind)
        {
            case CalcTokKind.Num:
                At++;
                return CalcEngine.Number(t.S) ?? throw new Fail(true);
            case CalcTokKind.Constant:
                At++;
                return t.S == "π" ? Math.PI : Math.E;
            case CalcTokKind.Fn:
            {
                At++;
                var args = new List<double> { Expression() };
                while (At < toks.Count && toks[At] == CalcTok.Comma)
                {
                    At++;
                    args.Add(Expression());
                }
                if (At >= toks.Count || toks[At] != CalcTok.Close) throw new Fail(true);
                At++;
                return Apply(t.S, args);
            }
            case CalcTokKind.Open:
            {
                At++;
                var v = Expression();
                if (At >= toks.Count || toks[At] != CalcTok.Close) throw new Fail(true);
                At++;
                return v;
            }
            default:
                throw new Fail(false);
        }
    }

    private double ToRad(double x) => deg ? x * Math.PI / 180 : x;
    private double FromRad(double x) => deg ? x * 180 / Math.PI : x;
    /// <summary>sin(π) is 0, not 1.2e-16 (π being what a double can hold of it).</summary>
    private static double Snap(double x) => Math.Abs(x) < 1e-14 ? 0 : x;

    private double Apply(string f, List<double> args)
    {
        var x = args[0];
        return f switch
        {
            "sin" => Snap(Math.Sin(ToRad(x))),
            "cos" => Snap(Math.Cos(ToRad(x))),
            "tan" => Snap(Math.Tan(ToRad(x))),
            "sin⁻¹" => FromRad(Math.Asin(x)),
            "cos⁻¹" => FromRad(Math.Acos(x)),
            "tan⁻¹" => FromRad(Math.Atan(x)),
            "sinh" => Math.Sinh(x),
            "cosh" => Math.Cosh(x),
            "tanh" => Math.Tanh(x),
            "sinh⁻¹" => Math.Asinh(x),
            "cosh⁻¹" => Math.Acosh(x),
            "tanh⁻¹" => Math.Atanh(x),
            "ln" => Math.Log(x),
            "log" => args.Count > 1 ? Math.Log(x) / Math.Log(args[1]) : Math.Log10(x),
            "log₂" => Math.Log2(x),
            "√" => Math.Sqrt(x),
            "∛" => Math.Cbrt(x),
            _ => throw new Fail(false),
        };
    }

    /// <summary>x! — and Γ(x + 1) for a number that is not whole.</summary>
    public static double Factorial(double n)
    {
        if (n < 0 && n == Math.Round(n)) return double.NaN;
        if (n == Math.Round(n))
        {
            if (n > 170) return double.PositiveInfinity;
            var r = 1.0;
            for (var k = 2.0; k <= n; k++) r *= k;
            return r;
        }
        return Gamma(n + 1);
    }

    private static readonly double[] Lanczos =
    {
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
        12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
    };

    /// <summary>Γ(x), as C's tgamma (Lanczos' approximation, and the reflection for x under ½).</summary>
    private static double Gamma(double x)
    {
        if (x < 0.5) return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1 - x));
        if (x > 171.7) return double.PositiveInfinity;
        x -= 1;
        var a = Lanczos[0];
        var t = x + 7.5;
        for (var i = 1; i < 9; i++) a += Lanczos[i] / (x + i);
        var half = Math.Pow(t, (x + 0.5) / 2); // (in two halves: t^(x+½) alone would overflow before e^−t brings it down)
        return Math.Sqrt(2 * Math.PI) * half * (half * Math.Exp(-t)) * a;
    }
}

/// <summary>
/// A key of the calculator (the Mac's CalcKey): its id (what it presses), what it shows, its kind (a digit, an
/// operator, a top key, a function), and whether it is two keys wide.
/// </summary>
internal sealed record CalcKey(string Id, string Label, CalcKey.Kinds Kind = CalcKey.Kinds.Fn, bool Wide = false)
{
    public enum Kinds { Num, Op, Top, Fn }

    private static CalcKey K(string id, string label, Kinds kind = Kinds.Fn) => new(id, label, kind);

    /// <summary>The scientific layout: five rows of ten, as Apple's.</summary>
    public static readonly CalcKey[][] Scientific =
    {
        new[] { K("(", "("), K(")", ")"), K("mc", "mc"), K("mplus", "m+"), K("mminus", "m−"), K("mr", "mr"), K("ac", "AC", Kinds.Top), K("neg", "+/−", Kinds.Top), K("pct", "%", Kinds.Top), K("/", "÷", Kinds.Op) },
        new[] { K("second", "2ⁿᵈ"), K("x2", "x²"), K("x3", "x³"), K("^", "xʸ"), K("ex", "eˣ"), K("10x", "10ˣ"), K("7", "7", Kinds.Num), K("8", "8", Kinds.Num), K("9", "9", Kinds.Num), K("*", "×", Kinds.Op) },
        new[] { K("inv", "¹⁄x"), K("sqrt", "²√x"), K("cbrt", "³√x"), K("root", "ʸ√x"), K("ln", "ln"), K("log10", "log₁₀"), K("4", "4", Kinds.Num), K("5", "5", Kinds.Num), K("6", "6", Kinds.Num), K("-", "−", Kinds.Op) },
        new[] { K("fact", "x!"), K("sin", "sin"), K("cos", "cos"), K("tan", "tan"), K("e", "e"), K("ee", "EE"), K("1", "1", Kinds.Num), K("2", "2", Kinds.Num), K("3", "3", Kinds.Num), K("+", "+", Kinds.Op) },
        new[] { K("rad", "Rad"), K("sinh", "sinh"), K("cosh", "cosh"), K("tanh", "tanh"), K("pi", "π"), K("rand", "Rand"), new CalcKey("0", "0", Kinds.Num, true), K(".", ".", Kinds.Num), K("=", "=", Kinds.Op) },
    };

    /// <summary>The basic layout, for the pin's flyout: the number pad with brackets, a root, a square and π.</summary>
    public static readonly CalcKey[][] Basic =
    {
        new[] { K("(", "("), K(")", ")"), K("sqrt", "√"), K("x2", "x²"), K("pi", "π") },
        new[] { K("ac", "AC", Kinds.Top), K("neg", "+/−", Kinds.Top), K("pct", "%", Kinds.Top), K("^", "xʸ"), K("/", "÷", Kinds.Op) },
        new[] { K("7", "7", Kinds.Num), K("8", "8", Kinds.Num), K("9", "9", Kinds.Num), K("back", "⌫", Kinds.Top), K("*", "×", Kinds.Op) },
        new[] { K("4", "4", Kinds.Num), K("5", "5", Kinds.Num), K("6", "6", Kinds.Num), K("ln", "ln"), K("-", "−", Kinds.Op) },
        new[] { K("1", "1", Kinds.Num), K("2", "2", Kinds.Num), K("3", "3", Kinds.Num), K("sin", "sin"), K("+", "+", Kinds.Op) },
        new[] { new CalcKey("0", "0", Kinds.Num, true), K(".", ".", Kinds.Num), K("cos", "cos"), K("=", "=", Kinds.Op) },
    };

    /// <summary>What a key under 2nd becomes: its id and label.</summary>
    public static readonly Dictionary<string, (string Id, string Label)> Alternates = new()
    {
        ["ex"] = ("ypow", "yˣ"), ["10x"] = ("2x", "2ˣ"), ["ln"] = ("logy", "logᵧ"), ["log10"] = ("log2", "log₂"),
        ["sin"] = ("asin", "sin⁻¹"), ["cos"] = ("acos", "cos⁻¹"), ["tan"] = ("atan", "tan⁻¹"),
        ["sinh"] = ("asinh", "sinh⁻¹"), ["cosh"] = ("acosh", "cosh⁻¹"), ["tanh"] = ("atanh", "tanh⁻¹"),
    };

    public static readonly Dictionary<string, string> Titles = new()
    {
        ["mc"] = "Memory clear", ["mplus"] = "Memory add", ["mminus"] = "Memory subtract", ["mr"] = "Memory recall", ["ac"] = "All clear",
        ["neg"] = "Change sign", ["pct"] = "Percent", ["second"] = "Second functions", ["x2"] = "Squared", ["x3"] = "Cubed", ["^"] = "To the power of",
        ["ex"] = "e to the x", ["10x"] = "10 to the x", ["inv"] = "One over x", ["sqrt"] = "Square root", ["cbrt"] = "Cube root",
        ["root"] = "The y-th root of x", ["ln"] = "Natural log", ["log10"] = "Log base 10", ["fact"] = "Factorial", ["ee"] = "Times ten to the",
        ["pi"] = "Pi", ["rand"] = "A random number between 0 and 1", ["/"] = "Divide", ["*"] = "Multiply", ["-"] = "Subtract", ["+"] = "Add",
        ["="] = "Equals", ["back"] = "Delete", ["e"] = "Euler’s number",
    };
}

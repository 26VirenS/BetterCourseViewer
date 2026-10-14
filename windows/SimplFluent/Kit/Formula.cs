using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfMath;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;

namespace Simpl.Kit;

/// <summary>
/// Canvas's formulas as maths, not code (the Mac shows Canvas's own formula picture; Windows' text cannot draw it, an
/// SVG). Canvas writes a formula as a picture carrying its TeX (data-equation-content, its title, or an alt of "LaTeX:
/// …"): here the TeX is typeset (WPF-Math) into a shape drawn in the text's own colour, at the text's size, so it reads
/// as part of the sentence in light and dark alike. TeX that cannot be typeset is written out as plain maths instead —
/// v⃗, dv⃗/dt, x², α — never as TeX source.
/// </summary>
public static class Formula
{
    /// <summary>The TeX of a picture that is one of Canvas's formulas, or null for any other picture.</summary>
    public static string? TexOf(IReadOnlyDictionary<string, string> attrs)
    {
        var cls = attrs.GetValueOrDefault("class") ?? "";
        var src = attrs.GetValueOrDefault("src") ?? "";
        var tex = attrs.GetValueOrDefault("data-equation-content");
        if (tex == null && !cls.Contains("equation_image", StringComparison.OrdinalIgnoreCase) && !src.Contains("/equation_images/", StringComparison.OrdinalIgnoreCase)) return null;
        tex ??= attrs.GetValueOrDefault("title");
        if (string.IsNullOrWhiteSpace(tex)) tex = Regex.Replace(attrs.GetValueOrDefault("alt") ?? "", @"^\s*LaTeX:\s*", "", RegexOptions.IgnoreCase);
        if (string.IsNullOrWhiteSpace(tex) && src.Contains("/equation_images/", StringComparison.OrdinalIgnoreCase))
        {
            // (the picture's own address carries the TeX, encoded twice: /equation_images/%255Cvec%257Bv%257D)
            var tail = src[(src.IndexOf("/equation_images/", StringComparison.OrdinalIgnoreCase) + 17)..];
            var q = tail.IndexOfAny(new[] { '?', '#' });
            if (q >= 0) tail = tail[..q];
            try { tex = Uri.UnescapeDataString(Uri.UnescapeDataString(tail)); } catch { tex = null; }
        }
        return string.IsNullOrWhiteSpace(tex) ? null : tex.Trim();
    }

    private static readonly Dictionary<(string, double), Geometry?> Typeset = new();

    /// <summary>The formula typeset at a text size: a shape in the text's colour, or null for TeX WPF-Math cannot read.</summary>
    public static FrameworkElement? Shape(string tex, double size)
    {
        var key = (tex, size);
        if (!Typeset.TryGetValue(key, out var geometry))
        {
            try
            {
                var formula = WpfTeXFormulaParser.Instance.Parse(tex);
                var environment = WpfTeXEnvironment.Create(TexStyle.Text, size, "Segoe UI");
                geometry = formula.RenderToGeometry(environment, size);
                if (geometry.Bounds.IsEmpty || geometry.Bounds.Width <= 0) geometry = null;
                else geometry.Freeze();
            }
            catch (Exception e)
            {
                Support.Log.Write($"formula: {e.GetType().Name} {e.Message}");
                geometry = null;
            }
            if (Typeset.Count > 400) Typeset.Clear();
            Typeset[key] = geometry;
        }
        if (geometry == null) return null;
        var b = geometry.Bounds;
        var path = new Path { Data = geometry, Stretch = Stretch.None, Margin = new Thickness(1, 0, 1, 0), SnapsToDevicePixels = false };
        path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, B.Text);
        // (from the shape's own top-left, so nothing of it is cut and nothing hangs in the air above it)
        path.RenderTransform = new TranslateTransform(-b.X, -b.Y);
        var holder = new System.Windows.Controls.Canvas { Width = b.Width + 2, Height = b.Height, ClipToBounds = false, Children = { path } };
        System.Windows.Automation.AutomationProperties.SetName(holder, Readable(tex));
        holder.ToolTip = null;
        return holder;
    }

    // ---- the last fallback: the formula as plain maths ----

    private static readonly Dictionary<string, string> Symbols = new()
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ε", ["zeta"] = "ζ",
        ["eta"] = "η", ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ",
        ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ",
        ["varphi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω", ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ",
        ["Lambda"] = "Λ", ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
        ["cdot"] = "·", ["times"] = "×", ["div"] = "÷", ["pm"] = "±", ["mp"] = "∓", ["le"] = "≤", ["leq"] = "≤", ["ge"] = "≥",
        ["geq"] = "≥", ["ne"] = "≠", ["neq"] = "≠", ["approx"] = "≈", ["sim"] = "∼", ["equiv"] = "≡", ["propto"] = "∝",
        ["infty"] = "∞", ["to"] = "→", ["rightarrow"] = "→", ["leftarrow"] = "←", ["Rightarrow"] = "⇒", ["leftrightarrow"] = "↔",
        ["int"] = "∫", ["iint"] = "∬", ["oint"] = "∮", ["sum"] = "∑", ["prod"] = "∏", ["partial"] = "∂", ["nabla"] = "∇",
        ["degree"] = "°", ["circ"] = "°", ["angle"] = "∠", ["perp"] = "⊥", ["parallel"] = "∥", ["in"] = "∈", ["notin"] = "∉",
        ["cup"] = "∪", ["cap"] = "∩", ["subset"] = "⊂", ["subseteq"] = "⊆", ["forall"] = "∀", ["exists"] = "∃", ["hbar"] = "ħ",
        ["ell"] = "ℓ", ["ldots"] = "…", ["cdots"] = "⋯", ["dots"] = "…", ["quad"] = " ", ["qquad"] = "  ", ["prime"] = "′",
        ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan", ["log"] = "log", ["ln"] = "ln", ["exp"] = "exp", ["lim"] = "lim",
        ["min"] = "min", ["max"] = "max", ["det"] = "det",
    };

    private static readonly Dictionary<string, char> Accents = new()
    {
        ["vec"] = '⃗', ["overrightarrow"] = '⃗', ["hat"] = '̂', ["widehat"] = '̂', ["bar"] = '̄',
        ["overline"] = '̅', ["dot"] = '̇', ["ddot"] = '̈', ["tilde"] = '̃', ["widetilde"] = '̃',
    };

    private const string Sup = "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼⁽⁾ⁿⁱ", SupOf = "0123456789+-=()ni";
    private const string Sub = "₀₁₂₃₄₅₆₇₈₉₊₋₌₍₎ₐₑₒₓₕₖₗₘₙₚₛₜ", SubOf = "0123456789+-=()aeoxhklmnpst";

    /// <summary>TeX written out as plain maths: "\vec{a}=\frac{d\vec{v}}{dt}" → "a⃗ = dv⃗/dt".</summary>
    public static string Readable(string tex)
    {
        var i = 0;
        var s = Group(tex.Trim(), ref i, '\0');
        s = Regex.Replace(s, @"\s*([=<>≤≥≠≈±×÷→·])\s*", " $1 ");
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    /// <summary>Read up to the closing character (or the end): commands, groups, scripts.</summary>
    private static string Group(string t, ref int i, char close)
    {
        var sb = new StringBuilder();
        while (i < t.Length)
        {
            var c = t[i];
            if (c == close) { i++; break; }
            if (c == '{') { i++; sb.Append(Group(t, ref i, '}')); continue; }
            if (c == '^' || c == '_') { i++; sb.Append(Script(Arg(t, ref i), c == '^')); continue; }
            if (c == '\\')
            {
                i++;
                var start = i;
                while (i < t.Length && char.IsLetter(t[i])) i++;
                var name = t[start..i];
                if (name.Length == 0) { if (i < t.Length) { var ch = t[i++]; sb.Append(ch is ',' or ';' or ' ' or ':' or '!' ? " " : ch.ToString()); } continue; }
                while (i < t.Length && t[i] == ' ') i++; // (as TeX: the spaces after a command's name are not the formula's)
                sb.Append(Command(name, t, ref i));
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>The next argument: a group, a command, or one character.</summary>
    private static string Arg(string t, ref int i)
    {
        while (i < t.Length && t[i] == ' ') i++;
        if (i >= t.Length) return "";
        if (t[i] == '{') { i++; return Group(t, ref i, '}'); }
        if (t[i] == '\\')
        {
            i++;
            var start = i;
            while (i < t.Length && char.IsLetter(t[i])) i++;
            var name = t[start..i];
            while (i < t.Length && t[i] == ' ') i++;
            return Command(name, t, ref i);
        }
        return t[i++].ToString();
    }

    private static string Command(string name, string t, ref int i)
    {
        switch (name)
        {
            case "frac" or "dfrac" or "tfrac":
            {
                var a = Arg(t, ref i);
                var b = Arg(t, ref i);
                return $"{Wrap(a)}/{Wrap(b)}";
            }
            case "sqrt":
            {
                var a = Arg(t, ref i);
                return a.Length == 1 ? $"√{a}" : $"√({a})";
            }
            case "text" or "mathrm" or "mathit" or "mathbf" or "textbf" or "operatorname" or "mathbb" or "boldsymbol":
                return Arg(t, ref i);
            case "left" or "right" or "big" or "Big" or "bigg" or "Bigg" or "displaystyle" or "limits":
                return "";
        }
        if (Accents.TryGetValue(name, out var mark))
        {
            var a = Arg(t, ref i);
            return a.Length == 0 ? "" : a + mark;
        }
        return Symbols.TryGetValue(name, out var sym) ? sym : name;
    }

    /// <summary>A part of a fraction, in brackets when it is more than one thing.</summary>
    private static string Wrap(string a)
    {
        a = a.Trim();
        var plain = a.Length <= 1 || Regex.IsMatch(a, @"^[\p{L}\p{N}\p{Mn}′.]+$");
        return plain ? a : $"({a})";
    }

    /// <summary>A superscript or subscript as Unicode's raised or lowered figures where it has them all; else ^(…) / _(…).</summary>
    private static string Script(string a, bool up)
    {
        if (up && a == "°") return "°";
        var from = up ? SupOf : SubOf;
        var to = up ? Sup : Sub;
        var sb = new StringBuilder();
        foreach (var c in a)
        {
            var k = from.IndexOf(c);
            if (k < 0) return up ? (a.Length == 1 ? $"^{a}" : $"^({a})") : (a.Length == 1 ? $"_{a}" : $"_({a})");
            sb.Append(to[k]);
        }
        return sb.ToString();
    }
}

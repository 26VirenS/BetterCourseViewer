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

    private static readonly Dictionary<(string, double), (Geometry Shape, double? Baseline)?> Typeset = new();

    /// <summary>TeX's maths is drawn in Latin Modern, whose small letters stand lower than Segoe UI's (an x-height of
    /// 0.43 of its size against 0.5): typeset this much larger, a formula's letters are as tall as the sentence's round
    /// it, as MathJax matches a page's own type.</summary>
    private const double MathScale = 1.15;

    /// <summary>Marks a formula's shape that knows where its baseline is: it then stands on the sentence's baseline
    /// (the shape's foot is the baseline, what hangs below it drawn below), not centred on the line.</summary>
    private static readonly object OnBaseline = new();

    /// <summary>How a formula's shape stands in its line of text: on the text's baseline, as a typeset formula does,
    /// when its baseline is known; else centred on the line.</summary>
    public static BaselineAlignment Alignment(FrameworkElement shape) => shape.Tag == OnBaseline ? BaselineAlignment.Baseline : BaselineAlignment.Center;

    /// <summary>The formula typeset at a text size: a shape in the text's colour, or null for TeX WPF-Math cannot read.
    /// It stands on the text's baseline (<see cref="Alignment"/>), so "such as v⃗?" reads as one line and an answer that
    /// is only a formula sits level with its letter.</summary>
    public static FrameworkElement? Shape(string tex, double size)
    {
        var key = (tex, size);
        if (!Typeset.TryGetValue(key, out var made))
        {
            try
            {
                var scale = size * MathScale;
                var formula = WpfTeXFormulaParser.Instance.Parse(tex);
                var environment = WpfTeXEnvironment.Create(TexStyle.Text, scale, "Segoe UI");
                // (drawn as RenderToGeometry draws it, its first element told apart: WPF-Math draws a formula from the
                //  top of its box, the whole box first, at the box's baseline — the line the formula stands on)
                var geometry = new GeometryGroup();
                var finder = new BaselineFinder(new GeometryElementRenderer(geometry, scale));
                XamlMath.Rendering.TeXFormulaExtensions.RenderTo(formula, finder, environment, 0, 0);
                if (geometry.Bounds.IsEmpty || geometry.Bounds.Width <= 0) made = null;
                else
                {
                    geometry.Freeze();
                    var b = geometry.Bounds;
                    // (a baseline is believed only where it falls within the formula's ink, or just under it)
                    double? baseline = finder.Y is { } y && y * scale >= b.Top && y * scale <= b.Bottom + scale ? y * scale : null;
                    made = (geometry, baseline);
                }
            }
            catch (Exception e)
            {
                Support.Log.Write($"formula: {e.GetType().Name} {e.Message}");
                made = null;
            }
            if (Typeset.Count > 400) Typeset.Clear();
            Typeset[key] = made;
        }
        if (made is not { } m) return null;
        var bounds = m.Shape.Bounds;
        var path = new Path { Data = m.Shape, Stretch = Stretch.None, SnapsToDevicePixels = false };
        path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, B.Text);
        // (from the shape's own top-left, so nothing of it is cut and nothing hangs in the air above it)
        path.RenderTransform = new TranslateTransform(-bounds.X, -bounds.Y);
        // (standing on its baseline, the holder is only as tall as what rises above it: what hangs below — a fraction's
        //  lower half, a subscript — is drawn under the holder's foot, which a canvas never cuts)
        var tall = m.Baseline is { } line ? Math.Max(1, line - bounds.Top) : bounds.Height;
        var holder = new System.Windows.Controls.Canvas { Width = bounds.Width + 2, Height = tall, Margin = new Thickness(1, 0, 1, 0), ClipToBounds = false, Children = { path } };
        if (m.Baseline != null) holder.Tag = OnBaseline;
        System.Windows.Automation.AutomationProperties.SetName(holder, Readable(tex));
        holder.ToolTip = null;
        return holder;
    }

    /// <summary>Passes WPF-Math's drawing on to the renderer that makes the shape, keeping where the formula's whole box
    /// was drawn: its baseline, in the formula's own units.</summary>
    private sealed class BaselineFinder : XamlMath.Rendering.IElementRenderer
    {
        private readonly XamlMath.Rendering.IElementRenderer inner;
        public double? Y { get; private set; }

        public BaselineFinder(XamlMath.Rendering.IElementRenderer inner) => this.inner = inner;

        public void RenderElement(XamlMath.Boxes.Box box, double x, double y)
        {
            Y ??= y;
            inner.RenderElement(box, x, y);
        }

        public void RenderCharacter(XamlMath.CharInfo info, double x, double y, XamlMath.Rendering.IBrush? foreground) => inner.RenderCharacter(info, x, y, foreground);
        public void RenderRectangle(XamlMath.Rendering.Rectangle rectangle, XamlMath.Rendering.IBrush? foreground) => inner.RenderRectangle(rectangle, foreground);
        public void RenderLine(XamlMath.Rendering.Point point0, XamlMath.Rendering.Point point1, XamlMath.Rendering.IBrush? foreground) => inner.RenderLine(point0, point1, foreground);
        public void RenderTransformed(XamlMath.Boxes.Box box, IEnumerable<XamlMath.Rendering.Transformations.Transformation> transforms, double x, double y) => inner.RenderTransformed(box, transforms, x, y);
        public void FinishRendering() => inner.FinishRendering();
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

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Gas Laws (the Mac's GasLawsTool): PV = nRT with any one of the four left blank and solved, each field in the unit it
// likes; and the combined law P1V1/T1 = P2V2/T2 with one of six blank, or a pair held constant (Boyle, Charles,
// Gay-Lussac). A piston drawing follows the answer: its height the volume, its dots the gas's density, its colour the
// temperature's warmth, moving over 200 ms (not when Windows' animation effects are off). The sums are in
// Tools/Chemistry/Model/ChemGas.cs.

#pragma warning disable CA2255
internal static class GasRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("gas", c => new GasTool(c), c => new GasQuick(c));
}
#pragma warning restore CA2255

/// <summary>The tool's tabs and what a launch asks (--tool gas --tool-state ideal|combined, or --gas ideal;
/// --gas-values "P=1 atm,V=22.4 L,T=273.15 K" or "P1=1 atm,V1=2 L,T1=300 K,P2=2 atm,T2=600 K"; --gas-constant T|P|V).</summary>
internal static class GasPages
{
    public static readonly string[] Keys = { "ideal", "combined" };
    public static readonly string[] Names = { "Ideal gas", "Combined" };

    public static string? AskedTab()
    {
        var t = (ChemTools.LaunchState("gas") ?? ChemCalc.Flag("gas"))?.Trim().ToLowerInvariant();
        return t != null && Keys.Contains(t) ? t : null;
    }

    /// <summary>The launch's values, read once and shared by the tabs and the pin.</summary>
    public static readonly Lazy<Dictionary<string, string>> Values = new(() => ChemCalc.Pairs(ChemCalc.Flag("gas-values")));

    /// <summary>The units a field's letter takes.</summary>
    public static string[] UnitsOf(string letter) => letter[0] switch { 'P' => ChemGasUnits.Pressure, 'V' => ChemGasUnits.Volume, 'T' => ChemGasUnits.Temperature, _ => ChemGasUnits.Amount };

    /// <summary>The words for a field's letter.</summary>
    public static string LabelOf(string letter) => letter[0] switch { 'P' => "Pressure", 'V' => "Volume", 'T' => "Temperature", _ => "Amount" } + (letter.Length > 1 ? $" {letter[1..]}" : "");
}

/// <summary>A field of the gas laws: its box and its unit menu, remembered per tool, started from the launch's values.</summary>
internal sealed class GasField
{
    public readonly TextBox Box;
    public readonly ComboBox Units;
    public readonly FrameworkElement View;
    private readonly string[] units;
    private readonly string fallback;

    public GasField(string letter, ChemCalc.Later later, bool quick = false)
    {
        units = GasPages.UnitsOf(letter);
        fallback = units[0];
        var (start, startUnit) = GasPages.Values.Value.TryGetValue(letter, out var v) ? ChemCalc.Split(v, units, fallback) : (null, null);
        // (the launch's blank: a letter given as "P=" leaves the field empty however it was remembered)
        Box = ChemCalc.Field("gas", letter, "?", later, start, GasPages.LabelOf(letter));
        Units = ChemCalc.Choice("gas", letter + ".unit", units, fallback, later, startUnit, quick ? 68 : null);
        if (quick)
        {
            Units.Margin = new Thickness(6, 0, 0, 0);
            View = K.Columns("*, auto", Box.Col(0), Units.Col(1));
        }
        else View = ChemCalc.WithUnit(GasPages.LabelOf(letter), Box, Units);
    }

    public string Unit => ChemCalc.Chosen(Units, fallback);
    public GasValue Value => new(ChemNum.Read(Box.Text), Unit);
    public bool Blank => Box.Text.Trim().Length == 0;

    /// <summary>The answer shown faintly in the blank field, "?" otherwise.</summary>
    public void Solved(string? answer) => Box.PlaceholderText = answer ?? "?";
}

/// <summary>The whole tool: its two tabs, the page chosen under them.</summary>
internal sealed class GasTool : ContentControl
{
    private readonly ContentControl host = new() { Focusable = false };
    private readonly FrameworkElement?[] pages = new FrameworkElement?[2];
    private string said = "";

    public GasTool(ToolContext c)
    {
        Focusable = false;
        _ = c.TakeInfo("carry");
        var tabs = ChemCalc.Tabs("gas", GasPages.Names, GasPages.Keys, GasPages.AskedTab(), Show);
        Content = ToolKit.Page(K.V(16, tabs, host));
        Show(tabs.Selected);
    }

    private void Show(int i)
    {
        pages[i] ??= i == 0 ? new IdealGasPage() : new CombinedGasPage();
        host.Content = pages[i];
        ChemCalc.Shows("gas", ref said, GasPages.Keys[i]);
    }
}

/// <summary>PV = nRT: four fields, the one left blank solved; the formula with the solved letter marked, and the piston.</summary>
internal sealed class IdealGasPage : ContentControl
{
    public IdealGasPage()
    {
        Focusable = false;
        var under = new ContentControl { Focusable = false };
        var formulaLine = new TextBlock { FontSize = 22, FontFamily = K.DisplayFace, TextWrapping = TextWrapping.Wrap };
        formulaLine.Fg(B.Text);
        var answer = Everyday.Figures("—", 40, FontWeights.Bold);
        var solvedFor = K.Wrapped("", Ty.Caption, B.Text2);
        var piston = new PistonView { Height = 230 };
        string? plain = null;
        var said = "";
        GasField p = null!, v = null!, n = null!, t = null!;
        TextBox formula = null!;
        FrameworkElement formulaField = null!;
        var later = new ChemCalc.Later(() =>
        {
            plain = null;
            var grams = n.Unit == "g";
            formulaField.Visibility = grams ? Visibility.Visible : Visibility.Collapsed;
            var f = grams ? ChemFormula.Parse(formula.Text) : null;
            var r = ChemGas.Ideal(p.Value, v.Value, n.Value, t.Value, f);
            foreach (var g in new[] { p, v, n, t }) g.Solved(null);
            if (!r.Ok)
            {
                var all = new[] { p, v, n, t };
                var typedSomething = all.Any(g => !g.Blank);
                // (a number that is not one is said; a blank is only wanting)
                var unreadable = all.FirstOrDefault(g => !g.Blank && ChemNum.Read(g.Box.Text) == null);
                ChemCalc.Say(under, unreadable != null ? $"{GasPages.LabelOf(unreadable == p ? "P" : unreadable == v ? "V" : unreadable == n ? "n" : "T")} needs to be a number." : r.Message, problem: typedSomething);
                Formula(formulaLine, null);
                answer.Text = "—";
                solvedFor.Text = "Fill in three of the four; the blank one is solved.";
                piston.Show(null, null, null);
                ChemCalc.Shows("gas ideal", ref said, "nothing");
                return;
            }
            ChemCalc.Say(under, null);
            Formula(formulaLine, r.Solved);
            var figures = Math.Max(3, new[] { p, v, n, t }.Where(g => !g.Blank).Select(g => ChemNum.FiguresOf(g.Box.Text)).DefaultIfEmpty(4).Max());
            var text = ChemCalc.Shown(ChemNum.With(r.Value, r.Unit, figures));
            answer.Text = text;
            var letter = r.Solved switch { GasVar.P => "P", GasVar.V => "V", GasVar.N => "n", _ => "T" };
            (r.Solved switch { GasVar.P => p, GasVar.V => v, GasVar.N => n, _ => t }).Solved(ChemNum.Sig(r.Value, figures));
            solvedFor.Text = $"Solved for {letter} · R = 0.082057 L·atm/(mol·K) · in base units P = {ChemNum.Sig(r.Atm)} atm, V = {ChemNum.Sig(r.Litres)} L, n = {ChemNum.Sig(r.Moles)} mol, T = {ChemNum.Sig(r.Kelvin)} K";
            piston.Show(r.Litres, r.Moles / r.Litres, r.Kelvin);
            plain = $"{letter} = {ChemNum.With(r.Value, r.Unit, figures)} (PV = nRT; P = {ChemNum.Sig(r.Atm)} atm, V = {ChemNum.Sig(r.Litres)} L, n = {ChemNum.Sig(r.Moles)} mol, T = {ChemNum.Sig(r.Kelvin)} K)";
            AutomationProperties.SetName(answer, $"{GasPages.LabelOf(letter)}: {text}");
            ChemCalc.Shows("gas ideal", ref said, $"{letter}={ChemNum.Sig(r.Value, figures)} {r.Unit}");
        });
        p = new GasField("P", later);
        v = new GasField("V", later);
        n = new GasField("n", later);
        t = new GasField("T", later);
        formula = ChemCalc.Field("gas", "formula", "CO2", later, GasPages.Values.Value.TryGetValue("formula", out var ff) ? ff : null, "The gas’s formula");
        formulaField = ToolKit.Field("Gas (for grams)", formula);
        var copy = ChemCalc.Copy(() => plain);
        var inputs = K.V(12, p.View, v.View, n.View, formulaField, t.View, under);
        var card = K.Card(K.V(10, formulaLine, Everyday.Shrinks(answer), solvedFor, copy), new Thickness(20));
        var side = K.V(16, card, K.Card(piston, new Thickness(12)));
        Content = ToolKit.Columns(inputs, side, 380, 860);
        ChemCalc.CopyOnCtrlC(this, () => plain);
        later.Now();
    }

    /// <summary>"PV = nRT", the solved letter in the accent and bold.</summary>
    private static void Formula(TextBlock t, GasVar? solved)
    {
        t.Inlines.Clear();
        foreach (var (text, var) in new (string, GasVar?)[] { ("P", GasVar.P), ("V", GasVar.V), (" = ", null), ("n", GasVar.N), ("R", null), ("T", GasVar.T) })
        {
            var run = new Run(text);
            if (var != null && var == solved) { run.FontWeight = FontWeights.Bold; run.SetResourceReference(TextElement.ForegroundProperty, B.AccentText); }
            t.Inlines.Add(run);
        }
        AutomationProperties.SetName(t, solved == null ? "P V equals n R T" : $"P V equals n R T, solved for {solved}");
    }
}

/// <summary>P1V1/T1 = P2V2/T2: six fields, one blank solved; a pair held constant by a chip hides it.</summary>
internal sealed class CombinedGasPage : ContentControl
{
    private static readonly (string Key, string Name, string Formula)[] Laws =
    {
        ("", "All six", "P₁V₁/T₁ = P₂V₂/T₂"),
        ("T", "Boyle (T constant)", "P₁V₁ = P₂V₂"),
        ("P", "Charles (P constant)", "V₁/T₁ = V₂/T₂"),
        ("V", "Gay-Lussac (V constant)", "P₁/T₁ = P₂/T₂"),
    };

    private string constant;

    public CombinedGasPage()
    {
        Focusable = false;
        constant = ChemCalc.Flag("gas-constant")?.Trim().ToUpperInvariant() ?? ChemPrefs.String("gas", "constant") ?? "";
        if (!Laws.Any(l => l.Key == constant)) constant = "";
        var under = new ContentControl { Focusable = false };
        var formulaLine = new TextBlock { FontSize = 22, FontFamily = K.DisplayFace, TextWrapping = TextWrapping.Wrap };
        formulaLine.Fg(B.Text);
        var answer = Everyday.Figures("—", 40, FontWeights.Bold);
        var solvedFor = K.Wrapped("", Ty.Caption, B.Text2);
        var piston = new PistonView { Height = 230 };
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        string? plain = null;
        var said = "";
        GasField[] fields = null!;
        var letters = new[] { "P1", "V1", "T1", "P2", "V2", "T2" };
        var later = new ChemCalc.Later(() =>
        {
            plain = null;
            var hidden = constant switch { "T" => new[] { 2, 5 }, "P" => new[] { 0, 3 }, "V" => new[] { 1, 4 }, _ => Array.Empty<int>() };
            for (var i = 0; i < 6; i++) fields[i].View.Visibility = hidden.Contains(i) ? Visibility.Collapsed : Visibility.Visible;
            var r = ChemGas.Combined(fields.Select(f => f.Value).ToArray(), constant.Length == 0 ? null : constant);
            foreach (var f in fields) f.Solved(null);
            var law = Laws.First(l => l.Key == constant);
            if (!r.Ok)
            {
                var asked = fields.Where((f, i) => !hidden.Contains(i)).ToList();
                var unreadable = asked.FirstOrDefault(f => !f.Blank && ChemNum.Read(f.Box.Text) == null);
                ChemCalc.Say(under, unreadable != null ? $"{GasPages.LabelOf(letters[Array.IndexOf(fields, unreadable)])} needs to be a number." : r.Message, problem: asked.Any(f => !f.Blank));
                formulaLine.Text = law.Formula;
                answer.Text = "—";
                solvedFor.Text = "Fill in all but one; the blank one is solved.";
                piston.Show(null, null, null);
                ChemCalc.Shows("gas combined", ref said, "nothing");
                return;
            }
            ChemCalc.Say(under, null);
            formulaLine.Inlines.Clear();
            var solvedWord = letters[r.Solved];
            var pretty = solvedWord[0] + (solvedWord[1] == '1' ? "₁" : "₂");
            var at = law.Formula.IndexOf(pretty, StringComparison.Ordinal);
            formulaLine.Inlines.Add(new Run(law.Formula[..at]));
            var mark = new Run(pretty) { FontWeight = FontWeights.Bold };
            mark.SetResourceReference(TextElement.ForegroundProperty, B.AccentText);
            formulaLine.Inlines.Add(mark);
            formulaLine.Inlines.Add(new Run(law.Formula[(at + pretty.Length)..]));
            var figures = Math.Max(3, fields.Where(f => !f.Blank).Select(f => ChemNum.FiguresOf(f.Box.Text)).DefaultIfEmpty(4).Max());
            var text = ChemCalc.Shown(ChemNum.With(r.Value, r.Unit, figures));
            answer.Text = text;
            fields[r.Solved].Solved(ChemNum.Sig(r.Value, figures));
            solvedFor.Text = $"Solved for {pretty} · {law.Name}";
            // (the piston shows the second state: its volume, its density by P/T, its warmth)
            var b = r.Base;
            piston.Show(constant == "V" ? null : b[4], b[3] / (ChemGas.R * b[5]), constant == "T" ? null : b[5]);
            plain = $"{solvedWord} = {ChemNum.With(r.Value, r.Unit, figures)} ({law.Formula})";
            AutomationProperties.SetName(answer, $"{GasPages.LabelOf(solvedWord)}: {text}");
            ChemCalc.Shows("gas combined", ref said, $"{solvedWord}={ChemNum.Sig(r.Value, figures)} {r.Unit}");
        });
        fields = letters.Select(l => new GasField(l, later)).ToArray();
        void Chips()
        {
            chips.Children.Clear();
            foreach (var (key, name, _) in Laws)
            {
                var k = key;
                var b = PTKit.Choice(name, key == constant, () => { constant = k; ChemPrefs.Set("gas", "constant", k); Chips(); later.Now(); });
                b.Margin = new Thickness(0, 0, 8, 8);
                chips.Children.Add(b);
            }
        }
        Chips();
        var before = K.V(12, K.Text("Before", Ty.BodyStrong), fields[0].View, fields[1].View, fields[2].View);
        var after = K.V(12, K.Text("After", Ty.BodyStrong), fields[3].View, fields[4].View, fields[5].View);
        var pair = K.Columns("*, 16, *", before.Col(0), after.Col(2));
        var copy = ChemCalc.Copy(() => plain);
        var inputs = K.V(12, chips, pair, under);
        var card = K.Card(K.V(10, formulaLine, Everyday.Shrinks(answer), solvedFor, copy), new Thickness(20));
        var side = K.V(16, card, K.Card(piston, new Thickness(12)));
        Content = ToolKit.Columns(inputs, side, 380, 860);
        ChemCalc.CopyOnCtrlC(this, () => plain);
        later.Now();
    }
}

/// <summary>
/// A cylinder with a piston: the piston's height follows the volume, the dots' number the gas's density (n/V), their
/// colour the temperature's warmth (blue cold, red hot) — each change over 200 ms, easing out, or at once where
/// Windows' animation effects are off (Motion.On). The dots keep their places in the gas as the piston moves.
/// </summary>
internal sealed class PistonView : FrameworkElement
{
    public static readonly DependencyProperty LiftProperty = DependencyProperty.Register(nameof(Lift), typeof(double), typeof(PistonView),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WarmthProperty = DependencyProperty.Register(nameof(Warmth), typeof(double), typeof(PistonView),
        new FrameworkPropertyMetadata(0.3, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(PistonView),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PistonBrushProperty = DependencyProperty.Register(nameof(PistonBrush), typeof(Brush), typeof(PistonView),
        new FrameworkPropertyMetadata(Brushes.DarkGray, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Color Cold = K.Hex("#3f8ef7"), Hot = K.Hex("#ff4b2b");
    private readonly List<Point> dots = new();
    private int wanted;
    private bool empty = true;

    public double Lift { get => (double)GetValue(LiftProperty); set => SetValue(LiftProperty, value); }
    public double Warmth { get => (double)GetValue(WarmthProperty); set => SetValue(WarmthProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush PistonBrush { get => (Brush)GetValue(PistonBrushProperty); set => SetValue(PistonBrushProperty, value); }

    public PistonView()
    {
        SetResourceReference(StrokeProperty, B.StrongStroke);
        SetResourceReference(PistonBrushProperty, B.Text2);
        MinHeight = 160;
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, "A cylinder with a piston showing the gas");
    }

    /// <summary>The state to show: litres (null: the piston at rest), moles per litre and kelvin (null: a middling warmth).</summary>
    public void Show(double? litres, double? density, double? kelvin)
    {
        empty = litres == null && density == null && kelvin == null;
        var lift = litres is { } l && l > 0 ? 0.12 + 0.78 * (1 - Math.Exp(-l / 25)) : 0.5;
        var warmth = kelvin is { } k ? Math.Clamp((k - 150) / 500, 0, 1) : 0.3;
        wanted = density is { } d && d > 0 ? (int)Math.Clamp(Math.Round(d * 700), 4, 140) : 0;
        // (the same seed each time: the first dots keep their places as more come or go)
        if (dots.Count < wanted)
        {
            var random = new Random(7);
            dots.Clear();
            for (var i = 0; i < wanted; i++) dots.Add(new Point(random.NextDouble(), random.NextDouble()));
        }
        Motion.To(this, LiftProperty, lift, 200, Motion.Decelerate);
        Motion.To(this, WarmthProperty, warmth, 200, Motion.Decelerate);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 40 || h < 40) return;
        var cyl = new Rect(w * 0.3, 12, w * 0.4, h - 24);
        var stroke = new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round };
        var gasColor = K.Mix(Cold, Hot, Math.Clamp(Warmth, 0, 1));
        var lift = Math.Clamp(Lift, 0.05, 0.95);
        var pistonY = cyl.Bottom - cyl.Height * lift;
        // (the gas: a wash of its colour under the piston, its dots in that colour)
        var gas = new Rect(cyl.Left, pistonY, cyl.Width, cyl.Bottom - pistonY);
        dc.DrawRectangle(new SolidColorBrush(K.WithAlpha(gasColor, empty ? 0.06 : 0.14)), null, gas);
        if (!empty)
        {
            var dot = new SolidColorBrush(gasColor);
            var r = Math.Clamp(cyl.Width / 40, 2.2, 4);
            for (var i = 0; i < Math.Min(wanted, dots.Count); i++)
            {
                var d = dots[i];
                dc.DrawEllipse(dot, null, new Point(gas.Left + r + d.X * (gas.Width - 2 * r), gas.Top + r + d.Y * Math.Max(0, gas.Height - 2 * r)), r, r);
            }
        }
        // (the cylinder: open at the top, rounded at the bottom)
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(new Point(cyl.Left, cyl.Top), false, false);
            g.LineTo(new Point(cyl.Left, cyl.Bottom - 10), true, true);
            g.ArcTo(new Point(cyl.Left + 10, cyl.Bottom), new Size(10, 10), 0, false, SweepDirection.Counterclockwise, true, true);
            g.LineTo(new Point(cyl.Right - 10, cyl.Bottom), true, true);
            g.ArcTo(new Point(cyl.Right, cyl.Bottom - 10), new Size(10, 10), 0, false, SweepDirection.Counterclockwise, true, true);
            g.LineTo(new Point(cyl.Right, cyl.Top), true, true);
        }
        body.Freeze();
        dc.DrawGeometry(null, stroke, body);
        // (the piston and its rod)
        var piston = new Rect(cyl.Left + 2, pistonY - 7, cyl.Width - 4, 14);
        dc.DrawRoundedRectangle(PistonBrush, null, piston, 3, 3);
        dc.DrawRectangle(PistonBrush, null, new Rect(cyl.Left + cyl.Width / 2 - 3, cyl.Top - 6, 6, Math.Max(0, pistonY - 7 - cyl.Top + 6)));
    }
}

/// <summary>The tool in its pin's flyout: the ideal gas law compact — four fields two by two and the answer.</summary>
internal sealed class GasQuick : ContentControl
{
    public GasQuick(ToolContext c)
    {
        Focusable = false;
        var answer = Everyday.Figures("—", 26, FontWeights.Bold);
        var under = new ContentControl { Focusable = false, Visibility = Visibility.Collapsed };
        GasField p = null!, v = null!, n = null!, t = null!;
        string? plain = null;
        var later = new ChemCalc.Later(() =>
        {
            plain = null;
            var f = n.Unit == "g" ? ChemFormula.Parse(ChemPrefs.String("gas", "formula")) : null;
            var r = ChemGas.Ideal(p.Value, v.Value, n.Value, t.Value, f);
            foreach (var g in new[] { p, v, n, t }) g.Solved(null);
            if (!r.Ok)
            {
                answer.Text = "—";
                ChemCalc.Say(under, new[] { p, v, n, t }.Any(g => !g.Blank) ? r.Message : "Fill in three; the blank one is solved.", problem: new[] { p, v, n, t }.Any(g => !g.Blank));
                return;
            }
            ChemCalc.Say(under, null);
            var letter = r.Solved switch { GasVar.P => "P", GasVar.V => "V", GasVar.N => "n", _ => "T" };
            var figures = Math.Max(3, new[] { p, v, n, t }.Where(g => !g.Blank).Select(g => ChemNum.FiguresOf(g.Box.Text)).DefaultIfEmpty(4).Max());
            answer.Text = ChemCalc.Shown($"{letter} = {ChemNum.With(r.Value, r.Unit, figures)}");
            (r.Solved switch { GasVar.P => p, GasVar.V => v, GasVar.N => n, _ => t }).Solved(ChemNum.Sig(r.Value, figures));
            plain = $"{letter} = {ChemNum.With(r.Value, r.Unit, figures)}";
        });
        p = new GasField("P", later, quick: true);
        v = new GasField("V", later, quick: true);
        n = new GasField("n", later, quick: true);
        t = new GasField("T", later, quick: true);
        FrameworkElement Labelled(string label, GasField g) => K.V(4, K.Text(label, Ty.Caption, B.Text2), g.View);
        var grid = K.V(8, K.Columns("*, 8, *", Labelled("P", p).Col(0), Labelled("V", v).Col(2)), K.Columns("*, 8, *", Labelled("n", n).Col(0), Labelled("T", t).Col(2)));
        var open = BioUI.OpenButton("Open Gas Laws", () => c.OpenFull(new Dictionary<string, string> { ["carry"] = "pin" }, null), "Open the whole tool");
        // (the whole tool reads the same remembered fields, so it goes on from these)
        c.Carry = () => new[] { p, v, n, t }.Any(g => !g.Blank) ? new Dictionary<string, string> { ["carry"] = "pin" } : null;
        ChemCalc.CopyOnCtrlC(this, () => plain);
        Content = K.V(10, grid, under, Everyday.Shrinks(answer), open);
        later.Now();
    }
}

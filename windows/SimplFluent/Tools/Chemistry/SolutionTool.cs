using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Solutions & pH (the Mac's SolutionsTool): four tabs — molarity from any two of amount, volume and molarity (with mass
// percent and ppm); a dilution M1V1 = M2V2 with one blank ("How much stock?"); pH, pOH, [H+] and [OH-] from any one,
// and the pH of a strong or weak acid or base from its concentration, on a colour strip 0–14; and a titration's
// equivalence point, its pH at the points that matter and its curve, with a buffer by Henderson–Hasselbalch. The sums
// are in Tools/Chemistry/Model/ChemSolution.cs; Kw is water's at 25 °C, the only temperature here.

#pragma warning disable CA2255
internal static class SolutionRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("solution", c => new SolutionTool(c), c => new SolutionQuick(c));
}
#pragma warning restore CA2255

/// <summary>The tool's tabs and what a launch asks (--tool solution --tool-state ph, or --solution ph; --solution-values
/// "H=1e-3", "kind=weak acid,conc=0.10,K=1.8e-5", "M1=2 M,V1=50 mL,M2=0.5 M", "amount=58.44 g,formula=NaCl,volume=1 L",
/// "titration=weak acid,analyte=0.1,volume=25,titrant=0.1,K=1.8e-5", "pKa=4.74,base=0.2,acid=0.1").</summary>
internal static class SolutionPages
{
    public static readonly string[] Keys = { "molarity", "dilution", "ph", "titration" };
    public static readonly string[] Names = { "Molarity", "Dilution", "pH", "Titration" };
    public static readonly string[] Givens = { "pH", "pOH", "[H⁺]", "[OH⁻]" };
    public static readonly string[] Kinds = { "strong acid", "strong base", "weak acid (Ka)", "weak base (Kb)" };
    public static readonly string[] Titrations = { "strong acid + strong base", "weak acid + strong base", "weak base + strong acid" };
    public static readonly Color Tint = K.Hex("#a259ff");

    public static string? AskedTab()
    {
        var t = (ChemTools.LaunchState("solution") ?? ChemCalc.Flag("solution"))?.Trim().ToLowerInvariant();
        return t != null && Keys.Contains(t) ? t : null;
    }

    public static readonly Lazy<Dictionary<string, string>> Values = new(() => ChemCalc.Pairs(ChemCalc.Flag("solution-values")));

    public static string? Value(string key) => Values.Value.TryGetValue(key, out var v) ? v : null;

    /// <summary>A launch's value split from its unit, or nothing.</summary>
    public static (string? Value, string? Unit) Split(string key, string[] units, string fallback) =>
        Value(key) is { } v ? ChemCalc.Split(v, units, fallback) : (null, null);

    /// <summary>"pH 2.88" with the strip's words for where it falls.</summary>
    public static string Describe(double ph) => ph < 3 ? "strongly acidic" : ph < 6.5 ? "acidic" : ph <= 7.5 ? "about neutral" : ph <= 11 ? "basic" : "strongly basic";
}

/// <summary>The whole tool: its four tabs, the page chosen under them.</summary>
internal sealed class SolutionTool : ContentControl
{
    private readonly ContentControl host = new() { Focusable = false };
    private readonly FrameworkElement?[] pages = new FrameworkElement?[4];
    private string said = "";

    public SolutionTool(ToolContext c)
    {
        Focusable = false;
        _ = c.TakeInfo("carry");
        var tabs = ChemCalc.Tabs("solution", SolutionPages.Names, SolutionPages.Keys, SolutionPages.AskedTab(), Show);
        Content = ToolKit.Page(K.V(16, tabs, host));
        Show(tabs.Selected);
    }

    private void Show(int i)
    {
        pages[i] ??= i switch { 0 => new MolarityPage(), 1 => new DilutionPage(), 2 => new PhPage(), _ => new TitrationPage() };
        host.Content = pages[i];
        ChemCalc.Shows("solution", ref said, SolutionPages.Keys[i]);
    }
}

/// <summary>Molarity: any two of the amount (moles or grams of a formula), the volume and the molarity; mass % and ppm too.</summary>
internal sealed class MolarityPage : ContentControl
{
    public MolarityPage()
    {
        Focusable = false;
        var under = new ContentControl { Focusable = false };
        var answer = Everyday.Figures("—", 40, FontWeights.Bold);
        var solvedFor = K.Wrapped("", Ty.Caption, B.Text2);
        var facts = new ContentControl { Focusable = false };
        string? plain = null;
        var said = "";
        TextBox amount = null!, formula = null!, volume = null!, molarity = null!, density = null!;
        ComboBox amountUnit = null!, volumeUnit = null!;
        FrameworkElement formulaField = null!;
        var later = new ChemCalc.Later(() =>
        {
            plain = null;
            var grams = ChemCalc.Chosen(amountUnit, "mol") == "g";
            formulaField.Visibility = grams ? Visibility.Visible : Visibility.Collapsed;
            var a = ChemNum.Read(amount.Text);
            var f = grams ? ChemFormula.Parse(formula.Text) : null;
            var vUnit = ChemCalc.Chosen(volumeUnit, "L");
            var v = ChemNum.Read(volume.Text) is { } vv ? vv * (vUnit == "mL" ? 0.001 : 1) : (double?)null;
            var m = ChemNum.Read(molarity.Text);
            var d = ChemNum.Read(density.Text) ?? 1.0;
            var r = ChemMolarity.Solve(grams ? null : a, grams ? a : null, f, v, m, d, out var message);
            foreach (var b in new[] { amount, volume, molarity }) b.PlaceholderText = "?";
            if (r == null)
            {
                var typed = new[] { amount, volume, molarity }.Any(b => b.Text.Trim().Length > 0);
                var unreadable = new[] { (amount, "The amount"), (volume, "The volume"), (molarity, "The molarity") }.FirstOrDefault(x => x.Item1.Text.Trim().Length > 0 && ChemNum.Read(x.Item1.Text) == null);
                ChemCalc.Say(under, unreadable.Item1 != null ? $"{unreadable.Item2} needs to be a number." : grams && f == null && formula.Text.Trim().Length > 0 ? "Can’t read that formula." : message, problem: typed);
                answer.Text = "—";
                solvedFor.Text = "Fill in two of amount, volume and molarity; the blank one is solved.";
                facts.Content = null;
                ChemCalc.Shows("solution molarity", ref said, "nothing");
                return;
            }
            ChemCalc.Say(under, null);
            var figures = Math.Max(3, new[] { amount, volume, molarity }.Where(b => b.Text.Trim().Length > 0).Select(b => ChemNum.FiguresOf(b.Text)).DefaultIfEmpty(4).Max());
            var big = r.Solved switch
            {
                "amount" => grams && r.Grams is { } g ? ChemNum.With(g, "g", figures) : ChemNum.With(r.Moles, "mol", figures),
                "volume" => ChemNum.With(vUnit == "mL" ? r.VolumeL * 1000 : r.VolumeL, vUnit, figures),
                _ => ChemNum.With(r.Molarity, "M", figures),
            };
            answer.Text = ChemCalc.Shown(big);
            (r.Solved switch { "amount" => amount, "volume" => volume, _ => molarity }).PlaceholderText = big;
            solvedFor.Text = $"Solved for the {r.Solved} · M = mol ÷ L";
            var rows = new List<(string, string, Color?)> { ("Moles", ChemNum.With(r.Moles, "mol", figures), null) };
            if (r.Grams is { } gr) rows.Add(("Mass", ChemNum.With(gr, "g", figures), null));
            rows.Add(("Volume", ChemNum.With(r.VolumeL, "L", figures), null));
            rows.Add(("Molarity", ChemNum.With(r.Molarity, "M", figures), null));
            if (r.MassPercent is { } pc) rows.Add(("Mass %", $"{ChemNum.Sig(pc, figures)} %", null));
            if (r.Ppm is { } ppm) rows.Add(("ppm", ChemNum.Sig(ppm, figures), null));
            facts.Content = ChemKit.Facts(rows.Select(x => (x.Item1, ChemCalc.Shown(x.Item2), x.Item3)));
            plain = string.Join("\n", rows.Select(x => $"{x.Item1}: {x.Item2}"));
            AutomationProperties.SetName(answer, $"{r.Solved}: {big}");
            ChemCalc.Shows("solution molarity", ref said, $"{r.Solved} {big}");
        });
        var (startAmount, startAmountUnit) = SolutionPages.Split("amount", new[] { "mol", "g" }, "mol");
        var (startVolume, startVolumeUnit) = SolutionPages.Split("volume", new[] { "L", "mL" }, "L");
        amount = ChemCalc.Field("solution", "amount", "?", later, startAmount, "The amount of solute");
        amountUnit = ChemCalc.Choice("solution", "amount.unit", new[] { "mol", "g" }, "mol", later, startAmountUnit);
        formula = ChemCalc.Field("solution", "formula", "NaCl", later, SolutionPages.Value("formula"), "The solute’s formula");
        formulaField = ToolKit.Field("Solute (for grams)", formula);
        volume = ChemCalc.Field("solution", "volume", "?", later, startVolume, "The volume of solution");
        volumeUnit = ChemCalc.Choice("solution", "volume.unit", new[] { "L", "mL" }, "L", later, startVolumeUnit);
        molarity = ChemCalc.Field("solution", "molarity", "?", later, SolutionPages.Value("molarity"), "The molarity");
        density = ChemCalc.Field("solution", "density", "1.00", later, SolutionPages.Value("density"), "The solution’s density");
        if (density.Text.Trim().Length == 0) density.Text = "1.00";
        var copy = ChemCalc.Copy(() => plain);
        var inputs = K.V(12, ChemCalc.WithUnit("Amount of solute", amount, amountUnit), formulaField, ChemCalc.WithUnit("Volume of solution", volume, volumeUnit),
            ToolKit.Field("Molarity", molarity, suffix: "M"), ToolKit.Field("Density of the solution (for mass % and ppm)", density, suffix: "g/mL", width: 220), under);
        var card = K.Card(K.V(10, Everyday.Shrinks(answer), solvedFor, copy), new Thickness(20));
        Content = ToolKit.Columns(inputs, K.V(16, card, facts), 360, 860);
        ChemCalc.CopyOnCtrlC(this, () => plain);
        later.Now();
    }
}

/// <summary>Dilution: M1V1 = M2V2, one blank solved, and how much stock to take.</summary>
internal sealed class DilutionPage : ContentControl
{
    public DilutionPage()
    {
        Focusable = false;
        var under = new ContentControl { Focusable = false };
        var answer = Everyday.Figures("—", 40, FontWeights.Bold);
        var words = K.Wrapped("", Ty.Body);
        var note = K.Wrapped("", Ty.Caption, B.Text2);
        string? plain = null;
        var said = "";
        TextBox[] boxes = null!;
        ComboBox unit = null!;
        var labels = new[] { "Stock concentration (M₁)", "Stock volume to take (V₁)", "Wanted concentration (M₂)", "Final volume (V₂)" };
        var letters = new[] { "M1", "V1", "M2", "V2" };
        var later = new ChemCalc.Later(() =>
        {
            plain = null;
            var values = boxes.Select(b => ChemNum.Read(b.Text)).ToArray();
            var r = ChemDilution.Solve(values[0], values[1], values[2], values[3], out var message);
            foreach (var b in boxes) b.PlaceholderText = "?";
            var u = ChemCalc.Chosen(unit, "mL");
            if (r == null)
            {
                var typed = boxes.Any(b => b.Text.Trim().Length > 0);
                var unreadable = Array.FindIndex(boxes, b => b.Text.Trim().Length > 0 && ChemNum.Read(b.Text) == null);
                ChemCalc.Say(under, unreadable >= 0 ? $"{labels[unreadable]} needs to be a number." : message, problem: typed);
                answer.Text = "—";
                words.Text = "Fill in three of the four; the blank one is solved.";
                note.Text = "";
                ChemCalc.Shows("solution dilution", ref said, "nothing");
                return;
            }
            var (k, value) = r.Value;
            ChemCalc.Say(under, message, problem: message != null);
            var figures = Math.Max(3, boxes.Where(b => b.Text.Trim().Length > 0).Select(b => ChemNum.FiguresOf(b.Text)).DefaultIfEmpty(4).Max());
            var all = new[] { values[0] ?? value, values[1] ?? value, values[2] ?? value, values[3] ?? value };
            var isVolume = k is 1 or 3;
            var big = ChemNum.With(value, isVolume ? u : "M", figures);
            answer.Text = ChemCalc.Shown(big);
            boxes[k].PlaceholderText = big;
            string M(double x) => ChemNum.With(x, "M", figures);
            string V(double x) => ChemNum.With(x, u, figures);
            words.Text = ChemCalc.Shown($"Take {V(all[1])} of the {M(all[0])} stock and make it up to {V(all[3])}: that gives {M(all[2])}.");
            note.Text = $"M₁V₁ = M₂V₂ · solved for {letters[k][0]}{(letters[k][1] == '1' ? "₁" : "₂")} · add {ChemCalc.Shown(V(Math.Max(0, all[3] - all[1])))} of solvent";
            plain = words.Text.Replace("−", "-");
            AutomationProperties.SetName(answer, $"{labels[k]}: {big}");
            ChemCalc.Shows("solution dilution", ref said, $"{letters[k]}={ChemNum.Sig(value, figures)} {(isVolume ? u : "M")}");
        });
        var (startV1, startUnit) = SolutionPages.Split("V1", new[] { "mL", "L" }, "mL");
        var (startV2, startUnit2) = SolutionPages.Split("V2", new[] { "mL", "L" }, "mL");
        unit = ChemCalc.Choice("solution", "dilution.unit", new[] { "mL", "L" }, "mL", later, startUnit ?? startUnit2, 92);
        boxes = new[]
        {
            ChemCalc.Field("solution", "M1", "?", later, SolutionPages.Split("M1", new[] { "M" }, "M").Value, labels[0]),
            ChemCalc.Field("solution", "V1", "?", later, startV1, labels[1]),
            ChemCalc.Field("solution", "M2", "?", later, SolutionPages.Split("M2", new[] { "M" }, "M").Value, labels[2]),
            ChemCalc.Field("solution", "V2", "?", later, startV2, labels[3]),
        };
        var copy = ChemCalc.Copy(() => plain);
        unit.Margin = new Thickness(8, 0, 0, 0);
        var volumes = K.Columns("*, auto", K.V(6, K.Text("Volumes in", Ty.BodyStrong, B.Text2), unit).Align(HorizontalAlignment.Left).Col(0));
        var inputs = K.V(12, ToolKit.Field(labels[0], boxes[0], suffix: "M"), ToolKit.Field(labels[1], boxes[1]), ToolKit.Field(labels[2], boxes[2], suffix: "M"), ToolKit.Field(labels[3], boxes[3]), volumes, under);
        var card = K.Card(K.V(10, K.Text("How much stock?", Ty.Caption, B.Text2), Everyday.Shrinks(answer), words, note, copy), new Thickness(20));
        Content = ToolKit.Columns(inputs, card, 380, 860);
        ChemCalc.CopyOnCtrlC(this, () => plain);
        later.Now();
    }
}

/// <summary>pH: the four from any one, and from a concentration; a colour strip with the marker.</summary>
internal sealed class PhPage : ContentControl
{
    public PhPage()
    {
        Focusable = false;
        var underOne = new ContentControl { Focusable = false };
        var underConc = new ContentControl { Focusable = false };
        var factsOne = new ContentControl { Focusable = false };
        var factsConc = new ContentControl { Focusable = false };
        var strip = new PhStrip { Height = 64 };
        var stripWords = K.Wrapped("", Ty.Body);
        string? plain = null;
        var said = "";
        var lastEdited = "one";
        TextBox value = null!, conc = null!, k = null!;
        ComboBox given = null!, kind = null!;
        FrameworkElement kField = null!;
        var later = new ChemCalc.Later(() =>
        {
            plain = null;
            // (from one value)
            var g = Array.IndexOf(SolutionPages.Givens, ChemCalc.Chosen(given, "pH"));
            var v = ChemNum.Read(value.Text);
            PhSet? one = v is { } vv ? ChemPh.From((PhGiven)Math.Max(0, g), vv) : null;
            ChemCalc.Say(underOne, value.Text.Trim().Length == 0 ? null : v == null ? "That needs to be a number (1e-3 is fine)." : one == null ? "A concentration must be above zero." : null, problem: true);
            factsOne.Content = one == null ? null : ChemKit.Facts(Rows(one, null));
            // (from a concentration)
            var kindAt = Array.IndexOf(SolutionPages.Kinds, ChemCalc.Chosen(kind, SolutionPages.Kinds[0]));
            var weak = kindAt >= 2;
            kField.Visibility = weak ? Visibility.Visible : Visibility.Collapsed;
            var c = ChemNum.Read(conc.Text);
            var kv = ChemNum.Read(k.Text);
            var ab = c is { } cc ? ChemPh.OfSolution((AcidBaseKind)Math.Max(0, kindAt), cc, kv) : null;
            ChemCalc.Say(underConc, conc.Text.Trim().Length == 0 ? null : c == null ? "The concentration needs to be a number." : c <= 0 ? "The concentration must be above zero." : weak && kv is not > 0 ? (kindAt == 2 ? "Give the acid’s Ka (1.8e-5 for acetic acid)." : "Give the base’s Kb (1.8e-5 for ammonia).") : null, problem: true);
            factsConc.Content = ab == null ? null : ChemKit.Facts(Rows(ab.Set, ab.PercentIonised));
            // (the strip: the last section edited, if it has a pH)
            var ph = lastEdited == "one" ? one?.PH ?? ab?.Set.PH : ab?.Set.PH ?? one?.PH;
            strip.Show(ph);
            stripWords.Text = ph is { } p ? $"pH {ChemNum.Fixed(p, 2)} · {SolutionPages.Describe(p)}" : "Type a value above to place the marker.";
            var set = lastEdited == "one" ? one ?? ab?.Set : ab?.Set ?? one;
            plain = set == null ? null : $"pH {ChemNum.Fixed(set.PH, 2)}, pOH {ChemNum.Fixed(set.POH, 2)}, [H+] {ChemNum.Sig(set.H)} M, [OH-] {ChemNum.Sig(set.OH)} M" + (ab?.PercentIonised is { } pi && lastEdited != "one" ? $", {ChemNum.Sig(pi, 3)} % ionised" : "");
            ChemCalc.Shows("solution ph", ref said, ph is { } p2 ? $"pH {ChemNum.Fixed(p2, 2)}" : "nothing");
        });
        // (the launch: pH=3, pOH=11, H=1e-3 or OH=1e-11 picks the kind of value given)
        var startGiven = new[] { ("pH", 0), ("pOH", 1), ("H", 2), ("OH", 3) }.FirstOrDefault(x => SolutionPages.Value(x.Item1) != null);
        given = ChemCalc.Choice("solution", "ph.given", SolutionPages.Givens, "pH", later, startGiven.Item1 != null ? SolutionPages.Givens[startGiven.Item2] : null, 92);
        value = ChemCalc.Field("solution", "ph.value", "3", later, startGiven.Item1 != null ? SolutionPages.Value(startGiven.Item1) : null, "The value given");
        var startKind = SolutionPages.Value("kind") is { } sk ? SolutionPages.Kinds.FirstOrDefault(x => x.StartsWith(sk, StringComparison.OrdinalIgnoreCase)) : null;
        kind = ChemCalc.Choice("solution", "ph.kind", SolutionPages.Kinds, SolutionPages.Kinds[0], later, startKind);
        conc = ChemCalc.Field("solution", "ph.conc", "0.10", later, SolutionPages.Value("conc"), "The concentration");
        k = ChemCalc.Field("solution", "ph.k", "1.8e-5", later, SolutionPages.Value("K"), "Ka or Kb");
        kField = ToolKit.Field("Ka or Kb", k);
        if (startKind != null || SolutionPages.Value("conc") != null) lastEdited = "conc";
        value.TextChanged += (_, _) => lastEdited = "one";
        given.SelectionChanged += (_, _) => lastEdited = "one";
        foreach (var b in new[] { conc, k }) b.TextChanged += (_, _) => lastEdited = "conc";
        kind.SelectionChanged += (_, _) => lastEdited = "conc";
        var copy = ChemCalc.Copy(() => plain);
        given.Margin = new Thickness(0, 0, 8, 0);
        var one = K.V(10, K.Text("From one value", Ty.BodyStrong), K.Columns("auto, *", given.Col(0), value.Col(1)), underOne, factsOne);
        var fromConc = K.V(10, K.Text("From a concentration", Ty.BodyStrong), kind, ToolKit.Field("Concentration", conc, suffix: "M"), kField, underConc, factsConc);
        var inputs = K.V(20, one, fromConc);
        var card = K.Card(K.V(10, strip, stripWords, K.Wrapped("Kw = 1.0 × 10⁻¹⁴ at 25 °C, the only temperature here.", Ty.Caption, B.Text2), copy), new Thickness(20));
        Content = ToolKit.Columns(inputs, card, 380, 860, sideFirst: true);
        ChemCalc.CopyOnCtrlC(this, () => plain);
        later.Now();
    }

    private static IEnumerable<(string, string, Color?)> Rows(PhSet s, double? percent)
    {
        yield return ("pH", ChemNum.Fixed(s.PH, 2), SolutionPages.Tint);
        yield return ("pOH", ChemNum.Fixed(s.POH, 2), null);
        yield return ("[H⁺]", ChemCalc.Shown(ChemNum.With(s.H, "M")), null);
        yield return ("[OH⁻]", ChemCalc.Shown(ChemNum.With(s.OH, "M")), null);
        if (percent is { } p) yield return ("Ionised", $"{ChemNum.Sig(p, 3)} %", null);
    }
}

/// <summary>The pH strip 0–14, red through green to purple, with a marker at the pH shown (moved over 200 ms, or at once
/// where Windows' animation effects are off).</summary>
internal sealed class PhStrip : FrameworkElement
{
    public static readonly DependencyProperty AtProperty = DependencyProperty.Register(nameof(At), typeof(double), typeof(PhStrip),
        new FrameworkPropertyMetadata(7.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(PhStrip),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MarkBrushProperty = DependencyProperty.Register(nameof(MarkBrush), typeof(Brush), typeof(PhStrip),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly LinearGradientBrush Colours = Make();
    private bool shown;

    public double At { get => (double)GetValue(AtProperty); set => SetValue(AtProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush MarkBrush { get => (Brush)GetValue(MarkBrushProperty); set => SetValue(MarkBrushProperty, value); }

    public PhStrip()
    {
        SetResourceReference(TextBrushProperty, B.Text2);
        SetResourceReference(MarkBrushProperty, B.Text);
        IsHitTestVisible = false;
        MinHeight = 48;
    }

    private static LinearGradientBrush Make()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        foreach (var (at, hex) in new[] { (0.0, "#d7263d"), (2.0, "#f06a1d"), (4.0, "#f2b705"), (6.0, "#9ac32c"), (7.0, "#2fa84f"), (8.0, "#2aa79b"), (10.0, "#2f7fe0"), (12.0, "#5b4fd6"), (14.0, "#7b2cbf") })
            b.GradientStops.Add(new GradientStop(K.Hex(hex), at / 14));
        b.Freeze();
        return b;
    }

    /// <summary>The pH to mark (null: no marker).</summary>
    public void Show(double? ph)
    {
        shown = ph != null;
        if (ph is { } p) Motion.To(this, AtProperty, Math.Clamp(p, 0, 14), 200, Motion.Decelerate);
        AutomationProperties.SetName(this, ph is { } q ? $"pH strip, marker at {ChemNum.Fixed(q, 2)}" : "pH strip from 0 to 14");
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        if (w < 40) return;
        var bar = new Rect(6, 18, w - 12, 16);
        dc.DrawRoundedRectangle(Colours, null, bar, 8, 8);
        var face = new Typeface(K.SmallFace, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var i = 0; i <= 14; i += 7)
        {
            var x = bar.Left + bar.Width * i / 14;
            var t = new FormattedText(i.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 11, TextBrush, dpi);
            dc.DrawText(t, new Point(Math.Clamp(x - t.Width / 2, 0, w - t.Width), bar.Bottom + 4));
        }
        if (!shown) return;
        var mx = bar.Left + bar.Width * Math.Clamp(At, 0, 14) / 14;
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(mx, bar.Top - 2), true, true);
            g.LineTo(new Point(mx - 7, bar.Top - 12), true, true);
            g.LineTo(new Point(mx + 7, bar.Top - 12), true, true);
        }
        tri.Freeze();
        dc.DrawGeometry(MarkBrush, null, tri);
        dc.DrawEllipse(Brushes.White, new Pen(MarkBrush, 2), new Point(mx, bar.Top + bar.Height / 2), 5, 5);
    }
}

/// <summary>Titration: the kind, the analyte and the titrant; the equivalence point, the pH along the way, the curve; a buffer.</summary>
internal sealed class TitrationPage : ContentControl
{
    public TitrationPage()
    {
        Focusable = false;
        var under = new ContentControl { Focusable = false };
        var facts = new ContentControl { Focusable = false };
        var plot = new LabPlot { Height = 260, XLabel = "Titrant added (mL)", YLabel = "pH" };
        var plotWords = K.Wrapped("", Ty.Caption, B.Text2);
        var bufferOut = K.Wrapped("", Ty.BodyStrong);
        var bufferUnder = new ContentControl { Focusable = false };
        string? plain = null;
        var said = "";
        TextBox analyte = null!, volume = null!, titrant = null!, k = null!, pKa = null!, baseConc = null!, acidConc = null!;
        ComboBox kind = null!;
        FrameworkElement kField = null!;
        var later = new ChemCalc.Later(() =>
        {
            plain = null;
            var kindAt = Math.Max(0, Array.IndexOf(SolutionPages.Titrations, ChemCalc.Chosen(kind, SolutionPages.Titrations[0])));
            var tk = (TitrationKind)kindAt;
            kField.Visibility = tk == TitrationKind.StrongAcidStrongBase ? Visibility.Collapsed : Visibility.Visible;
            var a = ChemNum.Read(analyte.Text);
            var v = ChemNum.Read(volume.Text);
            var t = ChemNum.Read(titrant.Text);
            var kv = ChemNum.Read(k.Text);
            var r = a is { } aa && v is { } vv && t is { } tt ? ChemTitration.Run(tk, aa, vv, tt, kv, out _) : null;
            string? message = null;
            if (r == null)
            {
                var typed = new[] { analyte, volume, titrant }.Any(b => b.Text.Trim().Length > 0);
                var unreadable = new[] { (analyte, "The analyte’s concentration"), (volume, "The analyte’s volume"), (titrant, "The titrant’s concentration") }.FirstOrDefault(x => x.Item1.Text.Trim().Length > 0 && ChemNum.Read(x.Item1.Text) == null);
                if (unreadable.Item1 != null) message = $"{unreadable.Item2} needs to be a number.";
                else if (a == null || v == null || t == null) message = typed ? "Fill in the analyte’s concentration and volume and the titrant’s concentration." : null;
                else ChemTitration.Run(tk, a.Value, v.Value, t.Value, kv, out message);
                ChemCalc.Say(under, message, problem: typed);
                facts.Content = null;
                plot.Clear();
                plot.XMin = 0; plot.XMax = 50; plot.YMin = 0; plot.YMax = 14;
                plot.Redraw();
                plotWords.Text = "The curve shows here.";
            }
            else
            {
                ChemCalc.Say(under, null);
                var figures = 3;
                var rows = new List<(string, string, Color?)>
                {
                    ("Equivalence volume", ChemNum.With(r.EquivalenceMl, "mL", figures), SolutionPages.Tint),
                    ("pH at start", ChemNum.Fixed(r.StartPh, 2), null),
                    (r.PK is { } pk ? (tk == TitrationKind.WeakAcidStrongBase ? "pH at half-equivalence (= pKa)" : "pH at half-equivalence (pKb " + ChemNum.Fixed(pk, 2) + ")") : "pH at half-equivalence", ChemNum.Fixed(r.HalfPh, 2), null),
                    ("pH at equivalence", ChemNum.Fixed(r.EquivalencePh, 2), null),
                    ($"pH at {ChemNum.Sig(2 * r.EquivalenceMl, figures)} mL (twice)", ChemNum.Fixed(r.AfterPh, 2), null),
                };
                facts.Content = ChemKit.Facts(rows);
                plot.Clear();
                plot.XMin = 0; plot.XMax = 2 * r.EquivalenceMl; plot.YMin = 0; plot.YMax = 14;
                plot.Guides.Add(new PlotGuide(7, true, "pH 7", K.WithAlpha(SolutionPages.Tint, 0.35)));
                plot.Lines.Add(new PlotLine(r.Curve.Select(p => (p.Ml, p.Ph)).ToList(), SolutionPages.Tint, Width: 2.4));
                plot.Marks.Add(new PlotMark(r.EquivalenceMl, r.EquivalencePh, SolutionPages.Tint, $"Equivalence · pH {ChemNum.Fixed(r.EquivalencePh, 2)}"));
                plot.Redraw();
                plotWords.Text = $"{SolutionPages.Titrations[kindAt]}: {ChemTitration.CurvePoints} points from 0 to {ChemNum.Sig(2 * r.EquivalenceMl, figures)} mL, each pH from the charge balance.";
                AutomationProperties.SetName(plot, $"Titration curve: equivalence at {ChemNum.Sig(r.EquivalenceMl, figures)} mL, pH {ChemNum.Fixed(r.EquivalencePh, 2)}");
                plain = string.Join("\n", rows.Select(x => $"{x.Item1}: {x.Item2}"));
            }
            // (the buffer)
            var pk2 = ChemNum.Read(pKa.Text);
            var bc = ChemNum.Read(baseConc.Text);
            var ac = ChemNum.Read(acidConc.Text);
            var buffer = pk2 is { } p2 && bc is { } b2 && ac is { } a2 ? ChemPh.Buffer(p2, b2, a2) : null;
            var bufferTyped = new[] { pKa, baseConc, acidConc }.Any(b => b.Text.Trim().Length > 0);
            ChemCalc.Say(bufferUnder, buffer == null && bufferTyped ? (pk2 == null || bc == null || ac == null ? "Fill in pKa, [A⁻] and [HA]." : "Both concentrations must be above zero.") : null, problem: true);
            bufferOut.Text = buffer is { } bp ? ChemCalc.Shown($"pH = {ChemNum.Fixed(pk2!.Value, 2)} + log({ChemNum.Sig(bc!.Value)} / {ChemNum.Sig(ac!.Value)}) = {ChemNum.Fixed(bp, 2)}") : "pH = pKa + log([A⁻] / [HA])";
            ChemCalc.Shows("solution titration", ref said, r == null ? "nothing" : $"eq {ChemNum.Sig(r.EquivalenceMl, 3)} mL pH {ChemNum.Fixed(r.EquivalencePh, 2)}");
        });
        var startKind = SolutionPages.Value("titration") is { } sk ? SolutionPages.Titrations.FirstOrDefault(x => x.StartsWith(sk, StringComparison.OrdinalIgnoreCase)) : null;
        kind = ChemCalc.Choice("solution", "titration.kind", SolutionPages.Titrations, SolutionPages.Titrations[0], later, startKind);
        analyte = ChemCalc.Field("solution", "titration.analyte", "0.1", later, SolutionPages.Split("analyte", new[] { "M" }, "M").Value, "The analyte’s concentration");
        volume = ChemCalc.Field("solution", "titration.volume", "25", later, SolutionPages.Split("volume", new[] { "mL" }, "mL").Value, "The analyte’s volume");
        titrant = ChemCalc.Field("solution", "titration.titrant", "0.1", later, SolutionPages.Split("titrant", new[] { "M" }, "M").Value, "The titrant’s concentration");
        k = ChemCalc.Field("solution", "titration.k", "1.8e-5", later, SolutionPages.Value("K"), "Ka or Kb");
        kField = ToolKit.Field("Ka of the acid, or Kb of the base", k);
        pKa = ChemCalc.Field("solution", "buffer.pka", "4.74", later, SolutionPages.Value("pKa"), "pKa");
        baseConc = ChemCalc.Field("solution", "buffer.base", "0.1", later, SolutionPages.Value("base"), "The base’s concentration");
        acidConc = ChemCalc.Field("solution", "buffer.acid", "0.1", later, SolutionPages.Value("acid"), "The acid’s concentration");
        var copy = ChemCalc.Copy(() => plain);
        var inputs = K.V(12, K.V(6, K.Text("Titration", Ty.BodyStrong, B.Text2), kind), ToolKit.Field("Analyte concentration", analyte, suffix: "M"), ToolKit.Field("Analyte volume", volume, suffix: "mL"),
            ToolKit.Field("Titrant concentration", titrant, suffix: "M"), kField, under);
        var bufferRow = K.Columns("*, 8, *, 8, *", ToolKit.Field("pKa", pKa).Col(0), ToolKit.Field("[A⁻]", baseConc, suffix: "M").Col(2), ToolKit.Field("[HA]", acidConc, suffix: "M").Col(4));
        var buffer = K.V(10, K.Text("Buffer · Henderson–Hasselbalch", Ty.BodyStrong), bufferRow, bufferUnder, bufferOut);
        var side = K.V(16, facts, K.Card(K.V(8, plot, plotWords), new Thickness(12)), copy);
        Content = K.V(24, ToolKit.Columns(inputs, side, 460, 860), buffer);
        ChemCalc.CopyOnCtrlC(this, () => plain);
        later.Now();
    }
}

/// <summary>The tool in its pin's flyout: the pH tab compact — one value, the other three.</summary>
internal sealed class SolutionQuick : ContentControl
{
    public SolutionQuick(ToolContext c)
    {
        Focusable = false;
        var facts = new ContentControl { Focusable = false };
        var under = new ContentControl { Focusable = false, Visibility = Visibility.Collapsed };
        TextBox value = null!;
        ComboBox given = null!;
        string? plain = null;
        var later = new ChemCalc.Later(() =>
        {
            var g = Math.Max(0, Array.IndexOf(SolutionPages.Givens, ChemCalc.Chosen(given, "pH")));
            var v = ChemNum.Read(value.Text);
            var s = v is { } vv ? ChemPh.From((PhGiven)g, vv) : null;
            ChemCalc.Say(under, value.Text.Trim().Length == 0 ? null : v == null ? "That needs to be a number." : s == null ? "A concentration must be above zero." : null, problem: true);
            facts.Content = s == null ? K.Wrapped("Type one; the other three follow.", Ty.Caption, B.Text2)
                : ChemKit.Facts(new (string, string, Color?)[] { ("pH", ChemNum.Fixed(s.PH, 2), SolutionPages.Tint), ("pOH", ChemNum.Fixed(s.POH, 2), null), ("[H⁺]", ChemCalc.Shown(ChemNum.With(s.H, "M")), null), ("[OH⁻]", ChemCalc.Shown(ChemNum.With(s.OH, "M")), null) });
            plain = s == null ? null : $"pH {ChemNum.Fixed(s.PH, 2)}, pOH {ChemNum.Fixed(s.POH, 2)}, [H+] {ChemNum.Sig(s.H)} M, [OH-] {ChemNum.Sig(s.OH)} M";
        });
        var startGiven = new[] { ("pH", 0), ("pOH", 1), ("H", 2), ("OH", 3) }.FirstOrDefault(x => SolutionPages.Value(x.Item1) != null);
        given = ChemCalc.Choice("solution", "ph.given", SolutionPages.Givens, "pH", later, startGiven.Item1 != null ? SolutionPages.Givens[startGiven.Item2] : null, 92);
        value = ChemCalc.Field("solution", "ph.value", "3", later, startGiven.Item1 != null ? SolutionPages.Value(startGiven.Item1) : null, "The value given");
        given.Margin = new Thickness(0, 0, 8, 0);
        var open = BioUI.OpenButton("Open Solutions & pH", () => c.OpenFull(new Dictionary<string, string> { ["carry"] = "pin" }, null), "Open the whole tool");
        c.Carry = () => value.Text.Trim().Length > 0 ? new Dictionary<string, string> { ["carry"] = "pin" } : null;
        ChemCalc.CopyOnCtrlC(this, () => plain);
        Content = K.V(10, K.Columns("auto, *", given.Col(0), value.Col(1)), under, facts, open);
        later.Now();
    }
}

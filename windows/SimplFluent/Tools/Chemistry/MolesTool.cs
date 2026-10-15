using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Molar Mass & Stoichiometry (the Mac's MolarMassTool): four tabs — a formula's molar mass with its breakdown by element
// and percent composition; an amount converted between grams, moles, particles and litres at STP; a reaction's limiting
// reagent, yields, what is left and the percent yield; and an empirical formula from mass percents, with the molecular
// one from a molar mass. The sums are in Tools/Chemistry/Model (ChemMolarMass, ChemStoich, on the balancer).

#pragma warning disable CA2255
internal static class MolesRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("moles", c => new MolesTool(c), c => new MolesQuick(c));
}
#pragma warning restore CA2255

/// <summary>The tool's tabs, and what a launch asks of it (--tool moles --tool-state reaction, or --moles reaction; then
/// --moles-formula, --moles-amount "10 g", --moles-equation, --moles-given "H2=4g,O2=16g", --moles-actual "H2O=15g",
/// --moles-empirical "C=40,H=6.71,O=53.3", --moles-mass 180).</summary>
internal static class MolesPages
{
    public static readonly string[] Keys = { "molar", "convert", "reaction", "empirical" };
    public static readonly string[] Names = { "Molar mass", "Convert", "Reaction", "Empirical" };
    public static readonly string[] AmountUnits = { "grams", "moles", "molecules", "litres at STP" };
    public static readonly string[] GivenUnits = { "g", "mol" };

    public static string? AskedTab()
    {
        var t = (ChemTools.LaunchState("moles") ?? ChemCalc.Flag("moles"))?.Trim().ToLowerInvariant();
        return t != null && Keys.Contains(t) ? t : null;
    }
}

/// <summary>The whole tool: its tabs across the top, the page chosen under them.</summary>
internal sealed class MolesTool : ContentControl
{
    private readonly ContentControl host = new() { Focusable = false };
    private readonly FrameworkElement?[] pages = new FrameworkElement?[4];
    private readonly string? carriedFormula;
    private string said = "";

    public MolesTool(ToolContext c)
    {
        Focusable = false;
        // (a pin's flyout opened full screen: its formula, on the Molar mass tab)
        carriedFormula = c.TakeInfo("formula");
        var asked = MolesPages.AskedTab() ?? (carriedFormula != null ? "molar" : null);
        var tabs = ChemCalc.Tabs("moles", MolesPages.Names, MolesPages.Keys, asked, Show);
        Content = ToolKit.Page(K.V(16, tabs, host));
        Show(tabs.Selected);
    }

    private void Show(int i)
    {
        pages[i] ??= i switch { 0 => new MolarPage(carriedFormula), 1 => new ConvertPage(), 2 => new ReactionPage(), _ => new EmpiricalPage() };
        host.Content = pages[i];
        ChemCalc.Shows("moles", ref said, MolesPages.Keys[i]);
    }
}

/// <summary>Molar mass: a formula, its mass large with its breakdown by element and each one's share.</summary>
internal sealed class MolarPage : ContentControl
{
    public MolarPage(string? carried)
    {
        Focusable = false;
        var under = new ContentControl { Focusable = false };
        var shownFormula = new ContentControl { Focusable = false };
        var mass = Everyday.Figures("—", 40, FontWeights.Bold);
        var table = new ContentControl { Focusable = false };
        MolarMassResult? shown = null;
        TextBox field = null!;
        var copy = ChemCalc.Copy(() => shown == null ? null : $"{shown.Formula.Text}: {ChemNum.Fixed(shown.Total, 3)} g/mol");
        var said = "";
        var later = new ChemCalc.Later(() =>
        {
            shown = field.Text.Trim().Length == 0 ? null : ChemMolarMass.Of(field.Text, out var bad);
            if (shown == null)
            {
                ChemCalc.Say(under, field.Text.Trim().Length == 0 ? null : $"Can’t read “{(string.IsNullOrEmpty(ChemFormulaBad(field.Text)) ? field.Text.Trim() : ChemFormulaBad(field.Text))}” — is it a real formula?", problem: true);
                shownFormula.Content = null;
                mass.Text = "—";
                table.Content = K.Card(K.Empty(SymbolRegular.TableSimple24, "Each element’s count, mass and share show here."), new Thickness(6));
            }
            else
            {
                ChemCalc.Say(under, null);
                shownFormula.Content = ChemCalc.Formula(shown.Formula.Runs, 22);
                mass.Text = $"{ChemNum.Fixed(shown.Total, 3)} g/mol";
                table.Content = ChemCalc.Table(new[] { "Element", "Count", "Atomic mass", "Subtotal", "Mass %" },
                    shown.Rows.Select(r => new object?[] { $"{r.Symbol} · {r.Name}", r.Count, ChemNum.Fixed(r.AtomicMass, 3), ChemNum.Fixed(r.Subtotal, 3), ChemNum.Fixed(r.Percent, 2) }),
                    new[] { false, true, true, true, true });
            }
            copy.IsEnabled = shown != null;
            AutomationProperties.SetName(mass, shown == null ? "No molar mass yet" : $"{shown.Formula.Text}: {mass.Text}");
            ChemCalc.Shows("moles molar", ref said, shown == null ? "nothing" : $"{shown.Formula.Text} {ChemNum.Fixed(shown.Total, 3)}");
        });
        field = ChemCalc.Field("moles", "formula", "Ca(OH)2", later, ChemCalc.Flag("moles-formula") ?? carried, "The formula");
        field.FontSize = 16;
        var chips = ChemCalc.Chips(new[] { "Ca(OH)2", "C6H12O6", "CuSO4·5H2O" }.Select(e => (e, (Action)(() => { field.Text = e; later.Now(); }))), "Try");
        var main = K.V(12, ToolKit.Field("Formula", field), ChemCalc.Hint("Brackets, hydrates (CuSO4·5H2O) and charges are fine; a charge is ignored."), chips, under, K.V(10, K.Text("Breakdown", Ty.BodyStrong), table));
        var card = K.Card(K.V(10, shownFormula, Everyday.Shrinks(mass), K.Text("Molar mass", Ty.Caption, B.Text2), copy), new Thickness(20));
        Content = ToolKit.Columns(main, card, 340, 860, sideFirst: true);
        ChemCalc.CopyOnCtrlC(this, () => shown == null ? null : $"{shown.Formula.Text}: {ChemNum.Fixed(shown.Total, 3)} g/mol");
        later.Now();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { if (!IsKeyboardFocusWithin) field.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>What a formula's reader could not read.</summary>
    public static string? ChemFormulaBad(string typed)
    {
        ChemFormula.Parse(typed, out var bad);
        return bad;
    }
}

/// <summary>Convert: a formula and one amount, the other three worked out.</summary>
internal sealed class ConvertPage : ContentControl
{
    public ConvertPage()
    {
        Focusable = false;
        var under = new ContentControl { Focusable = false };
        var results = new ContentControl { Focusable = false };
        var line = K.Wrapped("", Ty.Caption, B.Text2);
        string? plain = null;
        var said = "";
        TextBox formula = null!, amount = null!;
        ComboBox unit = null!;
        var later = new ChemCalc.Later(() =>
        {
            plain = null;
            var f = ChemFormula.Parse(formula.Text, out var bad);
            var v = ChemNum.Read(amount.Text);
            if (f == null) ChemCalc.Say(under, formula.Text.Trim().Length == 0 ? "Type a formula and an amount." : $"Can’t read “{(string.IsNullOrEmpty(bad) ? formula.Text.Trim() : bad)}” — is it a real formula?", problem: formula.Text.Trim().Length > 0);
            else if (v == null) ChemCalc.Say(under, amount.Text.Trim().Length == 0 ? "Type an amount." : "The amount needs to be a number.", problem: amount.Text.Trim().Length > 0);
            else if (v < 0) ChemCalc.Say(under, "The amount can’t be below zero.", problem: true);
            else ChemCalc.Say(under, null);
            if (f == null || v == null || v < 0)
            {
                results.Content = K.Card(K.Empty(SymbolRegular.ArrowSwap24, "Grams, moles, molecules and litres at STP show here."), new Thickness(6));
                line.Text = "";
                ChemCalc.Shows("moles convert", ref said, "nothing");
                return;
            }
            var kind = (AmountKind)Array.IndexOf(MolesPages.AmountUnits, ChemCalc.Chosen(unit, "grams"));
            var r = ChemStoich.Convert(f, v.Value, kind);
            var figures = ChemNum.FiguresOf(amount.Text);
            var particles = f.Atoms.Count == 1 && f.AtomCount == 1 ? "atoms" : f.Charge != 0 || f.Atoms.Any(a => ChemElements.Of(a.Symbol)?.Block is "s" or "d" or "f") ? "formula units" : "molecules";
            var rows = new (string Name, string Value, AmountKind Kind)[]
            {
                ("Grams", ChemNum.With(r.Grams, "g", figures), AmountKind.Grams),
                ("Moles", ChemNum.With(r.Moles, "mol", figures), AmountKind.Moles),
                (char.ToUpperInvariant(particles[0]) + particles[1..], ChemNum.With(r.Particles, particles, figures), AmountKind.Particles),
                ("Litres of gas at STP", ChemNum.With(r.Litres, "L", figures), AmountKind.LitresStp),
            };
            var v2 = K.V(0);
            foreach (var row in rows)
            {
                if (v2.Children.Count > 0) v2.Children.Add(K.Divider(14));
                var given = row.Kind == kind;
                var cell = ChemCalc.Result(given ? $"{row.Name} (given)" : row.Name, ChemCalc.Shown(row.Value), given ? null : K.Hex("#2fb37a"));
                cell.Margin = new Thickness(14, 9, 14, 9);
                v2.Children.Add(cell);
            }
            results.Content = K.Card(v2, new Thickness(0));
            line.Text = $"Molar mass {ChemNum.Fixed(r.MolarMass, 3)} g/mol · 22.4 L/mol at STP · 6.02214076e23 per mol";
            plain = $"{ChemNum.Sig(v.Value, figures)} {MolesPages.AmountUnits[(int)kind]} {f.Text} = " + string.Join(" = ", rows.Where(x => x.Kind != kind).Select(x => x.Value));
            ChemCalc.Shows("moles convert", ref said, $"{f.Text} {ChemNum.Sig(v.Value)} {MolesPages.AmountUnits[(int)kind]}");
        });
        var (startAmount, startUnit) = ChemCalc.Flag("moles-amount") is { } a ? ChemCalc.Split(a, MolesPages.AmountUnits, "grams") : (null, null);
        formula = ChemCalc.Field("moles", "convert.formula", "H2O", later, ChemCalc.Flag("moles-formula"), "The formula");
        amount = ChemCalc.Field("moles", "convert.amount", "10", later, startAmount, "The amount");
        unit = ChemCalc.Choice("moles", "convert.unit", MolesPages.AmountUnits, "grams", later, startUnit);
        var copy = ChemCalc.Copy(() => plain);
        var inputs = K.V(12, ToolKit.Field("Formula", formula), ChemCalc.WithUnit("Amount", amount, unit), under);
        var side = K.V(10, K.Text("Every way", Ty.BodyStrong), results, line, copy);
        Content = ToolKit.Columns(inputs, side, 380, 860, sideFirst: true);
        ChemCalc.CopyOnCtrlC(this, () => plain);
        later.Now();
    }
}

/// <summary>Reaction: a balanced equation, what was given of each reactant, and the limiting reagent, yields and leftovers.</summary>
internal sealed class ReactionPage : ContentControl
{
    private readonly Dictionary<string, (string Value, string Unit)> given = new(StringComparer.Ordinal);
    private readonly ContentControl under = new() { Focusable = false }, reactants = new() { Focusable = false }, products = new() { Focusable = false }, summary = new() { Focusable = false };
    private readonly ComboBox actualOf = new() { MinWidth = 0, Width = 120 };
    private readonly TextBox equation, actual;
    private readonly ChemCalc.Later later;
    private readonly List<(ChemFormula Formula, TextBlock Words, TextBlock Mark)> reactantRows = new();
    private readonly List<(ChemFormula Formula, TextBlock Words)> productRows = new();
    private string built = "";
    private bool building;
    private string said = "";
    private string? plain;

    public ReactionPage()
    {
        Focusable = false;
        later = new ChemCalc.Later(Update);
        equation = ChemCalc.Field("moles", "equation", "2H2 + O2 → 2H2O", later, ChemCalc.Flag("moles-equation"), "The equation");
        equation.FontSize = 16;
        // (what was given, kept as "H2=4 g,O2=16 g")
        foreach (var (k, v) in ChemCalc.Pairs(ChemCalc.Flag("moles-given") ?? ChemPrefs.String("moles", "given")))
            given[k] = ChemCalc.Split(v, MolesPages.GivenUnits, "g");
        var (actualSpecies, actualValue) = ChemCalc.Pairs(ChemCalc.Flag("moles-actual") ?? ChemPrefs.String("moles", "actual")).FirstOrDefault() is { Key: { } ks } kv ? (ks, ChemCalc.Split(kv.Value, MolesPages.GivenUnits, "g").Value) : (null, null);
        actual = ToolKit.Box(actualValue ?? "", "0");
        actual.TextChanged += (_, _) => { Remember(); later.Ask(); };
        actual.PreviewKeyDown += (_, a) => { if (a.Key == System.Windows.Input.Key.Enter) { later.Now(); a.Handled = true; } };
        AutomationProperties.SetName(actual, "Actual yield in grams");
        actualOf.Tag = actualSpecies;
        actualOf.SelectionChanged += (_, _) => { Remember(); later.Now(); };
        AutomationProperties.SetName(actualOf, "The product weighed");
        var copy = ChemCalc.Copy(() => plain);
        var yieldRow = K.Columns("auto, auto, auto, auto", K.Text("Actual yield of", Ty.Body, B.Text2).Margin(0, 0, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0),
            actualOf.Col(1), actual.Margin(8, 0, 0, 0).Col(2), K.Text("g", Ty.Body, B.Text2).Margin(8, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(3));
        actual.Width = 90;
        var main = K.V(12, ToolKit.Field("Balanced equation", equation), under, K.V(10, K.Text("Reactants · what you have", Ty.BodyStrong), reactants), K.V(10, K.Text("Products · theoretical yield", Ty.BodyStrong), products), yieldRow);
        var side = K.V(10, K.Text("Result", Ty.BodyStrong), summary, copy);
        Content = ToolKit.Columns(main, side, 340, 860);
        ChemCalc.CopyOnCtrlC(this, () => plain);
        Update();
    }

    private void Remember()
    {
        ChemPrefs.Set("moles", "given", string.Join(",", given.Where(g => g.Value.Value.Trim().Length > 0).Select(g => $"{g.Key}={g.Value.Value.Trim()} {g.Value.Unit}")));
        var of = Everyday.Chosen(actualOf);
        ChemPrefs.Set("moles", "actual", of != null && actual.Text.Trim().Length > 0 ? $"{of}={actual.Text.Trim()} g" : null);
    }

    private void Update()
    {
        // (Build chooses the product weighed, and the box's SelectionChanged asks for this very update: the one under
        // way finishes with its rows instead)
        if (building) return;
        plain = null;
        var r = ChemBalancer.Balance(equation.Text);
        if (!r.Ok)
        {
            ChemCalc.Say(under, r.Outcome == BalanceOutcome.Empty ? "Type the reaction; it is balanced for you." : r.Message, problem: r.Outcome != BalanceOutcome.Empty);
            built = "";
            reactantRows.Clear();
            productRows.Clear();
            reactants.Content = K.Card(K.Empty(SymbolRegular.Beaker24, "Each reactant and what you have of it show here."), new Thickness(6));
            products.Content = null;
            summary.Content = K.Card(K.Empty(SymbolRegular.Scales24, "The limiting reagent and the yields show here."), new Thickness(6));
            ChemCalc.Shows("moles reaction", ref said, "nothing");
            return;
        }
        // ("balanced for you" when the coefficients typed were not these)
        var typed = r.Species.Select(f => f.Coefficient).ToList();
        var changed = typed.Where((c, i) => c != r.Coefficients[i]).Any();
        ChemCalc.Say(under, changed ? $"Balanced for you: {r.PrettyText}" : null);
        var signature = string.Join("|", r.Species.Select((f, i) => f.Text + "×" + r.Coefficients[i]));
        if (signature != built) Build(r);
        var amounts = r.Reactants.Select(f => given.TryGetValue(f.Text, out var g) ? new GivenAmount(ChemNum.Read(g.Value), g.Unit == "g") : new GivenAmount(null, true)).ToList();
        var actualIndex = Everyday.Chosen(actualOf) is { } of ? r.Products.ToList().FindIndex(f => f.Text == of) : -1;
        var result = ChemStoich.React(r, amounts, actualIndex >= 0 ? actualIndex : null, ChemNum.Read(actual.Text));
        foreach (var (f, words, mark) in reactantRows)
        {
            var row = result.Rows.First(x => x.Reactant && x.Formula.Text == f.Text);
            if (row.GivenMoles is { } gm)
            {
                words.Text = ChemCalc.Shown($"{ChemNum.Sig(gm)} mol given ({ChemNum.Sig(row.GivenGrams!.Value)} g) · uses {ChemNum.Sig(row.UsedOrMadeMoles)} mol · {ChemNum.Sig(row.LeftMoles!.Value)} mol ({ChemNum.Sig(row.LeftGrams!.Value)} g) left");
                mark.Text = row.Limiting ? "Limiting" : "In excess";
                mark.Foreground = new System.Windows.Media.SolidColorBrush(K.Darkened(row.Limiting ? ChemKit.Orange : ChemKit.Green));
            }
            else
            {
                words.Text = result.Message ?? "Nothing given: taken to be in excess.";
                mark.Text = "";
            }
        }
        foreach (var (f, words) in productRows)
        {
            var row = result.Rows.First(x => !x.Reactant && x.Formula.Text == f.Text);
            words.Text = result.Limiting == null ? "—" : ChemCalc.Shown($"{ChemNum.Sig(row.UsedOrMadeMoles)} mol · {ChemNum.Sig(row.UsedOrMadeGrams)} g");
        }
        var lines = new List<(string, string, System.Windows.Media.Color?)>();
        if (result.Limiting is { } lim)
        {
            var limRow = result.Rows.First(x => x.Limiting);
            lines.Add(("Limiting reagent", $"{lim.Text} ({ChemNum.Sig(limRow.GivenMoles!.Value)} mol)", ChemKit.Orange));
            foreach (var row in result.Products) lines.Add(($"{row.Formula.Text} yield", ChemCalc.Shown($"{ChemNum.Sig(row.UsedOrMadeGrams)} g · {ChemNum.Sig(row.UsedOrMadeMoles)} mol"), null));
            foreach (var row in result.Reactants.Where(x => x.LeftMoles is > 0)) lines.Add(($"{row.Formula.Text} left", ChemCalc.Shown($"{ChemNum.Sig(row.LeftGrams!.Value)} g · {ChemNum.Sig(row.LeftMoles!.Value)} mol"), null));
            if (result.PercentYield is { } py) lines.Add(("Percent yield", $"{ChemNum.Sig(py, 3)} %", py > 100 ? ChemKit.Red : ChemKit.Green));
            plain = $"{r.PlainText}\n" + string.Join("\n", lines.Select(l => $"{l.Item1}: {l.Item2}"));
            summary.Content = ChemKit.Facts(lines);
        }
        else summary.Content = K.Card(K.Empty(SymbolRegular.Scales24, result.Message ?? "Give what you have of a reactant."), new Thickness(6));
        ChemCalc.Shows("moles reaction", ref said, result.Limiting == null ? $"{r.PlainText} (nothing given)" : $"{r.PlainText} limiting {result.Limiting.Text}" + (result.PercentYield is { } p ? $" yield {ChemNum.Sig(p, 3)}%" : ""));
    }

    /// <summary>The rows for this equation's species: a reactant's formula, its given box and unit, and the line under
    /// them; a product's formula and its yield line.</summary>
    private void Build(BalanceResult r)
    {
        building = true;
        try { BuildRows(r); } finally { building = false; }
    }

    private void BuildRows(BalanceResult r)
    {
        built = string.Join("|", r.Species.Select((f, i) => f.Text + "×" + r.Coefficients[i]));
        reactantRows.Clear();
        productRows.Clear();
        var rv = K.V(0);
        for (var i = 0; i < r.Reactants.Count; i++)
        {
            var f = r.Reactants[i];
            if (rv.Children.Count > 0) rv.Children.Add(K.Divider(14));
            var name = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
            name.Fg(B.Text);
            if (r.Coefficients[i] != 1) name.Inlines.Add(new Run(r.Coefficients[i].ToString(CultureInfo.InvariantCulture)) { FontWeight = FontWeights.Bold });
            ChemCalc.AddFormula(name.Inlines, f.Runs, 16, FontWeights.Normal);
            name.VerticalAlignment = VerticalAlignment.Center;
            var key = f.Text;
            var have = given.TryGetValue(key, out var g) ? g : ("", "g");
            var box = ToolKit.Box(have.Item1, "0");
            box.Width = 90;
            AutomationProperties.SetName(box, $"Amount of {f.Text}");
            var unit = new ComboBox { MinWidth = 0, Width = 72, Margin = new Thickness(8, 0, 0, 0) };
            foreach (var u in MolesPages.GivenUnits) unit.Items.Add(Everyday.Item(u, u));
            Everyday.Choose(unit, have.Item2);
            AutomationProperties.SetName(unit, $"Unit of {f.Text}");
            box.TextChanged += (_, _) => { given[key] = (box.Text, ChemCalc.Chosen(unit, "g")); Remember(); later.Ask(); };
            box.PreviewKeyDown += (_, a) => { if (a.Key == System.Windows.Input.Key.Enter) { later.Now(); a.Handled = true; } };
            unit.SelectionChanged += (_, _) => { given[key] = (box.Text, ChemCalc.Chosen(unit, "g")); Remember(); later.Now(); };
            var mark = K.Text("", Ty.Caption);
            mark.FontWeight = FontWeights.SemiBold;
            mark.VerticalAlignment = VerticalAlignment.Center;
            var words = K.Wrapped("", Ty.Caption, B.Text2);
            var top = K.Columns("*, auto, auto, auto", name.Col(0), box.Col(1), unit.Col(2), mark.Margin(12, 0, 0, 0).Col(3));
            var row = K.V(4, top, words);
            row.Margin = new Thickness(14, 9, 14, 9);
            rv.Children.Add(row);
            reactantRows.Add((f, words, mark));
        }
        reactants.Content = K.Card(rv, new Thickness(0));
        var pv = K.V(0);
        actualOf.Items.Clear();
        for (var i = 0; i < r.Products.Count; i++)
        {
            var f = r.Products[i];
            var c = r.Coefficients[r.Reactants.Count + i];
            if (pv.Children.Count > 0) pv.Children.Add(K.Divider(14));
            var name = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
            name.Fg(B.Text);
            if (c != 1) name.Inlines.Add(new Run(c.ToString(CultureInfo.InvariantCulture)) { FontWeight = FontWeights.Bold });
            ChemCalc.AddFormula(name.Inlines, f.Runs, 16, FontWeights.Normal);
            var words = K.Text("", Ty.BodyStrong);
            words.TextAlignment = TextAlignment.Right;
            Typography.SetNumeralAlignment(words, FontNumeralAlignment.Tabular);
            var row = K.Columns("*, auto", name.Col(0), words.Margin(12, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
            row.Margin = new Thickness(14, 9, 14, 9);
            pv.Children.Add(row);
            productRows.Add((f, words));
            actualOf.Items.Add(Everyday.Item(f.Text, f.Text));
        }
        products.Content = K.Card(pv, new Thickness(0));
        // (the product weighed: the one remembered, else the first)
        var want = actualOf.Tag as string ?? Everyday.Chosen(actualOf);
        Everyday.Choose(actualOf, r.Products.Any(f => f.Text == want) ? want : r.Products[0].Text);
        actualOf.Tag = null;
    }
}

/// <summary>Empirical: each element's mass percent (or grams), the empirical formula, and the molecular one from a molar mass.</summary>
internal sealed class EmpiricalPage : ContentControl
{
    private const int RowCount = 6;

    public EmpiricalPage()
    {
        Focusable = false;
        var under = new ContentControl { Focusable = false };
        var table = new ContentControl { Focusable = false };
        var formula = new ContentControl { Focusable = false, MinHeight = 40 };
        var molecular = new ContentControl { Focusable = false };
        var note = K.Wrapped("", Ty.Caption, B.Text2);
        var symbols = new TextBox[RowCount];
        var amounts = new TextBox[RowCount];
        TextBox mass = null!;
        string? plain = null;
        var said = "";
        var later = new ChemCalc.Later(() =>
        {
            plain = null;
            ChemPrefs.Set("moles", "empirical", string.Join(",", Enumerable.Range(0, RowCount).Where(i => symbols[i].Text.Trim().Length > 0 || amounts[i].Text.Trim().Length > 0).Select(i => $"{symbols[i].Text.Trim()}={amounts[i].Text.Trim()}")));
            var parts = new List<(string, double)>();
            string? problem = null;
            for (var i = 0; i < RowCount; i++)
            {
                var s = symbols[i].Text.Trim();
                var a = amounts[i].Text.Trim();
                if (s.Length == 0 && a.Length == 0) continue;
                if (ChemNum.Read(a) is not { } v) { problem ??= s.Length > 0 ? $"{ChemStoich.Capitalised(s)} needs its amount." : "An amount needs its element."; continue; }
                parts.Add((s, v));
            }
            var mm = ChemNum.Read(mass.Text);
            var r = parts.Count == 0 ? null : ChemStoich.Empirical(parts, mm, out var bad2);
            string? bad = null;
            if (parts.Count > 0 && r == null) { ChemStoich.Empirical(parts, mm, out bad); }
            ChemCalc.Say(under, problem ?? bad ?? (parts.Count == 0 ? "Type each element and its mass percent (or grams)." : null), problem: problem != null || bad != null);
            if (r == null)
            {
                formula.Content = K.Wrapped("—", Ty.BodyLarge, B.Text2);
                molecular.Content = null;
                note.Text = "";
                table.Content = K.Card(K.Empty(SymbolRegular.TableSimple24, "Each element’s moles and ratio show here."), new Thickness(6));
                ChemCalc.Shows("moles empirical", ref said, "nothing");
                return;
            }
            var ef = ChemFormula.Parse(r.Formula)!;
            formula.Content = K.V(4, K.Text("Empirical formula", Ty.Caption, B.Text2), ChemCalc.Formula(ef.Runs, 36, FontWeights.Bold));
            molecular.Content = r.Molecular is { } mol
                ? K.V(4, K.Text($"Molecular formula · {r.MolecularMultiplier}× the empirical", Ty.Caption, B.Text2), ChemCalc.Formula(ChemFormula.Parse(mol)!.Runs, 28, FontWeights.SemiBold))
                : K.Wrapped("Add the molar mass for the molecular formula.", Ty.Caption, B.Text2);
            note.Text = $"Empirical mass {ChemNum.Fixed(r.FormulaMass, 2)} g/mol" + (r.Multiplier > 1 ? $" · ratios ×{r.Multiplier} to come out whole" : "") + (r.Approximate ? " · rounded as best it could be — check the numbers" : "");
            table.Content = ChemCalc.Table(new[] { "Element", "Amount", "Moles", "Ratio", "Whole" },
                r.Rows.Select(x => new object?[] { $"{x.Symbol} · {ChemElements.Of(x.Symbol)?.Name}", ChemNum.Sig(x.Amount), ChemNum.Sig(x.Moles), ChemNum.Fixed(x.Ratio, 3), x.Whole }),
                new[] { false, true, true, true, true });
            plain = $"Empirical formula {r.Formula}" + (r.Molecular is { } m2 ? $" · molecular formula {m2}" : "");
            ChemCalc.Shows("moles empirical", ref said, r.Formula + (r.Molecular is { } m3 ? " " + m3 : ""));
        });
        var start = ChemCalc.Pairs(ChemCalc.Flag("moles-empirical") ?? ChemPrefs.String("moles", "empirical")).ToList();
        var grid = K.V(8);
        grid.Children.Add(K.Columns("80, *", K.Text("Element", Ty.Caption, B.Text2).Col(0), K.Text("Mass % (or grams)", Ty.Caption, B.Text2).Margin(8, 0, 0, 0).Col(1)));
        for (var i = 0; i < RowCount; i++)
        {
            var s = ToolKit.Box(i < start.Count ? start[i].Key : "", i == 0 ? "C" : "");
            var a = ToolKit.Box(i < start.Count ? start[i].Value : "", i == 0 ? "40.0" : "");
            s.MaxLength = 2;
            AutomationProperties.SetName(s, $"Element {i + 1}");
            AutomationProperties.SetName(a, $"Amount of element {i + 1}");
            foreach (var b in new[] { s, a })
            {
                b.TextChanged += (_, _) => later.Ask();
                b.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { later.Now(); e.Handled = true; } };
            }
            symbols[i] = s;
            amounts[i] = a;
            grid.Children.Add(K.Columns("80, *", s.Col(0), a.Margin(8, 0, 0, 0).Col(1)));
        }
        mass = ChemCalc.Field("moles", "empirical.mass", "optional", later, ChemCalc.Flag("moles-mass"), "Molar mass, optional");
        var copy = ChemCalc.Copy(() => plain);
        var inputs = K.V(12, grid, ToolKit.Field("Molar mass (optional)", mass, suffix: "g/mol", width: 220), under);
        var card = K.Card(K.V(12, formula, molecular, note, copy), new Thickness(20));
        var side = K.V(16, card, K.V(10, K.Text("Working", Ty.BodyStrong), table));
        Content = ToolKit.Columns(inputs, side, 400, 860, sideFirst: true);
        ChemCalc.CopyOnCtrlC(this, () => plain);
        later.Now();
    }
}

/// <summary>The tool in its pin's flyout: a formula and its molar mass; Open full screen carries the formula to the Molar mass tab.</summary>
internal sealed class MolesQuick : ContentControl
{
    public MolesQuick(ToolContext c)
    {
        Focusable = false;
        var shown = new ContentControl { Focusable = false };
        var mass = Everyday.Figures("—", 30, FontWeights.Bold);
        var under = new ContentControl { Focusable = false, Visibility = Visibility.Collapsed };
        TextBox field = null!;
        MolarMassResult? r = null;
        var later = new ChemCalc.Later(() =>
        {
            r = field.Text.Trim().Length == 0 ? null : ChemMolarMass.Of(field.Text, out _);
            shown.Content = r == null ? null : ChemCalc.Formula(r.Formula.Runs, 16, null, B.Text2);
            mass.Text = r == null ? "—" : $"{ChemNum.Fixed(r.Total, 3)} g/mol";
            ChemCalc.Say(under, r == null && field.Text.Trim().Length > 0 ? "Can’t read that formula." : null, problem: true);
        });
        field = ChemCalc.Field("moles", "formula", "Ca(OH)2", later, ChemCalc.Flag("moles-formula"), "The formula");
        var open = BioUI.OpenButton("Open Molar Mass & Stoichiometry", () => c.OpenFull(Carry(), null), "Open the whole tool on this formula");
        Dictionary<string, string>? Carry() => field.Text.Trim().Length > 0 ? new Dictionary<string, string> { ["formula"] = field.Text.Trim() } : null;
        c.Carry = Carry;
        ChemCalc.CopyOnCtrlC(this, () => r == null ? null : $"{r.Formula.Text}: {ChemNum.Fixed(r.Total, 3)} g/mol");
        Content = K.V(10, field, under, K.V(2, shown, Everyday.Shrinks(mass)), open);
        later.Now();
    }
}

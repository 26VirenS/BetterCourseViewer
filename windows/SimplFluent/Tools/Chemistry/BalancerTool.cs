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

// The Equation Balancer (the Mac's EquationBalancerTool): one field for the whole equation, balanced as it is typed
// (a moment after the last key; Return at once) — the balanced equation typeset with its coefficients in bold, the
// count of each element (and the charge) on each side with a tick when they agree, and the mass check; or, in plain
// words, why it cannot be balanced. The sums are in Tools/Chemistry/Model/ChemBalancer.cs.

#pragma warning disable CA2255
internal static class BalancerRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("balance", c => new BalancerTool(c), c => new BalancerQuick(c));
}
#pragma warning restore CA2255

/// <summary>The balancer's four examples, as the spec has them.</summary>
internal static class BalancerExamples
{
    public static readonly string[] All =
    {
        "Fe + O2 → Fe2O3",
        "C3H8 + O2 → CO2 + H2O",
        "Al + HCl → AlCl3 + H2",
        "KMnO4 + HCl → KCl + MnCl2 + H2O + Cl2",
    };

    /// <summary>What the balancer opens on: the launch's (--tool balance --tool-state "…" or --balance "…"), what a
    /// pin handed over, else what was typed last time; an equation is never made up.</summary>
    public static string Start(ToolContext c) =>
        ChemTools.LaunchState("balance") ?? ChemCalc.Flag("balance") ?? c.TakeInfo("equation") ?? ChemPrefs.String("balance", "equation") ?? "";
}

/// <summary>The whole tool: the field and its examples over the balanced equation and the count table side by side.</summary>
internal sealed class BalancerTool : ContentControl
{
    private readonly TextBox field;
    private readonly ContentControl under = new() { Focusable = false };
    private readonly ContentControl equation = new() { Focusable = false };
    private readonly ContentControl counts = new() { Focusable = false };
    private readonly TextBlock mass = K.Wrapped("", Ty.Body, B.Text2);
    private readonly Wpf.Ui.Controls.Button copy, clear;
    private BalanceResult shown = new() { Outcome = BalanceOutcome.Empty };
    private string said = "";

    public BalancerTool(ToolContext c)
    {
        Focusable = false;
        var later = new ChemCalc.Later(Update);
        field = ChemCalc.Field("balance", "equation", "Fe + O2 → Fe2O3", later, BalancerExamples.Start(c), "The equation to balance");
        field.FontSize = 16;
        field.MinHeight = 38;
        copy = ChemCalc.Copy(() => shown.Ok ? shown.PlainText : null);
        clear = K.Button("Clear", () => { field.Text = ""; later.Now(); field.Focus(); }, icon: SymbolRegular.Dismiss24);
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        var chips = ChemCalc.Chips(BalancerExamples.All.Select(e => (e, (Action)(() => { field.Text = e; later.Now(); }))), "Try");
        var hint = ChemCalc.Hint("Write the reaction with an arrow (->, → or =), + between species; (s), (l), (g), (aq) and charges like Fe3+ or SO4^2- are fine. Return balances.");
        var left = K.V(12, ToolKit.Field("Equation", field), hint, chips, under);
        mass.Visibility = Visibility.Collapsed;
        Typography.SetNumeralAlignment(mass, FontNumeralAlignment.Tabular);
        var result = K.Card(K.V(12, equation, mass, K.H(8, copy, clear)), new Thickness(20));
        equation.MinHeight = 44;
        var main = K.V(20, left, result);
        var side = K.V(10, K.Text("Atom count", Ty.BodyStrong), counts);
        Content = ToolKit.Page(ToolKit.Columns(main, side, 380, 860));
        ChemCalc.CopyOnCtrlC(this, () => shown.Ok ? shown.PlainText : null);
        Update();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { if (!IsKeyboardFocusWithin) field.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Update()
    {
        shown = ChemBalancer.Balance(field.Text);
        var r = shown;
        ChemCalc.Say(under, r.Outcome is BalanceOutcome.Empty or BalanceOutcome.Balanced ? null : r.Message, problem: true);
        if (r.Ok)
        {
            equation.Content = ChemCalc.Equation(r, 26);
            mass.Text = $"Mass check: {ChemNum.Fixed(r.LeftMass, 2)} g = {ChemNum.Fixed(r.RightMass, 2)} g  ✓";
            mass.Visibility = Visibility.Visible;
        }
        else
        {
            var words = r.Outcome == BalanceOutcome.Empty ? "Type an equation to balance it." : "Not balanced yet.";
            equation.Content = K.Wrapped(words, Ty.BodyLarge, B.Text2);
            mass.Visibility = Visibility.Collapsed;
        }
        copy.IsEnabled = r.Ok;
        copy.ToolTip = r.Ok ? $"Copy {r.PlainText}" : "Copy";
        clear.IsEnabled = field.Text.Length > 0;
        counts.Content = CountTable(r);
        AutomationProperties.SetName(equation, r.Ok ? $"Balanced: {r.PlainText}" : r.Outcome == BalanceOutcome.Empty ? "No equation yet" : r.Message);
        var now = r.Outcome == BalanceOutcome.Empty ? "nothing" : r.Ok ? $"balanced {string.Join(",", r.Coefficients)}" : r.Outcome.ToString().ToLowerInvariant();
        ChemCalc.Shows("balance", ref said, now);
    }

    /// <summary>Each element's count each side (and the charge's), a tick where they agree.</summary>
    public static FrameworkElement CountTable(BalanceResult r)
    {
        if (r.Rows.Count == 0) return K.Card(K.Empty(SymbolRegular.TableSimple24, "Each element’s atoms on each side show here."), new Thickness(6));
        var rows = r.Rows.Select(row => new object?[] { row.Name == "Charge" ? "Charge" : $"{row.Name} · {ChemElements.Of(row.Name)?.Name ?? row.Name}", row.Left, row.Right, ChemCalc.Mark(row.Equal) });
        return ChemCalc.Table(new[] { "Element", "Reactants", "Products", "" }, rows, new[] { false, true, true, false });
    }
}

/// <summary>The balancer in its pin's flyout: the field and the balanced line; Open full screen carries the equation.</summary>
internal sealed class BalancerQuick : ContentControl
{
    public BalancerQuick(ToolContext c)
    {
        Focusable = false;
        var result = new ContentControl { Focusable = false, MinHeight = 28 };
        var under = new ContentControl { Focusable = false, Visibility = Visibility.Collapsed };
        TextBox field = null!;
        BalanceResult shown = new() { Outcome = BalanceOutcome.Empty };
        var later = new ChemCalc.Later(() =>
        {
            shown = ChemBalancer.Balance(field.Text);
            ChemCalc.Say(under, shown.Outcome is BalanceOutcome.Empty or BalanceOutcome.Balanced ? null : shown.Message, problem: true);
            result.Content = shown.Ok ? ChemCalc.Equation(shown, 18) : K.Wrapped(shown.Outcome == BalanceOutcome.Empty ? "Type an equation." : "Not balanced yet.", Ty.Body, B.Text2);
        });
        field = ChemCalc.Field("balance", "equation", "Fe + O2 → Fe2O3", later, BalancerExamples.Start(c), "The equation to balance");
        var copy = ChemCalc.Copy(() => shown.Ok ? shown.PlainText : null);
        var open = BioUI.OpenButton("Open Equation Balancer", () => c.OpenFull(field.Text.Trim().Length > 0 ? new Dictionary<string, string> { ["equation"] = field.Text } : null, null), "Open the whole tool on this equation");
        // (0.3.5) Open full screen hands the whole tool the equation typed here
        c.Carry = () => field.Text.Trim().Length > 0 ? new Dictionary<string, string> { ["equation"] = field.Text } : null;
        ChemCalc.CopyOnCtrlC(this, () => shown.Ok ? shown.PlainText : null);
        Content = K.V(10, field, under, result, copy, open);
        later.Now();
    }
}

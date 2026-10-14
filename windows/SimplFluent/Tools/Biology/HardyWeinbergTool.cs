using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Hardy–Weinberg (spec §4.7), the Mac's HardyWeinbergTool.swift: allele and genotype frequencies worked out from any one
// value with each step shown, a genotype bar and a 10 × 10 grid of people; a population's counts tested against
// equilibrium (χ², df = 1); and a population simulated over generations — selection, mutation, migration, inbreeding and
// drift — with five condition chips that light up when broken. The arithmetic, and the state the tool keeps, are
// Model/HardyWeinbergModel.cs; this file has the tool, its Calculate page, its pieces and the pin's quick version;
// HardyWeinbergPages.cs the Test and Simulate pages and the simulation's runner.

#pragma warning disable CA2255
internal static class HardyWeinbergToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("popgen", (c, state) => new HardyWeinbergTool(c, state), HardyWeinbergTool.Pages, (c, state) => new HardyWeinbergCompact(c, state));
}
#pragma warning restore CA2255

/// <summary>Hardy–Weinberg: its three pages, the one chosen in its header shown.</summary>
internal sealed class HardyWeinbergTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("calculate", "Calculate", SymbolRegular.Calculator24, "Everything from one value"),
        new("test", "Test", SymbolRegular.CheckmarkCircle24, "Is a population in equilibrium?"),
        new("simulate", "Simulate", SymbolRegular.DataLine24, "Selection, drift, mutation and migration over generations"),
    };

    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private string? shown;

    /// <summary>The tool's settings kept in Prefs (SimplBio.popgen.*), read back once, and the simulation run when they change.</summary>
    public static void Persist()
    {
        HardyWeinbergModel.Persist(k => BioPrefs.String("popgen", k), (k, v) => BioPrefs.Set("popgen", k, v));
        HWRunner.Hook();
    }

    public HardyWeinbergTool(ToolContext c, BioLaunchState? state)
    {
        Persist();
        if (state != null) HardyWeinbergModel.ApplyLaunch(state.Value, state.Has, state.Page);
        if (HardyWeinbergModel.TakeHandoff(c.TakeInfo) is { } page) BioNav.Set("popgen", page);
        HWRunner.Ensure();
        Children.Add(host);
        Show();
        void Nav(string key) { if (key == "popgen") Show(); }
        Loaded += (_, _) => { BioNav.Changed -= Nav; BioNav.Changed += Nav; Show(); };
        Unloaded += (_, _) => BioNav.Changed -= Nav;
    }

    private void Show()
    {
        var page = BioNav.Page("popgen");
        if (page == shown) return;
        shown = page;
        FrameworkElement view = page switch
        {
            "test" => new HWTestPage(),
            "simulate" => new HWSimulatePage(),
            _ => new HWCalculatePage(),
        };
        host.Content = view;
        Motion.FadeIn(view, Motion.Medium);
    }
}

// ---- pieces ----

/// <summary>The genotypes' colours: AA blue, Aa orange, aa green-blue — validated together for colour-vision deficiency
/// on light and dark (the data-visualisation palette's first three), and always shown with their names; and the
/// simulation's lines, the tool's green deepened for contrast on light.</summary>
internal static class HWColors
{
    public static readonly BioHexPair AA = new("#2a78d6", "#3987e5");
    public static readonly BioHexPair Aa = new("#eb6834", "#d95926");
    public static readonly BioHexPair aa = new("#1baf7a", "#199e70");
    public static readonly BioHexPair Line = new("#5e8f22", "#9fd45a");

    public static Color[] All => new[] { AA.ToColor(), Aa.ToColor(), aa.ToColor() };

    /// <summary>The tool's own colour (its tile's).</summary>
    public static Color Tool => BioColors.Tool("popgen");
}

/// <summary>The three genotype frequencies as one bar, each part named under it.</summary>
internal static class GenotypeBar
{
    public static FrameworkElement Make(double aA, double het, double rec, double height = 26, bool labelled = true)
    {
        var parts = new (string Name, double F, Color C)[] { ("AA", aA, HWColors.AA.ToColor()), ("Aa", het, HWColors.Aa.ToColor()), ("aa", rec, HWColors.aa.ToColor()) };
        var bar = new Grid { Height = height };
        var col = 0;
        foreach (var (name, f, c) in parts)
        {
            if (f <= 0) continue;
            if (bar.ColumnDefinitions.Count > 0) { bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) }); col++; }
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(f, GridUnitType.Star) });
            var seg = new Border { CornerRadius = new CornerRadius(4), Background = K.Brush(c), ToolTip = $"{name} {HardyWeinberg.Num(f)}" };
            bar.Children.Add(seg.Col(col));
            col++;
        }
        var v = K.V(6, bar);
        if (labelled)
        {
            var row = new WrapPanel();
            foreach (var (name, f, c) in parts)
                row.Children.Add(K.H(5, K.Dot(c, 9), K.Text($"{name} {HardyWeinberg.Num(f)}", Ty.Caption).Tabular()).Margin(0, 0, 14, 2));
            v.Children.Add(row);
        }
        AutomationProperties.SetName(v, $"Genotype frequencies: AA {HardyWeinberg.Num(aA)}, Aa {HardyWeinberg.Num(het)}, aa {HardyWeinberg.Num(rec)}");
        return v;
    }

    /// <summary>The colours' key alone (AA, Aa, aa), for several bars at once: the frequencies under the last bar would
    /// read as everyone's.</summary>
    public static FrameworkElement Key()
    {
        var row = new WrapPanel();
        foreach (var (name, c) in new[] { ("AA", HWColors.AA.ToColor()), ("Aa", HWColors.Aa.ToColor()), ("aa", HWColors.aa.ToColor()) })
            row.Children.Add(K.H(5, K.Dot(c, 9), K.Text(name, Ty.Caption)).Margin(0, 0, 14, 2));
        return row;
    }
}

/// <summary>A hundred people, coloured by genotype (AA, then Aa, then aa, row by row), and how many of each.</summary>
internal static class PeopleGrid
{
    public static FrameworkElement Make(IReadOnlyList<int> counts)
    {
        var colours = HWColors.All;
        var who = counts.SelectMany((n, g) => Enumerable.Repeat(g, Math.Max(0, n))).ToList();
        var grid = new UniformGrid { Rows = 10, Columns = 10, HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < 100; i++)
        {
            var icon = i < who.Count ? K.Icon(SymbolRegular.Person24, 17, colours[who[i]], filled: true) : K.Icon(SymbolRegular.Person24, 17, B.Text3, filled: true);
            icon.Margin = new Thickness(2.5);
            grid.Children.Add(icon);
        }
        string[] names = { "AA", "Aa", "aa" };
        var key = new WrapPanel();
        for (var g = 0; g < 3; g++)
            key.Children.Add(K.H(5, K.Icon(SymbolRegular.Person16, 12, colours[g], filled: true), K.Text($"{(g < counts.Count ? counts[g] : 0)} {names[g]}", Ty.Caption).Tabular()).Margin(0, 0, 14, 2));
        var v = K.V(8, grid, key);
        AutomationProperties.SetName(v, $"Of 100 people: {(counts.Count > 0 ? counts[0] : 0)} AA, {(counts.Count > 1 ? counts[1] : 0)} Aa, {(counts.Count > 2 ? counts[2] : 0)} aa");
        return v;
    }
}

/// <summary>A count field with its genotype's swatch over it.</summary>
internal static class HWCountField
{
    public static (FrameworkElement View, TextBox Box) Make(string genotype, Color colour, string text, Action<string> changed)
    {
        var box = ToolKit.Box(text, "0", changed);
        box.FontSize = 15;
        box.TextAlignment = TextAlignment.Right;
        box.MinWidth = 0;
        Typography.SetNumeralAlignment(box, FontNumeralAlignment.Tabular);
        AutomationProperties.SetName(box, $"{genotype} count");
        var label = K.H(5, K.Dot(colour, 8), K.Text(genotype, Ty.Caption, B.Text2).Strong());
        return (K.V(4, label, box), box);
    }
}

/// <summary>A page that draws again whenever the tool's state or the app's appearance changes, while it is on screen.</summary>
internal abstract class HWPageBase : Grid
{
    private bool listening;

    protected HWPageBase()
    {
        Loaded += (_, _) => Listen();
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) Listen(); };
        // (WPF can send Unloaded after the Loaded that follows it when a template above the page is made again — WPF-UI's
        //  styles arrive in steps as Simpl starts — and a page that stopped listening then would never hear the simulation
        //  finish: it lets go only if it is still out of the window once the dispatcher has caught up)
        Unloaded += (_, _) => Dispatcher.BeginInvoke(() => { if (PresentationSource.FromVisual(this) == null) Quiet(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Changed() => CrossKit.KeepFocus(this, Draw);

    private void Listen()
    {
        if (!listening)
        {
            listening = true;
            HardyWeinbergModel.Changed += Changed;
            Appearance.Changed += Changed;
        }
        Shown();
        Draw();
    }

    private void Quiet()
    {
        if (!listening) return;
        listening = false;
        HardyWeinbergModel.Changed -= Changed;
        Appearance.Changed -= Changed;
    }

    /// <summary>The page came on screen (before it draws).</summary>
    protected virtual void Shown() { }

    protected abstract void Draw();
}

// ---- the Calculate page ----

internal sealed class HWCalculatePage : HWPageBase
{
    private sealed record Example(string Title, HWGiven Given, string Value);

    private static readonly Example[] Examples =
    {
        new("q² = 0.16", HWGiven.Q2, "0.16"),
        new("Cystic fibrosis: 1 in 2,500 affected", HWGiven.RecessivePercent, "0.04"),
        new("Tasting PTC: 70% can (dominant)", HWGiven.DominantPercent, "70"),
        new("A sample: 50 AA, 30 Aa, 20 aa", HWGiven.Counts, ""),
    };

    private readonly ContentControl given = CrossKit.Host();
    private readonly TextBlock shortName = K.Text("", Ty.BodyLarge).Strong();
    private readonly TextBlock percent = K.Text("%", Ty.BodyLarge, B.Text2);
    private readonly TextBox value;
    private readonly FrameworkElement valueRow;
    private readonly FrameworkElement countsRow;
    private readonly TextBox countAA, countAa, countaa;
    private readonly ContentControl hint = CrossKit.Host();
    private readonly ContentControl centre = CrossKit.Host();
    private readonly ContentControl right = CrossKit.Host();
    private bool syncing;

    public HWCalculatePage()
    {
        value = ToolKit.Box(HardyWeinbergModel.Value, "", t => { if (!syncing) HardyWeinbergModel.SetValue(t); });
        value.FontSize = 17;
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        var equals = K.Text("=", Ty.BodyLarge, B.Text2);
        foreach (var e in new FrameworkElement[] { shortName, equals, percent }) e.VerticalAlignment = VerticalAlignment.Center;
        shortName.MinWidth = 30;
        valueRow = K.Columns("auto, auto, *, auto", shortName.Col(0), equals.Margin(8, 0, 8, 0).Col(1), value.Col(2), percent.Margin(8, 0, 0, 0).Col(3));
        void Counts(string? a, string? b, string? c) => HardyWeinbergModel.SetCounts(a ?? countAA!.Text, b ?? countAa!.Text, c ?? countaa!.Text);
        (var fAA, countAA) = HWCountField.Make("AA", HWColors.AA.ToColor(), HardyWeinbergModel.CountAA, t => { if (!syncing) Counts(t, null, null); });
        (var fAa, countAa) = HWCountField.Make("Aa", HWColors.Aa.ToColor(), HardyWeinbergModel.CountAa, t => { if (!syncing) Counts(null, t, null); });
        (var faa, countaa) = HWCountField.Make("aa", HWColors.aa.ToColor(), HardyWeinbergModel.Countaa, t => { if (!syncing) Counts(null, null, t); });
        countsRow = K.Columns("*, 8, *, 8, *", fAA.Col(0), fAa.Col(2), faa.Col(4));
        var examples = K.V(6);
        foreach (var e in Examples)
        {
            var ex = e;
            var b = K.Button(e.Title, () =>
            {
                HardyWeinbergModel.SetGiven(ex.Given);
                if (ex.Given == HWGiven.Counts) HardyWeinbergModel.SetCounts("50", "30", "20");
                else HardyWeinbergModel.SetValue(ex.Value);
            });
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            examples.Children.Add(b);
        }
        var left = K.V(14, CrossKit.Heading("Given"), given, valueRow, countsRow, hint, CrossKit.Heading("Examples"), examples);
        Children.Add(BioUI.Work(left, centre, right));
        CrossKit.FindKey(this, () => HardyWeinbergModel.Given == HWGiven.Counts ? countAA : value);
        Draw();
    }

    protected override void Draw()
    {
        var g = HardyWeinbergModel.Given;
        var r = HardyWeinbergModel.Calculation;
        given.Content = CrossKit.Menu(K.Text(g.Title(), Ty.BodyStrong), m =>
        {
            foreach (var x in HWGivens.All)
            {
                var gx = x;
                m.Items.Add(CrossKit.Item(x.Title(), x == g, () => HardyWeinbergModel.SetGiven(gx)));
            }
        }, "What you know about the population", "given");
        syncing = true;
        shortName.Text = g.Short();
        percent.Visibility = g.IsPercent() ? Visibility.Visible : Visibility.Collapsed;
        value.PlaceholderText = g.Placeholder();
        value.ToolTip = g.Title() + " (Ctrl+F)";
        AutomationProperties.SetName(value, g.Title());
        if (value.Text != HardyWeinbergModel.Value && !value.IsKeyboardFocusWithin) value.Text = HardyWeinbergModel.Value;
        foreach (var (box, text) in new[] { (countAA, HardyWeinbergModel.CountAA), (countAa, HardyWeinbergModel.CountAa), (countaa, HardyWeinbergModel.Countaa) })
            if (box.Text != text && !box.IsKeyboardFocusWithin) box.Text = text;
        syncing = false;
        valueRow.Visibility = g == HWGiven.Counts ? Visibility.Collapsed : Visibility.Visible;
        countsRow.Visibility = g == HWGiven.Counts ? Visibility.Visible : Visibility.Collapsed;
        CrossKit.Wrong(value, r.Error != null && g != HWGiven.Counts);
        hint.Content = r.Error is { } e
            ? CrossKit.Problem(e)
            : CrossKit.Caption(g.IsPercent() ? "A percent, 0 to 100." : (g == HWGiven.Counts ? "Whole numbers of individuals." : "A frequency, 0 to 1 (or a fraction like 1/2500)."));
        centre.Content = Centre(r.Calc);
        right.Content = Side(r.Calc);
    }

    private static FrameworkElement Centre(HWCalc? c)
    {
        if (c == null)
            return CrossKit.CentreCard(K.Wrapped("Enter a value on the left: any one of p, q, p², 2pq, q², the share showing either trait, or the genotype counts.", Ty.Body, B.Text2));
        var steps = new Grid();
        steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < c.Steps.Count; i++)
        {
            steps.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = K.Wrapped(c.Steps[i].Label, Ty.Caption, B.Text2).Strong();
            label.Margin = new Thickness(0, 7, 12, 5);
            var math = PTKit.Selectable(c.Steps[i].Math, 16, FontWeights.Medium);
            Typography.SetNumeralAlignment(math, FontNumeralAlignment.Tabular);
            math.Margin = new Thickness(0, 4, 0, 4);
            AutomationProperties.SetName(math, $"{c.Steps[i].Label}: {c.Steps[i].Math}");
            steps.Children.Add(label.Row(i).Col(0));
            steps.Children.Add(math.Row(i).Col(1));
        }
        var v = K.V(20, K.V(6, CrossKit.Label("Step by step"), steps));
        if (c.Notes.Count > 0)
        {
            var notes = K.V(6);
            foreach (var n in c.Notes) notes.Children.Add(CrossKit.Note(n));
            v.Children.Add(notes);
        }
        v.Children.Add(K.V(10, CrossKit.Label("Genotype frequencies"), GenotypeBar.Make(c.P2, c.TwoPQ, c.Q2)));
        v.Children.Add(K.V(10, CrossKit.Label("100 people"), PeopleGrid.Make(HardyWeinberg.People(new[] { c.P2, c.TwoPQ, c.Q2 }))));
        return CrossKit.CentreCard(v);
    }

    private static FrameworkElement Side(HWCalc? c)
    {
        var v = K.V(14);
        if (c != null)
        {
            v.Children.Add(BioUI.Facts(new (string, string, Color?)[]
            {
                ("p (A)", HardyWeinberg.Num(c.P), null),
                ("q (a)", HardyWeinberg.Num(c.Q), null),
                ("p² (AA)", HardyWeinberg.Num(c.P2), HWColors.AA.ToColor()),
                ("2pq (Aa)", HardyWeinberg.Num(c.TwoPQ), HWColors.Aa.ToColor()),
                ("q² (aa)", HardyWeinberg.Num(c.Q2), HWColors.aa.ToColor()),
                ("Carriers per 1,000", HardyWeinberg.Carriers(c.CarriersPer1000), null),
            }));
            if (c.Counts is { } s)
            {
                double n = s.N;
                v.Children.Add(BioUI.Card("Observed and expected", null, $"{s.N} individuals", BioUI.Facts(new (string, string, Color?)[]
                {
                    ("AA", $"{s.AA} · expected {HardyWeinberg.Num(c.P2 * n)}", null),
                    ("Aa", $"{s.Aa} · expected {HardyWeinberg.Num(c.TwoPQ * n)}", null),
                    ("aa", $"{s.aa} · expected {HardyWeinberg.Num(c.Q2 * n)}", null),
                })));
            }
        }
        v.Children.Add(BioUI.Card("The equations", null, null,
            BioUI.FactRow("Alleles", "p + q = 1"),
            BioUI.FactRow("Genotypes", "p² + 2pq + q² = 1"),
            BioUI.FactRow("Watch out", "A dominant allele does not become more common by being dominant. Without selection, mutation, migration or drift, p stays the same in every generation.")));
        v.Children.Add(BioUI.Card("The five conditions", null, null,
            K.Wrapped("No mutation · no migration · a very large population · random mating · no natural selection. While all five hold, allele and genotype frequencies stay the same.", Ty.Body)));
        return v;
    }
}

// ---- the pin's quick version ----

/// <summary>
/// Hardy–Weinberg in its pin's flyout (spec §3.2 item 3): everything from one value — p, q, p², 2pq, q² and carriers — and
/// its genotype bar; Enter (or Open) opens it in the whole tool with the steps, the 100 people, the test and the simulation.
/// </summary>
internal sealed class HardyWeinbergCompact : ContentControl
{
    private static readonly HWGiven[] Kinds = { HWGiven.Q2, HWGiven.P, HWGiven.Q, HWGiven.P2, HWGiven.TwoPQ, HWGiven.RecessivePercent, HWGiven.DominantPercent };

    /// <summary>What was typed, kept while the flyout comes and goes.</summary>
    private static HWGiven kept = HWGiven.Q2;
    private static string keptValue = "0.16";

    private readonly ToolContext c;
    private readonly ContentControl menu = CrossKit.Host();
    private readonly TextBox box;
    private readonly ContentControl result = CrossKit.Host();

    public HardyWeinbergCompact(ToolContext c, BioLaunchState? state)
    {
        this.c = c;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        // (a launch's value, as the pin's picture asks: --pin-open popgen --tool-state q2=0.16)
        if (state != null)
            foreach (var g in Kinds)
                if (state.Value(g.Key()) is { } v) { kept = g; keptValue = v; }
        box = ToolKit.Box(keptValue, kept.Placeholder(), t => { keptValue = t; Draw(); });
        box.FontSize = 15;
        Typography.SetNumeralAlignment(box, FontNumeralAlignment.Tabular);
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Open();
        };
        var open = BioUI.OpenButton("Open in Hardy–Weinberg", Open, "The steps, the 100 people, the test and the simulation (Enter)");
        Content = K.V(12, K.Columns("190, 8, *", menu.Col(0), box.Col(2)), result, open);
        Draw();
    }

    private void Open() => c.OpenFull(new Dictionary<string, string> { ["given"] = kept.Key(), ["value"] = box.Text }, null);

    private void Draw()
    {
        menu.Content = CrossKit.Menu(new Viewbox { Child = K.Text(kept.Title(), Ty.Body), StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left }, m =>
        {
            foreach (var g in Kinds)
            {
                var gg = g;
                m.Items.Add(CrossKit.Item(g.Title(), g == kept, () =>
                {
                    kept = gg;
                    box.PlaceholderText = gg.Placeholder();
                    CrossKit.KeepFocus(this, Draw);
                }));
            }
        }, "Given", "given");
        AutomationProperties.SetName(box, kept.Title());
        var r = HWInput.Number(box.Text, kept.IsPercent()) is { } v ? HardyWeinberg.Calculate(kept, v) : ((HWCalc?)null, (string?)null);
        if (r.Item1 is { } calc)
        {
            FrameworkElement Cell(string name, double x)
            {
                var n = K.Text(name, Ty.Body, B.Text2);
                n.Width = 32;
                n.VerticalAlignment = VerticalAlignment.Bottom;
                return K.H(6, n, Everyday.Figures(HardyWeinberg.Num(x), 17, FontWeights.Bold));
            }
            var carriers = K.Text($"{HardyWeinberg.Carriers(calc.CarriersPer1000)} carriers / 1,000", Ty.Caption, B.Text2);
            carriers.VerticalAlignment = VerticalAlignment.Center;
            var grid = K.Rows("auto, 4, auto, 4, auto",
                K.Columns("*, 16, *", Cell("p", calc.P).Col(0), Cell("q", calc.Q).Col(2)).Row(0),
                K.Columns("*, 16, *", Cell("p²", calc.P2).Col(0), Cell("2pq", calc.TwoPQ).Col(2)).Row(2),
                K.Columns("*, 16, *", Cell("q²", calc.Q2).Col(0), carriers.Col(2)).Row(4));
            result.Content = K.V(10, grid, GenotypeBar.Make(calc.P2, calc.TwoPQ, calc.Q2, 14));
            CrossKit.Wrong(box, false);
        }
        else if (r.Item2 is { } e)
        {
            result.Content = CrossKit.Problem(e);
            CrossKit.Wrong(box, true);
        }
        else
        {
            result.Content = K.Wrapped("Type a value: q² = 0.16 gives q = 0.40, p = 0.60, 2pq = 0.48.", Ty.Body, B.Text2);
            CrossKit.Wrong(box, box.Text.Trim().Length > 0);
        }
    }
}

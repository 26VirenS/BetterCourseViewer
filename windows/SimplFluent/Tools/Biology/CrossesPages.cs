using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Genetics Crosses' other pages (spec §4.5), the Mac's CrossesProbability.swift, CrossesChiSquare.swift and
// CrossesPedigree.swift: Probability — "the probability that an offspring of AaBbCc × AaBbCc is aaB_Cc", asked gene by gene
// with chips and worked by the product rule, each factor from that gene's own small Punnett square, then the sum rule and
// the chance over several children; Chi-square — the categories with their ratio and observed counts, worked the way the
// AP sheet writes it, the null hypothesis in words and the conclusion "reject" or "fail to reject" (never "accept", never
// "prove"); Pedigree — a family drawn the standard way and its mode checker, the people who rule a mode out ringed.

/// <summary>A page that draws again whenever the tool's state or the app's appearance changes, while it is on screen.</summary>
internal abstract class CrossesPageBase : Grid
{
    protected CrossesPageBase()
    {
        void Changed() => CrossKit.KeepFocus(this, Draw);
        Loaded += (_, _) => { CrossesModel.Changed -= Changed; CrossesModel.Changed += Changed; Appearance.Changed -= Changed; Appearance.Changed += Changed; Draw(); };
        Unloaded += (_, _) => { CrossesModel.Changed -= Changed; Appearance.Changed -= Changed; };
    }

    protected abstract void Draw();
}

// ---- Probability ----

internal sealed class CrossProbabilityPage : CrossesPageBase
{
    private readonly TextBox typed;
    private readonly ContentControl typedNote = CrossKit.Host();
    private readonly Wpf.Ui.Controls.Button useCross;
    private readonly ContentControl chips = CrossKit.Host();
    private readonly ContentControl centre = CrossKit.Host();
    private readonly ContentControl right = CrossKit.Host();
    private bool syncing;

    public CrossProbabilityPage()
    {
        typed = ToolKit.Box(CrossesModel.ProbTyped, "AaBbCc × AaBbCc", t => { if (!syncing) CrossesModel.ProbTyped = t; });
        typed.FontSize = 15;
        typed.ToolTip = "Type a cross and press Enter (Ctrl+F)";
        AutomationProperties.SetName(typed, "Cross, typed");
        typed.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            CrossesModel.ProbTyped = typed.Text;
            CrossesModel.ApplyProbTyped();
            if (CrossesModel.ProbError == null && typed.Text != CrossesModel.ProbTyped)
            {
                syncing = true;
                typed.Text = CrossesModel.ProbTyped;
                typed.CaretIndex = typed.Text.Length;
                syncing = false;
            }
        };
        useCross = ChemKit.Wide("Use the Cross page's cross", () => CrossesModel.SetProbSetup(CrossesModel.Setup.Copy()));
        var left = K.V(14,
            CrossKit.Heading("Cross"), K.V(6, typed, typedNote), useCross,
            CrossKit.Heading("Offspring asked for"), chips,
            CrossKit.Caption("Pick a genotype for each gene, a group (A_ is AA or Aa), or any."));
        Children.Add(BioUI.Work(left, centre, right));
        CrossKit.FindKey(this, () => typed);
        Draw();
    }

    private static BioCross Cross => CrossesModel.ProbSetup.Cross;

    protected override void Draw()
    {
        syncing = true;
        if (typed.Text != CrossesModel.ProbTyped && !typed.IsKeyboardFocusWithin) typed.Text = CrossesModel.ProbTyped;
        syncing = false;
        CrossKit.Wrong(typed, CrossesModel.ProbError != null);
        typedNote.Content = CrossesModel.ProbError is { } e ? CrossKit.Problem(e) : null;
        useCross.IsEnabled = !CrossesModel.Setup.Equals(CrossesModel.ProbSetup);
        useCross.ToolTip = "Ask about " + CrossText.Plain(CrossesModel.Setup.Text);
        var v = K.V(14);
        var genes = CrossesModel.ProbSetup.Genes;
        for (var k = 0; k < genes.Count; k++) v.Children.Add(Chips(k));
        chips.Content = v;
        var targets = CrossesModel.ChosenTargets;
        var product = CrossProbability.Product(Cross, targets);
        centre.Content = Centre(targets, product);
        right.Content = K.V(14, SumCard(), ChildrenCard(product.Product),
            BioUI.Caption("The product rule: independent events happen together with the product of their chances (and). The sum rule: outcomes that exclude each other happen with the sum of their chances (or)."));
    }

    private static FrameworkElement Chips(int k)
    {
        var g = CrossesModel.ProbSetup.Genes[k];
        var chosen = k < CrossesModel.ProbTargets.Count ? CrossesModel.ProbTargets[k] : "any";
        var wrap = new WrapPanel();
        foreach (var t in CrossProbability.Targets(g))
        {
            var id = t.Id;
            var chip = CrossKit.Chip(t.Label, id == chosen, () => CrossesModel.SetProbTarget(k, id), CrossColors.Tool,
                t.IsAny ? $"Any genotype for gene {g.Letter}" : $"Gene {g.Letter}: {CrossText.Plain(t.Label)}", $"chip:{k}:{id}");
            wrap.Children.Add(chip.Margin(0, 0, 6, 6));
        }
        AutomationProperties.SetName(wrap, $"Gene {g.Letter}");
        return K.V(6, K.Text($"Gene {g.Letter} · {g.Pattern.Label().ToLowerInvariant()}", Ty.Caption, B.Text2).Strong(), wrap);
    }

    private static FrameworkElement Centre(List<CrossTarget> targets, (List<BioFrac> Factors, BioFrac Product) pr)
    {
        var cross = Cross;
        var wanted = CrossProbability.TargetText(cross, targets);
        var lethal = cross.Genes.Any(g => g.Pattern is BioInheritance.LethalDominantHomozygote or BioInheritance.LethalRecessive);
        var tool = CrossColors.Tool;
        var question = CrossInk.Wrapped($"Probability that an offspring of {CrossesModel.ProbSetup.Text} is {wanted}", 19, FontWeights.SemiBold);
        AutomationProperties.SetName(question, $"Probability that an offspring of {CrossText.Spoken(CrossesModel.ProbSetup.Text)} is {CrossText.Spoken(wanted)}");
        var result = K.H(10, CrossKit.Figures("= " + pr.Product.Text, 30, FontWeights.Bold, tool), CrossKit.Figures("≈ " + CrossProbability.Percent(pr.Product.Double), 20, FontWeights.SemiBold));
        AutomationProperties.SetName(result, $"equals {pr.Product.Text}, about {CrossProbability.Percent(pr.Product.Double)}");
        var rule = K.V(8,
            CrossKit.Label("The product rule"),
            CrossInk.Wrapped("P = " + string.Join(" × ", targets.Select(t => $"P({(t.IsAny ? "any" : t.Label)})")), 17, FontWeights.Medium),
            CrossKit.Figures("= " + string.Join(" × ", pr.Factors.Select(f => f.Text)), 17, FontWeights.Medium),
            result,
            K.Wrapped("The genes are on different chromosomes and assort independently, so the chances for each gene multiply."
                + (lethal ? " For a lethal gene, each chance is among the offspring that are born." : ""), Ty.Body, B.Text2));
        var squares = new WrapPanel();
        for (var k = 0; k < cross.Genes.Count && k < targets.Count && k < pr.Factors.Count; k++)
            squares.Children.Add(GeneSquare(cross, k, targets[k], pr.Factors[k]).Margin(0, 0, 14, 14));
        return CrossKit.CentreCard(K.V(20, K.V(6, CrossKit.Label("The question"), question), rule, CrossKit.Label("Each gene's own square"), squares));
    }

    private static FrameworkElement GeneSquare(BioCross cross, int k, CrossTarget t, BioFrac factor)
    {
        var one = new BioCross(new List<BioGene> { cross.Genes[k] }, new[] { cross.Parent1[k] }, new[] { cross.Parent2[k] });
        Func<int[][], bool>? lit = t.IsAny ? null : g => t.Has(g[0]);
        var v = K.V(8,
            CrossInk.Text(one.GenotypeText(one.Parent1) + " × " + one.GenotypeText(one.Parent2), 14),
            new PunnettGrid(new CrossPunnett(one), lit, small: true),
            CrossInk.Text($"P({(t.IsAny ? "any" : t.Label)}) = {factor.Text}", 14, FontWeights.Bold, K.Darkened(CrossColors.Tool)));
        var b = new Border { Child = v, Padding = new Thickness(12), CornerRadius = new CornerRadius(12) };
        b.Bg(B.Subtle);
        return b;
    }

    private static FrameworkElement SumCard()
    {
        var genes = CrossesModel.ProbSetup.Genes;
        var cross = Cross;
        var k = Math.Min(CrossesModel.SumGene, Math.Max(genes.Count - 1, 0));
        var singles = k < genes.Count ? CrossProbability.Targets(genes[k]).Where(t => t.Single).ToList() : new List<CrossTarget>();
        var picked = singles.Where(t => CrossesModel.SumTargets.Contains(t.Id)).ToList();
        var sum = CrossProbability.Sum(cross, k, picked);
        var rows = new List<UIElement?>();
        if (genes.Count > 1)
            rows.Add(CrossKit.Segmented(genes.Select(g => $"Gene {g.Letter}").ToList(), k, CrossesModel.SetSumGene, uid: "sumgene"));
        var wrap = new WrapPanel();
        foreach (var t in singles)
        {
            var id = t.Id;
            wrap.Children.Add(CrossKit.Chip(t.Label, CrossesModel.SumTargets.Contains(id), () => CrossesModel.ToggleSumTarget(id), CrossColors.Tool, uid: $"sum:{id}").Margin(0, 0, 6, 6));
        }
        rows.Add(wrap);
        if (picked.Count == 0) rows.Add(K.Wrapped("Pick two or more genotypes.", Ty.Body, B.Text2));
        else
        {
            var working = K.V(4,
                CrossInk.Wrapped("P(" + string.Join(" or ", picked.Select(t => t.Label)) + ")", 15, FontWeights.SemiBold),
                CrossInk.Wrapped("= " + string.Join(" + ", picked.Select(t => $"P({t.Label})")), 14, FontWeights.Normal),
                CrossKit.Figures("= " + string.Join(" + ", sum.Terms.Select(f => f.Text)), 14, FontWeights.Normal),
                CrossKit.Figures($"= {sum.Total.Text} ({sum.Total.PercentText})", 18, FontWeights.Bold));
            AutomationProperties.SetName(working, $"Probability of {string.Join(" or ", picked.Select(t => CrossText.Spoken(t.Label)))}: {sum.Total.Text}");
            rows.Add(working);
            rows.Add(BioUI.Caption("An offspring has only one genotype, so these outcomes exclude each other and their chances add."));
        }
        return BioUI.Card("Sum rule", null, "Either one outcome or another", rows.ToArray());
    }

    private static FrameworkElement ChildrenCard(BioFrac p)
    {
        var n = CrossesModel.Children;
        var k = Math.Min(CrossesModel.ExactlyK, n);
        var atLeast = CrossProbability.AtLeastOne(p, n);
        var exactly = CrossProbability.Exactly(k, n, p);
        var q = BioFrac.One - p;
        string Sup(int x) => HWFormat.Superscript(x);
        FrameworkElement Stepper(string words, int value, int lo, int hi, Action<int> set, string less, string more, string uid)
        {
            var t = K.Text(words, Ty.BodyStrong);
            t.VerticalAlignment = VerticalAlignment.Center;
            return K.Columns("*, auto", t.Col(0), CrossKit.Keyed(ChemKit.Stepper(value, lo, hi, set, less, more), uid).Col(1));
        }
        var first = K.V(4,
            K.Text($"At least one of {n}", Ty.Caption, B.Text2).Strong(),
            CrossKit.Figures($"1 − (1 − p){Sup(n)} = 1 − ({q.Text}){Sup(n)}", 14, FontWeights.Normal),
            CrossKit.Figures("= " + atLeast.Text, 17, FontWeights.Bold));
        AutomationProperties.SetName(first, $"At least one of {n}: {atLeast.Text}");
        var second = K.V(4,
            CrossKit.Figures($"C({n}, {k}) × p{Sup(k)} × (1 − p){Sup(n - k)}", 14, FontWeights.Normal),
            CrossKit.Figures($"= {CrossProbability.Choose(n, k)} × ({p.Text}){Sup(k)} × ({q.Text}){Sup(n - k)}", 14, FontWeights.Normal),
            CrossKit.Figures("= " + exactly.Text, 17, FontWeights.Bold));
        AutomationProperties.SetName(second, $"Exactly {k} of {n}: {exactly.Text}");
        return BioUI.Card("Several children", null, "Each child is a new, independent event",
            K.Wrapped($"p = {p.Text} for one child (the question in the middle)", Ty.Body, B.Text2),
            Stepper(ToolKit.Plural(n, "child", "children"), n, 1, 20, CrossesModel.SetChildren, "One child fewer", "One child more", "children"),
            first,
            K.Divider(),
            Stepper($"Exactly {k} of {n}", k, 0, n, CrossesModel.SetExactlyK, "One fewer", "One more", "exactly"),
            second,
            BioUI.Caption("C(n, k) counts the orders the k children could come in (the binomial)."));
    }
}

// ---- Chi-square ----

internal sealed class CrossChiSquarePage : CrossesPageBase
{
    private readonly ContentControl source = CrossKit.Host();
    private readonly ContentControl rows = CrossKit.Host();
    private readonly ContentControl centre = CrossKit.Host();
    private readonly ContentControl right = CrossKit.Host();
    private int shape = -1;
    private TextBox? firstBox;

    public CrossChiSquarePage()
    {
        var add = ChemKit.Wide("Add a category", CrossesModel.AddChiRow, icon: SymbolRegular.Add24);
        add.ToolTip = "Another category of offspring";
        var left = K.V(14,
            CrossKit.Heading("Start from"), source,
            CrossKit.Heading("Categories"), rows, add,
            CrossKit.Caption("Ratio: the parts the null hypothesis expects (9, 3, 3, 1). Observed: the counts you got, as whole numbers."));
        Children.Add(BioUI.Work(left, centre, right));
        CrossKit.FindKey(this, () => firstBox);
        Draw();
    }

    protected override void Draw()
    {
        var setupText = CrossesModel.Setup.Text;
        source.Content = CrossKit.Menu(CrossInk.Text(CrossesModel.ChiSource, 14), m =>
        {
            m.Items.Add(CrossKit.Item($"The Cross page's cross ({setupText})", false, CrossesModel.FillChiFromCross));
            m.Items.Add(CrossKit.Item("Mendel's seed shapes (3 : 1)", false, () => CrossesModel.SetChiRows(CrossesModel.MendelSeeds, "Mendel's seed shapes")));
            m.Items.Add(CrossKit.Item("Mendel's dihybrid cross (9 : 3 : 3 : 1)", false, () => CrossesModel.SetChiRows(CrossesModel.MendelDihybrid, "Mendel's dihybrid cross")));
        }, "Fill the categories from the cross or from Mendel's counts", "chisource");
        // (the fields are made again only when rows come or go: one typed in keeps its place and its caret)
        if (shape != CrossesModel.ChiShape)
        {
            shape = CrossesModel.ChiShape;
            rows.Content = Rows();
        }
        var made = ChiSquareWork.Make(CrossesModel.ChiRows);
        centre.Content = Centre(made.Work, made.Problems);
        right.Content = ChiCriticalTable.Make(made.Work?.Result.Df ?? Math.Max(CrossesModel.ChiRows.Count - 1, 1), made.Work?.Result.Chi2, CrossColors.Tool);
    }

    private FrameworkElement Rows()
    {
        var g = new Grid();
        foreach (var w in new[] { "*", "6", "54", "6", "74", "4", "auto" }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = K.Length(w) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(CrossKit.Label("Category").Col(0));
        g.Children.Add(CrossKit.Label("Ratio").Col(2));
        g.Children.Add(CrossKit.Label("Observed").Col(4));
        firstBox = null;
        var r = 1;
        foreach (var row in CrossesModel.ChiRows)
        {
            var id = row.Id;
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            r++;
            var name = ToolKit.Box(row.Name, "Name", t => CrossesModel.EditChiRow(id, name: t));
            AutomationProperties.SetName(name, "Category name");
            var ratio = ToolKit.Box(row.Ratio, "1", t => CrossesModel.EditChiRow(id, ratio: t));
            ratio.TextAlignment = TextAlignment.Right;
            AutomationProperties.SetName(ratio, $"{row.Name} expected ratio");
            var observed = ToolKit.Box(row.Observed, "0", t => CrossesModel.EditChiRow(id, observed: t));
            observed.TextAlignment = TextAlignment.Right;
            AutomationProperties.SetName(observed, $"{row.Name} observed count");
            foreach (var box in new[] { name, ratio, observed }) box.MinWidth = 0;
            firstBox ??= observed;
            var remove = K.IconButton(SymbolRegular.SubtractCircle24, () => CrossesModel.RemoveChiRow(id), "Take this category out", 16, 28);
            remove.IsEnabled = CrossesModel.ChiRows.Count > 2;
            foreach (var (e, col) in new (FrameworkElement, int)[] { (name, 0), (ratio, 2), (observed, 4), (remove, 6) })
            {
                Grid.SetRow(e, r);
                Grid.SetColumn(e, col);
                e.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(e);
            }
            r++;
        }
        return g;
    }

    private static FrameworkElement Centre(ChiSquareWork? w, List<string> problems)
    {
        if (w == null)
        {
            // (what the test will assume, said before any count is in)
            var v = K.V(8);
            if (ChiSquareWork.RatioTextOf(CrossesModel.ChiRows) is { } ratio)
                v.Children.Add(BioUI.FactRow("Null hypothesis (H₀)", ChiSquareWork.NullHypothesisFor(ratio), CrossColors.Tool).Margin(0, 0, 0, 10));
            v.Children.Add(K.Text("Not yet", Ty.Subtitle));
            foreach (var p in problems) v.Children.Add(ChemKit.Line(SymbolRegular.ErrorCircle16, p, K.Tone("orange")));
            return CrossKit.CentreCard(v);
        }
        var tool = CrossColors.Tool;
        var body = K.V(18,
            BioUI.FactRow("Null hypothesis (H₀)", w.NullHypothesis, tool),
            Table(w),
            new ObservedChart(w.Names, w.Observed, w.Expected, new[] { tool }),
            BioUI.Facts(new (string, string, Color?)[]
            {
                ("χ² = Σ (o − e)² ÷ e", HWFormat.Fixed(w.Result.Chi2, 3), null),
                ("Degrees of freedom", $"{w.Names.Count} − 1 = {w.Result.Df}", null),
                ("Critical value (p = 0.05)", HWFormat.Fixed(w.Result.Critical, 2), null),
                ("p-value", w.PText, null),
            }),
            Conclusion(w));
        return CrossKit.CentreCard(body);
    }

    private static string F2(double x) => HWFormat.Fixed(x, 2);

    private static FrameworkElement Table(ChiSquareWork w)
    {
        var g = new Grid();
        for (var i = 0; i < 5; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var row = 0;
        void Add(UIElement e, int col, bool right = true)
        {
            if (e is FrameworkElement fe) { fe.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left; fe.Margin = new Thickness(col == 0 ? 0 : 14, 4, 0, 4); }
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            g.Children.Add(e);
        }
        void NextRow() { g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
        TextBlock Cell(string s, bool bold = false, Color? colour = null)
        {
            var t = K.Text(s, bold ? Ty.BodyStrong : Ty.Body).Tabular();
            if (colour is { } c) t.Foreground = K.Brush(K.Darkened(c));
            return t;
        }
        NextRow();
        Add(CrossKit.Label("Category"), 0, false);
        Add(CrossKit.Label("o"), 1);
        Add(CrossKit.Label("e"), 2);
        Add(CrossKit.Label("o − e"), 3);
        Add(CrossKit.Label("(o − e)² ÷ e"), 4);
        row++;
        NextRow();
        var d1 = K.Divider();
        Grid.SetColumnSpan(d1, 5);
        Add(d1, 0, false);
        d1.HorizontalAlignment = HorizontalAlignment.Stretch;
        row++;
        for (var i = 0; i < w.Names.Count; i++)
        {
            NextRow();
            var name = CrossInk.Text(w.Names[i], 14);
            Add(name, 0, false);
            Add(Cell(ChiSquareWork.Number(w.Observed[i])), 1);
            Add(Cell(F2(w.Expected[i]), colour: w.Expected[i] < 5 ? K.Tone("orange") : null), 2);
            Add(Cell(F2(w.Observed[i] - w.Expected[i])), 3);
            Add(Cell(HWFormat.Fixed(w.Result.Terms[i], 3)), 4);
            row++;
        }
        NextRow();
        var d2 = K.Divider();
        Grid.SetColumnSpan(d2, 5);
        Add(d2, 0, false);
        d2.HorizontalAlignment = HorizontalAlignment.Stretch;
        row++;
        NextRow();
        Add(Cell("Total"), 0, false);
        Add(Cell(ChiSquareWork.Number(w.Total)), 1);
        Add(Cell(F2(w.Expected.Sum())), 2);
        Add(Cell("χ² = " + HWFormat.Fixed(w.Result.Chi2, 3), bold: true), 4);
        var b = new Border { Child = g, Padding = new Thickness(14), CornerRadius = new CornerRadius(12) };
        b.Bg(B.Subtle);
        AutomationProperties.SetName(b, "The chi-square table: " + string.Join("; ", w.Names.Select((n, i) =>
            $"{CrossText.Spoken(n)}: observed {ChiSquareWork.Number(w.Observed[i])}, expected {F2(w.Expected[i])}, term {HWFormat.Fixed(w.Result.Terms[i], 3)}")) + $". Chi-square {HWFormat.Fixed(w.Result.Chi2, 3)}");
        return b;
    }

    private static FrameworkElement Conclusion(ChiSquareWork w)
    {
        var lines = new List<UIElement>
        {
            K.Wrapped(w.Result.Conclusion, Ty.BodyStrong),
            K.Wrapped(w.ConclusionDetail, Ty.Body),
            K.Wrapped(w.Meaning, Ty.Body, B.Text2),
        };
        if (w.LowExpected) lines.Add(ChemKit.Line(SymbolRegular.Warning16, "An expected count is under 5, where chi-square is unreliable: combine categories or count more.", K.Tone("orange")));
        lines.Add(BioUI.Caption("We never “accept” the null hypothesis, and a test never “proves” it: we reject it, or fail to reject it."));
        return CrossKit.Conclusion(w.Result.Reject, lines);
    }
}

// ---- Pedigree ----

/// <summary>A pedigree symbol: a square (male) or a circle (female), filled when affected, its left half filled for a known
/// carrier.</summary>
internal static class PedigreeSymbol
{
    public static FrameworkElement Make(bool male, bool affected, bool carrier, double size = 36)
    {
        Shape Outline()
        {
            Shape s = male ? new Rectangle { RadiusX = size * 0.06, RadiusY = size * 0.06 } : new Ellipse();
            s.Width = size;
            s.Height = size;
            return s;
        }
        var g = new Grid { Width = size, Height = size };
        var ground = Outline();
        ground.SetResourceReference(Shape.FillProperty, affected ? B.Text : B.Card);
        g.Children.Add(ground);
        if (!affected && carrier)
        {
            // (the left half filled: a rectangle clipped to the shape)
            var half = new Rectangle { Width = size / 2, Height = size, HorizontalAlignment = HorizontalAlignment.Left };
            half.SetResourceReference(Shape.FillProperty, B.Text);
            var clip = male ? (Geometry)new RectangleGeometry(new Rect(0, 0, size, size), size * 0.06, size * 0.06) : new EllipseGeometry(new Rect(0, 0, size, size));
            var holder = new Grid { Width = size, Height = size, Clip = clip, Children = { half } };
            g.Children.Add(holder);
        }
        var edge = Outline();
        edge.Fill = Brushes.Transparent;
        edge.StrokeThickness = Math.Max(1.2, size / 18);
        edge.SetResourceReference(Shape.StrokeProperty, B.Text);
        g.Children.Add(edge);
        g.IsHitTestVisible = false;
        return g;
    }
}

internal sealed class CrossPedigreePage : CrossesPageBase
{
    private const double Slot = 76, RowHeight = 112, Symbol = 36, Margin0 = 40;
    private readonly ContentControl left = CrossKit.Host();
    private readonly ContentControl centre = CrossKit.Host();
    private readonly ContentControl right = CrossKit.Host();
    private Border? card;

    public CrossPedigreePage()
    {
        Children.Add(BioUI.Work(left, centre, right));
        Draw();
    }

    protected override void Draw()
    {
        var p = CrossesModel.Pedigree;
        var verdicts = PedigreeChecker.Check(p);
        var labels = p.Labels;
        var hadFocus = card?.IsKeyboardFocusWithin == true;
        left.Content = Controls(p, labels);
        centre.Content = Drawing(p, verdicts);
        right.Content = Side(verdicts, labels);
        if (hadFocus) card?.Dispatcher.BeginInvoke(() => card?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    // ---- editing ----

    private static FrameworkElement Controls(Pedigree p, Dictionary<int, string> labels)
    {
        var current = Pedigree.Examples.FirstOrDefault(e => e.Pedigree.Equals(p));
        var start = CrossKit.Menu(K.Text(current?.Title ?? "Your own", Ty.BodyStrong), m =>
        {
            foreach (var e in Pedigree.Examples)
            {
                var ex = e;
                m.Items.Add(CrossKit.Item(e.Title, e == current, () =>
                {
                    CrossesModel.Select(null);
                    CrossesModel.ShowMode(null);
                    CrossesModel.SetPedigree(ex.Pedigree.Copy());
                }));
            }
        }, "A pedigree to begin with", "pedstart");
        var v = K.V(14, CrossKit.Heading("Start from"), start);
        if (current != null) v.Children.Add(CrossKit.Caption(current.Note));
        v.Children.Add(CrossKit.Heading("Selected"));
        if (CrossesModel.Selected is { } id && p.Person(id) is { } person) v.Children.Add(Editor(p, person, labels.TryGetValue(id, out var l) ? l : ""));
        else v.Children.Add(K.Wrapped("Click someone to set whether they are affected, or to add a partner or children.", Ty.Body, B.Text2));
        v.Children.Add(CrossKit.Heading("Key"));
        FrameworkElement Key(bool male, bool affected, bool carrier, string text) => K.H(8, PedigreeSymbol.Make(male, affected, carrier, 16), K.Text(text, Ty.Caption));
        v.Children.Add(K.V(6, Key(true, false, false, "Male"), Key(false, false, false, "Female"), Key(true, true, false, "Affected"), Key(false, false, true, "Known carrier")));
        v.Children.Add(K.Text($"{p.People.Count} of {Pedigree.MaxPeople} people", Ty.Caption, B.Text3));
        return v;
    }

    private static FrameworkElement Editor(Pedigree p, PedigreePerson x, string label)
    {
        var id = x.Id;
        var status = x.Affected ? 2 : (x.Carrier ? 1 : 0);
        var hasPartner = p.Partner(id) != null;
        var head = K.H(10, PedigreeSymbol.Make(x.Male, x.Affected, x.Carrier, 22), K.Text($"{label} · {(x.Male ? "male" : "female")}", Ty.BodyStrong));
        var affected = CrossKit.Segmented(new[] { "Unaffected", "Carrier", "Affected" }, status,
            i => CrossesModel.EditPedigree(q => q.Update(id, y => { y.Affected = i == 2; y.Carrier = i == 1; })),
            "Affected shows the trait; a carrier is known to have one recessive allele without showing it", stretch: true, uid: "status");
        var sex = CrossKit.Segmented(new[] { "Male", "Female" }, x.Male ? 0 : 1, i => CrossesModel.EditPedigree(q => q.Update(id, y => y.Male = i == 0)),
            p.CanChangeSex(id) ? "Male or female" : "A partner's sex is fixed: a couple is a father and a mother", stretch: true, uid: "sex");
        sex.IsEnabled = p.CanChangeSex(id);
        Wpf.Ui.Controls.Button Act(string text, SymbolRegular icon, Action a, bool enabled, string tip)
        {
            var b = K.Button(text, a, icon: icon);
            b.MinWidth = 0;
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.IsEnabled = enabled;
            b.ToolTip = tip;
            b.Uid = "add:" + text;
            return b;
        }
        var full = p.IsFull;
        var partner = Act("Partner", SymbolRegular.Heart24, () => CrossesModel.EditPedigree(q => { if (q.AddPartner(id) is { } n) CrossesModel.Select(n); }), !hasPartner && !full, hasPartner ? "Already has a partner" : "Add a partner");
        var son = Act("Son", SymbolRegular.Square24, () => CrossesModel.EditPedigree(q => q.AddChild(id, true)), hasPartner && !full, hasPartner ? "Add a son to this couple" : "Add a partner first");
        var daughter = Act("Daughter", SymbolRegular.Circle24, () => CrossesModel.EditPedigree(q => q.AddChild(id, false)), hasPartner && !full, hasPartner ? "Add a daughter to this couple" : "Add a partner first");
        var adds = K.Columns("*, 6, *, 6, *", partner.Col(0), son.Col(2), daughter.Col(4));
        var remove = ChemKit.Wide($"Remove {label} and their descendants", () =>
        {
            CrossesModel.Select(null);
            CrossesModel.EditPedigree(q => q.Remove(id));
        });
        remove.IsEnabled = p.CanRemove(id);
        remove.Uid = "remove";
        remove.ToolTip = p.CanRemove(id) ? "Take them out, with their children and a partner who joined through them" : "The first person stays";
        return K.Card(K.V(10, head, affected, sex, adds, remove), new Thickness(12), 10);
    }

    // ---- the drawing ----

    private static double X(double slot) => Margin0 + slot * Slot;
    private static double Y(int gen) => 30 + gen * RowHeight + Symbol / 2;

    private FrameworkElement Drawing(Pedigree p, List<PedigreeVerdict> verdicts)
    {
        var spots = p.Layout();
        var ringed = verdicts.FirstOrDefault(v => v.Mode == CrossesModel.ShownMode)?.Culprits.ToHashSet() ?? new HashSet<int>();
        var maxX = spots.Count == 0 ? 0 : spots.Max(s => s.X);
        var gens = (spots.Count == 0 ? 0 : spots.Max(s => s.Generation)) + 1;
        var canvas = new Canvas { Width = (maxX + 0.5) * Slot + 2 * Margin0, Height = gens * RowHeight + 40, Margin = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var lines = new Path { Data = Lines(p, spots), StrokeThickness = 1.5, Opacity = 0.75 };
        lines.SetResourceReference(Shape.StrokeProperty, B.Text);
        canvas.Children.Add(lines);
        string[] roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        for (var g = 0; g < gens; g++)
        {
            var t = K.Text(g < roman.Length ? roman[g] : $"{g + 1}", Ty.BodyStrong, B.Text2);
            Canvas.SetLeft(t, 6);
            Canvas.SetTop(t, Y(g) - 10);
            canvas.Children.Add(t);
        }
        foreach (var s in spots)
        {
            var b = Person(p, s, ringed.Contains(s.Id));
            // (the symbol's centre on its generation's line)
            Canvas.SetLeft(b, X(s.X) - (Slot - 6) / 2);
            Canvas.SetTop(b, Y(s.Generation) - (Symbol + 18) / 2);
            canvas.Children.Add(b);
        }
        AutomationProperties.SetName(canvas, $"Pedigree of {p.People.Count} people");
        var title = K.Text("Pedigree", Ty.Subtitle);
        var head = K.Columns("*, auto", title.Col(0));
        if (CrossesModel.ShownMode is { } m)
        {
            var ring = ChemKit.Line(SymbolRegular.ErrorCircle16, $"Ringed: who rules out {m.Title().ToLowerInvariant()}", K.Tone("red"));
            ring.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(ring.Col(1));
        }
        var scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        var foot = K.Text("Click a person to select them · ← → move between people · Esc lets go", Ty.Caption, B.Text2).Center();
        var g2 = K.Rows("auto, *, auto", head.Margin(16).Row(0), scroll.Row(1), foot.Margin(12).Row(2));
        card = K.Card(g2, new Thickness(0), 12);
        card.Focusable = true;
        card.FocusVisualStyle = null;
        AutomationProperties.SetName(card, "Pedigree drawing: arrow keys move between people, Escape lets go");
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is not Border && e.OriginalSource is not Button) return;
            // (the drawing keeps the focus while the arrows move the selection: the selected person is ringed)
            if (e.Key == Key.Left) { card.Focus(); Move(-1, spots); e.Handled = true; }
            else if (e.Key == Key.Right) { card.Focus(); Move(1, spots); e.Handled = true; }
            else if (e.Key == Key.Escape && CrossesModel.Selected != null) { CrossesModel.Select(null); e.Handled = true; }
        };
        card.MouseLeftButtonDown += (_, _) => card.Focus();
        return card;
    }

    private static void Move(int step, List<PedigreeSpot> spots)
    {
        var order = spots.OrderBy(s => s.Generation).ThenBy(s => s.X).Select(s => s.Id).ToList();
        if (order.Count == 0) return;
        if (CrossesModel.Selected is { } s && order.IndexOf(s) is var i && i >= 0) CrossesModel.Select(order[(i + step + order.Count) % order.Count]);
        else CrossesModel.Select(order[0]);
    }

    /// <summary>Marriage lines between partners, a line down from each couple to the line over their children, and a line
    /// down to each child.</summary>
    private static Geometry Lines(Pedigree p, List<PedigreeSpot> spots)
    {
        var at = spots.ToDictionary(s => s.Id);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            void Seg(double x0, double y0, double x1, double y1)
            {
                ctx.BeginFigure(new Point(x0, y0), false, false);
                ctx.LineTo(new Point(x1, y1), true, false);
            }
            foreach (var c in p.Couples)
            {
                if (!at.TryGetValue(c[0], out var a) || !at.TryGetValue(c[1], out var b)) continue;
                var (l, r) = a.X < b.X ? (a, b) : (b, a);
                var yy = Y(l.Generation);
                Seg(X(l.X) + Symbol / 2, yy, X(r.X) - Symbol / 2, yy);
                var kids = p.People.Where(x => x.Father == c[0] && x.Mother == c[1]).Where(x => at.ContainsKey(x.Id)).Select(x => at[x.Id]).ToList();
                if (kids.Count == 0) continue;
                var mid = (X(l.X) + X(r.X)) / 2;
                var sib = Y(l.Generation + 1) - Symbol / 2 - 24;
                Seg(mid, yy, mid, sib);
                var xs = kids.Select(k => X(k.X)).Append(mid).ToList();
                Seg(xs.Min(), sib, xs.Max(), sib);
                foreach (var k in kids) Seg(X(k.X), sib, X(k.X), Y(k.Generation) - Symbol / 2);
            }
        }
        geo.Freeze();
        return geo;
    }

    private static string Description(PedigreePerson? p)
    {
        if (p == null) return "";
        var sex = p.Male ? "male" : "female";
        return p.Affected ? $"{sex}, affected" : (p.Carrier ? $"{sex}, carrier" : $"{sex}, unaffected");
    }

    private static FrameworkElement Person(Pedigree p, PedigreeSpot s, bool ringed)
    {
        var x = p.Person(s.Id);
        var selected = CrossesModel.Selected == s.Id;
        var symbol = new Grid { Width = Symbol + 18, Height = Symbol + 18, HorizontalAlignment = HorizontalAlignment.Center };
        symbol.Children.Add(PedigreeSymbol.Make(x?.Male ?? true, x?.Affected ?? false, x?.Carrier ?? false, Symbol).Align(HorizontalAlignment.Center, VerticalAlignment.Center));
        if (ringed)
            symbol.Children.Add(new Ellipse { Width = Symbol + 16, Height = Symbol + 16, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 2, 1.5 }, Stroke = K.Brush(K.Tone("red")), IsHitTestVisible = false });
        if (selected)
        {
            var sel = new Rectangle { Width = Symbol + 10, Height = Symbol + 10, RadiusX = 9, RadiusY = 9, StrokeThickness = 2.5, IsHitTestVisible = false };
            sel.SetResourceReference(Shape.StrokeProperty, B.Accent);
            symbol.Children.Add(sel);
        }
        var label = K.Text(s.Label, Ty.Caption, B.Text2).Center();
        label.FontWeight = selected ? FontWeights.Bold : FontWeights.Medium;
        if (ringed) label.Foreground = K.Brush(K.Darkened(K.Tone("red")));
        var face = K.V(-4, symbol, label);
        var b = K.Subtle(face, () => CrossesModel.Select(selected ? null : s.Id), new Thickness(0), 8);
        b.Width = Slot - 6;
        b.Height = Symbol + 34;
        b.VerticalContentAlignment = VerticalAlignment.Top;
        b.ToolTip = $"{s.Label}: {Description(x)}";
        b.Uid = $"person:{s.Id}";
        AutomationProperties.SetName(b, $"{s.Label}, {Description(x)}{(ringed ? ", rules out the mode shown" : "")}{(selected ? ", selected" : "")}");
        return b;
    }

    // ---- the verdicts ----

    private static FrameworkElement Side(List<PedigreeVerdict> verdicts, Dictionary<int, string> labels)
    {
        var v = K.V(12, CrossKit.Heading("Which modes fit?"));
        foreach (var x in verdicts) v.Children.Add(Verdict(x, labels));
        v.Children.Add(BioUI.Caption("Consistent means some set of genotypes explains everyone — it does not prove the mode. Assumes full penetrance (everyone with the genotype shows it) and no new mutations."));
        v.Children.Add(BioUI.Card("Clues", null, null,
            BioUI.FactRow("Recessive", "An affected child of two unaffected parents."),
            BioUI.FactRow("Dominant", "An unaffected child of two affected parents; affected in every generation."),
            BioUI.FactRow("X-linked recessive", "Mostly males; an affected mother's sons are all affected; it passes through unaffected carrier mothers."),
            BioUI.FactRow("X-linked dominant", "An affected father's daughters are all affected and his sons never are.")));
        return v;
    }

    private static FrameworkElement Verdict(PedigreeVerdict v, Dictionary<int, string> labels)
    {
        var shown = CrossesModel.ShownMode == v.Mode;
        var tone = v.Consistent ? K.Tone("green") : K.Tone("red");
        var icon = K.Icon(v.Consistent ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.DismissCircle24, 18, tone, filled: true);
        var state = K.Text(v.Consistent ? "Consistent" : "Ruled out", Ty.BodyStrong);
        state.Foreground = K.Brush(K.Darkened(tone));
        var head = K.Columns("auto, *, auto", icon.Margin(0, 0, 8, 0).Col(0), K.Text(v.Mode.Title(), Ty.BodyStrong).Col(1), state.Col(2));
        var body = K.V(6, head);
        if (!v.Consistent)
        {
            body.Children.Add(K.Wrapped("By " + string.Join(", ", v.Culprits.Where(labels.ContainsKey).Select(c => labels[c])), Ty.Body));
            foreach (var r in v.Reasons) body.Children.Add(K.Wrapped(r, Ty.Caption, B.Text2));
            var mode = v.Mode;
            var ring = K.Link(shown ? "Hide them" : "Ring them", () => CrossesModel.ShowMode(shown ? null : mode));
            ring.ToolTip = $"Ring the people who rule out {v.Mode.Title().ToLowerInvariant()}";
            ring.Uid = $"ring:{v.Mode}";
            ring.HorizontalAlignment = HorizontalAlignment.Left;
            ring.Margin = new Thickness(-6, 0, 0, 0);
            body.Children.Add(ring);
        }
        AutomationProperties.SetName(body, $"{v.Mode.Title()}: {(v.Consistent ? "consistent" : "ruled out by " + string.Join(", ", v.Culprits.Where(labels.ContainsKey).Select(c => labels[c])))}");
        return Everyday.TintedCard(body, shown ? K.Tone("red") : null, new Thickness(12), 10);
    }
}

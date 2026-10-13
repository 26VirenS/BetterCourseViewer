using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Genetics Crosses (spec §4.5), the Mac's CrossesTool.swift: a cross of one to three genes — complete, incomplete and
// codominance, ABO, X-linked, lethal — with each parent's gametes, the Punnett square (or the forked-line diagram), and
// its genotypes and phenotypes kept in separate boxes (confusing the two ratios is a named exam error); the product and
// sum rules; the chi-square test with its null hypothesis written out; and a pedigree's mode checker. The arithmetic is
// exact (BioCross; Model/CrossesModel.cs, Model/PedigreeModel.cs, which keep the tool's state too); this file has the
// tool, its Cross page and the pin's quick version; CrossesPages.cs the other pages; CrossesParts.cs the pieces.

#pragma warning disable CA2255
internal static class CrossesToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("cross", (c, state) => new CrossesTool(c, state), CrossesTool.Pages, (c, state) => new CrossesCompact(c, state));
}
#pragma warning restore CA2255

/// <summary>Genetics Crosses: its four pages, the one chosen in its header shown.</summary>
internal sealed class CrossesTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("cross", "Cross", SymbolRegular.Grid24, "A cross and its Punnett square"),
        new("probability", "Probability", SymbolRegular.Calculator24, "The product and sum rules"),
        new("chi", "Chi-square", SymbolRegular.DataBarVertical24, "Observed against expected"),
        new("pedigree", "Pedigree", SymbolRegular.PeopleTeam24, "Which inheritance a pedigree fits"),
    };

    private readonly ToolContext c;
    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private string? shown;

    /// <summary>The tool's settings kept in Prefs (SimplBio.cross.*), read back once.</summary>
    public static void Persist() => CrossesModel.Persist(k => BioPrefs.String("cross", k), (k, v) => BioPrefs.Set("cross", k, v));

    public CrossesTool(ToolContext c, BioLaunchState? state)
    {
        this.c = c;
        Persist();
        if (state != null) CrossesModel.ApplyLaunch(state.Value, state.Has);
        if (CrossesModel.TakeHandoff(c.TakeInfo) is { } page) BioNav.Set("cross", page);
        Children.Add(host);
        Show();
        void Nav(string key) { if (key == "cross") Show(); }
        Loaded += (_, _) => { BioNav.Changed -= Nav; BioNav.Changed += Nav; Show(); };
        Unloaded += (_, _) => BioNav.Changed -= Nav;
    }

    private void Show()
    {
        var page = BioNav.Page("cross");
        if (page == shown) return;
        shown = page;
        FrameworkElement view = page switch
        {
            "probability" => new CrossProbabilityPage(),
            "chi" => new CrossChiSquarePage(),
            "pedigree" => new CrossPedigreePage(),
            _ => new CrossPage(c),
        };
        host.Content = view;
        Motion.FadeIn(view, Motion.Medium);
    }
}

/// <summary>
/// The Cross page: the genes and parents on the left (a typed cross, or each gene's letter, pattern, example and the
/// parents' genotypes from menus that list only what a parent can have), the gametes and the square (or forked line) in
/// the middle, the genotypes and the phenotypes on the right, in boxes of their own.
/// </summary>
internal sealed class CrossPage : Grid
{
    private readonly ToolContext c;
    private readonly TextBox typed;
    private readonly ContentControl typedNote = CrossKit.Host();
    private readonly ContentControl genes = CrossKit.Host();
    private readonly Wpf.Ui.Controls.Button addGene;
    private readonly ContentControl notes = CrossKit.Host();
    private readonly ContentControl centre = CrossKit.Host();
    private readonly ContentControl right = CrossKit.Host();
    private bool syncing;

    public CrossPage(ToolContext c)
    {
        this.c = c;
        typed = ToolKit.Box(CrossesModel.Typed, "AaBb × aaBb", t => { if (!syncing) CrossesModel.Typed = t; });
        typed.FontSize = 15;
        typed.ToolTip = "Type a cross and press Enter (Ctrl+F): AaBb × aaBb, X^A X^a × X^a Y, I^A i × I^B i, C^R C^W × C^R C^W";
        AutomationProperties.SetName(typed, "Cross, typed");
        typed.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            CrossesModel.Typed = typed.Text;
            CrossesModel.ApplyTyped();
            // (read: the box shows the cross as the tool writes it, "Aa x aa" as "Aa × aa")
            if (CrossesModel.TypedError == null && typed.Text != CrossesModel.Typed)
            {
                syncing = true;
                typed.Text = CrossesModel.Typed;
                typed.CaretIndex = typed.Text.Length;
                syncing = false;
            }
        };
        addGene = ChemKit.Wide("Add a gene", () => CrossesModel.Edit(s => s.AddGene()), icon: SymbolRegular.Add24);
        addGene.ToolTip = "Up to three genes, each on its own pair of chromosomes";
        var test = ChemKit.Wide("Cross parent 2 with the recessive", CrossesModel.TestCross);
        test.ToolTip = "Make parent 2 homozygous recessive for every gene";
        var left = K.V(14,
            CrossKit.Heading("Cross"), K.V(6, typed, typedNote),
            CrossKit.Heading("Genes"), genes, addGene,
            CrossKit.Heading("Test cross"), test,
            CrossKit.Caption("A test cross finds an unknown genotype: crossed with aa, an AA parent gives all dominant offspring, an Aa parent 1 dominant : 1 recessive."),
            notes);
        Children.Add(BioUI.Work(left, centre, right));
        CrossKit.FindKey(this, () => typed);
        Draw();
        void Changed() => CrossKit.KeepFocus(this, Draw);
        Loaded += (_, _) => { CrossesModel.Changed -= Changed; CrossesModel.Changed += Changed; Appearance.Changed -= Changed; Appearance.Changed += Changed; Draw(); };
        Unloaded += (_, _) => { CrossesModel.Changed -= Changed; Appearance.Changed -= Changed; };
    }

    private void Draw()
    {
        var setup = CrossesModel.Setup;
        syncing = true;
        if (typed.Text != CrossesModel.Typed && !typed.IsKeyboardFocusWithin) typed.Text = CrossesModel.Typed;
        syncing = false;
        CrossKit.Wrong(typed, CrossesModel.TypedError != null);
        typedNote.Content = CrossesModel.TypedError is { } e
            ? CrossKit.Problem(e)
            : CrossKit.Caption("Type one with × or x between the parents, and ^ before a superscript. Enter reads it.");
        var editors = K.V(10);
        for (var k = 0; k < setup.Genes.Count; k++) editors.Children.Add(GeneEditor(setup, k));
        genes.Content = editors;
        addGene.IsEnabled = setup.Genes.Count < CrossSetup.MaxGenes;
        var ns = K.V(6);
        foreach (var n in CrossesModel.Notes) ns.Children.Add(CrossKit.Note(n));
        notes.Content = ns;
        centre.Content = Centre(setup);
        right.Content = CrossResults.Make(setup);
    }

    // ---- a gene's editor ----

    /// <summary>One gene of the cross: its letter, its pattern, an example from §5.3.1, and each parent's genotype (menus
    /// listing only the genotypes that parent can have).</summary>
    private static FrameworkElement GeneEditor(CrossSetup setup, int k)
    {
        var g = setup.Genes[k];
        var x = setup.XIndex != null;
        var letterFace = K.Text(g.Letter.ToString(), Ty.BodyStrong);
        letterFace.FontSize = 16;
        var letter = CrossKit.Menu(letterFace, m =>
        {
            foreach (var l in setup.FreeLetters(k))
            {
                var ch = l;
                m.Items.Add(CrossKit.Item(l.ToString(), l == g.Letter, () => CrossesModel.Edit(s => s.SetLetter(ch, k))));
            }
        }, g.Pattern == BioInheritance.Abo ? "The ABO gene is I" : "The gene's letter", $"letter:{k}");
        letter.IsEnabled = g.Pattern != BioInheritance.Abo;
        letter.Width = 64;
        AutomationProperties.SetName(letter, $"Gene letter {g.Letter}");
        var pattern = CrossKit.Menu(K.Text(g.Pattern.Label(), Ty.BodyStrong), m =>
        {
            foreach (var p in Enum.GetValues<BioInheritance>())
            {
                var pp = p;
                m.Items.Add(CrossKit.Item(p.Label(), p == g.Pattern, () => CrossesModel.Edit(s => s.SetPattern(pp, k)), setup.Allowed(p, k)));
            }
        }, "How the gene's alleles show", $"pattern:{k}");
        AutomationProperties.SetName(pattern, $"Pattern: {g.Pattern.Label()}");
        var top = K.Columns("auto, 8, *, auto", letter.Col(0), pattern.Col(2));
        if (setup.Genes.Count > 1)
        {
            var remove = K.IconButton(SymbolRegular.SubtractCircle24, () => CrossesModel.Edit(s => s.RemoveGene(k)), $"Take gene {g.Letter} out of the cross");
            remove.Margin = new Thickness(6, 0, 0, 0);
            remove.Uid = $"remove:{k}";
            top.Children.Add(remove.Col(3));
        }
        var current = k < setup.Examples.Count ? setup.Examples[k] : null;
        var exampleTitle = CrossExamples.Find(current)?.Title;
        var exampleFace = K.Text(exampleTitle ?? "Example…", Ty.Body, exampleTitle == null ? B.Text2 : B.Text);
        var example = CrossKit.Menu(exampleFace, m =>
        {
            foreach (var e in CrossExamples.All)
            {
                var ee = e;
                m.Items.Add(CrossKit.Item(e.Title, e.Id == current, () => CrossesModel.Edit(s => s.ApplyExample(ee, k)), setup.Allowed(e.Gene.Pattern, k)));
            }
        }, "A gene from the textbook: its alleles, pattern and usual parents", $"example:{k}");
        var v = K.V(10, top, example, ParentRow(setup, k, 1, x ? "♀ Mother" : "Parent 1"), ParentRow(setup, k, 2, x ? "♂ Father" : "Parent 2"));
        return K.Card(v, new Thickness(12), 10);
    }

    private static FrameworkElement ParentRow(CrossSetup setup, int k, int parent, string title)
    {
        var g = setup.Genes[k];
        var current = parent == 1 ? setup.Parent1[k] : setup.Parent2[k];
        var face = K.H(8, CrossInk.Text(g.Text(current), 15, FontWeights.Bold), K.Text(g.Phenotype(current).Label, Ty.Caption, B.Text2));
        face.VerticalAlignment = VerticalAlignment.Center;
        var menu = CrossKit.Menu(face, m =>
        {
            foreach (var gt in setup.Genotypes(k, parent))
            {
                var pick = gt;
                var row = K.H(10, CrossInk.Text(g.Text(gt), 14, FontWeights.SemiBold), K.Text(g.Phenotype(gt).Label, Ty.Caption, B.Text2));
                var item = new System.Windows.Controls.MenuItem { Header = row, IsChecked = gt.SequenceEqual(current) };
                AutomationProperties.SetName(item, $"{CrossText.Spoken(g.Text(gt))}, {g.Phenotype(gt).Label}");
                item.Click += (_, _) => CrossesModel.Edit(s =>
                {
                    if (parent == 1) s.Parent1[k] = pick.OrderBy(a => a).ToArray(); else s.Parent2[k] = pick.OrderBy(a => a).ToArray();
                });
                m.Items.Add(item);
            }
        }, $"{title}'s genotype for gene {g.Letter}", $"parent:{k}:{parent}");
        AutomationProperties.SetName(menu, $"{title}: {CrossText.Spoken(g.Text(current))}, {g.Phenotype(current).Label}");
        var label = K.Text(title, Ty.Caption, B.Text2).Strong();
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Width = 70;
        return K.Columns("auto, *", label.Col(0), menu.Col(1));
    }

    // ---- the centre ----

    private FrameworkElement Centre(CrossSetup setup)
    {
        var cross = setup.Cross;
        var title = CrossInk.Text(setup.Text, 22, FontWeights.Bold);
        AutomationProperties.SetName(title, "Cross: " + CrossText.Spoken(setup.Text));
        var diagram = CrossKit.Segmented(new[] { "Punnett square", "Forked line" }, CrossesModel.ShownDiagram == CrossesModel.Diagram.Square ? 0 : 1,
            i => CrossesModel.SetDiagram(i == 0 ? CrossesModel.Diagram.Square : CrossesModel.Diagram.Forked), "The whole square, or each gene's outcomes multiplied along branches", uid: "diagram");
        diagram.VerticalAlignment = VerticalAlignment.Center;
        var head = K.Columns("*, auto", new Viewbox { Child = title, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left }.Col(0), diagram.Margin(12, 0, 0, 0).Col(1));
        var body = K.V(18, Gametes(setup, cross));
        if (CrossesModel.WhyShown) body.Children.Add(new AssortmentPanel(c, setup));
        if (CrossesModel.ShownDiagram == CrossesModel.Diagram.Square)
        {
            body.Children.Add(CrossKit.Sideways(new PunnettGrid(new CrossPunnett(cross))));
            if (cross.Gametes1.Count * cross.Gametes2.Count >= 64)
                body.Children.Add(CrossKit.Caption("64 cells: the forked line shows the same thing more clearly."));
        }
        else body.Children.Add(Forked(cross));
        if (Legend(cross.Result()) is { } legend) body.Children.Add(legend);
        int n1 = cross.Gametes1.Count, n2 = cross.Gametes2.Count;
        var foot = K.Wrapped($"{n1} × {n2} = {ToolKit.Plural(n1 * n2, "cell")}, each as likely as the next. Point at a cell to light every cell with its genotype.", Ty.Caption, B.Text2).Center();
        var scroll = CrossKit.Scroller(body, new Thickness(18, 0, 18, 18));
        var g = K.Rows("auto, *, auto", head.Margin(16).Row(0), scroll.Row(1), foot.Margin(12).Row(2));
        return K.Card(g, new Thickness(0), 12);
    }

    /// <summary>Each parent's gametes (side by side while they are few, one over the other when there are eight), and "Why?".</summary>
    private static FrameworkElement Gametes(CrossSetup setup, BioCross cross)
    {
        var x = setup.XIndex != null;
        var g1 = cross.Gametes1.Select(g => new CrossGamete(cross.GameteText(g.Gamete), g.P)).ToList();
        var g2 = cross.Gametes2.Select(g => new CrossGamete(cross.GameteText(g.Gamete), g.P)).ToList();
        var first = GameteRow.Make(x ? "♀ Mother" : "Parent 1", cross.GenotypeText(cross.Parent1), g1, BioPalette.Maternal.ToColor());
        var second = GameteRow.Make(x ? "♂ Father" : "Parent 2", cross.GenotypeText(cross.Parent2), g2, BioPalette.Paternal.ToColor());
        var why = K.Link(CrossesModel.WhyShown ? "Hide why" : "Why these gametes?", () => CrossesModel.SetWhyShown(!CrossesModel.WhyShown));
        why.ToolTip = "How meiosis makes them: segregation and independent assortment";
        why.Uid = "why";
        why.HorizontalAlignment = HorizontalAlignment.Left;
        if (Math.Max(g1.Count, g2.Count) > 4) return K.V(12, first, second, why);
        var row = new WrapPanel();
        row.Children.Add(first.Margin(0, 0, 24, 8));
        row.Children.Add(second.Margin(0, 0, 24, 8));
        why.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(why);
        return row;
    }

    private static FrameworkElement Forked(BioCross cross)
    {
        var line = new CrossForkedLine(cross, CrossesModel.ForkedByGenotype);
        var by = CrossKit.Segmented(new[] { "Phenotypes", "Genotypes" }, CrossesModel.ForkedByGenotype ? 1 : 0, i => CrossesModel.SetForkedByGenotype(i == 1), uid: "forkedby");
        var tints = CrossesModel.ForkedByGenotype ? new Dictionary<string, Color>() : CrossColors.Tints(cross.Result());
        var words = CrossesModel.ForkedByGenotype
            ? $"Each gene's genotypes from its own small square, multiplied along each branch: {line.Leaves.Count} genotypes."
            : "Each gene's phenotypes from its own small square, multiplied along each branch (the product rule).";
        return K.V(10, by.Align(HorizontalAlignment.Left), CrossKit.Sideways(ForkedLineView.Make(line, cross.Genes, tints)), CrossKit.Caption(words));
    }

    private static FrameworkElement? Legend(BioCrossResult r)
    {
        var dies = r.Genotypes.Any(g => g.Dies);
        if (r.Phenotypes.Count <= 1 && !dies) return null;
        var row = new WrapPanel();
        for (var i = 0; i < r.Phenotypes.Count; i++)
        {
            var sw = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = K.Brush(K.WithAlpha(CrossColors.Phenotype(i, r.Phenotypes[i].Text), 0.6)), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(K.H(6, sw, CrossInk.Text(r.Phenotypes[i].Text, 12, FontWeights.Medium)).Margin(0, 0, 14, 4));
        }
        if (dies)
        {
            var sw = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = CrossColors.Hatching(), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(K.H(6, sw, K.Text("dies before birth", Ty.Caption)).Margin(0, 0, 14, 4));
        }
        AutomationProperties.SetName(row, "Key: " + string.Join(", ", r.Phenotypes.Select(p => CrossText.Spoken(p.Text))) + (dies ? ", hatched: dies before birth" : ""));
        return row;
    }
}

/// <summary>The right column: genotypes and phenotypes in boxes of their own (a named exam error is confusing their
/// ratios), daughters and sons apart for an X-linked gene, the dead counted apart from the born.</summary>
internal static class CrossResults
{
    public static FrameworkElement Make(CrossSetup setup)
    {
        var c = setup.Cross;
        var all = c.Result();
        var tints = CrossColors.Tints(all);
        var v = K.V(14);
        if (c.HasXLinked)
        {
            BioCrossResult d = c.Result(BioSex.Female), s = c.Result(BioSex.Male);
            v.Children.Add(Genotypes(new (string?, BioCrossResult)[] { ("Daughters", d), ("Sons", s) }, all));
            v.Children.Add(Phenotypes(new (string?, BioCrossResult)[] { ("Daughters", d), ("Sons", s), ("All offspring", all) }, tints));
        }
        else
        {
            v.Children.Add(Genotypes(new (string?, BioCrossResult)[] { (null, all) }, all));
            v.Children.Add(Phenotypes(new (string?, BioCrossResult)[] { (null, all) }, tints));
        }
        v.Children.Add(BioUI.Caption("Genotypes count allele combinations; phenotypes count what shows. 1 AA : 2 Aa : 1 aa is one cross's genotypic ratio, and 3 : 1 its phenotypic ratio."));
        return v;
    }

    private static FrameworkElement Genotypes((string? Name, BioCrossResult R)[] sections, BioCrossResult whole)
    {
        var rows = new List<UIElement?>();
        for (var i = 0; i < sections.Length; i++)
        {
            var (name, r) = sections[i];
            var block = K.V(8);
            if (name != null) block.Children.Add(K.Text(name, Ty.Caption, B.Text2).Strong());
            var ratio = CrossInk.Wrapped(r.Genotypes.Count == 1 ? "all " + r.Genotypes[0].Text : r.GenotypicRatioText, 16, FontWeights.SemiBold);
            AutomationProperties.SetName(ratio, "Genotypic ratio: " + CrossText.Spoken(r.GenotypicRatioText));
            block.Children.Add(ratio);
            if (r.Genotypes.Any(g => g.Dies))
            {
                var born = K.Wrapped($"Born: {BioRatio.Text(r.BornGenotypicRatio)} — {(BioFrac.One - r.BornFraction).Text} die before birth", Ty.Caption).Strong();
                born.Foreground = K.Brush(K.Darkened(K.Tone("orange")));
                block.Children.Add(born);
            }
            foreach (var g in r.Genotypes) block.Children.Add(GenotypeRow(g, name == null ? whole.SquareSize : null));
            rows.Add(block);
            if (i < sections.Length - 1) rows.Add(K.Divider());
        }
        return BioUI.Card("Genotypes", null, "Genotypic ratio", rows.ToArray());
    }

    private static FrameworkElement GenotypeRow(BioCrossResult.GenotypeRow g, int? total)
    {
        var text = CrossInk.Text(g.Text, 14, FontWeights.SemiBold);
        if (g.Dies) text.TextDecorations = TextDecorations.Strikethrough;
        var words = K.H(8, text);
        if (g.Dies)
        {
            var d = K.Text("dies", Ty.Caption);
            d.Foreground = K.Brush(K.Darkened(K.Tone("orange")));
            words.Children.Add(d);
        }
        if (g.Carrier) words.Children.Add(K.Text("carrier", Ty.Caption, B.Text2));
        var count = K.Text(total is { } t ? $"{g.Count}/{t}" : "", Ty.Caption, B.Text3).Tabular();
        var p = K.Text(g.P.Text, Ty.BodyStrong).Tabular();
        var pct = K.Text(g.P.PercentText, Ty.Caption, B.Text2).Tabular();
        pct.Width = 48;
        pct.TextAlignment = TextAlignment.Right;
        foreach (var e in new FrameworkElement[] { count, p, pct }) e.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("*, auto, auto, auto", words.Col(0), count.Margin(4, 0, 8, 0).Col(1), p.Col(2), pct.Col(3));
        AutomationProperties.SetName(row, $"{CrossText.Spoken(g.Text)}{(g.Dies ? ", dies" : "")}: {g.P.Text}, {g.P.PercentText}");
        return row;
    }

    private static FrameworkElement Phenotypes((string? Name, BioCrossResult R)[] sections, Dictionary<string, Color> tints)
    {
        var rows = new List<UIElement?>();
        for (var i = 0; i < sections.Length; i++)
        {
            var (name, r) = sections[i];
            var block = K.V(8);
            if (name != null) block.Children.Add(K.Text(name, Ty.Caption, B.Text2).Strong());
            if (r.Phenotypes.Count == 1) block.Children.Add(CrossInk.Wrapped("all " + r.Phenotypes[0].Text, 20, FontWeights.Bold));
            else
            {
                block.Children.Add(CrossKit.Figures(BioRatio.Text(r.PhenotypicRatio), 20, FontWeights.Bold));
                block.Children.Add(CrossInk.Wrapped(r.PhenotypicRatioText, 13, FontWeights.Medium, B.Text2));
            }
            foreach (var p in r.Phenotypes) block.Children.Add(PhenotypeRow(p, tints.TryGetValue(p.Text, out var tc) ? tc : BioPalette.Coil.ToColor()));
            AutomationProperties.SetName(block, $"{name ?? "Phenotypic ratio"}: {CrossText.Spoken(r.PhenotypicRatioText)}");
            rows.Add(block);
            if (i < sections.Length - 1) rows.Add(K.Divider());
        }
        return BioUI.Card("Phenotypes", null, "Phenotypic ratio (of those born)", rows.ToArray());
    }

    private static FrameworkElement PhenotypeRow(BioCrossResult.PhenotypeRow p, Color tint)
    {
        var sw = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Background = K.Brush(K.WithAlpha(tint, 0.7)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var name = CrossInk.Text(p.Text, 14, FontWeights.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Center;
        var f = K.Text(p.P.Text, Ty.BodyStrong).Tabular();
        var pct = K.Text(p.P.PercentText, Ty.Caption, B.Text2).Tabular();
        pct.Width = 48;
        pct.TextAlignment = TextAlignment.Right;
        pct.VerticalAlignment = VerticalAlignment.Center;
        f.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("auto, *, auto, auto", sw.Col(0), name.Col(1), f.Margin(8, 0, 0, 0).Col(2), pct.Col(3));
        if (p.Carriers.IsZero) return row;
        return K.V(2, row, K.Text($"{(p.Carriers / p.P).Text} of these are carriers", Ty.Caption, B.Text2).Margin(19, 0, 0, 0));
    }
}

/// <summary>
/// Genetics Crosses in its pin's flyout (spec §3.2 item 3): a monohybrid cross — each parent AA, Aa or aa — its square and
/// both ratios; Open (Enter) takes the cross to the whole tool.
/// </summary>
internal sealed class CrossesCompact : ContentControl
{
    /// <summary>The two parents as picked, kept while the flyout comes and goes.</summary>
    private static int[] kept1 = { 0, 1 }, kept2 = { 0, 1 };
    private static readonly int[][] Options = { new[] { 0, 0 }, new[] { 0, 1 }, new[] { 1, 1 } };

    private readonly ToolContext c;

    public CrossesCompact(ToolContext c, BioLaunchState? state)
    {
        this.c = c;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        // (a launch's cross, as the pin's picture asks: --pin-open cross --tool-state cross=Aa x aa)
        if (state?.Value("cross") is { } typed && BioCross.Parse(typed).Cross is { Genes.Count: 1 } one && one.Genes[0].Pattern == BioInheritance.Complete)
        {
            kept1 = one.Parent1[0];
            kept2 = one.Parent2[0];
        }
        Draw();
    }

    private void Draw()
    {
        var setup = new CrossSetup(new[] { new BioGene('A') }, new[] { kept1 }, new[] { kept2 });
        var cross = setup.Cross;
        var r = cross.Result();
        var gene = setup.Genes[0];
        FrameworkElement Picker(int[] value, string label, Action<int[]> set)
        {
            var i = Array.FindIndex(Options, o => o.SequenceEqual(value));
            var seg = CrossKit.Segmented(Options.Select(o => gene.Text(o)).ToList(), i, n => { set(Options[n]); CrossKit.KeepFocus(this, Draw); }, $"{label}'s genotype", uid: label);
            AutomationProperties.SetName(seg, label);
            return seg;
        }
        var times = K.Text("×", Ty.BodyLarge, B.Text2).Strong();
        times.VerticalAlignment = VerticalAlignment.Center;
        var parents = K.H(10, Picker(kept1, "Parent 1", v => kept1 = v), times, Picker(kept2, "Parent 2", v => kept2 = v));
        var genotypes = K.V(2, K.Text("Genotypes", Ty.Caption, B.Text2).Strong(), CrossInk.Wrapped(r.Genotypes.Count == 1 ? "all " + r.Genotypes[0].Text : r.GenotypicRatioText, 14, FontWeights.SemiBold));
        var phenotypes = K.V(2, K.Text("Phenotypes", Ty.Caption, B.Text2).Strong(), CrossInk.Wrapped(r.Phenotypes.Count == 1 ? "all " + r.Phenotypes[0].Text : r.PhenotypicRatioText, 14, FontWeights.SemiBold));
        var ratios = K.V(8, genotypes, phenotypes);
        var middle = K.Columns("auto, 14, *", new PunnettGrid(new CrossPunnett(cross), small: true).Col(0), ratios.Col(2));
        var open = K.Button("Open in Genetics Crosses", () => c.OpenFull(new Dictionary<string, string> { ["cross"] = setup.Text }, null), accent: true);
        open.HorizontalAlignment = HorizontalAlignment.Stretch;
        open.HorizontalContentAlignment = HorizontalAlignment.Center;
        open.MinHeight = 36;
        open.IsDefault = true;
        open.ToolTip = "This cross in the whole tool, with dihybrids, other patterns, probability and chi-square (Enter)";
        Content = K.V(12, parents, middle, open);
    }
}

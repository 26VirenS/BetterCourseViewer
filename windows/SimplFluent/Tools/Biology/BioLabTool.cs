using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Bio Lab Calculator (spec §4.8, §5.5): lab mathematics and the AP Equations and Formulas sheet, worked as on paper.
// Six pages, picked in the header — Solutions, Microscopy, Cells, Statistics, Populations, Molecules — each a column or
// two of calculators as cards. Every card shows its formula, its fields with unit menus (the SI prefixes), the answer to
// sensible significant figures (3 unless the setting at the top says otherwise, never more than the least precise input
// plus one), the values substituted with their units, and a "Show steps" with the unit conversions; a field out of range
// turns red with the reason. What it keeps (the modes, texts and units, steps open, the figures) is BioLabModel's, so it
// outlives this view, and is remembered in Prefs (SimplBio.biolab.*). The pin's quick version is C₁V₁ = C₂V₂ and molarity
// (BioLabCompact). Launch state (--tool biolab --tool-state <page>:calc=<id>:mode=<id>:steps:sf=<n>:<field>=<text>, with
// digits=<n> as the Mac's name for sf, and underscores in a text for spaces) opens a page and a calculator as the
// pictures want them.

#pragma warning disable CA2255
internal static class BioLabToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("biolab", (c, state) => new BioLabTool(c, state), BioLabTool.Pages, (c, state) => new BioLabCompact(c, state));
}
#pragma warning restore CA2255

/// <summary>Bio Lab Calculator: its pages of calculators.</summary>
internal sealed class BioLabTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("solutions", "Solutions", SymbolRegular.Drop24, "Molarity, dilutions and percent solutions"),
        new("microscopy", "Microscopy", SymbolRegular.Microscope24, "Magnification, field of view and size"),
        new("cells", "Cells", SymbolRegular.CircleSmall24, "Surface area to volume, water potential, cell counts"),
        new("stats", "Statistics", SymbolRegular.DataBarVertical24, "Mean, standard deviation, error bars, chi-square"),
        new("ecology", "Populations", SymbolRegular.LeafTwo24, "Growth, rate and diversity"),
        new("molecules", "Molecules", SymbolRegular.Molecule24, "Absorbance, PCR, centrifuges, pH and Q₁₀"),
    };

    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private string? shown;

    public BioLabTool(ToolContext c, BioLaunchState? state)
    {
        BioLabStore.Load();
        // (handed over by the pin's quick version: the calculator begun there)
        if (c.TakeInfo("calc") is { } calc && LabCalcs.Find(calc) is { } hc)
        {
            BioNav.Set("biolab", hc.Page, remember: false);
            BioLabModel.SetSteps(hc.Id, true);
            BioLabModel.Focus = hc.Id;
        }
        if (state != null) FromLaunch(state);
        Children.Add(host);
        void Nav(string key) { if (key == "biolab") Draw(); }
        Loaded += (_, _) => { BioNav.Changed -= Nav; BioNav.Changed += Nav; Draw(); };
        Unloaded += (_, _) => BioNav.Changed -= Nav;
        Draw();
    }

    /// <summary>The pictures' state: the figures, a calculator's mode, fields and steps, and the calculator to show.</summary>
    private static void FromLaunch(BioLaunchState state)
    {
        foreach (var k in new[] { "sf", "digits" })
            if (int.TryParse(state.Value(k), out var sf)) BioLabModel.Figures = Math.Clamp(sf, 2, 6);
        if (state.Value("calc") is not { } id || LabCalcs.Find(id) is not { } calc)
        {
            // (steps with no calculator named: every card's on the page, as the Mac's)
            if (state.Has("steps")) foreach (var each in LabCalcs.On(BioNav.Page("biolab"))) BioLabModel.SetSteps(each.Id, true);
            return;
        }
        BioNav.Set("biolab", calc.Page, remember: false);
        if (state.Value("mode") is { } mode) BioLabModel.SetMode(calc.Id, mode);
        foreach (var f in calc.Fields)
            if (state.Value(f.Id) is { } text && !f.Id.Equals("calc", StringComparison.OrdinalIgnoreCase) && !f.Id.Equals("mode", StringComparison.OrdinalIgnoreCase))
                BioLabModel.SetText(calc.Id, f.Id, text.Replace('_', ' '));
        if (state.Has("steps")) BioLabModel.SetSteps(calc.Id, true);
        BioLabModel.Focus = calc.Id;
    }

    private void Draw()
    {
        var page = BioNav.Page("biolab");
        if (page == shown) return;
        shown = page;
        var view = new LabPageView(page);
        host.Content = view;
        Motion.FadeIn(view, Motion.Normal);
    }
}

/// <summary>The Bio Lab Calculator's remembered state (Prefs SimplBio.biolab.figures and SimplBio.biolab.calc.&lt;id&gt;):
/// read once, written as it changes.</summary>
internal static class BioLabStore
{
    private static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        var f = BioPrefs.Double("biolab", "figures", 3);
        if (f is >= 2 and <= 6) BioLabModel.Figures = (int)f;
        foreach (var c in LabCalcs.All) BioLabModel.Load(c.Id, BioPrefs.String("biolab", "calc." + c.Id));
        BioLabModel.Changed += Save;
    }

    private static void Save(string id)
    {
        if (id.Length == 0) BioPrefs.Set("biolab", "figures", (double)BioLabModel.Figures);
        else BioPrefs.Set("biolab", "calc." + id, BioLabModel.Save(id));
    }
}

/// <summary>A page of the Bio Lab Calculator: its line and the significant-figures setting, then its calculators in one
/// column or two.</summary>
internal sealed class LabPageView : ScrollViewer
{
    private readonly string page;
    private readonly ComboBox figures = new() { Width = 72, MinWidth = 0 };
    private readonly List<LabCalcCard> cards = new();

    public LabPageView(string page)
    {
        this.page = page;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Focusable = false;
        Padding = new Thickness(40, 4, 40, 40);
        SizeChanged += (_, _) => Padding = ActualWidth < 560 ? new Thickness(20, 4, 20, 32) : new Thickness(40, 4, 40, 40);

        var info = BioLabTool.Pages.FirstOrDefault(p => p.Id == page);
        for (var n = 2; n <= 6; n++) figures.Items.Add(new ComboBoxItem { Content = $"{n}", Tag = n });
        figures.SelectedIndex = BioLabModel.Figures - 2;
        figures.SelectionChanged += (_, _) => { if (figures.SelectedItem is ComboBoxItem { Tag: int n }) BioLabModel.Figures = n; };
        figures.ToolTip = "Answers have this many significant figures — never more than the least precise value typed, plus one.";
        AutomationProperties.SetName(figures, "Significant figures");
        var sf = K.H(8, K.Text("Significant figures", Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center), figures);
        var head = K.Columns("*, auto", BioUI.Caption(PageLine(page, info?.Help)).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), sf.Col(1));
        head.Margin = new Thickness(0, 0, 0, 16);

        var columns = new LabColumns();
        foreach (var calc in LabCalcs.On(page))
        {
            var card = new LabCalcCard(calc);
            cards.Add(card);
            columns.Children.Add(card);
        }
        var foot = BioUI.Caption("Formulas from lab mathematics and the AP Biology Equations and Formulas sheet. Every answer is worked as on paper: press “Show steps” to see the units converted and the rounding.");
        foot.Margin = new Thickness(0, 18, 0, 0);
        var body = K.V(0, head, columns, foot);
        body.MaxWidth = 1480;
        body.HorizontalAlignment = HorizontalAlignment.Stretch;
        Content = body;
        AutomationProperties.SetName(this, $"Bio Lab Calculator, {info?.Title ?? page}");

        void Changed(string id)
        {
            if (id.Length == 0 && figures.SelectedIndex != BioLabModel.Figures - 2) figures.SelectedIndex = BioLabModel.Figures - 2;
        }
        Loaded += (_, _) =>
        {
            BioLabModel.Changed -= Changed;
            BioLabModel.Changed += Changed;
            // (a calculator asked for — from the pin, or a launch — brought into view once)
            if (BioLabModel.Focus is { } id && cards.FirstOrDefault(c => c.Calc.Id == id) is { } card)
            {
                BioLabModel.Focus = null;
                Dispatcher.BeginInvoke(() => { card.BringIntoView(new Rect(0, -12, card.ActualWidth, card.ActualHeight + 24)); card.FocusFirst(); }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };
        Unloaded += (_, _) => BioLabModel.Changed -= Changed;
    }

    private static string PageLine(string page, string? help) => page switch
    {
        "solutions" => "Molarity, dilutions, serial dilutions and plate counts, percent solutions and units.",
        "microscopy" => "Magnification, field of view, specimen size and scale bars.",
        "cells" => "Surface area to volume, water potential and hemocytometer counts.",
        "stats" => "Mean, median, mode, range, standard deviation, standard error, error bars and chi-square.",
        "ecology" => "Rates of change, exponential and logistic growth, and Simpson’s diversity index.",
        "molecules" => "Beer–Lambert, nucleic acids from A₂₆₀, PCR, centrifuges, bacterial growth, pH and Q₁₀.",
        _ => help ?? "",
    };
}

/// <summary>
/// Cards in one column, or two side by side from 980 wide: each card goes to the shorter column when the columns are
/// laid out, and stays there while they keep their number — a card that grows (its steps opened) doesn't jump across.
/// </summary>
internal sealed class LabColumns : Panel
{
    private const double Gap = 20, Wide = 980;
    private int laidFor = -1;
    private int laidCount = -1;
    private int[] column = Array.Empty<int>();

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? 1000 : available.Width;
        var cols = w >= Wide ? 2 : 1;
        var colW = (w - Gap * (cols - 1)) / cols;
        foreach (UIElement c in InternalChildren) c.Measure(new Size(colW, double.PositiveInfinity));
        if (cols != laidFor || InternalChildren.Count != laidCount)
        {
            laidFor = cols;
            laidCount = InternalChildren.Count;
            column = new int[InternalChildren.Count];
            var heights = new double[cols];
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                var k = Array.IndexOf(heights, heights.Min());
                column[i] = k;
                heights[k] += InternalChildren[i].DesiredSize.Height + Gap;
            }
        }
        var h = new double[cols];
        for (var i = 0; i < InternalChildren.Count; i++) h[column[i]] += InternalChildren[i].DesiredSize.Height + Gap;
        return new Size(w, Math.Max(0, h.DefaultIfEmpty(0).Max() - Gap));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cols = Math.Max(1, laidFor);
        var colW = (final.Width - Gap * (cols - 1)) / cols;
        var y = new double[cols];
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            var k = i < column.Length ? column[i] : 0;
            c.Arrange(new Rect(k * (colW + Gap), y[k], colW, c.DesiredSize.Height));
            y[k] += c.DesiredSize.Height + Gap;
        }
        return final;
    }
}

/// <summary>
/// A calculator as a card: its title and line, the choice of what it solves for, its formula, its fields with unit
/// menus, the answer (its unit chosen beside it), the values substituted, notes, a table or chart if it has one, and
/// "Show steps". The fields are made once for each mode, so typing never loses its place; the answer is drawn again
/// as each character goes in.
/// </summary>
internal sealed class LabCalcCard : ContentControl
{
    public LabCalc Calc { get; }
    private readonly string id;
    private readonly Stack fields = K.V(12);
    private readonly ContentControl modes = new() { Focusable = false };
    private readonly TextBlock formula = new() { FontFamily = K.DisplayFace, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly ContentControl answer = new() { Focusable = false };
    private readonly ContentControl extra = new() { Focusable = false };
    private readonly ContentControl steps = new() { Focusable = false };
    private readonly Dictionary<string, (TextBox Box, ComboBox? Unit, TextBlock Note, string Hint)> boxes = new();
    private string? builtMode;
    private bool syncing;

    public LabCalcCard(LabCalc calc)
    {
        Calc = calc;
        id = calc.Id;
        Focusable = false;
        formula.Fg(B.Text);
        var title = K.Wrapped(calc.Title, Ty.BodyLarge).Strong();
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level3);
        var reset = K.IconButton(SymbolRegular.ArrowReset24, () => BioLabModel.Reset(id), $"Put {calc.Title.ToLowerInvariant()} back to its example", 14, 28);
        var top = K.Columns("*, auto", K.V(2, title, BioUI.Caption(calc.Help)).Col(0), reset.Align(HorizontalAlignment.Right, VerticalAlignment.Top).Col(1));
        var body = K.V(14, top, modes, formula, fields, answer, extra, steps);
        var card = K.Card(body, new Thickness(18, 16, 18, 16));
        Content = card;
        AutomationProperties.SetName(this, calc.Title);
        Build();
        void Changed(string which) { if (which == id || which.Length == 0) Refresh(); }
        Loaded += (_, _) => { BioLabModel.Changed -= Changed; BioLabModel.Changed += Changed; Refresh(); };
        Unloaded += (_, _) => BioLabModel.Changed -= Changed;
    }

    /// <summary>The first field takes the keyboard (a calculator handed over from the pin).</summary>
    public void FocusFirst()
    {
        if (boxes.Values.FirstOrDefault().Box is { } b) Keyboard.Focus(b);
    }

    /// <summary>The mode's fields made (again, when the mode changed).</summary>
    private void Build()
    {
        var mode = Calc.Mode(BioLabModel.Mode(id));
        builtMode = mode.Id;
        syncing = true;
        // the choice of mode
        if (Calc.Modes.Count > 1)
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            if (Calc.ModeLabel.Length > 0)
                wrap.Children.Add(K.Text(Calc.ModeLabel, Ty.Body, B.Text2).Margin(0, 6, 10, 6));
            foreach (var m in Calc.Modes)
            {
                var mid = m.Id;
                var b = PTKit.Choice(m.Title, m.Id == mode.Id, () => BioLabModel.SetMode(id, mid));
                b.Margin = new Thickness(0, 0, 6, 6);
                b.Padding = new Thickness(11, 4, 11, 5);
                b.MinHeight = 30;
                AutomationProperties.SetName(b, m.Id == mode.Id ? $"{Calc.ModeLabel} {m.Title}, chosen" : $"{Calc.ModeLabel} {m.Title}");
                wrap.Children.Add(b);
            }
            modes.Content = wrap;
        }
        formula.Text = mode.Formula;
        AutomationProperties.SetName(formula, "Formula: " + mode.Formula);
        // the fields
        fields.Children.Clear();
        boxes.Clear();
        var grid = new LabFieldGrid();
        foreach (var fid in mode.Inputs) grid.Children.Add(Field(Calc.Field(fid)));
        fields.Children.Add(grid);
        syncing = false;
    }

    private FrameworkElement Field(LabField f)
    {
        var box = ToolKit.Box(BioLabModel.Text(id, f.Id), f.IsList ? "e.g. " + f.Text : f.Optional ? "Optional" : "");
        box.MinHeight = 32;
        if (f.IsList)
        {
            box.TextWrapping = TextWrapping.Wrap;
            box.AcceptsReturn = false;
        }
        var fid = f.Id;
        box.TextChanged += (_, _) => { if (!syncing) BioLabModel.SetText(id, fid, box.Text); };
        AutomationProperties.SetName(box, f.Label);
        ComboBox? unit = null;
        var units = LabUnits.Of(f.Dim);
        UIElement row = box;
        if (units.Count > 1 || units[0].Symbol.Length > 0)
        {
            unit = UnitMenu(f, BioLabModel.Unit(id, f.Id), u => BioLabModel.SetUnit(id, fid, u));
            row = K.Columns("*, 8, auto", box.Col(0), unit.Col(2));
        }
        if (f.Presets is { Count: > 0 } presets)
        {
            // (the field's usual values — a 10× ocular, a 10⁻³ plate, a 0.2 M sucrose solution — a click away)
            FrameworkElement? at = null;
            var pick = K.IconButton(SymbolRegular.ChevronDown24, () => Presets(at!, f, presets), $"Usual values of {f.Label.ToLowerInvariant()}", 14, 32);
            at = pick;
            row = K.Columns("*, 6, auto", ((FrameworkElement)row).Col(0), pick.Col(2));
        }
        var hint = f.Hint ?? (f.IsList ? "Separate the numbers with commas or spaces." : f.Optional ? "Optional: leave it empty if you don't have it." : "");
        var note = K.Wrapped(hint, Ty.Caption, B.Text2);
        note.Visibility = hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        boxes[f.Id] = (box, unit, note, hint);
        var label = K.Text(f.Label, Ty.Caption, B.Text2).Strong();
        label.TextWrapping = TextWrapping.Wrap;
        return K.V(5, label, row, note);
    }

    /// <summary>A field's usual values as a menu under its button; one chosen fills the field.</summary>
    private void Presets(FrameworkElement at, LabField f, IReadOnlyList<LabPreset> presets)
    {
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = at, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var p in presets)
        {
            var text = p.Text;
            var item = new System.Windows.Controls.MenuItem { Header = p.Label };
            item.Click += (_, _) => BioLabModel.SetText(id, f.Id, text);
            menu.Items.Add(item);
        }
        AutomationProperties.SetName(menu, $"Usual values of {f.Label}");
        menu.IsOpen = true;
    }

    /// <summary>A unit menu: its quantity's units (the SI prefixes), the one chosen shown.</summary>
    private static ComboBox UnitMenu(LabField f, string chosen, Action<string> pick)
    {
        var units = LabUnits.Of(f.Dim);
        var menu = new ComboBox { MinWidth = 0, Width = units.Max(u => u.Symbol.Length) > 6 ? 128 : 92 };
        foreach (var u in units) menu.Items.Add(new ComboBoxItem { Content = u.Symbol, Tag = u.Symbol, ToolTip = u.Name });
        menu.SelectedItem = menu.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == chosen) ?? menu.Items[0];
        menu.SelectionChanged += (_, _) => { if (menu.SelectedItem is ComboBoxItem { Tag: string s }) pick(s); };
        AutomationProperties.SetName(menu, $"{f.Label} unit");
        menu.IsEnabled = units.Count > 1;
        return menu;
    }

    /// <summary>The answer, the fields' reasons and the steps drawn again.</summary>
    private void Refresh()
    {
        if (builtMode != BioLabModel.Mode(id)) Build();
        var r = BioLabModel.Result(id);
        var mode = Calc.Mode(BioLabModel.Mode(id));
        syncing = true;
        foreach (var (fid, (box, unit, note, hint)) in boxes)
        {
            var text = BioLabModel.Text(id, fid);
            if (box.Text != text) box.Text = text;
            if (unit != null && unit.SelectedItem is ComboBoxItem { Tag: string u } && u != BioLabModel.Unit(id, fid))
                unit.SelectedItem = unit.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == BioLabModel.Unit(id, fid));
            if (r.FieldErrors.TryGetValue(fid, out var why))
            {
                note.Text = why;
                note.Visibility = Visibility.Visible;
                note.SetResourceReference(TextBlock.ForegroundProperty, B.Critical);
                box.SetResourceReference(Control.BorderBrushProperty, B.Critical);
                AutomationProperties.SetHelpText(box, why);
            }
            else
            {
                note.Text = hint;
                note.Visibility = hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                note.SetResourceReference(TextBlock.ForegroundProperty, B.Text2);
                box.ClearValue(Control.BorderBrushProperty);
                AutomationProperties.SetHelpText(box, hint);
            }
        }
        syncing = false;
        answer.Content = Answer(r, mode);
        extra.Content = Extra(r);
        steps.Content = Steps(r);
    }

    private FrameworkElement Answer(LabResult r, LabMode mode)
    {
        var tint = BioColors.Tool("biolab");
        if (!r.Ok)
        {
            var why = r.Problem ?? (r.FieldErrors.Count > 0 ? "Fix the field in red to see the answer." : "Fill in the fields to see the answer.");
            var note = ToolKit.Note(why, r.Problem != null ? SymbolRegular.Warning24 : SymbolRegular.Info24, r.Problem != null ? null : K.Hex("#8e8e93"));
            AutomationProperties.SetName(note, why);
            return note;
        }
        var v = K.V(6);
        var big = Everyday.Figures(r.Value, 30, FontWeights.SemiBold);
        var sym = K.Text($"{r.Symbol} =", Ty.BodyLarge, B.Text2);
        sym.VerticalAlignment = VerticalAlignment.Center;
        sym.Margin = new Thickness(0, 0, 10, 0);
        var line = K.H(0, sym, big);
        // (the answer's unit: a menu when it fills a field, words otherwise)
        if (mode.Output is { } outId && Calc.Field(outId) is { } of && LabUnits.Of(of.Dim).Count > 1)
        {
            var menu = UnitMenu(of, BioLabModel.Unit(id, outId), u => BioLabModel.SetUnit(id, outId, u));
            menu.Margin = new Thickness(10, 0, 0, 0);
            menu.VerticalAlignment = VerticalAlignment.Center;
            menu.ToolTip = "The answer's unit";
            line.Children.Add(menu);
        }
        else if (r.Unit.Length > 0)
        {
            var u = K.Text(LabResult.UnitAfter(r.Unit), Ty.BodyLarge);
            u.VerticalAlignment = VerticalAlignment.Bottom;
            u.Margin = new Thickness(0, 0, 0, 5);
            line.Children.Add(u);
        }
        var copy = K.IconButton(SymbolRegular.Copy24, () => Everyday.Copy(r.Sentence.Replace("−", "-")), "Copy the answer", 14, 28);
        var head = K.Columns("*, auto", new Viewbox { Child = line, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left }.Col(0),
            copy.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        v.Children.Add(head);
        if (r.Alt != null) v.Children.Add(K.Text($"= {r.Alt}", Ty.Body, B.Text2));
        if (r.Substitution.Length > 0) v.Children.Add(K.Wrapped(r.Substitution, Ty.Body, B.Text2));
        if (r.Rows.Count > 0)
        {
            var rows = K.V(0);
            for (var i = 0; i < r.Rows.Count; i++)
            {
                var row = r.Rows[i];
                var value = K.Text(row.Value, row.Strong ? Ty.BodyStrong : Ty.Body);
                value.TextWrapping = TextWrapping.Wrap;
                value.TextAlignment = TextAlignment.Right;
                value.FontFeatures();
                var g = K.Columns("auto, *", K.Text(row.Name, Ty.Body, B.Text2).Margin(0, 0, 14, 0).Col(0), value.Col(1));
                g.Margin = new Thickness(0, 6, 0, 6);
                rows.Children.Add(g);
                if (i < r.Rows.Count - 1) rows.Children.Add(K.Divider());
            }
            v.Children.Add(rows.Margin(0, 4, 0, 0));
        }
        foreach (var n in r.Notes) v.Children.Add(ChemKit.Line(SymbolRegular.Info16, n, null));
        var panel = Everyday.TintedCard(v, tint, new Thickness(14, 12, 14, 12));
        AutomationProperties.SetName(panel, $"Answer: {r.Sentence}{(r.Alt != null ? $", {r.Alt}" : "")}");
        AutomationProperties.SetLiveSetting(panel, AutomationLiveSetting.Polite);
        return panel;
    }

    /// <summary>A calculator's table or chart (the serial dilution's tubes, SA:V against size, growth, error bars, a scale bar).</summary>
    private FrameworkElement? Extra(LabResult r)
    {
        if (id == "convert") return PrefixTable();
        if (!r.Ok && id != "stats") return null;
        var tint = BioColors.Tool("biolab");
        switch (r.Extra)
        {
            case LabTable t:
                return Table(t);
            case LabCurve c:
            {
                var plot = new LabPlot { Height = 190, XLabel = c.XLabel, YLabel = c.YLabel };
                var quiet = K.Hex(Appearance.Dark ? "#aeaeb2" : "#8e8e93");
                foreach (var s in c.Series) plot.Lines.Add(new PlotLine(s.Points, s.Tone == 0 ? tint : quiet, s.Dashed, c.Series.Count > 1 ? s.Name : null));
                if (c.Mark is { } m) plot.Marks.Add(new PlotMark(m.X, m.Y, tint, m.Label));
                if (c.HLine is { } k) plot.Guides.Add(new PlotGuide(k, true, c.HLineLabel ?? ""));
                if (c.Series.Count > 0) { plot.XMin = 0; plot.XMax = c.Series.Max(s => s.Points.Count > 0 ? s.Points.Max(p => p.X) : 1); }
                AutomationProperties.SetName(plot, id == "sav"
                    ? "Graph of SA:V against size: SA:V falls as the size grows, halving each time the size doubles."
                    : "Graph of population size N over time: exponential growth curving up without limit, logistic growth levelling off at K.");
                return plot;
            }
            case List<LabBar> bars when bars.Count > 0:
            {
                var plot = new LabPlot { Height = 170, YLabel = "mean ± 2 SE" };
                foreach (var b in bars) plot.Bars.Add(new PlotBar(b.Name, b.Mean, b.Low, b.High, tint));
                AutomationProperties.SetName(plot, "Error bars: " + string.Join("; ", bars.Select(b => $"{b.Name} mean {LabNumbers.Plain(b.Mean)}, from {LabNumbers.Plain(b.Low)} to {LabNumbers.Plain(b.High)}")));
                return plot;
            }
            case LabScaleBar s:
                return ScaleBarPreview(s);
        }
        return null;
    }

    private static FrameworkElement Table(LabTable t)
    {
        var g = new Grid();
        foreach (var _ in t.Heads) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Row(IReadOnlyList<string> cells, bool head, int r)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < cells.Count; c++)
            {
                var tb = K.Text(cells[c], head ? Ty.Caption : Ty.Body, head ? B.Text2 : B.Text);
                if (head) tb.FontWeight = FontWeights.SemiBold;
                tb.TextWrapping = TextWrapping.Wrap;
                tb.FontFeatures();
                tb.Margin = new Thickness(0, 5, 10, 5);
                Grid.SetRow(tb, r);
                Grid.SetColumn(tb, c);
                g.Children.Add(tb);
            }
            if (!head)
            {
                var line = K.Divider();
                line.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(line, r);
                Grid.SetColumnSpan(line, cells.Count);
                g.Children.Add(line);
            }
        }
        Row(t.Heads, true, 0);
        for (var i = 0; i < t.Rows.Count; i++) Row(t.Rows[i], false, i + 1);
        return g;
    }

    /// <summary>§5.5's metric prefix table (the AP sheet's), in two columns.</summary>
    private static FrameworkElement PrefixTable()
    {
        var p = LabUnits.Prefixes;
        var g = new Grid();
        foreach (var w in new[] { "auto", "auto", "*", "24", "auto", "auto", "*" }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = K.Length(w) });
        var half = (p.Count + 1) / 2;
        for (var i = 0; i < half; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < p.Count; i++)
        {
            var (name, symbol, power) = p[i];
            var col = i < half ? 0 : 4;
            var row = i < half ? i : i - half;
            var s = K.Text(symbol, Ty.BodyStrong).Margin(0, 3, 10, 3);
            var n = K.Text(name, Ty.Body, B.Text2).Margin(0, 3, 10, 3);
            var f = K.Text("10" + LabNumbers.Sup(power), Ty.Body).Margin(0, 3, 0, 3);
            foreach (var (e, c) in new[] { ((FrameworkElement)s, 0), (n, 1), (f, 2) }) { Grid.SetRow(e, row); Grid.SetColumn(e, col + c); g.Children.Add(e); }
        }
        var heading = K.Text("Metric prefixes", Ty.BodyStrong);
        AutomationProperties.SetName(g, "Metric prefixes: " + string.Join(", ", p.Select(x => $"{x.Name} {x.Symbol} 10 to the {x.Power}")));
        return K.V(8, heading, g);
    }

    /// <summary>The scale bar as it would be drawn: at its real length on screen when it fits, with what it stands for.</summary>
    private static FrameworkElement ScaleBarPreview(LabScaleBar s)
    {
        var tint = BioColors.Tool("biolab");
        var host = new Grid { Height = 54 };
        var bar = new Rectangle { Height = 5, Fill = K.Brush(tint), RadiusX = 1, RadiusY = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var label = K.Text(s.Label, Ty.BodyStrong);
        label.VerticalAlignment = VerticalAlignment.Top;
        var note = K.Text("", Ty.Caption, B.Text2);
        note.VerticalAlignment = VerticalAlignment.Bottom;
        host.Children.Add(label);
        host.Children.Add(bar);
        host.Children.Add(note);
        void Lay()
        {
            // (96 pixels an inch at 100%: 25.4 mm)
            var px = s.Millimetres * 96 / 25.4;
            var room = Math.Max(40, host.ActualWidth - 4);
            var fits = px <= room && px >= 6;
            bar.Width = fits ? px : Math.Min(room, Math.Max(40, room * 0.6));
            note.Text = fits ? $"{LabNumbers.Plain(s.Millimetres)} mm on the image (about life size here at 100% zoom)" : $"{LabNumbers.Plain(s.Millimetres)} mm on the image (not to scale here)";
        }
        host.SizeChanged += (_, _) => Lay();
        AutomationProperties.SetName(host, $"Scale bar: {LabNumbers.Plain(s.Millimetres)} mm long, labelled {s.Label}");
        return host;
    }

    private FrameworkElement Steps(LabResult r)
    {
        var open = BioLabModel.StepsOpen(id);
        var chevron = K.Icon(open ? SymbolRegular.ChevronDown16 : SymbolRegular.ChevronRight16, 12, B.Text2);
        var toggle = K.Subtle(K.H(6, chevron, K.Text(open ? "Hide steps" : "Show steps", Ty.BodyStrong, B.AccentText)), () => BioLabModel.SetSteps(id, !open), new Thickness(4, 4, 8, 4));
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.ToolTip = "The working: units converted, then the sums, then the rounding";
        AutomationProperties.SetName(toggle, open ? "Hide steps" : "Show steps");
        if (!open || r.Steps.Count == 0) return toggle;
        var list = K.V(6);
        for (var i = 0; i < r.Steps.Count; i++)
        {
            var n = K.Text($"{i + 1}.", Ty.Body, B.Text2).Margin(0, 0, 8, 0);
            n.MinWidth = 18;
            list.Children.Add(K.Columns("auto, *", n.Col(0), K.Wrapped(r.Steps[i], Ty.Body).Col(1)));
        }
        var inset = new Border { Child = list, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(6) };
        inset.Bg(B.Subtle);
        AutomationProperties.SetName(inset, "Steps: " + string.Join(". ", r.Steps));
        if (Motion.On) Motion.FadeIn(inset, Motion.Normal);
        return K.V(8, toggle, inset);
    }
}

/// <summary>A calculator's fields: two side by side when the card is wide enough, else one under another.</summary>
internal sealed class LabFieldGrid : Panel
{
    private const double Gap = 14, RowGap = 12, TwoFrom = 470;

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? 500 : available.Width;
        var two = w >= TwoFrom;
        var colW = two ? (w - Gap) / 2 : w;
        double h = 0, row = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            c.Measure(new Size(colW, double.PositiveInfinity));
            row = Math.Max(row, c.DesiredSize.Height);
            if (!two || i % 2 == 1 || i == InternalChildren.Count - 1)
            {
                h += row + (i == InternalChildren.Count - 1 ? 0 : RowGap);
                row = 0;
            }
        }
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var two = final.Width >= TwoFrom;
        var colW = two ? (final.Width - Gap) / 2 : final.Width;
        double y = 0;
        for (var i = 0; i < InternalChildren.Count; i += two ? 2 : 1)
        {
            var a = InternalChildren[i];
            var b = two && i + 1 < InternalChildren.Count ? InternalChildren[i + 1] : null;
            var rowH = Math.Max(a.DesiredSize.Height, b?.DesiredSize.Height ?? 0);
            a.Arrange(new Rect(0, y, colW, rowH));
            b?.Arrange(new Rect(colW + Gap, y, colW, rowH));
            y += rowH + RowGap;
        }
        return final;
    }
}

/// <summary>
/// Bio Lab Calculator in its pin's flyout (spec §3.2 item 3): C₁V₁ = C₂V₂ (how much stock) and molarity (the mass to
/// weigh out), with unit menus. It works on the same calculators as the whole tool, so Enter — or Open — carries what
/// was begun here into it.
/// </summary>
internal sealed class BioLabCompact : ContentControl
{
    private readonly ToolContext c;
    private readonly SelectorBar pick;
    private readonly ContentControl body = new() { Focusable = false };
    private readonly Dictionary<string, (TextBox Box, ComboBox Unit)> boxes = new();
    private readonly ContentControl answer = new() { Focusable = false };
    private string calc;
    private bool syncing;

    public BioLabCompact(ToolContext c, BioLaunchState? state)
    {
        this.c = c;
        Focusable = false;
        BioLabStore.Load();
        calc = state?.Has("molarity") == true ? "molarity" : state?.Has("dilution") == true ? "dilution" : BioPrefs.String("biolab", "quick") == "molarity" ? "molarity" : "dilution";
        pick = new SelectorBar(new[] { "C₁V₁ = C₂V₂", "Molarity" }, calc == "molarity" ? 1 : 0);
        pick.Changed += i =>
        {
            calc = i == 1 ? "molarity" : "dilution";
            BioPrefs.Set("biolab", "quick", calc);
            Build();
        };
        var open = K.Link("Open Bio Lab Calculator", Open);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.ToolTip = "The whole calculator, with these numbers (Enter)";
        Content = K.V(12, pick, body, answer, open);
        Build();
        void Changed(string which) { if (which == calc || which.Length == 0) Refresh(); }
        Loaded += (_, _) => { BioLabModel.Changed -= Changed; BioLabModel.Changed += Changed; Refresh(); };
        Unloaded += (_, _) => BioLabModel.Changed -= Changed;
    }

    private (string Mode, string[] Fields) Shape => calc == "molarity" ? ("mass", new[] { "c", "v", "mw" }) : ("V1", new[] { "C1", "C2", "V2" });

    private void Build()
    {
        var (mode, ids) = Shape;
        BioLabModel.SetMode(calc, mode);
        var def = LabCalcs.Find(calc)!;
        syncing = true;
        boxes.Clear();
        var rows = K.V(8);
        foreach (var fid in ids)
        {
            var f = def.Field(fid);
            var box = ToolKit.Box(BioLabModel.Text(calc, fid));
            var id = fid;
            box.TextChanged += (_, _) => { if (!syncing) BioLabModel.SetText(calc, id, box.Text); };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Open(); } };
            AutomationProperties.SetName(box, f.Label);
            var units = LabUnits.Of(f.Dim);
            var menu = new ComboBox { Width = 82, MinWidth = 0, IsEnabled = units.Count > 1 };
            foreach (var u in units) menu.Items.Add(new ComboBoxItem { Content = u.Symbol, Tag = u.Symbol, ToolTip = u.Name });
            menu.SelectedItem = menu.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == BioLabModel.Unit(calc, fid)) ?? menu.Items[0];
            menu.SelectionChanged += (_, _) => { if (!syncing && menu.SelectedItem is ComboBoxItem { Tag: string s }) BioLabModel.SetUnit(calc, id, s); };
            AutomationProperties.SetName(menu, $"{f.Label} unit");
            var label = K.Text($"{f.Symbol}  {f.Label}", Ty.Caption, B.Text2);
            label.FontWeight = FontWeights.SemiBold;
            label.TextWrapping = TextWrapping.Wrap;
            label.VerticalAlignment = VerticalAlignment.Center;
            rows.Children.Add(K.Columns("108, *, 6, auto", label.Col(0), box.Col(1), menu.Col(3)));
            boxes[fid] = (box, menu);
        }
        var formula = new TextBlock { Text = def.Mode(mode).Formula, FontFamily = K.DisplayFace, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        formula.Fg(B.Text);
        body.Content = K.V(10, formula, rows);
        syncing = false;
        Refresh();
    }

    private void Refresh()
    {
        var r = BioLabModel.Result(calc);
        syncing = true;
        foreach (var (fid, (box, _)) in boxes)
        {
            var t = BioLabModel.Text(calc, fid);
            if (box.Text != t) box.Text = t;
            if (r.FieldErrors.ContainsKey(fid)) box.SetResourceReference(Control.BorderBrushProperty, B.Critical); else box.ClearValue(Control.BorderBrushProperty);
            AutomationProperties.SetHelpText(box, r.FieldErrors.GetValueOrDefault(fid) ?? "");
        }
        syncing = false;
        if (!r.Ok)
        {
            answer.Content = K.Wrapped(r.Problem ?? r.FieldErrors.Values.FirstOrDefault() ?? "Fill in the three numbers.", Ty.Body, B.Text2);
            return;
        }
        var big = Everyday.Figures($"{r.Symbol} = {r.Value}{LabResult.UnitAfter(r.Unit)}", 26, FontWeights.SemiBold);
        var v = K.V(4, Everyday.Shrinks(big));
        if (r.Notes.FirstOrDefault() is { } note) v.Children.Add(K.Wrapped(note, Ty.Body, B.Text2));
        AutomationProperties.SetName(v, r.Sentence);
        AutomationProperties.SetLiveSetting(v, AutomationLiveSetting.Polite);
        answer.Content = v;
    }

    private void Open() => c.OpenFull(new Dictionary<string, string> { ["calc"] = calc }, null);
}

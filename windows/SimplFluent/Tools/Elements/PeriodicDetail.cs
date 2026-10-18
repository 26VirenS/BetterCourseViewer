using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// An element's own page, grown out of its square in the table (the Mac's PTElementPage): its tile, name and kinds; See
/// it in 3D, which hands it to Atoms &amp; Orbitals (its atom, orbitals and electron cloud to turn — on this page until
/// 0.3.2); its electrons, shell by shell and orbital by orbital; its facts; how it compares with every element; its
/// successive ionization energies; where it sits in the table; its neighbours; and its story. ← and → step through the
/// elements; Table (or Ctrl+[, or Escape) returns to the table.
/// </summary>
internal sealed class PTElementPage : Grid
{
    private readonly Action close;
    private readonly Action<int> go;
    private readonly Action<PTElement> openAtoms;
    private readonly ContentControl body = new() { Focusable = false };
    private readonly ScrollViewer scroller;
    private PTElement e;
    private int layout = -1;

    /// <param name="openAtoms">See it in 3D: the element handed to Atoms &amp; Orbitals (PeriodicTableTool.OpenAtoms).</param>
    public PTElementPage(PTElement element, Action close, Action<int> go, Action<PTElement> openAtoms)
    {
        e = element;
        this.close = close;
        this.go = go;
        this.openAtoms = openAtoms;
        scroller = ToolKit.Page(body, 1480);
        Children.Add(scroller);
        Focusable = true;
        FocusVisualStyle = null;
        scroller.SizeChanged += (_, _) => Relayout();
        PreviewKeyDown += OnKey;
        PreviewMouseDown += (_, a) => { if (!IsKeyboardFocusWithin && a.OriginalSource is not System.Windows.Controls.Primitives.TextBoxBase) Focus(); };
        Loaded += (_, _) =>
        {
            PTKeys.Hook();
            PTKeys.Pages.Add(this);
            Dispatcher.BeginInvoke(() => { if (!IsKeyboardFocusWithin) Focus(); }, DispatcherPriority.Input);
        };
        Unloaded += (_, _) => PTKeys.Pages.Remove(this);
        Build();
    }

    public PTElement Element => e;

    /// <summary>Back to the table.</summary>
    public void Back() => close();

    /// <summary>Another element on the page (its neighbour, the next): drawn afresh, the page where it was.</summary>
    public void Show(PTElement next)
    {
        if (next.Number == e.Number) return;
        e = next;
        Build();
        Motion.FadeIn(body, Motion.Normal);
        Shot.Moved();
    }

    private void Step(int d)
    {
        var n = e.Number + d;
        if (n >= 1 && n <= PeriodicTable.Elements.Count) go(n);
    }

    private void OnKey(object sender, KeyEventArgs a)
    {
        if (a.OriginalSource is TextBoxBase) return;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (a.Key == Key.Left && !ctrl) { Step(-1); a.Handled = true; }
        else if (a.Key == Key.Right && !ctrl) { Step(1); a.Handled = true; }
        else if (ctrl && a.Key == Key.OemOpenBrackets) { close(); a.Handled = true; }
    }

    /// <summary>By the page's width: wide, See it in 3D and its electrons in a column beside the facts; middle, the two
    /// side by side, as tall as each other, the facts below; narrow, one column.</summary>
    private void Relayout()
    {
        var w = Math.Min(1480, scroller.ActualWidth - scroller.Padding.Left - scroller.Padding.Right);
        var l = w >= 1000 ? 2 : w >= 620 ? 1 : 0;
        if (l == layout) return;
        layout = l;
        Build();
    }

    private void Build()
    {
        if (layout < 0)
        {
            var w = scroller.ActualWidth > 0 ? scroller.ActualWidth - 80 : 1100;
            layout = w >= 1000 ? 2 : w >= 620 ? 1 : 0;
        }
        var atoms = AtomsCard();
        var config = PTElectrons.Card(e);
        FrameworkElement columns;
        if (layout == 2)
        {
            var left = K.V(18, atoms, config);
            left.VerticalAlignment = VerticalAlignment.Top;
            var right = Right();
            right.VerticalAlignment = VerticalAlignment.Top;
            columns = K.Columns("430, 18, *", left.Col(0), right.Col(2));
        }
        else if (layout == 1)
            columns = K.V(18, K.Columns("*, 18, *", atoms.Col(0), config.Col(2)), Right());
        else columns = K.V(18, atoms, config, Right());
        body.Content = K.V(18, Header(), Title(), columns);
    }

    // ---- the top ----

    private FrameworkElement Header()
    {
        var back = K.Button("Table", close, icon: SymbolRegular.ChevronLeft24);
        back.MinWidth = 0;
        back.ToolTip = "Back to the table (Ctrl+[)";
        var count = PeriodicTable.Elements.Count;
        var prev = PTKit.Choice("The element before", false, () => Step(-1), SymbolRegular.ChevronLeft24, squished: true);
        prev.IsEnabled = e.Number > 1;
        prev.ToolTip = "The element before (←)";
        var next = PTKit.Choice("The element after", false, () => Step(1), SymbolRegular.ChevronRight24, squished: true);
        next.IsEnabled = e.Number < count;
        next.ToolTip = "The element after (→)";
        var at = K.Text($"{e.Number} / {count}", Ty.Body, B.Text2).Tabular();
        at.VerticalAlignment = VerticalAlignment.Center;
        at.Margin = new Thickness(6, 0, 6, 0);
        return K.Columns("auto, *, auto", back.Col(0), K.H(6, prev, at, next).Col(2));
    }

    private FrameworkElement Title()
    {
        var ink = PTKit.InkOn(e.Kind.Color);
        var num = PTKit.Text(e.Number.ToString(CultureInfo.InvariantCulture), 15, FontWeights.SemiBold, ink).Tabular();
        num.HorizontalAlignment = HorizontalAlignment.Left;
        var sym = PTKit.Fit(PTKit.Text(e.Symbol, 46, FontWeights.Bold, ink));
        sym.VerticalAlignment = VerticalAlignment.Center;
        var mass = PTKit.Text(e.MassText, 12.5, FontWeights.Normal, ink).Tabular();
        mass.HorizontalAlignment = HorizontalAlignment.Center;
        var tile = new Border
        {
            Width = 112, Height = 112, CornerRadius = new CornerRadius(12), Padding = new Thickness(12),
            Background = new SolidColorBrush(e.Kind.Color), Child = K.Rows("auto, *, auto", num.Row(0), sym.Row(1), mass.Row(2)),
        };
        var name = PTKit.Fit(PTKit.Text(e.Name, 40, FontWeights.SemiBold, B.Text, K.DisplayFace), HorizontalAlignment.Left);
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var chip in new[]
                 {
                     PTKit.Chip(e.Kind.Label, e.Kind.Color),
                     PTKit.Chip($"{e.Block}-block", PTColourings.BlockColor(e.Block)),
                     PTKit.Chip($"Group {(e.Group is { } g ? g.ToString(CultureInfo.InvariantCulture) : "—")} · Period {e.Period}", null),
                     PTKit.Chip(e.StateWord, PTColourings.StateColor(e.Phase)),
                 })
        {
            chip.Margin = new Thickness(0, 0, 8, 6);
            chips.Children.Add(chip);
        }
        var words = K.V(10, name, chips);
        words.VerticalAlignment = VerticalAlignment.Center;
        return K.Columns("auto, *", tile.Col(0), words.Margin(22, 0, 0, 0).Col(1));
    }

    private static TextBlock Section(string t) => K.Text(t, Ty.BodyStrong);

    // ---- its atom in 3D, a press away ----

    /// <summary>
    /// See it in 3D: a card that hands the element to Atoms &amp; Orbitals — its shells, its orbitals and its electron
    /// cloud, to turn and zoom — in this popup's place (the tour's last step lights it). Atoms &amp; Orbitals' tile and
    /// colour, so it reads as the tool it opens.
    /// </summary>
    private FrameworkElement AtomsCard()
    {
        var kind = ToolsCenter.KindOf("atoms");
        var tile = kind != null ? K.IconTile(kind.Symbol, kind.Color, 44) : new Border();
        tile.VerticalAlignment = VerticalAlignment.Top;
        var words = K.V(3, K.Text("See it in 3D", Ty.BodyStrong),
            K.Wrapped($"{e.Name}’s shells, orbitals and electron cloud, to turn and zoom, in Atoms & Orbitals.", Ty.Body, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var chevron = K.Icon(SymbolRegular.ChevronRight20, 16, B.Text2);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("auto, *, auto", tile.Margin(0, 0, 14, 0).Col(0), words.Col(1), chevron.Margin(12, 0, 0, 0).Col(2));
        var b = K.CardButton(row, () => openAtoms(e), 8, new Thickness(16));
        b.VerticalContentAlignment = VerticalAlignment.Center;
        b.ToolTip = $"Open Atoms & Orbitals for {e.Name}";
        System.Windows.Automation.AutomationProperties.SetName(b, $"See {e.Name} in 3D, in Atoms & Orbitals");
        return b.TourTarget("ptAtoms");
    }

    // ---- the right ----

    private FrameworkElement Right()
    {
        var v = K.V(18, Stats(), Compare());
        if ((e.Extra?.Ionization.Count ?? 0) > 1) v.Children.Add(Ionizations());
        var where = WhereItSits();
        where.Width = 280;
        where.VerticalAlignment = VerticalAlignment.Top;
        var near = Neighbours();
        near.VerticalAlignment = VerticalAlignment.Top;
        v.Children.Add(K.Columns("auto, 18, *", where.Col(0), near.Col(2)));
        v.Children.Add(About());
        return v;
    }

    private FrameworkElement Stats()
    {
        string Opt(double? v, Func<double, string> f) => v is { } x ? f(x) : "—";
        var x = e.Extra;
        var grid = new PTFlowGrid(150, 10);
        foreach (var (k, v, sub) in new (string, string, string?)[]
                 {
                     ("Atomic mass", $"{e.MassText} u", null),
                     ("Electronegativity", Opt(e.En, PTElement.Trim), null),
                     ("1st ionization", Opt(e.Ionization, i => $"{PTColourings.Short(i)} kJ/mol"), null),
                     ("Atomic radius", Opt(e.Radius, r => $"{(int)r} pm"), null),
                     ("Electron affinity", Opt(e.Affinity, a => $"{PTColourings.Short(a)} kJ/mol"), null),
                     ("Melting point", Opt(e.Melt, m => $"{PTColourings.Short(m)} K"), PTElement.Celsius(e.Melt)),
                     ("Boiling point", Opt(e.Boil, b => $"{PTColourings.Short(b)} K"), PTElement.Celsius(e.Boil)),
                     ("Density", e.DensityText, null),
                     ("Protons", $"{e.Number}", null),
                     ("Neutrons (common)", $"{e.Neutrons}", null),
                     ("Electrons", $"{e.Number}", null),
                     ("Valence electrons", e.ValenceElectrons is { } ve ? $"{ve}" : "—", null),
                     ("Covalent radius", Opt(x?.Covalent, r => $"{(int)r} pm"), null),
                     ("Van der Waals radius", Opt(x?.VanDerWaals, r => $"{(int)r} pm"), null),
                     ("Molar heat", Opt(x?.MolarHeat, h => $"{PTColourings.Short(h)} J/(mol·K)"), null),
                     ("Block · group · period", $"{e.Block} · {(e.Group is { } g ? g.ToString(CultureInfo.InvariantCulture) : "—")} · {e.Period}", null),
                 })
            grid.Children.Add(Stat(k, v, sub));
        return grid;
    }

    private static FrameworkElement Stat(string k, string v, string? sub)
    {
        // (a line kept for the note under the value whether there is one or not, so every box is the same size)
        var note = sub is null or "—" ? null : sub;
        var n = K.Text(note ?? " ", Ty.Caption, B.Text2);
        n.Opacity = note == null ? 0 : 1;
        var value = PTKit.Fit(PTKit.Text(v, 19, FontWeights.SemiBold).Tabular(), HorizontalAlignment.Left);
        return K.Card(K.V(4, K.Text(k, Ty.Caption, B.Text2), value, n), new Thickness(12));
    }

    private FrameworkElement Compare()
    {
        var v = K.V(10, Section("Compared with every element"));
        foreach (var c in new[] { PTColouring.Electronegativity, PTColouring.Radius, PTColouring.Ionization, PTColouring.Affinity, PTColouring.Melting, PTColouring.Boiling, PTColouring.Density })
        {
            var track = new Border { Height = 7, CornerRadius = new CornerRadius(3.5), VerticalAlignment = VerticalAlignment.Center }.Bg(B.Subtle);
            var bar = new Grid { Height = 7, VerticalAlignment = VerticalAlignment.Center, Children = { track } };
            // (the bar as long as the share of elements this one is above — the figure beside it, "above 59%" — in the
            //  colour the table gives its value; not the value's place between the lowest and the highest, which read
            //  against the figure as a contradiction)
            if (PTRank.Percentile(c, e) is { } p && c.Fraction(e) is { } f)
            {
                var fill = new Border { Height = 7, CornerRadius = new CornerRadius(3.5), Background = new SolidColorBrush(PTColourings.Ramp(f)), HorizontalAlignment = HorizontalAlignment.Left };
                bar.Children.Add(fill);
                bar.SizeChanged += (_, a) => fill.Width = Math.Max(6, a.NewSize.Width * p);
                bar.ToolTip = $"Higher than {(int)Math.Round((decimal)p * 100, MidpointRounding.AwayFromZero)}% of the elements whose {c.Label().ToLowerInvariant()} is known";
            }
            var label = K.Text(c.Label(), Ty.Body);
            label.VerticalAlignment = VerticalAlignment.Center;
            var text = K.Text(CompareText(c), Ty.Caption, B.Text2).Tabular();
            text.TextAlignment = TextAlignment.Right;
            text.HorizontalAlignment = HorizontalAlignment.Right;
            text.VerticalAlignment = VerticalAlignment.Center;
            v.Children.Add(K.Columns("150, *, 200", label.Col(0), bar.Margin(14, 0, 14, 0).Col(1), text.Col(2)));
        }
        return K.Card(v, new Thickness(16));
    }

    private string CompareText(PTColouring c)
    {
        if (c.Value(e) == null) return "not known";
        var v = $"{c.Text(e)}{(c.Unit().Length == 0 || c == PTColouring.Electronegativity ? "" : " " + c.Unit())}";
        return PTRank.Percentile(c, e) is { } p ? $"{v} · above {(int)Math.Round((decimal)p * 100, MidpointRounding.AwayFromZero)}%" : v;
    }

    /// <summary>Successive ionization energies, on a log scale: the jump after the outer electrons shows where the core starts.</summary>
    private FrameworkElement Ionizations()
    {
        var ies = e.Extra?.Ionization ?? Array.Empty<double>();
        var top = Math.Log10(ies.DefaultIfEmpty(10).Max());
        var bottom = Math.Log10(Math.Max(ies.DefaultIfEmpty(1).Min() * 0.5, 1));
        int? jump = null;
        for (var i = 1; i < ies.Count; i++) if (ies[i] / ies[i - 1] > 2.8) { jump = i; break; }
        var bars = new UniformGrid { Rows = 1, Columns = ies.Count, Height = 150 };
        for (var i = 0; i < ies.Count; i++)
        {
            var value = PTKit.Fit(K.Text(PTColourings.Short(ies[i]), Ty.Caption, B.Text2).Tabular());
            var h = Math.Max(6, (Math.Log10(ies[i]) - bottom) / Math.Max(top - bottom, 0.01) * 110);
            var bar = new Border { Height = h, CornerRadius = new CornerRadius(5) };
            if (jump is { } j && i >= j) bar.Background = new SolidColorBrush(K.Hex("#ff9f0a")); else bar.Bg(B.Accent);
            var ord = K.Text(PTKit.Ordinal(i + 1), Ty.Caption, B.Text2);
            ord.HorizontalAlignment = HorizontalAlignment.Center;
            var col = K.V(4, value, bar, ord);
            col.VerticalAlignment = VerticalAlignment.Bottom;
            col.Margin = new Thickness(5, 0, 5, 0);
            bars.Children.Add(col);
        }
        var note = jump is { } jj
            ? $"kJ/mol. The big jump after the {PTKit.Ordinal(jj)} electron: the first {jj} are its outer electrons; the rest are held in the core."
            : "kJ/mol, on a log scale.";
        return K.Card(K.V(10, Section("Successive ionization energies"), bars, K.Wrapped(note, Ty.Caption, B.Text2)), new Thickness(16));
    }

    /// <summary>The table in miniature, the element lit, its group and period washed.</summary>
    private Border WhereItSits()
    {
        var words = e.Y <= 7 ? $"Group {(e.Group is { } g ? g.ToString(CultureInfo.InvariantCulture) : "—")}, period {e.Period}" : $"{e.Kind.Label}s, under the table (period {e.Period})";
        return K.Card(K.V(10, Section("Where it sits"), new PTMiniTable(e), K.Wrapped(words, Ty.Caption, B.Text2)), new Thickness(16));
    }

    private Border Neighbours()
    {
        var above = PeriodicTable.Step(e, 0, -1);
        var below = e.Y < 7 ? PeriodicTable.Step(e, 0, 1) : null;
        var before = PeriodicTable.ByNumber(e.Number - 1);
        var after = PeriodicTable.ByNumber(e.Number + 1);
        var grid = new PTFlowGrid(0, 10, columns: 2);
        if (above is { Y: <= 7 } && e.Y <= 7) grid.Children.Add(Neighbour("Above", above));
        if (below is { Y: <= 7 }) grid.Children.Add(Neighbour("Below", below));
        if (before != null) grid.Children.Add(Neighbour("Before", before));
        if (after != null) grid.Children.Add(Neighbour("After", after));
        return K.Card(K.V(10, Section("Neighbours"), grid), new Thickness(16));
    }

    private FrameworkElement Neighbour(string word, PTElement x)
    {
        var words = K.V(1, K.Text(word, Ty.Caption, B.Text2), K.Text(x.Name, Ty.BodyStrong));
        words.VerticalAlignment = VerticalAlignment.Center;
        var b = K.Subtle(K.Columns("auto, *", PTKit.Tile(x, 34, 15, 8).Col(0), words.Margin(10, 0, 0, 0).Col(1)), () => go(x.Number), new Thickness(8), 8);
        b.SetResourceReference(Control.BackgroundProperty, B.Control2);
        b.ToolTip = $"Open {x.Name}";
        System.Windows.Automation.AutomationProperties.SetName(b, $"{word}: {x.Name}");
        return b;
    }

    private FrameworkElement About()
    {
        FrameworkElement Note(SymbolRegular s, string text)
        {
            var t = K.Wrapped(text, Ty.Body, B.Text2);
            var icon = K.Icon(s, 14, B.Text2);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 3, 6, 0);
            return K.Columns("auto, *", icon.Col(0), t.Col(1));
        }
        var v = K.V(10, Section($"About {e.Name}"), PTKit.Selectable(e.Summary, 14, FontWeights.Normal));
        if (e.Extra is { Appearance.Length: > 0 } x) v.Children.Add(Note(SymbolRegular.Eye24, $"Looks: {x.Appearance}"));
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        if (e.Found is { Length: > 0 } found) row.Children.Add(Note(SymbolRegular.Sparkle24, $"Found by {found}").Margin(0, 0, 16, 4));
        if (e.Extra is { NamedBy.Length: > 0 } nx) row.Children.Add(Note(SymbolRegular.BookLetter24, $"Named by {nx.NamedBy}").Margin(0, 0, 16, 4));
        var wiki = new Uri($"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(e.Name)}");
        var link = K.Subtle(K.H(6, K.Icon(SymbolRegular.Open24, 14, B.AccentText), K.Text("Wikipedia", Ty.Body, B.AccentText)), () => Engine.Engine.OpenExternally(wiki), new Thickness(4, 1, 4, 2));
        link.Cursor = Cursors.Hand;
        link.ToolTip = wiki.ToString();
        row.Children.Add(link);
        v.Children.Add(row);
        return K.Card(v, new Thickness(16));
    }
}

/// <summary>An element's electrons as its page and Atoms &amp; Orbitals show them.</summary>
internal static class PTElectrons
{
    /// <summary>Its configuration, short and in full, its shells, and its outer subshells' orbitals as boxes of arrows.</summary>
    public static Border Card(PTElement e)
    {
        var shells = K.H(6, K.Text("Shells", Ty.Body, B.Text2), K.Text(string.Join(" · ", e.Shells), Ty.BodyStrong).Tabular());
        // (the outer subshells' orbitals as boxes, each electron an arrow: one up in every box before any pairs)
        var boxes = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var s in PTConfig.Valence(e))
        {
            var row = K.H(2);
            foreach (var f in s.Filling)
            {
                var arrow = K.Text(f == 2 ? "↑↓" : f == 1 ? "↑" : " ", Ty.Body, B.AccentText);
                arrow.FontSize = 13;
                arrow.FontWeight = FontWeights.Bold;
                arrow.HorizontalAlignment = HorizontalAlignment.Center;
                arrow.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(4), Child = arrow }.Stroke(B.StrongStroke));
            }
            var name = PTKit.Text(s.Name, 12, FontWeights.Normal, B.Text2, PTKit.Mono);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            var group = K.V(4, row, name);
            group.Margin = new Thickness(0, 0, 16, 6);
            boxes.Children.Add(group);
        }
        var v = K.V(12,
            K.Text("Electron configuration", Ty.BodyStrong),
            PTKit.Selectable(PTConfig.Pretty(e), 22, FontWeights.SemiBold, B.Text, PTKit.Mono),
            PTKit.Selectable(PTConfig.Full(e), 14, FontWeights.Normal, B.Text2, PTKit.Mono),
            shells,
            boxes,
            K.Wrapped("Arrows are electrons: each orbital takes one before any takes a second (Hund’s rule), the second with the opposite spin.", Ty.Caption, B.Text2));
        return K.Card(v, new Thickness(16));
    }
}

/// <summary>The table in miniature (the element page's "Where it sits"): the element in yellow, its group and its period in
/// the accent, the rest faint.</summary>
internal sealed class PTMiniTable : FrameworkElement
{
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(PTMiniTable),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(PTMiniTable),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly PTElement e;

    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }

    public PTMiniTable(PTElement e)
    {
        this.e = e;
        Height = 120;
        SetResourceReference(AccentProperty, B.Accent);
        SetResourceReference(InkProperty, B.Text);
        System.Windows.Automation.AutomationProperties.SetName(this, $"{e.Name} in the periodic table");
    }

    protected override void OnRender(DrawingContext dc)
    {
        const double gap = 2;
        var c = Math.Min((ActualWidth - 17 * gap) / 18, (ActualHeight - 9 * gap) / 10);
        if (c <= 0) return;
        var yellow = new SolidColorBrush(K.Hex(Appearance.Dark ? "#ffd60a" : "#ffcc00"));
        foreach (var x in PeriodicTable.Elements)
        {
            var row = x.Y <= 7 ? x.Y - 1 : x.Y - 1 + 0.4;
            var r = new Rect((x.X - 1) * (c + gap), row * (c + gap), c, c);
            var same = x.X == e.X || (x.Y == e.Y && e.Y <= 7);
            if (x.Number == e.Number) { dc.DrawRoundedRectangle(yellow, null, r, 1.5, 1.5); continue; }
            dc.PushOpacity(same ? 0.7 : 0.14);
            dc.DrawRoundedRectangle(same ? Accent : Ink, null, r, 1.5, 1.5);
            dc.Pop();
        }
    }
}

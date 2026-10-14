using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// Gene Expression's Codon table page (spec §4.3, §6.1, §7 item 13), the Mac's GXCodonsPage: the standard genetic code as
/// a grid — first base down the side, second across the top, third within each block, U C A G everywhere — or as a wheel
/// read from the centre out; a lookup both ways (a codon to its amino acid, an amino acid to all its codons); a switch to
/// DNA coding-strand letters, clearly titled; and the card of the amino acid pressed, with "See in 3D" in the Biomolecule
/// Builder.
/// </summary>
internal sealed class GXCodonsPage : Grid
{
    private readonly ToolContext c;
    private readonly SelectorBar viewBar;
    private readonly Wpf.Ui.Controls.ToggleSwitch dnaSwitch;
    private readonly Wpf.Ui.Controls.TextBox lookup;
    private readonly ContentControl clearHost = new() { Focusable = false };
    private readonly ContentControl answerHost = new() { Focusable = false };
    private readonly TextBlock tableTitle = K.Text("", Ty.Subtitle);
    private readonly TextBlock tableNote = K.Wrapped("", Ty.Body, B.Text2);
    private readonly ContentControl badgeHost = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Top };
    private readonly ContentControl tableHost = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock foot = K.Wrapped("", Ty.Caption, B.Text2).Center();
    private readonly GXCodonWheel wheel = new();
    private readonly ScrollViewer gridScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly Stack cards = K.V(14);

    public GXCodonsPage(ToolContext c)
    {
        this.c = c;
        viewBar = GXKit.Choice(new[] { "Grid", "Wheel" }, GeneExpressionModel.CodonView == GXCodonView.Wheel ? 1 : 0, i => GeneExpressionModel.CodonView = i == 1 ? GXCodonView.Wheel : GXCodonView.Grid);
        viewBar.ToolTip = "The table as a grid, or as a wheel read from the centre out";
        var (dnaRow, dnaToggle) = GXKit.Toggle("DNA coding-strand letters (T for U)", GeneExpressionModel.DnaLetters, on => GeneExpressionModel.DnaLetters = on,
            "The coding strand reads like the mRNA, with T for U");
        dnaSwitch = dnaToggle;
        lookup = GXKit.Field(GeneExpressionModel.Lookup, "AUG, ATG, Lys, K, lysine…", t => GeneExpressionModel.Lookup = t);
        System.Windows.Automation.AutomationProperties.SetName(lookup, "Codon or amino acid");
        lookup.ToolTip = "A codon (AUG or ATG) or an amino acid (Lys, K, lysine). Enter picks it (Ctrl+F)";
        lookup.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            GeneExpressionModel.PickFromLookup();
            e.Handled = true;
        };
        var left = K.V(14,
            BioUI.Heading("View"), viewBar, dnaRow,
            BioUI.Heading("Look up"), K.Columns("*, auto", lookup.Col(0), clearHost.Col(1)), answerHost,
            BioUI.Heading("Classes"),
            BioUI.Legend(BioAAClasses.All.Select(k => (BioColors.Of(k), k.Label())).Append((GXColors.Stop.ToColor(), "Stop (no amino acid)"))),
            BioUI.Caption("AUG is the start codon, and inside a gene it codes Met. UUG and CUG can rarely start translation too; this tool uses AUG only."),
            BioUI.Caption("Mitochondria use a slightly different code (in vertebrate mitochondria UGA codes Trp). This is the standard code (NCBI table 1)."));

        var head = K.Columns("*, auto", K.V(2, tableTitle, tableNote).Col(0), badgeHost.Col(1));
        head.Margin = new Thickness(16, 14, 16, 8);
        foot.Margin = new Thickness(12, 8, 12, 12);
        wheel.Margin = new Thickness(16);
        var card = K.Card(K.Rows("auto, *, auto", head.Row(0), tableHost.Row(1), foot.Row(2)), new Thickness(0));

        Children.Add(BioUI.Work(left, card, cards));
        void Model() => Refresh();
        Loaded += (_, _) => { GeneExpressionModel.Changed -= Model; GeneExpressionModel.Changed += Model; Refresh(); };
        Unloaded += (_, _) => GeneExpressionModel.Changed -= Model;
        wheel.HoverChanged += () => Cards();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                lookup.Focus();
                lookup.SelectAll();
                e.Handled = true;
            }
        };
        Refresh();
    }

    private static GXQuery Query => GXCodonTable.Parse(GeneExpressionModel.Lookup);

    private void Refresh()
    {
        var dna = GeneExpressionModel.DnaLetters;
        viewBar.Select(GeneExpressionModel.CodonView == GXCodonView.Wheel ? 1 : 0, false);
        if (dnaSwitch.IsChecked != dna) dnaSwitch.IsChecked = dna;
        GXKit.Keep(lookup, GeneExpressionModel.Lookup);
        clearHost.Content = GeneExpressionModel.Lookup.Length == 0 ? null
            : K.IconButton(SymbolRegular.DismissCircle20, () => { GeneExpressionModel.Lookup = ""; GeneExpressionModel.Picked = null; lookup.Focus(); }, "Clear the lookup", 16, 32).Margin(6, 0, 0, 0);
        answerHost.Content = GXCodonAnswer.Make(Query, dna);
        tableTitle.Text = dna ? "The genetic code in DNA letters" : "The genetic code";
        tableNote.Text = dna ? "Coding-strand codons, 5′→3′ (T for U). The ribosome reads the mRNA: the same, with U."
            : "mRNA codons, read 5′→3′. 64 codons: 61 for amino acids, 3 stops.";
        badgeHost.Content = dna ? GXKit.Badge("DNA letters", BioPalette.Thymine.ToColor()) : null;
        var lit = GXCodonTable.Matches(Query);
        if (GeneExpressionModel.CodonView == GXCodonView.Grid)
        {
            gridScroll.Content = TableGrid(lit, dna);
            tableHost.Content = gridScroll;
            foot.Text = "Read it: first base on the left, second across the top, third on the right. Press a codon for its amino acid.";
        }
        else
        {
            wheel.Set(lit, GeneExpressionModel.Picked, dna);
            tableHost.Content = wheel;
            foot.Text = "Read it from the centre out: first base, second, third, then the amino acid. The start codon AUG is outlined in green.";
        }
        Cards();
    }

    // ---- the grid ----

    private FrameworkElement TableGrid(HashSet<string> lit, bool dna)
    {
        var g = new Grid { Margin = new Thickness(16, 0, 16, 16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        for (var i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        for (var i = 0; i < 5; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(K.Text("1st", Ty.Caption, B.Text2).Strong().Align(HorizontalAlignment.Center, VerticalAlignment.Bottom).Row(0).Col(0));
        for (var second = 0; second < 4; second++)
        {
            var v = K.V(0);
            v.HorizontalAlignment = HorizontalAlignment.Center;
            if (second == 0) v.Children.Add(K.Text("2nd base", Ty.Caption, B.Text2).Center());
            v.Children.Add(Letter(second, 18, dna));
            v.Margin = new Thickness(0, 0, 0, 4);
            g.Children.Add(v.Row(0).Col(1 + second));
        }
        g.Children.Add(K.Text("3rd", Ty.Caption, B.Text2).Strong().Align(HorizontalAlignment.Center, VerticalAlignment.Bottom).Row(0).Col(5));
        for (var first = 0; first < 4; first++)
        {
            g.Children.Add(Letter(first, 22, dna).Align(HorizontalAlignment.Center, VerticalAlignment.Center).Row(1 + first).Col(0));
            for (var second = 0; second < 4; second++)
            {
                var block = K.V(4);
                for (var third = 0; third < 4; third++) block.Children.Add(Cell(GXCodonTable.Codon(first, second, third), lit, dna));
                var b = new Border { Child = block, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), Margin = new Thickness(2) };
                b.Background = K.Brush(GXDraw.Alpha(ColourOfText(), 0.04));
                g.Children.Add(b.Row(1 + first).Col(1 + second));
            }
            var thirds = K.V(4);
            thirds.Margin = new Thickness(0, 6, 0, 6);
            for (var third = 0; third < 4; third++)
            {
                var l = Letter(third, 13, dna);
                l.Height = 28;
                l.Padding = new Thickness(0, 5, 0, 0);
                thirds.Children.Add(l);
            }
            g.Children.Add(thirds.Row(1 + first).Col(5));
        }
        System.Windows.Automation.AutomationProperties.SetName(g, "The genetic code, first base by row, second by column, third within each block");
        return g;
    }

    private static Color ColourOfText() => Appearance.Dark ? Colors.White : Colors.Black;

    private static TextBlock Letter(int i, double size, bool dna)
    {
        var s = GXCodonTable.Letters(BioCode.Bases[i].ToString(), dna);
        return new TextBlock { Text = s, FontFamily = BioUI.Mono, FontSize = size, FontWeight = FontWeights.Bold, Foreground = K.Brush(BioColors.Base(s[0])), HorizontalAlignment = HorizontalAlignment.Center };
    }

    private FrameworkElement Cell(string codon, HashSet<string> lit, bool dna)
    {
        var aa = BioCode.AminoAcid(codon) ?? '?';
        var on = lit.Contains(codon);
        var chosen = GeneExpressionModel.Picked == codon;
        var tint = aa == '*' ? GXColors.Stop.ToColor() : BioColors.AminoAcid(aa);
        var shown = GXCodonTable.Letters(codon, dna);
        var three = K.Text(GXKit.Three(aa), Ty.Body);
        three.FontSize = 13;
        three.FontWeight = aa == '*' ? FontWeights.Bold : FontWeights.Medium;
        if (aa == '*') three.Foreground = K.Brush(K.Darkened(GXColors.Stop.ToColor()));
        three.VerticalAlignment = VerticalAlignment.Center;
        var bases = GXKit.Bases(shown, 13);
        bases.TextWrapping = TextWrapping.NoWrap;
        bases.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("auto, *, auto", bases.Col(0), three.Margin(5, 0, 0, 0).Col(2));
        if (codon == "AUG") row.Children.Add(GXKit.Badge("Start", GXColors.Start.ToColor(), 9.5).Margin(3, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        row.Margin = new Thickness(6, 0, 6, 0);
        row.VerticalAlignment = VerticalAlignment.Center;
        var face = new Border
        {
            Child = row, Height = 28, CornerRadius = new CornerRadius(7), Background = K.Brush(GXDraw.Alpha(tint, on || chosen ? 0.32 : 0.12)), ClipToBounds = true,
            BorderThickness = new Thickness(chosen ? 2 : 1.5),
            BorderBrush = chosen ? K.Brush(Appearance.Accent) : on ? K.Brush(GXDraw.Alpha(Appearance.Accent, 0.7))
                : codon == "AUG" ? K.Brush(GXDraw.Alpha(GXColors.Start.ToColor(), 0.7)) : Brushes.Transparent,
        };
        var b = K.Subtle(face, () => GeneExpressionModel.Picked = codon, new Thickness(0), 7);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.Opacity = lit.Count == 0 || on || chosen ? 1 : 0.38;
        b.ToolTip = $"{shown} → {GXKit.Name(aa)}";
        System.Windows.Automation.AutomationProperties.SetName(b, $"{string.Join(" ", shown.Select(x => x.ToString()))}: {(aa == '*' ? "stop codon" : GXKit.Name(aa))}{(codon == "AUG" ? ", start codon" : "")}{(chosen ? ", picked" : "")}");
        return b;
    }

    // ---- right: the amino acid's card ----

    private void Cards()
    {
        cards.Children.Clear();
        var hovered = wheel.Hovered is { Length: 3 } h && GeneExpressionModel.CodonView == GXCodonView.Wheel ? h : null;
        var q = Query;
        if ((GeneExpressionModel.Picked ?? hovered) is { } codon) cards.Children.Add(CodonCard(codon));
        else if (q.Kind == GXQueryKind.AminoAcid && BioAminoAcids.ByOne.TryGetValue(q.Aa, out var a)) cards.Children.Add(AminoAcidCard(a, null));
        else cards.Children.Add(BioUI.Card("Press a codon", null, null,
            K.Wrapped("Its amino acid's card comes here: its class, charge, side chain and every codon that codes it. Type in the lookup to light codons.", Ty.Body, B.Text2)));
        cards.Children.Add(BioUI.Card("Reading a codon", null, null,
            BioUI.FactRow("Direction", "Codons are read on the mRNA, 5′→3′, three bases at a time from the start codon AUG."),
            BioUI.FactRow("Anticodon", "A tRNA's anticodon pairs antiparallel: codon 5′-AUG-3′ pairs with anticodon 3′-UAC-5′ (written 5′-CAU-3′ in tRNA tables)."),
            BioUI.FactRow("Redundancy", "Most amino acids have 2–6 codons, often differing only in the third base; Met and Trp have one each.")));
    }

    private FrameworkElement CodonCard(string codon)
    {
        var dna = GeneExpressionModel.DnaLetters;
        var aa = BioCode.AminoAcid(codon) ?? '?';
        if (aa == '*')
            return BioUI.Card("Stop codon", GXColors.Stop.ToColor(), $"{GXCodonTable.Letters(codon, dna)} · “{BioCode.StopNames.GetValueOrDefault(codon, "stop")}”",
                BioUI.FactRow("What it does", "A stop codon codes for no amino acid. A release factor, a protein and not a tRNA, binds it in the ribosome's A site, and the finished polypeptide is let go."),
                BioUI.FactRow("The three stops", "UAA (“ochre”), UAG (“amber”) and UGA (“opal”)."),
                BioUI.FactRow("Footnote", "In vertebrate mitochondria UGA codes Trp instead; this table is the standard code."));
        return BioAminoAcids.ByOne.TryGetValue(aa, out var a) ? AminoAcidCard(a, codon) : BioUI.Card(codon, null, null);
    }

    private FrameworkElement AminoAcidCard(BioAminoAcid a, string? codon)
    {
        var dna = GeneExpressionModel.DnaLetters;
        var rows = new List<UIElement?>();
        if (codon != null)
        {
            rows.Add(BioUI.FactRow("This codon", $"{GXCodonTable.Letters(codon, dna)} → {a.Three}{(codon == "AUG" ? ": the start codon, and Met inside a gene" : "")}"));
            rows.Add(BioUI.FactRow("Its tRNA's anticodon", $"3′-{BioCode.Anticodon3To5(codon)}-5′ (5′-{BioCode.Anticodon5To3(codon)}-3′ in tRNA tables)"));
        }
        rows.Add(BioUI.FactRow(a.CodonCount == 1 ? "Its codon" : $"Its {a.CodonCount} codons", string.Join("  ", a.Codons.Select(x => GXCodonTable.Letters(x, dna)))));
        rows.Add(BioUI.FactRow("Class", $"{a.Group.Label()} · {a.Lehninger}"));
        rows.Add(BioUI.FactRow("Side chain at pH 7.4", a.PKa is { } pk
            ? $"Charge {a.ChargeText} · pKa ≈ {pk.ToString("0.00", CultureInfo.InvariantCulture)} (can shift inside a protein)"
            : $"Charge {a.ChargeText}"));
        rows.Add(BioUI.FactRow("Hydropathy (Kyte–Doolittle)", a.Hydropathy.ToString("+0.0;−0.0;0.0", CultureInfo.InvariantCulture) + (a.Hydropathy > 0 ? " · hydrophobic" : " · hydrophilic")));
        rows.Add(BioUI.FactRow("Mass", $"{a.Mass.ToString("0.00", CultureInfo.InvariantCulture)} g/mol free · {a.ResidueMass.ToString("0.00", CultureInfo.InvariantCulture)} g/mol in a chain · {BiomolText.Formula(a.Formula)}"));
        rows.Add(BioUI.FactRow("Note", a.Note));
        var see = ChemKit.Wide("See in 3D", () => BioTools.Open(c, "biomol", new() { ["molecule"] = a.Three.ToUpperInvariant(), ["page"] = "amino" }), icon: SymbolRegular.Cube24);
        see.ToolTip = $"Open {a.Name} in the Biomolecule Builder";
        rows.Add(see);
        return BioUI.Card(a.Name, BioColors.Of(a.Group), $"{a.Three} · {a.One} · {a.Group.Label()}", rows.ToArray());
    }
}

/// <summary>A codon's answer (the Mac's GXCodonAnswer): the amino acid's name, three- and one-letter codes and class, with
/// Start or Stop where it applies (the pin's quick version and the codon table's lookup).</summary>
internal static class GXCodonAnswer
{
    public static FrameworkElement Make(GXQuery q, bool dna)
    {
        switch (q.Kind)
        {
            case GXQueryKind.Codon:
            {
                var aa = BioCode.AminoAcid(q.Codon) ?? '?';
                if (aa == '*') return Answer("Stop", $"{GXCodonTable.Letters(q.Codon, dna)} · “{BioCode.StopNames.GetValueOrDefault(q.Codon, "stop")}” · no amino acid", GXColors.Stop.ToColor(), "Stop");
                if (BioAminoAcids.ByOne.TryGetValue(aa, out var a))
                    return Answer(a.Name, $"{GXCodonTable.Letters(q.Codon, dna)} → {a.Three} · {a.One} · {a.Group.Label()}", BioColors.Of(a.Group), q.Codon == "AUG" ? "Start" : null);
                break;
            }
            case GXQueryKind.AminoAcid:
                if (BioAminoAcids.ByOne.TryGetValue(q.Aa, out var b))
                {
                    var n = b.Codons.Count;
                    return Answer(b.Name, $"{b.Three} · {b.One} · {b.Group.Label()} · {n} codon{(n == 1 ? "" : "s")}", BioColors.Of(b.Group), q.Aa == 'M' ? "Start" : null);
                }
                break;
            case GXQueryKind.Stop:
                return Answer("Stop codons", "UAA, UAG, UGA · read by release factors", GXColors.Stop.ToColor(), "Stop");
            case GXQueryKind.Invalid:
                return ChemKit.Line(SymbolRegular.ErrorCircle16, q.Why, ChemKit.Orange);
        }
        return BioUI.Caption("Type a codon (AUG or ATG) or an amino acid (Lys, K, lysine).");
    }

    private static FrameworkElement Answer(string title, string line, Color colour, string? badge)
    {
        var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(4), Background = K.Brush(colour), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 10, 0) };
        var head = K.H(6, K.Text(title, Ty.BodyStrong));
        if (badge != null) head.Children.Add(GXKit.Badge(badge, badge == "Stop" ? GXColors.Stop.ToColor() : GXColors.Start.ToColor()));
        var v = K.V(2, head, K.Wrapped(line, Ty.Body, B.Text2));
        var row = K.Columns("auto, *", swatch.Col(0), v.Col(1));
        System.Windows.Automation.AutomationProperties.SetName(row, $"{title}{(badge != null ? ", " + badge : "")}. {line}");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(row, System.Windows.Automation.AutomationLiveSetting.Polite);
        return row;
    }
}

/// <summary>
/// The codon wheel (§4.3), the Mac's GXCodonWheel: three rings from the centre — first base (4 × 90°), second (16 × 22.5°),
/// third (64 × 5.625°) — in U, C, A, G order clockwise from 12 o'clock, ring radii 0.18, 0.36, 0.56 and 0.78 of the
/// wheel's, and the amino acids outside at 0.90. Codons a lookup lights are outlined; the one under the pointer shows in
/// the centre; a press picks it (a press on an inner ring picks the first codon it begins).
/// </summary>
internal sealed class GXCodonWheel : GXCanvas
{
    private HashSet<string> lit = new();
    private string? picked;
    private bool dna;

    /// <summary>The codon (or first one or two bases) under the pointer.</summary>
    public string? Hovered { get; private set; }
    public event Action? HoverChanged;

    public GXCodonWheel()
    {
        Focusable = false;
        Cursor = Cursors.Hand;
        MinHeight = 320;
        System.Windows.Automation.AutomationProperties.SetName(this, "Codon wheel, read from the centre out");
        System.Windows.Automation.AutomationProperties.SetHelpText(this, "The grid view lists every codon");
    }

    public void Set(HashSet<string> lit, string? picked, bool dna)
    {
        this.lit = lit;
        this.picked = picked;
        this.dna = dna;
        InvalidateVisual();
    }

    private (Point Centre, double Radius) Geometry(Size size) => (new Point(size.Width / 2, size.Height / 2), Math.Max(10, Math.Min(size.Width, size.Height) / 2 - 6));

    private string? Hit(Point p)
    {
        var (c, r) = Geometry(RenderSize);
        double dx = p.X - c.X, dy = p.Y - c.Y;
        var a = Math.Atan2(dx, -dy) * 180 / Math.PI;
        if (a < 0) a += 360;
        return GXCodonTable.Hit(Math.Sqrt(dx * dx + dy * dy) / r, a);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = Hit(e.GetPosition(this));
        if (h == Hovered) return;
        Hovered = h;
        ToolTip = h is { Length: 3 } ? $"{GXCodonTable.Letters(h, dna)} → {GXKit.Name(BioCode.AminoAcid(h) ?? '?')}" : null;
        System.Windows.Automation.AutomationProperties.SetHelpText(this, h is { Length: 3 } ? $"{h}: {GXKit.Name(BioCode.AminoAcid(h) ?? '?')}" : "The grid view lists every codon");
        InvalidateVisual();
        HoverChanged?.Invoke();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (Hovered == null) return;
        Hovered = null;
        InvalidateVisual();
        HoverChanged?.Invoke();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (Hit(e.GetPosition(this)) is not { } h) return;
        GeneExpressionModel.Picked = h.Length == 3 ? h : BioCode.Entries.FirstOrDefault(x => x.Codon.StartsWith(h, StringComparison.Ordinal)).Codon ?? h;
        e.Handled = true;
    }

    private static Point At(Point c, double r, double deg)
    {
        var t = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Sin(t), c.Y - r * Math.Cos(t));
    }

    private static StreamGeometry Sector(Point c, double r0, double r1, double a0, double a1)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(c, r0, a0), true, true);
            var n = Math.Max(2, (int)((a1 - a0) / 2));
            for (var k = 0; k <= n; k++) ctx.LineTo(At(c, r1, a0 + (a1 - a0) * k / n), true, true);
            for (var k = n; k >= 0; k--) ctx.LineTo(At(c, r0, a0 + (a1 - a0) * k / n), true, true);
        }
        g.Freeze();
        return g;
    }

    protected override void Draw(DrawingContext dc, Size size)
    {
        var (c, R) = Geometry(size);
        var rings = GXCodonTable.RingRadii.Select(x => x * R).ToArray();
        var picking = picked != null ? new[] { picked } : Array.Empty<string>();
        var hover = Hovered ?? "";
        var any = lit.Count > 0 || picking.Length > 0;
        var edge = GXDraw.Pen(Primary, 1, 0.18);
        var thin = GXDraw.Pen(Primary, 0.5, 0.18);
        // (the three rings of bases, each sector on its base's tint with its letter)
        for (var ring = 0; ring < 3; ring++)
        {
            double r0 = rings[ring], r1 = rings[ring + 1];
            foreach (var s in GXCodonTable.Sectors(ring))
            {
                var shown = GXCodonTable.Letters(s.Prefix[^1].ToString(), dna)[0];
                var colour = BioColors.Base(shown);
                var inLit = lit.Any(x => x.StartsWith(s.Prefix, StringComparison.Ordinal)) || picking.Any(x => x.StartsWith(s.Prefix, StringComparison.Ordinal));
                var dim = any && !inLit;
                var under = hover.Length > 0 && (hover.StartsWith(s.Prefix, StringComparison.Ordinal) || s.Prefix.StartsWith(hover, StringComparison.Ordinal));
                dc.DrawGeometry(GXDraw.Brush(colour, dim ? 0.06 : (under ? 0.36 : 0.18)), ring == 2 ? thin : edge, Sector(c, r0, r1, s.From, s.To));
                var size0 = ring == 0 ? 22 : (ring == 1 ? 15 : Math.Max(8, Math.Min(11, R * 0.045)));
                Say(dc, shown.ToString(), At(c, (r0 + r1) / 2, s.Mid), size0, FontWeights.Bold, GXDraw.Alpha(colour, dim ? 0.35 : 1), mono: true);
            }
        }
        // (the amino acids outside: a band in each class's colour, and the name read outward)
        double band0 = rings[3] + 3, band1 = rings[3] + R * 0.05;
        var accentPen = GXDraw.Pen(Accent, 2);
        var startPen = GXDraw.Pen(GXColors.Start.ToColor(), 2);
        foreach (var g in GXCodonTable.WheelLabels)
        {
            var on = g.Codons.Any(x => lit.Contains(x) || picking.Contains(x));
            var dim = any && !on;
            var colour = g.Aa == '*' ? GXColors.Stop.ToColor() : BioColors.AminoAcid(g.Aa);
            dc.DrawGeometry(GXDraw.Brush(colour, dim ? 0.2 : 0.85), null, Sector(c, band0, band1, g.From + 0.4, g.To - 0.4));
            if (on) dc.DrawGeometry(null, accentPen, Sector(c, rings[2], band1, g.From, g.To));
            var label = GXKit.Three(g.Aa);
            var mid = (g.From + g.To) / 2;
            var at = At(c, GXCodonTable.LabelRadius * R, mid);
            // (read outward on the right half, inward on the left, so no name is upside down)
            dc.PushTransform(new RotateTransform(mid <= 180 ? mid - 90 : mid + 90, at.X, at.Y));
            Say(dc, label, at, Math.Max(9, Math.Min(12, R * 0.05)), on ? FontWeights.Bold : FontWeights.SemiBold, dim ? GXDraw.Alpha(Secondary, 0.6) : K.Darkened(colour));
            dc.Pop();
            // (the start codon outlined in green)
            if (g.Codons.Contains("AUG")) dc.DrawGeometry(null, startPen, Sector(c, rings[2], rings[3], g.From, g.To));
        }
        // (the centre: what is under the pointer, else how to read it)
        dc.DrawEllipse(GXDraw.Brush(Primary, 0.04), null, c, rings[0] - 2, rings[0] - 2);
        if (Hovered is { Length: 3 } h && BioCode.AminoAcid(h) is { } aa)
        {
            Say(dc, GXCodonTable.Letters(h, dna), new Point(c.X, c.Y - 8), 15, FontWeights.Bold, Primary, mono: true);
            Say(dc, GXKit.Three(aa), new Point(c.X, c.Y + 10), 12, FontWeights.SemiBold, Secondary);
        }
        else
        {
            Say(dc, "5′→3′", new Point(c.X, c.Y - 7), 11, FontWeights.SemiBold, Secondary);
            Say(dc, "from here", new Point(c.X, c.Y + 8), 10, FontWeights.Normal, Secondary);
        }
    }
}

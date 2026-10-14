using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// DNA & RNA (spec §4.2), the Mac's DNATool: five pages chosen in its header — the double helix in 3D (cartoon or atom by
// atom, with its ends, grooves, measurements and H-bonds, pressable, unzippable), the nucleotides (3D, by part, numbered,
// DNA against RNA, and the base pairs in 2D), a strand's facts (its complement, reverse complement, GC, Chargaff, mass
// and melting temperature), replication stepped through at a fork, and RNA (an mRNA's parts, yeast tRNA-Phe as a
// cloverleaf and as its 3D L, the ribosomes). What it keeps is DNAToolModel's (Model/DNAToolModel.cs), remembered as
// SimplBio.dna.<field>; each page is a view of its own (DNAHelixPage.cs …), made again when the page changes. A launch
// can open any page in any state (--tool dna --tool-state helix:style=atoms:grooves …; README, Launch flags).

#pragma warning disable CA2255
internal static class DNAToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("dna", (c, state) => new DNATool(c, state), DNATool.Pages);
}
#pragma warning restore CA2255

/// <summary>DNA &amp; RNA: its pages in a host that shows the one chosen in the header.</summary>
internal sealed class DNATool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("helix", "Double helix", SymbolRegular.Ribbon24, "B-DNA in 3D"),
        new("nucleotides", "Nucleotides", SymbolRegular.Molecule24, "The eight nucleotides and ATP"),
        new("sequence", "Sequence", SymbolRegular.TextT24, "A strand, its complement and its facts"),
        new("replication", "Replication", SymbolRegular.BranchFork24, "A replication fork, step by step"),
        new("rna", "RNA", SymbolRegular.Pulse24, "mRNA, tRNA and ribosomes"),
    };

    private readonly ToolContext c;
    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private string? shown;

    public DNATool(ToolContext c, BioLaunchState? state)
    {
        this.c = c;
        DnaPrefs.Load();
        TakeHandOff(c);
        if (state != null) Apply(state);
        Children.Add(host);
        Show(false);
        void OnNav(string k) { if (k == "dna") Show(true); }
        Loaded += (_, _) => { BioNav.Changed -= OnNav; BioNav.Changed += OnNav; Show(false); };
        Unloaded += (_, _) => BioNav.Changed -= OnNav;
    }

    private void Show(bool animate)
    {
        var page = BioNav.Page("dna");
        if (page == shown && host.Content != null) return;
        shown = page;
        FrameworkElement view = page switch
        {
            "nucleotides" => new DnaNucleotidesPage(),
            "sequence" => new DnaSequencePage(c),
            "replication" => new DnaReplicationPage(c),
            "rna" => new DnaRnaPage(c),
            _ => new DnaHelixPage(),
        };
        host.Content = view;
        if (animate) Motion.FadeIn(view, Motion.Medium);
    }

    /// <summary>
    /// What another tool handed over (spec §3.2; both apps' words): a strand (<c>sequence</c>, RNA letters written as DNA)
    /// shown on the Sequence page, a nucleotide (<c>nucleotide</c>, "DT"; <c>molecule</c> read as the same) on the
    /// Nucleotides page, or just a page (<c>page</c>: Cell Division's "What happens in S phase" opens Replication) —
    /// <c>page</c> also says where a strand or a nucleotide is shown.
    /// </summary>
    private static void TakeHandOff(ToolContext c)
    {
        var page = c.TakeInfo("page") is { } p && Pages.Any(x => x.Id == p) ? p : null;
        var nucleotide = c.TakeInfo("nucleotide") ?? c.TakeInfo("molecule");
        if (c.TakeInfo("sequence") is { Length: > 0 } handed)
        {
            DNAToolModel.Quietly(() => DNAToolModel.SetSequence(handed.ToUpperInvariant().Replace('U', 'T')));
            BioNav.Set("dna", page ?? "sequence");
        }
        else if (NucleotideCode(nucleotide) is { } code)
        {
            DNAToolModel.Quietly(() => { DNAToolModel.Nucleotide = code; DNAToolModel.ShowPairs = false; });
            BioNav.Set("dna", page ?? "nucleotides");
        }
        else if (page != null) BioNav.Set("dna", page);
    }

    /// <summary>A nucleotide's code as the tool names it ("dt" → "DT"), else null.</summary>
    private static string? NucleotideCode(string? s) =>
        s == null ? null : DnaNucleotides.Codes.FirstOrDefault(x => string.Equals(x, s.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>What a launch asked for (the screenshot suite's pictures): put in the model without being remembered. The
    /// Mac's words, with this app's first ones beside them. helix: <c>style=atoms|space|cartoon</c>, <c>grooves</c>,
    /// <c>measure</c>, <c>ends</c>/<c>noends</c>, <c>nohbonds</c>, <c>unzip=0.5</c>, <c>pair=5</c> (selects base pair 5),
    /// <c>seq=ATGC…</c> (or --bio-seq); nucleotides: <c>mol=DT</c> (or <c>nt=</c>), <c>pairs</c> (or <c>bases</c>), <c>model</c>,
    /// <c>au</c>, <c>numbers</c>, <c>compare</c>, <c>parts</c>/<c>noparts</c> (or <c>cpk</c>), <c>part=sugar</c>; replication:
    /// <c>step=6</c> (or --bio-step); rna: <c>view=cloverleaf|lshape|mrna|ribosomes</c> (or <c>trna</c>, <c>trna3d</c>,
    /// <c>mrna</c>, <c>ribosome</c>), <c>modified</c>, <c>base=34</c>, <c>arm=anticodon</c>.</summary>
    private static void Apply(BioLaunchState s)
    {
        DNAToolModel.Quietly(() =>
        {
            if (s.Value("seq") is { Length: > 0 } seq) DNAToolModel.SetSequence(seq);
            if (DnaStyles.Parse(s.Value("style") ?? s.Value("DNAStyle")) is { } st) DNAToolModel.Style = st;
            if (s.Has("grooves")) DNAToolModel.ShowGrooves = true;
            if (s.Has("measure")) DNAToolModel.ShowMeasure = true;
            if (s.Has("ends")) DNAToolModel.ShowEnds = true;
            if (s.Has("noends")) DNAToolModel.ShowEnds = false;
            if (s.Has("nohbonds")) DNAToolModel.ShowHBonds = false;
            if (double.TryParse(s.Value("unzip"), NumberStyles.Float, CultureInfo.InvariantCulture, out var u)) DNAToolModel.Unzip = Math.Clamp(u, 0, 1);
            if (int.TryParse(s.Value("pair"), out var pair) && pair >= 1 && pair <= DNAToolModel.HelixSequence.Length) DNAToolModel.Selected = $"bp:{pair - 1}";
            if (NucleotideCode(s.Value("mol") ?? s.Value("nt") ?? s.Value("Nucleotide")) is { } code) DNAToolModel.Nucleotide = code;
            if (s.Has("pairs") || s.Has("bases")) DNAToolModel.ShowPairs = true;
            if (s.Has("model")) DNAToolModel.ShowPairs = false;
            if (s.Has("au")) DNAToolModel.PairWithUracil = true;
            if (s.Has("numbers")) DNAToolModel.ShowNumbers = true;
            if (s.Has("compare")) DNAToolModel.ShowDifferences = true;
            if (s.Has("parts")) DNAToolModel.ShowParts = true;
            if (s.Has("cpk") || s.Has("noparts")) DNAToolModel.ShowParts = false;
            if (s.Value("part") is { } part) DNAToolModel.NucleotidePart = part;
            if (int.TryParse(s.Value("step"), out var step)) DNAToolModel.Replication.Go(Math.Clamp(step - 1, 0, DnaReplication.Steps.Length - 1), animated: false);
            foreach (var v in new[] { "mrna", "trna", "trna3d", "ribosome", "ribosomes", "cloverleaf", "lshape" })
                if (s.Has(v) && DnaStyles.ParseRna(v) is { } rv) DNAToolModel.RnaView = rv;
            if (DnaStyles.ParseRna(s.Value("view")) is { } rv2) DNAToolModel.RnaView = rv2;
            if (s.Has("modified")) DNAToolModel.ShowModified = true;
            if (int.TryParse(s.Value("base"), out var b) && b is >= 1 and <= 76) DNAToolModel.TrnaSelected = b;
            if (s.Value("arm")?.ToLowerInvariant() is { } arm && DnaTrna.Arms.Any(a => a.Key == arm)) DNAToolModel.TrnaArmSelected = "arm:" + arm;
        });
    }
}

/// <summary>DNA &amp; RNA's settings in Prefs (SimplBio.dna.&lt;field&gt;, as text): read once a run, written as they change.</summary>
internal static class DnaPrefs
{
    private static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        DNAToolModel.Restore(f => BioPrefs.String("dna", f));
        DNAToolModel.Remember = (f, v) => BioPrefs.Set("dna", f, v switch { bool b => b ? "true" : "false", null => null, _ => Convert.ToString(v, CultureInfo.InvariantCulture) });
    }
}

/// <summary>What DNA &amp; RNA's pages draw with.</summary>
internal static class DnaWpf
{
    /// <summary>The chemistry tables' colours and radii for the helix's and the nucleotides' elements, read on the UI thread
    /// and handed to a scene made off it.</summary>
    public static DnaAtomPaint Paint()
    {
        var hex = new Dictionary<string, string>();
        var radius = new Dictionary<(string, DnaStyle), double>();
        foreach (var e in new[] { "C", "N", "O", "P", "H" })
        {
            hex[e] = BioMoleculeScene.CpkHex(e);
            radius[(e, DnaStyle.Atoms)] = BioMoleculeScene.Radius(e, BioAtomStyle.BallStick);
            radius[(e, DnaStyle.Cartoon)] = radius[(e, DnaStyle.Atoms)];
            radius[(e, DnaStyle.SpaceFill)] = BioMoleculeScene.Radius(e, BioAtomStyle.SpaceFill);
        }
        return new DnaAtomPaint(e => hex.GetValueOrDefault(e, "#c8c8c8"), (e, s) => radius.GetValueOrDefault((e, s), 0.4));
    }

    /// <summary>Whether white reads on a colour (else near-black does).</summary>
    public static bool WhiteOn(string hex)
    {
        var (r, g, b) = BioPalette.Rgb(hex);
        static double L(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        var lum = 0.2126 * L(r) + 0.7152 * L(g) + 0.0722 * L(b);
        return (1.05) / (lum + 0.05) >= (lum + 0.05) / 0.05;
    }

    /// <summary>The words drawn over the whole model (<see cref="DnaLetter.OnTop"/>) as the view's marks.</summary>
    public static IEnumerable<BioMark> Marks(IReadOnlyList<DnaLetter>? letters) =>
        letters?.Where(l => l.OnTop).Select(l => new BioMark(l.Text, l.Anchor, l.Size, l.Atom, l.AtomRadius)) ?? Enumerable.Empty<BioMark>();

    /// <summary>Words over a scene as billboards: white or near-black on a colour (a base's letter), else the page's text
    /// colour; those drawn over the whole model are the view's marks instead (<see cref="Marks"/>).</summary>
    public static IEnumerable<BioBillboard> Billboards(IReadOnlyList<DnaLetter>? letters, bool dark)
    {
        if (letters == null) yield break;
        foreach (var l in letters)
        {
            if (l.OnTop) continue;
            Color colour;
            bool glow;
            if (l.Behind is { } behind)
            {
                glow = WhiteOn(behind);
                colour = glow ? Colors.White : Color.FromRgb(0x1c, 0x1c, 0x1e);
            }
            else
            {
                glow = dark;
                colour = dark ? Color.FromRgb(0xf5, 0xf5, 0xf7) : Color.FromRgb(0x1c, 0x1c, 0x1e);
            }
            yield return new BioBillboard(BioBillboards.Text(l.Text, l.Size, colour, glow), l.Anchor, l.Lift, l.Id);
        }
    }


    /// <summary>A choice among a few, as a wide button that is the accent's when chosen (the Periodic Table's).</summary>
    public static System.Windows.Controls.Button Choice(string text, string? sub, bool chosen, Action pick, Color? swatch = null)
    {
        var words = K.V(1, K.Text(text, Ty.BodyStrong));
        if (sub != null) words.Children.Add(K.Wrapped(sub, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        UIElement face = words;
        if (swatch is { } sw)
        {
            var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = K.Brush(sw), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            face = K.Columns("auto, *", dot.Col(0), words.Col(1));
        }
        var inner = ChemKit.Tint(face, chosen ? B.Accent : B.Text, chosen ? 0.16 : 0.05, 8);
        if (inner.Children[^1] is FrameworkElement fe) fe.Margin = new Thickness(12, 8, 12, 9);
        var b = K.Subtle(inner, pick, new Thickness(0), 8);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        System.Windows.Automation.AutomationProperties.SetName(b, chosen ? $"{text}, chosen" : text);
        return b;
    }

    /// <summary>The bases' colours and letters, and the strands' (never colour alone).</summary>
    public static FrameworkElement BaseLegend(string bases = "ATGC", bool strands = true)
    {
        var items = bases.Select(b => (BioColors.Base(b), $"{b} · {DnaNucleotides.BaseName(b)}{(DnaNucleotides.IsPurine(b) ? " (purine)" : " (pyrimidine)")}")).ToList();
        if (strands)
        {
            items.Add((BioPalette.Strand1.ToColor(), "Strand 1 backbone (5′→3′ up)"));
            items.Add((BioPalette.Strand2.ToColor(), "Strand 2 backbone (5′→3′ down)"));
        }
        return BioUI.Legend(items);
    }

    /// <summary>A 3D view's card: its title at the left, what the page puts at the right (Turn, Reset view …), the view.</summary>
    public static Border SceneCard(UIElement title, UIElement? trailing, BioSceneView scene, string hint = "Drag to turn · scroll to zoom · press a part to learn about it")
    {
        var bar = K.Columns("*, auto", title.Col(0));
        if (trailing is FrameworkElement t) { t.VerticalAlignment = VerticalAlignment.Center; bar.Children.Add(t.Col(1)); }
        return BioSceneCard.Make(bar, scene, hint);
    }

    /// <summary>"6,196.87".</summary>
    public static string N(double v, string format = "#,##0.00") => v.ToString(format, CultureInfo.InvariantCulture);
}

/// <summary>A setting with its switch at the right (the molecule builder's), made once and kept: <see cref="Sync"/> shows
/// the model's value without setting it again, so a page drawing itself afresh never replaces the switch under the pointer.</summary>
internal sealed class DnaToggle : Grid
{
    private readonly ToggleSwitch s = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool syncing;

    public DnaToggle(string text, Action<bool> set, string? tip = null)
    {
        s.Checked += (_, _) => { if (!syncing) set(true); };
        s.Unchecked += (_, _) => { if (!syncing) set(false); };
        System.Windows.Automation.AutomationProperties.SetName(s, text);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(K.Wrapped(text, Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0));
        Children.Add(s.Col(1));
        if (tip != null) ToolTip = tip;
    }

    public DnaToggle Sync(bool on)
    {
        if (s.IsChecked == on) return this;
        syncing = true;
        s.IsChecked = on;
        syncing = false;
        return this;
    }
}

/// <summary>
/// A page laid out in two columns (the drawing and its cards) from 1000 wide, the drawing over the cards below that — for
/// the pages whose 2D drawing needs more room than the three-column layout's middle (replication: beside a column on a
/// 1366 window its letters and enzymes' names came out 7 px tall). Over the cards, the drawing is as tall as leaves 90 of
/// the page for the first card's title under it (240 to 640), so the step's card is in sight under it and the fork's words
/// stay readable in a tool's popup. (It once gave the drawing the page's whole height, and the step's card sat out of
/// sight below it.)
/// </summary>
internal sealed class DnaTwoColumns : Grid
{
    private readonly FrameworkElement main, side;
    private readonly ScrollViewer sideScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly ScrollViewer all = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private bool? wide;

    public DnaTwoColumns(FrameworkElement main, FrameworkElement side)
    {
        this.main = main;
        this.side = side;
        Margin = new Thickness(40, 8, 40, 24);
        // (held only on the widest screens, as the shared work layout is: the drawing takes the room a wide popup gives)
        MaxWidth = 2200;
        side.VerticalAlignment = VerticalAlignment.Top;
        SizeChanged += (_, e) => Lay(e.NewSize);
    }

    protected override Size MeasureOverride(Size available)
    {
        if (wide == true) main.Height = double.IsInfinity(available.Height) ? 620 : double.NaN;
        return base.MeasureOverride(available);
    }

    private static double NarrowHeight(double page) => Math.Max(240, Math.Min(640, page - 90));

    private void Lay(Size size)
    {
        var w = size.Width >= 1000;
        if (!w) main.Height = NarrowHeight(size.Height);
        if (wide == w) return;
        wide = w;
        foreach (var e in new[] { main, side }) ChemKit.Detach(e);
        sideScroll.Content = null;
        all.Content = null;
        Children.Clear();
        ColumnDefinitions.Clear();
        if (w)
        {
            main.Height = double.NaN;
            side.Width = 300;
            side.Margin = new Thickness(0, 0, 0, 20);
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(316) });
            sideScroll.Content = side;
            Children.Add(main.Col(0));
            Children.Add(sideScroll.Col(2));
        }
        else
        {
            side.Width = double.NaN;
            side.Margin = new Thickness(0, 18, 0, 24);
            main.Height = NarrowHeight(size.Height);
            all.Content = K.V(0, main, side);
            Children.Add(all);
        }
    }
}

/// <summary>A drawing made in its own units and fitted to the room it is given (centred, never stretched), drawn by the
/// page in OnRender: the replication fork, the cloverleaf, the base pairs, the ribosomes.</summary>
internal abstract class DnaDrawing : FrameworkElement
{
    /// <summary>The drawing's size in its own units.</summary>
    protected abstract Size Design { get; }

    /// <summary>Where in its own units the part fitted to the room starts (from the top): what it draws above that is
    /// never shown, so a drawing can leave out the empty band its units leave over what it draws.</summary>
    protected virtual double DesignTop => 0;

    /// <summary>Its units → the element's, and back.</summary>
    protected Matrix Fit
    {
        get
        {
            var d = Design;
            if (ActualWidth <= 0 || ActualHeight <= 0 || d.Width <= 0 || d.Height <= 0) return Matrix.Identity;
            var s = Math.Min(ActualWidth / d.Width, ActualHeight / d.Height);
            var m = Matrix.Identity;
            m.Translate(0, -DesignTop);
            m.Scale(s, s);
            m.Translate((ActualWidth - d.Width * s) / 2, (ActualHeight - d.Height * s) / 2);
            return m;
        }
    }

    protected Point ToDesign(Point p)
    {
        var m = Fit;
        if (!m.HasInverse) return p;
        m.Invert();
        return m.Transform(p);
    }

    protected DnaDrawing()
    {
        SnapsToDevicePixels = false;
        void Again() => InvalidateVisual();
        Loaded += (_, _) => { Appearance.Changed -= Again; Appearance.Changed += Again; };
        Unloaded += (_, _) => Appearance.Changed -= Again;
    }

    protected override Size MeasureOverride(Size available)
    {
        var d = Design;
        if (double.IsInfinity(available.Width) && double.IsInfinity(available.Height)) return d;
        if (double.IsInfinity(available.Height)) return new Size(available.Width, available.Width * d.Height / d.Width);
        if (double.IsInfinity(available.Width)) return new Size(available.Height * d.Width / d.Height, available.Height);
        return available;
    }

    protected sealed override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.PushTransform(new MatrixTransform(Fit));
        Draw(dc);
        dc.Pop();
    }

    protected abstract void Draw(DrawingContext dc);

    // ---- what the drawings share ----

    protected static bool Dark => Appearance.Dark;
    protected static Color TextColour => Dark ? Color.FromRgb(0xf5, 0xf5, 0xf7) : Color.FromRgb(0x1c, 0x1c, 0x1e);
    protected static Color Text2Colour => Dark ? Color.FromRgb(0xae, 0xae, 0xb2) : Color.FromRgb(0x6e, 0x6e, 0x73);
    protected static Color LineColour => Dark ? Color.FromRgb(0x8e, 0x8e, 0x93) : Color.FromRgb(0x8e, 0x8e, 0x93);

    private static readonly Typeface Regular = new(K.TextFace, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Strong = new(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Mono = new(BioUI.Mono, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    protected FormattedText Words(string s, double size, Color colour, bool strong = false, bool mono = false) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mono ? Mono : strong ? Strong : Regular, size, K.Brush(colour),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Words placed by their anchor: 0 left, 0.5 centre, 1 right; vertically centred on y.</summary>
    protected void Say(DrawingContext dc, string s, double x, double y, double size, Color colour, double align = 0.5, bool strong = false, bool mono = false)
    {
        var t = Words(s, size, colour, strong, mono);
        dc.DrawText(t, new Point(x - t.Width * align, y - t.Height / 2));
    }

    protected static Pen PenOf(Color c, double thickness, bool dashed = false)
    {
        var p = new Pen(K.Brush(c), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (dashed) p.DashStyle = new DashStyle(new[] { 1.6, 1.8 }, 0);
        p.Freeze();
        return p;
    }

    protected static Color Base(char b) => BioColors.Base(b);
}

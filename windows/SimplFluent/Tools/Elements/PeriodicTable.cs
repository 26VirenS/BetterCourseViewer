using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

#pragma warning disable CA2255
internal static class PeriodicTableRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        ToolRegistry.Register("ptable", c => new PeriodicTableTool(c), PeriodicTableCompact.Make, c => new PTHeaderControls(c));
}
#pragma warning restore CA2255

// ---- one element's square ----

/// <summary>
/// One element's square (the Mac's PTCell): its number, symbol and — as the table is coloured — its name or its value, in
/// its colour for the view (a kind's washed, a property's scale solid with white words); dimmed when a search or a kind
/// leaves it out, edged when a search finds it; a touch larger under the pointer.
/// </summary>
internal sealed class PTCell : Border
{
    public readonly PTElement Element;
    private readonly bool compact;
    private readonly TextBlock number, symbol, word;
    private readonly Viewbox wordFit;
    private readonly SolidColorBrush fill = new(Colors.Transparent);
    private readonly SolidColorBrush edge = new(Colors.Transparent);
    private readonly ScaleTransform zoom = new(1, 1);
    private PTColouring colouring = PTColouring.Category;
    private bool painted, dimmed, matched, hover;
    private double cell = -1;

    public PTCell(PTElement e, bool compact)
    {
        Element = e;
        this.compact = compact;
        number = Line(e.Number.ToString(CultureInfo.InvariantCulture), FontWeights.Medium).Tabular();
        number.HorizontalAlignment = HorizontalAlignment.Left;
        number.Opacity = 0.75;
        symbol = Line(e.Symbol, FontWeights.Bold);
        symbol.HorizontalAlignment = HorizontalAlignment.Center;
        word = Line(e.Name, FontWeights.Normal).Tabular();
        wordFit = PTKit.Fit(word);
        wordFit.Opacity = 0.8;
        Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { number, symbol, wordFit } };
        Background = fill;
        BorderBrush = edge;
        BorderThickness = new Thickness(compact ? 1 : 1.5);
        RenderTransform = zoom;
        RenderTransformOrigin = new Point(0.5, 0.5);
        SnapsToDevicePixels = true;
        Cursor = Cursors.Hand;
        ToolTip = $"{e.Name} · {e.Number}";
        AutomationProperties.SetName(this, $"{e.Name}, {e.Number}");
        MouseEnter += (_, _) => Hover(true);
        MouseLeave += (_, _) => Hover(false);
    }

    private static TextBlock Line(string s, FontWeight w) => new()
    {
        Text = s, FontWeight = w, FontFamily = K.TextFace, TextTrimming = TextTrimming.None, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    /// <summary>The square's size: its words' sizes, and which show (the number from 34, the name or value from 48).</summary>
    public void Size(double size)
    {
        if (Math.Abs(size - cell) < 0.01) return;
        cell = size;
        CornerRadius = new CornerRadius(Math.Max(2, size * 0.14));
        Padding = compact ? new Thickness(0) : new Thickness(size * 0.08, size * 0.05, size * 0.08, size * 0.05);
        void Font(TextBlock t, double s) { t.FontSize = Math.Max(1, s); t.LineHeight = Math.Max(1, s * 1.18); }
        Font(number, size * 0.19);
        Font(symbol, size * (compact ? 0.48 : 0.36));
        Font(word, size * 0.14);
        number.Visibility = !compact && size >= 34 ? Visibility.Visible : Visibility.Collapsed;
        wordFit.Visibility = !compact && size >= 48 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Its colour and marks for the view, the search and the kind lit.</summary>
    public void Paint(PTColouring c, bool dim, bool match, bool animate)
    {
        var changed = !painted || c != colouring;
        var marks = !painted || dim != dimmed || match != matched;
        if (!changed && !marks) return;
        painted = true;
        colouring = c;
        dimmed = dim;
        matched = match;
        if (changed)
        {
            word.Text = c == PTColouring.Category ? Element.Name : c.Text(Element);
            if (c != PTColouring.Category) { word.Foreground = Brushes.White; number.Foreground = Brushes.White; symbol.Foreground = Brushes.White; }
            else { word.Fg(B.Text); number.Fg(B.Text); symbol.Fg(B.Text); }
        }
        Fill(animate && changed);
        if (animate) Motion.To(this, OpacityProperty, dim ? 0.22 : 1, Motion.Normal);
        else { BeginAnimation(OpacityProperty, null); Opacity = dim ? 0.22 : 1; }
    }

    private void Fill(bool animate)
    {
        var color = colouring.Color(Element);
        var solid = colouring != PTColouring.Category;
        var alpha = solid ? (hover ? 1 : 0.88) : (hover ? 0.42 : matched ? 0.5 : 0.24);
        var to = K.WithAlpha(color, alpha);
        if (animate && Motion.On) fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, Motion.Ms(420)) { EasingFunction = Motion.Gentle });
        else { fill.BeginAnimation(SolidColorBrush.ColorProperty, null); fill.Color = to; }
        edge.Color = matched ? (solid ? Colors.White : color) : Colors.Transparent;
    }

    private void Hover(bool on)
    {
        hover = on;
        Panel.SetZIndex(this, on ? 1 : 0);
        Fill(false);
        if (compact) return;
        Motion.To(zoom, ScaleTransform.ScaleXProperty, on ? 1.08 : 1, Motion.Normal);
        Motion.To(zoom, ScaleTransform.ScaleYProperty, on ? 1.08 : 1, Motion.Normal);
    }
}

// ---- the table drawn ----

/// <summary>
/// The table drawn (the Mac's PTGrid): each element in its place (rows 1–7, the lanthanides and actinides under them after
/// a gap), sized to fit its width at the table's own proportions; in its colour for what the table is coloured by; the
/// elements a search or a kind leaves out dimmed; and, in the space the table leaves at its top, the element under the
/// pointer (only while the pointer is on it — a press opens it).
/// </summary>
internal sealed class PTGrid : Panel
{
    public const double GapRow = 0.45;
    private static double Ratio => 18 / (9 + GapRow);
    private readonly bool compact;
    private readonly List<PTCell> cells = new();
    private readonly ContentControl corner = new() { IsHitTestVisible = false, Focusable = false };
    private readonly TextBlock? lanthanides, actinides;
    private readonly DispatcherTimer leave = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private PTElement? shown;
    private PTColouring shownColouring;
    private double cornerCell = -1;
    private PTCell? down;

    /// <summary>A press on an element, with its square's place in the table.</summary>
    public Action<PTElement, Rect>? Pick;
    /// <summary>The element under the pointer, or none.</summary>
    public Action<PTElement?>? Hover;

    public double Cell { get; private set; } = 40;
    public double Gap => compact ? 2 : 4;

    public PTGrid(bool compact)
    {
        this.compact = compact;
        Children.Add(corner);
        Panel.SetZIndex(corner, 2);
        foreach (var e in PeriodicTable.Elements)
        {
            var c = new PTCell(e, compact);
            c.MouseEnter += (_, _) => { leave.Stop(); Hover?.Invoke(c.Element); };
            c.MouseLeave += (_, _) => leave.Start();
            c.PreviewMouseLeftButtonDown += (_, _) => down = c;
            c.MouseLeftButtonUp += (_, a) =>
            {
                if (down != c) return;
                down = null;
                a.Handled = true;
                Pick?.Invoke(c.Element, new Rect(X(c.Element), Y(c.Element), Cell, Cell));
            };
            if (!compact && e.Number == 6) c.TourTarget("ptCarbon");
            cells.Add(c);
            Children.Add(c);
        }
        leave.Tick += (_, _) => { leave.Stop(); Hover?.Invoke(null); };
        if (!compact)
        {
            // (the gap over the transition metals, rows 1–3: where the tour's card can sit over the table without hiding
            //  an element)
            PTTour.Rooms.AddOrUpdate(this, () => new Rect(2 * (Cell + Gap), 0, 10 * (Cell + Gap) - Gap, 3 * (Cell + Gap) - Gap));
            lanthanides = RowLabel("57–71");
            actinides = RowLabel("89–103");
            Children.Add(lanthanides);
            Children.Add(actinides);
        }
        Paint(PTColouring.Category, null, null, false);
    }

    private static TextBlock RowLabel(string s)
    {
        var t = new TextBlock { Text = s, FontWeight = FontWeights.Medium, FontFamily = K.TextFace, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        t.Fg(B.Text2);
        return t;
    }

    public PTCell? CellOf(int number) => cells.FirstOrDefault(c => c.Element.Number == number);

    /// <summary>Every square coloured for the view, and dimmed or edged for the search's matches and the kind lit.</summary>
    public void Paint(PTColouring colouring, HashSet<int>? matches, string? category, bool animate)
    {
        foreach (var c in cells)
        {
            var e = c.Element;
            var dim = (matches != null && !matches.Contains(e.Number)) || (category != null && e.Category != category);
            c.Paint(colouring, dim, matches?.Contains(e.Number) ?? false, animate);
        }
    }

    /// <summary>The element in the table's empty top (none: the space left empty).</summary>
    public void Show(PTElement? e, PTColouring colouring)
    {
        if (e?.Number == shown?.Number && colouring == shownColouring && Math.Abs(cornerCell - Cell) < 0.5) return;
        shown = e;
        shownColouring = colouring;
        cornerCell = Cell;
        corner.Content = e == null ? null : PTCornerCard.Make(e, Cell, colouring);
        if (e != null) Motion.FadeIn(corner, Motion.Fast);
    }

    private double X(PTElement e) => (e.X - 1) * (Cell + Gap);

    private double Y(PTElement e) => e.Y <= 7 ? (e.Y - 1) * (Cell + Gap) : 7 * (Cell + Gap) + Cell * GapRow + (e.Y - 9) * (Cell + Gap);

    protected override Size MeasureOverride(Size available)
    {
        double w = available.Width, h = available.Height, fw, fh;
        if (double.IsInfinity(w) && double.IsInfinity(h)) { fw = 18 * 40 + 17 * Gap; fh = fw / Ratio; }
        else if (double.IsInfinity(h) || h * Ratio >= w) { fw = w; fh = w / Ratio; }
        else { fh = h; fw = h * Ratio; }
        var cell = Math.Max(8, Math.Min((fw - 17 * Gap) / 18, (fh - 8 * Gap) / (9 + GapRow)));
        if (Math.Abs(cell - Cell) > 0.01)
        {
            Cell = cell;
            foreach (var c in cells) c.Size(cell);
            foreach (var t in new[] { lanthanides, actinides }) if (t != null) t.FontSize = Math.Max(8, cell * 0.2);
            if (shown != null && Math.Abs(cornerCell - cell) >= 0.5) { var s = shown; shown = null; Show(s, shownColouring); }
        }
        foreach (var c in cells) c.Measure(new Size(Cell, Cell));
        corner.Measure(new Size(10 * (Cell + Gap) - Gap, 3 * (Cell + Gap) - Gap));
        lanthanides?.Measure(new Size(Cell, Cell));
        actinides?.Measure(new Size(Cell, Cell));
        return new Size(Math.Max(0, fw), Math.Max(0, fh));
    }

    protected override Size ArrangeOverride(Size final)
    {
        foreach (var c in cells) c.Arrange(new Rect(X(c.Element), Y(c.Element), Cell, Cell));
        corner.Arrange(new Rect(2 * (Cell + Gap), 0, 10 * (Cell + Gap) - Gap, 3 * (Cell + Gap) - Gap));
        lanthanides?.Arrange(new Rect(2 * (Cell + Gap), 5 * (Cell + Gap), Cell, Cell));
        actinides?.Arrange(new Rect(2 * (Cell + Gap), 6 * (Cell + Gap), Cell, Cell));
        return final;
    }
}

/// <summary>
/// The element under the pointer, in the space at the table's top (the Mac's PTCornerCard): its symbol large on its
/// colour, its number, name, kind and mass, its electrons and — the table coloured by a property — its value.
/// </summary>
internal static class PTCornerCard
{
    public static FrameworkElement Make(PTElement e, double cell, PTColouring colouring)
    {
        var color = colouring.Color(e);
        var num = PTKit.Text(e.Number.ToString(CultureInfo.InvariantCulture), Math.Max(8, cell * 0.3), FontWeights.SemiBold, Colors.White).Tabular();
        num.HorizontalAlignment = HorizontalAlignment.Left;
        var sym = PTKit.Fit(PTKit.Text(e.Symbol, cell * 1.1, FontWeights.Bold, Colors.White));
        sym.VerticalAlignment = VerticalAlignment.Center;
        var tile = new Border
        {
            Width = cell * 2.4, Height = cell * 2.4, Padding = new Thickness(cell * 0.12), CornerRadius = new CornerRadius(cell * 0.3),
            Background = new SolidColorBrush(color), Child = K.Rows("auto, *", num.Row(0), sym.Row(1)), VerticalAlignment = VerticalAlignment.Center,
        };
        // (never smaller than reads: the pin's table is small)
        var words = new Stack { Orientation = Orientation.Vertical, Spacing = cell * 0.06, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(PTKit.Fit(PTKit.Text(e.Name, Math.Max(14, cell * 0.5), FontWeights.Bold), HorizontalAlignment.Left));
        words.Children.Add(PTKit.Fit(PTKit.Text($"{e.Kind.Label} · {e.MassText} u", Math.Max(11, cell * 0.26), FontWeights.Normal, K.Darkened(e.Kind.Color)), HorizontalAlignment.Left));
        words.Children.Add(PTKit.Fit(PTKit.Text(PTConfig.Pretty(e), Math.Max(10.5, cell * 0.24), FontWeights.Normal, B.Text2, PTKit.Mono), HorizontalAlignment.Left));
        if (colouring.Scaled())
        {
            var unit = colouring.Unit().Length == 0 || colouring.Value(e) == null ? "" : " " + colouring.Unit();
            words.Children.Add(PTKit.Fit(PTKit.Text($"{colouring.Label()}: {colouring.Text(e)}{unit}", Math.Max(10.5, cell * 0.24), FontWeights.SemiBold), HorizontalAlignment.Left));
        }
        words.Margin = new Thickness(cell * 0.3, 0, 0, 0);
        var g = K.Columns("auto, *", tile.Col(0), words.Col(1));
        g.Margin = new Thickness(cell * 0.3, 0, cell * 0.3, 0);
        g.VerticalAlignment = VerticalAlignment.Center;
        return g;
    }
}

// ---- the table as a tool ----

/// <summary>
/// The full Periodic Table (the Mac's PeriodicTableTool): its pages named in a bar of their own at its top (Table, Lewis
/// structures, Molecule builder, and Tour), the page shown named in the heading too; the table, coloured by any of its
/// properties (with the scale, each cell's value and the trend arrows), as large as the page shows whole — f-block and
/// all — its key and its colourings each on one line; the element under the pointer in its empty top, and a press
/// opening the element's own page, grown out of its square; on its other pages the Lewis structure maker and the 3D
/// molecule builder (Tools/Chemistry); and its own short tour, offered the first time it opens (never started unasked),
/// that can be skipped from any step or with Escape.
/// </summary>
/// <remarks>
/// The screenshot suite's flags (the Mac's -SimplPT… defaults): --ptable-element Fe (an element's page; a number, symbol or
/// name), --ptable-orbitals (its model showing orbitals), --ptable-cloud (its model showing the electron cloud),
/// --ptable-subshell 3d (with either of those, the subshell shown; "all" for the whole cloud), --ptable-colour
/// electronegativity (category, electronegativity, radius, ionization, affinity, melting, boiling, density, block, state),
/// --ptable-category metalloid (a kind lit), --ptable-hover Fe (that element's card in the table's top), --ptable-page
/// lewis|molecules, --ptable-molecule Ethanol, --ptable-lewis "NO₃⁻", --ptable-search ne (the header's search open with its
/// results), --ptable-tour 4 (the tour at its fourth step), --ptable-offer (the tour offered, as on a first opening), and
/// --ptable-pin-element 25 for the pin's flyout.
/// </remarks>
internal sealed class PeriodicTableTool : Grid
{
    private readonly ToolContext ctx;
    private readonly ScrollViewer tablePage;
    private readonly ContentControl chemHost = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Grid pageHost = new() { Visibility = Visibility.Collapsed };
    private readonly PTGrid grid = new(compact: false);
    private readonly ContentControl top = new() { Focusable = false };
    private readonly StackPanel colourBar = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer colourRow, legendRow;
    private readonly StackPanel legend = new() { Orientation = Orientation.Horizontal };
    private readonly ColumnDefinition gutter = new() { Width = new GridLength(0) };
    private readonly ContentControl across = new() { Focusable = false, Margin = new Thickness(0, 0, 0, 6) };
    private readonly ContentControl up = new() { Focusable = false, Width = 20, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock hint;
    private readonly ContentControl offerHost = new() { Focusable = false, Visibility = Visibility.Collapsed };
    private readonly PTPagesBar pagesBar;
    private FrameworkElement? lewis, molecules;
    private PTElementPage? page;
    private PTElement? open, hovered;
    private Point anchor = new(0.5, 0.5);
    private int version;
    private bool firstLoad = true;
    private bool orbitalsAsked;
    private bool cloudAsked;
    private string? subshellAsked;

    // the tour
    private List<PTTourStep> steps = new();
    private int? tourStep;
    private bool tourDone;
    private PTTourOverlay? overlay;
    private DispatcherTimer? nextTimer;
    private int? tourAsked;
    /// <summary>The tour offered ("Take a quick tour?") as the tool comes up — the first time it opens.</summary>
    private bool offerAsked;

    private PTColouring Colouring => PTState.Colouring;

    public PeriodicTableTool(ToolContext c)
    {
        ctx = c;
        ClipToBounds = false;

        // its pages, in a bar of their own over the page shown (an element's page covers it: that page has its own way back)
        pagesBar = new PTPagesBar(() => Tour(0));
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // the table's page: the key (or scale), what it is coloured by — each on one line, scrolled sideways where the page
        // is narrow — the table with its trends, as large as the page shows whole, and a line on using it
        colourBar.TourTarget("ptColours");
        colourRow = PTKit.ChipRow(colourBar);
        legendRow = PTKit.ChipRow(legend);
        var trendGrid = new Grid { MaxWidth = 1300, HorizontalAlignment = HorizontalAlignment.Left };
        trendGrid.ColumnDefinitions.Add(gutter);
        trendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        trendGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        trendGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        trendGrid.Children.Add(across.Row(0).Col(1));
        trendGrid.Children.Add(up.Row(1).Col(0));
        grid.HorizontalAlignment = HorizontalAlignment.Left;
        grid.VerticalAlignment = VerticalAlignment.Top;
        trendGrid.Children.Add(grid.Row(1).Col(1));
        grid.TourTarget("ptGrid");
        grid.Hover = e => { hovered = e; grid.Show(e, Colouring); CheckTour(); };
        grid.Pick = (e, rect) =>
        {
            var p = grid.TranslatePoint(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), this);
            OpenElement(e, p);
        };
        hint = K.Wrapped("Point at an element to see it here; press it to open its page. Type a symbol, name or number and press Return to open it.", Ty.Body, B.Text2);
        tablePage = ToolKit.Page(K.V(14, offerHost, top, colourRow, trendGrid, hint));
        tablePage.SizeChanged += (_, _) => FitTable();
        foreach (var part in new FrameworkElement[] { offerHost, top, colourRow, across, hint, grid }) part.SizeChanged += (_, _) => FitTable();

        Children.Add(pagesBar.Row(0));
        Children.Add(tablePage.Row(1));
        Children.Add(chemHost.Row(1));
        Children.Add(pageHost.Row(0, 2));

        // what it is handed (a pin's flyout: an element), and the screenshot suite's flags
        if (Launch.Take("", "ptable-colour") is { } col && PTColourings.Parse(col) is { } cv) PTState.Colouring = cv;
        if (Launch.Take("", "ptable-category") is { } cat && PTCategory.All.FirstOrDefault(k => string.Equals(k.Key, cat.Trim(), StringComparison.OrdinalIgnoreCase)) is { } kind) PTState.Category = kind.Key;
        if (Launch.Take("", "ptable-page") is { } pg && PTNav.Parse(pg) is { } pv) PTNav.Page = pv;
        orbitalsAsked = Launch.Take("", "ptable-orbitals") != null;
        cloudAsked = Launch.Take("", "ptable-cloud") != null;
        subshellAsked = Launch.Take("", "ptable-subshell");
        var molecule = Launch.Take("", "ptable-molecule");
        var lewisAsk = Launch.Take("", "ptable-lewis");
        var tourAt = Launch.Take("", "ptable-tour");
        var hover = Launch.Take("", "ptable-hover");
        var handed = PeriodicTable.Named(c.TakeInfo("element")) ?? PeriodicTable.Named(Launch.Take("", "ptable-element"));

        Recolour(false);
        if (PeriodicTable.Named(hover) is { } he) { hovered = he; grid.Show(he, Colouring); }
        if (handed != null) OpenElement(handed, null, animate: false);
        Present(false);
        if (molecule is { Length: > 0 }) Dispatcher.BeginInvoke(() => Hand(() => PTChemistry.OpenMolecule?.Invoke(molecule)), DispatcherPriority.Loaded);
        if (lewisAsk is { Length: > 0 }) Dispatcher.BeginInvoke(() => Hand(() => PTChemistry.OpenLewis?.Invoke(lewisAsk)), DispatcherPriority.Loaded);

        // the tour: its step for the screenshot suite (--ptable-tour 5, as the app's own --open tour:5), else on the
        // first opening — not in a development run or while the suite pictures the tool
        var picture = molecule != null || lewisAsk != null || hover != null || handed != null || orbitalsAsked || cloudAsked || Launch.Get("ptable-search") != null
                      || Launch.Get("ptable-colour") != null || Launch.Get("ptable-page") != null || Launch.IsDevelopment;
        if (tourAt != null) tourAsked = int.TryParse(tourAt.Trim(':', ' '), out var n) ? Math.Max(0, n - 1) : 0;
        else if (Launch.Take("", "ptable-offer") != null || (!c.Quick && !Prefs.Bool("simpl.ptTourSeen") && !picture)) offerAsked = true;

        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    /// <summary>
    /// The table as large as the page shows it whole, so the lanthanides and actinides need no scrolling: its height held
    /// to what the page has left under the tour's offer, its key and its colourings and above its line — its squares then
    /// as large as that height or the page's width allows, whichever is less (their names drop away below 48). Never so
    /// small a square is under 30: the page scrolls instead.
    /// </summary>
    private void FitTable()
    {
        var view = tablePage.ViewportHeight;
        if (view <= 0) return;
        double others;
        // (once laid out, what the page holds besides the table is measured whole — the scroller's extent less the table:
        //  its key, its colourings, its trends, its line and the room between them — so the table takes exactly the
        //  rest, never a little more than shows; before that, the parts are added up)
        if (tablePage.ExtentHeight > 0 && grid.ActualHeight > 0)
            others = tablePage.ExtentHeight - grid.ActualHeight + 2;
        else
        {
            static double Part(FrameworkElement e) => e.Visibility == Visibility.Collapsed ? 0 : e.ActualHeight + e.Margin.Top + e.Margin.Bottom + 14;
            others = Part(offerHost) + Part(top) + Part(colourRow) + Part(hint) + (across.Visibility == Visibility.Collapsed ? 0 : across.ActualHeight + across.Margin.Bottom)
                     + tablePage.Padding.Top + tablePage.Padding.Bottom + 2;
        }
        var h = Math.Max(30 * (9 + PTGrid.GapRow) + 8 * grid.Gap, view - others);
        if (double.IsInfinity(grid.MaxHeight) || Math.Abs(grid.MaxHeight - h) > 1) grid.MaxHeight = h;
    }

    private static void Hand(Action a)
    {
        try { a(); } catch (Exception e) { Log.Write($"ptable: {e.Message}"); }
    }

    // ---- what the tool listens to while it shows ----

    private void Attach()
    {
        Detach();
        PTNav.Changed += OnNav;
        PTNav.Chosen += OnChosen;
        PTNav.TourAsked += OnTourAsked;
        PTSearch.Changed += OnSearch;
        PTSearch.Submitted += OnSubmitted;
        PTSearch.Picked += OnPicked;
        PTTour.Changed += CheckTour;
        PTTour.Skip = SkipTour;
        Recolour(false);
        Present(false);
        Heading();
        if (firstLoad)
        {
            firstLoad = false;
            if (cloudAsked) page?.ShowModel(PTModel.Cloud, subshellAsked);
            else if (orbitalsAsked) page?.ShowModel(PTModel.Orbitals, subshellAsked);
            if (tourAsked is { } at) PTKit.After(1.2, () => { if (IsLoaded) Tour(at); });
            else if (offerAsked) Offer(true);
        }
        if (tourStep != null) DrawTour();
    }

    private void Detach()
    {
        PTNav.Changed -= OnNav;
        PTNav.Chosen -= OnChosen;
        PTNav.TourAsked -= OnTourAsked;
        PTSearch.Changed -= OnSearch;
        PTSearch.Submitted -= OnSubmitted;
        PTSearch.Picked -= OnPicked;
        PTTour.Changed -= CheckTour;
        if (PTTour.Skip == SkipTour) PTTour.Skip = null;
        overlay?.Hide();
    }

    private void OnNav()
    {
        // (a page chosen in the bar while an element is open: back from it to that page)
        if (open != null) CloseElement(false);
        Present(true);
        Heading();
        CheckTour();
    }

    /// <summary>The page shown named in the tool's heading ("Tools › Periodic table › Lewis structures", the table's name
    /// going back to the table); the table itself, the tool's name alone.</summary>
    private void Heading() =>
        ctx.ShowPage(PTNav.Page == PTPage.Table ? null : PTNav.Page.Label(), () => PTNav.Choose(PTPage.Table));

    /// <summary>
    /// "Take a quick tour?" (Windows 11's InfoBar) over the table the first time it opens, in place of a tour started
    /// unasked: Take the tour starts it; Not now, or its ×, puts it away for good (Tour, in the page bar, is there later).
    /// </summary>
    private void Offer(bool on)
    {
        if (!on)
        {
            if (offerHost.Visibility == Visibility.Collapsed) return;
            Motion.FadeOut(offerHost, () => { offerHost.Visibility = Visibility.Collapsed; offerHost.Content = null; offerHost.Opacity = 1; }, Motion.Normal);
            return;
        }
        void Away(bool take)
        {
            Prefs.Set("simpl.ptTourSeen", true);
            Offer(false);
            if (take) Tour(0);
        }
        var go = K.Button("Take the tour", () => Away(true), accent: true);
        var later = K.Button("Not now", () => Away(false));
        var close = K.IconButton(SymbolRegular.Dismiss16, () => Away(false), "Not now", 12, 28);
        close.VerticalAlignment = VerticalAlignment.Top;
        var words = K.V(2, K.Text("Take a quick tour?", Ty.BodyStrong),
            K.Wrapped("Eleven short steps through the table, Lewis structures and the 3D builder.", Ty.Body, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var buttons = K.H(8, go, later);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("auto, *, auto, auto",
            K.Icon(SymbolRegular.Map24, 20, B.AccentText).Margin(0, 2, 14, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0),
            words.Col(1), buttons.Margin(16, 0, 8, 0).Col(2), close.Col(3));
        var bar = new Border { Child = row, Padding = new Thickness(16, 12, 10, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        bar.Background = new SolidColorBrush(K.WithAlpha(Appearance.Accent, Appearance.Dark ? 0.16 : 0.08));
        bar.BorderBrush = new SolidColorBrush(K.WithAlpha(Appearance.Accent, 0.3));
        AutomationProperties.SetName(bar, "Take a quick tour of the periodic table?");
        offerHost.Content = bar;
        offerHost.Visibility = Visibility.Visible;
        Motion.FadeIn(offerHost, Motion.Medium);
    }

    private void OnChosen()
    {
        if (open != null) { CloseElement(true); CheckTour(); }
    }

    private void OnTourAsked() => Tour(0);

    private void OnSearch()
    {
        Repaint(true);
        CheckTour();
    }

    private void OnSubmitted()
    {
        if (PeriodicTable.Find(PTSearch.Query).FirstOrDefault() is not { } m) return;
        PTSearch.Query = "";
        OpenElement(m, null);
    }

    private void OnPicked(int n)
    {
        if (PeriodicTable.ByNumber(n) is not { } e) return;
        PTSearch.Query = "";
        OpenElement(e, null);
    }

    // ---- the table ----

    private void SetColouring(PTColouring c)
    {
        if (PTState.Colouring == c) return;
        PTState.Colouring = c;
        Recolour(true);
        CheckTour();
    }

    /// <summary>The table, its key and its trend drawn for what it is coloured by, the search and the kind lit.</summary>
    private void Recolour(bool animate)
    {
        var c = Colouring;
        Repaint(animate);
        top.Content = c == PTColouring.Category ? Legend() : c.Scaled() ? Scale(c) : KeyChips(c);
        colourBar.Children.Clear();
        foreach (var k in PTColourings.All)
        {
            var kk = k;
            var b = PTKit.Choice(k.Label(), c == k, () => SetColouring(kk));
            b.ToolTip = $"Colour the table by {k.Label().ToLowerInvariant()}";
            b.Margin = new Thickness(0, 0, 6, 6);
            colourBar.Children.Add(b);
        }
        Trend(c);
        if (animate) Motion.FadeIn(top, Motion.Normal);
    }

    /// <summary>The squares coloured, and dimmed or edged for the search and the kind lit.</summary>
    private void Repaint(bool animate)
    {
        var c = Colouring;
        grid.Paint(c, PTSearch.Matches(PTSearch.Query), c == PTColouring.Category ? PTState.Category : null, animate);
        grid.Show(hovered, c);
    }

    /// <summary>The kinds, a chip each in its colour on one line (kept, so a chip pressed far along stays in view): filled
    /// when it lights its elements.</summary>
    private FrameworkElement Legend()
    {
        var wrap = legend;
        wrap.Children.Clear();
        foreach (var k in PTCategory.All)
        {
            var on = PTState.Category == k.Key;
            var ink = PTKit.InkOn(k.Color);
            var dot = new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(on ? ink : k.Color), VerticalAlignment = VerticalAlignment.Center };
            var t = K.Text(k.Label, Ty.Caption);
            t.FontWeight = FontWeights.Medium;
            if (on) t.Foreground = new SolidColorBrush(ink);
            var tint = K.WithAlpha(k.Color, Appearance.Dark ? 0.26 : 0.18);
            var pill = new Border { Child = K.H(6, dot, t), Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(on ? k.Color : tint) };
            var key = k.Key;
            var b = K.Subtle(pill, () =>
            {
                PTState.Category = PTState.Category == key ? null : key;
                Repaint(true);
                top.Content = Legend();
            }, new Thickness(0), 13);
            b.MouseEnter += (_, _) => { if (!on) pill.Background = new SolidColorBrush(K.WithAlpha(k.Color, Appearance.Dark ? 0.4 : 0.3)); };
            b.MouseLeave += (_, _) => { if (!on) pill.Background = new SolidColorBrush(tint); };
            b.ToolTip = on ? "Show every kind" : $"Light the {k.Label.ToLowerInvariant()} elements";
            b.Margin = new Thickness(0, 0, 6, 6);
            AutomationProperties.SetName(b, k.Label);
            wrap.Children.Add(b);
        }
        return legendRow;
    }

    /// <summary>The scale of the property the table is coloured by: lowest to highest.</summary>
    private static FrameworkElement Scale(PTColouring c)
    {
        var stops = new GradientStopCollection();
        for (var i = 0; i <= 8; i++) stops.Add(new GradientStop(PTColourings.Ramp(i / 8.0), i / 8.0));
        var bar = new Border { Height = 8, Width = 300, CornerRadius = new CornerRadius(4), Background = new LinearGradientBrush(stops, 0), VerticalAlignment = VerticalAlignment.Center };
        var row = K.H(10, K.Text(c.Label(), Ty.BodyStrong), K.Text(c.LowText(), Ty.Caption, B.Text2).Tabular(), bar, K.Text(c.HighText(), Ty.Caption, B.Text2).Tabular());
        if (c.Unit().Length > 0) row.Children.Add(K.Text(c.Unit(), Ty.Caption, B.Text2));
        foreach (FrameworkElement k in row.Children) k.VerticalAlignment = VerticalAlignment.Center;
        row.Margin = new Thickness(0, 4, 0, 4);
        return row;
    }

    /// <summary>The blocks' or the states' colours.</summary>
    private static FrameworkElement KeyChips(PTColouring c)
    {
        var keys = c == PTColouring.Block
            ? new[] { ("s-block", PTColourings.BlockColor("s")), ("p-block", PTColourings.BlockColor("p")), ("d-block", PTColourings.BlockColor("d")), ("f-block", PTColourings.BlockColor("f")) }
            : new[] { ("Solid", PTColourings.StateColor("Solid")), ("Liquid", PTColourings.StateColor("Liquid")), ("Gas", PTColourings.StateColor("Gas")) };
        var row = K.H(6);
        foreach (var (name, color) in keys)
        {
            var t = K.Text(name, Ty.Caption);
            t.FontWeight = FontWeights.Medium;
            row.Children.Add(new Border
            {
                Child = K.H(6, new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center }, t),
                Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(K.WithAlpha(color, 0.18)),
            });
        }
        row.Margin = new Thickness(0, 0, 0, 6);
        return row;
    }

    /// <summary>The trend arrows round the table, for a property with a simple trend: across the top, the trend across a
    /// period; down the side, the trend in a group.</summary>
    private void Trend(PTColouring c)
    {
        if (c.Trend() is not { } t)
        {
            gutter.Width = new GridLength(0);
            across.Visibility = Visibility.Collapsed;
            up.Visibility = Visibility.Collapsed;
            return;
        }
        gutter.Width = new GridLength(30);
        across.Visibility = Visibility.Visible;
        up.Visibility = Visibility.Visible;
        Rectangle Line(bool horizontal)
        {
            var r = new Rectangle { Height = horizontal ? 2 : double.NaN, Width = horizontal ? double.NaN : 2, VerticalAlignment = horizontal ? VerticalAlignment.Center : VerticalAlignment.Stretch, HorizontalAlignment = horizontal ? HorizontalAlignment.Stretch : HorizontalAlignment.Center };
            r.SetResourceReference(Shape.FillProperty, B.Caution);
            return r;
        }
        FrameworkElement Arrow(SymbolRegular s) => K.Icon(s, 13, B.Caution, filled: true);
        var words = K.Text(t.AcrossWords, Ty.Caption, B.Caution);
        words.FontWeight = FontWeights.SemiBold;
        words.Margin = new Thickness(6, 0, 6, 0);
        var row = K.Columns("auto, *, auto, *, auto");
        if (t.Across < 0) row.Children.Add(Arrow(SymbolRegular.ArrowLeft24).Margin(0, 0, 6, 0).Col(0));
        row.Children.Add(Line(true).Col(1));
        row.Children.Add(words.Col(2));
        row.Children.Add(Line(true).Col(3));
        if (t.Across > 0) row.Children.Add(Arrow(SymbolRegular.ArrowRight24).Margin(6, 0, 0, 0).Col(4));
        across.Content = row;

        var side = K.Text(t.UpWords, Ty.Caption, B.Caution);
        side.FontWeight = FontWeights.SemiBold;
        // (its whole length given it before it is turned on its side: measured turned, in the narrow gutter, it was given
        //  the gutter's width and cut to "I…")
        side.TextTrimming = TextTrimming.None;
        side.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        side.Width = Math.Ceiling(side.DesiredSize.Width) + 2;
        side.LayoutTransform = new RotateTransform(-90);
        side.HorizontalAlignment = HorizontalAlignment.Center;
        side.Margin = new Thickness(0, 4, 0, 4);
        var col = K.Rows("auto, *, auto, *, auto");
        if (t.Up > 0) col.Children.Add(Arrow(SymbolRegular.ArrowUp24).Margin(0, 0, 0, 4).Row(0));
        col.Children.Add(Line(false).Row(1));
        col.Children.Add(side.Row(2));
        col.Children.Add(Line(false).Row(3));
        if (t.Up < 0) col.Children.Add(Arrow(SymbolRegular.ArrowDown24).Margin(0, 4, 0, 0).Row(4));
        up.Content = col;
    }

    // ---- an element's page, grown out of its square ----

    /// <summary>An element opened (from its square: <paramref name="from"/> in this view's coordinates; else from the middle).</summary>
    private void OpenElement(PTElement e, Point? from, bool animate = true)
    {
        hovered = null;
        grid.Show(null, Colouring);
        var size = new Size(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
        anchor = from is { } p && ActualWidth > 0 ? new Point(Math.Clamp(p.X / size.Width, 0, 1), Math.Clamp(p.Y / size.Height, 0, 1)) : new Point(0.5, 0.5);
        var was = open;
        open = e;
        if (page == null)
        {
            page = new PTElementPage(e, () => { CloseElement(true); CheckTour(); }, Go);
            pageHost.Children.Clear();
            pageHost.Children.Add(page);
        }
        else page.Show(e);
        if (was == null) Present(animate);
        CheckTour();
    }

    /// <summary>The element before or after, on its page.</summary>
    private void Go(int n)
    {
        if (PeriodicTable.ByNumber(n) is not { } next || page == null) return;
        open = next;
        page.Show(next);
        CheckTour();
    }

    private void CloseElement(bool animate)
    {
        if (open == null) return;
        open = null;
        Present(animate);
    }

    /// <summary>What shows: an element's page over everything, else the page chosen in the header.</summary>
    private void Present(bool animate)
    {
        var v = ++version;
        UIElement under = PTNav.Page == PTPage.Table ? tablePage : chemHost;
        if (PTNav.Page != PTPage.Table) chemHost.Content = ChemistryPage(PTNav.Page);
        var other = ReferenceEquals(under, tablePage) ? (UIElement)chemHost : tablePage;
        other.Visibility = Visibility.Collapsed;
        if (open != null)
        {
            pageHost.Visibility = Visibility.Visible;
            if (animate && Motion.On)
            {
                var s = new ScaleTransform(0.06, 0.06);
                pageHost.RenderTransformOrigin = anchor;
                pageHost.RenderTransform = s;
                Motion.FromTo(s, ScaleTransform.ScaleXProperty, 0.06, 1, 460, Motion.Gentle);
                Motion.FromTo(s, ScaleTransform.ScaleYProperty, 0.06, 1, 460, Motion.Gentle);
                Motion.FromTo(pageHost, OpacityProperty, 0, 1, Motion.Medium);
                Motion.To(under, OpacityProperty, 0, Motion.Normal, done: () => { if (v == version) under.Visibility = Visibility.Collapsed; });
            }
            else
            {
                pageHost.BeginAnimation(OpacityProperty, null);
                pageHost.Opacity = 1;
                pageHost.RenderTransform = Transform.Identity;
                under.Visibility = Visibility.Collapsed;
            }
            return;
        }
        under.Visibility = Visibility.Visible;
        if (pageHost.Visibility == Visibility.Visible && animate && Motion.On)
        {
            var s = pageHost.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
            if (pageHost.RenderTransform != s) pageHost.RenderTransform = s;
            pageHost.RenderTransformOrigin = anchor;
            Motion.To(s, ScaleTransform.ScaleXProperty, 0.06, 380, Motion.Accelerate);
            Motion.To(s, ScaleTransform.ScaleYProperty, 0.06, 380, Motion.Accelerate);
            Motion.To(pageHost, OpacityProperty, 0, Motion.Medium, Motion.Accelerate, done: () => { if (v == version) DropPage(); });
            Motion.FromTo(under, OpacityProperty, 0, 1, Motion.Medium, Motion.Decelerate);
        }
        else
        {
            DropPage();
            under.BeginAnimation(OpacityProperty, null);
            under.Opacity = 1;
            if (animate) Motion.FadeIn(under, Motion.Normal);
        }
    }

    private void DropPage()
    {
        pageHost.Visibility = Visibility.Collapsed;
        pageHost.Children.Clear();
        page = null;
    }

    /// <summary>The Lewis structure maker's or the molecule builder's page (Tools/Chemistry), made once while the tool is
    /// open; not there yet, a line that says so.</summary>
    private FrameworkElement ChemistryPage(PTPage p)
    {
        try
        {
            if (p == PTPage.Lewis && PTChemistry.LewisView is { } lv) return lewis ??= lv(ctx);
            if (p == PTPage.Molecules && PTChemistry.MoleculeView is { } mv) return molecules ??= mv(ctx);
        }
        catch (Exception e)
        {
            Log.Write($"ptable {p}: {e}");
            return K.Wrapped($"This page could not open: {e.Message}", Ty.Body, B.Text2).Margin(40, 8, 40, 24);
        }
        var name = p == PTPage.Lewis ? "The Lewis structure maker" : "The molecule builder";
        return K.Empty(p.Symbol(), $"{name} is on its way to Simpl for Windows.").Margin(40, 8, 40, 24);
    }

    // ---- the tour ----

    /// <summary>The tour at a step: the tool set to what the step needs (the page its control is on, an element's page, a
    /// preset), and nothing yet done for it.</summary>
    private void Tour(int i)
    {
        if (i == 0 || steps.Count == 0) steps = PTTourStep.Available();
        if (i < 0 || i >= steps.Count) return;
        var s = steps[i];
        Offer(false);
        PTTour.Active = true;
        PTTour.ClearEvent();
        tourDone = false;
        tourStep = i;
        nextTimer?.Stop();
        if (i == 0)
        {
            if (PTState.Colouring != PTColouring.Category) { PTState.Colouring = PTColouring.Category; Recolour(true); }
            PTSearch.Query = "";
            hovered = null;
            grid.Show(null, Colouring);
        }
        if (s.Element is { } n && PeriodicTable.ByNumber(n) is { } e)
        {
            if (open == null) OpenElement(e, null);
        }
        else if (s.Target is "ptGrid" or "ptColours")
        {
            CloseElement(true);
            PTNav.Page = PTPage.Table;
        }
        else if (s.Target.StartsWith("lewis", StringComparison.Ordinal))
        {
            CloseElement(true);
            PTNav.Page = PTPage.Lewis;
        }
        else if (s.Target == "molViewer")
        {
            CloseElement(true);
            PTNav.Page = PTPage.Molecules;
        }
        else if (s.Target.StartsWith("ptPage", StringComparison.Ordinal)) CloseElement(true);
        if (s.Molecule is { } m) Hand(() => PTChemistry.OpenMolecule?.Invoke(m));
        // (Draw asked for from the quick way: the quick way's water first)
        if (s.Target == "lewisWay") PTTour.ShowLewis("H₂O");
        if (s.Lewis is { } l) PTTour.ShowLewis(l);
        DrawTour();
        CheckTour();
    }

    /// <summary>The step's thing done: on to the next (a breath after its card is ticked), or the tour ended after the last.</summary>
    private void CheckTour()
    {
        if (tourStep is not { } i || i >= steps.Count) return;
        var s = steps[i];
        var done = s.Act switch
        {
            PTTourAct.Hover => hovered != null,
            PTTourAct.Colour => Colouring != PTColouring.Category,
            PTTourAct.Open => open != null,
            PTTourAct.Page => PTNav.Page == s.Page && open == null,
            PTTourAct.Event => PTTour.Event == s.Event,
            _ => PTSearch.Query.Trim().Length > 0,
        };
        if (!done || tourDone) return;
        tourDone = true;
        DrawTour();
        nextTimer?.Stop();
        nextTimer = PTKit.After(i == steps.Count - 1 ? 2.2 : 1.6, () => { if (tourStep == i) NextTour(i); });
    }

    private void NextTour(int i)
    {
        if (tourStep != i) return;
        if (i + 1 < steps.Count) Tour(i + 1); else EndTour();
    }

    /// <summary>The tour over — its last step done, skipped, closed or Escape pressed — and counted as seen: never offered
    /// again on its own (Tour, in the page bar, takes it again); the tool left as it is.</summary>
    private void EndTour()
    {
        Prefs.Set("simpl.ptTourSeen", true);
        PTTour.Active = false;
        tourStep = null;
        nextTimer?.Stop();
        overlay?.Hide();
    }

    /// <summary>Skip tour, its ×, or Escape (PTKeys): true if a tour was on.</summary>
    private bool SkipTour()
    {
        if (tourStep == null) return false;
        EndTour();
        return true;
    }

    private void DrawTour()
    {
        if (tourStep is not { } i || i >= steps.Count || !IsLoaded) return;
        overlay ??= new PTTourOverlay(this);
        overlay.Show(i, steps.Count, steps[i], tourDone, () => NextTour(i), () => SkipTour(), i > 0 ? () => { if (tourStep == i) Tour(i - 1); } : null);
    }
}

/// <summary>
/// The table's pin, opened (the Mac's PeriodicTableCompact): the whole table, small, with a search that lights the matches
/// and a line naming the element under the pointer (or the first match). The element under the pointer shows in the
/// table's empty top; a press opens the whole table on its page.
/// </summary>
internal static class PeriodicTableCompact
{
    public static FrameworkElement Make(ToolContext c)
    {
        var grid = new PTGrid(compact: true);
        PTElement? over = null;
        var box = new TextBox { PlaceholderText = "Find an element", Width = 160, ClearButtonEnabled = false, MinHeight = 32 };
        var line = K.Text("", Ty.Body, B.Text2);
        line.VerticalAlignment = VerticalAlignment.Center;
        List<PTElement> Found() => PeriodicTable.Find(box.Text);
        void Open(PTElement e) => c.OpenFull(new Dictionary<string, string> { ["element"] = e.Number.ToString(CultureInfo.InvariantCulture) }, null);
        // (the element under the pointer, else the first the search finds, in the table's inset card — its facts said
        //  once, there; the line beside the field only says when nothing matches)
        void Line()
        {
            var e = over ?? Found().FirstOrDefault();
            grid.Show(e, PTColouring.Category);
            line.Text = e == null && box.Text.Trim().Length > 0 ? "No element" : "";
        }
        box.TextChanged += (_, _) =>
        {
            grid.Paint(PTColouring.Category, PTSearch.Matches(box.Text), null, false);
            Line();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (Found().FirstOrDefault() is { } f) Open(f);
        };
        grid.Hover = e => { over = e; Line(); };
        grid.Pick = (e, _) => Open(e);
        // (the screenshot suite: --ptable-pin-element 25 shows that element's card)
        if (PeriodicTable.Named(Launch.Take("", "ptable-pin-element")) is { } shown) { over = shown; Line(); }
        var head = K.Columns("auto, *", box.Col(0), line.Margin(10, 0, 0, 0).Col(1));
        return K.V(10, head, grid);
    }
}

// ---- its pages ----

/// <summary>
/// The Periodic Table's pages in a bar of their own over the page shown (Windows 11's SelectorBar; the Mac's labelled
/// page control): each page's symbol and name, always in words — the one shown in the text colour over an accent pill
/// that slides to the page chosen (Motion.Out over 220 ms; at once with Windows' animations off) — and Tour at the bar's
/// right. On a narrow page the pages scroll sideways rather than drawing in to their symbols.
/// </summary>
internal sealed class PTPagesBar : Grid
{
    private readonly List<(PTPage Page, Button Button, TextBlock Words)> items = new();
    private readonly Border pill = new()
    {
        Height = 3, Width = 18, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
        IsHitTestVisible = false, Margin = new Thickness(0, 0, 0, 2),
    };
    private readonly TranslateTransform shift = new();
    private readonly Grid strip = new();
    private bool placed;

    public PTPagesBar(Action tour)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var p in Enum.GetValues<PTPage>())
        {
            var pp = p;
            var words = K.Text(p.Label(), Ty.Body);
            var b = K.Subtle(K.H(8, K.Icon(p.Symbol(), 16, B.Text2), words), () => PTNav.Choose(pp), new Thickness(12, 8, 12, 12), 4);
            b.ToolTip = p.Help();
            if (p == PTPage.Lewis) b.TourTarget("ptPageLewis");
            if (p == PTPage.Molecules) b.TourTarget("ptPageMolecules");
            items.Add((p, b, words));
            row.Children.Add(b);
        }
        pill.SetResourceReference(Border.BackgroundProperty, B.Accent);
        pill.RenderTransform = shift;
        strip.Children.Add(row);
        strip.Children.Add(pill);
        AutomationProperties.SetName(strip, "Periodic table pages");
        var tourButton = K.Button("Tour", tour, icon: SymbolRegular.QuestionCircle24);
        tourButton.MinWidth = 0;
        tourButton.ToolTip = "A short tour of the table and its builders";
        tourButton.VerticalAlignment = VerticalAlignment.Center;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(PTKit.ChipRow(strip).Col(0));
        Children.Add(tourButton.Margin(12, 0, 0, 0).Col(1));
        // (the first page's symbol in line with the page under it: the page's margin less the button's own room)
        Margin = new Thickness(28, 0, 40, 4);
        SizeChanged += (_, _) => Margin = ActualWidth + Margin.Left + Margin.Right < 560 ? new Thickness(8, 0, 20, 4) : new Thickness(28, 0, 40, 4);
        strip.SizeChanged += (_, _) => Draw(false);
        void Changed() => Draw(true);
        Loaded += (_, _) => { PTNav.Changed -= Changed; PTNav.Changed += Changed; Draw(false); };
        Unloaded += (_, _) => PTNav.Changed -= Changed;
        Draw(false);
    }

    /// <summary>The page shown marked: its name in the text colour, the pill under it — slid there, or put there at once.</summary>
    private void Draw(bool animate)
    {
        foreach (var (p, b, w) in items)
        {
            var on = p == PTNav.Page;
            w.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            w.Fg(on ? B.Text : B.Text2);
            AutomationProperties.SetName(b, on ? $"{p.Label()}, shown" : p.Label());
        }
        var chosen = items.First(i => i.Page == PTNav.Page).Button;
        if (chosen.ActualWidth <= 0 || !strip.IsAncestorOf(chosen)) return;
        double x;
        try { x = chosen.TranslatePoint(new Point(chosen.ActualWidth / 2 - pill.Width / 2, 0), strip).X; } catch { return; }
        if (animate && placed) Motion.To(shift, TranslateTransform.XProperty, x, Motion.Page, Motion.Out);
        else { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = x; }
        placed = true;
    }
}

// ---- the header's controls ----

/// <summary>
/// The Periodic Table's search in the tool's heading, left of its pin (the Mac's PTHeaderControls): a button that opens
/// into a field; what the search finds listed under the field, the one the arrow keys are on lit, a press (or Return)
/// opening it. (Its pages have a bar of their own in the tool, PTPagesBar: named, never drawn in to symbols.)
/// </summary>
internal sealed class PTHeaderControls : ContentControl
{
    private readonly ContentControl searchHost = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private bool? drawnSearching;
    private TextBox? field;
    private FrameworkElement? fieldBox;
    private Border? results;
    private int lit;
    private bool focusAsked;

    public PTHeaderControls(ToolContext c)
    {
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
        Content = searchHost;
        // (the screenshot suite: --ptable-search ne, the search open with its results)
        if (Launch.Take("", "ptable-search") is { Length: > 0 } q) { PTSearch.Searching = true; PTSearch.Query = q; }
        PTKeys.Hook();
        Draw();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        Detach();
        PTSearch.Changed += Draw;
        LayoutUpdated += Place;
        Draw();
    }

    private void Detach()
    {
        PTSearch.Changed -= Draw;
        LayoutUpdated -= Place;
        RemoveResults();
        if (ReferenceEquals(PTKeys.SearchField, field)) PTKeys.SearchField = null;
    }

    private void Draw()
    {
        if (drawnSearching != PTSearch.Searching) DrawSearch();
        // (cleared elsewhere — the tour's start, an element opened — the field shows it)
        if (field != null && field.Text != PTSearch.Query) field.Text = PTSearch.Query;
        Results();
    }

    private void DrawSearch()
    {
        drawnSearching = PTSearch.Searching;
        if (ReferenceEquals(PTKeys.SearchField, field)) PTKeys.SearchField = null;
        field = null;
        if (!PTSearch.Searching)
        {
            var b = PTKit.Choice("Search", false, () => { focusAsked = true; PTSearch.Searching = true; }, SymbolRegular.Search24, squished: true);
            b.ToolTip = "Search the elements";
            b.TourTarget("ptSearch");
            fieldBox = b;
            searchHost.Content = b;
            if (Motion.On) Motion.FadeIn(b, Motion.Normal);
            return;
        }
        var f = new TextBox
        {
            PlaceholderText = "Symbol, name or number", Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, Width = 230, MinHeight = 32,
            ClearButtonEnabled = false, Text = PTSearch.Query, VerticalAlignment = VerticalAlignment.Center,
        };
        f.TextChanged += (_, _) => { lit = 0; PTSearch.Query = f.Text; };
        f.PreviewKeyDown += (_, e) =>
        {
            var r = Found();
            switch (e.Key)
            {
                case Key.Down: lit = Math.Min(lit + 1, Math.Max(r.Count - 1, 0)); Results(); e.Handled = true; break;
                case Key.Up: lit = Math.Max(lit - 1, 0); Results(); e.Handled = true; break;
                case Key.Enter:
                    if (lit >= 0 && lit < r.Count) Choose(r[lit]); else PTSearch.Submit();
                    e.Handled = true;
                    break;
            }
        };
        f.GotKeyboardFocus += (_, _) => PTKeys.SearchField = f;
        f.LostKeyboardFocus += (_, _) =>
        {
            // (left with nothing typed: the field draws back into its button)
            Dispatcher.BeginInvoke(() => { if (!f.IsKeyboardFocusWithin && PTSearch.Query.Length == 0 && ReferenceEquals(field, f)) PTSearch.Searching = false; }, DispatcherPriority.Input);
        };
        var close = K.IconButton(SymbolRegular.DismissCircle24, Close, "Close the search", 16, 32);
        var row = K.H(4, f, close);
        row.ToolTip = "Light the elements that match; Return opens the first";
        row.TourTarget("ptSearch");
        field = f;
        fieldBox = row;
        PTKeys.SearchField = f;
        PTKeys.CloseSearch = Close;
        searchHost.Content = row;
        if (Motion.On)
        {
            var s = new ScaleTransform(0.6, 1);
            row.RenderTransformOrigin = new Point(1, 0.5);
            row.RenderTransform = s;
            Motion.FromTo(s, ScaleTransform.ScaleXProperty, 0.6, 1, Motion.Medium, Motion.Decelerate);
            Motion.FadeIn(row, Motion.Normal);
        }
        if (focusAsked || PTSearch.Query.Length == 0)
        {
            focusAsked = false;
            Dispatcher.BeginInvoke(() => { f.Focus(); Keyboard.Focus(f); f.CaretIndex = f.Text.Length; }, DispatcherPriority.Input);
        }
    }

    private static List<PTElement> Found() => PeriodicTable.Find(PTSearch.Query).Take(7).ToList();

    private void Choose(PTElement e)
    {
        Close();
        PTSearch.Pick(e.Number);
    }

    private void Close()
    {
        PTSearch.Query = "";
        PTSearch.Searching = false;
        Keyboard.ClearFocus();
    }

    // ---- what the search finds, under the field ----

    private void Results()
    {
        var q = PTSearch.Query.Trim();
        if (!PTSearch.Searching || q.Length == 0 || !IsLoaded) { RemoveResults(); return; }
        var r = Found();
        lit = Math.Clamp(lit, 0, Math.Max(0, r.Count - 1));
        var list = K.V(2);
        if (r.Count == 0) list.Children.Add(K.Text($"No element matches “{q}”", Ty.Body, B.Text2).Margin(12));
        for (var k = 0; k < r.Count; k++)
        {
            var e = r[k];
            var kk = k;
            var words = K.V(1, K.Text(e.Name, Ty.BodyStrong), K.Text($"{e.Number} · {e.Kind.Label}", Ty.Caption, B.Text2));
            words.VerticalAlignment = VerticalAlignment.Center;
            var row = K.Columns("auto, *, auto",
                PTKit.Tile(e, 32, 14, 6).Col(0),
                words.Margin(10, 0, 8, 0).Col(1),
                K.Icon(SymbolRegular.ChevronRight20, 11, B.Text3).Col(2));
            var b = K.Subtle(row, () => Choose(e), new Thickness(8, 6, 8, 6), 6);
            b.Focusable = false;
            b.Width = 284;
            if (k == lit) b.Background = new SolidColorBrush(K.WithAlpha(Appearance.Accent, 0.16));
            b.MouseEnter += (_, _) => { if (lit != kk) { lit = kk; Results(); } };
            AutomationProperties.SetName(b, $"{e.Name}, {e.Number}, {e.Kind.Label}");
            list.Children.Add(b);
        }
        var layer = PTLayer.Of(this);
        if (layer == null) return;
        var fresh = results == null;
        results ??= new Border { Padding = new Thickness(6), CornerRadius = new CornerRadius(8), Effect = K.Shadow(20, 8, 0.2) };
        results.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        results.SetResourceReference(TextElement.ForegroundProperty, B.Text);
        results.Child = list;
        if (results.Parent == null) layer.Children.Add(results);
        Place(null, EventArgs.Empty);
        if (fresh && Motion.On)
        {
            var s = new ScaleTransform(0.96, 0.96);
            results.RenderTransformOrigin = new Point(1, 0);
            results.RenderTransform = s;
            Motion.FromTo(s, ScaleTransform.ScaleXProperty, 0.96, 1, Motion.Normal);
            Motion.FromTo(s, ScaleTransform.ScaleYProperty, 0.96, 1, Motion.Normal);
            Motion.FadeIn(results, Motion.Fast);
        }
    }

    /// <summary>The results under the field, their right edge under its right edge.</summary>
    private void Place(object? sender, EventArgs e)
    {
        if (results?.Parent is not Canvas layer || fieldBox is not { IsLoaded: true } box) return;
        Point at;
        try { at = box.TranslatePoint(new Point(box.ActualWidth, box.ActualHeight), layer); } catch { return; }
        results.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var x = Math.Max(8, at.X - results.DesiredSize.Width);
        var y = at.Y + 10;
        if (Math.Abs(Canvas.GetLeft(results) - x) > 0.5 || double.IsNaN(Canvas.GetLeft(results))) Canvas.SetLeft(results, x);
        if (Math.Abs(Canvas.GetTop(results) - y) > 0.5 || double.IsNaN(Canvas.GetTop(results))) Canvas.SetTop(results, y);
    }

    private void RemoveResults()
    {
        if (results?.Parent is Panel p) p.Children.Remove(results);
        results = null;
    }
}

/// <summary>
/// Escape where the Periodic Table has a use for it before the window does (the window's own Escape closes the tool's
/// sheet): with the tour on it ends the tour; in the search field it shuts the search; on an element's page it goes back
/// to the table.
/// </summary>
internal static class PTKeys
{
    private static bool hooked;
    public static TextBox? SearchField;
    public static Action? CloseSearch;
    public static readonly HashSet<PTElementPage> Pages = new();

    public static void Hook()
    {
        if (hooked) return;
        hooked = true;
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnKey));
    }

    private static void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        // (the tour first: Escape ends it — counted as seen — and leaves the table where it is)
        if (PTTour.Active && PTTour.Skip is { } skip && skip())
        {
            e.Handled = true;
            return;
        }
        if (Keyboard.FocusedElement is not DependencyObject f) return;
        if (SearchField is { IsLoaded: true } box && (ReferenceEquals(f, box) || box.IsAncestorOf(f)))
        {
            e.Handled = true;
            CloseSearch?.Invoke();
            return;
        }
        foreach (var page in Pages.ToList())
            if (page.IsLoaded && (ReferenceEquals(f, page) || page.IsAncestorOf(f)))
            {
                e.Handled = true;
                page.Back();
                return;
            }
    }
}

// ---- the Lewis maker's and the molecule builder's layout ----

/// <summary>
/// The Lewis maker's and the molecule builder's layout (the Mac's PTWorkLayout, for Tools/Chemistry): on a wide window
/// (1100 and up) the controls, the drawing and the facts side by side, each side column scrolling on its own; on a
/// narrower one, the drawing across the top and the controls and facts side by side under it, all scrolling as one.
/// </summary>
public static class PTWorkLayout
{
    public static FrameworkElement Make(FrameworkElement left, FrameworkElement centre, FrameworkElement right)
    {
        var host = new Grid { Margin = new Thickness(40, 0, 40, 24), MaxWidth = 1480 + 80, HorizontalAlignment = HorizontalAlignment.Stretch };
        bool? wide = null;
        void Detach(FrameworkElement e)
        {
            switch (e.Parent)
            {
                case Panel p: p.Children.Remove(e); break;
                case ContentControl c: c.Content = null; break;
                case Decorator d: d.Child = null; break;
            }
        }
        void Lay()
        {
            var w = host.ActualWidth >= 1100;
            if (wide == w) return;
            wide = w;
            foreach (var e in new[] { left, centre, right }) Detach(e);
            host.Children.Clear();
            host.ColumnDefinitions.Clear();
            host.RowDefinitions.Clear();
            ScrollViewer Scroll(UIElement content) => new()
            {
                Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
            };
            if (w)
            {
                // (each side column's content held to the column's width, less room for its scroller, so nothing runs
                // under the centre or under the scroll bar)
                left.Width = 284; left.HorizontalAlignment = HorizontalAlignment.Left; left.Margin = new Thickness(0, 0, 0, 20);
                right.Width = 300; right.HorizontalAlignment = HorizontalAlignment.Left; right.Margin = new Thickness(0, 0, 0, 20);
                centre.Height = double.NaN;
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(316) });
                host.Children.Add(Scroll(left).Col(0));
                host.Children.Add(centre.Col(2));
                host.Children.Add(Scroll(right).Col(4));
            }
            else
            {
                left.Width = double.NaN; left.HorizontalAlignment = HorizontalAlignment.Stretch; left.Margin = new Thickness(0);
                right.Width = double.NaN; right.HorizontalAlignment = HorizontalAlignment.Stretch; right.Margin = new Thickness(0);
                centre.Height = Math.Max(460, Math.Min(640, host.ActualHeight * 0.75));
                var sides = K.Columns("*, 18, *", left.Col(0), right.Col(2));
                sides.VerticalAlignment = VerticalAlignment.Top;
                left.VerticalAlignment = VerticalAlignment.Top;
                right.VerticalAlignment = VerticalAlignment.Top;
                var stack = K.V(18, centre, sides);
                stack.Margin = new Thickness(0, 0, 0, 24);
                host.Children.Add(Scroll(stack));
            }
        }
        host.SizeChanged += (_, e) =>
        {
            Lay();
            if (wide == false && e.HeightChanged) centre.Height = Math.Max(460, Math.Min(640, host.ActualHeight * 0.75));
        };
        host.Loaded += (_, _) => Lay();
        return host;
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// The Statistics tool's four pages (Statistics.cs holds the tool, its state and its pin) and the chart they draw on:
// a histogram with the box plot under it, a scatter with its line, the normal curve with an area shaded.

/// <summary>Describe: a sample's every figure, its histogram (Sturges' bins) and box plot, its outliers past 1.5 IQR.</summary>
internal sealed class DescribePage : StatsPage
{
    private readonly StatsChart chart = new();
    private readonly ContentControl results = new() { Focusable = false }, outliers = new() { Focusable = false }, skipped = new() { Focusable = false };
    private readonly Wpf.Ui.Controls.Button copy;
    private List<(string Name, string Value)> rows = new();

    public override FrameworkElement View { get; }

    public DescribePage()
    {
        var box = Numbers("data", "2, 4, 4, 4, 5, 5, 7, 9", "Data", 110);
        var hint = K.Wrapped("Numbers separated by spaces, commas or line breaks — a column pasted from a spreadsheet works.", Ty.Caption, B.Text2);
        var data = ToolKit.Section("Data", K.V(8, box, hint, skipped), Example(() => s.Set("data", "2, 4, 4, 4, 5, 5, 7, 9")));
        var main = K.V(20, data, ToolKit.Section("Histogram and box plot", chart));
        copy = K.Button("Copy", () => Everyday.Copy(Report(rows)), icon: SymbolRegular.Copy24);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        var note = K.Wrapped("Quartiles are the medians of the lower and upper halves, the median itself left out when n is odd. Outliers lie past 1.5 IQR from the quartiles.", Ty.Caption, B.Text3);
        var side = K.Card(K.V(12, results, outliers, note, copy), new Thickness(16));
        View = ToolKit.Columns(main, side, 380, 860, sideFirst: true);
    }

    public override void Update()
    {
        Sync();
        var numbers = StatsMath.Numbers(s.Get("data"), out var skippedCount);
        skipped.Content = skippedCount > 0 ? ToolKit.Note($"{ToolKit.Plural(skippedCount, "entry", "entries")} skipped: not a number.") : null;
        var d = StatsMath.Describe(numbers);
        chart.ShowHistogram(d);
        if (d == null)
        {
            rows = new();
            results.Content = Blank("Type or paste numbers to describe them.");
            outliers.Content = null;
            copy.IsEnabled = false;
            return;
        }
        rows = new()
        {
            ("n", d.N.ToString(CultureInfo.InvariantCulture)),
            ("Mean", StatsMath.Sig(d.Mean)),
            ("Median", StatsMath.Sig(d.Median)),
            ("Mode", d.Modes.Length == 0 ? "No mode" : string.Join(", ", d.Modes.Select(m => StatsMath.Sig(m)))),
            ("Min", StatsMath.Sig(d.Min)),
            ("Max", StatsMath.Sig(d.Max)),
            ("Range", StatsMath.Sig(d.Range)),
            ("Q1", StatsMath.Sig(d.Q1)),
            ("Q3", StatsMath.Sig(d.Q3)),
            ("IQR", StatsMath.Sig(d.Iqr)),
            ("Sample SD (n − 1)", d.N > 1 ? StatsMath.Sig(d.SampleSd) : "—"),
            ("Population SD", StatsMath.Sig(d.PopSd)),
            ("Sample variance", d.N > 1 ? StatsMath.Sig(d.SampleVar) : "—"),
            ("Population variance", StatsMath.Sig(d.PopVar)),
            ("SE of the mean", d.N > 1 ? StatsMath.Sig(d.Se) : "—"),
            ("Sum", StatsMath.Sig(d.Sum)),
            ("Sum of squares", StatsMath.Sig(d.SumSquares)),
        };
        results.Content = Rows(rows);
        var outs = d.Outliers;
        if (outs.Length == 0) outliers.Content = K.Wrapped("No outliers.", Ty.Body, B.Text2);
        else
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            wrap.Children.Add(K.Text("Outliers", Ty.Body, B.Text2).Margin(0, 0, 8, 6));
            foreach (var o in outs)
            {
                var chip = K.Chip(StatsMath.Sig(o), K.Tone("red"), strong: true);
                chip.Margin = new Thickness(0, 0, 6, 6);
                wrap.Children.Add(chip);
            }
            outliers.Content = wrap;
        }
        copy.IsEnabled = true;
    }
}

/// <summary>Regression: the least-squares line through x and y, with r and r², a scatter, and y for an x.</summary>
internal sealed class RegressionPage : StatsPage
{
    private readonly StatsChart chart = new();
    private readonly TextBlock equation = Everyday.Figures("—", 26, FontWeights.Bold);
    private readonly ContentControl results = new() { Focusable = false }, predicted = new() { Focusable = false }, skipped = new() { Focusable = false };
    private readonly Wpf.Ui.Controls.Button copy;
    private List<(string Name, string Value)> rows = new();

    public override FrameworkElement View { get; }

    public RegressionPage()
    {
        var x = Numbers("x", "1, 2, 3, 4, 5", "x values", 110);
        var y = Numbers("y", "2, 4, 5, 4, 5", "y values", 110);
        var pair = K.Columns("*, 12, *", K.V(6, K.Text("x", Ty.BodyStrong, B.Text2), x).Col(0), K.V(6, K.Text("y", Ty.BodyStrong, B.Text2), y).Col(2));
        var hint = K.Wrapped("Two columns of the same length, one number a line or separated by commas.", Ty.Caption, B.Text2);
        var points = ToolKit.Section("Points", K.V(8, pair, hint, skipped), Example(() => { s.Set("x", "1, 2, 3, 4, 5"); s.Set("y", "2, 4, 5, 4, 5"); }));
        var main = K.V(20, points, ToolKit.Section("Scatter and line", chart));
        var predict = Number("predict", "e.g. 6", "Predict y from x");
        var predictField = ToolKit.Field("Predict y from x", predict, width: 160);
        copy = K.Button("Copy", () => Everyday.Copy(Report(rows)), icon: SymbolRegular.Copy24);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        equation.ContextMenu = Screens.MoreKit.Menu(("Copy", SymbolRegular.Copy24, () => Everyday.Copy(equation.Text.Replace("−", "-"))));
        var side = K.Card(K.V(12, Everyday.Shrinks(equation), results, predictField, predicted, copy), new Thickness(16));
        View = ToolKit.Columns(main, side, 380, 860, sideFirst: true);
    }

    public override void Update()
    {
        Sync();
        var xs = StatsMath.Numbers(s.Get("x"), out var sx);
        var ys = StatsMath.Numbers(s.Get("y"), out var sy);
        skipped.Content = sx + sy > 0 ? ToolKit.Note($"{ToolKit.Plural(sx + sy, "entry", "entries")} skipped: not a number.")
            : xs.Length != ys.Length && xs.Length + ys.Length > 0 ? ToolKit.Note($"x has {xs.Length} values and y has {ys.Length}: only the first {Math.Min(xs.Length, ys.Length)} pairs count.") : null;
        var r = StatsMath.Linear(xs, ys);
        chart.ShowScatter(xs, ys, r);
        if (r == null)
        {
            rows = new();
            equation.Text = "—";
            results.Content = Blank(xs.Length < 2 || ys.Length < 2 ? "Two or more pairs of x and y draw the line." : "The x values are all the same: no line fits.");
            predicted.Content = null;
            copy.IsEnabled = false;
            return;
        }
        equation.Text = $"y = {StatsMath.Sig(r.Slope)}x {(r.Intercept < 0 ? "−" : "+")} {StatsMath.Sig(Math.Abs(r.Intercept))}";
        AutomationProperties.SetName(equation, $"The line: {equation.Text}");
        rows = new()
        {
            ("Equation", equation.Text),
            ("Slope", StatsMath.Sig(r.Slope)),
            ("Intercept", StatsMath.Sig(r.Intercept)),
            ("r", StatsMath.Sig(r.R)),
            ("r²", StatsMath.Sig(r.R2)),
            ("n", r.N.ToString(CultureInfo.InvariantCulture)),
            ("Mean x", StatsMath.Sig(r.MeanX)),
            ("Mean y", StatsMath.Sig(r.MeanY)),
        };
        results.Content = Rows(rows.Skip(1));
        predicted.Content = s.Number("predict") is { } px ? Row($"y at x = {StatsMath.Sig(px)}", StatsMath.Sig(r.Predict(px)), strong: true) : null;
        copy.IsEnabled = true;
    }
}

/// <summary>Distributions: the normal (μ, σ: probabilities below, above and between, the z-score, and x for a p) and t
/// (df: a t's two- and one-tailed p, and the t for a p).</summary>
internal sealed class DistributionsPage : StatsPage
{
    private readonly StatsChart chart = new();
    private readonly ContentControl normalResults = new() { Focusable = false }, tResults = new() { Focusable = false };
    private readonly Wpf.Ui.Controls.Button copy;
    private List<(string Name, string Value)> rows = new();

    public override FrameworkElement View { get; }

    public DistributionsPage()
    {
        var mu = Number("mu", "100", "Mean μ");
        var sigma = Number("sigma", "15", "Standard deviation σ");
        var x = Number("nx", "115", "x");
        var a = Number("a", "85", "a");
        var b = Number("b", "b", "b");
        var p = Number("p", "0.9", "p");
        var row1 = K.Columns("*, 10, *, 10, *", ToolKit.Field("μ", mu).Col(0), ToolKit.Field("σ", sigma).Col(2), ToolKit.Field("x", x).Col(4));
        var row2 = K.Columns("*, 10, *, 10, *", ToolKit.Field("a", a).Col(0), ToolKit.Field("b", b).Col(2), ToolKit.Field("p (for the inverse)", p).Col(4));
        var normal = ToolKit.Section("Normal", K.V(12, row1, row2, K.Wrapped("P(X < x), P(X > x), P(a < X < b), the z-score of x, and the x below which a share p of the distribution lies.", Ty.Caption, B.Text2)),
            Example(() => { s.Set("mu", "100"); s.Set("sigma", "15"); s.Set("nx", "115"); s.Set("a", "85"); s.Set("b", "115"); s.Set("p", "0.9"); }));
        var df = Number("tdf", "10", "Degrees of freedom");
        var t = Number("tt", "2.228", "t");
        var tp = Number("tp", "0.05", "p");
        var row3 = K.Columns("*, 10, *, 10, *", ToolKit.Field("df", df).Col(0), ToolKit.Field("t", t).Col(2), ToolKit.Field("p (for the inverse)", tp).Col(4));
        var tSection = ToolKit.Section("t distribution", K.V(12, row3, K.Wrapped("The two-tailed and one-tailed p of a t, and the t whose two-tailed p is p (the critical value).", Ty.Caption, B.Text2)),
            Example(() => { s.Set("tdf", "10"); s.Set("tt", "2.228"); s.Set("tp", "0.05"); }));
        var main = K.V(20, normal, ToolKit.Section("The curve", chart), tSection);
        copy = K.Button("Copy", () => Everyday.Copy(Report(rows)), icon: SymbolRegular.Copy24);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        var side = K.Card(K.V(12, K.Text("Normal", Ty.BodyStrong), normalResults, K.Text("t", Ty.BodyStrong).Margin(0, 8, 0, 0), tResults, copy), new Thickness(16));
        View = ToolKit.Columns(main, side, 380, 860, sideFirst: true);
    }

    public override void Update()
    {
        Sync();
        rows = new();
        double? mu = s.Number("mu"), sigma = s.Number("sigma"), x = s.Number("nx"), a = s.Number("a"), b = s.Number("b"), p = s.Number("p");
        if (mu is { } m && sigma is { } sd && sd > 0)
        {
            var normal = new List<(string, string)>();
            if (x is { } xv)
            {
                var z = (xv - m) / sd;
                var below = StatsMath.NormalCdf(z);
                normal.Add(($"z of {StatsMath.Sig(xv)}", StatsMath.Sig(z)));
                normal.Add(($"P(X < {StatsMath.Sig(xv)})", StatsMath.Sig(below)));
                normal.Add(($"P(X > {StatsMath.Sig(xv)})", StatsMath.Sig(1 - below)));
            }
            if (a is { } av && b is { } bv)
            {
                var between = Math.Abs(StatsMath.NormalCdf(bv, m, sd) - StatsMath.NormalCdf(av, m, sd));
                normal.Add(($"P({StatsMath.Sig(Math.Min(av, bv))} < X < {StatsMath.Sig(Math.Max(av, bv))})", StatsMath.Sig(between)));
            }
            if (p is { } pv && pv > 0 && pv < 1) normal.Add(($"x with P(X < x) = {StatsMath.Sig(pv)}", StatsMath.Sig(m + sd * StatsMath.NormalInverse(pv))));
            if (normal.Count == 0) normalResults.Content = Blank("Add an x, a and b, or a p.");
            else normalResults.Content = Rows(normal);
            rows.AddRange(normal);
            chart.ShowNormal(m, sd, x, a, b);
        }
        else
        {
            normalResults.Content = Blank(sigma is { } bad && bad <= 0 ? "σ has to be more than 0." : "Give μ and σ.");
            chart.ShowNormal(double.NaN, double.NaN, null, null, null);
        }
        double? df = s.Number("tdf"), t = s.Number("tt"), tp = s.Number("tp");
        if (df is { } dfv && dfv > 0)
        {
            var tRows = new List<(string, string)>();
            if (t is { } tv)
            {
                var two = StatsMath.TTwoTailed(tv, dfv);
                tRows.Add(($"Two-tailed p of t = {StatsMath.Sig(tv)}", StatsMath.PText(two)));
                tRows.Add(("One-tailed p", StatsMath.PText(two / 2)));
            }
            if (tp is { } tpv && tpv > 0 && tpv < 1) tRows.Add(($"t with two-tailed p = {StatsMath.Sig(tpv)}", StatsMath.Sig(StatsMath.TCritical(tpv, dfv))));
            tResults.Content = tRows.Count == 0 ? Blank("Add a t, or a p.") : Rows(tRows);
            rows.AddRange(tRows);
        }
        else tResults.Content = Blank("Give the degrees of freedom.");
        copy.IsEnabled = rows.Count > 0;
    }
}

/// <summary>Tests: the one-sample t-test (from numbers or summary figures), Welch's two-sample and the paired.</summary>
internal sealed class TestsPage : StatsPage
{
    private readonly ComboBox kind = new() { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220 };
    private readonly FrameworkElement bField, mu0Field, summary;
    private readonly ContentControl results = new() { Focusable = false }, skipped = new() { Focusable = false };
    private readonly TextBlock verdict = K.Wrapped("", Ty.Body, B.Text2);
    private readonly Wpf.Ui.Controls.Button copy;
    private List<(string Name, string Value)> rows = new();
    private bool choosing;

    public override FrameworkElement View { get; }

    public TestsPage()
    {
        kind.Items.Add(Everyday.Item("One-sample", "one"));
        kind.Items.Add(Everyday.Item("Two-sample (Welch)", "two"));
        kind.Items.Add(Everyday.Item("Paired", "paired"));
        kind.SelectionChanged += (_, _) => { if (!choosing && Everyday.Chosen(kind) is { } k) s.Set("test", k); };
        AutomationProperties.SetName(kind, "Test");
        var a = Numbers("sampleA", "20, 22, 19, 24, 25", "Sample A", 90);
        var b = Numbers("sampleB", "18, 17, 21, 19, 16", "Sample B", 90);
        var aField = K.V(6, K.Text("Sample A", Ty.BodyStrong, B.Text2), a);
        bField = K.V(6, K.Text("Sample B", Ty.BodyStrong, B.Text2), b);
        var mu0 = Number("mu0", "20", "μ₀, the mean tested against");
        mu0Field = ToolKit.Field("μ₀ (the mean tested against)", mu0, width: 200);
        var mean = Number("smean", "mean", "Sample mean");
        var sd = Number("ssd", "SD", "Sample SD");
        var n = Number("sn", "n", "Sample size");
        summary = K.V(8, K.Text("Or from summary figures, when Sample A is empty", Ty.Caption, B.Text2),
            K.Columns("*, 10, *, 10, *", ToolKit.Field("Mean", mean).Col(0), ToolKit.Field("Sample SD", sd).Col(2), ToolKit.Field("n", n).Col(4)));
        var inputs = ToolKit.Section("Test", K.V(14, kind, aField, bField, mu0Field, summary, skipped),
            Example(() => { s.Set("sampleA", "20, 22, 19, 24, 25"); s.Set("sampleB", "18, 17, 21, 19, 16"); s.Set("mu0", "20"); }));
        var main = K.V(20, inputs, K.Wrapped("Welch's test does not assume the two samples share a variance; the paired test takes the differences pair by pair. Every p is two-tailed; the interval is at 95%.", Ty.Caption, B.Text3));
        copy = K.Button("Copy", () => Everyday.Copy(Report(rows)), icon: SymbolRegular.Copy24);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        var side = K.Card(K.V(12, results, verdict, copy), new Thickness(16));
        View = ToolKit.Columns(main, side, 380, 860, sideFirst: true);
    }

    public override void Update()
    {
        Sync();
        var k = s.Get("test", "one");
        if (k is not ("one" or "two" or "paired")) k = "one";
        choosing = true;
        Everyday.Choose(kind, k);
        choosing = false;
        bField.Visibility = k == "one" ? Visibility.Collapsed : Visibility.Visible;
        mu0Field.Visibility = k == "one" ? Visibility.Visible : Visibility.Collapsed;
        summary.Visibility = k == "one" ? Visibility.Visible : Visibility.Collapsed;
        var a = StatsMath.Numbers(s.Get("sampleA"), out var sa);
        var b = StatsMath.Numbers(s.Get("sampleB"), out var sb);
        skipped.Content = sa + (k == "one" ? 0 : sb) > 0 ? ToolKit.Note("Some entries were skipped: not numbers.") : null;
        TResult? r = null;
        string estimate = "Mean", blank = "";
        switch (k)
        {
            case "one":
                var mu0 = s.Number("mu0");
                if (mu0 == null) blank = "Give μ₀, the mean to test against.";
                else if (a.Length >= 2) r = StatsMath.OneSample(a, mu0.Value);
                else if (s.Number("smean") is { } m && s.Number("ssd") is { } sd && s.Number("sn") is { } n && n >= 2) r = StatsMath.OneSample(m, sd, (int)n, mu0.Value);
                else blank = "Two or more numbers in Sample A, or its mean, SD and n.";
                if (r == null && blank.Length == 0) blank = "The sample has no spread: no test can be made.";
                break;
            case "two":
                estimate = "Difference of means (A − B)";
                if (a.Length < 2 || b.Length < 2) blank = "Two or more numbers in each sample.";
                else r = StatsMath.Welch(a, b);
                if (r == null && blank.Length == 0) blank = "Neither sample has any spread: no test can be made.";
                break;
            default:
                estimate = "Mean difference (A − B)";
                if (a.Length < 2 || b.Length < 2) blank = "Two or more pairs.";
                else if (a.Length != b.Length) blank = $"Paired samples have to be the same length: A has {a.Length} and B has {b.Length}.";
                else r = StatsMath.Paired(a, b);
                if (r == null && blank.Length == 0) blank = "The differences have no spread: no test can be made.";
                break;
        }
        if (r == null)
        {
            rows = new();
            results.Content = Blank(blank);
            verdict.Text = "";
            copy.IsEnabled = false;
            return;
        }
        rows = new()
        {
            ("t", StatsMath.Sig(r.T)),
            ("df", StatsMath.Sig(r.Df)),
            ("p (two-tailed)", StatsMath.PText(r.P)),
            ("p (one-tailed)", StatsMath.PText(r.OneTailed)),
            (estimate, StatsMath.Sig(r.Estimate)),
            ("95% CI", $"{StatsMath.Sig(r.CiLow)} to {StatsMath.Sig(r.CiHigh)}"),
            ("Standard error", StatsMath.Sig(r.Se)),
        };
        results.Content = Rows(rows);
        verdict.Text = r.P < 0.05
            ? "p < 0.05: a difference this large would be unlikely by chance alone."
            : "p ≥ 0.05: not enough to say the means differ.";
        copy.IsEnabled = true;
    }
}

/// <summary>
/// The pages' chart, drawn as it is asked: a histogram with the box plot under it, a scatter with its line, or the
/// normal curve with the area below x shaded (a and b marked). Its colours follow light and dark.
/// </summary>
internal sealed class StatsChart : FrameworkElement
{
    private enum Mode { None, Histogram, Scatter, Normal }

    private Mode mode;
    private Description? d;
    private Histogram? bins;
    private double[] xs = Array.Empty<double>(), ys = Array.Empty<double>();
    private Regression? line;
    private double mu, sigma;
    private double? x, a, b;

    public StatsChart()
    {
        Height = 240;
        IsHitTestVisible = false;
        Loaded += (_, _) => { Appearance.Changed -= Redraw; Appearance.Changed += Redraw; InvalidateVisual(); };
        Unloaded += (_, _) => Appearance.Changed -= Redraw;
    }

    private void Redraw() => InvalidateVisual();

    public void ShowHistogram(Description? description)
    {
        mode = Mode.Histogram;
        d = description;
        bins = description == null ? null : StatsMath.Bins(description);
        InvalidateVisual();
    }

    public void ShowScatter(double[] x, double[] y, Regression? r)
    {
        mode = Mode.Scatter;
        var n = Math.Min(x.Length, y.Length);
        xs = x.Take(n).ToArray();
        ys = y.Take(n).ToArray();
        line = r;
        InvalidateVisual();
    }

    public void ShowNormal(double mean, double sd, double? at, double? from, double? to)
    {
        mode = Mode.Normal;
        mu = mean; sigma = sd; x = at; a = from; b = to;
        InvalidateVisual();
    }

    // ---- the drawing ----

    private const double Left = 44, Right = 14, Top = 12, Bottom = 30;

    private static Color Ink => Appearance.Dark ? Color.FromRgb(0xd6, 0xd6, 0xd6) : Color.FromRgb(0x3a, 0x3a, 0x3a);
    private static Color Faint => K.WithAlpha(Ink, 0.16);
    private static Color Tint => StatsPage.Tint;

    private FormattedText Words(string text, double size = 11, Color? colour = null) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(K.TextFace, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size,
            K.Brush(colour ?? K.WithAlpha(Ink, 0.75)), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= Left + Right + 10 || h <= Top + Bottom + 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        switch (mode)
        {
            case Mode.Histogram when d != null && bins != null: DrawHistogram(dc, w, h); break;
            case Mode.Scatter when xs.Length > 0: DrawScatter(dc, w, h); break;
            case Mode.Normal when double.IsFinite(mu) && sigma > 0: DrawNormal(dc, w, h); break;
            default:
                var t = Words(mode == Mode.Histogram ? "The histogram and box plot draw once there are numbers." : mode == Mode.Scatter ? "The scatter draws once there are points." : "The curve draws once μ and σ are given.", 12);
                dc.DrawText(t, new Point((w - t.Width) / 2, (h - t.Height) / 2));
                break;
        }
    }

    private void Label(DrawingContext dc, string text, double cx, double y, bool centre = true)
    {
        var t = Words(text);
        dc.DrawText(t, new Point(centre ? cx - t.Width / 2 : cx, y));
    }

    private void LabelRight(DrawingContext dc, string text, double rightX, double cy)
    {
        var t = Words(text);
        dc.DrawText(t, new Point(rightX - t.Width, cy - t.Height / 2));
    }

    /// <summary>A round step for about so many ticks across a span.</summary>
    private static double Step(double span, int ticks)
    {
        if (span <= 0) return 1;
        var raw = span / ticks;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var m = raw / mag;
        return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * mag;
    }

    private void DrawHistogram(DrawingContext dc, double w, double h)
    {
        var desc = d!;
        var hist = bins!;
        const double strip = 34; // the box plot's room under the bars
        double plotW = w - Left - Right, plotH = h - Top - Bottom - strip;
        double lo = hist.Start, hi = hist.Start + hist.Width * hist.Counts.Length;
        if (hi <= lo) { lo -= 1; hi += 1; }
        double X(double v) => Left + plotW * (v - lo) / (hi - lo);
        var max = Math.Max(1, hist.Counts.Max());
        var step = Step(max, 4);
        var ink = new Pen(K.Brush(Faint), 1);
        for (var c = 0.0; c <= max + 1e-9; c += step)
        {
            var y = Top + plotH * (1 - c / max);
            dc.DrawLine(ink, new Point(Left, y), new Point(w - Right, y));
            LabelRight(dc, c.ToString("0.##", CultureInfo.InvariantCulture), Left - 6, y);
        }
        var fill = K.Brush(K.WithAlpha(Tint, 0.65));
        var edge = new Pen(K.Brush(Tint), 1);
        for (var i = 0; i < hist.Counts.Length; i++)
        {
            double x0 = X(lo + i * hist.Width), x1 = X(lo + (i + 1) * hist.Width);
            var barH = plotH * hist.Counts[i] / max;
            if (hist.Counts[i] > 0) dc.DrawRectangle(fill, edge, new Rect(x0 + 1, Top + plotH - barH, Math.Max(1, x1 - x0 - 2), barH));
        }
        dc.DrawLine(new Pen(K.Brush(K.WithAlpha(Ink, 0.5)), 1), new Point(Left, Top + plotH), new Point(w - Right, Top + plotH));
        // the bin edges along the axis, every other one when they crowd
        var every = hist.Counts.Length > 8 ? 2 : 1;
        for (var i = 0; i <= hist.Counts.Length; i += every) Label(dc, StatsMath.Sig(lo + i * hist.Width, 4), X(lo + i * hist.Width), Top + plotH + 4);
        // the box plot: whiskers to the last values inside the fences, the box Q1 to Q3, the median, outliers as dots
        var y0 = Top + plotH + 24;
        var boxH = 14.0;
        double fenceLo = desc.Q1 - 1.5 * desc.Iqr, fenceHi = desc.Q3 + 1.5 * desc.Iqr;
        var inside = desc.Sorted.Where(v => v >= fenceLo && v <= fenceHi).ToArray();
        double wLo = inside.Length > 0 ? inside[0] : desc.Min, wHi = inside.Length > 0 ? inside[^1] : desc.Max;
        var pen = new Pen(K.Brush(Tint), 1.5);
        var mid = y0 + boxH / 2;
        dc.DrawLine(pen, new Point(X(wLo), mid), new Point(X(desc.Q1), mid));
        dc.DrawLine(pen, new Point(X(desc.Q3), mid), new Point(X(wHi), mid));
        dc.DrawLine(pen, new Point(X(wLo), mid - 5), new Point(X(wLo), mid + 5));
        dc.DrawLine(pen, new Point(X(wHi), mid - 5), new Point(X(wHi), mid + 5));
        dc.DrawRectangle(K.Brush(K.WithAlpha(Tint, 0.25)), pen, new Rect(X(desc.Q1), y0, Math.Max(1, X(desc.Q3) - X(desc.Q1)), boxH));
        dc.DrawLine(new Pen(K.Brush(Tint), 2.5), new Point(X(desc.Median), y0), new Point(X(desc.Median), y0 + boxH));
        foreach (var o in desc.Outliers) dc.DrawEllipse(K.Brush(K.Tone("red")), null, new Point(X(o), mid), 3.5, 3.5);
    }

    private void DrawScatter(DrawingContext dc, double w, double h)
    {
        double plotW = w - Left - Right, plotH = h - Top - Bottom;
        double xlo = xs.Min(), xhi = xs.Max(), ylo = ys.Min(), yhi = ys.Max();
        if (line != null) { ylo = Math.Min(ylo, Math.Min(line.Predict(xlo), line.Predict(xhi))); yhi = Math.Max(yhi, Math.Max(line.Predict(xlo), line.Predict(xhi))); }
        if (xhi - xlo <= 0) { xlo -= 1; xhi += 1; }
        if (yhi - ylo <= 0) { ylo -= 1; yhi += 1; }
        double padX = (xhi - xlo) * 0.08, padY = (yhi - ylo) * 0.1;
        xlo -= padX; xhi += padX; ylo -= padY; yhi += padY;
        double X(double v) => Left + plotW * (v - xlo) / (xhi - xlo);
        double Y(double v) => Top + plotH * (1 - (v - ylo) / (yhi - ylo));
        var faint = new Pen(K.Brush(Faint), 1);
        var sx = Step(xhi - xlo, 6);
        for (var v = Math.Ceiling(xlo / sx) * sx; v <= xhi + 1e-9; v += sx)
        {
            dc.DrawLine(faint, new Point(X(v), Top), new Point(X(v), Top + plotH));
            Label(dc, StatsMath.Sig(v, 4), X(v), Top + plotH + 4);
        }
        var sy = Step(yhi - ylo, 4);
        for (var v = Math.Ceiling(ylo / sy) * sy; v <= yhi + 1e-9; v += sy)
        {
            dc.DrawLine(faint, new Point(Left, Y(v)), new Point(w - Right, Y(v)));
            LabelRight(dc, StatsMath.Sig(v, 4), Left - 6, Y(v));
        }
        dc.DrawLine(new Pen(K.Brush(K.WithAlpha(Ink, 0.5)), 1), new Point(Left, Top + plotH), new Point(w - Right, Top + plotH));
        if (line != null)
        {
            double x0 = xlo + padX, x1 = xhi - padX;
            dc.DrawLine(new Pen(K.Brush(Tint), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(X(x0), Y(line.Predict(x0))), new Point(X(x1), Y(line.Predict(x1))));
        }
        var dot = K.Brush(K.WithAlpha(Tint, 0.9));
        var ring = new Pen(K.Brush(Appearance.Dark ? Colors.Black : Colors.White), 1);
        for (var i = 0; i < xs.Length; i++) dc.DrawEllipse(dot, ring, new Point(X(xs[i]), Y(ys[i])), 4.5, 4.5);
    }

    private void DrawNormal(DrawingContext dc, double w, double h)
    {
        double plotW = w - Left - Right, plotH = h - Top - Bottom;
        double lo = mu - 4 * sigma, hi = mu + 4 * sigma;
        double X(double v) => Left + plotW * (v - lo) / (hi - lo);
        var peak = StatsMath.NormalPdf(0) / sigma;
        double Y(double v) => Top + plotH * (1 - StatsMath.NormalPdf((v - mu) / sigma) / sigma / peak * 0.92);
        var faint = new Pen(K.Brush(Faint), 1);
        for (var k = -3; k <= 3; k++)
        {
            var v = mu + k * sigma;
            dc.DrawLine(faint, new Point(X(v), Top), new Point(X(v), Top + plotH));
            Label(dc, StatsMath.Sig(v, 4), X(v), Top + plotH + 4);
        }
        // the curve, 160 points across, and the area below x shaded under it
        const int steps = 160;
        var curve = new List<Point>(steps + 1);
        for (var i = 0; i <= steps; i++) { var v = lo + (hi - lo) * i / steps; curve.Add(new Point(X(v), Y(v))); }
        if (x is { } at)
        {
            var shadeTo = Math.Clamp(at, lo, hi);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(Left, Top + plotH), true, true);
                foreach (var p in curve) { if (p.X <= X(shadeTo)) ctx.LineTo(p, false, false); }
                ctx.LineTo(new Point(X(shadeTo), Y(shadeTo)), false, false);
                ctx.LineTo(new Point(X(shadeTo), Top + plotH), false, false);
            }
            g.Freeze();
            dc.DrawGeometry(K.Brush(K.WithAlpha(Tint, 0.28)), null, g);
            if (at >= lo && at <= hi)
            {
                dc.DrawLine(new Pen(K.Brush(Tint), 1.5), new Point(X(at), Y(at)), new Point(X(at), Top + plotH));
                var t = Words($"x = {StatsMath.Sig(at, 4)}", 11, Tint);
                dc.DrawText(t, new Point(Math.Min(w - Right - t.Width, X(at) + 5), Top + 2));
            }
        }
        foreach (var (mark, name) in new[] { (a, "a"), (b, "b") })
            if (mark is { } v && v >= lo && v <= hi)
            {
                dc.DrawLine(new Pen(K.Brush(K.WithAlpha(Ink, 0.5)), 1) { DashStyle = DashStyles.Dash }, new Point(X(v), Y(v)), new Point(X(v), Top + plotH));
                Label(dc, name, X(v), Y(v) - 14);
            }
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(curve[0], false, false);
            ctx.PolyLineTo(curve.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(K.Brush(Tint), 2) { LineJoin = PenLineJoin.Round }, geometry);
        dc.DrawLine(new Pen(K.Brush(K.WithAlpha(Ink, 0.5)), 1), new Point(Left, Top + plotH), new Point(w - Right, Top + plotH));
    }
}

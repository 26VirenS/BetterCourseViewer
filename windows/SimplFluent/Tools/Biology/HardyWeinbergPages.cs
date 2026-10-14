using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Hardy–Weinberg's Test and Simulate pages (spec §4.7, §5.4.2–§5.4.3), the Mac's HardyWeinbergPages.swift. Test: a
// sample's genotype counts against the proportions its own allele frequency predicts, χ² with df = 1 (editable, with the
// reason). Simulate: discrete generations of a diploid population — mutation, migration, mating with inbreeding,
// selection, then drift, in that order — p drawn for each replicate over the generations, the genotypes in bars, and the
// five conditions as chips that light up red when the settings break them. The simulation runs off the UI thread
// (HWRunner), the newest settings winning.

// ---- Test ----

internal sealed class HWTestPage : HWPageBase
{
    private readonly TextBox testAA, testAa, testaa;
    private readonly ContentControl problem = CrossKit.Host();
    private readonly ContentControl df = CrossKit.Host();
    private readonly ContentControl centre = CrossKit.Host();
    private readonly ContentControl right = CrossKit.Host();
    private bool syncing;

    public HWTestPage()
    {
        void Set(string? a, string? b, string? c) => HardyWeinbergModel.SetTest(a ?? testAA!.Text, b ?? testAa!.Text, c ?? testaa!.Text);
        (var fAA, testAA) = HWCountField.Make("AA", HWColors.AA.ToColor(), HardyWeinbergModel.TestAA, t => { if (!syncing) Set(t, null, null); });
        (var fAa, testAa) = HWCountField.Make("Aa", HWColors.Aa.ToColor(), HardyWeinbergModel.TestAa, t => { if (!syncing) Set(null, t, null); });
        (var faa, testaa) = HWCountField.Make("aa", HWColors.aa.ToColor(), HardyWeinbergModel.Testaa, t => { if (!syncing) Set(null, null, t); });
        var example = ChemKit.Wide("Example: 50 AA, 30 Aa, 20 aa", () => HardyWeinbergModel.SetTest("50", "30", "20"));
        var left = K.V(14,
            CrossKit.Heading("Genotype counts"), K.Columns("*, 8, *, 8, *", fAA.Col(0), fAa.Col(2), faa.Col(4)), problem, example,
            CrossKit.Heading("Degrees of freedom"), df, CrossKit.Caption(HWTest.DfNote));
        Children.Add(BioUI.Work(left, centre, right));
        CrossKit.FindKey(this, () => testAA);
        Draw();
    }

    protected override void Draw()
    {
        syncing = true;
        foreach (var (box, text) in new[] { (testAA, HardyWeinbergModel.TestAA), (testAa, HardyWeinbergModel.TestAa), (testaa, HardyWeinbergModel.Testaa) })
            if (box.Text != text && !box.IsKeyboardFocusWithin) box.Text = text;
        syncing = false;
        var r = HardyWeinbergModel.Test;
        problem.Content = r.Error is { } e ? CrossKit.Problem(e) : null;
        df.Content = CrossKit.Segmented(new[] { "df = 1", "df = 2" }, HardyWeinbergModel.Df == 2 ? 1 : 0, i => HardyWeinbergModel.SetDf(i + 1), "1 for population genetics; some courses use 2", uid: "df");
        centre.Content = Centre(r.Test);
        var side = K.V(14, ChiCriticalTable.Make(HardyWeinbergModel.Df, r.Test?.Result.Chi2, HWColors.Tool));
        if (r.Test is { } t) side.Children.Add(Working(t));
        right.Content = side;
    }

    private static FrameworkElement Centre(HWTest? t)
    {
        if (t == null) return CrossKit.CentreCard(K.Wrapped("Enter the counts of AA, Aa and aa individuals in a sample.", Ty.Body, B.Text2));
        var observed = new double[] { t.Counts.AA, t.Counts.Aa, t.Counts.aa };
        var lines = new List<UIElement>
        {
            K.Wrapped(t.Result.Conclusion, Ty.BodyStrong),
            K.Wrapped(t.Detail, Ty.Body),
        };
        if (t.LowExpected) lines.Add(ChemKit.Line(SymbolRegular.Warning16, "An expected count is under 5, where chi-square is unreliable.", K.Tone("orange")));
        var body = K.V(18,
            BioUI.FactRow("Null hypothesis (H₀)", t.NullHypothesis, HWColors.Tool),
            Table(t),
            new ObservedChart(new[] { "AA", "Aa", "aa" }, observed, t.Expected, HWColors.All, "expected under equilibrium"),
            BioUI.Facts(new (string, string, Color?)[]
            {
                ("χ²", HWFormat.Fixed(t.Result.Chi2, 4), null),
                ("Degrees of freedom", $"{t.Result.Df}", null),
                ("Critical value (p = 0.05)", HWFormat.Fixed(t.Result.Critical, 2), null),
                ("p-value", t.PText, null),
            }),
            CrossKit.Conclusion(t.Result.Reject, lines));
        return CrossKit.CentreCard(body);
    }

    private static FrameworkElement Table(HWTest t)
    {
        string[] names = { "AA", "Aa", "aa" }, formulas = { "N·p²", "N·2pq", "N·q²" };
        var observed = new[] { t.Counts.AA, t.Counts.Aa, t.Counts.aa };
        var colours = HWColors.All;
        var g = new Grid();
        for (var i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var row = 0;
        void Add(UIElement e, int col)
        {
            if (e is FrameworkElement fe) { fe.HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right; fe.Margin = new Thickness(col == 0 ? 0 : 14, 4, 0, 4); }
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            g.Children.Add(e);
        }
        void Next() => g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock Cell(string s, bool bold = false) => K.Text(s, bold ? Ty.BodyStrong : Ty.Body).Tabular();
        void Rule()
        {
            Next();
            var d = K.Divider();
            Grid.SetColumnSpan(d, 4);
            Add(d, 0);
            d.HorizontalAlignment = HorizontalAlignment.Stretch;
            row++;
        }
        Next();
        Add(CrossKit.Label("Genotype"), 0);
        Add(CrossKit.Label("Observed"), 1);
        Add(CrossKit.Label("Expected"), 2);
        Add(CrossKit.Label("(o − e)² ÷ e"), 3);
        row++;
        Rule();
        for (var i = 0; i < 3; i++)
        {
            Next();
            Add(K.H(6, K.Dot(colours[i], 9), K.Text(names[i], Ty.BodyStrong)), 0);
            Add(Cell($"{observed[i]}"), 1);
            Add(K.V(0, Cell(HWFormat.Fixed(t.Expected[i], 2)), K.Text(formulas[i], Ty.Caption, B.Text3)), 2);
            Add(Cell(HWFormat.Fixed(t.Result.Terms[i], 4)), 3);
            row++;
        }
        Rule();
        Next();
        Add(Cell($"N = {t.Counts.N}"), 0);
        Add(Cell($"{t.Counts.N}"), 1);
        Add(Cell(HWFormat.Fixed(t.Expected.Sum(), 2)), 2);
        Add(Cell("χ² = " + HWFormat.Fixed(t.Result.Chi2, 4), bold: true), 3);
        var b = new Border { Child = g, Padding = new Thickness(14), CornerRadius = new CornerRadius(12) };
        b.Bg(B.Subtle);
        AutomationProperties.SetName(b, "The test's table: " + string.Join("; ", names.Select((n, i) => $"{n}: observed {observed[i]}, expected {HWFormat.Fixed(t.Expected[i], 2)}")) + $". Chi-square {HWFormat.Fixed(t.Result.Chi2, 4)}");
        return b;
    }

    private static FrameworkElement Working(HWTest t) => BioUI.Card("From the counts", null, null,
        BioUI.FactRow("Allele frequency", $"p = (2 × {t.Counts.AA} + {t.Counts.Aa}) ÷ (2 × {t.Counts.N}) = {HardyWeinberg.Num(t.P)}; q = 1 − p = {HardyWeinberg.Num(t.Q)}"),
        BioUI.FactRow("Expected", "N·p², N·2pq and N·q² with this p — the proportions the population would have in equilibrium."),
        BioUI.FactRow("df = 1", "Three classes, less one, less one more for p, which was estimated from these same counts."));
}

// ---- Simulate ----

/// <summary>The simulation run off the UI thread when its settings change: after they rest 120 ms (a slider being
/// dragged), the newest settings winning; a picture waits for it (Shot).</summary>
internal static class HWRunner
{
    private static bool hooked;
    private static DispatcherTimer? wait;
    private static CancellationTokenSource? running;

    public static void Hook()
    {
        if (hooked) return;
        hooked = true;
        HardyWeinbergModel.SimChanged += Schedule;
    }

    /// <summary>A run for the settings as they are, if there is none yet.</summary>
    public static void Ensure()
    {
        if (HardyWeinbergModel.Running && running == null && wait?.IsEnabled != true) Go();
    }

    /// <summary>A run waiting for the settings to rest, which a picture waits for as it waits for a run (on a slow start the
    /// picture's own timer could otherwise come first and picture the empty chart).</summary>
    private static bool pending;

    private static void Schedule()
    {
        Shot.Moved();
        running?.Cancel();
        running = null;
        if (wait == null)
        {
            wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            wait.Tick += (_, _) =>
            {
                wait.Stop();
                Go();
                if (pending) { pending = false; Shot.Ended(); }
            };
        }
        if (!pending) { pending = true; Shot.Began(); }
        wait.Stop();
        wait.Start();
    }

    private static void Go()
    {
        running?.Cancel();
        var s = HardyWeinbergModel.Sim;
        var token = new CancellationTokenSource();
        running = token;
        var ui = Dispatcher.CurrentDispatcher;
        Shot.Began();
        Task.Run(() => HWSimulation.Run(s, () => token.IsCancellationRequested)).ContinueWith(t => ui.BeginInvoke(() =>
        {
            Shot.Ended();
            if (ReferenceEquals(running, token)) running = null;
            if (!token.IsCancellationRequested && t.Status == TaskStatus.RanToCompletion) HardyWeinbergModel.Finished(s, t.Result);
            else if (t.Exception is { } e) Log.Write("popgen simulation: " + e.InnerException?.Message);
        }), TaskScheduler.Default);
    }
}

/// <summary>
/// p over the generations, a line for each replicate population (one colour: they are the same population run again, not
/// different things), on a quiet grid with p from 0 to 1. Pointing at it marks a generation (the bars and the readout
/// follow it); leaving lets go. The lines are drawn again only when the populations change.
/// </summary>
internal sealed class HWChart : Grid
{
    private const double Left = 36, Bottom = 30, Top = 8, Right = 12;
    private readonly Canvas plot = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly Canvas marks = new() { IsHitTestVisible = false };
    private List<HWPopulation> pops = new();
    private int generations = 100;
    private int? hovered;

    public HWChart()
    {
        Height = 260;
        Children.Add(plot);
        Children.Add(marks);
        plot.SizeChanged += (_, _) => { DrawLines(); DrawHover(); };
        plot.MouseMove += (_, e) =>
        {
            var w = plot.ActualWidth - Left - Right;
            if (w <= 0) return;
            var x = e.GetPosition(plot).X - Left;
            HardyWeinbergModel.SetHovered((int)Math.Clamp(Math.Round(x / w * generations), 0, generations));
        };
        plot.MouseLeave += (_, _) => HardyWeinbergModel.SetHovered(null);
        plot.Cursor = Cursors.Cross;
    }

    public void Update(List<HWPopulation> populations, int gens, int? hover)
    {
        if (!ReferenceEquals(populations, pops) || gens != generations)
        {
            pops = populations;
            generations = Math.Max(gens, 1);
            DrawLines();
        }
        hovered = hover;
        DrawHover();
    }

    private double X(double t) => Left + (plot.ActualWidth - Left - Right) * t / generations;
    private double Y(double p) => Top + (plot.ActualHeight - Top - Bottom) * (1 - p);

    private void DrawLines()
    {
        plot.Children.Clear();
        double w = plot.ActualWidth, h = plot.ActualHeight;
        if (w <= Left + Right || h <= Top + Bottom) return;
        // (the grid: p at quarters, generations at a round step)
        foreach (var p in new[] { 0, 0.25, 0.5, 0.75, 1 })
        {
            var line = new Rectangle { Width = w - Left - Right, Height = 1, Opacity = p == 0 ? 0.8 : 0.4 };
            line.SetResourceReference(Shape.FillProperty, B.Divider);
            Canvas.SetLeft(line, Left);
            Canvas.SetTop(line, Y(p));
            plot.Children.Add(line);
            var t = K.Text(p.ToString("0.##", CultureInfo.InvariantCulture), Ty.Caption, B.Text3).Tabular();
            t.Width = Left - 6;
            t.TextAlignment = TextAlignment.Right;
            Canvas.SetTop(t, Y(p) - 8);
            plot.Children.Add(t);
        }
        var step = generations <= 25 ? 5 : generations <= 50 ? 10 : generations <= 100 ? 25 : 50;
        for (var g = 0; g <= generations; g += step)
        {
            var t = K.Text($"{g}", Ty.Caption, B.Text3).Tabular();
            t.Width = 40;
            t.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(t, X(g) - 20);
            Canvas.SetTop(t, h - Bottom + 4);
            plot.Children.Add(t);
        }
        var axis = K.Text("Generation", Ty.Caption, B.Text2);
        axis.Width = w;
        axis.TextAlignment = TextAlignment.Center;
        Canvas.SetTop(axis, h - 15);
        plot.Children.Add(axis);
        var colour = HWColors.Line.ToColor();
        foreach (var pop in pops)
        {
            var pl = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stroke = K.Brush(colour), Opacity = pops.Count > 1 ? 0.8 : 1, IsHitTestVisible = false };
            var pts = new PointCollection(pop.Generations.Count);
            for (var t = 0; t < pop.Generations.Count; t++) pts.Add(new Point(X(t), Y(pop.Generations[t].P)));
            pts.Freeze();
            pl.Points = pts;
            plot.Children.Add(pl);
        }
    }

    private void DrawHover()
    {
        marks.Children.Clear();
        if (hovered is not { } g || plot.ActualWidth <= Left + Right) return;
        var rule = new Rectangle { Width = 1, Height = Math.Max(0, plot.ActualHeight - Top - Bottom), Opacity = 0.5 };
        rule.SetResourceReference(Shape.FillProperty, B.Text2);
        Canvas.SetLeft(rule, X(g));
        Canvas.SetTop(rule, Top);
        marks.Children.Add(rule);
        foreach (var pop in pops)
        {
            if (g >= pop.Generations.Count) continue;
            var dot = new Ellipse { Width = 8, Height = 8, StrokeThickness = 2, Fill = K.Brush(HWColors.Line.ToColor()) };
            dot.SetResourceReference(Shape.StrokeProperty, B.Card);
            Canvas.SetLeft(dot, X(g) - 4);
            Canvas.SetTop(dot, Y(pop.Generations[g].P) - 4);
            marks.Children.Add(dot);
        }
    }
}

internal sealed class HWSimulatePage : HWPageBase
{
    private readonly List<(Slider Slider, TextBlock Value, Func<HWSimParams, double> Get, int Digits)> sliders = new();
    private readonly ToggleSwitch infinite;
    private readonly FrameworkElement sizeRow;
    private readonly Slider size;
    private readonly TextBlock sizeValue = K.Text("", Ty.BodyStrong).Tabular();
    private readonly ContentControl preset = CrossKit.Host();
    private readonly ContentControl replicates = CrossKit.Host();
    private readonly ContentControl generations = CrossKit.Host();
    private readonly TextBox seed;
    private readonly ContentControl chips = CrossKit.Host();
    private readonly TextBlock verdict = K.Wrapped("", Ty.BodyStrong);
    private readonly TextBlock readout = K.Text("", Ty.Caption, B.Text2).Tabular();
    private readonly ProgressRing spinner = new() { IsIndeterminate = true, Width = 14, Height = 14 };
    private readonly HWChart chart = new();
    private readonly ContentControl bars = CrossKit.Host();
    private readonly ContentControl right = CrossKit.Host();
    private bool syncing;

    private static HWSimParams S => HardyWeinbergModel.Sim;

    private static void Change(Func<HWSimParams, HWSimParams> edit) => HardyWeinbergModel.SetSim(edit(S));

    public HWSimulatePage()
    {
        // ---- population ----
        infinite = new ToggleSwitch { Content = "Infinite (no drift)", IsChecked = S.Size == null, ToolTip = "An infinite population follows the equations exactly; a finite one drifts by chance" };
        infinite.Checked += (_, _) => { if (!syncing) Change(s => s with { Size = null }); };
        infinite.Unchecked += (_, _) => { if (!syncing) Change(s => s with { Size = 100 }); };
        size = new Slider { Minimum = 1, Maximum = 4, Value = Math.Log10(S.Size ?? 100), SmallChange = 0.05, LargeChange = 0.25, ToolTip = "10 to 10,000 individuals (2N alleles drawn each generation)" };
        AutomationProperties.SetName(size, "Population size");
        size.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            var raw = Math.Pow(10, e.NewValue);
            var step = raw < 100 ? 1 : (raw < 1000 ? 10 : 100);
            var n = (int)Math.Clamp(Math.Round(raw / step) * step, HWSimParams.MinSize, HWSimParams.MaxSize);
            if (n != S.Size) Change(s => s with { Size = n });
        };
        var sizeTitle = K.Text("Size N", Ty.Body);
        sizeRow = K.V(2, K.Columns("*, auto", sizeTitle.Col(0), sizeValue.Col(1)), size);
        var population = K.V(12, CrossKit.Heading("Population"), infinite, sizeRow,
            SliderRow("Starting p", s => s.P0, (s, v) => s with { P0 = v }, 0, 1, 0.01, 2, "The A allele's frequency in generation 0"));
        // ---- selection ----
        var selection = K.V(12, CrossKit.Heading("Selection"), preset,
            SliderRow("w AA", s => s.WAA, (s, v) => s with { WAA = v }, 0, 1, 0.05, 2, "Fitness of AA: its share of offspring"),
            SliderRow("w Aa", s => s.WAa, (s, v) => s with { WAa = v }, 0, 1, 0.05, 2, "Fitness of Aa"),
            SliderRow("w aa", s => s.Waa, (s, v) => s with { Waa = v }, 0, 1, 0.05, 2, "Fitness of aa"));
        // ---- mutation, migration, mating ----
        var forces = K.V(12, CrossKit.Heading("Mutation, migration, mating"),
            SliderRow("u (A → a)", s => s.U, (s, v) => s with { U = v }, 0, 0.01, 0.0005, 4, "Mutation from A to a, per generation"),
            SliderRow("v (a → A)", s => s.V, (s, v) => s with { V = v }, 0, 0.01, 0.0005, 4, "Mutation from a to A, per generation"),
            SliderRow("Migration m", s => s.M, (s, v) => s with { M = v }, 0, 0.5, 0.01, 2, "The share of each generation's alleles from migrants"),
            SliderRow("Migrants' p", s => s.Pm, (s, v) => s with { Pm = v }, 0, 1, 0.01, 2, "The A allele's frequency among the migrants"),
            SliderRow("Inbreeding F", s => s.F, (s, v) => s with { F = v }, 0, 1, 0.05, 2, "0 is random mating; 1 is complete inbreeding"));
        // ---- runs ----
        seed = ToolKit.Box(S.Seed.ToString(CultureInfo.InvariantCulture), "1", t =>
        {
            if (!syncing && ulong.TryParse(t.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v != S.Seed) Change(s => s with { Seed = v });
        });
        Typography.SetNumeralAlignment(seed, FontNumeralAlignment.Tabular);
        AutomationProperties.SetName(seed, "Seed");
        // (the sheet gives its first text box the focus as it opens — here the seed, at the foot of the column: the column
        //  stays at its top, the population's settings in view, until a key or a press of the student's own moves it)
        var settled = false;
        PreviewKeyDown += (_, _) => settled = true;
        PreviewMouseDown += (_, _) => settled = true;
        seed.RequestBringIntoView += (_, e) => { if (!settled) e.Handled = true; };
        var next = K.IconButton(SymbolRegular.ArrowShuffle24, NextSeed, "Run again with the next seed (Ctrl+Shift+R)");
        var seedLabel = K.Text("Seed", Ty.Body);
        seedLabel.VerticalAlignment = VerticalAlignment.Center;
        var seedRow = K.Columns("auto, 8, *, 6, auto", seedLabel.Col(0), seed.Col(2), next.Col(4));
        var reset = K.Link("Back to no forces", () => Change(s => new HWSimParams { Size = s.Size, Seed = s.Seed, Replicates = s.Replicates, Generations = s.Generations, P0 = s.P0 }));
        reset.ToolTip = "Selection, mutation, migration and inbreeding off";
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(-6, 0, 0, 0);
        var runs = K.V(12, CrossKit.Heading("Runs"), replicates, generations, seedRow,
            CrossKit.Caption("Population r (from 0) draws from seed + r: a seed gives the same populations every time, here and in Simpl for Mac."), reset);
        var left = K.V(18, population, selection, forces, runs);

        // ---- the centre ----
        spinner.VerticalAlignment = VerticalAlignment.Center;
        var chartHead = K.Columns("*, auto, auto", CrossKit.Label("p, the A allele's frequency").Col(0), spinner.Margin(0, 0, 8, 0).Col(1), readout.Col(2));
        var centre = CrossKit.CentreCard(K.V(14, chips, verdict, K.V(6, chartHead, chart), bars));
        Children.Add(BioUI.Work(left, centre, right));
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.R && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { NextSeed(); e.Handled = true; }
        };
        Draw();
    }

    private static void NextSeed() => Change(s => s with { Seed = unchecked(s.Seed + 1) });

    /// <summary>A setting's slider: its name and value over it, snapped to its step.</summary>
    private FrameworkElement SliderRow(string title, Func<HWSimParams, double> get, Func<HWSimParams, double, HWSimParams> set, double lo, double hi, double step, int digits, string help)
    {
        var slider = new Slider { Minimum = lo, Maximum = hi, Value = get(S), SmallChange = step, LargeChange = step * 5, TickFrequency = step, IsSnapToTickEnabled = true, ToolTip = help };
        AutomationProperties.SetName(slider, title);
        var value = K.Text(HWFormat.Fixed(get(S), digits), Ty.BodyStrong).Tabular();
        slider.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            var v = Math.Round(Math.Round(e.NewValue / step) * step, 10);
            value.Text = HWFormat.Fixed(v, digits);
            if (v != get(S)) Change(s => set(s, v));
        };
        sliders.Add((slider, value, get, digits));
        return K.V(2, K.Columns("*, auto", K.Text(title, Ty.Body).Col(0), value.Col(1)), slider);
    }

    protected override void Draw()
    {
        var s = S;
        // (the controls follow the settings changed elsewhere: a preset, "back to no forces", a launch)
        syncing = true;
        foreach (var (slider, value, get, digits) in sliders)
        {
            if (Math.Abs(slider.Value - get(s)) > 1e-12 && !slider.IsMouseCaptureWithin) slider.Value = get(s);
            value.Text = HWFormat.Fixed(get(s), digits);
            AutomationProperties.SetItemStatus(slider, value.Text);
        }
        infinite.IsChecked = s.Size == null;
        sizeRow.Visibility = s.Size == null ? Visibility.Collapsed : Visibility.Visible;
        if (s.Size is { } n)
        {
            sizeValue.Text = n.ToString("N0", CultureInfo.InvariantCulture);
            if (!size.IsMouseCaptureWithin && Math.Abs(Math.Pow(10, size.Value) - n) > n * 0.06) size.Value = Math.Log10(n);
            AutomationProperties.SetItemStatus(size, sizeValue.Text);
        }
        if (seed.Text.Trim() != s.Seed.ToString(CultureInfo.InvariantCulture) && !seed.IsKeyboardFocusWithin) seed.Text = s.Seed.ToString(CultureInfo.InvariantCulture);
        syncing = false;
        var matching = HWSelections.Matching(s);
        preset.Content = CrossKit.Menu(K.Text(matching?.Title() ?? "Your own", Ty.BodyStrong), m =>
        {
            foreach (var p in HWSelections.All)
            {
                var pp = p;
                m.Items.Add(CrossKit.Item(p.Title(), p == matching, () => Change(x => x.With(pp))));
            }
        }, "Fitnesses of AA, Aa and aa", "preset");
        var stepperText = K.Text(ToolKit.Plural(s.Replicates, "population"), Ty.Body);
        stepperText.VerticalAlignment = VerticalAlignment.Center;
        var stepper = CrossKit.Keyed(ChemKit.Stepper(s.Replicates, 1, 10, r => Change(x => x with { Replicates = r }), "One population fewer", "One population more"), "reps");
        stepper.IsEnabled = s.Size != null;
        stepper.ToolTip = "Replicate populations: drift differs in each";
        replicates.Content = K.Columns("*, auto", stepperText.Col(0), stepper.Col(1));
        var gi = Array.IndexOf(HWSimParams.GenerationChoices, s.Generations);
        generations.Content = K.V(4, K.Text("Generations", Ty.Body),
            CrossKit.Segmented(HWSimParams.GenerationChoices.Select(g => $"{g}").ToList(), gi, i => Change(x => x with { Generations = HWSimParams.GenerationChoices[i] }), "Generations to run", stretch: true, uid: "gens"));

        // ---- the five conditions ----
        var conditions = HWSimulation.Assumptions(s);
        var wrap = new WrapPanel();
        var red = K.Tone("red");
        foreach (var a in conditions)
        {
            var icon = K.Icon(a.Holds ? SymbolRegular.Checkmark16 : SymbolRegular.Dismiss16, 11, a.Holds ? B.Text2 : B.OnAccent);
            if (!a.Holds) icon.Foreground = Brushes.White;
            var words = K.Text(a.Title, Ty.Caption).Strong();
            if (a.Holds) words.Fg(B.Text2); else words.Foreground = Brushes.White;
            var chip = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 4, 10, 5), Margin = new Thickness(0, 0, 6, 6), Child = K.H(4, icon, words), ToolTip = a.Holds ? $"{a.Title}: holds" : a.Detail };
            if (a.Holds) chip.Bg(B.Subtle); else chip.Background = K.Brush(Color.FromRgb(0xc4, 0x2b, 0x1c));
            AutomationProperties.SetName(chip, $"{a.Title}: {(a.Holds ? "holds" : "broken. " + a.Detail)}");
            wrap.Children.Add(chip);
        }
        chips.Content = wrap;
        var broken = conditions.Any(a => !a.Holds);
        verdict.Text = HWSimulation.Verdict(s);
        if (broken) verdict.Foreground = K.Brush(K.Darkened(red)); else verdict.Fg(B.Text2);

        // ---- the chart, the bars, the side ----
        var pops = HardyWeinbergModel.Populations;
        var shownRun = HardyWeinbergModel.RunFor ?? s;
        chart.Update(pops, shownRun.Generations, HardyWeinbergModel.Hovered);
        spinner.Visibility = HardyWeinbergModel.Running ? Visibility.Visible : Visibility.Collapsed;
        readout.Text = Readout(pops);
        AutomationProperties.SetName(chart, $"p over {shownRun.Generations} generations for {ToolKit.Plural(pops.Count, "population")}. {readout.Text}");
        bars.Content = Bars(pops);
        right.Content = Side(pops, shownRun);
    }

    private static string Readout(List<HWPopulation> pops)
    {
        var g = HardyWeinbergModel.Shown;
        var ps = pops.Where(p => g < p.Generations.Count).Select(p => p.Generations[g].P).ToList();
        if (ps.Count == 0) return "";
        if (ps.Count == 1) return $"generation {g}: p = {HardyWeinberg.Num(ps[0])}";
        return $"generation {g}: mean p = {HardyWeinberg.Num(ps.Average())}";
    }

    private static FrameworkElement Bars(List<HWPopulation> pops)
    {
        var g = HardyWeinbergModel.Shown;
        var v = K.V(6, CrossKit.Label($"Genotype frequencies in generation {g}{(g == 0 ? "" : ", after selection")}"));
        for (var r = 0; r < pops.Count; r++)
        {
            if (g < pops[r].Generations.Count)
            {
                var x = pops[r].Generations[g];
                var n = K.Text(pops.Count > 1 ? $"{r + 1}" : "", Ty.Caption, B.Text2).Tabular();
                n.Width = 18;
                n.TextAlignment = TextAlignment.Right;
                n.Margin = new Thickness(0, 0, 8, 0);
                v.Children.Add(K.Columns("auto, *", n.Col(0), GenotypeBar.Make(x.AA, x.Aa, x.aa, pops.Count > 5 ? 10 : 14, r == pops.Count - 1).Col(1)));
            }
            else
            {
                var t = K.Wrapped($"Population {r + 1} died out in generation {pops[r].ExtinctAt ?? 0}: mean fitness reached 0.", Ty.Caption);
                t.Foreground = K.Brush(K.Darkened(K.Tone("red")));
                v.Children.Add(t);
            }
        }
        return v;
    }

    private static FrameworkElement Side(List<HWPopulation> pops, HWSimParams s)
    {
        var g = HardyWeinbergModel.Shown;
        var fixedA = pops.Count(p => (p.Generations.LastOrDefault()?.P ?? 0) >= 1);
        var lost = pops.Count(p => (p.Generations.LastOrDefault()?.P ?? 1) <= 0);
        var dead = pops.Count(p => p.ExtinctAt != null);
        var rows = pops.Select((p, r) =>
        {
            var gens = p.Generations;
            var value = g < gens.Count ? HardyWeinberg.Num(gens[g].P) + (gens[g].K is { } k ? $"  ({k} of {2 * (s.Size ?? 0)})" : "") : "died out";
            return (pops.Count > 1 ? $"Population {r + 1}" : "p", value, (Color?)null);
        }).ToList();
        var lines = new List<UIElement?> { BioUI.Facts(rows) };
        if (pops.Count > 1 || s.Size != null)
            lines.Add(K.Wrapped($"At the end: A fixed in {fixedA}, lost in {lost}{(dead > 0 ? $", {dead} died out" : "")} of {pops.Count}.", Ty.Body));
        var generation = BioUI.Card($"Generation {g}", null, HardyWeinbergModel.Hovered == null ? "The last; point at the chart for another" : "Under the pointer", lines.ToArray());
        var order = BioUI.Card("One generation, in order", null, null,
            BioUI.FactRow("1. Mutation", "p₁ = p(1 − u) + (1 − p)v"),
            BioUI.FactRow("2. Migration", "p₂ = (1 − m)p₁ + m·pₘ (pₘ: the migrants' p)"),
            BioUI.FactRow("3. Mating", "AA = p₂² + Fp₂q₂ · Aa = 2p₂q₂(1 − F) · aa = q₂² + Fp₂q₂"),
            BioUI.FactRow("4. Selection", "w̄ = Σ genotype × w; each genotype × w ÷ w̄; p₃ = AA + Aa ÷ 2"),
            BioUI.FactRow("5. Drift", s.Size == null ? "None: an infinite population keeps p₃." : "2N alleles drawn, each A with chance p₃: p = k ÷ 2N."));
        return K.V(14, generation, order,
            BioUI.Caption("Drift is strongest in small populations: try N = 10 against N = 1,000 with several populations. Selection against a recessive slows as it gets rare, because most of its alleles hide in carriers."));
    }
}

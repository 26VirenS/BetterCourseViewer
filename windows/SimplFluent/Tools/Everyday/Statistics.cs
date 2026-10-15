using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Statistics (tools; the Mac's StatisticsTool): four pages under one heading — Describe (a sample's every figure, its
// histogram and box plot), Regression (the line through points, with a prediction), Distributions (the normal and t:
// probabilities and their inverses) and Tests (the one-sample, Welch and paired t-tests). The maths is
// Model/Statistics.cs; the pages and the chart are StatisticsPages.cs.

#pragma warning disable CA2255
internal static class StatisticsRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("stats", c => new StatisticsTool(c).View, c => new StatisticsCompact(c).View);
}
#pragma warning restore CA2255

/// <summary>
/// What Statistics is showing, kept on this PC (Prefs <c>SimplStats.&lt;field&gt;</c>): the page, and every field as typed
/// — the whole tool's and its pin's alike (the pin's data is the Describe page's).
/// </summary>
internal sealed class StatsState
{
    public static StatsState Shared { get; } = new();

    public static readonly string[] Tabs = { "Describe", "Regression", "Distributions", "Tests" };

    private readonly Dictionary<string, string> fields = new();

    public event Action? Changed;

    public int Tab
    {
        get => Math.Clamp(int.TryParse(Get("tab"), out var t) ? t : 0, 0, Tabs.Length - 1);
        set => Set("tab", value.ToString());
    }

    /// <summary>A field as typed ("data", "x", "mu"…), or what it starts as.</summary>
    public string Get(string name, string fallback = "")
    {
        if (fields.TryGetValue(name, out var v)) return v;
        return fields[name] = Prefs.String($"SimplStats.{name}") ?? fallback;
    }

    public void Set(string name, string value)
    {
        if (Get(name) == value) return;
        fields[name] = value;
        Prefs.Set($"SimplStats.{name}", value.Length == 0 ? null : value);
        Changed?.Invoke();
    }

    public double? Number(string name) => UnitMath.Number(Get(name));

    /// <summary>The page a launch asked for by its word (describe, regression, dist, tests).</summary>
    public static int? TabOf(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "describe" => 0, "regression" => 1, "dist" or "distributions" => 2, "tests" or "test" => 3, _ => null,
    };

    /// <summary>
    /// The screenshot suite's start: the page as --tool-state (describe, regression, dist, tests); --stats-data "2,4,4"
    /// (Describe's data and Tests' first sample), --stats-x / --stats-y (Regression's columns), --stats-b (Tests' second
    /// sample), --stats-mu0, --stats-test one|two|paired, --stats-normal "100,15,115[,85,115[,0.9]]" (μ, σ, x, a, b, p),
    /// --stats-t "10,2.228[,0.05]" (df, t, p).
    /// </summary>
    public void FromLaunch(string? state)
    {
        if (TabOf(state) is { } tab) Tab = tab;
        if (Launch.Take("", "stats-data") is { Length: > 0 } data) { Set("data", data); Set("sampleA", data); }
        if (Launch.Take("", "stats-x") is { Length: > 0 } x) Set("x", x);
        if (Launch.Take("", "stats-y") is { Length: > 0 } y) Set("y", y);
        if (Launch.Take("", "stats-b") is { Length: > 0 } b) Set("sampleB", b);
        if (Launch.Take("", "stats-mu0") is { Length: > 0 } mu0) Set("mu0", mu0);
        if (Launch.Take("", "stats-test") is { Length: > 0 } test) Set("test", test);
        if (Launch.Take("", "stats-normal") is { Length: > 0 } normal)
        {
            var parts = normal.Split(',');
            string[] names = { "mu", "sigma", "nx", "a", "b", "p" };
            for (var i = 0; i < parts.Length && i < names.Length; i++) Set(names[i], parts[i].Trim());
        }
        if (Launch.Take("", "stats-t") is { Length: > 0 } t)
        {
            var parts = t.Split(',');
            string[] names = { "tdf", "tt", "tp" };
            for (var i = 0; i < parts.Length && i < names.Length; i++) Set(names[i], parts[i].Trim());
        }
    }
}

/// <summary>A page of the Statistics tool: its view, and what it shows made again as the fields change.</summary>
internal abstract class StatsPage
{
    protected readonly StatsState s = StatsState.Shared;
    public abstract FrameworkElement View { get; }
    public abstract void Update();

    /// <summary>A multi-line box for numbers, its text kept in the state's field.</summary>
    protected TextBox Numbers(string field, string placeholder, string name, double minHeight = 96)
    {
        var box = new TextBox
        {
            Text = s.Get(field), PlaceholderText = placeholder, AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = minHeight, MaxHeight = 220, ClearButtonEnabled = false,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        box.TextChanged += (_, _) => { if (!Syncing) s.Set(field, box.Text); };
        AutomationProperties.SetName(box, name);
        Boxes[field] = box;
        return box;
    }

    /// <summary>A one-line box for a number, its text kept in the state's field.</summary>
    protected TextBox Number(string field, string placeholder, string name)
    {
        var box = ToolKit.Box(s.Get(field), placeholder, t => { if (!Syncing) s.Set(field, t); });
        AutomationProperties.SetName(box, name);
        Boxes[field] = box;
        return box;
    }

    protected readonly Dictionary<string, TextBox> Boxes = new();
    protected bool Syncing;

    /// <summary>The boxes shown what the state has (a launch's, an example's), without writing it back.</summary>
    protected void Sync()
    {
        Syncing = true;
        foreach (var (field, box) in Boxes) if (box.Text != s.Get(field)) box.Text = s.Get(field);
        Syncing = false;
    }

    /// <summary>A results card's row: its name, its value in tabular figures.</summary>
    public static FrameworkElement Row(string name, string value, bool strong = false)
    {
        var v = K.Text(value, strong ? Ty.BodyStrong : Ty.Body);
        v.FontFeatures();
        v.TextWrapping = TextWrapping.Wrap;
        v.TextAlignment = TextAlignment.Right;
        return K.Columns("*, auto", K.Text(name, Ty.Body, B.Text2).Col(0), v.Margin(12, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1)).Margin(0, 6, 0, 6);
    }

    /// <summary>Rows one under another with hairlines between.</summary>
    public static Stack Rows(IEnumerable<(string Name, string Value)> rows)
    {
        var list = K.V(0);
        foreach (var (name, value) in rows)
        {
            if (list.Children.Count > 0) list.Children.Add(K.Divider());
            list.Children.Add(Row(name, value));
        }
        return list;
    }

    /// <summary>The results as plain text for the clipboard.</summary>
    public static string Report(IEnumerable<(string Name, string Value)> rows) => string.Join("\n", rows.Select(r => $"{r.Name}: {r.Value}")).Replace("−", "-");

    /// <summary>A link that fills the fields with an example.</summary>
    protected static FrameworkElement Example(Action fill)
    {
        var link = K.Link("Example", fill, Ty.Caption);
        link.ToolTip = "Fill in an example";
        return link;
    }

    /// <summary>What a card says while its fields are empty or wrong.</summary>
    public static FrameworkElement Blank(string text) => K.Wrapped(text, Ty.Body, B.Text2);

    /// <summary>Statistics' own colour (its tile's).</summary>
    public static Color Tint => ToolsCenter.KindOf("stats")?.Color ?? K.Hex("#ff2d55");
}

/// <summary>
/// Statistics (the Mac's StatisticsTool): its four pages under a selector bar, each page's inputs and chart at the left
/// and its results at the right (the results first on a narrow window).
/// </summary>
internal sealed class StatisticsTool
{
    private readonly StatsState s = StatsState.Shared;
    private readonly ContentControl host = new() { Focusable = false };
    private readonly StatsPage[] pages;
    private readonly SelectorBar bar;
    private int shown = -1;
    private string said = "";

    public FrameworkElement View { get; }

    public StatisticsTool(ToolContext c)
    {
        s.FromLaunch(Everyday.LaunchState("stats"));
        if (c.TakeInfo("data") is { Length: > 0 } data) { s.Set("data", data); s.Tab = 0; }
        pages = new StatsPage[] { new DescribePage(), new RegressionPage(), new DistributionsPage(), new TestsPage() };
        bar = new SelectorBar(StatsState.Tabs, s.Tab) { HorizontalAlignment = HorizontalAlignment.Left };
        bar.Changed += i => { s.Tab = i; Update(); };
        View = Everyday.Page(K.V(16, bar, host));
        Update();
        Everyday.Follow(View, () => { s.Changed += Update; Update(); }, () => s.Changed -= Update);
    }

    private void Update()
    {
        var tab = s.Tab;
        if (bar.Selected != tab) bar.Select(tab, false);
        if (shown != tab)
        {
            shown = tab;
            host.Content = pages[tab].View;
        }
        pages[tab].Update();
        if (Shot.Asked)
        {
            var now = StatsState.Tabs[tab].ToLowerInvariant();
            if (said != now) { said = now; Log.Write($"stats: shows {now}"); }
        }
    }
}

/// <summary>Statistics' pin, opened (the Mac's StatisticsCompact): a sample's numbers, and its mean, median and sample
/// SD; Enter opens the whole tool on them (the numbers are the Describe page's already).</summary>
internal sealed class StatisticsCompact
{
    private readonly StatsState s = StatsState.Shared;
    private readonly TextBox data;
    private readonly TextBlock mean = Everyday.Figures("—", 26, FontWeights.Bold), median = Everyday.Figures("—", 26, FontWeights.Bold), sd = Everyday.Figures("—", 26, FontWeights.Bold);
    private readonly TextBlock line = K.Wrapped("", Ty.Caption, B.Text2);
    private bool syncing;

    public FrameworkElement View { get; }

    public StatisticsCompact(ToolContext c)
    {
        s.FromLaunch(null);
        data = new TextBox
        {
            Text = s.Get("data"), PlaceholderText = "2, 4, 4, 4, 5, 5, 7, 9", AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 72, MaxHeight = 140, ClearButtonEnabled = false, VerticalContentAlignment = VerticalAlignment.Top,
        };
        data.TextChanged += (_, _) => { if (!syncing) s.Set("data", data.Text); };
        data.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            c.OpenFull(new Dictionary<string, string> { ["data"] = data.Text }, null);
        };
        AutomationProperties.SetName(data, "Data");
        FrameworkElement Figure(string label, TextBlock t) => K.V(0, Everyday.Shrinks(t), K.Text(label, Ty.Caption, B.Text2));
        var figures = K.Columns("*, *, *", Figure("Mean", mean).Col(0), Figure("Median", median).Col(1), Figure("Sample SD", sd).Col(2));
        View = K.V(10, data, figures, line);
        Update();
        Everyday.Follow(View, () => { s.Changed += Update; Update(); }, () => s.Changed -= Update);
    }

    private void Update()
    {
        syncing = true;
        if (data.Text != s.Get("data")) data.Text = s.Get("data");
        syncing = false;
        var d = StatsMath.Describe(StatsMath.Numbers(s.Get("data")));
        mean.Text = d == null ? "—" : StatsMath.Sig(d.Mean);
        median.Text = d == null ? "—" : StatsMath.Sig(d.Median);
        sd.Text = d is { N: > 1 } ? StatsMath.Sig(d.SampleSd) : "—";
        line.Text = d == null ? "Numbers separated by spaces, commas or line breaks. Enter opens the whole tool." : $"n = {d.N} · min {StatsMath.Sig(d.Min)} · max {StatsMath.Sig(d.Max)}";
    }
}

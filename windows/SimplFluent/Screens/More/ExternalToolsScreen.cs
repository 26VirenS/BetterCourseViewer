using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// External Tools, in the navigation pane (the Mac's ExternalToolsView, 1.3.67): every tool the school provides in the
/// student's courses — a course's own (its navigation's) and those found only in its assignments and modules — a card a
/// tool: its name, its site, where it is found, and each course it is in, a press away (Knewton Alta and Macmillan in
/// their own windows, the rest in the tool window). The filter beside the title narrows them by name, site or course.
/// </summary>
public sealed class ExternalToolsScreen : Screen
{
    /// <summary>What the page's `externalTools` gives (the Mac's ExternalToolsData).</summary>
    private sealed class Data
    {
        public List<Tool> Rows { get; set; } = new();
        public string? Sub { get; set; }
        public string? Empty { get; set; }
    }

    private sealed class Tool
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public List<string> Kinds { get; set; } = new();
        public int Assignments { get; set; }
        public List<Course> Courses { get; set; } = new();
    }

    private sealed class Course
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Color { get; set; } = "";
        /// <summary>How it opens in this course: `course` with `tool` (its tab), `assignment` or `moduleItem`.</summary>
        public Dictionary<string, string> Open { get; set; } = new();
    }

    private readonly Loader<Data> model;
    private TextBox? filter;
    private string query = "";
    private bool hadRows;
    private string? lastSub;

    public ExternalToolsScreen(Engine.Engine engine) : base(engine) => model = Watch(new Loader<Data>());

    public override string Title => "External Tools";

    public override string? Subtitle => model.Data is { Rows.Count: > 0 } d ? d.Sub : null;

    /// <summary>The filter, beside the title once there are tools (the search box is the app's own).</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (model.Data is not { Rows.Count: > 0 }) return null;
            if (filter != null) return filter;
            filter = new TextBox
            {
                PlaceholderText = "Filter tools", Icon = new SymbolIcon { Symbol = SymbolRegular.Filter24 }, IconPlacement = ElementPlacement.Left,
                Width = 220, MinHeight = 32, VerticalAlignment = VerticalAlignment.Center,
            };
            filter.TextChanged += (_, _) =>
            {
                if (filter.Text == query) return;
                query = filter.Text;
                Render();
            };
            return filter;
        }
    }

    public override void Refresh() => model.Ensure(engine, "externalTools");

    protected override FrameworkElement Build()
    {
        var has = model.Data is { Rows.Count: > 0 };
        if (has != hadRows || Subtitle != lastSub) { hadRows = has; lastSub = Subtitle; HeaderChange(); }
        if (model.Data is not { } d) return K.LoadState(model.Error, () => model.Reload(engine, "externalTools"));
        if (d.Rows.Count == 0)
            return MoreKit.Unavailable(SymbolRegular.PuzzlePiece24, "No External Tools", d.Empty ?? "None of your courses has an external tool.", 420);
        var shown = Filtered(d.Rows);
        if (shown.Count == 0) return K.Text($"No tool matches “{query.Trim()}”.", Ty.Body, B.Text2);
        var grid = new MoreGrid(320, 16, shown.Select(t => (UIElement)Card(t)), maxColumn: 480);
        return grid;
    }

    private List<Tool> Filtered(List<Tool> rows)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0) return rows;
        return rows.Where(t => t.Name.ToLowerInvariant().Contains(q) || t.Host.ToLowerInvariant().Contains(q) || t.Courses.Any(c => c.Name.ToLowerInvariant().Contains(q))).ToList();
    }

    /// <summary>A tool: its letter on its first course's colour, its name, its site and where it is found, then each course it is in.</summary>
    private FrameworkElement Card(Tool t)
    {
        var tint = K.Hex(t.Courses.FirstOrDefault()?.Color, Color.FromRgb(0x8e, 0x8e, 0x93));
        var letter = new TextBlock
        {
            Text = t.Name.Length > 0 ? t.Name[..1].ToUpperInvariant() : "•", FontSize = 19, FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var tile = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(8), Child = letter, VerticalAlignment = VerticalAlignment.Top,
            Background = new LinearGradientBrush(K.Mix(tint, Colors.White, 0.12), K.Mix(tint, Colors.Black, 0.12), 90),
        };
        var words = K.V(2, K.Wrapped(t.Name, Ty.BodyStrong, B.Text, 2));
        if (t.Host.Length > 0) words.Children.Add(K.Text(t.Host, Ty.Caption, B.Text2));
        words.Children.Add(K.Wrapped(WhereFound(t), Ty.Caption, B.Text2, 2));
        var head = K.Columns("auto, *", tile.Margin(0, 0, 12, 0).Col(0), words.Col(1));
        // (each course it is in, opened there)
        var courses = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in t.Courses) courses.Children.Add(CoursePill(t, c));
        var card = K.Card(K.V(12, head, courses), new Thickness(16));
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    private FrameworkElement CoursePill(Tool t, Course c)
    {
        var name = K.Text(c.Name, Ty.Caption);
        name.FontWeight = FontWeights.Medium;
        name.MaxWidth = 220;
        var row = K.H(6, K.Dot(K.Hex(c.Color), 7), name, K.Icon(SymbolRegular.ArrowUpRight20, 10, B.Text2));
        var b = K.Subtle(row, () => Open(t, c), new Thickness(10, 5, 10, 6), 12);
        b.SetResourceReference(Control.BackgroundProperty, B.Subtle3);
        b.Stroke(B.ControlStroke);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.Margin = new Thickness(0, 0, 6, 6);
        b.ToolTip = $"Open {t.Name} in {c.Name}";
        return b;
    }

    /// <summary>Where it was found: a course's navigation, its assignments (how many), its modules.</summary>
    private static string WhereFound(Tool t)
    {
        var parts = new List<string> { t.Courses.Count == 1 ? "1 course" : $"{t.Courses.Count} courses" };
        if (t.Kinds.Contains("Course navigation")) parts.Add("Course navigation");
        if (t.Assignments > 0) parts.Add(t.Assignments == 1 ? "1 assignment" : $"{t.Assignments} assignments");
        if (t.Kinds.Contains("Modules")) parts.Add("Modules");
        return string.Join(" · ", parts);
    }

    /// <summary>Opened in that course: Knewton Alta's and Macmillan's assignments in their own windows, the rest in the tool window.</summary>
    private void Open(Tool t, Course c)
    {
        var label = (t.Name + " " + t.Host).ToLowerInvariant();
        if (c.Open.TryGetValue("assignment", out var a) && c.Open.TryGetValue("course", out var course))
        {
            if (label.Contains("knewton")) { engine.OpenAlta(new AltaLaunch(course, a, t.Name)); return; }
            if (label.Contains("macmillan") || label.Contains("sapling") || label.Contains("achieve.")) { engine.OpenMacmillan(new AltaLaunch(course, a, t.Name)); return; }
        }
        engine.OpenTool(new ToolLaunch(t.Name, c.Open));
    }
}

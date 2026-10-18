using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>(0.3.32, the Mac's MacmillanState) Macmillan's assessment as its skin reads it (extension/tools/macmillan/
/// skin.js): before it is opened, its cover; then the question shown, the list of questions, the score and the controls.</summary>
internal sealed class MacmillanState
{
    public sealed class CoverInfo
    {
        public string? Name { get; set; }
        public string? Due { get; set; }
        public int? Count { get; set; }
        public List<string>? Policies { get; set; }
    }

    public sealed class Item
    {
        public int N { get; set; }
        public string Label { get; set; } = "";
        /// <summary>"default" (not yet answered), "success" (solved), else Macmillan's own word for it.</summary>
        public string State { get; set; } = "default";
        public string Note { get; set; } = "";
        public string? Points { get; set; }
        public bool Solved => State == "success";
        public bool Tried => !Solved && State != "default";
    }

    public sealed class ToolsInfo
    {
        public bool Resources { get; set; }
        public bool Tutor { get; set; }
        public bool Solution { get; set; }
        public bool Layout { get; set; }
    }

    public sealed class AttemptsInfo
    {
        public string Label { get; set; } = "";
        public List<string> Choices { get; set; } = new();
    }

    public string Kind { get; set; } = "";
    /// <summary>"cover", "question" or "other".</summary>
    public string Page { get; set; } = "";
    public CoverInfo? Cover { get; set; }
    public string? Title { get; set; }
    public int? Index { get; set; }
    public int? Count { get; set; }
    public int? Completed { get; set; }
    public double[]? Score { get; set; }
    public List<string>? Policies { get; set; }
    public List<Item>? Items { get; set; }
    public string? Main { get; set; }
    public bool? MainOn { get; set; }
    public ToolsInfo? Tools { get; set; }
    public AttemptsInfo? Attempts { get; set; }
    public bool? First { get; set; }
    public bool? Last { get; set; }

    public bool IsQuestion => Page == "question";
    public bool IsCover => Page == "cover";
}

/// <summary>The skin's parts (the Mac's MacmillanRail, MacmillanTile, MacmillanBar, MacmillanStartScreen).</summary>
internal static class MacmillanParts
{
    /// <summary>Down the left, as Alta's objectives are: the score and how many questions are done, every question as a
    /// tile, Show browser, and the grading settings.</summary>
    public static FrameworkElement Rail(MacmillanState s, bool browser, Action<bool> setBrowser, Action<int> go)
    {
        var got = s.Score is { Length: > 0 } sc ? sc[0] : 0;
        var total = s.Score is { Length: > 1 } sc2 ? sc2[1] : 0;
        var count = s.Count ?? s.Items?.Count ?? 0;
        var bar = new ProgressBar { Minimum = 0, Maximum = total > 0 ? total : 1, Value = Math.Min(got, total), Height = 6 };
        var score = K.V(8,
            K.Columns("*, auto", K.Text("Score", Ty.BodyStrong).Col(0), K.Text(total > 0 ? $"{Number(got)} / {Number(total)}" : "–", Ty.BodyStrong).Col(1)),
            bar,
            K.Text($"{s.Completed ?? 0} of {count} questions completed", Ty.Caption, B.Text2));

        var grid = new UniformGrid { Columns = 5 };
        foreach (var item in s.Items ?? new())
        {
            var it = item;
            grid.Children.Add(Tile(it, it.N - 1 == s.Index, () => go(it.N)));
        }
        var questions = K.Card(K.V(10, K.Text("Questions", Ty.BodyStrong), grid), new Thickness(14), 8);

        var show = K.Button("Show browser", () => setBrowser(!browser), icon: SymbolRegular.Globe24);
        show.Appearance = browser ? ControlAppearance.Primary : ControlAppearance.Secondary;
        show.ToolTip = browser ? "Back to Simpl’s view of the assignment" : "Show Macmillan Learning’s own page, as Macmillan draws it";
        show.HorizontalAlignment = HorizontalAlignment.Left;

        var v = K.V(16, score, questions, show);
        if (s.Policies is { Count: > 0 } p)
        {
            var list = K.V(8);
            foreach (var line in p) list.Children.Add(K.H(8, K.Icon(Symbol(line), 16, B.Text2), K.Wrapped(line, Ty.Caption, B.Text2)));
            v.Children.Add(new Expander { Header = K.Text("Grading settings", Ty.BodyStrong), Content = list.Margin(0, 8, 0, 0), IsExpanded = true });
        }
        var scroll = new ScrollViewer { Content = v.Margin(18, 14, 18, 24), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return new Border { Width = 270, Child = scroll, BorderThickness = new Thickness(0, 0, 1, 0) }.Stroke(B.Divider);
    }

    /// <summary>One question's tile: its number; the one shown filled, a solved one washed with a tick, one tried and not
    /// yet right marked in orange. A press opens it.</summary>
    private static Button Tile(MacmillanState.Item item, bool current, Action open)
    {
        var orange = Color.FromRgb(0xe3, 0x6c, 0x0a);
        var green = Color.FromRgb(0x10, 0x89, 0x3e);
        var face = new Border { Height = 32, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 6, 6) };
        if (current) face.SetResourceReference(Border.BackgroundProperty, B.Accent);
        else face.Background = K.Brush(item.Solved ? Color.FromArgb(48, green.R, green.G, green.B) : item.Tried ? Color.FromArgb(44, orange.R, orange.G, orange.B) : Color.FromArgb(20, 128, 128, 128));
        face.Child = item.Solved && !current
            ? K.Icon(SymbolRegular.Checkmark24, 14, green)
            : K.Text(item.Label.Replace("Q", ""), Ty.BodyStrong, current ? B.OnAccent : B.Text2);
        if (face.Child is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        if (item.Tried && !current && face.Child is TextBlock t) t.Foreground = K.Brush(orange);
        var b = K.Subtle(face, open, new Thickness(0), 6);
        b.ToolTip = string.Join(" · ", new[] { item.Label, item.Note, item.Points ?? "" }.Where(x => x.Length > 0));
        System.Windows.Automation.AutomationProperties.SetName(b, $"Question {item.N}, {item.Note}{(current ? ", shown" : "")}");
        return b;
    }

    /// <summary>Along the page's foot: the arrows and which question this is, Question or My answer once it has been
    /// answered, Resources, AI Tutor, Solution and Switch to new layout, and the main button.</summary>
    public static FrameworkElement Bar(MacmillanState s, Action<string, object?> act)
    {
        var index = s.Index ?? 0;
        var count = s.Count ?? 0;
        var prev = K.IconButton(SymbolRegular.ChevronLeft24, () => act("prev", null), "Previous question");
        prev.IsEnabled = s.First != true && index > 0;
        var next = K.IconButton(SymbolRegular.ChevronRight24, () => act("next", null), "Next question");
        next.IsEnabled = s.Last != true && index + 1 < count;
        var left = K.H(6, prev, next, K.Text($"Question {index + 1} of {count}", Ty.BodyStrong).Margin(6, 0, 0, 0));
        foreach (UIElement c in left.Children) if (c is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Center;
        if (s.Attempts is { Choices.Count: > 1 } a)
        {
            var seg = K.H(2);
            for (var i = 0; i < a.Choices.Count; i++)
            {
                var k = i;
                var on = a.Choices[i] == a.Label;
                var b = K.Button(a.Choices[i], () => act("attempt", k));
                b.Appearance = on ? ControlAppearance.Primary : ControlAppearance.Secondary;
                b.MinWidth = 0;
                seg.Children.Add(b);
            }
            left.Children.Add(seg.Margin(12, 0, 0, 0));
        }
        var right = K.H(8);
        if (s.Tools is { } t)
        {
            if (t.Resources) right.Children.Add(K.Button("Resources", () => act("resources", null), icon: SymbolRegular.Book24));
            if (t.Tutor) right.Children.Add(K.Button("AI Tutor", () => act("tutor", null), icon: SymbolRegular.Sparkle24));
            if (t.Solution) right.Children.Add(K.Button("Solution", () => act("solution", null), icon: SymbolRegular.Lightbulb24));
            if (t.Layout)
            {
                Button? more = null;
                more = K.IconButton(SymbolRegular.MoreHorizontal24, () =>
                {
                    var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Top };
                    var mi = new System.Windows.Controls.MenuItem { Header = "Switch to new layout" };
                    mi.Click += (_, _) => act("layout", null);
                    menu.Items.Add(mi);
                    menu.IsOpen = true;
                }, "More of Macmillan’s");
                right.Children.Add(more);
            }
        }
        if (s.Main is { Length: > 0 } main)
        {
            var go = K.Button(main, () => act("main", null), accent: true);
            go.IsEnabled = s.MainOn != false;
            go.IsDefault = true;
            right.Children.Add(go);
        }
        foreach (UIElement c in right.Children) if (c is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("auto, *, auto", left.Col(0), right.Col(2));
        return new Border { Child = row, Padding = new Thickness(16, 10, 16, 12), BorderThickness = new Thickness(0, 1, 0, 0) }.Bg(B.Layer).Stroke(B.Divider);
    }

    /// <summary>Before the assessment is opened, its cover as Alta's start screen is: its name, when it is due and how
    /// many questions it has, its grading settings, and Open assignment.</summary>
    public static FrameworkElement Start(MacmillanState.CoverInfo c, Action open)
    {
        var line = string.Join(" · ", new[] { c.Due ?? "", c.Count is { } n ? $"{n} question{(n == 1 ? "" : "s")}" : "" }.Where(x => x.Length > 0));
        var v = K.V(16, K.Text(c.Name ?? "Assignment", Ty.Title), K.Text(line, Ty.Body, B.Text2));
        if (c.Policies is { Count: > 0 } p)
        {
            var list = K.V(10, K.Text("Grading settings", Ty.BodyStrong));
            foreach (var l in p) list.Children.Add(K.H(10, K.Icon(Symbol(l), 18, B.Text2), K.Wrapped(l, Ty.Body)));
            v.Children.Add(K.Card(list, new Thickness(16), 8));
        }
        Wpf.Ui.Controls.Button? go = null;
        go = K.Button("Open assignment", () => { go!.IsEnabled = false; open(); }, accent: true);
        go.IsDefault = true;
        go.HorizontalAlignment = HorizontalAlignment.Left;
        v.Children.Add(go);
        v.MaxWidth = 620;
        v.Margin = new Thickness(28);
        v.HorizontalAlignment = HorizontalAlignment.Center;
        return new ScrollViewer { Content = v, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }.Bg(B.Solid);
    }

    private static string Number(double v) => v == Math.Round(v) ? ((int)v).ToString(System.Globalization.CultureInfo.CurrentCulture) : v.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>A grading setting's symbol, by what it says.</summary>
    private static SymbolRegular Symbol(string line)
    {
        var l = line.ToLowerInvariant();
        if (l.Contains("attempt") && !l.Contains("incorrect")) return SymbolRegular.ArrowCounterclockwise24;
        if (l.Contains("partial credit") || l.Contains("incorrect")) return SymbolRegular.SubtractCircle24;
        if (l.Contains("late")) return SymbolRegular.Clock24;
        if (l.Contains("solution")) return SymbolRegular.Lightbulb24;
        if (l.Contains("tutor")) return SymbolRegular.Sparkle24;
        if (l.Contains("resource")) return SymbolRegular.Book24;
        return SymbolRegular.CheckmarkCircle24;
    }
}

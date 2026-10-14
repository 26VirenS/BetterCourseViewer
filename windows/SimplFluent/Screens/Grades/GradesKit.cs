using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// What Grades and a course's own grades share (the Mac's LetterChip, StatusChip, GradeBar, CategoryLine, NestedRings,
/// GradeWeightBar and the colours each group's ring wears), drawn in Windows 11's look: the words and numbers as the
/// page gives them, the colours of the course and its groups, everything else a theme resource.
/// </summary>
internal static class GradesKit
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The rings' own colours for groups that bring none (the Mac's: blue, purple, orange, teal, pink, indigo).</summary>
    public static readonly Color[] Palette =
    {
        Color.FromRgb(0x00, 0x7a, 0xff), Color.FromRgb(0xaf, 0x52, 0xde), Color.FromRgb(0xff, 0x95, 0x00),
        Color.FromRgb(0x30, 0xb0, 0xc7), Color.FromRgb(0xff, 0x2d, 0x55), Color.FromRgb(0x58, 0x56, 0xd6),
    };

    /// <summary>What-If's own colour (the Mac's orange): its switch, its wand, the scores tried.</summary>
    public static Color Orange => K.Tone("orange");

    /// <summary>A group with no colour of its own and no score yet (the Mac's secondary).</summary>
    public static Color Gray => K.Tone(null);

    // ---- numbers as the Mac writes them ----

    /// <summary>Points as Canvas writes them: whole numbers bare, others as short as they go.</summary>
    public static string Num(double v) =>
        !double.IsFinite(v) ? "—" : v == Math.Round(v) && Math.Abs(v) < 1e15 ? ((long)v).ToString(Inv) : v.ToString("G6", Inv);

    /// <summary>A course's score as the engine writes it ("87.5%", "92%"; "–" with none) — worked out here, the same way,
    /// only for a page a version older that sends no words for it.</summary>
    public static string Percent(string? text, double? v) =>
        !string.IsNullOrEmpty(text) && text != "N/A" ? text! : v is { } x ? Half(x, 1).ToString("0.#", Inv) + "%" : "–";

    /// <summary>A number rounded as every Simpl writes it: half up on its decimal value (3.425 is 3.43, 81.25 is 81.3), not
    /// on the binary double (which holds 3.425 as 3.42499…) nor to even (.NET's own Math.Round).</summary>
    public static decimal Half(double v, int places) =>
        double.IsFinite(v) ? Math.Round((decimal)v, places, MidpointRounding.AwayFromZero) : 0;

    /// <summary>A GPA or a goal to two places, half up ("3.43", "4.00").</summary>
    public static string Gpa(double v) => Half(v, 2).ToString("0.00", Inv);

    /// <summary>A share of the grade: whole numbers bare ("40%"), others to one place.</summary>
    public static string Share(double w) => w == Math.Round(w) ? ((long)w).ToString(Inv) + "%" : w.ToString("0.0", Inv) + "%";

    /// <summary>A number typed (a comma taken as the decimal point), if it is one.</summary>
    public static double? Number(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return double.TryParse(s.Replace(",", ".").Trim(), NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v : null;
    }

    /// <summary>"7 of 18 graded · 1 not posted yet", as the engine counts it for this page and the course's own Grades
    /// alike (its own count only from a page a version older).</summary>
    public static string GradedLine(GradeRow r) =>
        !string.IsNullOrEmpty(r.GradedText) ? r.GradedText! : r.Total > 0 ? $"{r.Graded} of {r.Total} graded" : "Nothing graded yet";

    /// <summary>A course's second line: its other name, only when it says something its label does not — never the same
    /// code again with dashes ("MATH-021-20" under "F26-MATH 021 20"). A page a version older sends no AltName: its name,
    /// where it differs.</summary>
    public static string? SecondName(GradeRow r) =>
        r.AltName != null ? (r.AltName.Length > 0 ? r.AltName : null) : !string.IsNullOrEmpty(r.Name) && r.Name != r.Code ? r.Name : null;

    /// <summary>Why a course has no score, in words ("Hidden by instructor", "No grade yet"), or null with a score.</summary>
    public static string? Why(GradeRow r) => r.Pct != null || string.IsNullOrEmpty(r.Why) ? null : r.Why;

    /// <summary>What that means, for its tooltip (the Mac's words).</summary>
    public static string WhyTip(GradeRow r) => r.Hidden == true
        ? "The instructor hides this course’s total, so there is no score to show. The term GPA leaves it out."
        : "Nothing in this course has a score yet. The term GPA leaves it out until it has one.";

    /// <summary>Why a course has no score, where its letter would be: a slashed eye for a total the instructor hides, a
    /// clock for one with nothing graded yet, and the words — never a bare "N/A" beside a line saying work is graded.</summary>
    public static FrameworkElement WhyNote(GradeRow r, string why)
    {
        var h = K.H(5, K.Icon(r.Hidden == true ? SymbolRegular.EyeOff16 : SymbolRegular.Clock16, 12, B.Text2), K.Text(why, Ty.Caption, B.Text2));
        h.ToolTip = WhyTip(r);
        return h;
    }

    /// <summary>Digits all one width, so a column of scores lines up (the Mac's monospacedDigit).</summary>
    public static T Tabular<T>(this T e) where T : DependencyObject
    {
        Typography.SetNumeralAlignment(e, FontNumeralAlignment.Tabular);
        return e;
    }

    // ---- colours ----

    /// <summary>Each group's colour, as its ring has it: those with a score in order on the rings' palette (or their own
    /// colour), those without in their own colour or grey.</summary>
    public static Dictionary<string, Color> GroupColors(IEnumerable<CGGroup> groups)
    {
        var colors = new Dictionary<string, Color>();
        var graded = 0;
        foreach (var g in groups)
        {
            if (g.Pct != null) { colors[g.Id] = Appearance.Hex(g.Color) ?? Palette[graded % Palette.Length]; graded++; }
            else colors[g.Id] = Appearance.Hex(g.Color) ?? Gray;
        }
        return colors;
    }

    /// <summary>The same for a course's groups as Grades lists them (in order: a group with no score yet has no ring).</summary>
    public static List<Color> CategoryColors(IEnumerable<GradeCategory> cats)
    {
        var colors = new List<Color>();
        var graded = 0;
        foreach (var c in cats)
        {
            if (c.Pct != null) { colors.Add(Appearance.Hex(c.Color) ?? Palette[graded % Palette.Length]); graded++; }
            else colors.Add(Appearance.Hex(c.Color) ?? Gray);
        }
        return colors;
    }

    /// <summary>A letter's colour: A green, B teal, C yellow, D orange, F red; anything else (Pass, Complete) grey.</summary>
    public static Color LetterColor(string band) => band switch
    {
        "A" => K.Tone("green"),
        "B" => Color.FromRgb(0x30, 0xb0, 0xc7),
        "C" => K.Tone("yellow"),
        "D" => K.Tone("orange"),
        "F" or "E" => K.Tone("red"),
        _ => K.Tone(null),
    };

    // ---- small pieces ----

    /// <summary>A letter grade on a wash of its letter's colour, as the web's grade tags (the Mac's LetterChip).</summary>
    public static Border Letter(string letter)
    {
        var chip = K.Chip(letter, LetterColor(letter.Length > 0 ? letter[..1] : ""), strong: true);
        chip.MinWidth = 28;
        chip.Padding = new Thickness(7, 1, 7, 2);
        if (chip.Child is TextBlock t)
        {
            t.FontWeight = FontWeights.Bold;
            t.TextAlignment = TextAlignment.Center;
        }
        return chip;
    }

    /// <summary>A status in words on a wash of its colour: Missing, Late, Excused, Dropped (the Mac's StatusChip).</summary>
    public static Border Status(string text, string? tone = null) => K.Chip(text, tone switch
    {
        "bad" => K.Tone("red"),
        "warn" => K.Tone("orange"),
        "good" => K.Tone("green"),
        "info" => K.Tone("blue"),
        "purple" => K.Tone("purple"),
        _ => K.Tone(null),
    }, strong: true);

    /// <summary>A badge's tone: Missing bad, Late a warning, Excused for information.</summary>
    public static string Tone(string badge) => badge switch { "Missing" => "bad", "Late" => "warn", "Excused" => "info", _ => "" };

    /// <summary>A card's own heading, with what is at its right (the Mac's CardHeading).</summary>
    public static FrameworkElement CardHeading(string text, string? trailing) =>
        CardHeadingWith(text, string.IsNullOrEmpty(trailing) ? null : K.Text(trailing!, Ty.Caption, B.Text2));

    public static FrameworkElement CardHeadingWith(string text, TextBlock? trailing)
    {
        var g = K.Columns("auto, *", K.Text(text, Ty.BodyStrong).Col(0));
        if (trailing != null)
        {
            trailing.Margin = new Thickness(12, 0, 0, 1);
            trailing.HorizontalAlignment = HorizontalAlignment.Right;
            trailing.VerticalAlignment = VerticalAlignment.Bottom;
            g.Children.Add(trailing.Col(1));
        }
        return g;
    }

    /// <summary>A section's heading over its rows in one card (the Mac's CardSection).</summary>
    public static FrameworkElement CardSection(string title, string? trailing, UIElement content) =>
        K.V(0, Heading.Section(title, string.IsNullOrEmpty(trailing) ? null : trailing), K.Card(content, new Thickness(4)));

    // ---- rings inside rings ----

    /// <summary>
    /// Rings inside rings, as the Activity rings (the Mac's NestedRings): the course's total outermost in its colour, then
    /// one for each assignment group with graded work, in the group's colour or the rings' palette — as many as fit.
    /// </summary>
    public static FrameworkElement Rings(double? total, Color color, IEnumerable<(double? Pct, string? Color)> groups, int limit,
        double size, double outer, double inner, double gap)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(K.Ring(total / 100, color, size, outer));
        var i = 0;
        foreach (var (pct, hex) in groups.Where(x => x.Pct != null).Take(limit))
        {
            var inset = outer + gap + i * (inner + gap);
            var s = size - 2 * inset;
            if (s < inner * 3) break;
            g.Children.Add(K.Ring(pct / 100, Appearance.Hex(hex) ?? Palette[i % Palette.Length], s, inner));
            i++;
        }
        return g;
    }

    public static IEnumerable<(double? Pct, string? Color)> Bands(IEnumerable<GradeCategory> cats) => cats.Select(c => (c.Pct, c.Color));

    public static IEnumerable<(double? Pct, string? Color)> Bands(IEnumerable<CGGroup> groups) => groups.Select(g => (g.Pct, g.Color));

    // ---- bars ----

    private static readonly HashSet<string> Filled = new();

    /// <summary>
    /// A bar filled to a share (a group's score, the GPA out of 4) in its colour over a wash of it (the Mac's GradeBar). It
    /// fills once, the first time it shows anywhere; <paramref name="mark"/> draws a tick across it (the goal).
    /// </summary>
    public static FrameworkElement Bar(double? value, Color color, double height, double? mark = null, string? key = null)
    {
        var v = Math.Clamp(value ?? 0, 0, 1);
        var g = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = K.Brush(K.WithAlpha(color, 0.16)) });
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), Background = K.Brush(color), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        g.Children.Add(fill);
        Border? tick = null;
        if (mark != null)
        {
            // (a canvas, so the tick may stand out above and below the bar)
            tick = new Border { Width = 2, Height = height + 6, CornerRadius = new CornerRadius(1), Opacity = 0.55 }.Bg(B.Text);
            Canvas.SetTop(tick, -3);
            g.Children.Add(new Canvas { ClipToBounds = false, IsHitTestVisible = false, Children = { tick } });
        }
        g.SizeChanged += (_, e) =>
        {
            var w = e.NewSize.Width;
            fill.Width = v > 0 ? Math.Max(w * v, height) : 0;
            if (tick != null && mark is { } m) Canvas.SetLeft(tick, Math.Clamp(w * Math.Clamp(m, 0, 1) - 1, 0, Math.Max(w - 2, 0)));
        };
        if (key != null && Filled.Add(key) && v > 0)
        {
            var s = new ScaleTransform(0, 1);
            fill.RenderTransformOrigin = new Point(0, 0.5);
            fill.RenderTransform = s;
            Motion.FromTo(s, ScaleTransform.ScaleXProperty, 0, 1, 700, Motion.Gentle, 50);
        }
        return g;
    }

    /// <summary>One assignment group's line: its colour, its name, its weight and its score, and a bar filled to the score.</summary>
    public static FrameworkElement CategoryLine(string name, string? weight, string? value, double? pct, Color color, string? key)
    {
        var top = K.Columns("auto, *, auto, auto",
            K.Dot(color, 9).Margin(0, 0, 8, 0).Col(0),
            K.Text(name, Ty.Body).Col(1),
            string.IsNullOrEmpty(weight) ? null : K.Text(weight!, Ty.Caption, B.Text2).Margin(6, 0, 8, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2),
            K.Text(value ?? "–", Ty.BodyStrong).Tabular().Col(3));
        var v = K.V(5, top, Bar(pct / 100, color, 6, null, key));
        AutomationProperties.SetName(v, string.Join(", ", new[] { name, weight, value ?? "no score" }.Where(s => !string.IsNullOrEmpty(s))));
        return v;
    }

    /// <summary>
    /// A weighted course's grade broken down, as the web draws it (the Mac's GradeWeightBar): one bar, a piece a group,
    /// each as wide as its share of the grade — filled in its colour once something in it is graded, a dashed outline until
    /// then. A piece pointed at says its group and share under the bar, the others fading back.
    /// </summary>
    public static FrameworkElement WeightBar(List<CGGroup> groups, Dictionary<string, Color> colors)
    {
        var parts = groups.Where(g => (g.Weight ?? 0) > 0).ToList();
        if (parts.Count == 0) return K.Text("No assignment group carries weight yet.", Ty.Caption, B.Text3);
        var bar = new Grid { Height = 14 };
        var caption = new ContentControl { Focusable = false, IsTabStop = false };
        var pieces = new Dictionary<string, FrameworkElement>();
        string? hovered = null;
        Color ColorOf(CGGroup g) => colors.TryGetValue(g.Id, out var c) ? c : Gray;

        FrameworkElement? Caption()
        {
            if (hovered != null && parts.FirstOrDefault(p => p.Id == hovered) is { } g)
            {
                var name = K.Text(g.Name, Ty.Caption).Strong();
                var what = K.Text($"{Share(g.Weight ?? 0)} of grade · {(g.Graded == true ? g.Value ?? "–" : "nothing graded yet")}", Ty.Caption, B.Text2);
                return K.H(6, K.Dot(ColorOf(g), 7), name, what);
            }
            return parts.Any(p => p.Graded != true) ? K.Text("Dashed: nothing graded there yet.", Ty.Caption, B.Text3) : null;
        }

        void Update()
        {
            foreach (var (id, p) in pieces) Motion.To(p, UIElement.OpacityProperty, hovered == null || hovered == id ? 1 : 0.35, Motion.Normal);
            caption.Content = Caption();
        }

        for (var i = 0; i < parts.Count; i++)
        {
            var g = parts[i];
            if (i > 0) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(g.Weight ?? 0, GridUnitType.Star), MinWidth = 4 });
            var color = ColorOf(g);
            Shape piece = g.Graded == true
                ? new Rectangle { RadiusX = 7, RadiusY = 7, Fill = new LinearGradientBrush(K.Mix(color, Colors.White, 0.14), color, 90) }
                : new Rectangle
                {
                    RadiusX = 7, RadiusY = 7, Fill = K.Brush(K.WithAlpha(color, 0.08)),
                    Stroke = K.Brush(K.WithAlpha(color, 0.75)), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4 / 1.5, 3 / 1.5 },
                };
            piece.ToolTip = $"{g.Name} · {Share(g.Weight ?? 0)} of grade{(g.Graded == true ? "" : " · nothing graded yet")}";
            piece.MouseEnter += (_, _) => { hovered = g.Id; Update(); };
            piece.MouseLeave += (_, _) => { if (hovered == g.Id) { hovered = null; Update(); } };
            bar.Children.Add(piece.Col(i * 2));
            pieces[g.Id] = piece;
        }
        caption.Content = Caption();
        var v = K.V(6, bar, caption);
        AutomationProperties.SetName(v, "Grade breakdown: " + string.Join(", ", parts.Select(p => $"{p.Name} {Share(p.Weight ?? 0)}")));
        return v;
    }

    // ---- menus and links ----

    public static MenuItem Item(string text, SymbolRegular icon, Action act)
    {
        var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = icon } };
        mi.Click += (_, _) => act();
        return mi;
    }

    /// <summary>A context menu of these items (MenuItems and Separators), or null when there are none.</summary>
    public static ContextMenu? Menu(IEnumerable<object> items)
    {
        var m = new ContextMenu { MinWidth = 220 };
        foreach (var i in items) m.Items.Add(i);
        return m.Items.Count == 0 ? null : m;
    }

    /// <summary>A menu opened under what was pressed.</summary>
    public static void Open(ContextMenu m, UIElement at)
    {
        m.PlacementTarget = at;
        m.Placement = PlacementMode.Bottom;
        m.IsOpen = true;
    }

    /// <summary>A school address on the clipboard, whole.</summary>
    public static void Copy(Engine.Engine engine, string? link)
    {
        if (engine.Absolute(link) is not { } u) return;
        try { Clipboard.SetText(u.ToString()); } catch { /* the clipboard is busy: nothing copied */ }
    }

    /// <summary>A link's own three: open it here, open the school's own page for it, copy it.</summary>
    public static IEnumerable<object> LinkItems(Engine.Engine engine, string url, string title, string open = "Open")
    {
        yield return Item(open, SymbolRegular.Open24, () => engine.GoTo(url, title));
        if (Shell.PlaceWindow.MenuItem(engine, url, title) is { } apart) yield return apart;
        yield return new Separator();
        yield return Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(url, title));
        yield return Item("Copy link", SymbolRegular.Link24, () => Copy(engine, url));
    }

    /// <summary>The page's More (the Mac's CanvasMenu): Open in Canvas and Copy Link for the page's own address.</summary>
    public static Button More(Engine.Engine engine, Func<string?> url, Func<string> title)
    {
        Button? b = null;
        b = K.IconButton(SymbolRegular.MoreHorizontal24, () =>
        {
            var u = url();
            if (u == null || b == null) return;
            var items = new List<object>();
            // (the page in a window of its own, to keep beside the work: Ctrl+Shift+O)
            if (Shell.PlaceWindow.MenuItem(engine, u, title(), gesture: true) is { } apart) { items.Add(apart); items.Add(new Separator()); }
            items.Add(Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(u, title())));
            items.Add(Item("Copy link", SymbolRegular.Link24, () => Copy(engine, u)));
            var m = Menu(items.ToArray());
            if (m != null) Open(m, b);
        }, "More");
        return b;
    }

    // ---- lists ----

    /// <summary>Sections cut into two columns where they balance best, their order kept: <paramref name="weight"/> is how tall each is likely to be.</summary>
    public static (List<T> Left, List<T> Right) Balanced<T>(List<T> items, Func<T, int> weight)
    {
        if (items.Count <= 1) return (items.ToList(), new List<T>());
        var weights = items.Select(i => Math.Max(weight(i), 1)).ToList();
        var total = weights.Sum();
        int best = 1, bestGap = int.MaxValue, running = 0;
        for (var k = 1; k < items.Count; k++)
        {
            running += weights[k - 1];
            var gap = Math.Abs(total - 2 * running);
            if (gap < bestGap) { bestGap = gap; best = k; }
        }
        return (items.Take(best).ToList(), items.Skip(best).ToList());
    }

    /// <summary>Names as Finder sorts them: numbers by their value ("Quiz 2" before "Quiz 10"), letters regardless of case.</summary>
    public static int Natural(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                var si = i; while (i < a.Length && char.IsDigit(a[i])) i++;
                var sj = j; while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                var c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                var si = i; while (i < a.Length && !char.IsDigit(a[i])) i++;
                var sj = j; while (j < b.Length && !char.IsDigit(b[j])) j++;
                var c = string.Compare(a[si..i], b[sj..j], CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
                if (c != 0) return c;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}

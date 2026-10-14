using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// All Courses (the Mac's CoursesView): every course, as the school's Courses screen has them — the ones in the
/// navigation pane (chosen in the setup) first, then the rest — each a card in its colour with the pane's name for it,
/// its term and teacher (or its other name, only when that says something new), its score in a ring, its unread
/// announcements, how much is handed in and, under a hairline, its sections a click away, each named under its symbol.
/// Current, Past and Future beside the title (Windows 11's SelectorBar, as the web's filter has them) — the line under
/// the title saying what the one chosen shows — with Choose courses. A card's menu opens its sections, gives it a
/// nickname (shown everywhere, Canvas too) or opens it on the school's site.
/// </summary>
public sealed class CoursesScreen : Screen
{
    /// <summary>Every course (allCourses): the current ones — those in the pane first, saying so — and the past and future ones.</summary>
    private sealed class AllCoursesData
    {
        public string? Sub { get; set; }
        /// <summary>(2.99.33) The line under the title for each filter: "Fall 2026" (the term alone when the sections
        /// count the courses, else "Fall 2026 · 8 courses"), "2 past courses", "1 course to come" / "None yet".</summary>
        public FilterLines? Subs { get; set; }
        public List<CourseRow> Current { get; set; } = new();
        public List<CourseRow>? Past { get; set; }
        public List<CourseRow>? Future { get; set; }
    }

    private sealed class FilterLines
    {
        public string? Current { get; set; }
        public string? Past { get; set; }
        public string? Future { get; set; }
    }

    /// <summary>The filter's choices. The first is the current courses — all it ever showed, so it is named for them (its
    /// saved value stays "all").</summary>
    private static readonly string[] Filters = { "Current", "Past", "Future" };

    /// <summary>The sections a card leads to: their names in full (its menu, the tooltips) and short enough for five side
    /// by side under their symbols on the narrowest card — the Mac's words.</summary>
    private static readonly (string Kind, string Label, string Short)[] Links =
    {
        ("grades", "Grades", "Grades"), ("assignments", "Assignments", "Work"), ("modules", "Modules", "Modules"),
        ("files", "Files", "Files"), ("announcements", "Announcements", "News"),
    };

    private readonly Loader<AllCoursesData> data;
    private readonly Loader<Dictionary<string, CourseProgress>> progress;
    private int filter;
    private FrameworkElement? commands;
    private string? shownSub;
    /// <summary>The cards have come in: true from the start (the page comes in as a whole); a new filter brings them in one after another.</summary>
    private bool cardsEntered = true;

    public CoursesScreen(Engine.Engine engine) : base(engine)
    {
        data = Watch(new Loader<AllCoursesData>());
        progress = Watch(new Loader<Dictionary<string, CourseProgress>>());
        // (the screenshot suite: --courses-filter past or future opens on that filter, the choice saved left as it is)
        filter = (Launch.Take("", "courses-filter") ?? Prefs.String("coursesFilter")) switch { "past" => 1, "future" => 2, _ => 0 };
    }

    public override string Title => "Courses";
    /// <summary>The page's line for what the filter shows — "2 past courses" under Past, never the current term's count —
    /// in Windows' words ("navigation pane", where the Mac's says sidebar).</summary>
    public override string? Subtitle
    {
        get
        {
            if (data.Data is not { } d) return null;
            var line = filter switch { 1 => d.Subs?.Past, 2 => d.Subs?.Future, _ => d.Subs?.Current ?? d.Sub };
            return line?.Replace("sidebar", "navigation pane");
        }
    }

    /// <summary>Current · Past · Future (the Mac's segmented picker) and Choose courses, beside the title.</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (commands != null) return commands;
            var selector = new SelectorBar(Filters, filter) { ToolTip = "Current courses, past ones, or ones to come" };
            selector.Changed += v =>
            {
                filter = v;
                Prefs.Set("coursesFilter", v switch { 1 => "past", 2 => "future", _ => "all" });
                cardsEntered = false;
                Render();
                // (the line under the title follows the filter)
                SubtitleChanged();
            };
            // (a symbol of its own — the pane it fills —, not the checklist To Do and the quizzes wear)
            var choose = K.Button("Choose courses", engine.AskSetup, icon: SymbolRegular.PanelLeft24);
            choose.ToolTip = "Choose the courses in the navigation pane";
            return commands = K.H(12, selector, choose);
        }
    }

    /// <summary>The header drawn again when the line under the title says something else (a new answer, another filter).</summary>
    private void SubtitleChanged()
    {
        var now = Subtitle;
        if (now == shownSub) return;
        shownSub = now;
        HeaderChange();
    }

    public override void Refresh()
    {
        data.Ensure(engine, "allCourses");
        progress.Ensure(engine, "coursesProgress", new { all = true });
    }

    private static int Cols(double w) => Math.Max(1, (int)Math.Floor((Math.Max(300, w) + 18) / (290 + 18)));

    protected override bool LayoutChanges(double before, double now) => Cols(before) != Cols(now);

    protected override FrameworkElement Build()
    {
        if (data.Data is not { } d) return K.LoadState(data.Error, () => data.Reload(engine, "allCourses"));
        SubtitleChanged();
        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 28 };
        switch (filter)
        {
            case 1:
                if (d.Past is not { Count: > 0 }) page.Children.Add(K.Empty(SymbolRegular.History24, "No past courses."));
                else page.Children.Add(CardGrid(d.Past));
                break;
            case 2:
                if (d.Future is not { Count: > 0 }) page.Children.Add(K.Empty(SymbolRegular.CalendarClock24, "No courses to come."));
                else page.Children.Add(CardGrid(d.Future));
                break;
            default:
            {
                var chosen = d.Current.Where(c => c.Chosen != false).ToList();
                var others = d.Current.Where(c => c.Chosen == false).ToList();
                if (d.Current.Count == 0) page.Children.Add(K.Empty(SymbolRegular.Library24, "No current courses."));
                else if (others.Count == 0 || chosen.Count == 0) page.Children.Add(CardGrid(d.Current));
                else
                {
                    page.Children.Add(K.V(0, Heading.Section("In the navigation pane", chosen.Count.ToString(CultureInfo.CurrentCulture)), CardGrid(chosen)));
                    var note = K.Wrapped("Courses you left out in the setup. Choose courses puts them in the navigation pane.", Ty.Body, B.Text2);
                    page.Children.Add(K.V(0, Heading.Section("Not in the navigation pane", others.Count.ToString(CultureInfo.CurrentCulture)), K.V(12, note, CardGrid(others))));
                }
                break;
            }
        }
        return page;
    }

    // ---- the cards ----

    /// <summary>The cards in as many columns as fit at 290 wide (the Mac's adaptive grid), a row's cards as tall as each other.</summary>
    private FrameworkElement CardGrid(List<CourseRow> rows)
    {
        const double gap = 18;
        var cols = Cols(Width);
        var grid = new Grid();
        for (var i = 0; i < cols; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var n = (rows.Count + cols - 1) / cols;
        for (var r = 0; r < n; r++)
        {
            if (r > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(gap) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        var p = progress.Data;
        for (var k = 0; k < rows.Count; k++)
        {
            var c = rows[k];
            var card = Card(c, p != null && p.TryGetValue(c.Id, out var pr) ? pr : null);
            Grid.SetColumn(card, (k % cols) * 2);
            Grid.SetRow(card, (k / cols) * 2);
            grid.Children.Add(card);
        }
        // the cards come in one after another when the filter changes (with the page, they come in as it does)
        if (!cardsEntered)
        {
            cardsEntered = true;
            Motion.Stagger(grid.Children.OfType<UIElement>(), 40, 20);
        }
        return grid;
    }

    /// <summary>A course's card (the Mac's CourseCard): its colour with the pane's name for it and its unread
    /// announcements; its term and teacher — or its other name, only when that says something new ("MATH-021-20" under
    /// "F26-MATH 021 20" said the course twice) —, how much is handed in (a bar) and its score (a ring) — a click opens
    /// the course — and under a hairline its sections, each a click away with its name under its symbol.</summary>
    private FrameworkElement Card(CourseRow c, CourseProgress? p)
    {
        var color = K.Hex(c.Color);
        var ctx = $"courses/{c.Id}";

        // its colour, its name on it — in white only where white reads 3:1 on the colour, else near-black (students
        // pick their colours in Canvas, and many are light) —, its unread announcements at the corner
        var ink = K.InkOn(color);
        var code = new TextBlock
        {
            Text = c.Code, FontSize = 20, FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace, Foreground = K.Brush(ink),
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 54, LineHeight = 26,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(16, 14, 64, 12), VerticalAlignment = VerticalAlignment.Top,
            Effect = ink == Colors.White ? K.TextShadow() : null,
        };
        var banner = new Grid
        {
            Height = 92,
            Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(color, 0), new GradientStop(K.WithAlpha(color, 0.78), 1) }, new Point(0, 0), new Point(1, 1)),
        };
        banner.Children.Add(code);
        if (c.Unread is int n && n > 0)
        {
            // (on the white pill, a light colour taken deeper so its count reads)
            var onWhite = ink == Colors.White ? color : K.Mix(color, Colors.Black, 0.35);
            var count = new TextBlock { Text = n.ToString(CultureInfo.CurrentCulture), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = K.Brush(onWhite), VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 3, 9, 4),
                Child = K.H(5, K.Icon(SymbolRegular.Megaphone24, 12, onWhite, filled: true), count),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 13, 13, 0),
                ToolTip = UnreadWords(c, n),
            };
            System.Windows.Automation.AutomationProperties.SetName(pill, UnreadWords(c, n));
            banner.Children.Add(pill);
        }

        // its other name when that says something new (a nickname's real name), else its term and teacher; how much is
        // handed in; its score in a ring
        var words = K.V(5);
        if (SecondLine(c) is { Length: > 0 } second)
        {
            var line = K.Wrapped(second, Ty.Body, B.Text, 2);
            line.FontWeight = FontWeights.Medium;
            words.Children.Add(line);
        }
        if (p is { Total: > 0 })
        {
            words.Children.Add(K.Text($"{p.Done} of {p.Total} handed in", Ty.Caption, B.Text2));
            words.Children.Add(HandedIn(p, color));
        }
        words.VerticalAlignment = VerticalAlignment.Center;
        // ("–" with no score, and why in the ring's tooltip — "No grade yet", "Hidden by instructor" — on a track in the
        //  course's own colour, as the Mac's ring has it)
        var score = K.Text(c.ScoreText.Length > 0 ? c.ScoreText : "–", Ty.Caption, c.Score == null ? B.Text2 : B.Text).Strong().Center();
        score.VerticalAlignment = VerticalAlignment.Center;
        var ring = K.Ring(c.Score is { } s ? s / 100 : null, color, 56, 5, score, tinted: true);
        if (c.Score == null && c.ScoreWhy is { Length: > 0 } why)
        {
            ring.ToolTip = why;
            if (ring is Panel face) face.Background = Brushes.Transparent; // (the whole ring answers the pointer, not only its stroke)
        }
        var facts = K.Columns("*, auto", words.Margin(0, 0, 14, 0).Col(0), ring.Col(1));
        facts.Margin = new Thickness(16, 14, 16, 14);

        // its sections, each a click away, each named under its symbol (none to be learnt from a tooltip)
        var links = new UniformGrid { Rows = 1, Columns = Links.Length, Margin = new Thickness(8, 6, 8, 6) };
        foreach (var (kind, label, shortName) in Links)
        {
            var k = kind;
            var caption = K.Text(shortName, Ty.Caption, B.Text2).Center();
            caption.FontSize = 11;
            var b = K.Subtle(K.V(3, K.Icon(Glyph.Section(kind), 16, K.Darkened(color)), caption), () => engine.Go(Place.Section(ctx, k)), new Thickness(0, 6, 0, 5), 4);
            b.MinHeight = 46;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(b, $"{c.Code} {label}");
            links.Children.Add(b);
        }

        var content = K.Rows("auto, auto, *, auto, auto", banner.Row(0), facts.Row(1), K.Divider().Margin(14, 0, 14, 0).Row(3), links.Row(4));
        var card = K.CardButton(content, () => engine.OpenWeb(c.Url, c.Code), 8, new Thickness(0));
        Ahead.On(card, build => engine.PrepareWeb(c.Url, build));
        card.VerticalAlignment = VerticalAlignment.Stretch;
        // (the banner's corners follow the card's)
        content.SizeChanged += (_, e) => content.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        // (its name, and its other name when that says something new)
        card.ToolTip = c.AltName is { Length: > 0 } alt ? $"{c.Code}\n{alt}" : c.Code;
        System.Windows.Automation.AutomationProperties.SetName(card, $"{c.Code}, {(c.Score == null && c.ScoreWhy is { Length: > 0 } w ? w : c.ScoreText)}");
        CourseKit.Menu(card, items =>
        {
            items.Add(CourseKit.MenuEntry("Open", SymbolRegular.DocumentArrowRight24, () => engine.OpenWeb(c.Url, c.Code)));
            items.Add(new Separator());
            foreach (var (kind, label, _) in Links)
            {
                var k = kind;
                items.Add(CourseKit.MenuEntry(label, Glyph.Section(kind), () => engine.Go(Place.Section(ctx, k))));
            }
            items.Add(new Separator());
            items.Add(CourseKit.MenuEntry("Nickname…", SymbolRegular.Rename24, () => Rename(c)));
            items.Add(CourseKit.MenuEntry($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(c.Url, c.Code)));
            items.Add(CourseKit.MenuEntry("Copy link", SymbolRegular.Link24, () => CourseKit.CopyLink(engine, c.Url)));
        });
        return card;
    }

    /// <summary>The line under a card's band: the course's other name when the engine says it differs (a nickname's real
    /// name), else its term and first teacher ("Fall 2026 · Yue Lei"); an answer from before 2.99.33 has neither, and a
    /// name that says nothing new beside the band is left out then too.</summary>
    private static string SecondLine(CourseRow c)
    {
        if (c.AltName is { Length: > 0 } alt) return alt;
        if (c.Detail is { Length: > 0 } detail) return detail;
        if (c.AltName == null && c.Detail == null && CourseKit.SaysMore(c.Name, c.Code)) return c.Name!;
        return "";
    }

    /// <summary>The unread announcements in words ("2 unread announcements"), for the badge's tooltip and a screen reader.</summary>
    private static string UnreadWords(CourseRow c, int n) =>
        c.UnreadLabel is { Length: > 0 } l ? l : $"{n} unread {(n == 1 ? "announcement" : "announcements")}";

    /// <summary>How much is handed in, as a bar in the course's colour on a wash of it.</summary>
    private static FrameworkElement HandedIn(CourseProgress p, Color color)
    {
        var share = Math.Clamp((double)p.Done / Math.Max(p.Total, 1), 0, 1);
        var track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = K.Brush(K.WithAlpha(color, 0.16)) };
        var fill = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = K.Brush(color), HorizontalAlignment = HorizontalAlignment.Left };
        var bar = new Grid { Height = 5, Children = { track, fill } };
        bar.SizeChanged += (_, e) => fill.Width = share > 0 ? Math.Max(5, e.NewSize.Width * share) : 0;
        return CourseKit.Capped(bar, 200).Margin(0, 2, 0, 0);
    }

    // ---- the nickname ----

    /// <summary>A course's nickname (the Mac's NicknameSheet): shown instead of its name everywhere, Canvas included;
    /// empty, the name comes back.</summary>
    private void Rename(CourseRow c)
    {
        var box = new TextBox { PlaceholderText = string.IsNullOrEmpty(c.Original) ? "Nickname" : c.Original, Text = c.Nickname ?? "", MaxLength = 80 };
        var where = engine.OnBrightspace ? "on this PC only" : "in Canvas too";
        var note = K.Wrapped($"Shown instead of “{(string.IsNullOrEmpty(c.Original) ? c.Code : c.Original)}” everywhere, {where}.", Ty.Body, B.Text2);
        var s = new Sheet { Title = "Nickname", Body = K.V(14, note, box), Width = 440 };
        s.Add("Save", SheetButtonKind.Primary, () => Save(c, box.Text));
        if (!string.IsNullOrEmpty(c.Nickname)) s.Add("Remove nickname", SheetButtonKind.Secondary, () => Save(c, ""));
        s.Add("Cancel", SheetButtonKind.Close);
        Dialogs.Show(s, Window.GetWindow(View));
    }

    private async void Save(CourseRow c, string name)
    {
        if (!await engine.Act("setNickname", new { id = c.Id, name = name.Trim() })) return;
        try { await engine.Call<OK>("refresh"); } catch { /* read again as it is */ }
        data.Reload(engine, "allCourses");
        progress.Reload(engine, "coursesProgress", new { all = true });
        await engine.LoadSidebar();
    }
}

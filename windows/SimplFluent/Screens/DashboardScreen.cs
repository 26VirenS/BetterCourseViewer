using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// The Dashboard (the Mac's DashboardView), in Windows 11's cards: the greeting as the page's title and the date under
/// it; six counters, each growing in place into a panel of what it counted (the panel rises out of its card and folds
/// back into it); then one of three views, chosen with the SelectorBar beside the title and kept — Cards (the courses as
/// cards), List (everything coming up, day by day, ticked off where it is done) and Activity (the school's recent
/// activity). Beside the view on a wide page, under it on a narrow one: the day's work, the grades skyline and the
/// week's load per course.
/// </summary>
public sealed partial class DashboardScreen : Screen
{
    private readonly Loader<Today> today;
    private readonly Loader<TodayCounts> countsQuick, countsFresh;
    private readonly Loader<DashCoursesData> courses;
    private readonly Loader<Dictionary<string, CourseProgress>> progress;
    private readonly Loader<DashSkylineData> skyKept, skyFresh;
    private readonly Loader<DashListData> list;
    private readonly Loader<DashActivityData> activity;
    private readonly Loader<ItemsSheetData> sheet = new();
    private readonly Dictionary<string, bool> ticked = new();
    private readonly HashSet<string> seen = new();
    private static readonly string[] Views = { "Cards", "List", "Activity" };
    private int view;
    private bool? hideDone;
    private int overdueAdjust;
    private SelectorBar? selector;
    private FrameworkElement? commands;
    private bool skyRisen;

    public DashboardScreen(Engine.Engine engine) : base(engine)
    {
        today = Watch(new Loader<Today>());
        countsQuick = Watch(new Loader<TodayCounts>());
        countsFresh = Watch(new Loader<TodayCounts>());
        courses = Watch(new Loader<DashCoursesData>());
        progress = Watch(new Loader<Dictionary<string, CourseProgress>>());
        skyKept = Watch(new Loader<DashSkylineData>());
        skyFresh = Watch(new Loader<DashSkylineData>());
        list = Watch(new Loader<DashListData>());
        activity = Watch(new Loader<DashActivityData>());
        sheet.Changed += PanelData;
        view = Launch.Get("dash-view") switch { "cards" => 0, "activity" => 2, "list" => 1, _ => (int)Prefs.Double("dashView", 1) };
        openKey = Launch.Get("sheet") is { Length: > 0 } k ? k : null;
        sheetAsked = openKey != null;
    }

    private string[] ViewsHere => engine.OnBrightspace ? Views[..2] : Views;
    private int View => Math.Min(view, ViewsHere.Length - 1);

    public override string Title => today.Data is { } d ? Greeting(d) : "Dashboard";

    /// <summary>The date, and — while the counts are still the ones kept from last time — that they are being brought up
    /// to date (the Mac's "Updating…"): the numbers stay, dimmed, and the live ones take their places as they come.</summary>
    public override string? Subtitle => today.Data?.DateLine is { } line ? (Updating ? $"{line} · Updating…" : line) : null;

    /// <summary>The counts shown are the ones kept from last time: Today's answer not in yet, or the counts made on their
    /// own (Overdue, Graded) only as kept.</summary>
    private bool Updating => today.Data != null && (today.Kept || countsFresh.Data == null);

    /// <summary>Cards · List · Activity (Windows 11's SelectorBar) and New task, beside the title.</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (commands != null) return commands;
            selector = new SelectorBar(ViewsHere, View);
            selector.Changed += v =>
            {
                if (v == 0 && view != 0) courseCardsShown = false;
                view = v;
                Prefs.Set("dashView", v);
                Refresh();
                Render();
            };
            var add = K.Button("New task", () => engine.AskNewTask(), icon: SymbolRegular.Add24);
            add.ToolTip = "New task (Ctrl+N)";
            return commands = K.H(12, selector, add);
        }
    }

    public override void Refresh()
    {
        today.Ensure(engine, "today");
        countsQuick.Ensure(engine, "todayCounts", new { kept = true });
        countsFresh.Ensure(engine, "todayCounts", new { kept = false });
        courses.Ensure(engine, "dashCourses");
        progress.Ensure(engine, "coursesProgress");
        skyKept.Ensure(engine, "dashSkyline", new { kept = true });
        skyFresh.Ensure(engine, "dashSkyline", new { kept = false });
        if (View == 1) list.Ensure(engine, "dashList", hideDone is { } hd ? new { hideDone = hd } : null);
        if (View == 2) activity.Ensure(engine, "dashActivity");
        if (openKey != null) sheet.Ensure(engine, "todaySheet", new { key = openKey });
    }

    private static string Greeting(Today d)
    {
        var first = (d.Me?.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var hour = DateTime.Now.Hour;
        var part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        return first.Length == 0 ? part : $"{part}, {first}";
    }

    // ---- the page ----

    private Grid? root;
    private Canvas? overlay;
    private readonly Dictionary<string, Button> tiles = new();
    private string? lastTitle, lastSubtitle;

    protected override bool LayoutChanges(double before, double now) =>
        CounterLayout(before, WindowWidth) != CounterLayout(now, WindowWidth) || (before >= 900) != (now >= 900) || (before >= 760) != (now >= 760) || CardCols(before) != CardCols(now)
        || Math.Abs(RailWidth(before) - RailWidth(now)) >= 24;

    /// <summary>The widest the view beside the rail grows: a row's title and its due time stay within a glance of each
    /// other on a 1080p screen (the rest of the page goes to the rail, then to room at the right).</summary>
    private const double MainMost = 820;

    /// <summary>The rail beside the view on a page 900 wide or wider: about a third of it, more where the view has reached
    /// its widest, never under 340 nor over 720.</summary>
    private static double RailWidth(double page) => Math.Clamp(Math.Max(Math.Round(page * 0.3), Math.Round(page - 28 - MainMost)), 340, 720);

    protected override bool WindowLayoutChanges(double before, double now) => CounterLayout(Width, before) != CounterLayout(Width, now);

    protected override FrameworkElement Build()
    {
        var d = today.Data;
        if (Title != lastTitle || Subtitle != lastSubtitle) { lastTitle = Title; lastSubtitle = Subtitle; HeaderChange(); }
        if (d == null) return K.LoadState(today.Error, () => today.Reload(engine, "today"));
        var width = Math.Max(320, Width);
        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 28 };
        var look = (Appearance.Dark, Appearance.Accent, Appearance.ThemeName);
        var layout = CounterLayout(width, WindowWidth);
        page.Children.Add(Part("counters", new object?[] { d, countsQuick.Data, countsFresh.Data, today.Kept, overdueAdjust, openKey, layout, look }, () => Counters(d, layout)));
        var two = width >= 900;
        var railW = RailWidth(width);
        var mainW = two ? Math.Min(MainMost, width - railW - 28) : width;
        // (the view: what it shows, at its width; the cards are the courses' and their progress, the list the planner's,
        //  the activity the school's — not today's, so a new count leaves them as they are)
        var main = Part("main", new object?[] { View, Math.Round(mainW), courses.Data, progress.Data, courses.Error, list.Data, list.Error, activity.Data, activity.Error, ticks, seenTurn, courseCardsShown, look }, () => Main(d, mainW));
        if (two)
        {
            var rail = Part("rail", new object?[] { d, skyFresh.Data, skyKept.Data, View, railW, false, ticks, look }, () => Rail(d, railW, false));
            var g = K.Columns($"*, 28, {railW.ToString(System.Globalization.CultureInfo.InvariantCulture)}", main.Col(0), rail.Col(2));
            // (on a page wider still, what is over stays as room at the right, not between a row's title and its time)
            g.ColumnDefinitions[0].MaxWidth = MainMost;
            page.Children.Add(g);
        }
        else
        {
            // (one column: the view chosen right under the counters — Activity or Cards shows at once — then the day's work)
            page.Children.Add(main);
            if (View != 1) page.Children.Add(Part("today", new object?[] { d, ticks, look }, () => TodaySection(d, true)));
            page.Children.Add(Part("rail", new object?[] { d, skyFresh.Data, skyKept.Data, View, Math.Round(width), true, ticks, look }, () => Rail(d, width, true)));
        }
        root = new Grid();
        overlay = new Canvas { ClipToBounds = false };
        root.Children.Add(page);
        root.Children.Add(overlay);
        if (openKey != null) root.Loaded += (_, _) => ShowPanel(animate: !sheetAsked);
        return root;
    }

    // ---- parts kept from build to build ----

    /// <summary>Each part of the page as it was built last, with what it was built from.</summary>
    private readonly Dictionary<string, (object?[] Inputs, FrameworkElement Part)> parts = new();
    /// <summary>Bumped as a tick is shown, or an activity seen: the parts that draw them are built again.</summary>
    private int ticks, seenTurn;

    /// <summary>
    /// A part of the page (the counters, the view, the rail): the one built last time when what it shows has not changed —
    /// the same answers, the same width and view, the same ticks and look — taken from the page before and put in this
    /// one. A new count leaves the courses' cards exactly as they were (not built again, not redrawn), so the Dashboard's
    /// answers read again in the background change only what they change, in place.
    /// </summary>
    private FrameworkElement Part(string key, object?[] inputs, Func<FrameworkElement> make)
    {
        if (parts.TryGetValue(key, out var was) && was.Inputs.Length == inputs.Length && was.Inputs.Zip(inputs).All(p => Equals(p.First, p.Second)))
        {
            switch (was.Part.Parent)
            {
                case Panel panel: panel.Children.Remove(was.Part); break;
                case Decorator dec: dec.Child = null; break;
                case ContentControl cc: cc.Content = null; break;
            }
            return was.Part;
        }
        var e = make();
        parts[key] = (inputs, e);
        return e;
    }

    // ---- the counters (the Mac's DashCounterTile, as Windows 11 cards) ----

    private static readonly string[] Order = { "today", "tomorrow", "next", "overdue", "unread", "graded" };

    /// <summary>Each counter's colour. Red means late, and only Overdue wears it: Due today is Windows' blue, whatever the
    /// theme's accent (a theme's pink or red would read as late too).</summary>
    private static Color CounterColor(string key) => key switch
    {
        "today" => Color.FromRgb(0x00, 0x78, 0xd4), "next" => Color.FromRgb(0x2f, 0xa8, 0x4f), "unread" => Color.FromRgb(0xf7, 0x8c, 0x1a),
        "overdue" => Color.FromRgb(0xe8, 0x4a, 0x3f), "tomorrow" => Color.FromRgb(0xf2, 0xa1, 0x00), "graded" => Color.FromRgb(0x6b, 0x5c, 0xe0),
        _ => Appearance.Accent,
    };

    /// <summary>What a counter counts, in full: its panel's title until the panel's own comes.</summary>
    private static string FullName(string key, string label) => key switch
    {
        "today" => "Due today", "next" => "Next 7 days", "unread" => "Announcements", "overdue" => "Overdue",
        "tomorrow" => "Due tomorrow", "graded" => "Graded this week", _ => label,
    };

    /// <summary>A counter as its tooltip and a screen reader say it, the number in its words: "3 unread announcements",
    /// "2 graded this week", "4 due today".</summary>
    private static string Spoken(string key, string label, int? value)
    {
        if (value is not { } n) return $"{FullName(key, label)}: counting";
        return key switch
        {
            "today" => $"{n} due today",
            "tomorrow" => $"{n} due tomorrow",
            "next" => $"{n} due in the next 7 days",
            "overdue" => $"{n} overdue",
            "unread" => $"{n} unread {(n == 1 ? "announcement" : "announcements")}",
            "graded" => $"{n} graded this week",
            _ => $"{FullName(key, label)}: {n}",
        };
    }

    /// <summary>A window this wide or wider shows the six counters in one row, however wide the page beside the
    /// navigation pane is (1024 is the pictures' usual window, the page beside the pane 640 of it).</summary>
    private const double OneRowFrom = 1024;

    /// <summary>A one-row tile's padding at its sides, its symbol's size and the room between the symbol and its words.</summary>
    private const double RowPad = 12, RowSymbol = 22, RowGap = 8;

    /// <summary>
    /// How the counters are laid out for a page and a window this wide: on a window 1024 wide or wider, one row of six —
    /// told by the window, so a page that narrows as the pane or the rail takes room never puts them back in two rows —
    /// and whether each of those tiles has room for its symbol beside its name (else the symbol goes beside its number);
    /// on a narrower window, three to a row, or two on the narrowest page. And which names the row wears, decided once for
    /// all six: the full ones ("Announcements", "Graded this week") where every one of them fits as its tile draws it,
    /// else the short ones every tile fits ("Unread", "Graded"), whose lines then say the rest — never a name cut.
    /// </summary>
    private (int Cols, bool Roomy, bool Full) CounterLayout(double page, double window)
    {
        if (window < OneRowFrom)
        {
            var cols = page >= 520 ? 3 : 2;
            // (a tall tile: its symbol, 28, and 10 after it beside its name, inside 14 at each side and its edge)
            var tile = (Math.Max(320, page) - (cols - 1) * 12) / cols - 2;
            return (cols, false, tile - 2 * 14 - 28 - 10 >= WidestLabel(full: true) + 2);
        }
        var words = (Math.Max(320, page) - 5 * 12) / 6 - 2 - 2 * RowPad;
        var roomy = words >= RowSymbol + RowGap + WidestLabel(full: false) + 2;
        var room = roomy ? words - RowSymbol - RowGap : words;
        return (6, roomy, room >= WidestLabel(full: true) + 2);
    }

    /// <summary>A counter's name in the wording its row wears.</summary>
    private static string NameOf(Counter c, bool full) => full && !string.IsNullOrEmpty(c.FullLabel) ? c.FullLabel! : c.Label;

    /// <summary>The widest of the counters' names in the tiles' type (the short ones, or the full), measured once for each
    /// set of names.</summary>
    private double WidestLabel(bool full)
    {
        var names = today.Data?.Counters.Select(c => NameOf(c, full)).ToList() ?? new List<string>();
        var key = string.Join("\u001f", names);
        if (!widest.TryGetValue(full, out var was) || was.Labels != key)
            widest[full] = was = (key, names.Select(n => WordsWidth(n, Ty.Body)).DefaultIfEmpty(0).Max());
        return was.Width;
    }

    private readonly Dictionary<bool, (string Labels, double Width)> widest = new();

    /// <summary>How wide a line of words is in a step of Windows' type ramp, as a TextBlock lays it out.</summary>
    private static double WordsWidth(string s, Ty ty)
    {
        var (size, weight, face, _) = K.Ramp(ty);
        return new FormattedText(s, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(face, FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
    }

    private FrameworkElement Counters(Today d, (int Cols, bool Roomy, bool Full) layout)
    {
        var cols = layout.Cols;
        var items = d.Counters.OrderBy(c => Array.IndexOf(Order, c.Key) is var i && i < 0 ? 99 : i).ToList();
        var grid = new Grid();
        for (var c = 0; c < cols; c++)
        {
            if (c > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var rows = (items.Count + cols - 1) / cols;
        for (var r = 0; r < rows; r++)
        {
            if (r > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        tiles.Clear();
        for (var k = 0; k < items.Count; k++)
        {
            var c = items[k];
            var tile = cols == 6 ? RowTile(c, layout.Roomy, layout.Full) : Tile(c, layout.Full);
            Grid.SetColumn(tile, (k % cols) * 2);
            Grid.SetRow(tile, (k / cols) * 2);
            grid.Children.Add(tile);
            tiles[c.Key] = tile;
            if (c.Key == "next") Shell.Tour.Mark(tile, Shell.TourSpot.CounterNext);
            if (openKey == c.Key) tile.Opacity = 0;
        }
        Shell.Tour.Mark(grid, Shell.TourSpot.Counters);
        return grid;
    }

    /// <summary>A counter's number as its tile draws it: "–" while it is counted, red for the alert, dimmed while it is
    /// the one kept from last time (the live one takes its place as it comes).</summary>
    private static TextBlock Number((int? Value, string? Note, bool Kept) n, bool alert)
    {
        var count = K.Text(n.Value?.ToString() ?? "–", Ty.Count, n.Value == null ? B.Text3 : alert ? B.Critical : B.Text);
        if (n.Value != null && n.Kept) count.Opacity = 0.5;
        return count;
    }

    /// <summary>A counter's tile three to a row (two on the narrowest page): its symbol and name, its number, the line under it.</summary>
    private Button Tile(Counter c, bool full)
    {
        var n = Count(c, full);
        var alert = c.Tone == "red" && (n.Value ?? 0) > 0;
        var color = CounterColor(c.Key);
        var label = K.Text(NameOf(c, full), Ty.Body, B.Text2);
        var head = K.Columns("auto, *", K.IconTile(Glyph.Counter(c.Key), color, 28).Col(0), label.Margin(10, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1));
        var count = Number(n, alert);
        count.Margin = new Thickness(0, 10, 0, 0);
        var noteT = K.Text(n.Note ?? " ", Ty.Caption, B.Text2);
        var content = K.V(0, head, count, noteT);
        content.Margin = new Thickness(14, 14, 14, 12);
        return TileButton(c, content, n.Value, alert, color);
    }

    /// <summary>
    /// A counter's tile in one row of six, made for it — two lines, about 72 tall, so the work under the counters stays
    /// in view: its name (never cut short) over its number, the symbol beside the name where both fit; where they do
    /// not (the narrowest tiles, on a window not much over 1024), beside the number instead. The line about the count
    /// follows the number, cut short at its end, and is left out where too little of it would show.
    /// </summary>
    private Button RowTile(Counter c, bool roomy, bool full)
    {
        var n = Count(c, full);
        var alert = c.Tone == "red" && (n.Value ?? 0) > 0;
        var color = CounterColor(c.Key);
        var symbol = K.IconTile(Glyph.Counter(c.Key), color, RowSymbol);
        var label = K.Text(NameOf(c, full), Ty.Body, B.Text2);
        label.TextTrimming = TextTrimming.None;
        // (a name wider than its room, were there ever one, shrunk to fit rather than cut; a number likewise)
        var name = new Viewbox { Child = label, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var head = roomy ? K.Columns("auto, *", symbol.Col(0), name.Margin(RowGap, 0, 0, 0).Col(1)) : K.Columns("*", name);
        head.Height = RowSymbol;
        var count = Number(n, alert);
        count.FontSize = 28;
        count.LineHeight = 32;
        count.TextTrimming = TextTrimming.None;
        Grid line;
        if (roomy)
        {
            line = K.Columns("auto, *", count.Col(0));
            if (!string.IsNullOrEmpty(n.Note))
            {
                var noteT = K.Text(n.Note!, Ty.Caption, B.Text2).Margin(RowGap, 0, 0, 5).Align(HorizontalAlignment.Stretch, VerticalAlignment.Bottom).Col(1);
                // (shown when all of it fits, or enough to say something — about nine letters)
                var enough = Math.Min(WordsWidth(n.Note!, Ty.Caption), 56);
                noteT.SizeChanged += (_, e) => noteT.Visibility = e.NewSize.Width + 0.5 >= enough ? Visibility.Visible : Visibility.Hidden;
                line.Children.Add(noteT);
            }
        }
        else
        {
            // (no line about the count: beside the symbol and the number, too little of it would ever show)
            var number = new Viewbox { Child = count, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(RowGap, 0, 0, 0) };
            line = K.Columns("auto, *", symbol.Col(0), number.Col(1));
        }
        line.Height = 32;
        var content = K.V(0, head, line);
        content.Margin = new Thickness(RowPad, 8, RowPad, 8);
        return TileButton(c, content, n.Value, alert, color);
    }

    /// <summary>A counter's tile round its words: its colour washed across it (or its theme's picture), opening its panel
    /// when pressed, what it counts with its number its tooltip and what a screen reader says. Overdue with something in
    /// it wears the alert on its own face — a solid red tint ringed in red, over any wash, picture or theme — so it never
    /// looks like a tile with nothing in it.</summary>
    private Button TileButton(Counter c, FrameworkElement content, int? value, bool alert, Color color)
    {
        // (its theme's scene, or a photo of the student's, under its words — Settings ▸ Appearance)
        var face = new Grid();
        if (Scenes.Backdrop(c.Key) is { } scene) face.Children.Add(scene);
        else if (!alert) face.Children.Add(Wash(color));
        if (alert) face.Children.Add(AlertFace());
        face.Children.Add(content);
        face.SizeChanged += (_, e) => face.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        var b = K.CardButton(face, () => SetOpen(openKey == c.Key ? null : c.Key), 8, new Thickness(0));
        var spoken = Spoken(c.Key, c.Label, value);
        b.ToolTip = spoken;
        System.Windows.Automation.AutomationProperties.SetName(b, spoken);
        return b;
    }

    /// <summary>The alert's face: Windows' critical background, solid, ringed in its critical red.</summary>
    private static Border AlertFace()
    {
        var f = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1.5), IsHitTestVisible = false };
        f.SetResourceReference(Border.BackgroundProperty, B.CriticalBack);
        f.SetResourceReference(Border.BorderBrushProperty, B.Critical);
        return f;
    }

    /// <summary>A counter's colour washed across its card (Windows 11's tinted cards): strongest at its symbol's corner,
    /// fading towards the far one — the panel it grows into wears the same.</summary>
    private static Border Wash(Color c) => new()
    {
        Background = new LinearGradientBrush(K.WithAlpha(c, 0.24), K.WithAlpha(c, 0.05), new Point(0, 0), new Point(1, 1)),
        CornerRadius = new CornerRadius(7), IsHitTestVisible = false,
    };

    /// <summary>
    /// A counter's number, the line that goes with the name its row wears (the short name's, or the full one's), and
    /// whether the number is the one kept from last time — Overdue and Graded from the counts made on their own (the
    /// kept ones until the live ones come), the rest from Today.
    /// </summary>
    private (int? Value, string? Note, bool Kept) Count(Counter c, bool full = false)
    {
        var fresh = countsFresh.Data;
        var quick = countsQuick.Data;
        if (c.Key == "overdue")
        {
            var k = fresh ?? quick;
            if (k?.Overdue is { } o)
            {
                var n = Math.Max(0, o + overdueAdjust);
                return (n, overdueAdjust != 0 ? (n > 0 ? $"{n} not submitted" : "Nothing overdue") : k.OverdueNote, fresh == null);
            }
            return (null, null, false);
        }
        if (c.Key == "graded")
        {
            var k = fresh ?? quick;
            var note = full && !string.IsNullOrEmpty(k?.GradedFullNote) ? k!.GradedFullNote : k?.GradedNote;
            return k?.Graded is { } g ? (g, note, fresh == null) : (null, null, false);
        }
        var line = full && !string.IsNullOrEmpty(c.FullNote) ? c.FullNote : c.Note;
        if (today.Data == null || c.Pending == true) return (null, line, false);
        return (c.Value, line, today.Kept);
    }

    // ---- the panel a counter grows into (the Mac's DashCounterPanel: 440 wide; here a Windows 11 flyout surface) ----

    private string? openKey;
    private bool sheetAsked;
    private Border? frame;
    private FrameworkElement? panelContent;
    private Rect panelRect;

    private void SetOpen(string? key)
    {
        if (key == openKey) return;
        if (openKey != null) ClosePanel(openKey);
        openKey = key;
        if (key != null)
        {
            Shell.Tour.Did(Shell.TourEvent.CounterOpened);
            sheet.Ensure(engine, "todaySheet", new { key });
            ShowPanel(animate: true);
        }
    }

    public override bool Escape()
    {
        if (openKey == null) return false;
        SetOpen(null);
        return true;
    }

    /// <summary>A press anywhere outside the open panel — the page round it, the pane, the header, the title bar — folds it
    /// back (the press still does what it does).</summary>
    public override void Pressed(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (openKey == null || frame == null) return;
        var p = e.GetPosition(frame);
        if (p.X >= 0 && p.Y >= 0 && p.X <= frame.ActualWidth && p.Y <= frame.ActualHeight) return;
        if (tiles.TryGetValue(openKey, out var t) && t.IsMouseOver) return; // (its own tile closes it by its click)
        SetOpen(null);
    }

    private Rect TileRect(string key)
    {
        if (root == null || !tiles.TryGetValue(key, out var t) || !t.IsLoaded) return Rect.Empty;
        var p = t.TranslatePoint(new Point(0, 0), root);
        return new Rect(p, new Size(t.ActualWidth, t.ActualHeight));
    }

    private void ShowPanel(bool animate)
    {
        if (openKey == null || root == null || overlay == null) return;
        var key = openKey;
        var tile = TileRect(key);
        if (tile.IsEmpty) return;
        var d = today.Data;
        var c = d?.Counters.FirstOrDefault(k => k.Key == key);
        if (c == null) { openKey = null; return; }
        overlay.Children.Clear();
        var w = Math.Min(440, root.ActualWidth);
        // placed by where its tile sits across the row: the first tile's panel from its left edge, the last one's from
        // its right, one between them centred on it in step — kept on the page (the Mac's, 1.3.16)
        var along = root.ActualWidth > tile.Width ? Math.Clamp(tile.X / (root.ActualWidth - tile.Width), 0, 1) : 0;
        var x = Math.Clamp(tile.X + (tile.Width - w) * along, 0, Math.Max(0, root.ActualWidth - w));
        panelContent = PanelContent(c, key, w);
        panelContent.Width = w - 2;
        panelContent.Measure(new Size(w - 2, double.PositiveInfinity));
        var h = Math.Max(tile.Height, panelContent.DesiredSize.Height + 2);
        panelRect = new Rect(x, tile.Y, w, h);
        frame = new Border
        {
            CornerRadius = new CornerRadius(8), ClipToBounds = true,
            Child = new Grid { Children = { Wash(CounterColor(key)), new Canvas { Children = { panelContent } } } },
            Effect = K.Shadow(32, 10, 0.22), SnapsToDevicePixels = true,
        };
        frame.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        Shell.Tour.Mark(frame, Shell.TourSpot.CounterPanel);
        overlay.Children.Add(frame);
        if (tiles.TryGetValue(key, out var t)) t.Opacity = 0;
        if (!animate || !Motion.On)
        {
            PlaceAt(frame, panelRect);
            return;
        }
        // the panel rises out of its card: the surface grows from the card's place and size to its own, its words
        // fading in as it opens (Windows' decelerate curve)
        PlaceAt(frame, tile);
        panelContent.Opacity = 0;
        Motion.FromTo(frame, Canvas.LeftProperty, tile.X, panelRect.X, Motion.Slow + 67, Motion.Decelerate);
        Motion.FromTo(frame, Canvas.TopProperty, tile.Y, panelRect.Y, Motion.Slow + 67, Motion.Decelerate);
        Motion.FromTo(frame, FrameworkElement.WidthProperty, tile.Width, panelRect.Width, Motion.Slow + 67, Motion.Decelerate);
        Motion.FromTo(frame, FrameworkElement.HeightProperty, tile.Height, panelRect.Height, Motion.Slow + 67, Motion.Decelerate);
        Motion.FadeIn(panelContent, Motion.Medium, 90);
        Shot.Moved();
    }

    private static void PlaceAt(FrameworkElement e, Rect r)
    {
        foreach (var dp in new[] { Canvas.LeftProperty, Canvas.TopProperty, FrameworkElement.WidthProperty, FrameworkElement.HeightProperty }) e.BeginAnimation(dp, null);
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = r.Width;
        e.Height = r.Height;
    }

    /// <summary>Folded back into its tile: the surface shrinks to the card's place, its words fading first, then the card shows again.</summary>
    private void ClosePanel(string key)
    {
        var f = frame;
        var content = panelContent;
        frame = null;
        panelContent = null;
        if (f == null || overlay == null) { if (tiles.TryGetValue(key, out var t0)) t0.Opacity = 1; return; }
        var tile = TileRect(key);
        void Done()
        {
            overlay?.Children.Remove(f);
            if (tiles.TryGetValue(key, out var t)) { t.Opacity = 1; Motion.FadeIn(t, Motion.Fast); }
        }
        if (!Motion.On || tile.IsEmpty) { Done(); return; }
        if (content != null) Motion.FadeOut(content, () => { }, Motion.Fast);
        var from = new Rect(Canvas.GetLeft(f), Canvas.GetTop(f), f.ActualWidth, f.ActualHeight);
        Motion.FromTo(f, Canvas.LeftProperty, from.X, tile.X, Motion.Medium, Motion.Standard);
        Motion.FromTo(f, Canvas.TopProperty, from.Y, tile.Y, Motion.Medium, Motion.Standard);
        Motion.FromTo(f, FrameworkElement.WidthProperty, from.Width, tile.Width, Motion.Medium, Motion.Standard);
        Motion.FromTo(f, FrameworkElement.HeightProperty, from.Height, tile.Height, Motion.Medium, Motion.Standard, 0, Done);
    }

    /// <summary>The list came (or changed) while the panel is open: its words again, the surface to its new height.</summary>
    private void PanelData()
    {
        if (openKey == null || frame == null || root == null || today.Data?.Counters.FirstOrDefault(k => k.Key == openKey) is not { } c) return;
        var w = panelRect.Width;
        var content = PanelContent(c, openKey, w);
        content.Width = w - 2;
        content.Measure(new Size(w - 2, double.PositiveInfinity));
        if (frame.Child is Grid g && g.Children.OfType<Canvas>().FirstOrDefault() is { } cv) { cv.Children.Clear(); cv.Children.Add(content); }
        panelContent = content;
        var h = Math.Max(TileRect(openKey).Height, content.DesiredSize.Height + 2);
        Motion.To(frame, FrameworkElement.HeightProperty, h, Motion.Medium, Motion.Decelerate);
        panelRect.Height = h;
        Shot.Moved();
    }

    /// <summary>The rows a panel shows before its list scrolls: about six, the panel's foot (See all) under them.</summary>
    private const double PanelListMost = 340;

    private FrameworkElement PanelContent(Counter c, string key, double w)
    {
        var data = sheet.Data;
        var n = Count(c, full: true);
        var alert = c.Tone == "red" && (n.Value ?? 0) > 0;
        var color = CounterColor(key);
        var close = K.IconButton(SymbolRegular.Dismiss24, () => SetOpen(null), "Close", 14, 32);
        var titleRow = K.Columns("auto, *, auto",
            K.IconTile(Glyph.Counter(key), color, 28).Col(0),
            K.Text(data?.Title ?? FullName(key, c.Label), Ty.BodyStrong).Margin(10, 0, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
            close.Col(2));
        var count = K.Text(n.Value?.ToString() ?? "–", Ty.TitleLarge, n.Value == null ? B.Text3 : alert ? B.Critical : B.Text);
        var line = data?.Note ?? n.Note;
        var countRow = K.Columns("auto, *", count.Col(0));
        if (!string.IsNullOrEmpty(line)) countRow.Children.Add(K.Wrapped(line!, Ty.Body, B.Text2, 2).Margin(14, 0, 0, 6).Align(HorizontalAlignment.Stretch, VerticalAlignment.Bottom).Col(1));
        var head = K.V(4, titleRow, countRow);
        // (a click on the header folds it back too)
        head.Background = Brushes.Transparent;
        head.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not DependencyObject o || !IsInside(o, close)) SetOpen(null); };

        FrameworkElement body;
        FrameworkElement? foot = null;
        if (data == null)
        {
            body = sheet.Error is { } err ? K.Empty(SymbolRegular.ErrorCircle24, err) : K.Reading("Loading…");
        }
        else if (data.Sections.Count == 0) body = K.Empty(SymbolRegular.CheckmarkCircle24, data.Empty ?? "Nothing here.");
        else
        {
            var sections = new Stack { Orientation = Orientation.Vertical, Spacing = 14 };
            foreach (var s in data.Sections) sections.Children.Add(Section(key, s));
            body = new ScrollViewer { Content = sections, MaxHeight = PanelListMost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            foot = SeeAll(key, n.Value ?? data.Sections.Sum(s => s.Rows.Count));
        }
        var all = K.V(14, head, K.Divider(), body);
        if (foot != null) all.Children.Add(foot);
        return new Border { Child = all, Padding = new Thickness(18, 14, 14, foot != null ? 10 : 16) };
    }

    /// <summary>The panel's way on to the whole list, so it is never a dead end: the work in To Do (which lists it day by
    /// day), the grades in Grades, the announcements among the notifications.</summary>
    private FrameworkElement SeeAll(string key, int count)
    {
        var (words, place) = key switch
        {
            "graded" => ("See all in Grades", Place.Grades),
            "unread" => ("See all in Notifications", Place.Notifications),
            _ => (count > 0 ? $"See all {count} in To Do" : "See all in To Do", Place.Todo),
        };
        var link = K.Link(words, () => { SetOpen(null); engine.Go(place); });
        link.HorizontalAlignment = HorizontalAlignment.Left;
        link.Margin = new Thickness(-6, -6, 0, 0);
        return link;
    }

    private static bool IsInside(DependencyObject o, DependencyObject container)
    {
        for (var p = o; p != null; p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p)) if (p == container) return true;
        return false;
    }

    /// <summary>
    /// A section of the panel: its title, its rows — each the Dashboard's one row of work (its course and kind over its
    /// title, when at its right with what to say about it under that: "10 pts", "Missing · 15 pts", "19 / 19"), a
    /// chevron to its quick look, or Dismiss. A page from before those fields came draws a row as it did: a bar of the
    /// course's colour, the title, its line.
    /// </summary>
    private FrameworkElement Section(string key, SheetSection s)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 2 };
        var quiet = s.Quiet == true;
        if (s.Title.Length > 0) v.Children.Add(K.Text(s.Title, Ty.Caption, quiet ? B.Text3 : B.Text2).Strong().Margin(8, 0, 0, 4));
        foreach (var row in s.Rows)
        {
            var rq = row.Quiet == true;
            var title = K.Wrapped(row.Title, Ty.Body, rq ? B.Text2 : B.Text, 2);
            Grid g;
            if (row.When != null || row.Course != null || row.Kind != null)
            {
                var tint = K.Hex(row.Color);
                if (rq) tint = K.Mix(tint, Color.FromRgb(0x8a, 0x8a, 0x8a), 0.5);
                var symbol = key == "unread" ? SymbolRegular.Megaphone24 : WorkPreview.KindOf(row.Url)?.Item2 ?? Glyph.Work(row.Kind);
                g = K.Columns("*, auto", WorkFace(tint, symbol, row.Course, row.Kind, null, title, row.Detail, row.When, stacked: true, tileSize: 28).Col(0));
            }
            else
            {
                var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = K.Brush(K.WithAlpha(K.Hex(row.Color), rq ? 0.5 : 1)), Margin = new Thickness(0, 2, 12, 2) };
                var words = K.V(1, title);
                if (!string.IsNullOrEmpty(row.Sub)) words.Children.Add(K.Text(row.Sub!, Ty.Caption, B.Text2));
                g = K.Columns("auto, *, auto", bar.Col(0), words.Col(1));
            }
            var last = g.ColumnDefinitions.Count - 1;
            var clearable = row.Clearable == true && row.Key != null;
            if (clearable)
            {
                var k = row.Key!;
                var dismiss = K.Button("Dismiss", () => Clear(k));
                dismiss.MinWidth = 0;
                dismiss.Margin = new Thickness(8, 0, 0, 0);
                g.Children.Add(dismiss.Col(last));
                v.Children.Add(K.Card(g, new Thickness(8), 4).Bg(B.Subtle));
            }
            else if (row.Url != null)
            {
                g.Children.Add(K.Icon(SymbolRegular.ChevronRight20, 12, B.Text3).Margin(8, 0, 0, 0).Col(last));
                var r = row;
                var b = K.Subtle(g, null, new Thickness(8, 7, 8, 7));
                b.Click += (_, _) => WorkPreview.Press(WorkPreview.Sheet(r, key, engine), b);
                v.Children.Add(b);
            }
            else v.Children.Add(g.Margin(8, 7, 8, 7));
        }
        return v;
    }

    /// <summary>Dismissed from Overdue: the row goes at once, the tile's number with it; the school told, the list read again.</summary>
    private async void Clear(string k)
    {
        if (sheet.Data is { } d)
        {
            foreach (var s in d.Sections) s.Rows.RemoveAll(r => r.Key == k);
            d.Sections.RemoveAll(s => s.Rows.Count == 0);
        }
        overdueAdjust--;
        PanelData();
        try
        {
            var left = await engine.Call<TodayCounts>("clearOverdue", new { key = k });
            overdueAdjust = 0;
            if (countsFresh.Data != null) countsFresh.Data.Overdue = left.Overdue;
            engine.DataChanged();
        }
        catch { overdueAdjust++; }
        sheet.Again();
        if (openKey != null) sheet.Ensure(engine, "todaySheet", new { key = openKey });
        Render();
    }

    // ---- ticking work off ----

    private bool Done(string id, bool done) => ticked.TryGetValue(id, out var tk) ? tk : done;

    /// <summary>Ticked off (or back on): shown at once; the school told; read again after a moment, so the tick shows before the row moves.</summary>
    private async void Tick(string id, bool done)
    {
        ticked[id] = done;
        ticks++;
        var ok = await engine.Act("complete", new { id, done });
        if (!ok) { ticked[id] = !done; ticks++; Render(); return; }
        await Task.Delay(450);
        ticked.Remove(id);
        ticks++;
        list.Again();
        today.Again();
        engine.DataChanged();
    }
}

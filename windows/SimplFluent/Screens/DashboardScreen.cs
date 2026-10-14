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
    public override string? Subtitle => today.Data?.DateLine is { } line ? (Updating ? $"{line} · Updating…" : Stale ? $"{line} · Couldn’t update" : line) : null;

    /// <summary>The counts shown are the ones kept from last time: Today's answer not in yet, or the counts made on their
    /// own (Overdue, Graded) only as kept.</summary>
    private bool Updating => today.Data != null && (TodayUpdating || CountsUpdating);

    /// <summary>Today's live answer, or the live counts, still being read (only then are the kept ones dimmed).</summary>
    private bool TodayUpdating => today.Kept && today.Loading;
    private bool CountsUpdating => countsFresh.Data == null && countsFresh.Loading;

    /// <summary>A live read failed (offline, the school's error): the kept counts stand, undimmed, and the page says it
    /// could not bring them up to date — never that it still is.</summary>
    private bool Stale => today.Data != null && !Updating && ((today.Kept && !today.Loading) || (countsFresh.Data == null && countsFresh.Error != null));

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
        page.Children.Add(Part("counters", new object?[] { d, countsQuick.Data, countsFresh.Data, TodayUpdating, CountsUpdating, overdueAdjust, openKey, layout, look }, () => Counters(d, layout)));
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
        // (a panel already up — over the page, or over another place — is kept, its list read again)
        if (openKey != null) root.Loaded += (_, _) => { if (frame == null) ShowPanel(animate: !sheetAsked); else PanelData(); };
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

    /// <summary>The colour a counter's tile and its panel wear: the counter's own, except that Overdue wears its red only
    /// while something is overdue — with nothing in it, or while it is still counted, a quiet grey — so red on the
    /// Dashboard always means late work.</summary>
    private static Color TileColor(Counter c, int? value) =>
        c.Tone == "red" && (value ?? 0) <= 0 ? Color.FromRgb(0x8a, 0x8a, 0x8a) : CounterColor(c.Key);

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

    /// <summary>A window this wide or wider shows the six counters in one row of square tiles, however wide the page
    /// beside the navigation pane is (1024 is the pictures' usual window, the page beside the pane 640 of it).</summary>
    private const double OneRowFrom = 1024;

    /// <summary>A square tile's padding, its symbol's size and the room between the symbol and its name.</summary>
    private const double SquarePad = 12, SquareSymbol = 24, SquareGap = 8;

    /// <summary>The narrowest square tiles' padding at their sides (the Mac's too): on a 1024 window the longest name,
    /// Next 7 days, has room at its own size in them only so — at 12 it was shrunk a little to fit.</summary>
    private const double SquarePadTight = 10;

    /// <summary>The tallest a square tile grows: as tall as it is wide on a window up to about 1650 wide; on a wider one (a
    /// 1080p window's tiles are about 246 wide) it stops here, square enough, where a tile as tall as wide would only add
    /// empty colour under its words and push the work under the counters further down.</summary>
    private const double SquareMost = 200;

    /// <summary>
    /// How the counters are laid out for a page and a window this wide: on a window 1024 wide or wider, one row of six
    /// square tiles — told by the window, so a page that narrows as the pane or the rail takes room never puts them back
    /// in two rows — and whether each of those tiles has room for its symbol beside its name at its top (else the name
    /// has the top to itself and the symbol goes beside the number); on a narrower window, three to a row, or two on the
    /// narrowest page. And which names the row wears, decided once for all six: the full ones ("Announcements", "Graded
    /// this week") where every one of them fits as its tile draws it, else the short ones every tile fits ("Unread",
    /// "Graded"), whose lines then say the rest — never a name cut.
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
        var words = (Math.Max(320, page) - 5 * 12) / 6 - 2 - 2 * SquarePad;
        var roomy = words >= SquareSymbol + SquareGap + WidestLabel(full: false) + 2;
        // (the narrowest tiles' words sit SquarePadTight in from their sides, so they have that much more room)
        var room = roomy ? words - SquareSymbol - SquareGap : words + 2 * (SquarePad - SquarePadTight);
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
            var tile = cols == 6 ? SquareTile(c, layout.Roomy, layout.Full) : Tile(c, layout.Full);
            Grid.SetColumn(tile, (k % cols) * 2);
            Grid.SetRow(tile, (k / cols) * 2);
            grid.Children.Add(tile);
            tiles[c.Key] = tile;
            if (c.Key == "next") Shell.Tour.Mark(tile, Shell.TourSpot.CounterNext);
            if (openKey == c.Key && !panelMoved) tile.Opacity = 0;
        }
        Shell.Tour.Mark(grid, Shell.TourSpot.Counters);
        return grid;
    }

    /// <summary>A counter's number as its tile draws it: "–" while it is counted, red for the alert, dimmed while it is
    /// the one kept from last time (the live one takes its place as it comes).</summary>
    private static TextBlock Number((int? Value, string? Note, bool Kept) n, bool alert)
    {
        var count = K.Text(n.Value?.ToString() ?? "–", Ty.Count, n.Value == null ? B.Text3 : B.Text);
        if (alert && n.Value != null) count.Foreground = new SolidColorBrush(AlertRed);
        if (n.Value != null && n.Kept) count.Opacity = 0.5;
        return count;
    }

    /// <summary>
    /// The alert's red, the same whatever the theme: Windows' critical red in light, and in dark a red of Overdue's own
    /// (#FF5A4F) — dark's critical (#FF99A4) is a pink that a theme's accent can match (Dusk's #FFAABB), and the alert
    /// must never look like the accent. On the alert's face it reads at over 4:1 in dark, 5:1 in light.
    /// </summary>
    private static Color AlertRed => Appearance.Dark ? Color.FromRgb(0xff, 0x5a, 0x4f) : Color.FromRgb(0xc4, 0x2b, 0x1c);

    /// <summary>A counter's tile three to a row (two on the narrowest page): its symbol and name, its number, the line
    /// under it in whole words.</summary>
    private Button Tile(Counter c, bool full)
    {
        var n = Count(c, full);
        var alert = c.Tone == "red" && (n.Value ?? 0) > 0;
        var color = TileColor(c, n.Value);
        var label = K.Text(NameOf(c, full), Ty.Body, B.Text2);
        var head = K.Columns("auto, *", K.IconTile(Glyph.Counter(c.Key), color, 28).Col(0), label.Margin(10, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1));
        var count = Number(n, alert);
        count.Margin = new Thickness(0, 10, 0, 0);
        var content = K.V(0, head, count, new NoteLines(n.Note));
        content.Margin = new Thickness(14, 14, 14, 12);
        return TileButton(c, content, n, alert, color, square: false);
    }

    /// <summary>
    /// A counter's tile in one row of six: square — as tall as it is wide, never shorter than its words need nor taller
    /// than <see cref="SquareMost"/> — and laid out as the tile always was, so each counter reads as a card of its own
    /// colour: its symbol and name at the top, its number under them, the line about the count under that in whole words
    /// on up to two lines, and the rest of the square its colour (or its theme's picture, pinned at its foot). Where the
    /// symbol and the name do not fit side by side (the narrowest tiles, on a window not much over 1024), the name has
    /// the top to itself and the symbol leads the number.
    /// </summary>
    private Button SquareTile(Counter c, bool roomy, bool full)
    {
        var n = Count(c, full);
        var alert = c.Tone == "red" && (n.Value ?? 0) > 0;
        var color = TileColor(c, n.Value);
        var symbol = K.IconTile(Glyph.Counter(c.Key), color, SquareSymbol);
        var label = K.Text(NameOf(c, full), Ty.Body, B.Text2);
        label.TextTrimming = TextTrimming.None;
        // (a name wider than its room, were there ever one, shrunk to fit rather than cut; a number likewise)
        var name = new Viewbox { Child = label, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var count = Number(n, alert);
        count.TextTrimming = TextTrimming.None;
        FrameworkElement head, line;
        if (roomy)
        {
            head = K.Columns("auto, *", symbol.Col(0), name.Margin(SquareGap, 0, 0, 0).Col(1));
            line = count;
        }
        else
        {
            head = name;
            var number = new Viewbox { Child = count, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(SquareGap, 0, 0, 0) };
            line = K.Columns("auto, *", symbol.Col(0), number.Col(1));
        }
        line.Margin = new Thickness(0, 8, 0, 0);
        var content = K.V(0, head, line, new NoteLines(n.Note));
        content.Margin = roomy ? new Thickness(SquarePad, SquarePad, SquarePad, 10) : new Thickness(SquarePadTight, 10, SquarePadTight, 10);
        return TileButton(c, content, n, alert, color, square: true);
    }

    /// <summary>A counter's tile round its words: its colour washed across it (or its theme's picture), opening its panel
    /// when pressed, what it counts with its number — and the line about it, whole, wherever the tile had no room for
    /// it — its tooltip and what a screen reader says. Overdue with something in it wears the alert on its own face — a
    /// solid red tint ringed in red, over any wash, picture or theme — so it never looks like a tile with nothing in it.</summary>
    private Button TileButton(Counter c, FrameworkElement content, (int? Value, string? Note, bool Kept) n, bool alert, Color color, bool square)
    {
        // (its theme's scene, or a photo of the student's, under its words — Settings ▸ Appearance)
        var face = new Grid();
        if (Scenes.Backdrop(c.Key) is { } scene)
        {
            face.Children.Add(scene);
            // (the scene kept faint behind the name and the number, whole only in the tile's empty corner)
            if (!alert) face.Children.Add(Scenes.WordsVeil());
        }
        else if (!alert) face.Children.Add(Wash(color));
        if (alert) face.Children.Add(AlertFace());
        face.Children.Add(content);
        face.SizeChanged += (_, e) => face.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        var b = K.CardButton(square ? new Square { Child = face, Most = SquareMost } : face, () => SetOpen(openKey == c.Key ? null : c.Key), 8, new Thickness(0));
        var spoken = Spoken(c.Key, c.Label, n.Value);
        b.ToolTip = string.IsNullOrEmpty(n.Note) ? spoken : $"{spoken}\n{n.Note}";
        System.Windows.Automation.AutomationProperties.SetName(b, spoken);
        if (!string.IsNullOrEmpty(n.Note)) System.Windows.Automation.AutomationProperties.SetHelpText(b, n.Note);
        return b;
    }

    /// <summary>A square tile's face: as tall as it is wide, up to its most, and never shorter than what it holds needs.</summary>
    private sealed class Square : Decorator
    {
        public double Most { get; init; } = double.PositiveInfinity;

        protected override Size MeasureOverride(Size available)
        {
            var held = base.MeasureOverride(available);
            var side = double.IsInfinity(available.Width) ? 0 : Math.Min(available.Width, Most);
            return new Size(held.Width, Math.Max(held.Height, side));
        }
    }

    /// <summary>
    /// The line about a count, whole: on as many lines as it takes, up to two — its parts ("announcements · 2 courses" is
    /// two) kept whole on lines of their own where each fits the tile's width ("announcements" over "2 courses", not
    /// "announcements · 2" over "courses"), else wrapped at its words where each word fits — or not at all (never a word
    /// cut, never "…"; the tile's tooltip and what a screen reader says keep it). Decided as it is measured, so a tile
    /// that widens or narrows with the window shows it or leaves it out by itself. A tile without one keeps a line's
    /// room, so its number sits where its neighbours' numbers do.
    /// </summary>
    private sealed class NoteLines : Decorator
    {
        private const int Most = 2;
        private readonly TextBlock text;
        private readonly string note;
        private readonly string[] parts;
        private readonly double[] partWidths, wordWidths;
        private double room = double.NaN;

        public NoteLines(string? note)
        {
            this.note = note ?? "";
            parts = this.note.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            partWidths = parts.Select(p => WordsWidth(p, Ty.Caption)).ToArray();
            wordWidths = this.note.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => WordsWidth(w, Ty.Caption)).ToArray();
            text = K.Text("", Ty.Caption, B.Text2, TextWrapping.WrapWithOverflow);
            text.TextTrimming = TextTrimming.None;
            Child = text;
        }

        protected override Size MeasureOverride(Size available)
        {
            var line = K.Ramp(Ty.Caption).Line;
            if (!available.Width.Equals(room))
            {
                room = available.Width;
                var shown = Lines(room);
                if (text.Text != shown) text.Text = shown;
            }
            text.Measure(new Size(available.Width, double.PositiveInfinity));
            // (Hidden, not Collapsed: showing or hiding it here asks for no second measure)
            var whole = text.Text.Length > 0 && text.DesiredSize.Height <= Most * line + 0.5;
            text.Visibility = whole ? Visibility.Visible : Visibility.Hidden;
            return whole ? text.DesiredSize : new Size(0, line);
        }

        /// <summary>The note as it shows in this room: its parts packed whole onto their lines where each part fits a
        /// line, else the note as it is, to wrap at its words where each word fits one; nothing where neither.</summary>
        private string Lines(double room)
        {
            if (note.Length == 0 || double.IsInfinity(room)) return note;
            if (partWidths.All(w => w <= room + 0.5))
            {
                var lines = new List<string>();
                var at = "";
                foreach (var part in parts)
                {
                    var joined = at.Length == 0 ? part : $"{at} · {part}";
                    if (at.Length == 0 || WordsWidth(joined, Ty.Caption) <= room + 0.5) at = joined;
                    else { lines.Add(at); at = part; }
                }
                lines.Add(at);
                if (lines.Count <= Most) return string.Join("\n", lines);
            }
            return wordWidths.All(w => w <= room + 0.5) ? note : "";
        }
    }

    /// <summary>The alert's face: Windows' critical background, solid, ringed in the alert's red.</summary>
    private static Border AlertFace()
    {
        var f = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1.5), IsHitTestVisible = false, BorderBrush = new SolidColorBrush(AlertRed) };
        f.SetResourceReference(Border.BackgroundProperty, B.CriticalBack);
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
                return (n, overdueAdjust != 0 ? (n > 0 ? $"{n} not submitted" : "Nothing overdue") : k.OverdueNote, CountsUpdating);
            }
            return (null, null, false);
        }
        if (c.Key == "graded")
        {
            var k = fresh ?? quick;
            var note = full && !string.IsNullOrEmpty(k?.GradedFullNote) ? k!.GradedFullNote : k?.GradedNote;
            return k?.Graded is { } g ? (g, note, CountsUpdating) : (null, null, false);
        }
        var line = full && !string.IsNullOrEmpty(c.FullNote) ? c.FullNote : c.Note;
        if (today.Data == null || c.Pending == true) return (null, line, false);
        return (c.Value, line, TodayUpdating);
    }

    // ---- the panel a counter grows into (the Mac's DashCounterPanel: 440 wide; here a Windows 11 flyout surface) ----
    // (it takes nothing from the window round it and stays until it is closed — its ×, Escape, its tile again — over
    //  whatever place is opened meanwhile: it is drawn in the shell's overlay over the page, WorkPreview.Layer, and its
    //  heading moves it)

    private string? openKey;
    private bool sheetAsked;
    private Border? frame;
    /// <summary>The panel moved away from its tile by its heading: the tile shows again, and the panel fades away when it
    /// is closed rather than folding into it.</summary>
    private bool panelMoved;

    /// <summary>Where the panel is drawn: over the page, above every place (the shell's overlay); this page's own until
    /// the shell has one.</summary>
    private Canvas? Layer => WorkPreview.Layer ?? overlay;
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

    /// <summary>A counter's tile where the panel is drawn (nothing while the page is not shown).</summary>
    private Rect TileRect(string key)
    {
        if (Layer is not { } layer || !tiles.TryGetValue(key, out var t) || !t.IsLoaded || !t.IsVisible) return Rect.Empty;
        try { return new Rect(t.TranslatePoint(new Point(0, 0), layer), new Size(t.ActualWidth, t.ActualHeight)); }
        catch (InvalidOperationException) { return Rect.Empty; }
    }

    /// <summary>The page's content where the panel is drawn.</summary>
    private Rect PageRect(Canvas layer)
    {
        if (root == null || !root.IsVisible) return Rect.Empty;
        try { return new Rect(root.TranslatePoint(new Point(0, 0), layer), new Size(root.ActualWidth, root.ActualHeight)); }
        catch (InvalidOperationException) { return Rect.Empty; }
    }

    private void ShowPanel(bool animate)
    {
        if (openKey == null || root == null || Layer is not { } layer) return;
        var key = openKey;
        var tile = TileRect(key);
        var pageRect = PageRect(layer);
        if (tile.IsEmpty || pageRect.IsEmpty) return;
        var d = today.Data;
        var c = d?.Counters.FirstOrDefault(k => k.Key == key);
        if (c == null) { openKey = null; return; }
        if (frame != null) (frame.Parent as Panel)?.Children.Remove(frame);
        panelMoved = false;
        var w = Math.Min(440, pageRect.Width);
        // placed by where its tile sits across the row: the first tile's panel from its left edge, the last one's from
        // its right, one between them centred on it in step — kept on the page (the Mac's, 1.3.16)
        var along = pageRect.Width > tile.Width ? Math.Clamp((tile.X - pageRect.X) / (pageRect.Width - tile.Width), 0, 1) : 0;
        var x = Math.Clamp(tile.X + (tile.Width - w) * along, pageRect.X, Math.Max(pageRect.X, pageRect.Right - w));
        panelContent = PanelContent(c, key, w);
        panelContent.Width = w - 2;
        panelContent.Measure(new Size(w - 2, double.PositiveInfinity));
        var h = Math.Max(tile.Height, panelContent.DesiredSize.Height + 2);
        panelRect = new Rect(x, tile.Y, w, h);
        frame = new Border
        {
            CornerRadius = new CornerRadius(8), ClipToBounds = true,
            Child = new Grid { Children = { Wash(TileColor(c, Count(c).Value)), new Canvas { Children = { panelContent } } } },
            Effect = K.Shadow(32, 10, 0.22), SnapsToDevicePixels = true,
        };
        frame.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        Shell.Tour.Mark(frame, Shell.TourSpot.CounterPanel);
        // (its heading moves it — its tile then shows again; Escape inside it closes it, wherever it is)
        Drag.ByHeading(frame, 56, () =>
        {
            panelMoved = true;
            if (openKey != null && tiles.TryGetValue(openKey, out var t2) && t2.Opacity < 1) { t2.Opacity = 1; Motion.FadeIn(t2, Motion.Fast); }
        });
        frame.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; SetOpen(null); } };
        layer.Children.Add(frame);
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
        var moved = panelMoved;
        frame = null;
        panelContent = null;
        panelMoved = false;
        if (f == null) { if (tiles.TryGetValue(key, out var t0)) t0.Opacity = 1; return; }
        var tile = TileRect(key);
        void Done()
        {
            (f.Parent as Panel)?.Children.Remove(f);
            if (tiles.TryGetValue(key, out var t) && t.Opacity < 1) { t.Opacity = 1; Motion.FadeIn(t, Motion.Fast); }
        }
        // (moved away from its tile, or its tile not shown — another place open under it: it fades where it is)
        if (Motion.On && (moved || tile.IsEmpty)) { Motion.FadeOut(f, Done, Motion.Fast); return; }
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

    private FrameworkElement PanelContent(Counter c, string key, double w)
    {
        var data = sheet.Data;
        var n = Count(c, full: true);
        var alert = c.Tone == "red" && (n.Value ?? 0) > 0;
        var color = TileColor(c, n.Value);
        var close = K.IconButton(SymbolRegular.Dismiss24, () => SetOpen(null), "Close (Esc)", 14, 32);
        var titleRow = K.Columns("auto, *, auto",
            K.IconTile(Glyph.Counter(key), color, 28).Col(0),
            K.Text(data?.Title ?? FullName(key, c.Label), Ty.BodyStrong).Margin(10, 0, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
            close.Col(2));
        var count = K.Text(n.Value?.ToString() ?? "–", Ty.TitleLarge, n.Value == null ? B.Text3 : B.Text);
        if (alert) count.Foreground = new SolidColorBrush(AlertRed);
        var line = data?.Note ?? n.Note;
        var countRow = K.Columns("auto, *", count.Col(0));
        if (!string.IsNullOrEmpty(line)) countRow.Children.Add(K.Wrapped(line!, Ty.Body, B.Text2, 2).Margin(14, 0, 0, 6).Align(HorizontalAlignment.Stretch, VerticalAlignment.Bottom).Col(1));
        var head = K.V(4, titleRow, countRow);
        head.Background = Brushes.Transparent; // (its heading takes hold of the panel to move it: Kit/Drag.cs)

        // (the list under the header, scrolling past about five rows, and nothing under it: the panel as 0.2.9 had it)
        FrameworkElement body;
        if (data == null)
        {
            body = sheet.Error is { } err ? K.Empty(SymbolRegular.ErrorCircle24, err) : K.Reading("Loading…");
        }
        else if (data.Sections.Count == 0) body = K.Empty(SymbolRegular.CheckmarkCircle24, data.Empty ?? "Nothing here.");
        else
        {
            var sections = new Stack { Orientation = Orientation.Vertical, Spacing = 14 };
            foreach (var s in data.Sections) sections.Children.Add(Section(key, s));
            body = new ScrollViewer { Content = sections, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
        var all = K.V(14, head, K.Divider(), body);
        return new Border { Child = all, Padding = new Thickness(18, 14, 14, 16) };
    }

    /// <summary>
    /// A section of the panel: its title, its rows as 0.2.9 drew them — a bar of the course's colour, the title, and under
    /// it the engine's one line about it ("F26-MATH 021 20 · 10 pts · due 11:59 PM") — each with a chevron to its quick
    /// look, or Dismiss.
    /// </summary>
    private FrameworkElement Section(string key, SheetSection s)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 2 };
        var quiet = s.Quiet == true;
        if (s.Title.Length > 0) v.Children.Add(K.Text(s.Title, Ty.Caption, quiet ? B.Text3 : B.Text2).Strong().Margin(8, 0, 0, 4));
        foreach (var row in s.Rows)
        {
            var rq = row.Quiet == true;
            var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = K.Brush(K.WithAlpha(K.Hex(row.Color), rq ? 0.5 : 1)), Margin = new Thickness(0, 2, 12, 2) };
            var words = K.V(1, K.Wrapped(row.Title, Ty.Body, rq ? B.Text2 : B.Text, 2));
            if (!string.IsNullOrEmpty(row.Sub)) words.Children.Add(K.Text(row.Sub!, Ty.Caption, B.Text2));
            var g = K.Columns("auto, *, auto", bar.Col(0), words.Col(1));
            var clearable = row.Clearable == true && row.Key != null;
            if (clearable)
            {
                var k = row.Key!;
                var dismiss = K.Button("Dismiss", () => Clear(k));
                dismiss.MinWidth = 0;
                dismiss.Margin = new Thickness(8, 0, 0, 0);
                g.Children.Add(dismiss.Col(2));
                v.Children.Add(K.Card(g, new Thickness(8), 4).Bg(B.Subtle));
            }
            else if (row.Url != null)
            {
                g.Children.Add(K.Icon(SymbolRegular.ChevronRight20, 12, B.Text3).Margin(8, 0, 0, 0).Col(2));
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

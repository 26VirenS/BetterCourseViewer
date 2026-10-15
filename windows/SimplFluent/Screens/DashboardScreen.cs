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
        ChecksForView();
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
                ChecksForView();
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
        CounterLayout(before) != CounterLayout(now) || (before >= 900) != (now >= 900) || (before >= 760) != (now >= 760) || CardCols(before) != CardCols(now)
        || Math.Abs(RailWidth(before) - RailWidth(now)) >= 24;

    /// <summary>The widest the view beside the rail grows: a row's title and its due time stay within a glance of each
    /// other on a 1080p screen (the rest of the page goes to the rail, then to room at the right).</summary>
    private const double MainMost = 820;

    /// <summary>The rail beside the view on a page 900 wide or wider: about a third of it, more where the view has reached
    /// its widest, never under 340 nor over 720.</summary>
    private static double RailWidth(double page) => Math.Clamp(Math.Max(Math.Round(page * 0.3), Math.Round(page - 28 - MainMost)), 340, 720);

    protected override FrameworkElement Build()
    {
        var d = today.Data;
        if (Title != lastTitle || Subtitle != lastSubtitle) { lastTitle = Title; lastSubtitle = Subtitle; HeaderChange(); }
        if (d == null) return K.LoadState(today.Error, () => today.Reload(engine, "today"));
        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 28 };
        builtWidth = Width;
        var (specs, two, railW) = Specs(d);
        page.Children.Add(Part("counters", specs["counters"]));
        var main = Part("main", specs["main"]);
        if (two)
        {
            var rail = Part("rail", specs["rail"]);
            var g = K.Columns($"*, 28, {railW.ToString(System.Globalization.CultureInfo.InvariantCulture)}", main.Col(0), rail.Col(2));
            // (on a page wider still, what is over stays as room at the right, not between a row's title and its time)
            g.ColumnDefinitions[0].MaxWidth = MainMost;
            page.Children.Add(g);
        }
        else
        {
            // (one column: the view chosen right under the counters — Activity or Cards shows at once — then the day's work)
            page.Children.Add(main);
            if (specs.TryGetValue("today", out var todaySpec)) page.Children.Add(Part("today", todaySpec));
            page.Children.Add(Part("rail", specs["rail"]));
        }
        shownParts = specs.Keys.ToList();
        root = new Grid();
        overlay = new Canvas { ClipToBounds = false };
        root.Children.Add(page);
        root.Children.Add(overlay);
        // (a panel already up is kept, its list read again, and goes where the page built again has put its tile; one
        //  asked for at launch, --sheet, opens once the page is up — tried again as each build of the page is)
        if (openKey != null)
            root.Loaded += (_, _) =>
            {
                if (frame != null) { Follow(); PanelData(); }
                else if (!ShowPanel(animate: !sheetAsked) && !sheetAsked) Unplaced();
            };
        return root;
    }

    // ---- parts kept from build to build ----

    /// <summary>A part of the page: what it is built from (compared from build to build), and how it is built.</summary>
    private sealed record PartSpec(object?[] Inputs, Func<FrameworkElement> Make);

    /// <summary>Each part of the page as it was built last, with what it was built from.</summary>
    private readonly Dictionary<string, (object?[] Inputs, FrameworkElement Part)> parts = new();
    /// <summary>The parts on the page as last built ("counters", "main", "rail", and "today" in one column), and the width
    /// it was built at (what a live check lays its changes out for).</summary>
    private List<string> shownParts = new();
    private double builtWidth;
    /// <summary>Bumped as a tick is shown, or an activity seen: the parts that draw them are built again.</summary>
    private int ticks, seenTurn;

    /// <summary>
    /// The page's parts for what is read now, at the page's width: the counters, the view chosen, the rail beside it on a
    /// page 900 wide or wider (else under it, the day's work its own part) — each with what it is built from. Build lays
    /// them out; a live check compares them again to change only the parts whose answers changed.
    /// </summary>
    private (Dictionary<string, PartSpec> Specs, bool Two, double RailW) Specs(Today d, double? at = null)
    {
        var width = Math.Max(320, at ?? Width);
        var look = (Appearance.Dark, Appearance.Accent, Appearance.ThemeName);
        var layout = CounterLayout(width);
        var specs = new Dictionary<string, PartSpec>
        {
            ["counters"] = new(new object?[] { d, countsQuick.Data, countsFresh.Data, TodayUpdating, CountsUpdating, overdueAdjust, openKey, layout, look }, () => Counters(d, layout)),
        };
        var two = width >= 900;
        var railW = RailWidth(width);
        var mainW = two ? Math.Min(MainMost, width - railW - 28) : width;
        // (the view: what it shows, at its width; the cards are the courses' and their progress, the list the planner's,
        //  the activity the school's — not today's, so a new count leaves them as they are)
        specs["main"] = new(new object?[] { View, Math.Round(mainW), courses.Data, progress.Data, courses.Error, list.Data, list.Error, activity.Data, activity.Error, ticks, seenTurn, courseCardsShown, look }, () => Main(d, mainW));
        if (two) specs["rail"] = new(new object?[] { d, skyFresh.Data, skyKept.Data, View, railW, false, ticks, look }, () => Rail(d, railW, false));
        else
        {
            if (View != 1) specs["today"] = new(new object?[] { d, ticks, look }, () => TodaySection(d, true));
            specs["rail"] = new(new object?[] { d, skyFresh.Data, skyKept.Data, View, Math.Round(width), true, ticks, look }, () => Rail(d, width, true));
        }
        return (specs, two, railW);
    }

    /// <summary>
    /// A part of the page (the counters, the view, the rail): the one built last time when what it shows has not changed —
    /// the same answers, the same width and view, the same ticks and look — taken from the page before and put in this
    /// one. A new count leaves the courses' cards exactly as they were (not built again, not redrawn), so the Dashboard's
    /// answers read again in the background change only what they change, in place. What a live check may change in it
    /// later (a counter's number, a list's rows) is noted as it is built (<see cref="PartRefs"/>).
    /// </summary>
    private FrameworkElement Part(string key, PartSpec spec)
    {
        if (parts.TryGetValue(key, out var was) && SameInputs(was.Inputs, spec.Inputs))
        {
            switch (was.Part.Parent)
            {
                case Panel panel: panel.Children.Remove(was.Part); break;
                case Decorator dec: dec.Child = null; break;
                case ContentControl cc: cc.Content = null; break;
            }
            return was.Part;
        }
        return Make(key, spec);
    }

    private static bool SameInputs(object?[] a, object?[] b) => a.Length == b.Length && a.Zip(b).All(p => Equals(p.First, p.Second));

    /// <summary>A part built (its pieces noted for the live check), and kept as built.</summary>
    private FrameworkElement Make(string key, PartSpec spec)
    {
        var was = noting;
        noting = new PartRefs();
        try
        {
            var e = spec.Make();
            parts[key] = (spec.Inputs, e);
            refs[key] = noting;
            return e;
        }
        finally { noting = was; }
    }

    // ---- the counters (the Mac's DashCounterTile, as Windows 11 cards) ----

    private static readonly string[] Order = { "today", "tomorrow", "next", "overdue", "unread", "graded" };

    /// <summary>
    /// Each counter's colour — the one its symbol wears, washed across its card and the panel it grows into: Due today
    /// blue, Tomorrow orange, Next 7 days green, Overdue red, Unread orange, Graded purple. Red is Overdue's alone: Due
    /// today is Windows' blue whatever the theme's accent (a theme's pink or red would read as late), and Overdue says
    /// late work in full — its number red, its card the alert's — only while something is overdue.
    /// </summary>
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

    /// <summary>
    /// How the counters are laid out on a page this wide (0.2.9's and the Mac's 1.3.77, cards as tall as their words): six
    /// to a row where six cards each have room for their symbol beside their name (the short names: a page about 870
    /// wide, a 1280 window's), else three to a row, or two on a page under 520. And which names the counters wear,
    /// decided once for all six: the full ones ("Announcements", "Graded this week") where every one of them fits beside
    /// its symbol, else the short ones ("Unread", "Graded"), whose lines then say the rest — never a name cut.
    /// </summary>
    private (int Cols, bool Full) CounterLayout(double page)
    {
        page = Math.Max(320, page);
        var cols = NameRoom(page, 6) >= WidestLabel(full: false) + 2 ? 6 : page >= 520 ? 3 : 2;
        return (cols, NameRoom(page, cols) >= WidestLabel(full: true) + 2);
    }

    /// <summary>A card's padding, its symbol's size and the room between the symbol and its name: 0.2.9's three or two to
    /// a row, a little tighter six to a row.</summary>
    private static (double Pad, double Symbol, double Gap) CardSizes(int cols) => cols == 6 ? (12, 24, 8) : (14, 28, 10);

    /// <summary>The room a card has for its name beside its symbol, <paramref name="cols"/> to a row on a page this wide
    /// (12 between the cards, a card's edge 1 at each side).</summary>
    private static double NameRoom(double page, int cols)
    {
        var (pad, symbol, gap) = CardSizes(cols);
        return (page - (cols - 1) * 12) / cols - 2 - 2 * pad - symbol - gap;
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

    private FrameworkElement Counters(Today d, (int Cols, bool Full) layout)
    {
        var cols = layout.Cols;
        var items = d.Counters.OrderBy(c => Array.IndexOf(Order, c.Key) is var i && i < 0 ? 99 : i).ToList();
        var grid = new Grid();
        for (var c = 0; c < cols; c++)
        {
            if (c > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        // (each row as tall as its tallest card's words, every card in it as tall as that)
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
            var tile = Tile(c, cols, layout.Full);
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
        var count = K.Text(n.Value?.ToString() ?? "–", Ty.Count, n.Value == null ? B.Text3 : B.Text);
        if (alert && n.Value != null) count.Foreground = new SolidColorBrush(AlertRed);
        if (n.Value != null && n.Kept) count.Opacity = 0.5;
        return count;
    }

    /// <summary>
    /// The alert's red, the same whatever the theme: Windows' critical red in light, and in dark a red of Overdue's own
    /// (#FF5A4F) — dark's critical (#FF99A4) is a pink that a theme's accent can match (Dusk's #FFAABB), and the alert
    /// must never look like the accent. On the alert's face it reads at about 4:1 in both (<see cref="AlertFace"/>).
    /// </summary>
    private static Color AlertRed => Appearance.Dark ? Color.FromRgb(0xff, 0x5a, 0x4f) : Color.FromRgb(0xc4, 0x2b, 0x1c);

    /// <summary>
    /// A counter's card (0.2.9's, the Mac's 1.3.77): its symbol and name at the top, its number under them, and the line
    /// about the count under that, whole on up to two lines or left out — as tall as that and no taller (the cards of a
    /// row as tall as its tallest), its colour washed across it. <paramref name="cols"/> to a row.
    /// </summary>
    private Button Tile(Counter c, int cols, bool full)
    {
        var (pad, size, gap) = CardSizes(cols);
        var n = Count(c, full);
        var alert = c.Tone == "red" && (n.Value ?? 0) > 0;
        var color = CounterColor(c.Key);
        var label = K.Text(NameOf(c, full), Ty.Body, B.Text2);
        label.TextTrimming = TextTrimming.None;
        // (a name wider than its room — were there ever one, a school's own name for a counter — shrunk to fit rather
        //  than cut)
        var name = new Viewbox { Child = label, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(gap, 0, 0, 0) };
        var head = K.Columns("auto, *", K.IconTile(Glyph.Counter(c.Key), color, size).Col(0), name.Col(1));
        // (the number in a holder of its own, cut at its edges: a live check's new number rolls in there, the old out)
        var number = new Grid { ClipToBounds = true, Margin = new Thickness(0, cols == 6 ? 8 : 10, 0, 0) };
        number.Children.Add(Number(n, alert));
        var content = K.V(0, head, number, new NoteLines(n.Note));
        content.Margin = new Thickness(pad, pad, pad, pad - 2);
        var tile = TileButton(c, content, n, alert, color);
        if (noting != null) noting.Tiles[c.Key] = new TileRefs(tile, number, content, (n.Value, n.Note, n.Kept, alert, NameOf(c, full)));
        return tile;
    }

    /// <summary>
    /// A counter's card round its words: its colour washed across it, inside it, ringed as every card is (0.3.3: no coloured
    /// edge — the owner: "remove the unnecessary border") — over
    /// its theme's picture too, which still shows through — opening its panel when pressed, what it counts with its number
    /// (and the line about it, whole, wherever the card had no room for it) its tooltip and what a screen reader says.
    /// Overdue with something in it wears the alert instead — a stronger red wash than its own, its number red — so late
    /// work is never only a tint.
    /// </summary>
    private Button TileButton(Counter c, FrameworkElement content, (int? Value, string? Note, bool Kept) n, bool alert, Color color)
    {
        var face = Face(c.Key, alert, color);
        face.Children.Add(content);
        var b = K.CardButton(face, () => SetOpen(openKey == c.Key ? null : c.Key), 8, new Thickness(0));
        var spoken = Spoken(c.Key, c.Label, n.Value);
        b.ToolTip = string.IsNullOrEmpty(n.Note) ? spoken : $"{spoken}\n{n.Note}";
        System.Windows.Automation.AutomationProperties.SetName(b, spoken);
        if (!string.IsNullOrEmpty(n.Note)) System.Windows.Automation.AutomationProperties.SetHelpText(b, n.Note);
        return b;
    }

    /// <summary>
    /// What a counter's card is drawn on under its words: its theme's scene, or a photo of the student's (Settings ▸
    /// Appearance), kept faint behind the name and the number by the page's colour, whole only in the card's empty
    /// corner; over it the counter's wash, or the alert's. Cut to the card's corners. The panel the card grows into wears
    /// it too, as it starts out and as it lands back (<see cref="CardLook"/>).
    /// </summary>
    private static Grid Face(string key, bool alert, Color color)
    {
        var face = new Grid();
        if (Scenes.Backdrop(key) is { } scene)
        {
            face.Children.Add(scene);
            face.Children.Add(Scenes.WordsVeil());
        }
        face.Children.Add(alert ? AlertFace() : Wash(color));
        face.SizeChanged += (_, e) => face.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        return face;
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

    /// <summary>
    /// The alert's face, Overdue's while something is late: a stronger red wash than the one it wears with nothing late
    /// (<see cref="Wash"/>). In light Overdue's own red, more of it; in dark a deeper red laid on
    /// thicker, redder than its own wash without lifting the card towards the red number on it — which reads on it at
    /// about 4:1 in both (34 px semibold).
    /// </summary>
    private static Border AlertFace()
    {
        var (c, near, far) = Appearance.Dark ? (Color.FromRgb(0xa0, 0x1c, 0x14), 0.46, 0.32) : (CounterColor("overdue"), 0.26, 0.16);
        return new Border
        {
            Background = new LinearGradientBrush(K.WithAlpha(c, near), K.WithAlpha(c, far), new Point(0, 0), new Point(1, 1)),
            CornerRadius = new CornerRadius(7), IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// A counter's colour washed across its card (Windows 11's tinted cards), drawn inside it: across the whole card, a
    /// little stronger at its symbol's corner, so each card reads as its colour at a glance — lighter in light, where a
    /// colour on white reads strong, so the number and the words over it stay easy to read. The panel it grows into
    /// (<paramref name="card"/> false) wears it as before, strongest at its symbol's corner and fading towards the far
    /// one, over a list.
    /// </summary>
    private static Border Wash(Color c, bool card = true)
    {
        var (near, far) = !card ? (0.24, 0.05) : Appearance.Dark ? (0.26, 0.14) : (0.18, 0.08);
        return new Border
        {
            Background = new LinearGradientBrush(K.WithAlpha(c, near), K.WithAlpha(c, far), new Point(0, 0), new Point(1, 1)),
            CornerRadius = new CornerRadius(7), IsHitTestVisible = false,
        };
    }

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
            // (the screenshot suite's --overdue-none: Overdue as it is with nothing late — the mock always has some — for
            //  a picture of its own red wash beside the others)
            if (OverdueNonePicture) return (0, "Nothing overdue", false);
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
    // (0.2.9's, as the Mac's 1.3.77: pressed, a counter's card grows in place into its panel over the page, and the panel
    //  folds back into the card as it closes — its ×, a click on its heading, Escape, the card again, or a press anywhere
    //  else, which still does what it does, so the panel never holds the window. It is drawn in the shell's overlay over
    //  the page, WorkPreview.Layer, under any quick look, and goes with its card as the page scrolls; nothing under it
    //  moves, and it goes with the page when another place is opened)

    private string? openKey;
    private bool sheetAsked;

    /// <summary>The screenshot suite's --open sheetpreview (with --sheet): the open panel's first row of work, whose quick
    /// look is opened once it is on screen — the card a counter's row opens, for a picture.</summary>
    private static bool sheetPreviewPressed;
    private FrameworkElement? sheetPreviewRow;
    private Action? sheetPreviewPress;
    private Border? frame;
    /// <summary>The open panel's card face (<see cref="CardLook"/>): there as the card starts to grow, gone as it opens.</summary>
    private FrameworkElement? frameLook;
    /// <summary>Panels folding back into their cards, still drawn: put away at once with the page.</summary>
    private readonly List<Border> folding = new();

    /// <summary>Where the panel is drawn: over the page, above every place (the shell's overlay) — this page's own until
    /// the shell has one, and in a window of its own (the Dashboard opened in a new window), whose page the shell's
    /// overlay is not over.</summary>
    private Canvas? Layer => WorkPreview.Layer is { } shell && root != null && Window.GetWindow(root) is { } here && ReferenceEquals(Window.GetWindow(shell), here) ? shell : overlay;
    private FrameworkElement? panelContent;
    private Rect panelRect;

    /// <summary>How long a card takes to grow into its panel (Windows' decelerate curve), and when — and for how long —
    /// the panel's words fade in as it grows; how long the card's face takes to leave the panel's surface as it does.</summary>
    private const double GrowMs = Motion.Slow + 67, WordsAfterMs = 90, WordsMs = Motion.Medium, LookMs = WordsAfterMs + WordsMs / 2;

    /// <summary>The screenshot suite's --open sheetmorph (with --sheet): the panel drawn as it is a quarter of the way
    /// through growing out of its card, for a picture of the grow (pictures are taken with motion off).</summary>
    private static bool MorphPicture => Launch.Get("open") == "sheetmorph";

    /// <summary>The screenshot suite's --overdue-none: Overdue drawn with nothing late, for a picture of its own wash.</summary>
    private static bool OverdueNonePicture => Shot.Asked && Launch.Flag("overdue-none");
    /// <summary>The page's scroller, listened to while the panel is up.</summary>
    private ScrollViewer? pageScroller;
    /// <summary>The screenshot suite's --open sheetscroll (with --sheet): the page under the open panel scrolled a little,
    /// once, for a picture of the panel going with its tile.</summary>
    private static bool sheetScrolled;

    private void SetOpen(string? key)
    {
        if (key == openKey) return;
        if (openKey != null) ClosePanel(openKey);
        openKey = key;
        if (key != null)
        {
            Shell.Tour.Did(Shell.TourEvent.CounterOpened);
            sheet.Ensure(engine, "todaySheet", new { key });
            if (!ShowPanel(animate: true)) Unplaced();
        }
    }

    /// <summary>A panel that could not be put over its card (the page not on show, its card not laid out): not open after
    /// all — its card as it was, and the page's live check not held for it.</summary>
    private void Unplaced()
    {
        if (openKey is { } key && tiles.TryGetValue(key, out var t)) { t.BeginAnimation(UIElement.OpacityProperty, null); t.Opacity = 1; }
        openKey = null;
    }

    public override bool Escape()
    {
        if (openKey == null) return false;
        SetOpen(null);
        return true;
    }

    /// <summary>A press anywhere outside the open panel — the page round it, another card, the pane, the header, the title
    /// bar — folds it back into its card, and the press still does what it does (another card opens its own). A press on
    /// the panel, on a quick look over it (both in its layer) or on its own card (whose click folds it) leaves it.</summary>
    public override void Pressed(MouseButtonEventArgs e)
    {
        if (openKey == null || frame == null || e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is DependencyObject o && Layer is { } layer && Drag.Within(o, layer)) return;
        if (tiles.TryGetValue(openKey, out var t) && t.IsMouseOver) return;
        SetOpen(null);
    }

    /// <summary>Gone from view (another place, or a page over this one): an open panel goes with the page — put away at
    /// once, its card as it was.</summary>
    public override void Hide()
    {
        base.Hide();
        var f = frame;
        openKey = null;
        frame = null;
        panelContent = null;
        frameLook = null;
        WatchScroll(on: false);
        if (f != null) folding.Add(f);
        foreach (var x in folding) { x.BeginAnimation(UIElement.OpacityProperty, null); (x.Parent as Panel)?.Children.Remove(x); }
        folding.Clear();
        foreach (var t in tiles.Values) { t.BeginAnimation(UIElement.OpacityProperty, null); t.Opacity = 1; }
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

    /// <summary>The open counter's panel put over its card — grown out of it, or there at once — and whether it could be:
    /// not while the page is not on show, or its card not laid out.</summary>
    private bool ShowPanel(bool animate)
    {
        if (openKey == null || root == null || Layer is not { } layer) return false;
        var key = openKey;
        var tile = TileRect(key);
        var pageRect = PageRect(layer);
        if (tile.IsEmpty || pageRect.IsEmpty) return false;
        var d = today.Data;
        var c = d?.Counters.FirstOrDefault(k => k.Key == key);
        if (c == null) return false;
        if (frame != null) (frame.Parent as Panel)?.Children.Remove(frame);
        var w = PanelSpot(tile, pageRect, 0).Width;
        panelContent = PanelContent(c, key, w);
        panelContent.Width = w - 2;
        panelContent.Measure(new Size(w - 2, double.PositiveInfinity));
        var h = Math.Max(tile.Height, panelContent.DesiredSize.Height + 2);
        panelRect = PanelSpot(tile, pageRect, h);
        var look = CardLook(c);
        frameLook = look;
        frame = new Border
        {
            CornerRadius = new CornerRadius(8), ClipToBounds = true,
            Child = new Grid { Children = { Wash(CounterColor(key), card: false), look, new Canvas { Children = { panelContent } } } },
            Effect = K.Shadow(32, 10, 0.22), SnapsToDevicePixels = true,
        };
        frame.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        Shell.Tour.Mark(frame, Shell.TourSpot.CounterPanel);
        frame.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; SetOpen(null); } };
        // (under a quick look already up — one opened from Coming up stays over the panel, as it was over 0.2.9's, and
        //  the shell's Escape folds the one on top first)
        layer.Children.Insert(0, frame);
        if (tiles.TryGetValue(key, out var t)) { t.BeginAnimation(UIElement.OpacityProperty, null); t.Opacity = 0; }
        // (it goes with its tile as the page scrolls: a tile scrolled part way under the page's header opens it cut there)
        WatchScroll(on: true);
        ClipToPage(layer);
        // (the screenshot suite: --open sheetscroll scrolls the page under the panel a little, once it is up)
        if (!sheetScrolled && Launch.Get("open") == "sheetscroll") { sheetScrolled = true; ScrollForPicture(); }
        if (MorphPicture) { CatchGrowing(); return true; }
        look.Opacity = 0;
        if (!animate || !Motion.Fades)
        {
            PlaceAt(frame, panelRect);
            return true;
        }
        if (!Motion.On)
        {
            // (Windows' Animation effects off: nothing grows — the panel fades in where it opens, over its card, which
            //  goes once it is in)
            PlaceAt(frame, panelRect);
            var f = frame;
            if (t != null) t.Opacity = 1;
            Motion.Fade(f, 0, 1, Motion.Normal, () => { if (ReferenceEquals(frame, f) && tiles.TryGetValue(key, out var t1)) t1.Opacity = 0; });
            return true;
        }
        // the card grows in place into its panel: the surface grows from the card's place and size to its own — the
        // card's own face at first, giving way to the panel's — its words fading in as it opens (Windows' decelerate
        // curve), 0.2.9's
        PlaceAt(frame, tile);
        panelContent.Opacity = 0;
        Motion.FromTo(frame, Canvas.LeftProperty, tile.X, panelRect.X, GrowMs, Motion.Decelerate);
        Motion.FromTo(frame, Canvas.TopProperty, tile.Y, panelRect.Y, GrowMs, Motion.Decelerate);
        Motion.FromTo(frame, FrameworkElement.WidthProperty, tile.Width, panelRect.Width, GrowMs, Motion.Decelerate);
        Motion.FromTo(frame, FrameworkElement.HeightProperty, tile.Height, panelRect.Height, GrowMs, Motion.Decelerate);
        Motion.FromTo(look, UIElement.OpacityProperty, 1, 0, LookMs, Motion.Standard);
        Motion.FadeIn(panelContent, WordsMs, WordsAfterMs);
        Shot.Moved();
        return true;
    }

    /// <summary>
    /// A counter's card's own face — its background, its scene, its wash (or the alert's) and the edge every card has, without
    /// its words — laid in its panel's surface over the panel's own: there as the card starts to grow, gone by the time
    /// the panel's words are in, and back as the panel folds into the card, so the surface is the card's at the first
    /// frame of the grow and at the last of the fold, never jumping from one look to the other.
    /// </summary>
    private Border CardLook(Counter c)
    {
        var alert = c.Tone == "red" && (Count(c).Value ?? 0) > 0;
        var color = CounterColor(c.Key);
        var look = new Border { Child = Face(c.Key, alert, color), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), IsHitTestVisible = false };
        look.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        look.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush"); // (the card's own edge, as its button wears it)
        return look;
    }

    /// <summary>--open sheetmorph: the panel as it is a quarter of the way through growing out of its card — its surface
    /// where the grow has taken it by then, the card's face as far gone and its words as far faded in — drawn still, for
    /// a picture. Drawn again each time the panel is laid out (its list come, the page settled).</summary>
    private void CatchGrowing()
    {
        if (frame == null || panelContent == null || openKey == null) return;
        var tile = TileRect(openKey);
        if (tile.IsEmpty) return;
        const double at = GrowMs / 4;
        var p = Motion.Decelerate.Ease(at / GrowMs);
        double Step(double from, double to) => from + (to - from) * p;
        PlaceAt(frame, new Rect(Step(tile.X, panelRect.X), Step(tile.Y, panelRect.Y), Step(tile.Width, panelRect.Width), Step(tile.Height, panelRect.Height)));
        panelContent.Opacity = Motion.Decelerate.Ease(Math.Clamp((at - WordsAfterMs) / WordsMs, 0, 1));
        if (frameLook != null) frameLook.Opacity = 1 - Motion.Standard.Ease(Math.Clamp(at / LookMs, 0, 1));
    }

    /// <summary>
    /// Where the panel sits for its tile, <paramref name="h"/> tall: 440 wide (or the page's width), level with the tile's
    /// top, and placed by where its tile sits across the row — the first tile's panel from its left edge, the last one's
    /// from its right, one between them centred on it in step — kept on the page (the Mac's, 1.3.16).
    /// </summary>
    private static Rect PanelSpot(Rect tile, Rect pageRect, double h)
    {
        var w = Math.Min(440, pageRect.Width);
        var along = pageRect.Width > tile.Width ? Math.Clamp((tile.X - pageRect.X) / (pageRect.Width - tile.Width), 0, 1) : 0;
        var x = Math.Clamp(tile.X + (tile.Width - w) * along, pageRect.X, Math.Max(pageRect.X, pageRect.Right - w));
        return new Rect(x, tile.Y, w, h);
    }

    /// <summary>
    /// The panel kept on its tile, as 0.2.9's was on the page itself: as the page scrolls, or is laid out again (the
    /// window resized, the counters put in other rows), it goes where its tile has gone. True when its width changed (its
    /// words are then laid out again).
    /// </summary>
    private bool Follow(bool evenMoving = false)
    {
        if (frame == null || openKey == null || Layer is not { } layer) return false;
        // (the page still sliding into place — back from another place — has its tile where it will not stay: once it rests)
        if (!evenMoving && PageMoving()) { FollowWhenStill(); return false; }
        var tile = TileRect(openKey);
        var pageRect = PageRect(layer);
        if (tile.IsEmpty || pageRect.IsEmpty) return false;
        var to = PanelSpot(tile, pageRect, panelRect.Height);
        // (only what has changed is set: a panel still rising out of its tile goes on rising where nothing has)
        if (Math.Abs(to.X - panelRect.X) > 0.5) { frame.BeginAnimation(Canvas.LeftProperty, null); Canvas.SetLeft(frame, to.X); }
        if (Math.Abs(to.Y - panelRect.Y) > 0.5) { frame.BeginAnimation(Canvas.TopProperty, null); Canvas.SetTop(frame, to.Y); }
        var resized = Math.Abs(to.Width - panelRect.Width) > 0.5;
        if (resized) { frame.BeginAnimation(FrameworkElement.WidthProperty, null); frame.Width = to.Width; }
        panelRect = to;
        ClipToPage(layer);
        if (MorphPicture) CatchGrowing();
        return resized;
    }

    /// <summary>Whether the page is still moving into place: the shell slides a page in on a translation of one of its
    /// parents (AppShellPages.Switch), which comes to rest at none.</summary>
    private bool PageMoving()
    {
        for (DependencyObject? p = root; p != null; p = VisualTreeHelper.GetParent(p))
            if (p is UIElement { RenderTransform: TranslateTransform t } && (Math.Abs(t.X) > 0.5 || Math.Abs(t.Y) > 0.5)) return true;
        return false;
    }

    private bool followWaiting;

    /// <summary>The panel to its tile once the page has come to rest (a page's move takes about a fifth of a second; it
    /// waits no more than about half a second).</summary>
    private async void FollowWhenStill()
    {
        if (followWaiting) return;
        followWaiting = true;
        for (var i = 0; i < 12 && PageMoving(); i++) await Task.Delay(50);
        followWaiting = false;
        if (Follow(evenMoving: true)) PanelData();
    }

    /// <summary>The panel on its tile cut where the page's scroller cuts the page — under the page's header, as 0.2.9's
    /// went, its tile scrolled up there.</summary>
    private void ClipToPage(Canvas layer)
    {
        if (frame == null) return;
        var cut = 0.0;
        if (pageScroller is { IsVisible: true } s)
        {
            try { cut = s.TranslatePoint(new Point(0, 0), layer).Y - panelRect.Y; }
            catch (InvalidOperationException) { }
        }
        frame.Clip = cut > 0.5 ? new RectangleGeometry(new Rect(-10000, cut, 20000, 20000)) : null;
    }

    /// <summary>The page's scroller listened to while the panel is up (<paramref name="on"/>), let go once it closes.</summary>
    private void WatchScroll(bool on)
    {
        ScrollViewer? s = null;
        if (on) for (DependencyObject? p = root; p != null; p = VisualTreeHelper.GetParent(p)) if (p is ScrollViewer sv) { s = sv; break; }
        if (ReferenceEquals(s, pageScroller)) return;
        if (pageScroller != null) pageScroller.ScrollChanged -= PageScrolled;
        pageScroller = s;
        if (s != null) s.ScrollChanged += PageScrolled;
    }

    /// <summary>The page scrolled, or its scroller was resized: the panel on its tile goes with it.</summary>
    private void PageScrolled(object sender, ScrollChangedEventArgs e)
    {
        // (the page's own scroller: a list scrolling inside the page tells it too)
        if (!ReferenceEquals(e.OriginalSource, pageScroller)) return;
        if (Follow()) PanelData();
    }

    /// <summary>--open sheetscroll: the page scrolled 72 down a moment after the panel is up — its tile part way under the
    /// page's header, the panel with it. The picture waits for it (on a slow runner it was once taken just before).</summary>
    private async void ScrollForPicture()
    {
        Shot.Began();
        try
        {
            await Task.Delay(3000);
            pageScroller?.ScrollToVerticalOffset(72);
        }
        finally { Shot.Ended(); }
    }

    private static void PlaceAt(FrameworkElement e, Rect r)
    {
        foreach (var dp in new[] { Canvas.LeftProperty, Canvas.TopProperty, FrameworkElement.WidthProperty, FrameworkElement.HeightProperty }) e.BeginAnimation(dp, null);
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = r.Width;
        e.Height = r.Height;
    }

    /// <summary>
    /// Folded back into its card (0.2.9's): the surface shrinks to the card's place and size, its words fading first and
    /// the card's face coming back into it, then lifts off the card, whose words show through it as it goes. A folding
    /// panel lies under any panel opened meanwhile and takes no press — its own card pressed again while it folds opens
    /// it again, over it. With Windows' Animation effects off nothing shrinks: the panel fades where it is, and its card
    /// shows again under it.
    /// </summary>
    private void ClosePanel(string key)
    {
        var f = frame;
        var content = panelContent;
        var look = frameLook;
        frame = null;
        panelContent = null;
        frameLook = null;
        WatchScroll(on: false);
        if (f == null) { if (tiles.TryGetValue(key, out var t0)) { t0.BeginAnimation(UIElement.OpacityProperty, null); t0.Opacity = 1; } return; }
        var tile = TileRect(key);
        folding.Add(f);
        f.IsHitTestVisible = false;
        Panel.SetZIndex(f, -1);
        void Gone()
        {
            if (folding.Remove(f)) (f.Parent as Panel)?.Children.Remove(f);
        }
        void CardBack()
        {
            if (tiles.TryGetValue(key, out var t)) { t.BeginAnimation(UIElement.OpacityProperty, null); t.Opacity = 1; }
        }
        void Landed()
        {
            // (put away already, with the page)
            if (!folding.Contains(f)) return;
            // (opened again as it folded: its card stays hidden under the new panel)
            if (openKey == key) { Gone(); return; }
            CardBack();
            Motion.Fade(f, f.Opacity, 0, Motion.Fast, Gone);
        }
        if (!Motion.Fades) { Gone(); CardBack(); return; }
        if (!Motion.On || tile.IsEmpty)
        {
            // (nothing shrinks: its card shows again under it as it fades where it is)
            CardBack();
            Motion.Fade(f, f.Opacity, 0, Motion.Fast, Gone);
            return;
        }
        if (content != null) Motion.FadeOut(content, () => { }, Motion.Fast);
        if (look != null) Motion.To(look, UIElement.OpacityProperty, 1, Motion.Medium, Motion.Standard);
        var from = new Rect(Canvas.GetLeft(f), Canvas.GetTop(f), f.ActualWidth, f.ActualHeight);
        Motion.FromTo(f, Canvas.LeftProperty, from.X, tile.X, Motion.Medium, Motion.Standard);
        Motion.FromTo(f, Canvas.TopProperty, from.Y, tile.Y, Motion.Medium, Motion.Standard);
        Motion.FromTo(f, FrameworkElement.WidthProperty, from.Width, tile.Width, Motion.Medium, Motion.Standard);
        Motion.FromTo(f, FrameworkElement.HeightProperty, from.Height, tile.Height, Motion.Medium, Motion.Standard, 0, Landed);
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
        if (MorphPicture) CatchGrowing();
        Shot.Moved();
    }

    private FrameworkElement PanelContent(Counter c, string key, double w)
    {
        var data = sheet.Data;
        var n = Count(c, full: true);
        var alert = c.Tone == "red" && (n.Value ?? 0) > 0;
        var color = CounterColor(key);
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
        // (a click on its heading folds it back too, as 0.2.9's did — a click begun there: not the second of a double-click
        //  on its card, landing on the panel growing over it)
        head.Background = Brushes.Transparent;
        var pressed = false;
        head.MouseLeftButtonDown += (_, e) => pressed = e.ClickCount == 1;
        head.MouseLeftButtonUp += (_, e) =>
        {
            if (pressed && (e.OriginalSource is not DependencyObject o || !Drag.Within(o, close))) SetOpen(null);
            pressed = false;
        };

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
            sheetPreviewRow = null;
            foreach (var s in data.Sections) sections.Children.Add(Section(key, s));
            body = new ScrollViewer { Content = sections, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
        var all = K.V(14, head, K.Divider(), body);
        return new Border { Child = all, Padding = new Thickness(18, 14, 14, 16) };
    }

    /// <summary>
    /// A section of the panel: its title, its rows as 0.2.9 drew them — a bar of the course's colour, the title, and under
    /// it the engine's one line about it with the kind of work in it ("F26-MATH 021 20 · Quiz · 10 pts · due 11:59 PM") —
    /// each with a chevron to its quick look, or Dismiss.
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
            if (RowLine(key, row) is { Length: > 0 } line) words.Children.Add(K.Text(line, Ty.Caption, B.Text2));
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
                // (the screenshot suite: --open sheetpreview opens the first row's quick look once it is on screen — that
                //  row as it is then, the list read again or not)
                if (sheetPreviewRow == null && Launch.Get("open") == "sheetpreview")
                {
                    sheetPreviewRow = b;
                    sheetPreviewPress = () => WorkPreview.Press(WorkPreview.Sheet(r, key, engine), b);
                    b.Loaded += async (_, _) =>
                    {
                        if (sheetPreviewPressed) return;
                        sheetPreviewPressed = true;
                        await Task.Delay(1500);
                        for (var i = 0; i < 30 && sheetPreviewRow is { IsLoaded: false }; i++) await Task.Delay(100);
                        sheetPreviewPress?.Invoke();
                    };
                }
                v.Children.Add(b);
            }
            else v.Children.Add(g.Margin(8, 7, 8, 7));
        }
        return v;
    }

    /// <summary>
    /// A row's line as 0.2.9's panel read it: the engine's line (`sub`) with the kind of work after its course ("F26-MATH 021
    /// 20 · Quiz · 10 pts · due 11:59 PM", or after its "—") — the engine (2.99.33) gives the kind on its own now, for
    /// the apps that lay it out in a column of its own. Announcements, every row of one kind, keep their line as it is.
    /// </summary>
    private static string? RowLine(string key, SheetRow row)
    {
        var sub = row.Sub;
        if (key == "unread" || string.IsNullOrWhiteSpace(row.Kind) || string.IsNullOrEmpty(sub)) return sub;
        var parts = sub.Split(" · ").ToList();
        if (parts.Contains(row.Kind!)) return sub;
        var afterCourse = parts[0] == "—" || (!string.IsNullOrEmpty(row.Course) && parts[0] == row.Course);
        parts.Insert(afterCourse ? 1 : 0, row.Kind!);
        return string.Join(" · ", parts);
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

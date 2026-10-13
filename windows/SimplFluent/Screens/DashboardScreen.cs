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
    public override string? Subtitle => today.Data?.DateLine;

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
    private string? lastTitle;

    protected override bool LayoutChanges(double before, double now) =>
        CounterLayout(before, WindowWidth) != CounterLayout(now, WindowWidth) || (before >= 900) != (now >= 900) || (before >= 760) != (now >= 760) || CardCols(before) != CardCols(now);

    protected override bool WindowLayoutChanges(double before, double now) => CounterLayout(Width, before) != CounterLayout(Width, now);

    protected override FrameworkElement Build()
    {
        var d = today.Data;
        if (Title != lastTitle) { lastTitle = Title; HeaderChange(); }
        if (d == null) return K.LoadState(today.Error, () => today.Reload(engine, "today"));
        var width = Math.Max(320, Width);
        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 28 };
        var look = (Appearance.Dark, Appearance.Accent, Appearance.ThemeName);
        var layout = CounterLayout(width, WindowWidth);
        page.Children.Add(Part("counters", new object?[] { d, countsQuick.Data, countsFresh.Data, overdueAdjust, openKey, layout, look }, () => Counters(d, layout)));
        var two = width >= 900;
        var railW = Math.Clamp(Math.Round(width * 0.3), 340, 440);
        var mainW = two ? width - railW - 28 : width;
        // (the view: what it shows, at its width; the cards are the courses' and their progress, the list the planner's,
        //  the activity the school's — not today's, so a new count leaves them as they are)
        var main = Part("main", new object?[] { View, Math.Round(mainW), courses.Data, progress.Data, courses.Error, list.Data, list.Error, activity.Data, activity.Error, ticks, seenTurn, courseCardsShown, look }, () => Main(d, mainW));
        if (two)
        {
            var rail = Part("rail", new object?[] { d, skyFresh.Data, skyKept.Data, View, railW, false, ticks, look }, () => Rail(d, railW, false));
            var g = K.Columns($"*, 28, {railW.ToString(System.Globalization.CultureInfo.InvariantCulture)}", main.Col(0), rail.Col(2));
            page.Children.Add(g);
        }
        else
        {
            if (View != 1) page.Children.Add(Part("today", new object?[] { d, ticks, look }, () => TodaySection(d, true)));
            page.Children.Add(main);
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

    private static Color CounterColor(string key) => key switch
    {
        "today" => Color.FromRgb(0xe8, 0x4a, 0x3f), "next" => Color.FromRgb(0x2f, 0xa8, 0x4f), "unread" => Color.FromRgb(0xf7, 0x8c, 0x1a),
        "overdue" => Color.FromRgb(0xe8, 0x4a, 0x3f), "tomorrow" => Color.FromRgb(0xf2, 0xa1, 0x00), "graded" => Color.FromRgb(0x6b, 0x5c, 0xe0),
        _ => Appearance.Accent,
    };

    private static string FullName(string key, string label) => key switch
    {
        "today" => "Due today", "next" => "Next 7 days", "unread" => "Unread announcements", "overdue" => "Overdue",
        "tomorrow" => "Due tomorrow", "graded" => "Graded this week", _ => label,
    };

    /// <summary>A window this wide or wider shows the six counters in one row, however wide the page beside the
    /// navigation pane is (1024 is the pictures' usual window, the page beside the pane 640 of it).</summary>
    private const double OneRowFrom = 1024;

    /// <summary>A one-row tile's padding at its sides, its symbol's size and the room between the symbol and its words.</summary>
    private const double RowPad = 12, RowSymbol = 22, RowGap = 8;

    /// <summary>The narrowest one-row tiles' padding at their sides (the Mac's too): on a 1024 window the longest name,
    /// Next 7 days, has room at its own size in them only so — at 12 it was shrunk a little to fit.</summary>
    private const double RowPadTight = 10;

    /// <summary>
    /// How the counters are laid out for a page and a window this wide: on a window 1024 wide or wider, one row of six —
    /// told by the window, so a page that narrows as the pane or the rail takes room never puts them back in two rows —
    /// and whether each of those tiles has room for its symbol beside its name (else the symbol goes beside its number);
    /// on a narrower window, three to a row, or two on the narrowest page.
    /// </summary>
    private (int Cols, bool Roomy) CounterLayout(double page, double window)
    {
        if (window < OneRowFrom) return (page >= 520 ? 3 : 2, false);
        var words = (Math.Max(320, page) - 5 * 12) / 6 - 2 - 2 * RowPad;
        return (6, words >= RowSymbol + RowGap + WidestLabel() + 2);
    }

    /// <summary>The widest of the counters' names in the tiles' type, measured once for each set of names.</summary>
    private double WidestLabel()
    {
        var labels = string.Join("\u001f", today.Data?.Counters.Select(c => c.Label) ?? Enumerable.Empty<string>());
        if (widest.Labels != labels) widest = (labels, today.Data?.Counters.Select(c => WordsWidth(c.Label, Ty.Body)).DefaultIfEmpty(0).Max() ?? 0);
        return widest.Width;
    }

    private (string? Labels, double Width) widest;

    /// <summary>How wide a line of words is in a step of Windows' type ramp, as a TextBlock lays it out.</summary>
    private static double WordsWidth(string s, Ty ty)
    {
        var (size, weight, face, _) = K.Ramp(ty);
        return new FormattedText(s, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(face, FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
    }

    private FrameworkElement Counters(Today d, (int Cols, bool Roomy) layout)
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
            var tile = cols == 6 ? RowTile(c, layout.Roomy) : Tile(c);
            Grid.SetColumn(tile, (k % cols) * 2);
            Grid.SetRow(tile, (k / cols) * 2);
            grid.Children.Add(tile);
            tiles[c.Key] = tile;
            if (c.Key == "next") Shell.Tour.Mark(tile, Shell.TourSpot.CounterNext);
            if (openKey == c.Key) tile.Opacity = 0;
        }
        return grid;
    }

    /// <summary>A counter's tile three to a row (two on the narrowest page): its symbol and name, its number, the line under it.</summary>
    private Button Tile(Counter c)
    {
        var (value, note) = Count(c);
        var alert = c.Tone == "red" && (value ?? 0) > 0;
        var color = CounterColor(c.Key);
        var head = K.Columns("auto, *", K.IconTile(Glyph.Counter(c.Key), color, 28).Col(0), K.Text(c.Label, Ty.Body, B.Text2).Margin(10, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1));
        var count = K.Text(value?.ToString() ?? "–", Ty.Count, value == null ? B.Text3 : alert ? B.Critical : B.Text);
        count.Margin = new Thickness(0, 10, 0, 0);
        var noteT = K.Text(note ?? " ", Ty.Caption, B.Text2);
        var content = K.V(0, head, count, noteT);
        content.Margin = new Thickness(14, 14, 14, 12);
        return TileButton(c, content, value, alert, color);
    }

    /// <summary>
    /// A counter's tile in one row of six, made for it — two lines, about 72 tall, so the work under the counters stays
    /// in view: its name (never cut short) over its number, the symbol beside the name where both fit; where they do
    /// not (the narrowest tiles, on a window not much over 1024), beside the number instead. The line about the count
    /// follows the number, cut short at its end, and is left out where too little of it would show.
    /// </summary>
    private Button RowTile(Counter c, bool roomy)
    {
        var (value, note) = Count(c);
        var alert = c.Tone == "red" && (value ?? 0) > 0;
        var color = CounterColor(c.Key);
        var symbol = K.IconTile(Glyph.Counter(c.Key), color, RowSymbol);
        var label = K.Text(c.Label, Ty.Body, B.Text2);
        label.TextTrimming = TextTrimming.None;
        // (a name wider than its room, were there ever one, shrunk to fit rather than cut; a number likewise)
        var name = new Viewbox { Child = label, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var head = roomy ? K.Columns("auto, *", symbol.Col(0), name.Margin(RowGap, 0, 0, 0).Col(1)) : K.Columns("*", name);
        head.Height = RowSymbol;
        var count = K.Text(value?.ToString() ?? "–", Ty.Count, value == null ? B.Text3 : alert ? B.Critical : B.Text);
        count.FontSize = 28;
        count.LineHeight = 32;
        count.TextTrimming = TextTrimming.None;
        Grid line;
        if (roomy)
        {
            line = K.Columns("auto, *", count.Col(0));
            if (!string.IsNullOrEmpty(note))
            {
                var noteT = K.Text(note!, Ty.Caption, B.Text2).Margin(RowGap, 0, 0, 5).Align(HorizontalAlignment.Stretch, VerticalAlignment.Bottom).Col(1);
                // (shown when all of it fits, or enough to say something — about nine letters)
                var enough = Math.Min(WordsWidth(note!, Ty.Caption), 56);
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
        content.Margin = roomy ? new Thickness(RowPad, 8, RowPad, 8) : new Thickness(RowPadTight, 8, RowPadTight, 8);
        return TileButton(c, content, value, alert, color);
    }

    /// <summary>A counter's tile round its words: its colour washed across it (or its theme's picture, or the alert's red),
    /// opening its panel when pressed, its full name its tooltip and what a screen reader says with its count.</summary>
    private Button TileButton(Counter c, FrameworkElement content, int? value, bool alert, Color color)
    {
        // (its theme's scene, or a photo of the student's, under its words — Settings ▸ Appearance)
        var face = new Grid();
        if (Scenes.Backdrop(c.Key) is { } scene) face.Children.Add(scene);
        else if (!alert) face.Children.Add(Wash(color));
        face.Children.Add(content);
        face.SizeChanged += (_, e) => face.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        var b = K.CardButton(face, () => SetOpen(openKey == c.Key ? null : c.Key), 8, new Thickness(0));
        if (alert) b.SetResourceReference(Control.BackgroundProperty, B.CriticalBack);
        b.ToolTip = FullName(c.Key, c.Label);
        System.Windows.Automation.AutomationProperties.SetName(b, $"{FullName(c.Key, c.Label)}: {value?.ToString() ?? "counting"}");
        return b;
    }

    /// <summary>A counter's colour washed across its card (Windows 11's tinted cards): strongest at its symbol's corner,
    /// fading towards the far one — the panel it grows into wears the same.</summary>
    private static Border Wash(Color c) => new()
    {
        Background = new LinearGradientBrush(K.WithAlpha(c, 0.24), K.WithAlpha(c, 0.05), new Point(0, 0), new Point(1, 1)),
        CornerRadius = new CornerRadius(7), IsHitTestVisible = false,
    };

    /// <summary>A counter's number and note: never one from before — Overdue and Graded from the counts made on their own, the rest from Today.</summary>
    private (int? Value, string? Note) Count(Counter c)
    {
        var fresh = countsFresh.Data;
        var quick = countsQuick.Data;
        if (c.Key == "overdue")
        {
            var k = fresh ?? quick;
            if (k?.Overdue is { } o)
            {
                var n = Math.Max(0, o + overdueAdjust);
                return (n, overdueAdjust != 0 ? (n > 0 ? $"{n} not submitted" : "Nothing overdue") : k.OverdueNote);
            }
            return (null, null);
        }
        if (c.Key == "graded")
        {
            var k = fresh ?? quick;
            return k?.Graded is { } g ? (g, k.GradedNote) : (null, null);
        }
        if (today.Data == null || c.Pending == true) return (null, c.Note);
        return (c.Value, c.Note);
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

    private FrameworkElement PanelContent(Counter c, string key, double w)
    {
        var data = sheet.Data;
        var (value, note) = Count(c);
        var alert = c.Tone == "red" && (value ?? 0) > 0;
        var color = CounterColor(key);
        var close = K.IconButton(SymbolRegular.Dismiss24, () => SetOpen(null), "Close", 14, 32);
        var titleRow = K.Columns("auto, *, auto",
            K.IconTile(Glyph.Counter(key), color, 28).Col(0),
            K.Text(data?.Title ?? FullName(key, c.Label), Ty.BodyStrong).Margin(10, 0, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
            close.Col(2));
        var count = K.Text(value?.ToString() ?? "–", Ty.TitleLarge, value == null ? B.Text3 : alert ? B.Critical : B.Text);
        var line = data?.Note ?? note;
        var countRow = K.Columns("auto, *", count.Col(0));
        if (!string.IsNullOrEmpty(line)) countRow.Children.Add(K.Wrapped(line!, Ty.Body, B.Text2, 2).Margin(14, 0, 0, 6).Align(HorizontalAlignment.Stretch, VerticalAlignment.Bottom).Col(1));
        var head = K.V(4, titleRow, countRow);
        // (a click on the header folds it back too)
        head.Background = Brushes.Transparent;
        head.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not DependencyObject o || !IsInside(o, close)) SetOpen(null); };

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

    private static bool IsInside(DependencyObject o, DependencyObject container)
    {
        for (var p = o; p != null; p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p)) if (p == container) return true;
        return false;
    }

    /// <summary>A section of the panel: its title, its rows (a bar of the course's colour, the title, the line, a chevron or Dismiss).</summary>
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

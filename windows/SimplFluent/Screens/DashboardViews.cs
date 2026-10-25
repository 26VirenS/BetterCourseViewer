using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

public sealed partial class DashboardScreen
{
    private static bool previewShot, previewPressed;
    /// <summary>(the screenshot suite's --open preview) the first row's id, and its quick look as that row is now: the
    /// page can be drawn again between the row's coming and the press.</summary>
    private static string? previewId;
    private static FrameworkElement? previewRow;
    private static Action? previewPress;

    private FrameworkElement Main(Today d, double w) => View switch
    {
        0 => CoursesSection(w),
        2 => ActivitySection(w),
        _ => Upcoming(w),
    };

    // ---- Coming up: the List view (the Mac's DashDayList) ----

    private FrameworkElement Upcoming(double w)
    {
        var l = list.Data;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var heading = UpcomingHeading(l);
        v.Children.Add(heading);
        if (l == null)
        {
            v.Children.Add(list.Error is { } err ? K.Empty(SymbolRegular.ErrorCircle24, err) : K.Reading("Reading your planner…"));
            return v;
        }
        if (l.Days.Count == 0) { v.Children.Add(K.Card(K.Empty(SymbolRegular.WeatherSunny24, l.Empty ?? "Nothing coming up."))); return v; }
        var wide = w >= 760;
        var days = new Stack { Orientation = Orientation.Vertical, Spacing = 22 };
        var blocks = new List<DayRefs>();
        foreach (var day in l.Days)
        {
            var b = DayBlock(day, wide);
            days.Children.Add(b.Block);
            blocks.Add(b);
        }
        v.Children.Add(days);
        if (noting != null) noting.Upcoming = new UpcomingRefs(v, heading, UpcomingSaid(l), days, wide, blocks);
        return v;
    }

    /// <summary>Coming up's heading: what is left to do and what is done, and Hide (or Show) completed.</summary>
    private FrameworkElement UpcomingHeading(DashListData? l)
    {
        string? trailing = l is { Total: > 0 } ? $"{l.Total - (l.Done ?? 0)} to do · {l.Done ?? 0} done" : null;
        UIElement? action = null;
        if (l != null && ((l.Done ?? 0) > 0 || l.HideDone == true))
        {
            var hiding = l.HideDone == true;
            action = K.Button(hiding ? $"Show completed · {l.Done ?? 0}" : "Hide completed", () => { hideDone = !hiding; list.Again(); Refresh(); },
                icon: hiding ? SymbolRegular.Eye24 : SymbolRegular.CheckmarkCircle24);
        }
        return Heading.Section("Coming up", trailing, action);
    }

    /// <summary>A day of Coming up: its name and date, how many items, and its rows in a card.</summary>
    private DayRefs DayBlock(DashDay day, bool wide)
    {
        // (the day's date beside its name, saying only what the name does not — none beside Today: the greeting over
        //  the counters says it already)
        var date = string.IsNullOrEmpty(day.Date) || day.Title == "Today" ? "" : day.Date!.StartsWith(day.Title + ", ") ? day.Date![(day.Title.Length + 2)..] : day.Date!;
        var count = K.Text(Items(day.Rows.Count), Ty.Caption, B.Text3);
        var head = K.Columns("auto, *, auto",
            K.Text(day.Title, Ty.BodyStrong).Col(0),
            K.Text(date, Ty.Body, B.Text2).Margin(10, 0, 0, 0).Col(1),
            count.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2));
        head.Margin = new Thickness(4, 0, 4, 8);
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var kept = RowsInto(rows, 84, day.Rows.Select(r => (r.Id, DashLook(r, day.Title, wide), (FrameworkElement)WorkLine(r, day.Title, wide))));
        return new DayRefs(DayKey(day), K.V(0, head, K.Card(rows, new Thickness(4))), count, kept);
    }

    private static string Items(int n) => $"{n} {(n == 1 ? "item" : "items")}";

    /// <summary>
    /// The Dashboard's one row of work — Coming up's and Today's alike, so the same work reads the same wherever it
    /// shows: its kind's symbol on a tile of its course's colour; its course and kind (with its flags) over its title;
    /// its points and when it is due at its right — side by side in a wide list, the time over the points where the row
    /// is narrow (the rail beside the view).
    /// </summary>
    internal static Grid WorkFace(Color color, SymbolRegular symbol, string? course, string? kind, IEnumerable<WorkFlag>? flags, TextBlock title,
        string? points, string? when, bool stacked, double tileSize = 32, double whenWidth = 0, WorkPlan? plan = null)
    {
        var tile = K.IconTile(symbol, color, tileSize);
        tile.VerticalAlignment = VerticalAlignment.Center;
        var meta = new WrapPanel { Orientation = Orientation.Horizontal };
        // (the course and the kind as two pieces, so a narrow row — the rail — takes the kind onto a second line rather
        //  than cutting it: "F26-MATH 021 20 ·" then "Assignment")
        var hasCourse = !string.IsNullOrEmpty(course);
        var hasKind = !string.IsNullOrEmpty(kind);
        if (hasCourse) meta.Children.Add(K.Text(hasKind ? course + " ·" : course!, Ty.Caption, B.Text2).Margin(0, 0, hasKind ? 4 : 8, 0));
        if (hasKind) meta.Children.Add(K.Text(kind!, Ty.Caption, B.Text2).Margin(0, 0, 8, 0));
        foreach (var f in flags ?? Enumerable.Empty<WorkFlag>()) meta.Children.Add(K.Chip(f.Word, K.Tone(f.Kind)).Margin(0, 0, 6, 0));
        var words = K.V(2, title);
        if (meta.Children.Count > 0) words.Children.Insert(0, meta);
        // (0.3.60) the time it is planned for, pulled to
        if (plan != null) words.Children.Add(PlanRail.Pill(plan).Margin(0, 2, 0, 0));
        words.VerticalAlignment = VerticalAlignment.Center;
        if (stacked)
        {
            var right = K.V(1);
            right.VerticalAlignment = VerticalAlignment.Center;
            right.MaxWidth = 150;
            if (!string.IsNullOrEmpty(when)) right.Children.Add(K.Text(when!, Ty.Body, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center));
            if (!string.IsNullOrEmpty(points)) right.Children.Add(K.Text(points!, Ty.Caption, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center));
            return K.Columns("auto, *, auto", tile.Margin(0, 0, 12, 0).Col(0), words.Col(1), right.Margin(10, 0, 0, 0).Col(2));
        }
        var due = K.Text(when ?? "", Ty.Body, B.Text2);
        due.TextAlignment = TextAlignment.Right;
        due.MinWidth = whenWidth;
        var pts = K.Text(points ?? "", Ty.Body, B.Text2);
        pts.TextAlignment = TextAlignment.Right;
        pts.MinWidth = whenWidth >= 112 && !string.IsNullOrEmpty(points) ? 64 : 0;
        return K.Columns("auto, *, auto, auto",
            tile.Margin(0, 0, 12, 0).Col(0), words.Col(1),
            pts.Margin(8, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2),
            due.Margin(8, 0, 6, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3));
    }

    /// <summary>A row of Coming up (the Mac's DashWorkLine): the check, then the Dashboard's row of work.</summary>
    private FrameworkElement WorkLine(DashRow row, string day, bool wide)
    {
        var done = Done(row.Id, row.Done);
        var check = new CheckCircle(done, done ? "Mark as not done" : "Mark as done");
        check.Toggled += on => Tick(row.Id, on);
        var title = K.Wrapped(row.Title, Ty.Body, done ? B.Text2 : B.Text, 2);
        if (done) title.Struck();
        var face = WorkFace(K.Hex(row.Color), Glyph.Work(row.Type ?? row.Kind), row.Course, row.Kind, row.Flags, title, row.Points, row.Due ?? row.Time, stacked: false, whenWidth: wide ? 112 : 90,
            plan: done ? null : row.Plan);
        var g = K.Columns("auto, *", check.Margin(6, 0, 12, 0).Col(0), face.Col(1));
        // (a click: its quick look, grown out of the row; a double-click: the work whole)
        var b = K.Subtle(g, null, new Thickness(4, 10, 4, 10));
        b.Click += (_, _) => WorkPreview.Press(WorkPreview.Dash(row, day, engine, on => Tick(row.Id, on)), b);
        // (the screenshot suite: --open preview opens the first row's quick look once it is on screen — that row as it
        //  is then, drawn again or not)
        if (Launch.Get("open") == "preview")
        {
            if (!previewShot)
            {
                previewShot = true;
                previewId = row.Id;
            }
            if (row.Id == previewId)
            {
                previewRow = b;
                previewPress = () => WorkPreview.Press(WorkPreview.Dash(row, day, engine, on => Tick(row.Id, on)), b);
                b.Loaded += async (_, _) =>
                {
                    if (previewPressed) return;
                    previewPressed = true;
                    await Task.Delay(1500);
                    for (var i = 0; i < 30 && previewRow is { IsLoaded: false }; i++) await Task.Delay(100);
                    previewPress?.Invoke();
                };
            }
        }
        if (done) return b;
        // (0.3.60) pulled to the right, the free times hang under it, and letting go on one plans it: the row held in a
        // holder that takes the mouse from it, so a pull is never also its click
        var holder = new Grid { Background = Brushes.Transparent, Children = { b } };
        PlanRail.Attach(holder, row.Id, row.Title, engine);
        var menu = new ContextMenu();
        holder.ContextMenu = menu;
        holder.ContextMenuOpening += (_, _) =>
        {
            menu.Items.Clear();
            var planned = row.Plan != null;
            menu.Items.Add(CalendarKit.Item(planned ? "Move Plan…" : "Plan a Time…", SymbolRegular.Clock24, () => PlanRail.Open(holder, row.Id, row.Title, engine)));
            if (planned) menu.Items.Add(CalendarKit.Item("Clear Plan", SymbolRegular.CalendarCancel24, () => PlanRail.Clear(row.Id, engine)));
        };
        return holder;
    }

    // ---- Cards: the courses (the Mac's DashCourseCard, as Windows 11 cards) ----

    private static int CardCols(double w) => Math.Max(1, (int)Math.Floor((Math.Max(320, w) * (w >= 900 ? 0.7 : 1) + 16) / (264 + 16)));

    /// <summary>The cards have come in: true from the start (the page comes in as a whole); a switch to Cards brings them in one after another.</summary>
    private bool courseCardsShown = true;

    private FrameworkElement CoursesSection(double w)
    {
        var c = courses.Data;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        v.Children.Add(Heading.Section("Courses", c == null ? null : $"{c.Rows.Count} {(c.Rows.Count == 1 ? "course" : "courses")}",
            K.Link("All courses", () => engine.Go(Place.Courses))));
        if (c == null)
        {
            v.Children.Add(courses.Error is { } err ? K.Empty(SymbolRegular.ErrorCircle24, err) : K.Reading("Reading your courses…"));
            return v;
        }
        if (c.Rows.Count == 0) { v.Children.Add(K.Card(K.Empty(SymbolRegular.BookOpen24, c.Empty ?? "No courses chosen yet."))); return v; }
        const double gap = 16;
        var cols = Math.Max(1, (int)Math.Floor((w + gap) / (264 + gap)));
        var grid = new Grid();
        for (var i = 0; i < cols; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var rowsN = (c.Rows.Count + cols - 1) / cols;
        for (var r = 0; r < rowsN; r++)
        {
            if (r > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(gap) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        var cardW = (w - gap * (cols - 1)) / cols;
        var made = new List<(string Id, string Look, FrameworkElement Card)>();
        for (var k = 0; k < c.Rows.Count; k++)
        {
            var card = CourseCard(c.Rows[k], cardW);
            Grid.SetColumn(card, (k % cols) * 2);
            Grid.SetRow(card, (k / cols) * 2);
            grid.Children.Add(card);
            made.Add((c.Rows[k].Id, CardLook(c.Rows[k], cardW), card));
        }
        v.Children.Add(grid);
        if (noting != null) noting.Cards = new CardsRefs(cardW, made);
        // (the cards come in one after another when Cards is chosen, not with the page, nor at every answer or count)
        if (!courseCardsShown) Motion.Stagger(grid.Children.OfType<UIElement>(), 40, 20);
        courseCardsShown = true;
        return v;
    }

    private static readonly HashSet<string> NativeSections = new() { "announcements", "assignments", "discussions", "files", "grades", "modules", "pages", "people", "quizzes", "syllabus" };

    /// <summary>Cards this wide or wider name their quick links beside their symbols; narrower ones have the symbols, each
    /// named in its tooltip.</summary>
    private const double LabelledLinksFrom = 400;

    /// <summary>
    /// A course as a card (the Mac's DashCourseCard): a band of its colour with the name the pane gives it and what wants
    /// attention (work due today, "2 new" announcements); under it its term and teacher — and its other name only where it
    /// says something new — its score in a ring, how much is handed in, what is next, and its quick links.
    /// </summary>
    private FrameworkElement CourseCard(DashCourse course, double cardW)
    {
        var color = K.Hex(course.Color);
        // (the course's name whole: a name longer than the band's one line takes a second rather than an ellipsis)
        var heroWords = new TextBlock
        {
            Text = course.Code, FontSize = 16, FontWeight = FontWeights.SemiBold, FontFamily = K.DisplayFace, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 20, MaxHeight = 40,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 1),
        };
        // (what wants attention, in words — "2 due today", "2 new" — or, where the name and the words do not both fit in the
        //  band, as its symbol and number, the words kept in the tooltip and for a screen reader: the course's name is
        //  never the one cut; failing that too, the pills leave the band for a line of their own under it)
        Stack Badges(bool brief, bool onCard = false)
        {
            // (on the band: white pills with coloured words; under it, on the card, the colour filled with white words)
            Border Make(string text, Color tone, SymbolRegular? icon) => onCard ? Pill(text, tone, Colors.White, icon) : Pill(text, Colors.White, tone, icon);
            var row = K.H(6);
            row.VerticalAlignment = VerticalAlignment.Center;
            if (course.DueToday is > 0)
            {
                var said = $"{course.DueToday} due today";
                var due = Make(brief ? $"{course.DueToday}" : said, CounterColor("today"), brief ? SymbolRegular.Clock24 : null);
                due.ToolTip = said;
                System.Windows.Automation.AutomationProperties.SetName(due, said);
                row.Children.Add(due);
            }
            if (course.Unread is > 0)
            {
                var n = course.Unread.Value;
                var said = string.IsNullOrEmpty(course.UnreadLabel) ? $"{n} unread {(n == 1 ? "announcement" : "announcements")}" : course.UnreadLabel!;
                var unread = Make(brief ? $"{n}" : string.IsNullOrEmpty(course.UnreadText) ? $"{n} new" : course.UnreadText!, color, SymbolRegular.Megaphone24);
                unread.ToolTip = said;
                System.Windows.Automation.AutomationProperties.SetName(unread, said);
                row.Children.Add(unread);
            }
            return row;
        }
        static double Wide(FrameworkElement e)
        {
            e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return e.DesiredSize.Width;
        }
        var nameWidth = new FormattedText(course.Code, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(K.DisplayFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 16, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
        var band = cardW - heroWords.Margin.Left - heroWords.Margin.Right - 12;
        var badges = Badges(brief: false);
        Stack? under = null;
        if (badges.Children.Count > 0 && nameWidth + Wide(badges) > band)
        {
            badges = Badges(brief: true);
            if (nameWidth + Wide(badges) > band) { under = Badges(brief: false, onCard: true); badges = K.H(6); }
        }
        badges.Margin = new Thickness(0, 0, 12, 0);
        var hero = new Grid
        {
            Height = 48,
            Background = new LinearGradientBrush(color, K.Mix(color, Colors.Black, 0.18), 90),
        };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.Children.Add(heroWords.Col(0));
        hero.Children.Add(badges.Col(1));
        // its other name where it says something new, its term and teacher; its score in a ring
        var ringInside = K.V(0, K.Text(string.IsNullOrEmpty(course.ScoreText) ? "–" : course.ScoreText!, Ty.Caption, course.Score == null ? B.Text2 : B.Text).Strong().Center());
        if (!string.IsNullOrEmpty(course.Grade)) ringInside.Children.Add(K.Text(course.Grade!, Ty.Caption, B.Text2).Center());
        ringInside.VerticalAlignment = VerticalAlignment.Center;
        // (on a track in the course's own colour, as the Courses page's card has it; with no score, why in its tooltip over
        //  the whole ring, not only its stroke)
        var ring = K.Ring(course.Score is { } s ? s / 100 : null, color, 56, 5, ringInside, tinted: true);
        if (course.Score == null && !string.IsNullOrEmpty(course.ScoreWhy))
        {
            ring.ToolTip = course.ScoreWhy;
            if (ring is Panel face) face.Background = Brushes.Transparent;
        }
        var names = K.V(2);
        if (!string.IsNullOrEmpty(course.AltName)) names.Children.Add(K.Text(course.AltName!, Ty.BodyStrong));
        if (!string.IsNullOrEmpty(course.Sub)) names.Children.Add(K.Wrapped(course.Sub!, Ty.Body, B.Text2, 2));
        names.VerticalAlignment = VerticalAlignment.Center;
        var top = K.Columns("*, auto", names.Margin(0, 0, 12, 0).Col(0), ring.Col(1));
        // how much is handed in
        var p = progress.Data != null ? (progress.Data.TryGetValue(course.Id, out var pr) ? pr : new CourseProgress { Done = 0, Total = 0 }) : null;
        var handed = p == null ? "Counting…" : p.Total > 0 ? $"{p.Done} of {p.Total} handed in" : "Nothing to hand in";
        var bar = K.Columns("*, auto", K.Bar(p is { Total: > 0 } ? (double)p.Done / p.Total : 0, color).Col(0), K.Text(handed, Ty.Caption, B.Text2).Margin(10, 0, 0, 0).Col(1));
        // what is next
        FrameworkElement next;
        if (course.Next is { } nx)
        {
            var line = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            K.Style(line, Ty.Body);
            line.Inlines.Add(new System.Windows.Documents.Run("Next  ") { Foreground = Res(B.Text2) });
            line.Inlines.Add(new System.Windows.Documents.Run(nx.Title) { FontWeight = FontWeights.SemiBold, Foreground = Res(B.Text) });
            if (!string.IsNullOrEmpty(nx.When)) line.Inlines.Add(new System.Windows.Documents.Run("  ·  " + nx.When) { Foreground = Res(B.Text2) });
            next = line;
        }
        else next = K.Text("Nothing due", Ty.Body, B.Text2);
        // its quick links: named beside their symbols on a wide card, by their tooltips on a narrow one
        var labelled = cardW >= LabelledLinksFrom;
        var links = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var link in (course.Links ?? new()).Take(5))
        {
            var lk = link;
            void Go()
            {
                if (lk.Kind is { } kind && NativeSections.Contains(kind)) engine.Go(Place.Section("courses/" + course.Id, kind));
                else if (lk.Url != null) engine.OpenWeb(lk.Url, lk.Label ?? course.Code);
            }
            var name = link.Label ?? ScreenFactory.SectionName(engine, "courses/" + course.Id, link.Kind);
            if (labelled)
            {
                var b = K.Subtle(K.H(6, K.Icon(Glyph.Section(link.Kind), 16, B.Text2), K.Text(name, Ty.Caption, B.Text2)), Go, new Thickness(8, 6, 10, 6));
                b.ToolTip = name;
                System.Windows.Automation.AutomationProperties.SetName(b, name);
                links.Children.Add(b);
            }
            else links.Children.Add(K.IconButton(Glyph.Section(link.Kind), Go, name, 16, 32));
        }
        var bodyStack = K.V(12, top, bar, next);
        if (under != null) { under.HorizontalAlignment = HorizontalAlignment.Left; bodyStack.Children.Insert(0, under); }
        if (links.Children.Count > 0) bodyStack.Children.Add(links.Margin(-8, -4, 0, -6));
        bodyStack.Margin = new Thickness(16, 12, 16, 14);
        var content = K.Rows("auto, auto", hero.Row(0), bodyStack.Row(1));
        var card = K.CardButton(content, () => engine.OpenWeb(course.Url, course.Code), 8, new Thickness(0));
        Ahead.On(card, build => engine.PrepareWeb(course.Url, build));
        card.VerticalContentAlignment = VerticalAlignment.Top;
        // (the hero's corners follow the card's)
        hero.Clip = null;
        content.SizeChanged += (_, e) => content.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        card.ToolTip = string.IsNullOrEmpty(course.AltName) ? course.Code : $"{course.Code} · {course.AltName}";
        return card;
    }

    private static Brush Res(string key) => (Brush)(Application.Current.TryFindResource(key) ?? Brushes.Gray);

    private static Border Pill(string text, Color back, Color fore, SymbolRegular? icon = null)
    {
        var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = K.Brush(fore), VerticalAlignment = VerticalAlignment.Center };
        var row = K.H(4, icon is { } i ? K.Icon(i, 12, fore, filled: true) : null, t);
        return new Border { Background = K.Brush(back), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 3), Child = row };
    }

    // ---- Activity (the Mac's DashActivityList) ----

    private FrameworkElement ActivitySection(double w)
    {
        var a = activity.Data;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var heading = Heading.Section("Recent activity", ActivitySaid(a));
        v.Children.Add(heading);
        if (a == null)
        {
            v.Children.Add(activity.Error is { } err ? K.Empty(SymbolRegular.ErrorCircle24, err) : K.Reading("Reading recent activity…"));
            return v;
        }
        if (a.Rows.Count == 0) { v.Children.Add(K.Card(K.Empty(SymbolRegular.Clock24, a.Empty ?? "No recent activity."))); return v; }
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var kept = RowsInto(rows, 72, a.Rows.Select(r => (r.Id, ActivityLook(r), (FrameworkElement)ActivityRow(r))));
        v.Children.Add(K.Card(rows, new Thickness(4)));
        if (noting != null) noting.Activity = new ActivityRefs(v, heading, ActivitySaid(a), kept);
        return v;
    }

    /// <summary>Recent activity's heading line: how many are new ("3 new"), or none.</summary>
    private string? ActivitySaid(DashActivityData? a)
    {
        var fresh = a?.Rows.Count(r => r.Unread == true && !seen.Contains(r.Id)) ?? 0;
        return fresh > 0 ? $"{fresh} new" : null;
    }

    private FrameworkElement ActivityRow(DashActivityRow row)
    {
        var unread = row.Unread == true && !seen.Contains(row.Id);
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 14, 8, 0), Visibility = unread ? Visibility.Visible : Visibility.Hidden };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, B.Accent);
        var tile = K.IconTile(Glyph.Activity(row.Type), K.Hex(row.Color), 34);
        tile.VerticalAlignment = VerticalAlignment.Top;
        var title = K.Wrapped(row.Title, Ty.Body, B.Text, 2);
        if (unread) title.FontWeight = FontWeights.SemiBold;
        var head = K.Columns("*, auto", title.Col(0), K.Text(row.When ?? "", Ty.Caption, B.Text2).Margin(8, 2, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Top).Col(1));
        var words = K.V(2, head);
        var sub = string.Join(" · ", new[] { row.Kind, row.Course }.Where(s => !string.IsNullOrEmpty(s)));
        if (sub.Length > 0) words.Children.Add(K.Text(sub, Ty.Caption, B.Text2));
        if (!string.IsNullOrEmpty(row.Preview)) words.Children.Add(K.Wrapped(row.Preview!, Ty.Caption, B.Text2, 2));
        var g = K.Columns("auto, auto, *", dot.Col(0), tile.Margin(0, 0, 12, 0).Col(1), words.Col(2));
        var r = row;
        return K.Subtle(g, () => OpenActivity(r), new Thickness(4, 10, 10, 10));
    }

    /// <summary>An activity item opened: its dot goes (here and on the web Dashboard), and it opens.</summary>
    private void OpenActivity(DashActivityRow row)
    {
        if (row.Unread == true && seen.Add(row.Id)) { seenTurn++; _ = engine.Act("dashSeen", new { id = row.Id }); Render(); }
        if (!string.IsNullOrEmpty(row.Url)) engine.OpenWeb(row.Url!, row.Title);
    }
}

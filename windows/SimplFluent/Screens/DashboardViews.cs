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
        string? trailing = l is { Total: > 0 } ? $"{l.Total - (l.Done ?? 0)} to do · {l.Done ?? 0} done" : null;
        UIElement? action = null;
        if (l != null && ((l.Done ?? 0) > 0 || l.HideDone == true))
        {
            var hiding = l.HideDone == true;
            action = K.Button(hiding ? $"Show completed · {l.Done ?? 0}" : "Hide completed", () => { hideDone = !hiding; list.Again(); Refresh(); },
                icon: hiding ? SymbolRegular.Eye24 : SymbolRegular.CheckmarkCircle24);
        }
        v.Children.Add(Heading.Section("Coming up", trailing, action));
        if (l == null)
        {
            v.Children.Add(list.Error is { } err ? K.Empty(SymbolRegular.ErrorCircle24, err) : K.Reading("Reading your planner…"));
            return v;
        }
        if (l.Days.Count == 0) { v.Children.Add(K.Card(K.Empty(SymbolRegular.WeatherSunny24, l.Empty ?? "Nothing coming up."))); return v; }
        var wide = w >= 760;
        var days = new Stack { Orientation = Orientation.Vertical, Spacing = 22 };
        foreach (var day in l.Days)
        {
            var date = string.IsNullOrEmpty(day.Date) ? "" : day.Date!.StartsWith(day.Title + ", ") ? day.Date![(day.Title.Length + 2)..] : day.Date!;
            var head = K.Columns("auto, *, auto",
                K.Text(day.Title, Ty.BodyStrong).Col(0),
                K.Text(date, Ty.Body, B.Text2).Margin(10, 0, 0, 0).Col(1),
                K.Text($"{day.Rows.Count} {(day.Rows.Count == 1 ? "item" : "items")}", Ty.Caption, B.Text3).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2));
            head.Margin = new Thickness(4, 0, 4, 8);
            var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
            for (var k = 0; k < day.Rows.Count; k++)
            {
                if (k > 0) rows.Children.Add(K.Divider(84));
                rows.Children.Add(WorkLine(day.Rows[k], day.Title, wide));
            }
            days.Children.Add(K.V(0, head, K.Card(rows, new Thickness(4))));
        }
        v.Children.Add(days);
        return v;
    }

    /// <summary>The Mac's DashWorkLine: the check; its kind on a tile of its course's colour; its course and kind with its
    /// flags over its title; its points; when it is due.</summary>
    private FrameworkElement WorkLine(DashRow row, string day, bool wide)
    {
        var done = Done(row.Id, row.Done);
        var check = new CheckCircle(done, done ? "Mark as not done" : "Mark as done");
        check.Toggled += on => Tick(row.Id, on);
        var color = K.Hex(row.Color);
        var tile = K.IconTile(Glyph.Work(row.Type ?? row.Kind), color, 32);
        var sub = string.Join(" · ", new[] { row.Course, row.Kind }.Where(s => !string.IsNullOrEmpty(s)));
        var meta = new WrapPanel { Orientation = Orientation.Horizontal };
        if (sub.Length > 0) meta.Children.Add(K.Text(sub, Ty.Caption, B.Text2).Margin(0, 0, 8, 0));
        foreach (var f in row.Flags ?? new()) meta.Children.Add(K.Chip(f.Word, K.Tone(f.Kind)).Margin(0, 0, 6, 0));
        var title = K.Wrapped(row.Title, Ty.Body, done ? B.Text2 : B.Text, 2);
        if (done) title.Struck();
        var words = K.V(2, meta, title);
        words.VerticalAlignment = VerticalAlignment.Center;
        var due = K.Text(row.Due ?? row.Time ?? "", Ty.Body, B.Text2);
        due.TextAlignment = TextAlignment.Right;
        due.MinWidth = wide ? 112 : 90;
        var points = K.Text(row.Points ?? "", Ty.Body, B.Text2);
        points.TextAlignment = TextAlignment.Right;
        points.MinWidth = wide && !string.IsNullOrEmpty(row.Points) ? 64 : 0;
        var g = K.Columns("auto, auto, *, auto, auto",
            check.Margin(6, 0, 12, 0).Col(0), tile.Margin(0, 0, 12, 0).Col(1), words.Col(2),
            points.Margin(8, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3),
            due.Margin(8, 0, 6, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(4));
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
        return b;
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
        for (var k = 0; k < c.Rows.Count; k++)
        {
            var card = CourseCard(c.Rows[k]);
            Grid.SetColumn(card, (k % cols) * 2);
            Grid.SetRow(card, (k / cols) * 2);
            grid.Children.Add(card);
        }
        v.Children.Add(grid);
        // (the cards come in one after another when Cards is chosen, not with the page, nor at every answer or count)
        if (!courseCardsShown) Motion.Stagger(grid.Children.OfType<UIElement>(), 40, 20);
        courseCardsShown = true;
        return v;
    }

    private static readonly HashSet<string> NativeSections = new() { "announcements", "assignments", "discussions", "files", "grades", "modules", "pages", "people", "quizzes", "syllabus" };

    private FrameworkElement CourseCard(DashCourse course)
    {
        var color = K.Hex(course.Color);
        // its colour at the top, its code on it, what wants attention at its corner
        var heroWords = new TextBlock { Text = course.Code, FontSize = 20, FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxHeight = 52, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 16, 12) };
        var badges = K.H(6);
        badges.HorizontalAlignment = HorizontalAlignment.Right;
        badges.VerticalAlignment = VerticalAlignment.Top;
        badges.Margin = new Thickness(0, 12, 12, 0);
        if (course.DueToday is > 0) badges.Children.Add(Pill($"{course.DueToday} due today", Color.FromRgb(0xe8, 0x4a, 0x3f), Colors.White));
        if (course.Unread is > 0) badges.Children.Add(Pill(course.Unread.Value.ToString(), Colors.White, color, SymbolRegular.Megaphone24));
        var hero = new Grid
        {
            Height = 96,
            Background = new LinearGradientBrush(color, K.Mix(color, Colors.Black, 0.18), 90),
            Children = { heroWords, badges },
        };
        // its name, term and teacher; its score in a ring
        var ringInside = K.V(0, K.Text(course.ScoreText ?? "N/A", Ty.Caption, course.Score == null ? B.Text2 : B.Text).Strong().Center());
        if (!string.IsNullOrEmpty(course.Grade)) ringInside.Children.Add(K.Text(course.Grade!, Ty.Caption, B.Text2).Center());
        ringInside.VerticalAlignment = VerticalAlignment.Center;
        var ring = K.Ring(course.Score is { } s ? s / 100 : null, color, 56, 5, ringInside);
        var names = K.V(2, K.Text(course.Name ?? course.Code, Ty.BodyStrong), K.Text(course.Sub ?? " ", Ty.Caption, B.Text2));
        names.VerticalAlignment = VerticalAlignment.Center;
        var top = K.Columns("*, auto", names.Margin(0, 0, 12, 0).Col(0), ring.Col(1));
        // how much is handed in
        var p = progress.Data != null ? (progress.Data.TryGetValue(course.Id, out var pr) ? pr : new CourseProgress { Done = 0, Total = 0 }) : null;
        var handed = p == null ? "Counting…" : p.Total > 0 ? $"{p.Done} of {p.Total} handed in" : "Nothing to hand in";
        var bar = K.Columns("*, auto", K.Bar(p is { Total: > 0 } ? (double)p.Done / p.Total : 0, color).Col(0), K.Text(handed, Ty.Caption, B.Text2).Margin(10, 0, 0, 0).Col(1));
        // what is next
        FrameworkElement next;
        if (course.Next is { } n)
        {
            var line = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            K.Style(line, Ty.Body);
            line.Inlines.Add(new System.Windows.Documents.Run("Next  ") { Foreground = Res(B.Text2) });
            line.Inlines.Add(new System.Windows.Documents.Run(n.Title) { FontWeight = FontWeights.SemiBold, Foreground = Res(B.Text) });
            if (!string.IsNullOrEmpty(n.When)) line.Inlines.Add(new System.Windows.Documents.Run("  ·  " + n.When) { Foreground = Res(B.Text2) });
            next = line;
        }
        else next = K.Text("Nothing due", Ty.Body, B.Text2);
        // its quick links
        var links = K.H(2);
        foreach (var link in (course.Links ?? new()).Take(5))
        {
            var lk = link;
            var b = K.IconButton(Glyph.Section(link.Kind), () =>
            {
                if (lk.Kind is { } kind && NativeSections.Contains(kind)) engine.Go(Place.Section("courses/" + course.Id, kind));
                else if (lk.Url != null) engine.OpenWeb(lk.Url, lk.Label ?? course.Code);
            }, link.Label ?? link.Kind ?? "", 16, 32);
            links.Children.Add(b);
        }
        var bodyStack = K.V(12, top, bar, next);
        if (links.Children.Count > 0) bodyStack.Children.Add(links.Margin(-8, -4, 0, -6));
        bodyStack.Margin = new Thickness(16, 12, 16, 14);
        var content = K.Rows("auto, auto", hero.Row(0), bodyStack.Row(1));
        var card = K.CardButton(content, () => engine.OpenWeb(course.Url, course.Code), 8, new Thickness(0));
        Ahead.On(card, build => engine.PrepareWeb(course.Url, build));
        card.VerticalContentAlignment = VerticalAlignment.Top;
        // (the hero's corners follow the card's)
        hero.Clip = null;
        content.SizeChanged += (_, e) => content.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        card.ToolTip = course.Name ?? course.Code;
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
        var fresh = a?.Rows.Count(r => r.Unread == true && !seen.Contains(r.Id)) ?? 0;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        v.Children.Add(Heading.Section("Recent activity", fresh > 0 ? $"{fresh} new" : null));
        if (a == null)
        {
            v.Children.Add(activity.Error is { } err ? K.Empty(SymbolRegular.ErrorCircle24, err) : K.Reading("Reading recent activity…"));
            return v;
        }
        if (a.Rows.Count == 0) { v.Children.Add(K.Card(K.Empty(SymbolRegular.Clock24, a.Empty ?? "No recent activity."))); return v; }
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        for (var k = 0; k < a.Rows.Count; k++)
        {
            if (k > 0) rows.Children.Add(K.Divider(72));
            rows.Children.Add(ActivityRow(a.Rows[k]));
        }
        v.Children.Add(K.Card(rows, new Thickness(4)));
        return v;
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

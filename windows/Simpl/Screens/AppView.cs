using Simpl.Components;
using Simpl.Engine;
using Simpl.Graphics;
using Simpl.UI;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.Screens;

/// <summary>
/// The window once the school is ready (the Mac's MainShell): the sidebar — Simpl's own: Dashboard, To Do, Calendar,
/// Grades, Notifications, the Inbox, Tools, each course with its sections, the account at its foot — floating in glass
/// at the left; the screen chosen there, scrolling under the toolbar; and the toolbar: Back and Forward, Refresh, the
/// screen's title and its own buttons, and Search. Drawn each frame from what the engine knows.
/// </summary>
public sealed class AppView
{
    private readonly Engine.Engine engine;
    private readonly GlassHost host;
    private readonly DashboardScreen dashboard;
    private readonly HashSet<string> open = new();
    private string query = "";
    private int lastNav = -1;

    public const float SideWidth = 268;
    public const float Bar = 38;

    public AppView(Engine.Engine engine, GlassHost host)
    {
        this.engine = engine;
        this.host = host;
        dashboard = new DashboardScreen(engine, host);
    }

    public Color4 Ground => DefaultGround(host.Ui.T);

    /// <summary>Light or dark: as asked at launch (--appearance), else as the page says, else as Windows is set.</summary>
    public static bool Dark(bool? page) => Support.Launch.Get("appearance") switch
    {
        "dark" => true,
        "light" => false,
        _ => page ?? SystemDark(),
    };

    public static bool SystemDark()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    public static Color4 DefaultGround(Theme t) => t.Dark ? new Color4(0.105f, 0.105f, 0.115f, 1) : new Color4(0.95f, 0.95f, 0.965f, 1);

    /// <summary>A frame of the window.</summary>
    public void Frame(Ui ui)
    {
        ui.T.Dark = Dark(engine.Snapshot?.Dark);
        if (engine.Phase == Phase.Starting && engine.Snapshot == null) { Opening(ui); return; }

        var content = new R(SideWidth + 16, 0, Math.Max(200, ui.W - SideWidth - 16), ui.H);
        var place = engine.Nav.Current.Place;
        if (engine.NavVersion != lastNav)
        {
            lastNav = engine.NavVersion;
            ui.ScrollTo("page", 0);
        }

        // the screen, under the toolbar
        ui.Layer(Ui.Page);
        var page = new R(content.X, 0, content.W, ui.H);
        var top = 70f;
        switch (place.Kind)
        {
            case PlaceKind.Dashboard: dashboard.Frame(ui, page, top); break;
            default: ComingNext(ui, page, top, place); break;
        }

        // the toolbar and the sidebar, over it
        ui.Layer(Ui.Chrome);
        Toolbar(ui, content, place);
        Sidebar(ui, place);
    }

    /// <summary>The school's site loading underneath, before the screens have anything to show.</summary>
    private void Opening(Ui ui)
    {
        ui.Layer(Ui.Page);
        var r = new R(ui.W / 2 - 170, ui.H / 2 - 70, 340, 140);
        ui.Glass(r, 22);
        ui.Spinner(r.CenterX, r.Y + 44, 11);
        ui.Text("Opening your school…", r.X, r.Y + 72, Theme.Headline, ui.T.Ink(), r.W, TextAlignment.Center);
        ui.Text(engine.Host, r.X, r.Y + 98, Theme.Footnote, ui.T.Ink2, r.W, TextAlignment.Center);
    }

    // ---- the toolbar ----

    private void Toolbar(Ui ui, R content, Place place)
    {
        var y = 14f;
        var x = content.X + 4;
        var nav = engine.Nav;
        var pressed = ui.IconCapsule("nav", x, y, new[] { "chevron.left", "chevron.right" }, new[] { nav.CanGoBack, nav.CanGoForward }, Bar);
        if (pressed == 0) engine.GoBack();
        if (pressed == 1) engine.GoForward();
        x += Bar * 2 + 4 + 10;
        if (ui.IconCapsule("refresh", x, y, new[] { "arrow.clockwise" }, new[] { engine.Phase == Phase.Native }, Bar) == 0) engine.Refresh();
        x += Bar + 4 + 14;
        var title = Title(place);
        var titleW = Math.Min(ui.Measure(title, Theme.Title3).X, 320);
        ui.Text(title, x, y + (Bar - ui.Measure(title, Theme.Title3).Y) / 2, Theme.Title3, ui.T.Ink(), 320);

        // Search, at the right
        var searchW = Math.Clamp(content.W * 0.26f, 200, 300);
        var sr = new R(content.Right - searchW - 14, y, searchW, Bar);
        var before = query;
        var entered = ui.TextField("search", sr, ref query, $"Search {engine.LmsName}", Theme.Callout, "magnifyingglass");
        if (query != before)
        {
            engine.Query = query;
            var t = query.Trim();
            if (t.Length >= 2) engine.Search(t);
            else if (t.Length == 0 && place.Kind == PlaceKind.Search) engine.GoBack();
        }
        if (entered && query.Trim().Length > 0) engine.Search(query.Trim());

        // the screen's own buttons, before Search
        var right = sr.X - 12;
        if (place.Kind == PlaceKind.Dashboard) dashboard.ToolbarItems(ui, right, y, x + titleW + 20);
    }

    private string Title(Place place) => place.Kind switch
    {
        PlaceKind.Dashboard => "Dashboard",
        PlaceKind.Todo => "To Do",
        PlaceKind.Calendar => "Calendar",
        PlaceKind.Grades => "Grades",
        PlaceKind.Notifications => "Notifications",
        PlaceKind.Inbox => "Inbox",
        PlaceKind.Tools => "Tools",
        PlaceKind.ExternalTools => "External Tools",
        PlaceKind.Groups => "Groups",
        PlaceKind.Courses => "All Courses",
        PlaceKind.Search => "Search",
        PlaceKind.Home => CourseName(place.Ctx) ?? "Course",
        PlaceKind.Section => (engine.Sections.TryGetValue(place.Ctx ?? "", out var ss) ? ss.FirstOrDefault(s => s.Kind == place.Arg)?.Label : null)
                             ?? Cap(place.Arg ?? ""),
        _ => "Simpl",
    };

    private string? CourseName(string? ctx)
    {
        if (ctx == null) return null;
        if (ctx.StartsWith("courses/")) return engine.Courses.FirstOrDefault(c => c.Id == ctx[8..]) is { } c ? Name(c) : null;
        if (ctx.StartsWith("groups/")) return engine.Groups.FirstOrDefault(g => g.Id == ctx[7..])?.Name;
        return null;
    }

    private static string Name(Models.CourseRow c) => !string.IsNullOrEmpty(c.Nickname) ? c.Nickname! : !string.IsNullOrEmpty(c.Name) ? c.Name! : c.Code;

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---- the sidebar ----

    private static readonly (string Label, string Icon, Place Place)[] Roots =
    {
        ("Dashboard", "square.grid.2x2", Place.Dashboard), ("To Do", "checklist", Place.Todo), ("Calendar", "calendar", Place.Calendar),
        ("Grades", "chart.bar.xaxis", Place.Grades), ("Notifications", "bell", Place.Notifications), ("Inbox", "tray", Place.Inbox),
        ("Tools", "wrench.and.screwdriver", Place.Tools), ("External Tools", "puzzlepiece.extension", Place.ExternalTools),
    };

    /// <summary>A course's sections before its home has said which it has.</summary>
    private static readonly (string Kind, string Label)[] DefaultSections =
    {
        ("announcements", "Announcements"), ("assignments", "Assignments"), ("discussions", "Discussions"), ("grades", "Grades"),
        ("modules", "Modules"), ("pages", "Pages"), ("files", "Files"), ("quizzes", "Quizzes"), ("people", "People"), ("syllabus", "Syllabus"),
    };

    private void Sidebar(Ui ui, Place current)
    {
        var panel = new R(10, 10, SideWidth - 4, ui.H - 20);
        ui.Glass(panel, 22);
        var t = ui.T;

        // the school, at the top
        var school = engine.Snapshot?.Site ?? engine.Host;
        var head = new R(panel.X + 14, panel.Y + 14, panel.W - 28, 40);
        ui.Fill(new R(head.X, head.Y + 4, 32, 32), 9, t.Accent);
        ui.TextIn(Initials(school), new R(head.X, head.Y + 4, 32, 32), Theme.Semibold(Theme.Callout), new Color4(1, 1, 1, 1));
        ui.Text(school, head.X + 42, head.Y + 4, Theme.Headline, t.Ink(), head.W - 42);
        ui.Text(engine.LmsName, head.X + 42, head.Y + 23, Theme.Caption, t.Ink2, head.W - 42);

        // the list, scrolling between the school and the account
        var list = new R(panel.X + 8, head.Bottom + 12, panel.W - 16, panel.Bottom - 62 - (head.Bottom + 12));
        var off = ui.BeginScroll("sidebar", list);
        var y = list.Y - off;
        const float rowH = 32;
        foreach (var (label, icon, place) in Roots)
        {
            if (engine.OnBrightspace && (place.Kind is PlaceKind.Inbox or PlaceKind.ExternalTools)) continue;
            var badge = !engine.CountsLive ? 0 : place.Kind switch
            {
                PlaceKind.Notifications => engine.Snapshot?.NotifUnread ?? 0,
                PlaceKind.Inbox => engine.Snapshot?.InboxUnread ?? 0,
                _ => 0,
            };
            if (SideRow(ui, "side:" + label, new R(list.X, y, list.W, rowH), icon, label, current == place, badge)) engine.Go(place);
            y += rowH + 2;
        }

        y += 14;
        ui.Text("Courses", list.X + 10, y, Theme.Semibold(Theme.Caption), t.Ink3);
        y += 22;
        foreach (var c in engine.Courses)
        {
            var ctx = "courses/" + c.Id;
            var r = new R(list.X, y, list.W, rowH);
            var on = current.Ctx == ctx && current.Kind == PlaceKind.Home;
            var i = ui.Row("side:c:" + c.Id, r, on);
            ui.Fill(new R(r.X + 12, r.CenterY - 6.5f, 13, 13), 4, Theme.Hex(c.Color));
            ui.Text(Name(c), r.X + 34, r.CenterY - 9, Theme.Body, t.Ink(), r.W - 34 - 30);
            var isOpen = open.Contains(ctx);
            if (ui.IconButton("side:c:open:" + c.Id, new R(r.Right - 28, r.Y + 4, 24, 24), isOpen ? "chevron.down" : "chevron.right", size: 11, color: t.Ink2))
            {
                if (!open.Remove(ctx)) { open.Add(ctx); _ = engine.LoadSections(ctx); }
            }
            else if (i.Clicked) engine.Go(Place.Home(ctx));
            y += rowH + 2;
            if (isOpen)
            {
                var sections = engine.Sections.TryGetValue(ctx, out var ss) && ss.Count > 0
                    ? ss.Select(s => (s.Kind, s.Label)).ToArray()
                    : DefaultSections;
                foreach (var (kind, label) in sections)
                {
                    var p = Place.Section(ctx, kind);
                    if (SideRow(ui, $"side:s:{c.Id}:{kind}", new R(list.X + 20, y, list.W - 20, 30), null, label, current == p, 0, Glyphs.Section(kind)))
                        engine.Go(p);
                    y += 32;
                }
            }
        }
        if (SideRow(ui, "side:all", new R(list.X, y, list.W, rowH), "books.vertical", "All Courses", current == Place.Courses, 0)) engine.Go(Place.Courses);
        y += rowH + 2;
        if (!engine.OnBrightspace && engine.Groups.Count > 0)
        {
            if (SideRow(ui, "side:groups", new R(list.X, y, list.W, rowH), "person.2", "Groups", current == Place.Groups, 0)) engine.Go(Place.Groups);
            y += rowH + 2;
        }
        ui.EndScroll("sidebar", list, y + off - list.Y + 8);

        // the account, at the foot
        var me = engine.Snapshot?.Me;
        var foot = new R(panel.X + 14, panel.Bottom - 50, panel.W - 28, 36);
        ui.Line(panel.X + 14, foot.Y - 8, panel.Right - 14, foot.Y - 8, t.Separator);
        ui.Circle(foot.X + 16, foot.CenterY, 15, Theme.WithAlpha(t.Accent, 0.85f));
        ui.TextIn(me?.Initials ?? Initials(me?.Name ?? "?"), new R(foot.X + 1, foot.Y + 3, 30, 30), Theme.Semibold(Theme.Footnote), new Color4(1, 1, 1, 1));
        ui.Text(me?.Name ?? "Signed in", foot.X + 40, foot.CenterY - 9, Theme.Callout, t.Ink(), foot.W - 40);
    }

    private static bool SideRow(Ui ui, string id, R r, string? icon, string label, bool on, int badge, string? glyph = null)
    {
        var i = ui.Row(id, r, on);
        var t = ui.T;
        var ink = on ? t.Ink(0.98f) : t.Ink(0.86f);
        if (icon != null) ui.Icon(icon, r.X + 20, r.CenterY, 15, on ? t.Accent : t.Ink2);
        else if (glyph != null) ui.Glyph(glyph, r.X + 20, r.CenterY, 13, t.Ink2);
        ui.Text(label, r.X + 40, r.CenterY - 9, Theme.Body, ink, r.W - 40 - (badge > 0 ? 40 : 8));
        ui.Badge(r.Right - 8, r.CenterY, badge);
        return i.Clicked;
    }

    private static string Initials(string s)
    {
        var parts = s.Split(new[] { ' ', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}",
        };
    }

    // ---- a screen not yet on Windows ----

    private void ComingNext(Ui ui, R page, float top, Place place)
    {
        var t = ui.T;
        var r = new R(page.X + Math.Max(0, (page.W - 520) / 2), top + 80, Math.Min(520, page.W - 24), 210);
        ui.Glass(r, 22);
        ui.Icon(place.Kind == PlaceKind.Section ? "doc.text" : "sparkles", r.CenterX, r.Y + 46, 30, t.Accent);
        ui.Text(Title(place), r.X, r.Y + 78, Theme.Title3, t.Ink(), r.W, TextAlignment.Center);
        ui.Text("This screen is coming to Simpl for Windows next. Until then it opens as the school's own page.",
            r.X + 40, r.Y + 110, Theme.Callout, t.Ink2, r.W - 80, TextAlignment.Center, 2);
        if (ui.Button("open-web", new R(r.CenterX - 90, r.Bottom - 54, 180, 38), $"Open in {engine.LmsName}", prominent: true))
            engine.OpenWebScreen(WebPath(place), Title(place));
    }

    private string WebPath(Place place) => place.Kind switch
    {
        PlaceKind.Todo => "/",
        PlaceKind.Calendar => "/calendar",
        PlaceKind.Grades => "/grades",
        PlaceKind.Notifications => "/profile/communication",
        PlaceKind.Inbox => "/conversations",
        PlaceKind.Courses => "/courses",
        PlaceKind.Groups => "/groups",
        PlaceKind.Home => "/" + place.Ctx,
        PlaceKind.Section => $"/{place.Ctx}/{place.Arg}",
        _ => "/",
    };
}

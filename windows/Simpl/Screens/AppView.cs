using Simpl.Components;
using Simpl.Engine;
using Simpl.Graphics;
using Simpl.UI;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.Screens;

/// <summary>
/// The window once the school is ready (the Mac's MainShell, measured on its pictures): the sidebar down the whole
/// left — the school at its head, Dashboard, To Do, Calendar, Grades, Notifications, the Inbox, Tools, each course
/// with its sections, the groups, and who is signed in at its foot — the screen chosen there scrolling under the
/// toolbar, and the toolbar in the title bar's band: Back and Forward, Refresh, the screen's title, its own items,
/// and Search.
/// </summary>
public sealed class AppView
{
    private readonly Engine.Engine engine;
    private readonly GlassHost host;
    private readonly DashboardScreen dashboard;
    private readonly HashSet<string> open = new();
    private string query = "";
    private bool sideHidden, searchOpen;
    private int lastNav = -1;

    /// <summary>The sidebar's width (the Mac's, as pictured).</summary>
    public const float SideW = 278;
    /// <summary>What a screen says of itself for the toolbar: its title, a line under it, and its own items drawn from the right.</summary>
    public delegate float ToolbarItems(Ui ui, float right, float y);

    public AppView(Engine.Engine engine, GlassHost host)
    {
        this.engine = engine;
        this.host = host;
        dashboard = new DashboardScreen(engine, host);
    }

    public Color4 Ground => host.Ui.T.Page;

    public static Color4 DefaultGround(Theme t) => t.Page;

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

    /// <summary>A frame of the window.</summary>
    public void Frame(Ui ui)
    {
        ui.T.Dark = Dark(engine.Snapshot?.Dark);
        if (engine.Phase == Phase.Starting && engine.Snapshot == null) { Opening(ui); return; }

        var side = sideHidden ? 0 : SideW;
        var content = new R(side, 0, Math.Max(200, ui.W - side), ui.H);
        var place = engine.Nav.Current.Place;
        if (engine.NavVersion != lastNav)
        {
            lastNav = engine.NavVersion;
            ui.ScrollTo("page", 0);
        }
        foreach (var k in ui.Keys) if (k == Key.Escape && ui.Focus == null) dashboard.Escape();

        // the screen, scrolling under the toolbar
        ui.Layer(Ui.Page);
        string title;
        string? subtitle = null;
        ToolbarItems? items = null;
        switch (place.Kind)
        {
            case PlaceKind.Dashboard:
                dashboard.Frame(ui, content);
                title = "Dashboard";
                items = dashboard.Toolbar;
                break;
            default:
                ComingNext(ui, content, place);
                title = Title(place);
                break;
        }

        // the toolbar and the sidebar, over it
        ui.Layer(Ui.Chrome);
        Toolbar(ui, content, title, subtitle, items);
        if (!sideHidden) Sidebar(ui, place);
    }

    /// <summary>The school's site loading underneath, before the screens have anything to show.</summary>
    private void Opening(Ui ui)
    {
        ui.Layer(Ui.Page);
        ui.Spinner(ui.W / 2, ui.H / 2 - 20, 11);
        Lines.Text(ui, "Opening your school…", 0, ui.H / 2 + 6, Theme.Callout, ui.T.Ink2, ui.W, TextAlignment.Center);
    }

    // ---- the toolbar: in the title bar's band (the Mac's), 32-tall items of glass at y 10 ----

    private void Toolbar(Ui ui, R content, string title, string? subtitle, ToolbarItems? items)
    {
        const float y = 10;
        var x = content.X + 10;
        if (sideHidden)
        {
            if (ui.ToolbarButton("side:show", x, y, "sidebar.left", true, 16)) sideHidden = false;
            x += Ui.ToolbarItem + 10;
        }
        var nav = engine.Nav;
        var pressed = ui.BackForward("nav", x, y, nav.CanGoBack, nav.CanGoForward);
        if (pressed == 0) engine.GoBack();
        if (pressed == 1) engine.GoForward();
        x += 70 + 10;
        if (engine.Refreshing)
        {
            ui.Glass(new R(x, y, Ui.ToolbarItem, Ui.ToolbarItem), Ui.ToolbarItem / 2, GlassKind.Panel, null, 0, Ui.Shadow.None);
            ui.Spinner(x + 16, y + 16, 7);
        }
        else if (ui.ToolbarButton("refresh", x, y, "arrow.clockwise", engine.Phase == Phase.Native)) engine.Refresh();
        x += Ui.ToolbarItem + 16;
        var t = ui.T;
        if (subtitle == null) Lines.Text(ui, title, x, y + 7, Theme.ToolbarTitle, t.Ink(), 360);
        else
        {
            Lines.Text(ui, title, x, y + 2, Theme.ToolbarSmall, t.Ink(), 360);
            Lines.Text(ui, subtitle, x, y + 17, Theme.ToolbarSub, t.Ink2, 360);
        }
        var titleEnd = x + Math.Min(ui.Measure(title, Theme.ToolbarTitle).X, 360) + 16;

        // Search, at the right (before Windows' own caption buttons): a field while there is room or it is open, else a button
        var right = ui.W - Shell.MainWindow.CaptionButtonW * 3 - 12;
        const float fieldW = 294;
        var itemsW = items == null ? 0 : items(ui, -10000, -10000); // (measured first, drawn below)
        var room = right - titleEnd - (itemsW > 0 ? itemsW + 18 : 0);
        var field = searchOpen || room >= fieldW;
        float itemsRight;
        if (field)
        {
            var fr = new R(right - fieldW, y, fieldW, Ui.ToolbarItem);
            if (searchOpen && room < fieldW) itemsW = 0; // (opened by hand with no room: the field over the screen's items)
            var before = query;
            if (searchOpen && ui.Focus == null && query.Length == 0 && !ui.Pressed) searchOpen = false;
            var entered = ui.TextField("search", fr, ref query, $"Search {engine.LmsName} or type /", Theme.Callout, "magnifyingglass");
            if (query != before) Searched(query, engine.Nav.Current.Place);
            if (entered && query.Trim().Length > 0) engine.Search(query.Trim());
            itemsRight = fr.X - 19;
        }
        else
        {
            if (ui.ToolbarButton("search:open", right - Ui.ToolbarItem, y, "magnifyingglass")) { searchOpen = true; ui.Focus = "search"; }
            itemsRight = right - Ui.ToolbarItem - 19;
        }
        if (itemsW > 0 && items != null) items(ui, itemsRight, y);
    }

    private void Searched(string q, Place place)
    {
        engine.Query = q;
        var t = q.Trim();
        if (t.Length >= 2) engine.Search(t);
        else if (t.Length == 0 && place.Kind == PlaceKind.Search) engine.GoBack();
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
        PlaceKind.Courses => "Courses",
        PlaceKind.Search => "Search",
        PlaceKind.Home => CourseName(place.Ctx) ?? "Course",
        PlaceKind.Section => (engine.Sections.TryGetValue(place.Ctx ?? "", out var ss) ? ss.FirstOrDefault(s => s.Kind == place.Arg)?.Label : null)
                             ?? Cap(place.Arg ?? ""),
        _ => "Simpl",
    };

    private string? CourseName(string? ctx)
    {
        if (ctx == null) return null;
        if (ctx.StartsWith("courses/")) return engine.Courses.FirstOrDefault(c => c.Id == ctx[8..])?.Code;
        if (ctx.StartsWith("groups/")) return engine.Groups.FirstOrDefault(g => g.Id == ctx[7..])?.Name;
        return null;
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---- the sidebar (the Mac's, measured): 278 wide, rows of 40 ----

    private static readonly (string Label, string Icon, Place Place)[] Roots =
    {
        ("Dashboard", "square.grid.2x2", Place.Dashboard), ("To Do", "checklist", Place.Todo), ("Calendar", "calendar", Place.Calendar),
        ("Grades", "chart.bar.xaxis", Place.Grades), ("Notifications", "bell", Place.Notifications), ("Inbox", "tray", Place.Inbox),
        ("Tools", "wrench.and.screwdriver", Place.Tools), ("External Tools", "puzzlepiece.extension", Place.ExternalTools),
    };

    /// <summary>A course's sections before its home has said which it has (the Mac's defaultSections).</summary>
    private static readonly (string Kind, string Label)[] DefaultSections =
    {
        ("announcements", "Announcements"), ("assignments", "Assignments"), ("discussions", "Discussions"), ("grades", "Grades"),
        ("modules", "Modules"), ("pages", "Pages"), ("files", "Files"), ("quizzes", "Quizzes"), ("people", "People"), ("syllabus", "Syllabus"),
    };

    private const float RowH = 40, RowX = 18;
    private float RowW => SideW - 36;

    private void Sidebar(Ui ui, Place current)
    {
        var t = ui.T;
        var H = ui.H;
        ui.Fill(new R(0, 0, SideW, H), t.Sidebar);
        ui.Fill(new R(SideW - 1, 0, 1, H), t.SidebarEdge);
        ui.Block(new R(0, 0, SideW, H));

        // the toggle, where the Mac has it; the school at the head
        if (ui.IconButton("side:hide", new R(SideW - 24 - 16 - 8, 10, 32, 32), "sidebar.left", true, 16, t.Ink2)) sideHidden = true;
        var school = engine.Snapshot?.Site is { Length: > 0 } s ? s : "Simpl";
        ui.Fill(new R(26, 56, 34, 34), 9, t.Accent);
        ui.TextIn(school[..1].ToUpperInvariant(), new R(26, 56, 34, 34), new Font(Theme.TextFace, 16, FontWeight.Bold), new Color4(1, 1, 1, 1));
        Lines.Text(ui, school, 71, 56 + 7, Theme.Headline, t.Ink(), SideW - 71 - 18);

        // the rows, scrolling between the head and the foot
        var list = new R(0, 100, SideW - 1, H - 64 - 100);
        var off = ui.BeginScroll("sidebar", list);
        var y = list.Y - off;
        foreach (var (label, icon, place) in Roots)
        {
            if (engine.OnBrightspace && (place.Kind is PlaceKind.Inbox or PlaceKind.ExternalTools)) continue;
            var badge = !engine.CountsLive ? 0 : place.Kind switch
            {
                PlaceKind.Notifications => engine.Snapshot?.NotifUnread ?? 0,
                PlaceKind.Inbox => engine.Snapshot?.InboxUnread ?? 0,
                _ => 0,
            };
            if (SideRow(ui, "side:" + label, y, icon, label, current == place, badge)) engine.Go(place);
            y += RowH;
        }

        y += 12;
        Lines.Text(ui, "Courses", 22, y + 3, Theme.Semibold(Theme.Caption2), t.Ink2);
        y += 32;
        foreach (var c in engine.Courses)
        {
            var ctx = "courses/" + c.Id;
            var r = new R(RowX, y, RowW, RowH);
            var on = current.Ctx == ctx && current.Kind == PlaceKind.Home;
            var i = ui.SideRowWash("side:c:" + c.Id, r, on);
            ui.Fill(new R(27, r.CenterY - 6.5f, 13, 13), 3.5f, Theme.Hex(c.Color));
            var isOpen = open.Contains(ctx) || current.Ctx == ctx;
            var chev = new R(r.Right - 14 - 16, r.CenterY - 9, 18, 18);
            var textRight = chev.X - 8;
            if (engine.CoursesLive && c.Unread is > 0) textRight -= ui.CountLabel(c.Unread.Value, chev.X - 8, r.CenterY) + 8;
            Lines.Text(ui, c.Code, 48, r.CenterY - 9, Theme.Body, t.Ink(), textRight - 48);
            if (ui.IconButton("side:c:open:" + c.Id, chev, isOpen ? "chevron.down" : "chevron.right", true, 11.5f, t.Ink2, FontWeight.SemiBold))
            {
                if (!open.Remove(ctx)) { open.Add(ctx); _ = engine.LoadSections(ctx); }
            }
            else if (i.Clicked) engine.Go(Place.Home(ctx));
            y += RowH;
            if (isOpen)
            {
                var sections = engine.Sections.TryGetValue(ctx, out var ss) && ss.Count > 0
                    ? ss.Where(x => x.Kind != "home").Select(x => (x.Kind, x.Label)).ToArray()
                    : DefaultSections;
                foreach (var (kind, label) in sections)
                {
                    var p = Place.Section(ctx, kind);
                    if (SideRow(ui, $"side:s:{c.Id}:{kind}", y, Glyphs.Section(kind), label, current == p, 0, 20, true)) engine.Go(p);
                    y += RowH;
                }
            }
        }
        if (SideRow(ui, "side:all", y, "books.vertical", "All Courses", current == Place.Courses, 0)) engine.Go(Place.Courses);
        y += RowH;
        if (!engine.OnBrightspace && engine.Groups.Count > 0)
        {
            var r = new R(RowX, y, RowW, RowH);
            var i = ui.SideRowWash("side:groups", r, current == Place.Groups);
            ui.Icon("person.2", 38, r.CenterY, 16, t.Ink2);
            Lines.Text(ui, "Groups", 57, r.CenterY - 9, Theme.Body, t.Ink(), r.Right - 57 - 40);
            var chev = new R(r.Right - 14 - 16, r.CenterY - 9, 18, 18);
            var groupsOpen = open.Contains("groups");
            if (ui.IconButton("side:groups:open", chev, groupsOpen ? "chevron.down" : "chevron.right", true, 11.5f, t.Ink2, FontWeight.SemiBold))
            {
                if (!open.Remove("groups")) open.Add("groups");
            }
            else if (i.Clicked) engine.Go(Place.Groups);
            y += RowH;
            if (groupsOpen)
            {
                foreach (var g in engine.Groups)
                {
                    var gr = new R(RowX, y, RowW, RowH);
                    var p = Place.Home("groups/" + g.Id);
                    var gi = ui.SideRowWash("side:g:" + g.Id, gr, current == p);
                    ui.Fill(new R(27 + 20, gr.CenterY - 6.5f, 13, 13), 4, Theme.Hex(g.Color));
                    Lines.Text(ui, g.Name, 48 + 21, gr.CenterY - 9, Theme.Body, t.Ink(), gr.Right - 69 - 12);
                    if (gi.Clicked) engine.Go(p);
                    y += RowH;
                }
            }
        }
        ui.EndScroll("sidebar", list, y + off - list.Y + 8);

        // who is signed in, at the foot (the Mac's AccountBar)
        var me = engine.Snapshot?.Me;
        var top = H - 64;
        ui.Fill(new R(0, top, SideW - 1, 1), t.Separator);
        var cy = top + 32;
        ui.Avatar(me?.Initials ?? Initials(me?.Name ?? ""), 37, cy, 30);
        Lines.Text(ui, me?.Name ?? "Account", 62, cy - 17, Theme.Semibold(Theme.Callout), t.Ink(), SideW - 62 - 48);
        if (engine.CountsLive) Lines.Text(ui, engine.Snapshot?.Site ?? engine.Host, 62, cy + 1, Theme.Caption, t.Ink2, SideW - 62 - 48);
        else
        {
            ui.Spinner(62 + 6, cy + 8, 5);
            Lines.Text(ui, "Updating…", 62 + 17, cy + 1, Theme.Caption, t.Ink2, SideW - 79 - 48);
        }
        ui.RoundIconButton("side:account", SideW - 25, cy, "ellipsis.circle", 16, t.Accent);
    }

    /// <summary>A row of the sidebar: its symbol 16 secondary at 38, its words 15 at 57 (a section's row 20 further in), a count at its end; clicked.</summary>
    private bool SideRow(Ui ui, string id, float y, string icon, string label, bool on, int badge, float indent = 0, bool glyph = false)
    {
        var t = ui.T;
        var r = new R(RowX, y, RowW, RowH);
        var i = ui.SideRowWash(id, r, on);
        if (glyph) ui.Glyph(icon, 38 + indent, r.CenterY, 15, t.Ink2);
        else ui.Icon(icon, 38 + indent, r.CenterY, 16, t.Ink2);
        var right = r.Right - 16;
        if (badge > 0)
        {
            var s = badge.ToString();
            var m = ui.Measure(s, Theme.Caption);
            Lines.Text(ui, s, right - m.X, r.CenterY - 7.5f, Theme.Caption, t.Ink2);
            right -= m.X + 10;
        }
        Lines.Text(ui, label, 57 + indent, r.CenterY - 9, Theme.Body, t.Ink(), right - 57 - indent);
        return i.Clicked;
    }

    private static string Initials(string s)
    {
        var parts = s.Split(new[] { ' ', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "·",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}",
        };
    }

    // ---- a screen not yet on Windows ----

    private void ComingNext(Ui ui, R content, Place place)
    {
        var t = ui.T;
        var page = PageFrame.Begin(ui, content);
        var y = page.Top;
        y += Headings.Screen(ui, Title(place), null, page.X, y, page.W) + 26;
        var r = new R(page.X, y, page.W, 150);
        ui.Glass(r, 18);
        ui.Icon("sparkles", r.X + 34, r.Y + 44, 22, t.Accent);
        Lines.Text(ui, "Coming to Simpl for Windows next", r.X + 60, r.Y + 32, Theme.Headline, t.Ink(), r.W - 80);
        Lines.Text(ui, "Until then this screen opens as the school's own page.", r.X + 60, r.Y + 56, Theme.Callout, t.Ink2, r.W - 80);
        if (ui.GlassButton("open-web", r.X + 60, r.Y + 94, $"Open in {engine.LmsName}", "globe"))
            engine.OpenWebScreen(WebPath(place), Title(place));
        page.End(ui, r.Bottom);
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

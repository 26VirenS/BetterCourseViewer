using Simpl.Engine;

namespace Simpl.Screens;

/// <summary>
/// Which screen shows a place or a route (the Mac's MainShell.destination and Router): the app's own where it has one;
/// the school's own page, inside the window, where it does not yet.
/// </summary>
public static class ScreenFactory
{
    public static Screen For(Engine.Engine engine, Place p) => p.Kind switch
    {
        PlaceKind.Dashboard => new DashboardScreen(engine),
        PlaceKind.Todo => new TodoScreen(engine),
        PlaceKind.Calendar => new CalendarScreen(engine),
        PlaceKind.Notifications => new NotificationsScreen(engine),
        PlaceKind.Search => new SearchScreen(engine, p.Arg ?? ""),
        PlaceKind.ExternalTools => new ExternalToolsScreen(engine),
        PlaceKind.Tools => new ToolsScreen(engine),
        _ => Native(engine, p) ?? new WebScreen(engine, engine.SchoolUrl(p), () => TitleOf(engine, p)),
    };

    /// <summary>The screens made since the Dashboard (filled in as they come over from the Mac).</summary>
    private static Screen? Native(Engine.Engine engine, Place p) => Registry.Place?.Invoke(engine, p);

    public static Screen For(Engine.Engine engine, Route r)
    {
        if (Registry.Route?.Invoke(engine, r) is { } s) return s;
        return r.Kind switch
        {
            RouteKind.Topic => new TopicScreen(engine, r.A, r.B),
            RouteKind.Web => new WebScreen(engine, engine.Absolute(r.A), string.IsNullOrEmpty(r.B) ? engine.LmsName : r.B),
            _ => new WebScreen(engine, UrlOf(engine, r), TitleOf(r)),
        };
    }

    /// <summary>Where the screens made later plug in (each returns null for what it does not show).</summary>
    public static class Registry
    {
        public static Func<Engine.Engine, Place, Screen?>? Place;
        public static Func<Engine.Engine, Route, Screen?>? Route;
    }

    public static string TitleOf(Engine.Engine engine, Place p) => p.Kind switch
    {
        PlaceKind.Dashboard => "Dashboard",
        PlaceKind.Courses => "All Courses",
        PlaceKind.Todo => "To Do",
        PlaceKind.Calendar => "Calendar",
        PlaceKind.Grades => "Grades",
        PlaceKind.Notifications => "Notifications",
        PlaceKind.Inbox => "Inbox",
        PlaceKind.Tools => "Tools",
        PlaceKind.ExternalTools => "External Tools",
        PlaceKind.Groups => "Groups",
        PlaceKind.Search => string.IsNullOrEmpty(p.Arg) ? "Search" : $"Search: {p.Arg}",
        PlaceKind.Home => ContextName(engine, p.Ctx),
        PlaceKind.Section => SectionName(engine, p.Ctx, p.Arg),
        _ => "Simpl",
    };

    public static string ContextName(Engine.Engine engine, string? ctx)
    {
        if (ctx == null) return "Course";
        if (ctx.StartsWith("courses/") && engine.Courses.FirstOrDefault(c => $"courses/{c.Id}" == ctx) is { } c)
            return string.IsNullOrEmpty(c.Code) ? c.Name : c.Code;
        if (ctx.StartsWith("groups/") && engine.Groups.FirstOrDefault(g => $"groups/{g.Id}" == ctx) is { } g) return g.Name;
        return ctx.StartsWith("groups/") ? "Group" : "Course";
    }

    public static string SectionName(Engine.Engine engine, string? ctx, string? kind)
    {
        if (ctx != null && engine.Sections.TryGetValue(ctx, out var list) && list.FirstOrDefault(s => s.Kind == kind) is { } s) return s.Label;
        return kind switch
        {
            "announcements" => "Announcements", "assignments" => "Assignments", "discussions" => "Discussions", "grades" => "Grades",
            "modules" => "Modules", "pages" => "Pages", "files" => "Files", "quizzes" => "Quizzes", "people" => "People", "syllabus" => "Syllabus",
            _ => kind ?? "Section",
        };
    }

    public static string TitleOf(Route r) => r.Kind switch
    {
        RouteKind.Web => string.IsNullOrEmpty(r.B) ? "Page" : r.B,
        RouteKind.Assignment => "Assignment",
        RouteKind.Topic => "Discussion",
        RouteKind.Page => "Page",
        RouteKind.Folder => string.IsNullOrEmpty(r.C) ? "Folder" : r.C,
        RouteKind.Conversation => "Conversation",
        RouteKind.Section => r.B,
        _ => r.Kind.ToString(),
    };

    public static Uri? UrlOf(Engine.Engine engine, Route r) => r.Kind switch
    {
        RouteKind.Assignment => engine.Absolute($"/courses/{r.A}/assignments/{r.B}"),
        RouteKind.Topic => engine.Absolute($"/{r.A}/discussion_topics/{r.B}"),
        RouteKind.Page => engine.Absolute($"/{r.A}/pages/{r.B}"),
        RouteKind.Folder => engine.Absolute($"/{r.A}/files/folder/{r.C}"),
        RouteKind.Conversation => engine.Absolute($"/conversations#/conversations/{r.A}"),
        RouteKind.Course => engine.Absolute($"/courses/{r.A}"),
        RouteKind.Group => engine.Absolute($"/groups/{r.A}"),
        RouteKind.Section => engine.SchoolUrl(Place.Section(r.A, r.B)),
        _ => engine.Absolute("/"),
    };
}

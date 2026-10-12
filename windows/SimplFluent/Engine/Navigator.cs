namespace Simpl.Engine;

public enum PlaceKind { Dashboard, Courses, Todo, Calendar, Grades, Notifications, Inbox, Tools, ExternalTools, Groups, Search, Home, Section }

/// <summary>
/// A place in the sidebar: a root of the app (Dashboard, To Do …), a course's or a group's home, or one of its
/// sections. A context (<c>Ctx</c>) is "courses/&lt;id&gt;" or "groups/&lt;id&gt;", as in the Mac app; <c>Arg</c> is
/// a section's kind or a search's words.
/// </summary>
public sealed record Place(PlaceKind Kind, string? Ctx = null, string? Arg = null)
{
    public static readonly Place Dashboard = new(PlaceKind.Dashboard);
    public static readonly Place Courses = new(PlaceKind.Courses);
    public static readonly Place Todo = new(PlaceKind.Todo);
    public static readonly Place Calendar = new(PlaceKind.Calendar);
    public static readonly Place Grades = new(PlaceKind.Grades);
    public static readonly Place Notifications = new(PlaceKind.Notifications);
    public static readonly Place Inbox = new(PlaceKind.Inbox);
    public static readonly Place Tools = new(PlaceKind.Tools);
    public static readonly Place ExternalTools = new(PlaceKind.ExternalTools);
    public static readonly Place Groups = new(PlaceKind.Groups);
    public static Place Search(string q) => new(PlaceKind.Search, null, q);
    public static Place Home(string ctx) => new(PlaceKind.Home, ctx);
    public static Place Section(string ctx, string kind) => new(PlaceKind.Section, ctx, kind);

    /// <summary>What the window keeps one screen for: every search is the one Search screen.</summary>
    public Place Identity => Kind == PlaceKind.Search ? Search("") : this;

    /// <summary>The section a place shows ("home" for a context's home).</summary>
    public string? SectionKind => Kind switch { PlaceKind.Home => "home", PlaceKind.Section => Arg, _ => null };

    /// <summary>The place's name, as <see cref="Engine.PlaceNamed"/> reads it (a window more opens on it).</summary>
    public string Name => Kind switch
    {
        PlaceKind.Search => "search:" + Arg,
        PlaceKind.Home => Ctx!.StartsWith("groups/") ? "group:" + Ctx[7..] : "course:" + Ctx![8..],
        PlaceKind.Section => $"section:{Ctx}:{Arg}",
        PlaceKind.ExternalTools => "externaltools",
        _ => Kind.ToString().ToLowerInvariant(),
    };
}

public enum RouteKind { Web, Notifications, Calendar, Course, Group, Groups, Section, Topic, Assignment, Page, Folder, Inbox, Conversation }

/// <summary>
/// A screen pushed on a place, or a native screen by name (the Mac's Route): web(url, title), course(id), group(id),
/// section(ctx, kind), topic(ctx, id), assignment(course, id), page(ctx, slug), folder(ctx, id, name), conversation(id).
/// </summary>
public sealed record Route(RouteKind Kind, string A = "", string B = "", string C = "")
{
    public static Route Web(string url, string title) => new(RouteKind.Web, url, title);
    public static Route Course(string id) => new(RouteKind.Course, id);
    public static Route Group(string id) => new(RouteKind.Group, id);
    public static Route Section(string ctx, string kind) => new(RouteKind.Section, ctx, kind);
    public static Route Topic(string ctx, string id) => new(RouteKind.Topic, ctx, id);
    public static Route Assignment(string course, string id) => new(RouteKind.Assignment, course, id);
    public static Route Page(string ctx, string slug) => new(RouteKind.Page, ctx, slug);
    public static Route Folder(string ctx, string id, string name) => new(RouteKind.Folder, ctx, id, name);
    public static Route Conversation(string id) => new(RouteKind.Conversation, id);
    public static readonly Route InboxRoute = new(RouteKind.Inbox);
    public static readonly Route GroupsRoute = new(RouteKind.Groups);
    public static readonly Route NotificationsRoute = new(RouteKind.Notifications);
    public static readonly Route CalendarRoute = new(RouteKind.Calendar);
}

/// <summary>Where the window is: the place in the sidebar and what is pushed on it.</summary>
public sealed record Location(Place Place, IReadOnlyList<Route> Path)
{
    public Location(Place place) : this(place, Array.Empty<Route>()) { }

    public bool Equals(Location? other) => other != null && other.Place == Place && other.Path.SequenceEqual(Path);
    public override int GetHashCode() => HashCode.Combine(Place, Path.Count);
}

/// <summary>
/// Where the window is and has been: the place, what is pushed on it, and the history behind and ahead of it — Back and
/// Forward walk it as a browser's do, across places as well as within one (the Mac's Navigator).
/// </summary>
public sealed class Navigator
{
    private readonly List<Location> back = new();
    private readonly List<Location> forward = new();

    public Location Current { get; private set; } = new(Place.Dashboard);
    public bool CanGoBack => back.Count > 0;
    public bool CanGoForward => forward.Count > 0;

    /// <summary>A place picked (the sidebar, a link to a course): shown at its root.</summary>
    public void Go(Place place)
    {
        if (Current.Place == place && Current.Path.Count == 0) return;
        Record();
        Current = new Location(place);
    }

    /// <summary>A screen pushed on the place showing (not twice in a row).</summary>
    public void Push(Route route)
    {
        if (Current.Path.Count > 0 && Current.Path[^1] == route) return;
        Record();
        Current = Current with { Path = Current.Path.Append(route).ToList() };
    }

    /// <summary>A course's screen opened from elsewhere: the course's own place with the screen on it, as one step.</summary>
    public void Push(Route route, Place place)
    {
        if (Current.Place.Ctx == place.Ctx) { Push(route); return; }
        Record();
        Current = new Location(place, new List<Route> { route });
    }

    /// <summary>The stack changed from below (a screen's own Back): taken as a step like any other.</summary>
    public void SetPath(IReadOnlyList<Route> path)
    {
        if (path.SequenceEqual(Current.Path)) return;
        Record();
        Current = Current with { Path = path.ToList() };
    }

    public void GoBack()
    {
        if (back.Count == 0) return;
        forward.Add(Current);
        Current = back[^1];
        back.RemoveAt(back.Count - 1);
    }

    public void GoForward()
    {
        if (forward.Count == 0) return;
        back.Add(Current);
        Current = forward[^1];
        forward.RemoveAt(forward.Count - 1);
    }

    /// <summary>The place showing taken over by another without a step in the history (a search typed further).</summary>
    public void Replace(Place place) => Current = new Location(place);

    /// <summary>Back to a place's root (its sidebar row pressed again).</summary>
    public void PopToRoot()
    {
        if (Current.Path.Count == 0) return;
        Record();
        Current = new Location(Current.Place);
    }

    private void Record()
    {
        back.Add(Current);
        if (back.Count > 120) back.RemoveRange(0, back.Count - 120);
        forward.Clear();
    }
}

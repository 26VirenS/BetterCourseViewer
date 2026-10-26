using System.Web;
using Simpl.Models;

namespace Simpl.Engine;

/// <summary>
/// Which of the school's addresses the app draws itself (the Mac's Router): a course and what is in it, a group and
/// what is in it, the list of groups and the Inbox. A quiz opens in the app's own quiz screen, an external tool in its
/// window, a file opened from Downloads; anything else by where the school sends it (<see cref="OpenWeb"/>).
/// </summary>
public sealed partial class Engine
{
    /// <summary>The school's site is Brightspace: the page says so; before it has, its own domains.</summary>
    public bool OnBrightspace
    {
        get
        {
            if (Lms != null) return Lms == "d2l";
            var h = BaseUrl.Host.ToLowerInvariant();
            return h.EndsWith(".brightspace.com") || h.EndsWith(".d2l.com") || h.EndsWith(".desire2learn.com");
        }
    }

    /// <summary>The name the school's own site goes by.</summary>
    public string LmsName => OnBrightspace ? "Brightspace" : "Canvas";

    public static bool Numeric(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);

    private static string[] Parts(Uri u) => u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();

    private static string? QueryValue(Uri u, string name) => HttpUtility.ParseQueryString(u.Query).Get(name);

    /// <summary>A Brightspace page's address as the interface's own (lms.js fromPage, in short).</summary>
    public string InterfaceAddress(string raw)
    {
        if (Absolute(raw) is not { } url || !IsSchool(url) || !url.AbsolutePath.StartsWith("/d2l/", StringComparison.OrdinalIgnoreCase)) return raw;
        var hash = url.Fragment;
        if (QueryValue(url, "simpl") is { } own && own.StartsWith('/')) return own + hash;
        var p = Parts(url);
        if (p.Length == 2 && p[1] == "home") return "/" + hash;
        if (p.Length == 3 && p[1] == "home" && Numeric(p[2])) return $"/courses/{p[2]}";
        if (url.AbsolutePath.Equals("/d2l/lms/quizzing/user/quiz_summary.d2l", StringComparison.OrdinalIgnoreCase)
            && QueryValue(url, "ou") is { } ou && QueryValue(url, "qi") is { } qi && Numeric(ou) && Numeric(qi))
            return $"/courses/{ou}/quizzes/{qi}?bcv=native";
        return raw;
    }

    private sealed class PageAnswer { public string Url { get; set; } = ""; }

    /// <summary>The school's own page for an address: on Brightspace the Brightspace page for the interface's address.</summary>
    public async Task<Uri> SchoolPage(Uri url)
    {
        if (!OnBrightspace || !IsSchool(url) || url.AbsolutePath.StartsWith("/d2l/", StringComparison.OrdinalIgnoreCase)) return url;
        try { var p = await Call<PageAnswer>("pageFor", new { url = url.ToString() }); if (Absolute(p.Url) is { } u) return u; } catch { }
        return url;
    }

    public Route? NativeRoute(string raw, string title)
    {
        raw = InterfaceAddress(raw);
        if (Absolute(raw) is not { } url || !IsSchool(url)) return null;
        var q = url.Query;
        if (q.Contains("bcv=") || q.Contains("display=")) return null; // (a screen asked for by name, a frame's view)
        var p = Parts(url);
        if (p.Length == 0) return null;
        var first = p[0];
        if (first == "conversations")
        {
            if (p.Length == 2 && Numeric(p[1])) return Route.Conversation(p[1]);
            if (QueryValue(url, "id") is { } cid && Numeric(cid)) return Route.Conversation(cid);
            return Route.InboxRoute;
        }
        if (p.Length == 1 && first == "groups") return Route.GroupsRoute;
        if (p.Length < 2 || !(first is "courses" or "groups") || !Numeric(p[1])) return null;
        var id = p[1];
        var ctx = $"{first}/{id}";
        var isCourse = first == "courses";
        if (p.Length == 2) return isCourse ? Route.Course(id) : Route.Group(id);
        var rest = p[2..];
        var one = rest.Length == 1;
        switch (rest[0])
        {
            case "announcements":
                if (one) return Route.Section(ctx, "announcements");
                return Numeric(rest[1]) ? Route.Topic(ctx, rest[1]) : Route.Section(ctx, "announcements");
            case "discussion_topics":
                if (one) return Route.Section(ctx, "discussions");
                return Numeric(rest[1]) ? Route.Topic(ctx, rest[1]) : Route.Section(ctx, "discussions");
            case "assignments" when isCourse:
                if (one) return Route.Section(ctx, "assignments");
                if (rest[1] == "syllabus") return Route.Section(ctx, "syllabus");
                return Numeric(rest[1]) ? Route.Assignment(id, rest[1]) : Route.Section(ctx, "assignments");
            case "syllabus":
                return Route.Section(ctx, "syllabus");
            case "modules":
                if (rest.Length >= 3 && rest[1] == "items") return null; // (a module item: resolved first, in OpenWeb)
                return Route.Section(ctx, "modules");
            case "pages":
                if (one) return Route.Section(ctx, "pages");
                return Route.Page(ctx, rest[1]);
            case "wiki":
                return Route.Page(ctx, rest.Length >= 2 ? rest[1] : "");
            case "files":
                return Route.Section(ctx, "files");
            case "users":
            case "people":
                return Route.Section(ctx, "people");
            case "groups" when isCourse:
                return Route.GroupsRoute;
            case "quizzes" when isCourse:
                if (OnBrightspace && rest.Length >= 2) return null;
                return Route.Section(ctx, "quizzes");
            case "grades" when isCourse:
                return Route.Section(ctx, "grades");
            default:
                return null;
        }
    }

    /// <summary>A root's own address: the dashboard, the course list, the grades, the calendar, the notifications — and
    /// (0.3.64) To Do's (the interface's /todo, or the Dashboard's #todo), which is To Do &amp; Calendar's now.</summary>
    public string? TabName(string raw)
    {
        raw = InterfaceAddress(raw);
        if (Absolute(raw) is not { } url || !IsSchool(url)) return null;
        var p = Parts(url);
        if (p.Length == 0) return url.Fragment == "#todo" ? "todo" : "today";
        return p[0] switch
        {
            "courses" when p.Length == 1 => "courses",
            "grades" => "grades",
            "todo" when p.Length == 1 => "todo",
            "calendar" or "calendar2" => "calendar",
            "profile" when p.Length >= 2 && p[1] == "communication" => "notifications",
            "courses" when p.Length >= 3 && p[2] == "calendar_events" => "calendar",
            _ => null,
        };
    }

    /// <summary>A course file's page (…/files/12, its preview, ?wrap=1): the file itself, to open.</summary>
    public Uri? FileDownloadFor(string raw)
    {
        raw = InterfaceAddress(raw);
        if (Absolute(raw) is not { } url || !IsSchool(url)) return null;
        if (IsDownload(url)) return url;
        var p = Parts(url);
        var i = Array.IndexOf(p, "files");
        if (i < 0 || i + 1 >= p.Length || !Numeric(p[i + 1])) return null;
        if (OnBrightspace && i == 2 && p[0] == "courses" && Numeric(p[1]))
            return Absolute($"/d2l/le/content/{p[1]}/topics/files/download/{p[i + 1]}/DirectFileTopicDownload");
        return Absolute("/" + string.Join("/", p[..(i + 2)]) + "/download?download_frd=1");
    }

    /// <summary>(modules-file) A module's file, opened as the Files screen opens one: the file as the school's API has it — its
    /// own download address, with the one-time key Canvas puts on it — fetched and opened under the name the module gives
    /// it. The module's own address for it (…/files/12/download, which needs the session and Canvas's redirect to its file
    /// host) is kept for when the school will not say, as it was; a Brightspace content file goes that way always, from
    /// its topic.</summary>
    public void OpenModuleFile(string url, string title)
    {
        if (LinkedFile(url) is null) { GoTo(url, title); return; }
        OpenLinkedFile(url, title);
    }

    /// <summary>(0.3.23) A school file's link anywhere else — in an assignment's instructions, an announcement, a
    /// discussion, a page, a row — opened as a module's file is: by its id, the file as the school's API has it (its own
    /// download address, with the one-time key Canvas puts on it, and its real name, not "download"). Only when the school
    /// will not say is the address made from the link fetched, as before — with the link's own key (verifier) kept on it,
    /// which a file shared from another course needs; it used to be dropped.</summary>
    public async void OpenLinkedFile(string url, string title)
    {
        if (LinkedFile(url) is not { } file) { GoTo(url, title); return; }
        FileRow? f = null;
        try { f = await Call<FileRow>("fileInfo", new { id = file.Id }); } catch { }
        if (f is { Url: { Length: > 0 } u, Locked: not true } && Absolute(u) is { } a)
        {
            FilePreview.Open(a, string.IsNullOrEmpty(title) ? f.Name : title, Web);
            return;
        }
        FilePreview.Open(KeepingKey(url, file.Download), string.IsNullOrEmpty(title) ? "File" : title, Web);
    }

    /// <summary>(0.3.23) A Canvas file's link (…/courses/1/files/12, ?wrap=1, …/files/12/download?download_frd=1, …/preview,
    /// and the Files page's own address for one, …/files?preview=12 or …/files/folder/Week 3?preview=12): the file's id, and
    /// the address that fetches it with the session (…/files/12/download?download_frd=1). Null for anything else — a
    /// folder, another site, Brightspace (whose files are its content topics).</summary>
    public (string Id, Uri Download)? LinkedFile(string raw)
    {
        if (OnBrightspace || Absolute(InterfaceAddress(raw)) is not { } url || !IsSchool(url)) return null;
        var p = Parts(url);
        var i = Array.IndexOf(p, "files");
        if (i < 0) return null;
        static bool IsId(string? id) => !string.IsNullOrEmpty(id) && id.Any(char.IsAsciiDigit) && id.All(char.IsAsciiLetterOrDigit);
        // the Files page with a file previewed over it, the link a teacher copies from there: the file is the one
        // previewed (the folder only says where it sits)
        if (i + 1 == p.Length || p[i + 1] == "folder")
        {
            var previewed = QueryValue(url, "preview");
            if (!IsId(previewed)) return null;
            var at = Absolute("/" + string.Join("/", p[..(i + 1)]) + "/" + previewed + "/download?download_frd=1");
            return at is null ? null : (previewed!, at);
        }
        var id = p[i + 1];
        if (!IsId(id)) return null;
        var rest = p[(i + 2)..];
        if (rest.Length > 1 || rest.Any(x => x is not ("download" or "preview" or "file_preview"))) return null;
        var download = FileDownloadFor(raw) ?? Absolute("/" + string.Join("/", p[..(i + 2)]) + "/download?download_frd=1");
        return download is null ? null : (id, download);
    }

    /// <summary>The address made from a file's link, with the link's own one-time key (verifier) on it.</summary>
    private Uri KeepingKey(string link, Uri download)
    {
        if (Absolute(link) is not { } from || download.Query.Contains("verifier=")) return download;
        var key = from.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).FirstOrDefault(x => x.Length == 2 && x[0] == "verifier")?[1];
        if (string.IsNullOrEmpty(key)) return download;
        var query = download.Query.TrimStart('?');
        return new UriBuilder(download) { Query = (query.Length > 0 ? query + "&" : "") + "verifier=" + key }.Uri;
    }

    /// <summary>Brightspace's own viewer for a content file it will not hand over.</summary>
    public Uri? SchoolViewer(Uri url)
    {
        if (!OnBrightspace) return null;
        var p = Parts(url);
        if (p.Length == 9 && p[0] == "d2l" && p[1] == "api" && p[2] == "le" && Numeric(p[4]) && p[5] == "content" && p[6] == "topics" && Numeric(p[7]) && p[8] == "file")
            return Absolute($"/d2l/le/content/{p[4]}/viewContent/{p[7]}/View");
        if (p.Length == 9 && p[0] == "d2l" && p[1] == "le" && p[2] == "content" && Numeric(p[3]) && p[4] == "topics" && p[5] == "files"
            && p[6] == "download" && Numeric(p[7]) && p[8] == "DirectFileTopicDownload")
            return Absolute($"/d2l/le/content/{p[3]}/viewContent/{p[7]}/View");
        return null;
    }

    /// <summary>A hand-in's own address (…/assignments/5/submissions/7): the assignment, opened at its feedback.</summary>
    public static bool IsSubmission(Uri url)
    {
        var p = Parts(url);
        return p.Length >= 5 && p[2] == "assignments" && p[4] == "submissions";
    }

    /// <summary>A course's own external tool (…/courses/1/external_tools/9): opened in a tool window, not a screen.</summary>
    public ToolLaunch? ToolLaunchFor(string raw, string title)
    {
        raw = InterfaceAddress(raw);
        if (Absolute(raw) is not { } url || !IsSchool(url)) return null;
        var p = Parts(url);
        if (p.Length != 4 || p[0] != "courses" || !Numeric(p[1]) || p[2] != "external_tools") return null;
        var t = string.IsNullOrEmpty(title) ? "Tool" : title;
        if (Numeric(p[3])) return ToolLaunch.CourseTool(p[1], p[3], t);
        if (p[3] == "retrieve" && QueryValue(url, "url") is { Length: > 0 } u)
            return new ToolLaunch(t, new Dictionary<string, string> { ["course"] = p[1], ["url"] = u });
        return null;
    }

    /// <summary>A Classic quiz (…/courses/1/quizzes/9, or its take page): the app's own quiz screen, over everything.</summary>
    public QuizLaunch? QuizLaunchFor(string raw, string title)
    {
        if (OnBrightspace) return null;
        if (Absolute(raw) is not { } url || !IsSchool(url)) return null;
        if (url.Query.Contains("bcv=") || url.Query.Contains("display=")) return null;
        var p = Parts(url);
        if (p.Length < 4 || p[0] != "courses" || !Numeric(p[1]) || p[2] != "quizzes" || !Numeric(p[3])) return null;
        return new QuizLaunch(p[1], p[3], string.IsNullOrEmpty(title) ? "Quiz" : title) { Feedback = p.Length >= 5 && p[4] == "history" };
    }

    /// <summary>A course file's own address (…/files/12/download).</summary>
    public static bool IsDownload(Uri url) =>
        url.AbsolutePath.Contains("/files/") && (url.AbsolutePath.EndsWith("/download") || url.Query.Contains("download"));
}

using System.Globalization;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;

namespace Simpl.Shell;

/// <summary>The search box's suggestions for what it holds (the Mac's SearchPalette, its sections and rows).</summary>
public sealed partial class SearchPalette
{
    /// <summary>Words that are a command: nothing is searched for them.</summary>
    public static bool IsCommand(string text)
    {
        var t = text.Trim();
        return t.StartsWith('/') || t.StartsWith('>');
    }

    /// <summary>The commands whose names the words start (every one, for no words), as the Search screen shows them.</summary>
    public static IReadOnlyList<PaletteItem> CommandItems(string words, Engine.Engine engine)
    {
        var all = PaletteCommand.Catalog(engine);
        var q = words.Trim().ToLowerInvariant();
        if (q.Length == 0) return all.Where(c => c.Name != "help").Select(c => c.PlainRow).ToList();
        if (q.Length < 2) return [];
        return PaletteCommand.Matching(all, q, strict: true).Take(4).Select(c => c.PlainRow).ToList();
    }

    /// <summary>The symbol for a kind of search result (the Mac's SearchGlyph).</summary>
    public static SymbolRegular KindSymbol(string group) => group switch
    {
        "Courses" => SymbolRegular.Library24,
        "Assignments" => SymbolRegular.DocumentText24,
        "Announcements" => SymbolRegular.Megaphone24,
        "Pages" => SymbolRegular.DocumentOnePage24,
        "Discussions" => SymbolRegular.ChatMultiple24,
        "Files" => SymbolRegular.Folder24,
        "People" => SymbolRegular.PersonCircle24,
        _ => SymbolRegular.Search24,
    };

    // ---- what is typed ----

    private readonly record struct Typed(string Raw, bool Command, string Name, string Arg, bool HasArg)
    {
        public static Typed Of(string raw)
        {
            var t = raw.TrimStart(' ');
            if (t.Length == 0 || (t[0] != '/' && t[0] != '>')) return new Typed(raw, false, "", "", false);
            var rest = t[1..];
            var sp = rest.IndexOf(' ');
            if (sp < 0) return new Typed(raw, true, rest, "", false);
            var n = rest[..sp];
            var a = rest[sp..].Trim();
            // ("/ grades": the name after the space)
            return n.Length == 0 ? new Typed(raw, true, a, "", false) : new Typed(raw, true, n, a, true);
        }
    }

    private enum JobKind { Kinds, Due, Grades }

    /// <summary>What the app answers later: the school's search for one kind, the planner, the grades.</summary>
    private sealed record Job(JobKind Kind, string Title, string Q, string[]? Kinds = null, bool Overdue = false);

    private (List<PaletteSection> Sections, Job? Job) Suggest(string raw)
    {
        var typed = Typed.Of(raw);
        return typed.Command ? CommandSections(typed) : (PlainSections(typed), null);
    }

    // ---- words typed ----

    private List<PaletteSection> PlainSections(Typed t)
    {
        var words = t.Raw.Trim();
        var q = words.ToLowerInvariant();
        var outList = new List<PaletteSection>();
        if (q.Length == 0) return outList;
        if (PaletteSum.Answer(words) is { } a)
            outList.Add(new PaletteSection("Answer", [new PaletteItem("answer", a.Value, SymbolRegular.EqualCircle24, PaletteAction.Copy(a.Value)) { Sub = a.Sub, Trailing = "Copy", Filled = true }]));
        if (q.Length >= 2)
        {
            var cmds = PaletteCommand.Matching(PaletteCommand.Catalog(engine), q, strict: true).Take(4).Select(c => c.PlainRow).ToList();
            if (cmds.Count > 0) outList.Add(new PaletteSection("Commands", cmds));
        }
        var courses = CourseItems(q, engine, 5);
        if (courses.Count > 0) outList.Add(new PaletteSection("Courses", courses));
        if (!engine.OnBrightspace)
        {
            var groups = GroupItems(q, engine, 3);
            if (groups.Count > 0) outList.Add(new PaletteSection("Groups", groups));
        }
        outList.Add(new PaletteSection("Search", [SearchRow(words)]));
        return outList;
    }

    // ---- a command typed ----

    private (List<PaletteSection>, Job?) CommandSections(Typed t)
    {
        var all = PaletteCommand.Catalog(engine);
        if (!t.HasArg)
        {
            var list = PaletteCommand.Matching(all, t.Name, strict: false);
            if (list.Count == 0)
            {
                var help = new PaletteItem("help", "/help", SymbolRegular.AppsList24, PaletteAction.Fill("/")) { Sub = "Every command" };
                return ([new PaletteSection("No Command by That Name", [help, SearchRow(t.Name)])], null);
            }
            return ([new PaletteSection("Commands", list.Select(c => c.SlashRow).ToList())], null);
        }
        if (PaletteCommand.Named(all, t.Name) is not { } cmd)
        {
            // ("/physics quiz tomorrow": no command by that name — the words searched)
            var words = $"{t.Name} {t.Arg}".Trim();
            return ([new PaletteSection("Search", [SearchRow(words)])], null);
        }
        return ArgumentSections(cmd, t.Arg, all);
    }

    /// <summary>What a command takes, as a list that narrows as the rest is typed.</summary>
    private (List<PaletteSection>, Job?) ArgumentSections(PaletteCommand cmd, string arg, List<PaletteCommand> all)
    {
        var q = arg.ToLowerInvariant();
        static List<PaletteSection> One(string title, params PaletteItem[] items) => [new PaletteSection(title, items)];
        switch (cmd.Lists)
        {
            case null:
                return (One(cmd.Title, cmd.SlashRow), null);
            case PaletteLists.Courses:
            {
                var items = CourseItems(q, engine, 10);
                return (One("Courses", items.Count == 0 ? [Hint("No course by that name", SymbolRegular.Book24)] : items.ToArray()), null);
            }
            case PaletteLists.Groups:
            {
                var items = GroupItems(q, engine, 10);
                return (One("Groups", items.Count == 0 ? [Hint("No group by that name", SymbolRegular.People24)] : items.ToArray()), null);
            }
            case PaletteLists.Grades:
                return (One("Grades", GradeItems(q).ToArray()), new Job(JobKind.Grades, "Grades", q));
            case PaletteLists.Due:
                return (One(cmd.Title, Looking), new Job(JobKind.Due, cmd.Title, q));
            case PaletteLists.Overdue:
                return (One(cmd.Title, Looking), new Job(JobKind.Due, cmd.Title, q, Overdue: true));
            case PaletteLists.Kinds:
                if (q.Length < 2) return (One(cmd.Title, Hint("Type part of its name…", cmd.Symbol)), null);
                return (One(cmd.Title, Looking), new Job(JobKind.Kinds, cmd.Title, arg, cmd.Kinds));
            case PaletteLists.Words:
                if (q.Length == 0) return (One(cmd.Title, Hint("Type what you are after…", cmd.Symbol)), null);
                return (One(cmd.Title, SearchRow(arg)), null);
            case PaletteLists.Task:
            {
                var items = new List<PaletteItem>();
                if (arg.Length > 0)
                    items.Add(new PaletteItem("task.add", $"Add “{arg}” to To Do", SymbolRegular.AddCircle24, PaletteAction.AddTask(arg)) { Sub = "A task for today", Trailing = "Enter", Filled = true });
                items.Add(new PaletteItem("task.new", "New Task…", SymbolRegular.Compose24, PaletteAction.Of(PaletteDo.NewTask)) { Sub = "With a day, a course, a priority and a repeat", Trailing = "Ctrl+N" });
                return (One(cmd.Title, items.ToArray()), null);
            }
            case PaletteLists.Commands:
            {
                var list = PaletteCommand.Matching(all, q, strict: false).Where(c => c.Name != "help").Select(c => c.SlashRow).ToArray();
                return (One("Commands", list), null);
            }
        }
        return (One(cmd.Title, cmd.SlashRow), null);
    }

    // ---- rows ----

    private static readonly PaletteItem Looking = new("looking", "Looking…", SymbolRegular.HourglassHalf24, PaletteAction.None);

    private static PaletteItem Hint(string text, SymbolRegular symbol) => new("hint", text, symbol, PaletteAction.None);

    private PaletteItem SearchRow(string words) =>
        new("search", $"Search for “{words}”", SymbolRegular.Search24, PaletteAction.Search(words)) { Sub = $"Everything in {engine.LmsName}", Trailing = "Enter" };

    /// <summary>A course's sections before its home has said which it has (the Mac's Sidebar.defaultSections).</summary>
    private static readonly SectionLink[] DefaultSections =
    [
        new() { Kind = "announcements", Label = "Announcements" },
        new() { Kind = "assignments", Label = "Assignments" },
        new() { Kind = "discussions", Label = "Discussions" },
        new() { Kind = "grades", Label = "Grades" },
        new() { Kind = "modules", Label = "Modules" },
        new() { Kind = "pages", Label = "Pages" },
        new() { Kind = "files", Label = "Files" },
        new() { Kind = "quizzes", Label = "Quizzes" },
        new() { Kind = "people", Label = "People" },
        new() { Kind = "syllabus", Label = "Syllabus" },
    ];

    private static string CodeOf(CourseRow c) => string.IsNullOrEmpty(c.Code) ? c.Name ?? "" : c.Code;

    /// <summary>The courses whose code or name holds the words, the codes that start with them first.</summary>
    private static List<CourseRow> CoursesFor(string q, Engine.Engine engine)
    {
        var s = q.ToLowerInvariant();
        if (s.Length == 0) return engine.Courses.ToList();
        var found = engine.Courses.Where(c => new[] { c.Code, c.Name ?? "", c.Nickname ?? "", c.Original ?? "" }
            .Any(x => x.ToLowerInvariant().Contains(s, StringComparison.Ordinal))).ToList();
        bool Starts(CourseRow c) => c.Code.ToLowerInvariant().StartsWith(s, StringComparison.Ordinal);
        return found.Where(Starts).Concat(found.Where(c => !Starts(c))).ToList();
    }

    /// <summary>Your courses for the words — or, when none has them all, a course and one of its sections ("bio files").</summary>
    private static List<PaletteItem> CourseItems(string q, Engine.Engine engine, int limit)
    {
        var found = CoursesFor(q, engine);
        if (found.Count > 0)
            return found.Take(limit).Select(c => new PaletteItem($"c.{c.Id}", CodeOf(c), SymbolRegular.Book24, PaletteAction.Go(Place.Home($"courses/{c.Id}")))
            {
                Sub = c.Name == CodeOf(c) ? null : c.Name, Color = c.Color, Square = true,
                Trailing = (c.Unread ?? 0) > 0 ? $"{c.Unread} new" : null,
            }).ToList();
        return SectionItems(q, engine, limit);
    }

    private static List<PaletteItem> SectionItems(string q, Engine.Engine engine, int limit)
    {
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count < 2) return [];
        var last = words[^1];
        words.RemoveAt(words.Count - 1);
        if (last.Length < 2) return [];
        var name = string.Join(" ", words);
        var outList = new List<PaletteItem>();
        foreach (var c in CoursesFor(name, engine))
        {
            var ctx = $"courses/{c.Id}";
            IEnumerable<SectionLink> secs = engine.Sections.TryGetValue(ctx, out var known) ? known : DefaultSections;
            foreach (var s in secs)
            {
                if (s.Kind == "home") continue;
                if (!s.Kind.ToLowerInvariant().StartsWith(last, StringComparison.Ordinal) && !s.Label.ToLowerInvariant().StartsWith(last, StringComparison.Ordinal)) continue;
                outList.Add(new PaletteItem($"s.{ctx}.{s.Kind}", $"{s.Label} — {CodeOf(c)}", Glyph.Section(s.Kind), PaletteAction.Go(Place.Section(ctx, s.Kind)))
                {
                    Sub = c.Name, Color = c.Color,
                });
            }
        }
        return outList.Take(limit).ToList();
    }

    private static List<PaletteItem> GroupItems(string q, Engine.Engine engine, int limit)
    {
        var s = q.ToLowerInvariant();
        var found = s.Length == 0 ? engine.Groups : engine.Groups.Where(g => g.Name.ToLowerInvariant().Contains(s, StringComparison.Ordinal)).ToList();
        return found.Take(limit).Select(g => new PaletteItem($"g.{g.Id}", g.Name, SymbolRegular.PeopleTeam24, PaletteAction.Go(Place.Home($"groups/{g.Id}")))
        {
            Sub = g.Sub ?? "Group", Color = g.Color, Filled = true,
        }).ToList();
    }

    /// <summary>/grades: the overview (with the GPA once read) and each course's grade — the courses' own scores at once,
    /// the Grades screen's (with the letter) once they are in.</summary>
    private List<PaletteItem> GradeItems(string q)
    {
        var items = new List<PaletteItem>();
        var data = gradesCache?.Data;
        if (q.Length == 0)
        {
            var gpa = data?.Gpa is { } g ? g.ToString("0.00", CultureInfo.InvariantCulture) : null;
            items.Add(new PaletteItem("grades.all", "Grades Overview", SymbolRegular.DataBarVertical24, PaletteAction.Go(Place.Grades))
            {
                Sub = gpa != null ? $"GPA {gpa} · every course" : "Every course, and your GPA", Trailing = gpa,
            });
        }
        if (data != null)
        {
            foreach (var r in data.Rows)
            {
                if (q.Length > 0 && !r.Code.ToLowerInvariant().Contains(q, StringComparison.Ordinal) && !(r.Name ?? "").ToLowerInvariant().Contains(q, StringComparison.Ordinal)) continue;
                var score = string.Join(" · ", new[] { r.PctText, r.Letter ?? "" }.Where(x => x.Length > 0));
                items.Add(new PaletteItem($"grade.{r.Id}", r.Code, SymbolRegular.DataBarVertical24, PaletteAction.Go(Place.Section($"courses/{r.Id}", "grades")))
                {
                    Sub = $"Grades in {r.Name ?? r.Code}", Color = r.Color, Trailing = score, Filled = true,
                });
            }
        }
        else
        {
            foreach (var c in CoursesFor(q, engine))
                items.Add(new PaletteItem($"grade.{c.Id}", CodeOf(c), SymbolRegular.DataBarVertical24, PaletteAction.Go(Place.Section($"courses/{c.Id}", "grades")))
                {
                    Sub = $"Grades in {c.Name ?? CodeOf(c)}", Color = c.Color, Trailing = c.ScoreText, Filled = true,
                });
        }
        if (items.Count == 0) items.Add(Hint("No course by that name", SymbolRegular.DataBarVertical24));
        return items;
    }

    // ---- what is asked for ----

    private (DateTime At, TodoData Data)? todoCache;
    private (DateTime At, GradesData Data)? gradesCache;

    private async Task<PaletteSection?> Run(Job job, CancellationToken token)
    {
        switch (job.Kind)
        {
            case JobKind.Kinds:
            {
                await Task.Delay(280, token); // (a pause in the typing before the school is asked)
                SearchData d;
                try { d = await engine.Call<SearchData>("search", new { q = job.Q }); }
                catch { return new PaletteSection(job.Title, [Hint($"{engine.LmsName} could not be searched just now.", SymbolRegular.Warning24)]); }
                var items = new List<PaletteItem>();
                foreach (var g in d.Groups.Where(g => job.Kinds?.Contains(g.Title) == true))
                    for (var i = 0; i < g.Rows.Count; i++)
                    {
                        var r = g.Rows[i];
                        var external = r.External == true;
                        items.Add(new PaletteItem($"r.{g.Title}.{i}", r.Title, KindSymbol(g.Title), PaletteAction.Open(r.Url, r.Title, external))
                        {
                            Sub = r.Sub, Color = r.Color, Trailing = external ? "↗" : null, Filled = true,
                        });
                    }
                if (items.Count == 0)
                    items.Add(new PaletteItem("none", $"Nothing for “{job.Q}”", SymbolRegular.Search24, PaletteAction.Search(job.Q)) { Sub = "Search everything instead" });
                return new PaletteSection(job.Title, items.Take(8).ToList());
            }
            case JobKind.Due:
            {
                if (await Todo() is not { } d)
                    return new PaletteSection(job.Title, [Hint("To Do could not be read just now.", SymbolRegular.Warning24)]);
                var items = DueItems(d, job.Q, job.Overdue);
                if (items.Count == 0)
                    return new PaletteSection(job.Title, [Hint(job.Overdue ? "Nothing overdue." : "Nothing due in the next seven days.", SymbolRegular.CheckmarkCircle24)]);
                return new PaletteSection(job.Title, items);
            }
            case JobKind.Grades:
                if (await Grades() == null) return null; // (the courses' own scores stay)
                return new PaletteSection("Grades", GradeItems(job.Q));
        }
        return null;
    }

    /// <summary>The planner's work for /due (today, tomorrow, week, a name, or all that is still to come) and /overdue.</summary>
    private static List<PaletteItem> DueItems(TodoData d, string q, bool overdue)
    {
        var now = DateTime.Now;
        var today = DateTime.Today;
        (int Lo, int Hi)? span = q switch
        {
            "today" => (0, 0),
            "tomorrow" => (1, 1),
            "week" or "this week" => (0, 6),
            _ => null,
        };
        var seen = new HashSet<string>();
        var found = new List<(DateTime? Date, WorkRow Row)>();
        foreach (var r in d.Sections.SelectMany(s => s.Rows))
        {
            if (r.Done || !seen.Add(r.Id)) continue;
            var date = ParseDate(r.Date);
            if (overdue)
            {
                if (date is not { } due || due >= now) continue;
            }
            else if (span is { } sp)
            {
                if (date is not { } due) continue;
                var n = (due.Date - today).Days;
                if (n < sp.Lo || n > sp.Hi) continue;
            }
            else if (date is { } due && due < today)
            {
                continue; // (what is past its date is /overdue's)
            }
            if (span == null && q.Length > 0)
            {
                var hit = r.Title.ToLowerInvariant().Contains(q, StringComparison.Ordinal) || (r.Course ?? "").ToLowerInvariant().Contains(q, StringComparison.Ordinal);
                if (!hit) continue;
            }
            found.Add((date, r));
        }
        return found.OrderBy(f => f.Date ?? DateTime.MaxValue).Take(8).Select(f =>
        {
            var r = f.Row;
            var sub = string.Join(" · ", new[] { r.Course ?? "", r.When ?? r.Time ?? "" }.Where(x => x.Length > 0));
            return new PaletteItem($"w.{r.Id}", r.Title, Glyph.Work(r.Type ?? "assignment"), PaletteAction.Work(r.Custom == true ? null : r.Url, r.Title))
            {
                Sub = sub, Color = r.Color, Trailing = r.Flag?.Word,
            };
        }).ToList();
    }

    /// <summary>The planner's dates, as the page writes them (ISO 8601, with or without its fraction of a second), in this PC's time.</summary>
    private static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrEmpty(s) || !s.Contains('T')) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.LocalDateTime : null;
    }

    private async Task<TodoData?> Todo()
    {
        if (todoCache is { } c && (DateTime.UtcNow - c.At).TotalSeconds < 90) return c.Data;
        try
        {
            var d = await engine.Call<TodoData>("todo");
            todoCache = (DateTime.UtcNow, d);
            return d;
        }
        catch { return null; }
    }

    private async Task<GradesData?> Grades()
    {
        if (gradesCache is { } c && (DateTime.UtcNow - c.At).TotalSeconds < 120) return c.Data;
        try
        {
            var d = await engine.Call<GradesData>("grades");
            gradesCache = (DateTime.UtcNow, d);
            return d;
        }
        catch { return null; }
    }

    /// <summary>A section that answered later in the place of the one that waited for it (or after the rest).</summary>
    private static List<PaletteSection> Merge(List<PaletteSection> based, PaletteSection extra)
    {
        var outList = based.ToList();
        var i = outList.FindIndex(s => s.Title == extra.Title);
        if (i >= 0) outList[i] = extra; else outList.Add(extra);
        return outList;
    }
}

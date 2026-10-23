using System.Globalization;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
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

    private enum JobKind { Kinds, Due, Grades, Everything, Recent, Tools }

    /// <summary>What the app answers later: the school's search (for one kind, or every kind of <see cref="Kinds"/>), the
    /// planner, the grades, the files changed last, the courses' external tools.</summary>
    private sealed record Job(JobKind Kind, string Title, string Q, string[]? Kinds = null, bool Overdue = false);

    private (List<PaletteSection> Sections, Job? Job) Suggest(string raw)
    {
        var typed = Typed.Of(raw);
        if (typed.Command) return CommandSections(typed);
        var words = typed.Raw.Trim();
        if (words.Length == 0) return EmptySections();
        // (0.3.44, 0.3.56) as the web box does: from the second letter what the index holds answers at once, and
        //  everything in the school after a pause — assignments, announcements, pages, discussions, files, people (those
        //  of the kind searched in alone)
        var kinds = scope?.Kinds;
        var everything = words.Length >= 2 && PaletteSum.Answer(words) == null && kinds is not { Length: 0 } ? new Job(JobKind.Everything, "", words, kinds) : null;
        return (PlainSections(typed), everything);
    }

    /// <summary>(0.3.56) Before anything is typed: the four kinds to search in, or the kind's own things — your courses (the
    /// pane's, in its order), what is due this week, the files and pages changed last, every command.</summary>
    private (List<PaletteSection>, Job?) EmptySections()
    {
        if (scope == null)
            return ([new PaletteSection("Search in", SearchScope.All.Select(k => new PaletteItem($"scope.{k.Key}", k.Title, k.Symbol, PaletteAction.Scope(k.Key)) { Sub = k.Hint, Trailing = k.Shortcut }).ToList())], null);
        if (scope == SearchScope.Courses)
        {
            var items = CourseItems("", engine, 12);
            return ([new PaletteSection("Courses", items.Count == 0 ? [Hint("No courses yet", SymbolRegular.Book24)] : items)], null);
        }
        if (scope == SearchScope.Work) return ([new PaletteSection("Due this week", [Looking])], new Job(JobKind.Due, "Due this week", "week"));
        if (scope == SearchScope.Files) return ([new PaletteSection("Recent files", [Looking])], new Job(JobKind.Recent, "Recent files", ""));
        var all = PaletteCommand.Catalog(engine).Where(c => c.Name != "help").Select(c => c.PlainRow).ToList();
        return ([new PaletteSection("Commands", all)], null);
    }

    // ---- words typed ----

    private List<PaletteSection> PlainSections(Typed t)
    {
        var words = t.Raw.Trim();
        var q = words.ToLowerInvariant();
        var outList = new List<PaletteSection>();
        if (q.Length == 0) return outList;
        // (0.3.56) a kind searched in: its own things alone (Work's and Files' come from the search)
        if (scope == SearchScope.Courses)
        {
            var mine = CourseItems(q, engine, 12);
            return [new PaletteSection("Courses", mine.Count == 0 ? [Hint("No course by that name", SymbolRegular.Book24)] : mine)];
        }
        if (scope == SearchScope.Actions)
        {
            var list = PaletteCommand.Matching(PaletteCommand.Catalog(engine), q, strict: false).Where(c => c.Name != "help").Select(c => c.PlainRow).ToList();
            return [new PaletteSection("Commands", list.Count == 0 ? [Hint("No command by that name", SymbolRegular.AppsList24)] : list)];
        }
        if (scope != null) return outList;
        // (a sum's answer comes first and is the row Enter runs: it alone says Enter — "Enter to copy" — and the search
        //  row under it says nothing of Enter)
        var answer = PaletteSum.Answer(words);
        if (answer is { } a)
            outList.Add(new PaletteSection("Answer", [new PaletteItem("answer", a.Value, SymbolRegular.EqualCircle24, PaletteAction.Copy(a.Value)) { Sub = a.Sub, Trailing = "Enter to copy", Filled = true }]));
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
        // (0.3.56: no "Search for" row — everything the school holds answers under these as you type)
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
                return ([new PaletteSection("No command by that name", [help, SearchRow(t.Name)])], null);
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
                items.Add(new PaletteItem("task.new", "New task…", SymbolRegular.Compose24, PaletteAction.Of(PaletteDo.NewTask)) { Sub = "With a day, a course, a priority and a repeat", Trailing = "Ctrl+N" });
                return (One(cmd.Title, items.ToArray()), null);
            }
            case PaletteLists.Commands:
            {
                var list = PaletteCommand.Matching(all, q, strict: false).Where(c => c.Name != "help").Select(c => c.SlashRow).ToArray();
                return (One("Commands", list), null);
            }
            case PaletteLists.Tools:
                if (toolsCache is { } tc && (DateTime.UtcNow - tc.At).TotalSeconds < 300)
                {
                    toolsShown = tc.Data;
                    return ([ToolSection(tc.Data, arg)], null);
                }
                return (One("Open a tool", Looking), new Job(JobKind.Tools, "Open a tool", arg));
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

    /// <summary>A course's second line: its other name only when it says something the pane's name does not, else its
    /// term and teacher ("Fall 2026 · Yue Lei") — never the same course named again.</summary>
    private static string? SecondLine(CourseRow c) =>
        !string.IsNullOrEmpty(c.AltName) ? c.AltName : !string.IsNullOrEmpty(c.Detail) ? c.Detail : null;

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
                Sub = SecondLine(c), Color = c.Color, Square = true,
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
                    Sub = SecondLine(c), Color = c.Color,
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

    /// <summary>/grades: the overview (its GPA once, at its end, as the engine writes it) and each course's grade — the
    /// courses' own scores at once, the Grades screen's (with the letter, and how much is graded) once they are in. Each
    /// course is named as the pane names it; the line under it says something new.</summary>
    private List<PaletteItem> GradeItems(string q)
    {
        var items = new List<PaletteItem>();
        var data = gradesCache?.Data;
        if (q.Length == 0)
        {
            var gpa = data?.GpaText is { Length: > 0 } gt && gt != "–" ? gt : data?.GpaText == null && data?.Gpa is { } g ? Math.Round((decimal)g, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture) : null;
            items.Add(new PaletteItem("grades.all", "Grades overview", SymbolRegular.DataBarVertical24, PaletteAction.Go(Place.Grades))
            {
                Sub = gpa != null ? "Every course, and your GPA" : "Every course", Trailing = gpa != null ? $"GPA {gpa}" : null,
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
                    Sub = !string.IsNullOrEmpty(r.GradedText) ? $"Grades · {r.GradedText}" : !string.IsNullOrEmpty(r.AltName) ? $"Grades · {r.AltName}" : "Grades", Color = r.Color, Trailing = score, Filled = true,
                });
            }
        }
        else
        {
            foreach (var c in CoursesFor(q, engine))
                items.Add(new PaletteItem($"grade.{c.Id}", CodeOf(c), SymbolRegular.DataBarVertical24, PaletteAction.Go(Place.Section($"courses/{c.Id}", "grades")))
                {
                    Sub = !string.IsNullOrEmpty(c.AltName) ? $"Grades · {c.AltName}" : "Grades", Color = c.Color, Trailing = c.ScoreText, Filled = true,
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
                            // (a result elsewhere opens in Simpl's own window, not the browser: no ↗, which is the
                            //  browser's mark everywhere else)
                            Sub = r.Sub, Color = r.Color, Filled = true,
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
            case JobKind.Recent:
            {
                SearchData d;
                try { d = await engine.Call<SearchData>("searchRecent"); }
                catch { return new PaletteSection(job.Title, [Hint("Your files could not be read just now.", SymbolRegular.Warning24)]); }
                var items = d.Groups.SelectMany(g => g.Rows.Select((r, i) => new PaletteItem($"f.{g.Title}.{i}", r.Title, KindSymbol(g.Title), PaletteAction.Open(r.Url, r.Title, r.External == true))
                {
                    Sub = r.Sub, Color = r.Color, Filled = true,
                })).Take(16).ToList();
                return new PaletteSection(job.Title, items.Count == 0 ? [Hint("No files in your courses yet.", SymbolRegular.Folder24)] : items);
            }
            case JobKind.Tools:
            {
                if (await Tools() is not { } d)
                    return new PaletteSection(job.Title, [Hint("Your courses’ tools could not be read just now.", SymbolRegular.Warning24)]);
                toolsShown = d;
                return ToolSection(d, job.Q);
            }
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

    /// <summary>The search's kinds in the order the web box shows them; never Courses (the app's own rows, listed already).</summary>
    private static readonly string[] KindOrder = ["Assignments", "Announcements", "Pages", "Discussions", "Files", "People"];

    /// <summary>(0.3.44, 0.3.56) What is typed searched, each kind its own section (five rows at most) after the app's own:
    /// what the engine's index holds at once (searchLocal), then — once the typing pauses — the school's answer for the
    /// rest (search); a kind searched in, its own groups alone. Nothing found: says so.</summary>
    private async Task Everything(Job job, List<PaletteSection> based, Func<bool> current, CancellationToken token)
    {
        var found = new List<PaletteSection>();
        try
        {
            var near = await engine.Call<SearchData>("searchLocal", new { q = job.Q });
            if (!current()) return;
            found = KindSections(near, job.Kinds);
            if (found.Count > 0) { Publish(Compose(based, found), keepChoice: true); Shot.Moved(); }
        }
        catch { /* the index not ready: the school answers */ }
        await Task.Delay(280, token); // (a pause in the typing before the school is asked)
        SearchData? all = null;
        try { all = await engine.Call<SearchData>("search", new { q = job.Q }); }
        catch { /* what the index gave stays */ }
        if (!current()) return;
        if (all != null) found = KindSections(all, job.Kinds);
        var outList = Compose(based, found);
        if (outList.Count == 0 || (job.Kinds != null && found.Count == 0))
            outList.Add(new PaletteSection("No results", [Hint($"Nothing for “{job.Q}”", SymbolRegular.Search24)]));
        Publish(outList, keepChoice: true);
        Shot.Moved();
    }

    private static List<PaletteSection> KindSections(SearchData d, string[]? kinds) =>
        KindOrder.Where(k => kinds == null || kinds.Contains(k)).Select(k =>
        {
            var g = d.Groups.FirstOrDefault(x => x.Title == k);
            if (g == null) return null;
            // (a result elsewhere opens in Simpl's own window, not the browser: no ↗, which is the browser's mark everywhere else)
            var items = g.Rows.Take(5).Select((r, i) => new PaletteItem($"r.{g.Title}.{i}", r.Title, KindSymbol(g.Title), PaletteAction.Open(r.Url, r.Title, r.External == true))
            {
                Sub = r.Sub, Color = r.Color, Filled = true,
            }).ToList();
            return items.Count > 0 ? new PaletteSection(k, items) : null;
        }).OfType<PaletteSection>().ToList();

    private static List<PaletteSection> Compose(List<PaletteSection> based, List<PaletteSection> found)
    {
        var shown = based.Select(s => s.Title).ToHashSet();
        return based.Concat(found.Where(f => !shown.Contains(f.Title))).ToList();
    }

    // ---- /open ----

    private (DateTime At, Screens.ExternalToolsScreen.Data Data)? toolsCache;
    /// <summary>The tools /open listed last, which a row picked is opened from.</summary>
    private Screens.ExternalToolsScreen.Data? toolsShown;

    private async Task<Screens.ExternalToolsScreen.Data?> Tools()
    {
        if (toolsCache is { } c && (DateTime.UtcNow - c.At).TotalSeconds < 300) return c.Data;
        try
        {
            var d = await engine.Call<Screens.ExternalToolsScreen.Data>("externalTools");
            toolsCache = (DateTime.UtcNow, d);
            return d;
        }
        catch { return null; }
    }

    /// <summary>(0.3.56, the web's /open) /open's list: the tools whose names hold what is typed — one used through
    /// assignments or module items asks which of them next ("/open Knewton Alta › "); one that is a course's own tab opens
    /// there, a row for each course. After the ›, that tool's assignments and module items in every course — what is still
    /// to do first, the soonest first — narrowed by what is typed after it.</summary>
    private static PaletteSection ToolSection(Screens.ExternalToolsScreen.Data d, string raw)
    {
        var bar = raw.IndexOf('›');
        if (bar >= 0)
        {
            var name = raw[..bar].Trim().ToLowerInvariant();
            var q = raw[(bar + 1)..].Trim().ToLowerInvariant();
            if (d.Rows.FirstOrDefault(t => t.Name.ToLowerInvariant() == name) is not { } tool)
                return new PaletteSection("Open a tool", [Hint("No tool by that name in your courses", SymbolRegular.PuzzlePiece24)]);
            var places = new List<(Screens.ExternalToolsScreen.Course C, Screens.ExternalToolsScreen.Item It, int N)>();
            var n = 0;
            foreach (var c in tool.Courses)
                foreach (var it in c.Items)
                {
                    n++;
                    if (q.Length > 0 && !it.Title.ToLowerInvariant().Contains(q, StringComparison.Ordinal) && !c.Name.ToLowerInvariant().Contains(q, StringComparison.Ordinal)) continue;
                    places.Add((c, it, n));
                }
            // (each course's list is in its order already: still to do, soonest first; the courses' merged the same way)
            var ordered = places
                .OrderBy(p => p.It.Upcoming ? 0 : 1)
                .ThenBy(p => p.It.Upcoming ? ParseDate(p.It.Due) ?? DateTime.MaxValue : DateTime.MaxValue)
                .ThenBy(p => p.N);
            var items = ordered.Select(p => new PaletteItem($"tp.{p.C.Id}.{p.It.Kind}.{p.It.Id}", p.It.Title, p.It.Kind == "moduleItem" ? SymbolRegular.BoxMultiple24 : SymbolRegular.DocumentText24,
                PaletteAction.ToolPlace(new ToolPick(tool.Id, p.C.Id, p.It.Kind, p.It.Id)))
            {
                Sub = string.Join(" · ", new[] { p.C.Name, p.It.Sub ?? "" }.Where(x => x.Length > 0)), Color = p.C.Color, Trailing = p.It.Status?.Word, Filled = true,
            }).ToList();
            return new PaletteSection($"{tool.Name}: which assignment?", items.Count == 0 ? [Hint($"Nothing by that name in {tool.Name}", SymbolRegular.DocumentText24)] : items);
        }
        var words = raw.Trim().ToLowerInvariant();
        var rows = new List<PaletteItem>();
        foreach (var t in d.Rows)
        {
            if (words.Length > 0 && !t.Name.ToLowerInvariant().Contains(words, StringComparison.Ordinal) && !t.Host.ToLowerInvariant().Contains(words, StringComparison.Ordinal)) continue;
            if (t.Instances > 0)
            {
                var line = $"{(t.Courses.Count == 1 ? t.Courses[0].Name : $"{t.Courses.Count} courses")} · {t.Instances} {(t.Instances == 1 ? "assignment" : "assignments")}";
                rows.Add(new PaletteItem($"tool.{t.Id}", t.Name, SymbolRegular.PuzzlePiece24, PaletteAction.Fill($"/open {t.Name} › ")) { Sub = line, Trailing = "Choose…" });
            }
            else
                foreach (var c in t.Courses)
                    rows.Add(new PaletteItem($"tool.{t.Id}.{c.Id}", t.Name, SymbolRegular.PuzzlePiece24, PaletteAction.ToolPlace(new ToolPick(t.Id, c.Id, null, null))) { Sub = c.Name, Color = c.Color, Filled = true });
        }
        return new PaletteSection("Open a tool", rows.Count == 0 ? [Hint("No tool by that name in your courses", SymbolRegular.PuzzlePiece24)] : rows);
    }

    /// <summary>A place /open listed, opened as External Tools opens it (Knewton Alta's and Macmillan's in their own windows).</summary>
    private void OpenToolPlace(ToolPick pick)
    {
        if (toolsShown?.Rows.FirstOrDefault(t => t.Id == pick.Tool) is not { } tool) return;
        if (tool.Courses.FirstOrDefault(c => c.Id == pick.Course) is not { } course) return;
        var it = pick.Kind == null ? null : course.Items.FirstOrDefault(i => i.Kind == pick.Kind && i.Id == pick.Item);
        Screens.ExternalToolsScreen.Open(engine, tool, course, it);
    }

    /// <summary>A section that answered later in the place of the one that waited for it — the same title, or (0.3.56:
    /// /open's "Looking…" answered as "Knewton Alta: which assignment?") the one that held "Looking…" — or after the rest.</summary>
    private static List<PaletteSection> Merge(List<PaletteSection> based, PaletteSection extra)
    {
        var outList = based.ToList();
        var i = outList.FindIndex(s => s.Title == extra.Title);
        if (i < 0) i = outList.FindIndex(s => s.Items.Any(it => it.Id == Looking.Id));
        if (i >= 0) outList[i] = extra; else outList.Add(extra);
        return outList;
    }
}

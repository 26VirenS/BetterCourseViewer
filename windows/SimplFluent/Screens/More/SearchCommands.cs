using Simpl.Engine;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

/// <summary>What a command takes before it can run (the Mac's PaletteCommand.Lists).</summary>
internal enum CommandTakes { Nothing, Courses, Groups, Grades, Due, Overdue, Words, Task, Commands, Kinds }

/// <summary>
/// A command of the search box's, as the Mac's (SearchCommands.swift, the web box's hub.js COMMANDS): its name and the
/// other names it answers to, the line under it, its symbol, its shortcut on Windows, what it takes, and what it does.
/// </summary>
internal sealed class SearchCommand
{
    public string Name { get; init; } = "";
    public string[] Aliases { get; init; } = Array.Empty<string>();
    public string Title { get; init; } = "";
    public string Hint { get; init; } = "";
    public SymbolRegular Symbol { get; init; }
    /// <summary>Its shortcut, where Windows has one (Ctrl+N, Ctrl+R …).</summary>
    public string? Key { get; init; }
    public CommandTakes Takes { get; init; }
    /// <summary>The search's own groups it lists (Assignments, Files …).</summary>
    public string[] Kinds { get; init; } = Array.Empty<string>();
    /// <summary>Runs as it stands even though it takes something (Grades opens Grades).</summary>
    public bool Quick { get; init; }
    /// <summary>What it does on its own; for one that takes something, the nearest the window does without the Mac's
    /// field of suggestions (All Courses for a course, To Do for what is due).</summary>
    public Action? Act { get; init; }

    public IEnumerable<string> Names => Aliases.Prepend(Name);

    /// <summary>It takes something first (the Mac puts "/name " in the field to list it).</summary>
    public bool Fills => Takes != CommandTakes.Nothing && !Quick;

    /// <summary>How well a typed name fits: the name itself, a name or the title it starts, then (unless strict) a word
    /// inside a name or the line under it. Null: not at all.</summary>
    public int? Rank(string typed, bool strict)
    {
        var s = typed.ToLowerInvariant();
        if (s.Length == 0) return 3;
        if (Names.Contains(s)) return 0;
        if (Names.Any(n => n.StartsWith(s, StringComparison.Ordinal)) || Title.ToLowerInvariant().StartsWith(s, StringComparison.Ordinal)) return 1;
        if (!strict && (Names.Any(n => n.Contains(s)) || Hint.ToLowerInvariant().Contains(s) || Title.ToLowerInvariant().Contains(s))) return 2;
        return null;
    }
}

/// <summary>The search box's commands, as the Search screen shows them (the Mac's SearchPalette.catalog and commandItems).</summary>
internal static class SearchCommands
{
    /// <summary>The commands whose names the words start (every one, for no words), as the Search screen shows them.</summary>
    public static List<SearchCommand> For(string words, Engine.Engine engine)
    {
        var all = Catalog(engine);
        var q = words.Trim().ToLowerInvariant();
        if (q.Length == 0) return all.Where(c => c.Name != "help").ToList();
        if (q.Length < 2) return new();
        return Matching(all, q, strict: true).Take(4).ToList();
    }

    /// <summary>The commands a typed name could mean, best first.</summary>
    public static List<SearchCommand> Matching(List<SearchCommand> all, string typed, bool strict) =>
        all.Select((c, i) => (Rank: c.Rank(typed, strict), Index: i, Command: c))
            .Where(x => x.Rank != null)
            .OrderBy(x => x.Rank).ThenBy(x => x.Index)
            .Select(x => x.Command).ToList();

    /// <summary>Every command the box knows, as the Mac's where Windows has the same thing (the Mac's tour is not here).</summary>
    public static List<SearchCommand> Catalog(Engine.Engine e)
    {
        var lms = e.LmsName;
        var canvas = !e.OnBrightspace; // (Brightspace has no Inbox or groups here)
        var c = new List<SearchCommand>
        {
            new() { Name = "dashboard", Aliases = new[] { "home", "today" }, Title = "Dashboard", Hint = "What’s due and new, at a glance", Symbol = SymbolRegular.Home24, Act = () => e.Go(Place.Dashboard) },
            new() { Name = "todo", Aliases = new[] { "tasks", "planner" }, Title = "To Do", Hint = "Everything to do this week", Symbol = SymbolRegular.TaskListLtr24, Act = () => e.Go(Place.Todo) },
            new() { Name = "calendar", Aliases = new[] { "cal", "schedule" }, Title = "Calendar", Hint = "Your courses’ calendar", Symbol = SymbolRegular.CalendarLtr24, Act = () => e.Go(Place.Calendar) },
            new() { Name = "grades", Aliases = new[] { "gpa", "grade", "marks" }, Title = "Grades", Hint = "Your GPA and every course’s grade", Symbol = SymbolRegular.DataBarVertical24, Takes = CommandTakes.Grades, Quick = true, Act = () => e.Go(Place.Grades) },
            new() { Name = "notifications", Aliases = new[] { "alerts", "activity" }, Title = "Notifications", Hint = "What’s new in your courses", Symbol = SymbolRegular.Alert24, Act = () => e.Go(Place.Notifications) },
        };
        if (canvas) c.Add(new() { Name = "inbox", Aliases = new[] { "messages", "mail", "conversations" }, Title = "Inbox", Hint = "Your messages", Symbol = SymbolRegular.Mail24, Act = () => e.Go(Place.Inbox) });
        c.Add(new() { Name = "courses", Aliases = new[] { "classes" }, Title = "All Courses", Hint = "Every course you are in", Symbol = SymbolRegular.Library24, Act = () => e.Go(Place.Courses) });
        c.Add(new() { Name = "course", Aliases = new[] { "class", "go", "open" }, Title = "Open a Course", Hint = "One of your courses, from All Courses", Symbol = SymbolRegular.Book24, Takes = CommandTakes.Courses, Act = () => e.Go(Place.Courses) });
        if (canvas)
        {
            c.Add(new() { Name = "groups", Title = "Groups", Hint = "Your groups", Symbol = SymbolRegular.People24, Act = () => e.Go(Place.Groups) });
            c.Add(new() { Name = "group", Aliases = new[] { "team" }, Title = "Open a Group", Hint = "One of your groups", Symbol = SymbolRegular.PeopleTeam24, Takes = CommandTakes.Groups, Act = () => e.Go(Place.Groups) });
        }
        c.Add(new() { Name = "tools", Aliases = new[] { "tool", "widgets", "timer", "calculator" }, Title = "Tools", Hint = "Timer, calculator, flashcards and more", Symbol = SymbolRegular.Toolbox24, Act = () => e.Go(Place.Tools) });
        c.Add(new() { Name = "external", Aliases = new[] { "external tools", "lti", "apps" }, Title = "External Tools", Hint = "Every tool your courses use", Symbol = SymbolRegular.PuzzlePiece24, Act = () => e.Go(Place.ExternalTools) });
        c.Add(new() { Name = "due", Aliases = new[] { "upcoming", "soon", "week" }, Title = "Due", Hint = "What is due: today, tomorrow, this week", Symbol = SymbolRegular.Clock24, Takes = CommandTakes.Due, Act = () => e.Go(Place.Todo) });
        c.Add(new() { Name = "overdue", Aliases = new[] { "late", "missing" }, Title = "Overdue", Hint = "Past its date and not handed in", Symbol = SymbolRegular.ErrorCircle24, Takes = CommandTakes.Overdue, Act = () => e.Go(Place.Todo) });
        c.Add(new() { Name = "assignment", Aliases = new[] { "hw", "homework", "assignments" }, Title = "Assignments", Hint = "Find an assignment", Symbol = SymbolRegular.DocumentText24, Takes = CommandTakes.Kinds, Kinds = new[] { "Assignments" } });
        c.Add(new() { Name = "quiz", Aliases = new[] { "quizzes", "test", "exam" }, Title = "Quizzes", Hint = "Find a quiz", Symbol = SymbolRegular.TaskListLtr24, Takes = CommandTakes.Kinds, Kinds = new[] { "Assignments" } });
        c.Add(new() { Name = "announcement", Aliases = new[] { "ann", "news", "announcements" }, Title = "Announcements", Hint = "Find an announcement", Symbol = SymbolRegular.Megaphone24, Takes = CommandTakes.Kinds, Kinds = new[] { "Announcements" } });
        c.Add(new() { Name = "discussion", Aliases = new[] { "disc", "thread", "discussions" }, Title = "Discussions", Hint = "Find a discussion", Symbol = SymbolRegular.ChatMultiple24, Takes = CommandTakes.Kinds, Kinds = new[] { "Discussions" } });
        c.Add(new() { Name = "page", Aliases = new[] { "wiki", "pages" }, Title = "Pages", Hint = "Find a page in your courses", Symbol = SymbolRegular.DocumentOnePage24, Takes = CommandTakes.Kinds, Kinds = new[] { "Pages" } });
        c.Add(new() { Name = "file", Aliases = new[] { "files", "read", "download", "pdf" }, Title = "Files", Hint = "Find a course file — it opens in its app", Symbol = SymbolRegular.Folder24, Takes = CommandTakes.Kinds, Kinds = new[] { "Files" } });
        c.Add(new() { Name = "people", Aliases = new[] { "person", "who", "classmate", "teacher", "ta" }, Title = "People", Hint = "Find someone in your courses", Symbol = SymbolRegular.PersonCircle24, Takes = CommandTakes.Kinds, Kinds = new[] { "People" } });
        c.Add(new() { Name = "find", Aliases = new[] { "search", "show", "what", "whats" }, Title = "Search", Hint = "Search everything for the words", Symbol = SymbolRegular.Search24, Takes = CommandTakes.Words });
        c.Add(new() { Name = "note", Aliases = new[] { "task", "remind", "add" }, Title = "Add a Task", Hint = "A task in To Do, with a day, a course and a priority", Symbol = SymbolRegular.AddCircle24, Key = "Ctrl+N", Takes = CommandTakes.Task, Act = e.AskNewTask });
        if (canvas) c.Add(new() { Name = "message", Aliases = new[] { "compose", "write", "email", "send" }, Title = "New Message", Hint = "Write to a teacher or a classmate", Symbol = SymbolRegular.Compose24, Act = e.AskNewMessage });
        c.Add(new() { Name = "reload", Aliases = new[] { "refresh", "update" }, Title = "Reload", Hint = $"Everything asked of {lms} afresh", Symbol = SymbolRegular.ArrowClockwise24, Key = "Ctrl+R", Act = e.Refresh });
        c.Add(new() { Name = "back", Aliases = new[] { "previous" }, Title = "Back", Hint = "Where you were before", Symbol = SymbolRegular.ArrowLeft24, Key = "Alt+←", Act = e.GoBack });
        c.Add(new() { Name = "forward", Aliases = new[] { "next" }, Title = "Forward", Hint = "Where you went next", Symbol = SymbolRegular.ArrowRight24, Key = "Alt+→", Act = e.GoForward });
        c.Add(new() { Name = "dark", Aliases = new[] { "night" }, Title = "Dark Appearance", Hint = "Simpl in dark, whatever Windows is set to", Symbol = SymbolRegular.WeatherMoon24, Act = () => Appearance.Look = "dark" });
        c.Add(new() { Name = "light", Aliases = new[] { "day" }, Title = "Light Appearance", Hint = "Simpl in light, whatever Windows is set to", Symbol = SymbolRegular.WeatherSunny24, Act = () => Appearance.Look = "light" });
        c.Add(new() { Name = "system", Aliases = new[] { "auto", "appearance", "theme", "look" }, Title = "System Appearance", Hint = "Light or dark as Windows is set", Symbol = SymbolRegular.DarkTheme24, Act = () => Appearance.Look = "system" });
        c.Add(new() { Name = "settings", Aliases = new[] { "preferences", "options", "prefs" }, Title = "Settings", Hint = "Simpl’s settings", Symbol = SymbolRegular.Settings24, Key = "Ctrl+,", Act = e.AskSettings });
        c.Add(new() { Name = "whatsnew", Aliases = new[] { "changes", "version", "new", "release" }, Title = "What’s New", Hint = "What changed in this version", Symbol = SymbolRegular.Sparkle24, Act = () => e.ShowWhatsNew(WhatsNew.All) });
        c.Add(new() { Name = "setup", Aliases = new[] { "goals" }, Title = "Guided Setup", Hint = "Choose your courses and goals again", Symbol = SymbolRegular.CheckmarkCircle24, Act = e.AskSetup });
        c.Add(new() { Name = "browser", Aliases = new[] { "web", "site", "canvas", "brightspace" }, Title = $"Open {lms} in Browser", Hint = "The school’s own site", Symbol = SymbolRegular.Globe24, Act = () => Engine.Engine.OpenExternally(e.BaseUrl) });
        c.Add(new() { Name = "help", Aliases = new[] { "?", "commands" }, Title = "Commands", Hint = "Every command", Symbol = SymbolRegular.AppsList24, Takes = CommandTakes.Commands, Act = () => e.Go(Place.Search("")) });
        return c;
    }
}

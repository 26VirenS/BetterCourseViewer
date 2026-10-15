using Simpl.Engine;
using Wpf.Ui.Controls;

namespace Simpl.Shell;

/// <summary>What a suggestion of the search box's does when it is picked (the Mac's PaletteAction).</summary>
public enum PaletteDo
{
    Go, Open, Work, Search, Fill, NewTask, AddTask, NewMessage, Refresh, ReloadPage, Settings, WhatsNew, Setup, Tour, Look,
    Back, Forward, Browser, Copy, Tool, Nothing,
}

/// <summary>
/// A suggestion's action and what it needs: the place it goes to (<see cref="To"/>), and the address, the words, the
/// text the box is filled with, the look or what is copied (<see cref="Text"/>), with a title for what is opened.
/// </summary>
public sealed record PaletteAction(PaletteDo Do, Place? To = null, string Text = "", string Title = "", bool External = false)
{
    public static readonly PaletteAction None = new(PaletteDo.Nothing);

    public static PaletteAction Of(PaletteDo d) => new(d);
    public static PaletteAction Go(Place place) => new(PaletteDo.Go, place);
    public static PaletteAction Open(string url, string title, bool external) => new(PaletteDo.Open, Text: url, Title: title, External: external);
    /// <summary>A piece of work from the planner: its page, or To Do for a task of your own (no address).</summary>
    public static PaletteAction Work(string? url, string title) => new(PaletteDo.Work, Text: url ?? "", Title: title);
    public static PaletteAction Search(string words) => new(PaletteDo.Search, Text: words);
    /// <summary>The box given these words ("/course "), to list what the command takes.</summary>
    public static PaletteAction Fill(string text) => new(PaletteDo.Fill, Text: text);
    public static PaletteAction AddTask(string title) => new(PaletteDo.AddTask, Text: title);
    /// <summary>"light", "dark" or "system" (Settings ▸ Appearance's choice).</summary>
    public static PaletteAction Look(string look) => new(PaletteDo.Look, Text: look);
    public static PaletteAction Copy(string text) => new(PaletteDo.Copy, Text: text);
    /// <summary>A tool opened in its popup over where you are, by its key ("lewis").</summary>
    public static PaletteAction Tool(string key) => new(PaletteDo.Tool, Text: key);

    /// <summary>Picked, it does something (not a hint, nor "Looking…").</summary>
    public bool Acts => Do != PaletteDo.Nothing;
}

/// <summary>
/// One suggestion (the Mac's PaletteItem): what it says and the line under it, its symbol — in a course's colour where it
/// is one of a course's, or the course's own square — a shortcut or a score at its end, and what it does.
/// </summary>
public sealed record PaletteItem(string Id, string Title, SymbolRegular Symbol, PaletteAction Action)
{
    public string? Sub { get; init; }
    /// <summary>What a command takes, drawn after its name as grey placeholders ("‹course› ‹section›"), never as part of it.</summary>
    public string? Args { get; init; }
    /// <summary>A course's (or a group's) colour, as the page gives it: the symbol drawn in it.</summary>
    public string? Color { get; init; }
    public string? Trailing { get; init; }
    /// <summary>A course itself: its colour's rounded square with its first letter (the navigation pane's), not a symbol.</summary>
    public bool Square { get; init; }
    public bool Filled { get; init; }
}

/// <summary>Suggestions under one heading (Answer, Commands, Courses, Groups, Search …).</summary>
public sealed record PaletteSection(string Title, IReadOnlyList<PaletteItem> Items);

/// <summary>What a command that takes something lists once a space follows its name.</summary>
internal enum PaletteLists { Courses, Groups, Grades, Due, Overdue, Kinds, Words, Task, Commands }

/// <summary>
/// A command of the search box's, as the web box's (hub.js COMMANDS) in the app's own terms (the Mac's PaletteCommand):
/// its name and the other names it answers to, the line under it, what it takes (picking it then puts "/name " in the
/// box, to list that), and what it does on its own.
/// </summary>
internal sealed class PaletteCommand
{
    public required string Name { get; init; }
    public string[] Aliases { get; init; } = [];
    public required string Title { get; init; }
    public required string Hint { get; init; }
    public SymbolRegular Symbol { get; init; }
    public string? Key { get; init; }
    public string? Takes { get; init; }
    public PaletteLists? Lists { get; init; }
    /// <summary>The search's own groups a command of <see cref="PaletteLists.Kinds"/> finds in (Assignments, Files …).</summary>
    public string[] Kinds { get; init; } = [];
    /// <summary>Runs as it stands even though it takes something (/grades opens Grades; "/grades bio" lists the course's).</summary>
    public bool Quick { get; init; }
    public PaletteAction Action { get; init; } = PaletteAction.None;

    public IEnumerable<string> Names => Aliases.Prepend(Name);

    private bool Fills => Lists != null && !Quick;

    /// <summary>How well a typed name fits: the name itself, a name it starts or the title it starts, then (unless strict)
    /// a word inside a name or the line under it. Null: not at all.</summary>
    public int? Rank(string typed, bool strict)
    {
        var s = typed.ToLowerInvariant();
        if (s.Length == 0) return 3;
        if (Names.Contains(s)) return 0;
        var title = Title.ToLowerInvariant();
        if (Names.Any(n => n.StartsWith(s, StringComparison.Ordinal)) || title.StartsWith(s, StringComparison.Ordinal)) return 1;
        if (!strict && (Names.Any(n => n.Contains(s, StringComparison.Ordinal)) || Hint.ToLowerInvariant().Contains(s, StringComparison.Ordinal) || title.Contains(s, StringComparison.Ordinal))) return 2;
        return null;
    }

    /// <summary>Its row under "/": "/course" with what it takes after it as placeholders ("‹course› ‹section›"), the
    /// line under it saying what it does with an example.</summary>
    public PaletteItem SlashRow =>
        new($"cmd.{Name}", $"/{Name}", Symbol, Fills ? PaletteAction.Fill($"/{Name} ") : Action) { Sub = Hint, Trailing = Key, Args = Fills ? Takes : null };

    /// <summary>Its row among words typed: "Grades", what it does, and its shortcut (or its name, for one that takes something).</summary>
    public PaletteItem PlainRow =>
        new($"cmd.{Name}", Title, Symbol, Fills ? PaletteAction.Fill($"/{Name} ") : Action) { Sub = Hint, Trailing = Key ?? (Fills ? $"/{Name}" : null) };

    // ---- the commands ----

    private static PaletteCommand C(string name, string[] aliases, string title, string hint, SymbolRegular symbol, PaletteAction? action = null,
        string? key = null, string? takes = null, PaletteLists? lists = null, string[]? kinds = null, bool quick = false) => new()
        {
            Name = name, Aliases = aliases, Title = title, Hint = hint, Symbol = symbol, Action = action ?? PaletteAction.None,
            Key = key, Takes = takes, Lists = lists, Kinds = kinds ?? [], Quick = quick,
        };

    /// <summary>Every command the box knows, as the web box's (hub.js COMMANDS) where the app has the same thing; the
    /// shortcuts are Windows' (Ctrl for ⌘, Alt+Left and Alt+Right for Back and Forward), the names in Windows 11's
    /// sentence case (a place keeps the name its page has), and what each takes written as placeholders.</summary>
    public static List<PaletteCommand> Catalog(Engine.Engine engine)
    {
        var lms = engine.LmsName;
        var canvas = !engine.OnBrightspace; // (Brightspace has no Inbox or groups here)
        var c = new List<PaletteCommand>
        {
            C("dashboard", ["home", "today"], "Dashboard", "What’s due and new, at a glance", SymbolRegular.Home24, PaletteAction.Go(Place.Dashboard), "Ctrl+1"),
            C("todo", ["tasks", "planner"], "To Do", "Everything to do this week", SymbolRegular.TaskListLtr24, PaletteAction.Go(Place.Todo), "Ctrl+2"),
            C("calendar", ["cal", "schedule"], "Calendar", "Your courses’ calendar", SymbolRegular.CalendarLtr24, PaletteAction.Go(Place.Calendar), "Ctrl+3"),
            C("grades", ["gpa", "grade", "marks"], "Grades", "Your GPA and every course’s grade — or one: /grades bio", SymbolRegular.DataBarVertical24,
                PaletteAction.Go(Place.Grades), "Ctrl+4", "‹course›", PaletteLists.Grades, quick: true),
            C("notifications", ["alerts", "activity"], "Notifications", "What’s new in your courses", SymbolRegular.Alert24, PaletteAction.Go(Place.Notifications), "Ctrl+5"),
        };
        if (canvas) c.Add(C("inbox", ["messages", "mail", "conversations"], "Inbox", "Your messages", SymbolRegular.Mail24, PaletteAction.Go(Place.Inbox), "Ctrl+6"));
        c.Add(C("courses", ["classes"], "All courses", "Every course you are in", SymbolRegular.Library24, PaletteAction.Go(Place.Courses)));
        c.Add(C("course", ["class", "go", "open"], "Open a course", "A course — or one of its sections: /course bio files", SymbolRegular.Book24,
            takes: "‹course› ‹section›", lists: PaletteLists.Courses));
        if (canvas)
        {
            c.Add(C("groups", [], "Groups", "Your groups", SymbolRegular.People24, PaletteAction.Go(Place.Groups)));
            c.Add(C("group", ["team"], "Open a group", "One of your groups", SymbolRegular.PeopleTeam24, takes: "‹group›", lists: PaletteLists.Groups));
        }
        c.Add(C("tools", ["tool", "widgets", "timer", "calculator"], "Tools", "Timer, calculator, flashcards and more", SymbolRegular.Toolbox24, PaletteAction.Go(Place.Tools), "Ctrl+7"));
        c.AddRange(ChemistryTools());
        c.AddRange(PhysicsTools());
        c.AddRange(EverydayTools());
        c.Add(C("external", ["external tools", "lti", "apps"], "External tools", "Every tool your courses use", SymbolRegular.PuzzlePiece24, PaletteAction.Go(Place.ExternalTools)));
        c.Add(C("due", ["upcoming", "soon", "week"], "Due", "What is due: today, tomorrow, this week, or by name", SymbolRegular.Clock24,
            takes: "‹when or name›", lists: PaletteLists.Due));
        c.Add(C("overdue", ["late", "missing"], "Overdue", "Past its date and not handed in", SymbolRegular.ErrorCircle24, takes: "‹name›", lists: PaletteLists.Overdue));
        c.Add(C("assignment", ["hw", "homework", "assignments"], "Assignments", "Find an assignment", SymbolRegular.DocumentText24, takes: "‹name›", lists: PaletteLists.Kinds, kinds: ["Assignments"]));
        c.Add(C("quiz", ["quizzes", "test", "exam"], "Quizzes", "Find a quiz", Simpl.Kit.Glyph.Work("quiz"), takes: "‹name›", lists: PaletteLists.Kinds, kinds: ["Assignments"]));
        c.Add(C("announcement", ["ann", "news", "announcements"], "Announcements", "Find an announcement", SymbolRegular.Megaphone24, takes: "‹words›", lists: PaletteLists.Kinds, kinds: ["Announcements"]));
        c.Add(C("discussion", ["disc", "thread", "discussions"], "Discussions", "Find a discussion", SymbolRegular.ChatMultiple24, takes: "‹name›", lists: PaletteLists.Kinds, kinds: ["Discussions"]));
        c.Add(C("page", ["wiki", "pages"], "Pages", "Find a page in your courses", SymbolRegular.DocumentOnePage24, takes: "‹name›", lists: PaletteLists.Kinds, kinds: ["Pages"]));
        c.Add(C("file", ["files", "read", "download", "pdf"], "Files", "Find a course file — it opens on this PC", SymbolRegular.Folder24, takes: "‹name›", lists: PaletteLists.Kinds, kinds: ["Files"]));
        c.Add(C("people", ["person", "who", "classmate", "teacher", "ta"], "People", "Find someone in your courses", SymbolRegular.PersonCircle24, takes: "‹name›", lists: PaletteLists.Kinds, kinds: ["People"]));
        c.Add(C("find", ["search", "show", "what", "whats"], "Search", "Search everything for the words", SymbolRegular.Search24, takes: "‹words›", lists: PaletteLists.Words));
        c.Add(C("note", ["task", "remind", "add"], "Add a task", "A task for today in To Do: /note read chapter 4", SymbolRegular.AddCircle24, key: "Ctrl+N", takes: "‹task›", lists: PaletteLists.Task));
        if (canvas) c.Add(C("message", ["compose", "write", "email", "send"], "New message", "Write to a teacher or a classmate", SymbolRegular.Compose24, PaletteAction.Of(PaletteDo.NewMessage), "Ctrl+Shift+N"));
        c.Add(C("reload", ["refresh", "update"], "Reload", $"Everything asked of {lms} afresh", SymbolRegular.ArrowClockwise24, PaletteAction.Of(PaletteDo.Refresh), "Ctrl+R"));
        c.Add(C("back", ["previous"], "Back", "Where you were before", SymbolRegular.ArrowLeft24, PaletteAction.Of(PaletteDo.Back), "Alt+Left"));
        c.Add(C("forward", ["next"], "Forward", "Where you went next", SymbolRegular.ArrowRight24, PaletteAction.Of(PaletteDo.Forward), "Alt+Right"));
        c.Add(C("dark", ["night"], "Dark appearance", "Simpl in dark, whatever Windows is set to", SymbolRegular.WeatherMoon24, PaletteAction.Look("dark")));
        c.Add(C("light", ["day"], "Light appearance", "Simpl in light, whatever Windows is set to", SymbolRegular.WeatherSunny24, PaletteAction.Look("light")));
        c.Add(C("system", ["auto", "appearance", "theme", "look"], "System appearance", "Light or dark as Windows is set", SymbolRegular.DarkTheme24, PaletteAction.Look("system")));
        c.Add(C("settings", ["preferences", "options", "prefs"], "Settings", "Simpl’s settings", SymbolRegular.Settings24, PaletteAction.Of(PaletteDo.Settings), "Ctrl+,"));
        c.Add(C("whatsnew", ["changes", "version", "new", "release"], "What’s new", "What changed in this version", SymbolRegular.Sparkle24, PaletteAction.Of(PaletteDo.WhatsNew)));
        c.Add(C("setup", ["goals"], "Guided setup", "Choose your courses and goals again", SymbolRegular.CheckmarkCircle24, PaletteAction.Of(PaletteDo.Setup)));
        c.Add(C("tour", ["welcome", "guide", "intro", "onboarding"], "Take the tour", "A quick look round the app", SymbolRegular.Map24, PaletteAction.Of(PaletteDo.Tour)));
        c.Add(C("browser", ["web", "site", "canvas", "brightspace"], $"Open {lms} in browser", "The school’s own site", SymbolRegular.Globe24, PaletteAction.Of(PaletteDo.Browser)));
        c.Add(C("help", ["?", "commands"], "Commands", "Every command", SymbolRegular.AppsList24, lists: PaletteLists.Commands));
        return c;
    }

    /// <summary>
    /// The Chemistry section's tools, each opened in its popup by its name or a word a student would type for it ("periodic",
    /// "lewis", "vsepr", "orbitals", "balance"): the four split out of the Periodic Table in 0.3.2, findable from anywhere
    /// as the one tool was from its tile — by the Mac's names and words (SearchCommands.swift) — and the calculators after
    /// them. Their titles, lines and symbols are their tiles' (Tools/Chemistry/ChemTools.cs).
    /// </summary>
    internal static IEnumerable<PaletteCommand> ChemistryTools()
    {
        (string Key, string Name, string[] Aliases)[] words =
        {
            ("ptable", "ptable", ["periodic", "periodic table", "elements", "element"]),
            ("lewis", "lewis", ["lewis structures", "lewis structure", "dot structure", "resonance", "electron dot"]),
            ("molecule", "molecule", ["molecules", "molecule builder", "vsepr", "3d molecule"]),
            ("atoms", "atom", ["atoms", "orbitals", "orbital", "electron cloud", "shells", "bohr"]),
            // (the chemistry calculators (tools), by the spec's words)
            ("balance", "balance", ["equation", "reaction", "coefficients", "balance equation", "equation balancer"]),
        };
        foreach (var (key, name, aliases) in words)
            if (Simpl.Tools.ChemTools.Kind(key) is { } kind)
                yield return C(name, aliases, kind.Name, kind.Note.TrimEnd('.'), kind.Symbol, PaletteAction.Tool(key));
    }

    /// <summary>
    /// The Physics section's tools (0.3.9), each opened in its popup by its name or a word a student would type for it
    /// ("kinematics", "projectile", "dot product"), by the Mac's names and words. Their titles, lines and symbols are
    /// their tiles' (Tools/Physics/PhysTools.cs). No word here is another command's ("light" is the appearance's).
    /// </summary>
    internal static IEnumerable<PaletteCommand> PhysicsTools()
    {
        (string Key, string Name, string[] Aliases)[] words =
        {
            ("motion", "motion", ["kinematics", "projectile", "projectiles", "velocity", "acceleration", "free fall"]),
            ("vectors", "vectors", ["vector", "components", "dot product", "cross product", "resultant", "magnitude"]),
        };
        foreach (var (key, name, aliases) in words)
            if (Simpl.Tools.PhysTools.Kind(key) is { } kind)
                yield return C(name, aliases, kind.Name, kind.Note.TrimEnd('.'), kind.Symbol, PaletteAction.Tool(key));
    }

    /// <summary>
    /// (tools) The tools program's everyday tools, each opened in its popup by its name or a word a student would type
    /// for it ("/words" or "word count", "essay"), as the chemistry tools are; their titles, lines and symbols are their
    /// tiles' (Screens/More/ToolsScreen.cs). The words are the Mac's (SearchCommands.swift).
    /// </summary>
    internal static IEnumerable<PaletteCommand> EverydayTools()
    {
        (string Key, string Name, string[] Aliases)[] words =
        {
            ("words", "words", ["word count", "word counter", "characters", "reading time", "essay"]),
            ("stats", "stats", ["statistics", "mean", "standard deviation", "regression", "normal", "z-score", "t-test"]),
            ("matrix", "matrix", ["matrices", "determinant", "inverse", "linear algebra", "system of equations", "eigenvalues"]),
        };
        foreach (var (key, name, aliases) in words)
            if (Simpl.Tools.ToolsCenter.KindOf(key) is { } kind)
                yield return C(name, aliases, kind.Name, kind.Note.TrimEnd('.'), kind.Symbol, PaletteAction.Tool(key));
    }

    /// <summary>The commands a typed name could mean, best first.</summary>
    public static List<PaletteCommand> Matching(List<PaletteCommand> all, string typed, bool strict) =>
        all.Select((c, i) => (c, i, r: c.Rank(typed, strict)))
            .Where(x => x.r != null)
            .OrderBy(x => x.r)
            .ThenBy(x => x.i)
            .Select(x => x.c)
            .ToList();

    /// <summary>The command by its name or one it answers to ("/what's due" is /what).</summary>
    public static PaletteCommand? Named(List<PaletteCommand> all, string typed)
    {
        var s = typed.ToLowerInvariant();
        if (s.EndsWith("'s", StringComparison.Ordinal) || s.EndsWith("’s", StringComparison.Ordinal)) s = s[..^2];
        if (s.Length == 0) return null;
        return all.FirstOrDefault(c => c.Names.Contains(s));
    }
}

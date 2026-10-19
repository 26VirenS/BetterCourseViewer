using System.Reflection;
using Simpl.Models;

namespace Simpl.Support;

/// <summary>
/// The Windows app's own release notes (the Mac's MacWhatsNew): every version's, newest first, each note's title 22
/// characters at most and its words 60. After an update, the versions since the one last seen are shown once.
/// </summary>
public static class WhatsNew
{
    private const string SeenKey = "whatsNewSeen";

    /// <summary>This copy's version as its release names it: "0.2.0 (v001)" for the preview build of run 1.</summary>
    public static string Current => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";

    /// <summary>The version alone ("0.2.0"), as the notes name theirs.</summary>
    public static string Number => Current.Split(' ')[0];

    private static Release R(string version, string date, params (string Kind, string Title, string Body)[] notes) => new()
    {
        Version = version,
        Date = date,
        Notes = notes.Select(n => new ReleaseNote { Kind = n.Kind, Title = n.Title, Body = n.Body }).ToList(),
    };

    public static readonly List<Release> Releases = new()
    {
        R("0.3.43", "2026-10-19",
            ("fixed", "Alta Graphs Show", "A question's Desmos graph shows instead of a blank box.")),
        R("0.3.42", "2026-10-19",
            ("improved", "Sun and Moon Switch", "Alta's Light/Dark switch carries its sun and moon.")),
        R("0.3.41", "2026-10-19",
            ("improved", "Alta Controls on Top", "Light/Dark is a switch, beside Browser in the top bar.")),
        R("0.3.40", "2026-10-19",
            ("improved", "Fewer PIN Prompts", "Windows Hello asks at most once every 30 days."),
            ("improved", "Pick Your Courses", "Setup starts with no courses ticked.")),
        R("0.3.39", "2026-10-19",
            ("fixed", "Math Keyboard", "Macmillan's math keyboard sits under the answer."),
            ("fixed", "Final Score", "A finished Macmillan assignment shows its score.")),
        R("0.3.38", "2026-10-19",
            ("fixed", "Macmillan Pictures", "Pictures stay in sight on a narrower window.")),
        R("0.3.37", "2026-10-19",
            ("fixed", "Macmillan Pictures", "Units by their fields, pictures enlarge on click.")),
        R("0.3.36", "2026-10-19",
            ("fixed", "Macmillan Diagrams", "Diagrams at right, widgets readable, nothing cut off.")),
        R("0.3.35", "2026-10-18",
            ("fixed", "Macmillan Polished", "One glass panel, Alta-style Correct, readable fields.")),
        R("0.3.34", "2026-10-18",
            ("fixed", "Macmillan on Glass", "Full width, the diagram back, and dark mode.")),
        R("0.3.33", "2026-10-18",
            ("fixed", "Macmillan Fixed", "Its list, score and settings fill in; the page fits.")),
        R("0.3.32", "2026-10-18",
            ("new", "Macmillan Redesigned", "Questions, score and Submit in Simpl's own layout.")),
        R("0.3.31", "2026-10-18",
            ("new", "Appearance Mode", "⋯ > Appearance: style Simpl right on the window."),
            ("new", "Photos in Place", "Add a photo to the pane or any card from its button."),
            ("new", "Course Colours", "Pick each course's colour from the Appearance bar.")),
        R("0.3.30", "2026-10-18",
            ("new", "GeoGebra Calculator", "The Calculator is GeoGebra: graphs, CAS, 3D and more."),
            ("improved", "Scientific in the Pin", "The pinned Calculator is GeoGebra's scientific one.")),
        R("0.3.29", "2026-10-18",
            ("new", "Step-by-Step Solver", "Equations, factoring, calculus and more, with every step."),
            ("new", "Graphs and Keypad", "Graph any answer; build problems on the maths keypad.")),
        R("0.3.28", "2026-10-18",
            ("new", "Element at a Glance", "Press an element for a small window with its key facts."),
            ("improved", "Easier-to-Read Colours", "The table's coloured words and squares read clearly."),
            ("improved", "Precise Atomic Masses", "Every atomic mass as precisely as it is known.")),
        R("0.3.27", "2026-10-18",
            ("new", "Developer Tools", "Logs, system use and stats, for those who know the way in."),
            ("improved", "Critical Fixes Sooner", "Simpl looks for a critical update every minute.")),
        R("0.3.26", "2026-10-17",
            ("improved", "Front Page First", "A course leads with its front page, its news just above."),
            ("improved", "Every Link a Button", "A course's tools and links are buttons on its home."),
            ("improved", "Course Up Top", "A course chosen moves to the top of the side pane.")),
        R("0.3.25", "2026-10-17",
            ("improved", "Alta Even Faster", "Knewton Alta opens faster again, and uses less battery.")),
        R("0.3.24", "2026-10-16",
            ("fixed", "Tools Open at Once", "Open Tool loads the tool, not Canvas's Load button.")),
        R("0.3.23", "2026-10-16",
            ("fixed", "Files Open Again", "Files linked in instructions open properly again."),
            ("fixed", "Alta Maths Drawn Whole", "Brackets and roots show right away, no Light or Dark needed.")),
        R("0.3.22", "2026-10-16",
            ("improved", "Critical updates", "Simpl looks for a critical update every five minutes.")),
        R("0.3.21", "2026-10-16",
            ("improved", "Faster Alta", "Knewton Alta assignments open noticeably faster.")),
        R("0.3.20", "2026-10-16",
            ("fixed", "Alta verdict", "Correct shows only once Alta says so.")),
        R("0.3.19", "2026-10-15",
            ("new", "Alta verdict", "Correct or Incorrect shows beside Continue.")),
        R("0.3.18", "2026-10-15",
            ("improved", "Larger Alta graphs", "A question's graph is drawn twice as large.")),
        R("0.3.17", "2026-10-15",
            ("fixed", "Module files open", "A module's file opens as one from Files does."),
            ("improved", "Rubric ring", "One band of colour; each level sits at its points.")),
        R("0.3.16", "2026-10-15",
            ("fixed", "Study planner order", "Work due sooner comes first on a shared day.")),
        R("0.3.15", "2026-10-15",
            ("new", "Verb conjugations", "Conjugate any verb in five languages, every tense.")),
        R("0.3.14", "2026-10-15",
            ("new", "Circuit builder", "Build a DC circuit and read every part's V, I and P.")),
        R("0.3.13", "2026-10-15",
            ("new", "Solutions & pH", "Molarity, dilution, pH and titration curves.")),
        R("0.3.12", "2026-10-15",
            ("new", "Moles & stoichiometry", "Molar mass, conversions, limiting reagent and yields."),
            ("new", "Gas laws", "PV = nRT and the combined law, with a piston drawing.")),
        R("0.3.11", "2026-10-15",
            ("new", "Compress files", "Shrink pictures and PDFs to fit an upload limit."),
            ("new", "Study planner", "Study blocks from your due dates, a week at a time.")),
        R("0.3.10", "2026-10-15",
            ("new", "Word counter", "Words, characters, sentences and reading time as you type."),
            ("new", "Statistics", "Mean, SD, regression, normal and t distributions, t-tests."),
            ("new", "Matrix calculator", "Determinant, inverse, RREF, eigenvalues and solving.")),
        R("0.3.9", "2026-10-15",
            ("new", "Motion & projectiles", "1D motion and projectile paths, in a new Physics section."),
            ("new", "Vectors", "Add, dot and cross vectors, with components and a diagram.")),
        R("0.3.8", "2026-10-15",
            ("new", "Equation balancer", "Balance any chemical equation, with an atom count.")),
        R("0.3.7", "2026-10-15",
            ("improved", "Always up to date", "Updates now always install automatically."),
            ("improved", "Earlier versions stay", "Go back a version and Simpl stays on it until you update.")),
        R("0.3.6", "2026-10-15",
            ("new", "Dark mode for Alta", "Switch Alta's page between light and dark."),
            ("new", "Show browser", "See Alta's own page beside your objectives."),
            ("fixed", "Alta pop-ups in full", "Alta's pop-ups now show their whole message.")),
        R("0.3.5", "2026-10-15",
            ("improved", "Open pins full screen", "A pin's flyout button now opens its tool full screen."),
            ("improved", "Pins keep your work", "Your graph, numbers or search carry over to the full tool.")),
        R("0.3.4", "2026-10-15",
            ("fixed", "Popups in the middle", "Every popup opens in the middle of the window.")),
        R("0.3.3", "2026-10-15",
            ("improved", "Cleaner counter cards", "No coloured border; just each card's own colour.")),
        R("0.3.2", "2026-10-14",
            ("new", "Chemistry tools", "Periodic Table, Lewis, Molecule Builder, Atoms & Orbitals."),
            ("new", "Live updates", "Open screens check for new work every minute."),
            ("improved", "Dashboard cards", "Cards grow into their lists again, each in its colour."),
            ("improved", "Wider tool popups", "Tools span the whole window; drag an edge to resize."),
            ("improved", "Only what changed", "Your Dashboard updates just the parts that changed."),
            ("fixed", "Messages stay unread", "Checks never mark messages read while you're away.")),
        R("0.3.1", "2026-10-14",
            ("new", "More from Canvas", "History, Help and your school's own tools in the sidebar."),
            ("new", "Beta updates", "Turn on Get beta updates to try new versions first."),
            ("improved", "Tools in popups", "Tools open over your work and never block the app."),
            ("improved", "Biology tools, fixed", "Controls in sight, models full size, colours right away."),
            ("improved", "Counters as before", "Square, coloured counters and the old panels are back."),
            ("fixed", "Tools by provider", "External tools grouped by their site, in light mode.")),
        R("0.3.0", "2026-10-14",
            ("new", "A biology toolkit", "Ten new tools, from a 3D cell to genetic crosses."),
            ("new", "Send from phone", "Scan a code to hand in photos; nothing leaves your Wi-Fi."),
            ("improved", "Clearer screens", "Grades, dates and courses read one way everywhere."),
            ("improved", "Updates on their own", "New versions install themselves when you're not busy."),
            ("improved", "Lighter and faster", "Much less memory when minimized; screens open 2–3x faster."),
            ("fixed", "Fewer PIN prompts", "Windows Hello asks once, not every time.")),
        R("0.2.9", "2026-10-13",
            ("new", "Locked with Windows Hello", "Your saved sign-in opens only with your face, fingerprint or PIN."),
            ("improved", "Encrypted by Windows", "The rest of Simpl's data is sealed by Windows itself, with its key in this PC's TPM.")),
        R("0.2.8", "2026-10-13",
            ("new", "External Tools in the sidebar", "Every tool your courses use, one click away under Tools."),
            ("new", "Every place a tool is used", "Click Knewton Alta, or any tool in your assignments, to choose which one to open."),
            ("improved", "External Tools, tidied", "Cards sit side by side without gaps, and long course names fit.")),
        R("0.2.7", "2026-10-13",
            ("new", "Your data, encrypted", "Your sign-in, settings, saved pages and flashcards are encrypted, with the key held by this PC's security chip."),
            ("improved", "Pinned tools in colour", "Each pinned tool shows in its own colour on the title bar, easy to spot in light or dark.")),
        R("0.2.6", "2026-10-12",
            ("improved", "A quicker start", "Your Dashboard from last time shows at once while Simpl connects."),
            ("improved", "Smoother switching", "The sidebar and header stay put, and a page is drawn again only when it changed."),
            ("improved", "Easier on slower PCs", "Without a graphics card (a virtual machine, a remote session), Simpl drops shadows, motion and Mica to keep up."),
            ("improved", "Quieter saving", "Moving or sizing the window no longer writes to disk as it goes.")),
        R("0.2.5", "2026-10-12",
            ("fixed", "No strip behind the What-If bar", "The page runs under it now, all the way to the window's edge."),
            ("improved", "Pages scroll to the edge", "Every page, Settings and To Do scroll right to the bottom of the window.")),
        R("0.2.4", "2026-10-12",
            ("fixed", "The tour finds its part", "It scrolls to what it points at, clear of the What-If bar."),
            ("fixed", "The What-If bar", "The last rows scroll clear of it, scores and all.")),
        R("0.2.3", "2026-10-12",
            ("new", "Uninstall in Settings", "Settings ▸ General removes Simpl, and your data if you ask."),
            ("fixed", "Files in replies", "A reply's attached files show under it, even with no words."),
            ("fixed", "Setup's last step", "Once your choices are saved, it no longer says it couldn't.")),
        R("0.2.2", "2026-10-12",
            ("improved", "A smoother tour", "The light and its card glide from one part to the next."),
            ("improved", "Pins and file notices", "Pins come and go smoothly; a file's notice fades away."),
            ("fixed", "One Done, Try Again", "A tool has one Done; Try Again shows it's reading at once."),
            ("fixed", "Updates for everyone", "A new install finds each new version, betas on or not.")),
        R("0.2.1", "2026-10-12",
            ("new", "Electron cloud model", "A Cloud view: dots show where electrons are likely to be."),
            ("new", "One subshell at a time", "Pick a subshell such as 3d; the rest of the atom goes grey."),
            ("new", "An installer", "Simpl-Setup.exe installs Simpl for you; no admin needed."),
            ("new", "Updates in Settings", "Settings ▸ Updates offers each new version as it comes."),
            ("new", "Pin to the taskbar", "After the tour, Simpl offers once to stay on your taskbar."),
            ("improved", "Coloured counters", "Each Dashboard card wears its colour, as its panel does."),
            ("improved", "Tools open whole", "From the Tools page too, a tool opens over the window."),
            ("fixed", "Setup saves", "Setup saves again; quit it halfway and it asks next time."),
            ("fixed", "Pages fill the window", "An assignment and other pages use the full width.")),
        R("0.2.0", "2026-10-12",
            ("new", "Made for Windows 11", "Mica, the Settings app's navigation, cards and motion."),
            ("new", "Signing in", "Find your school, confirm it, and sign in with Simpl's own card."),
            ("new", "The Dashboard", "Counters that open into what they count; Cards, List, Activity."),
            ("new", "Every screen", "To Do, Calendar, Grades, Inbox, your courses and assignments, drawn by Simpl."),
            ("new", "A quick look", "Click a piece of work and a card grows out of it; double-click to open it."),
            ("new", "Quizzes, Alta and Macmillan", "Taken in Simpl's own popups, as on the Mac."),
            ("new", "The tools", "Twelve of them, from the Periodic Table to Flashcards; pin one to the title bar."),
            ("new", "Search and commands", "Suggestions as you type; / or Ctrl+K for every command."),
            ("improved", "Runs in the background", "Closing the window keeps Simpl in the notification area."),
            ("improved", "Dialogs", "The window dims behind every question Simpl asks.")),
        R("0.1.0", "2026-10-12",
            ("new", "Simpl for Windows", "Your Canvas, as the Mac app has it, on Windows."),
            ("new", "The sidebar", "Dashboard, To Do, Grades and your courses, a click away."),
            ("new", "Signing in", "Your school's sign-in as Simpl's own card; kept on this PC if you ask."),
            ("new", "Settings", "Appearance, notifications, grades, data and updates, from the account menu."),
            ("new", "Files to Downloads", "A file opened or downloaded lands in your Downloads folder.")),
    };

    public static WhatsNewData All => new() { Version = Current, Releases = Releases };

    /// <summary>After an update, the versions since the one last seen (null: nothing to show; never unasked in a picture).</summary>
    public static WhatsNewData? Due()
    {
        if (Launch.Get("shot") != null) return null;
        var seen = Prefs.String(SeenKey);
        var newer = Releases.Where(r => seen != null ? IsNewer(r.Version, seen) : r.Version == Number)
                            .Where(r => !IsNewer(r.Version, Number)).ToList();
        return newer.Count == 0 ? null : new WhatsNewData { Version = Current, Releases = newer };
    }

    public static void MarkSeen() => Prefs.Set(SeenKey, Number);

    /// <summary>1.10 is newer than 1.9; a missing part counts as 0.</summary>
    public static bool IsNewer(string a, string b)
    {
        var x = a.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var y = b.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var p = i < x.Length ? x[i] : 0;
            var q = i < y.Length ? y[i] : 0;
            if (p != q) return p > q;
        }
        return false;
    }
}

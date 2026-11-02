using System.Reflection;
using Simpl.Models;

namespace Simpl.Support;

/// <summary>
/// The Windows app's own release notes (the Mac's MacWhatsNew): every version's, newest first, each note's title 22
/// characters at most and its words 60 — in sentence case, as every title in the app, and saying what a student notices,
/// not how Simpl does it. A note's kind is new, improved or fixed; "dev" for one only the developer tools show, which
/// never leads (<see cref="Leading"/>). After an update, the versions since the one last seen are shown once.
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
        R("0.3.108", "2026-11-02",
            ("improved", "Whole questions", "Exported questions include the parts you scroll to see.")),
        R("0.3.107", "2026-11-01",
            ("new", "Export questions", "Macmillan, ALEKS and Alta questions saved as pictures."),
            ("new", "Every question at once", "In Macmillan, every question goes into one zip in Downloads.")),
        R("0.3.106", "2026-11-01",
            ("improved", "One kind of popup", "Every popup has the same heading, with × at the top right."),
            ("improved", "Fits smaller windows", "A compact menu and a calendar that fits narrow windows."),
            ("improved", "Clearer words", "Plain words in sentence case, and one way to write times."),
            ("fixed", "Web tools say why", "A web tool that does not load says so in plain words.")),
        R("0.3.105", "2026-11-01",
            ("new", "Community tools", "Add tools other students made, from the Tools page."),
            ("new", "Tools work offline", "Added tools remember your numbers and work offline."),
            ("fixed", "No updates in homework", "Updates wait while Alta, ALEKS or Macmillan is open."),
            ("fixed", "No more restart loop", "A failed update no longer closes and reopens Simpl.")),
        R("0.3.104", "2026-11-01", ("new", "Share your theme", "Send your theme to Simpl Community from Appearance.")),
        R("0.3.103", "2026-10-31",
            ("fixed", "No more stuck updates", "Every PC now gets the newest version."),
            ("new", "Updates within a day", "An update that has waited a day installs 30 minutes later."),
            ("improved", "No updates in quizzes", "No update ever restarts Simpl during a quiz.")),
        R("0.3.102", "2026-10-30", ("improved", "Clearer questions", "Macmillan and ALEKS questions match light and dark mode.")),
        R("0.3.101", "2026-10-30",
            ("improved", "Bigger tool popups", "A tool's popup is bigger and lists all its work in order.")),
        R("0.3.100", "2026-10-30",
            ("improved", "List or activity", "The Dashboard shows a list by day or recent activity."),
            ("improved", "Tool lists in a popup", "Choosing a tool opens its list in a popup over the page."),
            ("fixed", "No surprise reopening", "Updates wait while Simpl is closed and never jump in front.")),
        R("0.3.99", "2026-10-30",
            ("new", "Modules in the popup", "Macmillan and ALEKS module items open in their popup.")),
        R("0.3.98", "2026-10-30",
            ("new", "Homework start page", "Macmillan homework opens on a start page with Start."),
            ("improved", "Smoother questions", "Moving between Macmillan questions animates like a quiz."),
            ("improved", "A better tool list", "A tool's list shows what's next, to do, and done."),
            ("fixed", "No more Load button", "Tools that ask to load in a new window just open.")),
        R("0.3.97", "2026-10-30",
            ("fixed", "Homework in its popup", "Macmillan and ALEKS homework now opens in its own popup.")),
        R("0.3.96", "2026-10-30",
            ("fixed", "Macmillan fits", "Macmillan's page fits the popup without scrolling sideways.")),
        R("0.3.95", "2026-10-30",
            ("new", "Simpl's own buttons", "Macmillan and ALEKS get Simpl's own buttons and heading.")),
        R("0.3.94", "2026-10-30",
            ("new", "Homework like Alta", "Macmillan and ALEKS open in a glass popup, as Alta does.")),
        R("0.3.93", "2026-10-30",
            ("new", "A cleaner ALEKS", "ALEKS homework gets a cleaner look. Simpl Look turns it off.")),
        R("0.3.92", "2026-10-30",
            ("new", "A cleaner Macmillan", "Macmillan homework gets a cleaner look. Simpl Look turns it off.")),
        R("0.3.91", "2026-10-30",
            ("dev", "Record a tool's page", "Developers can record a tool's page as it changes.")),
        R("0.3.90", "2026-10-29",
            ("new", "Drag to unplan", "Drag a plan onto To Do to take it off the calendar.")),
        R("0.3.89", "2026-10-29",
            ("improved", "Faster updates", "Simpl looks for a new version every 30 seconds.")),
        R("0.3.88", "2026-10-29",
            ("improved", "This week's betas", "Everything from the betas since 0.3.57, for everyone.")),
        R("0.3.87", "2026-10-29",
            ("improved", "Quiet colour", "Silent mode keeps colour where it means something.")),
        R("0.3.86", "2026-10-29",
            ("improved", "Silent mode everywhere", "Silent mode stays on across the app, in black and white.")),
        R("0.3.85", "2026-10-29",
            ("new", "Preview files first", "Files open in a preview; download only what you keep.")),
        R("0.3.84", "2026-10-29",
            ("new", "Silent mode", "Only what's next, in big type on black. Strike items out.")),
        R("0.3.83", "2026-10-28",
            ("dev", "Simulate an update", "Developer tools can replay an update from any older version.")),
        R("0.3.82", "2026-10-27",
            ("improved", "Sidebar like the web", "Courses sit under All courses, each in its own colour.")),
        R("0.3.81", "2026-10-27",
            ("new", "Sign in to calendars", "Sign in to iCloud Calendar in Sync to see it in Simpl."),
            ("improved", "Choose calendars", "Calendars now picks what shows; Sync connects apps.")),
        R("0.3.80", "2026-10-26",
            ("new", "To Do meets Calendar", "Your tasks and your week, together in one tab.")),
        R("0.3.79", "2026-10-26",
            ("fixed", "Back in front", "After an update Simpl opens in front, with What's new."),
            ("improved", "Plan popup beside it", "A plan's popup opens next to it on the calendar.")),
        R("0.3.78", "2026-10-26",
            ("improved", "Inbox opens at once", "The Inbox is ready before you click it, like the main pages.")),
        R("0.3.77", "2026-10-26",
            ("improved", "Pages ready sooner", "Main pages and Tools open at once, and start-up is smoother.")),
        R("0.3.76", "2026-10-26",
            ("improved", "Pages open instantly", "Main pages are ready before you click them.")),
        R("0.3.75", "2026-10-26",
            ("improved", "Smoother and faster", "Simpl no longer redraws itself in the background.")),
        R("0.3.74", "2026-10-26",
            ("fixed", "Your colour throughout", "A colour you pick tints the counters, pane and window.")),
        R("0.3.73", "2026-10-26",
            ("fixed", "Events end on time", "Classes on the week show from start to end.")),
        R("0.3.72", "2026-10-26",
            ("improved", "Photo options first", "Click a card or the pane for its options, then Upload.")),
        R("0.3.71", "2026-10-26",
            ("improved", "Alta's own pop-ups", "Alta's pop-ups show just as Alta draws them.")),
        R("0.3.70", "2026-10-26",
            ("new", "Colour each card", "Give any card or the pane a colour of its own.")),
        R("0.3.69", "2026-10-26",
            ("improved", "Smoother plan dragging", "A plan follows the pointer as you drag it, then settles."),
            ("improved", "Events at their length", "A short plan on the week is as short as it lasts.")),
        R("0.3.68", "2026-10-26",
            ("new", "Click a card to upload", "In Appearance, click any card or the pane for a photo."),
            ("improved", "Controls round photos", "Zoom, Ink and Done sit around the photo you're placing.")),
        R("0.3.67", "2026-10-26",
            ("new", "Simpl Community", "Wear themes other students made, from Appearance.")),
        R("0.3.66", "2026-10-26",
            ("new", "Edit your plans", "Click a plan to rename it, repeat it, colour it and more."),
            ("new", "Stretch a plan", "Drag a plan's foot to make it longer or shorter."),
            ("new", "Group your tasks", "Drop a task or a plan onto a plan to work on them together."),
            ("improved", "Buttons grow a name", "A calendar button widens to show its name as you point.")),
        R("0.3.65", "2026-10-26",
            ("new", "Pick the agenda's days", "In Agenda, pick its days on the month on the left."),
            ("improved", "Buttons say their name", "Rest the pointer on a calendar button to see its name."),
            ("improved", "Find appointment icon", "Find appointment has its own icon, unlike Calendars.")),
        R("0.3.64", "2026-10-26",
            ("new", "To Do & Calendar", "To Do and the calendar are now one place."),
            ("new", "Pinch the week", "Pinch or Ctrl+scroll the hours to see more of the day."),
            ("improved", "Five or seven days", "Press Week again to show 5 or 7 days."),
            ("improved", "Peek past the hours", "Events above or below the view wait at its edge."),
            ("improved", "The page holds still", "Only the calendar and To Do scroll."),
            ("improved", "Quicker popups", "Click outside a preview to close it; it closes faster.")),
        R("0.3.63", "2026-10-25",
            ("new", "Plan from To Do", "To Do sits beside the week: drag work onto a time."),
            ("new", "Your Simpl calendar", "Your work and plans in Outlook, Google or your calendar."),
            ("improved", "Sealed and private", "Only your private link can open your Simpl calendar.")),
        R("0.3.62", "2026-10-25",
            ("new", "Week by the hour", "The week shows each item at its time, as long as it lasts."),
            ("new", "Drag plans to move", "Drag a plan on the week to move it to another time."),
            ("improved", "Calmer time picker", "Pull to plan says each day once and shows what it does.")),
        R("0.3.61", "2026-10-25",
            ("new", "Place your photo", "A photo opens on its card: drag, zoom and set it there."),
            ("improved", "Solid photo borders", "Words on a photo with Ink off have a solid white edge.")),
        R("0.3.60", "2026-10-25",
            ("new", "Calendar sync", "Calendar's Sync puts your work in Outlook or Google."),
            ("new", "Your calendars here", "Link Google, iCloud or Outlook to see your own events."),
            ("new", "Pull to plan", "Pull work to the right to plan a free time for it.")),
        R("0.3.59", "2026-10-25",
            ("new", "Photos without Ink", "Ink off shows a photo as it is, its words bordered in white.")),
        R("0.3.58", "2026-10-25",
            ("improved", "A steadier Macmillan", "Macmillan opens as its own page in the tool window.")),
        R("0.3.57", "2026-10-23",
            ("improved", "This week's betas", "Everything from the betas since 0.3.52, for everyone.")),
        R("0.3.56", "2026-10-23",
            ("new", "Search like the web", "A big search over the window: kinds, commands, results."),
            ("improved", "Pick the assignment", "/open on a tool lists its assignments to choose from.")),
        R("0.3.55", "2026-10-22",
            ("fixed", "Stays signed in", "A restart or an update no longer asks you to sign in.")),
        R("0.3.54", "2026-10-22",
            ("fixed", "Stays in Simpl", "No more switching to the school's own Dashboard.")),
        R("0.3.53", "2026-10-22",
            ("new", "Find appointment", "Reserve office hours from the calendar's Find appointment."),
            ("new", "Give back a time", "Right-click a reserved time to give it back.")),
        R("0.3.52", "2026-10-21",
            ("improved", "Colour kept as picked", "A custom colour is worn as you pick it, no button.")),
        R("0.3.51", "2026-10-21",
            ("dev", "Crash log", "Developer tools keep each crash and what led to it.")),
        R("0.3.50", "2026-10-20",
            ("improved", "Submit as ZIP", "Two or more files: Submit as ZIP sits beside Submit.")),
        R("0.3.49", "2026-10-20",
            ("new", "Hand in as a .zip", "Hand in a folder, or several files, as one .zip.")),
        R("0.3.48", "2026-10-20",
            ("improved", "Tidier skyline", "Four windows across each tower; the rest hide.")),
        R("0.3.47", "2026-10-20",
            ("improved", "Browser in Macmillan", "Browser sits in the popup's top bar, as in Alta.")),
        R("0.3.46", "2026-10-20",
            ("improved", "The real logo", "The icon is now drawn from the official artwork.")),
        R("0.3.45", "2026-10-20",
            ("new", "A new look", "Simpl has a new icon: a bold S with a blue dot.")),
        R("0.3.44", "2026-10-19",
            ("new", "Instant search", "Results show under the search box as you type.")),
        R("0.3.43", "2026-10-19",
            ("fixed", "Alta graphs show", "A question's Desmos graph shows instead of a blank box.")),
        R("0.3.42", "2026-10-19",
            ("improved", "Sun and moon switch", "Alta's Light/Dark switch carries its sun and moon.")),
        R("0.3.41", "2026-10-19",
            ("improved", "Alta controls on top", "Light/Dark is a switch, beside Browser in the top bar.")),
        R("0.3.40", "2026-10-19",
            ("improved", "Fewer PIN prompts", "Windows Hello asks at most once every 30 days."),
            ("improved", "Pick your courses", "Setup starts with no courses ticked.")),
        R("0.3.39", "2026-10-19",
            ("fixed", "Math keyboard", "Macmillan's math keyboard sits under the answer."),
            ("fixed", "Final score", "A finished Macmillan assignment shows its score.")),
        R("0.3.38", "2026-10-19",
            ("fixed", "Macmillan pictures", "Pictures stay in sight on a narrower window.")),
        R("0.3.37", "2026-10-19",
            ("fixed", "Macmillan pictures", "Units by their fields, pictures enlarge on click.")),
        R("0.3.36", "2026-10-19",
            ("fixed", "Macmillan diagrams", "Diagrams at right, widgets readable, nothing cut off.")),
        R("0.3.35", "2026-10-18",
            ("fixed", "Macmillan polished", "One glass panel, Alta-style Correct, readable fields.")),
        R("0.3.34", "2026-10-18",
            ("fixed", "Macmillan on glass", "Full width, the diagram back, and dark mode.")),
        R("0.3.33", "2026-10-18",
            ("fixed", "Macmillan fixed", "Its list, score and settings fill in; the page fits.")),
        R("0.3.32", "2026-10-18",
            ("new", "Macmillan redesigned", "Questions, score and Submit in Simpl's own layout.")),
        R("0.3.31", "2026-10-18",
            ("new", "Appearance mode", "⋯ > Appearance: style Simpl right on the window."),
            ("new", "Photos in place", "Add a photo to the pane or any card from its button."),
            ("new", "Course colours", "Pick each course's colour from the Appearance bar.")),
        R("0.3.30", "2026-10-18",
            ("new", "GeoGebra calculator", "The calculator is GeoGebra's: graphs, CAS, 3D and more."),
            ("improved", "Scientific in the pin", "The pinned calculator is GeoGebra's scientific one.")),
        R("0.3.29", "2026-10-18",
            ("new", "Step-by-step solver", "Equations, factoring, calculus and more, with every step."),
            ("new", "Graphs and keypad", "Graph any answer; build problems on the maths keypad.")),
        R("0.3.28", "2026-10-18",
            ("new", "Element at a glance", "Press an element for a small window with its key facts."),
            ("improved", "Easier-to-read colours", "The table's coloured words and squares read clearly."),
            ("improved", "Precise atomic masses", "Every atomic mass as precisely as it is known.")),
        R("0.3.27", "2026-10-18",
            ("dev", "Developer tools", "Logs, system use and stats, for those who know the way in."),
            ("improved", "Critical fixes sooner", "Simpl looks for a critical update every minute.")),
        R("0.3.26", "2026-10-17",
            ("improved", "Front page first", "A course leads with its front page, its news just above."),
            ("improved", "Every link a button", "A course's tools and links are buttons on its home."),
            ("improved", "Course up top", "A course chosen moves to the top of the side pane.")),
        R("0.3.25", "2026-10-17",
            ("improved", "Alta even faster", "Knewton Alta opens faster again, and uses less battery.")),
        R("0.3.24", "2026-10-16",
            ("fixed", "Tools open at once", "Open tool loads the tool, not Canvas's Load button.")),
        R("0.3.23", "2026-10-16",
            ("fixed", "Files open again", "Files linked in instructions open properly again."),
            ("fixed", "Alta maths drawn whole", "Brackets and roots show right away, no Light or Dark needed.")),
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
            ("new", "Chemistry tools", "Periodic table, Lewis structures, molecules and atoms."),
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
            ("new", "External tools in the sidebar", "Every tool your courses use, one click away under Tools."),
            ("new", "Every place a tool is used", "Click Knewton Alta, or any tool in your assignments, to choose which one to open."),
            ("improved", "External tools, tidied", "Cards sit side by side without gaps, and long course names fit.")),
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
            ("fixed", "One Done, Try again", "A tool has one Done; Try again shows it's reading at once."),
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
            ("new", "The tools", "Twelve of them, from the periodic table to flashcards; pin one to the title bar."),
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

    /// <summary>
    /// The changes What's New leads with, at most <paramref name="most"/>: the newest version's first, and in each version
    /// what is new before what is better before what is fixed — the ones a student notices — never a note for the
    /// developer tools. The rest are a click away (Shell/WhatsNewSheet.cs).
    /// </summary>
    public static List<ReleaseNote> Leading(IEnumerable<Release> releases, int most = 3)
    {
        static int Rank(string kind) => kind switch { "new" => 0, "improved" => 1, "fixed" => 2, _ => 3 };
        return releases.SelectMany(r => r.Notes.Where(n => Rank(n.Kind) < 3).OrderBy(n => Rank(n.Kind))).Take(most).ToList();
    }

    /// <summary>After an update, the versions since the one last seen (null: nothing to show; never unasked in a picture).</summary>
    public static WhatsNewData? Due()
    {
        if (Launch.Get("shot") != null) return null;
        var newer = Since(Prefs.String(SeenKey), Number);
        return newer.Count == 0 ? null : new WhatsNewData { Version = Current, Releases = newer };
    }

    /// <summary>The versions an update from <paramref name="seen"/> to <paramref name="number"/> brings, newest first
    /// (none seen before: that version's own alone).</summary>
    public static List<Release> Since(string? seen, string number) =>
        Releases.Where(r => seen != null ? IsNewer(r.Version, seen) : r.Version == number)
                .Where(r => !IsNewer(r.Version, number)).ToList();

    /// <summary>(0.3.83) The versions before this one, newest first: what the developer tools can play an update from.</summary>
    public static List<string> Older() => Releases.Where(r => IsNewer(Number, r.Version)).Select(r => r.Version).ToList();

    public static void MarkSeen() => Prefs.Set(SeenKey, Number);

    /// <summary>(0.3.83) As if <paramref name="from"/> was the version here before this one (an update played through).</summary>
    public static void Pretend(string from) => Prefs.Set(SeenKey, from);

    /// <summary>(0.3.80) A What's New seen before, by some version of this app: it was here before this one.</summary>
    public static bool SeenAny => Prefs.String(SeenKey) != null;

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

/// <summary>
/// (0.3.80, the Mac's 1.3.182 MergedIntro) After an update, once: To Do and the calendar are one place now (To Do &amp;
/// Calendar), and Quick Plan — a task dragged from To Do onto the calendar is given time there (let go on a day, at its
/// first free time). Shown in place of that update's What's New (the account menu's What's new has its notes), never
/// after a first install (nothing was apart for it).
/// </summary>
public static class MergedIntro
{
    private const string SeenKey = "mergedIntro:seen";

    /// <summary>The version it came with: an update from one before it brings it up.</summary>
    public const string Since = "0.3.80";

    /// <summary>After an update (a What's New seen before: an older version was here), not seen yet.</summary>
    public static bool Due => WhatsNew.SeenAny && !Prefs.Bool(SeenKey);

    /// <summary>Seen: its popup was up, or a What's New was (a first install's).</summary>
    public static void MarkSeen() => Prefs.Set(SeenKey, true);

    /// <summary>(0.3.83) An update from <paramref name="from"/> brings it up: that version came before it.</summary>
    public static bool ComesAfter(string from) => WhatsNew.IsNewer(Since, from);

    /// <summary>(0.3.83) As after an update from <paramref name="from"/>: not seen yet when that came before it, else seen.</summary>
    public static void Pretend(string from) => Prefs.Set(SeenKey, ComesAfter(from) ? null : (object)true);
}

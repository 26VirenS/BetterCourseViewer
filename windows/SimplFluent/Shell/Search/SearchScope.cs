using Wpf.Ui.Controls;

namespace Simpl.Shell;

/// <summary>
/// (0.3.56) One of the four kinds the search can be narrowed to, as the web box's (search.js SCOPES) and the Mac's
/// (FloatingSearch.swift): before anything is typed they are what the search lists, Ctrl+1 to Ctrl+4 while it is open.
/// </summary>
public sealed record SearchScope(string Key, string Title, string Hint, SymbolRegular Symbol, string Shortcut, string[]? Kinds)
{
    public static readonly SearchScope Courses = new("courses", "Courses", "Your courses and their sections", SymbolRegular.Library24, "Ctrl+1", []);
    public static readonly SearchScope Work = new("work", "Work", "Assignments, quizzes, discussions and announcements", SymbolRegular.DocumentText24, "Ctrl+2",
        ["Assignments", "Discussions", "Announcements"]);
    public static readonly SearchScope Files = new("files", "Files", "Course files and pages", SymbolRegular.Folder24, "Ctrl+3", ["Files", "Pages"]);
    public static readonly SearchScope Actions = new("actions", "Actions", "Every command: open, add, switch, go", SymbolRegular.AppsList24, "Ctrl+4", []);

    public static readonly IReadOnlyList<SearchScope> All = [Courses, Work, Files, Actions];

    public static SearchScope? Of(string? key) => All.FirstOrDefault(s => s.Key == key);

    // (Kinds: the search's own groups this kind answers with — none for Courses and Actions, whose rows are the app's own)
}

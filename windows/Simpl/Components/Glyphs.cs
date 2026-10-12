namespace Simpl.Components;

/// <summary>
/// The Mac's SF Symbols by name, as Segoe Fluent Icons glyphs (Windows 11's own icon face; Segoe MDL2 Assets on Windows
/// 10 has most of them at the same code points). A symbol with no counterpart falls back to a dot.
/// </summary>
public static class Glyphs
{
    private static readonly Dictionary<string, string> Map = new()
    {
        // the sidebar
        ["square.grid.2x2"] = "", ["checklist"] = "", ["calendar"] = "", ["chart.bar.xaxis"] = "",
        ["bell"] = "", ["tray"] = "", ["wrench.and.screwdriver"] = "", ["puzzlepiece.extension"] = "",
        ["books.vertical"] = "", ["person.2"] = "", ["house"] = "",
        // the toolbar and common actions
        ["magnifyingglass"] = "", ["chevron.left"] = "", ["chevron.right"] = "", ["chevron.down"] = "",
        ["chevron.up"] = "", ["arrow.clockwise"] = "", ["plus"] = "", ["ellipsis"] = "",
        ["ellipsis.circle"] = "", ["gearshape"] = "", ["xmark"] = "", ["sidebar.left"] = "",
        ["square.and.arrow.up"] = "", ["arrow.up.forward"] = "", ["arrow.up.right.square"] = "",
        ["link"] = "", ["doc.on.doc"] = "", ["line.3.horizontal.decrease"] = "", ["pin"] = "",
        ["pin.fill"] = "", ["star"] = "", ["star.fill"] = "", ["trash"] = "", ["pencil"] = "",
        ["square.and.pencil"] = "", ["paperplane"] = "", ["arrowshape.turn.up.left"] = "",
        ["paperclip"] = "", ["info.circle"] = "", ["questionmark.circle"] = "", ["person.crop.circle"] = "",
        ["person"] = "", ["sun.max"] = "", ["moon"] = "", ["eye"] = "", ["eye.slash"] = "",
        ["slider.horizontal.3"] = "", ["arrow.down.circle"] = "", ["terminal"] = "", ["macwindow"] = "",
        ["list.bullet.rectangle"] = "", ["brain.head.profile"] = "", ["curlybraces"] = "",
        // state
        ["checkmark"] = "", ["checkmark.circle"] = "", ["checkmark.circle.fill"] = "", ["circle"] = "",
        ["clock"] = "", ["exclamationmark.triangle"] = "", ["exclamationmark.circle"] = "",
        ["sunrise"] = "", ["megaphone"] = "", ["flag"] = "", ["lock"] = "", ["lock.open"] = "",
        ["bolt"] = "", ["sparkles"] = "", ["timer"] = "", ["hourglass"] = "",
        // a course's sections and the work in them
        ["doc.text"] = "", ["doc"] = "", ["doc.richtext"] = "", ["list.bullet"] = "",
        ["list.bullet.clipboard"] = "", ["bubble.left.and.bubble.right"] = "", ["bubble.left"] = "",
        ["folder"] = "", ["person.3"] = "", ["graduationcap"] = "", ["square.stack"] = "",
        ["rectangle.stack"] = "", ["questionmark.square"] = "", ["checkmark.square"] = "",
        ["text.book.closed"] = "", ["calendar.badge.clock"] = "", ["envelope"] = "",
        ["video"] = "", ["photo"] = "", ["music.note"] = "", ["globe"] = "",
        // the Dashboard and the sidebar's more
        ["chart.bar"] = "\uE9D2", ["checkmark.seal"] = "\uE930", ["tray.and.arrow.up"] = "\uE898", ["textformat"] = "\uE8D2",
        ["building.2"] = "\uE80F", ["person.3.sequence"] = "\uE716", ["square.stack.3d.up"] = "\uE8FD", ["arrow.left"] = "\uE72B",
        ["arrow.right"] = "\uE72A", ["chevron.left.small"] = "\uE76B", ["chevron.right.small"] = "\uE76C",
    };

    public static string Of(string? symbol) =>
        symbol == null ? "" :
        symbol.Length == 1 && symbol[0] >= '\uE000' ? symbol : // (a glyph already)
        Map.TryGetValue(symbol, out var g) ? g : (Map.TryGetValue(symbol.Replace(".fill", ""), out var g2) ? g2 : "");

    /// <summary>A section of a course, by its kind (the Mac's Glyph.section).</summary>
    public static string Section(string kind) => Of(kind switch
    {
        "home" => "house",
        "announcements" => "megaphone",
        "assignments" => "doc.text",
        "discussions" => "bubble.left.and.bubble.right",
        "grades" => "chart.bar.xaxis",
        "modules" => "square.stack",
        "pages" => "doc.richtext",
        "files" => "folder",
        "quizzes" => "checklist",
        "people" => "person.2",
        "syllabus" => "text.book.closed",
        "groups" => "person.2",
        "calendar" => "calendar",
        _ => "doc",
    });

    /// <summary>An item of the school's recent activity by its type (the Mac's DashGlyph.activity).</summary>
    public static string Activity(string? type) => Of(type switch
    {
        "Announcement" => "megaphone",
        "DiscussionTopic" => "bubble.left.and.bubble.right",
        "Conversation" => "envelope",
        "Message" => "bell",
        "Submission" => "checkmark.seal",
        "Conference" or "WebConference" => "video",
        "Collaboration" or "AssessmentRequest" => "person.2",
        _ => "doc",
    });

    /// <summary>A piece of work by its kind ("assignment", "quiz", "discussion", …).</summary>
    public static string Work(string? kind) => Of((kind ?? "").ToLowerInvariant() switch
    {
        "quiz" or "quizzes" => "checklist",
        "discussion" or "discussion_topic" => "bubble.left.and.bubble.right",
        "announcement" => "megaphone",
        "page" or "wiki_page" => "doc.richtext",
        "event" or "calendar_event" => "calendar",
        "todo" or "custom" or "planner_note" => "checkmark.square",
        "file" => "doc",
        _ => "doc.text",
    });
}

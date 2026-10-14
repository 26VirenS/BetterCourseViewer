using Wpf.Ui.Controls;

namespace Simpl.Kit;

/// <summary>The Mac's symbols for the school's things (its Glyph and DashGlyph), as Fluent System Icons.</summary>
public static class Glyph
{
    /// <summary>A section of a course, by its kind.</summary>
    public static SymbolRegular Section(string? kind) => kind switch
    {
        "home" => SymbolRegular.Home24,
        "announcements" => SymbolRegular.Megaphone24,
        "assignments" => SymbolRegular.DocumentText24,
        "discussions" => SymbolRegular.ChatMultiple24,
        "grades" => SymbolRegular.DataBarVertical24,
        "modules" => SymbolRegular.Stack24,
        "pages" => SymbolRegular.DocumentOnePage24,
        "files" => SymbolRegular.Folder24,
        "quizzes" => SymbolRegular.DocumentQuestionMark24, // (its own symbol, a question paper: the checklist is To Do’s)
        "people" or "groups" => SymbolRegular.People24,
        "syllabus" => SymbolRegular.BookOpen24,
        "calendar" => SymbolRegular.CalendarLtr24,
        _ => SymbolRegular.Document24,
    };

    /// <summary>An item of the school's recent activity by its type.</summary>
    public static SymbolRegular Activity(string? type) => type switch
    {
        "Announcement" => SymbolRegular.Megaphone24,
        "DiscussionTopic" => SymbolRegular.ChatMultiple24,
        "Conversation" => SymbolRegular.Mail24,
        "Message" => SymbolRegular.Alert24,
        "Submission" => SymbolRegular.CheckmarkStarburst24,
        "Conference" or "WebConference" => SymbolRegular.Video24,
        "Collaboration" or "AssessmentRequest" => SymbolRegular.People24,
        _ => SymbolRegular.Document24,
    };

    /// <summary>A piece of work by its kind ("assignment", "quiz", "discussion", …).</summary>
    public static SymbolRegular Work(string? kind) => (kind ?? "").ToLowerInvariant() switch
    {
        "quiz" or "quizzes" => SymbolRegular.DocumentQuestionMark24,
        "discussion" or "discussion_topic" => SymbolRegular.ChatMultiple24,
        "announcement" => SymbolRegular.Megaphone24,
        "page" or "wiki_page" => SymbolRegular.DocumentOnePage24,
        "event" or "calendar_event" => SymbolRegular.CalendarLtr24,
        "todo" or "custom" or "planner_note" => SymbolRegular.CheckboxChecked24,
        "file" => SymbolRegular.Document24,
        _ => SymbolRegular.DocumentText24,
    };

    /// <summary>A Dashboard counter's symbol by its key.</summary>
    public static SymbolRegular Counter(string key) => key switch
    {
        "today" => SymbolRegular.Clock24,
        "tomorrow" => SymbolRegular.WeatherSunny24,
        "next" => SymbolRegular.CalendarLtr24,
        "overdue" => SymbolRegular.ErrorCircle24,
        "unread" => SymbolRegular.Megaphone24,
        "graded" => SymbolRegular.DataBarVertical24,
        _ => SymbolRegular.Circle24,
    };
}

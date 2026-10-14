using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;

namespace Simpl.Screens;

/// <summary>
/// A level of priority (the Mac's TodoPriority): its word, its short word on a row, its colour. A priority is the
/// student's alone; it never reaches the school.
/// </summary>
internal sealed record TodoPriority(int Id, string Label, string Short, Color Color)
{
    public static readonly TodoPriority[] All =
    {
        new(3, "High", "High", K.Tone("red")),
        new(2, "Medium", "Med", K.Tone("orange")),
        new(1, "Low", "Low", Color.FromRgb(0x30, 0xb0, 0xc7)),
        new(0, "None", "", K.Tone(null)),
    };

    public static TodoPriority Of(int level) => All.FirstOrDefault(p => p.Id == level) ?? All[^1];
}

/// <summary>
/// How To Do words what the page hands it (the Mac's TodoView helpers and PreviewFormat): days and times, a planner
/// item's kind, a status's colour, the first words of an assignment for its quick look.
/// </summary>
internal static class TodoWords
{
    /// <summary>The words, or null when there are none.</summary>
    public static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>A date as the page sends it (ISO 8601, with or without its milliseconds), in this PC's time.</summary>
    public static DateTime? Date(string? iso)
    {
        if (Text(iso) is not { } s) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var o) ? o.LocalDateTime : null;
    }

    /// <summary>A moment as the page reads one (ISO 8601, in UTC), as the Mac's ISO8601DateFormatter writes it.</summary>
    public static string Iso(DateTime local) => local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static int DaysFromToday(DateTime d) => (int)Math.Round((d.Date - DateTime.Today).TotalDays);

    /// <summary>A row's day with its time, where its card is not one day: "Today 11:59 PM", "Fri 11:59 PM", "Oct 20 11:59 PM".</summary>
    public static string? DayAndTime(WorkRow row)
    {
        if (Date(row.Date) is not { } date) return Text(row.Time);
        var apart = DaysFromToday(date);
        var day = apart switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            _ => Math.Abs(apart) < 7 ? date.ToString("ddd", CultureInfo.InvariantCulture) : date.ToString(date.Year == DateTime.Today.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture),
        };
        return Text(row.Time) is { } time ? $"{day} {time}" : day;
    }

    /// <summary>A day in words, as the lists name them: Today, Tomorrow, a weekday this week, else its date.</summary>
    public static string DayWord(DateTime date)
    {
        var apart = DaysFromToday(date);
        if (apart == 0) return "Today";
        if (apart == 1) return "Tomorrow";
        if (apart == -1) return "Yesterday";
        if (apart > 0 && apart < 7) return date.ToString("dddd", CultureInfo.InvariantCulture);
        if (date.Year == DateTime.Today.Year) return date.ToString("MMM d", CultureInfo.InvariantCulture);
        return date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>"Due today at 11:59 PM", "Saturday at 3:00 PM", "Due Oct 16 at 11:59 PM", "Tomorrow, all day".</summary>
    public static string? When(string? day, string? time, bool due)
    {
        var d = (day ?? "").Trim();
        var t = (time ?? "").Trim();
        if (d.Length == 0 && t.Length == 0) return null;
        string phrase;
        if (d.Length == 0) phrase = t;
        else if (t.Length == 0) phrase = d;
        else if (t.Equals("all day", StringComparison.OrdinalIgnoreCase)) phrase = $"{d}, all day";
        else phrase = $"{d} at {t}";
        if (!due) return phrase;
        // ("Due today", "Due tomorrow": the relative days in the middle of the line are lower case)
        if (d is "Today" or "Tomorrow" or "Yesterday") return "Due " + d.ToLowerInvariant() + phrase[d.Length..];
        return "Due " + phrase;
    }

    /// <summary>A planner item's kind in words, by its type (store.js classify).</summary>
    public static string? PlannerKind(string? type) => (type ?? "") switch
    {
        "assignment" or "" => "Assignment",
        "quiz" => "Quiz",
        "discussion_topic" => "Discussion",
        "wiki_page" => "Page",
        "calendar_event" => "Event",
        "planner_note" => "My task",
        "announcement" => "Announcement",
        "assessment_request" => "Peer review",
        _ => null,
    };

    /// <summary>A planner type that is work to hand in (it is due, and has a Hand in or a Take quiz).</summary>
    public static bool IsWork(string? type) => (type ?? "") is "assignment" or "quiz" or "discussion_topic" or "";

    /// <summary>A piece of work's own action, by its kind (the Mac's WorkAction.label): Take quiz, Reply, Open, Hand in.</summary>
    public static (string Label, Wpf.Ui.Controls.SymbolRegular Icon) ActionOf(string? kind) => (kind ?? "").ToLowerInvariant() switch
    {
        "quiz" => ("Take quiz", Glyph.Work("quiz")),
        "discussion" or "discussion_topic" => ("Reply", Wpf.Ui.Controls.SymbolRegular.Chat24),
        "tool" => ("Open", Wpf.Ui.Controls.SymbolRegular.PuzzlePiece24),
        _ => ("Hand in", Wpf.Ui.Controls.SymbolRegular.ArrowUpload24),
    };

    /// <summary>"10 pts" out of a row's line ("Assignment · 10 pts · due 11:59 PM").</summary>
    public static string? Points(string? text) =>
        text?.Split(" · ").Select(p => p.Trim()).FirstOrDefault(p => p.EndsWith(" pts") || p.EndsWith(" pt"));

    /// <summary>A score as Canvas writes one: 9, 9.5, 9.25.</summary>
    public static string Number(double v)
    {
        if (!double.IsFinite(v)) return "–";
        if (Math.Round(v) == v && Math.Abs(v) < 1e9) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>A status's colour by its kind (the page's words: bad, warn, good, info, purple, muted).</summary>
    public static Color Tone(string? kind) => kind switch
    {
        "bad" => K.Tone("red"),
        "warn" => K.Tone("orange"),
        "good" => K.Tone("green"),
        "info" => K.Tone("blue"),
        "purple" => K.Tone("purple"),
        _ => K.Tone(kind),
    };

    /// <summary>Canvas's rich text as a few plain lines, for a quick look's first words (the Mac's PreviewText.plain).</summary>
    public static string? Plain(string? html, int limit = 700)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var s = html;
        s = Regex.Replace(s, @"<(script|style)[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<br\s*/?>|</(p|div|ul|ol|h[1-6]|tr|blockquote|pre)>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, "[ \t ]+", " ");
        s = Regex.Replace(s, @" *\n *", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        s = s.Trim();
        if (s.Length == 0) return null;
        if (s.Length > limit) s = s[..limit].Trim() + "…";
        return s;
    }

    /// <summary>Figures of one width, so counts and times line up.</summary>
    public static void Tabular(System.Windows.DependencyObject t) =>
        System.Windows.Documents.Typography.SetNumeralAlignment(t, System.Windows.FontNumeralAlignment.Tabular);

    /// <summary>How soon a second press is a double-click's, as Windows is set.</summary>
    public static long DoubleClickMs
    {
        get
        {
            try { return GetDoubleClickTime(); } catch { return 500; }
        }
    }

    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
}

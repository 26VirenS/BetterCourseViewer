using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Simpl.Screens;

/// <summary>
/// What the Calendar and Notifications share: a menu's item with its symbol, a link copied, the colour of a status word
/// (the Mac's StatusChip), the symbol of a calendar item's kind, and the words the preview card puts together from what a
/// row says (the Mac's PreviewFormat and PreviewText).
/// </summary>
internal static class CalendarKit
{
    /// <summary>How the Calendar writes its days and months: in English, one way whatever Windows' language ("Oct 13",
    /// "October 2026"), as every date in Simpl is. The week still starts on the day Windows is set to
    /// (<see cref="FirstDayOfWeek"/>).</summary>
    public static CultureInfo Culture => CultureInfo.InvariantCulture;

    /// <summary>The week's first day, as Windows is set (a Sunday, a Monday …).</summary>
    public static DayOfWeek FirstDayOfWeek => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    /// <summary>A day as every date is written: "Oct 13", or "Tue, Oct 13" with its weekday; the year only when it is not
    /// this one ("Jan 5, 2027").</summary>
    public static string DateWords(DateTime day, bool weekday = false)
    {
        var date = day.ToString(day.Year == DateTime.Today.Year ? "MMM d" : "MMM d, yyyy", Culture);
        return weekday ? $"{day.ToString("ddd", Culture)}, {date}" : date;
    }

    /// <summary>Struck through: work handed in or excused, in every view (the engine's <c>struck</c>; from a page a
    /// version older, work handed in) — never a lecture or a lab that is only over.</summary>
    public static bool IsStruck(Models.CalEvent ev) =>
        ev.Struck ?? (ev.Missing != true && (ev.Excused == true || (ev.Done == true && IsWork(ev.Kind))));

    /// <summary>Greyed: over (its due time or its end gone by), or struck.</summary>
    public static bool IsPast(Models.CalEvent ev) => (ev.Past ?? ev.Done == true) || IsStruck(ev);

    /// <summary>A menu's item with its symbol (the Mac's context menus and its More menu).</summary>
    public static MenuItem Item(string text, SymbolRegular icon, Action act)
    {
        var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = icon } };
        mi.Click += (_, _) => act();
        return mi;
    }

    /// <summary>An address on the clipboard (Copy Link).</summary>
    public static void Copy(Uri? u)
    {
        if (u == null) return;
        try { Clipboard.SetText(u.ToString()); } catch { /* the clipboard busy elsewhere: nothing copied */ }
    }

    /// <summary>A status word's colour as the page names it (the Mac's StatusChip): bad, warn, good, info, purple; else grey.</summary>
    public static Color Tone(string? kind) => kind switch
    {
        "bad" or "red" or "danger" => K.Tone("red"),
        "warn" or "orange" => K.Tone("orange"),
        "good" or "green" => K.Tone("green"),
        "info" or "blue" => K.Tone("blue"),
        "purple" => K.Tone("purple"),
        _ => K.Tone("gray"),
    };

    /// <summary>Work handed in (it is due, and has its own Hand In or Take Quiz), not an event.</summary>
    public static bool IsWork(string? kind) => kind is "Assignment" or "Quiz" or "Discussion";

    /// <summary>A calendar item's symbol by its kind (the Mac's CalendarEventLine.glyph).</summary>
    public static SymbolRegular Glyph(string? kind) => kind switch
    {
        "Assignment" => SymbolRegular.DocumentText24,
        "Quiz" => Simpl.Kit.Glyph.Work("quiz"),
        "Discussion" => SymbolRegular.ChatMultiple24,
        "Appointment" => SymbolRegular.People24,
        _ => SymbolRegular.CalendarLtr24,
    };

    /// <summary>The words, or null when there are none.</summary>
    public static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>"10 pts" out of a row's line ("Assignment · 10 pts · due 11:59 PM").</summary>
    public static string? Points(string? text) =>
        text?.Split(" · ").Select(p => p.Trim()).FirstOrDefault(p => p.EndsWith(" pts") || p.EndsWith(" pt"));

    /// <summary>"Due today at 11:59 PM", "Saturday at 3:00 PM", "Due Oct 16 at 11:59 PM", "Tomorrow, all day".</summary>
    public static string? When(string? day, string? time, bool due)
    {
        var d = (day ?? "").Trim();
        var t = (time ?? "").Trim();
        if (d.Length == 0 && t.Length == 0) return null;
        var phrase = d.Length == 0 ? t : t.Length == 0 ? d : t.Equals("all day", StringComparison.OrdinalIgnoreCase) ? $"{d}, all day" : $"{d} at {t}";
        if (!due) return phrase;
        // ("Due today", "Due tomorrow": the relative days in the middle of the line are lower case)
        if (d is "Today" or "Tomorrow" or "Yesterday") return "Due " + d.ToLowerInvariant() + phrase[d.Length..];
        return "Due " + phrase;
    }

    /// <summary>A day in words, as the Dashboard's List names them: Today, Tomorrow, a weekday this week, else its date.</summary>
    public static string DayWord(DateTime date)
    {
        var today = DateTime.Today;
        var d = date.Date;
        if (d == today) return "Today";
        if (d == today.AddDays(1)) return "Tomorrow";
        if (d == today.AddDays(-1)) return "Yesterday";
        var apart = (d - today).TotalDays;
        if (apart > 0 && apart < 7) return d.ToString("dddd", Culture);
        return d.Year == today.Year ? d.ToString("MMM d", Culture) : d.ToString("MMM d, yyyy", Culture);
    }

    /// <summary>A date as the page sends it (ISO 8601), on this PC's clock.</summary>
    public static DateTime? Date(string? iso) =>
        Text(iso) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.LocalDateTime : null;

    /// <summary>A score as Canvas writes one: 9, 9.5, 9.25.</summary>
    public static string Number(double v)
    {
        if (!double.IsFinite(v)) return "–";
        if (Math.Round(v) == v && Math.Abs(v) < 1e9) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Canvas's rich text as a few plain lines, for the preview's excerpt (the screen itself draws it whole).</summary>
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
        s = Regex.Replace(s, " *\n *", "\n");
        s = Regex.Replace(s, "\n{3,}", "\n\n");
        s = s.Trim();
        if (s.Length == 0) return null;
        if (s.Length > limit) s = s[..limit].Trim() + "…";
        return s;
    }
}

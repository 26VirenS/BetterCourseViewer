using System.Globalization;
using System.Text.RegularExpressions;

namespace Simpl.Support;

/// <summary>
/// (The UI review's W22) One clock for every time the app writes: the reader's own, from Windows' region settings —
/// "11:59 PM" where the day has an AM and a PM, "23:59" where it runs to 24 — never "11:59 PM" on the Dashboard,
/// "11:59pm" in the Plan popup, "11:15am" in Find Appointment and "11:59 pm" in Macmillan's cover. The engine writes
/// its times the same way (ui.js fmtTime, in WebView2's locale, which is Windows'); text from Canvas or a publisher,
/// written its own way, goes through <see cref="Tidy"/>.
/// </summary>
internal static class Clock
{
    /// <summary>Windows' short time ("h:mm tt", "HH:mm"): hours and minutes, never seconds.</summary>
    public static string Pattern
    {
        get
        {
            var p = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
            return string.IsNullOrWhiteSpace(p) ? "h:mm tt" : p;
        }
    }

    /// <summary>The clock has an AM and a PM.</summary>
    public static bool TwelveHour => Pattern.Contains('t');

    /// <summary>"11:59 PM" (the reader's clock: "23:59" on a 24-hour one). A plain space before AM and PM: ICU writes a
    /// narrow no-break one (U+202F), which reads the same but is not the same text as the engine's or Canvas's.</summary>
    public static string Time(DateTime t) => Plain(t.ToString(Pattern, CultureInfo.CurrentCulture));

    private static string Plain(string s) => s.Replace('\u202F', ' ').Replace('\u00A0', ' ');

    /// <summary>An hour alone, for a day's edge: "8 AM", "Noon", "12 AM" (on a 24-hour clock "08:00", "12:00").</summary>
    public static string Hour(int h)
    {
        if (!TwelveHour) return new DateTime(2000, 1, 1, h, 0, 0).ToString(Pattern.Contains("HH") ? "HH:00" : "H:00", CultureInfo.CurrentCulture);
        return h == 12 ? "Noon" : Plain(new DateTime(2000, 1, 1, h, 0, 0).ToString("h tt", CultureInfo.CurrentCulture));
    }

    // a time written with its AM or PM, however it is written: "11:59pm", "11:59 pm", "11:59 P.M.", "11:59PM", or the
    // compact "10:30a" (no space before the letter — "3:15 a student" is not a time)
    private static readonly Regex Written = new(@"\b(\d{1,2}):(\d{2})(?:\s?([AaPp])\.?\s?[Mm]\.?|([AaPp]))(?![A-Za-z])", RegexOptions.Compiled);

    /// <summary>Text from Canvas, a publisher or the engine with its times written its own way — each time in it
    /// written the app's way ("Due Nov 3, 11:59 pm" → "Due Nov 3, 11:59 PM"). Anything else is left as it is.</summary>
    public static string Tidy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        return Written.Replace(text, m =>
        {
            var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (h is < 1 or > 12 || min > 59) return m.Value;
            var letter = (m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value).ToLowerInvariant();
            var hour = h % 12 + (letter == "p" ? 12 : 0);
            return Time(new DateTime(2000, 1, 1, hour, min, 0));
        });
    }
}

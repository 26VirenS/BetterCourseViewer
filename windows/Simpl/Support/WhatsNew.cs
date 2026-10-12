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

    /// <summary>This copy's version as its release names it: "0.1.0 (v015)" for the preview build of run 15.</summary>
    public static string Current => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";

    /// <summary>The version alone ("0.1.0"), as the notes name theirs.</summary>
    public static string Number => Current.Split(' ')[0];

    private static Release R(string version, string date, params (string Kind, string Title, string Body)[] notes) => new()
    {
        Version = version,
        Date = date,
        Notes = notes.Select(n => new ReleaseNote { Kind = n.Kind, Title = n.Title, Body = n.Body }).ToList(),
    };

    public static readonly List<Release> Releases = new()
    {
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

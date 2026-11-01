using System.Text.RegularExpressions;

namespace Simpl.Tests;

/// <summary>
/// Windows 11's rule for the words of an app (Fluent's): sentence case — a capital for the first word of a title, a
/// label or a button, and for names, never for every word ("Word counter", not "Word Counter"). A word is a name when
/// it is a product's, a company's or a place's that keeps its capitals wherever it is (Canvas, Windows Hello,
/// Macmillan), or a part of Simpl named as it is labelled (To Do, Settings, the Done and Try again buttons a note
/// tells of). Each sentence's first word may be a capital; so may the first after a colon, a "·", a "—" or a "▸".
/// </summary>
internal static class SentenceCase
{
    /// <summary>The names that keep their capitals.</summary>
    public static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        // products, companies, schools, places
        "Simpl", "Canvas", "Windows", "Hello", "Microsoft", "Store", "Explorer", "Google", "Outlook", "Apple", "Mac", "Brightspace",
        "Knewton", "Alta", "Macmillan", "Learning", "Desmos", "GeoGebra", "Community", "Excel", "PowerPoint", "Word", "Zoom",
        "Merced", "Chicago", "Atlantic", "Press",
        // people a law or a rule is named after
        "Lewis", "Hardy", "Weinberg", "Henderson", "Hasselbalch", "Punnett", "Hund", "Waals", "Gram",
        // parts of Simpl named as they are labelled
        "To", "Do", "Calendar", "Settings", "Dashboard", "Inbox", "Tools", "Grades", "Downloads", "Done", "Try", "Load", "Ink",
        "Browser", "What-If", "Agenda",
    };

    private static readonly Regex Sentences = new(@"(?<=[.:?!·—▸])\s+", RegexOptions.Compiled);
    private static readonly Regex Words = new(@"[A-Za-z][A-Za-z'’\-]*", RegexOptions.Compiled);

    /// <summary>The words of <paramref name="text"/> capitalized as no word in sentence case is: after its sentence's
    /// first, a capital and then small letters, and not a name.</summary>
    public static List<string> Offenders(string text)
    {
        var found = new List<string>();
        foreach (var sentence in Sentences.Split(text))
        {
            var words = Words.Matches(sentence).Select(m => m.Value).ToList();
            foreach (var w in words.Skip(1))
            {
                var bare = w.TrimEnd('\'', '’').Replace("’s", "").Replace("'s", "");
                if (!char.IsUpper(bare[0]) || bare.Skip(1).Any(char.IsUpper) || Names.Contains(bare)) continue;
                found.Add(w);
            }
        }
        return found;
    }
}

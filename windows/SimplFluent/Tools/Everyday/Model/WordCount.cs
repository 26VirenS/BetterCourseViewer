using System.Globalization;
using System.Text;

namespace Simpl.Tools;

// The Word Counter's counting (0.3.10): what a text comes to — its words, characters, sentences and paragraphs, how long
// it takes to read or say, and the words it uses most — with no window in it, so the tests can run it as it is. Both
// apps count the same way (the Mac's WordCount): a word is a run of non-spaces with a letter or a figure in it, a
// sentence ends at . ! or ? before a space or the end, a paragraph is a block between blank lines.

/// <summary>What a text comes to.</summary>
internal sealed record WordStats(int Words, int Characters, int CharactersNoSpaces, int Sentences, int Paragraphs, double AverageWordLength, string LongestWord,
    IReadOnlyList<(string Word, int Count)> TopWords)
{
    public static readonly WordStats Empty = new(0, 0, 0, 0, 0, 0, "", Array.Empty<(string, int)>());

    /// <summary>Read silently, at 238 words a minute (the average adult's).</summary>
    public TimeSpan ReadingTime => WordCount.Time(Words, 238);

    /// <summary>Read aloud, at 150 words a minute (a talk's pace).</summary>
    public TimeSpan SpeakingTime => WordCount.Time(Words, 150);
}

/// <summary>The Word Counter's sums.</summary>
internal static class WordCount
{
    /// <summary>Words that say nothing on their own, left out of the most-used list (the usual English stop words).</summary>
    public static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any", "are", "as", "at", "be", "because",
        "been", "before", "being", "below", "between", "both", "but", "by", "can", "could", "did", "do", "does", "doing", "down",
        "during", "each", "few", "for", "from", "further", "had", "has", "have", "having", "he", "her", "here", "hers", "herself",
        "him", "himself", "his", "how", "i", "if", "in", "into", "is", "it", "its", "itself", "just", "me", "more", "most", "my",
        "myself", "no", "nor", "not", "now", "of", "off", "on", "once", "only", "or", "other", "our", "ours", "ourselves", "out",
        "over", "own", "same", "she", "should", "so", "some", "such", "than", "that", "the", "their", "theirs", "them",
        "themselves", "then", "there", "these", "they", "this", "those", "through", "to", "too", "under", "until", "up", "very",
        "was", "we", "were", "what", "when", "where", "which", "while", "who", "whom", "why", "will", "with", "would", "you",
        "your", "yours", "yourself", "yourselves", "also", "may", "might", "must", "shall", "us", "one", "two", "like", "much",
        "many", "every", "even", "well", "still", "yet", "s", "t", "d", "ll", "re", "ve", "m",
    };

    /// <summary>The text counted.</summary>
    public static WordStats Count(string? text)
    {
        if (string.IsNullOrEmpty(text)) return WordStats.Empty;
        // (characters as a person sees them — an emoji one, a line break one however Windows writes it)
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        int characters = 0, noSpaces = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            characters++;
            if (!string.IsNullOrWhiteSpace(elements.GetTextElement())) noSpaces++;
        }

        var words = new List<string>();
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (token.Any(char.IsLetterOrDigit)) words.Add(token);

        var lengths = 0;
        var longest = "";
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var w in words)
        {
            var core = Trim(w);
            if (core.Length == 0) continue;
            lengths += new StringInfo(core).LengthInTextElements;
            if (core.Length > longest.Length) longest = core;
            var key = core.ToLowerInvariant();
            if (!key.Any(char.IsLetter) || StopWords.Contains(key)) continue;
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        var top = counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(10).Select(p => (p.Key, p.Value)).ToList();

        return new WordStats(words.Count, characters, noSpaces, Sentences(text), Paragraphs(text), words.Count == 0 ? 0 : (double)lengths / words.Count, longest, top);
    }

    /// <summary>A word without the punctuation round it: "(cell)," is "cell"; an apostrophe or a hyphen inside stays.</summary>
    public static string Trim(string word)
    {
        int a = 0, b = word.Length;
        while (a < b && !char.IsLetterOrDigit(word[a])) a++;
        while (b > a && !char.IsLetterOrDigit(word[b - 1])) b--;
        return word[a..b];
    }

    /// <summary>
    /// Sentences: each ends at a full stop, a question mark or an exclamation mark (one or more, a closing quote or
    /// bracket after them allowed) that is followed by a space or the end — so 3.14 and e.g. do not end one — and a text
    /// with words but no such mark is one sentence.
    /// </summary>
    public static int Sentences(string text)
    {
        var n = 0;
        var inSentence = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsLetterOrDigit(ch)) { inSentence = true; continue; }
            if (ch is '.' or '!' or '?')
            {
                var j = i;
                while (j < text.Length && text[j] is '.' or '!' or '?') j++;
                while (j < text.Length && text[j] is '"' or '\'' or '”' or '’' or ')' or ']') j++;
                if (j >= text.Length || char.IsWhiteSpace(text[j]))
                {
                    if (inSentence) n++;
                    inSentence = false;
                    i = j - 1;
                }
            }
        }
        return inSentence ? n + 1 : n;
    }

    /// <summary>Paragraphs: the blocks of text between blank lines, counting those with a word in them.</summary>
    public static int Paragraphs(string text)
    {
        var n = 0;
        var inBlock = false;
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (line.Any(char.IsLetterOrDigit)) { if (!inBlock) n++; inBlock = true; }
            else if (line.Trim().Length == 0) inBlock = false;
        }
        return n;
    }

    /// <summary>So many words at so many a minute, to the second.</summary>
    public static TimeSpan Time(int words, int perMinute) => TimeSpan.FromSeconds(Math.Round(words * 60.0 / perMinute));

    /// <summary>"12 s", "1 min 5 s", "1 h 2 min".</summary>
    public static string Clock(TimeSpan t)
    {
        var s = (int)Math.Round(t.TotalSeconds);
        if (s < 60) return $"{s} s";
        int h = s / 3600, m = s % 3600 / 60, sec = s % 60;
        if (h > 0) return m == 0 ? $"{h} h" : $"{h} h {m} min";
        return sec == 0 ? $"{m} min" : $"{m} min {sec} s";
    }

    /// <summary>A count in figures with its thousands grouped: "1,234".</summary>
    public static string N(int n) => n.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>"4.3" — the average word's length to a tenth.</summary>
    public static string Tenth(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>What is left to a target, or over it: "120 to go", "15 over", "On target".</summary>
    public static string ToTarget(int words, int target)
    {
        if (target <= 0) return "";
        var d = target - words;
        return d > 0 ? $"{N(d)} to go" : d < 0 ? $"{N(-d)} over" : "On target";
    }

    /// <summary>The counts as plain text for the clipboard.</summary>
    public static string Report(WordStats s)
    {
        var b = new StringBuilder();
        b.Append("Words: ").Append(N(s.Words)).Append('\n');
        b.Append("Characters: ").Append(N(s.Characters)).Append(" (").Append(N(s.CharactersNoSpaces)).Append(" without spaces)\n");
        b.Append("Sentences: ").Append(N(s.Sentences)).Append('\n');
        b.Append("Paragraphs: ").Append(N(s.Paragraphs)).Append('\n');
        b.Append("Reading time: ").Append(Clock(s.ReadingTime)).Append('\n');
        b.Append("Speaking time: ").Append(Clock(s.SpeakingTime)).Append('\n');
        b.Append("Average word length: ").Append(Tenth(s.AverageWordLength)).Append('\n');
        if (s.LongestWord.Length > 0) b.Append("Longest word: ").Append(s.LongestWord).Append('\n');
        return b.ToString().TrimEnd('\n');
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// The citation generator, as the web's (the Mac's Citations.swift): MLA 9, APA 7 or Chicago 17 from the fields you fill
// in, for a website, a journal article, a book, a video or a course file. Style and type pick the template; the fields
// fill it. The values live in one set keyed by field name, so author, title and year survive a change of type — and only
// the active type's fields are ever read, so a value it has no field for can never leak into the citation (a website's
// URL on a book). The citation forms as you type; what is still needed is named, and Copy and Save wait until it is
// whole. Saved citations stay on this PC; Copy list gives them alphabetically.

#pragma warning disable CA2255
internal static class CitationRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("cite", c => new CitationTool(c), c => new CitationCompact(c));
}
#pragma warning restore CA2255

/// <summary>A run of a citation's text, in italics or not.</summary>
public sealed class CiteSeg
{
    public string Text { get; set; } = "";
    public bool Italic { get; set; }

    public CiteSeg() { }
    public CiteSeg(string text, bool italic) { Text = text; Italic = italic; }
}

/// <summary>A citation saved on this PC.</summary>
public sealed class SavedCitation
{
    public string Id { get; set; } = "";
    /// <summary>The style's name ("MLA 9").</summary>
    public string Style { get; set; } = "";
    public List<CiteSeg> Parts { get; set; } = new();
    public string Plain { get; set; } = "";
}

/// <summary>A field of a kind of source: its key, label, an example, whether a citation needs it, and whether it takes a whole row.</summary>
public sealed record CiteField(string Key, string Label, string Hint, bool Required = false, bool Wide = true);

/// <summary>The citation's templates, one per style, and the work on names and dates they need (the Mac's CiteBuilder).</summary>
public static class CiteBuilder
{
    public static readonly (string Key, string Name)[] Styles = { ("mla", "MLA 9"), ("apa", "APA 7"), ("chicago", "Chicago 17") };

    public static readonly (string Key, string Name, SymbolRegular Symbol)[] Types =
    {
        ("website", "Website", SymbolRegular.Globe24), ("journal", "Journal article", SymbolRegular.News24), ("book", "Book", SymbolRegular.Book24),
        ("video", "Video", SymbolRegular.Video24), ("coursefile", "Course file", SymbolRegular.SlideMultiple24),
    };

    public static readonly string[] FieldKeys = { "author", "title", "container", "publisher", "year", "month", "day", "url", "accessed", "volume", "issue", "pages", "doi", "city", "edition", "course", "institution" };

    public static string StyleName(string key) => Styles.FirstOrDefault(s => s.Key == key).Name ?? "MLA 9";

    /// <summary>
    /// A source type's fields, each with its hint for an empty box — the form an answer takes ("YYYY", "First Last; First
    /// Last", "e.g. 47"), never a made-up answer that reads as filled in (and could be copied into the citation), as the
    /// Mac's.
    /// </summary>
    public static IReadOnlyList<CiteField> Fields(string type, string lms)
    {
        static CiteField F(string key, string label, string hint, bool required = false, bool wide = true) => new(key, label, hint, required, wide);
        return type switch
        {
            "journal" => new[]
            {
                F("author", "Author", "First Last; First Last", true), F("title", "Article title", "Title of the article", true),
                F("container", "Journal", "Name of the journal", true), F("volume", "Volume", "e.g. 47", wide: false), F("issue", "Issue", "e.g. 3", wide: false),
                F("year", "Year", "YYYY", true, wide: false), F("pages", "Pages", "e.g. 112–137", wide: false), F("doi", "DOI", "e.g. 10.1080/…"),
            },
            "book" => new[]
            {
                F("author", "Author", "First Last; First Last", true), F("title", "Book title", "Title of the book", true), F("edition", "Edition", "e.g. 2nd ed.", wide: false),
                F("city", "City", "Where it was published", wide: false), F("publisher", "Publisher", "Name of the publisher", true), F("year", "Year", "YYYY", true, wide: false),
            },
            "video" => new[]
            {
                F("author", "Uploader", "Channel or person", true), F("title", "Video title", "Title of the video", true), F("container", "Platform", "e.g. YouTube", true, wide: false),
                F("month", "Month", "Month", wide: false), F("day", "Day", "Day", wide: false), F("year", "Year", "YYYY", true, wide: false), F("url", "URL", "https://…", true),
            },
            "coursefile" => new[]
            {
                F("author", "Instructor", "First Last", true), F("title", "File title", "Title of the file", true), F("course", "Course", "e.g. MATH 021", true, wide: false),
                F("institution", "Institution", "Your school", true, wide: false), F("year", "Year", "YYYY", true, wide: false), F("url", "URL", $"Optional {lms} link"),
            },
            _ => new[]
            {
                F("author", "Author", "First Last; First Last", true), F("title", "Page title", "Title of the page", true), F("container", "Website", "Name of the site", true, wide: false),
                F("publisher", "Publisher", "If not the site’s own", wide: false), F("month", "Month", "Month", wide: false), F("day", "Day", "Day", wide: false), F("year", "Year", "YYYY", true, wide: false),
                F("url", "URL", "https://…", true), F("accessed", "Date accessed", "D Month YYYY", wide: false),
            },
        };
    }

    // ---- names ----

    public static List<string> SplitNames(string raw) =>
        Regex.Replace(raw, @"\s+and\s+", ";", RegexOptions.IgnoreCase)
            .Split(';')
            .Select(s => s.Trim(' ', '\t'))
            .Where(s => s.Length > 0)
            .ToList();

    private static (string First, string Middle, string Last) Parts(string name)
    {
        var b = name.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (b.Length <= 1) return ("", "", b.FirstOrDefault() ?? "");
        return (b[0], string.Join(" ", b[1..^1]), b[^1]);
    }

    private static string Inverted(string name)
    {
        var n = Parts(name);
        if (n.First.Length == 0) return n.Last;
        return $"{n.Last}, " + string.Join(" ", new[] { n.First, n.Middle }.Where(s => s.Length > 0));
    }

    private static string Initials(string name)
    {
        var n = Parts(name);
        if (n.First.Length == 0) return n.Last;
        var given = new[] { n.First }.Concat(n.Middle.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return $"{n.Last}, " + string.Join(" ", given.Where(g => g.Length > 0).Select(g => StringInfo.GetNextTextElement(g, 0).ToUpperInvariant() + "."));
    }

    public static string AuthorString(string raw, string style)
    {
        var list = SplitNames(raw);
        if (list.Count == 0) return "";
        var first = list[0];
        if (style == "apa")
        {
            var f = list.Select(Initials).ToList();
            if (f.Count == 1) return f[0];
            if (f.Count == 2) return $"{f[0]}, & {f[1]}";
            return string.Join(", ", f.Take(f.Count - 1)) + ", & " + f[^1];
        }
        if (style == "mla")
        {
            if (list.Count == 1) return Inverted(first);
            if (list.Count == 2) return $"{Inverted(first)}, and {list[1]}";
            return $"{Inverted(first)}, et al.";
        }
        if (list.Count == 1) return Inverted(first);
        if (list.Count == 2) return $"{Inverted(first)} and {list[1]}";
        return $"{Inverted(first)} et al.";
    }

    public static string Surname(string raw)
    {
        var list = SplitNames(raw);
        if (list.Count == 0) return "";
        var n = Parts(list[0]);
        if (list.Count == 1) return n.Last;
        if (list.Count == 2) return $"{n.Last} and {Parts(list[1]).Last}";
        return $"{n.Last} et al.";
    }

    // ---- formatting ----

    /// <summary>The citation, as runs of text in italics or not: one template per style, switching on the type.</summary>
    public static List<CiteSeg> Build(string style, string type, IReadOnlyDictionary<string, string> values, string lms)
    {
        var f = new Dictionary<string, string>();
        foreach (var d in Fields(type, lms)) f[d.Key] = (values.TryGetValue(d.Key, out var x) ? x : "").Trim();
        string V(string k) => f.TryGetValue(k, out var s) ? s : "";
        static string Join(IEnumerable<string> items, string sep) => string.Join(sep, items.Where(s => s.Length > 0));
        static string Dot(string s)
        {
            var t = s.Trim();
            if (t.Length == 0) return "";
            if (".?!".Contains(t[^1])) return $"{t} ";
            return $"{t}. ";
        }
        var output = new List<CiteSeg>();
        void Put(string text, bool italic = false)
        {
            if (text.Length > 0) output.Add(new CiteSeg(text, italic));
        }
        var a = AuthorString(V("author"), style);
        var url = Regex.Replace(V("url"), "^https?://", "");
        var doi = Regex.Replace(V("doi"), @"^https?://doi\.org/", "");
        if (style == "mla")
        {
            Put(Dot(a));
            if (type == "book")
            {
                Put(V("title"), true);
                Put(". ");
            }
            else Put($"“{V("title")}.” ");
            if (type is "website" or "journal" or "video")
            {
                Put(V("container"), true);
                Put(", ");
            }
            if (type == "journal")
            {
                Put(Join(new[] { V("volume").Length == 0 ? "" : $"vol. {V("volume")}", V("issue").Length == 0 ? "" : $"no. {V("issue")}" }, ", "));
                Put(V("volume").Length == 0 && V("issue").Length == 0 ? "" : ", ");
                Put(Dot(Join(new[] { V("year"), V("pages").Length == 0 ? "" : $"pp. {V("pages")}" }, ", ")));
            }
            else if (type == "book")
            {
                Put(Dot(Join(new[] { V("edition"), V("publisher"), V("year") }, ", ")));
            }
            else if (type == "coursefile")
            {
                Put(V("course"));
                Put(V("course").Length == 0 ? "" : ", ");
                Put(Dot(Join(new[] { V("institution"), V("year") }, ", ")));
                Put($"{lms}. ");
            }
            else
            {
                Put(Dot(Join(new[] { V("publisher"), Join(new[] { V("day"), V("month"), V("year") }, " ") }, ", ")));
            }
            if (url.Length > 0) Put($"{url}.");
            if (url.Length > 0 && V("accessed").Length > 0 && type == "website") Put($" Accessed {V("accessed")}.");
            return output;
        }
        if (style == "apa")
        {
            Put(Dot(a));
            var yearOr = V("year").Length == 0 ? "n.d." : V("year");
            var d = type is "journal" or "book" or "coursefile" ? yearOr : Join(new[] { yearOr, Join(new[] { V("month"), V("day") }, " ") }, ", ");
            Put($"({d}). ");
            switch (type)
            {
                case "book":
                    Put(V("title"), true);
                    Put(V("edition").Length == 0 ? "" : $" ({V("edition")})");
                    Put(". ");
                    break;
                case "journal":
                    Put(Dot(V("title")));
                    break;
                case "video":
                    Put(V("title"), true);
                    Put(" [Video]. ");
                    break;
                case "coursefile":
                    Put(V("title"), true);
                    Put(" [Lecture slides]. ");
                    break;
                default:
                    Put(V("title"), true);
                    Put(". ");
                    break;
            }
            if (type == "journal")
            {
                Put(V("container"), true);
                Put(V("volume").Length == 0 ? "" : ", ");
                Put(V("volume"), true);
                Put(V("issue").Length == 0 ? "" : $"({V("issue")})");
                Put(V("pages").Length == 0 ? "" : $", {V("pages")}");
                Put(". ");
                if (doi.Length > 0) Put($"https://doi.org/{doi}");
            }
            else if (type == "book")
            {
                Put(Dot(V("publisher")));
            }
            else if (type == "coursefile")
            {
                Put(Dot(Join(new[] { V("institution"), lms }, ". ")));
                if (url.Length > 0) Put($"https://{url}");
            }
            else
            {
                Put(Dot(V("container")));
                if (url.Length > 0) Put($"https://{url}");
            }
            return output;
        }
        // Chicago
        Put(Dot(a));
        if (type == "book")
        {
            Put(V("title"), true);
            Put(". ");
            Put(Dot(V("edition")));
            var imprint = Join(new[] { Join(new[] { V("city"), V("publisher") }, ": "), V("year") }, ", ");
            Put(imprint.Length == 0 ? "" : $"{imprint}.");
            return output;
        }
        Put($"“{V("title")}.” ");
        if (type == "journal")
        {
            Put(V("container"), true);
            Put(" ");
            Put(Join(new[] { V("volume"), V("issue").Length == 0 ? "" : $"no. {V("issue")}" }, ", "));
            Put(V("year").Length == 0 ? "" : $" ({V("year")})");
            Put(V("pages").Length == 0 ? "." : $": {V("pages")}.");
            if (doi.Length > 0) Put($" https://doi.org/{doi}.");
            return output;
        }
        if (type == "coursefile")
        {
            Put(Join(new[] { V("course").Length == 0 ? "" : $"{V("course")} lecture slides", V("institution"), V("year") }, ", "));
            Put($". {lms}.");
            return output;
        }
        Put(V("container"), true);
        Put(". ");
        var when = Join(new[] { V("month"), V("day") }, " ") + (V("year").Length == 0 ? "" : $"{(V("month").Length == 0 ? "" : ", ")}{V("year")}");
        Put(Dot(when));
        if (url.Length > 0) Put($"https://{url}.");
        return output;
    }

    public static string Plain(IEnumerable<CiteSeg> parts) => Regex.Replace(string.Concat(parts.Select(p => p.Text)), @"\s+", " ").Trim(' ', '\t');

    public static string InText(string style, string type, IReadOnlyDictionary<string, string> values, string lms)
    {
        var keys = Fields(type, lms).Select(f => f.Key).ToHashSet();
        string Val(string k) => keys.Contains(k) && values.TryGetValue(k, out var v) ? v.Trim(' ', '\t') : "";
        var s = Surname(Val("author")) is { Length: > 0 } sur ? sur : "Author";
        var year = Val("year").Length == 0 ? "n.d." : Val("year");
        var pg = Val("pages").Split(new[] { '–', '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return style switch
        {
            "mla" => $"({s}{(pg.Length == 0 ? "" : $" {pg}")})",
            "apa" => $"({s}, {year}{(pg.Length == 0 ? "" : $", p. {pg}")})",
            _ => $"({s} {year}{(pg.Length == 0 ? "" : $", {pg}")})",
        };
    }

    /// <summary>"8 October 2026".</summary>
    public static string TodayWords() => DateTime.Now.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"));

    /// <summary>What a pasted link tells: the kind of source and the fields it settles.</summary>
    public static (string Type, Dictionary<string, string> Values) Prefill(string raw)
    {
        var v = raw.Trim();
        var withScheme = Regex.IsMatch(v, "^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase) ? v : $"https://{v}";
        var uri = Uri.TryCreate(withScheme, UriKind.Absolute, out var u) ? u : null;
        var host = Regex.Replace(uri?.Host ?? "", @"^www\.", "").ToLowerInvariant();
        if (host == "youtube.com" || host.EndsWith(".youtube.com") || host == "youtu.be") return ("video", new() { ["container"] = "YouTube", ["url"] = v });
        if (host == "vimeo.com" || host.EndsWith(".vimeo.com")) return ("video", new() { ["container"] = "Vimeo", ["url"] = v });
        if (host == "doi.org" || host.EndsWith(".doi.org"))
        {
            var path = Uri.UnescapeDataString(uri?.AbsolutePath ?? "").Trim('/');
            return ("journal", new() { ["doi"] = path });
        }
        return ("website", new() { ["url"] = v, ["accessed"] = TodayWords() });
    }
}

/// <summary>
/// The saved citations and the style last used, kept in the app's preferences under the Mac's keys (SimplCitations, the
/// list as JSON; SimplCiteStyle).
/// </summary>
public static class CitationStore
{
    private const string SavedKey = "SimplCitations";
    private const string StyleKey = "SimplCiteStyle";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static List<SavedCitation>? saved;
    private static string? style;

    /// <summary>A citation saved or removed, or the style changed.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<SavedCitation> Saved => saved ??= Read();

    public static string Style
    {
        get
        {
            if (style != null) return style;
            string? s = null;
            try { s = Prefs.String(StyleKey); } catch { /* not a word: the default */ }
            return style = CiteBuilder.Styles.Any(x => x.Key == s) ? s! : "mla";
        }
        set
        {
            if (!CiteBuilder.Styles.Any(x => x.Key == value)) return;
            var was = Style;
            style = value;
            Prefs.Set(StyleKey, value);
            if (was != value) Changed?.Invoke();
        }
    }

    public static void Add(IReadOnlyList<CiteSeg> parts, string style)
    {
        (saved ??= Read()).Add(new SavedCitation
        {
            Id = Guid.NewGuid().ToString().ToUpperInvariant(), Style = CiteBuilder.StyleName(style),
            Parts = parts.Select(p => new CiteSeg(p.Text, p.Italic)).ToList(), Plain = CiteBuilder.Plain(parts),
        });
        Write();
    }

    public static void Remove(SavedCitation c)
    {
        (saved ??= Read()).RemoveAll(x => x.Id == c.Id);
        Write();
    }

    /// <summary>Every saved citation, alphabetically.</summary>
    public static IReadOnlyList<SavedCitation> Alphabetical => Saved.OrderBy(c => c.Plain, StringComparer.Ordinal).ToList();

    /// <summary>Every saved citation, alphabetically, one to a line.</summary>
    public static string List => string.Join("\n", Alphabetical.Select(c => c.Plain));

    private static List<SavedCitation> Read()
    {
        try
        {
            if (Prefs.String(SavedKey) is { Length: > 0 } json && JsonSerializer.Deserialize<List<SavedCitation>>(json, Options) is { } list)
                return list.Where(c => c != null).ToList();
        }
        catch (Exception e) { Log.Write($"citations: {e.Message}"); }
        return new List<SavedCitation>();
    }

    private static void Write()
    {
        Prefs.Set(SavedKey, JsonSerializer.Serialize(saved ?? new List<SavedCitation>(), Options));
        Changed?.Invoke();
    }
}

/// <summary>
/// For the screenshot suite: --cite-demo [website|journal|book|video|coursefile|partial] fills in a sample source (a
/// journal article unless named; "partial" a website with what is still needed), --cite-style apa picks the style, and
/// either saves two sample citations when none are saved yet.
/// </summary>
internal static class CitationDemo
{
    private static bool seeded;

    public static void SeedIfAsked(string lms)
    {
        if (seeded) return;
        seeded = true;
        if (Launch.Take("", "cite-style") is { } s) CitationStore.Style = s.ToLowerInvariant();
        if (Launch.Get("cite-demo") == null || CitationStore.Saved.Count > 0) return;
        CitationStore.Add(CiteBuilder.Build("mla", "book", Sample("book"), lms), "mla");
        CitationStore.Add(CiteBuilder.Build("apa", "website", Sample("website"), lms), "apa");
    }

    public static Dictionary<string, string> Sample(string type) => type switch
    {
        "book" => new()
        {
            ["author"] = "Jane R. Okonkwo", ["title"] = "The Unread Syllabus", ["edition"] = "2nd ed.", ["city"] = "Chicago",
            ["publisher"] = "University of Chicago Press", ["year"] = "2026",
        },
        "website" => new()
        {
            ["author"] = "Amir Haddad", ["title"] = "How Students Actually Read Syllabi", ["container"] = "The Atlantic", ["month"] = "March",
            ["day"] = "4", ["year"] = "2026", ["url"] = "https://www.theatlantic.com/education/2026/03/syllabi/", ["accessed"] = "12 September 2026",
        },
        "partial" => new() { ["title"] = "How Students Actually Read Syllabi", ["url"] = "theatlantic.com/education/2026/03/syllabi/", ["accessed"] = "12 September 2026" },
        "video" => new()
        {
            ["author"] = "Simpl Courses", ["title"] = "Reading a Rubric in Two Minutes", ["container"] = "YouTube", ["month"] = "March", ["day"] = "4",
            ["year"] = "2026", ["url"] = "youtube.com/watch?v=Rb7x2Q4mZtE",
        },
        "coursefile" => new()
        {
            ["author"] = "Yuwen Li", ["title"] = "Lec06 — Composition of Functions", ["course"] = "MATH 021", ["institution"] = "UC Merced", ["year"] = "2026",
        },
        _ => new()
        {
            ["author"] = "Jane R. Okonkwo; Amir Haddad", ["title"] = "Reading Habits in First-Year Courses", ["container"] = "Journal of Higher Education",
            ["volume"] = "47", ["issue"] = "3", ["year"] = "2026", ["pages"] = "112–137", ["doi"] = "10.1080/00221546.2026.1234567",
        },
    };
}

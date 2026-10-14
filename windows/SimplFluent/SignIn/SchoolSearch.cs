using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Simpl.Engine;
using Simpl.Support;

namespace Simpl.SignIn;

/// <summary>
/// The schools to search (the Mac's SchoolSearch): Instructure's and D2L's listings carried in the app
/// (extension/data/schools.json), and — after a pause in the typing — a Brightspace at the name typed, and Instructure's
/// own "Find my school" lookup for what the listings lack. A site listed under several names ("State Center Community
/// College District" and "State Center SCCCD", both scccd.instructure.com) is one row: the name that matched best, the
/// others it goes by said under it ("also: State Center SCCCD").
/// </summary>
public sealed class SchoolSearch : Observable
{
    /// <summary>A school's site: the name shown, its address, and the other names it is listed under that matched.</summary>
    public sealed record School(string Name, string Domain, IReadOnlyList<string>? Also = null)
    {
        /// <summary>One row per site, whatever it is called.</summary>
        public string Id => Domain.ToLowerInvariant();

        /// <summary>The row's second line: the address, and the other names it goes by — two, then how many more.</summary>
        public string Sub
        {
            get
            {
                if (Also is not { Count: > 0 } a) return Domain;
                var names = string.Join(", ", a.Take(2));
                if (a.Count > 2) names += $" +{a.Count - 2}";
                return $"{Domain} · also: {names}";
            }
        }
    }

    private sealed record Entry(School School, string Folded, string[] Words);

    private List<Entry> entries = new();
    private List<School> results = new();
    public List<School> Results { get => results; private set => Set(ref results, value); }
    private bool asking;
    public bool Asking { get => asking; private set => Set(ref asking, value); }
    private string last = "";
    private CancellationTokenSource? pending;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Folded for matching: lower case, accents off, punctuation as spaces ("Université" finds "universite").</summary>
    public static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var l = char.ToLowerInvariant(c);
            sb.Append(char.IsLetterOrDigit(l) || l == '.' ? l : ' ');
        }
        return sb.ToString();
    }

    /// <summary>"catcourses.ucmerced.edu", "https://school.instructure.com/login" … → the bare host.</summary>
    public static string? NormalizeHost(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        if (s.Length == 0) return null;
        if (!s.Contains("://")) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return null;
        var host = u.Host;
        return host.Contains('.') && !host.EndsWith('.') ? host : null;
    }

    /// <summary>The query as an address when it reads as one (a dot, no spaces): offered to open as it is.</summary>
    public static string? AddressLike(string q)
    {
        var t = q.Trim().ToLowerInvariant();
        if (!t.Contains('.') || t.Contains(' ') || NormalizeHost(t) == null) return null;
        return t.Replace("https://", "").Replace("http://", "").Split('/')[0];
    }

    public async Task Load()
    {
        if (entries.Count > 0) return;
        var path = Path.Combine(ScriptBundle.ExtensionDir, "data", "schools.json");
        entries = await Task.Run(() =>
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                return doc.RootElement.GetProperty("schools").EnumerateArray()
                    .Select(r => r.EnumerateArray().Select(x => x.GetString() ?? "").ToArray())
                    .Where(r => r.Length >= 2)
                    .Select(r =>
                    {
                        var folded = Fold($"{r[0]} {r[1]}");
                        return new Entry(new School(r[0], r[1]), folded, folded.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    }).ToList();
            }
            catch { return new List<Entry>(); }
        });
        if (last.Length > 0) Find(last);
    }

    /// <summary>The listings matching every word typed, best first: a name that starts with it, then a word that does, then the rest.</summary>
    public void Find(string raw)
    {
        last = raw;
        pending?.Cancel();
        var q = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (q.Length == 0) { Results = new(); Asking = false; return; }
        var scored = new List<(int Score, School School)>();
        foreach (var e in entries)
        {
            if (!q.All(t => e.Folded.Contains(t))) continue;
            var score = e.Folded.StartsWith(q[0]) ? 4 : 0;
            score += q.Count(t => e.Words.Any(w => w.StartsWith(t))) * 2;
            scored.Add((score, e.School));
        }
        Results = BySite(scored).Take(60).ToList();
        if (raw.Trim().Length < 3 || AddressLike(raw) != null) { Asking = false; return; }
        var lookup = Results.Count < 8;
        Asking = lookup;
        var cts = pending = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, cts.Token);
                var own = await Brightspace(raw);
                var more = lookup ? await Lookup(raw) : new List<School>();
                if (cts.IsCancellationRequested) return;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (last != raw) return;
                    // (a Brightspace at the very name typed is the school's own: first; what the lookup adds, after)
                    Results = Join(Join(Results, own, first: true), more, first: false);
                    Asking = false;
                });
            }
            catch (TaskCanceledException) { }
        });
    }

    /// <summary>
    /// The listings matched, one row a site, best first: each site under the name that matched best (of two as good,
    /// the shorter), the others it is listed under kept for its second line; the sites in the order of their best match,
    /// then by name.
    /// </summary>
    public static IEnumerable<School> BySite(IEnumerable<(int Score, School School)> scored) =>
        scored.GroupBy(s => s.School.Id)
            .Select(g =>
            {
                var ranked = g.OrderByDescending(s => s.Score).ThenBy(s => s.School.Name.Length).ThenBy(s => s.School.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                var best = ranked[0];
                return (best.Score, School: best.School with { Also = Others(best.School.Name, ranked.Skip(1).Select(s => s.School.Name)) });
            })
            .OrderByDescending(s => s.Score).ThenBy(s => s.School.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(s => s.School);

    /// <summary>Schools found later (a Brightspace, Instructure's lookup) added to the rows: a site already there gains
    /// their names as its other names, in its place; a new one goes first or last, merged with its like.</summary>
    public static List<School> Join(List<School> rows, IEnumerable<School> found, bool first)
    {
        var list = rows.ToList();
        var added = new List<School>();
        foreach (var s in found)
        {
            var i = list.FindIndex(r => r.Id == s.Id);
            if (i >= 0) { list[i] = WithName(list[i], s.Name); continue; }
            var j = added.FindIndex(r => r.Id == s.Id);
            if (j >= 0) added[j] = WithName(added[j], s.Name);
            else added.Add(s);
        }
        if (first) list.InsertRange(0, added); else list.AddRange(added);
        return list;
    }

    private static School WithName(School row, string name) =>
        row with { Also = Others(row.Name, (row.Also ?? Array.Empty<string>()).Append(name)) };

    /// <summary>The other names a site goes by: once each, never its own row name again.</summary>
    private static IReadOnlyList<string> Others(string title, IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { title };
        return names.Where(n => n.Trim().Length > 0 && seen.Add(n.Trim())).Select(n => n.Trim()).ToList();
    }

    /// <summary>A school's Brightspace is most often at &lt;its name&gt;.brightspace.com: asked there.</summary>
    private static async Task<List<School>> Brightspace(string name)
    {
        var words = Fold(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new string(w.Where(c => c < 128 && char.IsLetterOrDigit(c)).ToArray())).Where(w => w.Length > 0).ToList();
        if (words.Count == 0) return new();
        var slugs = new List<string> { string.Concat(words) };
        if (words.Count > 1) slugs.Add(words[0]);
        var outList = new List<School>();
        foreach (var slug in slugs.Where(s => s.Length is >= 3 and <= 40))
            if (await Probe(slug) is { } s) outList.Add(s);
        return outList;
    }

    private static async Task<School?> Probe(string slug)
    {
        var host = $"{slug}.brightspace.com";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var res = await Http.GetAsync($"https://{host}/d2l/login", cts.Token);
            if ((int)res.StatusCode >= 500) return null;
            var name = host;
            var page = await res.Content.ReadAsStringAsync(cts.Token);
            if (page.Length > 200_000) page = page[..200_000];
            if (string.Equals(res.RequestMessage?.RequestUri?.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(page, "<title>([^<]*)</title>", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var t = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
                    t = Regex.Replace(t, @"^\s*Log ?in\s*[-–—|:]\s*", "", RegexOptions.IgnoreCase).Trim();
                    if (t.Length > 0 && t.Length <= 120 && !t.ToLowerInvariant().StartsWith("log")) name = t;
                }
            }
            return new School(name, host);
        }
        catch { return null; }
    }

    private sealed class Found { public string? Name { get; set; } public string? Domain { get; set; } }

    /// <summary>Instructure's "Find my school" lookup (the one Canvas's own apps use).</summary>
    private static async Task<List<School>> Lookup(string name)
    {
        try
        {
            var url = "https://canvas.instructure.com/api/v1/accounts/search?per_page=50&name=" + Uri.EscapeDataString(name);
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return new();
            var rows = JsonSerializer.Deserialize<List<Found>>(await res.Content.ReadAsStringAsync(), Json.Options) ?? new();
            return rows.Where(r => !string.IsNullOrEmpty(r.Name) && r.Domain != null && NormalizeHost(r.Domain) != null)
                       .Select(r => new School(r.Name!, r.Domain!.ToLowerInvariant())).ToList();
        }
        catch { return new(); }
    }
}

/// <summary>
/// The school's name as the student picked it in the search ("State Center Community College District"), kept for its
/// address (the Mac's SchoolNames) so the sign-in card can say where the password goes in words the student just
/// confirmed. None for an address typed by hand, nor for one chosen before this was kept: the card then says "your
/// school". (--school-name gives one to a development run's pictures.)
/// </summary>
public static class SchoolName
{
    private static string Key(string host) => "schoolName:" + host.ToLowerInvariant();

    public static void Remember(string name, string host)
    {
        var n = name.Trim();
        Prefs.Set(Key(host), n.Length == 0 || string.Equals(n, host, StringComparison.OrdinalIgnoreCase) ? null : n);
    }

    public static void Forget(string host) => Prefs.Remove(Key(host));

    public static string? For(string host) =>
        Launch.IsDevelopment && Launch.Get("school-name") is { Length: > 0 } given ? given
        : Prefs.String(Key(host)) is { Length: > 0 } kept ? kept : null;
}

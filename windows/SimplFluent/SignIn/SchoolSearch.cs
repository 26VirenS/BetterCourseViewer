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
/// own "Find my school" lookup for what the listings lack.
/// </summary>
public sealed class SchoolSearch : Observable
{
    public sealed record School(string Name, string Domain)
    {
        public string Id => $"{Name}|{Domain}";
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
        Results = scored.OrderByDescending(s => s.Score).ThenBy(s => s.School.Name, StringComparer.CurrentCultureIgnoreCase).Take(60).Select(s => s.School).ToList();
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
                    var seen = Results.Select(r => r.Id).ToHashSet();
                    var list = own.Where(s => seen.Add(s.Id)).ToList();
                    list.AddRange(Results);
                    list.AddRange(more.Where(s => seen.Add(s.Id)));
                    Results = list;
                    Asking = false;
                });
            }
            catch (TaskCanceledException) { }
        });
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

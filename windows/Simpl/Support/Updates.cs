using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Simpl.Support;

/// <summary>
/// Settings ▸ Updates (the Mac's Updater, as far as a preview goes): this version, whether updates are wanted on their
/// own and whether betas are, Check Now against the versions published (GitHub's releases of this repository, the
/// Windows ones), and the versions published before. A new version is a download from its own page for now — it takes
/// this copy's place when it is unzipped over it; the Mac's installing-by-itself comes with a signed build.
/// </summary>
public static class Updates
{
    public enum Mode { Idle, Checking, UpToDate, Available, Failed }

    public sealed record Release(string Tag, int Run, string Version, bool Beta, DateTime? Published, long Size, string Url, string? Notes)
    {
        /// <summary>"Oct 11, 2026 · 52.9 MB": when it came out and how large its download is.</summary>
        public string Detail
        {
            get
            {
                var parts = new List<string>();
                if (Published is { } d) parts.Add(d.ToLocalTime().ToString("MMM d, yyyy"));
                if (Size > 0) parts.Add(Size >= 1 << 20 ? $"{Size / 1048576.0:0.0} MB" : $"{Size / 1024.0:0} KB");
                return string.Join(" · ", parts);
            }
        }
    }

    public static event Action? Changed;
    public static Mode State { get; private set; } = Mode.Idle;
    public static string? Message { get; private set; }
    public static Release? Found { get; private set; }
    public static List<Release>? Known { get; private set; }

    public static bool Automatic { get => Prefs.Bool("updates:auto", true); set { Prefs.Set("updates:auto", value); Changed?.Invoke(); } }
    public static bool Beta { get => Prefs.Bool("updates:beta", false); set { Prefs.Set("updates:beta", value); Changed?.Invoke(); } }
    public static DateTime? LastCheck
    {
        get => DateTime.TryParse(Prefs.String("updates:checked"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        private set => Prefs.Set("updates:checked", value?.ToString("o"));
    }

    public static bool IsDevelopmentRun => Launch.IsDevelopment;

    /// <summary>This copy's version as its release names it ("0.1.0 (v015)"), and its run number (0 for a build of one's own).</summary>
    public static string Version => WhatsNew.Current;
    public static int CurrentRun => Regex.Match(Version, @"\(v(\d+)\)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task Check()
    {
        if (State == Mode.Checking) return;
        State = Mode.Checking;
        Message = null;
        Changed?.Invoke();
        try
        {
            var list = await Releases();
            var newest = list.FirstOrDefault(r => Beta || !r.Beta);
            if (newest != null && newest.Run > CurrentRun) { Found = newest; State = Mode.Available; }
            else { Found = null; State = Mode.UpToDate; }
        }
        catch (Exception e)
        {
            State = Mode.Failed;
            Message = "Could not check: " + e.Message;
        }
        LastCheck = DateTime.UtcNow;
        Changed?.Invoke();
    }

    /// <summary>The Windows versions published, newest first.</summary>
    public static async Task<List<Release>> Releases()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/26VirenS/BetterCourseViewer/releases?per_page=40");
        req.Headers.TryAddWithoutValidation("User-Agent", "Simpl-for-Windows");
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw new Exception($"GitHub answered {(int)res.StatusCode}.");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var list = new List<Release>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            var m = Regex.Match(tag, @"^windows-app-v(\d+)$");
            if (!m.Success) continue;
            var run = int.Parse(m.Groups[1].Value);
            long size = 0;
            if (r.TryGetProperty("assets", out var assets)) foreach (var a in assets.EnumerateArray()) size += a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            DateTime? published = r.TryGetProperty("published_at", out var p) && DateTime.TryParse(p.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
            list.Add(new Release(tag, run, $"v{run:000}", r.TryGetProperty("prerelease", out var pre) && pre.GetBoolean(), published, size,
                r.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "", r.TryGetProperty("body", out var b) ? b.GetString() : null));
        }
        list.Sort((a, b) => b.Run.CompareTo(a.Run));
        Known = list;
        Changed?.Invoke();
        return list;
    }

    /// <summary>A version's page, in the browser: its download is there.</summary>
    public static void Open(Release r)
    {
        if (Uri.TryCreate(r.Url, UriKind.Absolute, out var u)) Engine.Engine.OpenExternally(u);
    }
}

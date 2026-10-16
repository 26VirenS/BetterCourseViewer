using System.Text.Json;
using System.Text.RegularExpressions;

namespace Simpl.Support;

/// <summary>
/// The Windows versions published, and the one this copy updates to (Support/Updates.cs looks, downloads and installs):
/// GitHub's releases of this repository read by their tags, and chosen by run number and by Get beta updates — the
/// usual releases for every copy, the betas too only for one with it on (the Mac's beta feed). Nothing here asks the
/// network or reads a setting, so the tests (windows/SimplFluent.Tests/UpdatesTests.cs) give it releases of their own —
/// and whether one an earlier version put back stays back from waits (<see cref="StaysBack"/>).
/// </summary>
public static partial class Updates
{
    /// <summary>
    /// The tags the Windows 11-look versions are published under (.github/workflows/windows-fluent.yml): a usual
    /// release's windows-fluent-v&lt;run&gt;, a beta's windows-fluent-beta-v&lt;run&gt; (Run workflow ▸ beta). A beta has a tag
    /// of its own because a copy from before the beta channel (0.3.0 and earlier) lists only the first, and took any
    /// 0.x version it found there, a beta too: under its own tag a beta never reaches it.
    /// </summary>
    private static readonly Regex Tags = new(@"^windows-fluent-(beta-)?v(\d+)$", RegexOptions.CultureInvariant);

    /// <summary>A version's run and whether it is a beta, from its release's tag and title — a beta's tag says so, and
    /// so does the title of one published under the usual tag as a beta; null for a tag that is not a Windows 11-look
    /// version's (the Mac's, the extension's, the first Windows app's windows-app-v…).</summary>
    internal static (int Run, bool Beta)? Read(string tag, string title)
    {
        var m = Tags.Match(tag);
        if (!m.Success || !int.TryParse(m.Groups[2].Value, out var run)) return null;
        // (not GitHub's pre-release mark: every Windows 11-look version is published as a pre-release, the usual ones
        //  too, and with that as the mark Get beta updates off would never offer one)
        return (run, m.Groups[1].Success || Regex.IsMatch(title, @"\bbeta\b", RegexOptions.IgnoreCase));
    }

    /// <summary>The Windows versions in GitHub's list of releases (its JSON), newest first: a draft left out, and a run
    /// published both as a beta and as a usual release (the beta promoted) listed once, as the usual release.</summary>
    internal static List<Release> Parse(JsonElement releases)
    {
        var list = new List<Release>();
        foreach (var r in releases.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            var tag = r.TryGetProperty("tag_name", out var tg) && tg.ValueKind == JsonValueKind.String ? tg.GetString() ?? "" : "";
            var title = r.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() ?? "" : "";
            if (Read(tag, title) is not { } version) continue;
            var (run, beta) = version;
            // (the zip's size, as the version's download; the installer apart, for a copy it installed)
            long size = 0;
            Asset? setup = null;
            if (r.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var bytes = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
                    if (name.Equals(SetupName, StringComparison.OrdinalIgnoreCase)
                        && a.TryGetProperty("browser_download_url", out var u) && u.GetString() is { Length: > 0 } url)
                    {
                        // (GitHub gives each file's digest, "sha256:…", on releases made since mid-2025)
                        var digest = a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String && dg.GetString() is { } dgs
                                     && dgs.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? dgs[7..] : null;
                        setup = new Asset(url, bytes, digest);
                    }
                    else size += bytes;
                }
            DateTime? published = r.TryGetProperty("published_at", out var p) && DateTime.TryParse(p.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
            list.Add(new Release(tag, run, $"v{run:000}", beta, published, size,
                r.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? "" : "", r.TryGetProperty("body", out var b) ? b.GetString() : null, setup));
        }
        // (newest first; of a run's two, the usual release before the beta — and only it kept)
        list.Sort((a, b) => a.Run != b.Run ? b.Run.CompareTo(a.Run) : a.Beta.CompareTo(b.Beta));
        return list.Where((r, i) => i == 0 || list[i - 1].Run != r.Run).ToList();
    }

    /// <summary>What a look at the versions finds (<see cref="Choose"/>): the version to update to (null: this copy is up
    /// to date), and whether it goes in whatever the update settings say.</summary>
    internal readonly record struct Choice(Release? Found, bool Critical);

    /// <summary>
    /// The version this copy updates to: the newest, by run, of those newer than it that it may take — the usual
    /// releases, and the betas too only while Get beta updates is on (<paramref name="betas"/>). So a usual release
    /// newer than a beta takes over from it; and with betas turned off, a copy on a beta stays on it (never goes back to
    /// an older version) until a usual release newer than it comes. Critical when it, or one it may take between this
    /// copy and it, is a Critical Update: a usual release's reaches every copy, a beta's only the copies taking betas.
    /// </summary>
    internal static Choice Choose(IEnumerable<Release> published, int currentRun, bool betas)
    {
        var mine = published.Where(r => r.Run > currentRun && (betas || !r.Beta)).ToList();
        var newest = mine.OrderByDescending(r => r.Run).ThenBy(r => r.Beta).FirstOrDefault();
        return new Choice(newest, mine.Any(r => r.Critical));
    }

    /// <summary>
    /// The Critical Update the site's feed (/app/windows-latest.json, written by scripts/publish-site.sh; read by the
    /// five-minute look) names for a copy: the newest of the usual releases' (<c>critical</c>), and with Get beta updates
    /// on (<paramref name="betas"/>) the newest of all, a beta's too (<c>criticalBeta</c>) — as <see cref="Choose"/>
    /// finds one among the releases a copy may take. 0 when it names none, or the feed is not as it should be.
    /// </summary>
    internal static int CriticalRun(JsonElement feed, bool betas)
    {
        if (feed.ValueKind != JsonValueKind.Object) return 0;
        int Run(string name) => feed.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var run) ? run : 0;
        return betas ? Math.Max(Run("critical"), Run("criticalBeta")) : Run("critical");
    }

    /// <summary>
    /// The version found waits for Update now (0.3.7, Updates.KeptBack): an earlier version put back from run
    /// <paramref name="stayingFrom"/> stays, so that version and those before it are not put in by themselves — or the
    /// newest would go straight back in — while one newer than it is, as every update is, and a Critical Update
    /// (<paramref name="critical"/>, as <see cref="Choose"/> says) whatever it.
    /// </summary>
    internal static bool StaysBack(Release? found, bool critical, int? stayingFrom) =>
        !critical && found != null && stayingFrom is { } back && found.Run <= back;
}

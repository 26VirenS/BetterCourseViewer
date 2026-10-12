using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Simpl.Support;

/// <summary>
/// Settings ▸ Updates (the Mac's Updater, as far as a preview goes): this version, whether updates are wanted on their
/// own and whether betas are, Check Now against the versions published (GitHub's releases of this repository, the
/// Windows ones), and the versions published before. A copy Simpl-Setup.exe installed updates through it: Download
/// fetches the new version's installer, and Restart to Update runs it quietly — Simpl quits, the installer puts the new
/// version in its place and opens it again. A copy run from its zip opens the new version's page instead, to be
/// unzipped over it (or installed).
/// </summary>
public static class Updates
{
    public enum Mode { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }

    /// <summary>A file a version was published with: where it downloads from, its size, its SHA-256 when GitHub gives it.</summary>
    public sealed record Asset(string Url, long Size, string? Sha256);

    public sealed record Release(string Tag, int Run, string Version, bool Beta, DateTime? Published, long Size, string Url, string? Notes, Asset? Setup)
    {
        /// <summary>"Oct 11, 2026 · 52.9 MB": when it came out and how large the download this copy would take is.</summary>
        public string Detail
        {
            get
            {
                var parts = new List<string>();
                if (Published is { } d) parts.Add(d.ToLocalTime().ToString("MMM d, yyyy"));
                var size = Installed && Setup is { } s ? s.Size : Size;
                if (size > 0) parts.Add(size >= 1 << 20 ? $"{size / 1048576.0:0.0} MB" : $"{size / 1024.0:0} KB");
                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>The installer's name among a version's files, and the quiet update it is run for (Installer/Simpl.iss).</summary>
    public const string SetupName = "Simpl-Setup.exe";
    public static readonly string[] SilentArguments = { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/LAUNCH" };

    /// <summary>The installer's AppId (Installer/Simpl.iss): its entry in Settings ▸ Apps is the uninstall key named by it.</summary>
    public const string InstallerAppId = "{B65F70B7-E13E-4CCC-9E1C-0B659660D130}";

    public static event Action? Changed;
    /// <summary>A download moved on (<see cref="Progress"/>): often, so apart from <see cref="Changed"/>.</summary>
    public static event Action? ProgressChanged;
    public static Mode State { get; private set; } = Mode.Idle;
    public static string? Message { get; private set; }
    public static Release? Found { get; private set; }
    public static List<Release>? Known { get; private set; }
    /// <summary>How much of the installer has come, 0 to 1, while <see cref="Mode.Downloading"/>.</summary>
    public static double Progress { get; private set; }
    private static string? downloaded;

    public static bool Automatic { get => Prefs.Bool("updates:auto", true); set { Prefs.Set("updates:auto", value); Changed?.Invoke(); } }
    public static bool Beta { get => Prefs.Bool("updates:beta", false); set { Prefs.Set("updates:beta", value); Changed?.Invoke(); } }
    public static DateTime? LastCheck
    {
        get => DateTime.TryParse(Prefs.String("updates:checked"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        private set => Prefs.Set("updates:checked", value?.ToString("o"));
    }

    public static bool IsDevelopmentRun => Launch.IsDevelopment;

    /// <summary>This copy's version as its release names it ("0.2.0 (v001)"), and its run number (0 for a build of one's own).</summary>
    public static string Version => WhatsNew.Current;
    public static int CurrentRun => Regex.Match(Version, @"\(v(\d+)\)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    private static bool? installed;

    /// <summary>
    /// This copy was put here by Simpl-Setup.exe: its uninstall entry (the current user's — Simpl installs for one
    /// person) names the folder this Simpl.exe runs from. A copy unzipped elsewhere, or one copied out of that folder, is not.
    /// </summary>
    public static bool Installed => installed ??= FindInstalled();

    private static bool FindInstalled()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerAppId}_is1");
            return k?.GetValue("InstallLocation") is string at && at.Length > 0 && SameFolder(at, AppPaths.AppDir);
        }
        catch { return false; }
    }

    private static bool SameFolder(string a, string b)
    {
        static string Norm(string p)
        {
            if (p.StartsWith(@"\\?\", StringComparison.Ordinal) && !p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = p[4..];
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.Trim().Trim('"'))).TrimEnd('\\', '/');
        }
        try { return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>The version found is installed from here (Download, then Restart to Update), not from its page.</summary>
    public static bool InstallsHere => Installed && Found?.Setup != null;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    // (the installer is tens of megabytes: a download has as long as it needs, so long as it keeps coming)
    private static readonly HttpClient Downloads = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task Check()
    {
        if (State is Mode.Checking or Mode.Downloading) return;
        State = Mode.Checking;
        Message = null;
        Changed?.Invoke();
        try
        {
            var list = await Releases();
            var newest = list.FirstOrDefault(r => Beta || !r.Beta);
            if (newest != null && newest.Run > CurrentRun)
            {
                // (the same version downloaded already: ready to install as it was)
                var ready = Found?.Tag == newest.Tag && downloaded != null && File.Exists(downloaded);
                Found = newest;
                State = ready ? Mode.Ready : Mode.Available;
            }
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
            var m = Regex.Match(tag, @"^windows-fluent-v(\d+)$");
            if (!m.Success) continue;
            var run = int.Parse(m.Groups[1].Value);
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
            list.Add(new Release(tag, run, $"v{run:000}", r.TryGetProperty("prerelease", out var pre) && pre.GetBoolean(), published, size,
                r.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? "" : "", r.TryGetProperty("body", out var b) ? b.GetString() : null, setup));
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

    /// <summary>
    /// Download, for the version found: its installer fetched into a folder of its own under Windows' temporary files
    /// (checked against the size and digest published), then ready for Restart to Update — for a copy the installer
    /// put here. Any other copy: the version's page, as before.
    /// </summary>
    public static async Task Download()
    {
        if (Found is not { } r || State is Mode.Downloading or Mode.Checking) return;
        if (!InstallsHere || r.Setup is not { } setup) { Open(r); return; }
        State = Mode.Downloading;
        Progress = 0;
        Message = null;
        Changed?.Invoke();
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Simpl-Update", r.Tag);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, SetupName);
            var part = file + ".part";
            using (var req = new HttpRequestMessage(HttpMethod.Get, setup.Url))
            {
                req.Headers.TryAddWithoutValidation("User-Agent", "Simpl-for-Windows");
                using var res = await Downloads.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (!res.IsSuccessStatusCode) throw new Exception($"GitHub answered {(int)res.StatusCode}.");
                var total = res.Content.Headers.ContentLength ?? setup.Size;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var from = await res.Content.ReadAsStreamAsync())
                await using (var to = File.Create(part))
                {
                    var buffer = new byte[1 << 16];
                    long got = 0;
                    var told = 0.0;
                    int n;
                    // (a minute with nothing coming: given up, to be tried again)
                    using var stalled = new CancellationTokenSource();
                    while (true)
                    {
                        stalled.CancelAfter(TimeSpan.FromMinutes(1));
                        n = await from.ReadAsync(buffer, stalled.Token);
                        if (n <= 0) break;
                        await to.WriteAsync(buffer.AsMemory(0, n));
                        hash.AppendData(buffer, 0, n);
                        got += n;
                        if (total > 0 && (double)got / total - told >= 0.01)
                        {
                            told = Progress = Math.Min(1, (double)got / total);
                            ProgressChanged?.Invoke();
                        }
                    }
                    if (setup.Size > 0 && got != setup.Size) throw new Exception("the download stopped short.");
                }
                if (setup.Sha256 is { } want && !Convert.ToHexString(hash.GetHashAndReset()).Equals(want, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("the download is not the file that was published.");
            }
            File.Move(part, file, true);
            downloaded = file;
            Progress = 1;
            State = Mode.Ready;
            Log.Write($"update: {r.Tag}'s installer downloaded");
        }
        catch (Exception e)
        {
            State = Mode.Failed;
            Message = "Could not download: " + (e is OperationCanceledException ? "nothing came for a minute." : e.Message);
            Log.Write("update: " + e.Message);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Restart to Update: the installer started quietly (it waits for Simpl to have quit, puts the new version in its
    /// place and opens it again), and Simpl quits as Quit Simpl does, so nothing of it holds the files.
    /// </summary>
    public static void InstallAndRestart()
    {
        if (State != Mode.Ready || downloaded is not { } file || !File.Exists(file)) return;
        try
        {
            // (through the shell, so it inherits nothing of Simpl's — no handle of it keeps Simpl "open" for the installer)
            var dir = Path.GetDirectoryName(file)!;
            var log = Path.Combine(dir, "Setup.log");
            Process.Start(new ProcessStartInfo(file, string.Join(' ', SilentArguments) + $" \"/LOG={log}\"") { UseShellExecute = true, WorkingDirectory = dir });
        }
        catch (Exception e)
        {
            State = Mode.Failed;
            Message = "Could not start the update: " + e.Message;
            Changed?.Invoke();
            return;
        }
        Log.Write("update: installing " + file + ", and quitting");
        EraseAll.Quit();
    }

    /// <summary>The installers downloaded for this version or an earlier one, deleted (each is tens of megabytes).</summary>
    public static void Tidy()
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "Simpl-Update");
            if (!Directory.Exists(root) || CurrentRun == 0) return;
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                var m = Regex.Match(Path.GetFileName(d), @"^windows-fluent-v(\d+)$");
                if (!m.Success || int.Parse(m.Groups[1].Value) > CurrentRun) continue;
                try { Directory.Delete(d, true); } catch { /* (its installer still running: the next start) */ }
            }
        }
        catch (Exception e) { Log.Write("update: tidying: " + e.Message); }
    }
}

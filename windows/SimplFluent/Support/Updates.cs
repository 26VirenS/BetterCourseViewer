using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Simpl.Support;

/// <summary>
/// Settings ▸ Updates, and Simpl keeping itself up to date (the Mac's Updater). At launch when the last look is over an
/// hour old, and every hour after (Check for updates automatically, on unless turned off), it reads the versions
/// published — GitHub's releases of this repository, the Windows ones (Support/UpdatesChoice.cs: a beta only with Get
/// beta updates on) — and a newer one is offered in Settings ▸ Updates, with the versions published before and whether
/// betas are wanted. A copy Simpl-Setup.exe installed always installs its updates itself (0.3.7: Install updates
/// automatically is no longer a setting): it downloads the new version's installer by itself (picked up where it
/// stopped, tried again a while later when it fails; checked against the size and SHA-256 published, and — when this
/// copy is signed — signed by the same publisher) and puts it in at once, unless
/// something is open that an update must not cut off (<see cref="HeldBy"/>: a quiz, an Alta or a Macmillan assignment,
/// a page of the school's own or a tool's, a dialog, a hand-in, the sign-in, the tour; or the student working in the
/// window that moment) — then it waits, and goes in as soon as all of it is closed. Going in, it notes where the window
/// was (<see cref="Resume"/>), Simpl quits as Quit Simpl does, and the installer, run quietly, puts the new version in
/// place and opens it again, where the student was. A Critical Update (a release whose notes carry
/// <see cref="CriticalMark"/>) goes in whatever the pause or an earlier version put back say. Restart to Update puts a
/// downloaded version in at once. After an earlier version is put back (Earlier versions ▸ Install…), Simpl stays on it
/// (<see cref="StayingFrom"/>) until a version newer than the one it went back from comes, or Update now. A copy run
/// from its zip opens the new version's page instead, to be unzipped over it (or installed). A development run never
/// checks or installs anything; --update-test plays an update through against a local file
/// (windows/SimplFluent/README.md, Updates).
/// </summary>
public static partial class Updates
{
    public enum Mode { Idle, Checking, UpToDate, Available, Downloading, Ready, Installing, Failed }

    /// <summary>A file a version was published with: where it downloads from, its size, its SHA-256 when GitHub gives it.</summary>
    public sealed record Asset(string Url, long Size, string? Sha256);

    public sealed record Release(string Tag, int Run, string Version, bool Beta, DateTime? Published, long Size, string Url, string? Notes, Asset? Setup)
    {
        /// <summary>A Critical Update: its notes carry the mark (<see cref="CriticalMark"/>).</summary>
        public bool Critical => Notes?.Contains(CriticalMark, StringComparison.Ordinal) == true;

        /// <summary>"Oct 11 · 52.9 MB": when it came out (the year only when it is not this one's, "Oct 11, 2025") and how
        /// large the download this copy would take is.</summary>
        public string Detail
        {
            get
            {
                var parts = new List<string>();
                if (Published is { } d)
                {
                    var local = d.ToLocalTime();
                    parts.Add(local.ToString(local.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture));
                }
                var size = Installed && Setup is { } s ? s.Size : Size;
                if (size > 0) parts.Add(size >= 1 << 20 ? $"{size / 1048576.0:0.0} MB" : $"{size / 1024.0:0} KB");
                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>
    /// Something open that an update must not cut off: what it is, for simpl.log ("quiz"), and when the update goes in,
    /// for the notice over the window ("when you close the quiz") — none for what passes in a moment (a menu, typing).
    /// One taken with <see cref="Hold"/> holds until it is disposed (closed).
    /// </summary>
    public sealed class Holding : IDisposable
    {
        public string What { get; }
        public string? When { get; }

        public Holding(string what, string? when)
        {
            What = what;
            When = when;
        }

        public void Dispose() => Let(this);
    }

    /// <summary>What a Critical Update's release notes carry, as the Mac's do (scripts/release-mac-native.sh): the
    /// Windows workflow's Run workflow ▸ critical adds it.</summary>
    public const string CriticalMark = "<!-- simpl-critical -->";

    /// <summary>The installer's name among a version's files, and the quiet update it is run for (Installer/Simpl.iss).</summary>
    public const string SetupName = "Simpl-Setup.exe";
    public static readonly string[] SilentArguments = { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/LAUNCH" };

    /// <summary>The installer's AppId (Installer/Simpl.iss): its entry in Settings ▸ Apps is the uninstall key named by it.</summary>
    public const string InstallerAppId = "{B65F70B7-E13E-4CCC-9E1C-0B659660D130}";
    /// <summary>That entry, the current user's (Inno Setup names it by the AppId and "_is1"): where Simpl is installed, and
    /// its uninstaller (Support/Uninstall.cs).</summary>
    public const string EntryKey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerAppId}_is1";

    public static event Action? Changed;
    /// <summary>A download moved on (<see cref="Progress"/>): often, so apart from <see cref="Changed"/>.</summary>
    public static event Action? ProgressChanged;
    public static Mode State { get; private set; } = Mode.Idle;
    public static string? Message { get; private set; }
    public static Release? Found { get; private set; }
    public static List<Release>? Known { get; private set; }
    /// <summary>The version found is a Critical Update, or comes after one: it goes in whatever the settings say.</summary>
    public static bool Critical { get; private set; }
    /// <summary>How much of the installer has come, 0 to 1, while <see cref="Mode.Downloading"/>.</summary>
    public static double Progress { get; private set; }
    /// <summary>A version downloaded and ready, waiting to go in: what it waits for (null when nothing holds it back).</summary>
    public static Holding? Waiting { get; private set; }
    /// <summary>A version found, not downloaded by itself: this PC is on a metered connection (Download fetches it anyway).</summary>
    public static bool HeldForMetered { get; private set; }
    private static string? downloaded;

    /// <summary>Check for updates automatically: at launch and every hour (Settings ▸ Updates; on unless turned off).</summary>
    public static bool Automatic { get => Prefs.Bool("updates:auto", true); set { Prefs.Set("updates:auto", value); Changed?.Invoke(); if (value) CheckIfDue(); } }
    /// <summary>
    /// Where 0.3.6 and earlier kept Install updates automatically (0.3.7: an update always installs itself in a copy the
    /// installer put here). One turned off is let go of once, at the first start (<see cref="Settle"/>), so it installs
    /// again; nothing reads it any more.
    /// </summary>
    private const string OldInstallKey = "updates:install";

    private const string StayKey = "updates:staying";

    /// <summary>
    /// An earlier version put back (Earlier versions ▸ Install…) stays: the run of the version it went back from (0.3.7,
    /// in place of Install updates automatically turning off). That version and those before it are not put in by
    /// themselves — or the newest would go straight back in at the next quiet moment — while one newer than it is, as
    /// every update is, and a Critical Update whatever this says. Update now lets it go (<see cref="UpdateNow"/>), and so
    /// does this copy reaching that version another way (Simpl-Setup.exe run by hand). Null: Simpl stays on nothing. Kept
    /// with the other settings, encrypted as they are. (--update-test-staying: an update played through as a copy put
    /// back from the version it finds would have it, for Settings' picture.)
    /// </summary>
    public static int? StayingFrom
    {
        get
        {
            if (Testing && Launch.Flag("update-test-staying")) return CurrentRun + 1;
            var run = (int)Prefs.Double(StayKey);
            return run > 0 ? run : null;
        }
        private set { Prefs.Set(StayKey, value is { } run ? (double)run : null); Changed?.Invoke(); }
    }

    /// <summary>The version found is one Simpl stays back from (<see cref="StayingFrom"/>): not put in by itself, unless
    /// it is a Critical Update.</summary>
    public static bool KeptBack => StaysBack(Found, Critical, StayingFrom);
    /// <summary>Get beta updates (Settings ▸ Updates; off unless turned on): the betas are offered too, not only the usual
    /// releases (<see cref="Choose"/>). Turned off, the versions are read again — a beta found and not yet in is let go —
    /// and a copy on a beta stays on it until a usual release newer than it comes.</summary>
    public static bool Beta
    {
        get => Prefs.Bool("updates:beta", false);
        set { Prefs.Set("updates:beta", value); Changed?.Invoke(); if (!Off) _ = Check(); }
    }
    /// <summary>
    /// Updates paused (the Mac's Developer ▸ Pause updates; here --pause-updates on|off, with Simpl quit): the hourly look
    /// is made all the same, but nothing found installs by itself — except a Critical Update, which nothing holds back.
    /// </summary>
    public static bool Paused { get => Prefs.Bool("updates:paused"); set { Prefs.Set("updates:paused", value ? true : null); Changed?.Invoke(); } }

    public static DateTime? LastCheck
    {
        get => DateTime.TryParse(Prefs.String("updates:checked"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        private set => Prefs.Set("updates:checked", value?.ToString("o"));
    }

    public static bool IsDevelopmentRun => Launch.IsDevelopment;

    /// <summary>--update-test &lt;installer&gt; (a development run's): an update played through against a local file —
    /// the next version, found at once, its installer that file (README.md, Updates).</summary>
    private static string? TestFile => Launch.IsDevelopment ? Launch.Get("update-test") : null;
    public static bool Testing => TestFile != null;
    /// <summary>Nothing looked for, nothing installed: a development run, unless an update is played through in it.</summary>
    public static bool Off => IsDevelopmentRun && !Testing;

    /// <summary>This copy's version as its release names it ("0.2.0 (v001)"), and its run number (0 for a build of one's own).</summary>
    public static string Version => WhatsNew.Current;
    public static int CurrentRun => Regex.Match(Version, @"\(v(\d+)\)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    private static bool? installed;

    /// <summary>
    /// This copy was put here by Simpl-Setup.exe: its uninstall entry (the current user's — Simpl installs for one
    /// person) names the folder this Simpl.exe runs from. A copy unzipped elsewhere, or one copied out of that folder, is
    /// not. (--update-test-installed: an update played through as an installed copy would have it.)
    /// </summary>
    public static bool Installed => installed ??= (Testing && Launch.Flag("update-test-installed")) || FindInstalled();

    private static bool FindInstalled()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(EntryKey);
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

    /// <summary>The version found goes in by itself once it is here: a Critical Update always; another when updates are
    /// not paused and it is not one an earlier version put back stays back from (<see cref="KeptBack"/>).</summary>
    public static bool GoesInByItself => Critical || (!Paused && !KeptBack);

    // (the list of releases, read once an hour, comes compressed: a tenth of its size or less)
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
    // (the installer is tens of megabytes: a download has as long as it needs, so long as it keeps coming)
    private static readonly HttpClient Downloads = new() { Timeout = Timeout.InfiniteTimeSpan };

    // ---- the schedule ----

    private const double Hour = 60;
    private static bool started;
    private static DispatcherTimer? ticker, waiter;
    /// <summary>When a download that failed by itself is tried again, and how many have failed in a row.</summary>
    private static DateTime? retryAt;
    private static int failures;
    private static readonly double[] RetryMinutes = { 2, 10, 30, Hour };

    /// <summary>
    /// From the window's first showing: the versions read now when the last look is over an hour old (or never made, or
    /// the clock went back), and looked at again every ten minutes (and whenever Simpl comes to the front) for the hour
    /// being up — a PC asleep does not count its hours. An update waiting to go in is looked at every couple of seconds
    /// for what holds it back (much of it is only seen by looking), and as Simpl leaves the front.
    /// </summary>
    public static void Start()
    {
        if (started || Off || Application.Current is not { } app) return;
        started = true;
        // (Simpl just opened counts as Simpl in use: one in front is given its half minute before an update goes in —
        // not an update played through, whose pictures are taken sooner)
        if (!Testing) lastInput = Environment.TickCount64;
        InputManager.Current.PreProcessInput += OnInput;
        ticker = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        ticker.Tick += (_, _) => Tick();
        ticker.Start();
        waiter = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        waiter.Tick += (_, _) => Proceed();
        app.Activated += (_, _) => CheckIfDue();
        app.Deactivated += (_, _) => Proceed();
        Settle();
        if (Testing)
        {
            Log.Write($"update test: {TestFile}{(Installed ? ", as an installed copy" : "")}{(Launch.Flag("update-test-critical") ? ", a Critical Update" : "")}{(StayingFrom is { } from ? $", staying back from v{from:000}" : "")}");
            if (Launch.Get("update-test-hold") is { Length: > 0 } what) Hold(what, $"when you close the {what}");
            _ = Check();
            return;
        }
        CheckIfDue();
    }

    /// <summary>
    /// As the checks begin (0.3.7): Install updates automatically, turned off in 0.3.6 or earlier, let go of — an update
    /// always installs itself now; and an earlier version's stay (<see cref="StayingFrom"/>) let go of once this copy is
    /// the version it went back from, or newer (Simpl-Setup.exe run by hand, a Critical Update gone in).
    /// </summary>
    private static void Settle()
    {
        if (!Prefs.Bool(OldInstallKey, true)) Log.Write("update: Install updates automatically was off; updates install themselves now");
        Prefs.Remove(OldInstallKey);
        if (StayingFrom is { } from && CurrentRun >= from)
        {
            Log.Write($"update: on {Version}, so no longer staying back from v{from:000}");
            StayingFrom = null;
        }
    }

    private static void Tick()
    {
        // (a download that failed by itself — no network, cut off — tried again once its while is up; a Critical
        // Update's every ten minutes)
        if (State == Mode.Failed && Found != null && InstallsHere && retryAt is { } at && DateTime.UtcNow >= at && GoesInByItself)
        {
            Log.Write($"update: trying the download again ({failures} failed)");
            _ = Download(asked: false);
        }
        CheckIfDue();
        Proceed();
    }

    /// <summary>The versions read when the last look is an hour old (or was never made, or the clock went back).</summary>
    public static void CheckIfDue()
    {
        if (!started || Testing || !Automatic) return;
        if (LastCheck is { } last)
        {
            var since = (DateTime.UtcNow - last.ToUniversalTime()).TotalMinutes;
            if (since >= 0 && since < Hour) return;
        }
        _ = Check();
    }

    // ---- checking ----

    private static bool checking;

    /// <summary>Check for updates (and the hourly look): the versions read afresh; a newer one found is fetched and put in
    /// by itself when it may be. A check, a download or an install under way is left alone; an update downloaded and
    /// waiting stays as it is while the versions are read again (a newer one since takes its place), and so does one
    /// found when the versions cannot be read this time.</summary>
    public static async Task Check()
    {
        if (Off || checking || State is Mode.Downloading or Mode.Installing) return;
        checking = true;
        var was = State;
        if (was != Mode.Ready)
        {
            State = Mode.Checking;
            Message = null;
            Changed?.Invoke();
        }
        HeldForMetered = false;
        try
        {
            var list = await Releases();
            // (an update that began going in meanwhile is left to finish)
            if (restarting) { checking = false; return; }
            // (the newest this copy may take: a beta only with Get beta updates on)
            var (newest, critical) = Choose(list, CurrentRun, Beta);
            if (newest != null)
            {
                // (the same version downloaded already: ready to go in as it was)
                var ready = Found?.Tag == newest.Tag && downloaded != null && File.Exists(downloaded);
                if (!ready) downloaded = null;
                Found = newest;
                // (a Critical Update newer than this copy, of those it may take: the newest goes in, whatever the settings)
                Critical = critical;
                State = ready ? Mode.Ready : Mode.Available;
                // (one an earlier version put back stays back from says so: it waits for Update now)
                if (was is not (Mode.Available or Mode.Ready))
                    Log.Write($"update: {newest.Version} found{(Critical ? " (a Critical Update)" : "")}{(KeptBack ? $", kept back: staying back from v{StayingFrom:000}" : "")}");
            }
            else { Found = null; Critical = false; downloaded = null; State = Mode.UpToDate; }
        }
        catch (Exception e) when (restarting)
        {
            Log.Write($"update: could not check ({e.Message})");
            checking = false;
            return;
        }
        catch (Exception e)
        {
            if (was is Mode.Available or Mode.Ready && Found != null)
            {
                State = was == Mode.Ready && downloaded != null && File.Exists(downloaded) ? Mode.Ready : Mode.Available;
                Log.Write($"update: could not check ({e.Message}); {Found.Version} stays as it was");
            }
            else
            {
                State = Mode.Failed;
                Message = "Could not check: " + e.Message;
            }
        }
        checking = false;
        LastCheck = DateTime.UtcNow;
        Changed?.Invoke();
        Proceed();
    }

    /// <summary>The Windows versions published, newest first (an update played through: the one it plays, no network).
    /// The first hundred releases GitHub lists, the most it gives at once: the Mac's, the extension's and the Windows
    /// betas share the list, a Mac beta every few minutes on a busy day, in GitHub's own order — not quite the order of
    /// publishing — so the forty 0.3.0 and earlier read could leave a usual release out.</summary>
    public static async Task<List<Release>> Releases()
    {
        if (Testing)
        {
            Known = TestReleases();
            Changed?.Invoke();
            return Known;
        }
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/26VirenS/BetterCourseViewer/releases?per_page=100");
        req.Headers.TryAddWithoutValidation("User-Agent", "Simpl-for-Windows");
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw new Exception($"GitHub answered {(int)res.StatusCode}.");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var list = Parse(doc.RootElement);
        Known = list;
        Changed?.Invoke();
        return list;
    }

    /// <summary>The version an update played through finds: the next run, its installer the file given (its SHA-256
    /// that file's, or --update-test-sha's to try a mismatch), a Critical Update with --update-test-critical.</summary>
    private static List<Release> TestReleases()
    {
        var file = Path.GetFullPath(TestFile!);
        var info = new FileInfo(file);
        var run = CurrentRun + 1;
        string sha;
        if (Launch.Get("update-test-sha") is { Length: > 0 } given) sha = given;
        else using (var f = File.OpenRead(file)) sha = Convert.ToHexString(SHA256.HashData(f));
        var notes = "An update played through (--update-test)." + (Launch.Flag("update-test-critical") ? "\n" + CriticalMark : "");
        var page = "https://github.com/26VirenS/BetterCourseViewer/releases";
        var list = new List<Release>
        {
            new($"windows-fluent-v{run:000}", run, $"v{run:000}", false, DateTime.UtcNow, info.Length, page, notes, new Asset(file, info.Length, sha)),
        };
        // (--update-test-earlier: this version and one before it as well, so Settings ▸ Updates ▸ Earlier versions has
        //  rows to show — their dates, sizes and Install…)
        if (Launch.Flag("update-test-earlier"))
            for (var back = 0; back <= 1 && CurrentRun - back > 0; back++)
            {
                var r = CurrentRun - back;
                list.Add(new($"windows-fluent-v{r:000}", r, $"v{r:000}", false, DateTime.UtcNow.AddDays(-3 - 4 * back), info.Length, page,
                    "An earlier version played through (--update-test-earlier).", new Asset(file, info.Length, sha)));
            }
        return list;
    }

    /// <summary>A version's page, in the browser: its download is there.</summary>
    public static void Open(Release r)
    {
        if (Uri.TryCreate(r.Url, UriKind.Absolute, out var u)) Engine.Engine.OpenExternally(u);
    }

    // ---- downloading ----

    /// <summary>Where a version's installer is downloaded to: a folder of its own under Windows' temporary files (one
    /// played through: under the profile's, so copies run at once keep apart).</summary>
    private static string FolderFor(Release r) => Path.Combine(Testing ? Path.Combine(AppPaths.Root, "Update") : Path.Combine(Path.GetTempPath(), "Simpl-Update"), r.Tag);

    /// <summary>
    /// Download, for the version found (pressed, or by itself): its installer fetched into its folder — picked up where
    /// an earlier try stopped, when the server lets it — then checked (<see cref="Verify"/>) and ready to go in, for a
    /// copy the installer put here. Any other copy, asked: the version's page, as before. One downloaded already and
    /// still there is checked and taken as it is.
    /// </summary>
    public static async Task Download(bool asked = true)
    {
        if (Found is not { } r || State is Mode.Downloading or Mode.Checking or Mode.Installing) return;
        if (!InstallsHere || r.Setup is not { } setup) { if (asked) Open(r); return; }
        State = Mode.Downloading;
        HeldForMetered = false;
        Progress = 0;
        Message = null;
        Changed?.Invoke();
        try
        {
            var dir = FolderFor(r);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, SetupName);
            var part = file + ".part";
            if (File.Exists(file))
            {
                // (kept from before — Simpl quit before it went in): checked, and taken if it holds
                try { await Task.Run(() => Verify(file, setup)); }
                catch (Exception e)
                {
                    Log.Write($"update: the installer kept from before is no good ({e.Message}): downloading it again");
                    File.Delete(file);
                }
            }
            if (!File.Exists(file))
            {
                await Fetch(setup, part);
                await Task.Run(() => Verify(part, setup));
                File.Move(part, file, true);
            }
            downloaded = file;
            Progress = 1;
            State = Mode.Ready;
            failures = 0;
            retryAt = null;
            Log.Write($"update: {r.Version}'s installer downloaded and checked");
        }
        catch (Exception e)
        {
            State = Mode.Failed;
            var why = e is OperationCanceledException ? "nothing came for a minute." : e.Message;
            Message = "Could not download: " + why;
            failures++;
            // (tried again by itself a while on: sooner at first, then once an hour; a Critical Update every ten minutes)
            retryAt = DateTime.UtcNow.AddMinutes(Critical ? 10 : RetryMinutes[Math.Min(failures, RetryMinutes.Length) - 1]);
            Log.Write($"update: could not download {r.Version}: {why}");
        }
        Changed?.Invoke();
        Proceed();
    }

    /// <summary>
    /// An earlier version put in this one's place (Settings ▸ Updates ▸ Earlier versions ▸ Install…, the Mac's): for the
    /// copy the installer put here, that version's installer downloaded and checked as an update's is, then run as one
    /// is — Simpl closes and opens again on it. It stays on it first (<see cref="StayingFrom"/>: this version, or the one
    /// an earlier stay went back from when that is newer), or this version would go back in at the next quiet moment (a
    /// Critical Update still does) — and meanwhile nothing else is fetched or put in by itself. Null once it is going in,
    /// else why not — and then all is as it was: the stay as it was, the versions read again for what is really newer.
    /// </summary>
    public static async Task<string?> InstallEarlier(Release r)
    {
        if (Off || !Installed || r.Setup == null || r.Run >= CurrentRun) return "This copy can’t put that version in its place: its page has the download.";
        if (checking || restarting || State is Mode.Checking or Mode.Downloading or Mode.Installing) return "Simpl is busy with an update. Try again in a moment.";
        var wasStaying = StayingFrom;
        var wasFailures = failures;
        // (0.3.7: in place of turning Install updates automatically off — kept with the settings before the installer runs)
        StayingFrom = Math.Max(wasStaying ?? 0, CurrentRun);
        Found = r;
        Critical = false;
        downloaded = null;
        State = Mode.Available;
        Message = null;
        Log.Write($"update: {r.Version} asked for, in this version's place");
        Changed?.Invoke();
        await Download();
        if (State == Mode.Ready && Found == r)
        {
            InstallAndRestart();
            // (the installer could not be started — the card says why: this version stays, and so does the stay as it was)
            if (State == Mode.Failed) StayingFrom = wasStaying;
            return null;
        }
        var why = Message ?? $"Could not download {r.Version}.";
        Log.Write($"update: {r.Version} not put in ({why})");
        StayingFrom = wasStaying;
        Found = null;
        downloaded = null;
        State = Mode.Idle;
        Message = null;
        retryAt = null;
        failures = wasFailures;
        Changed?.Invoke();
        _ = Check();
        return why;
    }

    /// <summary>The installer fetched into <paramref name="part"/>, after what an earlier try left there when the server
    /// sends the rest (206); from the start otherwise. A minute with nothing coming gives up.</summary>
    private static async Task Fetch(Asset setup, string part)
    {
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (setup.Size > 0 && have >= setup.Size) { File.Delete(part); have = 0; }
        // (a minute with nothing coming — no answer at all, or the download stopping partway: given up, to be tried again;
        // the minute starts over with each piece that comes)
        using var stalled = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        Stream from;
        HttpResponseMessage? res = null;
        long total;
        if (File.Exists(setup.Url))
        {
            // (an update played through: its local file, read as a download would come)
            var local = File.OpenRead(setup.Url);
            local.Seek(have, SeekOrigin.Begin);
            from = local;
            total = local.Length;
        }
        else
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, setup.Url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Simpl-for-Windows");
            if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
            res = await Downloads.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stalled.Token);
            if (res.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // (what was kept is not a beginning of this file: from the start, next time)
                res.Dispose();
                File.Delete(part);
                throw new Exception("the download had to start over.");
            }
            if (!res.IsSuccessStatusCode) { var code = (int)res.StatusCode; res.Dispose(); throw new Exception($"GitHub answered {code}."); }
            if (res.StatusCode != HttpStatusCode.PartialContent) have = 0; // (the whole file sent: from the start)
            total = res.Content.Headers.ContentLength is { } len ? len + have : setup.Size;
            from = await res.Content.ReadAsStreamAsync(stalled.Token);
        }
        if (have > 0) Log.Write($"update: picking the download up at {have / 1048576.0:0.0} MB");
        try
        {
            await using var to = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[1 << 16];
            var got = have;
            var told = 0.0;
            while (true)
            {
                stalled.CancelAfter(TimeSpan.FromMinutes(1));
                var n = await from.ReadAsync(buffer, stalled.Token);
                if (n <= 0) break;
                await to.WriteAsync(buffer.AsMemory(0, n));
                got += n;
                if (total > 0 && (double)got / total - told >= 0.01)
                {
                    told = Progress = Math.Min(1, (double)got / total);
                    ProgressChanged?.Invoke();
                }
            }
        }
        finally
        {
            await from.DisposeAsync();
            res?.Dispose();
        }
    }

    /// <summary>
    /// The installer downloaded checked before it may run: the size published; a program (not a web page in its place);
    /// the SHA-256 GitHub publishes, when it gives one; and — when this copy is signed — a signature Windows trusts, by
    /// this copy's own publisher. Throws, saying which failed.
    /// </summary>
    private static void Verify(string file, Asset setup)
    {
        var info = new FileInfo(file);
        if (setup.Size > 0 && info.Length != setup.Size) throw new Exception("the download stopped short.");
        using (var f = File.OpenRead(file))
        {
            if (f.ReadByte() != 'M' || f.ReadByte() != 'Z') throw new Exception("the download is not the installer (a web page in its place, most likely).");
            f.Position = 0;
            if (setup.Sha256 is { Length: > 0 } want && !Convert.ToHexString(SHA256.HashData(f)).Equals(want.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new Exception("the download is not the file that was published.");
        }
        if (OwnPublisher is { } mine)
        {
            var theirs = Publisher(file);
            if (theirs == null) throw new Exception("the download isn’t signed, so it wasn’t installed.");
            if (!string.Equals(theirs, mine, StringComparison.Ordinal)) throw new Exception("the download isn’t signed by Simpl’s publisher, so it wasn’t installed.");
        }
    }

    private static string? ownPublisher;
    private static bool ownRead;

    /// <summary>Who signed this copy (its certificate's subject), or null for a copy that is not signed: then an update
    /// is held to its checksum alone, as before signing was set up.</summary>
    private static string? OwnPublisher
    {
        get
        {
            if (ownRead) return ownPublisher;
            ownRead = true;
            try
            {
                var exe = Environment.ProcessPath ?? Path.Combine(AppPaths.AppDir, "Simpl.exe");
                using var cert = X509Certificate.CreateFromSignedFile(exe);
                ownPublisher = cert.Subject;
                Log.Write("update: this copy is signed by " + ownPublisher);
            }
            catch { ownPublisher = null; }
            return ownPublisher;
        }
    }

    /// <summary>Who signed a file, when its signature holds as Windows checks it (signed, unchanged since, by a
    /// certificate Windows trusts — revocation not looked up, so nothing is asked of anyone over the network); null when
    /// it is not signed or does not hold.</summary>
    private static string? Publisher(string file)
    {
        try
        {
            if (!Trust.Holds(file)) return null;
            using var cert = X509Certificate.CreateFromSignedFile(file);
            return cert.Subject;
        }
        catch { return null; }
    }

    /// <summary>WinVerifyTrust, as Windows checks a program's signature.</summary>
    private static class Trust
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileData
        {
            public uint Size;
            public string Path;
            public IntPtr Handle;
            public IntPtr Subject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Data
        {
            public uint Size;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr File;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProvFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref Data data);

        private const uint UiNone = 2, RevokeNone = 0, ChoiceFile = 1, StateIgnore = 0, RevocationCheckNone = 0x10, CacheOnly = 0x1000;

        public static bool Holds(string file)
        {
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // (WINTRUST_ACTION_GENERIC_VERIFY_V2)
            var info = new FileData { Size = (uint)Marshal.SizeOf<FileData>(), Path = file };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<FileData>());
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                var data = new Data
                {
                    Size = (uint)Marshal.SizeOf<Data>(), UiChoice = UiNone, RevocationChecks = RevokeNone, UnionChoice = ChoiceFile,
                    File = ptr, StateAction = StateIgnore, ProvFlags = RevocationCheckNone | CacheOnly,
                };
                return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure<FileData>(ptr);
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    /// <summary>This PC is on a metered connection (a phone's hotspot, a capped plan), as Windows says: a download that
    /// comes by itself waits for another (Windows Update does the same).</summary>
    private static bool Metered
    {
        get
        {
            try
            {
                var cost = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost();
                return cost != null && (cost.NetworkCostType is Windows.Networking.Connectivity.NetworkCostType.Fixed or Windows.Networking.Connectivity.NetworkCostType.Variable
                    || cost.Roaming || cost.OverDataLimit);
            }
            catch { return false; }
        }
    }

    // ---- what holds an update back ----

    private static readonly List<Holding> holds = new();
    private static long lastInput;

    /// <summary>
    /// What the window has open that an update waits for, looked at when asked (UpdateWatch's, Shell/UpdateWatch.cs): a
    /// dialog over it, the sign-in, the tour, a window of a page of the school's, a menu. Null: nothing.
    /// </summary>
    public static Func<Holding?>? Watch { get; set; }

    /// <summary>Something open that an update must not cut off (the Mac's UpdateHold): held from now until the holding
    /// is disposed — the quiz, an Alta or a Macmillan assignment, a page of the school's in the window.</summary>
    public static Holding Hold(string what, string? when)
    {
        var h = new Holding(what, when);
        holds.Add(h);
        return h;
    }

    /// <summary>Something by this name holds updates now (the quiz, Knewton Alta, Macmillan Learning): the live check
    /// waits for it too (Shell/LiveCheck.cs).</summary>
    public static bool Holds(string what) => holds.Any(h => h.What == what);

    /// <summary>One closed: with none left, a waiting update may go in (a moment on, once what closed has gone).</summary>
    private static void Let(Holding h)
    {
        if (!holds.Remove(h) || !started || State != Mode.Ready) return;
        waiter?.Stop();
        waiter?.Start();
    }

    /// <summary>
    /// What holds an update back now, the first of: what was held (<see cref="Hold"/>: a quiz, an Alta or a Macmillan
    /// assignment, a page of the school's in the window); what the window has open (<see cref="Watch"/>); a Windows
    /// dialog over Simpl (opening or saving a file). Null: nothing.
    /// </summary>
    public static Holding? HeldBy()
    {
        if (holds.Count > 0) return holds[0];
        if (Watch?.Invoke() is { } w) return w;
        if (ComponentDispatcher.IsThreadModal) return new Holding("a Windows dialog", "when you close the dialog");
        return null;
    }

    /// <summary>A key, a click, a scroll or a touch in any of Simpl's windows.</summary>
    private static void OnInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is KeyEventArgs or TextCompositionEventArgs or MouseButtonEventArgs or MouseWheelEventArgs or TouchEventArgs or StylusDownEventArgs)
            lastInput = Environment.TickCount64;
    }

    /// <summary>Seconds since the student last pressed, clicked, scrolled or touched anything in Simpl.</summary>
    private static double Idle => lastInput == 0 ? double.MaxValue : (Environment.TickCount64 - lastInput) / 1000.0;

    /// <summary>The quiet an update waits for, besides what is open: with Simpl in front, half a minute without a key or
    /// a click (the student may be reading); behind other windows, a moment (a click that just sent it there). A Critical
    /// Update: a moment either way.</summary>
    private static double QuietNeeded => !Critical && Application.Current?.Windows.OfType<Window>().Any(w => w.IsActive) == true ? 30 : 5;

    // ---- going in ----

    /// <summary>The main window shows (the notice is seen on it; the moment before an update goes in is given only then).</summary>
    public static Func<bool>? WindowShown { get; set; }
    /// <summary>Where the window is now (UpdateWatch's): kept as Simpl quits for an update, so it opens again there.</summary>
    public static Func<Resume?>? Where { get; set; }

    private static bool restarting;

    /// <summary>
    /// What follows from where things stand (the Mac's installIfQuiet): a version found, here to be installed, is
    /// fetched by itself when it may go in by itself (on a metered connection, only a Critical Update); one downloaded
    /// goes in when nothing holds it back and Simpl has been quiet a moment — else it waits, and is looked at again
    /// every couple of seconds.
    /// </summary>
    public static void Proceed()
    {
        if (!started || restarting) return;
        // (a beta found, or still downloading, as Get beta updates was turned off: let go, not fetched or put in by
        //  itself, and the versions read again. Not one asked for in Earlier versions, which nothing puts in by itself)
        if (Found is { Beta: true } r && !Beta && GoesInByItself && State is Mode.Available or Mode.Ready or Mode.Failed)
        {
            Log.Write($"update: {r.Version} is a beta, and Get beta updates is off: let go");
            Found = null;
            Critical = false;
            downloaded = null;
            retryAt = null;
            State = Mode.Idle;
            Message = null;
            Changed?.Invoke();
            _ = Check();
            return;
        }
        if (State == Mode.Available && InstallsHere && GoesInByItself)
        {
            if (!Critical && Metered)
            {
                if (!HeldForMetered) { HeldForMetered = true; Log.Write("update: waiting for a connection that isn’t metered"); Changed?.Invoke(); }
                return;
            }
            _ = Download(asked: false);
            return;
        }
        if (State != Mode.Ready || !GoesInByItself)
        {
            waiter?.Stop();
            SetWaiting(null);
            return;
        }
        if (HeldBy() is { } hold)
        {
            SetWaiting(hold);
            waiter?.Start();
            return;
        }
        SetWaiting(null);
        if (Idle < QuietNeeded)
        {
            waiter?.Start();
            return;
        }
        waiter?.Stop();
        _ = GoIn();
    }

    private static void SetWaiting(Holding? hold)
    {
        if (hold?.What == Waiting?.What && hold?.When == Waiting?.When) { Waiting = hold; return; }
        if (hold != null) Log.Write($"update: held ({hold.What})");
        else if (Waiting != null) Log.Write($"update: no longer held (by {Waiting.What})");
        Waiting = hold;
        Changed?.Invoke();
    }

    /// <summary>Going in: the notice over the window says so for a moment (when it shows), then Simpl quits and the
    /// installer runs. A key or a click meanwhile, or something opened, and it waits again.</summary>
    private static async Task GoIn()
    {
        restarting = true;
        State = Mode.Installing;
        Changed?.Invoke();
        var at = Environment.TickCount64;
        if (WindowShown?.Invoke() == true) await Task.Delay(2500);
        if (lastInput > at || HeldBy() is not null)
        {
            Log.Write("update: not now (Simpl was used meanwhile)");
            restarting = false;
            State = Mode.Ready;
            Changed?.Invoke();
            Proceed();
            return;
        }
        InstallNow();
    }

    /// <summary>
    /// Restart to Update (pressed): the version downloaded goes in now, whatever is open — the student asked (the Mac's
    /// Update Now).
    /// </summary>
    public static void InstallAndRestart()
    {
        if (State != Mode.Ready || restarting) return;
        restarting = true;
        State = Mode.Installing;
        Changed?.Invoke();
        InstallNow();
    }

    /// <summary>
    /// Update now (Settings ▸ Updates, while Simpl stays on an earlier version put back, <see cref="StayingFrom"/>): the
    /// version found downloaded and checked as an update's is, the stay let go of — the student chose to update, so it
    /// is let go of even when the download fails, and that is tried again as any update's is — and the version put in at
    /// once, as Restart to Update does. Kept back until the download is done, so nothing else starts it meanwhile.
    /// </summary>
    public static async Task UpdateNow()
    {
        if (restarting) return;
        if (Found != null && State is Mode.Available or Mode.Failed) await Download();
        if (StayingFrom is { } from)
        {
            Log.Write($"update: Update now: no longer staying back from v{from:000}");
            StayingFrom = null;
        }
        if (State == Mode.Ready) InstallAndRestart();
        else Proceed();
    }

    /// <summary>
    /// The installer started quietly (it waits for Simpl to have quit, puts the new version in its place and opens it
    /// again), where the window is noted first, and Simpl quits as Quit Simpl does — what it keeps written out as it
    /// goes — so nothing of it holds the files. An update played through for a picture stops short of it.
    /// </summary>
    private static void InstallNow()
    {
        if (downloaded is not { } file || !File.Exists(file))
        {
            // (the installer downloaded is gone — Windows' Storage Sense or Disk Cleanup empties its temporary files):
            // downloaded again, by itself when it may be, else by Download
            Log.Write("update: the downloaded installer is gone; Download again");
            downloaded = null;
            restarting = false;
            State = Mode.Available;
            Message = null;
            Changed?.Invoke();
            Proceed();
            return;
        }
        Remember();
        if (Testing && Shot.Asked)
        {
            Log.Write("update: installing — a picture: not installed");
            return;
        }
        try
        {
            Prefs.Flush();
            // (through the shell, so it inherits nothing of Simpl's — no handle of it keeps Simpl "open" for the installer)
            var dir = Path.GetDirectoryName(file)!;
            var log = Path.Combine(dir, "Setup.log");
            Process.Start(new ProcessStartInfo(file, string.Join(' ', SilentArguments) + $" \"/LOG={log}\"") { UseShellExecute = true, WorkingDirectory = dir });
        }
        catch (Exception e)
        {
            Prefs.Remove(ResumeKey);
            restarting = false;
            State = Mode.Failed;
            Message = "Could not start the update: " + e.Message;
            Log.Write("update: " + Message);
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
            // (each in a folder named by its version's tag, a beta's too)
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                if (Read(Path.GetFileName(d), "") is not { } v || v.Run > CurrentRun) continue;
                try { Directory.Delete(d, true); } catch { /* (its installer still running: the next start) */ }
            }
        }
        catch (Exception e) { Log.Write("update: tidying: " + e.Message); }
    }

    // ---- where the window was ----

    /// <summary>
    /// Where the window was as Simpl quit for an update, so the new version opens there: its place and what was opened
    /// on it (Engine's Place.Name and routes), and how the window stood — "front" (in use: opened in front), "behind"
    /// (behind other windows, or minimized: opened minimized, taking nothing from what is in front) or "hidden" (in the
    /// notification area: opened there, out of sight).
    /// </summary>
    public sealed class Resume
    {
        public string? To { get; set; }
        public DateTime At { get; set; }
        public string? Place { get; set; }
        public List<string[]>? Path { get; set; }
        public string Window { get; set; } = "front";
    }

    private const string ResumeKey = "updates:resume";

    /// <summary>Opened again by an update, within half an hour of it: where the window was (read once, as Simpl starts).</summary>
    public static Resume? Resumed { get; private set; }

    /// <summary>The window opens out of sight (minimized, or in the notification area): it takes nothing from what is in
    /// front, and is not maximized as it opens (Placement).</summary>
    public static bool ReopensOutOfSight => Resumed?.Window is "behind" or "hidden";

    private static void Remember()
    {
        var where = Where?.Invoke() ?? new Resume();
        where.To = Found?.Version;
        where.At = DateTime.UtcNow;
        Prefs.Set(ResumeKey, JsonSerializer.Serialize(where));
        Log.Write($"update: where it was: {where.Place ?? "(no place)"}{(where.Path is { Count: > 0 } p ? $" with {p.Count} opened on it" : "")}, the window {where.Window}");
    }

    /// <summary>As Simpl starts: where an update left the window, when one did a moment ago — taken, so it is used once.</summary>
    public static void ReadResume()
    {
        if (Prefs.String(ResumeKey) is not { } text) return;
        Prefs.Remove(ResumeKey);
        try
        {
            if (JsonSerializer.Deserialize<Resume>(text) is not { } r) return;
            var since = (DateTime.UtcNow - r.At.ToUniversalTime()).TotalMinutes;
            if (since < 0 || since > 30) return;
            Resumed = r;
            Log.Write($"update: back after updating to {r.To} ({Version}), the window {r.Window}, at {r.Place ?? "(no place)"}");
        }
        catch (Exception e) { Log.Write("update: where it was could not be read: " + e.Message); }
    }

    /// <summary>The place and what was opened on it, as the update left them (the engine's launch place; once).</summary>
    public static (string? Place, List<Engine.Route> Path)? TakeResumedPlace()
    {
        if (Resumed is not { } r || r.Place == null) return null;
        var place = r.Place;
        r.Place = null;
        var path = new List<Engine.Route>();
        foreach (var p in r.Path ?? new())
        {
            // (a tool open on the Tools place, as 0.3.0 kept one: the Tools page, the tool not opened again)
            if (p.Length < 1 || !Enum.TryParse<Engine.RouteKind>(p[0], out var kind) || kind == Engine.RouteKind.Tool) break;
            path.Add(new Engine.Route(kind, p.ElementAtOrDefault(1) ?? "", p.ElementAtOrDefault(2) ?? "", p.ElementAtOrDefault(3) ?? ""));
        }
        return (place, path);
    }

    /// <summary>A place and what is opened on it, as kept for <see cref="Resume"/>: a page's address (a screen of the
    /// school's own) and what is under it are left out — the place is opened as far as before it.</summary>
    public static List<string[]> Kept(IEnumerable<Engine.Route> path) =>
        path.TakeWhile(r => r.Kind != Engine.RouteKind.Web).Select(r => new[] { r.Kind.ToString(), r.A, r.B, r.C }).ToList();
}

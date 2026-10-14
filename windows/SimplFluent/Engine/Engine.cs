using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Models;
using Simpl.Support;

namespace Simpl.Engine;

public enum Phase { Starting, Web, Native }

/// <summary>How the window last moved: a place picked, a screen pushed, Back, Forward, a place's root, the steps under a
/// screen set, or the place taken over in place (a search typed further).</summary>
public enum NavMove { Go, Push, Back, Forward, Root, Path, Replace }

public sealed class EngineException : Exception
{
    public EngineException(string message) : base(message) { }
}

/// <summary>
/// The school's site in a web view as the app's engine — the Mac's Engine, on WebView2: one web view with the
/// extension in it, signed in to the school, under the window's own screens and answering what they show
/// (<see cref="Call{T}"/>). It is what the window shows only while signing in; once the page says it is ready
/// (shell.state), the sidebar, the toolbar and the native screens go over it. Where the window is (a place in the
/// sidebar, what is pushed on it, Back and Forward) lives here too, so a press anywhere can open its screen.
/// </summary>
public sealed partial class Engine : Observable, IShellListener
{
    public string Host { get; }
    public Uri BaseUrl { get; }
    /// <summary>The sign-in: what covers the web view while the school asks for a username and password, and the details kept (the Mac's LoginAssist).</summary>
    public LoginAssist Login { get; }
    /// <summary>A page is on its way in the main frame.</summary>
    public bool Loading { get; private set; }
    /// <summary>The window's bottom layer: the web view, under everything.</summary>
    public WebView2 Web { get; } = new() { DefaultBackgroundColor = System.Drawing.Color.Transparent };
    public AnswerCache Answers { get; }

    private Phase phase = Phase.Starting;
    public Phase Phase { get => phase; private set { if (Set(ref phase, value)) PhaseChanged(); } }

    public Navigator Nav { get; } = new();
    private int navVersion;
    /// <summary>Bumps whenever the window moves (a place, a push, Back, Forward).</summary>
    public int NavVersion { get => navVersion; private set => Set(ref navVersion, value); }
    /// <summary>How it last moved (set before <see cref="NavVersion"/> says it did): the shell's page comes in to match.</summary>
    public NavMove LastMove { get; private set; }

    private int callsInFlight;
    /// <summary>Calls into the page not answered yet, and when the last one began or ended (Environment.TickCount64): the
    /// prefetch keeps out of the way of the screens' own.</summary>
    public int CallsInFlight => callsInFlight;
    public long LastCallAt { get; private set; }

    private Snapshot? snapshot;
    public Snapshot? Snapshot { get => snapshot; private set => Set(ref snapshot, value); }
    private string? lms;
    /// <summary>The school's site as the page says it: "canvas" or "d2l" (Brightspace).</summary>
    public string? Lms { get => lms; private set { if (Set(ref lms, value)) { Changed(nameof(OnBrightspace)); Changed(nameof(LmsName)); } } }
    private int dataVersion;
    /// <summary>Bumps when what the screens show may have changed (a tick, a hand-in, a refresh): they read again.</summary>
    public int DataVersion { get => dataVersion; private set => Set(ref dataVersion, value); }
    private bool countsLive, coursesLive;
    /// <summary>The counts are the school's word now, not what was kept from before: until they are, they are not shown.</summary>
    public bool CountsLive { get => countsLive; private set => Set(ref countsLive, value); }
    public bool CoursesLive { get => coursesLive; private set => Set(ref coursesLive, value); }
    private List<CourseRow> courses = new();
    public List<CourseRow> Courses { get => courses; private set => Set(ref courses, value); }
    private List<GroupRow> groups = new();
    public List<GroupRow> Groups { get => groups; private set => Set(ref groups, value); }
    private Dictionary<string, List<SectionLink>> sections = new();
    /// <summary>A course's sections as its home says (its own tabs), once looked at.</summary>
    public Dictionary<string, List<SectionLink>> Sections { get => sections; private set => Set(ref sections, value); }
    private string query = "";
    /// <summary>The toolbar's search field.</summary>
    public string Query { get => query; set => Set(ref query, value); }
    private bool refreshing;
    public bool Refreshing { get => refreshing; private set => Set(ref refreshing, value); }

    /// <summary>What a press on a piece of work asked for, for the screen it opens: Hand In, or the feedback.</summary>
    public (string Id, bool Feedback)? Arrival { get; set; }

    // the window's popups: whoever shows the window shows them (MainWindow)
    public event Action<QuizLaunch>? QuizRequested;
    public event Action<AltaLaunch>? AltaRequested;
    public event Action<AltaLaunch>? MacmillanRequested;
    public event Action<ToolLaunch>? ToolRequested;
    public event Action? SetupRequested;
    public event Action<WhatsNewData>? WhatsNewRequested;
    /// <summary>Settings asked for (the page's "open options", the account menu's Settings…, --open settings).</summary>
    public event Action? SettingsRequested;
    public event Action? NewTaskRequested;
    public event Action? NewMessageRequested;

    private bool ready;
    private bool shownNative;
    private bool openedOnKept;
    private bool launchPlaceTaken;
    private bool started;
    private readonly Dictionary<long, TaskCompletionSource<(bool Ok, string Text)>> calls = new();
    private long nextCall;
    /// <summary>Reads asked and not answered yet, by the file their answer is kept in: the same read asked meanwhile waits for that answer.</summary>
    private readonly Dictionary<string, TaskCompletionSource<string>> reading = new();
    private readonly HashSet<string> sectionsRead = new();

    public Engine(string host)
    {
        Host = host;
        var dev = Launch.DevBaseUrl;
        BaseUrl = dev != null && string.Equals(dev.Host, host, StringComparison.OrdinalIgnoreCase) ? dev : new Uri($"https://{host}/");
        Answers = new AnswerCache(BaseUrl.IsDefaultPort ? BaseUrl.Host : $"{BaseUrl.Host}_{BaseUrl.Port}");
        Bridge.Shared.Shell = this;
        Bridge.Shared.SaveAtExit();
        Login = new LoginAssist(BaseUrl.Host, this);
        Login.OnRestart = () => Web.CoreWebView2?.Navigate(new Uri(BaseUrl, "/").ToString()); // (Start Over: Canvas afresh, which asks the school anew)
        Login.PropertyChanged += (_, e) => Changed("Login." + e.PropertyName);
    }

    // MARK: - Starting

    /// <summary>The web views' browser, as Simpl runs it: media may play without a press; a page out of sight (the
    /// engine's, under the app's screens) keeps its timers and its pace. A development run may add more
    /// (--browser-args, ';' between them), their features merged with these.</summary>
    public static string BrowserArguments { get; } = MergeArguments(
        "--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --disable-features=IntensiveWakeUpThrottling,CalculateNativeWinOcclusion" +
        // (the network service in the browser's own process, not a process of its own: measured, 12 MB less committed
        //  and 35 MB less in RAM, the calls no slower)
        " --enable-features=NetworkServiceInProcess2",
        Launch.IsDevelopment ? Launch.Get("browser-args")?.Replace(';', ' ') : null);

    private static Task<CoreWebView2Environment>? environment;

    /// <summary>
    /// The one browser every web view of Simpl's runs in (the engine, the school's pages, the tools, the previews): one
    /// environment, made once, on Simpl's web data with <see cref="BrowserArguments"/> — so all share one browser
    /// process, one GPU process and one network service, made once.
    /// </summary>
    public static Task<CoreWebView2Environment> WebEnvironment()
    {
        if (environment is { IsFaulted: false, IsCanceled: false } e) return e;
        return environment = CoreWebView2Environment.CreateAsync(null, AppPaths.WebData, new CoreWebView2EnvironmentOptions(BrowserArguments));
    }

    /// <summary>Two sets of browser arguments as one: Chromium takes only the last --enable-features and the last
    /// --disable-features, so each is made one list.</summary>
    internal static string MergeArguments(string first, string? more)
    {
        if (string.IsNullOrWhiteSpace(more)) return first;
        var plain = new List<string>();
        var enable = new List<string>();
        var disable = new List<string>();
        foreach (var a in (first + " " + more).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (a.StartsWith("--enable-features=", StringComparison.Ordinal)) enable.AddRange(a["--enable-features=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries));
            else if (a.StartsWith("--disable-features=", StringComparison.Ordinal)) disable.AddRange(a["--disable-features=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries));
            else if (!plain.Contains(a)) plain.Add(a);
        }
        if (enable.Count > 0) plain.Add("--enable-features=" + string.Join(',', enable.Distinct()));
        if (disable.Count > 0) plain.Add("--disable-features=" + string.Join(',', disable.Distinct()));
        return string.Join(' ', plain);
    }

    public async void Start()
    {
        if (started) return;
        started = true;
        // (the extension's scripts read and put together meanwhile, off the window's thread)
        var bundle = Task.Run(() => ScriptBundle.Scripts(Host, shellOn: true));
        // signed in here before: the app's own screens at once, while the web view starts (what they read waits for the
        // page, as it would)
        OpenOnKept();
        try
        {
            // (the page runs as briskly with the app's own screens over it as when it is shown: nothing of it is put to
            // sleep for being out of sight)
            var env = await WebEnvironment();
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception e)
        {
            if (Web.Parent == null) return; // (let go of while it started: another school chosen over the screens kept)
            Phase = Phase.Web;
            Shell.Dialogs.Fail("Simpl needs Microsoft Edge WebView2", "Simpl draws your school's pages with Microsoft Edge WebView2, which could not be started: " + e.Message + "\n\nInstall it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and open Simpl again.");
            return;
        }
        var core = Web.CoreWebView2;
        PageErrors.Watch(core); // (the screenshot suite's runs: the page's own errors in simpl.log, for CI to read)
        // (a development run may ask the page to keep to little memory throughout: --web-memory low)
        if (Launch.IsDevelopment && Launch.Get("web-memory") == "low") core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        // the browser never offers to save a password, and fills nothing in by itself: the sign-in is the app's own
        // (LoginAssist), and what the student chooses to keep goes into Simpl's own vault (LoginVault), never the
        // browser's store
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        Bridge.Shared.Register(Web);
        Bridge.Shared.FollowSystemAppearance();
        if (Launch.Flag("demo")) Bridge.Shared.Seed(ScriptBundle.DemoSeed());
        // the extension's scripts, then the sign-in reader on every page (the school's sign-in pages above all): all asked
        // for at once, one after another in that order — the order they run in — and all in before the first page
        var adding = new List<Task<string>>();
        foreach (var s in await bundle) adding.Add(core.AddScriptToExecuteOnDocumentCreatedAsync(s));
        adding.Add(core.AddScriptToExecuteOnDocumentCreatedAsync(ScriptBundle.LoginScript(BaseUrl.Host)));
        await Task.WhenAll(adding);
        core.WebMessageReceived += OnMessage;
        core.NavigationStarting += (_, e) =>
        {
            Loading = true;
            ready = false;
            FailCalls("The page went away.");
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)) Login.WillLoad(u); // (the saved sign-in page is covered from its first frame)
        };
        core.NavigationCompleted += (_, e) =>
        {
            Loading = false;
            var u = Uri.TryCreate(core.Source, UriKind.Absolute, out var parsed) ? parsed : null;
            PageFinished(u);
            // (a page that came, whatever it said — a sign-in refused with a 401 is still a page; one that never came is not)
            if (e.IsSuccess || e.HttpStatusCode > 0) Login.DidLoad(u);
            else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) Login.PageFailed();
        };
        Bridge.Shared.SignOutRequested -= SignOut;
        Bridge.Shared.SignOutRequested += SignOut;
        core.NewWindowRequested += OnNewWindow;
        core.DownloadStarting += OnDownload;
        core.Navigate(new Uri(BaseUrl, "/").ToString());
        // a page that never says (a school's sign-in on another host, a page that failed): shown as it is
        _ = Task.Delay(10_000).ContinueWith(_ => Application.Current.Dispatcher.Invoke(() => { if (Phase == Phase.Starting) SetPhase(Phase.Web); }));
    }

    /// <summary>
    /// Signed in here before, the setup done (or put off with Set up later): the app's own screens at once, on what they
    /// showed last time, while the web view starts and the page loads underneath; the live answers replace them as they
    /// come.
    /// </summary>
    private void OpenOnKept()
    {
        if (Phase != Phase.Starting || Prefs.Bool("noKept")) return;
        var s = Kept<Snapshot>("snapshot");
        if (s == null || (s.SetupDone != true && !Prefs.Bool(SetupLaterKey)) || s.Me == null) return;
        Snapshot = s;
        if (s.Lms != null) Lms = s.Lms;
        if (Kept<CoursesData>("courses") is { } c) Courses = c.Rows;
        if (Kept<GroupsData>("groups") is { } g) Groups = g.Current;
        openedOnKept = true;
        TakeLaunchPlace();
        SetPhase(Phase.Native);
    }

    /// <summary>What a read answered last time, if it was kept.</summary>
    public T? Kept<T>(string name, object? args = null) where T : class
    {
        var text = Answers.Text(name, ArgsOf(args));
        if (text == null) return null;
        try { return JsonSerializer.Deserialize<T>(text, Json.Options); } catch { return null; }
    }

    // MARK: - What the page says

    public void ShellMessage(string op, JsonObject body)
    {
        switch (op)
        {
            case "shell.state":
            {
                if (body["lms"]?.ToString() is { } k && k != Lms) Lms = k;
                var on = body["shell"]?.GetValue<bool>() == true;
                if (on)
                {
                    ready = true;
                    openedOnKept = false;
                    SetPhase(Phase.Native);
                    if (!shownNative) { shownNative = true; FirstNative().ContinueWith(t => Log.Write("first: " + t.Exception), TaskContinuationOptions.OnlyOnFaulted); }
                }
                else
                {
                    ready = false;
                    openedOnKept = false;
                    SetPhase(Phase.Web);
                }
                break;
            }
            case "shell.open":
                if (body["url"]?.ToString() is { } url) OpenWeb(url, body["title"]?.ToString() ?? "");
                break;
            case "shell.tab":
                SwitchTab(body["tab"]?.ToString() ?? "today");
                break;
        }
    }

    /// <summary>
    /// The page under the app's own screens is never seen while they are up: it keeps its code and its data but lays
    /// out, paints and animates nothing, so it costs next to nothing; shown again (a sign-in), it draws as before.
    /// </summary>
    private void IdlePage(bool idle)
    {
        if (Web.CoreWebView2 == null) return;
        var js = """
        (function (on) {
          var d = document.documentElement; if (!d) return;
          var id = 'simpl-engine-idle', s = document.getElementById(id);
          if (on && !s) {
            s = document.createElement('style'); s.id = id;
            s.textContent = 'html.simpl-engine-idle body{content-visibility:hidden!important}html.simpl-engine-idle *,html.simpl-engine-idle *::before,html.simpl-engine-idle *::after{animation-play-state:paused!important;transition:none!important}';
            (document.head || d).appendChild(s);
          }
          d.classList.toggle('simpl-engine-idle', !!on);
        })
        """ + $"({(idle ? "true" : "false")});";
        _ = Web.CoreWebView2.ExecuteScriptAsync(js);
    }

    /// <summary>
    /// The page asked to keep to little memory (the window minimized or in the notification area a while:
    /// Support.MemoryTidy) — WebView2 then hands back what its processes are not using — or for its pace again. Measured:
    /// minimized, its processes' working set from 342 MB to 53; its calls no slower once back to normal.
    /// </summary>
    public void Lean(bool on)
    {
        if (Web.CoreWebView2 is not { } core) return;
        if (Launch.IsDevelopment && Launch.Get("web-memory") == "low") return; // (kept low throughout)
        core.MemoryUsageTargetLevel = on ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
    }

    private void SetPhase(Phase p)
    {
        IdlePage(p == Phase.Native);
        Phase = p;
    }

    private void PhaseChanged()
    {
        // (the page underneath lets go of the keyboard: what is typed goes to the app's own screens)
        if (Phase == Phase.Native && Web.IsKeyboardFocusWithin) System.Windows.Input.Keyboard.ClearFocus();
    }

    private void PageFinished(Uri? url)
    {
        if (url == null) return;
        if (Phase == Phase.Native) IdlePage(true);
        if (!string.Equals(url.Host, BaseUrl.Host, StringComparison.OrdinalIgnoreCase) || IsSignIn(url.AbsolutePath))
        {
            if (Phase != Phase.Web) { openedOnKept = false; ready = false; SetPhase(Phase.Web); }
        }
    }

    public static bool IsSignIn(string path)
    {
        var p = path.ToLowerInvariant();
        return p.StartsWith("/login") || p.StartsWith("/d2l/login") || p.StartsWith("/d2l/lp/auth/login");
    }

    /// <summary>
    /// Sign Out (the Mac's AppSession.signOut): the school's session — its cookies and site data — and the saved sign-in
    /// cleared, or the app would sign straight back in; then the school's sign-in page.
    /// </summary>
    public async void SignOut()
    {
        Notices.Stop();
        Login.Forget();
        LoginVault.Clear();
        if (Web.CoreWebView2 is { } core)
        {
            try
            {
                core.CookieManager.DeleteAllCookies();
                await core.Profile.ClearBrowsingDataAsync();
            }
            catch (Exception e) { Log.Write("sign out: " + e.Message); }
        }
        SignedOut();
    }

    /// <summary>Signed out: the school's sign-in page at once, and the next account starts afresh.</summary>
    public void SignedOut()
    {
        Answers.Clear();
        ready = false;
        openedOnKept = false;
        shownNative = false;
        Snapshot = null;
        Courses = new();
        Groups = new();
        CountsLive = false;
        CoursesLive = false;
        Nav.Replace(Place.Dashboard);
        LastMove = NavMove.Replace;
        NavVersion++;
        SetPhase(Phase.Web);
        Web.CoreWebView2?.Navigate(new Uri(BaseUrl, "/").ToString());
    }

    /// <summary>Where the window opens, as the screenshot suite asks (--place todo, course:101, section:courses/101:assignments),
    /// or where an update left it (Support/Updates.cs: its place, and what was opened on it).</summary>
    private void TakeLaunchPlace()
    {
        if (launchPlaceTaken) return;
        launchPlaceTaken = true;
        var resumed = Updates.TakeResumedPlace();
        if ((Launch.Get("place") ?? resumed?.Place) is { } p && PlaceNamed(p) is { } place)
        {
            Nav.Replace(place);
            if (resumed is { Path: var opened } && Launch.Get("place") == null)
            {
                if (opened.Count > 0) Nav.SetPath(opened);
                Log.Write($"update: opened again at {p}{(opened.Count > 0 ? $" with {opened.Count} opened on it" : "")}");
            }
            LastMove = NavMove.Replace;
            NavVersion++;
            if (place.Ctx is { } ctx)
            {
                if (Kept<HomeData>("home", new { ctx }) is { } h) SetSections(ctx, h.Sections);
                _ = LoadSections(ctx);
            }
            if (place.Kind == PlaceKind.Search) Query = place.Arg ?? "";
        }
    }

    private async Task FirstNative()
    {
        TakeLaunchPlace();
        if (Launch.Get("push") is { Length: > 0 } push) OpenWeb(push, "");
        if (Launch.Take("quiz:") is { } q)
        {
            var parts = q.Split(':');
            if (parts.Length >= 2) QuizRequested?.Invoke(new QuizLaunch(parts[0], parts[1], "Quiz")
            {
                Begin = parts.Length > 2 && (parts[2] == "take" || parts[2] == "review"),
                StartAt = parts.Length > 3 && int.TryParse(parts[3], out var n) ? n : null,
                Feedback = parts.Length > 2 && parts[2] == "feedback",
                Review = parts.Length > 2 && parts[2] == "review",
            });
        }
        else if (Launch.Take("tool:") is { } t)
        {
            var parts = t.Split(':');
            if (parts.Length == 2) OpenTool(ToolLaunch.CourseTool(parts[0], parts[1], "Tool"));
        }
        else if (Launch.Take("alta:") is { } a)
        {
            var parts = a.Split(':');
            if (parts.Length == 2) AltaRequested?.Invoke(new AltaLaunch(parts[0], parts[1], "Knewton Alta"));
        }
        else if (Launch.Take("macmillan:") is { } mm)
        {
            var parts = mm.Split(':');
            if (parts.Length == 2) MacmillanRequested?.Invoke(new AltaLaunch(parts[0], parts[1], "Macmillan Learning"));
        }
        else if (Launch.Take("newtask") != null) NewTaskRequested?.Invoke();
        else if (Launch.Take("compose") != null) NewMessageRequested?.Invoke();
        await RefreshSnapshot();
        await LoadSidebar();
        // (a first setup quit halfway, or whose save failed, is asked again — the page may have kept some of it — unless
        //  it was put off with Set up later, when it waits for Settings ▸ General ▸ Courses shown and goals)
        var later = Prefs.Bool(SetupLaterKey);
        if (Snapshot?.SetupDone == false && !later && !Launch.IsDevelopment) Prefs.Set(SetupPendingKey, true);
        if (((Snapshot?.SetupDone == false || Prefs.Bool(SetupPendingKey)) && !later) || Launch.Take("setup") != null) { SetupRequested?.Invoke(); return; }
        if (Launch.Take("whatsnew") != null) WhatsNewRequested?.Invoke(WhatsNew.All);
        else if (Launch.Take("settings") != null) SettingsRequested?.Invoke();
        else if (WhatsNew.Due() is { } wn) WhatsNewRequested?.Invoke(wn);
    }

    /// <summary>The first setup not finished yet — quit halfway, or its save failed: asked again at the next start.</summary>
    public const string SetupPendingKey = "setup:pending";

    /// <summary>The first setup put off with Set up later (the Mac's SetupLater): not put up again at launch, the app
    /// opening on what it showed last time meanwhile; a setup finished later clears it.</summary>
    public const string SetupLaterKey = "setup:later";

    /// <summary>A place by the name the screenshot suite gives it.</summary>
    public static Place? PlaceNamed(string raw)
    {
        var parts = raw.Split(':', 3);
        return parts[0] switch
        {
            "dashboard" or "today" => Place.Dashboard,
            "courses" => Place.Courses,
            "todo" => Place.Todo,
            "calendar" => Place.Calendar,
            "grades" => Place.Grades,
            "notifications" => Place.Notifications,
            "inbox" => Place.Inbox,
            "tools" => Place.Tools,
            "externaltools" or "external-tools" => Place.ExternalTools,
            "groups" => Place.Groups,
            "search" => Place.Search(parts.Length > 1 ? parts[1] : ""),
            "course" => parts.Length > 1 ? Place.Home($"courses/{parts[1]}") : null,
            "group" => parts.Length > 1 ? Place.Home($"groups/{parts[1]}") : null,
            "section" => parts.Length > 2 ? Place.Section(parts[1], parts[2]) : null,
            _ => null,
        };
    }

    public async Task RefreshSnapshot()
    {
        try
        {
            var s = await Call<Snapshot>("snapshot");
            Snapshot = s;
            CountsLive = true;
            if (s.Lms != null && s.Lms != Lms) Lms = s.Lms;
        }
        catch { /* the counts stay as they were */ }
    }

    /// <summary>The sidebar's courses and groups, read again.</summary>
    public async Task LoadSidebar()
    {
        try { var c = await Call<CoursesData>("courses"); Courses = c.Rows; CoursesLive = true; } catch { }
        try { var g = await Call<GroupsData>("groups"); Groups = g.Current; } catch { }
    }

    /// <summary>A course's sections, as its home lists them (looked up once, when its row in the sidebar is opened).</summary>
    public async Task LoadSections(string ctx)
    {
        if (sectionsRead.Contains(ctx)) return;
        if (!Sections.ContainsKey(ctx) && Kept<HomeData>("home", new { ctx }) is { } kept) SetSections(ctx, kept.Sections);
        try
        {
            var h = await Call<HomeData>("home", new { ctx });
            sectionsRead.Add(ctx);
            SetSections(ctx, h.Sections);
        }
        catch { }
    }

    private void SetSections(string ctx, List<SectionLink> list)
    {
        var copy = new Dictionary<string, List<SectionLink>>(Sections) { [ctx] = list };
        Sections = copy;
    }

    /// <summary>Something changed under the screens (a tick, a hand-in): they read again, and so do the counts.</summary>
    public void DataChanged()
    {
        reading.Clear(); // (what is read from here on is asked anew, never joined to a read asked before the change)
        DataVersion++;
        _ = RefreshSnapshot();
    }

    /// <summary>View ▸ Reload: the school asked afresh (the page's caches let go), then every screen reads again.</summary>
    public async void Refresh()
    {
        if (Refreshing) return;
        Refreshing = true;
        try { await Call<OK>("refresh"); } catch { }
        reading.Clear(); // (the page's caches let go: nothing read from here on joins a read asked before)
        await LoadSidebar();
        DataChanged();
        await Task.Delay(400);
        Refreshing = false;
    }

    public void Reload() => Web.CoreWebView2?.Reload();

    // MARK: - Calls

    private static JsonObject ArgsOf(object? args) => args switch
    {
        null => new JsonObject(),
        JsonObject o => o,
        _ => JsonSerializer.SerializeToNode(args, Json.Script) as JsonObject ?? new JsonObject(),
    };

    /// <summary>
    /// A call into the page's engine (BCVNative.call), its answer decoded. Waits for the page to be ready (a page load
    /// under way), and tries again if the page went away mid-call. A read already asked with the same arguments, not
    /// answered yet, is not asked twice: its answer is this one's too.
    /// </summary>
    public async Task<T> Call<T>(string name, object? args = null)
    {
        var a = ArgsOf(args);
        var asked = DataVersion;
        var text = await Read(name, a);
        // (decoded off the window's thread: a long answer holds up nothing on screen)
        var answer = await Task.Run(() => Decode<T>(name, text));
        Answers.Keep(text, name, a, asked);
        return answer;
    }

    /// <summary>
    /// A call's answer text, as <see cref="CallText"/> gives it; a read (a name whose answers are kept) already asked with
    /// the same arguments and not answered yet is waited for instead of asked again.
    /// </summary>
    private Task<string> Read(string name, JsonObject a)
    {
        if (AnswerCache.FileName(name, a) is not { } key) return CallText(name, a);
        if (reading.TryGetValue(key, out var asked)) return asked.Task;
        var shared = new TaskCompletionSource<string>();
        reading[key] = shared;
        _ = Ask(key, shared, name, a);
        return shared.Task;
    }

    private async Task Ask(string key, TaskCompletionSource<string> shared, string name, JsonObject a)
    {
        string text;
        try { text = await CallText(name, a); }
        catch (Exception e)
        {
            Settled(key, shared);
            shared.SetException(e);
            return;
        }
        Settled(key, shared);
        shared.SetResult(text);
    }

    /// <summary>A shared read answered: the next one asked is asked anew (unless a change let go of it already).</summary>
    private void Settled(string key, TaskCompletionSource<string> shared)
    {
        if (reading.TryGetValue(key, out var now) && now == shared) reading.Remove(key);
    }

    /// <summary>An answer read (off the window's thread): the page's own error said as one, else the answer decoded.</summary>
    private static T Decode<T>(string name, string text)
    {
        if (text.Length > 0 && text[0] == '{')
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String) throw new EngineException(err.GetString() ?? "");
            }
            catch (JsonException) { throw new EngineException("The answer could not be read."); }
        }
        T? answer;
        try { answer = JsonSerializer.Deserialize<T>(text, Json.Options); }
        catch (JsonException e)
        {
            Debug.WriteLine($"[Simpl] {name} answer not understood: {e.Message}");
            throw new EngineException("The answer could not be read.");
        }
        if (answer == null) throw new EngineException("The answer could not be read.");
        return answer;
    }

    /// <summary>A call's answer as the page gives it (JSON text): the Developer window's console, and <see cref="Call{T}"/>.</summary>
    public async Task<string> CallText(string name, object? args = null)
    {
        var a = ArgsOf(args);
        Exception last = new EngineException($"{LmsName} is still loading. Try again in a moment.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var hold = !ready && Shot.WaitsForPage;
            if (hold) Shot.Began();
            try { await WhenReady(); }
            finally { if (hold) Shot.Ended(); }
            if (!ready || Web.CoreWebView2 == null) throw new EngineException($"{LmsName} is still loading. Try again in a moment.");
            var id = ++nextCall;
            var done = new TaskCompletionSource<(bool Ok, string Text)>();
            calls[id] = done;
            Shot.Began();
            callsInFlight++;
            LastCallAt = Environment.TickCount64;
            var perfAt = Perf.CallStarted(name);
            try
            {
                await Web.CoreWebView2.ExecuteScriptAsync($"window.__simplCall&&window.__simplCall({id},{Json.Str(name)},{Json.Literal(a)})");
                using var wait = new CancellationTokenSource();
                var finished = await Task.WhenAny(done.Task, Task.Delay(90_000, wait.Token));
                wait.Cancel(); // (its timer let go of as soon as the answer is in)
                if (finished != done.Task) throw new EngineException("The page did not answer.");
                var (ok, text) = done.Task.Result;
                if (ok) return text;
                if (text == "not ready") { last = new EngineException("not ready"); await Task.Delay(400); continue; }
                throw new EngineException(text);
            }
            catch (EngineException e) when (e.Message == "The page went away.")
            {
                last = e;
                await Task.Delay(400);
            }
            finally
            {
                calls.Remove(id);
                Shot.Ended();
                callsInFlight = Math.Max(0, callsInFlight - 1);
                LastCallAt = Environment.TickCount64;
                Perf.CallEnded(name, perfAt);
            }
        }
        throw last;
    }

    /// <summary>An action whose answer is only whether it worked.</summary>
    public async Task<bool> Act(string name, object? args = null)
    {
        try { var r = await Call<OK>(name, args); return r.Ok ?? true; }
        catch { return false; }
    }

    private async Task WhenReady(double timeout = 25)
    {
        var start = DateTime.UtcNow;
        while (!ready && (DateTime.UtcNow - start).TotalSeconds < timeout) await Task.Delay(100);
    }

    private void FailCalls(string why)
    {
        foreach (var c in calls.Values.ToList()) c.TrySetException(new EngineException(why));
    }

    /// <summary>
    /// The call a message answers, when it is a call's answer as the native channel writes it, its kind and its id before
    /// the answer itself (<see cref="ScriptBundle.NativeChannel"/>): only its first few names are looked at, never the
    /// answer. Anything else, null — read the usual way.
    /// </summary>
    private static long? AnswerTo(string json)
    {
        var head = System.Text.Encoding.UTF8.GetBytes(json.Length > 96 ? json[..96] : json);
        var r = new Utf8JsonReader(head, isFinalBlock: false, default);
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return null;
        string? kind = null;
        long? id = null;
        while (kind == null || id == null)
        {
            if (!r.Read() || r.TokenType != JsonTokenType.PropertyName) return null;
            var name = r.GetString();
            if (!r.Read()) return null;
            if (name == "kind" && r.TokenType == JsonTokenType.String) kind = r.GetString();
            else if (name == "id" && r.TokenType == JsonTokenType.Number && r.TryGetInt64(out var n)) id = n;
            else return null;
        }
        return kind == "call" ? id : null;
    }

    /// <summary>A call's answer read (off the window's thread) and handed to the call.</summary>
    private static void Answer(TaskCompletionSource<(bool Ok, string Text)> done, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var m = doc.RootElement;
            var ok = m.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
            var text = !m.TryGetProperty("text", out var t) ? "" : t.ValueKind switch
            {
                JsonValueKind.String => t.GetString() ?? "",
                JsonValueKind.Null => "",
                _ => t.GetRawText(),
            };
            done.TrySetResult((ok, text));
        }
        catch { /* (not an answer that can be read: as if none came) */ }
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.WebMessageAsJson; } catch { return; }
        // a call's answer: the call's as it comes, whatever the page does next, and read off the window's thread; all
        // else the page says (the bridge's storage, its questions) is heard here, in order
        if (AnswerTo(json) is { } callId)
        {
            if (calls.Remove(callId, out var answered)) _ = Task.Run(() => Answer(answered, json));
            return;
        }
        JsonObject? m;
        try { m = JsonNode.Parse(json) as JsonObject; } catch { return; }
        if (m == null) return;
        var kind = m["kind"]?.ToString();
        if (kind == "call")
        {
            var id = m["id"]?.GetValue<long>() ?? 0;
            if (calls.TryGetValue(id, out var done)) done.TrySetResult((m["ok"]?.GetValue<bool>() == true, m["text"]?.ToString() ?? ""));
            return;
        }
        if (kind != "msg") return;
        var msgId = m["id"]?.GetValue<long>() ?? 0;
        var name = m["name"]?.ToString();
        JsonNode? answer = null;
        var ok = true;
        try
        {
            if (name == "bcv" && m["msg"] is JsonObject body) answer = await Bridge.Shared.Handle(body, Web);
            else if (name == "bcvLogin" && m["msg"] is JsonObject said) Login.Said(said, e.Source); // (the page's own address, as WebView2 knows it)
        }
        catch (Exception ex)
        {
            ok = false;
            answer = ex.Message;
            Log.Write($"bridge {(m["msg"] as JsonObject)?["op"]} ({name}): {ex}");
        }
        if (Web.CoreWebView2 == null) return;
        var value = answer == null ? "null" : Json.Literal(answer);
        _ = Web.CoreWebView2.ExecuteScriptAsync($"window.__simplNative&&window.__simplNative.reply({msgId},{(ok ? "true" : "false")},{value})");
    }

    /// <summary>A page that asks for a window of its own: the school's in a window of the app's, another site in the browser.</summary>
    private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)) return;
        if (IsSchool(u)) OpenWeb(u.ToString(), ""); else OpenBrowser(u);
    }

    private void OnDownload(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // a file the page hands over: kept in Downloads and opened, as the Mac shows it in Quick Look
        e.Handled = true;
        FilePreview.Downloading(e);
    }

    // MARK: - Where the window is

    private void Moved(NavMove how)
    {
        LastMove = how;
        NavVersion++;
    }

    /// <summary>A place picked: shown at its root.</summary>
    public void Go(Place place)
    {
        Nav.Go(place);
        Moved(NavMove.Go);
        if (place.Ctx is { } ctx) _ = LoadSections(ctx);
    }

    /// <summary>A screen pushed on the place showing.</summary>
    public void Push(Route route) { Nav.Push(route); Moved(NavMove.Push); }

    public void GoBack() { Nav.GoBack(); Moved(NavMove.Back); }

    public void GoForward() { Nav.GoForward(); Moved(NavMove.Forward); }

    public void PopToRoot() { Nav.PopToRoot(); Moved(NavMove.Root); }

    public void SetPath(IReadOnlyList<Route> path)
    {
        var deeper = path.Count > Nav.Current.Path.Count;
        Nav.SetPath(path);
        Moved(deeper ? NavMove.Path : NavMove.Back);
    }

    /// <summary>
    /// A native screen by its route: a course, a group, a section, the Inbox and the roots are places in the sidebar; an
    /// assignment, a discussion, a page, a folder, a conversation are pushed on the place showing — a course's screen in
    /// its course.
    /// </summary>
    public void Open(Route route)
    {
        switch (route.Kind)
        {
            case RouteKind.Course: Go(Place.Home($"courses/{route.A}")); return;
            case RouteKind.Group: Go(Place.Home($"groups/{route.A}")); return;
            case RouteKind.Groups: Go(Place.Groups); return;
            case RouteKind.Inbox: Go(Place.Inbox); return;
            case RouteKind.Notifications: Go(Place.Notifications); return;
            case RouteKind.Calendar: Go(Place.Calendar); return;
            case RouteKind.Section: Go(Place.Section(route.A, route.B)); return;
        }
        if (PlaceOf(route) is { } place && !Navigator.Within(Nav.Current.Place, place))
        {
            Nav.Push(route, place);
            Moved(NavMove.Push);
            if (place.Ctx is { } ctx) _ = LoadSections(ctx);
        }
        else Push(route);
    }

    /// <summary>The shell, asked to get a screen ready ahead of a click (Kit.Ahead): read it — and, pressed, build it unseen.</summary>
    public event Action<Place, Route?, bool>? PrepareRequested;

    /// <summary>A place about to be picked: its screen read ahead (and, <paramref name="build"/>, built unseen).</summary>
    public void Prepare(Place place, bool build) => PrepareRequested?.Invoke(place, null, build);

    /// <summary>The school's address about to be opened: the screen it opens read ahead, when it is one of the app's own.</summary>
    public void PrepareWeb(string? url, bool build)
    {
        if (string.IsNullOrEmpty(url) || NativeRoute(url, "") is not { } route) return;
        switch (route.Kind)
        {
            case RouteKind.Course: Prepare(Place.Home($"courses/{route.A}"), build); return;
            case RouteKind.Group: Prepare(Place.Home($"groups/{route.A}"), build); return;
            case RouteKind.Groups: Prepare(Place.Groups, build); return;
            case RouteKind.Inbox: Prepare(Place.Inbox, build); return;
            case RouteKind.Notifications: Prepare(Place.Notifications, build); return;
            case RouteKind.Calendar: Prepare(Place.Calendar, build); return;
            case RouteKind.Section: Prepare(Place.Section(route.A, route.B), build); return;
            case RouteKind.Web: return;
        }
        PrepareRequested?.Invoke(PlaceOf(route) ?? Nav.Current.Place, route, build);
    }

    /// <summary>The place in its course a screen belongs under.</summary>
    public static Place? PlaceOf(Route route) => route.Kind switch
    {
        RouteKind.Assignment => Place.Section($"courses/{route.A}", "assignments"),
        RouteKind.Folder => Place.Section(route.A, "files"),
        RouteKind.Topic or RouteKind.Page => Place.Home(route.A),
        _ => null,
    };

    /// <summary>
    /// The school's address, wherever it leads: its native screen, a tool or a quiz over everything, a file opened, a
    /// root; else where the school sends it.
    /// </summary>
    public void OpenWeb(string url, string title)
    {
        if (ToolLaunchFor(url, title) is { } t) { OpenTool(t); return; }
        if (QuizLaunchFor(url, title) is { } q) { OpenQuiz(q); return; }
        if (FileDownloadFor(url) is { } file)
        {
            FilePreview.Open(file, string.IsNullOrEmpty(title) ? System.IO.Path.GetFileName(file.AbsolutePath) : title, Web);
            return;
        }
        if (NativeRoute(url, title) is { } route)
        {
            if (route.Kind == RouteKind.Assignment && Absolute(url) is { } u && IsSubmission(u)) Arrival = (route.B, true);
            Open(route);
            return;
        }
        if (TabName(url) is { } tab) { SwitchTab(tab); return; }
        ResolveThenOpen(url, title);
    }

    private bool OpensNatively(string url) =>
        ToolLaunchFor(url, "") != null || QuizLaunchFor(url, "") != null || FileDownloadFor(url) != null || NativeRoute(url, "") != null || TabName(url) != null;

    private sealed class Resolved { public string Url { get; set; } = ""; }

    /// <summary>An address with no screen as it stands (a module item's, one the school redirects): opened where it leads.</summary>
    private async void ResolveThenOpen(string raw, string title)
    {
        var from = Absolute(raw);
        if (from == null || !IsSchool(from))
        {
            if (from != null) OpenBrowser(from);
            return;
        }
        try
        {
            var to = await Call<Resolved>("resolveUrl", new { url = from.ToString() });
            if (Absolute(to.Url) is { } u && !Same(u, from))
            {
                if (!IsSchool(u)) { OpenBrowser(u); return; }
                if (OpensNatively(to.Url)) { OpenWeb(to.Url, title); return; }
            }
        }
        catch { }
        OpenWebScreen(from.ToString(), title);
    }

    /// <summary>The school's own page for an address ("Open in Canvas"), in a window of its own with the session.</summary>
    public async void OpenWebScreen(string url, string title)
    {
        if (Absolute(url) is not { } raw) return;
        var u = await SchoolPage(raw);
        OpenTool(ToolLaunch.Page(u.ToString(), string.IsNullOrEmpty(title) ? LmsName : title));
    }

    /// <summary>A page of another site: in a browser window of Simpl's own, never sent off.</summary>
    public void OpenBrowser(Uri u, string title = "")
    {
        if (u.Scheme is not ("http" or "https")) { OpenExternally(u); return; }
        OpenTool(ToolLaunch.Page(u.ToString(), string.IsNullOrEmpty(title) ? u.Host : title));
    }

    public static void OpenExternally(Uri u)
    {
        try { Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true }); } catch { }
    }

    public void OpenTool(ToolLaunch t) => ToolRequested?.Invoke(t);

    public void OpenQuiz(QuizLaunch q) => QuizRequested?.Invoke(q);

    public void OpenAlta(AltaLaunch a) => AltaRequested?.Invoke(a);

    public void OpenMacmillan(AltaLaunch a) => MacmillanRequested?.Invoke(a);

    public void AskSetup() => SetupRequested?.Invoke();

    public void AskSettings() => SettingsRequested?.Invoke();

    public void AskNewTask() => NewTaskRequested?.Invoke();

    public void AskNewMessage() => NewMessageRequested?.Invoke();

    public void ShowWhatsNew(WhatsNewData d) => WhatsNewRequested?.Invoke(d);

    /// <summary>A piece of work's own action: hand it in — take the quiz, open the discussion — or see its feedback.</summary>
    public void ActOn(string? raw, string title, bool feedback)
    {
        if (string.IsNullOrEmpty(raw)) return;
        if (QuizLaunchFor(raw, title) is { } q) { OpenQuiz(q with { Feedback = feedback }); return; }
        if (NativeRoute(raw, title) is { Kind: RouteKind.Assignment } route)
        {
            Arrival = (route.B, feedback);
            Push(route);
            return;
        }
        OpenWeb(raw, title);
    }

    /// <summary>A root by the name the router and the page use.</summary>
    public void SwitchTab(string name)
    {
        Go(name switch
        {
            "notifications" => Place.Notifications,
            "calendar" => Place.Calendar,
            "courses" => Place.Courses,
            "todo" => Place.Todo,
            "grades" => Place.Grades,
            "inbox" => Place.Inbox,
            _ => Place.Dashboard,
        });
    }

    /// <summary>The toolbar's search: its results in the window.</summary>
    public void Search(string text)
    {
        var q = text.Trim();
        if (q.Length == 0) return;
        if (Nav.Current.Place.Kind == PlaceKind.Search && Nav.Current.Path.Count == 0) { Nav.Replace(Place.Search(q)); Moved(NavMove.Replace); }
        else Go(Place.Search(q));
    }

    public void OpenFromSearch(string url, string title, bool external)
    {
        if (external && Uri.TryCreate(url, UriKind.Absolute, out var u)) OpenBrowser(u); else OpenWeb(url, title);
    }

    /// <summary>A row's address, wherever it leads (the Mac's go).</summary>
    public void GoTo(string? raw, string title = "")
    {
        if (string.IsNullOrEmpty(raw) || Absolute(raw) is not { } u) return;
        if (IsSchool(u)) OpenWeb(raw, title); else OpenBrowser(u, title);
    }

    public Uri? Absolute(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        return Uri.TryCreate(BaseUrl, url, out var u) ? u : null;
    }

    private static bool Same(Uri a, Uri b)
    {
        static string Key(Uri u) => $"{u.AbsolutePath.TrimEnd('/')}?{u.Query.TrimStart('?')}#{u.Fragment.TrimStart('#')}";
        return Key(a) == Key(b);
    }

    public bool IsSchool(Uri u) => u.Scheme is "http" or "https" && string.Equals(u.Host, BaseUrl.Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>The school's address of a place, for Open in Canvas and Copy Link.</summary>
    public Uri? SchoolUrl(Place place) => place.Kind switch
    {
        PlaceKind.Dashboard or PlaceKind.Todo => Absolute("/"),
        PlaceKind.Courses => Absolute("/courses"),
        PlaceKind.Calendar => Absolute("/calendar"),
        PlaceKind.Grades => Absolute("/grades"),
        PlaceKind.Notifications => Absolute("/profile/communication"),
        PlaceKind.Inbox => Absolute("/conversations"),
        PlaceKind.Groups => Absolute("/groups"),
        PlaceKind.Home => Absolute("/" + place.Ctx),
        PlaceKind.Section => Absolute($"/{place.Ctx}/" + (place.Arg switch { "discussions" => "discussion_topics", "people" => "users", "syllabus" => "assignments/syllabus", _ => place.Arg })),
        _ => null,
    };
}

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Engine;
using Simpl.Shell;
using Simpl.Support;

namespace Simpl.Screens;

// (Mac 1.3.17) A Knewton Alta assignment opened in the app: Open Tool on an Alta assignment opens a popup (AltaPopup)
// with the objectives and the total mastery down the left and Alta's own page in the rest. (1.3.45) The popup only
// reads Alta: the student works on Alta's page itself; the app presses only what the student presses in the popup.

/// <summary>A progress target as Alta's answers carry it: an objective's id, how far it is mastered, its status.</summary>
internal sealed class AltaTarget
{
    public string? Id { get; set; }
    public double? Progress { get; set; }
    public string? Status { get; set; }
}

/// <summary>What the page passes from a question's answer (the Mac's AltaReport).</summary>
internal sealed class AltaReport
{
    public sealed class Objective
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    public sealed class Now
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public double? Low { get; set; }
        public double? High { get; set; }
        public string? Source { get; set; }
    }

    public string? Name { get; set; }
    public double? Threshold { get; set; }
    public List<Objective> Objectives { get; set; } = new();
    public List<AltaTarget> Targets { get; set; } = new();
    public double? Percent { get; set; }
    public double? Progress { get; set; }
    public string? Status { get; set; }
    public bool Completed { get; set; }
    public double? Due { get; set; }
    public Now Current { get; set; } = new();
}

/// <summary>
/// (1.3.17) The assignment's overview, as Alta's page reads it before it is started (or between questions): its name,
/// when it is due, its objectives with Alta's estimate of the questions each takes, and how far it is mastered.
/// </summary>
internal sealed class AltaOverview
{
    public sealed class Objective
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public double? Low { get; set; }
        public double? High { get; set; }
    }

    public string? Name { get; set; }
    public double? Due { get; set; }
    /// <summary>(1.3.19) As the page words them, when no answer of Alta's gave them ("Monday, Oct 12 11:59pm PDT").</summary>
    public string? DueText { get; set; }
    public string? StatusText { get; set; }
    public double? Threshold { get; set; }
    public double? Percent { get; set; }
    /// <summary>(1.3.21) The whole's progress (0…1) and each objective's, from any answer of Alta's that carries them.</summary>
    public double? Progress { get; set; }
    public List<AltaTarget>? Targets { get; set; }
    public string? Status { get; set; }
    public bool? Started { get; set; }
    public bool? Completed { get; set; }
    public List<Objective> Objectives { get; set; } = new();

    /// <summary>Its objectives as the assignment's list keeps them (each named, with its id).</summary>
    public List<AltaKnown> List => Objectives.Where(o => !string.IsNullOrEmpty(o.Id) && o.Name != null).Select(o => new AltaKnown(o.Id!, o.Name!, o.Low, o.High)).ToList();
}

/// <summary>One of the assignment's objectives as its overview or cover lists it: its id, name and Alta's estimate.</summary>
internal sealed record AltaKnown(string Id, string Name, double? Low, double? High);

/// <summary>
/// (1.3.59) The assignment's cover as Alta's page shows it before it is started: its name, its details, its note, what
/// its Start says, whether it offers Review Instruction, and its objectives by topic.
/// </summary>
internal sealed class AltaCover
{
    public sealed class Detail
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string? Sub { get; set; }
    }

    public sealed class Objective
    {
        public string Name { get; set; } = "";
        public double? Mastery { get; set; }
    }

    public sealed class Topic
    {
        public string Name { get; set; } = "";
        public string Estimate { get; set; } = "";
        public double? Low { get; set; }
        public double? High { get; set; }
        public List<Objective> Objectives { get; set; } = new();
    }

    public string Name { get; set; } = "";
    public List<Detail> Details { get; set; } = new();
    public string Note { get; set; } = "";
    public string Start { get; set; } = "";
    public bool Review { get; set; }
    public List<Topic> Topics { get; set; } = new();

    /// <summary>(1.3.60) Every objective, in order, each with its topic's estimate: the assignment's whole list.</summary>
    public List<AltaKnown> List => Topics.SelectMany(t => t.Objectives.Select(o => new AltaKnown(o.Name, o.Name, t.Low, t.High))).ToList();
}

/// <summary>One objective as the popup shows it.</summary>
internal sealed record AltaObjective(string Id, int Number, string Name, double Mastery, bool Current)
{
    public bool Mastered => Mastery >= 0.999;
}

/// <summary>(1.3.53) A part of Alta's question as the page lays it out (its own coordinates): what it asks, its answer, or a lesson.</summary>
internal sealed record AltaPanel(int Id, string Kind, Rect Rect);

/// <summary>(1.3.70) A small graph in what a question asks, where the page lays it (its own coordinates).</summary>
internal sealed record AltaPic(int Id, Rect Rect);

/// <summary>(1.3.53) One of the buttons in Alta's button bars, whatever it says: shown as the app's own, pressing Alta's.</summary>
internal sealed record AltaButton(int Id, string Label, bool Primary, bool Enabled);

/// <summary>(0.3.5) One of a pop-up's buttons as Alta has it: its words, whether Alta makes it the main one, whether it can
/// be pressed, and whether it is the pop-up's close button (an icon, a ×).</summary>
internal sealed record AltaDialogButton(string Label, bool Primary, bool Enabled, bool Close);

/// <summary>(1.3.51) A pop-up of Alta's, shown as the app's own: (0.3.5) its title, its body as plain HTML the app draws as
/// its own words — its formulas' TeX, its pictures as the page made them — and its buttons; and how much of it was read
/// and when, for the log.</summary>
internal sealed class AltaDialog
{
    public string Title { get; init; } = "";
    public string Html { get; init; } = "";
    public List<AltaDialogButton> Buttons { get; init; } = new();
    public int Words { get; init; }
    public int Maths { get; init; }
    public int Pics { get; init; }
    /// <summary>Pictures of its body that could not be had (their words, if they had any, are shown in their place).</summary>
    public int Missed { get; init; }
    /// <summary>What it holds that the app cannot show as its own ("a video", "a form", "a frame of another site’s"), or "".</summary>
    public string Live { get; init; } = "";
    /// <summary>"settled", or how long it was waited for ("after 3 s").</summary>
    public string Ready { get; init; } = "";
}

/// <summary>
/// One Alta assignment open in the popup (the Mac's AltaSession): Alta's page in a web view of the popup's own — signed
/// in as the app is (the engine's web data, its browser arguments) — and what the page last said of the objectives and
/// their mastery, the question's parts and buttons, its pop-ups and its cover. The popup draws from it and is told when
/// it changes.
/// </summary>
internal sealed class AltaSession
{
    private sealed class LaunchAnswer { public string Url { get; set; } = ""; }

    private const string NamesKey = "altaObjectiveNames";
    private const string PicsLightKey = "altaGraphsLight";
    private const string DarkKey = "altaDark";
    private const string BrowserKey = "altaBrowser";

    private readonly Engine.Engine engine;
    private Window owner;
    private readonly Dictionary<string, string> names;
    private readonly List<CoreWebView2Frame> frames = new();
    private readonly Dictionary<string, TaskCompletionSource<JsonNode?>> codeAsked = new();
    private DispatcherTimer? dialogHoldLimit;
    private bool dialogHeld;
    private string? darkScript;
    private bool started, closed, notifyQueued;
    private string lastPaint = "";
    private int? picOpen;

    /// <summary>Something the popup shows changed (coalesced: told once per turn of the dispatcher).</summary>
    public event Action? Changed;

    public AltaLaunch Launch { get; }
    /// <summary>Alta's page: the popup places it, shown only while nothing of the popup's own is in its place. (0.3.22) The
    /// web view made ahead for it (AltaSpare), when there was one; else one made with the session.</summary>
    public WebView2 Web { get; private set; }
    /// <summary>(0.3.22) The web view made ahead that this open took, dressed already (its scripts in, its empty page
    /// loaded) — null once Start has taken it up, or when there was none.</summary>
    private AltaSpare.View? spare;

    public AltaReport? Report { get; private set; }
    /// <summary>(1.3.17) The overview Alta opens on: the assignment and its objectives, before a question is asked.</summary>
    public AltaOverview? Overview { get; private set; }
    /// <summary>Alta's page still opening (the launch through Canvas): a spinner in its place meanwhile.</summary>
    public bool Opening { get; private set; } = true;
    /// <summary>The launch could not be had from Canvas.</summary>
    public string? Failure { get; private set; }
    /// <summary>(1.3.21) The mastery last heard, from wherever Alta said it, 0…1: the whole, and each objective's by its id.</summary>
    public double? LivePercent { get; private set; }
    public Dictionary<string, AltaTarget> LiveTargets { get; } = new();
    /// <summary>(1.3.47) How Alta's page is shown: "question" or "lesson" (its parts alone), "overview", or — not
    /// recognised — "unknown" / "none", when it is shown as it is.</summary>
    public string FocusState { get; private set; } = "";
    /// <summary>(1.3.53) The parts' panels, the page's height, which question they are of, and the parts' buttons.</summary>
    public List<AltaPanel> Panels { get; private set; } = new();
    public double PageHeight { get; private set; }
    public int PanelGen { get; private set; }
    public List<AltaButton> Buttons { get; private set; } = new();
    /// <summary>(0.3.19) Alta's verdict on the question, as the page script reads its strip under the answer: "right",
    /// "wrong" or "" (none shown) — the popup's own pill beside its buttons, gone with the next question.</summary>
    public string Verdict { get; private set; } = "";
    /// <summary>(1.3.70) Where each small graph is in the page.</summary>
    public List<AltaPic> Pics { get; private set; } = new();
    /// <summary>In dark mode a graph's colours are turned over with the question's unless the student asks for them as
    /// Alta draws them (on a white card): kept for every assignment.</summary>
    public bool PicsLight { get; private set; } = Prefs.Bool(PicsLightKey);
    /// <summary>(0.3.5) Alta's page in dark — its question's colours turned over on the dark ground, as it was in dark mode
    /// before 0.3.5 — rather than in light, as it is made (the owner's choice for every outside tool): the student's choice
    /// above the objectives, kept for every assignment, light at first, whatever the app's appearance. (--alta-look dark or
    /// light, in a development run, starts it so.)</summary>
    public bool Dark { get; private set; } = Support.Launch.Get("alta-look") is { } look ? look == "dark" : Prefs.Bool(DarkKey);
    /// <summary>(0.3.5) The part of the popup Alta's page is shown in, dressed in the page's look: its panels take their
    /// colours from it.</summary>
    public FrameworkElement? Region { get; set; }
    /// <summary>(0.3.5) Show browser: Alta's own page as it is, as in a browser — the same page, signed in, nothing of it
    /// laid out or hidden, its buttons and pop-ups its own — in place of the question laid out by the app; the objectives
    /// stay. The student's choice beside Light and Dark, kept for every assignment. (--alta-browser starts a development
    /// run with it.)</summary>
    public bool Browser { get; private set; } = Support.Launch.Flag("alta-browser") || Prefs.Bool(BrowserKey);
    /// <summary>(1.3.60) Whether Alta's page is laid out and in sight (kept out of sight while it loads).</summary>
    public bool PageShown { get; private set; }
    /// <summary>(1.3.59) The assignment's cover, while Alta's page shows it: the popup's start screen.</summary>
    public AltaCover? Cover { get; private set; }
    /// <summary>(1.3.51) Alta's pop-up open, shown as the app's own; bumped each time it is said again.</summary>
    public AltaDialog? Dialog { get; private set; }
    public int DialogVersion { get; private set; }
    /// <summary>(0.3.5) Alta's pop-up open in its page, shown there as Alta draws it — the app could not show it as its
    /// own, or the student asked for Alta's — the page in sight under it until it closes.</summary>
    public bool DialogInPage { get; private set; }
    /// <summary>(1.3.47) Each time Alta's page was not recognised and shown as it is: when, why, and what the page held.</summary>
    public List<string> ErrorLogs { get; } = new();
    /// <summary>(1.3.23) The assignment's objectives as its overview lists them, kept for this assignment.</summary>
    public List<AltaKnown> Known { get; private set; } = new();

    public AltaSession(Engine.Engine engine, AltaLaunch launch, Window owner, long? asked = null, AltaSpare.View? spare = null)
    {
        this.engine = engine;
        this.owner = owner;
        Launch = launch;
        since = asked ?? Stopwatch.GetTimestamp();
        this.spare = spare;
        webHow = spare != null ? "spare" : "made";
        Web = spare?.Web ?? new WebView2 { Visibility = Visibility.Hidden };
        AltaSpare.Opened();
        names = ReadJson<Dictionary<string, string>>(NamesKey) ?? new();
        // (0.3.21) the launch asked of Canvas the moment the popup is asked for, before any of it is built — the
        // page sent there once the web view is made (Start)
        given = Launch.Page is { } p ? engine.Absolute(p) : null;
        if (given == null) launching = AskLaunch();
    }

    /// <summary>The popup's own window, once it shows: a graph opened from the page keeps above it (AltaGraphWindow).</summary>
    public void ShowsIn(Window w) => owner = w;

    private string KnownKey => $"altaObjectives:{Launch.Course}:{Launch.Assignment}";

    /// <summary>(1.3.49) Alta's question (or lesson) is shown alone.</summary>
    public bool Focused => FocusState is "question" or "lesson";

    /// <summary>The question laid out on panels, one behind each part (never while the browser is shown).</summary>
    public bool Split => Focused && !Browser && Panels.Count > 0 && PageHeight > 0;

    private void Notify()
    {
        Shot.Moved();
        if (notifyQueued || closed) return;
        notifyQueued = true;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            notifyQueued = false;
            if (!closed) Changed?.Invoke();
        }, DispatcherPriority.Normal);
    }

    // ---- starting ----

    /// <summary>(0.3.21) Done once the page has been sent on its way — to the launch, or to the page asked for —
    /// or could not be: the popup asks Canvas for the assignment's due line only then, never in the launch's way.</summary>
    public Task Launched => launched.Task;
    private readonly TaskCompletionSource launched = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>(0.3.21) The page asked for (a page of the mock's, for the pictures), or else the launch,
    /// asked of Canvas as the session is made.</summary>
    private readonly Uri? given;
    private readonly Task<(Uri? Url, string? Why)>? launching;

    /// <summary>Alta opened: the web view made with the engine's web data and the page script put in it while the launch,
    /// asked as the session was made, is on its way — (0.3.22) or the one made ahead taken up, all of that done already;
    /// the page sent to the launch (or to the page asked for) once both are done.</summary>
    public async void Start()
    {
        if (started) return;
        started = true;
        Hold(true); // (the screenshot suite waits for Alta's page, not only for the app's own calls)
        // (0.3.21) the launch, asked already, waited for only when the page is to go to it — while the web view
        // is made and dressed; it was asked only once all of that was done, its whole time on top of it
        if (ReadJson<List<AltaKnown>>(KnownKey) is { } kept) Known = kept;
        if (spare is { } ahead && Web.CoreWebView2 is { } readyCore)
        {
            spare = null;
            webReady = Now();
            darkScript = ahead.FlagsId;
            // (the look chosen since it was made: its flags put in again, ahead of the page script as they must be)
            if (ahead.Flags != DarkScript() || ahead.HookId == null)
            {
                try
                {
                    if (ahead.FlagsId != null) readyCore.RemoveScriptToExecuteOnDocumentCreated(ahead.FlagsId);
                    if (ahead.HookId != null) readyCore.RemoveScriptToExecuteOnDocumentCreated(ahead.HookId);
                    darkScript = await readyCore.AddScriptToExecuteOnDocumentCreatedAsync(DarkScript());
                    await readyCore.AddScriptToExecuteOnDocumentCreatedAsync(AltaHook.Source);
                }
                catch (Exception e) { Log.Write("alta script: " + e.Message); }
                if (closed) return;
            }
        }
        else
        {
            try
            {
                var made = await Dress(Web, DarkScript(), () => closed, () => webReady = Now());
                if (made is not { } ids) return;
                darkScript = ids.Flags;
            }
            catch (Exception e)
            {
                Log.Write("alta: " + e.Message);
                Fail(e.Message);
                return;
            }
        }
        var core = Web.CoreWebView2;
        core.WebMessageReceived += (_, e) => { if (Said(e) is { } s) Take(s, null); };
        core.FrameCreated += (_, e) => WatchFrame(e.Frame);
        // (0.3.22: a spare's empty page is never seen here — AltaSpare waits for it to load before it is handed over, and
        // this is listened to only after — so every navigation that ends, an empty one too, ends the skeleton as before)
        core.NavigationCompleted += (_, _) =>
        {
            LightPages.Tell(core, "alta");
            if (pageAt == null) { pageAt = Now(); pageHost = HostOf(core); }
            if (!Opening) return;
            Opening = false;
            Hold(false);
            Notify();
        };
        core.NewWindowRequested += (_, e) => { e.Handled = true; engine.GoTo(e.Uri, ""); };
        core.DownloadStarting += (_, e) => { e.Handled = true; FilePreview.Downloading(e); };
        if (given != null)
        {
            core.Navigate(given.ToString());
            launched.TrySetResult();
            return;
        }
        var (url, why) = await launching!;
        if (url == null) Fail(why ?? "");
        else if (!closed) core.Navigate(url.ToString());
        launched.TrySetResult();
    }

    /// <summary>
    /// (0.3.22) A web view for Alta's page, made on Simpl's one browser (the engine's web data: Alta's launch finds the
    /// student signed in) and dressed: its settings, Alta's page told the student prefers light (0.3.5: as an outside
    /// tool's is — Shell's LightPages — so a look of its own for dark never comes in; the app turns its question's colours
    /// over itself when it is to be dark), and before every page the look's flags, then the page script. Made as the popup
    /// opens, or ahead (AltaSpare). The ids of the two scripts, or null when <paramref name="gone"/> says the web view
    /// is no longer wanted; <paramref name="made"/> is told the moment the web view itself is made.
    /// </summary>
    internal static async Task<(string? Flags, string? Hook)?> Dress(WebView2 web, string flags, Func<bool> gone, Action? made = null)
    {
        var env = await Simpl.Engine.Engine.WebEnvironment();
        await web.EnsureCoreWebView2Async(env);
        if (gone()) return null;
        made?.Invoke();
        var core = web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        await LightPages.Apply(core, "alta");
        if (gone()) return null;
        string? flagsId = null, hookId = null;
        try
        {
            flagsId = await core.AddScriptToExecuteOnDocumentCreatedAsync(flags);
            hookId = await core.AddScriptToExecuteOnDocumentCreatedAsync(AltaHook.Source);
        }
        catch (Exception e) { Log.Write("alta script: " + e.Message); }
        return gone() ? null : (flagsId, hookId);
    }

    /// <summary>(0.3.21) The assignment's launch through Canvas, asked at once: its address, or why there is
    /// none (never thrown — the web view may still be being made when it comes).</summary>
    private async Task<(Uri? Url, string? Why)> AskLaunch()
    {
        launchAsked = Now();
        try
        {
            var a = await engine.Call<LaunchAnswer>("toolLaunch", new { course = Launch.Course, assignment = Launch.Assignment });
            return Uri.TryCreate(a.Url, UriKind.Absolute, out var u) ? (u, null) : (null, "The answer could not be read.");
        }
        catch (Exception e) { return (null, e.Message); }
        finally { launchAnswered = Now(); }
    }

    // ---- (0.3.21) the open's timeline: one line in the app's log for each open ----

    /// <summary>When the popup was asked for (Stopwatch ticks), and since then, in milliseconds: the launch asked of Canvas
    /// and answered, the web view ready, the first page in (its site), the first of Alta's own pages, the part Alta's page
    /// was read as first (question, lesson, overview, cover), and Alta's page shown — how, how far into its own load, how
    /// long its part had been still, and whether only because the wait ran out.</summary>
    private readonly long since;
    private long? launchAsked, launchAnswered, webReady, pageAt, altaPageAt, partAt, shownAt;
    private string pageHost = "", partKind = "", shownHow = "";
    /// <summary>(0.3.22) The web view: "spare", made ahead and taken, or "made" as the popup opened.</summary>
    private readonly string webHow;
    private double? shownOwn, shownQuiet;
    private bool shownCapped, timelineSaid;

    private long Now() => (long)Stopwatch.GetElapsedTime(since).TotalMilliseconds;

    private static string HostOf(CoreWebView2 core)
    {
        try { return Uri.TryCreate(core.Source, UriKind.Absolute, out var u) ? u.Host : ""; } catch { return ""; }
    }

    /// <summary>Whether the page in the web view is one of Alta's own, as the page script tells (its site or its address
    /// names Knewton or Alta).</summary>
    private bool OnAlta()
    {
        try
        {
            if (Web.CoreWebView2 is not { } core || !Uri.TryCreate(core.Source, UriKind.Absolute, out var u)) return false;
            var where = u.Host + u.AbsolutePath;
            return where.Contains("knewton", StringComparison.OrdinalIgnoreCase) || where.Contains("alta", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>The line, written once: when Alta's page is shown, or else when the open ends without it (it failed, or the
    /// popup closed first). "alta: launch asked +2 ms · launch answered +240 ms · web view made +410 ms · page +620 ms
    /// (school.instructure.com) · alta page +1300 ms · question +1700 ms · shown +1820 ms (question, 520 ms into its page,
    /// quiet 110 ms)" — (0.3.22) "web view spare +3 ms" for one made ahead.</summary>
    private void SayTimeline(string? end = null)
    {
        if (timelineSaid) return;
        timelineSaid = true;
        static string At(long? ms) => ms is { } v ? $"+{v} ms" : "—";
        var parts = new List<string>();
        if (Launch.Page != null && launchAsked == null) parts.Add("page given");
        else { parts.Add("launch asked " + At(launchAsked)); parts.Add("launch answered " + At(launchAnswered)); }
        parts.Add($"web view {webHow} " + At(webReady));
        parts.Add($"page {At(pageAt)}" + (pageHost.Length > 0 ? $" ({pageHost})" : ""));
        if (altaPageAt != null) parts.Add("alta page " + At(altaPageAt));
        if (partAt != null) parts.Add($"{partKind} {At(partAt)}");
        if (shownAt != null)
        {
            var how = new List<string> { shownHow.Length > 0 ? shownHow : "shown" };
            if (shownOwn is { } own) how.Add($"{Math.Round(own)} ms into its page");
            if (shownQuiet is { } q) how.Add($"quiet {Math.Round(q)} ms");
            if (shownCapped) how.Add("capped");
            parts.Add($"shown {At(shownAt)} ({string.Join(", ", how)})");
        }
        else parts.Add("not shown: " + (end ?? "closed first") + " " + At(Now()));
        Log.Write("alta: " + string.Join(" · ", parts));
    }

    private bool holding;

    /// <summary>The picture held while Alta opens (counted as work in flight), let go once its page is in, it failed, or
    /// the popup closed.</summary>
    private void Hold(bool on)
    {
        if (on == holding) return;
        holding = on;
        if (on) Shot.Began(); else Shot.Ended();
    }

    private void Fail(string why)
    {
        SayTimeline("it could not be opened");
        launched.TrySetResult();
        Failure = "Knewton Alta could not be opened. " + why;
        Hold(false);
        Opening = false;
        Notify();
    }

    private static string? Said(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try { return e.TryGetWebMessageAsString(); } catch { return null; }
    }

    private void WatchFrame(CoreWebView2Frame f)
    {
        try
        {
            f.WebMessageReceived += (_, e) => { if (Said(e) is { } s) Take(s, f); };
            f.FrameCreated += (_, e) => WatchFrame(e.Frame);
            f.Destroyed += (_, _) => frames.Remove(f);
        }
        catch { /* an older runtime: the frame's messages are not heard */ }
    }

    /// <summary>(maths drawn whole) The skeleton never left over Alta's page for good: while it is up once the page has
    /// begun (not opening, no start screen, not Show browser), the page is asked to look at itself again every few seconds
    /// — what Light then Dark did by hand on the Mac — and after 26 seconds, longer than any wait of the page script's own,
    /// it is shown as it is, said in the Error Logs. Begun again with each page and each time Alta goes between parts.</summary>
    private int skeletonWatch;

    private async void WatchSkeleton()
    {
        var mine = ++skeletonWatch;
        for (var k = 1; k <= 6; k++)
        {
            await Task.Delay(k == 1 ? 6000 : 4000);
            if (mine != skeletonWatch || closed || Opening || PageShown || Browser || DialogInPage || Cover != null || Failure != null) return;
            if (k < 6) { _ = Run("window.__simplBrowserNow ? window.__simplBrowserNow() : null"); continue; }
            // (shown as it is by the page script itself, so its focus is lifted and what it knows stays in step — it
            // says why in the Error Logs, with what the page held; a page without it only has its veil lifted)
            var byPage = Bool(await Run("window.__simplAsIsNow ? window.__simplAsIsNow() : (document.documentElement ? document.documentElement.setAttribute('data-simpl-shown', '') : null)")) == true;
            if (mine != skeletonWatch || closed) return;
            PageShown = true;
            if (!byPage)
            {
                ErrorLogs.Add($"{DateTime.Now.ToString("MMM d, yyyy, h:mm:ss tt", CultureInfo.CurrentCulture)} — Alta’s page shown as it is: it was still out of sight after 26 seconds");
                if (ErrorLogs.Count > 50) ErrorLogs.RemoveAt(0);
            }
            Notify();
        }
    }

    /// <summary>The popup closed: the page let go, its graph's window with it.</summary>
    public void Close()
    {
        if (closed) return;
        closed = true;
        skeletonWatch++;
        SayTimeline();
        launched.TrySetResult();
        Hold(false);
        HoldForDialog(false);
        ClosePic();
        foreach (var t in codeAsked.Values) t.TrySetResult(null);
        try { Web.Dispose(); } catch { }
        AltaSpare.Closed(engine); // (0.3.22: the next popup's web view made ahead, at a quiet moment)
    }

    // ---- (1.3.50) light or dark, told to the page; (0.3.5) as the student chooses, not as the app is ----

    private string DarkScript() => Flags(Dark, PicsLight, Browser);

    private static string Flags(bool dark, bool picsLight, bool browser) => $"window.__simplDark = {Js(dark)}; window.__simplPicsLight = {Js(picsLight)}; window.__simplBrowser = {Js(browser)};";

    /// <summary>(0.3.22) The flags a popup opened now starts with (as <see cref="Dark"/>, <see cref="PicsLight"/> and
    /// <see cref="Browser"/> are first read): the ones a web view made ahead is given.</summary>
    internal static string FlagsAsKept() => Flags(
        Support.Launch.Get("alta-look") is { } look ? look == "dark" : Prefs.Bool(DarkKey),
        Prefs.Bool(PicsLightKey),
        Support.Launch.Flag("alta-browser") || Prefs.Bool(BrowserKey));

    private static string Js(bool b) => b ? "true" : "false";

    /// <summary>(0.3.5) Alta's page in light or dark, as the student chooses: kept for every assignment, and told to the
    /// page at once (its graph's window drawn again in it).</summary>
    public void SetDark(bool dark)
    {
        if (dark == Dark) return;
        Dark = dark;
        Prefs.Set(DarkKey, dark);
        Appearance();
        Notify();
        if (picOpen is { } i) _ = Reopen(i);
    }

    /// <summary>(0.3.5) Show browser, or the question laid out again: kept for every assignment, and told to the page at
    /// once — the page as it is, or laid out again; a pop-up of Alta's the app was showing given back to the page, or
    /// taken up again; the question's graph window put away.</summary>
    public void SetBrowser(bool on)
    {
        if (on == Browser) return;
        Browser = on;
        Prefs.Set(BrowserKey, on);
        if (on)
        {
            ClosePic();
            HoldForDialog(false);
            if (Dialog != null || DialogInPage) { Dialog = null; DialogInPage = false; DialogVersion++; }
        }
        Appearance();
        _ = Run($"window.__simplBrowser = {Js(on)}; window.__simplBrowserNow ? window.__simplBrowserNow() : null");
        Notify();
    }

    /// <summary>Light or dark as chosen (<see cref="Dark"/>): before each page (a script ahead of the page script) and at
    /// once to the page shown; in dark the question's colours are turned over, for the dark ground. The panels are drawn
    /// again in the look's card colours (the app's appearance changed, or the look chosen).</summary>
    public async void Appearance()
    {
        lastPaint = "";
        if (Web.CoreWebView2 is not { } core || closed) return;
        try
        {
            if (darkScript != null) core.RemoveScriptToExecuteOnDocumentCreated(darkScript);
            darkScript = await core.AddScriptToExecuteOnDocumentCreatedAsync(DarkScript());
        }
        catch { }
        _ = Run(DarkScript() + " if (window.__simplFocusDark) window.__simplFocusDark(); null");
        PaintPanels();
    }

    // ---- what the page says ----

    private void Take(string text, CoreWebView2Frame? frame)
    {
        if (closed) return;
        JsonObject? head;
        try { head = JsonNode.Parse(text) as JsonObject; } catch { return; }
        if (head == null) return;
        var main = frame == null;
        switch (Str(head["kind"]))
        {
            case "code":
                if (Str(head["token"]) is { } token && codeAsked.Remove(token, out var asked)) asked.TrySetResult(head["result"]?.DeepClone());
                return;
            case "frame":
                if (main)
                {
                    // (0.3.21: the first of Alta's own pages in — not the school's launch page on the way to it)
                    if (altaPageAt == null && OnAlta()) altaPageAt = Now();
                    // (1.3.60) a new page: kept out of sight in it until it is laid out — (1.3.62) and the last page's
                    // panels and buttons put away with it
                    PageShown = false;
                    Panels = new();
                    Buttons = new();
                    Pics = new();
                    Verdict = "";
                    lastPaint = "";
                    ClosePic();
                    // (0.3.5: and the last page's pop-up with them, whether the app showed it or the page)
                    HoldForDialog(false);
                    if (Dialog != null || DialogInPage) { Dialog = null; DialogInPage = false; DialogVersion++; }
                    WatchSkeleton();
                    Notify();
                    return;
                }
                frames.RemoveAll(f => SameFrame(f, frame!));
                frames.Add(frame!);
                if (frames.Count > 30) frames.RemoveAt(0);
                return;
            case "panels":
            {
                if (!main) return;
                var list = new List<AltaPanel>();
                var k = 0;
                foreach (var p in (head["panels"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                {
                    if (Box(p) is { } r) list.Add(new AltaPanel(k, Str(p["kind"]) ?? "", r));
                    k++;
                }
                var pics = new List<AltaPic>();
                k = 0;
                foreach (var p in (head["pics"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                {
                    if (Box(p) is { } r) pics.Add(new AltaPic(k, r));
                    k++;
                }
                var height = Num(head["height"]) ?? 0;
                var gen = (int)(Num(head["gen"]) ?? 0);
                if (!list.SequenceEqual(Panels)) Panels = list;
                if (height != PageHeight) PageHeight = height;
                if (!pics.SequenceEqual(Pics)) Pics = pics;
                if (gen != PanelGen)
                {
                    // (another question: the last one's graph window goes, and the page back to its top)
                    PanelGen = gen;
                    ClosePic();
                    _ = Run("window.scrollTo(0, 0); null");
                }
                // (0.3.19) Alta's verdict, as it comes and as it goes: the pill beside the buttons
                var verdict = Str(head["verdict"]) ?? "";
                if (verdict != Verdict) Verdict = verdict;
                PaintPanels();
                Notify();
                return;
            }
            case "buttons":
            {
                if (!main) return;
                var list = new List<AltaButton>();
                var k = 0;
                foreach (var b in (head["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                    list.Add(new AltaButton(k++, Str(b["label"]) ?? "", Bool(b["primary"]) ?? false, Bool(b["enabled"]) ?? true));
                if (list.SequenceEqual(Buttons)) return;
                Buttons = list;
                Notify();
                return;
            }
            case "dialog":
            {
                if (!main) return;
                if (Bool(head["open"]) != true)
                {
                    HoldForDialog(false);
                    if (Dialog != null || DialogInPage) { Dialog = null; DialogInPage = false; DialogVersion++; Notify(); }
                    return;
                }
                // (0.3.5) one seen, out of sight in the page until it has settled: a picture waits for it — never while
                // the browser is shown, where Alta's pop-ups are its own (one said just before it was asked for)
                if (Browser) return;
                if (Bool(head["pending"]) == true) { HoldForDialog(true); return; }
                HoldForDialog(false);
                var d = new AltaDialog
                {
                    Title = Str(head["title"]) ?? "", Html = Str(head["html"]) ?? "",
                    Buttons = (head["buttons"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                        .Select(b => new AltaDialogButton(Str(b["label"]) ?? "", Bool(b["primary"]) ?? false, Bool(b["enabled"]) ?? true, Bool(b["close"]) ?? false))
                        .Where(b => b.Label.Length > 0).ToList(),
                    Words = (int)(Num(head["words"]) ?? 0), Maths = (int)(Num(head["maths"]) ?? 0), Pics = (int)(Num(head["pics"]) ?? 0),
                    Missed = (int)(Num(head["missed"]) ?? 0), Live = Str(head["live"]) ?? "", Ready = Str(head["ready"]) ?? "",
                };
                // (never an empty dialog: what the app cannot draw, or a body with nothing read in it, is Alta's own, in its page)
                if (d.Live.Length > 0) { GiveBack($"it holds {d.Live}, which the app cannot show as its own", d); return; }
                if (d.Words == 0 && d.Maths == 0 && d.Pics == 0) { GiveBack("nothing in its body could be read", d); return; }
                Dialog = d;
                DialogInPage = false;
                DialogVersion++;
                _ = Run("window.__simplDialog ? window.__simplDialog.hide() : null");
                if (Shot.Asked)
                    Log.Write($"alta: a pop-up shown as the app's own: \"{d.Title}\", {d.Words} words, {d.Maths} formulas, {d.Pics} pictures ({d.Missed} not had), buttons {string.Join(" | ", d.Buttons.Select(b => b.Label))} ({d.Ready})");
                Notify();
                return;
            }
            case "focus":
            {
                if (!main) return;
                var state = Str(head["state"]) ?? "";
                FocusState = state;
                if (partAt == null && state is "question" or "lesson" or "overview") { partAt = Now(); partKind = state; }
                if (state is not ("question" or "lesson" or "overview"))
                {
                    var entry = $"{DateTime.Now.ToString("MMM d, yyyy, h:mm:ss tt", CultureInfo.CurrentCulture)} — Alta’s page shown as it is: {Str(head["reason"]) ?? "not recognised"} [{state}]";
                    if (head["shape"] is JsonObject shape) entry += "\n" + shape.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                    ErrorLogs.Add(entry);
                    if (ErrorLogs.Count > 50) ErrorLogs.RemoveAt(0);
                }
                PaintPanels();
                Notify();
                return;
            }
            case "shown":
                if (!main) return;
                PageShown = true;
                // (0.3.21: Alta's page shown — not the school's launch page on the way to it, shown as it is)
                if (shownAt == null && Str(head["how"]) is { } how && how != "not alta")
                {
                    shownAt = Now();
                    shownHow = how;
                    shownOwn = Num(head["t"]);
                    shownQuiet = Num(head["quiet"]);
                    shownCapped = Bool(head["capped"]) == true;
                    SayTimeline();
                }
                Notify();
                return;
            case "hidden":
                if (!main) return;
                PageShown = false;
                ClosePic(); // (Alta between parts: the question gone)
                WatchSkeleton();
                Notify();
                return;
            case "remeasured":
            {
                // (maths drawn whole) MathQuill's brackets or √ found drawn flat — drawn out of sight — and sized again
                if (!main) return;
                var n = (int)(Num(head["count"]) ?? 0);
                ErrorLogs.Add($"{DateTime.Now.ToString("MMM d, yyyy, h:mm:ss tt", CultureInfo.CurrentCulture)} — Alta’s maths drawn flat ({n} bracket{(n == 1 ? "" : "s")} or roots), sized again{(Bool(head["mathquill"]) == true ? " by MathQuill" : "")}");
                if (ErrorLogs.Count > 50) ErrorLogs.RemoveAt(0);
                Notify();
                return;
            }
            case "picOpen":
                if (main && Num(head["i"]) is { } i) _ = OpenPic((int)i);
                return;
            case "cover":
            {
                if (!main) return;
                if (Bool(head["none"]) == true)
                {
                    if (Cover != null) { Cover = null; Notify(); }
                    return;
                }
                if (Read<AltaCover>(head) is not { } c) return;
                // (0.3.21: an overview that is the assignment's cover is told as the cover, when it was first read)
                if (partAt == null || partKind == "overview") { partAt ??= Now(); partKind = "cover"; }
                Cover = c;
                // (1.3.60) the cover's list is the whole of the assignment's objectives, every one named
                Keep(c.List);
                Notify();
                return;
            }
            case "overview":
            {
                if (Read<AltaOverview>(head) is not { } o) return;
                var renamed = false;
                foreach (var x in o.Objectives)
                    if (!string.IsNullOrEmpty(x.Id) && !string.IsNullOrEmpty(x.Name) && (!names.TryGetValue(x.Id!, out var had) || had != x.Name)) { names[x.Id!] = x.Name!; renamed = true; }
                if (renamed) Prefs.Set(NamesKey, JsonSerializer.Serialize(names));
                Overview = Merge(Overview, o);
                Hear(o.Percent is { } pc ? pc / 100 : o.Progress, o.Targets ?? new());
                Keep(o.List);
                Notify();
                return;
            }
            case "content":
            {
                if (Read<AltaReport>(head) is not { } r) return;
                // (1.3.34) an answer that names only the objective on screen: the assignment's objectives kept from the last
                if (Report is { } old)
                {
                    if (r.Objectives.Count == 0) r.Objectives = old.Objectives;
                    if (r.Targets.Count == 0) r.Targets = old.Targets;
                    r.Name ??= old.Name;
                    r.Due ??= old.Due;
                    r.Threshold ??= old.Threshold;
                }
                Remember(r);
                Report = r;
                Hear(r.Percent is { } pc ? pc / 100 : r.Progress, r.Targets);
                Notify();
                return;
            }
            case "meter":
            {
                // (1.3.21) Alta's page's own mastery bars: the whole's, and each objective's by its name
                var percent = Num(head["percent"]);
                var found = new List<AltaTarget>();
                var named = Objectives;
                foreach (var b in (head["bars"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                {
                    if (Str(b["label"]) is not { Length: > 0 } label || Num(b["value"]) is not { } value) continue;
                    var o = named.FirstOrDefault(o =>
                    {
                        var n = o.Name.ToLowerInvariant();
                        return !o.Name.StartsWith("Objective ", StringComparison.Ordinal) && n.Length > 3 && (label.Contains(n) || n.Contains(label));
                    });
                    if (o != null) found.Add(new AltaTarget { Id = o.Id, Progress = value });
                }
                Hear(percent is { } pc ? pc / 100 : null, found);
                Notify();
                return;
            }
        }
    }

    private static bool SameFrame(CoreWebView2Frame a, CoreWebView2Frame b)
    {
        try { return a.FrameId == b.FrameId; } catch { return ReferenceEquals(a, b); }
    }

    // ---- reading the page's words ----

    private static string? Str(JsonNode? n)
    {
        try { return n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null; } catch { return null; }
    }

    private static double? Num(JsonNode? n)
    {
        try
        {
            if (n is not JsonValue v) return null;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return p;
        }
        catch { }
        return null;
    }

    private static bool? Bool(JsonNode? n)
    {
        try { return n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null; } catch { return null; }
    }

    private static Rect? Box(JsonObject p)
    {
        if (Num(p["x"]) is not { } x || Num(p["y"]) is not { } y || Num(p["w"]) is not { } w || Num(p["h"]) is not { } h || w <= 0 || h <= 0) return null;
        return new Rect(x, y, w, h);
    }

    private static T? Read<T>(JsonObject o) where T : class
    {
        try { return o.Deserialize<T>(Json.Options); } catch { return null; }
    }

    private static T? ReadJson<T>(string key) where T : class
    {
        try { return Prefs.String(key) is { Length: > 0 } s ? JsonSerializer.Deserialize<T>(s, Json.Options) : null; } catch { return null; }
    }

    // ---- what is known of the objectives ----

    private void Keep(List<AltaKnown> list)
    {
        if (list.Count == 0 || list.Count < Known.Count || list.SequenceEqual(Known)) return;
        Known = list;
        Prefs.Set(KnownKey, JsonSerializer.Serialize(list, Json.Script));
    }

    private static bool Same(string? a, string? b)
    {
        if (a == null || b == null) return false;
        var x = a.Trim().ToLowerInvariant();
        return x.Length > 0 && x == b.Trim().ToLowerInvariant();
    }

    /// <summary>(1.3.23) Alta's estimate of the questions the objective being worked on takes, as its overview says it
    /// ("Estimated 4 - 9 questions"), else as the question's answer has it.</summary>
    public (double Low, double High)? Estimate
    {
        get
        {
            if (Report?.Current is not { } c) return null;
            if (Known.FirstOrDefault(o => o.Id == c.Id || Same(o.Name, c.Name)) is { Low: { } l, High: { } h } && h > 0) return (l, h);
            if (c.Low is { } l2 && c.High is { } h2 && h2 > 0) return (l2, h2);
            return null;
        }
    }

    /// <summary>A newer overview kept over the last, its blanks filled from the one before.</summary>
    private static AltaOverview Merge(AltaOverview? old, AltaOverview now)
    {
        if (old == null) return now;
        return new AltaOverview
        {
            Name = now.Name ?? old.Name, Due = now.Due ?? old.Due, DueText = now.DueText ?? old.DueText, StatusText = now.StatusText ?? old.StatusText,
            Threshold = now.Threshold ?? old.Threshold, Percent = now.Percent ?? old.Percent, Progress = now.Progress ?? old.Progress,
            Targets = now.Targets ?? old.Targets, Status = now.Status ?? old.Status, Started = now.Started ?? old.Started,
            Completed = now.Completed ?? old.Completed, Objectives = now.Objectives.Count == 0 ? old.Objectives : now.Objectives,
        };
    }

    /// <summary>(1.3.21) Mastery heard: the newest word on it kept, whichever of Alta's answers or bars it came in (as a
    /// fraction, or as Alta's percent).</summary>
    private void Hear(double? percent, IEnumerable<AltaTarget> targets)
    {
        static double Unit(double p) => Math.Clamp(p > 1 ? p / 100 : p, 0, 1);
        if (percent is { } p) LivePercent = Unit(p);
        foreach (var t in targets)
        {
            if (string.IsNullOrEmpty(t.Id)) continue;
            LiveTargets.TryGetValue(t.Id!, out var before);
            LiveTargets[t.Id!] = new AltaTarget { Id = t.Id, Progress = t.Progress is { } v ? Unit(v) : null, Status = t.Status ?? before?.Status };
        }
    }

    private void Remember(AltaReport r)
    {
        if (r.Current.Id is not { } id || r.Current.Name is not { Length: > 0 } name || (names.TryGetValue(id, out var had) && had == name)) return;
        names[id] = name;
        if (names.Count > 400) foreach (var k in names.Keys.Take(names.Count - 300).ToList()) names.Remove(k);
        Prefs.Set(NamesKey, JsonSerializer.Serialize(names));
    }

    /// <summary>The objectives in Alta's order (the assignment's list, else the progress targets, else the one on
    /// screen), each with its mastery: its target's progress, or full once its status says it is mastered.</summary>
    public List<AltaObjective> Objectives
    {
        get
        {
            if (Report is not { } r)
            {
                // (1.3.60) on the cover: its objectives, all of them, each as mastered as the cover says
                if (Cover is { } c && c.List.Count > 0)
                {
                    var levels = c.Topics.SelectMany(t => t.Objectives.Select(o => o.Mastery)).ToList();
                    return c.List.Select((o, k) => new AltaObjective(o.Id, k + 1, o.Name, Math.Clamp(levels[k] ?? 0, 0, 1), false)).ToList();
                }
                // (before a question: the overview's objectives, each as mastered as the whole is said to be when it is done)
                var done = Overview?.Completed == true;
                return (Overview?.List ?? new()).Select((o, k) => new AltaObjective(o.Id, k + 1, o.Name, done ? 1 : Level(LiveTargets.GetValueOrDefault(o.Id)), false)).ToList();
            }
            var ids = r.Objectives.Select(o => o.Id).OfType<string>().ToList();
            if (ids.Count == 0) ids = r.Targets.Select(t => t.Id).OfType<string>().ToList();
            if (ids.Count == 0 && r.Current.Id is { } only) ids = new() { only };
            ids = ids.Distinct().ToList();
            var here = r.Current.Id is { } cid ? ids.IndexOf(cid) : -1;
            var there = Known.FindIndex(o => Same(o.Name, r.Current.Name));
            var aligned = Known.Count == ids.Count || (here >= 0 && here == there);
            return ids.Select((id, k) =>
            {
                // (1.3.21) the newest word on it, from any of Alta's answers or its page's bars, over the question's
                var p = Level(LiveTargets.GetValueOrDefault(id) ?? r.Targets.FirstOrDefault(t => t.Id == id));
                // (1.3.20) a name from the overview by its place when Alta's answers name only the objective on screen —
                // (1.3.23) the overview's list lined up by the objective on screen when the two lists differ in length
                var byPlace = aligned && k < Known.Count ? Known[k].Name : null;
                var byId = Known.FirstOrDefault(o => o.Id == id)?.Name;
                var named = r.Objectives.FirstOrDefault(o => o.Id == id)?.Name ?? (r.Current.Id == id ? r.Current.Name : null) ?? names.GetValueOrDefault(id) ?? byId ?? byPlace;
                return new AltaObjective(id, k + 1, named ?? $"Objective {k + 1}", p, r.Current.Id == id);
            }).ToList();
        }
    }

    /// <summary>An objective's mastery, 0…1: its progress, or full once its status says it is mastered.</summary>
    private static double Level(AltaTarget? t)
    {
        if ((t?.Status ?? "").ToLowerInvariant() is "complete" or "completed" or "mastered" or "done") return 1;
        return Math.Clamp(t?.Progress ?? 0, 0, 1);
    }

    /// <summary>The whole assignment's mastery, 0…1.</summary>
    public double Mastery
    {
        get
        {
            if (Report is not { } r)
            {
                if (Overview?.Completed == true) return 1;
                if (LivePercent is { } lp) return lp;
                if (Overview?.Percent is { } op) return Math.Clamp(op / 100, 0, 1);
                // (no overall figure yet — the start screen, before Alta's report: the mean of the objectives the rail
                // shows, never a 0% over "1 of 3 objectives mastered")
                var shown = Objectives;
                return shown.Count == 0 ? 0 : shown.Average(o => o.Mastery);
            }
            if (r.Completed || (r.Status ?? "").ToLowerInvariant() == "complete") return 1;
            if (LivePercent is { } p) return p;
            if (r.Percent is { } pc) return Math.Clamp(pc / 100, 0, 1);
            if (r.Progress is { } pr) return Math.Clamp(pr, 0, 1);
            var list = Objectives;
            return list.Count == 0 ? 0 : list.Average(o => o.Mastery);
        }
    }

    // ---- pressing Alta's own ----

    /// <summary>A script in Alta's page (an expression); what it gives back, or null.</summary>
    private async Task<JsonNode?> Run(string js)
    {
        if (closed || Web.CoreWebView2 is not { } core) return null;
        try { return JsonNode.Parse(await core.ExecuteScriptAsync(js)); } catch { return null; }
    }

    /// <summary>The cover's Start (or Continue, Resume), pressed in Alta's page.</summary>
    public void StartCover() => _ = Run("window.__simplCover ? window.__simplCover.start() : null");

    /// <summary>The cover's Review Instruction, pressed in Alta's page.</summary>
    public void ReviewCover() => _ = Run("window.__simplCover ? window.__simplCover.review() : null");

    public void PressButton(int index)
    {
        // (1.3.70: the question answered — Submit, Continue — its graph's window put away)
        if (Buttons.FirstOrDefault(b => b.Id == index)?.Primary == true) ClosePic();
        _ = Run($"window.__simplButtons ? window.__simplButtons.press({index}) : null");
    }

    /// <summary>One of the pop-up's buttons, pressed in Alta's own.</summary>
    public void PressDialog(int index) => _ = Run($"window.__simplDialog ? window.__simplDialog.press({index}) : null");

    /// <summary>(0.3.5) A link in the pop-up's body, followed as Alta's page follows it (the page's own link pressed).</summary>
    public void DialogLink(string href) => _ = Run($"window.__simplDialog ? window.__simplDialog.link({Json.Str(href)}) : null");

    /// <summary>The app's dialog put away without a button (Escape with no way out of Alta's, Show in Alta's page): Alta's
    /// pop-up given back to the page.</summary>
    public void DialogDismissed() => GiveBack(null, null);

    /// <summary>
    /// (0.3.5) Alta's pop-up shown in its page as Alta draws it, the page in sight under it, until it closes: one the app
    /// cannot show as its own — what it holds cannot be drawn (a video, a form, another site's frame), or nothing in its
    /// body could be read (<paramref name="why"/>, written in the Error Logs with what was read of it) — never shown as an
    /// empty dialog; or one the student asked to see there.
    /// </summary>
    private void GiveBack(string? why, AltaDialog? read)
    {
        Dialog = null;
        DialogInPage = true;
        DialogVersion++;
        _ = Run("window.__simplDialog ? window.__simplDialog.giveBack() : null");
        if (why != null)
        {
            var entry = $"{DateTime.Now.ToString("MMM d, yyyy, h:mm:ss tt", CultureInfo.CurrentCulture)} — a pop-up of Alta’s shown in its page: {why}";
            if (read != null)
                entry += $"\n  title \"{read.Title}\", {read.Words} words, {read.Maths} formulas, {read.Pics} pictures, buttons {string.Join(" | ", read.Buttons.Select(b => b.Label))} ({read.Ready})";
            ErrorLogs.Add(entry);
            if (ErrorLogs.Count > 50) ErrorLogs.RemoveAt(0);
            if (Shot.Asked) Log.Write("alta: a pop-up shown in Alta's page: " + why);
        }
        Notify();
    }

    /// <summary>(0.3.5) A picture waits while a pop-up of Alta's settles in the page (twelve seconds at most).</summary>
    private void HoldForDialog(bool on)
    {
        if (on == dialogHeld) return;
        dialogHeld = on;
        dialogHoldLimit?.Stop();
        dialogHoldLimit = null;
        if (!on) { Shot.Ended(); return; }
        Shot.Began();
        dialogHoldLimit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        dialogHoldLimit.Tick += (_, _) => HoldForDialog(false);
        dialogHoldLimit.Start();
    }

    /// <summary>What the page shows now, cropped to a part of it (the page's own coordinates in its view).</summary>
    private async Task<BitmapSource?> Preview(Rect rect, double dpr)
    {
        if (Web.CoreWebView2 is not { } core) return null;
        try
        {
            using var stream = new MemoryStream();
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            if (Decode(stream.ToArray()) is not { } whole) return null;
            var at = new Int32Rect((int)Math.Max(0, rect.X * dpr), (int)Math.Max(0, rect.Y * dpr), 0, 0);
            at.Width = (int)Math.Min(rect.Width * dpr, whole.PixelWidth - at.X);
            at.Height = (int)Math.Min(rect.Height * dpr, whole.PixelHeight - at.Y);
            if (at.Width <= 4 || at.Height <= 4) return null;
            var cut = new CroppedBitmap(whole, at);
            cut.Freeze();
            return cut;
        }
        catch { return null; }
    }

    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.StreamSource = new MemoryStream(bytes);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch { return null; }
    }

    // ---- (1.3.70) the question's graphs: small, opened large in a window of their own ----

    public void SetPicsLight(bool on)
    {
        PicsLight = on;
        Prefs.Set(PicsLightKey, on);
        AltaGraphWindow.Light(on);
        Appearance();
        Notify();
        if (picOpen is { } i) _ = Reopen(i);
    }

    private async Task Reopen(int i)
    {
        await Task.Delay(120);
        await OpenPic(i);
    }

    /// <summary>The graph, large, in its window: a picture of it as the page draws it now, at its own size (drawn afresh
    /// at that scale, so it is sharp).</summary>
    public async Task OpenPic(int i)
    {
        if (await Run($"(function () {{ var r = window.__simplPic ? window.__simplPic.rect({i}) : null; if (r) {{ r.sx = window.scrollX; r.sy = window.scrollY; r.vw = window.innerWidth; r.vh = window.innerHeight; r.dpr = window.devicePixelRatio || 1; }} return r; }})()") is not JsonObject r
            || Num(r["x"]) is not { } x || Num(r["y"]) is not { } y || Num(r["w"]) is not { } w || Num(r["h"]) is not { } h || w <= 4 || h <= 4) return;
        var z = Math.Max(0.05, Num(r["z"]) ?? 1);
        var rect = Rect.Intersect(new Rect(x - 4, y - 4, w + 8, h + 8), new Rect(0, 0, Num(r["vw"]) ?? 4000, Num(r["vh"]) ?? 4000));
        if (rect.IsEmpty || rect.Width <= 1 || rect.Height <= 1) return;
        var image = await Snapshot(rect, Num(r["sx"]) ?? 0, Num(r["sy"]) ?? 0, 1 / z) ?? await Preview(rect, Num(r["dpr"]) ?? 1);
        if (image == null || closed) return;
        picOpen = i;
        AltaGraphWindow.Show(image, new Size(rect.Width / z, rect.Height / z), Launch.Title, this, owner);
    }

    /// <summary>Part of the page drawn afresh at a scale (the browser's own picture of it, through its DevTools).</summary>
    private async Task<BitmapSource?> Snapshot(Rect rect, double scrollX, double scrollY, double scale)
    {
        if (Web.CoreWebView2 is not { } core) return null;
        try
        {
            var args = new JsonObject
            {
                ["format"] = "png", ["fromSurface"] = true, ["captureBeyondViewport"] = false,
                ["clip"] = new JsonObject { ["x"] = rect.X + scrollX, ["y"] = rect.Y + scrollY, ["width"] = rect.Width, ["height"] = rect.Height, ["scale"] = scale },
            };
            var call = core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", args.ToJsonString());
            if (await Task.WhenAny(call, Task.Delay(4000)) != call) return null;
            return Str(JsonNode.Parse(await call)?["data"]) is { } data ? Decode(Convert.FromBase64String(data)) : null;
        }
        catch { return null; }
    }

    /// <summary>The question answered or gone: its graph's window goes with it.</summary>
    public void ClosePic()
    {
        picOpen = null;
        AltaGraphWindow.CloseIt();
    }

    public void PicWindowClosed() => picOpen = null;

    // ---- the panels behind the question's parts ----

    /// <summary>
    /// (1.3.53, 1.3.59) A panel behind each part where the page lays it out — every panel as wide as the widest, one
    /// column of them, each as tall as its own content. The Mac lays glass behind its see-through web view; a WebView2
    /// is a window of its own that nothing can be drawn behind, so the app has the page draw them, in Windows 11's card
    /// colours, under everything of Alta's — scrolling with the page, as the Mac's scroll with it.
    /// </summary>
    private void PaintPanels()
    {
        var list = Split ? Panels : new List<AltaPanel>();
        string at = "[]";
        if (list.Count > 0)
        {
            var left = list.Min(p => p.Rect.Left);
            var right = list.Max(p => p.Rect.Right);
            at = Json.Write(list.Select(p => new { x = Math.Round(left - 16), y = Math.Round(p.Rect.Top - 16), w = Math.Round(right - left + 32), h = Math.Round(p.Rect.Height + 32) }).ToList());
        }
        var js = $"({PanelsScript})({at}, {Json.Str(AltaLook.Css(Kit.B.Card, Region))}, {Json.Str(AltaLook.Css(Kit.B.CardStroke, Region))})";
        if (js == lastPaint) return;
        lastPaint = js;
        _ = Run(js);
    }

    private const string PanelsScript = """
        function (list, fill, edge) {
          var host = document.getElementById('simpl-panels');
          if (!list.length) { if (host) host.remove(); return null; }
          if (!host) {
            host = document.createElement('div');
            host.id = 'simpl-panels';
            host.setAttribute('aria-hidden', 'true');
            host.style.cssText = 'position:absolute;left:0;top:0;width:0;height:0;z-index:-1;pointer-events:none;';
            document.documentElement.appendChild(host);
          }
          while (host.children.length > list.length) host.removeChild(host.lastChild);
          while (host.children.length < list.length) host.appendChild(document.createElement('div'));
          list.forEach(function (p, i) {
            host.children[i].style.cssText = 'position:absolute;box-sizing:border-box;left:' + p.x + 'px;top:' + p.y + 'px;width:' + p.w + 'px;height:' + p.h
              + 'px;border-radius:8px;background:' + fill + ';border:1px solid ' + edge + ';';
          });
          return null;
        }
        """;

    // ---- (1.3.46) Save Page Code ----

    /// <summary>(1.3.47) The Error Logs as text, to copy.</summary>
    public string ErrorLogText => string.Join("\n\n", ErrorLogs);

    public void ClearErrorLogs()
    {
        ErrorLogs.Clear();
        Notify();
    }

    /// <summary>
    /// Every bit of code of Alta's page as it is now — the page and each frame in it: its HTML (with any web component's
    /// own tree), and the scripts and style sheets it loads — in one text file in Downloads (the Error Logs first, when
    /// saved from them). Addresses are written without their queries (a launch's tokens ride in them).
    /// </summary>
    public async Task<string?> SavePageCode(bool withLogs)
    {
        var line = new string('=', 100);
        var out_ = new StringBuilder($"Simpl for Windows {Updates.Version}: Knewton Alta page code, saved {DateTime.Now.ToString("F", CultureInfo.CurrentCulture)}\n");
        if (withLogs)
        {
            out_.Append($"\n{line}\nERROR LOGS ({ErrorLogs.Count})\n{line}\n");
            out_.Append(ErrorLogText).Append('\n');
        }
        var seen = new HashSet<string>();
        var count = 0;
        var targets = new List<CoreWebView2Frame?> { null };
        targets.AddRange(frames);
        foreach (var f in targets)
        {
            if (await Collect(f) is not JsonObject r) continue;
            var url = Str(r["url"]) ?? "?";
            var html = Str(r["html"]) ?? "";
            if (!seen.Add(url + "#" + html.Length)) continue;
            count++;
            out_.Append($"\n\n{line}\n");
            out_.Append($"PAGE {count}{(f == null ? " (the popup's page)" : " (a frame in it)")}: {url}\n");
            out_.Append($"Title: {Str(r["title"]) ?? ""}\n{line}\n");
            out_.Append("\n---- HTML ----\n").Append(html).Append('\n');
            foreach (var sh in (r["shadows"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                out_.Append($"\n---- WEB COMPONENT'S OWN TREE: {Str(sh["host"]) ?? ""} ----\n").Append(Str(sh["html"]) ?? "").Append('\n');
            foreach (var file in (r["files"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                out_.Append($"\n---- {(Str(file["kind"]) ?? "file").ToUpperInvariant()}: {Str(file["url"]) ?? ""} ----\n");
                if (Str(file["text"]) is { } t) out_.Append(t).Append('\n');
                else out_.Append($"(could not be read: {Str(file["error"]) ?? "unknown"})\n");
            }
        }
        if (count == 0) return null;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd 'at' HH.mm.ss", CultureInfo.InvariantCulture);
        try
        {
            var dir = Downloads();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"Simpl Alta {(withLogs ? "Error Logs" : "Code")} {stamp}.txt");
            await File.WriteAllTextAsync(path, out_.ToString());
            return path;
        }
        catch { return null; }
    }

    /// <summary>The page-code script run in a frame (null: the page itself), its answer passed back as a message.</summary>
    private async Task<JsonNode?> Collect(CoreWebView2Frame? frame)
    {
        if (closed || Web.CoreWebView2 is not { } core) return null;
        var token = Guid.NewGuid().ToString("N");
        var done = new TaskCompletionSource<JsonNode?>();
        codeAsked[token] = done;
        var js = "(async function () {\n" + AltaHook.Collect + "\n})().then(function (r) { window.chrome.webview.postMessage(JSON.stringify({ kind: 'code', token: '" + token
                 + "', result: r })); }, function () { window.chrome.webview.postMessage(JSON.stringify({ kind: 'code', token: '" + token + "', result: null })); }); null";
        try
        {
            if (frame == null) await core.ExecuteScriptAsync(js);
            else if (frame.IsDestroyed() == 0) await frame.ExecuteScriptAsync(js);
            else return null;
            var finished = await Task.WhenAny(done.Task, Task.Delay(40_000));
            return finished == done.Task ? done.Task.Result : null;
        }
        catch { return null; }
        finally { codeAsked.Remove(token); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>The student's Downloads folder (wherever Windows keeps it).</summary>
    public static string Downloads()
    {
        try
        {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0)
            {
                var s = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}

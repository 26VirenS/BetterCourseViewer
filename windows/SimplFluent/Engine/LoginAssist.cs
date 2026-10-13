using System.Text.Json;
using System.Text.Json.Nodes;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>
/// The app's side of signing in (the Mac's LoginAssist). Web/login.js, on every page the engine's web view opens, says
/// what a page asks for; this decides what covers the web view and when details go into a page:
///
/// - a sign-in form the reader can fill, and nothing saved for it: the app's own form (LoginLayer) over the page. What
///   is typed there goes into the page's own fields and its own button is pressed.
/// - the saved sign-in page coming up: "Logging you in" from its first frame, the saved details filled and sent; a
///   two-step page gets the username, then the password on the next step.
///   A saved password locked with Windows Hello is asked for first (LoginVault.UnlockAsync), under that cover: the
///   prompt cancelled, nothing is filled and the school's page is shown, to sign in by hand; Windows Hello's key gone,
///   the saved sign-in is forgotten and the app's form comes up with the username in it.
/// - a page with nothing to fill after that (a two-factor prompt, a consent page): the cover lifts and the page is
///   shown, to be finished by hand (LoginOverlay's note says so); the sign-in carries on from there.
/// - the sign-in form again after a press (a wrong password): the form, saying so. Saved details that failed are not
///   tried again until new ones are given.
/// - Canvas reached after details typed in the form: "Stay logged in?". Reached with saved details an earlier Simpl
///   kept in the vault alone: they are locked with Windows Hello now, where it is set up (LoginVault.LockAtUseAsync).
/// - a school page that says the sign-in went stale ("Stale Request", an expired request): started again from Canvas
///   by itself, once a minute at most; and on every school page shown, Start Over does the same by hand.
///
/// The details are kept, when the student says so, encrypted in Simpl's own vault and locked with Windows Hello
/// (LoginVault) — never by the browser, whose own password saving is off. Nothing here writes them down anywhere else,
/// the log included.
/// </summary>
public sealed class LoginAssist : Observable
{
    public enum Mode { Idle, Capture, Working }

    private readonly Engine engine;
    private readonly string canvasHost;

    private Mode phase = Mode.Idle;
    /// <summary>What covers the page: nothing, the app's own form, or "Logging you in".</summary>
    public Mode Phase { get => phase; private set => Set(ref phase, value); }
    private string captureHost = "";
    /// <summary>The form's page: the host it signs in at.</summary>
    public string CaptureHost { get => captureHost; private set => Set(ref captureHost, value); }
    private string? captureError;
    /// <summary>What the form says went wrong (a wrong password), or null.</summary>
    public string? CaptureError { get => captureError; private set => Set(ref captureError, value); }
    private bool askStay;
    /// <summary>"Stay logged in?" is up.</summary>
    public bool AskStay { get => askStay; private set => Set(ref askStay, value); }
    private bool handoff;
    /// <summary>The school's page is shown in the middle of a sign-in, for the student to do a step of it (a two-factor
    /// code, a consent page, a page the reader could not fill, or one they asked to see): a note over it says so, and the
    /// sign-in carries on once Canvas is reached.</summary>
    public bool Handoff { get => handoff; set => Set(ref handoff, value); }
    private bool onSchoolPage;
    /// <summary>The web view is on the school's sign-in (another host, or Canvas's own /login): Start Over is offered there.</summary>
    public bool OnSchoolPage { get => onSchoolPage; private set => Set(ref onSchoolPage, value); }
    private string prefillUser = "";
    public string PrefillUser { get => prefillUser; set => Set(ref prefillUser, value); }

    /// <summary>Starts the sign-in again from Canvas (a fresh load of Canvas, which sends the school a new request).</summary>
    public Action? OnRestart;

    private sealed record Attempt(string Host, string Step, DateTime At, bool Auto, string User, string Pass);
    private Attempt? attempt;
    private bool navigatedSinceAttempt;
    /// <summary>Details typed in the form, until "Stay logged in?" is answered.</summary>
    private (string User, string Pass, string Host)? typed;
    private bool autoOff, declined;
    /// <summary>The saved password is being asked of Windows Hello: one request at a time, and the watchdog waits.</summary>
    private bool unlocking;
    private string kindHere = "full";
    private CancellationTokenSource? watchdog;
    private DateTime? lastAutoRestart;

    public LoginAssist(string canvasHost, Engine engine)
    {
        this.canvasHost = canvasHost.ToLowerInvariant();
        this.engine = engine;
    }

    public bool IsCapture => Phase == Mode.Capture;
    /// <summary>The app's own cover is up over the page: the form, or "Logging you in".</summary>
    public bool Covers => Phase != Mode.Idle;
    /// <summary>Nothing of the sign-in on screen or still to come: no cover, no form, no "Stay logged in?" asked or
    /// about to be (details typed in the form are still on their way). The app's own popups wait for this.</summary>
    public bool Settled => Phase == Mode.Idle && !AskStay && typed == null;

    /// <summary>The school's own sign-in on its own host: Canvas's /login (and /login/ldap…), Brightspace's /d2l/login.</summary>
    public static bool IsSignIn(string path) => Engine.IsSignIn(path);

    /// <summary>This PC itself (the mock, in development): as good as https — nothing leaves the machine.</summary>
    private static bool Loopback(string host) => host is "localhost" or "127.0.0.1" or "[::1]";

    // MARK: - What the reader says

    /// <summary>A message from login.js, with the page's own address as WebView2 knows it — not what the script says it is.</summary>
    public void Said(JsonObject body, string? sourceUri)
    {
        var kind = body["kind"]?.ToString();
        if (kind == null) return;
        if (!Uri.TryCreate(sourceUri ?? "", UriKind.Absolute, out var origin)) return;
        var host = origin.Host.ToLowerInvariant();
        var secure = origin.Scheme == "https" || Loopback(host);
        var error = body["error"]?.ToString();
        if (string.IsNullOrEmpty(error)) error = null;
        var stale = body["stale"] is JsonValue sv && sv.TryGetValue<bool>(out var s) && s;
        if (stale && host != canvasHost) { StaleSeen(); return; }
        Handle(kind, host, error, secure);
    }

    /// <summary>The school's page says the request went stale: start again by itself — but not twice within a minute,
    /// so a page that keeps saying it cannot send the app round in a loop; then it stays shown, with Start Over on it.</summary>
    private void StaleSeen()
    {
        if (lastAutoRestart is { } last && (DateTime.UtcNow - last).TotalSeconds < 60)
        {
            if (Phase == Mode.Working) Reveal();
            return;
        }
        lastAutoRestart = DateTime.UtcNow;
        Restart();
    }

    /// <summary>Start Over: whatever sign-in was under way forgotten, and Canvas loaded afresh (it sends the school a new
    /// request); saved details are tried again on the way.</summary>
    public void Restart()
    {
        Disarm();
        attempt = null;
        navigatedSinceAttempt = false;
        autoOff = false;
        declined = false;
        Handoff = false;
        if (Phase != Mode.Idle) Phase = Mode.Idle;
        OnRestart?.Invoke();
    }

    private void Handle(string kind, string host, string? error, bool secure)
    {
        if (kind == "none")
        {
            // a page with nothing to fill: two-factor, a consent page, a hop — shown, not covered
            if (Phase == Mode.Working) HandOver();
            return;
        }
        if (!secure) return; // never on a page that is not https
        kindHere = kind;
        // after a press: the next step, the page still busy with it, or the sign-in again (it did not work)
        if (attempt is { } a && a.Host == host && (DateTime.UtcNow - a.At).TotalSeconds < 30)
        {
            if (a.Step == "user" && kind == "pass") { Fill(a.User, a.Pass, host, kind, a.Auto); return; }
            if (navigatedSinceAttempt || error != null) Failed(host, error, a.Auto);
            return;
        }
        var saved = LoginVault.Load();
        if (saved != null && saved.Host == host && !autoOff) { FillSaved(saved, host); return; }
        if (declined)
        {
            if (Phase == Mode.Working) Reveal();
            return;
        }
        if (PrefillUser.Length == 0) PrefillUser = typed?.User ?? saved?.User ?? "";
        Disarm();
        Handoff = false;
        Capture(host, null);
    }

    private void Capture(string host, string? error)
    {
        CaptureHost = host;
        CaptureError = error;
        Phase = Mode.Capture;
    }

    // MARK: - Filling

    /// <summary>
    /// The saved sign-in's page: its password asked for — Windows Hello asks, for one locked with it — under "Logging you
    /// in", then filled and sent. Cancelled (or Windows could not): nothing filled, and the school's page shown to sign
    /// in by hand, the saved details not tried again until Simpl starts over. Its Windows Hello key gone: forgotten, and
    /// the app's form with the username in it ("Stay logged in?" keeps it again after).
    /// </summary>
    private async void FillSaved(SavedLogin saved, string host)
    {
        if (unlocking) return; // (the page said so twice while Windows Hello asks: once is enough)
        unlocking = true;
        Phase = Mode.Working;
        var before = attempt;
        string? pass;
        try { pass = await LoginVault.UnlockAsync("Unlock your saved sign-in with Windows Hello"); }
        finally { unlocking = false; }
        // (Start Over, Show the Page, or details filled meanwhile: what was asked for is not filled in now)
        if (Phase != Mode.Working || !ReferenceEquals(attempt, before)) return;
        if (pass != null) { Fill(saved.User, pass, host, kindHere, auto: true); return; }
        if (LoginVault.Load() == null)
        {
            PrefillUser = saved.User;
            Disarm();
            Handoff = false;
            Capture(host, null);
            return;
        }
        autoOff = true;
        HandOver();
    }

    private async void Fill(string user, string pass, string host, string step, bool auto)
    {
        var core = engine.Web.CoreWebView2;
        if (core == null || !string.Equals(HostOf(core.Source), host, StringComparison.OrdinalIgnoreCase)) { Reveal(); return; }
        Phase = Mode.Working;
        attempt = new Attempt(host, step, DateTime.UtcNow, auto, user, pass);
        navigatedSinceAttempt = false;
        Arm(25);
        string result;
        try { result = await core.ExecuteScriptAsync("window.__simplLogin?window.__simplLogin.fill(" + Json.Literal(new { user, pass }) + "):null"); }
        catch { HandOver(); return; }
        try
        {
            if (string.IsNullOrEmpty(result) || result == "null") { HandOver(); return; } // (no reader on the page)
            using var doc = JsonDocument.Parse(result);
            // nothing to press: the page is shown with the details in it, to be sent by hand
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("pressed", out var p) && p.ValueKind == JsonValueKind.False) HandOver();
        }
        catch { HandOver(); }
    }

    private void Failed(string host, string? error, bool auto)
    {
        attempt = null;
        if (auto) autoOff = true;
        var saved = LoginVault.Load();
        PrefillUser = typed?.User ?? saved?.User ?? PrefillUser;
        typed = null;
        Disarm();
        Handoff = false;
        Capture(host, error ?? "That username or password didn’t work. Try again.");
    }

    /// <summary>Lift whatever covers the page: the page itself is what the student needs now.</summary>
    public void Reveal()
    {
        Disarm();
        if (Phase != Mode.Idle) Phase = Mode.Idle;
    }

    /// <summary>The page shown in the middle of a sign-in, with the note that says what to do on it.</summary>
    private void HandOver()
    {
        if (Phase == Mode.Working) Handoff = true;
        Reveal();
    }

    /// <summary>"Show the Page", pressed under "Logging you in": the school's page, to do what it asks by hand.</summary>
    public void ShowPage()
    {
        Handoff = true;
        Reveal();
    }

    private void Disarm()
    {
        watchdog?.Cancel();
        watchdog = null;
    }

    private async void Arm(double seconds)
    {
        watchdog?.Cancel();
        var cts = watchdog = new CancellationTokenSource();
        try { await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token); }
        catch (OperationCanceledException) { return; }
        if (cts != watchdog) return;
        // (a page still on its way, or Windows Hello still asking for the saved password: a little longer)
        if (engine.Loading || unlocking) { Arm(2); return; }
        if (Phase == Mode.Working) HandOver();
    }

    // MARK: - The form and the question after

    /// <summary>The details typed in the app's own form: into the page's own fields, its button pressed.</summary>
    public void Submit(string user, string pass)
    {
        if (Phase != Mode.Capture) return;
        var host = CaptureHost;
        typed = (user, pass, host);
        autoOff = false;
        Fill(user, pass, host, kindHere, auto: false);
    }

    /// <summary>"Use the website instead": the page as it is, and the form not offered again until the app is opened again.</summary>
    public void UseWebsite()
    {
        declined = true;
        Reveal();
    }

    /// <summary>The answer to "Stay logged in?": kept, locked with Windows Hello where it is set up (it asks); the app's
    /// own popups wait until that is done.</summary>
    public async void Stay(bool yes)
    {
        AskStay = false;
        if (yes && typed is { } t)
        {
            autoOff = false;
            await LoginVault.SaveAsync(t.Host, t.User, t.Pass);
        }
        typed = null;
        Changed(nameof(Settled));
    }

    /// <summary>Signed out: nothing held in memory either.</summary>
    public void Forget()
    {
        typed = null;
        attempt = null;
        autoOff = false;
        PrefillUser = "";
        Handoff = false;
        Reveal();
    }

    // MARK: - The web view's moves

    /// <summary>A page is about to load in the main frame.</summary>
    public void WillLoad(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        if (attempt != null) navigatedSinceAttempt = true;
        SchoolPage(url);
        // the saved sign-in page coming up: covered from its first frame, not after it has shown
        if (Phase == Mode.Idle && !autoOff && host != canvasHost && LoginVault.Load() is { } s && s.Host == host)
        {
            Phase = Mode.Working;
            Arm(25);
        }
    }

    /// <summary>A page finished loading in the main frame.</summary>
    public void DidLoad(Uri? url)
    {
        if (url == null) return;
        var host = url.Host.ToLowerInvariant();
        SchoolPage(url);
        if (host == canvasHost && !IsSignIn(url.AbsolutePath))
        {
            // on Canvas: signed in
            var viaForm = attempt is { Auto: false } && typed != null;
            // (with the saved details: kept by an earlier Simpl in the vault alone, they are locked with Windows Hello now)
            if (attempt is { Auto: true }) _ = LoginVault.LockAtUseAsync();
            attempt = null;
            autoOff = false;
            Handoff = false;
            Reveal();
            // details typed in the form got the student in: they are offered to be kept (or kept anew, where the saved
            // ones had stopped working)
            if (viaForm) AskStay = true; else typed = null;
            return;
        }
        // a page the reader cannot see (it never reports): shown anyway, after a moment
        if (Phase == Mode.Working) Arm(6);
    }

    /// <summary>Where the web view is: on the school's sign-in, Start Over is offered.</summary>
    private void SchoolPage(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        var on = host.Length > 0 && canvasHost.Length > 0 && (host != canvasHost || IsSignIn(url.AbsolutePath));
        if (on != OnSchoolPage) OnSchoolPage = on;
    }

    /// <summary>The page could not be loaded at all.</summary>
    public void PageFailed()
    {
        attempt = null;
        HandOver();
    }

    private static string HostOf(string? uri) => Uri.TryCreate(uri ?? "", UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";
}

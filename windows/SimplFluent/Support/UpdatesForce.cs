using System.Text.RegularExpressions;

namespace Simpl.Support;

/// <summary>
/// (0.3.103, the Mac's 1.3.207) An update kept waiting. One that goes in by itself but has waited a day for what is open
/// (a dialog, a page of the school's, a window, Simpl in use) asks: a prompt over the window says it goes in in half an
/// hour. Later puts the prompt away; it comes back once with five minutes left, and at the next start while the time
/// runs. When the time is up it goes in, whatever is open, as Restart to Update does. Never during a quiz: while one is
/// open nothing is asked and nothing goes in, and the update goes in once it is closed. The day and the half hour are
/// kept with the settings, so quitting Simpl does not start them again; both are let go of once Simpl is up to date or
/// on a version of its own.
/// </summary>
public static partial class Updates
{
    /// <summary>The prompt over the window: the version waiting, and when it goes in.</summary>
    public sealed record ForcePrompt(string Version, DateTime At);

    /// <summary>The prompt showing now (UpdateWatch's, Shell/UpdateWatch.cs, shows and hides it); null: none.</summary>
    public static ForcePrompt? Prompt { get; private set; }
    public static event Action? PromptChanged;
    /// <summary>A quiz open holds an update that would have gone in by now (its day and half hour up, or a Critical Update).</summary>
    public static bool HeldByQuiz { get; private set; }

    /// <summary>A quiz open on a page of the school's (UpdateWatch's: a window of its own, or the window's own page), besides
    /// Simpl's quiz (held by the name "quiz").</summary>
    public static Func<bool>? QuizPage { get; set; }

    private const string SinceKey = "updates:waitingSince";
    private const string FromKey = "updates:waitingFrom";
    private const string ForceKey = "updates:forceAt";
    private static readonly TimeSpan WaitBeforeForcing = TimeSpan.FromHours(24);
    private static readonly TimeSpan ForceGrace = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ForceReminder = TimeSpan.FromMinutes(5);
    private static bool promptedThisRun, reminded;
    /// <summary>What holds an update while Simpl opens (UpdateWatch's).</summary>
    public const string Opening = "Simpl opening";
    /// <summary>When Simpl was first seen opened this run (0: not yet): nothing is asked or put in for ten seconds after,
    /// so what it opens with — a quiz asked for, a page — is open first.</summary>
    private static long openedAt;
    private static readonly TimeSpan AfterOpening = TimeSpan.FromSeconds(10);
    /// <summary>An update played through for the prompt's picture (--update-test-waited): the picture waits until the
    /// prompt is up, or a quiz holds the update.</summary>
    private static bool pictureWaits;

    /// <summary>As the checks begin, an update played through for the prompt's picture holds the picture back.</summary>
    private static void WaitForPrompt()
    {
        if (!Testing || !Launch.Flag("update-test-waited") || !Shot.Asked || pictureWaits) return;
        pictureWaits = true;
        Shot.Began();
    }

    private static void PictureMayGo()
    {
        if (!pictureWaits) return;
        pictureWaits = false;
        Shot.Ended();
    }

    /// <summary>For the Developer window: how long the update found has waited, and when it goes in.</summary>
    public static string WaitingText
    {
        get
        {
            var since = Prefs.Double(SinceKey);
            if (since <= 0) return "nothing waiting";
            var hours = (NowSeconds - since) / 3600;
            var at = Prefs.Double(ForceKey);
            return $"waiting {hours:0.0} h" + (at > 0 ? $", goes in at {DateTime.UnixEpoch.AddSeconds(at).ToLocalTime():HH:mm}" : "") + (HeldByQuiz ? ", held by a quiz" : "");
        }
    }

    /// <summary>A quiz is open now: Simpl's own, or one on a page of the school's. (0.3.105) Knewton Alta, ALEKS and
    /// Macmillan Learning count as one too (their popups hold by those names: Screens/Alta, Screens/Look): an
    /// assignment there can be a timed test, and not even a forced update goes in over it, as on the Mac.</summary>
    public static bool QuizOpen => Holds("quiz") || Holds("Knewton Alta") || Holds("ALEKS") || Holds("Macmillan Learning") || QuizPage?.Invoke() == true;

    /// <summary>A page that is a quiz being taken: a New Quiz (its quiz-lti or quiz-api host), or a Classic Quiz's
    /// /quizzes/…/take.</summary>
    public static bool IsQuizPage(Uri? u)
    {
        if (u == null) return false;
        var host = u.Host.ToLowerInvariant();
        if (host.Contains("quiz-lti") || host.Contains("quiz-api")) return true;
        var path = u.AbsolutePath.ToLowerInvariant();
        return path.Contains("/quizzes/") && (path.EndsWith("/take") || path.Contains("/take/"));
    }

    private static double NowSeconds => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

    /// <summary>As the checks begin: the day and the half hour kept from another version let go of (this copy updated, or
    /// put on another version).</summary>
    private static void SettleWaiting()
    {
        if (Prefs.String(FromKey) is { } from && from != Version) ClearWaiting();
    }

    /// <summary>An update found that goes in by itself: waited for from now, unless it was already.</summary>
    private static void NoteWaiting()
    {
        if (Prefs.String(FromKey) == Version && Prefs.Double(SinceKey) > 0) return;
        // (an update played through for the prompt's picture, its day already up)
        var since = Testing && Launch.Flag("update-test-waited") ? NowSeconds - WaitBeforeForcing.TotalSeconds - 60 : NowSeconds;
        Prefs.Set(SinceKey, since);
        Prefs.Set(FromKey, Version);
        Prefs.Remove(ForceKey);
    }

    /// <summary>Up to date: nothing waits.</summary>
    private static void EndWaiting()
    {
        if (Prefs.String(FromKey) == null && Prefs.Double(SinceKey) <= 0) return;
        ClearWaiting();
        ShowPrompt(null);
    }

    private static void ClearWaiting()
    {
        Prefs.Remove(SinceKey);
        Prefs.Remove(FromKey);
        Prefs.Remove(ForceKey);
        promptedThisRun = reminded = false;
    }

    private static void ShowPrompt(ForcePrompt? p)
    {
        if (p == Prompt) return;
        Prompt = p;
        PromptChanged?.Invoke();
    }

    /// <summary>Later (the prompt's): put away until five minutes are left; it still goes in when the time is up.</summary>
    public static void DismissPrompt()
    {
        if (Prompt == null) return;
        Log.Write($"update: the prompt put away (Later); {Prompt.Version} goes in at {Prompt.At.ToLocalTime():HH:mm}");
        ShowPrompt(null);
    }

    /// <summary>Update Now (the prompt's): in now, as Restart to Update.</summary>
    public static void InstallFromPrompt()
    {
        ShowPrompt(null);
        Log.Write("update: Update Now (the prompt's)");
        InstallAndRestart();
    }

    /// <summary>
    /// An update ready that goes in by itself, looked at every couple of seconds while it waits (Proceed): its day up, the
    /// prompt with its half hour, and once that is up, in. True: it is going in. Nothing while a quiz is open, nor while
    /// Simpl is in the notification area (no update goes in from there); the half hour begins only with the window in view.
    /// </summary>
    private static bool ForceIfDue()
    {
        if (Found is not { } r) return false;
        var since = Prefs.Double(SinceKey);
        if (since <= 0 || Prefs.String(FromKey) != Version) { NoteWaiting(); return false; }
        var quiz = QuizOpen;
        var due = Critical || NowSeconds - since >= WaitBeforeForcing.TotalSeconds;
        if (HeldByQuiz != (quiz && due))
        {
            HeldByQuiz = quiz && due;
            if (HeldByQuiz) { Log.Write($"update: {r.Version} waits for the quiz to close"); PictureMayGo(); }
            Changed?.Invoke();
        }
        if (NowSeconds - since < WaitBeforeForcing.TotalSeconds) return false;
        if (quiz || Away?.Invoke() == true)
        {
            if (Prompt != null) { ShowPrompt(null); promptedThisRun = false; }
            return false;
        }
        // (Simpl still opening, and ten seconds after: what it opens with is open first — a quiz, never asked over)
        if (HeldBy()?.What == Opening) return false;
        if (openedAt == 0) openedAt = Environment.TickCount64;
        if (Environment.TickCount64 - openedAt < AfterOpening.TotalMilliseconds) return false;
        var shown = WindowShown?.Invoke() == true;
        var now = DateTime.UtcNow;
        var kept = Prefs.Double(ForceKey);
        if (kept <= 0)
        {
            // (the half hour begins with the prompt seen)
            if (!shown) return false;
            var at = now + ForceGrace;
            Prefs.Set(ForceKey, (at - DateTime.UnixEpoch).TotalSeconds);
            promptedThisRun = true;
            Log.Write($"update: {r.Version} has waited a day for what is open; it goes in in {ForceGrace.TotalMinutes:0} minutes");
            Log.Write($"update: the prompt is up ({r.Version})");
            ShowPrompt(new ForcePrompt(r.Version, at));
            PictureMayGo();
            return false;
        }
        var due2 = DateTime.UnixEpoch.AddSeconds(kept);
        if (now >= due2)
        {
            ShowPrompt(null);
            Log.Write($"update: {r.Version} goes in now, its half hour up");
            InstallAndRestart();
            return true;
        }
        var left = due2 - now;
        if (shown && (!promptedThisRun || (!reminded && left <= ForceReminder)))
        {
            if (left <= ForceReminder) reminded = true;
            promptedThisRun = true;
            Log.Write($"update: the prompt is up ({r.Version}, {left.TotalMinutes:0} minutes left)");
            ShowPrompt(new ForcePrompt(r.Version, due2));
        }
        // (a newer version found meanwhile: the prompt up names it)
        else if (Prompt is { } up && up.Version != r.Version) ShowPrompt(new ForcePrompt(r.Version, due2));
        return false;
    }
}

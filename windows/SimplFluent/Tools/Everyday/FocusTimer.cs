using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Support;

namespace Simpl.Tools;

#pragma warning disable CA2255
internal static class FocusTimerRegistration
{
    /// <summary>The focus timer as a tool, and as the title bar's live pin while a session goes.</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register()
    {
        ToolRegistry.Register("pomo", c => new FocusTimerTool(c).View, c => new FocusTimerCompact(c).View);
        ToolRegistry.Timer = FocusTimer.Shared;
    }
}
#pragma warning restore CA2255

/// <summary>
/// The focus timer, as the web's (the Mac's FocusTimer): focus for a while, then a break — a long one after every fourth
/// session. It runs on the wall clock, not by counting ticks: a running session is its end time, kept in the app's
/// preferences, and the time left is worked out whenever something shows it — so it keeps going while you move around the
/// app, close the window to the notification area, or quit. A focus block that runs out counts and its break starts by
/// itself; a break that runs out leaves the next block waiting for a press. The end of each is a Windows notification.
/// </summary>
/// <remarks>
/// Nothing is read until the timer is first looked at (the title bar's pin, as the window opens): it is made before the
/// app has read its launch arguments, and where it keeps its files depends on them.
/// </remarks>
public sealed class FocusTimer : ITimerState
{
    public static FocusTimer Shared { get; } = new();

    public enum Phase { Focus, Short, Long }

    public static readonly Phase[] Phases = { Phase.Focus, Phase.Short, Phase.Long };

    public static string Name(Phase p) => p switch { Phase.Focus => "Focus", Phase.Short => "Short break", _ => "Long break" };

    public static string ShortName(Phase p) => p switch { Phase.Focus => "Focus", Phase.Short => "Short", _ => "Long" };

    public static Color ColorOf(Phase p) => p == Phase.Focus ? Color.FromRgb(0xff, 0x95, 0x00) : Color.FromRgb(0x34, 0xc7, 0x59);

    /// <summary>
    /// What is kept: the phase, each phase's minutes, the end of a running session or the seconds left in a paused one, and
    /// the sessions done today.
    /// </summary>
    private sealed class Record
    {
        public Phase Phase = Phase.Focus;
        public int FocusMins = 25, ShortMins = 5, LongMins = 15;
        public DateTime? EndAt;
        public int? Left;
        public int Done;
        public string Day = "";
    }

    /// <summary>The record as it is written: the Mac's keys, its end as seconds since 2001 (a Mac Date's own count).</summary>
    private sealed class Saved
    {
        public string? Phase { get; set; }
        public int FocusMins { get; set; } = 25;
        public int ShortMins { get; set; } = 5;
        public int LongMins { get; set; } = 15;
        public double? EndAt { get; set; }
        public int? Left { get; set; }
        public int Done { get; set; }
        public string? Day { get; set; }
    }

    private const string Key = "focusTimer";
    private static readonly DateTime Reference = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private Record? rec;
    private string? ended;
    private DispatcherTimer? ticker;
    /// <summary>A session started without its notifications (the screenshot suite's): its ends pass quietly.</summary>
    private bool quiet;

    private FocusTimer() { }

    /// <summary>Started, paused, a phase over, stopped, a phase's minutes changed.</summary>
    public event Action? Changed;

    private Record R
    {
        get
        {
            if (rec == null) Load();
            return rec!;
        }
    }

    // ---- reading it ----

    public Phase Current => R.Phase;

    public int Minutes(Phase p) => p switch { Phase.Focus => R.FocusMins, Phase.Short => R.ShortMins, _ => R.LongMins };

    /// <summary>A phase's length in seconds (1 to 90 minutes).</summary>
    public int Length(Phase? p = null) => Math.Clamp(Minutes(p ?? R.Phase), 1, 90) * 60;

    public bool Running => R.EndAt is { } e && e > DateTime.UtcNow;

    public bool Paused => R.EndAt == null && R.Left != null;

    /// <summary>A session to show: going, or paused part way.</summary>
    public bool Active => R.EndAt != null || R.Left != null;

    /// <summary>Seconds left in the phase (paused, running, or the whole of it when nothing is going).</summary>
    public int RemainingSeconds(DateTime? at = null)
    {
        if (R.EndAt is { } e) return Math.Max(0, (int)Math.Ceiling((e - (at ?? DateTime.UtcNow)).TotalSeconds));
        return Math.Max(0, R.Left ?? Length());
    }

    /// <summary>How much of the phase is left, 1 to 0.</summary>
    public double Fraction
    {
        get
        {
            double len = Length();
            return len > 0 ? Math.Clamp(RemainingSeconds() / len, 0, 1) : 0;
        }
    }

    /// <summary>The phase a skip goes to (a skipped focus block does not count as one done).</summary>
    public Phase NextPhase => R.Phase == Phase.Focus ? (R.Done > 0 && R.Done % 4 == 0 ? Phase.Long : Phase.Short) : Phase.Focus;

    /// <summary>Sessions towards the long break (0 to 3).</summary>
    public int Cycle => R.Done % 4;

    /// <summary>The line under the dots.</summary>
    public string CycleLine => R.Phase switch
    {
        Phase.Focus => $"Session {Cycle + 1} of 4",
        Phase.Long => "Long break",
        _ => $"Short break · {4 - Cycle} to a long one",
    };

    public int DoneToday => R.Day == Today() ? R.Done : 0;

    /// <summary>What just finished ("focus" or "break"): the timer says so once.</summary>
    public string? Ended => ended;

    /// <summary>"25:00", "4:07".</summary>
    public static string Clock(int seconds)
    {
        var s = Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>When a running phase ends, in Windows' own time format.</summary>
    public string? EndsText => R.EndAt is { } e && Running ? "Ends " + e.ToLocalTime().ToString("t", CultureInfo.CurrentCulture) : null;

    // ---- the pin's view of it (ITimerState) ----

    public string PhaseName => Name(R.Phase);
    public Color PhaseColor => ColorOf(R.Phase);
    public string NextPhaseName => Name(NextPhase);
    public TimeSpan Remaining => TimeSpan.FromSeconds(RemainingSeconds());

    // ---- moving it ----

    public void Start() => Start(true);

    private void Start(bool notify)
    {
        var r = R;
        r.EndAt = DateTime.UtcNow.AddSeconds(RemainingSeconds());
        r.Left = null;
        ended = null;
        quiet = !notify;
        Commit();
    }

    public void Pause()
    {
        var r = R;
        r.Left = RemainingSeconds();
        r.EndAt = null;
        Commit();
    }

    public void Toggle()
    {
        if (Running) Pause(); else Start();
    }

    /// <summary>Cancel: the phase back to its whole length, stopped.</summary>
    public void Reset()
    {
        var r = R;
        r.Left = null;
        r.EndAt = null;
        ended = null;
        Commit();
    }

    /// <summary>A phase picked (a session going is stopped by it, as on the web), or skipped to (it goes on running).</summary>
    public void SetPhase(Phase p, bool keepRunning = false)
    {
        var r = R;
        r.Phase = p;
        r.Left = null;
        r.EndAt = keepRunning ? DateTime.UtcNow.AddSeconds(Length(p)) : null;
        ended = null;
        if (keepRunning) quiet = false;
        Commit();
    }

    public void Skip() => SetPhase(NextPhase, Running);

    /// <summary>A phase's minutes set (1 to 90); the phase showing, if nothing is going, starts from the new length.</summary>
    public void SetMinutes(int m, Phase p)
    {
        var r = R;
        var v = Math.Clamp(m, 1, 90);
        if (Minutes(p) == v) return;
        switch (p)
        {
            case Phase.Focus: r.FocusMins = v; break;
            case Phase.Short: r.ShortMins = v; break;
            default: r.LongMins = v; break;
        }
        if (p == r.Phase && !Active) r.Left = null;
        Commit();
    }

    // ---- the clock underneath ----

    /// <summary>
    /// A phase that ran out: a focus block counts and its break starts by itself, from the moment the block ended; a break
    /// that ran out leaves the next focus block waiting (a session should not start with nobody at the desk).
    /// </summary>
    private void Settle()
    {
        var r = R;
        var wasFocus = r.Phase == Phase.Focus;
        var at = r.EndAt ?? DateTime.UtcNow;
        if (wasFocus) r.Done += 1;
        r.Phase = wasFocus ? (r.Done % 4 == 0 ? Phase.Long : Phase.Short) : Phase.Focus;
        r.EndAt = wasFocus ? at.AddSeconds(Length(r.Phase)) : null;
        r.Left = null;
        ended = wasFocus ? "focus" : "break";
    }

    /// <param name="notify">Said in a notification (a phase that ran out while the app watched, not while it was closed).</param>
    private void SettleIfDue(bool notify)
    {
        var r = R;
        var changed = false;
        (string Title, string Text)? note = null;
        for (var n = 0; r.EndAt is { } e && e <= DateTime.UtcNow && n < 8; n++)
        {
            var wasFocus = r.Phase == Phase.Focus;
            Settle();
            changed = true;
            note = wasFocus
                ? ("Focus session done", $"{Name(r.Phase)} started: {Minutes(r.Phase)} minutes.")
                : ("Break over", "Press Start when you are back.");
        }
        var today = Today();
        if (r.Day != today)
        {
            r.Day = today;
            r.Done = 0;
            changed = true;
        }
        if (changed) Commit();
        if (notify && !quiet && note is { } say)
            (Application.Current?.MainWindow as Shell.MainWindow)?.Notify(say.Title, say.Text, () => (Application.Current?.MainWindow as Shell.MainWindow)?.Bring());
    }

    private void Commit()
    {
        Save();
        ArmTicker();
        Changed?.Invoke();
    }

    /// <summary>Once a second while a session runs: a phase that ran out is settled (what shows the time redraws itself).</summary>
    private void ArmTicker()
    {
        if (R.EndAt != null)
        {
            if (ticker == null)
            {
                ticker = new DispatcherTimer(DispatcherPriority.Normal, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher) { Interval = TimeSpan.FromSeconds(1) };
                ticker.Tick += (_, _) => SettleIfDue(true);
            }
            if (!ticker.IsEnabled) ticker.Start();
        }
        else ticker?.Stop();
    }

    // ---- kept ----

    private void Load()
    {
        var r = new Record();
        try
        {
            if (Prefs.String(Key) is { Length: > 0 } json && JsonSerializer.Deserialize<Saved>(json, Json.Options) is { } s)
            {
                r.Phase = s.Phase switch { "short" => Phase.Short, "long" => Phase.Long, _ => Phase.Focus };
                r.FocusMins = Math.Clamp(s.FocusMins, 1, 90);
                r.ShortMins = Math.Clamp(s.ShortMins, 1, 90);
                r.LongMins = Math.Clamp(s.LongMins, 1, 90);
                r.EndAt = s.EndAt is { } at ? Reference.AddSeconds(at) : null;
                r.Left = s.Left;
                r.Done = Math.Max(0, s.Done);
                r.Day = s.Day ?? "";
            }
        }
        catch (Exception e) { Log.Write("focus timer: " + e.Message); }
        rec = r;
        SettleIfDue(false);
        ArmTicker();
        // (the screenshot suite: --focus-start starts a session, for the title bar's live timer — as the Mac's SimplFocusDemo)
        if (Launch.Take("", "focus-start") != null && !Running) Start(false);
    }

    private void Save()
    {
        var r = R;
        var s = new Saved
        {
            Phase = r.Phase switch { Phase.Short => "short", Phase.Long => "long", _ => "focus" },
            FocusMins = r.FocusMins, ShortMins = r.ShortMins, LongMins = r.LongMins,
            EndAt = r.EndAt is { } e ? (e - Reference).TotalSeconds : null,
            Left = r.Left, Done = r.Done, Day = r.Day,
        };
        Prefs.Set(Key, JsonSerializer.Serialize(s, Json.Script));
    }

    /// <summary>The day a count of sessions belongs to ("2026-10-08").</summary>
    private static string Today() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

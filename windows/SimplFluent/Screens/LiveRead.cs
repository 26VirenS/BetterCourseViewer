using Simpl.Support;

namespace Simpl.Screens;

/// <summary>
/// Something on screen a live check reads again (Shell/LiveCheck.cs): a screen's loader, or the pane's counts. It is
/// asked again with the engine's maxAge and the hash of what it shows; an answer the same as that changes nothing, not
/// even a redraw — it only says when it was last the school's.
/// </summary>
public interface ILiveRead
{
    /// <summary>The call it reads ("today"), as the log names it; null until it has asked.</summary>
    string? Call { get; }

    /// <summary>It has something on screen to check and nothing on its way (a read under way answers for itself).</summary>
    bool CanCheck { get; }

    /// <summary>When what it shows was last the school's answer (Environment.TickCount64; 0: never — only what was kept
    /// from last time).</summary>
    long FreshAt { get; }

    /// <summary>Read again, no answer older than <paramref name="maxAge"/> seconds: the same (nothing to do), new (to be
    /// applied — <see cref="LivePending.Apply"/>), or failed.</summary>
    Task<LivePending> Check(Engine.Engine engine, double maxAge);
}

/// <summary>A live check's answer for one read: the same as on screen, new (with how to show it), or failed.</summary>
public sealed class LivePending
{
    public ILiveRead Read { get; }
    public string Call { get; }
    public LiveAnswerKind Kind { get; }
    /// <summary>A new answer shown — the read's data replaced and its screen told — unless something else has changed it
    /// meanwhile (a read of the screen's own, a tick): false then, and nothing done.</summary>
    public Func<bool>? Apply { get; }
    /// <summary>The read was changed while the check asked (its answer is no longer the one to show).</summary>
    public bool Dropped { get; }

    private LivePending(ILiveRead read, string call, LiveAnswerKind kind, Func<bool>? apply, bool dropped)
    {
        Read = read;
        Call = call;
        Kind = kind;
        Apply = apply;
        Dropped = dropped;
    }

    public static LivePending Same(ILiveRead r, string call) => new(r, call, LiveAnswerKind.Same, null, false);
    public static LivePending New(ILiveRead r, string call, Func<bool> apply) => new(r, call, LiveAnswerKind.New, apply, false);
    public static LivePending Failed(ILiveRead r, string call) => new(r, call, LiveAnswerKind.Failed, null, false);
    /// <summary>Changed under the check: nothing to apply, and no failure either.</summary>
    public static LivePending Gone(ILiveRead r, string call) => new(r, call, LiveAnswerKind.Same, null, true);
}

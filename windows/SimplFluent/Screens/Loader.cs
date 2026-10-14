using System.Text.Json;
using System.Text.Json.Nodes;
using Simpl.Support;

namespace Simpl.Screens;

/// <summary>
/// What a screen reads from the engine (the Mac's Loader): the answer kept from last time at once, then the school's
/// answer now; read again whenever the engine says what the screens show may have changed (DataVersion), the arguments
/// change, or what it has is older than <see cref="StaleAfter"/> (stale while it is read again: what it has stays shown).
/// An answer the page gave moments ago, nothing changed since (the shell's prefetch, another screen's read), is taken
/// as it is, with no need to ask again. <see cref="Changed"/> says an answer came (or failed), for the screen to show it
/// — every answer, one that says word for word what the last one said too (<see cref="LastSame"/>: nothing to build
/// again for it). A loader of a screen the live check looks after (<see cref="Driven"/>) leaves what is only old to the
/// check, which asks again with the engine's maxAge and changes it only if the answer is new
/// (<see cref="ILiveRead"/>; <see cref="LastLive"/> says an answer came that way).
/// </summary>
public sealed class Loader<T> : ILiveRead where T : class
{
    /// <summary>What was read is read again, as the screen is shown or refreshed, once it is this old (the live check's
    /// minute).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(LiveRules.Every);

    private T? data;
    /// <summary>The JSON text <see cref="Data"/> came from (the answer kept, or the page's), or null when not known.</summary>
    private string? text;

    /// <summary>What was read. Set by the screen (a change shown before the page says it), the next answer is shown whatever it says.</summary>
    public T? Data
    {
        get => data;
        set
        {
            data = value;
            text = null;
            hash = null;
            turn++;
            Kept = false;
        }
    }
    /// <summary>An answer the screen asked for itself (To Do regrouped, a module's item ticked) taken as what it shows,
    /// with the text it came as: the next live check compares with it and changes nothing when it is the same (set as
    /// <see cref="Data"/>, the text was not known, and the next check took the same answer as new and built again).</summary>
    public void Take(T value, string answer)
    {
        Data = value;
        text = answer;
    }

    /// <summary>What it has is the answer kept from last time: the page's own is not in yet (the Dashboard draws its counts
    /// dimmed meanwhile, saying it is updating).</summary>
    public bool Kept { get; private set; }
    public string? Error { get; private set; }
    public bool Loading { get; private set; }
    /// <summary>The last answer said word for word what the data came from, and nothing had failed: <see cref="Data"/> is
    /// as it was (the same object), and the screen need not be built again.</summary>
    public bool LastSame { get; private set; }
    /// <summary>The answer being told came from a live check (Changed is raised while it is true): the screen shows what
    /// changed in place (<see cref="Screen.LiveApplied"/>) rather than being built again.</summary>
    public bool LastLive { get; private set; }
    /// <summary>A live check reads it again (the Dashboard's copies of what was kept from its last visit say no).</summary>
    public bool Checks { get; set; } = true;
    /// <summary>Its screen is looked after by the live check (set by the screen): what is only old is checked, not read
    /// again here.</summary>
    internal Func<bool> Driven { get; set; } = () => false;
    public event Action? Changed;
    private int version = -1;
    private string args = "\0";
    private int generation;
    /// <summary>When what it has was the page's answer (Environment.TickCount64; 0: never, or only what was kept from before).</summary>
    private long answeredAt;
    /// <summary>The call last asked, its arguments, and the engine's hash of the answer shown (null: not known yet — the
    /// check compares the text instead).</summary>
    private string? call;
    private object? callArgs;
    private string? hash;
    /// <summary>Bumped whenever what it shows is replaced: a check that began before is no longer the one to show.</summary>
    private int turn;

    string? ILiveRead.Call => call;
    public bool CanCheck => Checks && call != null && data != null && !Loading;
    public long FreshAt => answeredAt;

    /// <summary>Read if it has not been, or if what it reads may have changed, or is old.</summary>
    public void Ensure(Engine.Engine engine, string name, object? a = null)
    {
        var key = a == null ? "" : JsonSerializer.Serialize(a);
        // (old: never the page's answer yet, or — for a screen the live check does not look after — a minute old)
        var old = answeredAt == 0 || (!Driven() && Environment.TickCount64 - answeredAt > StaleAfter.TotalMilliseconds);
        call = name;
        callArgs = a;
        if (version == engine.DataVersion && key == args && !old) return;
        if (Loading && key == args) return;
        version = engine.DataVersion;
        // (the first read is not a move: what was kept is drawn by the screen's first build)
        var moved = args != "\0" && key != args;
        if (key != args)
        {
            args = key;
            Data = null;
            Error = null;
            answeredAt = 0;
        }
        if (data == null && engine.Kept<T>(name, a) is { } kept)
        {
            data = kept;
            Kept = true;
            hash = null;
            turn++;
            // (for other arguments the answer is shown whatever it says: what was kept for them may never have been drawn)
            if (!moved) text = TextOf(engine, name, a);
            // read moments ago, nothing changed since: that answer is this one's
            if (engine.Answers.Fresh(name, ArgsOf(a), engine.DataVersion) is { } at)
            {
                if (moved) text = TextOf(engine, name, a);
                answeredAt = at;
                Kept = false;
                Error = null;
                LastSame = !moved;
                generation++; // (a read still on its way, for other arguments, is no longer this one's)
                Loading = false;
                // (told after whatever asked, as an answer would be: a screen moved to other arguments builds again)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke(), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
        }
        _ = Load(engine, name, a, ++generation);
    }

    /// <summary>Read again at the next Ensure (a tick, a refresh); that answer is shown whatever it says.</summary>
    public void Again()
    {
        version = -1;
        answeredAt = 0;
        text = null;
        hash = null;
    }

    /// <summary>Read again now, the answer shown whatever it says. After a failure (Try Again), the error goes at once, so
    /// the spinner shows while it reads.</summary>
    public void Reload(Engine.Engine engine, string name, object? a = null)
    {
        version = -1;
        answeredAt = 0;
        var failed = Error != null;
        Error = null;
        Ensure(engine, name, a);
        text = null; // (after Ensure, which may have taken what was kept)
        hash = null;
        if (!failed) return;
        LastSame = false;
        Changed?.Invoke();
    }

    /// <summary>The screen watching it was built (kept for the screens' parts, which tell their loaders as screens do).</summary>
    public void Drawn() { }

    private async Task Load(Engine.Engine engine, string name, object? a, int gen)
    {
        Loading = true;
        Support.Shot.Began();
        var started = DateTime.UtcNow;
        var same = false;
        try
        {
            var (d, t) = await engine.CallWithText<T>(name, a);
            if (gen != generation) return;
            same = t == text && Error == null;
            if (!same)
            {
                data = d;
                text = t;
                hash = null;
                turn++;
            }
            Error = null;
            Kept = false;
            answeredAt = Environment.TickCount64;
            if (Support.Launch.IsDevelopment) Support.Log.Write($"read {name}: {(DateTime.UtcNow - started).TotalSeconds:0.0}s{(same ? ", as before" : "")}");
        }
        catch (Exception e)
        {
            if (gen != generation) return;
            if (Data == null) Error = e.Message;
            Support.Log.Write($"read {name} failed after {(DateTime.UtcNow - started).TotalSeconds:0.0}s: {e.Message}");
        }
        finally
        {
            Support.Shot.Ended();
            if (gen == generation) Loading = false;
        }
        LastSame = same;
        Changed?.Invoke();
    }

    /// <summary>
    /// The live check's read (<see cref="ILiveRead"/>): the call last asked, asked again with <paramref name="maxAge"/> and
    /// the hash of the answer shown (on the first check after a read, none: the answer's text is compared with the one
    /// shown instead). The same: only when it was the school's changes. New: decoded off the window's thread, and shown
    /// when <see cref="LivePending.Apply"/> says — unless the screen read or changed it meanwhile.
    /// </summary>
    public async Task<LivePending> Check(Engine.Engine engine, double maxAge)
    {
        var name = call!;
        var a = callArgs;
        var at = turn;
        var plain = ArgsOf(a);
        var answer = await engine.CheckCall(name, plain, maxAge, hash);
        if (at != turn || name != call) return LivePending.Gone(this, name);
        if (answer.Kind == LiveAnswerKind.Failed)
        {
            Log.Write($"live: {name} could not be checked: {answer.Error}");
            return LivePending.Failed(this, name);
        }
        if (answer.Kind == LiveAnswerKind.Same || (text != null && answer.Value == text))
        {
            hash = answer.Hash;
            answeredAt = Environment.TickCount64;
            engine.Answers.Confirm(name, plain, engine.DataVersion);
            return LivePending.Same(this, name);
        }
        T d;
        try { d = await Task.Run(() => Engine.Engine.Decode<T>(name, answer.Value!)); }
        catch (Exception e)
        {
            Log.Write($"live: {name} answer not understood: {e.Message}");
            return LivePending.Failed(this, name);
        }
        if (at != turn || name != call) return LivePending.Gone(this, name);
        var value = answer.Value!;
        var h = answer.Hash;
        return LivePending.New(this, name, () =>
        {
            if (at != turn || name != call) return false;
            data = d;
            text = value;
            hash = h;
            turn++;
            Kept = false;
            Error = null;
            answeredAt = Environment.TickCount64;
            engine.Answers.Keep(value, name, plain, engine.DataVersion);
            LastSame = false;
            LastLive = true;
            try { Changed?.Invoke(); }
            finally { LastLive = false; }
            return true;
        });
    }

    private static JsonObject ArgsOf(object? a) => a switch
    {
        null => new JsonObject(),
        JsonObject j => j,
        _ => JsonSerializer.SerializeToNode(a, Support.Json.Script) as JsonObject ?? new JsonObject(),
    };

    /// <summary>The text a read's answer is kept as (the arguments made as the engine makes them) — after a call, the
    /// answer just given; null for a read that is never kept (todayCounts).</summary>
    private static string? TextOf(Engine.Engine engine, string name, object? a)
    {
        if (!Engine.AnswerCache.Reads.Contains(name)) return null;
        return engine.Answers.Text(name, ArgsOf(a));
    }
}

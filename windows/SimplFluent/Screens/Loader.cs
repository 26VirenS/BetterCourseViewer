using System.Text.Json;
using System.Text.Json.Nodes;

namespace Simpl.Screens;

/// <summary>
/// What a screen reads from the engine (the Mac's Loader): the answer kept from last time at once, then the school's
/// answer now; read again whenever the engine says what the screens show may have changed (DataVersion), the arguments
/// change, or what it has is older than <see cref="StaleAfter"/> (stale while it is read again: what it has stays shown).
/// An answer the page gave moments ago, nothing changed since (the shell's prefetch, another screen's read), is taken
/// as it is, with no need to ask again. <see cref="Changed"/> says an answer came (or failed), for the screen to show it
/// — every answer, one that says word for word what the last one said too (<see cref="LastSame"/>: nothing to build
/// again for it).
/// </summary>
public sealed class Loader<T> where T : class
{
    /// <summary>What was read is read again, as the screen is shown or refreshed, once it is this old.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

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
        }
    }
    public string? Error { get; private set; }
    public bool Loading { get; private set; }
    /// <summary>The last answer said word for word what the data came from, and nothing had failed: <see cref="Data"/> is
    /// as it was (the same object), and the screen need not be built again.</summary>
    public bool LastSame { get; private set; }
    public event Action? Changed;
    private int version = -1;
    private string args = "\0";
    private int generation;
    /// <summary>When what it has was the page's answer (Environment.TickCount64; 0: never, or only what was kept from before).</summary>
    private long answeredAt;

    /// <summary>Read if it has not been, or if what it reads may have changed, or is old.</summary>
    public void Ensure(Engine.Engine engine, string name, object? a = null)
    {
        var key = a == null ? "" : JsonSerializer.Serialize(a);
        var old = answeredAt == 0 || Environment.TickCount64 - answeredAt > StaleAfter.TotalMilliseconds;
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
            // (for other arguments the answer is shown whatever it says: what was kept for them may never have been drawn)
            if (!moved) text = TextOf(engine, name, a);
            // read moments ago, nothing changed since: that answer is this one's
            if (engine.Answers.Fresh(name, ArgsOf(a), engine.DataVersion) is { } at)
            {
                if (moved) text = TextOf(engine, name, a);
                answeredAt = at;
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
            var d = await engine.Call<T>(name, a);
            if (gen != generation) return;
            var t = TextOf(engine, name, a);
            same = t != null && t == text && Error == null;
            if (!same)
            {
                data = d;
                text = t;
            }
            Error = null;
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

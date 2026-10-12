using System.Text.Json;
using System.Text.Json.Nodes;

namespace Simpl.Screens;

/// <summary>
/// What a screen reads from the engine (the Mac's Loader): the answer kept from last time at once, then the school's
/// answer now; read again whenever the engine says what the screens show may have changed (DataVersion), or the
/// arguments change. <see cref="Changed"/> says an answer came (or failed), for the screen to show it — every answer,
/// one that says word for word what the last one said too (<see cref="LastSame"/>: nothing to build again for it).
/// </summary>
public sealed class Loader<T> where T : class
{
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
    /// <summary>The screen was built while this read (a spinner drawn, say): its answer is shown whatever it says.</summary>
    public bool MustRender { get; private set; }
    public event Action? Changed;
    private int version = -1;
    private string args = "\0";
    private int generation;

    /// <summary>Read if it has not been, or if what it reads may have changed.</summary>
    public void Ensure(Engine.Engine engine, string name, object? a = null)
    {
        var key = a == null ? "" : JsonSerializer.Serialize(a);
        if (version == engine.DataVersion && key == args) return;
        if (Loading && key == args) return;
        version = engine.DataVersion;
        var moved = key != args;
        if (moved)
        {
            args = key;
            Data = null;
            Error = null;
        }
        if (data == null && engine.Kept<T>(name, a) is { } kept)
        {
            data = kept;
            // (for other arguments the answer is shown whatever it says: what was kept for them may never have been drawn)
            if (!moved) text = TextOf(engine, name, a);
        }
        _ = Load(engine, name, a, ++generation);
    }

    /// <summary>Read again at the next Ensure (a tick, a refresh); that answer is shown whatever it says.</summary>
    public void Again()
    {
        version = -1;
        text = null;
    }

    /// <summary>Read again now, the answer shown whatever it says. After a failure (Try Again), the error goes at once, so
    /// the spinner shows while it reads.</summary>
    public void Reload(Engine.Engine engine, string name, object? a = null)
    {
        version = -1;
        var failed = Error != null;
        Error = null;
        Ensure(engine, name, a);
        text = null; // (after Ensure, which may have taken what was kept)
        if (!failed) return;
        LastSame = false;
        Changed?.Invoke();
    }

    /// <summary>The screen watching it was built: if this is reading, its answer is shown whatever it says.</summary>
    public void Drawn()
    {
        if (Loading) MustRender = true;
    }

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
            same = t != null && t == text && Error == null && !MustRender;
            if (!same)
            {
                data = d;
                text = t;
            }
            Error = null;
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
        MustRender = false;
        LastSame = same;
        Changed?.Invoke();
    }

    /// <summary>The text a read's answer is kept as (the arguments made as the engine makes them) — after a call, the
    /// answer just given; null for a read that is never kept (todayCounts).</summary>
    private static string? TextOf(Engine.Engine engine, string name, object? a)
    {
        if (!Engine.AnswerCache.Reads.Contains(name)) return null;
        var o = a switch
        {
            null => new JsonObject(),
            JsonObject j => j,
            _ => JsonSerializer.SerializeToNode(a, Support.Json.Script) as JsonObject ?? new JsonObject(),
        };
        return engine.Answers.Text(name, o);
    }
}

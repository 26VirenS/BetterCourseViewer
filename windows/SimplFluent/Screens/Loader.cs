namespace Simpl.Screens;

/// <summary>
/// What a screen reads from the engine (the Mac's Loader): the answer kept from last time at once, then the school's
/// answer now; read again whenever the engine says what the screens show may have changed (DataVersion), or the
/// arguments change. <see cref="Changed"/> says an answer came (or failed), for the screen to show it.
/// </summary>
public sealed class Loader<T> where T : class
{
    public T? Data { get; set; }
    public string? Error { get; private set; }
    public bool Loading { get; private set; }
    public event Action? Changed;
    private int version = -1;
    private string args = "\0";
    private int generation;

    /// <summary>Read if it has not been, or if what it reads may have changed.</summary>
    public void Ensure(Engine.Engine engine, string name, object? a = null)
    {
        var key = a == null ? "" : System.Text.Json.JsonSerializer.Serialize(a);
        if (version == engine.DataVersion && key == args) return;
        if (Loading && key == args) return;
        version = engine.DataVersion;
        if (key != args)
        {
            args = key;
            Data = null;
            Error = null;
        }
        Data ??= engine.Kept<T>(name, a);
        _ = Load(engine, name, a, ++generation);
    }

    /// <summary>Read again at the next Ensure (a tick, a refresh).</summary>
    public void Again() => version = -1;

    /// <summary>Read again now.</summary>
    public void Reload(Engine.Engine engine, string name, object? a = null)
    {
        version = -1;
        Ensure(engine, name, a);
    }

    private async Task Load(Engine.Engine engine, string name, object? a, int gen)
    {
        Loading = true;
        Support.Shot.Began();
        var started = DateTime.UtcNow;
        try
        {
            var d = await engine.Call<T>(name, a);
            if (gen != generation) return;
            Data = d;
            Error = null;
            if (Support.Launch.IsDevelopment) Support.Log.Write($"read {name}: {(DateTime.UtcNow - started).TotalSeconds:0.0}s");
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
        Changed?.Invoke();
    }
}

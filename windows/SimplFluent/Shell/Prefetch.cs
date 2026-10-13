using Simpl.Engine;
using Simpl.Screens;
using Simpl.Support;

namespace Simpl.Shell;

/// <summary>
/// Reading ahead, so a screen opens with everything it shows. Once the start has settled — the counts live, nothing
/// asked of the page for a moment — the root places (To Do, the Calendar, Grades, Notifications, the Inbox, All Courses)
/// and each course's home are read, one screen at a time (two or three reads at once at most), a breath between them,
/// and never while the student's own screens are asking: the school is asked at the pace of a student clicking
/// through, not all at once. And a pointer resting on its way to a screen has that screen read meanwhile. Each is read
/// through the screen itself, made for it unseen and let go once its answers are in: the answers stay in the engine's
/// AnswerCache (in memory, and sealed on disk), and the screen opened then shows them at once, asking again only once
/// they are a while old. What is already kept, or was read moments ago, is not asked again.
/// </summary>
internal sealed class Prefetcher
{
    /// <summary>How many courses' homes are read ahead (a student with more opens the rest as they always did).</summary>
    private const int Courses = 12;

    private readonly Engine.Engine engine;
    private readonly Func<Location, bool> kept;
    private readonly Func<double> width;
    private bool started, stopped;
    private int busy;

    public Prefetcher(Engine.Engine engine, Func<Location, bool> kept, Func<double> width)
    {
        this.engine = engine;
        this.kept = kept;
        this.width = width;
    }

    /// <summary>A screen is being read ahead now.</summary>
    public bool Busy => busy > 0;

    /// <summary>The roots and the courses read, once, when the start has settled.</summary>
    public async void Start()
    {
        // (a picture of the screenshot suite is of what it opened on, nothing read ahead behind it)
        if (started || Launch.Flag("no-prefetch") || Shot.Asked) return;
        started = true;
        try
        {
            // (the counts live: the page answers; then a moment with nothing asked of it)
            while (!stopped && !engine.CountsLive) await Task.Delay(250);
            await Quiet(1200, 20_000);
            var done = 0;
            var at = Environment.TickCount64;
            foreach (var place in Targets())
            {
                if (stopped || engine.Phase != Phase.Native) return;
                if (kept(new Location(place))) continue;
                await One(place, null);
                done++;
                await Task.Delay(120);
                await Quiet(300, 5000);
            }
            if (Launch.IsDevelopment) Log.Write($"prefetch: {done} screens read ahead in {(Environment.TickCount64 - at) / 1000.0:0.0} s");
        }
        catch (Exception e) { Log.Write("prefetch: " + e.Message); }
    }

    public void Stop() => stopped = true;

    private IEnumerable<Place> Targets()
    {
        yield return Place.Todo;
        yield return Place.Calendar;
        yield return Place.Grades;
        yield return Place.Notifications;
        if (!engine.OnBrightspace) yield return Place.Inbox;
        yield return Place.Courses;
        foreach (var c in engine.Courses.Take(Courses).ToList()) yield return Place.Home($"courses/{c.Id}");
    }

    /// <summary>One screen read ahead: made unseen, asked to read, let go once its answers are in (15 s at most).</summary>
    public async Task One(Place place, Route? route)
    {
        if (stopped) return;
        busy++;
        Screen? s = null;
        try
        {
            s = route != null ? ScreenFactory.For(engine, route) : ScreenFactory.For(engine, place);
            s.SetWidth(width());
            s.Prefetch();
            var from = Environment.TickCount64;
            while (s.Reading && Environment.TickCount64 - from < 15_000) await Task.Delay(50);
        }
        catch (Exception e) { Log.Write($"prefetch {place.Name}: {e.Message}"); }
        finally
        {
            s?.Hide();
            s?.Release();
            busy--;
        }
    }

    /// <summary>Once nothing has been asked of the page for <paramref name="ms"/> (or <paramref name="limit"/> has passed).</summary>
    private async Task Quiet(int ms, int limit)
    {
        var from = Environment.TickCount64;
        while (!stopped && Environment.TickCount64 - from < limit)
        {
            if (engine.CallsInFlight == 0 && Environment.TickCount64 - engine.LastCallAt >= ms) return;
            await Task.Delay(100);
        }
    }
}

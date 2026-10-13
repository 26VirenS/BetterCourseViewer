using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Models;

namespace Simpl.Support;

/// <summary>
/// --perf-walk &lt;folder&gt;: once the app's own screens are up and their first answers in, one fixed walk through
/// it — the Dashboard, To Do, the Calendar, Grades, the Inbox, a course, an assignment, Notifications, New task,
/// Settings, Tools, a search typed, the window dragged and resized, the Dashboard again — each step timed with Perf's
/// numbers (and how each screen came in: its page kept, built from the answers kept, or read); a screen shown a while
/// ago again (Grades), its page let go since; memory read once the start has settled, after the walk, after it has
/// been left alone a while, and after the window has been minimized a while (then brought back, timed); all written to perf[-name].json and perf[-name].txt in the folder and to simpl.log,
/// and then Simpl quits. It only looks: New task and Settings are opened and closed with nothing saved. --perf-name
/// (the files' suffix), --perf-course and --perf-assignment (what is opened; else the first course, and the first
/// assignment To Do lists), --perf-minimized (how long the window stays minimized, 30 s; 0: not), --perf-timeout
/// (240 s from the start: what was walked by then is written).
/// </summary>
public static class PerfWalk
{
    private const double MB = 1024 * 1024;
    /// <summary>A step's answers are waited for this long at most.</summary>
    private const double StepLimit = 15_000;
    private static readonly string[] Typed = { "d", "di", "dis", "disc", "discu", "discus" };

    private static Shell.MainWindow? main;
    private static readonly List<Row> rows = new();
    private static string stage = "the window";
    private static bool started, finished;

    /// <summary>Perf.Watch, given --perf-walk: walked once the window has drawn, and ended by --perf-timeout whatever happens.</summary>
    public static void Arm(Window w)
    {
        if (w is not Shell.MainWindow m) return;
        main = m;
        var limit = Launch.Number("perf-timeout", 240);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1000, limit * 1000 - Perf.Now)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Finish(Perf.Fmt($"not finished in {limit:0} s: stopped at {stage}"));
        };
        timer.Start();
        m.ContentRendered += (_, _) =>
        {
            if (started) return;
            started = true;
            _ = Run(m);
        };
    }

    private static async Task Run(Shell.MainWindow m)
    {
        try
        {
            stage = "the app's own screens (a sign-in, or the school's own page, was showing)";
            if (!await Until(() => Perf.Current is { Phase: Phase.Native } e0 && !e0.Login.Covers)) return;
            // (the first answers, or 45 s: a page that never goes quiet is walked anyway, its start row without them)
            stage = "the first answers";
            await Until(() => Perf.Reached("first-data") != null, 45_000);
            if (finished) return;
            var e = Perf.Current!;
            // the start, as the step before the walk: its times from the process's start, the rest all it took
            var place = e.Nav.Current;
            rows.Add(new Row(place.Path.Count == 0 && place.Place.Kind == PlaceKind.Dashboard ? "Dashboard (start)" : "Start", Perf.Reached("first-screen"), Perf.Reached("first-data"),
                (int)Perf.BuildCount, Perf.BuildMs, (int)Perf.CallCount, Perf.CallMs, Perf.LongestStall, null, null, GC.GetTotalAllocatedBytes(false) / MB,
                "ui is the first screen and data its first answers, in ms from the process's start; the rest is all the start took"));
            // a sheet over the window (What's New) put away; one that must be answered (the first setup) stops the walk
            stage = "a sheet over the window";
            for (var i = 0; i < 6 && m.Sheets.Current is { } s; i++)
            {
                if (s.Modal) { Finish("a sheet that must be answered first (the first setup) is open: answer it, then walk again"); return; }
                s.Close();
                await Until(() => m.Sheets.Current != s, 3000);
            }
            await Task.Delay(500);
            // memory once the start has settled: nothing asked of the page for two seconds (twenty at most)
            stage = "the start settling";
            await Perf.Quiet(2000, 20_000);
            if (finished) return;
            Remember("settled");
            Perf.Fast(true);

            await Go("To Do", () => e.Go(Place.Todo));
            await Go("Calendar", () => e.Go(Place.Calendar));
            await Go("Grades", () => e.Go(Place.Grades));
            if (e.OnBrightspace) Skip("Inbox", "Brightspace: no Inbox"); else await Go("Inbox", () => e.Go(Place.Inbox));
            var course = Launch.Get("perf-course") ?? e.Courses.FirstOrDefault()?.Id;
            if (course == null) Skip("Course", "no course to open");
            else await Go("Course", () => e.Go(Place.Home($"courses/{course}")));
            if (Assignment(e, course) is { } a) await Go("Assignment", () => e.Open(Route.Assignment(a.Course, a.Id)));
            else Skip("Assignment", "no assignment to open (none on To Do)");
            await Go("Notifications", () => e.Go(Place.Notifications));
            await NewTask(m, e);
            await Settings(e);
            await Go("Tools", () => e.Go(Place.Tools));
            await Typing(e);
            await Drag(m);
            await Resize(m);
            await Go("Dashboard (again)", () => e.Go(Place.Dashboard));
            // a screen shown a while ago, its page let go since: made again from the answers kept
            await Go("Grades (again)", () => e.Go(Place.Grades));
            await Go("Dashboard (last)", () => e.Go(Place.Dashboard));
            // memory after the walk, settled as the start was; then left alone a while; then after a while minimized,
            // and the window back
            stage = "the walk settling";
            await Perf.Quiet(2000, 20_000);
            if (finished) return;
            Remember("walked");
            stage = "left alone";
            await Perf.Quiet(16_000, 40_000);
            if (finished) return;
            Remember("idle");
            await Minimized(m);
            Finish(null);
        }
        catch (Exception x)
        {
            Finish("the walk failed: " + x.Message);
            Log.Write("perf walk: " + x);
        }
    }

    /// <summary>The assignment opened: --perf-assignment (in --perf-course, else the first course), else the first To Do
    /// lists (its kept answer: To Do was walked just before).</summary>
    private static (string Course, string Id)? Assignment(Engine.Engine e, string? course)
    {
        if (Launch.Get("perf-assignment") is { Length: > 0 } id) return course != null ? (course, id) : null;
        var todo = e.Kept<TodoData>("todo");
        foreach (var row in todo?.Sections.SelectMany(s => s.Rows) ?? Enumerable.Empty<WorkRow>())
            if (Regex.Match(row.Url ?? "", @"/courses/(\d+)/assignments/(\d+)") is { Success: true } hit) return (hit.Groups[1].Value, hit.Groups[2].Value);
        return null;
    }

    // ---- the steps ----

    /// <summary>A screen gone to: drawn (ui), its answers in and shown (data).</summary>
    private static async Task Go(string name, Action act)
    {
        if (finished) return;
        stage = name;
        var k = new Mark();
        Perf.TakeOpen();
        act();
        var shown = await Perf.Idle();
        var settled = await Perf.Settle(k.T0, shown, StepLimit);
        await k.FramesFor(700);
        // (how the shell brought it in: its page kept, built from the answers kept, or waiting for the page)
        var how = Perf.TakeOpen() is { } o ? $"opened {o.How}" : null;
        rows.Add(k.Row(name, Perf.Ms(k.T0, shown), settled is { } t ? Perf.Ms(k.T0, t) : null, settled == null ? (how != null ? how + "; " : "") + Late : how));
        await Task.Delay(250);
    }

    private static async Task NewTask(Shell.MainWindow m, Engine.Engine e)
    {
        if (finished) return;
        stage = "New task sheet";
        var k = new Mark();
        e.AskNewTask();
        var shown = await Perf.Idle();
        var sheet = m.Sheets.Current;
        var settled = await Perf.Settle(k.T0, shown, StepLimit);
        await k.FramesFor(700);
        rows.Add(k.Row("New task sheet", Perf.Ms(k.T0, shown), settled is { } t ? Perf.Ms(k.T0, t) : null, sheet == null ? "no sheet came up" : settled == null ? Late : null));
        // (put away as Escape does: nothing added)
        if (sheet != null)
        {
            sheet.Close();
            await Until(() => !m.Sheets.IsOpen, 3000);
        }
        await Task.Delay(250);
    }

    private static async Task Settings(Engine.Engine e)
    {
        if (finished) return;
        stage = "Settings";
        var k = new Mark();
        long? drawn = null;
        e.AskSettings();
        var w = Application.Current.Windows.OfType<Shell.SettingsWindow>().FirstOrDefault();
        if (w == null)
        {
            k.Stop();
            Skip("Settings", "its window did not open");
            return;
        }
        w.ContentRendered += (_, _) => drawn ??= Perf.Ticks();
        await Until(() => drawn != null, 8000);
        var shown = drawn ?? Perf.Ticks();
        var settled = await Perf.Settle(k.T0, shown, StepLimit);
        await k.FramesFor(700);
        rows.Add(k.Row("Settings window", drawn is { } d ? Perf.Ms(k.T0, d) : null, settled is { } t ? Perf.Ms(k.T0, t) : null,
            drawn == null ? "its window had not drawn after 8 s" : settled == null ? Late : null));
        w.Close();
        await Until(() => !w.IsVisible, 3000);
        await Task.Delay(400);
    }

    /// <summary>A search typed into the engine as the search box does, a letter every 150 ms: ui is each letter's
    /// average, data the results after the last one.</summary>
    private static async Task Typing(Engine.Engine e)
    {
        if (finished) return;
        stage = "Search typing";
        var k = new Mark(double.PositiveInfinity);
        var each = new List<double>();
        long last = k.T0, shown = k.T0;
        foreach (var text in Typed)
        {
            if (finished) { k.Stop(); return; }
            last = Perf.Ticks();
            e.Search(text);
            shown = await Perf.Idle();
            each.Add(Perf.Ms(last, shown));
            var wait = 150 - Perf.Ms(last, Perf.Ticks());
            if (wait >= 1) await Task.Delay((int)wait);
        }
        var settled = await Perf.Settle(last, shown, StepLimit);
        await k.FramesFor(700);
        rows.Add(k.Row("Search typing", each.Average(), settled is { } t ? Perf.Ms(last, t) : null,
            Perf.Fmt($"{each.Count} letters 150 ms apart: ui is each one's average (the slowest {each.Max():0} ms), data is from the last")));
        await Task.Delay(250);
    }

    private static async Task Drag(Shell.MainWindow m)
    {
        if (finished) return;
        stage = "Window drag";
        if (m.WindowState != WindowState.Normal) { Skip("Window drag", "the window is maximized"); return; }
        var k = new Mark(double.PositiveInfinity);
        var left = m.Left;
        var each = new List<double>();
        for (var i = 0; i < 60 && !finished; i++)
        {
            var t = Perf.Ticks();
            m.Left += 2;
            each.Add(Perf.Ms(t, await Perf.Idle()));
        }
        await k.FramesFor(700);
        if (each.Count > 0) rows.Add(k.Row("Window drag", each.Average(), null, Perf.Fmt($"{each.Count} moves of 2 px: ui is each one's average (the slowest {each.Max():0} ms)")));
        else k.Stop();
        m.Left = left;
        await Perf.Idle();
        await Task.Delay(250);
    }

    private static async Task Resize(Shell.MainWindow m)
    {
        if (finished) return;
        stage = "Window resize";
        if (m.WindowState != WindowState.Normal) { Skip("Window resize", "the window is maximized"); return; }
        var k = new Mark(double.PositiveInfinity);
        var width = m.Width;
        var each = new List<double>();
        for (var i = 0; i < 30 && !finished; i++)
        {
            var t = Perf.Ticks();
            m.Width = m.ActualWidth + 2;
            each.Add(Perf.Ms(t, await Perf.Idle()));
        }
        await k.FramesFor(700);
        m.Width = width;
        var settled = await Perf.Settle(k.T0, await Perf.Idle(), StepLimit);
        if (each.Count > 0) rows.Add(k.Row("Window resize", each.Average(), settled is { } t2 ? Perf.Ms(k.T0, t2) : null,
            Perf.Fmt($"{each.Count} steps of 2 px, then its size again: ui is each step's average (the slowest {each.Max():0} ms), data all of it")));
        else k.Stop();
        await Task.Delay(250);
    }

    /// <summary>
    /// The window minimized for --perf-minimized seconds (30; 0: not at all) and memory read then, as a student leaves
    /// Simpl down on the taskbar; then brought back as the taskbar does, and timed: drawn again (ui), its frames.
    /// </summary>
    private static async Task Minimized(Shell.MainWindow m)
    {
        var seconds = Launch.Number("perf-minimized", 30);
        if (finished || seconds <= 0) return;
        stage = "minimized";
        m.WindowState = WindowState.Minimized;
        var from = Perf.Ticks();
        while (!finished && Perf.Ms(from, Perf.Ticks()) < seconds * 1000) await Task.Delay(250);
        if (finished) return;
        Remember("minimized");
        stage = "Restore";
        var k = new Mark();
        long? frame = null;
        void First(object? s, EventArgs a) => frame ??= Perf.Ticks();
        CompositionTarget.Rendering += First;
        m.WindowState = WindowState.Normal;
        m.Activate();
        var shown = await Perf.Idle();
        await Until(() => frame != null, 3000);
        CompositionTarget.Rendering -= First;
        await k.FramesFor(700);
        var drawn = Math.Max(shown, frame ?? shown);
        rows.Add(k.Row("Restore", Perf.Ms(k.T0, drawn), null, Perf.Fmt($"after {seconds:0} s minimized: ui is its first frame drawn, the window idle again")));
        await Task.Delay(250);
    }

    /// <summary>Memory now, by the name of the moment (settled, walked, minimized): written with the steps.</summary>
    private static void Remember(string when)
    {
        try
        {
            var m = Perf.Memory();
            memory.Add((when, m));
            Log.Write($"perf walk: memory {when} {Perf.MemoryText(m)}");
        }
        catch (Exception x) { Log.Write($"perf walk: memory {when} not read: {x.Message}"); }
    }

    private static readonly List<(string When, PerfMemory.Reading Reading)> memory = new();

    private const string Late = "answers not all in after 15 s";

    private static void Skip(string name, string why) => rows.Add(new Row(name, null, null, 0, 0, 0, 0, 0, null, null, 0, "skipped: " + why) { Skipped = true });

    /// <summary>True once it holds; false if the walk ended meanwhile, or after <paramref name="limitMs"/>.</summary>
    private static async Task<bool> Until(Func<bool> holds, double limitMs = double.PositiveInfinity)
    {
        var from = Perf.Ticks();
        while (!finished)
        {
            if (holds()) return true;
            if (Perf.Ms(from, Perf.Ticks()) > limitMs) return false;
            await Task.Delay(50);
        }
        return false;
    }

    // ---- a step's numbers ----

    /// <summary>One step: its ui and data times, the builds and calls it took (how many, ms), the longest stall, its
    /// frames and the longest gap between them, MB allocated; a note.</summary>
    private sealed record Row(string Name, double? Ui, double? Data, int Builds, double BuildMs, int Calls, double CallMs, double Stall, int? Frames, double? WorstFrame, double Mb, string? Note)
    {
        public bool Skipped { get; init; }
    }

    /// <summary>
    /// Where a step began: the counts then, and its frames from then (CompositionTarget.Rendering, once per frame drawn —
    /// listened to only meanwhile, as it keeps WPF drawing) for the first 700 ms, or all of a drag, a resize or typing.
    /// </summary>
    private sealed class Mark
    {
        public readonly long T0 = Perf.Ticks();
        private readonly long calls = Perf.CallCount, builds = Perf.BuildCount;
        private readonly double callMs = Perf.CallMs, buildMs = Perf.BuildMs;
        private readonly long allocated = GC.GetTotalAllocatedBytes(false);
        private readonly long end;
        private long last;
        private TimeSpan lastFrame = TimeSpan.MinValue;
        private int frames;
        private double worst;
        private bool listening;

        public Mark(double frameWindowMs = 700)
        {
            Perf.StallWindow(); // (the longest stall from here)
            end = double.IsPositiveInfinity(frameWindowMs) ? long.MaxValue : T0 + (long)(frameWindowMs * System.Diagnostics.Stopwatch.Frequency / 1000);
            last = T0;
            CompositionTarget.Rendering += Frame;
            listening = true;
        }

        private void Frame(object? sender, EventArgs a)
        {
            if (a is RenderingEventArgs r)
            {
                if (r.RenderingTime == lastFrame) return; // (raised more than once for one frame)
                lastFrame = r.RenderingTime;
            }
            var now = Perf.Ticks();
            if (now > end) { Stop(); return; }
            worst = Math.Max(worst, Perf.Ms(last, now));
            last = now;
            frames++;
        }

        /// <summary>No more frames counted (the window over, or after at least <paramref name="ms"/> of a long one).</summary>
        public async Task FramesFor(double ms)
        {
            var left = ms - Perf.Ms(T0, Perf.Ticks());
            if (left >= 1 && listening) await Task.Delay((int)left);
            Stop();
        }

        public void Stop()
        {
            if (!listening) return;
            listening = false;
            CompositionTarget.Rendering -= Frame;
            // (the gap after the last frame, to the window's end, counts too: a window that drew nothing more)
            worst = Math.Max(worst, Perf.Ms(last, Math.Min(Perf.Ticks(), end)));
        }

        public Row Row(string name, double? ui, double? data, string? note)
        {
            Stop();
            return new Row(name, ui, data, (int)(Perf.BuildCount - builds), Perf.BuildMs - buildMs, (int)(Perf.CallCount - calls), Perf.CallMs - callMs,
                Perf.StallWindow(), frames, worst, (GC.GetTotalAllocatedBytes(false) - allocated) / MB, note);
        }
    }

    // ---- the report ----

    /// <summary>Written (what was walked, with a note if not all), said in simpl.log, and Simpl quits.</summary>
    private static void Finish(string? note)
    {
        if (finished) return;
        finished = true;
        Perf.Fast(false);
        try { Write(note); }
        catch (Exception x) { Log.Write("perf walk: not written: " + x); }
        EraseAll.Quit();
    }

    private static void Write(string? note)
    {
        var dir = Launch.Get("perf-walk") is { Length: > 0 } d && d != "true" ? d : AppPaths.Root;
        var name = Launch.Get("perf-name");
        var suffix = string.IsNullOrWhiteSpace(name) ? "" : "-" + name;
        Directory.CreateDirectory(dir);
        var steps = new JsonArray();
        foreach (var r in rows)
            steps.Add(new JsonObject
            {
                ["name"] = r.Name, ["ui"] = R(r.Ui), ["data"] = R(r.Data), ["builds"] = r.Builds, ["buildMs"] = Math.Round(r.BuildMs),
                ["calls"] = r.Calls, ["callMs"] = Math.Round(r.CallMs), ["stall"] = Math.Round(r.Stall), ["frames"] = r.Frames,
                ["worstFrame"] = R(r.WorstFrame), ["mb"] = Math.Round(r.Mb, 1), ["skipped"] = r.Skipped, ["note"] = r.Note,
            });
        var mem = new JsonObject();
        foreach (var (when, m) in memory) mem[when] = Perf.MemoryJson(m);
        mem["end"] = Perf.MemoryJson(Perf.Memory());
        var json = new JsonObject
        {
            ["name"] = name, ["version"] = WhatsNew.Current, ["when"] = DateTime.Now.ToString("s"), ["complete"] = note == null, ["note"] = note,
            ["env"] = Perf.MachineJson(), ["start"] = Perf.StartJson(), ["steps"] = steps, ["calls"] = Perf.CallsJson(), ["builds"] = Perf.BuildsJson(),
            ["switches"] = Perf.SwitchesJson(), ["memory"] = mem, ["stalls"] = Perf.StallsJson(),
            ["args"] = Launch.Get("browser-args") != null ? Engine.Engine.BrowserArguments : null,
            ["webMemory"] = Launch.Get("web-memory"),
        };
        File.WriteAllText(Path.Combine(dir, $"perf{suffix}.json"), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        var text = Text(name, note);
        File.WriteAllText(Path.Combine(dir, $"perf{suffix}.txt"), text);
        foreach (var line in text.Split('\n')) if (line.Length > 0) Log.Write("perf: " + line);
    }

    private static double? R(double? v) => v is { } x ? Math.Round(x) : null;

    private static string Text(string? name, string? note)
    {
        var b = new StringBuilder();
        b.Append(Perf.Fmt($"Simpl {WhatsNew.Current} · perf walk{(string.IsNullOrWhiteSpace(name) ? "" : " " + name)} · {DateTime.Now:yyyy-MM-dd HH:mm} · "))
         .Append(note == null ? "complete" : "not complete: " + note).Append('\n');
        b.Append(Perf.MachineText()).Append('\n');
        b.Append("start (ms from the process's start) ").Append(Perf.StartText()).Append("\n\n");
        b.Append(Line("step", "ui ms", "data ms", "builds · ms", "calls · ms", "stall ms", "frames", "worst gap", "MB"));
        foreach (var r in rows)
            b.Append(r.Skipped
                ? Line(r.Name, "—", "—", "—", "—", "—", "—", "—", "—")
                : Line(r.Name, N(r.Ui), N(r.Data), Perf.Fmt($"{r.Builds} · {r.BuildMs:0}"), Perf.Fmt($"{r.Calls} · {r.CallMs:0}"), N(r.Stall),
                    r.Frames is { } f ? Perf.Fmt($"{f}") : "—", N(r.WorstFrame), Perf.Fmt($"{r.Mb:0.0}")));
        b.Append("(ui: drawn, the window idle again · data: its answers in and shown · stall: the longest the window took to answer\n" +
                 " input · frames drawn in the first 700 ms — a drag, a resize or typing: all of it — and the worst gap between\n" +
                 " them, ms · MB allocated)\n");
        foreach (var r in rows.Where(r => r.Note != null)) b.Append("  ").Append(r.Name).Append(": ").Append(r.Note).Append('\n');
        b.Append("\ncalls (n · p50 · p95 · max ms, most time first)\n");
        foreach (var c in Perf.CallLines(40)) b.Append("  ").Append(c).Append('\n');
        b.Append("builds (n · p50 · max ms)\n");
        foreach (var c in Perf.BuildLines()) b.Append("  ").Append(c).Append('\n');
        b.Append("switches (n · drawn p50 · max · answered p50 · max ms)\n");
        foreach (var c in Perf.SwitchLines()) b.Append("  ").Append(c).Append('\n');
        b.Append("memory, MB (private · working set · private working set)\n");
        foreach (var (when, m) in memory) b.Append("  ").Append(Perf.Fmt($"{when,-10} ")).Append(Perf.MemoryText(m)).Append('\n');
        b.Append("  ").Append(Perf.Fmt($"{"end",-10} ")).Append(Perf.MemoryText()).Append('\n');
        b.Append("stalls  ").Append(Perf.StallText()).Append('\n');
        return b.ToString();
    }

    private static string N(double? v) => v is { } x ? Perf.Fmt($"{x:0}") : "—";

    private static string Line(string name, string ui, string data, string builds, string calls, string stall, string frames, string worst, string mb) =>
        $"{Fit(name, 20),-20} {ui,8} {data,8}  {builds,-12} {calls,-13} {stall,8} {frames,7} {worst,10} {mb,7}\n";

    private static string Fit(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}

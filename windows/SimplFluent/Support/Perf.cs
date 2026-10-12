using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Simpl.Engine;
using Core = Simpl.Engine.Engine;

namespace Simpl.Support;

/// <summary>
/// How long Simpl takes, kept as it runs, in every simpl.log (a student's too): once, how it started — the window drawn,
/// the kept screens up, the web view, the school's page, the counts live, the first screen, its first answers shown —
/// and what it runs on; then, at two minutes, every ten and at exit, the engine's calls, the screens switched to, the
/// window's stalls and memory. Seven lines elsewhere feed it (Program, MainWindow.Open, Engine.CallText, Screen.Render);
/// it never throws into the app, and nothing it does shows. The walk (<see cref="PerfWalk"/>, --perf-walk) reads the
/// same numbers.
/// </summary>
public static class Perf
{
    private const double MB = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // the clock: ms from the process's start (Windows' word for when it began, read once), then a stopwatch
    private static long begun = Stopwatch.GetTimestamp();
    private static double offset;

    // when the start reached each point, each once
    private static readonly Dictionary<string, double> marks = new();
    private static readonly (string Key, string Label)[] Start =
    {
        ("main", "main"), ("window", "window"), ("kept-shell", "kept shell"), ("webview", "webview"), ("nav-start", "nav"),
        ("page", "page"), ("engine-ready", "engine ready"), ("first-screen", "first screen"), ("first-data", "first data"),
    };

    // the engine's calls: in flight now, the last one's end, and when the window was idle after the last to end
    private static readonly Dictionary<string, Series> calls = new();
    private static int inFlight;
    private static long lastActivity, endSeq, idleSeq, idleAt;
    private static long callCount, buildCount;
    private static double callMs, buildMs;

    private static readonly Dictionary<string, Series> builds = new();
    private static readonly Dictionary<string, (Series Ui, Series Data)> switches = new();
    private static int switchSeq;
    private static bool firstScreenAsked, startLogged, envRead;

    // the window's stalls: a probe posted at Input priority every half second (every 50 ms while a walk runs), and how
    // late it ran
    private static Timer? probe;
    private static int probeEvery = 500, probing, outstanding;
    private static long stallCount, stallEnd;
    private static double stallLongest, windowLongest;

    private static Window? main;
    private static Core? engine;
    private static CoreWebView2Environment? webEnv;
    private static readonly Machine env = new();

    // ---- the hooks ----

    /// <summary>Main's first line: the clock set to the process's own start.</summary>
    public static void Begin()
    {
        try
        {
            begun = Stopwatch.GetTimestamp();
            using var p = Process.GetCurrentProcess();
            offset = Math.Max(0, (DateTime.Now - p.StartTime).TotalMilliseconds);
            Mark("main");
        }
        catch { /* (the clock starts at Main instead) */ }
    }

    /// <summary>The window made, not shown yet: its first frame, its stalls, the start said, the summaries, a walk.</summary>
    public static void Watch(Window w)
    {
        try
        {
            main = w;
            w.ContentRendered += (_, _) => Safe(() => { Mark("window"); ReadEnv(w); });
            w.IsVisibleChanged += (_, _) => Safe(Probe);
            w.StateChanged += (_, _) => Safe(Probe);
            Application.Current.Exit += (_, _) => Safe(() =>
            {
                lock (Gate) { probe?.Dispose(); probe = null; }
                if (!startLogged) LogStart();
                Summary("at exit");
            });
            // (the start is said once the first answers are in; after a minute, with what was reached)
            var minute = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
            minute.Tick += (_, _) => Safe(() => { minute.Stop(); if (!startLogged) LogStart(); });
            minute.Start();
            var every = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(2) };
            every.Tick += (_, _) => Safe(() =>
            {
                if (every.Interval < TimeSpan.FromMinutes(10)) every.Interval = TimeSpan.FromMinutes(10);
                Summary(Fmt($"at {Now / 60000:0} min"));
            });
            every.Start();
            if (Launch.Get("perf-walk") != null) PerfWalk.Arm(w);
        }
        catch { /* (no numbers this time) */ }
    }

    /// <summary>The engine made (MainWindow.Open), before anything else follows it: the web view's start, the phases,
    /// the counts live, every screen switched to.</summary>
    public static void Engine(Engine.Engine e)
    {
        try
        {
            if (engine != null) engine.PropertyChanged -= OnEngine;
            engine = e;
            e.PropertyChanged += OnEngine;
            e.Web.CoreWebView2InitializationCompleted += (_, a) => Safe(() => { if (a.IsSuccess && ReferenceEquals(e, engine)) WebReady(e); });
        }
        catch { }
    }

    /// <summary>A call into the page begins (Engine.CallText): its start, for <see cref="CallEnded"/>.</summary>
    public static long CallStarted(string name)
    {
        var t = Stopwatch.GetTimestamp();
        try { lock (Gate) { inFlight++; lastActivity = t; } } catch { }
        return t;
    }

    /// <summary>The call answered, failed or went away.</summary>
    public static void CallEnded(string name, long at)
    {
        try
        {
            var t = Ticks();
            var ms = Ms(at, t);
            long seq = 0;
            lock (Gate)
            {
                inFlight = Math.Max(0, inFlight - 1);
                lastActivity = t;
                callCount++;
                callMs += ms;
                Of(calls, name).Add(ms);
                if (inFlight == 0) seq = ++endSeq;
            }
            // (nothing left in flight: when the window has done what the answer asked of it — built, laid out — it is idle)
            if (seq > 0) Application.Current?.Dispatcher.InvokeAsync(() => Safe(() => { lock (Gate) if (seq == endSeq) { idleSeq = seq; idleAt = Ticks(); } }), DispatcherPriority.ContextIdle);
        }
        catch { }
    }

    /// <summary>Now, for <see cref="Built"/> (Screen.Render, before Build).</summary>
    public static long Stamp() => Stopwatch.GetTimestamp();

    /// <summary>A screen built and put in place (Screen.Render).</summary>
    public static void Built(string screen, long at, bool first)
    {
        try
        {
            var ms = Ms(at, Ticks());
            lock (Gate)
            {
                buildCount++;
                buildMs += ms;
                Of(builds, screen).Add(ms);
            }
            if (!firstScreenAsked)
            {
                firstScreenAsked = true;
                _ = Then(async () => Mark("first-screen", await Idle()));
            }
        }
        catch { }
    }

    // ---- what the engine says ----

    private static void OnEngine(object? sender, PropertyChangedEventArgs a)
    {
        try
        {
            if (!ReferenceEquals(sender, engine) || engine is not { } e) return;
            switch (a.PropertyName)
            {
                case nameof(Core.Phase):
                    // (the app's screens on what was kept, before the page was even asked for — not the page's own ready)
                    if (e.Phase == Phase.Native && !e.CountsLive && !Has("nav-start")) Mark("kept-shell");
                    break;
                case nameof(Core.CountsLive):
                    if (e.CountsLive && !Has("engine-ready"))
                    {
                        var at = Ticks();
                        Mark("engine-ready");
                        _ = Then(async () =>
                        {
                            if (await Settle(at, await Idle(), 60_000) is { } t) Mark("first-data", t);
                            if (!startLogged) LogStart();
                        });
                    }
                    break;
                case nameof(Core.NavVersion):
                    _ = Then(() => Switched(e));
                    break;
            }
        }
        catch { }
    }

    /// <summary>A screen switched to: drawn (the window idle after the switch — Screen.Render's build, at Background, and
    /// the layout come before ContextIdle), then its answers in and shown. Kept by the kind of screen.</summary>
    private static async Task Switched(Core e)
    {
        var seq = ++switchSeq;
        var t0 = Ticks();
        var at = e.Nav.Current;
        var kind = at.Path.Count > 0 ? at.Path[^1].Kind.ToString() : at.Place.Kind == PlaceKind.Section ? $"Section {at.Place.Arg}" : at.Place.Kind.ToString();
        var shown = await Idle();
        var ui = Ms(t0, shown);
        // (another switch before the answers came: this one's are no longer its own)
        var settled = await Settle(t0, shown, 20_000, () => seq == switchSeq);
        lock (Gate)
        {
            if (!switches.TryGetValue(kind, out var s)) switches[kind] = s = (new Series(), new Series());
            s.Ui.Add(ui);
            if (settled is { } d) s.Data.Add(Ms(t0, d));
        }
    }

    private static void WebReady(Core e)
    {
        Mark("webview");
        var core = e.Web.CoreWebView2;
        if (core == null) return;
        webEnv = core.Environment;
        // (the first of each counts; the rest change nothing)
        core.NavigationStarting += (_, _) => Safe(() => Mark("nav-start"));
        core.NavigationCompleted += (_, _) => Safe(() => Mark("page"));
    }

    // ---- settling ----

    /// <summary>
    /// When what was asked from <paramref name="from"/> on was in and shown: the first stretch of 300 ms with no call in
    /// flight, and the window idle after the last answer — or, nothing asked meanwhile, when it was drawn
    /// (<paramref name="shown"/>). Null if not by <paramref name="limitMs"/>, or no longer wanted.
    /// </summary>
    internal static async Task<long?> Settle(long from, long shown, double limitMs, Func<bool>? wanted = null)
    {
        while (true)
        {
            var now = Ticks();
            if (Ms(from, now) > limitMs || wanted?.Invoke() == false) return null;
            lock (Gate)
            {
                if (inFlight == 0)
                {
                    if (lastActivity < from) { if (Ms(Math.Max(from, shown), now) >= 300) return shown; }
                    else if (Ms(lastActivity, now) >= 300 && idleSeq == endSeq) return Math.Max(idleAt, shown);
                }
            }
            await Task.Delay(30);
        }
    }

    /// <summary>When the window is next idle: everything asked of it so far done (built, laid out), input included —
    /// the moment taken there, not where it is awaited (an answer arriving meanwhile would come first).</summary>
    internal static Task<long> Idle() => (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(Ticks, DispatcherPriority.ContextIdle).Task;

    // ---- the window's stalls ----

    /// <summary>The probe runs while the window shows (not hidden in the notification area, not minimized).</summary>
    private static void Probe()
    {
        var on = main is { IsVisible: true } w && w.WindowState != WindowState.Minimized;
        lock (Gate)
        {
            if (!on) { probe?.Dispose(); probe = null; probing = 0; return; }
            if (probe != null && probing == probeEvery) return;
            probe ??= new Timer(_ => Post());
            probe.Change(probeEvery, probeEvery);
            probing = probeEvery;
        }
    }

    private static void Post()
    {
        try
        {
            // (a window that does not answer at all is not given an endless queue of probes)
            if (Interlocked.Increment(ref outstanding) > 40) { Interlocked.Decrement(ref outstanding); return; }
            var posted = Stopwatch.GetTimestamp();
            main?.Dispatcher.InvokeAsync(() => Probed(posted), DispatcherPriority.Input);
        }
        catch { }
    }

    private static void Probed(long posted)
    {
        Interlocked.Decrement(ref outstanding);
        var now = Ticks();
        var late = Ms(posted, now);
        lock (Gate)
        {
            windowLongest = Math.Max(windowLongest, late);
            stallLongest = Math.Max(stallLongest, late);
            // (one stall, however many probes waited through it: they all run as it ends)
            if (late > 200 && posted >= stallEnd) { stallCount++; stallEnd = now; }
        }
    }

    /// <summary>The walk's finer probe (every 50 ms), or the usual one.</summary>
    internal static void Fast(bool on)
    {
        try
        {
            probeEvery = on ? 50 : 500;
            main?.Dispatcher.Invoke(Probe);
        }
        catch { }
    }

    /// <summary>The longest stall since this was last asked.</summary>
    internal static double StallWindow()
    {
        lock (Gate)
        {
            var w = windowLongest;
            windowLongest = 0;
            return w;
        }
    }

    // ---- the clock ----

    internal static long Ticks() => Stopwatch.GetTimestamp();

    internal static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    /// <summary>A moment, in ms from the process's start.</summary>
    internal static double At(long t) => offset + Ms(begun, t);

    internal static double Now => At(Ticks());

    private static void Mark(string name, long? at = null)
    {
        var ms = At(at ?? Ticks());
        lock (Gate) marks.TryAdd(name, ms);
    }

    private static bool Has(string name) { lock (Gate) return marks.ContainsKey(name); }

    /// <summary>When the start reached a point (ms from the process's start), if it has.</summary>
    internal static double? Reached(string name) { lock (Gate) return marks.TryGetValue(name, out var v) ? v : null; }

    internal static Core? Current => engine;
    internal static long CallCount { get { lock (Gate) return callCount; } }
    internal static double CallMs { get { lock (Gate) return callMs; } }
    internal static long BuildCount { get { lock (Gate) return buildCount; } }
    internal static double BuildMs { get { lock (Gate) return buildMs; } }
    internal static double LongestStall { get { lock (Gate) return stallLongest; } }

    // ---- what it runs on ----

    /// <summary>What the PC is (Windows has the render tier right only once the window has drawn).</summary>
    private sealed class Machine
    {
        public int? Tier;
        public bool? Motion;
        public double? Scale;
        public string? Os, OsArch, ProcessArch, Cpu, Maker, WebView;
        public bool Emulated;
        public int Cpus;
        public double Ram;
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

    private static int Tier() => RenderCapability.Tier >> 16;

    private static void ReadEnv(Window w)
    {
        if (envRead) return;
        envRead = true;
        env.Tier = Tier();
        env.Motion = Kit.Motion.On;
        env.Scale = VisualTreeHelper.GetDpi(w).DpiScaleX;
        RenderCapability.TierChanged += (_, _) => Safe(() => { env.Tier = Tier(); Log.Write($"perf: render tier now {TierText(env.Tier)}"); });
        // (the registry, WebView2's version: away from the window)
        _ = Task.Run(() =>
        {
            try { ReadMachine(); } catch { }
        });
    }

    private static void ReadMachine()
    {
        var v = Environment.OSVersion.Version;
        env.Os = $"Windows {v.Major}.{v.Minor}.{v.Build}";
        env.ProcessArch = RuntimeInformation.ProcessArchitecture.ToString();
        env.OsArch = RuntimeInformation.OSArchitecture.ToString();
        try
        {
            // (Windows' own word: an x64 Simpl on an ARM64 PC runs emulated, which .NET may not say)
            if (IsWow64Process2(GetCurrentProcess(), out var process, out var native))
            {
                env.OsArch = native switch { 0xAA64 => "Arm64", 0x8664 => "X64", 0x014C => "X86", _ => env.OsArch };
                env.Emulated = process != 0 || (native == 0xAA64 && RuntimeInformation.ProcessArchitecture != Architecture.Arm64);
            }
        }
        catch { /* (before Windows 10 1709: .NET's word) */ }
        env.Cpus = Environment.ProcessorCount;
        env.Ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * MB);
        try
        {
            using var cpu = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            env.Cpu = (cpu?.GetValue("ProcessorNameString") as string)?.Trim();
            using var bios = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var maker = string.Join(" ", new[] { bios?.GetValue("SystemManufacturer") as string, bios?.GetValue("SystemProductName") as string }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            env.Maker = maker.Length > 0 ? maker : null;
        }
        catch { }
        try { env.WebView = CoreWebView2Environment.GetAvailableBrowserVersionString(null); } catch { }
    }

    private static string TierText(int? t) => t switch { 0 => "0 (software)", 1 => "1 (partial)", 2 => "2 (hardware)", null => "—", _ => t.Value.ToString(Inv) };

    /// <summary>One line: the render tier, motion, Windows and the processor against Simpl's, the processors, memory,
    /// the display's scale, the PC's maker and model (a VM's), WebView2.</summary>
    internal static string MachineText()
    {
        var parts = new List<string>
        {
            "tier " + TierText(env.Tier),
            env.Motion switch { true => "motion on", false => "motion off", null => "motion —" },
            $"{env.Os ?? "Windows"} {env.OsArch} (process {env.ProcessArch}{(env.Emulated ? ", emulated" : "")})",
            Fmt($"{env.Cpus} cpus") + (env.Cpu is { } c ? " " + c : ""),
            env.Ram > 0 ? Fmt($"{env.Ram:0} GB") : "— GB",
            env.Scale is { } s ? Fmt($"{s * 100:0}%") : "—",
        };
        if (env.Maker != null) parts.Add(env.Maker);
        parts.Add("WebView2 " + (env.WebView ?? "—"));
        return string.Join(" · ", parts);
    }

    internal static JsonObject MachineJson() => new()
    {
        ["tier"] = env.Tier, ["motion"] = env.Motion, ["os"] = env.Os, ["osArch"] = env.OsArch, ["processArch"] = env.ProcessArch,
        ["emulated"] = env.Emulated, ["cpus"] = env.Cpus, ["cpu"] = env.Cpu, ["ramGB"] = Math.Round(env.Ram, 1),
        ["scale"] = env.Scale, ["maker"] = env.Maker, ["webView2"] = env.WebView, ["version"] = WhatsNew.Current,
    };

    // ---- what is said ----

    /// <summary>How the start went: each point in ms from the process's start ("—": not reached).</summary>
    internal static string StartText()
    {
        lock (Gate) return string.Join(" · ", Start.Select(s => s.Label + " " + (marks.TryGetValue(s.Key, out var v) ? Fmt($"{v:0}") : "—")));
    }

    internal static JsonObject StartJson()
    {
        var o = new JsonObject();
        lock (Gate) foreach (var (key, _) in Start) o[key] = marks.TryGetValue(key, out var v) ? Math.Round(v) : null;
        return o;
    }

    private static void LogStart()
    {
        startLogged = true;
        Log.Write($"perf: start {StartText()} ms | {MachineText()}");
    }

    private static void Summary(string when)
    {
        Log.Write($"perf: calls {when} (n · p50 · p95 · max ms, most time first) {string.Join("; ", CallLines(12))}");
        Log.Write($"perf: switches {when} (n · drawn p50 · max · answered p50 · max ms) {string.Join("; ", SwitchLines())}");
        Log.Write($"perf: stalls {when} {StallText()}");
        Log.Write($"perf: memory {when} {MemoryText()}");
    }

    internal static IEnumerable<string> CallLines(int top)
    {
        lock (Gate)
            return calls.OrderByDescending(c => c.Value.Total).Take(top)
                .Select(c => Fmt($"{c.Key} {c.Value.N} · {c.Value.P(0.5):0} · {c.Value.P(0.95):0} · {c.Value.Max:0}")).ToList();
    }

    internal static IEnumerable<string> BuildLines()
    {
        lock (Gate)
            return builds.OrderByDescending(b => b.Value.Total)
                .Select(b => Fmt($"{b.Key} {b.Value.N} · {b.Value.P(0.5):0} · {b.Value.Max:0}")).ToList();
    }

    internal static IEnumerable<string> SwitchLines()
    {
        lock (Gate)
            return switches.OrderByDescending(s => s.Value.Ui.N)
                .Select(s => Fmt($"{s.Key} {s.Value.Ui.N} · {s.Value.Ui.P(0.5):0} · {s.Value.Ui.Max:0} · ") +
                             (s.Value.Data.N > 0 ? Fmt($"{s.Value.Data.P(0.5):0} · {s.Value.Data.Max:0}") : "— · —")).ToList();
    }

    internal static JsonObject CallsJson() { lock (Gate) return SeriesJson(calls, p95: true); }

    internal static JsonObject BuildsJson() { lock (Gate) return SeriesJson(builds, p95: false); }

    internal static JsonObject SwitchesJson()
    {
        var o = new JsonObject();
        lock (Gate)
            foreach (var (k, s) in switches)
                o[k] = new JsonObject
                {
                    ["n"] = s.Ui.N, ["uiP50"] = Math.Round(s.Ui.P(0.5)), ["uiMax"] = Math.Round(s.Ui.Max),
                    ["dataP50"] = s.Data.N > 0 ? Math.Round(s.Data.P(0.5)) : null, ["dataMax"] = s.Data.N > 0 ? Math.Round(s.Data.Max) : null,
                };
        return o;
    }

    private static JsonObject SeriesJson(Dictionary<string, Series> all, bool p95)
    {
        var o = new JsonObject();
        foreach (var (k, s) in all.OrderByDescending(x => x.Value.Total))
        {
            var e = new JsonObject { ["n"] = s.N, ["p50"] = Math.Round(s.P(0.5)) };
            if (p95) e["p95"] = Math.Round(s.P(0.95));
            e["max"] = Math.Round(s.Max);
            e["total"] = Math.Round(s.Total);
            o[k] = e;
        }
        return o;
    }

    internal static string StallText()
    {
        lock (Gate) return Fmt($"{stallCount} over 200 ms, the longest {stallLongest:0} ms");
    }

    internal static JsonObject StallsJson()
    {
        lock (Gate) return new JsonObject { ["over200"] = stallCount, ["longest"] = Math.Round(stallLongest) };
    }

    /// <summary>Memory now: .NET's (and its collections), the process's, and the web view's own processes'.</summary>
    private sealed record Memory(double Managed, int Gc0, int Gc1, int Gc2, double Private, double WorkingSet, double? Web, int WebProcesses);

    private static Memory ReadMemory()
    {
        using var p = Process.GetCurrentProcess();
        double? web = null;
        var count = 0;
        try
        {
            if (webEnv != null)
            {
                double sum = 0;
                foreach (var info in webEnv.GetProcessInfos())
                {
                    try { using var wp = Process.GetProcessById(info.ProcessId); sum += wp.WorkingSet64; count++; } catch { }
                }
                web = sum / MB;
            }
        }
        catch { /* (the web view gone, at exit) */ }
        return new Memory(GC.GetTotalMemory(false) / MB, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), p.PrivateMemorySize64 / MB, p.WorkingSet64 / MB, web, count);
    }

    internal static string MemoryText()
    {
        var m = ReadMemory();
        return Fmt($"managed {m.Managed:0} MB · gc {m.Gc0}/{m.Gc1}/{m.Gc2} · private {m.Private:0} MB · working set {m.WorkingSet:0} MB · web view ") +
               (m.Web is { } w ? Fmt($"{w:0} MB in {m.WebProcesses} processes") : "—");
    }

    internal static JsonObject MemoryJson()
    {
        var m = ReadMemory();
        return new JsonObject
        {
            ["managedMB"] = Math.Round(m.Managed, 1), ["gc0"] = m.Gc0, ["gc1"] = m.Gc1, ["gc2"] = m.Gc2,
            ["privateMB"] = Math.Round(m.Private, 1), ["workingSetMB"] = Math.Round(m.WorkingSet, 1),
            ["webViewMB"] = m.Web is { } w ? Math.Round(w, 1) : null, ["webViewProcesses"] = m.WebProcesses,
        };
    }

    // ---- pieces ----

    /// <summary>A run of timings: how many, their total and the longest of all, and the last 200 for the percentiles.</summary>
    private sealed class Series
    {
        private readonly double[] last = new double[200];
        private int next;
        public long N;
        public double Total, Max;

        public void Add(double ms)
        {
            last[next] = ms;
            next = (next + 1) % last.Length;
            N++;
            Total += ms;
            Max = Math.Max(Max, ms);
        }

        public double P(double q)
        {
            var n = (int)Math.Min(N, last.Length);
            if (n == 0) return 0;
            var sorted = last.Take(n).OrderBy(x => x).ToArray();
            return sorted[Math.Clamp((int)Math.Ceiling(q * n) - 1, 0, n - 1)];
        }
    }

    private static Series Of(Dictionary<string, Series> all, string name)
    {
        if (!all.TryGetValue(name, out var s)) all[name] = s = new Series();
        return s;
    }

    internal static string Fmt(FormattableString s) => s.ToString(Inv);

    private static void Safe(Action a)
    {
        try { a(); } catch { /* (the numbers are never worth the app) */ }
    }

    private static async Task Then(Func<Task> a)
    {
        try { await a(); } catch { }
    }
}

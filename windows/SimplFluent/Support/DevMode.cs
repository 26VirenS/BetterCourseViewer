using System.Globalization;
using System.Text.Json.Nodes;

namespace Simpl.Support;

/// <summary>
/// (0.3.27, the Mac's DevMode) The developer tools, kept hidden: in with seven clicks on Settings ▸ About ▸ Version within
/// four seconds, or Ctrl+Alt+Shift+D anywhere in the main window (which takes them away again). Once in, the Developer
/// window (Shell/DeveloperWindow), Save Page Code in Alta's popup, and Alta's Error logs. Not
/// <see cref="Launch.IsDevelopment"/>, which is a test run's.
/// </summary>
public static class DevMode
{
    private const string Key = "developer";
    private static readonly List<long> taps = new();

    public static bool On => Prefs.Bool(Key);

    /// <summary>Turned on or off (the popups' buttons, About's row).</summary>
    public static event Action? Changed;

    /// <summary>The window asked for (the shell opens it: it has the engine).</summary>
    public static event Action? OpenAsked;

    /// <summary>A click on About's Version: the seventh within four seconds lets you in (or, in already, opens the window).</summary>
    public static void Tap()
    {
        var now = Environment.TickCount64;
        taps.RemoveAll(t => now - t > 4000);
        taps.Add(now);
        if (taps.Count < 7) return;
        taps.Clear();
        if (On) OpenAsked?.Invoke(); else Unlock();
    }

    public static void Unlock()
    {
        Prefs.Set(Key, true);
        Log.Write("developer tools on");
        System.Media.SystemSounds.Asterisk.Play();
        Changed?.Invoke();
        OpenAsked?.Invoke();
    }

    public static void Lock()
    {
        Prefs.Remove(Key);
        Log.Write("developer tools off");
        Changed?.Invoke();
    }

    public static void Toggle()
    {
        if (On) Lock(); else Unlock();
    }
}

/// <summary>
/// (0.3.27) Every call into the page since Simpl started (or since Reset), for the Developer window's Stats: how many, how
/// many failed, the last one's time and when, the middle, the 95th and the longest, and the time all took (Perf's own
/// series, which the log's summary uses, stay as they are).
/// </summary>
public static class DevStats
{
    public sealed class Entry
    {
        private readonly double[] last = new double[200];
        private int next;
        public string Name = "";
        public long N, Failed;
        public double Total, Max, Last;
        public DateTime At;

        public void Add(double ms, bool ok)
        {
            last[next] = ms;
            next = (next + 1) % last.Length;
            N++;
            if (!ok) Failed++;
            Total += ms;
            Max = Math.Max(Max, ms);
            Last = ms;
            At = DateTime.Now;
        }

        public double P(double q)
        {
            var n = (int)Math.Min(N, last.Length);
            if (n == 0) return 0;
            var sorted = last.Take(n).OrderBy(x => x).ToArray();
            return sorted[Math.Clamp((int)Math.Ceiling(q * n) - 1, 0, n - 1)];
        }

        public double Mean => N == 0 ? 0 : Total / N;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> all = new();
    public static DateTime Since { get; private set; } = DateTime.Now;

    public static void Record(string name, double ms, bool ok)
    {
        try
        {
            lock (Gate)
            {
                if (!all.TryGetValue(name, out var e)) all[name] = e = new Entry { Name = name };
                e.Add(ms, ok);
            }
        }
        catch { }
    }

    /// <summary>A copy of each, to draw from (the numbers keep coming meanwhile).</summary>
    public static List<(string Name, long N, long Failed, double Mean, double P50, double P95, double Max, double Last, DateTime At, double Total)> Rows()
    {
        lock (Gate) return all.Values.Select(e => (e.Name, e.N, e.Failed, e.Mean, e.P(0.5), e.P(0.95), e.Max, e.Last, e.At, e.Total)).ToList();
    }

    public static void Reset()
    {
        lock (Gate) all.Clear();
        Since = DateTime.Now;
    }

    public static string Text()
    {
        var inv = CultureInfo.InvariantCulture;
        var lines = new List<string> { $"Simpl for Windows {Updates.Version}: calls since {Since.ToString("G", inv)} (n · failed · mean · p50 · p95 · max · last ms)" };
        lines.AddRange(Rows().OrderByDescending(r => r.Total).Select(r => string.Create(inv, $"{r.Name}\t{r.N}\t{r.Failed}\t{r.Mean:0}\t{r.P50:0}\t{r.P95:0}\t{r.Max:0}\t{r.Last:0}")));
        return string.Join("\n", lines);
    }

    public static JsonObject Json()
    {
        var o = new JsonObject();
        foreach (var r in Rows().OrderByDescending(r => r.Total))
            o[r.Name] = new JsonObject { ["n"] = r.N, ["failed"] = r.Failed, ["mean"] = Math.Round(r.Mean), ["p50"] = Math.Round(r.P50), ["p95"] = Math.Round(r.P95), ["max"] = Math.Round(r.Max) };
        return o;
    }
}

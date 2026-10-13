using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Simpl.Support;

/// <summary>
/// Simpl giving memory back when it is not being used. Left alone a moment — nothing pressed or typed in its window for
/// ten seconds, nothing asked of the page, a good deal made and thrown away since last time — .NET is asked to collect
/// and hand its free memory back to Windows (once a minute at most; a fraction of a second, while nobody is using it).
/// Minimized or in the notification area for twenty seconds: the same, then the web view's page is asked to keep to
/// little memory (WebView2 hands its own back) and Windows takes the pages Simpl is not using out of RAM (its working
/// set) — never sooner, so a window brought straight back has everything where it was; brought back later, the few
/// pages it draws with come back from Windows' own standby memory, not the disk. Back in view, the page is asked for its
/// pace again at once. The web view's other processes (its browser, its GPU process) are trimmed the same way.
/// </summary>
public static class MemoryTidy
{
    /// <summary>How long the window must be left alone, and minimized, before memory is given back.</summary>
    private const int IdleMs = 10_000, AwayMs = 20_000;
    /// <summary>At most once a minute while the window shows, and only after this much was made and thrown away.</summary>
    private const int EveryMs = 60_000;
    private const long Churn = 24L * 1024 * 1024;

    private static Window? window;
    private static Func<Engine.Engine?>? engine;
    private static long lastInput = Environment.TickCount64, awaySince, lastTidy, allocatedAt;
    private static bool trimmed;

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    /// <summary>The main window, watched from its making (and the engine it shows, once there is one).</summary>
    public static void Watch(Window w, Func<Engine.Engine?> e)
    {
        if (Launch.Flag("no-tidy") || Shot.Asked) return;
        window = w;
        engine = e;
        allocatedAt = GC.GetTotalAllocatedBytes(false);
        InputManager.Current.PostProcessInput += (_, a) =>
        {
            if (a.StagingItem.Input is MouseEventArgs or KeyboardEventArgs or TouchEventArgs or StylusEventArgs) lastInput = Environment.TickCount64;
        };
        w.StateChanged += (_, _) => Moved();
        w.IsVisibleChanged += (_, _) => Moved();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    private static bool Away => window is not { IsVisible: true } w || w.WindowState == WindowState.Minimized;

    /// <summary>Minimized, hidden or back: back in view, the page has its pace at once.</summary>
    private static void Moved()
    {
        if (Away) { if (awaySince == 0) awaySince = Environment.TickCount64; return; }
        awaySince = 0;
        if (!trimmed) return;
        trimmed = false;
        Lean(false);
    }

    private static void Tick()
    {
        try
        {
            var now = Environment.TickCount64;
            if (Away)
            {
                if (awaySince == 0) awaySince = now;
                if (trimmed || now - awaySince < AwayMs) return;
                trimmed = true;
                Lean(true);
                Collect(full: true);
                SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
                var web = TrimStarted();
                Log.Write($"memory: given back after {(now - awaySince) / 1000} s away (and by {web} of the web view's processes)");
                return;
            }
            var e = engine?.Invoke();
            if (now - lastInput < IdleMs || now - lastTidy < EveryMs) return;
            if (e != null && (e.CallsInFlight > 0 || now - e.LastCallAt < IdleMs)) return;
            if (GC.GetTotalAllocatedBytes(false) - allocatedAt < Churn) return;
            Collect(full: false);
        }
        catch (Exception x) { Log.Write("memory: " + x.Message); }
    }

    /// <summary>
    /// The web view's own processes (its browser, GPU process and utilities; its page's renderer has given its memory back
    /// already, asked to keep to little) out of RAM too, as Simpl's own: what they need again comes back from Windows'
    /// standby memory as they touch it. How many could be asked.
    /// </summary>
    private static int TrimStarted()
    {
        const uint SetQuota = 0x0100, QueryLimited = 0x1000;
        var n = 0;
        foreach (var pid in PerfMemory.Descendants())
        {
            var h = OpenProcess(SetQuota | QueryLimited, false, pid);
            if (h == IntPtr.Zero) continue;
            try { if (SetProcessWorkingSetSize(h, -1, -1)) n++; }
            finally { CloseHandle(h); }
        }
        return n;
    }

    /// <summary>.NET's free memory handed back to Windows: a compacting collection that gives back all it can (and,
    /// <paramref name="full"/>, what the finalizers then let go of — the native memory behind the trees let go).</summary>
    private static void Collect(bool full)
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        if (full)
        {
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        lastTidy = Environment.TickCount64;
        allocatedAt = GC.GetTotalAllocatedBytes(false);
    }

    /// <summary>The page (and the tools' web views) asked to keep to little memory, or for their pace again.</summary>
    private static void Lean(bool on)
    {
        try { engine?.Invoke()?.Lean(on); } catch { }
        try { Tools.GraphWeb.Lean(on); } catch { }
    }
}

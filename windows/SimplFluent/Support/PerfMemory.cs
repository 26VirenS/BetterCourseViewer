using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Simpl.Support;

/// <summary>
/// Memory as Windows counts it, for Simpl and every process it started: Simpl's own, and WebView2's — its browser, the
/// renderers, the GPU process, the utilities, the crash reporter — each with its private bytes (what it alone has
/// committed), its working set (what of it is in RAM now, the pages it shares with others too) and its private working
/// set (Task Manager's Memory column). Every process is read in one call to Windows (NtQuerySystemInformation), none
/// opened, so a sandboxed renderer is read like the rest; WebView2 names what each of its own is.
/// </summary>
internal static class PerfMemory
{
    private const double MB = 1024 * 1024;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>One process: its id, what it is, and its private bytes, working set and private working set, MB.</summary>
    internal sealed record Proc(int Pid, string Kind, double Private, double WorkingSet, double PrivateWs);

    /// <summary>Simpl's process, WebView2's (and anything else Simpl started), and .NET's own view of its heap.</summary>
    internal sealed record Reading(Proc Simpl, IReadOnlyList<Proc> Web, double Managed, double GcHeap, double GcCommitted, double GcFragmented)
    {
        public double WebPrivate => Web.Sum(p => p.Private);
        public double WebWorkingSet => Web.Sum(p => p.WorkingSet);
        public double WebPrivateWs => Web.Sum(p => p.PrivateWs);
        public double Private => Simpl.Private + WebPrivate;
        public double WorkingSet => Simpl.WorkingSet + WebWorkingSet;
        public double PrivateWs => Simpl.PrivateWs + WebPrivateWs;
    }

    /// <summary>Every process Simpl started, and the ones they started (WebView2's), by id.</summary>
    public static IReadOnlyList<int> Descendants()
    {
        var me = Environment.ProcessId;
        var all = Snapshot();
        return Under(me, all).Where(p => p != me).ToList();
    }

    /// <summary>The process and every process whose parent, or its parent's parent…, it is (a parent's id reused by a
    /// later process is not taken for it: a child is never older than its parent).</summary>
    private static HashSet<int> Under(int me, Dictionary<int, Raw> all)
    {
        var under = new HashSet<int> { me };
        bool grew;
        do
        {
            grew = false;
            foreach (var p in all.Values)
                if (!under.Contains(p.Pid) && under.Contains(p.Parent) && p.Created >= (all.TryGetValue(p.Parent, out var parent) ? parent.Created : 0))
                {
                    under.Add(p.Pid);
                    grew = true;
                }
        } while (grew);
        return under;
    }

    /// <summary>Now: Simpl, and every process under it (WebView2's kinds named by <paramref name="env"/> where it knows them).</summary>
    public static Reading Read(CoreWebView2Environment? env)
    {
        var me = Environment.ProcessId;
        var all = Snapshot();
        var kinds = new Dictionary<int, string>();
        try
        {
            if (env != null)
                foreach (var info in env.GetProcessInfos())
                    kinds[info.ProcessId] = info.Kind switch
                    {
                        CoreWebView2ProcessKind.Browser => "browser",
                        CoreWebView2ProcessKind.Renderer => "renderer",
                        CoreWebView2ProcessKind.Gpu => "gpu",
                        CoreWebView2ProcessKind.Utility => "utility",
                        _ => info.Kind.ToString().ToLowerInvariant(),
                    };
        }
        catch { /* (the web view gone, at exit) */ }
        // (every process whose parent, or its parent's parent…, is Simpl: WebView2's browser is Simpl's child, the rest are its)
        var under = Under(me, all);
        Proc Of(Raw r, string kind) => new(r.Pid, kind, r.Private / MB, r.WorkingSet / MB, r.PrivateWs / MB);
        var simpl = all.TryGetValue(me, out var self) ? Of(self, "simpl") : Fallback();
        var web = all.Values.Where(p => p.Pid != me && under.Contains(p.Pid))
            .Select(p => Of(p, kinds.TryGetValue(p.Pid, out var k) ? k : "other"))
            .OrderBy(p => Order(p.Kind)).ThenByDescending(p => p.Private).ToList();
        var gc = GC.GetGCMemoryInfo();
        return new Reading(simpl, web, GC.GetTotalMemory(false) / MB, gc.HeapSizeBytes / MB, gc.TotalCommittedBytes / MB, gc.FragmentedBytes / MB);
    }

    private static int Order(string kind) => kind switch { "browser" => 0, "renderer" => 1, "gpu" => 2, "utility" => 3, _ => 4 };

    /// <summary>Simpl's own, as .NET reads it (no private working set), should Windows' list not have it.</summary>
    private static Proc Fallback()
    {
        using var p = Process.GetCurrentProcess();
        return new Proc(p.Id, "simpl", p.PrivateMemorySize64 / MB, p.WorkingSet64 / MB, 0);
    }

    // ---- said ----

    /// <summary>One line: Simpl, WebView2 (by kind) and the two together, each private · working set · private working
    /// set; then .NET's heap.</summary>
    public static string Text(Reading m)
    {
        string Three(double a, double b, double c) => string.Format(Inv, "{0:0} · {1:0} · {2:0}", a, b, c);
        var kinds = m.Web.GroupBy(p => p.Kind).Select(g =>
            string.Format(Inv, "{0}{1} {2}", g.Key, g.Count() > 1 ? $" ×{g.Count()}" : "", Three(g.Sum(p => p.Private), g.Sum(p => p.WorkingSet), g.Sum(p => p.PrivateWs))));
        return string.Format(Inv, "all {0} MB (private · working set · private working set) = Simpl {1} + WebView2 {2} in {3} processes ({4}) · .NET {5:0} MB in use, heap {6:0}, committed {7:0}, fragmented {8:0}",
            Three(m.Private, m.WorkingSet, m.PrivateWs), Three(m.Simpl.Private, m.Simpl.WorkingSet, m.Simpl.PrivateWs),
            Three(m.WebPrivate, m.WebWorkingSet, m.WebPrivateWs), m.Web.Count, string.Join(", ", kinds),
            m.Managed, m.GcHeap, m.GcCommitted, m.GcFragmented);
    }

    public static JsonObject Json(Reading m)
    {
        static JsonObject Three(double a, double b, double c) => new() { ["private"] = Math.Round(a, 1), ["workingSet"] = Math.Round(b, 1), ["privateWs"] = Math.Round(c, 1) };
        var kinds = new JsonObject();
        foreach (var g in m.Web.GroupBy(p => p.Kind))
        {
            var o = Three(g.Sum(p => p.Private), g.Sum(p => p.WorkingSet), g.Sum(p => p.PrivateWs));
            o["n"] = g.Count();
            kinds[g.Key] = o;
        }
        var web = Three(m.WebPrivate, m.WebWorkingSet, m.WebPrivateWs);
        web["processes"] = m.Web.Count;
        web["kinds"] = kinds;
        return new JsonObject
        {
            ["total"] = Three(m.Private, m.WorkingSet, m.PrivateWs),
            ["simpl"] = Three(m.Simpl.Private, m.Simpl.WorkingSet, m.Simpl.PrivateWs),
            ["webView"] = web,
            ["managedMB"] = Math.Round(m.Managed, 1), ["gcHeapMB"] = Math.Round(m.GcHeap, 1),
            ["gcCommittedMB"] = Math.Round(m.GcCommitted, 1), ["gcFragmentedMB"] = Math.Round(m.GcFragmented, 1),
        };
    }

    // ---- Windows' list of processes ----

    private readonly record struct Raw(int Pid, int Parent, long Created, double Private, double WorkingSet, double PrivateWs);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returned);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length, MaximumLength;
        public IntPtr Buffer;
    }

    /// <summary>The start of SYSTEM_PROCESS_INFORMATION, as far as its private page count.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize;
        public uint HardFaultCount;
        public uint NumberOfThreadsHighWatermark;
        public ulong CycleTime;
        public long CreateTime;
        public long UserTime;
        public long KernelTime;
        public UnicodeString ImageName;
        public int BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
        public uint HandleCount;
        public uint SessionId;
        public UIntPtr UniqueProcessKey;
        public UIntPtr PeakVirtualSize;
        public UIntPtr VirtualSize;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivatePageCount;
    }

    /// <summary>Every process now, by id (SystemProcessInformation; the buffer grown until it holds them all).</summary>
    private static Dictionary<int, Raw> Snapshot()
    {
        const int SystemProcessInformation = 5;
        const int LengthMismatch = unchecked((int)0xC0000004);
        var all = new Dictionary<int, Raw>();
        var size = 512 * 1024;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQuerySystemInformation(SystemProcessInformation, buffer, size, out var needed);
                if (status == LengthMismatch) { size = Math.Max(size * 2, needed + 64 * 1024); continue; }
                if (status != 0) return all;
                var at = buffer;
                while (true)
                {
                    var e = Marshal.PtrToStructure<ProcessEntry>(at);
                    var pid = (int)e.UniqueProcessId;
                    all[pid] = new Raw(pid, (int)e.InheritedFromUniqueProcessId, e.CreateTime,
                        (double)e.PrivatePageCount.ToUInt64(), (double)e.WorkingSetSize.ToUInt64(), e.WorkingSetPrivateSize);
                    if (e.NextEntryOffset == 0) break;
                    at += (int)e.NextEntryOffset;
                }
                return all;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return all;
    }
}

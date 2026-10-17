using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Support;

namespace Simpl.Screens;

/// <summary>
/// (0.3.25, the Mac's AltaSpare) One web view for Knewton Alta's popup made ahead, at a quiet moment once the Dashboard is
/// up (or once an Alta assignment's page or the External Tools list has said so), for an account that uses Knewton Alta
/// (<see cref="Engine.Engine.HasAlta"/>): made on Simpl's one browser, dressed as the popup's own is — its settings, Alta's
/// page told the student prefers light (<c>LightPages</c>), the look's flags and the page script put in before every page
/// — and an empty page (about:blank) loaded, so its page's process is already running when the student opens an
/// assignment. The popup takes it (<see cref="Take"/>) instead of making one then — making and dressing one was most of
/// the ~500 ms from the popup up to the first page in, on the mock — and the next is made only after that popup has
/// closed, at a quiet moment again, never while one is opening.
///
/// It only ever holds that empty page: a web view that has shown anything is never handed on, so no page, script or
/// state of one open (or one account) reaches the next — each open has a web view nobody has used. Its cookies are the
/// app's own web data's, as every Alta popup's always were. It is for the engine it was made for only (one school, one
/// sign-in), and it is let go after ten minutes unused, when the student signs out and when another school is chosen.
/// It waits, out of sight, in the main window (<see cref="Host"/>, which MainWindow gives it): WebView2 makes a web view
/// only in a window, and the popup's takes it over from there.
/// </summary>
internal static class AltaSpare
{
    /// <summary>A web view made ahead, its empty page loaded, with the two scripts it runs before every page: the look's
    /// flags (their text, to tell whether they still hold when it is taken) and the page script.</summary>
    internal sealed class View
    {
        public required WebView2 Web { get; init; }
        public required Engine.Engine For { get; init; }
        public string? FlagsId { get; set; }
        public string Flags { get; set; } = "";
        public string? HookId { get; set; }
    }

    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(10);

    /// <summary>Where a spare waits until it is taken: a part of the main window's that is never seen.</summary>
    public static Panel? Host { get; set; }

    private static View? spare;
    private static View? making;
    private static DispatcherTimer? waiting, expiry;
    private static int open, generation;

    /// <summary>Made ahead at all: always, but in the screenshot suite only when a picture asks (--alta-spare), so the
    /// rest are taken as they were.</summary>
    private static bool Allowed => !Shot.Asked || Launch.Flag("alta-spare");

    /// <summary>An Alta popup opened: none is made while it is open.</summary>
    public static void Opened() => open++;

    /// <summary>An Alta popup closed: the next one's web view made ahead, at a quiet moment.</summary>
    public static void Closed(Engine.Engine engine)
    {
        open = Math.Max(0, open - 1);
        if (engine.HasAlta) Prepare(engine);
    }

    /// <summary>The spare for an Alta popup about to open for this engine, taken out of its place — null when there is
    /// none ready (the popup makes its own).</summary>
    public static View? Take(Engine.Engine engine)
    {
        if (spare is not { } s || !ReferenceEquals(s.For, engine)) return null;
        spare = null;
        expiry?.Stop();
        Host?.Children.Remove(s.Web);
        return s;
    }

    /// <summary>Made at the next quiet moment — no call to the school in flight for <paramref name="quiet"/> seconds (half
    /// a minute at most) and no Alta popup open — for an account that uses Alta; nothing when one is up or on its way.</summary>
    public static void Prepare(Engine.Engine engine, double quiet = 1.5)
    {
        if (!Allowed || Host == null || spare != null || making != null || waiting != null) return;
        var gen = generation;
        double still = 0, waited = 0;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        waiting = t;
        t.Tick += (_, _) =>
        {
            if (gen != generation) { t.Stop(); return; }
            waited += 0.25;
            still = engine.CallsInFlight == 0 && open == 0 ? still + 0.25 : 0;
            if (still < quiet && waited < 30) return;
            t.Stop();
            waiting = null;
            if (engine.HasAlta && open == 0) _ = Make(engine, gen);
        };
        t.Start();
    }

    /// <summary>(the screenshot suite's --alta-spare) Made now, and waited for until its empty page is loaded, before the
    /// popup it is for is asked for.</summary>
    public static async Task MakeNow(Engine.Engine engine)
    {
        if (!Allowed || Host == null) return;
        waiting?.Stop();
        waiting = null;
        if (spare == null && making == null) await Make(engine, generation);
        for (var i = 0; i < 50 && making != null; i++) await Task.Delay(100);
    }

    /// <summary>Let go: signed out, another school chosen, or ten minutes unused — and one being made or waited for is
    /// not made after all.</summary>
    public static void Drop(string why)
    {
        generation++;
        waiting?.Stop();
        waiting = null;
        expiry?.Stop();
        if (spare is not { } s) return;
        spare = null;
        Dispose(s);
        Log.Write($"alta: the spare web view let go ({why})");
    }

    private static void Dispose(View v)
    {
        Host?.Children.Remove(v.Web);
        try { v.Web.Dispose(); } catch { }
    }

    private static async Task Make(Engine.Engine engine, int gen)
    {
        if (Host is not { } host || making != null) return;
        var began = Stopwatch.GetTimestamp();
        var view = new View { Web = new WebView2 { Visibility = Visibility.Hidden }, For = engine, Flags = AltaSession.FlagsAsKept() };
        making = view;
        host.Children.Add(view.Web);
        try
        {
            var made = await AltaSession.Dress(view.Web, view.Flags, () => gen != generation);
            if (made is not { } ids || view.Web.CoreWebView2 is not { } core) { making = null; Dispose(view); return; }
            view.FlagsId = ids.Flags;
            view.HookId = ids.Hook;
            // (its empty page: the page's process started for it, ready for Alta's)
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Done(object? sender, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult();
            core.NavigationCompleted += Done;
            core.Navigate("about:blank");
            await Task.WhenAny(loaded.Task, Task.Delay(5000));
            core.NavigationCompleted -= Done;
            if (!loaded.Task.IsCompleted) throw new TimeoutException("its empty page did not load");
        }
        catch (Exception e)
        {
            making = null;
            Dispose(view);
            Log.Write("alta: a spare web view: " + e.Message);
            return;
        }
        making = null;
        if (gen != generation) { Dispose(view); return; }
        spare = view;
        expiry ??= NewExpiry();
        expiry.Stop();
        expiry.Start();
        Log.Write($"alta: a spare web view made ahead ({(long)Stopwatch.GetElapsedTime(began).TotalMilliseconds} ms)");
    }

    private static DispatcherTimer NewExpiry()
    {
        var t = new DispatcherTimer { Interval = KeepFor };
        t.Tick += (_, _) => { t.Stop(); Drop("unused for ten minutes"); };
        return t;
    }
}

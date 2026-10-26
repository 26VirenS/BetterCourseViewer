using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Simpl.Support;

namespace Simpl.Kit;

/// <summary>
/// WPF-UI's control styles as one dictionary. WPF-UI keeps them in 91 dictionaries merged into one (ControlsDictionary),
/// and every resource WPF looks up for an element — the style it may have by its type, a theme colour named by a
/// DynamicResource, in Simpl's own code and in WPF-UI's templates — searched all 91, one after another, before it
/// reached the theme's colours; and a page is built from the bottom up, so each element's lookups ran again every time
/// the part it is in was added to another. Measured on a walk through the app, a fifth of the window's busy time went to
/// those searches. Copied into a single dictionary — merged in WPF-UI's order, a later one over an earlier and a
/// dictionary's own entries over what it merges, as WPF would find them — a lookup is a few hashtable reads. The theme
/// (light or dark) stays the dictionary of its own that WPF-UI swaps; the accent is written over all of it at the
/// application's own level, as before.
///
/// Copying makes every style (a control's whole template with it): 170 ms that the window used to wait for before it
/// could appear. It is now copied a few milliseconds at a time while the window has nothing else to do — the web view
/// starts out of process meanwhile — and put in WPF-UI's place once whole. When the app's pages are made on the page's
/// own answers (a first start, late: the web view had to come up), whatever is left is copied then at once
/// (<see cref="Finish"/>), so they are made with it; made on what was kept, early, they do not wait for it. Each value in
/// the copy is the one WPF found in WPF-UI's own (a style already used is that same style), so swapping it in under a
/// page changes nothing shown. --no-flatten leaves WPF-UI's dictionaries as they are.
/// </summary>
public static class FlatResources
{
    /// <summary>The longest a step copies for before the window has its turn again.</summary>
    private const double StepMs = 6;

    /// <summary>The rest of the copy, done at once and put in place (null once done, or never asked).</summary>
    private static Action? finish;

    /// <summary>The copy still under way: once whole it is put in WPF-UI's place, and the controls take their templates
    /// again from it — a list scrolled before then is back at its top (the screenshot suite's --open more waits).</summary>
    public static bool Copying => finish != null;

    /// <summary>The application's WPF-UI control styles, copied while the window is idle, then put in their place.</summary>
    public static void Apply(Application app)
    {
        if (Launch.Flag("no-flatten")) return;
        try
        {
            var merged = app.Resources.MergedDictionaries;
            var controls = merged.OfType<Wpf.Ui.Markup.ControlsDictionary>().FirstOrDefault();
            if (controls == null) return;
            var parts = new List<ResourceDictionary>();
            InOrder(controls, parts);
            var flat = new ResourceDictionary();
            var asked = Stopwatch.GetTimestamp();
            double busy = 0, atOnce = 0;
            int next = 0, count = 0, steps = 0;
            var done = false;
            // (the dictionary being copied, and its keys not copied yet)
            List<object>? keys = null;
            var at = 0;

            // (entries copied until the time is up — at least one — or all are: one at a time, not a dictionary at a time
            //  as before 0.3.77, when a large one's styles, made as they were copied, held the window 25 to 45 ms a step)
            void Copy(double budgetMs)
            {
                var t = Stopwatch.GetTimestamp();
                do
                {
                    if (keys == null) { keys = parts[next].Keys.Cast<object>().ToList(); at = 0; }
                    if (at < keys.Count)
                    {
                        var key = keys[at++];
                        flat[key] = parts[next][key];
                        count++;
                    }
                    if (at >= keys.Count) { keys = null; next++; }
                } while (next < parts.Count && Perf.Ms(t, Stopwatch.GetTimestamp()) < budgetMs);
                busy += Perf.Ms(t, Stopwatch.GetTimestamp());
                steps++;
            }
            void Swap()
            {
                done = true;
                finish = null;
                var at = merged.IndexOf(controls);
                if (at < 0) { Log.Write("resources: left as they are (WPF-UI's dictionary was moved)"); return; }
                merged[at] = flat;
                Log.Write(Perf.Fmt($"resources: {count} of WPF-UI's in one dictionary, {busy:0} ms in {steps} steps{(atOnce > 0 ? Perf.Fmt($" ({atOnce:0} ms of it at once, as the pages were made)") : "")}, done {Perf.Ms(asked, Stopwatch.GetTimestamp()):0} ms after asked"));
            }
            void Step()
            {
                if (done) return;
                try
                {
                    Copy(StepMs);
                    if (next < parts.Count) { app.Dispatcher.BeginInvoke(Step, DispatcherPriority.ContextIdle); return; }
                    Swap();
                }
                catch (Exception e) { done = true; finish = null; Log.Write("resources: left as they are: " + e.Message); }
            }
            finish = () =>
            {
                if (done) return;
                try
                {
                    var t = Stopwatch.GetTimestamp();
                    if (next < parts.Count) Copy(double.PositiveInfinity);
                    atOnce = Perf.Ms(t, Stopwatch.GetTimestamp());
                    Swap();
                }
                catch (Exception e) { done = true; Log.Write("resources: left as they are: " + e.Message); }
            };
            app.Dispatcher.BeginInvoke(Step, DispatcherPriority.ContextIdle);
        }
        catch (Exception e) { Log.Write("resources: left as they are: " + e.Message); }
    }

    /// <summary>The copy finished now, at once, and put in place, if it is not already (before pages are made that
    /// should be made with it).</summary>
    public static void Finish()
    {
        var f = finish;
        finish = null;
        f?.Invoke();
    }

    /// <summary>A dictionary and what it merges, in the order their entries are copied: what it merges first, in order,
    /// then its own over them.</summary>
    private static void InOrder(ResourceDictionary d, List<ResourceDictionary> into)
    {
        foreach (var m in d.MergedDictionaries) InOrder(m, into);
        into.Add(d);
    }
}

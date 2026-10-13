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
/// Copying makes every style (a control's whole template with it), so it is done a few milliseconds at a time while the
/// window has nothing else to do — the first of it as the web view starts, out of process — and the copy put in WPF-UI's
/// place once whole: the window appears as soon as it did before, and each value in the copy is the one WPF found in
/// WPF-UI's own (a style already used is that same style), so nothing shown changes. --no-flatten leaves WPF-UI's
/// dictionaries as they are.
/// </summary>
public static class FlatResources
{
    /// <summary>The longest a step copies for before the window has its turn again.</summary>
    private const double StepMs = 6;

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
            double busy = 0;
            int next = 0, count = 0, steps = 0;
            void Step()
            {
                var t = Stopwatch.GetTimestamp();
                try
                {
                    do
                    {
                        var d = parts[next++];
                        foreach (var key in d.Keys.Cast<object>().ToList())
                        {
                            flat[key] = d[key];
                            count++;
                        }
                    } while (next < parts.Count && Perf.Ms(t, Stopwatch.GetTimestamp()) < StepMs);
                    steps++;
                    if (next < parts.Count)
                    {
                        busy += Perf.Ms(t, Stopwatch.GetTimestamp());
                        app.Dispatcher.BeginInvoke(Step, DispatcherPriority.ContextIdle);
                        return;
                    }
                    var at = merged.IndexOf(controls);
                    if (at < 0) return;
                    merged[at] = flat;
                    busy += Perf.Ms(t, Stopwatch.GetTimestamp());
                    Log.Write(Perf.Fmt($"resources: {count} of WPF-UI's in one dictionary, {busy:0} ms in {steps} steps, done {Perf.Ms(asked, Stopwatch.GetTimestamp()):0} ms after asked"));
                }
                catch (Exception e) { Log.Write("resources: left as they are: " + e.Message); }
            }
            app.Dispatcher.BeginInvoke(Step, DispatcherPriority.ContextIdle);
        }
        catch (Exception e) { Log.Write("resources: left as they are: " + e.Message); }
    }

    /// <summary>A dictionary and what it merges, in the order their entries are copied: what it merges first, in order,
    /// then its own over them.</summary>
    private static void InOrder(ResourceDictionary d, List<ResourceDictionary> into)
    {
        foreach (var m in d.MergedDictionaries) InOrder(m, into);
        into.Add(d);
    }
}

using System.Diagnostics;
using System.Windows;
using Simpl.Support;

namespace Simpl.Kit;

/// <summary>
/// WPF-UI's control styles as one dictionary. WPF-UI keeps them in 91 dictionaries merged into one (ControlsDictionary),
/// and every resource WPF looks up for an element — the style it may have by its type, a theme colour named by a
/// DynamicResource, in Simpl's own code and in WPF-UI's templates — searched all 91, one after another, before it
/// reached the theme's colours; and a page is built from the bottom up, so each element's lookups ran again every time
/// the part it is in was added to another. Measured on a walk through the app, a fifth of the window's busy time went to
/// those searches. Copied at start into a single dictionary — merged in WPF-UI's order, a later one over an earlier and
/// a dictionary's own entries over what it merges, as WPF would find them — a lookup is a few hashtable reads. The
/// theme (light or dark) stays the dictionary of its own that WPF-UI swaps; the accent is written over all of it at the
/// application's own level, as before. --no-flatten leaves WPF-UI's dictionaries as they are.
/// </summary>
public static class FlatResources
{
    /// <summary>The application's WPF-UI control styles, made one dictionary (before any window is made).</summary>
    public static void Apply(Application app)
    {
        if (Launch.Flag("no-flatten")) return;
        try
        {
            var started = Stopwatch.GetTimestamp();
            var merged = app.Resources.MergedDictionaries;
            var count = 0;
            for (var i = 0; i < merged.Count; i++)
            {
                if (merged[i] is not Wpf.Ui.Markup.ControlsDictionary controls) continue;
                var flat = new ResourceDictionary();
                Copy(controls, flat, ref count);
                merged[i] = flat;
            }
            Log.Write(Perf.Fmt($"resources: {count} of WPF-UI's in one dictionary, in {Perf.Ms(started, Stopwatch.GetTimestamp()):0} ms"));
        }
        catch (Exception e) { Log.Write("resources: left as they are: " + e.Message); }
    }

    /// <summary>A dictionary's resources into another: what it merges first, in order, then its own over them.</summary>
    private static void Copy(ResourceDictionary from, ResourceDictionary to, ref int count)
    {
        foreach (var m in from.MergedDictionaries) Copy(m, to, ref count);
        foreach (var key in from.Keys.Cast<object>().ToList())
        {
            to[key] = from[key];
            count++;
        }
    }
}

namespace Simpl.Support;

/// <summary>
/// What the app was started with — the Mac's launch arguments, Windows-style (--name value). For the screenshot suite and
/// development: --base-url http://localhost:8800 (the mock Canvas, as -SimplBaseURL), --demo (as a student who has used
/// it), --place grades (where the window opens), --push /courses/101/assignments/1001, --open quiz:101:9011,
/// --appearance dark, --shot file.png --shot-after 12 (a picture of the window, then quit), --window-size 1280x820,
/// --place tools --tool cell --tool-state plant:cutaway (a tool opened, a biology tool in that state; --bio-seq ATGC and
/// any --bio-&lt;name&gt; too: Tools/Biology/README.md), --bio-check (the biology toolkit's self-check in the log),
/// --tools-scroll physics (the Tools page scrolled to a section), --tool motion --tool-state projectile:v0=20,angle=45
/// and --motion-values (a physics tool in that state: Tools/Physics/PhysTools.cs).
/// </summary>
public static class Launch
{
    private static readonly Dictionary<string, string> Args = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Taken = new(StringComparer.OrdinalIgnoreCase);

    public static void Parse(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;
            var name = a[2..];
            var eq = name.IndexOf('=');
            if (eq > 0) { Args[name[..eq]] = name[(eq + 1)..]; continue; }
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) { Args[name] = args[++i]; } else { Args[name] = "true"; }
        }
    }

    public static string? Get(string name) => Args.TryGetValue(name, out var v) ? v : null;

    /// <summary>The arguments as the log says them: a sign-in's (--sign-in user:pass, the screenshot suite's) left out —
    /// a password is never written down, the mock's either.</summary>
    public static string Said(string[] args) => string.Join(' ', args.Select((a, i) =>
        i > 0 && string.Equals(args[i - 1], "--sign-in", StringComparison.OrdinalIgnoreCase) && !a.StartsWith("--") ? "…"
        : a.StartsWith("--sign-in=", StringComparison.OrdinalIgnoreCase) ? "--sign-in=…"
        : a));

    public static bool Flag(string name) => Args.TryGetValue(name, out var v) && v is "true" or "yes" or "YES" or "1";

    public static double Number(string name, double fallback) =>
        double.TryParse(Get(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;

    /// <summary>The address given at launch (the mock Canvas), when it is one.</summary>
    public static Uri? DevBaseUrl => Uri.TryCreate(Get("base-url") ?? "", UriKind.Absolute, out var u) ? u : null;

    /// <summary>A development run: the mock or a demo, or a picture to take — never updates, never a real school's sign-in kept.</summary>
    public static bool IsDevelopment => DevBaseUrl != null || Flag("demo") || Get("shot") != null;

    /// <summary>--open quiz:101:9011 and the like: what follows the prefix, once.</summary>
    public static string? Take(string prefix, string key = "open")
    {
        if (Taken.Contains(key)) return null;
        var v = Get(key);
        if (v == null || !v.StartsWith(prefix, StringComparison.Ordinal)) return null;
        Taken.Add(key);
        return v[prefix.Length..];
    }
}

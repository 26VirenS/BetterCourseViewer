using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// The physics tools' place among the tools (tools), as the chemistry tools' is (Tools/Chemistry/ChemTools.cs): a
// "Physics" section of their own on the Tools page, after Chemistry and before Biology, with their keys, names, lines,
// symbols and colours (the Mac's, both apps the same); each tool's file registers its own views (Tools/Physics/MotionTool.cs
// and the rest), its maths kept apart from WPF in Tools/Physics/Model so the unit tests check it on any machine; and
// what a launch asks of each (--tool motion --tool-state projectile:v0=20,angle=45, for the screenshot suite).

/// <summary>The physics tools as the app knows them.</summary>
public static class PhysTools
{
    /// <summary>
    /// The section's tools, in the Tools page's order, as the Mac has them: their keys, names in the chemistry tools'
    /// Title Case, lines and colours — a family of blues. Every symbol's code is at most U+FFFF (README, Icons), and
    /// none is worn by a chemistry or biology tool.
    /// </summary>
    public static readonly ToolsScreen.Kind[] Kinds =
    {
        new("motion", "Kinematics & Projectiles", "1D motion and projectile paths.", SymbolRegular.Rocket24, "#0a84ff"),
    };

    public static ToolsScreen.Kind? Kind(string key) => Kinds.FirstOrDefault(k => k.Key == key);

    /// <summary>One of the Physics section's tools.</summary>
    public static bool IsPhysics(string key) => Kinds.Any(k => k.Key == key);

    /// <summary>
    /// What a launch asked of a physics tool, once, and only of the tool the launch opened (--tool &lt;key&gt;):
    /// <c>--tool-state</c> as it was given — a tab and its values for Kinematics &amp; Projectiles
    /// ("projectile:v0=20,angle=45,h=0").
    /// </summary>
    public static string? LaunchState(string key)
    {
        if (Launch.Get("tool") != key) return null;
        var state = Launch.Take("", "tool-state");
        if (state is { Length: > 0 }) Log.Write($"{key}: launched with state \"{state}\"");
        return state is { Length: > 0 } ? state : null;
    }
}

/// <summary>
/// What the physics tools keep from one opening to the next, on this PC (Prefs <c>SimplPhys.&lt;tool&gt;.&lt;field&gt;</c>,
/// the Mac's PhysPrefs keys): Kinematics &amp; Projectiles' tab, numbers and units (<c>motion.tab</c>, <c>motion.oneD</c>,
/// <c>motion.projectile</c>, <c>motion.quick</c>, <c>motion.imperial</c>). Written as they change; a picture's run has a
/// profile of its own that starts empty, so each picture shows its tool as its launch asks.
/// </summary>
internal static class PhysPrefs
{
    public static string Key(string tool, string field) => $"SimplPhys.{tool}.{field}";
    public static string? String(string tool, string field) => Prefs.String(Key(tool, field));
    public static bool Bool(string tool, string field, bool fallback) => Prefs.Bool(Key(tool, field), fallback);
    public static void Set(string tool, string field, object? value) => Prefs.Set(Key(tool, field), value);
}

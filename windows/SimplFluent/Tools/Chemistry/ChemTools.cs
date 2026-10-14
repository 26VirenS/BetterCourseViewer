using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// The chemistry tools' place among the tools (0.3.2), as the biology toolkit's is (Tools/Biology/BioTools.cs): the four
// that were one tool until then — the Periodic Table, its Lewis structures and Molecule builder pages, and its element
// page's atom in 3D — with their names, lines, symbols and colours (the Tools page's "Chemistry" section shows them,
// ToolsScreen.cs); the two builders' registration (the Periodic Table registers in Tools/Elements/PeriodicTable.cs, Atoms
// & Orbitals in Tools/Elements/AtomsTool.cs); and what a launch asks of each (--tool lewis --tool-state NO3-, for the
// screenshot suite). One tool hands another what it shows with ToolsCenter.HandTo: an element's See it in 3D, Atoms &
// Orbitals' Open in Periodic Table, Lewis Structures' Open in Molecule Builder.

/// <summary>The chemistry tools as the app knows them.</summary>
public static class ChemTools
{
    /// <summary>
    /// The four, in the Tools page's order: names in the biology tools' Title Case, a line each short enough for one line
    /// on its tile, and a palette of their own — red, plum, sky and green, each in a gap the biology tools' colours leave,
    /// so the two sections never read as one. The Periodic Table keeps its key, so a pin of it stays the Periodic Table.
    /// Every symbol's code is at most U+FFFF (README, Icons).
    /// </summary>
    public static readonly ToolsScreen.Kind[] Kinds =
    {
        new("ptable", "Periodic Table", "Every element, its trends and its facts.", SymbolRegular.Table24, "#e5484d"),
        new("lewis", "Lewis Structures", "Bonds, lone pairs, charges and resonance.", SymbolRegular.GridDots24, "#a144af"),
        new("molecule", "Molecule Builder", "Build a molecule and turn it in 3D.", SymbolRegular.Molecule24, "#0d8fcf"),
        new("atom", "Atoms & Orbitals", "An element’s shells, orbitals and electron cloud.", SymbolRegular.Target24, "#3d9a50"),
    };

    /// <summary>One of the four (they have a section of their own on the Tools page).</summary>
    public static bool IsChemistry(string key) => Kinds.Any(k => k.Key == key);

    public static ToolsScreen.Kind? Kind(string key) => Kinds.FirstOrDefault(k => k.Key == key);

    /// <summary>
    /// What a launch asked of a chemistry tool, once, and only of the tool the launch opened (--tool &lt;key&gt;):
    /// <c>--tool-state</c> as it was given — a molecule for the builders ("NO3-", "Ethanol"), an element, its view and its
    /// subshell for Atoms &amp; Orbitals ("Fe:cloud:3d").
    /// </summary>
    public static string? LaunchState(string key)
    {
        if (Launch.Get("tool") != key) return null;
        var state = Launch.Take("", "tool-state");
        if (state is { Length: > 0 }) Log.Write($"{key}: launched with state \"{state}\"");
        return state is { Length: > 0 } ? state : null;
    }
}

#pragma warning disable CA2255
/// <summary>Lewis Structures and the Molecule Builder registered: each its builder (Tools/Chemistry) — starting from what
/// the launch asked, else the molecule it had — and, in a pin's flyout, its tile, its line and Open (the work wants the
/// whole popup).</summary>
internal static class ChemToolsRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register()
    {
        ToolRegistry.Register("lewis", c => Chemistry.Lewis(c, ChemTools.LaunchState("lewis")), c => new BioQuickOpen(c));
        ToolRegistry.Register("molecule", c => Chemistry.Molecule(c, ChemTools.LaunchState("molecule")), c => new BioQuickOpen(c));
    }
}
#pragma warning restore CA2255

using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// The chemistry tools' place among the tools (0.3.2), as the biology toolkit's is (Tools/Biology/BioTools.cs): the four
// that were one tool until then — the Periodic Table, its Lewis structures and Molecule builder pages, and its element
// page's atom in 3D — with their keys, names, lines, symbols and colours (the Mac's) (the Tools page's "Chemistry" section shows them,
// ToolsScreen.cs); the two builders' registration (the Periodic Table registers in Tools/Elements/PeriodicTable.cs, Atoms
// & Orbitals in Tools/Elements/AtomsTool.cs); and what a launch asks of each (--tool lewis --tool-state NO3-, for the
// screenshot suite). One tool hands another what it shows with ToolsCenter.HandTo: an element's See it in 3D, Atoms &
// Orbitals' Open in Periodic Table, Lewis Structures' Open in Molecule Builder.

/// <summary>The chemistry tools as the app knows them.</summary>
public static class ChemTools
{
    /// <summary>
    /// The four, in the Tools page's order, as the Mac has them (ChemTools.swift: both apps the same): their keys, names
    /// in the biology tools' Title Case, lines and colours — red, cyan, green and violet. The Periodic Table keeps its key,
    /// so a pin of it stays the Periodic Table; its symbol a frame of element tiles (a table's grid read as the
    /// Biomolecule Builder's cube a few rows below). Every symbol's code is at most U+FFFF (README, Icons).
    /// </summary>
    public static readonly ToolsScreen.Kind[] Kinds =
    {
        new("ptable", "Periodic Table", "Every element, its trends and its facts.", SymbolRegular.SlideGrid24, "#e5484d"),
        new("lewis", "Lewis Structures", "Bonds, lone pairs, formal charges and resonance.", SymbolRegular.GridDots24, "#0ea5c6"),
        new("molecule", "Molecule Builder", "Build molecules in 3D, shaped by VSEPR.", SymbolRegular.Molecule24, "#3faa4b"),
        new("atoms", "Atoms & Orbitals", "Any element’s shells, orbitals and cloud in 3D.", SymbolRegular.Target24, "#b452e0"),
    };

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

/// <summary>
/// What the chemistry tools keep from one opening to the next, on this PC (Prefs <c>SimplChem.&lt;tool&gt;.&lt;field&gt;</c>,
/// the Mac's ChemPrefs keys): the Periodic Table's colouring and the last element opened (<c>ptable.colour</c>,
/// <c>ptable.element</c>), the Lewis maker's molecule (<c>lewis.draft</c>), the Molecule Builder's and how it is shown
/// (<c>molecule.saved</c>, <c>.style</c>, <c>.lonePairs</c>, <c>.labels</c>), and Atoms &amp; Orbitals' element and view
/// (<c>atoms.element</c>, <c>atoms.view</c>). Written as they change (Prefs writes the file a moment after the last
/// change); a picture's run has a profile of its own that starts empty, so each picture shows its tool as its launch
/// asks.
/// </summary>
internal static class ChemPrefs
{
    public static string Key(string tool, string field) => $"SimplChem.{tool}.{field}";
    public static string? String(string tool, string field) => Prefs.String(Key(tool, field));
    public static bool Bool(string tool, string field, bool fallback) => Prefs.Bool(Key(tool, field), fallback);
    public static void Set(string tool, string field, object? value) => Prefs.Set(Key(tool, field), value);
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

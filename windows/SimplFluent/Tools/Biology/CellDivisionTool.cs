using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// Cell Division (spec §4.6): mitosis and meiosis stage by stage, with the counts at every step, and the cell cycle.
// Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header. It
// registers itself below (BioTools.Register: its key, its view, its pages); its pages show in the tool's header
// (BioNav.Page("divide") is the one shown), and what the launch asked of it reaches it as `state` (--tool divide
// --tool-state <state>; Tools/Biology/README.md). What it works out and keeps belongs in CellDivisionModel
// (Model/CellDivisionModel.cs), so it outlives this view.

#pragma warning disable CA2255
internal static class CellDivisionToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("divide", (c, state) => new CellDivisionTool(c, state), CellDivisionTool.Pages);
}
#pragma warning restore CA2255

/// <summary>Cell Division (a placeholder until it is built).</summary>
internal sealed class CellDivisionTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("mitosis", "Mitosis", SymbolRegular.ShapeUnion24, "Mitosis, stage by stage"),
        new("meiosis", "Meiosis", SymbolRegular.Shapes24, "Meiosis I and II, stage by stage"),
        new("compare", "Compare", SymbolRegular.SplitVertical24, "Mitosis and meiosis side by side"),
        new("cycle", "Cell cycle", SymbolRegular.ArrowSync24, "The cell cycle and its checkpoints"),
    };

    public CellDivisionTool(ToolContext c, BioLaunchState? state)
    {
        Children.Add(new BioToolPlaceholder(c, state));
    }
}

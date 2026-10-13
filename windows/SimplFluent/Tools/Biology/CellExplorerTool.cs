using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// Cell Explorer (spec §4.1): animal, plant and bacterial cells in 3D, a patch of membrane up close, and the three
// compared. Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header.
// It registers itself below (BioTools.Register: its key, its view, its pages); its pages show in the tool's header
// (BioNav.Page("cell") is the one shown), and what the launch asked of it reaches it as `state` (--tool cell
// --tool-state <state>; Tools/Biology/README.md). What it works out and keeps belongs in CellExplorerModel
// (Model/CellExplorerModel.cs), so it outlives this view.

#pragma warning disable CA2255
internal static class CellExplorerToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("cell", (c, state) => new CellExplorerTool(c, state), CellExplorerTool.Pages);
}
#pragma warning restore CA2255

/// <summary>Cell Explorer (a placeholder until it is built).</summary>
internal sealed class CellExplorerTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("animal", "Animal", SymbolRegular.AnimalCat24, "An animal cell"),
        new("plant", "Plant", SymbolRegular.LeafOne24, "A plant cell"),
        new("bacterium", "Bacterium", SymbolRegular.Pill24, "A bacterial cell (E. coli)"),
        new("membrane", "Membrane", SymbolRegular.Layer24, "The plasma membrane up close"),
        new("compare", "Compare", SymbolRegular.SplitVertical24, "Animal, plant and bacterial cells side by side"),
    };

    public CellExplorerTool(ToolContext c, BioLaunchState? state)
    {
        Children.Add(new BioToolPlaceholder(c, state));
    }
}

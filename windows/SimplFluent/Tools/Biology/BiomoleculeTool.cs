using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// Biomolecule Builder (spec §4.4): amino acids and proteins, nucleic acids, sugars and lipids, built and seen in 3D.
// Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header. It
// registers itself below (BioTools.Register: its key, its view, its pages); its pages show in the tool's header
// (BioNav.Page("biomol") is the one shown), and what the launch asked of it reaches it as `state` (--tool biomol
// --tool-state <state>; Tools/Biology/README.md). What it works out and keeps belongs in BiomoleculeBuilderModel
// (Model/BiomoleculeBuilderModel.cs), so it outlives this view.

#pragma warning disable CA2255
internal static class BiomoleculeToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("biomol", (c, state) => new BiomoleculeTool(c, state), BiomoleculeTool.Pages);
}
#pragma warning restore CA2255

/// <summary>Biomolecule Builder (a placeholder until it is built).</summary>
internal sealed class BiomoleculeTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("amino", "Amino acids", SymbolRegular.TextCaseTitle24, "The 20 amino acids"),
        new("proteins", "Proteins", SymbolRegular.Diagram24, "Build a peptide; the four levels of structure"),
        new("nucleic", "Nucleic acids", SymbolRegular.Link24, "Nucleotides joined into a strand"),
        new("carbs", "Carbohydrates", SymbolRegular.FoodGrains24, "Sugars and the glycosidic bond"),
        new("lipids", "Lipids", SymbolRegular.Drop24, "Fatty acids, fats, phospholipids and cholesterol"),
    };

    public BiomoleculeTool(ToolContext c, BioLaunchState? state)
    {
        Children.Add(new BioToolPlaceholder(c, state));
    }
}

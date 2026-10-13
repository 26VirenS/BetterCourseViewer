using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// Bio Lab Calculator (spec §4.8): solutions, microscopy, cells, statistics, populations and molecules, worked as on
// paper. Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header. It
// registers itself below (BioTools.Register: its key, its view, its pages, its pin's quick version); its pages show in
// the tool's header (BioNav.Page("biolab") is the one shown), and what the launch asked of it reaches it as `state`
// (--tool biolab --tool-state <state>; Tools/Biology/README.md). What it works out and keeps belongs in BioLabModel
// (Model/BioLabModel.cs), so it outlives this view.

#pragma warning disable CA2255
internal static class BioLabToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("biolab", (c, state) => new BioLabTool(c, state), BioLabTool.Pages, (c, state) => new BioLabCompact(c, state));
}
#pragma warning restore CA2255

/// <summary>Bio Lab Calculator (a placeholder until it is built).</summary>
internal sealed class BioLabTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("solutions", "Solutions", SymbolRegular.Drop24, "Molarity, dilutions and percent solutions"),
        new("microscopy", "Microscopy", SymbolRegular.ZoomIn24, "Magnification, field of view and size"),
        new("cells", "Cells", SymbolRegular.CircleSmall24, "Surface area to volume, water potential, cell counts"),
        new("stats", "Statistics", SymbolRegular.DataBarVertical24, "Mean, standard deviation, error bars, chi-square"),
        new("ecology", "Populations", SymbolRegular.LeafTwo24, "Growth, rate and diversity"),
        new("molecules", "Molecules", SymbolRegular.Molecule24, "Absorbance, PCR, centrifuges, pH and Q₁₀"),
    };

    public BioLabTool(ToolContext c, BioLaunchState? state)
    {
        Children.Add(new BioToolPlaceholder(c, state));
    }
}

/// <summary>Bio Lab Calculator in its pin's flyout (spec §3.2 item 3: C₁V₁ and molarity): until it is built, its tile, its line and
/// Open. c.OpenFull(info, null) opens the whole tool with what was begun here (["sequence"] = … and so on).</summary>
internal sealed class BioLabCompact : ContentControl
{
    public BioLabCompact(ToolContext c, BioLaunchState? state)
    {
        Focusable = false;
        Content = new BioQuickOpen(c);
    }
}

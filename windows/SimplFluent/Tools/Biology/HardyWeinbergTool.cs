using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// Hardy–Weinberg (spec §4.7): allele and genotype frequencies worked out, tested and simulated. Until its builder
// replaces this file, a placeholder: its name, its line and the page chosen in its header. It registers itself below
// (BioTools.Register: its key, its view, its pages, its pin's quick version); its pages show in the tool's header
// (BioNav.Page("popgen") is the one shown), and what the launch asked of it reaches it as `state` (--tool popgen
// --tool-state <state>; Tools/Biology/README.md). What it works out and keeps belongs in HardyWeinbergModel
// (Model/HardyWeinbergModel.cs), so it outlives this view.

#pragma warning disable CA2255
internal static class HardyWeinbergToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("popgen", (c, state) => new HardyWeinbergTool(c, state), HardyWeinbergTool.Pages, (c, state) => new HardyWeinbergCompact(c, state));
}
#pragma warning restore CA2255

/// <summary>Hardy–Weinberg (a placeholder until it is built).</summary>
internal sealed class HardyWeinbergTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("calculate", "Calculate", SymbolRegular.Calculator24, "Everything from one value"),
        new("test", "Test", SymbolRegular.CheckmarkCircle24, "Is a population in equilibrium?"),
        new("simulate", "Simulate", SymbolRegular.DataLine24, "Selection, drift, mutation and migration over generations"),
    };

    public HardyWeinbergTool(ToolContext c, BioLaunchState? state)
    {
        Children.Add(new BioToolPlaceholder(c, state));
    }
}

/// <summary>Hardy–Weinberg in its pin's flyout (spec §3.2 item 3: Hardy–Weinberg from one value): until it is built, its tile, its line and
/// Open. c.OpenFull(info, null) opens the whole tool with what was begun here (["sequence"] = … and so on).</summary>
internal sealed class HardyWeinbergCompact : ContentControl
{
    public HardyWeinbergCompact(ToolContext c, BioLaunchState? state)
    {
        Focusable = false;
        Content = new BioQuickOpen(c);
    }
}

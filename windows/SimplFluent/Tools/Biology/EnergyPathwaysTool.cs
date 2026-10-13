using System.Windows.Controls;

namespace Simpl.Tools;

// Photosynthesis & Respiration (spec §4.10): where each stage happens and what goes in and out. Until its builder
// replaces this file, a placeholder: its name, its line and the page chosen in its header. It registers itself below
// (BioTools.Register: its key, its view, its pages); its pages show in the tool's header (BioNav.Page("energy") is the
// one shown), and what the launch asked of it reaches it as `state` (--tool energy --tool-state <state>;
// Tools/Biology/README.md). What it works out and keeps belongs in EnergyPathwaysModel (Model/EnergyPathwaysModel.cs),
// so it outlives this view.

#pragma warning disable CA2255
internal static class EnergyPathwaysToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("energy", (c, state) => new EnergyPathwaysTool(c, state), null);
}
#pragma warning restore CA2255

/// <summary>Photosynthesis & Respiration (a placeholder until it is built).</summary>
internal sealed class EnergyPathwaysTool : Grid
{
    public EnergyPathwaysTool(ToolContext c, BioLaunchState? state)
    {
        Children.Add(new BioToolPlaceholder(c, state));
    }
}

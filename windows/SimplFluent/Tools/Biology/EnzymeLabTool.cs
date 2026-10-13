using System.Windows.Controls;

namespace Simpl.Tools;

// Enzyme Lab (spec §4.9): rate against substrate, temperature and pH, with inhibitors. Until its builder replaces this
// file, a placeholder: its name, its line and the page chosen in its header. It registers itself below
// (BioTools.Register: its key, its view, its pages); its pages show in the tool's header (BioNav.Page("enzyme") is the
// one shown), and what the launch asked of it reaches it as `state` (--tool enzyme --tool-state <state>;
// Tools/Biology/README.md). What it works out and keeps belongs in EnzymeLabModel (Model/EnzymeLabModel.cs), so it
// outlives this view.

#pragma warning disable CA2255
internal static class EnzymeLabToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("enzyme", (c, state) => new EnzymeLabTool(c, state), null);
}
#pragma warning restore CA2255

/// <summary>Enzyme Lab (a placeholder until it is built).</summary>
internal sealed class EnzymeLabTool : Grid
{
    public EnzymeLabTool(ToolContext c, BioLaunchState? state)
    {
        Children.Add(new BioToolPlaceholder(c, state));
    }
}

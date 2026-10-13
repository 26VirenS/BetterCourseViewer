using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// Genetics Crosses (spec §4.5): Punnett squares with every gamete, probability by the product rule, and chi-square.
// Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header. It
// registers itself below (BioTools.Register: its key, its view, its pages, its pin's quick version); its pages show in
// the tool's header (BioNav.Page("cross") is the one shown), and what the launch asked of it reaches it as `state`
// (--tool cross --tool-state <state>; Tools/Biology/README.md). What it works out and keeps belongs in CrossesModel
// (Model/CrossesModel.cs), so it outlives this view.

#pragma warning disable CA2255
internal static class CrossesToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("cross", (c, state) => new CrossesTool(c, state), CrossesTool.Pages, (c, state) => new CrossesCompact(c, state));
}
#pragma warning restore CA2255

/// <summary>Genetics Crosses (a placeholder until it is built).</summary>
internal sealed class CrossesTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("cross", "Cross", SymbolRegular.Grid24, "A cross and its Punnett square"),
        new("probability", "Probability", SymbolRegular.Calculator24, "The product and sum rules"),
        new("chi", "Chi-square", SymbolRegular.DataBarVertical24, "Observed against expected"),
        new("pedigree", "Pedigree", SymbolRegular.PeopleTeam24, "Which inheritance a pedigree fits"),
    };

    public CrossesTool(ToolContext c, BioLaunchState? state)
    {
        Children.Add(new BioToolPlaceholder(c, state));
    }
}

/// <summary>Genetics Crosses in its pin's flyout (spec §3.2 item 3: a monohybrid Punnett square): until it is built, its tile, its line and
/// Open. c.OpenFull(info, null) opens the whole tool with what was begun here (["sequence"] = … and so on).</summary>
internal sealed class CrossesCompact : ContentControl
{
    public CrossesCompact(ToolContext c, BioLaunchState? state)
    {
        Focusable = false;
        Content = new BioQuickOpen(c);
    }
}

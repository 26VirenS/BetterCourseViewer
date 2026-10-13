using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// DNA & RNA (spec §4.2): the double helix, nucleotides, a sequence's facts, replication and RNA. Until its builder
// replaces this file, a placeholder: its name, its line and the page chosen in its header. It registers itself below
// (BioTools.Register: its key, its view, its pages); its pages show in the tool's header (BioNav.Page("dna") is the one
// shown), and what the launch asked of it reaches it as `state` (--tool dna --tool-state <state>;
// Tools/Biology/README.md). What it works out and keeps belongs in DNAToolModel (Model/DNAToolModel.cs), so it outlives
// this view.

#pragma warning disable CA2255
internal static class DNAToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("dna", (c, state) => new DNATool(c, state), DNATool.Pages);
}
#pragma warning restore CA2255

/// <summary>DNA & RNA (a placeholder until it is built).</summary>
internal sealed class DNATool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("helix", "Double helix", SymbolRegular.Ribbon24, "B-DNA in 3D"),
        new("nucleotides", "Nucleotides", SymbolRegular.Molecule24, "The eight nucleotides and ATP"),
        new("sequence", "Sequence", SymbolRegular.TextT24, "A strand, its complement and its facts"),
        new("replication", "Replication", SymbolRegular.BranchFork24, "A replication fork, step by step"),
        new("rna", "RNA", SymbolRegular.Pulse24, "mRNA, tRNA and ribosomes"),
    };

    public DNATool(ToolContext c, BioLaunchState? state)
    {
        Children.Add(new BioToolPlaceholder(c, state));
    }
}

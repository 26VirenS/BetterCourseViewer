using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// Gene Expression (spec §4.3): transcription and translation step by step, the codon table, reading frames and
// mutations. Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header.
// It registers itself below (BioTools.Register: its key, its view, its pages, its pin's quick version); its pages show
// in the tool's header (BioNav.Page("gene") is the one shown), and what the launch asked of it reaches it as `state`
// (--tool gene --tool-state <state>; Tools/Biology/README.md). What it works out and keeps belongs in
// GeneExpressionModel (Model/GeneExpressionModel.cs), so it outlives this view.

#pragma warning disable CA2255
internal static class GeneExpressionToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("gene", (c, state) => new GeneExpressionTool(c, state), GeneExpressionTool.Pages, (c, state) => new GeneExpressionCompact(c, state));
}
#pragma warning restore CA2255

/// <summary>Gene Expression (a placeholder until it is built).</summary>
internal sealed class GeneExpressionTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("express", "Transcribe & translate", SymbolRegular.PlayCircle24, "A gene made into a protein, step by step"),
        new("codons", "Codon table", SymbolRegular.Table24, "The genetic code"),
        new("frames", "Reading frames", SymbolRegular.TextAlignLeft24, "Six frames and the open reading frames"),
        new("mutations", "Mutations", SymbolRegular.ArrowSwap24, "What a change in the DNA does to the protein"),
    };

    public GeneExpressionTool(ToolContext c, BioLaunchState? state)
    {
        Children.Add(new BioToolPlaceholder(c, state));
    }
}

/// <summary>Gene Expression in its pin's flyout (spec §3.2 item 3: the codon lookup): until it is built, its tile, its line and
/// Open. c.OpenFull(info, null) opens the whole tool with what was begun here (["sequence"] = … and so on).</summary>
internal sealed class GeneExpressionCompact : ContentControl
{
    public GeneExpressionCompact(ToolContext c, BioLaunchState? state)
    {
        Focusable = false;
        Content = new BioQuickOpen(c);
    }
}

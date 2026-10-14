using Simpl.Screens;
using Simpl.Shell;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Periodic Table split into the Tools page's Chemistry section (0.3.2, Tools/Chemistry/ChemTools.cs): its four tools
/// in their order, the Periodic Table's key kept (so a pin of it stays the Periodic Table), its place out of Math &amp;
/// science, a palette of their own beside the biology tools', the search box's words for each, and the view words a launch
/// gives Atoms &amp; Orbitals.
/// </summary>
public class ChemistryToolsTests
{
    public ChemistryToolsTests() => _ = Profile.Folder;

    [Fact]
    public void TheChemistrySectionHasItsFourInOrder()
    {
        Assert.Equal(new[] { "ptable", "lewis", "molecule", "atom" }, ChemTools.Kinds.Select(k => k.Key));
        Assert.Equal(new[] { "Periodic Table", "Lewis Structures", "Molecule Builder", "Atoms & Orbitals" }, ChemTools.Kinds.Select(k => k.Name));
        Assert.All(ChemTools.Kinds, k => Assert.False(string.IsNullOrWhiteSpace(k.Note)));
    }

    [Fact]
    public void ThePeriodicTableLivesUnderChemistryAndKeepsItsKey()
    {
        // (not in Math & science any more; every tool once on the page, the Periodic Table by the key a pin keeps)
        Assert.DoesNotContain(ToolsScreen.Groups, g => g.Keys.Contains("ptable"));
        Assert.Equal(new[] { "calc", "graph", "units" }, ToolsScreen.Groups.First(g => g.Title == "Math & science").Keys);
        Assert.Equal(ToolsScreen.Kinds.Length, ToolsScreen.Kinds.Select(k => k.Key).Distinct().Count());
        foreach (var k in ChemTools.Kinds) Assert.Same(k, ToolsCenter.KindOf(k.Key));
        Assert.True(ChemTools.IsChemistry("ptable"));
        Assert.False(BioTools.IsBiology("ptable"));
    }

    [Fact]
    public void ChemistryHasAPaletteOfItsOwn()
    {
        var biology = BioTools.Kinds.Select(k => k.Hex.ToLowerInvariant()).ToHashSet();
        var chemistry = ChemTools.Kinds.Select(k => k.Hex.ToLowerInvariant()).ToList();
        Assert.Equal(4, chemistry.Distinct().Count());
        Assert.DoesNotContain(chemistry, biology.Contains);
    }

    [Theory]
    [InlineData("periodic", "ptable")]
    [InlineData("lewis", "lewis")]
    [InlineData("vsepr", "molecule")]
    [InlineData("orbitals", "atom")]
    [InlineData("electron cloud", "atom")]
    public void TheSearchBoxOpensEachByItsWords(string typed, string key)
    {
        var command = PaletteCommand.Named(PaletteCommand.ChemistryTools().ToList(), typed);
        Assert.NotNull(command);
        Assert.Equal(PaletteDo.Tool, command!.Action.Do);
        Assert.Equal(key, command.Action.Text);
    }

    [Theory]
    [InlineData("atom", "Atom")]
    [InlineData("Orbitals", "Orbitals")]
    [InlineData(" cloud ", "Cloud")]
    public void AtomsAndOrbitalsViewsByTheirWords(string word, string view) => Assert.Equal(view, AtomState.Parse(word)?.ToString());

    [Fact]
    public void AnUnknownViewWordIsNone() => Assert.Null(AtomState.Parse("table"));
}

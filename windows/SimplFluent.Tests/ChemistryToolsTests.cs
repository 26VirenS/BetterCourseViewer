using Simpl.Screens;
using Simpl.Shell;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Periodic Table split into the Tools page's Chemistry section (0.3.2, Tools/Chemistry/ChemTools.cs): its four tools
/// in their order and the calculators after them (tools), the Periodic Table's key kept (so a pin of it stays the Periodic
/// Table), its place out of Math &amp; science, a palette of their own beside the biology tools', the search box's words
/// for each, and the view words a launch gives Atoms &amp; Orbitals.
/// </summary>
public class ChemistryToolsTests
{
    public ChemistryToolsTests() => _ = Profile.Folder;

    /// <summary>The section's tools in their order: the Periodic Table's four, then the calculators as they shipped.</summary>
    private static readonly (string Key, string Name, string Short, string Hex)[] Expected =
    {
        ("ptable", "Periodic Table", "Elements", "#e5484d"),
        ("lewis", "Lewis Structures", "Lewis", "#0ea5c6"),
        ("molecule", "Molecule Builder", "Molecules", "#3faa4b"),
        ("atoms", "Atoms & Orbitals", "Atoms", "#b452e0"),
        ("balance", "Equation Balancer", "Balance", "#ff6b35"),
        ("moles", "Molar Mass & Stoichiometry", "Moles", "#2fb37a"),
    };

    [Fact]
    public void TheChemistrySectionHasItsToolsInOrder()
    {
        Assert.Equal(Expected.Select(e => e.Key), ChemTools.Kinds.Select(k => k.Key));
        Assert.Equal(Expected.Select(e => e.Name), ChemTools.Kinds.Select(k => k.Name));
        Assert.All(ChemTools.Kinds, k => Assert.False(string.IsNullOrWhiteSpace(k.Note)));
        // (a note fits a tile's one line; the spec's help line is at most 32 characters for the calculators)
        Assert.All(ChemTools.Kinds.Skip(4), k => Assert.True(k.Note.Length <= 32, k.Note));
        // (every symbol's code is at most U+FFFF, or WPF-UI 4.3 draws a stray character: README, Icons)
        Assert.All(ChemTools.Kinds, k => Assert.True((int)k.Symbol <= 0xFFFF, k.Key));
    }

    [Fact]
    public void ThePeriodicTableLivesUnderChemistryAndKeepsItsKey()
    {
        // (not in Math & science any more — the tools program's Statistics and Matrix Calculator came at the end of the
        //  group instead; every tool once on the page, the Periodic Table by the key a pin keeps)
        Assert.DoesNotContain(ToolsScreen.Groups, g => g.Keys.Contains("ptable"));
        Assert.Equal(new[] { "calc", "graph", "units", "stats", "matrix" }, ToolsScreen.Groups.First(g => g.Title == "Math & science").Keys);
        Assert.Equal(ToolsScreen.Kinds.Length, ToolsScreen.Kinds.Select(k => k.Key).Distinct().Count());
        foreach (var k in ChemTools.Kinds) Assert.Same(k, ToolsCenter.KindOf(k.Key));
        Assert.NotNull(ChemTools.Kind("ptable"));
        Assert.False(BioTools.IsBiology("ptable"));
    }

    [Fact]
    public void ChemistryHasAPaletteOfItsOwn()
    {
        var biology = BioTools.Kinds.Select(k => k.Hex.ToLowerInvariant()).ToHashSet();
        var chemistry = ChemTools.Kinds.Select(k => k.Hex.ToLowerInvariant()).ToList();
        Assert.Equal(Expected.Length, chemistry.Distinct().Count());
        Assert.DoesNotContain(chemistry, biology.Contains);
        // (no chemistry tool wears a biology tool's symbol: the Periodic Table's grid read as the Biomolecule Builder's
        //  cube a few rows below)
        Assert.DoesNotContain(ChemTools.Kinds, c => BioTools.Kinds.Any(b => b.Symbol == c.Symbol));
    }

    [Fact]
    public void BothAppsTheSame()
    {
        // (the Mac's ChemTools.swift: its keys, lines, colours and short names — the calculators' from the tools' spec)
        Assert.Equal(Expected.Select(e => e.Hex), ChemTools.Kinds.Select(k => k.Hex.ToLowerInvariant()));
        Assert.Equal("Bonds, lone pairs, formal charges and resonance.", ChemTools.Kind("lewis")!.Note);
        Assert.Equal("Build molecules in 3D, shaped by VSEPR.", ChemTools.Kind("molecule")!.Note);
        Assert.Equal("Balance any chemical equation.", ChemTools.Kind("balance")!.Note);
        Assert.Equal("Molar mass, moles and yields.", ChemTools.Kind("moles")!.Note);
        Assert.Equal(Expected.Select(e => e.Short), ChemTools.Kinds.Select(k => ToolsCenter.ShortName(k.Key)));
    }

    [Theory]
    [InlineData("periodic", "ptable")]
    [InlineData("lewis", "lewis")]
    [InlineData("vsepr", "molecule")]
    [InlineData("orbitals", "atoms")]
    [InlineData("electron cloud", "atoms")]
    [InlineData("resonance", "lewis")]
    [InlineData("bohr", "atoms")]
    [InlineData("balance", "balance")]
    [InlineData("equation", "balance")]
    [InlineData("coefficients", "balance")]
    [InlineData("moles", "moles")]
    [InlineData("molar mass", "moles")]
    [InlineData("limiting reagent", "moles")]
    [InlineData("empirical", "moles")]
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

    [Fact]
    public void NoChemistryWordClashesWithAnotherCommand()
    {
        // (a tool's name or alias that another command answers to would open the wrong thing: Named takes the first)
        var mine = PaletteCommand.ChemistryTools().SelectMany(c => c.Names).ToList();
        Assert.Equal(mine.Count, mine.Distinct().Count());
        foreach (var word in new[] { "light", "dark", "system", "theme", "new", "update", "next", "test", "find", "search", "show", "page", "file", "read", "note", "add", "task", "help", "tour", "guide", "web", "site", "due", "week", "tool", "timer", "calculator" })
            Assert.DoesNotContain(word, mine);
    }

    [Fact]
    public void ALaunchStateNamesTheElementTheViewAndTheSubshell()
    {
        AtomState.Apply("Fe:cloud:3d");
        Assert.Equal("Fe", AtomState.Element.Symbol);
        Assert.Equal("Cloud", AtomState.View.ToString());
        Assert.Equal("3d", AtomState.CloudFocus);
        // (its parts in any order; another element clears the subshells picked for the last)
        AtomState.Apply("orbitals:6");
        Assert.Equal("C", AtomState.Element.Symbol);
        Assert.Equal("Orbitals", AtomState.View.ToString());
        Assert.Null(AtomState.CloudFocus);
        Assert.Null(AtomState.Subshell);
    }
}

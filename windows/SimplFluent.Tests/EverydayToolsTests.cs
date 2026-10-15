using Simpl.Screens;
using Simpl.Shell;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The tools program's everyday tools (Screens/More/ToolsScreen.cs, Shell/Search/PaletteCommand.cs): each in its group
/// at the end, known to the tools' centre by its key, with a short name, a symbol WPF-UI can draw, and the search box's
/// words for it.
/// </summary>
public class EverydayToolsTests
{
    public EverydayToolsTests() => _ = Profile.Folder;

    [Fact]
    public void TheWordCounterEndsTheStudyGroup()
    {
        // (the Study planner and Verb conjugations, shipped after it, come after it)
        var study = ToolsScreen.Groups.First(g => g.Title == "Study").Keys;
        Assert.Equal("words", study[^3]);
        var kind = ToolsCenter.KindOf("words");
        Assert.NotNull(kind);
        Assert.Same(kind, ToolsScreen.Kinds.First(k => k.Key == "words"));
        Assert.Equal("Word Counter", kind!.Name);
        Assert.Equal("Words, characters, reading time.", kind.Note);
        Assert.Equal("#8e8e93", kind.Hex);
        Assert.Equal("Words", ToolsCenter.ShortName("words"));
    }

    [Fact]
    public void TheMatrixCalculatorEndsTheMathAndScienceGroup()
    {
        var maths = ToolsScreen.Groups.First(g => g.Title == "Math & science").Keys;
        Assert.Equal("matrix", maths[^1]);
        var kind = ToolsCenter.KindOf("matrix");
        Assert.NotNull(kind);
        Assert.Equal("Matrix Calculator", kind!.Name);
        Assert.Equal("Determinant, inverse, solve, RREF.", kind.Note);
        Assert.Equal("#5e5ce6", kind.Hex);
        Assert.Equal("Matrix", ToolsCenter.ShortName("matrix"));
        // (its symbol is no biology tile's: a table of cells, not Crosses' grid)
        Assert.DoesNotContain(BioTools.Kinds, b => b.Symbol == kind.Symbol);
        Assert.Equal(new[] { "add", "sub", "mul", "scale", "transpose", "det", "inverse", "rank", "rref", "trace", "solve", "eigen", "power" }, MatrixState.Ops.Select(o => o.Key));
    }

    [Fact]
    public void StatisticsComesBeforeTheMatrixCalculator()
    {
        var maths = ToolsScreen.Groups.First(g => g.Title == "Math & science").Keys;
        Assert.Equal("stats", maths[^2]);
        var kind = ToolsCenter.KindOf("stats");
        Assert.NotNull(kind);
        Assert.Equal("Statistics", kind!.Name);
        Assert.Equal("Mean, SD, regression, z and t.", kind.Note);
        Assert.Equal("#ff2d55", kind.Hex);
        Assert.Equal("Stats", ToolsCenter.ShortName("stats"));
        Assert.Equal(new[] { "Describe", "Regression", "Distributions", "Tests" }, StatsState.Tabs);
    }

    [Theory]
    [InlineData("describe", 0)]
    [InlineData("regression", 1)]
    [InlineData("dist", 2)]
    [InlineData("Tests", 3)]
    [InlineData("histogram", null)]
    public void ALaunchNamesStatisticsPage(string word, int? tab) => Assert.Equal(tab, StatsState.TabOf(word));

    [Fact]
    public void EveryKeyIsItsOwnAndEverySymbolDrawable()
    {
        Assert.Equal(ToolsScreen.Kinds.Length, ToolsScreen.Kinds.Select(k => k.Key).Distinct().Count());
        // (WPF-UI 4.3 draws only the Fluent icons whose code is at most U+FFFF: README, Icons)
        Assert.All(ToolsScreen.Kinds, k => Assert.True((int)k.Symbol <= 0xFFFF, $"{k.Key}'s symbol is past U+FFFF"));
    }

    [Theory]
    [InlineData("words", "words")]
    [InlineData("word count", "words")]
    [InlineData("essay", "words")]
    [InlineData("reading time", "words")]
    [InlineData("stats", "stats")]
    [InlineData("statistics", "stats")]
    [InlineData("standard deviation", "stats")]
    [InlineData("t-test", "stats")]
    [InlineData("regression", "stats")]
    [InlineData("matrix", "matrix")]
    [InlineData("determinant", "matrix")]
    [InlineData("eigenvalues", "matrix")]
    [InlineData("linear algebra", "matrix")]
    public void TheSearchBoxOpensEachByItsWords(string typed, string key)
    {
        var command = PaletteCommand.Named(PaletteCommand.EverydayTools().ToList(), typed);
        Assert.NotNull(command);
        Assert.Equal(PaletteDo.Tool, command!.Action.Do);
        Assert.Equal(key, command.Action.Text);
    }

    [Fact]
    public void TheEverydayWordsClashWithNoOtherCommand()
    {
        // (a word of theirs that another command already answers to would open that command instead)
        var everyday = PaletteCommand.EverydayTools().SelectMany(c => c.Names).ToList();
        var others = PaletteCommand.ChemistryTools().SelectMany(c => c.Names).ToHashSet();
        Assert.DoesNotContain(everyday, others.Contains);
        Assert.Equal(everyday.Count, everyday.Distinct().Count());
    }
}

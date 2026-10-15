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
        var study = ToolsScreen.Groups.First(g => g.Title == "Study").Keys;
        Assert.Equal("words", study[^1]);
        var kind = ToolsCenter.KindOf("words");
        Assert.NotNull(kind);
        Assert.Same(kind, ToolsScreen.Kinds.First(k => k.Key == "words"));
        Assert.Equal("Word Counter", kind!.Name);
        Assert.Equal("Words, characters, reading time.", kind.Note);
        Assert.Equal("#8e8e93", kind.Hex);
        Assert.Equal("Words", ToolsCenter.ShortName("words"));
    }

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

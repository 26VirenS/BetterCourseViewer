using Simpl.Screens;
using Simpl.Shell;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>Verb Conjugations' place among the tools (Screens/More/ToolsScreen.cs) and in the search box
/// (Shell/Search/PaletteCommand.cs); its engine is VerbsTests'.</summary>
public class VerbsToolTests
{
    public VerbsToolTests() => _ = Profile.Folder;

    [Fact]
    public void VerbConjugationsIsAStudyToolWithTheMacsWords()
    {
        var kind = ToolsCenter.KindOf("verbs");
        Assert.NotNull(kind);
        Assert.Equal("Conjugate verbs in 5 languages.", kind!.Note);
        Assert.Equal("#30b0c7", kind.Hex);
        Assert.Equal("Verbs", ToolsCenter.ShortName("verbs"));
        Assert.Equal("verbs", ToolsScreen.Groups.First(g => g.Title == "Study").Keys.Last());
        Assert.True((int)kind.Symbol <= 0xFFFF);
        foreach (var word in new[] { "verbs", "conjugate", "spanish", "tense" })
        {
            var command = PaletteCommand.Named(PaletteCommand.NewTools().ToList(), word);
            Assert.NotNull(command);
            Assert.Equal(PaletteDo.Tool, command!.Action.Do);
            Assert.Equal("verbs", command.Action.Text);
        }
    }
}

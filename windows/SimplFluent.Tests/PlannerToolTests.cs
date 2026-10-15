using Simpl.Screens;
using Simpl.Shell;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>The Study Planner's place among the tools (Screens/More/ToolsScreen.cs) and in the search box
/// (Shell/Search/PaletteCommand.cs); its sums are PlannerTests'.</summary>
public class PlannerToolTests
{
    public PlannerToolTests() => _ = Profile.Folder;

    [Fact]
    public void TheStudyPlannerIsAStudyToolWithTheMacsWords()
    {
        var kind = ToolsCenter.KindOf("plan");
        Assert.NotNull(kind);
        Assert.Equal("Study blocks from your due dates.", kind!.Note);
        Assert.Equal("#34c759", kind.Hex);
        Assert.Equal("Plan", ToolsCenter.ShortName("plan"));
        Assert.Equal("plan", ToolsScreen.Groups.First(g => g.Title == "Study").Keys.Last());
        Assert.True((int)kind.Symbol <= 0xFFFF);
        foreach (var word in new[] { "plan", "study plan", "exam prep" })
        {
            var command = PaletteCommand.Named(PaletteCommand.NewTools().ToList(), word);
            Assert.NotNull(command);
            Assert.Equal(PaletteDo.Tool, command!.Action.Do);
            Assert.Equal("plan", command.Action.Text);
        }
    }
}

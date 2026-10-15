using Simpl.Screens;
using Simpl.Shell;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Tools page's Physics section (tools, Tools/Physics/PhysTools.cs): its tools in their order, their keys among every
/// tool's, a palette of their own beside the chemistry and biology tools' with symbols WPF-UI draws, their short names,
/// the search box's words for each, and the state a launch gives Kinematics &amp; Projectiles.
/// </summary>
public class PhysicsToolsTests
{
    public PhysicsToolsTests() => _ = Profile.Folder;

    [Fact]
    public void ThePhysicsSectionHasItsToolsInOrder()
    {
        Assert.Equal(new[] { "motion" }, PhysTools.Kinds.Select(k => k.Key));
        Assert.Equal(new[] { "Kinematics & Projectiles" }, PhysTools.Kinds.Select(k => k.Name));
        Assert.All(PhysTools.Kinds, k => Assert.False(string.IsNullOrWhiteSpace(k.Note)));
        Assert.All(PhysTools.Kinds, k => Assert.True(k.Note.Length <= 32 && k.Note.EndsWith('.')));
    }

    [Fact]
    public void EveryPhysicsToolIsAmongTheToolsOnceAndNowhereElse()
    {
        Assert.Equal(ToolsScreen.Kinds.Length, ToolsScreen.Kinds.Select(k => k.Key).Distinct().Count());
        foreach (var k in PhysTools.Kinds)
        {
            Assert.Same(k, ToolsCenter.KindOf(k.Key));
            Assert.True(PhysTools.IsPhysics(k.Key));
            Assert.False(BioTools.IsBiology(k.Key));
            Assert.Null(ChemTools.Kind(k.Key));
            Assert.DoesNotContain(ToolsScreen.Groups, g => g.Keys.Contains(k.Key));
        }
        // (after the chemistry tools, before the biology ones, as the page shows them)
        var keys = ToolsScreen.Kinds.Select(k => k.Key).ToList();
        Assert.True(keys.IndexOf("atoms") < keys.IndexOf("motion"));
        Assert.True(keys.IndexOf("motion") < keys.IndexOf("cell"));
        Assert.False(PhysTools.IsPhysics("ptable"));
    }

    [Fact]
    public void PhysicsHasAPaletteOfItsOwn()
    {
        var others = ChemTools.Kinds.Concat(BioTools.Kinds).Select(k => k.Hex.ToLowerInvariant()).ToHashSet();
        var physics = PhysTools.Kinds.Select(k => k.Hex.ToLowerInvariant()).ToList();
        Assert.Equal(physics.Count, physics.Distinct().Count());
        Assert.DoesNotContain(physics, others.Contains);
        // (no physics tool wears a chemistry or biology tool's symbol, and every symbol's code is one WPF-UI 4.3 draws)
        Assert.DoesNotContain(PhysTools.Kinds, p => ChemTools.Kinds.Concat(BioTools.Kinds).Any(o => o.Symbol == p.Symbol));
        Assert.All(PhysTools.Kinds, k => Assert.True((int)k.Symbol <= 0xFFFF, $"{k.Key}'s symbol is above U+FFFF"));
    }

    [Fact]
    public void BothAppsTheSame()
    {
        // (the Mac's PhysTools.swift: its keys, lines, colours and short names)
        Assert.Equal("#0a84ff", PhysTools.Kind("motion")!.Hex.ToLowerInvariant());
        Assert.Equal("1D motion and projectile paths.", PhysTools.Kind("motion")!.Note);
        Assert.Equal("Motion", ToolsCenter.ShortName("motion"));
    }

    [Theory]
    [InlineData("motion", "motion")]
    [InlineData("kinematics", "motion")]
    [InlineData("projectile", "motion")]
    [InlineData("free fall", "motion")]
    [InlineData("acceleration", "motion")]
    public void TheSearchBoxOpensEachByItsWords(string typed, string key)
    {
        var command = PaletteCommand.Named(PaletteCommand.PhysicsTools().ToList(), typed);
        Assert.NotNull(command);
        Assert.Equal(PaletteDo.Tool, command!.Action.Do);
        Assert.Equal(key, command.Action.Text);
    }

    [Fact]
    public void NoPhysicsWordIsAnotherCommandsName()
    {
        var taken = new[] { "light", "dark", "system", "theme", "look", "new", "update", "next", "test", "find", "search", "show", "what", "page", "file",
            "read", "note", "add", "task", "help", "tour", "guide", "web", "site", "due", "week", "tool", "timer", "calculator", "periodic", "element", "atom", "atoms", "orbitals", "molecule" };
        foreach (var c in PaletteCommand.PhysicsTools()) foreach (var n in c.Names) Assert.DoesNotContain(n, taken);
    }

    [Fact]
    public void ALaunchStateNamesTheTabAndTheValues()
    {
        var l = MotionLaunch.Parse("projectile:v0=20,angle=45,h=0");
        Assert.Equal("projectile", l.Tab);
        Assert.Equal("20", l.Get("v0", "u"));
        Assert.Equal("45", l.Get("angle"));
        Assert.Equal("0", l.Get("h"));
        Assert.Null(l.Get("g"));
        // (the values alone, in --motion-values; a tab alone; nothing)
        var v = MotionLaunch.Parse("1d", "v0=0,a=2,t=5");
        Assert.Equal("1d", v.Tab);
        Assert.Equal("0", v.Get("v0"));
        Assert.Equal("5", v.Get("t"));
        Assert.True(v.Any);
        var bare = MotionLaunch.Parse("v0=20,angle=30");
        Assert.Null(bare.Tab);
        Assert.Equal("30", bare.Get("angle"));
        var none = MotionLaunch.Parse(null);
        Assert.Null(none.Tab);
        Assert.False(none.Any);
    }
}

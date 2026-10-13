using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Cell Explorer's models (Tools/Biology/Model/CellExplorer*.cs, CellMembraneModel.cs) against the specification: §5.1's
/// words on the cards, the Compare table exactly, each cell built from its seed the same every time, the cutaway, explode
/// and scale rules, the scene the 3D view is given, the membrane patch and the quiz. Its self-check lines (§5.1's counts,
/// the triangle budgets, the placement rules) run with the rest in BiologyTests.TheSelfCheckIsAllOK. Plain .NET: no window
/// is opened.
/// </summary>
public class CellExplorerTests
{
    // ---- the words (§5.1, §5.1.4, §5.1.6) ----

    [Fact]
    public void TheCardsSayWhatTheSpecificationSays()
    {
        var golgi = CEParts.Part(CEKind.Animal, "golgi")!;
        Assert.Equal("Modifies, sorts and packages proteins and lipids from the ER; ships them in vesicles (to the membrane, lysosomes or outside).", golgi.Function);
        Assert.Equal("Single membrane · one around each flattened sac (cisterna)", golgi.StructureText);
        Assert.Equal("Cis face toward the ER, trans face toward the membrane.", golgi.Mixup);
        var wall = CEParts.Part(CEKind.Plant, "wall")!;
        Assert.Equal("Plant cells have **both** a wall and a membrane.", wall.Mixup);
        Assert.Equal(CEGroup.Outside, wall.Group);
        var ribosome = CEParts.Part(CEKind.Plant, "ribosome")!;
        Assert.EndsWith("In plant cells: 80S in cytosol; 70S in chloroplasts and mitochondria.", ribosome.Function);
        Assert.Equal("animal ✓ · plant ✓ · bacteria ✓", ribosome.FoundInText);
        Assert.Contains("bacteria: 70S", CEParts.Part(CEKind.Bacterium, "ribosome")!.FoundInNotes);
        Assert.Equal("animal ✗ · plant ✗ · bacteria ✓", CEParts.Part(CEKind.Bacterium, "fimbria")!.FoundInText);
        // (double membranes: nucleus, mitochondria, chloroplasts; one: ER, Golgi, lysosomes, vacuole, vesicles; none: the rest)
        Assert.Equal(CEMembranes.Single, CEParts.Part(CEKind.Plant, "vacuole")!.Membranes);
        Assert.Equal(CEMembranes.None, CEParts.Part(CEKind.Animal, "centrosome")!.Membranes);
        Assert.Equal("#2c3e8f", CEParts.Part(CEKind.Bacterium, "ribosome")!.Colour.Light);
        Assert.Equal(0.35, CEParts.Part(CEKind.Plant, "vacuole")!.Alpha);
    }

    [Fact]
    public void ThePartsListIsInItsGroups()
    {
        foreach (var k in CEKinds.All)
        {
            var groups = CEParts.All(k).Select(p => (int)p.Group).ToList();
            Assert.Equal(groups.OrderBy(g => g), groups);
        }
        Assert.False(CEParts.Part(CEKind.Animal, "flagellum")!.DefaultVisible);
        Assert.Contains("flagellum", CellExplorerModel.Cell(CEKind.Animal).Hidden);
    }

    [Fact]
    public void TheCompareTableIsTheSpecifications()
    {
        var rows = CECompare.Rows;
        Assert.Equal(23, rows.Count);
        Assert.Equal(new[] { "✓", "✓", "✗ (respiration on the plasma membrane)" }, rows.First(r => r.Structure == "Mitochondria").Cells);
        Assert.Equal(new[] { "✗", "✓ cellulose", "✓ peptidoglycan" }, rows.First(r => r.Structure == "Cell wall").Cells);
        Assert.Equal((CEKind.Plant, "wall"), rows.First(r => r.Structure == "Cell wall").Open);
        Assert.Equal((CEKind.Animal, "chromatin"), rows.First(r => r.Structure == "DNA").Open);
        Assert.Equal((CEKind.Bacterium, "fimbria"), rows.First(r => r.Structure == "Fimbriae / pili").Open);
        Assert.Null(rows.First(r => r.Structure == "Typical size").Open);
        Assert.Equal((CEMark.Partly, "(related proteins such as FtsZ and MreB, no true microtubule network)"), CEMarks.Read(rows.First(r => r.Structure == "Cytoskeleton").Cells[2]));
    }

    // ---- the cells (§4.1 Rendering, §5.1.5) ----

    [Fact]
    public void ACellIsTheSameEveryTime()
    {
        var a = CEBuild.Build(new CEBuildOptions(CEKind.Bacterium));
        var b = CEBuild.Build(new CEBuildOptions(CEKind.Bacterium));
        Assert.Equal(a.Triangles, b.Triangles);
        Assert.Equal(a.Instances.Select(i => i.Centre), b.Instances.Select(i => i.Centre));
        // (the seed's first draw is the cell's: SplitMix64 0xBAC7)
        var g = new BioRandom(0xBAC7);
        Assert.Equal(g.Next(), new BioRandom(CEKind.Bacterium.Seed()).Next());
    }

    [Fact]
    public void TheCutawayLeavesTheOctantEmpty()
    {
        var m = CEBuild.Build(new CEBuildOptions(CEKind.Bacterium));
        var whole = CEBuild.Build(new CEBuildOptions(CEKind.Bacterium, Cutaway: false));
        // (counted the same, drawn fewer)
        Assert.Equal(whole.Placed["ribosome"], m.Placed["ribosome"]);
        Assert.True(m.Shown["ribosome"] < whole.Shown["ribosome"]);
        Assert.True(m.PartsShown.Contains("cytoplasm") && !whole.PartsShown.Contains("cytoplasm"));
    }

    [Fact]
    public void TheSceneHasTheCellsPartsAndLabels()
    {
        var m = CEBuildCache.Model(new CEBuildOptions(CEKind.Bacterium));
        var c = CEScene.Content(m, new CEView());
        // (the many — ribosomes, fimbriae — in one item a material, but for the one their label points to)
        Assert.Contains(c.Items, i => i.Id == "organelle:ribosome");
        Assert.Contains(c.Items, i => i.Id!.StartsWith("organelle:ribosome:", StringComparison.Ordinal));
        Assert.True(c.Items.Count < 80, $"{c.Items.Count} items");
        Assert.Equal(m.PartsShown.Count, c.Labels.Count);
        Assert.Equal(c.Labels.Count, c.Labels.Select(l => l.Id).Distinct().Count());
        // (hidden: no items, no label)
        var hidden = CEScene.Content(m, new CEView(Hidden: new[] { "capsule" }));
        Assert.DoesNotContain(hidden.Items, i => i.Id!.StartsWith("organelle:capsule", StringComparison.Ordinal));
        Assert.DoesNotContain(hidden.Labels, l => l.Id == "organelle:capsule");
        // (exploded: the shells fade, the flagella stay)
        var exploded = CEScene.Content(m, new CEView(Explode: 1));
        Assert.All(exploded.Items.Where(i => i.Id!.StartsWith("organelle:pm", StringComparison.Ordinal)), i => Assert.True(i.Material.Alpha <= 0.15 + 1e-9));
        Assert.True(exploded.Frame!.Value.Radius > c.Frame!.Value.Radius);
    }

    [Fact]
    public void TrueScaleDrawsTheTinyThingsAtTheirSize()
    {
        var teaching = CEBuildCache.Model(new CEBuildOptions(CEKind.Bacterium));
        var real = CEBuildCache.Model(new CEBuildOptions(CEKind.Bacterium, TrueScale: true));
        Assert.Equal(0.020, teaching.Instances.First(i => i.Part == "ribosome").Radius, 12);
        Assert.Equal(0.010, real.Instances.First(i => i.Part == "ribosome").Radius, 12);
        Assert.InRange(real.Extra["ribosome.free"], 19_000, 20_000);
        Assert.Equal("24 capsules r 0.45 µm, length 2.2 µm, with cristae", CEParts.InThisModel(CEParts.Part(CEKind.Animal, "mito")!, false));
        Assert.Contains("4× its true size", CEParts.InThisModel(CEParts.Part(CEKind.Animal, "ribosome")!, false));
    }

    [Fact]
    public void LookingInsideAMitochondrionShowsItsCristae()
    {
        var m = CEBuildCache.Model(new CEBuildOptions(CEKind.Animal));
        var (instances, labels) = CEBuild.LookInsideScene(m, "mito");
        Assert.Single(instances);
        Assert.Contains(labels, l => l.Text == "Cristae");
        var nucleus = CEBuild.LookInsideScene(m, "nucleus");
        Assert.Contains(nucleus.Labels, l => l.Text == "Nucleolus");
        Assert.Contains(nucleus.Instances, i => i.Part == "pore");
    }

    // ---- the state the tool keeps ----

    [Fact]
    public void SelectingAndIsolating()
    {
        var k = CEKind.Plant;
        CellExplorerModel.Isolate(k, "nucleus", lookInside: true);
        CellExplorerModel.Select(k, "pore");
        Assert.Equal("nucleus", CellExplorerModel.Cell(k).Isolated);
        CellExplorerModel.Select(k, "mito");
        Assert.Null(CellExplorerModel.Cell(k).Isolated);
        CellExplorerModel.SetVisible(k, "mito", false);
        Assert.Null(CellExplorerModel.Cell(k).Selected);
        CellExplorerModel.SetVisible(k, "mito", true);
        Assert.DoesNotContain("mito", CellExplorerModel.View(k, false).Hidden!);
    }

    [Fact]
    public void TheQuizCountsRightAndMissed()
    {
        var g = new BioRandom(11);
        var q = new CEQuiz(CEQuizMode.Find, CEKind.Bacterium, CEParts.All(CEKind.Bacterium).Select(p => p.Id).ToList(), ref g);
        Assert.Equal(10, q.Questions.Count);
        var first = q.Current!.Part;
        Assert.False(q.Answer(first == "pm" ? "wall" : "pm"));
        Assert.True(q.MissedThis);
        Assert.True(q.Answer(first));
        Assert.Equal(0, q.Score);
        Assert.Equal(new[] { first }, q.Misses);
        while (!q.Done) q.Answer(q.Current!.Part);
        Assert.Equal(9, q.Score);
    }

    // ---- the membrane (§4.1.1) ----

    [Fact]
    public void TheMembranePatch()
    {
        Assert.Equal("integral", CEMembrane.PartOf("part:integral:protein:5"));
        Assert.Equal("integral:channel", CEMembrane.PartOf("part:integral:channel"));
        Assert.Equal("bilayer:heads", CEMembrane.PartOf("part:bilayer:heads:pc"));
        Assert.Equal("side:outside", CEMembrane.PartOf("side:outside"));
        Assert.Null(CEMembrane.PartOf("atom:3"));
        var plain = CEMembrane.Build(false, cholesterol: false, glyco: false, proteins: false, carrierAnimated: true);
        Assert.DoesNotContain(plain.Content.Items, i => i.Id is "part:cholesterol" or "part:integral:carrier" or "part:integral:protein:5");
        Assert.Contains(plain.Content.Items, i => i.Id == "part:integral:channel");
        var moving = CEMembrane.Moving();
        Assert.Equal(7, moving.Carrier.Count);
        // (the water goes in from the outside: the channel's way runs against the membrane's up)
        Assert.True(BioV.Dot(moving.ChannelPath.Out - moving.ChannelPath.In, moving.Up) < 0);
    }

    // ---- the scale bar (§4.1 item 7) ----

    [Fact]
    public void TheScaleBarPicksAPreferredLength()
    {
        double[] cells = { 1, 5, 10 }, more = { 0.1, 0.2, 0.5, 2, 20, 50, 100 };
        Assert.Equal(1, CellScaleBar.Length(100, cells, more));
        Assert.Equal(5, CellScaleBar.Length(20, cells, more));
        Assert.Equal(10, CellScaleBar.Length(8, cells, more));
        // (a bacterium close up: half a micrometre, said in nanometres)
        Assert.Equal(0.5, CellScaleBar.Length(200, cells, more));
        Assert.Equal("500 nm", CellScaleBar.Words(0.5, "µm"));
        Assert.Equal("5 µm", CellScaleBar.Words(5, "µm"));
        Assert.Equal("10 nm", CellScaleBar.Words(10, "nm"));
        // (nothing fits: the one nearest 110 long)
        Assert.Equal(100, CellScaleBar.Length(0.5, cells, more));
    }
}

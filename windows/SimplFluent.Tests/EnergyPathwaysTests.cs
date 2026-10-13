using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Photosynthesis & Respiration's model (Tools/Biology/Model/EnergyPathwaysModel.cs) against the specification's §5.8:
/// the stage table read word for word, the step-throughs' running totals and carbons against it, fermentation's 2 ATP,
/// one G3P's costs, following the atoms, and what the map shows by day and by night. Plain .NET: no window is opened.
/// </summary>
public class EnergyPathwaysTests
{
    private static (int, int) T(EnergyStepInfo s, string name) => EnergyPathwaysModel.Total(s, name);

    [Fact]
    public void TheStageTableIsTheSpecifications()
    {
        var s = EnergyPathwaysModel.Stages;
        Assert.Equal(new[] { "Light reactions", "Calvin cycle", "Glycolysis", "Pyruvate oxidation", "Citric acid (Krebs) cycle", "Oxidative phosphorylation", "Total", "Fermentation" }, s.Select(x => x.Name));
        Assert.Equal("Thylakoid membranes (chloroplast)", EnergyPathwaysModel.Stage("light").Where);
        Assert.Equal("O₂ (from **water**), NADPH, ATP", EnergyPathwaysModel.Stage("light").Out);
        Assert.Equal("2 pyruvate, 2 NADH, 4 ATP (net **2**)", EnergyPathwaysModel.Stage("glycolysis").Out);
        Assert.Equal("about 26–28 ATP", EnergyPathwaysModel.Stage("oxphos").Out);
        Assert.Equal("about 30–32 ATP per glucose (older books: 36–38)", EnergyPathwaysModel.Stage("total").Out);
        Assert.Equal("", EnergyPathwaysModel.Stage("pyruvate").Notes);
        Assert.Equal(new[] { "light", "calvin", "glycolysis", "pyruvate", "krebs", "oxphos", "fermentation" }, EnergyPathwaysModel.Pressable.Select(x => x.Id));
        Assert.True(EnergyPathwaysModel.Stage("light").NeedsLight && EnergyPathwaysModel.Stage("calvin").NeedsLight && !EnergyPathwaysModel.Stage("krebs").NeedsLight);
        Assert.Equal("6 CO₂ + 6 H₂O + light → C₆H₁₂O₆ + 6 O₂", EnergyPathwaysModel.PhotosynthesisEquation);
        Assert.Equal("Chloroplast", EnergyPathwaysModel.Organelle("stroma"));
        Assert.Equal("Mitochondrion", EnergyPathwaysModel.Organelle("inner"));
        Assert.Equal(10, EnergyPathwaysModel.Count("10 NADH, 2 FADH₂, O₂ (final e⁻ acceptor → H₂O)", "NADH"));
        Assert.Equal(0, EnergyPathwaysModel.Count("3 CO₂, 9 ATP, 6 NADPH per G3P", "NADH"));
    }

    [Fact]
    public void RespirationsRunningTotals()
    {
        var (steps, info) = EnergyPathwaysModel.Respiration();
        Assert.Equal(9, steps.Count);
        Assert.Equal(steps.Count, info.Count);
        Assert.All(steps, s => Assert.InRange(s.Title.Length, 1, 40));
        Assert.Equal((-2, -2), T(info[1], "ATP"));
        Assert.Equal("2", info[2].Counts.First(c => c.Name == "ATP").Text);
        Assert.Equal("+4", info[2].Counts.First(c => c.Name == "ATP").ChangeText);
        var end = info[^1];
        Assert.Equal("30–32", end.Counts.First(c => c.Name == "ATP").Text);
        Assert.Equal(((10, 10), (2, 2), (6, 6)), (T(end, "NADH"), T(end, "FADH₂"), T(end, "CO₂")));
        Assert.Equal("+26–28", info[7].Counts.First(c => c.Name == "ATP").ChangeText);
        Assert.All(info, i => Assert.Equal(6, i.CarbonTotal));
        Assert.Equal(new[] { "cytosol", "cytosol", "cytosol", "matrix", "matrix", "matrix", "inner", "inner", null }, info.Select(i => i.Place));
        Assert.Equal("2 pyruvate", info[2].Token);
        Assert.Equal(5, EnergyPathwaysModel.RespirationGroups(EnergyFermentation.None).Count);
    }

    [Fact]
    public void FermentationMakesOnlyGlycolysissTwoATP()
    {
        foreach (var f in new[] { EnergyFermentation.Lactate, EnergyFermentation.Ethanol })
        {
            var (steps, info) = EnergyPathwaysModel.Respiration(f);
            Assert.Equal(5, steps.Count);
            Assert.Equal((2, 2), T(info[^1], "ATP"));
            Assert.Equal((0, 0), T(info[^1], "NADH"));
            Assert.Equal(f == EnergyFermentation.Ethanol ? (2, 2) : (0, 0), T(info[^1], "CO₂"));
            Assert.All(info.Skip(3), i => Assert.Equal("fermentation", i.Stage));
            Assert.All(info, i => Assert.Equal(6, i.CarbonTotal));
        }
    }

    [Fact]
    public void OneG3P()
    {
        var (steps, info) = EnergyPathwaysModel.Photosynthesis();
        Assert.Equal(9, steps.Count);
        var end = info[^1];
        Assert.Equal(((3, 3), (9, 9), (6, 6), (1, 1)), (T(end, "CO₂ fixed"), T(end, "ATP used"), T(end, "NADPH used"), T(end, "G3P made")));
        Assert.Equal(((9, 9), (6, 6), (6, 6), (3, 3)), (T(end, "ATP made"), T(end, "NADPH made"), T(end, "H₂O split"), T(end, "O₂ released")));
        Assert.Contains("from water", steps[1].Text);
        Assert.DoesNotContain("**", steps[1].Text);
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 2, 2, 2, 2 }, steps.Select(s => s.Group ?? 0));
        Assert.All(info.Where(i => i.Carbons.Count > 0), i => Assert.Equal(18, i.CarbonTotal));
    }

    [Fact]
    public void FollowingTheAtoms()
    {
        var o = EnergyPathwaysModel.Trace(EnergyTrace.Oxygen);
        Assert.Equal(new[] { "H₂[O]", "[O]=O", "[O]=O", "H₂[O]" }, o.Select(x => x.Formula));
        Assert.Equal(new[] { "thylakoid", "thylakoid", "cytosol", "inner" }, o.Select(x => x.Place));
        var c = EnergyPathwaysModel.Trace(EnergyTrace.Carbon);
        Assert.Equal("[C]O₂", c[0].Formula);
        Assert.Equal("[C]O₂", c[^1].Formula);
        Assert.Equal(3, c.First(x => x.Molecule == "Glucose").Traced);
        Assert.Equal(6, EnergyPathwaysModel.TraceSteps(EnergyTrace.Carbon).Count);
    }

    [Fact]
    public void WhatTheMapShows()
    {
        try
        {
            EnergyPathwaysModel.SetFermentation(EnergyFermentation.Ethanol);
            Assert.Equal(5, EnergyPathwaysModel.Respiring.Steps.Count);
            Assert.Equal(5, EnergyPathwaysModel.RespirationInfo.Count);
            EnergyPathwaysModel.SetFermentation(EnergyFermentation.None);
            Assert.Equal(9, EnergyPathwaysModel.Respiring.Steps.Count);
            EnergyPathwaysModel.SetTrace(EnergyTrace.Carbon);
            Assert.Equal(6, EnergyPathwaysModel.Tracing.Steps.Count);
            Assert.Equal(6, EnergyPathwaysModel.TraceNow.Count);
            EnergyPathwaysModel.Select("krebs");
            Assert.Equal("krebs", EnergyPathwaysModel.Selected);
            EnergyPathwaysModel.Select("nonsense");
            Assert.Equal("krebs", EnergyPathwaysModel.Selected);

            // plants respire too: by night the chloroplast's stages stop, the mitochondrion's go on
            EnergyPathwaysModel.SetPlantsRespire(true);
            Assert.True(EnergyPathwaysModel.Working("light"));
            EnergyPathwaysModel.SetDark(true);
            Assert.False(EnergyPathwaysModel.Working("light"));
            Assert.False(EnergyPathwaysModel.Working("calvin"));
            Assert.True(EnergyPathwaysModel.Working("krebs"));
            Assert.True(EnergyPathwaysModel.Working("glycolysis"));
            EnergyPathwaysModel.SetPlantsRespire(false);
            Assert.False(EnergyPathwaysModel.Dark);
        }
        finally
        {
            EnergyPathwaysModel.SetFermentation(EnergyFermentation.None);
            EnergyPathwaysModel.SetTrace(EnergyTrace.Oxygen);
            EnergyPathwaysModel.Select("light");
            EnergyPathwaysModel.SetPlantsRespire(false);
        }
    }
}

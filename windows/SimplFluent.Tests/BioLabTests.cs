using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Bio Lab Calculator's model (Tools/Biology/Model/BioLab*.cs) against the specification's §5.5: numbers read as a
/// student types them, significant figures counted and kept to the least precise input plus one, every calculator's
/// test vector, solving the other way round, the reasons a field turns red, and the state the tool keeps. Plain .NET:
/// no window is opened.
/// </summary>
public class BioLabTests
{
    private static LabResult Run(string calc, string? mode = null, params (string Field, string Text)[] texts) =>
        LabCalcs.Find(calc)!.Run(mode, texts.ToDictionary(t => t.Field, t => t.Text), null, 3);

    // ---- numbers ----

    [Theory]
    [InlineData("0.050", 0.05, 2)]
    [InlineData("250", 250, 2)]
    [InlineData("250.", 250, 3)]
    [InlineData("2.0", 2, 2)]
    [InlineData("3,000", 3000, 1)]
    [InlineData("12,500.5", 12500.5, 6)]
    [InlineData("1.00", 1, 3)]
    [InlineData("1e-7", 1e-7, 1)]
    [InlineData("1.5 × 10^9", 1.5e9, 2)]
    [InlineData("1.5x10^9", 1.5e9, 2)]
    [InlineData("2.5 × 10⁸", 2.5e8, 2)]
    [InlineData("−2.45", -2.45, 3)]
    public void NumbersAreReadAsTyped(string text, double value, int figures)
    {
        var n = LabNumbers.Parse(text);
        Assert.NotNull(n);
        Assert.Equal(value, n!.Value.Value, 9);
        Assert.Equal(figures, n.Value.Figures);
    }

    [Fact]
    public void ExactNumbersAndNonsense()
    {
        Assert.True(LabNumbers.Parse("10^-6")!.Value.Exact);
        Assert.True(LabNumbers.Parse("10⁻⁶")!.Value.Exact);
        Assert.Equal(1e-6, LabNumbers.Parse("10⁻⁶")!.Value.Value, 15);
        Assert.True(LabNumbers.Parse("0")!.Value.Exact);
        Assert.True(LabNumbers.Parse("150", exact: true)!.Value.Exact);
        foreach (var bad in new[] { "", " ", "abc", "1,5", "1.2.3", "--4", "1e", "×10^3" }) Assert.Null(LabNumbers.Parse(bad));
        var (list, badWord) = LabNumbers.ParseList("12.1, 13.4 11.8;12.9\n13.0");
        Assert.Null(badWord);
        Assert.Equal(5, list!.Count);
        Assert.Equal("x", LabNumbers.ParseList("1, x, 3").Bad);
        Assert.Equal(4, LabNumbers.ParseList("9:3:3:1").Numbers!.Count);
    }

    [Fact]
    public void AnswersKeepToTheLeastPreciseInputPlusOne()
    {
        Assert.Equal(3, LabNumbers.Allowed(3, new[] { LabNumbers.Parse("0.50")!.Value, LabNumbers.Parse("58.44")!.Value }));
        Assert.Equal(2, LabNumbers.Allowed(3, new[] { LabNumbers.Parse("0.1")!.Value, LabNumbers.Parse("1.0")!.Value }));
        Assert.Equal(5, LabNumbers.Allowed(6, new[] { LabNumbers.Parse("58.44")!.Value }));
        Assert.Equal(6, LabNumbers.Allowed(6, new[] { LabNumbers.Parse("100", exact: true)!.Value }));
        // the same calculator with a less precise input: fewer figures, and the steps say why
        var r = Run("molarity", "mass", ("c", "0.5"));
        Assert.Equal("7.3", r.Value);
        Assert.Contains(r.Steps, s => s.Contains("0.5 M has 1"));
        // a higher setting gives more figures, as far as the inputs allow
        var six = LabCalcs.Find("molarity")!.Run("mass", new Dictionary<string, string> { ["c"] = "0.5000", ["v"] = "250.0" }, null, 6);
        Assert.Equal("7.3050", six.Value);
    }

    [Theory]
    [InlineData(7.305, 3, false, "7.31")]
    [InlineData(1006.2, 2, false, "1,000")]
    [InlineData(1006.2, 3, false, "1,010")]
    [InlineData(1006.2, 4, false, "1,006")]
    [InlineData(6400, 2, false, "6,400")]
    [InlineData(8.0386e-5, 3, false, "8.04 × 10⁻⁵")]
    [InlineData(1.5e9, 2, false, "1.5 × 10⁹")]
    [InlineData(840000, 3, true, "8.4 × 10⁵")]
    [InlineData(0.45, 3, false, "0.450")]
    [InlineData(5, 2, false, "5.0")]
    [InlineData(0.62, 3, true, "0.62")]
    [InlineData(-2.4514, 3, false, "−2.45")]
    [InlineData(9.996, 3, false, "10.0")]
    [InlineData(0.00125, 3, false, "0.00125")]
    public void WrittenToSignificantFigures(double v, int figures, bool trim, string shown) =>
        Assert.Equal(shown, LabNumbers.Sig(v, figures, trim));

    [Fact]
    public void ThePrefixTableIsTheAPSheets()
    {
        var p = LabUnits.Prefixes;
        Assert.Equal(new[] { "giga", "mega", "kilo", "deci", "centi", "milli", "micro", "nano", "pico" }, p.Select(x => x.Name));
        Assert.Equal(new[] { 9, 6, 3, -1, -2, -3, -6, -9, -12 }, p.Select(x => x.Power));
        Assert.Equal(1e-3, LabUnits.Find(LabDim.Volume, "mL").Factor);
        Assert.Equal(295, LabUnits.Find(LabDim.Temperature, "K").FromBase(22));
        Assert.Equal("µM", LabUnits.Nice(LabDim.Concentration, 8.04e-5)!.Symbol);
    }

    // ---- §5.5's test vectors ----

    [Fact]
    public void Solutions()
    {
        var m = Run("molarity", "mass");
        Assert.Equal(7.305, m.Raw, 9);
        Assert.Equal("7.31", m.Value);
        Assert.Equal("g", m.Unit);
        Assert.Contains("0.25 L", m.Substitution);
        Assert.Contains(m.Steps, s => s == "V = 250 mL = 0.25 L");

        var d = Run("dilution", "V1");
        Assert.Equal(5.0, d.Raw, 9);
        Assert.Equal("5.0", d.Value);
        Assert.Contains(d.Notes, n => n.Contains("add 45 mL of solvent"));

        var s = Run("serial");
        Assert.StartsWith("10⁻⁴ (1:10,000)", s.Rows[0].Value);
        var table = Assert.IsType<LabTable>(s.Extra);
        Assert.Equal(5, table.Rows.Count);
        Assert.Equal("1:1,000", table.Rows[3][1]);

        var p = Run("plate");
        Assert.Equal(1.5e9, p.Raw, 0);
        Assert.Equal("1.5 × 10⁹", p.Value);
        // a dilution typed as 1,000,000 is read as 1:1,000,000
        Assert.Equal(1.5e9, Run("plate", null, ("dil", "1000000")).Raw, 0);

        var pc = Run("percent", "wv");
        Assert.Equal(2.0, pc.Raw, 9);
        Assert.Equal("% w/v = 2.0%", pc.Sentence);

        var u = Run("convert", "conc");
        Assert.Equal("0.25", u.Value);
        Assert.Equal("mM", u.Unit);
    }

    [Fact]
    public void Microscopy()
    {
        Assert.Equal("400", Run("totalMag").Value);
        var fov = Run("fov");
        Assert.Equal(0.45, fov.Raw, 9);
        Assert.Equal("450 µm", fov.Alt);
        Assert.Equal(90, Run("specimen").Raw, 9);
        var m = Run("magnification", "mag");
        Assert.Equal(3000, m.Raw, 6);
        Assert.Equal("3,000", m.Value);
        Assert.Equal(15e-6, Run("magnification", "actual").RawBase, 12);
        Assert.Equal(45e-3, Run("magnification", "image").RawBase, 12);
        var bar = Run("scaleBar");
        Assert.Equal(30, Assert.IsType<LabScaleBar>(bar.Extra).Millimetres, 6);
    }

    [Fact]
    public void Cells()
    {
        Assert.Equal(3.0, Run("sav", "sphere", ("r", "1")).Raw, 9);
        Assert.Equal(3.0, Run("sav", "cube", ("s", "2")).Raw, 9);
        Assert.Equal(22.0 / 6, Run("sav", "rect", ("l", "3"), ("w", "2"), ("h", "1")).Raw, 9);
        Assert.Equal(3.0, Run("sav", "cylinder", ("r", "1"), ("h", "2")).Raw, 9);
        // SA:V halves when every length doubles
        Assert.Equal(1.5, Run("sav", "sphere", ("r", "2")).Raw, 9);
        Assert.IsType<LabCurve>(Run("sav", "sphere").Extra);

        var w = Run("water");
        Assert.Equal(-2.45145, w.Raw, 9);
        Assert.Equal("−2.45 bar", w.Rows[0].Value);
        Assert.Equal(-4.9029, Run("water", null, ("i", "2")).Raw, 9);
        // a pressure potential adds to it
        Assert.Equal(-1.45145, Run("water", null, ("P", "1.00")).Raw, 9);

        var h = Run("hemocytometer", null, ("counts", "42"));
        Assert.Equal(8.4e5, h.Raw, 6);
        Assert.Equal("8.4 × 10⁵", h.Value);
    }

    [Fact]
    public void Statistics()
    {
        var s = LabCalcs.Stats(new[] { 12.1, 13.4, 11.8, 12.9, 13.0 });
        Assert.Equal(12.64, s.Mean, 9);
        Assert.Equal(0.6656, s.Sd, 4);
        Assert.Equal(0.2977, s.Se, 4);
        Assert.Equal(12.9, s.Median, 9);
        Assert.Empty(s.Modes);
        var r = Run("stats");
        string Row(string n) => r.Rows.First(x => x.Name == n).Value;
        Assert.Equal("12.64", Row("Mean x̄"));
        Assert.Equal("0.666", Row("Standard deviation s"));
        Assert.Equal("0.298", Row("Standard error SE"));
        Assert.Equal("12.04 to 13.24", Row("x̄ ± 2 SE"));
        // two groups: their error bars and whether they overlap
        var two = Run("stats", null, ("g2", "15.1, 15.4, 14.9, 15.6"));
        Assert.Equal(2, Assert.IsType<List<LabBar>>(two.Extra).Count);
        Assert.Contains(two.Notes, n => n.Contains("don’t overlap"));
        Assert.Equal(new[] { 2.0 }, LabCalcs.Stats(new[] { 1.0, 2, 2, 3 }).Modes);

        var chi = Run("chisq", "ratio");
        Assert.Equal(0.470, chi.Raw, 3);
        Assert.Contains("fail to reject", chi.Rows.Last().Value);
        Assert.Equal(chi.Raw, Run("chisq", "counts").Raw, 9);
        Assert.True(Run("chisq", "ratio", ("ratio", "3:1")).FieldErrors.ContainsKey("ratio"));
    }

    [Fact]
    public void Populations()
    {
        Assert.Equal(5.0, Run("rate").Raw, 9);
        Assert.Equal(50, Run("growth", "exponential").Raw, 9);
        Assert.Equal(45, Run("growth", "logistic").Raw, 9);
        Assert.IsType<LabCurve>(Run("growth", "logistic").Extra);
        var d = Run("simpson");
        Assert.Equal(0.62, d.Raw, 9);
        Assert.Equal("0.62", d.Value);
        Assert.Equal(0, Run("simpson", null, ("counts", "12")).Raw, 9);
    }

    [Fact]
    public void Molecules()
    {
        var b = Run("beer", "c");
        Assert.Equal(0.5 / 6220, b.Raw, 12);
        Assert.Equal("8.04 × 10⁻⁵", b.Value);
        Assert.Equal("80.4 µM", b.Alt);
        Assert.Equal(125, Run("a260", "ds").Raw, 9);
        Assert.Equal(100, Run("a260", "rna").Raw, 9);
        Assert.Equal(82.5, Run("a260", "ss").Raw, 9);
        var pcr = Run("pcr");
        Assert.Equal(1.073741824e11, pcr.Raw, 0);
        Assert.Equal("1.07 × 10¹¹", pcr.Value);
        var rcf = Run("centrifuge", "rcf");
        Assert.Equal(1006.2, rcf.Raw, 6);
        // (10 cm and 3,000 rpm have one figure each, so the answer has two: 1,006.2 is "1,000"; the spec's "1,006" is its value)
        Assert.Equal("1,000", rcf.Value);
        Assert.Equal(3000, Run("centrifuge", "rpm", ("rcf", "1006.2")).Raw, 6);
        Assert.Equal(6400, Run("bacteria").Raw, 6);
        Assert.Equal("6,400", Run("bacteria").Value);
        Assert.Equal("7.00", Run("ph", "pH").Value);
        Assert.Equal(1e-7, Run("ph", "H").RawBase, 15);
        Assert.Equal("4.30", Run("ph", "pH", ("H", "5.0e-5")).Value);
        var q = Run("q10");
        Assert.Equal(2.2, q.Raw, 9);
        Assert.Equal("2.2", q.Value);
    }

    [Fact]
    public void EveryCalculatorWorksInEveryMode()
    {
        Assert.Equal(26, LabCalcs.All.Count);
        Assert.Equal(new[] { 6, 5, 3, 2, 3, 7 }, new[] { "solutions", "microscopy", "cells", "stats", "ecology", "molecules" }.Select(p => LabCalcs.On(p).Count()));
        foreach (var c in LabCalcs.All)
            foreach (var m in c.Modes)
            {
                var r = c.Run(m.Id);
                Assert.True(r.Ok, $"{c.Id}/{m.Id}: {r.Problem} {string.Join(", ", r.FieldErrors.Values)}");
                Assert.True(r.Steps.Count > 0, $"{c.Id}/{m.Id} has no steps");
                Assert.All(m.Inputs, id => Assert.Contains(c.Fields, f => f.Id == id));
            }
    }

    [Fact]
    public void FieldsSayWhatIsWrong()
    {
        Assert.Equal("Volume must be more than 0.", Run("molarity", "mass", ("v", "-3")).FieldErrors["v"]);
        Assert.Contains("isn’t a number", Run("molarity", "mass", ("mw", "salt")).FieldErrors["mw"]);
        Assert.Contains("Type", Run("molarity", "mass", ("mw", "")).FieldErrors["mw"]);
        Assert.Contains("weaker", Run("dilution", "V1", ("C2", "2")).Problem);
        Assert.Contains("whole number", Run("pcr", null, ("cycles", "2.5")).FieldErrors["cycles"]);
        Assert.Contains("−273", Run("water", null, ("T", "-300")).FieldErrors["T"]);
        Assert.True(Run("rate", null, ("t2", "0")).FieldErrors.ContainsKey("t2"));
        Assert.True(Run("hemocytometer", null, ("counts", "40, 4.5")).FieldErrors.ContainsKey("counts"));
    }

    [Fact]
    public void ExtrasAsOnTheMac()
    {
        // ratios and fractions are read as exact factors
        Assert.Equal(0.1, LabNumbers.Parse("1:10")!.Value.Value, 12);
        Assert.True(LabNumbers.Parse("1/1000000")!.Value.Exact);
        Assert.Equal(1.5e9, Run("plate", null, ("dil", "1:1000000")).Raw, 0);
        // the solute menu's molar masses from atomic weights
        Assert.Equal("58.44", LabNumbers.Fixed(LabCalcs.MolarMass("NaCl")!.Value, 2));
        Assert.Equal("180.16", LabNumbers.Fixed(LabCalcs.MolarMass("C6H12O6")!.Value, 2));
        Assert.Null(LabCalcs.MolarMass("Xy2"));
        var mw = LabCalcs.Find("molarity")!.Field("mw").Presets!;
        Assert.Equal(9, mw.Count);
        Assert.Equal("58.44", mw[0].Text);
        Assert.Equal(4, LabCalcs.Find("totalMag")!.Field("objective").Presets!.Count);
        // PCR at less than 100% efficiency
        Assert.Equal(100 * Math.Pow(1.9, 30), Run("pcr", null, ("eff", "90")).Raw, 0);
        Assert.True(Run("pcr", null, ("eff", "120")).FieldErrors.ContainsKey("eff"));
        // the A260/A280 purity ratio, when A280 is given
        Assert.DoesNotContain(Run("a260", "ds").Rows, row => row.Name == "A₂₆₀/A₂₈₀");
        Assert.StartsWith("1.85", Run("a260", "ds", ("a280", "0.135")).Rows.First(row => row.Name == "A₂₆₀/A₂₈₀").Value);
        // Q₁₀ solved for the rate at the other temperature
        Assert.Equal(4.4, Run("q10", "r2", ("q", "2.2")).Raw, 9);
        // which way water moves
        Assert.Contains(Run("water", null, ("out", "0")).Notes, n => n.Contains("water moves in"));
        Assert.Contains(Run("water", null, ("out", "-5")).Notes, n => n.Contains("water moves out"));
        // lengths and amounts converted
        Assert.Equal("1,800", Run("convert", "length").Value);
        Assert.Equal("125", Run("convert", "amount").Value);
    }

    // ---- the state the tool keeps ----

    [Fact]
    public void TheModelKeepsAndCarriesTheAnswer()
    {
        BioLabModel.Reset("molarity");
        Assert.Equal("mass", BioLabModel.Mode("molarity"));
        Assert.Equal("7.31", BioLabModel.Result("molarity").Value);
        // solving for the concentration next: the mass found moves into the mass field
        BioLabModel.SetText("molarity", "m", "1");
        BioLabModel.SetMode("molarity", "moles");
        BioLabModel.SetMode("molarity", "mass");
        BioLabModel.SetMode("molarity", "molarity");
        Assert.Equal("7.31", BioLabModel.Text("molarity", "m"));
        Assert.Equal(0.5, BioLabModel.Result("molarity").Raw, 3);
        // units chosen, saved and read back
        BioLabModel.SetUnit("molarity", "c", "mM");
        Assert.Equal("mM", BioLabModel.Result("molarity").Unit);
        var saved = BioLabModel.Save("molarity");
        BioLabModel.Reset("molarity");
        Assert.Equal("mass", BioLabModel.Mode("molarity"));
        BioLabModel.Load("molarity", saved);
        Assert.Equal("molarity", BioLabModel.Mode("molarity"));
        Assert.Equal("mM", BioLabModel.Unit("molarity", "c"));
        BioLabModel.Load("molarity", "{not json");
        Assert.Equal("molarity", BioLabModel.Mode("molarity"));
        BioLabModel.Reset("molarity");
    }

    [Fact]
    public void TheFiguresSettingReachesEveryCalculator()
    {
        var seen = new List<string>();
        void Heard(string id) => seen.Add(id);
        BioLabModel.Changed += Heard;
        try
        {
            BioLabModel.Figures = 9;
            Assert.Equal(6, BioLabModel.Figures);
            Assert.Equal("", seen.Last());
            BioLabModel.Figures = 3;
        }
        finally { BioLabModel.Changed -= Heard; }
    }

    /// <summary>Cell Explorer's "Why are cells small?", in both apps' words and in the earlier ones.</summary>
    [Fact]
    public void CellExplorersCellArrivesInTheSurfaceAreaCalculator()
    {
        Dictionary<string, string> info = new();
        string? Take(string k) => info.Remove(k, out var v) ? v : null;
        try
        {
            BioLabModel.Reset("sav");
            info = new() { ["savShape"] = "sphere", ["savSize"] = "19.2", ["savUnit"] = "µm", ["from"] = "cell" };
            Assert.True(BioLabModel.TakeCells(Take));
            Assert.Empty(info);
            Assert.Equal("sphere", BioLabModel.Mode("sav"));
            Assert.Equal("9.6", BioLabModel.Text("sav", "r"));
            Assert.True(BioLabModel.StepsOpen("sav"));
            Assert.Equal("sav", BioLabModel.Focus);
            info = new() { ["savShape"] = "box", ["savSize"] = "44,26,26", ["savUnit"] = "µm" };
            Assert.True(BioLabModel.TakeCells(Take));
            Assert.Equal("rect", BioLabModel.Mode("sav"));
            Assert.Equal(new[] { "44", "26", "26" }, new[] { "l", "w", "h" }.Select(f => BioLabModel.Text("sav", f)));
            info = new() { ["savShape"] = "cylinder", ["savSize"] = "1,2" };
            Assert.True(BioLabModel.TakeCells(Take));
            Assert.Equal(("cylinder", "0.5", "2"), (BioLabModel.Mode("sav"), BioLabModel.Text("sav", "r"), BioLabModel.Text("sav", "h")));
            Assert.True(BioLabModel.Result("sav").Ok);
            // (the earlier words: calc=sav with the shape and its fields)
            info = new() { ["calc"] = "sav", ["mode"] = "sphere", ["r"] = "3" };
            Assert.True(BioLabModel.TakeCells(Take));
            Assert.Equal(("sphere", "3"), (BioLabModel.Mode("sav"), BioLabModel.Text("sav", "r")));
            // (another calculator handed over is left to the pin's hand-off)
            info = new() { ["calc"] = "molarity" };
            Assert.False(BioLabModel.TakeCells(Take));
        }
        finally
        {
            BioLabModel.Reset("sav");
            BioLabModel.Focus = null;
        }
    }
}

using System.Globalization;

namespace Simpl.Tools;

// Enzyme Lab's own model (spec §4.9, §5.7): the rate models — illustrative, not data — that the simulator's three linked
// graphs draw: Michaelis–Menten against substrate (with a competitive or noncompetitive inhibitor), a Q₁₀ rise against
// temperature until the enzyme unfolds (and, once past Tm + 2w, a "cooled back down" curve at a tenth: denaturation does
// not undo), a bell against pH, and the activation-energy diagram with and without the enzyme. The graphs are linked
// through the conditions: the rate against [S] uses the Vmax the temperature and pH leave (Vmax × f(T) × f(pH)), and each
// graph marks where the conditions are now. The numbers, words and starting settings are the Mac's
// (EnzymeLabModel.swift), so the two teach the same. The settings are kept here, in a static class, so they outlive the
// view being built again (a view subscribes to Changed on Loaded and lets go on Unloaded). Plain .NET, no WPF, so the
// unit tests reach it; its BioSelfCheck lines are EnzymeChecks below.

public enum EnzymeInhibitor { None, Competitive, Noncompetitive }

/// <summary>A temperature preset (§5.7): where the enzyme starts to unfold (Tm), how sharply (w), the peak §5.7 gives,
/// and a line about it.</summary>
public sealed record EnzymeTemperaturePreset(string Id, string Name, double Tm, double W, double StatedPeak, string Line);

/// <summary>A pH preset (§5.7): an enzyme, its optimum pH, and where it works.</summary>
public sealed record EnzymePhPreset(string Id, string Name, double Optimum, string Line);

/// <summary>What the active-site picture shows.</summary>
public enum EnzymeSite
{
    /// <summary>Substrate in the active site, the enzyme closed slightly round it (induced fit).</summary>
    Bound,
    /// <summary>A competitive inhibitor in the active site; the substrate kept out.</summary>
    Competitive,
    /// <summary>A noncompetitive inhibitor at another site; the active site's shape changed.</summary>
    Noncompetitive,
    /// <summary>Unfolded by heat.</summary>
    Denatured,
}

/// <summary>Enzyme Lab's models and settings, kept across its views.</summary>
public static class EnzymeLabModel
{
    // ---- the models (§5.7) ----

    /// <summary>The temperature term's Q₁₀.</summary>
    public const double Q10 = 2;

    /// <summary>The pH bell's width: v ∝ exp(−((pH − optimum) / 1.5)²).</summary>
    public const double PhWidth = 1.5;

    /// <summary>What is left of an enzyme's activity, at every temperature, once it has been heated past Tm + 2w.</summary>
    public const double AfterDenaturing = 0.1;

    /// <summary>The energy diagram's free energies, kJ/mol (illustrative): ΔG, and the barrier without and with the enzyme.</summary>
    public const double DeltaG = -30, EaUncatalysed = 75, EaCatalysed = 40;

    public static readonly IReadOnlyList<EnzymeTemperaturePreset> TemperaturePresets = new EnzymeTemperaturePreset[]
    {
        new("human", "Human enzyme", 41.0, 2.5, 37.1, "Works best near body temperature, 37 °C."),
        new("taq", "Taq polymerase", 80.0, 3.0, 76, "From a hot-spring bacterium, Thermus aquaticus: the polymerase in PCR."),
        new("fish", "Antarctic fish enzyme", 19, 2.5, 15, "From a fish of near-freezing seas: fast in the cold, unfolds above 20 °C."),
    };

    public static readonly IReadOnlyList<EnzymePhPreset> PhPresets = new EnzymePhPreset[]
    {
        new("pepsin", "Pepsin", 2.0, "Digests protein in the stomach's acid."),
        new("amylase", "Salivary amylase", 6.8, "Starts digesting starch in the mouth."),
        new("catalase", "Catalase", 7.0, "Breaks down hydrogen peroxide inside cells."),
        new("trypsin", "Trypsin", 8.0, "Digests protein in the small intestine."),
    };

    public static EnzymeTemperaturePreset TemperaturePreset(string? id) => TemperaturePresets.FirstOrDefault(p => p.Id == id) ?? TemperaturePresets[0];
    public static EnzymePhPreset PhPreset(string? id) => PhPresets.FirstOrDefault(p => p.Id == id) ?? PhPresets[1];

    /// <summary>Michaelis–Menten: v = Vmax·[S] / (Km + [S]).</summary>
    public static double Rate(double s, double vmax, double km) => s <= 0 ? 0 : vmax * s / (km + s);

    /// <summary>What an inhibitor leaves: competitive raises Km (Km·(1 + [I]/Ki)), Vmax unchanged; noncompetitive lowers
    /// Vmax (Vmax / (1 + [I]/Ki)), Km unchanged.</summary>
    public static (double Vmax, double Km) Apparent(double vmax, double km, EnzymeInhibitor inhibitor, double i, double ki)
    {
        var f = 1 + Math.Max(i, 0) / Math.Max(ki, 1e-9);
        return inhibitor switch
        {
            EnzymeInhibitor.Competitive => (vmax, km * f),
            EnzymeInhibitor.Noncompetitive => (vmax / f, km),
            _ => (vmax, km),
        };
    }

    /// <summary>The temperature term before normalising: Q₁₀^((T − 20)/10) / (1 + e^((T − Tm)/w)).</summary>
    public static double TemperatureRaw(double t, double tm, double w) => Math.Pow(Q10, (t - 20) / 10) / (1 + Math.Exp((t - tm) / w));

    /// <summary>
    /// Where the temperature curve peaks: d/dT ln v = ln Q₁₀/10 − σ/w = 0, with σ the logistic term, so σ = w·ln Q₁₀/10
    /// and T = Tm + w·ln(σ / (1 − σ)) — 37.1 °C for the human enzyme, 76 °C for Taq, 15 °C for the fish's (§5.7).
    /// </summary>
    public static double PeakTemperature(EnzymeTemperaturePreset p)
    {
        var sigma = p.W * Math.Log(Q10) / 10;
        return p.Tm + p.W * Math.Log(sigma / (1 - sigma));
    }

    /// <summary>The temperature term normalised to its peak = 1.</summary>
    public static double TemperatureFactor(double t, EnzymeTemperaturePreset p) => TemperatureRaw(t, p.Tm, p.W) / TemperatureRaw(PeakTemperature(p), p.Tm, p.W);

    /// <summary>The temperature past which the enzyme unfolds for good (Tm + 2w): it doesn't recover on cooling.</summary>
    public static double DenaturesAbove(EnzymeTemperaturePreset p) => p.Tm + 2 * p.W;

    /// <summary>The pH term: exp(−((pH − optimum)/1.5)²), 1 at the optimum.</summary>
    public static double PhFactor(double ph, double optimum)
    {
        var x = (ph - optimum) / PhWidth;
        return Math.Exp(-x * x);
    }

    /// <summary>
    /// The energy diagram (§5.7) along the reaction, x from 0 (the reactants, at 0) to 1 (the products, at ΔG), over a
    /// barrier of <paramref name="ea"/>: flat, up to the transition state at x = 0.5, down to the products, flat. The ends
    /// are the same with and without the enzyme: ΔG is unchanged; only the barrier is lower.
    /// </summary>
    public static double Energy(double x, double ea, double deltaG = DeltaG)
    {
        static double Smooth(double u)
        {
            var c = Math.Clamp(u, 0, 1);
            return c * c * (3 - 2 * c);
        }
        if (x <= 0.5) return ea * Smooth((x - 0.15) / 0.35);
        return deltaG + (ea - deltaG) * (1 - Smooth((x - 0.5) / 0.35));
    }

    /// <summary>The energy diagram's curve, at 101 points.</summary>
    public static IReadOnlyList<(double X, double G)> EnergyProfile(double ea) => Enumerable.Range(0, 101).Select(i => (i / 100.0, Energy(i / 100.0, ea))).ToList();

    /// <summary>The energy diagram without the enzyme.</summary>
    public static IReadOnlyList<(double X, double G)> Uncatalysed { get; } = EnergyProfile(EaUncatalysed);

    /// <summary>The energy diagram with the enzyme.</summary>
    public static IReadOnlyList<(double X, double G)> Catalysed { get; } = EnergyProfile(EaCatalysed);

    /// <summary>The common mix-ups the card points out (ESICI), as the Mac says them.</summary>
    public static readonly IReadOnlyList<(string Title, string Text)> MixUps = new (string, string)[]
    {
        ("Enzymes are not used up", "The same enzyme molecule binds substrate, releases product and is ready again, thousands of times a second."),
        ("They lower the activation energy", "An enzyme does not add energy or change ΔG; it gives the reaction an easier path over a lower barrier."),
        ("Competitive: more substrate wins", "The inhibitor competes for the active site, so enough substrate crowds it out: Km goes up, Vmax stays the same."),
        ("Noncompetitive: more substrate doesn't help", "The inhibitor binds elsewhere and changes the active site's shape: Vmax goes down, Km stays the same."),
        ("Heat unfolds the enzyme for good", "Above its optimum the enzyme denatures: its shape, and its active site, are lost. Cooling it down does not fold it back."),
    };

    /// <summary>The active-site picture's words.</summary>
    public static string SiteText(EnzymeSite s) => s switch
    {
        EnzymeSite.Bound => "Substrate fits the active site, and the enzyme closes slightly around it: induced fit.",
        EnzymeSite.Competitive => "A competitive inhibitor sits in the active site, so substrate can't bind until it leaves.",
        EnzymeSite.Noncompetitive => "A noncompetitive inhibitor binds at another site and changes the active site's shape, so substrate no longer fits.",
        _ => "Heated past its optimum, the enzyme has unfolded: the active site is gone, and cooling won't bring it back.",
    };

    // ---- the simulator's settings (the Mac's to start with) ----

    /// <summary>The settings changed: the graphs, the picture and the facts draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();

    public const double SubstrateMax = 100, TemperatureMax = 100;

    /// <summary>Substrate concentration, mM (illustrative).</summary>
    public static double Substrate { get; private set; } = 20;
    /// <summary>Vmax, µmol/min (illustrative).</summary>
    public static double Vmax { get; private set; } = 100;
    /// <summary>Km, mM.</summary>
    public static double Km { get; private set; } = 5;
    public static EnzymeInhibitor Inhibitor { get; private set; } = EnzymeInhibitor.None;
    /// <summary>Inhibitor concentration, mM.</summary>
    public static double InhibitorConcentration { get; private set; } = 10;
    /// <summary>Ki, mM.</summary>
    public static double Ki { get; private set; } = 5;
    public static string TemperatureId { get; private set; } = "human";
    /// <summary>°C.</summary>
    public static double Temperature { get; private set; } = 37;
    /// <summary>Heated past Tm + 2w since it was fresh: a tenth of its activity, whatever the temperature now.</summary>
    public static bool Denatured { get; private set; }
    public static string PhId { get; private set; } = "amylase";
    public static double Ph { get; private set; } = 7.0;

    public static EnzymeTemperaturePreset Preset => TemperaturePreset(TemperatureId);
    public static EnzymePhPreset PhEnzyme => PhPreset(PhId);

    public static void SetSubstrate(double s) { Substrate = Math.Clamp(s, 0, SubstrateMax); Tell(); }
    public static void SetVmax(double v) { Vmax = Math.Clamp(v, 10, 200); Tell(); }
    public static void SetKm(double k) { Km = Math.Clamp(k, 0.5, 20); Tell(); }
    public static void SetInhibitor(EnzymeInhibitor i) { if (Inhibitor == i) return; Inhibitor = i; Tell(); }
    public static void SetInhibitorConcentration(double i) { InhibitorConcentration = Math.Clamp(i, 0, 20); Tell(); }
    public static void SetKi(double k) { Ki = Math.Clamp(k, 0.5, 20); Tell(); }

    /// <summary>The temperature set; heating past Tm + 2w denatures the enzyme, for good.</summary>
    public static void SetTemperature(double t)
    {
        Temperature = Math.Clamp(t, 0, TemperatureMax);
        if (Temperature > DenaturesAbove(Preset)) Denatured = true;
        Tell();
    }

    /// <summary>Another enzyme's temperature curve: a fresh sample (which unfolds at once if it is already too hot).</summary>
    public static void SetTemperaturePreset(string id)
    {
        TemperatureId = TemperaturePreset(id).Id;
        Denatured = Temperature > DenaturesAbove(Preset);
        Tell();
    }

    /// <summary>A fresh enzyme (the denatured one thrown away), at its optimum if it was too hot.</summary>
    public static void Fresh()
    {
        Denatured = false;
        if (Temperature > DenaturesAbove(Preset)) Temperature = Math.Round(PeakTemperature(Preset));
        Tell();
    }

    public static void SetPhPreset(string id)
    {
        PhId = PhPreset(id).Id;
        Tell();
    }

    public static void SetPh(double ph) { Ph = Math.Clamp(ph, 0, 14); Tell(); }

    /// <summary>Every setting back as it started.</summary>
    public static void Reset()
    {
        (Substrate, Vmax, Km, Inhibitor, InhibitorConcentration, Ki) = (20, 100, 5, EnzymeInhibitor.None, 10, 5);
        (TemperatureId, Temperature, Denatured, PhId, Ph) = ("human", 37, false, "amylase", 7.0);
        Tell();
    }

    // ---- what the settings give ----

    public static (double Vmax, double Km) ApparentNow => Apparent(Vmax, Km, Inhibitor, InhibitorConcentration, Ki);
    public static double KmNow => ApparentNow.Km;

    /// <summary>The temperature term now (a tenth of it once denatured).</summary>
    public static double TemperatureNow => TemperatureFactor(Temperature, Preset) * (Denatured ? AfterDenaturing : 1);

    public static double PhNow => PhFactor(Ph, PhEnzyme.Optimum);

    /// <summary>The Vmax the inhibitor, the temperature and the pH leave.</summary>
    public static double VmaxNow => ApparentNow.Vmax * TemperatureNow * PhNow;

    /// <summary>The rate at the [S] set.</summary>
    public static double RateNow => Rate(Substrate, VmaxNow, KmNow);

    /// <summary>The rate against [S] (0 to the axis's end, dense near 0 where it climbs fastest), with the inhibitor or
    /// without it — both at the temperature and pH set.</summary>
    public static IReadOnlyList<(double X, double Y)> RateCurve(bool withInhibitor = true)
    {
        var vmax = (withInhibitor ? ApparentNow.Vmax : Vmax) * TemperatureNow * PhNow;
        var km = withInhibitor ? KmNow : Km;
        return Enumerable.Range(0, 20).Select(i => i * 0.25).Concat(Enumerable.Range(5, 96).Select(i => (double)i)).Select(s => (s, Rate(s, vmax, km))).ToList();
    }

    /// <summary>A preset's temperature curve (its peak 1), every 1 °C — at a tenth for "cooled back down".</summary>
    public static IReadOnlyList<(double X, double Y)> TemperatureCurve(EnzymeTemperaturePreset p, bool cooled = false) =>
        Enumerable.Range(0, 101).Select(i => ((double)i, TemperatureFactor(i, p) * (cooled ? AfterDenaturing : 1))).ToList();

    /// <summary>A preset's pH curve, every 0.2.</summary>
    public static IReadOnlyList<(double X, double Y)> PhCurve(EnzymePhPreset p) =>
        Enumerable.Range(0, 71).Select(i => (i / 5.0, PhFactor(i / 5.0, p.Optimum))).ToList();

    /// <summary>What the active-site picture shows now.</summary>
    public static EnzymeSite Site => Denatured || Temperature > DenaturesAbove(Preset) ? EnzymeSite.Denatured
        : Inhibitor == EnzymeInhibitor.Competitive && InhibitorConcentration > 0 ? EnzymeSite.Competitive
        : Inhibitor == EnzymeInhibitor.Noncompetitive && InhibitorConcentration > 0 ? EnzymeSite.Noncompetitive
        : EnzymeSite.Bound;

    /// <summary>How far the pH is from the enzyme's best, 0 (at it) toward 1.</summary>
    public static double PhStrain => 1 - PhNow;

    /// <summary>How far heat past the optimum has begun to unfold it, 0 (at or below the optimum) to 1.</summary>
    public static double HeatStrain => Temperature > PeakTemperature(Preset) ? 1 - TemperatureFactor(Temperature, Preset) : 0;

    /// <summary>How much the picture bends the active site, 0–1: the worse of the two.</summary>
    public static double Strain => Math.Max(PhStrain, HeatStrain);

    /// <summary>Why the active site's outline wavers, if it does.</summary>
    public static string? StrainNote =>
        HeatStrain > 0.3 ? $"Above its optimum the enzyme starts to unfold, and the active site loses its shape. Past {DenaturesAbove(Preset).ToString("0.#", CultureInfo.InvariantCulture)} °C it is denatured for good."
        : PhStrain > 0.5 ? "Far from its best pH, the active site's charges change and it loses its shape: the rate drops."
        : null;

    public static string F(double v, int decimals) => v.ToString("F" + decimals, CultureInfo.InvariantCulture);
}

public static partial class BioSelfCheck
{
    /// <summary>Enzyme Lab's own bio-check lines, against §5.7 (as the Mac's are, enzyme.*): Michaelis–Menten and its
    /// inhibitors, the three temperature presets' peaks, denaturation, the pH presets, the energy diagram and the linking.</summary>
    static partial void EnzymeChecks(BioCheckReport r)
    {
        string F(string format, params object[] a) => string.Format(CultureInfo.InvariantCulture, format, a);
        // Michaelis–Menten: half Vmax at [S] = Km
        r.Near("enzyme.michaelis-menten", EnzymeLabModel.Rate(5, 100, 5), 50, 1e-9, 3);
        // competitive: Km × (1 + [I]/Ki), Vmax unchanged, so enough substrate overcomes it
        var comp = EnzymeLabModel.Apparent(100, 5, EnzymeInhibitor.Competitive, 10, 5);
        var overcome = EnzymeLabModel.Rate(1e7, comp.Vmax, comp.Km) / EnzymeLabModel.Rate(1e7, 100, 5);
        r.Check("enzyme.competitive", F("Km={0:F1},Vmax={1:F1},high[S]={2:F4}", comp.Km, comp.Vmax, overcome), Math.Abs(comp.Km - 15) < 1e-9 && comp.Vmax == 100 && overcome > 0.9999);
        // noncompetitive: Vmax / (1 + [I]/Ki), Km unchanged, and substrate does not overcome it
        var non = EnzymeLabModel.Apparent(100, 5, EnzymeInhibitor.Noncompetitive, 10, 5);
        var stays = EnzymeLabModel.Rate(1e7, non.Vmax, non.Km) / EnzymeLabModel.Rate(1e7, 100, 5);
        r.Check("enzyme.noncompetitive", F("Km={0:F1},Vmax={1:F2},high[S]={2:F4}", non.Km, non.Vmax, stays), non.Km == 5 && Math.Abs(non.Vmax - 100.0 / 3) < 1e-9 && Math.Abs(stays - 1.0 / 3) < 1e-4);
        // the temperature optima §5.7 gives: 37.1, 76 and 15 °C; the worked-out peak agrees with a search of the curve
        var peaks = EnzymeLabModel.TemperaturePresets.Select(EnzymeLabModel.PeakTemperature).ToList();
        r.Check("enzyme.temperature-peaks", string.Join(",", peaks.Select(t => t.ToString("F2", CultureInfo.InvariantCulture))),
            Math.Abs(peaks[0] - 37.1) < 0.05 && Math.Abs(peaks[1] - 76.0) < 0.05 && Math.Abs(peaks[2] - 15.0) < 0.15
            && EnzymeLabModel.TemperaturePresets.Zip(peaks).All(z => Math.Abs(z.Second - z.First.StatedPeak) <= 0.1));
        var searched = new List<double>();
        foreach (var p in EnzymeLabModel.TemperaturePresets)
        {
            double lo = 0, hi = 100;
            for (var k = 0; k < 200; k++)
            {
                double a = lo + (hi - lo) / 3, b = hi - (hi - lo) / 3;
                if (EnzymeLabModel.TemperatureRaw(a, p.Tm, p.W) < EnzymeLabModel.TemperatureRaw(b, p.Tm, p.W)) lo = a; else hi = b;
            }
            searched.Add(Math.Abs((lo + hi) / 2 - EnzymeLabModel.PeakTemperature(p)));
        }
        r.Check("enzyme.peak-search", searched.Max().ToString("F5", CultureInfo.InvariantCulture), searched.Max() < 0.001);
        var human = EnzymeLabModel.TemperaturePresets[0];
        var at = EnzymeLabModel.TemperatureFactor(EnzymeLabModel.PeakTemperature(human), human);
        r.Check("enzyme.normalised", at.ToString("F6", CultureInfo.InvariantCulture), Math.Abs(at - 1) < 1e-12 && EnzymeLabModel.TemperatureFactor(25, human) < 1 && EnzymeLabModel.TemperatureFactor(45, human) < 1);
        // denaturation is for good: past Tm + 2w = 46 °C, cooled back to 37 °C it keeps a tenth (the models alone, so the
        // settings a student has made are left as they are)
        var fresh = EnzymeLabModel.Rate(20, 100 * EnzymeLabModel.TemperatureFactor(37, human) * EnzymeLabModel.PhFactor(7, 6.8), 5);
        var cooled = EnzymeLabModel.Rate(20, 100 * EnzymeLabModel.TemperatureFactor(37, human) * EnzymeLabModel.AfterDenaturing * EnzymeLabModel.PhFactor(7, 6.8), 5);
        r.Check("enzyme.denaturation", F("at={0:F0},kept={1:F3}", EnzymeLabModel.DenaturesAbove(human), cooled / fresh),
            EnzymeLabModel.DenaturesAbove(human) == 46 && 47 > EnzymeLabModel.DenaturesAbove(human) && Math.Abs(cooled / fresh - EnzymeLabModel.AfterDenaturing) < 1e-9);
        // pH: 1 at the optimum, e⁻¹ at 1.5 either side; the presets' optima
        r.Check("enzyme.pH", F("{0:F4},{1:F4}", EnzymeLabModel.PhFactor(2, 2), EnzymeLabModel.PhFactor(9.5, 8)),
            EnzymeLabModel.PhFactor(2, 2) == 1 && Math.Abs(EnzymeLabModel.PhFactor(9.5, 8) - Math.Exp(-1)) < 1e-12
            && EnzymeLabModel.PhPresets.Select(p => p.Optimum).SequenceEqual(new[] { 2.0, 6.8, 7.0, 8.0 }));
        // the energy diagram: reactants at 0, products at ΔG = −30 both ways, barriers 75 and 40
        var un = EnzymeLabModel.Uncatalysed;
        var cat = EnzymeLabModel.Catalysed;
        r.Check("enzyme.energy", F("{0:F0},{1:F0},{2:F0},{3:F0}", un.Max(p => p.G), cat.Max(p => p.G), un[^1].G, cat[^1].G),
            un[0].G == 0 && cat[0].G == 0 && un[^1].G == -30 && cat[^1].G == -30 && Math.Abs(un.Max(p => p.G) - 75) < 1e-9 && Math.Abs(cat.Max(p => p.G) - 40) < 1e-9);
        // the linked graphs: the rate against [S] uses Vmax × f(T) × f(pH) (the starting settings: [S] 20 mM, Km 5 mM, 37 °C, pH 7.0, amylase)
        var expected = EnzymeLabModel.Rate(20, 100 * EnzymeLabModel.TemperatureFactor(37, human) * EnzymeLabModel.PhFactor(7, 6.8), 5);
        r.Near("enzyme.linked", fresh, expected, 1e-9, 4);
    }
}

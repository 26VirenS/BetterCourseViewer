using System.Globalization;
using System.Text.RegularExpressions;

namespace Simpl.Tools;

// Photosynthesis & Respiration's own model (spec §4.10, §5.8): where each stage happens and what goes in and out (§5.8's
// table, copied out of the specification by a script into LabSpecText), the step-throughs — one glucose through
// respiration with running totals of ATP, NADH, FADH₂ and CO₂ (with O₂, or fermenting to lactate or ethanol), and one
// turn of the light reactions and the Calvin cycle to one G3P — and "Follow the atoms": the O in O₂ back to water, the C
// in glucose from CO₂ and back to CO₂. The steps, their words and counts are the Mac's (EnergyPathwaysModel.swift), so the
// tool teaches the same on both. What the map shows and where each step-through is are kept here, in a static class, so
// they outlive the view being built again (a view subscribes to Changed on Loaded and lets go on Unloaded). Plain .NET,
// no WPF, so the unit tests reach it; its BioSelfCheck lines are EnergyChecks below. Words marked **so** are bold where
// they are shown.

/// <summary>A stage (a row of §5.8): its id, its name, where it happens (§5.8's words), what goes in and out (per glucose
/// unless stated), notes; the part of the map it is drawn on (thylakoid, stroma, cytosol, matrix, inner); its process;
/// the common mix-up it clears up; whether it stops in the dark.</summary>
public sealed record EnergyStage(string Id, string Name, string Where, string In, string Out, string Notes, string Place, string Process,
    string? MixUp, bool NeedsLight);

/// <summary>Respiration with O₂, or fermentation without it (to lactate, or to ethanol and CO₂).</summary>
public enum EnergyFermentation { None, Lactate, Ethanol }

/// <summary>Which atom "Follow the atoms" follows.</summary>
public enum EnergyTrace { Oxygen, Carbon }

/// <summary>A count a step-through keeps (ATP, NADH …): its total so far (a range at oxidative phosphorylation's ATP) and
/// this step's change.</summary>
public sealed record EnergyCount(string Name, int Low, int High, int ChangeLow, int ChangeHigh)
{
    /// <summary>"4", "30–32", "−2".</summary>
    public string Text => Range(Low, High);

    /// <summary>"+2", "+26–28", "" if none.</summary>
    public string ChangeText => ChangeLow == 0 && ChangeHigh == 0 ? "" : (ChangeLow > 0 ? "+" : "") + Range(ChangeLow, ChangeHigh);

    private static string Range(int a, int b)
    {
        static string N(int x) => x < 0 ? "−" + -x : x.ToString(CultureInfo.InvariantCulture);
        return a == b ? N(a) : $"{N(a)}–{N(b)}";
    }
}

/// <summary>Molecules holding the carbons a step-through follows (2 pyruvate, 3 carbons each).</summary>
public sealed record EnergyCarbons(string Molecule, int Count, int CarbonsEach);

/// <summary>A step's place on the map and what it does: where (null: the whole cell, a summary), the stage it lights, what
/// goes in and comes out in it, the running totals at its end, and where the carbons are.</summary>
public sealed record EnergyStepInfo(string? Place, string? Stage, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs,
    IReadOnlyList<EnergyCount> Counts, IReadOnlyList<EnergyCarbons> Carbons)
{
    /// <summary>What the map's token says: the molecules the carbons are in, else the first thing made.</summary>
    public string Token => Carbons.Count > 0
        ? string.Join(" + ", Carbons.Take(2).Select(c => c.Count == 1 ? c.Molecule : $"{c.Count} {c.Molecule}"))
        : Outputs.FirstOrDefault()?.Replace("**", "") ?? "";

    public int CarbonTotal => Carbons.Sum(c => c.Count * c.CarbonsEach);
}

/// <summary>A step of "Follow the atoms": the molecule the atom is in (its formula with the atom in [ ]; for carbon, how
/// many carbons it has and which one is followed), the step's title and words, where, and what process moved it.</summary>
public sealed record EnergyTraceStep(string Molecule, string Formula, int Carbons, int Traced, string Title, string Text, string? Place, string Process);

/// <summary>Photosynthesis & Respiration's stages, step-throughs and settings, kept across its views.</summary>
public static class EnergyPathwaysModel
{
    // ---- the stages (§5.8) ----

    /// <summary>The stages' ids in §5.8's table order, where each is drawn, its process, its mix-up and whether it needs
    /// light (the mix-ups are the Mac's words).</summary>
    private static readonly (string Id, string Place, string Process, string? MixUp, bool Light)[] Ids =
    {
        ("light", "thylakoid", "photosynthesis", "The O₂ comes from splitting **water**, not from CO₂.", true),
        ("calvin", "stroma", "photosynthesis", "It is not \"the dark reactions\": it runs on the light reactions' ATP and NADPH, so it stops soon after dark.", true),
        ("glycolysis", "cytosol", "respiration", "Glycolysis happens in the cytosol, not the mitochondrion, and nets only **2** ATP.", false),
        ("pyruvate", "matrix", "respiration", null, false),
        ("krebs", "matrix", "respiration", "Most of the energy leaves the cycle in NADH and FADH₂, not in ATP.", false),
        ("oxphos", "inner", "respiration", "O₂ is needed only here, at the very end, as the last electron acceptor; it becomes water.", false),
        ("total", "", "respiration", null, false),
        ("fermentation", "cytosol", "fermentation", "Fermentation makes no extra ATP: it recycles NAD⁺ so glycolysis can keep going.", false),
    };

    /// <summary>§5.8's eight rows: light reactions, Calvin cycle, glycolysis, pyruvate oxidation, the citric acid cycle,
    /// oxidative phosphorylation, the total, fermentation.</summary>
    public static readonly IReadOnlyList<EnergyStage> Stages = LabSpecText.Stages.Select((row, i) =>
    {
        var c = row.Split('\t');
        var (id, place, process, mix, light) = Ids[i];
        return new EnergyStage(id, c[0], c[1], c[2], c[3], c[4], place, process, mix, light);
    }).ToList();

    public static EnergyStage Stage(string id) => Stages.First(s => s.Id == id);

    /// <summary>The seven stages on the map (all but the total), in §5.8's order.</summary>
    public static IReadOnlyList<EnergyStage> Pressable => Stages.Where(s => s.Place.Length > 0).ToList();

    /// <summary>§5.8's overall equations, and photosynthesis with the water written out on both sides (which shows where
    /// each O goes).</summary>
    public const string PhotosynthesisEquation = LabSpecText.Photosynthesis, RespirationEquation = LabSpecText.Respiration;
    public const string PhotosynthesisFull = "6 CO₂ + 12 H₂O + light → C₆H₁₂O₆ + 6 O₂ + 6 H₂O";

    /// <summary>"Plants respire too", in words.</summary>
    public const string RespireText = "Plants respire too: their mitochondria work day and night. In the dark the light reactions stop, and the Calvin cycle soon stops too, without their ATP and NADPH.";

    /// <summary>A place's name.</summary>
    public static string PlaceName(string? place) => place switch
    {
        "thylakoid" => "Thylakoid membranes",
        "stroma" => "Stroma",
        "cytosol" => "Cytosol",
        "matrix" => "Mitochondrial matrix",
        "inner" => "Inner mitochondrial membrane",
        _ => "The whole cell",
    };

    /// <summary>The organelle a place is in (the cytosol is in none).</summary>
    public static string Organelle(string? place) => place switch
    {
        "thylakoid" or "stroma" => "Chloroplast",
        "matrix" or "inner" => "Mitochondrion",
        _ => "Cytosol",
    };

    /// <summary>The common mix-ups (§7 items 27 and 42), as the card says them.</summary>
    public static readonly IReadOnlyList<(string Title, string Text)> MixUps = new (string, string)[]
    {
        ("O₂ comes from water", "The O₂ photosynthesis releases comes from splitting **water** in the light reactions, not from CO₂."),
        ("Plants respire too", "Plant cells have mitochondria as well as chloroplasts. They respire day and night; they photosynthesise only in light."),
        ("Glycolysis needs no O₂", "It happens in the cytosol and nets **2** ATP per glucose, with or without O₂."),
        ("About 30–32 ATP", "Current textbooks give about **30–32** ATP per glucose; older ones say 36–38."),
    };

    // ---- the step-throughs (the Mac's steps, word for word) ----

    private sealed record Row(string Title, string Text, string? Place, string? Stage, int Group, string[] Inputs, string[] Outputs,
        (string Name, int Low, int High)[] Change, EnergyCarbons[] Carbons);

    private static EnergyCarbons C(string molecule, int count, int each) => new(molecule, count, each);

    /// <summary>Steps built from their changes, the running totals added up as they go.</summary>
    private static (IReadOnlyList<BioStep> Steps, IReadOnlyList<EnergyStepInfo> Info) Build(string[] names, IEnumerable<Row> rows)
    {
        var low = names.ToDictionary(n => n, _ => 0);
        var high = names.ToDictionary(n => n, _ => 0);
        var steps = new List<BioStep>();
        var info = new List<EnergyStepInfo>();
        foreach (var row in rows)
        {
            foreach (var (n, l, h) in row.Change) { low[n] += l; high[n] += h; }
            var counts = names.Select(n =>
            {
                var c = row.Change.FirstOrDefault(x => x.Name == n);
                return new EnergyCount(n, low[n], high[n], c.Name == null ? 0 : c.Low, c.Name == null ? 0 : c.High);
            }).ToList();
            steps.Add(new BioStep(steps.Count, row.Title, row.Text.Replace("**", ""), row.Group));
            info.Add(new EnergyStepInfo(row.Place, row.Stage, row.Inputs, row.Outputs, counts, row.Carbons));
        }
        return (steps, info);
    }

    public static readonly string[] RespirationCounts = { "ATP", "NADH", "FADH₂", "CO₂" };

    /// <summary>One glucose through respiration: with O₂ to the end of the chain, or (without O₂) fermenting.</summary>
    public static (IReadOnlyList<BioStep> Steps, IReadOnlyList<EnergyStepInfo> Info) Respiration(EnergyFermentation fermenting = EnergyFermentation.None)
    {
        var glucose = new[] { C("glucose", 1, 6) };
        var rows = new List<Row>
        {
            new("One glucose", "A glucose molecule, six carbons, arrives in the cytosol. Nothing has been made from it yet.",
                "cytosol", null, 1, Array.Empty<string>(), new[] { "glucose" }, Array.Empty<(string, int, int)>(), glucose),
            new("Glycolysis: energy invested", "Two ATP add phosphate groups to glucose, which splits into two 3-carbon sugars (G3P). So far, ATP is spent.",
                "cytosol", "glycolysis", 1, new[] { "glucose", "2 ATP" }, new[] { "2 G3P", "2 ADP" }, new[] { ("ATP", -2, -2) }, new[] { C("G3P", 2, 3) }),
            new("Glycolysis: energy paid back", "Each G3P is oxidised to pyruvate: 2 NADH and 4 ATP in all, a net of 2 ATP. No O₂ is needed.",
                "cytosol", "glycolysis", 1, new[] { "2 G3P", "2 NAD⁺", "4 ADP + Pᵢ" }, new[] { "2 pyruvate", "2 NADH", "4 ATP" }, new[] { ("ATP", 4, 4), ("NADH", 2, 2) },
                new[] { C("pyruvate", 2, 3) }),
        };
        switch (fermenting)
        {
            case EnergyFermentation.Lactate:
                rows.Add(new("Fermentation: lactate", "Without O₂ the electron transport chain stops. Pyruvate takes the electrons from NADH and becomes lactate, so NAD⁺ is free for glycolysis again.",
                    "cytosol", "fermentation", 2, new[] { "2 pyruvate", "2 NADH" }, new[] { "2 lactate", "2 NAD⁺" }, new[] { ("NADH", -2, -2) }, new[] { C("lactate", 2, 3) }));
                rows.Add(new("Total without oxygen", "Only glycolysis's 2 ATP per glucose. Fermentation itself makes no ATP: it regenerates NAD⁺ so glycolysis can go on.",
                    null, "fermentation", 3, Array.Empty<string>(), new[] { "2 ATP", "2 lactate" }, Array.Empty<(string, int, int)>(), new[] { C("lactate", 2, 3) }));
                break;
            case EnergyFermentation.Ethanol:
                rows.Add(new("Fermentation: ethanol", "Without O₂ the electron transport chain stops. Each pyruvate loses a carbon as CO₂, then takes the electrons from NADH and becomes ethanol, freeing NAD⁺.",
                    "cytosol", "fermentation", 2, new[] { "2 pyruvate", "2 NADH" }, new[] { "2 ethanol", "2 CO₂", "2 NAD⁺" }, new[] { ("NADH", -2, -2), ("CO₂", 2, 2) },
                    new[] { C("ethanol", 2, 2), C("CO₂", 2, 1) }));
                rows.Add(new("Total without oxygen", "Only glycolysis's 2 ATP per glucose. Fermentation itself makes no ATP: it regenerates NAD⁺ so glycolysis can go on.",
                    null, "fermentation", 3, Array.Empty<string>(), new[] { "2 ATP", "2 ethanol", "2 CO₂" }, Array.Empty<(string, int, int)>(),
                    new[] { C("ethanol", 2, 2), C("CO₂", 2, 1) }));
                break;
            default:
            {
                var six = new[] { C("CO₂", 6, 1) };
                rows.Add(new("Pyruvate oxidation", "Each pyruvate enters the mitochondrial matrix, loses a carbon as CO₂ and joins coenzyme A: 2 acetyl-CoA, 2 CO₂ and 2 NADH.",
                    "matrix", "pyruvate", 2, new[] { "2 pyruvate", "2 NAD⁺", "2 CoA" }, new[] { "2 acetyl-CoA", "2 CO₂", "2 NADH" }, new[] { ("NADH", 2, 2), ("CO₂", 2, 2) },
                    new[] { C("acetyl-CoA", 2, 2), C("CO₂", 2, 1) }));
                rows.Add(new("Citric acid cycle, turn 1", "Acetyl-CoA joins oxaloacetate to make citrate. One turn releases 2 CO₂ and makes 3 NADH, 1 FADH₂ and 1 ATP.",
                    "matrix", "krebs", 3, new[] { "1 acetyl-CoA", "3 NAD⁺", "FAD" }, new[] { "2 CO₂", "3 NADH", "1 FADH₂", "1 ATP" },
                    new[] { ("NADH", 3, 3), ("FADH₂", 1, 1), ("ATP", 1, 1), ("CO₂", 2, 2) }, new[] { C("acetyl-CoA", 1, 2), C("CO₂", 4, 1) }));
                rows.Add(new("Citric acid cycle, turn 2", "The second acetyl-CoA goes round the same way. All six of glucose's carbons have now left as CO₂.",
                    "matrix", "krebs", 3, new[] { "1 acetyl-CoA", "3 NAD⁺", "FAD" }, new[] { "2 CO₂", "3 NADH", "1 FADH₂", "1 ATP" },
                    new[] { ("NADH", 3, 3), ("FADH₂", 1, 1), ("ATP", 1, 1), ("CO₂", 2, 2) }, six));
                rows.Add(new("Electron transport chain", "NADH and FADH₂ hand their electrons to the chain in the inner membrane, which pumps H⁺ into the intermembrane space. O₂ takes the electrons at the end and becomes H₂O.",
                    "inner", "oxphos", 4, new[] { "10 NADH", "2 FADH₂", "6 O₂" }, new[] { "10 NAD⁺", "2 FAD", "6 H₂O", "an H⁺ gradient" }, Array.Empty<(string, int, int)>(), six));
                rows.Add(new("ATP synthase", "H⁺ flows back into the matrix through ATP synthase, which turns and makes about 26–28 ATP: chemiosmosis.",
                    "inner", "oxphos", 4, new[] { "ADP + Pᵢ", "H⁺ gradient" }, new[] { "about 26–28 ATP" }, new[] { ("ATP", 26, 28) }, six));
                rows.Add(new("Total with oxygen", "About 30–32 ATP per glucose (older books say 36–38). Six carbons left as 6 CO₂, and 6 O₂ became 6 H₂O.",
                    null, null, 5, Array.Empty<string>(), new[] { "about 30–32 ATP", "6 CO₂", "6 H₂O" }, Array.Empty<(string, int, int)>(), six));
                break;
            }
        }
        return Build(RespirationCounts, rows);
    }

    public static readonly string[] PhotosynthesisCounts = { "H₂O split", "O₂ released", "ATP made", "NADPH made", "CO₂ fixed", "ATP used", "NADPH used", "G3P made" };

    /// <summary>One turn of the light reactions and the Calvin cycle, to one G3P.</summary>
    public static (IReadOnlyList<BioStep> Steps, IReadOnlyList<EnergyStepInfo> Info) Photosynthesis() => Build(PhotosynthesisCounts, new Row[]
    {
        new("Light reaches photosystem II", "In the thylakoid membrane, chlorophyll in photosystem II absorbs light, and its electrons are raised to a higher energy.",
            "thylakoid", "light", 1, new[] { "light" }, new[] { "excited electrons" }, Array.Empty<(string, int, int)>(), Array.Empty<EnergyCarbons>()),
        new("Water is split", "Photosystem II replaces its electrons by splitting water: 6 H₂O → 3 O₂ + 12 H⁺ + 12 e⁻. The O₂ comes from **water**, not CO₂.",
            "thylakoid", "light", 1, new[] { "6 H₂O" }, new[] { "3 O₂", "12 H⁺", "12 e⁻" }, new[] { ("H₂O split", 6, 6), ("O₂ released", 3, 3) }, Array.Empty<EnergyCarbons>()),
        new("Electron transport", "The electrons pass along a chain to photosystem I; their energy pumps H⁺ into the thylakoid space.",
            "thylakoid", "light", 1, new[] { "electrons" }, new[] { "an H⁺ gradient" }, Array.Empty<(string, int, int)>(), Array.Empty<EnergyCarbons>()),
        new("Photosystem I makes NADPH", "Light raises the electrons again at photosystem I, and they reduce NADP⁺ to NADPH.",
            "thylakoid", "light", 1, new[] { "6 NADP⁺", "light" }, new[] { "6 NADPH" }, new[] { ("NADPH made", 6, 6) }, Array.Empty<EnergyCarbons>()),
        new("ATP synthase makes ATP", "H⁺ flows back out to the stroma through ATP synthase, making ATP; some comes from cyclic electron flow around photosystem I.",
            "thylakoid", "light", 1, new[] { "9 ADP + Pᵢ" }, new[] { "9 ATP" }, new[] { ("ATP made", 9, 9) }, Array.Empty<EnergyCarbons>()),
        new("Carbon fixation", "In the stroma, rubisco joins 3 CO₂ to 3 RuBP (5 carbons each), giving 6 molecules of 3-PGA (3 carbons each).",
            "stroma", "calvin", 2, new[] { "3 CO₂", "3 RuBP" }, new[] { "6 3-PGA" }, new[] { ("CO₂ fixed", 3, 3) }, new[] { C("3-PGA", 6, 3) }),
        new("Reduction", "6 ATP and 6 NADPH from the light reactions turn the 6 3-PGA into 6 G3P, a 3-carbon sugar.",
            "stroma", "calvin", 2, new[] { "6 3-PGA", "6 ATP", "6 NADPH" }, new[] { "6 G3P", "6 ADP", "6 NADP⁺" }, new[] { ("ATP used", 6, 6), ("NADPH used", 6, 6) },
            new[] { C("G3P", 6, 3) }),
        new("One G3P leaves", "One G3P leaves the cycle: the sugar made. Two of them make one glucose.",
            "stroma", "calvin", 2, new[] { "6 G3P" }, new[] { "1 G3P out", "5 G3P stay" }, new[] { ("G3P made", 1, 1) }, new[] { C("G3P (out)", 1, 3), C("G3P", 5, 3) }),
        new("Regeneration", "3 more ATP turn the other 5 G3P back into 3 RuBP. One G3P cost 3 CO₂, 9 ATP and 6 NADPH; a glucose costs twice that.",
            "stroma", "calvin", 2, new[] { "5 G3P", "3 ATP" }, new[] { "3 RuBP", "3 ADP" }, new[] { ("ATP used", 3, 3) }, new[] { C("G3P (out)", 1, 3), C("RuBP", 3, 5) }),
    });

    /// <summary>"Follow the atoms": the O in O₂ back to water and on to water again; the C in glucose from CO₂ and back.</summary>
    public static IReadOnlyList<EnergyTraceStep> Trace(EnergyTrace t) => t == EnergyTrace.Oxygen
        ? new EnergyTraceStep[]
        {
            new("Water", "H₂[O]", 0, 0, "In water", "Water from the roots reaches the chloroplast's thylakoids. Follow its oxygen atom.", "thylakoid", "Taken up by the roots"),
            new("Oxygen", "[O]=O", 0, 0, "Water is split", "Photosystem II splits water: 2 H₂O → O₂ + 4 H⁺ + 4 e⁻. The atom is now in O₂: the O₂ plants release comes from **water**, not from CO₂.", "thylakoid", "Light reactions"),
            new("Oxygen", "[O]=O", 0, 0, "O₂ leaves the chloroplast", "It diffuses out through the cytosol: out of the leaf by the stomata, or into a mitochondrion of this cell or an animal's.", "cytosol", "Diffusion"),
            new("Water", "H₂[O]", 0, 0, "O₂ takes the electrons", "At the end of the electron transport chain, O₂ + 4 e⁻ + 4 H⁺ → 2 H₂O. The atom is back in water.", "inner", "Oxidative phosphorylation"),
        }
        : new EnergyTraceStep[]
        {
            new("Carbon dioxide", "[C]O₂", 1, 0, "In carbon dioxide", "CO₂ from the air enters the leaf through the stomata and reaches the stroma. Follow its carbon.", "stroma", "Diffusion"),
            new("3-PGA", "3-phosphoglycerate", 3, 0, "Fixed by rubisco", "Rubisco joins CO₂ to RuBP; the carbon becomes the carboxyl carbon (carbon 1) of a 3-PGA.", "stroma", "Calvin cycle"),
            new("G3P", "glyceraldehyde 3-phosphate", 3, 0, "Reduced to a sugar", "ATP and NADPH reduce 3-PGA to G3P, a 3-carbon sugar. The carbon is still carbon 1.", "stroma", "Calvin cycle"),
            new("Glucose", "C₆H₁₂O₆", 6, 3, "Built into glucose", "Two 3-carbon sugars join into glucose (kept as starch, or sent round the plant as sucrose). Our carbon is now glucose's carbon 4.", "cytosol", "Sugar made from G3P"),
            new("Pyruvate", "pyruvate", 3, 0, "Glycolysis splits glucose", "In the cytosol, of this cell or of one that ate it, glycolysis splits glucose into two pyruvate. Carbon 4 becomes a pyruvate's carboxyl carbon.", "cytosol", "Glycolysis"),
            new("Carbon dioxide", "[C]O₂", 1, 0, "Back to carbon dioxide", "In the matrix, pyruvate oxidation removes the carboxyl carbon as CO₂: our carbon, back where it began. The other carbons leave as CO₂ in the citric acid cycle.", "matrix", "Pyruvate oxidation"),
        };

    public static IReadOnlyList<BioStep> TraceSteps(EnergyTrace t) => Trace(t).Select((s, i) => new BioStep(i, s.Title, s.Text.Replace("**", ""))).ToList();

    /// <summary>The step-throughs' groups (number keys).</summary>
    public static IReadOnlyDictionary<int, string> RespirationGroups(EnergyFermentation f) => f == EnergyFermentation.None
        ? new Dictionary<int, string> { [1] = "Glycolysis", [2] = "Pyruvate oxidation", [3] = "Citric acid cycle", [4] = "Oxidative phosphorylation", [5] = "Total" }
        : new Dictionary<int, string> { [1] = "Glycolysis", [2] = "Fermentation", [3] = "Total" };

    public static readonly IReadOnlyDictionary<int, string> PhotosynthesisGroups = new Dictionary<int, string> { [1] = "Light reactions", [2] = "Calvin cycle" };

    // ---- what the tool shows ----

    /// <summary>Something the map shows changed (the stage pressed, the fermentation, the trace, day or night).</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();

    /// <summary>Respiration with O₂, or fermenting.</summary>
    public static EnergyFermentation Fermentation { get; private set; } = EnergyFermentation.None;
    /// <summary>The atom "Follow the atoms" follows.</summary>
    public static EnergyTrace Traced { get; private set; } = EnergyTrace.Oxygen;
    /// <summary>The stage pressed on the map page (its card shown).</summary>
    public static string Selected { get; private set; } = "light";
    /// <summary>"Plants respire too": which organelle works in light and in the dark.</summary>
    public static bool PlantsRespire { get; private set; }
    /// <summary>With "Plants respire too": night (the chloroplast idle, the mitochondrion still working).</summary>
    public static bool Dark { get; private set; }

    private static IReadOnlyList<EnergyStepInfo> respirationInfo = Respiration().Info;

    /// <summary>The respiration page's step-through (one glucose; kept here, so it outlives the view).</summary>
    public static BioStepController Respiring { get; } = new(Respiration().Steps);
    /// <summary>The photosynthesis page's (one G3P).</summary>
    public static BioStepController Photosynthesising { get; } = new(Photosynthesis().Steps);
    /// <summary>The atoms page's.</summary>
    public static BioStepController Tracing { get; } = new(TraceSteps(EnergyTrace.Oxygen));

    public static IReadOnlyList<EnergyStepInfo> RespirationInfo => respirationInfo;
    public static readonly IReadOnlyList<EnergyStepInfo> PhotosynthesisInfo = Photosynthesis().Info;
    public static IReadOnlyList<EnergyTraceStep> TraceNow => Trace(Traced);

    public static void SetFermentation(EnergyFermentation f)
    {
        if (f == Fermentation) return;
        Fermentation = f;
        var p = Respiration(f);
        respirationInfo = p.Info;
        Respiring.SetSteps(p.Steps);
        Tell();
    }

    public static void SetTrace(EnergyTrace t)
    {
        if (t == Traced) return;
        Traced = t;
        Tracing.SetSteps(TraceSteps(t));
        Tell();
    }

    public static void Select(string stage)
    {
        if (Selected == stage || Stages.All(s => s.Id != stage)) return;
        Selected = stage;
        Tell();
    }

    public static void SetPlantsRespire(bool on) { if (PlantsRespire == on) return; PlantsRespire = on; if (!on) Dark = false; Tell(); }
    public static void SetDark(bool on) { if (Dark == on) return; Dark = on; Tell(); }

    /// <summary>Whether a stage is at work now: in the dark ("Plants respire too") the chloroplast's stop; the
    /// mitochondrion's and the cytosol's go on day and night.</summary>
    public static bool Working(string stageId) => !(PlantsRespire && Dark && Stage(stageId).NeedsLight);

    // ---- reading numbers out of §5.8's table (the self-check adds them up) ----

    /// <summary>The count before a molecule's name in a cell of the table ("2 pyruvate, 2 NADH, 4 ATP" → NADH 2), or 0.</summary>
    public static int Count(string cell, string molecule)
    {
        var m = Regex.Match(cell, @"(\d+)\s+" + Regex.Escape(molecule) + @"(?![\w₂⁺])");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    /// <summary>A step's totals by name: (low, high).</summary>
    public static (int Low, int High) Total(EnergyStepInfo step, string name) => step.Counts.First(c => c.Name == name) is var c ? (c.Low, c.High) : (0, 0);
}

public static partial class BioSelfCheck
{
    /// <summary>Photosynthesis & Respiration's own bio-check lines: §5.8's table read and added up, and the step-throughs'
    /// running totals and carbons against it (the lines named as the Mac's are, energy.*).</summary>
    static partial void EnergyChecks(BioCheckReport r)
    {
        var s = EnergyPathwaysModel.Pressable;
        r.Check("energy.stages", string.Join(",", s.Select(x => x.Id)), s.Count == 7
            && s.Select(x => x.Place).SequenceEqual(new[] { "thylakoid", "stroma", "cytosol", "matrix", "matrix", "inner", "cytosol" })
            && EnergyPathwaysModel.Stages.Count == 8 && EnergyPathwaysModel.Stage("total").Out.StartsWith("about 30–32 ATP", StringComparison.Ordinal));
        var st = EnergyPathwaysModel.Stage;
        r.Check("energy.glycolysis", st("glycolysis").Out.Replace(", ", "; "), st("glycolysis").Out == "2 pyruvate, 2 NADH, 4 ATP (net **2**)");
        r.Check("energy.oxygen-from-water", st("light").Out.Split(',')[0], st("light").Out.StartsWith("O₂ (from **water**)", StringComparison.Ordinal) && st("light").In.StartsWith("H₂O", StringComparison.Ordinal));

        // §5.8 added up: the NADH and FADH₂ made are what oxidative phosphorylation takes in; the CO₂ is the overall
        // equation's 6; the ATP is glycolysis's net 2 + the cycle's 2 + 26–28 = the total's 30–32
        int I(Match m, int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
        var nadh = new[] { "glycolysis", "pyruvate", "krebs" }.Sum(id => EnergyPathwaysModel.Count(st(id).Out, "NADH"));
        var fadh = EnergyPathwaysModel.Count(st("krebs").Out, "FADH₂");
        var co2 = new[] { "pyruvate", "krebs" }.Sum(id => EnergyPathwaysModel.Count(st(id).Out, "CO₂"));
        var range = Regex.Match(st("oxphos").Out, @"(\d+)–(\d+)");
        var total = Regex.Match(st("total").Out, @"(\d+)–(\d+)");
        var net = I(Regex.Match(st("glycolysis").Out, @"net \**(\d+)"), 1);
        var krebsAtp = EnergyPathwaysModel.Count(st("krebs").Out, "ATP");
        r.Check("energy.table-adds-up", $"NADH{nadh},FADH2{fadh},CO2{co2},ATP{net + krebsAtp + I(range, 1)}-{net + krebsAtp + I(range, 2)}",
            nadh == EnergyPathwaysModel.Count(st("oxphos").In, "NADH") && fadh == EnergyPathwaysModel.Count(st("oxphos").In, "FADH₂")
            && co2 == EnergyPathwaysModel.Count(EnergyPathwaysModel.RespirationEquation, "CO₂")
            && net + krebsAtp + I(range, 1) == I(total, 1) && net + krebsAtp + I(range, 2) == I(total, 2));

        // one glucose with O₂: 30–32 ATP (4 of them before the chain), 10 NADH, 2 FADH₂, 6 CO₂ — §5.8's numbers
        var aerobic = EnergyPathwaysModel.Respiration().Info;
        var end = aerobic[^1];
        (int, int) T(EnergyStepInfo x, string n) => EnergyPathwaysModel.Total(x, n);
        var synthase = EnergyPathwaysModel.Respiration().Steps.First(x => x.Title == "ATP synthase").Index;
        r.Check("energy.respiration", $"ATP{T(end, "ATP").Item1}-{T(end, "ATP").Item2},NADH{T(end, "NADH").Item1},FADH2{T(end, "FADH₂").Item1},CO2{T(end, "CO₂").Item1}",
            T(end, "ATP") == (I(total, 1), I(total, 2)) && T(end, "NADH") == (nadh, nadh) && T(end, "FADH₂") == (fadh, fadh) && T(end, "CO₂") == (co2, co2)
            && T(aerobic[synthase - 1], "ATP") == (4, 4));
        r.Check("energy.glycolysis-net", $"ATP{T(aerobic[2], "ATP").Item1}", T(aerobic[2], "ATP") == (net, net) && T(aerobic[2], "NADH") == (2, 2));

        // without O₂: glycolysis's 2 ATP only, NAD⁺ regenerated; ethanol gives off 2 CO₂
        var lactate = EnergyPathwaysModel.Respiration(EnergyFermentation.Lactate).Info[^1];
        var ethanol = EnergyPathwaysModel.Respiration(EnergyFermentation.Ethanol).Info[^1];
        r.Check("energy.fermentation", $"ATP{T(lactate, "ATP").Item1},NADH{T(lactate, "NADH").Item1},CO2{T(ethanol, "CO₂").Item1}",
            T(lactate, "ATP") == (2, 2) && T(lactate, "NADH") == (0, 0) && T(ethanol, "ATP") == (2, 2) && T(ethanol, "CO₂") == (2, 2) && T(lactate, "CO₂") == (0, 0)
            && st("fermentation").Notes.Contains("2 ATP"));

        // every carbon of glucose accounted for at every step
        var all = new[] { EnergyFermentation.None, EnergyFermentation.Lactate, EnergyFermentation.Ethanol }.Select(f => EnergyPathwaysModel.Respiration(f)).ToList();
        var lost = all.SelectMany(p => p.Info.Zip(p.Steps)).Where(z => z.First.CarbonTotal != 6).Select(z => z.Second.Title).ToList();
        r.Check("energy.carbons", lost.Count == 0 ? "6-each-step" : string.Join(",", lost), lost.Count == 0);

        // one G3P: 3 CO₂, 9 ATP, 6 NADPH (the Calvin row's), and 3 O₂ from 6 H₂O; twice that per glucose; 18 carbons kept
        var photo = EnergyPathwaysModel.Photosynthesis().Info;
        var g = photo[^1];
        var calvinIn = st("calvin").In;
        var perGlucose = Regex.Match(calvinIn, @"\((\d+) CO₂, (\d+) ATP, (\d+) NADPH per glucose\)");
        r.Check("energy.calvin", $"CO2{T(g, "CO₂ fixed").Item1},ATP{T(g, "ATP used").Item1},NADPH{T(g, "NADPH used").Item1},O2{T(g, "O₂ released").Item1},G3P{T(g, "G3P made").Item1}",
            T(g, "CO₂ fixed").Item1 == EnergyPathwaysModel.Count(calvinIn, "CO₂") && T(g, "ATP used").Item1 == EnergyPathwaysModel.Count(calvinIn, "ATP")
            && T(g, "NADPH used").Item1 == EnergyPathwaysModel.Count(calvinIn, "NADPH") && T(g, "ATP made") == T(g, "ATP used") && T(g, "NADPH made") == T(g, "NADPH used")
            && T(g, "O₂ released") == (3, 3) && T(g, "H₂O split") == (6, 6) && T(g, "G3P made") == (1, 1)
            && perGlucose.Success && I(perGlucose, 1) == 2 * T(g, "CO₂ fixed").Item1 && I(perGlucose, 2) == 2 * T(g, "ATP used").Item1 && I(perGlucose, 3) == 2 * T(g, "NADPH used").Item1);
        var calvin = photo.Where(x => x.Carbons.Count > 0).ToList();
        r.Check("energy.calvin-carbons", $"{calvin.Count}", calvin.Count == 4 && calvin.All(x => x.CarbonTotal == 18));

        // follow the atoms: the O in O₂ comes from water and returns to water; the C goes from CO₂ to CO₂
        var o = EnergyPathwaysModel.Trace(EnergyTrace.Oxygen);
        var c = EnergyPathwaysModel.Trace(EnergyTrace.Carbon);
        r.Check("energy.trace-oxygen", string.Join(">", o.Select(x => x.Formula)), o[0].Formula == "H₂[O]" && o[1].Formula == "[O]=O" && o[1].Place == "thylakoid"
            && o[^1].Formula == "H₂[O]" && o[^1].Place == "inner");
        r.Check("energy.trace-carbon", string.Join(">", c.Select(x => x.Molecule)), c[0].Formula == "[C]O₂" && c[^1].Formula == "[C]O₂"
            && c.Any(x => x.Molecule == "Glucose" && x.Carbons == 6) && c.All(x => x.Traced < Math.Max(x.Carbons, 1)));

        // §3.6: titles of at most 40 characters
        var titles = all.SelectMany(p => p.Steps).Concat(EnergyPathwaysModel.Photosynthesis().Steps).Select(x => x.Title).Concat(o.Concat(c).Select(x => x.Title)).ToList();
        r.Check("energy.titles", $"{titles.Max(t => t.Length)}", titles.All(t => t.Length <= 40));
        r.Same("energy.equations", EnergyPathwaysModel.PhotosynthesisEquation + " | " + EnergyPathwaysModel.RespirationEquation,
            "6 CO₂ + 6 H₂O + light → C₆H₁₂O₆ + 6 O₂ | C₆H₁₂O₆ + 6 O₂ → 6 CO₂ + 6 H₂O + energy (ATP and heat)");
    }
}

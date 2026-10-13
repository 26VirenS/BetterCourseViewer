import Foundation

// Photosynthesis & Respiration's own model (spec §4.10, §5.8): where each stage happens and what goes in and out (the
// §5.8 table, word for word), the step-throughs — one glucose through respiration with running totals of ATP, NADH,
// FADH₂ and CO₂ (aerobic, or fermenting to lactate or ethanol), and one turn of the light reactions and the Calvin cycle
// to one G3P — and "Follow the atoms": the O in O₂ back to water, the C in glucose from CO₂ and back to CO₂. Foundation
// only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. Words marked **so** are bold where they are shown.

/// Where in a plant cell a stage happens (the map's regions).
enum EnergyPlace: String, CaseIterable, Codable {
    case thylakoid, stroma, cytosol, matrix, innerMembrane

    var name: String {
        switch self {
        case .thylakoid: return "Thylakoid membranes"
        case .stroma: return "Stroma"
        case .cytosol: return "Cytosol"
        case .matrix: return "Mitochondrial matrix"
        case .innerMembrane: return "Inner mitochondrial membrane"
        }
    }

    /// The organelle it is in (the cytosol is in none).
    var organelle: String {
        switch self {
        case .thylakoid, .stroma: return "Chloroplast"
        case .cytosol: return "Cytosol"
        case .matrix, .innerMembrane: return "Mitochondrion"
        }
    }
}

enum EnergyProcess: String {
    case photosynthesis, respiration, fermentation

    var title: String {
        switch self {
        case .photosynthesis: return "Photosynthesis"
        case .respiration: return "Cellular respiration"
        case .fermentation: return "Fermentation"
        }
    }
}

/// A stage of the map (a row of §5.8's table).
struct EnergyStage: Identifiable, Equatable {
    let id: String
    let name: String
    let process: EnergyProcess
    let place: EnergyPlace
    /// §5.8's "Where", as written.
    let whereText: String
    let inputs: [String]
    let outputs: [String]
    let notes: String
    /// The common mix-up it clears up, if one.
    let mixUp: String?
    /// The map's arrows it lights.
    let flows: [String]
    /// It stops in the dark.
    let needsLight: Bool
}

/// A count a step-through keeps (ATP, NADH …): its name and value, with the step's change.
struct EnergyCount: Equatable, Identifiable {
    let name: String
    /// The total so far (low and high: oxidative phosphorylation's ATP is a range).
    let low: Int
    let high: Int
    /// This step's change (0, 0 if none).
    let changeLow: Int
    let changeHigh: Int
    var id: String { name }

    /// "4", "30–32", "−2".
    var text: String { Self.range(low, high) }
    /// "+2", "+26–28", "" if none.
    var changeText: String {
        if changeLow == 0 && changeHigh == 0 { return "" }
        return (changeLow > 0 ? "+" : "") + Self.range(changeLow, changeHigh)
    }

    private static func range(_ a: Int, _ b: Int) -> String {
        func n(_ x: Int) -> String { x < 0 ? "−\(-x)" : "\(x)" }
        return a == b ? n(a) : "\(n(a))–\(n(b))"
    }
}

/// Molecules holding the carbons a step-through follows (2 pyruvate, 3 carbons each).
struct EnergyCarbons: Equatable {
    let molecule: String
    let count: Int
    let carbonsEach: Int
}

/// A step of a step-through, with the running totals at its end.
struct EnergyStep {
    let step: BioStep
    /// Where it happens (nil: the whole cell, for a summary).
    let place: EnergyPlace?
    /// The map stage it lights.
    let stage: String?
    /// What goes in and comes out in this step.
    let inputs: [String]
    let outputs: [String]
    let counts: [EnergyCount]
    let carbons: [EnergyCarbons]
}

/// A step of "Follow the atoms": the molecule the atom is in (its formula with the atom in [ ], and for carbon, the
/// carbon chain with the atom's place), where, and what happened.
struct EnergyTraceStep {
    let molecule: String
    let formula: String
    /// Carbons in the molecule (the chain drawn), 0 for the oxygen trace.
    let carbons: Int
    /// Which carbon is the one followed (0-based).
    let traced: Int
    let title: String
    let text: String
    let place: EnergyPlace?
    let process: String
}

enum EnergyTrace: String, CaseIterable {
    case oxygen, carbon

    var title: String { self == .oxygen ? "The O in O₂" : "The C in glucose" }
}

enum EnergyFermentation: String, CaseIterable {
    case lactate, ethanol

    var title: String { self == .lactate ? "Lactate (animals)" : "Ethanol (yeast)" }
}

enum EnergyPathways {
    /// §5.8's overall equations.
    static let photosynthesisEquation = "6 CO₂ + 6 H₂O + light → C₆H₁₂O₆ + 6 O₂"
    static let respirationEquation = "C₆H₁₂O₆ + 6 O₂ → 6 CO₂ + 6 H₂O + energy (ATP and heat)"
    /// The same with the water written out on both sides, which shows where each O goes.
    static let photosynthesisFull = "6 CO₂ + 12 H₂O + light → C₆H₁₂O₆ + 6 O₂ + 6 H₂O"
    /// §5.8's total row.
    static let respirationTotal = "about 30–32 ATP per glucose (older books: 36–38)"

    /// §5.8's table, row by row.
    static let stages: [EnergyStage] = [
        EnergyStage(id: "light", name: "Light reactions", process: .photosynthesis, place: .thylakoid,
                    whereText: "Thylakoid membranes (chloroplast)",
                    inputs: ["H₂O", "light", "NADP⁺", "ADP + Pᵢ"], outputs: ["O₂ (from **water**)", "NADPH", "ATP"],
                    notes: "Photosystem II, then I; an H⁺ gradient drives ATP synthase",
                    mixUp: "The O₂ comes from splitting **water**, not from CO₂.", flows: ["light", "water", "oxygen"], needsLight: true),
        EnergyStage(id: "calvin", name: "Calvin cycle", process: .photosynthesis, place: .stroma, whereText: "Stroma",
                    inputs: ["3 CO₂, 9 ATP, 6 NADPH per G3P (6 CO₂, 18 ATP, 12 NADPH per glucose)"], outputs: ["1 G3P (2 per glucose)", "ADP", "NADP⁺"],
                    notes: "Rubisco fixes CO₂",
                    mixUp: "It is not \"the dark reactions\": it runs on the light reactions' ATP and NADPH, so it stops soon after dark.",
                    flows: ["carbonDioxide", "sugar"], needsLight: true),
        EnergyStage(id: "glycolysis", name: "Glycolysis", process: .respiration, place: .cytosol, whereText: "Cytosol",
                    inputs: ["glucose", "2 NAD⁺", "2 ATP invested"], outputs: ["2 pyruvate", "2 NADH", "4 ATP (net **2**)"],
                    notes: "No O₂ needed",
                    mixUp: "Glycolysis happens in the cytosol, not the mitochondrion, and nets only **2** ATP.", flows: ["sugar", "pyruvate"], needsLight: false),
        EnergyStage(id: "pyruvate", name: "Pyruvate oxidation", process: .respiration, place: .matrix, whereText: "Mitochondrial matrix",
                    inputs: ["2 pyruvate", "2 NAD⁺", "2 CoA"], outputs: ["2 acetyl-CoA", "2 CO₂", "2 NADH"],
                    notes: "", mixUp: nil, flows: ["pyruvate", "carbonDioxide"], needsLight: false),
        EnergyStage(id: "krebs", name: "Citric acid (Krebs) cycle", process: .respiration, place: .matrix, whereText: "Matrix",
                    inputs: ["2 acetyl-CoA (2 turns)"], outputs: ["4 CO₂", "6 NADH", "2 FADH₂", "2 ATP (or GTP)"],
                    notes: "", mixUp: "Most of the energy leaves the cycle in NADH and FADH₂, not in ATP.", flows: ["carbonDioxide"], needsLight: false),
        EnergyStage(id: "oxphos", name: "Oxidative phosphorylation", process: .respiration, place: .innerMembrane,
                    whereText: "Inner mitochondrial membrane",
                    inputs: ["10 NADH", "2 FADH₂", "O₂ (final e⁻ acceptor → H₂O)"], outputs: ["about 26–28 ATP"],
                    notes: "Chemiosmosis via ATP synthase",
                    mixUp: "O₂ is needed only here, at the very end, as the last electron acceptor; it becomes water.",
                    flows: ["oxygen", "atp"], needsLight: false),
        EnergyStage(id: "fermentation", name: "Fermentation", process: .fermentation, place: .cytosol, whereText: "Cytosol",
                    inputs: ["pyruvate", "NADH"], outputs: ["lactate (animals, some bacteria) or ethanol + CO₂ (yeast)", "NAD⁺ regenerated"],
                    notes: "Total yield: only glycolysis's 2 ATP",
                    mixUp: "Fermentation makes no extra ATP: it recycles NAD⁺ so glycolysis can keep going.", flows: [], needsLight: false),
    ]

    static func stage(_ id: String?) -> EnergyStage? { stages.first { $0.id == id } }

    /// Plants respire too (the map's toggle): the mitochondrion works in light and dark, the chloroplast only in light.
    static let respireText = "Plants respire too: their mitochondria work day and night. In the dark the light reactions stop, and the Calvin cycle soon stops too, without their ATP and NADPH."

    // MARK: Step-throughs

    /// Steps built from per-step changes, the running totals added up as they go.
    private static func build(_ names: [String], _ rows: [(title: String, text: String, place: EnergyPlace?, stage: String?, group: Int,
                                                         inputs: [String], outputs: [String], change: [String: (Int, Int)], carbons: [EnergyCarbons])]) -> [EnergyStep] {
        var low = [String: Int](), high = [String: Int]()
        return rows.enumerated().map { i, row in
            for (k, c) in row.change {
                low[k, default: 0] += c.0
                high[k, default: 0] += c.1
            }
            let counts = names.map { n in
                EnergyCount(name: n, low: low[n] ?? 0, high: high[n] ?? 0, changeLow: row.change[n]?.0 ?? 0, changeHigh: row.change[n]?.1 ?? 0)
            }
            return EnergyStep(step: BioStep(i, row.title, row.text, group: row.group), place: row.place, stage: row.stage,
                              inputs: row.inputs, outputs: row.outputs, counts: counts, carbons: row.carbons)
        }
    }

    static let respirationCounts = ["ATP", "NADH", "FADH₂", "CO₂"]

    /// One glucose through respiration: with O₂ to the end of the chain, or (without O₂) fermenting.
    static func respiration(fermenting: EnergyFermentation? = nil) -> [EnergyStep] {
        let glucose = [EnergyCarbons(molecule: "glucose", count: 1, carbonsEach: 6)]
        var rows: [(title: String, text: String, place: EnergyPlace?, stage: String?, group: Int, inputs: [String], outputs: [String],
                    change: [String: (Int, Int)], carbons: [EnergyCarbons])] = [
            ("One glucose", "A glucose molecule, six carbons, arrives in the cytosol. Nothing has been made from it yet.",
             .cytosol, nil, 1, [], ["glucose"], [:], glucose),
            ("Glycolysis: energy invested", "Two ATP add phosphate groups to glucose, which splits into two 3-carbon sugars (G3P). So far, ATP is spent.",
             .cytosol, "glycolysis", 1, ["glucose", "2 ATP"], ["2 G3P", "2 ADP"], ["ATP": (-2, -2)],
             [EnergyCarbons(molecule: "G3P", count: 2, carbonsEach: 3)]),
            ("Glycolysis: energy paid back", "Each G3P is oxidised to pyruvate: 2 NADH and 4 ATP in all, a net of 2 ATP. No O₂ is needed.",
             .cytosol, "glycolysis", 1, ["2 G3P", "2 NAD⁺", "4 ADP + Pᵢ"], ["2 pyruvate", "2 NADH", "4 ATP"], ["ATP": (4, 4), "NADH": (2, 2)],
             [EnergyCarbons(molecule: "pyruvate", count: 2, carbonsEach: 3)]),
        ]
        switch fermenting {
        case .lactate?:
            rows.append(("Fermentation: lactate", "Without O₂ the electron transport chain stops. Pyruvate takes the electrons from NADH and becomes lactate, so NAD⁺ is free for glycolysis again.",
                         .cytosol, "fermentation", 2, ["2 pyruvate", "2 NADH"], ["2 lactate", "2 NAD⁺"], ["NADH": (-2, -2)],
                         [EnergyCarbons(molecule: "lactate", count: 2, carbonsEach: 3)]))
            rows.append(("Total without oxygen", "Only glycolysis's 2 ATP per glucose. Fermentation itself makes no ATP: it regenerates NAD⁺ so glycolysis can go on.",
                         nil, "fermentation", 3, [], ["2 ATP", "2 lactate"], [:], [EnergyCarbons(molecule: "lactate", count: 2, carbonsEach: 3)]))
        case .ethanol?:
            rows.append(("Fermentation: ethanol", "Without O₂ the electron transport chain stops. Each pyruvate loses a carbon as CO₂, then takes the electrons from NADH and becomes ethanol, freeing NAD⁺.",
                         .cytosol, "fermentation", 2, ["2 pyruvate", "2 NADH"], ["2 ethanol", "2 CO₂", "2 NAD⁺"], ["NADH": (-2, -2), "CO₂": (2, 2)],
                         [EnergyCarbons(molecule: "ethanol", count: 2, carbonsEach: 2), EnergyCarbons(molecule: "CO₂", count: 2, carbonsEach: 1)]))
            rows.append(("Total without oxygen", "Only glycolysis's 2 ATP per glucose. Fermentation itself makes no ATP: it regenerates NAD⁺ so glycolysis can go on.",
                         nil, "fermentation", 3, [], ["2 ATP", "2 ethanol", "2 CO₂"], [:],
                         [EnergyCarbons(molecule: "ethanol", count: 2, carbonsEach: 2), EnergyCarbons(molecule: "CO₂", count: 2, carbonsEach: 1)]))
        case nil:
            let six = [EnergyCarbons(molecule: "CO₂", count: 6, carbonsEach: 1)]
            rows += [
                ("Pyruvate oxidation", "Each pyruvate enters the mitochondrial matrix, loses a carbon as CO₂ and joins coenzyme A: 2 acetyl-CoA, 2 CO₂ and 2 NADH.",
                 .matrix, "pyruvate", 2, ["2 pyruvate", "2 NAD⁺", "2 CoA"], ["2 acetyl-CoA", "2 CO₂", "2 NADH"], ["NADH": (2, 2), "CO₂": (2, 2)],
                 [EnergyCarbons(molecule: "acetyl-CoA", count: 2, carbonsEach: 2), EnergyCarbons(molecule: "CO₂", count: 2, carbonsEach: 1)]),
                ("Citric acid cycle, turn 1", "Acetyl-CoA joins oxaloacetate to make citrate. One turn releases 2 CO₂ and makes 3 NADH, 1 FADH₂ and 1 ATP.",
                 .matrix, "krebs", 3, ["1 acetyl-CoA", "3 NAD⁺", "FAD"], ["2 CO₂", "3 NADH", "1 FADH₂", "1 ATP"],
                 ["NADH": (3, 3), "FADH₂": (1, 1), "ATP": (1, 1), "CO₂": (2, 2)],
                 [EnergyCarbons(molecule: "acetyl-CoA", count: 1, carbonsEach: 2), EnergyCarbons(molecule: "CO₂", count: 4, carbonsEach: 1)]),
                ("Citric acid cycle, turn 2", "The second acetyl-CoA goes round the same way. All six of glucose's carbons have now left as CO₂.",
                 .matrix, "krebs", 3, ["1 acetyl-CoA", "3 NAD⁺", "FAD"], ["2 CO₂", "3 NADH", "1 FADH₂", "1 ATP"],
                 ["NADH": (3, 3), "FADH₂": (1, 1), "ATP": (1, 1), "CO₂": (2, 2)], six),
                ("Electron transport chain", "NADH and FADH₂ hand their electrons to the chain in the inner membrane, which pumps H⁺ into the intermembrane space. O₂ takes the electrons at the end and becomes H₂O.",
                 .innerMembrane, "oxphos", 4, ["10 NADH", "2 FADH₂", "6 O₂"], ["10 NAD⁺", "2 FAD", "6 H₂O", "an H⁺ gradient"], [:], six),
                ("ATP synthase", "H⁺ flows back into the matrix through ATP synthase, which turns and makes about 26–28 ATP: chemiosmosis.",
                 .innerMembrane, "oxphos", 4, ["ADP + Pᵢ", "H⁺ gradient"], ["about 26–28 ATP"], ["ATP": (26, 28)], six),
                ("Total with oxygen", "About 30–32 ATP per glucose (older books say 36–38). Six carbons left as 6 CO₂, and 6 O₂ became 6 H₂O.",
                 nil, nil, 5, [], ["about 30–32 ATP", "6 CO₂", "6 H₂O"], [:], six),
            ]
        }
        return build(respirationCounts, rows)
    }

    static let photosynthesisCounts = ["H₂O split", "O₂ released", "ATP made", "NADPH made", "CO₂ fixed", "ATP used", "NADPH used", "G3P made"]

    /// One turn of the light reactions and the Calvin cycle, to one G3P.
    static let photosynthesis: [EnergyStep] = build(photosynthesisCounts, [
        ("Light reaches photosystem II", "In the thylakoid membrane, chlorophyll in photosystem II absorbs light, and its electrons are raised to a higher energy.",
         .thylakoid, "light", 1, ["light"], ["excited electrons"], [:], []),
        ("Water is split", "Photosystem II replaces its electrons by splitting water: 6 H₂O → 3 O₂ + 12 H⁺ + 12 e⁻. The O₂ comes from **water**, not CO₂.",
         .thylakoid, "light", 1, ["6 H₂O"], ["3 O₂", "12 H⁺", "12 e⁻"], ["H₂O split": (6, 6), "O₂ released": (3, 3)], []),
        ("Electron transport", "The electrons pass along a chain to photosystem I; their energy pumps H⁺ into the thylakoid space.",
         .thylakoid, "light", 1, ["electrons"], ["an H⁺ gradient"], [:], []),
        ("Photosystem I makes NADPH", "Light raises the electrons again at photosystem I, and they reduce NADP⁺ to NADPH.",
         .thylakoid, "light", 1, ["6 NADP⁺", "light"], ["6 NADPH"], ["NADPH made": (6, 6)], []),
        ("ATP synthase makes ATP", "H⁺ flows back out to the stroma through ATP synthase, making ATP; some comes from cyclic electron flow around photosystem I.",
         .thylakoid, "light", 1, ["9 ADP + Pᵢ"], ["9 ATP"], ["ATP made": (9, 9)], []),
        ("Carbon fixation", "In the stroma, rubisco joins 3 CO₂ to 3 RuBP (5 carbons each), giving 6 molecules of 3-PGA (3 carbons each).",
         .stroma, "calvin", 2, ["3 CO₂", "3 RuBP"], ["6 3-PGA"], ["CO₂ fixed": (3, 3)], [EnergyCarbons(molecule: "3-PGA", count: 6, carbonsEach: 3)]),
        ("Reduction", "6 ATP and 6 NADPH from the light reactions turn the 6 3-PGA into 6 G3P, a 3-carbon sugar.",
         .stroma, "calvin", 2, ["6 3-PGA", "6 ATP", "6 NADPH"], ["6 G3P", "6 ADP", "6 NADP⁺"], ["ATP used": (6, 6), "NADPH used": (6, 6)],
         [EnergyCarbons(molecule: "G3P", count: 6, carbonsEach: 3)]),
        ("One G3P leaves", "One G3P leaves the cycle: the sugar made. Two of them make one glucose.",
         .stroma, "calvin", 2, ["6 G3P"], ["1 G3P out", "5 G3P stay"], ["G3P made": (1, 1)],
         [EnergyCarbons(molecule: "G3P (out)", count: 1, carbonsEach: 3), EnergyCarbons(molecule: "G3P", count: 5, carbonsEach: 3)]),
        ("Regeneration", "3 more ATP turn the other 5 G3P back into 3 RuBP. One G3P cost 3 CO₂, 9 ATP and 6 NADPH; a glucose costs twice that.",
         .stroma, "calvin", 2, ["5 G3P", "3 ATP"], ["3 RuBP", "3 ADP"], ["ATP used": (3, 3)],
         [EnergyCarbons(molecule: "G3P (out)", count: 1, carbonsEach: 3), EnergyCarbons(molecule: "RuBP", count: 3, carbonsEach: 5)]),
    ])

    // MARK: Follow the atoms

    static func trace(_ t: EnergyTrace) -> [EnergyTraceStep] {
        switch t {
        case .oxygen:
            return [
                EnergyTraceStep(molecule: "Water", formula: "H₂[O]", carbons: 0, traced: 0, title: "In water",
                                text: "Water from the roots reaches the chloroplast's thylakoids. Follow its oxygen atom.",
                                place: .thylakoid, process: "Taken up by the roots"),
                EnergyTraceStep(molecule: "Oxygen", formula: "[O]=O", carbons: 0, traced: 0, title: "Water is split",
                                text: "Photosystem II splits water: 2 H₂O → O₂ + 4 H⁺ + 4 e⁻. The atom is now in O₂: the O₂ plants release comes from **water**, not from CO₂.",
                                place: .thylakoid, process: "Light reactions"),
                EnergyTraceStep(molecule: "Oxygen", formula: "[O]=O", carbons: 0, traced: 0, title: "O₂ leaves the chloroplast",
                                text: "It diffuses out through the cytosol: out of the leaf by the stomata, or into a mitochondrion of this cell or an animal's.",
                                place: .cytosol, process: "Diffusion"),
                EnergyTraceStep(molecule: "Water", formula: "H₂[O]", carbons: 0, traced: 0, title: "O₂ takes the electrons",
                                text: "At the end of the electron transport chain, O₂ + 4 e⁻ + 4 H⁺ → 2 H₂O. The atom is back in water.",
                                place: .innerMembrane, process: "Oxidative phosphorylation"),
            ]
        case .carbon:
            return [
                EnergyTraceStep(molecule: "Carbon dioxide", formula: "[C]O₂", carbons: 1, traced: 0, title: "In carbon dioxide",
                                text: "CO₂ from the air enters the leaf through the stomata and reaches the stroma. Follow its carbon.",
                                place: .stroma, process: "Diffusion"),
                EnergyTraceStep(molecule: "3-PGA", formula: "3-phosphoglycerate", carbons: 3, traced: 0, title: "Fixed by rubisco",
                                text: "Rubisco joins CO₂ to RuBP; the carbon becomes the carboxyl carbon (carbon 1) of a 3-PGA.",
                                place: .stroma, process: "Calvin cycle"),
                EnergyTraceStep(molecule: "G3P", formula: "glyceraldehyde 3-phosphate", carbons: 3, traced: 0, title: "Reduced to a sugar",
                                text: "ATP and NADPH reduce 3-PGA to G3P, a 3-carbon sugar. The carbon is still carbon 1.",
                                place: .stroma, process: "Calvin cycle"),
                EnergyTraceStep(molecule: "Glucose", formula: "C₆H₁₂O₆", carbons: 6, traced: 3, title: "Built into glucose",
                                text: "Two 3-carbon sugars join into glucose (kept as starch, or sent round the plant as sucrose). Our carbon is now glucose's carbon 4.",
                                place: .cytosol, process: "Sugar made from G3P"),
                EnergyTraceStep(molecule: "Pyruvate", formula: "pyruvate", carbons: 3, traced: 0, title: "Glycolysis splits glucose",
                                text: "In the cytosol, of this cell or of one that ate it, glycolysis splits glucose into two pyruvate. Carbon 4 becomes a pyruvate's carboxyl carbon.",
                                place: .cytosol, process: "Glycolysis"),
                EnergyTraceStep(molecule: "Carbon dioxide", formula: "[C]O₂", carbons: 1, traced: 0, title: "Back to carbon dioxide",
                                text: "In the matrix, pyruvate oxidation removes the carboxyl carbon as CO₂: our carbon, back where it began. The other carbons leave as CO₂ in the citric acid cycle.",
                                place: .matrix, process: "Pyruvate oxidation"),
            ]
        }
    }

    static func traceSteps(_ t: EnergyTrace) -> [BioStep] {
        trace(t).enumerated().map { BioStep($0.offset, $0.element.title, $0.element.text) }
    }
}

// MARK: - Self-check

extension BioSelfCheck {
    /// Photosynthesis & Respiration's own `bio-check` lines, against §5.8.
    static func energyChecks(_ r: inout BioCheckReport) {
        let s = EnergyPathways.stages
        r.check("energy.stages", s.map(\.id).joined(separator: ","), s.count == 7
                && s.map(\.place) == [.thylakoid, .stroma, .cytosol, .matrix, .matrix, .innerMembrane, .cytosol])
        r.check("energy.glycolysis", EnergyPathways.stage("glycolysis")?.outputs.joined(separator: "; ") ?? "-",
                EnergyPathways.stage("glycolysis")?.outputs == ["2 pyruvate", "2 NADH", "4 ATP (net **2**)"])
        r.check("energy.oxygen-from-water", EnergyPathways.stage("light")?.outputs.first ?? "-",
                EnergyPathways.stage("light")?.outputs.first == "O₂ (from **water**)" && EnergyPathways.stage("light")?.inputs.first == "H₂O")
        // (one glucose with O₂: 30–32 ATP (4 of them before the chain), 10 NADH, 2 FADH₂, 6 CO₂)
        func totals(_ steps: [EnergyStep], at i: Int? = nil) -> [String: (Int, Int)] {
            let step = i.map { steps[$0] } ?? steps[steps.count - 1]
            return Dictionary(uniqueKeysWithValues: step.counts.map { ($0.name, ($0.low, $0.high)) })
        }
        let aerobic = EnergyPathways.respiration()
        let t = totals(aerobic)
        let beforeChain = totals(aerobic, at: aerobic.firstIndex { $0.step.title == "ATP synthase" }! - 1)
        r.check("energy.respiration", "ATP\(t["ATP"]!.0)-\(t["ATP"]!.1),NADH\(t["NADH"]!.0),FADH2\(t["FADH₂"]!.0),CO2\(t["CO₂"]!.0)",
                t["ATP"]! == (30, 32) && t["NADH"]! == (10, 10) && t["FADH₂"]! == (2, 2) && t["CO₂"]! == (6, 6) && beforeChain["ATP"]! == (4, 4))
        let afterGlycolysis = totals(aerobic, at: 2)
        r.check("energy.glycolysis-net", "ATP\(afterGlycolysis["ATP"]!.0)", afterGlycolysis["ATP"]! == (2, 2) && afterGlycolysis["NADH"]! == (2, 2))
        // (without O₂: glycolysis's 2 ATP only, NAD⁺ regenerated; ethanol releases 2 CO₂)
        let lactate = totals(EnergyPathways.respiration(fermenting: .lactate)), ethanol = totals(EnergyPathways.respiration(fermenting: .ethanol))
        r.check("energy.fermentation", "ATP\(lactate["ATP"]!.0),NADH\(lactate["NADH"]!.0),CO2\(ethanol["CO₂"]!.0)",
                lactate["ATP"]! == (2, 2) && lactate["NADH"]! == (0, 0) && ethanol["ATP"]! == (2, 2) && ethanol["CO₂"]! == (2, 2) && lactate["CO₂"]! == (0, 0))
        // (every carbon of glucose accounted for at every step)
        let all = [EnergyPathways.respiration(), EnergyPathways.respiration(fermenting: .lactate), EnergyPathways.respiration(fermenting: .ethanol)]
        let lost = all.flatMap { $0 }.filter { $0.carbons.reduce(0) { $0 + $1.count * $1.carbonsEach } != 6 }.map(\.step.title)
        r.check("energy.carbons", lost.isEmpty ? "6-each-step" : lost.joined(separator: ","), lost.isEmpty)
        // (one G3P: 3 CO₂, 9 ATP, 6 NADPH, and 3 O₂ from 6 H₂O; the Calvin cycle's 18 carbons kept)
        let p = totals(EnergyPathways.photosynthesis)
        r.check("energy.calvin", "CO2\(p["CO₂ fixed"]!.0),ATP\(p["ATP used"]!.0),NADPH\(p["NADPH used"]!.0),O2\(p["O₂ released"]!.0),G3P\(p["G3P made"]!.0)",
                p["CO₂ fixed"]! == (3, 3) && p["ATP used"]! == (9, 9) && p["NADPH used"]! == (6, 6) && p["ATP made"]! == p["ATP used"]!
                && p["NADPH made"]! == p["NADPH used"]! && p["O₂ released"]! == (3, 3) && p["H₂O split"]! == (6, 6) && p["G3P made"]! == (1, 1))
        let calvin = EnergyPathways.photosynthesis.filter { !$0.carbons.isEmpty }
        r.check("energy.calvin-carbons", "\(calvin.count)", calvin.count == 4
                && calvin.allSatisfy { $0.carbons.reduce(0) { $0 + $1.count * $1.carbonsEach } == 18 })
        // (follow the atoms: the O in O₂ comes from water and returns to water; the C goes from CO₂ to CO₂)
        let o = EnergyPathways.trace(.oxygen), c = EnergyPathways.trace(.carbon)
        r.check("energy.trace-oxygen", o.map(\.formula).joined(separator: ">"), o.first?.formula == "H₂[O]" && o[1].formula == "[O]=O"
                && o[1].place == .thylakoid && o.last?.formula == "H₂[O]" && o.last?.place == .innerMembrane)
        r.check("energy.trace-carbon", c.map(\.molecule).joined(separator: ">"), c.first?.formula == "[C]O₂" && c.last?.formula == "[C]O₂"
                && c.contains { $0.molecule == "Glucose" && $0.carbons == 6 } && c.allSatisfy { $0.traced < max($0.carbons, 1) })
        // (§3.6: titles of at most 40 characters)
        let long = (all.flatMap { $0 } + EnergyPathways.photosynthesis).map(\.step.title) + (o + c).map(\.title)
        r.check("energy.titles", "\(long.map(\.count).max() ?? 0)", long.allSatisfy { $0.count <= 40 })
        r.same("energy.equations", EnergyPathways.photosynthesisEquation + " | " + EnergyPathways.respirationEquation,
               "6 CO₂ + 6 H₂O + light → C₆H₁₂O₆ + 6 O₂ | C₆H₁₂O₆ + 6 O₂ → 6 CO₂ + 6 H₂O + energy (ATP and heat)")
    }
}

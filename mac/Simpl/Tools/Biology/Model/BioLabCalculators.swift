import Foundation

// Bio Lab Calculator's calculators (spec §4.8, §5.5): every formula of the lab-formula table, each as values its card
// draws — fields with unit menus, "solve for" modes, and the working: the formula rearranged, the numbers substituted
// with their units, the steps (unit conversions first) and the answer rounded as §5.5 says. Each starts on the
// specification's test vector, so a card opened for the first time shows a worked example. BioSelfCheck.biolabChecks
// runs every one of them against §5.5.

/// A solute in the molar-mass menu, its molar mass worked out from its formula.
struct BioLabSolute {
    let name: String
    /// Plain ("C12H22O11"), for the arithmetic.
    let formula: String
    var mass: Double { BioLab.molarMass(formula) ?? 0 }
    /// With subscripts ("C₁₂H₂₂O₁₁").
    var written: String { BioLab.subscripted(formula) }
}

enum BioLab {
    /// A page of the tool: its calculators, with a line saying what it is for.
    struct Page {
        let id: String
        let title: String
        let line: String
    }

    static let pages: [Page] = [
        Page(id: "solutions", title: "Solutions", line: "Molarity, dilutions, serial dilutions, plate counts, percent solutions and units."),
        Page(id: "microscopy", title: "Microscopy", line: "Magnification, field of view, specimen size and scale bars."),
        Page(id: "cells", title: "Cells", line: "Surface area to volume, water potential and cell counts."),
        Page(id: "stats", title: "Statistics", line: "Mean, standard deviation, standard error, ±2 SE error bars and chi-square."),
        Page(id: "ecology", title: "Populations", line: "Rates of change, population growth and Simpson's diversity."),
        Page(id: "molecules", title: "Molecules", line: "Absorbance, nucleic acids from A₂₆₀, PCR, centrifuges, bacterial growth, pH and Q₁₀."),
    ]

    static let all: [BioLabCalculator] = [
        molarity, dilution, serial, plate, percent, convert,
        totalMagnification, fieldOfView, specimen, magnification, scaleBar,
        surfaceArea, waterPotential, hemocytometer,
        statistics, chiSquare,
        rate, growth, simpson,
        beerLambert, nucleicAcid, pcr, centrifuge, bacteria, pH, q10,
    ]

    static func calculator(_ id: String) -> BioLabCalculator? { all.first { $0.id == id } }

    /// A page's calculators, in order.
    static func on(_ page: String) -> [BioLabCalculator] { all.filter { $0.page == page } }

    /// The metric prefixes of the AP sheet (§5.5).
    static let prefixes: [BioLabPrefix] = [
        BioLabPrefix(name: "giga", symbol: "G", exponent: 9), BioLabPrefix(name: "mega", symbol: "M", exponent: 6),
        BioLabPrefix(name: "kilo", symbol: "k", exponent: 3), BioLabPrefix(name: "deci", symbol: "d", exponent: -1),
        BioLabPrefix(name: "centi", symbol: "c", exponent: -2), BioLabPrefix(name: "milli", symbol: "m", exponent: -3),
        BioLabPrefix(name: "micro", symbol: "µ", exponent: -6), BioLabPrefix(name: "nano", symbol: "n", exponent: -9),
        BioLabPrefix(name: "pico", symbol: "p", exponent: -12),
    ]

    /// R in ΨS = −iCRT: 0.0831 L·bar/(mol·K) (the AP sheet).
    static let gasConstant = 0.0831
    /// RCF = 1.118 × 10⁻⁵ × r(cm) × rpm².
    static let centrifugeConstant = 1.118e-5

    // MARK: Molar masses

    /// Standard atomic weights (g/mol) of the elements the solute menu uses.
    static let atomicWeights: [String: Double] = ["H": 1.00794, "C": 12.0107, "N": 14.0067, "O": 15.9994, "Na": 22.98977,
                                                  "Mg": 24.3050, "P": 30.973762, "S": 32.065, "Cl": 35.453, "K": 39.0983, "Ca": 40.078]

    /// The molar mass of a formula written plainly ("NaHCO3"), nil if it names an element not in the table.
    static func molarMass(_ formula: String) -> Double? {
        var total = 0.0
        var i = formula.startIndex
        while i < formula.endIndex {
            guard formula[i].isUppercase else { return nil }
            var symbol = String(formula[i])
            i = formula.index(after: i)
            while i < formula.endIndex, formula[i].isLowercase { symbol.append(formula[i]); i = formula.index(after: i) }
            var digits = ""
            while i < formula.endIndex, formula[i].isNumber { digits.append(formula[i]); i = formula.index(after: i) }
            guard let w = atomicWeights[symbol] else { return nil }
            total += w * Double(Int(digits) ?? 1)
        }
        return total
    }

    static func subscripted(_ s: String) -> String {
        let subs: [Character: Character] = ["0": "₀", "1": "₁", "2": "₂", "3": "₃", "4": "₄", "5": "₅", "6": "₆", "7": "₇", "8": "₈", "9": "₉"]
        return String(s.map { subs[$0] ?? $0 })
    }

    static let solutes: [BioLabSolute] = [
        BioLabSolute(name: "Sodium chloride", formula: "NaCl"), BioLabSolute(name: "Potassium chloride", formula: "KCl"),
        BioLabSolute(name: "Calcium chloride", formula: "CaCl2"), BioLabSolute(name: "Magnesium chloride", formula: "MgCl2"),
        BioLabSolute(name: "Sodium hydroxide", formula: "NaOH"), BioLabSolute(name: "Sodium bicarbonate", formula: "NaHCO3"),
        BioLabSolute(name: "Glucose", formula: "C6H12O6"), BioLabSolute(name: "Sucrose", formula: "C12H22O11"),
        BioLabSolute(name: "Tris", formula: "C4H11NO3"),
    ]

    private static var solutePresets: [BioLabPreset] {
        solutes.map { BioLabPreset(label: "\($0.name), \($0.written): \(BioLabFormat.fixed($0.mass, decimals: 2))", text: BioLabFormat.fixed($0.mass, decimals: 2)) }
    }

    // MARK: Small helpers

    private static func w(_ x: Double) -> String { BioLabFormat.work(x) }
    private static func sub(_ n: Int) -> String { subscripted(String(n)) }

    private static func unit(_ q: BioLabQuantity, _ symbol: String) -> BioLabUnit? { q.unit(symbol) }

    private static func plainAnswer(_ symbol: String, _ label: String, _ value: Double, _ unit: String, _ digits: Int, field: String? = nil) -> BioLabAnswer {
        BioLabAnswer(symbol: symbol, label: label, value: value, unit: unit, text: BioLabFormat.sig(value, digits), field: field)
    }

    // MARK: - Solutions

    static let molarity = BioLabCalculator(
        id: "molarity", page: "solutions", title: "Molarity",
        line: "Mass, moles, concentration and volume of a solution.",
        formula: "M = n / V,   n = m / MW",
        fields: [
            BioLabField("M", "M", "Molarity", quantity: .concentration, unit: "M", initial: "0.5"),
            BioLabField("V", "V", "Volume of solution", quantity: .volume, unit: "mL", initial: "250"),
            BioLabField("MW", "MW", "Molar mass", unit: "g/mol", initial: "58.44", presets: solutePresets),
            BioLabField("m", "m", "Mass of solute", quantity: .mass, unit: "g", initial: "7.31"),
            BioLabField("n", "n", "Moles of solute", quantity: .amount, unit: "mol", initial: "0.125"),
        ],
        modes: [
            BioLabMode("mass", "Mass", "m = M × V × MW", inputs: ["M", "V", "MW"], unknown: "m"),
            BioLabMode("molarity", "Molarity", "M = m / (MW × V)", inputs: ["m", "MW", "V"], unknown: "M"),
            BioLabMode("volume", "Volume", "V = m / (MW × M)", inputs: ["m", "MW", "M"], unknown: "V"),
            BioLabMode("moles", "Moles", "n = m / MW", inputs: ["m", "MW"], unknown: "n"),
        ],
        compute: { v in
            let litre = unit(.volume, "L"), molar = unit(.concentration, "M"), gram = unit(.mass, "g")
            switch v.mode.id {
            case "molarity":
                let m = try v.positive("m"), mw = try v.positive("MW"), V = try v.positive("V")
                let n = m / mw, M = n / V
                var steps = [v.conversion("m", to: gram), v.conversion("V", to: litre)].compactMap { $0 }
                steps.append("Moles: n = m / MW = \(v.written(m, in: gram)) / \(v.shown("MW")) = \(w(n)) mol")
                steps.append("Molarity: M = n / V = \(w(n)) mol / \(v.written(V, in: litre)) = \(w(M)) M")
                return BioLabResult(answers: [v.answer("M", base: M)], formula: v.mode.formula,
                                    working: "M = \(v.written(m, in: gram)) / (\(v.shown("MW")) × \(v.written(V, in: litre)))", steps: steps)
            case "volume":
                let m = try v.positive("m"), mw = try v.positive("MW"), M = try v.positive("M")
                let n = m / mw, V = n / M
                var steps = [v.conversion("m", to: gram), v.conversion("M", to: molar)].compactMap { $0 }
                steps.append("Moles: n = m / MW = \(v.written(m, in: gram)) / \(v.shown("MW")) = \(w(n)) mol")
                steps.append("Volume: V = n / M = \(w(n)) mol / \(v.written(M, in: molar)) = \(w(V)) L")
                if let out = v.out("V"), out.symbol != "L" { steps.append("\(w(V)) L = \(v.written(V, in: out))") }
                return BioLabResult(answers: [v.answer("V", base: V)], formula: v.mode.formula,
                                    working: "V = \(v.written(m, in: gram)) / (\(v.shown("MW")) × \(v.written(M, in: molar)))", steps: steps)
            case "moles":
                let m = try v.positive("m"), mw = try v.positive("MW")
                let n = m / mw
                var steps = [v.conversion("m", to: gram)].compactMap { $0 }
                steps.append("Moles: n = m / MW = \(v.written(m, in: gram)) / \(v.shown("MW")) = \(w(n)) mol")
                return BioLabResult(answers: [v.answer("n", base: n)], formula: v.mode.formula,
                                    working: "n = \(v.written(m, in: gram)) / \(v.shown("MW"))", steps: steps)
            default:
                let M = try v.positive("M"), V = try v.positive("V"), mw = try v.positive("MW")
                let n = M * V, m = n * mw
                var steps = [v.conversion("V", to: litre), v.conversion("M", to: molar)].compactMap { $0 }
                steps.append("Moles needed: n = M × V = \(v.written(M, in: molar)) × \(v.written(V, in: litre)) = \(w(n)) mol")
                steps.append("Mass: m = n × MW = \(w(n)) mol × \(v.shown("MW")) = \(w(m)) g")
                let a = v.answer("m", base: m)
                return BioLabResult(answers: [a, plainAnswer("n", "Moles", n, "mol", v.digits)], formula: v.mode.formula,
                                    working: "m = \(v.written(M, in: molar)) × \(v.written(V, in: litre)) × \(v.shown("MW"))", steps: steps,
                                    notes: ["Weigh \(a.full), dissolve it in less water than you need, then add water up to \(v.shown("V"))."])
            }
        })

    static let dilution = BioLabCalculator(
        id: "dilution", page: "solutions", title: "Dilution",
        line: "How much stock to dilute, or how strong the result is. Any matching units.",
        formula: "C₁V₁ = C₂V₂",
        fields: [
            BioLabField("C1", "C₁", "Stock concentration", quantity: .solution, unit: "M", initial: "1.0"),
            BioLabField("V1", "V₁", "Volume of stock", quantity: .volume, unit: "mL", initial: "5.0"),
            BioLabField("C2", "C₂", "Final concentration", quantity: .solution, unit: "M", initial: "0.1"),
            BioLabField("V2", "V₂", "Final volume", quantity: .volume, unit: "mL", initial: "50"),
        ],
        modes: [
            BioLabMode("V1", "V₁", "V₁ = C₂V₂ / C₁", inputs: ["C1", "C2", "V2"], unknown: "V1"),
            BioLabMode("C1", "C₁", "C₁ = C₂V₂ / V₁", inputs: ["V1", "C2", "V2"], unknown: "C1"),
            BioLabMode("C2", "C₂", "C₂ = C₁V₁ / V₂", inputs: ["C1", "V1", "V2"], unknown: "C2"),
            BioLabMode("V2", "V₂", "V₂ = C₁V₁ / C₂", inputs: ["C1", "V1", "C2"], unknown: "V2"),
        ],
        compute: { v in
            func sameKind(_ a: String, _ b: String) throws {
                if v.unit(a)?.group != v.unit(b)?.group {
                    throw BioLabProblem(b, "C₁ and C₂ need the same kind of unit: both molar, both %, both × or both mass per volume.")
                }
            }
            func solvent(_ v1: Double, _ v2: Double, in u: BioLabUnit?) -> BioLabAnswer {
                let f = u?.factor ?? 1
                return BioLabAnswer(symbol: "V₂ − V₁", label: "Solvent to add", value: (v2 - v1) / f, unit: u?.symbol ?? "L",
                                    text: BioLabFormat.sig((v2 - v1) / f, v.digits))
            }
            switch v.mode.id {
            case "C1":
                try sameKind("C2", "C1")
                let v1 = try v.positive("V1"), c2 = try v.positive("C2"), v2 = try v.positive("V2")
                guard v1 <= v2 else { throw BioLabProblem("V1", "V₁ can't be more than V₂: the final volume includes the stock.") }
                let cu = v.unit("C2"), vu = v.unit("V2")
                let c1 = c2 * v2 / v1
                var steps = [v.conversion("V1", to: vu)].compactMap { $0 }
                steps.append("C₁ = C₂V₂ / V₁ = (\(v.shown("C2")) × \(v.shown("V2"))) / \(v.shown("V1", in: vu)) = \(v.written(c1, in: cu))")
                if let out = v.out("C1"), out.symbol != cu?.symbol { steps.append("\(v.written(c1, in: cu)) = \(v.written(c1, in: out))") }
                return BioLabResult(answers: [v.answer("C1", base: c1)], formula: v.mode.formula,
                                    working: "C₁ = (\(v.shown("C2")) × \(v.shown("V2"))) / \(v.shown("V1", in: vu))", steps: steps)
            case "C2":
                try sameKind("C1", "C2")
                let c1 = try v.positive("C1"), v1 = try v.positive("V1"), v2 = try v.positive("V2")
                guard v1 <= v2 else { throw BioLabProblem("V2", "V₂ can't be less than V₁: the final volume includes the stock.") }
                let cu = v.unit("C1"), vu = v.unit("V2")
                let c2 = c1 * v1 / v2
                var steps = [v.conversion("V1", to: vu)].compactMap { $0 }
                steps.append("C₂ = C₁V₁ / V₂ = (\(v.shown("C1")) × \(v.shown("V1", in: vu))) / \(v.shown("V2")) = \(v.written(c2, in: cu))")
                if let out = v.out("C2"), out.symbol != cu?.symbol { steps.append("\(v.written(c2, in: cu)) = \(v.written(c2, in: out))") }
                return BioLabResult(answers: [v.answer("C2", base: c2)], formula: v.mode.formula,
                                    working: "C₂ = (\(v.shown("C1")) × \(v.shown("V1", in: vu))) / \(v.shown("V2"))", steps: steps,
                                    notes: ["The stock is diluted \(BioLabFormat.ratio(v1 / v2)): \(v.shown("V1", in: vu)) of stock in \(v.shown("V2")) in all."])
            case "V2":
                try sameKind("C1", "C2")
                let c1 = try v.positive("C1"), v1 = try v.positive("V1"), c2 = try v.positive("C2")
                guard c2 <= c1 else { throw BioLabProblem("C2", "C₂ must not be more than C₁: a dilution makes a solution weaker.") }
                let cu = v.unit("C1"), vu = v.unit("V1")
                let v2 = c1 * v1 / c2
                var steps = [v.conversion("C2", to: cu)].compactMap { $0 }
                steps.append("V₂ = C₁V₁ / C₂ = (\(v.shown("C1")) × \(v.shown("V1"))) / \(v.shown("C2", in: cu)) = \(v.written(v2, in: vu))")
                if let out = v.out("V2"), out.symbol != vu?.symbol { steps.append("\(v.written(v2, in: vu)) = \(v.written(v2, in: out))") }
                let a = v.answer("V2", base: v2), s = solvent(v1, v2, in: v.out("V2"))
                return BioLabResult(answers: [a, s], formula: v.mode.formula,
                                    working: "V₂ = (\(v.shown("C1")) × \(v.shown("V1"))) / \(v.shown("C2", in: cu))", steps: steps,
                                    notes: ["Add \(s.full) of solvent to \(v.shown("V1")) of stock to make \(a.full)."])
            default:
                try sameKind("C1", "C2")
                let c1 = try v.positive("C1"), c2 = try v.positive("C2"), v2 = try v.positive("V2")
                guard c2 <= c1 else { throw BioLabProblem("C2", "C₂ must not be more than C₁: a dilution makes a solution weaker.") }
                let cu = v.unit("C1"), vu = v.unit("V2")
                let v1 = c2 * v2 / c1
                var steps = [v.conversion("C2", to: cu)].compactMap { $0 }
                steps.append("V₁ = C₂V₂ / C₁ = (\(v.shown("C2", in: cu)) × \(v.shown("V2"))) / \(v.shown("C1")) = \(v.written(v1, in: vu))")
                if let out = v.out("V1"), out.symbol != vu?.symbol { steps.append("\(v.written(v1, in: vu)) = \(v.written(v1, in: out))") }
                let a = v.answer("V1", base: v1), s = solvent(v1, v2, in: v.out("V1"))
                steps.append("Solvent: V₂ − V₁ = \(v.shown("V2", in: v.out("V1"))) − \(a.full) = \(s.full)")
                return BioLabResult(answers: [a, s], formula: v.mode.formula,
                                    working: "V₁ = (\(v.shown("C2", in: cu)) × \(v.shown("V2"))) / \(v.shown("C1"))", steps: steps,
                                    notes: ["Take \(a.full) of stock and add \(s.full) of solvent to make \(v.shown("V2"))."])
            }
        })

    static let serial = BioLabCalculator(
        id: "serial", page: "solutions", title: "Serial dilution",
        line: "The same dilution, again and again: every tube's factor and concentration.",
        formula: "f = sample / (sample + diluent);   Cₖ = C₀ × fᵏ",
        fields: [
            BioLabField("vs", "", "Sample moved each step", quantity: .volume, unit: "mL", initial: "1"),
            BioLabField("vd", "", "Diluent in each tube", quantity: .volume, unit: "mL", initial: "9"),
            BioLabField("k", "k", "Number of steps", unit: "steps", initial: "4", exact: true),
            BioLabField("c0", "C₀", "Starting concentration (optional)", quantity: .stock, unit: "M", initial: "1.0", optional: true),
        ],
        modes: [BioLabMode("serial", "", "f = sample / (sample + diluent);   Cₖ = C₀ × fᵏ", inputs: ["vs", "vd", "k", "c0"])],
        compute: { v in
            let vs = try v.positive("vs"), vd = try v.nonNegative("vd"), k = try v.whole("k", 1...12)
            let c0: Double? = try v.text("c0").isEmpty ? nil : v.positive("c0")
            let f = vs / (vs + vd), total = pow(f, Double(k))
            let d = v.digits, cu = v.unitText("c0")
            var tubes: [BioLabTube] = []
            var rows: [[String]] = []
            for i in 0...k {
                let fi = pow(f, Double(i))
                let factor = i == 0 ? "1" : BioLabFormat.dilution(fi, digits: d)
                let conc = c0.map { BioLabFormat.sig($0 * fi, d) + " " + cu } ?? "—"
                tubes.append(BioLabTube(label: i == 0 ? "Stock" : "\(i)", factor: factor, concentration: conc, depth: Double(i) / Double(k)))
                rows.append(c0 == nil ? [i == 0 ? "Stock" : "Tube \(i)", factor] : [i == 0 ? "Stock" : "Tube \(i)", factor, conc])
            }
            var answers = [
                BioLabAnswer(symbol: "f", label: "Each step", value: f, unit: "", text: "\(BioLabFormat.ratio(f, digits: d)) (\(BioLabFormat.dilution(f, digits: d)))"),
                BioLabAnswer(symbol: "fᵏ", label: "After \(k) steps", value: total, unit: "",
                             text: "\(BioLabFormat.dilution(total, digits: d)) (\(BioLabFormat.ratio(total, digits: d)))"),
            ]
            var steps = [
                "Each tube: \(v.shown("vs")) of the tube before into \(v.shown("vd")) of diluent, so f = \(v.shown("vs")) / (\(v.shown("vs")) + \(v.shown("vd"))) = \(w(f)) (\(BioLabFormat.ratio(f, digits: d))).",
                "The factors multiply: after \(k) steps, fᵏ = \(w(f))\(BioLabFormat.superscript(k)) = \(BioLabFormat.dilution(total, digits: d)).",
            ]
            if let c0 {
                answers.append(BioLabAnswer(symbol: "C\(sub(k))", label: "In the last tube", value: c0 * total, unit: cu, text: BioLabFormat.sig(c0 * total, d)))
                steps.append("C\(sub(k)) = C₀ × fᵏ = \(v.shown("c0")) × \(BioLabFormat.dilution(total, digits: d)) = \(BioLabFormat.sig(c0 * total, d)) \(cu)")
            }
            return BioLabResult(answers: answers, formula: v.mode.formula,
                                working: "f = \(BioLabFormat.ratio(f, digits: d));  fᵏ = \(BioLabFormat.dilution(total, digits: d))", steps: steps,
                                notes: ["Mix each tube well before taking the next sample."],
                                table: BioLabTable(columns: c0 == nil ? ["Tube", "Dilution"] : ["Tube", "Dilution", "Concentration"], rows: rows),
                                tubes: tubes)
        })

    static let plate = BioLabCalculator(
        id: "plate", page: "solutions", title: "Plate count → original concentration",
        line: "Colonies on a plate back to cells per mL in the original sample.",
        formula: "cells/mL = colonies / (volume plated × total dilution)",
        fields: [
            BioLabField("colonies", "", "Colonies counted", unit: "colonies", initial: "150", exact: true),
            BioLabField("vp", "", "Volume plated", quantity: .volume, unit: "mL", initial: "0.1"),
            BioLabField("dil", "", "Total dilution of the tube plated", initial: "10^-6", exact: true,
                        presets: (1...8).map { BioLabPreset(label: BioLabFormat.power(-$0), text: "10^-\($0)") },
                        hint: "As a power of ten (10^-6) or a ratio (1:1000000)."),
        ],
        modes: [BioLabMode("plate", "", "cells/mL = colonies / (volume plated in mL × total dilution)", inputs: ["colonies", "vp", "dil"])],
        compute: { v in
            let c = try v.nonNegative("colonies"), vp = try v.positive("vp")
            var dil = try v.positive("dil")
            var notes: [String] = []
            if dil > 1 {
                notes.append("\(BioLabFormat.plain(dil)) is read as a 1:\(BioLabFormat.plain(dil)) dilution, \(BioLabFormat.dilution(1 / dil)).")
                dil = 1 / dil
            }
            let ml = vp * 1000, perMl = c / ml, cfu = perMl / dil
            var steps = [v.conversion("vp", to: unit(.volume, "mL"))].compactMap { $0 }
            steps.append("Colonies per mL plated: \(BioLabFormat.plain(c)) / \(w(ml)) mL = \(w(perMl)) per mL")
            steps.append("Undo the dilution: \(w(perMl)) / \(BioLabFormat.dilution(dil)) = \(BioLabFormat.sig(cfu, v.digits)) CFU/mL")
            notes.append(c < 30 || c > 300 ? "Plates with 30 to 300 colonies give the most reliable counts." : "Each colony grew from one cell: a colony-forming unit (CFU).")
            return BioLabResult(answers: [plainAnswer("", "In the original sample", cfu, "CFU/mL", v.digits)], formula: v.mode.formula,
                                working: "cells/mL = \(BioLabFormat.plain(c)) / (\(w(ml)) mL × \(BioLabFormat.dilution(dil)))", steps: steps, notes: notes)
        })

    static let percent = BioLabCalculator(
        id: "percent", page: "solutions", title: "Percent solution",
        line: "Weight per volume (w/v) and volume per volume (v/v).",
        formula: "% w/v = g solute / 100 mL solution",
        modeLabel: "Find",
        fields: [
            BioLabField("g", "", "Solute (mass)", quantity: .mass, unit: "g", initial: "2"),
            BioLabField("sv", "", "Solute (volume)", quantity: .volume, unit: "mL", initial: "5"),
            BioLabField("vol", "", "Volume of solution", quantity: .volume, unit: "mL", initial: "100"),
            BioLabField("pct", "%", "Percent", unit: "%", initial: "2"),
        ],
        modes: [
            BioLabMode("wv", "% w/v", "% w/v = g solute / mL solution × 100", inputs: ["g", "vol"], unknown: "pct"),
            BioLabMode("wvMass", "Grams (w/v)", "g solute = % × mL solution / 100", inputs: ["pct", "vol"], unknown: "g"),
            BioLabMode("vv", "% v/v", "% v/v = mL solute / mL solution × 100", inputs: ["sv", "vol"], unknown: "pct"),
            BioLabMode("vvVolume", "mL (v/v)", "mL solute = % × mL solution / 100", inputs: ["pct", "vol"], unknown: "sv"),
        ],
        compute: { v in
            let mlUnit = unit(.volume, "mL"), gUnit = unit(.mass, "g")
            switch v.mode.id {
            case "wvMass":
                let p = try v.positive("pct"), vol = try v.positive("vol")
                let ml = vol * 1000, g = p * ml / 100
                var steps = [v.conversion("vol", to: mlUnit)].compactMap { $0 }
                steps.append("g = \(v.shown("pct")) × \(w(ml)) mL / 100 = \(w(g)) g")
                let a = v.answer("g", base: g)
                return BioLabResult(answers: [a], formula: v.mode.formula, working: "g = \(v.shown("pct")) × \(w(ml)) mL / 100", steps: steps,
                                    notes: ["Dissolve \(a.full) of solute and make it up to \(v.shown("vol")) with water."])
            case "vv":
                let sv = try v.positive("sv"), vol = try v.positive("vol")
                guard sv <= vol else { throw BioLabProblem("sv", "The solute can't be more than the whole solution.") }
                let p = sv / vol * 100
                let steps = [v.conversion("sv", to: mlUnit), v.conversion("vol", to: mlUnit)].compactMap { $0 }
                    + ["% v/v = \(w(sv * 1000)) mL / \(w(vol * 1000)) mL × 100 = \(w(p))%"]
                return BioLabResult(answers: [v.answer("pct", base: p, unitText: "% v/v")], formula: v.mode.formula,
                                    working: "% v/v = \(w(sv * 1000)) mL / \(w(vol * 1000)) mL × 100", steps: steps)
            case "vvVolume":
                let p = try v.positive("pct"), vol = try v.positive("vol")
                guard p <= 100 else { throw BioLabProblem("pct", "A v/v percent can't be more than 100%.") }
                let ml = vol * 1000, s = p * ml / 100
                var steps = [v.conversion("vol", to: mlUnit)].compactMap { $0 }
                steps.append("mL solute = \(v.shown("pct")) × \(w(ml)) mL / 100 = \(w(s)) mL")
                let a = v.answer("sv", base: s / 1000)
                return BioLabResult(answers: [a], formula: v.mode.formula, working: "mL solute = \(v.shown("pct")) × \(w(ml)) mL / 100", steps: steps,
                                    notes: ["Measure \(a.full) of the liquid and add water up to \(v.shown("vol"))."])
            default:
                let g = try v.positive("g"), vol = try v.positive("vol")
                let ml = vol * 1000, p = g / ml * 100
                let steps = [v.conversion("g", to: gUnit), v.conversion("vol", to: mlUnit)].compactMap { $0 }
                    + ["% w/v = \(v.written(g, in: gUnit)) / \(w(ml)) mL × 100 = \(w(p))%"]
                return BioLabResult(answers: [v.answer("pct", base: p, unitText: "% w/v")], formula: v.mode.formula,
                                    working: "% w/v = \(v.written(g, in: gUnit)) / \(w(ml)) mL × 100", steps: steps)
            }
        })

    static let convert = BioLabCalculator(
        id: "convert", page: "solutions", title: "Unit conversion",
        line: "Between M, mM, µM and nM; L, mL and µL; g, mg and µg; and more.",
        formula: "Each prefix step (milli, micro, nano) is a factor of 1,000",
        modeLabel: "Convert",
        fields: [
            BioLabField("cIn", "", "Concentration", quantity: .concentration, unit: "µM", initial: "250"),
            BioLabField("cOut", "", "In", quantity: .concentration, unit: "mM"),
            BioLabField("vIn", "", "Volume", quantity: .volume, unit: "mL", initial: "2.5"),
            BioLabField("vOut", "", "In", quantity: .volume, unit: "µL"),
            BioLabField("mIn", "", "Mass", quantity: .mass, unit: "g", initial: "0.5"),
            BioLabField("mOut", "", "In", quantity: .mass, unit: "mg"),
            BioLabField("lIn", "", "Length", quantity: .length, unit: "mm", initial: "1.8"),
            BioLabField("lOut", "", "In", quantity: .length, unit: "µm"),
            BioLabField("nIn", "", "Amount", quantity: .amount, unit: "mol", initial: "0.125"),
            BioLabField("nOut", "", "In", quantity: .amount, unit: "mmol"),
        ],
        modes: [
            BioLabMode("conc", "Concentration", "", inputs: ["cIn"], unknown: "cOut"),
            BioLabMode("volume", "Volume", "", inputs: ["vIn"], unknown: "vOut"),
            BioLabMode("mass", "Mass", "", inputs: ["mIn"], unknown: "mOut"),
            BioLabMode("length", "Length", "", inputs: ["lIn"], unknown: "lOut"),
            BioLabMode("amount", "Amount", "", inputs: ["nIn"], unknown: "nOut"),
        ],
        compute: { v in
            let i = v.mode.inputs[0], o = v.mode.unknown ?? ""
            let n = try v.typed(i)
            let base = try v.value(i)
            guard let from = v.unit(i), let to = v.unit(o) else { throw BioLabProblem(o, "Choose a unit.") }
            let d = min(v.setting, n.sigFigs ?? v.setting)
            let r = from.factor / to.factor
            let rule = r >= 1
                ? "1 \(from.symbol) = \(BioLabFormat.plain(r)) \(to.symbol), so multiply by \(BioLabFormat.plain(r))."
                : "1 \(to.symbol) = \(BioLabFormat.plain(1 / r)) \(from.symbol), so divide by \(BioLabFormat.plain(1 / r))."
            let a = v.answer(o, base: base, digits: d)
            return BioLabResult(answers: [a], formula: "1 \(from.symbol) = \(BioLabFormat.plain(r)) \(to.symbol)",
                                working: "\(v.shown(i)) = \(a.full)",
                                steps: [rule, "\(v.shown(i)) × \(BioLabFormat.plain(r)) = \(a.full)"],
                                precision: "Rounded to \(d) significant figure\(d == 1 ? "" : "s"), as many as \(v.shown(i)) has: a change of unit changes no precision.")
        })

    // MARK: - Microscopy

    private static let objectives = [BioLabPreset(label: "4× scanning", text: "4"), BioLabPreset(label: "10× low power", text: "10"),
                                     BioLabPreset(label: "40× high power", text: "40"), BioLabPreset(label: "100× oil immersion", text: "100")]

    static let totalMagnification = BioLabCalculator(
        id: "totalMag", page: "microscopy", title: "Total magnification",
        line: "The eyepiece and the objective together.",
        formula: "total = ocular × objective",
        fields: [
            BioLabField("ocular", "", "Ocular (eyepiece) lens", unit: "×", initial: "10", exact: true,
                        presets: [BioLabPreset(label: "10×", text: "10"), BioLabPreset(label: "15×", text: "15")]),
            BioLabField("objective", "", "Objective lens", unit: "×", initial: "40", exact: true, presets: objectives),
            BioLabField("total", "", "Total magnification", unit: "×"),
        ],
        modes: [BioLabMode("total", "", "total = ocular × objective", inputs: ["ocular", "objective"], unknown: "total")],
        compute: { v in
            let o = try v.positive("ocular"), b = try v.positive("objective")
            let t = o * b
            let a = plainAnswer("", "Total magnification", t, "×", v.digits, field: "total")
            return BioLabResult(answers: [a], formula: v.mode.formula, working: "total = \(v.shown("ocular")) × \(v.shown("objective")) = \(a.full)",
                                steps: ["The ocular magnifies the objective's image again: \(BioLabFormat.plain(o)) × \(BioLabFormat.plain(b)) = \(BioLabFormat.plain(t))."],
                                notes: ["The specimen looks \(BioLabFormat.plain(t)) times as wide as it is."])
        })

    static let fieldOfView = BioLabCalculator(
        id: "fov", page: "microscopy", title: "Field of view",
        line: "How wide the circle you see is at another magnification.",
        formula: "FOV₂ = FOV₁ × (M₁ / M₂)",
        fields: [
            BioLabField("fov1", "FOV₁", "Field of view at the first magnification", quantity: .length, unit: "mm", initial: "1.8"),
            BioLabField("m1", "M₁", "First magnification", unit: "×", initial: "100", exact: true),
            BioLabField("m2", "M₂", "New magnification", unit: "×", initial: "400", exact: true),
            BioLabField("fov2", "FOV₂", "New field of view", quantity: .length, unit: "mm"),
        ],
        modes: [BioLabMode("fov", "", "FOV₂ = FOV₁ × (M₁ / M₂)", inputs: ["fov1", "m1", "m2"], unknown: "fov2")],
        compute: { v in
            let f1 = try v.positive("fov1"), m1 = try v.positive("m1"), m2 = try v.positive("m2")
            let f2 = f1 * m1 / m2
            let u1 = v.unit("fov1")
            let a = v.answer("fov2", base: f2, alsoIn: v.out("fov2")?.symbol == "µm" ? "mm" : "µm")
            let times = m2 / m1
            return BioLabResult(answers: [a], formula: v.mode.formula,
                                working: "FOV₂ = \(v.shown("fov1")) × (\(BioLabFormat.plain(m1)) / \(BioLabFormat.plain(m2)))",
                                steps: ["FOV₂ = \(v.shown("fov1")) × \(w(m1 / m2)) = \(v.written(f2, in: u1))"] + (v.out("fov2")?.symbol != u1?.symbol ? ["\(v.written(f2, in: u1)) = \(a.full)"] : []),
                                notes: [times >= 1 ? "\(BioLabFormat.plain(times)) times the magnification shows 1/\(BioLabFormat.plain(times)) of the width: the field shrinks as magnification rises."
                                    : "Less magnification shows a wider field."])
        })

    static let specimen = BioLabCalculator(
        id: "specimen", page: "microscopy", title: "Specimen size",
        line: "Estimate a cell's size from how many fit across the field.",
        formula: "size ≈ FOV / number that fit across",
        fields: [
            BioLabField("fov", "FOV", "Field of view (diameter)", quantity: .length, unit: "µm", initial: "450"),
            BioLabField("count", "", "How many fit across", unit: "across", initial: "5", exact: true),
            BioLabField("size", "", "Size of one", quantity: .length, unit: "µm"),
        ],
        modes: [BioLabMode("specimen", "", "size ≈ FOV / number that fit across", inputs: ["fov", "count"], unknown: "size")],
        compute: { v in
            let fov = try v.positive("fov"), n = try v.positive("count")
            let s = fov / n
            let a = v.answer("size", base: s)
            return BioLabResult(answers: [a], formula: v.mode.formula, working: "size ≈ \(v.shown("fov")) / \(BioLabFormat.plain(n))",
                                steps: ["\(v.shown("fov")) / \(BioLabFormat.plain(n)) = \(a.full)"],
                                notes: ["Count the cells end to end along a diameter of the field; it is an estimate."])
        })

    static let magnification = BioLabCalculator(
        id: "magnification", page: "microscopy", title: "Magnification",
        line: "From a drawing or photo: image size, actual size and magnification.",
        formula: "M = image size / actual size",
        fields: [
            BioLabField("image", "", "Size in the image", quantity: .length, unit: "mm", initial: "45"),
            BioLabField("actual", "", "Actual size", quantity: .length, unit: "µm", initial: "15"),
            BioLabField("mag", "M", "Magnification", unit: "×", initial: "3000"),
        ],
        modes: [
            BioLabMode("mag", "M", "M = image size / actual size", inputs: ["image", "actual"], unknown: "mag"),
            BioLabMode("image", "Image", "image size = M × actual size", inputs: ["actual", "mag"], unknown: "image"),
            BioLabMode("actual", "Actual", "actual size = image size / M", inputs: ["image", "mag"], unknown: "actual"),
        ],
        compute: { v in
            switch v.mode.id {
            case "image":
                let actual = try v.positive("actual"), m = try v.positive("mag")
                let img = actual * m
                let a = v.answer("image", base: img)
                return BioLabResult(answers: [a], formula: v.mode.formula, working: "image size = \(BioLabFormat.plain(m)) × \(v.shown("actual"))",
                                    steps: ["\(BioLabFormat.plain(m)) × \(v.shown("actual")) = \(v.written(img, in: v.unit("actual")))",
                                            "= \(a.full)"])
            case "actual":
                let img = try v.positive("image"), m = try v.positive("mag")
                let actual = img / m
                let a = v.answer("actual", base: actual)
                return BioLabResult(answers: [a], formula: v.mode.formula, working: "actual size = \(v.shown("image")) / \(BioLabFormat.plain(m))",
                                    steps: ["\(v.shown("image")) / \(BioLabFormat.plain(m)) = \(v.written(actual, in: v.unit("image")))", "= \(a.full)"])
            default:
                let img = try v.positive("image"), actual = try v.positive("actual")
                let m = img / actual
                let au = v.unit("actual")
                var steps = [v.conversion("image", to: au).map { "Same unit: \($0)" }].compactMap { $0 }
                let a = plainAnswer("M", "Magnification", m, "×", v.digits, field: "mag")
                steps.append("M = \(v.written(img, in: au)) / \(v.shown("actual")) = \(a.full)")
                return BioLabResult(answers: [a], formula: v.mode.formula, working: "M = \(v.written(img, in: au)) / \(v.shown("actual"))", steps: steps,
                                    notes: ["Put both sizes in the same unit before dividing: magnification has no unit."])
            }
        })

    static let scaleBar = BioLabCalculator(
        id: "scaleBar", page: "microscopy", title: "Scale bar",
        line: "How long to draw a scale bar on an image at a known magnification.",
        formula: "bar length on the image = M × length it stands for",
        fields: [
            BioLabField("mag", "M", "Magnification of the image", unit: "×", initial: "400", exact: true),
            BioLabField("bar", "", "Length the bar stands for", quantity: .length, unit: "µm", initial: "50"),
            BioLabField("barImage", "", "Bar length on the image", quantity: .length, unit: "mm"),
        ],
        modes: [BioLabMode("scaleBar", "", "bar length on the image = M × length it stands for", inputs: ["mag", "bar"], unknown: "barImage")],
        compute: { v in
            let m = try v.positive("mag"), bar = try v.positive("bar")
            let len = m * bar
            let a = v.answer("barImage", base: len)
            // (a bar near 20 mm reads well: the largest 1, 2 or 5 × 10ⁿ that fits)
            let target = 0.02 / m
            let e = floor(log10(target)), mant = target / pow(10, e)
            let nice = (mant >= 5 ? 5 : (mant >= 2 ? 2 : 1)) * pow(10, e)
            let niceUnit = BioLabQuantity.length.units.first { nice / $0.factor >= 1 && nice / $0.factor < 1000 } ?? BioLabQuantity.length.units[3]
            let niceText = BioLabFormat.plain(nice / niceUnit.factor) + " " + niceUnit.symbol
            return BioLabResult(answers: [a], formula: v.mode.formula, working: "bar = \(BioLabFormat.plain(m)) × \(v.shown("bar"))",
                                steps: ["\(BioLabFormat.plain(m)) × \(v.shown("bar")) = \(v.written(len, in: v.unit("bar")))", "= \(a.full)"],
                                notes: ["For a bar about 20 mm long at \(BioLabFormat.plain(m))×, label it \(niceText) (\(w(m * nice * 1000)) mm)."],
                                scaleBar: (label: v.shown("bar"), millimetres: len * 1000))
        })

    // MARK: - Cells

    static let surfaceArea = BioLabCalculator(
        id: "sav", page: "cells", title: "Surface area and volume",
        line: "SA, V and SA:V for the AP sheet's shapes, and how SA:V falls as size grows.",
        formula: "SA:V = surface area / volume",
        modeLabel: "Shape",
        fields: [
            BioLabField("r", "r", "Radius", quantity: .length, unit: "µm", initial: "1"),
            BioLabField("s", "s", "Side", quantity: .length, unit: "µm", initial: "2"),
            BioLabField("l", "l", "Length", quantity: .length, unit: "µm", initial: "3"),
            BioLabField("w", "w", "Width", quantity: .length, unit: "µm", initial: "2"),
            BioLabField("h", "h", "Height", quantity: .length, unit: "µm", initial: "1"),
        ],
        modes: [
            BioLabMode("sphere", "Sphere", "SA = 4πr²,  V = 4/3 πr³", inputs: ["r"]),
            BioLabMode("cube", "Cube", "SA = 6s²,  V = s³", inputs: ["s"]),
            BioLabMode("rect", "Box", "SA = 2lh + 2lw + 2wh,  V = lwh", inputs: ["l", "w", "h"]),
            BioLabMode("cylinder", "Cylinder", "SA = 2πrh + 2πr²,  V = πr²h", inputs: ["r", "h"]),
        ],
        compute: { v in
            let main = v.mode.inputs[0]
            guard let u = v.unit(main) else { throw BioLabProblem(main, "Choose a unit.") }
            var steps = v.mode.inputs.dropFirst().compactMap { v.conversion($0, to: u) }
            func len(_ id: String) throws -> Double { try v.positive(id) / u.factor }
            let us = u.symbol
            let sa: Double, vol: Double, working: String
            switch v.mode.id {
            case "cube":
                let s = try len("s")
                sa = 6 * s * s
                vol = s * s * s
                steps += ["SA = 6s² = 6 × (\(w(s)) \(us))² = \(w(sa)) \(us)²", "V = s³ = (\(w(s)) \(us))³ = \(w(vol)) \(us)³"]
                working = "SA:V = 6(\(w(s)))² / (\(w(s)))³"
            case "rect":
                let l = try len("l"), wd = try len("w"), h = try len("h")
                sa = 2 * l * h + 2 * l * wd + 2 * wd * h
                vol = l * wd * h
                steps += ["SA = 2lh + 2lw + 2wh = 2(\(w(l)))(\(w(h))) + 2(\(w(l)))(\(w(wd))) + 2(\(w(wd)))(\(w(h))) = \(w(sa)) \(us)²",
                          "V = lwh = \(w(l)) × \(w(wd)) × \(w(h)) = \(w(vol)) \(us)³"]
                working = "SA:V = \(w(sa)) \(us)² / \(w(vol)) \(us)³"
            case "cylinder":
                let r = try len("r"), h = try len("h")
                sa = 2 * Double.pi * r * h + 2 * Double.pi * r * r
                vol = Double.pi * r * r * h
                steps += ["SA = 2πrh + 2πr² = 2π(\(w(r)))(\(w(h))) + 2π(\(w(r)))² = \(w(sa)) \(us)²",
                          "V = πr²h = π(\(w(r)))²(\(w(h))) = \(w(vol)) \(us)³"]
                working = "SA:V = \(w(sa)) \(us)² / \(w(vol)) \(us)³"
            default:
                let r = try len("r")
                sa = 4 * Double.pi * r * r
                vol = 4.0 / 3.0 * Double.pi * r * r * r
                steps += ["SA = 4πr² = 4π × (\(w(r)) \(us))² = \(w(sa)) \(us)²", "V = 4/3 πr³ = 4/3 π × (\(w(r)) \(us))³ = \(w(vol)) \(us)³"]
                working = "SA:V = 4π(\(w(r)))² / (4/3 π(\(w(r)))³)"
            }
            let ratio = sa / vol, d = v.digits
            steps.append("SA:V = \(w(sa)) / \(w(vol)) = \(BioLabFormat.sig(ratio, d)) \(us)⁻¹")
            // (every length scaled by k: SA by k², V by k³, so SA:V by 1/k)
            let size = try len(main)
            let points = (0...40).map { i -> BioLabPoint in
                let k = 0.25 + 3.75 * Double(i) / 40
                return BioLabPoint(x: k * size, y: ratio / k)
            }
            return BioLabResult(answers: [plainAnswer("SA:V", "Surface area to volume", ratio, "\(us)⁻¹", d),
                                          plainAnswer("SA", "Surface area", sa, "\(us)²", d),
                                          plainAnswer("V", "Volume", vol, "\(us)³", d)],
                                formula: v.mode.formula, working: working, steps: steps,
                                notes: ["Double every length and SA:V halves: a small cell has more membrane for each unit of volume it must supply."],
                                series: [BioLabSeries(name: "SA:V", points: points)], marks: [BioLabPoint(x: size, y: ratio)])
        })

    static let waterPotential = BioLabCalculator(
        id: "water", page: "cells", title: "Water potential",
        line: "Solute potential from concentration, and which way water moves.",
        formula: "Ψ = ΨP + ΨS,   ΨS = −iCRT",
        fields: [
            BioLabField("i", "i", "Ionisation constant", initial: "1", exact: true,
                        presets: [BioLabPreset(label: "Sucrose or glucose: 1", text: "1"), BioLabPreset(label: "NaCl or KCl: 2", text: "2"),
                                  BioLabPreset(label: "CaCl₂: 3", text: "3")],
                        hint: "How many particles each unit makes in water."),
            BioLabField("C", "C", "Molar concentration", quantity: .concentration, unit: "M", initial: "0.1"),
            BioLabField("T", "T", "Temperature", unit: "°C", initial: "22", exact: true),
            BioLabField("P", "ΨP", "Pressure potential", unit: "bar", initial: "0", exact: true),
            BioLabField("out", "", "Ψ outside (optional)", unit: "bar", exact: true, optional: true,
                        hint: "The surrounding solution's water potential, to see which way water moves."),
        ],
        modes: [BioLabMode("water", "", "ΨS = −iCRT,   Ψ = ΨP + ΨS", inputs: ["i", "C", "T", "P", "out"])],
        compute: { v in
            let i = try v.positive("i"), C = try v.nonNegative("C"), t = try v.value("T"), p = try v.value("P")
            guard t > -273 else { throw BioLabProblem("T", "The temperature must be above −273 °C.") }
            let K = t + 273, R = gasConstant
            let psiS = -i * C * R * K, psi = p + psiS
            let out = try v.optionalValue("out")
            let d = v.digits
            var steps = [v.conversion("C", to: unit(.concentration, "M"))].compactMap { $0 }
            steps += ["T = \(BioLabFormat.plain(t)) °C + 273 = \(BioLabFormat.plain(K)) K",
                      "ΨS = −iCRT = −(\(BioLabFormat.plain(i)))(\(w(C)) mol/L)(0.0831 L·bar/(mol·K))(\(BioLabFormat.plain(K)) K) = \(w(psiS)) bar",
                      "Ψ = ΨP + ΨS = \(BioLabFormat.plain(p)) bar + (\(w(psiS)) bar) = \(w(psi)) bar"]
            var notes = ["Water moves from higher (less negative) Ψ to lower Ψ. Pure water in an open beaker has Ψ = 0."]
            if let out {
                let o = BioLabFormat.sig(out, d), inside = BioLabFormat.sig(psi, d)
                if abs(out - psi) < 1e-9 {
                    notes.insert("Outside and inside are equal (\(o) bar): no net movement of water.", at: 0)
                } else if out > psi {
                    notes.insert("Outside (\(o) bar) is higher than inside (\(inside) bar): water moves into the cell.", at: 0)
                } else {
                    notes.insert("Outside (\(o) bar) is lower than inside (\(inside) bar): water moves out of the cell.", at: 0)
                }
            }
            return BioLabResult(answers: [plainAnswer("ΨS", "Solute potential", psiS, "bar", d), plainAnswer("Ψ", "Water potential", psi, "bar", d)],
                                formula: v.mode.formula,
                                working: "ΨS = −(\(BioLabFormat.plain(i)))(\(w(C)) M)(0.0831)(\(BioLabFormat.plain(K)) K)", steps: steps, notes: notes)
        })

    static let hemocytometer = BioLabCalculator(
        id: "hemocytometer", page: "cells", title: "Hemocytometer count",
        line: "Cells per mL from the counts in a hemocytometer's large squares.",
        formula: "cells/mL = mean count per large square × dilution × 10⁴",
        fields: [
            BioLabField("counts", "", "Cells per large square", kind: .list, initial: "42", exact: true,
                        hint: "One count, or several separated by commas: their mean is used."),
            BioLabField("dil", "", "Dilution factor", initial: "2", exact: true, hint: "2 if the sample was mixed 1:1 with trypan blue."),
        ],
        modes: [BioLabMode("hemocytometer", "", "cells/mL = mean count per large square × dilution × 10⁴", inputs: ["counts", "dil"])],
        compute: { v in
            let counts = try v.list("counts")
            guard counts.allSatisfy({ $0 >= 0 }) else { throw BioLabProblem("counts", "Counts can't be negative.") }
            let dil = try v.positive("dil")
            let mean = counts.reduce(0, +) / Double(counts.count)
            let cells = mean * dil * 1e4
            let d = v.digits
            return BioLabResult(answers: [plainAnswer("", "Cells per mL", cells, "cells/mL", d)], formula: v.mode.formula,
                                working: "cells/mL = \(w(mean)) × \(BioLabFormat.plain(dil)) × 10⁴",
                                steps: [counts.count == 1 ? "One square counted: \(w(mean)) cells"
                                            : "Mean count: (\(counts.map { BioLabFormat.plain($0) }.joined(separator: " + "))) / \(counts.count) = \(w(mean)) per square",
                                        "A large square is 1 mm × 1 mm × 0.1 mm deep: 0.1 µL, which is 10⁻⁴ mL. So × 10⁴ gives cells per mL.",
                                        "\(w(mean)) × \(BioLabFormat.plain(dil)) × 10⁴ = \(BioLabFormat.sig(cells, d)) cells/mL"])
        })

    // MARK: - Statistics

    /// A group's statistics (n ≥ 2): mean, median, modes, range, sample standard deviation (n − 1) and standard error.
    static func summary(_ name: String, _ xs: [Double]) -> BioLabSummary {
        let n = Double(xs.count)
        let mean = xs.reduce(0, +) / n
        let sorted = xs.sorted()
        let median = xs.count % 2 == 1 ? sorted[xs.count / 2] : (sorted[xs.count / 2 - 1] + sorted[xs.count / 2]) / 2
        var counts: [Double: Int] = [:]
        for x in xs { counts[x, default: 0] += 1 }
        let top = counts.values.max() ?? 0
        let modes = top > 1 ? counts.filter { $0.value == top }.keys.sorted() : []
        let ss = xs.reduce(0) { $0 + ($1 - mean) * ($1 - mean) }
        let sd = xs.count > 1 ? (ss / (n - 1)).squareRoot() : 0
        return BioLabSummary(name: name, values: xs, mean: mean, median: median, modes: modes, range: (sorted.last ?? 0) - (sorted.first ?? 0),
                             sd: sd, se: sd / n.squareRoot())
    }

    static let statistics = BioLabCalculator(
        id: "stats", page: "stats", title: "Descriptive statistics",
        line: "Mean, median, mode, range, standard deviation, standard error and ±2 SE error bars.",
        formula: "x̄ = Σx / n,   s = √(Σ(x − x̄)² / (n − 1)),   SE = s / √n",
        fields: [
            BioLabField("aName", "", "Group 1", kind: .text, initial: "Control"),
            BioLabField("a", "", "Data", kind: .list, initial: "12.1, 13.4, 11.8, 12.9, 13.0", hint: "Numbers separated by commas or spaces."),
            BioLabField("bName", "", "Group 2 (optional)", kind: .text, initial: "Treatment", optional: true),
            BioLabField("b", "", "Data", kind: .list, initial: "14.2, 13.9, 14.8, 14.5, 14.1", optional: true),
        ],
        modes: [BioLabMode("stats", "", "x̄ = Σx / n,   s = √(Σ(x − x̄)² / (n − 1)),   SE = s / √n", inputs: ["aName", "a", "bName", "b"])],
        compute: { v in
            let a = try v.list("a", min: 2)
            let b: [Double]? = try v.text("b").isEmpty ? nil : v.list("b", min: 2)
            let nameA = v.text("aName").isEmpty ? "Group 1" : v.text("aName")
            let nameB = v.text("bName").isEmpty ? "Group 2" : v.text("bName")
            let d = v.digits
            var groups = [(summary(nameA, a), v.listDecimals("a"))]
            if let b { groups.append((summary(nameB, b), v.listDecimals("b"))) }
            func f(_ x: Double, _ dec: Int) -> String { BioLabFormat.fixed(x, decimals: dec) }
            let labels = ["n", "Mean x̄", "Median", "Mode", "Range", "Standard deviation s", "Standard error SE", "x̄ ± 2SE"]
            var rows = labels.map { [$0] }
            for (s, dec) in groups {
                let even = s.values.count % 2 == 0
                rows[0].append("\(s.values.count)")
                rows[1].append(f(s.mean, dec + 1))
                rows[2].append(f(s.median, dec + (even ? 1 : 0)))
                rows[3].append(s.modes.isEmpty ? "none" : s.modes.map { f($0, dec) }.joined(separator: ", "))
                rows[4].append(f(s.range, dec))
                rows[5].append(BioLabFormat.sig(s.sd, d))
                rows[6].append(BioLabFormat.sig(s.se, d))
                rows[7].append("\(f(s.low, dec + 1)) to \(f(s.high, dec + 1))")
            }
            let (s0, dec0) = groups[0]
            let n = s0.values.count, sum = a.reduce(0, +), ss = a.reduce(0) { $0 + ($1 - s0.mean) * ($1 - s0.mean) }
            var steps = ["\(nameA): Σx = \(w(sum)), n = \(n), so x̄ = \(w(sum)) / \(n) = \(f(s0.mean, dec0 + 1))",
                         "Σ(x − x̄)² = \(w(ss))",
                         "s = √(\(w(ss)) / \(n - 1)) = \(BioLabFormat.sig(s0.sd, d))",
                         "SE = s / √n = \(BioLabFormat.sig(s0.sd, d)) / √\(n) = \(BioLabFormat.sig(s0.se, d))",
                         "x̄ ± 2SE = \(f(s0.mean, dec0 + 1)) ± \(f(2 * s0.se, dec0 + 1)), from \(f(s0.low, dec0 + 1)) to \(f(s0.high, dec0 + 1))"]
            var notes = ["±2 SE is about a 95% confidence interval for the mean: the AP sheet's error bars."]
            if groups.count == 2 {
                let (s1, dec1) = groups[1]
                steps.append("\(nameB): x̄ = \(f(s1.mean, dec1 + 1)), s = \(BioLabFormat.sig(s1.sd, d)), SE = \(BioLabFormat.sig(s1.se, d)), x̄ ± 2SE from \(f(s1.low, dec1 + 1)) to \(f(s1.high, dec1 + 1))")
                let overlap = s0.low <= s1.high && s1.low <= s0.high
                notes.insert(overlap
                             ? "The ±2 SE error bars overlap: the difference between the means may be due to chance."
                             : "The ±2 SE error bars do not overlap: the difference between the means is likely significant.", at: 0)
            }
            let mean = BioLabAnswer(symbol: "x̄", label: "Mean of \(nameA)", value: s0.mean, unit: "", text: f(s0.mean, dec0 + 1))
            return BioLabResult(answers: [mean, plainAnswer("s", "Standard deviation", s0.sd, "", d), plainAnswer("SE", "Standard error", s0.se, "", d)],
                                formula: v.mode.formula, working: "x̄ = \(w(sum)) / \(n) = \(f(s0.mean, dec0 + 1))", steps: steps, notes: notes,
                                table: BioLabTable(columns: [""] + groups.map(\.0.name), rows: rows), summaries: groups.map(\.0),
                                precision: "The mean, median and error bars are given to one more decimal place than the data; s and SE to \(d) significant figures.")
        })

    static let chiSquare = BioLabCalculator(
        id: "chisq", page: "stats", title: "Chi-square",
        line: "Do the counts fit what was expected? The AP goodness-of-fit test.",
        formula: "χ² = Σ (o − e)² / e",
        modeLabel: "Expected",
        fields: [
            BioLabField("names", "", "Categories (optional)", kind: .text, initial: "Round yellow, Round green, Wrinkled yellow, Wrinkled green", optional: true),
            BioLabField("o", "o", "Observed counts", kind: .list, initial: "315, 108, 101, 32", exact: true),
            BioLabField("ratio", "", "Expected ratio", kind: .list, initial: "9:3:3:1", exact: true),
            BioLabField("e", "e", "Expected counts", kind: .list, initial: "312.75, 104.25, 104.25, 34.75", exact: true),
        ],
        modes: [
            BioLabMode("ratio", "From a ratio", "χ² = Σ (o − e)² / e", inputs: ["names", "o", "ratio"]),
            BioLabMode("counts", "As counts", "χ² = Σ (o − e)² / e", inputs: ["names", "o", "e"]),
        ],
        compute: { v in
            let o = try v.list("o", min: 2)
            guard o.allSatisfy({ $0 >= 0 }) else { throw BioLabProblem("o", "Counts can't be negative.") }
            let total = o.reduce(0, +)
            var steps: [String] = []
            let e: [Double]
            if v.mode.id == "counts" {
                e = try v.list("e", min: 2)
                guard e.count == o.count else { throw BioLabProblem("e", "There are \(e.count) expected counts but \(o.count) observed.") }
                guard e.allSatisfy({ $0 > 0 }) else { throw BioLabProblem("e", "Every expected count must be more than 0.") }
            } else {
                let r = try v.list("ratio", min: 2)
                guard r.count == o.count else { throw BioLabProblem("ratio", "The ratio has \(r.count) parts but there are \(o.count) observed counts.") }
                guard r.allSatisfy({ $0 > 0 }) else { throw BioLabProblem("ratio", "Every part of the ratio must be more than 0.") }
                e = BioChiSquare.expected(ratio: r, total: total)
                let parts = r.reduce(0, +)
                steps.append("Total observed: \(BioLabFormat.plain(total)). Expected = total × part / \(BioLabFormat.plain(parts)): "
                             + zip(r, e).map { "\(BioLabFormat.plain(total)) × \(BioLabFormat.plain($0))/\(BioLabFormat.plain(parts)) = \(BioLabFormat.plain($1))" }.joined(separator: "; "))
            }
            guard total > 0 else { throw BioLabProblem("o", "The observed counts add up to 0.") }
            let t = BioChiSquare.test(observed: o, expected: e)
            var names = v.names("names")
            var notes: [String] = []
            if names.count != o.count {
                if !names.isEmpty { notes.append("There are \(names.count) names for \(o.count) counts, so the categories are numbered.") }
                names = (1...o.count).map { "Class \($0)" }
            }
            let d = v.digits
            let eText = e.map { BioLabFormat.plain($0) }
            var rows = (0..<o.count).map { [names[$0], BioLabFormat.plain(o[$0]), eText[$0], BioLabFormat.sig(t.terms[$0], 3)] }
            rows.append(["Total", BioLabFormat.plain(total), BioLabFormat.plain(e.reduce(0, +)), "χ² = " + BioLabFormat.sig(t.chi2, d)])
            steps.append("Each term (o − e)² / e: " + (0..<o.count).map { "(\(BioLabFormat.plain(o[$0])) − \(eText[$0]))² / \(eText[$0]) = \(BioLabFormat.sig(t.terms[$0], 3))" }.joined(separator: "; "))
            steps.append("χ² = " + t.terms.map { BioLabFormat.sig($0, 3) }.joined(separator: " + ") + " = \(BioLabFormat.sig(t.chi2, d))")
            steps.append("df = categories − 1 = \(o.count) − 1 = \(t.df). The critical value at p = 0.05 is \(String(format: "%.2f", t.critical)).")
            steps.append(t.conclusion)
            notes.insert(t.conclusion, at: 0)
            notes.insert(t.reject ? "A deviation this large would happen by chance less than 5% of the time."
                         : "A deviation this large could happen by chance more than 5% of the time.", at: 1)
            if e.contains(where: { $0 < 5 }) { notes.append("An expected count below 5 makes the test unreliable.") }
            let pText = t.p < 0.001 ? "< 0.001" : BioLabFormat.sig(t.p, 2)
            return BioLabResult(answers: [plainAnswer("χ²", "Chi-square", t.chi2, "", d),
                                          BioLabAnswer(symbol: "df", label: "Degrees of freedom", value: Double(t.df), unit: "", text: "\(t.df)"),
                                          BioLabAnswer(symbol: "", label: "Critical value (p = 0.05)", value: t.critical, unit: "", text: String(format: "%.2f", t.critical)),
                                          BioLabAnswer(symbol: "p", label: "p-value", value: t.p, unit: "", text: pText)],
                                formula: v.mode.formula, working: "χ² = " + t.terms.map { BioLabFormat.sig($0, 3) }.joined(separator: " + "),
                                steps: steps, notes: notes,
                                table: BioLabTable(columns: ["Category", "Observed o", "Expected e", "(o − e)² / e"], rows: rows), highlight: t.df)
        })

    // MARK: - Populations

    static let rate = BioLabCalculator(
        id: "rate", page: "ecology", title: "Rate",
        line: "How fast something changed: oxygen made, mass lost, height grown.",
        formula: "rate = ΔY / Δt",
        fields: [
            BioLabField("yName", "", "What was measured (its unit)", kind: .text, initial: "mL O₂"),
            BioLabField("y1", "Y₁", "Value at the start", initial: "0.4"),
            BioLabField("y2", "Y₂", "Value at the end", initial: "2.6"),
            BioLabField("t1", "t₁", "Start time", quantity: .time, unit: "min", initial: "0"),
            BioLabField("t2", "t₂", "End time", quantity: .time, unit: "min", initial: "10"),
            BioLabField("rate", "", "Rate", quantity: .time, unit: "min", per: true),
        ],
        modes: [BioLabMode("rate", "", "rate = ΔY / Δt = (Y₂ − Y₁) / (t₂ − t₁)", inputs: ["yName", "y1", "y2", "t1", "t2"], unknown: "rate")],
        compute: { v in
            let y1 = try v.value("y1"), y2 = try v.value("y2"), t1 = try v.value("t1"), t2 = try v.value("t2")
            guard t2 != t1 else { throw BioLabProblem("t2", "The end time must differ from the start time.") }
            guard let u = v.out("rate") else { throw BioLabProblem("rate", "Choose a unit.") }
            let name = v.text("yName")
            let dy = y2 - y1, dt = (t2 - t1) / u.factor, r = dy / dt
            var steps = [v.conversion("t1", to: u), v.conversion("t2", to: u)].compactMap { $0 }
            steps += ["ΔY = Y₂ − Y₁ = \(v.shown("y2")) − \(v.shown("y1")) = \(w(dy))\(name.isEmpty ? "" : " " + name)",
                      "Δt = t₂ − t₁ = \(w(dt)) \(u.symbol)",
                      "rate = \(w(dy)) / \(w(dt)) \(u.symbol) = \(BioLabFormat.sig(r, v.digits)) \(name.isEmpty ? "per \(u.symbol)" : "\(name)/\(u.symbol)")"]
            let a = BioLabAnswer(symbol: "ΔY/Δt", label: "Rate", value: r, unit: name.isEmpty ? "per \(u.symbol)" : "\(name)/\(u.symbol)",
                                 text: BioLabFormat.sig(r, v.digits), field: "rate")
            return BioLabResult(answers: [a], formula: v.mode.formula, working: "rate = (\(v.shown("y2")) − \(v.shown("y1"))) / \(w(dt)) \(u.symbol)",
                                steps: steps, notes: r < 0 ? ["A negative rate means Y fell over the interval."] : ["On a graph, this is the slope between the two points."])
        })

    static let growth = BioLabCalculator(
        id: "growth", page: "ecology", title: "Population growth",
        line: "Exponential and logistic growth, as the AP sheet writes them.",
        formula: "dN/dt = rₘₐₓN,   dN/dt = rₘₐₓN (K − N) / K",
        modeLabel: "Model",
        fields: [
            BioLabField("r", "rₘₐₓ", "Maximum per capita growth rate", initial: "0.5", hint: "Per unit of time: per day, per year…"),
            BioLabField("N", "N", "Population size", unit: "individuals", initial: "100", exact: true),
            BioLabField("K", "K", "Carrying capacity", unit: "individuals", initial: "1000", exact: true),
        ],
        modes: [
            BioLabMode("exponential", "Exponential", "dN/dt = rₘₐₓN", inputs: ["r", "N"]),
            BioLabMode("logistic", "Logistic", "dN/dt = rₘₐₓN (K − N) / K", inputs: ["r", "N", "K"]),
        ],
        compute: { v in
            let r = try v.value("r"), n = try v.nonNegative("N")
            let logistic = v.mode.id == "logistic"
            let K = try logistic ? v.positive("K") : 0
            let d = v.digits
            let rate = logistic ? r * n * (K - n) / K : r * n
            var steps = logistic
                ? ["(K − N) / K = (\(BioLabFormat.plain(K)) − \(BioLabFormat.plain(n))) / \(BioLabFormat.plain(K)) = \(w((K - n) / K))",
                   "dN/dt = \(BioLabFormat.plain(r)) × \(BioLabFormat.plain(n)) × \(w((K - n) / K)) = \(BioLabFormat.sig(rate, d))"]
                : ["dN/dt = \(BioLabFormat.plain(r)) × \(BioLabFormat.plain(n)) = \(BioLabFormat.sig(rate, d))"]
            steps.append("That is \(BioLabFormat.sig(rate, d)) individuals per unit of time, right now.")
            // (the curves N(t) from N: e^(rt), and K / (1 + (K − N)/N · e^(−rt)))
            var series: [BioLabSeries] = []
            if r > 0 && n > 0 {
                let tMax = logistic && K > n ? log(99 * (K - n) / n) / r : 4.6 / r
                let cap = logistic ? 1.6 * K : .infinity
                let exp = (0...60).map { i -> BioLabPoint in
                    let t = tMax * Double(i) / 60
                    return BioLabPoint(x: t, y: n * Foundation.exp(r * t))
                }.filter { $0.y <= cap }
                if logistic {
                    let logi = (0...60).map { i -> BioLabPoint in
                        let t = tMax * Double(i) / 60
                        return BioLabPoint(x: t, y: K / (1 + (K - n) / n * Foundation.exp(-r * t)))
                    }
                    series = [BioLabSeries(name: "Logistic", points: logi), BioLabSeries(name: "Exponential", points: exp, dashed: true),
                              BioLabSeries(name: "K", points: [BioLabPoint(x: 0, y: K), BioLabPoint(x: tMax, y: K)], dashed: true)]
                } else {
                    series = [BioLabSeries(name: "Exponential", points: exp)]
                }
            }
            let notes = logistic
                ? ["Growth is fastest at N = K/2 = \(BioLabFormat.plain(K / 2)), where dN/dt = rₘₐₓK/4 = \(BioLabFormat.sig(r * K / 4, d)).",
                   "As N nears K, (K − N)/K nears 0 and growth stops: an S-shaped curve."]
                : ["The bigger the population, the faster it grows: a J-shaped curve."]
            return BioLabResult(answers: [plainAnswer("dN/dt", "Growth rate", rate, "individuals per unit time", d)], formula: v.mode.formula,
                                working: logistic ? "dN/dt = \(BioLabFormat.plain(r)) × \(BioLabFormat.plain(n)) × (\(BioLabFormat.plain(K)) − \(BioLabFormat.plain(n))) / \(BioLabFormat.plain(K))"
                                    : "dN/dt = \(BioLabFormat.plain(r)) × \(BioLabFormat.plain(n))",
                                steps: steps, notes: notes, series: series, marks: [BioLabPoint(x: 0, y: n)])
        })

    static let simpson = BioLabCalculator(
        id: "simpson", page: "ecology", title: "Simpson's diversity index",
        line: "How diverse a community is, from the number of each species.",
        formula: "D = 1 − Σ(n/N)²",
        fields: [
            BioLabField("names", "", "Species (optional)", kind: .text, initial: "Oak, Maple, Birch", optional: true),
            BioLabField("counts", "n", "Individuals of each species", kind: .list, initial: "10, 6, 4", exact: true),
        ],
        modes: [BioLabMode("simpson", "", "D = 1 − Σ(n/N)²", inputs: ["names", "counts"])],
        compute: { v in
            let counts = try v.list("counts")
            guard counts.allSatisfy({ $0 >= 0 }) else { throw BioLabProblem("counts", "Counts can't be negative.") }
            let N = counts.reduce(0, +)
            guard N > 0 else { throw BioLabProblem("counts", "There must be at least one individual.") }
            var names = v.names("names")
            if names.count != counts.count { names = (1...counts.count).map { "Species \($0)" } }
            let ps = counts.map { $0 / N }, sum = ps.reduce(0) { $0 + $1 * $1 }, D = 1 - sum
            let d = v.digits
            var rows = (0..<counts.count).map { [names[$0], BioLabFormat.plain(counts[$0]), w(ps[$0]), w(ps[$0] * ps[$0])] }
            rows.append(["Total N", BioLabFormat.plain(N), "1", "Σ = \(w(sum))"])
            return BioLabResult(answers: [plainAnswer("D", "Simpson's diversity index", D, "", d)], formula: v.mode.formula,
                                working: "D = 1 − [" + counts.map { "(\(BioLabFormat.plain($0))/\(BioLabFormat.plain(N)))²" }.joined(separator: " + ") + "]",
                                steps: ["N = " + counts.map { BioLabFormat.plain($0) }.joined(separator: " + ") + " = \(BioLabFormat.plain(N))",
                                        "Σ(n/N)² = " + ps.map { w($0 * $0) }.joined(separator: " + ") + " = \(w(sum))",
                                        "D = 1 − \(w(sum)) = \(BioLabFormat.sig(D, d))"],
                                notes: ["D runs from 0 (one species) toward 1 (many species, evenly spread).",
                                        "Two individuals picked at random are of different species \(BioLabFormat.sig(D * 100, 2))% of the time."],
                                table: BioLabTable(columns: ["Species", "n", "n/N", "(n/N)²"], rows: rows))
        })

    // MARK: - Molecules

    static let beerLambert = BioLabCalculator(
        id: "beer", page: "molecules", title: "Beer–Lambert law",
        line: "Concentration from absorbance in a spectrophotometer.",
        formula: "A = εlc",
        fields: [
            BioLabField("A", "A", "Absorbance", initial: "0.5"),
            BioLabField("eps", "ε", "Molar absorptivity", unit: "M⁻¹ cm⁻¹", initial: "6220",
                        presets: [BioLabPreset(label: "NADH at 340 nm: 6,220", text: "6220")]),
            BioLabField("l", "l", "Path length", unit: "cm", initial: "1", exact: true),
            BioLabField("c", "c", "Concentration", quantity: .concentration, unit: "M", initial: "8.04e-5"),
        ],
        modes: [
            BioLabMode("c", "c", "c = A / (εl)", inputs: ["A", "eps", "l"], unknown: "c"),
            BioLabMode("A", "A", "A = εlc", inputs: ["eps", "l", "c"], unknown: "A"),
            BioLabMode("eps", "ε", "ε = A / (lc)", inputs: ["A", "l", "c"], unknown: "eps"),
        ],
        compute: { v in
            let molar = unit(.concentration, "M")
            switch v.mode.id {
            case "A":
                let e = try v.positive("eps"), l = try v.positive("l"), c = try v.nonNegative("c")
                let A = e * l * c
                let steps = [v.conversion("c", to: molar)].compactMap { $0 } + ["A = \(v.shown("eps")) × \(v.shown("l")) × \(v.written(c, in: molar)) = \(w(A))"]
                return BioLabResult(answers: [plainAnswer("A", "Absorbance", A, "", v.digits, field: "A")], formula: v.mode.formula,
                                    working: "A = \(v.shown("eps")) × \(v.shown("l")) × \(v.written(c, in: molar))", steps: steps,
                                    notes: ["Absorbance has no unit. Readings above about 1 are less reliable: dilute and multiply back."])
            case "eps":
                let A = try v.nonNegative("A"), l = try v.positive("l"), c = try v.positive("c")
                let e = A / (l * c)
                let steps = [v.conversion("c", to: molar)].compactMap { $0 } + ["ε = \(v.shown("A")) / (\(v.shown("l")) × \(v.written(c, in: molar))) = \(w(e)) M⁻¹ cm⁻¹"]
                return BioLabResult(answers: [plainAnswer("ε", "Molar absorptivity", e, "M⁻¹ cm⁻¹", v.digits, field: "eps")], formula: v.mode.formula,
                                    working: "ε = \(v.shown("A")) / (\(v.shown("l")) × \(v.written(c, in: molar)))", steps: steps)
            default:
                let A = try v.nonNegative("A"), e = try v.positive("eps"), l = try v.positive("l")
                let c = A / (e * l)
                let a = v.answer("c", base: c)
                return BioLabResult(answers: [a], formula: v.mode.formula, working: "c = \(v.shown("A")) / (\(v.shown("eps")) × \(v.shown("l")))",
                                    steps: ["c = A / (εl) = \(v.shown("A")) / (\(v.shown("eps")) × \(v.shown("l"))) = \(w(c)) M", a.alt.map { "\(w(c)) M \($0)" }].compactMap { $0 },
                                    notes: ["Absorbance has no unit. Readings above about 1 are less reliable: dilute and multiply back."])
            }
        })

    static let nucleicAcid = BioLabCalculator(
        id: "a260", page: "molecules", title: "Nucleic acid from A₂₆₀",
        line: "DNA or RNA concentration from absorbance at 260 nm.",
        formula: "c = A₂₆₀ × factor × dilution",
        fields: [
            BioLabField("a260", "A₂₆₀", "Absorbance at 260 nm", initial: "0.25"),
            BioLabField("dil", "", "Dilution factor", initial: "10", exact: true,
                        presets: [BioLabPreset(label: "Undiluted (1)", text: "1"), BioLabPreset(label: "10", text: "10"),
                                  BioLabPreset(label: "50", text: "50"), BioLabPreset(label: "100", text: "100")]),
            BioLabField("type", "", "Nucleic acid", kind: .choice, initial: "ds",
                        choices: [BioLabChoice(id: "ds", label: "dsDNA (50)"), BioLabChoice(id: "rna", label: "RNA (40)"), BioLabChoice(id: "ss", label: "ssDNA (33)")]),
            BioLabField("a280", "A₂₈₀", "Absorbance at 280 nm (optional)", optional: true, hint: "For the purity ratio A₂₆₀/A₂₈₀."),
            BioLabField("c", "c", "Concentration", quantity: .massConcentration, unit: "µg/mL"),
        ],
        modes: [BioLabMode("a260", "", "c = A₂₆₀ × factor × dilution", inputs: ["a260", "dil", "type", "a280"], unknown: "c")],
        compute: { v in
            let A = try v.nonNegative("a260"), dil = try v.positive("dil")
            let type = v.choice("type")
            let factor = type == "rna" ? 40.0 : (type == "ss" ? 33.0 : 50.0)
            let name = type == "rna" ? "RNA" : (type == "ss" ? "single-stranded DNA" : "double-stranded DNA")
            let ug = A * factor * dil
            let a = v.answer("c", base: ug * 1e-3)
            var notes: [String] = []
            if let a280 = try v.optionalValue("a280") {
                guard a280 > 0 else { throw BioLabProblem("a280", "A₂₈₀ must be more than 0.") }
                let ratio = A / a280
                notes.append("A₂₆₀/A₂₈₀ = \(BioLabFormat.fixed(ratio, decimals: 2)): about 1.8 is pure DNA and about 2.0 pure RNA; much lower suggests protein.")
            }
            notes.append("Read A₂₆₀ between about 0.1 and 1 for a reliable value.")
            return BioLabResult(answers: [a], formula: v.mode.formula,
                                working: "c = \(v.shown("a260")) × \(BioLabFormat.plain(factor)) µg/mL × \(BioLabFormat.plain(dil))",
                                steps: ["An A₂₆₀ of 1.0 is about \(BioLabFormat.plain(factor)) µg/mL of \(name) (50 for dsDNA, 40 for RNA, 33 for ssDNA).",
                                        "c = \(v.shown("a260")) × \(BioLabFormat.plain(factor)) µg/mL × \(BioLabFormat.plain(dil)) = \(w(ug)) µg/mL"],
                                notes: notes)
        })

    static let pcr = BioLabCalculator(
        id: "pcr", page: "molecules", title: "PCR copies",
        line: "How many copies of a sequence after a number of cycles.",
        formula: "N = N₀ × 2ⁿ",
        fields: [
            BioLabField("n0", "N₀", "Starting copies", unit: "copies", initial: "100", exact: true),
            BioLabField("cycles", "n", "Cycles", unit: "cycles", initial: "30", exact: true),
            BioLabField("eff", "E", "Efficiency", unit: "%", initial: "100", exact: true, hint: "100% is the ideal doubling every cycle."),
            BioLabField("N", "N", "Copies after n cycles", unit: "copies"),
        ],
        modes: [BioLabMode("pcr", "", "N = N₀ × (1 + E)ⁿ, which is N₀ × 2ⁿ at 100%", inputs: ["n0", "cycles", "eff"], unknown: "N")],
        compute: { v in
            let n0 = try v.nonNegative("n0"), n = try v.whole("cycles", 0...60), e = try v.positive("eff")
            guard e <= 100 else { throw BioLabProblem("eff", "Efficiency can't be more than 100%.") }
            let factor = 1 + e / 100
            let N = n0 * pow(factor, Double(n))
            let d = v.digits
            let base = factor == 2 ? "2" : w(factor)
            // (a power of two written out whole while it is exact: 2³⁰ = 1,073,741,824)
            let grown = factor == 2 && n <= 52 ? BioLabFormat.fixed(pow(2, Double(n)), decimals: 0) : BioLabFormat.sig(pow(factor, Double(n)), 6)
            return BioLabResult(answers: [plainAnswer("N", "Copies after \(n) cycles", N, "copies", d, field: "N")], formula: v.mode.formula,
                                working: "N = \(BioLabFormat.plain(n0)) × \(base)\(BioLabFormat.superscript(n))",
                                steps: ["Each cycle at \(BioLabFormat.plain(e))% efficiency multiplies the copies by \(base).",
                                        "\(base)\(BioLabFormat.superscript(n)) = \(grown)",
                                        "N = \(BioLabFormat.plain(n0)) × \(grown) = \(BioLabFormat.sig(N, d)) copies"],
                                notes: ["Real reactions level off in later cycles as primers and nucleotides run out."])
        })

    static let centrifuge = BioLabCalculator(
        id: "centrifuge", page: "molecules", title: "Centrifuge",
        line: "Relative centrifugal force (× g) and rpm, for a rotor's radius.",
        formula: "RCF = 1.118 × 10⁻⁵ × r × rpm²",
        fields: [
            BioLabField("r", "r", "Rotor radius", quantity: .length, unit: "cm", initial: "10", hint: "From the centre of the rotor to the sample."),
            BioLabField("rpm", "rpm", "Speed", unit: "rpm", initial: "3000"),
            BioLabField("rcf", "RCF", "Relative centrifugal force", unit: "× g", initial: "1006"),
        ],
        modes: [
            BioLabMode("rcf", "RCF", "RCF = 1.118 × 10⁻⁵ × r(cm) × rpm²", inputs: ["r", "rpm"], unknown: "rcf"),
            BioLabMode("rpm", "rpm", "rpm = √(RCF / (1.118 × 10⁻⁵ × r(cm)))", inputs: ["r", "rcf"], unknown: "rpm"),
        ],
        compute: { v in
            let cm = unit(.length, "cm")
            let r = try v.positive("r") / 0.01
            let steps0 = [v.conversion("r", to: cm)].compactMap { $0 }
            if v.mode.id == "rpm" {
                let g = try v.positive("rcf")
                let rpm = (g / (centrifugeConstant * r)).squareRoot()
                return BioLabResult(answers: [plainAnswer("rpm", "Speed", rpm, "rpm", v.digits, field: "rpm")], formula: v.mode.formula,
                                    working: "rpm = √(\(v.shown("rcf")) / (1.118 × 10⁻⁵ × \(w(r)) cm))",
                                    steps: steps0 + ["1.118 × 10⁻⁵ × \(w(r)) cm = \(w(centrifugeConstant * r))",
                                                     "rpm = √(\(w(g)) / (\(w(centrifugeConstant * r)))) = \(w(rpm)) rpm"])
            }
            let rpm = try v.positive("rpm")
            let g = centrifugeConstant * r * rpm * rpm
            return BioLabResult(answers: [plainAnswer("RCF", "Relative centrifugal force", g, "× g", v.digits, field: "rcf")], formula: v.mode.formula,
                                working: "RCF = 1.118 × 10⁻⁵ × \(w(r)) cm × (\(v.shown("rpm")))²",
                                steps: steps0 + ["rpm² = \(BioLabFormat.plain(rpm * rpm))", "RCF = 1.118 × 10⁻⁵ × \(w(r)) × \(BioLabFormat.plain(rpm * rpm)) = \(w(g)) × g"],
                                notes: ["Protocols give × g because it means the same force in any centrifuge; rpm depends on the rotor."])
        })

    static let bacteria = BioLabCalculator(
        id: "bacteria", page: "molecules", title: "Bacterial growth",
        line: "Cells after a time, from the generation (doubling) time.",
        formula: "N = N₀ × 2^(t/g)",
        fields: [
            BioLabField("n0", "N₀", "Starting number", unit: "cells", initial: "100", exact: true),
            BioLabField("g", "g", "Generation (doubling) time", quantity: .time, unit: "min", initial: "20"),
            BioLabField("t", "t", "Time", quantity: .time, unit: "h", initial: "2"),
            BioLabField("N", "N", "Number after time t", unit: "cells"),
        ],
        modes: [BioLabMode("bacteria", "", "N = N₀ × 2^(t/g)", inputs: ["n0", "g", "t"], unknown: "N")],
        compute: { v in
            let n0 = try v.nonNegative("n0"), g = try v.positive("g"), t = try v.nonNegative("t")
            let gens = t / g
            guard gens <= 200 else { throw BioLabProblem("t", "That is more than 200 doublings: try a shorter time.") }
            let N = n0 * pow(2, gens)
            let gu = v.unit("g")
            var steps = [v.conversion("t", to: gu).map { "Same unit: \($0)" }].compactMap { $0 }
            let whole = gens == gens.rounded()
            let power = whole ? "2" + BioLabFormat.superscript(Int(gens)) : "2^\(w(gens))"
            steps += ["Generations: t / g = \(v.written(t, in: gu)) / \(v.shown("g")) = \(w(gens))",
                      "N = \(BioLabFormat.plain(n0)) × \(power) = \(BioLabFormat.plain(n0)) × \(w(pow(2, gens))) = \(BioLabFormat.sig(N, v.digits)) cells"]
            return BioLabResult(answers: [plainAnswer("N", "Cells after \(v.shown("t"))", N, "cells", v.digits, field: "N"),
                                          plainAnswer("t/g", "Generations", gens, "", max(v.digits, 2))],
                                formula: v.mode.formula, working: "N = \(BioLabFormat.plain(n0)) × 2^(\(v.written(t, in: gu)) / \(v.shown("g")))", steps: steps,
                                notes: ["This is the exponential phase: growth slows once nutrients run short."])
        })

    static let pH = BioLabCalculator(
        id: "ph", page: "molecules", title: "pH",
        line: "pH from the hydrogen-ion concentration, and back.",
        formula: "pH = −log₁₀[H⁺]",
        fields: [
            BioLabField("H", "[H⁺]", "Hydrogen-ion concentration", quantity: .concentration, unit: "M", initial: "1 × 10^-7"),
            BioLabField("pH", "pH", "pH", initial: "7.00"),
        ],
        modes: [
            BioLabMode("pH", "pH", "pH = −log₁₀[H⁺]", inputs: ["H"], unknown: "pH"),
            BioLabMode("H", "[H⁺]", "[H⁺] = 10^(−pH)", inputs: ["pH"], unknown: "H"),
        ],
        compute: { v in
            let molar = unit(.concentration, "M")
            func kind(_ p: Double) -> String { p < 7 ? "acidic" : (p > 7 ? "basic" : "neutral") }
            if v.mode.id == "H" {
                let p = try v.value("pH")
                guard p >= -2 && p <= 16 else { throw BioLabProblem("pH", "A pH is usually between 0 and 14.") }
                let dec = BioLabParse.decimals(v.text("pH"))
                let d = min(v.setting, max(dec, 1) + 1)
                let h = pow(10, -p)
                let a = v.answer("H", base: h, digits: d)
                return BioLabResult(answers: [a], formula: v.mode.formula, working: "[H⁺] = 10^(−\(v.shown("pH")))",
                                    steps: ["[H⁺] = 10^(−\(v.shown("pH"))) = \(BioLabFormat.sig(h, d)) M", a.alt.map { "\(BioLabFormat.sig(h, d)) M \($0)" }].compactMap { $0 },
                                    notes: ["Each pH unit is a tenfold change in [H⁺]. This solution is \(kind(p)) (at 25 °C)."],
                                    precision: "\(d) significant figures: [H⁺] has as many as the pH has decimal places (\(dec)), plus at most one.")
            }
            let n = try v.typed("H")
            let h = try v.positive("H")
            let sf = n.sigFigs ?? v.setting
            let dec = min(v.setting, sf + 1)
            let p = -log10(h)
            let pText = BioLabFormat.fixed(p, decimals: dec)
            return BioLabResult(answers: [BioLabAnswer(symbol: "pH", label: "pH", value: p, unit: "", text: pText, field: "pH")],
                                formula: v.mode.formula, working: "pH = −log₁₀(\(v.written(h, in: molar)))",
                                steps: [v.conversion("H", to: molar), "pH = −log₁₀(\(v.written(h, in: molar))) = \(pText)",
                                        "pOH = 14 − pH = \(BioLabFormat.fixed(14 - p, decimals: dec)) (in water at 25 °C)"].compactMap { $0 },
                                notes: ["Each pH unit is a tenfold change in [H⁺]. This solution is \(kind(p)) (at 25 °C)."],
                                precision: "\(dec) decimal places: a pH has as many as [H⁺] has significant figures (\(sf)), plus at most one.")
        })

    static let q10 = BioLabCalculator(
        id: "q10", page: "molecules", title: "Q₁₀",
        line: "How much faster a process runs for every 10 °C warmer.",
        formula: "Q₁₀ = (R₂ / R₁)^(10 / (T₂ − T₁))",
        fields: [
            BioLabField("r1", "R₁", "Rate at T₁", initial: "2.0", hint: "Any rate unit, the same for both."),
            BioLabField("t1", "T₁", "First temperature", unit: "°C", initial: "20", exact: true),
            BioLabField("r2", "R₂", "Rate at T₂", initial: "4.4"),
            BioLabField("t2", "T₂", "Second temperature", unit: "°C", initial: "30", exact: true),
            BioLabField("q", "Q₁₀", "Temperature coefficient", initial: "2.2"),
        ],
        modes: [
            BioLabMode("q", "Q₁₀", "Q₁₀ = (R₂ / R₁)^(10 / (T₂ − T₁))", inputs: ["r1", "t1", "r2", "t2"], unknown: "q"),
            BioLabMode("r2", "R₂", "R₂ = R₁ × Q₁₀^((T₂ − T₁) / 10)", inputs: ["r1", "t1", "t2", "q"], unknown: "r2"),
        ],
        compute: { v in
            let r1 = try v.positive("r1"), t1 = try v.value("t1"), t2 = try v.value("t2")
            guard t1 != t2 else { throw BioLabProblem("t2", "T₂ must differ from T₁.") }
            if v.mode.id == "r2" {
                let q = try v.positive("q")
                let r2 = r1 * pow(q, (t2 - t1) / 10)
                return BioLabResult(answers: [plainAnswer("R₂", "Rate at \(BioLabFormat.plain(t2)) °C", r2, "", v.digits, field: "r2")], formula: v.mode.formula,
                                    working: "R₂ = \(v.shown("r1")) × \(v.shown("q"))^(\(w(t2 - t1)) / 10)",
                                    steps: ["(T₂ − T₁) / 10 = (\(BioLabFormat.plain(t2)) − \(BioLabFormat.plain(t1))) / 10 = \(w((t2 - t1) / 10))",
                                            "R₂ = \(v.shown("r1")) × \(v.shown("q"))^\(w((t2 - t1) / 10)) = \(w(r2))"])
            }
            let r2 = try v.positive("r2")
            let q = pow(r2 / r1, 10 / (t2 - t1))
            return BioLabResult(answers: [plainAnswer("Q₁₀", "Temperature coefficient", q, "", v.digits, field: "q")], formula: v.mode.formula,
                                working: "Q₁₀ = (\(v.shown("r2")) / \(v.shown("r1")))^(10 / (\(BioLabFormat.plain(t2)) − \(BioLabFormat.plain(t1))))",
                                steps: ["R₂ / R₁ = \(v.shown("r2")) / \(v.shown("r1")) = \(w(r2 / r1))",
                                        "10 / (T₂ − T₁) = 10 / \(w(t2 - t1)) = \(w(10 / (t2 - t1)))",
                                        "Q₁₀ = \(w(r2 / r1))^\(w(10 / (t2 - t1))) = \(w(q))"],
                                notes: ["A Q₁₀ of about 2 to 3 is typical of enzyme-catalysed processes; about 1, of physical ones such as diffusion."])
        })
}

import AppKit
import SwiftUI

// (1.3.93) Molar Mass & Stoichiometry: four tabs. Molar mass — a formula's molar mass with its breakdown (each element,
// its count, atomic mass, subtotal and mass %, the percent composition). Convert — one amount of a formula in grams,
// moles, particles or litres of gas at STP, and the other three. Reaction — a balanced equation (balanced for you by
// the Equation Balancer's arithmetic if it needs it) with what is given of each reactant: the limiting reagent, each
// product's theoretical yield, what is left of the rest, and the percent yield from an actual yield. Empirical — each
// element's mass % (or grams) to the empirical formula, and the molecular one with a molar mass. Everything works
// through Model/ChemFormula.swift and Model/ChemStoich.swift; each tab keeps its inputs (ChemPrefs `moles.*`); the
// launch's -SimplMoles <tab>, -SimplMolesFormula, -SimplMolesEquation, -SimplMolesGiven "H2=4g,O2=16g",
// -SimplMolesEmpirical "C=40,H=6.71,O=53.3" and -SimplMolesMass open it on a state for a picture. Its pin is the
// Molar mass tab small: a formula to g/mol.

/// The tabs, by the launch flag's and the kept tab's word.
enum MolesTab: String, CaseIterable, Identifiable {
    case molar, convert, reaction, empirical

    var id: String { rawValue }

    var title: String {
        switch self {
        case .molar: return "Molar mass"
        case .convert: return "Convert"
        case .reaction: return "Reaction"
        case .empirical: return "Empirical"
        }
    }
}

struct MolarMassTool: View {
    @State private var tab = MolarMassTool.firstTab
    // Molar mass
    @State private var formula = MolarMassTool.launched(.molar, "SimplMolesFormula") ?? ChemPrefs.string(.moles, "formula") ?? "C6H12O6"
    // Convert
    @State private var convertFormula = MolarMassTool.launched(.convert, "SimplMolesFormula") ?? ChemPrefs.string(.moles, "convertFormula") ?? "H2O"
    @State private var convertValue = ChemPrefs.string(.moles, "convertValue") ?? "10"
    @State private var convertUnit = ChemPrefs.string(.moles, "convertUnit").flatMap { ChemStoich.AmountUnit(rawValue: $0) } ?? .grams
    // Reaction
    @State private var equation = MolarMassTool.launched(.reaction, "SimplMolesEquation") ?? ChemPrefs.string(.moles, "equation") ?? "2H2 + O2 → 2H2O"
    @State private var given = MolarMassTool.firstGiven
    @State private var actual = ChemPrefs.string(.moles, "actual") ?? ""
    @State private var actualOf = ChemPrefs.string(.moles, "actualOf") ?? ""
    // Empirical
    @State private var shares = MolarMassTool.firstShares
    @State private var inGrams = ChemPrefs.bool(.moles, "sharesInGrams", default: false)
    @State private var empiricalMass = MolarMassTool.launched(.empirical, "SimplMolesMass") ?? ChemPrefs.string(.moles, "empiricalMass") ?? ""
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    // the fields kept between openings, watched as one list (eleven .onChange modifiers in a row were more than the
    // compiler could type-check in reasonable time); the one Bool, inGrams, keeps its own
    private static let rememberedFields = ["tab", "formula", "convertFormula", "convertValue", "convertUnit", "equation", "given", "actual", "actualOf", "shares", "empiricalMass"]
    private var remembered: [String] {
        [tab.rawValue, formula, convertFormula, convertValue, convertUnit.rawValue, equation, Self.givenText(given) ?? "", actual, actualOf,
         shares.map { "\($0.symbol)=\($0.amount)" }.joined(separator: ","), empiricalMass]
    }

    static let molarExamples = ["Ca(OH)2", "C6H12O6", "CuSO4·5H2O"]

    // MARK: Opening

    private static var launchedHere: Bool { ChemTools.launched == .moles }

    private static var firstTab: MolesTab {
        if launchedHere, let t = UserDefaults.standard.string(forKey: "SimplMoles").flatMap({ MolesTab(rawValue: $0) }) { return t }
        return ChemPrefs.string(.moles, "tab").flatMap { MolesTab(rawValue: $0) } ?? .molar
    }

    /// A launch flag's value, when the launch opened this tool on `tab` (a formula flag means the tab it opens on).
    private static func launched(_ tab: MolesTab, _ key: String) -> String? {
        guard launchedHere, firstTab == tab, let v = UserDefaults.standard.string(forKey: key), !v.isEmpty else { return nil }
        return v
    }

    /// What is given of each reactant, by the species' text: "4 g" typed as "4" with grams on.
    struct GivenText: Equatable {
        var value = ""
        var grams = true
    }

    private static var firstGiven: [String: GivenText] {
        let raw = launched(.reaction, "SimplMolesGiven") ?? ChemPrefs.string(.moles, "given")
        var out: [String: GivenText] = [:]
        for (k, g) in ChemStoich.givens(raw) { out[k] = GivenText(value: ChemNumber.sig(g.value, 6), grams: g.grams) }
        return out
    }

    /// An element's share on the Empirical tab.
    struct Share: Identifiable, Equatable {
        let id = UUID()
        var symbol = ""
        var amount = ""
    }

    private static var firstShares: [Share] {
        let raw = launched(.empirical, "SimplMolesEmpirical") ?? ChemPrefs.string(.moles, "shares")
        let parts = ChemStoich.shares(raw)
        if parts.isEmpty { return [Share(symbol: "C", amount: "40.0"), Share(symbol: "H", amount: "6.71"), Share(symbol: "O", amount: "53.3")] }
        return parts.map { Share(symbol: $0.symbol, amount: ChemNumber.sig($0.amount, 6)) }
    }

    var body: some View {
        ToolPage {
            Picker("Tab", selection: $tab) {
                ForEach(MolesTab.allCases) { t in Text(t.title).tag(t) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .frame(maxWidth: 520)
            .accessibilityLabel("What to work out")
            switch tab {
            case .molar: molarTab
            case .convert: convertTab
            case .reaction: reactionTab
            case .empirical: empiricalTab
            }
        }
        .onAppear(perform: takeHandOff)
        .onChange(of: remembered) { _, values in ChemPrefs.remember(.moles, Self.rememberedFields, values) }
        .onChange(of: inGrams) { _, on in ChemPrefs.set(.moles, "sharesInGrams", on) }
    }

    /// (1.3.83) What the pin's field had, its panel opened full screen: a formula, on the Molar mass tab.
    private func takeHandOff() {
        if let f = ToolsCenter.shared.takeInfo("formula"), !f.isEmpty {
            formula = f
            tab = .molar
        }
    }

    /// What is given, as it is kept: "H2=4g,O2=16g".
    private static func givenText(_ g: [String: GivenText]) -> String? {
        let parts = g.keys.sorted().compactMap { k -> String? in
            guard let t = g[k], !t.value.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }
            return "\(k)=\(t.value.trimmingCharacters(in: .whitespaces))\(t.grams ? "g" : "mol")"
        }
        return parts.isEmpty ? nil : parts.joined(separator: ",")
    }

    // MARK: Molar mass

    private var molarTab: some View {
        let parsed = ChemFormula.parse(formula)
        return ToolColumns(sideWidth: 440, breakpoint: 1000) {
            VStack(alignment: .leading, spacing: 20) {
                formulaField("Formula", text: $formula, examples: Self.molarExamples)
                VStack(alignment: .leading, spacing: 10) {
                    switch parsed {
                    case .success(let f):
                        Text(f.pretty)
                            .font(.system(size: 26, weight: .semibold, design: .rounded))
                            .textSelection(.enabled)
                        Text("\(ChemNumber.fixed(f.molarMass, 3)) g/mol")
                            .font(.system(size: 48, weight: .bold, design: .rounded).monospacedDigit())
                            .lineLimit(1)
                            .minimumScaleFactor(0.5)
                            .textSelection(.enabled)
                            .contentTransition(.numericText())
                        Text("\(toolPlural(f.atoms, "atom")) in each formula unit")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                    case .failure(let e):
                        if e == .empty {
                            Text("Type a formula, or pick one above.").font(.sBody).foregroundStyle(.secondary)
                        } else {
                            ToolNote(text: e.message)
                        }
                    }
                    Button {
                        if let f = try? parsed.get() { copyToPasteboard("\(f.text): \(ChemNumber.fixed(f.molarMass, 3)) g/mol") }
                    } label: {
                        Label("Copy", systemImage: "doc.on.doc")
                    }
                    .glassButton()
                    .disabled((try? parsed.get()) == nil)
                    .help("Copy the molar mass")
                }
                .padding(24)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card(radius: 22)
                .animation(reduceMotion ? nil : Motion.snappy, value: (try? parsed.get())?.molarMass ?? 0)
            }
        } side: {
            PageSection(title: "Breakdown") {
                if let f = try? parsed.get() {
                    breakdown(f)
                } else {
                    Text("Each element's atoms, mass and share of the whole.").font(.sCallout).foregroundStyle(.secondary)
                }
            }
        }
    }

    /// A formula field with its example chips.
    private func formulaField(_ label: String, text: Binding<String>, examples: [String]) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(label).font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
            TextField("C6H12O6", text: text)
                .textFieldStyle(.roundedBorder)
                .font(.system(size: 18).monospacedDigit())
                .controlSize(.large)
                .frame(maxWidth: 420)
                .accessibilityLabel(label)
            ChipScroller {
                HStack(spacing: 8) {
                    ForEach(examples, id: \.self) { e in
                        Button(e) { text.wrappedValue = e }
                            .font(.sCallout)
                            .glassChip(ToolKind.moles.color, on: text.wrappedValue == e)
                    }
                }
                .padding(.vertical, 2)
            }
        }
    }

    private func breakdown(_ f: ChemFormula) -> some View {
        let b = ChemMolar.breakdown(f)
        return Grid(alignment: .trailing, horizontalSpacing: 10, verticalSpacing: 0) {
            GridRow {
                Text("Element").gridColumnAlignment(.leading)
                Text("Count")
                Text("Atomic mass")
                Text("Subtotal")
                Text("Mass %")
            }
            .font(.sCaption.weight(.semibold))
            .foregroundStyle(.secondary)
            .padding(.horizontal, 14)
            .padding(.vertical, 8)
            ForEach(b.rows, id: \.symbol) { row in
                Divider().padding(.leading, 14).gridCellColumns(5)
                GridRow {
                    Text(row.symbol).font(.sCallout.weight(.medium)).gridColumnAlignment(.leading)
                    Text("\(row.count)").font(.sCallout.monospacedDigit())
                    Text(ChemNumber.sig(row.atomicMass, 6)).font(.sCallout.monospacedDigit())
                    Text(ChemNumber.fixed(row.subtotal, 3)).font(.sCallout.monospacedDigit())
                    Text("\(ChemNumber.fixed(row.percent, 2)) %").font(.sCallout.monospacedDigit())
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 8)
                .accessibilityElement(children: .combine)
            }
            Divider().padding(.leading, 14).gridCellColumns(5)
            GridRow {
                Text("Total").font(.sCallout.weight(.semibold)).gridColumnAlignment(.leading)
                Text("\(f.atoms)").font(.sCallout.monospacedDigit())
                Text("")
                Text(ChemNumber.fixed(b.total, 3)).font(.sCallout.weight(.semibold).monospacedDigit())
                Text("100 %").font(.sCallout.monospacedDigit())
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 8)
        }
        .card(radius: 14)
    }

    // MARK: Convert

    private var convertTab: some View {
        let parsed = ChemFormula.parse(convertFormula)
        let amounts = (try? parsed.get()).flatMap { f in ChemNumber.number(convertValue).flatMap { ChemStoich.convert($0, convertUnit, molarMass: f.molarMass) } }
        return ToolColumns(sideWidth: 380, breakpoint: 980) {
            VStack(alignment: .leading, spacing: 20) {
                formulaField("Formula", text: $convertFormula, examples: ["H2O", "CO2", "NaCl", "C6H12O6"])
                VStack(alignment: .leading, spacing: 6) {
                    Text("Amount").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                    HStack(spacing: 8) {
                        TextField("10", text: $convertValue)
                            .textFieldStyle(.roundedBorder)
                            .font(.sBody)
                            .controlSize(.large)
                            .frame(width: 140)
                            .accessibilityLabel("Amount")
                        Picker("Unit", selection: $convertUnit) {
                            ForEach(ChemStoich.AmountUnit.allCases, id: \.self) { u in Text(u.label).tag(u) }
                        }
                        .labelsHidden()
                        .controlSize(.large)
                        .frame(maxWidth: 260)
                    }
                }
                VStack(alignment: .leading, spacing: 12) {
                    if let a = amounts {
                        ForEach(ChemStoich.AmountUnit.allCases.filter { $0 != convertUnit }, id: \.self) { u in
                            HStack(alignment: .firstTextBaseline, spacing: 10) {
                                Text(u == .particles ? (ChemNumber.pretty(ChemNumber.sig(a.particles))) : ChemNumber.sig(a.value(u)))
                                    .font(.system(size: 30, weight: .bold, design: .rounded).monospacedDigit())
                                    .lineLimit(1)
                                    .minimumScaleFactor(0.5)
                                    .textSelection(.enabled)
                                Text(u == .particles ? "molecules / formula units" : (u == .litres ? "L at STP" : u.symbol))
                                    .font(.sBody)
                                    .foregroundStyle(.secondary)
                            }
                            .accessibilityElement(children: .combine)
                        }
                        Button {
                            copyToPasteboard(ChemStoich.AmountUnit.allCases.map { "\(ChemNumber.sig(a.value($0))) \($0.symbol)" }.joined(separator: " = "))
                        } label: {
                            Label("Copy", systemImage: "doc.on.doc")
                        }
                        .glassButton()
                        .help("Copy the amount in every unit")
                    } else if case .failure(let e) = parsed, e != .empty {
                        ToolNote(text: e.message)
                    } else {
                        Text("A formula and an amount: the other three come out here.").font(.sBody).foregroundStyle(.secondary)
                    }
                }
                .padding(24)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card(radius: 22)
            }
        } side: {
            PageSection(title: "How") {
                VStack(spacing: 0) {
                    BioValueRow(label: "Molar mass", value: (try? parsed.get()).map { "\(ChemNumber.fixed($0.molarMass, 3)) g/mol" } ?? "—")
                    BioValueRow(label: "Avogadro's number", value: "6.02214076 × 10²³ /mol")
                    BioValueRow(label: "A mole of gas at STP", value: "22.4 L", last: true)
                }
                .card(radius: 14)
                Text("Grams ÷ molar mass = moles; moles × Avogadro's number = particles; moles × 22.4 L = litres of an ideal gas at STP (0 °C, 1 atm).")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    // MARK: Reaction

    private var balanced: Result<ChemBalanced, ChemBalanceError> { ChemBalancer.balance(equation) }

    private var reactionTab: some View {
        let result = balanced
        let b = try? result.get()
        let reaction = b.flatMap { bal -> ChemStoich.Reaction? in
            var givens: [Int: ChemStoich.Given] = [:]
            for (i, s) in bal.species.enumerated() where s.reactant {
                if let t = given[s.text], let v = ChemNumber.number(t.value) { givens[i] = .init(value: v, grams: t.grams) }
            }
            return ChemStoich.reaction(bal, given: givens)
        }
        return ToolColumns(sideWidth: 400, breakpoint: 1000) {
            VStack(alignment: .leading, spacing: 20) {
                VStack(alignment: .leading, spacing: 8) {
                    Text("Equation").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                    TextField("2H2 + O2 → 2H2O", text: $equation)
                        .textFieldStyle(.roundedBorder)
                        .font(.system(size: 18).monospacedDigit())
                        .controlSize(.large)
                        .accessibilityLabel("The reaction's equation")
                    switch result {
                    case .success(let bal):
                        HStack(spacing: 8) {
                            EquationText(balanced: bal).font(.system(size: 20, weight: .semibold, design: .rounded))
                            if bal.changed {
                                Text("balanced for you")
                                    .font(.sCaption.weight(.medium))
                                    .padding(.horizontal, 8)
                                    .padding(.vertical, 3)
                                    .glassCapsule(tint: ToolKind.balance.color.opacity(0.25))
                            }
                        }
                        .fixedSize(horizontal: false, vertical: true)
                    case .failure(let e):
                        if equation.trimmingCharacters(in: .whitespaces).isEmpty || e == .noArrow || e == .emptySide {
                            Text("Type the reaction, with → between its sides; it is balanced for you.").font(.sFootnote).foregroundStyle(.secondary)
                        } else {
                            ToolNote(text: e.message)
                        }
                    }
                }
                if let bal = b {
                    PageSection(title: "Given") {
                        VStack(spacing: 0) {
                            ForEach(Array(bal.reactants.enumerated()), id: \.offset) { k, pair in
                                givenRow(pair.species, coefficient: pair.coefficient, last: k == bal.reactants.count - 1)
                            }
                        }
                        .card(radius: 14)
                        Text("A reactant left blank is taken to be in excess.").font(.sFootnote).foregroundStyle(.secondary)
                    }
                    PageSection(title: "Actual yield") {
                        HStack(spacing: 8) {
                            TextField("15", text: $actual)
                                .textFieldStyle(.roundedBorder)
                                .font(.sBody)
                                .frame(width: 110)
                                .accessibilityLabel("Actual yield in grams")
                            Text("g of").font(.sCallout).foregroundStyle(.secondary)
                            Picker("Product", selection: Binding(get: { bal.products.first { $0.species.text == actualOf }?.species.text ?? bal.products.first?.species.text ?? "" },
                                                                 set: { actualOf = $0 })) {
                                ForEach(Array(bal.products.enumerated()), id: \.offset) { _, p in Text(p.species.formula.pretty).tag(p.species.text) }
                            }
                            .labelsHidden()
                            .frame(maxWidth: 200)
                            if let r = reaction, let v = ChemNumber.number(actual),
                               let y = r.yields.first(where: { $0.species.text == actualOf }) ?? r.yields.first,
                               let pct = ChemStoich.percentYield(actual: v, theoretical: y.grams) {
                                Text("→ \(ChemNumber.fixed(pct, 1)) % yield")
                                    .font(.sHeadline.monospacedDigit())
                                    .foregroundStyle(pct > 100.05 ? Color.orange : Color.primary)
                            }
                        }
                    }
                }
            }
        } side: {
            PageSection(title: "Result") {
                if let r = reaction {
                    reactionResult(r)
                } else {
                    Text("Give an amount of a reactant to see what limits, what each product comes to, and what is left.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private func givenRow(_ s: ChemSpecies, coefficient: Int, last: Bool) -> some View {
        let text = Binding(get: { given[s.text]?.value ?? "" }, set: { given[s.text, default: GivenText()].value = $0 })
        let grams = Binding(get: { given[s.text]?.grams ?? true }, set: { given[s.text, default: GivenText()].grams = $0 })
        return VStack(spacing: 0) {
            HStack(spacing: 10) {
                Text((coefficient == 1 ? "" : "\(coefficient) ") + s.formula.pretty)
                    .font(.sCallout.weight(.medium))
                    .frame(minWidth: 90, alignment: .leading)
                Text("\(ChemNumber.fixed(s.formula.molarMass, 3)) g/mol")
                    .font(.sCaption.monospacedDigit())
                    .foregroundStyle(.secondary)
                Spacer(minLength: 8)
                TextField("4", text: text)
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody)
                    .frame(width: 90)
                    .accessibilityLabel("Given of \(s.text)")
                Picker("Unit", selection: grams) {
                    Text("g").tag(true)
                    Text("mol").tag(false)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(width: 90)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 8)
            if !last { Divider().padding(.leading, 14) }
        }
    }

    private func reactionResult(_ r: ChemStoich.Reaction) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Limiting reagent").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                Text(r.limiting.formula.pretty)
                    .font(.system(size: 30, weight: .bold, design: .rounded))
                Text("The reaction runs \(ChemNumber.sig(r.extent)) times as written.").font(.sCallout).foregroundStyle(.secondary)
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card(radius: 16)
            BioHeading("Theoretical yield")
            VStack(spacing: 0) {
                ForEach(Array(r.yields.enumerated()), id: \.offset) { k, y in
                    BioValueRow(label: y.species.formula.pretty, value: "\(ChemNumber.sig(y.grams)) g · \(ChemNumber.sig(y.moles)) mol", last: k == r.yields.count - 1)
                }
            }
            .card(radius: 14)
            if !r.excess.isEmpty {
                BioHeading("Excess left")
                VStack(spacing: 0) {
                    ForEach(Array(r.excess.enumerated()), id: \.offset) { k, e in
                        BioValueRow(label: e.species.formula.pretty, value: "\(ChemNumber.sig(e.grams)) g · \(ChemNumber.sig(e.moles)) mol", last: k == r.excess.count - 1)
                    }
                }
                .card(radius: 14)
            }
            Button {
                var lines = ["Limiting reagent: \(r.limiting.text)"]
                lines += r.yields.map { "\($0.species.text): \(ChemNumber.sig($0.grams)) g (\(ChemNumber.sig($0.moles)) mol)" }
                lines += r.excess.map { "\($0.species.text) left: \(ChemNumber.sig($0.grams)) g (\(ChemNumber.sig($0.moles)) mol)" }
                copyToPasteboard(lines.joined(separator: "\n"))
            } label: {
                Label("Copy", systemImage: "doc.on.doc")
            }
            .glassButton()
            .help("Copy the limiting reagent, the yields and what is left")
        }
    }

    // MARK: Empirical

    private var empiricalTab: some View {
        let parts = shares.filter { !$0.symbol.trimmingCharacters(in: .whitespaces).isEmpty || !$0.amount.trimmingCharacters(in: .whitespaces).isEmpty }
            .map { (symbol: $0.symbol, amount: ChemNumber.number($0.amount) ?? 0) }
        let result = ChemStoich.empirical(parts, molarMass: ChemNumber.number(empiricalMass))
        return ToolColumns(sideWidth: 400, breakpoint: 980) {
            VStack(alignment: .leading, spacing: 20) {
                HStack(spacing: 12) {
                    Text("Each element's").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                    Picker("Share", selection: $inGrams) {
                        Text("mass %").tag(false)
                        Text("grams").tag(true)
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .frame(width: 160)
                }
                VStack(spacing: 0) {
                    ForEach($shares) { $share in
                        HStack(spacing: 10) {
                            TextField("C", text: $share.symbol)
                                .textFieldStyle(.roundedBorder)
                                .font(.sBody)
                                .frame(width: 70)
                                .accessibilityLabel("Element")
                            TextField(inGrams ? "grams" : "%", text: $share.amount)
                                .textFieldStyle(.roundedBorder)
                                .font(.sBody)
                                .frame(width: 120)
                                .accessibilityLabel(inGrams ? "Grams" : "Mass percent")
                            Text(inGrams ? "g" : "%").font(.sCallout).foregroundStyle(.secondary)
                            Spacer(minLength: 0)
                            Button {
                                shares.removeAll { $0.id == share.id }
                            } label: {
                                Image(systemName: "minus.circle").foregroundStyle(.secondary)
                            }
                            .buttonStyle(.plain)
                            .disabled(shares.count <= 1)
                            .help("Take this element out")
                            .accessibilityLabel("Take \(share.symbol) out")
                        }
                        .padding(.horizontal, 14)
                        .padding(.vertical, 8)
                        Divider().padding(.leading, 14)
                    }
                    Button {
                        if shares.count < 8 { shares.append(Share()) }
                    } label: {
                        Label("Add an element", systemImage: "plus")
                    }
                    .buttonStyle(.plain)
                    .foregroundStyle(Theme.accent)
                    .padding(14)
                    .disabled(shares.count >= 8)
                }
                .card(radius: 14)
                .frame(maxWidth: 420)
                ToolField(label: "Molar mass (optional)", text: $empiricalMass, placeholder: "180", hint: "With it, the molecular formula too.", suffix: "g/mol", width: 220)
            }
        } side: {
            PageSection(title: "Formula") {
                VStack(alignment: .leading, spacing: 12) {
                    switch result {
                    case .success(let e):
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Empirical").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                            Text((try? ChemFormula.parse(e.empiricalText).get())?.pretty ?? e.empiricalText)
                                .font(.system(size: 34, weight: .bold, design: .rounded))
                                .textSelection(.enabled)
                            Text("\(ChemNumber.fixed(e.empiricalMass, 3)) g/mol").font(.sCallout).foregroundStyle(.secondary)
                        }
                        if let m = e.molecularText, let k = e.multiple {
                            VStack(alignment: .leading, spacing: 4) {
                                Text("Molecular").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                                Text((try? ChemFormula.parse(m).get())?.pretty ?? m)
                                    .font(.system(size: 34, weight: .bold, design: .rounded))
                                    .textSelection(.enabled)
                                Text(k == 1 ? "The molar mass is the empirical formula's: they are the same." : "\(k) × the empirical formula, from the molar mass.")
                                    .font(.sCallout)
                                    .foregroundStyle(.secondary)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                        }
                        Button {
                            copyToPasteboard(e.molecularText.map { "\(e.empiricalText) (molecular \($0))" } ?? e.empiricalText)
                        } label: {
                            Label("Copy", systemImage: "doc.on.doc")
                        }
                        .glassButton()
                        .help("Copy the formula")
                    case .failure(let e):
                        if e == .empty && parts.allSatisfy({ $0.amount == 0 }) {
                            Text("Each element's share brings its moles, then their ratio.").font(.sCallout).foregroundStyle(.secondary)
                        } else {
                            ToolNote(text: e.message)
                        }
                    }
                }
                .padding(16)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card(radius: 16)
                Text("Moles of each over the smallest; times 2 to 6 until every ratio is within 0.08 of a whole number.")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

/// Molar Mass & Stoichiometry's pin, opened: a formula to its molar mass; Return opens the tool whole on it.
struct MolarMassCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var formula = UserDefaults.standard.string(forKey: "SimplMolesFormula") ?? ""

    private var parsed: Result<ChemFormula, ChemFormulaError> { ChemFormula.parse(formula) }

    /// (1.3.83) What Open Full Screen carries: the formula typed (nothing typed: nothing).
    private var carried: [String: String] { formula.trimmingCharacters(in: .whitespaces).isEmpty ? [:] : ["formula": formula] }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            TextField("C6H12O6", text: $formula)
                .textFieldStyle(.roundedBorder)
                .font(.sCallout)
                .onSubmit { openFull(carried) }
                .accessibilityLabel("Formula")
            switch parsed {
            case .success(let f):
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(ChemNumber.fixed(f.molarMass, 3))
                        .font(.system(size: 30, weight: .bold, design: .rounded).monospacedDigit())
                        .lineLimit(1)
                        .minimumScaleFactor(0.5)
                        .textSelection(.enabled)
                    Text("g/mol").font(.sCallout).foregroundStyle(.secondary)
                }
                Text("\(f.pretty) · \(toolPlural(f.atoms, "atom"))").font(.sCallout).foregroundStyle(.secondary)
            case .failure(let e):
                Text(e == .empty ? "Type a formula for its molar mass." : e.message)
                    .font(.sCallout)
                    .foregroundStyle(e == .empty ? Color.secondary : Color.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .quickCarry(carried)
    }
}

import AppKit
import SwiftUI

// (tools) Solutions & pH: four tabs. Molarity — any two of an amount (moles, or grams of a formula), a volume and a
// molarity to the third, with the mass % and ppm of a water-like solution. Dilution — M₁V₁ = M₂V₂, "How much stock?",
// one blank solved. pH — any one of pH, pOH, [H⁺] and [OH⁻] to the other three on a 0–14 strip (Kw = 1.0 × 10⁻¹⁴ at
// 25 °C, the only temperature here), and the pH of a strong or weak acid or base from its concentration (a weak one's
// quadratic solved exactly, with its percent ionised). Titration — a strong or weak analyte with a strong titrant:
// the equivalence volume, the pH at the start, half-way, at equivalence and past it, and the curve drawn from the
// charge balance with the equivalence point marked; a buffer's pH by Henderson–Hasselbalch under it. The arithmetic
// is Model/ChemSolution.swift; each tab keeps its inputs (ChemPrefs `solution.*`); the launch's -SimplSolution <tab>
// and -SimplSolutionValues "pH=3" (or "kind=weak acid,c=0.1,K=1.8e-5", "M1=2,V1=50 mL,M2=0.5", "kind=wa-sb,Ca=0.1,
// Va=25,Ct=0.1,K=1.8e-5") open it on a state for a picture. Its pin is the pH tab small.

enum SolutionTab: String, CaseIterable, Identifiable {
    case molarity, dilution, ph, titration

    var id: String { rawValue }

    var title: String {
        switch self {
        case .molarity: return "Molarity"
        case .dilution: return "Dilution"
        case .ph: return "pH"
        case .titration: return "Titration"
        }
    }
}

struct SolutionsPHTool: View {
    @State private var tab = SolutionsPHTool.firstTab
    @State private var molarity = SolutionsPHTool.firstFields(.molarity, ["formula": "NaCl", "amount": "5.844", "amountUnit": "g", "volume": "100", "volumeUnit": "mL", "molarity": ""])
    @State private var dilution = SolutionsPHTool.firstFields(.dilution, ["M1": "2", "V1": "50", "M2": "0.5", "V2": "", "unit": "mL"])
    @State private var ph = SolutionsPHTool.firstFields(.ph, ["given": "h", "value": "0.001", "kind": "weakAcid", "c": "0.10", "K": "1.8e-5"])
    @State private var titration = SolutionsPHTool.firstFields(.titration, ["kind": "weakAcidStrongBase", "Ca": "0.1", "Va": "25", "Ct": "0.1", "K": "1.8e-5", "pKa": "4.74", "base": "0.1", "acid": "0.1"])
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    // MARK: Opening

    private static var launchedHere: Bool { ChemTools.launched == .solution }

    private static var firstTab: SolutionTab {
        if launchedHere, let t = UserDefaults.standard.string(forKey: "SimplSolution").flatMap({ SolutionTab(rawValue: $0) }) { return t }
        return ChemPrefs.string(.solution, "tab").flatMap { SolutionTab(rawValue: $0) } ?? .ph
    }

    /// A tab's fields: the launch's (-SimplSolutionValues, when it opened this tool on that tab), else as kept, else
    /// `start`. A launch's units ("50 mL", "4 g") set the unit fields; its pH may come as pH, pOH, H or OH; its kinds
    /// may be short ("weak acid", "wa-sb").
    private static func firstFields(_ tab: SolutionTab, _ start: [String: String]) -> [String: String] {
        var out = start
        if launchedHere, firstTab == tab, let raw = UserDefaults.standard.string(forKey: "SimplSolutionValues"), !raw.isEmpty {
            for (k, v) in ChemLaunchValues.parse(raw) { take(&out, tab, k, v) }
            return out
        }
        for (k, v) in ChemLaunchValues.parse(ChemPrefs.string(.solution, tab.rawValue)) where out[k] != nil { out[k] = v }
        return out
    }

    private static func take(_ out: inout [String: String], _ tab: SolutionTab, _ key: String, _ raw: String) {
        let (number, unit) = ChemLaunchValues.split(raw)
        switch (tab, key) {
        case (.molarity, "amount"):
            out["amount"] = number
            if let unit { out["amountUnit"] = unit.lowercased().hasPrefix("mol") ? "mol" : "g" }
        case (.molarity, "volume"):
            out["volume"] = number
            if let unit { out["volumeUnit"] = unit.lowercased() == "l" ? "L" : "mL" }
        case (.dilution, "V1"), (.dilution, "V2"):
            out[key] = number
            if let unit { out["unit"] = unit.lowercased() == "l" ? "L" : "mL" }
        case (.ph, "pH"), (.ph, "pOH"), (.ph, "H"), (.ph, "OH"):
            out["given"] = key == "H" ? "h" : (key == "OH" ? "oh" : key)
            out["value"] = number
        case (.ph, "kind"):
            out["kind"] = ChemSolution.Kind.allCases.first { $0.label.hasPrefix(raw.lowercased()) || $0.rawValue == raw }?.rawValue ?? out["kind"]
        case (.titration, "kind"):
            let short = ["sa-sb": "strongAcidStrongBase", "wa-sb": "weakAcidStrongBase", "wb-sa": "weakBaseStrongAcid"]
            out["kind"] = short[raw.lowercased()] ?? ChemSolution.TitrationKind(rawValue: raw)?.rawValue ?? out["kind"]
        default:
            if out[key] != nil { out[key] = raw }
        }
    }

    var body: some View {
        ToolPage {
            Picker("Tab", selection: $tab) {
                ForEach(SolutionTab.allCases) { t in Text(t.title).tag(t) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .frame(maxWidth: 520)
            .accessibilityLabel("What to work out")
            switch tab {
            case .molarity: molarityTab
            case .dilution: dilutionTab
            case .ph: phTab
            case .titration: titrationTab
            }
        }
        .onAppear(perform: takeHandOff)
        .onChange(of: tab) { _, t in ChemPrefs.set(.solution, "tab", t.rawValue) }
        .onChange(of: molarity) { _, f in ChemPrefs.set(.solution, "molarity", Self.text(f)) }
        .onChange(of: dilution) { _, f in ChemPrefs.set(.solution, "dilution", Self.text(f)) }
        .onChange(of: ph) { _, f in ChemPrefs.set(.solution, "ph", Self.text(f)) }
        .onChange(of: titration) { _, f in ChemPrefs.set(.solution, "titration", Self.text(f)) }
    }

    private static func text(_ f: [String: String]) -> String { f.keys.sorted().map { "\($0)=\(f[$0] ?? "")" }.joined(separator: ",") }

    /// (1.3.83) What the pin had, its panel opened full screen: which of pH, pOH, [H⁺] and [OH⁻], and its value.
    private func takeHandOff() {
        guard let given = ToolsCenter.shared.takeInfo("given"), let value = ToolsCenter.shared.takeInfo("value") else { return }
        ph["given"] = given
        ph["value"] = value
        tab = .ph
    }

    private func field(_ dict: [String: String], _ name: String, label: String? = nil, _ convert: (Double) -> Double = { $0 }) -> ChemGas.Field {
        ChemGas.Field(name: label ?? name, text: dict[name] ?? "", value: ChemNumber.number(dict[name] ?? "").map(convert))
    }

    // MARK: Molarity

    private var molarityTab: some View {
        let grams = molarity["amountUnit"] == "g"
        let inML = molarity["volumeUnit"] != "L"
        let formula = try? ChemFormula.parse(molarity["formula"] ?? "").get()
        let molarMass = formula?.molarMass
        // (grams of a formula it cannot read: the amount cannot be read)
        let amount = ChemGas.Field(name: "the amount", text: molarity["amount"] ?? "", value: ChemNumber.number(molarity["amount"] ?? "").flatMap { v in
            grams ? molarMass.map { v / $0 } : v
        })
        let volume = field(molarity, "volume", label: "the volume") { inML ? $0 / 1000 : $0 }
        let conc = field(molarity, "molarity", label: "the molarity")
        let result = ChemSolution.molarity([
            ChemGas.Field(name: "moles", text: amount.text, value: amount.value), ChemGas.Field(name: "volume", text: volume.text, value: volume.value),
            ChemGas.Field(name: "molarity", text: conc.text, value: conc.value),
        ])
        let solved = try? result.get()
        let moles = solved?.0 == "moles" ? solved?.1 : amount.value
        let litres = solved?.0 == "volume" ? solved?.1 : volume.value
        let gramsOf = moles.flatMap { n in molarMass.map { n * $0 } }
        return ToolColumns(sideWidth: 380, breakpoint: 980) {
            VStack(alignment: .leading, spacing: 20) {
                VStack(spacing: 10) {
                    HStack(spacing: 10) {
                        Text("Solute").font(.sCallout).foregroundStyle(.secondary).frame(width: 110, alignment: .leading)
                        TextField("NaCl", text: gasBinding($molarity, "formula"))
                            .textFieldStyle(.roundedBorder)
                            .font(.sBody)
                            .frame(width: 140)
                            .accessibilityLabel("The solute's formula")
                        Text(molarMass.map { "\(ChemNumber.fixed($0, 3)) g/mol" } ?? "a formula, for grams").font(.sCaption).foregroundStyle(.secondary)
                        Spacer(minLength: 0)
                    }
                    .padding(.horizontal, 14)
                    .padding(.vertical, 4)
                    solutionRow("Amount", gasBinding($molarity, "amount"), solved: solved?.0 == "moles" ? molesShown(solved?.1, grams: gramsOf) : nil) {
                        Picker("Unit", selection: gasBinding($molarity, "amountUnit")) {
                            Text("mol").tag("mol")
                            Text("g").tag("g")
                        }
                    }
                    solutionRow("Volume", gasBinding($molarity, "volume"), solved: solved?.0 == "volume" ? solved.map { "\(ChemNumber.sig(inML ? $0.1 * 1000 : $0.1)) \(inML ? "mL" : "L")" } : nil) {
                        Picker("Unit", selection: gasBinding($molarity, "volumeUnit")) {
                            Text("mL").tag("mL")
                            Text("L").tag("L")
                        }
                    }
                    solutionRow("Molarity", gasBinding($molarity, "molarity"), solved: solved?.0 == "molarity" ? solved.map { "\(ChemNumber.sig($0.1)) M" } : nil) {
                        Text("mol/L").font(.sCallout).foregroundStyle(.secondary)
                    }
                }
                .padding(.vertical, 6)
                .card(radius: 14)
                Text("Fill in two; the third is solved.").font(.sFootnote).foregroundStyle(.secondary)
                VStack(alignment: .leading, spacing: 10) {
                    switch result {
                    case .success(let (which, v)):
                        let shown = which == "moles" ? (molesShown(v, grams: gramsOf) ?? "") : (which == "volume" ? "\(ChemNumber.sig(inML ? v * 1000 : v)) \(inML ? "mL" : "L")" : "\(ChemNumber.sig(v)) M")
                        bigAnswer(which == "moles" ? "Amount" : which.capitalized, shown)
                    case .failure(let e):
                        if grams, !amount.isBlank, molarMass == nil {
                            ToolNote(text: "Give the solute's formula to turn grams into moles.")
                        } else if e == .tooManyBlank || e == .nothingBlank {
                            Text(e.message).font(.sBody).foregroundStyle(.secondary)
                        } else {
                            ToolNote(text: e.message)
                        }
                    }
                }
                .padding(24)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card(radius: 22)
            }
        } side: {
            PageSection(title: "Mass % and ppm") {
                if let g = gramsOf, let l = litres, let pct = ChemSolution.massPercent(grams: g, litres: l), let ppm = ChemSolution.ppm(grams: g, litres: l) {
                    VStack(spacing: 0) {
                        BioValueRow(label: "Solute", value: "\(ChemNumber.sig(g)) g")
                        BioValueRow(label: "Mass %", value: "\(ChemNumber.sig(pct)) %")
                        BioValueRow(label: "ppm", value: "\(ChemNumber.sig(ppm)) mg/L", last: true)
                    }
                    .card(radius: 14)
                    Text("For a dilute, water-like solution (1.00 g/mL): the solute's grams over the solution's, and milligrams per litre.")
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                } else {
                    Text("With the solute's formula and the volume: its share of the solution's mass, and parts per million.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private func molesShown(_ moles: Double?, grams: Double?) -> String? {
        guard let moles else { return nil }
        if let grams { return "\(ChemNumber.sig(moles)) mol · \(ChemNumber.sig(grams)) g" }
        return "\(ChemNumber.sig(moles)) mol"
    }

    /// A field's row: its name, the field — or the answer in its place when it is the one solved — and its unit.
    private func solutionRow<U: View>(_ name: String, _ text: Binding<String>, solved: String?, @ViewBuilder unit: () -> U) -> some View {
        HStack(spacing: 10) {
            Text(name).font(.sCallout).foregroundStyle(.secondary).frame(width: 110, alignment: .leading)
            if let solved {
                Text(solved)
                    .font(.sBody.weight(.semibold).monospacedDigit())
                    .foregroundStyle(ToolKind.solution.color)
                    .frame(minWidth: 140, alignment: .leading)
                    .accessibilityLabel("\(name), solved: \(solved)")
            } else {
                TextField(name, text: text)
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody)
                    .frame(width: 140)
                    .accessibilityLabel(name)
            }
            unit()
                .labelsHidden()
                .frame(maxWidth: 120)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 4)
    }

    private func bigAnswer(_ name: String, _ value: String) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(name).font(.sBody).foregroundStyle(.secondary)
                Text(value)
                    .font(.system(size: 34, weight: .bold, design: .rounded).monospacedDigit())
                    .lineLimit(1)
                    .minimumScaleFactor(0.5)
                    .textSelection(.enabled)
                    .contentTransition(.numericText())
            }
            Button {
                copyToPasteboard("\(name): \(value)")
            } label: {
                Label("Copy", systemImage: "doc.on.doc")
            }
            .glassButton()
            .help("Copy \(value)")
        }
        .animation(reduceMotion ? nil : Motion.snappy, value: value)
    }

    // MARK: Dilution

    private var dilutionTab: some View {
        let unit = dilution["unit"] == "L" ? "L" : "mL"
        let fields = ["M1", "V1", "M2", "V2"].map { field(dilution, $0) }
        let result = ChemSolution.dilution(fields)
        let solved = try? result.get()
        func shown(_ s: (String, Double)?) -> String? { s.map { "\(ChemNumber.sig($0.1)) \($0.0.hasPrefix("M") ? "M" : unit)" } }
        return ToolColumns(sideWidth: 360, breakpoint: 960) {
            VStack(alignment: .leading, spacing: 20) {
                Text("How much stock? Leave the one you want blank.").font(.sBody).foregroundStyle(.secondary)
                HStack(alignment: .top, spacing: 14) {
                    dilutionCard("Stock", "1", unit: unit, solved: solved)
                    dilutionCard("Final", "2", unit: unit, solved: solved)
                }
                HStack(spacing: 10) {
                    Text("Volumes in").font(.sCallout).foregroundStyle(.secondary)
                    Picker("Unit", selection: gasBinding($dilution, "unit")) {
                        Text("mL").tag("mL")
                        Text("L").tag("L")
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .frame(width: 110)
                }
                VStack(alignment: .leading, spacing: 10) {
                    (Text("M").italic() + Text("₁") + Text("V").italic() + Text("₁") + Text(" = ") + Text("M").italic() + Text("₂") + Text("V").italic() + Text("₂"))
                        .font(.system(size: 26, weight: .medium, design: .serif))
                    switch result {
                    case .success(let s):
                        bigAnswer(s.0.hasPrefix("M") ? (s.0 == "M1" ? "Stock concentration" : "Final concentration") : (s.0 == "V1" ? "Stock to take" : "Final volume"), shown(s) ?? "")
                        if s.0 == "V1" { Text("Measure \(shown(s) ?? "") of the stock and dilute it to \(dilution["V2"] ?? "") \(unit).").font(.sCallout).foregroundStyle(.secondary) }
                        if s.0 == "V2" { Text("Dilute \(dilution["V1"] ?? "") \(unit) of the stock to \(shown(s) ?? "") in all.").font(.sCallout).foregroundStyle(.secondary) }
                    case .failure(let e):
                        if e == .tooManyBlank || e == .nothingBlank {
                            Text(e.message).font(.sBody).foregroundStyle(.secondary)
                        } else {
                            ToolNote(text: e.message)
                        }
                    }
                }
                .padding(24)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card(radius: 22)
            }
        } side: {
            PageSection(title: "How") {
                Text("The moles of solute don't change when water is added: the stock's concentration times its volume is the final concentration times the final volume. A dilution never ends stronger than its stock.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private func dilutionCard(_ title: String, _ k: String, unit: String, solved: (String, Double)?) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(.sHeadline)
            ForEach(["M", "V"], id: \.self) { q in
                let key = "\(q)\(k)"
                HStack(spacing: 8) {
                    (Text(q).italic() + Text(k == "1" ? "₁" : "₂"))
                        .font(.system(size: 17, weight: .bold, design: .serif))
                        .frame(width: 30, alignment: .trailing)
                    if solved?.0 == key, let s = solved {
                        Text("\(ChemNumber.sig(s.1)) \(q == "M" ? "M" : unit)")
                            .font(.sBody.weight(.semibold).monospacedDigit())
                            .foregroundStyle(ToolKind.solution.color)
                            .accessibilityLabel("\(key), solved")
                    } else {
                        TextField(key, text: gasBinding($dilution, key))
                            .textFieldStyle(.roundedBorder)
                            .font(.sBody)
                            .frame(width: 100)
                            .accessibilityLabel(q == "M" ? "\(title) concentration" : "\(title) volume")
                        Text(q == "M" ? "M" : unit).font(.sCallout).foregroundStyle(.secondary)
                    }
                }
            }
        }
        .padding(14)
        .frame(minWidth: 200, alignment: .leading)
        .card(radius: 14)
    }

    // MARK: pH

    private var given: ChemSolution.Given { ChemSolution.Given(rawValue: ph["given"] ?? "") ?? .pH }
    private var kind: ChemSolution.Kind { ChemSolution.Kind(rawValue: ph["kind"] ?? "") ?? .weakAcid }

    private var phTab: some View {
        let value = ChemNumber.number(ph["value"] ?? "")
        let acidity = value.flatMap { ChemSolution.acidity(given, $0) }
        let conc = ChemNumber.number(ph["c"] ?? "")
        let k = ChemNumber.number(ph["K"] ?? "")
        let fromConc = conc.flatMap { ChemSolution.fromConcentration(kind, c: $0, k: k) }
        return ToolColumns(sideWidth: 400, breakpoint: 1000) {
            VStack(alignment: .leading, spacing: 20) {
                VStack(alignment: .leading, spacing: 10) {
                    HStack(spacing: 10) {
                        Picker("Given", selection: Binding(get: { given }, set: { ph["given"] = $0.rawValue })) {
                            ForEach(ChemSolution.Given.allCases, id: \.self) { g in Text(g.label).tag(g) }
                        }
                        .pickerStyle(.segmented)
                        .labelsHidden()
                        .frame(width: 280)
                        TextField(given == .pH ? "3" : "0.001", text: gasBinding($ph, "value"))
                            .textFieldStyle(.roundedBorder)
                            .font(.sBody)
                            .frame(width: 120)
                            .accessibilityLabel(given.label)
                        if given == .h || given == .oh { Text("M").font(.sCallout).foregroundStyle(.secondary) }
                    }
                    Text("One of them; the other three come out. Kw = 1.0 × 10⁻¹⁴ at 25 °C — other temperatures aren't modelled here.")
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                VStack(alignment: .leading, spacing: 14) {
                    if let a = acidity {
                        acidityRows(a)
                    } else if !(ph["value"] ?? "").trimmingCharacters(in: .whitespaces).isEmpty {
                        ToolNote(text: value == nil ? "Can't read \(given.label) — is it a number?" : "\(given.label) must be more than 0.")
                    } else {
                        Text("Type a value.").font(.sBody).foregroundStyle(.secondary)
                    }
                    PHStrip(pH: acidity?.pH)
                }
                .padding(24)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card(radius: 22)
            }
        } side: {
            PageSection(title: "From a concentration") {
                VStack(alignment: .leading, spacing: 12) {
                    Picker("Kind", selection: Binding(get: { kind }, set: { ph["kind"] = $0.rawValue })) {
                        ForEach(ChemSolution.Kind.allCases, id: \.self) { k in Text(k.label).tag(k) }
                    }
                    .labelsHidden()
                    .frame(maxWidth: 220, alignment: .leading)
                    HStack(spacing: 8) {
                        TextField("0.10", text: gasBinding($ph, "c"))
                            .textFieldStyle(.roundedBorder)
                            .font(.sBody)
                            .frame(width: 100)
                            .accessibilityLabel("Concentration")
                        Text("M").font(.sCallout).foregroundStyle(.secondary)
                        if kind.weak {
                            Text(kind.constant).font(.sCallout).foregroundStyle(.secondary).padding(.leading, 6)
                            TextField("1.8e-5", text: gasBinding($ph, "K"))
                                .textFieldStyle(.roundedBorder)
                                .font(.sBody)
                                .frame(width: 100)
                                .accessibilityLabel(kind.constant)
                        }
                    }
                    if let r = fromConc {
                        VStack(spacing: 0) {
                            BioValueRow(label: "pH", value: ChemNumber.fixed(r.acidity.pH, 2))
                            BioValueRow(label: "pOH", value: ChemNumber.fixed(r.acidity.pOH, 2))
                            BioValueRow(label: "[H⁺]", value: "\(ChemNumber.pretty(ChemNumber.sig(r.acidity.h, 3))) M")
                            BioValueRow(label: "[OH⁻]", value: "\(ChemNumber.pretty(ChemNumber.sig(r.acidity.oh, 3))) M", last: r.ionised == nil)
                            if let i = r.ionised { BioValueRow(label: "Ionised", value: "\(ChemNumber.fixed(i, 2)) %", last: true) }
                        }
                        .card(radius: 14)
                        PHStrip(pH: r.acidity.pH, compact: true)
                        Text(kind.weak ? "The quadratic x² + \(kind.constant)·x − \(kind.constant)·C = 0 solved exactly, not the 5 % shortcut." : "Water's own ions are counted, so a very dilute one never crosses 7.")
                            .font(.sFootnote)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    } else if kind.weak, conc != nil, k == nil {
                        Text("Give \(kind.constant) for a weak one.").font(.sCallout).foregroundStyle(.secondary)
                    } else {
                        Text("A concentration, and for a weak acid or base its constant.").font(.sCallout).foregroundStyle(.secondary)
                    }
                }
            }
        }
    }

    private func acidityRows(_ a: ChemSolution.Acidity) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text("pH \(ChemNumber.fixed(a.pH, 2))")
                    .font(.system(size: 40, weight: .bold, design: .rounded).monospacedDigit())
                    .textSelection(.enabled)
                    .contentTransition(.numericText())
                Text(a.word).font(.sBody).foregroundStyle(.secondary)
            }
            HStack(spacing: 18) {
                value("pOH", ChemNumber.fixed(a.pOH, 2))
                value("[H⁺]", "\(ChemNumber.pretty(ChemNumber.sig(a.h, 3))) M")
                value("[OH⁻]", "\(ChemNumber.pretty(ChemNumber.sig(a.oh, 3))) M")
            }
            Button {
                copyToPasteboard("pH \(ChemNumber.fixed(a.pH, 2)), pOH \(ChemNumber.fixed(a.pOH, 2)), [H+] \(ChemNumber.sig(a.h, 3)) M, [OH-] \(ChemNumber.sig(a.oh, 3)) M")
            } label: {
                Label("Copy", systemImage: "doc.on.doc")
            }
            .glassButton()
            .help("Copy all four")
        }
        .animation(reduceMotion ? nil : Motion.snappy, value: a.pH)
    }

    private func value(_ label: String, _ v: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(label).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
            Text(v).font(.sHeadline.monospacedDigit()).textSelection(.enabled)
        }
        .accessibilityElement(children: .combine)
    }

    // MARK: Titration

    private var titrationKind: ChemSolution.TitrationKind { ChemSolution.TitrationKind(rawValue: titration["kind"] ?? "") ?? .weakAcidStrongBase }

    private var titrationTab: some View {
        let kind = titrationKind
        let ca = ChemNumber.number(titration["Ca"] ?? ""), va = ChemNumber.number(titration["Va"] ?? ""), ct = ChemNumber.number(titration["Ct"] ?? "")
        let k = ChemNumber.number(titration["K"] ?? "")
        let t = ca.flatMap { c in va.flatMap { v in ct.flatMap { ChemSolution.titration(kind, analyte: c, analyteVolume: v, titrant: $0, k: k) } } }
        let pKa = ChemNumber.number(titration["pKa"] ?? ""), base = ChemNumber.number(titration["base"] ?? ""), acid = ChemNumber.number(titration["acid"] ?? "")
        let buffer = pKa.flatMap { p in base.flatMap { b in acid.flatMap { ChemSolution.bufferPH(pKa: p, base: b, acid: $0) } } }
        return ToolColumns(sideWidth: 420, breakpoint: 1040) {
            VStack(alignment: .leading, spacing: 20) {
                Picker("Kind", selection: Binding(get: { kind }, set: { titration["kind"] = $0.rawValue })) {
                    ForEach(ChemSolution.TitrationKind.allCases, id: \.self) { k in Text(k.label).tag(k) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(maxWidth: 560)
                VStack(spacing: 10) {
                    solutionRow(kind.acidAnalyte ? "Acid" : "Base", gasBinding($titration, "Ca"), solved: nil) { Text("M").font(.sCallout).foregroundStyle(.secondary) }
                    solutionRow("Its volume", gasBinding($titration, "Va"), solved: nil) { Text("mL").font(.sCallout).foregroundStyle(.secondary) }
                    solutionRow(kind.acidAnalyte ? "Base added" : "Acid added", gasBinding($titration, "Ct"), solved: nil) { Text("M").font(.sCallout).foregroundStyle(.secondary) }
                    if kind.weak {
                        solutionRow(kind.constant, gasBinding($titration, "K"), solved: nil) { Text(kind.constant == "Ka" ? "of the acid" : "of the base").font(.sCallout).foregroundStyle(.secondary) }
                    }
                }
                .padding(.vertical, 6)
                .card(radius: 14)
                if let t {
                    TitrationCurve(titration: t, kind: kind)
                        .frame(height: 300)
                        .frame(maxWidth: .infinity)
                        .padding(16)
                        .card(radius: 16)
                        .accessibilityLabel("The titration curve: pH from \(ChemNumber.fixed(t.start, 2)) to \(ChemNumber.fixed(t.after, 2)), equivalence at \(ChemNumber.sig(t.equivalence)) mL, pH \(ChemNumber.fixed(t.atEquivalence, 2))")
                } else {
                    Text(kind.weak && k == nil && ca != nil ? "Give \(kind.constant) to draw the curve." : "The concentrations and the volume draw the curve.")
                        .font(.sBody)
                        .foregroundStyle(.secondary)
                }
            }
        } side: {
            VStack(alignment: .leading, spacing: 20) {
                PageSection(title: "Points") {
                    if let t {
                        VStack(spacing: 0) {
                            BioValueRow(label: "Equivalence volume", value: "\(ChemNumber.sig(t.equivalence)) mL")
                            BioValueRow(label: "pH at the start", value: ChemNumber.fixed(t.start, 2))
                            BioValueRow(label: kind.weak ? "Half-equivalence (= p\(kind.constant == "Ka" ? "Ka" : "Ka of the conjugate"))" : "Half-equivalence", value: ChemNumber.fixed(t.half, 2))
                            BioValueRow(label: "At equivalence", value: ChemNumber.fixed(t.atEquivalence, 2))
                            BioValueRow(label: "At twice the volume", value: ChemNumber.fixed(t.after, 2), last: true)
                        }
                        .card(radius: 14)
                        Button {
                            copyToPasteboard("Equivalence \(ChemNumber.sig(t.equivalence)) mL; pH start \(ChemNumber.fixed(t.start, 2)), half \(ChemNumber.fixed(t.half, 2)), equivalence \(ChemNumber.fixed(t.atEquivalence, 2)), twice \(ChemNumber.fixed(t.after, 2))")
                        } label: {
                            Label("Copy", systemImage: "doc.on.doc")
                        }
                        .glassButton()
                        .help("Copy the points")
                        Text("Each point is the [H⁺] that balances the charges — exact, with Henderson–Hasselbalch's answer in the buffer region.")
                            .font(.sFootnote)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    } else {
                        Text("The equivalence volume and the pH at the start, half-way, at equivalence and past it.").font(.sCallout).foregroundStyle(.secondary)
                    }
                }
                PageSection(title: "Buffer") {
                    VStack(alignment: .leading, spacing: 10) {
                        Text("pH = pKa + log([A⁻]/[HA])").font(.system(size: 17, weight: .medium, design: .serif))
                        HStack(spacing: 8) {
                            smallField("pKa", "pKa", "4.74")
                            smallField("[A⁻]", "base", "0.1")
                            smallField("[HA]", "acid", "0.1")
                        }
                        if let b = buffer {
                            Text("pH \(ChemNumber.fixed(b, 2))")
                                .font(.system(size: 26, weight: .bold, design: .rounded).monospacedDigit())
                                .textSelection(.enabled)
                                .contentTransition(.numericText())
                        } else {
                            Text("Its pKa and the two parts, in the same unit.").font(.sCallout).foregroundStyle(.secondary)
                        }
                    }
                    .padding(16)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .card(radius: 16)
                }
            }
        }
    }

    private func smallField(_ label: String, _ key: String, _ prompt: String) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(label).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
            TextField(prompt, text: gasBinding($titration, key))
                .textFieldStyle(.roundedBorder)
                .font(.sBody)
                .frame(width: 90)
                .accessibilityLabel(label)
        }
    }
}

/// The 0–14 strip, red through green to violet, with a marker at the pH (clamped to the strip).
struct PHStrip: View {
    let pH: Double?
    var compact = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    static let colors: [Color] = [Color(hex: "#e5342b"), Color(hex: "#f4842a"), Color(hex: "#f5c42a"), Color(hex: "#9ccb3b"), Color(hex: "#3ab46a"),
                                  Color(hex: "#2aa7a0"), Color(hex: "#2f7fd0"), Color(hex: "#5c4fc9"), Color(hex: "#8a3fb8")]

    var body: some View {
        VStack(spacing: 4) {
            GeometryReader { g in
                let w = g.size.width
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .fill(LinearGradient(colors: Self.colors, startPoint: .leading, endPoint: .trailing))
                        .frame(height: compact ? 10 : 16)
                    if let pH {
                        let x = CGFloat(min(max(pH, 0), 14) / 14) * w
                        Circle()
                            .fill(Color.white)
                            .frame(width: compact ? 14 : 20, height: compact ? 14 : 20)
                            .overlay(Circle().strokeBorder(Color.black.opacity(0.35), lineWidth: 1.5))
                            .shadow(color: .black.opacity(0.25), radius: 2, y: 1)
                            .offset(x: x - (compact ? 7 : 10))
                            .animation(reduceMotion ? nil : Motion.snappy, value: pH)
                    }
                }
                .frame(height: compact ? 14 : 20)
            }
            .frame(height: compact ? 14 : 20)
            HStack {
                Text("0").frame(maxWidth: .infinity, alignment: .leading)
                Text("acidic").frame(maxWidth: .infinity)
                Text("7")
                Text("basic").frame(maxWidth: .infinity)
                Text("14").frame(maxWidth: .infinity, alignment: .trailing)
            }
            .font(.sCaption2)
            .foregroundStyle(.secondary)
            .monospacedDigit()
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(pH.map { "pH \(ChemNumber.fixed($0, 2)) on a strip from 0 to 14" } ?? "A pH strip from 0 to 14")
    }
}

/// The titration curve: pH against the titrant's volume, from none to twice the equivalence volume, with the
/// equivalence point marked and its volume and pH said.
struct TitrationCurve: View {
    let titration: ChemSolution.Titration
    let kind: ChemSolution.TitrationKind

    var body: some View {
        GeometryReader { g in
            let left: CGFloat = 34, bottom: CGFloat = 24, top: CGFloat = 8, right: CGFloat = 12
            let w = g.size.width - left - right, h = g.size.height - top - bottom
            let maxV = max(titration.equivalence * 2, 1e-9)
            let x: (Double) -> CGFloat = { v in left + CGFloat(v / maxV) * w }
            let y: (Double) -> CGFloat = { pH in top + CGFloat(1 - min(max(pH, 0), 14) / 14) * h }
            ZStack(alignment: .topLeading) {
                // the grid: pH 0, 7, 14 and the volume's quarters
                Path { p in
                    for pH in [0.0, 7.0, 14.0] {
                        p.move(to: CGPoint(x: left, y: y(pH)))
                        p.addLine(to: CGPoint(x: left + w, y: y(pH)))
                    }
                    p.move(to: CGPoint(x: left, y: top))
                    p.addLine(to: CGPoint(x: left, y: top + h))
                }
                .stroke(Color.primary.opacity(0.15), lineWidth: 1)
                ForEach([0.0, 7.0, 14.0], id: \.self) { pH in
                    Text(String(Int(pH))).font(.sCaption2).foregroundStyle(.secondary).position(x: left - 14, y: y(pH))
                }
                Text("0 mL").font(.sCaption2).foregroundStyle(.secondary).position(x: left + 14, y: top + h + 12)
                Text("\(ChemNumber.sig(maxV)) mL").font(.sCaption2).foregroundStyle(.secondary).position(x: left + w - 24, y: top + h + 12)
                // the equivalence line and point
                Path { p in
                    p.move(to: CGPoint(x: x(titration.equivalence), y: top))
                    p.addLine(to: CGPoint(x: x(titration.equivalence), y: top + h))
                }
                .stroke(ToolKind.solution.color.opacity(0.5), style: StrokeStyle(lineWidth: 1, dash: [4, 4]))
                // the curve
                Path { p in
                    for (i, pt) in titration.curve.enumerated() {
                        let q = CGPoint(x: x(pt.volume), y: y(pt.pH))
                        if i == 0 { p.move(to: q) } else { p.addLine(to: q) }
                    }
                }
                .stroke(ToolKind.solution.color, style: StrokeStyle(lineWidth: 2.5, lineCap: .round, lineJoin: .round))
                Circle()
                    .fill(ToolKind.solution.color)
                    .frame(width: 10, height: 10)
                    .overlay(Circle().strokeBorder(Color.white, lineWidth: 1.5))
                    .position(x: x(titration.equivalence), y: y(titration.atEquivalence))
                Text("\(ChemNumber.sig(titration.equivalence)) mL · pH \(ChemNumber.fixed(titration.atEquivalence, 2))")
                    .font(.sCaption.weight(.medium))
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(Color.primary.opacity(0.06), in: Capsule())
                    .position(x: min(max(x(titration.equivalence) + (kind.acidAnalyte ? 70 : -70), left + 70), left + w - 70),
                              y: y(titration.atEquivalence) + (kind.acidAnalyte ? 16 : -16))
            }
        }
    }
}

/// Solutions & pH's pin, opened: the pH tab small — one of pH, pOH, [H⁺] and [OH⁻] typed, the other three shown;
/// Return opens the tool whole on it.
struct SolutionsPHCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var given = SolutionsPHCompact.launch.0
    @State private var value = SolutionsPHCompact.launch.1

    /// The launch's -SimplSolutionValues "pH=3" (or pOH, H, OH), for a picture.
    private static var launch: (ChemSolution.Given, String) {
        let d = ChemLaunchValues.parse(UserDefaults.standard.string(forKey: "SimplSolutionValues"))
        for (key, g) in [("pH", ChemSolution.Given.pH), ("pOH", .pOH), ("H", .h), ("OH", .oh)] {
            if let v = d[key] { return (g, ChemLaunchValues.split(v).number) }
        }
        return (.pH, "")
    }

    private var acidity: ChemSolution.Acidity? { ChemNumber.number(value).flatMap { ChemSolution.acidity(given, $0) } }

    /// (1.3.83) What Open Full Screen carries: which quantity and its value, once typed.
    private var carried: [String: String] { value.trimmingCharacters(in: .whitespaces).isEmpty ? [:] : ["given": given.rawValue, "value": value] }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                Picker("Given", selection: $given) {
                    ForEach(ChemSolution.Given.allCases, id: \.self) { g in Text(g.label).tag(g) }
                }
                .labelsHidden()
                .frame(width: 90)
                TextField(given == .pH ? "3" : "0.001", text: $value)
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody)
                    .onSubmit { openFull(carried) }
                    .accessibilityLabel(given.label)
            }
            if let a = acidity {
                Text("pH \(ChemNumber.fixed(a.pH, 2)) · \(a.word)")
                    .font(.system(size: 24, weight: .bold, design: .rounded).monospacedDigit())
                    .textSelection(.enabled)
                Text("pOH \(ChemNumber.fixed(a.pOH, 2)) · [H⁺] \(ChemNumber.pretty(ChemNumber.sig(a.h, 3))) M · [OH⁻] \(ChemNumber.pretty(ChemNumber.sig(a.oh, 3))) M")
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                PHStrip(pH: a.pH, compact: true)
            } else {
                Text(value.trimmingCharacters(in: .whitespaces).isEmpty ? "One of them: the other three come out." : "Can't read that — a number more than 0.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        }
        .quickCarry(carried)
    }
}

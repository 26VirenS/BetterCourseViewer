import AppKit
import SwiftUI

// (1.3.93) Gas Laws: two tabs. Ideal gas — P, V, n and T each with its own units (atm, kPa, mmHg, bar, psi; L, mL,
// m³; mol or grams of a formula; K, °C, °F); leave one blank and it is solved, the formula shown with that letter lit.
// Combined — P₁V₁/T₁ = P₂V₂/T₂, six fields, one blank solved; a chip holds a pair constant (Boyle, Charles,
// Gay-Lussac) and hides it. Beside them a piston: a cylinder whose piston rises with the volume, with dots as dense
// as the gas and a colour as warm as its temperature, moving as a value changes (200 ms, still with Reduce Motion).
// The arithmetic is Model/ChemGas.swift; each tab keeps its values and units (ChemPrefs `gas.*`); the launch's
// -SimplGas ideal|combined and -SimplGasValues "P=1 atm,V=22.4 L,T=273.15 K" open it on a state for a picture. Its
// pin is the Ideal gas tab small.

enum GasTab: String, CaseIterable, Identifiable {
    case ideal, combined

    var id: String { rawValue }
    var title: String { self == .ideal ? "Ideal gas" : "Combined" }
}

/// The units of the Ideal gas tab's four fields, or of the Combined tab's three quantities.
struct GasUnits: Equatable {
    var p: ChemGas.PressureUnit = .atm
    var v: ChemGas.VolumeUnit = .L
    var t: ChemGas.TemperatureUnit = .K
    /// The amount in grams of `formula` rather than moles (the Ideal gas tab).
    var grams = false
    var formula = ""

    /// As kept and as a launch gives them.
    var text: String { "P=\(p.rawValue),V=\(v.rawValue),T=\(t.rawValue),n=\(grams ? "g" : "mol"),formula=\(formula)" }

    init() {}

    init(text: String?) {
        let d = ChemLaunchValues.parse(text)
        if let u = ChemGas.PressureUnit.named(d["P"]) { p = u }
        if let u = ChemGas.VolumeUnit.named(d["V"]) { v = u }
        if let u = ChemGas.TemperatureUnit.named(d["T"]) { t = u }
        grams = d["n"] == "g"
        formula = d["formula"] ?? ""
    }
}

struct GasLawsTool: View {
    @State private var tab = GasLawsTool.firstTab
    @State private var ideal = GasLawsTool.firstValues(.ideal)
    @State private var idealUnits = GasLawsTool.firstUnits(.ideal)
    @State private var combined = GasLawsTool.firstValues(.combined)
    @State private var combinedUnits = GasLawsTool.firstUnits(.combined)
    @State private var constant = GasLawsTool.firstConstant
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    // MARK: Opening

    private static var launchedHere: Bool { ChemTools.launched == .gas }

    private static var firstTab: GasTab {
        if launchedHere, let t = UserDefaults.standard.string(forKey: "SimplGas").flatMap({ GasTab(rawValue: $0) }) { return t }
        return ChemPrefs.string(.gas, "tab").flatMap { GasTab(rawValue: $0) } ?? .ideal
    }

    /// The launch's values, when it opened this tool on `tab`: "P=1 atm,V=22.4 L,T=273.15 K" (a value's unit sets
    /// its field's; "n=4 g H2" an amount in grams of a formula).
    private static func launchedValues(_ tab: GasTab) -> [String: String]? {
        guard launchedHere, firstTab == tab, let raw = UserDefaults.standard.string(forKey: "SimplGasValues"), !raw.isEmpty else { return nil }
        return ChemLaunchValues.parse(raw)
    }

    private static func firstValues(_ tab: GasTab) -> [String: String] {
        let keys = tab == .ideal ? ["P", "V", "n", "T"] : ["P1", "V1", "T1", "P2", "V2", "T2"]
        var out: [String: String] = [:]
        if let launched = launchedValues(tab) {
            for k in keys { out[k] = launched[k].map { ChemLaunchValues.split($0).number } ?? "" }
            return out
        }
        let kept = ChemLaunchValues.parse(ChemPrefs.string(.gas, tab == .ideal ? "ideal" : "combined"))
        for k in keys { out[k] = kept[k] ?? "" }
        if tab == .ideal && kept.isEmpty { out = ["P": "1", "V": "22.4", "n": "", "T": "273.15"] }
        if tab == .combined && kept.isEmpty { out = ["P1": "1", "V1": "2", "T1": "300", "P2": "2", "V2": "", "T2": "600"] }
        return out
    }

    private static func firstUnits(_ tab: GasTab) -> GasUnits {
        var u = GasUnits(text: ChemPrefs.string(.gas, tab == .ideal ? "idealUnits" : "combinedUnits"))
        guard let launched = launchedValues(tab) else { return u }
        for (k, raw) in launched {
            let unit = ChemLaunchValues.split(raw).unit
            if k.hasPrefix("P"), let x = ChemGas.PressureUnit.named(unit) { u.p = x }
            if k.hasPrefix("V"), let x = ChemGas.VolumeUnit.named(unit) { u.v = x }
            if k.hasPrefix("T"), let x = ChemGas.TemperatureUnit.named(unit) { u.t = x }
            if k == "n", let unit, unit.lowercased().hasPrefix("g") {
                u.grams = true
                u.formula = unit.split(separator: " ").dropFirst().joined(separator: " ")
            }
        }
        return u
    }

    private static var firstConstant: ChemGas.Constant? {
        if launchedHere, firstTab == .combined, let c = UserDefaults.standard.string(forKey: "SimplGasConstant").flatMap({ ChemGas.Constant(rawValue: $0) }) { return c }
        return ChemPrefs.string(.gas, "constant").flatMap { ChemGas.Constant(rawValue: $0) }
    }

    var body: some View {
        ToolPage {
            Picker("Tab", selection: $tab) {
                ForEach(GasTab.allCases) { t in Text(t.title).tag(t) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .frame(maxWidth: 320)
            .accessibilityLabel("Which law")
            ToolColumns(sideWidth: 360, breakpoint: 980) {
                switch tab {
                case .ideal: idealTab
                case .combined: combinedTab
                }
            } side: {
                pistonSide
            }
        }
        .onAppear(perform: takeHandOff)
        .onChange(of: tab) { _, t in ChemPrefs.set(.gas, "tab", t.rawValue) }
        .onChange(of: ideal) { _, v in ChemPrefs.set(.gas, "ideal", Self.valuesText(v)) }
        .onChange(of: combined) { _, v in ChemPrefs.set(.gas, "combined", Self.valuesText(v)) }
        .onChange(of: idealUnits) { _, u in ChemPrefs.set(.gas, "idealUnits", u.text) }
        .onChange(of: combinedUnits) { _, u in ChemPrefs.set(.gas, "combinedUnits", u.text) }
        .onChange(of: constant) { _, c in ChemPrefs.set(.gas, "constant", c?.rawValue) }
    }

    private static func valuesText(_ v: [String: String]) -> String {
        v.keys.sorted().map { "\($0)=\(v[$0] ?? "")" }.joined(separator: ",")
    }

    /// (1.3.83) What the pin's Ideal gas fields had, its panel opened full screen: "P=1 atm,V=22.4 L,T=273.15 K".
    private func takeHandOff() {
        guard let raw = ToolsCenter.shared.takeInfo("values"), !raw.isEmpty else { return }
        let d = ChemLaunchValues.parse(raw)
        for k in ["P", "V", "n", "T"] { ideal[k] = d[k].map { ChemLaunchValues.split($0).number } ?? "" }
        if let x = ChemGas.PressureUnit.named(d["P"].map { ChemLaunchValues.split($0).unit ?? "" }) { idealUnits.p = x }
        if let x = ChemGas.VolumeUnit.named(d["V"].map { ChemLaunchValues.split($0).unit ?? "" }) { idealUnits.v = x }
        if let x = ChemGas.TemperatureUnit.named(d["T"].map { ChemLaunchValues.split($0).unit ?? "" }) { idealUnits.t = x }
        tab = .ideal
    }

    // MARK: Ideal gas

    /// The four fields in the law's units: what was typed, converted.
    private var idealFields: (P: ChemGas.Field, V: ChemGas.Field, n: ChemGas.Field, T: ChemGas.Field) {
        let u = idealUnits
        let formulaMass = (try? ChemFormula.parse(u.formula).get())?.molarMass
        let n = ChemGas.Field(name: "n", text: ideal["n"] ?? "", value: ChemNumber.number(ideal["n"] ?? "").flatMap { v -> Double? in
            guard u.grams else { return v }
            return formulaMass.map { v / $0 }
        })
        return (Self.field("P", ideal["P"], { $0 * u.p.atm }), Self.field("V", ideal["V"], { $0 * u.v.litres }), n, Self.field("T", ideal["T"], { u.t.kelvin($0) }))
    }

    private static func field(_ name: String, _ text: String?, _ convert: (Double) -> Double) -> ChemGas.Field {
        ChemGas.Field(name: name, text: text ?? "", value: ChemNumber.number(text ?? "").map(convert))
    }

    private var idealResult: Result<(ChemGas.Variable, Double), ChemGas.SolveError> {
        let f = idealFields
        // (grams of a formula it cannot read: the amount cannot be read)
        if idealUnits.grams, !f.n.isBlank, (try? ChemFormula.parse(idealUnits.formula).get()) == nil { return .failure(.unreadable("the formula for n")) }
        return ChemGas.ideal(P: f.P, V: f.V, n: f.n, T: f.T)
    }

    private var idealTab: some View {
        let result = idealResult
        let solved = try? result.get()
        return VStack(alignment: .leading, spacing: 20) {
            VStack(spacing: 10) {
                gasRow("P", "Pressure", gasBinding($ideal, "P"), solved: solved?.0 == .P ? idealShown(solved) : nil) {
                    Picker("Unit", selection: $idealUnits.p) { ForEach(ChemGas.PressureUnit.allCases, id: \.self) { Text($0.label).tag($0) } }
                }
                gasRow("V", "Volume", gasBinding($ideal, "V"), solved: solved?.0 == .V ? idealShown(solved) : nil) {
                    Picker("Unit", selection: $idealUnits.v) { ForEach(ChemGas.VolumeUnit.allCases, id: \.self) { Text($0.label).tag($0) } }
                }
                gasRow("n", "Amount", gasBinding($ideal, "n"), solved: solved?.0 == .n ? idealShown(solved) : nil) {
                    Picker("Unit", selection: $idealUnits.grams) {
                        Text("mol").tag(false)
                        Text("g of…").tag(true)
                    }
                    if idealUnits.grams {
                        TextField("H2", text: $idealUnits.formula)
                            .textFieldStyle(.roundedBorder)
                            .font(.sBody)
                            .frame(width: 90)
                            .accessibilityLabel("The gas's formula")
                    }
                }
                gasRow("T", "Temperature", gasBinding($ideal, "T"), solved: solved?.0 == .T ? idealShown(solved) : nil) {
                    Picker("Unit", selection: $idealUnits.t) { ForEach(ChemGas.TemperatureUnit.allCases, id: \.self) { Text($0.label).tag($0) } }
                }
            }
            .padding(.vertical, 6)
            .card(radius: 14)
            Text("Leave the one you want blank.").font(.sFootnote).foregroundStyle(.secondary)
            answerCard(law: lawText(solved: solved?.0.rawValue, pieces: [("P", "P"), ("V", "V"), (" = ", nil), ("n", "n"), ("R", nil), ("T", "T")]),
                       result: result.map { v in (Self.name(v.0), idealShown(v) ?? "") }, note: "R = 0.082057 L·atm/(mol·K); the fields are converted to atm, L, mol and K.")
        }
    }

    /// The solved value in its field's unit (and for an amount in grams, in moles too).
    private func idealShown(_ solved: (ChemGas.Variable, Double)?) -> String? {
        guard let (which, v) = solved else { return nil }
        let u = idealUnits
        switch which {
        case .P: return "\(ChemNumber.sig(v / u.p.atm)) \(u.p.label)"
        case .V: return "\(ChemNumber.sig(v / u.v.litres)) \(u.v.label)"
        case .T: return "\(ChemNumber.sig(u.t.from(kelvin: v))) \(u.t.label)"
        case .n:
            if u.grams, let m = (try? ChemFormula.parse(u.formula).get())?.molarMass { return "\(ChemNumber.sig(v * m)) g of \(u.formula) (\(ChemNumber.sig(v)) mol)" }
            return "\(ChemNumber.sig(v)) mol"
        }
    }

    private static func name(_ v: ChemGas.Variable) -> String {
        switch v {
        case .P: return "Pressure"
        case .V: return "Volume"
        case .n: return "Amount"
        case .T: return "Temperature"
        }
    }

    /// A field's row: its letter and name, the field, its unit, and — when it is the one solved — the answer in place.
    private func gasRow<U: View>(_ letter: String, _ name: String, _ text: Binding<String>, solved: String?, @ViewBuilder unit: () -> U) -> some View {
        HStack(spacing: 10) {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text(letter).font(.system(size: 17, weight: .bold, design: .serif).italic()).frame(width: 18, alignment: .trailing)
                Text(name).font(.sCallout).foregroundStyle(.secondary)
            }
            .frame(width: 130, alignment: .leading)
            if let solved {
                Text(solved)
                    .font(.sBody.weight(.semibold).monospacedDigit())
                    .foregroundStyle(ToolKind.gas.color)
                    .frame(minWidth: 120, alignment: .leading)
                    .accessibilityLabel("\(name), solved: \(solved)")
            } else {
                TextField(letter, text: text)
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody)
                    .frame(width: 120)
                    .accessibilityLabel(name)
            }
            unit()
                .labelsHidden()
                .frame(maxWidth: 150)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 4)
    }

    /// The law written out, the solved letter lit: `pieces` are its parts, each with the letter it stands for (nil: none).
    private func lawText(solved: String?, pieces: [(String, String?)]) -> Text {
        var out = Text("")
        for (s, letter) in pieces {
            var t = Text(s)
            if letter != nil { t = t.italic() }
            if let letter, letter == solved { t = t.bold().foregroundColor(ToolKind.gas.color) }
            out = out + t
        }
        return out
    }

    private func answerCard(law: Text, result: Result<(String, String), ChemGas.SolveError>, note: String) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            law.font(.system(size: 26, weight: .medium, design: .serif))
            switch result {
            case .success(let (name, value)):
                HStack(alignment: .firstTextBaseline, spacing: 10) {
                    Text(name).font(.sBody).foregroundStyle(.secondary)
                    Text(value)
                        .font(.system(size: 36, weight: .bold, design: .rounded).monospacedDigit())
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
            case .failure(let e):
                if e == .tooManyBlank || e == .nothingBlank {
                    Text(e.message).font(.sBody).foregroundStyle(.secondary)
                } else {
                    ToolNote(text: e.message)
                }
            }
            Text(note).font(.sFootnote).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        .padding(24)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 22)
        .animation(reduceMotion ? nil : Motion.snappy, value: (try? result.get())?.1 ?? "")
    }

    // MARK: Combined

    private var combinedFields: [String: ChemGas.Field] {
        let u = combinedUnits
        var out: [String: ChemGas.Field] = [:]
        for k in ["P1", "P2"] { out[k] = Self.field(k, combined[k], { $0 * u.p.atm }) }
        for k in ["V1", "V2"] { out[k] = Self.field(k, combined[k], { $0 * u.v.litres }) }
        for k in ["T1", "T2"] { out[k] = Self.field(k, combined[k], { u.t.kelvin($0) }) }
        return out
    }

    private var combinedResult: Result<(String, Double), ChemGas.SolveError> { ChemGas.combined(combinedFields, constant: constant) }

    private var combinedTab: some View {
        let result = combinedResult
        let solved = try? result.get()
        let hidden = constant?.hidden ?? []
        return VStack(alignment: .leading, spacing: 20) {
            ChipScroller {
                HStack(spacing: 8) {
                    ForEach(ChemGas.Constant.allCases, id: \.self) { c in
                        Button(c.law) {
                            withAnimation(reduceMotion ? nil : Motion.snappy) { constant = constant == c ? nil : c }
                        }
                        .font(.sCallout)
                        .glassChip(ToolKind.gas.color, on: constant == c)
                        .help(constant == c ? "Back to the whole combined law" : "\(c.law): its fields left out")
                    }
                }
                .padding(.vertical, 2)
            }
            HStack(spacing: 12) {
                unitPicker("Pressure", selection: $combinedUnits.p, all: ChemGas.PressureUnit.allCases, label: \.label)
                unitPicker("Volume", selection: $combinedUnits.v, all: ChemGas.VolumeUnit.allCases, label: \.label)
                unitPicker("Temperature", selection: $combinedUnits.t, all: ChemGas.TemperatureUnit.allCases, label: \.label)
            }
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .top, spacing: 14) {
                    stateCard(1, hidden: hidden, solved: solved)
                    stateCard(2, hidden: hidden, solved: solved)
                }
                VStack(alignment: .leading, spacing: 14) {
                    stateCard(1, hidden: hidden, solved: solved)
                    stateCard(2, hidden: hidden, solved: solved)
                }
            }
            Text("Leave the one you want blank.").font(.sFootnote).foregroundStyle(.secondary)
            answerCard(law: combinedLaw(solved: solved?.0), result: result.map { ($0.0, combinedShown($0) ?? "") },
                       note: constant.map { "\($0.law): the same gas, its \($0.rawValue) unchanged." } ?? "The same amount of gas in two states; temperatures in kelvin.")
        }
    }

    private func unitPicker<U: Hashable>(_ name: String, selection: Binding<U>, all: [U], label: KeyPath<U, String>) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(name).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
            Picker(name, selection: selection) {
                ForEach(all, id: \.self) { u in Text(u[keyPath: label]).tag(u) }
            }
            .labelsHidden()
            .frame(width: 120)
        }
    }

    private func stateCard(_ k: Int, hidden: [String], solved: (String, Double)?) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("State \(k)").font(.sHeadline)
            ForEach(["P", "V", "T"], id: \.self) { q in
                let key = "\(q)\(k)"
                if !hidden.contains(key) {
                    HStack(spacing: 8) {
                        (Text(q).italic() + Text(k == 1 ? "₁" : "₂"))
                            .font(.system(size: 17, weight: .bold, design: .serif))
                            .frame(width: 30, alignment: .trailing)
                        if solved?.0 == key, let s = combinedShown(solved) {
                            Text(s)
                                .font(.sBody.weight(.semibold).monospacedDigit())
                                .foregroundStyle(ToolKind.gas.color)
                                .accessibilityLabel("\(key), solved: \(s)")
                        } else {
                            TextField(key, text: gasBinding($combined, key))
                                .textFieldStyle(.roundedBorder)
                                .font(.sBody)
                                .frame(width: 110)
                                .accessibilityLabel(key)
                            Text(q == "P" ? combinedUnits.p.label : (q == "V" ? combinedUnits.v.label : combinedUnits.t.label)).font(.sCallout).foregroundStyle(.secondary)
                        }
                    }
                }
            }
        }
        .padding(14)
        .frame(minWidth: 220, alignment: .leading)
        .card(radius: 14)
    }

    private func combinedShown(_ solved: (String, Double)?) -> String? {
        guard let (key, v) = solved else { return nil }
        let u = combinedUnits
        if key.hasPrefix("P") { return "\(ChemNumber.sig(v / u.p.atm)) \(u.p.label)" }
        if key.hasPrefix("V") { return "\(ChemNumber.sig(v / u.v.litres)) \(u.v.label)" }
        return "\(ChemNumber.sig(u.t.from(kelvin: v))) \(u.t.label)"
    }

    /// The combined law, or the one the chip picked, the solved letter lit.
    private func combinedLaw(solved: String?) -> Text {
        func side(_ k: Int) -> [(String, String?)] {
            let sub = k == 1 ? "₁" : "₂"
            var pieces: [(String, String?)] = []
            if constant != .P { pieces.append(("P\(sub)", "P\(k)")) }
            if constant != .V { pieces.append(("V\(sub)", "V\(k)")) }
            if constant != .T { pieces.append(("/T\(sub)", "T\(k)")) }
            return pieces
        }
        return lawText(solved: solved, pieces: side(1) + [(" = ", nil)] + side(2))
    }

    // MARK: The piston

    /// The gas as drawn: its volume in litres, its density in mol/L and its temperature in kelvin — from the Ideal
    /// gas tab's fields with the solved one, or the Combined tab's second state (its first until the second is whole).
    private var drawn: (litres: Double, moles: Double, kelvin: Double) {
        switch tab {
        case .ideal:
            let f = idealFields
            var p = f.P.value, v = f.V.value, n = f.n.value, t = f.T.value
            if let (which, x) = try? idealResult.get() {
                switch which {
                case .P: p = x
                case .V: v = x
                case .n: n = x
                case .T: t = x
                }
            }
            let litres = v ?? 22.4, kelvin = t ?? 273.15
            let moles = n ?? (p.map { $0 * litres / (ChemGas.R * kelvin) } ?? 1)
            return (litres, moles, kelvin)
        case .combined:
            var f = combinedFields
            if let (key, x) = try? combinedResult.get() { f[key] = ChemGas.Field(name: key, text: "", value: x) }
            func state(_ k: Int) -> (Double, Double, Double)? {
                let p = constant == .P ? (f["P1"]?.value ?? 1) : f["P\(k)"]?.value
                let v = constant == .V ? (f["V1"]?.value ?? 1) : f["V\(k)"]?.value
                let t = constant == .T ? (f["T1"]?.value ?? 273.15) : f["T\(k)"]?.value
                guard let p, let v, let t, t > 0 else { return nil }
                return (v, p * v / (ChemGas.R * t), t)
            }
            return state(2) ?? state(1) ?? (2, 0.08, 300)
        }
    }

    private var pistonSide: some View {
        let d = drawn
        return PageSection(title: "The gas") {
            GasPistonView(height: ChemGas.pistonHeight(litres: d.litres), dots: ChemGas.pistonDots(moles: d.moles, litres: d.litres), warmth: ChemGas.pistonWarmth(kelvin: d.kelvin))
                .frame(height: 300)
                .frame(maxWidth: .infinity)
                .card(radius: 16)
                .accessibilityLabel("A piston: \(ChemNumber.sig(d.litres)) litres, \(ChemNumber.sig(d.moles / d.litres)) moles per litre, \(ChemNumber.sig(d.kelvin)) kelvin")
            Text("The piston rises with the volume; the dots are as dense as the gas (n/V, or P/RT); the colour warms with the temperature.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// The piston: a cylinder with its piston `height` of the way up (0 to 1), `dots` molecules below it, coloured from
/// cold blue to hot red by `warmth`. A change moves the piston and the dots over 200 ms (none with Reduce Motion).
struct GasPistonView: View {
    let height: Double
    let dots: Int
    let warmth: Double
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var gas: Color {
        Color(red: 0.25 + 0.65 * warmth, green: 0.45 - 0.2 * abs(warmth - 0.5), blue: 0.95 - 0.75 * warmth)
    }

    var body: some View {
        GeometryReader { g in
            let w = min(g.size.width - 60, 150), h = g.size.height - 40
            let x0 = (g.size.width - w) / 2, y0: CGFloat = 20
            let gasHeight = h * CGFloat(height)
            let top = y0 + h - gasHeight
            ZStack(alignment: .topLeading) {
                // the cylinder
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(Color.primary.opacity(0.05))
                    .frame(width: w, height: h)
                    .offset(x: x0, y: y0)
                // the gas
                Rectangle()
                    .fill(LinearGradient(colors: [gas.opacity(0.55), gas.opacity(0.2)], startPoint: .bottom, endPoint: .top))
                    .frame(width: w, height: gasHeight)
                    .offset(x: x0, y: top)
                    .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous).size(width: w, height: h).offset(x: x0, y: y0))
                // the molecules, each in its own spot of the gas's room
                ForEach(0..<dots, id: \.self) { i in
                    let (fx, fy) = Self.spot(i)
                    Circle()
                        .fill(gas)
                        .frame(width: 7, height: 7)
                        .offset(x: x0 + 6 + fx * (w - 19), y: top + 6 + fy * max(gasHeight - 19, 0))
                }
                // the piston and its rod
                Rectangle()
                    .fill(Color.secondary)
                    .frame(width: 6, height: max(top - y0 + 4, 4))
                    .offset(x: x0 + w / 2 - 3, y: y0 - 4)
                RoundedRectangle(cornerRadius: 3, style: .continuous)
                    .fill(Color.primary.opacity(0.55))
                    .frame(width: w - 4, height: 10)
                    .offset(x: x0 + 2, y: top - 5)
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .strokeBorder(Color.primary.opacity(0.25), lineWidth: 1.5)
                    .frame(width: w, height: h)
                    .offset(x: x0, y: y0)
            }
            .animation(reduceMotion ? nil : .easeOut(duration: 0.2), value: height)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.2), value: dots)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.2), value: warmth)
        }
    }

    /// Where the i-th molecule sits, as a share of the room across and up: scattered, the same every time.
    private static func spot(_ i: Int) -> (CGFloat, CGFloat) {
        let a = Double(i) * 0.618033988749895
        let b = Double(i) * 0.754877666246693
        return (CGFloat(a - a.rounded(.down)), CGFloat(b - b.rounded(.down)))
    }
}

/// Gas Laws' pin, opened: the Ideal gas tab small — P, V, n and T with their units, one blank solved; Return opens the
/// tool whole on them.
struct GasLawsCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var values = GasLawsCompact.firstValues
    @State private var units = GasLawsCompact.firstUnits

    private static var launch: [String: String] { ChemLaunchValues.parse(UserDefaults.standard.string(forKey: "SimplGasValues")) }

    private static var firstValues: [String: String] {
        var out: [String: String] = ["P": "", "V": "", "n": "", "T": ""]
        for (k, v) in launch where out[k] != nil { out[k] = ChemLaunchValues.split(v).number }
        return out
    }

    private static var firstUnits: GasUnits {
        var u = GasUnits()
        if let x = ChemGas.PressureUnit.named(launch["P"].flatMap { ChemLaunchValues.split($0).unit }) { u.p = x }
        if let x = ChemGas.VolumeUnit.named(launch["V"].flatMap { ChemLaunchValues.split($0).unit }) { u.v = x }
        if let x = ChemGas.TemperatureUnit.named(launch["T"].flatMap { ChemLaunchValues.split($0).unit }) { u.t = x }
        return u
    }

    private var result: Result<(ChemGas.Variable, Double), ChemGas.SolveError> {
        func f(_ name: String, _ convert: (Double) -> Double) -> ChemGas.Field {
            ChemGas.Field(name: name, text: values[name] ?? "", value: ChemNumber.number(values[name] ?? "").map(convert))
        }
        return ChemGas.ideal(P: f("P", { $0 * units.p.atm }), V: f("V", { $0 * units.v.litres }), n: f("n", { $0 }), T: f("T", { units.t.kelvin($0) }))
    }

    /// (1.3.83) What Open Full Screen carries: the fields with their units, once anything is typed.
    private var carried: [String: String] {
        guard values.values.contains(where: { !$0.trimmingCharacters(in: .whitespaces).isEmpty }) else { return [:] }
        let parts = [("P", units.p.rawValue), ("V", units.v.rawValue), ("n", "mol"), ("T", units.t.rawValue)].map { k, u in "\(k)=\(values[k] ?? "") \(u)" }
        return ["values": parts.joined(separator: ",")]
    }

    private var shown: String {
        switch result {
        case .success(let (which, v)):
            switch which {
            case .P: return "P = \(ChemNumber.sig(v / units.p.atm)) \(units.p.label)"
            case .V: return "V = \(ChemNumber.sig(v / units.v.litres)) \(units.v.label)"
            case .n: return "n = \(ChemNumber.sig(v)) mol"
            case .T: return "T = \(ChemNumber.sig(units.t.from(kelvin: v))) \(units.t.label)"
            }
        case .failure(let e):
            return e.message
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 8) {
                row("P", gasBinding($values, "P")) { Picker("", selection: $units.p) { ForEach(ChemGas.PressureUnit.allCases, id: \.self) { Text($0.rawValue).tag($0) } } }
                row("V", gasBinding($values, "V")) { Picker("", selection: $units.v) { ForEach(ChemGas.VolumeUnit.allCases, id: \.self) { Text($0.label).tag($0) } } }
                row("n", gasBinding($values, "n")) { Text("mol").font(.sCallout).foregroundStyle(.secondary) }
                row("T", gasBinding($values, "T")) { Picker("", selection: $units.t) { ForEach(ChemGas.TemperatureUnit.allCases, id: \.self) { Text($0.label).tag($0) } } }
            }
            Text(shown)
                .font((try? result.get()) == nil ? .sCallout : .system(size: 22, weight: .bold, design: .rounded).monospacedDigit())
                .foregroundStyle((try? result.get()) == nil ? Color.secondary : Color.primary)
                .lineLimit(2)
                .minimumScaleFactor(0.6)
                .textSelection(.enabled)
                .fixedSize(horizontal: false, vertical: true)
        }
        .onSubmit { openFull(carried) }
        .quickCarry(carried)
    }

    private func row<U: View>(_ letter: String, _ text: Binding<String>, @ViewBuilder unit: () -> U) -> some View {
        GridRow {
            Text(letter).font(.system(size: 15, weight: .bold, design: .serif).italic()).frame(width: 16, alignment: .trailing)
            TextField(letter, text: text)
                .textFieldStyle(.roundedBorder)
                .font(.sBody)
                .frame(width: 90)
                .accessibilityLabel(letter)
            unit().labelsHidden().frame(width: 110, alignment: .leading)
        }
    }
}

/// One field of a dictionary of fields, bound: "" until something is typed in it.
func gasBinding(_ dict: Binding<[String: String]>, _ key: String) -> Binding<String> {
    Binding(get: { dict.wrappedValue[key] ?? "" }, set: { dict.wrappedValue[key] = $0 })
}

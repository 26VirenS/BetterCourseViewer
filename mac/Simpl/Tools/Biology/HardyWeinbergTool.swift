import SwiftUI

// Hardy–Weinberg (spec §4.7): allele and genotype frequencies worked out from any one value with each step shown, a
// genotype bar and a 10 × 10 grid of people; a population's counts tested against equilibrium (χ², df = 1); and a
// population simulated over generations — selection, mutation, migration, inbreeding and drift — with five condition
// chips that light up when broken. The arithmetic is HardyWeinbergModel.swift's; this file has the tool, its state, the
// Calculate page and the pin's quick version; HardyWeinbergTest.swift and HardyWeinbergSimulate.swift the other pages.

/// The genotypes' colours: AA blue, Aa orange, aa green-blue — validated together for colour-vision deficiency on light
/// and dark, and always shown with their names.
enum HWColors {
    static let AA = BioHexPair("#2a78d6", "#3987e5")
    static let Aa = BioHexPair("#eb6834", "#d95926")
    static let aa = BioHexPair("#1baf7a", "#199e70")
    /// The simulation's lines: the tool's green, deepened for contrast on light.
    static let line = BioHexPair("#5e8f22", "#9fd45a")

    static var all: [Color] { [AA.color, Aa.color, aa.color] }
}

/// A number as typed: "0.16", "16%", "1/2500" ("%" makes a frequency of a percent where a frequency is wanted).
enum HWInput {
    static func number(_ text: String, percent: Bool = false) -> Double? {
        var t = text.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: "")
        guard !t.isEmpty else { return nil }
        var scale = 1.0
        if t.hasSuffix("%") {
            t.removeLast()
            if !percent { scale = 0.01 }
        }
        if let slash = t.firstIndex(of: "/") {
            guard let a = Double(t[..<slash].trimmingCharacters(in: .whitespaces)), let b = Double(t[t.index(after: slash)...].trimmingCharacters(in: .whitespaces)), b != 0 else { return nil }
            return a / b * scale * (percent ? 100 : 1)
        }
        return Double(t).map { $0 * scale }
    }

    static func count(_ text: String) -> Int? {
        let t = text.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: "")
        guard let n = Int(t), n >= 0 else { return nil }
        return n
    }
}

/// A simulation run's ticket: a newer run marks the older one cancelled, and the older one stops between generations.
private final class HWRunToken: @unchecked Sendable {
    var cancelled = false
}

/// The Hardy–Weinberg tool's state: kept while its pages change, remembered between launches (SimplBio.popgen.*).
@MainActor
final class HardyWeinbergState: ObservableObject {
    static let shared = HardyWeinbergState()

    // MARK: Calculate
    @Published var given: HWGiven { didSet { BioPrefs.set(.popgen, "given", given.rawValue) } }
    @Published var value: String { didSet { BioPrefs.set(.popgen, "value", value) } }
    @Published var countAA: String
    @Published var countAa: String
    @Published var countaa: String

    // MARK: Test
    @Published var testAA: String { didSet { saveTest() } }
    @Published var testAa: String { didSet { saveTest() } }
    @Published var testaa: String { didSet { saveTest() } }
    @Published var df = 1

    // MARK: Simulate
    @Published var sim: HWSimParams {
        didSet {
            guard sim != oldValue else { return }
            if let d = try? JSONEncoder().encode(sim) { BioPrefs.set(.popgen, "sim", String(decoding: d, as: UTF8.self)) }
            run()
        }
    }
    @Published private(set) var populations: [HWPopulation] = []
    @Published private(set) var running = false
    /// The generation under the pointer on the chart (nil: the last).
    @Published var hovered: Int?

    private var token: HWRunToken?

    init() {
        given = BioPrefs.string(.popgen, "given").flatMap { HWGiven(rawValue: $0) } ?? .q2
        value = BioPrefs.string(.popgen, "value") ?? "0.16"
        countAA = "50"
        countAa = "30"
        countaa = "20"
        let t = (BioPrefs.string(.popgen, "test") ?? "50,30,20").split(separator: ",").map(String.init)
        testAA = t.count == 3 ? t[0] : "50"
        testAa = t.count == 3 ? t[1] : "30"
        testaa = t.count == 3 ? t[2] : "20"
        sim = BioPrefs.string(.popgen, "sim").flatMap { try? JSONDecoder().decode(HWSimParams.self, from: Data($0.utf8)) } ?? HWSimParams()
        run()
    }

    private func saveTest() { BioPrefs.set(.popgen, "test", [testAA, testAa, testaa].joined(separator: ",")) }

    /// The calculator's answer for what is typed, or what is wrong with it.
    var calculation: (calc: HWCalc?, error: String?) {
        if given == .counts {
            guard let a = HWInput.count(countAA), let b = HWInput.count(countAa), let c = HWInput.count(countaa) else {
                return (nil, "Counts are whole numbers, 0 or more.")
            }
            return HardyWeinberg.calculate(counts: HWCounts(AA: a, Aa: b, aa: c))
        }
        guard let v = HWInput.number(value, percent: given.isPercent) else { return (nil, value.isEmpty ? nil : "Enter a number, like \(given.placeholder).") }
        return HardyWeinberg.calculate(given, v)
    }

    var test: (test: HWTest?, error: String?) {
        guard let a = HWInput.count(testAA), let b = HWInput.count(testAa), let c = HWInput.count(testaa) else {
            return (nil, "Counts are whole numbers, 0 or more.")
        }
        return HardyWeinberg.test(HWCounts(AA: a, Aa: b, aa: c), df: df)
    }

    /// The simulation run again with the settings as they are (the newest run wins; an older one stops).
    func run() {
        token?.cancelled = true
        let t = HWRunToken()
        token = t
        let s = sim
        running = true
        Task.detached(priority: .userInitiated) {
            // (a slider being dragged: wait for it to rest a moment)
            try? await Task.sleep(nanoseconds: 120_000_000)
            guard !t.cancelled else { return }
            let pops = HWSimulation.run(s) { t.cancelled }
            guard !t.cancelled else { return }
            await MainActor.run {
                let me = HardyWeinbergState.shared
                guard me.token === t else { return }
                me.populations = pops
                me.running = false
                if let h = me.hovered, h > s.generations { me.hovered = nil }
            }
        }
    }

    // MARK: A launch's state, a hand-off

    private static var launched = false

    /// What a launch asked (-SimplTool popgen -SimplToolState calculate:q2=0.16 …), once.
    func applyLaunch(_ state: BioLaunchState?) {
        guard let state, !HardyWeinbergState.launched else { return }
        HardyWeinbergState.launched = true
        if let g = state.value("given").flatMap({ HWGiven(rawValue: $0) }) { given = g }
        if let v = state.value("value") { value = v }
        for g in HWGiven.allCases where g != .counts {
            if let v = state.value(g.rawValue) {
                given = g
                value = v
            }
        }
        if let c = state.value("counts") {
            let parts = c.split(separator: ",").map(String.init)
            if parts.count == 3 {
                (testAA, testAa, testaa) = (parts[0], parts[1], parts[2])
                (countAA, countAa, countaa) = (parts[0], parts[1], parts[2])
                if state.page == "calculate" { given = .counts }
            }
        }
        if let d = state.value("df").flatMap({ Int($0) }), d >= 1 { df = d }
        // (a launch onto the Simulate page starts from the defaults, so its picture never depends on an earlier run's)
        var s = state.page == "simulate" ? HWSimParams() : sim
        if state.has("infinite") { s.size = nil }
        if let n = state.value("n").flatMap({ Int($0) }) { s.size = min(max(n, 10), 10_000) }
        if let p = state.value("p0").flatMap({ Double($0) }) { s.p0 = min(max(p, 0), 1) }
        if let r = state.value("reps").flatMap({ Int($0) }) { s.replicates = min(max(r, 1), 10) }
        if let seed = state.value("seed").flatMap({ UInt64($0) }) { s.seed = seed }
        if let g = state.value("gens").flatMap({ Int($0) }) { s.generations = min(max(g, 1), 200) }
        if let preset = state.value("preset").flatMap({ HWSelection(rawValue: $0) }) {
            (s.wAA, s.wAa, s.waa) = (preset.fitness.AA, preset.fitness.Aa, preset.fitness.aa)
        }
        if let f = state.value("F").flatMap({ Double($0) }) { s.F = min(max(f, 0), 1) }
        if let u = state.value("u").flatMap({ Double($0) }) { s.u = u }
        if let v = state.value("v").flatMap({ Double($0) }) { s.v = v }
        if let m = state.value("m").flatMap({ Double($0) }) { s.m = m }
        if let pm = state.value("pm").flatMap({ Double($0) }) { s.pm = pm }
        sim = s
        if let h = state.value("hover").flatMap({ Int($0) }) { hovered = h }
    }

    /// What the pin handed over: ["given": "q2", "value": "0.16"], or ["counts": "50,30,20"].
    func takeHandoff() {
        let tools = ToolsCenter.shared
        if let g = tools.takeInfo("given").flatMap({ HWGiven(rawValue: $0) }) {
            given = g
            BioNav.shared.set(.popgen, "calculate")
        }
        if let v = tools.takeInfo("value") { value = v }
        if let c = tools.takeInfo("counts") {
            let parts = c.split(separator: ",").map(String.init)
            if parts.count == 3 {
                (testAA, testAa, testaa) = (parts[0], parts[1], parts[2])
                BioNav.shared.set(.popgen, "test")
            }
        }
    }
}

struct HardyWeinbergTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.popgen)`.
    static let pages: [BioPage] = [
        BioPage(id: "calculate", title: "Calculate", symbol: "function", help: "Everything from one value"),
        BioPage(id: "test", title: "Test", symbol: "checkmark.circle", help: "Is a population in equilibrium?"),
        BioPage(id: "simulate", title: "Simulate", symbol: "chart.xyaxis.line", help: "Selection, drift, mutation and migration over generations"),
    ]

    @ObservedObject private var nav = BioNav.shared
    @ObservedObject private var model = HardyWeinbergState.shared

    var body: some View {
        page
            .onAppear {
                model.applyLaunch(state)
                model.takeHandoff()
            }
    }

    @ViewBuilder
    private var page: some View {
        switch nav.page(.popgen) {
        case "test": HWTestPage(model: model)
        case "simulate": HWSimulatePage(model: model)
        default: HWCalculatePage(model: model)
        }
    }
}

// MARK: - Pieces

/// The three genotype frequencies as one bar, each part named.
struct GenotypeBar: View {
    let AA: Double
    let Aa: Double
    let aa: Double
    var height: CGFloat = 26
    var labelled = true

    var body: some View {
        let parts: [(String, Double, Color)] = [("AA", AA, HWColors.AA.color), ("Aa", Aa, HWColors.Aa.color), ("aa", aa, HWColors.aa.color)]
        VStack(alignment: .leading, spacing: 6) {
            GeometryReader { geo in
                let total = max(AA + Aa + aa, 1e-12)
                let gaps = CGFloat(parts.filter { $0.1 > 0 }.count - 1) * 2
                let w = max(geo.size.width - max(gaps, 0), 0)
                HStack(spacing: 2) {
                    ForEach(parts.indices, id: \.self) { i in
                        if parts[i].1 > 0 {
                            RoundedRectangle(cornerRadius: 4, style: .continuous)
                                .fill(parts[i].2)
                                .frame(width: w * CGFloat(parts[i].1 / total))
                        }
                    }
                }
            }
            .frame(height: height)
            if labelled {
                HStack(spacing: 14) {
                    ForEach(parts.indices, id: \.self) { i in
                        HStack(spacing: 5) {
                            Circle().fill(parts[i].2).frame(width: 9, height: 9)
                            Text("\(parts[i].0) \(HardyWeinberg.num(parts[i].1))").font(.sCaption.monospacedDigit())
                        }
                    }
                }
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Genotype frequencies: AA \(HardyWeinberg.num(AA)), Aa \(HardyWeinberg.num(Aa)), aa \(HardyWeinberg.num(aa))")
    }
}

/// A hundred people, coloured by genotype (AA, then Aa, then aa, row by row).
struct PeopleGrid: View {
    let counts: [Int]

    var body: some View {
        let colours = HWColors.all
        let who: [Int] = counts.enumerated().flatMap { Array(repeating: $0.offset, count: $0.element) }
        VStack(alignment: .leading, spacing: 8) {
            Grid(horizontalSpacing: 5, verticalSpacing: 5) {
                ForEach(0..<10, id: \.self) { r in
                    GridRow {
                        ForEach(0..<10, id: \.self) { c in
                            let i = r * 10 + c
                            Image(systemName: "person.fill")
                                .font(.system(size: 15))
                                .foregroundStyle(i < who.count ? colours[who[i]] : Color.secondary.opacity(0.3))
                        }
                    }
                }
            }
            HStack(spacing: 14) {
                ForEach(0..<3, id: \.self) { g in
                    HStack(spacing: 5) {
                        Image(systemName: "person.fill").font(.system(size: 11)).foregroundStyle(colours[g])
                        Text("\(counts.count > g ? counts[g] : 0) \(["AA", "Aa", "aa"][g])").font(.sCaption.monospacedDigit())
                    }
                }
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Of 100 people: \(counts.count > 0 ? counts[0] : 0) AA, \(counts.count > 1 ? counts[1] : 0) Aa, \(counts.count > 2 ? counts[2] : 0) aa")
    }
}

/// A count field with its genotype's swatch over it.
struct HWCountField: View {
    let genotype: String
    let colour: Color
    @Binding var text: String

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 5) {
                Circle().fill(colour).frame(width: 8, height: 8)
                Text(genotype).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
            }
            TextField("0", text: $text)
                .textFieldStyle(.roundedBorder)
                .font(.system(size: 15, weight: .medium).monospacedDigit())
                .multilineTextAlignment(.trailing)
                .accessibilityLabel("\(genotype) count")
        }
    }
}

// MARK: - The Calculate page

struct HWCalculatePage: View {
    @ObservedObject var model: HardyWeinbergState
    @FocusState private var typing: Bool

    var body: some View {
        let r = model.calculation
        PTWorkLayout {
            controls(error: r.error)
        } centre: {
            centre(r.calc)
        } right: {
            side(r.calc)
        }
    }

    private struct Example {
        let title: String
        let given: HWGiven
        let value: String
    }

    private static let examples = [
        Example(title: "q² = 0.16", given: .q2, value: "0.16"),
        Example(title: "Cystic fibrosis: 1 in 2,500 affected", given: .recessivePercent, value: "0.04"),
        Example(title: "Tasting PTC: 70% can (dominant)", given: .dominantPercent, value: "70"),
        Example(title: "A sample: 50 AA, 30 Aa, 20 aa", given: .counts, value: ""),
    ]

    private func controls(error: String?) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Given")
            CrossMenu(help: "What you know about the population") {
                ForEach(HWGiven.allCases) { g in
                    Button { model.given = g } label: { CrossMenuItem(title: g.title, on: g == model.given) }
                }
            } label: {
                Text(model.given.title).font(.sCallout.weight(.semibold)).lineLimit(1).minimumScaleFactor(0.8)
            }
            if model.given == .counts {
                HStack(spacing: 8) {
                    HWCountField(genotype: "AA", colour: HWColors.AA.color, text: $model.countAA)
                    HWCountField(genotype: "Aa", colour: HWColors.Aa.color, text: $model.countAa)
                    HWCountField(genotype: "aa", colour: HWColors.aa.color, text: $model.countaa)
                }
            } else {
                HStack(spacing: 8) {
                    Text(model.given.short).font(.system(size: 17, weight: .semibold)).frame(minWidth: 30, alignment: .leading)
                    Text("=").foregroundStyle(.secondary)
                    TextField(model.given.placeholder, text: $model.value)
                        .textFieldStyle(.roundedBorder)
                        .font(.system(size: 17, weight: .medium).monospacedDigit())
                        .focused($typing)
                        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.red, lineWidth: 1.5).opacity(error == nil ? 0 : 1))
                        .help("\(model.given.title) (⌘F)")
                        .accessibilityLabel(model.given.title)
                        .background {
                            Button("") { typing = true }
                                .keyboardShortcut("f", modifiers: .command)
                                .opacity(0)
                                .frame(width: 0, height: 0)
                                .accessibilityHidden(true)
                        }
                    if model.given.isPercent { Text("%").foregroundStyle(.secondary) }
                }
            }
            if let error {
                Label(error, systemImage: "exclamationmark.circle")
                    .font(.sCaption)
                    .foregroundStyle(.red)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                Text(model.given.isPercent ? "A percent, 0 to 100." : (model.given == .counts ? "Whole numbers of individuals." : "A frequency, 0 to 1 (or a fraction like 1/2500)."))
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
            BioHeading("Examples")
            ForEach(Self.examples.indices, id: \.self) { i in
                let e = Self.examples[i]
                Button {
                    model.given = e.given
                    if e.given == .counts {
                        (model.countAA, model.countAa, model.countaa) = ("50", "30", "20")
                    } else {
                        model.value = e.value
                    }
                } label: {
                    Text(e.title).frame(maxWidth: .infinity, alignment: .leading)
                }
                .glassButton()
            }
        }
        .font(.sCallout)
    }

    private func centre(_ c: HWCalc?) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                if let c {
                    VStack(alignment: .leading, spacing: 10) {
                        Text("Step by step").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                        ForEach(c.steps.indices, id: \.self) { i in
                            HStack(alignment: .firstTextBaseline, spacing: 12) {
                                Text(c.steps[i].label)
                                    .font(.sCaption.weight(.semibold))
                                    .foregroundStyle(.secondary)
                                    .frame(width: 96, alignment: .leading)
                                Text(c.steps[i].math)
                                    .font(.system(size: 16, weight: .medium).monospacedDigit())
                                    .textSelection(.enabled)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            .accessibilityElement(children: .combine)
                        }
                    }
                    ForEach(c.notes, id: \.self) { n in
                        Label(n, systemImage: "info.circle").font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    }
                    VStack(alignment: .leading, spacing: 10) {
                        Text("Genotype frequencies").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                        GenotypeBar(AA: c.p2, Aa: c.twoPQ, aa: c.q2)
                    }
                    VStack(alignment: .leading, spacing: 10) {
                        Text("100 people").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                        PeopleGrid(counts: HardyWeinberg.people([c.p2, c.twoPQ, c.q2]))
                    }
                } else {
                    Text("Enter a value on the left: any one of p, q, p², 2pq, q², the share showing either trait, or the genotype counts.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(20)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .card(radius: 20)
    }

    private func side(_ c: HWCalc?) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            if let c {
                VStack(spacing: 0) {
                    BioValueRow(label: "p (A)", value: HardyWeinberg.num(c.p))
                    BioValueRow(label: "q (a)", value: HardyWeinberg.num(c.q))
                    BioValueRow(label: "p² (AA)", value: HardyWeinberg.num(c.p2), color: HWColors.AA.color)
                    BioValueRow(label: "2pq (Aa)", value: HardyWeinberg.num(c.twoPQ), color: HWColors.Aa.color)
                    BioValueRow(label: "q² (aa)", value: HardyWeinberg.num(c.q2), color: HWColors.aa.color)
                    BioValueRow(label: "Carriers per 1,000", value: HardyWeinberg.carriers(c.carriersPer1000), last: true)
                }
                .card(radius: 14)
                if let s = c.counts {
                    BioCard(title: "Observed and expected", subtitle: "\(s.n) individuals") {
                        let n = Double(s.n)
                        BioValueRow(label: "AA", value: "\(s.AA) · expected \(HardyWeinberg.num(c.p2 * n))")
                        BioValueRow(label: "Aa", value: "\(s.Aa) · expected \(HardyWeinberg.num(c.twoPQ * n))")
                        BioValueRow(label: "aa", value: "\(s.aa) · expected \(HardyWeinberg.num(c.q2 * n))", last: true)
                    }
                }
            }
            BioCard(title: "The equations") {
                BioFactRow(label: "Alleles", text: "p + q = 1")
                BioFactRow(label: "Genotypes", text: "p² + 2pq + q² = 1")
                BioFactRow(label: "Watch out", text: "A dominant allele does not become more common by being dominant. Without selection, mutation, migration or drift, p stays the same in every generation.")
            }
            BioCard(title: "The five conditions") {
                Text("No mutation · no migration · a very large population · random mating · no natural selection. While all five hold, allele and genotype frequencies stay the same.")
                    .font(.sCallout)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

// MARK: - The pin's quick version

/// Hardy–Weinberg in its pin's popover (spec §3.2 item 3): everything from one value — p, q, p², 2pq, q² and carriers —
/// and its genotype bar; Return opens it in the whole tool.
struct HardyWeinbergCompact: View {
    let openFull: (_ info: [String: String]) -> Void
    @State private var given: HWGiven = .q2
    @State private var value = "0.16"

    private static let kinds: [HWGiven] = [.q2, .p, .q, .p2, .twoPQ, .recessivePercent, .dominantPercent]

    var body: some View {
        let v = HWInput.number(value, percent: given.isPercent)
        let r = v.map { HardyWeinberg.calculate(given, $0) }
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                Picker("Given", selection: $given) {
                    ForEach(Self.kinds) { g in Text(g.title).tag(g) }
                }
                .labelsHidden()
                .frame(width: 190)
                TextField(given.placeholder, text: $value)
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 15, weight: .medium).monospacedDigit())
                    .onSubmit { openFull(["given": given.rawValue, "value": value]) }
                    .accessibilityLabel(given.title)
            }
            if let c = r?.calc {
                Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 4) {
                    GridRow {
                        cell("p", c.p)
                        cell("q", c.q)
                    }
                    GridRow {
                        cell("p²", c.p2)
                        cell("2pq", c.twoPQ)
                    }
                    GridRow {
                        cell("q²", c.q2)
                        Text("\(HardyWeinberg.carriers(c.carriersPer1000)) carriers / 1,000").font(.sCaption).foregroundStyle(.secondary)
                    }
                }
                GenotypeBar(AA: c.p2, Aa: c.twoPQ, aa: c.q2, height: 14)
            } else if let e = r?.error {
                Label(e, systemImage: "exclamationmark.circle").font(.sCaption).foregroundStyle(.red)
            } else {
                Text("Type a value: q² = 0.16 gives q = 0.40, p = 0.60, 2pq = 0.48.").font(.sCallout).foregroundStyle(.secondary)
            }
            Button {
                openFull(["given": given.rawValue, "value": value])
            } label: {
                Text("Open in Hardy–Weinberg").frame(maxWidth: .infinity)
            }
            .glassButton(prominent: true)
            .controlSize(.large)
            .help("The steps, the 100 people, the test and the simulation")
        }
    }

    private func cell(_ name: String, _ x: Double) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(name).font(.sCallout).foregroundStyle(.secondary).frame(width: 30, alignment: .leading)
            Text(HardyWeinberg.num(x)).font(.system(size: 17, weight: .bold).monospacedDigit())
        }
    }
}

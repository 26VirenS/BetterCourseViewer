import Charts
import SwiftUI

// Bio Lab Calculator (spec §4.8, §5.5): lab mathematics and the AP formula sheet, done the way it is written on paper.
// Six pages of calculators (solutions, microscopy, cells, statistics, populations, molecules), each a card: the
// formula, its fields with unit menus (and a "solve for" choice where any one can be worked out), the answer with its
// units rounded as §5.5 says, the numbers substituted, and "Show steps" with the unit conversions. Some draw more: SA:V
// against size, growth curves, error bars, a serial dilution's tubes, a scale bar. What is typed is remembered per
// calculator (SimplBio.biolab.calc.<id>), and the pin's quick version (C₁V₁ = C₂V₂ and molarity) shares it. The
// calculators themselves — their fields, modes and working — are values in Model/BioLabCalculators.swift.
//
// Launch: -SimplTool biolab -SimplToolState <page>[:steps][:calc=<id>][:digits=<n>] (steps opens every card's steps).

// MARK: - The inputs, kept

/// What has been typed into every calculator, and the rounding setting — remembered on this Mac, and shared by the
/// tool and its pin.
@MainActor
final class BioLabStore: ObservableObject {
    static let shared = BioLabStore()

    @Published private(set) var entries: [String: BioLabEntry] = [:]
    /// Significant figures for answers: 2–6 (3 unless changed).
    @Published var digits: Int {
        didSet { BioPrefs.set(.biolab, "digits", String(digits)) }
    }
    /// The calculators whose steps are open.
    @Published var expanded: Set<String> = []
    /// A line over a calculator's fields saying where its numbers came from (another tool's hand-off), by calculator,
    /// until they are changed.
    @Published private(set) var notes: [String: String] = [:]

    private init() {
        digits = min(max(Int(BioPrefs.string(.biolab, "digits") ?? "") ?? 3, 2), 6)
        var e: [String: BioLabEntry] = [:]
        for c in BioLab.all { e[c.id] = Self.load(c) }
        entries = e
    }

    /// A calculator's saved inputs over its defaults (a field or unit it no longer has is dropped).
    private static func load(_ c: BioLabCalculator) -> BioLabEntry {
        var e = c.defaultEntry
        guard let s = BioPrefs.string(.biolab, "calc." + c.id),
              let saved = try? JSONDecoder().decode(BioLabEntry.self, from: Data(s.utf8)) else { return e }
        if c.modes.contains(where: { $0.id == saved.mode }) { e.mode = saved.mode }
        for (k, t) in saved.texts where c.field(k) != nil { e.texts[k] = t }
        for (k, u) in saved.units where c.field(k)?.units.contains(where: { $0.symbol == u }) == true { e.units[k] = u }
        return e
    }

    func entry(_ c: BioLabCalculator) -> BioLabEntry { entries[c.id] ?? c.defaultEntry }
    func outcome(_ c: BioLabCalculator) -> BioLabOutcome { c.run(entry(c), digits: digits) }

    func setText(_ c: BioLabCalculator, _ field: String, _ text: String) {
        notes[c.id] = nil
        change(c) { $0.texts[field] = text }
    }

    func setUnit(_ c: BioLabCalculator, _ field: String, _ unit: String) {
        notes[c.id] = nil
        change(c) { $0.units[field] = unit }
    }

    /// Cell Explorer's cell put into the surface area and volume card ("Why are cells small?"): its shape, its sizes and
    /// their unit, with a line saying so.
    func take(_ h: BioLabSAVHandOff) {
        guard let c = BioLab.calculator("sav"), c.modes.contains(where: { $0.id == h.mode }) else { return }
        change(c) { e in
            e.mode = h.mode
            for (k, t) in h.texts where c.field(k) != nil { e.texts[k] = t }
            if let u = h.unit {
                for k in c.mode(h.mode).inputs where c.field(k)?.units.contains(where: { $0.symbol == u }) == true { e.units[k] = u }
            }
        }
        notes[c.id] = h.note
    }

    /// Another thing to solve for: the answer just worked out goes into the field it filled, so the numbers stay together
    /// (solve for V₁, then for C₂, and the V₁ found is the one used).
    func setMode(_ c: BioLabCalculator, _ mode: String) {
        let old = entry(c)
        guard old.mode != mode else { return }
        notes[c.id] = nil
        let answer = c.mode(old.mode).unknown.flatMap { u in outcome(c).result?.answers.first { $0.field == u } }
        change(c) { e in
            if let a = answer, let f = a.field { e.texts[f] = a.text }
            e.mode = mode
        }
    }

    /// Back to the worked example.
    func reset(_ c: BioLabCalculator) {
        notes[c.id] = nil
        change(c) { $0 = c.defaultEntry }
    }

    func showSteps(_ id: String, _ on: Bool) {
        if on { expanded.insert(id) } else { expanded.remove(id) }
    }

    private func change(_ c: BioLabCalculator, _ edit: (inout BioLabEntry) -> Void) {
        var e = entry(c)
        edit(&e)
        entries[c.id] = e
        if let data = try? JSONEncoder().encode(e) { BioPrefs.set(.biolab, "calc." + c.id, String(decoding: data, as: UTF8.self)) }
    }
}

// MARK: - The tool

struct BioLabTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.biolab)`.
    static let pages: [BioPage] = [
        BioPage(id: "solutions", title: "Solutions", symbol: "drop", help: "Molarity, dilutions and percent solutions"),
        BioPage(id: "microscopy", title: "Microscopy", symbol: "magnifyingglass", help: "Magnification, field of view and size"),
        BioPage(id: "cells", title: "Cells", symbol: "circle.dashed", help: "Surface area to volume, water potential, cell counts"),
        BioPage(id: "stats", title: "Statistics", symbol: "chart.bar", help: "Mean, standard deviation, error bars, chi-square"),
        BioPage(id: "ecology", title: "Populations", symbol: "leaf", help: "Growth, rate and diversity"),
        BioPage(id: "molecules", title: "Molecules", symbol: "atom", help: "Absorbance, PCR, centrifuges, pH and Q₁₀"),
    ]

    @ObservedObject private var nav = BioNav.shared
    @ObservedObject private var store = BioLabStore.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var applied = false

    var body: some View {
        let page = nav.page(.biolab)
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    BioLabPageTop(page: page)
                    BioLabColumns(items: BioLabItem.items(on: page))
                }
                .frame(maxWidth: 1480, alignment: .leading)
                .padding(.horizontal, 40)
                .padding(.top, 8)
                .padding(.bottom, 40)
                .frame(maxWidth: .infinity)
            }
            .onAppear { apply(proxy) }
        }
    }

    /// What the launch, the pin or another tool asked for, once: a calculator to go to (its steps open), every card's
    /// steps, a rounding — or Cell Explorer's cell, put into the surface area and volume card (BioLabSAVHandOff).
    private func apply(_ proxy: ScrollViewProxy) {
        guard !applied else { return }
        applied = true
        if let d = state?.value("digits").flatMap({ Int($0) }) { store.digits = min(max(d, 2), 6) }
        if state?.has("steps") == true {
            for c in BioLab.on(nav.page(.biolab)) { store.showSteps(c.id, true) }
        }
        let tools = ToolsCenter.shared
        var handed = tools.takeInfo("calc")
        if handed == nil || handed == "sav" {
            var info: [String: String] = [:]
            for k in BioLabSAVHandOff.keys { if let v = tools.takeInfo(k) { info[k] = v } }
            if let cell = BioLabSAVHandOff(info) {
                store.take(cell)
                handed = "sav"
            }
        }
        guard let id = handed ?? state?.value("calc"), let c = BioLab.calculator(id) else { return }
        nav.set(.biolab, c.page)
        if handed != nil { store.showSteps(c.id, true) }
        DispatchQueue.main.async {
            withAnimation(reduceMotion ? nil : Motion.gentle) { proxy.scrollTo(c.id, anchor: .top) }
        }
    }
}

/// The page's line and the rounding setting.
private struct BioLabPageTop: View {
    let page: String
    @ObservedObject private var store = BioLabStore.shared

    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            Text(BioLab.pages.first { $0.id == page }?.line ?? "")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 12)
            HStack(spacing: 6) {
                Text("Significant figures").font(.sCallout).foregroundStyle(.secondary)
                Picker("Significant figures", selection: $store.digits) {
                    ForEach(2...6, id: \.self) { Text("\($0)").tag($0) }
                }
                .labelsHidden()
                .pickerStyle(.menu)
                .fixedSize()
            }
            .help("Answers are rounded to this many significant figures, and never more than one past the least precise measurement")
        }
    }
}

// MARK: - The page's cards in columns

/// A card on a page: a calculator, or one of the reference tables.
enum BioLabItem: Identifiable {
    case calculator(BioLabCalculator)
    case prefixes
    case criticalValues

    var id: String {
        switch self {
        case .calculator(let c): return c.id
        case .prefixes: return "prefixes"
        case .criticalValues: return "critical"
        }
    }

    /// Roughly how tall its card is, to share the columns out evenly.
    var weight: Double {
        switch self {
        case .calculator(let c):
            let extra: [String: Double] = ["sav": 5, "growth": 5, "stats": 10, "chisq": 7, "serial": 6, "simpson": 3, "scaleBar": 2]
            return 4 + Double(c.fields(in: c.modes[0]).count) * 1.1 + (c.modes.count > 1 ? 1 : 0) + (extra[c.id] ?? 0)
        case .prefixes: return 7
        case .criticalValues: return 9
        }
    }

    static func items(on page: String) -> [BioLabItem] {
        var out = BioLab.on(page).map { BioLabItem.calculator($0) }
        if page == "solutions" { out.append(.prefixes) }
        if page == "stats" { out.append(.criticalValues) }
        return out
    }

    /// The cards dealt into `n` columns, each to the shortest so far, so the page reads across and then down.
    static func balance(_ items: [BioLabItem], into n: Int) -> [[BioLabItem]] {
        var columns = Array(repeating: [BioLabItem](), count: n)
        var heights = Array(repeating: 0.0, count: n)
        for item in items {
            let i = heights.indices.min { heights[$0] < heights[$1] } ?? 0
            columns[i].append(item)
            heights[i] += item.weight
        }
        return columns
    }
}

/// Three columns of cards on a wide window, two on a narrower one, one on a narrow one.
private struct BioLabColumns: View {
    let items: [BioLabItem]

    var body: some View {
        ViewThatFits(in: .horizontal) {
            columns(3)
            columns(2)
            columns(1)
        }
    }

    private func columns(_ n: Int) -> some View {
        let cols = BioLabItem.balance(items, into: n)
        return HStack(alignment: .top, spacing: 18) {
            ForEach(0..<n, id: \.self) { i in
                VStack(spacing: 18) {
                    ForEach(cols[i]) { item in BioLabItemView(item: item) }
                }
                // (an ideal width of its own: a long line inside must not make the columns seem too wide to fit)
                .frame(minWidth: 370, idealWidth: 370, maxWidth: .infinity, alignment: .top)
            }
        }
    }
}

private struct BioLabItemView: View {
    let item: BioLabItem

    var body: some View {
        switch item {
        case .calculator(let c): BioLabCard(calc: c)
        case .prefixes: BioLabPrefixCard()
        case .criticalValues: BioLabCriticalCard()
        }
    }
}

// MARK: - A calculator's card

/// A calculator: its title and line, the formula, what to solve for, its fields, the answer and the working, anything
/// it draws, and its steps. `compact` is the pin's version: fields, answer and working only.
struct BioLabCard: View {
    let calc: BioLabCalculator
    var compact = false
    @ObservedObject private var store = BioLabStore.shared

    var body: some View {
        let entry = store.entry(calc)
        let mode = calc.mode(entry.mode)
        let outcome = calc.run(entry, digits: store.digits)
        let content = VStack(alignment: .leading, spacing: compact ? 10 : 14) {
            if !compact { header }
            if !compact, let note = store.notes[calc.id] {
                Label(note, systemImage: "arrow.down.right.circle")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text(calc.formula)
                .font(.system(size: compact ? 14 : 15, weight: .medium))
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(Theme.well, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                .textSelection(.enabled)
                .accessibilityLabel("Formula: \(calc.formula)")
            if calc.modes.count > 1 { modePicker(entry) }
            VStack(spacing: 8) {
                ForEach(calc.fields(in: mode)) { f in
                    BioLabFieldRow(calc: calc, field: f, entry: entry, unknown: mode.unknown == f.id,
                                   answer: outcome.result?.answers.first { $0.field == f.id },
                                   wrong: outcome.problem?.field == f.id, compact: compact)
                }
            }
            Divider()
            BioLabAnswerView(outcome: outcome, sheetFormula: calc.formula, compact: compact)
            if !compact, let r = outcome.result {
                BioLabExtras(calc: calc, result: r)
                steps(r)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        if compact {
            content
        } else {
            content
                .padding(18)
                .card(radius: 18)
                .id(calc.id)
        }
    }

    private var header: some View {
        HStack(alignment: .top, spacing: 8) {
            VStack(alignment: .leading, spacing: 3) {
                Text(calc.title).font(.sHeadline).accessibilityAddTraits(.isHeader)
                Text(calc.line).font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 4)
            Button {
                store.reset(calc)
            } label: {
                Image(systemName: "arrow.counterclockwise")
                    .font(.system(size: 12, weight: .semibold))
                    .frame(width: 24, height: 24)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.borderless)
            .help("Back to the worked example")
            .accessibilityLabel("Reset \(calc.title) to its worked example")
        }
    }

    /// What to solve for: a segmented control where the choices are short, else a menu.
    private func modePicker(_ entry: BioLabEntry) -> some View {
        let selection = Binding(get: { entry.mode }, set: { store.setMode(calc, $0) })
        let segmented = calc.modes.count <= 4 && calc.modes.reduce(0) { $0 + $1.title.count } <= 28
        return HStack(spacing: 10) {
            Text(calc.modeLabel).font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
            if segmented {
                Picker(calc.modeLabel, selection: selection) {
                    ForEach(calc.modes) { m in Text(m.title).tag(m.id) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
            } else {
                Picker(calc.modeLabel, selection: selection) {
                    ForEach(calc.modes) { m in Text(m.title).tag(m.id) }
                }
                .pickerStyle(.menu)
                .labelsHidden()
                .fixedSize()
                Spacer(minLength: 0)
            }
        }
        .help(calc.modeLabel == "Solve for" ? "Choose which one to work out from the others" : "Choose the \(calc.modeLabel.lowercased())")
    }

    private func steps(_ r: BioLabResult) -> some View {
        DisclosureGroup(isExpanded: Binding(get: { store.expanded.contains(calc.id) }, set: { store.showSteps(calc.id, $0) })) {
            VStack(alignment: .leading, spacing: 6) {
                ForEach(Array(r.steps.enumerated()), id: \.offset) { i, s in
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Text("\(i + 1)")
                            .font(.sCaption.weight(.semibold).monospacedDigit())
                            .foregroundStyle(.secondary)
                            .frame(width: 16, alignment: .trailing)
                        Text(s).font(.sCallout).fixedSize(horizontal: false, vertical: true)
                    }
                }
                Text(r.precision)
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 2)
            }
            .textSelection(.enabled)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.top, 6)
        } label: {
            Text("Show steps").font(.sCallout.weight(.medium))
        }
        .help("The working, step by step, with the unit conversions")
    }
}

/// One of a calculator's fields: its symbol and words, what is typed (or, for the unknown, the answer), and its unit.
private struct BioLabFieldRow: View {
    let calc: BioLabCalculator
    let field: BioLabField
    let entry: BioLabEntry
    let unknown: Bool
    let answer: BioLabAnswer?
    let wrong: Bool
    var compact = false
    @ObservedObject private var store = BioLabStore.shared

    private var text: Binding<String> {
        Binding(get: { entry.texts[field.id] ?? "" }, set: { store.setText(calc, field.id, $0) })
    }

    private var unit: Binding<String> {
        Binding(get: { entry.units[field.id] ?? field.initialUnit?.symbol ?? "" }, set: { store.setUnit(calc, field.id, $0) })
    }

    private var boxWidth: CGFloat { compact ? 92 : 104 }
    private var unitWidth: CGFloat { compact ? 76 : 86 }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            switch field.kind {
            case .number: numberRow
            case .list, .text: listRow
            case .choice: choiceRow
            }
            if let hint = field.hint, !compact, !unknown {
                Text(hint).font(.sFootnote).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var numberRow: some View {
        HStack(spacing: 8) {
            if !field.symbol.isEmpty {
                Text(field.symbol)
                    .font(.system(size: 14, weight: .semibold))
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
                    .frame(width: compact ? 34 : 40, alignment: .leading)
            }
            HStack(spacing: 4) {
                Text(field.label)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
                if !field.presets.isEmpty && !unknown { presets }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if unknown { answerBox } else { entryField }
            unitView
        }
    }

    private var entryField: some View {
        TextField(field.optional ? "optional" : "", text: text)
            .textFieldStyle(.roundedBorder)
            .font(.sCallout.monospacedDigit())
            .multilineTextAlignment(.trailing)
            .frame(width: boxWidth)
            .overlay {
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .stroke(Color.red, lineWidth: wrong ? 1.5 : 0)
            }
            .help(field.hint ?? field.label)
            .accessibilityLabel(field.symbol.isEmpty ? field.label : "\(field.label), \(field.symbol)")
    }

    /// The unknown: the answer in place of a field, in the accent.
    private var answerBox: some View {
        // (as wide as its number needs, at least a field's width: held to the field's width, a power of ten —
        // "8.0 × 10⁻⁵" — was shrunk until it could hardly be read)
        Text(answer?.text ?? "—")
            .font(.sCallout.weight(.semibold).monospacedDigit())
            .foregroundStyle(Theme.accent)
            .lineLimit(1)
            .fixedSize(horizontal: true, vertical: false)
            .padding(.horizontal, 7)
            .frame(minWidth: boxWidth, alignment: .trailing)
            .padding(.vertical, 4)
            .background(Theme.accent.opacity(0.12), in: RoundedRectangle(cornerRadius: 6, style: .continuous))
            .help("Worked out from the other fields; choose its unit beside it")
            .accessibilityLabel("\(field.label): \(answer?.full ?? "not worked out")")
    }

    @ViewBuilder
    private var unitView: some View {
        if !field.units.isEmpty {
            Picker("Unit", selection: unit) {
                ForEach(field.units) { u in Text(field.per ? "per \(u.symbol)" : u.symbol).tag(u.symbol) }
            }
            .labelsHidden()
            .pickerStyle(.menu)
            .frame(width: unitWidth)
            .help("Unit: " + (field.units.first(where: { $0.symbol == unit.wrappedValue })?.name ?? unit.wrappedValue))
            .accessibilityLabel("Unit of \(field.label)")
        } else {
            Text(field.unit ?? "")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
                .frame(width: unitWidth, alignment: .leading)
        }
    }

    private var presets: some View {
        Menu {
            ForEach(field.presets, id: \.self) { p in
                Button(p.label) { store.setText(calc, field.id, p.text) }
            }
        } label: {
            Image(systemName: "list.bullet")
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(.secondary)
        }
        .menuStyle(.button)
        .buttonStyle(.borderless)
        .menuIndicator(.hidden)
        .fixedSize()
        .help("Common values for \(field.label.lowercased())")
        .accessibilityLabel("Common values for \(field.label)")
    }

    private var listRow: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack(spacing: 6) {
                if !field.symbol.isEmpty { Text(field.symbol).font(.system(size: 14, weight: .semibold)) }
                Text(field.label).font(.sCallout).foregroundStyle(.secondary)
            }
            TextField(field.kind == .list ? "12.1, 13.4, 11.8" : (field.optional ? "optional" : ""), text: text, axis: .vertical)
                .textFieldStyle(.roundedBorder)
                .font(.sCallout.monospacedDigit())
                .lineLimit(1...4)
                .overlay {
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .stroke(Color.red, lineWidth: wrong ? 1.5 : 0)
                }
                .help(field.hint ?? field.label)
                .accessibilityLabel(field.label)
        }
    }

    private var choiceRow: some View {
        HStack(spacing: 8) {
            Text(field.label).font(.sCallout).foregroundStyle(.secondary)
            Spacer(minLength: 4)
            Picker(field.label, selection: text) {
                ForEach(field.choices) { c in Text(c.label).tag(c.id) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
        }
    }
}

/// The answer large (with the same in a handier unit), any other answers, the formula solved and the numbers
/// substituted, and the notes — or, if something typed is wrong, why.
private struct BioLabAnswerView: View {
    let outcome: BioLabOutcome
    /// The formula at the card's top (not repeated here).
    var sheetFormula = ""
    var compact = false

    var body: some View {
        switch outcome {
        case .problem(let p):
            Label {
                Text(p.message).fixedSize(horizontal: false, vertical: true)
            } icon: {
                Image(systemName: "exclamationmark.circle.fill")
            }
            .font(.sCallout)
            .foregroundStyle(.red)
        case .result(let r):
            VStack(alignment: .leading, spacing: 8) {
                if let a = r.answers.first { main(a) }
                ForEach(Array(r.answers.dropFirst().enumerated()), id: \.offset) { _, a in other(a) }
                VStack(alignment: .leading, spacing: 2) {
                    if r.formula != sheetFormula { Text(r.formula).font(.sCallout).foregroundStyle(.secondary) }
                    Text(r.working).font(.sCallout.monospacedDigit()).fixedSize(horizontal: false, vertical: true)
                }
                .textSelection(.enabled)
                ForEach(compact ? Array(r.notes.prefix(1)) : r.notes, id: \.self) { n in
                    Label {
                        Text(n).fixedSize(horizontal: false, vertical: true)
                    } icon: {
                        Image(systemName: "info.circle")
                    }
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                }
            }
        }
    }

    private func main(_ a: BioLabAnswer) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(a.label).font(.sCaption).foregroundStyle(.secondary)
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                if !a.symbol.isEmpty { Text("\(a.symbol) =").font(.sBody).foregroundStyle(.secondary) }
                Text(a.full)
                    .font(.system(size: compact ? 24 : 26, weight: .bold, design: .rounded).monospacedDigit())
                    .lineLimit(1)
                    .minimumScaleFactor(0.5)
                    .textSelection(.enabled)
                if let alt = a.alt { Text(alt).font(.sCallout).foregroundStyle(.secondary).lineLimit(1) }
                Spacer(minLength: 0)
                Button {
                    copyToPasteboard(a.full.replacingOccurrences(of: "−", with: "-"))
                } label: {
                    Image(systemName: "doc.on.doc").font(.system(size: 12))
                }
                .buttonStyle(.borderless)
                .help("Copy the answer")
                .accessibilityLabel("Copy \(a.full)")
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func other(_ a: BioLabAnswer) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(a.label).font(.sCallout).foregroundStyle(.secondary)
            Spacer(minLength: 8)
            Text(a.symbol.isEmpty ? a.full : "\(a.symbol) = \(a.full)")
                .font(.sCallout.weight(.semibold).monospacedDigit())
                .multilineTextAlignment(.trailing)
                .textSelection(.enabled)
        }
        .accessibilityElement(children: .combine)
    }
}

// MARK: - What some calculators draw

private struct BioLabExtras: View {
    let calc: BioLabCalculator
    let result: BioLabResult

    var body: some View {
        switch calc.id {
        case "sav":
            BioLabSAVChart(result: result)
        case "growth":
            if !result.series.isEmpty { BioLabGrowthChart(result: result) }
        case "stats":
            VStack(alignment: .leading, spacing: 12) {
                BioLabErrorBars(summaries: result.summaries)
                if let t = result.table { BioLabTableView(table: t) }
            }
        case "serial":
            VStack(alignment: .leading, spacing: 12) {
                BioLabTubes(tubes: result.tubes)
                if let t = result.table { BioLabTableView(table: t) }
            }
        case "scaleBar":
            if let bar = result.scaleBar { BioLabScaleBar(label: bar.label, millimetres: bar.millimetres) }
        default:
            if let t = result.table { BioLabTableView(table: t, totalRow: true) }
        }
    }
}

/// A small table, its header in small capitals' weight, numbers aligned right.
private struct BioLabTableView: View {
    let table: BioLabTable
    /// The last row is a total (bold).
    var totalRow = false

    var body: some View {
        Grid(alignment: .leading, horizontalSpacing: 14, verticalSpacing: 5) {
            GridRow {
                ForEach(table.columns.indices, id: \.self) { i in
                    Text(table.columns[i])
                        .font(.sCaption.weight(.semibold))
                        .foregroundStyle(.secondary)
                        .gridColumnAlignment(i == 0 ? .leading : .trailing)
                }
            }
            Divider()
            ForEach(table.rows.indices, id: \.self) { r in
                GridRow {
                    ForEach(table.rows[r].indices, id: \.self) { i in
                        Text(table.rows[r][i])
                            .font(.sCallout.monospacedDigit())
                            .fontWeight(totalRow && r == table.rows.count - 1 ? .semibold : .regular)
                    }
                }
            }
        }
        .textSelection(.enabled)
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// SA:V against size, for every length scaled together: it falls as 1/size. The shape typed is marked.
private struct BioLabSAVChart: View {
    let result: BioLabResult

    var body: some View {
        let unit = result.answers.first?.unit ?? ""
        let length = String(unit.dropLast(2))
        VStack(alignment: .leading, spacing: 4) {
            Chart {
                ForEach(result.series.first?.points ?? []) { p in
                    LineMark(x: .value("Size", p.x), y: .value("SA:V", p.y))
                        .interpolationMethod(.catmullRom)
                        .foregroundStyle(BioColor.tool(.biolab))
                        .lineStyle(StrokeStyle(lineWidth: 2.2, lineCap: .round))
                }
                ForEach(result.marks) { m in
                    PointMark(x: .value("Size", m.x), y: .value("SA:V", m.y))
                        .foregroundStyle(Theme.accent)
                        .symbolSize(70)
                        .annotation(position: .topTrailing) {
                            Text("this one").font(.sCaption2).foregroundStyle(.secondary)
                        }
                }
            }
            .chartXAxisLabel("Size (\(length))")
            .chartYAxisLabel("SA:V (\(unit))")
            .frame(height: 160)
            .accessibilityLabel("Chart of surface area to volume against size: it falls as the size grows")
            Text("Every length scaled together: SA:V falls as the size grows.").font(.sFootnote).foregroundStyle(.secondary)
        }
    }
}

/// Population against time from N now: logistic levelling at K, exponential running away.
private struct BioLabGrowthChart: View {
    let result: BioLabResult

    private func colour(_ name: String) -> Color {
        switch name {
        case "Logistic": return BioColor.tool(.popgen)
        case "Exponential": return BioColor.tool(.biolab)
        default: return Color.secondary
        }
    }

    var body: some View {
        Chart {
            ForEach(result.series) { s in
                ForEach(s.points) { p in
                    LineMark(x: .value("Time", p.x), y: .value("N", p.y), series: .value("Model", s.name))
                        .foregroundStyle(by: .value("Model", s.name))
                        .lineStyle(StrokeStyle(lineWidth: s.name == "K" ? 1 : 2.2, lineCap: .round, dash: s.dashed ? [5, 4] : []))
                }
            }
        }
        .chartForegroundStyleScale(domain: result.series.map(\.name), range: result.series.map { colour($0.name) })
        .chartXAxisLabel("Time")
        .chartYAxisLabel("Population N")
        .frame(height: 170)
        .accessibilityLabel("Chart of population against time for the growth models")
    }
}

/// Each group's data as dots, its mean as a diamond and ±2 SE as a bar.
private struct BioLabErrorBars: View {
    let summaries: [BioLabSummary]

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Chart {
                ForEach(summaries, id: \.name) { s in
                    ForEach(Array(s.values.enumerated()), id: \.offset) { _, v in
                        PointMark(x: .value("Group", s.name), y: .value("Value", v))
                            .foregroundStyle(Color.secondary.opacity(0.55))
                            .symbolSize(22)
                    }
                    RuleMark(x: .value("Group", s.name), yStart: .value("Low", s.low), yEnd: .value("High", s.high))
                        .lineStyle(StrokeStyle(lineWidth: 2.5, lineCap: .round))
                        .foregroundStyle(Theme.accent)
                    PointMark(x: .value("Group", s.name), y: .value("Mean", s.mean))
                        .symbol(.diamond)
                        .symbolSize(90)
                        .foregroundStyle(Theme.accent)
                }
            }
            .chartYScale(domain: .automatic(includesZero: false))
            .frame(height: 180)
            .accessibilityLabel("Error bar plot: each group's mean with plus or minus two standard errors")
            Text("◆ mean · bar: x̄ ± 2SE · dots: the data").font(.sFootnote).foregroundStyle(.secondary)
        }
    }
}

/// A serial dilution's tubes, each paler than the one before.
private struct BioLabTubes: View {
    let tubes: [BioLabTube]

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(alignment: .bottom, spacing: 12) {
                ForEach(tubes.indices, id: \.self) { i in tube(tubes[i]) }
            }
            .padding(.vertical, 2)
        }
    }

    private func tube(_ t: BioLabTube) -> some View {
        let tint = BioColor.tool(.biolab)
        return VStack(spacing: 4) {
            ZStack(alignment: .bottom) {
                UnevenRoundedRectangle(bottomLeadingRadius: 11, bottomTrailingRadius: 11, style: .continuous)
                    .fill(Color.primary.opacity(0.04))
                UnevenRoundedRectangle(bottomLeadingRadius: 9, bottomTrailingRadius: 9, style: .continuous)
                    .fill(tint.opacity(0.08 + 0.8 * (1 - t.depth)))
                    .frame(height: 38)
                    .padding(3)
                UnevenRoundedRectangle(bottomLeadingRadius: 11, bottomTrailingRadius: 11, style: .continuous)
                    .stroke(Color.primary.opacity(0.35), lineWidth: 1.2)
            }
            .frame(width: 26, height: 58)
            Text(t.label).font(.sCaption2).foregroundStyle(.secondary)
            Text(t.factor).font(.sCaption.monospacedDigit())
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(t.label == "Stock" ? "Stock" : "Tube \(t.label)"): dilution \(t.factor), \(t.concentration)")
    }
}

/// The scale bar, drawn as long as it would be on a printed page (1 mm = 72/25.4 points), up to the card's width.
private struct BioLabScaleBar: View {
    let label: String
    let millimetres: Double

    var body: some View {
        let points = min(max(millimetres * 72 / 25.4, 6), 280)
        VStack(alignment: .leading, spacing: 5) {
            Text(label).font(.sCaption.weight(.semibold))
            HStack(spacing: 0) {
                Rectangle().frame(width: 1.5, height: 10)
                Rectangle().frame(width: points, height: 3)
                Rectangle().frame(width: 1.5, height: 10)
            }
            .foregroundStyle(Color.primary)
            Text("Drawn about as long as on a printed page.").font(.sFootnote).foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Scale bar for \(label)")
    }
}

// MARK: - The reference cards

/// The AP sheet's metric prefixes (on the Solutions page).
private struct BioLabPrefixCard: View {
    var body: some View {
        BioCard(title: "Metric prefixes", subtitle: "From the AP formula sheet") {
            Grid(alignment: .leading, horizontalSpacing: 18, verticalSpacing: 6) {
                ForEach(BioLab.prefixes, id: \.name) { p in
                    GridRow {
                        Text(p.name).font(.sCallout)
                        Text(p.symbol).font(.sCallout.weight(.semibold))
                        Text(BioLabFormat.power(p.exponent)).font(.sCallout.monospacedDigit()).gridColumnAlignment(.trailing)
                    }
                    .accessibilityElement(children: .combine)
                }
            }
            Text("1 mL = 1,000 µL, 1 M = 1,000 mM: each step of three is a factor of 1,000.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// §5.3.4's critical values, the chi-square card's df lit (on the Statistics page).
private struct BioLabCriticalCard: View {
    @ObservedObject private var store = BioLabStore.shared

    var body: some View {
        let df = store.outcome(BioLab.chiSquare).result?.highlight
        BioCard(title: "Chi-square critical values", subtitle: "Reject the null hypothesis when χ² is larger than the value for its df and p.") {
            VStack(spacing: 0) {
                row(["df"] + BioChiSquare.tableP.map { "p = " + BioLabFormat.plain($0) }, header: true, lit: false)
                Divider()
                ForEach(1...10, id: \.self) { d in
                    row(["\(d)"] + (BioChiSquare.table[d] ?? []).map { String(halfUp: "%.2f", $0) }, header: false, lit: d == df)
                }
            }
            Text(df.map { "Lit: df = \($0), from the chi-square card." } ?? "The AP sheet gives p = 0.05 and 0.01; df = categories − 1.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
        }
    }

    private func row(_ cells: [String], header: Bool, lit: Bool) -> some View {
        HStack(spacing: 0) {
            ForEach(cells.indices, id: \.self) { i in
                Text(cells[i])
                    .font(header ? .sCaption.weight(.semibold) : (i == 2 ? .sCallout.weight(.semibold).monospacedDigit() : .sCallout.monospacedDigit()))
                    .foregroundStyle(header ? Color.secondary : Color.primary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
                    .frame(maxWidth: .infinity, alignment: i == 0 ? .leading : .trailing)
            }
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .background(lit ? Theme.accent.opacity(0.16) : Color.clear, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
        .accessibilityElement(children: .combine)
    }
}

// MARK: - The pin's quick version

/// Bio Lab Calculator in its pin's popover (spec §3.2 item 3): C₁V₁ = C₂V₂ and molarity, with unit menus. It shares
/// what is typed with the whole tool, which `openFull` opens at the same calculator with its steps.
struct BioLabCompact: View {
    let openFull: (_ info: [String: String]) -> Void
    @State private var which = BioPrefs.string(.biolab, "quick") ?? "dilution"

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Picker("Calculator", selection: Binding(get: { which }, set: { which = $0; BioPrefs.set(.biolab, "quick", $0) })) {
                Text("C₁V₁ = C₂V₂").tag("dilution")
                Text("Molarity").tag("molarity")
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            if let c = BioLab.calculator(which) { BioLabCard(calc: c, compact: true) }
            Button {
                openFull(["calc": which])
            } label: {
                Label("Show the steps", systemImage: "list.number").frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .help("Open Bio Lab Calculator at this calculator, with its working step by step")
        }
    }
}

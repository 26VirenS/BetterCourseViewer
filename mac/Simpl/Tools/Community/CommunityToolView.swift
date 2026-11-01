import AppKit
import SwiftUI

// (1.3.211) A Simpl Community tool drawn with the Mac's own controls: a number as a field with its unit and a stepper
// that keeps to its range and step, a choice as a segmented control (a menu past four options), a switch for a toggle;
// its results worked out as the values change (ToolFormat.run) — the first one shown large in the tool's colour, the
// rest in rows, each copied from its menu — then its table, its note and who made it. The whole tool in the tool's
// popup (CommunityToolScreen: the values put in kept for next time, Reset putting its own back), and small in the
// browser's preview of it (CommunityToolPreview: values of its own, kept nowhere). Nothing it does leaves this Mac.

/// An added tool in the tool's popup: its heading (with Reset), its inputs and results side by side on a wide popup.
struct CommunityToolScreen: View {
    let key: String
    @ObservedObject private var store = CommunityToolStore.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if let tool = store.tool(key) {
                CommunityHeading(symbol: ToolFormat.sfSymbol(tool.symbol), color: Color(hex: tool.color), title: tool.name, line: tool.summary) {
                    Button {
                        store.reset(key)
                    } label: {
                        Label("Reset", systemImage: "arrow.counterclockwise")
                            .font(.sCallout.weight(.medium))
                    }
                    .glassButton()
                    .controlSize(.large)
                    .disabled(store.values(for: key).isEmpty)
                    .help("Put back the values \(tool.name) starts with")
                }
                .onAppear { store.said(tool) }
                // (made afresh by Reset, so every field shows its default again — one left with a word in it too)
                CommunityToolBody(tool: tool, values: Binding(get: { store.values(for: key) }, set: { store.setValues($0, for: key) }))
                    .id(store.resets)
            } else {
                CommunityHeading(symbol: "puzzlepiece.extension", color: Color.secondary, title: "Community Tool", line: "Simpl Community") {
                    EmptyView()
                }
                EmptyNote(text: "This tool is no longer on this Mac.", symbol: "puzzlepiece.extension")
                    .padding(.horizontal, 40)
                Spacer(minLength: 0)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(PageGround())
    }
}

/// The whole tool: its inputs beside its results (one over the other on a narrow popup), its table, its note, its maker.
struct CommunityToolBody: View {
    let tool: ToolFormat.Tool
    @Binding var values: [String: ToolJSON]

    var body: some View {
        let answers = ToolFormat.run(tool, given: values)
        ToolPage {
            ToolColumns(sideWidth: 440, breakpoint: 920) {
                PageSection(title: "Inputs") {
                    CommunityInputs(tool: tool, values: $values, compact: false)
                        .padding(18)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .card(radius: 18)
                }
            } side: {
                PageSection(title: "Results") {
                    CommunityResults(tool: tool, answers: answers, compact: false)
                }
            }
            if let table = tool.table {
                CommunityTableView(table: table, compact: false)
            }
            if let note = tool.note {
                Text(note)
                    .font(.sBody)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
                    .frame(maxWidth: 760, alignment: .leading)
            }
            Label {
                Text(tool.maker.isEmpty ? "Shared in Simpl Community" : "Made by \(tool.maker) and shared in Simpl Community")
            } icon: {
                Image(systemName: "person.2")
            }
            .font(.sFootnote)
            .foregroundStyle(.secondary)
        }
    }
}

/// A tool small, as the browser shows it before it is added: its inputs, working, and what they come to — with values of
/// its own, kept nowhere.
struct CommunityToolPreview: View {
    let tool: ToolFormat.Tool
    @State private var values: [String: ToolJSON] = [:]

    var body: some View {
        let answers = ToolFormat.run(tool, given: values)
        VStack(alignment: .leading, spacing: 12) {
            CommunityInputs(tool: tool, values: $values, compact: true)
            CommunityResults(tool: tool, answers: answers, compact: true)
            if let table = tool.table {
                CommunityTableView(table: table, compact: true)
            }
        }
    }
}

// MARK: - Inputs

/// A tool's inputs, each as the Mac's own control for its kind.
struct CommunityInputs: View {
    let tool: ToolFormat.Tool
    @Binding var values: [String: ToolJSON]
    let compact: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: compact ? 10 : 16) {
            ForEach(tool.inputs, id: \.id) { input in
                field(input)
            }
        }
    }

    @ViewBuilder
    private func field(_ input: ToolFormat.Input) -> some View {
        let value = Binding<ToolJSON?>(get: { values[input.id] }, set: { values[input.id] = $0 })
        switch input.kind {
        case .number: CommunityNumberInput(input: input, value: value, compact: compact)
        case .choice: CommunityChoiceInput(input: input, value: value, compact: compact)
        case .toggle: CommunityToggleInput(input: input, value: value, compact: compact)
        }
    }
}

/// An input's name over its control.
private struct CommunityInputLabel: View {
    let text: String
    let compact: Bool

    var body: some View {
        Text(text)
            .font((compact ? Font.sCaption : Font.sSubheadline).weight(.semibold))
            .foregroundStyle(.secondary)
    }
}

/// A number: a field (its default shown until another is typed), its unit, and a stepper by its step that stops at its
/// least and its most. A number typed outside its range is worked out at the end of it, and said so; what is not a
/// number is worked out as its default.
private struct CommunityNumberInput: View {
    let input: ToolFormat.Input
    @Binding var value: ToolJSON?
    let compact: Bool
    @State private var text = ""

    private var current: Double { ToolFormat.inputValue(input, value) }

    var body: some View {
        let up: (() -> Void)? = canStep(up: true) ? { step(up: true) } : nil
        let down: (() -> Void)? = canStep(up: false) ? { step(up: false) } : nil
        VStack(alignment: .leading, spacing: 5) {
            CommunityInputLabel(text: input.label, compact: compact)
            HStack(spacing: 8) {
                TextField(ToolFormat.jsString(input.number), text: $text)
                    .textFieldStyle(.roundedBorder)
                    .font(compact ? Font.sCallout : Font.sBody)
                    .controlSize(compact ? .regular : .large)
                    .frame(width: compact ? 112 : 170)
                    .accessibilityLabel(input.unit.isEmpty ? input.label : "\(input.label), in \(input.unit)")
                if !input.unit.isEmpty {
                    Text(input.unit)
                        .font(compact ? Font.sCallout : Font.sBody)
                        .foregroundStyle(.secondary)
                }
                Stepper(input.label, onIncrement: up, onDecrement: down)
                    .labelsHidden()
                    .controlSize(compact ? .small : .regular)
                Spacer(minLength: 0)
            }
            if let hint {
                Text(hint)
                    .font(.sFootnote)
                    .foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .onAppear { text = Self.text(value, input) }
        .onChange(of: text) { _, t in
            let next = StudyMath.number(t).map { ToolJSON.number($0) }
            // (its default, shown in the field as it opens, is not a value put in)
            if next == value || (value == nil && next == .number(input.number)) { return }
            value = next
        }
        .onChange(of: value) { _, v in
            // (set from outside — the stepper — the field shows it, unless it reads as it already; a field emptied, or
            //  begun with a minus, is left as it is being typed)
            let shown = StudyMath.number(text).map { ToolJSON.number($0) }
            if let v {
                if shown != v { text = Self.text(v, input) }
            } else if let shown, shown != .number(input.number) {
                text = Self.text(nil, input)
            }
        }
    }

    private static func text(_ v: ToolJSON?, _ input: ToolFormat.Input) -> String {
        ToolFormat.jsString(v?.finite ?? input.number)
    }

    /// What a typed number comes to, when it is not taken as it is.
    private var hint: String? {
        let typed = text.trimmingCharacters(in: .whitespaces)
        guard let n = StudyMath.number(typed) else {
            return typed.isEmpty ? nil : "Not a number: worked out with \(ToolFormat.jsString(input.number))."
        }
        if let hi = input.max, n > hi { return "\(ToolFormat.jsString(hi)) at most: worked out with \(ToolFormat.jsString(hi))." }
        if let lo = input.min, n < lo { return "\(ToolFormat.jsString(lo)) at least: worked out with \(ToolFormat.jsString(lo))." }
        return nil
    }

    private func canStep(up: Bool) -> Bool {
        if up, let hi = input.max { return current < hi }
        if !up, let lo = input.min { return current > lo }
        return true
    }

    /// One step up or down from the value worked out with, to as many decimals as the step and the value have (0.1 + 0.2
    /// is 0.3), kept inside the range.
    private func step(up: Bool) {
        let v = current
        let places = Swift.min(10, Swift.max(Self.places(input.step), Self.places(v)))
        let next = ToolFormat.roundTo(v + (up ? input.step : -input.step), Double(places))
        value = .number(ToolFormat.jsMin(input.max ?? .infinity, ToolFormat.jsMax(input.min ?? -.infinity, next)))
    }

    private static func places(_ v: Double) -> Int {
        let s = ToolFormat.jsString(v)
        if s.contains("e") { return 10 }
        guard let dot = s.firstIndex(of: ".") else { return 0 }
        return s.distance(from: dot, to: s.endIndex) - 1
    }
}

/// A choice: a segmented control while its options fit one (four, three in a preview), else a menu.
private struct CommunityChoiceInput: View {
    let input: ToolFormat.Input
    @Binding var value: ToolJSON?
    let compact: Bool

    var body: some View {
        let selection = Binding<Double>(get: { ToolFormat.inputValue(input, value) }, set: { value = .number($0) })
        VStack(alignment: .leading, spacing: 5) {
            CommunityInputLabel(text: input.label, compact: compact)
            if input.options.count <= (compact ? 3 : 4) {
                Picker(input.label, selection: selection) { options }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .frame(maxWidth: compact ? CGFloat.infinity : 460, alignment: .leading)
            } else {
                Picker(input.label, selection: selection) { options }
                    .pickerStyle(.menu)
                    .labelsHidden()
                    .controlSize(compact ? .regular : .large)
                    .frame(maxWidth: 320, alignment: .leading)
            }
        }
    }

    private var options: some View {
        ForEach(input.options, id: \.value) { o in
            Text(o.label).tag(o.value)
        }
    }
}

/// A toggle: a switch.
private struct CommunityToggleInput: View {
    let input: ToolFormat.Input
    @Binding var value: ToolJSON?
    let compact: Bool

    var body: some View {
        Toggle(isOn: Binding(get: { ToolFormat.inputValue(input, value) == 1 }, set: { value = .bool($0) })) {
            Text(input.label).font(compact ? Font.sCallout : Font.sBody)
        }
        .toggleStyle(.switch)
        .controlSize(compact ? .small : .regular)
    }
}

// MARK: - Results

/// What the values come to: the first result shown large in the tool's colour, the rest in rows (a preview's first few);
/// a result its formula hides is left out, and a value that is not a number reads "—".
struct CommunityResults: View {
    let tool: ToolFormat.Tool
    let answers: [ToolFormat.Answer]
    let compact: Bool

    /// How many rows under the first a preview shows.
    private static let previewRows = 3

    var body: some View {
        let shown = answers.filter(\.shown)
        VStack(alignment: .leading, spacing: compact ? 8 : 12) {
            if let first = shown.first {
                big(first)
                let rest = Array(shown.dropFirst())
                if !rest.isEmpty {
                    rows(compact ? Array(rest.prefix(Self.previewRows)) : rest)
                }
                if compact && rest.count > Self.previewRows {
                    Text("And \(toolPlural(rest.count - Self.previewRows, "result")) more")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                }
            } else {
                EmptyNote(text: "Nothing to show for these values.", symbol: "eye.slash")
            }
        }
    }

    private static func line(_ a: ToolFormat.Answer) -> String { a.unit.isEmpty ? a.text : "\(a.text) \(a.unit)" }

    private func big(_ a: ToolFormat.Answer) -> some View {
        let color = Color(hex: tool.color)
        return VStack(alignment: .leading, spacing: 4) {
            Text(a.label)
                .font((compact ? Font.sCaption : Font.sSubheadline).weight(.semibold))
                .foregroundStyle(.secondary)
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(a.text)
                    .font(.system(size: compact ? 30 : 60, weight: .bold, design: .rounded).monospacedDigit())
                    .foregroundStyle(color)
                    .lineLimit(1)
                    .minimumScaleFactor(0.4)
                    .contentTransition(.numericText())
                    .textSelection(.enabled)
                if !a.unit.isEmpty {
                    Text(a.unit)
                        .font(compact ? Font.sHeadline : Font.sTitle2)
                        .foregroundStyle(.secondary)
                }
            }
        }
        .padding(compact ? 12 : 20)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: compact ? 14 : 18, tint: color)
        .contextMenu {
            Button("Copy \(a.label)") { copyToPasteboard(Self.line(a)) }
        }
        .accessibilityElement(children: .combine)
    }

    private func rows(_ list: [ToolFormat.Answer]) -> some View {
        VStack(spacing: 0) {
            ForEach(Array(list.enumerated()), id: \.offset) { i, a in
                VStack(spacing: 0) {
                    if i > 0 { RowDivider(inset: 0) }
                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        Text(a.label)
                            .font(compact ? Font.sCallout : Font.sBody)
                            .foregroundStyle(.secondary)
                        Spacer(minLength: 8)
                        Text(Self.line(a))
                            .font((compact ? Font.sCallout : Font.sBody).weight(.semibold).monospacedDigit())
                            .lineLimit(1)
                            .minimumScaleFactor(0.6)
                            .textSelection(.enabled)
                    }
                    .padding(.vertical, compact ? 6 : 9)
                    .contentShape(Rectangle())
                    .contextMenu {
                        Button("Copy \(a.label)") { copyToPasteboard(Self.line(a)) }
                    }
                    .accessibilityElement(children: .combine)
                }
            }
        }
        .padding(.horizontal, compact ? 12 : 16)
        .padding(.vertical, 4)
        .card(radius: compact ? 12 : 16)
    }
}

// MARK: - The table

/// A tool's table, as it is: its columns' names over its rows (a preview's first few).
struct CommunityTableView: View {
    let table: ToolFormat.Table
    let compact: Bool

    private static let previewRows = 3

    var body: some View {
        let rows = compact ? Array(table.rows.prefix(Self.previewRows)) : table.rows
        VStack(alignment: .leading, spacing: compact ? 6 : 10) {
            Text(table.title.isEmpty ? "Table" : table.title)
                .font(compact ? Font.sSubheadline.weight(.semibold) : Font.sTitle3)
                .foregroundStyle(compact ? Color.secondary : Color.primary)
            Grid(alignment: .leading, horizontalSpacing: compact ? 14 : 22, verticalSpacing: compact ? 5 : 8) {
                GridRow {
                    ForEach(Array(table.columns.enumerated()), id: \.offset) { _, c in
                        Text(c)
                            .font((compact ? Font.sCaption : Font.sSubheadline).weight(.semibold))
                            .foregroundStyle(.secondary)
                    }
                }
                Divider()
                ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                    GridRow {
                        ForEach(Array(row.enumerated()), id: \.offset) { _, cell in
                            Text(cell)
                                .font(compact ? Font.sCallout : Font.sBody)
                                .fixedSize(horizontal: false, vertical: true)
                                .textSelection(.enabled)
                        }
                    }
                }
            }
            .padding(compact ? 12 : 16)
            .frame(maxWidth: compact ? CGFloat.infinity : 760, alignment: .leading)
            .card(radius: compact ? 12 : 16)
            if compact && table.rows.count > Self.previewRows {
                Text("And \(toolPlural(table.rows.count - Self.previewRows, "row")) more")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

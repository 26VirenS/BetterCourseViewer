import AppKit
import SwiftUI

/// An element of the periodic table (PeriodicData.swift).
struct PTElement: Identifiable, Hashable {
    let number: Int
    let symbol: String
    let name: String
    let mass: Double
    let category: String
    let group: Int?
    let period: Int
    let block: String
    let phase: String
    let config: String
    let en: Double?
    let melt: Double?
    let boil: Double?
    let density: Double?
    let found: String?
    let x: Int
    let y: Int
    let summary: String

    var id: Int { number }

    /// A whole number is the longest-lived isotope's mass, the convention for an element with no stable one.
    var massText: String {
        mass == mass.rounded() ? "[\(Int(mass))]" : String(format: "%.3f", mass)
    }

    var line: String { "\(name) · \(number) · \(massText)" }

    var kind: PTCategory { PTCategory.of(category) }

    static func celsius(_ k: Double?) -> String {
        guard let k else { return "—" }
        return "\(Int((k - 273.15).rounded())) °C"
    }

    var densityText: String {
        guard let density else { return "—" }
        return "\(PTElement.trim(density)) \(phase == "Gas" ? "g/L" : "g/cm³")"
    }

    static func trim(_ v: Double) -> String {
        v == v.rounded() && abs(v) < 1e9 ? String(Int(v)) : String(v)
    }
}

/// A kind of element, and its colour.
struct PTCategory: Identifiable, Hashable {
    let key: String
    let label: String
    let hex: String

    var id: String { key }
    var color: Color { Color(hex: hex) }

    static let all: [PTCategory] = [
        PTCategory(key: "alkali metal", label: "Alkali metal", hex: "#ff453a"),
        PTCategory(key: "alkaline earth metal", label: "Alkaline earth", hex: "#ff9f0a"),
        PTCategory(key: "transition metal", label: "Transition metal", hex: "#ffd60a"),
        PTCategory(key: "post-transition metal", label: "Post-transition", hex: "#30d158"),
        PTCategory(key: "metalloid", label: "Metalloid", hex: "#64d2ff"),
        PTCategory(key: "nonmetal", label: "Nonmetal", hex: "#0a84ff"),
        PTCategory(key: "noble gas", label: "Noble gas", hex: "#bf5af2"),
        PTCategory(key: "lanthanide", label: "Lanthanide", hex: "#ff375f"),
        PTCategory(key: "actinide", label: "Actinide", hex: "#ac8e68"),
        PTCategory(key: "unknown", label: "Unknown", hex: "#8e8e93"),
    ]

    static func of(_ key: String) -> PTCategory {
        all.first { $0.key == key } ?? all[all.count - 1]
    }
}

/// The elements, read once from their JSON.
enum PeriodicTable {
    static let elements: [PTElement] = {
        guard let data = PeriodicData.json.data(using: .utf8),
              let rows = (try? JSONSerialization.jsonObject(with: data)) as? [[Any]] else { return [] }
        return rows.compactMap { PeriodicTable.element($0) }
    }()

    private static func element(_ r: [Any]) -> PTElement? {
        guard r.count >= 18 else { return nil }
        func num(_ i: Int) -> Double? { (r[i] as? NSNumber)?.doubleValue }
        func int(_ i: Int) -> Int? { (r[i] as? NSNumber)?.intValue }
        func str(_ i: Int) -> String? { r[i] as? String }
        guard let n = int(0), let symbol = str(1), let name = str(2), let x = int(15), let y = int(16) else { return nil }
        return PTElement(number: n, symbol: symbol, name: name, mass: num(3) ?? 0, category: str(4) ?? "unknown", group: int(5),
                         period: int(6) ?? y, block: str(7) ?? "", phase: str(8) ?? "", config: str(9) ?? "", en: num(10),
                         melt: num(11), boil: num(12), density: num(13), found: str(14), x: x, y: y, summary: str(17) ?? "")
    }

    static func byNumber(_ n: Int) -> PTElement? { elements.first { $0.number == n } }

    /// What a search finds, best first: a symbol, name or number exactly; then ones starting with it; then names holding it.
    static func find(_ q: String) -> [PTElement] {
        let s = q.trimmingCharacters(in: .whitespaces).lowercased()
        guard !s.isEmpty else { return [] }
        let exact = elements.filter { $0.symbol.lowercased() == s || $0.name.lowercased() == s || String($0.number) == s }
        let starts = elements.filter { e in
            !exact.contains(e) && (e.symbol.lowercased().hasPrefix(s) || e.name.lowercased().hasPrefix(s) || String(e.number).hasPrefix(s))
        }
        let within = elements.filter { e in !exact.contains(e) && !starts.contains(e) && e.name.lowercased().contains(s) }
        return exact + starts + within
    }

    /// The element next to one in a direction (the arrow keys), skipping the gaps.
    static func step(from e: PTElement, dx: Int, dy: Int) -> PTElement? {
        var x = e.x + dx
        var y = e.y + dy
        var n = 0
        while x >= 1 && x <= 18 && y >= 1 && y <= 10 && n < 18 {
            if let hit = elements.first(where: { $0.x == x && $0.y == y }) { return hit }
            x += dx
            y += dy
            n += 1
        }
        return nil
    }
}

/// The table drawn: each element in its place (rows 1–7, the lanthanides and actinides under them after a gap), in its
/// kind's colour; the elements a search or a kind leaves out dimmed; and, in the space the table leaves at its top, the
/// element picked.
struct PTGrid: View {
    let selected: PTElement?
    let matches: Set<Int>?
    let category: String?
    var compact = false
    let pick: (PTElement) -> Void
    var hover: (PTElement?) -> Void = { _ in }

    private let gapRow: CGFloat = 0.45

    var body: some View {
        GeometryReader { geo in
            let gap: CGFloat = compact ? 2 : 4
            let byWidth = (geo.size.width - 17 * gap) / 18
            let byHeight = (geo.size.height - 8 * gap) / (9 + gapRow)
            let cell = max(8, min(byWidth, byHeight))
            ZStack(alignment: .topLeading) {
                if !compact, let e = selected {
                    PTCornerCard(element: e, cell: cell)
                        .frame(width: 10 * (cell + gap) - gap, height: 3 * (cell + gap) - gap)
                        .offset(x: 2 * (cell + gap) + cell * 0.4, y: 0)
                }
                ForEach(PeriodicTable.elements) { e in
                    PTCell(element: e, cell: cell, compact: compact, selected: selected == e, dimmed: dim(e), matched: matches?.contains(e.number) ?? false)
                        .frame(width: cell, height: cell)
                        .offset(x: CGFloat(e.x - 1) * (cell + gap), y: rowY(e.y, cell: cell, gap: gap))
                        .onTapGesture { pick(e) }
                        .onHover { inside in hover(inside ? e : nil) }
                }
                if !compact {
                    rowLabel("57–71", cell: cell).offset(x: 2 * (cell + gap), y: 5 * (cell + gap))
                    rowLabel("89–103", cell: cell).offset(x: 2 * (cell + gap), y: 6 * (cell + gap))
                }
            }
        }
        .aspectRatio(18 / (9 + gapRow), contentMode: .fit)
    }

    private func rowY(_ y: Int, cell: CGFloat, gap: CGFloat) -> CGFloat {
        if y <= 7 { return CGFloat(y - 1) * (cell + gap) }
        return 7 * (cell + gap) + cell * gapRow + CGFloat(y - 9) * (cell + gap)
    }

    private func dim(_ e: PTElement) -> Bool {
        if let matches, !matches.contains(e.number) { return true }
        if let category, e.category != category { return true }
        return false
    }

    private func rowLabel(_ text: String, cell: CGFloat) -> some View {
        Text(text)
            .font(.system(size: max(8, cell * 0.2), weight: .medium))
            .foregroundStyle(.secondary)
            .frame(width: cell, height: cell)
    }
}

/// One element's square.
private struct PTCell: View {
    let element: PTElement
    let cell: CGFloat
    let compact: Bool
    let selected: Bool
    let dimmed: Bool
    let matched: Bool
    @State private var hover = false

    var body: some View {
        let color = element.kind.color
        let shape = RoundedRectangle(cornerRadius: max(2, cell * 0.14), style: .continuous)
        VStack(spacing: 0) {
            if !compact && cell >= 34 {
                Text("\(element.number)")
                    .font(.system(size: cell * 0.19, weight: .medium).monospacedDigit())
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .opacity(0.75)
            }
            Text(element.symbol)
                .font(.system(size: cell * (compact ? 0.48 : 0.36), weight: .bold))
            if !compact && cell >= 52 {
                Text(element.name)
                    .font(.system(size: cell * 0.14))
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
                    .opacity(0.8)
            }
        }
        .padding(.horizontal, compact ? 0 : cell * 0.08)
        .padding(.vertical, compact ? 0 : cell * 0.05)
        .frame(width: cell, height: cell)
        .foregroundStyle(selected ? Color.white : Color.primary)
        .background(shape.fill(selected ? color : color.opacity(hover ? 0.42 : (matched ? 0.5 : 0.24))))
        .overlay(shape.strokeBorder(selected || matched ? color : Color.clear, lineWidth: selected ? 0 : 1.5))
        .opacity(dimmed ? 0.22 : 1)
        .scaleEffect(hover && !compact ? 1.06 : 1)
        .zIndex(hover ? 1 : 0)
        .animation(Motion.hover, value: hover)
        .animation(Motion.snappy, value: dimmed)
        .contentShape(shape)
        .onHover { hover = $0 }
        .help("\(element.name) · \(element.number)")
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(element.name), \(element.number)")
        .accessibilityAddTraits(.isButton)
    }
}

/// The element picked, in the space at the table's top: its symbol large, its number, name, kind and mass.
private struct PTCornerCard: View {
    let element: PTElement
    let cell: CGFloat

    var body: some View {
        let color = element.kind.color
        HStack(spacing: cell * 0.3) {
            VStack(spacing: 0) {
                Text("\(element.number)")
                    .font(.system(size: cell * 0.3, weight: .semibold).monospacedDigit())
                    .frame(maxWidth: .infinity, alignment: .leading)
                Text(element.symbol)
                    .font(.system(size: cell * 1.1, weight: .bold))
                    .minimumScaleFactor(0.5)
            }
            .padding(cell * 0.12)
            .frame(width: cell * 2.4, height: cell * 2.4)
            .foregroundStyle(.white)
            .background(color, in: RoundedRectangle(cornerRadius: cell * 0.3, style: .continuous))
            VStack(alignment: .leading, spacing: cell * 0.06) {
                Text(element.name)
                    .font(.system(size: cell * 0.5, weight: .bold))
                    .lineLimit(1)
                    .minimumScaleFactor(0.5)
                Text("\(element.kind.label) · \(element.massText) u")
                    .font(.system(size: cell * 0.26))
                    .foregroundStyle(color)
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
                Text(element.config)
                    .font(.system(size: cell * 0.24).monospaced())
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
            }
            Spacer(minLength: 0)
        }
        .padding(.leading, cell * 0.1)
        .transition(.opacity)
        .id(element.number)
    }
}

// MARK: - The table as a tool

/// Periodic table: every element in its place, coloured by its kind, with the one picked large in the table's empty
/// corner and its facts under the table — group, period and block, its state at room temperature, its electrons, its
/// electronegativity, melting and boiling points, density, who found it — and a line about it. A search by symbol, name or
/// number lights the matches (Return picks the first); the legend lights a kind; the arrow keys walk the table.
struct PeriodicTableTool: View {
    @State private var selected: PTElement?
    @State private var query = ""
    @State private var category: String?
    @FocusState private var tableFocused: Bool

    private var matches: Set<Int>? {
        let q = query.trimmingCharacters(in: .whitespaces)
        return q.isEmpty ? nil : Set(PeriodicTable.find(q).map(\.number))
    }

    var body: some View {
        ToolPage(spacing: 18) {
            top
            PTGrid(selected: selected, matches: matches, category: category) { e in
                withAnimation(Motion.snappy) { selected = e }
                tableFocused = true
            }
            .frame(maxWidth: 1300)
            .focusable()
            .focused($tableFocused)
            .focusEffectDisabled()
            .onKeyPress(keys: [.leftArrow, .rightArrow, .upArrow, .downArrow]) { press in
                walk(press.key)
            }
            if let e = selected {
                PTFacts(element: e)
                    .transition(.opacity)
            } else {
                Text("Press an element, or type its symbol, name or number.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        }
        .onAppear {
            if let raw = ToolsCenter.shared.takeInfo("element"), let n = Int(raw) { selected = PeriodicTable.byNumber(n) }
        }
    }

    private var top: some View {
        HStack(alignment: .center, spacing: 16) {
            TextField("Symbol, name or number", text: $query)
                .textFieldStyle(.roundedBorder)
                .font(.sBody)
                .controlSize(.large)
                .frame(width: 260)
                .onSubmit {
                    if let m = PeriodicTable.find(query).first {
                        withAnimation(Motion.snappy) { selected = m }
                        query = ""
                    }
                }
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 6) {
                    ForEach(PTCategory.all) { c in legendChip(c) }
                }
                .padding(.vertical, 2)
            }
        }
    }

    private func legendChip(_ c: PTCategory) -> some View {
        let on = category == c.key
        return Button {
            withAnimation(Motion.snappy) { category = on ? nil : c.key }
        } label: {
            HStack(spacing: 6) {
                Circle().fill(c.color).frame(width: 9, height: 9)
                Text(c.label).font(.sCaption.weight(.medium))
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 5)
            .background(c.color.opacity(on ? 0.3 : 0.1), in: Capsule())
            .overlay(Capsule().strokeBorder(on ? c.color : Color.clear, lineWidth: 1))
        }
        .buttonStyle(.plain)
        .help(on ? "Show every kind" : "Light the \(c.label.lowercased()) elements")
    }

    private func walk(_ key: KeyEquivalent) -> KeyPress.Result {
        guard let e = selected ?? PeriodicTable.byNumber(1) else { return .ignored }
        guard selected != nil else {
            selected = e
            return .handled
        }
        let dx = key == .leftArrow ? -1 : (key == .rightArrow ? 1 : 0)
        let dy = key == .upArrow ? -1 : (key == .downArrow ? 1 : 0)
        if let n = PeriodicTable.step(from: e, dx: dx, dy: dy) {
            withAnimation(Motion.snappy) { selected = n }
        }
        return .handled
    }
}

/// The picked element's facts, under the table: as many to a row as the window has room for, then its line and where to
/// read more.
private struct PTFacts: View {
    let element: PTElement

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 14)], alignment: .leading, spacing: 14) {
                fact("Group · period · block", "\(element.group.map { "Group \($0)" } ?? "No group") · Period \(element.period) · \(element.block)-block")
                fact("At room temperature", element.phase.isEmpty ? "—" : element.phase)
                fact("Electrons", element.config.isEmpty ? "—" : element.config)
                fact("Electronegativity", element.en.map { PTElement.trim($0) } ?? "—")
                fact("Melts · boils", "\(PTElement.celsius(element.melt)) · \(PTElement.celsius(element.boil))")
                fact("Density", element.densityText)
            }
            HStack(alignment: .firstTextBaseline, spacing: 12) {
                Text(element.summary)
                    .font(.sBody)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
            HStack(spacing: 14) {
                if let found = element.found, !found.isEmpty {
                    Label("Found by \(found)", systemImage: "sparkle.magnifyingglass")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
                if let url = URL(string: "https://en.wikipedia.org/wiki/\(element.name)") {
                    Link(destination: url) {
                        Label("Wikipedia", systemImage: "arrow.up.right.square")
                    }
                    .font(.sCallout)
                }
            }
        }
        .id(element.number)
    }

    private func fact(_ k: String, _ v: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(k.uppercased())
                .font(.system(size: 11.5, weight: .semibold))
                .tracking(0.5)
                .foregroundStyle(.secondary)
            Text(v)
                .font(.sHeadline)
                .textSelection(.enabled)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 16, tint: element.kind.color)
    }
}

/// The table's pin, opened: the whole table, small, with a search that lights the matches and a line naming the element
/// under the pointer (or the first match). A press on an element opens the table on it.
struct PeriodicTableCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var query = ""
    @State private var over: PTElement?

    private var found: [PTElement] { PeriodicTable.find(query) }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                TextField("Find an element", text: $query)
                    .textFieldStyle(.roundedBorder)
                    .font(.sCallout)
                    .frame(width: 160)
                    .onSubmit { if let e = found.first { openFull(["element": String(e.number)]) } }
                Text(line)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .truncationMode(.tail)
            }
            PTGrid(selected: nil, matches: query.trimmingCharacters(in: .whitespaces).isEmpty ? nil : Set(found.map(\.number)), category: nil, compact: true, pick: { e in
                openFull(["element": String(e.number)])
            }, hover: { e in over = e })
        }
    }

    private var line: String {
        if let e = over ?? found.first { return e.line }
        return query.trimmingCharacters(in: .whitespaces).isEmpty ? "" : "No element"
    }
}

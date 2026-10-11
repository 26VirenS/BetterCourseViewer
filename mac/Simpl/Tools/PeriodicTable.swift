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
/// colour for what the table is coloured by; the elements a search or a kind leaves out dimmed; and, in the space the
/// table leaves at its top, the element under the pointer (1.3.51: only while the pointer is on it — a press opens it).
struct PTGrid: View {
    /// The element in the table's empty top: the one under the pointer.
    let shown: PTElement?
    let matches: Set<Int>?
    let category: String?
    var colouring: PTColouring = .category
    var compact = false
    /// A press on an element, with its square's frame in the "ptRoot" space (the full table opens it from there).
    let pick: (PTElement, CGRect) -> Void
    var hover: (PTElement?) -> Void = { _ in }

    private let gapRow: CGFloat = 0.45

    var body: some View {
        GeometryReader { geo in
            let gap: CGFloat = compact ? 2 : 4
            let byWidth = (geo.size.width - 17 * gap) / 18
            let byHeight = (geo.size.height - 8 * gap) / (9 + gapRow)
            let cell = max(8, min(byWidth, byHeight))
            let origin = geo.frame(in: .named("ptRoot")).origin
            ZStack(alignment: .topLeading) {
                if let e = shown {
                    PTCornerCard(element: e, cell: cell, colouring: colouring)
                        .frame(width: 10 * (cell + gap) - gap, height: 3 * (cell + gap) - gap)
                        .offset(x: 2 * (cell + gap), y: 0)
                        .allowsHitTesting(false)
                }
                ForEach(PeriodicTable.elements) { e in
                    let x = CGFloat(e.x - 1) * (cell + gap)
                    let y = rowY(e.y, cell: cell, gap: gap)
                    PTCell(element: e, cell: cell, compact: compact, colouring: colouring, dimmed: dim(e), matched: matches?.contains(e.number) ?? false)
                        .frame(width: cell, height: cell)
                        .offset(x: x, y: y)
                        .onTapGesture { pick(e, CGRect(x: origin.x + x, y: origin.y + y, width: cell, height: cell)) }
                        .onHover { inside in hover(inside ? e : nil) }
                }
                if !compact {
                    rowLabel("57–71", cell: cell).offset(x: 2 * (cell + gap), y: 5 * (cell + gap))
                    rowLabel("89–103", cell: cell).offset(x: 2 * (cell + gap), y: 6 * (cell + gap))
                }
            }
            .frame(width: geo.size.width, height: geo.size.height, alignment: .topLeading)
            .animation(Motion.hover, value: shown?.number)
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

/// One element's square: its number, symbol and — as the table is coloured — its name or its value.
private struct PTCell: View {
    let element: PTElement
    let cell: CGFloat
    let compact: Bool
    let colouring: PTColouring
    let dimmed: Bool
    let matched: Bool
    @State private var hover = false

    var body: some View {
        let color = colouring.color(element)
        // (a property's scale is drawn solid, its words white; a kind's colour washed, as before)
        let solid = colouring != .category
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
            if !compact && cell >= 48 {
                Text(colouring == .category ? element.name : colouring.text(element))
                    .font(.system(size: cell * 0.14).monospacedDigit())
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
                    .opacity(0.8)
            }
        }
        .padding(.horizontal, compact ? 0 : cell * 0.08)
        .padding(.vertical, compact ? 0 : cell * 0.05)
        .frame(width: cell, height: cell)
        .foregroundStyle(solid ? Color.white : Color.primary)
        .background(shape.fill(solid ? color.opacity(hover ? 1 : 0.88) : color.opacity(hover ? 0.42 : (matched ? 0.5 : 0.24))))
        .overlay(shape.strokeBorder(matched ? (solid ? Color.white : color) : Color.clear, lineWidth: 1.5))
        .opacity(dimmed ? 0.22 : 1)
        .scaleEffect(hover && !compact ? 1.08 : 1)
        .zIndex(hover ? 1 : 0)
        .animation(Motion.hover, value: hover)
        .animation(Motion.snappy, value: dimmed)
        .animation(Motion.gentle, value: colouring)
        .contentShape(shape)
        .onHover { hover = $0 }
        .help("\(element.name) · \(element.number)")
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(element.name), \(element.number)")
        .accessibilityAddTraits(.isButton)
    }
}

/// The element under the pointer, in the space at the table's top: its symbol large, its number, name, kind and mass,
/// its electrons and — the table coloured by a property — its value.
private struct PTCornerCard: View {
    let element: PTElement
    let cell: CGFloat
    var colouring: PTColouring = .category

    var body: some View {
        let color = colouring.color(element)
        HStack(spacing: cell * 0.3) {
            VStack(spacing: 0) {
                Text("\(element.number)")
                    .font(.system(size: max(8, cell * 0.3), weight: .semibold).monospacedDigit())
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
                // (never smaller than reads: the pin's table is small)
                Text(element.name)
                    .font(.system(size: max(14, cell * 0.5), weight: .bold))
                    .lineLimit(1)
                    .minimumScaleFactor(0.5)
                Text("\(element.kind.label) · \(element.massText) u")
                    .font(.system(size: max(11, cell * 0.26)))
                    .foregroundStyle(element.kind.color)
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
                Text(PTConfig.pretty(element))
                    .font(.system(size: max(10.5, cell * 0.24)).monospaced())
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
                if colouring.scaled {
                    Text("\(colouring.label): \(colouring.text(element))\(colouring.unit.isEmpty || colouring.value(element) == nil ? "" : " \(colouring.unit)")")
                        .font(.system(size: max(10.5, cell * 0.24), weight: .semibold))
                        .lineLimit(1)
                        .minimumScaleFactor(0.6)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, cell * 0.3)
        .transition(.opacity)
        .id(element.number)
    }
}

// MARK: - The table as a tool

/// (1.3.51) The full Periodic Table: the table, coloured by any of its properties (with the scale, each cell's value and,
/// if asked, the trend arrows), the element under the pointer in its empty top, and a press opening the element's own
/// page, grown out of its square; and beside the table, the Lewis structure maker and the 3D molecule builder.
struct PeriodicTableTool: View {
    enum Page: String, CaseIterable, Identifiable {
        case table, lewis, molecules
        var id: String { rawValue }
        var label: String { self == .table ? "Table" : (self == .lewis ? "Lewis structures" : "Molecule builder") }
        var symbol: String { self == .table ? "square.grid.3x3" : (self == .lewis ? "circle.grid.cross" : "atom") }
    }

    @State private var page: Page = .table
    @State private var open: PTElement?
    @State private var anchor: UnitPoint = .center
    @State private var hovered: PTElement?
    @State private var query = ""
    @State private var category: String?
    @State private var colouring: PTColouring = .category
    @State private var trends = false
    @StateObject private var molecule = MoleculeModel()

    private var matches: Set<Int>? {
        let q = query.trimmingCharacters(in: .whitespaces)
        return q.isEmpty ? nil : Set(PeriodicTable.find(q).map(\.number))
    }

    var body: some View {
        GeometryReader { root in
            ZStack(alignment: .top) {
                if let e = open {
                    PTElementPage(element: e, close: close, go: { n in
                        if let next = PeriodicTable.byNumber(n) { withAnimation(Motion.gentle) { open = next } }
                    })
                    .transition(.asymmetric(insertion: .scale(scale: 0.06, anchor: anchor).combined(with: .opacity),
                                            removal: .scale(scale: 0.06, anchor: anchor).combined(with: .opacity)))
                    .zIndex(1)
                } else {
                    VStack(alignment: .leading, spacing: 0) {
                        pages
                            .padding(.horizontal, 40)
                            .padding(.bottom, 10)
                            .frame(maxWidth: 1480 + 80, alignment: .leading)
                            .frame(maxWidth: .infinity)
                        switch page {
                        case .table: table(root.size)
                        case .lewis: LewisMaker { lewis in
                            molecule.load(lewis: lewis)
                            withAnimation(Motion.gentle) { page = .molecules }
                        }
                        case .molecules: MoleculeBuilder(model: molecule)
                        }
                    }
                    .transition(.opacity)
                }
            }
            .frame(width: root.size.width, height: root.size.height, alignment: .top)
        }
        .coordinateSpace(name: "ptRoot")
        .onAppear {
            if let raw = ToolsCenter.shared.takeInfo("element"), let n = Int(raw), let e = PeriodicTable.byNumber(n) {
                anchor = .center
                open = e
            }
        }
    }

    // MARK: The pages

    private var pages: some View {
        HStack(spacing: 8) {
            GlassGroup(spacing: 6) {
                ForEach(Page.allCases) { p in
                    Button {
                        withAnimation(Motion.gentle) { page = p }
                    } label: {
                        Label(p.label, systemImage: p.symbol)
                    }
                    .glassButton(prominent: page == p)
                    .controlSize(.large)
                    .help(p == .table ? "The periodic table" : (p == .lewis ? "Draw a molecule's Lewis structure" : "Build a molecule and turn it in 3D"))
                }
            }
            Spacer(minLength: 0)
        }
    }

    // MARK: The table

    private func table(_ size: CGSize) -> some View {
        ToolPage(spacing: 14) {
            top
            colourBar
            trendWrap {
                PTGrid(shown: hovered, matches: matches, category: colouring == .category ? category : nil, colouring: colouring,
                       pick: { e, rect in openElement(e, from: rect, in: size) },
                       hover: { e in hovered = e })
            }
            .frame(maxWidth: 1300)
            Text("Point at an element to see it here; press it to open its page. Type a symbol, name or number and press Return to open it.")
                .font(.sCallout)
                .foregroundStyle(.secondary)
        }
    }

    private func openElement(_ e: PTElement, from rect: CGRect, in size: CGSize) {
        anchor = UnitPoint(x: size.width > 0 ? rect.midX / size.width : 0.5, y: size.height > 0 ? rect.midY / size.height : 0.5)
        hovered = nil
        withAnimation(.spring(response: 0.5, dampingFraction: 0.86)) { open = e }
    }

    private func close() {
        withAnimation(.spring(response: 0.45, dampingFraction: 0.9)) { open = nil }
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
                        query = ""
                        anchor = .center
                        withAnimation(.spring(response: 0.5, dampingFraction: 0.86)) { open = m }
                    }
                }
            if colouring == .category {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 6) {
                        ForEach(PTCategory.all) { c in legendChip(c) }
                    }
                    .padding(.vertical, 2)
                }
            } else if colouring.scaled {
                scale
            } else {
                keyChips
            }
        }
        .animation(Motion.gentle, value: colouring)
    }

    /// The scale of the property the table is coloured by: lowest to highest.
    private var scale: some View {
        HStack(spacing: 10) {
            Text(colouring.label).font(.sCallout.weight(.semibold))
            Text(colouring.lowText).font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
            LinearGradient(colors: (0...8).map { PTColouring.ramp(Double($0) / 8) }, startPoint: .leading, endPoint: .trailing)
                .frame(height: 8)
                .clipShape(Capsule())
                .frame(minWidth: 160, maxWidth: 360)
            Text(colouring.highText).font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
            if !colouring.unit.isEmpty {
                Text(colouring.unit).font(.sCaption).foregroundStyle(.secondary)
            }
        }
    }

    /// The blocks' or the states' colours.
    private var keyChips: some View {
        let keys: [(String, String)] = colouring == .block
            ? [("s", "s-block"), ("p", "p-block"), ("d", "d-block"), ("f", "f-block")].map { ($0.1, PTColouring.blockColors[$0.0] ?? "") }
            : [("Solid", "Solid"), ("Liquid", "Liquid"), ("Gas", "Gas")].map { ($0.1, PTColouring.stateColors[$0.0] ?? "") }
        return HStack(spacing: 6) {
            ForEach(keys.indices, id: \.self) { i in
                HStack(spacing: 6) {
                    Circle().fill(Color(hex: keys[i].1)).frame(width: 9, height: 9)
                    Text(keys[i].0).font(.sCaption.weight(.medium))
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 5)
                .background(Color(hex: keys[i].1).opacity(0.18), in: Capsule())
            }
        }
    }

    private var colourBar: some View {
        HStack(spacing: 12) {
            Text("Colour by").font(.sCallout).foregroundStyle(.secondary)
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 4) {
                    ForEach(PTColouring.allCases) { c in
                        Button {
                            withAnimation(Motion.gentle) { colouring = c }
                        } label: {
                            Text(c.label)
                                .font(.sCaption.weight(colouring == c ? .semibold : .regular))
                                .padding(.horizontal, 11)
                                .padding(.vertical, 6)
                                .background(colouring == c ? Color.primary.opacity(0.14) : Color.clear, in: Capsule())
                                .contentShape(Capsule())
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(4)
                .background(Color.primary.opacity(0.05), in: Capsule())
            }
            Toggle("Trend arrows", isOn: $trends.animation(Motion.gentle))
                .toggleStyle(.switch)
                .controlSize(.small)
                .font(.sCallout)
                .disabled(colouring.trend == nil)
                .help(colouring.trend == nil ? "\(colouring.label) has no simple trend across the table" : "Show how \(colouring.label.lowercased()) changes across the table")
        }
    }

    /// The table with its trend arrows, when they are on: across the top, the trend across a period; down the side, the
    /// trend in a group.
    @ViewBuilder
    private func trendWrap<G: View>(@ViewBuilder _ grid: () -> G) -> some View {
        if trends, let t = colouring.trend {
            VStack(spacing: 6) {
                HStack(spacing: 6) {
                    if t.across < 0 { Image(systemName: "arrow.left").font(.system(size: 13, weight: .bold)) }
                    Rectangle().frame(height: 2)
                    Text(t.acrossWords).font(.sCaption.weight(.semibold)).fixedSize()
                    Rectangle().frame(height: 2)
                    if t.across > 0 { Image(systemName: "arrow.right").font(.system(size: 13, weight: .bold)) }
                }
                .foregroundStyle(Color.yellow)
                .padding(.leading, 30)
                grid()
                    .padding(.leading, 30)
                    .overlay(alignment: .leading) {
                        VStack(spacing: 4) {
                            if t.up > 0 { Image(systemName: "arrow.up").font(.system(size: 13, weight: .bold)) }
                            Rectangle().frame(width: 2)
                            Text(t.upWords)
                                .font(.sCaption.weight(.semibold))
                                .fixedSize()
                                .rotationEffect(.degrees(-90))
                                .frame(width: 16, height: 140)
                            Rectangle().frame(width: 2)
                            if t.up < 0 { Image(systemName: "arrow.down").font(.system(size: 13, weight: .bold)) }
                        }
                        .frame(width: 20)
                        .foregroundStyle(Color.yellow)
                    }
            }
            .transition(.opacity)
        } else {
            grid()
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
}

/// The table's pin, opened: the whole table, small, with a search that lights the matches and a line naming the element
/// under the pointer (or the first match). (1.3.51) The element under the pointer shows in the table's empty top; a
/// press opens the whole table on its page.
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
            PTGrid(shown: over, matches: query.trimmingCharacters(in: .whitespaces).isEmpty ? nil : Set(found.map(\.number)), category: nil, compact: true,
                   pick: { e, _ in openFull(["element": String(e.number)]) }, hover: { e in over = e })
        }
        // (the screenshot suite: -SimplPinElement 25 shows that element's card)
        .onAppear {
            let n = UserDefaults.standard.integer(forKey: "SimplPinElement")
            if n > 0, over == nil { over = PeriodicTable.byNumber(n) }
        }
    }

    private var line: String {
        if let e = over ?? found.first { return e.line }
        return query.trimmingCharacters(in: .whitespaces).isEmpty ? "" : "No element"
    }
}

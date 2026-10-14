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

    /// (1.3.65) by number, looked up rather than searched for
    private static let numbered: [Int: PTElement] = Dictionary(elements.map { ($0.number, $0) }, uniquingKeysWith: { a, _ in a })
    static func byNumber(_ n: Int) -> PTElement? { numbered[n] }

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
                // (tools-3) the top three rows and the space between them, where the element under the pointer shows:
                // what the tour's first step lights, its card under it rather than over the table
                Color.clear
                    .frame(width: max(0, 18 * (cell + gap) - gap), height: max(0, 3 * (cell + gap) - gap))
                    .allowsHitTesting(false)
                    .tourTarget(compact ? nil : "ptTop")
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
                        .equatable()
                        .frame(width: cell, height: cell)
                        .tourTarget(!compact && e.number == 6 ? "ptCarbon" : nil)
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
/// (1.3.65) Equatable: a cell whose element, size, view and marks are as they were is not drawn again when the table is
/// (a hover elsewhere, the preview changing).
private struct PTCell: View, Equatable {
    static func == (a: PTCell, b: PTCell) -> Bool {
        a.element.number == b.element.number && a.cell == b.cell && a.compact == b.compact && a.colouring == b.colouring
            && a.dimmed == b.dimmed && a.matched == b.matched
    }

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
        // (tools-11) coloured by a property on a scale, a square too small for three lines shows the value — what the
        // view is about — in place of the number, rather than the symbol alone
        let valueFirst = colouring.scaled && !compact && cell >= 26 && cell < 48
        VStack(spacing: 0) {
            if !compact && cell >= 34 && !valueFirst {
                Text("\(element.number)")
                    .font(.system(size: cell * 0.19, weight: .medium).monospacedDigit())
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .opacity(0.75)
            }
            Text(element.symbol)
                .font(.system(size: cell * (compact ? 0.48 : 0.36), weight: .bold))
            if valueFirst {
                Text(colouring.text(element))
                    .font(.system(size: max(7, cell * 0.22), weight: .medium).monospacedDigit())
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
                    .opacity(0.9)
            } else if !compact && cell >= 48 {
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
        /// (tools-4) The heading while the page shows.
        var title: String { self == .table ? "Periodic table" : label }
        /// (tools-4) The 3D builder's is its own, not the atom the Periodic Table's tile wears.
        var symbol: String { self == .table ? "square.grid.3x3" : (self == .lewis ? "circle.grid.cross" : "rotate.3d") }
    }

    /// (1.3.62) which page is shown, chosen in the tool's header
    @ObservedObject private var nav = PTNav.shared
    private var page: Page { nav.page }
    @State private var open: PTElement?
    @State private var anchor: UnitPoint = .center
    @State private var hovered: PTElement?
    @ObservedObject private var search = PTSearch.shared
    @State private var category: String?
    @State private var colouring: PTColouring = .category
    @StateObject private var molecule = MoleculeModel()
    /// (1.3.59) The tour: seen once; the step shown, if it is on. (tools-3) Offered the first time the table opens, not
    /// started; skipped, closed or declined, it counts as seen.
    @AppStorage("simpl.ptTourSeen") private var tourSeen = false
    @State private var tourStep: Int?
    @State private var offerTour = false
    /// A step gone back to: it waits for Next rather than moving on by itself (what it asks may be done already).
    @State private var tourHold: Int?
    /// (1.3.64) the step's thing done: its card says so for a moment before the next comes
    @State private var tourDone = false
    @ObservedObject private var tourWord = PTTour.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var matches: Set<Int>? {
        let q = search.query.trimmingCharacters(in: .whitespaces)
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
                        switch page {
                        case .table: table(root.size)
                        case .lewis: LewisMaker { graph, structure in
                            molecule.load(graph: graph, structure: structure)
                            withAnimation(Motion.gentle) { nav.page = .molecules }
                        }
                        case .molecules: MoleculeBuilder(model: molecule)
                        }
                    }
                    .transition(.opacity)
                }
            }
            .frame(width: root.size.width, height: root.size.height, alignment: .top)
            // (tools-3) the tour offered, the first time: a small card at the tool's foot
            .overlay(alignment: .bottom) {
                if offerTour && tourStep == nil {
                    PTTourOffer(take: {
                        withAnimation(Motion.gentle) { offerTour = false }
                        tour(0)
                    }, later: declineTour)
                    .padding(.horizontal, 24)
                    .padding(.bottom, 24)
                    .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.move(edge: .bottom).combined(with: .opacity))
                }
            }
            // (1.3.61) the guided tour: the tool dimmed, the step's control lit, its card beside it
            .overlay(alignment: .topLeading) {
                if let i = tourStep {
                    PTTourOverlay(step: i, origin: root.frame(in: .global).origin, size: root.size, done: tourDone,
                                  next: { nextTour(i) }, back: { tour(i - 1, hold: true) }, skip: endTour)
                        .transition(.opacity)
                }
            }
        }
        .coordinateSpace(name: "ptRoot")
        // (1.3.54) the search, in the tool's header: Return opens the first element it finds
        // (1.3.62) a page chosen in the header while an element is open: back from it to that page; Tour asked for there
        .onChange(of: nav.page) { _, _ in
            if open != nil { withAnimation(Motion.gentle) { open = nil } }
        }
        .onChange(of: nav.tourAsked) { _, _ in
            withAnimation(Motion.gentle) { offerTour = false }
            tour(0)
        }
        // (tools-29) the heading's one Back says "‹ Table" while an element is open, and closes it
        .onChange(of: open?.number) { _, _ in nav.elementOpen = open != nil }
        .onChange(of: nav.closeAsked) { _, _ in close() }
        // (1.3.64) an element picked from the search's results: opened
        .onChange(of: search.pickedCount) { _, _ in
            guard let n = search.picked, let e = PeriodicTable.byNumber(n) else { return }
            search.query = ""
            anchor = .center
            withAnimation(.spring(response: 0.5, dampingFraction: 0.86)) { open = e }
        }
        // (the tour's step, done: watched for in what the tool shows and what it is told)
        .onChange(of: hovered?.number) { _, _ in checkTour() }
        .onChange(of: colouring) { _, _ in checkTour() }
        .onChange(of: open?.number) { _, _ in checkTour() }
        .onChange(of: nav.page) { _, _ in checkTour() }
        .onChange(of: search.query) { _, _ in checkTour() }
        .onChange(of: tourWord.events) { _, _ in checkTour() }
        .onChange(of: search.submitted) { _, _ in
            guard let m = PeriodicTable.find(search.query).first else { return }
            search.query = ""
            anchor = .center
            withAnimation(.spring(response: 0.5, dampingFraction: 0.86)) { open = m }
        }
        .onAppear {
            if let raw = ToolsCenter.shared.takeInfo("element"), let n = Int(raw), let e = PeriodicTable.byNumber(n) {
                anchor = .center
                open = e
            }
            // (the screenshot suite: -SimplPTColour, -SimplPTPage, -SimplPTElement, -SimplPTMolecule)
            let d = UserDefaults.standard
            if let c = d.string(forKey: "SimplPTColour"), let v = PTColouring(rawValue: c) { colouring = v }
            if let p = d.string(forKey: "SimplPTPage"), let v = Page(rawValue: p) { nav.page = v }
            if let m = d.string(forKey: "SimplPTMolecule") { molecule.loadPreset(m) }
            // (1.3.78) a molecule handed over by the Biomolecule Builder, shown in the molecule builder as it is
            if let text = ToolsCenter.shared.takeInfo("molecule"), molecule.load(handOff: text) { nav.page = .molecules }
            let n = d.integer(forKey: "SimplPTElement")
            if n > 0, let e = PeriodicTable.byNumber(n) { open = e }
            nav.elementOpen = open != nil
            // (the tour: its step for the screenshot suite, -SimplPTTour 5; else, tools-3, offered on the first opening
            // — -SimplPTTourOffer YES for the suite's picture of it — and not while the suite pictures the tool (1.3.76:
            // nor an element's view, -SimplPTModel, which its page reads))
            let shot = ["SimplPTColour", "SimplPTPage", "SimplPTMolecule", "SimplPTElement", "SimplLewisPreset", "SimplPTModel"].contains { d.object(forKey: $0) != nil }
            if d.object(forKey: "SimplPTTour") != nil {
                tour(min(max(d.integer(forKey: "SimplPTTour"), 0), PTTourStep.all.count - 1))
            } else if d.bool(forKey: "SimplPTTourOffer") || (!tourSeen && !shot) {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                    guard tourStep == nil else { return }
                    withAnimation(Motion.gentle) { offerTour = true }
                }
            }
        }
        .onDisappear {
            nav.elementOpen = false
            // (tools-3: the tool left mid-tour — the tour closed with it, and seen)
            if tourStep != nil { endTour() }
        }
    }

    /// (tools-3) The offered tour turned down: not offered again (the heading's Tour runs it any time).
    private func declineTour() {
        tourSeen = true
        withAnimation(Motion.gentle) { offerTour = false }
    }

    /// The tour at a step: the tool set to what the step needs (the page its control is on, an element's page, a
    /// preset), and nothing yet done for it.
    private func tour(_ i: Int, hold: Bool = false) {
        guard PTTourStep.all.indices.contains(i) else { return }
        tourHold = hold ? i : nil
        let s = PTTourStep.all[i]
        PTTour.shared.active = true
        PTTourFrames.shared.wake()
        PTTour.shared.clearEvent()
        tourDone = false
        withAnimation(Motion.gentle) {
            tourStep = i
            if i == 0 {
                colouring = .category
                search.query = ""
                hovered = nil
            }
            if let n = s.element, let e = PeriodicTable.byNumber(n) {
                if open == nil { anchor = .center; open = e }
            } else if ["ptGrid", "ptTop", "ptCarbon", "ptColours"].contains(s.target) {
                open = nil
                nav.page = .table
            } else if s.target.hasPrefix("lewis") {
                open = nil
                if nav.page != .lewis { nav.page = .lewis }
            } else if s.target == "molViewer" {
                open = nil
                if nav.page != .molecules { nav.page = .molecules }
            } else if s.target.hasPrefix("ptPage") {
                open = nil
            }
        }
        if let m = s.molecule { molecule.loadPreset(m) }
        // (Draw asked for from the quick way: the quick way's water first)
        if s.target == "lewisWay" { PTTour.shared.showLewis("H₂O") }
        if let l = s.lewis { PTTour.shared.showLewis(l) }
        // (tools-3: no wait for a Next — every card has one, the step's thing a suggestion)
        checkTour()
    }

    /// The step's thing done: on to the next, or the tour ended after the last.
    private func checkTour() {
        guard let i = tourStep, PTTourStep.all.indices.contains(i), tourHold != i else { return }
        let done: Bool
        switch PTTourStep.all[i].act {
        case .hover: done = hovered != nil
        case .colour: done = colouring != .category
        case .open: done = open != nil
        case .page(let p): done = nav.page == p && open == nil
        case .event(let e): done = PTTour.shared.event == e
        case .typed: done = !search.query.trimmingCharacters(in: .whitespaces).isEmpty
        }
        guard done, !tourDone else { return }
        withAnimation(Motion.snappy) { tourDone = true }
        // (1.3.64) a breath after it is done — the card ticked — before the next step comes
        DispatchQueue.main.asyncAfter(deadline: .now() + (i == PTTourStep.all.count - 1 ? 2.2 : 1.6)) {
            guard tourStep == i else { return }
            nextTour(i)
        }
    }

    private func nextTour(_ i: Int) {
        if i + 1 < PTTourStep.all.count { tour(i + 1) } else { endTour() }
    }

    /// The tour over — its last step done, or (tools-3) left with Skip Tour, its ✕ or Escape: seen either way, so not
    /// again on its own; the tool left as it is.
    private func endTour() {
        tourSeen = true
        PTTour.shared.active = false
        PTTour.shared.showLewis(nil)
        withAnimation(Motion.gentle) { tourStep = nil }
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
                .tourTarget("ptGrid")
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
            if colouring == .category {
                // (tools-14) a row cut off at the window's edge fades there, with a chevron that scrolls it on
                ChipScroller {
                    GlassGroup(spacing: 6) {
                        HStack(spacing: 6) {
                            ForEach(PTCategory.all) { c in legendChip(c) }
                        }
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

    /// (1.3.54) What the table is coloured by, as glass buttons (the one chosen prominent).
    private var colourBar: some View {
        ChipScroller {
            GlassGroup(spacing: 6) {
                HStack(spacing: 6) {
                    ForEach(PTColouring.allCases) { c in
                        Button {
                            withAnimation(Motion.gentle) { colouring = c }
                        } label: {
                            Text(c.label).font(.sCallout.weight(colouring == c ? .semibold : .regular))
                        }
                        .glassButton(prominent: colouring == c)
                        .help("Colour the table by \(c.label.lowercased())")
                    }
                }
            }
            .padding(.vertical, 2)
        }
        .tourTarget("ptColours")
    }

    /// The table with its trend arrows — (1.3.54) always, for a property with a simple trend: across the top, the trend
    /// across a period; down the side, the trend in a group. (tools-11) The side's gutter kept slim, for the squares' room.
    @ViewBuilder
    private func trendWrap<G: View>(@ViewBuilder _ grid: () -> G) -> some View {
        if let t = colouring.trend {
            VStack(spacing: 6) {
                HStack(spacing: 6) {
                    if t.across < 0 { Image(systemName: "arrow.left").font(.system(size: 13, weight: .bold)) }
                    Rectangle().frame(height: 2)
                    Text(t.acrossWords).font(.sCaption.weight(.semibold)).fixedSize()
                    Rectangle().frame(height: 2)
                    if t.across > 0 { Image(systemName: "arrow.right").font(.system(size: 13, weight: .bold)) }
                }
                .foregroundStyle(Color.yellow)
                .padding(.leading, 24)
                grid()
                    .padding(.leading, 24)
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

    /// (1.3.54) A kind's chip, in glass of its colour: filled when it lights its elements.
    private func legendChip(_ c: PTCategory) -> some View {
        let on = category == c.key
        return Button {
            withAnimation(Motion.snappy) { category = on ? nil : c.key }
        } label: {
            HStack(spacing: 6) {
                Circle().fill(on ? Color.white : c.color).frame(width: 9, height: 9)
                Text(c.label).font(.sCaption.weight(.medium))
            }
        }
        .glassChip(c.color, on: on)
        .help(on ? "Show every kind" : "Light the \(c.label.lowercased()) elements")
    }
}

/// The table's pin, opened: the whole table, small, with a search that lights the matches. (1.3.51) The element under
/// the pointer — (tools-34) or, while a search is typed, the first match, the one Return opens — shows in the table's
/// empty top, and nowhere else (its facts used to be said twice, in a line beside the search as well); a press opens the
/// whole table on its page.
struct PeriodicTableCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var query = ""
    @State private var over: PTElement?

    private var typed: Bool { !query.trimmingCharacters(in: .whitespaces).isEmpty }
    private var found: [PTElement] { PeriodicTable.find(query) }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                TextField("Find an element", text: $query)
                    .textFieldStyle(.roundedBorder)
                    .font(.sCallout)
                    .frame(maxWidth: 220)
                    .onSubmit { if let e = found.first { openFull(["element": String(e.number)]) } }
                if typed && found.isEmpty {
                    Text("No element")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
                Spacer(minLength: 0)
            }
            PTGrid(shown: over ?? (typed ? found.first : nil), matches: typed ? Set(found.map(\.number)) : nil, category: nil, compact: true,
                   pick: { e, _ in openFull(["element": String(e.number)]) }, hover: { e in over = e })
        }
        // (the screenshot suite: -SimplPinElement 25 shows that element's card)
        .onAppear {
            let n = UserDefaults.standard.integer(forKey: "SimplPinElement")
            if n > 0, over == nil { over = PeriodicTable.byNumber(n) }
        }
    }
}

/// (tools-24) Whether the Lewis maker or the molecule builder is laid out narrow (the drawing across the top): their
/// drawing cards then carry the controls a student reaches for first — the preset, Quick or Draw — in their own heading,
/// in sight however far the page is scrolled down.
private struct PTNarrowKey: EnvironmentKey {
    static let defaultValue = false
}

extension EnvironmentValues {
    var ptNarrow: Bool {
        get { self[PTNarrowKey.self] }
        set { self[PTNarrowKey.self] = newValue }
    }
}

/// Reads whether the builder is laid out narrow, for the part of it built inside (`PTWorkLayout` sets it).
struct PTNarrowReader<Content: View>: View {
    @Environment(\.ptNarrow) private var narrow
    @ViewBuilder var content: (Bool) -> Content

    var body: some View { content(narrow) }
}

/// (1.3.52) The Lewis maker's and the molecule builder's layout (and the biology tools'): on a wide window, the controls,
/// the drawing and the facts side by side; on a narrower one, the drawing across the top and the controls and facts side
/// by side under it — (tools-24) for the Lewis maker and the molecule builder (`shortDrawing`) the drawing about half the
/// height there, their controls in its card's top row, so what is under it is in sight too; for a biology tool's
/// step-through as tall as the window shows, up to 640, its parts never squeezed into each other.
struct PTWorkLayout<Left: View, Centre: View, Right: View>: View {
    var shortDrawing = false
    @ViewBuilder var left: Left
    @ViewBuilder var centre: Centre
    @ViewBuilder var right: Right

    var body: some View {
        GeometryReader { g in
            if g.size.width >= 1100 {
                // (1.3.58) each side column's content held to the column's width, less room for its scroller, so
                // nothing runs under the centre or under the scroll bar
                HStack(alignment: .top, spacing: 18) {
                    ScrollView {
                        left.frame(width: 284, alignment: .topLeading).padding(.bottom, 20)
                    }
                    .frame(width: 300, alignment: .leading)
                    centre
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                    ScrollView {
                        right.frame(width: 300, alignment: .topLeading).padding(.bottom, 20)
                    }
                    .frame(width: 316, alignment: .leading)
                }
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 18) {
                        // (1.3.78) the drawing as tall as the window shows, up to 640: at three quarters of a short
                        // window's height a biology tool's step-through was squeezed until its parts ran into each other,
                        // and its controls were pushed out over the columns under it
                        centre
                            .frame(height: shortDrawing ? max(360, min(560, g.size.height * 0.58)) : max(460, min(640, g.size.height)))
                        HStack(alignment: .top, spacing: 18) {
                            left.frame(maxWidth: .infinity, alignment: .topLeading)
                            right.frame(maxWidth: .infinity, alignment: .topLeading)
                        }
                    }
                    .padding(.bottom, 24)
                }
                .environment(\.ptNarrow, true)
            }
        }
        .padding(.horizontal, 40)
        .padding(.bottom, 24)
        .frame(maxWidth: 1480 + 80)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

/// (1.3.54) The Periodic Table's search, kept here so the tool's header can hold its field (left of the pin).
final class PTSearch: ObservableObject {
    static let shared = PTSearch()
    @Published var query = ""
    /// Raised by Return in the field: the table opens the first element it finds.
    @Published var submitted = 0
    /// (1.3.64) An element picked from the field's results: the table opens it.
    @Published private(set) var picked: Int?
    @Published private(set) var pickedCount = 0

    func pick(_ number: Int) {
        picked = number
        pickedCount += 1
    }
}

/// (1.3.62) Which page of the full Periodic Table is shown — chosen in the tool's header — and the tour asked for there.
final class PTNav: ObservableObject {
    static let shared = PTNav()
    @Published var page: PeriodicTableTool.Page = .table
    @Published var tourAsked = 0
    /// (tools-29) An element's own page is open: the heading's one Back then says "‹ Table" and closes it.
    @Published var elementOpen = false
    /// Raised by that Back: the table closes the element.
    @Published private(set) var closeAsked = 0

    func closeElement() { closeAsked += 1 }
}

/// (tools-4) The Periodic Table's second row, under its heading: its pages — Table, Lewis structures, Molecule builder —
/// in words, as a segmented control (they used to draw in to look-alike symbols in the heading where it ran short of
/// room), and beside them the search — (1.3.54) a glass button that opens into a field — and Tour, which draws in to its
/// symbol while the field is open.
struct PTPageBar: View {
    @ObservedObject private var nav = PTNav.shared
    @ObservedObject private var search = PTSearch.shared
    @State private var searching = false
    @FocusState private var focused: Bool
    /// (1.3.64) the result the arrow keys are on
    @State private var lit = 0

    private var results: [PTElement] { Array(PeriodicTable.find(search.query).prefix(7)) }

    var body: some View {
        HStack(spacing: 10) {
            Picker("Page", selection: Binding(get: { nav.page }, set: { p in withAnimation(Motion.gentle) { nav.page = p } })) {
                ForEach(PeriodicTableTool.Page.allCases) { p in
                    Text(p.label).tag(p)
                }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .controlSize(.large)
            .fixedSize()
            .help("The periodic table, the Lewis structure maker, or the 3D molecule builder")
            .tourTarget("ptPages")
            Spacer(minLength: 8)
            searchControl.tourTarget("ptSearch")
            Button { nav.tourAsked += 1 } label: {
                Label("Tour", systemImage: "questionmark.circle").labelStyle(SquishLabel(squished: searching))
            }
            .glassButton()
            .controlSize(.large)
            .help("A short tour of the table and its builders")
        }
        .animation(.spring(response: 0.38, dampingFraction: 0.86), value: searching)
        .onChange(of: focused) { _, f in
            if !f && search.query.isEmpty { searching = false }
        }
        .onAppear {
            // (the screenshot suite: -SimplPTSearch ne, the search open with its results)
            if let q = UserDefaults.standard.string(forKey: "SimplPTSearch"), !q.isEmpty {
                searching = true
                search.query = q
            }
        }
    }

    @ViewBuilder
    private var searchControl: some View {
        if searching {
            HStack(spacing: 8) {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField("Symbol, name or number", text: $search.query)
                    .textFieldStyle(.plain)
                    .font(.sBody)
                    .focused($focused)
                    .frame(minWidth: 120, idealWidth: 210, maxWidth: 210)
                    .onSubmit {
                        let r = results
                        if r.indices.contains(lit) { choose(r[lit]) } else { search.submitted += 1 }
                    }
                    .onExitCommand { close() }
                    .onKeyPress(.downArrow) { lit = min(lit + 1, max(results.count - 1, 0)); return .handled }
                    .onKeyPress(.upArrow) { lit = max(lit - 1, 0); return .handled }
                    .onChange(of: search.query) { _, _ in lit = 0 }
                Button(action: close) { Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary) }
                    .buttonStyle(.plain)
                    .help("Close the search")
            }
            .padding(.horizontal, 14)
            .frame(height: 34)
            .glassCard(radius: 17, interactive: true)
            .overlay(alignment: .topTrailing) {
                if !search.query.trimmingCharacters(in: .whitespaces).isEmpty {
                    resultList
                        .fixedSize()
                        .offset(y: 44)
                        .transition(.opacity.combined(with: .scale(scale: 0.96, anchor: .top)))
                }
            }
            .transition(.scale(scale: 0.6, anchor: .trailing).combined(with: .opacity))
            .help("Light the elements that match; Return opens the first")
        } else {
            Button {
                searching = true
                DispatchQueue.main.async { focused = true }
            } label: {
                Image(systemName: "magnifyingglass").frame(width: 18)
            }
            .glassButton()
            .controlSize(.large)
            .keyboardShortcut("f", modifiers: .command)
            .help("Search the elements (⌘F)")
            .transition(.scale(scale: 0.6, anchor: .trailing).combined(with: .opacity))
        }
    }

    /// (1.3.64) What the search finds, under the field: each element's tile, name, number and kind; the one the arrow
    /// keys are on lit; a press (or Return) opens it.
    private var resultList: some View {
        let r = results
        return VStack(alignment: .leading, spacing: 2) {
            if r.isEmpty {
                Text("No element matches “\(search.query.trimmingCharacters(in: .whitespaces))”")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .padding(12)
            }
            ForEach(Array(r.enumerated()), id: \.element.number) { k, e in
                Button { choose(e) } label: {
                    HStack(spacing: 10) {
                        Text(e.symbol)
                            .font(.system(size: 14, weight: .bold))
                            .foregroundStyle(.white)
                            .frame(width: 32, height: 32)
                            .background(e.kind.color.opacity(0.85), in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                        VStack(alignment: .leading, spacing: 1) {
                            Text(e.name).font(.sBody.weight(.semibold))
                            Text("\(e.number) · \(e.kind.label)").font(.sCaption).foregroundStyle(.secondary)
                        }
                        Spacer(minLength: 8)
                        Image(systemName: "chevron.right").font(.system(size: 11, weight: .semibold)).foregroundStyle(.tertiary)
                    }
                    .padding(.horizontal, 8)
                    .padding(.vertical, 6)
                    .frame(width: 284, alignment: .leading)
                    .background(k == lit ? Theme.accent.opacity(0.16) : Color.clear, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .onHover { inside in if inside { lit = k } }
            }
        }
        .padding(6)
        .glassCard(radius: 16)
        .shadow(color: .black.opacity(0.2), radius: 20, y: 8)
        .animation(Motion.snappy, value: lit)
    }

    private func choose(_ e: PTElement) {
        search.pick(e.number)
        close()
    }

    private func close() {
        search.query = ""
        focused = false
        searching = false
    }
}

/// (tools-14) A row of chips that scrolls sideways when it is wider than its place: an edge it runs past fades out, with a
/// chevron there that scrolls the row on (or back) — so a row cut at the window's edge never looks as if it simply ends
/// mid-word.
struct ChipScroller<Content: View>: View {
    @ViewBuilder var content: Content
    @State private var room: CGFloat = 0
    @State private var wide: CGFloat = 0
    /// The row's left edge in the scroller (0 at the start, negative once scrolled on).
    @State private var offset: CGFloat = 0
    @State private var space = UUID()
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var overflows: Bool { wide > room + 1 }
    private var atStart: Bool { offset > -2 }
    private var atEnd: Bool { offset <= room - wide + 2 }

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 0) {
                    Color.clear.frame(width: 0, height: 1).id("start")
                    content
                    Color.clear.frame(width: 0, height: 1).id("end")
                }
                .background {
                    GeometryReader { g in
                        let f = g.frame(in: .named(space))
                        Color.clear
                            .onAppear { wide = f.width; offset = f.minX }
                            .onChange(of: f) { _, n in
                                wide = n.width
                                offset = n.minX
                            }
                    }
                }
            }
            .coordinateSpace(name: space)
            .mask {
                // (each edge's fade always there, clear or solid: the change is a colour's, not a frame's)
                HStack(spacing: 0) {
                    LinearGradient(colors: [overflows && !atStart ? Color.clear : Color.black, Color.black], startPoint: .leading, endPoint: .trailing)
                        .frame(width: 40)
                    Rectangle()
                    LinearGradient(colors: [Color.black, overflows && !atEnd ? Color.clear : Color.black], startPoint: .leading, endPoint: .trailing)
                        .frame(width: 40)
                }
            }
            .overlay(alignment: .leading) {
                if overflows && !atStart { chevron("chevron.left", help: "Scroll back") { scroll(proxy, to: "start", anchor: .leading) } }
            }
            .overlay(alignment: .trailing) {
                if overflows && !atEnd { chevron("chevron.right", help: "Scroll for more") { scroll(proxy, to: "end", anchor: .trailing) } }
            }
            .animation(Motion.hover, value: atStart)
            .animation(Motion.hover, value: atEnd)
        }
        .background {
            GeometryReader { g in
                Color.clear
                    .onAppear { room = g.size.width }
                    .onChange(of: g.size.width) { _, w in room = w }
            }
        }
    }

    private func chevron(_ symbol: String, help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 11, weight: .bold))
                .frame(width: 26, height: 26)
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .glass(Circle(), interactive: true)
        .help(help)
        .transition(.opacity)
    }

    private func scroll(_ proxy: ScrollViewProxy, to id: String, anchor: UnitPoint) {
        if reduceMotion { proxy.scrollTo(id, anchor: anchor) } else { withAnimation(Motion.gentle) { proxy.scrollTo(id, anchor: anchor) } }
    }
}

/// A label that draws in to its symbol alone when room is wanted.
struct SquishLabel: LabelStyle {
    let squished: Bool

    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 6) {
            configuration.icon
            if !squished { configuration.title.transition(.opacity.combined(with: .scale(scale: 0.8, anchor: .leading))) }
        }
    }
}

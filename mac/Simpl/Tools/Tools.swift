import AppKit
import SwiftUI
import UniformTypeIdentifiers

// Tools (1.2): the things Simpl does on its own, as the web's Tools page has them — a citation generator, a focus timer,
// a calculator, a graphing calculator, the periodic table, Grade needed, a file converter, Merge & split PDFs, a PDF
// annotator, Image to text and flashcards — drawn natively, with a unit converter of the Mac's own beside them,
// (1.3.78) the biology toolkit's ten under a heading of their own (Biology/BioTools.swift), (1.3.80) the periodic
// table split into the chemistry tools' four, under theirs (Chemistry/ChemTools.swift), (tools) the chemistry
// calculators — the Equation Balancer, Molar Mass & Stoichiometry, Gas Laws, Solutions & pH — beside them, (tools) the
// physics tools under a "Physics" heading between them (Physics/PhysTools.swift), (tools) the study and maths tools
// after them — the Word Counter under Study (WordCounter.swift), Statistics and the Matrix Calculator under Math & science
// (Statistics.swift, MatrixCalculator.swift); their arithmetic in Model/, checked by the Models job — and (tools)
// Compress Files among the file tools (CompressFiles.swift) and the Study Planner and Verb Conjugations among the study
// tools (Planner/StudyPlanner.swift, Verbs/VerbConjugations.swift). One row in the sidebar opens a page of tiles,
// (tools-32) in sections by what they are for: Study, Math & science, Files, Chemistry, Physics and Biology;
// (1.3.79) a tile opens its tool in a popup over the window — the Tools page staying behind it, and the whole window
// usable round it (Shell/FloatingPopup.swift) — as its pin, a hand-off from another tool and a launch do. Any tool can be
// pinned: it then sits in the toolbar on every screen and opens a compact version of itself in a popover
// (PinnedTools.swift). Everything a tool keeps stays on this Mac; nothing is sent to Canvas.

/// A tool: its key (the web's own, so a pin means the same thing on both), its name, the line under it, its symbol and
/// its colour (used for its tile and its pin, nothing else).
enum ToolKind: String, CaseIterable, Identifiable, Hashable {
    case cite, pomo, calc, graph, ptable, need, conv, units, pdfx, mark, ocr, fc
    // (1.3.80) the chemistry tools beside the Periodic Table (`ptable`, the key its pin always had): their names, lines,
    // symbols and colours, and the Periodic Table's, are ChemTools.info's
    case lewis, molecule, atoms
    // (tools) the chemistry calculators after them — the Equation Balancer and the rest, ChemTools.info's too
    case balance, moles, gas, solution
    // (tools) the physics tools: their names, lines, symbols and colours are PhysTools.info's
    case motion, vectors, circuit
    // (1.3.78) the biology toolkit: its names, lines, symbols and colours are BioTools.info's
    case cell, dna, gene, biomol, cross, divide, popgen, biolab, enzyme, energy
    // (tools) the study and maths tools of the Mac's own, each with its name, line, symbol and colour below as the
    // first tools have them: the Word Counter (WordCounter.swift), Statistics (Statistics.swift), the Matrix Calculator
    // (MatrixCalculator.swift)
    case words, stats, matrix
    // (tools) Compress Files, in the Files group (CompressFiles.swift); the Study Planner and Verb Conjugations, in Study
    // (Planner/StudyPlanner.swift, Verbs/VerbConjugations.swift)
    case shrink, plan, verbs

    var id: String { rawValue }

    var name: String {
        switch self {
        case .cite: return "Citation generator"
        case .pomo: return "Focus timer"
        case .calc: return "Calculator"
        case .graph: return "Graphing calculator"
        case .motion, .vectors, .circuit: return PhysTools.info(self).name
        case .ptable, .lewis, .molecule, .atoms, .balance, .moles, .gas, .solution: return ChemTools.info(self).name
        case .need: return "Grade needed"
        case .conv: return "File converter"
        case .units: return "Unit converter"
        case .pdfx: return "Merge & split PDFs"
        case .mark: return "PDF annotator"
        case .ocr: return "Image to text"
        case .fc: return "Flashcards"
        case .words: return "Word Counter"
        case .stats: return "Statistics"
        case .matrix: return "Matrix Calculator"
        case .shrink: return "Compress Files"
        case .plan: return "Study Planner"
        case .verbs: return "Verb Conjugations"
        default: return BioTools.info(self).name
        }
    }

    /// A shorter name, kept for what still wants one. (tools-21) A pin's popover and its floating panel say `name`, the
    /// tile's own, so a panel reads as the tool that was pinned.
    var shortName: String {
        switch self {
        case .cite: return "Cite"
        case .pomo: return "Focus"
        case .calc: return "Calculator"
        case .graph: return "Graphing"
        case .motion, .vectors, .circuit: return PhysTools.info(self).shortName
        case .ptable, .lewis, .molecule, .atoms, .balance, .moles, .gas, .solution: return ChemTools.info(self).shortName
        case .need: return "Grade needed"
        case .conv: return "Convert"
        case .units: return "Units"
        case .pdfx: return "Merge & split"
        case .mark: return "Mark up"
        case .ocr: return "Image to text"
        case .fc: return "Flashcards"
        case .words: return "Words"
        case .stats: return "Stats"
        case .matrix: return "Matrix"
        case .shrink: return "Compress"
        case .plan: return "Plan"
        case .verbs: return "Verbs"
        default: return BioTools.info(self).shortName
        }
    }

    /// The line under its name: (tools-32) one short line, so a tile reads at a glance.
    var note: String {
        switch self {
        case .cite: return "Cite in MLA, APA or Chicago."
        case .pomo: return "Focus, then take a break."
        case .calc: return "GeoGebra: graphs, CAS, 3D and more."
        case .graph: return "Desmos, right here."
        case .motion, .vectors, .circuit: return PhysTools.info(self).note
        case .ptable, .lewis, .molecule, .atoms, .balance, .moles, .gas, .solution: return ChemTools.info(self).note
        case .need: return "What you need on the final."
        case .conv: return "Convert Word, PDF and images."
        case .units: return "Lengths, weights, temperatures."
        case .pdfx: return "Join PDFs, or take pages out."
        case .mark: return "Highlight and add notes."
        case .ocr: return "Copy the words in a picture."
        case .fc: return "Flip, learn, test and match."
        case .words: return "Words, characters, reading time."
        case .stats: return "Mean, SD, regression, z and t."
        case .matrix: return "Determinant, inverse, solve, RREF."
        case .shrink: return "Shrink images and PDFs to fit."
        case .plan: return "Study blocks from your due dates."
        case .verbs: return "Conjugate verbs in 5 languages."
        default: return BioTools.info(self).note
        }
    }

    var symbol: String {
        switch self {
        case .cite: return "quote.opening"
        case .pomo: return "timer"
        case .calc: return "plus.forwardslash.minus"
        case .graph: return "chart.xyaxis.line"
        case .motion, .vectors, .circuit: return PhysTools.info(self).symbol
        case .ptable, .lewis, .molecule, .atoms, .balance, .moles, .gas, .solution: return ChemTools.info(self).symbol
        case .need: return "percent"
        case .conv: return "arrow.triangle.2.circlepath"
        case .units: return "ruler"
        case .pdfx: return "doc.on.doc"
        case .mark: return "highlighter"
        case .ocr: return "text.viewfinder"
        case .fc: return "rectangle.on.rectangle.angled"
        case .words: return "text.word.spacing"
        case .stats: return "chart.bar.xaxis"
        case .matrix: return "square.grid.3x3"
        case .shrink: return "arrow.down.right.and.arrow.up.left"
        case .plan: return "calendar.badge.clock"
        case .verbs: return "character.book.closed"
        default: return BioTools.symbol(self)
        }
    }

    var hex: String {
        switch self {
        case .cite: return "#30b0c7"
        case .pomo: return "#ff9500"
        case .calc: return "#ff9f0a"
        case .graph: return "#5856d6"
        case .motion, .vectors, .circuit: return PhysTools.info(self).hex
        case .ptable, .lewis, .molecule, .atoms, .balance, .moles, .gas, .solution: return ChemTools.info(self).hex
        case .need: return "#ff375f"
        case .conv: return "#34c759"
        case .units: return "#64d2ff"
        case .pdfx: return "#bf5af2"
        case .mark: return "#e5a500"
        case .ocr: return "#00b3a4"
        case .fc: return "#0a84ff"
        case .words: return "#8e8e93"
        case .stats: return "#ff2d55"
        case .matrix: return "#5e5ce6"
        case .shrink: return "#ff9500"
        case .plan: return "#34c759"
        case .verbs: return "#30b0c7"
        default: return BioTools.info(self).hex
        }
    }

    var color: Color { Color(hex: hex) }

    /// The width of its pinned popover. (tools-34) The Periodic Table's wider, for squares big enough to point at.
    var popoverWidth: CGFloat {
        switch self {
        case .motion, .vectors, .circuit: return PhysTools.popoverWidth(self)
        case .ptable, .lewis, .molecule, .atoms, .balance, .moles, .gas, .solution: return ChemTools.popoverWidth(self)
        case .calc: return 420
        case .pomo: return 300 // (1.3.127) the web's mini timer: a strip of minutes and Start, or the count and its buttons
        case .cite: return 400
        case .graph: return 360
        case .fc: return 340
        case .matrix: return 380 // (tools) a 3 × 3 and its inverse side by side
        case .cell, .dna, .gene, .biomol, .cross, .divide, .popgen, .biolab, .enzyme, .energy: return BioTools.popoverWidth(self)
        default: return 330
        }
    }

    /// The files a tool takes, for a drop on its pin's popover (nil: it takes none).
    var accepts: [UTType]? {
        switch self {
        case .conv: return [.item]
        case .pdfx, .mark: return [.pdf]
        case .ocr: return [.image, .pdf]
        case .shrink: return [.image, .pdf] // (tools: pictures and PDFs made smaller)
        default: return nil
        }
    }
}

/// What the tools share across the window: which are pinned to the toolbar (kept in the app's defaults, the keys joined
/// by commas), the tool open in its popup over the window, and files handed to a tool from its pin (dropped or chosen
/// there) for it to take when it opens.
@MainActor
final class ToolsCenter: ObservableObject {
    static let shared = ToolsCenter()
    static let pinsKey = "SimplPinnedTools"

    @Published private(set) var pinned: [ToolKind] = []
    /// (1.3.79) The tool open whole — from its tile, its pin, a hand-off from another tool — in its popup over the window
    /// (nil: none). One at a time: another opened takes its place in the same popup.
    @Published private(set) var popup: ToolKind?
    /// (1.3.83) How many times the tool open has been made afresh in its popup — something begun in its pin's panel
    /// handed to it while it was open already (`popUp`'s `afresh`) — so it is drawn anew and takes it.
    @Published private(set) var popupMade = 0
    /// The window its popup is over.
    private weak var popupEngine: Engine?
    /// The tool's popup among its window's popups.
    static let popupKey = "tool"
    /// Files handed over from a pin's popover, for `handoffTool` to take.
    @Published private(set) var handoff: [URL] = []
    private(set) var handoffTool: ToolKind?
    /// Set by a pin's popover for the tool it opens (a set to study, a link to cite): read once by the tool.
    var handoffInfo: [String: String] = [:]
    /// (tools-30) Tools whose heading leaves out its line under the name for now: Flashcards, inside a set (its own
    /// bar names the set and the way back, and the question wants the room).
    @Published private(set) var quietHeadings: Set<ToolKind> = []

    init() {
        let raw = UserDefaults.standard.string(forKey: ToolsCenter.pinsKey) ?? ""
        var seen = Set<ToolKind>()
        pinned = raw.split(separator: ",").compactMap { ToolKind(rawValue: String($0).trimmingCharacters(in: .whitespaces)) }.filter { seen.insert($0).inserted }
        // (the screenshot suite: -SimplTool calc opens the Calculator in its popup, over the Tools page with -SimplPlace
        // tools; -SimplToolFile <path> hands the tool opened a file, as a drop on its pin does — the annotator's page,
        // Split's pages. 1.3.80: the Periodic Table's old page and view flags open the chemistry tools they are now —
        // -SimplTool ptable -SimplPTPage lewis is Lewis Structures: ChemLaunch)
        if let t = LaunchOpen.take("", key: "SimplTool"), let k = ToolKind(rawValue: ChemTools.launchKey(t)) { launchTool = k }
        if let k = launchTool, let path = LaunchOpen.take("", key: "SimplToolFile"), !path.isEmpty {
            launchFile = (k, URL(fileURLWithPath: path))
        }
    }

    /// The screenshot suite's tool (-SimplTool), opened in its popup once the main window is up.
    private var launchTool: ToolKind?

    /// The screenshot suite's file for the tool its launch opened: handed to that tool each time it is drawn afresh (the
    /// window's screens are made again as the app's answers come in, and a file taken once was gone by then).
    private var launchFile: (kind: ToolKind, url: URL)?

    func isPinned(_ k: ToolKind) -> Bool { pinned.contains(k) }

    func pin(_ k: ToolKind) {
        guard !pinned.contains(k) else { return }
        withAnimation(Motion.gentle) { pinned.append(k) }
        save()
        if k == .graph { MacTour.shared.did(.pinned) } // (the tour's "Pin the graphing calculator")
    }

    func unpin(_ k: ToolKind) {
        withAnimation(Motion.gentle) { pinned.removeAll { $0 == k } }
        save()
    }

    func togglePin(_ k: ToolKind) {
        if isPinned(k) { unpin(k) } else { pin(k) }
    }

    /// A pin moved along the toolbar (its context menu's Move Left and Move Right).
    func move(_ k: ToolKind, by step: Int) {
        guard let i = pinned.firstIndex(of: k) else { return }
        let j = min(max(i + step, 0), pinned.count - 1)
        guard i != j else { return }
        withAnimation(Motion.gentle) { pinned.swapAt(i, j) }
        save()
    }

    /// The pins the toolbar shows: the pinned tools, and the focus timer while a session is going even when it is not
    /// pinned (it borrows a pin for as long as it runs, as on the web).
    func toolbarPins(timerActive: Bool) -> [ToolKind] {
        if timerActive && !pinned.contains(.pomo) { return pinned + [.pomo] }
        return pinned
    }

    /// (tools-6) How many tools a toolbar holds before its room has been measured (ToolbarFit): three at most, past that
    /// two and the Tools pull-down. (1.3.79) Once measured, every pin that fits.
    static let toolbarRoom = 3

    /// (1.3.79) The pins split between a bar and its Tools pull-down: `fit` of them shown, the rest — from the end — in
    /// the pull-down, a running timer the last to go (it is live on the bar: its minutes and seconds). tools-6 showed three
    /// at most whatever the room; how many fit is measured now (ToolbarFit for a toolbar, PopupPins for a popup's bar),
    /// so a screen's own buttons still never go into the toolbar's » menu for a pin.
    func toolbarSplit(timerActive: Bool, fit: Int) -> (shown: [ToolKind], more: [ToolKind]) {
        let all = toolbarPins(timerActive: timerActive)
        let k = min(max(fit, 0), all.count)
        guard k < all.count else { return (all, []) }
        let others = all.filter { !(timerActive && $0 == .pomo) }
        var keep = Set(others.prefix(timerActive && k > 0 ? k - 1 : k))
        if timerActive && k > 0 { keep.insert(.pomo) }
        return (all.filter { keep.contains($0) }, all.filter { !keep.contains($0) })
    }

    /// (tools-30) A tool's heading with or without its line under the name.
    func quietHeading(_ k: ToolKind, _ quiet: Bool) {
        guard quietHeadings.contains(k) != quiet else { return }
        if quiet { quietHeadings.insert(k) } else { quietHeadings.remove(k) }
    }

    /// A tool opened whole — (1.3.79) from its tile on the Tools page, its pin, a hand-off from another tool, a launch —
    /// with any files or details for it: in its popup over the window it was asked for in (`engine`; else the one its
    /// popup is already over, else the main window's), the screen under it staying as it was, and usable. A tool already
    /// open is switched in place, in the same popup; over another window, that popup closes first. (1.3.80) The popup
    /// spans the window, over the sidebar, at the size the student last gave this tool by the popup's edges, if any
    /// (kept under `tool.<key>`). (1.3.83) `afresh`: the tool, if it is the one open already, made again in its popup —
    /// a cross-fade — to take what it is handed as it is made (what was begun in its pin's panel), where it would have
    /// stayed as it was and left it untaken.
    func popUp(_ k: ToolKind, engine: Engine? = nil, files: [URL] = [], info: [String: String] = [:], afresh: Bool = false) {
        handoff = files
        handoffTool = files.isEmpty ? nil : k
        handoffInfo = info
        guard let host = engine ?? popupEngine ?? AppModel.shared.engine else { return }
        if let was = popupEngine, was !== host { was.popups.close(Self.popupKey) }
        withAnimation(Motion.gentle) {
            if afresh && popup == k { popupMade += 1 }
            popup = k
        }
        popupEngine = host
        host.popups.present(Self.popupKey, title: k.name, layout: .large, sizeKey: "tool.\(k.rawValue)",
                            onClose: { [weak self] in self?.popupClosed() }) {
            ToolPopup()
        }
    }

    /// The tool's popup closed (its ×, Escape, ⌘W, or its window closing).
    private func popupClosed() {
        popup = nil
        popupEngine = nil
    }

    /// The tool's popup closed from within it (its ×).
    func closePopup() {
        popupEngine?.popups.close(Self.popupKey)
    }

    /// (the screenshot suite) The tool its launch asked for (-SimplTool), opened in its popup over the main window.
    func openLaunchTool(_ engine: Engine) {
        guard let k = launchTool else { return }
        launchTool = nil
        popUp(k, engine: engine)
    }

    /// The files handed to a tool, taken once.
    func takeFiles(for k: ToolKind) -> [URL] {
        if handoff.isEmpty, let f = launchFile, f.kind == k { return [f.url] }
        guard handoffTool == k, !handoff.isEmpty else { return [] }
        let files = handoff
        handoff = []
        handoffTool = nil
        return files
    }

    /// A detail handed to a tool, taken once.
    func takeInfo(_ key: String) -> String? {
        handoffInfo.removeValue(forKey: key)
    }

    private func save() {
        UserDefaults.standard.set(pinned.map(\.rawValue).joined(separator: ","), forKey: ToolsCenter.pinsKey)
    }
}

// MARK: - The Tools place

/// The Tools place: the page of tiles. (1.3.79) A tile opens its tool in a popup over the window (ToolPopup), as the
/// web's popup opens over its page — the page staying behind it, never put away for it. (tools-5) The pin is the tool's
/// heading's alone: the window's toolbar carries no second one.
struct ToolsView: View {
    var body: some View {
        ToolsGrid()
            .navigationTitle("Tools")
    }
}

/// (tools-32) The Tools page's sections, in order — what each tool is for, so a tool is found by its kind rather than by
/// reading every tile; the study tools first. (1.3.80) The Periodic Table moved out of Math & science into Chemistry,
/// with the three tools that were its pages. (tools) Physics, between Chemistry and Biology.
enum ToolGroup: CaseIterable {
    case study, science, files, chemistry, physics, biology

    var title: String {
        switch self {
        case .study: return "Study"
        case .science: return "Math & science"
        case .files: return "Files"
        case .chemistry: return "Chemistry"
        case .physics: return "Physics"
        case .biology: return "Biology"
        }
    }

    var kinds: [ToolKind] {
        switch self {
        case .study: return [.fc, .pomo, .cite, .need, .words, .verbs, .plan]
        case .science: return [.calc, .graph, .units, .stats, .matrix]
        case .files: return [.conv, .pdfx, .mark, .ocr, .shrink]
        case .chemistry: return ChemTools.kinds
        case .physics: return PhysTools.kinds
        case .biology: return BioTools.kinds
        }
    }
}

/// The page of tiles: every tool, a press opening it, its pin at its corner — (tools-32) in sections by what they are
/// for, (1.3.80) the chemistry tools', (tools) the physics tools' and (1.3.78) the biology toolkit's last, each under a
/// heading of its own.
struct ToolsGrid: View {
    var body: some View {
        ScrollViewReader { proxy in
            Page {
                ScreenHeading(title: "Tools", sub: "Pin a tool to keep it in the toolbar: its button there opens a quick version of it.")
                ForEach([ToolGroup.study, .science, .files], id: \.self) { g in
                    ToolGroupSection(title: g.title, kinds: g.kinds)
                }
                ChemToolsSection()
                PhysToolsSection()
                BioToolsSection()
            }
            .task {
                // (the screenshot suite: -SimplToolsScroll chemistry, physics or biology, the page scrolled to that section)
                guard let to = UserDefaults.standard.string(forKey: "SimplToolsScroll"), ["chemistry", "physics", "biology"].contains(to) else { return }
                try? await Task.sleep(nanoseconds: 400_000_000)
                proxy.scrollTo(to, anchor: .top)
            }
        }
    }
}

/// (tools-32) A section of the Tools page: its heading and its tiles.
struct ToolGroupSection: View {
    let title: String
    let kinds: [ToolKind]

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title)
                .font(.sTitle3)
                .accessibilityAddTraits(.isHeader)
            LazyVGrid(columns: ToolTile.columns, spacing: 14) {
                ForEach(kinds) { k in ToolTile(kind: k) }
            }
        }
    }
}

/// A tool's tile: its symbol on a wash of its colour, its name and line, and its pin; the focus timer's says how long is
/// left while a session goes.
struct ToolTile: View {
    let kind: ToolKind
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var focus = FocusTimer.shared
    /// (1.3.79) The window's engine, for its tool's popup to open over this window.
    @Environment(\.engineLink) private var link

    /// (tools-32) Three across from about 900 points of page, two below.
    static let columns = [GridItem(.adaptive(minimum: 280), spacing: 14)]

    var body: some View {
        Button {
            tools.popUp(kind, engine: link.engine)
        } label: {
            HStack(alignment: .top, spacing: 12) {
                IconTile(symbol: kind.symbol, color: kind.color, size: 44)
                VStack(alignment: .leading, spacing: 3) {
                    Text(kind.name)
                        .font(.sHeadline)
                        .foregroundStyle(.primary)
                    Text(kind.note)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                    if kind == .pomo && focus.active { FocusTileLine() }
                }
                Spacer(minLength: 36)
            }
            .padding(16)
            .frame(maxWidth: .infinity, minHeight: 84, alignment: .topLeading)
        }
        .buttonStyle(CardButtonStyle(radius: 20))
        .overlay(alignment: .topTrailing) { pinButton.padding(10) }
        .contextMenu {
            Button("Open") { tools.popUp(kind, engine: link.engine) }
            Button(tools.isPinned(kind) ? "Unpin from Toolbar" : "Pin to Toolbar") { tools.togglePin(kind) }
        }
        .accessibilityLabel("\(kind.name). \(kind.note)")
    }

    private var pinButton: some View {
        let on = tools.isPinned(kind)
        return Button {
            tools.togglePin(kind)
        } label: {
            Image(systemName: on ? "pin.fill" : "pin")
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(on ? kind.color : Color.secondary)
                .frame(width: 30, height: 30)
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .glass(Circle(), tint: on ? kind.color.opacity(0.25) : nil, interactive: true)
        .help(on ? "Pinned to the toolbar. Click to unpin \(kind.name)." : "Pin \(kind.name) to the toolbar")
        .accessibilityLabel(on ? "Unpin \(kind.name)" : "Pin \(kind.name)")
        .tourSpot(kind == .graph && !on ? .toolPin : nil) // (1.2.16: the tour lights the graphing calculator's)
    }
}

/// The focus timer's tile while a session goes: the phase and the time left, (tools-25) read from the one second hand
/// every timer label shares — drawn again each second on its own, not with the whole page of tiles.
private struct FocusTileLine: View {
    @ObservedObject private var focus = FocusTimer.shared
    @ObservedObject private var clock = FocusClock.shared

    var body: some View {
        Text("\(focus.rec.phase.name) · \(FocusTimer.clock(focus.remaining(at: clock.now)))\(focus.paused ? " · Paused" : "")")
            .font(.sCallout.weight(.semibold).monospacedDigit())
            .foregroundStyle(focus.rec.phase.color)
    }
}

/// A tool open whole, in its popup over the window (1.3.79): its heading (its tile, its name and line, its pages, its pin
/// and its ×) — dragged, the popup moves — and the tool under it, filling the popup.
struct ToolScreen: View {
    let kind: ToolKind

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ToolHeader(kind: kind)
                .padding(.horizontal, 40)
                .padding(.top, 20)
                .padding(.bottom, 12)
                .frame(maxWidth: 1480 + 80)
                .frame(maxWidth: .infinity)
                .popupHeader()
                .zIndex(2) // (1.3.64: what opens under it — the Periodic Table's search results — over the tool)
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .background(PageGround())
    }

    @ViewBuilder
    private var content: some View {
        switch kind {
        case .cite: CitationTool()
        case .pomo: FocusTimerTool()
        case .calc: CalculatorTool()
        case .graph: GraphTool()
        case .ptable: PeriodicTableTool()
        case .lewis: LewisStructuresTool()
        case .molecule: MoleculeBuilderTool()
        case .atoms: AtomsOrbitalsTool()
        case .balance: EquationBalancerTool()
        case .motion: KinematicsTool()
        case .vectors: VectorsTool()
        case .circuit: CircuitTool()
        case .moles: MolarMassTool()
        case .gas: GasLawsTool()
        case .solution: SolutionsPHTool()
        case .need: GradeNeededTool()
        case .conv: FileConverterTool()
        case .units: UnitConverterTool()
        case .pdfx: PDFMergeSplitTool()
        case .mark: PDFAnnotatorTool()
        case .ocr: ImageToTextTool()
        case .fc: FlashcardsTool()
        case .words: WordCounterTool()
        case .stats: StatisticsTool()
        case .matrix: MatrixCalculatorTool()
        case .cell, .dna, .gene, .biomol, .cross, .divide, .popgen, .biolab, .enzyme, .energy: BioToolScreen(kind: kind)
        case .shrink: CompressFilesTool()
        case .plan: StudyPlannerTool()
        case .verbs: VerbConjugationsTool()
        }
    }
}

/// A tool's heading: the tool's tile, its name and line, its pin and — (1.3.79) at its right end, where every full popup
/// has its Close — its popup's ×. (tools-4) The Periodic Table's has its search and Tour left of its pin — (1.3.87) on
/// the heading's own row, where the window has room for them with the search open; else on a row of their own under it,
/// as before (1.3.80: no longer its pages — Lewis Structures and the Molecule Builder are tools of their own, with
/// headings of their own); (tools-29) while one of its elements is open, "‹ Table" leads it, back to the table.
struct ToolHeader: View {
    let kind: ToolKind
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var ptNav = PTNav.shared

    var body: some View {
        // (1.3.78) a biology tool with pages: its pages' names, and its pin's words, only where the header has room for
        // them beside the tool's whole name; else their symbols alone — so the header is never wider than the window
        // (a wider one widened the whole tool under it, cut off at both sides)
        if kind.isBiology && BioTools.pages(kind).count > 1 {
            ViewThatFits(in: .horizontal) {
                row(pages: false, pinWords: true)
                row(pages: true, pinWords: true)
                row(pages: true, pinWords: false)
                // (and where even their symbols leave the name no room — "Cell Explo…", "Bio La…" — the pages under the
                // name, in place of its line)
                stacked
            }
        } else if kind == .ptable {
            // (1.3.87) the search and Tour up on the heading's row, left of the pin — the owner: "push these buttons up"
            // — measured with the search open (`ptRoom`), so opening it never moves them to a row of their own; (1.3.88)
            // where that leaves no room for the pin's words ("Pin to Toolbar" beside an element's "‹ Table"), the pin its
            // symbol alone first, as a biology tool's is, before they go down to a row under it
            ViewThatFits(in: .horizontal) {
                row(pages: nil, pinWords: true, ptControls: true)
                row(pages: nil, pinWords: false, ptControls: true)
                VStack(alignment: .leading, spacing: 12) {
                    row(pages: nil, pinWords: true)
                    PTHeaderBar(alone: true)
                }
            }
        } else {
            row(pages: nil, pinWords: true)
        }
    }

    /// (tools-29) While one of the Periodic Table's elements is open, Back to the table. (1.3.79: the tool's × closes its
    /// popup, as Escape does — (tools-3) while the Periodic Table's tour is on, Escape ends the tour first.)
    @ViewBuilder
    private var leading: some View {
        if kind == .ptable && ptNav.elementOpen {
            Button {
                ptNav.closeElement()
            } label: {
                Label("Table", systemImage: "chevron.left")
                    .font(.sCallout.weight(.medium))
                    .padding(.horizontal, 4)
            }
            .glassButton()
            .controlSize(.large)
            .keyboardShortcut("[", modifiers: .command)
            .help("Back to the table (⌘[)")
            .transition(.opacity)
        }
    }

    /// (1.3.79) The popup's ×: the tool closed, the window under it as it was.
    private var closeButton: some View {
        Button {
            tools.closePopup()
        } label: {
            Label("Close", systemImage: "xmark")
                .font(.sCallout.weight(.semibold))
                .labelStyle(.iconOnly)
                .frame(minWidth: 16)
        }
        .glassButton()
        .controlSize(.large)
        .help("Close \(kind.name) (Esc)")
        .accessibilityLabel("Close \(kind.name)")
    }

    /// (1.3.87) What the Periodic Table's search and Tour take beside the pin with the search open (its field, its
    /// symbol and its ×, and Tour drawn in to its symbol): kept for them on the heading's row whether it is open or not.
    private static let ptRoom: CGFloat = 340

    /// The heading's row: `pages` squishes a biology tool's page buttons to their symbols (nil: as they fit);
    /// (1.3.87) `ptControls`, the Periodic Table's search and Tour left of the pin.
    private func row(pages squished: Bool?, pinWords: Bool, ptControls: Bool = false) -> some View {
        HStack(alignment: .center, spacing: 14) {
            leading
            IconTile(symbol: kind.symbol, color: kind.color, size: 44)
            VStack(alignment: .leading, spacing: 2) {
                Text(kind.name)
                    .font(.sTitle)
                    .lineLimit(1)
                    .contentTransition(.opacity)
                // (1.3.62) the Periodic Table's header its name alone; (tools-30) a tool may quiet its line for a while
                if kind != .ptable && !tools.quietHeadings.contains(kind) {
                    Text(kind.note)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .transition(.opacity)
                }
            }
            Spacer(minLength: 12)
            // (1.3.78) a biology tool's pages
            if kind.isBiology { BioHeaderControls(kind: kind, squished: squished) }
            // (a width of its own, not a least: the button growing into the field, both drawn a moment as it opens, never
            // widens the row, or the heading would go to its two rows as the search opened)
            if ptControls { PTHeaderBar().frame(width: Self.ptRoom, alignment: .trailing) }
            // (tools-31) the button says what a press does: Unpin while it is pinned, Pin to Toolbar while it is not
            Button {
                tools.togglePin(kind)
            } label: {
                Label(tools.isPinned(kind) ? "Unpin" : "Pin to Toolbar", systemImage: tools.isPinned(kind) ? "pin.slash" : "pin")
                    .font(.sCallout.weight(.medium))
                    .labelStyle(SquishLabel(squished: !pinWords))
            }
            .glassButton()
            .controlSize(.large)
            .help(tools.isPinned(kind) ? "\(kind.name) is in the toolbar on every screen. Click to unpin it." : "Keep \(kind.name) in the toolbar on every screen")
            closeButton
        }
        .animation(Motion.gentle, value: ptNav.elementOpen)
        .animation(Motion.gentle, value: tools.quietHeadings)
    }

    /// (1.3.78) A biology tool's heading in a narrow window: its name whole, its pages' symbols under it, its pin a
    /// symbol.
    private var stacked: some View {
        HStack(alignment: .center, spacing: 14) {
            leading
            IconTile(symbol: kind.symbol, color: kind.color, size: 44)
            VStack(alignment: .leading, spacing: 6) {
                Text(kind.name)
                    .font(.sTitle)
                    .lineLimit(1)
                    .minimumScaleFactor(0.75)
                    .help(kind.note)
                BioHeaderControls(kind: kind, squished: true)
            }
            // (the name first offered the row's room, before the spacer: offered half, "Biomolecule Builder" was cut
            // to "Biomolecule Buil…" with room to spare)
            .layoutPriority(1)
            Spacer(minLength: 12)
            // (tools-31, as the row's: what a press does, in a plain glass button)
            Button {
                tools.togglePin(kind)
            } label: {
                Label(tools.isPinned(kind) ? "Unpin" : "Pin to Toolbar", systemImage: tools.isPinned(kind) ? "pin.slash" : "pin")
                    .font(.sCallout.weight(.medium))
                    .labelStyle(SquishLabel(squished: true))
            }
            .glassButton()
            .controlSize(.large)
            .help(tools.isPinned(kind) ? "\(kind.name) is in the toolbar on every screen. Click to unpin it." : "Keep \(kind.name) in the toolbar on every screen")
            closeButton
        }
    }
}

// MARK: - What the tools share

/// A tool's scrolling body: the page's ground and side margins, its content as wide as the window allows.
struct ToolPage<Content: View>: View {
    var maxWidth: CGFloat = 1480
    var spacing: CGFloat = 20
    @ViewBuilder var content: Content

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: spacing) { content }
                .frame(maxWidth: maxWidth, alignment: .leading)
                .padding(.horizontal, 40)
                .padding(.top, 8)
                .padding(.bottom, 40)
                .frame(maxWidth: .infinity)
        }
    }
}

/// Two columns side by side on a wide window (the work and its side panel), one over the other on a narrow one.
struct ToolColumns<Main: View, Side: View>: View {
    var sideWidth: CGFloat = 380
    var breakpoint: CGFloat = 900
    var spacing: CGFloat = 28
    @ViewBuilder var main: Main
    @ViewBuilder var side: Side

    var body: some View {
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .top, spacing: spacing) {
                // (an ideal width of its own: a long line inside must not make the pair seem too wide to fit)
                main.frame(minWidth: breakpoint - sideWidth - spacing, idealWidth: breakpoint - sideWidth - spacing, maxWidth: .infinity, alignment: .topLeading)
                side.frame(width: sideWidth, alignment: .topLeading)
            }
            VStack(alignment: .leading, spacing: spacing) {
                main
                side
            }
        }
    }
}

/// A labelled field of a tool: its label over it, a hint under it.
struct ToolField: View {
    let label: String
    @Binding var text: String
    var placeholder: String = ""
    var hint: String? = nil
    var suffix: String? = nil
    var width: CGFloat? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(label).font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
            HStack(spacing: 6) {
                TextField(placeholder, text: $text)
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody)
                    .controlSize(.large)
                if let suffix { Text(suffix).font(.sBody).foregroundStyle(.secondary) }
            }
            .frame(maxWidth: width ?? .infinity, alignment: .leading)
            if let hint, !hint.isEmpty {
                Text(hint).font(.sFootnote).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

/// A short line in a tool that says something went wrong or needs doing.
struct ToolNote: View {
    let text: String
    var symbol: String = "exclamationmark.triangle.fill"
    var tint: Color = .orange

    var body: some View {
        Label {
            Text(text).fixedSize(horizontal: false, vertical: true)
        } icon: {
            Image(systemName: symbol).foregroundStyle(tint)
        }
        .font(.sCallout)
    }
}

/// Where a tool takes its files: dropped on it from the Finder, or chosen with a press.
struct ToolDropZone: View {
    let title: String
    var sub: String? = nil
    let types: [UTType]
    var multiple = true
    var symbol = "square.and.arrow.down.on.square"
    var tint: Color = Theme.accent
    var height: CGFloat = 170
    let onFiles: ([URL]) -> Void
    @State private var over = false

    var body: some View {
        Button {
            let urls = ToolFiles.choose(types: types, multiple: multiple)
            if !urls.isEmpty { onFiles(urls) }
        } label: {
            VStack(spacing: 10) {
                Image(systemName: symbol)
                    .font(.system(size: 30, weight: .regular))
                    .foregroundStyle(tint)
                Text(title).font(.sHeadline)
                if let sub { Text(sub).font(.sCallout).foregroundStyle(.secondary).multilineTextAlignment(.center) }
            }
            .padding(20)
            .frame(maxWidth: .infinity, minHeight: height)
            .background {
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .fill(tint.opacity(over ? 0.14 : 0.05))
            }
            .overlay {
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .strokeBorder(tint.opacity(over ? 0.8 : 0.35), style: StrokeStyle(lineWidth: 1.5, dash: [6, 5]))
            }
            .contentShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        }
        .buttonStyle(.plain)
        .dropDestination(for: URL.self) { urls, _ in
            let ok = ToolFiles.filter(urls, types: types)
            guard !ok.isEmpty else { return false }
            onFiles(multiple ? ok : [ok[0]])
            return true
        } isTargeted: { over = $0 }
        .animation(Motion.hover, value: over)
    }
}

/// The Mac's own panels for a tool's files, and the checks on what was dropped.
enum ToolFiles {
    @MainActor
    static func choose(types: [UTType], multiple: Bool, message: String? = nil) -> [URL] {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = types
        panel.allowsMultipleSelection = multiple
        panel.canChooseDirectories = false
        panel.canChooseFiles = true
        if let message { panel.message = message }
        return panel.runModal() == .OK ? panel.urls : []
    }

    @MainActor
    static func saveURL(name: String, type: UTType?, message: String? = nil) -> URL? {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = name
        if let type { panel.allowedContentTypes = [type] }
        panel.canCreateDirectories = true
        panel.isExtensionHidden = false
        if let message { panel.message = message }
        return panel.runModal() == .OK ? panel.url : nil
    }

    @MainActor
    static func chooseFolder(message: String) -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.allowsMultipleSelection = false
        panel.prompt = "Save Here"
        panel.message = message
        return panel.runModal() == .OK ? panel.url : nil
    }

    /// The files whose type is one of `types` (anything, for `.item`).
    static func filter(_ urls: [URL], types: [UTType]) -> [URL] {
        urls.filter { url in
            guard url.isFileURL else { return false }
            if types.contains(.item) { return true }
            guard let t = UTType(filenameExtension: url.pathExtension.lowercased()) else { return false }
            return types.contains { t.conforms(to: $0) }
        }
    }

    /// A name for a new file that does not take the place of one already in `folder`.
    static func free(_ name: String, ext: String, in folder: URL) -> URL {
        var url = folder.appendingPathComponent(name).appendingPathExtension(ext)
        var n = 2
        while FileManager.default.fileExists(atPath: url.path) {
            url = folder.appendingPathComponent("\(name) \(n)").appendingPathExtension(ext)
            n += 1
        }
        return url
    }

    static func sizeText(_ url: URL) -> String {
        let size = (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
        return ByteCountFormatter.string(fromByteCount: Int64(size), countStyle: .file)
    }

    /// The finished file shown in the Finder.
    @MainActor
    static func reveal(_ urls: [URL]) {
        NSWorkspace.shared.activateFileViewerSelecting(urls)
    }
}

/// "1 term", "3 terms".
func toolPlural(_ n: Int, _ one: String, _ many: String? = nil) -> String {
    "\(n) \(n == 1 ? one : (many ?? one + "s"))"
}

/// (1.3.79) A tool open whole, in its popup over the window (ToolsCenter.popUp; the popup is Shell/FloatingPopup.swift's,
/// as large as the screen's side of the window less a margin all round, and sized again with it): the tool asked for,
/// at its top, switched in place — a cross-fade — when another is opened in it (a hand-off from another tool), (1.3.83)
/// or when it is made afresh to take what its pin's panel hands it; the last one still drawn as the popup goes.
struct ToolPopup: View {
    @ObservedObject private var tools = ToolsCenter.shared
    @State private var last: ToolKind?

    var body: some View {
        ZStack {
            if let k = tools.popup ?? last {
                ToolScreen(kind: k)
                    .id("\(k.rawValue).\(tools.popupMade)")
                    .transition(.opacity)
            }
        }
        .animation(Motion.gentle, value: tools.popup)
        .animation(Motion.gentle, value: tools.popupMade)
        .onAppear { last = tools.popup }
        .onChange(of: tools.popup) { _, k in
            if let k { last = k }
        }
    }
}

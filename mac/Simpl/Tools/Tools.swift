import AppKit
import SwiftUI
import UniformTypeIdentifiers

// Tools (1.2): the things Simpl does on its own, as the web's Tools page has them — a citation generator, a focus timer,
// a calculator, a graphing calculator, the periodic table, Grade needed, a file converter, Merge & split PDFs, a PDF
// annotator, Image to text and flashcards — drawn natively, with a unit converter of the Mac's own beside them, and
// (1.3.78) the biology toolkit's ten under a heading of their own (Biology/BioTools.swift). One row in the sidebar opens
// a page of tiles, (tools-32) in sections by what they are for: Study, Math & science, Files and Biology; a tile opens
// its tool in the window, under a "‹ Tools" back to the tiles. Any tool can be pinned: it then sits in the toolbar on
// every screen and opens a compact version of itself in a popover (PinnedTools.swift). Everything a tool keeps stays on
// this Mac; nothing is sent to Canvas.

/// A tool: its key (the web's own, so a pin means the same thing on both), its name, the line under it, its symbol and
/// its colour (used for its tile and its pin, nothing else).
enum ToolKind: String, CaseIterable, Identifiable, Hashable {
    case cite, pomo, calc, graph, ptable, need, conv, units, pdfx, mark, ocr, fc
    // (1.3.78) the biology toolkit: its names, lines, symbols and colours are BioTools.info's
    case cell, dna, gene, biomol, cross, divide, popgen, biolab, enzyme, energy

    var id: String { rawValue }

    var name: String {
        switch self {
        case .cite: return "Citation generator"
        case .pomo: return "Focus timer"
        case .calc: return "Calculator"
        case .graph: return "Graphing calculator"
        case .ptable: return "Periodic table"
        case .need: return "Grade needed"
        case .conv: return "File converter"
        case .units: return "Unit converter"
        case .pdfx: return "Merge & split PDFs"
        case .mark: return "PDF annotator"
        case .ocr: return "Image to text"
        case .fc: return "Flashcards"
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
        case .ptable: return "Elements"
        case .need: return "Grade needed"
        case .conv: return "Convert"
        case .units: return "Units"
        case .pdfx: return "Merge & split"
        case .mark: return "Mark up"
        case .ocr: return "Image to text"
        case .fc: return "Flashcards"
        default: return BioTools.info(self).shortName
        }
    }

    /// The line under its name: (tools-32) one short line, so a tile reads at a glance.
    var note: String {
        switch self {
        case .cite: return "Cite in MLA, APA or Chicago."
        case .pomo: return "Focus, then take a break."
        case .calc: return "Scientific, from your keyboard."
        case .graph: return "Desmos, right here."
        case .ptable: return "Every element and its facts."
        case .need: return "What you need on the final."
        case .conv: return "Convert Word, PDF and images."
        case .units: return "Lengths, weights, temperatures."
        case .pdfx: return "Join PDFs, or take pages out."
        case .mark: return "Highlight and add notes."
        case .ocr: return "Copy the words in a picture."
        case .fc: return "Flip, learn, test and match."
        default: return BioTools.info(self).note
        }
    }

    var symbol: String {
        switch self {
        case .cite: return "quote.opening"
        case .pomo: return "timer"
        case .calc: return "plus.forwardslash.minus"
        case .graph: return "chart.xyaxis.line"
        case .ptable: return "atom"
        case .need: return "percent"
        case .conv: return "arrow.triangle.2.circlepath"
        case .units: return "ruler"
        case .pdfx: return "doc.on.doc"
        case .mark: return "highlighter"
        case .ocr: return "text.viewfinder"
        case .fc: return "rectangle.on.rectangle.angled"
        default: return BioTools.symbol(self)
        }
    }

    var hex: String {
        switch self {
        case .cite: return "#30b0c7"
        case .pomo: return "#ff9500"
        case .calc: return "#ff9f0a"
        case .graph: return "#5856d6"
        case .ptable: return "#30d158"
        case .need: return "#ff375f"
        case .conv: return "#34c759"
        case .units: return "#64d2ff"
        case .pdfx: return "#bf5af2"
        case .mark: return "#e5a500"
        case .ocr: return "#00b3a4"
        case .fc: return "#0a84ff"
        default: return BioTools.info(self).hex
        }
    }

    var color: Color { Color(hex: hex) }

    /// (tools-4) The name its heading and the window's title show: the Periodic Table's says the page it is on (Lewis
    /// structures, Molecule builder), as nothing else on those pages names them.
    var title: String {
        self == .ptable ? PTNav.shared.page.title : name
    }

    /// The width of its pinned popover. (tools-34) The Periodic Table's wider, for squares big enough to point at.
    var popoverWidth: CGFloat {
        switch self {
        case .ptable: return 480
        case .calc: return 420
        case .cite: return 400
        case .graph: return 360
        case .fc: return 340
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
        default: return nil
        }
    }
}

/// What the tools share across the window: which are pinned to the toolbar (kept in the app's defaults, the keys joined
/// by commas), the tool open on the Tools page, and files handed to a tool from its pin (dropped or chosen there) for it
/// to take when it opens.
@MainActor
final class ToolsCenter: ObservableObject {
    static let shared = ToolsCenter()
    static let pinsKey = "SimplPinnedTools"

    @Published private(set) var pinned: [ToolKind] = []
    /// The tool open on the Tools page (nil: the page of tiles). Kept while you go elsewhere: Back brings it back as left.
    @Published var open: ToolKind?
    /// (1.2.1) A pinned tool opened whole over the window, where you are, in a large sheet — not on the Tools page.
    @Published var popup: ToolKind?
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
        // (the screenshot suite: -SimplTool calc opens the Calculator on the Tools page; -SimplToolFile <path> hands the
        // tool opened a file, as a drop on its pin does — the annotator's page, Split's pages)
        if let t = LaunchOpen.take("", key: "SimplTool"), let k = ToolKind(rawValue: t) { open = k }
        if let k = open, let path = LaunchOpen.take("", key: "SimplToolFile"), !path.isEmpty {
            launchFile = (k, URL(fileURLWithPath: path))
        }
    }

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

    /// (tools-6) How many tools the toolbar holds: a screen's own buttons (the Dashboard's Cards, List and Activity, its
    /// New Task) keep their room, where a fourth pin used to send them into the toolbar's » menu.
    static let toolbarRoom = 3

    /// (tools-6) The pins split between the toolbar and its Tools pull-down: all of them while they fit, else the first
    /// two and the pull-down with the rest — or, while a session runs, the timer (its pin as wide as two, with its
    /// minutes and seconds) and the pull-down with every other pin.
    func toolbarSplit(timerActive: Bool) -> (shown: [ToolKind], more: [ToolKind]) {
        let all = toolbarPins(timerActive: timerActive)
        guard all.count > Self.toolbarRoom else { return (all, []) }
        if timerActive { return ([.pomo], all.filter { $0 != .pomo }) }
        return (Array(all.prefix(Self.toolbarRoom - 1)), Array(all.dropFirst(Self.toolbarRoom - 1)))
    }

    /// (tools-30) A tool's heading with or without its line under the name.
    func quietHeading(_ k: ToolKind, _ quiet: Bool) {
        guard quietHeadings.contains(k) != quiet else { return }
        if quiet { quietHeadings.insert(k) } else { quietHeadings.remove(k) }
    }

    /// A tool opened on the Tools page from anywhere (a pin's popover, the Go menu), with any files or details for it.
    func show(_ k: ToolKind, engine: Engine?, files: [URL] = [], info: [String: String] = [:]) {
        handoff = files
        handoffTool = files.isEmpty ? nil : k
        handoffInfo = info
        withAnimation(Motion.gentle) { open = k }
        engine?.go(.tools)
    }

    /// A pinned tool opened whole (1.2.1): over the window, in a large sheet, with any files or details for it — the
    /// screen you were on stays as it was under it.
    func popUp(_ k: ToolKind, files: [URL] = [], info: [String: String] = [:]) {
        handoff = files
        handoffTool = files.isEmpty ? nil : k
        handoffInfo = info
        popup = k
    }

    /// (1.3.73) A tool opened whole from its pin: in the large sheet over the window — or, when a sheet already has the
    /// window (a quiz, an Alta or a Macmillan assignment: a second sheet cannot come up), on the Tools page of a window
    /// more, beside it.
    func openFull(_ k: ToolKind, engine: Engine?, files: [URL] = [], info: [String: String] = [:]) {
        let window = engine?.hostView.window
        let taken = window.map { $0.attachedSheet != nil || !$0.sheets.isEmpty } ?? false
        if taken {
            show(k, engine: nil, files: files, info: info)
            NotificationCenter.default.post(name: .simplOpenWindow, object: "tools")
        } else {
            popUp(k, files: files, info: info)
        }
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

/// The Tools place: the page of tiles, or the tool open on it with its own heading (Back to the tiles, its name, and its
/// pin) — a tool switched in place, as the web's popup opens over its page. (tools-5) The pin is the heading's alone:
/// the window's toolbar no longer carries a second one while a tool is open.
struct ToolsView: View {
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var ptNav = PTNav.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ZStack {
            if let k = tools.open {
                ToolScreen(kind: k)
                    .id(k)
                    .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.opacity.combined(with: .scale(scale: 0.985)))
            } else {
                ToolsGrid()
                    .transition(.opacity)
            }
        }
        .animation(reduceMotion ? nil : Motion.gentle, value: tools.open)
        .navigationTitle(tools.open?.title ?? "Tools")
    }
}

/// (tools-32) The Tools page's sections, in order — what each tool is for, so a tool is found by its kind rather than by
/// reading every tile; the study tools first.
enum ToolGroup: CaseIterable {
    case study, science, files, biology

    var title: String {
        switch self {
        case .study: return "Study"
        case .science: return "Math & science"
        case .files: return "Files"
        case .biology: return "Biology"
        }
    }

    var kinds: [ToolKind] {
        switch self {
        case .study: return [.fc, .pomo, .cite, .need]
        case .science: return [.calc, .graph, .units, .ptable]
        case .files: return [.conv, .pdfx, .mark, .ocr]
        case .biology: return BioTools.kinds
        }
    }
}

/// The page of tiles: every tool, a press opening it, its pin at its corner — (tools-32) in sections by what they are
/// for, (1.3.78) the biology toolkit's last, under a heading of its own.
struct ToolsGrid: View {
    var body: some View {
        ScrollViewReader { proxy in
            Page {
                ScreenHeading(title: "Tools", sub: "Pin a tool to keep it in the toolbar: its button there opens a quick version of it.")
                ForEach([ToolGroup.study, .science, .files], id: \.self) { g in
                    ToolGroupSection(title: g.title, kinds: g.kinds)
                }
                BioToolsSection()
            }
            .task {
                // (the screenshot suite: -SimplToolsScroll biology, the page scrolled to the Biology section)
                guard UserDefaults.standard.string(forKey: "SimplToolsScroll") == "biology" else { return }
                try? await Task.sleep(nanoseconds: 400_000_000)
                proxy.scrollTo("biology", anchor: .top)
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

    /// (tools-32) Three across from about 900 points of page, two below.
    static let columns = [GridItem(.adaptive(minimum: 280), spacing: 14)]

    var body: some View {
        Button {
            withAnimation(Motion.gentle) { tools.open = kind }
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
            Button("Open") { withAnimation(Motion.gentle) { tools.open = kind } }
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

/// A tool open on the Tools page: its heading (Back to the tiles, its tile, its name and line, its pin), and the tool
/// under it, filling the window.
struct ToolScreen: View {
    let kind: ToolKind
    /// (1.2.1) Over the window as a sheet (a pinned tool opened whole): Done closes it.
    var done: (() -> Void)? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ToolHeader(kind: kind, done: done)
                .padding(.horizontal, 40)
                .padding(.top, 20)
                .padding(.bottom, 12)
                .frame(maxWidth: 1480 + 80)
                .frame(maxWidth: .infinity)
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
        case .need: GradeNeededTool()
        case .conv: FileConverterTool()
        case .units: UnitConverterTool()
        case .pdfx: PDFMergeSplitTool()
        case .mark: PDFAnnotatorTool()
        case .ocr: ImageToTextTool()
        case .fc: FlashcardsTool()
        case .cell, .dna, .gene, .biomol, .cross, .divide, .popgen, .biolab, .enzyme, .energy: BioToolScreen(kind: kind)
        }
    }
}

/// A tool's heading: Back to the tiles, the tool's tile, its name and line, and its pin. (tools-4) The Periodic Table's
/// names the page it is on, with its pages, Tour and search in words on a row of their own under it; (tools-29) while one
/// of its elements is open, its one Back goes to the table ("‹ Table"), not to the tiles.
struct ToolHeader: View {
    let kind: ToolKind
    var done: (() -> Void)? = nil
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var ptNav = PTNav.shared
    @ObservedObject private var ptTour = PTTour.shared

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
            VStack(alignment: .leading, spacing: 12) {
                row(pages: nil, pinWords: true)
                PTPageBar()
            }
        } else {
            row(pages: nil, pinWords: true)
        }
    }

    /// Done (in the pin's sheet) or Back to the tiles — (tools-29) or, while one of the Periodic Table's elements is
    /// open, Back to the table (beside Done in the sheet).
    @ViewBuilder
    private var leading: some View {
        if let done {
            Button("Done", action: done)
                .font(.sCallout.weight(.medium))
                .glassButton()
                .controlSize(.large)
                // (tools-3: while the Periodic Table's tour is on, Escape ends the tour, not the sheet)
                .keyboardShortcut(kind == .ptable && ptTour.active ? nil : KeyboardShortcut.cancelAction)
                .help("Close \(kind.name) (Escape)")
        }
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
        } else if done == nil {
            Button {
                withAnimation(Motion.gentle) { tools.open = nil }
            } label: {
                Label("Tools", systemImage: "chevron.left")
                    .font(.sCallout.weight(.medium))
                    .padding(.horizontal, 4)
            }
            .glassButton()
            .controlSize(.large)
            .help("Back to all the tools")
            .transition(.opacity)
        }
    }

    /// The heading's row: `pages` squishes a biology tool's page buttons to their symbols (nil: as they fit).
    private func row(pages squished: Bool?, pinWords: Bool) -> some View {
        HStack(alignment: .center, spacing: 14) {
            leading
            IconTile(symbol: kind.symbol, color: kind.color, size: 44)
            VStack(alignment: .leading, spacing: 2) {
                Text(kind.title)
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

/// (1.2.1) A pinned tool opened whole, over the window: the tool as the Tools page has it, in a sheet nearly the
/// window's size.
struct ToolPopup: View {
    let kind: ToolKind
    @ObservedObject private var tools = ToolsCenter.shared

    var body: some View {
        let size = ToolPopup.size()
        ToolScreen(kind: kind) { tools.popup = nil }
            .frame(width: size.width, height: size.height)
    }

    /// Nearly the main window's size.
    @MainActor
    static func size() -> CGSize {
        let f = AppModel.shared.engine?.hostView.window?.frame.size ?? CGSize(width: 1280, height: 860)
        return CGSize(width: max(860, f.width - 70), height: max(600, f.height - 90))
    }
}

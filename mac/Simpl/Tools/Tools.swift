import AppKit
import SwiftUI
import UniformTypeIdentifiers

// Tools (1.2): the things Simpl does on its own, as the web's Tools page has them — a citation generator, a focus timer,
// a calculator, a graphing calculator, the periodic table, Grade needed, a file converter, Merge & split PDFs, a PDF
// annotator, Image to text and flashcards — drawn natively, with a unit converter of the Mac's own beside them. One row
// in the sidebar opens a page of tiles; a tile opens its
// tool in the window. Any tool can be pinned: it then sits in the toolbar on every screen and opens a compact version of
// itself in a popover (PinnedTools.swift). Everything a tool keeps stays on this Mac; nothing is sent to Canvas.

/// A tool: its key (the web's own, so a pin means the same thing on both), its name, the line under it, its symbol and
/// its colour (used for its tile and its pin, nothing else).
enum ToolKind: String, CaseIterable, Identifiable, Hashable {
    case cite, pomo, calc, graph, ptable, need, conv, units, pdfx, mark, ocr, fc

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
        }
    }

    /// The name a pin's popover and the toolbar's tooltips use.
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
        }
    }

    var note: String {
        switch self {
        case .cite: return "Cite a source in MLA, APA or Chicago."
        case .pomo: return "Focus for a while, then take a break."
        case .calc: return "Scientific, laid out like the Mac’s."
        case .graph: return "Desmos, right here."
        case .ptable: return "Every element and its facts, in place."
        case .need: return "What you need on the final to hit your goal."
        case .conv: return "Word, PDF and images, any way round."
        case .units: return "Lengths, weights, temperatures and more."
        case .pdfx: return "Join pages from PDFs into one, or take pages out."
        case .mark: return "Highlight and add notes, saved in the file."
        case .ocr: return "Read the words off a picture or a scan."
        case .fc: return "Make a set. Flip, learn, test, match."
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
        }
    }

    var color: Color { Color(hex: hex) }

    /// The width of its pinned popover.
    var popoverWidth: CGFloat {
        switch self {
        case .calc, .ptable: return 420
        case .cite: return 400
        case .graph: return 360
        case .fc: return 340
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

    init() {
        let raw = UserDefaults.standard.string(forKey: ToolsCenter.pinsKey) ?? ""
        var seen = Set<ToolKind>()
        pinned = raw.split(separator: ",").compactMap { ToolKind(rawValue: String($0).trimmingCharacters(in: .whitespaces)) }.filter { seen.insert($0).inserted }
        // (the screenshot suite: -SimplTool calc opens the Calculator on the Tools page)
        if let t = LaunchOpen.take("", key: "SimplTool"), let k = ToolKind(rawValue: t) { open = k }
    }

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

    /// The files handed to a tool, taken once.
    func takeFiles(for k: ToolKind) -> [URL] {
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
/// pin) — a tool switched in place, as the web's popup opens over its page.
struct ToolsView: View {
    @ObservedObject private var tools = ToolsCenter.shared
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
        .navigationTitle(tools.open?.name ?? "Tools")
        .toolbar {
            if let k = tools.open {
                ToolbarItem(placement: .primaryAction) {
                    Button {
                        tools.togglePin(k)
                    } label: {
                        Label(tools.isPinned(k) ? "Unpin from Toolbar" : "Pin to Toolbar", systemImage: tools.isPinned(k) ? "pin.slash" : "pin")
                    }
                    .help(tools.isPinned(k) ? "Take \(k.name) out of the toolbar" : "Keep \(k.name) in the toolbar on every screen")
                }
            }
        }
    }
}

/// The page of tiles: every tool, a press opening it, its pin at its corner.
struct ToolsGrid: View {
    @ObservedObject private var tools = ToolsCenter.shared

    var body: some View {
        Page {
            ScreenHeading(title: "Tools", sub: "Handy things, right here. Pin one and it sits in the toolbar on every screen.")
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 300), spacing: 16)], spacing: 16) {
                ForEach(ToolKind.allCases) { k in
                    ToolTile(kind: k)
                }
            }
            if tools.pinned.isEmpty {
                Label("Press a tile’s pin to keep that tool in the toolbar. Its button there opens a quick version of it.", systemImage: "pin")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
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

    var body: some View {
        Button {
            withAnimation(Motion.gentle) { tools.open = kind }
        } label: {
            HStack(alignment: .top, spacing: 14) {
                IconTile(symbol: kind.symbol, color: kind.color, size: 48)
                VStack(alignment: .leading, spacing: 4) {
                    Text(kind.name)
                        .font(.sHeadline)
                        .foregroundStyle(.primary)
                    Text(kind.note)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                    if kind == .pomo && focus.active {
                        TimelineView(.periodic(from: .now, by: 1)) { ctx in
                            Text("\(focus.rec.phase.name) · \(FocusTimer.clock(focus.remaining(at: ctx.date)))\(focus.paused ? " · Paused" : "")")
                                .font(.sCallout.weight(.semibold).monospacedDigit())
                                .foregroundStyle(focus.rec.phase.color)
                        }
                    }
                }
                Spacer(minLength: 36)
            }
            .padding(18)
            .frame(maxWidth: .infinity, minHeight: 104, alignment: .topLeading)
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
                .frame(width: 32, height: 32)
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .glass(Circle(), tint: on ? kind.color.opacity(0.25) : nil, interactive: true)
        .help(on ? "Unpin \(kind.name) from the toolbar" : "Pin \(kind.name) to the toolbar")
        .accessibilityLabel(on ? "Unpin \(kind.name)" : "Pin \(kind.name)")
        .tourSpot(kind == .graph && !on ? .toolPin : nil) // (1.2.16: the tour lights the graphing calculator's)
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
        }
    }
}

/// A tool's heading: Back to the tiles, the tool's tile, its name and line, and its pin.
struct ToolHeader: View {
    let kind: ToolKind
    var done: (() -> Void)? = nil
    @ObservedObject private var tools = ToolsCenter.shared

    var body: some View {
        HStack(alignment: .center, spacing: 14) {
            if let done {
                Button("Done", action: done)
                    .font(.sCallout.weight(.medium))
                    .glassButton()
                    .controlSize(.large)
                    .keyboardShortcut(.cancelAction)
                    .help("Close \(kind.name) (Escape)")
            } else {
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
            }
            IconTile(symbol: kind.symbol, color: kind.color, size: 44)
            VStack(alignment: .leading, spacing: 2) {
                Text(kind.name)
                    .font(.sTitle)
                    .lineLimit(1)
                Text(kind.note)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 12)
            Button {
                tools.togglePin(kind)
            } label: {
                Label(tools.isPinned(kind) ? "Pinned" : "Pin to Toolbar", systemImage: tools.isPinned(kind) ? "pin.fill" : "pin")
                    .font(.sCallout.weight(.medium))
            }
            .glassButton(prominent: tools.isPinned(kind))
            .tint(kind.color)
            .controlSize(.large)
            .help(tools.isPinned(kind) ? "In the toolbar on every screen. Press to take it out." : "Keep \(kind.name) in the toolbar on every screen")
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

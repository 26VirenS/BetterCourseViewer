import SwiftUI
import UniformTypeIdentifiers

// The tools pinned to the toolbar (1.2): the web's tray at the top right, on every screen of the window. Each pin is a
// toolbar button in its tool's colour; a press opens a popover with the tool's quickest use — the timer's controls, the
// calculator, Grade needed's three numbers, the periodic table small, a link to cite, a set to study, a file to drop —
// and its name opens the whole tool. A running focus timer is live in its pin, as the web's island: the minutes left on
// a ring and in figures, in its phase's colour (it borrows a pin while it runs if the timer is not pinned). A pin's
// context menu opens, moves or unpins it.

/// A pin in the toolbar.
struct PinnedToolButton: View {
    let kind: ToolKind
    let engine: Engine
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var focus = FocusTimer.shared
    @State private var shown = false

    private var live: Bool { kind == .pomo && focus.active }

    var body: some View {
        Button {
            shown.toggle()
        } label: {
            if live {
                PinnedTimerLabel()
            } else {
                Label {
                    Text(kind.name)
                } icon: {
                    Image(systemName: kind.symbol)
                        .foregroundStyle(kind.color)
                }
            }
        }
        .help(live ? "Focus timer: \(focus.rec.phase.name.lowercased())\(focus.paused ? ", paused" : "")" : kind.name)
        .accessibilityLabel(kind.name)
        .popover(isPresented: $shown, arrowEdge: .bottom) {
            PinnedToolPopover(kind: kind, engine: engine) { shown = false }
                .environmentObject(engine)
        }
        .contextMenu { menu }
    }

    @ViewBuilder
    private var menu: some View {
        Button("Open \(kind.name)") { tools.show(kind, engine: engine) }
        if kind == .pomo && focus.active {
            Divider()
            Button(focus.running ? "Pause" : "Resume") { focus.toggle() }
            Button("Skip to \(focus.nextPhase.name)") { focus.skip() }
            Button("Cancel Session") { focus.reset() }
        }
        Divider()
        if tools.isPinned(kind) {
            Button("Move Left") { tools.move(kind, by: -1) }
                .disabled(tools.pinned.first == kind)
            Button("Move Right") { tools.move(kind, by: 1) }
                .disabled(tools.pinned.last == kind)
            Button("Unpin from Toolbar") { tools.unpin(kind) }
        } else {
            Button("Pin to Toolbar") { tools.pin(kind) }
        }
    }
}

/// The running timer's pin: what is left of the phase on a small ring, and the minutes and seconds beside it.
struct PinnedTimerLabel: View {
    @ObservedObject private var focus = FocusTimer.shared

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { ctx in
            let color = focus.rec.phase.color
            HStack(spacing: 6) {
                ZStack {
                    Circle().stroke(color.opacity(0.25), lineWidth: 2.5)
                    Circle()
                        .trim(from: 0, to: focus.fraction(at: ctx.date))
                        .stroke(color, style: StrokeStyle(lineWidth: 2.5, lineCap: .round))
                        .rotationEffect(.degrees(-90))
                }
                .frame(width: 15, height: 15)
                Text(FocusTimer.clock(focus.remaining(at: ctx.date)))
                    .font(.system(size: 13.5, weight: .semibold, design: .rounded).monospacedDigit())
                    .foregroundStyle(color)
                if focus.paused {
                    Image(systemName: "pause.fill")
                        .font(.system(size: 9, weight: .bold))
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.horizontal, 4)
            .fixedSize()
        }
    }
}

/// A pin's popover: the tool's name (a press opens the whole tool), its pin, and its quickest use.
struct PinnedToolPopover: View {
    let kind: ToolKind
    let engine: Engine
    let close: () -> Void
    @ObservedObject private var tools = ToolsCenter.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            content
        }
        .padding(16)
        .frame(width: kind.popoverWidth)
    }

    private var header: some View {
        HStack(spacing: 10) {
            IconTile(symbol: kind.symbol, color: kind.color, size: 28)
            Button {
                openFull()
            } label: {
                HStack(spacing: 4) {
                    Text(kind.shortName).font(.sHeadline)
                    Image(systemName: "chevron.right")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(.secondary)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("Open \(kind.name)")
            Spacer(minLength: 8)
            Button {
                tools.togglePin(kind)
            } label: {
                Image(systemName: tools.isPinned(kind) ? "pin.slash" : "pin")
                    .frame(width: 26, height: 26)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.borderless)
            .help(tools.isPinned(kind) ? "Unpin from the toolbar" : "Pin to the toolbar")
            Button {
                openFull()
            } label: {
                Image(systemName: "arrow.up.left.and.arrow.down.right")
                    .frame(width: 26, height: 26)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.borderless)
            .help("Open \(kind.name) full size")
        }
    }

    @ViewBuilder
    private var content: some View {
        switch kind {
        case .pomo: FocusTimerCompact()
        case .calc: CalculatorCompact()
        case .need: GradeNeededCompact { openFull(info: $0) }
        case .ptable: PeriodicTableCompact { openFull(info: $0) }
        case .cite: CitationCompact { openFull(info: $0) }
        case .fc: FlashcardsCompact { openFull(info: $0) }
        case .graph: GraphCompact()
        case .units: UnitConverterCompact()
        case .conv:
            fileDrop(title: "Click to add files", sub: "Documents, PDFs, pictures and data, turned into one another.", multiple: true)
        case .pdfx:
            fileDrop(title: "Click to add PDFs", sub: "Put PDFs together, pull pages out, or turn them.", multiple: true)
        case .mark:
            fileDrop(title: "Click to add a PDF", sub: "Highlight, underline and add notes; saved in the PDF.", multiple: false)
        case .ocr:
            fileDrop(title: "Click to add a picture", sub: "The words in a picture or a scan, as text you can copy.", multiple: false)
        }
    }

    private func fileDrop(title: String, sub: String, multiple: Bool) -> some View {
        ToolDropZone(title: title, sub: sub, types: kind.accepts ?? [.item], multiple: multiple, tint: kind.color, height: 140) { urls in
            openFull(files: urls)
        }
    }

    /// The whole tool (1.2.1): over the window where you are, as a large sheet — not on the Tools page.
    private func openFull(info: [String: String] = [:], files: [URL] = []) {
        close()
        tools.popUp(kind, files: files, info: info)
    }
}

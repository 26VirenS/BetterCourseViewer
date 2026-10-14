import SwiftUI
import UniformTypeIdentifiers

// The tools pinned to the toolbar (1.2): the web's tray at the top right, on every screen of the window. Each pin is a
// toolbar button in its tool's colour; a press opens a popover with the tool's quickest use — the timer's controls, the
// calculator, Grade needed's three numbers, the periodic table small, a link to cite, a set to study, a file to drop —
// and its name opens the whole tool. A running focus timer is live in its pin, as the web's island: the minutes left on
// a ring and in figures, in its phase's colour (it borrows a pin while it runs if the timer is not pinned). A pin's
// context menu opens, moves or unpins it. (tools-6) The toolbar holds three tools at most: past that, two and a Tools
// pull-down with the rest, so a screen's own buttons keep their room.

/// A pin in the toolbar.
struct PinnedToolButton: View {
    let kind: ToolKind
    let engine: Engine
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var focus = FocusTimer.shared
    @ObservedObject private var floats = ToolFloats.shared
    @State private var shown = false
    /// (the screenshot suite) The launch's -SimplPinOpen has opened this pin's popover.
    @State private var launchOpened = false

    private var live: Bool { kind == .pomo && focus.active }

    var body: some View {
        Button {
            // (1.3.73: its quick panel pulled out and floating — brought to the front instead of the popover)
            if floats.isOut(kind) { floats.raise(kind) } else { shown.toggle() }
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
        // (the screenshot suite: -SimplPinOpen ptable opens that pin's popover a moment after launch; -SimplPinFloat calc
        // pulls that pin's panel out, near the window's top right)
        .task {
            let d = UserDefaults.standard
            if d.string(forKey: "SimplPinOpen") == kind.rawValue {
                try? await Task.sleep(nanoseconds: 2_500_000_000)
                shown = true
                launchOpened = true
            } else if d.string(forKey: "SimplPinFloat") == kind.rawValue {
                try? await Task.sleep(nanoseconds: 3_000_000_000)
                guard let w = engine.hostView.window, !floats.isOut(kind) else { return }
                floats.pullOut(kind, engine: engine, topLeft: NSPoint(x: w.frame.maxX - kind.popoverWidth - 48, y: w.frame.maxY - 112), mouse: nil)
            }
        }
        // (and open again as the app comes back to the front: a popover closes when its app is put behind another, and
        // the suite runs several copies of the app at once — Hardy–Weinberg's and the lab's were pictured closed)
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            guard launchOpened, !shown, UserDefaults.standard.string(forKey: "SimplPinOpen") == kind.rawValue else { return }
            shown = true
        }
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

/// The running timer's pin: what is left of the phase on a small ring, and the minutes and seconds beside it — (tools-25)
/// read from the one second hand every timer label shares, so the pin and the dial never say two different seconds.
struct PinnedTimerLabel: View {
    @ObservedObject private var focus = FocusTimer.shared
    @ObservedObject private var clock = FocusClock.shared

    var body: some View {
        let color = focus.rec.phase.color
        HStack(spacing: 6) {
            ZStack {
                Circle().stroke(color.opacity(0.25), lineWidth: 2.5)
                Circle()
                    .trim(from: 0, to: focus.fraction(at: clock.now))
                    .stroke(color, style: StrokeStyle(lineWidth: 2.5, lineCap: .round))
                    .rotationEffect(.degrees(-90))
            }
            .frame(width: 15, height: 15)
            Text(FocusTimer.clock(focus.remaining(at: clock.now)))
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

/// (tools-6) The pins past the toolbar's room, under one Tools pull-down: a tool picked opens its quick version in a
/// popover under the pull-down, as its own pin would (or brings its floating panel to the front, when it is out).
struct MorePinsMenu: View {
    let kinds: [ToolKind]
    let engine: Engine
    @ObservedObject private var floats = ToolFloats.shared
    @State private var shown: ToolKind?

    var body: some View {
        Menu {
            ForEach(kinds) { k in
                Button {
                    if floats.isOut(k) { floats.raise(k) } else { shown = k }
                } label: {
                    Label(k.name, systemImage: k.symbol)
                }
            }
            Divider()
            Button("Show All Tools") { engine.go(.tools) }
        } label: {
            Label("Tools", systemImage: "wrench.and.screwdriver")
        }
        .help("More pinned tools: \(kinds.map(\.name).joined(separator: ", "))")
        .accessibilityLabel("More pinned tools")
        .popover(item: $shown, arrowEdge: .bottom) { k in
            PinnedToolPopover(kind: k, engine: engine) { shown = nil }
                .environmentObject(engine)
        }
    }
}

/// A pin's popover: (tools-21) the tool's name as its tile says it — a press opens the whole tool, (tools-22) the one way
/// to — (tools-20) Pop Out, which takes the quick panel out into a small window of its own that floats over everything,
/// and the tool's quickest use. Unpinning is in the pin's own menu, not a button beside the name where a slip would make
/// the pin vanish.
struct PinnedToolPopover: View {
    let kind: ToolKind
    let engine: Engine
    let close: () -> Void
    @ObservedObject private var tools = ToolsCenter.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            PinnedToolQuick(kind: kind) { info, files in openFull(info: info, files: files) }
            // (1.3.73) the grip along the foot: dragged, the panel comes out into a window of its own (ToolFloat.swift)
            PullOutGrip(kind: kind, engine: engine, close: close)
                .padding(.bottom, -10)
        }
        .padding(16)
        .frame(width: kind.popoverWidth)
    }

    /// (a long name beside Pop Out's words: Pop Out its symbol alone)
    private var header: some View {
        ViewThatFits(in: .horizontal) {
            headerRow(words: true)
            headerRow(words: false)
        }
    }

    private func headerRow(words: Bool) -> some View {
        HStack(spacing: 10) {
            IconTile(symbol: kind.symbol, color: kind.color, size: 28)
            QuickPanelName(kind: kind) { openFull() }
            Spacer(minLength: 8)
            Button {
                ToolFloats.shared.popOut(kind, engine: engine)
                close()
            } label: {
                Label("Pop Out", systemImage: "pip.exit")
                    .font(.sCallout.weight(.medium))
                    .labelStyle(SquishLabel(squished: !words))
            }
            .glassButton()
            .controlSize(.small)
            .help("Pop out: keep \(kind.name) open in a small window that floats over everything, quizzes too")
            .accessibilityLabel("Pop Out")
        }
    }

    /// The whole tool (1.2.1): over the window where you are, as a large sheet — not on the Tools page (1.3.73: or, when
    /// a popup already has the window, on the Tools page of a window more).
    private func openFull(info: [String: String] = [:], files: [URL] = []) {
        close()
        tools.openFull(kind, engine: engine, files: files, info: info)
    }
}

/// (tools-21, tools-22) A quick panel's name, as the tool's tile says it, with a › — a press opens the whole tool.
struct QuickPanelName: View {
    let kind: ToolKind
    let open: () -> Void

    var body: some View {
        Button(action: open) {
            HStack(spacing: 4) {
                Text(kind.name)
                    .font(.sHeadline)
                    .lineLimit(1)
                    .minimumScaleFactor(0.85)
                Image(systemName: "chevron.right")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(.secondary)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("Open \(kind.name) full size")
        .accessibilityLabel("Open \(kind.name)")
    }
}

/// A tool's quickest use (the popover's, and the pulled-out panel's): the timer's controls, the calculator, Grade
/// needed's three numbers, the periodic table small, a link to cite, a set to study, a file to drop. `openFull` opens
/// the whole tool with what was begun here.
struct PinnedToolQuick: View {
    let kind: ToolKind
    let openFull: (_ info: [String: String], _ files: [URL]) -> Void

    var body: some View {
        switch kind {
        case .pomo: FocusTimerCompact()
        case .calc: CalculatorCompact()
        case .need: GradeNeededCompact { openFull($0, []) }
        case .ptable: PeriodicTableCompact { openFull($0, []) }
        case .cite: CitationCompact { openFull($0, []) }
        case .fc: FlashcardsCompact { openFull($0, []) }
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
        case .cell, .dna, .gene, .biomol, .cross, .divide, .popgen, .biolab, .enzyme, .energy:
            // (1.3.78) the biology tools': Biology/BioTools.swift
            BioQuick(kind: kind) { openFull($0, []) }
        }
    }

    private func fileDrop(title: String, sub: String, multiple: Bool) -> some View {
        ToolDropZone(title: title, sub: sub, types: kind.accepts ?? [.item], multiple: multiple, tint: kind.color, height: 140) { urls in
            openFull([:], urls)
        }
    }
}

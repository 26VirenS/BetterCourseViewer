import SwiftUI
import UniformTypeIdentifiers

// The tools pinned to the toolbar (1.2): the web's tray at the top right, on every screen of the window. Each pin is a
// toolbar button in its tool's colour; a press opens a popover with the tool's quickest use — the timer's controls, the
// calculator, Grade needed's three numbers, the periodic table small, a link to cite, a set to study, a file to drop —
// and (1.3.83) Open Full Screen at its top right opens the whole tool with what was begun there. A running focus timer
// is live in its pin, as the web's island: the minutes left on a ring and in figures, in its phase's colour (it borrows
// a pin while it runs if the timer is not pinned). A pin's context menu opens, pops out, moves or unpins it. (tools-6)
// The pins past the toolbar's room go into a Tools pull-down, so a screen's own buttons keep theirs — (1.3.79) only
// those: every pin that fits is shown, the toolbar's room measured as it is laid out (ToolbarFit), where tools-6 had
// three at most whatever the room.

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
        // (the screenshot suite: -SimplPinOpen ptable opens that pin's popover a moment after launch — -SimplPinFull need
        // too, and then presses its Open Full Screen: PinnedToolPopover; -SimplPinFloat calc pulls that pin's panel out,
        // near the window's top right)
        .task {
            let d = UserDefaults.standard
            if d.string(forKey: "SimplPinOpen") == kind.rawValue
                || (d.string(forKey: "SimplPinFull") == kind.rawValue && !PinnedToolPopover.launchFullPressed) {
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
            let d = UserDefaults.standard
            guard launchOpened, !shown else { return }
            // (-SimplPinFull's until its Open Full Screen has been pressed)
            guard d.string(forKey: "SimplPinOpen") == kind.rawValue
                    || (d.string(forKey: "SimplPinFull") == kind.rawValue && !PinnedToolPopover.launchFullPressed) else { return }
            shown = true
        }
    }

    @ViewBuilder
    private var menu: some View {
        Button("Open \(kind.name)") { tools.popUp(kind, engine: engine) } // (1.3.79: in its popup over the window)
        // (1.3.83) Pop Out, here since Open Full Screen took its place at the popover's top right: the quick panel in its
        // floating window by a press as well as by the popover's grip
        Button("Pop Out") { floats.popOut(kind, engine: engine) }
            .disabled(floats.isOut(kind))
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

/// A pin's popover: (tools-21) the tool's name as its tile says it; (1.3.83) Open Full Screen at its top right — the
/// tool's whole view in its popup over the window, with what was begun here (QuickCarry) — where Pop Out was (the grip
/// along the foot still pulls the panel out, and so does Pop Out in the pin's menu); and the tool's quickest use.
/// (tools-22) Open Full Screen is the one way from here to the whole tool: the name's ›, which opened it too, is gone,
/// and the name is the panel's title. Unpinning is in the pin's own menu, not a button beside the name where a slip
/// would make the pin vanish.
struct PinnedToolPopover: View {
    let kind: ToolKind
    let engine: Engine
    let close: () -> Void
    @ObservedObject private var tools = ToolsCenter.shared
    /// What the quick version has begun, for the whole tool (QuickCarry).
    @State private var carry = QuickCarry()
    /// (the screenshot suite) -SimplPinFull has pressed Open Full Screen, once a launch.
    @MainActor static var launchFullPressed = false

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                IconTile(symbol: kind.symbol, color: kind.color, size: 28)
                QuickPanelName(kind: kind)
                Spacer(minLength: 8)
                QuickFullScreenButton(kind: kind, action: openFullScreen)
            }
            PinnedToolQuick(kind: kind) { info, files in openFull(info: info, files: files) }
                .onPreferenceChange(QuickCarryKey.self) { c in MainActor.assumeIsolated { carry = c } }
            // (1.3.73) the grip along the foot: dragged, the panel comes out into a window of its own (ToolFloat.swift)
            PullOutGrip(kind: kind, engine: engine, close: close)
                .padding(.bottom, -10)
        }
        .padding(16)
        .frame(width: kind.popoverWidth)
        .task { await pressForPicture() }
    }

    /// (1.3.83) Open Full Screen: the popover closed, the tool whole over the window with what was begun here.
    private func openFullScreen() {
        close()
        tools.openFullScreen(kind, engine: engine, carry: carry)
    }

    /// The whole tool (1.2.1) with what the quick version hands it (a set to study, a link to cite, a file): over the
    /// window where you are — (1.3.79) in its popup, which never blocks the window (and comes up over a quiz or an Alta
    /// assignment's popup too) — not on the Tools page; (1.3.83) made afresh to take it if it is open already.
    private func openFull(info: [String: String] = [:], files: [URL] = []) {
        close()
        tools.popUp(kind, engine: engine, files: files, info: info, afresh: !info.isEmpty || !files.isEmpty)
    }

    /// (the screenshot suite: -SimplPinFull need presses Open Full Screen a moment after the popover opens — the graph's
    /// once Desmos has loaded and drawn what -SimplPinGraph put in it — once a launch)
    @MainActor
    private func pressForPicture() async {
        guard UserDefaults.standard.string(forKey: "SimplPinFull") == kind.rawValue, !Self.launchFullPressed else { return }
        if kind == .graph {
            var waited = 0
            while GraphWeb.mini.loading && waited < 60 {
                try? await Task.sleep(nanoseconds: 500_000_000)
                waited += 1
            }
            try? await Task.sleep(nanoseconds: 4_000_000_000)
        } else {
            try? await Task.sleep(nanoseconds: 2_000_000_000)
        }
        guard !Task.isCancelled, !Self.launchFullPressed else { return }
        Self.launchFullPressed = true
        openFullScreen()
    }
}

/// (tools-21) A quick panel's name, as the tool's tile says it: (1.3.83) the panel's title, no longer a button — Open
/// Full Screen beside it opens the whole tool, where a press on the name and its › did the same (tools-22: one way).
struct QuickPanelName: View {
    let kind: ToolKind

    var body: some View {
        Text(kind.name)
            .font(.sHeadline)
            .lineLimit(1)
            .minimumScaleFactor(0.85)
            .accessibilityAddTraits(.isHeader)
    }
}

/// (1.3.83) A quick panel's Open Full Screen, at its head's right end (the pin's popover's and its pulled-out panel's):
/// the tool's whole view, as its tile on the Tools page opens it — its popup over the window, spanning it, over the
/// sidebar — the panel closed and what was begun in it carried over. It took Pop Out's place (the owner: "it should be
/// full screen").
struct QuickFullScreenButton: View {
    let kind: ToolKind
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Label("Open Full Screen", systemImage: "arrow.up.left.and.arrow.down.right")
                .font(.sCallout.weight(.medium))
                .labelStyle(.iconOnly)
        }
        .glassButton()
        .controlSize(.small)
        .help("Open Full Screen")
        .accessibilityLabel("Open Full Screen")
        .accessibilityHint("Opens \(kind.name) whole, over the window, with what you began here")
    }
}

// MARK: - (1.3.83) What a quick panel carries to the whole tool

/// What was begun in a quick panel, for the whole tool as its Open Full Screen opens it: the details the tool's hand-offs
/// take (`ToolsCenter.takeInfo`: Grade needed's numbers, the element searched for, a link to cite, a unit and its value,
/// a codon, a cross…) and the calculator's line with its sums (`CalcEngine.handOff`). Empty — nothing begun, or the
/// panel as it opens — the whole tool opens as its tile opens it. Each quick version says what it carries as it changes
/// (`quickCarry`); the graph's drawing goes over by Desmos's own state (GraphWeb.carryOver), and the focus timer,
/// Flashcards' sets and the lab's numbers are the whole tool's already.
struct QuickCarry: Equatable {
    var info: [String: String] = [:]
    var calc: CalcEngine?

    var isEmpty: Bool { info.isEmpty && calc == nil }
}

struct QuickCarryKey: PreferenceKey {
    static let defaultValue = QuickCarry()

    static func reduce(value: inout QuickCarry, nextValue: () -> QuickCarry) {
        let next = nextValue()
        value.info.merge(next.info) { _, new in new }
        if let c = next.calc { value.calc = c }
    }
}

extension View {
    /// (1.3.83) What this quick version carries to the whole tool, were its panel opened full screen now (QuickCarry):
    /// empty while nothing has been begun in it.
    func quickCarry(_ info: [String: String]) -> some View {
        preference(key: QuickCarryKey.self, value: QuickCarry(info: info))
    }
}

extension ToolsCenter {
    /// (1.3.83) A quick panel's Open Full Screen: the tool's whole view in its popup over the window — `popUp`, as its tile
    /// opens it: spanning the window over the sidebar, or at the size the student last gave it — taking what was begun in
    /// the panel (`carry`); with something carried, the tool's popup, if it is open already, is made afresh to take it.
    /// The graph's drawing goes over by Desmos's own state (GraphWeb.carryOver).
    func openFullScreen(_ k: ToolKind, engine: Engine, carry: QuickCarry) {
        if k == .graph { GraphWeb.carryOver() }
        if let c = carry.calc { CalcEngine.handOff = c }
        popUp(k, engine: engine, info: carry.info, afresh: !carry.isEmpty)
        if UserDefaults.standard.string(forKey: "SimplShotFile") != nil {
            let what = carry.info.keys.sorted() + (carry.calc == nil ? [] : ["calculator"])
            NSLog("[Simpl] pin full screen: %@ (carried: %@)", k.rawValue, what.isEmpty ? "nothing" : what.joined(separator: ", "))
        }
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
        case .ptable, .lewis, .molecule, .atoms, .balance:
            // (1.3.80) the chemistry tools': Chemistry/ChemTools.swift
            ChemQuick(kind: kind) { openFull($0, []) }
        case .motion, .vectors, .circuit:
            // (tools) the physics tools': Physics/PhysTools.swift
            PhysQuick(kind: kind) { openFull($0, []) }
        case .cite: CitationCompact { openFull($0, []) }
        case .fc: FlashcardsCompact { openFull($0, []) }
        case .graph: GraphCompact()
        case .units: UnitConverterCompact()
        case .words: WordCounterCompact() // (tools) a small text area and its counts
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

// MARK: - (1.3.79) The toolbar's room for pins

/// (1.3.79) How many pins a window's toolbar shows: every pin that fits, measured as the toolbar is laid out — where
/// tools-6 showed three at most whatever the room (two pins, the Tools pull-down and a wide search field, in a wide
/// window). Only the pins that do not fit go into the pull-down, from the end (ToolsCenter.toolbarSplit), measured again
/// as the window is resized, the sidebar shown or hidden, or another screen comes up:
/// - With the search field (the main window, a window more), the field gives its room first: as many pins are shown as
///   leave it `searchRoom` points. The toolbar narrows the field as its items grow, so the field's width says what room
///   is left; once the toolbar folds it into a button for want of room, a pin folds, one at a time, until it is a field
///   again — before anything of the window's own would go into the toolbar's » menu.
/// - Without one (a tool window), a pin more is shown while none of them, and nothing of the window's own after them,
///   has gone into the » menu; when one has, the last pin folds.
/// Back, Forward, Refresh and the title come before the pins and are never what makes way. A count folded to is held as
/// the most for that width (so a toolbar never goes back and forth between two counts) until the window is a pin's room
/// wider, another screen comes up, or the pins or the timer change. The pins are measured by an unseen view in each
/// (ToolbarFitProbe); until they have been, the toolbar holds three at most, as before.
@MainActor
final class ToolbarFit: ObservableObject {
    /// What a measured toolbar item is.
    enum Role: Equatable {
        /// A pin, or the Tools pull-down with the pins past the room.
        case pin, more
        /// Something else of the window's own after the pins, which a pin must not push into the » menu (a tool
        /// window's Dark Page).
        case own
    }

    /// How many pins are shown (nil: not measured yet).
    @Published private(set) var shown: Int?
    /// The toolbar has the search field, which gives its room first.
    private let searching: Bool
    /// The narrowest the pins leave the search field: wide enough for its prompt ("Search or type /") whole.
    static let searchRoom: CGFloat = 140
    /// A pin's room in the toolbar (from one pin to the next), and the Tools pull-down's: as last measured.
    private var pitch: CGFloat = 40
    private var menu: CGFloat = 60
    /// The most pins found to fit at a window this wide (or narrower).
    private var cap: (count: Int, width: CGFloat)?
    /// The pins and the timer at the last measure.
    private var lastTotal: Int?
    private var lastTimer: Bool?
    private let probes = NSHashTable<ToolbarFitProbe>.weakObjects()
    /// A probe has been seen in its window: the measures work here (until then nothing is taken as being in the » menu).
    private var seen = false
    private weak var window: NSWindow?
    private weak var field: NSSearchField?
    private var resizeWatch: NSObjectProtocol?
    private var fieldWatch: NSObjectProtocol?
    private var pending: Task<Void, Never>?

    init(searching: Bool) {
        self.searching = searching
    }

    /// The pins split for this toolbar: as many as fit shown, the rest in the Tools pull-down.
    func split(timerActive: Bool) -> (shown: [ToolKind], more: [ToolKind]) {
        let tools = ToolsCenter.shared
        return tools.toolbarSplit(timerActive: timerActive, fit: count(of: tools.toolbarPins(timerActive: timerActive).count))
    }

    /// How many of `total` pins are shown: as measured, else three at most (two and the pull-down past that).
    private func count(of total: Int) -> Int {
        if let shown { return min(shown, total) }
        return total <= ToolsCenter.toolbarRoom ? total : ToolsCenter.toolbarRoom - 1
    }

    /// Another screen, or the sidebar shown or hidden: the toolbar's items and room are new — measured afresh.
    func reset() {
        cap = nil
        schedule()
    }

    fileprivate func add(_ probe: ToolbarFitProbe) {
        probes.add(probe)
        schedule()
    }

    fileprivate func remove(_ probe: ToolbarFitProbe) {
        probes.remove(probe)
        schedule()
    }

    fileprivate func moved(_ probe: ToolbarFitProbe) {
        if let w = probe.window {
            seen = true
            // (the window whose toolbar it is: in full screen the toolbar is shown in a window of its own, without one)
            if w !== window, w.toolbar != nil { watch(w) }
        }
        schedule()
    }

    private func watch(_ w: NSWindow) {
        window = w
        if let resizeWatch { NotificationCenter.default.removeObserver(resizeWatch) }
        resizeWatch = NotificationCenter.default.addObserver(forName: NSWindow.didResizeNotification, object: w, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.schedule() }
        }
    }

    /// Measured on the run loop a moment later, once the toolbar has been laid out (once for many changes at a time).
    func schedule() {
        guard pending == nil else { return }
        pending = Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 120_000_000)
            self?.pending = nil
            self?.measure()
        }
    }

    private func measure() {
        guard let window, window.isVisible, let toolbar = window.toolbar, toolbar.isVisible else { return }
        let timerActive = FocusTimer.shared.active
        let tools = ToolsCenter.shared
        let total = tools.toolbarPins(timerActive: timerActive).count
        guard total > 0 else { return }
        let now = count(of: total)
        let width = window.frame.width
        if let c = cap, width > c.width + pitch { cap = nil } // (a pin's room wider: tried again)
        // (a pin more or fewer, or the timer started or stopped — its pin wider or gone: tried again too)
        if total != lastTotal || timerActive != lastTimer { cap = nil }
        lastTotal = total
        lastTimer = timerActive
        let live = probes.allObjects

        // a pin, the pull-down or something of the window's own after them in the toolbar's » menu: the last pin folds
        if seen {
            let showing = Set(tools.toolbarSplit(timerActive: timerActive, fit: now).shown)
            var young = false
            let out = live.contains { p in
                let expected: Bool
                switch p.role {
                case .pin: expected = p.kind.map { showing.contains($0) } ?? false
                case .more: expected = now < total
                case .own: expected = true
                }
                guard expected, p.window == nil || p.isHiddenOrHasHiddenAncestor else { return false }
                if p.wasIn || p.born.timeIntervalSinceNow < -0.4 { return true }
                young = true // (just made, not yet put in place: looked at again in a moment)
                return false
            }
            if out {
                if now > 0 { fold(to: now - 1, width: width) }
                return
            }
            if young {
                schedule() // (nothing more shown until it has been seen in place, or not)
                return
            }
        }

        if searching {
            guard let f = searchField(in: window) else { return }
            if f.window == nil || f.isHiddenOrHasHiddenAncestor || f.frame.width < 60 {
                // (the field folded into a button for want of room: a pin folds, one at a time, until it is a field again)
                if now > 0 { fold(to: now - 1, width: width) }
                return
            }
            // the room the pins and the field share now, and as many pins as leave the field its room: a pin's room is
            // measured from two pins or more (where one is from the next) and kept, so the count asked for one way is
            // the count measured the other
            let pins = live.filter { $0.role == .pin && $0.window != nil }.map { $0.convert($0.bounds, to: nil) }.sorted { $0.minX < $1.minX }
            if pins.count >= 2, let a = pins.first, let b = pins.last {
                pitch = min(max((b.minX - a.minX) / CGFloat(pins.count - 1), 28), 120)
            }
            if let m = live.first(where: { $0.role == .more && $0.window != nil }) { menu = min(max(m.bounds.width + 12, 30), 120) }
            let step = pitch, pull = menu
            func need(_ n: Int) -> CGFloat { CGFloat(n) * step + (n < total ? pull : 0) }
            let budget = f.frame.width - Self.searchRoom + need(now)
            var best = 0
            for n in stride(from: total, through: 0, by: -1) where need(n) <= budget - (n > now ? 6 : 0) {
                best = n
                break
            }
            if let c = cap { best = min(best, c.count) }
            if best < now { fold(to: best, width: width) } else { set(best) }
        } else {
            // (no search field: one more while nothing has gone into the » menu)
            guard seen, now < total, cap.map({ now < $0.count }) ?? true else { return }
            set(now + 1)
        }
    }

    /// The window's toolbar search field (watched: as it narrows or widens, the room is measured again) — the search
    /// item's, else one in an item's view (as SearchField finds it where SwiftUI makes no search item).
    private func searchField(in window: NSWindow) -> NSSearchField? {
        let items = window.toolbar?.items ?? []
        func find(_ views: [NSView]) -> NSSearchField? {
            for v in views {
                if let f = v as? NSSearchField { return f }
                if let f = find(v.subviews) { return f }
            }
            return nil
        }
        guard let f = items.lazy.compactMap({ $0 as? NSSearchToolbarItem }).first?.searchField ?? find(items.compactMap(\.view)) else { return nil }
        if f !== field {
            field = f
            if let fieldWatch { NotificationCenter.default.removeObserver(fieldWatch) }
            f.postsFrameChangedNotifications = true
            fieldWatch = NotificationCenter.default.addObserver(forName: NSView.frameDidChangeNotification, object: f, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.schedule() }
            }
        }
        return f
    }

    /// One pin fewer, held as the most for this width.
    private func fold(to n: Int, width: CGFloat) {
        cap = (n, width)
        set(n)
    }

    private func set(_ n: Int) {
        guard shown != n else { return }
        shown = n
        schedule() // (measured again once the toolbar has been laid out with it)
    }
}

/// (1.3.79) An unseen view in a toolbar item (a pin, the Tools pull-down, something of the window's own), saying to its
/// window's ToolbarFit where it is — and, out of its window, that the toolbar has put its item into the » menu.
final class ToolbarFitProbe: NSView {
    weak var fit: ToolbarFit?
    var role: ToolbarFit.Role = .pin
    var kind: ToolKind?
    /// When it was made, and whether it has been in its window (one never put there is in the » menu from the start).
    let born = Date()
    private(set) var wasIn = false

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window != nil { wasIn = true }
        fit?.moved(self)
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

struct ToolbarFitMark: NSViewRepresentable {
    let fit: ToolbarFit
    let role: ToolbarFit.Role
    let kind: ToolKind?

    func makeNSView(context: Context) -> ToolbarFitProbe {
        let v = ToolbarFitProbe()
        v.fit = fit
        v.role = role
        v.kind = kind
        fit.add(v)
        return v
    }

    func updateNSView(_ v: ToolbarFitProbe, context: Context) {
        v.role = role
        v.kind = kind
    }

    static func dismantleNSView(_ v: ToolbarFitProbe, coordinator: ()) {
        v.fit?.remove(v)
    }
}

extension View {
    /// (1.3.79) This toolbar item measured for its window's ToolbarFit (`kind`: the pin's tool).
    func toolbarFit(_ fit: ToolbarFit, _ role: ToolbarFit.Role, _ kind: ToolKind? = nil) -> some View {
        background(ToolbarFitMark(fit: fit, role: role, kind: kind).allowsHitTesting(false))
    }
}

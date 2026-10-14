import AppKit
import SwiftUI

// The Mac tour (1.2.1). One thing at a time is lit, the whole window dimmed round it — the toolbar too, its buttons
// resting while it is on (home-28) — with a short card beside it that says what it is. (home-2, home-9) Six steps, from
// the Dashboard — the counters, Grades, a course, Tools, Search — to where a bug is reported, the same six as on
// Windows: a step may suggest something to try (click Grades) and moves on once it is done, but Next always works; Skip
// Tour, the card's ✕ or Escape end it, and that counts as seen. Each lit thing says where it is itself, measured in the
// window (TourProbe), so the light sits exactly on it however the window is laid out. It comes once, after the app's
// own window first appears and nothing else is over it; Help ▸ Take the Tour, or /tour in the search field, runs it
// again.

/// Where the tour is (nil: not on), and what was just done that a step may be waiting for.
@MainActor
final class MacTour: ObservableObject {
    static let shared = MacTour()
    @Published private(set) var step: Int?
    /// The last thing done that a step may wait for, and a count, so the same thing done twice is seen twice.
    @Published private(set) var event: TourEvent?
    @Published private(set) var events = 0

    func start(at index: Int = 0) {
        AppModel.shared.engine?.hostView.window?.makeKeyAndOrderFront(nil)
        event = nil
        step = max(0, index)
    }

    func show(_ index: Int) {
        event = nil
        step = index
    }

    /// Ended — its last step done, or (home-2) left early with Skip Tour, its ✕ or Escape: seen either way, so not
    /// again on its own (Help ▸ Take the Tour brings it back).
    func finish() {
        step = nil
        UserDefaults.standard.set(true, forKey: "tour:done")
    }

    /// Something a step may be waiting for, done somewhere in the window (nothing, while the tour is off).
    func did(_ e: TourEvent) {
        guard step != nil else { return }
        event = e
        events += 1
    }
}

/// What a step may wait for that the window's own state does not show.
enum TourEvent: Equatable {
    case ringHover, whatIfOn, whatIfEdited, whatIfOff, courseHover, pinned, counterOpened
}

/// The parts of the window the tour lights. (Some are no longer lit since the tour was cut to six steps — the What-If
/// switch, a ring, a pin — and still say where they are, for a tour that wants them again.)
enum TourSpot: Hashable {
    case overlay, sidebar, account, dashboardRow, gradesRow, toolsRow, firstCourse, counters
    case gradeRing, gradeDetails, whatIfSwitch, whatIfScore, toolPin, counterNext, counterPanel
    case preview // (1.2.3) a piece of work's quick look (Shell/WorkPreviewOverlay.swift)
}

// MARK: - Where each part is

/// Each lit part's view, as it says it is there (TourProbe), and where it is now in its window — measured by AppKit, so
/// a row of the sidebar's list (drawn in a view of its own) is placed as exactly as anything else.
@MainActor
final class TourFrames {
    static let shared = TourFrames()

    private final class Weak {
        weak var view: NSView?
        init(_ view: NSView) { self.view = view }
    }

    private var views: [TourSpot: [Weak]] = [:]

    func add(_ spot: TourSpot, _ view: NSView) {
        var list = (views[spot] ?? []).filter { $0.view != nil && $0.view !== view }
        list.append(Weak(view))
        views[spot] = list
    }

    func remove(_ spot: TourSpot, _ view: NSView) {
        views[spot] = views[spot]?.filter { $0.view != nil && $0.view !== view }
    }

    /// Where a part is, in its window's content from the top left — the first of its kind on screen (the topmost, then
    /// the leftmost), cut to what its scroll view shows; nil if none is.
    func rect(_ spot: TourSpot, in window: NSWindow) -> CGRect? {
        var best: CGRect?
        for w in views[spot] ?? [] {
            guard let v = w.view, v.window === window, !v.isHiddenOrHasHiddenAncestor else { continue }
            var r = v.convert(v.bounds, to: nil)
            if let scroll = v.enclosingScrollView {
                r = r.intersection(scroll.convert(scroll.bounds, to: nil))
            }
            guard !r.isNull, r.width >= 2, r.height >= 2 else { continue }
            let f = TourFrames.flip(r, window)
            if let b = best, b.minY < f.minY - 1 || (abs(b.minY - f.minY) <= 1 && b.minX <= f.minX) { continue }
            best = f
        }
        return best
    }

    /// The toolbar's search field, where it is in the window.
    static func searchField(in window: NSWindow) -> CGRect? {
        for item in window.toolbar?.items ?? [] {
            if let s = item as? NSSearchToolbarItem, s.searchField.window === window {
                return flip(s.searchField.convert(s.searchField.bounds, to: nil), window)
            }
        }
        return nil
    }

    /// Window coordinates (from the bottom left) to the content's (from the top left).
    static func flip(_ r: CGRect, _ window: NSWindow) -> CGRect {
        let h = window.contentView?.frame.height ?? window.frame.height
        return CGRect(x: r.minX, y: h - r.maxY, width: r.width, height: r.height)
    }
}

/// An unseen view under a lit part, saying where it is (it takes no presses).
final class TourProbeView: NSView {
    var spot: TourSpot? {
        didSet {
            guard oldValue != spot else { return }
            if let o = oldValue { TourFrames.shared.remove(o, self) }
            register()
        }
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        register()
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    private func register() {
        guard let spot else { return }
        if window != nil { TourFrames.shared.add(spot, self) } else { TourFrames.shared.remove(spot, self) }
    }
}

struct TourProbe: NSViewRepresentable {
    let spot: TourSpot

    func makeNSView(context: Context) -> TourProbeView {
        let v = TourProbeView()
        v.spot = spot
        return v
    }

    func updateNSView(_ v: TourProbeView, context: Context) { v.spot = spot }
}

extension View {
    /// This part of the window, for the tour to light (nil: none).
    func tourSpot(_ spot: TourSpot?) -> some View {
        background {
            if let spot { TourProbe(spot: spot).allowsHitTesting(false) }
        }
    }
}

// MARK: - The steps

/// A step: what it lights, what it says, and what it suggests trying (it moves on once that is done; Next never waits
/// for it).
struct TourStep: Identifiable {
    enum Aim { case none, spot(TourSpot), search, back }
    enum Act {
        case next
        case place((Place, Bool) -> Bool) // (the place shown, whether something is pushed on it)
        case event(TourEvent)
        case typed
        case opened
        case closed
    }
    enum Gesture { case click, hover, type }

    let id: String
    let aim: Aim
    let title: String
    let body: Text
    var doing: String? = nil
    var gesture: Gesture = .click
    var act: Act = .next
    var nextLabel = "Next"
}

/// Where the light, the card and its arrow go for a step, in the tour's own coordinates.
struct TourPlan {
    var hole: CGRect?
    var card: CGPoint // (the card's box, without its arrow)
    var arrow: TourArrow
    var arrowAt: CGFloat // (along the arrow's edge, from the box's leading or top edge)

    private static let gap: CGFloat = 16
    private static let margin: CGFloat = 14

    /// Beside the lit place: to its right when it is on the window's leading side (the sidebar), else under it, over
    /// it, or to its right; under a toolbar item, at the top, its arrow up at it; else in the middle.
    static func make(hole: CGRect?, toolbarX: CGFloat?, size: CGSize, top: CGFloat, card: CGSize) -> TourPlan {
        if let x = toolbarX {
            let cx = clamp(x - card.width / 2, margin, size.width - card.width - margin)
            return TourPlan(hole: nil, card: CGPoint(x: cx, y: top - 4), arrow: .top, arrowAt: x - cx) // (1.2.16: up, its point at the field)
        }
        guard let hole else {
            return TourPlan(hole: nil, card: CGPoint(x: max(margin, (size.width - card.width) / 2), y: max(margin, (size.height - card.height) / 2)), arrow: .none, arrowAt: 0)
        }
        let leading = hole.maxX < size.width * 0.4
        let sides: [TourArrow] = leading ? [.leading, .top, .bottom] : [.top, .bottom, .leading]
        for side in sides {
            switch side {
            case .leading where hole.maxX + gap + card.width <= size.width - margin:
                let y = clamp(hole.midY - card.height / 2, top + margin, size.height - card.height - margin)
                return TourPlan(hole: hole, card: CGPoint(x: hole.maxX + gap, y: y), arrow: .leading, arrowAt: hole.midY - y)
            case .top where hole.maxY + gap + card.height <= size.height - margin:
                let x = clamp(hole.midX - card.width / 2, margin, size.width - card.width - margin)
                return TourPlan(hole: hole, card: CGPoint(x: x, y: hole.maxY + gap), arrow: .top, arrowAt: hole.midX - x)
            case .bottom where hole.minY - gap - card.height >= top + margin:
                let x = clamp(hole.midX - card.width / 2, margin, size.width - card.width - margin)
                return TourPlan(hole: hole, card: CGPoint(x: x, y: hole.minY - gap - card.height), arrow: .bottom, arrowAt: hole.midX - x)
            default:
                continue
            }
        }
        // (no side has room: inside the window's foot, clear of the light where it can be)
        let y = hole.midY > size.height / 2 ? top + margin : size.height - card.height - margin
        return TourPlan(hole: hole, card: CGPoint(x: max(margin, (size.width - card.width) / 2), y: y), arrow: .none, arrowAt: 0)
    }

    private static func clamp(_ v: CGFloat, _ lo: CGFloat, _ hi: CGFloat) -> CGFloat { min(max(v, lo), max(lo, hi)) }
}

/// Which way the card's arrow points (from the card's edge to the thing).
enum TourArrow: Equatable { case none, top, bottom, leading }

// MARK: - The overlay

/// The tour over the window (MainShell puts it over the split view). Nothing is drawn, and nothing is held, while it
/// is off.
struct TourOverlay: View {
    @Binding var columns: NavigationSplitViewVisibility
    @EnvironmentObject private var engine: Engine
    @ObservedObject private var tour = MacTour.shared
    @ObservedObject private var preview = WorkPreview.shared
    @AppStorage("tour:done") private var done = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var cardSize = CGSize(width: TourCard.width, height: 170)
    @State private var nudged = false
    @State private var sawSetup = false
    @State private var advancing = false

    var body: some View {
        GeometryReader { proxy in
            ZStack(alignment: .topLeading) {
                TourProbe(spot: .overlay).allowsHitTesting(false)
                let list = steps
                if let i = tour.step, i < list.count {
                    TimelineView(.periodic(from: .now, by: 0.2)) { time in
                        let placed = self.plan(for: list[i], size: proxy.size, top: proxy.safeAreaInsets.top)
                        TourStage(step: list[i], index: i, count: list.count, plan: placed, size: proxy.size, cardSize: $cardSize, nudged: nudged,
                                  next: { next(list.count) }, back: back, skip: skip, poke: poke)
                            .onChange(of: time.date) { _, _ in check(list[i], count: list.count) }
                    }
                    .transition(.opacity)
                }
            }
        }
        // (home-28) over the whole window, the toolbar's strip as well: its coordinates start at the window's top, and
        // `safeAreaInsets.top` is the toolbar's height, which the cards keep clear of
        .ignoresSafeArea()
        .allowsHitTesting(tour.step != nil)
        .animation(reduceMotion ? nil : Motion.gentle, value: tour.step)
        .onChange(of: engine.setup) { _, on in if on { sawSetup = true } }
        .onChange(of: tour.step) { old, new in arrive(new, from: old) }
        .onChange(of: tour.events) { _, _ in
            let list = steps
            if let i = tour.step, i < list.count { check(list[i], count: list.count) }
        }
        .task { await autoStart() }
    }

    // MARK: The steps (the same six as Windows': windows/SimplFluent/Shell/Tour.cs)

    private var steps: [TourStep] {
        var s: [TourStep] = [
            TourStep(id: "dashboard", aim: .spot(.counters), title: "Everything due, at a glance",
                     body: Text("Each counter opens what it counts: work due today, tomorrow and in the next 7 days, anything overdue, new announcements and this week’s grades.")),
            TourStep(id: "grades", aim: .spot(.gradesRow), title: "Grades", body: Text("All your grades and your GPA in one place."),
                     doing: "Click Grades to look", act: .place { p, _ in p == .grades }),
        ]
        if !engine.courses.isEmpty {
            s.append(TourStep(id: "courses", aim: .spot(.firstCourse), title: "Your courses",
                              body: Text("Each course opens to its home, with its sections under it here.")))
        }
        s += [
            TourStep(id: "tools", aim: .spot(.toolsRow), title: "Tools",
                     body: Text("A PDF editor, a file converter, calculators, flashcards, a citation maker and more. Pin one to keep it in the toolbar."),
                     doing: "Click Tools to look", act: .place { p, _ in p == .tools }),
            TourStep(id: "search", aim: .search, title: "Search everything", body: Text("Find anything in your courses. Type / for commands, or press ⌘K.")),
            // (home-9) the way to report a bug last, where it is a help rather than the first thing said
            TourStep(id: "report", aim: .spot(.account), title: "Something wrong? Tell us here",
                     body: Text("Report a bug or ask for something new from this menu. Take this tour again from Help ▸ Take the Tour."),
                     nextLabel: "Done"),
        ]
        return s
    }

    // MARK: Placing it

    private func plan(for step: TourStep, size: CGSize, top: CGFloat) -> TourPlan {
        guard let window = engine.hostView.window, let me = TourFrames.shared.rect(.overlay, in: window) else {
            return TourPlan.make(hole: nil, toolbarX: nil, size: size, top: top, card: cardSize)
        }
        let local = { (r: CGRect) in r.offsetBy(dx: -me.minX, dy: -me.minY) }
        switch step.aim {
        case .none:
            return TourPlan.make(hole: nil, toolbarX: nil, size: size, top: top, card: cardSize)
        case .spot(let spot):
            let bounds = CGRect(origin: .zero, size: size)
            let hole = TourFrames.shared.rect(spot, in: window).map { local($0).insetBy(dx: -6, dy: -6) }.flatMap { bounds.intersects($0) ? $0 : nil }
            return TourPlan.make(hole: hole, toolbarX: nil, size: size, top: top, card: cardSize)
        case .search:
            let x = TourFrames.searchField(in: window).map { local($0).midX } ?? (size.width - 150)
            return TourPlan.make(hole: nil, toolbarX: x, size: size, top: top, card: cardSize)
        case .back:
            // (Back and Forward, where the detail's toolbar starts: just past the sidebar)
            let edge = TourFrames.shared.rect(.sidebar, in: window).map { local($0).maxX } ?? 0
            return TourPlan.make(hole: nil, toolbarX: edge + 34, size: size, top: top, card: cardSize)
        }
    }

    // MARK: Moving on

    /// The step's thing done: on to the next, a moment later (the press seen landing first).
    private func check(_ step: TourStep, count: Int) {
        guard !advancing, let i = tour.step else { return }
        if case .next = step.act { return }
        let here = engine.nav.current
        let pushed = !here.path.isEmpty
        let did: Bool
        switch step.act {
        case .next: did = false
        case .place(let test): did = test(here.place, pushed)
        case .event(let e): did = tour.event == e
        case .typed: did = !engine.query.trimmingCharacters(in: .whitespaces).isEmpty
        case .opened: did = pushed || engine.quiz != nil || preview.item != nil
        case .closed: did = !pushed && engine.quiz == nil && preview.item == nil
        }
        guard did else { return }
        advancing = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.45) {
            advancing = false
            guard tour.step == i else { return }
            next(count)
        }
    }

    private func next(_ count: Int) {
        guard let i = tour.step else { return }
        if i + 1 >= count { tour.finish() } else { tour.show(i + 1) }
    }

    private func back() {
        guard let i = tour.step, i > 0 else { return }
        tour.show(i - 1)
    }

    /// (home-2) Left before its end: over, and seen.
    private func skip() {
        guard tour.step != nil else { return }
        tour.finish()
    }

    /// A press outside the light: the card nudges, so the eye goes to it.
    private func poke() {
        guard !reduceMotion else { return }
        withAnimation(Motion.snappy) { nudged = true }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.16) { withAnimation(Motion.snappy) { nudged = false } }
    }

    /// A step arriving: the sidebar shown; the tour begun from the Dashboard, as the web's is.
    private func arrive(_ step: Int?, from old: Int?) {
        guard let step else { return }
        if old == nil {
            if columns != .all { columns = .all }
            SearchField.resign(engine)
            if step == 0, engine.nav.current.place != .dashboard || !engine.nav.current.path.isEmpty { engine.go(.dashboard) }
        }
        if step >= steps.count { tour.finish() }
    }

    /// Once, after the window first appears and nothing else is over it for a moment. (The screenshot suite, whose
    /// student has used the app, never sees it on its own: -SimplOpen tour, or tour:4 for the fourth step, shows it.)
    private func autoStart() async {
        if let at = LaunchOpen.take("tour") {
            try? await Task.sleep(nanoseconds: 3_000_000_000)
            let n = Int(at.trimmingCharacters(in: CharacterSet(charactersIn: ": "))) ?? 1
            tour.start(at: n - 1)
            return
        }
        guard !done, !UserDefaults.standard.bool(forKey: "SimplDemo") else { return }
        var calm = 0
        var ticks = 0
        while !Task.isCancelled {
            try? await Task.sleep(nanoseconds: 500_000_000)
            if done || tour.step != nil { return }
            ticks += 1
            // (counted from the sidebar's courses in — What's New is asked for after them — unless they are long in coming)
            let ready = free && (!engine.courses.isEmpty || ticks >= 20)
            calm = ready ? calm + 1 : 0
            if calm >= 5 {
                tour.start()
                return
            }
        }
    }

    /// Nothing over the window, nothing on the way: the app's own screens up, the setup done, no sheet, nothing typed.
    private var free: Bool {
        guard engine.phase == .native, let snap = engine.snapshot else { return false }
        let setUp = snap.setupDone != false || sawSetup
        return setUp && !engine.setup && engine.whatsNew == nil && engine.quiz == nil && !engine.newTask && !engine.newMessage
            && !engine.settingsOpen && engine.query.isEmpty && preview.item == nil
    }
}

/// The dim with its light, and the card.
private struct TourStage: View {
    let step: TourStep
    let index: Int
    let count: Int
    let plan: TourPlan
    let size: CGSize
    @Binding var cardSize: CGSize
    let nudged: Bool
    let next: () -> Void
    let back: () -> Void
    let skip: () -> Void
    let poke: () -> Void
    @FocusState private var focused: Bool

    var body: some View {
        ZStack(alignment: .topLeading) {
            TourVeil(hole: plan.hole)
                // (the light takes presses through to what is under it; the rest of the window nudges the card)
                .contentShape(TourHoleShape(hole: plan.hole), eoFill: true)
                .onTapGesture(perform: poke)
            TourCard(step: step, index: index, count: count, arrow: plan.arrow, arrowAt: plan.arrowAt, cardSize: $cardSize, next: next, back: back, skip: skip)
                .scaleEffect(nudged ? 1.025 : 1)
                .offset(x: plan.card.x - (plan.arrow == .leading ? CalloutMetrics.length : 0),
                        y: plan.card.y - (plan.arrow == .top ? CalloutMetrics.length : 0))
                .animation(Motion.gentle, value: plan.card)
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
        // the keys: Return and → on, ← back, (home-2) Escape ends it
        .focusable(true, interactions: .edit)
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(phases: .down) { press in key(press) }
        .task(id: index) {
            try? await Task.sleep(nanoseconds: 80_000_000) // (once the window has let go of what had the keys)
            focused = true
        }
    }

    private func key(_ press: KeyPress) -> KeyPress.Result {
        guard press.modifiers.isDisjoint(with: [.command, .control, .option]) else { return .ignored }
        switch press.key {
        case .rightArrow, .return:
            next()
            return .handled
        case .leftArrow:
            back()
            return .handled
        case .escape:
            skip()
            return .handled
        default:
            return .ignored
        }
    }
}

/// The window, less the light (for presses: even-odd, so the light lets them through).
private struct TourHoleShape: Shape {
    let hole: CGRect?

    func path(in r: CGRect) -> Path {
        var p = Path(r)
        if let hole { p.addRoundedRect(in: hole, cornerSize: CGSize(width: 14, height: 14), style: .continuous) }
        return p
    }
}

/// The window dimmed but for the lit place, which is ringed in the accent.
private struct TourVeil: View {
    let hole: CGRect?
    private static let dim = Theme.dynamic(light: NSColor(white: 0, alpha: 0.28), dark: NSColor(white: 0, alpha: 0.5))

    var body: some View {
        ZStack(alignment: .topLeading) {
            Rectangle().fill(TourVeil.dim)
            if let hole {
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .frame(width: hole.width, height: hole.height)
                    .offset(x: hole.minX, y: hole.minY)
                    .blendMode(.destinationOut)
            }
        }
        .compositingGroup()
        .overlay(alignment: .topLeading) {
            if let hole {
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .strokeBorder(Theme.accent, lineWidth: 2)
                    .frame(width: hole.width, height: hole.height)
                    .shadow(color: Theme.accent.opacity(0.45), radius: 8)
                    .offset(x: hole.minX, y: hole.minY)
            }
        }
        .animation(Motion.gentle, value: hole)
        .allowsHitTesting(true)
    }
}

/// The card: its count and its ✕, the title and its line, what to try, a bar of how far along, and (home-2) the same
/// controls on every step — Skip Tour, Back, and a Next that always works (Done on the last).
private struct TourCard: View {
    static let width: CGFloat = 320

    let step: TourStep
    let index: Int
    let count: Int
    let arrow: TourArrow
    let arrowAt: CGFloat
    @Binding var cardSize: CGSize
    let next: () -> Void
    let back: () -> Void
    let skip: () -> Void

    private var last: Bool { index == count - 1 }

    var body: some View {
        content
            .padding(18)
            .frame(width: TourCard.width, alignment: .leading)
            .background {
                GeometryReader { p in
                    Color.clear
                        .onAppear { cardSize = p.size }
                        .onChange(of: p.size) { _, s in cardSize = s }
                }
            }
            .padding(arrowRoom)
            .glass(CalloutShape(arrow: arrow, at: arrowAt))
            .accessibilityElement(children: .contain)
            .accessibilityLabel("Tour, step \(index + 1) of \(count): \(step.title)")
            .accessibilityAddTraits(.isModal)
    }

    private var arrowRoom: EdgeInsets {
        let l = CalloutMetrics.length
        switch arrow {
        case .none: return EdgeInsets()
        case .top: return EdgeInsets(top: l, leading: 0, bottom: 0, trailing: 0)
        case .bottom: return EdgeInsets(top: 0, leading: 0, bottom: l, trailing: 0)
        case .leading: return EdgeInsets(top: 0, leading: l, bottom: 0, trailing: 0)
        }
    }

    private var content: some View {
        VStack(alignment: .leading, spacing: 8) {
            if last {
                // (1.3.76) the setup's seal, bouncing once the card has glided to its place (Motion.gentle, above)
                FinishMark(size: 28, weight: .semibold, delay: 300_000_000)
            }
            HStack(spacing: 8) {
                Text("\(index + 1) of \(count)")
                    .font(.sFootnote.weight(.semibold).monospacedDigit())
                    .foregroundStyle(.secondary)
                Spacer(minLength: 8)
                Button(action: skip) {
                    Image(systemName: "xmark")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundStyle(.secondary)
                        .frame(width: 22, height: 22)
                        .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .keyboardShortcut(.cancelAction) // (Escape ends it wherever the keys are)
                .help("Close the tour (Esc)")
                .accessibilityLabel("Close the tour")
                .padding(.trailing, -6)
            }
            Text(step.title)
                .font(.sHeadline)
            step.body
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if let doing = step.doing { doingLine(doing) }
            footer
        }
    }

    private func doingLine(_ text: String) -> some View {
        HStack(spacing: 7) {
            Image(systemName: symbol)
                .font(.sCallout.weight(.semibold))
                .symbolEffect(.pulse, options: .repeating)
            Text(text)
                .font(.sCallout.weight(.semibold))
        }
        .foregroundStyle(Theme.accent)
        .padding(.top, 2)
        .accessibilityElement(children: .combine)
    }

    private var symbol: String {
        switch step.gesture {
        case .click: return "cursorarrow.click"
        case .hover: return "cursorarrow.rays"
        case .type: return "keyboard"
        }
    }

    private var footer: some View {
        VStack(alignment: .leading, spacing: 10) {
            ProgressView(value: Double(index + 1), total: Double(count))
                .progressViewStyle(.linear)
                .tint(Theme.accent)
                .accessibilityHidden(true)
            HStack(spacing: 8) {
                if !last {
                    Button("Skip Tour", action: skip)
                        .buttonStyle(.plain)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .help("End the tour (Esc). Take it again from Help ▸ Take the Tour.")
                }
                Spacer(minLength: 6)
                if index > 0 {
                    Button("Back", action: back)
                        .glassButton()
                }
                Button(step.nextLabel, action: next) // (Return, as the stage takes its keys)
                    .glassButton(prominent: true)
            }
        }
        .controlSize(.regular)
        .padding(.top, 4)
    }
}

/// The card's arrow: how far it stands out from the box, and half its base.
enum CalloutMetrics {
    static let length: CGFloat = 10
    static let half: CGFloat = 10
}

/// The card's outline: a rounded box with its arrow on one edge, drawn as one shape so the glass is one piece.
struct CalloutShape: Shape {
    let arrow: TourArrow
    let at: CGFloat
    var radius: CGFloat = 20

    func path(in r: CGRect) -> Path {
        let l = CalloutMetrics.length, h = CalloutMetrics.half
        var b = r
        switch arrow {
        case .top:
            b.origin.y += l
            b.size.height -= l
        case .bottom:
            b.size.height -= l
        case .leading:
            b.origin.x += l
            b.size.width -= l
        case .none:
            break
        }
        let rad = max(0, min(radius, b.width / 2, b.height / 2))
        let ax = min(max(b.minX + at, b.minX + rad + h), b.maxX - rad - h)
        let ay = min(max(b.minY + at, b.minY + rad + h), b.maxY - rad - h)
        var p = Path()
        p.move(to: CGPoint(x: b.minX + rad, y: b.minY))
        if arrow == .top {
            p.addLine(to: CGPoint(x: ax - h, y: b.minY))
            p.addLine(to: CGPoint(x: ax, y: r.minY))
            p.addLine(to: CGPoint(x: ax + h, y: b.minY))
        }
        p.addArc(tangent1End: CGPoint(x: b.maxX, y: b.minY), tangent2End: CGPoint(x: b.maxX, y: b.maxY), radius: rad)
        p.addArc(tangent1End: CGPoint(x: b.maxX, y: b.maxY), tangent2End: CGPoint(x: b.minX, y: b.maxY), radius: rad)
        if arrow == .bottom {
            p.addLine(to: CGPoint(x: ax + h, y: b.maxY))
            p.addLine(to: CGPoint(x: ax, y: r.maxY))
            p.addLine(to: CGPoint(x: ax - h, y: b.maxY))
        }
        p.addArc(tangent1End: CGPoint(x: b.minX, y: b.maxY), tangent2End: CGPoint(x: b.minX, y: b.minY), radius: rad)
        if arrow == .leading {
            p.addLine(to: CGPoint(x: b.minX, y: ay + h))
            p.addLine(to: CGPoint(x: r.minX, y: ay))
            p.addLine(to: CGPoint(x: b.minX, y: ay - h))
        }
        p.addArc(tangent1End: CGPoint(x: b.minX, y: b.minY), tangent2End: CGPoint(x: b.maxX, y: b.minY), radius: rad)
        p.closeSubpath()
        return p
    }
}

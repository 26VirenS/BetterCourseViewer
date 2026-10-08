import SwiftUI

// The Mac tour (1.2): the web's guided tour (extension/content/app/welcome.js) as coach marks. The window stays as it is,
// dimmed but for the part being shown — the places in the sidebar, a course and its sections, the Dashboard, the search
// field and its commands, Back and Forward, the account — with a glass callout beside it, its arrow at it, the step's
// count, Back, Next and Skip. Return or → is the next step, ← the one before, Escape ends it. It comes once, after the
// app's own window first appears and nothing else is over it (the setup, What's New); Help ▸ Take the Tour, or /tour in
// the search field, runs it again.

/// Where the tour is (nil: not on).
@MainActor
final class MacTour: ObservableObject {
    static let shared = MacTour()
    @Published private(set) var step: Int?

    func start(at index: Int = 0) {
        AppModel.shared.engine?.hostView.window?.makeKeyAndOrderFront(nil)
        step = max(0, index)
    }

    func show(_ index: Int) { step = index }

    /// Ended (its last step done, or skipped): not again on its own.
    func finish() {
        step = nil
        UserDefaults.standard.set(true, forKey: "tour:done")
    }
}

/// The parts of the window the tour points at.
enum TourSpot: Hashable {
    case sidebar, firstPlace, lastPlace, firstCourse, courseChevron, account, detail
}

/// Where each part is, in the window's own coordinates, as each part says. (A row of the sidebar's list is drawn apart
/// from the window's other views, so a preference from it would not reach the tour; each part says its frame here.)
@MainActor
final class TourFrames: ObservableObject {
    static let shared = TourFrames()
    private(set) var frames: [TourSpot: CGRect] = [:]

    func set(_ spot: TourSpot, _ rect: CGRect?) {
        guard frames[spot] != rect else { return }
        frames[spot] = rect
        if MacTour.shared.step != nil { objectWillChange.send() } // (redrawn only while the tour is on)
    }
}

/// Says where the view it is on is, and again whenever that changes (`tourSpot`).
struct TourSpotReader: ViewModifier {
    let spot: TourSpot?

    func body(content: Content) -> some View {
        content.background {
            if let spot {
                GeometryReader { proxy in
                    let frame = proxy.frame(in: .global)
                    Color.clear
                        .onAppear { TourFrames.shared.set(spot, frame) }
                        .onChange(of: frame) { _, f in TourFrames.shared.set(spot, f) }
                        .onDisappear { TourFrames.shared.set(spot, nil) }
                }
            }
        }
    }
}

extension View {
    /// This part of the window, for the tour to point at (nil: none).
    func tourSpot(_ spot: TourSpot?) -> some View { modifier(TourSpotReader(spot: spot)) }
}

/// A step: what it points at and what it says.
struct TourStep: Identifiable {
    enum Aim { case center, places, course, dashboard, search, history, account }
    let id: String
    let aim: Aim
    let symbol: String
    let title: String
    let body: String
    var keys: [String] = []
}

/// Which way the callout's arrow points (from the callout's edge to the thing).
enum TourArrow: Equatable { case none, top, bottom, leading }

/// Where the light, the callout and its arrow go for a step, in the tour's own coordinates.
struct TourPlan {
    var hole: CGRect?
    var card: CGPoint // (the callout's box, without its arrow)
    var arrow: TourArrow
    var arrowAt: CGFloat // (along the arrow's edge, from the box's leading or top edge)

    enum Side { case right, below, above }

    private static let gap: CGFloat = 18
    private static let margin: CGFloat = 16

    static func make(_ aim: TourStep.Aim, spots: [TourSpot: CGRect], size: CGSize, top: CGFloat, card: CGSize, inbox: Bool) -> TourPlan {
        let bounds = CGRect(origin: .zero, size: size)
        let sidebar = spots[.sidebar].flatMap { $0.width > 40 && $0.intersects(bounds) ? $0 : nil }
        switch aim {
        case .center:
            return centered(size, card)
        case .places:
            guard let sb = sidebar else { return centered(size, card) }
            let hole: CGRect
            if let rows = placeRows(spots, sb) {
                let extra = inbox ? rows.last.height : 0 // (the Inbox, under Notifications)
                hole = CGRect(x: sb.minX + 8, y: rows.first.minY - 4, width: sb.width - 16, height: rows.last.maxY + extra - rows.first.minY + 8)
            } else {
                let y = max(sb.minY, top) + 12
                hole = CGRect(x: sb.minX + 8, y: y, width: sb.width - 16, height: min(230, sb.height / 3))
            }
            return beside(hole, size, top, card, prefer: [.right, .below])
        case .course:
            guard let sb = sidebar else { return centered(size, card) }
            if placeRows(spots, sb) != nil, let row = spots[.firstCourse], row.minX >= sb.minX - 2, row.maxX <= sb.maxX + 2,
               let last = spots[.lastPlace], row.minY > last.maxY {
                let hole = CGRect(x: sb.minX + 8, y: row.minY - 3, width: sb.width - 16, height: row.height + 6)
                let chevron = spots[.courseChevron].flatMap { $0.minY >= hole.minY && $0.maxY <= hole.maxY ? $0 : nil }
                return beside(hole, size, top, card, prefer: [.right, .below], point: chevron.map { CGPoint(x: $0.midX, y: $0.midY) })
            }
            let y = max(sb.minY, top) + 290
            return beside(CGRect(x: sb.minX + 8, y: y, width: sb.width - 16, height: 40), size, top, card, prefer: [.right, .below])
        case .dashboard:
            guard let d = spots[.detail].flatMap({ $0.width > 100 && $0.intersects(bounds) ? $0 : nil }) else { return centered(size, card) }
            let y = max(d.minY, top) + 14
            let hole = CGRect(x: d.minX + 28, y: y, width: d.width - 56, height: max(120, min(300, (d.maxY - y) * 0.42)))
            return beside(hole, size, top, card, prefer: [.below, .right, .above])
        case .search:
            return toolbar(x: size.width - 110, size, top, card) // (the field, at the toolbar's trailing end)
        case .history:
            return toolbar(x: (sidebar?.maxX ?? 70) + 48, size, top, card) // (Back and Forward, where the screen's toolbar starts)
        case .account:
            guard let a = spots[.account].flatMap({ $0.height > 8 && $0.intersects(bounds) ? $0 : nil }) else { return centered(size, card) }
            return beside(a.insetBy(dx: 6, dy: 2), size, top, card, prefer: [.right, .above])
        }
    }

    /// The sidebar's first and last place, when what they said is believable: inside the sidebar, the one well above the other.
    private static func placeRows(_ spots: [TourSpot: CGRect], _ sb: CGRect) -> (first: CGRect, last: CGRect)? {
        guard let a = spots[.firstPlace], let b = spots[.lastPlace],
              a.minX >= sb.minX - 2, b.maxX <= sb.maxX + 2, b.minY - a.minY > 40, a.minY >= sb.minY, b.maxY <= sb.maxY else { return nil }
        return (first: a, last: b)
    }

    private static func clamp(_ v: CGFloat, _ lo: CGFloat, _ hi: CGFloat) -> CGFloat { min(max(v, lo), max(lo, hi)) }

    private static func centered(_ size: CGSize, _ card: CGSize) -> TourPlan {
        TourPlan(hole: nil, card: CGPoint(x: max(margin, (size.width - card.width) / 2), y: max(margin, (size.height - card.height) / 2)), arrow: .none, arrowAt: 0)
    }

    /// Under a toolbar item: the callout at the top of the window, its arrow up at the item.
    private static func toolbar(x: CGFloat, _ size: CGSize, _ top: CGFloat, _ card: CGSize) -> TourPlan {
        let cx = clamp(x - card.width / 2, margin, size.width - card.width - margin)
        return TourPlan(hole: nil, card: CGPoint(x: cx, y: top + 14), arrow: .top, arrowAt: x - cx)
    }

    /// Beside the lit place, on the first side it fits; else at the window's foot, with no arrow.
    private static func beside(_ hole: CGRect, _ size: CGSize, _ top: CGFloat, _ card: CGSize, prefer: [Side], point: CGPoint? = nil) -> TourPlan {
        let aim = point ?? CGPoint(x: hole.midX, y: hole.midY)
        for side in prefer {
            switch side {
            case .right where hole.maxX + gap + card.width <= size.width - margin:
                let y = clamp(aim.y - card.height / 2, top + margin, size.height - card.height - margin)
                return TourPlan(hole: hole, card: CGPoint(x: hole.maxX + gap, y: y), arrow: .leading, arrowAt: aim.y - y)
            case .below where hole.maxY + gap + card.height <= size.height - margin:
                let x = clamp(aim.x - card.width / 2, margin, size.width - card.width - margin)
                return TourPlan(hole: hole, card: CGPoint(x: x, y: hole.maxY + gap), arrow: .top, arrowAt: aim.x - x)
            case .above where hole.minY - gap - card.height >= top + margin:
                let x = clamp(aim.x - card.width / 2, margin, size.width - card.width - margin)
                return TourPlan(hole: hole, card: CGPoint(x: x, y: hole.minY - gap - card.height), arrow: .bottom, arrowAt: aim.x - x)
            default:
                continue
            }
        }
        return TourPlan(hole: hole, card: CGPoint(x: max(margin, (size.width - card.width) / 2), y: max(top + margin, size.height - card.height - margin)), arrow: .none, arrowAt: 0)
    }
}

// MARK: - The overlay

/// The tour over the window (MainShell puts it over the split view). Nothing is drawn, and nothing is held, while it
/// is off.
struct TourOverlay: View {
    @Binding var columns: NavigationSplitViewVisibility
    @EnvironmentObject private var engine: Engine
    @ObservedObject private var tour = MacTour.shared
    @ObservedObject private var frames = TourFrames.shared
    @AppStorage("tour:done") private var done = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var cardSize = CGSize(width: 380, height: 240) // (TourCard.width; its height as measured)
    @State private var nudged = false
    @State private var sawSetup = false

    var body: some View {
        GeometryReader { proxy in
            let list = steps
            if let i = tour.step, i < list.count {
                let origin = proxy.frame(in: .global).origin
                let spots = frames.frames.mapValues { $0.offsetBy(dx: -origin.x, dy: -origin.y) }
                let plan = TourPlan.make(list[i].aim, spots: spots, size: proxy.size, top: proxy.safeAreaInsets.top, card: cardSize, inbox: !engine.onBrightspace)
                TourStage(step: list[i], index: i, count: list.count, plan: plan, size: proxy.size, cardSize: $cardSize, nudged: nudged,
                          next: { next(list.count) }, back: back, skip: { tour.finish() }, poke: poke)
                    .transition(.opacity)
            }
        }
        .allowsHitTesting(tour.step != nil)
        .animation(reduceMotion ? nil : Motion.gentle, value: tour.step)
        .onChange(of: engine.setup) { _, on in if on { sawSetup = true } }
        .onChange(of: tour.step) { old, new in arrive(new, from: old) }
        .task { await autoStart() }
    }

    /// The steps, as this window has them (a course to point at, the Inbox on Canvas).
    private var steps: [TourStep] {
        let lms = engine.lmsName
        var s: [TourStep] = [
            TourStep(id: "hello", aim: .center, symbol: "hand.wave.fill", title: "Welcome to Simpl for Mac",
                     body: "A quick look round: where things are, and the keys that get you there. Press → or Return for the next step, ← to go back, and Escape to end the tour."),
            TourStep(id: "places", aim: .places, symbol: "sidebar.left", title: "Everything in one sidebar",
                     body: engine.onBrightspace ? "The Dashboard, To Do, the Calendar, Grades and Notifications — each a click away, or a key." : "The Dashboard, To Do, the Calendar, Grades, Notifications and the Inbox — each a click away, or a key.",
                     keys: ["⌘1", "⌘2", "⌘3", "⌘4", "⌘5"]),
        ]
        if !engine.courses.isEmpty {
            s.append(TourStep(id: "course", aim: .course, symbol: "chevron.right.circle.fill", title: "Your courses, and what’s in them",
                              body: "Click a course for its home. Its arrow lists its sections — Assignments, Modules, Files and the rest — right here in the sidebar."))
        }
        s.append(TourStep(id: "dashboard", aim: .dashboard, symbol: "square.grid.2x2.fill", title: "The Dashboard",
                          body: "What’s due, what’s new and how your grades stand, at a glance. Click a card to see everything in it.", keys: ["⌘1"]))
        s.append(TourStep(id: "search", aim: .search, symbol: "magnifyingglass", title: "Search — and commands",
                          body: "Find courses, work, files and people in \(lms). Type / for commands: /grades, /course bio files, /due today, /note read chapter 4, /dark.", keys: ["⌘K"]))
        s.append(TourStep(id: "history", aim: .history, symbol: "arrow.left.arrow.right", title: "Back and Forward",
                          body: "Like a browser’s, across the whole window: every place you went and everything you opened.", keys: ["⌘[", "⌘]"]))
        s.append(TourStep(id: "account", aim: .account, symbol: "person.crop.circle", title: "Your account",
                          body: "Settings, What’s New and your \(lms) profile are under the … button here — and Sign Out."))
        s.append(TourStep(id: "done", aim: .center, symbol: "checkmark.seal.fill", title: "You’re all set",
                          body: "Take the tour again any time from Help ▸ Take the Tour, or type /tour in the search field."))
        return s
    }

    private func next(_ count: Int) {
        guard let i = tour.step else { return }
        if i + 1 >= count { tour.finish() } else { tour.show(i + 1) }
    }

    private func back() {
        guard let i = tour.step, i > 0 else { return }
        tour.show(i - 1)
    }

    /// A press outside the callout: it nudges, so the eye goes to it.
    private func poke() {
        guard !reduceMotion else { return }
        withAnimation(Motion.snappy) { nudged = true }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.16) { withAnimation(Motion.snappy) { nudged = false } }
    }

    /// A step arriving: the sidebar shown for the tour; the Dashboard shown for its own step.
    private func arrive(_ step: Int?, from old: Int?) {
        guard let step else { return }
        if old == nil {
            if columns != .all { columns = .all }
            SearchField.resign(engine)
        }
        let list = steps
        guard step < list.count else {
            tour.finish()
            return
        }
        if list[step].aim == .dashboard, engine.nav.current.place != .dashboard || !engine.nav.current.path.isEmpty {
            engine.go(.dashboard)
        }
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
            && !engine.settingsOpen && engine.query.isEmpty
    }
}

/// The dim with its light, the callout, and the keys.
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
            escape
            TourVeil(hole: plan.hole)
                .contentShape(Rectangle())
                .onTapGesture(perform: poke)
            TourCard(step: step, index: index, count: count, arrow: plan.arrow, arrowAt: plan.arrowAt, cardSize: $cardSize, next: next, back: back, skip: skip)
                .scaleEffect(nudged ? 1.025 : 1)
                .offset(x: plan.card.x - (plan.arrow == .leading ? CalloutMetrics.length : 0),
                        y: plan.card.y - (plan.arrow == .top ? CalloutMetrics.length : 0))
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
        // the keys: Return and → on, ← back, Escape out (as the quiz screen takes its keys)
        .focusable(interactions: .edit)
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(phases: .down) { press in key(press) }
        .task {
            try? await Task.sleep(nanoseconds: 80_000_000) // (once the window has let go of what had the keys)
            focused = true
        }
        .onChange(of: index) { _, _ in focused = true }
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

    /// Escape as the window's cancel key too, whatever has the keys (ending twice is ending once): behind the dim,
    /// where no press reaches it.
    private var escape: some View {
        Button("", action: skip)
            .keyboardShortcut(.cancelAction)
            .opacity(0)
            .frame(width: 1, height: 1)
            .accessibilityHidden(true)
    }
}

/// The window dimmed but for the lit place, which is ringed in the accent.
private struct TourVeil: View {
    let hole: CGRect?
    private static let dim = Theme.dynamic(light: NSColor(white: 0, alpha: 0.24), dark: NSColor(white: 0, alpha: 0.48))

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
                    .strokeBorder(Color.accentColor, lineWidth: 2)
                    .frame(width: hole.width, height: hole.height)
                    .shadow(color: Color.accentColor.opacity(0.45), radius: 8)
                    .offset(x: hole.minX, y: hole.minY)
            }
        }
    }
}

/// The callout: a glass card with its arrow, the step's count and Skip, its symbol, title and words, the keys it is
/// about, the steps as dots, Back and Next.
private struct TourCard: View {
    static let width: CGFloat = 380

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
            .padding(22)
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

    /// Room for the arrow on its side (the box's own size stays as measured).
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
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                Text("\(index + 1) of \(count)")
                    .font(.sFootnote.weight(.semibold).monospacedDigit())
                    .foregroundStyle(.secondary)
                Spacer(minLength: 8)
                if !last {
                    Button("Skip Tour", action: skip)
                        .buttonStyle(.plain)
                        .font(.sFootnote.weight(.medium))
                        .foregroundStyle(.secondary)
                        .help("End the tour (Escape)")
                }
            }
            HStack(spacing: 12) {
                IconTile(symbol: step.symbol, color: .accentColor, size: 38)
                Text(step.title)
                    .font(.sTitle3)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text(step.body)
                .font(.sBody)
                .fixedSize(horizontal: false, vertical: true)
            if !step.keys.isEmpty { keyCaps }
            footer
        }
    }

    private var keyCaps: some View {
        HStack(spacing: 6) {
            ForEach(step.keys, id: \.self) { k in
                Text(k)
                    .font(.sCallout.weight(.medium))
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(Theme.well, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
            }
        }
        .accessibilityElement(children: .combine)
    }

    private var footer: some View {
        HStack(spacing: 10) {
            HStack(spacing: 5) {
                ForEach(0..<count, id: \.self) { i in
                    Capsule()
                        .fill(i == index ? Color.accentColor : Color.secondary.opacity(0.35))
                        .frame(width: i == index ? 16 : 6, height: 6)
                }
            }
            .accessibilityHidden(true)
            Spacer(minLength: 8)
            if index > 0 {
                Button("Back", action: back)
                    .glassButton()
                    .controlSize(.large)
            }
            Button(last ? "Done" : "Next", action: next) // (Return, as the stage takes its keys)
                .glassButton(prominent: true)
                .controlSize(.large)
        }
        .padding(.top, 4)
    }
}

/// The callout's arrow: how far it stands out from the box, and half its base.
enum CalloutMetrics {
    static let length: CGFloat = 11
    static let half: CGFloat = 11
}

/// The callout's outline: a rounded box with its arrow on one edge, drawn as one shape so the glass is one piece.
struct CalloutShape: Shape {
    let arrow: TourArrow
    let at: CGFloat
    var radius: CGFloat = 22

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

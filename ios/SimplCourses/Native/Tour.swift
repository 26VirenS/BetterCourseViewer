import Combine
import SwiftUI
import UIKit

extension Notification.Name {
    /// (Build 72) The tour opening Today's first counter itself (its spot not found in time, or the simulator suite's run).
    static let simplTourOpenCard = Notification.Name("SimplCourses.tourOpenCard")
    /// (Build 72) The tour closing the counter's list itself.
    static let simplTourCloseSheet = Notification.Name("SimplCourses.tourCloseSheet")
}

// MARK: - The tour after the first setup (iPhone 1.9, build 72)

/// (iPhone 1.9, build 72) A short tour, once, right after the first setup — three steps:
/// 1. a counter on Today tapped, and its list closed with Done;
/// 2. the bar at the very top playing each of its colours in turn, each said in words (blue loading, purple signing in,
///    yellow finish login, red login failed), then that there is no need to wait for it, and Next;
/// 3. Courses, the first course opened, back to the list, and the second course opened (two columns — the Duo open on
///    its side, an iPad — open on the first course by themselves: there, Switch Course to the second).
/// It cannot be skipped: it sits in a window of its own over everything (TourWindow), and only the ringed spot takes a
/// press; the rest of the screen waits. Nothing behind it is darkened. Something else coming up over the app (an alert,
/// the sign-in's page or its strip, a quiz) puts it away until that is done. Begun again from the start if the app is
/// closed before it ends.
@MainActor
final class Tour: ObservableObject {
    static let shared = Tour()

    enum Step: String {
        case card, closeSheet, bar, coursesTab, firstCourse, back, secondCourse, switchCourse, end
    }

    /// The app's own views the tour rings, where they report themselves (tourTarget), in window coordinates.
    enum Target: Hashable {
        case card
        case sheetDone
        case course(Int)
        case switcher
    }

    @Published private(set) var step: Step?
    /// The ring, in window coordinates, and its corners.
    @Published private(set) var ring: CGRect?
    @Published private(set) var ringRadius: CGFloat = 20
    /// Step 2: blue, purple, yellow, red — then (4) the words under them and Next.
    @Published private(set) var barPhase = 0
    /// The step's spot not found for a while: the tip's own button does what the spot would.
    @Published private(set) var stuck = false
    @Published private(set) var insets = UIEdgeInsets(top: 47, left: 0, bottom: 34, right: 0)
    /// The bar of step 2: the app's own (TopStatusBar), shown what to say.
    let demo = EngineStatus()

    private weak var engine: Engine?
    private var window: TourWindow?
    private var frames: [Target: CGRect] = [:]
    private var courseIDs: [Int: String] = [:]
    /// How many courses the Courses list shows, once it has said.
    private var listCount: Int?
    private var firstOpened: String?
    private var sheetUp = false
    private var stepSince = Date()
    private var hole: CGRect?
    private var ticker: Timer?
    private var barTask: Task<Void, Never>?
    /// The simulator suite (-SimplTour drive): the tour goes through itself, saying each step for its picture.
    private var drive = false

    private static let pendingKey = "tour:pending"
    private static let doneKey = "tour:done"

    // MARK: When

    /// The first setup is up: the tour follows it, once (and again at the next launch, if it is not finished).
    func setupShown() {
        let d = UserDefaults.standard
        guard !d.bool(forKey: Self.doneKey), !d.bool(forKey: "SimplDemo") else { return }
        d.set(true, forKey: Self.pendingKey)
    }

    /// The setup saved (and notifications asked for): the tour, if it is still to come.
    func afterSetup(_ engine: Engine) {
        guard UserDefaults.standard.bool(forKey: Self.pendingKey) else { return }
        begin(engine)
    }

    /// A launch with the setup done: a tour left unfinished begins again; and the simulator suite's (-SimplTour).
    func launched(_ engine: Engine) {
        if let t = UserDefaults.standard.string(forKey: "SimplTour"), !t.isEmpty {
            drive = t == "drive"
            begin(engine)
        } else if UserDefaults.standard.bool(forKey: Self.pendingKey) {
            begin(engine)
        }
    }

    private func begin(_ engine: Engine) {
        guard step == nil else { return }
        self.engine = engine
        step = .card // (taken at once: begun only once)
        Task {
            // the app's screens up, and nothing over them (the setup's cover gone, the sign-in settled)
            for _ in 0..<240 {
                if engine.calm { break }
                try? await Task.sleep(nanoseconds: 500_000_000)
            }
            try? await Task.sleep(nanoseconds: 700_000_000)
            engine.searchOpen = false
            engine.paths[.today] = []
            engine.paths[.courses] = []
            engine.tab = .today
            firstOpened = nil
            show()
            go(.card)
            ticker = Timer.scheduledTimer(withTimeInterval: 0.2, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.tick() }
            }
        }
    }

    private func finish() {
        UserDefaults.standard.set(true, forKey: Self.doneKey)
        UserDefaults.standard.removeObject(forKey: Self.pendingKey)
        ticker?.invalidate()
        ticker = nil
        barTask?.cancel()
        demo.link = .on
        step = nil
        ring = nil
        hole = nil
        window?.isHidden = true
        window = nil
        say("done")
    }

    // MARK: What the screens say

    /// A view's place on screen (tourTarget).
    func place(_ t: Target, _ r: CGRect, id: String? = nil) {
        guard TabBarProbe.usable(r) else { return }
        frames[t] = r
        if case .course(let i) = t, let id { courseIDs[i] = id }
    }

    /// The Courses list drawn: how many courses it shows.
    func courses(_ n: Int) { listCount = n }

    /// A counter's list up (true) or gone (false).
    func sheet(_ up: Bool) {
        sheetUp = up
        if step != nil { tick() }
    }

    // MARK: The steps

    private func go(_ s: Step) {
        guard step != nil || s == .card else { return }
        withAnimation(.spring(duration: 0.35, bounce: 0)) { step = s }
        stepSince = Date()
        stuck = false
        if s != .bar { barTask?.cancel(); demo.link = .on }
        say(s.rawValue)
        if s == .bar { playBar() }
        if s == .end { Haptics.success() }
        UIAccessibility.post(notification: .screenChanged, argument: nil)
        tick()
        driveStep(s)
    }

    /// Step 2: each colour about two seconds, then the words and Next.
    private func playBar() {
        barTask?.cancel()
        barPhase = 0
        demo.link = .on
        demo.began()
        barTask = Task { [weak self] in
            let waits: [UInt64] = [2_200, 2_200, 2_400, 1_400]
            for (i, ms) in waits.enumerated() {
                try? await Task.sleep(nanoseconds: ms * 1_000_000)
                guard let self, !Task.isCancelled, self.step == .bar else { return }
                switch i {
                case 0:
                    self.demo.link = .signingIn // (first, so the line keeps running as the read ends)
                    self.demo.ended()
                case 1: self.demo.link = .needsInput
                case 2: self.demo.link = .failed("Your school couldn’t be reached.")
                default: break
                }
                withAnimation(.snappy) { self.barPhase = i + 1 }
                self.window?.statusStyleChanged()
                if self.drive { self.shot(["bar-purple", "bar-yellow", "bar-red", "bar-next"][i]) }
            }
        }
    }

    /// Next, under the bar's last colour.
    func next() {
        guard step == .bar, barPhase >= 4 else { return }
        Haptics.tap()
        demo.link = .on
        window?.statusStyleChanged()
        go(.coursesTab)
    }

    /// Done, at the end.
    func done() {
        guard step == .end else { return }
        Haptics.tap()
        finish()
    }

    private var savedCourses: [String] { engine?.saved("courses", as: CoursesData.self)?.rows.map(\.id) ?? [] }
    private var hasSecond: Bool { courseIDs[1] != nil || (listCount ?? savedCourses.count) >= 2 }
    /// Two columns on Courses: a course's home on the left, opened by itself (TabSplit).
    private var twoColumns: Bool { engine?.splitEdge[.courses] != nil }

    /// What the step waits for, looked at again.
    private func advance(_ s: Step, _ engine: Engine) {
        let path = engine.paths[.courses]
        let open: String? = { if case .course(let id)? = path.first { return id } else { return nil } }()
        switch s {
        case .card:
            if sheetUp { go(.closeSheet) }
        case .closeSheet:
            if !sheetUp { go(.bar) }
        case .bar, .end:
            break
        case .coursesTab:
            if engine.tab == .courses { go(listCount == 0 ? .end : .firstCourse) }
        case .firstCourse:
            if listCount == 0 { go(.end) }
            else if twoColumns { go(hasSecond ? .switchCourse : .end) }
            else if let open {
                firstOpened = open
                go(hasSecond ? .back : .end)
            }
        case .back:
            if twoColumns { go(.switchCourse) } else if path.isEmpty { go(.secondCourse) }
        case .secondCourse:
            if twoColumns { go(.switchCourse) } else if let open, open != firstOpened { go(.end) }
        case .switchCourse:
            if !twoColumns {
                // (folded mid-step: one column again)
                go(path.isEmpty ? .secondCourse : .back)
            } else if let open {
                if firstOpened == nil { firstOpened = open } else if open != firstOpened { go(.end) }
            }
        }
    }

    // MARK: Each moment

    private func tick() {
        guard let engine, let s = step, let window else { return }
        // put away while something else is up over the app
        let away = engine.phase != .native || engine.status.signInOpen || engine.status.expanded || engine.quiz != nil
            || engine.tool != nil || engine.setup || engine.whatsNew != nil || engine.settingsOpen || !engine.web.login.settled
            || alertUp()
        if window.isHidden != away { window.isHidden = away }
        if away { return }
        if window.safeAreaInsets != insets { insets = window.safeAreaInsets }
        advance(s, engine)
        guard let now = step else { return }
        let spot = locate(now)
        let r = spot?.ring
        if r != ring || (spot.map { $0.radius } ?? ringRadius) != ringRadius {
            withAnimation(.spring(duration: 0.45, bounce: 0)) {
                ring = r
                if let spot { ringRadius = spot.radius }
            }
        }
        hole = spot?.hole
        let wants = ![.bar, .end].contains(now)
        let lost = wants && spot == nil && Date().timeIntervalSince(stepSince) > 6
        if lost != stuck { withAnimation(.snappy) { stuck = lost } }
    }

    /// The step's spot: the ring round it, the place that takes a press, and the ring's corners.
    private func locate(_ s: Step) -> (ring: CGRect, hole: CGRect, radius: CGFloat)? {
        guard let engine, let app = appWindow else { return nil }
        func around(_ f: CGRect, _ by: CGFloat, radius: CGFloat? = nil) -> (ring: CGRect, hole: CGRect, radius: CGFloat) {
            let r = f.insetBy(dx: -by, dy: -by)
            return (r, f.insetBy(dx: -2, dy: -2), radius ?? r.height / 2)
        }
        switch s {
        case .card:
            guard engine.tab == .today, let f = frames[.card] else { return nil }
            return around(f, 5, radius: 25)
        case .closeSheet:
            let found = TourFinder.sheetDone(in: app, log: dump("sheet")) ?? frames[.sheetDone].map(TourFinder.barButton)
            guard let f = found else { return nil }
            return around(f, 4)
        case .coursesTab:
            guard let f = TourFinder.tab(1, in: app, log: dump("tabs")) else { return nil }
            return around(f, 3)
        case .firstCourse, .secondCourse:
            guard engine.tab == .courses, engine.paths[.courses].isEmpty, let f = frames[.course(s == .firstCourse ? 0 : 1)] else { return nil }
            return around(f, 5, radius: 22)
        case .back:
            guard let f = TourFinder.back(in: app, log: dump("back")) else { return nil }
            let (r, _, radius) = around(f, 4)
            return (ring: r, hole: f.insetBy(dx: -8, dy: -8), radius: radius)
        case .switchCourse:
            guard let edge = engine.splitEdge[.courses] else { return nil }
            let found = TourFinder.trailingButton(leftOf: edge, in: app, log: dump("switch")) ?? frames[.switcher].map(TourFinder.barButton)
            guard let f = found else { return nil }
            // (the menu it opens comes down under it: the whole left column takes a press)
            let column = CGRect(x: 0, y: 0, width: edge, height: app.bounds.height)
            return (ring: f.insetBy(dx: -4, dy: -4), hole: column, radius: (f.height + 8) / 2)
        case .bar, .end:
            return nil
        }
    }

    /// The app's own window (not the tour's).
    private var appWindow: UIWindow? {
        let scene = window?.windowScene
        return scene?.keyWindow ?? scene?.windows.first { !($0 is TourWindow) && !$0.isHidden }
    }

    private func alertUp() -> Bool {
        var vc = appWindow?.rootViewController
        while let next = vc?.presentedViewController { vc = next }
        return vc is UIAlertController
    }

    private func show() {
        guard window == nil else { return }
        let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        guard let scene = engine?.hostView.window?.windowScene ?? scenes.first(where: { $0.activationState == .foregroundActive }) ?? scenes.first else { return }
        let w = TourWindow(windowScene: scene)
        w.windowLevel = UIWindow.Level(rawValue: UIWindow.Level.normal.rawValue + 1)
        w.backgroundColor = .clear
        let host = TourHost(rootView: TourOverlay(tour: self))
        host.view.backgroundColor = .clear
        host.tour = self
        w.rootViewController = host
        w.hole = { [weak self] in self?.hole }
        w.isHidden = false
        window = w
    }

    /// The status bar's words over the bar's strip: dark on yellow, light on red.
    var statusStyle: UIStatusBarStyle {
        guard step == .bar else { return .default }
        switch barPhase {
        case 2: return .darkContent
        case 3, 4: return .lightContent
        default: return .default
        }
    }

    // MARK: The simulator suite

    private func say(_ s: String) { print("[Simpl Courses] tour: \(s)") }

    /// Whether the bars looked through are written out now: at each of the simulator suite's pictures.
    private var writeOut = false

    private func dump(_ part: String) -> Bool { writeOut }

    private func shot(_ name: String) { say("shot \(name)") }

    /// (-SimplTour drive) Each step pictured once its ring is placed, then done as a press would.
    private func driveStep(_ s: Step) {
        guard drive else { return }
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            guard let self, self.step == s else { return }
            let names: [Step: String] = [.card: "card", .closeSheet: "close", .bar: "bar-blue", .coursesTab: "courses-tab", .firstCourse: "first-course",
                                         .back: "back", .secondCourse: "second-course", .switchCourse: "switch-course", .end: "end"]
            self.shot(names[s] ?? s.rawValue)
            if let r = self.ring { self.say("ring \(Int(r.minX)),\(Int(r.minY)) \(Int(r.width))x\(Int(r.height))") } else { self.say("no ring") }
            // (what the bars hold, and where the app's own views say they are, to check the ring against)
            self.writeOut = true
            _ = self.locate(s)
            self.writeOut = false
            for (t, f) in self.frames { self.say("view \(t) at \(Int(f.minX)),\(Int(f.minY)) \(Int(f.width))x\(Int(f.height))") }
            if s == .bar {
                while self.step == .bar && self.barPhase < 4 { try? await Task.sleep(nanoseconds: 200_000_000) }
            }
            try? await Task.sleep(nanoseconds: 2_000_000_000)
            guard self.step == s else { return }
            self.act(s)
        }
    }

    /// The step done for the student: the tip's own button when the spot is not found, and the suite's run.
    func act(_ s: Step) {
        guard let engine, step == s else { return }
        Haptics.tap()
        switch s {
        case .card:
            NotificationCenter.default.post(name: .simplTourOpenCard, object: nil)
            later(s) { $0.go(.bar) } // (no counter to open)
        case .closeSheet:
            NotificationCenter.default.post(name: .simplTourCloseSheet, object: nil)
            later(s) { $0.go(.bar) }
        case .bar: next()
        case .coursesTab: engine.tab = .courses
        case .firstCourse:
            if let id = courseIDs[0] ?? savedCourses.first { engine.push(.course(id: id)) } else { go(.end) }
        case .back: engine.paths[.courses] = []
        case .secondCourse:
            if let id = courseIDs[1] ?? savedCourses.dropFirst().first { engine.push(.course(id: id)) } else { go(.end) }
        case .switchCourse:
            let ids = savedCourses
            if let id = ([courseIDs[1]].compactMap { $0 } + ids).first(where: { $0 != firstOpened }) { engine.focusCourse(id) } else { go(.end) }
        case .end: done()
        }
    }

    private func later(_ s: Step, _ then: @escaping (Tour) -> Void) {
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            guard let self, self.step == s else { return }
            then(self)
        }
    }
}

// MARK: - Its window

/// (Build 72) The tour's own window, over the app's (its sheets, bars and menus too): a press in the ring's spot goes
/// through to the app; anywhere else, the tour's own tip takes it or nothing does.
final class TourWindow: UIWindow {
    var hole: @MainActor () -> CGRect? = { nil }

    override func hitTest(_ point: CGPoint, with event: UIEvent?) -> UIView? {
        if let h = hole(), h.contains(point) { return nil }
        return super.hitTest(point, with: event) ?? rootViewController?.view
    }

    func statusStyleChanged() { rootViewController?.setNeedsStatusBarAppearanceUpdate() }
}

final class TourHost: UIHostingController<TourOverlay> {
    weak var tour: Tour?
    override var preferredStatusBarStyle: UIStatusBarStyle { tour?.statusStyle ?? .default }
}

// MARK: - What it draws

/// (Build 72) The ring round the spot, the tip beside it with its arrow, and (step 2) the bar at the top.
struct TourOverlay: View {
    @ObservedObject var tour: Tour
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.colorScheme) private var scheme
    @State private var tipSize: CGSize = .zero
    @State private var glow = false

    var body: some View {
        GeometryReader { geo in
            let origin = geo.frame(in: .global).origin
            let size = geo.size
            let ring = tour.ring.map { $0.offsetBy(dx: -origin.x, dy: -origin.y) }
            ZStack(alignment: .topLeading) {
                if tour.step == .bar {
                    VStack(spacing: 0) {
                        TopStatusBar(status: tour.demo, open: {}, retry: {})
                        Spacer(minLength: 0)
                    }
                    .allowsHitTesting(false)
                    .transition(.opacity)
                }
                if let ring {
                    halo(ring)
                }
                if let step = tour.step {
                    tip(step, ring: ring, in: size)
                }
            }
            .frame(width: size.width, height: size.height, alignment: .topLeading)
        }
        .ignoresSafeArea()
        .animation(.easeOut(duration: 0.25), value: tour.step == .bar)
    }

    // MARK: The ring

    private func halo(_ r: CGRect) -> some View {
        let shape = RoundedRectangle(cornerRadius: tour.ringRadius, style: .continuous)
        return ZStack {
            shape.stroke(Color.accentColor.opacity(reduceMotion ? 0.22 : (glow ? 0.3 : 0.12)), lineWidth: 10)
            shape.stroke(Color.accentColor, lineWidth: 3)
        }
        .shadow(color: Color.accentColor.opacity(0.45), radius: 9)
        .frame(width: r.width, height: r.height)
        .position(x: r.midX, y: r.midY)
        .allowsHitTesting(false)
        .accessibilityHidden(true)
        .onAppear {
            guard !reduceMotion else { return }
            withAnimation(.easeInOut(duration: 1.1).repeatForever(autoreverses: true)) { glow = true }
        }
        .transition(.opacity)
    }

    // MARK: The tip

    private func tip(_ step: Tour.Step, ring: CGRect?, in size: CGSize) -> some View {
        let w = min(size.width - 32, 360)
        let ins = tour.insets
        // where it points: the ring, the bar at the top, or nothing (the end, a spot not found yet)
        let anchor: CGRect? = {
            if step == .bar {
                let h = tour.barPhase >= 2 ? ins.top + 54 : max(ins.top - 4, 3)
                return CGRect(x: size.width / 2 - 1, y: 0, width: 2, height: h)
            }
            return step == .end ? nil : ring
        }()
        let x: CGFloat = {
            guard let a = anchor else { return (size.width - w) / 2 }
            return min(max(a.midX - w / 2, 16), size.width - 16 - w)
        }()
        let gap: CGFloat = 13
        let h = max(tipSize.height, 60)
        let below: Bool = {
            guard let a = anchor else { return true }
            if step == .bar { return true }
            let roomBelow = size.height - ins.bottom - 8 - (a.maxY + gap)
            let roomAbove = a.minY - gap - ins.top - 8
            if step == .closeSheet && roomAbove >= h { return false } // (over the screen behind the sheet, as pictured)
            return roomBelow >= h || roomBelow >= roomAbove
        }()
        let y: CGFloat = {
            guard let a = anchor else { return max(ins.top + 20, size.height * 0.4 - h / 2) }
            return below ? a.maxY + gap : a.minY - gap - h
        }()
        let arrowX = anchor.map { min(max($0.midX, x + 26), x + w - 26) }
        return card(step, width: w)
            .overlay(alignment: below ? .topLeading : .bottomLeading) {
                if let ax = arrowX {
                    Rectangle()
                        .fill(fill)
                        .frame(width: 14, height: 14)
                        .rotationEffect(.degrees(45))
                        .offset(x: ax - x - 7, y: below ? -7 : 7)
                        .allowsHitTesting(false)
                }
            }
            .offset(x: x, y: y)
        .id(step)
        .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.opacity.combined(with: .scale(scale: 0.96, anchor: below ? .top : .bottom)))
        .animation(.spring(duration: 0.4, bounce: 0), value: x)
        .animation(.spring(duration: 0.4, bounce: 0), value: y)
    }

    private var fill: Color {
        scheme == .dark ? Color(.tertiarySystemBackground) : .white
    }

    private func card(_ step: Tour.Step, width: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            if let label = label(step) {
                Text(label)
                    .font(.caption2.weight(.semibold))
                    .kerning(0.2)
                    .foregroundStyle(Color.accentColor)
                    .padding(.bottom, 3)
            }
            title(step)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(.primary)
                .fixedSize(horizontal: false, vertical: true)
            if let sub = sub(step) {
                Text(sub)
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 3)
            }
            if step == .bar { dots.padding(.top, 9) }
            if let b = button(step) {
                Button(action: b.1) {
                    Text(b.0)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(.white)
                        .frame(maxWidth: .infinity, minHeight: 36)
                        .background(Color.accentColor, in: Capsule())
                        .contentShape(Capsule())
                }
                .buttonStyle(PressScale())
                .padding(.top, 10)
                .transition(.opacity)
            }
        }
        .padding(.horizontal, 14)
        .padding(.top, 12)
        .padding(.bottom, 13)
        .frame(width: width, alignment: .leading)
        .background(fill, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).strokeBorder(Color.primary.opacity(scheme == .dark ? 0.12 : 0.06), lineWidth: 0.5))
        .shadow(color: .black.opacity(scheme == .dark ? 0.5 : 0.2), radius: 15, y: 10)
        .onGeometryChange(for: CGSize.self) { $0.size } action: { tipSize = $0 }
        .accessibilityElement(children: .contain)
    }

    private func label(_ step: Tour.Step) -> String? {
        switch step {
        case .card, .closeSheet: return "1 OF 3"
        case .bar: return "2 OF 3 · THE BAR AT THE TOP"
        case .end: return nil
        default: return "3 OF 3"
        }
    }

    private func title(_ step: Tour.Step) -> Text {
        switch step {
        case .card: return Text("Tap a card to see what’s in it.")
        case .closeSheet: return Text("Tap Done to close it.")
        case .bar:
            let (word, rest, tint) = barWords
            return Text(word).foregroundStyle(tint) + Text(rest)
        case .coursesTab: return Text("Go to Courses.")
        case .firstCourse: return Text("Open your first course.")
        case .back, .switchCourse: return Text("Now switch to your second course.")
        case .secondCourse: return Text("Open your second course.")
        case .end: return Text("You’re all set.")
        }
    }

    private var barWords: (String, String, Color) {
        switch tour.barPhase {
        case 0: return ("Blue:", " loading", .accentColor)
        case 1: return ("Purple:", " signing in", Color(.systemPurple))
        case 2: return ("Yellow:", " tap it to finish login", Color(UIColor { $0.userInterfaceStyle == .dark ? .systemYellow : UIColor(red: 0.79, green: 0.6, blue: 0, alpha: 1) }))
        default: return ("Red:", " login failed, tap Try Again", Color(.systemRed))
        }
    }

    private func sub(_ step: Tour.Step) -> String? {
        switch step {
        case .back: return "Go back first."
        case .switchCourse: return "Tap the courses button at the top."
        case .bar where tour.barPhase >= 4: return "No need to wait for the bar. Simpl keeps loading in the background."
        default: return nil
        }
    }

    private func button(_ step: Tour.Step) -> (String, @MainActor () -> Void)? {
        switch step {
        case .bar: return tour.barPhase >= 4 ? ("Next", { tour.next() }) : nil
        case .end: return ("Done", { tour.done() })
        default:
            guard tour.stuck else { return nil }
            let words: [Tour.Step: String] = [.card: "Open a Card", .closeSheet: "Close It", .coursesTab: "Go to Courses", .firstCourse: "Open It",
                                              .back: "Go Back", .secondCourse: "Open It", .switchCourse: "Switch"]
            return (words[step] ?? "Next", { tour.act(step) })
        }
    }

    private var dots: some View {
        HStack(spacing: 5) {
            ForEach(0..<4, id: \.self) { i in
                Capsule()
                    .fill([Color.accentColor, Color(.systemPurple), Color(.systemYellow), Color(.systemRed)][i])
                    .frame(width: 22, height: 5)
                    .opacity(i <= tour.barPhase ? 1 : 0.25)
            }
        }
        .accessibilityHidden(true)
    }
}

// MARK: - Where the system's buttons are

/// (Build 72) The bars' buttons the tour rings — drawn by UIKit, not by the app's views — found in the window: a tab, a
/// Back button, a sheet's Done, the Switch Course button. Each is written out to the console once a part in the simulator
/// suite's run (`log`), as the tab bar is (TabBarProbe).
@MainActor
enum TourFinder {
    static func views<T: UIView>(_ type: T.Type, in v: UIView) -> [T] {
        var out: [T] = []
        func walk(_ v: UIView) {
            if let t = v as? T { out.append(t) }
            for s in v.subviews { walk(s) }
        }
        walk(v)
        return out
    }

    static func shown(_ v: UIView) -> Bool {
        guard v.window != nil else { return false }
        var x: UIView? = v
        while let y = x {
            if y.isHidden || y.alpha < 0.01 { return false }
            x = y.superview
        }
        return true
    }

    private static func name(_ v: UIView) -> String { String(describing: type(of: v)) }

    private static func write(_ title: String, _ root: UIView, in window: UIWindow) {
        var lines: [String] = []
        func walk(_ v: UIView, _ depth: Int) {
            guard depth < 8 else { return }
            for s in v.subviews where !s.isHidden && s.alpha > 0.01 {
                let f = s.convert(s.bounds, to: window)
                let id = s.accessibilityIdentifier.map { " #\($0)" } ?? ""
                lines.append("\(String(repeating: "  ", count: depth))\(name(s))\(id) \(Int(f.minX)),\(Int(f.minY)) \(Int(f.width))x\(Int(f.height))")
                walk(s, depth + 1)
            }
        }
        walk(root, 0)
        print("[Simpl Courses] tour: \(title)\n" + lines.joined(separator: "\n"))
    }

    /// A bar button's glass, from its words as SwiftUI lays them out: at least 44 points tall, 15 points either side.
    static func barButton(_ words: CGRect) -> CGRect {
        let w = max(44, words.width + 30), h = max(44, words.height + 22)
        return CGRect(x: words.midX - w / 2, y: words.midY - h / 2, width: w, height: h)
    }

    /// The tab bar's n-th tab (0 Today, 1 Courses, …), Search apart.
    static func tab(_ n: Int, in window: UIWindow, log: Bool) -> CGRect? {
        let bars = views(UITabBar.self, in: window).filter(shown)
        guard let bar = bars.first else { return nil }
        if log { write("tab bar", bar, in: window) }
        var frames: [CGRect] = []
        func walk(_ v: UIView, apart: Bool) {
            for s in v.subviews where !s.isHidden && s.alpha > 0.01 {
                let kind = name(s)
                let away = apart || kind.contains("Auxiliary")
                if kind.contains("TabButton") || kind.contains("TabBarButton") {
                    let f = s.convert(s.bounds, to: window)
                    if !away, TabBarProbe.usable(f), f.width >= 20, !frames.contains(where: { abs($0.midX - f.midX) < 4 && abs($0.midY - f.midY) < 4 }) {
                        frames.append(f)
                    }
                    continue
                }
                walk(s, apart: away)
            }
        }
        walk(bar, apart: false)
        guard !frames.isEmpty else { return nil }
        let wide = (frames.map(\.maxX).max()! - frames.map(\.minX).min()!) >= (frames.map(\.maxY).max()! - frames.map(\.minY).min()!)
        let sorted = frames.sorted { wide ? $0.midX < $1.midX : $0.midY < $1.midY }
        return n < sorted.count ? sorted[n] : nil
    }

    /// The bars on screen, top to bottom, outside any sheet (`in`: inside this view only).
    private static func navBars(in root: UIView, window: UIWindow) -> [(UINavigationBar, CGRect)] {
        views(UINavigationBar.self, in: root).filter(shown).compactMap { bar -> (UINavigationBar, CGRect)? in
            let f = bar.convert(bar.bounds, to: window)
            guard TabBarProbe.usable(f), f.width > 40, f.height > 20, window.bounds.intersects(f) else { return nil }
            return (bar, f)
        }
        .sorted { $0.1.minY < $1.1.minY }
    }

    /// A bar's buttons (the glass round each), leading to trailing.
    private static func buttons(in bar: UINavigationBar, window: UIWindow) -> [CGRect] {
        var out: [CGRect] = []
        func walk(_ v: UIView) {
            for s in v.subviews where !s.isHidden && s.alpha > 0.01 {
                if name(s).contains("ButtonBarButton") {
                    let f = s.convert(s.bounds, to: window)
                    if TabBarProbe.usable(f), f.width >= 28, f.height >= 28, !out.contains(where: { $0.intersects(f) && abs($0.midX - f.midX) < 6 }) { out.append(f) }
                    continue
                }
                walk(s)
            }
        }
        walk(bar)
        return out.sorted { $0.midX < $1.midX }
    }

    /// The Back button on the screen showing (outside sheets).
    static func back(in window: UIWindow, log: Bool) -> CGRect? {
        guard let root = window.rootViewController?.view else { return nil }
        let bars = navBars(in: root, window: window).filter { $0.0.topItem != nil && ($0.0.items?.count ?? 0) >= 1 }
        if log { bars.forEach { write("bar", $0.0, in: window) } }
        for (bar, f) in bars {
            // a view of the Back button's (by its kind or its name), and the button round it
            var found: CGRect?
            func walk(_ v: UIView) {
                for s in v.subviews where found == nil && !s.isHidden && s.alpha > 0.01 {
                    if name(s).contains("BackButton") || s.accessibilityIdentifier == "BackButton" {
                        var best = s.convert(s.bounds, to: window)
                        var up = s.superview
                        while let u = up, u !== bar {
                            let uf = u.convert(u.bounds, to: window)
                            if name(u).contains("ButtonBarButton") { best = uf; break }
                            up = u.superview
                        }
                        if best.width >= 28 && best.height >= 28 { found = best }
                        return
                    }
                    walk(s)
                }
            }
            walk(bar)
            if let found { return found }
            if bar.backItem != nil || (bar.items?.count ?? 0) >= 2, let first = buttons(in: bar, window: window).first, first.midX < f.midX {
                return first
            }
        }
        return nil
    }

    /// The trailing button of the sheet up over the app (its Done).
    static func sheetDone(in window: UIWindow, log: Bool) -> CGRect? {
        var vc = window.rootViewController
        while let next = vc?.presentedViewController { vc = next }
        guard let top = vc, top !== window.rootViewController, let view = top.viewIfLoaded else { return nil }
        let bars = navBars(in: view, window: window)
        if log { bars.forEach { write("sheet bar", $0.0, in: window) } }
        guard let first = bars.first, let last = buttons(in: first.0, window: window).last, last.midX > first.1.midX else { return nil }
        return last
    }

    /// The trailing button of the bar over the left column (Courses in two columns: Switch Course).
    static func trailingButton(leftOf edge: CGFloat, in window: UIWindow, log: Bool) -> CGRect? {
        guard let root = window.rootViewController?.view else { return nil }
        let bars = navBars(in: root, window: window).filter { $0.1.maxX <= edge + 2 }
        if log { bars.forEach { write("left bar", $0.0, in: window) } }
        guard let first = bars.first, let last = buttons(in: first.0, window: window).last, last.midX > first.1.midX else { return nil }
        return last
    }
}

extension View {
    /// (Build 72) This view's place on screen, for the tour's ring (Tour.place).
    func tourTarget(_ t: Tour.Target, id: String? = nil) -> some View {
        onGeometryChange(for: CGRect.self) { $0.frame(in: .global) } action: { r in Tour.shared.place(t, r, id: id) }
    }
}

/// (Build 72) A course row's background while the tour is on: the list's own, saying where the row is (the ring goes
/// round the whole row, not its words).
struct TourCell: View {
    let index: Int
    let id: String

    var body: some View {
        Color(.secondarySystemGroupedBackground).tourTarget(.course(index), id: id)
    }
}

import AppKit
import Network
import SwiftUI

// (1.3.80) Live checks, the app's side. A screen left open used to show what it was opened on until something was done
// or Refresh pressed; now the screen on show asks Canvas again, now and then, whether anything on it is new — every
// minute, a course's pages every ten — and puts in place only what changed: a counter's number, one card, one row. A
// check that finds nothing new changes nothing, so nothing is drawn. The engine's side (native-app.js) reads again only
// what is older than the check's age and says "unchanged" by the answer's fingerprint; the plan of when is LivePlan
// (LivePlan.swift); what holds a check back is LiveHolds; each screen says how it checks with `liveChecks`.

/// What a live check found on a screen: the parts of it that changed — none when nothing is new — and how to put them in
/// place, all in one go (the screen is drawn once).
struct LiveFound {
    var parts: [String] = []
    var apply: (@MainActor () -> Void)?

    /// Nothing new: nothing to put in place.
    static var unchanged: LiveFound { LiveFound() }
}

/// A read a check found new: its answer, whether what the screen shows is still what the check asked after, and what to
/// remember once the answer is shown (its fingerprint, for the next check).
struct LiveChange<T> {
    let value: T
    let current: @MainActor () -> Bool
    let keep: @MainActor () -> Void

    /// The new answer put in place by `show`, and remembered — unless the screen has read it afresh by itself since the
    /// check asked (a tick, View ▸ Reload): a read of its own is never undone, and the next check asks after it.
    @MainActor
    func put(_ show: () -> Void) {
        guard current() else { return }
        show()
        keep()
    }
}

/// What the live checks know of the answers a screen shows: each read's fingerprint, as the screen's own read or its
/// last check gave it. A check asks with it, and the same answer comes back as only "unchanged". It is a plain object the
/// screen never draws from, so a check that finds nothing new changes nothing a screen draws.
@MainActor
final class LiveMarks {
    private var prints: [String: String] = [:]
    /// When what the screen shows was last read, or found the same by a check: shown again later, the screen is checked
    /// at once if that is longer ago than its interval.
    var at: Date?

    func fingerprint(_ name: String, _ args: [String: Any] = [:]) -> String? {
        AnswerMemo.key(name, args).flatMap { prints[$0] }
    }

    /// The answer shown for a read is the one with this fingerprint (nil: one whose fingerprint is not known).
    func set(_ name: String, _ args: [String: Any] = [:], to fingerprint: String?) {
        guard let key = AnswerMemo.key(name, args) else { return }
        prints[key] = fingerprint
    }

    /// The screen's own read put what it shows in place: its fingerprint is the session's answer's (Engine.memo).
    func shown(_ name: String, _ args: [String: Any] = [:], by engine: Engine) {
        set(name, args, to: engine.heldFingerprint(name, args))
        at = Date()
    }

    func forget() {
        prints.removeAll()
    }
}

extension Engine {
    /// (1.3.80) One read of a screen checked live: nil when what the screen shows (`shown`) is what Canvas says now, else
    /// the new answer and what to remember once it is shown. The read is asked with its plain arguments — never one that
    /// sets a preference — and with the fingerprint `marks` holds for it; an answer that comes back new but says the same
    /// as the one shown (a first check of an answer kept from before) is the same.
    func liveCheck<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type, shown: T?, marks: LiveMarks,
                                 maxAge: Double) async throws -> LiveChange<T>? {
        let plain = AnswerMemo.plainArgs(name, args)
        let known = marks.fingerprint(name, plain)
        // (what the screen shows is still what was asked after: no read of its own put another answer in place meanwhile)
        let current: @MainActor () -> Bool = { marks.fingerprint(name, plain) == known }
        switch try await liveAnswer(name, plain, as: type, maxAge: maxAge, known: known) {
        case .same(let fingerprint):
            if current() { marks.set(name, plain, to: fingerprint) }
            return nil
        case .new(let value, let fingerprint):
            guard current() else { return nil }
            if let shown, LiveCompare.same(value, shown) {
                marks.set(name, plain, to: fingerprint)
                return nil
            }
            return LiveChange(value: value, current: current) { marks.set(name, plain, to: fingerprint) }
        }
    }
}

/// (1.3.80) A window's live checks. The screen on show says so (`liveChecks`) and is checked — two side by side each on
/// its own plan, one check at a time in the whole app (the windows share one page) — every minute, a course's pages
/// every ten minutes, while the window is in front of the student; a check due while one is under way is skipped. Held
/// back while anything in LiveHolds is so, and run as soon as that ends (a window brought back after longer than its
/// interval is checked at once). After a check that did not work the next waits 2, 4, then 5 minutes. Each check reads
/// the screen's calls with the check's age (its interval: the engine reads again only what is older), and the sidebar's
/// counts beside them; what changed is put in place unless something came up meanwhile that holds it back (then it is
/// checked again as soon as that ends). One line on the console a check: "live: dashboard unchanged · 0 reads · 0
/// redraws".
@MainActor
final class LiveRefresh {
    private struct Screen {
        let token: UUID
        let name: @MainActor () -> String
        var plan: LivePlan
        var check: @MainActor (Engine, Double) async throws -> LiveFound
    }

    private weak var engine: Engine?
    /// The window the screens are in (DetailStack's LiveWindowProbe says it).
    weak var window: NSWindow?
    /// The screens on show: one, or two side by side (the Inbox's list and the conversation beside it), each with its
    /// own plan.
    private var screens: [Screen] = []
    private var loop: Task<Void, Never>?
    /// The check under way in this window: its number, its screen, and since when.
    private var checking: (serial: Int, token: UUID, began: Date)?
    private var serial = 0
    /// What holds the checks back now, said on the console once as it begins (screenshot suite, developer mode).
    private var heldBy: String?

    /// A check under way in any window: one at a time.
    private static var busy = false
    /// Checks finished this launch (the screenshot suite's picture may wait for some: `-SimplLiveShotAfter 4`).
    private static var finished = 0
    private static let shotAfter = UserDefaults.standard.integer(forKey: "SimplLiveShotAfter")
    /// The screenshot suite's change to the mock school (`-SimplLiveChange done:8`), and when its clock started.
    private static let devChange = LivePlan.devChange(UserDefaults.standard.string(forKey: "SimplLiveChange"))
    private static var devChangeFrom: Date?
    private static var devChangeMade = false
    /// A check that has not answered in this long is let go: the next one may run (and waits as after one that did not
    /// work).
    private static let timeout: TimeInterval = 120

    init(engine: Engine) {
        self.engine = engine
    }

    /// The seconds between checks at a pace — or as the screenshot suite asks (`-SimplLiveEvery 5`,
    /// `-SimplLiveCourseEvery 5`).
    static func interval(_ pace: LivePlan.Pace) -> TimeInterval {
        let d = UserDefaults.standard
        let every: Double? = d.object(forKey: "SimplLiveEvery") == nil ? nil : d.double(forKey: "SimplLiveEvery")
        let course: Double? = d.object(forKey: "SimplLiveCourseEvery") == nil ? nil : d.double(forKey: "SimplLiveCourseEvery")
        return LivePlan.interval(pace, every: every, courseEvery: course)
    }

    /// How what a check found moves into place: the house's gentle spring; under Reduce Motion a short fade, nothing
    /// moving.
    static var motion: Animation {
        NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? .easeInOut(duration: 0.2) : Motion.gentle
    }

    // MARK: - The screens on show

    /// A screen on show says so: checked from now on, its first check one interval after `since` (when what it shows was
    /// last read; now when it does not know) — at once when that is already past. The same screen again only gives its
    /// check afresh, its plan as it was.
    func show(_ token: UUID, _ name: @escaping @MainActor () -> String, pace: LivePlan.Pace, since: Date?,
              check: @escaping @MainActor (Engine, Double) async throws -> LiveFound) {
        if let i = screens.firstIndex(where: { $0.token == token }) {
            screens[i].check = check
            return
        }
        screens.append(Screen(token: token, name: name, plan: LivePlan(every: Self.interval(pace), since: since ?? Date()), check: check))
        heldBy = nil
        run()
    }

    /// A screen is no longer on show (another went over it, or the window moved on): it is not checked, and what a check
    /// of it under way finds is dropped.
    func hide(_ token: UUID) {
        screens.removeAll { $0.token == token }
        if screens.isEmpty {
            loop?.cancel()
            loop = nil
        }
    }

    /// A screen's plan changed, if it is still on show.
    private func plan(_ token: UUID, _ change: (inout LivePlan) -> Void) {
        if let i = screens.firstIndex(where: { $0.token == token }) { change(&screens[i].plan) }
    }

    private func onShow(_ token: UUID) -> Bool {
        screens.contains { $0.token == token }
    }

    private func run() {
        guard loop == nil else { return }
        loop = Task { [weak self] in
            while !Task.isCancelled {
                guard let pause = await self?.step() else { return }
                try? await Task.sleep(nanoseconds: UInt64(pause * 1_000_000_000))
            }
        }
    }

    /// One turn: the first screen whose check is due started, if nothing holds it back. How long to wait before the next
    /// turn (a second at most: a hold that ends, or a window brought back, is noticed within it).
    private func step() async -> Double {
        guard let engine, !screens.isEmpty else { return 1 }
        Self.changeIfAsked(engine)
        if let c = checking, Date().timeIntervalSince(c.began) > Self.timeout {
            // (a check with no answer in two minutes is let go: the next may run, and waits as after one that did not work)
            checking = nil
            Self.busy = false
            plan(c.token) { $0.ended(worked: false, at: Date()) }
            if let s = screens.first(where: { $0.token == c.token }) {
                NSLog("[Simpl] %@", "live: \(s.name()) not checked (no answer in \(Int(Self.timeout)) s) · next in \(Int(s.plan.wait)) s")
            }
        }
        let holds = LiveHolds.shared.reasons(engine: engine, window: window)
        let now = Date()
        var pause = 1.0
        for s in screens {
            switch s.plan.decide(now: now, holds: holds, busy: Self.busy) {
            case .wait(let left):
                pause = min(pause, max(left, 0.2))
            case .hold(let why):
                if why != heldBy, OpenClock.verbose { NSLog("[Simpl] %@", "live: \(s.name()) waits (\(why))") }
                heldBy = why
            case .busy:
                break
            case .check:
                heldBy = nil
                start(s, engine: engine)
                return 0.5
            }
        }
        return pause
    }

    private func start(_ current: Screen, engine: Engine) {
        serial += 1
        let n = serial
        checking = (n, current.token, Date())
        Self.busy = true
        plan(current.token) { $0.began() }
        Task { [weak self] in await self?.perform(current, engine: engine, serial: n) }
    }

    /// A check: the screen's reads and the sidebar's counts asked again with the check's age, what changed put in place
    /// if nothing holds it back, and its line on the console once it has been drawn.
    private func perform(_ current: Screen, engine: Engine, serial n: Int) async {
        let age = current.plan.every
        let readsBefore = await engine.liveReads()
        let drawsBefore = LiveTrace.count(current.name())
        var found: LiveFound
        do {
            found = try await current.check(engine, age)
            // (the sidebar's counts — the Notifications and Inbox badges — beside every screen; a check of them that
            // does not work leaves them as they are)
            let main = engine.primary ?? engine
            if let s = try? await engine.liveCheck("snapshot", as: Snapshot.self, shown: engine.snapshot, marks: main.liveMarks, maxAge: age) {
                found.parts.append("sidebar")
                let screenApply = found.apply
                found.apply = {
                    screenApply?()
                    s.put { engine.showLiveSnapshot(s.value) }
                }
            }
        } catch {
            guard checking?.serial == n else { return }
            release(n)
            plan(current.token) { $0.ended(worked: false, at: Date()) }
            let wait = screens.first(where: { $0.token == current.token })?.plan.wait ?? age
            NSLog("[Simpl] %@", "live: \(current.name()) not checked (\(Self.why(error))) · next in \(Int(wait)) s")
            counted()
            return
        }
        // (let go meanwhile, or the screen no longer on show: what it found is dropped)
        guard checking?.serial == n, onShow(current.token) else {
            release(n)
            return
        }
        let readsAfter = await engine.liveReads()
        let reads = readsBefore.flatMap { b in readsAfter.map { $0 - b } }
        if !found.parts.isEmpty, let why = LiveHolds.shared.reasons(engine: engine, window: window).first {
            // (something came up while it was asked — a menu, a key pressed: nothing is put in place under it; the check
            // runs again as soon as that ends)
            release(n)
            plan(current.token) { $0.held() }
            NSLog("[Simpl] %@", "live: \(current.name()) found \(found.parts.joined(separator: ",")), held (\(why))")
            return
        }
        found.apply?()
        plan(current.token) { $0.ended(worked: true, at: Date()) }
        release(n)
        // (said once what was put in place has been drawn: the screen's redraws since the check began)
        try? await Task.sleep(nanoseconds: 600_000_000)
        let draws = drawsBefore.map { (LiveTrace.count(current.name()) ?? $0) - $0 }
        NSLog("[Simpl] %@", LivePlan.line(current.name(), parts: found.parts, reads: reads, draws: draws))
        counted()
    }

    /// The check numbered `n` is over: the next may run.
    private func release(_ n: Int) {
        guard checking?.serial == n else { return }
        checking = nil
        Self.busy = false
    }

    /// A check finished: the screenshot suite's picture, waiting for so many (`-SimplLiveShotAfter`), may be taken.
    private func counted() {
        Self.finished += 1
        if Self.finished == Self.shotAfter { Shot.ended() }
    }

    /// Why a check did not work, in a few words (never the page's own: it may name a course).
    private static func why(_ error: Error) -> String {
        guard let e = error as? EngineError else { return "no answer" }
        switch e {
        case .notReady(_): return "the page is loading"
        case .unreadable: return "an answer it could not read"
        case .page(_): return "the school said no"
        }
    }

    /// The screenshot suite's change to the mock school behind the engine's back (`-SimplLiveChange done:8`), made once,
    /// that many seconds after the main window's first screen was checked from: a check after it has to find it. The
    /// engine refuses it anywhere but the mock school, which is served from this Mac.
    private static func changeIfAsked(_ engine: Engine) {
        guard !devChangeMade, engine.primary == nil, let ask = devChange else { return }
        let from = devChangeFrom ?? Date()
        devChangeFrom = from
        guard Date().timeIntervalSince(from) >= ask.after else { return }
        devChangeMade = true
        Task {
            let text = (try? await engine.callText("liveDevChange", ["what": ask.what])) ?? ""
            let said = (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [String: Any]
            let what = (said?["id"] as? String).map { "made (\($0))" } ?? "not made (\(said?["error"] as? String ?? "no answer"))"
            NSLog("[Simpl] %@", "live: dev change \(ask.what) \(what)")
        }
    }
}

/// (1.3.80) What holds a live check back, and the check resumes when it ends: the window put away (minimized, covered,
/// the app not in front), no network, the page not ready or a screen's own read under way; a quiz, an Alta or a
/// Macmillan assignment open; the student
/// typing (a text field being edited, or a key pressed in the last 5 seconds); What-If on; any sheet, dialog, popup
/// (Hand In and Send from Phone among them — a tool's own popup aside), menu or the tour up; the mouse held down (a
/// drag); a counter's panel or a row's preview open. The screens say their own (`set`): What-If, a counter's panel.
@MainActor
final class LiveHolds {
    static let shared = LiveHolds()

    /// Holds the screens have put on, by why, each by whoever put it on.
    private var on: [String: Set<UUID>] = [:]
    private var watching = false
    private var keyWatch: Any?
    private var menuWatch: [NSObjectProtocol] = []
    private let network = NWPathMonitor()
    nonisolated(unsafe) private static var lastKey = Date.distantPast
    nonisolated(unsafe) private static var menuOpen = false
    nonisolated(unsafe) private static var online = true

    /// A screen's own hold (`why`), put on or taken off by `token`.
    func set(_ why: String, _ token: UUID, _ isOn: Bool) {
        if isOn {
            on[why, default: []].insert(token)
        } else {
            on[why]?.remove(token)
        }
    }

    /// Everything that holds a check of this window's screen back now (none: it may run).
    func reasons(engine: Engine, window: NSWindow?) -> [String] {
        watch()
        guard let window, window.isVisible else { return ["hidden"] }
        var out: [String] = []
        if window.isMiniaturized { out.append("minimized") }
        if !window.occlusionState.contains(.visible) { out.append("covered") }
        if !NSApp.isActive { out.append("inactive") }
        if !Self.online { out.append("offline") }
        if engine.phase != .native || !engine.pageReady { out.append("page") }
        if (engine.primary ?? engine).waiting > 0 { out.append("loading") } // (a screen's own read under way goes first)
        if engine.quiz != nil { out.append("quiz") }
        if engine.alta != nil || engine.macmillan != nil { out.append("assignment popup") }
        if typing { out.append("typing") }
        out += on.filter { !$0.value.isEmpty }.keys.sorted()
        if dialog(engine, window) { out.append("dialog") }
        if Self.menuOpen { out.append("menu") }
        if MacTour.shared.step != nil { out.append("tour") }
        if NSEvent.pressedMouseButtons != 0 { out.append("drag") }
        if WorkPreview.shared.item != nil { out.append("preview") }
        return out
    }

    /// The student is typing: a field being edited (its field editor has the keys), or a key pressed in the last 5 seconds.
    private var typing: Bool {
        if Date().timeIntervalSince(Self.lastKey) < 5 { return true }
        guard let editor = NSApp.keyWindow?.firstResponder as? NSTextView else { return false }
        return editor.isEditable
    }

    /// A sheet or a dialog over the window, a popup of its own (a tool's popup aside: the screen beside a tool is checked),
    /// the Settings window, the setup, What's New, a new task or message.
    private func dialog(_ engine: Engine, _ window: NSWindow) -> Bool {
        if window.attachedSheet != nil || NSApp.modalWindow != nil { return true }
        if engine.setup || engine.whatsNew != nil || engine.newTask || engine.newMessage || engine.settingsOpen { return true }
        // (a row's preview card is a popup too, "preview": it holds checks as the preview it is)
        return engine.popups.openKeys.contains { $0 != ToolsCenter.popupKey && $0 != "preview" }
    }

    /// The keys pressed, the menus open and the network watched, from the first time it is asked.
    private func watch() {
        guard !watching else { return }
        watching = true
        keyWatch = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            LiveHolds.lastKey = Date()
            return event
        }
        let center = NotificationCenter.default
        menuWatch = [
            center.addObserver(forName: NSMenu.didBeginTrackingNotification, object: nil, queue: .main) { _ in LiveHolds.menuOpen = true },
            center.addObserver(forName: NSMenu.didEndTrackingNotification, object: nil, queue: .main) { _ in LiveHolds.menuOpen = false },
        ]
        network.pathUpdateHandler = { path in LiveHolds.online = path.status == .satisfied }
        network.start(queue: .main)
    }
}

/// (1.3.80) How many times a screen has been drawn — the Dashboard counts its own — for a live check's line: "· 0
/// redraws" is a check that found nothing new and drew nothing.
@MainActor
enum LiveTrace {
    private static var drawn: [String: Int] = [:]

    @discardableResult
    static func drew(_ screen: String) -> Int {
        let n = (drawn[screen] ?? 0) + 1
        drawn[screen] = n
        return n
    }

    static func count(_ screen: String) -> Int? {
        drawn[screen]
    }
}

private struct LiveOnShowKey: EnvironmentKey {
    static let defaultValue = true
}

extension EnvironmentValues {
    /// (1.3.80) The screen is the one on show in its window's stack, not one under another pushed over it: only that one
    /// is checked live (DetailStack says so).
    var liveOnShow: Bool {
        get { self[LiveOnShowKey.self] }
        set { self[LiveOnShowKey.self] = newValue }
    }
}

/// (1.3.80) A screen checked live while it is on show (LiveRefresh): it says so as it appears or comes on show, and stops
/// as it goes or another goes over it.
private struct LiveChecked: ViewModifier {
    let name: @MainActor () -> String
    let pace: LivePlan.Pace
    let shown: Bool?
    let since: @MainActor () -> Date?
    let check: @MainActor (Engine, Double) async throws -> LiveFound
    @Environment(\.liveOnShow) private var onShow
    @Environment(\.engineLink) private var link
    @State private var token = UUID()
    @State private var here = false

    func body(content: Content) -> some View {
        content
            .onAppear {
                here = true
                say(shown ?? onShow)
            }
            .onDisappear {
                here = false
                link.acting.live.hide(token)
            }
            .onChange(of: shown ?? onShow) { _, now in
                if here { say(now) }
            }
    }

    private func say(_ on: Bool) {
        let live = link.acting.live
        if on {
            live.show(token, name, pace: pace, since: since(), check: check)
        } else {
            live.hide(token)
        }
    }
}

extension View {
    /// (1.3.80) This screen checked live while it is on show (LiveRefresh): `name` says it on the console, `pace` is how
    /// often, `shown` whether it is on show where its stack cannot say (the Dashboard, kept over the stack), `since` when
    /// what it shows was last read, and `check` asks again and says what changed.
    func liveChecks(_ name: String, pace: LivePlan.Pace = .minute, shown: Bool? = nil, since: @escaping @MainActor () -> Date? = { nil },
                    check: @escaping @MainActor (Engine, Double) async throws -> LiveFound) -> some View {
        modifier(LiveChecked(name: { name }, pace: pace, shown: shown, since: since, check: check))
    }

    /// (1.3.80) A screen read through a Loader, checked live: the read it last made asked again, and the new answer, if
    /// any, put in place (Loader.liveCheck). On the console by its read's name ("live: todo unchanged").
    func liveChecks<T: Decodable>(_ model: Loader<T>, pace: LivePlan.Pace = .minute) -> some View {
        modifier(LiveChecked(name: { [weak model] in model?.readName ?? "screen" }, pace: pace, shown: nil,
                             since: { [weak model] in model?.marks.at },
                             check: { [weak model] engine, age in
                                 guard let model else { return .unchanged }
                                 return try await model.liveCheck(engine, maxAge: age)
                             }))
    }
}

/// (1.3.80) The window its view is in, said to the window's live checks (they run only while it is in front of the
/// student). An unseen view (it takes no presses).
struct LiveWindowProbe: NSViewRepresentable {
    let live: LiveRefresh

    func makeNSView(context: Context) -> LiveWindowProbeView {
        let v = LiveWindowProbeView()
        v.live = live
        return v
    }

    func updateNSView(_ v: LiveWindowProbeView, context: Context) {
        v.live = live
        v.say()
    }
}

final class LiveWindowProbeView: NSView {
    weak var live: LiveRefresh?

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        say()
    }

    func say() {
        if let window { live?.window = window }
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

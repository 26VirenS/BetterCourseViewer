import Combine
import SwiftUI
import UIKit
import WebKit

/// What the bridge hears from the page about the app's own chrome (extension/content/app/native-app.js).
@MainActor
protocol ShellListener: AnyObject {
    func shellMessage(_ op: String, _ body: [String: Any])
}

enum EngineError: LocalizedError {
    case notReady(String) // (the school's site by name, Engine.lmsName)
    case unreadable
    case page(String)

    var errorDescription: String? {
        switch self {
        case .notReady(let site): return "\(site) is still loading. Try again in a moment."
        case .unreadable: return "The answer could not be read."
        case .page(let message): return message
        }
    }
}

private struct ErrorBox: Decodable { var error: String? }

/// Every tab's stack, by tab (a binding to one: `$engine.paths[dynamicMember: \Paths.[tab]]`).
struct Paths: Equatable {
    private var store: [AppTab: [Route]] = [:]
    subscript(tab: AppTab) -> [Route] {
        get { store[tab] ?? [] }
        set { store[tab] = newValue }
    }
}

/// The Canvas web view as the app's engine. One web view, with the extension in it, signed in to the
/// school's Canvas: underneath the native screens it answers what they show (call), and when a screen
/// of the web interface is pushed it is moved into that screen (attach) and asked to draw it (show).
/// The page says when the app's chrome is on (shell.state), what it drew (shell.page), and where a
/// press in it leads (shell.open, shell.tab). The tab selection and every tab's stack live here, so a
/// press in the page can push a screen on the stack showing.
@MainActor
final class Engine: ObservableObject, ShellListener {
    enum Phase { case starting, web, native }

    let host: String
    let web: WebController
    /// Where the web view sits when no web screen is showing it: the bottom layer of the app.
    let hostView = SlotView()

    @Published private(set) var phase: Phase = .starting
    @Published var tab: AppTab = .today
    @Published var paths = Paths()
    @Published private(set) var focus = false
    @Published private(set) var titles: [UUID: String] = [:]
    private(set) var revealed: Set<UUID> = []
    @Published private(set) var snapshot: Snapshot?
    /// The school's site as the page says it: "canvas" or "d2l" (Brightspace); nil until it has (Router.swift onBrightspace).
    @Published private(set) var lms: String?
    @Published var whatsNew: WhatsNewSheetItem? {
        didSet { if whatsNew == nil && oldValue != nil { schedulePopups() } }
    }
    /// Bumps when a screen's data may have changed under it (a tick in a web screen, an account change): the native screens read again.
    @Published private(set) var dataVersion = 0
    /// The tab bar's glass as drawn (TabBarProbe): a bar a screen puts in its place takes this size, these side
    /// margins, and sits this far below the safe area's bottom (ActionBar).
    @Published private(set) var barSize = CGSize(width: 0, height: 62)
    @Published private(set) var barSide: CGFloat = 21
    @Published private(set) var barLift: CGFloat = 14
    /// A screen with a bar of its own over the tab bar keeps the tab bar full size (no minimising as it scrolls).
    @Published var holdTabBar = false
    /// What a swipe on a piece of work asked for, for the screen it opens to do on arriving (1.4): Hand In
    /// (the screen's own main action) or the feedback.
    struct Arrival: Equatable {
        let id: String
        let feedback: Bool
    }
    var arrival: Arrival?
    /// An external tool open in its sheet (ToolSheet).
    @Published var tool: ToolLaunch?
    /// A quiz open in the app's own quiz screen (QuizScreen), over everything.
    @Published var quiz: QuizLaunch?
    /// The guided setup (SetupScreen): on the first run, and from Settings → Courses and Goals.
    @Published var setup = false {
        didSet { if !setup && oldValue { schedulePopups() } }
    }
    /// The app's Settings sheet is up (RootView says so): nothing else is put up over it.
    var settingsOpen = false {
        didSet { if !settingsOpen && oldValue { schedulePopups() } }
    }
    /// The search sheet, raised by the tab bar's search button (1.5.4: a sheet, not a page).
    /// (iPhone 1.8.1, build 48) Sign Out pressed in the picture's menu: asked once more, by the shell (NativeShell).
    @Published var askSignOut = false
    @Published var searchOpen = false {
        didSet { if !searchOpen && oldValue { schedulePopups() } }
    }
    /// The app's own popups still to show, in order (the first run's setup, What's New after an update).
    private var queuedPopups: [Popup] = []
    private var popupCheck: DispatchWorkItem?
    private var loginWatch: AnyCancellable?

    private var ready = false
    private var slots: [UUID: WeakBox<SlotView>] = [:]
    private(set) var active: UUID?
    private var screenURLs: [UUID: String] = [:]
    private struct Ask { var id: UUID; var url: String; var navigated: Bool }
    private var asked: Ask?
    private var insets: (top: CGFloat, bottom: CGFloat) = (0, 0)
    private var shownNative = false
    /// (1.7.6) Where the student was going as the app opened — a notification pressed, a link — kept until the school is
    /// known to have them signed in and the app's screens are up, then opened (checkSignIn).
    private var pendingOpen: String?
    /// A sign-in check under way; when the last one found the student signed in; and the page sent back to the school's
    /// sign-in by one, so the screens read afresh once it is done.
    private var checking = false
    private var lastSignedIn: Date?
    private var signingBackIn = false

    /// (iPhone 1.9) The app's screens are up from the first moment, on what they last showed (SavedAnswers), and the
    /// sign-in goes on behind them — the bar at the very top saying how it goes (status, TopStatusBar). So once anything
    /// has been kept for this school; the very first sign-in, with nothing kept, is the school's page itself, as before.
    private(set) var background: Bool
    /// (iPhone 1.9) The bar at the top: the sign-in behind the screens, and the screens' reads.
    let status = EngineStatus()
    /// (iPhone 1.9) A screen's read gave up waiting while the sign-in went on: every screen reads again once it is done.
    private var waitedOut = false
    /// (iPhone 1.9) The sign-in has gone on too long for anything to come of it (signInTimer): Login Failed, until the
    /// next page is asked for.
    private var tooLong = false
    private var signInTimer: DispatchWorkItem?
    /// (iPhone 1.9) Since when the school's sign-in has stood still with nothing being done to it, and since when
    /// Canvas's own page has stood loaded with the app's screens not on it (updateLink).
    private var schoolStillSince: Date?
    private var canvasStillSince: Date?
    /// (build 69) When the web view last began or finished a page: Finish Login waits until the page has stood still a
    /// while after it, so a school's sign-in still on its way is never taken for one waiting on the student.
    private var pageMovedAt = Date()
    /// (build 69) The one look again at the bar waiting to go (a newer one takes its place).
    private var linkRecheck: DispatchWorkItem?

    init(host: String) {
        self.host = host
        web = WebController(mode: .canvas(host: host))
        // (iPhone 1.9) what this school's screens last showed: up at once, signing in behind them
        SavedAnswers.shared.use(host: host)
        let kept = Engine.savedAnswer("snapshot", [:], as: Snapshot.self)
        // (with Simpl's own interface turned off, the page is what shows: nothing to put up over it)
        background = kept != nil && Bridge.shared.interfaceOn && !UserDefaults.standard.bool(forKey: "SimplNoSaved")
        if background {
            snapshot = kept
            lms = kept?.lms
            phase = .native
            status.link = .signingIn
        }
        Bridge.shared.shell = self
        hostView.take(web.webView, revealed: true)
        web.onNavigationStart = { [weak self] in self?.navigationStarted() }
        web.onFinish = { [weak self] url in self?.pageFinished(url) }
        web.onOpenInShell = { [weak self] url in self?.openWeb(url.absoluteString, title: "") }
        // the sign-in moving on (its cover lifting, "Stay logged in?" answered): a popup waiting may go up now; and, signing
        // in behind the screens, the bar at the top says where it has got
        loginWatch = web.login.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async {
                self?.schedulePopups()
                self?.updateLink()
            }
        }
        // (build 70) the other screens read ahead while the student is on one, unseen
        ReadAhead.shared.start(self)
        // the simulator suite's screens (.github/workflows/ios-shots.yml): -SimplTab courses
        if let t = UserDefaults.standard.string(forKey: "SimplTab") {
            if t == "calendar" { tab = .todo; paths[.todo] = [.calendar] } else if t == "search" { searchOpen = true } else if let first = AppTab(rawValue: t) { tab = first }
        }
    }

    func start() {
        if background { armSignInTimer() }
        web.loadIfNeeded()
        // a page that never says (a school's sign-in on another host, a page that failed): shown as it is
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 10_000_000_000)
            if self?.phase == .starting { self?.setPhase(.web) }
        }
        watchForSettled()
    }

    // MARK: - The simulator suite's "settled"

    /// The answers asked of the page and not yet given, and when the last one was asked or came (or the page last loaded).
    private var shotBusy = 0
    private var shotActivity = Date()

    /// (The simulator suite, scripts/dev/ios-shots.sh, launches with -SimplShotSettle YES.) Once the app's own screens
    /// are up, the page loaded, the sign-in done, a web screen asked for drawn, no popup still to go up, and nothing
    /// asked of the page for a moment and a half, "[Simpl Courses] shot: settled" is printed — once — on the console the
    /// suite reads, and its picture is taken then instead of after a set wait (the wait it gives is now only the
    /// longest). Nothing of this runs without it.
    private func watchForSettled() {
        guard UserDefaults.standard.bool(forKey: "SimplShotSettle") else { return }
        let began = Date()
        Task { [weak self] in
            for _ in 0..<480 { // (two minutes at most)
                try? await Task.sleep(nanoseconds: 250_000_000)
                guard let self = self else { return }
                if self.web.webView.isLoading { self.shotActivity = Date() }
                let up = Date().timeIntervalSince(began), quiet = Date().timeIntervalSince(self.shotActivity)
                let drawn = self.active.map { self.revealed.contains($0) } ?? true
                if up >= 3, self.phase == .native, self.web.login.settled, drawn, self.queuedPopups.isEmpty, self.shotBusy == 0, quiet >= 1.5 {
                    print("[Simpl Courses] shot: settled at \(String(format: "%.1f", up)) s")
                    return
                }
            }
        }
    }

    // MARK: - What the page says

    func shellMessage(_ op: String, _ body: [String: Any]) {
        switch op {
        case "shell.state":
            if let k = body["lms"] as? String, k != lms { lms = k; NotificationCenter.default.post(name: .simplLMSKnown, object: nil, userInfo: ["lms": k]) }
            let on = body["shell"] as? Bool ?? false
            web.shellOn = on
            if on {
                ready = true
                applyInsets()
                setPhase(.native)
                if !shownNative {
                    shownNative = true
                    Task { await self.firstNative() }
                }
                // (1.7.6) up on a page the school drew for the student: signed in. After a sign-in the check sent the
                // page back to, every screen reads again (what it asked meanwhile came back empty) — and (1.9) after one
                // behind the screens that a screen gave up waiting on
                lastSignedIn = Date()
                web.login.signedIn() // (build 64: the sign-in's tries by itself start afresh)
                if signingBackIn || waitedOut {
                    signingBackIn = false
                    waitedOut = false
                    changed()
                }
                updateLink()
                openPending()
            } else {
                ready = false
                if let id = active { detach(id) }
                if background { updateLink() } else { setPhase(.web) } // (1.9: the screens stay; the bar says it)
            }
        case "shell.page":
            pageDrawn(body)
        case "shell.open":
            if let url = body["url"] as? String { openWeb(url, title: body["title"] as? String ?? "") }
        case "shell.tab":
            switchTab(body["tab"] as? String ?? "today")
        case "shell.focus":
            withAnimation(.easeInOut(duration: 0.25)) { focus = body["on"] as? Bool ?? false }
        default:
            break
        }
    }

    private func setPhase(_ p: Phase) {
        guard phase != p else { return }
        withAnimation(.easeInOut(duration: 0.3)) { phase = p }
    }

    private func navigationStarted() {
        pageMovedAt = Date()
        ready = false
        if asked != nil { asked?.navigated = true }
        tooLong = false // (a page on its way: the sign-in is going somewhere again)
        updateLink()
    }

    private func pageFinished(_ url: URL?) {
        pageMovedAt = Date()
        // the school's sign-in, on its own host: nothing of ours runs there to say so
        if phase == .starting, let h = url?.host?.lowercased(), h != host.lowercased() { setPhase(.web) }
        updateLink()
    }

    // MARK: - Signing in behind the screens (iPhone 1.9)

    /// Where the sign-in has got, for the bar at the top (only while the app signs in behind its own screens): signed
    /// in; on its way (the page loading, the saved details going in); waiting on the student (a form with nothing saved
    /// for it, a two-step code, a school page standing still with nothing to fill); or failed (the saved details refused,
    /// the school not reached, too long).
    func updateLink() {
        guard background else { return }
        if ready && web.shellOn {
            schoolStillSince = nil
            canvasStillSince = nil
            tooLong = false
            setLink(.on)
            return
        }
        let l = web.login
        if let why = l.failure { setLink(.failed(why)); return }
        // (build 66) a try again by itself on its way (2, 5 or 12 seconds after a page that said something went wrong):
        // still signing in — not Finish Login over a failed page the app is about to leave by itself
        if l.retryPending {
            schoolStillSince = nil
            setLink(.signingIn)
            return
        }
        if case .capture(_, let error) = l.phase {
            if let error { setLink(.failed(error)); return }
            // (build 69) a form with nothing saved for it: Finish Login once the page has stopped moving — a form drawn on
            // a page that goes on by itself (a school's sign-in passing through) is still signing in
            setLink(stoodStill(for: 3) ? .needsInput : .signingIn)
            return
        }
        // (build 69) the page handed to the student (a two-step code, a page with nothing to fill): the same — while the
        // page is loading, or only just loaded, the sign-in is still on its way
        if l.handoff { setLink(stoodStill(for: 4) ? .needsInput : .signingIn); return }
        if l.phase == .working {
            schoolStillSince = nil
            setLink(.signingIn)
            return
        }
        if tooLong { setLink(.failed("Signing in is taking longer than it should.")); return }
        // the school's page, loaded and left as it is (nothing saved to fill it with, nothing it asks that the reader
        // can see): the student's to finish — after a moment, so a page that only passes through on its way to Canvas
        // is not taken for one
        if l.onSchoolPage && !web.webView.isLoading {
            let since = schoolStillSince ?? Date()
            schoolStillSince = since
            // (build 69: 6 seconds, not 2.5 — a school's sign-in often goes on by itself after a pause: a redirect its
            //  script makes, a second factor's frame loading — and the page loaded that long ago, too)
            if Date().timeIntervalSince(since) >= 6, stoodStill(for: 6) {
                setLink(.needsInput)
            } else {
                recheckLink(after: 1)
                setLink(.signingIn)
            }
            return
        }
        schoolStillSince = nil
        // Canvas's own page, loaded, and the app's screens never coming on over it (Simpl turned off on it): the page shown
        // as it is, as before 1.9, rather than a sign-in that never ends
        if let u = web.webView.url, u.host?.lowercased() == host.lowercased(), !LoginAssist.isSignIn(u.path), !web.webView.isLoading {
            // (build 64) Canvas reached only to answer with an error: the sign-in begun again by itself, not the page shown
            if web.login.canvasErrored {
                canvasStillSince = nil
                setLink(web.login.retry("\(lmsName) answered with an error.") ? .signingIn : .failed("\(lmsName) answered with an error."))
                return
            }
            let since = canvasStillSince ?? Date()
            canvasStillSince = since
            if Date().timeIntervalSince(since) >= 12 {
                leaveBackground()
                return
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 12.1) { [weak self] in self?.updateLink() }
        } else {
            canvasStillSince = nil
        }
        setLink(.signingIn)
    }

    /// Back to the way before 1.9: the page is what shows while the app's screens are not on (Simpl turned off).
    func leaveBackground() {
        guard background else { return }
        background = false
        signInTimer?.cancel()
        canvasStillSince = nil
        status.link = .on
        status.signInOpen = false
        setPhase(.web)
    }

    private func setLink(_ l: EngineStatus.Link) {
        guard status.link != l else { return }
        status.link = l
        if l == .signingIn { armSignInTimer() } else { signInTimer?.cancel() }
    }

    /// (build 69) Whether the web view has stood still — not loading, and no page begun or finished — for this long; if
    /// not yet, the bar is looked at again once it may have.
    private func stoodStill(for seconds: Double) -> Bool {
        let still = Date().timeIntervalSince(pageMovedAt)
        if !web.webView.isLoading && still >= seconds { return true }
        recheckLink(after: web.webView.isLoading ? 1 : max(0.2, seconds - still + 0.1))
        return false
    }

    /// (build 69) The bar looked at again in a moment — once, however many times it is asked for meanwhile.
    private func recheckLink(after seconds: Double) {
        linkRecheck?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.updateLink() }
        linkRecheck = work
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: work)
    }

    /// Signing in for 45 seconds with nothing to show for it: (build 64) begun again by itself, as any sign-in gone wrong is
    /// (LoginAssist.retry); once those tries are spent, Login Failed (Try Again, or the page to see why).
    private func armSignInTimer() {
        signInTimer?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self, self.background, !(self.ready && self.web.shellOn), self.status.link == .signingIn else { return }
            if self.web.login.retry("Signing in was taking too long.") {
                self.armSignInTimer()
                return
            }
            self.tooLong = true
            self.updateLink()
        }
        signInTimer = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 45, execute: work)
    }

    /// Try Again, on the bar's red strip or the sign-in's sheet: the sign-in begun afresh from Canvas.
    func retrySignIn() {
        tooLong = false
        schoolStillSince = nil
        web.login.restart() // (Canvas loaded afresh: it sends the school a new request; saved details are tried again)
        if background { setLink(.signingIn) }
    }

    /// The bar's strip pressed: the sign-in's page up over the app (SignInSheet).
    func showSignIn() {
        searchOpen = false
        status.signInOpen = true
    }

    /// Signed out (Settings): the screens go, and the school's sign-in is the whole screen, as on the app's first run.
    func signedOut() {
        background = false
        signInTimer?.cancel()
        status.link = .on
        status.signInOpen = false
        snapshot = nil
        if let id = active { detach(id) }
        ready = false
        web.shellOn = false
        shownNative = false
        setPhase(.starting)
    }

    private func firstNative() async {
        // (the simulator suite: -SimplPush /courses/101, or notifications)
        if let push = UserDefaults.standard.string(forKey: "SimplPush"), !push.isEmpty {
            if push == "notifications" { openNotifications() } else { openWeb(push, title: "") }
        }
        // (-SimplOpen quiz:101:9011, quiz:101:9011:take:5 straight into the attempt at question 6, quiz:101:9011:review its review,
        // tool:101:9 a course's tool, settings the Settings sheet)
        if let q = LaunchOpen.take("quiz:") {
            let parts = q.split(separator: ":").map(String.init)
            if parts.count >= 2 { quiz = QuizLaunch(course: parts[0], quiz: parts[1], title: "Quiz", begin: parts.count > 2 && (parts[2] == "take" || parts[2] == "review"), startAt: parts.count > 3 ? Int(parts[3]) : nil, review: parts.count > 2 && parts[2] == "review") }
        } else if let t = LaunchOpen.take("tool:") {
            let parts = t.split(separator: ":").map(String.init)
            if parts.count == 2 { tool = .courseTool(course: parts[0], id: parts[1], title: "Tool") }
        } else if LaunchOpen.take("settings") != nil {
            NotificationCenter.default.post(name: .simplOpenSettings, object: nil)
        }
        await refreshSnapshot()
        WidgetFeeder.shared.refresh(self) // (1.9: the widgets, from what Canvas says now)
        Task { await Reminders.shared.reschedule(self) } // (each launch: the reminders set again from what Canvas says now)
        Task { await Activity.shared.sync(self) } // (and what the stream holds now is seen: the app is showing it)
        // the first run: the iPhone's own setup (the courses that count, the goals), before anything else
        if snapshot?.setupDone == false || LaunchOpen.take("setup") != nil {
            queuePopup(.setup)
            return
        }
        Task { await NotificationAsk.afterSetup(self, delay: 4) } // (a setup made before this one asks, once)
        // after an update: what changed (seen once its sheet is really up — a sign-in still under way only delays it)
        if let wn = try? await call("whatsNew", ["due": true, "peek": true], as: WhatsNewData.self), !wn.releases.isEmpty {
            queuePopup(.whatsNew(wn))
        }
    }

    // MARK: - Still signed in? (1.7.6)

    /// Back in the app, however it came back (its icon, the app switcher, a notification, a link): the sign-in asked
    /// after (checkSignIn).
    func returned() {
        checkSignIn()
    }

    /// A notification pressed or a link opened: opened once the school is known to have the student signed in and the
    /// app's screens are up — never onto a screen left loading on a page that can no longer read anything.
    func openChecked(_ url: String) {
        pendingOpen = url
        checkSignIn()
    }

    /// Whether the school still has the student signed in, asked of it directly with the web view's own sign-in
    /// (Activity.signIn) while the app's screens are up — at every return to the app, a notification pressed, a link
    /// opened, and when the page cannot answer a screen. Ended (the session ran out while the app was away), the page
    /// goes back to the school's sign-in, which the saved sign-in fills in ("Logging you in"), and the screens come back
    /// once it is done; then what was asked for opens. With the screens not up, the page on its way or the sign-in
    /// showing is the check itself, and what was asked for waits for the screens. Asked again within half a minute of a
    /// good answer, it is taken as still good.
    func checkSignIn() {
        guard phase == .native, !checking else { return }
        if background && status.link != .on { return } // (1.9: signing in behind the screens already — that is the check)
        if let last = lastSignedIn, Date().timeIntervalSince(last) < 30 {
            openPending()
            return
        }
        checking = true
        Task {
            let answer = await signInAnswer()
            checking = false
            switch answer {
            case .signedOut where phase == .native:
                print("[Simpl Courses] sign-in: ended — back to the school's sign-in")
                signBackIn()
            case .signedIn:
                lastSignedIn = Date()
                openPending()
            default:
                openPending() // (no answer — offline, the school down: the screens as they are)
            }
        }
    }

    private func signInAnswer() async -> Activity.SignIn {
        if web.isTestSchool { return TestSchool.signedIn ? .signedIn : .signedOut } // (the test school's sign-in is the app's own)
        let cookies = await web.webView.configuration.websiteDataStore.httpCookieStore.allCookies()
        return await Activity.signIn(at: web.baseURL, cookies: cookies, lms: lms)
    }

    /// What was asked for while the sign-in was checked, opened once the screens are up and nothing is being checked.
    private func openPending() {
        guard phase == .native, !checking, let url = pendingOpen else { return }
        pendingOpen = nil
        go(url, title: "")
    }

    /// The page back to the school's sign-in: what is over the screens put away, the splash while the page goes, the
    /// sign-in (or "Logging you in") once it is there; shown as it is if nothing has said otherwise in ten seconds.
    private func signBackIn() {
        signingBackIn = true
        lastSignedIn = nil
        searchOpen = false
        tool = nil
        quiz = nil
        if let id = active { detach(id) }
        ready = false
        web.shellOn = false
        if background { // (1.9: the screens stay as they are, the bar at the top running, while it signs back in)
            tooLong = false
            setLink(.signingIn)
            web.load()
            return
        }
        setPhase(.starting)
        web.load()
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 10_000_000_000)
            if self?.phase == .starting { self?.setPhase(.web) }
        }
    }

    // MARK: - The app's own popups, one at a time

    enum Popup {
        case setup
        case whatsNew(WhatsNewData)
    }

    /// A popup to show when the screen is free for it.
    func queuePopup(_ p: Popup) {
        queuedPopups.append(p)
        schedulePopups()
    }

    /// Free for a popup: the app's own screens up, the page loaded, nothing of the sign-in on screen or still to
    /// come ("Logging you in", the form, "Stay logged in?"), and no other popup, sheet or screen over everything.
    private var popupFree: Bool {
        phase == .native && !web.webView.isLoading && web.login.settled && !setup && whatsNew == nil && quiz == nil && tool == nil && !settingsOpen && !searchOpen
    }

    /// Looked at again in a moment (a popup going away, the sign-in moving on), and again until the next one is up.
    func schedulePopups(after delay: Double = 0.7) {
        guard !queuedPopups.isEmpty else { return }
        popupCheck?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.presentNextPopup() }
        popupCheck = work
        DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
    }

    private func presentNextPopup() {
        guard !queuedPopups.isEmpty else { return }
        guard popupFree else {
            schedulePopups(after: 1.0)
            return
        }
        switch queuedPopups.removeFirst() {
        case .setup: setup = true
        case .whatsNew(let d): whatsNew = WhatsNewSheetItem(data: d)
        }
    }

    func refreshSnapshot() async {
        if let s = try? await call("snapshot", as: Snapshot.self, keep: true) {
            snapshot = s
            if let k = s.lms, k != lms { lms = k; NotificationCenter.default.post(name: .simplLMSKnown, object: nil, userInfo: ["lms": k]) }
        }
    }

    func setBar(_ b: TabBarProbe.Bar) {
        let s = b.size
        guard s.width.isFinite, s.height.isFinite, s.width > 0, s.height > 0, b.side.isFinite, b.lift.isFinite else { return }
        if abs(s.width - barSize.width) > 0.5 || abs(s.height - barSize.height) > 0.5 { barSize = s }
        if abs(b.side - barSide) > 0.5 { barSide = b.side }
        if abs(b.lift - barLift) > 0.5 { barLift = b.lift }
    }

    func changed() {
        ReadAhead.shared.forget() // (build 70: what was read ahead is stale now — read again, unseen, after the screens showing)
        dataVersion += 1
        Task { await refreshSnapshot() }
        Task { await Reminders.shared.reschedule(self) } // (a tick, a hand-in: its reminders go)
        WidgetFeeder.shared.refresh(self) // (1.9: and the widgets show it)
    }

    // MARK: - Calls

    /// A call into the page's engine (BCVNative.call), its answer decoded. Waits for the page to be ready
    /// (a page load under way), and tries once more if the page went away mid-call.
    func call<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type, keep: Bool = false, keepAs: [String: Any]? = nil) async throws -> T {
        shotBusy += 1 // (the simulator suite's picture waits for the page's answers: watchForSettled)
        shotActivity = Date()
        defer {
            shotBusy = max(0, shotBusy - 1)
            shotActivity = Date()
        }
        var lastError: Error = EngineError.notReady(lmsName)
        for attempt in 0..<3 {
            await whenReady()
            guard ready else {
                checkSignIn() // (1.7.6: a page that never came ready — the sign-in may have ended under it)
                throw EngineError.notReady(lmsName)
            }
            do {
                let raw = try await web.webView.callAsyncJavaScript("return JSON.stringify(await BCVNative.call(name, args))", arguments: ["name": name, "args": args], in: nil, contentWorld: .defaultClient)
                guard let text = raw as? String, let data = text.data(using: .utf8) else { throw EngineError.unreadable }
                if let box = try? JSONDecoder().decode(ErrorBox.self, from: data), let message = box.error { throw EngineError.page(message) }
                let answer = try JSONDecoder().decode(T.self, from: data)
                if keep { SavedAnswers.shared.keep(data, for: SavedAnswers.key(name, keepAs ?? args)) } // (1.9: shown at once next time)
                return answer
            } catch let e as EngineError {
                if case .page = e { checkSignIn() } // (1.7.6: the page could not read it — the sign-in may have ended)
                throw e
            } catch let e as DecodingError {
                print("[Simpl Courses] \(name) answer not understood:", e)
                throw EngineError.unreadable
            } catch {
                lastError = error
                if attempt < 2 { try? await Task.sleep(nanoseconds: 400_000_000) }
            }
        }
        throw lastError
    }

    /// (iPhone 1.9) A screen's read: the fresh answer, kept on the phone for the next time the screen opens (`saved`),
    /// the bar at the very top running while it comes.
    /// `keepAs`: the arguments it is kept under, where they are not the ones asked with (a read forced afresh is kept
    /// as the plain read the screen asks for next time).
    /// (build 70) A screen's read never runs while reading ahead is under way only for itself: reading ahead waits for a
    /// quiet moment after it (ReadAhead). And an answer read ahead in the last minute is fresh already: its screen reads it
    /// again without the bar (nothing is loading that the student is waiting for).
    func fetch<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type, keepAs: [String: Any]? = nil) async throws -> T {
        let key = SavedAnswers.key(name, keepAs ?? args)
        let quiet = ReadAhead.shared.fresh(key, within: 60)
        ReadAhead.shared.screenBegan()
        if !quiet { status.began() }
        defer {
            if !quiet { status.ended() }
            ReadAhead.shared.screenEnded(key)
        }
        return try await call(name, args, as: type, keep: true, keepAs: keepAs)
    }

    /// (iPhone 1.9) What this read answered last time, kept on the phone: shown at once while `fetch` reads it afresh.
    func saved<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type) -> T? {
        Engine.savedAnswer(name, args, as: type)
    }

    nonisolated static func savedAnswer<T: Decodable>(_ name: String, _ args: [String: Any], as type: T.Type) -> T? {
        guard let d = SavedAnswers.shared.data(SavedAnswers.key(name, args)) else { return nil }
        return try? JSONDecoder().decode(T.self, from: d)
    }

    /// (iPhone 1.9) Refresh: everything read afresh from Canvas — the page's own caches emptied, then every screen
    /// showing reads again (changed) — the bar at the top running from the press.
    func forceRefresh() async {
        Haptics.tap()
        status.began()
        _ = try? await call("refresh", as: OK.self)
        status.ended()
        changed()
    }

    /// An action whose answer is only whether it worked.
    @discardableResult
    func act(_ name: String, _ args: [String: Any] = [:]) async -> Bool {
        do {
            let r = try await call(name, args, as: OK.self)
            return r.ok ?? true
        } catch {
            Haptics.error()
            return false
        }
    }

    private func whenReady(timeout: Double = 25) async {
        let start = Date()
        while !ready && Date().timeIntervalSince(start) < timeout {
            try? await Task.sleep(nanoseconds: 100_000_000)
        }
        if !ready && background { waitedOut = true } // (1.9: read again once the sign-in behind the screens is done)
    }

    // MARK: - Navigation

    /// A Canvas address on the stack showing: its native screen (a course and everything in it, Groups, the
    /// Inbox — Router.swift), a tool or a quiz over everything, a file in the phone's viewer, a tab; else where
    /// Canvas sends it (1.6: never the web interface's screen).
    func openWeb(_ url: String, title: String) {
        if let t = toolLaunch(for: url, title: title) {
            openTool(t)
            return
        }
        if let q = quizLaunch(for: url, title: title) {
            openQuiz(q)
            return
        }
        if let file = fileDownload(for: url) {
            let name = title.isEmpty ? file.lastPathComponent : title
            // (1.7.3) a Brightspace content file Brightspace will not hand over opens in its own viewer instead
            var instead: (() -> Void)?
            if let v = schoolViewer(forDownload: file) { instead = { [weak self] in self?.openWebScreen(v.absoluteString, title: name) } }
            FilePreview.shared.open(file, name: name, in: web.webView, instead: instead)
            return
        }
        if let route = nativeRoute(for: url, title: title) {
            if case .assignment(_, let id) = route, let u = absolute(url), Engine.isSubmission(u) { arrival = Arrival(id: id, feedback: true) }
            push(route)
            return
        }
        if let t = tabName(for: url) {
            switchTab(t)
            return
        }
        resolveThenOpen(url, title: title)
    }

    /// (1.6) An address with a native screen of some kind: a tool, a quiz, a file, a screen, a tab.
    private func opensNatively(_ url: String) -> Bool {
        toolLaunch(for: url, title: "") != nil || quizLaunch(for: url, title: "") != nil || fileDownload(for: url) != nil
            || nativeRoute(for: url, title: "") != nil || tabName(for: url) != nil
    }

    private struct Resolved: Decodable { var url: String }

    /// (1.6) An address with no screen as it stands — a module item's, or one Canvas redirects — opened where Canvas
    /// sends it: a module item is the item it names. Only what has no screen anywhere (a Canvas page the app does
    /// not draw: Collaborations, Outcomes…) shows Canvas's own page, in a sheet over the app; never the web interface.
    private func resolveThenOpen(_ raw: String, title: String) {
        guard let from = absolute(raw), isCanvasHost(from) else {
            if let u = absolute(raw) { web.openExternally(u) }
            return
        }
        Task {
            let to = try? await call("resolveUrl", ["url": from.absoluteString], as: Resolved.self)
            if let to, !same(to.url, from.absoluteString), let u = absolute(to.url) {
                if !isCanvasHost(u) { web.openExternally(u); return }
                if opensNatively(to.url) { openWeb(to.url, title: title); return }
            }
            openCanvasPage(from.absoluteString, title: title)
        }
    }

    private func isCanvasHost(_ u: URL) -> Bool {
        let s = u.scheme?.lowercased() ?? ""
        if TestSchool.owns(u) { return TestSchool.owns(web.baseURL) } // (the test school's own scheme: its addresses are its)
        return (s == "http" || s == "https") && u.host?.lowercased() == web.baseURL.host?.lowercased()
    }

    /// Canvas's own page for an address ("Open Canvas's Page", a kind of quiz question the app does not answer, a part of
    /// Canvas with no screen in the app), in a sheet over the app with the Canvas session (1.6: no longer the web interface's
    /// screen pushed on the stack). Over a quiz or another sheet, it waits for that one to go first.
    func openWebScreen(_ url: String, title: String) {
        guard let raw = absolute(url) else { return }
        Task {
            let u = await schoolPage(for: raw) // (on Brightspace: the Brightspace page for the interface's address, 2.99.22)
            let name = title.isEmpty ? lmsName : title
            let show = { [weak self] in self?.tool = ToolLaunch(title: name, args: ["page": u.absoluteString]) }
            if quiz != nil || tool != nil {
                quiz = nil
                tool = nil
                try? await Task.sleep(nanoseconds: 450_000_000)
                show()
            } else {
                Haptics.tap()
                show()
            }
        }
    }

    func openCanvasPage(_ url: String, title: String) { openWebScreen(url, title: title) }

    /// (iPhone 1.8.1) Two pages side by side (PaneStack, Shell.swift) and where the fold runs, in window coordinates —
    /// set by the shell from the window's size.
    var twoPane = false
    var foldX: CGFloat?
    /// (iPhone 1.8.1, build 36) The window's own bounds, read from UIKit (TabBarProbe): the whole screen, whatever SwiftUI
    /// says of its safe areas — its middle is where the Duo folds.
    @Published private(set) var screen: CGRect = .zero
    /// (iPhone 1.8.1, build 43) A rail down the screen's side (the iPhone Duo folded, or open on its side), where iOS
    /// puts the bars' buttons one under another (TabBarProbe, ShellToolbar).
    @Published private(set) var rail = false
    /// (Build 47) When the rail last came or went: more than a few times in a few seconds (the screen's bars moving into
    /// it or out of it changing the very safe areas it is told by) and it is held as it is.
    private var railChanges: [Date] = []
    private var railHeld = false
    func setRail(_ on: Bool) {
        guard on != rail, !railHeld else { return }
        let now = Date()
        railChanges = railChanges.filter { now.timeIntervalSince($0) < 3 } + [now]
        if railChanges.count > 6 {
            railHeld = true
            print("[Simpl Courses] rail: came and went \(railChanges.count) times in 3 s; held as it is")
            return
        }
        rail = on
        print("[Simpl Courses] rail: \(on ? "down the side" : "none")")
    }
    func setScreen(_ r: CGRect) {
        guard TabBarProbe.usable(r), r.width > 0, abs(r.width - screen.width) > 0.5 || abs(r.height - screen.height) > 0.5 || abs(r.minX - screen.minX) > 0.5 else { return }
        screen = r
    }
    /// The last press on the app's own screens (none in a sheet): how far across the window, and when (PaneTouches).
    private var lastTouch: (x: CGFloat, at: Date)?

    func touched(x: CGFloat, inSheet: Bool) {
        lastTouch = inSheet ? nil : (x, Date())
    }

    /// (iPhone 1.8.1, build 37) Per tab, where its split view's list column ends (window x) while two columns show; nil
    /// when the split view is one stack (TabSplit).
    var splitEdge: [AppTab: CGFloat] = [:]
    /// (iPhone 1.8.1, build 41) Per tab, how many of its stack's first screens the left column shows itself while two
    /// columns show: 1 for Courses with a course open (its home on the left, TabSplit), 0 otherwise (the tab's own list).
    var splitLead: [AppTab: Int] = [:]

    /// A native screen on the stack showing (not twice in a row). (1.8.1, build 37) With the split view showing two
    /// columns, a screen opened from the left column — another assignment in Today, a section of the course whose home
    /// is there — is the right column's own, in place of whatever was open there (as a row picked in Mail's list);
    /// opened from the right column, it is pushed there. (Build 41) Another course opened while a course's home is on
    /// the left takes the left's place.
    func push(_ route: Route) {
        var path = paths[tab]
        if path.last == route { return }
        let lead = splitEdge[tab] == nil ? 0 : (splitLead[tab] ?? 0)
        if lead > 0, case .course = route {
            lastTouch = nil
            paths[tab] = [route]
            return
        }
        if let edge = splitEdge[tab], let t = lastTouch, Date().timeIntervalSince(t.at) < 3, t.x < edge {
            lastTouch = nil
            let next = Array(path.prefix(lead)) + [route]
            if path == next { return }
            paths[tab] = next
            return
        }
        path.append(route)
        lastTouch = nil
        paths[tab] = path
    }

    /// (iPhone 1.8.1, build 41) A course picked in the course strip (CourseStrip): its home, with nothing opened from
    /// it yet.
    func focusCourse(_ id: String) {
        lastTouch = nil
        let next = [Route.course(id: id)]
        if paths[.courses] != next { paths[.courses] = next }
    }

    /// An external tool, in a sheet of its own over everything.
    func openTool(_ t: ToolLaunch) {
        Haptics.tap()
        tool = t
    }

    func openQuiz(_ q: QuizLaunch) {
        Haptics.tap()
        quiz = q
    }

    /// A swipe on a piece of work: hand it in (take the quiz, open the discussion) or see its feedback, from the
    /// list — the quiz in its screen (straight at the feedback), an assignment's screen doing it on arrival.
    func act(on raw: String?, title: String, feedback: Bool) {
        guard let raw, !raw.isEmpty else { return }
        Haptics.play("medium")
        if var q = quizLaunch(for: raw, title: title) {
            q.feedback = feedback
            quiz = q // (the swipe's own feel already played: not openQuiz's as well)
            return
        }
        if let route = nativeRoute(for: raw, title: title), case .assignment(_, let id) = route {
            arrival = Arrival(id: id, feedback: feedback)
            push(route)
            return
        }
        openWeb(raw, title: title)
    }

    func openNotifications() { push(.notifications) }

    /// The Calendar, pushed on the stack showing (it has no tab of its own).
    func openCalendar() { push(.calendar) }

    private func switchTab(_ name: String) {
        if name == "notifications" {
            tab = .today
            paths[.today] = [.notifications]
            return
        }
        if name == "calendar" { // (a link to the calendar in a page: To Do, with the Calendar on it)
            tab = .todo
            paths[.todo] = [.calendar]
            return
        }
        if name == "search" { // (Search is a sheet over the screen showing, not a tab)
            searchOpen = true
            return
        }
        let t = AppTab(rawValue: name) ?? .today
        paths[t] = []
        tab = t
    }

    /// A search result pressed: the sheet goes first, then the result opens on the tab underneath (a quiz, a tool or
    /// Safari over everything, as anywhere else).
    func openFromSearch(_ url: String, title: String, external: Bool) {
        searchOpen = false
        Task {
            try? await Task.sleep(nanoseconds: 350_000_000) // (the sheet away before anything else is shown)
            if external, let u = URL(string: url) { web.openExternally(u) } else { openWeb(url, title: title) }
        }
    }

    /// A page the app hands to the web interface whole (the guided setup): the chrome steps aside until it is done.
    func loadFull(_ path: String) {
        guard let url = URL(string: path, relativeTo: web.baseURL) else { return }
        if let id = active { detach(id) }
        web.webView.load(URLRequest(url: url))
    }

    func reload() {
        web.webView.reload()
    }

    func absolute(_ url: String) -> URL? {
        URL(string: url, relativeTo: web.baseURL)?.absoluteURL
    }

    private func same(_ a: String?, _ b: String?) -> Bool {
        guard let a = a, let b = b, let x = absolute(a), let y = absolute(b) else { return false }
        func key(_ u: URL) -> String {
            var p = u.path
            while p.count > 1 && p.hasSuffix("/") { p.removeLast() }
            return "\(p)?\(u.query ?? "")#\(u.fragment ?? "")"
        }
        return key(x) == key(y)
    }

    // MARK: - The web view's place

    func register(_ slot: SlotView, for id: UUID) {
        slots[id] = WeakBox(slot)
        if let p = pendingAttach, p.id == id {
            pendingAttach = nil
            attach(id, url: p.url)
        }
    }
    private var pendingAttach: (id: UUID, url: String)?

    /// A web screen's body went away (its screen left the stack). Called while SwiftUI is taking its views
    /// down: what views read (the titles) is changed after that, never during it.
    func unregister(_ id: UUID) {
        if active == id {
            active = nil
            hostView.take(web.webView, revealed: true)
        }
        slots[id] = nil
        screenURLs[id] = nil
        revealed.remove(id)
        DispatchQueue.main.async { [weak self] in self?.titles[id] = nil }
    }

    /// A web screen came on: the web view moves into it (the screen it leaves keeps a picture of itself) and draws its address.
    func attach(_ id: UUID, url: String) {
        if screenURLs[id] == nil { screenURLs[id] = url }
        guard let slot = slots[id]?.value else {
            pendingAttach = (id, url) // (its body is not made yet: attached as it is)
            return
        }
        if active != id {
            if let old = active, let oldSlot = slots[old]?.value { oldSlot.freeze(web.webView) }
            active = id
            revealed.remove(id)
            slot.take(web.webView, revealed: false)
        }
        show(screenURLs[id] ?? url, for: id)
    }

    /// A web screen went off (a tab switched, a native screen pushed over it): the web view goes back underneath.
    func detach(_ id: UUID) {
        if pendingAttach?.id == id { pendingAttach = nil }
        guard active == id else { return }
        slots[id]?.value?.freeze(web.webView)
        active = nil
        asked = nil
        hostView.take(web.webView, revealed: true)
    }

    private func show(_ url: String, for id: UUID) {
        asked = Ask(id: id, url: url, navigated: false)
        let target = absolute(url)?.absoluteString ?? url
        Task {
            await whenReady()
            guard asked?.id == id, ready else { return }
            _ = try? await web.webView.callAsyncJavaScript("return await BCVNative.show(url)", arguments: ["url": target], in: nil, contentWorld: .defaultClient)
        }
        // never a screen left blank: whatever the page has drawn shows after a while
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 8_000_000_000)
            guard let self = self, self.active == id, !self.revealed.contains(id) else { return }
            self.reveal(id)
        }
    }

    private func pageDrawn(_ body: [String: Any]) {
        guard let id = active, let a = asked, a.id == id else { return }
        let url = body["url"] as? String
        let askedURL = body["asked"] as? String
        let matches: Bool
        if let askedURL = askedURL { matches = same(askedURL, a.url) } else { matches = a.navigated || same(url, a.url) }
        guard matches else { return }
        if let title = body["title"] as? String, !title.isEmpty { titles[id] = title }
        if let url = url { screenURLs[id] = url }
        let f = body["focus"] as? Bool ?? false
        if f != focus { withAnimation(.easeInOut(duration: 0.25)) { focus = f } } // (as shell.focus does: the bars go, never snap)
        reveal(id)
    }

    private func reveal(_ id: UUID) {
        revealed.insert(id)
        slots[id]?.value?.reveal(web.webView)
        applyInsets()
    }

    /// The page's insets are the app's bars (app.css: --bcv-shell-top / --bcv-shell-bottom).
    func setInsets(top: CGFloat, bottom: CGFloat) {
        guard abs(insets.top - top) > 0.5 || abs(insets.bottom - bottom) > 0.5 else { return }
        insets = (top, bottom)
        applyInsets()
    }

    private func applyInsets() {
        guard ready else { return }
        let js = "document.documentElement.style.setProperty('--bcv-shell-top','\(Int(insets.top.rounded()))px');document.documentElement.style.setProperty('--bcv-shell-bottom','\(Int(insets.bottom.rounded()))px');"
        web.webView.evaluateJavaScript(js, in: nil, in: .defaultClient) { _ in }
    }
}

/// What's New as a sheet's item.
struct WhatsNewSheetItem: Identifiable {
    let id = UUID()
    let data: WhatsNewData
}

/// A place the web view can sit: a web screen's body, or the app's bottom layer. It keeps a picture of
/// the page while the web view is elsewhere, so the screen stays as it was during a push, a pop or a
/// tab switch, and shows the web view again once its page is drawn.
final class SlotView: UIView {
    private var picture: UIView?
    private var spinner: UIActivityIndicatorView?
    private var savedOffset: CGPoint?
    var onInsets: ((UIEdgeInsets) -> Void)?
    var slotID: UUID?
    weak var owner: Engine?

    override init(frame: CGRect) {
        super.init(frame: frame)
        backgroundColor = .systemBackground
        clipsToBounds = true
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    func take(_ webView: WKWebView, revealed: Bool) {
        if webView.superview !== self {
            webView.removeFromSuperview()
            webView.frame = bounds
            webView.autoresizingMask = [.flexibleWidth, .flexibleHeight]
            insertSubview(webView, at: 0)
        }
        if revealed {
            webView.alpha = 1
            clearPicture()
        } else if picture == nil {
            webView.alpha = 0
            showSpinner()
        } else {
            webView.alpha = 0
        }
    }

    func freeze(_ webView: WKWebView) {
        guard webView.superview === self else { return }
        savedOffset = webView.scrollView.contentOffset
        if picture == nil, let snap = webView.snapshotView(afterScreenUpdates: false) {
            snap.frame = bounds
            snap.autoresizingMask = [.flexibleWidth, .flexibleHeight]
            addSubview(snap)
            picture = snap
        }
    }

    func reveal(_ webView: WKWebView) {
        guard webView.superview === self else { return }
        if let offset = savedOffset {
            savedOffset = nil
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) {
                let limit = Swift.max(0, webView.scrollView.contentSize.height - webView.scrollView.bounds.height)
                webView.scrollView.setContentOffset(CGPoint(x: 0, y: Swift.min(offset.y, limit)), animated: false)
            }
        }
        UIView.animate(withDuration: 0.18) {
            webView.alpha = 1
            self.picture?.alpha = 0
            self.spinner?.alpha = 0
        } completion: { _ in
            self.clearPicture()
        }
    }

    private func clearPicture() {
        picture?.removeFromSuperview()
        picture = nil
        spinner?.removeFromSuperview()
        spinner = nil
    }

    private func showSpinner() {
        guard spinner == nil else { return }
        let s = UIActivityIndicatorView(style: .large)
        s.translatesAutoresizingMaskIntoConstraints = false
        addSubview(s)
        NSLayoutConstraint.activate([s.centerXAnchor.constraint(equalTo: centerXAnchor), s.centerYAnchor.constraint(equalTo: centerYAnchor)])
        s.startAnimating()
        spinner = s
    }

    override func safeAreaInsetsDidChange() {
        super.safeAreaInsetsDidChange()
        onInsets?(safeAreaInsets)
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        onInsets?(safeAreaInsets)
    }
}

/// The app's bottom layer: where the web view lives when no web screen shows it (and, before the
/// shell is on, the whole app: the sign-in, the guided setup).
struct EngineHost: UIViewRepresentable {
    let engine: Engine

    func makeUIView(context: Context) -> SlotView { engine.hostView }
    func updateUIView(_ uiView: SlotView, context: Context) {}
}


// MARK: - Reading ahead (build 70)

extension Engine {
    /// (build 70) Whether the app may read ahead now: signed in with its screens on and the page ready, in the
    /// foreground, not taking a quiz, the sign-in not under way, not in Low Power Mode, and not the simulator suite's
    /// pictures (each waits for the page's answers to settle).
    var mayReadAhead: Bool {
        ready && web.shellOn && !focus && status.link == .on
            && UIApplication.shared.applicationState == .active
            && !ProcessInfo.processInfo.isLowPowerModeEnabled
            && !UserDefaults.standard.bool(forKey: "SimplShotSettle")
    }
}

/// (iPhone 1.9, build 70) Reading ahead: while the student is on one screen (a course's page, an assignment, anything),
/// the time is used to read the others — Today, To Do, the courses, Grades, the calendar as it was last shown,
/// Notifications, the Inbox, then each course's home and grades — so each opens on what Canvas says now, at once. One
/// read at a time, only after a screen's own reads have been quiet for three seconds, a moment between each; each read
/// again once it is five to fifteen minutes old (and all of them after something changes: a tick, a hand-in, Refresh).
/// None of it shows: the bar at the top is for what the student is waiting on, and nothing here is (Engine.fetch).
/// What it reads is kept as the screen's own read is (SavedAnswers), under the screen's own key; each asks the page for
/// Canvas's answers no older than its own age (`maxAge`), so a read ahead is a fresh one, not the page's last.
@MainActor
final class ReadAhead {
    static let shared = ReadAhead()

    private struct Read {
        let name: String
        let args: [String: Any]
        /// How old it may be before it is read again (seconds).
        let every: TimeInterval
    }

    private weak var engine: Engine?
    private var loop: Task<Void, Never>?
    /// When each answer was last read (by its screen or ahead of it), by its key.
    private var readAt: [String: Date] = [:]
    private var screenReads = 0
    private var lastScreenRead = Date.distantPast

    func start(_ engine: Engine) {
        self.engine = engine
        guard loop == nil else { return }
        loop = Task { [weak self] in await self?.run() }
    }

    /// A screen's own read beginning and ending (Engine.fetch).
    func screenBegan() {
        screenReads += 1
        lastScreenRead = Date()
    }

    func screenEnded(_ key: String) {
        screenReads = max(0, screenReads - 1)
        lastScreenRead = Date()
        readAt[key] = Date()
    }

    /// Whether an answer was read (ahead or by its screen) in the last `seconds`.
    func fresh(_ key: String, within seconds: TimeInterval) -> Bool {
        guard let at = readAt[key] else { return false }
        return Date().timeIntervalSince(at) < seconds
    }

    /// Something changed: every answer is read again, in its turn.
    func forget() { readAt = [:] }

    private func run() async {
        while !Task.isCancelled {
            guard let engine else { return }
            // (a screen reading, or only just done: the student's reads come first)
            guard engine.mayReadAhead, screenReads == 0, Date().timeIntervalSince(lastScreenRead) >= 3 else {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                continue
            }
            guard let r = next(engine) else {
                try? await Task.sleep(nanoseconds: 15_000_000_000) // (all of it fresh: looked at again in a while)
                continue
            }
            let key = SavedAnswers.key(r.name, r.args)
            readAt[key] = Date() // (marked first: a read that fails is not tried again at once)
            var asked = r.args
            asked["maxAge"] = r.every
            _ = try? await engine.call(r.name, asked, as: AnyAnswer.self, keep: true, keepAs: r.args)
            try? await Task.sleep(nanoseconds: 700_000_000)
        }
    }

    /// The first read that is missing or old enough to read again.
    private func next(_ engine: Engine) -> Read? {
        let now = Date()
        return reads(engine).first { r in
            guard let at = readAt[SavedAnswers.key(r.name, r.args)] else { return true }
            return now.timeIntervalSince(at) > r.every
        }
    }

    /// What is read ahead, the screens most often opened first — under the arguments each screen asks with, so the
    /// answer kept is the one it shows.
    private func reads(_ engine: Engine) -> [Read] {
        var list: [Read] = [
            Read(name: "today", args: [:], every: 300),
            Read(name: "todo", args: [:], every: 300),
            Read(name: "courses", args: [:], every: 600),
            Read(name: "grades", args: [:], every: 600),
            Read(name: "calendar", args: CalendarView.readAheadArgs(), every: 600),
            Read(name: "notifications", args: [:], every: 300),
        ]
        if !engine.onBrightspace { list.append(Read(name: "inbox", args: ["scope": "inbox"], every: 600)) }
        list.append(Read(name: "coursesProgress", args: [:], every: 900))
        // each course chosen: its home and its grades (the courses as last read)
        if let courses = engine.saved("courses", as: CoursesData.self) {
            for c in courses.rows.prefix(12) {
                list.append(Read(name: "home", args: ["ctx": "courses/\(c.id)"], every: 900))
                list.append(Read(name: "courseGrades", args: ["id": c.id], every: 900))
            }
        }
        return list
    }
}

/// An answer read only to be kept: whatever it holds.
private struct AnyAnswer: Decodable {
    init(from decoder: Decoder) throws {}
}

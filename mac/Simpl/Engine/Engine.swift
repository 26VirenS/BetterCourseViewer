import AppKit
import Combine
import QuartzCore
import SwiftUI
import WebKit

extension Notification.Name {
    /// (1.3.69) A window more of Simpl, on the place named in `object` (`Engine.place(named:)`).
    static let simplOpenWindow = Notification.Name("Simpl.openWindow")
}

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

/// (1.3.80) The head of a live check's answer (`Engine.liveScript`): the same answer as the one on screen or a new one,
/// its fingerprint, or the page's error.
private struct LiveHead: Decodable {
    var unchanged: Bool?
    var hash: String?
    var error: String?
}

/// What's New as a sheet's item.
struct WhatsNewSheetItem: Identifiable {
    let id = UUID()
    let data: WhatsNewData
}

/// The Canvas web view as the Mac app's engine, as in the iPhone app: one web view with the extension in it, signed in
/// to the school's Canvas, sitting underneath the window's own screens and answering what they show (`call`). It is
/// what the window shows only while signing in (the school's own page, under the app's sign-in form) — once the page
/// says it is ready (`shell.state`), the sidebar, the toolbar and the native screens go over it. Where the window is
/// (a place in the sidebar, what is pushed on it, Back and Forward) lives here too, so a press anywhere — a row, a
/// link in a post, a notification — can open its screen.
@MainActor
final class Engine: ObservableObject, ShellListener {
    enum Phase { case starting, web, native }

    let host: String
    let web: WebController
    /// (1.3.69) Another window's engine: the main window's, whose page answers this one's screens — this one keeps only
    /// where its own window is (its place, Back and Forward, its search, its sheets). Nil: the main window's own.
    private(set) var primary: Engine?
    private var mirrors: Set<AnyCancellable> = []
    /// The window's bottom layer: where the web view lives, under everything.
    let hostView = EngineView()

    @Published private(set) var phase: Phase = .starting
    /// (1.3.78) Where the window is and how it last moved, observed on their own (Whereabouts, Engine/Navigator.swift).
    let whereabouts = Whereabouts()
    var nav: Navigator {
        get { whereabouts.nav }
        set { whereabouts.nav = newValue }
    }
    @Published private(set) var snapshot: Snapshot?
    /// The school's site as the page says it: "canvas" or "d2l" (Brightspace); nil until it has (Router.swift onBrightspace).
    @Published private(set) var lms: String?
    /// Bumps when what the screens show may have changed (a tick, a hand-in, a refresh): they read again.
    @Published private(set) var dataVersion = 0
    /// The counts (the sidebar's badges, a course's unread) are Canvas's word now, not what was kept from before (1.2):
    /// until they are, they are not shown.
    @Published private(set) var countsLive = false
    /// The sidebar's courses (their unread counts) are Canvas's word now.
    @Published private(set) var coursesLive = false
    /// The sidebar's courses (those chosen in the setup) and groups.
    @Published private(set) var courses: [CourseRow] = []
    @Published private(set) var groups: [GroupRow] = []
    /// A course's sections as its home says (its own tabs), once looked at: the sidebar lists them under it.
    @Published private(set) var sections: [String: [SectionLink]] = [:]
    /// What the school added to Canvas's own navigation — its tools, History, Help — for the sidebar's last section
    /// (Shell/MoreFromCanvas.swift); nil or no items: no section.
    @Published private(set) var more: MoreNav?
    /// The toolbar's search field.
    @Published var query = ""
    /// A quiz in the app's own quiz screen (a sheet over the window).
    @Published var quiz: QuizLaunch?
    /// (1.3.17) A Knewton Alta assignment taken in the app's own popup (AltaQuiz.swift). (1.3.101) Set, its session is
    /// made at once and its launch asked of Canvas, while the popup is still being put up.
    @Published var alta: AltaLaunch? {
        didSet { if alta?.id != oldValue?.id { altaAsked() } }
    }
    /// (1.3.101) The Alta popup's session, made the moment `alta` is set, until the popup takes it (`altaSession(for:)`).
    private var pendingAlta: AltaSession?

    private func altaAsked() {
        guard let a = alta else { pendingAlta = nil; return }
        if pendingAlta?.launch.id == a.id { return } // (asked ahead, `askAlta`)
        askAlta(a)
    }

    /// (1.3.101) An Alta assignment's session made and its launch asked of Canvas now, ahead of its popup — the Tools
    /// list's, which puts the popup up only once the list has closed — taken by `alta` set to the same launch.
    func askAlta(_ a: AltaLaunch) {
        let s = AltaSession(launch: a)
        pendingAlta = s
        s.start(engine: self)
    }

    /// (1.3.101) The session for the Alta popup's launch: the one made as it was asked for, else a new one.
    func altaSession(for launch: AltaLaunch) -> AltaSession {
        if let s = pendingAlta, s.launch.id == launch.id {
            pendingAlta = nil
            return s
        }
        return AltaSession(launch: launch)
    }
    /// (1.3.66) A Macmillan Learning assignment open in its popup.
    @Published var macmillan: AltaLaunch?
    /// An external tool or a page of Canvas's own to open in a window of its own (the window opens it, then clears this).
    @Published var tool: ToolLaunch?
    /// The guided setup (a sheet): the first run, and Settings ▸ General ▸ Courses shown and goals.
    @Published var setup = false {
        didSet { if !setup && oldValue { schedulePopups() } }
    }
    @Published var whatsNew: WhatsNewSheetItem? {
        didSet { if whatsNew == nil && oldValue != nil { schedulePopups() } }
    }
    /// A new task (File → New Task, the + on To Do) and a new message (File → New Message): (1.3.79) their popups —
    /// asked for again while up, brought to the front.
    @Published var newTask = false {
        didSet { if newTask && oldValue { popups.raise("newtask") } }
    }
    @Published var newMessage = false {
        didSet { if newMessage && oldValue { popups.raise("compose") } }
    }
    /// (1.3.79) This window's popups over it, none of which blocks it (Shell/FloatingPopup.swift).
    private(set) lazy var popups = FloatingPopups(engine: self)
    /// What a press on a piece of work asked for, for the screen it opens to do on arriving: Hand In, or the feedback.
    struct Arrival: Equatable {
        let id: String
        let feedback: Bool
    }
    var arrival: Arrival?
    /// The Settings window is up: nothing else is put up over the main window meanwhile.
    var settingsOpen = false {
        didSet { if !settingsOpen && oldValue { schedulePopups() } }
    }

    private var ready = false
    private var shownNative = false
    /// The window went straight to the app's screens on what was kept (1.2), before the page has said it is signed in:
    /// if it turns out not to be, the sign-in is shown after all.
    private var openedOnKept = false
    private var launchPlaceTaken = false
    /// What the screens were last told, for this school (AnswerCache).
    let answers: AnswerCache
    /// (1.3.78) This session's answers, decoded, by their read (a window more uses the main window's).
    let memo = AnswerMemo()
    /// (1.3.78) What is read ahead of the student (ReadAhead.swift).
    let ahead = ReadAhead()
    /// (1.3.80) This window's live checks: the screen on show asked again, now and then, for what is new on it
    /// (Engine/LiveRefresh.swift).
    private(set) lazy var live = LiveRefresh(engine: self)
    /// (1.3.80) What the live checks know of what the whole window shows beside its screen: the sidebar's counts.
    let liveMarks = LiveMarks()
    private var queuedPopups: [Popup] = []
    private var popupCheck: DispatchWorkItem?
    private var loginWatch: AnyCancellable?

    init(host: String) {
        self.host = host
        let page = WebController(mode: .canvas(host: host))
        web = page
        answers = AnswerCache(key: page.baseURL.port.map { "\(page.baseURL.host ?? host)_\($0)" } ?? (page.baseURL.host ?? host))
        Bridge.shared.shell = self
        hostView.take(web.webView)
        web.onFinish = { [weak self] url in self?.pageFinished(url) }
        web.onNavigationStart = { [weak self] in self?.ready = false }
        web.onOpenInShell = { [weak self] url in self?.openWeb(url.absoluteString, title: "") }
        web.onOpenInApp = { [weak self] url in self?.openBrowser(url) } // (1.3.13: another site in Simpl's own window)
        // the sign-in moving on (its cover lifting, "Stay logged in?" answered): a popup waiting may go up now
        loginWatch = web.login.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async { self?.schedulePopups() }
        }
    }

    /// (1.3.69) A window more (File ▸ New Window, a row's Open in New Window): its own place, Back and Forward, search and
    /// sheets; the same school, page and answers as the main window's — what it knows, this one shows as it changes.
    init(sharing main: Engine, at place: Place? = nil) {
        host = main.host
        web = main.web
        answers = main.answers
        follow(main)
        if let place { nav.replace(place) }
    }

    /// The main window's engine followed (again, when the main window was closed and opened afresh: a new engine).
    func follow(_ engine: Engine) {
        let main = engine.primary ?? engine
        guard main !== self, main !== primary else { return }
        primary = main
        mirrors = []
        main.$phase.sink { [weak self] v in
            self?.phase = v
            if v != .native { self?.popups.closeAll() } // (1.3.79: the app's screens gone, its popups go with them)
        }.store(in: &mirrors)
        main.$snapshot.sink { [weak self] v in self?.snapshot = v }.store(in: &mirrors)
        main.$lms.sink { [weak self] v in self?.lms = v }.store(in: &mirrors)
        main.$dataVersion.sink { [weak self] v in self?.dataVersion = v }.store(in: &mirrors)
        main.$countsLive.sink { [weak self] v in self?.countsLive = v }.store(in: &mirrors)
        main.$coursesLive.sink { [weak self] v in self?.coursesLive = v }.store(in: &mirrors)
        main.$courses.sink { [weak self] v in self?.courses = v }.store(in: &mirrors)
        main.$groups.sink { [weak self] v in self?.groups = v }.store(in: &mirrors)
        main.$sections.sink { [weak self] v in self?.sections = v }.store(in: &mirrors)
        main.$more.sink { [weak self] v in self?.more = v }.store(in: &mirrors)
        main.$refreshing.sink { [weak self] v in self?.refreshing = v }.store(in: &mirrors)
    }

    func start() {
        guard primary == nil else { return } // (a window more: the main window's page is already there)
        OpenClock.navigated() // (1.3.78: the first screen is timed from here)
        web.loadIfNeeded()
        openOnKept()
        // a page that never says (a school's sign-in on another host, a page that failed): shown as it is
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 10_000_000_000)
            if self?.phase == .starting { self?.setPhase(.web) }
        }
    }

    /// (1.2) Signed in here before, the setup done: the app's own screens at once, on what they showed last time (the
    /// sidebar, who is signed in, each screen's last answer), while the page loads underneath; the live answers replace
    /// them as they come. If the page turns out to want a sign-in, the sign-in is shown then.
    private func openOnKept() {
        guard phase == .starting, !UserDefaults.standard.bool(forKey: "SimplNoKept"),
              let s = kept("snapshot", as: Snapshot.self), s.setupDone == true, s.me != nil else { return }
        snapshot = s
        if let k = s.lms { lms = k }
        if let c = kept("courses", as: CoursesData.self) { courses = c.rows }
        if let g = kept("groups", as: GroupsData.self) { groups = g.current }
        more = kept("moreFromCanvas", as: MoreNav.self)
        openedOnKept = true
        takeLaunchPlace()
        setPhase(.native)
    }

    /// What a read answered last time, if it was kept (AnswerCache).
    func kept<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type) -> T? {
        guard let d = answers.data(name, args) else { return nil }
        return try? JSONDecoder().decode(T.self, from: d)
    }

    /// (1.3.78) `kept`, read, opened and decoded off the main thread (a screen's first open after a launch).
    func keptAnswer<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type) async -> T? {
        guard let d = await answers.dataOffMain(name, args) else { return nil }
        return try? await Engine.decoded(d, as: T.self)
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
                openedOnKept = false
                setPhase(.native)
                if !shownNative {
                    shownNative = true
                    Task { await self.firstNative() }
                }
            } else {
                ready = false
                openedOnKept = false
                setPhase(.web)
            }
        case "shell.open":
            if let url = body["url"] as? String { openWeb(url, title: body["title"] as? String ?? "") }
        case "shell.tab":
            switchTab(body["tab"] as? String ?? "today")
        default:
            break
        }
    }

    /// (1.2.14) The page under the app's own screens is never seen while they are up: it keeps its code and its data but
    /// lays out, paints and animates nothing (content-visibility skips its whole body), so it costs next to no CPU, GPU
    /// or memory for pictures; shown again (a sign-in, a page to finish by hand), it draws as before.
    private func idlePage(_ idle: Bool) {
        let js = """
        (function (on) {
          var d = document.documentElement; if (!d) return;
          var id = 'simpl-engine-idle', s = document.getElementById(id);
          if (on && !s) {
            s = document.createElement('style'); s.id = id;
            s.textContent = 'html.simpl-engine-idle body{content-visibility:hidden!important}html.simpl-engine-idle *,html.simpl-engine-idle *::before,html.simpl-engine-idle *::after{animation-play-state:paused!important;transition:none!important}';
            (document.head || d).appendChild(s);
          }
          d.classList.toggle('simpl-engine-idle', !!on);
        })(\(idle ? "true" : "false"));
        """
        web.webView.evaluateJavaScript(js, completionHandler: nil)
    }

    private func setPhase(_ p: Phase) {
        idlePage(p == .native)
        guard phase != p else { return }
        // (1.3.78: in the screenshot suite and developer mode, when the window goes from the school's page to the app's
        // screens and back, and why — a picture taken as it changes is explained by it)
        if OpenClock.verbose { NSLog("[Simpl] window: %@ (%@)", p == .native ? "the app's screens" : p == .web ? "the school's page" : "starting", web.webView.url?.path ?? "no page") }
        withAnimation(.easeInOut(duration: 0.35)) { phase = p }
        if p != .native { popups.closeAll() } // (1.3.79: the app's screens gone — the sign-in shown — its popups go too)
        if p == .native {
            // (the page underneath lets go of the keyboard: what is typed goes to the app's own screens, never to it)
            DispatchQueue.main.async { [weak self] in
                guard let self, let window = self.hostView.window, let responder = window.firstResponder as? NSView,
                      responder === self.web.webView || responder.isDescendant(of: self.web.webView) else { return }
                window.makeFirstResponder(nil)
            }
        }
    }

    private func pageFinished(_ url: URL?) {
        // the school's sign-in, on its own host (nothing of ours runs there to say so), or the school site's own login:
        // the page is what the window shows — at the start, after signing out, or a session that ran out while the
        // app's screens were up (1.2.8: they no longer stay over a sign-in, hiding its code prompts and next steps)
        guard let u = url, let h = u.host?.lowercased() else { return }
        if phase == .native { idlePage(true) } // (a page loaded afresh under the app's screens: idle again)
        if h != host.lowercased() || LoginAssist.isSignIn(u.path), phase != .web {
            openedOnKept = false
            ready = false
            setPhase(.web)
        }
    }

    /// (1.2.8) Signed out: the window shows the school's sign-in page at once — not the app's screens over it — and the
    /// next account starts afresh (nothing of this one's kept, shown or remembered, its first steps run again).
    func signedOut() {
        if let primary { nav.replace(.dashboard); primary.signedOut(); return }
        answers.clear()
        memo.clear()
        liveMarks.forget()
        ready = false
        openedOnKept = false
        shownNative = false
        snapshot = nil
        courses = []
        groups = []
        more = nil
        countsLive = false
        coursesLive = false
        nav.replace(.dashboard)
        setPhase(.web)
        web.login.forget()
        web.load()
    }

    /// Where the window opens, as the screenshot suite asks (-SimplPlace todo, course:101, section:courses/101:assignments).
    private func takeLaunchPlace() {
        guard !launchPlaceTaken else { return }
        launchPlaceTaken = true
        if let p = UserDefaults.standard.string(forKey: "SimplPlace"), let place = Engine.place(named: p) {
            nav.replace(place) // (where the window opens: nothing behind it to go Back to)
            if let ctx = place.ctx {
                if let h = kept("home", ["ctx": ctx], as: HomeData.self) { sections[ctx] = h.sections }
                Task { await loadSections(ctx) }
            }
            if case .search(let q) = place { query = q } // (the toolbar's field says what was searched)
        }
    }

    private func firstNative() async {
        // (the screenshot suite: -SimplPush /courses/101/assignments/1001)
        takeLaunchPlace()
        if let push = UserDefaults.standard.string(forKey: "SimplPush"), !push.isEmpty { openWeb(push, title: "") }
        Walk.startIfAsked(self) // (1.3.78: the screenshot suite's walk, -SimplWalk)
        var moreTool = false
        if let q = LaunchOpen.take("quiz:") {
            let parts = q.split(separator: ":").map(String.init)
            if parts.count >= 2 { quiz = QuizLaunch(course: parts[0], quiz: parts[1], title: "Quiz", begin: parts.count > 2 && (parts[2] == "take" || parts[2] == "review"), startAt: parts.count > 3 ? Int(parts[3]) : nil, feedback: parts.count > 2 && parts[2] == "feedback", review: parts.count > 2 && parts[2] == "review") }
        } else if let t = LaunchOpen.take("tool:") {
            let parts = t.split(separator: ":").map(String.init)
            if parts.count == 2 { tool = .courseTool(course: parts[0], id: parts[1], title: "Tool") }
        } else if let a = LaunchOpen.take("alta:") {
            let parts = a.split(separator: ":").map(String.init) // (1.3.17: -SimplOpen alta:104:4003, an Alta assignment's popup)
            // (1.3.83: alta:104:4003:/mock-alta/… — opened on that page of the mock's Alta, as the pop-up's pictures need)
            if parts.count >= 2 { alta = AltaLaunch(course: parts[0], assignment: parts[1], title: "Knewton Alta", page: parts.count > 2 ? parts[2...].joined(separator: ":") : nil) }
        } else if let a = LaunchOpen.take("macmillan:") {
            let parts = a.split(separator: ":").map(String.init) // (1.3.66: -SimplOpen macmillan:104:4005)
            if parts.count == 2 { macmillan = AltaLaunch(course: parts[0], assignment: parts[1], title: "Macmillan Learning") }
        } else if let w = LaunchOpen.take("window:") {
            // (1.3.69: -SimplOpen window:grades, a window more on that place)
            NotificationCenter.default.post(name: .simplOpenWindow, object: w)
        } else if LaunchOpen.take("developer") != nil {
            DevMode.shared.openAsked += 1 // (1.3.72: the screenshot suite's look at the Developer window; not unlocked for good)
        } else if LaunchOpen.take("settings") != nil {
            NotificationCenter.default.post(name: .simplOpenSettings, object: nil)
        } else if LaunchOpen.take("newtask") != nil {
            newTask = true
        } else if LaunchOpen.take("compose") != nil {
            newMessage = true
        } else if let m = LaunchOpen.take("more:") {
            // (-SimplOpen more:history | more:help — the sidebar's History or Help popup; more:tool — its first tool,
            // once the sidebar has read it)
            if let list = MoreList(rawValue: m) { openMore(list) } else { moreTool = m == "tool" }
        }
        await refreshSnapshot()
        await loadSidebar()
        if moreTool, let t = more?.items.first(where: { $0.kind == "tool" }) { openMore(t) }
        readAheadAfterLaunch() // (1.3.78: once the window is quiet, the places most opened next, read ahead)
        Task { await Reminders.shared.reschedule(self) } // (each launch: the reminders set again from what Canvas says now)
        Task { await Activity.shared.sync(self) }
        // (the screenshot suite: -SimplActivityProbe YES — one read of the school as the new-activity check makes it, on the console)
        if UserDefaults.standard.bool(forKey: "SimplActivityProbe") { Task { await Activity.shared.probe(self) } }
        // the first run: the app's own setup (the courses shown, the goals), before anything else — unless (onboarding-4)
        // it was put off with Set Up Later, when it waits for Settings ▸ General ▸ Courses shown and goals
        if (snapshot?.setupDone == false && !SetupLater.asked) || LaunchOpen.take("setup") != nil {
            if snapshot?.setupDone == false { MacWhatsNew.markSeen() } // (a first run: nothing is new to it)
            queuePopup(.setup)
            return
        }
        Task { await NotificationAsk.afterSetup(self, delay: 4) } // (1.2.1: a setup made before this one asks, once)
        if LaunchOpen.take("whatsnew") != nil {
            queuePopup(.whatsNew(MacWhatsNew.due() ?? MacWhatsNew.all))
            return
        }
        // after an update: what changed in this app since the version last seen (1.3.3: the Mac's own notes, not the
        // web extension's; seen once its sheet is really up)
        if let wn = MacWhatsNew.due() {
            queuePopup(.whatsNew(wn))
        }
    }

    /// (1.3.69) A place's name, as `place(named:)` reads it (a window more opens on it).
    static func name(of place: Place) -> String {
        switch place {
        case .dashboard: return "dashboard"
        case .courses: return "courses"
        case .todo: return "todo"
        case .calendar: return "calendar"
        case .grades: return "grades"
        case .notifications: return "notifications"
        case .inbox: return "inbox"
        case .tools: return "tools"
        case .externalTools: return "externaltools"
        case .groups: return "groups"
        case .search(let q): return "search:\(q)"
        case .home(let ctx):
            let parts = ctx.split(separator: "/").map(String.init)
            return parts.count == 2 ? "\(parts[0] == "groups" ? "group" : "course"):\(parts[1])" : "dashboard"
        case .section(let ctx, let kind): return "section:\(ctx):\(kind)"
        }
    }

    /// A place by the name the screenshot suite gives it.
    static func place(named raw: String) -> Place? {
        let parts = raw.split(separator: ":", maxSplits: 2).map(String.init)
        switch parts.first ?? "" {
        case "dashboard", "today": return .dashboard
        case "courses": return .courses
        case "todo": return .todo
        case "calendar": return .calendar
        case "grades": return .grades
        case "notifications": return .notifications
        case "inbox": return .inbox
        case "tools": return .tools // (1.2)
        case "externaltools", "external-tools": return .externalTools // (1.3.67)
        case "groups": return .groups
        case "search": return .search(parts.count > 1 ? parts[1] : "")
        case "course": return parts.count > 1 ? .home("courses/\(parts[1])") : nil
        case "group": return parts.count > 1 ? .home("groups/\(parts[1])") : nil
        case "section": return parts.count > 2 ? .section(parts[1], parts[2]) : nil
        default: return nil
        }
    }

    // MARK: - The app's own popups, one at a time

    enum Popup {
        case setup
        case whatsNew(WhatsNewData)
    }

    func queuePopup(_ p: Popup) {
        queuedPopups.append(p)
        schedulePopups()
    }

    /// Free for a popup: the app's own screens up, the page loaded, nothing of the sign-in on screen or still to come,
    /// and no other sheet over the window.
    private var popupFree: Bool {
        phase == .native && !web.webView.isLoading && web.login.settled && !setup && whatsNew == nil && quiz == nil && !settingsOpen && !newTask && !newMessage
    }

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
        if let primary { await primary.refreshSnapshot(); return }
        if let s = try? await call("snapshot", as: Snapshot.self) {
            snapshot = s
            liveMarks.shown("snapshot", by: self) // (1.3.80: the counts shown are this read's, the answer a live check asks after)
            if !countsLive { withAnimation(Motion.gentle) { countsLive = true } }
            if let k = s.lms, k != lms { lms = k; NotificationCenter.default.post(name: .simplLMSKnown, object: nil, userInfo: ["lms": k]) }
        }
    }

    /// The sidebar's courses and groups, read again.
    func loadSidebar() async {
        if let primary { await primary.loadSidebar(); return }
        if let c = try? await call("courses", as: CoursesData.self) {
            withAnimation(Motion.gentle) {
                courses = c.rows
                coursesLive = true
            }
        }
        if let g = try? await call("groups", as: GroupsData.self) {
            withAnimation(Motion.gentle) { groups = g.current }
        }
        // (what the school added to Canvas's own navigation, read from the page's menu: the sidebar's last section)
        if let m = try? await call("moreFromCanvas", as: MoreNav.self), m != more {
            withAnimation(Motion.gentle) { more = m }
        }
    }

    /// A course's sections, as its home lists them (looked up once, when its row in the sidebar is opened; what was kept
    /// from last time shown meanwhile).
    func loadSections(_ ctx: String) async {
        if let primary { await primary.loadSections(ctx); return }
        guard !sectionsRead.contains(ctx) else { return }
        if sections[ctx] == nil, let h = kept("home", ["ctx": ctx], as: HomeData.self) { sections[ctx] = h.sections }
        if let h = try? await call("home", ["ctx": ctx], as: HomeData.self) {
            sectionsRead.insert(ctx)
            withAnimation(Motion.gentle) { sections[ctx] = h.sections }
        }
    }
    private var sectionsRead: Set<String> = []

    /// Something changed under the screens (a tick, a hand-in): they read again, and so do the counts and reminders.
    func changed() {
        if let primary { primary.changed(); return } // (every window reads again)
        memo.changed() // (1.3.78: what is held is stale, unless the action that changed it already said so)
        dataVersion += 1
        Task { await refreshSnapshot() }
        Task { await Reminders.shared.reschedule(self) }
    }

    /// View → Reload and the toolbar's Refresh (1.3.2): Canvas asked afresh (the page's caches let go), then every
    /// screen reads again. `refreshing` while it goes (the button turns); a second press meanwhile is the same one.
    @Published private(set) var refreshing = false

    func refresh() {
        if let primary { primary.refresh(); return }
        guard !refreshing else { return }
        refreshing = true
        Task {
            _ = try? await call("refresh", as: OK.self)
            await loadSidebar()
            changed()
            try? await Task.sleep(nanoseconds: 400_000_000) // (the screens' own reads begin; the turn shows it was done)
            refreshing = false
        }
    }

    func reload() {
        web.webView.reload()
    }

    // MARK: - Calls

    /// A call into the page's engine (BCVNative.call), its answer decoded. Waits for the page to be ready (a page load
    /// under way), and tries once more if the page went away mid-call. (1.3.78) A read's answer is held for the session
    /// (`memo`), and any other call that succeeds makes what is held stale (it may have changed what the screens show).
    func call<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type) async throws -> T {
        try await fetch(name, args, as: type, quiet: false)
    }

    /// (1.3.78) A screen's read: this session's answer while it is fresh (nothing is asked), else the page's (`call`).
    func read<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type, quiet: Bool = false) async throws -> T {
        if let primary { return try await primary.read(name, args, as: type, quiet: quiet) }
        if !AnswerMemo.sets(name, args), let held = recent(name, args, as: type), held.fresh { return held.value }
        return try await fetch(name, args, as: type, quiet: quiet)
    }

    /// (1.3.78) A call, `quiet` when it is read ahead: the screenshot suite's picture does not wait for it, and a read
    /// ahead lets a screen's own read go first (`waiting`).
    func fetch<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type, quiet: Bool) async throws -> T {
        if let primary { return try await primary.fetch(name, args, as: type, quiet: quiet) } // (a window more: the main window's page answers)
        if !quiet {
            Shot.began() // (the screenshot suite's picture waits for the page's answers: Support/Shot.swift)
            waiting += 1
        }
        defer {
            if !quiet {
                Shot.ended()
                waiting -= 1
            }
        }
        let held = AnswerMemo.held.contains(name)
        let generation = memo.generation
        let started = CACurrentMediaTime()
        let data = try await answer(name, args, shared: held)
        let got: (value: T, fingerprint: String?)
        do {
            // (off the main thread: a long answer never holds up the window; a read's fingerprint with it, 1.3.80)
            got = try await Engine.decoded(data, as: T.self, fingerprinted: held)
        } catch let e as DecodingError {
            print("[Simpl] \(name) answer not understood:", e)
            throw EngineError.unreadable
        }
        let decoded = got.value
        if held {
            answers.keep(data, name, args) // (a read: kept for next time, AnswerCache)
            if let key = AnswerMemo.key(name, args) { memo.put(key, decoded, generation: generation, fingerprint: got.fingerprint) }
        } else if !AnswerMemo.neutral.contains(name) {
            memo.invalidate() // (something was done: what the screens hold is asked for again as each opens)
        }
        if OpenClock.verbose {
            NSLog("[Simpl] %@ %@: %d ms", quiet ? "read ahead" : "call", name, Int(((CACurrentMediaTime() - started) * 1000).rounded()))
        }
        return decoded
    }

    /// (1.3.78) The page's answer to a call, as JSON data (its error thrown) — a read asked again while it is on its way
    /// is not asked twice: whoever asks waits for the same answer.
    private func answer(_ name: String, _ args: [String: Any], shared: Bool) async throws -> Data {
        guard shared, let key = AnswerMemo.callKey(name, args) else { return try await ask(name, args) }
        if let running = inflight[key] { return try await running.value }
        let task = Task { try await self.ask(name, args) }
        inflight[key] = task
        defer { inflight[key] = nil }
        return try await task.value
    }

    private var inflight: [String: Task<Data, Error>] = [:]
    /// (1.3.78) Reads a screen is waiting for, now: reading ahead waits until there are none.
    private(set) var waiting = 0

    private func ask(_ name: String, _ args: [String: Any]) async throws -> Data {
        var lastError: Error = EngineError.notReady(lmsName)
        for attempt in 0..<3 {
            await whenReady()
            guard ready else { throw EngineError.notReady(lmsName) }
            do {
                let raw = try await web.webView.callAsyncJavaScript("return JSON.stringify(await BCVNative.call(name, args))", arguments: ["name": name, "args": args], in: nil, contentWorld: .defaultClient)
                guard let text = raw as? String else { throw EngineError.unreadable }
                return try await Engine.checked(text)
            } catch let e as EngineError {
                throw e
            } catch {
                lastError = error
                if attempt < 2 { try? await Task.sleep(nanoseconds: 400_000_000) }
            }
        }
        throw lastError
    }

    /// (1.3.78) The page's JSON text as data, or the error it says — off the main thread, as is the decoding.
    private nonisolated static func checked(_ text: String) async throws -> Data {
        guard let data = text.data(using: .utf8) else { throw EngineError.unreadable }
        // (only an answer that starts as an object can be the page's { error })
        if data.first == UInt8(ascii: "{"), let box = try? JSONDecoder().decode(ErrorBox.self, from: data), let message = box.error {
            throw EngineError.page(message)
        }
        return data
    }

    private nonisolated static func decoded<V: Decodable>(_ data: Data, as type: V.Type) async throws -> V {
        try JSONDecoder().decode(V.self, from: data)
    }

    /// (1.3.80) An answer decoded off the main thread, with — when it is a read to be held — its JSON's fingerprint, the
    /// page's own for the same JSON (LiveFingerprint): a screen that shows it asks its first live check with it.
    private nonisolated static func decoded<V: Decodable>(_ data: Data, as type: V.Type, fingerprinted: Bool) async throws -> (value: V, fingerprint: String?) {
        let value = try JSONDecoder().decode(V.self, from: data)
        return (value, fingerprinted ? LiveFingerprint.of(String(decoding: data, as: UTF8.self)) : nil)
    }

    /// (1.3.72) The Developer window's console: a call's answer as the page gives it, as JSON text.
    func callText(_ name: String, _ args: [String: Any] = [:]) async throws -> String {
        if let primary { return try await primary.callText(name, args) }
        await whenReady()
        guard ready else { throw EngineError.notReady(lmsName) }
        let raw = try await web.webView.callAsyncJavaScript("return JSON.stringify(await BCVNative.call(name, args))", arguments: ["name": name, "args": args], in: nil, contentWorld: .defaultClient)
        return raw as? String ?? "null"
    }

    // MARK: - Live checks (1.3.80)

    /// What a live check heard of one read (`liveAnswer`): the answer is the one on screen, or a new one — each with its
    /// fingerprint, for the next check to ask with.
    enum LiveAnswer<T> {
        case same(String)
        case new(T, String)
    }

    /// The page is ready for a call: a live check is held back while it is not (a page loading under the screens).
    var pageReady: Bool { (primary ?? self).ready }

    /// A live check's call: the page's answer as a line for its head — `{unchanged, hash}`, or `{error}` — and, when the
    /// answer is new, its JSON on the line after it, exactly as a screen's read would get it (so it is kept as one is).
    private static let liveScript = """
    const r = await BCVNative.call(name, args);
    if (r && r.unchanged === true) return JSON.stringify({ unchanged: true, hash: r.hash });
    if (r && r.unchanged === false) return JSON.stringify({ unchanged: false, hash: r.hash }) + '\\n' + JSON.stringify(r.value);
    return JSON.stringify({ error: r && r.error ? String(r.error) : 'No answer.' });
    """

    /// (1.3.80) One of a screen's reads asked again for a live check (LiveRefresh.swift): every Canvas read its answer is
    /// made from that is older than `maxAge` seconds is read again, and none that is younger (native-app.js's `maxAge`);
    /// the answer is compared with the one on screen by its fingerprint (`known`: nil or "" when the screen knows none)
    /// and comes back as only that when it is the same — nothing sent, decoded or drawn. A new one is decoded, held for
    /// the session and kept for next time, as a screen's read is. It never waits for the page (one not ready says so at
    /// once) and never counts as a screen waiting (the screenshot suite's settling, reading ahead). The page's error is
    /// thrown.
    func liveAnswer<T: Decodable>(_ name: String, _ args: [String: Any], as type: T.Type, maxAge: Double, known: String?) async throws -> LiveAnswer<T> {
        if let primary { return try await primary.liveAnswer(name, args, as: type, maxAge: maxAge, known: known) }
        guard ready else { throw EngineError.notReady(lmsName) }
        var asked = args
        asked["maxAge"] = maxAge
        asked["known"] = known ?? ""
        // (1.3.85) whether the student is at the window as it is asked (LiveHolds.seen): only then may the check mark an
        // open conversation's or discussion's news read on Canvas
        if LiveHolds.shared.seen { asked["seen"] = true } else { asked.removeValue(forKey: "seen") }
        let generation = memo.generation
        let raw = try await web.webView.callAsyncJavaScript(Engine.liveScript, arguments: ["name": name, "args": asked], in: nil, contentWorld: .defaultClient)
        guard let text = raw as? String else { throw EngineError.unreadable }
        let found = try await Engine.liveParts(text)
        guard let data = found.value else { return .same(found.fingerprint) }
        guard let decoded = try? await Engine.decoded(data, as: T.self) else { throw EngineError.unreadable }
        if AnswerMemo.held.contains(name) {
            answers.keep(data, name, args) // (kept for next time, and held for the session, as a screen's read is)
            if let key = AnswerMemo.key(name, args) { memo.put(key, decoded, generation: generation, fingerprint: found.fingerprint) }
        }
        return .new(decoded, found.fingerprint)
    }

    /// (1.3.80) A live check's answer read off the main thread: its fingerprint, and the new answer's JSON when there is
    /// one; the page's error thrown.
    private nonisolated static func liveParts(_ text: String) async throws -> (fingerprint: String, value: Data?) {
        let data = Data(text.utf8)
        let cut = data.firstIndex(of: UInt8(ascii: "\n")) ?? data.endIndex
        guard let head = try? JSONDecoder().decode(LiveHead.self, from: Data(data[data.startIndex..<cut])) else { throw EngineError.unreadable }
        if let message = head.error { throw EngineError.page(message) }
        guard let fingerprint = head.hash else { throw EngineError.unreadable }
        if head.unchanged == true { return (fingerprint, nil) }
        guard cut < data.endIndex else { throw EngineError.unreadable }
        return (fingerprint, Data(data[data.index(after: cut)...]))
    }

    /// (1.3.80) The fingerprint of this session's answer to a read, if it holds one: a screen showing that answer asks its
    /// first live check with it.
    func heldFingerprint(_ name: String, _ args: [String: Any] = [:]) -> String? {
        if let primary { return primary.heldFingerprint(name, args) }
        guard let key = AnswerMemo.key(name, args) else { return nil }
        return memo.fingerprint(key)
    }

    /// (1.3.80) How many reads the page has made of the school so far (BCVNative.stats), for a live check's line: nil when
    /// the page cannot say.
    func liveReads() async -> Int? {
        if let primary { return await primary.liveReads() }
        guard ready else { return nil }
        let raw = try? await web.webView.callAsyncJavaScript("return BCVNative.stats ? BCVNative.stats().reads : null", arguments: [:], in: nil, contentWorld: .defaultClient)
        return (raw as? NSNumber)?.intValue
    }

    /// (1.3.80) The sidebar's counts as a live check found them (only when they changed): the badges move to them.
    func showLiveSnapshot(_ s: Snapshot) {
        if let primary { primary.showLiveSnapshot(s); return }
        withAnimation(Motion.gentle) { snapshot = s }
    }

    /// An action whose answer is only whether it worked.
    @discardableResult
    func act(_ name: String, _ args: [String: Any] = [:]) async -> Bool {
        do {
            let r = try await call(name, args, as: OK.self)
            return r.ok ?? true
        } catch {
            return false
        }
    }

    private func whenReady(timeout: Double = 25) async {
        let start = Date()
        while !ready && Date().timeIntervalSince(start) < timeout {
            try? await Task.sleep(nanoseconds: 100_000_000)
        }
    }

    // MARK: - Where the window is

    /// A place picked: shown at its root. (1.3.78) A place in the same course deeper in it (its home to a section) is a
    /// move deeper, and back to its home a move back; any other a move across (`moving`). `kind` says otherwise: a link
    /// to a course is a move deeper.
    func go(_ place: Place, animated: Bool = true, kind: PageMove.Kind? = nil) {
        let here = nav.current.place
        let way: PageMove.Kind
        if let kind {
            way = kind
        } else if let ctx = place.ctx, ctx == here.ctx, place != here {
            way = place.kind == "home" ? .back : (here.kind == "home" ? .deeper : .across)
        } else {
            way = .across
        }
        moving(way, animated: animated) { $0.go(place) }
        if case .home(let ctx) = place { Task { await loadSections(ctx) } }
        if case .section(let ctx, _) = place { Task { await loadSections(ctx) } }
    }

    /// A screen pushed on the place showing.
    func push(_ route: Route) {
        moving(.deeper) { $0.push(route) }
    }

    func goBack() {
        moving(.back) { $0.goBack() }
    }

    func goForward() {
        moving(.deeper) { $0.goForward() }
    }

    /// (1.3.102) A sidebar row pressed — a click on it, never the list saying its selection again as its rows change:
    /// the place it names, showing with a screen pushed on it (an assignment opened on the Dashboard, a conversation on
    /// the Inbox, a page on a course's home), goes back to its root as Back goes, one step in the history. Any other
    /// row is the list's selection's to go to (`Sidebar.selection`).
    func rowPressed(_ place: Place) {
        guard nav.current.place == place, !nav.current.path.isEmpty else { return }
        moving(.back) { $0.popToRoot() }
    }

    /// (1.3.78) How the window last moved (PageMove): each screen arrives by it, and the one leaving goes by it.
    var move: PageMove { whereabouts.move }

    /// (1.3.78) A move of the window's, timed (OpenClock) and told to the screens (`move`). A move to another place is
    /// animated on the page spring: the page leaving goes the way the move goes as it fades (PageExit) while the new one
    /// arrives (PageArrival). A move within a place swaps its screen at once — the stack keeps the one under it as it
    /// was — and the screen on top arrives by itself, so a push and a Back move as the places do. So does a move to
    /// another place with a screen already on it (an assignment opened from Grades, To Do or the Dashboard, or Back to
    /// one), as the push it is: the old page goes at once and the screen on top arrives by itself, its place's own page
    /// never shown under it (DetailStack).
    private func moving(_ kind: PageMove.Kind, animated: Bool = true, _ change: (inout Navigator) -> Void) {
        var next = nav
        change(&next)
        guard next != nav else { return } // (the place showing, picked again)
        OpenClock.navigated()
        let across = next.current.place.identity != nav.current.place.identity
        let fades = animated && across && next.current.path.isEmpty
        PageMotion.leaving(kind)
        var t = Transaction(animation: fades ? Motion.page : nil)
        t.disablesAnimations = !fades
        withTransaction(t) {
            whereabouts.move = PageMove(kind: animated ? kind : .none, serial: move.serial + 1)
            nav = next
        }
    }

    /// A native screen by its route: a course, a group, a section, the Inbox and the roots are places in the sidebar;
    /// an assignment, a discussion, a page, a folder, a conversation are pushed on the place showing.
    func open(_ route: Route) {
        switch route {
        case .course(let id): go(.home("courses/\(id)"), kind: .deeper)
        case .group(let id): go(.home("groups/\(id)"), kind: .deeper)
        case .groups: go(.groups)
        case .inbox: go(.inbox)
        case .notifications: go(.notifications)
        case .calendar: go(.calendar)
        case .section(let ctx, let kind): go(.section(ctx, kind), kind: .deeper)
        default:
            // (1.2.10) a course's screen opens in its course: the course chosen in the sidebar, the screen on it
            if let place = Engine.place(of: route), nav.current.place.ctx != place.ctx {
                moving(.deeper) { $0.push(route, in: place) }
                if let ctx = place.ctx { Task { await loadSections(ctx) } }
            } else {
                push(route)
            }
        }
    }

    /// The place in its course a screen belongs under: an assignment under the course's Assignments, a folder under its
    /// Files, a discussion, an announcement or a page under its home; nil for one of no course (a conversation).
    static func place(of route: Route) -> Place? {
        switch route {
        case .assignment(let course, _): return .section("courses/\(course)", "assignments")
        case .folder(let ctx, _, _): return .section(ctx, "files")
        case .topic(let ctx, _), .page(let ctx, _): return .home(ctx)
        default: return nil
        }
    }

    /// A Canvas address, wherever it leads: its native screen (Router.swift), a tool or a quiz over everything, a file in
    /// Quick Look, a root; else where Canvas sends it.
    func openWeb(_ url: String, title: String) {
        if let t = toolLaunch(for: url, title: title) {
            openTool(t)
            return
        }
        if let q = quizLaunch(for: url, title: title) {
            openQuiz(q)
            return
        }
        // (1.3.103) a Canvas file's link — one in an assignment's instructions above all — by its id, as a module's file
        // opens (FileViews.swift): it used to be fetched by the address made from the link alone, its key dropped
        if linkedFile(url) != nil {
            openLinkedFile(url, title: title)
            return
        }
        if let file = fileDownload(for: url) {
            let name = title.isEmpty ? file.lastPathComponent : title
            // (1.3.12) a Brightspace content file Brightspace will not hand over opens in its own viewer instead
            var instead: (() -> Void)?
            if let v = schoolViewer(forDownload: file) { instead = { [weak self] in self?.openWebScreen(v.absoluteString, title: name) } }
            FilePreview.shared.open(file, name: name, in: web.webView, for: .look, instead: instead)
            return
        }
        if let route = nativeRoute(for: url, title: title) {
            if case .assignment(_, let id) = route, let u = absolute(url), Engine.isSubmission(u) { arrival = Arrival(id: id, feedback: true) }
            open(route)
            return
        }
        if let t = tabName(for: url) {
            switchTab(t)
            return
        }
        resolveThenOpen(url, title: title)
    }

    private func opensNatively(_ url: String) -> Bool {
        toolLaunch(for: url, title: "") != nil || quizLaunch(for: url, title: "") != nil || fileDownload(for: url) != nil
            || nativeRoute(for: url, title: "") != nil || tabName(for: url) != nil
    }

    private struct Resolved: Decodable { var url: String }

    /// An address with no screen as it stands (a module item's, one Canvas redirects): opened where Canvas sends it.
    /// Only what has no screen anywhere shows Canvas's own page, in a window of its own.
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
        return (s == "http" || s == "https") && u.host?.lowercased() == web.baseURL.host?.lowercased()
    }

    /// Canvas's own page for an address ("Open in Canvas", a part of Canvas with no screen here), in a window of its own
    /// with the Canvas session.
    func openWebScreen(_ url: String, title: String) {
        guard let raw = absolute(url) else { return }
        Task {
            let u = await schoolPage(for: raw) // (on Brightspace: the Brightspace page for the interface's address, 2.99.22)
            tool = ToolLaunch(title: title.isEmpty ? lmsName : title, args: ["page": u.absoluteString])
        }
    }

    func openCanvasPage(_ url: String, title: String) { openWebScreen(url, title: title) }

    /// (1.3.13) A page of another site — a course's link, a link in an announcement — in a browser window of Simpl's
    /// own (the tool window: Back, Forward, Reload, Share, Open in Browser), never sent off to the Mac's browser.
    func openBrowser(_ u: URL, title: String = "") {
        tool = ToolLaunch(title: title.isEmpty ? (u.host ?? "Link") : title, args: ["page": u.absoluteString])
    }

    func openTool(_ t: ToolLaunch) {
        tool = t
    }

    func openQuiz(_ q: QuizLaunch) {
        quiz = q
    }

    /// A piece of work's own action (its context menu): hand it in — take the quiz, open the discussion — or see its
    /// feedback; an assignment's screen does it on arriving.
    func act(on raw: String?, title: String, feedback: Bool) {
        guard let raw, !raw.isEmpty else { return }
        if var q = quizLaunch(for: raw, title: title) {
            q.feedback = feedback
            quiz = q
            return
        }
        if let route = nativeRoute(for: raw, title: title), case .assignment(_, let id) = route {
            arrival = Arrival(id: id, feedback: feedback)
            push(route)
            return
        }
        openWeb(raw, title: title)
    }

    func openNotifications() { go(.notifications) }
    func openCalendar() { go(.calendar) }

    /// A root by the name the router and the page use.
    func switchTab(_ name: String) {
        switch name {
        case "notifications": go(.notifications)
        case "calendar": go(.calendar)
        case "courses": go(.courses)
        case "todo": go(.todo)
        case "grades": go(.grades)
        case "inbox": go(.inbox)
        default: go(.dashboard)
        }
    }

    /// The toolbar's search: its results in the window (Return in the field), or a result picked from its suggestions.
    func search(_ text: String) {
        let q = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !q.isEmpty else { return }
        if case .search = nav.current.place, nav.current.path.isEmpty {
            nav.replace(.search(q)) // (a search typed further: the same step, not another)
        } else {
            go(.search(q))
        }
    }

    func openFromSearch(_ url: String, title: String, external: Bool) {
        if external, let u = URL(string: url) { web.openExternally(u) } else { openWeb(url, title: title) }
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

    /// The Canvas address of a place, for Open in Canvas and Copy Link.
    func canvasURL(for place: Place) -> URL? {
        switch place {
        case .dashboard: return absolute("/")
        case .courses: return absolute("/courses")
        case .todo: return absolute("/")
        case .calendar: return absolute("/calendar")
        case .grades: return absolute("/grades")
        case .notifications: return absolute("/profile/communication")
        case .inbox: return absolute("/conversations")
        case .groups: return absolute("/groups")
        case .search: return nil
        case .tools: return nil // (1.2) the app's own: nothing on Canvas
        case .externalTools: return nil // (1.3.67) the app's own list
        case .home(let ctx): return absolute("/\(ctx)")
        case .section(let ctx, let kind):
            let path: String
            switch kind {
            case "discussions": path = "discussion_topics"
            case "people": path = "users"
            case "syllabus": path = "assignments/syllabus"
            default: path = kind
            }
            return absolute("/\(ctx)/\(path)")
        }
    }
}

/// The window's bottom layer: the web view, under everything (and, while signing in, what the window shows).
final class EngineView: NSView {
    func take(_ webView: WKWebView) {
        guard webView.superview !== self else { return }
        webView.removeFromSuperview()
        webView.frame = bounds
        webView.autoresizingMask = [.width, .height]
        addSubview(webView)
    }
}

struct EngineHost: NSViewRepresentable {
    let engine: Engine

    func makeNSView(context: Context) -> EngineView { engine.hostView }
    func updateNSView(_ nsView: EngineView, context: Context) {}
}

/// (1.3.78) The window's engine as a plain value of the environment, for views that only act through it — open, go,
/// call — and draw nothing it publishes: unlike a view holding it as an environment object, such a view is not drawn
/// again each time the engine changes (a search typed, a count read, a course's sections). The Dashboard's parts hold
/// it so: the Dashboard is kept under the stack while another screen shows, and drew itself again, unseen, at each.
struct EngineLink: Equatable {
    weak var engine: Engine?

    static func == (a: EngineLink, b: EngineLink) -> Bool { a.engine === b.engine }
}

private struct EngineLinkKey: EnvironmentKey {
    static let defaultValue = EngineLink()
}

extension EnvironmentValues {
    var engineLink: EngineLink {
        get { self[EngineLinkKey.self] }
        set { self[EngineLinkKey.self] = newValue }
    }
}

extension EngineLink {
    /// The engine linked (else the main window's: a view always has one to act through).
    @MainActor
    var acting: Engine {
        engine ?? AppModel.shared.engine!
    }
}

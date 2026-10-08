import AppKit
import Combine
import SwiftUI
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
    /// The window's bottom layer: where the web view lives, under everything.
    let hostView = EngineView()

    @Published private(set) var phase: Phase = .starting
    @Published var nav = Navigator()
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
    /// The toolbar's search field.
    @Published var query = ""
    /// A quiz in the app's own quiz screen (a sheet over the window).
    @Published var quiz: QuizLaunch?
    /// An external tool or a page of Canvas's own to open in a window of its own (the window opens it, then clears this).
    @Published var tool: ToolLaunch?
    /// The guided setup (a sheet): the first run, and Settings → Courses and Goals.
    @Published var setup = false {
        didSet { if !setup && oldValue { schedulePopups() } }
    }
    @Published var whatsNew: WhatsNewSheetItem? {
        didSet { if whatsNew == nil && oldValue != nil { schedulePopups() } }
    }
    /// A new task (File → New Task, the + on To Do) and a new message (File → New Message): their sheets.
    @Published var newTask = false
    @Published var newMessage = false
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
        // the sign-in moving on (its cover lifting, "Stay logged in?" answered): a popup waiting may go up now
        loginWatch = web.login.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async { self?.schedulePopups() }
        }
    }

    func start() {
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
        openedOnKept = true
        takeLaunchPlace()
        setPhase(.native)
    }

    /// What a read answered last time, if it was kept (AnswerCache).
    func kept<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type) -> T? {
        guard let d = answers.data(name, args) else { return nil }
        return try? JSONDecoder().decode(T.self, from: d)
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

    private func setPhase(_ p: Phase) {
        guard phase != p else { return }
        withAnimation(.easeInOut(duration: 0.35)) { phase = p }
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
        // the school's sign-in, on its own host: nothing of ours runs there to say so
        if phase == .starting || (openedOnKept && !ready), let h = url?.host?.lowercased(), h != host.lowercased() {
            openedOnKept = false
            setPhase(.web)
        }
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
        if let q = LaunchOpen.take("quiz:") {
            let parts = q.split(separator: ":").map(String.init)
            if parts.count >= 2 { quiz = QuizLaunch(course: parts[0], quiz: parts[1], title: "Quiz", begin: parts.count > 2 && (parts[2] == "take" || parts[2] == "review"), startAt: parts.count > 3 ? Int(parts[3]) : nil, review: parts.count > 2 && parts[2] == "review") }
        } else if let t = LaunchOpen.take("tool:") {
            let parts = t.split(separator: ":").map(String.init)
            if parts.count == 2 { tool = .courseTool(course: parts[0], id: parts[1], title: "Tool") }
        } else if LaunchOpen.take("settings") != nil {
            NotificationCenter.default.post(name: .simplOpenSettings, object: nil)
        } else if LaunchOpen.take("newtask") != nil {
            newTask = true
        } else if LaunchOpen.take("compose") != nil {
            newMessage = true
        }
        await refreshSnapshot()
        await loadSidebar()
        Task { await Reminders.shared.reschedule(self) } // (each launch: the reminders set again from what Canvas says now)
        Task { await Activity.shared.sync(self) }
        // (the screenshot suite: -SimplActivityProbe YES — one read of the school as the new-activity check makes it, on the console)
        if UserDefaults.standard.bool(forKey: "SimplActivityProbe") { Task { await Activity.shared.probe(self) } }
        // the first run: the app's own setup (the courses that count, the goals), before anything else
        if snapshot?.setupDone == false || LaunchOpen.take("setup") != nil {
            queuePopup(.setup)
            return
        }
        Task { await NotificationAsk.afterSetup(self, delay: 4) } // (1.2.1: a setup made before this one asks, once)
        if LaunchOpen.take("whatsnew") != nil, let wn = try? await call("whatsNew", as: WhatsNewData.self) {
            queuePopup(.whatsNew(wn))
            return
        }
        // after an update: what changed (seen once its sheet is really up)
        if let wn = try? await call("whatsNew", ["due": true, "peek": true], as: WhatsNewData.self), !wn.releases.isEmpty {
            queuePopup(.whatsNew(wn))
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
        if let s = try? await call("snapshot", as: Snapshot.self) {
            snapshot = s
            if !countsLive { withAnimation(Motion.gentle) { countsLive = true } }
            if let k = s.lms, k != lms { lms = k; NotificationCenter.default.post(name: .simplLMSKnown, object: nil, userInfo: ["lms": k]) }
        }
    }

    /// The sidebar's courses and groups, read again.
    func loadSidebar() async {
        if let c = try? await call("courses", as: CoursesData.self) {
            withAnimation(Motion.gentle) {
                courses = c.rows
                coursesLive = true
            }
        }
        if let g = try? await call("groups", as: GroupsData.self) {
            withAnimation(Motion.gentle) { groups = g.current }
        }
    }

    /// A course's sections, as its home lists them (looked up once, when its row in the sidebar is opened; what was kept
    /// from last time shown meanwhile).
    func loadSections(_ ctx: String) async {
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
        dataVersion += 1
        Task { await refreshSnapshot() }
        Task { await Reminders.shared.reschedule(self) }
    }

    /// View → Reload: Canvas asked afresh (the page's caches let go), then every screen reads again.
    func refresh() {
        Task {
            _ = try? await call("refresh", as: OK.self)
            await loadSidebar()
            changed()
        }
    }

    func reload() {
        web.webView.reload()
    }

    // MARK: - Calls

    /// A call into the page's engine (BCVNative.call), its answer decoded. Waits for the page to be ready (a page load
    /// under way), and tries once more if the page went away mid-call.
    func call<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type) async throws -> T {
        var lastError: Error = EngineError.notReady(lmsName)
        for attempt in 0..<3 {
            await whenReady()
            guard ready else { throw EngineError.notReady(lmsName) }
            do {
                let raw = try await web.webView.callAsyncJavaScript("return JSON.stringify(await BCVNative.call(name, args))", arguments: ["name": name, "args": args], in: nil, contentWorld: .defaultClient)
                guard let text = raw as? String, let data = text.data(using: .utf8) else { throw EngineError.unreadable }
                if let box = try? JSONDecoder().decode(ErrorBox.self, from: data), let message = box.error { throw EngineError.page(message) }
                let answer = try JSONDecoder().decode(T.self, from: data)
                answers.keep(data, name, args) // (a read: kept for next time, AnswerCache)
                return answer
            } catch let e as EngineError {
                throw e
            } catch let e as DecodingError {
                print("[Simpl] \(name) answer not understood:", e)
                throw EngineError.unreadable
            } catch {
                lastError = error
                if attempt < 2 { try? await Task.sleep(nanoseconds: 400_000_000) }
            }
        }
        throw lastError
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

    /// A place picked: shown at its root, the change cross-faded.
    func go(_ place: Place, animated: Bool = true) {
        if animated {
            withAnimation(.easeInOut(duration: 0.2)) { nav.go(place) }
        } else {
            nav.go(place)
        }
        if case .home(let ctx) = place { Task { await loadSections(ctx) } }
        if case .section(let ctx, _) = place { Task { await loadSections(ctx) } }
    }

    /// A screen pushed on the place showing (on the same short curve as Back and Forward).
    func push(_ route: Route) {
        withAnimation(.easeInOut(duration: 0.2)) { nav.push(route) }
    }

    func goBack() {
        withAnimation(.easeInOut(duration: 0.2)) { nav.goBack() }
    }

    func goForward() {
        withAnimation(.easeInOut(duration: 0.2)) { nav.goForward() }
    }

    /// A native screen by its route: a course, a group, a section, the Inbox and the roots are places in the sidebar;
    /// an assignment, a discussion, a page, a folder, a conversation are pushed on the place showing.
    func open(_ route: Route) {
        switch route {
        case .course(let id): go(.home("courses/\(id)"))
        case .group(let id): go(.home("groups/\(id)"))
        case .groups: go(.groups)
        case .inbox: go(.inbox)
        case .notifications: go(.notifications)
        case .calendar: go(.calendar)
        case .section(let ctx, let kind): go(.section(ctx, kind))
        default: push(route)
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
        if let file = fileDownload(for: url) {
            FilePreview.shared.open(file, name: title.isEmpty ? file.lastPathComponent : title, in: web.webView)
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

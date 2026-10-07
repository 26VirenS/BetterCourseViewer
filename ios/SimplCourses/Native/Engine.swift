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
    case notReady
    case unreadable
    case page(String)

    var errorDescription: String? {
        switch self {
        case .notReady: return "Canvas is still loading. Try again in a moment."
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
    @Published var whatsNew: WhatsNewSheetItem? {
        didSet { if whatsNew == nil && oldValue != nil { schedulePopups() } }
    }
    /// Bumps when a screen's data may have changed under it (a tick in a web screen, an account change): the native screens read again.
    @Published private(set) var dataVersion = 0
    /// The tab bar's glass as drawn (TabBarProbe): a bar a screen puts above it takes this width and height.
    @Published private(set) var barSize = CGSize(width: 0, height: 62)
    /// A screen with a bar of its own over the tab bar keeps the tab bar full size (no minimising as it scrolls).
    @Published var holdTabBar = false
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

    init(host: String) {
        self.host = host
        web = WebController(mode: .canvas(host: host))
        Bridge.shared.shell = self
        hostView.take(web.webView, revealed: true)
        web.onNavigationStart = { [weak self] in self?.navigationStarted() }
        web.onFinish = { [weak self] url in self?.pageFinished(url) }
        web.onOpenInShell = { [weak self] url in self?.openWeb(url.absoluteString, title: "") }
        // the sign-in moving on (its cover lifting, "Stay logged in?" answered): a popup waiting may go up now
        loginWatch = web.login.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async { self?.schedulePopups() }
        }
        // the simulator suite's screens (.github/workflows/ios-shots.yml): -SimplTab courses
        if let t = UserDefaults.standard.string(forKey: "SimplTab") {
            if t == "calendar" { tab = .todo; paths[.todo] = [.calendar] } else if let first = AppTab(rawValue: t) { tab = first }
        }
    }

    func start() {
        web.loadIfNeeded()
        // a page that never says (a school's sign-in on another host, a page that failed): shown as it is
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 10_000_000_000)
            if self?.phase == .starting { self?.setPhase(.web) }
        }
    }

    // MARK: - What the page says

    func shellMessage(_ op: String, _ body: [String: Any]) {
        switch op {
        case "shell.state":
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
            } else {
                ready = false
                if let id = active { detach(id) }
                setPhase(.web)
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
        ready = false
        if asked != nil { asked?.navigated = true }
    }

    private func pageFinished(_ url: URL?) {
        // the school's sign-in, on its own host: nothing of ours runs there to say so
        if phase == .starting, let h = url?.host?.lowercased(), h != host.lowercased() { setPhase(.web) }
    }

    private func firstNative() async {
        // (the simulator suite: -SimplPush /courses/101, or notifications)
        if let push = UserDefaults.standard.string(forKey: "SimplPush"), !push.isEmpty {
            if push == "notifications" { openNotifications() } else { openWeb(push, title: "") }
        }
        // (-SimplOpen quiz:101:9011, quiz:101:9011:take:5 straight into the attempt at question 6, tool:101:9 a course's tool)
        if let q = LaunchOpen.take("quiz:") {
            let parts = q.split(separator: ":").map(String.init)
            if parts.count >= 2 { quiz = QuizLaunch(course: parts[0], quiz: parts[1], title: "Quiz", begin: parts.count > 2 && parts[2] == "take", startAt: parts.count > 3 ? Int(parts[3]) : nil) }
        } else if let t = LaunchOpen.take("tool:") {
            let parts = t.split(separator: ":").map(String.init)
            if parts.count == 2 { tool = .courseTool(course: parts[0], id: parts[1], title: "Tool") }
        }
        await refreshSnapshot()
        // the first run: the iPhone's own setup (the courses that count, the goals), before anything else
        if snapshot?.setupDone == false || LaunchOpen.take("setup") != nil {
            queuePopup(.setup)
            return
        }
        // after an update: what changed (seen once its sheet is really up — a sign-in still under way only delays it)
        if let wn = try? await call("whatsNew", ["due": true, "peek": true], as: WhatsNewData.self), !wn.releases.isEmpty {
            queuePopup(.whatsNew(wn))
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
        phase == .native && !web.webView.isLoading && web.login.settled && !setup && whatsNew == nil && quiz == nil && tool == nil && !settingsOpen
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
        if let s = try? await call("snapshot", as: Snapshot.self) { snapshot = s }
    }

    func setBarSize(_ s: CGSize) {
        guard s.width.isFinite, s.height.isFinite, s.width > 0, s.height > 0 else { return }
        guard abs(s.width - barSize.width) > 0.5 || abs(s.height - barSize.height) > 0.5 else { return }
        barSize = s
    }

    func changed() {
        dataVersion += 1
        Task { await refreshSnapshot() }
    }

    // MARK: - Calls

    /// A call into the page's engine (BCVNative.call), its answer decoded. Waits for the page to be ready
    /// (a page load under way), and tries once more if the page went away mid-call.
    func call<T: Decodable>(_ name: String, _ args: [String: Any] = [:], as type: T.Type) async throws -> T {
        var lastError: Error = EngineError.notReady
        for attempt in 0..<3 {
            await whenReady()
            guard ready else { throw EngineError.notReady }
            do {
                let raw = try await web.webView.callAsyncJavaScript("return JSON.stringify(await BCVNative.call(name, args))", arguments: ["name": name, "args": args], in: nil, contentWorld: .defaultClient)
                guard let text = raw as? String, let data = text.data(using: .utf8) else { throw EngineError.unreadable }
                if let box = try? JSONDecoder().decode(ErrorBox.self, from: data), let message = box.error { throw EngineError.page(message) }
                return try JSONDecoder().decode(T.self, from: data)
            } catch let e as EngineError {
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
    }

    // MARK: - Navigation

    /// A Canvas address on the stack showing: its native screen when the app has one (a course and
    /// everything in it, Groups, the Inbox — Router.swift), else the web interface's screen for it.
    func openWeb(_ url: String, title: String) {
        if let t = toolLaunch(for: url, title: title) {
            openTool(t)
            return
        }
        if let q = quizLaunch(for: url, title: title) {
            openQuiz(q)
            return
        }
        if let route = nativeRoute(for: url, title: title) {
            push(route)
            return
        }
        openWebScreen(url, title: title)
    }

    /// The web interface's own screen for an address, even where a native one exists ("Open in Canvas").
    func openWebScreen(_ url: String, title: String) {
        var path = paths[tab]
        if case .web(let top, _)? = path.last, same(top, url) { return }
        path.append(.web(url: url, title: title))
        paths[tab] = path
    }

    /// A native screen on the stack showing (not twice in a row).
    func push(_ route: Route) {
        var path = paths[tab]
        if path.last == route { return }
        path.append(route)
        paths[tab] = path
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

    func openNotifications() {
        var path = paths[tab]
        if path.last != .notifications { path.append(.notifications) }
        paths[tab] = path
    }

    /// The Calendar, pushed on the stack showing (it has no tab of its own).
    func openCalendar() {
        var path = paths[tab]
        if path.last != .calendar { path.append(.calendar) }
        paths[tab] = path
    }

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
        let t = AppTab(rawValue: name) ?? .today
        paths[t] = []
        tab = t
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
        focus = body["focus"] as? Bool ?? false
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

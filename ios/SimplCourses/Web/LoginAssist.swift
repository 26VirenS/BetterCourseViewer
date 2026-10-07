import Foundation
import Security
import WebKit

extension Notification.Name {
    /// Posted when the saved sign-in is kept or forgotten (Settings shows it).
    static let simplLoginChanged = Notification.Name("SimplCourses.loginChanged")
}

/// The school sign-in the student chose to keep ("Stay logged in?"): the username and password, for
/// the one sign-in page they were given on. In the iPhone's Keychain, this device only, readable only
/// while it is unlocked; never synced, never backed up, and only ever typed into that page's own form.
struct SavedLogin: Codable, Equatable {
    var host: String
    var user: String
    var pass: String
    var savedAt: Date
}

enum LoginVault {
    private static let service = "com.simplcourses.ios.login"
    private static let account = "school"

    private static func query() -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }

    static func load() -> SavedLogin? {
        var q = query()
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        guard SecItemCopyMatching(q as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return try? JSONDecoder().decode(SavedLogin.self, from: data)
    }

    @discardableResult
    static func save(_ login: SavedLogin) -> Bool {
        guard let data = try? JSONEncoder().encode(login) else { return false }
        SecItemDelete(query() as CFDictionary)
        var q = query()
        q[kSecValueData as String] = data
        q[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        let ok = SecItemAdd(q as CFDictionary, nil) == errSecSuccess
        NotificationCenter.default.post(name: .simplLoginChanged, object: nil)
        return ok
    }

    static func clear() {
        SecItemDelete(query() as CFDictionary)
        NotificationCenter.default.post(name: .simplLoginChanged, object: nil)
    }
}

/// The app's side of signing in. Web/login.js (in a world of its own on every page) says what a page
/// asks for; this decides what covers the web view and when details go into a page:
///
/// - a sign-in form the reader can fill, and nothing saved for it: the native form (LoginLayer) over
///   the page. What is typed there goes into the page's own fields and its own button is pressed.
/// - the saved sign-in page coming up: "Logging you in" from its first frame, the saved details filled
///   and sent; a two-step page gets the username, then the password on the next step.
/// - a page with nothing to fill after that (a two-factor prompt, a consent page): the cover lifts and
///   the page is shown, to be finished by hand; the sign-in carries on from there.
/// - the sign-in form again after a press (a wrong password): the native form, saying so. Saved
///   details that failed are not tried again until new ones are given.
/// - Canvas reached after details typed in the form: "Stay logged in?".
/// - a school page that says the sign-in went stale ("Stale Request", an expired request): started again from
///   Canvas by itself, once a minute at most; and on every school page shown, Start Over does the same by hand.
final class LoginAssist: NSObject, ObservableObject, WKScriptMessageHandler {
    enum Phase: Equatable {
        case idle
        /// The native form over the page.
        case capture(host: String, error: String?)
        /// "Logging you in".
        case working
    }

    @Published var phase: Phase = .idle
    @Published var askStay = false
    /// The school's page is shown in the middle of a sign-in, for the student to do a step of it (a two-factor
    /// code, a consent page, a page the reader could not fill, or one they asked to see): a note over it says so,
    /// and the sign-in carries on once Canvas is reached.
    @Published var handoff = false
    /// The web view is on the school's sign-in (another host, or Canvas's own /login): Start Over is offered there.
    @Published private(set) var onSchoolPage = false
    /// Starts the sign-in again from Canvas (WebController: a fresh load of Canvas, which sends a new request).
    var onRestart: (() -> Void)?
    private var lastAutoRestart: Date?
    @Published var prefillUser = ""

    static let world = WKContentWorld.world(name: "SimplLogin")
    static let handlerName = "bcvLogin"

    let canvasHost: String
    weak var webView: WKWebView?

    private struct Attempt {
        let host: String
        let step: String
        let at: Date
        let auto: Bool
        let user: String
        let pass: String
    }

    private var attempt: Attempt?
    private var navigatedSinceAttempt = false
    /// Details typed in the form, until "Stay logged in?" is answered (published: the app's own popups wait for it).
    @Published private var typed: (user: String, pass: String, host: String)?
    private var autoOff = false
    private var declined = false
    private var kindHere = "full"
    private var watchdog: DispatchWorkItem?

    init(canvasHost: String) {
        self.canvasHost = canvasHost.lowercased()
        super.init()
    }

    var isCapture: Bool {
        if case .capture = phase { return true }
        return false
    }

    /// Nothing of the sign-in on screen or still to come: no cover, no form, no "Stay logged in?" asked or about to
    /// be (details typed in the form are still on their way). The app's own popups wait for this.
    var settled: Bool { phase == .idle && !askStay && typed == nil }

    // MARK: - What the reader says

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.frameInfo.isMainFrame, let body = message.body as? [String: Any], let kind = body["kind"] as? String else { return }
        // the page's own address, as WebKit knows it — not what the script says it is
        let origin = message.frameInfo.securityOrigin
        let host = origin.host.lowercased()
        let secure = origin.protocol == "https"
        let error = (body["error"] as? String).flatMap { $0.isEmpty ? nil : $0 }
        if (body["stale"] as? Bool) == true, host != canvasHost {
            staleSeen()
            return
        }
        handle(kind: kind, host: host, error: error, secure: secure)
    }

    /// The school's page says the request went stale: start again by itself — but not twice within a minute, so a
    /// page that keeps saying it cannot send the app round in a loop; then it stays shown, with Start Over on it.
    private func staleSeen() {
        if let last = lastAutoRestart, Date().timeIntervalSince(last) < 60 {
            if phase == .working { reveal() }
            return
        }
        lastAutoRestart = Date()
        restart()
    }

    /// Start Over: whatever sign-in was under way forgotten, and Canvas loaded afresh (it sends the school a new
    /// request); saved details are tried again on the way.
    func restart() {
        watchdog?.cancel()
        attempt = nil
        navigatedSinceAttempt = false
        autoOff = false
        declined = false
        handoff = false
        if phase != .idle { phase = .idle }
        onRestart?()
    }

    private func handle(kind: String, host: String, error: String?, secure: Bool) {
        if kind == "none" {
            // a page with nothing to fill: two-factor, a consent page, a hop — shown, not covered
            if phase == .working { handOver() }
            return
        }
        guard secure else { return } // never on a page that is not https
        kindHere = kind
        // after a press: the next step, the page still busy with it, or the sign-in again (it did not work)
        if let a = attempt, a.host == host, Date().timeIntervalSince(a.at) < 30 {
            if a.step == "user" && kind == "pass" {
                fill(user: a.user, pass: a.pass, host: host, step: kind, auto: a.auto)
                return
            }
            if navigatedSinceAttempt || error != nil {
                failed(host: host, error: error, auto: a.auto)
            }
            return
        }
        let saved = LoginVault.load()
        if let s = saved, s.host == host, !autoOff {
            fill(user: s.user, pass: s.pass, host: host, step: kind, auto: true)
            return
        }
        if declined {
            if phase == .working { reveal() }
            return
        }
        if prefillUser.isEmpty { prefillUser = typed?.user ?? saved?.user ?? "" }
        watchdog?.cancel()
        handoff = false
        phase = .capture(host: host, error: nil)
    }

    // MARK: - Filling

    private func fill(user: String, pass: String, host: String, step: String, auto: Bool) {
        guard let webView, webView.url?.host?.lowercased() == host else { reveal(); return }
        phase = .working
        attempt = Attempt(host: host, step: step, at: Date(), auto: auto, user: user, pass: pass)
        navigatedSinceAttempt = false
        guard let arg = JS.literal(["user": user, "pass": pass]) else { reveal(); return }
        webView.evaluateJavaScript("SimplLogin.fill(\(arg))", in: nil, in: LoginAssist.world) { [weak self] result in
            switch result {
            case .success(let value):
                // nothing to press: the page is shown with the details in it, to be sent by hand
                if let r = value as? [String: Any], (r["pressed"] as? Bool) == false { self?.handOver() }
            case .failure:
                self?.handOver()
            }
        }
        arm(seconds: 25)
    }

    private func failed(host: String, error: String?, auto: Bool) {
        attempt = nil
        if auto { autoOff = true }
        let saved = LoginVault.load()
        prefillUser = typed?.user ?? saved?.user ?? prefillUser
        typed = nil
        watchdog?.cancel()
        handoff = false
        phase = .capture(host: host, error: error ?? "That username or password didn’t work. Try again.")
    }

    /// Lift whatever covers the page: the page itself is what the student needs now.
    func reveal() {
        watchdog?.cancel()
        if phase != .idle { phase = .idle }
    }

    /// The page shown in the middle of a sign-in, with the note that says what to do on it.
    private func handOver() {
        if phase == .working { handoff = true }
        reveal()
    }

    /// "Show the page", pressed under "Logging you in": the school's page, to do what it asks by hand.
    func showPage() {
        handoff = true
        reveal()
    }

    private func arm(seconds: Double) {
        watchdog?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            if self.webView?.isLoading == true { self.arm(seconds: 2); return } // (a page still on its way: a little longer)
            if self.phase == .working { self.handOver() }
        }
        watchdog = work
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: work)
    }

    // MARK: - The native form and the question after

    /// The details typed in the native form: into the page's own fields, its button pressed.
    func submit(user: String, pass: String) {
        guard case .capture(let host, _) = phase else { return }
        typed = (user, pass, host)
        autoOff = false
        fill(user: user, pass: pass, host: host, step: kindHere, auto: false)
    }

    /// "Use the website instead": the page as it is, and the form not offered again until the app is opened again.
    func useWebsite() {
        declined = true
        reveal()
    }

    /// The answer to "Stay logged in?".
    func stay(_ yes: Bool) {
        askStay = false
        if yes, let t = typed {
            LoginVault.save(SavedLogin(host: t.host, user: t.user, pass: t.pass, savedAt: Date()))
            autoOff = false
        }
        typed = nil
    }

    /// Signed out: nothing held in memory either.
    func forget() {
        typed = nil
        attempt = nil
        autoOff = false
        prefillUser = ""
        handoff = false
        reveal()
    }

    // MARK: - The web view's moves

    /// A page is about to load in the main frame.
    func willLoad(_ url: URL) {
        let host = url.host?.lowercased() ?? ""
        if attempt != nil { navigatedSinceAttempt = true }
        schoolPage(url)
        // the saved sign-in page coming up: covered from its first frame, not after it has shown
        if phase == .idle, !autoOff, host != canvasHost, let s = LoginVault.load(), s.host == host {
            phase = .working
            arm(seconds: 25)
        }
    }

    /// A page finished loading in the main frame.
    func didLoad(_ url: URL?) {
        guard let url, let host = url.host?.lowercased() else { return }
        schoolPage(url)
        if host == canvasHost && !url.path.hasPrefix("/login") {
            // on Canvas: signed in
            let viaForm = attempt != nil && attempt?.auto == false && typed != nil
            attempt = nil
            autoOff = false
            handoff = false
            reveal()
            // details typed in the form got the student in: they are offered to be kept (or kept anew, where
            // the saved ones had stopped working)
            if viaForm { askStay = true } else { typed = nil }
            return
        }
        // a page the reader cannot see (it never reports): shown anyway, after a moment
        if phase == .working { arm(seconds: 6) }
    }

    /// Where the web view is: on the school's sign-in, Start Over is offered, and the edge swipe that goes Back is
    /// off — a sign-in page gone back to is one the school refuses as stale.
    private func schoolPage(_ url: URL) {
        let host = url.host?.lowercased() ?? ""
        let on = !host.isEmpty && !canvasHost.isEmpty && (host != canvasHost || url.path.hasPrefix("/login"))
        if on != onSchoolPage { onSchoolPage = on }
        webView?.allowsBackForwardNavigationGestures = !on
    }

    /// The page could not be loaded at all.
    func pageFailed() {
        attempt = nil
        handOver()
    }
}

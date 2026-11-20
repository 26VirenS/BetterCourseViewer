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
/// On a Mac, an item of its own in the login Keychain. (Mac) An early 1.3.76 build kept it inside Simpl instead
/// (Application Support ▸ Simpl ▸ login.bin, sealed by its vault, mac/Simpl/Support/Vault.swift): found there, it is
/// put back into the Keychain once and the file deleted.
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
        #if os(macOS)
        _ = movedBack
        #endif
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
        #if os(macOS)
        _ = movedBack
        #endif
        SecItemDelete(query() as CFDictionary)
        var q = query()
        q[kSecValueData as String] = data
        #if os(iOS)
        q[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        #endif
        let ok = SecItemAdd(q as CFDictionary, nil) == errSecSuccess
        NotificationCenter.default.post(name: .simplLoginChanged, object: nil)
        return ok
    }

    static func clear() {
        #if os(macOS)
        try? FileManager.default.removeItem(at: oldFile) // (one not yet put back: forgotten too, never put back)
        #endif
        SecItemDelete(query() as CFDictionary)
        NotificationCenter.default.post(name: .simplLoginChanged, object: nil)
    }

    #if os(macOS)
    /// (Mac) Where an early 1.3.76 build kept the sign-in instead of the Keychain: a file of Simpl's own, sealed by
    /// its vault.
    private static var oldFile: URL { Vault.folder.appendingPathComponent("login.bin") }

    /// (Mac) That file's sign-in put back into the Keychain, once a launch at most, and the file deleted
    /// (Vault.moveBack). One kept in the Keychain already is the one kept.
    private static let movedBack: Void = {
        Vault.moveBack(from: LoginVault.oldFile, purpose: "login") { data in
            var q = LoginVault.query()
            q[kSecValueData as String] = data
            let status = SecItemAdd(q as CFDictionary, nil)
            return status == errSecSuccess || status == errSecDuplicateItem
        }
    }()
    #endif
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
    /// (iPhone 1.8.1, build 42) How far "Logging you in" has got: at the school's page, the details sent, Canvas on its way.
    /// (Build 64) `retrying`: something went wrong, and it is about to be begun again by itself (retry).
    enum Stage: Equatable { case school, sending, opening, retrying }
    @Published private(set) var stage: Stage = .school
    @Published var askStay = false
    /// The school's page is shown in the middle of a sign-in, for the student to do a step of it (a two-factor
    /// code, a consent page, a page the reader could not fill, or one they asked to see): a note over it says so,
    /// and the sign-in carries on once Canvas is reached.
    @Published var handoff = false
    /// The web view is on the school's sign-in (another host, or Canvas's own /login): Start Over is offered there.
    @Published private(set) var onSchoolPage = false
    /// (iPhone 1.9) Why the last page could not be loaded at all (offline, the school not answering), until the next one
    /// is asked for: the app signing in behind its screens says Login Failed with it.
    @Published private(set) var failure: String?
    /// Starts the sign-in again from Canvas (WebController: a fresh load of Canvas, which sends a new request).
    var onRestart: (() -> Void)?
    /// (Build 64) How many times the sign-in has been begun again by itself since it last got through, was begun again by
    /// hand, or five quiet minutes passed (retry); when the last was; the one waiting to go; and what went wrong last.
    @Published private(set) var retries = 0
    private var lastRetry: Date?
    private var retryWork: DispatchWorkItem?
    /// (Build 66) A try again by itself waiting to go: the app says it is still signing in meanwhile, not that the page is
    /// the student's to finish.
    @Published private(set) var retryPending = false
    static let maxRetries = 3
    /// (Build 64) The last page's answer, as it came (WebController): a sign-in page that answered with an error (400 and
    /// up) is one to begin again from.
    private var lastResponse: (host: String, path: String, code: Int)?
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
    /// (Build 42) How many times the watchdog has waited on a page still loading (a few at most: a page with one thing on
    /// it that never finishes kept "Logging you in" up until Start Over was pressed).
    private var waits = 0
    /// (Build 42) A look, a moment after the details were sent, at whether the page took them (Attempt).
    private var check: DispatchWorkItem?
    /// The details typed into this page once more already (it had drawn its form again over them).
    private var refilled = false

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
        // (this machine itself — the mock, in development — is as good as https: nothing leaves it; and so is the test
        // school's sign-in page, answered on the phone itself at the app's own scheme — that school's web view and host only)
        #if os(iOS)
        let ownSchool = origin.protocol.lowercased() == TestSchool.scheme && TestSchool.isHost(host) && TestSchool.isHost(canvasHost)
        #else
        let ownSchool = false
        #endif
        let secure = origin.protocol == "https" || host == "localhost" || host == "127.0.0.1" || ownSchool
        let error = (body["error"] as? String).flatMap { $0.isEmpty ? nil : $0 }
        // a page that says the sign-in went wrong — a school page gone stale; (build 63) Canvas's own login page turning the
        // school's answer away; (build 64) any page of the sign-in that says it failed, with nothing on it to fill in, or
        // that came back as an error — the reader runs on no page of Canvas's but its login — is begun again by itself
        let problem = (body["problem"] as? String).flatMap { $0.isEmpty ? nil : $0 }
        if (body["stale"] as? Bool) == true || (body["failed"] as? Bool) == true {
            problemSeen(problem ?? "The sign-in page said something went wrong.")
            return
        }
        if kind == "none", let r = lastResponse, r.host == host, r.code >= 400 {
            problemSeen(problem ?? "\(host) answered with an error (\(r.code)).")
            return
        }
        handle(kind: kind, host: host, error: error, secure: secure)
    }

    // MARK: - Trying again by itself (build 64)

    /// Something went wrong on the way (a page that says so, an error answer, no network, a page that never moved): begun
    /// again by itself; once the tries are spent, the page is shown, and the app says Login Failed with why (`failure`).
    private func problemSeen(_ why: String) {
        if retry(why) { return }
        failure = why
        if phase == .working { handOver() }
    }

    /// The sign-in begun again from Canvas by itself, after a short wait that grows each time (2, 5, then 12 seconds) —
    /// three times at most, so a school that keeps failing cannot send the app round in a loop. The count starts afresh
    /// once the student is through, Start Over is pressed, or five minutes have passed since the last try. Returns whether
    /// it will try again.
    @discardableResult
    func retry(_ why: String) -> Bool {
        if retryPending { return true } // (one already on its way: the same trouble, said again)
        if let last = lastRetry, Date().timeIntervalSince(last) > 300 { retries = 0 }
        guard retries < LoginAssist.maxRetries else { return false }
        retries += 1
        lastRetry = Date()
        let wait = [2.0, 5.0, 12.0][min(retries - 1, 2)]
        print("[Simpl Courses] sign-in: \(why) — trying again (\(retries) of \(LoginAssist.maxRetries)) in \(Int(wait)) s")
        watchdog?.cancel()
        check?.cancel()
        attempt = nil
        if phase == .working { stage = .retrying } // ("Trying again" under "Logging you in")
        retryWork?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.startOver() }
        retryWork = work
        retryPending = true
        DispatchQueue.main.asyncAfter(deadline: .now() + wait, execute: work)
        return true
    }

    /// Signed in, as the app's own screens coming on say (Engine): the tries start afresh.
    func signedIn() {
        retries = 0
        lastRetry = nil
        retryWork?.cancel()
        retryPending = false
        if failure != nil { failure = nil }
    }

    /// The main frame's answer (WebController), for what a page that answered with an error is (`lastResponse`).
    func responded(_ url: URL?, status: Int) {
        lastResponse = (url?.host?.lowercased() ?? "", url?.path ?? "", status)
    }

    /// The last page answered with an error, and was Canvas's own page (not its login): a sign-in that reached Canvas only
    /// to be refused (the app's screens never come on over it — Engine).
    var canvasErrored: Bool {
        guard let r = lastResponse, r.code >= 400 else { return false }
        return r.host == canvasHost && !LoginAssist.isSignIn(r.path)
    }

    /// Start Over, pressed: whatever sign-in was under way forgotten, and Canvas loaded afresh (it sends the school a new
    /// request); saved details are tried again on the way. The tries made by itself start afresh.
    func restart() {
        retries = 0
        lastRetry = nil
        startOver()
    }

    /// Canvas loaded afresh, whatever sign-in was under way forgotten (Start Over, or a try again by itself).
    private func startOver() {
        retryWork?.cancel()
        retryPending = false
        watchdog?.cancel()
        check?.cancel()
        attempt = nil
        navigatedSinceAttempt = false
        autoOff = false
        declined = false
        handoff = false
        failure = nil
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
        stage = .sending
        let again = attempt.map { $0.host == host && $0.step == step && $0.user == user && $0.pass == pass } ?? false
        if !again { refilled = false }
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
        arm(seconds: 15)
        checkTaken(after: 3)
    }

    /// (Build 42) A moment after the details went in: the page still there, not loading, asking the same — and its fields
    /// empty again (it drew its form afresh over what was typed, not yet ready for it) — so typed and sent once more,
    /// rather than waiting on a page that will never move by itself.
    private func checkTaken(after seconds: Double) {
        check?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self, self.phase == .working, let a = self.attempt, !self.navigatedSinceAttempt, !self.refilled,
                  let webView = self.webView, !webView.isLoading, webView.url?.host?.lowercased() == a.host else { return }
            webView.evaluateJavaScript("SimplLogin.state()", in: nil, in: LoginAssist.world) { [weak self] result in
                guard let self, case .success(let value) = result, let r = value as? [String: Any],
                      (r["kind"] as? String) == a.step, (r["filled"] as? Bool) == false,
                      self.phase == .working, !self.navigatedSinceAttempt else { return }
                self.refilled = true
                self.fill(user: a.user, pass: a.pass, host: a.host, step: a.step, auto: a.auto)
            }
        }
        check = work
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: work)
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
        check?.cancel()
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
        waits = 0
        schedule(seconds)
    }

    private func schedule(_ seconds: Double) {
        watchdog?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            // (a page still on its way: a little longer — four times at most, not for as long as anything on it loads)
            if self.webView?.isLoading == true, self.waits < 4 {
                self.waits += 1
                self.schedule(2)
                return
            }
            guard self.phase == .working else { return }
            // (build 42) the saved details sent and nothing came of it — the page never moved: begun again from Canvas,
            // as Start Over would be (build 64: by itself, as any sign-in gone wrong is); otherwise the page, to be
            // finished by hand
            if let a = self.attempt, a.auto, !self.navigatedSinceAttempt, self.retry("The school's page did not take the details.") {
                return
            }
            self.handOver()
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
        failure = nil
        reveal()
    }

    // MARK: - The web view's moves

    /// A page is about to load in the main frame.
    func willLoad(_ url: URL) {
        let host = url.host?.lowercased() ?? ""
        if attempt != nil { navigatedSinceAttempt = true }
        if failure != nil, url.scheme != "about", url.scheme != "data" { failure = nil } // (a page asked for again: a new try)
        schoolPage(url)
        // the saved sign-in page coming up: covered from its first frame, not after it has shown
        if phase == .idle, !autoOff, host != canvasHost, let s = LoginVault.load(), s.host == host {
            phase = .working
            stage = .school
            arm(seconds: 25)
        }
        if phase == .working, host == canvasHost { stage = .opening }
    }

    /// (Build 42) A page's first bytes have arrived in the main frame: Canvas reached is signed in from this moment — the
    /// cover lifts now, not once everything on the page has finished loading (which, with one thing on it slow or
    /// hanging, could keep "Logging you in" up until Start Over was pressed).
    func committed(_ url: URL?) {
        guard let url, let host = url.host?.lowercased(), host == canvasHost, !LoginAssist.isSignIn(url.path) else { return }
        arrived()
    }

    /// The school's own sign-in on its own host: Canvas's /login (and /login/ldap…), Brightspace's /d2l/login (and the
    /// /d2l/lp/auth/login… its form posts to). Every other page there is the school's site, signed in.
    static func isSignIn(_ path: String) -> Bool {
        let p = path.lowercased()
        return p.hasPrefix("/login") || p.hasPrefix("/d2l/login") || p.hasPrefix("/d2l/lp/auth/login")
    }

    /// A page finished loading in the main frame.
    func didLoad(_ url: URL?) {
        guard let url, let host = url.host?.lowercased() else { return }
        schoolPage(url)
        if host == canvasHost && !LoginAssist.isSignIn(url.path) {
            arrived()
            return
        }
        // a page the reader cannot see (it never reports): shown anyway, after a moment (build 42: not sooner than the
        // details just sent into this very page have had to be taken)
        if phase == .working, attempt == nil || navigatedSinceAttempt { arm(seconds: 6) }
    }

    /// On Canvas: signed in.
    private func arrived() {
        let viaForm = attempt != nil && attempt?.auto == false && typed != nil
        attempt = nil
        autoOff = false
        handoff = false
        reveal()
        // details typed in the form got the student in: they are offered to be kept (or kept anew, where the saved ones
        // had stopped working); kept until that is answered (this is said again as the page finishes loading)
        if viaForm { askStay = true } else if !askStay { typed = nil }
    }

    /// Where the web view is: on the school's sign-in, Start Over is offered, and the edge swipe that goes Back is
    /// off — a sign-in page gone back to is one the school refuses as stale.
    private func schoolPage(_ url: URL) {
        let host = url.host?.lowercased() ?? ""
        let on = !host.isEmpty && !canvasHost.isEmpty && (host != canvasHost || LoginAssist.isSignIn(url.path))
        if on != onSchoolPage { onSchoolPage = on }
        webView?.allowsBackForwardNavigationGestures = !on
    }

    /// The page could not be loaded at all (`reason`: what WebKit said, for the app's Login Failed).
    func pageFailed(reason: String? = nil) {
        attempt = nil
        problemSeen(reason ?? "Your school’s site could not be reached.") // (build 64: tried again by itself first)
    }
}

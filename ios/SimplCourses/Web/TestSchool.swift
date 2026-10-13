#if os(iOS)
import CryptoKit
import Foundation
import UIKit
import WebKit

/// The test school: a Canvas of the app's own, at test.simpl.com, for App Review (a reviewer has no school account) and
/// for trying the app out — picked in "Find your school" like any school, signed in to like one, and then the whole app
/// against a student's worth of courses, work, grades, quizzes, discussions, messages and files, on a real iPhone, with
/// no server anywhere.
///
/// The school's site is the mock Canvas the test suites run (scripts/dev/mock-canvas.mjs), carried in the app as
/// Resources/TestSchool/mock.js (written by scripts/dev/make-test-school.mjs) and run by a hidden web view of its own
/// (TestSchoolMock). The web views that show the school — the engine's, a tool's sheet, the text in a native screen —
/// load it at simpltest://test.simpl.com/…, a scheme of the app's own: every request they make to it comes here
/// (TestSchoolHandler), goes to the hidden page, and its answer goes back as the page's. The extension's scripts see a
/// Canvas, the same one the suites test them against.
///
/// In front of the mock is the school's sign-in, the app's own: until the username and password are given, every page
/// is Canvas's sign-in page (the mock's own look, at /login/canvas, where the app's sign-in form and "Stay logged in?"
/// work as they do on a school's), and the API answers 401 as Canvas does. Signed in is a flag kept in UserDefaults, so
/// it lasts from one launch to the next; signing out, or changing school, clears it. What the mock holds (a note made,
/// work handed in) lasts for the launch: the next launch starts the school afresh.
enum TestSchool {
    /// The address the school is picked by, as any school's is.
    static let host = "test.simpl.com"
    /// Its name in the school search.
    static let name = "Simpl Test School"
    /// The scheme its pages are at in the app's web views: answered by the app, never by the network.
    static let scheme = "simpltest"
    /// The school's own address in the app (WebController.baseURL).
    static let baseURL = URL(string: "simpltest://test.simpl.com/")!
    /// Canvas's sign-in page, where the school's sign-in is.
    static let signInPath = "/login/canvas"

    static func isHost(_ host: String?) -> Bool { host?.lowercased() == TestSchool.host }

    /// An address of the test school's own (simpltest://test.simpl.com/…).
    static func owns(_ url: URL?) -> Bool { url?.scheme?.lowercased() == scheme && isHost(url?.host) }

    /// The school search finds it by name ("simpl test", "Simpl Test School") or by the start of its address ("test.simpl"),
    /// from the list in the app — never by asking the network.
    static func matches(_ query: String) -> Bool {
        let words = SchoolSearch.fold(query).split(separator: " ").map(String.init)
        if words.contains(where: { $0.hasPrefix("test.simpl") || $0.hasPrefix("simpltest") }) { return true }
        return words.contains("simpl") && words.contains { $0.count >= 3 && ($0.hasPrefix("test") || "test".hasPrefix($0)) }
    }

    // MARK: - Signing in

    /// The school's username and password are given to App Review in App Store Connect (App Review Information, sign-in
    /// required). No file holds the password: only the username, and the SHA-256 digest of the password after a salt —
    /// sha256("simpl-test-school:" + password), in hex — which is what a password given is checked against.
    private static let username = "tester"
    private static let salt = "simpl-test-school:"
    private static let passwordDigest = "0672a8538b02453f38502aca0e1a4d5bf5e3aad6947b126d1052b477b63b674f"
    private static let signedInKey = "SimplTestSchool.signedIn"
    /// The screenshot suite's sign-in (-SimplTestSchoolSignedIn YES) given up by a sign-out in this launch.
    private static var launchSignInDropped = false

    /// Signed in to the test school: by its sign-in page (kept from one launch to the next), or — for the screenshot
    /// suite only, so it never needs the password — by -SimplTestSchoolSignedIn YES at launch, honoured in a debug build
    /// run with the suite's own -SimplDemo YES and never in the app as shipped.
    static var signedIn: Bool {
        if UserDefaults.standard.bool(forKey: signedInKey) { return true }
        #if DEBUG
        if !launchSignInDropped, UserDefaults.standard.bool(forKey: "SimplDemo"), UserDefaults.standard.bool(forKey: "SimplTestSchoolSignedIn") { return true }
        #endif
        return false
    }

    /// The username (in any case) and the password the school's sign-in page was given are the school's.
    static func accepts(user: String, pass: String) -> Bool {
        guard user.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() == username else { return false }
        let digest = SHA256.hash(data: Data((salt + pass).utf8)).map { String(format: "%02x", $0) }.joined()
        return digest == passwordDigest
    }

    static func signIn() {
        UserDefaults.standard.set(true, forKey: signedInKey)
    }

    /// Signed out (Sign Out, a change of school, the page's own logout): the next page is the sign-in page again.
    static func signOut() {
        UserDefaults.standard.removeObject(forKey: signedInKey)
        launchSignInDropped = true
    }

    // MARK: - The web views' side

    /// The one handler for the scheme, shared by every web view that may show the school.
    static let handler = TestSchoolHandler()

    /// The scheme handed to a web view's configuration. Done for every one that may show the school, whatever school is
    /// picked: WebKit takes a scheme's handler only before the web view is made, never after. (A real school's pages never
    /// go to this scheme — WebController lets only the test school's own web view navigate to it.)
    static func register(on configuration: WKWebViewConfiguration) {
        configuration.setURLSchemeHandler(handler, forURLScheme: scheme)
    }

    /// The school is about to be shown: its hidden page starts loading now, so the first page is not kept waiting for it.
    static func warmUp() {
        TestSchoolMock.shared.start()
    }

    /// One of the school's files (FilePreview): its answer, the same as its pages get, redirects followed.
    static func fetch(_ url: URL, done: @escaping (Answer?) -> Void) {
        answer(Ask(method: "GET", url: url, headers: [:], body: Data()), document: false, done: done)
    }

    // MARK: - Requests and answers

    /// A request to the school, as a web view made it.
    struct Ask {
        var method: String
        var url: URL
        var headers: [String: String]
        var body: Data
    }

    /// The school's answer to one.
    struct Answer {
        var status: Int
        var headers: [String: String]
        var body: Data

        func header(_ name: String) -> String? {
            headers.first { $0.key.lowercased() == name.lowercased() }?.value
        }
    }

    /// A request's answer. A redirect is answered as a browser would end up: a page that is being navigated to goes on to
    /// where it is sent by a page that replaces itself (WebKit does not follow a redirect a scheme handler answers with,
    /// and the page's address must be the one it lands on: the sign-in reader, the router and the extension all read it);
    /// anything else — a fetch, a frame — is asked again at the new address here, as fetch follows a redirect, and gets
    /// the answer from there.
    static func answer(_ ask: Ask, document: Bool, hops: Int = 0, done: @escaping (Answer?) -> Void) {
        respond(to: ask) { answer in
            guard let answer, (300...399).contains(answer.status), let location = answer.header("Location"),
                  let next = URL(string: location, relativeTo: ask.url)?.absoluteURL else {
                done(answer)
                return
            }
            if document {
                done(TestSchool.onward(to: next))
                return
            }
            guard TestSchool.owns(next), hops < 8 else {
                done(answer)
                return
            }
            // (307 and 308 keep the method and the body; the others go on as a GET, as browsers do)
            let keep = answer.status == 307 || answer.status == 308
            var again = Ask(method: keep ? ask.method : "GET", url: next, headers: ask.headers, body: keep ? ask.body : Data())
            if !keep { again.headers = again.headers.filter { !["content-type", "content-length"].contains($0.key.lowercased()) } }
            TestSchool.answer(again, document: false, hops: hops + 1, done: done)
        }
    }

    /// The school's sign-in in front of the mock: Canvas's sign-in page and its form's answer here, the rest the mock's
    /// once signed in (401 from the API before, as Canvas answers; the sign-in page for any page).
    private static func respond(to ask: Ask, done: @escaping (Answer?) -> Void) {
        var path = ask.url.path
        while path.count > 1 && path.hasSuffix("/") { path.removeLast() }
        if path.isEmpty { path = "/" }
        let mock = TestSchoolMock.shared
        if path == "/login" || path == signInPath {
            if ask.method == "POST" && path == signInPath {
                let form = formFields(ask.body)
                let user = form["pseudonym_session[unique_id]"] ?? ""
                if accepts(user: user, pass: form["pseudonym_session[password]"] ?? "") {
                    signIn()
                    done(redirect("/"))
                } else {
                    // (Canvas's own words for it, over its form again, the username kept)
                    mock.loginPage(error: "Invalid username or password.", user: user) { done($0.map(TestSchool.page)) }
                }
                return
            }
            if signedIn { done(redirect("/")); return }
            if path == "/login" { done(redirect(signInPath)); return }
            mock.loginPage(error: nil, user: "") { done($0.map(TestSchool.page)) }
            return
        }
        guard signedIn else {
            done(path.hasPrefix("/api/") ? signedOutAPI : redirect(signInPath))
            return
        }
        if path == "/logout" && ask.method == "POST" {
            signOut()
            done(redirect(signInPath))
            return
        }
        mock.handle(method: ask.method, target: target(of: ask.url), headers: ask.headers, body: ask.body, done: done)
    }

    /// The answer a web view gets: the status, the headers and the body, at the address it asked for (no answer: as a
    /// server that cannot be reached, which the engine shows as such).
    static func send(_ answer: Answer?, for url: URL, to task: WKURLSchemeTask) {
        guard let answer, let response = HTTPURLResponse(url: url, statusCode: answer.status, httpVersion: "HTTP/1.1", headerFields: answer.headers) else {
            task.didFailWithError(URLError(.cannotConnectToHost))
            return
        }
        task.didReceive(response)
        if !answer.body.isEmpty { task.didReceive(answer.body) }
        task.didFinish()
    }

    /// A page being navigated to (the main frame's, or a tool sheet's), rather than a fetch or a frame: WebKit gives the
    /// page's own address as the first party, and a fetch says what it accepts (a navigation to this scheme says nothing,
    /// or, where WebKit does say, a browser's HTML) and the interface's requests say they are one.
    static func isDocument(_ request: URLRequest) -> Bool {
        if request.value(forHTTPHeaderField: "X-Requested-With") != nil { return false }
        if let accept = request.value(forHTTPHeaderField: "Accept"), !accept.contains("application/xhtml+xml") { return false }
        guard let main = request.mainDocumentURL else { return true }
        return main.absoluteString == request.url?.absoluteString
    }

    /// A request's body: WebKit hands a form's or a fetch's as data, a larger one (a file) as a stream.
    static func body(of request: URLRequest) -> Data {
        if let data = request.httpBody { return data }
        guard let stream = request.httpBodyStream else { return Data() }
        stream.open()
        defer { stream.close() }
        var out = Data()
        var buffer = [UInt8](repeating: 0, count: 64 * 1024)
        while out.count < 64 * 1024 * 1024 {
            let n = stream.read(&buffer, maxLength: buffer.count)
            if n <= 0 { break }
            out.append(buffer, count: n)
        }
        return out
    }

    /// The path and query the mock is asked for, as they were sent (still percent-encoded).
    private static func target(of url: URL) -> String {
        guard let c = URLComponents(url: url, resolvingAgainstBaseURL: false) else { return "/" }
        let path = c.percentEncodedPath.isEmpty ? "/" : c.percentEncodedPath
        return c.percentEncodedQuery.map { "\(path)?\($0)" } ?? path
    }

    /// A form's fields, as a browser sends them (application/x-www-form-urlencoded).
    private static func formFields(_ body: Data) -> [String: String] {
        var out: [String: String] = [:]
        let decode = { (s: Substring) -> String in
            let spaced = s.replacingOccurrences(of: "+", with: " ")
            return spaced.removingPercentEncoding ?? spaced
        }
        for pair in String(decoding: body, as: UTF8.self).split(separator: "&") {
            let parts = pair.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
            guard let key = parts.first else { continue }
            out[decode(key)] = parts.count > 1 ? decode(parts[1]) : ""
        }
        return out
    }

    private static func redirect(_ location: String) -> Answer {
        Answer(status: 302, headers: ["Location": location], body: Data())
    }

    private static func page(_ html: String) -> Answer {
        Answer(status: 200, headers: ["Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store"], body: Data(html.utf8))
    }

    /// The API signed out, in Canvas's words (lib/canvas-api.js reads them as a session that has ended).
    private static let signedOutAPI = Answer(status: 401, headers: ["Content-Type": "application/json; charset=utf-8"], body: Data(#"{"status":"unauthenticated","errors":[{"message":"user authorization required"}]}"#.utf8))

    /// A page that goes on to `url` at once, in its own place in the history (a redirect, for a page being navigated to).
    private static func onward(to url: URL) -> Answer {
        let to = JS.literal(url.absoluteString) ?? "\"/\""
        return page("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title></title><script>location.replace(\(to))</script></head><body></body></html>")
    }
}

/// Answers the web views' requests to simpltest://test.simpl.com: each goes to the school (TestSchool.answer) and its
/// answer back to the web view. On the main thread throughout, as WebKit calls it; a task WebKit has stopped (its page
/// went away) is never answered — touching one after its stop is an error WebKit raises.
final class TestSchoolHandler: NSObject, WKURLSchemeHandler {
    /// The tasks started and not stopped or answered yet.
    private var running = Set<ObjectIdentifier>()

    func webView(_ webView: WKWebView, start urlSchemeTask: WKURLSchemeTask) {
        let key = ObjectIdentifier(urlSchemeTask as AnyObject)
        running.insert(key)
        TestSchoolMock.shared.keepInWindow(webView.window)
        let request = urlSchemeTask.request
        guard let url = request.url, TestSchool.owns(url) else {
            running.remove(key)
            urlSchemeTask.didFailWithError(URLError(.unsupportedURL))
            return
        }
        let ask = TestSchool.Ask(method: (request.httpMethod ?? "GET").uppercased(), url: url, headers: request.allHTTPHeaderFields ?? [:], body: TestSchool.body(of: request))
        // (the sign-in form sent is always a page being navigated to: nothing else posts there)
        let signingIn = ask.method == "POST" && url.path == TestSchool.signInPath && request.value(forHTTPHeaderField: "X-Requested-With") == nil
        TestSchool.answer(ask, document: signingIn || TestSchool.isDocument(request)) { [weak self] answer in
            // (the task holds on to itself here until it is answered; one stopped meanwhile is left alone)
            guard let self, self.running.remove(key) != nil else { return }
            TestSchool.send(answer, for: url, to: urlSchemeTask)
        }
    }

    func webView(_ webView: WKWebView, stop urlSchemeTask: WKURLSchemeTask) {
        running.remove(ObjectIdentifier(urlSchemeTask as AnyObject))
    }
}

/// The hidden page the school runs in: Resources/TestSchool/index.html with mock.js, in a web view of its own (no
/// extension, nothing kept on disk), asked through window.__mockHandle for each request and window.__mockLoginPage for
/// the sign-in page. It is kept in the app's window — a single point, under everything, out of sight and out of reach of
/// a tap or VoiceOver — because WebKit suspends the pages of a web view outside a window, and a suspended page answers
/// nothing. If its process ends (memory pressure), it is loaded again: what the school held goes, as at a launch.
final class TestSchoolMock: NSObject, WKNavigationDelegate {
    static let shared = TestSchoolMock()

    private enum State { case off, loading, ready, broken }
    private var state = State.off
    private var view: WKWebView?
    private var generation = 0
    /// What waits for the page to be ready: told true once it is, false if it could not be.
    private var waiting: [(Bool) -> Void] = []

    /// The page made and loaded, unless it is loading or loaded already.
    func start() {
        guard state == .off || state == .broken else { return }
        generation += 1
        let mine = generation
        state = .loading
        view?.removeFromSuperview()
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .nonPersistent()
        let v = WKWebView(frame: CGRect(x: 0, y: 0, width: 1, height: 1), configuration: config)
        v.navigationDelegate = self
        v.isUserInteractionEnabled = false
        v.alpha = 0.01
        v.isAccessibilityElement = false
        v.accessibilityElementsHidden = true
        #if DEBUG
        v.isInspectable = true // (Safari's Web Inspector reaches the school's page in a debug build)
        #endif
        view = v
        guard let folder = Bundle.main.resourceURL?.appendingPathComponent("TestSchool", isDirectory: true) else {
            settle(false)
            return
        }
        v.loadFileURL(folder.appendingPathComponent("index.html"), allowingReadAccessTo: folder)
        DispatchQueue.main.async { [weak self] in self?.keepInWindow(nil) } // (not in the middle of SwiftUI making the screen)
        // (a page that never comes up: what waits on it is answered, as a school that cannot be reached, not kept waiting)
        DispatchQueue.main.asyncAfter(deadline: .now() + 20) { [weak self] in
            guard let self, self.generation == mine, self.state == .loading else { return }
            self.settle(false)
        }
    }

    /// In a window, so WebKit keeps the page running: the window of the web view asking, else the app's key window.
    func keepInWindow(_ preferred: UIWindow?) {
        guard let v = view, v.window == nil else { return }
        let windows = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.flatMap { $0.windows }
        guard let window = preferred ?? windows.first(where: { $0.isKeyWindow }) ?? windows.first else { return }
        v.frame = CGRect(x: 0, y: 0, width: 1, height: 1)
        window.insertSubview(v, at: 0)
    }

    private func settle(_ ok: Bool) {
        state = ok ? .ready : .broken
        let told = waiting
        waiting = []
        for go in told { go(ok) }
    }

    private func whenReady(_ go: @escaping (Bool) -> Void) {
        switch state {
        case .ready:
            go(true)
        case .loading:
            waiting.append(go)
        case .off, .broken:
            waiting.append(go)
            start()
        }
    }

    /// One request, answered by the mock (TestSchool.Answer), or nil when the page could not answer.
    func handle(method: String, target: String, headers: [String: String], body: Data, done: @escaping (TestSchool.Answer?) -> Void) {
        whenReady { [weak self] ok in
            guard ok, let view = self?.view else { done(nil); return }
            let headerJSON = (try? JSONSerialization.data(withJSONObject: headers)).flatMap { String(data: $0, encoding: .utf8) } ?? "{}"
            view.callAsyncJavaScript("return await window.__mockHandle(method, url, headers, body);", arguments: ["method": method, "url": target, "headers": headerJSON, "body": body.base64EncodedString()], in: nil, in: .page) { result in
                guard case .success(let value) = result, let r = value as? [String: Any], let status = (r["status"] as? NSNumber)?.intValue else {
                    done(nil)
                    return
                }
                var fields: [String: String] = [:]
                for (k, v) in r["headers"] as? [String: Any] ?? [:] { fields[k] = (v as? String) ?? "\(v)" }
                let data = (r["bodyBase64"] as? String).flatMap { Data(base64Encoded: $0) } ?? Data()
                done(TestSchool.Answer(status: status, headers: fields, body: data))
            }
        }
    }

    /// Canvas's sign-in page as the test school's (the mock's own, retitled), with an error over the form and the
    /// username kept when a sign-in was refused.
    func loginPage(error: String?, user: String, done: @escaping (String?) -> Void) {
        whenReady { [weak self] ok in
            guard ok, let view = self?.view else { done(nil); return }
            view.callAsyncJavaScript("return window.__mockLoginPage(error, user);", arguments: ["error": error ?? "", "user": user], in: nil, in: .page) { result in
                if case .success(let value) = result, let html = value as? String { done(html) } else { done(nil) }
            }
        }
    }

    // MARK: WKNavigationDelegate

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard webView === view else { return }
        // (loaded is not enough: the mock must have come up in it)
        webView.evaluateJavaScript("typeof window.__mockHandle === 'function'") { [weak self] value, _ in
            guard let self, webView === self.view, self.state == .loading else { return }
            self.settle((value as? Bool) == true)
        }
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        guard webView === view, state == .loading else { return }
        settle(false)
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        guard webView === view, state == .loading else { return }
        settle(false)
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        guard webView === view else { return }
        state = .off
        start()
    }
}
#endif

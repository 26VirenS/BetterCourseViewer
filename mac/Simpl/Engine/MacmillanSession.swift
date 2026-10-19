import AppKit
import SwiftUI
import WebKit

/// (1.3.66) A Macmillan Learning assignment (Achieve, Sapling, LaunchPad) in a popup over the window, as an Alta one is:
/// the assignment's launch through Canvas, its page as Macmillan draws it, and Save Page Code — every bit of the page's
/// code, each frame's too, in a text file — for the work that lays it out as the app's own (AltaSession's way).
@MainActor
final class MacmillanSession: ObservableObject {
    /// The page still opening (the launch through Canvas): a spinner over it meanwhile.
    @Published private(set) var opening = true
    /// The launch could not be had from Canvas.
    @Published private(set) var failure: String?
    /// (1.3.78) The page's own heading (its assignment's name, as Macmillan titles it), read once it has loaded: the
    /// popup's title when Canvas's name for it is not to be had.
    @Published private(set) var heading: String?
    /// (1.3.114) The assessment as its skin reads it (extension/tools/macmillan/skin.js): its cover, or the question shown
    /// with the list of questions, the score and the controls; nil until the assessment player has said (an assignment
    /// of another kind of Macmillan's is shown as it is).
    @Published private(set) var state: MacmillanState?
    /// (1.3.123) Macmillan's pop-up open (You solved it…), shown as the app's own card — as Knewton Alta's are.
    @Published var dialog: AltaDialog?
    /// Given back to Macmillan's page (Show in Macmillan's Page): shown as it is drawn until it closes.
    @Published private(set) var dialogInPage = false
    /// (1.3.114) Show Browser: Macmillan's own page, as Macmillan draws it, in place of the skinned one.
    @Published private(set) var browser = UserDefaults.standard.bool(forKey: MacmillanSession.browserKey)
    static let browserKey = "SimplMacmillanShowBrowser"
    /// (1.3.116) The app light or dark: the skinned page laid on the popup's glass in the same (the skin's 'theme').
    private(set) var dark = false
    /// The assessment player's frame, which the skin's reports come from and its presses go to.
    private var player: WKFrameInfo?
    private var launchOpened = false

    let launch: AltaLaunch
    let web: WKWebView
    /// (1.3.72) The Macmillan assignment open now, for the Developer menu's Save Page Code.
    static weak var current: MacmillanSession?
    private let relay: MacmillanRelay
    private var loading: NSKeyValueObservation?
    /// Each frame in the page that has said it is there (the page itself is asked as the main frame).
    private var frames: [WKFrameInfo] = []

    static let handler = "simplMacmillan"
    /// In every page and frame: it says it is there, for Save Page Code.
    private static let script = WKUserScript(source: """
    (function () {
      if (window.__simplMacmillanOn || !/^https?:$/.test(location.protocol)) return;
      window.__simplMacmillanOn = true;
      try { window.webkit.messageHandlers.\(handler).postMessage(JSON.stringify({ kind: 'frame' })); } catch (e) {}
    })();
    """, injectionTime: .atDocumentStart, forMainFrameOnly: false)

    /// (1.3.114) The skin, from the extension the app carries; and before it, whether it is to skin (Show Browser).
    private static let skin: WKUserScript? = {
        guard let dir = Bundle.main.url(forResource: "extension", withExtension: nil),
              let js = try? String(contentsOf: dir.appendingPathComponent("tools/macmillan/skin.js"), encoding: .utf8) else { return nil }
        let off = UserDefaults.standard.bool(forKey: browserKey)
        return WKUserScript(source: "window.__simplMacmillanSkinned = \(off ? "false" : "true");\n" + js, injectionTime: .atDocumentStart, forMainFrameOnly: false)
    }()

    init(launch: AltaLaunch) {
        self.launch = launch
        CrossSiteCookies.apply()
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default() // (the Canvas session: the launch finds the student signed in)
        config.mediaTypesRequiringUserActionForPlayback = []
        config.applicationNameForUserAgent = "Version/18.4 Safari/605.1.15"
        config.userContentController.addUserScript(Self.script)
        // (1.3.114) the skin, in every page and frame (it acts in the assessment player and the page that holds it)
        if let skin = Self.skin {
            // (1.3.116) light or dark as the app is, from the page's first moment (told again by setDark)
            let dark = NSApp.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            self.dark = dark
            config.userContentController.addUserScript(WKUserScript(source: "window.__simplMacmillanDark = \(dark ? "true" : "false"); window.__simplMacmillanGlass = true;", injectionTime: .atDocumentStart, forMainFrameOnly: false))
            config.userContentController.addUserScript(skin)
        }
        let r = MacmillanRelay()
        config.userContentController.add(r, name: Self.handler)
        relay = r
        web = WKWebView(frame: NSRect(x: 0, y: 0, width: 1000, height: 800), configuration: config)
        web.isInspectable = true
        // (1.3.79) Macmillan's page in light whatever the app's appearance, as every tool's is (ToolDarkPage.look): made
        // for light, it read badly in dark; the popup around it keeps the app's
        ToolDarkPage.look(web, dark: false)
        // (1.3.116) the skinned page clear, laid on the popup's glass (Show Browser puts a white page under it: MacmillanScreen)
        web.setValue(false, forKey: "drawsBackground")
        web.underPageBackgroundColor = .clear
        r.session = self
        MacmillanSession.current = self
        loading = web.observe(\.isLoading, options: [.new]) { [weak self] w, _ in
            let done = !w.isLoading && w.url != nil
            Task { @MainActor in
                if done, self?.opening == true { withAnimation(Motion.gentle) { self?.opening = false } }
                if done, self?.heading == nil { await self?.readHeading() } // (the first page's: never a later page's)
            }
        }
    }

    /// The page's main heading, as it reads now (nil while it has none).
    private func readHeading() async {
        let js = "return ((document.querySelector('main h1') || document.querySelector('h1') || {}).innerText || '').trim()"
        guard let h = await run(js, in: nil) as? String else { return }
        let words = h.split(whereSeparator: \.isWhitespace).joined(separator: " ")
        if !words.isEmpty, words.count <= 160 { heading = words }
    }

    /// Opened: the assignment's launch through Canvas (or the page asked for).
    func start(engine: Engine) async {
        guard web.url == nil else { return }
        if let p = launch.page, let u = engine.absolute(p) {
            web.load(URLRequest(url: u))
            return
        }
        struct Answer: Decodable { var url: String }
        do {
            let a = try await engine.call("toolLaunch", ["course": launch.course, "assignment": launch.assignment], as: Answer.self)
            guard let u = URL(string: a.url) else { throw EngineError.unreadable }
            web.load(URLRequest(url: u))
        } catch {
            withAnimation(Motion.gentle) { failure = "Macmillan Learning could not be opened. \(error.localizedDescription)"; opening = false }
        }
    }

    func take(_ text: String, frame: WKFrameInfo) {
        guard let data = text.data(using: .utf8),
              let head = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }
        // (1.3.114) the assessment as the skin reads it: the popup drawn round it
        if head["kind"] as? String == "state", let s = try? JSONDecoder().decode(MacmillanState.self, from: data) {
            player = frame
            if s.index != state?.index || s.page != state?.page { AltaGraphWindow.shared.close() } // (1.3.122: another question, its picture's window goes)
            if s != state { withAnimation(Motion.gentle) { state = s } }
            if opening { withAnimation(Motion.gentle) { opening = false } }
            if browser { Task { await act("skin", false) } }
            // (1.3.116) a player that came up thinking otherwise (a frame opened before the app changed) is told
            if let d = head["dark"] as? Bool, d != dark { Task { await act("theme", dark) } }
            // (the screenshot suite's -SimplMacmillanOpen YES: the cover's Open Assignment pressed, once)
            if s.isCover, !launchOpened, UserDefaults.standard.bool(forKey: "SimplMacmillanOpen") {
                launchOpened = true
                Task { try? await Task.sleep(nanoseconds: 600_000_000); await act("open") }
            }
            return
        }
        // (1.3.123) Macmillan's pop-up, read whole by the skin: the app's own card over the popup, or none
        if head["kind"] as? String == "dialog" {
            guard head["open"] as? Bool == true else { withAnimation(Motion.gentle) { dialog = nil; dialogInPage = false }; return }
            if head["inPage"] as? Bool == true { withAnimation(Motion.gentle) { dialog = nil; dialogInPage = true }; return }
            let title = head["title"] as? String ?? ""
            let html = head["html"] as? String ?? ""
            let choices = (head["buttons"] as? [[String: Any]] ?? []).map { b in
                AltaDialog.Choice(label: b["label"] as? String ?? "", primary: b["primary"] as? Bool ?? false,
                                  enabled: b["enabled"] as? Bool ?? true, close: b["close"] as? Bool ?? false)
            }
            guard !html.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !choices.isEmpty else { dialogDismissed(); return }
            dialogInPage = false
            if dialog != nil {
                dialog?.title = title
                dialog?.html = html
                dialog?.buttons = choices
                dialog?.step += 1
            } else {
                withAnimation(Motion.gentle) { dialog = AltaDialog(title: title, html: html, buttons: choices, base: frame.request.url ?? web.url ?? URL(fileURLWithPath: "/")) }
            }
            return
        }
        // (1.3.122) a question's picture pressed: large, in a window of its own over the popup, as Knewton Alta's graphs
        // open — moved and sized as the student likes (AltaGraphWindow); the picture's bytes as the page had them, or
        // its address, read with the page's cookies
        if head["kind"] as? String == "pic" {
            let title = (head["title"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? launch.title
            if let b64 = head["data"] as? String, let bytes = Data(base64Encoded: b64), let image = NSImage(data: bytes) {
                AltaGraphWindow.shared.show(image, title: title, session: nil)
            } else if let src = head["url"] as? String, let url = URL(string: src) {
                Task { await openPic(url, title: title) }
            }
            return
        }
        guard head["kind"] as? String == "frame", !frame.isMainFrame else { return }
        let key = frame.request.url?.absoluteString ?? ""
        frames.removeAll { ($0.request.url?.absoluteString ?? "") == key }
        frames.append(frame)
        if frames.count > 30 { frames.removeFirst() }
    }

    /// (1.3.123) One of the pop-up's buttons, pressed in Macmillan's own.
    func pressDialog(_ index: Int) { Task { _ = await act("dialog", index) } }

    /// The card put away without a button (Show in Macmillan's Page): the pop-up given back to the page.
    func dialogDismissed() {
        dialog = nil
        Task { _ = await act("dialogPage") }
    }

    /// (1.3.122) A picture by its address, with the page's cookies (it may be behind Macmillan's sign-in), in the window.
    private func openPic(_ url: URL, title: String) async {
        let cookies = await withCheckedContinuation { (go: CheckedContinuation<[HTTPCookie], Never>) in
            web.configuration.websiteDataStore.httpCookieStore.getAllCookies { go.resume(returning: $0) }
        }
        var req = URLRequest(url: url)
        for (k, v) in HTTPCookie.requestHeaderFields(with: cookies.filter { url.host?.hasSuffix($0.domain.trimmingCharacters(in: CharacterSet(charactersIn: "."))) ?? false }) { req.setValue(v, forHTTPHeaderField: k) }
        guard let res = try? await URLSession.shared.data(for: req), let image = NSImage(data: res.0) else { return }
        AltaGraphWindow.shared.show(image, title: title, session: nil)
    }

    /// Every bit of code of the page as it is now — the page and each frame in it: its HTML (with any web component's own
    /// tree), and the scripts and style sheets it loads — in one text file in Downloads. Addresses are written without
    /// their queries (a launch's tokens ride in them).
    func savePageCode() async -> URL? {
        let version = (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "?"
        var out = "Simpl for Mac \(version): Macmillan Learning page code, saved \(Date().formatted(date: .complete, time: .standard))\n"
        var seen = Set<String>()
        var count = 0
        let targets: [WKFrameInfo?] = [nil] + frames.map { Optional($0) }
        for f in targets {
            guard let r = await run(AltaSession.collect, in: f) as? [String: Any] else { continue }
            let url = r["url"] as? String ?? "?"
            let html = r["html"] as? String ?? ""
            guard seen.insert(url + "#" + String(html.count)).inserted else { continue }
            count += 1
            out += "\n\n" + String(repeating: "=", count: 100) + "\n"
            out += "PAGE \(count)\(f == nil ? " (the popup's page)" : " (a frame in it)"): \(url)\n"
            out += "Title: \(r["title"] as? String ?? "")\n" + String(repeating: "=", count: 100) + "\n"
            out += "\n---- HTML ----\n" + html + "\n"
            for sh in r["shadows"] as? [[String: Any]] ?? [] {
                out += "\n---- WEB COMPONENT'S OWN TREE: \(sh["host"] as? String ?? "") ----\n" + (sh["html"] as? String ?? "") + "\n"
            }
            for file in r["files"] as? [[String: Any]] ?? [] {
                let kind = (file["kind"] as? String ?? "file").uppercased()
                out += "\n---- \(kind): \(file["url"] as? String ?? "") ----\n"
                if let t = file["text"] as? String { out += t + "\n" } else { out += "(could not be read: \(file["error"] as? String ?? "unknown"))\n" }
            }
        }
        guard count > 0 else { return nil }
        let stamp: String = {
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.dateFormat = "yyyy-MM-dd 'at' HH.mm.ss"
            return f.string(from: Date())
        }()
        let downloads = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads", isDirectory: true)
        let file = downloads.appendingPathComponent("Simpl Macmillan Code \(stamp).txt")
        do { try out.write(to: file, atomically: true, encoding: .utf8) } catch { return nil }
        return file
    }

    /// (1.3.114) A press of the popup's, done on Macmillan's page: Open Assignment, a question, the arrows, the main
    /// button, Resources, AI Tutor, Solution, Switch to New Layout, an attempt, the skin on or off.
    @discardableResult
    func act(_ command: String, _ argument: Any? = nil) async -> Bool {
        guard let player else { return false }
        let arg: String
        switch argument {
        case let n as Int: arg = String(n)
        case let b as Bool: arg = b ? "true" : "false"
        case nil: arg = "null"
        default: arg = "null"
        }
        let js = "return window.__simplMacmillan ? window.__simplMacmillan(\"\(command)\", \(arg)) : false"
        return await run(js, in: player) as? Bool ?? false
    }

    /// (1.3.114) Show Browser on or off, kept for every Macmillan assignment: the page skinned again, or as Macmillan
    /// draws it — in the player and in the page that holds it.
    func setBrowser(_ on: Bool) {
        browser = on
        UserDefaults.standard.set(on, forKey: Self.browserKey)
        Task {
            await act("skin", !on)
            _ = await run("return window.__simplMacmillan ? window.__simplMacmillan('skin', \(on ? "false" : "true")) : false", in: nil)
        }
    }

    /// (1.3.116) The app gone light or dark: the skinned page with it.
    func setDark(_ on: Bool) {
        guard on != dark else { return }
        dark = on
        Task { await act("theme", on) }
    }

    /// The script run in that frame (nil: the main page); what it gives back, or nil.
    private func run(_ js: String, in frame: WKFrameInfo?) async -> Any? {
        await withCheckedContinuation { (go: CheckedContinuation<Any?, Never>) in
            web.callAsyncJavaScript(js, arguments: [:], in: frame, in: .page) { result in
                switch result {
                case .success(let v): go.resume(returning: v)
                case .failure: go.resume(returning: nil)
                }
            }
        }
    }
}

/// The page's messages, to the session (held weakly: the web view's controller keeps its handlers).
final class MacmillanRelay: NSObject, WKScriptMessageHandler {
    weak var session: MacmillanSession?

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let text = message.body as? String else { return }
        let frame = message.frameInfo
        MainActor.assumeIsolated { session?.take(text, frame: frame) }
    }
}

/// (1.3.114) Macmillan's assessment, as its skin reads it (extension/tools/macmillan/skin.js): before it is opened, its
/// cover; then the question shown, the list of questions with how each stands, the score, and the controls on the page.
struct MacmillanState: Decodable, Equatable {
    struct Cover: Decodable, Equatable {
        var name: String?
        var due: String?
        var count: Int?
        var policies: [String]?
    }
    struct Item: Decodable, Equatable, Identifiable {
        var n: Int
        var label: String
        /// "default" (not yet answered), "success" (solved), else Macmillan's own word for it ("error", …).
        var state: String
        var note: String
        var points: String?
        var id: Int { n }
        var solved: Bool { state == "success" }
        var tried: Bool { !solved && state != "default" }
    }
    struct Tools: Decodable, Equatable {
        var resources: Bool
        var tutor: Bool
        var solution: Bool
        var layout: Bool
    }
    struct Attempts: Decodable, Equatable {
        var label: String
        var choices: [String]
    }
    /// "cover", "question" or "other".
    /// (1.3.117) Macmillan's verdict on the question shown: "right", "wrong", or none yet.
    var verdict: String?
    var page: String
    var cover: Cover?
    var title: String?
    var index: Int?
    var count: Int?
    var completed: Int?
    var score: [Double]?
    var policies: [String]?
    var items: [Item]?
    var main: String?
    var mainOn: Bool?
    var tools: Tools?
    var attempts: Attempts?
    var results: [String]?
    var first: Bool?
    var last: Bool?

    var isQuestion: Bool { page == "question" }
    var isCover: Bool { page == "cover" }
}

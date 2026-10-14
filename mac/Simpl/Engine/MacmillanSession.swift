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

    init(launch: AltaLaunch) {
        self.launch = launch
        CrossSiteCookies.apply()
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default() // (the Canvas session: the launch finds the student signed in)
        config.mediaTypesRequiringUserActionForPlayback = []
        config.applicationNameForUserAgent = "Version/18.4 Safari/605.1.15"
        config.userContentController.addUserScript(Self.script)
        let r = MacmillanRelay()
        config.userContentController.add(r, name: Self.handler)
        relay = r
        web = WKWebView(frame: NSRect(x: 0, y: 0, width: 1000, height: 800), configuration: config)
        web.isInspectable = true
        // (1.3.79) Macmillan's page in light whatever the app's appearance, as every tool's is (ToolDarkPage.look): made
        // for light, it read badly in dark; the popup around it keeps the app's
        ToolDarkPage.look(web, dark: false)
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
              let head = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              head["kind"] as? String == "frame", !frame.isMainFrame else { return }
        let key = frame.request.url?.absoluteString ?? ""
        frames.removeAll { ($0.request.url?.absoluteString ?? "") == key }
        frames.append(frame)
        if frames.count > 30 { frames.removeFirst() }
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

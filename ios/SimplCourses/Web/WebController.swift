import Combine
import Foundation
#if os(iOS)
import SafariServices
import UIKit
#else
import AppKit
#endif
import WebKit

/// One web view with the extension injected: the Canvas site (an isolated content world, like an
/// extension's content scripts) or the bundled settings page (its own scripts, so the page world).
/// Also the delegate for navigation policy, new windows and JavaScript dialogs.
final class WebController: NSObject, ObservableObject, WKNavigationDelegate, WKUIDelegate {
    let mode: ScriptBundle.Mode
    let webView: WKWebView
    /// The school sign-in (LoginAssist): what the reader in its own world says, and the overlays over this view.
    let login: LoginAssist
    private let world: WKContentWorld
    @Published private(set) var isLoading = false
    @Published private(set) var pageTitle = ""
    /// The app's own chrome is on (Native/Engine.swift): a link pressed in a page Canvas drew becomes a screen on the
    /// app's stack, a link to another site opens in Safari's sheet, and the page's swipe-back is the stack's.
    var shellOn = false {
        didSet { webView.allowsBackForwardNavigationGestures = !shellOn }
    }
    var onNavigationStart: (() -> Void)?
    var onFinish: ((URL?) -> Void)?
    var onOpenInShell: ((URL) -> Void)?

    init(mode: ScriptBundle.Mode) {
        self.mode = mode
        switch mode {
        case .settings:
            world = .page
            login = LoginAssist(canvasHost: "")
        case .canvas(let host):
            world = .defaultClient
            login = LoginAssist(canvasHost: host)
        }
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default() // the Canvas session persists across launches, like a browser
        // Identify as Safari: Canvas serves its full web interface to Safari and a cut-down one to
        // unknown web views, and the extension is written for the full one.
        #if os(iOS)
        config.allowsInlineMediaPlayback = true
        config.applicationNameForUserAgent = "Version/17.0 Mobile/15E148 Safari/604.1"
        #else
        config.applicationNameForUserAgent = "Version/18.4 Safari/605.1.15" // (the Mac app: a Mac's Safari, Canvas's desktop pages)
        #endif
        let ucc = WKUserContentController()
        for script in ScriptBundle.userScripts(for: mode, world: world) { ucc.addUserScript(script) }
        ucc.addScriptMessageHandler(Bridge.shared, contentWorld: world, name: Bridge.handlerName)
        if case .canvas(let host) = mode {
            // the sign-in reader, on every page (the school's sign-in pages above all), in a world of its own
            ucc.addUserScript(ScriptBundle.loginScript(canvasHost: host))
            ucc.add(login, contentWorld: LoginAssist.world, name: LoginAssist.handlerName)
        }
        config.userContentController = ucc
        webView = WKWebView(frame: .zero, configuration: config)
        super.init()
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.allowsBackForwardNavigationGestures = true
        #if os(iOS)
        webView.isOpaque = false
        webView.backgroundColor = .systemBackground
        // The page lays itself out under the status bar and the home indicator using the safe-area
        // insets it reads from CSS (ScriptBundle sets viewport-fit=cover), so WebKit must not add its own.
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        #endif
        Bridge.shared.register(webView, world: world)
        login.webView = webView
        login.onRestart = { [weak self] in self?.load() } // (Start Over: Canvas afresh, which asks the school anew)
        if case .canvas = mode {
            CookieJar.shared.watch(webView.configuration.websiteDataStore.httpCookieStore) // the session outlives the app
            #if os(iOS)
            let refresh = UIRefreshControl()
            refresh.addTarget(self, action: #selector(pullToRefresh(_:)), for: .valueChanged)
            webView.scrollView.refreshControl = refresh
            #endif
        }
    }

    #if os(iOS)
    @objc private func pullToRefresh(_ sender: UIRefreshControl) {
        webView.reload()
    }
    #endif

    /// Pull to refresh put away (the phone's; a Mac has none).
    private func endRefreshing() {
        #if os(iOS)
        webView.scrollView.refreshControl?.endRefreshing()
        #endif
    }

    /// mailto:, tel:, another app's address: the system's to open.
    static func openSystem(_ url: URL) {
        #if os(iOS)
        UIApplication.shared.open(url)
        #else
        NSWorkspace.shared.open(url)
        #endif
    }

    var currentURL: URL? { webView.url }

    /// The school's Canvas: https on its host — or, for the simulator suite, the address given at launch
    /// (-SimplBaseURL http://localhost:8800), the mock Canvas.
    var baseURL: URL {
        if case .canvas(let host) = mode {
            if let dev = AppSession.devBaseURL, dev.host?.lowercased() == host.lowercased() { return dev }
            return URL(string: "https://\(host)/") ?? URL(fileURLWithPath: "/")
        }
        return ScriptBundle.extensionDir
    }
    private var loadStarted = false

    /// The first load, once. SwiftUI can call onAppear more than once for the same view, and a second
    /// load while the first is still being policy-checked fails the first with "Frame load interrupted".
    func loadIfNeeded() {
        guard !loadStarted else { return }
        load()
    }

    func load() {
        loadStarted = true
        switch mode {
        case .canvas(let host):
            // the first launch lands on the guided setup (after Canvas's sign-in, which returns to it): the web
            // interface's own, when the app's chrome is off — with it on, the app shows its own setup over the Dashboard
            let firstLaunch = !UserDefaults.standard.bool(forKey: "setupOpened") && UserDefaults.standard.bool(forKey: "nativeShellOff")
            guard let url = URL(string: firstLaunch ? "/?bcv=setup" : "/", relativeTo: baseURL)?.absoluteURL else { return }
            prepare(host: host) { [weak self] in
                self?.webView.load(URLRequest(url: url))
                UserDefaults.standard.set(true, forKey: "setupOpened")
            }
        case .settings:
            let page = ScriptBundle.extensionDir.appendingPathComponent("options/options.html")
            webView.loadFileURL(page, allowingReadAccessTo: ScriptBundle.extensionDir)
        }
    }

    private var prepared = false
    /// Before the first Canvas load: the saved cookies go back into the cookie store (so the session
    /// survives a relaunch) and the content rules are ready (so the first page is already fast).
    private func prepare(host: String, then: @escaping () -> Void) {
        if prepared {
            then()
            return
        }
        prepared = true
        let group = DispatchGroup()
        group.enter()
        CookieJar.shared.restore(into: webView.configuration.websiteDataStore.httpCookieStore) { group.leave() }
        group.enter()
        ContentRules.prepare(host: host) { group.leave() }
        group.notify(queue: .main, execute: then)
    }

    func reload() {
        if webView.url == nil { load() } else { webView.reload() }
    }

    // MARK: - Navigation

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = navigationAction.request.url else { decisionHandler(.allow); return }
        let scheme = url.scheme?.lowercased() ?? ""
        if !["http", "https", "file", "about", "blob", "data"].contains(scheme) {
            WebController.openSystem(url) // mailto:, tel:, another app
            decisionHandler(.cancel)
            return
        }
        if shellOn, case .canvas(let host) = mode, navigationAction.navigationType == .linkActivated, navigationAction.targetFrame?.isMainFrame ?? false {
            // the app's chrome is on: a link pressed in a page Canvas drew is a screen of the app's stack (a jump within
            // the page stays a jump); another site's link opens over the app, not in place of Canvas
            let here = webView.url
            let sameDocument = here.map { url.host == $0.host && url.path == $0.path && url.query == $0.query && url.fragment != nil } ?? false
            if !sameDocument {
                if url.host?.lowercased() == host.lowercased() { onOpenInShell?(url) } else { openExternally(url) }
                decisionHandler(.cancel)
                return
            }
        }
        if case .canvas(let host) = mode, navigationAction.targetFrame?.isMainFrame ?? true {
            // Canvas's own bundles load only where Canvas draws the page (see ContentRules)
            ContentRules.apply(to: webView.configuration.userContentController, blockCanvasBundles: RenderedRoutes.isRendered(url, host: host, interfaceOn: Bridge.shared.interfaceOn))
            login.willLoad(url) // (the saved sign-in page is covered from its first frame)
        }
        decisionHandler(.allow) // single sign-on hops between hosts stay inside the app
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse, decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
        if navigationResponse.isForMainFrame, !navigationResponse.canShowMIMEType {
            decisionHandler(.download) // a file: fetched with this view's session and opened in the phone's own viewer (FilePreview)
            return
        }
        decisionHandler(.allow)
    }

    func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) {
        FilePreview.shared.take(download, name: navigationResponse.response.suggestedFilename)
    }

    func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) {
        FilePreview.shared.take(download, name: nil)
    }

    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        isLoading = true
        onNavigationStart?()
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        isLoading = false
        pageTitle = webView.title ?? ""
        endRefreshing()
        if case .canvas = mode { login.didLoad(webView.url) }
        onFinish?(webView.url)
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        isLoading = false
        endRefreshing()
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        isLoading = false
        endRefreshing()
        let e = error as NSError
        // Not failures: a navigation that was cancelled or superseded (-999), and WebKit's "Frame load
        // interrupted" (WebKitErrorDomain 102), which it raises when a navigation is replaced by another
        // during its policy check, or when a response is cancelled so a download can open elsewhere.
        if e.code == NSURLErrorCancelled || (e.domain == "WebKitErrorDomain" && e.code == 102) { return }
        // Something else is already loading (a redirect, a retry): let it land rather than covering it.
        if webView.isLoading { return }
        login.pageFailed()
        showUnreachable(error)
    }

    /// WebKit killed the page's process (memory pressure, a crash): bring the page back rather than leaving a blank view.
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        if webView.url != nil { webView.reload() } else { load() }
    }

    private func showUnreachable(_ error: Error) {
        guard case .canvas = mode else { return }
        let message = error.localizedDescription
            .replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
        let html = """
        <!doctype html><meta name="viewport" content="width=device-width,initial-scale=1">
        <style>body{font-family:-apple-system,system-ui;margin:0;padding:48px 28px;color:#1c1c1e;background:#f2f2f7}
        h1{font-size:22px;margin:0 0 8px}p{color:#6e6e73;line-height:1.45}
        button{margin-top:18px;height:46px;padding:0 22px;border:0;border-radius:23px;background:#0a84ff;color:#fff;font:600 15px -apple-system,system-ui}
        @media(prefers-color-scheme:dark){body{background:#000;color:#fff}p{color:#98989d}}</style>
        <h1>Canvas could not be reached</h1><p>\(message)</p>
        <button onclick="location.href='\(baseURL.absoluteString)'">Try again</button>
        """
        webView.loadHTMLString(html, baseURL: nil)
    }

    // MARK: - New windows and dialogs

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if let url = navigationAction.request.url {
            if case .canvas(let host) = mode, url.host?.lowercased() == host.lowercased() {
                webView.load(navigationAction.request) // a "new tab" on Canvas: same view
            } else {
                openExternally(url) // external tools, downloads, other sites
            }
        }
        return nil
    }

    #if os(iOS)
    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping () -> Void) {
        let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in completionHandler() })
        if !present(alert) { completionHandler() }
    }

    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (Bool) -> Void) {
        let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "Cancel", style: .cancel) { _ in completionHandler(false) })
        alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in completionHandler(true) })
        if !present(alert) { completionHandler(false) }
    }

    func webView(_ webView: WKWebView, runJavaScriptTextInputPanelWithPrompt prompt: String, defaultText: String?, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (String?) -> Void) {
        let alert = UIAlertController(title: nil, message: prompt, preferredStyle: .alert)
        alert.addTextField { $0.text = defaultText }
        alert.addAction(UIAlertAction(title: "Cancel", style: .cancel) { _ in completionHandler(nil) })
        alert.addAction(UIAlertAction(title: "OK", style: .default) { [weak alert] _ in completionHandler(alert?.textFields?.first?.text ?? "") })
        if !present(alert) { completionHandler(nil) }
    }

    @discardableResult
    private func present(_ controller: UIViewController) -> Bool {
        guard let top = UIApplication.topViewController() else { return false }
        top.present(controller, animated: true)
        return true
    }

    /// http(s) in an in-app Safari sheet (its own cookies, so tools that need Safari's session still work); anything else to the system.
    func openExternally(_ url: URL) {
        let scheme = url.scheme?.lowercased() ?? ""
        if scheme == "http" || scheme == "https", let top = UIApplication.topViewController() {
            top.present(SFSafariViewController(url: url), animated: true)
        } else {
            UIApplication.shared.open(url)
        }
    }
    #else
    // The Mac's own: the page's alert, confirm and prompt as the window's sheet (an alert of its own when no window is up),
    // a file field's chooser as the open panel, and another site in the student's own browser.

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping () -> Void) {
        let alert = NSAlert()
        alert.messageText = message
        alert.addButton(withTitle: "OK")
        WebController.run(alert, over: webView) { _ in completionHandler() }
    }

    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (Bool) -> Void) {
        let alert = NSAlert()
        alert.messageText = message
        alert.addButton(withTitle: "OK")
        alert.addButton(withTitle: "Cancel")
        WebController.run(alert, over: webView) { completionHandler($0 == .alertFirstButtonReturn) }
    }

    func webView(_ webView: WKWebView, runJavaScriptTextInputPanelWithPrompt prompt: String, defaultText: String?, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (String?) -> Void) {
        let alert = NSAlert()
        alert.messageText = prompt
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 260, height: 24))
        field.stringValue = defaultText ?? ""
        alert.accessoryView = field
        alert.addButton(withTitle: "OK")
        alert.addButton(withTitle: "Cancel")
        WebController.run(alert, over: webView) { completionHandler($0 == .alertFirstButtonReturn ? field.stringValue : nil) }
    }

    func webView(_ webView: WKWebView, runOpenPanelWith parameters: WKOpenPanelParameters, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping ([URL]?) -> Void) {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = parameters.allowsDirectories
        panel.allowsMultipleSelection = parameters.allowsMultipleSelection
        if let window = webView.window {
            panel.beginSheetModal(for: window) { completionHandler($0 == .OK ? panel.urls : nil) }
        } else {
            completionHandler(panel.runModal() == .OK ? panel.urls : nil)
        }
    }

    /// An alert as the sheet of the window the view is in, else on its own; `done` gets the button pressed.
    static func run(_ alert: NSAlert, over view: NSView?, done: @escaping (NSApplication.ModalResponse) -> Void) {
        if let window = view?.window ?? NSApp.keyWindow ?? NSApp.mainWindow {
            alert.beginSheetModal(for: window, completionHandler: done)
        } else {
            done(alert.runModal())
        }
    }

    /// Another site, in the student's own browser (its own session there); anything else to the system.
    func openExternally(_ url: URL) {
        NSWorkspace.shared.open(url)
    }
    #endif
}

#if os(iOS)
extension UIApplication {
    static func topViewController() -> UIViewController? {
        let scenes = shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        let windows = scenes.flatMap { $0.windows }
        let window = windows.first { $0.isKeyWindow } ?? windows.first
        var controller = window?.rootViewController
        while let presented = controller?.presentedViewController { controller = presented }
        return controller
    }
}
#endif

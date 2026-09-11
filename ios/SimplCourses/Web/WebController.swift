import Combine
import Foundation
import SafariServices
import UIKit
import WebKit

/// One web view with the extension injected: the Canvas site (an isolated content world, like an
/// extension's content scripts) or the bundled settings page (its own scripts, so the page world).
/// Also the delegate for navigation policy, new windows and JavaScript dialogs.
final class WebController: NSObject, ObservableObject, WKNavigationDelegate, WKUIDelegate {
    let mode: ScriptBundle.Mode
    let webView: WKWebView
    private let world: WKContentWorld
    @Published private(set) var isLoading = false
    @Published private(set) var pageTitle = ""

    init(mode: ScriptBundle.Mode) {
        self.mode = mode
        switch mode {
        case .settings: world = .page
        case .canvas: world = .defaultClient
        }
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default() // the Canvas session persists across launches, like a browser
        config.allowsInlineMediaPlayback = true
        // Identify as Safari: Canvas serves its full web interface to Safari and a cut-down one to
        // unknown web views, and the extension is written for the full one.
        config.applicationNameForUserAgent = "Version/17.0 Mobile/15E148 Safari/604.1"
        let ucc = WKUserContentController()
        for script in ScriptBundle.userScripts(for: mode, world: world) { ucc.addUserScript(script) }
        ucc.addScriptMessageHandler(Bridge.shared, contentWorld: world, name: Bridge.handlerName)
        config.userContentController = ucc
        webView = WKWebView(frame: .zero, configuration: config)
        super.init()
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.allowsBackForwardNavigationGestures = true
        webView.isOpaque = false
        webView.backgroundColor = .systemBackground
        // The page lays itself out under the status bar and the home indicator using the safe-area
        // insets it reads from CSS (ScriptBundle sets viewport-fit=cover), so WebKit must not add its own.
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        Bridge.shared.register(webView, world: world)
        if case .canvas = mode {
            let refresh = UIRefreshControl()
            refresh.addTarget(self, action: #selector(pullToRefresh(_:)), for: .valueChanged)
            webView.scrollView.refreshControl = refresh
        }
    }

    @objc private func pullToRefresh(_ sender: UIRefreshControl) {
        webView.reload()
    }

    var currentURL: URL? { webView.url }
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
            if let url = URL(string: "https://\(host)/") { webView.load(URLRequest(url: url)) }
        case .settings:
            let page = ScriptBundle.extensionDir.appendingPathComponent("options/options.html")
            webView.loadFileURL(page, allowingReadAccessTo: ScriptBundle.extensionDir)
        }
    }

    func reload() {
        if webView.url == nil { load() } else { webView.reload() }
    }

    // MARK: - Navigation

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = navigationAction.request.url else { decisionHandler(.allow); return }
        let scheme = url.scheme?.lowercased() ?? ""
        if !["http", "https", "file", "about", "blob", "data"].contains(scheme) {
            UIApplication.shared.open(url) // mailto:, tel:, another app
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow) // single sign-on hops between hosts stay inside the app
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse, decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
        if navigationResponse.isForMainFrame, !navigationResponse.canShowMIMEType, let url = navigationResponse.response.url {
            openExternally(url) // a download: Safari can save and share it
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }

    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        isLoading = true
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        isLoading = false
        pageTitle = webView.title ?? ""
        webView.scrollView.refreshControl?.endRefreshing()
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        isLoading = false
        webView.scrollView.refreshControl?.endRefreshing()
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        isLoading = false
        webView.scrollView.refreshControl?.endRefreshing()
        let e = error as NSError
        // Not failures: a navigation that was cancelled or superseded (-999), and WebKit's "Frame load
        // interrupted" (WebKitErrorDomain 102), which it raises when a navigation is replaced by another
        // during its policy check, or when a response is cancelled so a download can open elsewhere.
        if e.code == NSURLErrorCancelled || (e.domain == "WebKitErrorDomain" && e.code == 102) { return }
        // Something else is already loading (a redirect, a retry): let it land rather than covering it.
        if webView.isLoading { return }
        showUnreachable(error)
    }

    /// WebKit killed the page's process (memory pressure, a crash): bring the page back rather than leaving a blank view.
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        if webView.url != nil { webView.reload() } else { load() }
    }

    private func showUnreachable(_ error: Error) {
        guard case .canvas(let host) = mode else { return }
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
        <button onclick="location.href='https://\(host)/'">Try again</button>
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
}

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

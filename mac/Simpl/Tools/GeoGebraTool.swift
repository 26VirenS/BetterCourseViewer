import AppKit
import SwiftUI
import WebKit

// (1.3.112) The Calculator is GeoGebra's: its Calculator Suite (graphing, geometry, 3D, CAS, probability, the
// scientific calculator) in the tool, its scientific calculator in the pin. The page is the extension's
// (extension/tools/geogebra), which loads GeoGebra from geogebra.org into a web view — so it needs the internet, and
// says so with Try again without it.

/// GeoGebra's page in a web view: `compact` the pin's (the scientific calculator); `problem` a command GeoGebra runs as
/// it opens (the screenshot suite's).
struct GeoGebraWeb: NSViewRepresentable {
    var compact = false
    var problem: String?
    @Environment(\.colorScheme) private var scheme
    @ObservedObject private var look = AppearanceStore.shared

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.userContentController.add(WeakHandler(context.coordinator), name: "simplSolver")
        let w = WKWebView(frame: .zero, configuration: config)
        w.setValue(false, forKey: "drawsBackground")
        w.navigationDelegate = context.coordinator
        context.coordinator.look = lookKey
        // (1.3.178) a picture waits for GeoGebra to say it is ready (or could not be loaded) — it comes from geogebra.org
        if UserDefaults.standard.string(forKey: "SimplShotFile") != nil {
            context.coordinator.awaited = true
            Shot.began()
        }
        load(w)
        return w
    }

    func updateNSView(_ w: WKWebView, context: Context) {
        context.coordinator.parent = self
        guard context.coordinator.look != lookKey else { return }
        context.coordinator.look = lookKey
        w.evaluateJavaScript("window.simplSolverTheme && simplSolverTheme('\(dark ? "dark" : "light")', '\(accent)')")
    }

    static func dismantleNSView(_ w: WKWebView, coordinator: Coordinator) {
        w.configuration.userContentController.removeScriptMessageHandler(forName: "simplSolver")
        coordinator.arrived()
    }

    private var dark: Bool { scheme == .dark }
    private var lookKey: String { (dark ? "dark" : "light") + accent }
    /// The accent in force, as #rrggbb.
    private var accent: String {
        let c = (AppearanceStore.accentNow ?? NSColor.controlAccentColor).usingColorSpace(.sRGB) ?? .systemBlue
        return String(format: "#%02x%02x%02x", Int(round(c.redComponent * 255)), Int(round(c.greenComponent * 255)), Int(round(c.blueComponent * 255)))
    }

    /// The page from the extension the app carries, its look in the address's fragment (a file's query is not kept by
    /// every WebKit).
    private func load(_ w: WKWebView) {
        guard let dir = Bundle.main.url(forResource: "extension", withExtension: nil) else {
            NSLog("[Simpl] calculator: the app has no copy of the extension's pages")
            return
        }
        let page = dir.appendingPathComponent("tools/geogebra/index.html")
        var items = [URLQueryItem(name: "theme", value: dark ? "dark" : "light"), URLQueryItem(name: "accent", value: accent), URLQueryItem(name: "os", value: "mac"), URLQueryItem(name: "clear", value: "1")]
        if compact { items.append(URLQueryItem(name: "compact", value: "1")) }
        if let p = problem, !p.isEmpty { items.append(URLQueryItem(name: "q", value: p)) }
        var c = URLComponents()
        c.queryItems = items
        let url = URL(string: page.absoluteString + "#" + (c.percentEncodedQuery ?? "")) ?? page
        w.loadFileURL(url, allowingReadAccessTo: dir)
    }

    final class Coordinator: NSObject, WKScriptMessageHandler, WKNavigationDelegate {
        var parent: GeoGebraWeb
        var look = ""
        /// (1.3.178) A picture is waiting for GeoGebra.
        var awaited = false
        init(_ p: GeoGebraWeb) { parent = p }

        /// GeoGebra here (or gone, or never coming): the picture's wait for it over.
        func arrived() {
            guard awaited else { return }
            awaited = false
            Shot.ended()
        }

        func userContentController(_ u: WKUserContentController, didReceive m: WKScriptMessage) {
            guard let body = m.body as? [String: Any], let kind = body["kind"] as? String else { return }
            switch kind {
            case "ready": NSLog("[Simpl] calculator: GeoGebra is ready"); arrived()
            case "offline": NSLog("[Simpl] calculator: GeoGebra could not be loaded"); arrived()
            default: break
            }
        }

        // (1.3.178) how its page loaded, on the console: the calculator was pictured blank, its page saying neither
        // that GeoGebra was ready nor that it could not be loaded
        func webView(_ w: WKWebView, didFinish n: WKNavigation!) {
            NSLog("[Simpl] calculator: its page loaded")
        }

        func webView(_ w: WKWebView, didFail n: WKNavigation!, withError e: Error) {
            NSLog("[Simpl] calculator: its page failed: %@", e.localizedDescription)
            arrived()
        }

        func webView(_ w: WKWebView, didFailProvisionalNavigation n: WKNavigation!, withError e: Error) {
            NSLog("[Simpl] calculator: its page could not be opened: %@", e.localizedDescription)
            arrived()
        }

        func webViewWebContentProcessDidTerminate(_ w: WKWebView) {
            NSLog("[Simpl] calculator: its page's process ended")
            arrived()
        }

        /// A link GeoGebra shows (its help, its sign-in) opens in the browser, never in the calculator.
        func webView(_ w: WKWebView, decidePolicyFor a: WKNavigationAction, decisionHandler: @escaping @MainActor (WKNavigationActionPolicy) -> Void) {
            if a.navigationType == .linkActivated, a.targetFrame?.isMainFrame != false, let u = a.request.url, u.scheme?.hasPrefix("http") == true {
                NSWorkspace.shared.open(u)
                decisionHandler(.cancel)
                return
            }
            decisionHandler(.allow)
        }
    }

    /// The page's messages, without the content controller keeping the coordinator alive.
    private final class WeakHandler: NSObject, WKScriptMessageHandler {
        weak var target: Coordinator?
        init(_ t: Coordinator) { target = t }
        func userContentController(_ u: WKUserContentController, didReceive m: WKScriptMessage) { target?.userContentController(u, didReceive: m) }
    }
}

/// Calculator: GeoGebra's Calculator Suite, the whole page.
struct CalculatorTool: View {
    // (the screenshot suite's: -SimplSolver "f(x) = x^2 - 2x - 3", a command GeoGebra runs as it opens)
    @State private var problem: String? = UserDefaults.standard.string(forKey: "SimplSolver")

    var body: some View {
        GeoGebraWeb(problem: problem)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

/// The calculator's pin, opened: GeoGebra's scientific calculator; the whole suite one Open Full Screen away.
struct CalculatorCompact: View {
    var openFull: ([String: String]) -> Void = { _ in }

    var body: some View {
        GeoGebraWeb(compact: true)
            .frame(height: 460)
    }
}

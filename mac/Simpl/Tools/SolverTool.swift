import AppKit
import SwiftUI
import WebKit

// (1.3.111) The Calculator, as Mathway's: a problem typed (or built on the keypad) and sent; the ways it can be worked
// offered; the one picked worked out — its answer exact and as a decimal, its steps, its graph. The page and its maths
// are the extension's (extension/tools/solver: the solver itself, its keypad, KaTeX to write the maths), shown in a
// web view with the app's look — light or dark, its accent — and the pin's own narrow version, which carries what was
// typed in it to the whole calculator.

/// The solver's page in a web view: `compact` the pin's; `problem` worked as it opens (a pin's carried over, the
/// screenshot suite's).
struct SolverWeb: NSViewRepresentable {
    var compact = false
    var problem: String?
    var option: String?
    var onText: (String) -> Void = { _ in }
    var openFull: (String, String?) -> Void = { _, _ in }
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
        guard let dir = Bundle.main.url(forResource: "extension", withExtension: nil) else { return }
        let page = dir.appendingPathComponent("tools/solver/index.html")
        var items = [URLQueryItem(name: "theme", value: dark ? "dark" : "light"), URLQueryItem(name: "accent", value: accent), URLQueryItem(name: "os", value: "mac"), URLQueryItem(name: "clear", value: "1")]
        if compact { items.append(URLQueryItem(name: "compact", value: "1")) }
        if let p = problem, !p.isEmpty { items.append(URLQueryItem(name: "q", value: p)) }
        if let o = option, !o.isEmpty { items.append(URLQueryItem(name: "option", value: o)) }
        var c = URLComponents()
        c.queryItems = items
        let url = URL(string: page.absoluteString + "#" + (c.percentEncodedQuery ?? "")) ?? page
        w.loadFileURL(url, allowingReadAccessTo: dir)
    }

    final class Coordinator: NSObject, WKScriptMessageHandler, WKNavigationDelegate {
        var parent: SolverWeb
        var look = ""
        init(_ p: SolverWeb) { parent = p }

        func userContentController(_ u: WKUserContentController, didReceive m: WKScriptMessage) {
            guard let body = m.body as? [String: Any], let kind = body["kind"] as? String else { return }
            let text = body["text"] as? String ?? ""
            switch kind {
            case "text": parent.onText(text)
            case "openFull": parent.openFull(text, body["option"] as? String)
            case "copy": copyToPasteboard(text)
            case "ready": if UserDefaults.standard.string(forKey: "SimplShotFile") != nil { NSLog("[Simpl] calculator: the solver is ready") }
            default: break
            }
        }

        /// A link in an answer (none yet) opens in the browser, never in the calculator.
        func webView(_ w: WKWebView, decidePolicyFor a: WKNavigationAction, decisionHandler: @escaping @MainActor (WKNavigationActionPolicy) -> Void) {
            if a.navigationType == .linkActivated, let u = a.request.url, u.scheme?.hasPrefix("http") == true {
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

/// Calculator: the solver, the whole page.
struct CalculatorTool: View {
    // (the pin's problem carried over, else the screenshot suite's: -SimplSolver "x^2 - x - 1 = 0" -SimplSolverOption solve:x)
    @State private var problem: String? = ToolsCenter.shared.takeInfo("problem") ?? UserDefaults.standard.string(forKey: "SimplSolver")
    @State private var option: String? = ToolsCenter.shared.takeInfo("option") ?? UserDefaults.standard.string(forKey: "SimplSolverOption")

    var body: some View {
        SolverWeb(problem: problem, option: option)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

/// The calculator's pin, opened: the solver narrow, its keypad folded away; what is typed carried to the whole one by
/// Open Full Screen — (1.3.111) and by the page's own Open full screen and Show steps.
struct CalculatorCompact: View {
    var openFull: ([String: String]) -> Void = { _ in }
    @State private var typed = ""

    var body: some View {
        SolverWeb(compact: true, onText: { typed = $0 }, openFull: { text, option in
            var info = ["problem": text]
            if let option { info["option"] = option }
            openFull(info)
        })
        .frame(height: 460)
        .quickCarry(typed.isEmpty ? [:] : ["problem": typed])
    }
}

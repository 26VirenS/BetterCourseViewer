import AppKit
import SwiftUI
import WebKit

// The graphing calculator, as the web's: Desmos, embedded, not reimplemented — their calculator page in a web view of
// its own. Each web view is kept for as long as the app runs, so a graph survives the tool closing or its pin's popover
// folding. Where Desmos cannot load, a plain message and the way out to desmos.com instead of a blank pane. (1.3.83)
// The pin's graph goes on in the whole one when its panel is opened full screen (GraphWeb.carryOver).

@MainActor
final class GraphWeb: ObservableObject {
    static let desmos = URL(string: "https://www.desmos.com/calculator")!
    static let full = GraphWeb()
    static let mini = GraphWeb()

    @Published private(set) var loading = true
    @Published private(set) var failed = false
    private var made: WKWebView?
    private let delegate = GraphWebDelegate()
    /// (1.3.83) What is to be done to the graph once its page has loaded (`whenReady`).
    private var pending: [Pending] = []

    /// A script for Desmos's page, the text it is given (`s`), and what it does, for the console.
    private struct Pending {
        let script: String
        let s: String
        let said: String
    }

    /// The web view, made (and Desmos asked for) the first time it is shown.
    var webView: WKWebView {
        if let made { return made }
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default()
        let w = WKWebView(frame: .zero, configuration: config)
        delegate.owner = self
        w.navigationDelegate = delegate
        w.uiDelegate = delegate
        w.allowsMagnification = true
        made = w
        w.load(URLRequest(url: GraphWeb.desmos))
        return w
    }

    func reload() {
        failed = false
        loading = true
        pending = [] // (an empty graph asked for: nothing put into it)
        webView.load(URLRequest(url: GraphWeb.desmos))
    }

    func finished() {
        loading = false
        failed = false
        let waiting = pending
        pending = []
        for p in waiting { run(p) }
    }

    func couldNotLoad() {
        loading = false
        failed = true
    }

    // MARK: (1.3.83) The pin's graph, carried to the whole one

    /// The pin's graph — its expressions, sliders, tables, notes and view — put into the whole one as the pin's panel
    /// opens full screen (ToolsCenter.openFullScreen), by Desmos's own `Calc.getState` and `Calc.setState` on its page:
    /// the graph begun small goes on large. The whole one's graph before it is a ⌘Z away (`allowUndo`). Nothing is
    /// carried while the pin's Desmos has not loaded, or its graph is empty — the whole one is left as it was.
    static func carryOver() {
        guard let from = mini.made, !mini.loading, !mini.failed else { return }
        Task { @MainActor in
            let read = try? await from.callAsyncJavaScript(GraphWeb.readState, arguments: [:], in: nil, contentWorld: .page)
            guard let state = read as? String, !state.isEmpty else {
                GraphWeb.shotLog("graph: the pin's graph is empty; nothing carried")
                return
            }
            GraphWeb.shotLog("graph: the pin's graph read (\(state.count) characters)")
            GraphWeb.full.whenReady("Calc.setState(JSON.parse(s), { allowUndo: true })", state, said: "the pin's graph put in the whole one")
        }
    }

    /// The graph's state as JSON, or "" when it has nothing in it (one empty expression line, as Desmos starts) or
    /// Desmos's calculator is not there.
    private static let readState = """
    if (!(window.Calc && Calc.getState)) return "";
    const state = Calc.getState();
    const list = (state.expressions && state.expressions.list) || [];
    return list.some(e => e.type !== "expression" || (e.latex || "").trim() !== "") ? JSON.stringify(state) : "";
    """

    /// (the screenshot suite: -SimplPinGraph "y=x^2" draws that in the pin's graph once Desmos is up, for the picture of
    /// the graph opened full screen from its pin to carry)
    func seedForPicture(_ latex: String) {
        whenReady("Calc.setExpression({ id: 'simpl-pin', latex: s })", latex, said: "drew \(latex) for the picture")
    }

    /// A script for Desmos's page, given `s`: run once the page has loaded (now, if it has) and Desmos's calculator is
    /// up on it — it comes a moment after the page — asked for every tenth of a second, ten seconds at most. `said`:
    /// what it does, for the console in a picture's run.
    private func whenReady(_ script: String, _ s: String, said: String) {
        let step = Pending(script: script, s: s, said: said)
        if made != nil, !loading, !failed { run(step) } else { pending.append(step) }
    }

    private func run(_ step: Pending) {
        guard let w = made else { return }
        let body = """
        for (let i = 0; i < 100 && !(window.Calc && Calc.setState); i++) await new Promise(r => setTimeout(r, 100));
        if (!(window.Calc && Calc.setState)) return false;
        \(step.script);
        return true;
        """
        Task { @MainActor in
            let done = (try? await w.callAsyncJavaScript(body, arguments: ["s": step.s], in: nil, contentWorld: .page)) as? Bool
            GraphWeb.shotLog("graph: \(step.said)\(done == true ? "" : " — not done: Desmos's calculator was not there")")
        }
    }

    /// A line on the console in a picture's run (-SimplShotFile), for CI to read what was carried.
    private static func shotLog(_ line: String) {
        guard UserDefaults.standard.string(forKey: "SimplShotFile") != nil else { return }
        NSLog("[Simpl] %@", line)
    }
}

/// Desmos's web view's delegate: whether it loaded, and its links to elsewhere (its help, its sign-in) in the browser.
final class GraphWebDelegate: NSObject, WKNavigationDelegate, WKUIDelegate {
    weak var owner: GraphWeb?

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        let o = owner
        Task { @MainActor in o?.finished() }
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        fail(error)
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        fail(error)
    }

    private func fail(_ error: Error) {
        if (error as NSError).code == NSURLErrorCancelled { return }
        let o = owner
        Task { @MainActor in o?.couldNotLoad() }
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        if navigationAction.navigationType == .linkActivated, let url = navigationAction.request.url, let host = url.host, !host.hasSuffix("desmos.com") {
            NSWorkspace.shared.open(url)
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if let url = navigationAction.request.url { NSWorkspace.shared.open(url) }
        return nil
    }
}

/// A kept web view, shown.
private struct GraphWebView: NSViewRepresentable {
    let web: GraphWeb

    func makeNSView(context: Context) -> NSView {
        let host = NSView()
        let w = web.webView
        w.removeFromSuperview()
        w.frame = host.bounds
        w.autoresizingMask = [.width, .height]
        host.addSubview(w)
        return host
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        let w = web.webView
        if w.superview !== nsView {
            w.removeFromSuperview()
            w.frame = nsView.bounds
            w.autoresizingMask = [.width, .height]
            nsView.addSubview(w)
        }
    }
}

/// Desmos, or why it is not here.
private struct GraphPane: View {
    @ObservedObject var web: GraphWeb

    var body: some View {
        ZStack {
            if web.failed {
                ContentUnavailableView {
                    Label("Desmos could not load", systemImage: "wifi.exclamationmark")
                } description: {
                    Text("Check your connection, or open desmos.com in the browser instead.")
                } actions: {
                    Button("Try Again") { web.reload() }
                    Button("Open desmos.com") { NSWorkspace.shared.open(GraphWeb.desmos) }
                }
            } else {
                GraphWebView(web: web)
                if web.loading {
                    ProgressView().controlSize(.regular)
                }
            }
        }
    }
}

/// Graphing calculator: Desmos, filling the window under the tool's heading.
struct GraphTool: View {
    @ObservedObject private var web = GraphWeb.full

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            GraphPane(web: web)
                .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).strokeBorder(Theme.edge, lineWidth: 1))
            HStack(spacing: 12) {
                Text("Powered by Desmos")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
                Spacer()
                Button {
                    web.reload()
                } label: {
                    Label("New Graph", systemImage: "arrow.counterclockwise")
                }
                .buttonStyle(.link)
                .help("Start again with an empty graph")
                Button {
                    NSWorkspace.shared.open(GraphWeb.desmos)
                } label: {
                    Label("Open in Browser", systemImage: "safari")
                }
                .buttonStyle(.link)
            }
            .font(.sCallout)
        }
        .padding(.horizontal, 40)
        .padding(.top, 4)
        .padding(.bottom, 24)
    }
}

/// The graphing calculator's pin, opened: a small Desmos, portrait, that keeps its graph while folded — (1.3.83) and goes
/// on in the whole one when its panel is opened full screen (GraphWeb.carryOver).
struct GraphCompact: View {
    @ObservedObject private var web = GraphWeb.mini
    /// (the screenshot suite) -SimplPinGraph's drawing asked for, once a launch.
    @MainActor private static var seeded = false

    var body: some View {
        GraphPane(web: web)
            .frame(height: 460)
            .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
            .onAppear {
                guard !Self.seeded, let latex = UserDefaults.standard.string(forKey: "SimplPinGraph"), !latex.isEmpty else { return }
                Self.seeded = true
                web.seedForPicture(latex)
            }
    }
}

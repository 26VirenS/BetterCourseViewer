import AppKit
import SwiftUI
import WebKit

// The graphing calculator, as the web's: Desmos, embedded, not reimplemented — their calculator page in a web view of
// its own. Each web view is kept for as long as the app runs, so a graph survives the tool closing or its pin's popover
// folding. Where Desmos cannot load, a plain message and the way out to desmos.com instead of a blank pane.

@MainActor
final class GraphWeb: ObservableObject {
    static let desmos = URL(string: "https://www.desmos.com/calculator")!
    static let full = GraphWeb()
    static let mini = GraphWeb()

    @Published private(set) var loading = true
    @Published private(set) var failed = false
    private var made: WKWebView?
    private let delegate = GraphWebDelegate()

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
        webView.load(URLRequest(url: GraphWeb.desmos))
    }

    func finished() {
        loading = false
        failed = false
    }

    func couldNotLoad() {
        loading = false
        failed = true
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

/// The graphing calculator's pin, opened: a small Desmos, portrait, that keeps its graph while folded.
struct GraphCompact: View {
    @ObservedObject private var web = GraphWeb.mini

    var body: some View {
        GraphPane(web: web)
            .frame(height: 460)
            .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
    }
}

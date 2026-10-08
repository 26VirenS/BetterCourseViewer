import SwiftUI
import UIKit
import WebKit

/// An external tool to open: an assignment's (or a New Quizzes quiz's), a module item's, a course's own, or a
/// launch URL. The page answers the launch (`toolLaunch`). (1.6) Or, with `page`, a page of Canvas's own the app
/// has no screen for, opened as it is (Engine.openWebScreen).
struct ToolLaunch: Identifiable {
    let id = UUID()
    let title: String
    let args: [String: String]

    static func assignment(course: String, id: String, title: String) -> ToolLaunch {
        ToolLaunch(title: title, args: ["course": course, "assignment": id])
    }

    static func moduleItem(course: String, id: String, title: String) -> ToolLaunch {
        ToolLaunch(title: title, args: ["course": course, "moduleItem": id])
    }

    static func courseTool(course: String, id: String, title: String) -> ToolLaunch {
        ToolLaunch(title: title, args: ["course": course, "tool": id])
    }
}

private struct LaunchAnswer: Decodable {
    var url: String
    var name: String?
    var sessionless: Bool?
}

/// An external tool in a sheet of its own, the whole height of the screen: Canvas's one-time launch opens the
/// tool at the top of its own page (no Canvas frame round it), in a web view with the Canvas session — its
/// alerts, file pickers, sign-in windows and video all work — with Done, reload, back, and Safari.
struct ToolSheet: View {
    let launch: ToolLaunch
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var url: URL?
    @State private var title = ""
    @State private var error: String?
    @StateObject private var browser = ToolBrowser()

    var body: some View {
        NavigationStack {
            Group {
                if let url {
                    ToolWebView(url: url, browser: browser)
                        .ignoresSafeArea(edges: .bottom)
                        .overlay(alignment: .top) {
                            if browser.loading {
                                ProgressView(value: browser.progress).progressViewStyle(.linear).tint(.accentColor)
                            }
                        }
                } else if let error {
                    ContentUnavailableView {
                        Label("The tool could not open", systemImage: "puzzlepiece.extension")
                    } description: {
                        Text(error)
                    } actions: {
                        Button("Try Again") { Task { await start() } }.glassButton()
                    }
                } else {
                    ProgressView("Opening \(launch.title)…").frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
            .navigationTitle(browser.title.isEmpty ? (title.isEmpty ? launch.title : title) : browser.title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Done") { dismiss() } }
                ToolbarItemGroup(placement: .topBarTrailing) {
                    Button { browser.back() } label: { Image(systemName: "chevron.backward") }
                        .disabled(!browser.canGoBack)
                        .accessibilityLabel("Back")
                    Menu {
                        Button { browser.reload() } label: { Label("Reload", systemImage: "arrow.clockwise") }
                        if let u = browser.current ?? url {
                            Button { engine.web.openExternally(u) } label: { Label("Open in Safari", systemImage: "safari") }
                            ShareLink(item: u) { Label("Share", systemImage: "square.and.arrow.up") }
                        }
                    } label: {
                        Image(systemName: "ellipsis.circle")
                    }
                    .accessibilityLabel("More")
                }
            }
        }
        .presentationDragIndicator(.visible)
        .interactiveDismissDisabled(browser.canGoBack) // (a swipe in the tool is not a reason to close it)
        .task { await start() }
    }

    private func start() async {
        error = nil
        if let p = launch.args["page"] {
            url = URL(string: p)
            return
        }
        do {
            let a = try await engine.call("toolLaunch", launch.args, as: LaunchAnswer.self)
            guard let u = URL(string: a.url) else { throw EngineError.unreadable }
            if let n = a.name, !n.isEmpty { title = n }
            url = u
        } catch {
            self.error = error.localizedDescription
        }
    }
}

/// The tool's web view's state, for the bar over it.
@MainActor
final class ToolBrowser: ObservableObject {
    @Published var title = ""
    @Published var loading = false
    @Published var progress = 0.0
    @Published var canGoBack = false
    @Published var current: URL?
    weak var webView: WKWebView?
    private var watchers: [NSKeyValueObservation] = []

    func attach(_ v: WKWebView) {
        webView = v
        watchers = [
            v.observe(\.title) { [weak self] w, _ in Task { @MainActor in self?.title = w.title ?? "" } },
            v.observe(\.isLoading) { [weak self] w, _ in Task { @MainActor in self?.loading = w.isLoading } },
            v.observe(\.estimatedProgress) { [weak self] w, _ in Task { @MainActor in self?.progress = w.estimatedProgress } },
            v.observe(\.canGoBack) { [weak self] w, _ in Task { @MainActor in self?.canGoBack = w.canGoBack } },
            v.observe(\.url) { [weak self] w, _ in Task { @MainActor in self?.current = w.url } },
        ]
    }

    func back() { webView?.goBack() }
    func reload() { webView?.reload() }
}

/// The tool's own web view: the Canvas session (a tool that asks Canvas again finds it signed in), inline video,
/// file pickers, a window a tool opens shown in place, and its alerts as the phone's own.
struct ToolWebView: UIViewRepresentable {
    let url: URL
    let browser: ToolBrowser

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeUIView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default()
        config.allowsInlineMediaPlayback = true
        config.mediaTypesRequiringUserActionForPlayback = []
        config.defaultWebpagePreferences.preferredContentMode = .mobile
        let v = WKWebView(frame: .zero, configuration: config)
        v.allowsBackForwardNavigationGestures = true
        v.navigationDelegate = context.coordinator
        v.uiDelegate = context.coordinator
        v.isInspectable = true
        browser.attach(v)
        v.load(URLRequest(url: url))
        return v
    }

    func updateUIView(_ v: WKWebView, context: Context) {}

    static func dismantleUIView(_ v: WKWebView, coordinator: Coordinator) {
        v.stopLoading()
        v.navigationDelegate = nil
        v.uiDelegate = nil
    }

    final class Coordinator: NSObject, WKNavigationDelegate, WKUIDelegate {
        // a window the tool opens (a sign-in, a resource): shown in place, as a tab would be
        func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
            if navigationAction.targetFrame == nil { webView.load(navigationAction.request) }
            return nil
        }

        func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
            let scheme = navigationAction.request.url?.scheme?.lowercased() ?? ""
            if ["http", "https", "about", "blob", "data"].contains(scheme) || scheme.isEmpty {
                decisionHandler(.allow)
            } else {
                if let u = navigationAction.request.url { UIApplication.shared.open(u) } // (mailto:, tel:, an app's link)
                decisionHandler(.cancel)
            }
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

        private func present(_ c: UIViewController) -> Bool {
            guard let top = UIApplication.topViewController() else { return false }
            top.present(c, animated: true)
            return true
        }
    }
}

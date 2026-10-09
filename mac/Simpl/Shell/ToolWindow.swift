import AppKit
import SwiftUI
import WebKit

private struct LaunchAnswer: Decodable {
    var url: String
    var name: String?
    var sessionless: Bool?
}

/// An external tool (an assignment's, a New Quizzes quiz's, a module item's, a course's own), or a page of Canvas's own
/// the app has no screen for, in a window of its own: Canvas's one-time launch opens the tool on its own page, in a web
/// view with the Canvas session — its alerts, file pickers, sign-in windows and video all work — under the Mac's own
/// toolbar: Back and Forward, Reload, and Open in Browser, Copy Link and Share.
struct ToolWindow: View {
    let launch: ToolLaunch?
    @EnvironmentObject private var model: AppModel
    @StateObject private var browser = ToolBrowser()
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var focus = FocusTimer.shared
    /// (1.2.16) A light tool page drawn dark while the Mac is in dark mode (the moon in the toolbar turns it off).
    @AppStorage(ToolDarkPage.key) private var darkPage = true
    @State private var url: URL?
    @State private var name = ""
    @State private var error: String?

    private var title: String {
        if !browser.title.isEmpty { return browser.title }
        if !name.isEmpty { return name }
        return launch?.title ?? lmsName
    }

    /// The school's site by name (the main window's engine knows which), for a window with no title of its own.
    private var lmsName: String { model.engine?.lmsName ?? "Canvas" }

    var body: some View {
        ZStack(alignment: .top) {
            if let url {
                ToolWebView(url: url, browser: browser)
                    .transition(.opacity)
            } else if let error {
                ContentUnavailableView {
                    Label("The tool could not open", systemImage: "puzzlepiece.extension")
                } description: {
                    Text(error)
                } actions: {
                    Button("Try Again") { Task { await start() } }
                        .keyboardShortcut(.defaultAction)
                }
            } else {
                VStack(spacing: 12) {
                    ProgressView()
                    Text("Opening \(launch?.title ?? lmsName)…").foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .transition(.opacity)
            }
            if browser.loading && url != nil {
                LoadingLine(progress: max(browser.progress, 0.05)) // (1.2.16: thin, flush with the window's top under the toolbar)
                    .transition(.opacity)
            }
        }
        .animation(Motion.gentle, value: url)
        .animation(.easeOut(duration: 0.2), value: browser.loading)
        .frame(minWidth: 640, minHeight: 480)
        .background(Color(nsColor: .windowBackgroundColor))
        .navigationTitle(title)
        .navigationSubtitle(browser.current?.host ?? "")
        .toolbar {
            ToolbarItem(placement: .navigation) {
                ControlGroup {
                    Button { browser.back() } label: { Label("Back", systemImage: "chevron.left") }
                        .disabled(!browser.canGoBack)
                        .help("Back")
                    Button { browser.forward() } label: { Label("Forward", systemImage: "chevron.right") }
                        .disabled(!browser.canGoForward)
                        .help("Forward")
                }
                .controlGroupStyle(.navigation)
            }
            ToolbarItemGroup(placement: .primaryAction) {
                Button { browser.reload() } label: { Label("Reload", systemImage: "arrow.clockwise") }
                    .keyboardShortcut("r")
                    .help("Reload")
                    .disabled(url == nil)
                if let u = browser.current ?? url {
                    ShareLink(item: u) { Label("Share", systemImage: "square.and.arrow.up") }
                        .help("Share")
                    Menu {
                        Button("Open in Browser") { NSWorkspace.shared.open(u) }
                        Button("Copy Link") { copyToPasteboard(u.absoluteString) }
                    } label: {
                        Label("More", systemImage: "ellipsis.circle")
                    }
                    .menuIndicator(.hidden)
                    .help("More")
                }
            }
            // (1.2.16) the tools pinned in the main window, here too
            if let engine = model.engine, !tools.toolbarPins(timerActive: focus.active).isEmpty {
                ToolbarItemGroup(placement: .primaryAction) {
                    ForEach(tools.toolbarPins(timerActive: focus.active)) { kind in
                        PinnedToolButton(kind: kind, engine: engine)
                            .environmentObject(engine)
                    }
                }
            }
            ToolbarItem(placement: .primaryAction) {
                Toggle(isOn: $darkPage) { Label("Dark Page", systemImage: darkPage ? "moon.fill" : "moon") }
                    .toggleStyle(.button)
                    .help(darkPage ? "Light pages are drawn dark in Dark Mode. Click to show them as they are." : "Draw light pages dark in Dark Mode")
            }
            // (1.2.1) a tool that says to allow third-party cookies: the way round it, at the window's top right
            ToolbarItem(placement: .primaryAction) {
                Button { Task { await fixCookies() } } label: { Label("Cookies", systemImage: "checkmark.shield") }
                    .labelStyle(.titleAndIcon)
                    .disabled(url == nil)
                    .help("A tool asks for third-party cookies? This allows them for Simpl and opens the tool on its own page")
            }
        }
        .task(id: model.engine == nil) { await start() }
        .onChange(of: darkPage) { _, on in ToolDarkPage.set(on, in: browser.webView) }
    }

    /// The Cookies button (1.2.1): cross-site cookies allowed for Simpl's web pages, then the tool launched again on
    /// its own page — where its cookies are its own, not a third party's inside Canvas's frame: Canvas's own one-time
    /// launch where it gives one, else Canvas's launch form sent to the window instead of the frame, else the page again.
    private func fixCookies() async {
        CrossSiteCookies.allow()
        guard let web = browser.webView else { return }
        if let here = browser.current ?? url, let args = ToolWindow.launchArgs(for: here), let engine = model.engine,
           let a = try? await engine.call("toolLaunch", args, as: LaunchAnswer.self), a.sessionless == true, let u = URL(string: a.url) {
            web.load(URLRequest(url: u))
            return
        }
        let js = "(() => { const f = document.getElementById('tool_form'); if (!f) return false; f.target = '_self'; f.submit(); return true; })()"
        let sent = ((try? await web.evaluateJavaScript(js)) as? Bool) ?? false
        if !sent { web.reload() }
    }

    /// What Canvas needs to launch the tool an address frames: a course's tool, an assignment's, a module item's.
    static func launchArgs(for u: URL) -> [String: String]? {
        let p = u.path.split(separator: "/").map(String.init)
        guard p.count >= 4, p[0] == "courses", p[1].allSatisfy(\.isNumber) else { return nil }
        let course = p[1]
        if p[2] == "external_tools", p[3].allSatisfy(\.isNumber) { return ["course": course, "tool": p[3]] }
        if p[2] == "assignments", p[3].allSatisfy(\.isNumber) { return ["course": course, "assignment": p[3]] }
        if p.count >= 5, p[2] == "modules", p[3] == "items", p[4].allSatisfy(\.isNumber) { return ["course": course, "moduleItem": p[4]] }
        return nil
    }

    private func start() async {
        guard url == nil, let launch else { return }
        error = nil
        if let p = launch.args["page"] {
            url = URL(string: p)
            return
        }
        guard let engine = model.engine else { return } // (the main window makes it; this window waits for it)
        do {
            let a = try await engine.call("toolLaunch", launch.args, as: LaunchAnswer.self)
            guard let u = URL(string: a.url) else { throw EngineError.unreadable }
            if let n = a.name, !n.isEmpty { name = n }
            url = u
        } catch {
            self.error = error.localizedDescription
        }
    }
}

/// (1.2.16) The page loading: a 2-point line across the window's top, under the toolbar.
private struct LoadingLine: View {
    let progress: Double

    var body: some View {
        GeometryReader { g in
            Rectangle()
                .fill(Color.accentColor)
                .frame(width: g.size.width * min(max(progress, 0), 1), height: 2)
                .animation(.easeOut(duration: 0.2), value: progress)
        }
        .frame(height: 2)
        .accessibilityLabel("Loading")
    }
}

/// (1.2.16) A tool's page in Dark Mode: a page with no dark look of its own (a light background, no dark colour scheme)
/// is drawn dark — its colours turned round, its pictures and video kept as they are; a page that is dark already, or
/// says it has a dark scheme, is left alone. The moon in the tool window's toolbar turns it off (kept for every tool).
enum ToolDarkPage {
    static let key = "SimplToolDarkPage"

    static var script: WKUserScript {
        let on = UserDefaults.standard.object(forKey: key) as? Bool ?? true
        let js = """
        (function () {
          if (window.__simplDarkApply) return;
          window.__simplDark = \(on ? "true" : "false");
          var mq = window.matchMedia('(prefers-color-scheme: dark)');
          var lum = function (c) { var m = (c || '').match(/\\d+(\\.\\d+)?/g); if (!m || m.length < 3) return null; if (m.length > 3 && +m[3] === 0) return null; return (0.2126 * m[0] + 0.7152 * m[1] + 0.0722 * m[2]) / 255; };
          var light = function () {
            var meta = document.querySelector('meta[name="color-scheme"]');
            if (meta && /dark/i.test(meta.content || '')) return false;
            var b = document.body ? lum(getComputedStyle(document.body).backgroundColor) : null;
            var h = lum(getComputedStyle(document.documentElement).backgroundColor);
            var v = b != null ? b : (h != null ? h : 1);
            return v > 0.55;
          };
          window.__simplDarkApply = function () {
            var id = 'simpl-dark-page', s = document.getElementById(id);
            var want = window.__simplDark && mq.matches && document.documentElement && (s ? true : light());
            if (want && !s) {
              s = document.createElement('style'); s.id = id;
              s.textContent = 'html{filter:invert(.92) hue-rotate(180deg)!important;background:#fff!important}img,video,picture,canvas,embed,object,svg image,[style*="background-image"]{filter:invert(1) hue-rotate(180deg)!important}';
              (document.head || document.documentElement).appendChild(s);
            } else if (!want && s) { s.remove(); }
          };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', window.__simplDarkApply, { once: true }); else window.__simplDarkApply();
          window.addEventListener('load', window.__simplDarkApply, { once: true });
          mq.addEventListener && mq.addEventListener('change', function () { var s = document.getElementById('simpl-dark-page'); if (s) s.remove(); window.__simplDarkApply(); });
        })();
        """
        return WKUserScript(source: js, injectionTime: .atDocumentStart, forMainFrameOnly: true)
    }

    /// The moon turned: the page showing follows at once (the next pages read the setting as they load).
    static func set(_ on: Bool, in view: WKWebView?) {
        view?.evaluateJavaScript("window.__simplDark = \(on ? "true" : "false"); var s = document.getElementById('simpl-dark-page'); if (s && !window.__simplDark) s.remove(); window.__simplDarkApply && window.__simplDarkApply();", completionHandler: nil)
    }
}

/// The tool's web view's state, for the toolbar over it.
@MainActor
final class ToolBrowser: ObservableObject {
    @Published var title = ""
    @Published var loading = false
    @Published var progress = 0.0
    @Published var canGoBack = false
    @Published var canGoForward = false
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
            v.observe(\.canGoForward) { [weak self] w, _ in Task { @MainActor in self?.canGoForward = w.canGoForward } },
            v.observe(\.url) { [weak self] w, _ in Task { @MainActor in self?.current = w.url } },
        ]
    }

    func back() { webView?.goBack() }
    func forward() { webView?.goForward() }
    func reload() { webView?.reload() }
}

/// The tool's own web view: the Canvas session (a tool that asks Canvas again finds it signed in), a window the tool
/// opens shown in place, its alerts as the window's sheets, a file field's chooser as the open panel, and a download in
/// Quick Look.
struct ToolWebView: NSViewRepresentable {
    let url: URL
    let browser: ToolBrowser

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> WKWebView {
        CrossSiteCookies.apply() // (1.2.1: allowed once with the Cookies button, allowed from then on)
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default()
        config.mediaTypesRequiringUserActionForPlayback = []
        config.applicationNameForUserAgent = "Version/18.4 Safari/605.1.15"
        config.userContentController.addUserScript(ToolDarkPage.script)
        let v = WKWebView(frame: .zero, configuration: config)
        v.allowsBackForwardNavigationGestures = true
        v.allowsMagnification = true
        v.navigationDelegate = context.coordinator
        v.uiDelegate = context.coordinator
        v.isInspectable = true
        browser.attach(v)
        v.load(URLRequest(url: url))
        return v
    }

    func updateNSView(_ v: WKWebView, context: Context) {}

    static func dismantleNSView(_ v: WKWebView, coordinator: Coordinator) {
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
            if navigationAction.shouldPerformDownload {
                decisionHandler(.download)
            } else if ["http", "https", "about", "blob", "data"].contains(scheme) || scheme.isEmpty {
                decisionHandler(.allow)
            } else {
                if let u = navigationAction.request.url { NSWorkspace.shared.open(u) } // (mailto:, an app's link)
                decisionHandler(.cancel)
            }
        }

        func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse, decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
            // (a file the page cannot show: fetched with the session, into Quick Look)
            decisionHandler(navigationResponse.canShowMIMEType ? .allow : .download)
        }

        func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) {
            FilePreview.shared.take(download, name: navigationAction.request.url?.lastPathComponent)
        }

        func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) {
            FilePreview.shared.take(download, name: navigationResponse.response.suggestedFilename)
        }

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
    }
}

/// (1.2.1) Cross-site cookies for Simpl's web pages: WebKit's tracking prevention — which keeps a tool's cookies out
/// when Canvas shows the tool in a frame — turned off for the app's own web data, as Safari's "Prevent cross-site
/// tracking" turned off would be, for Simpl alone. Allowed with a tool window's Cookies button, and kept.
@MainActor
enum CrossSiteCookies {
    private static let key = "SimplCrossSiteCookies"

    static func allow() {
        UserDefaults.standard.set(true, forKey: key)
        apply()
    }

    static func apply() {
        guard UserDefaults.standard.bool(forKey: key) else { return }
        let store = WKWebsiteDataStore.default()
        let sel = NSSelectorFromString("_setResourceLoadStatisticsEnabled:")
        guard store.responds(to: sel) else { return } // (a WebKit without it: the relaunch on the tool's own page still helps)
        typealias Setter = @convention(c) (AnyObject, Selector, Bool) -> Void
        unsafeBitCast(store.method(for: sel), to: Setter.self)(store, sel, false)
    }
}

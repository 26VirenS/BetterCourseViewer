import AppKit
import SwiftUI
import WebKit

/// Canvas's one-time launch of a tool (the tool window's, and the Macmillan and ALEKS popup's: Screens/LookQuiz.swift).
struct LaunchAnswer: Decodable {
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
    /// (1.3.196) Developer mode's Record button: the page's code, data and a picture saved each time it changes.
    @ObservedObject private var dev = DevMode.shared
    @StateObject private var recorder = PageRecorder()
    /// (the screenshot suite's -SimplRecordShot YES: the recording begun by itself once the tool's page has loaded)
    @State private var recordShot = UserDefaults.standard.bool(forKey: "SimplRecordShot")
    /// (1.3.79) How many pins the toolbar has room for (Tools/PinnedTools.swift).
    @StateObject private var fit = ToolbarFit(searching: false)
    /// (1.2.16) A light tool page drawn dark while the Mac is in dark mode (the moon in the toolbar turns it on and off).
    /// (1.3.79) Off until the moon turns it on: a tool's page is made for light, and in dark it was hard to read.
    @AppStorage(ToolDarkPage.key) private var darkPage = false
    /// (1.3.197) Simpl's look over Macmillan Learning's page, (1.3.198) and ALEKS's (PageLook): each on until the toolbar's
    /// Simpl Look turns it off there.
    @AppStorage(PageLook.macmillan.key) private var macmillanLook = true
    @AppStorage(PageLook.aleks.key) private var aleksLook = true
    /// The window's own appearance: the moon shows only in Dark Mode, where it does something.
    @Environment(\.colorScheme) private var scheme
    @State private var url: URL?
    /// (1.3.104) The school's own page for the tool, opened because its one-time launch could not be had: its launch
    /// form sent on in the window once it loads (ToolWebView.sendToolForm).
    @State private var sendForm: URL?
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
                ToolWebView(url: url, browser: browser, scripts: PageLook.scripts(),
                            messages: ["simplLook": PageLook.Listener()], externalTool: true, sendToolForm: sendForm)
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
        .modifier(UpdateHold()) // (1.2.19: no update relaunches Simpl while a tool is open)
        .navigationTitle(title)
        .navigationSubtitle(recorder.on ? "Recording · \(recorder.steps) saved" : browser.current?.host ?? "")
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
            // (1.3.196) developer mode: Record — each change of the page saved into a folder in Downloads until Stop
            if dev.unlocked && url != nil {
                ToolbarItem(placement: .primaryAction) {
                    Button { recorder.toggle(browser.webView, name: title) } label: {
                        Label(recorder.on ? "Stop Recording" : "Record", systemImage: recorder.on ? "stop.circle.fill" : "record.circle")
                            .foregroundStyle(recorder.on ? Color.red : Color.primary)
                    }
                    .help(recorder.on
                        ? "Stop recording: \(recorder.steps) saved, \(ByteCountFormatter.string(fromByteCount: Int64(recorder.bytes), countStyle: .file)) (developer mode)"
                        : "Record this page: its code, data and a screenshot each time it changes, into a folder in Downloads (developer mode)")
                    .toolbarFit(fit, .own)
                }
            }
            // (1.2.16) the tools pinned in the main window, here too — (tools-6) the rest in the Tools pull-down, (1.3.79)
            // only those past the toolbar's room (ToolbarFit: no search field here, so as many as put nothing into the
            // toolbar's » menu)
            if let engine = model.engine, !tools.toolbarPins(timerActive: focus.active).isEmpty {
                ToolbarItemGroup(placement: .primaryAction) {
                    ForEach(fit.split(timerActive: focus.active).shown) { kind in
                        PinnedToolButton(kind: kind, engine: engine)
                            .environmentObject(engine)
                            .toolbarFit(fit, .pin, kind)
                    }
                    if !fit.split(timerActive: focus.active).more.isEmpty {
                        MorePinsMenu(kinds: fit.split(timerActive: focus.active).more, engine: engine)
                            .environmentObject(engine)
                            .toolbarFit(fit, .more)
                    }
                }
            }
            // (1.3.197) on Macmillan Learning's pages, (1.3.198) and ALEKS's: Simpl's look over the page, or the page as the
            // tool draws it
            if let look = PageLook.applying(to: browser.current ?? url) {
                ToolbarItem(placement: .primaryAction) {
                    Toggle(isOn: lookOn(look)) { Label("Simpl Look", systemImage: "sparkles") }
                        .toggleStyle(.button)
                        .help(lookOn(look).wrappedValue
                            ? "Simpl's look is on: \(look.title)'s page laid out as an Alta question is. Click to show it as \(look.title) draws it."
                            : "Show \(look.title)'s page in Simpl's look, laid out as an Alta question is")
                        .toolbarFit(fit, .own)
                }
            }
            // (courses-33: only in Dark Mode, the one time it does anything — in Light Mode a lit moon over a white page
            // read as "dark mode is on", the loudest control in the bar)
            if scheme == .dark {
                ToolbarItem(placement: .primaryAction) {
                    Toggle(isOn: $darkPage) { Label("Dark Page", systemImage: darkPage ? "moon.fill" : "moon") }
                        .toggleStyle(.button)
                        .help(darkPage ? "Light pages are drawn dark in Dark Mode. Click to show them as they are." : "Draw light pages dark in Dark Mode")
                        .toolbarFit(fit, .own) // (1.3.79: never pushed into the » menu by a pin)
                }
            }
        }
        .task(id: model.engine == nil) { await start() }
        .onChange(of: darkPage) { _, on in ToolDarkPage.set(on, in: browser.webView) }
        .onChange(of: macmillanLook) { _, on in PageLook.macmillan.set(on, in: browser.webView) }
        .onChange(of: aleksLook) { _, on in PageLook.aleks.set(on, in: browser.webView) }
        .onChange(of: dev.unlocked) { _, on in if !on { recorder.stop() } } // (developer mode left: the recording ends)
        .onChange(of: browser.loading) { _, loading in
            guard recordShot, !loading, dev.unlocked, browser.webView != nil else { return }
            recordShot = false
            recorder.start(browser.webView!, name: title)
        }
        .onDisappear { recorder.stop(reveal: false) } // (the window closed: the recording ends where it is)
    }

    /// (1.3.198) The setting of the look on this page's tool.
    private func lookOn(_ look: PageLook) -> Binding<Bool> { look.name == PageLook.aleks.name ? $aleksLook : $macmillanLook }

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
            if a.sessionless == false { sendForm = u }
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
                .fill(Theme.accent)
                .frame(width: g.size.width * min(max(progress, 0), 1), height: 2)
                .animation(.easeOut(duration: 0.2), value: progress)
        }
        .frame(height: 2)
        .accessibilityLabel("Loading")
    }
}

/// (1.2.16) A tool's page in Dark Mode: a page with no dark look of its own (a light background, no dark colour scheme)
/// is drawn dark — its colours turned round, its pictures and video kept as they are; a page that is dark already, or
/// says it has a dark scheme, is left alone. The moon in the tool window's toolbar turns it on and off (kept for every
/// tool). (1.3.79) Off until the moon turns it on: a tool's page is shown light whatever the app's appearance (`look`)
/// — third-party pages are made for light; Canvas's own viewer in the feedback popup keeps it on, as before.
enum ToolDarkPage {
    static let key = "SimplToolDarkPage"

    /// Whether it is on: as the moon was last left, else `fallback` (off for a tool's page, on for Canvas's viewer).
    static func isOn(fallback: Bool) -> Bool {
        UserDefaults.standard.object(forKey: key) as? Bool ?? fallback
    }

    /// (1.3.79) A tool's page light — the web view's own appearance light, so the page's colour scheme is light too, and
    /// its form controls, its scroll bars and the ground under the page — unless Dark Page is on, when it is the
    /// window's (the app's) and the page is drawn dark by the script. The window around it keeps the app's appearance.
    static func look(_ view: WKWebView, dark: Bool) {
        view.appearance = dark ? nil : NSAppearance(named: .aqua)
        view.underPageBackgroundColor = dark ? nil : NSColor.white
    }

    static func script(fallback: Bool) -> WKUserScript {
        let on = isOn(fallback: fallback)
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

    /// The moon turned: the page showing follows at once (the next pages read the setting as they load) — (1.3.79) its
    /// appearance too: the window's while on (the script then draws it dark), light while off.
    static func set(_ on: Bool, in view: WKWebView?) {
        if let view { look(view, dark: on) }
        view?.evaluateJavaScript("window.__simplDark = \(on ? "true" : "false"); var s = document.getElementById('simpl-dark-page'); if (s && !window.__simplDark) s.remove(); window.__simplDarkApply && window.__simplDarkApply();", completionHandler: nil)
    }
}

/// (1.3.197) Simpl's look over a tool's own page, restyled as an Alta question is in the apps — styles only, from the
/// extension the app carries (the Windows app's too): nothing of the page is taken out, and every question is answered as
/// on the tool's own page (the old Macmillan skin, a second page drawn from Macmillan's, was taken out in 1.3.163).
/// Macmillan Learning (extension/tools/macmillan/look.js): a glass rail of questions with the one open marked, a glass
/// bar of its tools, the question's number large over its sheet, and the main button (Submit Answer, Next Question) with
/// the verdict beside it at the sheet's foot. (1.3.198) ALEKS (extension/tools/aleks/look.js): a light glass bar with the
/// topic and its progress, the question on a sheet, its tools in a glass column beside it, and the actions (Explanation,
/// Check, …) on a glass strip under the answer. Each script is put in every frame and draws only on its tool's pages; each
/// look is on until the toolbar's Simpl Look turns it off there (kept, one setting per tool).
@MainActor
struct PageLook {
    /// For the log (and the screenshot suite's checks): "macmillan", "aleks".
    let name: String
    /// As the toolbar's help names the tool.
    let title: String
    let key: String
    let file: String
    /// The script's own globals: the setting put before it, and its switch.
    let flag: String
    let setter: String
    /// (1.3.201) The sites its pages and its launch are on: Macmillan Learning's on *.macmillanlearning.com (its Integration
    /// Hub, Achieve, LaunchPad) and Canvas's launch of it on lmslink.bfwpub.com; ALEKS's on *.aleks.com (the launch on
    /// secure.aleks.com/lti/canvas, the pages on www-awu.aleks.com and the like, its files on static.aleks.com).
    let domains: [String]
    /// (the mock school's stand-in for the tool, on localhost)
    let mock: String

    static let macmillan = PageLook(name: "macmillan", title: "Macmillan", key: "SimplMacmillanLook", file: "tools/macmillan/look.js",
                                    flag: "__simplMMOn", setter: "__simplMMSet", domains: ["macmillanlearning.com", "bfwpub.com"], mock: "/mock-macmillan/")
    static let aleks = PageLook(name: "aleks", title: "ALEKS", key: "SimplAleksLook", file: "tools/aleks/look.js",
                                flag: "__simplAKOn", setter: "__simplAKSet", domains: ["aleks.com"], mock: "/mock-aleks/")
    static let all = [macmillan, aleks]

    /// (read with bool(forKey:), as @AppStorage reads it: a launch's -SimplMacmillanLook NO comes as the string "NO", which
    /// `as? Bool` does not take)
    var isOn: Bool { UserDefaults.standard.object(forKey: key) == nil || UserDefaults.standard.bool(forKey: key) }

    /// A page of the tool's (any of its hosts), or the mock's stand-in for one.
    func applies(to url: URL?) -> Bool {
        guard let url, let host = url.host?.lowercased() else { return false }
        if owns(host: host) { return true }
        return (host == "localhost" || host == "127.0.0.1") && url.path.hasPrefix(mock)
    }

    /// A host of one of its sites ("www-awu.aleks.com", "lmslink.bfwpub.com").
    func owns(host: String) -> Bool {
        let h = host.lowercased()
        return domains.contains { h == $0 || h.hasSuffix("." + $0) }
    }

    static func applying(to url: URL?) -> PageLook? { all.first { $0.applies(to: url) } }

    /// (1.3.201) The look of an assignment's tool, as the engine knows it — Macmillan Learning's (its launch on
    /// lmslink.bfwpub.com or a Macmillan site, Sapling, LaunchPad, or a title that names it) or ALEKS's (its launch on
    /// aleks.com, or a title that names it) — else by the host its tool launches on. (1.3.199 and 1.3.200 asked the
    /// assignment's own address in Canvas, which has no host, so such an assignment went to the tool window instead.)
    static func forAssignment(macmillan: Bool?, aleks: Bool?, host: String?) -> PageLook? {
        if macmillan == true { return .macmillan }
        if aleks == true { return .aleks }
        guard let host, !host.isEmpty else { return nil }
        return all.first { $0.owns(host: host) }
    }

    /// (1.3.199) A look by its name (the popup's launch says which): Macmillan's for a name it does not know.
    static func named(_ name: String) -> PageLook { all.first { $0.name == name } ?? macmillan }

    /// (1.3.199) For the popup's bar: the tool's name in full, and a mark and a colour of its own.
    var fullName: String { name == "aleks" ? "ALEKS" : "Macmillan Learning" }
    var symbol: String { name == "aleks" ? "chart.pie.fill" : "graduationcap.fill" }
    var color: Color { name == "aleks" ? Color.teal : Color.indigo }

    /// The looks' scripts, from the extension the app carries (each read once).
    private static var sources: [String: String] = [:]

    private var source: String? {
        if let s = Self.sources[file] { return s }
        guard let dir = Bundle.main.url(forResource: "extension", withExtension: nil),
              let js = try? String(contentsOf: dir.appendingPathComponent(file), encoding: .utf8) else {
            NSLog("[Simpl] %@ look: the app has no copy of the look's script", name)
            return nil
        }
        Self.sources[file] = js
        return js
    }

    /// In every frame at its start (the look there before the tool's page first shows), told whether it is on —
    /// (1.3.199) and, in the Macmillan and ALEKS popup, that the popup's rail is beside the page (`rail`); (1.3.200) and
    /// that the popup's own front end is over it (`front`: the page clear over the popup's Liquid Glass, its buttons the
    /// popup's). All on its first line, the line `set` knows the look's own script by.
    func script(rail: Bool = false, front: Bool = false) -> WKUserScript? {
        guard let source else { return nil }
        let head = "window.\(flag) = \(isOn ? "true" : "false");" + (rail || front ? " window.__simplLookRail = true;" : "")
            + (front ? " window.__simplLookFront = 'glass';" : "")
        return WKUserScript(source: head + "\n" + source, injectionTime: .atDocumentStart, forMainFrameOnly: false)
    }

    static func scripts(rail: Bool = false, front: Bool = false) -> [WKUserScript] { all.compactMap { $0.script(rail: rail, front: front) } }

    /// Turned on or off: the page showing follows at once (the page passes it on to its frames), and the pages loaded
    /// from now on start as it is — the script put in again with the new setting, the window's other scripts kept.
    func set(_ on: Bool, in view: WKWebView?) {
        NSLog("[Simpl] %@ look: %@", name, on ? "on" : "off")
        guard let view else { return }
        view.evaluateJavaScript("window.\(setter) && window.\(setter)(\(on ? "true" : "false"))", completionHandler: nil)
        let scripts = view.configuration.userContentController
        let mine = { (s: WKUserScript) in s.source.hasPrefix("window.\(flag) = ") }
        let head = scripts.userScripts.first(where: mine).map { String($0.source.prefix(while: { $0 != "\n" })) } ?? ""
        let kept = scripts.userScripts.filter { !mine($0) }
        scripts.removeAllUserScripts()
        for s in kept { scripts.addUserScript(s) }
        if let s = script(rail: head.contains("__simplLookRail"), front: head.contains("__simplLookFront")) { scripts.addUserScript(s) }
    }

    /// What a look found on its page, said once a page (for the log, and the screenshot suite's look_check): Macmillan's
    /// its questions and the one marked, ALEKS's what it found of the page (its "look" names it). (1.3.199: what the
    /// page tells the popup's rail as it changes is the rail's — LookRail — and not logged.)
    nonisolated static func log(_ o: [String: Any]) {
        guard o["rail"] == nil else { return }
        let said = o["said"] as? String ?? "?", host = o["host"] as? String ?? "?"
        if let look = o["look"] as? String {
            NSLog("[Simpl] %@ look: %@ in %@, %@", look, said, host, o["detail"] as? String ?? "")
            return
        }
        let here = o["here"] as? String ?? ""
        NSLog("[Simpl] macmillan look: %@ in %@, %@ questions, %@ marked", said, host, "\(o["rows"] ?? "?")", here.isEmpty ? "none" : here)
    }

    final class Listener: NSObject, WKScriptMessageHandler {
        func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
            guard let o = message.body as? [String: Any] else { return }
            PageLook.log(o)
        }
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
    /// (1.3.10) Scripts of the page's own put in too (the feedback popup's restyling of Canvas's viewer).
    var scripts: [WKUserScript] = []
    /// (1.3.17) What the page's scripts may tell the app, by name (an Alta assignment's report).
    var messages: [String: WKScriptMessageHandler] = [:]
    /// (1.3.79) A tool's page (the tool window's), not Canvas's own: light whatever the app's appearance, until the moon
    /// turns Dark Page on (ToolDarkPage.look).
    var externalTool = false
    /// (1.3.104) The school's own page for a tool whose one-time launch could not be had (an LTI 1.3 tool such as
    /// Smartwork): once it has loaded, Canvas's launch form on it — aimed at a new window, behind "Load … in a new window"
    /// for a tool Canvas opens that way — is sent on in this window, so the tool opens without the extra click.
    var sendToolForm: URL? = nil
    /// (1.3.200) No ground of its own: what is behind the web view (the Macmillan and ALEKS popup's Liquid Glass) shows
    /// wherever the page draws nothing.
    var clear = false

    /// The form sent, in the page: "sent", "framed" (the tool framed in the page, left as it is) or "none" (no form yet).
    /// The same script as the Windows app's (windows/SimplFluent/Shell/WebWindow.cs, SendToolForm).
    static let sendToolFormScript = """
        (function () {
          var f = document.getElementById('tool_form');
          if (!f || !f.action) return 'none';
          if (f.getAttribute('data-simpl-sent')) return 'sent';
          var target = (f.getAttribute('target') || '').toLowerCase();
          if (target && target !== '_blank') return 'framed';
          f.setAttribute('data-simpl-sent', '1');
          f.setAttribute('target', '_self');
          HTMLFormElement.prototype.submit.call(f);
          return 'sent';
        })()
        """

    func makeCoordinator() -> Coordinator {
        let c = Coordinator()
        c.sendToolForm = sendToolForm
        return c
    }

    func makeNSView(context: Context) -> WKWebView {
        CrossSiteCookies.apply() // (1.2.1: allowed once with the Cookies button — gone in 1.3.15 — and kept from then on)
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default()
        config.mediaTypesRequiringUserActionForPlayback = []
        config.applicationNameForUserAgent = "Version/18.4 Safari/605.1.15"
        config.userContentController.addUserScript(ToolDarkPage.script(fallback: !externalTool))
        for script in scripts { config.userContentController.addUserScript(script) }
        for (name, handler) in messages { config.userContentController.add(handler, name: name) }
        let v = WKWebView(frame: .zero, configuration: config)
        if externalTool { ToolDarkPage.look(v, dark: ToolDarkPage.isOn(fallback: false)) }
        if clear {
            v.setValue(false, forKey: "drawsBackground")
            v.underPageBackgroundColor = .clear
        }
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
        v.configuration.userContentController.removeAllScriptMessageHandlers()
        v.navigationDelegate = nil
        v.uiDelegate = nil
    }

    final class Coordinator: NSObject, WKNavigationDelegate, WKUIDelegate {
        /// (1.3.104) The school's page for the tool whose launch form is still to be sent (nil once sent, or for any other).
        var sendToolForm: URL?
        private var formTries = 0

        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            guard let page = sendToolForm, let here = webView.url else { return }
            let trim = CharacterSet(charactersIn: "/")
            guard here.host == page.host, here.path.trimmingCharacters(in: trim) == page.path.trimmingCharacters(in: trim) else {
                // (the page went elsewhere first — the school's sign-in: tried again when it comes back, a few times at most)
                formTries += 1
                if formTries > 4 { sendToolForm = nil }
                return
            }
            sendForm(in: webView)
        }

        private func sendForm(in webView: WKWebView) {
            webView.evaluateJavaScript(ToolWebView.sendToolFormScript) { [weak self, weak webView] result, _ in
                guard let self else { return }
                let said = result as? String ?? "none"
                // (no form on the page yet: Canvas may still be drawing it; looked for again a moment on, a few times)
                if said == "none", self.formTries < 4, let webView {
                    self.formTries += 1
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) { [weak self, weak webView] in
                        guard let self, let webView, self.sendToolForm != nil else { return }
                        self.sendForm(in: webView)
                    }
                    return
                }
                self.sendToolForm = nil
                NSLog("[Simpl] tool window: the school's page for the tool, its launch %@", said == "sent" ? "sent on in this window" : said == "framed" ? "framed in the page as it is" : "not found")
            }
        }

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
/// tracking" turned off would be, for Simpl alone. Allowed with a tool window's Cookies button (1.2.1 to 1.3.14), and kept.
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

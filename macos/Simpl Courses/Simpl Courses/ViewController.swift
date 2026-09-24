//
//  ViewController.swift
//  Simpl Courses
//
//  The settings window. It is the extension's own settings page (options/options.html, inside the
//  extension in this app's bundle) in a web view, with a bridge underneath in place of the browser's
//  extension APIs: `browser.storage` reads and writes the shared store the extension takes its
//  settings from, `browser.runtime.sendMessage` lands here, a link opens in Safari, and the page's
//  App section — the extension's state in Safari, updates, open at login — talks to this controller
//  through the same bridge (Resources/Bridge.js). A change the extension makes (the look switch on
//  a page) reaches the window within a second: the store's file is watched.
//

import Cocoa
import SafariServices
import WebKit

/// Safari knows the extension by the app's own identifier plus ".Extension" — read from this build
/// rather than written down, so a copy built with its own BUNDLE_ID still finds its extension.
let appBundleIdentifier = Bundle.main.bundleIdentifier ?? "com.simplcourses.app"
let extensionBundleIdentifier = "\(appBundleIdentifier).Extension"

class ViewController: NSViewController, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandlerWithReply {

    @IBOutlet var webView: WKWebView!

    private let store = SharedStore()
    private var watcher: Timer?
    private var lastModified: Double = -1
    private var lastRevision: Int = -1
    private var lastPrefsJSON = ""
    private var extensionState: [String: Any] = ["state": "unknown", "detail": ""]
    private var pageReady = false
    private var welcome: WelcomeView?

    override func viewDidLoad() {
        super.viewDidLoad()
        if Self.welcomeDue(store) {
            let w = WelcomeView(frame: view.bounds)
            w.onOpen = { [weak self] in self?.welcomeOpenSafari() }
            view.addSubview(w, positioned: .above, relativeTo: webView)
            welcome = w
        }
        webView.navigationDelegate = self
        webView.uiDelegate = self // (the page's confirm boxes — Reset everything, locked quizzes — need a hand to be shown at all)
        let controller = webView.configuration.userContentController
        controller.addScriptMessageHandler(self, contentWorld: .page, name: "simpl")
        // the bridge goes in before the page's own scripts, so `browser` is there when lib/settings.js looks for it
        if let url = Bundle.main.url(forResource: "Bridge", withExtension: "js"), var source = try? String(contentsOf: url, encoding: .utf8) {
            source = source.replacingOccurrences(of: "__MANIFEST__", with: Self.json(["version": Updater.shared.currentVersion, "name": "Simpl Courses"]))
            controller.addUserScript(WKUserScript(source: source, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        }
        if let page = Self.settingsPageURL() {
            webView.loadFileURL(page, allowingReadAccessTo: Bundle.main.bundleURL)
        } else {
            webView.loadHTMLString("<p style='font: 14px -apple-system; padding: 24px'>The extension's settings page was not found inside the app.</p>", baseURL: nil)
        }
        Updater.shared.onChange = { [weak self] _ in self?.pushState() }
        NotificationCenter.default.addObserver(self, selector: #selector(appBecameActive), name: NSApplication.didBecomeActiveNotification, object: nil)
        watcher = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in self?.watchStore() }
    }

    override func viewDidAppear() {
        super.viewDidAppear()
        if let window = view.window {
            window.title = "Simpl Courses"
            window.styleMask.insert(.resizable)
            window.minSize = NSSize(width: 760, height: 520)
            if window.frame.width < 900 {
                window.setContentSize(NSSize(width: 960, height: 660))
                window.center()
            }
        }
        refreshExtensionState()
        if UserDefaults.standard.bool(forKey: "openSafariOnLaunch") {
            // the first screen's Open Safari, carried over the move to the Applications folder
            UserDefaults.standard.removeObject(forKey: "openSafariOnLaunch")
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { [weak self] in self?.openSafariSettings { _, _ in } }
        }
    }

    // ---- the first screen -------------------------------------------------------------------------

    /// Once, on the first launch — not on a Mac whose setup is already done (an update arriving).
    static func welcomeDue(_ store: SharedStore) -> Bool {
        return !UserDefaults.standard.bool(forKey: "welcomed") && !store.read().setupDone
    }

    /// Open Safari, from the first screen. The app moves itself home first when it has to (Safari
    /// cannot see the extension until then), and the copy that opens from there carries on.
    private func welcomeOpenSafari() {
        let d = UserDefaults.standard
        d.set(true, forKey: "welcomed")
        if Placement.needsMove {
            d.set(true, forKey: "openSafariOnLaunch")
            if (try? Placement.moveToApplications()) != nil { return } // this copy is quitting; the new one opens Safari
            d.removeObject(forKey: "openSafariOnLaunch")
        }
        dismissWelcome()
        openSafariSettings { _, _ in }
    }

    private func dismissWelcome() {
        guard let w = welcome else { return }
        welcome = nil
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.35
            w.animator().alphaValue = 0
        }, completionHandler: { w.removeFromSuperview() })
    }

    /// options/options.html inside the extension, which lives inside this app.
    static func settingsPageURL() -> URL? {
        guard let plugins = Bundle.main.builtInPlugInsURL,
              let items = try? FileManager.default.contentsOfDirectory(at: plugins, includingPropertiesForKeys: nil) else { return nil }
        for item in items where item.pathExtension == "appex" {
            let page = item.appendingPathComponent("Contents/Resources/options/options.html")
            if FileManager.default.fileExists(atPath: page.path) { return page }
        }
        return nil
    }

    @objc private func appBecameActive() {
        refreshExtensionState() // coming back from Safari (the switch just ticked): read it again
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        pageReady = true
        pushState()
    }

    // ---- Safari's word on the extension ------------------------------------------------------------

    /// on / off / missing. "missing" is Safari saying it has never heard of this extension, which is
    /// the state worth explaining: the page says why (nearly always where the app is) and what puts it back.
    private func refreshExtensionState() {
        SFSafariExtensionManager.getStateOfSafariExtension(withIdentifier: extensionBundleIdentifier) { state, error in
            DispatchQueue.main.async {
                guard let state = state, error == nil else {
                    self.extensionState = ["state": "missing", "detail": Self.missingDetail(error)]
                    self.pushState()
                    return
                }
                self.extensionState = ["state": state.isEnabled ? "on" : "off", "detail": ""]
                self.pushState()
            }
        }
    }

    /// Why Safari would not know the extension. Its own words for it ("SFErrorDomain error 1") say
    /// nothing; where the app is says nearly everything.
    static func missingDetail(_ error: Error?) -> String {
        if Placement.isTranslocated {
            return "macOS is running this copy from a temporary place, so Safari cannot see the extension inside it. Move the app to the Applications folder."
        }
        if !Placement.isInApplications {
            return "Safari cannot find the extension. Move the app to the Applications folder and open it again."
        }
        return "Safari has not registered the extension yet. Open Safari once, then come back here."
    }

    private func openSafariSettings(_ reply: @escaping (Any?, String?) -> Void) {
        SFSafariApplication.showPreferencesForExtension(withIdentifier: extensionBundleIdentifier) { error in
            DispatchQueue.main.async {
                if let error = error {
                    // Safari will not show settings for an extension it does not know; bring Safari up at least
                    if let safari = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.apple.Safari") {
                        NSWorkspace.shared.openApplication(at: safari, configuration: NSWorkspace.OpenConfiguration(), completionHandler: nil)
                    }
                    let ns = error as NSError
                    let message = ns.domain == "SFErrorDomain" && ns.code == 1 ? Self.missingDetail(error) : error.localizedDescription
                    reply(["ok": false, "message": message], nil)
                } else {
                    reply(["ok": true], nil)
                }
                self.refreshExtensionState()
            }
        }
    }

    // ---- the page's own dialogs ----------------------------------------------------------------------
    // A web view shows none of alert(), confirm() and prompt() on its own: without these, a confirm
    // answers "no" at once, and Reset everything and Take locked quizzes here did nothing.

    private func sheet(_ message: String, buttons: [String], accessory: NSView? = nil, done: @escaping (NSApplication.ModalResponse) -> Void) {
        let alert = NSAlert()
        alert.messageText = "Simpl Courses"
        alert.informativeText = message
        for title in buttons { alert.addButton(withTitle: title) }
        alert.accessoryView = accessory
        if let window = view.window {
            alert.beginSheetModal(for: window, completionHandler: done)
        } else {
            done(alert.runModal())
        }
    }

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping () -> Void) {
        sheet(message, buttons: ["OK"]) { _ in completionHandler() }
    }

    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (Bool) -> Void) {
        sheet(message, buttons: ["OK", "Cancel"]) { completionHandler($0 == .alertFirstButtonReturn) }
    }

    func webView(_ webView: WKWebView, runJavaScriptTextInputPanelWithPrompt prompt: String, defaultText: String?, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (String?) -> Void) {
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 320, height: 24))
        field.stringValue = defaultText ?? ""
        sheet(prompt, buttons: ["OK", "Cancel"], accessory: field) { completionHandler($0 == .alertFirstButtonReturn ? field.stringValue : nil) }
    }

    // ---- the page's picture of the app ---------------------------------------------------------------

    private var appState: [String: Any] {
        let snap = store.read()
        var d: [String: Any] = [
            "version": Updater.shared.currentVersion,
            "extension": extensionState,
            "update": Updater.shared.report,
            "loginItem": LoginItem.enabled,
            "placement": Placement.report,
            "setupDone": snap.setupDone,
            "storePath": store.fileURL.path,
        ]
        if let site = snap.site { d["site"] = site }
        return d
    }

    private func pushState() {
        guard pageReady else { return }
        webView.evaluateJavaScript("window.__simplApp && window.__simplApp(\(Self.json(appState)))", completionHandler: nil)
    }

    /// The extension's writes (the look switch on a page, its report of the site) show here within a second.
    private func watchStore() {
        let m = store.modified
        guard m != lastModified else { return }
        lastModified = m
        let snap = store.read()
        if snap.revision != lastRevision {
            lastRevision = snap.revision
            if pageReady {
                let change: [String: Any] = ["settings": ["newValue": snap.settings ?? [:]]]
                webView.evaluateJavaScript("window.__simplStorageChanged && window.__simplStorageChanged(\(Self.json(change)))", completionHandler: nil)
            }
        }
        // the site's preferences the extension sent afresh (a snapshot recorded, a record uploaded on the page): the Grades section follows
        let prefsJSON = Self.json(snap.prefs ?? [:])
        if prefsJSON != lastPrefsJSON {
            lastPrefsJSON = prefsJSON
            if pageReady, let host = snap.prefsHost {
                let change: [String: Any] = ["prefs:\(host)": ["newValue": snap.prefs ?? [:]]]
                webView.evaluateJavaScript("window.__simplStorageChanged && window.__simplStorageChanged(\(Self.json(change)))", completionHandler: nil)
            }
        }
        pushState()
    }

    // ---- the bridge ----------------------------------------------------------------------------------

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage, replyHandler: @escaping (Any?, String?) -> Void) {
        guard let body = message.body as? [String: Any], let cmd = body["cmd"] as? String else {
            replyHandler(nil, "Simpl Courses: a message with no command")
            return
        }
        switch cmd {
        case "storage.get":
            replyHandler(storageGet(body["keys"]), nil)
        case "storage.set":
            if let items = body["items"] as? [String: Any], let settings = items["settings"] as? [String: Any] {
                let snap = store.update { $0.settings = settings }
                lastRevision = snap.revision
                lastModified = store.modified
            }
            // the site's preferences written from the Grades section: kept here for the window, and the
            // keys that changed sent to the extension as a command, so its own copy follows
            if let items = body["items"] as? [String: Any] {
                for (key, value) in items where key.hasPrefix("prefs:") {
                    guard let next = value as? [String: Any] else { continue }
                    let host = String(key.dropFirst("prefs:".count))
                    let snap = store.update(bump: false) { s in
                        let before = s.prefsHost == host ? (s.prefs ?? [:]) : [:]
                        var patch: [String: Any] = [:]
                        for (k, v) in next where !Self.same(before[k], v) { patch[k] = v }
                        for (k, _) in before where next[k] == nil { patch[k] = NSNull() }
                        s.prefs = next
                        s.prefsHost = host
                        if !patch.isEmpty { s.commands.append(["id": UUID().uuidString, "type": "setPrefs", "host": host, "patch": patch]) }
                    }
                    lastModified = store.modified
                    lastPrefsJSON = Self.json(snap.prefs ?? [:])
                }
            }
            replyHandler(["ok": true], nil)
        case "storage.remove":
            let keys = (body["keys"] as? [String]) ?? []
            if keys.contains("settings") {
                let snap = store.update { $0.settings = nil }
                lastRevision = snap.revision
                lastModified = store.modified
            }
            replyHandler(["ok": true], nil)
        case "storage.clear":
            // Reset everything: the settings go, and the extension is asked to clear its own storage too
            let snap = store.update { snap in
                snap.settings = nil
                snap.commands.append(["id": UUID().uuidString, "type": "wipe"])
            }
            lastRevision = snap.revision
            lastModified = store.modified
            replyHandler(["ok": true], nil)
        case "runtime.sendMessage":
            replyHandler(runtimeMessage(body["message"] as? [String: Any] ?? [:]), nil)
        case "open":
            if let s = body["url"] as? String, let url = URL(string: s) { openInSafari(url) }
            replyHandler(["ok": true], nil)
        case "app.state":
            replyHandler(appState, nil)
        case "app.openSafariSettings":
            openSafariSettings(replyHandler)
        case "app.moveToApplications":
            do {
                try Placement.moveToApplications()
                replyHandler(["ok": true], nil)
            } catch {
                replyHandler(["ok": false, "message": "\(error.localizedDescription) Drag the app to the Applications folder in the Finder, then open it again."], nil)
            }
        case "app.checkUpdates":
            Updater.shared.check()
            replyHandler(["ok": true], nil)
        case "app.installUpdate":
            Updater.shared.install()
            replyHandler(["ok": true], nil)
        case "app.setAutoUpdate":
            Updater.shared.automatic = body["on"] as? Bool ?? true
            replyHandler(["ok": true], nil)
            pushState()
        case "app.releases": // the Developer section: the versions published, for a rollback
            Updater.shared.releases { replyHandler(["ok": true, "releases": $0], nil) }
        case "app.rollback": // the Developer section: another version put in this one's place
            if let version = body["version"] as? String, let s = body["url"] as? String, let url = URL(string: s), !version.isEmpty {
                Updater.shared.rollback(to: version, url: url, sha256: body["sha256"] as? String)
                replyHandler(["ok": true], nil)
                pushState()
            } else {
                replyHandler(["ok": false, "message": "No version named."], nil)
            }
        case "app.setLoginItem":
            let problem = LoginItem.set(body["on"] as? Bool ?? true)
            replyHandler(["ok": problem == nil, "message": problem ?? ""], nil)
            pushState()
        case "app.openStore":
            NSWorkspace.shared.activateFileViewerSelecting([store.fileURL])
            replyHandler(["ok": true], nil)
        case "file.save":
            replyHandler(saveFile(name: body["name"] as? String ?? "export.txt", text: body["text"] as? String ?? ""), nil)
        case "file.open":
            replyHandler(openFile(types: body["types"] as? [String] ?? []), nil)
        default:
            replyHandler(nil, "Simpl Courses: unknown command \(cmd)")
        }
    }

    /// Two JSON values the same? (the preferences' keys, before and after a write from the window)
    private static func same(_ a: Any?, _ b: Any?) -> Bool {
        guard let a = a else { return b == nil }
        guard let b = b else { return false }
        return json(a) == json(b)
    }

    /// storage.local.get in Chrome's shapes: nothing = everything, a key, a list, or {key: default}.
    /// The store holds the settings; the site and the setup flag are the extension's word, read-only
    /// here; the site's preferences are the extension's copy, written back through a command.
    private func storageGet(_ keys: Any?) -> [String: Any] {
        let snap = store.read()
        var all: [String: Any] = [:]
        if let s = snap.settings { all["settings"] = s }
        if let site = snap.site { all["site:last"] = site }
        if snap.setupDone { all["setup:done"] = true }
        if let prefs = snap.prefs, let host = snap.prefsHost { all["prefs:\(host)"] = prefs }
        guard let keys = keys, !(keys is NSNull) else { return all }
        if let key = keys as? String { return all[key].map { [key: $0] } ?? [:] }
        if let list = keys as? [String] {
            var out: [String: Any] = [:]
            for k in list { if let v = all[k] { out[k] = v } }
            return out
        }
        if let defaults = keys as? [String: Any] {
            var out: [String: Any] = [:]
            for (k, fallback) in defaults { out[k] = all[k] ?? fallback }
            return out
        }
        return [:]
    }

    /// What the settings page asks the extension's background for, answered here instead.
    private func runtimeMessage(_ msg: [String: Any]) -> [String: Any] {
        switch msg["type"] as? String {
        case "registerDomain", "unregisterDomain":
            guard let raw = msg["origin"] as? String, let url = URL(string: raw.hasPrefix("http") ? raw : "https://\(raw)"), let host = url.host else {
                return ["ok": false, "message": "That does not look like a valid URL."]
            }
            let origin = "\(url.scheme ?? "https")://\(host)\(url.port.map { ":\($0)" } ?? "")"
            let adding = msg["type"] as? String == "registerDomain"
            let snap = store.update { snap in
                var settings = snap.settings ?? [:]
                var domains = settings["domains"] as? [String] ?? []
                domains.removeAll { $0 == origin }
                if adding { domains.append(origin) }
                settings["domains"] = domains
                snap.settings = settings
            }
            lastRevision = snap.revision
            lastModified = store.modified
            return ["ok": true, "origin": origin, "message": adding ? "Enabled on \(origin). Safari asks for the site when you open it." : ""]
        case "wipeSiteNotes":
            store.update(bump: false) { $0.commands.append(["id": UUID().uuidString, "type": "wipeSiteNotes"]) }
            lastModified = store.modified
            return ["ok": true, "cleared": 0]
        case "pushSettings", "openOptions":
            return ["ok": true]
        default:
            return ["ok": false, "message": "Not here"]
        }
    }

    /// A link from the page opens in Safari — the browser the extension lives in — not the default browser.
    private func openInSafari(_ url: URL) {
        if let safari = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.apple.Safari") {
            NSWorkspace.shared.open([url], withApplicationAt: safari, configuration: NSWorkspace.OpenConfiguration(), completionHandler: nil)
        } else {
            NSWorkspace.shared.open(url)
        }
    }

    private func saveFile(name: String, text: String) -> [String: Any] {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = name
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return ["ok": false, "cancelled": true] }
        do {
            try text.write(to: url, atomically: true, encoding: .utf8)
            return ["ok": true, "path": url.path]
        } catch {
            return ["ok": false, "message": error.localizedDescription]
        }
    }

    private func openFile(types: [String]) -> [String: Any] {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        if !types.isEmpty { panel.allowedFileTypes = types }
        guard panel.runModal() == .OK, let url = panel.url else { return ["ok": false, "cancelled": true] }
        do {
            let text = try String(contentsOf: url, encoding: .utf8)
            return ["ok": true, "name": url.lastPathComponent, "text": text]
        } catch {
            return ["ok": false, "message": error.localizedDescription]
        }
    }

    /// A value as a JavaScript literal (JSON, which is one).
    private static func json(_ value: Any) -> String {
        guard JSONSerialization.isValidJSONObject(value), let data = try? JSONSerialization.data(withJSONObject: value, options: []), let s = String(data: data, encoding: .utf8) else { return "null" }
        return s
    }
}

/// The first screen: black, the app's icon, "Let’s make Canvas simpler" and Open Safari. It covers
/// the settings until the button is pressed, and is not shown again.
final class WelcomeView: NSView {
    var onOpen: (() -> Void)?

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.backgroundColor = NSColor.black.cgColor
        appearance = NSAppearance(named: .darkAqua)
        autoresizingMask = [.width, .height]

        let icon = NSImageView(image: NSApp.applicationIconImage)
        icon.imageScaling = .scaleProportionallyUpOrDown
        icon.translatesAutoresizingMaskIntoConstraints = false

        let title = NSTextField(labelWithString: "Let’s make Canvas simpler")
        title.font = NSFont.systemFont(ofSize: 34, weight: .bold)
        title.textColor = .white
        title.alignment = .center
        title.translatesAutoresizingMaskIntoConstraints = false

        let button = NSButton(title: "Open Safari", target: self, action: #selector(pressed))
        button.bezelStyle = .rounded
        button.controlSize = .large
        button.keyEquivalent = "\r"
        button.translatesAutoresizingMaskIntoConstraints = false

        let stack = NSStackView(views: [icon, title, button])
        stack.orientation = .vertical
        stack.alignment = .centerX
        stack.spacing = 18
        stack.setCustomSpacing(30, after: title)
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([
            stack.centerXAnchor.constraint(equalTo: centerXAnchor),
            stack.centerYAnchor.constraint(equalTo: centerYAnchor, constant: -16),
            icon.widthAnchor.constraint(equalToConstant: 104),
            icon.heightAnchor.constraint(equalToConstant: 104),
            button.widthAnchor.constraint(greaterThanOrEqualToConstant: 170),
        ])
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not used")
    }

    @objc private func pressed() {
        onOpen?()
    }
}

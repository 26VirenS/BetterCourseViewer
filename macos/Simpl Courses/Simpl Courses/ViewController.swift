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

class ViewController: NSViewController, WKNavigationDelegate, WKScriptMessageHandlerWithReply {

    @IBOutlet var webView: WKWebView!

    private let store = SharedStore()
    private var watcher: Timer?
    private var lastModified: Double = -1
    private var lastRevision: Int = -1
    private var extensionState: [String: Any] = ["state": "unknown", "detail": ""]
    private var pageReady = false

    override func viewDidLoad() {
        super.viewDidLoad()
        webView.navigationDelegate = self
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
    /// the state worth explaining: the page lists what actually puts it back.
    private func refreshExtensionState() {
        SFSafariExtensionManager.getStateOfSafariExtension(withIdentifier: extensionBundleIdentifier) { state, error in
            DispatchQueue.main.async {
                guard let state = state, error == nil else {
                    let detail = error?.localizedDescription ?? "Safari does not list an extension with the identifier \(extensionBundleIdentifier)."
                    self.extensionState = ["state": "missing", "detail": detail]
                    self.pushState()
                    return
                }
                self.extensionState = ["state": state.isEnabled ? "on" : "off", "detail": ""]
                self.pushState()
            }
        }
    }

    private func openSafariSettings(_ reply: @escaping (Any?, String?) -> Void) {
        SFSafariApplication.showPreferencesForExtension(withIdentifier: extensionBundleIdentifier) { error in
            DispatchQueue.main.async {
                if let error = error {
                    reply(["ok": false, "message": error.localizedDescription], nil)
                } else {
                    reply(["ok": true], nil)
                }
                self.refreshExtensionState()
            }
        }
    }

    // ---- the page's picture of the app ---------------------------------------------------------------

    private var appState: [String: Any] {
        let snap = store.read()
        var d: [String: Any] = [
            "version": Updater.shared.currentVersion,
            "extension": extensionState,
            "update": Updater.shared.report,
            "loginItem": LoginItem.enabled,
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

    /// storage.local.get in Chrome's shapes: nothing = everything, a key, a list, or {key: default}.
    /// The store holds the settings; the site and the setup flag are the extension's word, read-only here.
    private func storageGet(_ keys: Any?) -> [String: Any] {
        let snap = store.read()
        var all: [String: Any] = [:]
        if let s = snap.settings { all["settings"] = s }
        if let site = snap.site { all["site:last"] = site }
        if snap.setupDone { all["setup:done"] = true }
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
        case "listDomains":
            let domains = (store.read().settings?["domains"] as? [String]) ?? []
            return ["ok": true, "scripts": domains.map { ["id": $0, "matches": ["\($0)/*"]] }]
        case "wipeSiteNotes":
            store.update(bump: false) { $0.commands.append(["id": UUID().uuidString, "type": "wipeSiteNotes"]) }
            lastModified = store.modified
            return ["ok": true, "cleared": 0]
        case "pushSettings", "openOptions", "setBadge":
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

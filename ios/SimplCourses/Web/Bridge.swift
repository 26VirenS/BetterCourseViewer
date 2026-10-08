import Foundation
#if os(iOS)
import UIKit
#else
import AppKit
#endif
import WebKit

/// The native half of Web/bridge.js: extension storage, "sign out" and "open settings". One instance
/// serves every web view the app opens, so a setting changed on the settings page reaches the Canvas
/// view the same way storage.onChanged does in a browser.
final class Bridge: NSObject, WKScriptMessageHandlerWithReply {
    static let shared = Bridge()
    static let handlerName = "bcv"

    private let store = BridgeStore()
    private var views: [ObjectIdentifier: (view: WeakBox<WKWebView>, world: WKContentWorld)] = [:]
    /// The app's own chrome (Native/Engine.swift): what the page says about it goes there.
    weak var shell: ShellListener?

    /// Keys written before the first page (the simulator suite: -SimplSeed '{"setup:done":true,…}').
    func seed(_ items: [String: Any]) {
        _ = store.set(items)
    }

    /// What the app's settings always are, whatever writes them (a settings import from a computer, an old
    /// command): light or dark is the phone's own setting (1.4: "system"), and Simpl's look is always on
    /// (1.4.5) — off, the app's own screens and the menu that leads back to Settings would be gone.
    static func followingPhone(_ items: [String: Any]) -> [String: Any] {
        guard var s = items["settings"] as? [String: Any], var a = s["appearance"] as? [String: Any] else { return items }
        let mode = a["darkMode"] as? String
        let skin = a["skin"] as? Bool
        let until = (a["offUntil"] as? NSNumber)?.doubleValue ?? 0
        guard (mode != nil && mode != "system") || skin == false || until != 0 else { return items }
        if mode != nil { a["darkMode"] = "system" }
        if skin != nil { a["skin"] = true }
        if a["offUntil"] != nil { a["offUntil"] = 0 }
        s["appearance"] = a
        var out = items
        out["settings"] = s
        return out
    }

    /// At launch: a saved look or appearance from before is brought into line (1.4.3 had a switch that turned
    /// the look off, with no way back to it: the app comes back on by itself).
    func followPhoneAppearance() {
        let s = store.get("settings")
        let fixed = Bridge.followingPhone(s)
        guard let a = fixed["settings"] as? [String: Any], let b = s["settings"] as? [String: Any], !NSDictionary(dictionary: a).isEqual(to: b) else { return }
        _ = store.set(fixed)
    }

    func register(_ webView: WKWebView, world: WKContentWorld) {
        views[ObjectIdentifier(webView)] = (WeakBox(webView), world)
    }

    /// The extension's saved settings (the appearance, among others), for the app's own chrome.
    var settings: [String: Any] {
        store.get("settings")["settings"] as? [String: Any] ?? [:]
    }

    /// appearance.skin: the redesigned interface is on (the default) or the student chose stock Canvas — and a
    /// turn-off for a while is over once its time has come (lib/settings.js lookOn, which every reader asks).
    /// In the app it is always on (followingPhone); still read, for a store written before 1.4.5.
    var interfaceOn: Bool { Bridge.interfaceOn(settings) }
    static func interfaceOn(_ settings: [String: Any], now: Date = Date()) -> Bool {
        let a = settings["appearance"] as? [String: Any] ?? [:]
        if (a["skin"] as? Bool) != false { return true }
        let until = (a["offUntil"] as? NSNumber)?.doubleValue ?? 0
        return until > 0 && now.timeIntervalSince1970 * 1000 >= until
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage, replyHandler: @escaping (Any?, String?) -> Void) {
        guard let body = message.body as? [String: Any], let op = body["op"] as? String else {
            replyHandler(nil, "Simpl Courses: malformed bridge message")
            return
        }
        switch op {
        case "storage.get":
            replyHandler(store.get(body["keys"]), nil)
        case "storage.set":
            let items = Bridge.followingPhone(body["items"] as? [String: Any] ?? [:])
            let changes = store.set(items)
            replyHandler(nil, nil)
            broadcast(changes)
            if let settings = items["settings"] as? [String: Any] { NotificationCenter.default.post(name: .simplSettingsChanged, object: nil, userInfo: settings) }
        case "signOut":
            NotificationCenter.default.post(name: .simplSignOutRequested, object: nil)
            replyHandler(["ok": true], nil)
        case "storage.remove":
            let changes = store.remove(body["keys"] as? [String] ?? [])
            replyHandler(nil, nil)
            broadcast(changes)
        case "storage.clear":
            let changes = store.clear()
            replyHandler(nil, nil)
            broadcast(changes)
        case "openOptions":
            NotificationCenter.default.post(name: .simplOpenSettings, object: nil)
            replyHandler(["ok": true], nil)
        case "log":
            print("[Simpl Courses web]", body["text"] as? String ?? "")
            replyHandler(nil, nil)
        #if os(iOS)
        case "ask":
            // the phone's own alert for a question the page asks (a destructive answer drawn in red)
            let title = body["title"] as? String ?? ""
            let note = body["note"] as? String ?? ""
            let alert = UIAlertController(title: title.isEmpty ? nil : title, message: note.isEmpty ? nil : note, preferredStyle: .alert)
            if let cancel = body["cancelLabel"] as? String, !cancel.isEmpty {
                alert.addAction(UIAlertAction(title: cancel, style: .cancel) { _ in replyHandler(["ok": false], nil) })
            }
            let ok = UIAlertAction(title: body["okLabel"] as? String ?? "OK", style: (body["danger"] as? Bool) == true ? .destructive : .default) { _ in replyHandler(["ok": true], nil) }
            alert.addAction(ok)
            if (body["danger"] as? Bool) != true { alert.preferredAction = ok }
            if !Bridge.present(alert) { replyHandler(nil, "Simpl Courses: nowhere to show the question") }
        case "menu":
            // the phone's own action sheet for a short list (a menu, a picker); the index picked, or -1
            let items = body["items"] as? [[String: Any]] ?? []
            let title = (body["title"] as? String).flatMap { $0.isEmpty ? nil : $0 }
            let sheet = UIAlertController(title: title, message: nil, preferredStyle: .actionSheet)
            for (i, item) in items.enumerated() {
                var label = item["label"] as? String ?? ""
                if let sub = item["sub"] as? String, !sub.isEmpty { label += " · \(sub)" }
                if (item["active"] as? Bool) == true { label = "✓ " + label }
                sheet.addAction(UIAlertAction(title: label, style: (item["danger"] as? Bool) == true ? .destructive : .default) { _ in replyHandler(["index": i], nil) })
            }
            sheet.addAction(UIAlertAction(title: "Cancel", style: .cancel) { _ in replyHandler(["index": -1], nil) })
            if let pop = sheet.popoverPresentationController, let view = message.webView {
                // (an iPad shows it as a popover, from the control that asked)
                pop.sourceView = view
                if let r = body["rect"] as? [String: Any], let x = Bridge.number(r["x"]), let y = Bridge.number(r["y"]) {
                    pop.sourceRect = CGRect(x: x, y: y, width: Bridge.number(r["w"]) ?? 1, height: Bridge.number(r["h"]) ?? 1)
                } else {
                    pop.sourceRect = CGRect(x: view.bounds.midX, y: view.bounds.midY, width: 1, height: 1)
                    pop.permittedArrowDirections = []
                }
            }
            if !Bridge.present(sheet) { replyHandler(nil, "Simpl Courses: nowhere to show the list") }
        #else
        case "ask":
            // the Mac's own alert, as the window's sheet (a destructive answer marked as such)
            let alert = NSAlert()
            alert.messageText = body["title"] as? String ?? ""
            alert.informativeText = body["note"] as? String ?? ""
            let ok = alert.addButton(withTitle: body["okLabel"] as? String ?? "OK")
            if (body["danger"] as? Bool) == true { ok.hasDestructiveAction = true }
            if let cancel = body["cancelLabel"] as? String, !cancel.isEmpty { alert.addButton(withTitle: cancel) }
            WebController.run(alert, over: message.webView) { replyHandler(["ok": $0 == .alertFirstButtonReturn], nil) }
        case "menu":
            // the Mac's own menu, at the control that asked; the index picked, or -1
            let items = body["items"] as? [[String: Any]] ?? []
            guard let view = message.webView else { replyHandler(["index": -1], nil); return }
            var at = NSPoint(x: view.bounds.midX, y: view.bounds.midY)
            if let r = body["rect"] as? [String: Any], let x = Bridge.number(r["x"]), let y = Bridge.number(r["y"]) {
                let h = Bridge.number(r["h"]) ?? 0
                at = NSPoint(x: x, y: view.isFlipped ? y + h : view.bounds.height - y - h)
            }
            let picker = MenuPicker(items: items) { replyHandler(["index": $0], nil) }
            picker.pop(in: view, at: at)
        #endif
        case "previewFile":
            // a file in the phone's own viewer, fetched with the page's own session
            guard let address = body["url"] as? String, let url = URL(string: address), ["http", "https"].contains(url.scheme?.lowercased() ?? ""), let view = message.webView else {
                replyHandler(["ok": false], nil)
                return
            }
            FilePreview.shared.open(url, name: body["name"] as? String ?? url.lastPathComponent, in: view)
            replyHandler(["ok": true], nil)
        case "navState":
            // the phone's own swipe-back: on where the page has somewhere to go back to (with the app's own chrome, the stack's swipe is the one)
            let shellOn = (message.webView?.navigationDelegate as? WebController)?.shellOn ?? false
            message.webView?.allowsBackForwardNavigationGestures = shellOn ? false : (body["canSwipeBack"] as? Bool ?? true)
            replyHandler(nil, nil)
        case "shell.state", "shell.page", "shell.open", "shell.tab", "shell.focus":
            let listener = shell
            MainActor.assumeIsolated { listener?.shellMessage(op, body) } // (the page's messages arrive on the main thread)
            replyHandler(nil, nil)
        case "haptic":
            Haptics.play(body["kind"] as? String ?? "light")
            replyHandler(nil, nil)
        default:
            replyHandler(nil, "Simpl Courses: unknown bridge op \(op)")
        }
    }

    #if os(iOS)
    /// Present over whatever is on screen; false when there is no window to present in.
    @discardableResult
    static func present(_ controller: UIViewController) -> Bool {
        guard let top = UIApplication.topViewController() else { return false }
        top.present(controller, animated: true)
        return true
    }
    #endif

    static func number(_ value: Any?) -> CGFloat? {
        (value as? NSNumber).map { CGFloat($0.doubleValue) }
    }

    /// storage.onChanged for every open web view, the one that wrote included (as browsers do).
    private func broadcast(_ changes: [String: Any]) {
        guard !changes.isEmpty, let js = JS.call("BCVBridge.storageChanged", changes) else { return }
        for (key, entry) in views {
            guard let view = entry.view.value else {
                views[key] = nil
                continue
            }
            view.evaluateJavaScript(js, in: nil, in: entry.world) { _ in }
        }
    }
}

final class WeakBox<T: AnyObject> {
    weak var value: T?
    init(_ value: T) { self.value = value }
}

#if os(macOS)
/// A short list the page offers (a menu, a picker), as the Mac's own pop-up menu: the index picked, or -1 when it
/// closes with nothing picked.
final class MenuPicker: NSObject, NSMenuDelegate {
    private let items: [[String: Any]]
    private let done: (Int) -> Void
    private var picked = -1
    private var keep: MenuPicker?

    init(items: [[String: Any]], done: @escaping (Int) -> Void) {
        self.items = items
        self.done = done
    }

    func pop(in view: NSView, at point: NSPoint) {
        let menu = NSMenu()
        menu.autoenablesItems = false
        menu.delegate = self
        for (i, item) in items.enumerated() {
            var label = item["label"] as? String ?? ""
            if let sub = item["sub"] as? String, !sub.isEmpty { label += " · \(sub)" }
            let entry = NSMenuItem(title: label, action: #selector(choose(_:)), keyEquivalent: "")
            entry.target = self
            entry.tag = i
            entry.state = (item["active"] as? Bool) == true ? .on : .off
            menu.addItem(entry)
        }
        keep = self // (held until the menu closes)
        menu.popUp(positioning: nil, at: point, in: view)
    }

    @objc private func choose(_ sender: NSMenuItem) {
        picked = sender.tag
    }

    func menuDidClose(_ menu: NSMenu) {
        // (the item's action runs after the menu has closed: the answer waits a turn for it)
        DispatchQueue.main.async {
            self.done(self.picked)
            self.keep = nil
        }
    }
}
#endif

import Foundation
import UIKit
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

    func register(_ webView: WKWebView, world: WKContentWorld) {
        views[ObjectIdentifier(webView)] = (WeakBox(webView), world)
    }

    /// The extension's saved settings (the appearance, among others), for the app's own chrome.
    var settings: [String: Any] {
        store.get("settings")["settings"] as? [String: Any] ?? [:]
    }

    /// appearance.skin: the redesigned interface is on (the default) or the student chose stock Canvas.
    var interfaceOn: Bool { Bridge.interfaceOn(settings) }
    static func interfaceOn(_ settings: [String: Any]) -> Bool {
        ((settings["appearance"] as? [String: Any])?["skin"] as? Bool) ?? true
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
            let items = body["items"] as? [String: Any] ?? [:]
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

    /// Present over whatever is on screen; false when there is no window to present in.
    @discardableResult
    static func present(_ controller: UIViewController) -> Bool {
        guard let top = UIApplication.topViewController() else { return false }
        top.present(controller, animated: true)
        return true
    }

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

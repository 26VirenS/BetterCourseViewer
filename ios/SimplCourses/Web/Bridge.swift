import Foundation
import WebKit

/// The native half of Web/bridge.js: extension storage, the fetch proxy for the smart-panel
/// providers, and "open settings". One instance serves every web view the app opens, so a setting
/// changed on the settings page reaches the Canvas view the same way storage.onChanged does in a browser.
final class Bridge: NSObject, WKScriptMessageHandlerWithReply {
    static let shared = Bridge()
    static let handlerName = "bcv"

    private let store = BridgeStore()
    private let fetcher = FetchProxy()
    private var views: [ObjectIdentifier: (view: WeakBox<WKWebView>, world: WKContentWorld)] = [:]

    func register(_ webView: WKWebView, world: WKContentWorld) {
        views[ObjectIdentifier(webView)] = (WeakBox(webView), world)
    }

    /// The extension's saved settings (the appearance, among others), for the app's own chrome.
    var settings: [String: Any] {
        store.get("settings")["settings"] as? [String: Any] ?? [:]
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
        case "fetch":
            guard let id = body["id"] as? Int, let urlString = body["url"] as? String, let url = URL(string: urlString), let webView = message.webView else {
                replyHandler(nil, "Simpl Courses: bad fetch request")
                return
            }
            fetcher.start(id: id, url: url, method: body["method"] as? String ?? "GET", headers: body["headers"] as? [String: String] ?? [:], body: body["body"] as? String, webView: webView, world: message.world)
            replyHandler(nil, nil)
        case "fetch.abort":
            if let id = body["id"] as? Int { fetcher.abort(id: id) }
            replyHandler(nil, nil)
        case "openOptions":
            NotificationCenter.default.post(name: .simplOpenSettings, object: nil)
            replyHandler(["ok": true], nil)
        case "log":
            print("[Simpl Courses web]", body["text"] as? String ?? "")
            replyHandler(nil, nil)
        default:
            replyHandler(nil, "Simpl Courses: unknown bridge op \(op)")
        }
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

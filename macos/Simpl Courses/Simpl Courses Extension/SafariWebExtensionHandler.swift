//
//  SafariWebExtensionHandler.swift
//  Simpl Courses Extension
//
//  The extension's side of the app. Its background script asks here (browser.runtime.sendNativeMessage)
//  on every sync — each Canvas page load, and every few seconds while Safari is up — and gets the
//  settings the app holds when they have moved on from what the extension knows, plus any command
//  the app left (a wipe after Reset everything). It also writes here: the few switches the
//  extension keeps (the look switch on a page, the popup) land in the same store, so the app's
//  window shows them; and it reports which Canvas it last drew and whether the setup has run.
//  "openApp" brings the app's window up, through its simplcourses:// address.
//

import SafariServices
import AppKit
import os.log

class SafariWebExtensionHandler: NSObject, NSExtensionRequestHandling {

    private let store = SharedStore()

    func beginRequest(with context: NSExtensionContext) {
        let request = context.inputItems.first as? NSExtensionItem
        let message: [String: Any]
        if #available(macOS 11.0, *) {
            message = request?.userInfo?[SFExtensionMessageKey] as? [String: Any] ?? [:]
        } else {
            message = request?.userInfo?["message"] as? [String: Any] ?? [:]
        }

        var reply: [String: Any] = ["ok": true]
        switch message["type"] as? String {
        case "getSettings":
            // the extension says what it knows (site, setup) as it asks; that is bookkeeping, not a change to the settings
            let site = message["site"] as? [String: Any]
            let setupDone = message["setupDone"] as? Bool
            // the site's own preferences, sent when they changed since the last ask: the app's window shows Grades from them
            let prefs = message["prefs"] as? [String: Any]
            let prefsHost = message["prefsHost"] as? String
            var snap = store.read()
            if site != nil || setupDone != nil || prefs != nil {
                snap = store.update(bump: false) { s in
                    if let site = site { s.site = site }
                    if let done = setupDone { s.setupDone = done }
                    if let prefs = prefs { s.prefs = prefs; s.prefsHost = prefsHost }
                }
            }
            let known = message["revision"] as? Int ?? -1
            reply["revision"] = snap.revision
            if snap.revision != known, let settings = snap.settings { reply["settings"] = settings }
            reply["hasSettings"] = snap.settings != nil
            reply["commands"] = snap.commands
        case "setSettings":
            if let settings = message["settings"] as? [String: Any] {
                let snap = store.update { $0.settings = settings }
                reply["revision"] = snap.revision
            } else {
                reply["ok"] = false
            }
        case "done":
            if let id = message["id"] as? String {
                let snap = store.update(bump: false) { s in s.commands.removeAll { ($0["id"] as? String) == id } }
                reply["revision"] = snap.revision
            }
        case "openApp":
            if let url = URL(string: "simplcourses://settings") { NSWorkspace.shared.open(url) }
        default:
            os_log(.default, "Simpl Courses: a message the handler does not know: %@", String(describing: message))
            reply = ["ok": false, "echo": message]
        }

        let response = NSExtensionItem()
        if #available(macOS 11.0, *) {
            response.userInfo = [SFExtensionMessageKey: reply]
        } else {
            response.userInfo = ["message": reply]
        }
        context.completeRequest(returningItems: [response], completionHandler: nil)
    }
}

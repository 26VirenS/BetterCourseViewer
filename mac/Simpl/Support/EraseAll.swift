import AppKit
import UserNotifications
import WebKit

/// Reset Everything (1.2.4): all Simpl keeps on this Mac erased — the extension's storage (settings, grade history,
/// goals, every flag), the pages kept for a fast start, the sign-in (the school's cookies and site data, the saved
/// login), the reminders and alerts, every preference of the app's own — and Simpl opened again, as on its first run.
/// Only what Simpl itself wrote is touched.
@MainActor
enum EraseAll {
    static func run(_ engine: Engine?) async {
        // nothing more written while it goes: the page stops, the kept answers stop being kept
        engine?.answers.stop()
        engine?.web.webView.stopLoading()
        engine?.web.webView.loadHTMLString("", baseURL: nil)

        // the reminders and new-activity alerts, and any notification already shown or waiting
        await Reminders.shared.clear()
        Activity.shared.reset()
        let centre = UNUserNotificationCenter.current()
        centre.removeAllPendingNotificationRequests()
        centre.removeAllDeliveredNotifications()

        // the sign-in: the cookies kept in the keychain, the saved login, and every bit of the school's site data
        CookieJar.shared.clear()
        LoginVault.clear()
        await WKWebsiteDataStore.default().removeData(ofTypes: WKWebsiteDataStore.allWebsiteDataTypes(), modifiedSince: .distantPast)
        HTTPCookieStorage.shared.removeCookies(since: .distantPast)
        URLCache.shared.removeAllCachedResponses()

        // what is kept on disk: the extension's storage and the answers kept for a fast start
        Bridge.shared.eraseStorage()
        AnswerCache.eraseAll()

        // every preference of the app's own (the window, the views chosen, the tour, what was asked already)
        let id = Bundle.main.bundleIdentifier ?? "com.simplcourses.mac"
        UserDefaults.standard.removePersistentDomain(forName: id)
        UserDefaults.standard.synchronize()

        relaunch(id)
    }

    /// Simpl quits and opens again once it has: a small helper waits for this process to end, lets go of anything
    /// written on the way out (the preferences, the windows macOS keeps for a relaunch), then opens the app.
    private static func relaunch(_ id: String) {
        let script = """
        while /bin/kill -0 "$1" 2>/dev/null; do /bin/sleep 0.2; done
        /usr/bin/defaults delete "$2" >/dev/null 2>&1
        /bin/rm -rf "$HOME/Library/Saved Application State/$2.savedState"
        /usr/bin/open "$3"
        """
        let helper = Process()
        helper.executableURL = URL(fileURLWithPath: "/bin/sh")
        helper.arguments = ["-c", script, "simpl-relaunch", String(ProcessInfo.processInfo.processIdentifier), id, Bundle.main.bundlePath]
        do {
            try helper.run()
        } catch {
            NSLog("[Simpl] could not start the relaunch helper: \(error)")
        }
        NSApp.terminate(nil)
    }
}

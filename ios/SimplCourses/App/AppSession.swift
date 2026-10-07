import Combine
import Foundation
import SwiftUI
import WebKit

extension Notification.Name {
    /// Posted by the bridge when a page asks for the settings (runtime.openOptionsPage).
    static let simplOpenSettings = Notification.Name("SimplCourses.openSettings")
    static let simplOpenSetup = Notification.Name("SimplCourses.openSetup")
    /// Posted by the bridge when the page asks to sign out (the account sheet).
    static let simplSignOutRequested = Notification.Name("SimplCourses.signOutRequested")
    /// Posted after the Canvas session was cleared, so the browser reloads to the login page.
    static let simplSignedOut = Notification.Name("SimplCourses.signedOut")
    /// Posted by the bridge whenever the extension's settings are written; userInfo is the settings object.
    static let simplSettingsChanged = Notification.Name("SimplCourses.settingsChanged")
    /// Posted when the interface was switched on or off in Settings: the page reloads with Canvas's
    /// bundles allowed or blocked accordingly (see ContentRules).
    static let simplInterfaceToggled = Notification.Name("SimplCourses.interfaceToggled")
}

/// App-level state: which Canvas host the student uses and the settings sheet. Light or dark is the
/// phone's own (1.4): nothing here pins it.
final class AppSession: ObservableObject {
    private static let hostKey = "canvasHost"

    @Published private(set) var host: String? = AppSession.devBaseURL?.host ?? UserDefaults.standard.string(forKey: AppSession.hostKey)

    /// The simulator suite's Canvas (.github/workflows/ios-shots.yml): `-SimplBaseURL http://localhost:8800` at launch.
    static var devBaseURL: URL? {
        guard let s = UserDefaults.standard.string(forKey: "SimplBaseURL"), let url = URL(string: s), url.host != nil else { return nil }
        return url
    }

    @Published var showSettings = false
    private var interfaceOn = Bridge.shared.interfaceOn
    private var bag = Set<AnyCancellable>()

    init() {
        // the simulator suite (-SimplDemo YES): the guided setup and the first-run notes marked done, as for a
        // student who has used the app, before the first page reads them
        if UserDefaults.standard.bool(forKey: "SimplDemo") { Bridge.shared.seed(AppSession.demoSeed()) }
        Bridge.shared.followPhoneAppearance()
        interfaceOn = Bridge.shared.interfaceOn // (a look turned off before 1.4.5 is on again by now)
        NotificationCenter.default.publisher(for: .simplOpenSettings)
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.showSettings = true }
            .store(in: &bag)
        NotificationCenter.default.publisher(for: .simplSignOutRequested)
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.signOut() }
            .store(in: &bag)
        NotificationCenter.default.publisher(for: .simplSettingsChanged)
            .receive(on: RunLoop.main)
            .sink { [weak self] note in
                guard let self = self else { return }
                let settings = note.userInfo as? [String: Any] ?? [:]
                let on = Bridge.interfaceOn(settings)
                if on != self.interfaceOn {
                    self.interfaceOn = on
                    NotificationCenter.default.post(name: .simplInterfaceToggled, object: nil)
                }
            }
            .store(in: &bag)
    }

    /// "catcourses.ucmerced.edu", "https://school.instructure.com/login" … → the bare host.
    static func normalizeHost(_ raw: String) -> String? {
        var s = raw.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if s.isEmpty { return nil }
        if !s.contains("://") { s = "https://" + s }
        guard let url = URL(string: s), let host = url.host, host.contains("."), !host.hasSuffix(".") else { return nil }
        return host
    }

    @discardableResult
    func setHost(_ raw: String) -> Bool {
        guard let h = AppSession.normalizeHost(raw) else { return false }
        UserDefaults.standard.set(h, forKey: AppSession.hostKey)
        host = h
        return true
    }

    func changeSchool() {
        Task { await Reminders.shared.clear() } // (another school's work: none of these reminders are its)
        Task { await Activity.shared.reset() }
        UserDefaults.standard.removeObject(forKey: AppSession.hostKey)
        host = nil
    }

    /// Clears the Canvas session (cookies and site data), like signing out of a browser, and forgets the
    /// saved sign-in (or the app would sign straight back in).
    func signOut(completion: @escaping () -> Void = {}) {
        Task { await Reminders.shared.clear() } // (the next account's work is not this one's)
        Task { await Activity.shared.reset() }
        CookieJar.shared.clear()
        LoginVault.clear()
        WKWebsiteDataStore.default().removeData(ofTypes: WKWebsiteDataStore.allWebsiteDataTypes(), modifiedSince: .distantPast) {
            completion()
            NotificationCenter.default.post(name: .simplSignedOut, object: nil)
        }
    }

    /// The app's own version (1.0 and up), with its build.
    static var version: String {
        let v = (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? ""
        let b = (Bundle.main.infoDictionary?["CFBundleVersion"] as? String) ?? ""
        return b.isEmpty ? v : "\(v) (\(b))"
    }

    /// The version of the extension the app carries (extension/manifest.json), which numbers the interface's releases.
    static var extensionVersion: String {
        (ScriptBundle.manifest()["version"] as? String) ?? ""
    }

    /// What a student who has used the app has in storage: the setup done, the first-run pointers seen, this version's notes read.
    static func demoSeed() -> [String: Any] {
        let background = ScriptBundle.file("background.js")
        var flow = 3
        if let r = background.range(of: #"const SETUP_FLOW = (\d+)"#, options: .regularExpression) {
            flow = Int(background[r].split(separator: " ").last ?? "3") ?? 3
        }
        return ["setup:offered": true, "setup:done": true, "welcome:search": true, "tools:welcomed": true, "setup:flow": flow, "whatsnew:seen": extensionVersion]
    }
}

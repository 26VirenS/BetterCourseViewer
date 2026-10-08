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
    /// Posted by the engine when the page says which platform the school's site is; userInfo["lms"] is "canvas" or "d2l".
    static let simplLMSKnown = Notification.Name("SimplCourses.lmsKnown")
}

/// App-level state: which Canvas host the student uses and the settings sheet. Light or dark is the
/// phone's own (1.4): nothing here pins it.
final class AppSession: ObservableObject {
    private static let hostKey = "canvasHost"

    @Published private(set) var host: String? = AppSession.pickerQuery != nil ? nil : (AppSession.devBaseURL?.host ?? UserDefaults.standard.string(forKey: AppSession.hostKey))

    /// The simulator suite's picture of the school search (`-SimplPicker merced`): the picker, that typed.
    static var pickerQuery: String? { UserDefaults.standard.string(forKey: "SimplPicker") }

    /// The simulator suite's Canvas (.github/workflows/ios-shots.yml): `-SimplBaseURL http://localhost:8800` at launch.
    static var devBaseURL: URL? {
        guard let s = UserDefaults.standard.string(forKey: "SimplBaseURL"), let url = URL(string: s), url.host != nil else { return nil }
        return url
    }

    @Published var showSettings = false
    /// (2.99.22) The school's site: "canvas" or "d2l" (Brightspace), as its page last said — kept per school, so it is known
    /// at the next launch before the page has loaded. Nil for a school whose page has not said yet.
    @Published private(set) var lms: String?
    private var interfaceOn = Bridge.shared.interfaceOn
    private var bag = Set<AnyCancellable>()

    init() {
        lms = host.flatMap { UserDefaults.standard.string(forKey: AppSession.lmsKey($0)) }
        NotificationCenter.default.publisher(for: .simplLMSKnown)
            .receive(on: RunLoop.main)
            .sink { [weak self] note in
                guard let self, let kind = note.userInfo?["lms"] as? String, let h = self.host else { return }
                UserDefaults.standard.set(kind, forKey: AppSession.lmsKey(h))
                if self.lms != kind { self.lms = kind }
            }
            .store(in: &bag)
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

    private static func lmsKey(_ host: String) -> String { "lms:\(host.lowercased())" }

    private static func brightspaceDomain(_ host: String) -> Bool {
        let h = host.lowercased()
        return h.hasSuffix(".brightspace.com") || h.hasSuffix(".d2l.com") || h.hasSuffix(".desire2learn.com")
    }

    /// The school's site is Brightspace: its page has said so, or before it has, the host is one of Brightspace's own.
    var onBrightspace: Bool {
        if let lms { return lms == "d2l" }
        return AppSession.brightspaceDomain(host ?? "")
    }
    /// The name the school's site goes by, for the words that say it ("Sign out of Brightspace on this device?").
    var lmsName: String { onBrightspace ? "Brightspace" : "Canvas" }

    /// The same for a host, where no session is at hand (the page shown when the site cannot be reached): what its page
    /// last said, or before it has, its domain.
    static func lmsName(host: String) -> String {
        if let lms = UserDefaults.standard.string(forKey: lmsKey(host)) { return lms == "d2l" ? "Brightspace" : "Canvas" }
        return brightspaceDomain(host) ? "Brightspace" : "Canvas"
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
        lms = UserDefaults.standard.string(forKey: AppSession.lmsKey(h))
        return true
    }

    func changeSchool() {
        Task { await Reminders.shared.clear() } // (another school's work: none of these reminders are its)
        Task { await Activity.shared.reset() }
        UserDefaults.standard.removeObject(forKey: AppSession.hostKey)
        host = nil
        lms = nil
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

import Combine
import Foundation
import SwiftUI
import WebKit

extension Notification.Name {
    /// Posted by the bridge when a page asks for the settings (runtime.openOptionsPage).
    static let simplOpenSettings = Notification.Name("SimplCourses.openSettings")
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

/// App-level state: which Canvas host the student uses, the settings sheet, and the appearance the
/// web interface chose (so the status bar matches it).
final class AppSession: ObservableObject {
    private static let hostKey = "canvasHost"

    @Published private(set) var host: String? = UserDefaults.standard.string(forKey: AppSession.hostKey)
    @Published var showSettings = false
    @Published private(set) var colorScheme: ColorScheme?
    private var interfaceOn = Bridge.shared.interfaceOn
    private var bag = Set<AnyCancellable>()

    init() {
        colorScheme = AppSession.scheme(from: Bridge.shared.settings)
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
                self.colorScheme = AppSession.scheme(from: settings)
                let on = Bridge.interfaceOn(settings)
                if on != self.interfaceOn {
                    self.interfaceOn = on
                    NotificationCenter.default.post(name: .simplInterfaceToggled, object: nil)
                }
            }
            .store(in: &bag)
    }

    /// The extension's appearance.darkMode: "on" / "off" pin the scheme; "system" follows the device.
    static func scheme(from settings: [String: Any]) -> ColorScheme? {
        let mode = (settings["appearance"] as? [String: Any])?["darkMode"] as? String
        switch mode {
        case "on": return .dark
        case "off": return .light
        default: return nil
        }
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
        UserDefaults.standard.removeObject(forKey: AppSession.hostKey)
        host = nil
    }

    /// Clears the Canvas session (cookies and site data), like signing out of a browser.
    func signOut(completion: @escaping () -> Void = {}) {
        CookieJar.shared.clear()
        WKWebsiteDataStore.default().removeData(ofTypes: WKWebsiteDataStore.allWebsiteDataTypes(), modifiedSince: .distantPast) {
            completion()
            NotificationCenter.default.post(name: .simplSignedOut, object: nil)
        }
    }

    static var version: String {
        (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? ""
    }
}

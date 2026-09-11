import Combine
import Foundation
import WebKit

extension Notification.Name {
    /// Posted by the bridge when a page asks for the settings (the smart panel's "add a key" button).
    static let simplOpenSettings = Notification.Name("SimplCourses.openSettings")
    /// Posted after the Canvas session was cleared, so the browser reloads to the login page.
    static let simplSignedOut = Notification.Name("SimplCourses.signedOut")
}

/// App-level state: which Canvas host the student uses, and the settings sheet.
final class AppSession: ObservableObject {
    private static let hostKey = "canvasHost"

    @Published private(set) var host: String? = UserDefaults.standard.string(forKey: AppSession.hostKey)
    @Published var showSettings = false
    private var bag = Set<AnyCancellable>()

    init() {
        NotificationCenter.default.publisher(for: .simplOpenSettings)
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.showSettings = true }
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
        UserDefaults.standard.removeObject(forKey: AppSession.hostKey)
        host = nil
    }

    /// Clears the Canvas session (cookies and site data), like signing out of a browser.
    func signOut(completion: @escaping () -> Void = {}) {
        WKWebsiteDataStore.default().removeData(ofTypes: WKWebsiteDataStore.allWebsiteDataTypes(), modifiedSince: .distantPast) {
            completion()
            NotificationCenter.default.post(name: .simplSignedOut, object: nil)
        }
    }

    static var version: String {
        (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? ""
    }
}

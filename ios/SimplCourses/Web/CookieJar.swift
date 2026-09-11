import Foundation
import Security
import UIKit
import WebKit

/// Keeps the Canvas session across launches. WebKit holds cookies that carry no expiry date (Canvas's
/// session cookie is one) in memory only, so a launch after the app had been killed came back at the
/// login page. The jar mirrors the web view's cookies into the Keychain whenever they change and when
/// the app leaves the foreground, and puts them back into the cookie store before the first page loads.
/// Signing out empties it.
final class CookieJar: NSObject, WKHTTPCookieStoreObserver {
    static let shared = CookieJar()
    private let service = "com.simplcourses.ios.cookies"
    private let account = "canvas"
    private weak var watched: WKHTTPCookieStore?
    private var pending: DispatchWorkItem?

    private struct Stored: Codable {
        var name: String
        var value: String
        var domain: String
        var path: String
        var expires: Double?
        var secure: Bool
        var httpOnly: Bool
        var sameSite: String?
    }

    /// Mirror this store from now on.
    func watch(_ store: WKHTTPCookieStore) {
        guard watched !== store else { return }
        watched = store
        store.add(self)
        NotificationCenter.default.addObserver(self, selector: #selector(appWillResignActive), name: UIApplication.willResignActiveNotification, object: nil)
    }

    func cookiesDidChange(in cookieStore: WKHTTPCookieStore) {
        pending?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.save(from: cookieStore) }
        pending = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 1, execute: work) // a login sets several at once
    }

    @objc private func appWillResignActive() {
        pending?.cancel()
        if let store = watched { save(from: store) }
    }

    func save(from store: WKHTTPCookieStore) {
        store.getAllCookies { [weak self] cookies in
            guard let self = self else { return }
            let now = Date()
            let list = cookies
                .filter { $0.expiresDate.map { $0 > now } ?? true }
                .map { Stored(name: $0.name, value: $0.value, domain: $0.domain, path: $0.path, expires: $0.expiresDate?.timeIntervalSince1970, secure: $0.isSecure, httpOnly: $0.isHTTPOnly, sameSite: $0.sameSitePolicy?.rawValue) }
            guard let data = try? JSONEncoder().encode(list) else { return }
            self.write(data)
        }
    }

    /// Puts the saved cookies into the store, then calls back (on the main queue).
    func restore(into store: WKHTTPCookieStore, completion: @escaping () -> Void) {
        guard let data = read(), let list = try? JSONDecoder().decode([Stored].self, from: data), !list.isEmpty else {
            completion()
            return
        }
        let now = Date()
        let group = DispatchGroup()
        for s in list {
            if let t = s.expires, Date(timeIntervalSince1970: t) <= now { continue }
            var props: [HTTPCookiePropertyKey: Any] = [.name: s.name, .value: s.value, .domain: s.domain, .path: s.path]
            if let t = s.expires { props[.expires] = Date(timeIntervalSince1970: t) }
            if s.secure { props[.secure] = "TRUE" }
            if s.httpOnly { props[HTTPCookiePropertyKey("HttpOnly")] = "TRUE" }
            if let p = s.sameSite { props[.sameSitePolicy] = HTTPCookieStringPolicy(rawValue: p) }
            guard let cookie = HTTPCookie(properties: props) else { continue }
            group.enter()
            store.setCookie(cookie) { group.leave() }
        }
        group.notify(queue: .main, execute: completion)
    }

    func clear() {
        pending?.cancel()
        SecItemDelete(query() as CFDictionary)
    }

    // MARK: - Keychain

    private func query() -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }

    private func write(_ data: Data) {
        var q = query()
        SecItemDelete(q as CFDictionary)
        q[kSecValueData as String] = data
        q[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        SecItemAdd(q as CFDictionary, nil)
    }

    private func read() -> Data? {
        var q = query()
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        guard SecItemCopyMatching(q as CFDictionary, &item) == errSecSuccess else { return nil }
        return item as? Data
    }
}

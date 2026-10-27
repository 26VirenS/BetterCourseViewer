import AppKit
import AuthenticationServices
import CryptoKit
import EventKit
import Foundation

/// (1.3.184) The kinds of calendar account Sync signs in to, and what each is read and written through:
/// - Apple Calendar — the calendars on this Mac (iCloud's, and any Google or Exchange account added to Calendar), through
///   EventKit: macOS asks once whether Simpl may use them. Nothing to sign in to.
/// - Google Calendar — the student signs in with Google in the browser's sheet (OAuth 2 with PKCE; Google's client for
///   this app, no secret in it); read with calendar.readonly, written only in a calendar Simpl makes there
///   (calendar.app.created).
/// - Outlook — a Microsoft account, personal or a school's (Microsoft 365), the same way (Microsoft Graph,
///   Calendars.ReadWrite).
/// What is read comes in as a linked calendar's events (PulledEvent, background.js calFetch's shape); what is written is
/// Simpl's own calendar (content/app/feed.js events: the work, the student's own tasks and the plans), in a calendar
/// named Simpl in that account.

enum CalendarProvider: String, Codable, CaseIterable, Identifiable {
    case apple, google, microsoft

    var id: String { rawValue }

    var name: String {
        switch self {
        case .apple: return "Apple Calendar"
        case .google: return "Google Calendar"
        case .microsoft: return "Outlook"
        }
    }

    /// What signing in is called on its button.
    var action: String {
        switch self {
        case .apple: return "Use This Mac's Calendars"
        case .google: return "Sign In with Google"
        case .microsoft: return "Sign In with Microsoft"
        }
    }

    var symbol: String {
        switch self {
        case .apple: return "applelogo"
        case .google: return "g.circle.fill"
        case .microsoft: return "envelope.circle.fill"
        }
    }

    var tint: String {
        switch self {
        case .apple: return "#ff3b30"
        case .google: return "#4285f4"
        case .microsoft: return "#0078d4"
        }
    }

    /// Whether this build can sign in to it: Google's and Microsoft's need this app's client, made in their consoles
    /// (CalendarKeys).
    var available: Bool {
        switch self {
        case .apple: return true
        case .google: return OAuthClient.google != nil
        case .microsoft: return OAuthClient.microsoft != nil
        }
    }
}

/// (1.3.184) This app's sign-in clients — public identifiers, not secrets: Google's "iOS" OAuth client for
/// com.simplcourses.mac (Google Cloud ▸ APIs & Services ▸ Credentials), and a Microsoft Entra app registration (a public
/// client with the macOS redirect msauth.com.simplcourses.mac://auth). Empty: that sign-in is not offered yet.
enum CalendarKeys {
    static let google = ""
    static let microsoft = ""
}

enum CalendarError: LocalizedError, Equatable {
    /// The student closed the sign-in sheet: nothing to say.
    case cancelled
    /// The account's sign-in no longer works (revoked, expired): it is signed in to again.
    case signedOut
    /// Access to the Mac's calendars was not given.
    case denied
    /// What was asked for is not there (a calendar or an event deleted in the calendar app).
    case notFound
    case message(String)

    var errorDescription: String? {
        switch self {
        case .cancelled: return "Sign-in was cancelled."
        case .signedOut: return "Sign in again: this account's sign-in has ended."
        case .denied: return "Simpl may not use this Mac's calendars. Allow it in System Settings ▸ Privacy & Security ▸ Calendars."
        case .notFound: return "That calendar is no longer there."
        case .message(let m): return m
        }
    }
}

/// A calendar in an account.
struct AccountCalendar: Codable, Identifiable, Equatable {
    var id: String
    var name: String
    var color: String
    /// Where it lives in the account (Apple Calendar's accounts: iCloud, Google, On My Mac …), else nil.
    var group: String?
    /// Shown on Simpl's calendar (and counted as busy when Simpl finds a free time).
    var on: Bool
}

/// An event read from an account's calendar, as the page keeps a linked calendar's (background.js calFetch): start and
/// end in ms; a whole day by its day ("2026-10-27") and how many days it runs.
struct PulledEvent: Equatable {
    var id: String
    var title: String
    var start: Double
    var end: Double
    var allDay: Bool
    var day: String?
    var days: Int?
    var location: String
    var free: Bool

    var page: [String: Any] {
        var o: [String: Any] = ["id": id, "title": title, "start": start, "end": end, "allDay": allDay, "location": location, "free": free]
        if let day { o["day"] = day }
        if let days { o["days"] = days }
        return o
    }
}

/// An event of Simpl's own calendar (content/app/feed.js events), written into the account's Simpl calendar.
struct PushEvent: Decodable, Equatable {
    var uid: String
    var title: String
    var start: Double?
    var end: Double?
    var allDay: Bool?
    var day: String?
    var days: Int?
    var description: String?
    var url: String?
    var location: String?
    var free: Bool?
    var rrule: String?
    var alarm: Double?

    /// What it says, as a short digest: an event unchanged since it was last written is left alone.
    var signature: String {
        let parts: [String] = [title, start.map { "\($0)" } ?? "", end.map { "\($0)" } ?? "", (allDay ?? false) ? "1" : "0", day ?? "", "\(days ?? 1)",
                               description ?? "", url ?? "", location ?? "", (free ?? false) ? "1" : "0", rrule ?? "", alarm.map { "\($0)" } ?? ""]
        let digest = SHA256.hash(data: Data(parts.joined(separator: "\u{1f}").utf8))
        return digest.prefix(12).map { String(format: "%02x", $0) }.joined()
    }

    var isAllDay: Bool { allDay == true && day != nil }

    /// Its start and its end (a due time, a moment, given a quarter of an hour: a calendar will not take an event with
    /// no length). A whole day: its first day's midnight here, and the midnight after its last.
    var span: (start: Date, end: Date)? {
        if isAllDay, let d = day, let first = PushEvent.dayDate(d) {
            let last = Calendar.current.date(byAdding: .day, value: max(1, days ?? 1), to: first) ?? first.addingTimeInterval(86400)
            return (first, last)
        }
        guard let s = start else { return nil }
        let a = Date(timeIntervalSince1970: s / 1000)
        let b = Date(timeIntervalSince1970: (end ?? s) / 1000)
        return (a, max(b, a.addingTimeInterval(15 * 60)))
    }

    /// The repeat, as a calendar keeps one: how often, and every how many.
    var repeats: (frequency: String, interval: Int)? {
        guard let r = rrule, !r.isEmpty else { return nil }
        var fields: [String: String] = [:]
        for p in r.split(separator: ";") {
            let kv = p.split(separator: "=", maxSplits: 1)
            if kv.count == 2, fields[String(kv[0]).uppercased()] == nil { fields[String(kv[0]).uppercased()] = String(kv[1]).uppercased() }
        }
        guard let f = fields["FREQ"], ["DAILY", "WEEKLY", "MONTHLY"].contains(f) else { return nil }
        return (f, max(1, Int(fields["INTERVAL"] ?? "1") ?? 1))
    }

    static func dayDate(_ day: String) -> Date? {
        let p = day.split(separator: "-").compactMap { Int($0) }
        guard p.count == 3 else { return nil }
        return Calendar.current.date(from: DateComponents(year: p[0], month: p[1], day: p[2]))
    }
}

/// One account's calendars, read and written.
@MainActor
protocol CalendarService {
    func calendars() async throws -> [AccountCalendar]
    func events(in calendar: String, from: Date, to: Date) async throws -> [PulledEvent]
    /// Simpl's calendar in this account: the one made before (`known`), else one made now. Its id.
    func simplCalendar(known: String?) async throws -> String
    /// An event written: put in, or (`existing`) changed. Its id there.
    func write(_ e: PushEvent, into calendar: String, existing: String?) async throws -> String
    func remove(_ id: String, from calendar: String) async throws
}

// MARK: - Signing in (Google, Microsoft)

/// A sign-in client: where its pages and tokens are, what it asks for, and where it comes back to.
struct OAuthClient {
    let provider: CalendarProvider
    let clientID: String
    let authorize: URL
    let token: URL
    let scopes: [String]
    let redirect: String
    let scheme: String
    let extra: [URLQueryItem]

    static var google: OAuthClient? {
        let id = CalendarKeys.google
        guard !id.isEmpty else { return nil }
        // (an iOS client's redirect: its id turned round, as Google gives it)
        let scheme = "com.googleusercontent.apps." + id.replacingOccurrences(of: ".apps.googleusercontent.com", with: "")
        return OAuthClient(provider: .google, clientID: id,
                           authorize: URL(string: "https://accounts.google.com/o/oauth2/v2/auth")!,
                           token: URL(string: "https://oauth2.googleapis.com/token")!,
                           scopes: ["openid", "email", "https://www.googleapis.com/auth/calendar.readonly", "https://www.googleapis.com/auth/calendar.app.created"],
                           redirect: scheme + ":/oauth2redirect", scheme: scheme,
                           extra: [URLQueryItem(name: "access_type", value: "offline"), URLQueryItem(name: "prompt", value: "consent")])
    }

    static var microsoft: OAuthClient? {
        let id = CalendarKeys.microsoft
        guard !id.isEmpty else { return nil }
        let scheme = "msauth.com.simplcourses.mac"
        return OAuthClient(provider: .microsoft, clientID: id,
                           authorize: URL(string: "https://login.microsoftonline.com/common/oauth2/v2.0/authorize")!,
                           token: URL(string: "https://login.microsoftonline.com/common/oauth2/v2.0/token")!,
                           scopes: ["openid", "profile", "email", "offline_access", "User.Read", "Calendars.ReadWrite"],
                           redirect: scheme + "://auth", scheme: scheme,
                           extra: [URLQueryItem(name: "prompt", value: "select_account")])
    }

    static func of(_ p: CalendarProvider) -> OAuthClient? {
        switch p {
        case .apple: return nil
        case .google: return google
        case .microsoft: return microsoft
        }
    }
}

/// An account's tokens: the access token and when it ends, the refresh token that makes the next, and who it is.
struct OAuthTokens: Codable {
    var access: String
    var refresh: String
    var expires: Date
    var email: String

    /// A form sent to the client's token address: the tokens it answers with (a refresh keeps the refresh token it was
    /// made with when no new one comes).
    static func exchange(_ c: OAuthClient, _ form: [String: String], keeping refresh: String = "", email known: String = "") async throws -> OAuthTokens {
        var form = form
        form["client_id"] = c.clientID
        if c.provider == .microsoft { form["scope"] = c.scopes.joined(separator: " ") }
        var r = URLRequest(url: c.token, timeoutInterval: 30)
        r.httpMethod = "POST"
        r.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        r.httpBody = Data(form.map { "\($0.key)=\(formEncode($0.value))" }.sorted().joined(separator: "&").utf8)
        let (data, response) = try await URLSession.shared.data(for: r)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        let j = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] ?? [:]
        guard (200..<300).contains(status), let access = j["access_token"] as? String else {
            if (j["error"] as? String) == "invalid_grant" || status == 401 { throw CalendarError.signedOut }
            throw CalendarError.message((j["error_description"] as? String).map { "The sign-in could not be finished: \($0)" } ?? "The sign-in could not be finished (\(status)).")
        }
        let expires = Date().addingTimeInterval(((j["expires_in"] as? NSNumber)?.doubleValue ?? 3600) - 60)
        let who = (j["id_token"] as? String).flatMap(identity) ?? known
        return OAuthTokens(access: access, refresh: j["refresh_token"] as? String ?? refresh, expires: expires, email: who)
    }

    /// Who signed in, from the ID token's claims (shown only; nothing is trusted from it).
    static func identity(_ jwt: String) -> String? {
        let parts = jwt.split(separator: ".")
        guard parts.count >= 2 else { return nil }
        var b = String(parts[1]).replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        while b.count % 4 != 0 { b += "=" }
        guard let data = Data(base64Encoded: b), let claims = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return nil }
        return (claims["email"] as? String) ?? (claims["preferred_username"] as? String)
    }

    static func formEncode(_ s: String) -> String {
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._~")
        return s.addingPercentEncoding(withAllowedCharacters: allowed) ?? s
    }
}

/// PKCE's verifier and its challenge (RFC 7636, S256).
enum PKCE {
    static func verifier() -> String {
        var bytes = [UInt8](repeating: 0, count: 48)
        _ = SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes)
        return base64URL(Data(bytes))
    }

    static func challenge(_ verifier: String) -> String {
        base64URL(Data(SHA256.hash(data: Data(verifier.utf8))))
    }

    static func base64URL(_ d: Data) -> String {
        d.base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "")
    }
}

/// The browser's sign-in sheet (ASWebAuthenticationSession): the provider's own page, the student's own sign-in kept by
/// the browser; it comes back to the app's redirect with a code, exchanged for the tokens.
@MainActor
final class OAuthSignIn {
    private var session: ASWebAuthenticationSession?
    private var anchor: SheetAnchor?

    /// The window the sheet comes down over (taken when it opens).
    private final class SheetAnchor: NSObject, ASWebAuthenticationPresentationContextProviding {
        let window: NSWindow
        init(_ window: NSWindow) { self.window = window }
        func presentationAnchor(for session: ASWebAuthenticationSession) -> ASPresentationAnchor { window }
    }

    func signIn(_ c: OAuthClient) async throws -> OAuthTokens {
        let verifier = PKCE.verifier(), state = PKCE.verifier()
        var parts = URLComponents(url: c.authorize, resolvingAgainstBaseURL: false)!
        parts.queryItems = [
            URLQueryItem(name: "client_id", value: c.clientID),
            URLQueryItem(name: "redirect_uri", value: c.redirect),
            URLQueryItem(name: "response_type", value: "code"),
            URLQueryItem(name: "scope", value: c.scopes.joined(separator: " ")),
            URLQueryItem(name: "code_challenge", value: PKCE.challenge(verifier)),
            URLQueryItem(name: "code_challenge_method", value: "S256"),
            URLQueryItem(name: "state", value: state),
        ] + c.extra
        guard let start = parts.url else { throw CalendarError.message("The sign-in page could not be made.") }
        let back: URL = try await withCheckedThrowingContinuation { (go: CheckedContinuation<URL, Error>) in
            let s = ASWebAuthenticationSession(url: start, callbackURLScheme: c.scheme) { url, error in
                if let url {
                    go.resume(returning: url)
                } else if let e = error as? ASWebAuthenticationSessionError, e.code == .canceledLogin {
                    go.resume(throwing: CalendarError.cancelled)
                } else {
                    go.resume(throwing: CalendarError.message(error?.localizedDescription ?? "The sign-in did not finish."))
                }
            }
            let anchor = SheetAnchor(NSApp.keyWindow ?? NSApp.windows.first(where: { $0.isVisible }) ?? NSWindow())
            self.anchor = anchor
            s.presentationContextProvider = anchor
            s.prefersEphemeralWebBrowserSession = false
            session = s
            if !s.start() {
                session = nil
                go.resume(throwing: CalendarError.message("The sign-in sheet could not be opened."))
            }
        }
        session = nil
        anchor = nil
        let q = URLComponents(url: back, resolvingAgainstBaseURL: false)?.queryItems ?? []
        let value = { (name: String) -> String? in q.first(where: { $0.name == name })?.value }
        if let e = value("error") { throw e == "access_denied" ? CalendarError.cancelled : CalendarError.message(value("error_description") ?? "The sign-in was refused (\(e)).") }
        guard value("state") == state else { throw CalendarError.message("The sign-in came back without its check. Try again.") }
        guard let code = value("code") else { throw CalendarError.message("The sign-in came back without its code. Try again.") }
        return try await OAuthTokens.exchange(c, ["grant_type": "authorization_code", "code": code, "redirect_uri": c.redirect, "code_verifier": verifier])
    }
}

/// A web API asked with an account's token: its JSON, or why not. A token turned down is refreshed once and asked again.
@MainActor
final class CalendarWeb {
    private let client: OAuthClient
    private var tokens: OAuthTokens
    private let saved: (OAuthTokens) -> Void

    init(client: OAuthClient, tokens: OAuthTokens, saved: @escaping (OAuthTokens) -> Void) {
        self.client = client
        self.tokens = tokens
        self.saved = saved
    }

    private func fresh() async throws -> String {
        if tokens.expires > Date() { return tokens.access }
        guard !tokens.refresh.isEmpty else { throw CalendarError.signedOut }
        tokens = try await OAuthTokens.exchange(client, ["grant_type": "refresh_token", "refresh_token": tokens.refresh], keeping: tokens.refresh, email: tokens.email)
        saved(tokens)
        return tokens.access
    }

    /// JSON sent and answered (nil for an answer with no body).
    @discardableResult
    func ask(_ method: String, _ url: URL, body: [String: Any]? = nil, headers: [String: String] = [:], again: Bool = true) async throws -> [String: Any]? {
        var r = URLRequest(url: url, timeoutInterval: 30)
        r.httpMethod = method
        let access = try await fresh()
        r.setValue("Bearer \(access)", forHTTPHeaderField: "Authorization")
        for (k, v) in headers { r.setValue(v, forHTTPHeaderField: k) }
        if let body {
            r.setValue("application/json", forHTTPHeaderField: "Content-Type")
            r.httpBody = try JSONSerialization.data(withJSONObject: body)
        }
        let (data, response) = try await URLSession.shared.data(for: r)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        if status == 401, again {
            tokens.expires = .distantPast
            return try await ask(method, url, body: body, headers: headers, again: false)
        }
        if status == 401 { throw CalendarError.signedOut }
        if status == 404 || status == 410 { throw CalendarError.notFound }
        let j = data.isEmpty ? nil : (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
        guard (200..<300).contains(status) else {
            let e = j?["error"]
            let why = (e as? [String: Any])?["message"] as? String ?? (e as? String) ?? "it answered \(status)"
            throw CalendarError.message("\(client.provider.name) said no: \(why)")
        }
        return j
    }
}

// MARK: - Google Calendar

@MainActor
final class GoogleCalendar: CalendarService {
    private let web: CalendarWeb
    private static let base = "https://www.googleapis.com/calendar/v3"

    init(web: CalendarWeb) { self.web = web }

    private func url(_ path: String, _ query: [String: String] = [:]) -> URL {
        var c = URLComponents(string: Self.base + path)!
        if !query.isEmpty { c.queryItems = query.sorted { $0.key < $1.key }.map { URLQueryItem(name: $0.key, value: $0.value) } }
        return c.url!
    }

    private static func enc(_ id: String) -> String { id.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? id }

    func calendars() async throws -> [AccountCalendar] {
        var out: [AccountCalendar] = []
        var page: String?
        repeat {
            var q = ["minAccessRole": "reader", "maxResults": "250"]
            if let page { q["pageToken"] = page }
            let j = try await web.ask("GET", url("/users/me/calendarList", q)) ?? [:]
            for c in j["items"] as? [[String: Any]] ?? [] {
                guard let id = c["id"] as? String, c["deleted"] as? Bool != true else { continue }
                let name = (c["summaryOverride"] as? String) ?? (c["summary"] as? String) ?? id
                out.append(AccountCalendar(id: id, name: name, color: (c["backgroundColor"] as? String) ?? "#4285f4", group: nil,
                                           on: c["primary"] as? Bool == true))
            }
            page = j["nextPageToken"] as? String
        } while page != nil
        return out
    }

    func events(in calendar: String, from: Date, to: Date) async throws -> [PulledEvent] {
        var out: [PulledEvent] = []
        var page: String?
        let iso = ISO8601DateFormatter()
        repeat {
            var q = ["timeMin": iso.string(from: from), "timeMax": iso.string(from: to), "singleEvents": "true", "maxResults": "2500", "showDeleted": "false"]
            if let page { q["pageToken"] = page }
            let j = try await web.ask("GET", url("/calendars/\(Self.enc(calendar))/events", q)) ?? [:]
            for e in j["items"] as? [[String: Any]] ?? [] {
                if let ev = Self.pulled(e) { out.append(ev) }
            }
            page = j["nextPageToken"] as? String
        } while page != nil && out.count < 4000
        return out
    }

    /// A Google event in the page's shape: a whole day by its dates, else its times; cancelled ones left out.
    static func pulled(_ e: [String: Any]) -> PulledEvent? {
        guard let id = e["id"] as? String, (e["status"] as? String) != "cancelled",
              let s = e["start"] as? [String: Any], let en = e["end"] as? [String: Any] else { return nil }
        let title = (e["summary"] as? String) ?? "Busy"
        let location = (e["location"] as? String) ?? ""
        let free = (e["transparency"] as? String) == "transparent"
        if let d = s["date"] as? String, let first = PushEvent.dayDate(d) {
            let last = (en["date"] as? String).flatMap(PushEvent.dayDate) ?? first.addingTimeInterval(86400)
            let days = max(1, Calendar.current.dateComponents([.day], from: first, to: last).day ?? 1)
            return PulledEvent(id: id, title: title, start: first.timeIntervalSince1970 * 1000, end: last.timeIntervalSince1970 * 1000, allDay: true, day: d, days: days, location: location, free: free)
        }
        guard let a = (s["dateTime"] as? String).flatMap({ CalendarTime.parse($0) }), let b = (en["dateTime"] as? String).flatMap({ CalendarTime.parse($0) }) else { return nil }
        return PulledEvent(id: id, title: title, start: a.timeIntervalSince1970 * 1000, end: b.timeIntervalSince1970 * 1000, allDay: false, day: nil, days: nil, location: location, free: free)
    }

    func simplCalendar(known: String?) async throws -> String {
        if let known {
            do {
                try await web.ask("GET", url("/calendars/\(Self.enc(known))"))
                return known
            } catch CalendarError.notFound {}
        }
        let j = try await web.ask("POST", url("/calendars"), body: ["summary": "Simpl", "description": "Your work, your own tasks and your plans, from Simpl.", "timeZone": TimeZone.current.identifier])
        guard let id = j?["id"] as? String else { throw CalendarError.message("Google did not make the Simpl calendar.") }
        return id
    }

    func write(_ e: PushEvent, into calendar: String, existing: String?) async throws -> String {
        guard let body = Self.body(e) else { throw CalendarError.message("An event with no time.") }
        if let existing {
            do {
                try await web.ask("PUT", url("/calendars/\(Self.enc(calendar))/events/\(Self.enc(existing))"), body: body)
                return existing
            } catch CalendarError.notFound {}
        }
        let j = try await web.ask("POST", url("/calendars/\(Self.enc(calendar))/events"), body: body)
        guard let id = j?["id"] as? String else { throw CalendarError.message("Google did not keep an event.") }
        return id
    }

    static func body(_ e: PushEvent) -> [String: Any]? {
        guard let span = e.span else { return nil }
        var b: [String: Any] = [
            "summary": e.title,
            "description": e.description ?? "",
            "location": e.location ?? "",
            "transparency": e.free == true ? "transparent" : "opaque",
            "extendedProperties": ["private": ["simplUid": e.uid]],
        ]
        if e.isAllDay, let d = e.day {
            let f = DateFormatter()
            f.calendar = Calendar(identifier: .gregorian)
            f.dateFormat = "yyyy-MM-dd"
            b["start"] = ["date": d]
            b["end"] = ["date": f.string(from: span.end)]
        } else {
            let iso = ISO8601DateFormatter()
            b["start"] = ["dateTime": iso.string(from: span.start), "timeZone": TimeZone.current.identifier]
            b["end"] = ["dateTime": iso.string(from: span.end), "timeZone": TimeZone.current.identifier]
        }
        if let u = e.url, !u.isEmpty { b["source"] = ["title": "Simpl", "url": u] }
        if let r = e.repeats { b["recurrence"] = ["RRULE:FREQ=\(r.frequency)\(r.interval > 1 ? ";INTERVAL=\(r.interval)" : "")"] }
        // (its alert as the plan has it; none else — Simpl's own reminders tell of what is due)
        var overrides: [[String: Any]] = []
        if let a = e.alarm, a >= 0 { overrides.append(["method": "popup", "minutes": Int(a)]) }
        b["reminders"] = ["useDefault": false, "overrides": overrides] as [String: Any]
        return b
    }

    func remove(_ id: String, from calendar: String) async throws {
        do { try await web.ask("DELETE", url("/calendars/\(Self.enc(calendar))/events/\(Self.enc(id))")) } catch CalendarError.notFound {}
    }
}

// MARK: - Outlook (Microsoft Graph)

@MainActor
final class OutlookCalendar: CalendarService {
    private let web: CalendarWeb
    private static let base = "https://graph.microsoft.com/v1.0"
    private static let utc = ["Prefer": "outlook.timezone=\"UTC\""]

    init(web: CalendarWeb) { self.web = web }

    private func url(_ path: String, _ query: [String: String] = [:]) -> URL {
        var c = URLComponents(string: Self.base + path)!
        if !query.isEmpty { c.queryItems = query.sorted { $0.key < $1.key }.map { URLQueryItem(name: $0.key, value: $0.value) } }
        return c.url!
    }

    private static func enc(_ id: String) -> String { id.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? id }

    /// Outlook's named colours, as near as Simpl's own.
    private static let colors: [String: String] = [
        "lightBlue": "#0a84ff", "lightGreen": "#34c759", "lightOrange": "#ff9500", "lightGray": "#8e8e93", "lightYellow": "#ffcc00",
        "lightTeal": "#30b0c7", "lightPink": "#ff2d55", "lightBrown": "#a2845e", "lightRed": "#ff3b30", "maxColor": "#0078d4", "auto": "#0078d4",
    ]

    func calendars() async throws -> [AccountCalendar] {
        let j = try await web.ask("GET", url("/me/calendars", ["$top": "100", "$select": "id,name,color,hexColor,isDefaultCalendar"])) ?? [:]
        return (j["value"] as? [[String: Any]] ?? []).compactMap { c -> AccountCalendar? in
            guard let id = c["id"] as? String else { return nil }
            let hex = (c["hexColor"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? Self.colors[c["color"] as? String ?? "auto"] ?? "#0078d4"
            return AccountCalendar(id: id, name: c["name"] as? String ?? "Calendar", color: hex, group: nil, on: c["isDefaultCalendar"] as? Bool == true)
        }
    }

    func events(in calendar: String, from: Date, to: Date) async throws -> [PulledEvent] {
        var out: [PulledEvent] = []
        let iso = ISO8601DateFormatter()
        var next: URL? = url("/me/calendars/\(Self.enc(calendar))/calendarView", [
            "startDateTime": iso.string(from: from), "endDateTime": iso.string(from: to), "$top": "500",
            "$select": "id,subject,start,end,isAllDay,location,showAs,isCancelled",
        ])
        while let u = next, out.count < 4000 {
            let j = try await web.ask("GET", u, headers: Self.utc) ?? [:]
            for e in j["value"] as? [[String: Any]] ?? [] {
                if let ev = Self.pulled(e) { out.append(ev) }
            }
            next = (j["@odata.nextLink"] as? String).flatMap { URL(string: $0) }
        }
        return out
    }

    /// An Outlook event in the page's shape (its times asked for in UTC).
    static func pulled(_ e: [String: Any]) -> PulledEvent? {
        guard let id = e["id"] as? String, e["isCancelled"] as? Bool != true,
              let s = (e["start"] as? [String: Any])?["dateTime"] as? String, let en = (e["end"] as? [String: Any])?["dateTime"] as? String,
              let a = CalendarTime.parse(s, utc: true), let b = CalendarTime.parse(en, utc: true) else { return nil }
        let title = (e["subject"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? "Busy"
        let location = ((e["location"] as? [String: Any])?["displayName"] as? String) ?? ""
        let free = (e["showAs"] as? String) == "free"
        if e["isAllDay"] as? Bool == true {
            // (a whole day's midnights, in UTC as asked: its days as written)
            var utcCal = Calendar(identifier: .gregorian)
            utcCal.timeZone = TimeZone(identifier: "UTC")!
            let p = utcCal.dateComponents([.year, .month, .day], from: a)
            let day = String(format: "%04d-%02d-%02d", p.year ?? 0, p.month ?? 0, p.day ?? 0)
            let days = max(1, Int((b.timeIntervalSince(a) / 86400).rounded()))
            let first = PushEvent.dayDate(day) ?? a
            return PulledEvent(id: id, title: title, start: first.timeIntervalSince1970 * 1000, end: first.addingTimeInterval(Double(days) * 86400).timeIntervalSince1970 * 1000,
                               allDay: true, day: day, days: days, location: location, free: free)
        }
        return PulledEvent(id: id, title: title, start: a.timeIntervalSince1970 * 1000, end: b.timeIntervalSince1970 * 1000, allDay: false, day: nil, days: nil, location: location, free: free)
    }

    func simplCalendar(known: String?) async throws -> String {
        if let known {
            do {
                try await web.ask("GET", url("/me/calendars/\(Self.enc(known))", ["$select": "id"]))
                return known
            } catch CalendarError.notFound {}
        }
        // (one named Simpl already there — made by Simpl on another computer — is used, not another made beside it)
        let list = try await web.ask("GET", url("/me/calendars", ["$top": "100", "$select": "id,name"])) ?? [:]
        if let had = (list["value"] as? [[String: Any]] ?? []).first(where: { ($0["name"] as? String) == "Simpl" })?["id"] as? String { return had }
        let j = try await web.ask("POST", url("/me/calendars"), body: ["name": "Simpl", "color": "lightBlue"])
        guard let id = j?["id"] as? String else { throw CalendarError.message("Outlook did not make the Simpl calendar.") }
        return id
    }

    func write(_ e: PushEvent, into calendar: String, existing: String?) async throws -> String {
        guard let body = Self.body(e) else { throw CalendarError.message("An event with no time.") }
        if let existing {
            do {
                try await web.ask("PATCH", url("/me/events/\(Self.enc(existing))"), body: body)
                return existing
            } catch CalendarError.notFound {}
        }
        let j = try await web.ask("POST", url("/me/calendars/\(Self.enc(calendar))/events"), body: body)
        guard let id = j?["id"] as? String else { throw CalendarError.message("Outlook did not keep an event.") }
        return id
    }

    static func body(_ e: PushEvent) -> [String: Any]? {
        guard let span = e.span else { return nil }
        let zone = TimeZone.current
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.timeZone = zone
        f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss"
        var b: [String: Any] = [
            "subject": e.title,
            "body": ["contentType": "text", "content": e.description ?? ""],
            "start": ["dateTime": f.string(from: span.start), "timeZone": zone.identifier],
            "end": ["dateTime": f.string(from: span.end), "timeZone": zone.identifier],
            "isAllDay": e.isAllDay,
            "showAs": e.free == true ? "free" : "busy",
            "location": ["displayName": e.location ?? ""],
        ]
        if let a = e.alarm, a >= 0 {
            b["isReminderOn"] = true
            b["reminderMinutesBeforeStart"] = Int(a)
        } else {
            b["isReminderOn"] = false
        }
        if let r = e.repeats {
            var cal = Calendar(identifier: .gregorian)
            cal.timeZone = zone
            let dow = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"][cal.component(.weekday, from: span.start) - 1]
            let startDay = String(f.string(from: span.start).prefix(10))
            let pattern: [String: Any]
            switch r.frequency {
            case "DAILY": pattern = ["type": "daily", "interval": r.interval]
            case "WEEKLY": pattern = ["type": "weekly", "interval": r.interval, "daysOfWeek": [dow], "firstDayOfWeek": "sunday"]
            default: pattern = ["type": "absoluteMonthly", "interval": r.interval, "dayOfMonth": cal.component(.day, from: span.start)]
            }
            b["recurrence"] = ["pattern": pattern, "range": ["type": "noEnd", "startDate": startDay]] as [String: Any]
        }
        return b
    }

    func remove(_ id: String, from calendar: String) async throws {
        do { try await web.ask("DELETE", url("/me/events/\(Self.enc(id))")) } catch CalendarError.notFound {}
    }
}

/// Times as the calendars' APIs write them: ISO 8601 with or without fractions of a second; Outlook's, asked for in UTC,
/// with seven digits of fraction and no zone.
enum CalendarTime {
    static func parse(_ s: String, utc: Bool = false) -> Date? {
        var t = s
        if utc, !t.hasSuffix("Z"), !t.contains("+"), t.range(of: #"-\d\d:\d\d$"#, options: .regularExpression) == nil { t += "Z" }
        // (fractions cut to milliseconds: ISO8601DateFormatter reads no more than three digits)
        if let dot = t.firstIndex(of: ".") {
            let digits = t[t.index(after: dot)...].prefix { $0.isNumber }
            let tail = t[t.index(after: dot)...].dropFirst(digits.count)
            let ms = String(digits.prefix(3)).padding(toLength: 3, withPad: "0", startingAt: 0)
            t = String(t[..<dot]) + "." + ms + String(tail)
        }
        let withFraction = ISO8601DateFormatter()
        withFraction.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return withFraction.date(from: t) ?? ISO8601DateFormatter().date(from: t)
    }
}

// MARK: - Apple Calendar (this Mac's calendars)

@MainActor
final class AppleCalendar: CalendarService {
    static let store = EKEventStore()

    /// Whether Simpl may use the Mac's calendars: asked the first time (macOS's own question).
    static func access() async throws {
        switch EKEventStore.authorizationStatus(for: .event) {
        case .fullAccess: return
        case .notDetermined:
            let ok = try await store.requestFullAccessToEvents()
            if !ok { throw CalendarError.denied }
        default:
            throw CalendarError.denied
        }
    }

    static var allowed: Bool { EKEventStore.authorizationStatus(for: .event) == .fullAccess }

    private var store: EKEventStore { Self.store }

    func calendars() async throws -> [AccountCalendar] {
        try await Self.access()
        return store.calendars(for: .event).map { c in
            AccountCalendar(id: c.calendarIdentifier, name: c.title, color: Self.hex(c.cgColor), group: c.source?.title,
                            on: c.calendarIdentifier == store.defaultCalendarForNewEvents?.calendarIdentifier)
        }
    }

    func events(in calendar: String, from: Date, to: Date) async throws -> [PulledEvent] {
        guard let c = store.calendar(withIdentifier: calendar) else { throw CalendarError.notFound }
        let p = store.predicateForEvents(withStart: from, end: to, calendars: [c])
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.dateFormat = "yyyy-MM-dd"
        return store.events(matching: p).prefix(4000).compactMap { e -> PulledEvent? in
            guard let a = e.startDate, let b = e.endDate, e.status != .canceled else { return nil }
            // (each time a repeating one comes round is an event of its own: its id and that time)
            let id = "\(e.calendarItemIdentifier)|\(Int(a.timeIntervalSince1970))"
            let free = e.availability == .free
            if e.isAllDay {
                let first = Calendar.current.startOfDay(for: a)
                let days = max(1, Calendar.current.dateComponents([.day], from: first, to: Calendar.current.startOfDay(for: b.addingTimeInterval(-1))).day.map { $0 + 1 } ?? 1)
                return PulledEvent(id: id, title: e.title ?? "Busy", start: first.timeIntervalSince1970 * 1000, end: first.addingTimeInterval(Double(days) * 86400).timeIntervalSince1970 * 1000,
                                   allDay: true, day: f.string(from: first), days: days, location: e.location ?? "", free: free)
            }
            return PulledEvent(id: id, title: e.title ?? "Busy", start: a.timeIntervalSince1970 * 1000, end: b.timeIntervalSince1970 * 1000, allDay: false, day: nil, days: nil, location: e.location ?? "", free: free)
        }
    }

    func simplCalendar(known: String?) async throws -> String {
        try await Self.access()
        if let known, store.calendar(withIdentifier: known) != nil { return known }
        if let had = store.calendars(for: .event).first(where: { $0.title == "Simpl" && $0.allowsContentModifications }) { return had.calendarIdentifier }
        let c = EKCalendar(for: .event, eventStore: store)
        c.title = "Simpl"
        c.cgColor = NSColor.systemBlue.cgColor
        // (in iCloud when the Mac has it, so the phone has it too; else where new events go; else on this Mac)
        let sources = store.sources
        c.source = sources.first { $0.sourceType == .calDAV && $0.title.lowercased().contains("icloud") }
            ?? store.defaultCalendarForNewEvents?.source
            ?? sources.first { $0.sourceType == .local }
        guard c.source != nil else { throw CalendarError.message("This Mac has no calendar account to keep a Simpl calendar in.") }
        do { try store.saveCalendar(c, commit: true) } catch { throw CalendarError.message("The Simpl calendar could not be made: \(error.localizedDescription)") }
        return c.calendarIdentifier
    }

    func write(_ e: PushEvent, into calendar: String, existing: String?) async throws -> String {
        guard let c = store.calendar(withIdentifier: calendar) else { throw CalendarError.notFound }
        guard let span = e.span else { throw CalendarError.message("An event with no time.") }
        let ev = existing.flatMap { store.calendarItem(withIdentifier: $0) as? EKEvent } ?? EKEvent(eventStore: store)
        ev.calendar = c
        ev.title = e.title
        ev.notes = e.description
        ev.location = e.location
        ev.url = e.url.flatMap { URL(string: $0) }
        ev.isAllDay = e.isAllDay
        ev.startDate = span.start
        ev.endDate = e.isAllDay ? span.end.addingTimeInterval(-1) : span.end
        ev.availability = e.free == true ? .free : .busy
        if let a = e.alarm, a >= 0 { ev.alarms = [EKAlarm(relativeOffset: -a * 60)] } else { ev.alarms = nil }
        if let r = e.repeats {
            let f: EKRecurrenceFrequency = r.frequency == "DAILY" ? .daily : r.frequency == "WEEKLY" ? .weekly : .monthly
            ev.recurrenceRules = [EKRecurrenceRule(recurrenceWith: f, interval: r.interval, end: nil)]
        } else if ev.hasRecurrenceRules {
            ev.recurrenceRules = nil
        }
        do { try store.save(ev, span: .futureEvents, commit: true) } catch { throw CalendarError.message("An event could not be kept: \(error.localizedDescription)") }
        return ev.calendarItemIdentifier
    }

    func remove(_ id: String, from calendar: String) async throws {
        guard let ev = store.calendarItem(withIdentifier: id) as? EKEvent else { return }
        try? store.remove(ev, span: .futureEvents, commit: true)
    }

    static func hex(_ c: CGColor?) -> String {
        guard let c, let rgb = NSColor(cgColor: c)?.usingColorSpace(.sRGB) else { return "#8e8e93" }
        return String(format: "#%02x%02x%02x", Int(rgb.redComponent * 255), Int(rgb.greenComponent * 255), Int(rgb.blueComponent * 255))
    }
}

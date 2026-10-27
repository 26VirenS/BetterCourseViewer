import AppKit
import CryptoKit
import Foundation

extension Notification.Name {
    /// (1.3.184) A calendar account's calendars read again (or one shown, hidden or let go): the Calendar shows them.
    static let simplCalendarAccountsSynced = Notification.Name("Simpl.calendarAccountsSynced")
}

/// (1.3.184) A calendar account signed in to in Sync: its calendars, which of them Simpl shows, whether Simpl writes
/// its own calendar there, and how the last sync went. Nothing secret: the tokens are sealed apart (CalendarAccounts).
struct CalendarAccount: Codable, Identifiable, Equatable {
    var id: String
    var provider: CalendarProvider
    /// Who: the email signed in with, or "This Mac".
    var name: String
    var calendars: [AccountCalendar]
    /// Simpl's own calendar — the work, the student's own tasks, the plans — written into a calendar named Simpl there.
    var push: Bool
    var simplCalendar: String?
    var synced: Date?
    var problem: String
    var pulled: Int
    var pushed: Int
    /// Its sign-in has ended: signed in to again before anything more is read.
    var signedOut: Bool
}

/// (1.3.184) Sync's calendar accounts, the real sign-in the student asked for in place of copying links: Apple
/// Calendar (this Mac's calendars), Google Calendar and Outlook (CalendarProviders.swift). Each, once signed in to:
/// - its calendars listed in Sync, each shown on Simpl's calendar or not (its main calendar at first); the events of
///   those shown, a month back to six months on, kept in the page's storage as a linked calendar's are (cal:links with
///   source "account", cal:feed:<id>), so the Calendar lays them over Canvas's and Simpl counts them busy when it finds
///   a free time — as the calendars linked by their address are (background.js leaves these to the app);
/// - Simpl's own calendar (the page's feed: content/app/feed.js events, asked for as `feedEvents`) written into a
///   calendar named Simpl in the account, made there the first time: each event put in, changed when what it says
///   changed (its digest kept beside its id), and taken out when it is gone from Simpl's.
/// Read and written soon after Simpl opens, every fifteen minutes, when Sync opens, and at Sync Now. The accounts are a
/// list in the preferences; their tokens are sealed by Simpl's vault (Vault.swift), never in the preferences or the page.
/// Signed out: its tokens forgotten (Google's given back), its calendars gone from Simpl's; the Simpl calendar it made
/// in the account stays there, to be deleted in the calendar app if it is not wanted.
@MainActor
final class CalendarAccounts: ObservableObject {
    static let shared = CalendarAccounts()

    @Published private(set) var accounts: [CalendarAccount] = []
    /// The accounts being synced or signed in to now (and "new:<provider>" while one is being added).
    @Published private(set) var busy: Set<String> = []

    private weak var engine: Engine?
    private var timer: Timer?
    private let signIn = OAuthSignIn()
    private var tokens: [String: OAuthTokens] = [:]

    private static let listKey = "SimplCalendarAccounts"
    private static let pushedKey = "SimplCalendarPushed."
    private static let tokenPurpose = "calendar-accounts"
    /// (the screenshot suite's -SimplCalAccountsDemo YES: two accounts as if signed in to, nothing read or written)
    static let demo = UserDefaults.standard.bool(forKey: "SimplCalAccountsDemo")
    private static let back: TimeInterval = 30 * 86400
    private static let ahead: TimeInterval = 180 * 86400

    private init() {
        if Self.demo {
            accounts = Self.demoAccounts()
            return
        }
        if let data = UserDefaults.standard.data(forKey: Self.listKey), let list = try? JSONDecoder().decode([CalendarAccount].self, from: data) { accounts = list }
        tokens = Self.readTokens()
    }

    // MARK: Started, and kept up to date

    /// Once the app's screens are up: read soon, then every fifteen minutes.
    func started(_ engine: Engine) {
        self.engine = engine
        guard !Self.demo, timer == nil else { return }
        timer = Timer.scheduledTimer(withTimeInterval: 15 * 60, repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.syncAll() }
        }
        guard !accounts.isEmpty else { return }
        Task {
            try? await Task.sleep(nanoseconds: 8_000_000_000)
            await syncAll()
        }
    }

    func syncAll() async {
        for a in accounts where !a.signedOut { await sync(a.id) }
    }

    /// The accounts not read in the last five minutes, read (Sync opened).
    func syncStale() async {
        for a in accounts where !a.signedOut && Date().timeIntervalSince(a.synced ?? .distantPast) > 5 * 60 { await sync(a.id) }
    }

    // MARK: Signing in and out

    /// An account signed in to (Apple: the Mac's calendars allowed) and read for the first time. Why not, or nil.
    func connect(_ p: CalendarProvider) async -> String? {
        guard !Self.demo else { return nil }
        let key = "new:\(p.rawValue)"
        guard !busy.contains(key) else { return nil }
        busy.insert(key)
        defer { busy.remove(key) }
        do {
            var account: CalendarAccount
            switch p {
            case .apple:
                try await AppleCalendar.access()
                account = CalendarAccount(id: "apple", provider: .apple, name: "This Mac's calendars", calendars: [], push: true, simplCalendar: nil, synced: nil, problem: "", pulled: 0, pushed: 0, signedOut: false)
            case .google, .microsoft:
                guard let client = OAuthClient.of(p) else { return "\(p.name) sign-in is not ready in this version yet." }
                let t = try await signIn.signIn(client)
                let who = t.email.isEmpty ? p.name : t.email
                account = CalendarAccount(id: "\(p.rawValue):\(who.lowercased())", provider: p, name: who, calendars: [], push: true, simplCalendar: nil, synced: nil, problem: "", pulled: 0, pushed: 0, signedOut: false)
                tokens[account.id] = t
                saveTokens()
            }
            if let i = accounts.firstIndex(where: { $0.id == account.id }) {
                // (signed in to again: what was chosen for it kept)
                account.calendars = accounts[i].calendars
                account.push = accounts[i].push
                account.simplCalendar = accounts[i].simplCalendar
                accounts[i] = account
            } else {
                accounts.append(account)
            }
            save()
            NSLog("[Simpl] calendar accounts: %@ signed in", p.rawValue)
            await sync(account.id)
            return nil
        } catch CalendarError.cancelled {
            return nil
        } catch {
            NSLog("[Simpl] calendar accounts: %@ not signed in: %@", p.rawValue, error.localizedDescription)
            return error.localizedDescription
        }
    }

    /// Signed out: its tokens forgotten (Google's given back), its calendars gone from Simpl's, what was written for it
    /// forgotten. The Simpl calendar in the account stays.
    func disconnect(_ id: String) async {
        guard let a = accounts.first(where: { $0.id == id }) else { return }
        accounts.removeAll { $0.id == id }
        save()
        if !Self.demo {
            if a.provider == .google, let t = tokens[id], !t.refresh.isEmpty {
                var r = URLRequest(url: URL(string: "https://oauth2.googleapis.com/revoke")!, timeoutInterval: 15)
                r.httpMethod = "POST"
                r.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
                r.httpBody = Data("token=\(OAuthTokens.formEncode(t.refresh))".utf8)
                _ = try? await URLSession.shared.data(for: r)
            }
            tokens[id] = nil
            saveTokens()
            UserDefaults.standard.removeObject(forKey: Self.pushedKey + id)
            writeLinks()
        }
        NSLog("[Simpl] calendar accounts: %@ signed out", a.provider.rawValue)
        NotificationCenter.default.post(name: .simplCalendarAccountsSynced, object: nil)
    }

    // MARK: Choices

    /// A calendar shown on Simpl's calendar, or not: its events read (or put away) at once.
    func show(_ calendar: String, of id: String, _ on: Bool) {
        guard let i = accounts.firstIndex(where: { $0.id == id }), let k = accounts[i].calendars.firstIndex(where: { $0.id == calendar }) else { return }
        accounts[i].calendars[k].on = on
        save()
        guard !Self.demo else { return }
        if on {
            Task { await sync(id) }
        } else {
            writeLinks()
            NotificationCenter.default.post(name: .simplCalendarAccountsSynced, object: nil)
        }
    }

    /// Simpl's own calendar written into the account, or no longer: turned off, what was written there is taken out.
    func setPush(_ on: Bool, for id: String) {
        guard let i = accounts.firstIndex(where: { $0.id == id }) else { return }
        accounts[i].push = on
        save()
        guard !Self.demo else { return }
        Task {
            if on {
                await sync(id)
            } else {
                await takeOut(id)
            }
        }
    }

    // MARK: Syncing one account

    func sync(_ id: String) async {
        guard !Self.demo, let engine, let start = accounts.first(where: { $0.id == id }), !busy.contains(id) else { return }
        busy.insert(id)
        defer { busy.remove(id) }
        var a = start
        do {
            let service = try self.service(for: a)
            // (its calendars as the account has them now: a new one not shown, one gone gone; never Simpl's own, nor one
            //  named Simpl — the calendar this app writes, or the one subscribed to by its private link — shown back)
            let listed = try await service.calendars().filter { $0.id != a.simplCalendar && $0.name != "Simpl" }
            let before = a.calendars
            a.calendars = listed.map { c -> AccountCalendar in
                var c = c
                if !before.isEmpty { c.on = before.first(where: { $0.id == c.id })?.on ?? false }
                return c
            }
            // in: the shown calendars' events
            let now = Date()
            var pulled = 0
            var feeds: [String: Any] = [:]
            for c in a.calendars where c.on {
                let events = try await service.events(in: c.id, from: now.addingTimeInterval(-Self.back), to: now.addingTimeInterval(Self.ahead))
                pulled += events.count
                feeds["cal:feed:\(Self.linkID(a.id, c.id))"] = ["at": now.timeIntervalSince1970 * 1000, "ok": true, "error": "", "name": c.name, "events": events.map(\.page)] as [String: Any]
            }
            a.pulled = pulled
            // out: Simpl's own calendar
            if a.push {
                let reply = try await engine.call("feedEvents", as: FeedEvents.self)
                let cal = try await service.simplCalendar(known: a.simplCalendar)
                a.simplCalendar = cal
                a.pushed = try await push(reply.events, via: service, account: a.id, into: cal)
                // (made just now in an account read above: not one of the student's own calendars)
                a.calendars.removeAll { $0.id == cal }
            }
            a.synced = Date()
            a.problem = ""
            a.signedOut = false
            put(a)
            Bridge.shared.keep(feeds)
            writeLinks()
            NSLog("[Simpl] calendar accounts: %@ synced, %d events in, %d out", a.provider.rawValue, a.pulled, a.push ? a.pushed : 0)
        } catch CalendarError.signedOut {
            a.signedOut = true
            a.problem = CalendarError.signedOut.localizedDescription
            put(a)
            NSLog("[Simpl] calendar accounts: %@ signed out by its provider", a.provider.rawValue)
        } catch {
            a.problem = error.localizedDescription
            put(a)
            NSLog("[Simpl] calendar accounts: %@ not synced: %@", a.provider.rawValue, error.localizedDescription)
        }
        NotificationCenter.default.post(name: .simplCalendarAccountsSynced, object: nil)
    }

    private struct FeedEvents: Decodable { var events: [PushEvent] }

    /// What was written for an event: its id there, and the digest of what it said.
    private struct Pushed: Codable {
        var id: String
        var sig: String
    }

    private struct PushedBook: Codable {
        var calendar: String
        var events: [String: Pushed]
    }

    /// Simpl's calendar written: each event put in or changed if what it says changed, and one gone from Simpl's taken
    /// out. How many it holds.
    private func push(_ events: [PushEvent], via service: CalendarService, account id: String, into cal: String) async throws -> Int {
        var book = readBook(id)
        if book.calendar != cal { book = PushedBook(calendar: cal, events: [:]) } // (a new calendar: everything again)
        var wanted: [String: PushEvent] = [:]
        for e in events where e.span != nil { wanted[e.uid] = e }
        var written = 0
        for e in wanted.values.sorted(by: { $0.uid < $1.uid }) {
            let had = book.events[e.uid]
            if had?.sig == e.signature { continue }
            let rid = try await service.write(e, into: cal, existing: had?.id)
            book.events[e.uid] = Pushed(id: rid, sig: e.signature)
            written += 1
            if written % 25 == 0 { saveBook(book, id) } // (a long first sync kept as it goes)
        }
        for (uid, had) in book.events where wanted[uid] == nil {
            try await service.remove(had.id, from: cal)
            book.events[uid] = nil
        }
        saveBook(book, id)
        return wanted.count
    }

    /// What was written into the account's Simpl calendar, taken out (Simpl's calendar no longer written there).
    private func takeOut(_ id: String) async {
        guard let a = accounts.first(where: { $0.id == id }), let cal = a.simplCalendar, let service = try? self.service(for: a) else { return }
        busy.insert(id)
        defer { busy.remove(id) }
        var book = readBook(id)
        for (uid, had) in book.events {
            try? await service.remove(had.id, from: cal)
            book.events[uid] = nil
        }
        saveBook(book, id)
        if let i = accounts.firstIndex(where: { $0.id == id }) {
            accounts[i].pushed = 0
            save()
        }
    }

    private func service(for a: CalendarAccount) throws -> CalendarService {
        switch a.provider {
        case .apple:
            return AppleCalendar()
        case .google, .microsoft:
            guard let client = OAuthClient.of(a.provider) else { throw CalendarError.message("\(a.provider.name) sign-in is not ready in this version.") }
            guard let t = tokens[a.id] else { throw CalendarError.signedOut }
            let web = CalendarWeb(client: client, tokens: t) { [weak self] fresh in
                self?.tokens[a.id] = fresh
                self?.saveTokens()
            }
            return a.provider == .google ? GoogleCalendar(web: web) : OutlookCalendar(web: web)
        }
    }

    private func put(_ a: CalendarAccount) {
        if let i = accounts.firstIndex(where: { $0.id == a.id }) { accounts[i] = a }
        save()
    }

    // MARK: The page's storage

    /// A shown calendar's id among the page's linked calendars: short, and the same each time.
    static func linkID(_ account: String, _ calendar: String) -> String {
        let d = SHA256.hash(data: Data("\(account)\u{1f}\(calendar)".utf8))
        return "a" + d.prefix(8).map { String(format: "%02x", $0) }.joined()
    }

    /// The page's linked calendars: those linked by their address as they are, then every shown calendar of every
    /// account (source "account": background.js leaves them to the app); the events of one no longer shown taken away.
    private func writeLinks() {
        let had = Bridge.shared.stored(["cal:links"])["cal:links"] as? [[String: Any]] ?? []
        let byAddress = had.filter { ($0["source"] as? String) != "account" }
        let hadIDs = Set(had.filter { ($0["source"] as? String) == "account" }.compactMap { $0["id"] as? String })
        var mine: [[String: Any]] = []
        for a in accounts where !a.signedOut {
            for c in a.calendars where c.on {
                mine.append(["id": Self.linkID(a.id, c.id), "name": c.name, "color": c.color, "on": true, "source": "account", "account": a.provider.rawValue, "accountName": a.name])
            }
        }
        let now = Set(mine.compactMap { $0["id"] as? String })
        Bridge.shared.keep(["cal:links": byAddress + mine], removing: hadIDs.subtracting(now).map { "cal:feed:\($0)" })
    }

    // MARK: Kept

    private func save() {
        guard !Self.demo, let data = try? JSONEncoder().encode(accounts) else { return }
        UserDefaults.standard.set(data, forKey: Self.listKey)
    }

    private func readBook(_ id: String) -> PushedBook {
        guard let data = UserDefaults.standard.data(forKey: Self.pushedKey + id), let b = try? JSONDecoder().decode(PushedBook.self, from: data) else { return PushedBook(calendar: "", events: [:]) }
        return b
    }

    private func saveBook(_ b: PushedBook, _ id: String) {
        if let data = try? JSONEncoder().encode(b) { UserDefaults.standard.set(data, forKey: Self.pushedKey + id) }
    }

    private static var tokenFile: URL { Vault.folder.appendingPathComponent(Vault.fileName(tokenPurpose)) }

    private static func readTokens() -> [String: OAuthTokens] {
        guard let saved = Vault.read(from: tokenFile, purpose: tokenPurpose), !saved.legacy,
              let list = try? JSONDecoder().decode([String: OAuthTokens].self, from: saved.data) else { return [:] }
        return list
    }

    private func saveTokens() {
        guard let data = try? JSONEncoder().encode(tokens) else { return }
        if tokens.isEmpty {
            try? FileManager.default.removeItem(at: Self.tokenFile)
        } else if !Vault.write(data, to: Self.tokenFile, purpose: Self.tokenPurpose) {
            NSLog("[Simpl] calendar accounts: the sign-in could not be sealed, so it is kept only until Simpl quits")
        }
    }

    // MARK: The pictures' accounts

    private static func demoAccounts() -> [CalendarAccount] {
        let ago = Date().addingTimeInterval(-120)
        return [
            CalendarAccount(id: "apple", provider: .apple, name: "This Mac's calendars", calendars: [
                AccountCalendar(id: "home", name: "Home", color: "#34c759", group: "iCloud", on: true),
                AccountCalendar(id: "work", name: "Work", color: "#ff9500", group: "iCloud", on: false),
                AccountCalendar(id: "birthdays", name: "Birthdays", color: "#8e8e93", group: "Other", on: false),
            ], push: true, simplCalendar: "simpl", synced: ago, problem: "", pulled: 23, pushed: 41, signedOut: false),
            CalendarAccount(id: "google:sam.student@gmail.com", provider: .google, name: "sam.student@gmail.com", calendars: [
                AccountCalendar(id: "primary", name: "Sam Student", color: "#4285f4", group: nil, on: true),
                AccountCalendar(id: "club", name: "Robotics Club", color: "#af52de", group: nil, on: true),
                AccountCalendar(id: "holidays", name: "Holidays in United States", color: "#0b8043", group: nil, on: false),
            ], push: false, simplCalendar: nil, synced: ago, problem: "", pulled: 58, pushed: 0, signedOut: false),
        ]
    }
}

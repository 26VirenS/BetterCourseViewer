#if os(iOS)
import BackgroundTasks
import UIKit
#endif
import SwiftUI
import UserNotifications

/// New activity (1.5): an alert when something is posted — an announcement, a grade, a comment on your work, a message,
/// a discussion — read while Simpl is closed. iOS wakes the app now and then (Background App Refresh: it decides when,
/// usually within an hour or a few, more often for an app used a lot); the app then reads Canvas's activity stream
/// itself with the sign-in kept on this iPhone (CookieJar) and says what is new. Nothing leaves the phone and no
/// server is involved. What the student has already seen in the app is not news: whenever the app's screens come up,
/// everything in the stream is marked seen, so an alert is only ever for what arrived while the app was away.
/// (1.7) Brightspace has no activity stream: there the app reads each chosen course's news and the grades released in
/// it, with the teacher's comment on each, the same way (`readBrightspace`).
@MainActor
final class Activity: ObservableObject {
    static let shared = Activity()
    /// The background refresh's name (Info.plist BGTaskSchedulerPermittedIdentifiers).
    static let taskID = "com.simplcourses.app.activity"

    enum Kind: String, CaseIterable, Identifiable {
        case announcements, grades, comments, messages, discussions

        var id: String { rawValue }
        var label: String {
            switch self {
            case .announcements: return "Announcements"
            case .grades: return "Grades"
            case .comments: return "Comments on Your Work"
            case .messages: return "Inbox Messages"
            case .discussions: return "Discussions"
            }
        }
        static let standard: Set<Kind> = [.announcements, .grades, .comments, .messages] // (a discussion's every reply is noisy: off until asked for)
        /// (1.7) What Brightspace's courses can say here: no Inbox there, and its discussions are not read
        static let brightspace: [Kind] = [.announcements, .grades, .comments]
    }

    /// What the page says the check needs (its `watchInfo` call): the student's id and the courses chosen; (1.7) which
    /// platform, and on Brightspace the API versions its page found the school answering.
    private struct Context: Codable {
        var me: String?
        var courses: [String: String]
        var lms: String?
        var lp: String?
        var le: String?
    }

    private struct WatchInfo: Decodable {
        struct Course: Decodable {
            var id: String
            var name: String
        }
        var me: String?
        var courses: [Course]
        var lms: String?
        var lp: String?
        var le: String?
    }

    /// What a read of the school came to.
    private enum Read {
        case events([Event])
        /// the kept sign-in was refused: it has ended
        case refused
        /// no answer, or not one to read
        case failed
    }

    /// Something new, as an alert says it.
    private struct Event {
        var key: String
        var kind: Kind
        var title: String
        var subtitle: String
        var body: String
        var url: String?
        var when: Date
    }

    private static let onKey = "SimplActivityOn"
    private static let kindsKey = "SimplActivityKinds"
    private static let seenKey = "SimplActivitySeen"
    private static let contextKey = "SimplActivityContext"
    private static let lastKey = "SimplActivityLast"
    private static let staleKey = "SimplActivityStaleSaid"
    private static let seenCap = 500
    private static let alertsCap = 4

    @Published private(set) var on = UserDefaults.standard.bool(forKey: Activity.onKey)
    @Published var kinds: Set<Kind> = Activity.loadKinds() {
        didSet { UserDefaults.standard.set(kinds.map(\.rawValue), forKey: Activity.kindsKey) }
    }
    /// Background App Refresh turned off for Simpl (or for the phone): Settings says so and offers the way there.
    @Published private(set) var refreshOff = false
    /// When Canvas was last read for this (in the background or as the app came up).
    @Published private(set) var lastCheck: Date? = UserDefaults.standard.object(forKey: Activity.lastKey) as? Date
    /// (1.7) The school is a Brightspace: Settings offers what it can say, and says why the alerts can pause.
    @Published private(set) var onBrightspace = Activity.context().lms == "d2l"

    /// The kinds of activity Settings offers for this school.
    var kindsOffered: [Kind] { onBrightspace ? Kind.brightspace : Kind.allCases }
    #if os(macOS)
    /// (the Mac app: it runs while it is open, so a look every 20 minutes is a timer of its own)
    private var timer: Timer?
    #endif

    private static func loadKinds() -> Set<Kind> {
        guard let raw = UserDefaults.standard.array(forKey: kindsKey) as? [String] else { return Kind.standard }
        return Set(raw.compactMap(Kind.init(rawValue:)))
    }

    // MARK: - On and off

    /// The switch turned on: iOS asks once for alerts; refused, it stays off (Settings says where to allow them).
    func enable(_ engine: Engine) async {
        let granted = (try? await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound])) ?? false
        await Reminders.shared.checkRefused()
        guard granted else {
            setOn(false)
            return
        }
        setOn(true)
        await sync(engine)
    }

    func disable() {
        setOn(false)
        #if os(iOS)
        BGTaskScheduler.shared.cancel(taskRequestWithIdentifier: Activity.taskID)
        #else
        timer?.invalidate()
        timer = nil
        #endif
    }

    /// Signed out, another school: what was seen and the courses chosen were that account's.
    func reset() {
        for k in [Activity.seenKey, Activity.contextKey, Activity.lastKey, Activity.staleKey] { UserDefaults.standard.removeObject(forKey: k) }
        lastCheck = nil
        onBrightspace = false
    }

    func checkRefresh() {
        #if os(iOS)
        refreshOff = UIApplication.shared.backgroundRefreshStatus != .available
        #else
        refreshOff = false
        #endif
    }

    private func setOn(_ value: Bool) {
        on = value
        UserDefaults.standard.set(value, forKey: Activity.onKey)
    }

    // MARK: - The app up: what it shows is seen

    /// The app's screens up (a launch, a return to the foreground): the courses chosen noted for the check, everything
    /// in the stream now marked seen (the student is looking at it), and the next check asked for.
    func sync(_ engine: Engine) async {
        guard on else { return }
        await note(engine)
        _ = await check(notify: false)
        schedule()
    }

    /// What the page says the check needs, kept for when it runs with no page up.
    private func note(_ engine: Engine) async {
        guard let w = try? await engine.call("watchInfo", as: WatchInfo.self) else { return }
        let ctx = Context(me: w.me, courses: Dictionary(w.courses.map { ($0.id, $0.name) }, uniquingKeysWith: { a, _ in a }), lms: w.lms, lp: w.lp, le: w.le)
        if let data = try? JSONEncoder().encode(ctx) { UserDefaults.standard.set(data, forKey: Activity.contextKey) }
        onBrightspace = w.lms == "d2l"
    }

    /// (the screenshot suite, scripts/dev/mac-shots.sh: -SimplActivityProbe YES) One read of the school as the check
    /// makes it, the switch on or off, said on the console: the platform, and what an alert would say. Nothing is
    /// marked seen and nothing is posted.
    func probe(_ engine: Engine) async {
        await note(engine)
        let ctx = Activity.context()
        var line = "activity-probe lms=\(ctx.lms ?? "?") courses=\(ctx.courses.count)"
        switch await read(ctx) {
        case .events(let list):
            line += " events=\(list.count) " + list.sorted { $0.when > $1.when }.prefix(10).map { "[\($0.kind.rawValue)] \($0.title) / \($0.subtitle) / \($0.body) -> \($0.url ?? "")" }.joined(separator: " | ")
        case .refused:
            line += " refused"
        case .failed:
            line += " failed"
        }
        FileHandle.standardError.write(Data((line + "\n").utf8))
    }

    /// The next background check, no sooner than 20 minutes from now (iOS picks the moment).
    func schedule() {
        guard on else { return }
        #if os(iOS)
        let r = BGAppRefreshTaskRequest(identifier: Activity.taskID)
        r.earliestBeginDate = Date(timeIntervalSinceNow: 20 * 60)
        try? BGTaskScheduler.shared.submit(r)
        #else
        guard timer == nil else { return }
        timer = Timer.scheduledTimer(withTimeInterval: 20 * 60, repeats: true) { _ in
            Task { @MainActor in
                guard Activity.shared.on else { return }
                _ = await Activity.shared.check(notify: true)
            }
        }
        #endif
    }

    // MARK: - The check

    /// iOS woke the app for it: the next one asked for first (so a check cut short still leaves one coming), then
    /// Canvas read and anything new said.
    func backgroundCheck() async {
        schedule()
        guard on else { return }
        _ = await check(notify: true)
    }

    /// Reads Canvas's activity stream (on Brightspace, its courses' news and grades); `notify`: says what is new
    /// (otherwise only marks it seen). The number said.
    @discardableResult
    func check(notify: Bool) async -> Int {
        let ctx = Activity.context()
        let events: [Event]
        switch await read(ctx) {
        case .events(let list):
            events = list
        case .refused:
            // (Brightspace ends a sign-in left unused after a time its school sets — often hours: an alert each time
            // would only nag. Settings says the alerts pause then, until Simpl is next opened.)
            if notify && ctx.lms != "d2l" { await sayStale() }
            return 0
        case .failed:
            return 0
        }
        UserDefaults.standard.set(false, forKey: Activity.staleKey)
        let now = Date()
        lastCheck = now
        UserDefaults.standard.set(now, forKey: Activity.lastKey)

        var seen = UserDefaults.standard.stringArray(forKey: Activity.seenKey) ?? []
        let primed = !seen.isEmpty
        let known = Set(seen)
        let fresh = events.filter { !known.contains($0.key) }
        seen.append(contentsOf: fresh.map(\.key))
        if seen.count > Activity.seenCap { seen.removeFirst(seen.count - Activity.seenCap) }
        if seen.isEmpty { seen = ["primed"] } // (a first read with nothing in it has learned all there is too)
        UserDefaults.standard.set(seen, forKey: Activity.seenKey)
        // (the very first read only learns what is there: an alert for every old thing is not news)
        guard notify, primed else { return 0 }
        let said = fresh.filter { kinds.contains($0.kind) }.sorted { $0.when > $1.when }
        for e in said.prefix(Activity.alertsCap) { await post(e) }
        if said.count > Activity.alertsCap {
            let more = said.count - Activity.alertsCap
            await post(Event(key: "more.\(Int(now.timeIntervalSince1970))", kind: .announcements, title: "Simpl Courses", subtitle: "", body: "\(more) more \(more == 1 ? "update" : "updates") on \(ctx.lms == "d2l" ? "Brightspace" : "Canvas").", url: "/", when: now))
        }
        return said.count
    }

    /// The school read as the check reads it, with the sign-in kept on this device.
    private func read(_ ctx: Context) async -> Read {
        guard let base = Activity.baseURL() else { return .failed }
        let host = base.host?.lowercased() ?? ""
        let cookies = HTTPCookie.requestHeaderFields(with: CookieJar.shared.cookies().filter { Activity.cookie($0, fits: host) })
        if ctx.lms == "d2l" { return await Activity.readBrightspace(base, ctx: ctx, cookies: cookies) }
        return await Activity.readCanvas(base, ctx: ctx, cookies: cookies)
    }

    private func post(_ e: Event) async {
        let c = UNMutableNotificationContent()
        c.title = e.title
        if !e.subtitle.isEmpty { c.subtitle = e.subtitle }
        c.body = e.body
        c.sound = .default
        c.threadIdentifier = "activity.\(e.kind.rawValue)"
        if let u = e.url { c.userInfo = ["url": u] }
        try? await UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: "simpl.activity.\(e.key)", content: c, trigger: nil))
    }

    /// Canvas refused the kept sign-in (it ended): said once, until a check gets through again.
    private func sayStale() async {
        guard !UserDefaults.standard.bool(forKey: Activity.staleKey) else { return }
        UserDefaults.standard.set(true, forKey: Activity.staleKey)
        await post(Event(key: "stale", kind: .announcements, title: "Simpl Courses", subtitle: "", body: "Open Simpl Courses to keep new-activity alerts coming: your Canvas sign-in needs a refresh.", url: "/", when: Date()))
    }

    // MARK: - Reading the stream

    /// A GET with the kept sign-in's cookies: the answer and its status, or nil with no answer.
    private static func get(_ url: URL, cookies: [String: String]) async -> (data: Data, status: Int)? {
        var req = URLRequest(url: url, timeoutInterval: 20)
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.httpShouldHandleCookies = false
        for (k, v) in cookies { req.setValue(v, forHTTPHeaderField: k) }
        guard let answer = try? await URLSession.shared.data(for: req), let http = answer.1 as? HTTPURLResponse else { return nil }
        return (answer.0, http.statusCode)
    }

    /// Canvas: the student's activity stream, its newest forty.
    private static func readCanvas(_ base: URL, ctx: Context, cookies: [String: String]) async -> Read {
        guard var comps = URLComponents(url: base.appendingPathComponent("api/v1/users/self/activity_stream"), resolvingAgainstBaseURL: false) else { return .failed }
        comps.queryItems = [URLQueryItem(name: "per_page", value: "40"), URLQueryItem(name: "only_active_courses", value: "true")]
        guard let url = comps.url, let answer = await get(url, cookies: cookies) else { return .failed }
        if answer.status == 401 || answer.status == 403 { return .refused }
        guard answer.status == 200, let list = json(answer.data) as? [[String: Any]] else { return .failed }
        return .events(list.flatMap { events(from: $0, ctx: ctx) })
    }

    /// (1.7) Brightspace: each chosen course's news, and its grades released with the teacher's comment on each —
    /// the last month's (an older one has long been seen, and what was seen is a list kept short). Who is signed in is
    /// asked first: refused there, the sign-in has ended (a course refusing says only that the course does).
    private static func readBrightspace(_ base: URL, ctx: Context, cookies: [String: String]) async -> Read {
        let lp = ctx.lp ?? "1.50", le = ctx.le ?? "1.82"
        guard let who = await get(base.appendingPathComponent("d2l/api/lp/\(lp)/users/whoami"), cookies: cookies) else { return .failed }
        if who.status == 401 || who.status == 403 { return .refused }
        guard who.status == 200 else { return .failed }
        let now = Date()
        let since = now.addingTimeInterval(-30 * 86_400)
        var out: [Event] = []
        for (ou, course) in ctx.courses.sorted(by: { $0.key < $1.key }) {
            async let newsRead = get(base.appendingPathComponent("d2l/api/le/\(le)/\(ou)/news/"), cookies: cookies)
            async let gradesRead = get(base.appendingPathComponent("d2l/api/le/\(le)/\(ou)/grades/values/myGradeValues/"), cookies: cookies)
            let (news, grades) = await (newsRead, gradesRead)
            if let n = news, n.status == 200, let list = json(n.data) as? [[String: Any]] {
                for item in list {
                    guard let id = str(item["Id"]), let num = Int(id), (item["IsPublished"] as? Bool) != false, (item["IsHidden"] as? Bool) != true else { continue }
                    let when = date(item["StartDate"]) ?? date(item["CreatedDate"]) ?? .distantPast
                    guard when >= since, when <= now else { continue }
                    let title = (item["Title"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? "New announcement"
                    // (the interface's own address for it: the announcement's screen, its id in the interface's news ids)
                    out.append(Event(key: "d2l.news:\(ou):\(id)", kind: .announcements, title: title, subtitle: course, body: plain(rich(item["Body"])), url: "/courses/\(ou)/discussion_topics/\(3_000_000_000 + num)", when: when))
                }
            }
            if let g = grades, g.status == 200, let list = json(g.data) as? [[String: Any]] {
                for v in list {
                    guard let gid = str(v["GradeObjectIdentifier"]) else { continue }
                    let when = date(v["ReleasedDate"]) ?? date(v["LastModified"])
                    if let w = when, w < since { continue }
                    let name = (v["GradeObjectName"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? "Your work"
                    let shown = (v["DisplayedGrade"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
                    let points = str(v["PointsNumerator"]).map { n in str(v["PointsDenominator"]).map { "\(n) / \($0)" } ?? n }
                    let text = points.map { p in shown.isEmpty || shown == p ? p : "\(p) · \(shown)" } ?? shown
                    guard !text.isEmpty else { continue }
                    out.append(Event(key: "d2l.grade:\(ou):\(gid):\(text)", kind: .grades, title: "\(name) graded", subtitle: course, body: text, url: "/courses/\(ou)/grades", when: when ?? now))
                    let said = plain(rich(v["Comments"]), max: 140)
                    if !said.isEmpty {
                        out.append(Event(key: "d2l.comment:\(ou):\(gid):\(stamp(said))", kind: .comments, title: "Feedback on \(name)", subtitle: course, body: "“\(said)”", url: "/courses/\(ou)/grades", when: when ?? now))
                    }
                }
            }
        }
        return .events(out)
    }

    /// Brightspace's rich text ({ Text, Html }): the HTML, or the plain text when there is no HTML.
    private static func rich(_ v: Any?) -> String? {
        guard let r = v as? [String: Any] else { return nil }
        if let h = r["Html"] as? String, !h.isEmpty { return h }
        return r["Text"] as? String
    }

    /// The same short stamp for the same words on every launch (Swift's own hash differs from one launch to the next).
    private static func stamp(_ s: String) -> String {
        var h: UInt64 = 0xcbf2_9ce4_8422_2325
        for b in s.utf8 { h = (h ^ UInt64(b)) &* 0x0000_0100_0000_01b3 }
        return String(h, radix: 36)
    }

    private static func baseURL() -> URL? {
        if let dev = AppSession.devBaseURL { return dev }
        guard let host = UserDefaults.standard.string(forKey: "canvasHost"), !host.isEmpty else { return nil }
        return URL(string: "https://\(host)/")
    }

    private static func context() -> Context {
        guard let data = UserDefaults.standard.data(forKey: contextKey), let c = try? JSONDecoder().decode(Context.self, from: data) else { return Context(me: nil, courses: [:]) }
        return c
    }

    private static func cookie(_ c: HTTPCookie, fits host: String) -> Bool {
        let d = c.domain.lowercased()
        let bare = d.hasPrefix(".") ? String(d.dropFirst()) : d
        return host == bare || host.hasSuffix(".\(bare)")
    }

    /// Canvas's JSON, its `while(1);` guard taken off.
    private static func json(_ data: Data) -> Any? {
        let guardBytes = Data("while(1);".utf8)
        let body = data.starts(with: guardBytes) ? data.dropFirst(guardBytes.count) : data[...]
        return try? JSONSerialization.jsonObject(with: Data(body))
    }

    private static func str(_ v: Any?) -> String? {
        if let s = v as? String { return s }
        if let n = v as? NSNumber { return n.stringValue }
        return nil
    }

    private static let isoFull: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    private static let isoPlain = ISO8601DateFormatter()

    private static func date(_ v: Any?) -> Date? {
        guard let s = str(v) else { return nil }
        return isoFull.date(from: s) ?? isoPlain.date(from: s)
    }

    /// HTML to a line of plain words.
    private static func plain(_ html: Any?, max: Int = 160) -> String {
        var s = str(html) ?? ""
        s = s.replacingOccurrences(of: "<[^>]+>", with: " ", options: .regularExpression)
        for (k, v) in ["&nbsp;": " ", "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": "\"", "&#39;": "'", "&rsquo;": "’", "&lsquo;": "‘", "&ldquo;": "“", "&rdquo;": "”", "&hellip;": "…"] {
            s = s.replacingOccurrences(of: k, with: v)
        }
        s = s.replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression).trimmingCharacters(in: .whitespacesAndNewlines)
        return s.count > max ? String(s.prefix(max - 1)) + "…" : s
    }

    /// What one stream item has to say, in the Notifications screen's own terms (store.js notifications()).
    private static func events(from a: [String: Any], ctx: Context) -> [Event] {
        guard let id = str(a["id"]), let type = a["type"] as? String else { return [] }
        let courseId = str(a["course_id"])
        // only the courses chosen (a message, which has no course, always counts)
        if let cid = courseId, !ctx.courses.isEmpty, ctx.courses[cid] == nil { return [] }
        let course = courseId.flatMap { ctx.courses[$0] } ?? (a["context_name"] as? String ?? "")
        let updated = str(a["updated_at"]) ?? str(a["created_at"]) ?? ""
        let when = date(updated) ?? Date.distantPast
        let url = a["html_url"] as? String
        let unread = (a["read_state"] as? Bool) == false
        let title = (a["title"] as? String).flatMap { $0.isEmpty ? nil : $0 }
        switch type {
        case "Announcement":
            return [Event(key: "ann:\(id)", kind: .announcements, title: title ?? "New announcement", subtitle: course, body: plain(a["message"]), url: url, when: when)]
        case "Submission":
            var out: [Event] = []
            let assignment = a["assignment"] as? [String: Any]
            let name = (assignment?["name"] as? String) ?? (title ?? "Your work").replacingOccurrences(of: "\\s+graded\\b.*$", with: "", options: .regularExpression)
            // a score the teacher has not posted yet is not news (posted_at null; absent means posted)
            let posted = !(a.keys.contains("posted_at") && a["posted_at"] is NSNull)
            let score = str(a["score"]), grade = (a["grade"] as? String).flatMap { $0.isEmpty ? nil : $0 }
            if posted, let shown = score ?? grade {
                let possible = str(assignment?["points_possible"]).map { " / \($0)" } ?? ""
                let letter = grade.flatMap { g in score.flatMap { g != $0 ? " · \(g)" : nil } } ?? ""
                out.append(Event(key: "grade:\(id):\(shown)", kind: .grades, title: "\(name) graded", subtitle: course, body: "\(shown)\(possible)\(letter)", url: url, when: when))
            }
            let comments = (a["submission_comments"] as? [[String: Any]] ?? []).filter { !((str($0["comment"]) ?? "").isEmpty) }
            if let last = comments.last, ctx.me == nil || str(last["author_id"]) != ctx.me {
                let who = (last["author_name"] as? String) ?? "Your instructor"
                let stamp = str(last["created_at"]) ?? str(last["id"]) ?? updated
                out.append(Event(key: "comment:\(id):\(stamp)", kind: .comments, title: "\(who) commented on \(name)", subtitle: course, body: "“\(plain(last["comment"], max: 140))”", url: url, when: date(stamp) ?? when))
            }
            return out
        case "Conversation":
            guard unread else { return [] }
            let cid = str(a["conversation_id"])
            return [Event(key: "msg:\(id):\(updated)", kind: .messages, title: title ?? "New message", subtitle: "Inbox", body: plain(a["message"], max: 140), url: cid.map { "/conversations?id=\($0)" } ?? url, when: when)]
        case "DiscussionTopic":
            guard unread else { return [] }
            let words = plain(a["message"], max: 120)
            return [Event(key: "disc:\(id):\(updated)", kind: .discussions, title: title ?? "Discussion", subtitle: course, body: words.isEmpty ? "New posts in this discussion." : words, url: url, when: when)]
        default:
            return []
        }
    }
}

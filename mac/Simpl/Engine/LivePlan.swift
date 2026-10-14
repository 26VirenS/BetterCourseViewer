import Foundation

// (1.3.80) Live checks: the screen on show asks Canvas again, now and then, whether anything on it is new, and puts in
// place only what changed (Engine/LiveRefresh.swift does that). What decides when — and the fingerprint and the
// comparison that say "nothing new" — needs no window: it is here, Foundation only, and checked on its own by
// scripts/dev/swift-checks/main.swift.

/// When the screen on show is checked next: once a minute (a course's pages every ten minutes), never two checks at
/// once, held back while the student is in the middle of something, and after checks that did not work, less often for a
/// while. A plan is the screen's own: a screen shown afresh starts one from when its data was last read.
struct LivePlan: Equatable {
    /// How often a screen is checked: the one-minute check, or the ten-minute one of a course's pages (whose data the
    /// engine keeps ten minutes at most).
    enum Pace: String, Equatable {
        case minute, course
    }

    /// What the plan says to do now.
    enum Decision: Equatable {
        /// Check now.
        case check
        /// Nothing due yet: the next check is this many seconds away.
        case wait(TimeInterval)
        /// A check is due, but something holds it back (the window put away, the student typing, a sheet up …): it
        /// runs once that ends.
        case hold(String)
        /// A check is due while one is still under way (this screen's or another window's): this one is skipped.
        case busy
    }

    /// The seconds between checks.
    let every: TimeInterval
    /// When what the screen shows was last known to be Canvas's word: its last check, or the read it opened on.
    private(set) var last: Date
    /// Checks that did not work, one after another (one that works sets it back to none).
    private(set) var failures = 0
    /// This screen's check is under way.
    private(set) var running = false

    init(every: TimeInterval, since: Date) {
        self.every = max(1, every)
        last = since
    }

    /// How long after the last check the next one is due: `every`; after checks that did not work, twice as long for each,
    /// five times `every` at most — the one-minute check's 2, 4, then 5 minutes.
    var wait: TimeInterval {
        guard failures > 0 else { return every }
        return min(every * pow(2, Double(min(failures, 16))), every * 5)
    }

    /// When the next check is due.
    var due: Date { last.addingTimeInterval(wait) }

    /// What to do at `now`: wait for the check that is not due yet; hold back one that is due while anything in `holds`
    /// is so (the first is said); skip it while a check is under way anywhere (`busy`); else check. A window put away for
    /// longer than the interval comes back to a check that is due: it runs at once.
    func decide(now: Date, holds: [String], busy: Bool) -> Decision {
        let left = due.timeIntervalSince(now)
        if left > 0 { return .wait(left) }
        if let why = holds.first { return .hold(why) }
        return running || busy ? .busy : .check
    }

    /// The check has begun.
    mutating func began() {
        running = true
    }

    /// The check is over: it worked (what it found is in place, or there was nothing new) or it did not (the next one
    /// waits longer).
    mutating func ended(worked: Bool, at now: Date) {
        running = false
        last = now
        failures = worked ? 0 : failures + 1
    }

    /// The check found something new while something came up that holds it back (a menu opened, a key pressed): nothing
    /// was put in place, and it is due again at once — it runs as soon as that ends.
    mutating func held() {
        running = false
    }

    /// The seconds between checks at each pace: a minute and ten minutes, or as the screenshot suite asks
    /// (`-SimplLiveEvery 5`: every 5 seconds, a course's pages every 50 unless `-SimplLiveCourseEvery` says otherwise).
    static func interval(_ pace: Pace, every: Double?, courseEvery: Double?) -> TimeInterval {
        let base = every.flatMap { $0 > 0 ? $0 : nil }
        switch pace {
        case .minute:
            return base ?? 60
        case .course:
            return courseEvery.flatMap { $0 > 0 ? $0 : nil } ?? base.map { $0 * 10 } ?? 600
        }
    }

    /// The console's line for a check — the screen's name, the parts that changed, how many reads it made of the school
    /// and how many times it drew the screen; never a name or a title of the student's: "live: dashboard unchanged · 0
    /// reads · 0 redraws", "live: dashboard updated counters,list · 7 reads · 1 redraw".
    static func line(_ screen: String, parts: [String], reads: Int?, draws: Int?) -> String {
        var s = "live: \(screen) " + (parts.isEmpty ? "unchanged" : "updated " + parts.joined(separator: ","))
        if let reads { s += " · \(reads) \(reads == 1 ? "read" : "reads")" }
        if let draws { s += " · \(draws) \(draws == 1 ? "redraw" : "redraws")" }
        return s
    }

    /// The screenshot suite's change to the mock school, behind the engine's back (`-SimplLiveChange done:8`: the soonest
    /// work marked done 8 seconds after the screen was first shown; `grade`, `task` as the engine has them): what, and
    /// how many seconds on. Nil for anything else.
    static func devChange(_ raw: String?) -> (what: String, after: TimeInterval)? {
        guard let raw, !raw.isEmpty else { return nil }
        let parts = raw.split(separator: ":", maxSplits: 1).map(String.init)
        let what = parts[0].trimmingCharacters(in: .whitespaces)
        guard !what.isEmpty, what.allSatisfy({ $0.isLetter }) else { return nil }
        let after: Double? = parts.count > 1 ? Double(parts[1].trimmingCharacters(in: .whitespaces)) : 8
        guard let after, after >= 0 else { return nil }
        return (what, after)
    }
}

/// The fingerprint the page's engine gives an answer (native-app.js `fingerprint`): the length of its JSON in UTF-16
/// units, then cyrb53 of it, both in base 36 — worked out here from the JSON the page sent, so a screen knows the
/// fingerprint of the answer it shows, and its first check asks with it (`known`): the same answer comes back as
/// "unchanged", with nothing to send, decode or draw.
enum LiveFingerprint {
    static func of(_ text: String) -> String {
        var h1: UInt32 = 0xdeadbeef
        var h2: UInt32 = 0x41c6ce57
        var length = 0
        for unit in text.utf16 {
            let c = UInt32(unit)
            h1 = (h1 ^ c) &* 2654435761
            h2 = (h2 ^ c) &* 1597334677
            length += 1
        }
        h1 = ((h1 ^ (h1 >> 16)) &* 2246822507) ^ ((h2 ^ (h2 >> 13)) &* 3266489909)
        h2 = ((h2 ^ (h2 >> 16)) &* 2246822507) ^ ((h1 ^ (h1 >> 13)) &* 3266489909)
        let value = (UInt64(h2 & 2097151) << 32) | UInt64(h1)
        return String(length, radix: 36) + "-" + String(value, radix: 36)
    }
}

/// Whether two answers say the same, each written out as JSON with its keys in order: what a check found and what the
/// screen shows, when the page could not say so by fingerprint (a screen's first check of an answer kept from before).
/// What the app does not read of an answer is not compared — a change only there changes nothing on screen.
enum LiveCompare {
    static func same(_ a: Any, _ b: Any) -> Bool {
        guard let x = a as? any Encodable, let y = b as? any Encodable, let dx = json(x), let dy = json(y) else { return false }
        return dx == dy
    }

    private static func json(_ value: any Encodable) -> Data? {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        return try? encoder.encode(value)
    }
}

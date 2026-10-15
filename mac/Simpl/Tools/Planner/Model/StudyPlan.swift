import Foundation

// (1.3.92) The Study Planner's planner, Foundation only so it can be checked on its own (scripts/dev/swift-checks):
// the student's upcoming work (assignments, quizzes, exams, discussions, their own tasks, each with its due date, its
// points and its course), their settings (hours a day, the block's length, the time of day, how many days before the
// due date to finish, which courses) and today in; study blocks out — "45 min · PHYS 008 · Quiz 3" — each on a day,
// at a time, never past its work's due date, with each piece of work's progress. Deterministic: the same work,
// settings, done blocks and moment give the same plan. The screens are StudyPlanner.swift's.

/// A piece of work to study for, as the engine lists it (reminders: three weeks of open work with due dates; the
/// Dashboard's list for its points and colour).
struct StudyItem: Hashable, Identifiable {
    let id: String
    let title: String
    /// The course's short name ("PHYS 008"), or "" for a task of the student's own.
    let course: String
    let color: String?
    /// "Assignment", "Quiz", "Discussion", "My task"… as the engine says it.
    let kind: String
    let points: Double?
    let due: Date

    /// What kind of work it is for the planner: an exam counts three times, a quiz one and a half, a discussion half.
    /// A midterm, a final, a test or an exam by name is an exam whatever Canvas calls it (a 100-point "Midterm 1"
    /// assignment).
    var factor: Double { StudyWeight.factor(kind: kind, title: title) }

    /// Its study need, in weight: its points (10 where it has none, as a plain assignment's worth) times its kind's factor.
    var weight: Double { (points ?? StudyWeight.pointsWhenNone) * factor }
}

enum StudyWeight {
    /// The points a piece of work without any counts as, so work is weighted alike when none has points.
    static let pointsWhenNone = 10.0
    /// Minutes of study a point of weight is worth: ten points of a plain assignment, 45 minutes — one block.
    static let minutesPerWeight = 4.5
    /// The most blocks one piece of work is given (a 200-point final would otherwise take every hour of three weeks).
    static let mostBlocks = 24

    static func factor(kind: String, title: String) -> Double {
        let k = kind.lowercased()
        let words = Set(title.lowercased().split(whereSeparator: { !$0.isLetter }).map(String.init))
        let examWords: Set<String> = ["exam", "exams", "midterm", "midterms", "final", "finals", "test", "tests"]
        if k.contains("exam") || !words.isDisjoint(with: examWords) { return 3 }
        if k.contains("quiz") { return 1.5 }
        if k.contains("discussion") { return 0.5 }
        return 1
    }

    /// How many blocks a piece of work needs at a block length.
    static func blocks(weight: Double, blockMinutes: Int) -> Int {
        guard blockMinutes > 0 else { return 1 }
        let minutes = weight * minutesPerWeight
        return min(mostBlocks, max(1, Int((minutes / Double(blockMinutes)).rounded(.up))))
    }
}

/// When in the day the blocks go.
enum StudyTime: String, CaseIterable {
    case morning, afternoon, evening

    var label: String {
        switch self {
        case .morning: return "Morning"
        case .afternoon: return "Afternoon"
        case .evening: return "Evening"
        }
    }

    /// The hour the day's first block starts.
    var startHour: Int {
        switch self {
        case .morning: return 9
        case .afternoon: return 14
        case .evening: return 19
        }
    }
}

/// The student's settings.
struct StudySettings: Equatable {
    /// Hours a day, Sunday first (Calendar's weekday 1 to 7): two on a weekday, three at the weekend.
    var hours: [Double] = StudySettings.defaultHours
    /// 25, 45, 60 or 90.
    var blockMinutes = 45
    var timeOfDay = StudyTime.afternoon
    /// Finish this many days before the due date.
    var finishDaysBefore = 1
    /// Courses left out (by short name); a task of the student's own is never left out.
    var excludedCourses: Set<String> = []

    static let defaultHours: [Double] = [3, 2, 2, 2, 2, 2, 3]
    static let blockChoices = [25, 45, 60, 90]
    /// The minutes between two blocks of a day.
    static let gapMinutes = 10

    /// The hours on a day (Calendar's weekday, 1 Sunday to 7 Saturday).
    func hours(weekday: Int) -> Double {
        let i = max(0, min(6, weekday - 1))
        return hours.count == 7 ? hours[i] : StudySettings.defaultHours[i]
    }

    /// The settings as one line, for keeping: "3,2,2,2,2,2,3|45|afternoon|1|PHYS 008;CHEM 001".
    var text: String {
        [hours.map { StudySettings.number($0) }.joined(separator: ","), String(blockMinutes), timeOfDay.rawValue, String(finishDaysBefore),
         excludedCourses.sorted().joined(separator: ";")].joined(separator: "|")
    }

    init() {}

    init?(text: String) {
        let parts = text.components(separatedBy: "|")
        guard parts.count >= 4 else { return nil }
        let h = parts[0].split(separator: ",").compactMap { Double($0) }
        guard h.count == 7, h.allSatisfy({ $0 >= 0 && $0 <= 24 }), let b = Int(parts[1]), StudySettings.blockChoices.contains(b),
              let t = StudyTime(rawValue: parts[2]), let f = Int(parts[3]), f >= 0, f <= 14 else { return nil }
        hours = h
        blockMinutes = b
        timeOfDay = t
        finishDaysBefore = f
        if parts.count > 4 { excludedCourses = Set(parts[4].split(separator: ";").map(String.init).filter { !$0.isEmpty }) }
    }

    private static func number(_ v: Double) -> String {
        v == v.rounded() ? String(Int(v)) : String(format: "%.2g", v)
    }
}

/// A block of study: a piece of work, on a day, at a time, for the block's minutes.
struct StudyBlock: Hashable, Identifiable {
    /// "<item id>:<n>", the n-th block of its work counting from the ones done.
    let id: String
    let itemId: String
    let title: String
    let course: String
    let color: String?
    let kind: String
    let start: Date
    let minutes: Int
    /// This block's number among its work's, and how many its work has in all (done ones counted).
    let number: Int
    let of: Int

    var end: Date { start.addingTimeInterval(Double(minutes) * 60) }

    /// "45 min · PHYS 008 · Quiz 3"
    var line: String { [String(minutes) + " min", course, title].filter { !$0.isEmpty }.joined(separator: " · ") }
}

/// How a piece of work stands in the plan: how many blocks it needs, how many are done, planned, and could not be fitted.
struct StudyProgress: Hashable, Identifiable {
    let item: StudyItem
    let blocks: Int
    let done: Int
    let planned: Int
    /// Blocks there was no room for before the due date (the hours too few, or the due date too near).
    let unplanned: Int

    var id: String { item.id }
    var remaining: Int { max(0, blocks - done) }
    /// "2 of 5 blocks done · 3 planned"
    var line: String {
        var s = "\(done) of \(blocks) block\(blocks == 1 ? "" : "s") done"
        if planned > 0 { s += " · \(planned) planned" }
        if unplanned > 0 { s += " · \(unplanned) won't fit" }
        return s
    }
}

struct StudyPlan {
    let blocks: [StudyBlock]
    let progress: [StudyProgress]
    /// The first day of the plan (today) and its last.
    let from: Date
    let to: Date

    static let empty = StudyPlan(blocks: [], progress: [], from: Date.distantPast, to: Date.distantPast)

    func blocks(on day: Date, calendar: Calendar) -> [StudyBlock] {
        blocks.filter { calendar.isDate($0.start, inSameDayAs: day) }
    }

    var minutesPlanned: Int { blocks.reduce(0) { $0 + $1.minutes } }
    var unplanned: Int { progress.reduce(0) { $0 + $1.unplanned } }
}

enum StudyPlanner {
    /// The plan: each piece of work's blocks spread over the days from today to the day it should be finished by, filling
    /// each day's hours by priority — the earliest due first, then the heaviest — and never past a due date. A block done
    /// (`done`: blocks done per work) is taken off that work's need first.
    static func plan(_ items: [StudyItem], settings: StudySettings, done: [String: Int], now: Date, calendar: Calendar) -> StudyPlan {
        let today = calendar.startOfDay(for: now)
        let open = items.filter { $0.due > now && (($0.course.isEmpty) || !settings.excludedCourses.contains($0.course)) }
            .sorted { a, b in
                if a.due != b.due { return a.due < b.due }
                if a.weight != b.weight { return a.weight > b.weight }
                return a.id < b.id
            }
        guard !open.isEmpty else { return StudyPlan(blocks: [], progress: [], from: today, to: today) }
        let block = settings.blockMinutes
        let step = Double(block + StudySettings.gapMinutes) * 60

        /// The moment a day's first block starts: the time of day chosen — today, no earlier than now (to the next
        /// quarter hour).
        func dayStart(_ day: Date) -> Date {
            var at = calendar.date(bySettingHour: settings.timeOfDay.startHour, minute: 0, second: 0, of: day) ?? day
            if calendar.isDate(day, inSameDayAs: today), at < now {
                let quarter = 15.0 * 60
                at = Date(timeIntervalSinceReferenceDate: (now.timeIntervalSinceReferenceDate / quarter).rounded(.up) * quarter)
            }
            return at
        }

        struct Work {
            let item: StudyItem
            let blocks: Int
            let done: Int
            let need: Int
            let window: [Date]
            var placed = 0
        }
        var works: [Work] = []
        var last = today
        for it in open {
            let blocks = StudyWeight.blocks(weight: it.weight, blockMinutes: block)
            let had = min(blocks, max(0, done[it.id] ?? 0))
            // the days it may be studied on: from today to `finishDaysBefore` days before it is due — today at least,
            // while it is still ahead — and only days a block can end before it is due
            var end = calendar.date(byAdding: .day, value: -settings.finishDaysBefore, to: calendar.startOfDay(for: it.due)) ?? today
            if end < today { end = today }
            var window: [Date] = []
            var d = today
            while d <= end {
                if dayStart(d).addingTimeInterval(Double(block) * 60) <= it.due { window.append(d) }
                guard let next = calendar.date(byAdding: .day, value: 1, to: d) else { break }
                d = next
            }
            if let w = window.last, w > last { last = w }
            works.append(Work(item: it, blocks: blocks, done: had, need: blocks - had, window: window))
        }

        var out: [StudyBlock] = []
        var day = today
        while day <= last {
            let weekday = calendar.component(.weekday, from: day)
            var room = Int((settings.hours(weekday: weekday) * 60 / Double(block)).rounded(.down))
            var onDay = 0
            let first = dayStart(day)
            for i in works.indices where room > 0 && works[i].placed < works[i].need {
                guard let j = works[i].window.firstIndex(where: { calendar.isDate($0, inSameDayAs: day) }) else { continue }
                // its share by this day: its blocks spread evenly over its window, what a full day held back caught up
                let target = Int((Double(works[i].need) * Double(j + 1) / Double(works[i].window.count)).rounded(.up))
                var want = min(target - works[i].placed, room)
                while want > 0 {
                    let start = first.addingTimeInterval(Double(onDay) * step)
                    // (the day's later blocks may run past a due time its first did not: this work stops here for today)
                    guard start.addingTimeInterval(Double(block) * 60) <= works[i].item.due else { break }
                    let w = works[i]
                    let n = w.done + w.placed + 1
                    out.append(StudyBlock(id: "\(w.item.id):\(n)", itemId: w.item.id, title: w.item.title, course: w.item.course, color: w.item.color,
                                          kind: w.item.kind, start: start, minutes: block, number: n, of: w.blocks))
                    works[i].placed += 1
                    onDay += 1
                    room -= 1
                    want -= 1
                }
            }
            guard let next = calendar.date(byAdding: .day, value: 1, to: day) else { break }
            day = next
        }
        let progress = works.map { StudyProgress(item: $0.item, blocks: $0.blocks, done: $0.done, planned: $0.placed, unplanned: $0.need - $0.placed) }
        return StudyPlan(blocks: out, progress: progress, from: today, to: last)
    }
}

/// The blocks done, kept as one line: "<item id>:<count>,<item id>:<count>".
enum StudyDone {
    static func parse(_ text: String?) -> [String: Int] {
        var out: [String: Int] = [:]
        for part in (text ?? "").split(separator: ",") {
            guard let colon = part.lastIndex(of: ":"), let n = Int(part[part.index(after: colon)...]), n > 0 else { continue }
            let id = String(part[..<colon]).trimmingCharacters(in: .whitespaces)
            if !id.isEmpty { out[id] = n }
        }
        return out
    }

    static func text(_ done: [String: Int]) -> String {
        done.filter { $0.value > 0 }.sorted { $0.key < $1.key }.map { "\($0.key):\($0.value)" }.joined(separator: ",")
    }
}

/// The plan as a calendar file (RFC 5545), for Add to Calendar: an event a block, in the Mac's own time written as UTC.
enum StudyICS {
    static func text(_ blocks: [StudyBlock], made: Date = Date()) -> String {
        var lines = ["BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Simpl//Study Planner//EN", "CALSCALE:GREGORIAN", "METHOD:PUBLISH", "X-WR-CALNAME:Study plan"]
        let stamp = utc(made)
        for b in blocks {
            lines += ["BEGIN:VEVENT", "UID:simpl-study-\(escape(b.id))@simpl", "DTSTAMP:\(stamp)", "DTSTART:\(utc(b.start))", "DTEND:\(utc(b.end))",
                      "SUMMARY:\(escape(summary(b)))", "DESCRIPTION:\(escape("Study block \(b.number) of \(b.of) for \(b.title). Planned by Simpl."))",
                      "END:VEVENT"]
        }
        lines.append("END:VCALENDAR")
        return lines.joined(separator: "\r\n") + "\r\n"
    }

    /// "Study: PHYS 008 · Quiz 3"
    static func summary(_ b: StudyBlock) -> String {
        "Study: " + [b.course, b.title].filter { !$0.isEmpty }.joined(separator: " · ")
    }

    private static let utcFormat: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyyMMdd'T'HHmmss'Z'"
        return f
    }()

    static func utc(_ d: Date) -> String { utcFormat.string(from: d) }

    /// A text as an iCalendar value: backslashes, commas, semicolons and line breaks escaped.
    static func escape(_ s: String) -> String {
        s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: ";", with: "\\;").replacingOccurrences(of: ",", with: "\\,")
            .replacingOccurrences(of: "\r\n", with: "\\n").replacingOccurrences(of: "\n", with: "\\n")
    }
}

/// The engine's "10 pts" as a number (nil: none).
enum StudyPoints {
    static func parse(_ text: String?) -> Double? {
        guard let text else { return nil }
        let digits = text.replacingOccurrences(of: ",", with: "").split(whereSeparator: { !($0.isNumber || $0 == ".") }).first
        guard let digits, let v = Double(digits), v.isFinite, v >= 0 else { return nil }
        return v
    }
}

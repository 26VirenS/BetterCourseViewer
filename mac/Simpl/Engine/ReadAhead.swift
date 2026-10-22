import Foundation
import SwiftUI

/// (1.3.78) This session's answers, decoded and held in memory by their read (its call and its arguments): a screen
/// opened again — or one read ahead — shows its whole content at once, with nothing decoded or asked for first. An
/// answer is fresh for a minute, and until something is done that may change what the screens show (a tick, a hand-in,
/// a post, a setting: any call that is not a read); a screen opened on a stale one shows it at once and asks again,
/// quietly, putting what changed in place. The disk keeps its own sealed copy of each read (AnswerCache), for the next
/// launch; signing out lets go of both.
@MainActor
final class AnswerMemo {
    private struct Entry {
        let value: Any
        let at: Date
        let generation: Int
        /// (1.3.80) Its JSON's fingerprint, as the page's engine gives one (LiveFingerprint): a screen that shows it asks
        /// its first live check with it.
        let fingerprint: String?
    }

    /// How long an answer counts as fresh: a screen opened within it is not asked for again.
    static let freshFor: TimeInterval = 60
    /// The calls whose answers are held: what the screens open on.
    static let held: Set<String> = AnswerCache.reads.union(["todayCounts", "externalTools", "neighbours", "quizAssignment"])
    /// Calls that are neither held nor change anything a screen shows: an address resolved, a search, a tool's launch,
    /// a quiz's start screen, what a message or a hand-in may hold, the settings' own reads, History and Help (read
    /// afresh as each opens). Any call not named here or in `held` makes what is held stale when it succeeds.
    static let neutral: Set<String> = [
        "resolveUrl", "pageFor", "search", "toolLaunch", "quizIntro", "quizRules", "quizAttempt", "quizFeedback",
        "composeContexts", "recipients", "gradeNeeded", "settingsInfo", "settingsExport", "historyExport", "convertInfo",
        "convertFile", "reminders", "watchInfo", "setupInfo", "whatsNew", "appearance", "moreHistory", "moreHelp", "appointments",
    ]
    /// A read's arguments that set a preference as it reads (To Do's grouping and Show Completed, the Dashboard's Hide
    /// Completed, the calendar's view) or ask the page to read afresh (a course's grades): its answer is the plain read's
    /// from then on, so it is held under the read without them.
    private static let settings: [String: Set<String>] = [
        "todo": ["group", "showDone"], "dashList": ["hideDone"], "calView": ["view"], "courseGrades": ["fresh"],
    ]

    private var entries: [String: Entry] = [:]
    private var order: [String] = []
    private(set) var generation = 0
    private var staleSince = Date.distantPast

    /// What an answer is held by: its read's name and its arguments, in a stable order, without the ones that only set a
    /// preference.
    static func key(_ name: String, _ args: [String: Any]) -> String? {
        callKey(name, plainArgs(name, args))
    }

    /// (1.3.80) A read's arguments without the ones that only set a preference: what a live check asks with (it never
    /// sets one — To Do's grouping, the Dashboard's Hide Completed — nor asks the page to read afresh by one).
    static func plainArgs(_ name: String, _ args: [String: Any]) -> [String: Any] {
        var plain = args
        for k in settings[name] ?? [] { plain.removeValue(forKey: k) }
        return plain
    }

    /// The read sets a preference as it reads: it is always asked, never answered from what is held. (A course's grades
    /// asked for afresh are the one exception: the screen always asks so, and a fresh enough answer is that.)
    static func sets(_ name: String, _ args: [String: Any]) -> Bool {
        guard let keys = settings[name] else { return false }
        return args.keys.contains { keys.contains($0) && !(name == "courseGrades" && $0 == "fresh") }
    }

    /// A call exactly as asked (the same read on its way twice is asked once).
    static func callKey(_ name: String, _ args: [String: Any]) -> String? {
        guard JSONSerialization.isValidJSONObject(args),
              let d = try? JSONSerialization.data(withJSONObject: args, options: [.sortedKeys]) else { return nil }
        return name + "|" + (String(data: d, encoding: .utf8) ?? "")
    }

    func value<T>(_ key: String, as type: T.Type) -> (value: T, fresh: Bool)? {
        guard let e = entries[key], let v = e.value as? T else { return nil }
        return (v, fresh(e))
    }

    func isFresh(_ key: String) -> Bool {
        entries[key].map(fresh) ?? false
    }

    /// (1.3.80) The fingerprint of the answer held under `key`, if one is held (fresh or not).
    func fingerprint(_ key: String) -> String? {
        entries[key]?.fingerprint
    }

    private func fresh(_ e: Entry) -> Bool {
        e.generation == generation && Date().timeIntervalSince(e.at) < Self.freshFor
    }

    /// An answer, held as of `generation` (the one when it was asked: an action finished meanwhile leaves it stale), with
    /// (1.3.80) its JSON's fingerprint when that is known.
    func put(_ key: String, _ value: Any, generation g: Int, fingerprint: String? = nil) {
        if entries.updateValue(Entry(value: value, at: Date(), generation: g, fingerprint: fingerprint), forKey: key) != nil {
            order.removeAll { $0 == key }
        }
        order.append(key)
        while order.count > 160 { entries.removeValue(forKey: order.removeFirst()) }
    }

    /// Everything held is stale from here on: still shown at once, asked for again.
    func invalidate() {
        generation += 1
        staleSince = Date()
    }

    /// (`Engine.changed`) Stale too — unless an action made it stale a moment ago: what was read since then follows it.
    func changed() {
        if Date().timeIntervalSince(staleSince) > 2 { invalidate() }
    }

    func clear() {
        entries.removeAll()
        order.removeAll()
        invalidate()
    }
}

/// (1.3.78) Reading ahead: what the student is likely to open next, asked of the page before it is opened, so its screen
/// is whole at once (`Engine.recent`). Once the window has been quiet a moment after it opens: the root places and each
/// course's home. Whenever the pointer rests on a sidebar row, a course's card or a row of work (80 ms, so passing over
/// it asks nothing): what it opens, first in line. Two reads at a time at most, never while a screen is waiting for one
/// of its own, with a breath between them — gentle on the school's servers. A read that is fresh, already on its way or
/// in line is not asked again. Nothing that marks something read (a discussion, a conversation) is read ahead.
@MainActor
final class ReadAhead {
    @MainActor
    struct Ask {
        let key: String
        let run: (Engine) async -> Void

        /// A read, decoded as its screen decodes it (so the screen finds it held).
        static func read<T: Decodable>(_ name: String, _ args: [String: Any], _ type: T.Type) -> Ask? {
            guard let key = AnswerMemo.key(name, args) else { return nil }
            return Ask(key: key) { engine in _ = try? await engine.read(name, args, as: type, quiet: true) }
        }
    }

    /// How many reads ahead may be on their way at once.
    static let width = 2
    private var line: [Ask] = []
    private var running = 0
    private var known: Set<String> = []
    private var launched = false

    /// Asks put in line — `first` (the pointer resting on something) ahead of what is already waiting.
    func add(_ asks: [Ask?], first: Bool, engine: Engine) {
        var fresh: [Ask] = []
        for case let ask? in asks where !engine.memo.isFresh(ask.key) {
            if known.contains(ask.key) {
                // (already in line: moved up when the pointer asks for it)
                if first, let i = line.firstIndex(where: { $0.key == ask.key }) { fresh.append(line.remove(at: i)) }
                continue
            }
            known.insert(ask.key)
            fresh.append(ask)
        }
        line = first ? fresh + line : line + fresh
        pump(engine)
    }

    /// Once after the window opens: when it has been quiet a moment (no screen waiting on a read for 1.2 seconds, 20
    /// seconds at most), the places most opened next.
    func afterLaunch(_ engine: Engine, _ asks: @escaping () -> [Ask?]) {
        guard !launched else { return }
        launched = true
        Task { @MainActor in
            var quiet = 0.0, waited = 0.0
            while quiet < 1.2 && waited < 20 {
                try? await Task.sleep(nanoseconds: 200_000_000)
                waited += 0.2
                quiet = engine.waiting == 0 ? quiet + 0.2 : 0
            }
            self.add(asks(), first: false, engine: engine)
        }
    }

    private func pump(_ engine: Engine) {
        while running < Self.width, !line.isEmpty {
            let ask = line.removeFirst()
            running += 1
            Task(priority: .utility) { @MainActor in
                // (a screen waiting on its own read goes first; five seconds at most)
                var held = 0
                while engine.waiting > 0 && held < 50 {
                    try? await Task.sleep(nanoseconds: 100_000_000)
                    held += 1
                }
                await ask.run(engine)
                try? await Task.sleep(nanoseconds: 120_000_000) // (a breath between reads)
                self.known.remove(ask.key)
                self.running -= 1
                self.pump(engine)
            }
        }
    }
}

extension Engine {
    /// (1.3.78) The screenshot suite's comparison (`-SimplPlain YES`): the screens load as they did before 1.3.78 —
    /// nothing answered from what this session holds, nothing read ahead, the Dashboard not kept — so the walk's times
    /// (Support/OpenClock.swift) can be set beside the same walk's as it is.
    static let plain = UserDefaults.standard.bool(forKey: "SimplPlain")

    /// (1.3.78) This session's answer to a read, if it has one (decoded, in memory), and whether it is still fresh.
    func recent<T>(_ name: String, _ args: [String: Any] = [:], as type: T.Type) -> (value: T, fresh: Bool)? {
        if let primary { return primary.recent(name, args, as: type) }
        guard !Engine.plain, let key = AnswerMemo.key(name, args) else { return nil }
        return memo.value(key, as: type)
    }

    /// (1.3.78) What a place opens on, read ahead (the pointer resting on its row).
    func readAhead(_ place: Place) {
        if let primary { primary.readAhead(place); return }
        guard phase == .native, !Engine.plain else { return }
        ahead.add(Engine.reads(for: place, engine: self), first: true, engine: self)
    }

    /// (1.3.78) What an address opens, read ahead (a course's card, a row of work): its own screen's read; nothing for
    /// a tool, a quiz, a file or a page of another site.
    func readAhead(url: String?) {
        if let primary { primary.readAhead(url: url); return }
        guard phase == .native, !Engine.plain, let url, !url.isEmpty,
              toolLaunch(for: url, title: "") == nil, quizLaunch(for: url, title: "") == nil, fileDownload(for: url) == nil else { return }
        if let route = nativeRoute(for: url, title: "") {
            ahead.add(Engine.reads(for: route, engine: self), first: true, engine: self)
        } else if let tab = tabName(for: url), let place = Engine.place(named: tab) {
            readAhead(place)
        }
    }

    /// (1.3.78) Once the window is up and quiet: the root places and every course's home, so the first visit to each is
    /// whole at once too.
    func readAheadAfterLaunch() {
        if primary != nil || Engine.plain { return }
        // (the screenshot suite's pictures are each of one screen: only its walks read ahead — five copies of the app
        // reading every place at once only slowed the runner)
        let d = UserDefaults.standard
        if d.string(forKey: "SimplShotFile") != nil && d.string(forKey: "SimplWalk") == nil { return }
        ahead.afterLaunch(self) { [weak self] in
            guard let self else { return [] }
            var places: [Place] = [.dashboard, .todo, .calendar, .grades, .notifications]
            if !self.onBrightspace { places.append(.inbox) }
            places.append(.courses)
            places += self.courses.map { Place.home("courses/\($0.id)") }
            let here = self.nav.current.place
            return places.filter { $0 != here }.flatMap { Engine.reads(for: $0, engine: self) }
        }
    }

    /// The reads a place's screen opens on, as it asks them.
    static func reads(for place: Place, engine: Engine) -> [ReadAhead.Ask?] {
        switch place {
        case .dashboard:
            return DashboardView.reads(engine)
        case .courses:
            return [.read("allCourses", AllCoursesData.self), .read("coursesProgress", ["all": true], [String: CourseProgress].self)]
        case .todo:
            return [.read("todo", TodoData.self)]
        case .calendar:
            return [CalendarView.readAhead()]
        case .grades:
            return [.read("grades", GradesData.self)]
        case .notifications:
            return [.read("notifications", NotificationsData.self)]
        case .inbox:
            return [.read("inbox", ["scope": "inbox"], InboxData.self)]
        case .externalTools:
            return [.read("externalTools", ExternalToolsData.self)]
        case .groups:
            return [.read("groups", GroupsData.self)]
        case .home(let ctx):
            return [.read("home", ["ctx": ctx], HomeData.self)]
        case .section(let ctx, let kind):
            return [sectionRead(ctx, kind)]
        case .search, .tools:
            return []
        }
    }

    /// The read a screen pushed on a place opens on (none for a conversation or a discussion: reading one marks it read).
    static func reads(for route: Route, engine: Engine) -> [ReadAhead.Ask?] {
        switch route {
        case .assignment(let course, let id):
            return [.read("assignment", ["course": course, "id": id], AssignmentData.self)]
        case .page(let ctx, let slug):
            return [.read("page", slug.isEmpty ? ["ctx": ctx] : ["ctx": ctx, "slug": slug], PageData.self)]
        case .folder(let ctx, let id, _):
            return [.read("files", id.isEmpty ? ["ctx": ctx] : ["ctx": ctx, "folder": id], FilesData.self)]
        case .course(let id):
            return reads(for: Place.home("courses/\(id)"), engine: engine)
        case .group(let id):
            return reads(for: Place.home("groups/\(id)"), engine: engine)
        case .section(let ctx, let kind):
            return reads(for: Place.section(ctx, kind), engine: engine)
        case .groups:
            return reads(for: Place.groups, engine: engine)
        case .inbox:
            return reads(for: Place.inbox, engine: engine)
        case .notifications:
            return reads(for: Place.notifications, engine: engine)
        case .calendar:
            return reads(for: Place.calendar, engine: engine)
        case .topic, .conversation, .web:
            return []
        }
    }

    /// A course's or a group's section, as its screen asks for it (CourseSections.swift).
    private static func sectionRead(_ ctx: String, _ kind: String) -> ReadAhead.Ask? {
        let c: [String: Any] = ["ctx": ctx]
        switch kind {
        case "announcements": return .read("announcements", c, PostListData.self)
        case "discussions": return .read("discussions", c, DiscussionsData.self)
        case "assignments": return .read("assignments", c, AssignmentsData.self)
        case "quizzes": return .read("quizzes", c, RowsData.self)
        case "syllabus": return .read("syllabus", c, RowsData.self)
        case "modules": return .read("modules", c, ModulesData.self)
        case "pages": return .read("pages", c, PagesData.self)
        case "people": return .read("people", c, PeopleData.self)
        case "files": return .read("files", c, FilesData.self)
        case "grades": return .read("courseGrades", CourseGradesView.firstRead(ContextHome.id(of: ctx)), CourseGradesData.self)
        default: return nil
        }
    }
}

extension ReadAhead.Ask {
    static func read<T: Decodable>(_ name: String, _ type: T.Type) -> ReadAhead.Ask? {
        read(name, [:], type)
    }
}

private struct ReadsAhead: ViewModifier {
    let action: () -> Void
    @State private var resting: Task<Void, Never>?

    func body(content: Content) -> some View {
        content.onHover { inside in
            resting?.cancel()
            resting = nil
            guard inside else { return }
            resting = Task { @MainActor in
                try? await Task.sleep(nanoseconds: 80_000_000) // (passing over it asks nothing)
                guard !Task.isCancelled else { return }
                action()
            }
        }
    }
}

extension View {
    /// (1.3.78) What this opens, read ahead once the pointer has rested on it for 80 ms (ReadAhead).
    func readsAhead(_ action: @escaping () -> Void) -> some View {
        modifier(ReadsAhead(action: action))
    }
}

import Foundation

// What the page's engine answers (extension/content/app/native-app.js), one shape per call. Every field
// the app does not strictly need is optional, so a page a version apart still decodes.

struct WorkFlag: Codable, Hashable {
    var word: String
    var kind: String?
}

struct Person: Codable, Hashable {
    var name: String
    var email: String?
    var pronouns: String?
    var initials: String?
    var avatar: String?
}

struct Snapshot: Codable {
    var me: Person?
    var notifUnread: Int?
    var inboxUnread: Int?
    var dark: Bool?
    var site: String?
    var host: String?
    var version: String?
    var setupDone: Bool?
    /// The school's site: "canvas" or "d2l" (Brightspace, 2.99.22).
    var lms: String?
}

struct WorkRow: Codable, Identifiable, Hashable {
    var id: String
    var title: String
    var sub: String?
    var course: String?
    var color: String?
    var date: String?
    var time: String?
    var done: Bool
    var flag: WorkFlag?
    var url: String?
    var custom: Bool?
    // To Do's own
    var courseName: String?
    var meta: String?
    var when: String?
    var pri: Int?
    var priShort: String?
    var type: String?
    var series: Int?
}

struct Counter: Codable, Identifiable, Hashable {
    var key: String
    var label: String
    var value: Int?
    var tone: String?
    var pending: Bool?
    /// The line under the number ("35 points total", "From 2 courses"): the Mac's Dashboard shows it (Mac 1.2).
    var note: String?
    var id: String { key }
}

struct TodayList: Codable {
    var heading: String
    var note: String?
    var rows: [WorkRow]
    var empty: String?
}

struct LoadRow: Codable, Identifiable, Hashable {
    var id: String
    var code: String
    var color: String?
    var done: Int
    var total: Int
}

struct Today: Codable {
    var dateLine: String?
    var me: Person?
    var notifUnread: Int?
    var counters: [Counter]
    var list: TodayList
    var load: [LoadRow]?
    var idle: Int?
    var hasCourses: Bool?
}

struct TodayCounts: Codable {
    var overdue: Int?
    var graded: Int?
    /// The lines under Overdue's and Graded's numbers (Mac 1.2).
    var overdueNote: String?
    var gradedNote: String?
}

struct SheetRow: Codable, Identifiable, Hashable {
    var title: String
    var sub: String?
    var color: String?
    var url: String?
    var quiet: Bool?
    var key: String?
    var clearable: Bool?
    var id: String { (key ?? "") + "|" + (url ?? "") + "|" + title }
}

struct SheetSection: Codable, Identifiable, Hashable {
    var title: String
    var quiet: Bool?
    var rows: [SheetRow]
    var id: String { title + "\(rows.count)" }
}

struct ItemsSheetData: Codable {
    var title: String
    var note: String?
    var empty: String?
    var sections: [SheetSection]
}

struct CourseRow: Codable, Identifiable, Hashable {
    var id: String
    var code: String
    var name: String?
    var nickname: String?
    var original: String?
    var color: String?
    var score: Double?
    var scoreText: String
    var unread: Int?
    var url: String
}

struct CoursesData: Codable {
    var sub: String?
    var rows: [CourseRow]
    var hidden: String?
    var empty: String?
}

struct CourseProgress: Codable, Hashable {
    var done: Int
    var total: Int
}

struct TodoSection: Codable, Identifiable, Hashable {
    var title: String
    var note: String?
    var rows: [WorkRow]
    var id: String { title }
}

struct NamedColor: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var color: String?
}

struct RepeatChoice: Codable, Identifiable, Hashable {
    var key: String
    var label: String
    var id: String { key }
}

struct TodoData: Codable {
    var sub: String?
    var pct: Int
    var done: Int
    var total: Int
    var group: String
    var showDone: Bool
    var sections: [TodoSection]
    var empty: String?
    var courses: [NamedColor]?
    var repeats: [RepeatChoice]?
}

struct GradeCategory: Codable, Hashable, Identifiable {
    var label: String
    var weight: String?
    var value: String?
    var pct: Double?
    var color: String?
    var id: String { label }
}

struct GradeRow: Codable, Identifiable, Hashable {
    var id: String
    var code: String
    var name: String?
    var color: String?
    var url: String?
    var pct: Double?
    var pctText: String
    var letter: String?
    var points: Double?
    var graded: Int
    var total: Int
    var own: Bool?
    var target: String?
    var cats: [GradeCategory]
}

struct ScaleStep: Codable, Hashable {
    var letter: String
    var min: Double
    var points: Double
}

struct GradeItem: Codable, Identifiable, Hashable {
    var course: String
    var color: String?
    var name: String
    var band: String
    var pct: Double
    var pctText: String
    var score: String
    var url: String?
    var id: String { (url ?? "") + name + course }
}

struct TrendPoint: Codable, Hashable {
    var date: String
    var gpa: Double
}

struct GradesData: Codable {
    var term: String?
    var gpa: Double?
    var goal: Double
    var scale: [ScaleStep]
    var rows: [GradeRow]
    var items: [GradeItem]
    var counts: [String: Int]?
    var trend: [TrendPoint]?
    var minY: Double?
    var maxY: Double?
}

struct CalEvent: Codable, Identifiable, Hashable {
    var id: String
    var title: String
    var date: String?
    var day: String
    var allDay: Bool?
    var time: String?
    var kind: String?
    var done: Bool?
    var missing: Bool?
    var excused: Bool?
    var sub: String?
    var color: String?
    var url: String?
}

struct CalendarChoice: Codable, Identifiable, Hashable {
    var code: String
    var name: String
    var color: String?
    var on: Bool
    var own: Bool?
    var id: String { code }
}

struct CalendarData: Codable {
    var events: [CalEvent]
    var calendars: [CalendarChoice]
    var notice: String?
    var error: String?
}

struct NotifRow: Codable, Identifiable, Hashable {
    var id: String
    var cat: String
    var catLabel: String
    var title: String
    var sub: String?
    var read: Bool
    var url: String?
    var color: String?
}

struct NotifDay: Codable, Identifiable, Hashable {
    var title: String
    var rows: [NotifRow]
    var id: String { title }
}

struct NotifCat: Codable, Identifiable, Hashable {
    var key: String
    var label: String
    var count: Int
    var id: String { key }
}

struct NotificationsData: Codable {
    var total: Int
    var unread: Int
    var days: [NotifDay]
    var cats: [NotifCat]
    var cleared: Int?
}

struct SearchRow: Codable, Identifiable, Hashable {
    var title: String
    var sub: String?
    var url: String
    var color: String?
    var external: Bool?
    var kind: String?
    var id: String { url + title }
}

struct SearchGroup: Codable, Identifiable, Hashable {
    var title: String
    var rows: [SearchRow]
    var id: String { title }
}

struct SearchData: Codable {
    var groups: [SearchGroup]
}

struct ReleaseNote: Codable, Hashable {
    var kind: String
    var title: String
    var body: String?
}

struct Release: Codable, Identifiable, Hashable {
    var version: String
    var date: String?
    var notes: [ReleaseNote]
    var id: String { version }
}

struct WhatsNewData: Codable {
    var version: String?
    var releases: [Release]
}

struct OK: Codable {
    var ok: Bool?
    var done: Bool?
    var overdue: Int?
    var dark: Bool?
    var view: String?
}

/// A screen of the web interface pushed on a native stack, or a native screen pushed by name.
/// A context (`ctx`) is "courses/<id>" or "groups/<id>": the screens a course and a group share.
enum Route: Hashable {
    case web(url: String, title: String)
    case notifications
    case calendar
    case course(id: String)
    case group(id: String)
    case groups
    case section(ctx: String, kind: String)
    case topic(ctx: String, id: String)
    case assignment(course: String, id: String)
    case page(ctx: String, slug: String)
    case folder(ctx: String, id: String, name: String)
    case inbox
    case conversation(id: String)
}

/// The tab bar: four tabs and Search (iOS shows five at most before it folds the rest into More; the
/// Calendar is pushed from Today and To Do instead, 1.1).
enum AppTab: String, Hashable, CaseIterable {
    case today, courses, todo, grades, search
}

import Foundation

// What the page's engine answers for the Mac's Dashboard beyond Today (1.2): native-app.js dashCourses, dashList,
// dashActivity and dashSkyline. Every field the page may leave out is optional.

/// The Dashboard's views, the web Dashboard's three: the courses as cards, the work coming up as a list by day, and
/// Canvas's recent activity. The one chosen is kept on this Mac.
enum DashView: String, CaseIterable, Identifiable {
    case cards, list, activity

    var id: String { rawValue }

    var title: String {
        switch self {
        case .cards: return "Cards"
        case .list: return "List"
        case .activity: return "Activity"
        }
    }

    var help: String {
        switch self {
        case .cards: return "Your courses as cards"
        case .list: return "Everything coming up, day by day"
        case .activity: return "Recent activity in your courses"
        }
    }
}

/// A course card's quick link: a section of the course (its kind, when the app has a screen for it) or an address.
struct DashLink: Codable, Hashable {
    var kind: String?
    var label: String?
    var url: String?
}

/// What is due next in a course.
struct DashNext: Codable, Hashable {
    var title: String
    var when: String?
    var url: String?
}

/// A course on the Dashboard: its names, term and teacher, colour and picture, score, what is due, its unread
/// announcements and its quick links.
struct DashCourse: Codable, Identifiable, Hashable {
    var id: String
    var code: String
    var name: String?
    var sub: String?
    var color: String?
    var image: String?
    var score: Double?
    var scoreText: String?
    var grade: String?
    var unread: Int?
    var dueToday: Int?
    var next: DashNext?
    var links: [DashLink]?
    var url: String
}

struct DashCoursesData: Codable {
    var rows: [DashCourse]
    var empty: String?
}

/// A piece of work in the List: its course and kind, where it stands (every flag), its points and its time.
struct DashRow: Codable, Identifiable, Hashable {
    var id: String
    var title: String
    var course: String?
    var color: String?
    var kind: String?
    var type: String?
    var flags: [WorkFlag]?
    var points: String?
    var due: String?
    var time: String?
    var done: Bool
    var url: String?
    var custom: Bool?

    /// The row as To Do's shape, for the work's own context menu (WorkAction).
    var workRow: WorkRow {
        WorkRow(id: id, title: title, done: done, url: url, custom: custom, type: type)
    }
}

/// A day of the List ("Today", "Tomorrow", "Saturday", "Oct 16") with its date in words.
struct DashDay: Codable, Identifiable, Hashable {
    var title: String
    var date: String?
    var rows: [DashRow]
    var id: String { title + "|" + (date ?? "") }
}

struct DashListData: Codable {
    var hideDone: Bool?
    var done: Int?
    var total: Int?
    var days: [DashDay]
    var empty: String?
}

/// An item of Canvas's recent activity: what it is, where, when, its first words, and whether it is new.
struct DashActivityRow: Codable, Identifiable, Hashable {
    var id: String
    var type: String?
    var kind: String?
    var title: String
    var course: String?
    var color: String?
    var when: String?
    var preview: String?
    var url: String?
    var unread: Bool?
}

struct DashActivityData: Codable {
    var rows: [DashActivityRow]
    var empty: String?
}

/// A window of the grades skyline: an assignment, lit in its grade's colour (A … F) once marked and posted; `free`
/// when it does not count toward the course's total.
struct DashSkyWindow: Codable, Hashable {
    var band: String?
    var free: Bool?
    var title: String?
}

/// A tower of the grades skyline: a course, as tall as its score, with a window per assignment (nil until read).
struct DashSkyCourse: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var code: String?
    var color: String?
    var score: Double?
    var url: String?
    var windows: [DashSkyWindow]?
}

struct DashSkylineData: Codable {
    var courses: [DashSkyCourse]
}

import Foundation

// MARK: - What the widgets show (iPhone 1.9)

/// (iPhone 1.9) What the Home Screen and Lock Screen widgets show: made by the page while the app is open (native-app.js
/// widgetFeed), kept by the app in the app group's container (WidgetShelf), read by the widgets (SimplWidgets). The
/// widgets read nothing from Canvas themselves: between the app's visits their timelines move on by the due times kept
/// here. Compiled into the app and the widgets both.
struct WidgetFeed: Codable, Equatable {
    /// A piece of open work, due at `dueMs` (milliseconds since 1970).
    struct Work: Codable, Equatable, Identifiable {
        var id: String
        var title: String
        var course: String
        var color: String
        var dueMs: Double
        var kind: String
        var url: String?

        var due: Date { Date(timeIntervalSince1970: dueMs / 1000) }
    }

    /// A selected course's score, as Grades shows it.
    struct Course: Codable, Equatable, Identifiable {
        var id: String
        var code: String
        var color: String
        var pct: Double?
        var pctText: String
        var letter: String?
        var url: String?
    }

    var madeMs: Double
    /// The school's name, and its address (the page's origin: "https://school.instructure.com").
    var site: String
    var base: String
    /// The open work due over the next two weeks, soonest first.
    var work: [Work]
    var overdue: Int?
    var gpa: Double?
    var gpaText: String
    var goal: Double?
    var goalText: String
    /// "0.38 below goal", "+0.20 above goal", "On your goal" (Grades' own words).
    var goalLine: String
    var courses: [Course]

    var made: Date { Date(timeIntervalSince1970: madeMs / 1000) }

    /// The work still to come at a moment, soonest first.
    func upcoming(after moment: Date) -> [Work] {
        work.filter { $0.due > moment }
    }

    /// The work still to come on that moment's own day.
    func dueToday(at moment: Date, calendar: Calendar = .current) -> [Work] {
        upcoming(after: moment).filter { calendar.isDate($0.due, inSameDayAs: moment) }
    }

    /// The work still to come within seven days of that moment.
    func dueThisWeek(at moment: Date) -> [Work] {
        let end = moment.addingTimeInterval(7 * 24 * 3600)
        return upcoming(after: moment).filter { $0.due <= end }
    }

    /// The same feed, but for when it was made: two feeds that say the same thing.
    func sameAs(_ other: WidgetFeed) -> Bool {
        var a = self
        a.madeMs = other.madeMs
        return a == other
    }

    /// A link that opens the app on an address of the school's (Engine.openFromOutside): simplcourses://open?url=…
    static func link(_ address: String?) -> URL? {
        guard let address, !address.isEmpty else { return nil }
        var c = URLComponents()
        c.scheme = "simplcourses"
        c.host = "open"
        c.queryItems = [URLQueryItem(name: "url", value: address)]
        return c.url
    }
}

/// The feed's one file, in the container the app and its widgets share.
enum WidgetShelf {
    static let group = "group.com.simplcourses.app"
    static let upNextKind = "SimplUpNext"
    static let gradesKind = "SimplGrades"

    private static var file: URL? {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: group)?.appendingPathComponent("widgets.json")
    }

    static func read() -> WidgetFeed? {
        guard let file, let data = try? Data(contentsOf: file) else { return nil }
        return try? JSONDecoder().decode(WidgetFeed.self, from: data)
    }

    @discardableResult
    static func write(_ feed: WidgetFeed) -> Bool {
        guard let file, let data = try? JSONEncoder().encode(feed) else { return false }
        return (try? data.write(to: file, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])) != nil
    }

    static func erase() {
        guard let file else { return }
        try? FileManager.default.removeItem(at: file)
    }
}

import SwiftUI
import WidgetKit

// MARK: - The timeline

struct FeedEntry: TimelineEntry {
    let date: Date
    let feed: WidgetFeed?
}

/// The feed the app kept, drawn now and — for a widget of what is due (`moving`) — again as each piece of work's due time
/// passes and as each day begins, so the widget moves on by itself between the app's visits. Read again every few hours
/// all the same (the app tells the widgets itself whenever it keeps something new).
struct FeedProvider: TimelineProvider {
    let moving: Bool

    func placeholder(in context: Context) -> FeedEntry {
        FeedEntry(date: Date(), feed: .sample)
    }

    func getSnapshot(in context: Context, completion: @escaping (FeedEntry) -> Void) {
        let kept = WidgetShelf.read()
        completion(FeedEntry(date: Date(), feed: kept ?? (context.isPreview ? .sample : nil)))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<FeedEntry>) -> Void) {
        let now = Date()
        let feed = WidgetShelf.read()
        var dates = [now]
        if moving, let feed {
            let calendar = Calendar.current
            var moves = feed.upcoming(after: now).prefix(12).map { $0.due.addingTimeInterval(1) }
            var day = calendar.startOfDay(for: now)
            for _ in 0..<2 {
                guard let next = calendar.date(byAdding: .day, value: 1, to: day) else { break }
                moves.append(next)
                day = next
            }
            dates += Set(moves).filter { $0 > now }.sorted()
        }
        let entries = dates.prefix(16).map { FeedEntry(date: $0, feed: feed) }
        completion(Timeline(entries: entries, policy: .after(now.addingTimeInterval(4 * 3600))))
    }
}

// MARK: - Pieces both widgets use

extension Color {
    /// A course's colour from its "#rrggbb" (gray where it has none).
    init(courseHex hex: String) {
        var s = hex.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        if s.count == 3 { s = s.map { "\($0)\($0)" }.joined() }
        guard s.count == 6, let v = UInt32(s, radix: 16) else {
            self = .gray
            return
        }
        self = Color(red: Double((v >> 16) & 0xff) / 255, green: Double((v >> 8) & 0xff) / 255, blue: Double(v & 0xff) / 255)
    }
}

enum When {
    /// When a piece of work is due, as short as a widget needs: its time on the day shown ("11:59 PM"), "Tomorrow", its
    /// weekday within the week ("Wed"), else its date ("Nov 24"); `withTime` adds the time to a day ("Wed 11:59 PM").
    static func text(_ due: Date, from now: Date, withTime: Bool = false) -> String {
        let calendar = Calendar.current
        let time = due.formatted(date: .omitted, time: .shortened)
        if calendar.isDate(due, inSameDayAs: now) { return time }
        let days = calendar.dateComponents([.day], from: calendar.startOfDay(for: now), to: calendar.startOfDay(for: due)).day ?? 0
        let day: String
        if days == 1 {
            day = "Tomorrow"
        } else if days > 1 && days < 7 {
            day = due.formatted(.dateTime.weekday(.abbreviated))
        } else {
            day = due.formatted(.dateTime.month(.abbreviated).day())
        }
        return withTime ? "\(day) \(time)" : day
    }
}

extension WidgetFeed {
    /// The app's own screen for an address of the school's ("/" Today, "/grades" Grades).
    func home(_ path: String) -> URL? {
        base.isEmpty ? nil : WidgetFeed.link(base + path)
    }

    /// What the widget gallery shows before the app has made a feed.
    static var sample: WidgetFeed {
        let now = Date().timeIntervalSince1970 * 1000
        let hour = 3_600_000.0
        return WidgetFeed(
            madeMs: now, site: "Canvas", base: "",
            work: [
                Work(id: "1", title: "Lab Report 3", course: "CHEM 101", color: "#FF9500", dueMs: now + 2 * hour, kind: "Assignment", url: nil),
                Work(id: "2", title: "Problem Set 7", course: "MATH 221", color: "#007AFF", dueMs: now + 5 * hour, kind: "Assignment", url: nil),
                Work(id: "3", title: "Reading Response", course: "ENGL 110", color: "#34C759", dueMs: now + 26 * hour, kind: "Discussion", url: nil),
                Work(id: "4", title: "Quiz 5", course: "PSYC 100", color: "#AF52DE", dueMs: now + 50 * hour, kind: "Quiz", url: nil),
            ],
            overdue: 0, gpa: 3.62, gpaText: "3.62", goal: 3.8, goalText: "3.80", goalLine: "0.18 below goal",
            courses: [
                Course(id: "1", code: "CHEM 101", color: "#FF9500", pct: 91.2, pctText: "91.2%", letter: "A−", url: nil),
                Course(id: "2", code: "MATH 221", color: "#007AFF", pct: 88.4, pctText: "88.4%", letter: "B+", url: nil),
                Course(id: "3", code: "ENGL 110", color: "#34C759", pct: 95, pctText: "95%", letter: "A", url: nil),
                Course(id: "4", code: "PSYC 100", color: "#AF52DE", pct: 84.7, pctText: "84.7%", letter: "B", url: nil),
            ]
        )
    }
}

/// A row that opens its address when pressed (a medium widget's rows), or just the row where it has none.
struct Linked<Content: View>: View {
    let url: URL?
    @ViewBuilder let content: Content

    var body: some View {
        if let url {
            Link(destination: url) { content }
        } else {
            content
        }
    }
}

/// The app's mark in a filled circle, at the top of a Home Screen widget.
struct Badge: View {
    let symbol: String
    var size: CGFloat = 28

    var body: some View {
        Image(systemName: symbol)
            .font(.system(size: size * 0.5, weight: .semibold))
            .foregroundStyle(.white)
            .frame(width: size, height: size)
            .background(Circle().fill(Color.accentColor))
            .widgetAccentable()
    }
}

/// Before the app has made a feed (never opened, signed out): what to do.
struct NoFeedView: View {
    let family: WidgetFamily
    let line: String

    var body: some View {
        switch family {
        case .accessoryInline:
            Label("Open Simpl Courses", systemImage: "graduationcap")
        case .accessoryCircular:
            ZStack {
                AccessoryWidgetBackground()
                Image(systemName: "graduationcap.fill")
                    .font(.title3)
            }
        case .accessoryRectangular:
            VStack(alignment: .leading, spacing: 2) {
                Text("Simpl Courses")
                    .font(.headline)
                    .widgetAccentable()
                Text(line)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        default:
            VStack(alignment: .leading, spacing: 6) {
                Badge(symbol: "graduationcap.fill")
                Spacer(minLength: 0)
                Text(line)
                    .font(.footnote.weight(.medium))
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
    }
}

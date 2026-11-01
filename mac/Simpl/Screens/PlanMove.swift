import Foundation

// A plan's block dragged on the week (CalendarView): where it lands. Foundation only, so it is checked on its own by
// scripts/dev/swift-checks/main.swift.

/// A plan moved `days` whole days and `minutes` on the week's grid: the days by the calendar, then the minutes — so a
/// move across a change of the clocks keeps the time of day it was let go at (Saturday 3 PM dragged to Sunday, the
/// first Sunday of November, is Sunday 3 PM, not 2 PM), as a row let go from To Do is planned at its day's time. Up to
/// 1.3.210 the days were added as 24 hours each, and such a move landed an hour off.
enum PlanMove {
    static func moved(_ start: Date, days: Int, minutes: Int, in cal: Calendar) -> Date? {
        cal.date(byAdding: .day, value: days, to: start).flatMap { cal.date(byAdding: .minute, value: minutes, to: $0) }
    }
}

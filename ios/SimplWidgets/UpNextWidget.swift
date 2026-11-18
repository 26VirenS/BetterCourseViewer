import SwiftUI
import WidgetKit

/// (iPhone 1.9) Up Next: what is due next in the selected courses. On the Home Screen, how many are due today (else over
/// the next seven days) and the next ones, each with its course's colour; on the Lock Screen, that count in a circle, the
/// next piece of work in a rectangle, or a line by the time. It moves on by itself as each due time passes.
struct UpNextWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: WidgetShelf.upNextKind, provider: FeedProvider(moving: true)) { entry in
            UpNextView(entry: entry)
        }
        .configurationDisplayName("Up Next")
        .description("What’s due next in your courses.")
        .supportedFamilies([.systemSmall, .systemMedium, .accessoryCircular, .accessoryRectangular, .accessoryInline])
    }
}

/// What the widget says at a moment: the work due later that day, else over the coming week, and what comes next.
private struct UpNext {
    let now: Date
    let today: Bool
    let count: Int
    let next: [WidgetFeed.Work]
    let overdue: Int

    init(_ feed: WidgetFeed, at now: Date) {
        self.now = now
        let day = feed.dueToday(at: now)
        today = !day.isEmpty
        count = today ? day.count : feed.dueThisWeek(at: now).count
        next = feed.upcoming(after: now)
        overdue = feed.overdue ?? 0
    }

    var heading: String { today ? "Due Today" : "Next 7 Days" }
}

struct UpNextView: View {
    let entry: FeedEntry
    @Environment(\.widgetFamily) private var family

    var body: some View {
        content
            .containerBackground(.background, for: .widget)
    }

    @ViewBuilder private var content: some View {
        if let feed = entry.feed {
            let s = UpNext(feed, at: entry.date)
            switch family {
            case .accessoryCircular: circular(s, feed)
            case .accessoryRectangular: rectangular(s, feed)
            case .accessoryInline: inline(s, feed)
            case .systemMedium: medium(s, feed)
            default: small(s, feed)
            }
        } else {
            NoFeedView(family: family, line: "Open Simpl Courses to see what’s due.")
        }
    }

    // MARK: Home Screen

    private func small(_ s: UpNext, _ feed: WidgetFeed) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top) {
                Badge(symbol: "checklist")
                Spacer(minLength: 4)
                count(s, size: 30)
            }
            Text(s.heading)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(Color.accentColor)
                .widgetAccentable()
                .padding(.top, 2)
            Spacer(minLength: 4)
            if s.next.isEmpty {
                AllClear()
            } else {
                VStack(alignment: .leading, spacing: 6) {
                    ForEach(s.next.prefix(2)) { WorkLine(work: $0, now: s.now) }
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .widgetURL(feed.home("/"))
    }

    private func medium(_ s: UpNext, _ feed: WidgetFeed) -> some View {
        HStack(alignment: .top, spacing: 14) {
            VStack(alignment: .leading, spacing: 0) {
                Badge(symbol: "checklist")
                Spacer(minLength: 4)
                count(s, size: 34)
                Text(s.heading)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(Color.accentColor)
                    .widgetAccentable()
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
                if s.overdue > 0 {
                    Text("\(s.overdue) overdue")
                        .font(.caption2.weight(.semibold))
                        .foregroundStyle(.red)
                        .padding(.top, 2)
                }
            }
            .frame(width: 88, alignment: .leading)
            .frame(maxHeight: .infinity, alignment: .topLeading)
            if s.next.isEmpty {
                AllClear()
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
            } else {
                VStack(alignment: .leading, spacing: 8) {
                    ForEach(s.next.prefix(3)) { w in
                        Linked(url: WidgetFeed.link(w.url)) {
                            WorkLine(work: w, now: s.now, wide: true)
                        }
                    }
                    Spacer(minLength: 0)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
        }
        .widgetURL(feed.home("/"))
    }

    private func count(_ s: UpNext, size: CGFloat) -> some View {
        Text("\(s.count)")
            .font(.system(size: size, weight: .bold, design: .rounded))
            .monospacedDigit()
            .contentTransition(.numericText())
            .lineLimit(1)
    }

    // MARK: Lock Screen

    private func circular(_ s: UpNext, _ feed: WidgetFeed) -> some View {
        ZStack {
            AccessoryWidgetBackground()
            VStack(spacing: -1) {
                Text("\(s.count)")
                    .font(.system(size: 24, weight: .semibold, design: .rounded))
                    .monospacedDigit()
                    .widgetAccentable()
                Text(s.today ? "TODAY" : "WEEK")
                    .font(.system(size: 9, weight: .semibold))
                    .foregroundStyle(.secondary)
            }
        }
        .widgetURL(feed.home("/"))
    }

    private func rectangular(_ s: UpNext, _ feed: WidgetFeed) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Label(s.today ? "\(s.count) Due Today" : "Up Next", systemImage: "checklist")
                .font(.headline)
                .widgetAccentable()
                .lineLimit(1)
            if let w = s.next.first {
                Text(w.title)
                    .lineLimit(1)
                Text(w.course.isEmpty ? When.text(w.due, from: s.now, withTime: true) : "\(When.text(w.due, from: s.now, withTime: true)) · \(w.course)")
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            } else {
                Text("All caught up")
                    .lineLimit(1)
                Text("Nothing due soon")
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .widgetURL(WidgetFeed.link(s.next.first?.url) ?? feed.home("/"))
    }

    @ViewBuilder private func inline(_ s: UpNext, _ feed: WidgetFeed) -> some View {
        if let w = s.next.first {
            Label("\(w.title) · \(When.text(w.due, from: s.now))", systemImage: "checklist")
                .widgetURL(WidgetFeed.link(w.url) ?? feed.home("/"))
        } else {
            Label("Nothing due soon", systemImage: "checkmark.circle")
                .widgetURL(feed.home("/"))
        }
    }
}

/// A piece of work: its course's colour as a bar beside its title, and when it is due (with its course).
private struct WorkLine: View {
    let work: WidgetFeed.Work
    let now: Date
    var wide = false

    var body: some View {
        HStack(alignment: .top, spacing: 6) {
            RoundedRectangle(cornerRadius: 1.5)
                .fill(Color(courseHex: work.color))
                .frame(width: 3)
                .widgetAccentable()
            VStack(alignment: .leading, spacing: 1) {
                Text(work.title)
                    .font(wide ? Font.footnote.weight(.semibold) : Font.caption.weight(.semibold))
                    .lineLimit(1)
                Text(sub)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
        .fixedSize(horizontal: false, vertical: true)
    }

    private var sub: String {
        let when = When.text(work.due, from: now, withTime: wide)
        return work.course.isEmpty ? when : "\(when) · \(work.course)"
    }
}

/// Nothing still to come in the next two weeks.
private struct AllClear: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Image(systemName: "checkmark.circle.fill")
                .font(.title3)
                .foregroundStyle(.green)
                .widgetAccentable()
            Text("All caught up")
                .font(.caption.weight(.semibold))
            Text("Nothing due in the next two weeks")
                .font(.caption2)
                .foregroundStyle(.secondary)
                .lineLimit(2)
        }
    }
}

import SwiftUI
import WidgetKit

/// (iPhone 1.9) Grades: the term GPA on the 4.0 scale, as a ring, and each selected course's score beside it. On the
/// Lock Screen, the GPA as a gauge, with the courses in a rectangle, or a line by the time.
struct GradesWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: WidgetShelf.gradesKind, provider: FeedProvider(moving: false)) { entry in
            GradesWidgetView(entry: entry)
        }
        .configurationDisplayName("Grades")
        .description("Your term GPA and each course’s grade.")
        .supportedFamilies([.systemSmall, .systemMedium, .accessoryCircular, .accessoryRectangular, .accessoryInline])
    }
}

struct GradesWidgetView: View {
    let entry: FeedEntry
    @Environment(\.widgetFamily) private var family

    var body: some View {
        content
            .containerBackground(.background, for: .widget)
    }

    @ViewBuilder private var content: some View {
        if let feed = entry.feed {
            switch family {
            case .accessoryCircular: circular(feed)
            case .accessoryRectangular: rectangular(feed)
            case .accessoryInline: inline(feed)
            case .systemMedium: medium(feed)
            default: small(feed)
            }
        } else {
            NoFeedView(family: family, line: "Open Simpl Courses to see your grades.")
        }
    }

    // MARK: Home Screen

    private func small(_ feed: WidgetFeed) -> some View {
        VStack(spacing: 6) {
            GPARing(feed: feed)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            goal(feed)
        }
        .widgetURL(feed.home("/grades"))
    }

    private func medium(_ feed: WidgetFeed) -> some View {
        HStack(spacing: 16) {
            VStack(spacing: 6) {
                GPARing(feed: feed)
                    .frame(maxHeight: .infinity)
                goal(feed)
            }
            .frame(width: 104)
            if feed.courses.isEmpty {
                Text("No courses selected yet")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
            } else {
                VStack(alignment: .leading, spacing: 6) {
                    ForEach(feed.courses.prefix(5)) { c in
                        Linked(url: WidgetFeed.link(c.url)) {
                            CourseLine(course: c)
                        }
                    }
                    Spacer(minLength: 0)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
        }
        .widgetURL(feed.home("/grades"))
    }

    private func goal(_ feed: WidgetFeed) -> some View {
        Text(feed.gpa == nil ? "No grades yet" : feed.goalLine.isEmpty ? "Goal \(feed.goalText)" : feed.goalLine)
            .font(.caption2.weight(.medium))
            .foregroundStyle(.secondary)
            .lineLimit(1)
            .minimumScaleFactor(0.8)
    }

    // MARK: Lock Screen

    @ViewBuilder private func circular(_ feed: WidgetFeed) -> some View {
        if let gpa = feed.gpa {
            Gauge(value: min(max(gpa, 0), 4), in: 0...4) {
                Text("GPA")
            } currentValueLabel: {
                Text(feed.gpaText)
            }
            .gaugeStyle(.accessoryCircular)
            .widgetAccentable()
            .widgetURL(feed.home("/grades"))
        } else {
            ZStack {
                AccessoryWidgetBackground()
                VStack(spacing: 0) {
                    Text("–")
                        .font(.system(size: 22, weight: .semibold, design: .rounded))
                    Text("GPA")
                        .font(.system(size: 9, weight: .semibold))
                        .foregroundStyle(.secondary)
                }
            }
            .widgetURL(feed.home("/grades"))
        }
    }

    private func rectangular(_ feed: WidgetFeed) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Label("GPA \(feed.gpaText)", systemImage: "graduationcap.fill")
                .font(.headline)
                .widgetAccentable()
                .lineLimit(1)
            if feed.courses.isEmpty {
                Text("No grades yet")
                    .foregroundStyle(.secondary)
            } else {
                ForEach(feed.courses.prefix(2)) { c in
                    HStack(spacing: 4) {
                        Text(c.code)
                            .lineLimit(1)
                        Spacer(minLength: 4)
                        Text(score(c))
                            .monospacedDigit()
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .layoutPriority(1)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .widgetURL(feed.home("/grades"))
    }

    private func inline(_ feed: WidgetFeed) -> some View {
        Label(feed.goalText.isEmpty ? "GPA \(feed.gpaText)" : "GPA \(feed.gpaText) · Goal \(feed.goalText)", systemImage: "graduationcap")
            .widgetURL(feed.home("/grades"))
    }

    private func score(_ c: WidgetFeed.Course) -> String {
        [c.pctText, c.letter ?? ""].filter { !$0.isEmpty }.joined(separator: " ")
    }
}

/// The term GPA as a ring around its figure, full at 4.0 — green once it reaches the goal.
private struct GPARing: View {
    let feed: WidgetFeed

    var body: some View {
        let gpa = feed.gpa ?? 0
        let onGoal = feed.gpa != nil && feed.goal.map { gpa >= $0 - 0.0001 } == true
        let tint = onGoal ? Color.green : Color.accentColor
        ZStack {
            Circle()
                .stroke(tint.opacity(0.18), lineWidth: 9)
            Circle()
                .trim(from: 0, to: min(max(gpa / 4, 0), 1))
                .stroke(tint, style: StrokeStyle(lineWidth: 9, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .widgetAccentable()
            VStack(spacing: -2) {
                Text(feed.gpaText)
                    .font(.system(size: 24, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .contentTransition(.numericText())
                    .minimumScaleFactor(0.7)
                    .lineLimit(1)
                Text("GPA")
                    .font(.caption2.weight(.semibold))
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 12)
        }
        .aspectRatio(1, contentMode: .fit)
        .padding(4.5)
    }
}

/// A course's colour, its name, its score and its letter.
private struct CourseLine: View {
    let course: WidgetFeed.Course

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(Color(courseHex: course.color))
                .frame(width: 7, height: 7)
                .widgetAccentable()
            Text(course.code)
                .font(.caption.weight(.medium))
                .lineLimit(1)
            Spacer(minLength: 4)
            Text(course.pctText)
                .font(.caption)
                .monospacedDigit()
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .layoutPriority(1)
            Text(course.letter ?? "–")
                .font(.caption.weight(.semibold))
                .frame(minWidth: 20, alignment: .trailing)
                .lineLimit(1)
                .layoutPriority(1)
        }
    }
}

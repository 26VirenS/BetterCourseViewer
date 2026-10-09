import SwiftUI

// The Dashboard's lists (1.2): the work coming up day by day (the List view, as the web Dashboard's List), Canvas's
// recent activity (the Activity view), and the day's work beside the cards.

/// A kind of planner item's symbol (an assignment, a quiz, a discussion, a page, an event, a task of one's own).
enum DashGlyph {
    static func work(_ type: String?) -> String {
        switch type ?? "" {
        case "planner_note": return Glyph.item("task")
        case "calendar_event": return Glyph.item("event")
        case "wiki_page": return Glyph.item("page")
        case "assessment_request": return "person.2"
        case "": return Glyph.item("assignment")
        default: return Glyph.item(type ?? "assignment")
        }
    }

    static func activity(_ type: String?) -> String {
        switch type ?? "" {
        case "Announcement": return "megaphone"
        case "DiscussionTopic": return "bubble.left.and.bubble.right"
        case "Conversation": return "envelope"
        case "Message": return "bell"
        case "Submission": return "checkmark.seal"
        case "Conference", "WebConference": return "video"
        case "Collaboration", "AssessmentRequest": return "person.2"
        default: return "doc"
        }
    }
}

/// The work coming up, a day at a time: each day's heading with its date, and its work in one card — ticked off
/// where it is done, every flag it carries, its points and its time.
struct DashDayList: View {
    let data: DashListData
    let wide: Bool
    let toggle: (DashRow, Bool) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 22) {
            if data.days.isEmpty {
                EmptyNote(text: data.empty ?? "Nothing coming up.", symbol: "sun.max")
            }
            ForEach(data.days) { day in
                VStack(alignment: .leading, spacing: 8) {
                    heading(day)
                    VStack(spacing: 0) {
                        ForEach(Array(day.rows.enumerated()), id: \.element.id) { i, row in
                            if i > 0 { RowDivider(inset: 92) }
                            DashWorkLine(row: row, wide: wide, day: day.title) { done in toggle(row, done) }
                                .transition(.opacity)
                        }
                    }
                    .padding(8)
                    .card(radius: 18)
                }
            }
        }
    }

    private func heading(_ day: DashDay) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text(day.title).font(.sHeadline)
            if let date = day.date, !date.isEmpty {
                Text(date.hasPrefix(day.title + ", ") ? String(date.dropFirst(day.title.count + 2)) : date)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            Text("\(day.rows.count) \(day.rows.count == 1 ? "item" : "items")")
                .font(.sCaption)
                .foregroundStyle(.tertiary)
        }
        .padding(.horizontal, 6)
    }
}

/// A piece of work in the List: its tick, its kind in its course's colour, its course and kind with where it stands,
/// its title, its points and when it is due. A press grows its preview out of it (1.2.3; a double-click opens it); its
/// context menu hands it in or shows its feedback.
struct DashWorkLine: View {
    let row: DashRow
    let wide: Bool
    /// The day it is under ("Today", "Saturday"), for its preview's due line.
    var day: String? = nil
    let toggle: (Bool) -> Void
    @EnvironmentObject private var engine: Engine

    var body: some View {
        PreviewLink(item: .dash(row, day: day, engine: engine, toggle: toggle)) {
            HStack(spacing: 12) {
                CheckCircle(done: row.done, color: .green) { toggle(!row.done) }
                IconTile(symbol: DashGlyph.work(row.type), color: Color(hex: row.color), size: 32)
                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 6) {
                        Text([row.course, row.kind].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "))
                            .font(.sCaption.weight(.medium))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                        ForEach(row.flags ?? [], id: \.self) { f in FlagBadge(flag: f) }
                    }
                    Text(row.title)
                        .strikethrough(row.done, color: .secondary)
                        .font(.sBody)
                        .lineLimit(2)
                        .foregroundStyle(row.done ? .secondary : .primary)
                }
                Spacer(minLength: 8)
                if let p = row.points, !p.isEmpty {
                    Text(p)
                        .font(.sCallout.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .frame(minWidth: wide ? 64 : 0, alignment: .trailing)
                }
                Text(row.due ?? row.time ?? "")
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .frame(minWidth: wide ? 112 : 90, alignment: .trailing)
            }
            .animation(Motion.snappy, value: row.done)
        }
        .workMenu(WorkAction(row.workRow), engine: engine)
    }
}

/// The day's work beside the cards, in the narrow column: its tick, its title, its course in its colour with its
/// kind, its time. A press grows its preview out of it (1.2.3).
struct DashTodayLine: View {
    let row: WorkRow
    let toggle: (Bool) -> Void
    @EnvironmentObject private var engine: Engine

    var body: some View {
        PreviewLink(item: .work(row, engine: engine, toggle: toggle)) {
            HStack(alignment: .top, spacing: 11) {
                CheckCircle(done: row.done, color: .green) { toggle(!row.done) }
                VStack(alignment: .leading, spacing: 3) {
                    Text(row.title)
                        .strikethrough(row.done, color: .secondary)
                        .font(.sBody)
                        .lineLimit(2)
                        .foregroundStyle(row.done ? .secondary : .primary)
                    HStack(spacing: 6) {
                        if let course = row.course, !course.isEmpty {
                            Circle().fill(Color(hex: row.color)).frame(width: 7, height: 7)
                            Text(course).lineLimit(1)
                        }
                        if let sub = row.sub, !sub.isEmpty {
                            Text(sub).lineLimit(1)
                        }
                    }
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    if let flag = row.flag { FlagBadge(flag: flag) }
                }
                Spacer(minLength: 6)
                if let time = row.time, !time.isEmpty {
                    Text(time)
                        .font(.sCallout.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
            }
            .animation(Motion.snappy, value: row.done)
        }
        .workMenu(WorkAction(row), engine: engine)
    }
}

/// Canvas's recent activity: what is new marked with a dot, each with its symbol in its course's colour, its title
/// and when, what it is and where, and its first lines. A press grows its preview out of it (1.2.3; a double-click
/// opens it, its dot going as it always did).
struct DashActivityList: View {
    let data: DashActivityData
    let open: (DashActivityRow) -> Void
    @EnvironmentObject private var engine: Engine

    var body: some View {
        if data.rows.isEmpty {
            EmptyNote(text: data.empty ?? "No recent activity.", symbol: "clock")
        } else {
            VStack(spacing: 0) {
                ForEach(Array(data.rows.enumerated()), id: \.element.id) { i, r in
                    if i > 0 { RowDivider(inset: 74) }
                    line(r)
                }
            }
            .padding(8)
            .card(radius: 18)
        }
    }

    /// An item's preview: what its row says, and (a grade posted) the assignment as the card opens.
    private func preview(_ r: DashActivityRow) -> PreviewItem {
        var item = PreviewItem(id: "activity:\(r.id)", title: r.title, kind: PreviewFormat.text(r.kind), symbol: DashGlyph.activity(r.type),
                               course: PreviewFormat.text(r.course), color: r.color)
        item.when = PreviewFormat.text(r.when)
        item.excerpt = PreviewFormat.text(r.preview)
        if let u = PreviewFormat.text(r.url) {
            item.url = u
            item.openWhole = { open(r) }
        }
        return item
    }

    private func line(_ r: DashActivityRow) -> some View {
        PreviewLink(item: preview(r)) {
            HStack(alignment: .top, spacing: 12) {
                Circle()
                    .fill(r.unread == true ? Theme.accent : Color.clear)
                    .frame(width: 8, height: 8)
                    .padding(.top, 13)
                    .accessibilityLabel(Text(r.unread == true ? "New" : ""))
                IconTile(symbol: DashGlyph.activity(r.type), color: Color(hex: r.color), size: 34)
                VStack(alignment: .leading, spacing: 3) {
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Text(r.title)
                            .font(r.unread == true ? .sBody.weight(.semibold) : .sBody)
                            .lineLimit(2)
                        Spacer(minLength: 8)
                        Text(r.when ?? "")
                            .font(.sCaption)
                            .foregroundStyle(.secondary)
                    }
                    Text([r.kind, r.course].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                    if let p = r.preview, !p.isEmpty {
                        Text(p)
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .lineLimit(2)
                    }
                }
            }
        }
        .contextMenu {
            if let u = r.url {
                Button("Open") { open(r) }
                Button("Open in \(engine.lmsName)") { engine.openWebScreen(u, title: r.title) }
                Button("Copy Link") { if let x = engine.absolute(u) { copyToPasteboard(x.absoluteString) } }
            }
        }
    }
}

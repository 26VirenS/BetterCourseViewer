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

    /// A kind of work in words ("Assignment", "Quiz", "Announcement", "My task" — the engine's), as its symbol.
    static func kind(_ kind: String?) -> String {
        switch (kind ?? "").lowercased() {
        case "announcement": return Glyph.item("announcement")
        case "my task", "task": return Glyph.item("task")
        case "event": return Glyph.item("event")
        case "peer review": return "person.2"
        case "": return Glyph.item("assignment")
        default: return Glyph.item((kind ?? "").lowercased())
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

    /// A day's title and its date as every date is written ("Tomorrow  Wed, Oct 14", "Thursday  Oct 15") — but Today's,
    /// which the greeting over the page already says (home-8).
    private func heading(_ day: DashDay) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text(day.title).font(.sHeadline)
            if day.title != "Today", let date = day.date, !date.isEmpty {
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
    // (1.3.78: the engine as a plain value, to act through — not drawn again each time it changes: EngineLink)
    @Environment(\.engineLink) private var link
    private var engine: Engine { link.acting }

    var body: some View {
        PreviewLink(item: .dash(row, day: day, engine: engine, toggle: toggle)) {
            HStack(spacing: 12) {
                CheckCircle(done: row.done, color: .green) { toggle(!row.done) }
                IconTile(symbol: DashGlyph.work(row.type), color: Color(hex: row.color), size: 32)
                DashWorkText(context: [row.course, row.kind].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "),
                             flags: row.flags ?? [], title: row.title, done: row.done)
                Spacer(minLength: 8)
                DashWorkFigures(time: row.due ?? row.time, points: row.points, stacked: false, wide: wide)
            }
            .animation(Motion.snappy, value: row.done)
        }
        .workMenu(WorkAction(row.workRow), engine: engine)
    }
}

/// (home-21) The words of a piece of work, as every Dashboard list draws them — Today, Coming up and a counter's panel:
/// its course and kind on a small line (with where it stands beside them), its title under it.
struct DashWorkText: View {
    let context: String
    var flags: [WorkFlag] = []
    let title: String
    var done = false
    var quiet = false

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            if !context.isEmpty || !flags.isEmpty {
                HStack(spacing: 6) {
                    if !context.isEmpty {
                        Text(context)
                            .font(.sCaption.weight(.medium))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                    ForEach(flags, id: \.self) { f in FlagBadge(flag: f) }
                }
            }
            Text(title)
                .strikethrough(done, color: .secondary)
                .font(.sBody)
                .lineLimit(2)
                .foregroundStyle(done || quiet ? .secondary : .primary)
                // (1.3.80) struck through at once, not drawn from one look to the other: ticked by a live check, the
                // title's change was left half-way — its letters gone, a line where they stood (light-48a-live-dashboard)
                .contentTransition(.identity)
        }
    }
}

/// (home-21) A piece of work's figures on the right of its row: its points and its time side by side on a wide list,
/// its time over its points where the row is narrow (the day's work in the side column, a counter's panel).
struct DashWorkFigures: View {
    let time: String?
    let points: String?
    var stacked = false
    var wide = false

    var body: some View {
        if stacked {
            VStack(alignment: .trailing, spacing: 2) {
                if let t = PreviewFormat.text(time) {
                    Text(t).font(.sCallout.monospacedDigit()).foregroundStyle(.secondary)
                }
                if let p = PreviewFormat.text(points) {
                    Text(p).font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
                }
            }
            .lineLimit(1)
            .fixedSize()
        } else {
            HStack(spacing: 12) {
                if let p = PreviewFormat.text(points) {
                    Text(p)
                        .font(.sCallout.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .frame(minWidth: wide ? 64 : 0, alignment: .trailing)
                }
                Text(time ?? "")
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .frame(minWidth: wide ? 112 : 76, alignment: .trailing)
            }
            .lineLimit(1)
            .fixedSize()
        }
    }
}

/// The day's work: (home-21) drawn as Coming up draws work — its tick, its kind's symbol in its course's colour, its
/// course and kind over its title, its points and its time on the right (one over the other in the narrow side column).
/// A press grows its preview out of it (1.2.3).
struct DashTodayLine: View {
    let row: WorkRow
    /// In the side column beside the view: the figures one over the other.
    var narrow = false
    let toggle: (Bool) -> Void
    // (1.3.78: the engine as a plain value, to act through — not drawn again each time it changes: EngineLink)
    @Environment(\.engineLink) private var link
    private var engine: Engine { link.acting }

    /// Its kind: the engine's word, else the first of its line ("Assignment · 10 pts"), or a task of one's own.
    private var kind: String {
        if let k = PreviewFormat.text(row.kind) { return k }
        if row.custom == true { return "My task" }
        return PreviewFormat.text((row.sub ?? "").components(separatedBy: " · ").first) ?? "Assignment"
    }

    var body: some View {
        PreviewLink(item: .work(row, engine: engine, toggle: toggle)) {
            HStack(spacing: 12) {
                CheckCircle(done: row.done, color: .green) { toggle(!row.done) }
                IconTile(symbol: DashGlyph.kind(kind), color: Color(hex: row.color), size: 32)
                DashWorkText(context: [PreviewFormat.text(row.course), kind].compactMap { $0 }.joined(separator: " · "),
                             flags: row.flag.map { [$0] } ?? [], title: row.title, done: row.done)
                Spacer(minLength: 8)
                DashWorkFigures(time: row.time, points: row.pts ?? PreviewFormat.points(in: row.sub), stacked: narrow)
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
    // (1.3.78: the engine as a plain value, to act through — not drawn again each time it changes: EngineLink)
    @Environment(\.engineLink) private var link
    private var engine: Engine { link.acting }

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

import SwiftUI

// MARK: - Sections and rows in cards

/// A card with its small-capitals heading, as Simpl's web cards are: the heading inside the card, at its top, and what it
/// holds under it (rows, a chart, a note).
struct CardSection<Content: View, Accessory: View>: View {
    let title: String
    var trailing: String? = nil
    var padding: CGFloat = 14
    @ViewBuilder var accessory: () -> Accessory
    @ViewBuilder var content: () -> Content

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                CardHeading(text: title, trailing: trailing)
                accessory()
            }
            .padding(.horizontal, 6)
            VStack(alignment: .leading, spacing: 0) { content() }
        }
        .padding(padding)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }
}

extension CardSection where Accessory == EmptyView {
    init(title: String, trailing: String? = nil, padding: CGFloat = 14, @ViewBuilder content: @escaping () -> Content) {
        self.init(title: title, trailing: trailing, padding: padding, accessory: { EmptyView() }, content: content)
    }
}

/// A row of a card that opens something: a wash under the pointer, a deeper one under a click, the whole row the target.
struct RowLink<Label: View>: View {
    let action: () -> Void
    @ViewBuilder var label: () -> Label

    var body: some View {
        Button(action: action) {
            label()
                .padding(.horizontal, 8)
                .padding(.vertical, 7)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .buttonStyle(RowButtonStyle())
    }
}

/// The hairline between two rows of a card, starting where the rows' words do.
struct RowDivider: View {
    var inset: CGFloat = 50

    var body: some View {
        Divider().padding(.leading, inset).padding(.trailing, 8)
    }
}

// MARK: - Shapes of row

/// A fact with its symbol (a teacher, a term, how much is still to do): every line's words start together.
struct Fact: View {
    let symbol: String
    let text: String
    var tint: Color = .secondary

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Image(systemName: symbol).frame(width: 18).accessibilityHidden(true)
            Text(text).textSelection(.enabled)
        }
        .font(.callout)
        .foregroundStyle(tint)
    }
}

/// A row with a title, a line under it and something at its end: the shape most lists here share.
struct InfoRow<Trailing: View>: View {
    let title: String
    var sub: String? = nil
    var symbol: String? = nil
    var tint: Color = .accentColor
    var unread = false
    @ViewBuilder var trailing: () -> Trailing

    var body: some View {
        HStack(spacing: 12) {
            if let symbol { IconTile(symbol: symbol, color: tint) }
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(unread ? .body.weight(.semibold) : .body)
                    .lineLimit(2)
                if let sub, !sub.isEmpty {
                    Text(sub).font(.callout).foregroundStyle(.secondary).lineLimit(2)
                }
            }
            Spacer(minLength: 6)
            trailing()
        }
        .contentShape(Rectangle())
    }
}

extension InfoRow where Trailing == EmptyView {
    init(title: String, sub: String? = nil, symbol: String? = nil, tint: Color = .accentColor, unread: Bool = false) {
        self.init(title: title, sub: sub, symbol: symbol, tint: tint, unread: unread) { EmptyView() }
    }
}

/// A row of work (Today, To Do): its tick, its title and line, where it stands, its course and its time. Ticked, the
/// title strikes through and greys on the house spring.
struct WorkRowView: View {
    let row: WorkRow
    var showCourse = true
    let toggle: (Bool) -> Void

    var body: some View {
        HStack(spacing: 12) {
            CheckCircle(done: row.done, color: .green) { toggle(!row.done) }
            VStack(alignment: .leading, spacing: 2) {
                Text(row.title)
                    .lineLimit(2)
                    .strikethrough(row.done, color: .secondary)
                    .foregroundStyle(row.done ? .secondary : .primary)
                if let sub = row.sub, !sub.isEmpty {
                    Text(sub).font(.callout).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer(minLength: 6)
            if let flag = row.flag { FlagBadge(flag: flag) }
            if showCourse, let course = row.course, !course.isEmpty { CourseChip(text: course, color: row.color) }
            if let time = row.time, !time.isEmpty {
                Text(time)
                    .font(.callout.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .frame(minWidth: 58, alignment: .trailing)
            }
        }
        .animation(Motion.snappy, value: row.done)
    }
}

/// A piece of work in a list: its kind's icon in the course's colour, its name, its facts, where it stands.
struct ARowView: View {
    let row: ARow
    let color: Color

    var body: some View {
        InfoRow(title: row.title, sub: row.sub, symbol: Glyph.item(row.kind ?? "assignment"), tint: color) {
            if let s = row.status, !s.word.isEmpty { StatusChip(text: s.word, tone: s.kind) }
        }
    }
}

/// An announcement or a discussion in a list: who posted it, when, its first lines, its replies.
struct PostRowView: View {
    let row: PostRow
    var color: Color = .accentColor

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            PersonAvatar(name: row.author ?? "", avatar: row.avatar, size: 34)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    if row.unread == true {
                        Circle().fill(Color.accentColor).frame(width: 8, height: 8).accessibilityLabel("Unread")
                    }
                    Text(row.title).font(row.unread == true ? .body.weight(.semibold) : .body).lineLimit(2)
                }
                Text([row.author, row.when].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "))
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
                if let p = row.preview, !p.isEmpty {
                    Text(p).font(.callout).foregroundStyle(.secondary).lineLimit(2)
                }
                if row.graded == true || (row.replies ?? 0) > 0 || (row.unreadCount ?? 0) > 0 {
                    HStack(spacing: 8) {
                        if row.graded == true { StatusChip(text: "Graded", tone: "purple") }
                        if let n = row.replies, n > 0 {
                            Label("\(n)", systemImage: "bubble.left").font(.caption).foregroundStyle(.secondary)
                        }
                        if let n = row.unreadCount, n > 0 {
                            Text("\(n) new").font(.caption.weight(.semibold)).foregroundStyle(color)
                        }
                    }
                    .padding(.top, 1)
                }
            }
            Spacer(minLength: 0)
        }
        .contentShape(Rectangle())
    }
}

/// A screen's toolbar item that opens the place showing on Canvas's own site, and copies its link.
struct CanvasMenu: View {
    let url: String?
    let title: String
    @EnvironmentObject private var engine: Engine

    var body: some View {
        Menu {
            Button {
                if let url { engine.openWebScreen(url, title: title) }
            } label: {
                Label("Open in Canvas", systemImage: "globe")
            }
            Button {
                if let url, let u = engine.absolute(url) { copyToPasteboard(u.absoluteString) }
            } label: {
                Label("Copy Link", systemImage: "link")
            }
        } label: {
            Label("More", systemImage: "ellipsis.circle")
        }
        .menuIndicator(.hidden)
        .disabled(url == nil)
        .help("More")
    }
}

import SwiftUI

/// A grade ring: the track, and the arc to the score in the course's colour, drawn (not a system gauge,
/// so it shows in any list and any size). It fills once, with a spring, when it first appears, and moves
/// from where it is when the score changes (a what-if score) — never from empty again.
struct Ring: View {
    let value: Double? // 0…100; nil draws the track alone
    let color: Color
    var lineWidth: CGFloat = 6
    @State private var shown: Double = 0
    @State private var appeared = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var target: Double { min(max(value ?? 0, 0), 100) / 100 }

    var body: some View {
        ZStack {
            Circle().stroke(color.opacity(0.18), lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: shown)
                .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
        }
        .padding(lineWidth / 2)
        .onAppear {
            guard !appeared else { return }
            appeared = true
            if reduceMotion { shown = target } else { withAnimation(.spring(response: 0.8, dampingFraction: 0.9).delay(0.05)) { shown = target } }
        }
        .onChange(of: target) {
            if reduceMotion { shown = target } else { withAnimation(.spring(response: 0.5, dampingFraction: 0.85)) { shown = target } }
        }
        .accessibilityHidden(true)
    }
}

/// Rings inside rings, as the Activity rings: the course's total outermost, then one ring per assignment
/// group that has graded work (its own colour), each filling once as `Ring` does.
struct NestedRings: View {
    struct Band: Identifiable {
        let id: Int
        let value: Double?
        let color: Color
    }

    let bands: [Band] // outermost first
    var outerWidth: CGFloat = 5
    var innerWidth: CGFloat = 3.5
    var gap: CGFloat = 1.5

    var body: some View {
        ZStack {
            ForEach(bands) { b in
                Ring(value: b.value, color: b.color, lineWidth: b.id == 0 ? outerWidth : innerWidth)
                    .padding(b.id == 0 ? 0 : outerWidth + gap + CGFloat(b.id - 1) * (innerWidth + gap))
            }
        }
        .accessibilityHidden(true)
    }

    /// The colours a group takes when Canvas's answer names none.
    static let palette: [Color] = [.blue, .purple, .orange, .teal, .pink, .indigo]

    /// The total and up to `limit` groups, as bands.
    static func bands(total: Double?, color: Color, groups: [(pct: Double?, color: String?)], limit: Int) -> [Band] {
        var out = [Band(id: 0, value: total, color: color)]
        for (i, g) in groups.filter({ $0.pct != nil }).prefix(limit).enumerated() {
            let c = g.color.map { Color(hex: $0) } ?? palette[i % palette.count]
            out.append(Band(id: i + 1, value: g.pct, color: c))
        }
        return out
    }
}

/// A status in words on a wash of its colour (Missing, Late, Graded, Submitted, Locked …).
struct StatusChip: View {
    let text: String
    var tone: String? = nil

    var body: some View {
        Text(text)
            .font(.caption2.weight(.semibold))
            .lineLimit(1)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .foregroundStyle(color)
            .background(color.opacity(0.14), in: Capsule())
    }

    var color: Color { StatusChip.color(tone) }

    static func color(_ tone: String?) -> Color {
        switch tone {
        case "bad": return .red
        case "warn": return .orange
        case "good": return .green
        case "info": return .blue
        case "purple": return .purple
        default: return .secondary
        }
    }
}

/// A round picture or the initials of a person (a teacher, a classmate, a sender).
struct PersonAvatar: View {
    let name: String
    var avatar: String? = nil
    var size: CGFloat = 32

    var body: some View {
        Group {
            if let s = avatar, let url = URL(string: s) {
                AsyncImage(url: url) { image in image.resizable().scaledToFill() } placeholder: { initials }
            } else {
                initials
            }
        }
        .frame(width: size, height: size)
        .clipShape(Circle())
        .accessibilityHidden(true)
    }

    private var initials: some View {
        let parts = name.split(separator: " ").prefix(2).compactMap { $0.first }
        return ZStack {
            Circle().fill(Color(.systemGray4))
            if parts.isEmpty {
                Image(systemName: "person.fill").font(.system(size: size * 0.5)).foregroundStyle(.white)
            } else {
                Text(String(parts).uppercased())
                    .font(.system(size: size * 0.38, weight: .semibold))
                    .foregroundStyle(.white)
            }
        }
    }
}

/// The SF Symbol for a section of a course or a group, and for a kind of item in a module.
enum Glyph {
    static func section(_ kind: String) -> String {
        switch kind {
        case "announcements": return "megaphone"
        case "discussions": return "bubble.left.and.bubble.right"
        case "assignments": return "doc.text"
        case "modules": return "square.stack.3d.up"
        case "pages": return "doc.richtext"
        case "files": return "folder"
        case "people": return "person.2"
        case "quizzes": return "checklist"
        case "syllabus": return "list.bullet.rectangle"
        case "grades": return "chart.bar"
        case "home": return "house"
        case "collaborations": return "person.3.sequence"
        case "conferences": return "video"
        default: return "square.grid.2x2"
        }
    }

    static func item(_ type: String) -> String {
        switch type {
        case "Assignment", "assignment": return "doc.text"
        case "Quiz", "quiz": return "checklist"
        case "Discussion", "discussion": return "bubble.left.and.bubble.right"
        case "Page", "page": return "doc.richtext"
        case "File", "file": return "doc"
        case "ExternalUrl", "link": return "link"
        case "ExternalTool", "Tool", "tool": return "puzzlepiece.extension"
        case "SubHeader": return "textformat"
        case "announcement": return "megaphone"
        case "folder": return "folder"
        default: return "doc"
        }
    }
}

/// An icon on a rounded wash of a colour, at the start of a row.
struct IconTile: View {
    let symbol: String
    let color: Color
    var size: CGFloat = 30

    var body: some View {
        RoundedRectangle(cornerRadius: size * 0.27, style: .continuous)
            .fill(color.opacity(0.16))
            .overlay(Image(systemName: symbol).font(.system(size: size * 0.48, weight: .semibold)).foregroundStyle(color))
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }
}

/// A fact with its symbol in a column of its own, so lines with different symbols start their words together
/// (a list's own Label puts a wide gap between the two).
struct Fact: View {
    let symbol: String
    let text: String
    var tint: Color = .secondary

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Image(systemName: symbol).frame(width: 20).accessibilityHidden(true)
            Text(text)
        }
        .font(.subheadline)
        .foregroundStyle(tint)
    }
}

extension View {
    /// The row's divider starts at its words (not at a picture's initials or a ring's letter, which
    /// the list would otherwise take for the row's text and start some dividers further left than others).
    func separatorAtText() -> some View {
        alignmentGuide(.listRowSeparatorLeading) { $0[.leading] }
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
                    Text(sub).font(.footnote).foregroundStyle(.secondary).lineLimit(2)
                }
            }
            .separatorAtText()
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

/// A line under a screen's large title (iOS 26: the bar's own subtitle, on the title's edge).
struct TitleSubtitle: ViewModifier {
    let text: String?

    @ViewBuilder
    func body(content: Content) -> some View {
        if #available(iOS 26.0, *) {
            if let text, !text.isEmpty { content.navigationSubtitle(text) } else { content }
        } else {
            content
        }
    }
}

/// The simulator suite's way into a sheet (-SimplOpen handin, -SimplOpen grades:101): taken once a launch.
@MainActor
/// Swipe right on a piece of work (1.4): Hand In — Take Quiz, Reply, Open for a quiz, a discussion, a tool —
/// while it waits on you; Feedback once it is handed in or graded. A full swipe does it.
struct WorkAction {
    let url: String?
    let title: String
    let kind: String?
    let handedIn: Bool
    let open: Bool

    /// A row of a course's lists: what its status says.
    init(_ r: ARow) {
        url = r.url
        title = r.title
        kind = r.kind
        let k = r.status?.kind ?? ""
        let word = r.status?.word ?? ""
        handedIn = k == "good" || word.hasPrefix("Submitted")
        open = k != "muted"
    }

    /// A row of Today or To Do: done is handed in (a task of your own has nothing to hand in).
    init(_ w: WorkRow) {
        url = w.custom == true ? nil : w.url
        title = w.title
        kind = w.type
        handedIn = w.done
        open = true
    }

    var label: (String, String) {
        switch (kind ?? "").lowercased() {
        case "quiz": return ("Take Quiz", "checklist")
        case "discussion", "discussion_topic": return ("Reply", "bubble.left.fill")
        case "tool": return ("Open", "puzzlepiece.extension.fill")
        default: return ("Hand In", "tray.and.arrow.up.fill")
        }
    }
}

extension View {
    /// The swipe-right action of a piece of work (WorkAction), with whatever else that edge already holds after it.
    func workSwipe<More: View>(_ w: WorkAction, engine: Engine, @ViewBuilder more: () -> More = { EmptyView() }) -> some View {
        self.swipeActions(edge: .leading, allowsFullSwipe: true) {
            if let url = w.url, !url.isEmpty {
                if w.handedIn {
                    Button { engine.act(on: url, title: w.title, feedback: true) } label: { Label("Feedback", systemImage: "text.bubble.fill") }
                        .tint(.purple)
                } else if w.open {
                    Button { engine.act(on: url, title: w.title, feedback: false) } label: { Label(w.label.0, systemImage: w.label.1) }
                        .tint(.green)
                }
            }
            more()
        }
    }
}

enum LaunchOpen {
    private static var used = false

    static func take(_ prefix: String) -> String? {
        guard !used, let v = UserDefaults.standard.string(forKey: "SimplOpen"), v.hasPrefix(prefix) else { return nil }
        used = true
        return String(v.dropFirst(prefix.count))
    }
}

/// A screen's data, read through the engine: its first answer, its error, and a reload. Screens hold one
/// and show `LoadState` until it has an answer; a reload keeps what is shown until the new answer is in.
@MainActor
final class Loader<T: Decodable>: ObservableObject {
    @Published var data: T?
    @Published var error: String?

    func load(_ engine: Engine, _ name: String, _ args: [String: Any] = [:]) async {
        do {
            let d = try await engine.call(name, args, as: T.self)
            var t = Transaction()
            t.disablesAnimations = true // (a redraw is not an arrival: the list changes in place)
            withTransaction(t) {
                data = d
                error = nil
            }
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }
}

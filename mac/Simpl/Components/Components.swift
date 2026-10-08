import SwiftUI

// MARK: - Cards and pages

/// A screen's scrolling page: Simpl's quiet ground, its content in a column that keeps a readable width on a wide window.
struct Page<Content: View>: View {
    var maxWidth: CGFloat = 1180
    var spacing: CGFloat = 22
    @ViewBuilder var content: Content

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: spacing) { content }
                .frame(maxWidth: maxWidth, alignment: .leading)
                .padding(.horizontal, 32)
                .padding(.top, 22)
                .padding(.bottom, 40)
                .frame(maxWidth: .infinity)
        }
        .background(Theme.page)
    }
}

/// A card on the page: the card colour, a hairline edge and a soft shadow.
struct CardBackground: ViewModifier {
    var radius: CGFloat = 16
    var tint: Color? = nil

    func body(content: Content) -> some View {
        content
            .background {
                RoundedRectangle(cornerRadius: radius, style: .continuous)
                    .fill(Theme.card)
                    .overlay {
                        if let tint { RoundedRectangle(cornerRadius: radius, style: .continuous).fill(tint.opacity(0.1)) }
                    }
            }
            .overlay(RoundedRectangle(cornerRadius: radius, style: .continuous).strokeBorder(Theme.edge, lineWidth: 1))
            .shadow(color: Theme.shadow, radius: 1.5, y: 1)
    }
}

extension View {
    func card(radius: CGFloat = 16, tint: Color? = nil) -> some View { modifier(CardBackground(radius: radius, tint: tint)) }
}

/// A card that is a button: it lifts under the pointer (a little larger, its shadow longer) and gives under a click, on
/// the house springs. Under Reduce Motion it changes colour only.
struct CardButtonStyle: ButtonStyle {
    var radius: CGFloat = 16
    var tint: Color? = nil

    func makeBody(configuration: Configuration) -> some View {
        CardButtonBody(configuration: configuration, radius: radius, tint: tint)
    }

    private struct CardButtonBody: View {
        let configuration: ButtonStyleConfiguration
        let radius: CGFloat
        let tint: Color?
        @State private var hover = false
        @Environment(\.accessibilityReduceMotion) private var reduceMotion

        var body: some View {
            let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
            configuration.label
                .contentShape(shape)
                .background {
                    shape.fill(hover ? Theme.cardHover : Theme.card)
                        .overlay { if let tint { shape.fill(tint.opacity(0.1)) } }
                }
                .overlay(shape.strokeBorder(Theme.edge, lineWidth: 1))
                .shadow(color: Theme.shadow, radius: hover && !reduceMotion ? 10 : 1.5, y: hover && !reduceMotion ? 4 : 1)
                .scaleEffect(reduceMotion ? 1 : (configuration.isPressed ? 0.985 : (hover ? 1.008 : 1)))
                .animation(Motion.hover, value: hover)
                .animation(Motion.snappy, value: configuration.isPressed)
                .onHover { hover = $0 }
        }
    }
}

/// A row in a card that is a button: a wash under the pointer, a deeper one under a click.
struct RowButtonStyle: ButtonStyle {
    var radius: CGFloat = 10

    func makeBody(configuration: Configuration) -> some View {
        RowBody(configuration: configuration, radius: radius)
    }

    private struct RowBody: View {
        let configuration: ButtonStyleConfiguration
        let radius: CGFloat
        @State private var hover = false

        var body: some View {
            configuration.label
                .contentShape(Rectangle())
                .background {
                    RoundedRectangle(cornerRadius: radius, style: .continuous)
                        .fill(Color.primary.opacity(configuration.isPressed ? 0.09 : (hover ? 0.05 : 0)))
                }
                .animation(Motion.hover, value: hover)
                .onHover { hover = $0 }
        }
    }
}

/// A card's heading: small capitals, as Simpl's web cards have them.
struct CardHeading: View {
    let text: String
    var trailing: String? = nil

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            Text(text.uppercased())
                .font(.system(size: 11, weight: .semibold))
                .tracking(0.6)
                .foregroundStyle(.secondary)
            Spacer()
            if let trailing, !trailing.isEmpty {
                Text(trailing).font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

/// A screen's own large heading, with the line under it.
struct ScreenHeading: View {
    let title: String
    var sub: String? = nil
    var color: Color? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 10) {
                if let color {
                    RoundedRectangle(cornerRadius: 4, style: .continuous).fill(color).frame(width: 14, height: 14)
                }
                Text(title)
                    .font(.system(size: 28, weight: .bold))
                    .tracking(-0.4)
                    .textSelection(.enabled)
            }
            if let sub, !sub.isEmpty {
                Text(sub).font(.callout).foregroundStyle(.secondary)
            }
        }
    }
}

// MARK: - Rings

/// A grade ring: the track and the arc to the score in the course's colour. It fills once, on a soft spring, when it
/// first appears, and moves from where it is when the score changes (a what-if score) — never from empty again.
struct Ring: View {
    let value: Double? // 0…100; nil draws the track alone
    let color: Color
    var lineWidth: CGFloat = 6
    var key: String? = nil
    @MainActor private static var filled = Set<String>()
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
            let seen = key.map { !Ring.filled.insert($0).inserted } ?? false
            if reduceMotion || seen { shown = target } else { withAnimation(Motion.fill.delay(0.05)) { shown = target } }
        }
        .onChange(of: target) { _, t in
            if reduceMotion { shown = t } else { withAnimation(.spring(response: 0.5, dampingFraction: 0.85)) { shown = t } }
        }
        .accessibilityHidden(true)
    }
}

/// Rings inside rings, as the Activity rings: the course's total outermost, then one per assignment group with graded work.
struct NestedRings: View {
    struct Band: Identifiable {
        let id: Int
        let value: Double?
        let color: Color
    }

    let bands: [Band]
    var outerWidth: CGFloat = 6
    var innerWidth: CGFloat = 4
    var gap: CGFloat = 2
    var key: String? = nil

    var body: some View {
        ZStack {
            ForEach(bands) { b in
                Ring(value: b.value, color: b.color, lineWidth: b.id == 0 ? outerWidth : innerWidth, key: key.map { "\($0)#\(b.id)" })
                    .padding(b.id == 0 ? 0 : outerWidth + gap + CGFloat(b.id - 1) * (innerWidth + gap))
            }
        }
        .accessibilityHidden(true)
    }

    static let palette: [Color] = [.blue, .purple, .orange, .teal, .pink, .indigo]

    static func bands(total: Double?, color: Color, groups: [(pct: Double?, color: String?)], limit: Int) -> [Band] {
        var out = [Band(id: 0, value: total, color: color)]
        for (i, g) in groups.filter({ $0.pct != nil }).prefix(limit).enumerated() {
            let c = g.color.map { Color(hex: $0) } ?? palette[i % palette.count]
            out.append(Band(id: i + 1, value: g.pct, color: c))
        }
        return out
    }
}

// MARK: - Chips, badges, ticks

/// A status in words on a wash of its colour (Missing, Late, Graded, Submitted, Locked …).
struct StatusChip: View {
    let text: String
    var tone: String? = nil

    var body: some View {
        Text(text)
            .font(.caption.weight(.semibold))
            .lineLimit(1)
            .padding(.horizontal, 7)
            .padding(.vertical, 2.5)
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

/// A course's chip: its short name on a wash of its colour.
struct CourseChip: View {
    let text: String
    let color: String?

    var body: some View {
        Text(text)
            .font(.caption.weight(.semibold))
            .lineLimit(1)
            .padding(.horizontal, 7)
            .padding(.vertical, 2.5)
            .foregroundStyle(Color(hex: color))
            .background(Color(hex: color).opacity(0.15), in: Capsule())
    }
}

/// Where a piece of work stands (Missing, Late, Excused, Feedback, New) — the words the web screens use.
struct FlagBadge: View {
    let flag: WorkFlag

    var body: some View {
        StatusChip(text: flag.word, tone: flag.kind)
    }
}

/// The round tick of a piece of work or a task: it fills with the colour and the tick springs in.
struct CheckCircle: View {
    let done: Bool
    var color: Color = .green
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            ZStack {
                Circle()
                    .strokeBorder(done ? color : Color.secondary.opacity(hover ? 0.9 : 0.5), lineWidth: 1.5)
                    .background(Circle().fill(done ? color : (hover ? color.opacity(0.12) : .clear)))
                if done {
                    Image(systemName: "checkmark")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundStyle(.white)
                        .transition(.scale(scale: 0.4).combined(with: .opacity))
                }
            }
            .frame(width: 20, height: 20)
            .contentShape(Circle())
            .animation(Motion.snappy, value: done)
            .animation(Motion.hover, value: hover)
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
        .help(done ? "Mark not done" : "Mark done")
        .accessibilityLabel(done ? "Mark not done" : "Mark done")
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
            .overlay(Image(systemName: symbol).font(.system(size: size * 0.46, weight: .semibold)).foregroundStyle(color))
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }
}

/// The student's picture, or their initials on the accent.
struct Avatar: View {
    let person: Person?
    var size: CGFloat = 30

    var body: some View {
        Group {
            if let s = person?.avatar, let url = URL(string: s) {
                AsyncImage(url: url) { image in
                    image.resizable().scaledToFill()
                } placeholder: {
                    initials
                }
            } else {
                initials
            }
        }
        .frame(width: size, height: size)
        .clipShape(Circle())
        .accessibilityHidden(true)
    }

    private var initials: some View {
        ZStack {
            Circle().fill(Color.accentColor.gradient)
            Text(person?.initials?.isEmpty == false ? person!.initials! : "·")
                .font(.system(size: size * 0.4, weight: .semibold))
                .foregroundStyle(.white)
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
            Circle().fill(Color.secondary.opacity(0.35))
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
        case "Discussion", "discussion", "discussion_topic": return "bubble.left.and.bubble.right"
        case "Page", "page": return "doc.richtext"
        case "File", "file": return "doc"
        case "ExternalUrl", "link": return "link"
        case "ExternalTool", "Tool", "tool": return "puzzlepiece.extension"
        case "SubHeader": return "textformat"
        case "announcement": return "megaphone"
        case "folder": return "folder"
        case "event": return "calendar"
        case "task", "My task": return "checkmark.circle"
        default: return "doc"
        }
    }
}

// MARK: - Loading

/// A screen's state while its first answer is on the way, or when it could not be read.
struct LoadState: View {
    let error: String?
    let retry: () -> Void

    var body: some View {
        if let error = error {
            ContentUnavailableView {
                Label("Couldn’t Load", systemImage: "exclamationmark.triangle")
            } description: {
                Text(error)
            } actions: {
                Button("Try Again", action: retry)
            }
        } else {
            ProgressView()
                .controlSize(.regular)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

/// A screen's data, read through the engine: its answer, its error, and a reload that keeps what is shown until the new
/// answer is in (a redraw is not an arrival; an answer to something just done arrives on the house spring).
@MainActor
final class Loader<T: Decodable>: ObservableObject {
    @Published var data: T?
    @Published var error: String?

    func load(_ engine: Engine, _ name: String, _ args: [String: Any] = [:], animated: Bool = false) async {
        do {
            let d = try await engine.call(name, args, as: T.self)
            if animated {
                withAnimation(Motion.gentle) {
                    data = d
                    error = nil
                }
                return
            }
            var t = Transaction()
            t.disablesAnimations = true
            withTransaction(t) {
                data = d
                error = nil
            }
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }
}

/// A short line where a list is empty.
struct EmptyNote: View {
    let text: String
    var symbol: String = "checkmark.circle"

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: symbol).foregroundStyle(.tertiary)
            Text(text).foregroundStyle(.secondary)
        }
        .font(.callout)
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.vertical, 12)
    }
}

/// A piece of work's own action (its context menu): Hand In — Take Quiz, Reply, Open for a quiz, a discussion, a tool —
/// while it waits on you; Feedback once it is handed in or graded.
struct WorkAction {
    let url: String?
    let title: String
    let kind: String?
    let handedIn: Bool
    let open: Bool

    init(_ r: ARow) {
        url = r.url
        title = r.title
        kind = r.kind
        let k = r.status?.kind ?? ""
        let word = r.status?.word ?? ""
        handedIn = k == "good" || word.hasPrefix("Submitted")
        open = k != "muted"
    }

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
        case "discussion", "discussion_topic": return ("Reply", "bubble.left")
        case "tool": return ("Open", "puzzlepiece.extension")
        default: return ("Hand In", "tray.and.arrow.up")
        }
    }
}

extension View {
    /// The context menu of a piece of work: open it, hand it in or see its feedback, open it in Canvas, copy its link.
    func workMenu(_ w: WorkAction, engine: Engine) -> some View {
        contextMenu {
            if let url = w.url, !url.isEmpty {
                Button("Open") { engine.openWeb(url, title: w.title) }
                if w.handedIn {
                    Button("See Feedback") { engine.act(on: url, title: w.title, feedback: true) }
                } else if w.open {
                    Button(w.label.0) { engine.act(on: url, title: w.title, feedback: false) }
                }
                Divider()
                Button("Open in Canvas") { engine.openWebScreen(url, title: w.title) }
                Button("Copy Link") { if let u = engine.absolute(url) { copyToPasteboard(u.absoluteString) } }
            }
        }
    }
}

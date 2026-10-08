import SwiftUI

// MARK: - Sections and rows in cards

/// A section of a page (1.2): its heading on the page itself, as `PageSection` draws it, and ONE card under it holding
/// what it has (rows, a chart, a note) — never a card in a card. The accessory (a link, a switch) sits at the heading's
/// end. `padding` is the card's own, round its rows.
struct CardSection<Content: View, Accessory: View>: View {
    let title: String
    var trailing: String? = nil
    var padding: CGFloat = 10
    @ViewBuilder var accessory: () -> Accessory
    @ViewBuilder var content: () -> Content

    var body: some View {
        PageSection(title: title, trailing: trailing, accessory: accessory) {
            VStack(alignment: .leading, spacing: 0) { content() }
                .padding(padding)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card()
        }
    }
}

extension CardSection where Accessory == EmptyView {
    init(title: String, trailing: String? = nil, padding: CGFloat = 10, @ViewBuilder content: @escaping () -> Content) {
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
                .padding(.vertical, 9)
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
        HStack(alignment: .firstTextBaseline, spacing: 9) {
            Image(systemName: symbol).frame(width: 20).accessibilityHidden(true)
            Text(text).textSelection(.enabled)
        }
        .font(.sBody)
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
                    .font(unread ? .sBody.weight(.semibold) : .sBody)
                    .lineLimit(2)
                if let sub, !sub.isEmpty {
                    Text(sub).font(.sCallout).foregroundStyle(.secondary).lineLimit(2)
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
                    .font(.sBody)
                    .lineLimit(2)
                    .strikethrough(row.done, color: .secondary)
                    .foregroundStyle(row.done ? .secondary : .primary)
                if let sub = row.sub, !sub.isEmpty {
                    Text(sub).font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer(minLength: 6)
            if let flag = row.flag { FlagBadge(flag: flag) }
            if showCourse, let course = row.course, !course.isEmpty { CourseChip(text: course, color: row.color) }
            if let time = row.time, !time.isEmpty {
                Text(time)
                    .font(.sCallout.monospacedDigit())
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

/// An announcement or a discussion in a list: who posted it, when, its first lines, its replies. `previewLines` lets a
/// wide column show more of what it says (1.2).
struct PostRowView: View {
    let row: PostRow
    var color: Color = .accentColor
    var previewLines: Int = 2

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            PersonAvatar(name: row.author ?? "", avatar: row.avatar, size: 34)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    if row.unread == true {
                        Circle().fill(Color.accentColor).frame(width: 8, height: 8).accessibilityLabel("Unread")
                    }
                    Text(row.title).font(row.unread == true ? .sBody.weight(.semibold) : .sBody).lineLimit(2)
                }
                Text([row.author, row.when].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "))
                    .font(.sFootnote).foregroundStyle(.secondary).lineLimit(1)
                if let p = row.preview, !p.isEmpty {
                    Text(p).font(.sCallout).foregroundStyle(.secondary).lineLimit(previewLines)
                }
                if row.graded == true || (row.replies ?? 0) > 0 || (row.unreadCount ?? 0) > 0 {
                    HStack(spacing: 8) {
                        if row.graded == true { StatusChip(text: "Graded", tone: "purple") }
                        if let n = row.replies, n > 0 {
                            Label("\(n)", systemImage: "bubble.left").font(.sFootnote).foregroundStyle(.secondary)
                        }
                        if let n = row.unreadCount, n > 0 {
                            Text("\(n) new").font(.sFootnote.weight(.semibold)).foregroundStyle(color)
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
                Label("Open in \(engine.lmsName)", systemImage: "globe")
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

// MARK: - A wide window's room (1.2)

/// A main column with a side column beside it once there is room for both (the side a set width, the main the rest);
/// on a narrower window the side goes under the main — or over it, with `sideFirstStacked`. Exactly two parts.
struct SideSplit: Layout {
    /// The side column's width (never more than 45% of the room).
    var side: CGFloat = 340
    /// The narrowest width the two columns sit side by side in.
    var from: CGFloat = 860
    var gap: CGFloat = 24
    /// The space between the two when one is under the other.
    var spacing: CGFloat = 22
    /// The side column on the left (a list beside what it shows), else on the right.
    var sideOnLeft = false
    var sideFirstStacked = false

    private func columns(_ width: CGFloat, _ count: Int) -> (main: CGFloat, side: CGFloat)? {
        guard count == 2, width.isFinite, width >= from else { return nil }
        let s = min(side, (width * 0.45).rounded())
        return (width - gap - s, s)
    }

    private func stacked(_ subviews: Subviews) -> [LayoutSubview] {
        let all = Array(subviews)
        return all.count == 2 && sideFirstStacked ? [all[1], all[0]] : all
    }

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        guard let width = proposal.width, width.isFinite else {
            let sizes = subviews.map { $0.sizeThatFits(.unspecified) }
            let shown = sizes.filter { $0.height > 0 }
            let height = shown.map(\.height).reduce(0, +) + spacing * CGFloat(max(shown.count - 1, 0))
            return CGSize(width: sizes.map(\.width).max() ?? 0, height: height)
        }
        if let c = columns(width, subviews.count) {
            let m = subviews[0].sizeThatFits(ProposedViewSize(width: c.main, height: nil)).height
            let s = subviews[1].sizeThatFits(ProposedViewSize(width: c.side, height: nil)).height
            return CGSize(width: width, height: max(m, s))
        }
        let heights = subviews.map { $0.sizeThatFits(ProposedViewSize(width: width, height: nil)).height }.filter { $0 > 0 }
        return CGSize(width: width, height: heights.reduce(0, +) + spacing * CGFloat(max(heights.count - 1, 0)))
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        if let c = columns(bounds.width, subviews.count) {
            let mainX = sideOnLeft ? bounds.minX + c.side + gap : bounds.minX
            let sideX = sideOnLeft ? bounds.minX : bounds.minX + c.main + gap
            subviews[0].place(at: CGPoint(x: mainX, y: bounds.minY), anchor: .topLeading, proposal: ProposedViewSize(width: c.main, height: nil))
            subviews[1].place(at: CGPoint(x: sideX, y: bounds.minY), anchor: .topLeading, proposal: ProposedViewSize(width: c.side, height: nil))
            return
        }
        var y = bounds.minY
        for s in stacked(subviews) {
            let size = ProposedViewSize(width: bounds.width, height: nil)
            s.place(at: CGPoint(x: bounds.minX, y: y), anchor: .topLeading, proposal: size)
            let h = s.sizeThatFits(size).height
            if h > 0 { y += h + spacing }
        }
    }
}

/// Pills that run along a line and wrap onto the next (a course's sections, a filter's kinds).
struct PillFlow: Layout {
    var spacing: CGFloat = 8
    var lineSpacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let room = proposal.width ?? .infinity
        var x: CGFloat = 0
        var y: CGFloat = 0
        var line: CGFloat = 0
        var widest: CGFloat = 0
        for s in subviews {
            let size = s.sizeThatFits(.unspecified)
            if x > 0 && x + size.width > room {
                y += line + lineSpacing
                x = 0
                line = 0
            }
            widest = max(widest, x + size.width)
            x += size.width + spacing
            line = max(line, size.height)
        }
        return CGSize(width: room.isFinite ? room : widest, height: subviews.isEmpty ? 0 : y + line)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX
        var y = bounds.minY
        var line: CGFloat = 0
        for s in subviews {
            let size = s.sizeThatFits(.unspecified)
            if x > bounds.minX && x + size.width > bounds.maxX {
                y += line + lineSpacing
                x = bounds.minX
                line = 0
            }
            s.place(at: CGPoint(x: x, y: y), anchor: .topLeading, proposal: ProposedViewSize(size))
            x += size.width + spacing
            line = max(line, size.height)
        }
    }
}

/// Whether the column a view is in is at least `threshold` wide: read from the width it is given, and again as the
/// window is resized. Put on a view that takes the column's whole width.
struct WidthGate: ViewModifier {
    let threshold: CGFloat
    @Binding var wide: Bool

    func body(content: Content) -> some View {
        content.background {
            GeometryReader { geo in
                Color.clear
                    .onAppear { update(geo.size.width) }
                    .onChange(of: geo.size.width) { _, w in update(w) }
            }
        }
    }

    private func update(_ width: CGFloat) {
        guard width > 0 else { return }
        let w = width >= threshold
        if w != wide { wide = w }
    }
}

extension View {
    /// Sets `wide` from the width this view is given (see `WidthGate`).
    func widthGate(_ threshold: CGFloat, wide: Binding<Bool>) -> some View {
        modifier(WidthGate(threshold: threshold, wide: wide))
    }
}

/// Sections cut into two columns where they balance best, their order kept (the first ones down the left, the rest
/// down the right): `weight` is how tall each one is likely to be.
func balancedSplit<T>(_ items: [T], weight: (T) -> Int) -> (left: [T], right: [T]) {
    guard items.count > 1 else { return (items, []) }
    let weights = items.map { max(weight($0), 1) }
    let total = weights.reduce(0, +)
    var best = 1
    var bestGap = Int.max
    var running = 0
    for k in 1..<items.count {
        running += weights[k - 1]
        let gap = abs(total - 2 * running)
        if gap < bestGap {
            bestGap = gap
            best = k
        }
    }
    return (Array(items[..<best]), Array(items[best...]))
}

/// A pill of glass that is a button (a course's section, a quick link): it answers the pointer on macOS 26, and gives a
/// little under a click everywhere (the colour alone under Reduce Motion).
struct GlassPillStyle: ButtonStyle {
    var tint: Color? = nil

    func makeBody(configuration: Configuration) -> some View {
        PillBody(configuration: configuration, tint: tint)
    }

    private struct PillBody: View {
        let configuration: ButtonStyleConfiguration
        let tint: Color?
        @State private var hover = false
        @Environment(\.accessibilityReduceMotion) private var reduceMotion

        var body: some View {
            configuration.label
                .contentShape(Capsule())
                .glassCapsule(tint: tint?.opacity(hover ? 0.2 : 0.1), interactive: true)
                .opacity(configuration.isPressed ? 0.8 : 1)
                .scaleEffect(reduceMotion ? 1 : (configuration.isPressed ? 0.97 : 1))
                .animation(Motion.snappy, value: configuration.isPressed)
                .animation(Motion.hover, value: hover)
                .onHover { hover = $0 }
        }
    }
}

/// A row picked in a list beside what it shows: the accent's wash under it (a list's own selection, not a box).
struct PickedWash: ViewModifier {
    let picked: Bool

    func body(content: Content) -> some View {
        content.background {
            RoundedRectangle(cornerRadius: 10, style: .continuous)
                .fill(Color.accentColor.opacity(picked ? 0.14 : 0))
        }
        .animation(Motion.snappy, value: picked)
    }
}

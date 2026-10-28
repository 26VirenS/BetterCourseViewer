import SwiftUI

/// (1.3.182) After an update, once: To Do and the calendar are one place now (To Do & Calendar), and Quick Plan — a
/// task dragged from To Do onto the calendar is given time there (let go on a day, at its first free time). Shown in
/// place of that update's What's New (Help ▸ What's New has its notes), never on a first run (its setup and tour are
/// for that). Over it a short scene plays over and over: the two coming together as one, then a row of To Do carried
/// onto the week, where it becomes a plan. Under Reduce Motion, the scene as it ends, still.
enum MergedIntro {
    private static let seenKey = "SimplMergedIntroSeen"

    /// The version it came with: an update from one before it brings it up.
    static let since = "1.3.182"

    /// Not seen yet (an update brings it up once, in place of What's New).
    static var due: Bool { !UserDefaults.standard.bool(forKey: seenKey) }

    /// Seen: its popup was up, or a first run made it moot.
    static func markSeen() { UserDefaults.standard.set(true, forKey: seenKey) }

    /// (1.3.189) An update from `from` brings it up: that version came before it.
    static func comesAfter(_ from: String) -> Bool { Updater.isNewer(since, than: from) }

    /// (1.3.189) As after an update from `from`: not seen yet when that came before it, else seen.
    static func pretend(from: String) {
        if comesAfter(from) { UserDefaults.standard.removeObject(forKey: seenKey) } else { markSeen() }
    }
}

struct MergedIntroItem: Identifiable {
    let id = UUID()
}

struct MergedIntroSheet: View {
    /// The popup is up (it is counted seen only then, and the update's What's New with it).
    var onShown: (() -> Void)? = nil
    /// Open To Do & Calendar: the place itself, To Do beside the calendar.
    var open: (() -> Void)? = nil
    @Environment(\.popup) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            MergeScene()
                .frame(height: 190)
                .padding(.top, 30)
                .padding(.horizontal, 28)
                .frame(maxWidth: .infinity)
                .popupHeader() // (dragged by its head, the popup moves)
            VStack(spacing: 6) {
                Text("To Do and Calendar are one")
                    .font(.system(size: 22, weight: .bold))
                    .tracking(-0.3)
                Text("Your tasks now sit beside your week, in one tab.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            .multilineTextAlignment(.center)
            .padding(.top, 18)
            .padding(.horizontal, 28)
            HStack(alignment: .top, spacing: 12) {
                IconTile(symbol: "hand.draw", color: Theme.accentIcon, size: 30)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Quick Plan")
                        .font(.sBody.weight(.semibold))
                    Text("Drag a task onto the calendar to set time aside for it. Drop it on a day and Simpl finds a free spot.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 0)
            }
            .padding(14)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Color.primary.opacity(0.05)))
            .padding(.horizontal, 28)
            .padding(.top, 18)
            HStack {
                Button("Open To Do & Calendar") {
                    dismiss()
                    open?()
                }
                .controlSize(.large)
                Spacer()
                Button("Got It") { dismiss() }
                    .buttonStyle(.borderedProminent)
                    .tint(Theme.accentFill)
                    .controlSize(.large)
                    .keyboardShortcut(.defaultAction)
            }
            .padding(.horizontal, 28)
            .padding(.vertical, 20)
        }
        .frame(width: 460)
        .background(PageGround())
        .overlay(alignment: .topLeading) { PopupCloseButton().padding(14) } // (its ×, as Got It)
        .onAppear { onShown?() }
    }
}

/// The scene, played over and over (still, as it ends, under Reduce Motion).
private struct MergeScene: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var began = Date()

    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 60, paused: reduceMotion)) { timeline in
            let t = reduceMotion ? MergeFrame.still : timeline.date.timeIntervalSince(began).truncatingRemainder(dividingBy: MergeFrame.cycle)
            GeometryReader { g in
                MergeFrame(t: t, size: g.size)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("To Do and the calendar come together as one; a task dragged onto the calendar becomes a plan")
    }
}

/// One moment of the scene, `t` seconds into it: To Do and the calendar apart, then together under one name; a row of
/// To Do taken up, carried onto Wednesday's morning and set down there as a plan (its dashed edge and clock); a while to
/// see it, then the whole fading for the next time round.
private struct MergeFrame: View {
    let t: Double
    let size: CGSize

    /// One time round, in seconds.
    static let cycle = 6.4
    /// The moment shown still (Reduce Motion): all of it done, nothing fading.
    static let still = 4.6

    private static let card = Color(nsColor: .controlBackgroundColor)
    private static let line = Color.primary.opacity(0.1)
    private static let rowTones: [Color] = [.green, .orange, .blue, .pink]
    private static let rowLengths: [CGFloat] = [0.78, 0.62, 0.7, 0.5]
    /// The week's own events: (day, hour row, colour).
    private static let events: [(Int, Int, Color)] = [(0, 2, .green), (3, 0, .orange), (4, 3, .blue), (1, 3, .pink)]

    // layout, in points inside the scene
    private let pad: CGFloat = 26
    private let top: CGFloat = 24
    private let rowHeight: CGFloat = 20
    private let rowStep: CGFloat = 28
    private let rowsTop: CGFloat = 14
    private let hourTop: CGFloat = 30
    private let hourStep: CGFloat = 30

    /// 0 to 1 over the seconds from `a` to `b`, eased in and out.
    private func step(_ a: Double, _ b: Double) -> CGFloat {
        let x = min(1, max(0, (t - a) / (b - a)))
        return CGFloat(x < 0.5 ? 4 * x * x * x : 1 - pow(-2 * x + 2, 3) / 2)
    }

    var body: some View {
        let merge = step(0.4, 1.3)
        let lift = step(1.8, 2.1)
        let carry = step(2.1, 3.1)
        let land = step(3.1, 3.4)
        let shown = step(0, 0.3) * (1 - step(5.8, 6.3))
        let inner = size.width - 2 * pad
        let todoWidth = inner * 0.36
        let calWidth = inner - todoWidth
        let height = size.height - top - 4
        let apart = 22 * (1 - merge)
        let todoX = pad - apart
        let calX = pad + todoWidth + apart
        let dayWidth = calWidth / 5
        let from = CGPoint(x: todoX + todoWidth / 2, y: top + rowsTop + rowStep + rowHeight / 2)
        let to = CGPoint(x: calX + dayWidth * 2.5, y: top + hourTop + hourStep + 14)
        let arc = sin(Double(carry) * .pi) * 24
        let at = CGPoint(x: from.x + (to.x - from.x) * carry, y: from.y + (to.y - from.y) * carry - CGFloat(arc))
        let pieceWidth = (todoWidth - 20) + ((dayWidth - 6) - (todoWidth - 20)) * land
        let pieceHeight = rowHeight + 2 * land
        ZStack(alignment: .topLeading) {
            // one card round both, once they are together
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(Self.card)
                .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Self.line))
                .frame(width: inner, height: height)
                .position(x: pad + inner / 2, y: top + height / 2)
                .opacity(Double(merge))
            todoPanel(width: todoWidth, height: height, ground: 1 - merge, taken: lift > 0)
                .position(x: todoX + todoWidth / 2, y: top + height / 2)
            calendarPanel(width: calWidth, height: height, ground: 1 - merge)
                .position(x: calX + calWidth / 2, y: top + height / 2)
            Rectangle()
                .fill(Self.line)
                .frame(width: 1, height: height - 16)
                .position(x: pad + todoWidth, y: top + height / 2)
                .opacity(Double(merge))
            // the names: each over its own, then one over both
            label("To Do").position(x: todoX + todoWidth / 2, y: top - 12).opacity(Double(1 - merge))
            label("Calendar").position(x: calX + calWidth / 2, y: top - 12).opacity(Double(1 - merge))
            label("To Do & Calendar").position(x: size.width / 2, y: top - 12).opacity(Double(merge))
            // the row carried onto the week, a plan once it is set down
            piece(width: pieceWidth, height: pieceHeight, land: land)
                .scaleEffect(1 + 0.06 * lift * (1 - land))
                .shadow(color: .black.opacity(Double(0.22 * lift * (1 - land))), radius: 8, y: 4)
                .position(at)
                .opacity(lift > 0 ? 1 : 0)
            Image(systemName: "cursorarrow")
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(Color.primary)
                .shadow(color: .black.opacity(0.25), radius: 1)
                .position(x: at.x + 14, y: at.y + 10)
                .opacity(Double(lift * (1 - land)))
        }
        .frame(width: size.width, height: size.height)
        .opacity(Double(shown))
    }

    private func label(_ s: String) -> some View {
        Text(s)
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(.secondary)
            .fixedSize()
    }

    /// To Do: four rows, each a tick, a line of words and its course's dot (the second faint once it is taken up).
    private func todoPanel(width: CGFloat, height: CGFloat, ground: CGFloat, taken: Bool) -> some View {
        ZStack(alignment: .topLeading) {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(Self.card)
                .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Self.line))
                .opacity(Double(ground))
            VStack(alignment: .leading, spacing: rowStep - rowHeight) {
                ForEach(0..<4, id: \.self) { i in
                    row(i, width: width - 20)
                        .opacity(taken && i == 1 ? 0.25 : 1)
                }
            }
            .padding(.leading, 10)
            .padding(.top, rowsTop)
        }
        .frame(width: width, height: height)
    }

    private func row(_ i: Int, width: CGFloat) -> some View {
        HStack(spacing: 6) {
            Circle()
                .strokeBorder(Color.primary.opacity(0.35), lineWidth: 1.5)
                .frame(width: 10, height: 10)
            RoundedRectangle(cornerRadius: 3)
                .fill(Color.primary.opacity(0.2))
                .frame(width: max(8, (width - 40) * Self.rowLengths[i]), height: 6)
            Spacer(minLength: 0)
            Circle()
                .fill(Self.rowTones[i])
                .frame(width: 6, height: 6)
        }
        .padding(.horizontal, 6)
        .frame(width: width, height: rowHeight)
        .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.primary.opacity(0.05)))
    }

    /// The week: its five days' heads, its hours' lines and a few of its own events.
    private func calendarPanel(width: CGFloat, height: CGFloat, ground: CGFloat) -> some View {
        let hourTop = self.hourTop, hourStep = self.hourStep
        return ZStack {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(Self.card)
                .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Self.line))
                .opacity(Double(ground))
            Canvas { ctx, s in
                let day = s.width / 5
                for c in 0..<5 {
                    let head = CGRect(x: day * CGFloat(c) + day / 2 - 9, y: 12, width: 18, height: 5)
                    ctx.fill(Path(roundedRect: head, cornerRadius: 2.5), with: .color(Color.primary.opacity(0.22)))
                    if c > 0 {
                        var p = Path()
                        p.move(to: CGPoint(x: day * CGFloat(c), y: hourTop - 6))
                        p.addLine(to: CGPoint(x: day * CGFloat(c), y: s.height - 8))
                        ctx.stroke(p, with: .color(Color.primary.opacity(0.08)), lineWidth: 1)
                    }
                }
                for r in 0..<4 {
                    let y = hourTop + CGFloat(r) * hourStep
                    var p = Path()
                    p.move(to: CGPoint(x: 6, y: y))
                    p.addLine(to: CGPoint(x: s.width - 6, y: y))
                    ctx.stroke(p, with: .color(Color.primary.opacity(0.08)), lineWidth: 1)
                }
                for (c, r, tone) in Self.events {
                    let block = CGRect(x: day * CGFloat(c) + 3, y: hourTop + CGFloat(r) * hourStep + 3, width: day - 6, height: 22)
                    ctx.fill(Path(roundedRect: block, cornerRadius: 5), with: .color(tone.opacity(0.3)))
                }
            }
        }
        .frame(width: width, height: height)
    }

    /// The row carried, turning into a plan as it is set down: the theme's colour, a dashed edge and a clock.
    private func piece(width: CGFloat, height: CGFloat, land: CGFloat) -> some View {
        ZStack {
            row(1, width: width)
                .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Self.card))
                .opacity(Double(1 - land))
            RoundedRectangle(cornerRadius: 5, style: .continuous)
                .fill(Theme.accent.opacity(0.28))
                .overlay(RoundedRectangle(cornerRadius: 5, style: .continuous)
                    .strokeBorder(Theme.accentIcon, style: StrokeStyle(lineWidth: 1.2, dash: [3, 2])))
                .overlay(alignment: .leading) {
                    Image(systemName: "clock")
                        .font(.system(size: 9, weight: .semibold))
                        .foregroundStyle(Theme.accentText)
                        .padding(.leading, 5)
                }
                .opacity(Double(land))
        }
        .frame(width: width, height: height)
    }
}

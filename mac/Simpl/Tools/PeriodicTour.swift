import SwiftUI

/// (1.3.59) A short tour of the full Periodic Table — (tools-3) offered the first time it opens ("Take a quick tour?"),
/// never started on its own, and run from its Tour button. (1.3.61) Each step dims the tool and lights the control it is
/// about. (1.3.62) Each suggests one thing — (1.3.80) hover an element, pick a colour view, open an element, search, and
/// last, open the element's atom in Atoms & Orbitals — and moves on by itself once it is done, its card (where it is,
/// what it is, what to try, a bar for how far) beside the control, not over it. (1.3.80: the table's tour only — its
/// steps through the Lewis and 3D builders went with them, now tools of their own; the last step's press takes the
/// student into Atoms & Orbitals, and the tour, done, with it.) (tools-3) As the app's own tour: every card has Skip
/// Tour, a ✕, Back and a Next that always works; Escape ends it; ended early, it counts as seen.
struct PTTourStep {
    enum Act {
        case hover, colour, open, typed
        case event(PTTourEvent)
    }
    enum Gesture { case click, hover, drag, type }

    let title: String
    let text: String
    /// What to do, said with the gesture it takes.
    let doing: String
    var gesture: Gesture = .click
    let act: Act
    /// The control the step lights (`tourTarget`).
    let target: String
    /// The element whose page the tool opens when the step comes, if none is open.
    var element: Int? = nil

    static let all: [PTTourStep] = [
        // (tools-3: the table's top rows lit — where an element's card shows — the card under them, not over the table)
        PTTourStep(title: "The periodic table", text: "Point at any element to see its card in the space at the top.",
                   doing: "Hover an element", gesture: .hover, act: .hover, target: "ptTop"),
        PTTourStep(title: "Colour by a property", text: "Each view colours the table; arrows show its trend.",
                   doing: "Click Electronegativity", act: .colour, target: "ptColours"),
        PTTourStep(title: "Every element, in depth", text: "Its page has its electrons, how it compares and every fact.",
                   doing: "Click carbon, or any element", act: .open, target: "ptCarbon"),
        PTTourStep(title: "Find anything", text: "A symbol, name or number; Return opens it.",
                   doing: "Search for an element", gesture: .type, act: .typed, target: "ptSearch"),
        PTTourStep(title: "Its atom in 3D", text: "Its atom, orbitals and electron cloud turn in 3D, here on its page.",
                   doing: "Pick Orbitals or Cloud", act: .event(.atoms), target: "ptAtoms", element: 6),
    ]
}

/// (1.3.62) What a step may wait for that the tool's own state does not show: (1.3.80) the element's atom opened in
/// Atoms & Orbitals.
enum PTTourEvent: Equatable { case atoms }

/// The tour's word with the parts that keep their own state: what was just done.
final class PTTour: ObservableObject {
    static let shared = PTTour()
    /// Whether the tour is on (nothing done is told it otherwise; tools-3: and the sheet's Done leaves Escape to it).
    @Published var active = false
    /// Which table runs it (PeriodicTableTool's mark): only that one puts it away.
    var owner: UUID?
    @Published private(set) var event: PTTourEvent?
    @Published private(set) var events = 0

    /// Something a step may be waiting for, done in the tool.
    func did(_ e: PTTourEvent) {
        guard active else { return }
        event = e
        events += 1
    }

    func clearEvent() { event = nil }
}

// MARK: - Where the tour's controls are

/// (1.3.61) Where each control the tour can light is on screen, in the window's coordinates.
final class PTTourFrames: ObservableObject {
    static let shared = PTTourFrames()
    @Published private(set) var frames: [String: CGRect] = [:]
    /// (1.3.65) kept quietly while the tour is off (scrolling moves them all the time), told only while it is on
    private var quiet: [String: CGRect] = [:]

    func set(_ id: String, _ rect: CGRect?) {
        guard quiet[id] != rect else { return }
        quiet[id] = rect
        if PTTour.shared.active { frames[id] = rect }
    }

    /// The tour beginning: where everything is now.
    func wake() { if frames != quiet { frames = quiet } }
}

private struct TourTarget: ViewModifier {
    let id: String

    func body(content: Content) -> some View {
        content.background {
            GeometryReader { g in
                let f = g.frame(in: .global)
                Color.clear
                    .onAppear { PTTourFrames.shared.set(id, f) }
                    .onChange(of: f) { _, n in PTTourFrames.shared.set(id, n) }
                    .onDisappear { PTTourFrames.shared.set(id, nil) }
            }
        }
    }
}

extension View {
    /// (1.3.61) A control the tour can light, by name; nil: none.
    @ViewBuilder
    func tourTarget(_ id: String?) -> some View {
        if let id { modifier(TourTarget(id: id)) } else { self }
    }
}

// MARK: - The spotlight

/// The tool dimmed but for a rounded hole round the control lit; its edges move smoothly from step to step.
private struct Spotlight: Shape {
    var hole: CGRect

    var animatableData: AnimatablePair<AnimatablePair<CGFloat, CGFloat>, AnimatablePair<CGFloat, CGFloat>> {
        get { AnimatablePair(AnimatablePair(hole.minX, hole.minY), AnimatablePair(hole.width, hole.height)) }
        set { hole = CGRect(x: newValue.first.first, y: newValue.first.second, width: newValue.second.first, height: newValue.second.second) }
    }

    func path(in rect: CGRect) -> Path {
        var p = Path(rect.insetBy(dx: -2000, dy: -2000))
        p.addRoundedRect(in: hole, cornerSize: CGSize(width: 16, height: 16), style: .continuous)
        return p
    }
}

/// The guided tour over the tool: the dim with its spotlight, a ring round the control, and the step's card beside it —
/// (tools-3) under it where there is room, else over it, else to its right, its arrow pointing at the control; only where
/// none of those has room (a control as big as the tool) in the tool's bottom corner, arrowless. Presses go through to
/// the tool, so what the step suggests can be done. `origin` is the tool's top left in the window's coordinates; `size`
/// the tool's size.
struct PTTourOverlay: View {
    let step: Int
    let origin: CGPoint
    let size: CGSize
    var done = false
    let next: () -> Void
    let back: () -> Void
    let skip: () -> Void
    @ObservedObject private var frames = PTTourFrames.shared
    @State private var cardSize = CGSize(width: PTTourCard.width, height: 190)

    var body: some View {
        let s = PTTourStep.all[step]
        let global = frames.frames[s.target]
        // (the control in the tool's coordinates, a little room round it; none yet: a small hole at the middle)
        let hole: CGRect = global.map { $0.offsetBy(dx: -origin.x, dy: -origin.y).insetBy(dx: -8, dy: -8) }
            ?? CGRect(x: size.width / 2 - 1, y: size.height / 2 - 1, width: 2, height: 2)
        let place = PTTourOverlay.place(hole: hole, card: CGSize(width: PTTourCard.width, height: cardSize.height), in: size)
        ZStack(alignment: .topLeading) {
            Spotlight(hole: hole)
                .fill(Color.black.opacity(0.42), style: FillStyle(eoFill: true))
                .allowsHitTesting(false)
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .stroke(Theme.accent, lineWidth: 2.5)
                .frame(width: hole.width, height: hole.height)
                .offset(x: hole.minX, y: hole.minY)
                .shadow(color: Theme.accent.opacity(0.6), radius: 10)
                .allowsHitTesting(false)
            PTTourCard(step: step, arrow: place.arrow, arrowAt: place.arrowAt, done: done, next: next, back: back, skip: skip, cardSize: $cardSize)
                .offset(x: place.origin.x, y: place.origin.y)
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
        .animation(.spring(response: 0.45, dampingFraction: 0.88), value: hole)
        .animation(.spring(response: 0.45, dampingFraction: 0.88), value: step)
    }

    /// Where the card goes beside the lit control: its top left (the arrow's room included), which edge its arrow is on,
    /// and how far along that edge the arrow sits.
    static func place(hole: CGRect, card: CGSize, in size: CGSize) -> (origin: CGPoint, arrow: TourArrow, arrowAt: CGFloat) {
        let gap: CGFloat = 8, edge: CGFloat = 12, arrow = CalloutMetrics.length
        let w = card.width, h = card.height
        func across(_ x: CGFloat) -> CGFloat { min(max(x, edge), max(edge, size.width - w - edge)) }
        func down(_ y: CGFloat) -> CGFloat { min(max(y, edge), max(edge, size.height - h - edge)) }
        if hole.maxY + gap + arrow + h + edge <= size.height {
            let x = across(hole.midX - w / 2)
            return (CGPoint(x: x, y: hole.maxY + gap), .top, hole.midX - x)
        }
        if hole.minY - gap - arrow - h >= edge {
            let x = across(hole.midX - w / 2)
            return (CGPoint(x: x, y: hole.minY - gap - arrow - h), .bottom, hole.midX - x)
        }
        if hole.maxX + gap + arrow + w + edge <= size.width {
            let y = down(hole.midY - h / 2)
            return (CGPoint(x: hole.maxX + gap, y: y), .leading, hole.midY - y)
        }
        return (CGPoint(x: size.width - w - edge, y: size.height - h - edge), .none, 0)
    }
}

/// The tour's card, as the app tour's: where it is ("3 of 11") and its ✕, what it is, what to try with its gesture
/// pulsing, a bar for how far, and (tools-3) the same controls on every step — Skip Tour, Back, and a Next that always
/// works (Done on the last).
struct PTTourCard: View {
    static let width: CGFloat = 320

    let step: Int
    let arrow: TourArrow
    let arrowAt: CGFloat
    var done = false
    let next: () -> Void
    let back: () -> Void
    let skip: () -> Void
    @Binding var cardSize: CGSize

    var body: some View {
        let s = PTTourStep.all[step]
        let count = PTTourStep.all.count
        let last = step == count - 1
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Text("\(step + 1) of \(count)")
                    .font(.sFootnote.weight(.semibold).monospacedDigit())
                    .foregroundStyle(.secondary)
                Spacer(minLength: 8)
                Button(action: skip) {
                    Image(systemName: "xmark")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundStyle(.secondary)
                        .frame(width: 22, height: 22)
                        .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .keyboardShortcut(.cancelAction) // (Escape ends the tour, before anything else it would close)
                .help("Close the tour (Esc)")
                .accessibilityLabel("Close the tour")
                .padding(.trailing, -6)
            }
            Text(s.title).font(.sHeadline)
            Text(s.text)
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            // (what to do, its gesture pulsing; 1.3.64: done, a green tick in its place for a moment)
            Group {
                if done {
                    HStack(spacing: 7) {
                        Image(systemName: "checkmark.circle.fill").font(.sCallout.weight(.semibold))
                        Text(step == count - 1 ? "All done" : "Nice").font(.sCallout.weight(.semibold))
                    }
                    .foregroundStyle(.green)
                    .transition(.scale(scale: 0.8).combined(with: .opacity))
                } else {
                    HStack(spacing: 7) {
                        Image(systemName: symbol(s.gesture))
                            .font(.sCallout.weight(.semibold))
                            .symbolEffect(.pulse, options: .repeating)
                        Text(s.doing).font(.sCallout.weight(.semibold))
                    }
                    .foregroundStyle(Theme.accent)
                    .transition(.opacity)
                }
            }
            .padding(.top, 2)
            ProgressView(value: Double(step + (done ? 1 : 0)), total: Double(count))
                .progressViewStyle(.linear)
                .tint(Theme.accent)
                .accessibilityHidden(true)
                .padding(.top, 4)
            HStack(spacing: 8) {
                if !last {
                    Button("Skip Tour", action: skip)
                        .buttonStyle(.plain)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .help("End the tour (Esc). The Tour button runs it again.")
                }
                Spacer(minLength: 6)
                if step > 0 {
                    Button("Back", action: back)
                        .glassButton()
                }
                Button(last ? "Done" : "Next", action: next)
                    .glassButton(prominent: true)
            }
            .controlSize(.regular)
        }
        .id(step)
        .padding(18)
        .frame(width: PTTourCard.width, alignment: .leading)
        .background {
            GeometryReader { p in
                Color.clear
                    .onAppear { cardSize = p.size }
                    .onChange(of: p.size) { _, n in cardSize = n }
            }
        }
        .padding(.top, arrow == .top ? CalloutMetrics.length : 0)
        .padding(.bottom, arrow == .bottom ? CalloutMetrics.length : 0)
        .padding(.leading, arrow == .leading ? CalloutMetrics.length : 0)
        .glass(CalloutShape(arrow: arrow, at: arrowAt))
        .shadow(color: .black.opacity(0.18), radius: 24, y: 10)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Tour, step \(step + 1) of \(count): \(s.title). \(s.doing)")
    }

    private func symbol(_ g: PTTourStep.Gesture) -> String {
        switch g {
        case .click: return "cursorarrow.click"
        case .hover: return "cursorarrow.rays"
        case .drag: return "hand.draw"
        case .type: return "keyboard"
        }
    }
}

/// (tools-3) The tour offered, the first time the table opens, rather than started: a small card at the tool's foot —
/// Take the Tour, or Not Now, which counts as seen (the Tour button runs it any time). A student who came to look up one
/// element is not put through the steps first.
struct PTTourOffer: View {
    let take: () -> Void
    let later: () -> Void

    var body: some View {
        HStack(spacing: 14) {
            Image(systemName: "questionmark.circle")
                .font(.system(size: 22, weight: .semibold))
                .foregroundStyle(Theme.accent)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text("Take a quick tour?")
                    .font(.sHeadline)
                Text("A few short steps: the table, its colours, an element’s page and its atom in 3D.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .frame(maxWidth: 320, alignment: .leading)
            Button("Not Now", action: later)
                .glassButton()
            Button("Take the Tour", action: take)
                .glassButton(prominent: true)
        }
        .controlSize(.large)
        .padding(16)
        .glassCard(radius: 20)
        .shadow(color: .black.opacity(0.18), radius: 24, y: 10)
        .accessibilityElement(children: .contain)
    }
}

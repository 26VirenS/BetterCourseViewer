import SwiftUI

/// (1.3.59) A short tour of the full Periodic Table, shown the first time it opens (and from its Tour button).
/// (1.3.61) Each step dims the tool and lights the control it is about. (1.3.62) As the app's own tour (Shell/Tour.swift)
/// is: each step asks for one thing — hover an element, pick a colour view, open an element, see its orbitals, go to the
/// Lewis builder, choose a preset, draw, look at a resonance structure, go to the 3D builder, turn the model, search —
/// and moves on by itself once it is done, its card (the app tour's: where it is, what it is, what to do, a bar for how
/// far) pointing at the control. A Next shows only if a step is left undone a while.
struct PTTourStep {
    enum Act {
        case hover, colour, open, typed
        case page(PeriodicTableTool.Page)
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
    /// What the tool is set to when the step comes: an element's page, the Lewis builder's preset, the 3D builder's.
    var element: Int? = nil
    var lewis: String? = nil
    var molecule: String? = nil

    static let all: [PTTourStep] = [
        PTTourStep(title: "The periodic table", text: "Point at any element to see its card up top.",
                   doing: "Hover an element", gesture: .hover, act: .hover, target: "ptGrid"),
        PTTourStep(title: "Colour by a property", text: "Each view colours the table; arrows show its trend.",
                   doing: "Click Electronegativity", act: .colour, target: "ptColours"),
        PTTourStep(title: "Every element, in depth", text: "Its page has a 3D atom, its electrons and every fact.",
                   doing: "Click an element", act: .open, target: "ptGrid"),
        PTTourStep(title: "Orbitals", text: "See the shapes its electrons fill. Drag to turn it.",
                   doing: "Click Orbitals", act: .event(.orbitals), target: "ptModel", element: 6),
        PTTourStep(title: "Lewis structures", text: "Draw any molecule's bonds and lone pairs.",
                   doing: "Click Lewis structures", act: .page(.lewis), target: "ptPageLewis"),
        PTTourStep(title: "Start from a preset", text: "Or pick a centre atom and what surrounds it.",
                   doing: "Choose a preset", act: .event(.preset), target: "lewisPreset"),
        PTTourStep(title: "Draw your own", text: "Place atoms, drag to bond, click a bond to change it.",
                   doing: "Click Draw", act: .event(.draw), target: "lewisWay"),
        PTTourStep(title: "Resonance", text: "Equivalent structures sit side by side.",
                   doing: "Click a structure", act: .event(.resonance), target: "lewisResonance", lewis: "NO₃⁻"),
        PTTourStep(title: "Build it in 3D", text: "Open in 3D sends any Lewis structure here too.",
                   doing: "Click Molecule builder", act: .page(.molecules), target: "ptPageMolecules"),
        PTTourStep(title: "Turn it", text: "Scroll to zoom; click an atom for its shape.",
                   doing: "Drag the model", gesture: .drag, act: .event(.turned), target: "molViewer", molecule: "Ethanol"),
        PTTourStep(title: "Find anything", text: "A symbol, name or number; Return opens it.",
                   doing: "Search for an element", gesture: .type, act: .typed, target: "ptSearch"),
    ]
}

/// (1.3.62) What a step may wait for that the tool's own state does not show.
enum PTTourEvent: Equatable { case orbitals, preset, draw, resonance, turned }

/// The tour's word with the parts that keep their own state: a preset for the Lewis builder, and what was just done.
final class PTTour: ObservableObject {
    static let shared = PTTour()
    @Published var lewisPreset: String?
    @Published var lewisNonce = 0
    /// Whether the tour is on (nothing done is told it otherwise).
    var active = false
    @Published private(set) var event: PTTourEvent?
    @Published private(set) var events = 0

    func showLewis(_ name: String?) {
        lewisPreset = name
        if name != nil { lewisNonce += 1 }
    }

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

/// The guided tour over the tool: the dim with its spotlight, a ring round the control, and the step's card beside it
/// (under it when there is room, else over it), its arrow pointing at the control. Presses go through to the tool, so
/// what the step asks can be done. `origin` is the tool's top left in the window's coordinates; `size` the tool's size.
struct PTTourOverlay: View {
    let step: Int
    let origin: CGPoint
    let size: CGSize
    let stuck: Bool
    var done = false
    let next: () -> Void
    @ObservedObject private var frames = PTTourFrames.shared
    @State private var cardSize = CGSize(width: PTTourCard.width, height: 170)

    var body: some View {
        let s = PTTourStep.all[step]
        let global = frames.frames[s.target]
        // (the control in the tool's coordinates, a little room round it; none yet: a small hole at the middle)
        let hole: CGRect = global.map { $0.offsetBy(dx: -origin.x, dy: -origin.y).insetBy(dx: -8, dy: -8) }
            ?? CGRect(x: size.width / 2 - 1, y: size.height / 2 - 1, width: 2, height: 2)
        let w = PTTourCard.width, h = cardSize.height
        let below = hole.maxY + h + 16 < size.height || hole.minY - h - 16 < 0
        let x = min(max(hole.midX - w / 2, 16), max(16, size.width - w - 16))
        let y = below ? min(hole.maxY + 10, size.height - h - 8) : max(hole.minY - h - 10, 8)
        let arrowAt = hole.midX - x
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
            PTTourCard(step: step, arrow: below ? .top : .bottom, arrowAt: arrowAt, stuck: stuck, done: done, next: next, cardSize: $cardSize)
                .offset(x: x, y: y)
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
        .animation(.spring(response: 0.45, dampingFraction: 0.88), value: hole)
        .animation(.spring(response: 0.45, dampingFraction: 0.88), value: step)
    }
}

/// The tour's card, as the app tour's: where it is ("3 of 11"), what it is, what to do with its gesture pulsing, and a
/// bar for how far — a Next only when the step has been left undone a while (or is the last's Done).
struct PTTourCard: View {
    static let width: CGFloat = 300

    let step: Int
    let arrow: TourArrow
    let arrowAt: CGFloat
    let stuck: Bool
    var done = false
    let next: () -> Void
    @Binding var cardSize: CGSize

    var body: some View {
        let s = PTTourStep.all[step]
        let count = PTTourStep.all.count
        VStack(alignment: .leading, spacing: 8) {
            Text("\(step + 1) of \(count)")
                .font(.sFootnote.weight(.semibold).monospacedDigit())
                .foregroundStyle(.secondary)
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
            HStack(spacing: 10) {
                ProgressView(value: Double(step + (done ? 1 : 0)), total: Double(count))
                    .progressViewStyle(.linear)
                    .tint(Theme.accent)
                    .frame(maxWidth: 90)
                Spacer(minLength: 6)
                if stuck {
                    Button("Next", action: next)
                        .buttonStyle(.plain)
                        .font(.sFootnote.weight(.medium))
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.top, 4)
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
        .padding(arrow == .top ? EdgeInsets(top: CalloutMetrics.length, leading: 0, bottom: 0, trailing: 0)
                                : EdgeInsets(top: 0, leading: 0, bottom: CalloutMetrics.length, trailing: 0))
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

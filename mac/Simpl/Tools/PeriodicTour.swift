import SwiftUI

/// (1.3.59) A short tour of the full Periodic Table, shown the first time it opens (and from its Tour button), each step
/// setting the tool to what it describes — the table, a colour view, an element's page, the Lewis builder's quick and
/// drawn ways and its resonance, the 3D builder, search. (1.3.61) A guided tour: each step dims the tool and lights the
/// control it is about — a spotlight cut round it, ringed — with its card beside it, pointing at it.
struct PTTourStep {
    let symbol: String
    let title: String
    let text: String
    /// The control the step lights (`tourTarget`).
    var target: String
    /// An element shown as if pointed at (the table's preview).
    var hover: Int? = nil
    var page: PeriodicTableTool.Page = .table
    var colouring: PTColouring = .category
    var element: Int? = nil
    var lewis: String? = nil
    var molecule: String? = nil

    static let all: [PTTourStep] = [
        PTTourStep(symbol: "hand.point.up.left", title: "Point at an element",
                   text: "Its card shows up top. Click it to open its page.", target: "ptCarbon", hover: 6),
        PTTourStep(symbol: "paintpalette", title: "Colour by a property",
                   text: "Pick a view. The arrows show its trend.", target: "ptColours", colouring: .electronegativity),
        PTTourStep(symbol: "atom", title: "Every element, in 3D",
                   text: "Drag to turn the atom; Orbitals shows their shapes. ← → step through.", target: "ptModel", element: 6),
        PTTourStep(symbol: "circle.grid.cross", title: "Lewis structures",
                   text: "Pick a centre atom and what surrounds it.", target: "lewisCentre", page: .lewis, lewis: "H₂O"),
        PTTourStep(symbol: "pencil", title: "Draw your own",
                   text: "Click to place, drag to bond, click a bond to change it.", target: "lewisCanvas", page: .lewis, lewis: "Benzene"),
        PTTourStep(symbol: "arrow.left.arrow.right", title: "Resonance",
                   text: "Every equivalent structure, side by side. Click one to see it large.", target: "lewisResonance",
                   page: .lewis, lewis: "NO₃⁻"),
        PTTourStep(symbol: "rotate.3d", title: "Build it in 3D",
                   text: "Drag to turn, scroll to zoom, click an atom. Spin on or off up here.", target: "molViewer",
                   page: .molecules, molecule: "Ethanol"),
        PTTourStep(symbol: "magnifyingglass", title: "Find anything",
                   text: "A symbol, name or number. Return opens it.", target: "ptSearch"),
    ]
}

/// What the tour asks of the Lewis builder, which keeps its own state: a preset to load, raised each step.
final class PTTour: ObservableObject {
    static let shared = PTTour()
    @Published var lewisPreset: String?
    @Published var lewisNonce = 0

    func showLewis(_ name: String?) {
        lewisPreset = name
        if name != nil { lewisNonce += 1 }
    }
}

/// The tour's card: the step's sign, title and line; dots for where it is; Skip, Back and Next (Return) or Done.
struct PTTourCard: View {
    let step: Int
    let back: () -> Void
    let next: () -> Void
    let skip: () -> Void
    var width: CGFloat = 440

    var body: some View {
        let s = PTTourStep.all[step]
        let last = step == PTTourStep.all.count - 1
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .top, spacing: 12) {
                Image(systemName: s.symbol)
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundStyle(Theme.accent)
                    .frame(width: 36, height: 36)
                    .background(Theme.accent.opacity(0.14), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                VStack(alignment: .leading, spacing: 3) {
                    Text(s.title).font(.sBody.weight(.semibold))
                    Text(s.text).font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
                .id(step)
                .transition(.opacity)
                Spacer(minLength: 0)
            }
            HStack(spacing: 10) {
                HStack(spacing: 5) {
                    ForEach(PTTourStep.all.indices, id: \.self) { i in
                        Capsule()
                            .fill(i == step ? Theme.accent : Color.primary.opacity(0.18))
                            .frame(width: i == step ? 16 : 6, height: 6)
                    }
                }
                Spacer(minLength: 8)
                if !last {
                    Button("Skip", action: skip)
                        .buttonStyle(.plain)
                        .foregroundStyle(.secondary)
                        .keyboardShortcut(.cancelAction)
                }
                if step > 0 {
                    Button("Back", action: back).glassButton()
                }
                Button(last ? "Done" : "Next", action: next)
                    .glassButton(prominent: true)
                    .keyboardShortcut(.defaultAction)
            }
            .font(.sCallout.weight(.medium))
        }
        .padding(16)
        .frame(width: width)
        .glassCard(radius: 20)
        .shadow(color: .black.opacity(0.18), radius: 24, y: 10)
    }
}

// MARK: - Where the tour's controls are

/// (1.3.61) Where each control the tour can light is on screen, in the window's coordinates.
final class PTTourFrames: ObservableObject {
    static let shared = PTTourFrames()
    @Published private(set) var frames: [String: CGRect] = [:]

    func set(_ id: String, _ rect: CGRect?) {
        if frames[id] != rect { frames[id] = rect }
    }
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
/// (under it when there is room, else over it), its arrow pointing at the control. `origin` is the tool's top left in
/// the window's coordinates; `size` the tool's size.
struct PTTourOverlay: View {
    let step: Int
    let origin: CGPoint
    let size: CGSize
    let back: () -> Void
    let next: () -> Void
    let skip: () -> Void
    @ObservedObject private var frames = PTTourFrames.shared

    var body: some View {
        let s = PTTourStep.all[step]
        let global = frames.frames[s.target]
        // (the control in the tool's coordinates, a little room round it; none yet: a small hole at the middle)
        let hole: CGRect = global.map { $0.offsetBy(dx: -origin.x, dy: -origin.y).insetBy(dx: -8, dy: -8) }
            ?? CGRect(x: size.width / 2 - 1, y: size.height / 2 - 1, width: 2, height: 2)
        let cardW: CGFloat = 380
        let cardH: CGFloat = 150
        let below = hole.maxY + cardH + 24 < size.height || hole.minY - cardH - 24 < 0
        let x = min(max(hole.midX - cardW / 2, 16), max(16, size.width - cardW - 16))
        let y = below ? min(hole.maxY + 16, size.height - cardH - 8) : max(hole.minY - cardH - 16, 8)
        let arrowX = min(max(hole.midX - x, 28), cardW - 28)
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
            VStack(spacing: 0) {
                if below { arrow(up: true).offset(x: arrowX - cardW / 2) }
                PTTourCard(step: step, back: back, next: next, skip: skip, width: cardW)
                if !below { arrow(up: false).offset(x: arrowX - cardW / 2) }
            }
            .frame(width: cardW)
            .offset(x: x, y: below ? y - 10 : y)
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
        .animation(.spring(response: 0.45, dampingFraction: 0.88), value: hole)
        .animation(.spring(response: 0.45, dampingFraction: 0.88), value: step)
    }

    private func arrow(up: Bool) -> some View {
        Triangle()
            .fill(.regularMaterial)
            .frame(width: 22, height: 11)
            .rotationEffect(.degrees(up ? 0 : 180))
    }
}

private struct Triangle: Shape {
    func path(in r: CGRect) -> Path {
        var p = Path()
        p.move(to: CGPoint(x: r.midX, y: r.minY))
        p.addLine(to: CGPoint(x: r.maxX, y: r.maxY))
        p.addLine(to: CGPoint(x: r.minX, y: r.maxY))
        p.closeSubpath()
        return p
    }
}

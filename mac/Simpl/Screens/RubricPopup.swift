import SwiftUI

// The rubric ring over the window (1.2.3): the web's rubric-ring.js overlay on the Mac. An assignment's page shows its
// rubric as one row (RubricSummaryRow) and the grade card's rubric button; either brings the ring up over the whole
// window — sidebar and all — with nothing round it: the window behind dims and blurs, so nothing of it
// reads through. The ring blooms in and, marked, bends to its marks; a slice, its label or Return opens a
// criterion into its bar (RubricStage.swift); the little ring on the bar, Escape or a press beside it rolls the bar back
// up; Escape or a press beside the ring (or ×) closes it. Ring or Grid at the top right (RubricPanels.swift).

/// What the rubric ring is showing over the window, if anything. One for the app: MainShell puts its layer over the
/// whole window, and an assignment's page asks it to show its rubric.
@MainActor
final class RubricPopup: ObservableObject {
    static let shared = RubricPopup()

    struct Item: Identifiable {
        let id = UUID()
        /// The assignment it is for ("course/id"): its page going away takes the ring with it.
        let owner: String
        let title: String
        let rubricTitle: String
        let context: String
        let rows: [RubricRow]
        let held: Bool
        /// A criterion to open on, and Ring or Grid to show (the screenshot suite's rubric:1 and rubric:grid): not kept.
        let open: Int?
        let view: String?
    }

    @Published private(set) var item: Item?
    /// On its way out: the ring fades and the window comes back into focus; then it goes.
    @Published private(set) var leaving = false

    func show(_ next: Item) {
        guard !next.rows.isEmpty else { return }
        leaving = false
        item = next
    }

    func dismiss() {
        guard let current = item, !leaving else { return }
        leaving = true
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 430_000_000)
            guard let self, self.item?.id == current.id else { return }
            self.item = nil
            self.leaving = false
        }
    }

    /// Closes the ring if it is the one an assignment's page put up.
    func dismiss(owner: String) {
        if item?.owner == owner { dismiss() }
    }
}

extension RubricPopup.Item {
    init(data d: AssignmentData, owner: String, open: Int? = nil, view: String? = nil) {
        self.init(owner: owner, title: d.title, rubricTitle: (d.rubricTitle ?? "").trimmingCharacters(in: .whitespacesAndNewlines),
                  context: d.context ?? "", rows: d.rubric, held: d.held == true, open: open, view: view)
    }
}

/// The ring's layer over the whole window (MainShell): empty, and letting every press through, until a page asks.
struct RubricPopupLayer: View {
    @ObservedObject private var popup = RubricPopup.shared

    var body: some View {
        ZStack {
            if let item = popup.item {
                RubricPopupView(item: item, leaving: popup.leaving) { popup.dismiss() }
                    .id(item.id)
            }
        }
        .allowsHitTesting(popup.item != nil && !popup.leaving)
    }
}

/// Where the stage stands in the window: below the title, above the foot's words, as large as fits (a little larger
/// than the web's units on a roomy window).
struct RingFit {
    let scale: CGFloat
    let origin: CGPoint
    let width: CGFloat
    let height: CGFloat

    init(size: CGSize) {
        let roomW = max(200, size.width - 24)
        let roomH = max(200, size.height - 148)
        let s = max(0.4, min(1.3, min(roomW / CGFloat(RingUnits.width), roomH / CGFloat(RingUnits.height))))
        scale = s
        width = CGFloat(RingUnits.width) * s
        height = CGFloat(RingUnits.height) * s
        origin = CGPoint(x: 12 + (roomW - width) / 2, y: 100 + (roomH - height) / 2)
    }

    /// The ring's middle, in the window.
    var centre: CGPoint {
        CGPoint(x: origin.x + CGFloat(RingUnits.cx) * scale, y: origin.y + CGFloat(RingUnits.cy) * scale)
    }

    /// A point of the window in the stage's units.
    func units(_ p: CGPoint) -> (x: Double, y: Double) {
        (Double((p.x - origin.x) / scale), Double((p.y - origin.y) / scale))
    }
}

/// The rubric ring over the window: the veils, the stage (or the grid), and round it the title, × and Ring / Grid, and
/// a line of what to do. ← → walk the criteria, Return opens one, Escape steps back and then closes.
struct RubricPopupView: View {
    let item: RubricPopup.Item
    let leaving: Bool
    let close: () -> Void
    private let model: RubricModel
    private let geo: RingGeometry
    @StateObject private var motion: RingMotion
    @AppStorage("rubricView") private var keptView = "ring"
    @State private var view: String
    @State private var shown = false
    /// The slice the pointer is on over the ring (from the presses' layer, not a label).
    @State private var ringHit = -1
    @FocusState private var focused: Bool
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(item: RubricPopup.Item, leaving: Bool, close: @escaping () -> Void) {
        self.item = item
        self.leaving = leaving
        self.close = close
        let m = RubricModel(rows: item.rows)
        let g = RingGeometry(model: m)
        model = m
        geo = g
        _motion = StateObject(wrappedValue: RingMotion(geometry: g))
        let kept = UserDefaults.standard.string(forKey: "rubricView") == "grid" ? "grid" : "ring"
        _view = State(initialValue: item.view == "grid" || item.view == "ring" ? (item.view ?? kept) : kept)
    }

    private var on: Bool { shown && !leaving }

    var body: some View {
        GeometryReader { box in
            let fit = RingFit(size: box.size)
            ZStack(alignment: .topLeading) {
                RubricVeils(fit: fit, lift: box.safeAreaInsets.top, light: scheme == .light)
                    .opacity(on ? 1 : 0)
                    .animation(.easeOut(duration: 0.38), value: on)
                catcher(fit)
                stage(fit)
                if view == "grid" {
                    grid(box.size)
                        .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.opacity.combined(with: .scale(scale: 0.97)))
                }
                chrome
                escapeKey
            }
            .frame(width: box.size.width, height: box.size.height, alignment: .topLeading)
        }
        .environment(\.colorScheme, .dark)
        .focusable()
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(phases: .down) { press in key(press) }
        .task {
            motion.instant = reduceMotion
            motion.start(graded: model.graded, opening: view == "ring" ? item.open : nil)
            shown = true
            try? await Task.sleep(nanoseconds: 80_000_000) // (once the window has let go of what had the keys)
            focused = true
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("\(item.title) rubric")
        .accessibilityAddTraits(.isModal)
    }

    // MARK: Parts

    /// Presses and the pointer anywhere not on the ring's own buttons: a slice opens, beside the bar rolls it back up,
    /// beside the ring closes it.
    private func catcher(_ fit: RingFit) -> some View {
        Color.clear
            .contentShape(Rectangle())
            .gesture(SpatialTapGesture().onEnded { value in tapped(value.location, fit) })
            .onContinuousHover { phase in
                switch phase {
                case .active(let p):
                    pointed(p, fit)
                case .ended:
                    if ringHit >= 0 && motion.hov == ringHit { motion.hover(-1) }
                    ringHit = -1
                }
            }
            .accessibilityHidden(true)
    }

    private func stage(_ fit: RingFit) -> some View {
        let ring = view == "ring"
        return RubricStageView(geo: geo, motion: motion, scale: fit.scale)
            .frame(width: fit.width, height: fit.height)
            .scaleEffect(leaving ? 0.92 : (ring ? 1 : 0.94))
            .opacity(ring && !leaving ? 1 : 0)
            .position(x: fit.origin.x + fit.width / 2, y: fit.origin.y + fit.height / 2)
            .allowsHitTesting(ring && !leaving)
            .accessibilityHidden(!ring)
            .animation(reduceMotion ? Animation.easeInOut(duration: 0.25) : Motion.gentle, value: ring)
            .animation(Animation.easeOut(duration: reduceMotion ? 0.2 : 0.32), value: leaving)
    }

    private func grid(_ size: CGSize) -> some View {
        RubricGridPanel(model: model, eyebrow: eyebrow, title: item.title)
            .frame(maxWidth: min(1000, max(320, size.width - 64)), maxHeight: max(240, size.height - 150))
            .scaleEffect(leaving ? 0.96 : 1)
            .opacity(leaving ? 0 : 1)
            .animation(.easeOut(duration: 0.3), value: leaving)
            .position(x: size.width / 2, y: size.height / 2 + 26)
    }

    /// The title over the ring, × at the top left, Ring / Grid at the top right, and what to do at the foot.
    private var chrome: some View {
        let ring = view == "ring"
        return ZStack(alignment: .top) {
            header
                .opacity(ring ? 1 : 0)
            HStack(alignment: .top) {
                closeButton
                Spacer(minLength: 12)
                RubricViewSwitch(view: Binding(get: { view }, set: { setView($0) }))
            }
            .padding(.horizontal, 20)
            .padding(.top, 16)
            Text(footWords)
                .font(.sCallout)
                .foregroundStyle(Color.white.opacity(0.62))
                .multilineTextAlignment(.center)
                .shadow(color: Color.black.opacity(0.5), radius: 4)
                .contentTransition(.opacity)
                .animation(.easeInOut(duration: 0.25), value: footWords)
                .padding(.horizontal, 24)
                .padding(.bottom, 16)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
                .opacity(ring ? 1 : 0)
                .allowsHitTesting(false)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .opacity(on ? 1 : 0)
        .animation(.easeOut(duration: 0.3).delay(on ? 0.1 : 0), value: on)
        .animation(Motion.gentle, value: ring)
    }

    private var header: some View {
        VStack(spacing: 4) {
            Text(eyebrow.uppercased())
                .font(.sCaption.weight(.semibold))
                .tracking(0.9)
                .foregroundStyle(Color.white.opacity(0.62))
            Text(item.title)
                .font(.sTitle2)
                .foregroundStyle(Color.white)
            Text(status)
                .font(.sCallout.weight(.medium))
                .foregroundStyle(Color.white.opacity(0.62))
        }
        .lineLimit(1)
        .multilineTextAlignment(.center)
        .shadow(color: Color.black.opacity(0.5), radius: 6)
        .padding(.horizontal, 130)
        .padding(.top, 14)
        .allowsHitTesting(false)
        .accessibilityElement(children: .combine)
    }

    private var closeButton: some View {
        Button(action: close) {
            Image(systemName: "xmark")
                .font(.system(size: 14, weight: .bold))
                .foregroundStyle(Color.white.opacity(0.9))
                .frame(width: 38, height: 38)
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .glass(Circle(), interactive: true)
        .help("Close the rubric (Esc)")
        .accessibilityLabel("Close the rubric")
    }

    /// Escape, wherever the keys are: the bar back into the ring, else the ring away.
    private var escapeKey: some View {
        Button("", action: escape)
            .keyboardShortcut(.cancelAction)
            .opacity(0)
            .frame(width: 1, height: 1)
            .accessibilityHidden(true)
    }

    // MARK: Words

    /// The rubric's own name (not just "Rubric") and the course.
    private var eyebrow: String {
        let name = item.rubricTitle.isEmpty || item.rubricTitle.lowercased() == "rubric" ? "Rubric" : item.rubricTitle
        return item.context.isEmpty ? name : "\(name) · \(item.context)"
    }

    private var status: String {
        let many = model.n == 1 ? "1 criterion" : "\(model.n) criteria"
        if model.graded { return "Graded · \(many)" }
        if item.held { return "\(many) · marks not posted yet" }
        return "\(many) · not graded yet"
    }

    private var footWords: String {
        if motion.target(.open) > 0.5 {
            return "Height on the bar is points. Switch with the dots; the little ring at the top or Esc goes back."
        }
        if model.graded { return "The ring pushes out where you scored well and pulls in where you lost points." }
        return "Each colour’s stretch is its share of the points. Pick one to open it."
    }

    // MARK: Doing

    private func setView(_ v: String) {
        guard v == "ring" || v == "grid", v != view else { return }
        keptView = v
        if v == "grid" { motion.hover(-1) }
        withAnimation(reduceMotion ? Animation.easeInOut(duration: 0.2) : Motion.gentle) { view = v }
    }

    private func escape() {
        if view == "ring" && motion.openNow > 0.02 {
            motion.toRing()
        } else {
            close()
        }
    }

    private func tapped(_ p: CGPoint, _ fit: RingFit) {
        focused = true
        guard view == "ring" else {
            close()
            return
        }
        let u = fit.units(p)
        let t = motion.openNow
        if t < 0.02, let k = geo.slice(x: u.x, y: u.y) {
            motion.select(k)
            return
        }
        if t > 0.02 {
            motion.toRing()
            return
        }
        let dx = u.x - RingUnits.cx
        let dy = u.y - RingUnits.cy
        if (dx * dx + dy * dy).squareRoot() < RingUnits.radius + 50 { return } // (a press inside the ring is not beside it)
        close()
    }

    private func pointed(_ p: CGPoint, _ fit: RingFit) {
        guard view == "ring", motion.openNow < 0.02 else { return }
        let u = fit.units(p)
        let k = geo.slice(x: u.x, y: u.y) ?? -1
        guard k != ringHit else { return }
        if k >= 0 {
            motion.hover(k)
        } else if motion.hov == ringHit {
            motion.hover(-1)
        }
        ringHit = k
    }

    private func key(_ press: KeyPress) -> KeyPress.Result {
        guard press.modifiers.isDisjoint(with: [.command, .control, .option]), view == "ring" else { return .ignored }
        switch press.key {
        case .leftArrow, .upArrow:
            motion.step(-1)
            return .handled
        case .rightArrow, .downArrow:
            motion.step(1)
            return .handled
        case .return, .space:
            guard motion.openNow < 0.5 else { return .ignored }
            motion.openCurrent()
            return .handled
        default:
            return .ignored
        }
    }
}

/// The window behind the ring, brought out of focus: a thick blur everywhere and a dim deepening towards the edges
/// — a little deeper over a light window.
private struct RubricVeils: View {
    let fit: RingFit
    /// How far the veils reach up past the content's top (under the toolbar).
    let lift: CGFloat
    let light: Bool

    var body: some View {
        GeometryReader { g in
            let c = CGPoint(x: fit.centre.x, y: fit.centre.y + lift)
            ZStack {
                // (1.2.3: the page behind is never read through the ring — a thick blur everywhere, and a dim that is
                // deep enough at the centre for the score and the levels to stand alone)
                Rectangle()
                    .fill(.ultraThickMaterial)
                RubricVeils.ellipse([Gradient.Stop(color: Color.black.opacity(light ? 0.66 : 0.58), location: 0.4),
                                     Gradient.Stop(color: Color.black.opacity(light ? 0.82 : 0.78), location: 1)],
                                    at: c, rx: fit.width * 0.66, ry: fit.height * 0.7, cover: g.size)
            }
            .frame(width: g.size.width, height: g.size.height)
            .clipped()
        }
        .ignoresSafeArea()
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }

    /// A radial gradient drawn as an ellipse `rx` × `ry` round `at`, its last colour running on to cover everything.
    static func ellipse(_ stops: [Gradient.Stop], at c: CGPoint, rx: CGFloat, ry: CGFloat, cover: CGSize) -> some View {
        let r = max(rx, 1)
        let squash = max(ry, 1) / r
        let side = 4 * max(cover.width, cover.height, 200) / min(1, squash)
        return RadialGradient(gradient: Gradient(stops: stops), center: .center, startRadius: 0, endRadius: r)
            .frame(width: side, height: side)
            .scaleEffect(x: 1, y: squash)
            .position(x: c.x, y: c.y)
    }
}

// MARK: - On the assignment's page

/// The rubric on an assignment's page (1.2.3): one row — the ring in miniature, the rubric's name and score, how many
/// criteria — that brings the ring up over the window.
struct RubricSummaryRow: View {
    let data: AssignmentData
    let open: () -> Void
    @State private var hover = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let m = RubricModel(rows: data.rubric)
        let head = RubricSummaryRow.headline(data, m)
        let sub = RubricSummaryRow.subline(data, m)
        Button(action: open) {
            HStack(spacing: 12) {
                RubricMiniRing(model: m, size: 36)
                    .rotationEffect(.degrees(hover && !reduceMotion ? 18 : 0))
                    .scaleEffect(hover && !reduceMotion ? 1.08 : 1)
                VStack(alignment: .leading, spacing: 1) {
                    Text(head)
                        .font(.sHeadline)
                        .monospacedDigit()
                        .lineLimit(1)
                    Text(sub)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                Image(systemName: "arrow.up.left.and.arrow.down.right")
                    .font(.sCallout.weight(.semibold))
                    .foregroundStyle(.secondary)
                    .padding(.leading, 8)
            }
            .padding(.leading, 10)
            .padding(.trailing, 16)
            .padding(.vertical, 9)
        }
        .buttonStyle(CardButtonStyle(radius: 26))
        .onHover { hover = $0 }
        .animation(reduceMotion ? nil : Motion.gentle, value: hover)
        .help("Open the rubric")
        .accessibilityLabel("\(head), \(sub)")
        .accessibilityHint("Opens the rubric ring over the window")
    }

    /// "Lab rubric · 8 / 10", or "Rubric · 10 pts" before it is marked.
    static func headline(_ d: AssignmentData, _ m: RubricModel) -> String {
        let t = (d.rubricTitle ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        let name = t.isEmpty ? "Rubric" : t
        let score = m.graded
            ? "\(RubricModel.num(m.earned)) / \(RubricModel.num(m.possible))"
            : "\(RubricModel.num(m.possible)) \(m.possible == 1 ? "pt" : "pts")"
        return "\(name) · \(score)"
    }

    static func subline(_ d: AssignmentData, _ m: RubricModel) -> String {
        let many = m.n == 1 ? "1 criterion" : "\(m.n) criteria"
        if m.graded {
            let pct = m.possible > 0 ? Int((m.earned / m.possible * 100).rounded()) : 0
            return "\(many) · \(pct)% · how it was marked"
        }
        if d.held == true { return "\(many) · marks not posted yet" }
        return "\(many) · how it will be marked"
    }
}

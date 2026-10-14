import AppKit
import SwiftUI

// The step-through every biology process uses (spec §3.6, §9): transcription and translation, replication, mitosis
// and meiosis, the energy pathways. The controller holds the steps, where it is and whether it is playing; the controls
// sit centred under the scene in a glass capsule — ⏮ ◀ ▶/⏸ ▶ ⏭, a dot per step, "Step 4 of 12 · Elongation" and the
// speed — with the step's explanation above them. Keys (when the scene or the controls have focus): ← →, Space, Home,
// End, and 1–9 for a tool's stage groups. Between steps a tool draws `phase(at:)` (600 ms, eased; back plays in
// reverse); under Reduce Motion it crossfades in 200 ms instead. Each change is announced to VoiceOver.

/// A process's position and playing, for a tool's drawing and its controls.
@MainActor
final class BioStepController: ObservableObject {
    @Published private(set) var steps: [BioStep]
    @Published private(set) var index = 0
    @Published private(set) var playing = false
    /// A change is being drawn: a tool's TimelineView runs only while it is (`.animation(paused: !moving)`).
    @Published private(set) var moving = false
    /// 0.5, 1 or 2.
    @Published var speed: Double = 1
    /// Set from the view's environment (BioStepControls does it): crossfade instead of moving.
    var reduceMotion = false

    private var transition: (from: Int, to: Int, start: Date, duration: Double, crossfade: Bool)?
    private var player: Task<Void, Never>?
    private var settle: Task<Void, Never>?

    init(steps: [BioStep], start: Int = 0) {
        self.steps = steps
        index = min(max(start, 0), max(steps.count - 1, 0))
    }

    var current: BioStep? { steps.indices.contains(index) ? steps[index] : nil }
    var atStart: Bool { index == 0 }
    var atEnd: Bool { index >= steps.count - 1 }

    /// Where the drawing is at a moment: at rest on the step, or part way between two (eased).
    func phase(at date: Date = Date()) -> BioStepPhase {
        guard let t = transition else { return .at(index) }
        let u = t.duration > 0 ? date.timeIntervalSince(t.start) / t.duration : 1
        if u >= 1 { return .at(t.to) }
        return BioStepPhase(from: t.from, to: t.to, t: t.crossfade ? max(u, 0) : BioStepTiming.ease(u), crossfade: t.crossfade)
    }

    /// Whether a change is still being drawn (a TimelineView can pause when it is not).
    func isMoving(at date: Date = Date()) -> Bool {
        guard let t = transition else { return false }
        return date.timeIntervalSince(t.start) < t.duration
    }

    /// The steps changed (another gene, prokaryote mode): kept where they were if asked and possible.
    func setSteps(_ new: [BioStep], keepIndex: Bool = false) {
        pause()
        steps = new
        index = keepIndex ? min(index, max(new.count - 1, 0)) : 0
        transition = nil
        moving = false
    }

    func go(to i: Int, animated: Bool = true) {
        guard steps.indices.contains(i), i != index else { return }
        let from = index
        index = i
        settle?.cancel()
        if animated {
            let d = (reduceMotion ? BioStepTiming.crossfade : BioStepTiming.move) / max(speed, 0.1)
            transition = (from, i, Date(), d, reduceMotion)
            moving = true
            // (the drawing at rest again once the move is over)
            settle = Task { [weak self] in
                try? await Task.sleep(nanoseconds: UInt64((d + 0.05) * 1_000_000_000))
                guard !Task.isCancelled else { return }
                self?.moving = false
            }
        } else {
            transition = nil
            moving = false
        }
        announce()
    }

    func next() { go(to: index + 1) }
    func back() { go(to: index - 1) }
    func first() { pause(); go(to: 0) }
    func last() { pause(); go(to: steps.count - 1) }

    /// The first step of a stage group (number keys).
    func jump(toGroup g: Int) {
        guard let i = BioStepTiming.start(ofGroup: g, in: steps) else { return }
        pause()
        go(to: i)
    }

    func toggle() {
        if playing { pause() } else { play() }
    }

    /// Play: a step every 2.4 s plus its move (at 1×), stopping at the last. From the last, it starts again at the first.
    func play() {
        guard steps.count > 1 else { return }
        if atEnd { go(to: 0, animated: false) }
        playing = true
        player?.cancel()
        player = Task { [weak self] in
            while let self, self.playing, !Task.isCancelled {
                let wait = (BioStepTiming.dwell + BioStepTiming.move) / max(self.speed, 0.1)
                try? await Task.sleep(nanoseconds: UInt64(wait * 1_000_000_000))
                guard !Task.isCancelled, self.playing else { return }
                if self.atEnd { self.pause(); return }
                self.next()
                if self.atEnd { self.pause(); return }
            }
        }
    }

    func pause() {
        playing = false
        player?.cancel()
        player = nil
    }

    /// The step's title and explanation, said by VoiceOver.
    private func announce() {
        guard let s = current, let app = NSApp else { return }
        let text = "\(BioStepTiming.caption(index, of: steps)). \(s.text)"
        let element: Any = (app.mainWindow as Any?) ?? app
        NSAccessibility.post(element: element, notification: .announcementRequested,
                             userInfo: [.announcement: text, .priority: NSAccessibilityPriorityLevel.high.rawValue])
    }
}

/// The step-through's controls (spec §9): the step's explanation, then a glass capsule with the buttons, the dots, the
/// caption and the speed. Number keys jump to `groups` (1 = its first name …) when the tool has them.
struct BioStepControls: View {
    @ObservedObject var controller: BioStepController
    /// The stage groups' names, by number (shown in the keys' tooltip).
    var groups: [Int: String] = [:]
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        VStack(spacing: 10) {
            if let s = controller.current {
                Text(s.text)
                    .font(.system(size: 15))
                    .multilineTextAlignment(.center)
                    .lineLimit(3)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: 640)
                    .id(controller.index)
                    .transition(.opacity)
                    .accessibilityHidden(true) // (announced on each change)
            }
            capsule
        }
        .animation(reduceMotion ? nil : .easeInOut(duration: 0.2), value: controller.index)
        .onAppear { controller.reduceMotion = reduceMotion }
        .onChange(of: reduceMotion) { _, r in controller.reduceMotion = r }
        .bioStepKeys(controller)
    }

    private var capsule: some View {
        HStack(spacing: 10) {
            button("backward.end.fill", "First step (Home)", disabled: controller.atStart) { controller.first() }
            button("chevron.left", "Back (←)", disabled: controller.atStart) { controller.back() }
            button(controller.playing ? "pause.fill" : "play.fill", controller.playing ? "Pause (Space)" : "Play (Space)", disabled: controller.steps.count < 2) {
                controller.toggle()
            }
            button("chevron.right", "Next (→)", disabled: controller.atEnd) { controller.next() }
            button("forward.end.fill", "Last step (End)", disabled: controller.atEnd) { controller.last() }
            Divider().frame(height: 18)
            dots
            Text(BioStepTiming.caption(controller.index, of: controller.steps))
                .font(.sCallout.weight(.medium).monospacedDigit())
                .lineLimit(1)
                .frame(minWidth: 120, alignment: .leading)
            Menu {
                ForEach(BioStepTiming.speeds, id: \.self) { s in
                    Button { controller.speed = s } label: {
                        if s == controller.speed { Label(speedText(s), systemImage: "checkmark") } else { Text(speedText(s)) }
                    }
                }
            } label: {
                Text(speedText(controller.speed)).font(.sCallout.weight(.medium).monospacedDigit())
            }
            .menuStyle(.button)
            .buttonStyle(.plain)
            .fixedSize()
            .help("Speed")
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 8)
        .glassCapsule()
        .help(groups.isEmpty ? "← → step · Space plays · Home and End" : "← → step · Space plays · " + groups.keys.sorted().map { "\($0) \(groups[$0] ?? "")" }.joined(separator: " · "))
    }

    /// A dot per step (a press goes there); at most 24 drawn, else a thin bar of progress.
    @ViewBuilder
    private var dots: some View {
        let n = controller.steps.count
        if n <= 24 {
            HStack(spacing: 5) {
                ForEach(0..<n, id: \.self) { i in
                    Button { controller.pause(); controller.go(to: i) } label: {
                        Circle()
                            .fill(i == controller.index ? Theme.accent : Color.primary.opacity(i < controller.index ? 0.45 : 0.18))
                            .frame(width: 7, height: 7)
                            .padding(2)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help(controller.steps[i].title)
                    .accessibilityLabel("Step \(i + 1): \(controller.steps[i].title)")
                }
            }
        } else {
            ProgressView(value: Double(controller.index + 1), total: Double(max(n, 1)))
                .progressViewStyle(.linear)
                .frame(width: 120)
        }
    }

    private func button(_ symbol: String, _ help: String, disabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 13, weight: .semibold))
                .frame(width: 26, height: 26)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(disabled)
        .opacity(disabled ? 0.35 : 1)
        .help(help)
        .accessibilityLabel(help)
    }

    private func speedText(_ s: Double) -> String { s == 0.5 ? "0.5×" : "\(Int(s))×" }
}

extension View {
    /// The step-through's keys on a view that can take focus (the scene, or the controls): ← → back and next, Space
    /// play and pause, Home and End, 1–9 the stage groups.
    func bioStepKeys(_ c: BioStepController) -> some View {
        focusable()
            .focusEffectDisabled()
            .onKeyPress(.leftArrow) { c.back(); return .handled }
            .onKeyPress(.rightArrow) { c.next(); return .handled }
            .onKeyPress(.space) { c.toggle(); return .handled }
            .onKeyPress(.home) { c.first(); return .handled }
            .onKeyPress(.end) { c.last(); return .handled }
            .onKeyPress(characters: .decimalDigits, phases: .down) { press in
                guard let n = Int(press.characters), n >= 1, n <= 9, BioStepTiming.start(ofGroup: n, in: c.steps) != nil else { return .ignored }
                c.jump(toGroup: n)
                return .handled
            }
    }
}

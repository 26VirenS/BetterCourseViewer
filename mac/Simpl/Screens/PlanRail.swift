import SwiftUI

/// (1.3.165) Pull to plan, as the web has it (extension/content/app/plan.js): a piece of work pulled to the right on To Do
/// or the Dashboard opens a rail of the free times between now and when it is due under it — found by the page
/// (native-app.js `planSlots`: 8 am to 11 pm, clear of the linked calendars, the classes on Canvas's calendar and the
/// other plans) — the further it is pulled the later the time, and let go on one, it is planned there. Pulled back short,
/// nothing. The same rail opens from the work's menu (Plan a Time…), where the times are pressed instead, and the
/// arrows and Return choose one; Move Plan… and Clear Plan are there once it is planned.

/// The work whose rail the menu asked for (its id): its row opens the rail in the pressing kind.
@MainActor
final class PlanAsk: ObservableObject {
    static let shared = PlanAsk()
    @Published var id: String?
}

/// The rail's state: the times found, the one chosen, how long, and how far the row has been pulled.
@MainActor
final class PlanRailModel: ObservableObject {
    enum Mode { case pull, pick }
    @Published var mode: Mode = .pick
    @Published var slots: [PlanSlot]?
    @Published var sel = -1
    @Published var mins = 60
    @Published var planned: String?
    @Published var dueWords = ""
    @Published var error: String?
    /// How far past the start of the pull the row has gone (points).
    @Published var pulled: CGFloat = 0

    static let lead: CGFloat = 26 // (pulled this far before the first time: let go short of it, nothing is planned)
    static let step: CGFloat = 26 // (pulled this much further for each time after)
    static let lengths = [30, 60, 90, 120]

    func load(_ id: String, mins: Int?, engine: Engine) async {
        var args: [String: Any] = ["id": id]
        if let mins { args["mins"] = mins }
        let r = try? await engine.call("planSlots", args, as: PlanSlots.self)
        withAnimation(Motion.snappy) {
            error = r?.error ?? (r == nil ? "The free times could not be found." : nil)
            slots = r?.slots ?? []
            self.mins = r?.mins ?? self.mins
            planned = r?.planned
            dueWords = r?.dueWords ?? ""
            if mode == .pick {
                sel = slots?.firstIndex(where: { $0.at == planned }) ?? ((slots?.isEmpty ?? true) ? -1 : 0)
            } else {
                follow()
            }
        }
    }

    /// In the pull: the time under the lens, from how far the row has gone.
    func follow() {
        guard let n = slots?.count, n > 0 else { sel = -1; return }
        let f = min(CGFloat(n - 1), max(-1, (pulled - Self.lead) / Self.step))
        sel = f < -0.5 ? -1 : Int(f.rounded())
    }

    var chosen: PlanSlot? {
        guard let s = slots, sel >= 0, sel < s.count else { return nil }
        return s[sel]
    }

    static func lengthWord(_ m: Int) -> String {
        m < 60 ? "\(m)m" : (m % 60 == 0 ? "\(m / 60)h" : "\(m / 60)½h")
    }
}

/// The rail: the work's name and how long, the times (the one chosen lit), and a line saying what letting go does.
struct PlanRailView: View {
    let id: String
    let title: String
    @ObservedObject var model: PlanRailModel
    let commit: (PlanSlot) -> Void
    let clear: () -> Void
    @EnvironmentObject private var engine: Engine
    @FocusState private var focused: Bool

    private static let chip: CGFloat = 98
    private static let gap: CGFloat = 6
    /// (1.3.167) A little more room before each new day's first time (the web's 2.99.109).
    private static let dayGap: CGFloat = 8

    /// Where the `i`th time starts along the rail: a chip and a gap each, and a day's room before each new day.
    private static func x(of i: Int, in slots: [PlanSlot]) -> CGFloat {
        guard i > 0, i < slots.count else { return CGFloat(i) * (chip + gap) }
        let newDays = (1...min(i, slots.count - 1)).filter { slots[$0].day != slots[$0 - 1].day }.count
        return CGFloat(i) * (chip + gap) + CGFloat(newDays) * dayGap
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                Image(systemName: "clock")
                    .foregroundStyle(Theme.accent)
                Text("Plan \(title)")
                    .font(.sCallout.weight(.semibold))
                    .lineLimit(1)
                Spacer(minLength: 8)
                Picker("How long", selection: Binding(get: { model.mins }, set: { m in
                    model.mins = m
                    model.slots = nil
                    Task { await model.load(id, mins: m, engine: engine) }
                })) {
                    ForEach(PlanRailModel.lengths, id: \.self) { m in Text(PlanRailModel.lengthWord(m)).tag(m) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
                .disabled(model.mode == .pull)
                if model.mode == .pick, model.planned != nil {
                    Button("Clear", role: .destructive) { clear() }
                        .controlSize(.small)
                }
            }
            rail
                .frame(height: 50)
            Text(status)
                .font(.sCaption)
                .foregroundStyle(model.sel >= 0 && model.mode == .pull ? Theme.accent : .secondary)
                .lineLimit(1)
        }
        .padding(14)
        .frame(width: 560)
        .focusable(model.mode == .pick)
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(.leftArrow) { move(-1) }
        .onKeyPress(.rightArrow) { move(1) }
        .onKeyPress(.return) {
            if let s = model.chosen { commit(s) }
            return .handled
        }
        .onAppear { if model.mode == .pick { focused = true } }
    }

    private var status: String {
        if let e = model.error { return e }
        guard let s = model.slots else { return "Finding free time…" }
        if s.isEmpty { return model.dueWords == "Past due" || model.dueWords.isEmpty ? "No free time in the week ahead." : "No free time left before it is due." }
        if model.mode == .pull {
            guard let c = model.chosen else { return "Pull further to choose a time" }
            return "Let go to plan \(c.day) \(c.time)"
        }
        return "← → to choose · Return to plan"
    }

    private func move(_ way: Int) -> KeyPress.Result {
        guard model.mode == .pick, let n = model.slots?.count, n > 0 else { return .ignored }
        withAnimation(Motion.snappy) { model.sel = min(n - 1, max(0, model.sel + way)) }
        return .handled
    }

    @ViewBuilder
    private var rail: some View {
        if let slots = model.slots {
            if model.mode == .pull {
                // the chosen time slides into the lens (the second place along: the time before it still in view)
                ZStack(alignment: .leading) {
                    HStack(spacing: Self.gap) {
                        ForEach(Array(slots.enumerated()), id: \.element.id) { i, s in
                            chipView(s, on: i == model.sel, newDay: i == 0 || slots[i - 1].day != s.day)
                                .padding(.leading, i > 0 && slots[i - 1].day != s.day ? Self.dayGap : 0)
                        }
                        dueChip
                    }
                    // (the chosen time under the lens, the second place along; short of the first, the lens empty)
                    .offset(x: (Self.chip + Self.gap) - (model.sel < 0 ? -(Self.chip + Self.gap) : Self.x(of: model.sel, in: slots)))
                    .animation(.easeOut(duration: 0.16), value: model.sel)
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .strokeBorder(model.sel >= 0 ? Theme.accent : Color.secondary.opacity(0.5), style: StrokeStyle(lineWidth: 2, dash: model.sel >= 0 ? [] : [5, 4]))
                        .frame(width: Self.chip, height: 50)
                        .offset(x: Self.chip + Self.gap)
                        .allowsHitTesting(false)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .clipped()
            } else {
                ScrollViewReader { proxy in
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: Self.gap) {
                            ForEach(Array(slots.enumerated()), id: \.element.id) { i, s in
                                Button { commit(s) } label: { chipView(s, on: i == model.sel, newDay: i == 0 || slots[i - 1].day != s.day) }
                                    .buttonStyle(.plain)
                                    .padding(.leading, i > 0 && slots[i - 1].day != s.day ? Self.dayGap : 0)
                                    .id(s.id)
                                    .help("\(s.day) \(s.time), \(PlanRailModel.lengthWord(model.mins))")
                            }
                            dueChip
                        }
                    }
                    .onChange(of: model.sel) { _, i in
                        if let s = model.slots, i >= 0, i < s.count { withAnimation(Motion.snappy) { proxy.scrollTo(s[i].id, anchor: .center) } }
                    }
                    .onAppear {
                        if let s = model.slots, model.sel >= 0, model.sel < s.count { proxy.scrollTo(s[model.sel].id, anchor: .center) }
                    }
                }
            }
        } else {
            HStack(spacing: 8) {
                ProgressView().controlSize(.small)
                Text("Finding free time…").font(.sCallout).foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    /// A time: (1.3.167) its day said once, over the first of the day's times — the rest keep the line, blank, so the
    /// times sit level.
    private func chipView(_ s: PlanSlot, on: Bool, newDay: Bool) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(newDay ? s.day.uppercased() : " ")
                .font(.system(size: 10.5, weight: .semibold))
                .foregroundStyle(on ? Color.white.opacity(0.85) : Theme.accent)
                .accessibilityHidden(!newDay)
            Text(s.time)
                .font(.system(size: 14, weight: .bold).monospacedDigit())
                .foregroundStyle(on ? Color.white : Color.primary)
        }
        .padding(.horizontal, 11)
        .frame(width: Self.chip, height: 50, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(on ? Theme.accent : Color.primary.opacity(0.07)))
        .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(s.at == model.planned && !on ? Theme.accent : .clear, lineWidth: 1.5))
        .contentShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
    }

    @ViewBuilder
    private var dueChip: some View {
        if !model.dueWords.isEmpty {
            Text(model.dueWords)
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.red)
                .padding(.horizontal, 12)
                .frame(height: 50)
                .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Color.red.opacity(0.45), lineWidth: 1.5))
                .fixedSize()
        }
    }
}

/// The row's plan: "Planned Tue 4:00 PM", a small blue capsule (grey once its time has gone by).
struct PlanPill: View {
    let plan: WorkPlan

    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: "clock")
                .font(.system(size: 10, weight: .semibold))
            Text(plan.words)
                .font(.system(size: 11, weight: .semibold))
                .lineLimit(1)
        }
        .foregroundStyle(plan.past == true ? Color.secondary : Theme.accent)
        .padding(.horizontal, 8)
        .frame(height: 20)
        .background(Capsule().fill((plan.past == true ? Color.secondary : Theme.accent).opacity(0.13)))
        .fixedSize()
        .help("\(PlanRailModel.lengthWord(plan.mins)) set aside · move or clear it from the work's menu")
    }
}

/// The pull itself, on a row of work: the row follows the pointer and then resists, the rail under it; let go on a time
/// and it is planned. A press (no pull) is the row's own: the rail asks for fourteen points sideways first.
struct PullToPlan: ViewModifier {
    let id: String
    let title: String
    let enabled: Bool
    // (the engine as a plain value, as the rows act through it: a row is not drawn again each time the engine changes)
    @Environment(\.engineLink) private var link
    private var engine: Engine { link.acting }
    @ObservedObject private var ask = PlanAsk.shared
    @StateObject private var model = PlanRailModel()
    @State private var shown = false
    @State private var dx: CGFloat = 0
    @State private var engaged = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private static let engage: CGFloat = 14

    func body(content: Content) -> some View {
        content
            .offset(x: rubber(dx))
            .background(alignment: .leading) { reveal }
            .simultaneousGesture(drag, including: enabled ? .all : .subviews)
            .popover(isPresented: $shown, arrowEdge: .bottom) {
                PlanRailView(id: id, title: title, model: model, commit: { s in plan(s) }, clear: { clearPlan() })
                    .environmentObject(engine)
            }
            .onChange(of: ask.id) { _, asked in
                guard asked == id else { return }
                ask.id = nil
                open(.pick)
            }
    }

    /// (1.3.167) What the pull does, uncovered behind the row as it moves (as a list's swipe shows its action under the
    /// row; the web's 2.99.109): a clock that grows with the pull, filled once a time is chosen.
    @ViewBuilder
    private var reveal: some View {
        let w = rubber(dx)
        if w > 0.5 {
            let k = min(1, w / 40)
            let armed = model.mode == .pull && model.chosen != nil
            RoundedRectangle(cornerRadius: 10, style: .continuous)
                .fill(armed ? Theme.accent : Theme.accent.opacity(0.12))
                .overlay {
                    Image(systemName: armed ? "clock.fill" : "clock")
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundStyle(armed ? Color.white : Theme.accent)
                        .scaleEffect(0.55 + 0.45 * k)
                        .opacity(k)
                }
                .frame(width: w)
                .animation(reduceMotion ? nil : Motion.snappy, value: armed)
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
    }

    private func rubber(_ x: CGFloat) -> CGFloat {
        let m: CGFloat = 64
        return x <= 0 ? 0 : m * x / (x + m)
    }

    private var drag: some Gesture {
        DragGesture(minimumDistance: Self.engage, coordinateSpace: .global)
            .onChanged { v in
                let w = v.translation.width
                let h = v.translation.height
                if !engaged {
                    guard w > Self.engage, w > abs(h) * 1.4 else { return }
                    engaged = true
                    open(.pull)
                }
                dx = max(0, w - Self.engage)
                model.pulled = dx
                model.follow()
            }
            .onEnded { _ in
                guard engaged else { return }
                engaged = false
                let pick = model.chosen
                withAnimation(reduceMotion ? Animation.easeOut(duration: 0.15) : Motion.snappy) { dx = 0 }
                if let pick { plan(pick) } else { shown = false }
            }
    }

    private func open(_ mode: PlanRailModel.Mode) {
        model.mode = mode
        model.slots = nil
        model.error = nil
        model.pulled = 0
        model.sel = -1
        shown = true
        Task { await model.load(id, mins: nil, engine: engine) }
    }

    private func plan(_ s: PlanSlot) {
        shown = false
        Task {
            let r = try? await engine.call("planPut", ["id": id, "at": s.at, "mins": model.mins], as: ActReply.self)
            if r?.ok == true { engine.changed() }
        }
    }

    private func clearPlan() {
        shown = false
        Task {
            _ = try? await engine.call("planClear", ["id": id], as: ActReply.self)
            engine.changed()
        }
    }
}

extension View {
    /// A row of work made pullable (content/app/plan.js on the web): `enabled` false for work done.
    func pullToPlan(id: String, title: String, enabled: Bool = true) -> some View {
        modifier(PullToPlan(id: id, title: title, enabled: enabled))
    }
}

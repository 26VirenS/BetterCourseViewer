import AppKit
import SwiftUI

/// The courses and goals the setup reads and writes (`setupInfo`), as the page answers them.
private struct SetupChoices: Decodable {
    /// A course this term: whether Simpl shows it (`on`), its nickname and the grade aimed for in it.
    struct Course: Decodable, Identifiable {
        var id: String
        var code: String
        var name: String
        var nickname: String
        var color: String
        var on: Bool
        var target: String
        /// (2.99.33) The name the sidebar will show (its nickname, else its name), and what is new about it — its real
        /// title where a nickname hides it, its term, its teacher ("Fall 2026 · Yue Lei"). Nil from an older page.
        var label: String?
        var sub: String?
    }

    var done: Bool
    var courses: [Course]
    var grades: [String]
    var goal: Double
    var tracking: Bool
}

/// The guided setup, as a sheet over the window: (onboarding-4) straight to the courses Simpl shows — the school's search
/// has said its welcome — each by one name (onboarding-1, onboarding-30); a nickname for each, if you like; the grade
/// history, the term's GPA goal and the grade aimed for in each course; and a read-back. Back and Continue sit at its
/// foot, and each step slides in from the side it is going to. Shown on the first run, every course ticked, with Set Up
/// Later (the app works as it stands until then); and again from Settings ▸ General ▸ Courses shown and goals, with
/// Cancel.
struct SetupScreen: View {
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var info = Loader<SetupChoices>()

    private enum Step: Int, CaseIterable { case courses, nicknames, grades, done }
    @State private var step: Step = .courses
    /// The way the last step went (Back slides the other way): set a moment before the step itself.
    @State private var forward = true
    /// The end, up: its seal bounces once.
    @State private var finished = false
    @State private var chosen: Set<String> = []
    @State private var nicknames: [String: String] = [:]
    @State private var targets: [String: String] = [:]
    @State private var tracking = true
    @State private var goal = 4.0
    @State private var seeded = false
    @State private var saving = false
    @State private var error: String?

    var body: some View {
        VStack(spacing: 0) {
            ZStack {
                if let d = info.data {
                    stepView(d)
                        .id(step)
                        .transition(slide)
                } else {
                    LoadState(error: info.error) { Task { await load() } }
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .clipped()
            Divider()
            footer
        }
        .frame(width: 640, height: 560)
        .interactiveDismissDisabled(saving || info.data?.done != true)
        .task { await load() }
    }

    @ViewBuilder
    private func stepView(_ d: SetupChoices) -> some View {
        switch step {
        case .courses: courses(d)
        case .nicknames: nicknamesStep(d)
        case .grades: grades(d)
        case .done: readBack(d)
        }
    }

    /// Forward, the next step comes in from the right and the last goes out to the left; Back, the other way. Under
    /// Reduce Motion they cross-fade.
    private var slide: AnyTransition {
        if reduceMotion { return .opacity }
        return .asymmetric(
            insertion: .move(edge: forward ? .trailing : .leading).combined(with: .opacity),
            removal: .move(edge: forward ? .leading : .trailing).combined(with: .opacity)
        )
    }

    private var reveal: AnyTransition {
        reduceMotion ? .opacity : .opacity.combined(with: .move(edge: .top))
    }

    // MARK: - Steps

    private func courses(_ d: SetupChoices) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            stepHeading("Which courses should Simpl show?", coursesLine)
            Form {
                if d.courses.isEmpty {
                    Section {
                        Text("No active courses right now. You can finish now and choose courses later from Settings.")
                            .foregroundStyle(.secondary)
                    }
                } else {
                    Section {
                        ForEach(d.courses) { c in courseRow(c) }
                    } header: {
                        HStack(alignment: .firstTextBaseline) {
                            Text("\(chosen.count) of \(d.courses.count) selected")
                                .contentTransition(.numericText(value: Double(chosen.count)))
                            Spacer()
                            Button(chosen.count == d.courses.count ? "Clear All" : "Select All") {
                                withAnimation(Motion.snappy) {
                                    chosen = chosen.count == d.courses.count ? [] : Set(d.courses.map(\.id))
                                }
                            }
                            .buttonStyle(.link)
                        }
                    }
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
        }
    }

    /// (onboarding-1) What a tick does — the course in the sidebar and on the Dashboard, and on Canvas one of the
    /// student's favourites (Brightspace's ticks are kept on this Mac) — and where the GPA is decided instead: a tick is
    /// never "only the courses that count towards your GPA", which would hide a lab's or a pass/fail course's work.
    private var coursesLine: String {
        let shown = "Ticked courses show in the sidebar and on your Dashboard"
        let gpa = "Pass/Fail, on the Grades step, keeps a course out of your GPA."
        return engine.onBrightspace ? "\(shown). \(gpa)" : "\(shown), and become your Canvas favourites. \(gpa)"
    }

    /// A course to tick, on one line (onboarding-30): the name the sidebar will show, and what is new about it beside it.
    private func courseRow(_ c: SetupChoices.Course) -> some View {
        Toggle(isOn: Binding(get: { chosen.contains(c.id) }, set: { pick(c.id, $0) })) {
            courseLine(c)
        }
        .toggleStyle(.checkbox)
        .padding(.vertical, 2)
    }

    /// The sidebar's name, bold, and after it in grey what that name does not say — the term and the teacher, the real
    /// title under a nickname — never the code over the same name spelled another way ("MATH-021-20" over "F26-MATH
    /// 021 20"). One line a course, so the whole term's list is in view; the pointer on it shows the line whole.
    private func courseLine(_ c: SetupChoices.Course) -> some View {
        let name = shown(c)
        let more = detail(c, under: name)
        return HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text(name)
                .fontWeight(.semibold)
                .lineLimit(1)
                .layoutPriority(1)
            Spacer(minLength: 0)
            if !more.isEmpty {
                Text(more)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .truncationMode(.tail)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .help(more.isEmpty ? name : "\(name) · \(more)")
    }

    /// A course by its own name (what the sidebar shows with no nickname), and what is new about it under it.
    private func courseNames(_ c: SetupChoices.Course) -> some View {
        let name = ownName(c)
        let more = detail(c, under: name)
        return VStack(alignment: .leading, spacing: 1) {
            Text(name)
                .fontWeight(.semibold)
                .lineLimit(1)
            if !more.isEmpty {
                Text(more)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
    }

    /// (1.3) The courses chosen, each with a field for a shorter name of the student's own: every one optional.
    private func nicknamesStep(_ d: SetupChoices) -> some View {
        let picked = d.courses.filter { chosen.contains($0.id) }
        return VStack(alignment: .leading, spacing: 0) {
            stepHeading("Nicknames", "Give a course a shorter name, if you like. Leave it empty to keep its own.")
            Form {
                Section {
                    if picked.isEmpty {
                        Text("No courses chosen: nothing to name.")
                            .foregroundStyle(.secondary)
                    }
                    ForEach(picked) { c in
                        LabeledContent {
                            TextField("Nickname", text: nickname(c), prompt: Text(ownName(c)))
                                .labelsHidden()
                                .textFieldStyle(.roundedBorder)
                                .frame(width: 220)
                        } label: {
                            courseNames(c)
                        }
                    }
                } footer: {
                    Text(engine.onBrightspace ? "A nickname shows everywhere in place of the course’s name — on this Mac only." : "A nickname shows everywhere in place of the course’s name — in Canvas too.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
        }
    }

    private func grades(_ d: SetupChoices) -> some View {
        let picked = d.courses.filter { chosen.contains($0.id) }
        return VStack(alignment: .leading, spacing: 0) {
            stepHeading("Grades", "\(engine.lmsName) keeps no history. Simpl can, on this Mac.")
            Form {
                Section {
                    // (onboarding-20) one name for it everywhere — grade history — as Settings ▸ Grades says it
                    Toggle(isOn: $tracking.animation(Motion.snappy)) {
                        Text("Keep grade history")
                        Text("One snapshot a day, on this Mac.")
                    }
                    .toggleStyle(.switch)
                    if tracking {
                        LabeledContent("Term GPA goal") {
                            HStack(spacing: 4) {
                                TextField("Term GPA goal", value: goalValue, format: .number.precision(.fractionLength(2)))
                                    .labelsHidden()
                                    .textFieldStyle(.roundedBorder)
                                    .multilineTextAlignment(.trailing)
                                    .frame(width: 64)
                                Stepper("Term GPA goal", value: goalValue, in: 0...4, step: 0.05)
                                    .labelsHidden()
                            }
                        }
                        .transition(reveal)
                    }
                }
                Section {
                    if picked.isEmpty {
                        Text("No courses chosen: nothing to aim at yet.")
                            .foregroundStyle(.secondary)
                    }
                    ForEach(picked) { c in
                        Picker(selection: target(c)) {
                            ForEach(Array(d.grades.reversed()), id: \.self) { g in
                                Text(g).tag(g)
                            }
                            Divider()
                            Text("Pass/Fail").tag("P/F")
                        } label: {
                            Text(shown(c))
                                .lineLimit(1)
                        }
                        .pickerStyle(.menu)
                    }
                } header: {
                    Text("Aiming For")
                } footer: {
                    Text("Pass/Fail keeps a course out of your GPA.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
        }
    }

    private func readBack(_ d: SetupChoices) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 6) {
                Image(systemName: "checkmark.seal.fill")
                    .font(.system(size: 40, weight: .semibold))
                    .foregroundStyle(.green)
                    .symbolEffect(.bounce, value: finished)
                    .padding(.bottom, 4)
                    .accessibilityHidden(true)
                Text("You’re Set")
                    .font(.system(size: 22, weight: .bold))
                Text("Change any of this later in Settings (⌘,) ▸ General ▸ Courses shown and goals.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 30)
            .padding(.top, 26)
            .onAppear { finished = true }
            Form {
                Section {
                    LabeledContent("Courses shown", value: "\(chosen.count) of \(d.courses.count)")
                    LabeledContent("Grade history", value: tracking ? "On · goal \(GoalFigure.text(goal))" : "Off")
                }
                if let error {
                    Section {
                        Label(error, systemImage: "exclamationmark.triangle.fill")
                            .foregroundStyle(.red)
                    }
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
        }
    }

    /// A step's title and its line under it, in grey (onboarding-1: an accent-coloured line read as a link).
    private func stepHeading(_ title: String, _ sub: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title)
                .font(.system(size: 22, weight: .bold))
            Text(sub)
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 30)
        .padding(.top, 26)
        .padding(.bottom, 2)
    }

    // MARK: - The foot: Cancel (or Set Up Later), Back, Continue

    private var footer: some View {
        HStack(spacing: 10) {
            if canCancel {
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                    .disabled(saving)
            } else if info.data?.done == false {
                // (onboarding-4) the first run's way past it: the app works as it stands meanwhile
                Button("Set Up Later") { later() }
                    .keyboardShortcut(.cancelAction)
                    .disabled(saving)
                    .help("Use Simpl as it is now, and set it up any time from Settings ▸ General ▸ Courses shown and goals.")
            }
            Spacer(minLength: 0)
            if let d = info.data {
                if step.rawValue > 0 {
                    Button("Back") { go(-1) }
                        .disabled(saving)
                }
                Button { primary(d) } label: {
                    HStack(spacing: 6) {
                        if saving { ProgressView().controlSize(.small) }
                        Text(nextWord(d))
                    }
                    .frame(minWidth: 80)
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
                .disabled(saving || (step == .courses && chosen.isEmpty && !d.courses.isEmpty))
            }
        }
        .overlay {
            if info.data != nil { progress }
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 14)
    }

    /// The steps, as a bar of as many.
    private var progress: some View {
        HStack(spacing: 6) {
            ForEach(Step.allCases, id: \.self) { s in
                Capsule()
                    .fill(s.rawValue <= step.rawValue ? Theme.accent : Color.secondary.opacity(0.25))
                    .frame(width: 26, height: 5)
            }
        }
        .animation(Motion.snappy, value: step)
        .accessibilityElement()
        .accessibilityLabel(Text("Step \(step.rawValue + 1) of \(Step.allCases.count)"))
    }

    /// Run again from Settings, or the courses could not be read: a way out. (The first run's has Set Up Later.)
    private var canCancel: Bool {
        info.data?.done == true || (info.data == nil && info.error != nil)
    }

    private func nextWord(_ d: SetupChoices) -> String {
        switch step {
        case .done: return saving ? "Saving…" : (d.done ? "Done" : "Start Using Simpl")
        default: return "Continue"
        }
    }

    // MARK: - Choosing

    private func pick(_ id: String, _ on: Bool) {
        withAnimation(Motion.snappy) {
            if on { chosen.insert(id) } else { chosen.remove(id) }
        }
    }

    private func nickname(_ c: SetupChoices.Course) -> Binding<String> {
        Binding(get: { nicknames[c.id] ?? "" }, set: { nicknames[c.id] = String($0.prefix(60)) })
    }

    private func target(_ c: SetupChoices.Course) -> Binding<String> {
        Binding(get: { targets[c.id] ?? "A+" }, set: { targets[c.id] = $0 })
    }

    /// The GPA goal, typed or stepped: kept between 0 and 4, to the hundredth (half up, as the engine keeps it).
    private var goalValue: Binding<Double> {
        Binding(get: { goal }, set: { v in
            let kept = min(max(v.isFinite ? v : 0, 0), 4)
            goal = GoalFigure.value(kept)
        })
    }

    /// The course's own name: what the sidebar shows when it has no nickname.
    private func ownName(_ c: SetupChoices.Course) -> String {
        c.name.isEmpty ? c.code : c.name
    }

    /// The name the sidebar will show once this is saved: the nickname typed here, else the course's own.
    private func shown(_ c: SetupChoices.Course) -> String {
        let n = (nicknames[c.id] ?? "").trimmingCharacters(in: .whitespaces)
        return n.isEmpty ? ownName(c) : n
    }

    /// What the page says is new about a course (`sub`: its term and teacher, its real title under a nickname), less
    /// anything that only repeats `name` — a nickname cleared here leaves the real title as the name itself.
    private func detail(_ c: SetupChoices.Course, under name: String) -> String {
        let sub = (c.sub ?? "").trimmingCharacters(in: .whitespaces)
        if sub == name { return "" }
        if sub.hasPrefix(name + " · ") { return String(sub.dropFirst(name.count + 3)) }
        return sub
    }

    // MARK: - Moving and saving

    private func primary(_ d: SetupChoices) {
        if step == .done {
            Task { await save(d) }
        } else {
            go(1)
        }
    }

    private func go(_ by: Int) {
        guard let next = Step(rawValue: step.rawValue + by) else { return }
        let ahead = by > 0
        Task {
            if forward != ahead {
                forward = ahead
                try? await Task.sleep(nanoseconds: 20_000_000) // (the leaving step takes the new way before it leaves)
            }
            withAnimation(Motion.gentle) { step = next }
        }
    }

    private func load() async {
        await info.load(engine, "setupInfo")
        guard let d = info.data, !seeded else { return }
        seeded = true
        chosen = Set(d.courses.filter(\.on).map(\.id))
        nicknames = Dictionary(d.courses.map { ($0.id, $0.nickname) }, uniquingKeysWith: { first, _ in first })
        targets = Dictionary(d.courses.map { ($0.id, $0.target) }, uniquingKeysWith: { first, _ in first })
        tracking = d.tracking
        goal = d.goal
    }

    /// (onboarding-4) Set Up Later: nothing saved — the courses stay as the school's site has them, the Dashboard working
    /// with them as it does — and the setup not put up again at launch; Settings ▸ General ▸ Courses shown and goals (or
    /// Courses ▸ Choose Courses) runs it whenever the student likes.
    private func later() {
        SetupLater.postpone()
        dismiss()
        Task { await NotificationAsk.afterSetup(engine) } // (asked once, as after a setup finished)
    }

    private func save(_ d: SetupChoices) async {
        guard !saving else { return }
        saving = true
        withAnimation(errorMotion) { error = nil }
        let picked = d.courses.filter { chosen.contains($0.id) }
        let nicks = Dictionary(picked.map { ($0.id, nicknames[$0.id] ?? "") }, uniquingKeysWith: { first, _ in first })
        let aims = Dictionary(picked.map { ($0.id, targets[$0.id] ?? "A+") }, uniquingKeysWith: { first, _ in first })
        let args: [String: Any] = ["courses": Array(chosen), "nicknames": nicks, "targets": aims, "tracking": tracking, "goal": goal]
        do {
            _ = try await engine.call("setupSave", args, as: OK.self)
            engine.changed()
            Task { await engine.loadSidebar() } // (the sidebar's courses: the ones just chosen, by their new names)
            dismiss()
            Task { await NotificationAsk.afterSetup(engine) } // (1.2.1: notifications asked for, once the setup is done)
        } catch {
            withAnimation(errorMotion) { self.error = error.localizedDescription }
        }
        saving = false
    }

    /// (1.3.76) Why it could not be saved comes and goes as a row of the read-back does (its own insert and removal),
    /// on the snappy spring; under Reduce Motion a short ease.
    private var errorMotion: Animation { reduceMotion ? .easeInOut(duration: 0.15) : Motion.snappy }
}

/// (onboarding-4) The first run's setup put off with Set Up Later: it is not put up again at launch (Engine.swift), and
/// the tour may be offered meanwhile (Tour.swift). Kept on this Mac; a setup finished later makes it moot.
enum SetupLater {
    private static let key = "setup:later"

    static var asked: Bool { UserDefaults.standard.bool(forKey: key) }

    static func postpone() { UserDefaults.standard.set(true, forKey: key) }
}

/// (1.3.78) The goal as every app and the engine round it: half up to the hundredth, on its decimal value — 3.425 is
/// 3.43, where `(v * 100).rounded() / 100` and "%.2f" read the double's 3.42499… as 3.42.
private enum GoalFigure {
    static func value(_ v: Double) -> Double {
        guard v.isFinite, var d = Decimal(string: "\(v)", locale: Locale(identifier: "en_US_POSIX")) else { return v }
        var out = Decimal()
        NSDecimalRound(&out, &d, 2, .plain)
        return NSDecimalNumber(decimal: out).doubleValue
    }

    /// Always two decimals: "3.43", "4.00".
    static func text(_ v: Double) -> String { String(format: "%.2f", value(v)) }
}

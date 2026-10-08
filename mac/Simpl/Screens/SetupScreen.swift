import AppKit
import SwiftUI

/// The courses and goals the setup reads and writes (`setupInfo`), as the page answers them.
private struct SetupChoices: Decodable {
    /// A course this term: whether it counts (`on`), its nickname and the grade aimed for in it.
    struct Course: Decodable, Identifiable {
        var id: String
        var code: String
        var name: String
        var nickname: String
        var color: String
        var on: Bool
        var target: String
    }

    var done: Bool
    var courses: [Course]
    var grades: [String]
    var goal: Double
    var tracking: Bool
}

/// The guided setup, as a sheet over the window: a welcome; the courses that count (each with a nickname if you like);
/// the grade history, its GPA goal and the grade aimed for in each course; and a read-back. Back and Continue sit at its
/// foot, and each step slides in from the side it is going to. Shown on the first run, and again from Settings ▸ General
/// ▸ Courses and Goals (straight to the courses, with Cancel).
struct SetupScreen: View {
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var info = Loader<SetupChoices>()

    private enum Step: Int, CaseIterable { case welcome, courses, grades, done }
    @State private var step: Step = .welcome
    /// The way the last step went (Back slides the other way): set a moment before the step itself.
    @State private var forward = true
    /// The welcome and the end, up: their pieces arrive one after another.
    @State private var welcomed = false
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
        case .welcome: welcome
        case .courses: courses(d)
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

    private var welcome: some View {
        VStack(spacing: 22) {
            Spacer(minLength: 0)
            Image(nsImage: NSApp.applicationIconImage)
                .resizable()
                .frame(width: 88, height: 88)
                .accessibilityHidden(true)
                .modifier(SetupArrive(index: 0, shown: welcomed))
            VStack(spacing: 6) {
                Text("Welcome to Simpl")
                    .font(.system(size: 28, weight: .bold))
                Text("\(engine.lmsName), simply, on your Mac. Two quick questions and you’re in.")
                    .font(.title3)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
            }
            .modifier(SetupArrive(index: 1, shown: welcomed))
            VStack(alignment: .leading, spacing: 16) {
                feature("sun.max.fill", .orange, "Dashboard", "What is due, what is new, and what was graded — at a glance.")
                    .modifier(SetupArrive(index: 2, shown: welcomed))
                feature("chart.bar.fill", .green, "Grades", "Rings for every course, your GPA, and what-if scores.")
                    .modifier(SetupArrive(index: 3, shown: welcomed))
                feature("checklist", .blue, "Quizzes and hand-ins", "Take quizzes and hand in work without leaving the app.")
                    .modifier(SetupArrive(index: 4, shown: welcomed))
            }
            .frame(maxWidth: 420, alignment: .leading)
            .padding(.top, 6)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 48)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear { welcomed = true } // (the one hello: its pieces arrive one after another)
    }

    private func feature(_ symbol: String, _ tint: Color, _ title: String, _ text: String) -> some View {
        HStack(alignment: .top, spacing: 14) {
            Image(systemName: symbol)
                .font(.title2)
                .foregroundStyle(tint)
                .frame(width: 32)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.headline)
                Text(text)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func courses(_ d: SetupChoices) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            stepHeading("Which classes are you in?", "Only select the courses that count towards your GPA.", accent: true)
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
                    } footer: {
                        Text(engine.onBrightspace ? "A nickname shows everywhere in place of the course’s code — on this Mac only." : "A nickname shows everywhere in place of the course’s code — in Canvas too.")
                            .font(.callout)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
        }
    }

    /// A course to tick, with its colour, its code and its name; ticked, a nickname for it.
    private func courseRow(_ c: SetupChoices.Course) -> some View {
        let on = chosen.contains(c.id)
        return VStack(alignment: .leading, spacing: 8) {
            Toggle(isOn: Binding(get: { chosen.contains(c.id) }, set: { pick(c.id, $0) })) {
                HStack(spacing: 10) {
                    RoundedRectangle(cornerRadius: 3.5, style: .continuous)
                        .fill(Color(hex: c.color))
                        .frame(width: 12, height: 12)
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(c.code)
                            .fontWeight(.semibold)
                            .lineLimit(1)
                        if !c.name.isEmpty && c.name != c.code {
                            Text(c.name)
                                .font(.callout)
                                .foregroundStyle(.secondary)
                                .lineLimit(2)
                        }
                    }
                }
            }
            .toggleStyle(.checkbox)
            if on {
                TextField("Nickname", text: nickname(c), prompt: Text("Nickname (optional)"))
                    .labelsHidden()
                    .textFieldStyle(.roundedBorder)
                    .frame(maxWidth: 320)
                    .padding(.leading, 22)
                    .transition(reveal)
            }
        }
        .padding(.vertical, 2)
    }

    private func grades(_ d: SetupChoices) -> some View {
        let picked = d.courses.filter { chosen.contains($0.id) }
        return VStack(alignment: .leading, spacing: 0) {
            stepHeading("Grades", "\(engine.lmsName) keeps no history. Simpl can, on this Mac.")
            Form {
                Section {
                    Toggle(isOn: $tracking.animation(Motion.snappy)) {
                        Text("Keep a History")
                        Text("One snapshot a day, on this Mac only.")
                    }
                    .toggleStyle(.switch)
                    if tracking {
                        LabeledContent("GPA Goal") {
                            HStack(spacing: 4) {
                                TextField("GPA Goal", value: goalValue, format: .number.precision(.fractionLength(2)))
                                    .labelsHidden()
                                    .textFieldStyle(.roundedBorder)
                                    .multilineTextAlignment(.trailing)
                                    .frame(width: 64)
                                Stepper("GPA Goal", value: goalValue, in: 0...4, step: 0.05)
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
                            HStack(spacing: 8) {
                                RoundedRectangle(cornerRadius: 3.5, style: .continuous)
                                    .fill(Color(hex: c.color))
                                    .frame(width: 12, height: 12)
                                    .accessibilityHidden(true)
                                Text(label(c))
                                    .lineLimit(1)
                            }
                        }
                        .pickerStyle(.menu)
                    }
                } header: {
                    Text("Aiming For")
                } footer: {
                    Text("Pass/Fail keeps a course out of your GPA.")
                        .font(.callout)
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
                Text("Change any of this later in Settings (⌘,) ▸ General ▸ Courses and Goals.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 30)
            .padding(.top, 26)
            .onAppear { finished = true }
            Form {
                Section {
                    LabeledContent("Courses shown", value: "\(chosen.count) of \(d.courses.count)")
                    LabeledContent("Grade history", value: tracking ? "On · goal \(String(format: "%.2f", goal))" : "Off")
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

    private func stepHeading(_ title: String, _ sub: String, accent: Bool = false) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title)
                .font(.system(size: 22, weight: .bold))
            Text(sub)
                .font(accent ? Font.callout.weight(.semibold) : Font.callout)
                .foregroundStyle(accent ? Color.accentColor : Color.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 30)
        .padding(.top, 26)
        .padding(.bottom, 2)
    }

    // MARK: - The foot: Cancel, Back, Continue

    private var footer: some View {
        HStack(spacing: 10) {
            if canCancel {
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                    .disabled(saving)
            }
            Spacer(minLength: 0)
            if let d = info.data {
                if step.rawValue > (d.done ? 1 : 0) {
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
            if step != .welcome && info.data != nil { progress }
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 14)
    }

    /// Three steps after the welcome, as a bar of three.
    private var progress: some View {
        HStack(spacing: 6) {
            ForEach(1..<Step.allCases.count, id: \.self) { i in
                Capsule()
                    .fill(i <= step.rawValue ? Color.accentColor : Color.secondary.opacity(0.25))
                    .frame(width: 26, height: 5)
            }
        }
        .animation(Motion.snappy, value: step)
        .accessibilityElement()
        .accessibilityLabel(Text("Step \(step.rawValue) of \(Step.allCases.count - 1)"))
    }

    /// Run again from Settings, or the courses could not be read: a way out. (The first run's setup is finished, not
    /// cancelled.)
    private var canCancel: Bool {
        info.data?.done == true || (info.data == nil && info.error != nil)
    }

    private func nextWord(_ d: SetupChoices) -> String {
        switch step {
        case .welcome: return "Get Started"
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

    /// The GPA goal, typed or stepped: kept between 0 and 4, to the hundredth.
    private var goalValue: Binding<Double> {
        Binding(get: { goal }, set: { v in
            let kept = min(max(v.isFinite ? v : 0, 0), 4)
            goal = (kept * 100).rounded() / 100
        })
    }

    private func label(_ c: SetupChoices.Course) -> String {
        let n = (nicknames[c.id] ?? "").trimmingCharacters(in: .whitespaces)
        return n.isEmpty ? c.code : n
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
        if d.done { step = .courses } // (run again from Settings: straight to the courses)
    }

    private func save(_ d: SetupChoices) async {
        guard !saving else { return }
        saving = true
        error = nil
        let picked = d.courses.filter { chosen.contains($0.id) }
        let nicks = Dictionary(picked.map { ($0.id, nicknames[$0.id] ?? "") }, uniquingKeysWith: { first, _ in first })
        let aims = Dictionary(picked.map { ($0.id, targets[$0.id] ?? "A+") }, uniquingKeysWith: { first, _ in first })
        let args: [String: Any] = ["courses": Array(chosen), "nicknames": nicks, "targets": aims, "tracking": tracking, "goal": goal]
        do {
            _ = try await engine.call("setupSave", args, as: OK.self)
            engine.changed()
            Task { await engine.loadSidebar() } // (the sidebar's courses: the ones just chosen, by their new names)
            dismiss()
        } catch {
            self.error = error.localizedDescription
        }
        saving = false
    }
}

/// A rare moment's pieces arriving one after another (the setup's welcome): each rises a little and fades in, a beat
/// after the one before, on the house's gentle spring. Under Reduce Motion a plain fade, all at once.
private struct SetupArrive: ViewModifier {
    let index: Int
    let shown: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        content
            .opacity(shown ? 1 : 0)
            .offset(y: shown || reduceMotion ? 0 : 8)
            .animation(reduceMotion ? Motion.gentle : Motion.gentle.delay(0.06 * Double(index)), value: shown)
    }
}

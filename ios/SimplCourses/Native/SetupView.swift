import SwiftUI

/// The courses and goals the setup reads and writes (`setupInfo`).
struct SetupInfo: Decodable {
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

/// The guided setup, the iPhone's own: a welcome, the courses that count (with a nickname each), the grade
/// history and goal, a target per course, and a read-back. No look, no colours, no layouts — the app draws
/// those itself. Shown on the first run, and again from Settings → Courses and Goals.
struct SetupScreen: View {
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @StateObject private var info = Loader<SetupInfo>()

    private enum Step: Int, CaseIterable { case welcome, courses, grades, done }
    @State private var step: Step = .welcome
    /// The way the last step went (Back slides the other way): set a frame before the step itself.
    @State private var forward = true
    /// The welcome and the end, up: their pieces arrive one after another (first run only).
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
        NavigationStack {
            Group {
                if let d = info.data {
                    page(d)
                } else {
                    LoadState(error: info.error) { Task { await load() } }
                }
            }
            .navigationTitle(step == .welcome ? "" : "Set Up")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                if info.data?.done == true {
                    ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() }.disabled(saving) }
                }
                if step != .welcome {
                    ToolbarItem(placement: .principal) { progress }
                }
            }
        }
        .interactiveDismissDisabled(info.data?.done != true)
        .task { await load() }
    }

    private func load() async {
        await info.load(engine, "setupInfo")
        guard let d = info.data, !seeded else { return }
        seeded = true
        chosen = Set(d.courses.filter(\.on).map(\.id))
        nicknames = Dictionary(uniqueKeysWithValues: d.courses.map { ($0.id, $0.nickname) })
        targets = Dictionary(uniqueKeysWithValues: d.courses.map { ($0.id, $0.target) })
        tracking = d.tracking
        goal = d.goal
        if d.done { step = .courses } // (run again from Settings: straight to the courses)
    }

    /// Three steps after the welcome, as a bar of three.
    private var progress: some View {
        HStack(spacing: 6) {
            ForEach(1..<Step.allCases.count, id: \.self) { i in
                Capsule()
                    .fill(i <= step.rawValue ? Color.accentColor : Color(.tertiarySystemFill))
                    .frame(width: 34, height: 5)
            }
        }
        .animation(.snappy, value: step)
        .accessibilityElement()
        .accessibilityLabel("Step \(step.rawValue) of \(Step.allCases.count - 1)")
    }

    @ViewBuilder
    private func page(_ d: SetupInfo) -> some View {
        Group {
            switch step {
            case .welcome: welcome
            case .courses: courses(d)
            case .grades: grades(d)
            case .done: done(d)
            }
        }
        .transition(.asymmetric(insertion: .move(edge: forward ? .trailing : .leading).combined(with: .opacity), removal: .move(edge: forward ? .leading : .trailing).combined(with: .opacity)))
        .safeAreaInset(edge: .bottom) { footer(d) }
    }

    // MARK: - Steps

    private var welcome: some View {
        ScrollView {
            VStack(spacing: 28) {
                Image(systemName: "graduationcap.fill")
                    .font(.system(size: 64, weight: .semibold))
                    .foregroundStyle(.tint)
                    .arrive(0, welcomed)
                    .padding(.top, 48)
                VStack(spacing: 8) {
                    Text("Welcome to Simpl Courses").font(.largeTitle.weight(.bold)).multilineTextAlignment(.center)
                    Text("\(engine.lmsName), simply, on your iPhone. Two quick questions and you’re in.")
                        .font(.body).foregroundStyle(.secondary).multilineTextAlignment(.center)
                }
                .arrive(1, welcomed)
                VStack(alignment: .leading, spacing: 20) {
                    feature("sun.max.fill", .orange, "Today", "What is due, what is new, and what was graded — at a glance.").arrive(2, welcomed)
                    feature("chart.bar.fill", .green, "Grades", "Rings for every course, your GPA, and what-if scores.").arrive(3, welcomed)
                    feature("checklist", .blue, "Quizzes and hand-ins", "Take quizzes and hand in work without leaving the app.").arrive(4, welcomed)
                }
                .padding(.top, 8)
            }
            .padding(.horizontal, 28)
        }
        .onAppear { welcomed = true } // (the first run's one hello: its pieces arrive one after another)
    }

    private func feature(_ symbol: String, _ tint: Color, _ title: String, _ text: String) -> some View {
        HStack(alignment: .top, spacing: 16) {
            Image(systemName: symbol).font(.title2).foregroundStyle(tint).frame(width: 34)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.headline)
                Text(text).font(.subheadline).foregroundStyle(.secondary)
            }
        }
    }

    private func courses(_ d: SetupInfo) -> some View {
        List {
            Section {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Which classes are you in?").font(.title2.weight(.bold))
                    Text("Only select the courses that count towards your GPA.").font(.subheadline.weight(.semibold)).foregroundStyle(.tint)
                }
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets(top: 8, leading: 4, bottom: 4, trailing: 4))
            }
            if d.courses.isEmpty {
                Section {
                    Text("No active courses right now. You can finish now and choose courses later from Settings.").foregroundStyle(.secondary)
                }
            } else {
                Section {
                    ForEach(d.courses) { c in courseRow(c) }
                } header: {
                    HStack {
                        Text("\(chosen.count) of \(d.courses.count) selected")
                        Spacer()
                        Button(chosen.count == d.courses.count ? "Clear All" : "Select All") {
                            Haptics.select()
                            chosen = chosen.count == d.courses.count ? [] : Set(d.courses.map(\.id))
                        }
                        .font(.footnote.weight(.semibold))
                        .textCase(nil)
                    }
                } footer: {
                    Text(engine.onBrightspace ? "A nickname shows everywhere in place of the course’s code — on this iPhone only." : "A nickname shows everywhere in place of the course’s code — in Canvas too.")
                }
            }
        }
        .listStyle(.insetGrouped)
    }

    private func courseRow(_ c: SetupInfo.Course) -> some View {
        let on = chosen.contains(c.id)
        return VStack(alignment: .leading, spacing: 8) {
            Button {
                Haptics.select()
                if on { chosen.remove(c.id) } else { chosen.insert(c.id) }
            } label: {
                HStack(spacing: 12) {
                    Image(systemName: on ? "checkmark.circle.fill" : "circle")
                        .font(.title2)
                        .foregroundStyle(on ? Color.accentColor : Color(.tertiaryLabel))
                        .contentTransition(.symbolEffect(.replace))
                    Circle().fill(Color(hex: c.color)).frame(width: 10, height: 10)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(c.code).font(.body.weight(.semibold)).lineLimit(1)
                        Text(c.name).font(.footnote).foregroundStyle(.secondary).lineLimit(2)
                    }
                    Spacer(minLength: 0)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityAddTraits(on ? .isSelected : [])
            if on {
                TextField("Nickname (optional)", text: Binding(get: { nicknames[c.id] ?? "" }, set: { nicknames[c.id] = String($0.prefix(60)) }))
                    .font(.subheadline)
                    .textInputAutocapitalization(.words)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 8)
                    .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color(.tertiarySystemFill)))
                    .padding(.leading, 34)
            }
        }
        .padding(.vertical, 3)
        .animation(.snappy(duration: 0.25), value: on)
    }

    private func grades(_ d: SetupInfo) -> some View {
        let picked = d.courses.filter { chosen.contains($0.id) }
        return List {
            Section {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Grades").font(.title2.weight(.bold))
                    Text("\(engine.lmsName) keeps no history. Simpl Courses can, on this iPhone.").font(.subheadline).foregroundStyle(.secondary)
                }
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets(top: 8, leading: 4, bottom: 4, trailing: 4))
            }
            Section {
                Toggle(isOn: $tracking.animation()) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Keep a History")
                        Text("One snapshot a day, on this iPhone only.").font(.footnote).foregroundStyle(.secondary)
                    }
                }
                if tracking {
                    Stepper(value: $goal.animation(.snappy), in: 0...4, step: 0.05) { // (the number rolls)
                        HStack {
                            Text("GPA Goal")
                            Spacer()
                            Text(String(format: "%.2f", goal)).font(.body.weight(.semibold).monospacedDigit()).contentTransition(.numericText(value: goal))
                        }
                    }
                }
            }
            Section {
                if picked.isEmpty {
                    Text("No courses chosen: nothing to aim at yet.").foregroundStyle(.secondary)
                }
                ForEach(picked) { c in
                    HStack(spacing: 10) {
                        Circle().fill(Color(hex: c.color)).frame(width: 10, height: 10)
                        Text(label(c)).lineLimit(1)
                        Spacer(minLength: 8)
                        Menu {
                            Picker("Aiming for", selection: Binding(get: { targets[c.id] ?? "A+" }, set: { targets[c.id] = $0 })) {
                                ForEach(d.grades.reversed(), id: \.self) { Text($0).tag($0) }
                                Divider()
                                Text("Pass/Fail").tag("P/F")
                            }
                        } label: {
                            HStack(spacing: 4) {
                                Text(targets[c.id] == "P/F" ? "Pass/Fail" : (targets[c.id] ?? "A+")).font(.body.weight(.semibold))
                                Image(systemName: "chevron.up.chevron.down").font(.caption.weight(.semibold))
                            }
                            .padding(.horizontal, 12)
                            .padding(.vertical, 6)
                            .background(Capsule().fill(Color.accentColor.opacity(0.14)))
                        }
                    }
                }
            } header: {
                Text("Aiming For")
            } footer: {
                Text("Pass/Fail keeps a course out of your GPA.")
            }
        }
        .listStyle(.insetGrouped)
    }

    private func done(_ d: SetupInfo) -> some View {
        List {
            Section {
                VStack(alignment: .leading, spacing: 6) {
                    Image(systemName: "checkmark.seal.fill")
                        .font(.system(size: 40, weight: .semibold))
                        .foregroundStyle(.green)
                        .symbolEffect(.bounce, value: finished)
                        .padding(.bottom, 4)
                    Text("You’re Set").font(.title2.weight(.bold))
                    Text("Change any of this later in Settings → Courses and Goals.").font(.subheadline).foregroundStyle(.secondary)
                }
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets(top: 8, leading: 4, bottom: 4, trailing: 4))
                .onAppear { finished = true }
            }
            Section {
                LabeledContent("Courses shown", value: "\(chosen.count) of \(d.courses.count)")
                LabeledContent("Grade history", value: tracking ? "On · goal \(String(format: "%.2f", goal))" : "Off")
            }
            if let error {
                Section { Label(error, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red) }
            }
        }
        .listStyle(.insetGrouped)
    }

    private func label(_ c: SetupInfo.Course) -> String {
        let n = (nicknames[c.id] ?? "").trimmingCharacters(in: .whitespaces)
        return n.isEmpty ? c.code : n
    }

    // MARK: - Moving and saving

    private func footer(_ d: SetupInfo) -> some View {
        HStack(spacing: 12) {
            if step.rawValue > (d.done ? 1 : 0) {
                Button {
                    go(-1)
                } label: {
                    Text("Back").font(.headline).frame(maxWidth: .infinity, minHeight: 50)
                }
                .glassButton()
                .buttonBorderShape(.capsule)
                .disabled(saving)
            }
            Button {
                if step == .done { Task { await save(d) } } else { go(1) }
            } label: {
                HStack(spacing: 8) {
                    if saving { ProgressView().tint(.white) }
                    Text(nextWord)
                }
                .font(.headline)
                .frame(maxWidth: .infinity, minHeight: 50)
            }
            .glassProminentButton()
            .buttonBorderShape(.capsule)
            .disabled(saving || (step == .courses && chosen.isEmpty && !d.courses.isEmpty))
        }
        .padding(.horizontal, 20)
        .padding(.bottom, 8)
    }

    private var nextWord: String {
        switch step {
        case .welcome: return "Get Started"
        case .done: return saving ? "Saving…" : "Start Using Simpl Courses"
        default: return "Continue"
        }
    }

    private func go(_ by: Int) {
        guard let next = Step(rawValue: step.rawValue + by) else { return }
        Haptics.tap()
        Task {
            if forward != (by > 0) {
                forward = by > 0
                try? await Task.sleep(nanoseconds: 20_000_000)
            }
            withAnimation(.spring(response: 0.42, dampingFraction: 0.88)) { step = next }
        }
    }

    private func save(_ d: SetupInfo) async {
        saving = true
        error = nil
        let nicks = Dictionary(uniqueKeysWithValues: d.courses.filter { chosen.contains($0.id) }.map { ($0.id, nicknames[$0.id] ?? "") })
        let aims = Dictionary(uniqueKeysWithValues: d.courses.filter { chosen.contains($0.id) }.map { ($0.id, targets[$0.id] ?? "A+") })
        let args: [String: Any] = ["courses": Array(chosen), "nicknames": nicks, "targets": aims, "tracking": tracking, "goal": goal]
        do {
            _ = try await engine.call("setupSave", args, as: OK.self)
            Haptics.success()
            engine.changed()
            dismiss()
        } catch {
            self.error = error.localizedDescription
            Haptics.error()
        }
        saving = false
    }
}

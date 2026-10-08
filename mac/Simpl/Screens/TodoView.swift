import SwiftUI

/// To Do: the next seven days of work and your own tasks, grouped by date, priority or course. A tick marks one done (it
/// stays a moment, then moves where it now belongs), the flag at a row's end sets its priority, a click opens the work,
/// a right-click has the rest — and + adds a task of your own.
struct TodoView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<TodoData>()
    /// A task of your own on its way out: the question first (and, when it repeats, whether the whole series goes).
    @State private var deleting: WorkRow?
    /// The grouping and Show Completed as just chosen, shown at once while the page regroups.
    @State private var pendingGroup: String?
    @State private var pendingShowDone: Bool?
    @State private var regrouping = 0
    /// A new task's sheet went up: what is read next is its answer, and arrives on the house spring.
    @State private var expectingTask = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Group {
            if let d = model.data {
                Page {
                    ScreenHeading(title: "To Do", sub: d.sub)
                    summary(d)
                    if d.sections.isEmpty {
                        EmptyNote(text: d.empty ?? "Nothing to do.")
                            .padding(.horizontal, 20)
                            .padding(.vertical, 6)
                            .card()
                            .transition(cardTransition)
                    }
                    ForEach(d.sections) { section in
                        sectionCard(section, group: d.group)
                            .transition(cardTransition)
                    }
                    Text("Click a task to open it, or right-click it for more. Priority is yours alone and never reaches \(engine.lmsName).")
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                        .padding(.horizontal, 6)
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("To Do")
        .navigationSubtitle(doneLine)
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button {
                    engine.openCalendar()
                } label: {
                    Label("Calendar", systemImage: "calendar")
                }
                .help("Calendar")
            }
            ToolbarItem(placement: .primaryAction) {
                Button {
                    engine.newTask = true
                } label: {
                    Label("New Task", systemImage: "plus")
                }
                .help("New Task")
            }
        }
        .task(id: engine.dataVersion) {
            // (a new task's answer arrives on the spring; any other reading is a redraw)
            let animated = expectingTask && model.data != nil
            expectingTask = false
            await load(animated: animated)
        }
        .onChange(of: engine.newTask) { _, open in
            if open { expectingTask = true }
        }
        .confirmationDialog(deleteQuestion, isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } }), titleVisibility: .visible) {
            if let row = deleting {
                Button("Delete Task", role: .destructive) { delete(row, series: false) }
                if let n = row.series, n > 0 {
                    Button("Delete All \(n + 1) Repeats", role: .destructive) { delete(row, series: true) }
                }
            }
            Button("Cancel", role: .cancel) { deleting = nil }
        } message: {
            Text(engine.onBrightspace ? "This removes it from your To Do." : "This removes it from your Canvas planner.")
        }
    }

    private var doneLine: String {
        guard let d = model.data else { return "" }
        return "\(d.done) of \(d.total) done"
    }

    private var deleteQuestion: String {
        guard let row = deleting else { return "Delete Task?" }
        return "Delete “\(row.title)”?"
    }

    private var cardTransition: AnyTransition {
        reduceMotion ? .opacity : .scale(scale: 0.96).combined(with: .opacity)
    }

    // MARK: - The progress and the grouping

    /// How much of the week is done, and how it is grouped: side by side on a wide window, one over the other on a narrow one.
    private func summary(_ d: TodoData) -> some View {
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .center, spacing: 16) {
                progress(d)
                Spacer(minLength: 16)
                controls(alignment: .trailing)
            }
            VStack(alignment: .leading, spacing: 14) {
                progress(d)
                controls(alignment: .leading)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    private func progress(_ d: TodoData) -> some View {
        HStack(spacing: 16) {
            Ring(value: Double(d.pct), color: .green, lineWidth: 7, key: "todo")
                .frame(width: 58, height: 58)
            VStack(alignment: .leading, spacing: 2) {
                Text("\(d.pct)%")
                    .font(.system(size: 30, weight: .bold, design: .rounded))
                    .contentTransition(.numericText(value: Double(d.pct)))
                Text("\(d.done) of \(d.total) done")
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText(value: Double(d.done)))
            }
            .monospacedDigit()
        }
        .accessibilityElement(children: .combine)
    }

    private func controls(alignment: HorizontalAlignment) -> some View {
        VStack(alignment: alignment, spacing: 10) {
            Picker("Group by", selection: Binding(get: { pendingGroup ?? model.data?.group ?? "date" }, set: { regroup(group: $0) })) {
                Text("Date").tag("date")
                Text("Priority").tag("priority")
                Text("Course").tag("course")
            }
            .pickerStyle(.segmented)
            .fixedSize()
            Toggle("Show Completed", isOn: Binding(get: { pendingShowDone ?? model.data?.showDone ?? false }, set: { regroup(showDone: $0) }))
                .toggleStyle(.checkbox)
        }
    }

    // MARK: - The sections

    private func sectionCard(_ section: TodoSection, group: String) -> some View {
        let byCourse = group == "course"
        // (a card that is not one day — another grouping, or Overdue — gives each row its day as well as its time)
        let withDay = group != "date" || section.title == "Overdue"
        return CardSection(title: section.title, trailing: section.note) {
            ForEach(Array(section.rows.enumerated()), id: \.element.id) { i, row in
                VStack(spacing: 0) {
                    if i > 0 { RowDivider(inset: 40) }
                    TodoTaskRow(
                        row: row,
                        shown: Self.shown(row, withCourse: !byCourse, withDay: withDay),
                        showCourse: !byCourse,
                        open: opener(row),
                        toggle: { toggle(row, $0) },
                        setPriority: { setPriority(row, $0) },
                        delete: { deleting = row }
                    )
                }
                .transition(.opacity)
            }
        }
    }

    /// A row as the shared work row shows it: what it is under its title (its course is its chip, or the card it is
    /// in), and its day with its time where the card is not one day.
    private static func shown(_ row: WorkRow, withCourse: Bool, withDay: Bool) -> WorkRow {
        var r = row
        if let meta = row.meta, !meta.isEmpty { r.sub = meta }
        if withCourse, let name = row.courseName, !name.isEmpty { r.course = name }
        if withDay { r.time = dayAndTime(row) }
        return r
    }

    private static let isoFractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    private static let isoPlain = ISO8601DateFormatter()

    /// "Today 11:59 PM", "Fri 11:59 PM", "Oct 20 11:59 PM".
    private static func dayAndTime(_ row: WorkRow) -> String? {
        guard let s = row.date, let date = isoFractional.date(from: s) ?? isoPlain.date(from: s) else { return row.time }
        let cal = Calendar.current
        let day: String
        if cal.isDateInToday(date) {
            day = "Today"
        } else if cal.isDateInTomorrow(date) {
            day = "Tomorrow"
        } else if cal.isDateInYesterday(date) {
            day = "Yesterday"
        } else {
            let apart = cal.dateComponents([.day], from: cal.startOfDay(for: Date()), to: cal.startOfDay(for: date)).day ?? 0
            day = abs(apart) < 7 ? date.formatted(.dateTime.weekday(.abbreviated)) : date.formatted(.dateTime.month(.abbreviated).day())
        }
        guard let time = row.time, !time.isEmpty else { return day }
        return "\(day) \(time)"
    }

    private func opener(_ row: WorkRow) -> (() -> Void)? {
        guard row.custom != true, let url = row.url, !url.isEmpty else { return nil }
        return { engine.openWeb(url, title: row.title) }
    }

    // MARK: - Reading and acting

    private func load(animated: Bool = false, group: String? = nil, showDone: Bool? = nil) async {
        var args: [String: Any] = [:]
        if let group { args["group"] = group }
        if let showDone { args["showDone"] = showDone }
        await model.load(engine, "todo", args, animated: animated)
    }

    private func regroup(group: String? = nil, showDone: Bool? = nil) {
        if let group { pendingGroup = group }
        if let showDone { pendingShowDone = showDone }
        regrouping += 1
        let ticket = regrouping
        Task {
            await load(animated: true, group: group, showDone: showDone)
            guard ticket == regrouping else { return } // (a later choice is on its way: it clears these)
            pendingGroup = nil
            pendingShowDone = nil
        }
    }

    private func toggle(_ row: WorkRow, _ done: Bool) {
        mutate(row.id) { $0.done = done }
        Task {
            if await engine.act("complete", ["id": row.id, "done": done]) {
                try? await Task.sleep(nanoseconds: 450_000_000) // (the tick shows before the row moves)
                await load(animated: true)
                engine.changed()
            } else {
                mutate(row.id) { $0.done = !done }
            }
        }
    }

    private func setPriority(_ row: WorkRow, _ level: Int) {
        mutate(row.id) {
            $0.pri = level
            $0.priShort = TodoPriority.of(level).short
        }
        Task {
            await engine.act("setPriority", ["id": row.id, "level": level])
            await load(animated: true)
        }
    }

    private func delete(_ row: WorkRow, series: Bool) {
        deleting = nil
        Task {
            if await engine.act("deleteTask", ["id": row.id, "series": series]) {
                await load(animated: true)
                engine.changed()
            }
        }
    }

    /// A row changed before the page has said so (a tick, a priority), and the count of what is done with it.
    private func mutate(_ id: String, _ change: (inout WorkRow) -> Void) {
        guard var d = model.data else { return }
        for s in d.sections.indices {
            guard let i = d.sections[s].rows.firstIndex(where: { $0.id == id }) else { continue }
            let was = d.sections[s].rows[i].done
            change(&d.sections[s].rows[i])
            let now = d.sections[s].rows[i].done
            if was != now {
                d.done = max(0, min(d.total, d.done + (now ? 1 : -1)))
                d.pct = d.total > 0 ? Int((Double(d.done) / Double(d.total) * 100).rounded()) : 0
            }
            break
        }
        withAnimation(Motion.snappy) { model.data = d }
    }
}

// MARK: - A row

/// A row of To Do: the shared work row (its tick, its title and what it is, where it stands, its course, its time) with
/// its priority's flag at its end. A click opens the work; a right-click opens it, hands it in or shows its feedback,
/// marks it done, sets its priority, deletes a task of your own, or opens it in Canvas.
private struct TodoTaskRow: View {
    let row: WorkRow
    let shown: WorkRow
    let showCourse: Bool
    let open: (() -> Void)?
    let toggle: (Bool) -> Void
    let setPriority: (Int) -> Void
    let delete: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var hover = false

    var body: some View {
        HStack(spacing: 2) {
            if let open {
                RowLink(action: open) { line }
            } else {
                line
                    .padding(.horizontal, 8)
                    .padding(.vertical, 7)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            TodoPriorityMenu(level: row.pri ?? 0, short: row.priShort, hovering: hover, set: setPriority)
        }
        .onHover { hover = $0 }
        .contextMenu { menu }
    }

    private var line: some View {
        WorkRowView(row: shown, showCourse: showCourse, toggle: toggle)
    }

    /// The work's own menu (as `.workMenu` has it) with the task's: done, priority, delete.
    @ViewBuilder
    private var menu: some View {
        let w = WorkAction(row)
        if let url = w.url, !url.isEmpty {
            Button("Open") { engine.openWeb(url, title: w.title) }
            if w.handedIn {
                Button("See Feedback") { engine.act(on: url, title: w.title, feedback: true) }
            } else if w.open {
                Button(w.label.0) { engine.act(on: url, title: w.title, feedback: false) }
            }
            Divider()
        }
        Button(row.done ? "Mark Not Done" : "Mark Done") { toggle(!row.done) }
        Menu("Priority") { todoPriorityChoices(level: row.pri ?? 0, set: setPriority) }
        if row.custom == true {
            Divider()
            Button("Delete Task…", role: .destructive) { delete() }
        }
        if let url = w.url, !url.isEmpty {
            Divider()
            Button("Open in \(engine.lmsName)") { engine.openWebScreen(url, title: w.title) }
            Button("Copy Link") { if let u = engine.absolute(url) { copyToPasteboard(u.absoluteString) } }
        }
    }
}

/// A row's priority as a flag at its end, a menu of the four levels under it: the level's colour and word when it has
/// one, a faint flag under the pointer when not.
private struct TodoPriorityMenu: View {
    let level: Int
    /// The page's own short word for the level ("High", "Med", "Low").
    let short: String?
    let hovering: Bool
    let set: (Int) -> Void

    var body: some View {
        let p = TodoPriority.of(level)
        let word = (short ?? "").isEmpty ? p.short : (short ?? p.short)
        let spoken: String = level > 0 ? "Priority: \(p.label)" : "Priority: none"
        Menu {
            todoPriorityChoices(level: level, set: set)
        } label: {
            Group {
                if level > 0 {
                    Label(word, systemImage: "flag.fill")
                        .labelStyle(.titleAndIcon)
                        .foregroundStyle(p.color)
                        .background(p.color.opacity(0.14), in: Capsule())
                } else {
                    Image(systemName: "flag")
                        .foregroundStyle(.secondary)
                        .opacity(hovering ? 1 : 0)
                }
            }
            .font(.sCaption.weight(.semibold))
            .padding(.horizontal, 7)
            .padding(.vertical, 2.5)
            .contentShape(Capsule())
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .fixedSize()
        .frame(width: 62, alignment: .trailing)
        .animation(Motion.hover, value: hovering)
        .help("Priority")
        .accessibilityLabel(spoken)
    }
}

/// The four levels as checked menu items (a priority is yours alone; it never reaches Canvas).
@MainActor @ViewBuilder
private func todoPriorityChoices(level: Int, set: @escaping (Int) -> Void) -> some View {
    ForEach(TodoPriority.all) { p in
        Toggle(p.label, isOn: Binding(get: { level == p.id }, set: { if $0 { set(p.id) } }))
    }
}

/// A level of priority: its word, its short word on a row, its colour.
private struct TodoPriority: Identifiable {
    let id: Int
    let label: String
    let short: String
    let color: Color

    static let all = [
        TodoPriority(id: 3, label: "High", short: "High", color: .red),
        TodoPriority(id: 2, label: "Medium", short: "Med", color: .orange),
        TodoPriority(id: 1, label: "Low", short: "Low", color: .teal),
        TodoPriority(id: 0, label: "None", short: "", color: .gray),
    ]

    static func of(_ level: Int) -> TodoPriority {
        all.first { $0.id == level } ?? all[all.count - 1]
    }
}

// MARK: - A new task

/// A task of your own (a Canvas planner note): what it is, its day, a course and a repeat if wanted, and its priority.
/// File → New Task and To Do's + put it over the window; Return adds it, Escape puts it away.
struct NewTaskSheet: View {
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var title = ""
    @State private var date = Date()
    @State private var course = ""
    @State private var repeatKey = ""
    @State private var until = Calendar.current.date(byAdding: .day, value: 56, to: Date()) ?? Date()
    @State private var priority = 2
    @State private var courses: [NamedColor] = []
    @State private var repeats: [RepeatChoice] = []
    @State private var busy = false
    @State private var failure: String?
    @FocusState private var focused: Bool

    private var trimmed: String { title.trimmingCharacters(in: .whitespacesAndNewlines) }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                IconTile(symbol: "checkmark.circle", color: .purple, size: 28)
                Text("New Task").font(.sTitle3.weight(.semibold))
                Spacer()
            }
            .padding(.horizontal, 20)
            .padding(.top, 18)
            .padding(.bottom, 2)
            form
            Divider()
            HStack(spacing: 10) {
                if busy { ProgressView().controlSize(.small) }
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Add", action: add)
                    .keyboardShortcut(.defaultAction)
                    .disabled(trimmed.isEmpty || busy)
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 14)
        }
        .frame(minWidth: 460, idealWidth: 500, minHeight: 430, idealHeight: 500)
        .task { await loadChoices() }
        .onAppear { DispatchQueue.main.async { focused = true } }
        .onChange(of: date) { _, d in
            // (a repeat ends after it starts)
            if until < d { until = Calendar.current.date(byAdding: .day, value: 56, to: d) ?? d }
        }
    }

    private var form: some View {
        Form {
            Section {
                TextField("Task", text: $title, prompt: Text("What do you need to do?"))
                    .labelsHidden()
                    .focused($focused)
                    .onSubmit(add)
                DatePicker("Day", selection: $date, displayedComponents: .date)
            } footer: {
                Text("It’s due by the end of that day.")
            }
            Section {
                Picker("Course", selection: $course) {
                    Text("No course").tag("")
                    ForEach(courses) { c in Text(c.name).tag(c.id) }
                }
                .pickerStyle(.menu)
                Picker("Repeat", selection: $repeatKey.animation(Motion.snappy)) { // (the Until row slides in and out)
                    Text("Doesn’t repeat").tag("")
                    ForEach(repeats) { r in Text(r.label).tag(r.key) }
                }
                .pickerStyle(.menu)
                if !repeatKey.isEmpty {
                    DatePicker("Until", selection: $until, in: date..., displayedComponents: .date)
                }
            }
            Section {
                Picker("Priority", selection: $priority) {
                    Text("High").tag(3)
                    Text("Medium").tag(2)
                    Text("Low").tag(1)
                    Text("None").tag(0)
                }
                .pickerStyle(.segmented)
            } footer: {
                Text("Priority is yours alone and never reaches \(engine.lmsName).")
            }
            if let failure {
                Section {
                    Label(failure, systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(.red)
                }
            }
        }
        .formStyle(.grouped)
    }

    /// The courses and the repeats to choose from, as To Do has them.
    private func loadChoices() async {
        guard let d = try? await engine.call("todo", as: TodoData.self) else { return }
        courses = d.courses ?? []
        repeats = d.repeats ?? []
    }

    private func add() {
        let t = trimmed
        guard !t.isEmpty, !busy else { return }
        busy = true
        failure = nil
        let iso = ISO8601DateFormatter()
        var args: [String: Any] = ["title": t, "date": iso.string(from: date), "priority": priority]
        if !course.isEmpty { args["courseId"] = course }
        if !repeatKey.isEmpty {
            args["repeat"] = repeatKey
            args["until"] = iso.string(from: until)
        }
        Task {
            do {
                _ = try await engine.call("addTask", args, as: OK.self)
                dismiss()
                engine.changed()
            } catch {
                failure = error.localizedDescription
                busy = false
            }
        }
    }
}

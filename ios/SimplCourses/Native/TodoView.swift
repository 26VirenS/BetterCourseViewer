import SwiftUI

/// To Do: the next seven days, grouped by date, priority or course; a tick to mark one done, a swipe for
/// Done or its priority, a long press for the rest, and + to add a task of your own.
struct TodoView: View {
    @EnvironmentObject private var engine: Engine
    @State private var data: TodoData?
    @State private var error: String?
    @State private var adding = false
    @State private var deleting: WorkRow?

    struct Pri: Identifiable { let id: Int; let label: String; let color: Color }
    private static let priorities: [Pri] = [Pri(id: 3, label: "High", color: .red), Pri(id: 2, label: "Medium", color: .orange), Pri(id: 1, label: "Low", color: .teal), Pri(id: 0, label: "None", color: .gray)]
    @State private var prioritizing: WorkRow?

    var body: some View {
        Group {
            if let d = data {
                List {
                    Section {
                        VStack(alignment: .leading, spacing: 8) {
                            HStack(alignment: .firstTextBaseline) {
                                Text("\(d.pct)%").font(.system(size: 34, weight: .bold, design: .rounded)).contentTransition(.numericText(value: Double(d.pct)))
                                Text("\(d.done) of \(d.total) done").foregroundStyle(.secondary)
                                Spacer()
                            }
                            ProgressView(value: Double(d.done), total: Double(max(d.total, 1))).tint(.green)
                            if let sub = d.sub { Text(sub).font(.footnote).foregroundStyle(.secondary) }
                        }
                        .padding(.vertical, 4)
                    }
                    if d.sections.isEmpty {
                        Section { Text(d.empty ?? "Nothing to do.").foregroundStyle(.secondary) }
                    }
                    ForEach(d.sections) { section in
                        Section {
                            ForEach(section.rows) { row in
                                todoRow(row, withCourse: d.group != "course")
                            }
                        } header: {
                            HStack {
                                Text(section.title)
                                Spacer()
                                if let note = section.note { Text(note).textCase(nil) }
                            }
                        }
                    }
                    Section {
                        Text("Tap a task to open it. Swipe for quick actions. Priority is yours alone and never reaches Canvas.")
                            .font(.footnote).foregroundStyle(.secondary)
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await load(force: true) }
            } else {
                LoadState(error: error) { Task { await load() } }
            }
        }
        .navigationTitle("To Do")
        .toolbar {
            ToolbarItem(placement: .topBarLeading) {
                Menu {
                    Picker("Group by", selection: Binding(get: { data?.group ?? "date" }, set: { g in regroup(group: g) })) {
                        Label("Date", systemImage: "calendar").tag("date")
                        Label("Priority", systemImage: "flag").tag("priority")
                        Label("Course", systemImage: "books.vertical").tag("course")
                    }
                    Toggle(isOn: Binding(get: { data?.showDone ?? false }, set: { on in regroup(showDone: on) })) {
                        Label("Show Completed", systemImage: "checkmark.circle")
                    }
                } label: {
                    Image(systemName: "line.3.horizontal.decrease")
                }
                .accessibilityLabel("Group and filter")
            }
        }
        .shellToolbar(calendar: true, add: {
            Haptics.tap()
            adding = true
        })
        .task(id: engine.dataVersion) { await load() }
        .sheet(isPresented: $adding) {
            AddTaskSheet(courses: data?.courses ?? [], repeats: data?.repeats ?? []) {
                Task { await load(force: false) }
            }
            .environmentObject(engine)
        }
        .confirmationDialog(deleting.map { "Delete “\($0.title)”?" } ?? "", isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } }), titleVisibility: .visible) {
            if let row = deleting {
                Button("Delete Task", role: .destructive) { delete(row, series: false) }
                if (row.series ?? 0) > 0 {
                    Button("Delete All \((row.series ?? 0) + 1) Repeats", role: .destructive) { delete(row, series: true) }
                }
            }
        } message: {
            Text("This removes it from your Canvas planner.")
        }
        .confirmationDialog("Priority", isPresented: Binding(get: { prioritizing != nil }, set: { if !$0 { prioritizing = nil } }), titleVisibility: .visible) {
            if let row = prioritizing { priorityButtons(row) }
        } message: {
            Text("Yours alone; it never reaches Canvas.")
        }
    }

    @ViewBuilder
    private func todoRow(_ row: WorkRow, withCourse: Bool) -> some View {
        HStack(spacing: 12) {
            CheckCircle(done: row.done, color: .green) { toggle(row, !row.done) }
            VStack(alignment: .leading, spacing: 2) {
                Text(row.title)
                    .lineLimit(2)
                    .strikethrough(row.done, color: .secondary)
                    .foregroundStyle(row.done ? .secondary : .primary)
                Text(withCourse ? (row.sub ?? "") : (row.meta ?? ""))
                    .font(.footnote).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer(minLength: 6)
            VStack(alignment: .trailing, spacing: 4) {
                HStack(spacing: 5) {
                    if let flag = row.flag { FlagBadge(flag: flag) }
                    if let p = row.pri, p > 0 { priorityChip(p) }
                }
                if let t = row.time { Text(t).font(.caption.monospacedDigit()).foregroundStyle(.secondary) }
            }
        }
        .padding(.vertical, 2)
        .contentShape(Rectangle())
        .onTapGesture { open(row) }
        .swipeActions(edge: .trailing, allowsFullSwipe: true) {
            Button { toggle(row, !row.done) } label: { Label(row.done ? "Not Done" : "Done", systemImage: row.done ? "arrow.uturn.backward" : "checkmark") }
                .tint(row.done ? .gray : .green)
        }
        .swipeActions(edge: .leading) {
            Button { prioritizing = row } label: { Label("Priority", systemImage: "flag.fill") }
                .tint(.orange)
        }
        .contextMenu {
            Menu { priorityButtons(row) } label: { Label("Priority", systemImage: "flag") }
            Button { toggle(row, !row.done) } label: { Label(row.done ? "Mark Not Done" : "Mark Done", systemImage: "checkmark.circle") }
            if row.custom != true, row.url != nil {
                Button { open(row) } label: { Label(row.type == "quiz" ? "Take Quiz" : "Open", systemImage: "arrow.up.right") }
            }
            if row.custom == true {
                Button(role: .destructive) { deleting = row } label: { Label("Delete Task", systemImage: "trash") }
            }
        }
    }

    @ViewBuilder
    private func priorityButtons(_ row: WorkRow) -> some View {
        ForEach(Self.priorities) { p in
            Button {
                setPriority(row, p.id)
            } label: {
                if (row.pri ?? 0) == p.id { Label(p.label, systemImage: "checkmark") } else { Text(p.label) }
            }
        }
    }

    private func priorityChip(_ p: Int) -> some View {
        let m = Self.priorities.first { $0.id == p } ?? Pri(id: 0, label: "", color: .gray)
        return Label(p == 2 ? "Med" : m.label, systemImage: "flag.fill")
            .font(.caption2.weight(.semibold))
            .labelStyle(.titleAndIcon)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .foregroundStyle(m.color)
            .background(m.color.opacity(0.14), in: Capsule())
    }

    private func load(force: Bool = false, group: String? = nil, showDone: Bool? = nil) async {
        if force { _ = try? await engine.call("refresh", as: OK.self) }
        var args: [String: Any] = [:]
        if let g = group { args["group"] = g }
        if let s = showDone { args["showDone"] = s }
        do {
            let d = try await engine.call("todo", args, as: TodoData.self)
            withAnimation(.snappy) { data = d }
            error = nil
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }

    private func regroup(group: String? = nil, showDone: Bool? = nil) {
        Haptics.select()
        Task { await load(group: group, showDone: showDone) }
    }

    private func toggle(_ row: WorkRow, _ done: Bool) {
        done ? Haptics.success() : Haptics.tap()
        mutate(row.id) { $0.done = done }
        Task {
            if await engine.act("complete", ["id": row.id, "done": done]) {
                try? await Task.sleep(nanoseconds: 450_000_000) // (the tick shows before the row moves)
                await load()
                engine.changed()
            } else {
                mutate(row.id) { $0.done = !done }
            }
        }
    }

    private func setPriority(_ row: WorkRow, _ level: Int) {
        Haptics.select()
        mutate(row.id) { $0.pri = level }
        Task {
            await engine.act("setPriority", ["id": row.id, "level": level])
            await load()
        }
    }

    private func delete(_ row: WorkRow, series: Bool) {
        deleting = nil
        Haptics.play("warning")
        Task {
            if await engine.act("deleteTask", ["id": row.id, "series": series]) {
                await load()
                engine.changed()
            }
        }
    }

    private func mutate(_ id: String, _ change: (inout WorkRow) -> Void) {
        guard var d = data else { return }
        for s in d.sections.indices {
            if let i = d.sections[s].rows.firstIndex(where: { $0.id == id }) { change(&d.sections[s].rows[i]) }
        }
        withAnimation(.snappy) { data = d }
    }

    private func open(_ row: WorkRow) {
        guard row.custom != true, let url = row.url else { return }
        Haptics.tap()
        engine.openWeb(url, title: row.title)
    }
}

/// A task of your own (a Canvas planner note): its name, its day, a course and a repeat if wanted, its priority.
struct AddTaskSheet: View {
    let courses: [NamedColor]
    let repeats: [RepeatChoice]
    let added: () -> Void
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var title = ""
    @State private var date = Date()
    @State private var course = ""
    @State private var repeatKey = ""
    @State private var until = Calendar.current.date(byAdding: .day, value: 56, to: Date()) ?? Date()
    @State private var priority = 2
    @State private var busy = false
    @State private var failure: String?
    @FocusState private var focused: Bool

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("What do you need to do?", text: $title)
                        .focused($focused)
                        .submitLabel(.done)
                        .onSubmit(add)
                    DatePicker("Day", selection: $date, displayedComponents: .date)
                }
                Section {
                    Picker("Course", selection: $course) {
                        Text("No course").tag("")
                        ForEach(courses) { c in Text(c.name).tag(c.id) }
                    }
                    Picker("Repeat", selection: $repeatKey) {
                        Text("Doesn’t repeat").tag("")
                        ForEach(repeats) { r in Text(r.label).tag(r.key) }
                    }
                    if !repeatKey.isEmpty {
                        DatePicker("Until", selection: $until, in: date..., displayedComponents: .date)
                    }
                }
                Section("Priority") {
                    Picker("Priority", selection: $priority) {
                        Text("High").tag(3)
                        Text("Medium").tag(2)
                        Text("Low").tag(1)
                        Text("None").tag(0)
                    }
                    .pickerStyle(.segmented)
                    .onChange(of: priority) { Haptics.select() }
                }
                if let failure = failure {
                    Section { Text(failure).foregroundStyle(.red) }
                }
            }
            .navigationTitle("New Task")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    if busy { ProgressView() } else {
                        Button("Add", action: add).disabled(title.trimmingCharacters(in: .whitespaces).isEmpty)
                    }
                }
            }
            .onAppear { focused = true }
        }
        .presentationDetents([.medium, .large])
    }

    private func add() {
        let t = title.trimmingCharacters(in: .whitespaces)
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
                Haptics.success()
                added()
                engine.changed()
                dismiss()
            } catch {
                Haptics.error()
                failure = error.localizedDescription
                busy = false
            }
        }
    }
}

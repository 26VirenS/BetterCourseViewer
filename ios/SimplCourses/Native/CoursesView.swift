import SwiftUI

/// Courses: the selected courses, each with its colour, its score, its unread announcements and how much
/// is handed in; a swipe (or a long press) to give one a nickname.
struct CoursesView: View {
    @EnvironmentObject private var engine: Engine
    @State private var data: CoursesData?
    @State private var progress: [String: CourseProgress] = [:]
    @State private var error: String?
    @State private var naming: CourseRow?
    @State private var nickname = ""
    /// (iPhone 1.9) The course picker is up (CoursePickerSheet).
    @State private var choosing = false
    /// (iPhone 1.8) An iPhone Duo unfolded, or an iPad: the courses as cards, as many across as fit.
    @Environment(\.wideScreen) private var wide
    @Environment(\.splitList) private var splitList
    /// (iPhone 1.8.1) The cards' own width: 750 points or more (the Duo on its side), a column each side of the fold.
    @State private var room: CGFloat = 0

    var body: some View {
        Group {
            if let d = data, wide && !splitList {
                grid(d)
            } else if let d = data {
                List {
                    Section {
                        if d.rows.isEmpty { Text(d.empty ?? "No courses selected.").foregroundStyle(.secondary) }
                        ForEach(d.rows) { c in
                            Button { open(c) } label: { row(c) }
                                .buttonStyle(.plain)
                                .swipeActions {
                                    Button { startNaming(c) } label: { Label("Nickname", systemImage: "pencil") }.tint(.indigo)
                                }
                                .contextMenu { menu(c) }
                        }
                    } header: {
                        if let sub = d.sub { Text(sub).textCase(nil) }
                    } footer: {
                        if let hidden = d.hidden, !hidden.isEmpty { Text(hidden) }
                    }
                    // (1.9) which courses these are, changed here
                    Section {
                        Button { choose() } label: { Label("Choose Courses…", systemImage: "checklist") }
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await load(force: true) }
            } else {
                LoadState(error: error) { Task { await load() } }
            }
        }
        .navigationTitle("Courses")
        // (1.9) Choose Courses, first in the bar's capsule
        .shellToolbar(menu: AnyView(
            Button { choose() } label: { Image(systemName: "checklist") }
                .accessibilityLabel("Choose Courses")
        ))
        .task(id: engine.dataVersion) { await load() }
        .sheet(isPresented: $choosing) {
            CoursePickerSheet()
                .environmentObject(engine)
        }
        .alert("Nickname", isPresented: Binding(get: { naming != nil }, set: { if !$0 { naming = nil } })) {
            TextField(naming?.original ?? "Nickname", text: $nickname)
                .textInputAutocapitalization(.words)
            Button("Save") { saveNickname(nickname) }
            if naming?.nickname?.isEmpty == false {
                Button("Remove Nickname", role: .destructive) { saveNickname("") }
            }
            Button("Cancel", role: .cancel) { naming = nil }
        } message: {
            Text("Shown instead of “\(naming?.original ?? "")” everywhere, \(engine.onBrightspace ? "on this iPhone only" : "in Canvas too").")
        }
    }

    private func choose() {
        Haptics.tap()
        choosing = true
    }

    /// A course's menu (a long press): open it, or one of its sections, or give it a nickname.
    @ViewBuilder
    private func menu(_ c: CourseRow) -> some View {
        Button { open(c) } label: { Label("Open", systemImage: "arrow.up.right") }
        Button { engine.openWeb("\(c.url)/grades", title: "Grades") } label: { Label("Grades", systemImage: "chart.bar") }
        Button { engine.openWeb("\(c.url)/assignments", title: "Assignments") } label: { Label("Assignments", systemImage: "doc.text") }
        Button { engine.openWeb("\(c.url)/modules", title: "Modules") } label: { Label("Modules", systemImage: "square.stack.3d.up") }
        Button { engine.openWeb("\(c.url)/files", title: "Files") } label: { Label("Files", systemImage: "folder") }
        Divider()
        Button { startNaming(c) } label: { Label("Nickname…", systemImage: "pencil") }
    }

    /// (iPhone 1.8) The courses on a wide screen: a card each, as many across as fit (two on an iPhone Duo unfolded,
    /// three on its side or on an iPad), on the grouped background the list has; a long press for the same menu.
    private func grid(_ d: CoursesData) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                if let sub = d.sub {
                    Text(sub).font(.footnote).foregroundStyle(.secondary).padding(.horizontal, 8)
                }
                if d.rows.isEmpty {
                    Text(d.empty ?? "No courses selected.").foregroundStyle(.secondary).padding(.horizontal, 8)
                }
                if room >= 750 {
                    // (1.8.1) a column each side of the fold, the cards across both in order — never one on the crease
                    FoldSplit {
                        VStack(spacing: 14) { ForEach(Array(d.rows.enumerated()).filter { $0.offset % 2 == 0 }.map(\.element)) { c in card(c) } }
                            .padding(.trailing, foldGutter)
                    } right: {
                        VStack(spacing: 14) { ForEach(Array(d.rows.enumerated()).filter { $0.offset % 2 == 1 }.map(\.element)) { c in card(c) } }
                            .padding(.leading, foldGutter)
                    }
                } else {
                    LazyVGrid(columns: wideColumns(minimum: 260), spacing: 14) {
                        ForEach(d.rows) { c in card(c) }
                    }
                }
                if let hidden = d.hidden, !hidden.isEmpty {
                    Text(hidden).font(.footnote).foregroundStyle(.secondary).padding(.horizontal, 8)
                }
                Button { choose() } label: { Label("Choose Courses…", systemImage: "checklist") } // (1.9)
                    .padding(.horizontal, 8)
                    .padding(.top, 4)
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 12)
        }
        .background(Color(.systemGroupedBackground))
        .refreshable { await load(force: true) }
        .onGeometryChange(for: CGFloat.self) { $0.size.width } action: { room = $0 }
    }

    private func card(_ c: CourseRow) -> some View {
        Button { open(c) } label: {
            row(c)
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 22, style: .continuous))
                .contentShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
        }
        .buttonStyle(.plain)
        .contextMenu { menu(c) }
    }

    private func row(_ c: CourseRow) -> some View {
        HStack(spacing: 14) {
            RoundedRectangle(cornerRadius: 11, style: .continuous)
                .fill(Color(hex: c.color).opacity(0.18))
                .overlay(Circle().fill(Color(hex: c.color)).frame(width: 12, height: 12))
                .frame(width: 42, height: 42)
            VStack(alignment: .leading, spacing: 3) {
                Text(c.code).font(.headline).lineLimit(1)
                Text(subline(c)).font(.subheadline).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer(minLength: 6)
            if let n = c.unread, n > 0 {
                Text("\(n)")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 7)
                    .padding(.vertical, 2)
                    .background(Color.red, in: Capsule())
                    .accessibilityLabel("\(n) unread announcements")
            }
            Text(c.scoreText)
                .font(.body.weight(.semibold).monospacedDigit())
                .foregroundStyle(c.score == nil ? .secondary : .primary)
            Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
    }

    private func subline(_ c: CourseRow) -> String {
        let name = c.name ?? ""
        if let p = progress[c.id], p.total > 0 { return "\(name) · \(p.done) of \(p.total) submitted" }
        return name
    }

    private func open(_ c: CourseRow) {
        Haptics.tap()
        engine.openWeb(c.url, title: c.code)
    }

    private func startNaming(_ c: CourseRow) {
        Haptics.tap()
        nickname = c.nickname ?? ""
        naming = c
    }

    private func saveNickname(_ name: String) {
        guard let c = naming else { return }
        naming = nil
        Task {
            if await engine.act("setNickname", ["id": c.id, "name": name]) {
                Haptics.success()
                await load(force: true)
            }
        }
    }

    private func load(force: Bool = false) async {
        if force { _ = try? await engine.call("refresh", as: OK.self) }
        // (1.9) what it showed last time, at once
        if data == nil { data = engine.saved("courses", as: CoursesData.self) }
        if progress.isEmpty, let p = engine.saved("coursesProgress", as: [String: CourseProgress].self) { progress = p }
        do {
            data = try await engine.fetch("courses", as: CoursesData.self)
            error = nil
            if let p = try? await engine.fetch("coursesProgress", as: [String: CourseProgress].self) {
                withAnimation { progress = p }
            }
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }
}

// MARK: - Choosing the courses (iPhone 1.9)

/// Every current course, ticked where it is selected (`courseChoices`).
struct CourseChoices: Codable {
    struct Course: Codable, Identifiable {
        var id: String
        var label: String
        var sub: String
        var color: String
        var on: Bool
    }

    var courses: [Course]
}

/// (iPhone 1.9) The selected courses, chosen on their own — from Settings, and from the Courses tab — rather than through
/// the whole setup: every current course with a tick, Select All, and Done. What is ticked is what Simpl shows (Today,
/// Courses, Grades, the GPA); saved as the student's Canvas favourites, exactly (`chooseCourses`), so Canvas's own
/// dashboard shows the same.
struct CoursePickerSheet: View {
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @StateObject private var model = Loader<CourseChoices>()
    @State private var chosen: Set<String> = []
    /// A tick changed by hand: a fresher list landing does not undo it.
    @State private var touched = false
    @State private var saving = false
    @State private var error: String?

    /// What is selected now, as the list says.
    private var initial: Set<String> { Set(model.data?.courses.filter(\.on).map(\.id) ?? []) }
    private var changed: Bool { model.data != nil && chosen != initial }

    var body: some View {
        NavigationStack {
            Group {
                if let d = model.data {
                    list(d)
                } else {
                    LoadState(error: model.error) { Task { await model.load(engine, "courseChoices") } }
                }
            }
            .navigationTitle("Choose Courses")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }.disabled(saving)
                }
                ToolbarItem(placement: .confirmationAction) {
                    if saving {
                        ProgressView()
                    } else {
                        Button("Done") { Task { await save() } }
                            .fontWeight(.semibold)
                            .disabled(chosen.isEmpty || !changed)
                    }
                }
            }
        }
        .interactiveDismissDisabled(changed || saving) // (ticks changed are not swiped away by accident: Cancel says so)
        .task { await model.load(engine, "courseChoices") }
        // the list as it is (the one kept from last time first, then the fresh one): its ticks, until one is changed here
        .onChange(of: model.data?.courses.map { "\($0.id):\($0.on)" } ?? [], initial: true) {
            if !touched { chosen = initial }
        }
    }

    private func list(_ d: CourseChoices) -> some View {
        List {
            if d.courses.isEmpty {
                Section {
                    Text("No current courses right now.").foregroundStyle(.secondary)
                }
            } else {
                Section {
                    ForEach(d.courses) { c in row(c) }
                } header: {
                    HStack {
                        Text("\(chosen.count) of \(d.courses.count) selected")
                        Spacer()
                        Button(chosen.count == d.courses.count ? "Clear All" : "Select All") {
                            Haptics.select()
                            touched = true
                            chosen = chosen.count == d.courses.count ? [] : Set(d.courses.map(\.id))
                        }
                        .font(.footnote.weight(.semibold))
                        .textCase(nil)
                    }
                } footer: {
                    if let error {
                        Text(error).foregroundStyle(.red)
                    } else if chosen.isEmpty {
                        Text("Choose at least one course.")
                    } else {
                        Text(engine.onBrightspace
                            ? "Selected courses are the ones Simpl shows: on Today, Courses and Grades, and in your GPA. They’re your pinned courses, so Brightspace shows the same."
                            : "Selected courses are the ones Simpl shows: on Today, Courses and Grades, and in your GPA. They’re your \(engine.lmsName) favorites, so \(engine.lmsName)’s dashboard shows the same.")
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .disabled(saving)
    }

    private func row(_ c: CourseChoices.Course) -> some View {
        let on = chosen.contains(c.id)
        return Button {
            Haptics.select()
            touched = true
            if on { chosen.remove(c.id) } else { chosen.insert(c.id) }
        } label: {
            HStack(spacing: 12) {
                Image(systemName: on ? "checkmark.circle.fill" : "circle")
                    .font(.title2)
                    .foregroundStyle(on ? Color.accentColor : Color(.tertiaryLabel))
                    .contentTransition(.symbolEffect(.replace))
                Circle().fill(Color(hex: c.color)).frame(width: 10, height: 10)
                VStack(alignment: .leading, spacing: 1) {
                    Text(c.label).font(.body.weight(.semibold)).lineLimit(2)
                    if !c.sub.isEmpty {
                        Text(c.sub).font(.footnote).foregroundStyle(.secondary).lineLimit(2)
                    }
                }
                Spacer(minLength: 0)
            }
            .padding(.vertical, 3)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
    }

    /// Done: the ticked courses selected (and no others), every screen reading again; the list kept as it now is, so
    /// it opens on these ticks next time.
    private func save() async {
        saving = true
        error = nil
        do {
            _ = try await engine.call("chooseCourses", ["courses": Array(chosen)], as: OK.self)
            if var d = model.data {
                for i in d.courses.indices { d.courses[i].on = chosen.contains(d.courses[i].id) }
                if let data = try? JSONEncoder().encode(d) { SavedAnswers.shared.keep(data, for: SavedAnswers.key("courseChoices", [:])) }
            }
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

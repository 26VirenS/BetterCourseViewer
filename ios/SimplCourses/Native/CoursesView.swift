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

    var body: some View {
        Group {
            if let d = data {
                List {
                    Section {
                        if d.rows.isEmpty { Text(d.empty ?? "No courses selected.").foregroundStyle(.secondary) }
                        ForEach(d.rows) { c in
                            Button { open(c) } label: { row(c) }
                                .buttonStyle(.plain)
                                .swipeActions {
                                    Button { startNaming(c) } label: { Label("Nickname", systemImage: "pencil") }.tint(.indigo)
                                }
                                .contextMenu {
                                    Button { open(c) } label: { Label("Open", systemImage: "arrow.up.right") }
                                    Button { engine.openWeb("\(c.url)/grades", title: "Grades") } label: { Label("Grades", systemImage: "chart.bar") }
                                    Button { engine.openWeb("\(c.url)/assignments", title: "Assignments") } label: { Label("Assignments", systemImage: "doc.text") }
                                    Button { engine.openWeb("\(c.url)/modules", title: "Modules") } label: { Label("Modules", systemImage: "square.stack.3d.up") }
                                    Button { engine.openWeb("\(c.url)/files", title: "Files") } label: { Label("Files", systemImage: "folder") }
                                    Divider()
                                    Button { startNaming(c) } label: { Label("Nickname…", systemImage: "pencil") }
                                }
                        }
                    } header: {
                        if let sub = d.sub { Text(sub).textCase(nil) }
                    } footer: {
                        if let hidden = d.hidden, !hidden.isEmpty { Text(hidden) }
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await load(force: true) }
            } else {
                LoadState(error: error) { Task { await load() } }
            }
        }
        .navigationTitle("Courses")
        .shellToolbar()
        .task(id: engine.dataVersion) { await load() }
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
        do {
            data = try await engine.call("courses", as: CoursesData.self)
            error = nil
            if let p = try? await engine.call("coursesProgress", as: [String: CourseProgress].self) {
                withAnimation { progress = p }
            }
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }
}

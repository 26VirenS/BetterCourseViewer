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

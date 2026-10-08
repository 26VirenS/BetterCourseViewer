import SwiftUI

/// All Courses: the courses chosen in the setup as cards — each in its colour, with its code and name, its score, its
/// unread announcements and how much is handed in. A course's context menu opens its sections, gives it a nickname
/// (shown everywhere, Canvas too), or opens it on Canvas.
struct CoursesView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<CoursesData>()
    @State private var progress: [String: CourseProgress] = [:]
    @State private var naming: CourseRow?

    var body: some View {
        Group {
            if let d = model.data {
                Page {
                    ScreenHeading(title: "Courses", sub: d.sub)
                    if d.rows.isEmpty {
                        EmptyNote(text: d.empty ?? "No courses selected.", symbol: "books.vertical")
                    }
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 260), spacing: 16)], spacing: 16) {
                        ForEach(d.rows) { c in card(c) }
                    }
                    if let hidden = d.hidden, !hidden.isEmpty {
                        Text(hidden).font(.callout).foregroundStyle(.secondary)
                    }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("Courses")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { engine.setup = true } label: { Label("Choose Courses", systemImage: "checklist") }
                    .help("Choose the courses that count")
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .sheet(item: $naming) { c in
            NicknameSheet(course: c) { name in save(c, name) }
        }
    }

    private func card(_ c: CourseRow) -> some View {
        let color = Color(hex: c.color)
        return Button { engine.openWeb(c.url, title: c.code) } label: {
            VStack(alignment: .leading, spacing: 0) {
                ZStack(alignment: .topLeading) {
                    LinearGradient(colors: [color, color.opacity(0.78)], startPoint: .topLeading, endPoint: .bottomTrailing)
                    Text(c.code)
                        .font(.system(size: 19, weight: .bold))
                        .foregroundStyle(.white)
                        .lineLimit(2)
                        .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
                        .padding(14)
                    if let n = c.unread, n > 0 {
                        HStack {
                            Spacer()
                            Label("\(n)", systemImage: "megaphone.fill")
                                .font(.caption.weight(.bold))
                                .foregroundStyle(color)
                                .padding(.horizontal, 8)
                                .padding(.vertical, 3)
                                .background(.white, in: Capsule())
                                .help("\(n) unread \(n == 1 ? "announcement" : "announcements")")
                        }
                        .padding(12)
                    }
                }
                .frame(height: 88)
                .clipShape(UnevenRoundedRectangle(topLeadingRadius: 16, topTrailingRadius: 16, style: .continuous))
                HStack(alignment: .center, spacing: 12) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(c.name ?? c.code).font(.callout.weight(.medium)).lineLimit(1)
                        Text(subline(c)).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                    }
                    Spacer(minLength: 6)
                    ZStack {
                        Ring(value: c.score, color: color, lineWidth: 4, key: "courses:\(c.id)")
                        Text(c.scoreText)
                            .font(.system(size: 10, weight: .semibold).monospacedDigit())
                            .foregroundStyle(c.score == nil ? .secondary : .primary)
                            .minimumScaleFactor(0.6)
                            .padding(4)
                    }
                    .frame(width: 44, height: 44)
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 12)
            }
        }
        .buttonStyle(CardButtonStyle())
        .accessibilityLabel("\(c.code), \(c.scoreText)")
        .contextMenu {
            Button("Open") { engine.openWeb(c.url, title: c.code) }
            Divider()
            ForEach([("grades", "Grades"), ("assignments", "Assignments"), ("modules", "Modules"), ("files", "Files"), ("announcements", "Announcements")], id: \.0) { kind, label in
                Button(label) { engine.go(.section("courses/\(c.id)", kind)) }
            }
            Divider()
            Button("Nickname…") { naming = c }
            Button("Open in Canvas") { engine.openWebScreen(c.url, title: c.code) }
            Button("Copy Link") { if let u = engine.absolute(c.url) { copyToPasteboard(u.absoluteString) } }
        }
    }

    private func subline(_ c: CourseRow) -> String {
        if let p = progress[c.id], p.total > 0 { return "\(p.done) of \(p.total) handed in" }
        return c.nickname?.isEmpty == false ? (c.original ?? "") : ""
    }

    private func save(_ c: CourseRow, _ name: String) {
        Task {
            if await engine.act("setNickname", ["id": c.id, "name": name]) {
                _ = try? await engine.call("refresh", as: OK.self)
                await load(animated: true)
                await engine.loadSidebar()
            }
        }
    }

    private func load(animated: Bool = false) async {
        await model.load(engine, "courses", animated: animated)
        if let p = try? await engine.call("coursesProgress", as: [String: CourseProgress].self) {
            withAnimation(Motion.gentle) { progress = p }
        }
    }
}

/// A course's nickname: shown instead of its Canvas name everywhere, Canvas included; empty, the name comes back.
private struct NicknameSheet: View {
    let course: CourseRow
    let save: (String) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Nickname").font(.headline)
            Text("Shown instead of “\(course.original ?? course.code)” everywhere, in Canvas too.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            TextField(course.original ?? "Nickname", text: $name)
                .textFieldStyle(.roundedBorder)
                .onSubmit { done(name) }
            HStack {
                if course.nickname?.isEmpty == false {
                    Button("Remove Nickname", role: .destructive) { done("") }
                }
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Save") { done(name) }
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.borderedProminent)
            }
        }
        .padding(20)
        .frame(width: 400)
        .onAppear { name = course.nickname ?? "" }
    }

    private func done(_ value: String) {
        save(value.trimmingCharacters(in: .whitespacesAndNewlines))
        dismiss()
    }
}

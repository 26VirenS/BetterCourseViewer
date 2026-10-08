import SwiftUI

/// All Courses (1.2.1): every course, as the web's Courses screen has them — the ones in the sidebar (chosen in the
/// setup) first, then the rest, each as a card in its colour, with its code and name, its score, its unread
/// announcements, how much is handed in, and a row of its sections a click away; Past and Future in the toolbar, as the
/// web's filter has them. A course's context menu opens its sections, gives it a nickname (shown everywhere, Canvas
/// too), or opens it on Canvas.
struct CoursesView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<AllCoursesData>()
    @AppStorage("SimplCoursesFilter") private var filter: CourseFilter = .all
    @State private var progress: [String: CourseProgress] = [:]
    @State private var naming: CourseRow?

    var body: some View {
        Group {
            if let d = model.data {
                Page {
                    ScreenHeading(title: "Courses", sub: d.sub)
                    content(d)
                }
                .font(.sBody)
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("Courses")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Picker("Show", selection: $filter.animation(Motion.gentle)) {
                    ForEach(CourseFilter.allCases) { f in Text(f.title).tag(f) }
                }
                .pickerStyle(.segmented)
                .fixedSize()
                .help("Current courses, past ones, or ones to come")
            }
            ToolbarItem(placement: .primaryAction) {
                Button { engine.setup = true } label: { Label("Choose Courses", systemImage: "checklist") }
                    .help("Choose the courses in the sidebar")
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .sheet(item: $naming) { c in
            NicknameSheet(course: c, onBrightspace: engine.onBrightspace) { name in save(c, name) }
        }
    }

    @ViewBuilder
    private func content(_ d: AllCoursesData) -> some View {
        switch filter {
        case .all:
            let chosen = d.current.filter { $0.chosen != false }
            let others = d.current.filter { $0.chosen == false }
            if d.current.isEmpty {
                EmptyNote(text: "No current courses.", symbol: "books.vertical")
            } else if others.isEmpty || chosen.isEmpty {
                grid(d.current)
            } else {
                PageSection(title: "In the Sidebar", trailing: "\(chosen.count)") { grid(chosen) }
                PageSection(title: "Not in the Sidebar", trailing: "\(others.count)") {
                    VStack(alignment: .leading, spacing: 12) {
                        Text("Courses you left out in the setup. Choose Courses puts them in the sidebar.")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                        grid(others)
                    }
                }
            }
        case .past:
            if d.past?.isEmpty != false { EmptyNote(text: "No past courses.", symbol: "clock.arrow.circlepath") } else { grid(d.past ?? []) }
        case .future:
            if d.future?.isEmpty != false { EmptyNote(text: "No courses to come.", symbol: "calendar.badge.clock") } else { grid(d.future ?? []) }
        }
    }

    private func grid(_ rows: [CourseRow]) -> some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 290), spacing: 18)], spacing: 18) {
            ForEach(rows) { c in
                CourseCard(course: c, progress: progress[c.id]) { naming = c }
            }
        }
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
        await model.load(engine, "allCourses", animated: animated)
        if let p = try? await engine.call("coursesProgress", ["all": true], as: [String: CourseProgress].self) {
            withAnimation(Motion.gentle) { progress = p }
        }
    }
}

/// Every course (allCourses): the current ones — those in the sidebar first, saying so — and the past and future ones.
struct AllCoursesData: Decodable {
    var sub: String?
    var current: [CourseRow]
    var past: [CourseRow]?
    var future: [CourseRow]?
}

/// The Courses screen's filter, as the web's: the current courses, the past, the ones to come.
enum CourseFilter: String, CaseIterable, Identifiable {
    case all, past, future
    var id: String { rawValue }
    var title: String {
        switch self {
        case .all: return "All"
        case .past: return "Past"
        case .future: return "Future"
        }
    }
}

/// A course's card: its colour with its code and its unread announcements, its name, how much is handed in (a bar) and
/// its score (a ring) — a click opens the course — and (1.2) under a hairline its sections, each a click away. It lifts
/// a little under the pointer, as the app's other cards do.
private struct CourseCard: View {
    let course: CourseRow
    let progress: CourseProgress?
    let rename: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var hover = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private static let links: [(kind: String, label: String)] = [
        ("grades", "Grades"), ("assignments", "Assignments"), ("modules", "Modules"), ("files", "Files"), ("announcements", "Announcements"),
    ]

    private var color: Color { Color(hex: course.color) }
    private var ctx: String { "courses/\(course.id)" }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Button {
                engine.openWeb(course.url, title: course.code)
            } label: {
                VStack(alignment: .leading, spacing: 0) {
                    banner
                    facts
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("\(course.code), \(course.scoreText)")
            Spacer(minLength: 0) // (the cards of a row as tall as each other, their sections along one line)
            Divider().padding(.horizontal, 14)
            sections
        }
        .frame(maxHeight: .infinity, alignment: .top)
        .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        .card(radius: 18)
        .scaleEffect(reduceMotion ? 1 : (hover ? 1.006 : 1))
        .animation(Motion.hover, value: hover)
        .onHover { hover = $0 }
        .contextMenu { menu }
    }

    private var banner: some View {
        ZStack(alignment: .topLeading) {
            LinearGradient(colors: [color, color.opacity(0.78)], startPoint: .topLeading, endPoint: .bottomTrailing)
            Text(course.code)
                .font(.sTitle3.weight(.bold))
                .foregroundStyle(.white)
                .lineLimit(2)
                .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
                .padding(16)
                .padding(.trailing, 56)
            if let n = course.unread, n > 0 {
                HStack {
                    Spacer()
                    Label("\(n)", systemImage: "megaphone.fill")
                        .font(.sFootnote.weight(.bold))
                        .foregroundStyle(color)
                        .padding(.horizontal, 9)
                        .padding(.vertical, 4)
                        .background(.white, in: Capsule())
                        .help("\(n) unread \(n == 1 ? "announcement" : "announcements")")
                }
                .padding(13)
            }
        }
        .frame(height: 92)
    }

    private var facts: some View {
        HStack(alignment: .center, spacing: 14) {
            VStack(alignment: .leading, spacing: 5) {
                Text(course.name ?? course.code)
                    .font(.sBody.weight(.medium))
                    .lineLimit(2)
                    .multilineTextAlignment(.leading)
                if let p = progress, p.total > 0 {
                    Text("\(p.done) of \(p.total) handed in")
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                    handedIn(p)
                } else if course.nickname?.isEmpty == false, let o = course.original, !o.isEmpty {
                    Text(o)
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 6)
            ZStack {
                Ring(value: course.score, color: color, lineWidth: 5, key: "courses:\(course.id)")
                Text(course.scoreText)
                    .font(.sFootnote.weight(.semibold).monospacedDigit())
                    .foregroundStyle(course.score == nil ? .secondary : .primary)
                    .minimumScaleFactor(0.6)
                    .padding(6)
            }
            .frame(width: 56, height: 56)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 14)
    }

    /// How much is handed in, as a bar in the course's colour.
    private func handedIn(_ p: CourseProgress) -> some View {
        let share = CGFloat(min(max(Double(p.done) / Double(max(p.total, 1)), 0), 1))
        return Capsule()
            .fill(color.opacity(0.16))
            .frame(height: 5)
            .overlay(alignment: .leading) {
                GeometryReader { g in
                    Capsule()
                        .fill(color)
                        .frame(width: share > 0 ? max(5, g.size.width * share) : 0)
                }
            }
            .frame(maxWidth: 200)
            .accessibilityHidden(true)
    }

    /// (1.2) The course's sections, each a click away.
    private var sections: some View {
        HStack(spacing: 2) {
            ForEach(CourseCard.links, id: \.kind) { link in
                Button {
                    engine.go(.section(ctx, link.kind))
                } label: {
                    Image(systemName: Glyph.section(link.kind))
                        .font(.sBody.weight(.medium))
                        .foregroundStyle(color)
                        .frame(maxWidth: .infinity, minHeight: 36)
                        .contentShape(Rectangle())
                }
                .buttonStyle(RowButtonStyle(radius: 9))
                .help(link.label)
                .accessibilityLabel("\(course.code) \(link.label)")
            }
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 6)
    }

    @ViewBuilder
    private var menu: some View {
        Button("Open") { engine.openWeb(course.url, title: course.code) }
        Divider()
        ForEach(CourseCard.links, id: \.kind) { link in
            Button(link.label) { engine.go(.section(ctx, link.kind)) }
        }
        Divider()
        Button("Nickname…", action: rename)
        Button("Open in \(engine.lmsName)") { engine.openWebScreen(course.url, title: course.code) }
        Button("Copy Link") { if let u = engine.absolute(course.url) { copyToPasteboard(u.absoluteString) } }
    }
}

/// A course's nickname: shown instead of its Canvas name everywhere, Canvas included; empty, the name comes back.
private struct NicknameSheet: View {
    let course: CourseRow
    /// The school's site is Brightspace (Engine.onBrightspace), which keeps no nicknames: they stay on this Mac.
    let onBrightspace: Bool
    let save: (String) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Nickname").font(.sTitle3)
            Text("Shown instead of “\(course.original ?? course.code)” everywhere, \(onBrightspace ? "on this Mac only" : "in Canvas too").")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            TextField(course.original ?? "Nickname", text: $name)
                .textFieldStyle(.roundedBorder)
                .font(.sBody)
                .onSubmit { done(name) }
            HStack {
                if course.nickname?.isEmpty == false {
                    Button("Remove Nickname", role: .destructive) { done("") }
                        .glassButton()
                }
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                    .glassButton()
                Button("Save") { done(name) }
                    .keyboardShortcut(.defaultAction)
                    .glassButton(prominent: true)
            }
            .controlSize(.large)
        }
        .padding(22)
        .frame(width: 440)
        .onAppear { name = course.nickname ?? "" }
    }

    private func done(_ value: String) {
        save(value.trimmingCharacters(in: .whitespacesAndNewlines))
        dismiss()
    }
}

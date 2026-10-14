import AppKit
import SwiftUI

/// All Courses (1.2.1): every course, as the web's Courses screen has them — the ones in the sidebar (chosen in the
/// setup) first, then the rest, each as a card in its colour, with its name, its term and teacher (or its other name,
/// when that says something new), its score, its unread announcements, how much is handed in, and a row of its
/// sections a click away; Current, Past and Future in the toolbar, as the web's filter has them. A course's context
/// menu opens its sections, gives it a nickname (shown everywhere, Canvas too), or opens it on Canvas.
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
                    // (courses-11: the line under the heading says what the filter shows — "2 past courses" under Past,
                    // never the current term's count)
                    ScreenHeading(title: "Courses", sub: d.sub(for: filter))
                    content(d)
                }
                .font(.sBody)
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("Courses")
        .openTimed("Courses", ready: model.data != nil, live: model.data != nil && !model.kept, source: model.source)
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
                // (courses-8: a symbol of its own and its name beside it — the checklist alone read as To Do)
                Button { engine.setup = true } label: { Label("Choose Courses", systemImage: "sidebar.squares.left") }
                    .labelStyle(.titleAndIcon)
                    .help("Choose the courses in the sidebar")
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .liveChecks(model) // (1.3.80: checked every minute while it is on show; only what changed moves)
        // (1.3.79: a popup over the window that never blocks it)
        .floatingPopup("nickname", title: "Nickname", item: $naming) { c in
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
                PageSection(title: "In the Sidebar", count: chosen.count) { grid(chosen) }
                PageSection(title: "Not in the Sidebar", count: others.count) {
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
        // (1.3.78: how much is handed in, as this session last read it, with the cards — then asked again if stale)
        if progress.isEmpty, let held = engine.recent("coursesProgress", ["all": true], as: [String: CourseProgress].self) {
            progress = held.value
        }
        await model.load(engine, "allCourses", animated: animated)
        if let p = try? await engine.read("coursesProgress", ["all": true], as: [String: CourseProgress].self) {
            withAnimation(Motion.gentle) { progress = p }
        }
    }
}

/// Every course (allCourses): the current ones — those in the sidebar first, saying so — and the past and future ones.
struct AllCoursesData: Decodable {
    var sub: String?
    /// (2.99.33) The line under the heading for each filter: "Fall 2026" (the term alone when the sections count the
    /// courses, else "Fall 2026 · 8 courses"), "2 past courses", "1 course to come" / "None yet".
    var subs: Subs?
    var current: [CourseRow]
    var past: [CourseRow]?
    var future: [CourseRow]?

    struct Subs: Decodable {
        var current: String?
        var past: String?
        var future: String?
    }

    /// The line under the heading for what the filter shows.
    func sub(for filter: CourseFilter) -> String? {
        switch filter {
        case .all: return subs?.current ?? sub
        case .past: return subs?.past
        case .future: return subs?.future
        }
    }
}

/// The Courses screen's filter, as the web's: the current courses, the past, the ones to come. (`all` is the current
/// ones, its name kept for the choice saved on this Mac; courses-12: it reads "Current", as that is all it shows.)
enum CourseFilter: String, CaseIterable, Identifiable {
    case all, past, future
    var id: String { rawValue }
    var title: String {
        switch self {
        case .all: return "Current"
        case .past: return "Past"
        case .future: return "Future"
        }
    }
}

/// A course's card: its colour with its name (the sidebar's) and its unread announcements, its term and teacher — or its
/// other name, only when that says something new (courses-5) —, how much is handed in (a bar) and its score (a ring) —
/// a click opens the course — and (1.2) under a hairline its sections, each a click away with its name under it. It
/// lifts a little under the pointer, as the app's other cards do.
private struct CourseCard: View {
    let course: CourseRow
    let progress: CourseProgress?
    let rename: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var hover = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// The sections a card leads to: their names in full (its menu, the tooltips) and short enough for five side by side
    /// under their symbols on the narrowest card (courses-14).
    private static let links: [(kind: String, label: String, short: String)] = [
        ("grades", "Grades", "Grades"), ("assignments", "Assignments", "Work"), ("modules", "Modules", "Modules"),
        ("files", "Files", "Files"), ("announcements", "Announcements", "News"),
    ]

    private var color: Color { Color(hex: course.color) }
    /// (courses-13) The banner's words: white only where white stands 3:1 against the course's colour (the minimum for
    /// large text), else near-black — students pick their colours in Canvas, and many are light.
    private var inkIsWhite: Bool { CourseCard.whiteReads(on: course.color) }
    private var ink: Color { inkIsWhite ? .white : Color(red: 28 / 255, green: 28 / 255, blue: 30 / 255) }
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
            .accessibilityLabel("\(course.code), \(scoreWords)")
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
        .readsAhead { engine.readAhead(.home(ctx)) } // (1.3.78: its home, read ahead)
        .contextMenu { menu }
    }

    private var banner: some View {
        ZStack(alignment: .topLeading) {
            LinearGradient(colors: [color, color.opacity(0.78)], startPoint: .topLeading, endPoint: .bottomTrailing)
            Text(course.code)
                .font(.sTitle3.weight(.bold))
                .foregroundStyle(ink)
                .lineLimit(2)
                .shadow(color: .black.opacity(inkIsWhite ? 0.18 : 0), radius: 2, y: 1)
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
                        .help(unreadWords(n))
                        .accessibilityLabel(unreadWords(n))
                }
                .padding(13)
            }
        }
        .frame(height: 92)
    }

    private var facts: some View {
        HStack(alignment: .center, spacing: 14) {
            VStack(alignment: .leading, spacing: 5) {
                // (courses-5: the banner names the course — "MATH-021-20" under "F26-MATH 021 20" said it twice. Its
                // other name only when that says something new, a nickname's real name; else its term and teacher)
                if !secondLine.isEmpty {
                    Text(secondLine)
                        .font(.sBody.weight(.medium))
                        .lineLimit(2)
                        .multilineTextAlignment(.leading)
                }
                if let p = progress, p.total > 0 {
                    Text("\(p.done) of \(p.total) handed in")
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                    handedIn(p)
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
            .help(course.score == nil ? (course.scoreWhy ?? "") : "") // (courses-15: why there is no figure, "No grade yet")
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 14)
    }

    /// The line under the banner: the course's other name when it says something new, else its term and teacher.
    private var secondLine: String {
        if let a = course.altName, !a.isEmpty { return a }
        return course.detail ?? ""
    }

    /// The score as it is said aloud: the figure, or why there is none.
    private var scoreWords: String {
        if course.score == nil, let why = course.scoreWhy, !why.isEmpty { return why }
        return course.scoreText
    }

    /// The unread announcements in words ("2 unread announcements"), for the badge's tooltip and VoiceOver.
    private func unreadWords(_ n: Int) -> String {
        if let l = course.unreadLabel, !l.isEmpty { return l }
        return "\(n) unread \(n == 1 ? "announcement" : "announcements")"
    }

    /// Whether white words stand 3:1 against a course's colour (WCAG's minimum for large text).
    static func whiteReads(on hex: String?) -> Bool {
        let l = AppearanceStore.luminance(NSColor(Color(hex: hex)))
        return 1.05 / (l + 0.05) >= 3
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

    /// (1.2) The course's sections, each a click away — (courses-14) its name under its symbol, so none has to be learnt
    /// from a tooltip.
    private var sections: some View {
        HStack(spacing: 2) {
            ForEach(CourseCard.links, id: \.kind) { link in
                Button {
                    engine.go(.section(ctx, link.kind))
                } label: {
                    VStack(spacing: 3) {
                        Image(systemName: Glyph.section(link.kind))
                            .font(.sBody.weight(.medium))
                            .foregroundStyle(color)
                        Text(link.short)
                            .font(.sCaption2.weight(.medium))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .minimumScaleFactor(0.85)
                    }
                    .frame(maxWidth: .infinity, minHeight: 46)
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
    /// (1.3.79) Its popup over the window, which Cancel and Save close.
    @Environment(\.popup) private var dismiss
    @State private var name = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Nickname").font(.sTitle3)
                .frame(maxWidth: .infinity, alignment: .leading)
                .popupHeader() // (1.3.79: dragged by its head, the popup moves)
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

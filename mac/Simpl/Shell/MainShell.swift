import SwiftUI

/// The window once Canvas is ready: the sidebar (Simpl's own — Dashboard, To Do, Calendar, Grades, Notifications, the
/// Inbox, each course with its sections, the groups, and the account at its foot), the screen chosen there with what
/// is pushed on it, and the toolbar: Back and Forward (a browser's, across the whole window), the screen's own title and
/// buttons, and Search (whose suggestions run commands, 1.2). The tour, the first time, goes over all of it.
struct MainShell: View {
    @EnvironmentObject private var engine: Engine
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var focus = FocusTimer.shared
    @State private var columns: NavigationSplitViewVisibility = .all
    @StateObject private var palette = SearchPalette()

    var body: some View {
        NavigationSplitView(columnVisibility: $columns) {
            Sidebar()
                .tourSpot(.sidebar)
                .navigationSplitViewColumnWidth(min: 240, ideal: 270, max: 360)
        } detail: {
            DetailStack()
        }
        .navigationSplitViewStyle(.balanced)
        .overlay { TourOverlay(columns: $columns) } // (1.2) the Mac tour (Shell/Tour.swift)
        // (1.2.1) a pinned tool opened whole: over the window, where you are
        .sheet(item: $tools.popup) { k in
            ToolPopup(kind: k)
                .environmentObject(engine)
        }
        // (1.2) the field finds and does: its suggestions are the web search box's — a sum, commands, courses and their
        // sections, groups — and "/" (or ">") lists every command (Shell/SearchCommands.swift); one picked runs
        .searchable(text: $engine.query, placement: .toolbar, prompt: "Search \(engine.lmsName) or type /")
        .searchSuggestions { PaletteSuggestions(palette: palette) }
        .onSubmit(of: .search) {
            if palette.take(engine.query, engine: engine) { return }
            palette.submit(engine.query, engine: engine)
        }
        .onChange(of: engine.query) { _, q in
            if palette.take(q, engine: engine) { return }
            palette.update(q, engine: engine)
            let t = q.trimmingCharacters(in: .whitespacesAndNewlines)
            if SearchPalette.isCommand(t) { return } // (a command typed: its list is in the suggestions, nothing is searched)
            if t.count >= 2 {
                engine.search(t)
            } else if t.isEmpty, case .search = engine.nav.current.place {
                engine.goBack()
            }
        }
        .onAppear { palette.launch(engine) }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                ControlGroup {
                    Button {
                        engine.goBack()
                    } label: {
                        Label("Back", systemImage: "chevron.left")
                    }
                    .disabled(!engine.nav.canGoBack)
                    .help("Back")
                    Button {
                        engine.goForward()
                    } label: {
                        Label("Forward", systemImage: "chevron.right")
                    }
                    .disabled(!engine.nav.canGoForward)
                    .help("Forward")
                }
                .controlGroupStyle(.navigation)
            }
            // (1.2) the tools pinned to the toolbar, on every screen: a press opens the tool's quick version in a popover;
            // a running focus timer shows its minutes here (mac/Simpl/Tools/PinnedTools.swift)
            if !tools.toolbarPins(timerActive: focus.active).isEmpty {
                ToolbarItemGroup(placement: .primaryAction) {
                    ForEach(tools.toolbarPins(timerActive: focus.active)) { kind in
                        PinnedToolButton(kind: kind, engine: engine)
                    }
                }
            }
        }
    }
}

/// The screen chosen in the sidebar, with what is pushed on it (an assignment, a discussion, a page, a folder, a
/// conversation). Another place cross-fades in; a push slides as Apple's own stacks do.
struct DetailStack: View {
    @EnvironmentObject private var engine: Engine

    var body: some View {
        let place = engine.nav.current.place
        NavigationStack(path: Binding(get: { engine.nav.current.path }, set: { engine.nav.setPath($0) })) {
            PlaceScreen(place: place)
                .navigationDestination(for: Route.self) { route in
                    RouteScreen(route: route)
                        .navigationBarBackButtonHidden(true) // (the toolbar's own Back and Forward walk the history)
                }
        }
        .id(place.identity)
        .transition(.opacity)
        .background(Theme.page.ignoresSafeArea())
    }
}

/// The screen for a place.
struct PlaceScreen: View {
    let place: Place

    var body: some View {
        switch place {
        case .dashboard: DashboardView()
        case .courses: CoursesView()
        case .todo: TodoView()
        case .calendar: CalendarView()
        case .grades: GradesView()
        case .notifications: NotificationsView()
        case .inbox: InboxView()
        case .tools: ToolsView()
        case .groups: GroupsView()
        case .search(let q): SearchView(query: q)
        case .home(let ctx): ContextHome(ctx: ctx)
        case .section(let ctx, let kind): SectionScreen(ctx: ctx, kind: kind)
        }
    }
}

/// The screen for something pushed on a place.
struct RouteScreen: View {
    let route: Route

    var body: some View {
        switch route {
        case .assignment(let course, let id): AssignmentView(course: course, id: id)
        case .topic(let ctx, let id): TopicView(ctx: ctx, id: id)
        case .page(let ctx, let slug): PageView(ctx: ctx, slug: slug)
        case .folder(let ctx, let id, let name): FilesView(ctx: ctx, folder: id, name: name)
        case .conversation(let id): ConversationView(id: id)
        case .course(let id): ContextHome(ctx: "courses/\(id)")
        case .group(let id): ContextHome(ctx: "groups/\(id)")
        case .section(let ctx, let kind): SectionScreen(ctx: ctx, kind: kind)
        case .groups: GroupsView()
        case .inbox: InboxView()
        case .notifications: NotificationsView()
        case .calendar: CalendarView()
        case .web(let url, let title): WebFallback(url: url, title: title)
        }
    }
}

/// An address handed on as a web screen (none should be on a Mac): opened in Canvas's window instead.
struct WebFallback: View {
    let url: String
    let title: String
    @EnvironmentObject private var engine: Engine

    var body: some View {
        ContentUnavailableView {
            Label(title.isEmpty ? engine.lmsName : title, systemImage: "safari")
        } description: {
            Text("This opens in a window of its own.")
        } actions: {
            Button("Open") { engine.openWebScreen(url, title: title) }
        }
        .onAppear { engine.openWebScreen(url, title: title) }
    }
}

// MARK: - The sidebar

struct Sidebar: View {
    @EnvironmentObject private var engine: Engine
    @State private var open: Set<String> = []

    /// A course's sections before its home has said which it has.
    static let defaultSections: [SectionLink] = [
        SectionLink(kind: "announcements", label: "Announcements"),
        SectionLink(kind: "assignments", label: "Assignments"),
        SectionLink(kind: "discussions", label: "Discussions"),
        SectionLink(kind: "grades", label: "Grades"),
        SectionLink(kind: "modules", label: "Modules"),
        SectionLink(kind: "pages", label: "Pages"),
        SectionLink(kind: "files", label: "Files"),
        SectionLink(kind: "quizzes", label: "Quizzes"),
        SectionLink(kind: "people", label: "People"),
        SectionLink(kind: "syllabus", label: "Syllabus"),
    ]

    private var selection: Binding<Place?> {
        Binding(get: { engine.nav.current.place }, set: { p in
            // (the list says its selection again as its rows change — a course's sections arriving: the place showing,
            // said again, is not a new step, and must not take away what is pushed on it)
            guard let p, p != engine.nav.current.place else { return }
            engine.go(p)
        })
    }

    var body: some View {
        VStack(spacing: 0) {
            list
            AccountBar()
        }
    }

    private var list: some View {
        List(selection: selection) {
            Section {
                Label("Dashboard", systemImage: "square.grid.2x2")
                    .tourSpot(.dashboardRow)
                    .tag(Place.dashboard)
                Label("To Do", systemImage: "checklist").tag(Place.todo)
                Label("Calendar", systemImage: "calendar").tag(Place.calendar)
                Label("Grades", systemImage: "chart.bar.xaxis")
                    .tourSpot(.gradesRow)
                    .tag(Place.grades)
                Label("Notifications", systemImage: "bell")
                    .badge(engine.countsLive ? (engine.snapshot?.notifUnread ?? 0) : 0) // (1.2: never a count from before)
                    .tag(Place.notifications)
                if !engine.onBrightspace { // (Brightspace has no Inbox or groups here, 2.99.22)
                    Label("Inbox", systemImage: "tray")
                        .badge(engine.countsLive ? (engine.snapshot?.inboxUnread ?? 0) : 0)
                        .tag(Place.inbox)
                }
                Label("Tools", systemImage: "wrench.and.screwdriver") // (1.2)
                    .tourSpot(.toolsRow)
                    .tag(Place.tools)
            }
            Section("Courses") {
                ForEach(engine.courses) { course in
                    courseRow(course)
                    if isOpen("courses/\(course.id)") {
                        ForEach(sections("courses/\(course.id)")) { s in
                            Label(s.label, systemImage: Glyph.section(s.kind))
                                .padding(.leading, 20)
                                .tag(Place.section("courses/\(course.id)", s.kind))
                                .transition(.opacity.combined(with: .move(edge: .top)))
                        }
                    }
                }
                Label("All Courses", systemImage: "books.vertical").tag(Place.courses)
                if !engine.onBrightspace, !engine.groups.isEmpty {
                    // (1.2) the groups in this section under one row of their own, as a course's sections are under it:
                    // a press opens all of them, the chevron lists them here — never each one pinned to the sidebar
                    groupsRow
                    if open.contains("groups") {
                        ForEach(engine.groups) { g in
                            HStack(spacing: 9) {
                                RoundedRectangle(cornerRadius: 4, style: .continuous)
                                    .fill(Color(hex: g.color))
                                    .frame(width: 13, height: 13)
                                Text(g.name).lineLimit(1)
                            }
                            .padding(.leading, 20)
                            .tag(Place.home("groups/\(g.id)"))
                            .transition(.opacity.combined(with: .move(edge: .top)))
                        }
                    }
                }
            }
        }
        .listStyle(.sidebar)
        .environment(\.sidebarRowSize, .large)
        .animation(Motion.gentle, value: open)
        .animation(Motion.gentle, value: engine.courses)
    }

    private var groupsRow: some View {
        HStack(spacing: 8) {
            Label("Groups", systemImage: "person.2")
            Spacer(minLength: 4)
            Button {
                withAnimation(Motion.gentle) {
                    if open.contains("groups") { open.remove("groups") } else { open.insert("groups") }
                }
            } label: {
                Image(systemName: "chevron.right")
                    .font(.system(size: 11.5, weight: .semibold))
                    .foregroundStyle(.secondary)
                    .rotationEffect(.degrees(open.contains("groups") ? 90 : 0))
                    .frame(width: 18, height: 18)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help(open.contains("groups") ? "Hide groups" : "Show groups")
        }
        .tag(Place.groups)
    }

    private func courseRow(_ c: CourseRow) -> some View {
        let ctx = "courses/\(c.id)"
        let expanded = isOpen(ctx)
        return HStack(spacing: 8) {
            RoundedRectangle(cornerRadius: 3.5, style: .continuous)
                .fill(Color(hex: c.color))
                .frame(width: 13, height: 13)
            Text(c.code)
                .lineLimit(1)
            Spacer(minLength: 4)
            if engine.coursesLive, let n = c.unread, n > 0 {
                Text("\(n)")
                    .font(.sCaption2.weight(.semibold).monospacedDigit())
                    .foregroundStyle(.secondary)
            }
            Button {
                withAnimation(Motion.gentle) {
                    if open.contains(ctx) { open.remove(ctx) } else { open.insert(ctx) }
                }
                Task { await engine.loadSections(ctx) }
            } label: {
                Image(systemName: "chevron.right")
                    .font(.system(size: 11.5, weight: .semibold))
                    .foregroundStyle(.secondary)
                    .rotationEffect(.degrees(expanded ? 90 : 0))
                    .frame(width: 16, height: 16)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help(expanded ? "Hide sections" : "Show sections")
        }
        .tourSpot(c.id == engine.courses.first?.id ? .firstCourse : nil)
        .onHover { on in if on { MacTour.shared.did(.courseHover) } } // (the tour's "Hover over a course")
        .tag(Place.home(ctx))
        .contextMenu {
            Button("Open in \(engine.lmsName)") { if let u = engine.canvasURL(for: .home(ctx)) { engine.openWebScreen(u.absoluteString, title: c.code) } }
            Button("Copy Link") { if let u = engine.canvasURL(for: .home(ctx)) { copyToPasteboard(u.absoluteString) } }
        }
    }

    private func isOpen(_ ctx: String) -> Bool {
        open.contains(ctx) || engine.nav.current.place.ctx == ctx
    }

    private func sections(_ ctx: String) -> [SectionLink] {
        (engine.sections[ctx] ?? Sidebar.defaultSections).filter { $0.kind != "home" }
    }
}

/// Who is signed in, at the sidebar's foot, with what was under the avatar on the web: Settings, What's New, the Canvas
/// profile, and Sign Out.
struct AccountBar: View {
    @EnvironmentObject private var engine: Engine
    @EnvironmentObject private var session: AppSession
    @State private var confirmSignOut = false

    var body: some View {
        let me = engine.snapshot?.me
        HStack(spacing: 10) {
            Avatar(person: me, size: 30)
            VStack(alignment: .leading, spacing: 1) {
                Text(me?.name ?? "Account")
                    .font(.sCallout.weight(.semibold))
                    .lineLimit(1)
                if engine.countsLive {
                    Text(engine.snapshot?.site ?? engine.host)
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                } else {
                    // (1.2) the screens are showing what was kept from last time while the live answers come in
                    HStack(spacing: 5) {
                        ProgressView().controlSize(.mini)
                        Text("Updating…")
                    }
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                }
            }
            Spacer(minLength: 4)
            Menu {
                SettingsLink { Text("Settings…") }
                Button("What’s New") {
                    Task { if let wn = try? await engine.call("whatsNew", as: WhatsNewData.self) { engine.whatsNew = WhatsNewSheetItem(data: wn) } }
                }
                Button("\(engine.lmsName) Profile") { engine.openWebScreen("/profile", title: "Profile") }
                Divider()
                Button("Report a Bug…") { ReportBug.open() } // (1.2.1: the web's purple button)
                Divider()
                Button("Sign Out…") { confirmSignOut = true }
            } label: {
                Image(systemName: "ellipsis.circle")
            }
            .menuStyle(.borderlessButton)
            .menuIndicator(.hidden)
            .fixedSize()
            .help("Account, and Report a Bug")
            .tourSpot(.account)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .overlay(alignment: .top) { Divider() }
        .confirmationDialog("Sign out of \(engine.lmsName) on this Mac?", isPresented: $confirmSignOut) {
            Button("Sign Out", role: .destructive) { session.signOut() }
        } message: {
            Text("Your \(engine.lmsName) session and any saved sign-in are cleared; settings stay.")
        }
    }
}

/// Text on the pasteboard (Copy Link).
func copyToPasteboard(_ text: String) {
    NSPasteboard.general.clearContents()
    NSPasteboard.general.setString(text, forType: .string)
}

/// Report a Bug (1.2.1): the site's form, as the web's purple button opens it.
enum ReportBug {
    static let url = URL(string: "https://simplcourses.com/report/")!

    @MainActor
    static func open() { NSWorkspace.shared.open(url) }
}

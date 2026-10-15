import SwiftUI

/// The window once Canvas is ready: the sidebar (Simpl's own — Dashboard, To Do, Calendar, Grades, Notifications, the
/// Inbox, each course with its sections, the groups, and the account at its foot), the screen chosen there with what
/// is pushed on it, and the toolbar: Back and Forward (a browser's, across the whole window), the screen's own title and
/// buttons, and Search (whose suggestions run commands, 1.2). The tour, the first time, goes over all of it.
struct MainShell: View {
    @EnvironmentObject private var engine: Engine
    @EnvironmentObject private var whereabouts: Whereabouts // (1.3.78: Back and Forward follow the window's moves)
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var focus = FocusTimer.shared
    /// (1.3.79) How many pins the toolbar has room for, measured as it is laid out (Tools/PinnedTools.swift): the search
    /// field gives its room first.
    @StateObject private var fit = ToolbarFit(searching: true)
    // (the screenshot suite: -SimplSidebar hidden opens the window with the sidebar folded away)
    @State private var columns: NavigationSplitViewVisibility = UserDefaults.standard.string(forKey: "SimplSidebar") == "hidden" ? .detailOnly : .all
    /// (1.3) The window was last wide enough for the sidebar beside the screen: crossing below folds it away, crossing
    /// back brings it out again (a sidebar opened by hand on a narrow window stays until the window narrows again).
    @State private var wasWide: Bool?
    @StateObject private var palette = SearchPalette()
    @ObservedObject private var appearance = AppearanceStore.shared
    /// (home-28) While the tour is on, the toolbar's buttons rest — dimmed with the window, and not to be pressed past it.
    @ObservedObject private var tour = MacTour.shared

    var body: some View {
        let touring = tour.step != nil
        NavigationSplitView(columnVisibility: $columns) {
            Sidebar()
                .id(appearance.revision) // (1.3.1: a colour worn, the sidebar drawn again in it — never the whole split view)
                .tourSpot(.sidebar)
                .navigationSplitViewColumnWidth(min: 210, ideal: 270, max: 360)
        } detail: {
            DetailStack()
                .id(appearance.revision)
        }
        .navigationSplitViewStyle(.balanced)
        .background {
            GeometryReader { g in
                Color.clear
                    .onAppear { fit(g.size.width) }
                    .onChange(of: g.size.width) { _, w in fit(w) }
            }
        }
        .overlay { TourOverlay(columns: $columns) } // (1.2) the Mac tour (Shell/Tour.swift)
        .overlay { WorkPreviewOverlay() } // (1.2.3) a piece of work's quick look, grown out of its row (Shell/WorkPreviewOverlay.swift)
        // (1.3.79) a tool opened whole, the rubric ring, Hand In and the rest are popups over the window that never block
        // it (Shell/FloatingPopup.swift: ToolsCenter.popUp, RubricPopup.show, EngineSheets)
        // (1.2) the field finds and does: its suggestions are the web search box's — a sum, commands, courses and their
        // sections, groups — and "/" (or ">") lists every command (Shell/SearchCommands.swift); one picked runs.
        // (home-29) Its prompt short enough to show whole in the field on every school ("Search Brightspa…" was cut).
        .searchable(text: $engine.query, placement: .toolbar, prompt: "Search or type /")
        .searchSuggestions { PaletteSuggestions(palette: palette) }
        .onSubmit(of: .search) {
            if palette.take(engine.query, engine: engine) { return }
            palette.submit(engine.query, engine: engine)
        }
        .onChange(of: engine.query) { _, q in
            if palette.take(q, engine: engine) { return }
            palette.update(q, engine: engine)
            // (home-13) What is typed is answered in the suggestions only — a sum, a command, a course — and the page
            // stays where it is: everything is searched on Return, or "Search for …" picked. (Emptied on the results,
            // the field goes back to where it was.)
            let t = q.trimmingCharacters(in: .whitespacesAndNewlines)
            if t.isEmpty, case .search = engine.nav.current.place {
                engine.goBack()
            }
        }
        .onAppear {
            palette.launch(engine)
            engine.popups.sidebarMoved(shown: columns != .detailOnly) // (1.3.79: whether a tool's popup keeps clear of it)
            if engine.primary == nil { tools.openLaunchTool(engine) } // (the screenshot suite's -SimplTool, over the main window)
        }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                ControlGroup {
                    Button {
                        engine.goBack()
                    } label: {
                        Label("Back", systemImage: "chevron.left")
                    }
                    .disabled(!engine.nav.canGoBack || touring)
                    .help("Back")
                    Button {
                        engine.goForward()
                    } label: {
                        Label("Forward", systemImage: "chevron.right")
                    }
                    .disabled(!engine.nav.canGoForward || touring)
                    .help("Forward")
                }
                .controlGroupStyle(.navigation)
            }
            // (1.3.2) Refresh: everything asked of the school afresh, whatever screen this is (View ▸ Reload, ⌘R)
            ToolbarItem(placement: .navigation) {
                Button {
                    engine.refresh()
                } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                        .modifier(Turning(on: engine.refreshing))
                }
                .disabled(engine.phase != .native || touring)
                .help("Refresh (⌘R): everything read from \(engine.lmsName) again")
            }
            // (1.2) the tools pinned to the toolbar, on every screen: a press opens the tool's quick version in a popover;
            // a running focus timer shows its minutes here (mac/Simpl/Tools/PinnedTools.swift). (tools-6) The pins past
            // the toolbar's room in a Tools pull-down, so the screen's own buttons keep theirs — (1.3.79) every pin that
            // fits shown, measured (ToolbarFit), not three at most whatever the room.
            if !tools.toolbarPins(timerActive: focus.active).isEmpty {
                ToolbarItemGroup(placement: .primaryAction) {
                    ForEach(fit.split(timerActive: focus.active).shown) { kind in
                        PinnedToolButton(kind: kind, engine: engine)
                            .disabled(touring)
                            .toolbarFit(fit, .pin, kind)
                    }
                    if !fit.split(timerActive: focus.active).more.isEmpty {
                        MorePinsMenu(kinds: fit.split(timerActive: focus.active).more, engine: engine)
                            .disabled(touring)
                            .toolbarFit(fit, .more)
                    }
                }
            }
        }
        // (1.3.79) another screen (its own toolbar buttons) or the sidebar shown or hidden: the pins' room measured afresh
        .onChange(of: engine.nav.current) { _, _ in fit.reset() }
        .onChange(of: columns) { _, now in
            fit.reset()
            engine.popups.sidebarMoved(shown: now != .detailOnly) // (1.3.79: a tool's popup kept in the screen's side of the window)
        }
    }
}

/// (1.3.2) A symbol turning while something goes (macOS 15's rotate effect; still before it).
private struct Turning: ViewModifier {
    let on: Bool

    func body(content: Content) -> some View {
        if #available(macOS 15.0, *) {
            content.symbolEffect(.rotate, isActive: on)
        } else {
            content
        }
    }
}

extension MainShell {
    /// (1.3) The sidebar folded away below `MainShell.narrow` points across, and out again above it — only as the window
    /// crosses the line, so the sidebar's own button still opens and closes it at any width.
    static let narrow: CGFloat = 880

    /// (1.3.80) The screenshot suite's -SimplSidebar hidden: the picture is of the window with its sidebar folded away.
    private static let launchFolded = UserDefaults.standard.string(forKey: "SimplSidebar") == "hidden"

    fileprivate func fit(_ width: CGFloat) {
        let wide = width >= Self.narrow
        guard wide != wasWide else { return }
        let first = wasWide == nil
        wasWide = wide
        if first && wide { return } // (a wide window opens as it was)
        // (a window laid out first at its default size, under the line, and only then given its picture's size, over it,
        // kept its sidebar folded: brought out by the crossing, it was pictured out — dark-58z-dna-plain-1024-folded)
        if wide && Self.launchFolded { return }
        withAnimation(Motion.gentle) { columns = wide ? .all : .detailOnly }
    }
}

/// The screen chosen in the sidebar, with what is pushed on it (an assignment, a discussion, a page, a folder, a
/// conversation). (1.3.78) Each screen arrives the way the window moved (PageArrival), and a place left goes the same
/// way (PageExit): one motion language for a push, Back and a place picked. The Dashboard, once opened, is kept over the
/// stack — its cards, its pictures, where it was scrolled — shown again as it was when the window comes back to it,
/// its answers refreshed in place; its title and toolbar stay in the stack where every screen's are (DashboardChrome).
struct DetailStack: View {
    @EnvironmentObject private var engine: Engine
    @EnvironmentObject private var whereabouts: Whereabouts // (1.3.78: drawn again as the window moves)
    @State private var dashboardKept = false
    /// (1.3.78) The place whose stack is up. A stack is made with nothing pushed on it and given its place's screens once
    /// it is there, as a push: made with screens already on it (an assignment opened from Grades, To Do or the
    /// Dashboard; Back or Forward to one), it set them away itself as it appeared, and showed its place's list.
    @State private var standing: Place?

    var body: some View {
        let place = engine.nav.current.place
        let path = engine.nav.current.path
        let dashboardUp = place == .dashboard && path.isEmpty
        ZStack {
            // (1.3.78) the stack's path is its own place's: a stack on its way out, as another place's comes in, keeps
            // the path it had — it never takes up the new place's — and one coming in has none until it is up
            NavigationStack(path: Binding(get: {
                guard engine.nav.current.place.identity == place.identity else { return path }
                return standing == place.identity ? engine.nav.current.path : []
            }, set: { p in
                guard engine.nav.current.place.identity == place.identity, standing == place.identity else { return }
                // (in the screenshot suite and developer mode the console says so: only the stack's own Back does this)
                if OpenClock.verbose, p != engine.nav.current.path {
                    NSLog("[Simpl] stack: %@ set its own path, %d screens to %d", Engine.name(of: place), engine.nav.current.path.count, p.count)
                }
                engine.nav.setPath(p)
            })) {
                PlaceScreen(place: place)
                    .background(PageGround().ignoresSafeArea())
                    .modifier(RootUnseen(top: path.isEmpty))
                    .modifier(PageArrival(move: engine.move, top: path.isEmpty && place != .dashboard))
                    .environment(\.liveOnShow, path.isEmpty) // (1.3.80: checked live only while nothing is pushed over it)
                    .navigationDestination(for: Route.self) { route in
                        RouteScreen(route: route)
                            .navigationBarBackButtonHidden(true) // (the toolbar's own Back and Forward walk the history)
                            // (1.2.3) its own ground, up under the toolbar: the screen it was opened from never shows through
                            .background(PageGround().ignoresSafeArea())
                            .modifier(PageArrival(move: engine.move, top: engine.nav.current.path.last == route))
                            .environment(\.liveOnShow, engine.nav.current.path.last == route) // (1.3.80: the one on top)
                    }
            }
            .onAppear {
                // (each place's stack, as it is made: up on the run loop's next turn, its screens put on it at once —
                // the one on top arrives by itself)
                let id = place.identity
                DispatchQueue.main.async {
                    var t = Transaction()
                    t.disablesAnimations = true
                    withTransaction(t) { standing = id }
                }
            }
            .id(place.identity)
            .transition(.page)
            .background(PageGround().ignoresSafeArea())
            if dashboardKept || dashboardUp {
                DashboardView(active: dashboardUp, version: engine.dataVersion, brightspace: engine.onBrightspace)
                    .background(PageGround().ignoresSafeArea())
                    .modifier(PageArrival(move: engine.move, top: dashboardUp))
                    .modifier(PageExit(gone: dashboardUp ? 0 : 1))
                    // (coming back, it arrives by itself: only its going is on the move's spring)
                    .transaction(value: dashboardUp) { t in if dashboardUp { t.animation = nil } }
                    .allowsHitTesting(dashboardUp)
                    .accessibilityHidden(!dashboardUp)
            }
        }
        .background { LiveWindowProbe(live: engine.live) } // (1.3.80: the window the live checks watch)
        .onAppear { if dashboardUp && !Engine.plain { dashboardKept = true } }
        .onChange(of: dashboardUp) { _, up in if up && !Engine.plain { dashboardKept = true } }
    }
}

/// (1.3.78) A place's own page in a stack made with screens due on it: not shown until it is the one on top (Back to
/// it), so it never shows between the page left and the screen arriving over it. One that has been on top stays under
/// the screens pushed on it, as it was.
private struct RootUnseen: ViewModifier {
    let top: Bool
    @State private var seen: Bool

    init(top: Bool) {
        self.top = top
        _seen = State(initialValue: top)
    }

    func body(content: Content) -> some View {
        content
            .opacity(seen ? 1 : 0)
            .onChange(of: top) { _, now in if now { seen = true } }
    }
}

/// The screen for a place.
struct PlaceScreen: View {
    let place: Place

    var body: some View {
        switch place {
        case .dashboard: DashboardChrome() // (1.3.78: the Dashboard itself is kept over the stack, DetailStack)
        case .courses: CoursesView()
        case .todo: TodoView()
        case .calendar: CalendarView()
        case .grades: GradesView()
        case .notifications: NotificationsView()
        case .inbox: InboxView()
        case .tools: ToolsView()
        case .externalTools: ExternalToolsView()
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
    @EnvironmentObject private var whereabouts: Whereabouts // (1.3.78: its selection follows the window's moves)
    @Environment(\.openWindow) private var openWindow
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
            SchoolHeader() // (1.3.8) the school at the top, as the web's sidebar has it; Dashboard under it
            // (1.3.78) the row of the place showing kept in sight: a window opened on a course's section far down the
            // list (a window more, on its Grades) shows it, and so does a move to a place whose row is scrolled away
            ScrollViewReader { proxy in
                list
                    .scrollContentBackground(.hidden) // (the sidebar's glass is the column's own)
                    .task {
                        try? await Task.sleep(nanoseconds: 250_000_000) // (once the course's sections are in the list)
                        proxy.scrollTo(whereabouts.nav.current.place)
                    }
                    .onChange(of: whereabouts.nav.current.place) { _, p in proxy.scrollTo(p) }
                    .task(id: "\(engine.more?.items.last?.id ?? "")|\(engine.courses.count)|\(engine.groups.count)") {
                        // (the screenshot suite: -SimplSidebarScroll more — the list scrolled to its last section, once
                        // that is in, and again as the rows above it change, for the section's picture)
                        guard UserDefaults.standard.string(forKey: "SimplSidebarScroll") == "more", let last = engine.more?.items.last?.id else { return }
                        try? await Task.sleep(nanoseconds: 600_000_000)
                        proxy.scrollTo(last, anchor: .bottom)
                    }
            }
            AccountBar()
        }
        .background { SidebarGround() } // (1.3.1) the sidebar's photo, frosted, behind its rows
    }

    private var list: some View {
        List(selection: selection) {
            Section {
                Label("Dashboard", systemImage: "square.grid.2x2")
                    .tourSpot(.dashboardRow)
                    .tag(Place.dashboard)
                    .windowMenu(Place.dashboard)
                Label("To Do", systemImage: "checklist").tag(Place.todo)
                    .windowMenu(Place.todo)
                Label("Calendar", systemImage: "calendar").tag(Place.calendar)
                    .windowMenu(Place.calendar)
                Label("Grades", systemImage: "chart.bar.xaxis")
                    .tourSpot(.gradesRow)
                    .tag(Place.grades)
                    .windowMenu(Place.grades)
                Label("Notifications", systemImage: "bell")
                    .badge(engine.countsLive ? (engine.snapshot?.notifUnread ?? 0) : 0) // (1.2: never a count from before)
                    .tag(Place.notifications)
                    .windowMenu(Place.notifications)
                if !engine.onBrightspace { // (Brightspace has no Inbox or groups here, 2.99.22)
                    Label("Inbox", systemImage: "tray")
                        .badge(engine.countsLive ? (engine.snapshot?.inboxUnread ?? 0) : 0)
                        .tag(Place.inbox)
                        .windowMenu(Place.inbox)
                }
                Label("Tools", systemImage: "wrench.and.screwdriver") // (1.2)
                    .tourSpot(.toolsRow)
                    .tag(Place.tools)
                    .windowMenu(Place.tools)
                if !engine.onBrightspace { // (1.3.67: Canvas's external tools)
                    Label("External Tools", systemImage: "puzzlepiece.extension")
                        .tag(Place.externalTools)
                        .windowMenu(Place.externalTools)
                }
            }
            Section("Courses") {
                ForEach(engine.courses) { course in
                    courseRow(course)
                    if isOpen("courses/\(course.id)") {
                        ForEach(sections("courses/\(course.id)")) { s in
                            Label(s.label, systemImage: Glyph.section(s.kind))
                                .padding(.leading, 20)
                                .tag(Place.section("courses/\(course.id)", s.kind))
                                .windowMenu(Place.section("courses/\(course.id)", s.kind))
                                .transition(.opacity.combined(with: .move(edge: .top)))
                        }
                    }
                }
                Label("All Courses", systemImage: "books.vertical").tag(Place.courses)
                    .windowMenu(Place.courses)
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
                            .windowMenu(Place.home("groups/\(g.id)"))
                            .transition(.opacity.combined(with: .move(edge: .top)))
                        }
                    }
                }
            }
            // what the school added to Canvas's own navigation — its tools, History, Help — under the app's own, as the
            // web's sidebar has it; none when it adds nothing (Shell/MoreFromCanvas.swift)
            if let more = engine.more, !more.items.isEmpty {
                MoreSection(more: more)
            }
        }
        .listStyle(.sidebar)
        .labelStyle(SidebarThemeLabel()) // (home-16) a colour worn: the row chosen's glyph in it, the words as the Mac's
        .environment(\.sidebarPlace, engine.nav.current.place)
        .environment(\.sidebarRowSize, .large)
        .animation(Motion.gentle, value: open)
        .animation(Motion.gentle, value: engine.courses)
        .animation(Motion.gentle, value: engine.more)
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
        .windowMenu(Place.groups)
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
                // (home-14) a count, as the Notifications row's, that says what it counts when the pointer rests on it
                let said = PreviewFormat.text(c.unreadLabel) ?? "\(n) unread \(n == 1 ? "announcement" : "announcements")"
                Text("\(n)")
                    .font(.sCaption2.weight(.semibold).monospacedDigit())
                    .foregroundStyle(.secondary)
                    .help(said)
                    .accessibilityLabel(said)
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
        .readsAhead { engine.readAhead(.home(ctx)) } // (1.3.78: its home, read ahead)
        .tag(Place.home(ctx))
        .id(Place.home(ctx)) // (1.3.78: the list scrolls to it)
        .contextMenu {
            Button("Open in New Window") { openWindow(id: "window", value: Engine.name(of: .home(ctx))) } // (1.3.69)
            Divider()
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

/// (1.3.69) A sidebar row's menu: its place in a window of its own. (1.3.78) And what the row opens, read ahead while
/// the pointer rests on it; and its place as its name in the list, for the list to scroll to it.
private struct WindowMenu: ViewModifier {
    let place: Place
    @EnvironmentObject private var engine: Engine
    @Environment(\.openWindow) private var openWindow

    func body(content: Content) -> some View {
        content
            .id(place)
            .contextMenu {
                Button("Open in New Window") { openWindow(id: "window", value: Engine.name(of: place)) }
            }
            .readsAhead { engine.readAhead(place) }
    }
}

private extension View {
    /// A sidebar row's menu and read-ahead, and (home-16) whether it is the row chosen, for its glyph's colour.
    func windowMenu(_ place: Place) -> some View { modifier(WindowMenu(place: place)).sidebarRow(place) }
}

/// Who is signed in, at the sidebar's foot — (home-31) their name and the address they sign in with (the school is named
/// once, at the sidebar's head) — with what was under the avatar on the web: Settings, What's New, the Canvas profile,
/// Report a Bug and Sign Out.
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
                    Text(PreviewFormat.text(me?.email) ?? engine.snapshot?.site ?? engine.host)
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
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
                Button("What’s New") { engine.whatsNew = WhatsNewSheetItem(data: MacWhatsNew.all) }
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

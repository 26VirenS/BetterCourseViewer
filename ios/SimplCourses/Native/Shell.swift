import SwiftUI

/// The app: the Canvas web view underneath as the engine (and, while the app's chrome is off — signing
/// in, the guided setup, the interface switched off — the whole screen), Apple's own tab bar, stacks and
/// screens on top of it once the page says it is ready.
struct RootView: View {
    let host: String
    @EnvironmentObject private var session: AppSession
    @StateObject private var engine: Engine
    @ObservedObject private var reminders = Reminders.shared
    @Environment(\.scenePhase) private var scenePhase

    init(host: String) {
        self.host = host
        _engine = StateObject(wrappedValue: Engine(host: host))
    }

    var body: some View {
        ZStack {
            EngineHost(engine: engine)
                .ignoresSafeArea()
                .allowsHitTesting(engine.phase != .native)
            if engine.phase == .native {
                NativeShell()
                    .transition(.opacity)
            }
            if engine.phase == .starting {
                Splash()
                    .transition(.opacity)
            }
        }
        .environmentObject(engine)
        .overlay { LoginLayer(assist: engine.web.login, pageShown: engine.phase != .native) } // (the app's sign-in form, "Logging you in", "Stay logged in?", Start Over)
        .sheet(isPresented: $session.showSettings) {
            SettingsSheet()
                .environmentObject(engine)
        }
        .sheet(item: $engine.whatsNew) { item in
            WhatsNewSheet(data: item.data) {
                // up at last: this version is seen
                Task { _ = try? await engine.call("whatsNewSeen", ["version": item.data.version ?? ""], as: OK.self) }
            }
        }
        .onChange(of: session.showSettings) { engine.settingsOpen = session.showSettings }
        .sheet(item: $engine.tool) { t in
            ToolSheet(launch: t)
                .environmentObject(engine)
                .presentationDetents([.large])
        }
        .fullScreenCover(item: $engine.quiz) { q in
            QuizScreen(launch: q)
                .environmentObject(engine)
        }
        .fullScreenCover(isPresented: $engine.setup) {
            SetupScreen()
                .environmentObject(engine)
        }
        .onReceive(NotificationCenter.default.publisher(for: .simplOpenSetup)) { _ in
            Task {
                try? await Task.sleep(nanoseconds: 450_000_000) // (the Settings sheet away first)
                engine.setup = true
            }
        }
        .onAppear {
            engine.start()
            openReminder() // (a reminder pressed before the app's screens were made)
        }
        // (1.7.6) a link that opened the app: opened as a notification's is, the sign-in asked after first
        .onOpenURL { engine.openFromOutside($0) }
        .onReceive(NotificationCenter.default.publisher(for: .simplSignedOut)) { _ in
            engine.web.login.forget()
            engine.web.load() // back to the login page
        }
        .onReceive(NotificationCenter.default.publisher(for: .simplInterfaceToggled)) { _ in
            engine.reload() // Canvas's own bundles are allowed or blocked per page; a fresh load applies it
        }
        // a reminder or an alert pressed: its work opens once the school is known to have the student signed in and the
        // app's screens are up (a cold launch waits for them) — Engine.openChecked
        .onChange(of: reminders.opening) { openReminder() }
        .onChange(of: engine.phase) { openReminder() }
        // back in the foreground: (1.7.6) the sign-in asked after, however the app came back; the reminders set again
        // from what Canvas says now
        .onChange(of: scenePhase) {
            if scenePhase == .background { Activity.shared.schedule() } // (the next look for new activity, while away)
            guard scenePhase == .active else { return }
            engine.returned()
            guard engine.phase == .native else { return }
            Task { await Reminders.shared.reschedule(engine) }
            Task { await Activity.shared.sync(engine) }
        }
    }

    private func openReminder() {
        guard let url = reminders.opening else { return }
        reminders.opening = nil
        engine.openChecked(url)
    }
}

/// While the first page loads: the app's name, not a blank web view.
struct Splash: View {
    var body: some View {
        ZStack {
            Color(.systemBackground).ignoresSafeArea()
            VStack(spacing: 18) {
                Image(systemName: "graduationcap.fill")
                    .font(.system(size: 54, weight: .semibold))
                    .foregroundStyle(.tint)
                    .symbolEffect(.pulse, options: .repeating)
                Text("Simpl Courses")
                    .font(.title2.weight(.bold))
                ProgressView()
            }
        }
    }
}

/// Apple's tab bar (Liquid Glass on iOS 26, minimising as a list scrolls), a stack per tab, and a search tab —
/// four tabs and Search, so none is folded into More; the Calendar is pushed from Today and To Do.
struct NativeShell: View {
    @EnvironmentObject private var engine: Engine
    /// The shell's own frame as SwiftUI measures it — the screen only for the simulator suite, which holds the app to the
    /// Duo's width on an iPad (-SimplWideWidth).
    @State private var frame: CGRect = .zero
    /// (iPhone 1.8.1, build 36) The whole screen, in window coordinates: the window's own bounds as UIKit has them
    /// (Engine.screen) — the side tab bar, the camera's strip and every safe area inside it. Its middle is where the Duo
    /// folds. (Builds 33 to 35 measured it in SwiftUI with a view ignoring the safe areas; on the Duo that still came out
    /// as the space inside them, and the split stayed 40 points left of the crease.)
    private var screen: CGRect { Self.shotWidth == nil && engine.screen.width > 0 ? engine.screen : frame }
    /// (iPhone 1.8) Both of the screen's sides 600 points or more: an iPhone Duo unfolded, or an iPad (`wideScreen`).
    private var wide: Bool { min(screen.width, screen.height) >= 600 }
    /// 750 points across or more (the Duo on its side, an iPad): two pages.
    private var twoPane: Bool { wide && screen.width >= 750 }

    var body: some View {
        // Search is a button in the tab bar, not a page (1.5.4): pressed, it raises the search sheet halfway over the
        // screen showing, and the tab stays where it was
        TabView(selection: Binding(get: { engine.tab }, set: { t in
            if t == .search { Haptics.tap(); engine.searchOpen = true } else { engine.tab = t }
        })) {
            Tab("Today", systemImage: "sun.max.fill", value: AppTab.today) {
                stack(.today) { TodayView() }
            }
            Tab("Courses", systemImage: "books.vertical.fill", value: AppTab.courses) {
                stack(.courses) { CoursesView() }
            }
            Tab("To Do", systemImage: "checklist", value: AppTab.todo) {
                stack(.todo) { TodoView() }
            }
            Tab("Grades", systemImage: "chart.bar.fill", value: AppTab.grades) {
                stack(.grades) { GradesView() }
            }
            Tab(value: AppTab.search, role: .search) {
                Color.clear // (never shown: the sheet below is the search)
            }
        }
        .sheet(isPresented: $engine.searchOpen) {
            SearchSheet()
                .environmentObject(engine)
        }
        .minimizingTabBar(!engine.holdTabBar)
        .background(TabBarProbe(engine: engine).allowsHitTesting(false))
        .sensoryFeedback(.selection, trigger: engine.tab)
        .environment(\.wideScreen, wide)
        .environment(\.foldX, wide ? screen.midX : nil)
        .environment(\.twoPane, twoPane)
        .environment(\.screenRect, screen)
        .onChange(of: screen, initial: true) { _, _ in
            engine.foldX = wide ? screen.midX : nil
            engine.twoPane = twoPane
        }
        // (iPhone 1.8: the Duo unfolded or folded, an iPad's window resized — every screen lays itself out again)
        .background {
            Color.clear
                .ignoresSafeArea()
                .onGeometryChange(for: CGRect.self) { $0.frame(in: .global) } action: { frame = $0 }
                .allowsHitTesting(false)
        }
        // (the simulator suite only, scripts/dev/ios-shots.sh: -SimplWideWidth 626 holds the app to the Duo's inner
        // display's width on an iPad, where Xcode has no Duo to picture)
        .frame(maxWidth: Self.shotWidth ?? .infinity)
    }

    private static let shotWidth: CGFloat? = {
        let w = UserDefaults.standard.double(forKey: "SimplWideWidth")
        return w >= 320 ? CGFloat(w) : nil
    }()

    private func stack<Root: View>(_ tab: AppTab, @ViewBuilder _ root: () -> Root) -> some View {
        TabSplit(tab: tab, root: root())
    }
}

/// (iPhone 1.8.1, build 37) A tab, as Apple's split view (NavigationSplitView) — Apple's own container for a list and
/// what is opened from it, the one its iPhone Duo guidance points to. Regular width and room for two (the Duo open on
/// its side, an iPad): the tab's own screen in the list column on the left, whatever is opened from it on the right,
/// with a stack of its own there (deeper screens push in that column, its Back turns back); pressing something else in
/// the list puts it on the right in place of what was there (Engine.push). Nothing opened yet, the right column shows
/// the tab's own companion (SplitHome): the Calendar beside Today and To Do, the first course beside Courses and
/// Grades. Compact (a phone, the Duo folded, the Duo open upright — two columns there would each be narrower than any
/// iPhone), the split view collapses into one stack, the list then what was opened, as Apple's own apps do — and as it
/// folds and unfolds it keeps what is open: the screen, a sheet or a menu on it, where it is scrolled. iOS places each
/// column's bars itself (on the Duo, in its rail) and keeps them clear of the fold. The list column's edge sits on the
/// fold where there is one.
struct TabSplit<Root: View>: View {
    let tab: AppTab
    let root: Root
    @EnvironmentObject private var engine: Engine
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.screenRect) private var screen
    @Environment(\.foldX) private var foldX
    @State private var visibility: NavigationSplitViewVisibility = .all
    @State private var compactColumn: NavigationSplitViewColumn = .sidebar
    @State private var frame: CGRect = .zero

    /// Two columns: the system's regular width, and room for two phone-wide columns (750 points).
    private var twoColumns: Bool { sizeClass == .regular && max(screen.width, frame.width) >= 750 }

    var body: some View {
        let path = engine.paths[tab]
        let two = twoColumns
        // the list column to the fold (from the split view's own left edge), never under 320 or over half
        let listWidth: CGFloat = {
            guard frame.width > 0 else { return 380 }
            let toFold = (foldX ?? frame.midX) - frame.minX
            return min(max(320, toFold), frame.width * 0.56).rounded()
        }()
        NavigationSplitView(columnVisibility: $visibility, preferredCompactColumn: $compactColumn) {
            root
                .environment(\.splitList, two)
                .navigationSplitViewColumnWidth(min: two ? listWidth : 320, ideal: listWidth, max: listWidth) // (build 39: all three, since iOS 26 shows the least)
                .background(SplitColumnWidth(width: two ? listWidth : 0, revision: path.hashValue).allowsHitTesting(false)) // (builds 38–40: the width iPadOS 26 otherwise keeps at 320)
                .onGeometryChange(for: CGRect.self) { $0.frame(in: .global) } action: { r in engine.splitEdge[tab] = two ? r.maxX : nil }
        } detail: {
            if let first = path.first {
                NavigationStack(path: rest) {
                    RouteScreen(route: first)
                        .navigationDestination(for: Route.self) { route in RouteScreen(route: route) }
                }
                .id(first)
            } else {
                SplitHome(tab: tab)
            }
        }
        .navigationSplitViewStyle(.balanced)
        .environment(\.horizontalSizeClass, two ? .regular : .compact)
        .onGeometryChange(for: CGRect.self) { $0.frame(in: .global) } action: { frame = $0 }
        // (compact: the stack shows what is open, or the list when nothing is — and Back from the first screen to the
        // list closes it)
        .onChange(of: path.isEmpty, initial: true) { _, empty in compactColumn = empty ? .sidebar : .detail }
        .onChange(of: compactColumn) { _, col in
            if col == .sidebar && !two && !engine.paths[tab].isEmpty { engine.paths[tab] = [] }
        }
        .onChange(of: two, initial: true) { _, on in
            if !on { engine.splitEdge[tab] = nil }
            if on { visibility = .all }
        }
    }

    /// The right column's stack past its first screen.
    private var rest: Binding<[Route]> {
        Binding(get: { Array(engine.paths[tab].dropFirst()) }, set: { more in
            guard let first = engine.paths[tab].first else { return }
            engine.paths[tab] = [first] + more
        })
    }
}

/// (iPhone 1.8.1, build 37) The right column with nothing opened in it yet: the tab's own companion — the Calendar beside
/// Today and To Do; the first course's home beside Courses, its grades beside Grades (as Mail opens on the first message).
struct SplitHome: View {
    let tab: AppTab
    @EnvironmentObject private var engine: Engine
    @State private var first: CourseRow?
    @State private var loaded = false

    var body: some View {
        switch tab {
        case .today, .todo, .search:
            CalendarView()
        case .courses, .grades:
            Group {
                if let c = first {
                    if tab == .courses { ContextHome(ctx: "courses/\(c.id)") } else { CourseGradesView(courseId: c.id) }
                } else if loaded {
                    ContentUnavailableView("No Courses", systemImage: "books.vertical", description: Text("Courses you choose in Settings show here."))
                } else {
                    ProgressView()
                }
            }
            .task {
                if let d = try? await engine.call("courses", as: CoursesData.self) { first = d.rows.first }
                loaded = true
            }
        }
    }
}

/// The screen a route opens.
struct RouteScreen: View {
    let route: Route

    var body: some View {
        switch route {
        case .web(let url, let title):
            WebScreen(url: url, title: title)
        case .notifications:
            NotificationsView()
        case .calendar:
            CalendarView()
        case .course(let id):
            ContextHome(ctx: "courses/\(id)")
        case .group(let id):
            ContextHome(ctx: "groups/\(id)")
        case .groups:
            GroupsView()
        case .section(let ctx, let kind):
            SectionScreen(ctx: ctx, kind: kind)
        case .topic(let ctx, let id):
            TopicView(ctx: ctx, id: id)
        case .assignment(let course, let id):
            AssignmentView(course: course, id: id)
        case .page(let ctx, let slug):
            PageView(ctx: ctx, slug: slug)
        case .folder(let ctx, let id, let name):
            FilesView(ctx: ctx, folder: id, name: name)
        case .inbox:
            InboxView()
        case .conversation(let id):
            ConversationView(id: id)
        }
    }
}

/// The Calendar, the bell (Notifications) and the account menu, at the top right of a root screen.
struct ShellToolbar: ViewModifier {
    var bell = false
    var calendar = false
    /// A screen's own add button (To Do's new task), with the shell's buttons and before the picture.
    var add: (() -> Void)? = nil
    /// A screen's own menu (To Do's grouping), first in the capsule: with the title on the bar's row, nothing sits at its leading edge.
    var menu: AnyView? = nil
    @EnvironmentObject private var engine: Engine

    /// The capsule's height (the bar's own glass buttons are 44 pt) and the glass left round the picture: 1 pt
    /// (1.5.5) — the picture meets the inside of the glass's rim, so it fills the end without bulging past it
    /// (3 pt read as a gap, none as a picture too big for its pill).
    static let height: CGFloat = 44
    static let ring: CGFloat = 1

    func body(content: Content) -> some View {
        // the buttons and the picture share one glass capsule, drawn here rather than by the bar's own grouping (1.5.1):
        // the bar pads every item it groups, which left the picture small and off the capsule's end. The screen's large
        // title sits on the same row, at the leading edge, rather than under it (inlineLarge).
        if #available(iOS 26.0, *) {
            content
                .toolbarTitleDisplayMode(.inlineLarge)
                .toolbar {
                    ToolbarItem(placement: .topBarTrailing) { capsule }
                        .sharedBackgroundVisibility(.hidden) // (the capsule is its own glass: no second one round it)
                }
        } else {
            content
                .toolbarTitleDisplayMode(.inlineLarge)
                .toolbar {
                    ToolbarItem(placement: .topBarTrailing) { capsule }
                }
        }
    }

    private var capsule: some View {
        HStack(spacing: 0) {
            if let menu {
                menu.frame(width: 42, height: Self.height).contentShape(Rectangle())
            }
            if calendar {
                Button {
                    Haptics.tap()
                    engine.openCalendar()
                } label: {
                    icon(Image(systemName: "calendar"))
                }
                .accessibilityLabel("Calendar")
            }
            if let add {
                Button(action: add) { icon(Image(systemName: "plus")) }
                    .accessibilityLabel("Add a task")
            }
            if bell {
                Button {
                    Haptics.tap()
                    engine.openNotifications()
                } label: {
                    icon(Image(systemName: (engine.snapshot?.notifUnread ?? 0) > 0 ? "bell.badge" : "bell")
                        .symbolRenderingMode(.multicolor)
                        .contentTransition(.symbolEffect(.replace)))
                }
                .accessibilityLabel("Notifications")
            }
            AccountMenu(size: Self.height - Self.ring * 2)
                .padding(Self.ring)
        }
        .padding(.leading, (menu != nil || calendar || add != nil || bell) ? 6 : 0)
        .buttonStyle(.plain)
        .foregroundStyle(.primary)
        .font(.system(size: 17, weight: .medium))
        .imageScale(.large)
        .modifier(GlassCapsule(interactive: true)) // (it gives under the finger and stretches toward a drag, as the bar's own glass does)
    }

    private func icon(_ image: some View) -> some View {
        image.frame(width: 42, height: Self.height).contentShape(Rectangle())
    }
}

extension View {
    func shellToolbar(bell: Bool = false, calendar: Bool = false, add: (() -> Void)? = nil, menu: AnyView? = nil) -> some View { modifier(ShellToolbar(bell: bell, calendar: calendar, add: add, menu: menu)) }
}

/// What the web interface kept under the avatar on a phone (Inbox, Groups, Settings, What's New, the
/// Canvas profile, Sign out), as Apple's own menu. (No light/dark switch: the phone's own setting rules, 1.4.)
struct AccountMenu: View {
    var size: CGFloat = 30
    @EnvironmentObject private var engine: Engine
    @EnvironmentObject private var session: AppSession
    @State private var confirmSignOut = false

    var body: some View {
        let me = engine.snapshot?.me
        let inbox = engine.snapshot?.inboxUnread ?? 0
        Menu {
            Section(me.map { "\($0.name)\($0.email.map { "\n\($0)" } ?? "")" } ?? "Account") {
                if !engine.onBrightspace { // (Brightspace has no Inbox or groups here, 2.99.22)
                    Button { engine.push(.inbox) } label: {
                        Label(inbox > 0 ? "Inbox (\(inbox) unread)" : "Inbox", systemImage: "tray")
                    }
                    Button { engine.push(.groups) } label: { Label("Groups", systemImage: "person.3") }
                }
                // (no Tools on the iPhone yet, 1.2)
            }
            Section {
                Button { session.showSettings = true } label: { Label("Settings", systemImage: "gearshape") }
                Button {
                    Task {
                        if let wn = try? await engine.call("whatsNew", as: WhatsNewData.self) { engine.whatsNew = WhatsNewSheetItem(data: wn) }
                    }
                } label: { Label("What’s New", systemImage: "star") }
                Button { engine.openWeb("/profile", title: "Profile") } label: { Label("\(engine.lmsName) Profile", systemImage: "person.crop.circle") }
            }
            Section {
                Button(role: .destructive) { confirmSignOut = true } label: {
                    Label("Sign Out", systemImage: "rectangle.portrait.and.arrow.right")
                }
            }
        } label: {
            Avatar(person: me, size: size)
                .contentShape(Circle())
        }
        // the picture alone, edge to edge: no bar-button padding round it (it sat 10 pt in from the capsule's end)
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .fixedSize()
        .accessibilityLabel("Account")
        .confirmationDialog("Sign out of \(engine.lmsName) on this device?", isPresented: $confirmSignOut, titleVisibility: .visible) {
            Button("Sign Out", role: .destructive) {
                Haptics.play("warning")
                session.signOut()
            }
        } message: {
            Text("Your \(engine.lmsName) session and any saved sign-in are cleared; settings stay.")
        }
    }
}

/// The student's picture, or their initials on a tint.
struct Avatar: View {
    let person: Person?
    var size: CGFloat = 30

    var body: some View {
        Group {
            if let s = person?.avatar, let url = URL(string: s) {
                AsyncImage(url: url) { image in
                    image.resizable().scaledToFill()
                } placeholder: {
                    initials
                }
            } else {
                initials
            }
        }
        .frame(width: size, height: size)
        .clipShape(Circle())
    }

    private var initials: some View {
        ZStack {
            Circle().fill(Color.accentColor.gradient)
            Text(person?.initials?.isEmpty == false ? person!.initials! : "·")
                .font(.system(size: size * 0.4, weight: .semibold))
                .foregroundStyle(.white)
        }
    }
}

/// What changed, as Apple's own sheet.
struct WhatsNewSheet: View {
    let data: WhatsNewData
    /// The sheet is up (What's New after an update is marked seen only then).
    var onShown: (() -> Void)? = nil
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List {
                ForEach(data.releases) { release in
                    Section {
                        ForEach(release.notes, id: \.self) { note in
                            Label {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(note.title).font(.body.weight(.semibold))
                                    if let body = note.body, !body.isEmpty {
                                        Text(body).font(.subheadline).foregroundStyle(.secondary)
                                    }
                                }
                            } icon: {
                                Image(systemName: icon(note.kind))
                                    .foregroundStyle(tone(note.kind))
                            }
                            .padding(.vertical, 2)
                        }
                    } header: {
                        Text("Version \(release.version)")
                    }
                }
            }
            .navigationTitle("What’s New")
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { dismiss() }
                }
            }
        }
        .presentationDetents([.medium, .large])
        .presentationDragIndicator(.visible)
        .onAppear { onShown?() }
    }

    private func icon(_ kind: String) -> String {
        switch kind {
        case "new": return "sparkles"
        case "fixed": return "wrench.adjustable"
        default: return "arrow.up.circle"
        }
    }

    private func tone(_ kind: String) -> Color {
        switch kind {
        case "new": return .green
        case "fixed": return .orange
        default: return .blue
        }
    }
}

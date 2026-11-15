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
        PaneStack(tab: tab, root: root())
    }
}

/// A tab's stack: Apple's own, one screen at a time, pushed and popped — on a phone, the Duo folded or upright.
/// (iPhone 1.8.1) With two pages (the Duo on its side, an iPad), the screen showing on the right and the one it was
/// opened from on the left, meeting at the fold; the tab's own screen alone across both halves (its sections in two
/// columns, ColumnList) until something is opened. The right page IS the stack — the same one the phone shows, at the
/// same place in the view tree — so folding and unfolding keep what is open: the screen, where it is scrolled, a sheet
/// or a menu up on it, what was typed; Back and the edge swipe are the stack's own. The left page is drawn from the
/// stack's path (the screen under the top, or the tab's own): opened from the right, the right's screen turns to the
/// left; opened from the left (another item in its list), the new one takes the right page's place (Engine.push).
/// The pages are laid across the whole screen and split at its very middle — where the Duo folds — each given only the
/// margins it has: the screen's own at its outer side (the camera's strip, the tab bar), the gutter at the fold.
struct PaneStack<Root: View>: View {
    let tab: AppTab
    let root: Root
    @EnvironmentObject private var engine: Engine
    @Environment(\.twoPane) private var twoPane
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// The stack's frame inside the safe areas, and where the pages' row really is (laid out to ignore the safe areas side
    /// to side: on the Duo that is the whole screen, or as far as the system lets it go) — window coordinates.
    @State private var frame: CGRect = .zero
    @State private var row: CGRect = .zero
    @Environment(\.foldX) private var foldX

    /// The left page: the tab's own screen, or a screen of the stack (by its place in it, so the same screen twice is two).
    enum Page: Hashable {
        case root
        case route(Route, depth: Int)
    }

    var body: some View {
        let path = engine.paths[tab]
        let two = twoPane && !path.isEmpty
        // (build 36) every number from where things really are: the margin each outer side needs is how far the row reaches
        // past the safe area there (none, if the system kept it inside), and the left page runs to the fold — the screen's
        // middle (NativeShell, from UIKit) — from wherever the row starts
        let inLeft = twoPane && row.width > 0 ? max(0, frame.minX - row.minX) : 0
        let inRight = twoPane && row.width > 0 ? max(0, row.maxX - frame.maxX) : 0
        let half: CGFloat? = row.width > 0 ? ((foldX ?? row.midX) - row.minX).rounded() : nil
        let left: Page = path.count >= 2 ? .route(path[path.count - 2], depth: path.count - 1) : .root
        // (one shape whether or not there are two pages: the stack keeps its place, and with it everything open on it)
        ZStack {
            Color.clear
                .onGeometryChange(for: CGRect.self) { $0.frame(in: .global) } action: { frame = $0 }
            HStack(spacing: 0) {
                if two {
                    NavigationStack { leftPage(left) }
                        .id(left)
                        .safeAreaPadding(EdgeInsets(top: 0, leading: inLeft, bottom: 0, trailing: foldGutter))
                        .frame(width: half)
                        .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.asymmetric(insertion: AnyTransition.move(edge: .trailing).combined(with: .opacity), removal: AnyTransition.opacity))
                }
                NavigationStack(path: $engine.paths[dynamicMember: \Paths.[tab]]) {
                    root.navigationDestination(for: Route.self) { route in RouteScreen(route: route) }
                }
                .safeAreaPadding(EdgeInsets(top: 0, leading: two ? foldGutter : inLeft, bottom: 0, trailing: inRight))
                .frame(maxWidth: .infinity)
            }
            .ignoresSafeArea(.container, edges: twoPane ? Edge.Set.horizontal : Edge.Set())
            .onGeometryChange(for: CGRect.self) { $0.frame(in: .global) } action: { row = $0 }
        }
        .animation(reduceMotion ? Animation.easeInOut(duration: 0.2) : Animation.snappy(duration: 0.35), value: two)
        .animation(reduceMotion ? Animation.easeInOut(duration: 0.2) : Animation.snappy(duration: 0.35), value: left)
    }

    @ViewBuilder
    private func leftPage(_ p: Page) -> some View {
        switch p {
        case .root:
            root
        case .route(let route, _):
            switch route {
            case .web(_, let title):
                // (one web view, and it is the right page's: a page of the web interface on the left says what it is,
                // and a press turns back to it)
                ContentUnavailableView {
                    Label(title.isEmpty ? "Canvas Page" : title, systemImage: "globe")
                } description: {
                    Text("The page you came from.")
                } actions: {
                    Button("Go Back to It") {
                        Haptics.tap()
                        var path = engine.paths[tab]
                        if !path.isEmpty { path.removeLast() }
                        engine.paths[tab] = path
                    }
                }
                .navigationTitle(title)
                .navigationBarTitleDisplayMode(.inline)
            default:
                RouteScreen(route: route)
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

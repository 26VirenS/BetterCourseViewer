import SwiftUI

/// The app: the Canvas web view underneath as the engine (and, while the app's chrome is off — signing
/// in, the guided setup, the interface switched off — the whole screen), Apple's own tab bar, stacks and
/// screens on top of it once the page says it is ready.
struct RootView: View {
    let host: String
    @EnvironmentObject private var session: AppSession
    @StateObject private var engine: Engine

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
        .overlay { LoginLayer(assist: engine.web.login) } // (the app's sign-in form, "Logging you in", "Stay logged in?")
        .sheet(isPresented: $session.showSettings) {
            SettingsSheet()
        }
        .sheet(item: $engine.whatsNew) { item in
            WhatsNewSheet(data: item.data)
        }
        .onAppear { engine.start() }
        .onReceive(NotificationCenter.default.publisher(for: .simplSignedOut)) { _ in
            engine.web.login.forget()
            engine.web.load() // back to the login page
        }
        .onReceive(NotificationCenter.default.publisher(for: .simplInterfaceToggled)) { _ in
            engine.reload() // Canvas's own bundles are allowed or blocked per page; a fresh load applies it
        }
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

    var body: some View {
        TabView(selection: $engine.tab) {
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
                stack(.search) { SearchView() }
            }
        }
        .minimizingTabBar()
        .sensoryFeedback(.selection, trigger: engine.tab)
    }

    private func stack<Root: View>(_ tab: AppTab, @ViewBuilder _ root: () -> Root) -> some View {
        NavigationStack(path: $engine.paths[dynamicMember: \Paths.[tab]]) {
            root()
                .navigationDestination(for: Route.self) { route in
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

    func body(content: Content) -> some View {
        // the buttons and the picture share one glass capsule; the screen's large title sits on the same row,
        // at the leading edge, rather than under it (inlineLarge)
        content
            .toolbarTitleDisplayMode(.inlineLarge)
            .toolbar {
                buttons
                ToolbarItem(placement: .topBarTrailing) {
                    AccountMenu()
                }
            }
    }

    @ToolbarContentBuilder
    private var buttons: some ToolbarContent {
        if let menu {
            ToolbarItem(placement: .topBarTrailing) { menu }
        }
        if calendar {
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    Haptics.tap()
                    engine.openCalendar()
                } label: {
                    Image(systemName: "calendar")
                }
                .accessibilityLabel("Calendar")
            }
        }
        if let add {
            ToolbarItem(placement: .topBarTrailing) {
                Button(action: add) { Image(systemName: "plus") }
                    .accessibilityLabel("Add a task")
            }
        }
        if bell {
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    Haptics.tap()
                    engine.openNotifications()
                } label: {
                    Image(systemName: (engine.snapshot?.notifUnread ?? 0) > 0 ? "bell.badge" : "bell")
                        .symbolRenderingMode(.multicolor)
                }
                .accessibilityLabel("Notifications")
            }
        }
    }
}

extension View {
    func shellToolbar(bell: Bool = false, calendar: Bool = false, add: (() -> Void)? = nil, menu: AnyView? = nil) -> some View { modifier(ShellToolbar(bell: bell, calendar: calendar, add: add, menu: menu)) }
}

/// What the web interface kept under the avatar on a phone (Inbox, Groups, Tools, the appearance,
/// Settings, the guided setup, What's New, the Canvas profile, Sign out), as Apple's own menu.
struct AccountMenu: View {
    var size: CGFloat = 30
    @EnvironmentObject private var engine: Engine
    @EnvironmentObject private var session: AppSession
    @State private var confirmSignOut = false

    var body: some View {
        let me = engine.snapshot?.me
        let inbox = engine.snapshot?.inboxUnread ?? 0
        let dark = engine.snapshot?.dark ?? false
        Menu {
            Section(me.map { "\($0.name)\($0.email.map { "\n\($0)" } ?? "")" } ?? "Account") {
                Button { engine.push(.inbox) } label: {
                    Label(inbox > 0 ? "Inbox (\(inbox) unread)" : "Inbox", systemImage: "tray")
                }
                Button { engine.push(.groups) } label: { Label("Groups", systemImage: "person.3") }
                // (no Tools on the iPhone yet, 1.2)
            }
            Section {
                Button {
                    Haptics.select()
                    Task {
                        _ = try? await engine.call("appearance", ["dark": !dark], as: OK.self)
                        await engine.refreshSnapshot()
                    }
                } label: {
                    Label(dark ? "Light Appearance" : "Dark Appearance", systemImage: dark ? "sun.max" : "moon")
                }
                Button { session.showSettings = true } label: { Label("Settings", systemImage: "gearshape") }
                Button {
                    Task {
                        if let wn = try? await engine.call("whatsNew", as: WhatsNewData.self) { engine.whatsNew = WhatsNewSheetItem(data: wn) }
                    }
                } label: { Label("What’s New", systemImage: "star") }
                Button { engine.openWeb("/profile", title: "Profile") } label: { Label("Canvas Profile", systemImage: "person.crop.circle") }
            }
            Section {
                Button(role: .destructive) { confirmSignOut = true } label: {
                    Label("Sign Out", systemImage: "rectangle.portrait.and.arrow.right")
                }
            }
        } label: {
            Avatar(person: me, size: size)
        }
        .accessibilityLabel("Account")
        .confirmationDialog("Sign out of Canvas on this device?", isPresented: $confirmSignOut, titleVisibility: .visible) {
            Button("Sign Out", role: .destructive) {
                Haptics.play("warning")
                session.signOut()
            }
        } message: {
            Text("Your Canvas session and any saved sign-in are cleared; settings stay.")
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

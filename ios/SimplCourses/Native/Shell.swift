import SwiftUI
import UIKit

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
    @EnvironmentObject private var session: AppSession
    /// The shell's own frame as SwiftUI measures it — the screen only for the simulator suite, which holds the app to the
    /// Duo's width on an iPad (-SimplWideWidth).
    @State private var frame: CGRect = .zero
    /// (Build 43, the simulator suite only: -SimplFoldCycle YES) The width the app is held to as it is folded and opened
    /// again and again (FoldCycle).
    @State private var cycleWidth: CGFloat?
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
        // (build 48) Sign Out, from the picture's menu wherever it is (AccountMenu)
        .confirmationDialog("Sign out of \(engine.lmsName) on this device?", isPresented: $engine.askSignOut, titleVisibility: .visible) {
            Button("Sign Out", role: .destructive) {
                Haptics.play("warning")
                session.signOut()
            }
        } message: {
            Text("Your \(engine.lmsName) session and any saved sign-in are cleared; settings stay.")
        }
        .task(id: engine.snapshot?.me) { await AccountPicture.shared.load(engine.snapshot?.me) }
        .minimizingTabBar(!engine.holdTabBar && !engine.rail) // (build 54: on the Duo's rail a minimised tab bar went altogether)
        .background(TabBarProbe(engine: engine).allowsHitTesting(false))
        .sensoryFeedback(.selection, trigger: engine.tab)
        .environment(\.wideScreen, wide)
        .environment(\.foldX, wide ? screen.midX : nil)
        .environment(\.twoPane, twoPane)
        .environment(\.screenRect, screen)
        .environment(\.sideRail, engine.rail)
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
        .frame(maxWidth: cycleWidth ?? Self.shotWidth ?? .infinity)
        .task { if UserDefaults.standard.bool(forKey: "SimplFoldCycle") { await foldCycle() } }
    }

    /// (Build 43, the simulator suite only) The iPhone Duo folded and opened again and again on each tab — the app held to
    /// a phone's width, then the Duo's open width — with something opened on the way, each step said on the console and
    /// "fold cycle: done" at the end: an app that does not stay up through it never says so.
    private func foldCycle() async {
        let say = { (s: String) in print("[Simpl Courses] fold cycle: \(s)") }
        try? await Task.sleep(nanoseconds: 4_000_000_000)
        for tab in [AppTab.today, .courses, .todo, .grades] {
            engine.tab = tab
            for round in 1...3 {
                cycleWidth = 390
                say("\(tab.rawValue) folded (\(round))")
                try? await Task.sleep(nanoseconds: 1_500_000_000)
                cycleWidth = 890
                say("\(tab.rawValue) open (\(round))")
                try? await Task.sleep(nanoseconds: 1_500_000_000)
                if round == 1 {
                    // something opened, to carry across the next fold
                    switch tab {
                    case .courses: engine.push(.section(ctx: "courses/101", kind: "assignments"))
                    case .todo: engine.push(.assignment(course: "101", id: "1001"))
                    case .grades: engine.push(.section(ctx: "courses/101", kind: "grades"))
                    default: engine.push(.notifications)
                    }
                    say("\(tab.rawValue) opened \(engine.paths[tab].count) deep")
                    try? await Task.sleep(nanoseconds: 1_200_000_000)
                }
            }
        }
        cycleWidth = nil
        say("done")
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
/// the tab's own companion (SplitHome): Notifications beside Today, the Calendar beside To Do, the first course's grades
/// beside Grades.
/// (Build 41) Courses instead puts a course's home on the left, the courses a strip at its top, and what is opened from
/// it (else its grades) on the right. Compact (a phone, the Duo folded, the Duo open upright — two columns there would each be narrower than any
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

    /// (Build 41) How many of the stack's first screens the left column shows itself: with two columns, Courses puts the
    /// open course's home on the left (the other courses behind a button at its top, CourseSwitcher) and what is opened
    /// from it on the right — the whole list of courses down the left was a poor use of half the screen. Otherwise the
    /// tab's own list is on the left (0).
    private func leadCount(_ path: [Route], two: Bool) -> Int {
        guard two, tab == .courses, case .course? = path.first else { return 0 }
        return 1
    }

    var body: some View {
        let path = engine.paths[tab]
        let two = twoColumns
        let lead = leadCount(path, two: two)
        // the left column to the fold (from the split view's own left edge), never under 320 or over half
        let listWidth: CGFloat = {
            guard frame.width > 0 else { return 380 }
            let toFold = (foldX ?? frame.midX) - frame.minX
            return min(max(320, toFold), frame.width * 0.56).rounded()
        }()
        NavigationSplitView(columnVisibility: $visibility, preferredCompactColumn: $compactColumn) {
            Group {
                if lead == 1, let first = path.first {
                    RouteScreen(route: first)
                        .id(first)
                        .environment(\.courseStrip, true)
                } else {
                    root
                }
            }
            .environment(\.splitList, two)
            .modifier(SolidColumn(on: two))
            // (build 50) the width SwiftUI's own, fixed — nothing of the app's sets it on the split view controller any more
            // (SplitColumnWatch says what it came out at)
            .navigationSplitViewColumnWidth(two ? listWidth : 320)
            .background(SplitColumnWatch(asked: two ? listWidth : 0).allowsHitTesting(false))
            .onGeometryChange(for: CGRect.self) { $0.frame(in: .global) } action: { r in engine.splitEdge[tab] = two ? r.maxX : nil }
        } detail: {
            if two {
                // (build 54) two columns: the right column's own page (its companion) at the bottom of its stack, and
                // what is opened pushed over it — so it has a Back button and the swipe back, to the companion. (Builds
                // 37–53 made what was opened the stack's first screen: nothing under it, no Back, no swipe — an
                // announcement opened beside Today could not be left.)
                NavigationStack(path: rest(from: lead)) {
                    companion(path, lead: lead)
                        .navigationDestination(for: Route.self) { route in RouteScreen(route: route) }
                }
                .id(companionKey(path, lead: lead))
            } else if let first = path.first {
                // one column: the list, then what was opened from it (Back from it closes it — onChange below)
                NavigationStack(path: rest(after: 0)) {
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
        .onChange(of: lead, initial: true) { _, n in engine.splitLead[tab] = n }
        // (build 41) Courses opened with two columns and no course yet: the first course's home on the left
        .task(id: two && path.isEmpty) {
            guard tab == .courses, two else { return }
            try? await Task.sleep(nanoseconds: 400_000_000) // (build 43: once the Duo has finished opening, not during it)
            guard engine.paths[.courses].isEmpty,
                  let d = try? await engine.call("courses", as: CoursesData.self), let c = d.rows.first,
                  twoColumns, engine.paths[.courses].isEmpty else { return }
            engine.paths[.courses] = [.course(id: c.id)]
        }
    }

    /// (Build 54) The right column's own page with nothing opened over it: a course's grades beside its home (Courses), the
    /// tab's companion otherwise (SplitHome).
    @ViewBuilder
    private func companion(_ path: [Route], lead: Int) -> some View {
        if lead == 1, case .course(let id)? = path.first {
            CourseGradesView(courseId: id)
        } else {
            SplitHome(tab: tab)
        }
    }

    /// The right column's stack is a new one when its companion changes (another course's home on the left).
    private func companionKey(_ path: [Route], lead: Int) -> String {
        if lead == 1, case .course(let id)? = path.first { return "course:\(id)" }
        return "home"
    }

    /// (Build 54) What is opened over the right column's companion: the tab's stack past what the left column shows.
    private func rest(from lead: Int) -> Binding<[Route]> {
        Binding(get: { Array(engine.paths[tab].dropFirst(lead)) }, set: { more in
            engine.paths[tab] = Array(engine.paths[tab].prefix(lead)) + more
        })
    }

    /// The right column's stack past its first screen (the left column showing the first `lead` of the tab's stack).
    private func rest(after lead: Int) -> Binding<[Route]> {
        Binding(get: { Array(engine.paths[tab].dropFirst(lead + 1)) }, set: { more in
            let p = engine.paths[tab]
            guard p.count > lead else { return }
            engine.paths[tab] = Array(p.prefix(lead + 1)) + more
        })
    }
}

/// (iPhone 1.8.1, build 52) The other courses, behind a button at the top of a course's home on the left of the split
/// view (TabSplit, ContextHome): a menu of them, each with its colour and its unread announcements, the open one ticked;
/// picked, that course's home takes the left. (Builds 41–51: a strip of capsules across the page, under the title.)
struct CourseSwitcher: View {
    let selected: String
    @EnvironmentObject private var engine: Engine
    @State private var rows: [CourseRow] = []

    var body: some View {
        Menu {
            Picker("Course", selection: Binding(get: { selected }, set: { id in
                Haptics.tap()
                engine.focusCourse(id)
            })) {
                ForEach(rows) { c in
                    Label {
                        Text((c.unread ?? 0) > 0 ? "\(c.code) · \(c.unread ?? 0) new" : c.code)
                    } icon: {
                        Image(uiImage: Self.dot(c.color))
                    }
                    .tag(c.id)
                }
            }
            .pickerStyle(.inline)
        } label: {
            Label("Switch Course", systemImage: "books.vertical")
        }
        .accessibilityLabel("Switch course")
        .task(id: engine.dataVersion) {
            if let d = try? await engine.call("courses", as: CoursesData.self) { rows = d.rows }
        }
    }

    /// A course's colour as a dot for a menu, drawn as it is (a menu draws a symbol in its own colour).
    static func dot(_ hex: String?) -> UIImage {
        let size = CGSize(width: 12, height: 12)
        return UIGraphicsImageRenderer(size: size).image { ctx in
            UIColor(Color(hex: hex)).setFill()
            ctx.cgContext.fillEllipse(in: CGRect(origin: .zero, size: size))
        }.withRenderingMode(.alwaysOriginal)
    }
}

/// (Build 52) The left column of a split view showing two: the page's own background under it, not the sidebar's glass —
/// in dark mode the glass is a light grey, which greyed the column's lighter words (a course's term, its teachers, what
/// is still to do) almost into it.
struct SolidColumn: ViewModifier {
    let on: Bool

    func body(content: Content) -> some View {
        content
            .scrollContentBackground(on ? .hidden : .automatic)
            .background {
                if on { Color(.systemGroupedBackground).ignoresSafeArea() }
            }
    }
}

private struct CourseStripKey: EnvironmentKey { static let defaultValue = false }
private struct SideRailKey: EnvironmentKey { static let defaultValue = false }

extension EnvironmentValues {
    /// (Build 43) A rail down the screen's side (Engine.rail): the bars' buttons go one under another in it.
    var sideRail: Bool {
        get { self[SideRailKey.self] }
        set { self[SideRailKey.self] = newValue }
    }

    /// (Build 41) A course's home on the left of Courses' split view: the button to switch courses at its top
    /// (ContextHome, CourseSwitcher).
    var courseStrip: Bool {
        get { self[CourseStripKey.self] }
        set { self[CourseStripKey.self] = newValue }
    }
}

/// (iPhone 1.8.1, build 37) The right column with nothing opened in it yet: the tab's own companion — Notifications beside
/// Today (build 42), the Calendar beside To Do; the first course's home beside Courses (only until build 41 puts that home on the left: TabSplit),
/// its grades beside Grades (as Mail opens on the first message).
struct SplitHome: View {
    let tab: AppTab
    @EnvironmentObject private var engine: Engine
    @State private var first: CourseRow?
    @State private var loaded = false

    var body: some View {
        switch tab {
        case .today:
            NotificationsView() // (build 42: what is new beside Today, rather than what is coming — Today has that)
        case .todo, .search:
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
    /// (Build 43) The Duo's rail: the buttons each the bar's own item, which iOS stacks down it — the capsule of them
    /// drawn here is one item, as wide as four buttons, and ran off the screen's edge there. (Build 46) The picture is
    /// one of them, in the rail on the Duo open on its side as well as folded: no longer an item of the tab bar's (one
    /// added to it only on a wide screen — build 42 — is the likeliest of what crashed the app there).
    @Environment(\.sideRail) private var rail

    /// Buttons of the screen's own or the shell's in the capsule, the picture aside.
    private var buttons: Bool { menu != nil || showCalendar || add != nil || showBell }
    /// (Build 53) Two columns showing, with the screen on the left: the bell is not shown while Notifications is what the
    /// right column shows (Today's companion), nor the Calendar's button while the Calendar is (To Do's) — they would
    /// open what is already open beside them. Something else opened there, they are back, to return to it.
    @Environment(\.splitList) private var splitList
    private var showBell: Bool { bell && !(splitList && besideIs(.notifications, in: .today)) }
    private var showCalendar: Bool { calendar && !(splitList && besideIs(.calendar, in: .todo)) }
    private func besideIs(_ route: Route, in tab: AppTab) -> Bool {
        let path = engine.paths[tab]
        return path.isEmpty || path.first == route
    }

    /// The capsule's height (the bar's own glass buttons are 44 pt) and the glass left round the picture: 1 pt
    /// (1.5.5) — the picture meets the inside of the glass's rim, so it fills the end without bulging past it
    /// (3 pt read as a gap, none as a picture too big for its pill).
    static let height: CGFloat = 44
    static let ring: CGFloat = 1

    func body(content: Content) -> some View {
        // the buttons and the picture share one glass capsule, drawn here rather than by the bar's own grouping (1.5.1):
        // the bar pads every item it groups, which left the picture small and off the capsule's end. The screen's large
        // title sits on the same row, at the leading edge, rather than under it (inlineLarge).
        // (build 43) one shape whatever the screen, its buttons in or out of it: switching between shapes as the Duo folds
        // or opens made every screen afresh — what was open on it, where it was scrolled — in the middle of the change
        if #available(iOS 26.0, *) {
            content
                .toolbarTitleDisplayMode(.inlineLarge)
                .toolbar {
                    if rail {
                        ToolbarItemGroup(placement: .topBarTrailing) { railItems }
                    }
                    if !rail {
                        ToolbarItem(placement: .topBarTrailing) { capsule }
                            .sharedBackgroundVisibility(.hidden) // (the capsule is its own glass: no second one round it)
                    }
                }
        } else {
            content
                .toolbarTitleDisplayMode(.inlineLarge)
                .toolbar {
                    if rail {
                        ToolbarItemGroup(placement: .topBarTrailing) { railItems }
                    }
                    if !rail {
                        ToolbarItem(placement: .topBarTrailing) { capsule }
                    }
                }
        }
    }

    private var capsule: some View {
        HStack(spacing: 0) {
            if let menu {
                menu.frame(width: 42, height: Self.height).contentShape(Rectangle())
            }
            if showCalendar {
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
            if showBell {
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
        .padding(.leading, buttons ? 6 : 0)
        .buttonStyle(.plain)
        .foregroundStyle(.primary)
        .font(.system(size: 17, weight: .medium))
        .imageScale(.large)
        .modifier(GlassCapsule(interactive: true)) // (it gives under the finger and stretches toward a drag, as the bar's own glass does)
    }

    private func icon(_ image: some View) -> some View {
        image.frame(width: 42, height: Self.height).contentShape(Rectangle())
    }

    /// The same buttons as the capsule's, as the bar's own items (the rail).
    @ViewBuilder
    private var railItems: some View {
        if let menu { menu }
        if showCalendar {
            Button {
                Haptics.tap()
                engine.openCalendar()
            } label: { Image(systemName: "calendar") }
            .accessibilityLabel("Calendar")
        }
        if let add {
            Button(action: add) { Image(systemName: "plus") }
                .accessibilityLabel("Add a task")
        }
        if showBell {
            Button {
                Haptics.tap()
                engine.openNotifications()
            } label: {
                Image(systemName: (engine.snapshot?.notifUnread ?? 0) > 0 ? "bell.badge" : "bell")
                    .symbolRenderingMode(.multicolor)
            }
            .accessibilityLabel("Notifications")
        }
        AccountMenu(plain: true)
    }
}

extension View {
    func shellToolbar(bell: Bool = false, calendar: Bool = false, add: (() -> Void)? = nil, menu: AnyView? = nil) -> some View { modifier(ShellToolbar(bell: bell, calendar: calendar, add: add, menu: menu)) }
}

/// What the web interface kept under the avatar on a phone (Inbox, Groups, Settings, What's New, the
/// Canvas profile, Sign out), as Apple's own menu. (No light/dark switch: the phone's own setting rules, 1.4.)
struct AccountMenu: View {
    var size: CGFloat = 30
    /// (Build 48) A bar button of the system's own, its picture an image (on the Duo's rail, AccountPicture): iOS moves
    /// such an item into the rail, as it does the bell — a view drawn here (Avatar) it leaves in the bar at the top.
    var plain = false
    @EnvironmentObject private var engine: Engine
    @EnvironmentObject private var session: AppSession
    @ObservedObject private var picture = AccountPicture.shared

    var body: some View {
        if plain {
            Menu { items } label: {
                Image(uiImage: picture.image).renderingMode(.original)
            }
            .accessibilityLabel("Account")
        } else {
            Menu { items } label: {
                Avatar(person: engine.snapshot?.me, size: size)
                    .contentShape(Circle())
            }
            // the picture alone, edge to edge: no bar-button padding round it (it sat 10 pt in from the capsule's end)
            .menuStyle(.button)
            .buttonStyle(.plain)
            .menuIndicator(.hidden)
            .fixedSize()
            .accessibilityLabel("Account")
        }
    }

    @ViewBuilder
    private var items: some View {
        let me = engine.snapshot?.me
        let inbox = engine.snapshot?.inboxUnread ?? 0
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
            // (build 48: asked from the shell, NativeShell — a dialog on the menu itself made it a view of the app's own,
            // which the rail does not take)
            Button(role: .destructive) { engine.askSignOut = true } label: {
                Label("Sign Out", systemImage: "rectangle.portrait.and.arrow.right")
            }
        }
    }
}

/// (iPhone 1.8.1, build 48) The student's picture as an image for a bar button of the system's own (AccountMenu on the
/// Duo's rail): round, 30 points, drawn as it is rather than tinted. Their initials on the tint at once, their photo once
/// it has come.
final class AccountPicture: ObservableObject {
    static let shared = AccountPicture()
    static let side: CGFloat = 30
    @Published private(set) var image: UIImage = UIImage(systemName: "person.crop.circle.fill")?
        .withTintColor(.secondaryLabel, renderingMode: .alwaysOriginal) ?? UIImage()
    private var made: Person?

    @MainActor
    func load(_ person: Person?) async {
        guard let person, person != made else { return }
        made = person
        let renderer = ImageRenderer(content: Avatar(person: Person(name: person.name, initials: person.initials), size: Self.side))
        renderer.scale = 3
        if let initials = renderer.uiImage { image = initials.withRenderingMode(.alwaysOriginal) }
        guard let s = person.avatar, let url = URL(string: s), url.host != nil,
              let fetched = try? await URLSession.shared.data(from: url),
              ((fetched.1 as? HTTPURLResponse)?.statusCode ?? 200) < 400, let photo = UIImage(data: fetched.0) else { return }
        image = Self.round(photo)
    }

    private static func round(_ photo: UIImage) -> UIImage {
        let size = CGSize(width: side, height: side)
        let format = UIGraphicsImageRendererFormat()
        format.scale = 3
        let image = UIGraphicsImageRenderer(size: size, format: format).image { _ in
            UIBezierPath(ovalIn: CGRect(origin: .zero, size: size)).addClip()
            // (filled, as the picture is everywhere else: the shorter side across the circle)
            let k = max(size.width / max(photo.size.width, 1), size.height / max(photo.size.height, 1))
            let w = photo.size.width * k, h = photo.size.height * k
            photo.draw(in: CGRect(x: (size.width - w) / 2, y: (size.height - h) / 2, width: w, height: h))
        }
        return image.withRenderingMode(.alwaysOriginal)
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

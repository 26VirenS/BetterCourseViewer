import AppKit
import SwiftUI

/// The menu bar: File ▸ New Task and New Message; View ▸ Reload (Canvas asked afresh) with the sidebar's and the
/// toolbar's own items; a Go menu as a browser's (Back, Forward, each place in the sidebar by number, each course);
/// Simpl ▸ Change School; Edit ▸ Search Commands (⌘K: the search field on "/"); Help ▸ Take the Tour, What's New and
/// Canvas in the browser; (1.3.69) File ▸ New Window. Each acts on the focused window's engine.
struct SimplCommands: Commands {
    @ObservedObject var session: AppSession
    @FocusedObject private var focused: Engine?
    @ObservedObject private var model = AppModel.shared
    @Environment(\.openWindow) private var openWindow
    @ObservedObject private var dev = DevMode.shared

    /// The main window's engine: the focused one, else the one the app keeps (a menu used from Settings or a tool's window).
    private var engine: Engine? { focused ?? model.engine }
    private var ready: Bool { engine?.phase == .native }

    var body: some Commands {
        SidebarCommands()
        ToolbarCommands()

        CommandGroup(after: .appInfo) {
            CheckForUpdatesButton()
        }

        CommandGroup(after: .appSettings) {
            Button("Change School…") { session.changeSchool() }
                .disabled(session.host == nil)
            // (1.3.72) only once developer mode is on (its secret entrances: Developer.swift)
            if dev.unlocked {
                Divider()
                DeveloperMenu(dev: dev, engine: engine)
            }
        }

        CommandGroup(replacing: .newItem) {
            // (1.3.69) another window of Simpl, on the place this one shows (⌘N stays New Task, as it has been)
            Button("New Window") { openWindow(id: "window", value: Engine.name(of: engine?.nav.current.place ?? .dashboard)) }
                .keyboardShortcut("n", modifiers: [.command, .option])
                .disabled(!ready)
            Divider()
            Button("New Task") { engine?.newTask() } // (1.3.170: To Do & Calendar, its composer open)
                .keyboardShortcut("n")
                .disabled(!ready)
            Button("New Message") { engine?.newMessage = true }
                .keyboardShortcut("n", modifiers: [.command, .shift])
                .disabled(!ready)
        }

        CommandGroup(before: .sidebar) {
            Button("Reload") { engine?.refresh() }
                .keyboardShortcut("r")
                .disabled(!ready)
            Button("Reload \(session.lmsName) Page") { engine?.reload() }
                .keyboardShortcut("r", modifiers: [.command, .option])
                .disabled(engine == nil)
            Divider()
        }

        CommandMenu("Go") {
            GoBackForward(whereabouts: engine?.whereabouts ?? Whereabouts.nowhere, engine: engine)
            Divider()
            place("Dashboard", .dashboard, key: "1")
            place("To Do & Calendar", .calendar, key: "2") // (1.3.170: one place)
            place("Grades", .grades, key: "4")
            place("Notifications", .notifications, key: "5")
            if !(engine?.onBrightspace ?? false) { place("Inbox", .inbox, key: "6") }
            place("Tools", .tools, key: "7") // (1.2)
            if !(engine?.onBrightspace ?? false) { place("External Tools", .externalTools, key: nil) } // (1.3.67)
            Divider()
            Menu("Courses") {
                ForEach(engine?.courses ?? []) { c in
                    Button(c.code) { engine?.go(.home("courses/\(c.id)")) }
                }
                if !(engine?.courses.isEmpty ?? true) { Divider() }
                Button("All Courses") { engine?.go(.courses) }
            }
            .disabled(!ready)
            if !(engine?.onBrightspace ?? false) { place("Groups", .groups, key: nil) }
        }

        CommandGroup(replacing: .help) {
            Button("Take the Tour") { MacTour.shared.start() } // (1.2, Shell/Tour.swift)
                .disabled(!ready)
            Button("Report a Bug…") { ReportBug.open() } // (1.2.1)
            Divider()
            Button("What’s New in Simpl for Mac") {
                engine?.whatsNew = WhatsNewSheetItem(data: MacWhatsNew.all) // (1.3.3: the Mac app's own notes)
            }
            .disabled(!ready)
            Button("Open \(session.lmsName) in Browser") {
                if let u = engine?.web.baseURL { NSWorkspace.shared.open(u) }
            }
            .disabled(engine == nil)
        }

        // (1.2) the search field from the keyboard, on "/": every command listed (Shell/SearchCommands.swift)
        CommandGroup(after: .textEditing) {
            Button("Search Commands…") {
                if let engine { SearchPalette.summon(engine) }
            }
            .keyboardShortcut("k")
            .disabled(!ready)
        }
    }

    @ViewBuilder
    private func place(_ title: String, _ place: Place, key: KeyEquivalent?) -> some View {
        if let key {
            Button(title) { engine?.go(place) }
                .keyboardShortcut(key)
                .disabled(!ready)
        } else {
            Button(title) { engine?.go(place) }
                .disabled(!ready)
        }
    }
}

/// Simpl ▸ Check for Updates… (1.2): the update feed read now. A newer version, a check that failed, or an update
/// already on its way opens Settings on Updates (Update Now is there); up to date, a short alert says so. Off in a
/// development run, which never updates.
private struct CheckForUpdatesButton: View {
    @Environment(\.openSettings) private var openSettings
    @ObservedObject private var updater = Updater.shared

    var body: some View {
        Button("Check for Updates…") {
            Task { await check() }
        }
        .disabled(Updater.isDevelopmentRun || updater.state == .checking)
    }

    @MainActor
    private func check() async {
        let found = await updater.checkNow()
        switch found {
        case .idle, .checking:
            break
        case .upToDate:
            let alert = NSAlert()
            alert.messageText = "Simpl is up to date"
            alert.informativeText = "Version \(updater.currentVersion) is the newest." + (updater.heldBack.map { " " + $0 } ?? "")
            alert.runModal()
        default:
            updater.revealPane = true
            openSettings()
        }
    }
}

/// (1.3.78) The Go menu's Back and Forward, enabled as the window in front can go back or forward — its whereabouts
/// watched on their own (Engine/Navigator.swift), as the engine no longer says when the window moves.
private struct GoBackForward: View {
    @ObservedObject var whereabouts: Whereabouts
    let engine: Engine?

    var body: some View {
        Button("Back") { engine?.goBack() }
            .keyboardShortcut("[")
            .disabled(engine == nil || !whereabouts.nav.canGoBack)
        Button("Forward") { engine?.goForward() }
            .keyboardShortcut("]")
            .disabled(engine == nil || !whereabouts.nav.canGoForward)
    }
}

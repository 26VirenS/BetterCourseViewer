import AppKit
import SwiftUI

/// The menu bar: File ▸ New Task and New Message; View ▸ Reload (Canvas asked afresh) with the sidebar's and the
/// toolbar's own items; a Go menu as a browser's (Back, Forward, each place in the sidebar by number, each course);
/// Simpl ▸ Change School; Help ▸ What's New and Canvas in the browser. Each acts on the main window's engine.
struct SimplCommands: Commands {
    @ObservedObject var session: AppSession
    @FocusedObject private var focused: Engine?
    @ObservedObject private var model = AppModel.shared

    /// The main window's engine: the focused one, else the one the app keeps (a menu used from Settings or a tool's window).
    private var engine: Engine? { focused ?? model.engine }
    private var ready: Bool { engine?.phase == .native }

    var body: some Commands {
        SidebarCommands()
        ToolbarCommands()

        CommandGroup(after: .appSettings) {
            Button("Change School…") { session.changeSchool() }
                .disabled(session.host == nil)
        }

        CommandGroup(replacing: .newItem) {
            Button("New Task") { engine?.newTask = true }
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
            Button("Reload Canvas Page") { engine?.reload() }
                .keyboardShortcut("r", modifiers: [.command, .option])
                .disabled(engine == nil)
            Divider()
        }

        CommandMenu("Go") {
            Button("Back") { engine?.goBack() }
                .keyboardShortcut("[")
                .disabled(!(engine?.nav.canGoBack ?? false))
            Button("Forward") { engine?.goForward() }
                .keyboardShortcut("]")
                .disabled(!(engine?.nav.canGoForward ?? false))
            Divider()
            place("Dashboard", .dashboard, key: "1")
            place("To Do", .todo, key: "2")
            place("Calendar", .calendar, key: "3")
            place("Grades", .grades, key: "4")
            place("Notifications", .notifications, key: "5")
            place("Inbox", .inbox, key: "6")
            Divider()
            Menu("Courses") {
                ForEach(engine?.courses ?? []) { c in
                    Button(c.code) { engine?.go(.home("courses/\(c.id)")) }
                }
                if !(engine?.courses.isEmpty ?? true) { Divider() }
                Button("All Courses") { engine?.go(.courses) }
            }
            .disabled(!ready)
            place("Groups", .groups, key: nil)
        }

        CommandGroup(replacing: .help) {
            Button("What’s New in Simpl") {
                guard let engine else { return }
                Task {
                    if let wn = try? await engine.call("whatsNew", as: WhatsNewData.self) { engine.whatsNew = WhatsNewSheetItem(data: wn) }
                }
            }
            .disabled(!ready)
            Button("Open Canvas in Browser") {
                if let u = engine?.web.baseURL { NSWorkspace.shared.open(u) }
            }
            .disabled(engine == nil)
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

import AppKit
import SwiftUI
import UserNotifications

/// Simpl for Mac: a Mac app of its own, not a Safari extension. As in the iPhone app, the student's Canvas runs in a web
/// view underneath (the extension's own scripts in it, signed in on the school's real sign-in page) and answers what
/// the app shows; the window over it is the Mac's own — a sidebar, a toolbar with Back, Forward and Search, menus,
/// sheets, popovers, Quick Look, a Settings window and a window for each external tool.
@main
struct SimplApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var session = AppSession()
    @StateObject private var model = AppModel.shared

    var body: some Scene {
        Window("Simpl", id: "main") {
            ContentView()
                .environmentObject(session)
                .environmentObject(model)
                .frame(minWidth: 960, minHeight: 640)
        }
        .defaultSize(width: 1360, height: 880)
        .windowToolbarStyle(.unified)
        .commands { SimplCommands(session: session) }

        // an external tool, or a page of Canvas's own the app does not draw: a window of its own each
        WindowGroup("Canvas", id: "tool", for: ToolLaunch.self) { $launch in
            ToolWindow(launch: launch)
                .environmentObject(model)
        }
        .defaultSize(width: 1120, height: 800)

        Settings {
            SettingsView()
                .environmentObject(session)
                .environmentObject(model)
        }
    }
}

/// What the app's windows share: the engine of the school signed in to (the main window makes it; Settings, the tools'
/// windows and the menu bar's commands read it here).
@MainActor
final class AppModel: ObservableObject {
    static let shared = AppModel()
    @Published var engine: Engine?
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        UNUserNotificationCenter.current().delegate = Reminders.shared // (a reminder pressed opens its work)
        // (the screenshot suite: -SimplAppearance dark draws the app dark whatever the Mac is set to)
        if let look = UserDefaults.standard.string(forKey: "SimplAppearance") {
            NSApp.appearance = NSAppearance(named: look == "dark" ? .darkAqua : .aqua)
        }
        Shot.armIfAsked()
    }

    /// The window closed: the app stays (its reminders and new-activity alerts keep coming); the Dock icon brings the window back.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}

struct ContentView: View {
    @EnvironmentObject private var session: AppSession

    var body: some View {
        Group {
            if let host = session.host {
                RootView(host: host)
                    .id(host) // a different school is a different engine
            } else {
                SchoolPicker()
            }
        }
    }
}

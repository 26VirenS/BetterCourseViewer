import AppKit
import SwiftUI
import UserNotifications

/// Simpl for Mac: a Mac app of its own, not a Safari extension. As in the iPhone app, the student's Canvas runs in a web
/// view underneath (the extension's own scripts in it, signed in on the school's real sign-in page) and answers what
/// the app shows; the window over it is the Mac's own — a sidebar, a toolbar with Back, Forward and Search, menus,
/// sheets, popovers, Quick Look, a Settings window, a window for each external tool and (1.3.69) as many windows of
/// Simpl itself as the student opens.
@main
struct SimplApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var session = AppSession()
    @StateObject private var model = AppModel.shared

    init() {
        Vault.prepare() // (1.3.76: what Simpl keeps is sealed; its vault opened off the main thread before the first read)
    }

    var body: some Scene {
        Window("Simpl", id: "main") {
            ContentView()
                .environmentObject(session)
                .environmentObject(model)
                .frame(minWidth: 720, minHeight: 520) // (1.3: down to a half-screen window; the screens fold to fit)
        }
        .defaultSize(width: 1360, height: 880)
        .windowResizability(.contentMinSize)
        .windowToolbarStyle(.unified)
        .commands { SimplCommands(session: session) }
        .handlesExternalEvents(matching: ["*"]) // (a simpl:// link comes to the main window)

        // (1.3.69) a window more of Simpl, on a place of its own (File ▸ New Window, a row's Open in New Window)
        WindowGroup("Simpl", id: "window", for: String.self) { $place in
            MoreWindow(place: place)
                .environmentObject(session)
                .environmentObject(model)
        }
        .defaultSize(width: 1240, height: 840)
        .windowResizability(.contentMinSize)
        .windowToolbarStyle(.unified)
        .handlesExternalEvents(matching: []) // (a simpl:// link goes to the main window, never a new one)

        // an external tool, or a page of Canvas's own the app does not draw: a window of its own each
        WindowGroup("Canvas", id: "tool", for: ToolLaunch.self) { $launch in
            ToolWindow(launch: launch)
                .environmentObject(model)
        }
        .defaultSize(width: 1120, height: 800)
        .handlesExternalEvents(matching: []) // (never an empty tool window for a link from outside)

        // (1.3.72) the Developer window: no item of its own in the Window menu (developer mode opens it)
        Window("Developer", id: "developer") {
            DeveloperView()
                .environmentObject(session)
                .environmentObject(model)
        }
        .defaultSize(width: 820, height: 640)
        .commandsRemoved()
        .handlesExternalEvents(matching: [])

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
        MoveToApplications.offerIfNeeded() // (1.3.13: opened from Downloads or a disk image, an offer to move to Applications)
        DevMode.shared.watchKeys() // (1.3.72: Control-Option-Shift-Command-D, developer mode in or out)
        Updater.shared.start() // (1.2: the update feed at launch and every hour (1.2.19); nothing in a development run)
    }

    /// The window closed: the app stays (its reminders and new-activity alerts keep coming); the Dock icon brings the window back.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}

struct ContentView: View {
    @EnvironmentObject private var session: AppSession
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        // (1.3.76) the picker and the school's window crossfade, on the engine's own curve for its splash and the app
        // (Engine.setPhase), so Yes, Sign In and Change School read as one fade with what comes after
        ZStack {
            if let host = session.host {
                RootView(host: host)
                    .id(host) // a different school is a different engine
                    .transition(.opacity)
            } else {
                SchoolPicker()
                    .transition(.opacity)
            }
        }
        .animation(.easeInOut(duration: reduceMotion ? 0.2 : 0.35), value: session.host)
        .onOpenURL { url in ContentView.open(url) }
    }

    /// A simpl:// link (simpl://courses/101/assignments/1001): that Canvas address, opened where it leads — its screen,
    /// a quiz, a file, a tool.
    @MainActor
    static func open(_ url: URL) {
        guard url.scheme?.lowercased() == "simpl", let engine = AppModel.shared.engine else { return }
        var path = "/" + (url.host ?? "") + url.path
        if let q = url.query, !q.isEmpty { path += "?" + q }
        engine.openWeb(path, title: "")
    }
}

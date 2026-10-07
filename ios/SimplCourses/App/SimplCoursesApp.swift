import SwiftUI
import UserNotifications

/// Simpl Courses for iOS: the student's own Canvas site in a full-screen web view, with the
/// extension injected into it. Signing in happens on Canvas's real login page inside the app.
@main
struct SimplCoursesApp: App {
    @StateObject private var session = AppSession()

    init() {
        UNUserNotificationCenter.current().delegate = Reminders.shared // (a reminder pressed, even from a cold launch, opens its work)
    }

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(session)
        }
        // iOS woke the app to look for new activity (Background App Refresh: it picks the moment)
        .backgroundTask(.appRefresh(Activity.taskID)) {
            await Activity.shared.backgroundCheck()
        }
    }
}

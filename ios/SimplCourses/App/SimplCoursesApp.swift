import SwiftUI

/// Simpl Courses for iOS: the student's own Canvas site in a full-screen web view, with the
/// extension injected into it. Signing in happens on Canvas's real login page inside the app.
@main
struct SimplCoursesApp: App {
    @StateObject private var session = AppSession()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(session)
        }
    }
}

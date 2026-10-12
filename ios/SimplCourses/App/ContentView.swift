import SwiftUI

struct ContentView: View {
    @EnvironmentObject private var session: AppSession
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Group {
            if let host = session.host {
                RootView(host: host)
                    .id(host) // a different school is a different engine
                    .transition(.opacity)
            } else {
                SchoolPickerView()
                    .transition(.opacity)
            }
        }
        // (1.7.5) a school picked or changed crossfades, as the splash does into the app (Engine.setPhase's 0.3s)
        .animation(reduceMotion ? .easeOut(duration: 0.2) : .easeInOut(duration: 0.3), value: session.host)
    }
}

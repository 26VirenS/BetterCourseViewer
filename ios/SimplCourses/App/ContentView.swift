import SwiftUI
import UIKit

struct ContentView: View {
    @EnvironmentObject private var session: AppSession
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// (Build 46) What made the app close unexpectedly last time (CrashCatcher), offered a moment after launch.
    @ObservedObject private var crash = CrashCatcher.shared
    @State private var crashAsk = false

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
        .task(id: crash.report) {
            guard crash.report != nil else { return }
            try? await Task.sleep(nanoseconds: 2_500_000_000) // (once the app is up)
            crashAsk = crash.report != nil
        }
        .alert("Simpl Courses closed unexpectedly", isPresented: $crashAsk) {
            Button("Copy Details") {
                UIPasteboard.general.string = crash.report
                crash.clear()
            }
            Button("Not Now", role: .cancel) { crash.clear() }
        } message: {
            Text("The details say what went wrong. Copy them and paste them to whoever is helping you fix it.")
        }
    }
}

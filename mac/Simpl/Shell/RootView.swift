import QuickLook
import SwiftUI

/// The main window for a school: the Canvas web view at the bottom (the engine; while signing in, what the window
/// shows), the app's own window over it once the page says it is ready, and the sign-in's own form over the page.
struct RootView: View {
    let host: String
    @EnvironmentObject private var session: AppSession
    @StateObject private var engine: Engine
    @ObservedObject private var reminders = Reminders.shared
    @ObservedObject private var files = FilePreview.shared
    @Environment(\.openWindow) private var openWindow
    @Environment(\.openSettings) private var openSettings

    init(host: String) {
        self.host = host
        _engine = StateObject(wrappedValue: Engine(host: host))
    }

    var body: some View {
        ZStack {
            EngineHost(engine: engine)
                .allowsHitTesting(engine.phase != .native)
            if engine.phase == .native {
                MainShell()
                    .transition(.opacity)
            }
            if engine.phase == .starting {
                Splash()
                    .transition(.opacity)
            }
        }
        .overlay { LoginLayer(assist: engine.web.login, pageShown: engine.phase != .native) }
        .overlay(alignment: .bottom) { FileToast(files: files) }
        .environmentObject(engine)
        .focusedSceneObject(engine)
        .sheet(item: $engine.whatsNew) { item in
            WhatsNewSheet(data: item.data) {
                Task { _ = try? await engine.call("whatsNewSeen", ["version": item.data.version ?? ""], as: OK.self) }
            }
        }
        .sheet(item: $engine.quiz) { q in
            QuizScreen(launch: q)
                .environmentObject(engine)
        }
        .sheet(isPresented: $engine.setup) {
            SetupScreen()
                .environmentObject(engine)
        }
        .sheet(isPresented: $engine.newTask) {
            NewTaskSheet()
                .environmentObject(engine)
        }
        .sheet(isPresented: $engine.newMessage) {
            ComposeSheet()
                .environmentObject(engine)
        }
        .onChange(of: engine.tool) { _, t in
            guard let t else { return }
            openWindow(id: "tool", value: t)
            engine.tool = nil
        }
        .onChange(of: session.showSettings) { _, show in
            guard show else { return }
            session.showSettings = false
            openSettings()
        }
        .onReceive(NotificationCenter.default.publisher(for: .simplOpenSetup)) { _ in engine.setup = true }
        .onReceive(NotificationCenter.default.publisher(for: .simplSignedOut)) { _ in
            engine.signedOut() // (back to the sign-in page, shown; nothing of this account's for the next)
        }
        .onReceive(NotificationCenter.default.publisher(for: .simplInterfaceToggled)) { _ in engine.reload() }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            guard engine.phase == .native else { return }
            Task { await Reminders.shared.reschedule(engine) }
            Task { await Activity.shared.sync(engine) }
        }
        .onChange(of: reminders.opening) { _, _ in openReminder() }
        .onChange(of: engine.phase) { _, _ in openReminder() }
        .onAppear {
            AppModel.shared.engine = engine
            engine.start()
            WideWindow.openWideOnce(engine.hostView) // (1.2.1: the first opening, wide)
        }
    }

    private func openReminder() {
        guard engine.phase == .native, let url = reminders.opening else { return }
        reminders.opening = nil
        engine.openWeb(url, title: "")
    }
}

/// While the first page loads: the app's mark and its name, not a blank page.
struct Splash: View {
    @State private var shown = false

    var body: some View {
        ZStack {
            Theme.page.ignoresSafeArea()
            VStack(spacing: 16) {
                Image(nsImage: NSApp.applicationIconImage)
                    .resizable()
                    .frame(width: 96, height: 96)
                    .scaleEffect(shown ? 1 : 0.92)
                    .opacity(shown ? 1 : 0)
                Text("Simpl")
                    .font(.system(size: 26, weight: .bold))
                    .opacity(shown ? 1 : 0)
                ProgressView()
                    .controlSize(.small)
                    .opacity(shown ? 1 : 0)
            }
        }
        .onAppear { withAnimation(Motion.gentle) { shown = true } }
    }
}

/// A file on its way to Quick Look (how far it has come, and × to stop it), a copy just kept in Downloads, or why one
/// could not be opened: a small glass capsule at the window's foot (FileToastContent, FileViews.swift).
struct FileToast: View {
    @ObservedObject var files: FilePreview

    var body: some View {
        FileToastContent(files: files)
    }
}

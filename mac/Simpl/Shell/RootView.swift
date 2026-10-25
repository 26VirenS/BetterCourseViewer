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
    @ObservedObject private var appearance = AppearanceStore.shared
    @Environment(\.openSettings) private var openSettings
    @Environment(\.openWindow) private var openWindow

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
        .tint(appearance.accentColor) // (1.3) one accent throughout: the theme's, or the Mac's own
        .overlay(alignment: .bottom) { FileToast(files: files) }
        .environmentObject(engine)
        .environmentObject(engine.whereabouts) // (1.3.78: where the window is, observed on its own)
        .environment(\.engineLink, EngineLink(engine: engine)) // (1.3.78: and the engine to act through, unobserved)
        .focusedSceneObject(engine)
        .modifier(EngineSheets(engine: engine))
        .onChange(of: session.showSettings) { _, show in
            guard show else { return }
            session.showSettings = false
            openSettings()
        }
        .onReceive(NotificationCenter.default.publisher(for: .simplOpenSetup)) { _ in engine.setup = true }
        .onReceive(DevMode.shared.$openAsked.dropFirst()) { _ in openWindow(id: "developer") } // (1.3.72)
        .overlay(alignment: .top) { CriticalUpdateNotice() } // (1.3.72: a Critical Update installing)
        .onReceive(NotificationCenter.default.publisher(for: .simplOpenWindow)) { n in
            openWindow(id: "window", value: n.object as? String ?? "dashboard")
        }
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

/// A window's popups and sheets — What's New, a quiz, an Alta assignment, the setup, a new task or
/// message — and the tool windows it opens: the main window's and (1.3.69) each window more's, over its own window.
/// (1.3.79) Only a quiz being taken and the setup are sheets, blocking the window; the rest are popups over it that
/// never do (Shell/FloatingPopup.swift).
struct EngineSheets: ViewModifier {
    @ObservedObject var engine: Engine
    @Environment(\.openWindow) private var openWindow

    func body(content: Content) -> some View {
        content
            .floatingPopup("whatsnew", title: "What’s New", item: $engine.whatsNew, engine: engine) { item in
                WhatsNewSheet(data: item.data) { MacWhatsNew.markSeen() }
            }
            .sheet(item: $engine.quiz) { q in
                QuizScreen(launch: q)
                    .environmentObject(engine)
                    .modifier(UpdateHold()) // (1.2.19: nor while a quiz is open)
            }
            .floatingPopup("alta", title: "Knewton Alta", item: $engine.alta, layout: .large, engine: engine) { a in
                AltaQuizScreen(launch: a, engine: engine)
                    .modifier(UpdateHold()) // (nor while an Alta assignment is open)
            }
            .sheet(isPresented: $engine.setup) {
                SetupScreen()
                    .environmentObject(engine)
            }
            .floatingPopup("newtask", title: "New Task", isPresented: $engine.newTask, engine: engine) {
                NewTaskSheet()
            }
            .floatingPopup("compose", title: "New Message", isPresented: $engine.newMessage, engine: engine) {
                ComposeSheet()
            }
            .onChange(of: engine.tool) { _, t in
                guard let t else { return }
                openWindow(id: "tool", value: t)
                engine.tool = nil
            }
    }
}

/// While the first page loads: the app's mark and its name, not a blank page.
struct Splash: View {
    @State private var shown = false

    var body: some View {
        ZStack {
            PageGround().ignoresSafeArea()
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

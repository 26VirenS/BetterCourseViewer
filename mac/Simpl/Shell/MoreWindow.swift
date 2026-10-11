import SwiftUI

/// (1.3.69) A window more of Simpl (File ▸ New Window, ⌥⌘N; a sidebar row's Open in New Window): the sidebar and the
/// screens as the main window has them, on a place of its own — its own Back and Forward, search and sheets — with the
/// main window's school, page and answers underneath (`Engine(sharing:)`): a tick or a hand-in in one shows in all.
struct MoreWindow: View {
    let place: String?
    @EnvironmentObject private var model: AppModel
    @Environment(\.openWindow) private var openWindow
    @State private var engine: Engine?

    var body: some View {
        Group {
            if let engine {
                MoreWindowBody(engine: engine)
                    .id(ObjectIdentifier(engine)) // (another school, or the main window opened afresh: a new engine)
            } else {
                Splash()
            }
        }
        .frame(minWidth: 720, minHeight: 520)
        .onAppear {
            bind(model.engine)
            // (brought back at launch without the main window: the main window opens, and this one follows it)
            if model.engine == nil { openWindow(id: "main") }
        }
        .onReceive(model.$engine) { bind($0) }
    }

    /// Following the main window's engine; a new one (the main window closed and opened again, another school) is
    /// followed afresh, where this window was kept.
    private func bind(_ main: Engine?) {
        guard let main, engine?.primary !== main else { return }
        let at = engine?.nav.current.place ?? place.flatMap(Engine.place(named:))
        engine = Engine(sharing: main, at: at)
    }
}

private struct MoreWindowBody: View {
    @ObservedObject var engine: Engine
    @ObservedObject private var files = FilePreview.shared
    @ObservedObject private var appearance = AppearanceStore.shared
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        ZStack {
            EngineHost(engine: engine) // (its own bottom layer, empty: the page stays in the main window)
            switch engine.phase {
            case .native:
                MainShell()
                    .transition(.opacity)
            case .starting:
                Splash()
                    .transition(.opacity)
            case .web:
                // (signing in happens in the main window, where the school's page is)
                ZStack {
                    PageGround().ignoresSafeArea()
                    ContentUnavailableView {
                        Label("Sign in to continue", systemImage: "person.crop.circle")
                    } description: {
                        Text("Sign in to \(engine.lmsName) in the main Simpl window; this one shows Simpl once you have.")
                    } actions: {
                        Button("Show Main Window") { openWindow(id: "main") }
                            .glassButton()
                    }
                }
                .transition(.opacity)
            }
        }
        .tint(appearance.accentColor)
        .overlay(alignment: .bottom) { FileToast(files: files) }
        .environmentObject(engine)
        .focusedSceneObject(engine)
        .modifier(EngineSheets(engine: engine))
    }
}

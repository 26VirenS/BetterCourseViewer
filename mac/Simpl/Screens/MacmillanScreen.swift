import AppKit
import SwiftUI
import WebKit

/// (1.3.66) A Macmillan Learning assignment in a popup over the window, as an Alta one opens: Close and the assignment's
/// name across the top, Save Page Code at the right; below, Macmillan's page as it draws it, the student working on it
/// there. (The page laid out as the app's own comes next, from its saved code, as Alta's was.)
struct MacmillanScreen: View {
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @StateObject private var session: MacmillanSession
    @State private var ideal = MacmillanScreen.idealSize()
    @State private var saving = false
    @State private var saved: URL?

    init(launch: AltaLaunch) {
        _session = StateObject(wrappedValue: MacmillanSession(launch: launch))
    }

    /// Most of the window it opens over, as the quiz sheet takes.
    private static func idealSize() -> CGSize {
        let windows = NSApp.windows.filter { $0.isVisible && $0.sheetParent == nil && !($0 is NSPanel) }
        guard let w = NSApp.mainWindow ?? windows.max(by: { $0.frame.width < $1.frame.width }), w.sheetParent == nil else {
            return CGSize(width: 1040, height: 700)
        }
        let room = w.contentLayoutRect.size
        return CGSize(width: min(max(room.width - 64, 760), 1400), height: min(max(room.height - 36, 520), 960))
    }

    var body: some View {
        VStack(spacing: 0) {
            topBar
            page.frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(width: ideal.width, height: ideal.height)
        .background(PageGround())
        .tint(Theme.accent)
        .task { await session.start(engine: engine) }
        .alert("Page code saved", isPresented: Binding(get: { saved != nil }, set: { if !$0 { saved = nil } })) {
            Button("Show in Finder") { if let saved { NSWorkspace.shared.activateFileViewerSelecting([saved]) } }
            Button("OK", role: .cancel) {}
        } message: {
            Text("Every bit of the page's code — the page, each frame in it, and the scripts and style sheets they load — is in Downloads as “\(saved?.lastPathComponent ?? "")”.")
        }
    }

    private var topBar: some View {
        HStack(spacing: 14) {
            GlassGroup(spacing: 8) {
                Button("Close") { dismiss() }
                    .glassButton()
                    .keyboardShortcut(.cancelAction)
                    .help("Close — your work is kept by Macmillan")
            }
            IconTile(symbol: "books.vertical", color: .red, size: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(session.launch.title).font(.sTitle3).lineLimit(1)
                Text("Macmillan Learning").font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer(minLength: 8)
            // (1.3.72: Save Page Code is in the Developer menu now)
        }
        .controlSize(.large)
        .padding(.horizontal, 22)
        .padding(.top, 16)
        .padding(.bottom, 12)
    }

    private func saveCode() {
        guard !saving else { return }
        saving = true
        Task {
            let file = await session.savePageCode()
            saving = false
            if let file { saved = file } else { NSSound.beep() }
        }
    }

    private var page: some View {
        ZStack {
            MacmillanPageHost(web: session.web)
                .background(Color.white)
            if let why = session.failure {
                ZStack {
                    PageGround()
                    ContentUnavailableView("Macmillan Learning could not be opened", systemImage: "exclamationmark.triangle", description: Text(why))
                }
            } else if session.opening {
                ZStack {
                    PageGround()
                    ProgressView("Opening Macmillan Learning…").font(.sBody)
                }
                .transition(.opacity)
            }
        }
    }
}

/// The page in the popup: the session's own web view.
private struct MacmillanPageHost: NSViewRepresentable {
    let web: WKWebView
    func makeNSView(context: Context) -> WKWebView { web }
    func updateNSView(_ v: WKWebView, context: Context) {}
}

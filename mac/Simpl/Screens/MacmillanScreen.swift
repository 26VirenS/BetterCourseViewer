import AppKit
import SwiftUI
import WebKit

/// (1.3.66) A Macmillan Learning assignment in a popup over the window, as an Alta one opens: the assignment's name and
/// when it is due across the top, Close at its right end (1.3.78); below, Macmillan's page as it draws it, the student
/// working on it there. (The page laid out as the app's own comes next, from its saved code, as Alta's was.)
struct MacmillanScreen: View {
    @EnvironmentObject private var engine: Engine
    /// (1.3.79) Its popup over the window (never blocking it: Shell/FloatingPopup.swift), which Close closes.
    @Environment(\.popup) private var dismiss
    @StateObject private var session: MacmillanSession
    /// (1.3.79) The popup's size, as the window gives it — sized again with the window.
    @Environment(\.popupSize) private var popupSize
    @State private var saving = false
    @State private var saved: URL?
    @ObservedObject private var dev = DevMode.shared // (1.3.109: Save Page Code in the header, to developers)
    /// (1.3.78) The Canvas assignment's name and due line, for the header as Alta's has it.
    @State private var canvas: CanvasDue.Facts?

    init(launch: AltaLaunch) {
        _session = StateObject(wrappedValue: MacmillanSession(launch: launch))
    }

    /// Most of the window it is over: the large popup's size.
    private var ideal: CGSize { popupSize ?? CGSize(width: 1040, height: 700) }

    var body: some View {
        VStack(spacing: 0) {
            topBar
            page.frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(width: ideal.width, height: ideal.height)
        .background(PageGround())
        .tint(Theme.accent)
        .task { await session.start(engine: engine) }
        .task { canvas = await CanvasDue.facts(engine, course: session.launch.course, assignment: session.launch.assignment) }
        .alert("Page code saved", isPresented: Binding(get: { saved != nil }, set: { if !$0 { saved = nil } })) {
            Button("Show in Finder") { if let saved { NSWorkspace.shared.activateFileViewerSelecting([saved]) } }
            Button("OK", role: .cancel) {}
        } message: {
            Text("Every bit of the page's code — the page, each frame in it, and the scripts and style sheets they load — is in Downloads as “\(saved?.lastPathComponent ?? "")”.")
        }
    }

    /// (1.3.78) As Alta's popup: the assignment's own name (Canvas's, else the page's heading) and "Macmillan Learning ·
    /// Due Oct 12 at 11:59 PM" under it; Close at the right end, where every full popup has it.
    private var topBar: some View {
        HStack(spacing: 14) {
            IconTile(symbol: "books.vertical", color: .red, size: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.sTitle3).lineLimit(1)
                if !subtitle.isEmpty {
                    Text(subtitle).font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer(minLength: 8)
            PopupPins() // (1.3.73: the toolbar's pins, here too)
            GlassGroup(spacing: 8) {
                // (1.3.109) Save Page Code here too, to developers (and in the Developer menu)
                if dev.unlocked {
                    Button {
                        Task { if let f = await session.savePageCode() { saved = f } else { NSSound.beep() } }
                    } label: {
                        Label("Save Page Code", systemImage: "chevron.left.forwardslash.chevron.right")
                    }
                    .glassButton()
                    .help("Save every bit of the page's code to Downloads (developer mode)")
                }
                Button("Close") { dismiss() }
                    .glassButton()
                    .keyboardShortcut(.cancelAction)
                    .help("Close — your work is kept by Macmillan")
            }
        }
        .controlSize(.large)
        .padding(.horizontal, 22)
        .padding(.top, 16)
        .padding(.bottom, 12)
        .popupHeader() // (1.3.79: dragged by its head, the popup moves)
    }

    /// Canvas's name for the assignment; else the page's own heading (a launch from a tool's row is named for the tool);
    /// else what it was opened as.
    private var title: String {
        if let t = canvas?.title, !t.isEmpty { return t }
        if let h = session.heading { return h }
        return session.launch.title.isEmpty ? "Macmillan Learning" : session.launch.title
    }

    /// "Macmillan Learning · Due Oct 12 at 11:59 PM" — never the publisher's name under itself, while the title is still
    /// only that.
    private var subtitle: String {
        [title == "Macmillan Learning" ? "" : "Macmillan Learning", canvas?.dueLine ?? ""].filter { !$0.isEmpty }.joined(separator: " · ")
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

import AppKit
import SwiftUI
import WebKit

/// (1.3.17) A Knewton Alta assignment in a popup over the window, as a quiz is taken (QuizScreen): Close and the
/// assignment's name across the top; down the left, its objectives as the quiz's question tiles — the one being worked
/// on filled, a mastered one ticked, the others filling as they are mastered — with each by name, and the total
/// mastery bar. (1.3.45) In the rest, Alta's own page, as it is: the student works on it there. AltaSession.swift reads
/// the objectives and their mastery from it.
struct AltaQuizScreen: View {
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme
    @StateObject private var session: AltaSession
    @State private var ideal = AltaQuizScreen.idealSize()
    @State private var saving = false
    @State private var saved: URL?
    @State private var showLogs = false

    init(launch: AltaLaunch) {
        _session = StateObject(wrappedValue: AltaSession(launch: launch))
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
            HStack(spacing: 0) {
                AltaRail(session: session)
                    .frame(width: ideal.width >= 960 ? 290 : 240)
                Divider()
                page
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .frame(width: ideal.width, height: ideal.height)
        .background(PageGround())
        .tint(Theme.accent)
        .task {
            session.appearance(dark: scheme == .dark)
            await session.start(engine: engine)
        }
        .onChange(of: scheme) { _, s in session.appearance(dark: s == .dark) }
        // (1.3.51) Alta's pop-ups as the Mac's own sheet: Alta's picture of it, its buttons the Mac's
        .sheet(item: Binding(get: { session.dialog }, set: { if $0 == nil && session.dialog != nil { session.dialogDismissed() } })) { d in
            AltaDialogSheet(dialog: d, press: { i in session.pressDialog(i) }, giveBack: { session.dialogDismissed() })
        }
        .alert("Page code saved", isPresented: Binding(get: { saved != nil }, set: { if !$0 { saved = nil } })) {
            Button("Show in Finder") { if let saved { NSWorkspace.shared.activateFileViewerSelecting([saved]) } }
            Button("OK", role: .cancel) {}
        } message: {
            Text("Every bit of Alta's page code — the page, each frame in it, and the scripts and style sheets they load — is in Downloads as “\(saved?.lastPathComponent ?? "")”.")
        }
    }

    // MARK: The top

    private var topBar: some View {
        HStack(spacing: 14) {
            GlassGroup(spacing: 8) {
                Button("Close") { dismiss() }
                    .glassButton()
                    .keyboardShortcut(.cancelAction)
                    .help("Close — your progress is kept by Alta")
            }
            IconTile(symbol: "brain.head.profile", color: Theme.accent, size: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(session.report?.name ?? session.overview?.name ?? session.launch.title)
                    .font(.sTitle3)
                    .lineLimit(1)
                Text(subtitle)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 8)
            // (1.3.46) all of Alta's page code, to one text file
            GlassGroup(spacing: 8) {
                // (1.3.47) shown once Alta's page was not recognised (and given back as it is)
                if !session.errorLogs.isEmpty {
                    Button { showLogs = true } label: {
                        Label("Error Logs (\(session.errorLogs.count))", systemImage: "exclamationmark.triangle")
                    }
                    .glassButton()
                    .help("Alta's page was not recognised, so it is shown as it is: see why")
                    .popover(isPresented: $showLogs, arrowEdge: .bottom) { logs }
                    .transition(.opacity)
                }
                Button { saveCode() } label: {
                    if saving {
                        ProgressView().controlSize(.small)
                    } else {
                        Label("Save Page Code", systemImage: "curlybraces")
                    }
                }
                .glassButton()
                .disabled(saving || session.opening || session.failure != nil)
                .help("Save every bit of Alta's page code — the page, its frames, their scripts and style sheets — to a text file in Downloads")
            }
        }
        .controlSize(.large)
        .padding(.horizontal, 22)
        .padding(.top, 16)
        .padding(.bottom, 12)
    }

    /// (1.3.47) The Error Logs: each time Alta's page was not recognised — when, why, what it held — to copy, or to save
    /// with the page's code.
    private var logs: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Error Logs").font(.sHeadline)
            Text("Alta's page was not recognised, so it is shown as it is. Send these (with the page code) to get it recognised.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ScrollView {
                Text(session.errorLogText)
                    .font(.system(size: 11, design: .monospaced))
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .frame(height: 280)
            .padding(8)
            .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color.primary.opacity(0.05)))
            HStack {
                Button("Clear") { session.clearErrorLogs(); showLogs = false }
                Spacer()
                Button("Copy") { copyToPasteboard(session.errorLogText) }
                Button("Save with Page Code") { showLogs = false; saveCode(withLogs: true) }
                    .buttonStyle(.borderedProminent)
                    .disabled(saving)
            }
        }
        .padding(16)
        .frame(width: 480)
    }

    private func saveCode(withLogs: Bool = false) {
        guard !saving else { return }
        saving = true
        Task {
            let file = await session.savePageCode(withLogs: withLogs)
            saving = false
            if let file { saved = file } else { NSSound.beep() }
        }
    }

    private var subtitle: String {
        var parts = ["Knewton Alta"]
        if let due = session.report?.due ?? session.overview?.due {
            parts.append("Due \(Date(timeIntervalSince1970: due / 1000).formatted(date: .abbreviated, time: .shortened))")
        } else if let words = session.overview?.dueText, !words.isEmpty {
            parts.append("Due \(words)")
        }
        if (session.report?.current.source ?? "").uppercased() == "PRACTICE" { parts.append("Practice") }
        return parts.joined(separator: " · ")
    }

    // MARK: Alta's page

    private var page: some View {
        ZStack {
            // (1.3.49) Alta's question laid on Liquid Glass: its page draws no ground of its own (the page script clears
            // the page's, its parts' and the question card's), so the glass behind is the card; everything in it is
            // drawn by Alta. (1.3.50) The glass is the app's own, light or dark: in dark mode the page script turns the
            // question's colours over (light words on the dark glass). Alta's page as it is (not recognised): edge to
            // edge, on white.
            let glassy = session.focused
            AltaPageHost(web: session.web)
                .padding(glassy ? 12 : 0)
                .background {
                    if glassy {
                        Rectangle().fill(Color.clear)
                            .glassCard(radius: 22, tint: scheme == .dark ? Color.black.opacity(0.18) : Color.white.opacity(0.3))
                    } else {
                        Color.white
                    }
                }
                .padding(glassy ? 18 : 0)
                .animation(Motion.gentle, value: glassy)
            if let why = session.failure {
                ZStack {
                    PageGround()
                    ContentUnavailableView("Knewton Alta could not be opened", systemImage: "exclamationmark.triangle", description: Text(why))
                }
            } else if session.opening {
                ZStack {
                    PageGround()
                    ProgressView("Opening Knewton Alta…").font(.sBody)
                }
                .transition(.opacity)
            }
        }
    }
}

/// Alta's page in the popup: the session's own web view.
private struct AltaPageHost: NSViewRepresentable {
    let web: WKWebView

    func makeNSView(context: Context) -> WKWebView { web }
    func updateNSView(_ v: WKWebView, context: Context) {}
}

// MARK: - The rail

/// Down the left: the objectives as the quiz's question tiles, each by name with its own bar, and the total mastery.
private struct AltaRail: View {
    @ObservedObject var session: AltaSession

    var body: some View {
        let list = session.objectives
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                objectivesCard(list)
                VStack(alignment: .leading, spacing: 12) {
                    ForEach(list) { o in AltaObjectiveRow(o: o) }
                }
                Divider()
                mastery(list)
                if session.report != nil {
                    if let e = session.estimate {
                        let low = e.low, high = e.high
                        Label(low == high ? "About \(Int(low)) questions on this objective" : "About \(Int(low))–\(Int(high)) questions on this objective", systemImage: "list.number")
                            .font(.sFootnote)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .padding(.horizontal, 20)
            .padding(.top, 10)
            .padding(.bottom, 24)
        }
        .scrollIndicators(.never)
    }

    private func objectivesCard(_ list: [AltaObjective]) -> some View {
        let mastered = list.filter(\.mastered).count
        return VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                Text("Objectives").font(.sHeadline)
                Spacer(minLength: 6)
                Text("\(mastered) of \(list.count)")
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
            if list.isEmpty {
                ProgressView().controlSize(.small).frame(maxWidth: .infinity, minHeight: 32)
            } else {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: 5), spacing: 6) {
                    ForEach(list) { o in AltaTile(o: o) }
                }
            }
        }
        .padding(14)
        .card(radius: 16)
    }

    private func mastery(_ list: [AltaObjective]) -> some View {
        let m = session.mastery
        let done = list.filter(\.mastered).count
        return VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Text("Mastery").font(.sHeadline)
                Spacer(minLength: 6)
                Text("\(Int((m * 100).rounded()))%")
                    .font(.sHeadline.monospacedDigit())
                    .contentTransition(.numericText(value: m))
                    .animation(Motion.snappy, value: m)
            }
            AltaMasteryBar(value: m)
            Text(list.isEmpty ? "Toward this assignment" : "\(done) of \(list.count) objectives mastered")
                .font(.sFootnote.monospacedDigit())
                .foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .combine)
    }
}

/// One objective's tile: its number, the one being worked on filled, a mastered one washed with a tick, one under way
/// filled from the bottom as far as it is mastered. Not a button: Alta chooses what comes next.
private struct AltaTile: View {
    let o: AltaObjective

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        let tint = Theme.accent
        ZStack {
            shape.fill(o.current ? tint : (o.mastered ? tint.opacity(0.18) : Color.primary.opacity(0.07)))
            if !o.current && !o.mastered && o.mastery > 0 {
                GeometryReader { g in
                    VStack(spacing: 0) {
                        Spacer(minLength: 0)
                        Rectangle().fill(tint.opacity(0.16)).frame(height: g.size.height * o.mastery)
                    }
                }
                .clipShape(shape)
            }
            if o.mastered && !o.current {
                Image(systemName: "checkmark").font(.system(size: 12, weight: .bold)).foregroundStyle(tint)
            } else {
                Text("\(o.number)")
                    .font(.sCallout.weight(.semibold).monospacedDigit())
                    .foregroundStyle(o.current ? Color.white : Color.secondary)
            }
        }
        .frame(height: 32)
        .frame(maxWidth: .infinity)
        .animation(Motion.fill, value: o.mastery)
        .animation(Motion.snappy, value: o.current)
        .help("\(o.name) — \(Int((o.mastery * 100).rounded()))% mastered\(o.current ? ", working on it now" : "")")
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Objective \(o.number), \(o.name), \(Int((o.mastery * 100).rounded())) percent mastered\(o.current ? ", working on it now" : "")")
    }
}

/// An objective by name, with its own bar.
private struct AltaObjectiveRow: View {
    let o: AltaObjective

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text("\(o.number)")
                    .font(.sFootnote.weight(.bold).monospacedDigit())
                    .foregroundStyle(o.current ? Theme.accent : .secondary)
                    .frame(width: 16, alignment: .leading)
                Text(o.name)
                    .font(.sCallout.weight(o.current ? .semibold : .regular))
                    .foregroundStyle(o.current ? .primary : .secondary)
                    .lineLimit(2)
                Spacer(minLength: 4)
                Text("\(Int((o.mastery * 100).rounded()))%")
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
            AltaMasteryBar(value: o.mastery, height: 4)
                .padding(.leading, 24)
        }
        .accessibilityElement(children: .combine)
    }
}

/// The mastery bar, where the quiz has its progress bar.
private struct AltaMasteryBar: View {
    let value: Double
    var height: CGFloat = 8

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.primary.opacity(0.08))
                Capsule()
                    .fill(Theme.accent)
                    .frame(width: max(value > 0 ? height : 0, g.size.width * min(max(value, 0), 1)))
            }
        }
        .frame(height: height)
        .animation(Motion.fill, value: value)
        .accessibilityLabel("Mastery")
        .accessibilityValue("\(Int((value * 100).rounded())) percent")
    }
}

/// (1.3.51) A pop-up of Alta's as a sheet of the Mac's: Alta's own picture of it, and its buttons as the Mac's — each
/// pressing Alta's. Escape presses its Close, Got it or OK, if it has one, else gives the pop-up back to the page.
private struct AltaDialogSheet: View {
    let dialog: AltaDialog
    let press: (Int) -> Void
    let giveBack: () -> Void

    private var cancelIndex: Int? {
        dialog.buttons.firstIndex { ["close", "cancel", "got it", "ok", "okay", "dismiss", "no thanks", "not now", "done"].contains($0.lowercased()) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 10) {
                IconTile(symbol: "brain.head.profile", color: Theme.accent, size: 26)
                Text(dialog.title.isEmpty ? "Knewton Alta" : dialog.title).font(.sHeadline)
                Spacer()
            }
            if let picture = dialog.picture {
                Image(nsImage: picture)
                    .resizable()
                    .interpolation(.high)
                    .aspectRatio(contentMode: .fit)
                    .frame(maxWidth: min(picture.size.width, 640), maxHeight: min(picture.size.height, 560))
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .frame(maxWidth: .infinity)
            } else if !dialog.text.isEmpty {
                Text(dialog.text).font(.sBody).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
            }
            HStack(spacing: 10) {
                Spacer()
                if cancelIndex == nil {
                    Button("Show in Alta's Page", action: giveBack)
                        .keyboardShortcut(.cancelAction)
                }
                ForEach(Array(dialog.buttons.enumerated()), id: \.offset) { i, label in
                    if i == cancelIndex {
                        Button(label) { press(i) }.keyboardShortcut(.cancelAction)
                    } else if i == dialog.buttons.count - 1 {
                        Button(label) { press(i) }.keyboardShortcut(.defaultAction)
                    } else {
                        Button(label) { press(i) }
                    }
                }
            }
            .controlSize(.large)
        }
        .padding(22)
        .frame(minWidth: 380, maxWidth: 700)
    }
}

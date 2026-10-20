import AppKit
import SwiftUI
import WebKit

/// (1.3.66) A Macmillan Learning assignment in a popup over the window, as an Alta one opens: the assignment's name and
/// when it is due across the top, Close at its right end (1.3.78). (1.3.114) Skinned as Alta's is, from its saved code:
/// before it is opened, its cover as a start screen (its questions, its due line, its grading settings, Open
/// Assignment); then down the left the score, how many questions are done, every question as a tile — the one shown
/// filled, a solved one ticked, one tried and not yet right marked — and the grading settings; in the rest Macmillan's
/// own question, its parts on cards, and along its foot the arrows, Question or My Answer, Resources, AI Tutor, Solution
/// and the main button (Submit Answer, Next Question). Show Browser puts Macmillan's page back as it draws it; an
/// assignment of another kind of Macmillan's is shown as it is.
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
    /// (1.3.116) The app light or dark, told to the skinned page.
    @Environment(\.colorScheme) private var scheme

    init(launch: AltaLaunch) {
        _session = StateObject(wrappedValue: MacmillanSession(launch: launch))
    }

    /// Most of the window it is over: the large popup's size.
    private var ideal: CGSize { popupSize ?? CGSize(width: 1040, height: 700) }

    var body: some View {
        VStack(spacing: 0) {
            topBar
            HStack(spacing: 0) {
                if let s = session.state, s.isQuestion {
                    MacmillanRail(state: s) { n in
                        Task { await session.act("go", n) }
                    }
                    .frame(width: ideal.width >= 960 ? 280 : 236)
                    .transition(.move(edge: .leading).combined(with: .opacity))
                    Divider()
                }
                VStack(spacing: 0) {
                    page.frame(maxWidth: .infinity, maxHeight: .infinity)
                    if let s = session.state, s.isQuestion, !session.browser {
                        MacmillanBar(state: s) { cmd, arg in Task { await session.act(cmd, arg) } }
                            .transition(.move(edge: .bottom).combined(with: .opacity))
                    }
                }
            }
        }
        .frame(width: ideal.width, height: ideal.height)
        // (1.3.123) Macmillan's pop-ups as the Mac's own, as Knewton Alta's are (AltaQuiz): a card of the popup's own over
        // its content, its buttons pressing Macmillan's; held open meanwhile, Escape answering the pop-up. (1.3.124) Its own
        // card: what it says large and centred, a check when it is a question solved (MacmillanDialogLayer)
        .overlay {
            if let d = session.dialog {
                MacmillanDialogLayer(dialog: d) { i in session.pressDialog(i) }
            }
        }
        .animation(Motion.gentle, value: session.dialog?.id)
        .popupCloseHeld(session.dialog != nil)
        .background(PageGround())
        .tint(Theme.accent)
        .task { await session.start(engine: engine) }
        .onChange(of: scheme, initial: true) { _, s in session.setDark(s == .dark) }
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
            // (1.3.139) Browser in the top bar, left of the tools, as Knewton Alta's is (it sat in the rail until then)
            if session.state?.isQuestion == true {
                Button {
                    withAnimation(Motion.gentle) { session.setBrowser(!session.browser) }
                } label: {
                    Label("Browser", systemImage: "globe").labelStyle(.titleAndIcon)
                }
                .glassChip(Theme.accent, on: session.browser)
                .fixedSize()
                .help(session.browser ? "Back to Simpl's view of the assignment" : "Show Macmillan Learning's own page, as Macmillan draws it")
                .accessibilityLabel("Show Browser")
                .accessibilityAddTraits(session.browser ? .isSelected : [])
            }
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
        if let t = session.state?.title ?? session.state?.cover?.name, !t.isEmpty { return t } // (1.3.114: the assessment's own)
        if let h = session.heading { return h }
        return session.launch.title.isEmpty ? "Macmillan Learning" : session.launch.title
    }

    /// "Macmillan Learning · Due Oct 12 at 11:59 PM" — never the publisher's name under itself, while the title is still
    /// only that.
    private var subtitle: String {
        [title == "Macmillan Learning" ? "" : "Macmillan Learning", canvas?.dueLine ?? session.state?.cover?.due ?? ""].filter { !$0.isEmpty }.joined(separator: " · ")
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

    /// (1.3.116) The page skinned now: the assessment player's cover or question, Show Browser off.
    private var skinned: Bool { !session.browser && (session.state?.isQuestion == true || session.state?.isCover == true) }
    /// (1.3.119) A question shown skinned: laid on the app's own glass panel.
    private var glassPanel: Bool { !session.browser && session.state?.isQuestion == true }

    private var page: some View {
        ZStack {
            // (1.3.116) skinned, laid on the popup's glass; as Macmillan draws it (Show Browser, another kind), on white.
            // (1.3.119) The question on a panel of real glass the app draws (the page clear over it, its own panel put
            // away: the skin's simpl-mm-glass), inset from the popup's edges
            MacmillanPageHost(web: session.web)
                .clipShape(RoundedRectangle(cornerRadius: glassPanel ? 22 : 0, style: .continuous))
                .background {
                    if glassPanel { Color.clear.glassCard(radius: 22) } else if !skinned { Color.white }
                }
                .padding(glassPanel ? 16 : 0)
            // (1.3.114) the cover as a start screen, over the page, until Open Assignment
            if let s = session.state, s.isCover, let c = s.cover, !session.browser {
                MacmillanStartScreen(cover: c) { Task { await session.act("open") } }
                    .transition(.opacity)
            }
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

// MARK: - (1.3.114) The skin's parts

/// Down the left, as Alta's objectives are: the score and how many questions are done, every question as a tile, and the
/// grading settings (Browser is in the top bar since 1.3.139).
private struct MacmillanRail: View {
    let state: MacmillanState
    let go: (Int) -> Void
    @State private var settingsOpen = true

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                score
                questions
                if let p = state.policies, !p.isEmpty {
                    DisclosureGroup(isExpanded: $settingsOpen) {
                        VStack(alignment: .leading, spacing: 8) {
                            ForEach(Array(p.enumerated()), id: \.offset) { _, line in
                                Label(line, systemImage: MacmillanRail.symbol(line))
                                    .font(.sFootnote)
                                    .foregroundStyle(.secondary)
                            }
                        }
                        .padding(.top, 8)
                    } label: {
                        Text("Grading Settings").font(.sHeadline)
                    }
                }
            }
            .padding(.horizontal, 20)
            .padding(.top, 10)
            .padding(.bottom, 24)
        }
        .scrollIndicators(.never)
    }

    /// The score as Macmillan keeps it, and how many questions are done.
    private var score: some View {
        let got = state.score?.first ?? 0
        let total = state.score?.dropFirst().first ?? 0
        let done = state.completed ?? 0
        let count = state.count ?? state.items?.count ?? 0
        return VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Text("Score").font(.sHeadline)
                Spacer(minLength: 6)
                // (1.3.126) finished, Macmillan gives the final score as a percentage (a third number marks it)
                Text(total > 0 ? ((state.score?.count ?? 0) > 2 ? "\(Self.number(got))%" : "\(Self.number(got)) / \(Self.number(total))") : "–")
                    .font(.sHeadline.monospacedDigit())
                    .contentTransition(.numericText(value: got))
                    .animation(Motion.snappy, value: got)
            }
            ProgressView(value: total > 0 ? min(got / total, 1) : 0)
                .tint(Theme.accent)
            Text("\(done) of \(count) questions completed")
                .font(.sFootnote.monospacedDigit())
                .foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .combine)
    }

    private var questions: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Questions").font(.sHeadline)
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: 5), spacing: 6) {
                ForEach(state.items ?? []) { item in
                    MacmillanTile(item: item, current: item.n - 1 == state.index) { go(item.n) }
                }
            }
        }
        .padding(14)
        .card(radius: 16)
    }

    static func number(_ v: Double) -> String { v == v.rounded() ? String(Int(v)) : String(format: "%.1f", v) }

    /// A grading setting's symbol, by what it says.
    static func symbol(_ line: String) -> String {
        let l = line.lowercased()
        if l.contains("attempt") && !l.contains("incorrect") { return "arrow.counterclockwise" }
        if l.contains("partial credit") || l.contains("incorrect") { return "minus.circle" }
        if l.contains("late") { return "clock" }
        if l.contains("solution") { return "lightbulb" }
        if l.contains("tutor") { return "sparkles" }
        if l.contains("resource") { return "book" }
        return "checkmark.circle"
    }
}

/// One question's tile: its number; the one shown filled, a solved one washed with a tick, one tried and not yet right
/// marked in orange. A press opens it.
private struct MacmillanTile: View {
    let item: MacmillanState.Item
    let current: Bool
    let open: () -> Void

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        let tint = Theme.accent
        Button(action: open) {
            ZStack {
                shape.fill(current ? tint : (item.solved ? Color.green.opacity(0.2) : (item.tried ? Color.orange.opacity(0.18) : Color.primary.opacity(0.07))))
                if item.solved && !current {
                    Image(systemName: "checkmark").font(.system(size: 12, weight: .bold)).foregroundStyle(Color.green)
                } else {
                    Text(item.label.replacingOccurrences(of: "Q", with: ""))
                        .font(.sCallout.weight(.semibold).monospacedDigit())
                        .foregroundStyle(current ? Color.white : (item.tried ? Color.orange : Color.secondary))
                }
            }
            .frame(height: 32)
            .frame(maxWidth: .infinity)
            .contentShape(shape)
        }
        .buttonStyle(.plain)
        .animation(Motion.snappy, value: current)
        .help([item.label, item.note, item.points ?? ""].filter { !$0.isEmpty }.joined(separator: " · "))
        .accessibilityLabel("Question \(item.n), \(item.note)\(current ? ", shown" : "")")
    }
}

/// Along the page's foot: the arrows and which question this is, Question or My Answer once it has been answered,
/// Resources, AI Tutor and Solution, the verdict (1.3.117: Correct or Incorrect, as Alta's), and the main button.
private struct MacmillanBar: View {
    let state: MacmillanState
    let act: (String, Any?) -> Void

    var body: some View {
        HStack(spacing: 10) {
            // (1.3.117) the arrows side by side, on the line with "Question 1 of 22"
            GlassGroup(spacing: 6) {
                HStack(spacing: 6) {
                    Button { act("prev", nil) } label: { Image(systemName: "chevron.left") }
                        .glassButton()
                        .disabled(state.first == true || (state.index ?? 0) <= 0)
                        .help("Previous question")
                    Button { act("next", nil) } label: { Image(systemName: "chevron.right") }
                        .glassButton()
                        .disabled(state.last == true || (state.index ?? 0) + 1 >= (state.count ?? 0))
                        .help("Next question")
                }
            }
            Text("Question \((state.index ?? 0) + 1) of \(state.count ?? 0)")
                .font(.sCallout.weight(.semibold).monospacedDigit())
            if let a = state.attempts, a.choices.count > 1 {
                Picker("", selection: Binding(get: { a.choices.firstIndex(of: a.label) ?? 0 }, set: { act("attempt", $0) })) {
                    ForEach(Array(a.choices.enumerated()), id: \.offset) { i, c in Text(c).tag(i) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
                .help("The question, or your answer to it")
            }
            Spacer(minLength: 8)
            // (1.3.117) Macmillan's verdict on the question as Alta's is: the popup's own Correct or Incorrect pill, at the
            // left of the buttons (Macmillan's Solved bar along the page's foot put away)
            if let v = state.verdict, !v.isEmpty { AltaVerdictPill(right: v == "right") }
            if let t = state.tools {
                if t.resources { Button { act("resources", nil) } label: { Label("Resources", systemImage: "book") }.glassButton() }
                if t.tutor { Button { act("tutor", nil) } label: { Label("AI Tutor", systemImage: "sparkles") }.glassButton() }
                if t.solution { Button { act("solution", nil) } label: { Label("Solution", systemImage: "lightbulb") }.glassButton() }
            }
            if let main = state.main {
                Button(main) { act("main", nil) }
                    .glassButton(prominent: true)
                    .keyboardShortcut(.defaultAction)
                    .disabled(state.mainOn == false)
            }
        }
        .controlSize(.large)
        .padding(.horizontal, 18)
        .padding(.vertical, 12)
        .background(.bar)
        .overlay(alignment: .top) { Divider() }
    }
}

/// Before the assessment is opened, its cover as Alta's start screen is: its name, when it is due and how many
/// questions it has, its grading settings, and Open Assignment.
private struct MacmillanStartScreen: View {
    let cover: MacmillanState.Cover
    let open: () -> Void
    @State private var going = false

    var body: some View {
        ZStack {
            PageGround()
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    VStack(alignment: .leading, spacing: 6) {
                        Text(cover.name ?? "Assignment").font(.sTitle2)
                        Text([cover.due ?? "", cover.count.map { "\($0) question\($0 == 1 ? "" : "s")" } ?? ""].filter { !$0.isEmpty }.joined(separator: " · "))
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                    }
                    if let p = cover.policies, !p.isEmpty {
                        VStack(alignment: .leading, spacing: 10) {
                            Text("Grading Settings").font(.sHeadline)
                            ForEach(Array(p.enumerated()), id: \.offset) { _, line in
                                Label(line, systemImage: MacmillanRail.symbol(line)).font(.sCallout)
                            }
                        }
                        .padding(16)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .card(radius: 16)
                    }
                    Button {
                        going = true
                        open()
                    } label: {
                        if going { ProgressView().controlSize(.small) } else { Text("Open Assignment") }
                    }
                    .glassButton(prominent: true)
                    .controlSize(.large)
                    .keyboardShortcut(.defaultAction)
                    .disabled(going)
                }
                .padding(28)
                .frame(maxWidth: 620, alignment: .leading)
                .frame(maxWidth: .infinity)
            }
        }
    }
}


/// (1.3.124) Macmillan's pop-up — You solved it in 1 attempt!, View Solution, Next Question — as a card of the Mac's own:
/// a check in a soft green disc (a question solved) or the pop-up's own title, what it says large and centred, its
/// buttons side by side across the card with Macmillan's main one lit, and its × at the top corner. Return presses the
/// main one, Escape the ×. It springs in over the dimmed question; a press rests the buttons until Macmillan moves on.
private struct MacmillanDialogLayer: View {
    let dialog: AltaDialog
    let press: (Int) -> Void
    @State private var shown = false
    @State private var pressed = false

    private struct Action: Identifiable {
        let id: Int
        let choice: AltaDialog.Choice
    }

    private var message: String {
        RichText.plainText(dialog.html).split(whereSeparator: \.isWhitespace).joined(separator: " ")
    }
    private var solved: Bool {
        let m = message.lowercased()
        return m.contains("solved") || (m.contains("correct") && !m.contains("incorrect"))
    }
    private var closeIndex: Int? { dialog.buttons.firstIndex(where: \.close) }
    private var actions: [Action] {
        dialog.buttons.enumerated().filter { !$0.element.close }.map { Action(id: $0.offset, choice: $0.element) }
    }
    private var mainIndex: Int? { actions.last(where: { $0.choice.primary })?.id ?? actions.last?.id }

    var body: some View {
        ZStack {
            Color.black.opacity(0.28)
                .contentShape(Rectangle())
                .onTapGesture {}
                .accessibilityHidden(true)
            card
                .scaleEffect(shown ? 1 : 0.92)
                .opacity(shown ? 1 : 0)
                .accessibilityAddTraits(.isModal)
        }
        .onAppear { withAnimation(.spring(response: 0.34, dampingFraction: 0.78)) { shown = true } }
        .onChange(of: dialog.step) { _, _ in pressed = false }
        .transition(.opacity)
    }

    private var card: some View {
        VStack(spacing: 16) {
            if solved {
                ZStack {
                    Circle()
                        .fill(Color.green.opacity(0.16))
                        .frame(width: 92, height: 92)
                    Circle()
                        .strokeBorder(Color.green.opacity(0.35), lineWidth: 1.5)
                        .frame(width: 92, height: 92)
                    Image(systemName: "checkmark")
                        .font(.system(size: 40, weight: .bold))
                        .foregroundStyle(.green)
                        .symbolEffect(.bounce, value: shown)
                }
                .overlay(alignment: .topTrailing) {
                    Image(systemName: "sparkles")
                        .font(.system(size: 20, weight: .semibold))
                        .foregroundStyle(.yellow)
                        .offset(x: 10, y: -6)
                        .opacity(shown ? 1 : 0)
                        .scaleEffect(shown ? 1 : 0.4)
                        .animation(.spring(response: 0.4, dampingFraction: 0.6).delay(0.15), value: shown)
                }
                .padding(.top, 6)
                .accessibilityHidden(true)
            } else {
                HStack(spacing: 10) {
                    IconTile(symbol: "books.vertical", color: .red, size: 26)
                    Text(dialog.title.trimmingCharacters(in: .whitespaces).isEmpty ? "Macmillan Learning" : dialog.title)
                        .font(.sHeadline)
                }
            }
            if !message.isEmpty {
                Text(message)
                    .font(solved ? Font.sTitle2 : Font.sBody)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
            HStack(spacing: 10) {
                ForEach(actions) { a in
                    Button { tap(a.id) } label: {
                        Text(a.choice.label).frame(maxWidth: .infinity)
                    }
                    .glassButton(prominent: a.id == mainIndex)
                    .keyboardShortcut(a.id == mainIndex ? KeyboardShortcut.defaultAction : nil)
                    .disabled(!a.choice.enabled || pressed)
                    .help(a.choice.label)
                }
            }
            .controlSize(.large)
            .padding(.top, 4)
        }
        .padding(.horizontal, 26)
        .padding(.top, 28)
        .padding(.bottom, 22)
        .frame(width: 400)
        .overlay(alignment: .topTrailing) {
            if let x = closeIndex {
                Button { tap(x) } label: {
                    Image(systemName: "xmark")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundStyle(.secondary)
                        .frame(width: 26, height: 26)
                        .background(Circle().fill(Color.primary.opacity(0.08)))
                        .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .keyboardShortcut(.cancelAction)
                .help("Close")
                .padding(12)
            }
        }
        .glassCard(radius: 26)
        .shadow(color: .black.opacity(0.3), radius: 30, y: 12)
    }

    private func tap(_ i: Int) {
        pressed = true
        press(i)
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) { pressed = false }
    }
}

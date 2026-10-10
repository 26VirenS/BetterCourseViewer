import AppKit
import Combine
import SwiftUI

/// A Classic Quiz taken in the app, in a sheet over the window: the intro (its rules, the access code, Begin or
/// Resume), the attempt one question at a time (the strip of every question to move between them, flags, the clock,
/// every kind of question answered in place), the review before handing in, the receipt, and the feedback on a
/// finished attempt. Answers save as they are given; nothing is handed in unless the student says so. In an attempt
/// ← and → move between the questions, 1–9 pick an answer, Return goes on, ⌘Return submits and Escape closes.
///
/// (1.2) The sheet is one page with nothing boxed on it: the question sits on the page and its answers are rows; the
/// review is a plain list. Its chrome floats over the page in Liquid Glass — Close and the clock at the top, the
/// strip of questions, the bar of Previous, Next and Submit at the foot — and on a wide sheet the questions (and the
/// feedback's score) go down a column at the side. The panes are QuizTaking.swift's and QuizResults.swift's.
struct QuizScreen: View {
    let launch: QuizLaunch
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var run: QuizRun
    @State private var confirmLeave = false
    @State private var confirmSubmit = false
    @State private var instructions = false
    /// The sheet's size, taken once from the window it opens over.
    @State private var ideal = QuizScreen.idealSize()
    private let clock = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    init(launch: QuizLaunch) {
        self.launch = launch
        _run = StateObject(wrappedValue: QuizRun(course: launch.course, quiz: launch.quiz))
    }

    /// (1.2) Most of the window the sheet opens over — the Mac's room put to the quiz's use, so a wide window has the
    /// side columns — within a bound that keeps it a sheet.
    private static func idealSize() -> CGSize {
        let windows = NSApp.windows.filter { $0.isVisible && $0.sheetParent == nil && !($0 is NSPanel) }
        guard let w = NSApp.mainWindow ?? windows.max(by: { $0.frame.width < $1.frame.width }), w.sheetParent == nil else {
            return CGSize(width: 1040, height: 700)
        }
        let room = w.contentLayoutRect.size
        return CGSize(width: min(max(room.width - 64, 720), 1400), height: min(max(room.height - 36, 500), 960))
    }

    private var tint: Color { Color(hex: run.attempt?.color ?? run.intro?.color ?? "#0a84ff") }
    private var inAttempt: Bool { [.taking, .review, .submitting].contains(run.stage) }

    var body: some View {
        VStack(spacing: 0) {
            topBar
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .animation(Motion.gentle, value: run.stage) // (each step — the intro, starting, the attempt, the review, the receipt, the feedback — crossfades, never cuts)
                .overlay(alignment: .top) {
                    banner.animation(Motion.gentle, value: run.banner) // (outside the if: it plays on the way in and out)
                }
        }
        // (1.3.7) exactly that size: a sheet keeps to its least width, not its ideal one, so the ideal alone left it at 720
        .frame(width: ideal.width, height: ideal.height) // (1.2: the window's room, never taller than it)
        .background(PageGround())
        .tint(tint)
        .interactiveDismissDisabled(inAttempt) // (Escape is Close, which asks first during an attempt)
        .task { await start() }
        .onReceive(clock) { t in run.checkClock(t) }
        .onChange(of: engine.dataVersion) { _, _ in
            if run.stage == .intro { Task { await run.loadIntro() } } // (View ▸ Reload: the intro read again)
        }
        .confirmationDialog("Leave the quiz?", isPresented: $confirmLeave, titleVisibility: .visible) {
            Button("Save and Exit") {
                Task {
                    await run.flush()
                    dismiss()
                }
            }
            Button("Keep Working", role: .cancel) {}
        } message: {
            Text("Every answer is saved with Canvas. The attempt stays open — come back to it from the quiz\(run.attempt?.timed == true ? "; the clock keeps running" : "").")
        }
        .confirmationDialog("Submit this attempt?", isPresented: $confirmSubmit, titleVisibility: .visible) {
            Button("Submit") { Task { await run.submit() } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(submitMessage)
        }
        .alert("Some answers were not kept", isPresented: missingShown) {
            Button("Submit Anyway", role: .destructive) {
                run.missing = []
                Task { await run.submit(force: true) }
            }
            Button("Go Back", role: .cancel) { run.missing = [] }
        } message: {
            Text(missingMessage)
        }
        .alert("Access Code", isPresented: $run.askCode) {
            TextField("Access code", text: $run.code)
                .autocorrectionDisabled()
            Button("Save Answer") { Task { await run.codeGiven() } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("Canvas wants the quiz’s access code to save your answers. It stays with this attempt.")
        }
        .alert("Time is up", isPresented: $run.timeUp) {
            Button("OK", role: .cancel) {}
        } message: {
            Text("Canvas closes the attempt at the time limit. Check your answers and submit.")
        }
        .onDisappear { if run.submitted { engine.changed() } }
    }

    /// The intro, then wherever the launch asked for: the last attempt's feedback, or straight into the attempt (on to
    /// a question, or its review).
    private func start() async {
        run.engine = engine
        await run.loadIntro()
        if launch.feedback, let last = run.intro?.last, last.feedback {
            await run.openFeedback(last.attempt)
        } else if launch.begin, run.intro?.canStart == true, run.intro?.needsCode != true {
            await run.begin()
            if let k = launch.startAt { await run.go(to: k) }
            if launch.review { await run.toReview() }
        }
    }

    // MARK: - The bar

    private var title: String { run.attempt?.title ?? run.intro?.title ?? launch.title }

    private var subtitle: String {
        let context = run.attempt?.context ?? run.intro?.context ?? ""
        let attempt = run.attempt.map { "Attempt \($0.attempt)" } ?? ""
        let parts: [String]
        switch run.stage {
        case .intro, .starting: parts = [context]
        case .taking: parts = [attempt, context]
        case .review, .submitting: parts = ["Review", attempt]
        case .receipt: parts = ["Submitted", context]
        case .feedback: parts = ["Feedback", (run.feedback?.attempt).map { "Attempt \($0)" } ?? ""]
        }
        return parts.filter { !$0.isEmpty }.joined(separator: " · ")
    }

    /// The sheet's own bar, on the page with no band behind it (1.2): Close (and Back, in the feedback) in glass, the
    /// quiz and where it stands, and during an attempt the clock and the instructions in glass; the quiz on Canvas in
    /// its menu. Submit is at the foot of the attempt, with Next.
    private var topBar: some View {
        HStack(spacing: 14) {
            GlassGroup(spacing: 8) {
                HStack(spacing: 8) {
                    if run.stage == .feedback {
                        Button { run.leaveFeedback() } label: { Label("Back", systemImage: "chevron.left") }
                            .glassButton()
                            .help("Back")
                    }
                    Button("Close", action: close)
                        .glassButton()
                        .keyboardShortcut(.cancelAction)
                        .disabled(run.stage == .submitting)
                        .help(inAttempt ? "Save your answers and close — the attempt stays open" : "Close the quiz")
                }
            }
            IconTile(symbol: "checklist", color: tint, size: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.sTitle3)
                    .lineLimit(1)
                if !subtitle.isEmpty {
                    Text(subtitle)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 8)
            GlassGroup(spacing: 8) {
                HStack(spacing: 8) {
                    if inAttempt {
                        QuizClock(run: run)
                        instructionsButton
                    }
                    moreMenu
                }
            }
        }
        .controlSize(.large)
        .padding(.horizontal, 22)
        .padding(.top, 16)
        .padding(.bottom, 12)
    }

    private var instructionsButton: some View {
        Button { instructions.toggle() } label: { Label("Instructions", systemImage: "book") }
            .labelStyle(.iconOnly)
            .glassButton()
            .help("Instructions")
            .popover(isPresented: $instructions, arrowEdge: .bottom) {
                QuizInstructionsPopover(html: run.attempt?.html ?? run.intro?.html ?? "")
                    .environmentObject(engine)
            }
    }

    private var moreMenu: some View {
        Menu {
            if run.stage == .taking {
                Button { Task { await run.toReview() } } label: { Label("Review Answers", systemImage: "list.bullet.rectangle") }
                Button(action: askSubmit) { Label("Submit…", systemImage: "paperplane") }
                Divider()
            }
            Button(action: openCanvas) { Label("Open in Canvas", systemImage: "globe") }
            Button(action: copyLink) { Label("Copy Link", systemImage: "link") }
        } label: {
            Image(systemName: "ellipsis")
        }
        .menuStyle(.button)
        .glassButton()
        .menuIndicator(.hidden)
        .fixedSize()
        .help("More")
        .accessibilityLabel("More")
    }

    // MARK: - What the sheet shows

    @ViewBuilder
    private var content: some View {
        switch run.stage {
        case .intro:
            if let i = run.intro {
                QuizIntroPane(run: run, intro: i, tint: tint)
                    .transition(.opacity)
            } else {
                LoadState(error: run.introError) { Task { await run.loadIntro() } }
            }
        case .starting:
            ProgressView(run.intro?.begin.hasPrefix("Continue") == true ? "Resuming your attempt…" : "Starting your attempt…")
                .font(.sBody)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .transition(.opacity)
        case .taking:
            QuizTakePane(run: run, tint: tint, openCanvas: openCanvas, submit: askSubmit)
                .transition(.opacity)
        case .review, .submitting:
            QuizReviewPane(run: run, tint: tint, submit: askSubmit)
                .transition(reviewTransition)
        case .receipt:
            QuizReceiptPane(run: run, tint: tint, done: { dismiss() })
                .transition(.opacity)
        case .feedback:
            QuizFeedbackPane(run: run, tint: tint)
                .transition(.opacity)
        }
    }

    /// The review arrives over the attempt; under Reduce Motion it only fades.
    private var reviewTransition: AnyTransition {
        reduceMotion ? .opacity : .scale(scale: 0.98).combined(with: .opacity)
    }

    /// A save that failed, a warning of the clock: a capsule at the top (glass; red for a failure), gone by itself
    /// (or with a click).
    @ViewBuilder
    private var banner: some View {
        if let b = run.banner {
            bannerSurface(
                Label(b.text, systemImage: b.error ? "exclamationmark.triangle.fill" : "clock.fill")
                    .font(.sBody.weight(.semibold))
                    .foregroundStyle(b.error ? Color.white : Color.primary)
                    .padding(.horizontal, 18)
                    .padding(.vertical, 10),
                error: b.error
            )
            .padding(.top, 10)
            .transition(bannerTransition)
            .onTapGesture { run.banner = nil }
            .help("Click to dismiss")
        }
    }

    @ViewBuilder
    private func bannerSurface<V: View>(_ v: V, error: Bool) -> some View {
        if error {
            v.background(Color.red, in: Capsule())
                .shadow(color: Theme.shadow, radius: 12, y: 4)
        } else {
            v.glassCapsule()
        }
    }

    private var bannerTransition: AnyTransition {
        reduceMotion ? .opacity : .move(edge: .top).combined(with: .opacity)
    }

    // MARK: - What the buttons do

    /// Close: at once outside an attempt; during one, after asking (every answer is saved; the attempt stays open).
    private func close() {
        if inAttempt { confirmLeave = true } else { dismiss() }
    }

    /// Submit (⌘Return): always asked first, with what is still blank.
    private func askSubmit() {
        guard run.stage == .taking || run.stage == .review else { return }
        confirmSubmit = true
    }

    private var submitMessage: String {
        let blank = run.questions.count - run.answeredCount
        guard blank > 0 else { return "Submitting ends the attempt." }
        return "\(blank) \(blank == 1 ? "question is" : "questions are") still blank. Submitting ends the attempt; blank questions are graded as incorrect."
    }

    private var missingShown: Binding<Bool> {
        Binding(get: { !run.missing.isEmpty }, set: { if !$0 { run.missing = [] } })
    }

    private var missingMessage: String {
        let one = run.missing.count == 1
        let which = run.missing.map { "question \($0)" }.joined(separator: ", ")
        return "Canvas did not keep your \(one ? "answer" : "answers") to \(which). Go back to answer \(one ? "it" : "them") again, or submit as it is."
    }

    private var quizPath: String { "/courses/\(launch.course)/quizzes/\(launch.quiz)" }

    /// Canvas's own page for the quiz, in a window of its own — during an attempt, the attempt as it stands (every
    /// answer saved first; nothing restarts).
    private func openCanvas() {
        let take = run.attempt?.takeUrl ?? ""
        let url = inAttempt && !take.isEmpty ? take : quizPath
        let name = title
        Task {
            await run.flush()
            dismiss()
            engine.openWebScreen(url, title: name)
        }
    }

    private func copyLink() {
        if let u = engine.absolute(quizPath) { copyToPasteboard(u.absoluteString) }
    }
}

// MARK: - Shared by the panes

/// The quiz sheet's measures (1.2).
enum QuizLayout {
    /// The width from which a pane puts a column beside the page (the questions, the score, the summary).
    static let wide: CGFloat = 900
    /// The readable width of a question and of a list of answers.
    static let column: CGFloat = 840
    /// The room left at the foot of a scrolling page so its end clears the floating bar.
    static let barClearance: CGFloat = 116
}

/// The floating bar at the foot of a pane (1.2): its buttons in Liquid Glass, drawn together, over a soft fade of the
/// page so what scrolls under it never fights the words on it. Only its buttons take clicks.
struct QuizFloatingBar<Content: View>: View {
    @ViewBuilder var content: Content

    var body: some View {
        GlassGroup(spacing: 8) {
            HStack(spacing: 10) { content }
        }
        .controlSize(.large)
        .padding(.horizontal, 24)
        .padding(.top, 30)
        .padding(.bottom, 20)
        .background {
            LinearGradient(colors: [Theme.page.opacity(0), Theme.page.opacity(0.88)], startPoint: .top, endPoint: .bottom)
                .allowsHitTesting(false)
        }
    }
}

// MARK: - The clock and the instructions

/// The attempt's clock in the bar, a glass capsule: the time left on a timed attempt (red in its last five minutes),
/// or the time taken so far. It ticks by itself, so the rest of the sheet is not drawn again every second.
private struct QuizClock: View {
    @ObservedObject var run: QuizRun
    @State private var now = Date()
    private let tick = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var body: some View {
        let left = run.remaining(at: now)
        let shown = left ?? run.elapsed(at: now)
        let low = (left ?? .infinity) < 300
        ZStack {
            if let shown {
                let time = QuizTime.clock(shown)
                let spoken: String = left != nil ? "\(time) left" : "\(time) so far"
                let tip: String = left != nil ? "Time left in this attempt" : "Time taken so far"
                Label(time, systemImage: left != nil ? "timer" : "clock")
                    .font(.sBody.weight(.semibold).monospacedDigit())
                    .foregroundStyle(low ? Color.red : Color.primary)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 7)
                    .glassCapsule(tint: low ? Color.red.opacity(0.35) : nil)
                    .help(tip)
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel(spoken)
            }
        }
        .onReceive(tick) { now = $0 }
    }
}

/// The quiz's instructions, a click away the whole way through an attempt.
private struct QuizInstructionsPopover: View {
    let html: String

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("Instructions")
                .font(.sTitle3)
                .padding(.horizontal, 20)
                .padding(.top, 16)
                .padding(.bottom, 10)
            Divider()
            ScrollView {
                Group {
                    if html.isEmpty {
                        Text("This quiz came with no instructions.")
                            .font(.sBody)
                            .foregroundStyle(.secondary)
                    } else {
                        RichText(html: html, size: 15)
                    }
                }
                .padding(20)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(width: 500, height: 460)
    }
}

// MARK: - Intro

/// A quiz before an attempt: what it is, its instructions, its rules (the attempts, the time limit, one question at a
/// time), the access code when it wants one, why it cannot begin, the last attempt, and the one thing to do next.
/// (1.2) All of it on the page, a heading per part and no box round any; on a wide sheet the instructions on the left
/// and the rules, the code and the last attempt in a column beside them. Begin floats at the foot in glass.
private struct QuizIntroPane: View {
    @ObservedObject var run: QuizRun
    let intro: QuizIntro
    let tint: Color
    @FocusState private var codeFocused: Bool

    var body: some View {
        GeometryReader { geo in
            let wide = geo.size.width >= QuizLayout.wide
            ScrollView {
                Group {
                    if wide { columns } else { oneColumn }
                }
                .padding(.horizontal, wide ? 48 : 36)
                .padding(.top, 20)
                .padding(.bottom, QuizLayout.barClearance)
                .frame(maxWidth: .infinity)
            }
            .overlay(alignment: .bottom) { actions }
        }
        .task {
            // (a quiz that wants a code: its field ready to type in, once the sheet is up)
            try? await Task.sleep(nanoseconds: 120_000_000)
            if intro.needsCode && run.code.isEmpty { codeFocused = true }
        }
    }

    /// A wide sheet: the quiz and its instructions, and beside them what it asks of the student.
    private var columns: some View {
        HStack(alignment: .top, spacing: 52) {
            VStack(alignment: .leading, spacing: 30) {
                heading
                notices
                instructions
            }
            .frame(maxWidth: 680, alignment: .leading)
            VStack(alignment: .leading, spacing: 30) {
                code
                rules
                lastAttempt
            }
            .frame(width: 300, alignment: .leading)
            .padding(.top, 8)
        }
        .frame(maxWidth: 1100, alignment: .leading)
    }

    private var oneColumn: some View {
        VStack(alignment: .leading, spacing: 30) {
            heading
            notices
            code
            rules
            instructions
            lastAttempt
        }
        .frame(maxWidth: 700, alignment: .leading)
    }

    private var heading: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label(intro.context ?? "Quiz", systemImage: "checklist")
                .font(.sBody.weight(.semibold))
                .foregroundStyle(tint)
                .lineLimit(1)
            Text(intro.title)
                .font(.sLargeTitle)
                .tracking(-0.5)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
            if !intro.facts.isEmpty {
                Text(intro.facts.joined(separator: " · "))
                    .font(.sBody)
                    .foregroundStyle(.secondary)
            }
            if let note = intro.note, !note.isEmpty {
                Text(note)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        }
    }

    /// Why it cannot begin, and when it unlocks: a line each, in its colour.
    @ViewBuilder
    private var notices: some View {
        let why = run.refused ?? ""
        let lock = intro.lockText ?? ""
        if !why.isEmpty || !lock.isEmpty {
            VStack(alignment: .leading, spacing: 10) {
                if !why.isEmpty {
                    Label(why, systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(.red)
                }
                if !lock.isEmpty {
                    Label(lock, systemImage: "lock.fill")
                        .foregroundStyle(.secondary)
                }
            }
            .font(.sBody)
            .fixedSize(horizontal: false, vertical: true)
        }
    }

    @ViewBuilder
    private var instructions: some View {
        if !intro.html.isEmpty {
            PageSection(title: "Before You Start") {
                RichText(html: intro.html, size: 15)
            }
        }
    }

    @ViewBuilder
    private var rules: some View {
        if !intro.rules.isEmpty {
            PageSection(title: "Rules") {
                VStack(alignment: .leading, spacing: 12) {
                    ForEach(intro.rules, id: \.self) { r in
                        HStack(alignment: .firstTextBaseline, spacing: 12) {
                            Image(systemName: r.symbol)
                                .foregroundStyle(Self.tone(r.tint))
                                .frame(width: 22)
                                .accessibilityHidden(true)
                            Text(r.text)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        .font(.sBody)
                    }
                }
            }
        }
    }

    @ViewBuilder
    private var code: some View {
        if intro.needsCode {
            PageSection(title: "Access Code") {
                VStack(alignment: .leading, spacing: 8) {
                    TextField("Access code", text: $run.code)
                        .textFieldStyle(.roundedBorder)
                        .controlSize(.large)
                        .font(.sBody)
                        .autocorrectionDisabled()
                        .focused($codeFocused)
                        .onSubmit(startAttempt)
                        .frame(maxWidth: 320)
                    Text("Your instructor gives the code. It stays with this attempt.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
            }
        }
    }

    @ViewBuilder
    private var lastAttempt: some View {
        // (1.3.32) every attempt, newest first, with its score and when it was handed in; the kept one marked
        if let list = intro.attempts, !list.isEmpty {
            PageSection(title: list.count == 1 ? "Your Attempt" : "Your Attempts") {
                VStack(alignment: .leading, spacing: 12) {
                    ForEach(list, id: \.attempt) { a in attemptRow(a) }
                    if let note = intro.keptNote, !note.isEmpty {
                        Text(note)
                            .font(.sFootnote)
                            .foregroundStyle(.secondary)
                    }
                }
            }
        } else if let last = intro.last {
            PageSection(title: "Your Last Attempt") {
                HStack(alignment: .firstTextBaseline, spacing: 12) {
                    Text("Attempt \(last.attempt)")
                        .font(.sBody.weight(.semibold))
                    Spacer(minLength: 8)
                    if let s = last.score {
                        Text(s)
                            .font(.sBody.monospacedDigit())
                            .foregroundStyle(.secondary)
                    } else if let why = last.why, !why.isEmpty {
                        Text(why)
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.trailing)
                    }
                }
            }
        }
    }

    private func attemptRow(_ a: QuizIntro.Attempt) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 6) {
                    Text("Attempt \(a.attempt)")
                        .font(.sBody.weight(.semibold))
                    if a.kept == true {
                        StatusChip(text: "Kept", tone: "success")
                    }
                }
                if let when = a.when, !when.isEmpty {
                    Text(when)
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer(minLength: 8)
            if let s = a.score {
                Text(s)
                    .font(.sBody.weight(.semibold).monospacedDigit())
                    .foregroundStyle(a.kept == true ? .primary : .secondary)
            } else if let why = a.why, !why.isEmpty {
                Text(why)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.trailing)
            }
        }
        .accessibilityElement(children: .combine)
    }

    /// Just the buttons, floating in glass at the foot: an attempt under way, Resume; a finished one with its feedback
    /// and another attempt left, Take Quiz and See Feedback side by side; with none left, See Feedback alone. Return
    /// presses the first.
    private var actions: some View {
        let feedback = intro.last?.feedback == true && !intro.begin.hasPrefix("Continue")
        return QuizFloatingBar {
            Spacer(minLength: 0)
            if feedback && intro.canStart {
                Button("See Feedback", action: openLastFeedback)
                    .glassButton()
                Button(takeTitle, action: startAttempt)
                    .glassButton(prominent: true)
                    .keyboardShortcut(.defaultAction)
                    .disabled(codeMissing)
            } else if feedback {
                Button(action: openLastFeedback) { Label("See Feedback", systemImage: "text.bubble") }
                    .glassButton(prominent: true)
                    .keyboardShortcut(.defaultAction)
            } else {
                Button(beginTitle, action: startAttempt)
                    .glassButton(prominent: true)
                    .keyboardShortcut(.defaultAction)
                    .disabled(!intro.canStart || codeMissing)
            }
        }
    }

    private var takeTitle: String { codeMissing ? "Enter the Code" : (intro.survey ? "Take Survey" : "Take Quiz") }
    private var beginTitle: String { codeMissing && intro.canStart ? "Enter the Access Code" : intro.begin }
    private var codeMissing: Bool { intro.needsCode && run.code.trimmingCharacters(in: .whitespaces).isEmpty }

    private func startAttempt() {
        guard intro.canStart, !codeMissing else { return }
        Task {
            guard run.stage == .intro else { return } // (Return in the code field and the default button both ask: one begins)
            await run.begin()
        }
    }

    private func openLastFeedback() {
        guard let last = intro.last else { return }
        Task { await run.openFeedback(last.attempt) }
    }

    private static func tone(_ t: String) -> Color {
        switch t {
        case "orange": return .orange
        case "green": return .green
        default: return .secondary
        }
    }
}

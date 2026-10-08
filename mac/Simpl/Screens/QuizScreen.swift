import AppKit
import Combine
import PhotosUI
import SwiftUI
import UniformTypeIdentifiers

/// A Classic Quiz taken in the app, in a sheet over the window: the intro (its rules, the access code, Begin or
/// Resume), the attempt one question at a time (the strip of every question to move between them, flags, the clock,
/// every kind of question answered in place), the review before handing in, the receipt, and the feedback on a
/// finished attempt. Answers save as they are given; nothing is handed in unless the student says so. In an attempt
/// ← and → move between the questions, 1–9 pick an answer, Return goes on, ⌘Return submits and Escape closes.
struct QuizScreen: View {
    let launch: QuizLaunch
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var run: QuizRun
    @State private var confirmLeave = false
    @State private var confirmSubmit = false
    @State private var instructions = false
    private let clock = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    init(launch: QuizLaunch) {
        self.launch = launch
        _run = StateObject(wrappedValue: QuizRun(course: launch.course, quiz: launch.quiz))
    }

    private var tint: Color { Color(hex: run.attempt?.color ?? run.intro?.color ?? "#0a84ff") }
    private var inAttempt: Bool { [.taking, .review, .submitting].contains(run.stage) }

    var body: some View {
        VStack(spacing: 0) {
            topBar
            Divider()
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .animation(Motion.gentle, value: run.stage) // (each step — the intro, starting, the attempt, the review, the receipt, the feedback — crossfades, never cuts)
                .overlay(alignment: .top) {
                    banner.animation(Motion.gentle, value: run.banner) // (outside the if: it plays on the way in and out)
                }
        }
        .frame(minWidth: 780, idealWidth: 920, minHeight: 620, idealHeight: 780)
        .background(Theme.page)
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

    /// The sheet's own bar: Close (and Back, in the feedback), the quiz and where it stands, and during an attempt the
    /// clock, the instructions and Submit; the quiz on Canvas in its menu.
    private var topBar: some View {
        HStack(spacing: 12) {
            if run.stage == .feedback {
                Button { run.leaveFeedback() } label: { Label("Back", systemImage: "chevron.left") }
                    .help("Back")
            }
            Button("Close", action: close)
                .keyboardShortcut(.cancelAction)
                .disabled(run.stage == .submitting)
                .help(inAttempt ? "Save your answers and close — the attempt stays open" : "Close the quiz")
            IconTile(symbol: "checklist", color: tint, size: 26)
            VStack(alignment: .leading, spacing: 1) {
                Text(title)
                    .font(.headline)
                    .lineLimit(1)
                if !subtitle.isEmpty {
                    Text(subtitle)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 8)
            if inAttempt {
                QuizClock(run: run)
                Button { instructions.toggle() } label: { Label("Instructions", systemImage: "book") }
                    .labelStyle(.iconOnly)
                    .buttonStyle(.borderless)
                    .help("Instructions")
                    .popover(isPresented: $instructions, arrowEdge: .bottom) {
                        QuizInstructionsPopover(html: run.attempt?.html ?? run.intro?.html ?? "")
                            .environmentObject(engine)
                    }
            }
            moreMenu
            if run.stage == .taking {
                Button("Submit…", action: askSubmit)
                    .keyboardShortcut(.return, modifiers: .command)
                    .help("Submit this attempt (⌘Return)")
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(.bar)
    }

    private var moreMenu: some View {
        Menu {
            if run.stage == .taking {
                Button { Task { await run.toReview() } } label: { Label("Review Answers", systemImage: "list.bullet.rectangle") }
                Divider()
            }
            Button(action: openCanvas) { Label("Open in Canvas", systemImage: "globe") }
            Button(action: copyLink) { Label("Copy Link", systemImage: "link") }
        } label: {
            Image(systemName: "ellipsis.circle")
        }
        .menuStyle(.button)
        .buttonStyle(.borderless)
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
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .transition(.opacity)
        case .taking:
            QuizTakePane(run: run, tint: tint, openCanvas: openCanvas)
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

    /// The review is a panel arriving over the attempt; under Reduce Motion it only fades.
    private var reviewTransition: AnyTransition {
        reduceMotion ? .opacity : .scale(scale: 0.98).combined(with: .opacity)
    }

    /// A save that failed, a warning of the clock: a capsule at the top, gone by itself (or with a click).
    @ViewBuilder
    private var banner: some View {
        if let b = run.banner {
            Label(b.text, systemImage: b.error ? "exclamationmark.triangle.fill" : "clock.fill")
                .font(.callout.weight(.semibold))
                .foregroundStyle(b.error ? Color.white : Color.primary)
                .padding(.horizontal, 16)
                .padding(.vertical, 9)
                .background(b.error ? AnyShapeStyle(Color.red) : AnyShapeStyle(.regularMaterial), in: Capsule())
                .overlay(Capsule().strokeBorder(Theme.edge))
                .shadow(color: Theme.shadow, radius: 12, y: 4)
                .padding(.top, 12)
                .transition(bannerTransition)
                .onTapGesture { run.banner = nil }
                .help("Click to dismiss")
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

// MARK: - The clock and the instructions

/// The attempt's clock in the bar: the time left on a timed attempt (red in its last five minutes), or the time taken
/// so far. It ticks by itself, so the rest of the sheet is not drawn again every second.
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
                    .font(.callout.weight(.semibold).monospacedDigit())
                    .foregroundStyle(low ? Color.red : Color.primary)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
                    .background(low ? Color.red.opacity(0.14) : Theme.well, in: Capsule())
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
                .font(.headline)
                .padding(.horizontal, 18)
                .padding(.top, 14)
                .padding(.bottom, 10)
            Divider()
            ScrollView {
                Group {
                    if html.isEmpty {
                        Text("This quiz came with no instructions.").foregroundStyle(.secondary)
                    } else {
                        RichText(html: html)
                    }
                }
                .padding(18)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(width: 460, height: 420)
    }
}

// MARK: - Intro

/// A quiz before an attempt: what it is, its instructions, its rules (the attempts, the time limit, one question at a
/// time), the access code when it wants one, why it cannot begin, the last attempt, and the one thing to do next.
private struct QuizIntroPane: View {
    @ObservedObject var run: QuizRun
    let intro: QuizIntro
    let tint: Color
    @FocusState private var codeFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    heading
                    if !intro.html.isEmpty {
                        CardSection(title: "Before You Start") {
                            RichText(html: intro.html)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 4)
                        }
                    }
                    if !intro.rules.isEmpty { rulesCard }
                    if intro.needsCode { codeCard }
                    if let why = run.refused {
                        notice(why, symbol: "exclamationmark.triangle.fill", color: .red, tinted: true)
                    }
                    if let lock = intro.lockText, !lock.isEmpty {
                        notice(lock, symbol: "lock.fill", color: .secondary, tinted: false)
                    }
                    if let last = intro.last { lastAttempt(last) }
                }
                .frame(maxWidth: 720, alignment: .leading)
                .padding(.horizontal, 32)
                .padding(.vertical, 24)
                .frame(maxWidth: .infinity)
            }
            Divider()
            actions
        }
        .task {
            // (a quiz that wants a code: its field ready to type in, once the sheet is up)
            try? await Task.sleep(nanoseconds: 120_000_000)
            if intro.needsCode && run.code.isEmpty { codeFocused = true }
        }
    }

    private var heading: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                IconTile(symbol: "checklist", color: tint, size: 26)
                Text(intro.context ?? "Quiz")
                    .font(.callout.weight(.semibold))
                    .foregroundStyle(tint)
                    .lineLimit(1)
            }
            Text(intro.title)
                .font(.system(size: 26, weight: .bold))
                .tracking(-0.3)
                .textSelection(.enabled)
            if !intro.facts.isEmpty {
                Text(intro.facts.joined(separator: " · "))
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            if let note = intro.note, !note.isEmpty {
                Text(note)
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    private var rulesCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            ForEach(intro.rules, id: \.self) { r in
                HStack(alignment: .firstTextBaseline, spacing: 10) {
                    Image(systemName: r.symbol)
                        .foregroundStyle(Self.tone(r.tint))
                        .frame(width: 20)
                        .accessibilityHidden(true)
                    Text(r.text)
                        .font(.callout)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    private var codeCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            CardHeading(text: "Access Code")
            TextField("Access code", text: $run.code)
                .textFieldStyle(.roundedBorder)
                .autocorrectionDisabled()
                .focused($codeFocused)
                .onSubmit(startAttempt)
                .frame(maxWidth: 320)
            Text("Your instructor gives the code. It stays with this attempt.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    private func notice(_ text: String, symbol: String, color: Color, tinted: Bool) -> some View {
        Label(text, systemImage: symbol)
            .font(.callout)
            .foregroundStyle(color)
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card(tint: tinted ? color : nil)
    }

    private func lastAttempt(_ last: QuizIntro.Last) -> some View {
        CardSection(title: "Your Last Attempt") {
            VStack(alignment: .leading, spacing: 2) {
                Text("Attempt \(last.attempt)").font(.body.weight(.semibold))
                if let s = last.score {
                    Text(s).font(.callout.monospacedDigit()).foregroundStyle(.secondary)
                } else if let why = last.why, !why.isEmpty {
                    Text(why).font(.callout).foregroundStyle(.secondary)
                }
            }
            .padding(.horizontal, 6)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    /// Just the buttons: an attempt under way, Resume; a finished one with its feedback and another attempt left, Take
    /// Quiz and See Feedback side by side; with none left, See Feedback alone. Return presses the first.
    private var actions: some View {
        let feedback = intro.last?.feedback == true && !intro.begin.hasPrefix("Continue")
        return HStack(spacing: 10) {
            Spacer()
            if feedback && intro.canStart {
                Button("See Feedback", action: openLastFeedback)
                Button(takeTitle, action: startAttempt)
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
                    .disabled(codeMissing)
            } else if feedback {
                Button(action: openLastFeedback) { Label("See Feedback", systemImage: "text.bubble") }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
            } else {
                Button(beginTitle, action: startAttempt)
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
                    .disabled(!intro.canStart || codeMissing)
            }
        }
        .controlSize(.large)
        .padding(.horizontal, 20)
        .padding(.vertical, 12)
        .background(.bar)
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

// MARK: - Taking

/// Where the keyboard is during an attempt: the attempt's own keys (held by the strip), or an answer being typed.
private enum QuizFocus: Hashable {
    case keys
    case field(String)
}

/// The attempt, one question at a time: the strip of every question at the top, the question in the middle (it slides
/// the way the student moves), and at the foot what is answered, whether it is saved, and Previous and Next. Keys: ←
/// and → move, 1–9 pick an answer, Return goes on (to the review after the last), F flags — none of them while an
/// answer is being typed.
private struct QuizTakePane: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let openCanvas: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @FocusState private var focus: QuizFocus?

    var body: some View {
        VStack(spacing: 0) {
            QuizStripView(run: run, tint: tint, jump: jump, review: review)
                .focusable(interactions: .edit) // (it takes the keys whether or not keyboard navigation is on)
                .focused($focus, equals: .keys)
                .focusEffectDisabled()
                .onKeyPress(phases: .down) { press in key(press) }
            Divider()
            ZStack {
                if let q = run.current {
                    QuizQuestionPage(run: run, q: q, tint: tint, focus: $focus, choose: { o in choose(q, o) }, flag: { flag(q) }, onReturn: next, openCanvas: openCanvas)
                        .opacity(run.moving != nil ? 0.5 : 1)
                        .allowsHitTesting(run.moving == nil) // (Canvas's own page is moving to the question: a beat, then it is here)
                        .animation(Motion.snappy, value: run.moving)
                        .id(q.id)
                        .transition(pageTransition)
                } else {
                    ContentUnavailableView("This quiz has no questions.", systemImage: "questionmark.circle")
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .clipped()
            .animation(Motion.gentle, value: run.idx)
            Divider()
            footer
        }
        .defaultFocus($focus, .keys)
        .task {
            try? await Task.sleep(nanoseconds: 80_000_000) // (once the sheet's window is key)
            if focus == nil { focus = .keys }
        }
        .onChange(of: run.idx) { _, _ in focus = .keys }
    }

    /// The page goes the way the student went: on, out to the left with the next coming in from the right; back, the
    /// other way. Under Reduce Motion, a cross-fade.
    private var pageTransition: AnyTransition {
        reduceMotion ? .opacity : .push(from: run.forward ? .trailing : .leading)
    }

    private var typing: Bool {
        if case .some(.field) = focus { return true }
        return false
    }

    private func key(_ press: KeyPress) -> KeyPress.Result {
        guard !typing, press.modifiers.isDisjoint(with: [.command, .control, .option]) else { return .ignored }
        switch press.key {
        case .leftArrow:
            back()
            return .handled
        case .rightArrow, .return:
            next()
            return .handled
        default:
            break
        }
        guard let q = run.current, run.moving == nil else { return .ignored }
        let c = press.characters.lowercased()
        if c == "f", q.kind != "info", q.kind != "pending" {
            flag(q)
            return .handled
        }
        if let n = Int(c), (1...9).contains(n), q.kind == "choice" || q.kind == "multi", q.options.indices.contains(n - 1) {
            choose(q, q.options[n - 1])
            return .handled
        }
        return .ignored
    }

    /// On to the next question, or — from the last — to the review.
    private func next() {
        focus = .keys
        guard run.moving == nil else { return }
        if run.isLast {
            Task { await run.toReview() }
        } else {
            Task { await run.next() }
        }
    }

    private func back() {
        focus = .keys
        guard run.canGoBack, run.moving == nil else { return }
        Task { await run.back() }
    }

    private func jump(_ k: Int) {
        focus = .keys
        Task { await run.go(to: k) }
    }

    private func review() {
        Task { await run.toReview() }
    }

    /// An option clicked or its number pressed: one answer picked, or a box ticked or cleared. Saved at once.
    private func choose(_ q: QuizQuestion, _ o: QuizQuestion.Option) {
        focus = .keys
        let multi = q.kind == "multi"
        withAnimation(Motion.snappy) {
            run.set(q.id) { x in
                if multi {
                    if let i = x.picks.firstIndex(of: o.id) { x.picks.remove(at: i) } else { x.picks.append(o.id) }
                } else {
                    x.pick = o.id
                }
            }
        }
    }

    private func flag(_ q: QuizQuestion) {
        focus = .keys
        Task { await run.flag(q.id) }
    }

    private var footer: some View {
        HStack(spacing: 10) {
            HStack(spacing: 6) {
                Text("\(run.answeredCount) of \(run.questions.count) answered")
                    .contentTransition(.numericText(value: Double(run.answeredCount)))
                    .animation(Motion.snappy, value: run.answeredCount)
                if !run.saveWord.isEmpty { Text("· \(run.saveWord)") }
                if run.moving != nil { ProgressView().controlSize(.small) }
            }
            .font(.callout.monospacedDigit())
            .foregroundStyle(.secondary)
            Spacer()
            Button(action: back) { Label("Previous", systemImage: "chevron.left") }
                .disabled(!run.canGoBack || run.moving != nil)
                .help("Previous question (←)")
            Button(action: next) {
                HStack(spacing: 5) {
                    Text(run.isLast ? "Review Answers" : "Next")
                    Image(systemName: run.isLast ? "list.bullet.rectangle" : "chevron.right")
                }
            }
            .buttonStyle(.borderedProminent)
            .disabled(run.moving != nil)
            .help(run.isLast ? "Review your answers (Return)" : "Next question (Return or →)")
        }
        .controlSize(.large)
        .padding(.horizontal, 20)
        .padding(.vertical, 10)
        .background(.bar)
    }
}

/// Every question as a numbered chip: answered (filled), flagged (an orange flag), the one showing (ringed), one sealed
/// behind the student (no going back) dimmed. A click goes to it; Review, at the end, goes to the review.
private struct QuizStripView: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let jump: (Int) -> Void
    let review: () -> Void

    var body: some View {
        HStack(spacing: 0) {
            ScrollViewReader { proxy in
                ScrollView(.horizontal) {
                    HStack(spacing: 6) {
                        ForEach(Array(run.questions.enumerated()), id: \.element.id) { k, q in
                            chip(q, k).id(k)
                        }
                    }
                    .padding(.horizontal, 16)
                    .padding(.vertical, 10)
                }
                .scrollIndicators(.never)
                .onAppear { proxy.scrollTo(run.idx, anchor: .center) }
                .onChange(of: run.idx) { _, k in
                    withAnimation(Motion.gentle) { proxy.scrollTo(k, anchor: .center) }
                }
            }
            Divider().frame(height: 24)
            Button(action: review) { Label("Review", systemImage: "list.bullet.rectangle") }
                .buttonStyle(.borderless)
                .disabled(run.moving != nil)
                .help("Review every answer before submitting")
                .padding(.horizontal, 14)
        }
        .background(.bar)
    }

    private func chip(_ q: QuizQuestion, _ k: Int) -> some View {
        let current = k == run.idx
        let sealed = run.attempt?.noBack == true && k < run.idx
        let done = q.isAnswered && q.kind != "info"
        let ink: Color = done ? .white : (current ? tint : .primary)
        let fill: Color = done ? tint : Theme.well
        let ring: Color = current ? (done ? Color.primary.opacity(0.55) : tint) : .clear
        return Button { jump(k) } label: {
            Text("\(k + 1)")
                .font(.callout.weight(.semibold).monospacedDigit())
                .frame(width: 32, height: 32)
                .foregroundStyle(ink)
                .background(Circle().fill(fill))
                .overlay(Circle().strokeBorder(ring, lineWidth: 2))
                .overlay(alignment: .topTrailing) {
                    if q.flagged {
                        Image(systemName: "flag.fill")
                            .font(.system(size: 8, weight: .bold))
                            .foregroundStyle(.white)
                            .padding(3)
                            .background(Circle().fill(.orange))
                            .offset(x: 4, y: -4)
                            .transition(.scale(scale: 0.5).combined(with: .opacity))
                    }
                }
                .overlay {
                    if run.moving == k { ProgressView().controlSize(.small) }
                }
                .opacity(sealed ? 0.35 : 1)
                .animation(Motion.snappy, value: done)
        }
        .buttonStyle(QuizChipStyle())
        .disabled(sealed || current || run.moving != nil)
        .help(chipHelp(q, k, sealed: sealed))
        .accessibilityLabel("Question \(k + 1)\(done ? ", answered" : "")\(q.flagged ? ", flagged" : "")")
    }

    private func chipHelp(_ q: QuizQuestion, _ k: Int, sealed: Bool) -> String {
        var s = "Question \(k + 1)"
        if sealed {
            s += " — sealed"
        } else if q.kind == "info" {
            s += " — information only"
        } else {
            s += q.isAnswered ? " — answered" : " — not answered"
        }
        if q.flagged { s += ", flagged" }
        return s
    }
}

/// A chip of the strip as a button: it grows a little under the pointer and gives under a click (under Reduce Motion
/// the click dims it instead).
private struct QuizChipStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        QuizChipBody(configuration: configuration)
    }
}

/// The body of a chip as a button (its own view, for its hover).
private struct QuizChipBody: View {
    let configuration: ButtonStyleConfiguration
    @State private var hover = false
    @Environment(\.isEnabled) private var enabled
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var scale: CGFloat {
        if reduceMotion { return 1 }
        if configuration.isPressed { return 0.92 }
        return hover && enabled ? 1.08 : 1
    }

    var body: some View {
        configuration.label
            .scaleEffect(scale)
            .opacity(reduceMotion && configuration.isPressed ? 0.7 : 1)
            .animation(Motion.hover, value: hover)
            .animation(Motion.snappy, value: configuration.isPressed)
            .onHover { hover = $0 }
    }
}

/// One question: its number and points, the flag, its words (Canvas's own, pictures and formulas included), and its
/// answer in the shape its kind takes — a row per choice, a box per answer that applies, a field, a menu per match or
/// blank, a file chosen or dropped, or Canvas's own page for a kind it answers there.
private struct QuizQuestionPage: View {
    @ObservedObject var run: QuizRun
    let q: QuizQuestion
    let tint: Color
    var focus: FocusState<QuizFocus?>.Binding
    /// An option clicked: picked, or ticked or cleared.
    let choose: (QuizQuestion.Option) -> Void
    let flag: () -> Void
    /// Return in a one-line answer: on to the next question.
    let onReturn: () -> Void
    let openCanvas: () -> Void
    @State private var importing = false
    @State private var photos: [PhotosPickerItem] = []
    @State private var dropping = false

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                header
                if q.kind == "pending" {
                    ProgressView("Loading the question…")
                        .frame(maxWidth: .infinity, minHeight: 140)
                } else {
                    RichText(html: q.html)
                    answer
                    if !q.hint.isEmpty {
                        Label(q.hint, systemImage: "info.circle")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .padding(24)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card()
            .frame(maxWidth: 780)
            .padding(.horizontal, 28)
            .padding(.vertical, 22)
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
            .onTapGesture { focus.wrappedValue = .keys } // (a click off the answers: the keys are the attempt's again)
        }
        .background(Theme.page)
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Question \(q.n)")
                    .font(.title2.weight(.bold))
                Text(ofLine)
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            if q.kind != "info" && q.kind != "pending" {
                Toggle(isOn: Binding(get: { q.flagged }, set: { _ in flag() })) {
                    Label {
                        Text(q.flagged ? "Flagged" : "Flag")
                    } icon: {
                        Image(systemName: q.flagged ? "flag.fill" : "flag")
                            .foregroundStyle(q.flagged ? Color.orange : Color.secondary)
                    }
                }
                .toggleStyle(.button)
                .help(q.flagged ? "Remove the flag (F)" : "Flag this question to come back to it (F)")
                .accessibilityLabel(q.flagged ? "Flagged for review" : "Flag for review")
            }
        }
    }

    private var ofLine: String {
        let total = run.questions.count
        guard let p = q.points else { return "of \(total)" }
        return "of \(total) · \(QuizFormat.points(p)) \(p == 1 ? "point" : "points")"
    }

    @ViewBuilder
    private var answer: some View {
        switch q.kind {
        case "choice": choices
        case "multi": multiple
        case "text": line(numeric: false)
        case "number": line(numeric: true)
        case "essay": essay
        case "match": matching
        case "drops", "blanks": blanks
        case "file": file
        case "info": EmptyView()
        default: elsewhere
        }
    }

    // one answer: a row per option with its radio, the whole row the target; its number on the keyboard picks it
    private var choices: some View {
        VStack(spacing: 6) {
            ForEach(Array(q.options.enumerated()), id: \.element.id) { k, o in
                let on = q.pick == o.id
                Button { choose(o) } label: {
                    QuizOptionRow(letter: o.letter, text: o.text, html: o.html, number: k < 9 ? k + 1 : nil, on: on, tint: tint) {
                        QuizRadio(on: on, tint: tint)
                    }
                }
                .buttonStyle(QuizChoiceStyle(on: on, tint: tint))
                .accessibilityLabel(QuizFormat.optionName(o))
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
    }

    // every answer that applies: a row per option with its box, the whole row the target
    private var multiple: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(Array(q.options.enumerated()), id: \.element.id) { k, o in
                let on = q.picks.contains(o.id)
                let ticked = Binding(get: { on }, set: { v in if v != on { choose(o) } })
                Button { choose(o) } label: {
                    QuizOptionRow(letter: o.letter, text: o.text, html: o.html, number: k < 9 ? k + 1 : nil, on: on, tint: tint) {
                        Toggle("", isOn: ticked)
                            .toggleStyle(.checkbox)
                            .labelsHidden()
                            .allowsHitTesting(false) // (the row is the target: one click, one tick)
                    }
                }
                .buttonStyle(QuizChoiceStyle(on: on, tint: tint))
                .accessibilityRepresentation {
                    Toggle(QuizFormat.optionName(o), isOn: ticked)
                }
            }
            Text("Pick every answer that applies.")
                .font(.footnote)
                .foregroundStyle(.secondary)
        }
    }

    private func line(numeric: Bool) -> some View {
        TextField(numeric ? "Number" : "Your answer", text: Binding(get: { q.text }, set: { v in run.set(q.id, typed: true) { $0.text = v } }))
            .textFieldStyle(.roundedBorder)
            .controlSize(.large)
            .font(.body.monospacedDigit())
            .autocorrectionDisabled(numeric)
            .focused(focus, equals: .field("\(q.id)#line"))
            .onSubmit(onReturn)
            .frame(maxWidth: numeric ? CGFloat(280) : CGFloat.infinity, alignment: .leading)
    }

    private var essay: some View {
        VStack(alignment: .leading, spacing: 6) {
            TextEditor(text: Binding(get: { q.text }, set: { v in run.set(q.id, typed: true) { $0.text = v } }))
                .font(.body)
                .scrollContentBackground(.hidden)
                .focused(focus, equals: .field("\(q.id)#essay"))
                .padding(8)
                .frame(height: 260)
                .background(Color(nsColor: .textBackgroundColor), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Color.secondary.opacity(0.3)))
            Text("A blank line starts a new paragraph; lines starting with • or 1. become a list.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    // matching: each item with a menu of the matches beside it
    private var matching: some View {
        VStack(spacing: 8) {
            ForEach(q.options) { o in
                HStack(spacing: 14) {
                    QuizOptionText(text: o.text, html: o.html)
                    Picker(QuizFormat.optionName(o), selection: Binding(get: { q.map[o.id] }, set: { v in run.set(q.id) { $0.map[o.id] = v } })) {
                        Text("Choose…").tag(String?.none)
                        ForEach(q.matches) { m in
                            Text(m.text).tag(Optional(m.id))
                        }
                    }
                    .pickerStyle(.menu)
                    .labelsHidden()
                    .frame(width: 260)
                }
                .padding(12)
                .background(Theme.well, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            }
        }
    }

    // one field per blank, numbered as its mark in the question's words: a menu to pick from, or a line to type
    private var blanks: some View {
        VStack(alignment: .leading, spacing: 10) {
            ForEach(q.blanks) { b in
                HStack(spacing: 12) {
                    Text("\(b.n)")
                        .font(.callout.weight(.bold).monospacedDigit())
                        .foregroundStyle(tint)
                        .frame(minWidth: 26, minHeight: 26)
                        .background(tint.opacity(0.16), in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                        .accessibilityHidden(true)
                    if q.kind == "drops" {
                        Picker(b.label, selection: Binding(get: { q.map[b.id] }, set: { v in run.set(q.id) { $0.map[b.id] = v } })) {
                            Text(b.label.hasPrefix("Blank ") ? "Choose…" : b.label).tag(String?.none)
                            ForEach(b.options) { c in
                                Text(c.text).tag(Optional(c.id))
                            }
                        }
                        .pickerStyle(.menu)
                        .labelsHidden()
                        .frame(maxWidth: 320, alignment: .leading)
                    } else {
                        TextField(b.label.hasPrefix("Blank ") ? "Your answer" : b.label, text: Binding(get: { q.map[b.id] ?? "" }, set: { v in run.set(q.id, typed: true) { $0.map[b.id] = v } }))
                            .textFieldStyle(.roundedBorder)
                            .focused(focus, equals: .field("\(q.id)#\(b.id)"))
                            .onSubmit(onReturn)
                            .frame(maxWidth: 360)
                    }
                    Spacer(minLength: 0)
                }
            }
        }
    }

    // a file: chosen in the Open panel, dragged in from the Finder, or picked from Photos; uploaded to the student's own
    // quiz files on Canvas, then replaced or removed
    private var file: some View {
        VStack(alignment: .leading, spacing: 10) {
            if run.uploading == q.id {
                HStack(spacing: 10) {
                    ProgressView().controlSize(.small)
                    Text("Uploading…").foregroundStyle(.secondary)
                }
                .padding(14)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Theme.well, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            } else if let f = q.files.first {
                HStack(spacing: 10) {
                    Image(systemName: "doc.fill").foregroundStyle(tint)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(f.name).lineLimit(1)
                        Text("Handed in with this attempt when you submit.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("Remove", role: .destructive) { run.set(q.id) { $0.files = [] } }
                }
                .padding(14)
                .background(Theme.well, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            }
            VStack(spacing: 10) {
                Image(systemName: "arrow.down.doc")
                    .font(.system(size: 26))
                    .foregroundStyle(dropping ? tint : Color.secondary)
                    .accessibilityHidden(true)
                Text(dropWords)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                HStack(spacing: 10) {
                    Button { importing = true } label: { Label(q.files.isEmpty ? "Choose File…" : "Replace…", systemImage: "folder") }
                    PhotosPicker(selection: $photos, maxSelectionCount: 1, matching: .any(of: [.images, .videos])) {
                        Label("Photo…", systemImage: "photo")
                    }
                }
                .disabled(run.uploading != nil)
            }
            .frame(maxWidth: .infinity)
            .padding(20)
            .background {
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .fill(dropping ? tint.opacity(0.1) : Color.clear)
            }
            .overlay {
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .strokeBorder(dropping ? tint : Color.secondary.opacity(0.35), style: StrokeStyle(lineWidth: 1.5, dash: [6, 4]))
            }
            .dropDestination(for: URL.self) { urls, _ in
                guard run.uploading == nil, let u = urls.first, u.isFileURL else { return false }
                take(u)
                return true
            } isTargeted: { on in
                withAnimation(Motion.snappy) { dropping = on }
            }
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.item], allowsMultipleSelection: false) { result in
            guard case .success(let urls) = result, let u = urls.first else { return }
            take(u)
        }
        .onChange(of: photos) {
            guard let item = photos.first else { return }
            photos = []
            Task {
                guard let d = try? await item.loadTransferable(type: Data.self) else {
                    run.say("The photo could not be read.", error: true)
                    return
                }
                let ut = item.supportedContentTypes.first(where: { $0.preferredFilenameExtension != nil }) ?? .jpeg
                await run.upload(q.id, PickedFile(name: "Photo.\(ut.preferredFilenameExtension ?? "jpg")", type: ut.preferredMIMEType ?? "image/jpeg", data: d))
            }
        }
    }

    private var dropWords: String {
        if dropping { return "Drop to upload it" }
        return q.files.isEmpty ? "Drag a file here, or" : "Drag another file here to replace it, or"
    }

    /// A file to hand in with this question (from the Open panel or the Finder): read, then uploaded.
    private func take(_ dropped: URL) {
        let u = (dropped as NSURL).filePathURL ?? dropped // (the Finder may hand over a file reference: its path, for its name)
        let scoped = u.startAccessingSecurityScopedResource()
        defer { if scoped { u.stopAccessingSecurityScopedResource() } }
        guard let d = try? Data(contentsOf: u) else {
            run.say("\(u.lastPathComponent) could not be read.", error: true)
            return
        }
        let mime = UTType(filenameExtension: u.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
        let picked = PickedFile(name: u.lastPathComponent, type: mime, data: d)
        Task { await run.upload(q.id, picked) }
    }

    // a kind Canvas adds that the app does not know yet: Canvas's own page answers it, on the same attempt
    private var elsewhere: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("This kind of question is answered on Canvas’s quiz page. Your other answers are already saved there.")
                .font(.callout)
                .foregroundStyle(.secondary)
            Button(action: openCanvas) { Label("Answer on Canvas", systemImage: "globe") }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Theme.well, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }
}

/// An option's row: its mark (a radio, a box), its letter, its words or Canvas's own content, and the number key that
/// picks it.
private struct QuizOptionRow<Mark: View>: View {
    let letter: String
    let text: String
    let html: String
    let number: Int?
    let on: Bool
    let tint: Color
    @ViewBuilder var mark: () -> Mark

    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            mark()
            Text(letter)
                .font(.callout.weight(.semibold))
                .foregroundStyle(on ? tint : Color.secondary)
                .frame(minWidth: 16)
            QuizOptionText(text: text, html: html)
            if let number {
                Text("\(number)")
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(.tertiary)
                    .padding(.horizontal, 5)
                    .padding(.vertical, 1)
                    .overlay(RoundedRectangle(cornerRadius: 4, style: .continuous).strokeBorder(Color.secondary.opacity(0.35)))
                    .help("Press \(number) to choose this answer")
                    .accessibilityHidden(true)
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentShape(Rectangle())
    }
}

/// A radio as the Mac draws one: an empty ring, or filled with the colour and a dot in its middle once picked.
private struct QuizRadio: View {
    let on: Bool
    let tint: Color

    var body: some View {
        ZStack {
            Circle().fill(on ? tint : Color(nsColor: .controlBackgroundColor))
            Circle().strokeBorder(on ? tint : Color.secondary.opacity(0.55), lineWidth: 1)
            if on {
                Circle()
                    .fill(Color.white)
                    .frame(width: 6, height: 6)
                    .transition(.scale(scale: 0.3).combined(with: .opacity))
            }
        }
        .frame(width: 16, height: 16)
        .animation(Motion.snappy, value: on)
        .accessibilityHidden(true)
    }
}

/// An option's row as a button: a wash under the pointer, a deeper one under a click, and once picked the colour's
/// wash with its edge.
private struct QuizChoiceStyle: ButtonStyle {
    let on: Bool
    let tint: Color

    func makeBody(configuration: Configuration) -> some View {
        QuizChoiceBody(configuration: configuration, on: on, tint: tint)
    }
}

/// The body of an option's row as a button (its own view, for its hover).
private struct QuizChoiceBody: View {
    let configuration: ButtonStyleConfiguration
    let on: Bool
    let tint: Color
    @State private var hover = false

    private var fill: Color {
        if on { return tint.opacity(configuration.isPressed ? 0.2 : 0.12) }
        if configuration.isPressed { return Color.primary.opacity(0.09) }
        return Color.primary.opacity(hover ? 0.05 : 0.025)
    }

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 10, style: .continuous)
        configuration.label
            .background(shape.fill(fill))
            .overlay(shape.strokeBorder(on ? tint : Theme.edge, lineWidth: on ? 1.5 : 1))
            .animation(Motion.hover, value: hover)
            .animation(Motion.snappy, value: on)
            .onHover { hover = $0 }
    }
}

/// An option's words, or Canvas's own content where it has none (a formula is an equation picture): a click on it is
/// the row's.
private struct QuizOptionText: View {
    let text: String
    let html: String

    var body: some View {
        if !html.isEmpty && (text.isEmpty || html.contains("<img")) {
            RichText(html: html)
                .allowsHitTesting(false)
                .frame(maxWidth: .infinity, alignment: .leading)
        } else {
            Text(text.isEmpty ? "—" : text)
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

// MARK: - Review

/// The attempt before it is handed in: every question with its answer in a line (or that it is blank, or flagged), a
/// click on one to change it, Back to Questions, and Submit (⌘Return).
private struct QuizReviewPane: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let submit: () -> Void

    var body: some View {
        let blank = run.questions.count - run.answeredCount
        let noBack = run.attempt?.noBack == true
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    ScreenHeading(title: "Review", sub: "\(run.answeredCount) of \(run.questions.count) answered · \(blank == 0 ? "nothing left blank" : "\(blank) left blank")")
                    VStack(spacing: 0) {
                        ForEach(Array(run.questions.enumerated()), id: \.element.id) { k, q in
                            if k > 0 { RowDivider(inset: 48) }
                            if noBack {
                                row(q)
                                    .padding(.horizontal, 8)
                                    .padding(.vertical, 7)
                            } else {
                                RowLink { Task { await run.keepWorking(at: k) } } label: { row(q) }
                                    .help("Change your answer to question \(q.n)")
                            }
                        }
                    }
                    .padding(8)
                    .card()
                    Text(noBack ? "This quiz seals each question once you leave it." : "Click a question to change it. Submitting ends the attempt; blank questions are graded as incorrect.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: 780, alignment: .leading)
                .padding(.horizontal, 28)
                .padding(.vertical, 22)
                .frame(maxWidth: .infinity)
            }
            Divider()
            footer
        }
    }

    private func row(_ q: QuizQuestion) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(q.n)")
                .font(.callout.weight(.bold).monospacedDigit())
                .frame(width: 28, height: 28)
                .foregroundStyle(q.isAnswered ? Color.white : Color.secondary)
                .background(Circle().fill(q.isAnswered ? tint : Theme.well))
            VStack(alignment: .leading, spacing: 3) {
                Text(q.plain).lineLimit(2)
                if q.kind == "info" {
                    Text("Information only").font(.callout).foregroundStyle(.secondary)
                } else if let s = q.summary {
                    Text(s).font(.callout.weight(.medium)).foregroundStyle(tint).lineLimit(2)
                } else {
                    Text("Not answered").font(.callout.weight(.semibold)).foregroundStyle(.red)
                }
            }
            Spacer(minLength: 4)
            if q.flagged {
                Image(systemName: "flag.fill")
                    .font(.callout)
                    .foregroundStyle(.orange)
                    .help("Flagged")
                    .accessibilityLabel("Flagged")
            }
        }
        .accessibilityElement(children: .combine)
    }

    private var footer: some View {
        HStack(spacing: 10) {
            if run.stage == .submitting {
                ProgressView().controlSize(.small)
                Text("Submitting…").foregroundStyle(.secondary)
            }
            Spacer()
            Button { Task { await run.keepWorking() } } label: { Label("Back to Questions", systemImage: "chevron.left") }
                .disabled(run.stage == .submitting)
                .help("Back to the question you were on")
            Button(action: submit) { Label(run.attempt?.survey == true ? "Submit Survey…" : "Submit Quiz…", systemImage: "paperplane") }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.return, modifiers: .command)
                .disabled(run.stage == .submitting)
                .help("Submit this attempt (⌘Return)")
        }
        .controlSize(.large)
        .padding(.horizontal, 20)
        .padding(.vertical, 10)
        .background(.bar)
    }
}

// MARK: - Receipt

/// The attempt handed in: the tick, what Canvas says of it (the questions answered, the score, the points for taking
/// part), and Done or the feedback. Its pieces arrive one after another, once.
private struct QuizReceiptPane: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let done: () -> Void
    @State private var shown = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let r = run.receipt
        VStack(spacing: 0) {
            ScrollView {
                VStack(spacing: 16) {
                    Image(systemName: "checkmark.circle.fill")
                        .font(.system(size: 64, weight: .semibold))
                        .foregroundStyle(.green)
                        .symbolEffect(.bounce, value: reduceMotion ? false : shown) // (under Reduce Motion the tick only fades in)
                        .quizArrive(0, shown)
                        .padding(.top, 40)
                        .accessibilityHidden(true)
                    Text(r?.title ?? "Attempt Submitted")
                        .font(.system(size: 26, weight: .bold))
                        .multilineTextAlignment(.center)
                        .quizArrive(1, shown)
                    if let lead = r?.lead, !lead.isEmpty {
                        Text(lead)
                            .font(.callout)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)
                            .frame(maxWidth: 460)
                            .quizArrive(2, shown)
                    }
                    VStack(spacing: 0) {
                        fact("Questions answered", r?.answered ?? "")
                        if let s = r?.score {
                            RowDivider(inset: 16)
                            fact("Score", s)
                        }
                        if let t = r?.takingPart {
                            RowDivider(inset: 16)
                            fact("For taking part", t)
                        }
                    }
                    .card()
                    .frame(maxWidth: 440)
                    .padding(.top, 6)
                    .quizArrive(3, shown)
                }
                .padding(.horizontal, 32)
                .padding(.bottom, 24)
                .frame(maxWidth: .infinity)
            }
            Divider()
            HStack(spacing: 10) {
                Spacer()
                if r?.feedback == true {
                    Button("Done", action: done)
                    Button("See Feedback") { Task { await run.openFeedback(r?.attempt) } }
                        .buttonStyle(.borderedProminent)
                        .keyboardShortcut(.defaultAction)
                } else {
                    Button("Done", action: done)
                        .buttonStyle(.borderedProminent)
                        .keyboardShortcut(.defaultAction)
                }
            }
            .controlSize(.large)
            .padding(.horizontal, 20)
            .padding(.vertical, 12)
            .background(.bar)
        }
        .onAppear { shown = true }
    }

    private func fact(_ k: String, _ v: String) -> some View {
        HStack {
            Text(k).foregroundStyle(.secondary)
            Spacer()
            Text(v).font(.body.weight(.semibold).monospacedDigit())
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
    }
}

// MARK: - Feedback

/// A finished attempt question by question: the score as a ring, the attempt to show, a filter (all, to review,
/// correct), the instructor's comments, and each question with what was answered, the correct answer where the quiz
/// shows it, every option, and the worked solution.
private struct QuizFeedbackPane: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    @State private var filter = 0

    var body: some View {
        if let fb = run.feedback {
            if let why = fb.hidden {
                ContentUnavailableView {
                    Label("Results Not Released", systemImage: "eye.slash")
                } description: {
                    Text(why)
                }
            } else {
                list(fb)
            }
        } else {
            LoadState(error: run.feedbackError) { Task { await run.openFeedback(run.feedback?.attempt) } }
        }
    }

    private func list(_ fb: QuizFeedback) -> some View {
        let rows = fb.rows ?? []
        let toReview = rows.filter { $0.verdict == "wrong" || $0.verdict == "partial" }
        let correct = rows.filter { $0.verdict == "right" }
        let shown = filter == 1 ? toReview : (filter == 2 ? correct : rows)
        return ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                score(fb)
                if fb.released == true {
                    Picker("Show", selection: $filter.animation(Motion.gentle)) {
                        Text("All \(rows.count)").tag(0)
                        Text("To Review \(toReview.count)").tag(1)
                        Text("Correct \(correct.count)").tag(2)
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .frame(maxWidth: 440)
                } else {
                    Label("Canvas has not released the question results for this attempt yet — your score and the questions are shown as they stand.", systemImage: "info.circle")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }
                if let cs = fb.comments, !cs.isEmpty { comments(cs) }
                if shown.isEmpty {
                    EmptyNote(text: emptyText)
                        .padding(.horizontal, 16)
                        .card()
                }
                ForEach(shown) { r in
                    QuizFeedbackCard(row: r, tint: tint)
                }
            }
            .frame(maxWidth: 820, alignment: .leading)
            .padding(.horizontal, 28)
            .padding(.vertical, 22)
            .frame(maxWidth: .infinity)
        }
    }

    private var emptyText: String {
        switch filter {
        case 1: return "Nothing to review — every question was answered correctly."
        case 2: return "No question in this attempt was answered correctly."
        default: return "This attempt has no questions to show."
        }
    }

    private func score(_ fb: QuizFeedback) -> some View {
        let color = fb.color.map { Color(hex: $0) } ?? tint
        return HStack(spacing: 20) {
            ZStack {
                Ring(value: fb.pct, color: color, lineWidth: 8)
                Text(fb.pct.map { "\(Int($0.rounded()))%" } ?? "—")
                    .font(.headline.weight(.bold))
                    .foregroundStyle(color)
            }
            .frame(width: 88, height: 88)
            VStack(alignment: .leading, spacing: 4) {
                Text("\(fb.score ?? "—") / \(fb.possible ?? "—")")
                    .font(.title2.weight(.bold).monospacedDigit())
                Text("Attempt \(fb.attempt ?? 1)")
                    .font(.callout.weight(.semibold))
                if let s = fb.summary {
                    Text(s).font(.callout).foregroundStyle(.secondary)
                }
            }
            Spacer(minLength: 0)
            if let atts = fb.attempts, atts.count > 1 {
                Picker("Attempt", selection: Binding(get: { fb.attempt ?? 1 }, set: { a in Task { await run.openFeedback(a) } })) {
                    ForEach(atts, id: \.self) { a in
                        Text("Attempt \(a)").tag(a)
                    }
                }
                .pickerStyle(.menu)
                .labelsHidden()
                .fixedSize()
                .help("Show another attempt")
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    private func comments(_ cs: [QuizFeedback.Comment]) -> some View {
        CardSection(title: "Instructor Comments") {
            ForEach(Array(cs.enumerated()), id: \.offset) { k, c in
                if k > 0 { RowDivider(inset: 48) }
                HStack(alignment: .top, spacing: 10) {
                    PersonAvatar(name: c.author, size: 30)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(c.author).font(.callout.weight(.semibold))
                        Text(c.text).font(.callout).textSelection(.enabled)
                    }
                    Spacer(minLength: 0)
                }
                .padding(8)
            }
        }
    }
}

/// One question of a finished attempt: its verdict and points, its words, what was answered (and the correct answer
/// where the quiz shows it, a matching question as its pairs), every option on request, and the worked solution.
private struct QuizFeedbackCard: View {
    let row: QuizFeedback.Row
    let tint: Color
    @State private var showOptions = false

    private var ink: Color {
        switch row.verdict {
        case "right": return .green
        case "wrong": return .red
        case "partial": return .orange
        default: return .secondary
        }
    }

    private var mark: String {
        switch row.verdict {
        case "right": return "checkmark.circle.fill"
        case "wrong": return "xmark.circle.fill"
        case "partial": return "minus.circle.fill"
        default: return "questionmark.circle.fill"
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                Image(systemName: mark)
                    .font(.title3)
                    .foregroundStyle(ink)
                    .accessibilityHidden(true)
                Text("Question \(row.n)").font(.headline)
                Text(row.verdictText)
                    .font(.callout.weight(.semibold))
                    .foregroundStyle(ink)
                Spacer()
                if !row.score.isEmpty {
                    Text(row.score)
                        .font(.callout.weight(.semibold).monospacedDigit())
                        .foregroundStyle(ink)
                }
            }
            RichText(html: row.html)
            if let m = row.match {
                matchTable(m)
            } else {
                answerBox("Your Answer", row.yours, tinted: row.verdict != "none", color: ink, empty: "No answer")
                if !row.right.isEmpty {
                    answerBox("Correct Answer", row.right, tinted: true, color: .green, empty: "—")
                }
            }
            if let opts = row.options, !opts.isEmpty {
                DisclosureGroup(isExpanded: $showOptions.animation(Motion.gentle)) {
                    VStack(spacing: 8) {
                        ForEach(opts) { o in
                            HStack(spacing: 10) {
                                Text(o.letter)
                                    .font(.caption.weight(.bold))
                                    .frame(width: 24, height: 24)
                                    .background(Circle().fill(Theme.well))
                                QuizOptionText(text: o.text, html: o.html)
                                if o.mine { pill("Yours", tint) }
                                if o.right { pill("Correct", .green) }
                            }
                        }
                    }
                    .padding(.top, 8)
                } label: {
                    Text(showOptions ? "Hide the Options" : "Show All \(opts.count) Options")
                        .font(.callout.weight(.semibold))
                }
            }
            if let s = row.solution {
                VStack(alignment: .leading, spacing: 6) {
                    CardHeading(text: "Worked Solution")
                    if !s.html.isEmpty {
                        RichText(html: s.html)
                    } else {
                        Text(s.text).font(.callout).textSelection(.enabled)
                    }
                }
                .padding(12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Theme.well, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            } else if row.noSolution {
                Text("Your instructor left no worked solution for this question.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    private func pill(_ t: String, _ c: Color) -> some View {
        Text(t)
            .font(.caption.weight(.semibold))
            .foregroundStyle(c)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(c.opacity(0.14), in: Capsule())
    }

    private func answerBox(_ label: String, _ parts: [QuizFeedback.Part], tinted: Bool, color: Color, empty: String) -> some View {
        let wash = tinted && !row.essay
        return VStack(alignment: .leading, spacing: 6) {
            CardHeading(text: label)
            if parts.isEmpty {
                Text(empty).italic().font(.callout).foregroundStyle(.secondary)
            } else {
                ForEach(Array(parts.enumerated()), id: \.offset) { _, p in
                    if !p.text.isEmpty {
                        Text(p.text)
                            .font(.callout.weight(row.essay ? .regular : .semibold))
                            .foregroundStyle(wash ? color : Color.primary)
                            .textSelection(.enabled)
                    } else if !p.html.isEmpty {
                        RichText(html: p.html)
                    }
                }
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(wash ? color.opacity(0.12) : Theme.well, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }

    private func matchTable(_ m: QuizFeedback.MatchTable) -> some View {
        VStack(spacing: 8) {
            ForEach(Array(m.rows.enumerated()), id: \.offset) { _, r in
                VStack(alignment: .leading, spacing: 4) {
                    Text(r.left).font(.callout.weight(.semibold))
                    HStack(spacing: 6) {
                        Image(systemName: r.ok == true ? "checkmark" : (r.ok == false ? "xmark" : "arrow.right"))
                            .font(.caption.weight(.bold))
                            .foregroundStyle(matchInk(r.ok))
                        Text(r.mine ?? "No match set")
                            .font(.callout)
                            .foregroundStyle(r.mine == nil ? Color.secondary : Color.primary)
                    }
                    if m.showRight, let right = r.right, r.ok != true {
                        Text("Correct: \(right)")
                            .font(.footnote.weight(.medium))
                            .foregroundStyle(.green)
                    }
                }
                .padding(10)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(matchFill(r.ok), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            }
        }
    }

    private func matchInk(_ ok: Bool?) -> Color {
        if ok == true { return .green }
        if ok == false { return .red }
        return .secondary
    }

    private func matchFill(_ ok: Bool?) -> Color {
        if ok == true { return Color.green.opacity(0.1) }
        if ok == false { return Color.red.opacity(0.1) }
        return Theme.well
    }
}

// MARK: - Small pieces

/// How the quiz screen writes a number of points (2, 1.5) and names an option (for VoiceOver and the menus).
private enum QuizFormat {
    static func points(_ v: Double) -> String {
        guard v.isFinite else { return "—" }
        return v == v.rounded() && abs(v) < 1e15 ? String(Int(v)) : String(format: "%g", v)
    }

    static func optionName(_ o: QuizQuestion.Option) -> String {
        o.text.isEmpty ? "Option \(o.letter)" : "\(o.letter). \(o.text)"
    }
}

/// A rare moment's pieces arriving one after another (the attempt handed in): each rises a little and fades in, a
/// beat after the one before. Under Reduce Motion a plain fade, all at once. Never on an everyday screen.
private struct QuizArrive: ViewModifier {
    let index: Int
    let shown: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        content
            .opacity(shown ? 1 : 0)
            .offset(y: shown || reduceMotion ? 0 : 8)
            .animation(reduceMotion ? Motion.gentle : Motion.gentle.delay(0.06 * Double(index)), value: shown)
    }
}

private extension View {
    func quizArrive(_ index: Int, _ shown: Bool) -> some View { modifier(QuizArrive(index: index, shown: shown)) }
}

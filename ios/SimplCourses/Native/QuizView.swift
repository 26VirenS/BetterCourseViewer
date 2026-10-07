import PhotosUI
import SwiftUI
import UniformTypeIdentifiers

/// A Classic Quiz taken in the app, over everything else: the intro (its rules, the access code, Begin), the
/// attempt one question at a time (a numbered strip to move between them, flags, the clock, every kind of
/// question answered in place), the review before handing in, the receipt, and the feedback on a finished
/// attempt. Answers save as they are given; nothing is handed in unless the student says so.
struct QuizScreen: View {
    let launch: QuizLaunch
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @StateObject private var run: QuizRun
    @State private var confirmLeave = false
    @State private var confirmSubmit = false
    @State private var instructions = false
    @State private var now = Date()
    private let clock = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    init(launch: QuizLaunch) {
        self.launch = launch
        _run = StateObject(wrappedValue: QuizRun(course: launch.course, quiz: launch.quiz))
    }

    private var tint: Color { Color(hex: run.attempt?.color ?? run.intro?.color ?? "#0a84ff") }
    private var inAttempt: Bool { [.taking, .review, .submitting].contains(run.stage) }

    var body: some View {
        NavigationStack {
            content
                .animation(.spring(duration: 0.45, bounce: 0.1), value: run.stage == .review || run.stage == .submitting) // (the last question pulled up: the review rises in)
                .navigationTitle(title)
                .navigationBarTitleDisplayMode(.inline)
                .toolbar { toolbar }
                .overlay(alignment: .top) { banner }
        }
        .tint(tint)
        .task {
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
        .onReceive(clock) { t in
            now = t
            run.checkClock(t)
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
        .alert("Submit this attempt?", isPresented: $confirmSubmit) {
            Button("Submit") { Task { await run.submit() } }
            Button("Cancel", role: .cancel) {}
        } message: {
            let blank = run.questions.count - run.answeredCount
            Text(blank > 0 ? "\(blank) \(blank == 1 ? "question is" : "questions are") still blank. Submitting ends the attempt; blank questions are graded as incorrect." : "Submitting ends the attempt.")
        }
        .alert("Some answers were not kept", isPresented: Binding(get: { !run.missing.isEmpty }, set: { if !$0 { run.missing = [] } })) {
            Button("Submit Anyway", role: .destructive) {
                run.missing = []
                Task { await run.submit(force: true) }
            }
            Button("Go Back", role: .cancel) { run.missing = [] }
        } message: {
            Text("Canvas did not keep your \(run.missing.count == 1 ? "answer" : "answers") to \(run.missing.map { "question \($0)" }.joined(separator: ", ")). Go back to answer \(run.missing.count == 1 ? "it" : "them") again, or submit as it is.")
        }
        .alert("Access Code", isPresented: $run.askCode) {
            TextField("Access code", text: $run.code)
                .textInputAutocapitalization(.never)
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
        .sheet(isPresented: $instructions) {
            QuizInstructions(title: run.attempt?.title ?? run.intro?.title ?? "Quiz", html: run.attempt?.html ?? run.intro?.html ?? "")
                .environmentObject(engine)
        }
        .onDisappear { if run.submitted { engine.changed() } }
    }

    private var title: String {
        switch run.stage {
        case .review, .submitting: return "Review"
        case .feedback: return "Feedback"
        default: return run.attempt?.title ?? run.intro?.title ?? launch.title
        }
    }

    @ViewBuilder
    private var content: some View {
        switch run.stage {
        case .intro:
            if let i = run.intro { QuizIntroView(run: run, intro: i, tint: tint) } else { LoadState(error: run.introError) { Task { await run.loadIntro() } } }
        case .starting:
            ProgressView(run.intro?.begin.hasPrefix("Continue") == true ? "Resuming your attempt…" : "Starting your attempt…")
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        case .taking:
            QuizTakeView(run: run, tint: tint)
                .transition(.opacity)
        case .review, .submitting:
            QuizReviewView(run: run, tint: tint, submit: { confirmSubmit = true }, held: {
                // the pull-and-hold's end: a blank question is asked about first, otherwise it is handed in
                if run.answeredCount < run.questions.count { confirmSubmit = true } else { Task { await run.submit() } }
            })
            .transition(.asymmetric(insertion: .move(edge: .bottom).combined(with: .opacity), removal: .opacity))
        case .receipt:
            QuizReceiptView(run: run, tint: tint) { dismiss() }
        case .feedback:
            QuizFeedbackView(run: run, tint: tint)
        }
    }

    @ToolbarContentBuilder
    private var toolbar: some ToolbarContent {
        ToolbarItem(placement: .cancellationAction) {
            if run.stage == .feedback {
                Button { run.leaveFeedback() } label: { Image(systemName: "chevron.backward") }
                    .accessibilityLabel("Back")
            } else {
                Button {
                    if inAttempt { confirmLeave = true } else { dismiss() }
                } label: {
                    Image(systemName: "xmark")
                }
                .accessibilityLabel(inAttempt ? "Save and Exit" : "Close")
            }
        }
        if inAttempt {
            ToolbarItem(placement: .principal) { timer }
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Button { instructions = true } label: { Label("Instructions", systemImage: "book") }
                    if run.stage == .taking {
                        Button { Task { await run.toReview() } } label: { Label("Review Answers", systemImage: "list.bullet.rectangle") }
                    }
                    Button { openCanvas(run.attempt?.takeUrl) } label: { Label("Canvas’s Quiz Page", systemImage: "globe") }
                } label: {
                    Image(systemName: "ellipsis")
                }
                .accessibilityLabel("More")
            }
        }
    }

    /// Time left on a timed attempt (red in its last five minutes), or the time taken so far.
    @ViewBuilder
    private var timer: some View {
        let left = run.remaining(at: now)
        let shown = left ?? run.elapsed(at: now)
        if let shown {
            let low = (left ?? .infinity) < 300
            Label(QuizTime.clock(shown), systemImage: left != nil ? "timer" : "clock")
                .labelStyle(.titleAndIcon) // (a bar's own style draws a label as its icon alone: the time is the point)
                .font(.subheadline.weight(.semibold).monospacedDigit())
                .foregroundStyle(low ? Color.red : Color.primary)
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background((low ? Color.red.opacity(0.14) : Color(.tertiarySystemFill)), in: Capsule())
                .accessibilityLabel(left != nil ? "\(QuizTime.clock(shown)) left" : "\(QuizTime.clock(shown)) so far")
        } else {
            Text(run.attempt?.title ?? "").font(.headline).lineLimit(1)
        }
    }

    @ViewBuilder
    private var banner: some View {
        if let b = run.banner {
            Label(b.text, systemImage: b.error ? "exclamationmark.triangle.fill" : "clock.fill")
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(b.error ? Color.white : Color.primary)
                .padding(.horizontal, 16)
                .padding(.vertical, 10)
                .background(b.error ? AnyShapeStyle(Color.red) : AnyShapeStyle(.regularMaterial), in: Capsule())
                .shadow(color: .black.opacity(0.15), radius: 10, y: 4)
                .padding(.top, 6)
                .transition(.move(edge: .top).combined(with: .opacity))
                .onTapGesture { run.banner = nil }
                .animation(.spring(response: 0.4, dampingFraction: 0.85), value: run.banner)
        }
    }

    /// Canvas's own quiz page, in the app's web screen (the attempt as it stands; nothing restarts).
    private func openCanvas(_ url: String?) {
        guard let url else { return }
        Task {
            await run.flush()
            dismiss()
            engine.openWebScreen(url, title: run.attempt?.title ?? launch.title)
        }
    }
}

// MARK: - Intro

private struct QuizIntroView: View {
    @ObservedObject var run: QuizRun
    let intro: QuizIntro
    let tint: Color
    @EnvironmentObject private var engine: Engine
    @FocusState private var codeFocused: Bool

    var body: some View {
        List {
            Section {
                VStack(alignment: .leading, spacing: 10) {
                    HStack(spacing: 8) {
                        IconTile(symbol: "checklist", color: tint, size: 26)
                        Text(intro.context ?? "Quiz").font(.subheadline.weight(.semibold)).foregroundStyle(tint).lineLimit(1)
                    }
                    Text(intro.title).font(.title2.weight(.bold))
                    Text(intro.facts.joined(separator: " · ")).font(.subheadline).foregroundStyle(.secondary)
                    if let note = intro.note, !note.isEmpty {
                        Text(note).font(.footnote).foregroundStyle(.secondary)
                    }
                }
                .padding(.vertical, 4)
            }
            if !intro.html.isEmpty {
                Section("Before You Start") { RichText(html: intro.html).padding(.vertical, 4) }
            }
            Section {
                ForEach(intro.rules, id: \.self) { r in
                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        Image(systemName: r.symbol).foregroundStyle(QuizIntroView.tone(r.tint)).frame(width: 22).accessibilityHidden(true)
                        Text(r.text).font(.subheadline)
                    }
                    .padding(.vertical, 2)
                }
            }
            if intro.needsCode {
                Section {
                    TextField("Access code", text: $run.code)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .focused($codeFocused)
                        .submitLabel(.go)
                        .onSubmit { Task { await run.begin() } }
                } header: {
                    Text("Access Code")
                } footer: {
                    Text("Your instructor gives the code. It stays with this attempt.")
                }
            }
            if let why = run.refused {
                Section { Label(why, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red) }
            }
            if let lock = intro.lockText, !lock.isEmpty {
                Section { Label(lock, systemImage: "lock.fill").foregroundStyle(.secondary) }
            }
            if let last = intro.last {
                Section("Your Last Attempt") {
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text("Attempt \(last.attempt)").font(.body.weight(.semibold))
                            if let s = last.score { Text(s).font(.subheadline.monospacedDigit()).foregroundStyle(.secondary) }
                            else if let why = last.why, !why.isEmpty { Text(why).font(.footnote).foregroundStyle(.secondary) }
                        }
                        Spacer()
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .refreshable { await run.loadIntro() }
        .safeAreaInset(edge: .bottom) { actions }
    }

    /// Just the buttons (1.4): an attempt under way, Continue; a finished one with its feedback and another
    /// attempt left, Take Quiz and See Feedback side by side; with none left, See Feedback alone.
    @ViewBuilder
    private var actions: some View {
        let feedback = intro.last?.feedback == true && !intro.begin.hasPrefix("Continue")
        ActionBar {
            if feedback && intro.canStart {
                ActionButton(title: codeMissing ? "Enter the Code" : (intro.survey ? "Take Survey" : "Take Quiz"), tint: tint) { Task { await run.begin() } }
                    .disabled(codeMissing)
                ActionButton(title: "See Feedback", tint: tint, prominent: false) { openLastFeedback() }
            } else if feedback {
                ActionButton(title: "See Feedback", symbol: "text.bubble.fill", tint: tint) { openLastFeedback() }
            } else {
                ActionButton(title: codeMissing && intro.canStart ? "Enter the Access Code" : intro.begin, tint: tint) { Task { await run.begin() } }
                    .disabled(!intro.canStart || codeMissing)
            }
        }
    }

    private func openLastFeedback() {
        guard let last = intro.last else { return }
        Task { await run.openFeedback(last.attempt) }
    }

    private var codeMissing: Bool { intro.needsCode && run.code.trimmingCharacters(in: .whitespaces).isEmpty }

    static func tone(_ t: String) -> Color {
        switch t {
        case "orange": return .orange
        case "green": return .green
        default: return .secondary
        }
    }
}

// MARK: - Taking

private struct QuizTakeView: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var advance: Task<Void, Never>?
    @State private var touching = false

    var body: some View {
        VStack(spacing: 0) {
            QuizStrip(run: run, tint: tint)
            Divider()
            ZStack {
                if let q = run.current {
                    page(q)
                        .id(q.id)
                        .transition(pageTransition)
                } else {
                    ContentUnavailableView("This quiz has no questions.", systemImage: "questionmark.circle")
                }
            }
            .clipped()
            .animation(reduceMotion ? .easeOut(duration: 0.2) : .spring(duration: 0.45, bounce: 0.12), value: run.idx)
        }
        .safeAreaInset(edge: .bottom) { footer }
        .onDisappear { advance?.cancel() }
    }

    /// The page goes the way the student went: on, up and out with the next rising from below; back, the other way.
    private var pageTransition: AnyTransition {
        if reduceMotion { return .opacity }
        return run.forward
            ? .asymmetric(insertion: .move(edge: .bottom).combined(with: .opacity), removal: .move(edge: .top).combined(with: .opacity))
            : .asymmetric(insertion: .move(edge: .top).combined(with: .opacity), removal: .move(edge: .bottom).combined(with: .opacity))
    }

    /// What pulling up past the end of this question does: the next question, or — on the last — the review.
    private var up: PullEdges.Up {
        run.isLast
            ? .init(pull: "Pull up to review your answers", release: "Release to review your answers", symbol: "list.bullet.rectangle")
            : .init(pull: "Pull up for question \(nextNumber)", release: "Release for question \(nextNumber)", symbol: "arrow.up")
    }

    private func page(_ q: QuizQuestion) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                QuestionView(run: run, q: q, total: run.questions.count, tint: tint) { picked(q) }
                    .padding(.horizontal, 20)
                    .padding(.top, 18)
                Label(up.pull, systemImage: run.isLast ? "list.bullet.rectangle" : "arrow.up")
                    .font(.footnote.weight(.medium))
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity)
                    .padding(.top, 34)
                    .padding(.bottom, 28)
                    .accessibilityHidden(true)
            }
        }
        .scrollDismissesKeyboard(.interactively)
        .modifier(PullEdges(
            tint: tint,
            up: up,
            down: run.canGoBack ? PullEdges.Up(pull: "Pull down for question \(run.idx)", release: "Release for question \(run.idx)", symbol: "arrow.down") : nil,
            onUp: { if run.isLast { toReview() } else { next() } },
            onDown: back,
            touching: $touching
        ))
        .opacity(run.moving != nil ? 0.5 : 1)
        .animation(.easeOut(duration: 0.2), value: run.moving)
        .allowsHitTesting(run.moving == nil)
        .accessibilityAction(named: run.isLast ? "Review Answers" : "Next Question") { if run.isLast { toReview() } else { next() } }
        .accessibilityAction(named: "Previous Question") { if run.canGoBack { back() } }
    }

    private var nextNumber: Int { min(run.idx + 2, run.questions.count) }

    private func next() {
        advance?.cancel()
        Haptics.tap()
        Task { await run.next() }
    }

    private func back() {
        advance?.cancel()
        Haptics.tap()
        Task { await run.back() }
    }

    private func toReview() {
        advance?.cancel()
        Haptics.tap()
        Task { await run.toReview() }
    }

    /// One answer picked on a one-answer question: a beat to see it marked, then on to the next by itself.
    private func picked(_ q: QuizQuestion) {
        guard q.kind == "choice", !run.isLast else { return }
        advance?.cancel()
        advance = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 550_000_000)
            guard !Task.isCancelled, run.current?.id == q.id, !touching else { return }
            await run.next()
        }
    }

    private var footer: some View {
        HStack(spacing: 8) {
            Text("\(run.answeredCount) of \(run.questions.count) answered")
                .contentTransition(.numericText(value: Double(run.answeredCount)))
                .animation(.snappy(duration: 0.25), value: run.answeredCount)
            if !run.saveWord.isEmpty { Text("· \(run.saveWord)") }
            Spacer()
            if run.moving != nil { ProgressView().controlSize(.small) }
        }
        .font(.footnote.monospacedDigit())
        .foregroundStyle(.secondary)
        .padding(.horizontal, 20)
        .padding(.vertical, 10)
        .background(.bar)
    }
}

/// Pulling a scroll view past either end to act (1.4): the scroll view's own rubber band is the resistance — the
/// finger travels two or three times as far as the page — and a badge rises from that edge with a ring filling to
/// the line. Let go past it: `onUp` / `onDown`. An `Up` that `hold`s acts only once the pull is held past the line
/// for `holdTime` (the ring fills a second time) — handing a quiz in.
struct PullEdges: ViewModifier {
    struct Up {
        var pull: String
        var release: String
        var symbol: String
        var hold = false
    }

    let tint: Color
    let up: Up?
    var down: Up? = nil
    let onUp: () -> Void
    var onDown: () -> Void = {}
    @Binding var touching: Bool
    @State private var pull = Pull()
    @State private var hold: CGFloat = 0
    @State private var holdTask: Task<Void, Never>?

    struct Pull: Equatable {
        var up: CGFloat = 0
        var down: CGFloat = 0
    }

    static let line: CGFloat = 70
    static let holdTime = 1.8 // (1.4.4: twice the 0.9s it was — handing in is never an accident)

    func body(content: Content) -> some View {
        content
            .scrollBounceBehavior(.always)
            .onScrollGeometryChange(for: Pull.self) { g in
                let top = -g.contentInsets.top
                let bottom = max(top, g.contentSize.height - g.containerSize.height + g.contentInsets.bottom)
                return Pull(up: max(0, g.contentOffset.y - bottom), down: max(0, top - g.contentOffset.y))
            } action: { _, p in
                pulled(p)
            }
            .onScrollPhaseChange { old, new in
                if new == .interacting { touching = true }
                if old == .interacting && new != .interacting { released() }
            }
            .overlay(alignment: .bottom) {
                if let up, pull.up > 2 { badge(up, amount: pull.up, edge: .bottom) }
            }
            .overlay(alignment: .top) {
                if let down, pull.down > 2 { badge(down, amount: pull.down, edge: .top) }
            }
            .onDisappear { holdTask?.cancel() }
    }

    private func pulled(_ p: Pull) {
        let was = pull
        pull = p
        guard touching else { return }
        if let up, p.up >= Self.line && was.up < Self.line {
            Haptics.play(up.hold ? "medium" : "select")
            if up.hold { startHold() }
        } else if p.up < Self.line && was.up >= Self.line {
            cancelHold()
        }
        if down != nil && p.down >= Self.line && was.down < Self.line { Haptics.select() }
    }

    private func released() {
        touching = false
        let p = pull
        if let up, !up.hold, p.up >= Self.line { onUp() }
        else if down != nil, p.down >= Self.line { onDown() }
        cancelHold() // (a hold let go before it is done does nothing)
    }

    private func startHold() {
        guard holdTask == nil else { return }
        withAnimation(.linear(duration: Self.holdTime)) { hold = 1 }
        holdTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: UInt64(Self.holdTime * 1_000_000_000))
            guard !Task.isCancelled else { return }
            holdTask = nil
            Haptics.success()
            onUp()
            withAnimation(.spring(duration: 0.3)) { hold = 0 }
        }
    }

    private func cancelHold() {
        holdTask?.cancel()
        holdTask = nil
        if hold > 0 { withAnimation(.spring(duration: 0.3, bounce: 0)) { hold = 0 } }
    }

    private func badge(_ u: Up, amount: CGFloat, edge: VerticalEdge) -> some View {
        let p = min(amount / Self.line, 1)
        let past = p >= 1
        let words = past ? (u.hold && hold >= 1 ? "Submitting…" : u.release) : u.pull
        return HStack(spacing: 10) {
            ZStack {
                Circle().stroke(tint.opacity(0.22), lineWidth: 3)
                Circle()
                    .trim(from: 0, to: u.hold && past ? max(hold, 0.02) : p)
                    .stroke(tint, style: StrokeStyle(lineWidth: 3, lineCap: .round))
                    .rotationEffect(.degrees(-90))
                Image(systemName: u.symbol)
                    .font(.caption.weight(.bold))
                    .foregroundStyle(tint)
                    .scaleEffect(past ? 1.15 : 1)
            }
            .frame(width: 26, height: 26)
            Text(words).font(.subheadline.weight(.semibold)).contentTransition(.opacity)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 9)
        .modifier(GlassCapsule())
        .opacity(min(1, amount / 18))
        .scaleEffect(0.92 + 0.08 * p)
        .padding(edge == .bottom ? .bottom : .top, 10)
        .animation(.snappy(duration: 0.2), value: past)
        .allowsHitTesting(false)
    }
}

/// Every question as a numbered circle: answered (filled), flagged (an orange flag), the one on screen
/// (ringed), one that is sealed (no going back) dimmed. Pressing one goes to it.
private struct QuizStrip: View {
    @ObservedObject var run: QuizRun
    let tint: Color

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(Array(run.questions.enumerated()), id: \.element.id) { k, q in
                        pill(q, k).id(k)
                    }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 10)
            }
            .onAppear { proxy.scrollTo(run.idx, anchor: .center) }
            .onChange(of: run.idx) { withAnimation { proxy.scrollTo(run.idx, anchor: .center) } }
        }
    }

    private func pill(_ q: QuizQuestion, _ k: Int) -> some View {
        let current = k == run.idx
        let sealed = run.attempt?.noBack == true && k < run.idx
        let done = q.isAnswered && q.kind != "info"
        return Button { Task { await run.go(to: k) } } label: {
            ZStack(alignment: .topTrailing) {
                Text("\(k + 1)")
                    .font(.subheadline.weight(.semibold).monospacedDigit())
                    .frame(width: 36, height: 36)
                    .foregroundStyle(done ? Color.white : (current ? tint : Color.primary))
                    .background(Circle().fill(done ? tint : Color(.tertiarySystemFill)))
                    .overlay(Circle().strokeBorder(current ? (done ? Color.primary.opacity(0.6) : tint) : .clear, lineWidth: 2.5))
                if q.flagged {
                    Image(systemName: "flag.fill")
                        .font(.system(size: 9, weight: .bold))
                        .foregroundStyle(.white)
                        .padding(3)
                        .background(Circle().fill(.orange))
                        .offset(x: 4, y: -4)
                }
                if run.moving == k {
                    ProgressView().controlSize(.small).frame(width: 36, height: 36)
                }
            }
            .opacity(sealed ? 0.35 : 1)
        }
        .buttonStyle(.plain)
        .disabled(sealed || current || run.moving != nil)
        .accessibilityLabel("Question \(k + 1)\(done ? ", answered" : "")\(q.flagged ? ", flagged" : "")")
    }
}

/// One question: its number and points, the flag, its words (Canvas's own, pictures and formulas
/// included), and its answer in the shape its kind takes.
private struct QuestionView: View {
    @ObservedObject var run: QuizRun
    let q: QuizQuestion
    let total: Int
    let tint: Color
    /// One answer picked on a one-answer question (the page moves on by itself).
    var picked: () -> Void = {}
    @EnvironmentObject private var engine: Engine
    @State private var importing = false
    @State private var photos: [PhotosPickerItem] = []

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(alignment: .firstTextBaseline) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Question \(q.n)").font(.title3.weight(.bold))
                    Text(ofLine).font(.subheadline).foregroundStyle(.secondary)
                }
                Spacer()
                if q.kind != "info" && q.kind != "pending" {
                    Button { Task { await run.flag(q.id) } } label: {
                        Label(q.flagged ? "Flagged" : "Flag", systemImage: q.flagged ? "flag.fill" : "flag")
                            .font(.subheadline.weight(.semibold))
                            .foregroundStyle(q.flagged ? Color.orange : Color.secondary)
                            .padding(.horizontal, 12)
                            .padding(.vertical, 7)
                            .background((q.flagged ? Color.orange.opacity(0.15) : Color(.tertiarySystemFill)), in: Capsule())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel(q.flagged ? "Flagged for review" : "Flag for review")
                }
            }
            if q.kind == "pending" {
                ProgressView("Loading the question…").frame(maxWidth: .infinity, minHeight: 120)
            } else {
                RichText(html: q.html)
                answer
                if !q.hint.isEmpty {
                    Label(q.hint, systemImage: "info.circle").font(.footnote).foregroundStyle(.secondary)
                }
            }
        }
    }

    private var ofLine: String {
        guard let p = q.points else { return "of \(total)" }
        return "of \(total) · \(CourseGradesView.num(p)) \(p == 1 ? "point" : "points")"
    }

    @ViewBuilder
    private var answer: some View {
        switch q.kind {
        case "choice", "multi": options
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

    // a choice: one option (a circle) or several (a square), each a full-width row that marks itself as picked
    private var options: some View {
        let multi = q.kind == "multi"
        return VStack(spacing: 10) {
            ForEach(q.options) { o in
                let on = multi ? q.picks.contains(o.id) : q.pick == o.id
                Button {
                    withAnimation(.snappy(duration: 0.22)) {
                        run.set(q.id) { x in
                            if multi {
                                if let i = x.picks.firstIndex(of: o.id) { x.picks.remove(at: i) } else { x.picks.append(o.id) }
                            } else {
                                x.pick = o.id
                            }
                        }
                    }
                    if !multi { picked() }
                } label: {
                    HStack(alignment: .center, spacing: 12) {
                        Group {
                            if multi {
                                Text(o.letter).frame(width: 30, height: 30).innerFill(on ? tint : Color(.tertiarySystemFill), radius: 8, minimum: 8)
                            } else {
                                Text(o.letter).frame(width: 30, height: 30).background(on ? tint : Color(.tertiarySystemFill), in: Circle())
                            }
                        }
                        .font(.subheadline.weight(.bold))
                        .foregroundStyle(on ? Color.white : Color.secondary)
                        OptionLabel(text: o.text, html: o.html)
                        Spacer(minLength: 0)
                        Image(systemName: on ? (multi ? "checkmark.square.fill" : "checkmark.circle.fill") : (multi ? "square" : "circle"))
                            .font(.title3)
                            .foregroundStyle(on ? tint : Color(.tertiaryLabel))
                            .contentTransition(.symbolEffect(.replace))
                            .symbolEffect(.bounce, value: on)
                    }
                    .padding(14)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .containerShape(RoundedRectangle(cornerRadius: 16, style: .continuous)) // (the letter's square is concentric with the row)
                    .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(on ? tint.opacity(0.12) : Color(.secondarySystemBackground)))
                    .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(on ? tint : .clear, lineWidth: 1.5))
                    .contentShape(Rectangle())
                }
                .buttonStyle(PressScale())
                .accessibilityAddTraits(on ? .isSelected : [])
            }
            if multi { Text("Pick every answer that applies.").font(.footnote).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading) }
        }
    }

    private func line(numeric: Bool) -> some View {
        TextField(numeric ? "Number" : "Your answer", text: Binding(get: { q.text }, set: { v in run.set(q.id, typed: true) { $0.text = v } }))
            .keyboardType(numeric ? .numbersAndPunctuation : .default)
            .textInputAutocapitalization(numeric ? .never : .sentences)
            .autocorrectionDisabled(numeric)
            .font(.body.monospacedDigit())
            .padding(14)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Color(.secondarySystemBackground)))
    }

    private var essay: some View {
        VStack(alignment: .leading, spacing: 6) {
            TextField("Your answer…", text: Binding(get: { q.text }, set: { v in run.set(q.id, typed: true) { $0.text = v } }), axis: .vertical)
                .lineLimit(8...30)
                .padding(14)
                .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Color(.secondarySystemBackground)))
            Text("A blank line starts a new paragraph; lines starting with • or 1. become a list.").font(.caption).foregroundStyle(.secondary)
        }
    }

    // matching: each value with the list of matches beside it
    private var matching: some View {
        VStack(spacing: 10) {
            ForEach(q.options) { o in
                VStack(alignment: .leading, spacing: 8) {
                    OptionLabel(text: o.text, html: o.html)
                    Menu {
                        Button("None") { run.set(q.id) { $0.map[o.id] = nil } }
                        ForEach(q.matches) { m in
                            Button { run.set(q.id) { $0.map[o.id] = m.id } } label: {
                                if q.map[o.id] == m.id { Label(m.text, systemImage: "checkmark") } else { Text(m.text) }
                            }
                        }
                    } label: {
                        pickLabel(matchName(q.map[o.id]))
                    }
                }
                .padding(14)
                .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Color(.secondarySystemBackground)))
            }
        }
    }

    // one field per blank, numbered as its mark in the question's words: a list to pick from, or a line to type
    private var blanks: some View {
        VStack(spacing: 10) {
            ForEach(q.blanks) { b in
                HStack(spacing: 12) {
                    Text("\(b.n)")
                        .font(.subheadline.weight(.bold))
                        .foregroundStyle(tint)
                        .frame(minWidth: 28, minHeight: 28)
                        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(tint.opacity(0.16)))
                    if q.kind == "drops" {
                        Menu {
                            Button("None") { run.set(q.id) { $0.map[b.id] = nil } }
                            ForEach(b.options) { c in
                                Button { run.set(q.id) { $0.map[b.id] = c.id } } label: {
                                    if q.map[b.id] == c.id { Label(c.text, systemImage: "checkmark") } else { Text(c.text) }
                                }
                            }
                        } label: {
                            pickLabel(choiceName(q.map[b.id], in: b), placeholder: b.label.hasPrefix("Blank ") ? "Choose…" : b.label)
                        }
                    } else {
                        TextField(b.label.hasPrefix("Blank ") ? "Your answer" : b.label, text: Binding(get: { q.map[b.id] ?? "" }, set: { v in run.set(q.id, typed: true) { $0.map[b.id] = v } }))
                            .textInputAutocapitalization(.never)
                            .padding(12)
                            .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color(.secondarySystemBackground)))
                    }
                }
            }
        }
    }

    private func matchName(_ id: String?) -> String? {
        guard let id else { return nil }
        return q.matches.first(where: { $0.id == id })?.text
    }

    private func choiceName(_ id: String?, in b: QuizQuestion.Blank) -> String? {
        guard let id else { return nil }
        return b.options.first(where: { $0.id == id })?.text
    }

    private func pickLabel(_ chosen: String?, placeholder: String = "Choose…") -> some View {
        HStack {
            Text(chosen ?? placeholder)
                .foregroundStyle(chosen == nil ? Color.secondary : Color.primary)
                .multilineTextAlignment(.leading)
            Spacer(minLength: 4)
            Image(systemName: "chevron.up.chevron.down").font(.footnote.weight(.semibold)).foregroundStyle(.secondary)
        }
        .padding(12)
        .frame(maxWidth: .infinity)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(chosen == nil ? Color(.tertiarySystemFill) : tint.opacity(0.12)))
    }

    // a file: chosen from Files or Photos, uploaded to the student's own quiz files, replaced or removed
    private var file: some View {
        VStack(alignment: .leading, spacing: 10) {
            if run.uploading == q.id {
                HStack(spacing: 10) {
                    ProgressView()
                    Text("Uploading…").foregroundStyle(.secondary)
                }
                .padding(14)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Color(.secondarySystemBackground)))
            } else if let f = q.files.first {
                HStack(spacing: 10) {
                    Image(systemName: "doc.fill").foregroundStyle(tint)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(f.name).lineLimit(1)
                        Text("Handed in with this attempt when you submit.").font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("Remove", role: .destructive) { run.set(q.id) { $0.files = [] } }
                        .buttonStyle(.borderless)
                }
                .padding(14)
                .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Color(.secondarySystemBackground)))
            }
            HStack(spacing: 10) {
                Button { importing = true } label: {
                    Label(q.files.isEmpty ? "Choose File" : "Replace", systemImage: "folder").frame(maxWidth: .infinity, minHeight: 40)
                }
                .glassButton()
                PhotosPicker(selection: $photos, maxSelectionCount: 1, matching: .any(of: [.images, .videos])) {
                    Label("Photo", systemImage: "photo").frame(maxWidth: .infinity, minHeight: 40)
                }
                .glassButton()
            }
            .disabled(run.uploading != nil)
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.item], allowsMultipleSelection: false) { result in
            guard case .success(let urls) = result, let u = urls.first else { return }
            let scoped = u.startAccessingSecurityScopedResource()
            defer { if scoped { u.stopAccessingSecurityScopedResource() } }
            guard let d = try? Data(contentsOf: u) else { run.say("\(u.lastPathComponent) could not be read.", error: true); return }
            let mime = UTType(filenameExtension: u.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
            let picked = PickedFile(name: u.lastPathComponent, type: mime, data: d)
            Task { await run.upload(q.id, picked) }
        }
        .onChange(of: photos) {
            guard let item = photos.first else { return }
            photos = []
            Task {
                guard let d = try? await item.loadTransferable(type: Data.self) else { run.say("The photo could not be read.", error: true); return }
                let ut = item.supportedContentTypes.first(where: { $0.preferredFilenameExtension != nil }) ?? .jpeg
                await run.upload(q.id, PickedFile(name: "Photo.\(ut.preferredFilenameExtension ?? "jpg")", type: ut.preferredMIMEType ?? "image/jpeg", data: d))
            }
        }
    }

    // a kind Canvas adds that the app does not know yet: Canvas's own page answers it, on the same attempt
    private var elsewhere: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("This kind of question is answered on Canvas’s quiz page. Your other answers are already saved there.")
                .font(.subheadline).foregroundStyle(.secondary)
            Button {
                Task {
                    await run.flush()
                    engine.quiz = nil
                    engine.openWebScreen(run.attempt?.takeUrl ?? "", title: run.attempt?.title ?? "Quiz")
                }
            } label: {
                Label("Answer on Canvas", systemImage: "globe").frame(maxWidth: .infinity, minHeight: 44)
            }
            .glassProminentButton()
        }
        .padding(14)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Color(.secondarySystemBackground)))
    }
}

/// An option's words, or Canvas's own content where it has none (a formula is an equation picture): a press
/// on it is the row's.
private struct OptionLabel: View {
    let text: String
    let html: String

    var body: some View {
        if !html.isEmpty && (text.isEmpty || html.contains("<img")) {
            RichText(html: html).allowsHitTesting(false)
        } else {
            Text(text.isEmpty ? "—" : text).multilineTextAlignment(.leading).frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

// MARK: - Review

private struct QuizReviewView: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    /// Submit Quiz pressed: asked first.
    let submit: () -> Void
    /// Pulled up and held at the foot of the list: the hold was the asking (a blank question is still raised).
    let held: () -> Void
    @State private var touching = false

    var body: some View {
        let blank = run.questions.count - run.answeredCount
        let noBack = run.attempt?.noBack == true
        List {
            Section {
                ForEach(Array(run.questions.enumerated()), id: \.element.id) { k, q in
                    Button { Task { await run.keepWorking(at: k) } } label: { row(q) }
                        .buttonStyle(.plain)
                        .disabled(noBack)
                }
            } header: {
                Text("\(run.answeredCount) of \(run.questions.count) answered · \(blank == 0 ? "nothing left blank" : "\(blank) left blank")")
            } footer: {
                Text(noBack ? "This quiz seals each question once you leave it." : "Tap a question to change it. Submitting ends the attempt; blank questions are graded as incorrect.")
            }
            Section {
                Label("Pull up and hold to submit", systemImage: "paperplane")
                    .font(.footnote.weight(.medium))
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity)
                    .listRowBackground(Color.clear)
                    .accessibilityHidden(true)
            }
        }
        .listStyle(.insetGrouped)
        .modifier(PullEdges(
            tint: tint,
            up: run.stage == .submitting ? nil : PullEdges.Up(pull: "Pull up and hold to submit", release: "Keep holding to submit", symbol: "paperplane.fill", hold: true),
            onUp: held,
            touching: $touching
        ))
        .safeAreaInset(edge: .bottom) {
            ActionBar {
                ActionButton(title: "Keep Working", tint: tint, prominent: false) { Task { await run.keepWorking() } }
                ActionButton(title: run.stage == .submitting ? "Submitting…" : "Submit Quiz", tint: tint, busy: run.stage == .submitting, action: submit)
            }
            .disabled(run.stage == .submitting)
        }
    }

    private func row(_ q: QuizQuestion) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(q.n)")
                .font(.subheadline.weight(.bold).monospacedDigit())
                .frame(width: 30, height: 30)
                .foregroundStyle(q.isAnswered ? Color.white : Color.secondary)
                .background(Circle().fill(q.isAnswered ? tint : Color(.tertiarySystemFill)))
            VStack(alignment: .leading, spacing: 3) {
                Text(q.plain).font(.subheadline).lineLimit(2)
                if q.kind == "info" {
                    Text("Information only").font(.footnote).foregroundStyle(.secondary)
                } else if let s = q.summary {
                    Text(s).font(.footnote.weight(.medium)).foregroundStyle(tint).lineLimit(2)
                } else {
                    Text("Not answered").font(.footnote.weight(.semibold)).foregroundStyle(.red)
                }
            }
            .separatorAtText()
            Spacer(minLength: 4)
            if q.flagged { Image(systemName: "flag.fill").foregroundStyle(.orange).font(.footnote) }
        }
        .padding(.vertical, 2)
        .contentShape(Rectangle())
    }
}

// MARK: - Receipt

private struct QuizReceiptView: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let done: () -> Void

    var body: some View {
        let r = run.receipt
        ScrollView {
            VStack(spacing: 18) {
                Image(systemName: "checkmark.circle.fill")
                    .font(.system(size: 64, weight: .semibold))
                    .foregroundStyle(.green)
                    .symbolEffect(.bounce, value: run.stage)
                    .padding(.top, 28)
                Text(r?.title ?? "Attempt Submitted").font(.title.weight(.bold))
                if let lead = r?.lead, !lead.isEmpty {
                    Text(lead).font(.subheadline).foregroundStyle(.secondary).multilineTextAlignment(.center).padding(.horizontal, 12)
                }
                VStack(spacing: 0) {
                    fact("Questions answered", r?.answered ?? "")
                    if let s = r?.score {
                        Divider().padding(.leading, 16)
                        fact("Score", s)
                    }
                    if let t = r?.takingPart {
                        Divider().padding(.leading, 16)
                        fact("For taking part", t)
                    }
                }
                .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(Color(.secondarySystemBackground)))
            }
            .padding(.horizontal, 20)
        }
        .safeAreaInset(edge: .bottom) {
            ActionBar {
                ActionButton(title: "Done", tint: tint, prominent: r?.feedback != true, action: done)
                if r?.feedback == true {
                    ActionButton(title: "See Feedback", tint: tint) { Task { await run.openFeedback(r?.attempt) } }
                }
            }
        }
    }

    private func fact(_ k: String, _ v: String) -> some View {
        HStack {
            Text(k).foregroundStyle(.secondary)
            Spacer()
            Text(v).font(.body.weight(.semibold).monospacedDigit())
        }
        .padding(16)
    }
}

// MARK: - Feedback

/// A finished attempt question by question: the score as a ring, a filter (all, to review, correct), the
/// instructor's comments, and each question with what you answered, the correct answer where the quiz shows
/// it, every option, and the worked solution.
private struct QuizFeedbackView: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    @State private var filter = 0

    var body: some View {
        if let fb = run.feedback {
            if let why = fb.hidden {
                ContentUnavailableView {
                    Label("Results not released", systemImage: "eye.slash")
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
        let shown = rows.filter { filter == 0 || (filter == 1 && ($0.verdict == "wrong" || $0.verdict == "partial")) || (filter == 2 && $0.verdict == "right") }
        let color = Color(hex: fb.color)
        return List {
            Section {
                HStack(spacing: 18) {
                    ZStack {
                        Ring(value: fb.pct, color: color, lineWidth: 8)
                        Text(fb.pct.map { "\(Int($0.rounded()))%" } ?? "—").font(.headline.weight(.bold)).foregroundStyle(color)
                    }
                    .frame(width: 84, height: 84)
                    VStack(alignment: .leading, spacing: 4) {
                        Text("\(fb.score ?? "—") / \(fb.possible ?? "—")").font(.title2.weight(.bold).monospacedDigit())
                        Text("Attempt \(fb.attempt ?? 1)").font(.subheadline.weight(.semibold))
                        if let s = fb.summary { Text(s).font(.footnote).foregroundStyle(.secondary) }
                    }
                    Spacer(minLength: 0)
                }
                .padding(.vertical, 6)
                if let atts = fb.attempts, atts.count > 1 {
                    Picker("Attempt", selection: Binding(get: { fb.attempt ?? 1 }, set: { a in Task { await run.openFeedback(a) } })) {
                        ForEach(atts, id: \.self) { Text("Attempt \($0)").tag($0) }
                    }
                }
            }
            if fb.released == true {
                Section {
                    Picker("Show", selection: $filter) {
                        Text("All \(rows.count)").tag(0)
                        Text("To Review \(rows.filter { $0.verdict == "wrong" || $0.verdict == "partial" }.count)").tag(1)
                        Text("Correct \(rows.filter { $0.verdict == "right" }.count)").tag(2)
                    }
                    .pickerStyle(.segmented)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets())
                }
            } else {
                Section { Label("Canvas has not released the question results for this attempt yet — your score and the questions are shown as they stand.", systemImage: "info.circle").font(.footnote).foregroundStyle(.secondary) }
            }
            if let cs = fb.comments, !cs.isEmpty {
                Section("Instructor Comments") {
                    ForEach(cs, id: \.self) { c in
                        HStack(alignment: .top, spacing: 10) {
                            PersonAvatar(name: c.author, size: 30)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(c.author).font(.subheadline.weight(.semibold))
                                Text(c.text).font(.subheadline)
                            }
                        }
                    }
                }
            }
            if shown.isEmpty {
                Section { Text(filter == 2 ? "No question in this attempt was answered correctly." : "Nothing to review — every question was answered correctly.").foregroundStyle(.secondary) }
            }
            ForEach(shown) { r in
                Section { FeedbackCard(row: r, tint: tint) }
            }
        }
        .listStyle(.insetGrouped)
    }
}

private struct FeedbackCard: View {
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
                Image(systemName: mark).foregroundStyle(ink).font(.title3)
                Text("Question \(row.n)").font(.headline)
                Text(row.verdictText).font(.subheadline.weight(.semibold)).foregroundStyle(ink)
                Spacer()
                if !row.score.isEmpty { Text(row.score).font(.subheadline.weight(.semibold).monospacedDigit()).foregroundStyle(ink) }
            }
            RichText(html: row.html)
            if let m = row.match {
                matchTable(m)
            } else {
                answerBox("Your Answer", row.yours, tinted: row.verdict != "none", color: ink, empty: "No answer")
                if !row.right.isEmpty { answerBox("Correct Answer", row.right, tinted: true, color: .green, empty: "—") }
            }
            if let opts = row.options, !opts.isEmpty {
                DisclosureGroup(isExpanded: $showOptions) {
                    VStack(spacing: 8) {
                        ForEach(opts) { o in
                            HStack(spacing: 10) {
                                Text(o.letter).font(.caption.weight(.bold)).frame(width: 24, height: 24)
                                    .background(Circle().fill(Color(.tertiarySystemFill)))
                                OptionLabel(text: o.text, html: o.html)
                                Spacer(minLength: 0)
                                if o.mine { tag("Yours", tint) }
                                if o.right { tag("Correct", .green) }
                            }
                        }
                    }
                    .padding(.top, 8)
                } label: {
                    Text(showOptions ? "Hide the Options" : "Show All \(opts.count) Options").font(.subheadline.weight(.semibold))
                }
            }
            if let s = row.solution {
                VStack(alignment: .leading, spacing: 6) {
                    Text("WORKED SOLUTION").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                    if !s.html.isEmpty { RichText(html: s.html) } else { Text(s.text).font(.subheadline) }
                }
                .padding(12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .innerFill(Color(.tertiarySystemFill), radius: 12, minimum: 10)
            } else if row.noSolution {
                Text("Your instructor left no worked solution for this question.").font(.footnote).foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 6)
    }

    private func tag(_ t: String, _ c: Color) -> some View {
        Text(t).font(.caption2.weight(.semibold)).foregroundStyle(c).padding(.horizontal, 6).padding(.vertical, 2).background(c.opacity(0.14), in: Capsule())
    }

    private func answerBox(_ label: String, _ parts: [QuizFeedback.Part], tinted: Bool, color: Color, empty: String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(label.uppercased()).font(.caption.weight(.semibold)).foregroundStyle(.secondary)
            if parts.isEmpty {
                Text(empty).italic().font(.subheadline).foregroundStyle(.secondary)
            } else {
                ForEach(Array(parts.enumerated()), id: \.offset) { _, p in
                    if !p.text.isEmpty {
                        Text(p.text).font(.subheadline.weight(row.essay ? .regular : .semibold)).foregroundStyle(tinted && !row.essay ? color : Color.primary)
                    } else if !p.html.isEmpty {
                        RichText(html: p.html)
                    }
                }
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .innerFill(tinted && !row.essay ? color.opacity(0.12) : Color(.tertiarySystemFill), radius: 12, minimum: 10)
    }

    private func matchTable(_ m: QuizFeedback.MatchTable) -> some View {
        VStack(spacing: 8) {
            ForEach(Array(m.rows.enumerated()), id: \.offset) { _, r in
                VStack(alignment: .leading, spacing: 4) {
                    Text(r.left).font(.subheadline.weight(.semibold))
                    HStack(spacing: 6) {
                        Image(systemName: r.ok == true ? "checkmark" : r.ok == false ? "xmark" : "arrow.right")
                            .font(.caption.weight(.bold))
                            .foregroundStyle(r.ok == true ? Color.green : r.ok == false ? Color.red : Color.secondary)
                        Text(r.mine ?? "No match set").font(.subheadline).foregroundStyle(r.mine == nil ? Color.secondary : Color.primary)
                    }
                    if m.showRight, let right = r.right, r.ok != true {
                        Text("Correct: \(right)").font(.footnote.weight(.medium)).foregroundStyle(.green)
                    }
                }
                .padding(10)
                .frame(maxWidth: .infinity, alignment: .leading)
                .innerFill(r.ok == true ? Color.green.opacity(0.1) : r.ok == false ? Color.red.opacity(0.1) : Color(.tertiarySystemFill), radius: 10, minimum: 8)
            }
        }
    }
}

/// The quiz's instructions, a press away the whole way through an attempt.
private struct QuizInstructions: View {
    let title: String
    let html: String
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            ScrollView {
                Group {
                    if html.isEmpty { Text("This quiz came with no instructions.").foregroundStyle(.secondary) } else { RichText(html: html) }
                }
                .padding(20)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .navigationTitle("Instructions")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } } }
        }
        .presentationDetents([.medium, .large])
        .presentationDragIndicator(.visible)
    }
}

import AppKit
import PhotosUI
import SwiftUI
import UniformTypeIdentifiers

// The attempt of QuizScreen's sheet (1.2): the question on the page, its answers as rows, the questions as glass chips
// (a strip over the page, or a column beside it on a wide sheet) and a floating glass bar at the foot.

/// Where the keyboard is during an attempt: the attempt's own keys (held by the strip or the side column), or an answer
/// being typed.
private enum QuizFocus: Hashable {
    case keys
    case field(String)
}

/// The attempt, one question at a time: the questions (a strip at the top, or down the side of a wide sheet), the
/// question on the page (it slides the way the student moves), and floating at the foot Previous, what is answered and
/// whether it is saved, Submit and Next. Keys: ← and → move, 1–9 pick an answer, Return goes on (to the review after
/// the last), F flags, ⌘Return submits — none of the plain ones while an answer is being typed.
struct QuizTakePane: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let openCanvas: () -> Void
    let submit: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @FocusState private var focus: QuizFocus?

    // (written out: the focus is of this file's own type, which would keep a synthesized one in this file)
    init(run: QuizRun, tint: Color, openCanvas: @escaping () -> Void, submit: @escaping () -> Void) {
        _run = ObservedObject(wrappedValue: run)
        self.tint = tint
        self.openCanvas = openCanvas
        self.submit = submit
    }

    var body: some View {
        GeometryReader { geo in
            let wide = geo.size.width >= QuizLayout.wide
            HStack(spacing: 0) {
                if wide {
                    QuizRail(run: run, tint: tint, jump: jump, review: review)
                        .frame(width: 256)
                    Divider()
                }
                VStack(spacing: 0) {
                    if !wide {
                        QuizStripView(run: run, tint: tint, jump: jump, review: review)
                    }
                    page
                        .overlay(alignment: .bottom) { bar }
                }
            }
            .onChange(of: wide) { _, _ in focus = .keys } // (the side column and the strip trade places: the keys go with them)
            // (1.3.7) the keys held by a view of their own, under everything: held by the chips' column, its first click went
            // to taking the keys back (after typing an answer), not to the chip, so a number took two or three clicks
            .background { holdingKeys(Color.clear.frame(width: 1, height: 1)).accessibilityHidden(true) }
        }
        .defaultFocus($focus, .keys)
        .task {
            try? await Task.sleep(nanoseconds: 80_000_000) // (once the sheet's window is key)
            if focus == nil { focus = .keys }
        }
        .onChange(of: run.idx) { _, _ in focus = .keys }
        .task {
            guard !QuizClickProbe.target.isEmpty else { return }
            try? await Task.sleep(nanoseconds: 3_000_000_000)
            QuizClickProbe.fire()
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            NSLog("SimplQuizClick after: question %d showing", run.idx + 1)
        }
    }

    /// The holder of the attempt's keys (whether or not keyboard navigation is on): an unseen view that takes no clicks.
    private func holdingKeys<V: View>(_ v: V) -> some View {
        v.focusable(interactions: .edit)
            .focused($focus, equals: .keys)
            .focusEffectDisabled()
            .onKeyPress(phases: .down) { press in key(press) }
    }

    private var page: some View {
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
    }

    /// The page goes the way the student went: on, out to the left with the next coming in from the right; back, the
    /// other way. Under Reduce Motion, a cross-fade.
    private var pageTransition: AnyTransition {
        reduceMotion ? .opacity : .push(from: run.forward ? .trailing : .leading)
    }

    /// The floating bar: Previous, how much is answered and whether it is saved, Submit, and Next (Review Answers on
    /// the last question) — the one to press, so the prominent one.
    private var bar: some View {
        QuizFloatingBar {
            Button(action: back) { Label("Previous", systemImage: "chevron.left") }
                .glassButton()
                .disabled(!run.canGoBack || run.moving != nil)
                .help("Previous question (←)")
            Spacer(minLength: 10)
            status
            Spacer(minLength: 10)
            Button("Submit…", action: submit)
                .glassButton()
                .keyboardShortcut(.return, modifiers: .command)
                .help("Submit this attempt (⌘Return)")
            Button(action: next) {
                HStack(spacing: 6) {
                    Text(run.isLast ? "Review Answers" : "Next")
                    Image(systemName: run.isLast ? "list.bullet.rectangle" : "chevron.right")
                }
            }
            .glassButton(prominent: true)
            .disabled(run.moving != nil)
            .help(run.isLast ? "Review your answers (Return)" : "Next question (Return or →)")
            .background { QuizClickProbe.mark("next") }
        }
    }

    private var status: some View {
        HStack(spacing: 6) {
            if run.moving != nil { ProgressView().controlSize(.small) }
            Text("\(run.answeredCount) of \(run.questions.count) answered")
                .contentTransition(.numericText(value: Double(run.answeredCount)))
                .animation(Motion.snappy, value: run.answeredCount)
            if !run.saveWord.isEmpty { Text("· \(run.saveWord)") }
        }
        .font(.sCallout.monospacedDigit())
        .foregroundStyle(.secondary)
        .lineLimit(1)
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
        .glassCapsule()
        .fixedSize()
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
}

// MARK: - The questions as chips

/// Every question's number as a tile in a strip over the page (a narrow sheet), Review at its end. The strip follows the
/// question showing.
private struct QuizStripView: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let jump: (Int) -> Void
    let review: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            ScrollViewReader { proxy in
                ScrollView(.horizontal) {
                    HStack(spacing: 6) {
                        ForEach(Array(run.questions.enumerated()), id: \.element.id) { k, q in
                            QuizChip(run: run, q: q, k: k, tint: tint, width: 36, jump: jump)
                                .id(k)
                        }
                    }
                    .padding(8)
                    .card(radius: 14)
                    .padding(.horizontal, 22)
                    .padding(.vertical, 6)
                }
                .scrollIndicators(.never)
                .onAppear { proxy.scrollTo(run.idx, anchor: .center) }
                .onChange(of: run.idx) { _, k in
                    withAnimation(Motion.gentle) { proxy.scrollTo(k, anchor: .center) }
                }
            }
            Button(action: review) { Label("Review", systemImage: "list.bullet.rectangle") }
                .glassButton()
                .controlSize(.large)
                .disabled(run.moving != nil)
                .help("Review every answer before submitting")
                .padding(.trailing, 22)
        }
        .padding(.bottom, 2)
    }
}

/// The questions down the side of a wide sheet: every question's number in a small card (QuizNumberCard), what the tiles
/// mean, how much is answered (and whether it is saved), and the review.
private struct QuizRail: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let jump: (Int) -> Void
    let review: () -> Void

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    QuizNumberCard(run: run, tint: tint, jump: jump)
                    legend
                    Divider()
                    progress
                }
                .padding(.horizontal, 22)
                .padding(.top, 10)
                .padding(.bottom, 24)
            }
            .scrollIndicators(.never)
            .onChange(of: run.idx) { _, k in
                withAnimation(Motion.gentle) { proxy.scrollTo(k) } // (only as far as it takes to show it)
            }
        }
    }

    private var legend: some View {
        VStack(alignment: .leading, spacing: 8) {
            key(fill: tint, ring: nil, "On screen")
            key(fill: tint.opacity(0.2), ring: nil, "Answered")
            key(fill: Theme.well, ring: .orange, "Flagged")
            key(fill: Theme.well, ring: nil, "Not answered")
        }
        .font(.sFootnote)
        .foregroundStyle(.secondary)
        .accessibilityElement(children: .combine)
    }

    private func key(fill: Color, ring: Color?, _ text: String) -> some View {
        HStack(spacing: 10) {
            RoundedRectangle(cornerRadius: 3.5, style: .continuous)
                .fill(fill)
                .overlay(RoundedRectangle(cornerRadius: 3.5, style: .continuous).strokeBorder(ring ?? Color.clear, lineWidth: 1.5))
                .frame(width: 13, height: 13)
            Text(text)
        }
    }

    private var progress: some View {
        let total = run.questions.count
        let done = run.answeredCount
        return VStack(alignment: .leading, spacing: 10) {
            Text("\(done) of \(total) answered")
                .font(.sHeadline.monospacedDigit())
                .contentTransition(.numericText(value: Double(done)))
                .animation(Motion.snappy, value: done)
            ProgressView(value: Double(done), total: Double(max(total, 1)))
                .progressViewStyle(.linear)
                .tint(tint)
                .animation(Motion.fill, value: done)
                .accessibilityHidden(true)
            if !run.saveWord.isEmpty {
                Text(run.saveWord)
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
            }
            Button(action: review) {
                Label("Review Answers", systemImage: "list.bullet.rectangle")
                    .frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .disabled(run.moving != nil)
            .help("Review every answer before submitting")
            .padding(.top, 6)
        }
    }
}

/// (1.3.8) Every question's number in a small card, a clean grid of tiles five across, with how many are answered at its
/// head: in place of the glass bubbles (a glass face drawn inside each button, under which a click could be lost).
private struct QuizNumberCard: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let jump: (Int) -> Void

    private let columns = Array(repeating: GridItem(.flexible(), spacing: 6), count: 5)

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                Text("Questions")
                    .font(.sHeadline)
                Spacer(minLength: 6)
                Text("\(run.answeredCount) of \(run.questions.count)")
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText(value: Double(run.answeredCount)))
                    .animation(Motion.snappy, value: run.answeredCount)
            }
            LazyVGrid(columns: columns, spacing: 6) {
                ForEach(Array(run.questions.enumerated()), id: \.element.id) { k, q in
                    QuizChip(run: run, q: q, k: k, tint: tint, jump: jump)
                        .id(k)
                }
            }
        }
        .padding(14)
        .card(radius: 16)
    }
}

/// One question's tile as a button: a click goes to it. Answered, flagged, the one showing, one sealed behind the student
/// (no going back) dimmed and shut. `width`: fixed in the strip; in the card, the grid's column.
private struct QuizChip: View {
    @ObservedObject var run: QuizRun
    let q: QuizQuestion
    let k: Int
    let tint: Color
    var width: CGFloat? = nil
    let jump: (Int) -> Void

    var body: some View {
        let current = k == run.idx
        let sealed = run.attempt?.noBack == true && k < run.idx
        let done = q.isAnswered && q.kind != "info"
        Button { jump(k) } label: {
            QuizNumberTile(number: k + 1, current: current, done: done, flagged: q.flagged, loading: run.moving == k, tint: tint, width: width)
                .opacity(sealed ? 0.35 : 1)
        }
        .buttonStyle(QuizTileStyle())
        .disabled(sealed || run.moving != nil) // (the one showing stays a button: a click on it does nothing, and asks nothing)
        .help(tip(sealed: sealed))
        .background { QuizClickProbe.mark(String(k + 1)) }
        .accessibilityLabel("Question \(k + 1)\(done ? ", answered" : "")\(q.flagged ? ", flagged" : "")\(current ? ", showing" : "")")
    }

    private func tip(sealed: Bool) -> String {
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

/// A tile's face: the number in a small rounded square — the one showing filled with the colour, an answered one washed
/// in it, one not yet answered a quiet grey; a flagged one edged in orange with its flag in the corner, one on its way a
/// spinner. Flat: no glass.
private struct QuizNumberTile: View {
    let number: Int
    let current: Bool
    let done: Bool
    let flagged: Bool
    var loading = false
    let tint: Color
    var width: CGFloat? = nil

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        Text("\(number)")
            .font(.sCallout.weight(.semibold).monospacedDigit())
            .foregroundStyle(current ? Color.white : (done ? tint : Color.secondary))
            .frame(width: width, height: 32)
            .frame(maxWidth: width == nil ? .infinity : nil)
            .background(shape.fill(current ? tint : (done ? tint.opacity(0.18) : Color.primary.opacity(0.07))))
            .overlay(shape.strokeBorder(flagged && !current ? Color.orange : Color.clear, lineWidth: 1.5))
            .overlay(alignment: .topTrailing) {
                if flagged {
                    Image(systemName: "flag.fill")
                        .font(.system(size: 7, weight: .bold))
                        .foregroundStyle(current ? Color.white : Color.orange)
                        .padding(4)
                        .transition(.opacity)
                }
            }
            .overlay {
                if loading { ProgressView().controlSize(.mini) }
            }
            .contentShape(shape)
            .animation(Motion.snappy, value: done)
            .animation(Motion.snappy, value: current)
            .animation(Motion.snappy, value: flagged)
    }
}

/// A tile as a button: a touch brighter under the pointer, a little smaller under a click (under Reduce Motion, dimmed).
private struct QuizTileStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        TileBody(configuration: configuration)
    }

    private struct TileBody: View {
        let configuration: ButtonStyleConfiguration
        @State private var hover = false
        @Environment(\.isEnabled) private var enabled
        @Environment(\.accessibilityReduceMotion) private var reduceMotion

        var body: some View {
            configuration.label
                .brightness(hover && enabled ? 0.06 : 0)
                .scaleEffect(!reduceMotion && configuration.isPressed ? 0.94 : 1)
                .opacity(reduceMotion && configuration.isPressed ? 0.7 : 1)
                .animation(Motion.snappy, value: configuration.isPressed)
                .animation(Motion.hover, value: hover)
                .onHover { hover = $0 }
        }
    }
}

/// (the screenshot suite: -SimplQuizClick 4 clicks question 4's tile, -SimplQuizClick next the bar's Next, as a person
/// would — a mouse down and a mouse up put on the app's queue at its middle in the sheet's window — so the picture shows
/// whether a click lands; what the click finds there is written to the console)
@MainActor
enum QuizClickProbe {
    static let target = UserDefaults.standard.string(forKey: "SimplQuizClick") ?? ""
    private static var frame: CGRect?

    /// Where the target is, in its window (nothing drawn, and nothing at all unless the suite asked for this one).
    @ViewBuilder
    static func mark(_ id: String) -> some View {
        if target == id {
            GeometryReader { g in
                Color.clear
                    .onAppear { frame = g.frame(in: .global) }
                    .onChange(of: g.frame(in: .global)) { _, f in frame = f }
            }
        }
    }

    static func fire() {
        guard !target.isEmpty, let r = frame,
              let w = NSApp.windows.first(where: { $0.isSheet && $0.isVisible }) ?? NSApp.keyWindow,
              let content = w.contentView else {
            NSLog("SimplQuizClick %@: nothing to click (frame %@)", target, String(describing: frame))
            return
        }
        NSApp.activate(ignoringOtherApps: true) // (as a person's click finds it: the app in front, the sheet's window key)
        w.makeKey()
        let at = NSPoint(x: r.midX, y: content.bounds.height - r.midY)
        let hit = content.hitTest(content.convert(at, from: nil))
        var chain: [String] = []
        var v: NSView? = hit
        while let x = v, chain.count < 8 { chain.append(String(describing: type(of: x))); v = x.superview }
        NSLog("SimplQuizClick %@ at %@ (frame %@): active %d, key %d, sheet %d; hit %@", target, NSStringFromPoint(at), NSStringFromRect(r),
              NSApp.isActive ? 1 : 0, w.isKeyWindow ? 1 : 0, w.isSheet ? 1 : 0, chain.joined(separator: " < "))
        for type in [NSEvent.EventType.leftMouseDown, .leftMouseUp] {
            if let e = NSEvent.mouseEvent(with: type, location: at, modifierFlags: [], timestamp: ProcessInfo.processInfo.systemUptime,
                                          windowNumber: w.windowNumber, context: nil, eventNumber: 0, clickCount: 1, pressure: type == .leftMouseDown ? 1 : 0) {
                NSApp.postEvent(e, atStart: false)
            }
        }
    }
}

/// A chip as a button: it grows a little under the pointer and gives under a click (under Reduce Motion the click
/// dims it instead). The feedback's chips use it too.
struct QuizChipStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        ChipBody(configuration: configuration)
    }

    private struct ChipBody: View {
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
}

// MARK: - The question

/// One question on the page, with no box round it: its number and points, the flag, its words (Canvas's own, pictures
/// and formulas included) at a reading size, and its answer in the shape its kind takes — a row per choice, a box per
/// answer that applies, a field, a menu per match or blank, a file chosen or dropped, or Canvas's own page for a kind
/// it answers there.
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
            VStack(alignment: .leading, spacing: 30) {
                header
                if q.kind == "pending" {
                    ProgressView("Loading the question…")
                        .font(.sBody)
                        .frame(maxWidth: .infinity, minHeight: 160)
                } else {
                    RichText(html: q.html, size: 18)
                    answer
                    if !q.hint.isEmpty {
                        Label(q.hint, systemImage: "info.circle")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .frame(maxWidth: QuizLayout.column, alignment: .leading)
            .padding(.horizontal, 56)
            .padding(.top, 30)
            .padding(.bottom, QuizLayout.barClearance)
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
            .onTapGesture { focus.wrappedValue = .keys } // (a click off the answers: the keys are the attempt's again)
        }
        .background(PageGround())
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .firstTextBaseline, spacing: 12) {
                Text("Question \(q.n)")
                    .font(.sTitle)
                    .tracking(-0.4)
                Text(ofLine)
                    .font(.sBody)
                    .foregroundStyle(.secondary)
                Spacer(minLength: 8)
                if q.kind != "info" && q.kind != "pending" { flagButton }
            }
            Divider()
        }
    }

    private var flagButton: some View {
        Button(action: flag) {
            Label {
                Text(q.flagged ? "Flagged" : "Flag")
            } icon: {
                Image(systemName: q.flagged ? "flag.fill" : "flag")
                    .foregroundStyle(q.flagged ? Color.orange : Color.secondary)
            }
        }
        .glassButton()
        .controlSize(.large)
        .help(q.flagged ? "Remove the flag (F)" : "Flag this question to come back to it (F)")
        .accessibilityLabel(q.flagged ? "Flagged for review" : "Flag for review")
        .accessibilityAddTraits(q.flagged ? .isSelected : [])
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

    // one answer: a row per option with its radio, the whole row the target; its number on the keyboard picks it. The
    // rows reach a little past the column, so their words start where the question's do.
    private var choices: some View {
        VStack(spacing: 8) {
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
        .padding(.horizontal, -20)
    }

    // every answer that applies: a row per option with its box, the whole row the target
    private var multiple: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(spacing: 8) {
                ForEach(Array(q.options.enumerated()), id: \.element.id) { k, o in
                    let on = q.picks.contains(o.id)
                    let ticked = Binding(get: { on }, set: { v in if v != on { choose(o) } })
                    Button { choose(o) } label: {
                        QuizOptionRow(letter: o.letter, text: o.text, html: o.html, number: k < 9 ? k + 1 : nil, on: on, tint: tint) {
                            QuizCheck(on: on, tint: tint)
                        }
                    }
                    .buttonStyle(QuizChoiceStyle(on: on, tint: tint))
                    .accessibilityRepresentation {
                        Toggle(QuizFormat.optionName(o), isOn: ticked)
                    }
                }
            }
            .padding(.horizontal, -20)
            Text("Pick every answer that applies.")
                .font(.sCallout)
                .foregroundStyle(.secondary)
        }
    }

    private func line(numeric: Bool) -> some View {
        TextField(numeric ? "Number" : "Your answer", text: Binding(get: { q.text }, set: { v in run.set(q.id, typed: true) { $0.text = v } }))
            .textFieldStyle(.roundedBorder)
            .controlSize(.large)
            .font(.sBody.monospacedDigit())
            .autocorrectionDisabled(numeric)
            .focused(focus, equals: .field("\(q.id)#line"))
            .onSubmit(onReturn)
            .frame(maxWidth: numeric ? CGFloat(300) : CGFloat.infinity, alignment: .leading)
    }

    private var essay: some View {
        VStack(alignment: .leading, spacing: 8) {
            TextEditor(text: Binding(get: { q.text }, set: { v in run.set(q.id, typed: true) { $0.text = v } }))
                .font(.sBody)
                .scrollContentBackground(.hidden)
                .focused(focus, equals: .field("\(q.id)#essay"))
                .padding(10)
                .frame(height: 300)
                .background(Color(nsColor: .textBackgroundColor), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Color.secondary.opacity(0.3)))
            Text("A blank line starts a new paragraph; lines starting with • or 1. become a list.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
        }
    }

    // matching: a row per item with a menu of the matches beside it, a hairline between the rows
    private var matching: some View {
        VStack(spacing: 0) {
            ForEach(Array(q.options.enumerated()), id: \.element.id) { k, o in
                if k > 0 { Divider() }
                HStack(spacing: 18) {
                    QuizOptionText(text: o.text, html: o.html)
                    Picker(QuizFormat.optionName(o), selection: Binding(get: { q.map[o.id] }, set: { v in run.set(q.id) { $0.map[o.id] = v } })) {
                        Text("Choose…").tag(String?.none)
                        ForEach(q.matches) { m in
                            Text(m.text).tag(Optional(m.id))
                        }
                    }
                    .pickerStyle(.menu)
                    .labelsHidden()
                    .frame(width: 280)
                }
                .padding(.vertical, 12)
            }
        }
        .controlSize(.large)
    }

    // one field per blank, numbered as its mark in the question's words: a menu to pick from, or a line to type
    private var blanks: some View {
        VStack(alignment: .leading, spacing: 12) {
            ForEach(q.blanks) { b in
                HStack(spacing: 14) {
                    Text("\(b.n)")
                        .font(.sCallout.weight(.bold).monospacedDigit())
                        .foregroundStyle(tint)
                        .frame(minWidth: 28, minHeight: 28)
                        .background(Circle().fill(tint.opacity(0.16)))
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
                        .frame(maxWidth: 340, alignment: .leading)
                    } else {
                        TextField(b.label.hasPrefix("Blank ") ? "Your answer" : b.label, text: Binding(get: { q.map[b.id] ?? "" }, set: { v in run.set(q.id, typed: true) { $0.map[b.id] = v } }))
                            .textFieldStyle(.roundedBorder)
                            .font(.sBody)
                            .focused(focus, equals: .field("\(q.id)#\(b.id)"))
                            .onSubmit(onReturn)
                            .frame(maxWidth: 380)
                    }
                    Spacer(minLength: 0)
                }
            }
        }
        .controlSize(.large)
    }

    // a file: chosen in the Open panel, dragged in from the Finder, or picked from Photos; uploaded to the student's own
    // quiz files on Canvas, then replaced or removed
    private var file: some View {
        VStack(alignment: .leading, spacing: 16) {
            if run.uploading == q.id {
                HStack(spacing: 10) {
                    ProgressView().controlSize(.small)
                    Text("Uploading…").foregroundStyle(.secondary)
                }
                .font(.sBody)
            } else if let f = q.files.first {
                HStack(spacing: 12) {
                    Image(systemName: "doc.fill")
                        .font(.sTitle2)
                        .foregroundStyle(tint)
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(f.name)
                            .font(.sBody.weight(.semibold))
                            .lineLimit(1)
                        Text("Handed in with this attempt when you submit.")
                            .font(.sFootnote)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("Remove", role: .destructive) { run.set(q.id) { $0.files = [] } }
                        .glassButton()
                }
            }
            dropZone
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

    /// Where a file is dropped: the one outline on the page, dashed, in the colour while a file is over it.
    private var dropZone: some View {
        VStack(spacing: 12) {
            Image(systemName: "arrow.down.doc")
                .font(.system(size: 30))
                .foregroundStyle(dropping ? tint : Color.secondary)
                .accessibilityHidden(true)
            Text(dropWords)
                .font(.sBody)
                .foregroundStyle(.secondary)
            HStack(spacing: 10) {
                Button { importing = true } label: { Label(q.files.isEmpty ? "Choose File…" : "Replace…", systemImage: "folder") }
                PhotosPicker(selection: $photos, maxSelectionCount: 1, matching: .any(of: [.images, .videos])) {
                    Label("Photo…", systemImage: "photo")
                }
            }
            .glassButton()
            .controlSize(.large)
            .disabled(run.uploading != nil)
        }
        .frame(maxWidth: .infinity)
        .padding(24)
        .background {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(dropping ? tint.opacity(0.1) : Color.clear)
        }
        .overlay {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
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
        VStack(alignment: .leading, spacing: 12) {
            Text("This kind of question is answered on Canvas’s quiz page. Your other answers are already saved there.")
                .font(.sBody)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Button(action: openCanvas) { Label("Answer on Canvas", systemImage: "globe") }
                .glassButton(prominent: true)
                .controlSize(.large)
        }
    }
}

// MARK: - An option's row

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
        HStack(alignment: .center, spacing: 16) {
            mark()
            Text(letter)
                .font(.sBody.weight(.semibold))
                .foregroundStyle(on ? tint : Color.secondary)
                .frame(minWidth: 18)
            QuizOptionText(text: text, html: html)
            if let number {
                Text("\(number)")
                    .font(.sCaption.monospacedDigit())
                    .foregroundStyle(.tertiary)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 1)
                    .overlay(RoundedRectangle(cornerRadius: 5, style: .continuous).strokeBorder(Color.secondary.opacity(0.3)))
                    .help("Press \(number) to choose this answer")
                    .accessibilityHidden(true)
            }
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 16)
        .frame(maxWidth: .infinity, minHeight: 58, alignment: .leading)
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
                    .frame(width: 7, height: 7)
                    .transition(.scale(scale: 0.3).combined(with: .opacity))
            }
        }
        .frame(width: 18, height: 18)
        .animation(Motion.snappy, value: on)
        .accessibilityHidden(true)
    }
}

/// A box as the Mac draws one: empty, or filled with the colour and ticked.
private struct QuizCheck: View {
    let on: Bool
    let tint: Color

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 5, style: .continuous)
        ZStack {
            shape.fill(on ? tint : Color(nsColor: .controlBackgroundColor))
            shape.strokeBorder(on ? tint : Color.secondary.opacity(0.55), lineWidth: 1)
            if on {
                Image(systemName: "checkmark")
                    .font(.system(size: 11, weight: .bold))
                    .foregroundStyle(.white)
                    .transition(.scale(scale: 0.4).combined(with: .opacity))
            }
        }
        .frame(width: 18, height: 18)
        .animation(Motion.snappy, value: on)
        .accessibilityHidden(true)
    }
}

/// An option's row as a button (1.2): no box at rest — a soft wash under the pointer, a deeper one under a click, and
/// once picked the colour's wash.
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
        if on { return tint.opacity(configuration.isPressed ? 0.22 : 0.14) }
        if configuration.isPressed { return Color.primary.opacity(0.08) }
        return Color.primary.opacity(hover ? 0.045 : 0)
    }

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 12, style: .continuous)
        configuration.label
            .background(shape.fill(fill))
            .contentShape(shape)
            .animation(Motion.hover, value: hover)
            .animation(Motion.snappy, value: on)
            .onHover { hover = $0 }
    }
}

/// An option's words, or Canvas's own content where it has none (a formula is an equation picture): a click on it is
/// the row's. The feedback's options use it too.
struct QuizOptionText: View {
    let text: String
    let html: String

    var body: some View {
        if !html.isEmpty && (text.isEmpty || html.contains("<img")) {
            RichText(html: html, size: 15)
                .allowsHitTesting(false)
                .frame(maxWidth: .infinity, alignment: .leading)
        } else {
            Text(text.isEmpty ? "—" : text)
                .font(.sBody)
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

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

import SwiftUI

// The ends of QuizScreen's sheet (1.2): the review before handing in, the receipt, and the feedback on a finished
// attempt — each a page with at most one level on it: plain lists with hairlines between their rows, no card per
// question, no box in a box.

// MARK: - Review

/// The attempt before it is handed in: every question as a row of a plain list — whether it is answered, its words, its
/// answer at the row's end (or that it is blank, or flagged) — a click on one to change it; on a wide sheet a column
/// beside it of how much is answered. Back to Questions and Submit (⌘Return) float at the foot.
struct QuizReviewPane: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let submit: () -> Void

    private var noBack: Bool { run.attempt?.noBack == true }
    private var blank: Int { run.questions.count - run.answeredCount }
    private var flagged: Int { run.questions.filter(\.flagged).count }

    var body: some View {
        GeometryReader { geo in
            let wide = geo.size.width >= QuizLayout.wide
            ScrollView {
                HStack(alignment: .top, spacing: 52) {
                    VStack(alignment: .leading, spacing: 20) {
                        heading(wide: wide)
                        list
                        Text(note)
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .frame(maxWidth: QuizLayout.column, alignment: .leading)
                    if wide {
                        summary
                            .frame(width: 230, alignment: .leading)
                            .padding(.top, 10)
                    }
                }
                .padding(.horizontal, 40)
                .padding(.top, 18)
                .padding(.bottom, QuizLayout.barClearance)
                .frame(maxWidth: .infinity)
            }
            .overlay(alignment: .bottom) { bar }
        }
    }

    private var countLine: String {
        "\(run.answeredCount) of \(run.questions.count) answered · \(blank == 0 ? "nothing left blank" : "\(blank) left blank")"
    }

    private var note: String {
        noBack ? "This quiz seals each question once you leave it." : "Click a question to change it. Submitting ends the attempt; blank questions are graded as incorrect."
    }

    private func heading(wide: Bool) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Review Your Answers")
                .font(.sLargeTitle)
                .tracking(-0.5)
            if !wide {
                Text(countLine)
                    .font(.sBody)
                    .foregroundStyle(.secondary)
            }
        }
    }

    /// The questions, a row each with a hairline between: on a quiz that lets the student go back, each row a button.
    /// The rows' wash reaches a little past the column, so their glyphs line up with the heading.
    private var list: some View {
        VStack(spacing: 0) {
            ForEach(Array(run.questions.enumerated()), id: \.element.id) { k, q in
                if k > 0 { RowDivider(inset: 46) }
                if noBack {
                    row(q)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 12)
                } else {
                    Button { Task { await run.keepWorking(at: k) } } label: {
                        row(q)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 12)
                    }
                    .buttonStyle(RowButtonStyle())
                    .help("Change your answer to question \(q.n)")
                }
            }
        }
        .padding(.horizontal, -10)
    }

    private func row(_ q: QuizQuestion) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 14) {
            glyph(q)
            VStack(alignment: .leading, spacing: 4) {
                Text(q.plain.isEmpty ? q.name : q.plain)
                    .font(.sBody)
                    .lineLimit(2)
                HStack(spacing: 6) {
                    Text("Question \(q.n)")
                    if q.flagged {
                        Image(systemName: "flag.fill")
                            .foregroundStyle(.orange)
                            .accessibilityHidden(true)
                        Text("Flagged")
                            .foregroundStyle(.orange)
                    }
                }
                .font(.sFootnote)
                .foregroundStyle(.secondary)
            }
            Spacer(minLength: 16)
            answer(q)
                .frame(maxWidth: 260, alignment: .trailing)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
    }

    /// Answered (a tick in the colour), blank (a mark in red), or only words to read.
    private func glyph(_ q: QuizQuestion) -> some View {
        let info = q.kind == "info"
        let done = q.isAnswered
        let symbol = info ? "info.circle" : (done ? "checkmark.circle.fill" : "exclamationmark.circle")
        let color: Color = info ? Color.secondary : (done ? tint : Color.red)
        return Image(systemName: symbol)
            .font(.sTitle3)
            .foregroundStyle(color)
            .frame(width: 22)
            .accessibilityHidden(true)
    }

    @ViewBuilder
    private func answer(_ q: QuizQuestion) -> some View {
        if q.kind == "info" {
            Text("Information only")
                .font(.sCallout)
                .foregroundStyle(.secondary)
        } else if let s = q.summary {
            Text(s)
                .font(.sBody.weight(.semibold))
                .foregroundStyle(tint)
                .lineLimit(2)
                .multilineTextAlignment(.trailing)
        } else {
            Text("Not answered")
                .font(.sBody.weight(.semibold))
                .foregroundStyle(.red)
        }
    }

    /// Beside the list on a wide sheet: a ring of how much is answered, and what is blank and flagged.
    private var summary: some View {
        let total = run.questions.count
        let pct: Double = total > 0 ? Double(run.answeredCount) / Double(total) * 100 : 0
        return VStack(alignment: .leading, spacing: 18) {
            ZStack {
                Ring(value: pct, color: tint, lineWidth: 9)
                VStack(spacing: 0) {
                    Text("\(run.answeredCount)")
                        .font(.sTitle.monospacedDigit())
                    Text("of \(total)")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
            }
            .frame(width: 116, height: 116)
            VStack(alignment: .leading, spacing: 10) {
                fact("checkmark.circle.fill", "\(run.answeredCount) answered", tint)
                fact("exclamationmark.circle", blank == 0 ? "Nothing left blank" : "\(blank) left blank", blank == 0 ? Color.secondary : Color.red)
                if flagged > 0 {
                    fact("flag.fill", "\(flagged) flagged", Color.orange)
                }
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func fact(_ symbol: String, _ text: String, _ color: Color) -> some View {
        Label {
            Text(text)
        } icon: {
            Image(systemName: symbol).foregroundStyle(color)
        }
        .font(.sBody)
    }

    private var bar: some View {
        QuizFloatingBar {
            Button { Task { await run.keepWorking() } } label: { Label("Back to Questions", systemImage: "chevron.left") }
                .glassButton()
                .disabled(run.stage == .submitting)
                .help("Back to the question you were on")
            Spacer(minLength: 10)
            if run.stage == .submitting {
                HStack(spacing: 8) {
                    ProgressView().controlSize(.small)
                    Text("Submitting…")
                }
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .glassCapsule()
            }
            Button(action: submit) { Label(run.attempt?.survey == true ? "Submit Survey…" : "Submit Quiz…", systemImage: "paperplane") }
                .glassButton(prominent: true)
                .keyboardShortcut(.return, modifiers: .command)
                .disabled(run.stage == .submitting)
                .help("Submit this attempt (⌘Return)")
        }
    }
}

// MARK: - Receipt

/// The attempt handed in: the tick, what Canvas says of it (the questions answered, the score, the points for taking
/// part) as plain rows, and Done or the feedback. Its pieces arrive one after another, once.
struct QuizReceiptPane: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    let done: () -> Void
    @State private var shown = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let r = run.receipt
        ScrollView {
            VStack(spacing: 18) {
                Image(systemName: "checkmark.circle.fill")
                    .font(.system(size: 72, weight: .semibold))
                    .foregroundStyle(.green)
                    .symbolEffect(.bounce, value: reduceMotion ? false : shown) // (under Reduce Motion the tick only fades in)
                    .quizArrive(0, shown)
                    .accessibilityHidden(true)
                Text(r?.title ?? "Attempt Submitted")
                    .font(.sLargeTitle)
                    .tracking(-0.5)
                    .multilineTextAlignment(.center)
                    .quizArrive(1, shown)
                if let lead = r?.lead, !lead.isEmpty {
                    Text(lead)
                        .font(.sBody)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                        .frame(maxWidth: 520)
                        .quizArrive(2, shown)
                }
                facts(r)
                    .frame(maxWidth: 460)
                    .padding(.top, 10)
                    .quizArrive(3, shown)
                buttons(r)
                    .padding(.top, 16)
                    .quizArrive(4, shown)
            }
            .padding(.horizontal, 40)
            .padding(.top, 44)
            .padding(.bottom, 40)
            .frame(maxWidth: .infinity)
        }
        .onAppear { shown = true }
    }

    private func facts(_ r: QuizReceipt?) -> some View {
        VStack(spacing: 0) {
            Divider()
            fact("Questions answered", r?.answered ?? "")
            if let s = r?.score {
                Divider()
                fact("Score", s)
            }
            if let t = r?.takingPart {
                Divider()
                fact("For taking part", t)
            }
            Divider()
        }
    }

    private func fact(_ k: String, _ v: String) -> some View {
        HStack {
            Text(k).foregroundStyle(.secondary)
            Spacer()
            Text(v).font(.sBody.weight(.semibold).monospacedDigit())
        }
        .font(.sBody)
        .padding(.vertical, 13)
    }

    private func buttons(_ r: QuizReceipt?) -> some View {
        GlassGroup(spacing: 8) {
            HStack(spacing: 10) {
                if r?.feedback == true {
                    Button("Done", action: done)
                        .glassButton()
                    Button("See Feedback") { Task { await run.openFeedback(r?.attempt) } }
                        .glassButton(prominent: true)
                        .keyboardShortcut(.defaultAction)
                } else {
                    Button("Done", action: done)
                        .glassButton(prominent: true)
                        .keyboardShortcut(.defaultAction)
                }
            }
        }
        .controlSize(.large)
    }
}

// MARK: - Feedback

/// A finished attempt question by question: the score as a ring, the attempt to show, a filter (all, to review,
/// correct), the instructor's comments, and each question with what was answered, the correct answer where the quiz
/// shows it, every option, and the worked solution. (1.2) The questions are sections of the page with a hairline
/// between them, never cards; on a wide sheet the score, the filter and a chip per question (a click goes to it) stay
/// in a column at the side.
struct QuizFeedbackPane: View {
    @ObservedObject var run: QuizRun
    let tint: Color
    @State private var filter = 0
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        if let fb = run.feedback {
            if let why = fb.hidden {
                ContentUnavailableView {
                    Label("Results Not Released", systemImage: "eye.slash")
                } description: {
                    Text(why)
                }
            } else {
                page(fb)
            }
        } else {
            LoadState(error: run.feedbackError) { Task { await run.openFeedback(run.feedback?.attempt) } }
        }
    }

    private func page(_ fb: QuizFeedback) -> some View {
        GeometryReader { geo in
            let wide = geo.size.width >= QuizLayout.wide
            ScrollViewReader { proxy in
                HStack(alignment: .top, spacing: 0) {
                    if wide {
                        ScrollView {
                            rail(fb, proxy: proxy)
                                .padding(.horizontal, 22)
                                .padding(.top, 14)
                                .padding(.bottom, 28)
                        }
                        .scrollIndicators(.never)
                        .frame(width: 284)
                        Divider()
                    }
                    ScrollView {
                        main(fb, wide: wide)
                            .frame(maxWidth: 800, alignment: .leading)
                            .padding(.horizontal, 40)
                            .padding(.top, 18)
                            .padding(.bottom, 48)
                            .frame(maxWidth: .infinity)
                    }
                }
            }
        }
    }

    private func main(_ fb: QuizFeedback, wide: Bool) -> some View {
        let rows = fb.rows ?? []
        let shown = rows.filter(passes)
        return VStack(alignment: .leading, spacing: 28) {
            if !wide {
                HStack(alignment: .center, spacing: 22) {
                    scoreRing(fb, size: 96)
                    scoreWords(fb)
                    Spacer(minLength: 0)
                    attemptPicker(fb)
                }
                filterBar(fb, rows: rows)
            } else if fb.released != true {
                notReleased
            }
            if let cs = fb.comments, !cs.isEmpty { comments(cs) }
            questions(shown)
        }
    }

    // MARK: The column at the side

    private func rail(_ fb: QuizFeedback, proxy: ScrollViewProxy) -> some View {
        let rows = fb.rows ?? []
        return VStack(alignment: .leading, spacing: 22) {
            VStack(alignment: .leading, spacing: 14) {
                scoreRing(fb, size: 108)
                scoreWords(fb)
                attemptPicker(fb)
            }
            if fb.released == true {
                filterRows(rows)
            }
            if !rows.isEmpty {
                VStack(alignment: .leading, spacing: 12) {
                    Text("Questions")
                        .font(.sHeadline)
                    GlassGroup(spacing: 6) {
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: 38, maximum: 46), spacing: 8)], alignment: .leading, spacing: 8) {
                            ForEach(rows) { r in
                                Button { go(to: r, proxy: proxy) } label: {
                                    QuizVerdictChip(n: r.n, verdict: r.verdict)
                                        .opacity(passes(r) ? 1 : 0.4)
                                }
                                .buttonStyle(QuizChipStyle())
                                .help("Question \(r.n) · \(r.verdictText)")
                                .accessibilityLabel("Question \(r.n), \(r.verdictText)")
                            }
                        }
                        .padding(.vertical, 2)
                    }
                }
            }
        }
    }

    /// A chip in the column clicked: the page goes to its question (shown again if the filter had hidden it).
    private func go(to r: QuizFeedback.Row, proxy: ScrollViewProxy) {
        let target = "fbq-\(r.n)"
        let animated = !reduceMotion
        let scroll: () -> Void = {
            if animated {
                withAnimation(Motion.gentle) { proxy.scrollTo(target, anchor: .top) }
            } else {
                proxy.scrollTo(target, anchor: .top)
            }
        }
        if passes(r) {
            scroll()
        } else {
            filter = 0
            DispatchQueue.main.async { scroll() } // (once the question is back on the page)
        }
    }

    // MARK: The score

    private func color(_ fb: QuizFeedback) -> Color { fb.color.map { Color(hex: $0) } ?? tint }

    private func scoreRing(_ fb: QuizFeedback, size: CGFloat) -> some View {
        let c = color(fb)
        return ZStack {
            Ring(value: fb.pct, color: c, lineWidth: 9)
            Text(fb.pct.map { "\(Int($0.rounded()))%" } ?? "—")
                .font(.sTitle3.weight(.bold).monospacedDigit())
                .foregroundStyle(c)
        }
        .frame(width: size, height: size)
    }

    private func scoreWords(_ fb: QuizFeedback) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("\(fb.score ?? "—") / \(fb.possible ?? "—")")
                .font(.sTitle.monospacedDigit())
            Text("Attempt \(fb.attempt ?? 1)")
                .font(.sBody.weight(.semibold))
            if let s = fb.summary {
                Text(s)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    @ViewBuilder
    private func attemptPicker(_ fb: QuizFeedback) -> some View {
        if let atts = fb.attempts, atts.count > 1 {
            Picker("Attempt", selection: Binding(get: { fb.attempt ?? 1 }, set: { a in Task { await run.openFeedback(a) } })) {
                ForEach(atts, id: \.self) { a in
                    Text("Attempt \(a)").tag(a)
                }
            }
            .pickerStyle(.menu)
            .labelsHidden()
            .controlSize(.large)
            .fixedSize()
            .help("Show another attempt")
        }
    }

    // MARK: The filter

    private static let filterNames = ["All", "To Review", "Correct"]

    private func passes(_ r: QuizFeedback.Row) -> Bool {
        switch filter {
        case 1: return r.verdict == "wrong" || r.verdict == "partial"
        case 2: return r.verdict == "right"
        default: return true
        }
    }

    private func counts(_ rows: [QuizFeedback.Row]) -> [Int] {
        [rows.count, rows.filter { $0.verdict == "wrong" || $0.verdict == "partial" }.count, rows.filter { $0.verdict == "right" }.count]
    }

    private func setFilter(_ k: Int) {
        withAnimation(reduceMotion ? nil : Motion.gentle) { filter = k }
    }

    /// A narrow sheet's filter: the segmented switcher (Liquid Glass on macOS 26), or why there is none yet.
    @ViewBuilder
    private func filterBar(_ fb: QuizFeedback, rows: [QuizFeedback.Row]) -> some View {
        if fb.released == true {
            let n = counts(rows)
            Picker("Show", selection: Binding(get: { filter }, set: { setFilter($0) })) {
                Text("All \(n[0])").tag(0)
                Text("To Review \(n[1])").tag(1)
                Text("Correct \(n[2])").tag(2)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .controlSize(.large)
            .frame(maxWidth: 460)
        } else {
            notReleased
        }
    }

    /// The column's filter: a row each, the one showing washed in the colour, as a sidebar's are.
    private func filterRows(_ rows: [QuizFeedback.Row]) -> some View {
        let n = counts(rows)
        return VStack(alignment: .leading, spacing: 2) {
            ForEach(0..<3, id: \.self) { k in
                Button { setFilter(k) } label: {
                    HStack {
                        Text(Self.filterNames[k])
                        Spacer(minLength: 8)
                        Text("\(n[k])")
                            .monospacedDigit()
                            .foregroundStyle(.secondary)
                    }
                    .font(.sBody.weight(filter == k ? .semibold : .regular))
                    .padding(.horizontal, 12)
                    .padding(.vertical, 8)
                    .background {
                        RoundedRectangle(cornerRadius: 10, style: .continuous)
                            .fill(filter == k ? tint.opacity(0.15) : Color.clear)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(filter == k ? .isSelected : [])
            }
        }
        .padding(.horizontal, -12)
    }

    private var notReleased: some View {
        Label("Canvas has not released the question results for this attempt yet — your score and the questions are shown as they stand.", systemImage: "info.circle")
            .font(.sCallout)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }

    // MARK: The page

    private func comments(_ cs: [QuizFeedback.Comment]) -> some View {
        PageSection(title: "Instructor Comments") {
            VStack(alignment: .leading, spacing: 0) {
                ForEach(Array(cs.enumerated()), id: \.offset) { k, c in
                    if k > 0 { RowDivider(inset: 46) }
                    HStack(alignment: .top, spacing: 12) {
                        PersonAvatar(name: c.author, size: 34)
                        VStack(alignment: .leading, spacing: 3) {
                            Text(c.author)
                                .font(.sBody.weight(.semibold))
                            Text(c.text)
                                .font(.sBody)
                                .textSelection(.enabled)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        Spacer(minLength: 0)
                    }
                    .padding(.vertical, 10)
                }
            }
        }
    }

    private func questions(_ shown: [QuizFeedback.Row]) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            if shown.isEmpty {
                EmptyNote(text: emptyText)
            }
            ForEach(Array(shown.enumerated()), id: \.element.id) { k, r in
                if k > 0 {
                    Divider()
                        .padding(.vertical, 28)
                }
                QuizFeedbackQuestion(row: r, tint: tint)
                    .id("fbq-\(r.n)")
            }
        }
    }

    private var emptyText: String {
        switch filter {
        case 1: return "Nothing to review — every question was answered correctly."
        case 2: return "No question in this attempt was answered correctly."
        default: return "This attempt has no questions to show."
        }
    }
}

/// A question's chip in the feedback's column: its number on glass, in the colour of how it went.
private struct QuizVerdictChip: View {
    let n: Int
    let verdict: String

    private var ink: Color {
        switch verdict {
        case "right": return .green
        case "wrong": return .red
        case "partial": return .orange
        default: return .secondary
        }
    }

    var body: some View {
        Text("\(n)")
            .font(.sCallout.weight(.semibold).monospacedDigit())
            .foregroundStyle(ink)
            .frame(width: 38, height: 38)
            .background(Circle().fill(ink.opacity(verdict == "none" ? 0 : 0.18)))
            .glass(Circle())
    }
}

/// One question of a finished attempt, a section of the page: its verdict and points, its words, what was answered
/// (and the correct answer beside it where the quiz shows it, a matching question as its pairs), every option on
/// request, and the worked solution. A coloured line at their edge marks the answers and the solution — no wash, no box.
private struct QuizFeedbackQuestion: View {
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
        VStack(alignment: .leading, spacing: 18) {
            header
            RichText(html: row.html, size: 16)
            answers
            options
            solution
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Image(systemName: mark)
                .font(.sTitle2)
                .foregroundStyle(ink)
                .accessibilityHidden(true)
            Text("Question \(row.n)")
                .font(.sTitle3)
            Text(row.verdictText)
                .font(.sBody.weight(.semibold))
                .foregroundStyle(ink)
            Spacer()
            if !row.score.isEmpty {
                Text(row.score)
                    .font(.sBody.weight(.semibold).monospacedDigit())
                    .foregroundStyle(ink)
            }
        }
    }

    @ViewBuilder
    private var answers: some View {
        if let m = row.match {
            matchTable(m)
        } else if row.essay || row.right.isEmpty {
            VStack(alignment: .leading, spacing: 16) {
                answer("Your Answer", row.yours, color: row.verdict == "none" ? nil : ink, empty: "No answer")
                if !row.right.isEmpty {
                    answer("Correct Answer", row.right, color: .green, empty: "—")
                }
            }
        } else {
            HStack(alignment: .top, spacing: 28) {
                answer("Your Answer", row.yours, color: row.verdict == "none" ? nil : ink, empty: "No answer")
                answer("Correct Answer", row.right, color: .green, empty: "—")
            }
        }
    }

    /// An answer under its label, a line of the verdict's colour at its edge; its words in that colour (an essay's in
    /// the text's own).
    private func answer(_ label: String, _ parts: [QuizFeedback.Part], color: Color?, empty: String) -> some View {
        let words: Color = row.essay ? Color.primary : (color ?? Color.primary)
        let edge: Color = (color ?? Color.secondary).opacity(0.65)
        return VStack(alignment: .leading, spacing: 6) {
            Text(label)
                .font(.sFootnote.weight(.semibold))
                .foregroundStyle(.secondary)
            if parts.isEmpty {
                Text(empty)
                    .italic()
                    .font(.sBody)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(Array(parts.enumerated()), id: \.offset) { _, p in
                    if !p.text.isEmpty {
                        Text(p.text)
                            .font(.sBody.weight(row.essay ? .regular : .semibold))
                            .foregroundStyle(words)
                            .textSelection(.enabled)
                            .fixedSize(horizontal: false, vertical: true)
                    } else if !p.html.isEmpty {
                        RichText(html: p.html, size: 15)
                    }
                }
            }
        }
        .padding(.leading, 14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(alignment: .leading) {
            Capsule().fill(edge).frame(width: 3)
        }
    }

    /// A matching question's pairs: a row each with a hairline between — the item, the match set (ticked or crossed),
    /// and the right one where it was missed and the quiz shows it.
    private func matchTable(_ m: QuizFeedback.MatchTable) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(m.rows.enumerated()), id: \.offset) { k, r in
                if k > 0 { Divider() }
                HStack(alignment: .firstTextBaseline, spacing: 18) {
                    Text(r.left)
                        .font(.sBody.weight(.semibold))
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Image(systemName: r.ok == true ? "checkmark" : (r.ok == false ? "xmark" : "arrow.right"))
                            .font(.sCallout.weight(.bold))
                            .foregroundStyle(matchInk(r.ok))
                            .accessibilityHidden(true)
                        VStack(alignment: .leading, spacing: 3) {
                            Text(r.mine ?? "No match set")
                                .font(.sBody)
                                .foregroundStyle(r.mine == nil ? Color.secondary : Color.primary)
                            if m.showRight, let right = r.right, r.ok != true {
                                Text("Correct: \(right)")
                                    .font(.sCallout.weight(.medium))
                                    .foregroundStyle(.green)
                            }
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .padding(.vertical, 11)
            }
        }
    }

    private func matchInk(_ ok: Bool?) -> Color {
        if ok == true { return .green }
        if ok == false { return .red }
        return .secondary
    }

    @ViewBuilder
    private var options: some View {
        if let opts = row.options, !opts.isEmpty {
            DisclosureGroup(isExpanded: $showOptions.animation(Motion.gentle)) {
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(Array(opts.enumerated()), id: \.element.id) { k, o in
                        if k > 0 { RowDivider(inset: 36) }
                        HStack(spacing: 12) {
                            Text(o.letter)
                                .font(.sCallout.weight(.bold))
                                .foregroundStyle(.secondary)
                                .frame(width: 24)
                            QuizOptionText(text: o.text, html: o.html)
                            if o.mine { pill("Yours", tint) }
                            if o.right { pill("Correct", .green) }
                        }
                        .padding(.vertical, 9)
                    }
                }
                .padding(.top, 6)
            } label: {
                Text(showOptions ? "Hide the Options" : "Show All \(opts.count) Options")
                    .font(.sBody.weight(.semibold))
            }
        }
    }

    private func pill(_ t: String, _ c: Color) -> some View {
        Text(t)
            .font(.sCaption.weight(.semibold))
            .foregroundStyle(c)
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(c.opacity(0.14), in: Capsule())
    }

    @ViewBuilder
    private var solution: some View {
        if let s = row.solution {
            VStack(alignment: .leading, spacing: 8) {
                Label("Worked Solution", systemImage: "lightbulb")
                    .font(.sHeadline)
                if !s.html.isEmpty {
                    RichText(html: s.html, size: 15)
                } else {
                    Text(s.text)
                        .font(.sBody)
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(.leading, 14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .overlay(alignment: .leading) {
                Capsule().fill(tint.opacity(0.55)).frame(width: 3)
            }
        } else if row.noSolution {
            Text("Your instructor left no worked solution for this question.")
                .font(.sCallout)
                .foregroundStyle(.secondary)
        }
    }
}

// MARK: - Small pieces

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

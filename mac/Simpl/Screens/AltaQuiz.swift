import AppKit
import SwiftUI
import WebKit

/// (1.3.17) A Knewton Alta assignment in a popup over the window, as a quiz is taken (QuizScreen): Close and the
/// assignment's name across the top; down the left, its objectives as the quiz's question tiles — the one being worked
/// on filled, a mastered one ticked, the others filling as they are mastered — with each by name, and the total
/// mastery bar; in the rest, the question, drawn by the app, with Check and then Alta's verdict and Continue in the
/// floating bar at the foot. A question the app does not draw itself (a graph, a formula, a lesson) shows Alta's own
/// page in its place. AltaSession.swift keeps Alta open out of sight and talks to it.
struct AltaQuizScreen: View {
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @StateObject private var session: AltaSession
    @State private var ideal = AltaQuizScreen.idealSize()

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
                questionArea
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .frame(width: ideal.width, height: ideal.height)
        .background(PageGround())
        .tint(Theme.accent)
        .task { await session.start(engine: engine) }
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
            GlassGroup(spacing: 8) {
                Button {
                    withAnimation(Motion.gentle) { session.showPage.toggle() }
                } label: {
                    Label(session.showPage ? "Show Question" : "Alta's Page", systemImage: session.showPage ? "list.bullet.rectangle" : "safari")
                }
                .glassButton()
                .disabled(session.phase == .loading || session.question?.kind == .page)
                .help(session.showPage ? "Back to the question as Simpl draws it" : "See this step on Alta's own page")
            }
        }
        .controlSize(.large)
        .padding(.horizontal, 22)
        .padding(.top, 16)
        .padding(.bottom, 12)
    }

    private var subtitle: String {
        var parts = ["Knewton Alta"]
        if let due = session.report?.due ?? session.overview?.due {
            parts.append("Due \(Date(timeIntervalSince1970: due / 1000).formatted(date: .abbreviated, time: .shortened))")
        }
        if (session.report?.current.source ?? "").uppercased() == "PRACTICE" { parts.append("Practice") }
        return parts.joined(separator: " · ")
    }

    // MARK: The question

    private var questionArea: some View {
        ZStack {
            // Alta's page, always in the same place (so it is never loaded again): out of sight unless shown
            AltaPageHost(web: session.web)
                .opacity(session.showPage ? 1 : 0)
                .allowsHitTesting(session.showPage)
                .accessibilityHidden(!session.showPage)
            if !session.showPage {
                AltaQuestionPane(session: session)
                    .transition(.opacity)
            }
        }
        .overlay(alignment: .bottom) {
            if !session.showPage || session.phase == .feedback { bar }
        }
        .animation(Motion.gentle, value: session.showPage)
    }

    @ViewBuilder
    private var bar: some View {
        switch session.phase {
        case .answering, .checking:
            if session.question?.kind != .page {
                QuizFloatingBar {
                    Button { session.check() } label: {
                        if session.phase == .checking {
                            ProgressView().controlSize(.small).frame(minWidth: 80)
                        } else {
                            Label("Check", systemImage: "checkmark.circle").frame(minWidth: 80)
                        }
                    }
                    .glassButton(prominent: true)
                    .keyboardShortcut(.return, modifiers: .command)
                    .disabled(!session.canCheck)
                    .help("Check your answer with Alta")
                }
            }
        case .feedback:
            QuizFloatingBar {
                if let v = session.verdict { AltaVerdictChip(verdict: v) }
                Button { session.next() } label: { Label("Continue", systemImage: "arrow.right").frame(minWidth: 90) }
                    .glassButton(prominent: true)
                    .keyboardShortcut(.return, modifiers: .command)
                    .help("On to what Alta gives next")
            }
        default:
            EmptyView()
        }
    }
}

/// Alta's page in the popup: the session's own web view, put where the question is.
private struct AltaPageHost: NSViewRepresentable {
    let web: WKWebView

    func makeNSView(context: Context) -> WKWebView { web }
    func updateNSView(_ v: WKWebView, context: Context) {}
}

// MARK: - The question pane

/// The question as the quiz draws one: its heading, what it asks (its maths typeset), and its answer — option rows, blanks
/// or a written answer — with what Alta said once it is checked.
private struct AltaQuestionPane: View {
    @ObservedObject var session: AltaSession
    @EnvironmentObject private var engine: Engine
    @State private var promptHeight: CGFloat = 24

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 28) {
                switch session.phase {
                case .failed(let why):
                    ContentUnavailableView("Knewton Alta could not be opened", systemImage: "exclamationmark.triangle", description: Text(why))
                        .frame(maxWidth: .infinity, minHeight: 320)
                case .start:
                    AltaStartCard(session: session)
                case .loading, .moving:
                    if session.report?.completed == true && session.phase == .loading {
                        complete
                    } else {
                        ProgressView(session.phase == .moving ? "Getting the next question…" : "Opening Knewton Alta…")
                            .font(.sBody)
                            .frame(maxWidth: .infinity, minHeight: 320)
                    }
                default:
                    if let q = session.question {
                        header
                        prompt(q)
                        answer(q)
                        if session.phase == .feedback, let v = session.verdict, !v.text.isEmpty {
                            AltaVerdictCard(verdict: v)
                        }
                    } else if session.report?.completed == true {
                        complete
                    }
                }
            }
            .frame(maxWidth: QuizLayout.column, alignment: .leading)
            .padding(.horizontal, 56)
            .padding(.top, 30)
            .padding(.bottom, QuizLayout.barClearance)
            .frame(maxWidth: .infinity)
        }
        .background(PageGround())
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .firstTextBaseline, spacing: 12) {
                Text("Question")
                    .font(.sTitle)
                    .tracking(-0.4)
                if let name = session.report?.current.name, !name.isEmpty {
                    Text(name)
                        .font(.sBody)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                Spacer(minLength: 8)
                if (session.report?.current.source ?? "").uppercased() == "PRACTICE" {
                    Text("Practice")
                        .font(.sFootnote.weight(.semibold))
                        .foregroundStyle(Theme.accent)
                        .padding(.horizontal, 9)
                        .padding(.vertical, 3)
                        .background(Capsule().fill(Theme.accent.opacity(0.14)))
                }
            }
            Divider()
        }
    }

    @ViewBuilder
    private func prompt(_ q: AltaQuestion) -> some View {
        let html = (q.kind == .blanks || q.kind == .dropdowns) ? (q.prompt ?? "") + AltaQuestionPane.marked(q.template ?? "") : (q.prompt ?? "")
        if !html.isEmpty {
            HTMLBlock(html: html, base: TeX.folder ?? engine.web.baseURL, size: 18, height: $promptHeight, math: true) { url in engine.openLink(url) }
                .frame(height: promptHeight)
        }
    }

    /// A cloze template with its blanks numbered (①, ②…) in place, for the fields under it.
    static func marked(_ template: String) -> String {
        let parts = template.components(separatedBy: "{{response}}")
        guard parts.count > 1 else { return template }
        var out = parts[0]
        for k in 1..<parts.count {
            out += "<span style=\"display:inline-block;min-width:2.2em;padding:0 6px;border-bottom:2px solid -apple-system-control-accent;text-align:center;font-weight:600;color:-apple-system-control-accent\">\(blankMark(k))</span>" + parts[k]
        }
        return "<div style=\"margin-top:12px\">\(out)</div>"
    }

    static func blankMark(_ k: Int) -> String {
        let marks = ["①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨", "⑩"]
        return k >= 1 && k <= marks.count ? marks[k - 1] : "(\(k))"
    }

    @ViewBuilder
    private func answer(_ q: AltaQuestion) -> some View {
        let locked = session.phase != .answering
        switch q.kind {
        case .choice:
            VStack(spacing: 6) {
                ForEach(Array(q.options.enumerated()), id: \.offset) { k, o in
                    let on = session.picks.contains(k)
                    Button { session.toggle(k) } label: {
                        AltaOptionRow(letter: String(Character(UnicodeScalar(UInt8(65 + min(k, 25))))), html: o.label, on: on, multiple: q.multiple)
                    }
                    .buttonStyle(AltaChoiceStyle(on: on))
                    .disabled(locked)
                    .keyboardShortcut(KeyEquivalent(Character(String(min(k + 1, 9)))), modifiers: []) // (1–9 pick, as in a quiz)
                }
            }
            if q.multiple {
                Text("Choose every answer that applies.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        case .blanks:
            VStack(alignment: .leading, spacing: 12) {
                ForEach(session.blanks.indices, id: \.self) { k in
                    HStack(spacing: 12) {
                        Text(AltaQuestionPane.blankMark(k + 1))
                            .font(.sTitle3.weight(.semibold))
                            .foregroundStyle(Theme.accent)
                            .frame(width: 28)
                        TextField("Your answer", text: $session.blanks[k])
                            .textFieldStyle(.roundedBorder)
                            .font(.sBody)
                            .disabled(locked)
                            .onSubmit { session.check() }
                    }
                }
            }
        case .dropdowns:
            VStack(alignment: .leading, spacing: 12) {
                ForEach(session.drops.indices, id: \.self) { k in
                    HStack(spacing: 12) {
                        Text(AltaQuestionPane.blankMark(k + 1))
                            .font(.sTitle3.weight(.semibold))
                            .foregroundStyle(Theme.accent)
                            .frame(width: 28)
                        Picker("Blank \(k + 1)", selection: $session.drops[k]) {
                            Text("Select…").tag("")
                            ForEach(q.choices.indices.contains(k) ? q.choices[k] : [], id: \.self) { c in Text(c).tag(c) }
                        }
                        .labelsHidden()
                        .frame(maxWidth: 360, alignment: .leading)
                        .disabled(locked)
                    }
                }
            }
        case .text:
            TextField("Your answer", text: $session.blanks[0])
                .textFieldStyle(.roundedBorder)
                .font(.sBody)
                .disabled(locked)
                .onSubmit { session.check() }
        case .longText:
            TextEditor(text: $session.blanks[0])
                .font(.sBody)
                .frame(minHeight: 160)
                .padding(8)
                .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Theme.card))
                .disabled(locked)
        case .page:
            EmptyView()
        }
    }

    private var complete: some View {
        VStack(spacing: 16) {
            Image(systemName: "checkmark.seal.fill")
                .font(.system(size: 44))
                .foregroundStyle(Theme.accent)
            Text("Assignment complete")
                .font(.sTitle2)
            Text("Every objective is mastered. Alta keeps your score; you can keep practicing.")
                .font(.sBody)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            Button("Keep Practicing") { session.next() }
                .glassButton(prominent: true)
                .controlSize(.large)
        }
        .frame(maxWidth: .infinity, minHeight: 320)
    }
}

/// An option's row, as the quiz's: its mark (a radio, or a box where several apply), its letter and its words.
private struct AltaOptionRow: View {
    let letter: String
    let html: String
    let on: Bool
    let multiple: Bool
    @EnvironmentObject private var engine: Engine
    @State private var height: CGFloat = 22

    var body: some View {
        HStack(alignment: .center, spacing: 16) {
            AltaMark(on: on, multiple: multiple)
            Text(letter)
                .font(.sBody.weight(.semibold))
                .foregroundStyle(on ? Theme.accent : Color.secondary)
                .frame(minWidth: 18)
            HTMLBlock(html: html, base: TeX.folder ?? engine.web.baseURL, size: 15, height: $height, math: true) { _ in }
                .frame(height: height)
                .allowsHitTesting(false)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 14)
        .frame(maxWidth: .infinity, minHeight: 58, alignment: .leading)
        .contentShape(Rectangle())
    }
}

/// A radio or a box as the Mac draws one.
private struct AltaMark: View {
    let on: Bool
    let multiple: Bool

    var body: some View {
        let tint = Theme.accent
        ZStack {
            if multiple {
                let shape = RoundedRectangle(cornerRadius: 5, style: .continuous)
                shape.fill(on ? tint : Color(nsColor: .controlBackgroundColor))
                shape.strokeBorder(on ? tint : Color.secondary.opacity(0.55), lineWidth: 1)
                if on { Image(systemName: "checkmark").font(.system(size: 11, weight: .bold)).foregroundStyle(.white) }
            } else {
                Circle().fill(on ? tint : Color(nsColor: .controlBackgroundColor))
                Circle().strokeBorder(on ? tint : Color.secondary.opacity(0.55), lineWidth: 1)
                if on { Circle().fill(Color.white).frame(width: 7, height: 7) }
            }
        }
        .frame(width: 18, height: 18)
        .animation(Motion.snappy, value: on)
        .accessibilityHidden(true)
    }
}

/// An option's row as a button: a soft wash under the pointer, the colour's once picked.
private struct AltaChoiceStyle: ButtonStyle {
    let on: Bool

    func makeBody(configuration: Configuration) -> some View {
        ChoiceBody(configuration: configuration, on: on)
    }

    private struct ChoiceBody: View {
        let configuration: ButtonStyleConfiguration
        let on: Bool
        @State private var hover = false

        var body: some View {
            let shape = RoundedRectangle(cornerRadius: 12, style: .continuous)
            let fill: Color = on ? Theme.accent.opacity(configuration.isPressed ? 0.22 : 0.14)
                : (configuration.isPressed ? Color.primary.opacity(0.08) : Color.primary.opacity(hover ? 0.045 : 0))
            configuration.label
                .background(shape.fill(fill))
                .contentShape(shape)
                .animation(Motion.hover, value: hover)
                .animation(Motion.snappy, value: on)
                .onHover { hover = $0 }
        }
    }
}

/// Alta's verdict in the bar: right, not right, or checked.
private struct AltaVerdictChip: View {
    let verdict: AltaVerdict

    private var word: String { verdict.correct == true ? "Correct" : verdict.correct == false ? "Not quite" : "Checked" }
    private var symbol: String { verdict.correct == true ? "checkmark.circle.fill" : verdict.correct == false ? "xmark.circle.fill" : "checkmark.circle" }
    private var color: Color { verdict.correct == true ? .green : verdict.correct == false ? .orange : .secondary }

    var body: some View {
        Label(word, systemImage: symbol)
            .font(.sBody.weight(.semibold))
            .foregroundStyle(color)
            .padding(.horizontal, 14)
            .padding(.vertical, 8)
            .glassCapsule(tint: color.opacity(0.25))
    }
}

/// What Alta wrote back under the question (its explanation, a worked step), as it showed it.
private struct AltaVerdictCard: View {
    let verdict: AltaVerdict

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label(verdict.correct == true ? "Correct" : verdict.correct == false ? "Not quite" : "From Alta",
                  systemImage: verdict.correct == true ? "checkmark.circle.fill" : verdict.correct == false ? "xmark.circle.fill" : "info.circle")
                .font(.sHeadline)
                .foregroundStyle(verdict.correct == true ? Color.green : verdict.correct == false ? Color.orange : Color.secondary)
            Text(verdict.text)
                .font(.sBody)
                .textSelection(.enabled)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(16)
        .card(radius: 14)
    }
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
                if let r = session.report {
                    if let low = r.current.low, let high = r.current.high, high > 0 {
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

/// (1.3.17) The assignment before its first question, as a quiz's intro: whether it is started, when it is due, its
/// objectives with Alta's estimate of the questions each takes, and Start (or Continue) — Alta's own button, pressed.
private struct AltaStartCard: View {
    @ObservedObject var session: AltaSession

    var body: some View {
        let o = session.overview
        let list = o?.objectives ?? []
        let low = list.compactMap(\.low).reduce(0, +)
        let high = list.compactMap(\.high).reduce(0, +)
        VStack(alignment: .leading, spacing: 26) {
            VStack(alignment: .leading, spacing: 10) {
                Text(o?.completed == true ? "Complete" : o?.started == true ? "In progress" : "Not started")
                    .font(.sFootnote.weight(.semibold))
                    .foregroundStyle(Theme.accent)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
                    .background(Capsule().fill(Theme.accent.opacity(0.14)))
                Text(o?.name ?? session.launch.title)
                    .font(.sTitle)
                    .tracking(-0.4)
                if let due = o?.due {
                    Label("Due \(Date(timeIntervalSince1970: due / 1000).formatted(date: .complete, time: .shortened))", systemImage: "calendar")
                        .font(.sBody)
                        .foregroundStyle(.secondary)
                }
            }
            if !list.isEmpty {
                VStack(alignment: .leading, spacing: 0) {
                    HStack(alignment: .firstTextBaseline) {
                        Text("Objectives").font(.sHeadline)
                        Spacer(minLength: 8)
                        if high > 0 {
                            Text(low == high ? "About \(Int(low)) questions in all" : "About \(Int(low))–\(Int(high)) questions in all")
                                .font(.sFootnote.monospacedDigit())
                                .foregroundStyle(.secondary)
                        }
                    }
                    .padding(.bottom, 12)
                    ForEach(Array(list.enumerated()), id: \.offset) { k, x in
                        if k > 0 { Divider().padding(.leading, 40) }
                        HStack(alignment: .firstTextBaseline, spacing: 14) {
                            Text("\(k + 1)")
                                .font(.sCallout.weight(.semibold).monospacedDigit())
                                .foregroundStyle(Theme.accent)
                                .frame(width: 26, height: 26)
                                .background(RoundedRectangle(cornerRadius: 7, style: .continuous).fill(Theme.accent.opacity(0.14)))
                            VStack(alignment: .leading, spacing: 3) {
                                Text(x.name).font(.sBody)
                                if let l = x.low, let h = x.high, h > 0 {
                                    Text(l == h ? "About \(Int(l)) questions" : "About \(Int(l))–\(Int(h)) questions")
                                        .font(.sFootnote)
                                        .foregroundStyle(.secondary)
                                }
                            }
                            Spacer(minLength: 0)
                        }
                        .padding(.vertical, 10)
                    }
                }
                .padding(18)
                .card(radius: 16)
            }
            Button { session.begin() } label: {
                Label(session.startWord, systemImage: "play.fill").frame(minWidth: 140)
            }
            .glassButton(prominent: true)
            .controlSize(.extraLarge)
            .keyboardShortcut(.defaultAction)
            .help("Begin with Alta: its first question opens here")
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

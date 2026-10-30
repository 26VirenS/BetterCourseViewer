import AppKit
import SwiftUI
import WebKit

/// (1.3.199) A Macmillan Learning or ALEKS assignment, as the popup over the window is asked for it.
struct LookLaunch: Identifiable, Hashable {
    let id = UUID()
    /// Its look's name (PageLook): "macmillan", "aleks".
    let look: String
    let course: String
    let assignment: String
    let title: String
    var dueLine: String? = nil
}

/// (1.3.199) A Macmillan Learning or ALEKS assignment in a popup over the window, as a Knewton Alta one is (AltaQuiz.swift)
/// — no longer the tool window 1.3.197 and 1.3.198 opened it in: the assignment's name and when it is due across the
/// top, with Simpl Look, Reload, Open in Window and Close; down the left, a rail of the assignment as Simpl reads it from
/// the tool's own page — Macmillan's questions as tiles (the one open marked, a solved one ticked, one tried and not
/// solved marked so), how many are done and the points; ALEKS's topic, its progress step by step, and ALEKS's word on
/// the last answer — and in the rest, the tool's own page in Simpl's look (PageLook's rail mode), where every question is
/// answered as on the tool's own page. A tile clicked opens that question (Macmillan's own row clicked in the page). The
/// page is zoomed out, as the browser's own zoom would, while the popup is narrower than the tool lays its page out.
/// (1.3.200) And the popup's own front end over the page, as Alta's popup is over Alta's: the question's heading the
/// popup's (which question, Previous and Next; ALEKS's topic and its steps), the question on a pane of Liquid Glass — the
/// tool's page clear over it, its words and answer fields the tool's own, punched through — and the tool's buttons and
/// verdict the popup's own along the foot, each pressing the tool's (its own bar out of sight in the page).
struct LookQuizScreen: View {
    let launch: LookLaunch
    @EnvironmentObject private var engine: Engine
    /// Its popup over the window (never blocking it: Shell/FloatingPopup.swift), which Close closes.
    @Environment(\.popup) private var dismiss
    @Environment(\.popupSize) private var popupSize
    @StateObject private var browser = ToolBrowser()
    @StateObject private var rail = LookRail()
    @State private var url: URL?
    /// The school's own page for the tool, when its one-time launch could not be had (ToolWebView.sendToolForm).
    @State private var sendForm: URL?
    @State private var error: String?
    @State private var dueLine: String?
    /// The page has finished loading once (the opening card goes then, or once the rail has heard from it).
    @State private var loadedOnce = false
    @AppStorage(PageLook.macmillan.key) private var macmillanLook = true
    @AppStorage(PageLook.aleks.key) private var aleksLook = true

    init(launch: LookLaunch) {
        self.launch = launch
        _dueLine = State(initialValue: launch.dueLine)
    }

    private var look: PageLook { PageLook.named(launch.look) }
    /// Most of the window it is over: the large popup's size.
    private var ideal: CGSize { popupSize ?? CGSize(width: 1040, height: 700) }
    private var lookOn: Binding<Bool> { look.name == PageLook.aleks.name ? $aleksLook : $macmillanLook }
    private var ready: Bool { error != nil || rail.heard || loadedOnce }

    var body: some View {
        VStack(spacing: 0) {
            topBar
            HStack(spacing: 0) {
                // (narrower in a small popup, so the tool's page keeps the room it is laid out for)
                LookRailView(rail: rail, look: look)
                    .frame(width: ideal.width >= 1040 ? 284 : 224)
                Divider()
                page
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .frame(width: ideal.width, height: ideal.height)
        .background(PageGround())
        .tint(Theme.accent)
        .task { await start() }
        .task {
            guard dueLine == nil else { return }
            dueLine = await CanvasDue.line(engine, course: launch.course, assignment: launch.assignment)
        }
        .onChange(of: browser.loading) { _, loading in
            rail.webView = browser.webView
            if !loading && url != nil && browser.progress > 0.5 { loadedOnce = true }
        }
        .onChange(of: macmillanLook) { _, on in if look.name == PageLook.macmillan.name { look.set(on, in: browser.webView) } }
        .onChange(of: aleksLook) { _, on in if look.name == PageLook.aleks.name { look.set(on, in: browser.webView) } }
        .onDisappear { browser.webView?.stopLoading() }
    }

    // MARK: The top

    private var topBar: some View {
        HStack(spacing: 14) {
            IconTile(symbol: look.symbol, color: look.color, size: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(launch.title)
                    .font(.sTitle3)
                    .lineLimit(1)
                Text(subtitle)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 8)
            AltaBrowserToggle(on: lookOn, label: "Simpl Look", icon: "sparkles",
                              helpOn: "Simpl's look is on: \(look.fullName)'s question on Liquid Glass, its buttons the popup's own. Click to show it as \(look.fullName) draws it.",
                              helpOff: "Show \(look.fullName)'s page in Simpl's look")
            PopupPins()
            GlassGroup(spacing: 8) {
                HStack(spacing: 8) { // (in a row: the glass container stacks what it holds)
                    Button { browser.reload() } label: { Label("Reload", systemImage: "arrow.clockwise") }
                        .labelStyle(.iconOnly)
                        .glassButton()
                        .help("Reload")
                        .disabled(url == nil)
                    Button {
                        // (the classic window, with Back, Forward, Share and Open in Browser — the work goes on there)
                        engine.openTool(.assignment(course: launch.course, id: launch.assignment, title: launch.title))
                        dismiss()
                    } label: { Label("Open in Window", systemImage: "macwindow") }
                        .labelStyle(.iconOnly)
                        .glassButton()
                        .help("Open in a window of its own, with Back, Forward and Open in Browser")
                    Button("Close") { dismiss() }
                        .glassButton()
                        .keyboardShortcut(.cancelAction)
                        .help("Close — your work is kept by \(look.fullName)")
                }
            }
        }
        .controlSize(.large)
        .padding(.horizontal, 22)
        .padding(.top, 16)
        .padding(.bottom, 12)
        .popupHeader()
    }

    /// "Macmillan Learning · Due Oct 12 at 11:59 PM".
    private var subtitle: String {
        [look.fullName, dueLine ?? ""].filter { !$0.isEmpty }.joined(separator: " · ")
    }

    // MARK: The page

    /// Simpl Look on: the popup's own front end over the tool's page. Off: the page as the tool draws it, on white.
    private var front: Bool { lookOn.wrappedValue }

    private var page: some View {
        VStack(spacing: 0) {
            if front && rail.heard && rail.onItem {
                LookQuestionHeader(rail: rail, look: look)
                    .transition(.opacity.combined(with: .offset(y: -6)))
            }
            ZStack {
                // (the pane of Liquid Glass the question is laid on — light, as the tool's page is drawn for light)
                if front {
                    Rectangle().fill(Color.clear)
                        .glassCard(radius: 26, tint: Color.white.opacity(0.5))
                        .environment(\.colorScheme, .light)
                        .transition(.opacity)
                } else {
                    Color.white
                }
                GeometryReader { g in
                    ZStack {
                        if let url {
                            ToolWebView(url: url, browser: browser, scripts: PageLook.scripts(front: true),
                                        messages: ["simplLook": rail.relay], externalTool: true, sendToolForm: sendForm, clear: true)
                        } else if let error {
                            ContentUnavailableView {
                                Label("\(look.fullName) could not open", systemImage: look.symbol)
                            } description: {
                                Text(error)
                            } actions: {
                                Button("Try Again") { Task { await start() } }
                                    .keyboardShortcut(.defaultAction)
                            }
                        }
                    }
                    .onAppear { rail.fit(width: g.size.width) }
                    .onChange(of: g.size.width) { _, w in rail.fit(width: w) }
                }
                .padding(front ? 6 : 0)
            }
            .padding(.horizontal, front ? 14 : 0)
            .padding(.top, front && !(rail.heard && rail.onItem) ? 14 : 0)
            .padding(.bottom, front ? 14 : 0)
        }
        // (the tool's buttons, whatever they say, as the popup's own — its main one prominent, one it has greyed out
        // greyed out — each pressing the tool's; its verdict on the last answer a pill at their left)
        .overlay(alignment: .bottom) {
            if front && ready && rail.hasButtons {
                LookButtonBar(rail: rail)
                    .padding(.horizontal, 14)
                    .padding(.bottom, 14)
                    .transition(.opacity.combined(with: .offset(y: 8)))
            }
        }
        .overlay {
            if !ready {
                LookOpening(look: look)
                    .transition(.opacity.combined(with: .scale(scale: 0.97)))
            }
        }
        .animation(Motion.gentle, value: ready)
        .animation(Motion.gentle, value: front)
        .animation(Motion.gentle, value: rail.onItem)
    }

    private func start() async {
        guard url == nil else { return }
        error = nil
        do {
            let a = try await engine.call("toolLaunch", ["course": launch.course, "assignment": launch.assignment], as: LaunchAnswer.self)
            guard let u = URL(string: a.url) else { throw EngineError.unreadable }
            if a.sessionless == false { sendForm = u }
            url = u
            NSLog("[Simpl] %@ popup: opened", look.name)
        } catch {
            self.error = error.localizedDescription
            NSLog("[Simpl] %@ popup: could not open: %@", look.name, error.localizedDescription)
        }
    }
}

/// While the tool's page is on its way: the tool's mark, its name and a spinner on a glass card over the ground.
private struct LookOpening: View {
    let look: PageLook

    var body: some View {
        VStack(spacing: 14) {
            IconTile(symbol: look.symbol, color: look.color, size: 44)
            Text("Opening \(look.fullName)…")
                .font(.sHeadline)
            ProgressView()
                .controlSize(.small)
        }
        .padding(.horizontal, 36)
        .padding(.vertical, 28)
        .glassCard(radius: 24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(PageGround())
        .accessibilityElement(children: .combine)
    }
}

// MARK: - The front end

/// (1.3.200) Over the question, the popup's own heading for it. Macmillan's: which question of how many, what Macmillan
/// says of it (its attempts, its points), Previous and Next, and its layout switch and grading settings under "…".
/// ALEKS's: the topic, its part of the course, and the topic's steps as ALEKS fills them.
private struct LookQuestionHeader: View {
    @ObservedObject var rail: LookRail
    let look: PageLook

    var body: some View {
        HStack(alignment: .center, spacing: 14) {
            if look.name == PageLook.aleks.name { aleks } else { macmillan }
        }
        .controlSize(.regular)
        .padding(.horizontal, 28)
        .padding(.top, 14)
        .padding(.bottom, 10)
    }

    private var question: LookRail.Question? { rail.questions.first(where: { $0.n == rail.here }) }

    @ViewBuilder
    private var macmillan: some View {
        let at = rail.questions.firstIndex(where: { $0.n == rail.here })
        let before = at.flatMap { $0 > 0 ? rail.questions[$0 - 1].n : nil }
        let after = at.flatMap { $0 + 1 < rail.questions.count ? rail.questions[$0 + 1].n : nil }
        VStack(alignment: .leading, spacing: 2) {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text("Question \(rail.here)")
                    .font(.sTitle3.weight(.semibold).monospacedDigit())
                    .contentTransition(.numericText(value: Double(rail.here)))
                Text("of \(rail.total)")
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
            if let q = question {
                Label(line(q), systemImage: q.solved ? "checkmark.circle.fill" : q.tried ? "exclamationmark.circle.fill" : "circle.dashed")
                    .font(.sFootnote)
                    .foregroundStyle(q.solved ? Color.green : q.tried ? Color.orange : Color.secondary)
                    .lineLimit(1)
                    .contentTransition(.opacity)
            }
        }
        .animation(Motion.snappy, value: rail.here)
        .accessibilityElement(children: .combine)
        Spacer(minLength: 8)
        GlassGroup(spacing: 6) {
            HStack(spacing: 6) {
                Button { if let before { rail.go(before) } } label: { Label("Previous Question", systemImage: "chevron.left") }
                    .labelStyle(.iconOnly)
                    .glassButton()
                    .disabled(before == nil)
                    .keyboardShortcut("[", modifiers: .command)
                    .help("Previous question")
                Button { if let after { rail.go(after) } } label: { Label("Next Question", systemImage: "chevron.right") }
                    .labelStyle(.iconOnly)
                    .glassButton()
                    .disabled(after == nil)
                    .keyboardShortcut("]", modifiers: .command)
                    .help("Next question")
            }
        }
        if !rail.layout.isEmpty || rail.policy {
            Menu {
                if !rail.layout.isEmpty {
                    Button(rail.layout) { rail.press(id: "layout", label: rail.layout) }
                }
                if rail.policy {
                    Button("Grading Policy") { rail.showPolicy() }
                }
            } label: {
                Label("More", systemImage: "ellipsis")
            }
            .labelStyle(.iconOnly)
            .menuStyle(.button)
            .menuIndicator(.hidden)
            .fixedSize()
            .glassButton()
            .help("Macmillan's layout and grading settings")
        }
    }

    /// "Solved in 1 attempt · +100", from Macmillan's own list.
    private func line(_ q: LookRail.Question) -> String {
        let said = q.sub.isEmpty ? (q.solved ? "Solved" : q.tried ? "Tried, not solved yet" : "Not answered yet") : q.sub
        return [said, q.points].filter { !$0.isEmpty }.joined(separator: " · ")
    }

    @ViewBuilder
    private var aleks: some View {
        VStack(alignment: .leading, spacing: 2) {
            if !rail.slice.isEmpty {
                Text(rail.slice.uppercased())
                    .font(.sCaption.weight(.semibold))
                    .foregroundStyle(.secondary)
                    .tracking(0.4)
                    .lineLimit(1)
            }
            Text(rail.topic)
                .font(.sTitle3.weight(.semibold))
                .lineLimit(2)
                .contentTransition(.opacity)
        }
        .animation(Motion.gentle, value: rail.topic)
        .accessibilityElement(children: .combine)
        Spacer(minLength: 8)
        if rail.total > 0 {
            VStack(alignment: .trailing, spacing: 6) {
                Text("\(max(rail.done, 0)) of \(rail.total)")
                    .font(.sCallout.weight(.semibold).monospacedDigit())
                    .contentTransition(.numericText(value: Double(max(rail.done, 0))))
                LookSteps(fills: rail.steps)
                    .frame(width: 140, height: 8)
            }
            .animation(Motion.snappy, value: rail.done)
            .accessibilityElement(children: .combine)
            .accessibilityLabel("Topic progress, \(max(rail.done, 0)) of \(rail.total)")
        }
    }
}

/// (1.3.200) Along the foot of the question, in Liquid Glass, the tool's own buttons as the popup's: its lesser ones at
/// the left with their symbols, its main one at the right in the accent (⌘↩), the tool's verdict a pill beside it.
private struct LookButtonBar: View {
    @ObservedObject var rail: LookRail

    var body: some View {
        // (docked: no haze of its own behind it — over the glass pane a haze reads as a smear)
        QuizFloatingBar(docked: true) {
            ForEach(rail.tools) { b in
                Button { rail.press(b) } label: {
                    if b.symbol.isEmpty { Text(b.label) } else { Label(b.label, systemImage: b.symbol) }
                }
                .glassButton()
                .disabled(!b.enabled)
                .help("\(b.label) (\(rail.lookName == PageLook.aleks.name ? "ALEKS" : "Macmillan")'s own)")
                .transition(.opacity.combined(with: .scale(scale: 0.94)))
            }
            Spacer(minLength: 12)
            if let v = rail.verdict {
                LookVerdictPill(verdict: v)
            }
            ForEach(rail.actions) { b in
                Button { rail.press(b) } label: { Text(b.label).frame(minWidth: 84) }
                    .glassButton(prominent: b.primary)
                    .disabled(!b.enabled)
                    .keyboardShortcut(b.primary ? KeyboardShortcut(.return, modifiers: .command) : nil)
                    .help(b.enabled ? "\(b.label) (⌘↩)" : "Not ready yet: answer the question first")
                    .transition(.opacity.combined(with: .scale(scale: 0.94)))
            }
        }
        .animation(Motion.snappy, value: rail.tools)
        .animation(Motion.snappy, value: rail.actions)
        .animation(Motion.snappy, value: rail.verdict)
    }
}

/// (1.3.200) The tool's verdict on the last answer, in its own words, as a pill (as Alta's, AltaVerdictPill): green with
/// a tick when right, red with a cross when wrong, plain otherwise.
private struct LookVerdictPill: View {
    let verdict: LookRail.Verdict

    var body: some View {
        let tone: Color = verdict.right ? .green : verdict.wrong ? .red : .secondary
        Label(verdict.text, systemImage: verdict.right ? "checkmark.circle.fill" : verdict.wrong ? "xmark.circle.fill" : "info.circle.fill")
            .font(.sCallout.weight(.semibold))
            .foregroundStyle(tone)
            .lineLimit(1)
            .padding(.horizontal, 14)
            .padding(.vertical, 7)
            .glassCapsule(tint: verdict.right || verdict.wrong ? tone.opacity(0.28) : nil)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(verdict.text)
            .transition(.opacity.combined(with: .scale(scale: 0.9)))
    }
}

// MARK: - What the page tells the rail

/// (1.3.199) What the tool's page tells the popup (extension/tools/*/look.js, `rail:`), as it changes; and what the rail
/// asks of the page back. Also the page's zoom: the width the tool lays its page out at, fitted to the popup's.
@MainActor
final class LookRail: ObservableObject {
    struct Question: Identifiable, Equatable {
        let n: Int
        /// Macmillan's own word for its state: "success" (solved), "default" (not answered), another (tried).
        let kind: String
        let label: String
        let sub: String
        let points: String
        var id: Int { n }
        var solved: Bool { kind == "success" }
        var tried: Bool { !solved && kind != "default" && !kind.isEmpty }
    }

    @Published private(set) var heard = false
    // Macmillan's
    @Published private(set) var questions: [Question] = []
    @Published private(set) var here = 0
    @Published private(set) var done = -1
    @Published private(set) var total = 0
    @Published private(set) var score = ""
    @Published private(set) var policy = false
    // ALEKS's
    @Published private(set) var slice = ""
    @Published private(set) var topic = ""
    @Published private(set) var fills: [Double] = []
    @Published private(set) var status = ""
    @Published private(set) var onQuestion = false
    // (1.3.200) the popup's front end: the tool's buttons — its lesser ones (Resources, AI Tutor; ALEKS's Explanation)
    // and its main ones (Submit Answer, Next Question; ALEKS's Check) — its verdict, and Macmillan's layout switch
    @Published private(set) var tools: [PageButton] = []
    @Published private(set) var actions: [PageButton] = []
    @Published private(set) var verdict: Verdict?
    @Published private(set) var layout = ""
    /// The look the page is in, as it last said ("macmillan", "aleks").
    private(set) var lookName = ""

    /// (1.3.200) One of the tool's own buttons, as the popup's front end draws it: pressed, it presses the tool's.
    struct PageButton: Identifiable, Equatable {
        let id: String
        let label: String
        let kind: String
        let primary: Bool
        let enabled: Bool

        /// Its symbol, from what it is ("" for its words alone).
        var symbol: String {
            let k = (kind + " " + label).lowercased()
            if k.contains("resource") { return "books.vertical" }
            if k.contains("-mat-") || k.contains("tutor") { return "sparkles" }
            if k.contains("solution") || k.contains("hint") { return "lightbulb" }
            if k.contains("explanation") || k.contains("explain") { return "text.book.closed" }
            if k.contains("calculator") { return "plus.forwardslash.minus" }
            if k.contains("video") { return "play.rectangle" }
            if k.contains("ebook") || k.contains("textbook") { return "book" }
            if k.contains("periodic") { return "atom" }
            if k.contains("help") { return "questionmark.circle" }
            return ""
        }
    }

    /// (1.3.200) The tool's word on the last answer: its own words, and whether it was right ("right", "wrong", "said").
    struct Verdict: Equatable {
        let text: String
        let kind: String
        var right: Bool { kind == "right" }
        var wrong: Bool { kind == "wrong" }
    }

    let relay = LookRelay()
    weak var webView: WKWebView? { didSet { zoom() } }
    private var wide: Double = 0
    private var width: CGFloat = 0
    private var logged = false
    private var frontLogged = false

    /// On a question (Macmillan's) or a topic (ALEKS's): the popup's own heading over it.
    var onItem: Bool { lookName == PageLook.aleks.name ? !topic.isEmpty : total > 0 && here > 0 }
    var hasButtons: Bool { !tools.isEmpty || !actions.isEmpty }

    /// How full each step of ALEKS's topic is: as ALEKS draws them, else as many full as are done.
    var steps: [Double] {
        if fills.count == total, total > 0 { return fills }
        let n = heard ? max(total, 0) : 5
        return (0..<n).map { $0 < done ? 1 : 0 }
    }

    init() { relay.rail = self }

    func take(_ o: [String: Any]) {
        guard let kind = o["rail"] as? String else {
            PageLook.log(o) // (what the look found, once a page: the log, and the screenshot suite's look_check)
            return
        }
        func int(_ k: String) -> Int { (o[k] as? NSNumber)?.intValue ?? -1 }
        func str(_ k: String) -> String { o[k] as? String ?? "" }
        func button(_ b: [String: Any], primary: Bool? = nil) -> PageButton? {
            guard let id = b["id"] as? String, let label = b["label"] as? String, !label.isEmpty else { return nil }
            return PageButton(id: id, label: label, kind: b["kind"] as? String ?? "", primary: primary ?? (b["primary"] as? Bool ?? false),
                              enabled: b["enabled"] as? Bool ?? true)
        }
        lookName = kind
        let f = o["front"] as? [String: Any]
        var lesser: [PageButton] = [], main: [PageButton] = [], said: Verdict? = nil
        if kind == PageLook.macmillan.name {
            lesser = (f?["tools"] as? [[String: Any]] ?? []).compactMap { button($0, primary: false) }
            main = (f?["main"] as? [String: Any]).flatMap { button($0, primary: true) }.map { [$0] } ?? []
            if let v = f?["verdict"] as? [String: Any], let t = v["text"] as? String, !t.isEmpty {
                said = Verdict(text: t, kind: v["kind"] as? String ?? "said")
            }
            let l = (f?["layout"] as? [String: Any])?["label"] as? String ?? ""
            if l != layout { layout = l }
        } else {
            let all = (f?["actions"] as? [[String: Any]] ?? []).compactMap { button($0) }
            lesser = all.filter { !$0.primary }
            main = all.filter(\.primary)
        }
        if lesser != tools { tools = lesser }
        if main != actions { actions = main }
        if said != verdict { verdict = said }
        if !frontLogged, hasButtons {
            frontLogged = true
            NSLog("[Simpl] %@ popup: front end, buttons %@", kind, (tools + actions).map(\.label).joined(separator: ", "))
        }
        if kind == PageLook.macmillan.name {
            questions = (o["rows"] as? [[String: Any]] ?? []).compactMap { (r: [String: Any]) -> Question? in
                guard let n = (r["n"] as? NSNumber)?.intValue else { return nil }
                return Question(n: n, kind: r["kind"] as? String ?? "default", label: r["label"] as? String ?? "Q\(n)",
                                sub: r["sub"] as? String ?? "", points: r["points"] as? String ?? "")
            }
            here = max(0, int("here"))
            done = int("done")
            total = max(0, int("total"))
            score = str("score")
            policy = o["policy"] as? Bool ?? false
        } else {
            slice = str("slice")
            topic = str("topic")
            done = int("done")
            total = max(0, int("total"))
            fills = (o["fills"] as? [Any] ?? []).compactMap { ($0 as? NSNumber)?.doubleValue }
            status = str("status")
            onQuestion = o["question"] as? Bool ?? false
        }
        heard = true
        if !logged {
            logged = true
            NSLog("[Simpl] %@ popup: rail heard, %@", kind, kind == PageLook.macmillan.name
                  ? "\(questions.count) questions, \(max(done, 0)) done, question \(here) open"
                  : "topic progress \(max(done, 0)) of \(total)")
        }
        if let w = (o["wide"] as? NSNumber)?.doubleValue, w > 0, w != wide {
            wide = w
            zoom()
        }
    }

    /// The popup's page as wide as it now is: the page zoomed to fit.
    func fit(width: CGFloat) {
        self.width = width
        zoom()
    }

    private var zoomSaid = -1.0

    /// The tool's page zoomed out (never in) as far as it takes to fit its width into the popup's — less a scroll bar's
    /// width and a little over, so the page never scrolls sideways for want of it — not past 66% (1.3.200: 70% left a
    /// small popup's page a few points too wide, scrolling sideways under the glass).
    private func zoom() {
        guard let v = webView, wide > 0, width > 28 else { return }
        let room = Double(width) - 28
        let z = max(0.66, min(1, room / wide))
        if abs(Double(v.pageZoom) - z) > 0.004 { v.pageZoom = CGFloat(z) }
        guard abs(zoomSaid - z) > 0.004 else { return }
        zoomSaid = z
        NSLog("[Simpl] look popup: page at %ld%% (its %ld wide in %ld)", Int((z * 100).rounded()), Int(wide), Int(room))
    }

    /// A question of Macmillan's opened: its own row in the page clicked.
    func go(_ n: Int) {
        guard n >= 1, questions.isEmpty || questions.contains(where: { $0.n == n }) else { return }
        webView?.evaluateJavaScript("window.__simplMMDo && window.__simplMMDo({ go: \(n) })", completionHandler: nil)
    }

    /// Macmillan's grading settings shown (its own button clicked).
    func showPolicy() {
        webView?.evaluateJavaScript("window.__simplMMDo && window.__simplMMDo({ policy: 1 })", completionHandler: nil)
    }

    /// (1.3.200) One of the tool's own buttons pressed, for the popup's: the page clicks it (look.js names each).
    func press(_ b: PageButton) { press(id: b.id, label: b.label) }

    func press(id: String, label: String) {
        let safe = id.filter { $0.isLetter || $0.isNumber || $0 == "-" }
        let fn = lookName == PageLook.aleks.name ? "__simplAKDo" : "__simplMMDo"
        NSLog("[Simpl] %@ popup: pressed %@", lookName, label)
        webView?.evaluateJavaScript("window.\(fn) && window.\(fn)({ press: '\(safe)' })", completionHandler: nil)
    }
}

/// The page's word, handed to the rail on the main thread (the content controller keeps its handler; the rail is not).
final class LookRelay: NSObject, WKScriptMessageHandler {
    weak var rail: LookRail?

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let o = message.body as? [String: Any] else { return }
        MainActor.assumeIsolated { rail?.take(o) }
    }
}

// MARK: - The rail

/// Down the left of the popup, on the popup's glass: the assignment as Simpl reads it from the tool's page.
private struct LookRailView: View {
    @ObservedObject var rail: LookRail
    let look: PageLook
    @Namespace private var marks

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                if look.name == PageLook.aleks.name { aleks } else { macmillan }
            }
            .padding(.horizontal, 18)
            .padding(.top, 10)
            .padding(.bottom, 24)
        }
        .scrollIndicators(.never)
        .redacted(reason: rail.heard ? [] : .placeholder)
        .animation(Motion.snappy, value: rail.heard)
    }

    // MARK: Macmillan

    private var doneCount: Int { rail.done >= 0 ? rail.done : rail.questions.filter(\.solved).count }
    private var totalCount: Int { rail.total > 0 ? rail.total : rail.questions.count }

    @ViewBuilder
    private var macmillan: some View {
        let share = totalCount > 0 ? Double(doneCount) / Double(totalCount) : 0
        HStack(spacing: 16) {
            LookRing(value: share)
                .frame(width: 66, height: 66)
            VStack(alignment: .leading, spacing: 3) {
                Text(rail.heard ? "\(doneCount) of \(totalCount)" : "0 of 0")
                    .font(.sTitle2.weight(.semibold).monospacedDigit())
                    .contentTransition(.numericText(value: Double(doneCount)))
                Text("questions done")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
                if let pts = points {
                    Text(pts)
                        .font(.sFootnote.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .contentTransition(.numericText())
                }
            }
            Spacer(minLength: 0)
        }
        .padding(14)
        .card(radius: 18)
        .animation(Motion.snappy, value: doneCount)
        .accessibilityElement(children: .combine)

        VStack(alignment: .leading, spacing: 12) {
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .firstTextBaseline) {
                    Text("Questions").font(.sHeadline)
                    Spacer(minLength: 6)
                    LookLegend().fixedSize()
                }
                Text("Questions").font(.sHeadline).frame(maxWidth: .infinity, alignment: .leading)
            }
            if rail.questions.isEmpty {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: 5), spacing: 6) {
                    ForEach(0..<10, id: \.self) { _ in
                        RoundedRectangle(cornerRadius: 9, style: .continuous).fill(Color.primary.opacity(0.07)).frame(height: 34)
                    }
                }
            } else {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: 5), spacing: 6) {
                    ForEach(rail.questions) { q in
                        LookTile(q: q, current: q.n == rail.here, marks: marks) { rail.go(q.n) }
                    }
                }
                .animation(Motion.snappy, value: rail.here)
            }
        }
        .padding(14)
        .card(radius: 18)

        if let q = rail.questions.first(where: { $0.n == rail.here }) {
            current(q)
                .id(q.n)
                .transition(.asymmetric(insertion: .opacity.combined(with: .offset(y: 6)), removal: .opacity))
        }

        if rail.policy {
            Button { rail.showPolicy() } label: {
                Label("Grading Policy", systemImage: "list.bullet.clipboard")
                    .frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .help("Macmillan's settings for this assignment: attempts, penalties and how it is scored")
        }
    }

    /// The question open: its words from Macmillan's list, its points, and the questions either side of it.
    private func current(_ q: LookRail.Question) -> some View {
        let at = rail.questions.firstIndex(of: q) ?? 0
        let before = at > 0 ? rail.questions[at - 1].n : nil
        let after = at + 1 < rail.questions.count ? rail.questions[at + 1].n : nil
        return VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Text("Question \(q.n)").font(.sHeadline)
                Spacer(minLength: 6)
                if !q.points.isEmpty {
                    Text(q.points)
                        .font(.sFootnote.weight(.semibold).monospacedDigit())
                        .padding(.horizontal, 8)
                        .padding(.vertical, 3)
                        .background(Capsule().fill(Color.green.opacity(0.16)))
                        .foregroundStyle(Color.green)
                }
            }
            Label(q.solved ? "Solved" : q.tried ? "Tried, not solved yet" : "Not answered yet",
                  systemImage: q.solved ? "checkmark.circle.fill" : q.tried ? "exclamationmark.circle.fill" : "circle.dashed")
                .font(.sCallout.weight(.medium))
                .foregroundStyle(q.solved ? Color.green : q.tried ? Color.orange : Color.secondary)
            if !q.sub.isEmpty {
                Text(q.sub).font(.sFootnote).foregroundStyle(.secondary)
            }
            HStack(spacing: 8) {
                Button { if let before { rail.go(before) } } label: {
                    Label("Previous", systemImage: "chevron.left").frame(maxWidth: .infinity)
                }
                .disabled(before == nil)
                Button { if let after { rail.go(after) } } label: {
                    Label("Next", systemImage: "chevron.right").frame(maxWidth: .infinity)
                }
                .disabled(after == nil)
            }
            .glassButton()
            .controlSize(.regular)
        }
        .padding(14)
        .card(radius: 18)
    }

    /// "1,196 of 2,300 points", from Macmillan's "+1196 pts/2300".
    private var points: String? {
        let nums = rail.score.split(whereSeparator: { !$0.isNumber && $0 != "." }).compactMap { Double($0) }
        let f = NumberFormatter()
        f.numberStyle = .decimal
        f.maximumFractionDigits = 1
        func s(_ d: Double) -> String { f.string(from: NSNumber(value: d)) ?? "\(d)" }
        switch nums.count {
        case 0: return nil
        case 1: return "\(s(nums[0])) points"
        default: return "\(s(nums[0])) of \(s(nums[1])) points"
        }
    }

    // MARK: ALEKS

    @ViewBuilder
    private var aleks: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(rail.slice.isEmpty ? "ALEKS" : rail.slice.uppercased())
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.secondary)
                .tracking(0.4)
                .lineLimit(2)
            Text(rail.topic.isEmpty ? (rail.heard ? "Choose a class or a topic in ALEKS" : "Your topic") : rail.topic)
                .font(.sTitle3.weight(.semibold))
                .fixedSize(horizontal: false, vertical: true)
                .contentTransition(.opacity)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .card(radius: 18)
        .animation(Motion.gentle, value: rail.topic)

        if rail.total > 0 || !rail.heard {
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .firstTextBaseline) {
                    Text("Topic Progress").font(.sHeadline)
                    Spacer(minLength: 6)
                    Text("\(max(rail.done, 0)) of \(rail.total)")
                        .font(.sHeadline.monospacedDigit())
                        .contentTransition(.numericText(value: Double(max(rail.done, 0))))
                }
                LookSteps(fills: steps)
                    .frame(height: 12)
                if !rail.status.isEmpty {
                    let tryAgain = rail.status.lowercased().contains("try") || rail.status.lowercased().contains("incorrect")
                    Label(rail.status, systemImage: tryAgain ? "arrow.uturn.backward.circle.fill" : "checkmark.circle.fill")
                        .font(.sCallout.weight(.semibold))
                        .foregroundStyle(tryAgain ? Color.orange : Color.green)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 6)
                        .background(Capsule().fill((tryAgain ? Color.orange : Color.green).opacity(0.14)))
                        .transition(.scale(scale: 0.92).combined(with: .opacity))
                }
                Text("A right answer fills a step; a wrong one can take one back. Fill them all to learn the topic.")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(14)
            .card(radius: 18)
            .animation(Motion.snappy, value: rail.status)
            .animation(Motion.snappy, value: rail.done)
        }
    }

    private var steps: [Double] { rail.steps }
}

/// How much is done, as a ring: the accent round a faint track, the share in its middle.
private struct LookRing: View {
    let value: Double

    var body: some View {
        ZStack {
            Circle().stroke(Color.primary.opacity(0.08), lineWidth: 7)
            Circle()
                .trim(from: 0, to: min(max(value, 0), 1))
                .stroke(Theme.accent, style: StrokeStyle(lineWidth: 7, lineCap: .round))
                .rotationEffect(.degrees(-90))
            Text("\(Int((value * 100).rounded()))%")
                .font(.sCallout.weight(.semibold).monospacedDigit())
                .contentTransition(.numericText(value: value))
        }
        .animation(Motion.fill, value: value)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Done")
        .accessibilityValue("\(Int((value * 100).rounded())) percent")
    }
}

/// What the tiles' marks mean, small, beside the heading.
private struct LookLegend: View {
    var body: some View {
        HStack(spacing: 8) {
            mark(Color.green, "Solved")
            mark(Color.orange, "Tried")
        }
        .font(.sCaption2)
        .foregroundStyle(.secondary)
    }

    private func mark(_ c: Color, _ word: String) -> some View {
        HStack(spacing: 3) {
            Circle().fill(c).frame(width: 6, height: 6)
            Text(word)
        }
    }
}

/// One question's tile: its number, the one open filled in the accent (its ring gliding from tile to tile), a solved
/// one washed green with a tick, one tried and not solved washed orange. A button: the question opens in the page.
private struct LookTile: View {
    let q: LookRail.Question
    let current: Bool
    let marks: Namespace.ID
    let open: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: open) {
            let shape = RoundedRectangle(cornerRadius: 9, style: .continuous)
            ZStack {
                shape.fill(fill)
                if current {
                    shape.fill(Theme.accent).matchedGeometryEffect(id: "here", in: marks)
                }
                if q.solved && !current {
                    Image(systemName: "checkmark").font(.system(size: 12, weight: .bold)).foregroundStyle(Color.green)
                } else {
                    Text("\(q.n)")
                        .font(.sCallout.weight(.semibold).monospacedDigit())
                        .foregroundStyle(current ? Color.white : q.tried ? Color.orange : Color.secondary)
                }
            }
            .frame(height: 34)
            .frame(maxWidth: .infinity)
            .scaleEffect(hover && !current ? 1.05 : 1)
            .contentShape(shape)
        }
        .buttonStyle(LookPress())
        .onHover { h in withAnimation(Motion.hover) { hover = h } }
        .help("\(q.label)\(q.sub.isEmpty ? "" : " — \(q.sub)")")
        .accessibilityLabel("Question \(q.n), \(q.solved ? "solved" : q.tried ? "tried" : "not answered")\(current ? ", open" : "")")
    }

    private var fill: Color {
        if q.solved { return Color.green.opacity(0.17) }
        if q.tried { return Color.orange.opacity(0.18) }
        return Color.primary.opacity(hover ? 0.11 : 0.07)
    }
}

/// A tile pressed: it gives a little under the pointer, at once, and springs back.
private struct LookPress: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.93 : 1)
            .animation(.spring(response: 0.22, dampingFraction: 0.7), value: configuration.isPressed)
    }
}

/// ALEKS's topic progress as its steps: a capsule each, filling green from the left as ALEKS fills it.
private struct LookSteps: View {
    let fills: [Double]

    var body: some View {
        GeometryReader { g in
            HStack(spacing: 4) {
                ForEach(fills.indices, id: \.self) { i in
                    ZStack(alignment: .leading) {
                        Capsule().fill(Color.primary.opacity(0.09))
                        Capsule()
                            .fill(Color.green)
                            .frame(width: max(0, (g.size.width - CGFloat(max(fills.count - 1, 0)) * 4) / CGFloat(max(fills.count, 1)) * CGFloat(min(max(fills[i], 0), 1))))
                    }
                }
            }
        }
        .animation(Motion.fill, value: fills)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Topic progress")
        .accessibilityValue("\(fills.filter { $0 >= 1 }.count) of \(fills.count) steps")
    }
}

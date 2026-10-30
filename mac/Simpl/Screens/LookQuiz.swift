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
                LookRailView(rail: rail, look: look)
                    .frame(width: ideal.width >= 960 ? 284 : 240)
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
                              helpOn: "Simpl's look is on: \(look.fullName)'s page on glass, its actions in a dock. Click to show it as \(look.fullName) draws it.",
                              helpOff: "Show \(look.fullName)'s page in Simpl's look")
            PopupPins()
            GlassGroup(spacing: 8) {
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

    private var page: some View {
        GeometryReader { g in
            ZStack {
                if let url {
                    ToolWebView(url: url, browser: browser, scripts: PageLook.scripts(rail: true),
                                messages: ["simplLook": rail.relay], externalTool: true, sendToolForm: sendForm)
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
                if !ready {
                    LookOpening(look: look)
                        .transition(.opacity.combined(with: .scale(scale: 0.97)))
                }
            }
            .onAppear { rail.fit(width: g.size.width) }
            .onChange(of: g.size.width) { _, w in rail.fit(width: w) }
        }
        .animation(Motion.gentle, value: ready)
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

    let relay = LookRelay()
    weak var webView: WKWebView? { didSet { zoom() } }
    private var wide: Double = 0
    private var width: CGFloat = 0
    private var logged = false

    init() { relay.rail = self }

    func take(_ o: [String: Any]) {
        guard let kind = o["rail"] as? String else {
            PageLook.log(o) // (what the look found, once a page: the log, and the screenshot suite's look_check)
            return
        }
        func int(_ k: String) -> Int { (o[k] as? NSNumber)?.intValue ?? -1 }
        func str(_ k: String) -> String { o[k] as? String ?? "" }
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

    /// The tool's page zoomed out (never in) as far as it takes to fit its width into the popup's, not past 70%.
    private func zoom() {
        guard let v = webView, wide > 0, width > 0 else { return }
        let z = max(0.7, min(1, Double(width) / wide))
        if abs(Double(v.pageZoom) - z) > 0.004 {
            v.pageZoom = CGFloat(z)
            NSLog("[Simpl] look popup: page zoomed to %ld%%", Int((z * 100).rounded()))
        }
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
            HStack(alignment: .firstTextBaseline) {
                Text("Questions").font(.sHeadline)
                Spacer(minLength: 6)
                LookLegend()
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

    /// How full each step of the topic is: as ALEKS draws them, else as many full as are done.
    private var steps: [Double] {
        if rail.fills.count == rail.total, rail.total > 0 { return rail.fills }
        let n = rail.heard ? max(rail.total, 0) : 5
        return (0..<n).map { $0 < rail.done ? 1 : 0 }
    }
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

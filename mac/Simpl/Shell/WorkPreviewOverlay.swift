import AppKit
import SwiftUI

// The work preview over the window (1.2.3; Components/WorkPreview.swift has what it shows and the rows that open it).
// MainShell puts it over the split view, above the tour. Nothing is drawn, and nothing takes a press, while no card is
// out. With one: the window lightly dimmed but for the row it grew from, and the card — Liquid Glass — growing out of
// that row on the house spring to its place: beside the row where there is room, under or over a wide one, else in the
// middle of the screen's side of the window. It folds back into the row when it closes. Under Reduce Motion it fades
// in and out in its place.

struct WorkPreviewOverlay: View {
    @EnvironmentObject private var engine: Engine
    @ObservedObject private var preview = WorkPreview.shared
    @ObservedObject private var tour = MacTour.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// Where the overlay itself is in the window: the rows' places are read in the window and made the overlay's own.
    @State private var origin = PreviewAnchor()
    /// The card's height as laid out at its width (the card grows to it).
    @State private var measured: CGFloat = 0
    /// The card last grown out (its token): each grows once, after it has been laid out at its row.
    @State private var grown = -1
    @FocusState private var focused: Bool

    var body: some View {
        GeometryReader { proxy in
            ZStack(alignment: .topLeading) {
                PreviewProbe(anchor: origin).allowsHitTesting(false)
                if let item = preview.item {
                    stage(item, size: proxy.size, top: proxy.safeAreaInsets.top)
                }
            }
        }
        .allowsHitTesting(preview.item != nil && preview.shown)
        // (the window gone somewhere else — a place picked, Back — takes the card with it)
        .onChange(of: engine.nav.current) { _, _ in preview.dismiss() }
    }

    // MARK: - Where it goes

    private struct Placed {
        var source: CGRect?
        var target: CGRect
        var lines: Int
    }

    /// The row's place and the card's, in the overlay's own coordinates; how many lines of the excerpt there is room for.
    private func geometry(size: CGSize, top: CGFloat) -> Placed {
        let me = origin.rect() ?? .zero
        let local = { (r: CGRect) in r.offsetBy(dx: -me.minX, dy: -me.minY) }
        let bounds = CGRect(origin: .zero, size: size)
        let source = preview.anchor?.rect().map(local).flatMap { r -> CGRect? in bounds.intersects(r) ? r : nil }
        // (the screen's side of the window: past the sidebar, under the toolbar)
        var lead: CGFloat = 0
        if let window = origin.view?.window, let side = TourFrames.shared.rect(.sidebar, in: window).map(local), side.maxX < size.width * 0.5 {
            lead = max(0, side.maxX)
        }
        let area = CGRect(x: lead, y: top, width: max(size.width - lead, 0), height: max(size.height - top, 0))
        let room = area.height - PreviewPlacement.margin * 2
        let lines = room >= 640 ? 8 : (room >= 500 ? 5 : 3)
        let target = PreviewPlacement.frame(source: source, area: area, height: measured > 0 ? measured : 300)
        return Placed(source: source, target: target, lines: lines)
    }

    // MARK: - The dim and the card

    private func stage(_ item: PreviewItem, size: CGSize, top: CGFloat) -> some View {
        let g = geometry(size: size, top: top)
        let out = preview.shown
        let shape = RoundedRectangle(cornerRadius: 22, style: .continuous)
        // (it starts as the row, and ends there; a row gone from view: a little smaller than the card, in its place)
        let start = g.source ?? g.target.insetBy(dx: g.target.width * 0.06, dy: g.target.height * 0.06)
        let r = out || reduceMotion ? g.target : start
        return ZStack(alignment: .topLeading) {
            PreviewVeil(hole: g.source, dimmed: out && tour.step == nil) // (the tour dims the window itself)
                .contentShape(PreviewHoleShape(hole: g.source), eoFill: true)
                .onTapGesture { preview.close() }
                .accessibilityHidden(true)
            card(item, g: g, frame: r, shape: shape, out: out)
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
    }

    private func card(_ item: PreviewItem, g: Placed, frame r: CGRect, shape: RoundedRectangle, out: Bool) -> some View {
        PreviewCard(item: item, width: g.target.width, lines: g.lines, onMeasure: measure)
            // (laid out at its full size from the first frame: the growing shape shows more of it as it opens)
            .frame(width: g.target.width, height: g.target.height, alignment: .topLeading)
            .opacity(out ? 1 : 0)
            .frame(width: max(r.width, 1), height: max(r.height, 1), alignment: .topLeading)
            .clipShape(shape)
            .glass(shape)
            .opacity(reduceMotion && !out ? 0 : 1)
            .contentShape(shape)
            // (a press on the card is the card's — the dim round it closes it — but a double-click's second, landing on
            // the card where it came up over its row, opens the work as the row would have)
            .onTapGesture { if preview.isSecondClick { preview.openWhole() } }
            .tourSpot(.preview)
            .focusable(true, interactions: .edit)
            .focused($focused)
            .focusEffectDisabled()
            .onKeyPress(phases: .down) { press in key(press) }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(Text("Preview of \(item.title)"))
            .accessibilityAddTraits(.isModal)
            .accessibilityAction(.escape) { preview.close() }
            .position(x: r.midX, y: r.midY)
            .id(preview.token)
            .task(id: preview.token) {
                try? await Task.sleep(nanoseconds: 80_000_000) // (once the window has let go of what had the keys)
                focused = true
                if grown != preview.token { // (never measured: out it comes all the same)
                    grown = preview.token
                    preview.grow()
                }
            }
    }

    /// The card's height, as laid out: the first time, out it grows (a moment later, so it is drawn at its row first);
    /// after that (what it says arriving), it grows to the new height on the same spring.
    private func measure(_ h: CGFloat) {
        guard h > 0 else { return }
        if abs(h - measured) > 0.5 {
            if preview.shown {
                withAnimation(WorkPreview.motion) { measured = h }
            } else {
                measured = h
            }
        }
        guard grown != preview.token else { return }
        grown = preview.token
        let t = preview.token
        Task { @MainActor in
            if WorkPreview.shared.token == t { WorkPreview.shared.grow() }
        }
    }

    /// Escape closes it; Return opens the work.
    private func key(_ press: KeyPress) -> KeyPress.Result {
        guard press.modifiers.isDisjoint(with: [.command, .control, .option]) else { return .ignored }
        switch press.key {
        case .escape:
            preview.close()
            return .handled
        case .return:
            guard preview.item?.openWhole != nil else { return .ignored }
            preview.openWhole()
            return .handled
        default:
            return .ignored
        }
    }
}

/// Where the card goes (1.2.8): beside its row, level with it, on the side the row leaves room — a row on the left of
/// the window opens to its right, one on the right to its left; a row too wide for the card beside it has it level with
/// it at the window's edge on that side (over the row's far end, never over or under the row); with no row, the middle
/// of the screen's side of the window — always inside it.
enum PreviewPlacement {
    static let width: CGFloat = 460
    static let margin: CGFloat = 16
    static let gap: CGFloat = 12

    static func frame(source: CGRect?, area: CGRect, height: CGFloat) -> CGRect {
        let w = min(width, max(area.width - margin * 2, 260))
        let h = min(height, max(area.height - margin * 2, 160))
        let minX = area.minX + margin, maxX = area.maxX - margin
        let minY = area.minY + margin, maxY = area.maxY - margin
        let centered = CGRect(x: max(minX, area.midX - w / 2), y: max(minY, area.midY - h / 2), width: w, height: h)
        guard let s = source else { return centered }
        let clampX = { (x: CGFloat) -> CGFloat in min(max(x, minX), max(minX, maxX - w)) }
        let clampY = { (y: CGFloat) -> CGFloat in min(max(y, minY), max(minY, maxY - h)) }
        let y = clampY(s.minY - 10)
        let right = CGRect(x: s.maxX + gap, y: y, width: w, height: h)
        let left = CGRect(x: s.minX - gap - w, y: y, width: w, height: h)
        let fits = { (r: CGRect) -> Bool in r.minX >= minX - 0.5 && r.maxX <= maxX + 0.5 }
        // (the side: where the row stands — a wide row, its words on the left, counts as the left)
        let rightFirst = s.midX <= area.midX + area.width * 0.12
        if let r = (rightFirst ? [right, left] : [left, right]).first(where: fits) { return r }
        return CGRect(x: clampX(rightFirst ? maxX - w : minX), y: y, width: w, height: h)
    }
}

/// The window lightly dimmed but for the row the card grew from.
private struct PreviewVeil: View {
    let hole: CGRect?
    let dimmed: Bool
    private static let dim = Theme.dynamic(light: NSColor(white: 0, alpha: 0.14), dark: NSColor(white: 0, alpha: 0.34))

    var body: some View {
        ZStack(alignment: .topLeading) {
            Rectangle().fill(PreviewVeil.dim.opacity(dimmed ? 1 : 0))
            if let hole {
                RoundedRectangle(cornerRadius: min(10, hole.height / 2), style: .continuous)
                    .frame(width: hole.width, height: hole.height)
                    .offset(x: hole.minX, y: hole.minY)
                    .blendMode(.destinationOut)
            }
        }
        .compositingGroup()
    }
}

/// The window, less the row (for presses: even-odd, so the row takes its own — a double-click's second opens it whole).
private struct PreviewHoleShape: Shape {
    let hole: CGRect?

    func path(in r: CGRect) -> Path {
        var p = Path(r)
        if let hole { p.addRoundedRect(in: hole, cornerSize: CGSize(width: min(10, hole.height / 2), height: min(10, hole.height / 2)), style: .continuous) }
        return p
    }
}

// MARK: - The card

/// What the card reads as it opens, beyond the row: an assignment's whole (its due date, points, where your work stands,
/// the grade, its instructions), or a page's.
private enum PreviewDetails {
    case assignment(AssignmentData)
    case page(PageData)
}

/// A fact on the card: its symbol and its words.
private struct PreviewFact: Identifiable {
    let symbol: String
    let text: String
    var strong = false
    var id: String { symbol + "|" + text }
}

/// The card's own button beside Open: Hand In, Take Quiz, Reply, See Feedback.
private struct PreviewAction {
    let title: String
    let symbol: String
    let run: () -> Void
}

/// The card's face: what the work is and where, its title, where it stands, its facts, the first words of what it says,
/// and what can be done with it. Laid out at the card's width and its own height, which it tells the overlay.
struct PreviewCard: View {
    let item: PreviewItem
    let width: CGFloat
    let lines: Int
    let onMeasure: (CGFloat) -> Void
    /// (1.2.15) Open under its row in a list: its row already says what it is, so no title; no Return for Open.
    var inline = false
    @EnvironmentObject private var engine: Engine
    @State private var details: PreviewDetails?
    @State private var excerpt: String?
    @State private var loading = false

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            info
            actions
        }
        .frame(width: width, alignment: .topLeading)
        .fixedSize(horizontal: false, vertical: true)
        .background {
            GeometryReader { p in
                Color.clear
                    .onAppear { onMeasure(p.size.height) }
                    .onChange(of: p.size.height) { _, h in onMeasure(h) }
            }
        }
        .task { await load() }
    }

    // MARK: What it shows

    private var assignment: AssignmentData? {
        if case .assignment(let d) = details { return d }
        return nil
    }

    private var page: PageData? {
        if case .page(let p) = details { return p }
        return nil
    }

    private var tint: Color {
        let hex = assignment?.color ?? page?.color ?? item.color
        return hex == nil ? .accentColor : Color(hex: hex)
    }

    /// "Assignment · BIO 101".
    private var kicker: String {
        let kind = PreviewFormat.text(assignment?.kind) ?? item.kind
        let course = PreviewFormat.text(assignment?.context) ?? PreviewFormat.text(page?.context) ?? item.course
        return [kind, course].compactMap { PreviewFormat.text($0) }.joined(separator: " · ")
    }

    /// Where it stands: the assignment's own word and grade once read (with the row's Feedback or New), else the row's.
    private var chips: [WorkFlag] {
        guard let d = assignment else { return item.flags }
        var out: [WorkFlag] = []
        if let s = d.status, !s.word.isEmpty { out.append(WorkFlag(word: s.word, kind: s.kind)) }
        if let g = d.grade, let score = g.score {
            var text = "\(PreviewFormat.number(score)) / \(PreviewFormat.number(g.possible ?? 0))"
            if let letter = PreviewFormat.text(g.letter) { text += " · \(letter)" }
            out.append(WorkFlag(word: text, kind: "info"))
        }
        if let late = PreviewFormat.text(d.grade?.late) { out.append(WorkFlag(word: late, kind: "warn")) }
        let words = Set(out.map(\.word))
        for f in item.flags where ["feedback", "new"].contains(f.word.lowercased()) && !words.contains(f.word) {
            out.append(f)
        }
        return out
    }

    private var facts: [PreviewFact] {
        var out: [PreviewFact] = []
        if let w = item.when {
            out.append(PreviewFact(symbol: "clock", text: w, strong: true))
        } else if let d = assignment {
            out.append(PreviewFact(symbol: "clock", text: PreviewFormat.text(d.due).map { "Due \($0)" } ?? "No due date", strong: true))
        }
        if let p = item.points ?? PreviewFormat.text(assignment?.points) { out.append(PreviewFact(symbol: "number.circle", text: p)) }
        if let s = PreviewFormat.text(assignment?.submitted) { out.append(PreviewFact(symbol: "checkmark.seal", text: s)) }
        if let a = PreviewFormat.text(assignment?.available) { out.append(PreviewFact(symbol: "lock.open", text: a)) }
        if let e = PreviewFormat.text(page?.edited) { out.append(PreviewFact(symbol: "pencil", text: e)) }
        if let d = PreviewFormat.text(item.detail) { out.append(PreviewFact(symbol: "info.circle", text: d)) }
        return out
    }

    /// Its own action beside Open, as its screen would put first: Take Quiz, See Feedback, Reply, Hand In, Open Tool.
    private var action: PreviewAction? {
        guard let url = item.url else { return nil }
        let title = item.title
        let e: Engine = self.engine // (held now: the card is gone by the time it runs)
        if let d = assignment {
            let handedIn = PreviewFormat.text(d.submitted) != nil || d.grade != nil
            if let q = PreviewFormat.text(d.quizUrl), !handedIn {
                return PreviewAction(title: "Take Quiz", symbol: "checklist") { e.act(on: q, title: title, feedback: false) }
            }
            if handedIn {
                return PreviewAction(title: "See Feedback", symbol: "text.bubble") { e.act(on: url, title: title, feedback: true) }
            }
            if let disc = PreviewFormat.text(d.discussionUrl) {
                return PreviewAction(title: "Reply", symbol: "bubble.left") { e.openWeb(disc, title: title) }
            }
            if d.canSubmit {
                return PreviewAction(title: d.resubmit == true ? "Resubmit" : "Hand In", symbol: "tray.and.arrow.up") { e.act(on: url, title: title, feedback: false) }
            }
            if d.toolUrl != nil {
                let quiz = d.ltiQuiz == true
                return PreviewAction(title: quiz ? "Take Quiz" : "Open Tool", symbol: quiz ? "checklist" : "puzzlepiece.extension") { e.act(on: url, title: title, feedback: false) }
            }
            return nil
        }
        guard let w = item.work else { return nil }
        if w.handedIn {
            return PreviewAction(title: "See Feedback", symbol: "text.bubble") { e.act(on: url, title: title, feedback: true) }
        }
        guard w.open else { return nil }
        let (label, symbol) = w.label
        return PreviewAction(title: label, symbol: symbol) { e.act(on: url, title: title, feedback: false) }
    }

    // MARK: The parts

    private var header: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 9) {
                IconTile(symbol: item.symbol, color: tint, size: 26)
                Text(kicker)
                    .font(.sCallout.weight(.semibold))
                    .foregroundStyle(tint)
                    .lineLimit(1)
                Spacer(minLength: 8)
                // (1.2.15: a close that reads as one — a labelled button, not a faint ×)
                Button { if inline { WorkPreview.shared.closeInline() } else { WorkPreview.shared.close() } } label: {
                    Label("Close", systemImage: "xmark.circle.fill")
                        .font(.sCallout.weight(.medium))
                }
                .buttonStyle(.bordered)
                .controlSize(.regular)
                .help("Close (Esc, or click anywhere else)")
            }
            if !inline {
                Text(assignment?.title ?? page?.title ?? item.title)
                    .font(.sTitle3)
                    .lineLimit(3)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.leading, 20)
        .padding(.trailing, 14)
        .padding(.top, 14)
    }

    private var info: some View {
        let flow = PillFlow(spacing: 6, lineSpacing: 6)
        let flags = self.chips
        return VStack(alignment: .leading, spacing: 12) {
            if !flags.isEmpty {
                flow {
                    ForEach(Array(flags.enumerated()), id: \.offset) { _, f in
                        StatusChip(text: f.word, tone: f.kind)
                    }
                }
            }
            if !facts.isEmpty {
                VStack(alignment: .leading, spacing: 6) {
                    ForEach(facts) { f in factRow(f) }
                }
            }
            excerptView
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 20)
        .padding(.top, 12)
    }

    private func factRow(_ f: PreviewFact) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 9) {
            Image(systemName: f.symbol)
                .foregroundStyle(.secondary)
                .frame(width: 18)
                .accessibilityHidden(true)
            Text(f.text)
                .foregroundStyle(f.strong ? .primary : .secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .font(f.strong ? .sCallout.weight(.medium) : .sCallout)
    }

    @ViewBuilder
    private var excerptView: some View {
        if let text = excerpt ?? item.excerpt {
            Text(text)
                .font(.sBody)
                .foregroundStyle(.secondary)
                .lineLimit(lines)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.top, 2)
                .transition(.opacity)
        } else if loading {
            HStack(spacing: 8) {
                ProgressView().controlSize(.small)
                Text("Loading…").font(.sCallout).foregroundStyle(.secondary)
            }
            .transition(.opacity)
        }
    }

    private var actions: some View {
        HStack(spacing: 10) {
            if item.toggle != nil, let done = item.done {
                Button { WorkPreview.shared.markDone(!done) } label: {
                    Label(done ? "Mark Not Done" : "Mark Done", systemImage: done ? "arrow.uturn.backward" : "checkmark")
                }
                .glassButton()
                .help(done ? "Mark it not done" : "Mark it done")
            }
            Spacer(minLength: 8)
            if let a = action {
                Button {
                    WorkPreview.shared.dismiss()
                    a.run()
                } label: {
                    Label(a.title, systemImage: a.symbol)
                }
                .glassButton()
            }
            if item.openWhole != nil {
                Button { WorkPreview.shared.openWhole() } label: {
                    Text(item.openLabel)
                }
                .glassButton(prominent: true)
                .keyboardShortcut(inline ? nil : .defaultAction)
                .help(inline ? item.openLabel : "\(item.openLabel) (Return)")
            }
        }
        .font(.sBody)
        .controlSize(.large)
        .padding(.horizontal, 20)
        .padding(.top, 18)
        .padding(.bottom, 18)
    }

    // MARK: Reading

    /// The assignment (or the page) behind the row, as its own screen reads it: what was kept from last time at once,
    /// then Canvas's answer. Anything else (a quiz, a discussion, an event) shows what its row knows.
    private func load() async {
        guard let url = item.url, let route = engine.nativeRoute(for: url, title: item.title) else { return }
        switch route {
        case .assignment(let course, let id):
            let args: [String: Any] = ["course": course, "id": id]
            if let kept = engine.kept("assignment", args, as: AssignmentData.self) { show(.assignment(kept), animated: false) }
            loading = true
            let fresh = try? await engine.call("assignment", args, as: AssignmentData.self)
            if let fresh { show(.assignment(fresh), animated: true) }
        case .page(let ctx, let slug):
            let args: [String: Any] = slug.isEmpty ? ["ctx": ctx] : ["ctx": ctx, "slug": slug]
            if let kept = engine.kept("page", args, as: PageData.self) { show(.page(kept), animated: false) }
            loading = true
            let fresh = try? await engine.call("page", args, as: PageData.self)
            if let fresh { show(.page(fresh), animated: true) }
        default:
            return
        }
        withAnimation(Motion.gentle) { loading = false }
    }

    private func show(_ d: PreviewDetails, animated: Bool) {
        let html: String
        switch d {
        case .assignment(let a): html = a.html
        case .page(let p): html = p.html
        }
        let text = PreviewText.plain(html)
        withAnimation(animated ? Motion.gentle : nil) {
            details = d
            excerpt = text
        }
    }
}

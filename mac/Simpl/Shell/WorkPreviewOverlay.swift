import AppKit
import SwiftUI

// The work preview over the window (1.2.3; Components/WorkPreview.swift has what it shows and the rows that open it).
// MainShell puts it over the split view, above the tour. Nothing is drawn, and nothing takes a press, while no card is
// out. With one: the card — Liquid Glass — growing out of the row it came from on the house spring to its place: just
// under the row (1.2.20), or just over it when there is no room under, else in the middle of the screen's side of the
// window. (home-28) Nothing is dimmed behind it, as Windows' flyout is (it dimmed the page but not the toolbar, and
// Windows dims nothing). (1.3.79) Nor is anything round it caught: it never blocks the window — a press round it does
// what it does and leaves the card up, and going to another screen leaves it where it is — until its ✕, Escape (while
// it has the keys) or its row pressed again closes it; dragged, it moves. It folds back into the row when it closes.
// Under Reduce Motion it fades in and out in its place. (1.3.79) A row in a popup over the window — a counter's list,
// a popup of its own above this overlay — has its card come up in a popup over that one, just under the row
// (PopupPreviewCard), never hidden under it.

struct WorkPreviewOverlay: View {
    @EnvironmentObject private var engine: Engine
    @EnvironmentObject private var whereabouts: Whereabouts // (1.3.78: a move takes the card with it)
    @ObservedObject private var preview = WorkPreview.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// Where the overlay itself is in the window: the rows' places are read in the window and made the overlay's own.
    @State private var origin = PreviewAnchor()
    /// The card's height as laid out at its width (the card grows to it).
    @State private var measured: CGFloat = 0
    /// The card last grown out (its token): each grows once, after it has been laid out at its row.
    @State private var grown = -1
    /// (1.3.79) Where the card stays once it no longer follows its row — dragged, or the window gone somewhere else —
    /// its top left in the overlay (nil: by its row).
    @State private var held: CGPoint?
    /// Where the card is while it follows its row, for it to stay there.
    @State private var following: CGPoint = .zero
    /// Where a drag of the card started.
    @State private var dragFrom: CGPoint?
    /// (1.3.79) The card put up in a popup of its own (its token; -1: none), its row being in a popup over the window.
    @State private var popped = -1
    /// That card out at its place (its token), for its popup to go as the card folds back.
    @State private var poppedOut = -1
    @FocusState private var focused: Bool

    /// (1.3.79) The card's row is in a popup over the window (a counter's list), which this overlay is under: the card
    /// comes up in a popup of its own over it (`popUpCard`), not here.
    private var elsewhere: Bool {
        popped == preview.token || preview.anchor?.view?.window is PopupPanel
    }

    var body: some View {
        let away = elsewhere
        GeometryReader { proxy in
            ZStack(alignment: .topLeading) {
                PreviewProbe(anchor: origin).allowsHitTesting(false)
                if let item = preview.item, !away {
                    stage(item, size: proxy.size, top: proxy.safeAreaInsets.top)
                }
            }
        }
        .allowsHitTesting(preview.item != nil && preview.shown && !away)
        // (1.3.79: the window gone somewhere else — a place picked, Back — leaves the card where it is)
        .onChange(of: whereabouts.nav.current) { _, _ in
            if preview.item != nil, held == nil { held = following }
        }
        .onChange(of: preview.token) { _, _ in
            held = nil // (a new card follows its own row)
            dragFrom = nil
            popUpCard()
        }
        .onChange(of: preview.shown) { _, now in
            guard popped >= 0, popped == preview.token else { return }
            if now {
                poppedOut = popped
            } else if poppedOut == popped {
                // (the card folding back — its ✕, Escape: its popup goes with it)
                popped = -1
                poppedOut = -1
                engine.popups.close(PopupPreviewCard.key)
            }
        }
    }

    /// (1.3.79) A card whose row is in a popup over this window — a counter's list — comes up in a popup of its own over
    /// that one, just under its row from the row's start, or just over it when there is no room under, as the card here
    /// does; closed (its ✕, Escape, ⌘W), the card folds back. Any other card — or none — takes that popup away.
    private func popUpCard() {
        let t = preview.token
        guard preview.item != nil, let row = preview.anchor?.view, let rowWindow = row.window as? PopupPanel,
              let window = origin.view?.window, rowWindow.parent === window else {
            if popped >= 0 {
                popped = -1
                poppedOut = -1
                engine.popups.close(PopupPreviewCard.key)
            }
            return
        }
        // (the row on the screen — cut to what its list shows — and the room under or over it there for the card's
        // excerpt, as the card here has it)
        var r = row.convert(row.bounds, to: nil)
        if let scroll = row.enclosingScrollView {
            let shown = r.intersection(scroll.convert(scroll.bounds, to: nil))
            if !shown.isNull, shown.height >= 1 { r = shown }
        }
        let onScreen = rowWindow.convertToScreen(r)
        let visible = (rowWindow.screen ?? NSScreen.main)?.visibleFrame ?? onScreen
        let gap = PreviewPlacement.gap + PreviewPlacement.margin
        let room = max(onScreen.minY - gap - visible.minY, visible.maxY - gap - onScreen.maxY)
        let lines = room >= 640 ? 8 : (room >= 460 ? 5 : (room >= 340 ? 3 : 2))
        popped = t
        poppedOut = -1
        engine.popups.present(PopupPreviewCard.key, title: preview.item?.title ?? "Preview", layout: .under(onScreen), ground: false,
                              anchor: .top, onClose: { if WorkPreview.shared.token == t { WorkPreview.shared.close() } }) {
            PopupPreviewCard(lines: lines).id(t)
        }
        DispatchQueue.main.async {
            if WorkPreview.shared.token == t { WorkPreview.shared.grow() }
        }
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
        let lines = PreviewPlacement.lines(source: source, area: area)
        var target = PreviewPlacement.frame(source: source, area: area, height: measured > 0 ? measured : 300)
        if let held {
            // (1.3.79: held where it was left, kept in the window — its head at least)
            target.origin = CGPoint(x: min(max(held.x, 0), max(0, size.width - target.width)),
                                    y: min(max(held.y, top), max(top, size.height - min(target.height, 120))))
        }
        return Placed(source: source, target: target, lines: lines)
    }

    // MARK: - The card

    /// The card alone takes presses (1.3.79): everything round it is the window's, as ever.
    private func stage(_ item: PreviewItem, size: CGSize, top: CGFloat) -> some View {
        let g = geometry(size: size, top: top)
        let out = preview.shown
        let shape = RoundedRectangle(cornerRadius: 22, style: .continuous)
        // (it starts as the row, and ends there; a row gone from view: a little smaller than the card, in its place)
        let start = g.source ?? g.target.insetBy(dx: g.target.width * 0.06, dy: g.target.height * 0.06)
        let r = out || reduceMotion ? g.target : start
        return ZStack(alignment: .topLeading) {
            card(item, g: g, frame: r, shape: shape, out: out)
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
        .onChange(of: g.target.origin, initial: true) { _, o in
            if held == nil { following = o }
        }
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
            // (a press on the card is the card's — but a double-click's second, landing on the card where it came up over
            // its row, opens the work as the row would have)
            .onTapGesture { if preview.isSecondClick { preview.openWhole() } }
            // (1.3.79) dragged, the card moves, and stays where it is let go
            .gesture(
                DragGesture(minimumDistance: 3, coordinateSpace: .global)
                    .onChanged { v in
                        let from = dragFrom ?? g.target.origin
                        if dragFrom == nil { dragFrom = from }
                        held = CGPoint(x: from.x + v.translation.width, y: from.y + v.translation.height)
                    }
                    .onEnded { _ in dragFrom = nil }
            )
            .tourSpot(.preview)
            .focusable(true, interactions: .edit)
            .focused($focused)
            .focusEffectDisabled()
            .onKeyPress(phases: .down) { press in key(press) }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(Text("Preview of \(item.title)"))
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

/// (1.3.79) The card for a row of a popup over the window (a counter's list), in a popup of its own over that one: the
/// card as the overlay draws it, on its glass, at the overlay's width; dragged, it moves; Escape or its ✕ folds it back,
/// and Return opens the work.
private struct PopupPreviewCard: View {
    /// The card's popup among the window's popups.
    static let key = "preview"

    let lines: Int
    @ObservedObject private var preview = WorkPreview.shared
    @FocusState private var focused: Bool

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 22, style: .continuous)
        if let item = preview.item {
            PreviewCard(item: item, width: PreviewPlacement.width, lines: lines, onMeasure: { _ in })
                .clipShape(shape)
                .glass(shape)
                .contentShape(shape)
                .popupHeader()
                .focusable(true, interactions: .edit)
                .focused($focused)
                .focusEffectDisabled()
                .onKeyPress(phases: .down) { press in key(press) }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(Text("Preview of \(item.title)"))
                .accessibilityAction(.escape) { preview.close() }
                .task {
                    try? await Task.sleep(nanoseconds: 80_000_000) // (once its popup has the keys)
                    focused = true
                }
        }
    }

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

/// Where the card goes (1.2.20): just under its row, from the row's start, as a menu from it — or just over it when
/// there is no room under — its excerpt cut to fit (`lines`); with no row, the middle of the screen's side of the
/// window — always inside it.
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
        let clampY = { (y: CGFloat) -> CGFloat in min(max(y, minY), max(minY, maxY - h)) }
        // (1.2.20) as a menu from its row, for every row: from the row's start, just under it — or just over it when
        // there is no room under — the row left in view; with room for neither, on the roomier side, kept in the window
        let x = min(max(s.minX + 8, minX), max(minX, maxX - w))
        let below = maxY - (s.maxY + gap), above = (s.minY - gap) - minY
        if below >= h { return CGRect(x: x, y: s.maxY + gap, width: w, height: h) }
        if above >= h { return CGRect(x: x, y: s.minY - gap - h, width: w, height: h) }
        let y = below >= above ? clampY(s.maxY + gap) : clampY(s.minY - gap - h)
        return CGRect(x: x, y: y, width: w, height: h)
    }

    /// (1.2.20) How many lines of what the work says the card shows: as many as the room under (or over) its row allows,
    /// so the card fits there.
    static func lines(source: CGRect?, area: CGRect) -> Int {
        let room: CGFloat
        if let s = source {
            room = max(area.maxY - margin - (s.maxY + gap), (s.minY - gap) - (area.minY + margin))
        } else {
            room = area.height - margin * 2
        }
        return room >= 640 ? 8 : (room >= 460 ? 5 : (room >= 340 ? 3 : 2))
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
        return hex == nil ? Theme.accent : Color(hex: hex)
    }

    /// "Assignment · BIO 101".
    private var kicker: String {
        let kind = PreviewFormat.text(assignment?.kind) ?? item.kind
        let course = PreviewFormat.text(assignment?.context) ?? PreviewFormat.text(page?.context) ?? item.course
        return [kind, course].compactMap { PreviewFormat.text($0) }.joined(separator: " · ")
    }

    /// Where it stands: the assignment's own word and grade once read (with the row's Feedback or New), else the row's.
    /// (planning-27) A graded piece says its score once — one chip, "13/16 · B−" — with no "Graded" (or, from an older
    /// page, the same score again) beside it; Late, Missing and Submitted stay, and a late penalty says Late for itself.
    private var chips: [WorkFlag] {
        guard let d = assignment else { return item.flags }
        var out: [WorkFlag] = []
        let graded = d.grade?.score != nil
        let late = PreviewFormat.text(d.grade?.late)
        if let s = d.status, !s.word.isEmpty {
            let word = s.word.lowercased()
            let sameScore = graded && s.word.filter { !$0.isWhitespace } == "\(PreviewFormat.number(d.grade?.score ?? 0))/\(PreviewFormat.number(d.grade?.possible ?? 0))"
            if !(graded && word == "graded") && !sameScore && !(late != nil && word == "late") {
                out.append(WorkFlag(word: s.word, kind: s.kind))
            }
        }
        if let g = d.grade, let score = g.score {
            var text = "\(PreviewFormat.number(score))/\(PreviewFormat.number(g.possible ?? 0))"
            if let letter = PreviewFormat.text(g.letter) { text += " · \(letter)" }
            out.append(WorkFlag(word: PreviewFormat.text(g.chip) ?? text, kind: "info"))
        }
        if let late { out.append(WorkFlag(word: late, kind: "warn")) }
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
        // (1.3.79) the row's own line only until the work's own facts are read: a counter's row would say its due time,
        // points or score again under them ("Due 11:59 PM" under "Due Oct 14 at 11:59 PM")
        if assignment == nil, let d = PreviewFormat.text(item.detail) { out.append(PreviewFact(symbol: "info.circle", text: d)) }
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
                return PreviewAction(title: "Take Quiz", symbol: "questionmark.square") { e.act(on: q, title: title, feedback: false) }
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
                return PreviewAction(title: quiz ? "Take Quiz" : "Open Tool", symbol: quiz ? "questionmark.square" : "puzzlepiece.extension") { e.act(on: url, title: title, feedback: false) }
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
                // (1.3.27) Canvas being asked again: a small spinner on this line, not a line of its own — the card no
                // longer grows and shrinks back as it updates
                if loading {
                    ProgressView()
                        .controlSize(.mini)
                        .help("Updating from Canvas…")
                        .accessibilityLabel("Updating")
                        .transition(.opacity)
                }
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
        // (1.3.32) a quiz: its own assignment, as the assignment's screen reads it — its grade, when it was handed in —
        // so a graded quiz shows its score wherever its card is, not only "Graded"
        if let url = item.url, let ids = quizIds(url) {
            let args: [String: Any] = ["course": ids.course, "quiz": ids.quiz]
            if let kept = engine.kept("quizAssignment", args, as: AssignmentData.self) { show(.assignment(kept), animated: false) }
            loading = true
            if let fresh = try? await engine.call("quizAssignment", args, as: AssignmentData.self) { show(.assignment(fresh), animated: true) }
            withAnimation(Motion.gentle) { loading = false }
            return
        }
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

    /// A Canvas quiz's course and quiz, from its address (…/courses/1/quizzes/2).
    private func quizIds(_ raw: String) -> (course: String, quiz: String)? {
        guard let u = engine.absolute(raw), u.host?.lowercased() == engine.web.baseURL.host?.lowercased() else { return nil }
        let p = u.path.split(separator: "/").map(String.init)
        guard p.count >= 4, p[0] == "courses", p[2] == "quizzes", Engine.numeric(p[1]), Engine.numeric(p[3]) else { return nil }
        return (p[1], p[3])
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

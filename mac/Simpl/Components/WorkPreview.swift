import AppKit
import SwiftUI

// A quick look at a piece of work (1.2.3), as the web's preview has it (extension/content/app/preview.js): a click on a
// row of work — the Dashboard's day, its List, a counter's panel, its activity; the Calendar's days, week and list; To
// Do — grows a card out of that row with what the work is: its kind and course, its title, when it is due, its points,
// where it stands and the first words of what it says, with the way through to it (Open, or Return), Hand In or Take
// Quiz where it waits on you, and Mark Done for a row with a tick. A click round it, Escape or its ✕ folds it back into
// the row. A double-click (or a ⌘-click) on a row opens the work whole, as a click did before; the row's own menu is as
// it was. The card itself is drawn over the window by WorkPreviewOverlay (Shell/WorkPreviewOverlay.swift).

/// A piece of work as its row knows it: enough for the card to come up at once, the rest read as it opens.
struct PreviewItem: Identifiable {
    let id: String
    var title: String
    /// What it is ("Assignment", "Quiz", "Discussion", "Page", "Event", "My task" …).
    var kind: String?
    var symbol: String
    var course: String?
    /// The course's colour (hex).
    var color: String?
    /// When it is due or happens ("Due today at 11:59 PM").
    var when: String?
    var points: String?
    /// Where it stands, as its row said (Missing, Late, Excused, Feedback …).
    var flags: [WorkFlag] = []
    /// What else the row said about it (a place, how it was handed in).
    var detail: String?
    /// The first words of what it says, where the row already had them (an activity item's).
    var excerpt: String?
    /// Ticked off (rows with a tick).
    var done: Bool?
    var url: String?
    /// Its own action (Hand In, Take Quiz, See Feedback), as its context menu has it.
    var work: WorkAction?
    /// Ticks it off or back (rows with a tick): Mark Done on the card.
    var toggle: ((Bool) -> Void)?
    /// What a click on the row did before: the work opened whole. Nil where there is nothing to open (a task of your own).
    var openWhole: (() -> Void)?
    /// The words on the card's main button.
    var openLabel = "Open"

    init(id: String, title: String, kind: String?, symbol: String, course: String?, color: String?) {
        self.id = id
        self.title = title
        self.kind = kind
        self.symbol = symbol
        self.course = course
        self.color = color
    }
}

/// The preview showing (one at a time), where it grew from, and whether it is out or folding back.
@MainActor
final class WorkPreview: ObservableObject {
    static let shared = WorkPreview()

    /// What the card shows (nil: no card).
    @Published private(set) var item: PreviewItem?
    /// The card is out at its place (false while it grows out of its row, and again as it folds back into it).
    @Published private(set) var shown = false
    /// Bumps with each card: a new one is a new card, grown from its own row.
    @Published private(set) var token = 0
    /// The row it grew out of, read again as it folds back (the list may have moved meanwhile).
    private(set) var anchor: PreviewAnchor?
    /// The last row pressed, and when.
    private var lastPress: (id: String, at: Date)?

    /// A press on the card itself this soon after a row's press opened it is that double-click's second (the card may
    /// have come up over its row, where the row would have taken it): the work opens whole.
    var isSecondClick: Bool {
        guard let last = lastPress, last.id == item?.id else { return false }
        return Date().timeIntervalSince(last.at) <= NSEvent.doubleClickInterval
    }

    /// The house spring, or a cross-fade's short curve under Reduce Motion.
    static var motion: Animation {
        NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? .easeInOut(duration: 0.2) : Motion.gentle
    }

    /// A row pressed: its preview — or, for a double-click or a ⌘-click, the work itself. The row whose card is out,
    /// pressed again, folds it back.
    func press(_ item: PreviewItem, from anchor: PreviewAnchor?) {
        // (the second press of the same row within a double-click's time is a double-click, whatever the event says)
        let now = Date()
        let again = lastPress.map { $0.id == item.id && now.timeIntervalSince($0.at) <= NSEvent.doubleClickInterval } ?? false
        lastPress = (item.id, now)
        if let open = item.openWhole, again || WorkPreview.wantsWhole() {
            lastPress = nil
            dismiss()
            open()
            return
        }
        if again { return } // (nothing to open whole, a task of your own: its card stays)
        if let current = self.item, current.id == item.id, shown {
            close()
            return
        }
        open(item, from: anchor)
    }

    /// The click that pressed the row was a double-click's second, or held ⌘.
    private static func wantsWhole() -> Bool {
        guard let e = NSApp.currentEvent else { return false }
        switch e.type {
        case .leftMouseDown, .leftMouseUp:
            return e.clickCount >= 2 || e.modifierFlags.contains(.command)
        default:
            return false // (a key, VoiceOver: clickCount is a mouse event's alone)
        }
    }

    /// A card for an item, at its row: the overlay grows it out once it has been laid out there (`grow`).
    func open(_ item: PreviewItem, from anchor: PreviewAnchor?) {
        var t = Transaction()
        t.disablesAnimations = true
        withTransaction(t) {
            self.anchor = anchor
            self.shown = false
            self.token += 1
            self.item = item
        }
    }

    /// Out of its row to its place.
    func grow() {
        guard item != nil, !shown else { return }
        withAnimation(WorkPreview.motion) { shown = true }
    }

    /// Folded back into its row, then gone.
    func close() {
        guard item != nil else { return }
        lastPress = nil // (a row pressed again after this is a press of its own, never a double-click's second)
        let t = token
        withAnimation(WorkPreview.motion) {
            shown = false
        } completion: { [weak self] in
            guard let self, self.token == t else { return }
            self.item = nil
            self.anchor = nil
        }
    }

    /// Gone at once (the work opened whole, the window gone somewhere else).
    func dismiss() {
        closeInline(animated: false)
        guard item != nil else { return }
        var t = Transaction()
        t.disablesAnimations = true
        withTransaction(t) {
            token += 1
            shown = false
            item = nil
            anchor = nil
        }
    }

    /// Open: the work whole, as its row opened it before.
    func openWhole() {
        guard let open = (item ?? inlineItem)?.openWhole else { return }
        dismiss()
        open()
    }

    /// Mark Done (or back): the card says so at once, and the row's own tick does the rest.
    func markDone(_ done: Bool) {
        if var it = inlineItem, item == nil, let toggle = it.toggle {
            it.done = done
            withAnimation(Motion.snappy) { inlineItem = it }
            toggle(done)
            return
        }
        guard var it = item, let toggle = it.toggle else { return }
        it.done = done
        withAnimation(Motion.snappy) { item = it }
        toggle(done)
    }

    // MARK: - Inline (1.2.15)

    /// The row whose preview is open in its list, under it (lists: the Dashboard's, a counter's, To Do); nil: none.
    @Published private(set) var inlineID: String?
    /// What that preview shows.
    @Published private(set) var inlineItem: PreviewItem?
    private var inlineAnchor: PreviewAnchor?
    private var monitor: Any?

    /// A row of a list pressed: its preview opens under it, in the list — or, a double-click or ⌘-click, the work
    /// itself; the row whose preview is open, pressed again, closes it.
    func pressInline(_ item: PreviewItem, from anchor: PreviewAnchor?) {
        let now = Date()
        let again = lastPress.map { $0.id == item.id && now.timeIntervalSince($0.at) <= NSEvent.doubleClickInterval } ?? false
        lastPress = (item.id, now)
        if let open = item.openWhole, again || WorkPreview.wantsWhole() {
            lastPress = nil
            dismiss()
            open()
            return
        }
        if again { return }
        if inlineID == item.id {
            closeInline()
            return
        }
        withAnimation(WorkPreview.motion) {
            inlineID = item.id
            inlineItem = item
        }
        inlineAnchor = anchor
        watch()
    }

    /// The preview in its list closed (its Close, Escape, a click anywhere round it).
    func closeInline(animated: Bool = true) {
        unwatch()
        guard inlineID != nil else { return }
        lastPress = nil
        if animated {
            withAnimation(WorkPreview.motion) {
                inlineID = nil
                inlineItem = nil
            }
        } else {
            inlineID = nil
            inlineItem = nil
        }
        inlineAnchor = nil
    }

    /// While one is open: a click outside its row and preview closes it (the click still does what it does), and
    /// Escape closes it first.
    private func watch() {
        guard monitor == nil else { return }
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .keyDown]) { [weak self] e in
            guard let self, self.inlineID != nil else { return e }
            if e.type == .keyDown {
                if e.keyCode == 53 { // (Escape)
                    self.closeInline()
                    return nil
                }
                return e
            }
            guard let view = self.inlineAnchor?.view, let window = view.window, e.window === window else {
                DispatchQueue.main.async { self.closeInline() }
                return e
            }
            let r = view.convert(view.bounds, to: nil)
            if !r.contains(e.locationInWindow) { DispatchQueue.main.async { self.closeInline() } }
            return e
        }
    }

    private func unwatch() {
        if let m = monitor { NSEvent.removeMonitor(m) }
        monitor = nil
    }
}

// MARK: - Where a row is

/// Where a row is in its window, read from AppKit through an unseen view under it — as the tour reads the parts it
/// lights (TourFrames, Shell/Tour.swift) — so the card grows from exactly that row however the window is laid out.
final class PreviewAnchor {
    weak var view: NSView?

    /// In the window's content from the top left, cut to what its scroll view shows; nil once it is gone or out of view.
    @MainActor
    func rect() -> CGRect? {
        guard let v = view, let window = v.window, !v.isHiddenOrHasHiddenAncestor else { return nil }
        var r = v.convert(v.bounds, to: nil)
        if let scroll = v.enclosingScrollView {
            r = r.intersection(scroll.convert(scroll.bounds, to: nil))
        }
        guard !r.isNull, r.width >= 1, r.height >= 1 else { return nil }
        return TourFrames.flip(r, window)
    }
}

/// The unseen view under a row (it takes no presses).
final class PreviewProbeView: NSView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

struct PreviewProbe: NSViewRepresentable {
    let anchor: PreviewAnchor

    func makeNSView(context: Context) -> PreviewProbeView {
        let v = PreviewProbeView()
        anchor.view = v
        return v
    }

    func updateNSView(_ v: PreviewProbeView, context: Context) {
        if anchor.view !== v { anchor.view = v }
    }
}

// MARK: - A row that previews

/// A row of work whose click grows its preview out of it (a double-click or ⌘-click opens it whole): a wash under the
/// pointer and a deeper one under a click, as RowLink's. `padded` gives it a row's own padding (as RowLink); off, the
/// label keeps its own (a calendar's item).
struct PreviewLink<Label: View>: View {
    let item: PreviewItem
    var padded = true
    var radius: CGFloat = 10
    /// (1.2.15) The preview opens under the row, in its list; off (1.2.19: every row again), the card grows out of the
    /// row over the window, level with it.
    var inline = false
    @ViewBuilder var label: () -> Label
    @State private var anchor = PreviewAnchor()
    @State private var rowWidth: CGFloat = 0
    @ObservedObject private var preview = WorkPreview.shared

    private var open: Bool { inline && preview.inlineID == item.id }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            withOpen(
                Button {
                    if inline { WorkPreview.shared.pressInline(item, from: anchor) } else { WorkPreview.shared.press(item, from: anchor) }
                } label: {
                    label()
                        .padding(.horizontal, padded ? 8 : 0)
                        .padding(.vertical, padded ? 9 : 0)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .buttonStyle(RowButtonStyle(radius: radius))
                .accessibilityHint(Text(open ? "Closes its preview" : "Shows a preview"))
            )
            if open, let shown = preview.inlineItem {
                PreviewCard(item: shown, width: max(rowWidth - 8, 260), lines: 5, onMeasure: { _ in }, inline: true)
                    .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Color.primary.opacity(0.06)))
                    .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(Color.primary.opacity(0.08)))
                    .padding(.leading, 4)
                    .padding(.top, 4)
                    .padding(.bottom, 8)
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }
        }
        .background { PreviewProbe(anchor: anchor).allowsHitTesting(false) }
        .background {
            GeometryReader { p in
                Color.clear
                    .onAppear { rowWidth = p.size.width }
                    .onChange(of: p.size.width) { _, w in rowWidth = w }
            }
        }
        .onAppear(perform: shot)
    }

    /// VoiceOver's Open: the work whole, without its preview.
    @ViewBuilder
    private func withOpen<V: View>(_ v: V) -> some View {
        if let open = item.openWhole {
            v.accessibilityAction(named: Text(item.openLabel), open)
        } else {
            v
        }
    }

    /// The screenshot suite (-SimplOpen preview): the first row that is on screen opens its preview (1.2.8: one laid out
    /// below the fold no longer takes it, so the card is pictured where it grows from).
    private func shot() {
        guard PreviewShot.wanted, !PreviewShot.taken else { return }
        let item = item
        let anchor = anchor
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 1_200_000_000)
            guard !PreviewShot.taken, anchor.rect() != nil else { return }
            PreviewShot.taken = true
            if inline { WorkPreview.shared.pressInline(item, from: anchor) } else { WorkPreview.shared.open(item, from: anchor) }
        }
    }
}

/// The screenshot suite's preview, asked for once at launch (-SimplOpen preview) and opened once.
@MainActor
enum PreviewShot {
    static let wanted = LaunchOpen.take("preview") != nil
    static var taken = false
}

// MARK: - From the rows

extension WorkAction {
    /// A piece of work known by its address and kind alone (a counter's row, the calendar's).
    init(url: String?, title: String, kind: String?, handedIn: Bool, open: Bool = true) {
        self.url = url
        self.title = title
        self.kind = kind
        self.handedIn = handedIn
        self.open = open
    }
}

extension PreviewItem {
    /// A row of work in Today's list or To Do (its planner kind from its id, "assignment:12"); `toggle` is its tick.
    @MainActor
    static func work(_ row: WorkRow, engine: Engine, toggle: ((Bool) -> Void)?) -> PreviewItem {
        let type = PreviewFormat.text(row.type) ?? row.id.split(separator: ":").first.map(String.init)
        let custom = row.custom == true
        let kind = custom ? "My task" : PreviewFormat.plannerKind(type)
        let course = [row.courseName, row.course].compactMap { PreviewFormat.text($0) }.first { $0 != kind }
        var item = PreviewItem(id: "work:\(row.id)", title: row.title, kind: kind, symbol: custom ? Glyph.item("task") : DashGlyph.work(type),
                               course: course, color: row.color)
        let day = PreviewFormat.date(row.date).map { PreviewFormat.dayWord($0) }
        item.when = PreviewFormat.when(day: day, time: row.time, due: !custom && PreviewFormat.isWork(type))
        item.points = PreviewFormat.points(in: row.meta ?? row.sub)
        item.flags = row.flag.map { [$0] } ?? []
        item.detail = custom ? PreviewFormat.text(row.meta) : nil
        item.done = row.done
        item.toggle = toggle
        if !custom, let url = PreviewFormat.text(row.url) {
            item.url = url
            if PreviewFormat.isWork(type) { item.work = WorkAction(row) }
            item.openWhole = { engine.openWeb(url, title: row.title) }
        }
        return item
    }

    /// A row of the Dashboard's List, under its day ("Today", "Saturday", "Oct 16").
    @MainActor
    static func dash(_ row: DashRow, day: String?, engine: Engine, toggle: ((Bool) -> Void)?) -> PreviewItem {
        let type = PreviewFormat.text(row.type) ?? row.id.split(separator: ":").first.map(String.init)
        let custom = row.custom == true
        let kind = PreviewFormat.text(row.kind) ?? PreviewFormat.plannerKind(type)
        let course = PreviewFormat.text(row.course).flatMap { c -> String? in c == kind ? nil : c }
        var item = PreviewItem(id: "dash:\(row.id)", title: row.title, kind: kind, symbol: DashGlyph.work(type), course: course, color: row.color)
        item.when = PreviewFormat.when(day: day, time: row.time, due: !custom && PreviewFormat.isWork(type))
        item.points = PreviewFormat.text(row.points)
        item.flags = row.flags ?? []
        item.done = row.done
        item.toggle = toggle
        if !custom, let url = PreviewFormat.text(row.url) {
            item.url = url
            if PreviewFormat.isWork(type) { item.work = WorkAction(row.workRow) }
            item.openWhole = { engine.openWeb(url, title: row.title) }
        }
        return item
    }

    /// A row of a counter's panel: its line under the title is "course · kind · points · when" (native-app.js sheetRow).
    @MainActor
    static func sheet(_ row: SheetRow, counter: String, engine: Engine) -> PreviewItem {
        let parts = (row.sub ?? "").components(separatedBy: " · ").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        let found: (String, String) = counter == "unread"
            ? ("Announcement", Glyph.item("announcement"))
            : (PreviewFormat.kind(of: row.url) ?? ("Assignment", Glyph.item("assignment")))
        let course = parts.first.flatMap { c -> String? in c == "—" ? nil : c }
        let kindWords: Set<String> = ["Assignment", "Quiz", "Discussion", "graded", "Page", "Event", "Peer review", "Announcement", "My task"]
        let rest = parts.dropFirst().filter { !kindWords.contains($0) && !$0.hasSuffix(" pts") && !$0.hasSuffix(" pt") }
        var item = PreviewItem(id: "sheet:\(counter):\(row.id)", title: row.title, kind: found.0, symbol: found.1, course: course, color: row.color)
        item.points = PreviewFormat.points(in: row.sub)
        item.detail = rest.isEmpty ? nil : PreviewFormat.capitalized(rest.joined(separator: " · "))
        if counter == "overdue" && row.quiet != true { item.flags = [WorkFlag(word: "Overdue", kind: "bad")] }
        if let url = PreviewFormat.text(row.url) {
            item.url = url
            item.openWhole = { engine.openWeb(url, title: row.title) }
            if counter != "unread" {
                item.work = WorkAction(url: url, title: row.title, kind: found.0.lowercased(), handedIn: counter == "graded" || row.quiet == true)
            }
        }
        return item
    }
}

// MARK: - Words

/// How the card words what its rows hand it.
enum PreviewFormat {
    /// The words, or nil when there are none.
    static func text(_ s: String?) -> String? {
        guard let s, !s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return s
    }

    /// A planner item's kind in words, by its type (store.js classify).
    static func plannerKind(_ type: String?) -> String? {
        switch type ?? "" {
        case "assignment", "": return "Assignment"
        case "quiz": return "Quiz"
        case "discussion_topic": return "Discussion"
        case "wiki_page": return "Page"
        case "calendar_event": return "Event"
        case "planner_note": return "My task"
        case "announcement": return "Announcement"
        case "assessment_request": return "Peer review"
        default: return nil
        }
    }

    /// A planner type that is work to hand in (it is due, and has a Hand In or a Take Quiz).
    static func isWork(_ type: String?) -> Bool {
        ["assignment", "quiz", "discussion_topic", ""].contains(type ?? "")
    }

    /// What an address is, by its path (…/assignments/12 is an assignment), with its symbol.
    static func kind(of url: String?) -> (String, String)? {
        guard let url, let u = URL(string: url, relativeTo: URL(string: "https://canvas.invalid")) else { return nil }
        let p = u.path.split(separator: "/").map(String.init)
        guard p.count >= 3 else { return nil }
        switch p[2] {
        case "assignments": return ("Assignment", Glyph.item("assignment"))
        case "quizzes": return ("Quiz", Glyph.item("quiz"))
        case "discussion_topics": return ("Discussion", Glyph.item("discussion"))
        case "announcements": return ("Announcement", Glyph.item("announcement"))
        case "pages", "wiki": return ("Page", Glyph.item("page"))
        case "files": return ("File", Glyph.item("file"))
        case "calendar_events": return ("Event", Glyph.item("event"))
        default: return nil
        }
    }

    /// "10 pts" out of a row's line ("Assignment · 10 pts · due 11:59 PM").
    static func points(in text: String?) -> String? {
        guard let text else { return nil }
        return text.components(separatedBy: " · ").map { $0.trimmingCharacters(in: .whitespaces) }.first { $0.hasSuffix(" pts") || $0.hasSuffix(" pt") }
    }

    /// "Due today at 11:59 PM", "Saturday at 3:00 PM", "Due Oct 16 at 11:59 PM", "Tomorrow, all day".
    static func when(day: String?, time: String?, due: Bool) -> String? {
        let d = (day ?? "").trimmingCharacters(in: .whitespaces)
        let t = (time ?? "").trimmingCharacters(in: .whitespaces)
        guard !d.isEmpty || !t.isEmpty else { return nil }
        let phrase: String
        if d.isEmpty {
            phrase = t
        } else if t.isEmpty {
            phrase = d
        } else if t.lowercased() == "all day" {
            phrase = "\(d), all day"
        } else {
            phrase = "\(d) at \(t)"
        }
        guard due else { return phrase }
        // ("Due today", "Due tomorrow": the relative days in the middle of the line are lower case)
        if !d.isEmpty, ["Today", "Tomorrow", "Yesterday"].contains(d) {
            return "Due " + d.lowercased() + String(phrase.dropFirst(d.count))
        }
        return "Due \(phrase)"
    }

    /// A day in words, as the Dashboard's List names them: Today, Tomorrow, a weekday this week, else its date.
    static func dayWord(_ date: Date) -> String {
        let cal = Calendar.current
        if cal.isDateInToday(date) { return "Today" }
        if cal.isDateInTomorrow(date) { return "Tomorrow" }
        if cal.isDateInYesterday(date) { return "Yesterday" }
        let apart = cal.dateComponents([.day], from: cal.startOfDay(for: Date()), to: cal.startOfDay(for: date)).day ?? 0
        if apart > 0 && apart < 7 { return date.formatted(.dateTime.weekday(.wide)) }
        if cal.isDate(date, equalTo: Date(), toGranularity: .year) { return date.formatted(.dateTime.month(.abbreviated).day()) }
        return date.formatted(.dateTime.month(.abbreviated).day().year())
    }

    private static let isoFractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    private static let isoPlain = ISO8601DateFormatter()

    /// A date as the page sends it (ISO 8601, with or without its milliseconds).
    static func date(_ iso: String?) -> Date? {
        guard let s = text(iso) else { return nil }
        return isoFractional.date(from: s) ?? isoPlain.date(from: s)
    }

    /// The first letter up ("due Oct 9 · not submitted" → "Due Oct 9 · not submitted").
    static func capitalized(_ s: String) -> String {
        guard let first = s.first else { return s }
        return first.uppercased() + String(s.dropFirst())
    }

    /// A score as Canvas writes one: 9, 9.5, 9.25.
    static func number(_ v: Double) -> String {
        guard v.isFinite else { return "–" }
        if v.rounded() == v, abs(v) < 1e9 { return String(Int(v)) }
        var s = String(format: "%.2f", v)
        while s.hasSuffix("0") { s.removeLast() }
        if s.hasSuffix(".") { s.removeLast() }
        return s
    }
}

/// Canvas's rich text as a few plain lines, for the card's excerpt (the screen itself draws it whole).
enum PreviewText {
    static func plain(_ html: String, limit: Int = 700) -> String? {
        guard !html.isEmpty else { return nil }
        var s = html
        s = s.replacingOccurrences(of: "<(script|style)[^>]*>[\\s\\S]*?</\\1>", with: " ", options: [.regularExpression, .caseInsensitive])
        s = s.replacingOccurrences(of: "<li[^>]*>", with: "\n• ", options: [.regularExpression, .caseInsensitive])
        s = s.replacingOccurrences(of: "<br\\s*/?>|</(p|div|ul|ol|h[1-6]|tr|blockquote|pre)>", with: "\n", options: [.regularExpression, .caseInsensitive])
        s = s.replacingOccurrences(of: "<[^>]+>", with: "", options: .regularExpression)
        let named: [(String, String)] = [
            ("&nbsp;", " "), ("&lt;", "<"), ("&gt;", ">"), ("&quot;", "\""), ("&#39;", "'"), ("&apos;", "'"),
            ("&rsquo;", "’"), ("&lsquo;", "‘"), ("&ldquo;", "“"), ("&rdquo;", "”"), ("&mdash;", "—"), ("&ndash;", "–"),
            ("&hellip;", "…"), ("&bull;", "•"), ("&amp;", "&"),
        ]
        for (k, v) in named { s = s.replacingOccurrences(of: k, with: v) }
        s = numericEntities(s)
        s = s.replacingOccurrences(of: "[ \t\u{00A0}]+", with: " ", options: .regularExpression)
        s = s.replacingOccurrences(of: " *\n *", with: "\n", options: .regularExpression)
        s = s.replacingOccurrences(of: "\n{3,}", with: "\n\n", options: .regularExpression)
        s = s.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !s.isEmpty else { return nil }
        if s.count > limit {
            s = String(s.prefix(limit)).trimmingCharacters(in: .whitespacesAndNewlines) + "…"
        }
        return s
    }

    /// &#8217; and &#x2019; as the characters they are.
    private static func numericEntities(_ s: String) -> String {
        guard s.contains("&#"), let re = try? NSRegularExpression(pattern: "&#([xX]?)([0-9a-fA-F]+);") else { return s }
        let ns = s as NSString
        var out = ""
        var last = 0
        for m in re.matches(in: s, range: NSRange(location: 0, length: ns.length)) {
            out += ns.substring(with: NSRange(location: last, length: m.range.location - last))
            let hex = m.range(at: 1).length > 0
            let digits = ns.substring(with: m.range(at: 2))
            if let v = UInt32(digits, radix: hex ? 16 : 10), let u = Unicode.Scalar(v) {
                out.unicodeScalars.append(u)
            } else {
                out += ns.substring(with: m.range)
            }
            last = m.range.location + m.range.length
        }
        out += ns.substring(from: last)
        return out
    }
}

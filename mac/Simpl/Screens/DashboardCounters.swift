import AppKit
import SwiftUI

// The Dashboard's six counters (1.2): each a glass tile with its symbol, its number and the line under it. Pressed, a
// tile opens a panel over itself (1.2.8: a panel's width, from the tile's corner) listing what it counted — (1.3.79) a
// popup over the window that never blocks it (Shell/FloatingPopup.swift): a click round it leaves it up, as going to
// another screen does; its ✕, Escape, ⌘W or the tile pressed again closes it, and its head drags it.
// (1.3.78) On a window 1024 points wide or wider they stand in one row of six, each a compact tile two lines tall.

/// A counter's look, as the web Dashboard's: its symbol and colour, and its name in full for the panel it opens.
enum DashCounterLook {
    static func symbol(_ key: String) -> String {
        switch key {
        case "today": return "clock"
        case "next": return "calendar"
        case "unread": return "megaphone"
        case "overdue": return "exclamationmark.circle"
        case "tomorrow": return "sunrise"
        case "graded": return "chart.bar"
        default: return "circle"
        }
    }

    /// (1.3.1) A colour worn: every counter's glyph in it; Regular, each in its own. Red means late and nothing else:
    /// Overdue keeps it whatever is worn, and Due today wears the accent.
    static func color(_ key: String) -> Color {
        if key == "overdue" { return alert }
        if Theme.themed { return Theme.accentIcon }
        switch key {
        case "today": return Theme.accent
        case "next": return Color(hex: "#34c759")
        case "unread": return Color(hex: "#ff9500")
        case "tomorrow": return Color(hex: "#ff9f0a")
        case "graded": return Color(hex: "#5856d6")
        default: return Theme.accent
        }
    }

    /// The red of something late: the system's, which reads on glass by day and by night.
    static let alert = Color(nsColor: .systemRed)

    /// The name for the panel's title until its own arrives, and for what is said of the tile.
    static func fullName(_ key: String, _ label: String) -> String {
        switch key {
        case "today": return "Due today"
        case "next": return "Next 7 days"
        case "unread": return "Announcements"
        case "overdue": return "Overdue"
        case "tomorrow": return "Due tomorrow"
        case "graded": return "Graded this week"
        default: return label
        }
    }

    /// What a tile counts, in full words with its number, for its tooltip and VoiceOver: "4 due today", "3 unread
    /// announcements", "2 graded this week" — whichever name the tile shows.
    static func spoken(_ key: String, _ label: String, _ value: Int?) -> String {
        guard let n = value else { return "\(fullName(key, label)): counting" }
        switch key {
        case "today": return "\(n) due today"
        case "tomorrow": return "\(n) due tomorrow"
        case "next": return "\(n) due in the next 7 days"
        case "overdue": return "\(n) overdue"
        case "unread": return "\(n) unread \(n == 1 ? "announcement" : "announcements")"
        case "graded": return "\(n) graded this week"
        default: return "\(fullName(key, label)): \(n)"
        }
    }
}

/// A counter's number: rolling to a new count on the house spring, a dash while it is still being counted. (home-25)
/// `stale`: the number kept from last time, shown faint while the live one is on its way — it rolls to the live one in
/// place, as the list under it changes in place.
struct DashCount: View {
    let value: Int?
    var alert = false
    var stale = false
    var size: CGFloat = 34

    var body: some View {
        Group {
            if let v = value {
                Text("\(v)").contentTransition(.numericText(value: Double(v)))
            } else {
                Text("–").foregroundStyle(.tertiary)
            }
        }
        .font(.system(size: size, weight: .bold, design: .rounded))
        .monospacedDigit()
        .foregroundStyle(alert ? DashCounterLook.alert : Color.primary)
        .opacity(stale && value != nil ? 0.45 : 1)
        .animation(Motion.hover, value: stale) // (critically damped: the live number settles in, no bounce)
    }
}

/// A counter's tile: its symbol and name, its number, the line under it. (1.3.78) In the row of six, a compact tile: its
/// name over its number, the line beside the number — cut short, or left out where there is no room to say it.
struct DashCounterTile: View {
    /// How a tile is laid out: tall, three across (or two); or compact, in the row of six — its symbol beside its name
    /// where every name in the row has room for one, else in the tile's corner beside its number.
    enum Style: Equatable {
        case tall
        case row(symbolBeside: Bool)
    }

    let counter: Counter
    /// (home-17) The name the row chose for it — its full one ("Announcements", "Graded this week") where every tile in
    /// the row has room for its own, else its short one ("Unread", "Graded") — and the line that goes with that name.
    let name: String
    let value: Int?
    let note: String?
    /// (home-25) The number kept from last time, shown faint until the live one is in.
    var stale = false
    var style: Style = .tall
    let action: () -> Void
    /// (1.3.1) Its photo's editor, from its context menu.
    @State private var editing = false

    private var alert: Bool { counter.tone == "red" && (value ?? 0) > 0 }
    private var radius: CGFloat { style == .tall ? 18 : 16 }

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
        Button(action: action) {
            Group {
                switch style {
                case .tall: tall
                case .row(let symbolBeside): compact(symbolBeside: symbolBeside)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background {
                // (1.3.1) its photo, frosted under its words (Settings ▸ Appearance, or its context menu); (home-27) it
                // fades out towards the tile's top, so nothing of it sits by the name or in the corner
                if let slot = PhotoSlot.card(counter.key) {
                    SlotPhotoView(slot: slot, fade: .tile)
                        .clipShape(shape)
                }
                // (home-1) something late: a red wash and a red edge of the tile's own, over its photo — drawn here, not
                // as the glass's tint, which macOS mutes in a window in the background — whatever is worn
                if alert {
                    shape.fill(DashCounterTile.alertWash)
                }
            }
            .overlay {
                if alert { shape.strokeBorder(DashCounterLook.alert.opacity(0.55), lineWidth: 1.5) }
            }
        }
        .buttonStyle(CardButtonStyle(radius: radius))
        .contextMenu {
            if PhotoSlot.card(counter.key) != nil {
                Button("Card Photo…") { editing = true }
            }
        }
        // (1.3.79: a popup over the window that never blocks it)
        .floatingPopup("card-photo:\(counter.key)", title: "Card Photo", isPresented: $editing) {
            if let slot = PhotoSlot.card(counter.key) {
                PhotoSlotSheet(slot: slot)
            }
        }
        .animation(Motion.snappy, value: value)
        .help(DashCounterLook.spoken(counter.key, counter.label, value) + (stale ? " · updating" : ""))
        .accessibilityLabel(DashCounterLook.spoken(counter.key, counter.label, value))
        .accessibilityValue(note ?? "")
        .accessibilityHint(Text("Shows what it counts"))
    }

    /// (home-1) The wash under a late count: solid enough to read as a warning in light and dark, on any photo.
    static let alertWash = Theme.dynamic(light: NSColor.systemRed.withAlphaComponent(0.14), dark: NSColor.systemRed.withAlphaComponent(0.24))

    private func symbol(size: CGFloat) -> some View {
        Image(systemName: DashCounterLook.symbol(counter.key))
            .font(.system(size: size, weight: .semibold))
            .foregroundStyle(DashCounterLook.color(counter.key))
    }

    /// Three lines: its symbol and name, its number, and the line under it.
    private var tall: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 7) {
                symbol(size: 14)
                Text(name)
                    .font(.sCallout.weight(.medium))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.85)
            }
            DashCount(value: value, alert: alert, stale: stale)
            Text(note?.isEmpty == false ? note! : " ")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .minimumScaleFactor(0.85)
                .opacity(stale ? 0.6 : 1)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 14)
    }

    // MARK: - Measuring the names

    /// (1.3.78) A compact tile's name, as it is drawn (the row measures every name with it), and what is beside it.
    static let rowLabelSize: CGFloat = 13
    static let rowSymbolWidth: CGFloat = 16
    static let rowSymbolGap: CGFloat = 6
    static let rowPadding: CGFloat = 12
    /// The compact tile's side padding with its symbol in the corner instead.
    static let rowCornerPadding: CGFloat = 10

    private static func widest(_ labels: [String], _ font: NSFont) -> CGFloat {
        (labels.map { ($0 as NSString).size(withAttributes: [.font: font]).width }.max() ?? 0).rounded(.up)
    }

    /// (1.3.78) Whether every name has room beside its symbol on a tile `tileWidth` across — measured as the tile draws
    /// them, so the row decides once for all six and no name is ever cut short.
    static func symbolsFit(_ labels: [String], tileWidth: CGFloat) -> Bool {
        rowFits(labels, tileWidth: tileWidth, symbolBeside: true)
    }

    /// (home-17) Whether every name has room on a compact tile `tileWidth` across, its symbol beside it or in the corner.
    static func rowFits(_ labels: [String], tileWidth: CGFloat, symbolBeside: Bool) -> Bool {
        let w = widest(labels, NSFont.systemFont(ofSize: rowLabelSize, weight: .medium))
        let room = symbolBeside ? rowPadding * 2 + rowSymbolWidth + rowSymbolGap : rowCornerPadding * 2
        return room + w + 2 <= tileWidth
    }

    /// (home-17) Whether every name has room on a tall tile `tileWidth` across, beside its symbol, as the tile draws it.
    static func tallFits(_ labels: [String], tileWidth: CGFloat) -> Bool {
        let w = widest(labels, NSFont.systemFont(ofSize: 14, weight: .medium))
        return 14 * 2 + 18 + 7 + w + 2 <= tileWidth
    }

    // MARK: - In the row of six

    /// Two lines: its name (its symbol beside it, where the row has room), then its number with the line beside it —
    /// in full where it fits, cut short where a few words of it still say something, else left out; its symbol in the
    /// corner where it is not beside the name. Never a third line.
    private func compact(symbolBeside: Bool) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: Self.rowSymbolGap) {
                if symbolBeside {
                    // (each symbol in the same square, so a tall one — Tomorrow's sunrise — moves no name down)
                    symbol(size: 12.5).frame(width: Self.rowSymbolWidth, height: Self.rowSymbolWidth)
                }
                Text(name)
                    .font(.system(size: Self.rowLabelSize, weight: .medium))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.8) // (a sidebar dragged wide on the narrowest window: smaller, never cut short)
            }
            HStack(alignment: .center, spacing: 6) {
                ViewThatFits(in: .horizontal) {
                    numberLine(note: note)
                    numberLine(note: note, cut: true)
                    numberLine(note: nil)
                }
                .layoutPriority(1)
                if !symbolBeside {
                    Spacer(minLength: 0)
                    symbol(size: 13)
                }
            }
        }
        .padding(.horizontal, symbolBeside ? Self.rowPadding : Self.rowCornerPadding)
        .padding(.vertical, 11)
    }

    /// The number, and the line beside it on its baseline: whole, or (`cut`) in whatever room is left once there is
    /// room for a few words of it, ending in an ellipsis.
    private func numberLine(note: String?, cut: Bool = false) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 7) {
            DashCount(value: value, alert: alert, stale: stale, size: 26)
                .fixedSize()
            if let note, !note.isEmpty {
                let line = Text(note)
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .truncationMode(.tail)
                    .opacity(stale ? 0.6 : 1)
                if cut {
                    line.frame(minWidth: 0, idealWidth: 64, maxWidth: .infinity, alignment: .leading)
                } else {
                    line
                }
            }
        }
    }
}

/// The panel a counter grows into: its header (the tile's symbol, name and number, larger) and what it counted —
/// what wants attention first, then, quieter, the rest of the same span under it — and the way to the whole list. Each
/// row previews its work under it, in the panel (1.2.3; home-20: never a second card over this one; a double-click opens
/// it); an overdue one the student has let go can be cleared.
struct DashCounterPanel: View {
    /// (1.2.8) How wide a panel opens (narrower only on a narrower page).
    static let width: CGFloat = 440 // (1.3.62: was 520)

    let counter: Counter
    let value: Int?
    let note: String?
    /// The panel's width: its words are laid out at it from the first frame, so they never squeeze while the tile
    /// grows (the growing shape shows more of them as it opens).
    let width: CGFloat
    /// (home-18) How tall its list may stand before it scrolls inside the panel: what the window has under the panel's
    /// header, so the panel stays in the window — about six rows on a laptop's window.
    var room: CGFloat = 380
    let close: () -> Void
    /// (1.2.8) How tall what it shows stands, for the page to size the floating panel to it.
    var onHeight: (CGFloat) -> Void = { _ in }
    /// (1.2.18) A row dismissed (the page's Overdue number drops with it), then Canvas's word on it.
    enum Cleared { case pressed, done(TodayCounts), failed }
    var onCleared: (Cleared) -> Void = { _ in }
    // (1.3.78: the engine as a plain value, to act through — not drawn again each time it changes: EngineLink)
    @Environment(\.engineLink) private var link
    private var engine: Engine { link.acting }
    @ObservedObject private var preview = WorkPreview.shared
    @State private var data: ItemsSheetData?

    /// One of its rows has its preview open under it.
    private var previewing: Bool { preview.inlineID?.hasPrefix("sheet:\(counter.key):") == true }
    @State private var error: String?

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 22, style: .continuous)
        content
            .frame(width: max(width, 280), alignment: .topLeading)
            .background {
                GeometryReader { p in
                    Color.clear
                        .onAppear { onHeight(p.size.height) }
                        .onChange(of: p.size.height) { _, h in onHeight(h) }
                }
            }
            .frame(minWidth: 0, maxWidth: .infinity, minHeight: 0, maxHeight: .infinity, alignment: .topLeading)
            .clipShape(shape)
            .glass(shape, tint: DashCounterLook.color(counter.key).opacity(0.10))
            .contentShape(shape)
            .onExitCommand { close() }
            .task { await load() }
            .onDisappear { if previewing { WorkPreview.shared.closeInline(animated: false) } }
    }

    private var content: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            if let d = data {
                // (1.2.2) no taller than the room the window has for it: the rest scrolls inside the panel (home-18:
                // about six rows on a laptop's window, the footer under them leading to the whole list); taller while
                // one of its rows has its preview open under it (1.2.15)
                let cap = max(150, min(room, 380))
                CappedScroll(max: previewing ? max(cap + 180, 420) : cap) { lists(d) }
                if !d.sections.isEmpty { footer }
            } else if let error {
                Text(error).font(.sCallout).foregroundStyle(.secondary)
            } else {
                HStack(spacing: 10) {
                    ProgressView().controlSize(.small)
                    Text("Loading…").font(.sCallout).foregroundStyle(.secondary)
                }
                .padding(.vertical, 6)
            }
        }
        .padding(.horizontal, 22)
        .padding(.top, 16)
        .padding(.bottom, 18)
    }

    /// Its symbol and name with its ✕, then its number with its line beside it — (home-10) the line given the panel's
    /// whole width, under the ✕ as well, before it wraps.
    private var header: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Image(systemName: DashCounterLook.symbol(counter.key))
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundStyle(DashCounterLook.color(counter.key))
                Text(data?.title ?? DashCounterLook.fullName(counter.key, counter.label))
                    .font(.sHeadline)
                Spacer(minLength: 8)
                Button(action: close) {
                    Image(systemName: "xmark")
                        .font(.system(size: 13, weight: .bold))
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(DashIconButtonStyle())
                .keyboardShortcut(.cancelAction)
                .help("Close (Esc)")
                .accessibilityLabel("Close")
            }
            HStack(alignment: .firstTextBaseline, spacing: 12) {
                DashCount(value: value, alert: counter.tone == "red" && (value ?? 0) > 0, size: 40)
                if let line = data?.note ?? note, !line.isEmpty {
                    Text(line)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
        // (1.3.79) its head drags its popup (its ✕, Escape, ⌘W or the tile pressed again close it)
        .popupHeader()
    }

    /// (home-18) The panel's way on to the whole list, so it is never a dead end: the work in To Do (which lists it day by
    /// day, overdue first), the grades in Grades, the announcements among the notifications.
    private var footer: some View {
        let way = seeAll
        return HStack {
            Button {
                close()
                engine.go(way.place)
            } label: {
                HStack(spacing: 3) {
                    Text(way.label)
                    Image(systemName: "chevron.right").font(.system(size: 10.5, weight: .semibold))
                }
                .font(.sCallout.weight(.medium))
            }
            .buttonStyle(.link)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 8)
    }

    private var seeAll: (label: String, place: Place) {
        switch counter.key {
        case "graded": return (label: "See All in Grades", place: .grades)
        case "unread": return (label: "See All in Notifications", place: .notifications)
        default:
            let n = value ?? 0
            return (label: n > 0 ? "See All \(n) in To Do" : "See All in To Do", place: .todo)
        }
    }

    @ViewBuilder
    private func lists(_ d: ItemsSheetData) -> some View {
        if d.sections.isEmpty {
            EmptyNote(text: d.empty ?? "Nothing here.")
        } else {
            let main = d.sections.filter { $0.quiet != true }
            let quiet = d.sections.filter { $0.quiet == true }
            if width >= 760, !main.isEmpty, !quiet.isEmpty {
                HStack(alignment: .top, spacing: 28) {
                    column(main, columns: 1)
                        .frame(maxWidth: .infinity, alignment: .topLeading)
                    column(quiet, columns: 1)
                        .frame(width: min(440, width * 0.4), alignment: .topLeading)
                }
            } else {
                column(main + quiet, columns: width >= 900 && quiet.isEmpty ? 2 : 1)
            }
        }
    }

    /// Sections one under another; a long list alone on a wide page runs in two columns.
    private func column(_ sections: [SheetSection], columns: Int) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            ForEach(sections) { section in
                VStack(alignment: .leading, spacing: 4) {
                    if !section.title.isEmpty {
                        Text(section.title)
                            .font(.sSubheadline.weight(.semibold))
                            .foregroundStyle(section.quiet == true ? .tertiary : .secondary)
                            .padding(.horizontal, 8)
                            .padding(.bottom, 2)
                    }
                    if columns > 1 && section.rows.count > 5 {
                        LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 18, alignment: .top), count: columns), alignment: .leading, spacing: 2) {
                            ForEach(section.rows) { row in item(row) }
                        }
                    } else {
                        ForEach(section.rows) { row in item(row) }
                    }
                }
            }
        }
    }

    /// (home-10, home-19, home-21) A row as the Dashboard's other lists draw work: its kind's symbol in its course's
    /// colour, its course and kind over its title, and on the right its time (or the day it was posted) over its points
    /// or score. An announcement's row names its course — the panel says they are announcements. An answer from an
    /// older page (no facts one by one) shows its one line under the title, as before.
    private func item(_ row: SheetRow) -> some View {
        let quiet = row.quiet == true
        let split = row.course != nil || row.kind != nil || row.when != nil
        let kind = PreviewFormat.text(row.kind) ?? (counter.key == "unread" ? "Announcement" : "Assignment")
        let context = [PreviewFormat.text(row.course), counter.key == "unread" ? nil : PreviewFormat.text(row.kind)]
            .compactMap { $0 }.joined(separator: " · ")
        // (home-20) its preview opens under it, in the panel — never a second card stacked over this one
        return PreviewLink(item: .sheet(row, counter: counter.key, engine: engine), inline: true) {
            HStack(spacing: 12) {
                IconTile(symbol: DashGlyph.kind(kind), color: Color(hex: row.color), size: 28)
                    .opacity(quiet ? 0.55 : 1)
                if split {
                    DashWorkText(context: context, title: row.title, quiet: quiet)
                    Spacer(minLength: 6)
                    DashWorkFigures(time: row.when, points: row.detail, stacked: true)
                } else {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(row.title)
                            .font(.sBody)
                            .foregroundStyle(quiet ? .secondary : .primary)
                            .lineLimit(2)
                        if let sub = row.sub, !sub.isEmpty {
                            Text(sub).font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
                        }
                    }
                    Spacer(minLength: 6)
                }
                if row.clearable == true, let k = row.key {
                    // (1.2.11) a button that says what it does; one after another, each row going as it is pressed
                    Button("Dismiss") { clear(k) }
                        .controlSize(.small)
                        .buttonStyle(.bordered)
                        .help("Dismiss from Overdue")
                        .accessibilityLabel("Dismiss \(row.title) from Overdue")
                }
            }
        }
        .contextMenu {
            if let u = row.url {
                Button("Open") { engine.openWeb(u, title: row.title) }
                Button("Open in \(engine.lmsName)") { engine.openWebScreen(u, title: row.title) }
                Button("Copy Link") { if let x = engine.absolute(u) { copyToPasteboard(x.absoluteString) } }
            }
            if row.clearable == true, let k = row.key {
                Divider()
                Button("Dismiss from Overdue") { clear(k) }
            }
        }
    }

    private func load() async {
        do {
            let d = try await engine.call("todaySheet", ["key": counter.key], as: ItemsSheetData.self)
            withAnimation(Motion.gentle) {
                data = d
                error = nil
            }
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }

    /// (1.2.11) The row goes at once (the next can be dismissed straight after); Canvas is told, and the list read again.
    /// (1.2.18) The tile's number goes down with it, and is set to what Canvas counts once it has been told.
    private func clear(_ k: String) {
        if var d = data {
            for i in d.sections.indices { d.sections[i].rows.removeAll { $0.key == k } }
            d.sections.removeAll { $0.rows.isEmpty }
            withAnimation(Motion.gentle) { data = d }
        }
        onCleared(.pressed)
        Task {
            let left = try? await engine.call("clearOverdue", ["key": k], as: TodayCounts.self)
            if let left, left.overdue != nil { onCleared(.done(left)) } else { onCleared(.failed) }
            await load()
            if left?.overdue != nil { engine.changed() }
        }
    }
}

/// Where each counter's tile stands, for its panel to grow from (1.2.8).
struct DashTileAnchors: PreferenceKey {
    static let defaultValue: [String: Anchor<CGRect>] = [:]
    static func reduce(value: inout [String: Anchor<CGRect>], nextValue: () -> [String: Anchor<CGRect>]) {
        value.merge(nextValue()) { $1 }
    }
}

/// (1.2.2) Content at its own height up to `max`, scrolling inside past it — the panel stays short however long its list.
/// (home-20) Its height is read through a preference, every time the content's changes — rows arriving, a preview
/// opening under one — so a short list never leaves the panel standing at the cap with nothing in its foot.
struct CappedScroll<Content: View>: View {
    let max: CGFloat
    @ViewBuilder var content: () -> Content
    @State private var height: CGFloat = 0

    var body: some View {
        ScrollView(.vertical) {
            content()
                .frame(maxWidth: .infinity, alignment: .topLeading)
                .background {
                    GeometryReader { p in Color.clear.preference(key: CappedHeightKey.self, value: p.size.height) }
                }
        }
        .onPreferenceChange(CappedHeightKey.self) { h in
            if abs(h - height) > 0.5 { height = h }
        }
        .scrollIndicators(height > max ? .automatic : .hidden)
        .scrollBounceBehavior(.basedOnSize)
        .frame(height: height > 0 ? min(height, max) : min(max, 120)) // (a short stand-in while it is first measured)
    }
}

private struct CappedHeightKey: PreferenceKey {
    static let defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = Swift.max(value, nextValue()) }
}

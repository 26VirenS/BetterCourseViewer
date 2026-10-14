import AppKit
import SwiftUI

// The Dashboard's six counters (1.2): each a glass tile with its symbol, its number and the line under it. Pressed, a
// tile grows where it stands into a panel floating over the page (1.2.8: a panel's width, from the tile's corner)
// listing what it counted, and folds back into its tile when closed (its ✕, Escape, a click anywhere else — which still
// does what it does — or the tile). (1.3.80) As 1.3.77 had them, again: each tile as tall as what is on it (not the
// squares of 1.3.79), the panel growing out of it in the page (not the popup over the window of 1.3.79, which faded
// in); and each tile now washed with its own colour.

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

extension View {
    /// The counter's tile and its panel drawn as one shape moving from one to the other (matchedGeometryEffect), unless
    /// Reduce Motion is on: then the panel simply fades in under its tile.
    @ViewBuilder
    func dashMorph(_ id: String, in ns: Namespace.ID, enabled: Bool) -> some View {
        if enabled {
            self.matchedGeometryEffect(id: id, in: ns)
        } else {
            self
        }
    }
}

/// (1.3.80) The screenshot suite's grow, held part-way (`-SimplMorphHold 0.5`): the tile and its panel go that share of
/// the way on an ease-out and stay there — the real morph stopped, so a picture shows the panel growing out of its tile
/// over the page. Never used without the launch argument.
struct DashMorphHold: CustomAnimation {
    let fraction: Double

    /// The hold asked for at launch (a share between 0 and 1), if any.
    static let asked: DashMorphHold? = {
        let f = UserDefaults.standard.double(forKey: "SimplMorphHold")
        return f > 0 && f < 1 ? DashMorphHold(fraction: f) : nil
    }()

    func animate<V: VectorArithmetic>(value: V, time: TimeInterval, context: inout AnimationContext<V>) -> V? {
        // (there in a quarter of a second, then held: never done)
        let t = min(time / 0.25, 1)
        return value.scaled(by: fraction * (1 - (1 - t) * (1 - t)))
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

/// A counter's tile, as 1.3.77 drew it (1.3.79: brought back from the compact strip of 1.3.78): its symbol in its
/// colour and its name, its number, and the line under it, whole — over its photo, whole too. (1.3.80) As tall as what
/// is on it (the row makes every tile in it as tall as its tallest), and washed with its own colour.
struct DashCounterTile: View {
    /// How its head is laid out: its symbol beside its name, as 1.3.77's; or, in a tile too narrow for both on one line,
    /// its symbol over its name.
    enum Style: Equatable {
        case beside
        case over
    }

    let counter: Counter
    /// (home-17) The name the row chose for it — its full one ("Announcements", "Graded this week") where every tile in
    /// the row has room for its own, else its short one ("Unread", "Graded") — and the line that goes with that name.
    let name: String
    let value: Int?
    let note: String?
    /// (home-25) The number kept from last time, shown faint until the live one is in.
    var stale = false
    var style: Style = .beside
    /// (1.3.79) How wide the row lays the tile out: its line is shown only where it fits whole at that width.
    var width: CGFloat = 0
    /// (1.3.79) What it counts, in full words, for its tooltip with its count: a line left out stays said there.
    var tip: String? = nil
    let action: () -> Void
    /// (1.3.1) Its photo's editor, from its context menu.
    @State private var editing = false
    @Environment(\.colorScheme) private var scheme

    private var alert: Bool { counter.tone == "red" && (value ?? 0) > 0 }

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 18, style: .continuous)
        Button(action: action) {
            VStack(alignment: .leading, spacing: 6) {
                head
                DashCount(value: value, alert: alert, stale: stale)
                Text(shownNote ?? " ")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
                    .opacity(stale ? 0.6 : 1)
            }
            .padding(.horizontal, Self.padding(style))
            .padding(.vertical, 14)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background {
                // (1.3.1) its photo, frosted under its words, the whole tile over (Settings ▸ Appearance, or its context
                // menu) — the colour a theme gives the tile, as 1.3.77 had it
                if let slot = PhotoSlot.card(counter.key) {
                    SlotPhotoView(slot: slot)
                        .clipShape(shape)
                }
                // (1.3.80) its own colour, a soft wash over the whole tile, over its photo and under its words; (home-1)
                // something late, a stronger red one — both drawn here, not as the glass's tint, which macOS mutes in a
                // window in the background — whatever is worn
                shape.fill(alert ? DashCounterTile.alertWash : wash)
            }
            .overlay {
                // (1.3.80) a faint edge in its colour; something late, a red one
                if alert {
                    shape.strokeBorder(DashCounterLook.alert.opacity(0.55), lineWidth: 1.5)
                } else {
                    shape.strokeBorder(DashCounterLook.color(counter.key).opacity(scheme == .dark ? 0.32 : 0.26), lineWidth: 1)
                }
            }
        }
        .buttonStyle(CardButtonStyle(radius: 18))
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
        .help(tooltip)
        .accessibilityLabel(DashCounterLook.spoken(counter.key, counter.label, value))
        .accessibilityValue(note ?? "")
        .accessibilityHint(Text("Shows what it counts"))
    }

    /// (home-1) The wash under a late count, drawn by the tile so a window in the background keeps it: 1.3.77's pink by
    /// day (the glass's red tint it had then), and by night one that reads on dark glass, where that tint never showed.
    static let alertWash = Theme.dynamic(light: NSColor.systemRed.withAlphaComponent(0.22), dark: NSColor.systemRed.withAlphaComponent(0.24))

    /// (1.3.80) The wash every tile wears: the colour its symbol wears (Due today the accent, Tomorrow orange, Next 7
    /// days green, Overdue red, Unread orange, Graded purple; a colour worn, that one), soft enough that its number and
    /// words read as plainly as on bare glass — a little more of it by night, where dark glass takes more to show it.
    private var wash: Color {
        DashCounterLook.color(counter.key).opacity(scheme == .dark ? 0.18 : 0.12)
    }

    /// Its count in words, and its line in full: "4 due today · 35 points total".
    private var tooltip: String {
        let line = tip ?? note
        return [DashCounterLook.spoken(counter.key, counter.label, value), line, stale ? "updating" : nil]
            .compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · ")
    }

    /// Its symbol beside its name (1.3.77's), or over it in a tile too narrow for both on one line.
    @ViewBuilder
    private var head: some View {
        let symbol = Image(systemName: DashCounterLook.symbol(counter.key))
            .font(.system(size: 14, weight: .semibold))
            .foregroundStyle(DashCounterLook.color(counter.key))
        let title = Text(name)
            .font(.sCallout.weight(.medium))
            .foregroundStyle(.secondary)
            .lineLimit(1)
            .minimumScaleFactor(style == .beside ? 0.85 : 0.75) // (a sidebar dragged wide: smaller, never cut short)
        switch style {
        case .beside:
            HStack(spacing: 7) {
                symbol
                title
            }
        case .over:
            VStack(alignment: .leading, spacing: 4) {
                symbol
                title
            }
        }
    }

    /// (1.3.79) The line under the number, whole in up to two lines at the tile's width; else left out (its words stay
    /// in the tooltip) — never cut in the middle of a word.
    private var shownNote: String? {
        guard let note, !note.isEmpty else { return nil }
        guard width > 0 else { return note }
        return Self.noteFits(note, room: width - Self.padding(style) * 2) ? note : nil
    }

    // MARK: - Measuring

    /// The tile's side padding: 1.3.77's 14 points; 12 where its symbol stands over its name (the narrowest tiles).
    static func padding(_ style: Style) -> CGFloat { style == .beside ? 14 : 12 }

    /// (home-17) Whether every name has room on a tile `tileWidth` across as the tile draws it — beside its symbol, or on
    /// a line of its own under it — so the row decides once for all six and no name is ever cut short.
    static func namesFit(_ labels: [String], tileWidth: CGFloat, style: Style) -> Bool {
        let font = NSFont.systemFont(ofSize: 14, weight: .medium)
        let widest = (labels.map { ($0 as NSString).size(withAttributes: [.font: font]).width }.max() ?? 0).rounded(.up)
        let beside: CGFloat = style == .beside ? 18 + 7 : 0 // (its symbol, and the room between them)
        return padding(style) * 2 + beside + widest + 2 <= tileWidth
    }

    /// (1.3.79) Whether a line fits whole in two lines `room` wide, wrapped as the tile wraps it (12 points, a word never
    /// broken): no word wider than the line, and no third line.
    static func noteFits(_ note: String, room: CGFloat) -> Bool {
        let paragraph = NSMutableParagraphStyle()
        paragraph.lineBreakStrategy = .standard // (as a SwiftUI Text wraps: no word left alone on the last line)
        let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 12), .paragraphStyle: paragraph]
        // (a little short of the whole: rounding never wraps a third line, or a word, that was measured to fit)
        let width = room - 2
        let words = note.split(whereSeparator: { $0 == " " }).map { (String($0) as NSString).size(withAttributes: attrs).width }
        guard width > 0, (words.max() ?? 0) <= width else { return false }
        let box = NSSize(width: width, height: .greatestFiniteMagnitude)
        let options: NSString.DrawingOptions = [.usesLineFragmentOrigin, .usesFontLeading]
        let line = ("Ag" as NSString).boundingRect(with: box, options: options, attributes: attrs).height
        let all = (note as NSString).boundingRect(with: box, options: options, attributes: attrs).height
        return all <= line * 2 + 0.5
    }
}

/// The panel a counter grows into: its header (the tile's symbol, name and number, larger) and what it counted —
/// what wants attention first, then, quieter, the rest of the same span under it. Each row previews its work (1.2.3; a
/// double-click opens it); an overdue one the student has let go can be cleared. (1.3.79) As 1.3.77 had it: a row is
/// its course's colour, its title and its line, and its preview the card over the window, not one opened in the panel.
struct DashCounterPanel: View {
    /// (1.2.8) How wide a panel opens (narrower only on a narrower page).
    static let width: CGFloat = 440 // (1.3.62: was 520)

    let counter: Counter
    let value: Int?
    let note: String?
    /// The panel's width: its words are laid out at it from the first frame, so they never squeeze while the tile
    /// grows (the growing shape shows more of them as it opens).
    let width: CGFloat
    /// (home-18) How tall its list may stand before it scrolls inside the panel, at most: what the window has under the
    /// panel's header, so the panel stays in the window.
    var room: CGFloat = 240
    let close: () -> Void
    /// (1.2.8) How tall what it shows stands, for the page to size the floating panel to it.
    var onHeight: (CGFloat) -> Void = { _ in }
    /// (1.2.18) A row dismissed (the page's Overdue number drops with it), then Canvas's word on it.
    enum Cleared { case pressed, done(TodayCounts), failed }
    var onCleared: (Cleared) -> Void = { _ in }
    // (1.3.78: the engine as a plain value, to act through — not drawn again each time it changes: EngineLink)
    @Environment(\.engineLink) private var link
    private var engine: Engine { link.acting }
    @State private var data: ItemsSheetData?
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
            .onTapGesture {} // (1.2.2: a click on the panel is the panel's — one anywhere else folds it back)
            .onExitCommand { close() }
            .task { await load() }
            // (1.3.79) a row's card folds back with the panel it came from: it never stays over a panel that is gone
            .onDisappear {
                if WorkPreview.shared.item?.id.hasPrefix("sheet:\(counter.key):") == true { WorkPreview.shared.close() }
            }
    }

    private var content: some View {
        VStack(alignment: .leading, spacing: 16) {
            header
            if let d = data {
                // (1.2.2) no taller than three and a half rows: the rest scrolls inside the panel (1.2.3: half of a
                // fourth row shows, so it reads as one to scroll) — (home-18) less where the window has less room
                CappedScroll(max: max(150, min(room, 240))) { lists(d) }
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
        .padding(.bottom, 20)
    }

    private var header: some View {
        HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 8) {
                    Image(systemName: DashCounterLook.symbol(counter.key))
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundStyle(DashCounterLook.color(counter.key))
                    Text(data?.title ?? DashCounterLook.fullName(counter.key, counter.label))
                        .font(.sHeadline)
                }
                HStack(alignment: .firstTextBaseline, spacing: 12) {
                    DashCount(value: value, alert: counter.tone == "red" && (value ?? 0) > 0, size: 40)
                    if let line = data?.note ?? note, !line.isEmpty {
                        Text(line)
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .lineLimit(2)
                    }
                }
            }
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
        // (1.2.11) the card pressed again — its head, where the tile was — closes it (the ✕ keeps its own press)
        .contentShape(Rectangle())
        .onTapGesture(perform: close)
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

    /// A row of the panel: a press grows its preview out of it (1.2.3, as the web's sheet previews its rows); a
    /// double-click opens it.
    private func item(_ row: SheetRow) -> some View {
        PreviewLink(item: .sheet(row, counter: counter.key, engine: engine)) {
            HStack(spacing: 12) {
                RoundedRectangle(cornerRadius: 2, style: .continuous)
                    .fill(Color(hex: row.color).opacity(row.quiet == true ? 0.5 : 1))
                    .frame(width: 4, height: 34)
                VStack(alignment: .leading, spacing: 2) {
                    Text(row.title)
                        .font(.sBody)
                        .foregroundStyle(row.quiet == true ? .secondary : .primary)
                        .lineLimit(2)
                    if let sub = row.sub, !sub.isEmpty {
                        Text(sub).font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
                    }
                }
                Spacer(minLength: 6)
                if row.clearable == true, let k = row.key {
                    // (1.2.11) a button that says what it does; one after another, each row going as it is pressed
                    Button("Dismiss") { clear(k) }
                        .controlSize(.small)
                        .buttonStyle(.bordered)
                        .help("Dismiss from Overdue")
                        .accessibilityLabel("Dismiss \(row.title) from Overdue")
                } else if row.url != nil {
                    Image(systemName: "chevron.right")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(.tertiary)
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

/// (1.3.80) Where each counter's panel is in its window, as AppKit has it (a PreviewAnchor for each counter, made as it
/// is first asked for): the page's watch for a click round the open panel reads it.
final class DashPanelAnchors {
    private var anchors: [String: PreviewAnchor] = [:]

    func callAsFunction(_ key: String) -> PreviewAnchor {
        if let a = anchors[key] { return a }
        let a = PreviewAnchor()
        anchors[key] = a
        return a
    }
}

/// (1.2.2) Content at its own height up to `max`, scrolling inside past it — the panel stays short however long its list.
/// (home-20) Its height is read through a preference, every time the content's changes — rows arriving, a row
/// dismissed — so a short list never leaves the panel standing at the cap with nothing in its foot.
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

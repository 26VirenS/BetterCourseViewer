import SwiftUI

// The Dashboard's six counters (1.2): each a glass tile with its symbol, its number and the line under it. Pressed, a
// tile grows where it stands into a panel floating over the page (1.2.8: a panel's width, from the tile's corner)
// listing what it counted, and folds back into its tile when closed (its ✕, Escape, a click round it, or the tile).

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

    /// (1.3.1) A colour worn: every counter's glyph in it; Regular, each in its own.
    static func color(_ key: String) -> Color {
        if Theme.themed { return Theme.accentIcon }
        switch key {
        case "today": return Color(hex: "#ff453a")
        case "next": return Color(hex: "#34c759")
        case "unread": return Color(hex: "#ff9500")
        case "overdue": return Color(hex: "#ff453a")
        case "tomorrow": return Color(hex: "#ff9f0a")
        case "graded": return Color(hex: "#5856d6")
        default: return Theme.accent
        }
    }

    static func fullName(_ key: String, _ label: String) -> String {
        switch key {
        case "today": return "Due today"
        case "next": return "Next 7 days"
        case "unread": return "Unread announcements"
        case "overdue": return "Overdue"
        case "tomorrow": return "Due tomorrow"
        case "graded": return "Graded this week"
        default: return label
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

/// A counter's number: rolling to a new count on the house spring, a dash while it is still being counted.
struct DashCount: View {
    let value: Int?
    var alert = false
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
        .foregroundStyle(alert ? Color.red : Color.primary)
    }
}

/// A counter's tile: its symbol and name, its number, the line under it.
struct DashCounterTile: View {
    let counter: Counter
    let value: Int?
    let note: String?
    let action: () -> Void
    /// (1.3.1) Its photo's editor, from its context menu.
    @State private var editing = false

    private var alert: Bool { counter.tone == "red" && (value ?? 0) > 0 }

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 7) {
                    Image(systemName: DashCounterLook.symbol(counter.key))
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundStyle(DashCounterLook.color(counter.key))
                    Text(counter.label)
                        .font(.sCallout.weight(.medium))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.85)
                }
                DashCount(value: value, alert: alert)
                Text(note?.isEmpty == false ? note! : " ")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.85)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 14)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background {
                // (1.3.1) its photo, frosted under its words (Settings ▸ Appearance, or its context menu)
                if let slot = PhotoSlot.card(counter.key) {
                    SlotPhotoView(slot: slot)
                        .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
                }
            }
        }
        .buttonStyle(CardButtonStyle(radius: 18, tint: alert ? .red : nil))
        .contextMenu {
            if PhotoSlot.card(counter.key) != nil {
                Button("Card Photo…") { editing = true }
            }
        }
        .sheet(isPresented: $editing) {
            if let slot = PhotoSlot.card(counter.key) {
                PhotoSlotSheet(slot: slot)
            }
        }
        .animation(Motion.snappy, value: value)
        .help("Show \(DashCounterLook.fullName(counter.key, counter.label).lowercased())")
        .accessibilityLabel("\(DashCounterLook.fullName(counter.key, counter.label)): \(value.map(String.init) ?? "counting")")
    }
}

/// The panel a counter grows into: its header (the tile's symbol, name and number, larger) and what it counted —
/// what wants attention first, then, quieter, the rest of the same span under it. Each row previews its work (1.2.3; a
/// double-click opens it); an overdue one the student has let go can be cleared.
struct DashCounterPanel: View {
    /// (1.2.8) How wide a panel opens (narrower only on a narrower page).
    static let width: CGFloat = 520

    let counter: Counter
    let value: Int?
    let note: String?
    /// The panel's width: its words are laid out at it from the first frame, so they never squeeze while the tile
    /// grows (the growing shape shows more of them as it opens).
    let width: CGFloat
    let close: () -> Void
    /// (1.2.8) How tall what it shows stands, for the page to size the floating panel to it.
    var onHeight: (CGFloat) -> Void = { _ in }
    /// (1.2.18) A row dismissed (the page's Overdue number drops with it), then Canvas's word on it.
    enum Cleared { case pressed, done(TodayCounts), failed }
    var onCleared: (Cleared) -> Void = { _ in }
    @EnvironmentObject private var engine: Engine
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
            .onTapGesture {} // (1.2.2: a click on the panel is the panel's — the page round it folds it back)
            .onExitCommand { close() }
            .task { await load() }
    }

    private var content: some View {
        VStack(alignment: .leading, spacing: 16) {
            header
            if let d = data {
                // (1.2.2) no taller than three and a half rows: the rest scrolls inside the panel
                // (1.2.3: half of a fourth row shows, so it reads as one to scroll; 1.2.15: taller while one of its rows
                // has its preview open under it)
                CappedScroll(max: previewing ? 560 : 240) { lists(d) }
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

/// (1.2.2) Content at its own height up to `max`, scrolling inside past it — the panel stays short however long its list.
struct CappedScroll<Content: View>: View {
    let max: CGFloat
    @ViewBuilder var content: () -> Content
    @State private var height: CGFloat = 0

    var body: some View {
        ScrollView(.vertical) {
            content()
                .frame(maxWidth: .infinity, alignment: .topLeading)
                .background {
                    GeometryReader { p in
                        Color.clear
                            .onAppear { height = p.size.height }
                            .onChange(of: p.size.height) { _, h in height = h }
                    }
                }
        }
        .scrollIndicators(height > max ? .automatic : .hidden)
        .scrollBounceBehavior(.basedOnSize)
        .frame(height: height > 0 ? min(height, max) : max) // (the cap while it is first measured)
    }
}

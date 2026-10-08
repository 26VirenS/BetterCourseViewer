import AppKit
import SwiftUI

// MARK: - The Inbox

/// The Inbox, as Mail shows a mailbox: the conversations at the left — a mailbox at a time (Inbox, Unread, Starred,
/// Sent, Archived, from the glass switcher at the list's head), narrowed by words typed in its filter (⌥⌘F) or to one
/// course — each with who it is with, how many messages, its star and paperclip, when, the subject, its last words and
/// its course; and the one chosen at the right, read there with its reply bar. (1.2) The list keeps a width of its own
/// (about a third of the Inbox, dragged wider or narrower at the line between them, double-clicked back), the
/// conversation has the rest; on a narrow window the list is shown alone and a conversation opens on a screen of its own.
/// Choosing one reads it (Canvas marks it read) and the arrow keys walk the list; a double-click or Return opens it on
/// a screen of its own; a swipe, a right-click or the star beside its subject stars it; New Message writes one.
struct InboxView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<InboxData>()
    @ObservedObject private var store = InboxStore.shared
    @State private var scope = "inbox"
    @State private var selection: String?
    /// Another mailbox on its way: the one shown stays, dimmed, until the new one swaps in (no blank list).
    @State private var switching = false
    /// Conversations opened here that Canvas has not yet said are read (it is told as each one opens).
    @State private var opened: Set<String> = []
    /// (1.2) The list narrowed: to the conversations with these words, and to one course (by its name).
    @State private var query = ""
    @State private var course: String?
    @FocusState private var filtering: Bool
    /// (1.2) How wide the Inbox is drawn (0 until it is known).
    @State private var width: CGFloat = 0
    /// (1.2) The list's width as last dragged (0: as the window's width suggests).
    @AppStorage("SimplInboxListWidth") private var listWidth: Double = 0
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        content
            .background(Theme.page)
            .navigationTitle(shown.label)
            .navigationSubtitle(subtitle)
            .toolbar { toolbar }
            .task(id: engine.dataVersion) { await load() }
            .onChange(of: scope) { _, _ in switchMailbox() }
            .onChange(of: selection) { _, id in chose(id) }
            .onChange(of: compact) { _, narrow in
                // (the conversation beside the list put away: on a narrow window one opens on a screen of its own)
                if narrow { selection = nil }
            }
    }

    @ViewBuilder
    private var content: some View {
        if let d = model.data {
            GeometryReader { geo in
                panes(d, width: geo.size.width)
                    .onAppear { width = geo.size.width }
                    .onChange(of: geo.size.width) { _, w in width = w }
            }
        } else {
            LoadState(error: model.error) { Task { await load() } }
        }
    }

    /// The list and the conversation side by side, the list at its own width; on a narrow window the list alone.
    @ViewBuilder
    private func panes(_ d: InboxData, width w: CGFloat) -> some View {
        if w < InboxLayout.twoPanes {
            listPane(d)
        } else {
            let lw = InboxLayout.listWidth(listWidth, in: w)
            HStack(spacing: 0) {
                listPane(d)
                    .frame(width: lw)
                InboxSplitHandle(width: lw, range: InboxLayout.listRange(in: w)) { listWidth = $0 }
                    .zIndex(1)
                reader
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
    }

    @ToolbarContentBuilder
    private var toolbar: some ToolbarContent {
        ToolbarItemGroup(placement: .primaryAction) {
            Button {
                engine.newMessage = true
            } label: {
                Label("New Message", systemImage: "square.and.pencil")
            }
            .help("New Message (⇧⌘N)")
            CanvasMenu(url: selection.map(inboxConversationAddress) ?? "/conversations", title: selectedRow?.subject ?? shown.label)
        }
    }

    // MARK: The list

    /// The mailbox's conversations under its switcher and filter, dimmed while another mailbox is on its way.
    private func listPane(_ d: InboxData) -> some View {
        let rows = visible(d)
        return VStack(spacing: 0) {
            listHeader(d)
            conversationList(d, rows)
                .opacity(switching ? 0.5 : 1)
                .animation(Motion.snappy, value: switching)
                .overlay {
                    if rows.isEmpty { emptyList(d) }
                }
        }
        .background(Theme.card)
    }

    /// The mailboxes as one glass switcher, the filter field and the course menu beside it, and the course chosen.
    private func listHeader(_ d: InboxData) -> some View {
        GlassGroup(spacing: 6) {
            VStack(alignment: .leading, spacing: 10) {
                InboxMailboxSwitcher(scope: $scope)
                HStack(spacing: 8) {
                    InboxFilterField(text: $query, focus: $filtering)
                    courseMenu(d)
                }
                if let course {
                    courseChip(course)
                        .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.opacity.combined(with: .move(edge: .top)))
                }
            }
        }
        .padding(.horizontal, 14)
        .padding(.top, 12)
        .padding(.bottom, 8)
        .animation(Motion.snappy, value: course)
    }

    private func conversationList(_ d: InboxData, _ rows: [ConvRow]) -> some View {
        ScrollViewReader { proxy in
            List(selection: $selection) {
                ForEach(rows) { c in row(c) }
            }
            .listStyle(.inset)
            .scrollContentBackground(.hidden)
            .contextMenu(forSelectionType: String.self) { ids in
                rowMenu(ids)
            } primaryAction: { ids in
                if let id = ids.first { openAlone(id) }
            }
            .onChange(of: d.scope) { _, _ in
                // (another mailbox: from its top, its newest first)
                if let first = rows.first?.id { proxy.scrollTo(first, anchor: .top) }
            }
        }
    }

    private func row(_ c: ConvRow) -> some View {
        InboxRowView(row: c, unread: isUnread(c), draft: store.drafts[c.id] != nil, courseColor: c.context.flatMap { courseColor($0) })
            .tag(c.id)
            .listRowSeparator(.visible)
            .swipeActions(edge: .leading) { starButton(c) }
            .swipeActions(edge: .trailing) {
                if isUnread(c) { readButton(c) }
            }
    }

    /// The list with nothing in it: the mailbox empty, nothing matching the words typed, or nothing from the course.
    @ViewBuilder
    private func emptyList(_ d: InboxData) -> some View {
        if d.rows.isEmpty {
            ContentUnavailableView(emptyTitle(d), systemImage: shown.symbol)
        } else if !trimmedQuery.isEmpty {
            ContentUnavailableView.search(text: trimmedQuery)
        } else {
            ContentUnavailableView {
                Label("Nothing from This Course", systemImage: "line.3.horizontal.decrease.circle")
            } description: {
                Text("\(shown.label) holds no conversations about \(course ?? "this course").")
            } actions: {
                Button("Show Every Course") { course = nil }
                    .glassButton()
            }
        }
    }

    /// The courses the mailbox's conversations are about, to narrow the list to one (checked as a menu's items are).
    private func courseMenu(_ d: InboxData) -> some View {
        let names = courseNames(d)
        let on = course != nil
        return Menu {
            Picker("Course", selection: $course) {
                Text("Every Course").tag(String?.none)
                ForEach(names, id: \.self) { name in
                    Text(name).tag(Optional(name))
                }
            }
            .pickerStyle(.inline)
            .labelsHidden()
        } label: {
            Image(systemName: on ? "line.3.horizontal.decrease.circle.fill" : "line.3.horizontal.decrease.circle")
                .font(.sTitle3.weight(.regular))
                .foregroundStyle(on ? Color.accentColor : Color.secondary)
                .frame(width: 38, height: 38)
                .contentShape(Circle())
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .fixedSize()
        .glass(Circle(), interactive: true)
        .disabled(names.isEmpty && !on)
        .help(on ? "Showing \(course ?? "")" : "Show One Course")
        .accessibilityLabel("Course")
    }

    /// The course the list is narrowed to, in its colour; a click shows every course again.
    private func courseChip(_ name: String) -> some View {
        let tone = courseColor(name)
        return Button {
            course = nil
        } label: {
            HStack(spacing: 7) {
                Circle()
                    .fill(tone ?? Color.accentColor)
                    .frame(width: 8, height: 8)
                Text(name)
                    .font(.sCallout.weight(.medium))
                    .lineLimit(1)
                Image(systemName: "xmark")
                    .font(.sCaption.weight(.bold))
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .glassCapsule(tint: (tone ?? Color.accentColor).opacity(0.3), interactive: true)
        .help("Show Every Course")
        .accessibilityLabel("\(name), show every course")
    }

    /// A conversation's context menu: open it on a screen of its own, mark it read, star it, show its course alone, open
    /// it in Canvas, copy its link. Under the list's foot, New Message.
    @ViewBuilder
    private func rowMenu(_ ids: Set<String>) -> some View {
        if let id = ids.first, let c = model.data?.rows.first(where: { $0.id == id }) {
            Button("Open") { openAlone(c.id) }
            if isUnread(c) {
                Button("Mark as Read") { markRead(c.id) }
            }
            Button(c.starred == true ? "Unstar" : "Star") { star(c.id, c.starred != true) }
            if let ctx = c.context, !ctx.isEmpty, course != ctx {
                Button("Show Only \(ctx)") { course = ctx }
            }
            Divider()
            Button("Open in \(engine.lmsName)") { engine.openWebScreen(inboxConversationAddress(c.id), title: c.subject) }
            Button("Copy Link") {
                if let u = engine.absolute(inboxConversationAddress(c.id)) { copyToPasteboard(u.absoluteString) }
            }
        } else {
            Button("New Message") { engine.newMessage = true }
        }
    }

    /// A swipe across a conversation to the right (two fingers on the trackpad): its star.
    private func starButton(_ c: ConvRow) -> some View {
        let on = c.starred == true
        return Button {
            star(c.id, !on)
        } label: {
            Label(on ? "Unstar" : "Star", systemImage: on ? "star.slash" : "star.fill")
        }
        .tint(.yellow)
    }

    /// A swipe to the left across an unread conversation: read, without opening it.
    private func readButton(_ c: ConvRow) -> some View {
        Button {
            markRead(c.id)
        } label: {
            Label("Mark as Read", systemImage: "envelope.open")
        }
        .tint(.blue)
    }

    // MARK: The reading pane

    /// The conversation chosen, or a word on choosing one.
    @ViewBuilder
    private var reader: some View {
        if let id = selection {
            let row = model.data?.rows.first(where: { $0.id == id })
            ConversationView(id: id, link: InboxLink(
                starred: row.map { $0.starred == true },
                star: { on in star(id, on) }
            ))
            .id(id)
        } else {
            ContentUnavailableView {
                Label("No Conversation Selected", systemImage: "envelope.open")
            } description: {
                Text("Choose a conversation to read it, or write a new message to a teacher, a classmate or a course.")
            } actions: {
                Button {
                    engine.newMessage = true
                } label: {
                    Label("New Message", systemImage: "square.and.pencil")
                }
                .glassButton(prominent: true)
                .controlSize(.large)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Theme.page)
        }
    }

    // MARK: What it does

    private var shown: InboxMailbox { InboxMailbox.all.first { $0.id == scope } ?? InboxMailbox.all[0] }

    /// Too narrow for the list and a conversation side by side.
    private var compact: Bool { width > 0 && width < InboxLayout.twoPanes }

    private var selectedRow: ConvRow? {
        guard let id = selection else { return nil }
        return model.data?.rows.first(where: { $0.id == id })
    }

    /// Under the title: how many are unread, else how many there are.
    private var subtitle: String {
        guard let rows = model.data?.rows, !rows.isEmpty else { return "" }
        let n = rows.filter { isUnread($0) }.count
        if n > 0 { return "\(n) unread" }
        return "\(rows.count) \(rows.count == 1 ? "conversation" : "conversations")"
    }

    private var trimmedQuery: String { query.trimmingCharacters(in: .whitespacesAndNewlines) }

    /// The mailbox's conversations, narrowed to the course chosen and to those with the words typed (who, the subject,
    /// the last words, the course).
    private func visible(_ d: InboxData) -> [ConvRow] {
        let q = trimmedQuery
        guard course != nil || !q.isEmpty else { return d.rows }
        return d.rows.filter { r in
            if let course, r.context != course { return false }
            if q.isEmpty { return true }
            return [r.who, r.subject, r.preview ?? "", r.context ?? ""].contains { $0.localizedCaseInsensitiveContains(q) }
        }
    }

    /// The courses the mailbox's conversations are about, by name (and the one chosen, though none here is about it).
    private func courseNames(_ d: InboxData) -> [String] {
        var names = Set(d.rows.compactMap { $0.context }.filter { !$0.isEmpty })
        if let course { names.insert(course) }
        return names.sorted { $0.localizedStandardCompare($1) == .orderedAscending }
    }

    /// A course's colour, by the name Canvas gives the conversation (one of the sidebar's courses).
    private func courseColor(_ name: String) -> Color? {
        guard !name.isEmpty, let c = engine.courses.first(where: { $0.name == name || $0.original == name || $0.nickname == name || $0.code == name }) else { return nil }
        return c.color.map { Color(hex: $0) }
    }

    private func isUnread(_ c: ConvRow) -> Bool { c.unread == true && !opened.contains(c.id) }

    private func emptyTitle(_ d: InboxData) -> String {
        let text = d.empty ?? "No messages."
        return text.hasSuffix(".") ? String(text.dropLast()) : text
    }

    /// A conversation chosen in the list: read beside it, or — on a narrow window — on a screen of its own.
    private func chose(_ id: String?) {
        guard let id else { return }
        if compact {
            selection = nil
            openAlone(id)
        } else {
            markOpened(id)
        }
    }

    /// A conversation chosen is read: its dot goes at once (Canvas is told as it opens).
    private func markOpened(_ id: String?) {
        guard let id, let c = model.data?.rows.first(where: { $0.id == id }), isUnread(c) else { return }
        withAnimation(Motion.snappy) { _ = opened.insert(id) }
    }

    /// A conversation on a screen of its own (a double-click, Return, Open; any choice on a narrow window).
    private func openAlone(_ id: String) {
        markOpened(id)
        engine.push(.conversation(id: id))
    }

    /// Read without reading it: Canvas marks a conversation read as the page opens it, so the page opens it, out of sight.
    private func markRead(_ id: String) {
        withAnimation(Motion.snappy) { _ = opened.insert(id) }
        Task {
            guard (try? await engine.call("conversation", ["id": id], as: ConversationData.self)) != nil else {
                withAnimation(Motion.snappy) { _ = opened.remove(id) }
                return
            }
            try? await Task.sleep(nanoseconds: 2_000_000_000) // (the page tells Canvas just after it answers)
            await engine.refreshSnapshot()
        }
    }

    /// Starred at once (the star shows before Canvas answers), and back if Canvas says no.
    private func star(_ id: String, _ on: Bool) {
        setStar(id, on)
        Task {
            if await engine.act("star", ["id": id, "on": on]) {
                await load(animated: true)
            } else {
                setStar(id, !on)
            }
        }
    }

    private func setStar(_ id: String, _ on: Bool) {
        guard let i = model.data?.rows.firstIndex(where: { $0.id == id }) else { return }
        withAnimation(Motion.snappy) { model.data?.rows[i].starred = on }
    }

    /// Another mailbox: as last read at once if it has been (read afresh meanwhile), else the list shown stays, dimmed,
    /// until the new one is in; a conversation not in it is put away.
    private func switchMailbox() {
        let wanted = scope
        if let kept = store.lists[wanted] {
            withAnimation(Motion.gentle) { model.data = kept }
        } else {
            switching = true
        }
        Task {
            await load(animated: true)
            guard wanted == scope else { return } // (another mailbox asked for since: its own load ends the switch)
            switching = false
            if let id = selection, model.data?.rows.contains(where: { $0.id == id }) != true { selection = nil }
        }
    }

    /// The mailbox showing, read (as last read at once, if it has been). Only the mailbox asked for last is shown (two
    /// picked in a row), and a conversation opened here stays read until Canvas says so too.
    private func load(animated: Bool = false) async {
        store.bind(engine)
        let wanted = scope
        if model.data == nil, let kept = store.lists[wanted] {
            model.data = kept
        }
        do {
            let d = try await engine.call("inbox", ["scope": wanted], as: InboxData.self)
            guard wanted == scope else { return }
            if animated {
                withAnimation(Motion.gentle) {
                    model.data = d
                    model.error = nil
                }
            } else {
                var t = Transaction()
                t.disablesAnimations = true
                withTransaction(t) {
                    model.data = d
                    model.error = nil
                }
            }
            store.lists[wanted] = d
            opened.formIntersection(d.rows.filter { $0.unread == true }.map(\.id))
            // (the screenshot suite: -SimplOpen conversation:<id>, or conversation:first, opens one beside the list)
            if let pick = LaunchOpen.take("conversation:") {
                selection = pick == "first" ? d.rows.first?.id : pick
            }
        } catch {
            if model.data == nil { model.error = error.localizedDescription }
        }
    }
}

/// A mailbox of the Inbox, as Canvas keeps them.
private struct InboxMailbox: Identifiable {
    let id: String
    let label: String
    let symbol: String

    static let all = [
        InboxMailbox(id: "inbox", label: "Inbox", symbol: "tray"),
        InboxMailbox(id: "unread", label: "Unread", symbol: "envelope.badge"),
        InboxMailbox(id: "starred", label: "Starred", symbol: "star"),
        InboxMailbox(id: "sent", label: "Sent", symbol: "paperplane"),
        InboxMailbox(id: "archived", label: "Archived", symbol: "archivebox"),
    ]
}

/// (1.2) How the Inbox shares its width: the list about a third of it (320–420 pt as the window suggests, 300–560 as
/// dragged), the conversation at least 400 pt; below 720 pt, the list alone.
private enum InboxLayout {
    static let twoPanes: CGFloat = 720
    static let leastThread: CGFloat = 400

    static func listRange(in w: CGFloat) -> ClosedRange<CGFloat> {
        let upper = max(300, min(560, w - leastThread))
        return 300...upper
    }

    static func listWidth(_ dragged: Double, in w: CGFloat) -> CGFloat {
        let range = listRange(in: w)
        let wanted = dragged > 0 ? CGFloat(dragged) : min(max(w * 0.36, 320), 420)
        return min(max(wanted, range.lowerBound), range.upperBound)
    }
}

/// (1.2) The line between the list and the conversation: dragged, the list is wider or narrower (and stays so);
/// double-clicked, it goes back to the width the window suggests. The pointer shows it can be dragged.
private struct InboxSplitHandle: View {
    let width: CGFloat
    let range: ClosedRange<CGFloat>
    /// The new width (0: as the window suggests).
    let resize: (Double) -> Void
    @State private var from: CGFloat?
    @State private var hovering = false

    var body: some View {
        Rectangle()
            .fill(Color(nsColor: .separatorColor))
            .frame(width: 1)
            .frame(maxHeight: .infinity)
            .overlay { grip }
            .accessibilityElement()
            .accessibilityLabel("Conversation list width")
            .accessibilityValue("\(Int(width)) points")
            .accessibilityAdjustableAction { direction in
                switch direction {
                case .increment: resize(Double(clamp(width + 24)))
                case .decrement: resize(Double(clamp(width - 24)))
                @unknown default: break
                }
            }
    }

    private var grip: some View {
        Color.clear
            .frame(width: 10)
            .contentShape(Rectangle())
            .onHover { inside in pointer(inside) }
            .onDisappear { pointer(false) }
            .onTapGesture(count: 2) { resize(0) }
            .gesture(
                DragGesture(minimumDistance: 1, coordinateSpace: .global)
                    .onChanged { value in
                        let start = from ?? width
                        if from == nil { from = width }
                        resize(Double(clamp(start + value.translation.width)))
                    }
                    .onEnded { _ in from = nil }
            )
    }

    private func clamp(_ w: CGFloat) -> CGFloat { min(max(w, range.lowerBound), range.upperBound) }

    private func pointer(_ inside: Bool) {
        guard inside != hovering else { return }
        hovering = inside
        if inside { NSCursor.resizeLeftRight.push() } else { NSCursor.pop() }
    }
}

/// (1.2) The mailboxes as one glass switcher: each its symbol, the one showing with its name too on a wash of the
/// accent that slides from one to the next.
private struct InboxMailboxSwitcher: View {
    @Binding var scope: String
    @Namespace private var pill
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        HStack(spacing: 2) {
            ForEach(InboxMailbox.all) { m in segment(m) }
        }
        .padding(3)
        .glassCapsule()
    }

    private func segment(_ m: InboxMailbox) -> some View {
        let on = m.id == scope
        return Button {
            withAnimation(reduceMotion ? Animation.easeInOut(duration: 0.15) : Motion.snappy) { scope = m.id }
        } label: {
            HStack(spacing: 6) {
                Image(systemName: m.symbol)
                    .symbolVariant(on ? .fill : .none)
                if on {
                    Text(m.label)
                        .lineLimit(1)
                        .fixedSize()
                }
            }
            .font(.sCallout.weight(.semibold))
            .foregroundStyle(on ? Color.accentColor : Color.secondary)
            .padding(.horizontal, on ? 12 : 6)
            .frame(maxWidth: on ? nil : CGFloat.infinity, minHeight: 34)
            .background {
                if on {
                    Capsule()
                        .fill(Color.accentColor.opacity(0.15))
                        .matchedGeometryEffect(id: "mailbox", in: pill)
                }
            }
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .help(m.label)
        .accessibilityLabel(m.label)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// (1.2) The list's filter: the conversations with the words typed (⌥⌘F to type there, Escape to clear it).
private struct InboxFilterField: View {
    @Binding var text: String
    var focus: FocusState<Bool>.Binding

    var body: some View {
        HStack(spacing: 7) {
            Button {
                focus.wrappedValue = true
            } label: {
                Image(systemName: "magnifyingglass")
                    .font(.sCallout.weight(.medium))
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .keyboardShortcut("f", modifiers: [.command, .option])
            .help("Filter Conversations (⌥⌘F)")
            .accessibilityLabel("Filter Conversations")
            TextField("Filter", text: $text, prompt: Text("Filter conversations"))
                .textFieldStyle(.plain)
                .font(.sBody)
                .labelsHidden()
                .focused(focus)
                .onExitCommand {
                    text = ""
                    focus.wrappedValue = false
                }
            if !text.isEmpty {
                Button {
                    text = ""
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(.plain)
                .help("Clear")
                .accessibilityLabel("Clear Filter")
            }
        }
        .padding(.horizontal, 12)
        .frame(height: 38)
        .glassCapsule()
    }
}

/// A conversation in the Inbox's list, as Mail draws a message: the blue dot while it is unread, a picture of who it is
/// with, their name and how many messages it holds, a pencil for a reply begun, its star and paperclip, when; the
/// subject, its last words, its course in its colour. Chosen, on the accent, its marks turn white as Mail's do.
private struct InboxRowView: View {
    let row: ConvRow
    let unread: Bool
    let draft: Bool
    var courseColor: Color?
    @Environment(\.backgroundProminence) private var prominence
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let chosen = prominence == .increased
        HStack(alignment: .top, spacing: 10) {
            Circle()
                .fill(unread ? (chosen ? Color.white : Color.accentColor) : Color.clear)
                .frame(width: 9, height: 9)
                .padding(.top, 6)
                .accessibilityLabel("Unread")
                .accessibilityHidden(!unread)
            InboxAvatar(name: row.who, size: 38)
            VStack(alignment: .leading, spacing: 3) {
                topLine(chosen)
                Text(row.subject)
                    .font(.sBody.weight(unread ? .semibold : .regular))
                    .lineLimit(1)
                if let p = row.preview, !p.isEmpty {
                    Text(p.replacingOccurrences(of: "\n", with: " "))
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
                if let c = row.context, !c.isEmpty {
                    courseLine(c, chosen: chosen)
                }
            }
        }
        .padding(.vertical, 6)
        .animation(Motion.snappy, value: row.starred)
        .animation(Motion.snappy, value: unread)
        .accessibilityElement(children: .combine)
    }

    private func topLine(_ chosen: Bool) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(row.who)
                .font(.sBody.weight(.semibold))
                .lineLimit(1)
            if let n = row.count, n > 1 {
                Text("\(n)")
                    .font(.sCaption.weight(.semibold).monospacedDigit())
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 1)
                    .background(.quaternary, in: Capsule())
                    .accessibilityLabel("\(n) messages")
            }
            Spacer(minLength: 6)
            marks(chosen)
            if let w = row.when, !w.isEmpty {
                Text(w)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .fixedSize()
            }
        }
    }

    @ViewBuilder
    private func marks(_ chosen: Bool) -> some View {
        if draft {
            Image(systemName: "pencil")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .help("A reply begun here")
                .accessibilityLabel("Unsent reply")
        }
        if row.starred == true {
            Image(systemName: "star.fill")
                .font(.sCallout)
                .foregroundStyle(chosen ? Color.white : Color.yellow)
                .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.scale(scale: 0.6).combined(with: .opacity))
                .accessibilityLabel("Starred")
        }
        if row.attachment == true {
            Image(systemName: "paperclip")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .accessibilityLabel("Has an attachment")
        }
    }

    private func courseLine(_ name: String, chosen: Bool) -> some View {
        HStack(spacing: 6) {
            Circle()
                .fill(chosen ? Color.white.opacity(0.85) : (courseColor ?? Color.secondary.opacity(0.5)))
                .frame(width: 7, height: 7)
            Text(name)
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .padding(.top, 1)
    }
}

// MARK: - Groups

/// Groups: the student's groups — the current ones, then the past ones — each a glass card washed in its course's
/// colour, with its course and how many are in it, that opens the group's own home. On a wide window, more to a row.
struct GroupsView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<GroupsData>()

    var body: some View {
        Group {
            if let d = model.data {
                if d.current.isEmpty && d.past.isEmpty {
                    ContentUnavailableView("No Groups", systemImage: "person.3", description: Text(d.empty ?? "You are not in any groups."))
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(Theme.page)
                } else {
                    Page {
                        ScreenHeading(title: "Groups", sub: summary(d))
                        if !d.current.isEmpty { section("Current", d.current) }
                        if !d.past.isEmpty { section("Past", d.past) }
                    }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("Groups")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                CanvasMenu(url: "/groups", title: "Groups")
            }
        }
        .task(id: engine.dataVersion) { await load() }
    }

    private func section(_ title: String, _ rows: [GroupRow]) -> some View {
        PageSection(title: title, trailing: "\(rows.count)") {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 300, maximum: 460), spacing: 16, alignment: .top)], alignment: .leading, spacing: 16) {
                ForEach(rows) { g in card(g) }
            }
        }
    }

    private func card(_ g: GroupRow) -> some View {
        Button {
            engine.go(.home("groups/\(g.id)"))
        } label: {
            GroupsCard(group: g)
        }
        .buttonStyle(CardButtonStyle(radius: 18, tint: Color(hex: g.color).opacity(0.5)))
        .help("Open \(g.name)")
        .contextMenu {
            Button("Open") { engine.go(.home("groups/\(g.id)")) }
            Divider()
            Button("Open in \(engine.lmsName)") { engine.openWebScreen("/groups/\(g.id)", title: g.name) }
            Button("Copy Link") {
                if let u = engine.absolute("/groups/\(g.id)") { copyToPasteboard(u.absoluteString) }
            }
        }
    }

    private func summary(_ d: GroupsData) -> String {
        var parts: [String] = []
        if !d.current.isEmpty { parts.append("\(d.current.count) current") }
        if !d.past.isEmpty { parts.append("\(d.past.count) past") }
        return parts.joined(separator: " · ")
    }

    private func load() async { await model.load(engine, "groups") }
}

/// A group as a card: its symbol on its course's colour, its name, its course (with the colour's dot) and how many are
/// in it.
private struct GroupsCard: View {
    let group: GroupRow

    var body: some View {
        HStack(spacing: 14) {
            IconTile(symbol: "person.3.fill", color: Color(hex: group.color), size: 46)
            VStack(alignment: .leading, spacing: 5) {
                Text(group.name)
                    .font(.sTitle3)
                    .lineLimit(2)
                    .multilineTextAlignment(.leading)
                if let course {
                    HStack(spacing: 6) {
                        Circle()
                            .fill(Color(hex: group.color))
                            .frame(width: 8, height: 8)
                        Text(course)
                            .font(.sCallout.weight(.medium))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
                if let members {
                    Label(members, systemImage: "person.2")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 8)
            Image(systemName: "chevron.right")
                .font(.sCallout.weight(.semibold))
                .foregroundStyle(.tertiary)
        }
        .padding(18)
        .frame(maxWidth: .infinity, minHeight: 100, alignment: .leading)
        .accessibilityElement(children: .combine)
    }

    /// Its line in parts ("BIO 101 · 4 members").
    private var parts: [String] { (group.sub ?? "").components(separatedBy: " · ").filter { !$0.isEmpty } }
    private var members: String? { parts.first { $0.contains("member") } }
    private var course: String? { parts.first { !$0.contains("member") } }
}

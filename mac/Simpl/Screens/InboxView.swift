import SwiftUI

// MARK: - The Inbox

/// The Inbox, as Mail shows a mailbox: the conversations at the left — a mailbox at a time (Inbox, Unread, Starred,
/// Sent, Archived), each with who it is with, how many messages, its star and paperclip, when, the subject, its last
/// words and its course — and the one chosen at the right, read there with its reply box. Choosing one reads it (Canvas
/// marks it read) and the arrow keys walk the list; a double-click or Return opens it on a screen of its own; a swipe, a
/// right-click or the star beside its subject stars it; New Message writes one.
struct InboxView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<InboxData>()
    @State private var scope = "inbox"
    @State private var selection: String?
    /// Another mailbox on its way: the one shown stays, dimmed, until the new one swaps in (no blank list).
    @State private var switching = false
    /// Conversations opened here that Canvas has not yet said are read (it is told as each one opens).
    @State private var opened: Set<String> = []
    /// A reply begun in a conversation, kept while another one is read.
    @State private var drafts: [String: String] = [:]

    var body: some View {
        Group {
            if let d = model.data {
                // (the split is held to the width the window gives it: left to itself it asks for its panes' ideal
                // widths, and on a smaller window that pushed the sidebar off the window's edge)
                GeometryReader { geo in
                    HSplitView {
                        listPane(d)
                            .frame(minWidth: 260, idealWidth: 340, maxWidth: 480)
                        reader
                            .frame(minWidth: 300, maxWidth: .infinity, maxHeight: .infinity)
                    }
                    .frame(width: geo.size.width, height: geo.size.height)
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .background(Theme.page)
        .navigationTitle(shown.label)
        .navigationSubtitle(unreadLine)
        .toolbar {
            ToolbarItemGroup(placement: .primaryAction) {
                Button {
                    engine.newMessage = true
                } label: {
                    Label("New Message", systemImage: "square.and.pencil")
                }
                .help("New Message (⇧⌘N)")
                CanvasMenu(url: selection.map(conversationAddress) ?? "/conversations", title: selectedRow?.subject ?? shown.label)
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .onChange(of: scope) { _, _ in switchMailbox() }
        .onChange(of: selection) { _, id in markOpened(id) }
    }

    // MARK: The list

    /// The mailbox's conversations under its picker, dimmed while another mailbox is on its way.
    private func listPane(_ d: InboxData) -> some View {
        VStack(spacing: 0) {
            mailboxPicker
                .padding(.horizontal, 12)
                .padding(.vertical, 9)
            Divider()
            ScrollViewReader { proxy in
                List(selection: $selection) {
                    ForEach(d.rows) { c in
                        InboxRowView(row: c, unread: isUnread(c), draft: drafts[c.id] != nil)
                            .tag(c.id)
                            .listRowSeparator(.visible)
                            .swipeActions(edge: .leading) { starButton(c) }
                    }
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
                    if let first = d.rows.first?.id { proxy.scrollTo(first, anchor: .top) }
                }
            }
            .opacity(switching ? 0.5 : 1)
            .animation(Motion.snappy, value: switching)
            .overlay {
                if d.rows.isEmpty {
                    ContentUnavailableView(emptyTitle(d), systemImage: shown.symbol)
                }
            }
        }
        .background(Theme.card)
    }

    /// The mailboxes, as a pop-up menu at the head of the list (it fits however narrow the list is drawn), with how
    /// many the one shown holds.
    private var mailboxPicker: some View {
        HStack(spacing: 8) {
            Picker("Mailbox", selection: $scope) {
                ForEach(InboxMailbox.all) { m in Label(m.label, systemImage: m.symbol).tag(m.id) }
            }
            .pickerStyle(.menu)
            .labelsHidden()
            .fixedSize()
            Spacer(minLength: 0)
            if let n = model.data?.rows.count, n > 0 {
                Text("\(n) \(n == 1 ? "conversation" : "conversations")")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .contentTransition(.numericText(value: Double(n)))
            }
        }
        .help("Mailbox")
    }

    /// A conversation's context menu: open it on a screen of its own, mark it read, star it, open it in Canvas, copy
    /// its link. Under the list's foot, New Message.
    @ViewBuilder
    private func rowMenu(_ ids: Set<String>) -> some View {
        if let id = ids.first, let c = model.data?.rows.first(where: { $0.id == id }) {
            Button("Open") { openAlone(c.id) }
            if isUnread(c) {
                Button("Mark as Read") { markRead(c.id) }
            }
            Button(c.starred == true ? "Unstar" : "Star") { star(c.id, c.starred != true) }
            Divider()
            Button("Open in Canvas") { engine.openWebScreen(conversationAddress(c.id), title: c.subject) }
            Button("Copy Link") {
                if let u = engine.absolute(conversationAddress(c.id)) { copyToPasteboard(u.absoluteString) }
            }
        } else {
            Button("New Message") { engine.newMessage = true }
        }
    }

    /// A swipe across a conversation (two fingers on the trackpad): its star.
    private func starButton(_ c: ConvRow) -> some View {
        let on = c.starred == true
        return Button {
            star(c.id, !on)
        } label: {
            Label(on ? "Unstar" : "Star", systemImage: on ? "star.slash" : "star.fill")
        }
        .tint(.yellow)
    }

    // MARK: The reading pane

    /// The conversation chosen, or a word on choosing one.
    @ViewBuilder
    private var reader: some View {
        if let id = selection {
            let row = model.data?.rows.first(where: { $0.id == id })
            ConversationView(id: id, link: InboxLink(
                starred: row.map { $0.starred == true },
                star: { on in star(id, on) },
                draft: draft(for: id)
            ))
            .id(id)
        } else {
            ContentUnavailableView {
                Label("No Conversation Selected", systemImage: "envelope.open")
            } description: {
                Text("Choose a conversation to read it, or write a new message to a teacher, a classmate or a course.")
            } actions: {
                Button("New Message") { engine.newMessage = true }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Theme.page)
        }
    }

    // MARK: What it does

    private var shown: InboxMailbox { InboxMailbox.all.first { $0.id == scope } ?? InboxMailbox.all[0] }

    private var selectedRow: ConvRow? {
        guard let id = selection else { return nil }
        return model.data?.rows.first(where: { $0.id == id })
    }

    private var unreadLine: String {
        let n = model.data?.rows.filter { isUnread($0) }.count ?? 0
        return n > 0 ? "\(n) unread" : ""
    }

    private func isUnread(_ c: ConvRow) -> Bool { c.unread == true && !opened.contains(c.id) }

    private func emptyTitle(_ d: InboxData) -> String {
        let text = d.empty ?? "No messages."
        return text.hasSuffix(".") ? String(text.dropLast()) : text
    }

    /// The reply begun in a conversation (gone once it is empty again).
    private func draft(for id: String) -> Binding<String> {
        Binding(get: { drafts[id] ?? "" }, set: { drafts[id] = $0.isEmpty ? nil : $0 })
    }

    /// A conversation chosen is read: its dot goes at once (Canvas is told as it opens).
    private func markOpened(_ id: String?) {
        guard let id, let c = model.data?.rows.first(where: { $0.id == id }), isUnread(c) else { return }
        withAnimation(Motion.snappy) { _ = opened.insert(id) }
    }

    /// A conversation on a screen of its own (a double-click, Return, Open).
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

    /// Another mailbox: the list shown stays, dimmed, until the new one is in; a conversation not in it is put away.
    private func switchMailbox() {
        switching = true
        let wanted = scope
        Task {
            await load(animated: true)
            guard wanted == scope else { return } // (another mailbox asked for since: its own load ends the switch)
            switching = false
            if let id = selection, model.data?.rows.contains(where: { $0.id == id }) != true { selection = nil }
        }
    }

    /// The mailbox showing, read. Only the mailbox asked for last is shown (two picked in a row), and a conversation
    /// opened here stays read until Canvas says so too.
    private func load(animated: Bool = false) async {
        let wanted = scope
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
            opened.formIntersection(d.rows.filter { $0.unread == true }.map(\.id))
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

/// What the Inbox hands the conversation it shows beside its list: the list's word on its star (nil once the mailbox
/// showing no longer holds it — Unread, after it is read), the star itself, and the reply begun in it.
private struct InboxLink {
    var starred: Bool?
    var star: (Bool) -> Void
    var draft: Binding<String>
}

/// A conversation in the Inbox's list, as Mail draws a message: the blue dot while it is unread, who it is with and how
/// many messages it holds, a pencil for a reply begun, its star and paperclip, when; the subject, its last words, its
/// course. Chosen, on the accent, its marks turn white as Mail's do.
private struct InboxRowView: View {
    let row: ConvRow
    let unread: Bool
    let draft: Bool
    @Environment(\.backgroundProminence) private var prominence
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let chosen = prominence == .increased
        HStack(alignment: .top, spacing: 8) {
            Circle()
                .fill(unread ? (chosen ? Color.white : Color.accentColor) : Color.clear)
                .frame(width: 8, height: 8)
                .padding(.top, 5)
                .accessibilityLabel("Unread")
                .accessibilityHidden(!unread)
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    Text(row.who)
                        .font(unread ? .body.weight(.semibold) : .body)
                        .lineLimit(1)
                    if let n = row.count, n > 1 {
                        Text("\(n)")
                            .font(.caption2.weight(.semibold).monospacedDigit())
                            .foregroundStyle(.secondary)
                            .padding(.horizontal, 5)
                            .padding(.vertical, 1)
                            .background(.quaternary, in: Capsule())
                            .accessibilityLabel("\(n) messages")
                    }
                    Spacer(minLength: 6)
                    if draft {
                        Image(systemName: "pencil")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .help("A reply begun here")
                            .accessibilityLabel("Unsent reply")
                    }
                    if row.starred == true {
                        Image(systemName: "star.fill")
                            .font(.caption)
                            .foregroundStyle(chosen ? Color.white : Color.yellow)
                            .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.scale(scale: 0.6).combined(with: .opacity))
                            .accessibilityLabel("Starred")
                    }
                    if row.attachment == true {
                        Image(systemName: "paperclip")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .accessibilityLabel("Has an attachment")
                    }
                    if let w = row.when, !w.isEmpty {
                        Text(w)
                            .font(.callout)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
                Text(row.subject)
                    .font(.callout.weight(unread ? .semibold : .regular))
                    .lineLimit(1)
                if let p = row.preview, !p.isEmpty {
                    Text(p)
                        .font(.callout)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
                if let c = row.context, !c.isEmpty {
                    Text(c)
                        .font(.subheadline)
                        .foregroundStyle(.tertiary)
                        .lineLimit(1)
                }
            }
        }
        .padding(.vertical, 5)
        .animation(Motion.snappy, value: row.starred)
        .animation(Motion.snappy, value: unread)
        .accessibilityElement(children: .combine)
    }
}

// MARK: - A conversation

/// One conversation: its subject, who is in it and its course, then its messages in order — the other people's at the
/// left with their pictures and names, yours at the right — each with its files (opened in Quick Look) and when it was
/// sent; its star; and the reply box at the foot (⌘Return sends). Pushed on a place it is a screen of its own, with its
/// own title and toolbar; beside the Inbox's list it is the reading pane.
struct ConversationView: View {
    let id: String
    /// Beside the Inbox's list (nil on a screen of its own).
    fileprivate var link: InboxLink?
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<ConversationData>()
    @State private var ownDraft = ""
    @State private var sending = false
    @State private var error: String?
    /// The sidebar's unread count asked for again, once, after the first answer (Canvas marks the conversation read then).
    @State private var counted = false
    @FocusState private var typing: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(id: String) {
        self.id = id
    }

    fileprivate init(id: String, link: InboxLink) {
        self.id = id
        self.link = link
    }

    var body: some View {
        chrome(content)
            .task(id: "\(id)#\(engine.dataVersion)") {
                await load()
                guard !counted, model.data != nil else { return }
                counted = true
                // (the page tells Canvas it is read just after answering: the sidebar's count once it has)
                try? await Task.sleep(nanoseconds: 2_000_000_000)
                if !Task.isCancelled { await engine.refreshSnapshot() }
            }
            .onChange(of: link?.starred) { _, on in
                // (the list's star is the one shown; kept here too, for when the list no longer holds the conversation)
                if let on, model.data?.starred != on { model.data?.starred = on }
            }
    }

    private var content: some View {
        let engine = self.engine
        return Group {
            if let d = model.data {
                thread(d)
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Theme.page)
        // (a link in a message goes through the app: a Canvas address to its screen, another site to the browser)
        .environment(\.openURL, OpenURLAction { url in
            Task { @MainActor in engine.openLink(url) }
            return .handled
        })
    }

    /// On a screen of its own: the subject as the window's title, the course under it, and Star and Canvas's menu in the
    /// toolbar. Beside the Inbox's list, the Inbox's own title and toolbar stay.
    @ViewBuilder
    private func chrome<V: View>(_ v: V) -> some View {
        if link == nil {
            v.navigationTitle(model.data?.subject ?? "Message")
                .navigationSubtitle(model.data?.context ?? "")
                .toolbar {
                    ToolbarItemGroup(placement: .primaryAction) {
                        Button {
                            setStarred(!starred)
                        } label: {
                            Label {
                                Text(starred ? "Unstar" : "Star")
                            } icon: {
                                Image(systemName: starred ? "star.fill" : "star")
                                    .contentTransition(.symbolEffect(.replace))
                            }
                        }
                        .help(starred ? "Unstar" : "Star")
                        .disabled(model.data == nil)
                        CanvasMenu(url: conversationAddress(id), title: model.data?.subject ?? "Message")
                    }
                }
        } else {
            v
        }
    }

    private func thread(_ d: ConversationData) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                header(d)
                    .padding(.bottom, 6)
                ForEach(d.messages) { m in
                    // (a reply sent rises in at the foot)
                    InboxBubble(message: m)
                        .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.move(edge: .bottom).combined(with: .opacity))
                }
            }
            .frame(maxWidth: 860, alignment: .leading)
            .padding(.horizontal, 28)
            .padding(.top, 22)
            .padding(.bottom, 18)
            .frame(maxWidth: .infinity)
        }
        .threadAnchor()
        .safeAreaInset(edge: .bottom, spacing: 0) { replyBar }
    }

    private func header(_ d: ConversationData) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                if link == nil {
                    ScreenHeading(title: d.subject)
                } else {
                    Text(d.subject)
                        .font(.system(size: 22, weight: .bold))
                        .tracking(-0.3)
                        .textSelection(.enabled)
                }
                Spacer(minLength: 8)
                starMark
            }
            if let p = d.people, !p.isEmpty { Fact(symbol: "person.2", text: p) }
            if let c = d.context, !c.isEmpty { Fact(symbol: "book.closed", text: c) }
        }
    }

    /// The conversation's star: a button beside the Inbox's list (the toolbar there is the Inbox's); on a screen of its
    /// own the mark alone, as the toolbar has the button.
    @ViewBuilder
    private var starMark: some View {
        if link != nil {
            Button {
                setStarred(!starred)
            } label: {
                Image(systemName: starred ? "star.fill" : "star")
                    .font(.title2)
                    .foregroundStyle(starred ? Color.yellow : Color.secondary)
                    .contentTransition(.symbolEffect(.replace))
            }
            .buttonStyle(.borderless)
            .help(starred ? "Unstar" : "Star")
            .accessibilityLabel(starred ? "Unstar" : "Star")
        } else if starred {
            Image(systemName: "star.fill")
                .font(.title2)
                .foregroundStyle(.yellow)
                .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.scale(scale: 0.6).combined(with: .opacity))
                .accessibilityLabel("Starred")
        }
    }

    /// The reply box and Send, over the foot of the thread; why a reply did not go, above them.
    private var replyBar: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let error {
                Label(error, systemImage: "exclamationmark.triangle.fill")
                    .font(.callout)
                    .foregroundStyle(.red)
                    .transition(.opacity)
            }
            HStack(alignment: .bottom, spacing: 10) {
                InboxReplyBox(text: draft, focus: $typing)
                Button {
                    send()
                } label: {
                    Group {
                        if sending {
                            ProgressView()
                                .controlSize(.small)
                        } else {
                            Label("Send", systemImage: "paperplane.fill")
                        }
                    }
                    .frame(minWidth: 64)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .keyboardShortcut(.return, modifiers: .command)
                .disabled(!canSend)
                .help("Send (⌘Return)")
            }
        }
        .frame(maxWidth: 860)
        .padding(.horizontal, 28)
        .padding(.vertical, 12)
        .frame(maxWidth: .infinity)
        .background(.regularMaterial)
        .overlay(alignment: .top) { Divider() }
    }

    /// The star shown: the Inbox list's while it holds the conversation, else this one's own.
    private var starred: Bool { link?.starred ?? (model.data?.starred == true) }

    private var draft: Binding<String> { link?.draft ?? $ownDraft }

    private var canSend: Bool {
        !sending && model.data != nil && !draft.wrappedValue.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    /// Starred at once, and back if Canvas says no. Beside the Inbox's list, the list's own star does it (one star).
    private func setStarred(_ on: Bool) {
        if let link, link.starred != nil {
            link.star(on)
            return
        }
        withAnimation(Motion.snappy) { model.data?.starred = on }
        Task {
            if await engine.act("star", ["id": id, "on": on]) {
                await load(animated: true)
            } else {
                withAnimation(Motion.snappy) { model.data?.starred = !on }
            }
        }
    }

    private func send() {
        guard canSend else { return }
        let text = draft.wrappedValue
        sending = true
        withAnimation(Motion.snappy) { error = nil }
        Task {
            do {
                _ = try await engine.call("sendReply", ["id": id, "body": text], as: OK.self)
                draft.wrappedValue = ""
                await load(animated: true)
                engine.changed()
            } catch {
                withAnimation(Motion.snappy) { self.error = error.localizedDescription }
            }
            sending = false
        }
    }

    private func load(animated: Bool = false) async {
        await model.load(engine, "conversation", ["id": id], animated: animated)
    }
}

/// A message in a conversation: the other person's at the left with their picture and name, yours at the right on a
/// wash of the accent; its words (selectable, their web addresses live), its files, and when it was sent.
private struct InboxBubble: View {
    let message: Message

    var body: some View {
        let mine = message.mine
        let shape = RoundedRectangle(cornerRadius: 16, style: .continuous)
        HStack(alignment: .bottom, spacing: 10) {
            if mine {
                Spacer(minLength: 72)
            } else {
                PersonAvatar(name: message.author, avatar: message.avatar, size: 30)
            }
            VStack(alignment: mine ? .trailing : .leading, spacing: 4) {
                if !mine {
                    Text(message.author)
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.secondary)
                }
                if !message.body.isEmpty {
                    Text(linkedText(message.body))
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                        .padding(.horizontal, 13)
                        .padding(.vertical, 9)
                        .background { shape.fill(mine ? Color.accentColor.opacity(0.16) : Theme.card) }
                        .overlay { shape.strokeBorder(mine ? Color.accentColor.opacity(0.22) : Theme.edge, lineWidth: 1) }
                }
                ForEach(message.attachments ?? []) { f in
                    InboxAttachmentChip(file: f)
                }
                if let w = message.when, !w.isEmpty {
                    Text(w)
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                }
            }
            .frame(maxWidth: 560, alignment: mine ? .trailing : .leading)
            if !mine { Spacer(minLength: 72) }
        }
        .frame(maxWidth: .infinity, alignment: mine ? .trailing : .leading)
    }
}

/// A file sent with a message: a click opens it in Quick Look.
private struct InboxAttachmentChip: View {
    let file: Attachment
    @EnvironmentObject private var engine: Engine

    var body: some View {
        Button {
            engine.openFile(file.url, name: file.name)
        } label: {
            Label(file.name, systemImage: "paperclip")
                .lineLimit(1)
                .truncationMode(.middle)
        }
        .buttonStyle(.bordered)
        .controlSize(.small)
        .help("Open \(file.name) in Quick Look")
        .contextMenu {
            Button("Open") { engine.openFile(file.url, name: file.name) }
            Button("Copy Link") {
                if let u = engine.absolute(file.url) { copyToPasteboard(u.absoluteString) }
            }
        }
    }
}

/// The reply box at a conversation's foot: it grows with what is written (to about seven lines, then it scrolls);
/// Return is a new line, ⌘Return sends.
private struct InboxReplyBox: View {
    @Binding var text: String
    var focus: FocusState<Bool>.Binding

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 10, style: .continuous)
        let focused = focus.wrappedValue
        // (the words again, unseen, give the box its height; the editor over them fills it)
        Text(measured)
            .font(.body)
            .lineLimit(7)
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity, minHeight: 34, alignment: .topLeading)
            .opacity(0)
            .accessibilityHidden(true)
            .overlay(alignment: .topLeading) {
                TextEditor(text: $text)
                    .font(.body)
                    .scrollContentBackground(.hidden)
                    .focusEffectDisabled()
                    .focused(focus)
                    .padding(.horizontal, 5)
                    .padding(.vertical, 8)
                    .accessibilityLabel("Reply")
            }
            .overlay(alignment: .topLeading) {
                if text.isEmpty {
                    Text("Write a reply")
                        .foregroundStyle(.tertiary)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 8)
                        .allowsHitTesting(false)
                }
            }
            .frame(maxHeight: 160)
            .background(Theme.card, in: shape)
            .overlay(shape.strokeBorder(focused ? Color.accentColor.opacity(0.6) : Theme.edge, lineWidth: focused ? 1.5 : 1))
            .animation(Motion.snappy, value: focused)
    }

    /// What is written, with a last empty line counted too.
    private var measured: String { text.isEmpty || text.hasSuffix("\n") ? text + " " : text }
}

private extension View {
    /// A conversation opens at its newest message and keeps it in view as replies come; a short one sits at the top
    /// (before macOS 15, a short one sits at the foot, as a chat's does).
    @ViewBuilder
    func threadAnchor() -> some View {
        if #available(macOS 15.0, *) {
            self.defaultScrollAnchor(.bottom, for: .initialOffset)
                .defaultScrollAnchor(.bottom, for: .sizeChanges)
                .defaultScrollAnchor(.top, for: .alignment)
        } else {
            self.defaultScrollAnchor(.bottom)
        }
    }
}

// MARK: - New Message

/// New Message: a course (optional — it narrows the people found to those in it), who it is to (found as you type; the
/// arrow keys and Return choose, and each one is a token that can be taken out again), a subject and the message.
/// ⌘Return sends; Escape cancels, asking first when something is written.
struct ComposeSheet: View {
    private let to: [Recipient]
    private let context: String?
    private let sent: () -> Void
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var people: [Recipient]
    @State private var course: String?
    @State private var contexts: [ComposeContext] = []
    @State private var query = ""
    @State private var found: [Recipient] = []
    /// The person found that Return adds (the arrow keys move it).
    @State private var highlighted = 0
    @State private var subject = ""
    @State private var message = ""
    @State private var sending = false
    @State private var error: String?
    @State private var confirmDiscard = false
    @FocusState private var focus: ComposeFocus?

    init(to: [Recipient] = [], context: String? = nil, sent: @escaping () -> Void = {}) {
        self.to = to
        self.context = context
        self.sent = sent
        _people = State(initialValue: to)
        _course = State(initialValue: context)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("New Message")
                .font(.title3.weight(.semibold))
                .padding(.horizontal, 20)
                .padding(.top, 18)
            Form {
                Section {
                    if !contexts.isEmpty || context != nil { coursePicker }
                    recipientsRow
                    ForEach(Array(suggestions.enumerated()), id: \.element.id) { i, r in
                        suggestionRow(r, highlighted: i == highlighted)
                    }
                    subjectRow
                }
                Section("Message") {
                    messageEditor
                }
                if let error {
                    Section {
                        Label(error, systemImage: "exclamationmark.triangle.fill")
                            .foregroundStyle(.red)
                    }
                }
            }
            .formStyle(.grouped)
            footer
        }
        .frame(minWidth: 560, idealWidth: 620, minHeight: 460, idealHeight: 560)
        .defaultFocus($focus, firstFocus)
        .interactiveDismissDisabled(sending)
        .task { await loadContexts() }
        .task(id: "\(query)|\(course ?? "")") { await search() }
        .confirmationDialog("Discard this message?", isPresented: $confirmDiscard) {
            Button("Discard", role: .destructive) { dismiss() }
        } message: {
            Text("It has not been sent.")
        }
    }

    /// The course a message is about: any, or one of the courses (it narrows the people found).
    private var coursePicker: some View {
        Picker("Course", selection: $course) {
            Text("Any Course").tag(String?.none)
            if let c = context, !contexts.contains(where: { $0.code == c }) {
                Text("This Course").tag(Optional(c))
            }
            ForEach(contexts) { c in
                Text(c.name).tag(Optional(c.code))
            }
        }
    }

    /// Who it is to: a token for each person chosen, and the field that finds more.
    private var recipientsRow: some View {
        HStack(alignment: .top, spacing: 12) {
            Text("To")
                .frame(width: 58, alignment: .leading)
                .padding(.top, people.isEmpty ? 0 : 3)
            VStack(alignment: .leading, spacing: 8) {
                if !people.isEmpty {
                    ComposeTokenFlow(spacing: 6) {
                        ForEach(people) { p in token(p) }
                    }
                }
                TextField("To", text: $query, prompt: Text(people.isEmpty ? "Search for a person or a course" : "Add someone else"))
                    .textFieldStyle(.plain)
                    .labelsHidden()
                    .focused($focus, equals: .to)
                    .onSubmit { takeHighlighted() }
                    .onKeyPress(.downArrow) { move(1) }
                    .onKeyPress(.upArrow) { move(-1) }
                    .onKeyPress(.delete) { removeLast() }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    /// A person chosen: their name, and the button that takes them out again.
    private func token(_ p: Recipient) -> some View {
        HStack(spacing: 3) {
            Text(p.name)
                .lineLimit(1)
            Button {
                remove(p)
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.borderless)
            .help("Remove \(p.name)")
            .accessibilityLabel("Remove \(p.name)")
        }
        .font(.callout)
        .padding(.leading, 9)
        .padding(.trailing, 5)
        .padding(.vertical, 3)
        .background(Color.accentColor.opacity(0.14), in: Capsule())
        .contextMenu {
            Button("Remove") { remove(p) }
        }
        .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.scale(scale: 0.9).combined(with: .opacity))
    }

    /// Someone found: a click (or Return while it is marked) adds them.
    private func suggestionRow(_ r: Recipient, highlighted on: Bool) -> some View {
        Button {
            add(r)
        } label: {
            HStack(spacing: 10) {
                // (a course, a group or a section reaches many people: its own symbol, not initials)
                if r.id.contains("_") {
                    IconTile(symbol: "person.3.fill", color: .accentColor, size: 26)
                } else {
                    PersonAvatar(name: r.name, size: 26)
                }
                VStack(alignment: .leading, spacing: 1) {
                    Text(r.name)
                        .foregroundStyle(.primary)
                        .lineLimit(1)
                    if let s = r.sub, !s.isEmpty {
                        Text(s)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
                Spacer(minLength: 8)
                Image(systemName: "plus.circle")
                    .foregroundStyle(.tint)
            }
            .padding(.horizontal, 6)
            .padding(.vertical, 3)
            .frame(maxWidth: .infinity, alignment: .leading)
            .contentShape(Rectangle())
            .background(on ? Color.accentColor.opacity(0.12) : Color.clear, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Add \(r.name)")
    }

    private var subjectRow: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text("Subject")
                .frame(width: 58, alignment: .leading)
            TextField("Subject", text: $subject, prompt: Text("Optional"))
                .textFieldStyle(.plain)
                .labelsHidden()
                .focused($focus, equals: .subject)
                .onSubmit { focus = .message }
        }
    }

    private var messageEditor: some View {
        TextEditor(text: $message)
            .font(.body)
            .scrollContentBackground(.hidden)
            .focusEffectDisabled()
            .focused($focus, equals: .message)
            .frame(minHeight: 180)
            .overlay(alignment: .topLeading) {
                if message.isEmpty {
                    Text("Write your message")
                        .foregroundStyle(.tertiary)
                        .padding(.leading, 5)
                        .allowsHitTesting(false)
                }
            }
            .accessibilityLabel("Message")
    }

    /// Cancel and Send, at the sheet's foot; while it goes, a word that it is going.
    private var footer: some View {
        HStack(spacing: 10) {
            if sending {
                ProgressView()
                    .controlSize(.small)
                Text("Sending…")
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Button("Cancel") { cancel() }
                .keyboardShortcut(.cancelAction)
                .disabled(sending)
            Button("Send") { send() }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.return, modifiers: .command)
                .disabled(!canSend)
                .help("Send (⌘Return)")
        }
        .padding(.horizontal, 20)
        .padding(.top, 4)
        .padding(.bottom, 16)
    }

    private var firstFocus: ComposeFocus { to.isEmpty ? .to : .subject }

    private var canSend: Bool {
        !sending && !people.isEmpty && !message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    /// The people found and not yet added: the first eight.
    private var suggestions: [Recipient] {
        Array(found.filter { f in !people.contains { $0.id == f.id } }.prefix(8))
    }

    private func loadContexts() async {
        guard contexts.isEmpty, let c = try? await engine.call("composeContexts", as: ComposeContextsData.self) else { return }
        withAnimation(Motion.gentle) { contexts = c.rows }
    }

    private func search() async {
        let q = query.trimmingCharacters(in: .whitespaces)
        guard q.count >= 2 else {
            found = []
            return
        }
        try? await Task.sleep(nanoseconds: 300_000_000) // (typed on meanwhile: this search gives way to the next)
        guard !Task.isCancelled else { return }
        var args: [String: Any] = ["q": q]
        if let course { args["context"] = course }
        if let r = try? await engine.call("recipients", args, as: RecipientsData.self), !Task.isCancelled {
            found = r.rows
            highlighted = 0
        }
    }

    private func add(_ r: Recipient) {
        withAnimation(Motion.snappy) {
            if !people.contains(where: { $0.id == r.id }) { people.append(r) }
        }
        query = ""
        found = []
        highlighted = 0
        focus = .to
    }

    private func remove(_ r: Recipient) {
        withAnimation(Motion.snappy) { people.removeAll { $0.id == r.id } }
    }

    private func move(_ step: Int) -> KeyPress.Result {
        let n = suggestions.count
        guard n > 0 else { return .ignored }
        highlighted = min(max(highlighted + step, 0), n - 1)
        return .handled
    }

    /// Return in the field: the person marked is added; with nothing typed, on to the subject.
    private func takeHighlighted() {
        let list = suggestions
        if list.indices.contains(highlighted) {
            add(list[highlighted])
        } else if query.trimmingCharacters(in: .whitespaces).isEmpty && !people.isEmpty {
            focus = .subject
        }
    }

    /// Delete in the empty field takes out the last person, as a token field does.
    private func removeLast() -> KeyPress.Result {
        guard query.isEmpty, let last = people.last else { return .ignored }
        remove(last)
        return .handled
    }

    private func cancel() {
        if message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            dismiss()
        } else {
            confirmDiscard = true
        }
    }

    private func send() {
        guard canSend else { return }
        sending = true
        withAnimation(Motion.snappy) { error = nil }
        var args: [String: Any] = ["recipients": people.map(\.id), "subject": subject, "body": message]
        if let course { args["context"] = course }
        let payload = args
        Task {
            do {
                _ = try await engine.call("sendMessage", payload, as: OK.self)
                sent()
                engine.changed()
                dismiss()
            } catch {
                withAnimation(Motion.snappy) { self.error = error.localizedDescription }
            }
            sending = false
        }
    }
}

/// Where the typing is in New Message.
private enum ComposeFocus: Hashable {
    case to, subject, message
}

/// The people a message is to, in lines that wrap as words do.
private struct ComposeTokenFlow: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let lines = arrange(subviews, in: proposal.width ?? .infinity)
        let height = lines.reduce(0) { $0 + $1.height } + spacing * CGFloat(max(lines.count - 1, 0))
        let widest = lines.map(\.width).max() ?? 0
        return CGSize(width: proposal.width ?? widest, height: height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var y = bounds.minY
        for line in arrange(subviews, in: bounds.width) {
            var x = bounds.minX
            for item in line.items {
                subviews[item.index].place(at: CGPoint(x: x, y: y + (line.height - item.size.height) / 2), proposal: ProposedViewSize(item.size))
                x += item.size.width + spacing
            }
            y += line.height + spacing
        }
    }

    /// A line of tokens: which, how wide, how tall.
    private struct Line {
        var items: [(index: Int, size: CGSize)] = []
        var width: CGFloat = 0
        var height: CGFloat = 0
    }

    private func arrange(_ subviews: Subviews, in width: CGFloat) -> [Line] {
        var lines: [Line] = []
        var line = Line()
        for index in subviews.indices {
            let size = subviews[index].sizeThatFits(ProposedViewSize(width: width, height: nil))
            if !line.items.isEmpty && line.width + spacing + size.width > width {
                lines.append(line)
                line = Line()
            }
            line.width = line.items.isEmpty ? size.width : line.width + spacing + size.width
            line.height = max(line.height, size.height)
            line.items.append((index: index, size: size))
        }
        if !line.items.isEmpty { lines.append(line) }
        return lines
    }
}

// MARK: - Groups

/// Groups: the student's groups — the current ones, then the past ones — each a card in its course's colour that opens
/// the group's own home.
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
                        if !d.current.isEmpty { cards("Current", d.current) }
                        if !d.past.isEmpty { cards("Past", d.past) }
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

    private func cards(_ title: String, _ rows: [GroupRow]) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            CardHeading(text: title, trailing: "\(rows.count)")
                .padding(.horizontal, 6)
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 14)], spacing: 14) {
                ForEach(rows) { g in
                    Button {
                        engine.go(.home("groups/\(g.id)"))
                    } label: {
                        GroupsCard(group: g)
                    }
                    .buttonStyle(CardButtonStyle())
                    .help("Open \(g.name)")
                    .contextMenu {
                        Button("Open") { engine.go(.home("groups/\(g.id)")) }
                        Divider()
                        Button("Open in Canvas") { engine.openWebScreen("/groups/\(g.id)", title: g.name) }
                        Button("Copy Link") {
                            if let u = engine.absolute("/groups/\(g.id)") { copyToPasteboard(u.absoluteString) }
                        }
                    }
                }
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

/// A group as a card: its symbol on its course's colour, its name, and its course and how many are in it.
private struct GroupsCard: View {
    let group: GroupRow

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top) {
                IconTile(symbol: "person.3.fill", color: Color(hex: group.color), size: 36)
                Spacer(minLength: 8)
                Image(systemName: "chevron.right")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.tertiary)
            }
            VStack(alignment: .leading, spacing: 3) {
                Text(group.name)
                    .font(.headline)
                    .lineLimit(2)
                    .multilineTextAlignment(.leading)
                if let sub = group.sub, !sub.isEmpty {
                    Text(sub)
                        .font(.callout)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                        .multilineTextAlignment(.leading)
                }
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, minHeight: 118, alignment: .topLeading)
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Shared in this file

/// A conversation's own address (Canvas's links name it so, and Simpl's web Inbox opens at it).
private func conversationAddress(_ id: String) -> String { "/conversations?id=\(id)" }

/// What finds the web addresses in a message's words.
private let inboxLinkDetector = try? NSDataDetector(types: NSTextCheckingResult.CheckingType.link.rawValue)

/// A message's words with their web addresses made links.
private func linkedText(_ text: String) -> AttributedString {
    var out = AttributedString(text)
    guard let detector = inboxLinkDetector else { return out }
    for match in detector.matches(in: text, options: [], range: NSRange(text.startIndex..., in: text)) {
        guard let url = match.url, let range = Range(match.range, in: out) else { continue }
        out[range].link = url
    }
    return out
}

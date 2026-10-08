import SwiftUI

// MARK: - A conversation

/// One conversation: its subject, who is in it and its course, then its messages in order as Mail shows a thread — each
/// with its sender's picture and name, when it was sent, its words (selectable, their web addresses live) and its files
/// as glass chips (opened in Quick Look), a hairline between one and the next; its star; and the reply bar floating in
/// glass at its foot (one line that grows with what is written, to six, then scrolls; ⌘Return sends). (1.2) The
/// messages keep a readable width however wide the window; on a wide one, who is in it, its course and every file sent in
/// it stand in a column at the right. Pushed on a place it is a screen of its own, with its own title and toolbar;
/// beside the Inbox's list it is the reading pane.
struct ConversationView: View {
    let id: String
    /// Beside the Inbox's list (nil on a screen of its own).
    var link: InboxLink?
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<ConversationData>()
    @ObservedObject private var store = InboxStore.shared
    @State private var sending = false
    @State private var error: String?
    /// The sidebar's unread count asked for again, once, after the first answer (Canvas marks the conversation read then).
    @State private var counted = false
    @FocusState private var typing: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(id: String) {
        self.id = id
    }

    init(id: String, link: InboxLink) {
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
                GeometryReader { geo in
                    layout(d, wide: geo.size.width >= InboxThread.sideColumnFrom)
                }
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

    /// The thread, and on a wide window its people, course and files in a column beside it.
    private func layout(_ d: ConversationData, wide: Bool) -> some View {
        HStack(spacing: 0) {
            thread(d, wide: wide)
            if wide {
                Divider()
                InboxThreadDetails(data: d)
                    .frame(width: InboxThread.sideColumn)
            }
        }
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
                        .keyboardShortcut("l", modifiers: [.command, .shift])
                        .help(starred ? "Unstar (⇧⌘L)" : "Star (⇧⌘L)")
                        .disabled(model.data == nil)
                        CanvasMenu(url: inboxConversationAddress(id), title: model.data?.subject ?? "Message")
                    }
                }
        } else {
            v
        }
    }

    private func thread(_ d: ConversationData, wide: Bool) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                header(d, wide: wide)
                    .padding(.bottom, 20)
                ForEach(d.messages) { m in
                    // (a reply sent rises in at the foot)
                    InboxLetter(message: m)
                        .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.move(edge: .bottom).combined(with: .opacity))
                }
            }
            .frame(maxWidth: InboxThread.column, alignment: .leading)
            .padding(.horizontal, 32)
            .padding(.top, 26)
            .padding(.bottom, 14)
            .frame(maxWidth: .infinity)
        }
        .threadAnchor()
        .safeAreaInset(edge: .bottom, spacing: 0) { replyBar(d) }
    }

    /// The subject and the star; under them (unless the column at the right says so) who is in it and its course.
    private func header(_ d: ConversationData, wide: Bool) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .center, spacing: 12) {
                if link == nil {
                    ScreenHeading(title: d.subject)
                } else {
                    Text(d.subject)
                        .font(wide ? .sTitle : .sTitle2)
                        .tracking(-0.4)
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 8)
                starMark
            }
            if !wide {
                if let p = d.people, !p.isEmpty { Fact(symbol: "person.2", text: p) }
                if let c = d.context, !c.isEmpty { Fact(symbol: "book.closed", text: c) }
            }
        }
    }

    /// The conversation's star: a glass button beside the Inbox's list (the toolbar there is the Inbox's); on a screen
    /// of its own the mark alone, as the toolbar has the button.
    @ViewBuilder
    private var starMark: some View {
        if link != nil {
            Button {
                setStarred(!starred)
            } label: {
                Image(systemName: starred ? "star.fill" : "star")
                    .font(.sTitle3.weight(.regular))
                    .foregroundStyle(starred ? Color.yellow : Color.secondary)
                    .contentTransition(.symbolEffect(.replace))
                    .frame(width: 40, height: 40)
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .glass(Circle(), interactive: true)
            .keyboardShortcut("l", modifiers: [.command, .shift])
            .help(starred ? "Unstar (⇧⌘L)" : "Star (⇧⌘L)")
            .accessibilityLabel(starred ? "Unstar" : "Star")
        } else if starred {
            Image(systemName: "star.fill")
                .font(.sTitle2)
                .foregroundStyle(.yellow)
                .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.scale(scale: 0.6).combined(with: .opacity))
                .accessibilityLabel("Starred")
        }
    }

    // MARK: The reply bar

    /// The reply bar, floating in glass over the foot of the thread: the reply (a line, growing to six) and Send; why a
    /// reply did not go, above it.
    private func replyBar(_ d: ConversationData) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            if let error {
                Label(error, systemImage: "exclamationmark.triangle.fill")
                    .font(.sCallout)
                    .foregroundStyle(.red)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 7)
                    .glassCapsule(tint: Color.red.opacity(0.25))
                    .transition(.opacity)
            }
            composer(d)
        }
        .frame(maxWidth: InboxThread.column)
        .padding(.horizontal, 32)
        .padding(.top, 6)
        .padding(.bottom, 16)
        .frame(maxWidth: .infinity)
    }

    private func composer(_ d: ConversationData) -> some View {
        let shape = RoundedRectangle(cornerRadius: 24, style: .continuous)
        return HStack(alignment: .bottom, spacing: 8) {
            InboxReplyBox(text: draft, focus: $typing, prompt: replyPrompt(d))
            if !draft.wrappedValue.isEmpty && !sending {
                Text("⌘↩")
                    .font(.sCaption.weight(.medium))
                    .foregroundStyle(.tertiary)
                    .padding(.bottom, 10)
                    .accessibilityHidden(true)
            }
            sendButton
        }
        .padding(.leading, 10)
        .padding(.trailing, 6)
        .padding(.vertical, 6)
        // (a click anywhere on the bar puts the typing there)
        .background {
            Color.clear
                .contentShape(Rectangle())
                .onTapGesture { typing = true }
        }
        .glass(shape)
        .overlay(shape.strokeBorder(Color.accentColor.opacity(typing ? 0.45 : 0), lineWidth: 1.5))
        .animation(Motion.snappy, value: typing)
    }

    private var sendButton: some View {
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
            .font(.sCallout.weight(.semibold))
            .frame(minWidth: 68)
        }
        .glassButton(prominent: true)
        .controlSize(.large)
        .buttonBorderShape(.capsule)
        .keyboardShortcut(.return, modifiers: .command)
        .disabled(!canSend)
        .help("Send (⌘Return)")
    }

    /// The reply's placeholder: to whom it goes.
    private func replyPrompt(_ d: ConversationData) -> String {
        let people = (d.people ?? "").components(separatedBy: ", ").filter { !$0.isEmpty }
        switch people.count {
        case 0: return "Write a reply"
        case 1: return "Reply to \(people[0])"
        default: return "Reply to all \(people.count)"
        }
    }

    // MARK: What it does

    /// The star shown: the Inbox list's while it holds the conversation, else this one's own.
    private var starred: Bool { link?.starred ?? (model.data?.starred == true) }

    /// The reply begun here (kept by the Inbox while other conversations are read, and on a screen of its own too).
    private var draft: Binding<String> {
        let id = self.id
        let store = self.store
        return Binding(get: { store.drafts[id] ?? "" }, set: { store.drafts[id] = $0.isEmpty ? nil : $0 })
    }

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

    /// The conversation read (as last read at once, if it has been, while Canvas is asked again).
    private func load(animated: Bool = false) async {
        store.bind(engine)
        if model.data == nil, let kept = store.conversations[id] {
            model.data = kept
        }
        await model.load(engine, "conversation", ["id": id], animated: animated)
        if let d = model.data { store.conversations[id] = d }
    }
}

/// (1.2) The thread's measures: the messages' readable width, and from how wide the conversation shows the column of
/// its people, course and files.
private enum InboxThread {
    static let column: CGFloat = 780
    static let sideColumnFrom: CGFloat = 1100
    static let sideColumn: CGFloat = 290
}

/// A message in a conversation, as Mail shows one in a thread (no bubble, no box): a hairline above it, the sender's
/// picture, their name ("You" for yours, in the accent) and when it was sent, its words, its files as glass chips.
private struct InboxLetter: View {
    let message: Message

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Divider()
            HStack(alignment: .top, spacing: 14) {
                InboxAvatar(name: message.author, avatar: message.avatar, size: 40)
                VStack(alignment: .leading, spacing: 8) {
                    heading
                    if !message.body.isEmpty {
                        Text(inboxLinkedText(message.body))
                            .font(.sBody)
                            .lineSpacing(3)
                            .textSelection(.enabled)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    if let files = message.attachments, !files.isEmpty {
                        InboxFlow(spacing: 8) {
                            ForEach(files) { f in InboxAttachmentChip(file: f) }
                        }
                        .padding(.top, 2)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .padding(.vertical, 18)
        }
    }

    private var heading: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(message.mine ? "You" : message.author)
                .font(.sHeadline)
                .foregroundStyle(message.mine ? Color.accentColor : Color.primary)
                .lineLimit(1)
                .help(message.author)
            Spacer(minLength: 8)
            if let w = message.when, !w.isEmpty {
                Text(w)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
    }
}

/// A file sent with a message, as a glass chip with its kind's symbol: a click opens it in Quick Look.
private struct InboxAttachmentChip: View {
    let file: Attachment
    @EnvironmentObject private var engine: Engine

    var body: some View {
        Button {
            engine.openFile(file.url, name: file.name)
        } label: {
            HStack(spacing: 7) {
                Image(systemName: InboxFiles.symbol(file.name))
                    .font(.sCallout.weight(.medium))
                    .foregroundStyle(.tint)
                Text(file.name)
                    .font(.sCallout)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            .padding(.horizontal, 13)
            .padding(.vertical, 8)
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .glassCapsule(interactive: true)
        .help("Open \(file.name) in Quick Look")
        .contextMenu {
            Button("Open") { engine.openFile(file.url, name: file.name) }
            Button("Copy Link") {
                if let u = engine.absolute(file.url) { copyToPasteboard(u.absoluteString) }
            }
        }
    }
}

/// A file's kind by the end of its name, as a symbol.
private enum InboxFiles {
    static func symbol(_ name: String) -> String {
        switch (name as NSString).pathExtension.lowercased() {
        case "pdf": return "doc.richtext"
        case "png", "jpg", "jpeg", "gif", "heic", "webp", "bmp", "tif", "tiff", "svg": return "photo"
        case "mp4", "mov", "m4v", "avi", "webm": return "film"
        case "mp3", "m4a", "wav", "aac", "ogg": return "waveform"
        case "zip", "gz", "tar", "rar", "7z": return "doc.zipper"
        case "doc", "docx", "pages", "txt", "rtf", "odt": return "doc.text"
        case "xls", "xlsx", "numbers", "csv": return "tablecells"
        case "ppt", "pptx", "key": return "rectangle.on.rectangle"
        default: return "paperclip"
        }
    }
}

/// (1.2) Beside a conversation on a wide window: who is in it (their pictures as the messages show them), its course,
/// how many messages and when, and every file sent in it — no boxes, a hairline at its left.
private struct InboxThreadDetails: View {
    let data: ConversationData
    @EnvironmentObject private var engine: Engine

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 26) {
                if !people.isEmpty { peopleSection }
                if let c = data.context, !c.isEmpty {
                    section("Course") { Fact(symbol: "book.closed", text: c) }
                }
                messagesSection
                if !files.isEmpty { filesSection }
            }
            .padding(.horizontal, 22)
            .padding(.vertical, 28)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private var peopleSection: some View {
        section("People", trailing: "\(people.count)") {
            ForEach(Array(people.enumerated()), id: \.offset) { _, name in
                HStack(spacing: 10) {
                    InboxAvatar(name: name, avatar: avatar(of: name), size: 30)
                    Text(name)
                        .font(.sCallout)
                        .lineLimit(1)
                        .textSelection(.enabled)
                }
            }
        }
    }

    private var messagesSection: some View {
        section("Messages", trailing: "\(data.messages.count)") {
            if let first = data.messages.first?.when, !first.isEmpty {
                Fact(symbol: "clock", text: "Started \(first)")
            }
            if data.messages.count > 1, let last = data.messages.last?.when, !last.isEmpty {
                Fact(symbol: "arrowshape.turn.up.left", text: "Latest \(last)")
            }
            Fact(symbol: "person.crop.circle", text: mineLine)
        }
    }

    private var filesSection: some View {
        section("Files", trailing: "\(files.count)") {
            VStack(alignment: .leading, spacing: 0) {
                ForEach(files) { f in
                    Button {
                        engine.openFile(f.url, name: f.name)
                    } label: {
                        HStack(spacing: 9) {
                            Image(systemName: InboxFiles.symbol(f.name))
                                .foregroundStyle(.tint)
                                .frame(width: 18)
                            Text(f.name)
                                .lineLimit(1)
                                .truncationMode(.middle)
                        }
                        .font(.sCallout)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 7)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .buttonStyle(RowButtonStyle())
                    .help("Open \(f.name) in Quick Look")
                }
            }
            .padding(.horizontal, -6)
        }
    }

    private func section<C: View>(_ title: String, trailing: String? = nil, @ViewBuilder content: () -> C) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            CardHeading(text: title, trailing: trailing)
            content()
        }
    }

    /// Everyone in it but you.
    private var people: [String] {
        (data.people ?? "").components(separatedBy: ", ").filter { !$0.isEmpty }
    }

    /// Every file sent in it, once each.
    private var files: [Attachment] {
        var seen = Set<String>()
        return data.messages.flatMap { $0.attachments ?? [] }.filter { seen.insert($0.url).inserted }
    }

    private var mineLine: String {
        let n = data.messages.filter(\.mine).count
        switch n {
        case 0: return "None from you yet"
        case 1: return "1 from you"
        default: return "\(n) from you"
        }
    }

    /// A person's picture, from a message of theirs.
    private func avatar(of name: String) -> String? {
        data.messages.first { $0.author == name && $0.avatar != nil }?.avatar
    }
}

/// The reply at a conversation's foot: a line that grows with what is written (to six lines, then it scrolls); Return
/// is a new line, ⌘Return sends.
private struct InboxReplyBox: View {
    @Binding var text: String
    var focus: FocusState<Bool>.Binding
    var prompt: String

    var body: some View {
        // (the words again, unseen, give the box its height; the editor over them fills it)
        Text(measured)
            .font(.sBody)
            .lineLimit(6)
            .padding(.horizontal, 6)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity, minHeight: 36, alignment: .topLeading)
            .opacity(0)
            .accessibilityHidden(true)
            .overlay(alignment: .topLeading) {
                TextEditor(text: $text)
                    .font(.sBody)
                    .scrollContentBackground(.hidden)
                    .focusEffectDisabled()
                    .focused(focus)
                    .padding(.horizontal, 1)
                    .padding(.vertical, 8)
                    .accessibilityLabel("Reply")
            }
            .overlay(alignment: .topLeading) {
                if text.isEmpty {
                    Text(prompt)
                        .font(.sBody)
                        .foregroundStyle(.tertiary)
                        .lineLimit(1)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 8)
                        .allowsHitTesting(false)
                }
            }
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

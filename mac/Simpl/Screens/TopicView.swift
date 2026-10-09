import SwiftUI

/// One announcement or discussion, as a page of its own: the post — who wrote it and when, its words, its files, the
/// assignment it is graded as — then every reply in reading order, threaded by depth, each step further in on a line in
/// the course's colour. Reply writes under the post, and a reply's own Reply writes under that reply: ⌘Return posts,
/// Escape puts the box away. A discussion that wants your post first says so, and a closed one says why. Opening it
/// marks it read.
struct TopicView: View {
    let ctx: String
    let id: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<TopicData>()
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// Where a reply is being written: "topic" under the post, else the id of the reply it answers.
    @State private var replyingTo: String?
    /// The box to bring into view once it is laid out.
    @State private var jump: String?

    /// How much further in each step of a thread sits.
    private static let indent: CGFloat = 20

    var body: some View {
        Group {
            if let d = model.data {
                ScrollViewReader { proxy in
                    page(d)
                        .onChange(of: jump) { _, target in scroll(proxy, to: target) }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(model.data?.title ?? "Discussion")
        .navigationSubtitle(model.data.map { kindLine($0) } ?? "")
        .toolbar {
            ToolbarItemGroup(placement: .primaryAction) {
                if let d = model.data {
                    NeighbourButtons(ctx: ctx, type: d.announcement == true ? "Announcement" : "Discussion", id: id)
                }
                Button { reply(to: nil) } label: {
                    Label("Reply", systemImage: "arrowshape.turn.up.left")
                }
                .help(replyTip)
                .disabled(model.data?.canReply != true)
                CanvasMenu(url: "/\(ctx)/discussion_topics/\(id)?bcv=native", title: model.data?.title ?? "Discussion")
            }
        }
        .task(id: engine.dataVersion) { await load() }
    }

    private var replyTip: String {
        guard let d = model.data else { return "Reply" }
        guard d.canReply == true else { return "Replies are closed" }
        return d.announcement == true ? "Reply to this announcement" : "Reply to this discussion"
    }

    // MARK: - The page

    /// (1.2) At a reading width, centred: a thread is read line by line, and lines across the whole of a wide window are
    /// hard to follow.
    private func page(_ d: TopicData) -> some View {
        let color = Color(hex: d.color)
        return Page(maxWidth: 1000) {
            ScreenHeading(title: d.title, sub: kindLine(d), color: color)
            postCard(d, color)
            if replyingTo == "topic" {
                composer(heading: d.announcement == true ? "Reply to the announcement" : "Reply to the discussion", parent: nil)
                    .padding(18)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .card()
                    .id("composer")
                    .transition(entering)
            }
            repliesCard(d, color)
        }
        .font(.sBody)
    }

    /// The post: who wrote it (or, when Canvas names nobody, the course, on its own colour), when, the assignment it is
    /// graded as, its words and its files.
    private func postCard(_ d: TopicData, _ color: Color) -> some View {
        let who = (d.author ?? "").isEmpty ? nil : d.author
        let parts: [String?] = [who == nil ? nil : d.context, d.when]
        let line = parts.compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · ")
        return VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 12) {
                if let who {
                    PersonAvatar(name: who, avatar: d.avatar, size: 42)
                } else {
                    IconTile(symbol: d.announcement == true ? "megaphone.fill" : "bubble.left.and.bubble.right.fill", color: color, size: 42)
                }
                VStack(alignment: .leading, spacing: 1) {
                    Text(who ?? d.context ?? "")
                        .font(.sHeadline)
                    if !line.isEmpty {
                        Text(line)
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                    }
                }
                Spacer(minLength: 0)
            }
            .accessibilityElement(children: .combine)
            if let g = d.graded, !g.isEmpty { gradedLink(g, d) }
            if !d.html.isEmpty { RichText(html: d.html) }
            if let files = d.attachments, !files.isEmpty { attachments(files) }
        }
        .padding(22)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    /// Graded: its points and due date, and a way to the assignment it is.
    private func gradedLink(_ g: String, _ d: TopicData) -> some View {
        Button {
            engine.go(d.assignmentUrl, title: d.title)
        } label: {
            HStack(spacing: 6) {
                StatusChip(text: "Graded", tone: "purple")
                Text(g)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                if d.assignmentUrl != nil {
                    Image(systemName: "chevron.right")
                        .font(.sFootnote.weight(.semibold))
                        .foregroundStyle(.tertiary)
                }
            }
            .padding(.horizontal, 6)
            .padding(.vertical, 4)
        }
        .buttonStyle(RowButtonStyle(radius: 8))
        .disabled(d.assignmentUrl == nil)
        .help("Open the assignment")
    }

    private func attachments(_ files: [Attachment]) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Divider()
                .padding(.horizontal, 8)
                .padding(.bottom, 4)
            CardHeading(text: "Attached")
                .padding(.horizontal, 8)
            ForEach(files) { f in
                RowLink { engine.openFile(f.url, name: f.name) } label: {
                    InfoRow(title: f.name, symbol: "paperclip", tint: .blue)
                }
                .help("Open \(f.name) in Quick Look")
                .contextMenu {
                    Button("Quick Look") { engine.openFile(f.url, name: f.name) }
                    Button("Copy Link") {
                        if let u = engine.absolute(f.url) { copyToPasteboard(u.absoluteString) }
                    }
                }
            }
        }
        .padding(.horizontal, -8) // (the rows' words line up with the post's, their wash reaching into the card's margin)
    }

    /// Every reply in reading order, threaded by depth; or why there are none to see.
    private func repliesCard(_ d: TopicData, _ color: Color) -> some View {
        CardSection(title: "Replies", trailing: d.count.flatMap { $0 > 0 ? "\($0)" : nil }) {
            repliesNote(d)
            ForEach(Array(d.entries.enumerated()), id: \.element.id) { i, e in
                if i > 0 { RowDivider(inset: 47 + CGFloat(max(e.depth, 0)) * TopicView.indent) }
                entryRow(e, d, color)
                if replyingTo == e.id {
                    // (1.2) in the thread, one step in on its line in the course's colour — no box of its own
                    composer(heading: e.author.isEmpty ? "Reply" : "Reply to \(e.author)", parent: e.id)
                        .padding(.leading, 14)
                        .padding(.vertical, 4)
                        .overlay(alignment: .leading) {
                            RoundedRectangle(cornerRadius: 1.25, style: .continuous)
                                .fill(color.opacity(0.6))
                                .frame(width: 2.5)
                        }
                        .padding(.leading, 8 + CGFloat(min(max(e.depth, 0) + 1, 4)) * TopicView.indent)
                        .padding(.trailing, 8)
                        .padding(.bottom, 10)
                        .id("composer")
                        .transition(entering)
                }
            }
        }
    }

    @ViewBuilder
    private func repliesNote(_ d: TopicData) -> some View {
        if d.locked == true, let t = d.lockText, !t.isEmpty {
            note(t, symbol: "lock.fill")
        } else if d.needFirst == true {
            HStack(spacing: 10) {
                note("Reply to see what others wrote.", symbol: "eye.slash")
                Spacer(minLength: 8)
                if d.canReply == true {
                    Button("Write Your Reply") { reply(to: nil) }
                        .buttonStyle(.link)
                        .padding(.trailing, 8)
                }
            }
        } else if d.entries.isEmpty {
            EmptyNote(text: d.announcement == true ? "No replies." : "No replies yet. Be the first.", symbol: "bubble.left")
                .padding(.horizontal, 8)
        }
    }

    private func note(_ text: String, symbol: String) -> some View {
        Label(text, systemImage: symbol)
            .font(.sCallout)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
            .padding(.horizontal, 8)
            .padding(.vertical, 10)
    }

    /// A reply: who wrote it and when, its words (Canvas's own when they hold pictures, tables or formulas), and Reply;
    /// further in by its depth, on the thread's line. A deleted one says so.
    private func entryRow(_ e: Entry, _ d: TopicData, _ color: Color) -> some View {
        let depth = CGFloat(max(e.depth, 0))
        return VStack(alignment: .leading, spacing: 6) {
            if e.deleted == true {
                Text(e.text)
                    .font(.sCallout)
                    .italic()
                    .foregroundStyle(.secondary)
            } else {
                HStack(spacing: 9) {
                    PersonAvatar(name: e.author, avatar: e.avatar, size: 30)
                    Text(e.author)
                        .font(.sBody.weight(.semibold))
                        .lineLimit(1)
                    if let w = e.when, !w.isEmpty {
                        Text(w)
                            .font(.sFootnote)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                    Spacer(minLength: 4)
                }
                .accessibilityElement(children: .combine)
                Group {
                    if e.rich == true, let h = e.html, !h.isEmpty {
                        RichText(html: h)
                    } else {
                        Text(e.text)
                            .font(.sBody)
                            .textSelection(.enabled)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                .padding(.leading, 39)
                if d.canReply == true {
                    Button { reply(to: e) } label: {
                        Label("Reply", systemImage: "arrowshape.turn.up.left")
                    }
                    .buttonStyle(.borderless)
                    .font(.sFootnote.weight(.medium))
                    .padding(.leading, 39)
                    .help("Reply to \(e.author)")
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.leading, depth * TopicView.indent)
        .background(alignment: .leading) {
            if e.depth > 0 {
                RoundedRectangle(cornerRadius: 1.25, style: .continuous)
                    .fill(color.opacity(0.35))
                    .frame(width: 2.5)
                    .padding(.leading, (depth - 1) * TopicView.indent + 8)
            }
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 10)
        .contextMenu {
            if e.deleted != true {
                if d.canReply == true {
                    Button("Reply") { reply(to: e) }
                }
                Button("Copy Text") { copyToPasteboard(e.text) }
            }
        }
    }

    private func composer(heading: String, parent: String?) -> some View {
        TopicComposer(heading: heading) { text in
            try await post(text, parent: parent)
        } cancel: {
            withAnimation(Motion.gentle) { replyingTo = nil }
        }
    }

    private var entering: AnyTransition {
        reduceMotion ? .opacity : .opacity.combined(with: .move(edge: .top))
    }

    private func kindLine(_ d: TopicData) -> String {
        let kind = d.announcement == true ? "Announcement" : "Discussion"
        return [kind, d.context ?? ""].filter { !$0.isEmpty }.joined(separator: " · ")
    }

    // MARK: - Doing

    /// A box to write in: under the post (`nil`), or under the reply it answers.
    private func reply(to e: Entry?) {
        guard model.data?.canReply == true else { return }
        withAnimation(Motion.gentle) { replyingTo = e?.id ?? "topic" }
        jump = "composer"
    }

    private func post(_ text: String, parent: String?) async throws {
        var args: [String: Any] = ["ctx": ctx, "id": id, "text": text]
        if let parent { args["parent"] = parent }
        _ = try await engine.call("reply", args, as: OK.self)
        withAnimation(Motion.gentle) { replyingTo = nil }
        await model.load(engine, "topic", ["ctx": ctx, "id": id], animated: true)
        engine.changed()
    }

    private func scroll(_ proxy: ScrollViewProxy, to target: String?) {
        guard let target else { return }
        jump = nil
        Task {
            try? await Task.sleep(nanoseconds: 80_000_000) // (the box laid out first)
            withAnimation(reduceMotion ? nil : Motion.gentle) { proxy.scrollTo(target, anchor: .center) }
        }
    }

    private func load() async {
        await model.load(engine, "topic", ["ctx": ctx, "id": id]) // (the page marks it read as it answers)
    }
}

/// Writing a reply in place — under the post, or under the reply it answers: the box takes the keys at once, ⌘Return
/// posts, Escape puts it away. It stays until the words are posted, and says so if they could not be.
private struct TopicComposer: View {
    let heading: String
    let send: (String) async throws -> Void
    let cancel: () -> Void
    @State private var text = ""
    @State private var sending = false
    @State private var error: String?
    @FocusState private var focused: Bool

    private var empty: Bool { text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(heading)
                .font(.sHeadline)
            ZStack(alignment: .topLeading) {
                TextEditor(text: $text)
                    .font(.sBody)
                    .scrollContentBackground(.hidden)
                    .focused($focused)
                    .disabled(sending)
                if text.isEmpty {
                    Text("Write a reply")
                        .font(.sBody)
                        .foregroundStyle(.tertiary)
                        .padding(.leading, 5)
                        .allowsHitTesting(false)
                }
            }
            .frame(height: 140)
            .padding(8)
            .background(Color(nsColor: .textBackgroundColor), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            .overlay {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .strokeBorder(focused ? Theme.accent.opacity(0.6) : Color.secondary.opacity(0.3), lineWidth: focused ? 2 : 1)
            }
            .animation(Motion.hover, value: focused)
            if let error {
                Label(error, systemImage: "exclamationmark.triangle.fill")
                    .font(.sCallout)
                    .foregroundStyle(.red)
            }
            HStack(spacing: 10) {
                Text("⌘↩ to post · esc to cancel")
                    .font(.sFootnote)
                    .foregroundStyle(.tertiary)
                Spacer(minLength: 8)
                Button("Cancel", action: cancel)
                    .keyboardShortcut(.cancelAction)
                    .glassButton()
                    .disabled(sending)
                Button(action: go) {
                    if sending {
                        ProgressView().controlSize(.small)
                    } else {
                        Text("Post")
                    }
                }
                .glassButton(prominent: true)
                .keyboardShortcut(.return, modifiers: .command)
                .disabled(sending || empty)
            }
            .controlSize(.large)
        }
        .task {
            try? await Task.sleep(nanoseconds: 50_000_000) // (in the window first, then the box takes the keys)
            focused = true
        }
    }

    private func go() {
        guard !empty, !sending else { return }
        sending = true
        error = nil
        Task {
            do {
                try await send(text)
            } catch {
                self.error = error.localizedDescription
            }
            sending = false
        }
    }
}

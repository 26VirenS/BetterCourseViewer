import SwiftUI

/// One announcement or discussion: the post, its files, and every reply in reading order, threaded by
/// depth — with Reply on the topic and on each reply, as Canvas allows.
struct TopicView: View {
    let ctx: String
    let id: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<TopicData>()
    @State private var replyTo: ReplyTarget?

    struct ReplyTarget: Identifiable {
        let id: String
        let parent: String?
        let quote: String
    }

    var body: some View {
        Group {
            if let d = model.data {
                List {
                    Section { post(d) }
                    if let files = d.attachments, !files.isEmpty {
                        Section("Attached") {
                            ForEach(files) { f in
                                Button { engine.openFile(f.url, name: f.name) } label: {
                                    InfoRow(title: f.name, symbol: "paperclip", tint: .blue)
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                    Section {
                        if d.locked == true, let t = d.lockText, !t.isEmpty {
                            Label(t, systemImage: "lock.fill").font(.subheadline).foregroundStyle(.secondary)
                        } else if d.needFirst == true {
                            Label("Reply to see what others wrote.", systemImage: "eye.slash").font(.subheadline).foregroundStyle(.secondary)
                        } else if d.entries.isEmpty {
                            Text(d.announcement == true ? "No replies." : "No replies yet. Be the first.").foregroundStyle(.secondary)
                        }
                        ForEach(d.entries) { e in entry(e, d) }
                    } header: {
                        HStack {
                            Text("Replies")
                            Spacer()
                            if let n = d.count, n > 0 { Text("\(n)").monospacedDigit() }
                        }
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await load() }
                .safeAreaInset(edge: .bottom) {
                    if d.canReply == true {
                        // the main action at the very bottom, as on an assignment (ActionButton, 1.4)
                        ActionBar {
                            ActionButton(title: "Reply", symbol: "arrowshape.turn.up.left.fill", tint: Color(hex: d.color)) {
                                replyTo = ReplyTarget(id: "topic", parent: nil, quote: d.title)
                            }
                        }
                    }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(model.data?.announcement == true ? "Announcement" : "Discussion")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    engine.openWebScreen("/\(ctx)/discussion_topics/\(id)?bcv=native", title: model.data?.title ?? "")
                } label: {
                    Image(systemName: "globe")
                }
                .accessibilityLabel("Open \(engine.lmsName)’s Page")
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .toolbar(.hidden, for: .tabBar) // (Reply takes the tab bar's place at the bottom)
        .sheet(item: $replyTo) { t in
            ReplySheet(title: t.parent == nil ? "Reply" : "Reply to \(t.quote)") { text in
                var args: [String: Any] = ["ctx": ctx, "id": id, "text": text]
                if let p = t.parent { args["parent"] = p }
                _ = try await engine.call("reply", args, as: OK.self)
                await load()
                engine.changed()
            }
        }
    }

    private func post(_ d: TopicData) -> some View {
        // (who posted it, or — when Canvas names nobody — the course, once, on its own colour)
        let who = (d.author ?? "").isEmpty ? nil : d.author
        return VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 10) {
                if let who {
                    PersonAvatar(name: who, avatar: d.avatar, size: 38)
                } else {
                    IconTile(symbol: d.announcement == true ? "megaphone.fill" : "bubble.left.and.bubble.right.fill", color: Color(hex: d.color), size: 38)
                }
                VStack(alignment: .leading, spacing: 1) {
                    Text(who ?? d.context ?? "").font(.subheadline.weight(.semibold))
                    Text([who == nil ? nil : d.context, d.when].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            Text(d.title).font(.title3.weight(.bold))
            if let g = d.graded, !g.isEmpty {
                Button { engine.go(d.assignmentUrl, title: d.title) } label: {
                    HStack(spacing: 6) {
                        StatusChip(text: "Graded", tone: "purple")
                        Text(g).font(.caption).foregroundStyle(.secondary)
                        if d.assignmentUrl != nil { Image(systemName: "chevron.right").font(.caption2).foregroundStyle(.tertiary) }
                    }
                }
                .buttonStyle(.plain)
                .disabled(d.assignmentUrl == nil)
            }
            if !d.html.isEmpty { RichText(html: d.html) }
        }
        .padding(.vertical, 6)
    }

    private func entry(_ e: Entry, _ d: TopicData) -> some View {
        HStack(alignment: .top, spacing: 10) {
            if e.depth > 0 {
                RoundedRectangle(cornerRadius: 1.5)
                    .fill(Color(hex: d.color).opacity(0.35))
                    .frame(width: 3)
                    .padding(.leading, CGFloat(e.depth - 1) * 12)
            }
            VStack(alignment: .leading, spacing: 6) {
                if e.deleted == true {
                    Text(e.text).font(.subheadline).italic().foregroundStyle(.secondary)
                } else {
                    HStack(spacing: 8) {
                        PersonAvatar(name: e.author, avatar: e.avatar, size: 26)
                        Text(e.author).font(.subheadline.weight(.semibold)).lineLimit(1)
                        Spacer(minLength: 4)
                        if let w = e.when { Text(w).font(.caption).foregroundStyle(.secondary).lineLimit(1) }
                    }
                    if e.rich == true, let h = e.html, !h.isEmpty {
                        RichText(html: h)
                    } else {
                        Text(e.text).font(.subheadline).textSelection(.enabled)
                    }
                    if d.canReply == true {
                        Button {
                            Haptics.tap()
                            replyTo = ReplyTarget(id: e.id, parent: e.id, quote: e.author)
                        } label: {
                            // (the arrow beside its word: a list's Label would set the word a column away)
                            HStack(spacing: 4) {
                                Image(systemName: "arrowshape.turn.up.left")
                                Text("Reply")
                            }
                            .font(.caption.weight(.medium))
                        }
                        .buttonStyle(.borderless)
                    }
                }
            }
            .separatorAtText()
        }
        .padding(.vertical, 4)
    }

    private func load() async { await model.load(engine, "topic", ["ctx": ctx, "id": id]) }
}

/// Writing a reply, a comment or a message: a text box with Send, the keyboard up at once; the sheet
/// stays until the words are sent, and says so if they could not be.
struct ReplySheet: View {
    let title: String
    var placeholder = "Write a reply"
    var action = "Post"
    let send: (String) async throws -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var text = ""
    @State private var sending = false
    @State private var error: String?
    @FocusState private var focused: Bool

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField(placeholder, text: $text, axis: .vertical)
                        .lineLimit(6...20)
                        .focused($focused)
                }
                if let error {
                    Section { Label(error, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red) }
                }
            }
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    if sending {
                        ProgressView()
                    } else {
                        Button(action) { go() }
                            .disabled(text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    }
                }
            }
            .interactiveDismissDisabled(sending || !text.isEmpty)
            .onAppear { focused = true }
        }
        .presentationDetents([.medium, .large])
        .presentationDragIndicator(.visible)
    }

    private func go() {
        sending = true
        error = nil
        Task {
            do {
                try await send(text)
                Haptics.success()
                dismiss()
            } catch {
                Haptics.error()
                self.error = error.localizedDescription
            }
            sending = false
        }
    }
}

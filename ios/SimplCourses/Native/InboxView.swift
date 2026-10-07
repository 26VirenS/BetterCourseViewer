import SwiftUI

/// Groups: the student's current groups and past ones; a group opens its own home.
struct GroupsView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<GroupsData>()

    var body: some View {
        Loaded(model: model, title: "Groups", load: load) { d in
            List {
                if !d.current.isEmpty { Section("Current") { ForEach(d.current) { g in row(g) } } }
                if !d.past.isEmpty { Section("Past") { ForEach(d.past) { g in row(g) } } }
            }
            .listStyle(.insetGrouped)
            .overlay { if d.current.isEmpty && d.past.isEmpty { EmptyNote(text: d.empty ?? "No groups", symbol: "person.3") } }
        }
    }

    private func row(_ g: GroupRow) -> some View {
        Button {
            Haptics.tap()
            engine.push(.group(id: g.id))
        } label: {
            InfoRow(title: g.name, sub: g.sub, symbol: "person.3.fill", tint: Color(hex: g.color)) {
                Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
            }
            .padding(.vertical, 2)
        }
        .buttonStyle(.plain)
    }

    private func load() async { await model.load(engine, "groups") }
}

/// The Inbox: a mailbox at a time (Inbox, Unread, Starred, Sent, Archived), each conversation with who,
/// when, its first words and its course; a swipe stars one; New Message writes one.
struct InboxView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<InboxData>()
    @State private var scope = "inbox"
    @State private var compose = false
    /// Another mailbox on its way: the one shown stays, dimmed, until the new one swaps in (no blank screen).
    @State private var switching = false

    struct Mailbox: Identifiable {
        let id: String
        let label: String
        let symbol: String
    }

    static let scopes = [
        Mailbox(id: "inbox", label: "Inbox", symbol: "tray"), Mailbox(id: "unread", label: "Unread", symbol: "envelope.badge"),
        Mailbox(id: "starred", label: "Starred", symbol: "star"), Mailbox(id: "sent", label: "Sent", symbol: "paperplane"),
        Mailbox(id: "archived", label: "Archived", symbol: "archivebox"),
    ]

    var body: some View {
        Loaded(model: model, title: InboxView.scopes.first(where: { $0.id == scope })?.label ?? "Inbox", load: load) { d in
            List {
                Section {
                    ForEach(d.rows) { c in
                        Button {
                            Haptics.tap()
                            engine.push(.conversation(id: c.id))
                        } label: {
                            row(c)
                        }
                        .buttonStyle(.plain)
                        .swipeActions(edge: .leading) {
                            Button { star(c, c.starred != true) } label: {
                                Label(c.starred == true ? "Unstar" : "Star", systemImage: c.starred == true ? "star.slash" : "star.fill")
                            }
                            .tint(.yellow)
                        }
                    }
                }
                .listSectionSeparator(.hidden, edges: .top) // (no rule over the first message, as in Mail)
            }
            .listStyle(.plain)
            .opacity(switching ? 0.5 : 1)
            .animation(.easeOut(duration: 0.15), value: switching)
            .overlay { if d.rows.isEmpty { EmptyNote(text: d.empty ?? "No messages", symbol: "tray") } }
        }
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Picker("Mailbox", selection: $scope) {
                        ForEach(InboxView.scopes) { s in Label(s.label, systemImage: s.symbol).tag(s.id) }
                    }
                } label: {
                    Image(systemName: "line.3.horizontal.decrease.circle")
                }
                .accessibilityLabel("Mailbox")
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button { compose = true } label: { Image(systemName: "square.and.pencil") }
                    .accessibilityLabel("New Message")
            }
        }
        .onChange(of: scope) {
            Haptics.select()
            switching = true
            Task {
                await model.load(engine, "inbox", ["scope": scope], animated: true)
                switching = false
            }
        }
        .sheet(isPresented: $compose) {
            ComposeSheet(sent: { Task { await load() } })
                .environmentObject(engine)
        }
    }

    private func row(_ c: ConvRow) -> some View {
        HStack(alignment: .top, spacing: 10) {
            Circle()
                .fill(c.unread == true ? Color.accentColor : .clear)
                .frame(width: 9, height: 9)
                .padding(.top, 6)
                .accessibilityLabel(c.unread == true ? "Unread" : "")
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline) {
                    Text(c.who).font(c.unread == true ? .body.weight(.semibold) : .body).lineLimit(1)
                    if let n = c.count, n > 1 { Text("\(n)").font(.caption).foregroundStyle(.secondary) }
                    Spacer(minLength: 6)
                    if c.starred == true {
                        Image(systemName: "star.fill").font(.caption).foregroundStyle(.yellow)
                            .transition(.scale(scale: 0.5).combined(with: .opacity))
                    }
                    if c.attachment == true { Image(systemName: "paperclip").font(.caption).foregroundStyle(.secondary) }
                    if let w = c.when { Text(w).font(.caption).foregroundStyle(.secondary).lineLimit(1) }
                }
                Text(c.subject).font(.subheadline.weight(c.unread == true ? .semibold : .regular)).lineLimit(1)
                if let p = c.preview, !p.isEmpty { Text(p).font(.subheadline).foregroundStyle(.secondary).lineLimit(2) }
                if let ctx = c.context, !ctx.isEmpty { Text(ctx).font(.caption).foregroundStyle(.tertiary).lineLimit(1) }
            }
            .separatorAtText()
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
    }

    /// Starred at once (the star shows before Canvas answers), and back if Canvas says no.
    private func star(_ c: ConvRow, _ on: Bool) {
        Haptics.select()
        setStar(c.id, on)
        Task {
            if await engine.act("star", ["id": c.id, "on": on]) { await model.load(engine, "inbox", ["scope": scope], animated: true) }
            else { setStar(c.id, !on) }
        }
    }

    private func setStar(_ id: String, _ on: Bool) {
        guard let i = model.data?.rows.firstIndex(where: { $0.id == id }) else { return }
        withAnimation(.snappy) { model.data?.rows[i].starred = on }
    }

    private func load() async { await model.load(engine, "inbox", ["scope": scope]) }
}

/// One conversation as a thread of bubbles (yours on the right), with a reply box under it.
struct ConversationView: View {
    let id: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<ConversationData>()
    @State private var draft = ""
    @State private var sending = false
    @State private var error: String?
    @FocusState private var typing: Bool

    var body: some View {
        Group {
            if let d = model.data {
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 12) {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(d.subject).font(.title3.weight(.bold))
                            if let p = d.people, !p.isEmpty { Text(p).font(.footnote).foregroundStyle(.secondary) }
                            if let c = d.context, !c.isEmpty { Text(c).font(.caption).foregroundStyle(.tertiary) }
                        }
                        .padding(.bottom, 6)
                        ForEach(d.messages) { m in
                            bubble(m).transition(.move(edge: .bottom).combined(with: .opacity)) // (a reply sent rises in at the foot)
                        }
                    }
                    .padding(.horizontal, 16)
                    .padding(.vertical, 12)
                }
                .defaultScrollAnchor(.bottom)
                .defaultScrollAnchor(.bottom, for: .sizeChanges) // (a new message keeps the foot in view)
                .scrollDismissesKeyboard(.interactively)
                .refreshable { await load() }
                .safeAreaInset(edge: .bottom) { replyBar }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("Message")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if let d = model.data {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        Haptics.select()
                        let on = d.starred != true
                        withAnimation(.snappy) { model.data?.starred = on }
                        Task {
                            if await engine.act("star", ["id": id, "on": on]) { await load() }
                            else { withAnimation(.snappy) { model.data?.starred = !on } }
                        }
                    } label: {
                        Image(systemName: d.starred == true ? "star.fill" : "star")
                            .contentTransition(.symbolEffect(.replace))
                    }
                    .accessibilityLabel(d.starred == true ? "Unstar" : "Star")
                }
            }
        }
        .task { await load() }
    }

    private func bubble(_ m: Message) -> some View {
        HStack(alignment: .bottom, spacing: 8) {
            if m.mine { Spacer(minLength: 40) } else { PersonAvatar(name: m.author, avatar: m.avatar, size: 28) }
            VStack(alignment: m.mine ? .trailing : .leading, spacing: 3) {
                if !m.mine { Text(m.author).font(.caption.weight(.semibold)).foregroundStyle(.secondary) }
                if !m.body.isEmpty {
                    Text(m.body)
                        .textSelection(.enabled)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 8)
                        .foregroundStyle(m.mine ? Color.white : Color.primary)
                        .background(m.mine ? AnyShapeStyle(Color.accentColor) : AnyShapeStyle(Color(.secondarySystemBackground)), in: RoundedRectangle(cornerRadius: 18, style: .continuous))
                }
                ForEach(m.attachments ?? []) { f in
                    Button { engine.openFile(f.url, name: f.name) } label: {
                        Label(f.name, systemImage: "paperclip").font(.footnote).lineLimit(1)
                            .padding(.horizontal, 10).padding(.vertical, 6)
                            .background(Color(.tertiarySystemBackground), in: Capsule())
                    }
                    .buttonStyle(.plain)
                }
                if let w = m.when { Text(w).font(.caption2).foregroundStyle(.tertiary) }
            }
            if !m.mine { Spacer(minLength: 40) }
        }
        .frame(maxWidth: .infinity, alignment: m.mine ? .trailing : .leading)
    }

    private var replyBar: some View {
        VStack(spacing: 4) {
            if let error { Text(error).font(.caption).foregroundStyle(.red) }
            HStack(alignment: .bottom, spacing: 8) {
                TextField("Reply", text: $draft, axis: .vertical)
                    .lineLimit(1...6)
                    .focused($typing)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 9)
                    .background(Color(.secondarySystemBackground), in: RoundedRectangle(cornerRadius: 20, style: .continuous))
                Button { send() } label: {
                    ZStack {
                        if sending { ProgressView().frame(width: 34, height: 34).transition(.opacity) }
                        else { Image(systemName: "arrow.up.circle.fill").font(.system(size: 32)).transition(.scale(scale: 0.6).combined(with: .opacity)) }
                    }
                    .animation(.snappy(duration: 0.2), value: sending)
                }
                .disabled(sending || draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                .accessibilityLabel("Send")
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(.bar)
    }

    private func send() {
        let text = draft
        sending = true
        error = nil
        Task {
            do {
                _ = try await engine.call("sendReply", ["id": id, "body": text], as: OK.self)
                Haptics.success()
                draft = ""
                await model.load(engine, "conversation", ["id": id], animated: true)
                engine.changed()
            } catch {
                Haptics.error()
                self.error = error.localizedDescription
            }
            sending = false
        }
    }

    private func load() async { await model.load(engine, "conversation", ["id": id]) }
}

/// New Message: a course (optional, it narrows the people found), who it is to (found as you type),
/// a subject and the message.
struct ComposeSheet: View {
    var to: [Recipient] = []
    var context: String? = nil
    var sent: () -> Void = {}
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var people: [Recipient] = []
    @State private var course: String?
    @State private var contexts: [ComposeContext] = []
    @State private var query = ""
    @State private var found: [Recipient] = []
    @State private var subject = ""
    @State private var message = ""
    @State private var sending = false
    @State private var error: String?
    @State private var started = false

    var body: some View {
        NavigationStack {
            Form {
                if !contexts.isEmpty {
                    Section {
                        Picker("Course", selection: $course) {
                            Text("Any").tag(String?.none)
                            ForEach(contexts) { c in Text(c.name).tag(Optional(c.code)) }
                        }
                    }
                }
                Section {
                    if !people.isEmpty {
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 6) {
                                ForEach(people) { p in
                                    Button {
                                        Haptics.select()
                                        people.removeAll { $0.id == p.id }
                                    } label: {
                                        HStack(spacing: 4) {
                                            Text(p.name).lineLimit(1)
                                            Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary)
                                        }
                                        .font(.subheadline)
                                        .padding(.horizontal, 10)
                                        .padding(.vertical, 5)
                                        .background(Color.accentColor.opacity(0.14), in: Capsule())
                                    }
                                    .buttonStyle(.plain)
                                    .accessibilityLabel("Remove \(p.name)")
                                }
                            }
                        }
                    }
                    TextField("Search people", text: $query)
                        .textInputAutocapitalization(.words)
                        .autocorrectionDisabled()
                    ForEach(found.filter { f in !people.contains { $0.id == f.id } }) { r in
                        Button {
                            Haptics.select()
                            people.append(r)
                            query = ""
                        } label: {
                            HStack(spacing: 10) {
                                PersonAvatar(name: r.name, size: 28)
                                VStack(alignment: .leading, spacing: 1) {
                                    Text(r.name).foregroundStyle(.primary)
                                    if let s = r.sub, !s.isEmpty { Text(s).font(.caption).foregroundStyle(.secondary).lineLimit(1) }
                                }
                                .separatorAtText()
                                Spacer()
                                Image(systemName: "plus.circle").foregroundStyle(.tint)
                            }
                        }
                    }
                } header: {
                    Text("To")
                }
                Section {
                    TextField("Subject", text: $subject)
                    TextField("Message", text: $message, axis: .vertical).lineLimit(6...20)
                }
                if let error {
                    Section { Label(error, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red) }
                }
            }
            .navigationTitle("New Message")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() }.disabled(sending) }
                ToolbarItem(placement: .confirmationAction) {
                    if sending { ProgressView() } else {
                        Button("Send") { send() }
                            .disabled(people.isEmpty || message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    }
                }
            }
            .interactiveDismissDisabled(sending || !message.isEmpty)
            .task {
                guard !started else { return }
                started = true
                people = to
                course = context
                if let c = try? await engine.call("composeContexts", as: ComposeContextsData.self) { contexts = c.rows }
            }
            .task(id: "\(query)|\(course ?? "")") { await search() }
        }
        .presentationDragIndicator(.visible)
    }

    private func search() async {
        let q = query.trimmingCharacters(in: .whitespaces)
        guard q.count >= 2 else { found = []; return }
        try? await Task.sleep(nanoseconds: 300_000_000)
        guard !Task.isCancelled else { return }
        var args: [String: Any] = ["q": q]
        if let course { args["context"] = course }
        if let r = try? await engine.call("recipients", args, as: RecipientsData.self), !Task.isCancelled { found = r.rows }
    }

    private func send() {
        sending = true
        error = nil
        var args: [String: Any] = ["recipients": people.map(\.id), "subject": subject, "body": message]
        if let course { args["context"] = course }
        Task {
            do {
                _ = try await engine.call("sendMessage", args, as: OK.self)
                Haptics.success()
                sent()
                engine.changed()
                dismiss()
            } catch {
                Haptics.error()
                self.error = error.localizedDescription
            }
            sending = false
        }
    }
}

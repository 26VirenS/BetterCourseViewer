import SwiftUI

// MARK: - New Message

/// New Message, as Mail's own window for one: who it is to (found as you type, in a glass panel under the field — the
/// arrow keys and Return choose, Escape puts it away — and each one a token that can be taken out again), the course it
/// is about (optional — it narrows the people found to those in it), a subject and the message, each a line of its own
/// with a hairline under it and no boxes; Cancel and Send in glass at the foot. ⌘Return sends; Escape cancels, asking
/// first when something is written.
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
    /// The person found that Return adds (the arrow keys and the pointer move it).
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
                .font(.sTitle2)
                .padding(.horizontal, ComposeMetrics.side)
                .padding(.top, 20)
                .padding(.bottom, 14)
            Divider()
            fields
                .zIndex(1) // (the people found float over the message under them)
            messageEditor
            Divider()
            footer
        }
        .frame(minWidth: 620, idealWidth: 720, minHeight: 540, idealHeight: 660)
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

    /// To, Course and Subject, each a line with a hairline under it; the people found float under To.
    private var fields: some View {
        VStack(alignment: .leading, spacing: 0) {
            recipientsRow
                .overlay(alignment: .bottomLeading) { suggestionPanel }
                .zIndex(1)
            Divider()
                .padding(.leading, ComposeMetrics.side)
            if !contexts.isEmpty || context != nil {
                courseRow
                Divider()
                    .padding(.leading, ComposeMetrics.side)
            }
            subjectRow
            Divider()
                .padding(.leading, ComposeMetrics.side)
        }
    }

    /// A line's name at its start, as Mail's are.
    private func fieldLabel(_ text: String) -> some View {
        Text(text)
            .font(.sBody)
            .foregroundStyle(.secondary)
            .frame(width: ComposeMetrics.label, alignment: .trailing)
    }

    /// Who it is to: a token for each person chosen, and the field that finds more.
    private var recipientsRow: some View {
        HStack(alignment: .top, spacing: ComposeMetrics.gap) {
            fieldLabel("To:")
                .padding(.top, people.isEmpty ? 0 : 4)
            VStack(alignment: .leading, spacing: 8) {
                if !people.isEmpty {
                    InboxFlow(spacing: 6) {
                        ForEach(people) { p in token(p) }
                    }
                }
                TextField("To", text: $query, prompt: Text(people.isEmpty ? "Search for a person or a course" : "Add someone else"))
                    .textFieldStyle(.plain)
                    .font(.sBody)
                    .labelsHidden()
                    .focused($focus, equals: .to)
                    .onSubmit { takeHighlighted() }
                    .onKeyPress(.downArrow) { move(1) }
                    .onKeyPress(.upArrow) { move(-1) }
                    .onKeyPress(.delete) { removeLast() }
                    .onKeyPress(.escape) { putAwayFound() }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, ComposeMetrics.side)
        .padding(.vertical, 12)
    }

    /// The course a message is about: any, or one of the courses (it narrows the people found).
    private var courseRow: some View {
        HStack(spacing: ComposeMetrics.gap) {
            fieldLabel("Course:")
            Picker("Course", selection: $course) {
                Text("Any Course").tag(String?.none)
                if let c = context, !contexts.contains(where: { $0.code == c }) {
                    Text("This Course").tag(Optional(c))
                }
                ForEach(contexts) { c in
                    Text(c.name).tag(Optional(c.code))
                }
            }
            .labelsHidden()
            .pickerStyle(.menu)
            .fixedSize()
            Spacer(minLength: 0)
        }
        .padding(.horizontal, ComposeMetrics.side)
        .padding(.vertical, 9)
    }

    private var subjectRow: some View {
        HStack(alignment: .firstTextBaseline, spacing: ComposeMetrics.gap) {
            fieldLabel("Subject:")
            TextField("Subject", text: $subject, prompt: Text("Optional"))
                .textFieldStyle(.plain)
                .font(.sBody)
                .labelsHidden()
                .focused($focus, equals: .subject)
                .onSubmit { focus = .message }
        }
        .padding(.horizontal, ComposeMetrics.side)
        .padding(.vertical, 12)
    }

    /// A person chosen: their name, and the button that takes them out again.
    private func token(_ p: Recipient) -> some View {
        HStack(spacing: 4) {
            if p.id.contains("_") {
                Image(systemName: "person.3.fill")
                    .font(.sCaption)
                    .foregroundStyle(.tint)
            }
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
        .font(.sCallout.weight(.medium))
        .padding(.leading, 10)
        .padding(.trailing, 6)
        .padding(.vertical, 4)
        .background(Theme.accent.opacity(0.14), in: Capsule())
        .contextMenu {
            Button("Remove") { remove(p) }
        }
        .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.scale(scale: 0.9).combined(with: .opacity))
    }

    /// (1.2) The people found, in a glass panel floating just under the To field (over what is below it).
    @ViewBuilder
    private var suggestionPanel: some View {
        let list = suggestions
        if !list.isEmpty {
            VStack(alignment: .leading, spacing: 0) {
                ForEach(Array(list.enumerated()), id: \.element.id) { i, r in
                    if i > 0 { RowDivider(inset: 50) }
                    suggestionRow(r, index: i)
                }
            }
            .padding(6)
            .frame(maxWidth: 480, alignment: .leading)
            .fixedSize(horizontal: false, vertical: true)
            .glassCard(radius: 16)
            .shadow(color: Theme.shadow, radius: 14, y: 6)
            .padding(.leading, ComposeMetrics.side + ComposeMetrics.label + ComposeMetrics.gap - 8)
            .padding(.trailing, ComposeMetrics.side)
            .padding(.top, 2)
            .alignmentGuide(.bottom) { d in d[.top] }
            .transition(.opacity)
        }
    }

    /// Someone found: a click (or Return while it is marked) adds them; the pointer marks it.
    private func suggestionRow(_ r: Recipient, index: Int) -> some View {
        let on = index == highlighted
        return Button {
            add(r)
        } label: {
            HStack(spacing: 10) {
                // (a course, a group or a section reaches many people: its own symbol, not initials)
                if r.id.contains("_") {
                    IconTile(symbol: "person.3.fill", color: Theme.accent, size: 30)
                } else {
                    InboxAvatar(name: r.name, size: 30)
                }
                VStack(alignment: .leading, spacing: 1) {
                    Text(r.name)
                        .font(.sBody)
                        .foregroundStyle(.primary)
                        .lineLimit(1)
                    if let s = r.sub, !s.isEmpty {
                        Text(s)
                            .font(.sFootnote)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
                Spacer(minLength: 8)
                Image(systemName: "plus.circle")
                    .font(.sBody)
                    .foregroundStyle(.tint)
                    .opacity(on ? 1 : 0.5)
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 6)
            .frame(maxWidth: .infinity, alignment: .leading)
            .contentShape(Rectangle())
            .background(on ? Theme.accent.opacity(0.14) : Color.clear, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
        .buttonStyle(.plain)
        .onHover { inside in
            if inside { highlighted = index }
        }
        .accessibilityLabel("Add \(r.name)")
    }

    private var messageEditor: some View {
        TextEditor(text: $message)
            .font(.sBody)
            .scrollContentBackground(.hidden)
            .focusEffectDisabled()
            .focused($focus, equals: .message)
            .overlay(alignment: .topLeading) {
                if message.isEmpty {
                    Text("Write your message")
                        .font(.sBody)
                        .foregroundStyle(.tertiary)
                        .padding(.leading, 5)
                        .allowsHitTesting(false)
                }
            }
            .padding(.horizontal, ComposeMetrics.side - 5) // (the editor's own inset makes up the rest)
            .padding(.vertical, 14)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .accessibilityLabel("Message")
    }

    /// Cancel and Send in glass at the sheet's foot; while it goes, a word that it is going; why it did not, if not.
    private var footer: some View {
        HStack(spacing: 12) {
            status
            Spacer(minLength: 12)
            GlassGroup(spacing: 6) {
                HStack(spacing: 10) {
                    Button("Cancel") { cancel() }
                        .glassButton()
                        .keyboardShortcut(.cancelAction)
                        .disabled(sending)
                    Button {
                        send()
                    } label: {
                        Label("Send", systemImage: "paperplane.fill")
                    }
                    .glassButton(prominent: true)
                    .keyboardShortcut(.return, modifiers: .command)
                    .disabled(!canSend)
                    .help("Send (⌘Return)")
                }
            }
            .controlSize(.large)
        }
        .padding(.horizontal, ComposeMetrics.side)
        .padding(.vertical, 14)
    }

    @ViewBuilder
    private var status: some View {
        if sending {
            HStack(spacing: 8) {
                ProgressView()
                    .controlSize(.small)
                Text("Sending…")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        } else if let error {
            Label(error, systemImage: "exclamationmark.triangle.fill")
                .font(.sCallout)
                .foregroundStyle(.red)
                .lineLimit(2)
                .transition(.opacity)
        }
    }

    private var firstFocus: ComposeFocus { to.isEmpty ? .to : .subject }

    private var canSend: Bool {
        !sending && !people.isEmpty && !message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    /// The people found and not yet added: the first six.
    private var suggestions: [Recipient] {
        Array(found.filter { f in !people.contains { $0.id == f.id } }.prefix(6))
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
            withAnimation(Motion.snappy) {
                found = r.rows
                highlighted = 0
            }
        }
    }

    private func add(_ r: Recipient) {
        withAnimation(Motion.snappy) {
            if !people.contains(where: { $0.id == r.id }) { people.append(r) }
            found = []
        }
        query = ""
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

    /// Escape with people found showing puts them away (the sheet stays); else it is the sheet's own Escape.
    private func putAwayFound() -> KeyPress.Result {
        guard !suggestions.isEmpty else { return .ignored }
        withAnimation(Motion.snappy) { found = [] }
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

/// New Message's measures: its sides, the width of a line's name, the space after it.
private enum ComposeMetrics {
    static let side: CGFloat = 24
    static let label: CGFloat = 70
    static let gap: CGFloat = 12
}

import PhotosUI
import SwiftUI
import UniformTypeIdentifiers

/// One assignment: its due date, points and kind, the instructions, where your work stands, what you
/// handed in, the grade with the class's numbers, the comments — the rubric in a sheet of its own from a
/// button by the grade — and Hand In at the very bottom where the tab bar was (hidden here, 1.4), opening a
/// half sheet for text, a web address or files (from Files or Photos), as the assignment takes them. A quiz
/// opens in the app's own quiz screen, a tool in its sheet, a discussion in its screen.
struct AssignmentView: View {
    let course: String
    let id: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<AssignmentData>()
    @State private var handIn = false
    @State private var comment = false
    @State private var rubric = false
    @State private var jump: String?
    /// Counts the hand-ins made here: each one bounces the Submitted seal once it is in view.
    @State private var handedIn = 0

    var body: some View {
        Group {
            if let d = model.data {
                ScrollViewReader { proxy in
                List {
                    Section { header(d) }
                    if let g = d.grade { Section("Grade") { gradeBlock(g, d) }.id("grade") }
                    else if d.held == true {
                        Section { Label("Graded, but your teacher has not released the grade yet.", systemImage: "eye.slash").foregroundStyle(.secondary) }
                    }
                    if d.grade == nil && !d.rubric.isEmpty {
                        Section {
                            Button { openRubric() } label: {
                                InfoRow(title: d.rubricTitle ?? "Rubric", sub: "\(d.rubric.count) criteria · how this is marked", symbol: "list.bullet.clipboard", tint: Color(hex: d.color)) {
                                    Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
                                }
                            }
                            .buttonStyle(.plain)
                        }
                    }
                    if !d.html.isEmpty {
                        Section("Instructions") { RichText(html: d.html).padding(.vertical, 4) }
                    }
                    workSection(d)
                    Section {
                        if d.comments.isEmpty { Text("No comments.").foregroundStyle(.secondary) }
                        ForEach(d.comments) { c in commentRow(c) }
                        Button { comment = true } label: { Label("Add a Comment", systemImage: "text.bubble") }
                    } header: {
                        Text("Comments")
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await load() }
                .safeAreaInset(edge: .bottom) { action(d) }
                .onChange(of: jump) {
                    guard let j = jump else { return }
                    jump = nil
                    withAnimation(.smooth(duration: 0.4)) { proxy.scrollTo(j, anchor: .top) }
                }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(model.data?.context ?? "Assignment")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    engine.openWebScreen(model.data?.canvasUrl ?? "/courses/\(course)/assignments/\(id)?bcv=native", title: model.data?.title ?? "")
                } label: {
                    Image(systemName: "globe")
                }
                .accessibilityLabel("Open \(engine.lmsName)’s Page")
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .toolbar(.hidden, for: .tabBar) // (the main action takes the tab bar's place at the bottom)
        .sheet(isPresented: $rubric) {
            if let d = model.data { RubricSheet(data: d) }
        }
        .sheet(isPresented: $handIn) {
            if let d = model.data {
                SubmitSheet(data: d) {
                    // the sheet away first, then the work arrives where it can be seen: the page goes to it and its seal bounces
                    try? await Task.sleep(nanoseconds: 350_000_000)
                    await model.load(engine, "assignment", ["course": course, "id": id], animated: true)
                    jump = "work"
                    try? await Task.sleep(nanoseconds: 300_000_000)
                    handedIn += 1
                    engine.changed()
                }
                .environmentObject(engine)
            }
        }
        .sheet(isPresented: $comment) {
            ReplySheet(title: "Comment", placeholder: "Write a comment for your teacher", action: "Send") { text in
                _ = try await engine.call("commentOn", ["course": course, "id": id, "text": text], as: OK.self)
                await load()
            }
        }
    }

    // MARK: - Parts

    private func header(_ d: AssignmentData) -> some View {
        let color = Color(hex: d.color)
        return VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                IconTile(symbol: Glyph.item(d.kind ?? "assignment"), color: color, size: 26)
                Text(d.kind ?? "Assignment").font(.subheadline.weight(.semibold)).foregroundStyle(color)
                Spacer()
                if let s = d.status, !s.word.isEmpty { StatusChip(text: s.word, tone: s.kind) }
            }
            Text(d.title).font(.title3.weight(.bold))
            VStack(alignment: .leading, spacing: 6) {
                if let due = d.due, !due.isEmpty { fact("calendar", "Due \(due)") } else { fact("calendar", "No due date") }
                if let p = d.points, !p.isEmpty { fact("star", p) }
                if let a = d.available, !a.isEmpty { fact("clock", a) }
                if let t = d.typesText, !t.isEmpty { fact("tray.and.arrow.up", t) }
                if let at = d.attemptsText, !at.isEmpty { fact("arrow.counterclockwise", at) }
            }
        }
        .padding(.vertical, 4)
    }

    private func fact(_ symbol: String, _ text: String) -> some View {
        Fact(symbol: symbol, text: text)
    }

    private func gradeBlock(_ g: GradeInfo, _ d: AssignmentData) -> some View {
        let color = Color(hex: d.color)
        return HStack(spacing: 16) {
            ZStack {
                Ring(value: g.pct, color: color, lineWidth: 6, key: "assignment:\(course)/\(id)")
                // (a complete / incomplete grade as a tick or a cross; any other word on one line, made to fit)
                switch (g.letter ?? "").lowercased() {
                case "complete", "pass": Image(systemName: "checkmark").font(.title3.weight(.bold)).foregroundStyle(color)
                case "incomplete", "fail": Image(systemName: "xmark").font(.title3.weight(.bold)).foregroundStyle(.secondary)
                default: Text(g.letter ?? (g.pct.map { "\(Int($0.rounded()))%" } ?? "")).font(.headline.weight(.bold)).foregroundStyle(color).lineLimit(1).minimumScaleFactor(0.4).padding(.horizontal, 6)
                }
            }
            .frame(width: 64, height: 64)
            VStack(alignment: .leading, spacing: 3) {
                if let s = g.score, let p = g.possible, p > 0 {
                    Text("\(CourseGradesView.num(s)) / \(CourseGradesView.num(p))").font(.title3.weight(.semibold).monospacedDigit())
                } else {
                    Text(g.text).font(.title3.weight(.semibold))
                }
                if let pct = g.pct { Text(String(format: "%.1f%%", pct)).font(.subheadline).foregroundStyle(.secondary) }
                if let late = g.late, !late.isEmpty { Text(late).font(.caption).foregroundStyle(.orange) }
                if let st = d.stats, !st.isEmpty { Text(st).font(.caption).foregroundStyle(.secondary) }
            }
            Spacer(minLength: 4)
            if !d.rubric.isEmpty {
                // the rubric beside the grade it explains, in its own sheet
                Button { openRubric() } label: {
                    VStack(spacing: 3) {
                        Image(systemName: "list.bullet.clipboard").font(.title3)
                        Text(d.rubricScore?.isEmpty == false ? d.rubricScore! : "Rubric").font(.caption2.weight(.semibold)).monospacedDigit()
                    }
                    .foregroundStyle(color)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 8)
                    .innerFill(color.opacity(0.14), radius: 12, minimum: 10) // (concentric with the grade's cell)
                }
                .buttonStyle(.borderless)
                .accessibilityLabel("Rubric")
            }
        }
        .padding(.vertical, 4)
    }

    private func openRubric() {
        Haptics.tap()
        rubric = true
    }

    @ViewBuilder
    private func workSection(_ d: AssignmentData) -> some View {
        let s = d.submission
        let hasWork = !(s?.files ?? []).isEmpty || !(s?.url ?? "").isEmpty || !(s?.text ?? "").isEmpty
        if (d.submitted ?? "").isEmpty == false || hasWork || (d.why ?? "").isEmpty == false {
            Section("Your work") {
                if let sub = d.submitted, !sub.isEmpty {
                    Label(sub, systemImage: "checkmark.seal.fill").foregroundStyle(.green)
                        .symbolEffect(.bounce, value: handedIn)
                }
                if let t = s?.text, !t.isEmpty { Text(t).font(.subheadline).lineLimit(8) }
                if let u = s?.url, !u.isEmpty {
                    Button { if let x = URL(string: u) { engine.openLink(x) } } label: { Label(u, systemImage: "link").lineLimit(1) }
                }
                ForEach(s?.files ?? []) { f in
                    Button { engine.openFile(f.url, name: f.name) } label: { InfoRow(title: f.name, symbol: "doc.fill", tint: .blue) }
                        .buttonStyle(.plain)
                }
                if let why = d.why, !why.isEmpty { Label(why, systemImage: "info.circle").font(.subheadline).foregroundStyle(.secondary) }
            }
            .id("work")
        }
    }

    private func commentRow(_ c: CommentRow) -> some View {
        HStack(alignment: .top, spacing: 10) {
            PersonAvatar(name: c.author, avatar: c.avatar, size: 30)
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text(c.author).font(.subheadline.weight(.semibold)).lineLimit(1)
                    Spacer()
                    if let w = c.when { Text(w).font(.caption).foregroundStyle(.secondary) }
                }
                if !c.text.isEmpty { Text(c.text).font(.subheadline).textSelection(.enabled) }
                if let a = c.attempt, a > 0 { Text("Attempt \(a)").font(.caption2).foregroundStyle(.tertiary) }
                ForEach(c.attachments ?? []) { f in
                    Button { engine.openFile(f.url, name: f.name) } label: {
                        HStack(spacing: 4) {
                            Image(systemName: "paperclip")
                            Text(f.name).lineLimit(1)
                        }
                        .font(.caption)
                    }
                    .buttonStyle(.borderless)
                }
            }
            .separatorAtText()
        }
        .padding(.vertical, 3)
    }

    /// The one thing to do next, at the very bottom where the tab bar was: hand in (again), take the quiz, open
    /// the tool or the discussion — the course's colour, concentric with the phone's corners (ActionButton).
    @ViewBuilder
    private func action(_ d: AssignmentData) -> some View {
        if let label = actionLabel(d) {
            ActionBar {
                ActionButton(title: label.0, symbol: label.1, tint: Color(hex: d.color)) { act(d) }
            }
        }
    }

    private func act(_ d: AssignmentData) {
        if d.canSubmit { handIn = true }
        else if let q = d.quizId { engine.openQuiz(QuizLaunch(course: course, quiz: q, title: d.title)) }
        else if let q = d.quizUrl { engine.go(q, title: d.title) }
        else if let u = d.discussionUrl { engine.go(u, title: d.title) }
        else if d.toolUrl != nil { engine.openTool(.assignment(course: course, id: id, title: d.title)) }
    }

    /// Arrived from a swipe in a list: its Hand In done at once, or its feedback shown (a quiz's in the quiz screen).
    private func arrived(_ d: AssignmentData) {
        guard let a = engine.arrival, a.id == id else { return }
        engine.arrival = nil
        if a.feedback {
            if let q = d.quizId { engine.openQuiz(QuizLaunch(course: course, quiz: q, title: d.title, feedback: true)) }
            else if d.grade != nil { jump = "grade" }
        } else if actionLabel(d) != nil {
            act(d)
        }
    }

    private func actionLabel(_ d: AssignmentData) -> (String, String)? {
        if d.canSubmit { return (d.resubmit == true ? "Hand In Again" : "Hand In", "tray.and.arrow.up.fill") }
        if d.quizUrl != nil { return ("Take Quiz", "checklist") }
        if d.discussionUrl != nil { return ("Open Discussion", "bubble.left.and.bubble.right.fill") }
        if d.toolUrl != nil { return d.ltiQuiz == true ? ("Take Quiz", "checklist") : ("Open Tool", "puzzlepiece.extension.fill") }
        return nil
    }

    private func load() async {
        await model.load(engine, "assignment", ["course": course, "id": id])
        if model.data?.canSubmit == true, LaunchOpen.take("handin") != nil { handIn = true }
        if let d = model.data { arrived(d) }
    }
}

/// A file picked to hand in, read into memory for the page's upload.
struct PickedFile: Identifiable, Hashable {
    let id = UUID()
    let name: String
    let type: String
    let data: Data

    var sizeText: String { ByteCountFormatter.string(fromByteCount: Int64(data.count), countStyle: .file) }
}

/// Hand In: the kinds this assignment takes from the phone (text, a web address, files), an optional
/// comment, and Submit. Files come from the Files app or Photos; the assignment's allowed types are
/// checked before anything is sent.
struct SubmitSheet: View {
    let data: AssignmentData
    let done: () async -> Void
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var type = ""
    @State private var text = ""
    @State private var url = ""
    @State private var files: [PickedFile] = []
    @State private var note = ""
    @State private var importing = false
    @State private var photos: [PhotosPickerItem] = []
    @State private var sending = false
    @State private var error: String?

    private static let limit = 50 * 1024 * 1024 // (read whole into memory and handed to the page: kept to a sane size)

    var body: some View {
        NavigationStack {
            Form {
                if data.types.count > 1 {
                    Section {
                        Picker("Hand in", selection: $type.animation(.snappy)) { // (the kind's own fields swap in place, not at once)
                            ForEach(data.types, id: \.self) { t in Text(SubmitSheet.label(t)).tag(t) }
                        }
                        .pickerStyle(.segmented)
                    }
                }
                switch type {
                case "online_text_entry":
                    Section("Your text") {
                        TextField("Write your answer", text: $text, axis: .vertical).lineLimit(8...30)
                    }
                case "online_url":
                    Section {
                        TextField("https://", text: $url)
                            .keyboardType(.URL)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                    } header: {
                        Text("Website address")
                    }
                case "online_upload":
                    filesSection
                default:
                    EmptyView()
                }
                Section("Comment (optional)") {
                    TextField("A note for your teacher", text: $note, axis: .vertical).lineLimit(2...6)
                }
                if let error {
                    Section { Label(error, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red) }
                }
            }
            .navigationTitle(data.resubmit == true ? "Hand In Again" : "Hand In")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() }.disabled(sending) }
                ToolbarItem(placement: .confirmationAction) {
                    if sending { ProgressView() } else { Button("Submit") { submit() }.disabled(!ready) }
                }
            }
            .interactiveDismissDisabled(sending || dirty)
            .fileImporter(isPresented: $importing, allowedContentTypes: allowedTypes, allowsMultipleSelection: true) { result in
                pickedFiles(result)
            }
            .onChange(of: photos) { pickedPhotos() }
            .onAppear { if type.isEmpty { type = data.types.first ?? "" } }
        }
        .presentationDetents([.fraction(0.5), .large]) // (a small sheet, half the screen; drag it up for more room)
        .presentationDragIndicator(.visible)
    }

    private var filesSection: some View {
        Section {
            ForEach(files) { f in
                HStack {
                    Image(systemName: "doc.fill").foregroundStyle(.blue)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(f.name).lineLimit(1)
                        Text(f.sizeText).font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
            .onDelete { files.remove(atOffsets: $0) }
            Button { importing = true } label: { Label("Choose from Files", systemImage: "folder") }
            PhotosPicker(selection: $photos, maxSelectionCount: 10, matching: .any(of: [.images, .videos])) {
                Label("Choose from Photos", systemImage: "photo.on.rectangle")
            }
        } header: {
            Text("Files")
        } footer: {
            if let a = data.allowed, !a.isEmpty { Text("This assignment takes \(a.map { ".\($0)" }.joined(separator: ", ")) files.") }
        }
    }

    private var dirty: Bool { !text.isEmpty || !url.isEmpty || !files.isEmpty || !note.isEmpty }

    private var ready: Bool {
        switch type {
        case "online_text_entry": return !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        case "online_url": return !url.trimmingCharacters(in: .whitespaces).isEmpty
        case "online_upload": return !files.isEmpty
        default: return false
        }
    }

    private var allowedTypes: [UTType] {
        let list = (data.allowed ?? []).compactMap { UTType(filenameExtension: $0) }
        return list.isEmpty ? [.item] : list
    }

    static func label(_ t: String) -> String {
        switch t {
        case "online_text_entry": return "Text"
        case "online_url": return "Website"
        case "online_upload": return "Files"
        default: return t
        }
    }

    private func allowedName(_ name: String) -> Bool {
        guard let a = data.allowed, !a.isEmpty else { return true }
        return a.contains((name as NSString).pathExtension.lowercased())
    }

    private func add(_ f: PickedFile) {
        guard f.data.count <= SubmitSheet.limit else {
            error = "\(f.name) is larger than 50 MB. Hand it in on \(engine.lmsName)’s own page."
            return
        }
        guard allowedName(f.name) else {
            error = "This assignment takes \((data.allowed ?? []).map { ".\($0)" }.joined(separator: ", ")) files."
            return
        }
        error = nil
        files.append(f)
    }

    private func pickedFiles(_ result: Result<[URL], Error>) {
        switch result {
        case .success(let urls):
            for u in urls {
                let scoped = u.startAccessingSecurityScopedResource()
                defer { if scoped { u.stopAccessingSecurityScopedResource() } }
                guard let d = try? Data(contentsOf: u) else { error = "\(u.lastPathComponent) could not be read."; continue }
                let mime = UTType(filenameExtension: u.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
                add(PickedFile(name: u.lastPathComponent, type: mime, data: d))
            }
        case .failure(let e):
            error = e.localizedDescription
        }
    }

    private func pickedPhotos() {
        let items = photos
        guard !items.isEmpty else { return }
        photos = []
        let fmt = DateFormatter()
        fmt.dateFormat = "yyyy-MM-dd 'at' HH.mm.ss"
        let stamp = fmt.string(from: Date())
        Task {
            for (i, item) in items.enumerated() {
                guard let d = try? await item.loadTransferable(type: Data.self) else { error = "A photo could not be read."; continue }
                let ut = item.supportedContentTypes.first(where: { $0.preferredFilenameExtension != nil }) ?? .jpeg
                let ext = ut.preferredFilenameExtension ?? "jpg"
                add(PickedFile(name: "Photo \(stamp)\(items.count > 1 ? " \(i + 1)" : "").\(ext)", type: ut.preferredMIMEType ?? "image/jpeg", data: d))
            }
        }
    }

    private func submit() {
        sending = true
        error = nil
        var args: [String: Any] = ["course": data.course, "id": data.id, "type": type, "comment": note]
        switch type {
        case "online_text_entry": args["text"] = text
        case "online_url": args["url"] = url.trimmingCharacters(in: .whitespaces)
        default: args["files"] = files.map { ["name": $0.name, "type": $0.type, "data": $0.data.base64EncodedString()] }
        }
        Task {
            do {
                _ = try await engine.call("submit", args, as: OK.self)
                Haptics.success()
                dismiss()
                await done() // (after the sheet goes: the assignment shows the work arriving)
            } catch {
                Haptics.error()
                self.error = error.localizedDescription
            }
            sending = false
        }
    }
}

/// The rubric, in a sheet of its own: each criterion with its ratings (the one given marked), its points and
/// the teacher's comment, and the total.
struct RubricSheet: View {
    let data: AssignmentData
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List {
                if let s = data.rubricScore, !s.isEmpty {
                    Section {
                        HStack {
                            Text("Score").font(.headline)
                            Spacer()
                            Text(s).font(.title3.weight(.bold).monospacedDigit())
                        }
                    }
                }
                ForEach(data.rubric) { r in
                    Section {
                        ForEach(Array(r.ratings.enumerated()), id: \.offset) { _, rating in
                            HStack(alignment: .top, spacing: 10) {
                                Image(systemName: rating.got == true ? "checkmark.circle.fill" : "circle")
                                    .foregroundStyle(rating.got == true ? Color.green : Color(.tertiaryLabel))
                                Text(rating.text).font(.subheadline).foregroundStyle(rating.got == true ? .primary : .secondary)
                                Spacer(minLength: 6)
                                if let p = rating.pts { Text(p).font(.subheadline.monospacedDigit()).foregroundStyle(.secondary) }
                            }
                            .listRowBackground(rating.got == true ? Color.green.opacity(0.12) : nil)
                        }
                        if let c = r.comment, !c.isEmpty {
                            Label(c, systemImage: "text.bubble").font(.subheadline)
                        }
                    } header: {
                        HStack(alignment: .firstTextBaseline) {
                            Text(r.name).textCase(nil)
                            Spacer()
                            if let p = r.pts { Text(p).textCase(nil).monospacedDigit() }
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .navigationTitle(data.rubricTitle ?? "Rubric")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } }
            }
        }
        .presentationDetents([.medium, .large])
        .presentationDragIndicator(.visible)
    }
}

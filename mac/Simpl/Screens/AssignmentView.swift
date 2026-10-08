import AppKit
import PhotosUI
import SwiftUI
import UniformTypeIdentifiers

/// One assignment, as a page of its own (1.2: sections on the page itself, one card deep): its kind, its course and
/// where your work stands over its name; then the instructions, what you handed in and the comments on the left, and on
/// the right the one thing to do next — Hand In, Take Quiz, Open Discussion, Open Tool — in glass, with the grade and
/// the facts: due, points, when it is open, how it is handed in, the attempts. The rubric is the ring (RubricRing.swift)
/// across the page: under the name once it is marked, after everything else before. A narrow window shows the right's
/// cards first, in one column. A file handed in or attached opens in Quick Look with a click (or previews in the page);
/// files dropped on the page open Hand In with them already in it; arriving from a piece of work's Hand In or See
/// Feedback does that at once.
struct AssignmentView: View {
    let course: String
    let id: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<AssignmentData>()
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var handIn = false
    @State private var commenting = false
    /// What was dropped on the page (files, or a web address): Hand In opens with it in.
    @State private var dropped: [URL] = []
    @State private var dropTargeted = false
    /// A part of the page to bring into view ("work" after a hand-in; "grade", "rubric" or "comments" for feedback), and
    /// the one lit up for a moment once it is there.
    @State private var jump: String?
    @State private var lit: String?
    /// Counts the hand-ins made here: each one bounces the Submitted seal once it is in view.
    @State private var handedIn = 0
    /// The screenshot suite's way to the rubric (-SimplOpen rubric, rubric:2, rubric:grid).
    @State private var rubricShot: String?

    var body: some View {
        Group {
            if let d = model.data {
                ScrollViewReader { proxy in
                    page(d)
                        // (initial: an arrival's feedback is asked for in the same update that first draws the page)
                        .onChange(of: jump, initial: true) { _, target in scroll(proxy, to: target) }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(model.data?.title ?? "Assignment")
        .navigationSubtitle(model.data?.context ?? "")
        .toolbar {
            ToolbarItemGroup(placement: .primaryAction) {
                if let d = model.data, let next = nextStep(d) {
                    Button { act(d) } label: {
                        Label(next.title, systemImage: next.symbol)
                    }
                    .help(next.help)
                }
                CanvasMenu(url: canvasURL, title: model.data?.title ?? "Assignment")
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .sheet(isPresented: $handIn, onDismiss: { dropped = [] }) {
            if let d = model.data {
                HandInSheet(assignment: d, dropped: dropped) { await handedInHere() }
                    .environmentObject(engine)
            }
        }
    }

    private var canvasURL: String { model.data?.canvasUrl ?? "/courses/\(course)/assignments/\(id)?bcv=native" }

    // MARK: - The page

    private func page(_ d: AssignmentData) -> some View {
        let columns = AssignmentColumns()
        let ringFirst = rubricFirst(d)
        return Page(spacing: 32) {
            header(d)
            if ringFirst { rubricSection(d) }
            columns {
                mainColumn(d)
                sideColumn(d)
            }
            if !ringFirst && !d.rubric.isEmpty { rubricSection(d) }
        }
        .overlay { dropOverlay(d) }
        .dropDestination(for: URL.self) { urls, _ in
            dropOnPage(urls, d)
        } isTargeted: { on in
            withAnimation(Motion.snappy) { dropTargeted = on }
        }
    }

    private func header(_ d: AssignmentData) -> some View {
        let color = courseTint(d)
        return VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                IconTile(symbol: Glyph.item(d.kind ?? "assignment"), color: color, size: 28)
                Text(d.kind ?? "Assignment")
                    .font(.sHeadline)
                    .foregroundStyle(color)
                if let c = d.context, !c.isEmpty {
                    Button {
                        engine.go(.home("courses/\(course)"))
                    } label: {
                        CourseChip(text: c, color: d.color)
                    }
                    .buttonStyle(.plain)
                    .help("Open \(c)")
                }
                if let s = d.status, !s.word.isEmpty {
                    StatusChip(text: s.word, tone: s.kind)
                        .transition(.opacity)
                }
            }
            ScreenHeading(title: d.title)
        }
    }

    // MARK: - The rubric

    /// Marked and posted, the rubric is the feedback: it comes first, under the name.
    private func rubricFirst(_ d: AssignmentData) -> Bool {
        !d.rubric.isEmpty && RubricModel(rows: d.rubric).graded
    }

    private func rubricSection(_ d: AssignmentData) -> some View {
        RubricSection(data: d, shot: rubricShot)
            .overlay { litFrame("rubric", d).padding(-12) }
            .id("rubric")
    }

    // MARK: - The left: what to read, what was handed in, what was said

    private func mainColumn(_ d: AssignmentData) -> some View {
        VStack(alignment: .leading, spacing: 32) {
            if !d.html.isEmpty {
                PageSection(title: "Instructions") {
                    RichText(html: d.html, size: 15)
                        .padding(.horizontal, 22)
                        .padding(.vertical, 18)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .card()
                }
            }
            workSection(d)
            commentsSection(d)
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
    }

    /// What you handed in: when, the text, the web address, the files (a click opens one in Quick Look; Preview shows
    /// it here).
    @ViewBuilder
    private func workSection(_ d: AssignmentData) -> some View {
        let files = d.submission?.files ?? []
        let link = d.submission?.url ?? ""
        let text = d.submission?.text ?? ""
        let submitted = d.submitted ?? ""
        if !submitted.isEmpty || !files.isEmpty || !link.isEmpty || !text.isEmpty {
            PageSection(title: "Your Work") {
                VStack(alignment: .leading, spacing: 0) {
                    if !submitted.isEmpty {
                        Label(submitted, systemImage: "checkmark.seal.fill")
                            .font(.sBody.weight(.semibold))
                            .foregroundStyle(.green)
                            .symbolEffect(.bounce, value: handedIn)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 8)
                    }
                    if !text.isEmpty {
                        if !submitted.isEmpty { RowDivider(inset: 10) }
                        Text(text)
                            .font(.sBody)
                            .textSelection(.enabled)
                            .lineLimit(14)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 10)
                    }
                    if !link.isEmpty {
                        if !submitted.isEmpty || !text.isEmpty { RowDivider(inset: 54) }
                        RowLink { visit(link) } label: {
                            InfoRow(title: link, sub: "Website", symbol: "link", tint: .blue)
                        }
                        .help("Open \(link)")
                        .contextMenu {
                            Button("Open") { visit(link) }
                            Button("Copy Link") { copyToPasteboard(link) }
                        }
                    }
                    ForEach(Array(files.enumerated()), id: \.element.id) { i, f in
                        if i > 0 || !submitted.isEmpty || !text.isEmpty || !link.isEmpty { RowDivider(inset: 54) }
                        AttachmentRow(file: f)
                    }
                }
                .padding(8)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card()
            }
            .id("work")
        }
    }

    /// The comments on your work, each with its files, and a box to write one.
    private func commentsSection(_ d: AssignmentData) -> some View {
        PageSection(title: "Comments", trailing: d.comments.isEmpty ? nil : "\(d.comments.count)", accessory: {
            if !commenting {
                Button {
                    withAnimation(Motion.gentle) { commenting = true }
                } label: {
                    Label("Add a Comment", systemImage: "text.bubble")
                }
                .glassButton()
                .help("Write a comment for your teacher")
            }
        }) {
            VStack(alignment: .leading, spacing: 0) {
                if d.comments.isEmpty && !commenting {
                    EmptyNote(text: "No comments yet.", symbol: "text.bubble")
                        .padding(.horizontal, 10)
                }
                ForEach(Array(d.comments.enumerated()), id: \.element.id) { i, c in
                    if i > 0 { RowDivider(inset: 56) }
                    commentRow(c)
                }
                if commenting {
                    CommentComposer { text in
                        try await sendComment(text)
                    } cancel: {
                        withAnimation(Motion.gentle) { commenting = false }
                    }
                    .padding(.horizontal, 8)
                    .padding(.top, d.comments.isEmpty ? 4 : 12)
                    .padding(.bottom, 4)
                    .transition(entering)
                }
            }
            .padding(8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card()
        }
        .overlay { litFrame("comments", d).padding(-10) }
        .id("comments")
    }

    private func commentRow(_ c: CommentRow) -> some View {
        HStack(alignment: .top, spacing: 12) {
            PersonAvatar(name: c.author, avatar: c.avatar, size: 34)
            VStack(alignment: .leading, spacing: 5) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(c.author)
                        .font(.sBody.weight(.semibold))
                        .lineLimit(1)
                    if let a = c.attempt, a > 0 {
                        Text("Attempt \(a)")
                            .font(.sCaption)
                            .foregroundStyle(.tertiary)
                    }
                    Spacer(minLength: 6)
                    if let w = c.when, !w.isEmpty {
                        Text(w)
                            .font(.sCaption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
                if !c.text.isEmpty {
                    Text(c.text)
                        .font(.sBody)
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                }
                ForEach(c.attachments ?? []) { f in
                    Button { engine.openFile(f.url, name: f.name) } label: {
                        HStack(spacing: 6) {
                            FileIcon(name: f.name, size: 18)
                            Text(f.name).lineLimit(1).truncationMode(.middle)
                        }
                    }
                    .buttonStyle(.link)
                    .font(.sCallout)
                    .help("Open \(f.name) in Quick Look")
                    .contextMenu { FileMenuItems(engine: engine, url: f.url, name: f.name) }
                }
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 10)
        .contextMenu {
            if !c.text.isEmpty {
                Button("Copy Comment") { copyToPasteboard(c.text) }
            }
        }
    }

    // MARK: - The right: the next step, the grade, the facts

    private func sideColumn(_ d: AssignmentData) -> some View {
        VStack(alignment: .leading, spacing: 16) {
            actionPanel(d)
            if let g = d.grade {
                gradeCard(g, d)
            } else if d.held == true {
                heldNote
            }
            if d.grade == nil && !d.rubric.isEmpty {
                rubricRow(d)
            }
            factsCard(d)
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
    }

    /// The one thing to do next, in glass in the course's colour and as wide as the column, with why it cannot be
    /// handed in here when it cannot; with nothing to do here, that reason and Canvas's own page.
    @ViewBuilder
    private func actionPanel(_ d: AssignmentData) -> some View {
        let why = d.why ?? ""
        if let next = nextStep(d) {
            VStack(alignment: .leading, spacing: 10) {
                Button { act(d) } label: {
                    Label(next.title, systemImage: next.symbol)
                        .font(.sHeadline)
                        .frame(maxWidth: .infinity)
                }
                .glassButton(prominent: true)
                .controlSize(.extraLarge)
                .tint(courseTint(d))
                .help(next.help)
                if takesDrops(d) {
                    Text(d.types.contains("online_upload") ? "Or drop files on this page." : "Or drop a link on this page.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity)
                }
                if !why.isEmpty { whyNote(why) }
            }
        } else if !why.isEmpty {
            VStack(alignment: .leading, spacing: 12) {
                whyNote(why)
                Button {
                    engine.openWebScreen(canvasURL, title: d.title)
                } label: {
                    Label("Open in \(engine.lmsName)", systemImage: "globe")
                        .frame(maxWidth: .infinity)
                }
                .glassButton()
                .controlSize(.large)
                .help("Open \(engine.lmsName)’s own page for this assignment")
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card()
        }
    }

    private func whyNote(_ why: String) -> some View {
        Label(why, systemImage: "info.circle")
            .font(.sCallout)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }

    /// The grade: its ring and letter, the points, the percentage, what lateness took, the class's numbers, and the way
    /// to the rubric.
    private func gradeCard(_ g: GradeInfo, _ d: AssignmentData) -> some View {
        let color = courseTint(d)
        return VStack(alignment: .leading, spacing: 14) {
            Text("Grade")
                .font(.sHeadline)
                .foregroundStyle(.secondary)
            HStack(spacing: 16) {
                ZStack {
                    Ring(value: g.pct, color: color, lineWidth: 7, key: "assignment:\(course)/\(id)")
                    Text(g.letter ?? g.pct.map { "\(Int($0.rounded()))%" } ?? "")
                        .font(.sTitle3.weight(.bold))
                        .foregroundStyle(color)
                        .minimumScaleFactor(0.6)
                        .padding(9)
                }
                .frame(width: 76, height: 76)
                VStack(alignment: .leading, spacing: 3) {
                    scoreText(g)
                    if let pct = g.pct {
                        Text(String(format: "%.1f%%", pct))
                            .font(.sBody)
                            .foregroundStyle(.secondary)
                    }
                    if let late = g.late, !late.isEmpty {
                        Text(late)
                            .font(.sCallout.weight(.semibold))
                            .foregroundStyle(.orange)
                    }
                }
                Spacer(minLength: 0)
            }
            .accessibilityElement(children: .combine)
            if let st = d.stats, !st.isEmpty {
                Label(st, systemImage: "person.3")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if !d.rubric.isEmpty { rubricJump(d) }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
        .overlay { litFrame("grade", d) }
        .id("grade")
    }

    @ViewBuilder
    private func scoreText(_ g: GradeInfo) -> some View {
        if let s = g.score, let p = g.possible, p > 0 {
            Text("\(AssignmentView.num(s)) / \(AssignmentView.num(p))")
                .font(.sTitle2.monospacedDigit())
                .contentTransition(.numericText(value: s))
        } else {
            Text(g.text)
                .font(.sTitle2)
        }
    }

    /// The grade's way to the rubric that explains it: the ring in miniature, its score; the page goes to it.
    private func rubricJump(_ d: AssignmentData) -> some View {
        Button { jump = "rubric" } label: {
            HStack(spacing: 10) {
                RubricMiniRing(model: RubricModel(rows: d.rubric), size: 26)
                Text(d.rubricTitle ?? "Rubric")
                    .lineLimit(1)
                Spacer(minLength: 6)
                if let s = d.rubricScore, !s.isEmpty {
                    Text(s)
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                }
                Image(systemName: rubricFirst(d) ? "chevron.up" : "chevron.down")
                    .font(.sCaption.weight(.semibold))
                    .foregroundStyle(.secondary)
            }
            .font(.sCallout.weight(.semibold))
            .frame(maxWidth: .infinity)
        }
        .glassButton()
        .controlSize(.large)
        .help("See how this was marked, criterion by criterion")
    }

    /// No grade yet, but a rubric: how it will be marked, a click away down the page.
    private func rubricRow(_ d: AssignmentData) -> some View {
        let n = d.rubric.count
        return Button { jump = "rubric" } label: {
            HStack(spacing: 12) {
                RubricMiniRing(model: RubricModel(rows: d.rubric), size: 32)
                VStack(alignment: .leading, spacing: 2) {
                    Text(d.rubricTitle ?? "Rubric")
                        .font(.sBody.weight(.semibold))
                        .lineLimit(1)
                    Text("\(n) \(n == 1 ? "criterion" : "criteria") · how this is marked")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
                Spacer(minLength: 6)
                Image(systemName: "chevron.down")
                    .font(.sCaption.weight(.semibold))
                    .foregroundStyle(.tertiary)
            }
            .padding(14)
        }
        .buttonStyle(CardButtonStyle())
        .help("See how this will be marked")
    }

    private var heldNote: some View {
        Label("Graded, but your teacher has not released the grade yet.", systemImage: "eye.slash")
            .font(.sBody)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card()
    }

    /// The facts, one to a line: when it is due, what it is worth, when it opens and closes, how it is handed in, the
    /// attempts used.
    private func factsCard(_ d: AssignmentData) -> some View {
        let color = courseTint(d)
        let lines = facts(d)
        return VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(lines.enumerated()), id: \.offset) { i, f in
                if i > 0 { RowDivider(inset: 56) }
                factLine(f.symbol, f.label, f.value, color)
            }
        }
        .padding(.vertical, 6)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    private func facts(_ d: AssignmentData) -> [(symbol: String, label: String, value: String)] {
        var out: [(symbol: String, label: String, value: String)] = []
        let due = d.due ?? ""
        out.append((symbol: "calendar", label: "Due", value: due.isEmpty ? "No due date" : due))
        if let p = d.points, !p.isEmpty { out.append((symbol: "star", label: "Points", value: p)) }
        if let a = d.available, !a.isEmpty { out.append((symbol: "clock", label: "Available", value: a)) }
        if let t = d.typesText, !t.isEmpty { out.append((symbol: "tray.and.arrow.up", label: "Handed In As", value: t)) }
        if let at = d.attemptsText, !at.isEmpty { out.append((symbol: "arrow.counterclockwise", label: "Attempts", value: at)) }
        return out
    }

    private func factLine(_ symbol: String, _ label: String, _ value: String, _ color: Color) -> some View {
        HStack(alignment: .top, spacing: 12) {
            IconTile(symbol: symbol, color: color, size: 30)
            VStack(alignment: .leading, spacing: 2) {
                Text(label.uppercased())
                    .font(.sCaption.weight(.semibold))
                    .tracking(0.6)
                    .foregroundStyle(.secondary)
                Text(value)
                    .font(.sBody)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 9)
        .accessibilityElement(children: .combine)
    }

    // MARK: - Light

    /// While files are dragged over the page: where they go.
    @ViewBuilder
    private func dropOverlay(_ d: AssignmentData) -> some View {
        if dropTargeted && takesDrops(d) {
            let color = courseTint(d)
            ZStack {
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .fill(color.opacity(0.06))
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .strokeBorder(color, style: StrokeStyle(lineWidth: 2.5, dash: [9, 6]))
                VStack(spacing: 8) {
                    Image(systemName: "tray.and.arrow.up.fill")
                        .font(.system(size: 32, weight: .semibold))
                        .foregroundStyle(color)
                    Text(d.resubmit == true ? "Drop to Hand In Again" : "Drop to Hand In")
                        .font(.sTitle3)
                    Text(d.title)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                .padding(.horizontal, 30)
                .padding(.vertical, 22)
                .glassCard(radius: 18)
            }
            .padding(14)
            .allowsHitTesting(false)
            .transition(.opacity)
        }
    }

    /// A part's edge in the course's colour, for a moment, when the page has just gone to it (the feedback asked for).
    private func litFrame(_ part: String, _ d: AssignmentData) -> some View {
        RoundedRectangle(cornerRadius: 16, style: .continuous)
            .strokeBorder(courseTint(d), lineWidth: 2)
            .opacity(lit == part ? 1 : 0)
            .allowsHitTesting(false)
    }

    private var entering: AnyTransition {
        reduceMotion ? .opacity : .opacity.combined(with: .move(edge: .top))
    }

    private func courseTint(_ d: AssignmentData) -> Color {
        guard let c = d.color, !c.isEmpty else { return .accentColor }
        return Color(hex: c)
    }

    private static func num(_ v: Double) -> String {
        if !v.isFinite { return "—" }
        if v == v.rounded() && abs(v) < 1e15 { return String(Int(v)) }
        return String(format: "%g", v)
    }

    // MARK: - Doing

    /// The one thing to do next: hand in (again), take the quiz, open the discussion or the tool.
    private func nextStep(_ d: AssignmentData) -> NextStep? {
        if d.canSubmit {
            return d.resubmit == true
                ? NextStep(title: "Hand In Again", symbol: "tray.and.arrow.up.fill", help: "Hand in this assignment again")
                : NextStep(title: "Hand In", symbol: "tray.and.arrow.up.fill", help: "Hand in this assignment")
        }
        if d.quizUrl != nil { return NextStep(title: "Take Quiz", symbol: "checklist", help: "Take this quiz") }
        if d.discussionUrl != nil {
            return NextStep(title: "Open Discussion", symbol: "bubble.left.and.bubble.right.fill", help: "Open this assignment’s discussion")
        }
        if d.toolUrl != nil {
            return d.ltiQuiz == true
                ? NextStep(title: "Take Quiz", symbol: "checklist", help: "Open this quiz in a window of its own")
                : NextStep(title: "Open Tool", symbol: "puzzlepiece.extension.fill", help: "Open this assignment’s tool in a window of its own")
        }
        return nil
    }

    private func act(_ d: AssignmentData) {
        if d.canSubmit {
            handIn = true
        } else if let q = d.quizId {
            engine.openQuiz(QuizLaunch(course: course, quiz: q, title: d.title))
        } else if let q = d.quizUrl {
            engine.go(q, title: d.title)
        } else if let u = d.discussionUrl {
            engine.go(u, title: d.title)
        } else if d.toolUrl != nil {
            engine.openTool(.assignment(course: course, id: id, title: d.title))
        }
    }

    /// Arrived from a piece of work's own action: its Hand In done at once, or its feedback shown (a quiz's in the quiz
    /// screen; else the page goes to the marked rubric, the grade, or the comments, and lights it).
    private func arrived(_ d: AssignmentData) {
        guard let a = engine.arrival, a.id == id else { return }
        engine.arrival = nil
        if a.feedback {
            if let q = d.quizId {
                engine.openQuiz(QuizLaunch(course: course, quiz: q, title: d.title, feedback: true))
            } else if d.grade != nil {
                jump = rubricFirst(d) ? "rubric" : "grade"
            } else if !d.comments.isEmpty {
                jump = "comments"
            }
        } else if nextStep(d) != nil {
            act(d)
        }
    }

    /// Whether something dropped on the page can be handed in: files, or a web address, as the assignment takes them.
    private func takesDrops(_ d: AssignmentData) -> Bool {
        d.canSubmit && (d.types.contains("online_upload") || d.types.contains("online_url"))
    }

    private func dropOnPage(_ urls: [URL], _ d: AssignmentData) -> Bool {
        guard takesDrops(d), !urls.isEmpty, !handIn else { return false }
        dropped = urls
        handIn = true
        return true
    }

    /// The sheet away first, then the work arrives where it can be seen: the page goes to it and its seal bounces.
    private func handedInHere() async {
        try? await Task.sleep(nanoseconds: 350_000_000)
        await reload()
        jump = "work"
        try? await Task.sleep(nanoseconds: 300_000_000)
        handedIn += 1
        engine.changed()
    }

    private func sendComment(_ text: String) async throws {
        _ = try await engine.call("commentOn", ["course": course, "id": id, "text": text], as: OK.self)
        withAnimation(Motion.gentle) { commenting = false }
        await reload()
    }

    /// A web address handed in: the app's own screen when it is one of the school's, else the browser.
    private func visit(_ link: String) {
        if let u = URL(string: link) { engine.openLink(u) }
    }

    private func scroll(_ proxy: ScrollViewProxy, to target: String?) {
        guard let target else { return }
        jump = nil
        Task {
            try? await Task.sleep(nanoseconds: 60_000_000) // (what it goes to laid out first)
            withAnimation(reduceMotion ? nil : Motion.gentle) { proxy.scrollTo(target, anchor: .top) }
            guard target != "work" else { return } // (the work has its seal's bounce)
            withAnimation(Motion.gentle) { lit = target }
            try? await Task.sleep(nanoseconds: 1_600_000_000)
            withAnimation(Motion.gentle) { lit = nil }
        }
    }

    private func load() async {
        await model.load(engine, "assignment", ["course": course, "id": id])
        if model.data?.canSubmit == true, LaunchOpen.take("handin") != nil { handIn = true }
        if model.data?.rubric.isEmpty == false, let shot = LaunchOpen.take("rubric") {
            rubricShot = shot
            jump = "rubric"
        }
        if let d = model.data { arrived(d) }
    }

    private func reload() async {
        await model.load(engine, "assignment", ["course": course, "id": id], animated: true)
    }
}

/// An assignment's next step, as its button says it: the words, the symbol, the tooltip.
private struct NextStep {
    let title: String
    let symbol: String
    let help: String
}

/// The assignment's page in two columns on a wide window — what to read and what was said on the left, the next step,
/// the grade and the facts on the right (a little wider as the window grows) — and in one column on a narrow one, the
/// right's cards first.
private struct AssignmentColumns: Layout {
    private static let gap: CGFloat = 28
    /// The narrowest the page's column may be and still hold the two side by side.
    private static let twoFrom: CGFloat = 780

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        guard subviews.count == 2 else { return .zero }
        let width = proposal.width.flatMap { $0.isFinite ? $0 : nil } ?? 900
        return CGSize(width: width, height: frames(width, subviews).height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        guard subviews.count == 2 else { return }
        let f = frames(bounds.width, subviews)
        subviews[0].place(at: CGPoint(x: bounds.minX + f.main.minX, y: bounds.minY + f.main.minY), anchor: .topLeading,
                          proposal: ProposedViewSize(width: f.main.width, height: nil))
        subviews[1].place(at: CGPoint(x: bounds.minX + f.side.minX, y: bounds.minY + f.side.minY), anchor: .topLeading,
                          proposal: ProposedViewSize(width: f.side.width, height: nil))
    }

    /// Where each column goes, and how tall the two are together.
    private func frames(_ width: CGFloat, _ subviews: Subviews) -> (main: CGRect, side: CGRect, height: CGFloat) {
        if width >= AssignmentColumns.twoFrom {
            let sideWidth = min(400, max(290, ((width - AssignmentColumns.gap) * 0.32).rounded()))
            let mainWidth = width - AssignmentColumns.gap - sideWidth
            let mainHeight = subviews[0].sizeThatFits(ProposedViewSize(width: mainWidth, height: nil)).height
            let sideHeight = subviews[1].sizeThatFits(ProposedViewSize(width: sideWidth, height: nil)).height
            return (CGRect(x: 0, y: 0, width: mainWidth, height: mainHeight),
                    CGRect(x: width - sideWidth, y: 0, width: sideWidth, height: sideHeight),
                    max(mainHeight, sideHeight))
        }
        let sideHeight = subviews[1].sizeThatFits(ProposedViewSize(width: width, height: nil)).height
        let mainHeight = subviews[0].sizeThatFits(ProposedViewSize(width: width, height: nil)).height
        let gap = sideHeight > 0 && mainHeight > 0 ? AssignmentColumns.gap : 0
        return (CGRect(x: 0, y: sideHeight + gap, width: width, height: mainHeight),
                CGRect(x: 0, y: 0, width: width, height: sideHeight),
                sideHeight + gap + mainHeight)
    }
}

/// A file handed in, as a row of Your Work: a click opens it in Quick Look; Preview shows it here, in the page, as
/// Quick Look draws it (fetched the first time it is asked for); its menu has the rest (its app, Download, Copy Link).
private struct AttachmentRow: View {
    let file: Attachment
    @EnvironmentObject private var engine: Engine
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var open = false
    @State private var local: URL?
    @State private var problem: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 8) {
                RowLink { engine.openFile(file.url, name: file.name) } label: {
                    HStack(spacing: 12) {
                        FileIcon(name: file.name, size: 32)
                        Text(file.name)
                            .font(.sBody)
                            .lineLimit(2)
                            .truncationMode(.middle)
                        Spacer(minLength: 6)
                    }
                }
                .help("Open \(file.name) in Quick Look")
                Button {
                    withAnimation(reduceMotion ? nil : Motion.gentle) { open.toggle() }
                } label: {
                    Label(open ? "Hide" : "Preview", systemImage: open ? "chevron.up" : "eye")
                }
                .glassButton()
                .help(open ? "Hide the preview" : "Show \(file.name) here")
                .padding(.trailing, 4)
            }
            .contextMenu { FileMenuItems(engine: engine, url: file.url, name: file.name) }
            if open {
                preview
                    .frame(height: 520)
                    .frame(maxWidth: .infinity)
                    .padding(.horizontal, 8)
                    .padding(.bottom, 8)
                    .transition(.opacity)
            }
        }
        .task(id: open) {
            guard open, local == nil else { return }
            await fetch()
        }
    }

    @ViewBuilder
    private var preview: some View {
        if let local {
            QuickLookView(file: local)
                .accessibilityLabel("Preview of \(file.name)")
        } else if let problem {
            ContentUnavailableView {
                Label("No Preview", systemImage: "exclamationmark.triangle")
            } description: {
                Text(problem)
            } actions: {
                Button("Try Again") { Task { await fetch() } }
            }
        } else {
            VStack(spacing: 10) {
                ProgressView()
                Text("Loading \(file.name)…")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    private func fetch() async {
        problem = nil
        do {
            local = try await engine.fetchFile(file.url, name: file.name)
        } catch is CancellationError {
            return
        } catch {
            problem = error.localizedDescription
        }
    }
}

/// Writing a comment for the teacher, in the comments' own card: the box takes the keys at once, ⌘Return sends,
/// Escape puts it away. It stays until the words are sent, and says so if they could not be.
private struct CommentComposer: View {
    let send: (String) async throws -> Void
    let cancel: () -> Void
    @State private var text = ""
    @State private var sending = false
    @State private var error: String?
    @FocusState private var focused: Bool

    private var empty: Bool { text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            ZStack(alignment: .topLeading) {
                TextEditor(text: $text)
                    .font(.sBody)
                    .scrollContentBackground(.hidden)
                    .focused($focused)
                    .disabled(sending)
                if text.isEmpty {
                    Text("Write a comment for your teacher")
                        .font(.sBody)
                        .foregroundStyle(.tertiary)
                        .padding(.leading, 5)
                        .allowsHitTesting(false)
                }
            }
            .frame(height: 110)
            .padding(10)
            .background(Theme.well, in: RoundedRectangle(cornerRadius: 12, style: .continuous)) // (1.2: a well, not a box in the card)
            .overlay {
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .strokeBorder(Color.accentColor.opacity(focused ? 0.6 : 0), lineWidth: 2)
            }
            .animation(Motion.hover, value: focused)
            if let error {
                Label(error, systemImage: "exclamationmark.triangle.fill")
                    .font(.sCallout)
                    .foregroundStyle(.red)
            }
            HStack(spacing: 8) {
                Text("⌘↩ to send · esc to cancel")
                    .font(.sCaption)
                    .foregroundStyle(.tertiary)
                Spacer(minLength: 8)
                Button("Cancel", action: cancel)
                    .glassButton()
                    .keyboardShortcut(.cancelAction)
                    .disabled(sending)
                Button(action: go) {
                    if sending {
                        ProgressView().controlSize(.small)
                    } else {
                        Text("Send")
                    }
                }
                .glassButton(prominent: true)
                .keyboardShortcut(.return, modifiers: .command)
                .disabled(sending || empty)
            }
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

/// Hand In, as a sheet over the assignment: the kinds it takes from here (text, a web address, files — chosen in an
/// Open panel or from Photos, or dropped on the sheet), a note for the teacher, and Submit. The assignment's own file
/// types are checked before anything is sent; while it goes, the sheet says so, and says why if it could not.
private struct HandInSheet: View {
    let assignment: AssignmentData
    let dropped: [URL]
    let done: () async -> Void
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var type: String
    @State private var text = ""
    @State private var url = ""
    @State private var files: [PickedFile] = []
    @State private var note = ""
    @State private var importing = false
    @State private var photos: [PhotosPickerItem] = []
    @State private var sending = false
    @State private var error: String?
    @State private var targeted = false

    private static let limit = 50 * 1024 * 1024 // (read whole into memory and handed to the page: kept to a sane size)

    init(assignment: AssignmentData, dropped: [URL], done: @escaping () async -> Void) {
        self.assignment = assignment
        self.dropped = dropped
        self.done = done
        _type = State(initialValue: assignment.types.first ?? "")
    }

    var body: some View {
        VStack(spacing: 0) {
            header
            Form {
                if assignment.types.count > 1 {
                    Section {
                        Picker("Hand In As", selection: $type.animation(Motion.snappy)) { // (the kind's own fields swap in place)
                            ForEach(assignment.types, id: \.self) { t in
                                Text(HandInSheet.label(t)).tag(t)
                            }
                        }
                        .pickerStyle(.segmented)
                    }
                }
                kindSection
                Section("Comment (Optional)") {
                    TextField("Comment", text: $note, prompt: Text("A note for your teacher"), axis: .vertical)
                        .labelsHidden()
                        .lineLimit(2...6)
                }
                if let error {
                    Section {
                        Label(error, systemImage: "exclamationmark.triangle.fill")
                            .foregroundStyle(.red)
                    }
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
            .disabled(sending)
            Divider()
            footer
        }
        .frame(minWidth: 520, idealWidth: 580, minHeight: 460, idealHeight: 560)
        .overlay { dropOverlay }
        .dropDestination(for: URL.self) { urls, _ in
            take(urls)
        } isTargeted: { on in
            withAnimation(Motion.snappy) { targeted = on }
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: allowedTypes, allowsMultipleSelection: true) { result in
            pickedFiles(result)
        }
        .onChange(of: photos) { _, _ in pickedPhotos() }
        .onAppear { if !dropped.isEmpty { _ = take(dropped) } }
        .interactiveDismissDisabled(sending)
    }

    // MARK: - Parts

    private var header: some View {
        HStack(spacing: 12) {
            IconTile(symbol: "tray.and.arrow.up.fill", color: accent, size: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text(assignment.resubmit == true ? "Hand In Again" : "Hand In")
                    .font(.sTitle3.weight(.semibold))
                Text(subtitle)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 20)
        .padding(.top, 18)
        .padding(.bottom, 6)
    }

    /// The fields of the kind of hand-in chosen.
    @ViewBuilder
    private var kindSection: some View {
        switch type {
        case "online_text_entry":
            Section("Your Text") {
                ZStack(alignment: .topLeading) {
                    TextEditor(text: $text)
                        .font(.sBody)
                        .scrollContentBackground(.hidden)
                        .frame(height: 200)
                    if text.isEmpty {
                        Text("Write your answer")
                            .foregroundStyle(.tertiary)
                            .padding(.leading, 5)
                            .allowsHitTesting(false)
                    }
                }
            }
        case "online_url":
            Section("Website Address") {
                TextField("Address", text: $url, prompt: Text("https://"))
                    .labelsHidden()
                    .autocorrectionDisabled()
            }
        case "online_upload":
            filesSection
        default:
            EmptyView()
        }
    }

    private var filesSection: some View {
        Section {
            if files.isEmpty { dropZone }
            ForEach(files) { f in fileRow(f) }
            HStack(spacing: 10) {
                Button { importing = true } label: {
                    Label("Choose Files…", systemImage: "folder")
                }
                PhotosPicker(selection: $photos, maxSelectionCount: 10, matching: .any(of: [.images, .videos])) {
                    Label("Choose from Photos…", systemImage: "photo.on.rectangle")
                }
                Spacer(minLength: 0)
            }
        } header: {
            Text("Files")
        } footer: {
            if let a = assignment.allowed, !a.isEmpty {
                Text("This assignment takes \(allowedList) files.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var dropZone: some View {
        VStack(spacing: 6) {
            Image(systemName: "arrow.down.doc")
                .font(.system(size: 26))
                .foregroundStyle(.secondary)
            Text("Drop files here")
                .font(.sCallout.weight(.semibold))
            Text("or choose them below")
                .font(.sCaption)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 18)
        .background {
            RoundedRectangle(cornerRadius: 10, style: .continuous)
                .strokeBorder(targeted ? Color.accentColor : Color.secondary.opacity(0.4), style: StrokeStyle(lineWidth: 1.5, dash: [5, 4]))
        }
        .accessibilityElement(children: .combine)
    }

    private func fileRow(_ f: PickedFile) -> some View {
        HStack(spacing: 10) {
            Image(nsImage: HandInSheet.icon(f.name))
                .resizable()
                .frame(width: 28, height: 28)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 1) {
                Text(f.name)
                    .lineLimit(1)
                    .truncationMode(.middle)
                Text(f.sizeText)
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            Button { remove(f) } label: {
                Image(systemName: "xmark.circle.fill")
            }
            .buttonStyle(.borderless)
            .foregroundStyle(.secondary)
            .help("Remove \(f.name)")
            .accessibilityLabel("Remove \(f.name)")
        }
        .contextMenu {
            Button("Remove", role: .destructive) { remove(f) }
        }
        .transition(.opacity)
    }

    private var footer: some View {
        HStack(spacing: 10) {
            if sending {
                ProgressView()
                    .controlSize(.small)
                Text(progressWord)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            } else if type == "online_text_entry" {
                Text("⌘↩ to submit")
                    .font(.sCaption)
                    .foregroundStyle(.tertiary)
            }
            Spacer(minLength: 8)
            Button("Cancel") { dismiss() }
                .glassButton()
                .keyboardShortcut(.cancelAction)
                .disabled(sending)
            Button("Submit") { submit() }
                .glassButton(prominent: true)
                .keyboardShortcut(submitKey)
                .disabled(!ready || sending)
        }
        .controlSize(.large)
        .padding(.horizontal, 20)
        .padding(.vertical, 14)
    }

    /// While files are dragged over the sheet: that they will be added.
    @ViewBuilder
    private var dropOverlay: some View {
        if targeted && takesDrops {
            ZStack {
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .fill(Color.accentColor.opacity(0.07))
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .strokeBorder(Color.accentColor, style: StrokeStyle(lineWidth: 2, dash: [8, 5]))
                Label(assignment.types.contains("online_upload") ? "Drop to Add Files" : "Drop to Add the Link", systemImage: "arrow.down.doc.fill")
                    .font(.sHeadline)
                    .padding(.horizontal, 18)
                    .padding(.vertical, 10)
                    .background(.regularMaterial, in: Capsule())
            }
            .padding(8)
            .allowsHitTesting(false)
            .transition(.opacity)
        }
    }

    // MARK: - What it knows

    private var subtitle: String {
        [assignment.title, assignment.attemptsText ?? ""].filter { !$0.isEmpty }.joined(separator: " · ")
    }

    private var accent: Color {
        guard let c = assignment.color, !c.isEmpty else { return .accentColor }
        return Color(hex: c)
    }

    private var progressWord: String {
        guard type == "online_upload" else { return "Handing in…" }
        return files.count == 1 ? "Uploading 1 file…" : "Uploading \(files.count) files…"
    }

    /// Return submits — but in a box of text, where Return is a new line, ⌘Return does.
    private var submitKey: KeyboardShortcut {
        type == "online_text_entry" ? KeyboardShortcut(.return, modifiers: .command) : .defaultAction
    }

    private var ready: Bool {
        switch type {
        case "online_text_entry": return !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        case "online_url": return !url.trimmingCharacters(in: .whitespaces).isEmpty
        case "online_upload": return !files.isEmpty
        default: return false
        }
    }

    private var takesDrops: Bool {
        assignment.types.contains("online_upload") || assignment.types.contains("online_url")
    }

    private var allowedTypes: [UTType] {
        let list = (assignment.allowed ?? []).compactMap { UTType(filenameExtension: $0) }
        return list.isEmpty ? [.item] : list
    }

    private var allowedList: String {
        (assignment.allowed ?? []).map { ".\($0)" }.joined(separator: ", ")
    }

    private func allowedName(_ name: String) -> Bool {
        guard let a = assignment.allowed, !a.isEmpty else { return true }
        return a.contains((name as NSString).pathExtension.lowercased())
    }

    private static func label(_ t: String) -> String {
        switch t {
        case "online_text_entry": return "Text"
        case "online_url": return "Website"
        case "online_upload": return "Files"
        default: return t
        }
    }

    /// The Finder's own icon for a kind of file.
    private static func icon(_ name: String) -> NSImage {
        NSWorkspace.shared.icon(for: UTType(filenameExtension: (name as NSString).pathExtension) ?? .data)
    }

    // MARK: - Taking files

    /// What was dropped (on the sheet, or on the page before it): files to hand in, or a web address for a website.
    private func take(_ urls: [URL]) -> Bool {
        let local = urls.filter(\.isFileURL)
        let links = urls.filter { ["http", "https"].contains($0.scheme?.lowercased() ?? "") }
        if !local.isEmpty && assignment.types.contains("online_upload") {
            if type != "online_upload" { withAnimation(Motion.snappy) { type = "online_upload" } }
            for u in local { addFile(u) }
            return true
        }
        if let link = links.first, assignment.types.contains("online_url") {
            withAnimation(Motion.snappy) {
                type = "online_url"
                url = link.absoluteString
            }
            error = nil
            return true
        }
        if !local.isEmpty || !links.isEmpty {
            error = local.isEmpty ? "This assignment doesn’t take a web address." : "This assignment doesn’t take files."
        }
        return false
    }

    /// A file on this Mac, read to be handed in (not a folder, and not over the size the page can take).
    private func addFile(_ u: URL) {
        let scoped = u.startAccessingSecurityScopedResource()
        defer { if scoped { u.stopAccessingSecurityScopedResource() } }
        let name = u.lastPathComponent
        let facts = try? u.resourceValues(forKeys: [.isDirectoryKey, .fileSizeKey])
        if facts?.isDirectory == true {
            error = "\(name) is a folder. Choose the files in it."
            return
        }
        if let size = facts?.fileSize, size > HandInSheet.limit {
            error = "\(name) is larger than 50 MB. Hand it in on \(engine.lmsName)’s own page."
            return
        }
        guard let bytes = try? Data(contentsOf: u) else {
            error = "\(name) could not be read."
            return
        }
        let mime = UTType(filenameExtension: u.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
        add(PickedFile(name: name, type: mime, data: bytes))
    }

    private func add(_ f: PickedFile) {
        guard f.data.count <= HandInSheet.limit else {
            error = "\(f.name) is larger than 50 MB. Hand it in on \(engine.lmsName)’s own page."
            return
        }
        guard allowedName(f.name) else {
            error = "This assignment takes \(allowedList) files."
            return
        }
        error = nil
        guard !files.contains(where: { $0.name == f.name && $0.data.count == f.data.count }) else { return } // (dropped twice)
        withAnimation(Motion.snappy) { files.append(f) }
    }

    private func remove(_ f: PickedFile) {
        withAnimation(Motion.snappy) { files.removeAll { $0.id == f.id } }
    }

    private func pickedFiles(_ result: Result<[URL], Error>) {
        switch result {
        case .success(let urls):
            for u in urls { addFile(u) }
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
                guard let bytes = try? await item.loadTransferable(type: Data.self) else {
                    error = "A photo could not be read."
                    continue
                }
                let ut = item.supportedContentTypes.first(where: { $0.preferredFilenameExtension != nil }) ?? .jpeg
                let ext = ut.preferredFilenameExtension ?? "jpg"
                let number = items.count > 1 ? " \(i + 1)" : ""
                add(PickedFile(name: "Photo \(stamp)\(number).\(ext)", type: ut.preferredMIMEType ?? "image/jpeg", data: bytes))
            }
        }
    }

    // MARK: - Sending

    private func submit() {
        guard ready, !sending else { return }
        sending = true
        error = nil
        var args: [String: Any] = ["course": assignment.course, "id": assignment.id, "type": type, "comment": note]
        switch type {
        case "online_text_entry": args["text"] = text
        case "online_url": args["url"] = url.trimmingCharacters(in: .whitespaces)
        default: args["files"] = files.map { ["name": $0.name, "type": $0.type, "data": $0.data.base64EncodedString()] }
        }
        Task {
            do {
                _ = try await engine.call("submit", args, as: OK.self)
                dismiss()
                await done() // (after the sheet goes: the assignment shows the work arriving)
            } catch {
                self.error = error.localizedDescription
            }
            sending = false
        }
    }
}

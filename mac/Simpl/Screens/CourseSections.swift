import SwiftUI

/// A section of a course or a group: its place in the sidebar under the course, and its tile on the course's home.
struct SectionScreen: View {
    let ctx: String
    let kind: String

    var body: some View {
        switch kind {
        case "announcements": AnnouncementsList(ctx: ctx)
        case "discussions": DiscussionsList(ctx: ctx)
        case "assignments": AssignmentsList(ctx: ctx)
        case "modules": ModulesList(ctx: ctx)
        case "pages": PagesList(ctx: ctx)
        case "files": FilesView(ctx: ctx, folder: "", name: "Files")
        case "people": PeopleList(ctx: ctx)
        case "quizzes": QuizzesList(ctx: ctx)
        case "syllabus": SyllabusView(ctx: ctx)
        case "grades": CourseGradesView(courseId: ContextHome.id(of: ctx))
        default: OtherSection(ctx: ctx, kind: kind)
        }
    }
}

// MARK: - Announcements and discussions

/// A course's or a group's announcements, newest first: who posted each and when, its first lines, its replies.
private struct AnnouncementsList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<PostListData>()

    var body: some View {
        let title = courseSectionTitle(engine, ctx, "announcements", "Announcements")
        SectionLoaded(model: model, title: title, subtitle: model.data?.context,
                      canvasURL: courseSectionURL(engine, ctx, "announcements"), load: load) { d in
            Page {
                ScreenHeading(title: title, sub: d.context, color: Color(hex: d.color))
                if d.rows.isEmpty {
                    EmptyCard(text: d.empty ?? "No announcements", symbol: "megaphone")
                } else {
                    RowsCard {
                        DividedRows(data: d.rows, inset: 54) { r in PostLink(row: r, color: Color(hex: d.color)) }
                    }
                }
            }
        }
    }

    private func load() async { await model.load(engine, "announcements", ["ctx": ctx]) }
}

/// A course's or a group's discussions — pinned, open, closed for comments — each with who started it, its latest
/// activity, its replies and how many are new to you.
private struct DiscussionsList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<DiscussionsData>()

    var body: some View {
        let title = courseSectionTitle(engine, ctx, "discussions", "Discussions")
        SectionLoaded(model: model, title: title, subtitle: model.data?.context,
                      canvasURL: courseSectionURL(engine, ctx, "discussions"), load: load) { d in
            Page {
                ScreenHeading(title: title, sub: d.context, color: Color(hex: d.color))
                if d.sections.isEmpty {
                    EmptyCard(text: d.empty ?? "No discussions", symbol: "bubble.left.and.bubble.right")
                }
                ForEach(d.sections) { s in
                    CardSection(title: s.title, trailing: "\(s.rows.count)") {
                        DividedRows(data: s.rows, inset: 54) { r in PostLink(row: r, color: Color(hex: d.color)) }
                    }
                }
            }
        }
    }

    private func load() async { await model.load(engine, "discussions", ["ctx": ctx]) }
}

// MARK: - Assignments, quizzes, the syllabus

/// A course's assignments — overdue (in red), upcoming, undated, past — each with where it stands; its menu hands it in
/// or shows its feedback.
private struct AssignmentsList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<AssignmentsData>()

    var body: some View {
        let title = courseSectionTitle(engine, ctx, "assignments", "Assignments")
        SectionLoaded(model: model, title: title, subtitle: model.data?.context,
                      canvasURL: courseSectionURL(engine, ctx, "assignments"), load: load) { d in
            Page {
                ScreenHeading(title: title, sub: d.context, color: Color(hex: d.color))
                if d.sections.isEmpty {
                    EmptyCard(text: d.empty ?? "No assignments", symbol: "doc.text")
                }
                ForEach(d.sections) { s in
                    CardSection(title: s.title, trailing: "\(s.rows.count)") {
                        DividedRows(data: s.rows) { r in
                            WorkLink(row: r, color: s.title == "Overdue" ? .red : Color(hex: d.color))
                        }
                    }
                }
            }
        }
    }

    private func load() async { await model.load(engine, "assignments", ["ctx": ctx]) }
}

/// A course's quizzes, Classic and New: their points, due dates, time limits and attempts, and where each stands.
private struct QuizzesList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<RowsData>()

    var body: some View {
        let title = courseSectionTitle(engine, ctx, "quizzes", "Quizzes")
        SectionLoaded(model: model, title: title, subtitle: model.data?.context,
                      canvasURL: courseSectionURL(engine, ctx, "quizzes"), load: load) { d in
            Page {
                ScreenHeading(title: title, sub: d.context, color: Color(hex: d.color))
                if d.rows.isEmpty {
                    EmptyCard(text: d.empty ?? "No quizzes", symbol: "checklist")
                } else {
                    RowsCard {
                        DividedRows(data: d.rows) { r in WorkLink(row: r, color: Color(hex: d.color)) }
                    }
                }
            }
        }
    }

    private func load() async { await model.load(engine, "quizzes", ["ctx": ctx]) }
}

/// A course's syllabus to read, at a reading width, and every piece of its work that has a date, in date order.
private struct SyllabusView: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<RowsData>()

    var body: some View {
        let title = courseSectionTitle(engine, ctx, "syllabus", "Syllabus")
        SectionLoaded(model: model, title: title, subtitle: model.data?.context,
                      canvasURL: courseSectionURL(engine, ctx, "syllabus"), load: load) { d in
            Page(maxWidth: 920) {
                ScreenHeading(title: title, sub: d.context, color: Color(hex: d.color))
                if let html = d.html, !html.isEmpty {
                    RichText(html: html)
                        .padding(.horizontal, 22)
                        .padding(.vertical, 18)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .card()
                }
                if !d.rows.isEmpty {
                    CardSection(title: "Dated work", trailing: "\(d.rows.count)") {
                        DividedRows(data: d.rows) { r in WorkLink(row: r, color: Color(hex: d.color)) }
                    }
                }
                if d.rows.isEmpty && (d.html ?? "").isEmpty {
                    EmptyCard(text: d.empty ?? "The syllabus is empty", symbol: "list.bullet.rectangle")
                }
            }
        }
    }

    private func load() async { await model.load(engine, "syllabus", ["ctx": ctx]) }
}

// MARK: - Modules

/// A course's modules, each a card that opens under its name: its items indented as the course sets them, under their
/// sub-headings — what each asks of you (View, Submit, Mark done), a tick once it is done, why one is locked, and Mark
/// Done where it is yours to mark. Only the module last opened here opens by itself.
private struct ModulesList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<ModulesData>()
    /// (iPhone 1.5.6) Nothing opens by itself but the module last opened in this course, kept on this Mac and let go
    /// when it is closed again; others opened since stay open while the list is up.
    @State private var opened: Set<String> = []
    @State private var restored = false
    /// Marked done (or not) here, before Canvas has answered: the tick shows at once, and goes back if Canvas says no.
    @State private var marking: [String: Bool] = [:]
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var lastKey: String { "SimplModLast.\(ctx)" }

    var body: some View {
        let title = courseSectionTitle(engine, ctx, "modules", "Modules")
        SectionLoaded(model: model, title: title, subtitle: model.data?.context,
                      canvasURL: courseSectionURL(engine, ctx, "modules"), load: { await load() }) { d in
            Page {
                ScreenHeading(title: title, sub: d.context, color: Color(hex: d.color))
                if d.modules.isEmpty {
                    EmptyCard(text: d.empty ?? "No modules", symbol: "square.stack.3d.up")
                } else {
                    VStack(alignment: .leading, spacing: 12) {
                        ForEach(d.modules) { m in module(m, Color(hex: d.color)) }
                    }
                }
            }
            .onAppear { restore(d) }
        }
    }

    /// A module's items arriving under its name: a short drop and a fade (the fade alone under Reduce Motion), revealed
    /// as the card grows.
    private var unfold: AnyTransition {
        reduceMotion ? .opacity : .opacity.combined(with: .offset(y: -6))
    }

    /// A tick arriving on an item marked done.
    private var tickIn: AnyTransition {
        reduceMotion ? .opacity : .scale(scale: 0.6).combined(with: .opacity)
    }

    private func module(_ m: ModuleData, _ color: Color) -> some View {
        let open = opened.contains(m.id)
        return VStack(alignment: .leading, spacing: 0) {
            Button { toggle(m) } label: { header(m, open: open) }
                .buttonStyle(RowButtonStyle())
                .accessibilityHint(open ? "Hides the module’s items" : "Shows the module’s items")
            if open {
                items(m, color)
                    .transition(unfold)
            }
        }
        .padding(6)
        .frame(maxWidth: .infinity, alignment: .leading)
        .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous)) // (the items show as the card grows)
        .card()
    }

    private func header(_ m: ModuleData, open: Bool) -> some View {
        HStack(spacing: 10) {
            Image(systemName: "chevron.right")
                .font(.system(size: 12.5, weight: .bold))
                .foregroundStyle(.secondary)
                .rotationEffect(.degrees(open ? 90 : 0))
                .frame(width: 14)
                .accessibilityHidden(true)
            if m.locked == true {
                Image(systemName: "lock.fill")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .accessibilityLabel("Locked")
            }
            if m.done == true {
                Image(systemName: "checkmark.circle.fill")
                    .font(.sCallout)
                    .foregroundStyle(.green)
                    .accessibilityLabel("Done")
            }
            Text(m.name)
                .font(.sHeadline)
                .foregroundStyle(.primary)
                .lineLimit(2)
                .multilineTextAlignment(.leading)
            Spacer(minLength: 8)
            if let p = m.progress, !p.isEmpty {
                Text(p)
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText())
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 10)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func items(_ m: ModuleData, _ color: Color) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Divider()
                .padding(.horizontal, 10)
                .padding(.bottom, 4)
            if m.locked == true, let t = m.lockText, !t.isEmpty {
                Label(t, systemImage: "lock.fill")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
            }
            ForEach(Array(m.items.enumerated()), id: \.element.id) { i, it in
                if i > 0, it.header != true, m.items[i - 1].header != true {
                    RowDivider(inset: 48 + CGFloat(it.indent ?? 0) * 20)
                }
                item(it, m, color)
            }
            if m.items.isEmpty {
                Text("Nothing in this module yet.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 8)
            }
        }
    }

    @ViewBuilder
    private func item(_ it: ModuleItem, _ m: ModuleData, _ color: Color) -> some View {
        let pad = CGFloat(it.indent ?? 0) * 20
        if it.header == true {
            Text(it.title)
                .font(.sCallout.weight(.semibold))
                .foregroundStyle(.secondary)
                .padding(.leading, 8 + pad)
                .padding(.trailing, 8)
                .padding(.top, 10)
                .padding(.bottom, 4)
                .frame(maxWidth: .infinity, alignment: .leading)
                .accessibilityAddTraits(.isHeader)
        } else {
            let locked = it.locked == true
            let done = marking[it.id] ?? (it.done == true)
            HStack(spacing: 8) {
                OptionalRowLink(enabled: !locked && (it.url != nil || it.type == "ExternalTool")) {
                    openItem(it)
                } label: {
                    itemLabel(it, locked: locked, done: done, color: color)
                }
                if it.markable == true && !done {
                    Button("Mark Done") { mark(it, m, true) }
                        .controlSize(.small)
                        .disabled(locked)
                        .help("Mark this item done in \(engine.lmsName)")
                        .padding(.trailing, 6)
                        .transition(.opacity)
                }
            }
            .padding(.leading, pad)
            .contextMenu { itemMenu(it, m, locked: locked, done: done) }
        }
    }

    private func itemLabel(_ it: ModuleItem, locked: Bool, done: Bool, color: Color) -> some View {
        HStack(spacing: 12) {
            IconTile(symbol: locked ? "lock.fill" : Glyph.item(it.type), color: locked ? .gray : color, size: 28)
            VStack(alignment: .leading, spacing: 2) {
                Text(it.title)
                    .lineLimit(2)
                    .foregroundStyle(locked ? .secondary : .primary)
                if let s = it.sub, !s.isEmpty {
                    Text(s).font(.sCaption).foregroundStyle(.secondary)
                }
                if locked, let t = it.lockText, !t.isEmpty {
                    Text(t).font(.sCaption).foregroundStyle(.secondary).lineLimit(2)
                } else if let r = it.requirement, !r.isEmpty, !done {
                    Text(r).font(.sCaption.weight(.medium)).foregroundStyle(color)
                }
            }
            Spacer(minLength: 6)
            if done {
                Image(systemName: "checkmark.circle.fill")
                    .font(.sTitle3)
                    .foregroundStyle(.green)
                    .help("Done")
                    .accessibilityLabel("Done")
                    .transition(tickIn)
            }
            if it.external == true {
                Image(systemName: "arrow.up.right")
                    .font(.sCaption)
                    .foregroundStyle(.tertiary)
                    .help("Opens in your browser")
                    .accessibilityLabel("Opens in your browser")
            }
        }
    }

    @ViewBuilder
    private func itemMenu(_ it: ModuleItem, _ m: ModuleData, locked: Bool, done: Bool) -> some View {
        if !locked, it.url != nil || it.type == "ExternalTool" {
            Button("Open") { openItem(it) }
        }
        if it.markable == true, !locked {
            Button(done ? "Mark as Not Done" : "Mark as Done") { mark(it, m, !done) }
        }
        if let url = it.url, !url.isEmpty {
            Divider()
            if it.external != true {
                Button("Open in \(engine.lmsName)") { engine.openWebScreen(ModulesList.page(url), title: it.title) }
            }
            Button("Copy Link") {
                if let u = engine.absolute(ModulesList.page(url)) { copyToPasteboard(u.absoluteString) }
            }
        }
    }

    /// A module item's own page on Canvas: a file's page, not its download.
    private static func page(_ url: String) -> String {
        guard let r = url.range(of: "/download") else { return url }
        return String(url[..<r.lowerBound])
    }

    private func openItem(_ it: ModuleItem) {
        // (a module's tool opens in a window of its own; everything else where it leads)
        if it.type == "ExternalTool", ctx.hasPrefix("courses/") {
            engine.openTool(.moduleItem(course: ContextHome.id(of: ctx), id: it.id, title: it.title))
        } else {
            engine.go(it.url, title: it.title)
        }
    }

    private func toggle(_ m: ModuleData) {
        withAnimation(Motion.gentle) {
            if opened.contains(m.id) {
                opened.remove(m.id)
                if UserDefaults.standard.string(forKey: lastKey) == m.id { UserDefaults.standard.removeObject(forKey: lastKey) }
            } else {
                opened.insert(m.id)
                UserDefaults.standard.set(m.id, forKey: lastKey)
            }
        }
    }

    private func restore(_ d: ModulesData) {
        guard !restored else { return }
        restored = true
        if let last = UserDefaults.standard.string(forKey: lastKey), d.modules.contains(where: { $0.id == last }) { opened = [last] }
    }

    private func mark(_ it: ModuleItem, _ m: ModuleData, _ done: Bool) {
        withAnimation(Motion.snappy) { marking[it.id] = done }
        Task {
            if await engine.act("markDone", ["ctx": ctx, "module": m.id, "item": it.id, "done": done]) {
                await load(animated: true)
            }
            withAnimation(Motion.snappy) { marking[it.id] = nil } // (Canvas's own answer from here: the reload, or back as it was)
        }
    }

    private func load(animated: Bool = false) async { await model.load(engine, "modules", ["ctx": ctx], animated: animated) }
}

// MARK: - Pages

/// A course's or a group's pages, the front page first, each with when it was last edited.
private struct PagesList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<PagesData>()

    var body: some View {
        let title = courseSectionTitle(engine, ctx, "pages", "Pages")
        SectionLoaded(model: model, title: title, subtitle: model.data?.context,
                      canvasURL: courseSectionURL(engine, ctx, "pages"), load: load) { d in
            Page {
                ScreenHeading(title: title, sub: d.context, color: Color(hex: d.color))
                if d.rows.isEmpty {
                    EmptyCard(text: d.empty ?? "No pages", symbol: "doc.richtext")
                } else {
                    RowsCard {
                        DividedRows(data: d.rows) { p in
                            RowLink {
                                engine.push(.page(ctx: ctx, slug: p.slug))
                            } label: {
                                InfoRow(title: p.title, sub: p.sub, symbol: "doc.richtext", tint: Color(hex: d.color))
                            }
                            .canvasRowMenu(p.title, url: "/\(ctx)/pages/\(p.slug)", engine: engine) {
                                engine.push(.page(ctx: ctx, slug: p.slug))
                            }
                        }
                    }
                }
            }
        }
    }

    private func load() async { await model.load(engine, "pages", ["ctx": ctx]) }
}

/// A page of a course or a group (its front page when no name is given), to read at a reading width: its title, when
/// it was last edited and by whom, why it is locked if it is, and its text.
struct PageView: View {
    let ctx: String
    let slug: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<PageData>()

    var body: some View {
        Group {
            if let d = model.data {
                Page(maxWidth: 920) {
                    ScreenHeading(title: d.title, sub: d.edited, color: Color(hex: d.color))
                    if let l = d.lockText, !l.isEmpty {
                        Label(l, systemImage: "lock.fill")
                            .font(.sCallout)
                            .foregroundStyle(.orange)
                            .padding(14)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .card(tint: .orange)
                    }
                    if !d.html.isEmpty {
                        RichText(html: d.html)
                            .padding(.horizontal, 22)
                            .padding(.vertical, 18)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .card()
                    }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(model.data?.title ?? "Page")
        .navigationSubtitle(model.data?.context ?? "")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                CanvasMenu(url: canvasPath, title: model.data?.title ?? "Page")
            }
        }
        .task(id: engine.dataVersion) { await load() }
    }

    /// The page's own address on Canvas (the front page's, when it has no name).
    private var canvasPath: String {
        let s = model.data?.slug ?? slug
        return s.isEmpty ? "/\(ctx)/wiki" : "/\(ctx)/pages/\(s)"
    }

    private func load() async { await model.load(engine, "page", slug.isEmpty ? ["ctx": ctx] : ["ctx": ctx, "slug": slug]) }
}

// MARK: - Files

/// A folder of a course's or a group's files (its root is Files): the folders in it, each opening its own list, then
/// its files with their kind, size and date — a click shows one in Quick Look. A locked file is listed, not opened.
struct FilesView: View {
    let ctx: String
    let folder: String
    let name: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<FilesData>()

    var body: some View {
        SectionLoaded(model: model, title: name, subtitle: model.data?.context,
                      canvasURL: "/\(ctx)/files", load: load) { d in
            Page {
                ScreenHeading(title: name, sub: d.context, color: Color(hex: d.color))
                if d.folders.isEmpty && d.files.isEmpty {
                    EmptyCard(text: d.empty ?? "This folder is empty", symbol: "folder")
                }
                if !d.folders.isEmpty {
                    CardSection(title: "Folders", trailing: "\(d.folders.count)") {
                        DividedRows(data: d.folders) { f in folderRow(f, Color(hex: d.color)) }
                    }
                }
                if !d.files.isEmpty {
                    CardSection(title: "Files", trailing: "\(d.files.count)") {
                        DividedRows(data: d.files) { f in fileRow(f) }
                    }
                }
            }
        }
    }

    private func folderRow(_ f: FolderRow, _ color: Color) -> some View {
        RowLink {
            engine.push(.folder(ctx: ctx, id: f.id, name: f.name))
        } label: {
            InfoRow(title: f.name, sub: f.sub, symbol: f.locked == true ? "lock.fill" : "folder.fill", tint: color) {
                Image(systemName: "chevron.right")
                    .font(.sFootnote.weight(.semibold))
                    .foregroundStyle(.tertiary)
            }
        }
        .contextMenu {
            Button("Open") { engine.push(.folder(ctx: ctx, id: f.id, name: f.name)) }
        }
    }

    private func fileRow(_ f: FileRow) -> some View {
        let locked = f.locked == true
        let kind = FilesView.kind(of: f)
        let page = "/\(ctx)/files/\(f.id)" // (the file's page on Canvas: its download address carries a one-time key)
        return OptionalRowLink(enabled: !locked && f.url != nil) {
            if let u = f.url { engine.openFile(u, name: f.name) }
        } label: {
            InfoRow(title: f.name, sub: f.sub, symbol: locked ? "lock.fill" : kind.symbol, tint: locked ? .gray : kind.tint) {
                Text(kind.word)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .frame(minWidth: 100, alignment: .trailing)
            }
        }
        .contextMenu {
            if let u = f.url, !locked {
                Button("Quick Look") { engine.openFile(u, name: f.name) }
                Divider()
            }
            Button("Open in \(engine.lmsName)") { engine.openWebScreen(page, title: f.name) }
            Button("Copy Link") { if let u = engine.absolute(page) { copyToPasteboard(u.absoluteString) } }
        }
    }

    /// What a file is, as Finder would put it: its symbol, its kind in a word, its colour.
    private static func kind(of f: FileRow) -> (symbol: String, word: String, tint: Color) {
        let ext = (f.name as NSString).pathExtension.lowercased()
        switch true {
        case f.kind == "pdf" || ext == "pdf": return ("doc.richtext.fill", "PDF", .red)
        case f.kind == "image": return ("photo", "Image", .teal)
        case f.kind == "video": return ("film", "Video", .purple)
        case f.kind == "audio": return ("waveform", "Audio", .pink)
        case ["doc", "docx", "pages", "txt", "rtf"].contains(ext): return ("doc.text.fill", "Document", .blue)
        case ["ppt", "pptx", "key"].contains(ext): return ("rectangle.on.rectangle", "Presentation", .orange)
        case ["xls", "xlsx", "csv", "numbers"].contains(ext): return ("tablecells", "Spreadsheet", .green)
        case ["zip", "gz", "tar", "7z", "rar"].contains(ext): return ("doc.zipper", "Archive", .brown)
        default: return ("doc.fill", ext.isEmpty ? "File" : ext.uppercased(), .blue)
        }
    }

    private func load() async { await model.load(engine, "files", folder.isEmpty ? ["ctx": ctx] : ["ctx": ctx, "folder": folder]) }
}

// MARK: - People

/// The people of a course or a group by role, teachers first: each with their picture and pronouns, and a Message
/// button (and menu item) that writes to them.
private struct PeopleList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<PeopleData>()
    @State private var writeTo: Recipient?

    var body: some View {
        let title = courseSectionTitle(engine, ctx, "people", "People")
        SectionLoaded(model: model, title: title, subtitle: model.data?.context,
                      canvasURL: courseSectionURL(engine, ctx, "people"), load: load) { d in
            Page {
                ScreenHeading(title: title, sub: d.context, color: Color(hex: d.color))
                if d.sections.isEmpty {
                    EmptyCard(text: d.empty ?? "Nobody to show", symbol: "person.2")
                }
                ForEach(d.sections) { s in
                    CardSection(title: s.title, trailing: "\(s.rows.count)") {
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: 240), spacing: 8)], alignment: .leading, spacing: 2) {
                            ForEach(s.rows) { p in person(p) }
                        }
                    }
                }
            }
        }
        .sheet(item: $writeTo) { r in
            ComposeSheet(to: [r], context: ctx.hasPrefix("courses/") ? "course_\(ContextHome.id(of: ctx))" : nil)
                .environmentObject(engine)
        }
    }

    private func person(_ p: PersonRow) -> some View {
        HStack(spacing: 10) {
            PersonAvatar(name: p.name, avatar: p.avatar, size: 34)
            VStack(alignment: .leading, spacing: 1) {
                Text(p.name).lineLimit(1)
                if let pr = p.pronouns, !pr.isEmpty {
                    Text(pr).font(.sCaption).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer(minLength: 4)
            Button {
                writeTo = Recipient(id: p.id, name: p.name)
            } label: {
                Image(systemName: "envelope")
            }
            .buttonStyle(.borderless)
            .help("Message \(p.name)")
            .accessibilityLabel("Message \(p.name)")
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 6)
        .contentShape(Rectangle())
        .contextMenu {
            Button("Message \(p.name)…") { writeTo = Recipient(id: p.id, name: p.name) }
        }
    }

    private func load() async { await model.load(engine, "people", ["ctx": ctx]) }
}

// MARK: - A section Simpl doesn't draw

/// A section Simpl has no screen of its own for: what it is, and Canvas's own page for it in a window.
private struct OtherSection: View {
    let ctx: String
    let kind: String
    @EnvironmentObject private var engine: Engine

    var body: some View {
        let title = courseSectionTitle(engine, ctx, kind, kind.capitalized)
        let url = courseSectionURL(engine, ctx, kind)
        ContentUnavailableView {
            Label(title, systemImage: Glyph.section(kind))
        } description: {
            Text("Simpl doesn’t show this part of \(engine.lmsName) itself yet.")
        } actions: {
            Button("Open in \(engine.lmsName)") {
                if let url { engine.openWebScreen(url, title: title) }
            }
            .disabled(url == nil)
        }
        .navigationTitle(title)
    }
}

// MARK: - Pieces of the sections

/// A section's screen: its page once the answer is in (the loading or error state until then), its name and its
/// course's in the toolbar with Canvas's own page for it in the toolbar's menu — read again whenever what the screens
/// show may have changed (View ▸ Reload, a tick, a hand-in).
private struct SectionLoaded<T: Decodable, Content: View>: View {
    @ObservedObject var model: Loader<T>
    let title: String
    let subtitle: String?
    let canvasURL: String?
    let load: () async -> Void
    @ViewBuilder var content: (T) -> Content
    @EnvironmentObject private var engine: Engine

    var body: some View {
        Group {
            if let d = model.data {
                content(d)
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(title)
        .navigationSubtitle(subtitle ?? "")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                CanvasMenu(url: canvasURL, title: title)
            }
        }
        .task(id: engine.dataVersion) { await load() }
    }
}

/// A section's name as its course gives it (its tab, as the sidebar lists it), else Simpl's own.
@MainActor
private func courseSectionTitle(_ engine: Engine, _ ctx: String, _ kind: String, _ fallback: String) -> String {
    engine.sections[ctx]?.first(where: { $0.kind == kind })?.label ?? fallback
}

/// A section's own page on Canvas, for Open in Canvas and Copy Link.
@MainActor
private func courseSectionURL(_ engine: Engine, _ ctx: String, _ kind: String) -> String? {
    engine.canvasURL(for: .section(ctx, kind))?.absoluteString
}

/// A card of rows with no heading of its own (the screen's heading names them).
private struct RowsCard<Content: View>: View {
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 0) { content }
            .padding(8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card()
    }
}

/// What a section says when it has nothing in it, in a card where its rows would be.
private struct EmptyCard: View {
    let text: String
    let symbol: String

    var body: some View {
        EmptyNote(text: text, symbol: symbol)
            .padding(.horizontal, 16)
            .card()
    }
}

/// An announcement or a discussion as a row of a card: a click opens it; its menu opens it, opens Canvas's own page
/// for it, or copies its link.
private struct PostLink: View {
    let row: PostRow
    let color: Color
    @EnvironmentObject private var engine: Engine

    var body: some View {
        RowLink {
            engine.go(row.url, title: row.title)
        } label: {
            PostRowView(row: row, color: color)
        }
        .canvasRowMenu(row.title, url: row.url, engine: engine) { engine.go(row.url, title: row.title) }
    }
}

/// A piece of work as a row of a card: a click opens it; its menu hands it in (or takes the quiz, or replies) or shows
/// its feedback, opens Canvas's own page for it, or copies its link.
private struct WorkLink: View {
    let row: ARow
    let color: Color
    @EnvironmentObject private var engine: Engine

    var body: some View {
        RowLink {
            engine.go(row.url, title: row.title)
        } label: {
            ARowView(row: row, color: color)
        }
        .workMenu(WorkAction(row), engine: engine)
    }
}

/// A row that opens something when it can; when it cannot (a locked file, an item with nowhere to go) it is shown the
/// same, without the wash under the pointer or the press.
private struct OptionalRowLink<Label: View>: View {
    let enabled: Bool
    let action: () -> Void
    @ViewBuilder var label: () -> Label

    var body: some View {
        if enabled {
            RowLink(action: action, label: label)
        } else {
            label()
                .padding(.horizontal, 8)
                .padding(.vertical, 7)
                .frame(maxWidth: .infinity, alignment: .leading)
                .accessibilityElement(children: .combine)
        }
    }
}

/// A card's rows with the hairline between each two, starting where their words do.
private struct DividedRows<Data: RandomAccessCollection, Row: View>: View where Data.Element: Identifiable {
    let data: Data
    var inset: CGFloat = 50
    @ViewBuilder var row: (Data.Element) -> Row

    var body: some View {
        ForEach(data) { item in
            if item.id != data.first?.id { RowDivider(inset: inset) }
            row(item)
        }
    }
}

private extension View {
    /// The context menu of a row that opens something: open it, Canvas's own page for it in a window of its own, or
    /// its link on the pasteboard.
    func canvasRowMenu(_ title: String, url: String?, engine: Engine, open: @escaping () -> Void) -> some View {
        contextMenu {
            Button("Open", action: open)
            if let url, !url.isEmpty {
                Divider()
                Button("Open in \(engine.lmsName)") { engine.openWebScreen(url, title: title) }
                Button("Copy Link") { if let u = engine.absolute(url) { copyToPasteboard(u.absoluteString) } }
            }
        }
    }
}

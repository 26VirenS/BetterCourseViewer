import SwiftUI

/// A section of a course or a group, by the name the course home's tile carries.
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
        default: ContentUnavailableView("Not on the iPhone yet", systemImage: "square.dashed")
        }
    }
}

/// A screen of a course's data: the list once the answer is in, the loading or error state until then,
/// read again when the data may have changed and when pulled down.
struct Loaded<T: Decodable, Content: View>: View {
    @ObservedObject var model: Loader<T>
    let title: String
    let load: () async -> Void
    @ViewBuilder let content: (T) -> Content
    @EnvironmentObject private var engine: Engine

    var body: some View {
        Group {
            if let d = model.data {
                content(d).refreshable { await load() }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(title)
        .navigationBarTitleDisplayMode(.inline)
        .task(id: engine.dataVersion) { await load() }
    }
}

/// What a list says when it has nothing in it.
struct EmptyNote: View {
    let text: String
    let symbol: String

    var body: some View {
        ContentUnavailableView(text, systemImage: symbol)
    }
}

// MARK: - Announcements and discussions

struct AnnouncementsList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<PostListData>()

    var body: some View {
        Loaded(model: model, title: "Announcements", load: load) { d in
            List {
                ForEach(d.rows) { r in
                    Button { engine.go(r.url, title: r.title) } label: { PostRowView(row: r, color: Color(hex: d.color)) }
                        .buttonStyle(.plain)
                }
            }
            .listStyle(.insetGrouped)
            .overlay { if d.rows.isEmpty { EmptyNote(text: d.empty ?? "No announcements", symbol: "megaphone") } }
        }
    }

    private func load() async { await model.load(engine, "announcements", ["ctx": ctx]) }
}

struct DiscussionsList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<DiscussionsData>()

    var body: some View {
        Loaded(model: model, title: "Discussions", load: load) { d in
            List {
                ForEach(d.sections) { s in
                    Section(s.title) {
                        ForEach(s.rows) { r in
                            Button { engine.go(r.url, title: r.title) } label: { PostRowView(row: r, color: Color(hex: d.color)) }
                                .buttonStyle(.plain)
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .overlay { if d.sections.isEmpty { EmptyNote(text: d.empty ?? "No discussions", symbol: "bubble.left.and.bubble.right") } }
        }
    }

    private func load() async { await model.load(engine, "discussions", ["ctx": ctx]) }
}

// MARK: - Assignments, quizzes, the syllabus

struct AssignmentsList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<AssignmentsData>()

    var body: some View {
        Loaded(model: model, title: "Assignments", load: load) { d in
            List {
                ForEach(d.sections) { s in
                    Section {
                        ForEach(s.rows) { r in
                            Button { engine.go(r.url, title: r.title) } label: { ARowView(row: r, color: s.title == "Overdue" ? .red : Color(hex: d.color)) }
                                .buttonStyle(.plain)
                                .workSwipe(WorkAction(r), engine: engine)
                        }
                    } header: {
                        HStack {
                            Text(s.title)
                            Spacer()
                            Text("\(s.rows.count)").monospacedDigit()
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .overlay { if d.sections.isEmpty { EmptyNote(text: d.empty ?? "No assignments", symbol: "doc.text") } }
        }
    }

    private func load() async { await model.load(engine, "assignments", ["ctx": ctx]) }
}

struct QuizzesList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<RowsData>()

    var body: some View {
        Loaded(model: model, title: "Quizzes", load: load) { d in
            List {
                ForEach(d.rows) { r in
                    Button { engine.go(r.url, title: r.title) } label: { ARowView(row: r, color: Color(hex: d.color)) }
                        .buttonStyle(.plain)
                        .workSwipe(WorkAction(r), engine: engine)
                }
            }
            .listStyle(.insetGrouped)
            .overlay { if d.rows.isEmpty { EmptyNote(text: d.empty ?? "No quizzes", symbol: "checklist") } }
        }
    }

    private func load() async { await model.load(engine, "quizzes", ["ctx": ctx]) }
}

struct SyllabusView: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<RowsData>()

    var body: some View {
        Loaded(model: model, title: "Syllabus", load: load) { d in
            List {
                if let html = d.html, !html.isEmpty {
                    Section { RichText(html: html).padding(.vertical, 6) }
                }
                if !d.rows.isEmpty {
                    Section("Dated work") {
                        ForEach(d.rows) { r in
                            Button { engine.go(r.url, title: r.title) } label: { ARowView(row: r, color: Color(hex: d.color)) }
                                .buttonStyle(.plain)
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .overlay { if d.rows.isEmpty && (d.html ?? "").isEmpty { EmptyNote(text: d.empty ?? "The syllabus is empty", symbol: "list.bullet.rectangle") } }
        }
    }

    private func load() async { await model.load(engine, "syllabus", ["ctx": ctx]) }
}

// MARK: - Modules

struct ModulesList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<ModulesData>()
    /// (1.5.6) Nothing opens by itself: the one module open is the one last opened in this course, kept on this
    /// iPhone — let go when it is closed again. Others opened since stay open while the list is up.
    @State private var opened: Set<String> = []
    @State private var restored = false
    private var lastKey: String { "SimplModLast.\(ctx)" }
    /// Marked done (or not) here, before Canvas has answered: the circle fills at once, and goes back if Canvas says no.
    @State private var marking: [String: Bool] = [:]

    var body: some View {
        Loaded(model: model, title: model.data?.title ?? (engine.onBrightspace ? "Content" : "Modules"), load: load) { d in // (Brightspace's own name there, as the course's sections say it)
            List {
                ForEach(d.modules) { m in
                    Section {
                        if opened.contains(m.id) {
                            if m.locked == true, let t = m.lockText, !t.isEmpty {
                                Label(t, systemImage: "lock.fill").font(.footnote).foregroundStyle(.secondary)
                            }
                            ForEach(m.items) { it in item(it, m, Color(hex: d.color)) }
                            if m.items.isEmpty { Text("Nothing in this module yet.").font(.footnote).foregroundStyle(.secondary) }
                        }
                    } header: {
                        header(m)
                    }
                }
            }
            .listStyle(.insetGrouped)
            .overlay { if d.modules.isEmpty { EmptyNote(text: d.empty ?? "No modules", symbol: "square.stack.3d.up") } }
            .onAppear {
                guard !restored else { return }
                restored = true
                if let last = UserDefaults.standard.string(forKey: lastKey), d.modules.contains(where: { $0.id == last }) { opened = [last] }
            }
        }
    }

    private func header(_ m: ModuleData) -> some View {
        Button {
            Haptics.select()
            withAnimation(.snappy) {
                if opened.contains(m.id) {
                    opened.remove(m.id)
                    if UserDefaults.standard.string(forKey: lastKey) == m.id { UserDefaults.standard.removeObject(forKey: lastKey) }
                } else {
                    opened.insert(m.id)
                    UserDefaults.standard.set(m.id, forKey: lastKey)
                }
            }
        } label: {
            HStack(spacing: 6) {
                if m.locked == true { Image(systemName: "lock.fill").font(.caption) }
                if m.done == true { Image(systemName: "checkmark.circle.fill").foregroundStyle(.green).font(.caption) }
                Text(m.name).lineLimit(2)
                Spacer()
                if let p = m.progress, !p.isEmpty { Text(p).textCase(nil).monospacedDigit() }
                Image(systemName: "chevron.down")
                    .font(.caption.weight(.semibold))
                    .rotationEffect(.degrees(opened.contains(m.id) ? 0 : -90))
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityHint(opened.contains(m.id) ? "Hides the module's items" : "Shows the module's items")
    }

    @ViewBuilder
    private func item(_ it: ModuleItem, _ m: ModuleData, _ color: Color) -> some View {
        let pad = CGFloat(it.indent ?? 0) * 14
        if it.header == true {
            Text(it.title)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(.secondary)
                .padding(.leading, pad)
        } else {
            let locked = it.locked == true
            Button {
                // a module's tool opens in the tool sheet; everything else where it leads
                if it.type == "ExternalTool", ctx.hasPrefix("courses/") { engine.openTool(.moduleItem(course: ContextHome.id(of: ctx), id: it.id, title: it.title)) }
                else { engine.go(it.url, title: it.title) }
            } label: {
                HStack(spacing: 12) {
                    IconTile(symbol: locked ? "lock.fill" : Glyph.item(it.type), color: locked ? .gray : color, size: 28)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(it.title).lineLimit(2).foregroundStyle(locked ? .secondary : .primary)
                        if let s = it.sub, !s.isEmpty { Text(s).font(.caption).foregroundStyle(.secondary) }
                        if locked, let t = it.lockText, !t.isEmpty { Text(t).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
                        else if let r = it.requirement, !r.isEmpty, it.done != true {
                            Text(r).font(.caption.weight(.medium)).foregroundStyle(color)
                        }
                    }
                    Spacer(minLength: 6)
                    let done = marking[it.id] ?? (it.done == true)
                    if done || it.markable == true {
                        // (one symbol either way, so the circle turns into the tick rather than being swapped for it)
                        Button { mark(it, m, true) } label: {
                            Image(systemName: done ? "checkmark.circle.fill" : "circle")
                                .font(.title3)
                                .foregroundStyle(done ? Color.green : Color(.tertiaryLabel))
                                .contentTransition(.symbolEffect(.replace))
                        }
                        .buttonStyle(.plain)
                        .allowsHitTesting(!done) // (a tick is not a button: a press on it opens the item, as before)
                        .accessibilityLabel(done ? "Done" : "Mark done")
                    }
                    if it.external == true { Image(systemName: "arrow.up.right").font(.caption).foregroundStyle(.tertiary) }
                }
                .padding(.leading, pad)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .disabled(locked || (it.url == nil && it.type != "ExternalTool"))
            .swipeActions {
                if it.markable == true {
                    let done = marking[it.id] ?? (it.done == true)
                    Button { mark(it, m, !done) } label: {
                        Label(done ? "Not Done" : "Done", systemImage: done ? "arrow.uturn.backward" : "checkmark")
                    }
                    .tint(done ? .gray : .green)
                }
            }
        }
    }

    private func mark(_ it: ModuleItem, _ m: ModuleData, _ done: Bool) {
        Haptics.select()
        withAnimation(.snappy) { marking[it.id] = done }
        Task {
            if await engine.act("markDone", ["ctx": ctx, "module": m.id, "item": it.id, "done": done]) {
                if done { Haptics.success() }
                await load()
            }
            withAnimation(.snappy) { marking[it.id] = nil } // (Canvas's own answer from here: the reload, or back as it was)
        }
    }

    private func load() async { await model.load(engine, "modules", ["ctx": ctx]) }
}

// MARK: - Pages

struct PagesList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<PagesData>()

    var body: some View {
        Loaded(model: model, title: "Pages", load: load) { d in
            List {
                ForEach(d.rows) { p in
                    Button {
                        Haptics.tap()
                        engine.push(.page(ctx: ctx, slug: p.slug))
                    } label: {
                        InfoRow(title: p.title, sub: p.sub, symbol: "doc.richtext", tint: Color(hex: d.color))
                    }
                    .buttonStyle(.plain)
                }
            }
            .listStyle(.insetGrouped)
            .overlay { if d.rows.isEmpty { EmptyNote(text: d.empty ?? "No pages", symbol: "doc.richtext") } }
        }
    }

    private func load() async { await model.load(engine, "pages", ["ctx": ctx]) }
}

/// A page of a course or a group (the front page when no name is given), as text to read.
struct PageView: View {
    let ctx: String
    let slug: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<PageData>()

    var body: some View {
        Group {
            if let d = model.data {
                // (a list like the other screens, so the page's text sits at the same margins as theirs)
                List {
                    Section {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(d.title).font(.title3.weight(.bold))
                            if let e = d.edited, !e.isEmpty { Text(e).font(.footnote).foregroundStyle(.secondary) }
                        }
                        .padding(.vertical, 4)
                        if let l = d.lockText, !l.isEmpty {
                            Label(l, systemImage: "lock.fill").font(.subheadline).foregroundStyle(.orange)
                        }
                    }
                    if !d.html.isEmpty {
                        Section { RichText(html: d.html).padding(.vertical, 6) }
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await load() }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(model.data?.context ?? "Page")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    let s = model.data?.slug ?? slug
                    engine.openWebScreen(s.isEmpty ? "/\(ctx)/wiki?bcv=native" : "/\(ctx)/pages/\(s)?bcv=native", title: model.data?.title ?? "Page")
                } label: {
                    Image(systemName: "globe")
                }
                .accessibilityLabel("Open \(engine.lmsName)’s Page")
            }
        }
        .task(id: engine.dataVersion) { await load() }
    }

    private func load() async { await model.load(engine, "page", slug.isEmpty ? ["ctx": ctx] : ["ctx": ctx, "slug": slug]) }
}

// MARK: - Files

/// A folder of a course's or a group's files: folders push their own list, a file opens in the phone's viewer.
struct FilesView: View {
    let ctx: String
    let folder: String
    let name: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<FilesData>()

    var body: some View {
        Loaded(model: model, title: name, load: load) { d in
            List {
                if !d.folders.isEmpty {
                    Section {
                        ForEach(d.folders) { f in
                            Button {
                                Haptics.tap()
                                engine.push(.folder(ctx: ctx, id: f.id, name: f.name))
                            } label: {
                                InfoRow(title: f.name, sub: f.sub, symbol: f.locked == true ? "lock.fill" : "folder.fill", tint: Color(hex: d.color)) {
                                    Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
                                }
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
                if !d.files.isEmpty {
                    Section {
                        ForEach(d.files) { f in
                            Button {
                                guard let u = f.url, f.locked != true else { return }
                                Haptics.tap()
                                engine.openFile(u, name: f.name)
                            } label: {
                                InfoRow(title: f.name, sub: f.sub, symbol: f.locked == true ? "lock.fill" : FilesView.symbol(f), tint: f.locked == true ? .gray : .blue)
                            }
                            .buttonStyle(.plain)
                            .disabled(f.locked == true || f.url == nil)
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .overlay { if d.folders.isEmpty && d.files.isEmpty { EmptyNote(text: d.empty ?? "This folder is empty", symbol: "folder") } }
        }
    }

    static func symbol(_ f: FileRow) -> String {
        let ext = (f.name as NSString).pathExtension.lowercased()
        switch true {
        case f.kind == "pdf" || ext == "pdf": return "doc.richtext.fill"
        case f.kind == "image": return "photo"
        case f.kind == "video": return "film"
        case f.kind == "audio": return "waveform"
        case ["doc", "docx", "pages", "txt", "rtf"].contains(ext): return "doc.text.fill"
        case ["ppt", "pptx", "key"].contains(ext): return "rectangle.on.rectangle"
        case ["xls", "xlsx", "csv", "numbers"].contains(ext): return "tablecells"
        case ["zip", "gz", "tar", "7z", "rar"].contains(ext): return "doc.zipper"
        default: return "doc.fill"
        }
    }

    private func load() async { await model.load(engine, "files", folder.isEmpty ? ["ctx": ctx] : ["ctx": ctx, "folder": folder]) }
}

// MARK: - People

struct PeopleList: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<PeopleData>()
    @State private var writeTo: Recipient?

    var body: some View {
        Loaded(model: model, title: model.data?.title ?? (engine.onBrightspace && ctx.hasPrefix("courses/") ? "Classlist" : "People"), load: load) { d in
            List {
                ForEach(d.sections) { s in
                    Section {
                        ForEach(s.rows) { p in
                            HStack(spacing: 12) {
                                PersonAvatar(name: p.name, avatar: p.avatar, size: 36)
                                VStack(alignment: .leading, spacing: 1) {
                                    Text(p.name)
                                    if let pr = p.pronouns, !pr.isEmpty { Text(pr).font(.caption).foregroundStyle(.secondary) }
                                }
                                .separatorAtText()
                                Spacer()
                            }
                            .contentShape(Rectangle())
                            .contextMenu {
                                Button { writeTo = Recipient(id: p.id, name: p.name) } label: { Label("Message", systemImage: "envelope") }
                            }
                            .swipeActions {
                                Button { writeTo = Recipient(id: p.id, name: p.name) } label: { Label("Message", systemImage: "envelope") }.tint(.blue)
                            }
                        }
                    } header: {
                        HStack {
                            Text(s.title)
                            Spacer()
                            Text("\(s.rows.count)").monospacedDigit()
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .overlay { if d.sections.isEmpty { EmptyNote(text: d.empty ?? "Nobody to show", symbol: "person.2") } }
        }
        .sheet(item: $writeTo) { r in
            ComposeSheet(to: [r], context: ctx.hasPrefix("courses/") ? "course_\(ContextHome.id(of: ctx))" : nil)
                .environmentObject(engine)
        }
    }

    private func load() async { await model.load(engine, "people", ["ctx": ctx]) }
}

import SwiftUI

/// A course's home (or a group's), drawn by the phone: its name, colour, teachers and grade ring (pressed,
/// the course's grades and what-if scores in a sheet), a tile per section, the work still to do, the
/// latest announcements, the front page, what was handed in lately and the course's other links.
struct ContextHome: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<HomeData>()
    @State private var grades = false

    private let columns = [GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10)]

    var body: some View {
        Group {
            if let d = model.data {
                List {
                    Section { header(d) }
                    if !d.sections.isEmpty {
                        Section {
                            LazyVGrid(columns: columns, spacing: 10) {
                                ForEach(d.sections) { s in tile(s, d) }
                            }
                            .listRowInsets(EdgeInsets())
                            .listRowBackground(Color.clear)
                        }
                    }
                    if let html = d.html, !html.isEmpty {
                        Section("About") { RichText(html: html).padding(.vertical, 4) }
                    }
                    if d.kind == "courses" { workSection(d) }
                    if !d.announcements.isEmpty {
                        Section {
                            ForEach(d.announcements) { a in
                                Button { engine.openWeb(a.url, title: a.title) } label: { PostRowView(row: a, color: Color(hex: d.color)) }
                                    .buttonStyle(.plain)
                            }
                            if d.sections.contains(where: { $0.kind == "announcements" }) {
                                Button("All Announcements") { engine.push(.section(ctx: ctx, kind: "announcements")) }
                            }
                        } header: {
                            Text("Latest announcements")
                        }
                    }
                    if let f = d.front {
                        Section("Front page") {
                            Button { engine.push(.page(ctx: ctx, slug: f.slug ?? "")) } label: {
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(f.title).font(.headline)
                                    if let x = f.excerpt, !x.isEmpty { Text(x).font(.subheadline).foregroundStyle(.secondary).lineLimit(4) }
                                }
                                .padding(.vertical, 2)
                                .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                        }
                    }
                    if let done = d.done, !done.isEmpty {
                        Section("Handed in lately") {
                            ForEach(done) { r in workRow(r, d) }
                        }
                    }
                    if let more = d.more, !more.isEmpty {
                        Section("More") {
                            ForEach(more) { m in
                                Button {
                                    if let t = m.tool, d.kind == "courses" { engine.openTool(.courseTool(course: ContextHome.id(of: ctx), id: t, title: m.label)) }
                                    else { engine.go(m.url, title: m.label) }
                                } label: {
                                    InfoRow(title: m.label, symbol: m.external == true ? "puzzlepiece.extension" : "link", tint: Color(hex: d.color)) {
                                        Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
                                    }
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await load() }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(model.data?.title ?? "")
        .navigationBarTitleDisplayMode(.large)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Button { engine.openWebScreen("/\(ctx)", title: model.data?.title ?? "") } label: { Label("Open in Simpl’s Web View", systemImage: "safari") }
                    Button { engine.openWebScreen("/\(ctx)?bcv=native", title: model.data?.title ?? "") } label: { Label("Open Canvas’s Page", systemImage: "globe") }
                } label: {
                    Image(systemName: "ellipsis.circle")
                }
                .accessibilityLabel("More")
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .sheet(isPresented: $grades) {
            NavigationStack {
                CourseGradesView(courseId: ContextHome.id(of: ctx), inSheet: true)
            }
            .environmentObject(engine)
            .presentationDetents([.large])
            .presentationDragIndicator(.visible)
        }
    }

    static func id(of ctx: String) -> String { String(ctx.split(separator: "/").last ?? "") }

    private func header(_ d: HomeData) -> some View {
        let color = Color(hex: d.color)
        return HStack(alignment: .center, spacing: 14) {
            // (every line with its symbol in one column, so their words start together)
            VStack(alignment: .leading, spacing: 5) {
                if let name = d.name, name != d.title { Text(name).font(.subheadline.weight(.semibold)).lineLimit(2) }
                if let sub = d.sub, !sub.isEmpty { Fact(symbol: d.kind == "groups" ? "person.3" : "book.closed", text: sub) }
                if let t = d.teachers, !t.isEmpty { Fact(symbol: "person.crop.circle", text: t) }
                if let n = d.openCount, n > 0 { Fact(symbol: "checklist", text: "\(n) still to do", tint: color) }
            }
            Spacer(minLength: 8)
            if d.kind == "courses" {
                Button {
                    Haptics.tap()
                    grades = true
                } label: {
                    ZStack {
                        Ring(value: d.score, color: color, lineWidth: 6, key: "home:\(ctx)")
                        VStack(spacing: 0) {
                            Text(d.letter ?? (d.score == nil ? "–" : "")).font(.headline.weight(.bold)).foregroundStyle(color)
                            if let s = d.scoreText { Text(s).font(.caption2.monospacedDigit()).foregroundStyle(.secondary).minimumScaleFactor(0.7) }
                        }
                    }
                    .frame(width: 70, height: 70)
                    .contentShape(Circle())
                }
                .buttonStyle(PressScale())
                .accessibilityLabel("Grades, \(d.scoreText ?? "no score")\(d.letter.map { ", \($0)" } ?? "")")
            } else {
                IconTile(symbol: "person.3.fill", color: color, size: 52)
            }
        }
        .padding(.vertical, 4)
    }

    private func tile(_ s: SectionLink, _ d: HomeData) -> some View {
        let color = Color(hex: d.color)
        return Button {
            Haptics.tap()
            engine.push(.section(ctx: ctx, kind: s.kind))
        } label: {
            VStack(alignment: .leading, spacing: 8) {
                Image(systemName: Glyph.section(s.kind))
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundStyle(color)
                    .frame(height: 22)
                Text(s.label)
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(.primary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.75)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 12)
            .padding(.vertical, 11)
            .contentCard(cornerRadius: 16)
            .contentShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
        }
        .buttonStyle(PressScale())
    }

    @ViewBuilder
    private func workSection(_ d: HomeData) -> some View {
        Section {
            if d.open.isEmpty {
                Label("You are all caught up here.", systemImage: "checkmark.circle").foregroundStyle(.secondary)
            }
            ForEach(d.open) { r in workRow(r, d) }
            if let n = d.openCount, n > d.open.count {
                Button("All \(n) To Do") { engine.push(.section(ctx: ctx, kind: "assignments")) }
            }
        } header: {
            Text("Still to do")
        }
    }

    private func workRow(_ r: ARow, _ d: HomeData) -> some View {
        Button { engine.go(r.url, title: r.title) } label: { ARowView(row: r, color: Color(hex: d.color)) }
            .buttonStyle(.plain)
    }

    private func load() async { await model.load(engine, "home", ["ctx": ctx]) }
}

/// A piece of work in a list: its kind's icon in the course's colour, its name, its facts, where it stands.
struct ARowView: View {
    let row: ARow
    let color: Color

    var body: some View {
        InfoRow(title: row.title, sub: row.sub, symbol: Glyph.item(row.kind ?? "assignment"), tint: color) {
            if let s = row.status, !s.word.isEmpty { StatusChip(text: s.word, tone: s.kind) }
        }
        .padding(.vertical, 2)
    }
}

/// An announcement or a discussion in a list: who posted it, when, its first lines, its replies.
struct PostRowView: View {
    let row: PostRow
    var color: Color = .accentColor

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            PersonAvatar(name: row.author ?? "", avatar: row.avatar, size: 34)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    if row.unread == true {
                        Circle().fill(Color.accentColor).frame(width: 8, height: 8).accessibilityLabel("Unread")
                    }
                    Text(row.title).font(row.unread == true ? .body.weight(.semibold) : .body).lineLimit(2)
                }
                Text([row.author, row.when].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "))
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
                if let p = row.preview, !p.isEmpty {
                    Text(p).font(.subheadline).foregroundStyle(.secondary).lineLimit(2)
                }
                HStack(spacing: 8) {
                    if row.graded == true { StatusChip(text: "Graded", tone: "purple") }
                    if let n = row.replies, n > 0 {
                        Label("\(n)", systemImage: "bubble.left").font(.caption).foregroundStyle(.secondary)
                    }
                    if let n = row.unreadCount, n > 0 {
                        Text("\(n) new").font(.caption.weight(.semibold)).foregroundStyle(color)
                    }
                }
            }
            .separatorAtText()
            Spacer(minLength: 0)
        }
        .padding(.vertical, 3)
        .contentShape(Rectangle())
    }
}

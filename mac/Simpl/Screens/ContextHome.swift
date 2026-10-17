import SwiftUI

/// A course's home, or a group's — (1.2) on the page itself, no card round its head: its name, its term, its teachers
/// and how much is still to do, with its grade ring (clicked, the course's grades); its sections as a row of glass
/// pills, each a place in the sidebar; then the work still to do, the latest announcements and what it says about
/// itself, with the front page, what was handed in lately and its other links in a column beside them on a wide window
/// (under them on a narrow one).
struct ContextHome: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<HomeData>()

    var body: some View {
        Group {
            if let d = model.data {
                Page { content(d) }
                    .font(.sBody)
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(model.data?.title ?? "")
        .navigationSubtitle(model.data?.sub ?? "")
        .openTimed(ctx.hasPrefix("groups/") ? "Group home" : "Course home", ready: model.data != nil, live: model.data != nil && !model.kept, source: model.source)
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                CanvasMenu(url: "/\(ctx)", title: model.data?.title ?? "")
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .liveChecks(model, pace: .course) // (1.3.80: a course's pages checked every ten minutes while on show)
    }

    /// The id in a context: "courses/101" → "101".
    static func id(of ctx: String) -> String { String(ctx.split(separator: "/").last ?? "") }

    /// (courses-5) Whether a course's name says something none of `shown` does, compared as the engine compares names
    /// (saysMore): case, spaces, dashes, underscores, dots and a term prefix ("F26-", "SP26 ") set aside, and a name
    /// that is a part of one shown saying nothing new — "F26-MATH 021 20" under a nickname whose line already reads
    /// "MATH-021-20 · Fall 2026" is the same name twice.
    static func saysMore(_ name: String, than shown: [String]) -> Bool {
        let key = nameKey(name)
        guard !key.isEmpty else { return false }
        return !shown.contains { nameKey($0).contains(key) }
    }

    private static func nameKey(_ s: String) -> String {
        let term = "^(?:(?:F|S|W|U|FA|SP|SU|WI|SM|FL|SPR|FAL|SUM|WIN)\\d{2,4}|\\d{4}(?:FA|SP|SU|WI|F|S|U|W)?|\\d{6})[-_\\s:]+(?=\\S)"
        return s.trimmingCharacters(in: .whitespaces)
            .replacingOccurrences(of: term, with: "", options: [.regularExpression, .caseInsensitive])
            .lowercased()
            .replacingOccurrences(of: "[\\s\\-_–—.]+", with: "", options: .regularExpression)
    }

    @ViewBuilder
    private func content(_ d: HomeData) -> some View {
        let color = Color(hex: d.color)
        // (1.3.108) only what the sidebar does not list under the course already: its sections are there (the home is
        // where this is), so the pills are the course's other links — its tools and pages — and any section it leaves out
        let listed = Set((engine.sections[ctx] ?? d.sections).map(\.kind))
        let tiles = d.sections.filter { $0.kind != "home" && !listed.contains($0.kind) }
        let more = d.more ?? []
        header(d, color)
        if !tiles.isEmpty || !more.isEmpty { sectionPills(tiles, more, d, color) }
        lower(d, color)
    }

    // MARK: - The head of the home

    private func header(_ d: HomeData, _ color: Color) -> some View {
        HStack(alignment: .center, spacing: 24) {
            VStack(alignment: .leading, spacing: 12) {
                ScreenHeading(title: d.title, color: color)
                // (every line with its symbol in one column, so their words start together)
                VStack(alignment: .leading, spacing: 8) {
                    if let name = d.name, ContextHome.saysMore(name, than: [d.title] + (d.sub ?? "").components(separatedBy: " · ")) {
                        Text(name)
                            .font(.sTitle3)
                            .foregroundStyle(.secondary)
                            .lineLimit(2)
                            .textSelection(.enabled)
                    }
                    if let sub = d.sub, !sub.isEmpty {
                        Fact(symbol: d.kind == "groups" ? "person.3" : "book.closed", text: sub)
                    }
                    if let t = d.teachers, !t.isEmpty {
                        Fact(symbol: "person.crop.circle", text: t)
                    }
                    if let n = d.openCount, n > 0 {
                        Fact(symbol: "checklist", text: "\(n) still to do", tint: color)
                    }
                }
            }
            Spacer(minLength: 12)
            if d.kind == "courses" {
                gradeRing(d, color)
            } else {
                IconTile(symbol: "person.3.fill", color: color, size: 72)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func gradeRing(_ d: HomeData, _ color: Color) -> some View {
        let letter: String? = d.letter ?? (d.score == nil ? "–" : nil)
        return Button {
            engine.go(.section(ctx, "grades"))
        } label: {
            ZStack {
                Ring(value: d.score, color: color, lineWidth: 9, key: "home:\(ctx)")
                VStack(spacing: 1) {
                    if let letter {
                        Text(letter)
                            .font(.sTitle2)
                            .foregroundStyle(color)
                    }
                    if let s = d.scoreText {
                        Text(s)
                            .font(.sFootnote.monospacedDigit())
                            .foregroundStyle(.secondary)
                            .minimumScaleFactor(0.7)
                            .contentTransition(.numericText(value: d.score ?? 0))
                    }
                }
                .padding(12)
            }
            .frame(width: 104, height: 104)
            .contentShape(Circle())
        }
        .buttonStyle(RingButtonStyle())
        .help("Grades")
        .accessibilityLabel("Grades, \(d.scoreText ?? "no score")\(d.letter.map { ", \($0)" } ?? "")")
    }

    /// (1.2) The sections as glass pills that run along a line and wrap: a click opens one, its menu has Canvas's page.
    /// (1.3.107) The course's other links — its tools, Canvas's pages that have no place of their own — are pills of their
    /// own after them (they were a "More" card in the side column), a tool's with the puzzle piece, opening its window.
    private func sectionPills(_ sections: [SectionLink], _ more: [MoreLink], _ d: HomeData, _ color: Color) -> some View {
        GlassGroup(spacing: 4) {
            let flow = PillFlow(spacing: 10, lineSpacing: 10)
            flow {
                ForEach(sections) { s in
                    Button {
                        engine.go(.section(ctx, s.kind))
                    } label: {
                        Label {
                            Text(s.label)
                                .font(.sBody.weight(.medium))
                                .foregroundStyle(.primary)
                                .lineLimit(1)
                        } icon: {
                            Image(systemName: Glyph.section(s.kind))
                                .foregroundStyle(color)
                        }
                        .padding(.horizontal, 16)
                        .padding(.vertical, 10)
                    }
                    .buttonStyle(GlassPillStyle())
                    .canvasRowMenu(s.label, url: engine.canvasURL(for: .section(ctx, s.kind))?.absoluteString, engine: engine) {
                        engine.go(.section(ctx, s.kind))
                    }
                    .readsAhead { engine.readAhead(.section(ctx, s.kind)) } // (1.3.78: the section, read ahead)
                }
                ForEach(more) { m in
                    Button {
                        openMore(m, d)
                    } label: {
                        Label {
                            Text(m.label)
                                .font(.sBody.weight(.medium))
                                .foregroundStyle(.primary)
                                .lineLimit(1)
                        } icon: {
                            Image(systemName: m.tool != nil || m.external == true ? "puzzlepiece.extension" : "link")
                                .foregroundStyle(color)
                        }
                        .padding(.horizontal, 16)
                        .padding(.vertical, 10)
                    }
                    .buttonStyle(GlassPillStyle())
                    .help(m.tool != nil || m.external == true ? "Opens in a window of its own" : m.label)
                    .canvasRowMenu(m.label, url: m.url, engine: engine) { openMore(m, d) }
                }
            }
        }
    }

    // MARK: - The two columns

    /// (1.3.107) The front page in the main column — the latest announcements, at most three and a line each, just above
    /// it, or, with no front page, the announcements in its place — and what it says about itself; the work still to do
    /// and what was handed in in the side column — under the main on a narrow window, or the only one when the other is
    /// empty. (Before, the work was the main column and the front page the side's.)
    @ViewBuilder
    private func lower(_ d: HomeData, _ color: Color) -> some View {
        let main = d.front != nil || !d.announcements.isEmpty || !(d.html ?? "").isEmpty
        let side = d.kind == "courses" || !(d.done ?? []).isEmpty
        if main && side {
            let columns = SideSplit(side: 360, from: 860, gap: 26, spacing: 24)
            columns {
                VStack(alignment: .leading, spacing: 24) { mainColumn(d, color) }
                VStack(alignment: .leading, spacing: 24) { sideColumn(d, color) }
            }
        } else if main {
            VStack(alignment: .leading, spacing: 24) { mainColumn(d, color) }
        } else if side {
            VStack(alignment: .leading, spacing: 24) { sideColumn(d, color) }
        }
    }

    @ViewBuilder
    private func mainColumn(_ d: HomeData, _ color: Color) -> some View {
        if let f = d.front {
            if !d.announcements.isEmpty { compactAnnouncements(d, color) }
            frontPage(f)
        } else if !d.announcements.isEmpty {
            announcements(d, color)
        }
        if let html = d.html, !html.isEmpty {
            CardSection(title: "About", padding: 18) {
                RichText(html: html)
            }
        }
    }

    @ViewBuilder
    private func sideColumn(_ d: HomeData, _ color: Color) -> some View {
        if d.kind == "courses" { stillToDo(d, color) }
        if let done = d.done, !done.isEmpty {
            CardSection(title: "Handed In Lately") {
                DividedRows(data: done) { r in workRow(r, color) }
            }
        }
    }

    private func stillToDo(_ d: HomeData, _ color: Color) -> some View {
        CardSection(title: "Still to Do", accessory: {
            if let n = d.openCount, n > d.open.count {
                Button("All \(n)") { engine.go(.section(ctx, "assignments")) }
                    .buttonStyle(.link)
                    .font(.sCallout)
            }
        }) {
            if d.open.isEmpty {
                EmptyNote(text: "You are all caught up here.")
                    .padding(.horizontal, 8)
            }
            DividedRows(data: d.open) { r in workRow(r, color) }
        }
    }

    private func announcements(_ d: HomeData, _ color: Color) -> some View {
        CardSection(title: "Latest Announcements", accessory: {
            if d.sections.contains(where: { $0.kind == "announcements" }) {
                Button("All Announcements") { engine.go(.section(ctx, "announcements")) }
                    .buttonStyle(.link)
                    .font(.sCallout)
            }
        }) {
            DividedRows(data: d.announcements, inset: 54) { a in
                RowLink {
                    engine.openWeb(a.url, title: a.title)
                } label: {
                    PostRowView(row: a, color: color, previewLines: 3)
                }
                .canvasRowMenu(a.title, url: a.url, engine: engine) { engine.openWeb(a.url, title: a.title) }
            }
        }
    }

    /// (1.3.107) Over the front page: the latest announcements squeezed to a line each, three at most — unread ones marked
    /// and in bold, who and when at the end of the line.
    private func compactAnnouncements(_ d: HomeData, _ color: Color) -> some View {
        CardSection(title: "Announcements", accessory: {
            if d.sections.contains(where: { $0.kind == "announcements" }) {
                Button(d.announcements.count > 3 ? "All \(d.announcements.count)" : "All Announcements") { engine.go(.section(ctx, "announcements")) }
                    .buttonStyle(.link)
                    .font(.sCallout)
            }
        }) {
            DividedRows(data: Array(d.announcements.prefix(3)), inset: 40) { a in
                RowLink {
                    engine.openWeb(a.url, title: a.title)
                } label: {
                    HStack(spacing: 10) {
                        Image(systemName: "megaphone")
                            .font(.sCallout)
                            .foregroundStyle(color)
                            .frame(width: 20)
                        if a.unread == true {
                            Circle().fill(Theme.accent).frame(width: 7, height: 7).accessibilityLabel("Unread")
                        }
                        Text(a.title)
                            .font(a.unread == true ? .sBody.weight(.semibold) : .sBody)
                            .lineLimit(1)
                        Spacer(minLength: 8)
                        Text([a.author, a.when].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "))
                            .font(.sFootnote)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
                .canvasRowMenu(a.title, url: a.url, engine: engine) { engine.openWeb(a.url, title: a.title) }
            }
        }
    }

    private func frontPage(_ f: FrontPage) -> some View {
        let slug = f.slug ?? ""
        return CardSection(title: "Front Page") {
            RowLink {
                engine.push(.page(ctx: ctx, slug: slug))
            } label: {
                VStack(alignment: .leading, spacing: 5) {
                    Text(f.title).font(.sHeadline)
                    if let x = f.excerpt, !x.isEmpty {
                        Text(x).font(.sCallout).foregroundStyle(.secondary).lineLimit(14) // (1.3.107: the wide column's, so more of it)
                    }
                }
            }
            .canvasRowMenu(f.title, url: slug.isEmpty ? "/\(ctx)/wiki" : "/\(ctx)/pages/\(slug)", engine: engine) {
                engine.push(.page(ctx: ctx, slug: slug))
            }
        }
    }

    private func workRow(_ r: ARow, _ color: Color) -> some View {
        RowLink {
            engine.go(r.url, title: r.title)
        } label: {
            ARowView(row: r, color: color)
        }
        .workMenu(WorkAction(r), engine: engine)
    }

    private func openMore(_ m: MoreLink, _ d: HomeData) {
        if let t = m.tool, d.kind == "courses" {
            engine.openTool(.courseTool(course: ContextHome.id(of: ctx), id: t, title: m.label))
        } else {
            engine.go(m.url, title: m.label)
        }
    }

    private func load() async { await model.load(engine, "home", ["ctx": ctx]) }
}

// MARK: - Pieces of the home

/// The grade ring as a button: a soft wash and a little lift under the pointer, a give under a click (the wash alone
/// under Reduce Motion).
private struct RingButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        RingBody(configuration: configuration)
    }

    private struct RingBody: View {
        let configuration: ButtonStyleConfiguration
        @State private var hover = false
        @Environment(\.accessibilityReduceMotion) private var reduceMotion

        var body: some View {
            configuration.label
                .background {
                    Circle().fill(Color.primary.opacity(configuration.isPressed ? 0.08 : (hover ? 0.045 : 0)))
                }
                .scaleEffect(reduceMotion ? 1 : (configuration.isPressed ? 0.96 : (hover ? 1.03 : 1)))
                .animation(Motion.hover, value: hover)
                .animation(Motion.snappy, value: configuration.isPressed)
                .onHover { hover = $0 }
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
    /// The context menu of a row or a tile that opens something: open it, Canvas's own page for it in a window of its
    /// own, or its link on the pasteboard.
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

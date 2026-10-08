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
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                CanvasMenu(url: "/\(ctx)", title: model.data?.title ?? "")
            }
        }
        .task(id: engine.dataVersion) { await load() }
    }

    /// The id in a context: "courses/101" → "101".
    static func id(of ctx: String) -> String { String(ctx.split(separator: "/").last ?? "") }

    @ViewBuilder
    private func content(_ d: HomeData) -> some View {
        let color = Color(hex: d.color)
        let tiles = d.sections.filter { $0.kind != "home" } // (the home is where this is)
        header(d, color)
        if !tiles.isEmpty { sectionPills(tiles, color) }
        lower(d, color)
    }

    // MARK: - The head of the home

    private func header(_ d: HomeData, _ color: Color) -> some View {
        HStack(alignment: .center, spacing: 24) {
            VStack(alignment: .leading, spacing: 12) {
                ScreenHeading(title: d.title, color: color)
                // (every line with its symbol in one column, so their words start together)
                VStack(alignment: .leading, spacing: 8) {
                    if let name = d.name, !name.isEmpty, name != d.title {
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
    private func sectionPills(_ sections: [SectionLink], _ color: Color) -> some View {
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
                }
            }
        }
    }

    // MARK: - The two columns

    /// The work, the news and what it says about itself in the main column; the front page, what was handed in and the
    /// other links in the side column — under the main on a narrow window, or the only one when the other is empty.
    @ViewBuilder
    private func lower(_ d: HomeData, _ color: Color) -> some View {
        let main = d.kind == "courses" || !d.announcements.isEmpty || !(d.html ?? "").isEmpty
        let side = d.front != nil || !(d.done ?? []).isEmpty || !(d.more ?? []).isEmpty
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
        if d.kind == "courses" { stillToDo(d, color) }
        if !d.announcements.isEmpty { announcements(d, color) }
        if let html = d.html, !html.isEmpty {
            CardSection(title: "About", padding: 18) {
                RichText(html: html)
            }
        }
    }

    @ViewBuilder
    private func sideColumn(_ d: HomeData, _ color: Color) -> some View {
        if let f = d.front { frontPage(f) }
        if let done = d.done, !done.isEmpty {
            CardSection(title: "Handed In Lately") {
                DividedRows(data: done) { r in workRow(r, color) }
            }
        }
        if let more = d.more, !more.isEmpty { moreLinks(more, d, color) }
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

    private func frontPage(_ f: FrontPage) -> some View {
        let slug = f.slug ?? ""
        return CardSection(title: "Front Page") {
            RowLink {
                engine.push(.page(ctx: ctx, slug: slug))
            } label: {
                VStack(alignment: .leading, spacing: 5) {
                    Text(f.title).font(.sHeadline)
                    if let x = f.excerpt, !x.isEmpty {
                        Text(x).font(.sCallout).foregroundStyle(.secondary).lineLimit(6)
                    }
                }
            }
            .canvasRowMenu(f.title, url: slug.isEmpty ? "/\(ctx)/wiki" : "/\(ctx)/pages/\(slug)", engine: engine) {
                engine.push(.page(ctx: ctx, slug: slug))
            }
        }
    }

    private func moreLinks(_ more: [MoreLink], _ d: HomeData, _ color: Color) -> some View {
        CardSection(title: "More") {
            DividedRows(data: more) { m in
                let window = m.tool != nil || m.external == true // (a tool opens in a window of its own)
                RowLink {
                    openMore(m, d)
                } label: {
                    InfoRow(title: m.label, symbol: m.external == true ? "puzzlepiece.extension" : "link", tint: color) {
                        Image(systemName: window ? "macwindow" : "chevron.right")
                            .font(.sFootnote.weight(.semibold))
                            .foregroundStyle(.tertiary)
                    }
                }
                .canvasRowMenu(m.label, url: m.url, engine: engine) { openMore(m, d) }
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

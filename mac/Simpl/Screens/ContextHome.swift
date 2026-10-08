import SwiftUI

/// A course's home, or a group's: its name, its term, its teachers and how much is still to do, with its grade ring
/// (clicked, the course's grades); a tile for each of its sections, each a place in the sidebar; what it says about
/// itself; then, side by side on a wide window, the work still to do and the latest announcements beside the front
/// page, what was handed in lately and the course's other links.
struct ContextHome: View {
    let ctx: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<HomeData>()

    var body: some View {
        Group {
            if let d = model.data {
                Page { content(d) }
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
        ScreenHeading(title: d.title, color: color)
        header(d, color)
        if !tiles.isEmpty { sectionTiles(tiles, color) }
        if let html = d.html, !html.isEmpty {
            CardSection(title: "About") {
                RichText(html: html).padding(.horizontal, 6)
            }
        }
        lower(d, color)
    }

    // MARK: - The head of the home

    private func header(_ d: HomeData, _ color: Color) -> some View {
        HStack(alignment: .center, spacing: 18) {
            // (every line with its symbol in one column, so their words start together)
            VStack(alignment: .leading, spacing: 7) {
                if let name = d.name, !name.isEmpty, name != d.title {
                    Text(name)
                        .font(.title3.weight(.semibold))
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
            Spacer(minLength: 12)
            if d.kind == "courses" {
                gradeRing(d, color)
            } else {
                IconTile(symbol: "person.3.fill", color: color, size: 56)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    private func gradeRing(_ d: HomeData, _ color: Color) -> some View {
        let letter: String? = d.letter ?? (d.score == nil ? "–" : nil)
        return Button {
            engine.go(.section(ctx, "grades"))
        } label: {
            ZStack {
                Ring(value: d.score, color: color, lineWidth: 7, key: "home:\(ctx)")
                VStack(spacing: 0) {
                    if let letter {
                        Text(letter)
                            .font(.title3.weight(.bold))
                            .foregroundStyle(color)
                    }
                    if let s = d.scoreText {
                        Text(s)
                            .font(.caption.monospacedDigit())
                            .foregroundStyle(.secondary)
                            .minimumScaleFactor(0.7)
                            .contentTransition(.numericText(value: d.score ?? 0))
                    }
                }
                .padding(10)
            }
            .frame(width: 80, height: 80)
            .contentShape(Circle())
        }
        .buttonStyle(RingButtonStyle())
        .help("Grades")
        .accessibilityLabel("Grades, \(d.scoreText ?? "no score")\(d.letter.map { ", \($0)" } ?? "")")
    }

    private func sectionTiles(_ sections: [SectionLink], _ color: Color) -> some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 180), spacing: 12)], spacing: 12) {
            ForEach(sections) { s in
                Button {
                    engine.go(.section(ctx, s.kind))
                } label: {
                    HStack(spacing: 10) {
                        IconTile(symbol: Glyph.section(s.kind), color: color, size: 30)
                        Text(s.label)
                            .font(.body.weight(.medium))
                            .foregroundStyle(.primary)
                            .lineLimit(1)
                        Spacer(minLength: 0)
                    }
                    .padding(.horizontal, 12)
                    .padding(.vertical, 10)
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .buttonStyle(CardButtonStyle(radius: 14))
                .canvasRowMenu(s.label, url: engine.canvasURL(for: .section(ctx, s.kind))?.absoluteString, engine: engine) {
                    engine.go(.section(ctx, s.kind))
                }
            }
        }
    }

    // MARK: - The two columns

    /// The work and the news on the left; the front page, what was handed in and the other links on the right — one
    /// column under the other on a narrow window, or when one side has nothing to show.
    @ViewBuilder
    private func lower(_ d: HomeData, _ color: Color) -> some View {
        let left = d.kind == "courses" || !d.announcements.isEmpty
        let right = d.front != nil || !(d.done ?? []).isEmpty || !(d.more ?? []).isEmpty
        if left && right {
            let columns = HomeColumns()
            columns {
                VStack(alignment: .leading, spacing: 22) { leftColumn(d, color) }
                VStack(alignment: .leading, spacing: 22) { rightColumn(d, color) }
            }
        } else if left {
            leftColumn(d, color)
        } else if right {
            rightColumn(d, color)
        }
    }

    @ViewBuilder
    private func leftColumn(_ d: HomeData, _ color: Color) -> some View {
        if d.kind == "courses" { stillToDo(d, color) }
        if !d.announcements.isEmpty { announcements(d, color) }
    }

    @ViewBuilder
    private func rightColumn(_ d: HomeData, _ color: Color) -> some View {
        if let f = d.front { frontPage(f) }
        if let done = d.done, !done.isEmpty {
            CardSection(title: "Handed in lately") {
                DividedRows(data: done) { r in workRow(r, color) }
            }
        }
        if let more = d.more, !more.isEmpty { moreLinks(more, d, color) }
    }

    private func stillToDo(_ d: HomeData, _ color: Color) -> some View {
        CardSection(title: "Still to do", accessory: {
            if let n = d.openCount, n > d.open.count {
                Button("All \(n) To Do") { engine.go(.section(ctx, "assignments")) }
                    .buttonStyle(.link)
                    .font(.callout)
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
        CardSection(title: "Latest announcements", accessory: {
            if d.sections.contains(where: { $0.kind == "announcements" }) {
                Button("All Announcements") { engine.go(.section(ctx, "announcements")) }
                    .buttonStyle(.link)
                    .font(.callout)
            }
        }) {
            DividedRows(data: d.announcements, inset: 54) { a in
                RowLink {
                    engine.openWeb(a.url, title: a.title)
                } label: {
                    PostRowView(row: a, color: color)
                }
                .canvasRowMenu(a.title, url: a.url, engine: engine) { engine.openWeb(a.url, title: a.title) }
            }
        }
    }

    private func frontPage(_ f: FrontPage) -> some View {
        let slug = f.slug ?? ""
        return CardSection(title: "Front page") {
            RowLink {
                engine.push(.page(ctx: ctx, slug: slug))
            } label: {
                VStack(alignment: .leading, spacing: 4) {
                    Text(f.title).font(.headline)
                    if let x = f.excerpt, !x.isEmpty {
                        Text(x).font(.callout).foregroundStyle(.secondary).lineLimit(4)
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
                            .font(.footnote.weight(.semibold))
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

/// The home's lower half on a wide window: two columns side by side, the left a little wider; on a window too narrow
/// for two to read well (or with other than two parts), one under the other.
private struct HomeColumns: Layout {
    var spacing: CGFloat = 22
    var gap: CGFloat = 18
    /// The narrowest width two columns read well in.
    var twoFrom: CGFloat = 860
    /// The left column's share of the width.
    var share: CGFloat = 0.56

    private func split(_ width: CGFloat, _ count: Int) -> (left: CGFloat, right: CGFloat)? {
        guard count == 2, width.isFinite, width >= twoFrom else { return nil }
        let left = ((width - gap) * share).rounded()
        return (left, width - gap - left)
    }

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        guard let width = proposal.width, width.isFinite else {
            let sizes = subviews.map { $0.sizeThatFits(.unspecified) }
            let height = sizes.map(\.height).reduce(0, +) + spacing * CGFloat(max(sizes.count - 1, 0))
            return CGSize(width: sizes.map(\.width).max() ?? 0, height: height)
        }
        if let cols = split(width, subviews.count) {
            let l = subviews[0].sizeThatFits(ProposedViewSize(width: cols.left, height: nil)).height
            let r = subviews[1].sizeThatFits(ProposedViewSize(width: cols.right, height: nil)).height
            return CGSize(width: width, height: max(l, r))
        }
        let heights = subviews.map { $0.sizeThatFits(ProposedViewSize(width: width, height: nil)).height }
        return CGSize(width: width, height: heights.reduce(0, +) + spacing * CGFloat(max(heights.count - 1, 0)))
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        if let cols = split(bounds.width, subviews.count) {
            subviews[0].place(at: bounds.origin, anchor: .topLeading, proposal: ProposedViewSize(width: cols.left, height: nil))
            subviews[1].place(at: CGPoint(x: bounds.minX + cols.left + gap, y: bounds.minY), anchor: .topLeading,
                              proposal: ProposedViewSize(width: cols.right, height: nil))
            return
        }
        var y = bounds.minY
        for s in subviews {
            let size = ProposedViewSize(width: bounds.width, height: nil)
            s.place(at: CGPoint(x: bounds.minX, y: y), anchor: .topLeading, proposal: size)
            y += s.sizeThatFits(size).height + spacing
        }
    }
}

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
                Button("Open in Canvas") { engine.openWebScreen(url, title: title) }
                Button("Copy Link") { if let u = engine.absolute(url) { copyToPasteboard(u.absoluteString) } }
            }
        }
    }
}

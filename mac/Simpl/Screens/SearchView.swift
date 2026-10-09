import SwiftUI

/// Search, from the toolbar's field (1.2): the search box's sources — your courses, their work, announcements, pages,
/// discussions and files, and people — each a section of the page under its own heading, its results one card of rows,
/// in as many columns as the window holds. Glass chips across the top narrow the page to one kind; the commands whose
/// names the words start sit over the results (the field's own suggestions list them too, as you type). Typing further
/// searches again after a pause (what is shown stays, dimmed, until the new answer is in); Return in the field searches
/// at once.
struct SearchView: View {
    let query: String
    @EnvironmentObject private var engine: Engine
    @State private var data: SearchData?
    @State private var answered = ""
    @State private var searching = false
    /// One kind of result alone (its chip pressed); nil for all of them.
    @State private var only: String?

    private var q: String { query.trimmingCharacters(in: .whitespaces) }

    var body: some View {
        Page(spacing: 26) {
            ScreenHeading(title: q.isEmpty ? "Search" : "Results for “\(q)”", sub: subline)
            content
        }
        .navigationTitle("Search")
        .task(id: q) { await run() }
    }

    @ViewBuilder
    private var content: some View {
        if q.isEmpty {
            SearchStart()
        } else if let d = data, d.groups.isEmpty, !searching {
            VStack(alignment: .leading, spacing: 26) {
                SearchActions(query: q)
                ContentUnavailableView.search(text: answered)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 30)
            }
        } else if let d = data {
            results(d)
        } else {
            ProgressView()
                .controlSize(.large)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 60)
        }
    }

    private var subline: String? {
        guard let d = data, !q.isEmpty else { return nil }
        let n = d.groups.reduce(0) { $0 + $1.rows.count }
        guard n > 0 else { return nil }
        let kinds = d.groups.count
        return "\(n) \(n == 1 ? "result" : "results")" + (kinds > 1 ? " in \(kinds) kinds" : "")
    }

    private func results(_ d: SearchData) -> some View {
        // (a kind chosen that this answer no longer has: all of them again)
        let kept = only.flatMap { o in d.groups.contains(where: { $0.title == o }) ? o : nil }
        let shown = kept.map { k in d.groups.filter { $0.title == k } } ?? d.groups
        let columns = ResultColumns()
        return VStack(alignment: .leading, spacing: 26) {
            if d.groups.count > 1 { kinds(d, kept: kept) }
            SearchActions(query: q)
            columns {
                ForEach(shown) { g in ResultGroup(group: g) }
            }
        }
        .opacity(searching ? 0.55 : 1)
        .animation(.easeOut(duration: 0.15), value: searching)
        .animation(Motion.gentle, value: kept)
    }

    /// The kinds found, as glass chips: one pressed shows it alone (pressed again, or All, every kind).
    private func kinds(_ d: SearchData, kept: String?) -> some View {
        let total = d.groups.reduce(0) { $0 + $1.rows.count }
        let row = GlassGroup(spacing: 8) {
            HStack(spacing: 8) {
                KindChip(title: "All", count: total, symbol: "square.grid.2x2", on: kept == nil) { only = nil }
                ForEach(d.groups) { g in
                    KindChip(title: g.title, count: g.rows.count, symbol: SearchGlyph.of(g.title), on: kept == g.title) {
                        only = kept == g.title ? nil : g.title
                    }
                }
            }
            .padding(.vertical, 2)
        }
        return ViewThatFits(in: .horizontal) {
            row
            ScrollView(.horizontal, showsIndicators: false) { row }
        }
    }

    /// The search for what is typed, after a pause in the typing (a new letter cancels the one waiting).
    private func run() async {
        guard !q.isEmpty else {
            data = nil
            return
        }
        if data != nil { try? await Task.sleep(nanoseconds: 280_000_000) }
        guard !Task.isCancelled else { return }
        searching = true
        let d = try? await engine.call("search", ["q": q], as: SearchData.self)
        guard !Task.isCancelled else { return }
        searching = false
        answered = q
        withAnimation(data == nil ? nil : Motion.gentle) { data = d ?? SearchData(groups: []) }
    }
}

/// One kind of result: its heading on the page, its rows in one card.
private struct ResultGroup: View {
    let group: SearchGroup

    var body: some View {
        PageSection(title: group.title, trailing: "\(group.rows.count)") {
            VStack(alignment: .leading, spacing: 0) {
                ForEach(Array(group.rows.enumerated()), id: \.element.id) { i, r in
                    if i > 0 { RowDivider(inset: 62) }
                    ResultRow(row: r, symbol: SearchGlyph.of(group.title))
                }
            }
            .padding(8)
            .card()
        }
    }
}

/// A result: its kind's symbol in its course's colour, its title and the line under it, and where it opens.
private struct ResultRow: View {
    let row: SearchRow
    let symbol: String
    @EnvironmentObject private var engine: Engine

    private var external: Bool { row.external == true }

    var body: some View {
        RowLink { engine.openFromSearch(row.url, title: row.title, external: external) } label: {
            HStack(spacing: 14) {
                IconTile(symbol: symbol, color: row.color.map { Color(hex: $0) } ?? Theme.accent, size: 36)
                VStack(alignment: .leading, spacing: 3) {
                    Text(row.title)
                        .font(.sBody.weight(.medium))
                        .lineLimit(2)
                    if let s = row.sub, !s.isEmpty {
                        Text(s)
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
                Spacer(minLength: 6)
                trailing
            }
            .padding(.vertical, 2)
        }
        .contextMenu {
            Button("Open") { engine.openFromSearch(row.url, title: row.title, external: external) }
            if !external {
                Button("Open in \(engine.lmsName)") { engine.openWebScreen(row.url, title: row.title) }
            }
            Button("Copy Link") { if let u = engine.absolute(row.url) { copyToPasteboard(u.absoluteString) } }
        }
    }

    @ViewBuilder
    private var trailing: some View {
        if external {
            Image(systemName: "arrow.up.right.square")
                .font(.sCallout)
                .foregroundStyle(.tertiary)
                .help("Opens in your browser")
        } else {
            Image(systemName: "chevron.right")
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.tertiary)
        }
    }
}

/// A kind of result as a glass chip, its count beside its name; tinted while it is the one shown.
private struct KindChip: View {
    let title: String
    let count: Int
    let symbol: String
    let on: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 7) {
                Image(systemName: symbol)
                    .font(.sCallout.weight(.semibold))
                Text(title)
                    .font(.sCallout.weight(.medium))
                Text("\(count)")
                    .font(.sCallout.monospacedDigit())
                    .opacity(0.7)
            }
            // (white on the chip the accent fills while it is the one shown; its label was lost in the tint)
            .foregroundStyle(on ? Color.white : Color.primary)
            .padding(.horizontal, 14)
            .padding(.vertical, 8)
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .glassCapsule(tint: on ? Theme.accent : nil, interactive: true)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// The commands whose names the words start, as glass buttons over the results.
private struct SearchActions: View {
    let query: String
    @EnvironmentObject private var engine: Engine

    var body: some View {
        // (one that takes something gets the words with it: "Discussions" puts "/discussion dis" in the field, its list)
        let items = SearchPalette.commandItems(matching: query, engine: engine).map { (item: PaletteItem) -> PaletteItem in
            guard case .fill(let text) = item.action else { return item }
            var withWords = item
            withWords.action = .fill(text + query)
            return withWords
        }
        if !items.isEmpty {
            PageSection(title: "Commands") {
                GlassGroup(spacing: 10) {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 230), spacing: 10, alignment: .leading)], alignment: .leading, spacing: 10) {
                        ForEach(items) { CommandPill(item: $0) }
                    }
                }
            }
        }
    }
}

/// Nothing typed yet: what Search finds, and every command, each a press away.
private struct SearchStart: View {
    @EnvironmentObject private var engine: Engine

    var body: some View {
        VStack(alignment: .leading, spacing: 26) {
            Text("Find courses, assignments, quizzes, announcements, pages, discussions, files and people in \(engine.lmsName) — or type / in the search field for a command.")
                .font(.sBody)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            PageSection(title: "Commands") {
                GlassGroup(spacing: 10) {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 230), spacing: 10, alignment: .leading)], alignment: .leading, spacing: 10) {
                        ForEach(SearchPalette.commandItems(matching: "", engine: engine)) { CommandPill(item: $0) }
                    }
                }
            }
        }
    }
}

/// The kinds of result in as many columns as the width holds — one under about 900 points, up to three on a wide
/// window — each kind going to the column that is shortest so far, so the columns end near each other.
private struct ResultColumns: Layout {
    var minColumn: CGFloat = 420
    var maxColumns = 3
    var spacing: CGFloat = 28
    var rowSpacing: CGFloat = 30

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width.flatMap { $0.isFinite ? $0 : nil } ?? 900
        let frames = arrange(width, subviews)
        return CGSize(width: width, height: frames.map(\.maxY).max() ?? 0)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let frames = arrange(bounds.width, subviews)
        for (i, f) in frames.enumerated() {
            subviews[i].place(at: CGPoint(x: bounds.minX + f.minX, y: bounds.minY + f.minY), anchor: .topLeading,
                              proposal: ProposedViewSize(width: f.width, height: nil))
        }
    }

    private func arrange(_ width: CGFloat, _ subviews: Subviews) -> [CGRect] {
        let fit = Int((width + spacing) / (minColumn + spacing))
        let n = max(1, min(maxColumns, fit, max(subviews.count, 1)))
        let columnWidth = (width - spacing * CGFloat(n - 1)) / CGFloat(n)
        var heights = Array(repeating: CGFloat(0), count: n)
        var used = Array(repeating: false, count: n)
        var out: [CGRect] = []
        for s in subviews {
            let h = s.sizeThatFits(ProposedViewSize(width: columnWidth, height: nil)).height
            var c = 0
            for k in 1..<n where heights[k] < heights[c] { c = k }
            let y = used[c] ? heights[c] + rowSpacing : 0
            out.append(CGRect(x: CGFloat(c) * (columnWidth + spacing), y: y, width: columnWidth, height: h))
            heights[c] = y + h
            used[c] = true
        }
        return out
    }
}

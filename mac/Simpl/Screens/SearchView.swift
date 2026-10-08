import SwiftUI

/// Search, from the toolbar's field: the search box's sources — your courses, their work, announcements, pages,
/// discussions and files, and people — each group a card. Typing further searches again after a pause (what is shown
/// stays, dimmed, until the new answer is in); Return in the field searches at once.
struct SearchView: View {
    let query: String
    @EnvironmentObject private var engine: Engine
    @State private var data: SearchData?
    @State private var answered = ""
    @State private var searching = false

    private var q: String { query.trimmingCharacters(in: .whitespaces) }

    var body: some View {
        Page {
            ScreenHeading(title: q.isEmpty ? "Search" : "Results for “\(q)”", sub: subline)
            if q.isEmpty {
                ContentUnavailableView("Search Canvas", systemImage: "magnifyingglass", description: Text("Courses, assignments, quizzes, announcements, pages, discussions, files and people."))
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 40)
            } else if let d = data, d.groups.isEmpty, !searching {
                ContentUnavailableView.search(text: answered)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 40)
            } else if let d = data {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 380), spacing: 16, alignment: .top)], alignment: .leading, spacing: 16) {
                    ForEach(d.groups) { g in group(g) }
                }
                .opacity(searching ? 0.55 : 1)
                .animation(.easeOut(duration: 0.15), value: searching)
            } else {
                ProgressView().frame(maxWidth: .infinity).padding(.vertical, 40)
            }
        }
        .navigationTitle("Search")
        .task(id: q) { await run() }
    }

    private var subline: String? {
        guard let d = data, !q.isEmpty else { return nil }
        let n = d.groups.reduce(0) { $0 + $1.rows.count }
        return n == 0 ? nil : "\(n) \(n == 1 ? "result" : "results")"
    }

    private func group(_ g: SearchGroup) -> some View {
        CardSection(title: g.title, trailing: "\(g.rows.count)") {
            ForEach(Array(g.rows.enumerated()), id: \.element.id) { i, r in
                if i > 0 { RowDivider(inset: 46) }
                RowLink { engine.openFromSearch(r.url, title: r.title, external: r.external == true) } label: {
                    HStack(spacing: 12) {
                        IconTile(symbol: icon(g.title), color: r.color.map { Color(hex: $0) } ?? .accentColor, size: 28)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(r.title).lineLimit(2)
                            if let s = r.sub, !s.isEmpty { Text(s).font(.callout).foregroundStyle(.secondary).lineLimit(1) }
                        }
                        Spacer(minLength: 4)
                        if r.external == true {
                            Image(systemName: "arrow.up.right.square").foregroundStyle(.tertiary).help("Opens in your browser")
                        }
                    }
                }
                .contextMenu {
                    Button("Open") { engine.openFromSearch(r.url, title: r.title, external: r.external == true) }
                    if r.external != true {
                        Button("Open in Canvas") { engine.openWebScreen(r.url, title: r.title) }
                    }
                    Button("Copy Link") { if let u = engine.absolute(r.url) { copyToPasteboard(u.absoluteString) } }
                }
            }
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

    private func icon(_ group: String) -> String {
        switch group {
        case "Courses": return "books.vertical.fill"
        case "Assignments": return "doc.text.fill"
        case "Announcements": return "megaphone.fill"
        case "Pages": return "doc.richtext.fill"
        case "Discussions": return "bubble.left.and.bubble.right.fill"
        case "Files": return "folder.fill"
        case "People": return "person.crop.circle.fill"
        default: return "magnifyingglass"
        }
    }
}

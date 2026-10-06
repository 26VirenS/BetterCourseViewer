import SwiftUI

/// Notifications (from the bell): what is overdue, due soon, graded, commented on, said and announced, by
/// day; a filter by kind; a swipe to mark one read or clear it. Read and cleared marks stay on this device.
struct NotificationsView: View {
    @EnvironmentObject private var engine: Engine
    @State private var data: NotificationsData?
    @State private var error: String?
    @State private var filter = "all"

    var body: some View {
        Group {
            if let d = data {
                List {
                    Section {
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 8) {
                                chip("all", "All", d.total)
                                ForEach(d.cats) { c in chip(c.key, c.label, c.count) }
                            }
                            .padding(.vertical, 4)
                        }
                        .scrollClipDisabled() // (the first chip at the cards' edge, the row still scrolling to the screen's)
                        .listRowInsets(EdgeInsets())
                        .listRowBackground(Color.clear)
                    } footer: {
                        Text("\(d.total) \(d.total == 1 ? "notification" : "notifications") · \(d.unread) unread")
                    }
                    let days = d.days.map { day in NotifDay(title: day.title, rows: day.rows.filter { filter == "all" || $0.cat == filter }) }.filter { !$0.rows.isEmpty }
                    if days.isEmpty {
                        Section {
                            ContentUnavailableView("Nothing here", systemImage: "bell.slash", description: Text(d.total > 0 ? "Nothing in this category." : "Cleared notifications do not come back."))
                        }
                    }
                    ForEach(days) { day in
                        Section(day.title) {
                            ForEach(day.rows) { n in row(n) }
                        }
                    }
                    if let cleared = d.cleared, cleared > 0 {
                        Section {
                            Button("Restore \(cleared) cleared \(cleared == 1 ? "notification" : "notifications")") { mark([], restore: true) }
                        }
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await load(force: true) }
            } else {
                LoadState(error: error) { Task { await load() } }
            }
        }
        .navigationTitle("Notifications")
        .navigationBarTitleDisplayMode(.large)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Button { mark("all", read: true) } label: { Label("Mark All Read", systemImage: "envelope.open") }
                    Button(role: .destructive) { mark("all", gone: true) } label: { Label("Clear All", systemImage: "trash") }
                } label: { Image(systemName: "ellipsis.circle") }
            }
        }
        .task { await load() }
    }

    private func chip(_ key: String, _ label: String, _ count: Int) -> some View {
        Button {
            Haptics.select()
            withAnimation(.snappy) { filter = key }
        } label: {
            Text(count > 0 ? "\(label) \(count)" : label)
                .font(.subheadline.weight(.medium))
                .padding(.horizontal, 14)
                .padding(.vertical, 8)
                .foregroundStyle(filter == key ? Color.white : Color.primary)
                .chipBackground(on: filter == key)
        }
        .buttonStyle(.plain)
    }

    private func row(_ n: NotifRow) -> some View {
        Button {
            Haptics.tap()
            if !n.read { mark([n.id], read: true) }
            if let u = n.url { engine.openWeb(u, title: n.title) }
        } label: {
            HStack(alignment: .top, spacing: 12) {
                Image(systemName: icon(n.cat))
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(tone(n.cat))
                    .frame(width: 32, height: 32)
                    .background(tone(n.cat).opacity(0.15), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                VStack(alignment: .leading, spacing: 3) {
                    HStack(alignment: .firstTextBaseline) {
                        Text(n.title).font(.body.weight(n.read ? .regular : .semibold)).lineLimit(2)
                        Spacer(minLength: 4)
                        if !n.read { Circle().fill(Color.accentColor).frame(width: 8, height: 8) }
                    }
                    if let sub = n.sub, !sub.isEmpty { Text(sub).font(.footnote).foregroundStyle(.secondary).lineLimit(3) }
                    Text(n.catLabel).font(.caption2.weight(.semibold)).foregroundStyle(tone(n.cat))
                }
            }
            .padding(.vertical, 2)
        }
        .buttonStyle(.plain)
        .swipeActions(edge: .trailing, allowsFullSwipe: true) {
            Button(role: .destructive) { mark([n.id], gone: true) } label: { Label("Clear", systemImage: "xmark") }
            if !n.read {
                Button { mark([n.id], read: true) } label: { Label("Read", systemImage: "envelope.open") }.tint(.blue)
            }
        }
        .swipeActions(edge: .leading) {
            Button { mark([n.id], read: !n.read) } label: { Label(n.read ? "Unread" : "Read", systemImage: n.read ? "envelope.badge" : "envelope.open") }.tint(.blue)
        }
    }

    private func mark(_ ids: Any, read: Bool? = nil, gone: Bool = false, restore: Bool = false) {
        Haptics.play(gone ? "rigid" : "light")
        var args: [String: Any] = ["ids": ids]
        if let r = read { args["read"] = r }
        if gone { args["gone"] = true }
        if restore { args["restore"] = true }
        Task {
            await engine.act("notifMark", args)
            await load()
            await engine.refreshSnapshot()
        }
    }

    private func load(force: Bool = false) async {
        do {
            let d = try await engine.call("notifications", force ? ["force": true] : [:], as: NotificationsData.self)
            withAnimation(.snappy) { data = d }
            error = nil
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }

    private func icon(_ cat: String) -> String {
        switch cat {
        case "overdue": return "exclamationmark.triangle.fill"
        case "soon": return "clock.fill"
        case "graded": return "chart.bar.fill"
        case "feedback": return "text.bubble.fill"
        case "message": return "envelope.fill"
        case "discuss": return "person.2.fill"
        case "announce": return "megaphone.fill"
        default: return "gearshape.fill"
        }
    }

    private func tone(_ cat: String) -> Color {
        switch cat {
        case "overdue": return .red
        case "soon": return .orange
        case "graded": return .green
        case "feedback": return .blue
        case "message": return .teal
        case "discuss": return .purple
        case "announce": return .indigo
        default: return .gray
        }
    }
}

/// Search: the search box's sources — your courses, their work, announcements, pages, discussions and
/// files, and people — under Apple's own search field.
struct SearchView: View {
    @EnvironmentObject private var engine: Engine
    @State private var query = ""
    @State private var data: SearchData?
    @State private var searching = false
    @State private var task: Task<Void, Never>?

    var body: some View {
        List {
            if query.trimmingCharacters(in: .whitespaces).isEmpty {
                Section {
                    ContentUnavailableView("Search Canvas", systemImage: "magnifyingglass", description: Text("Courses, assignments, quizzes, announcements, pages, discussions, files and people."))
                }
            } else if let d = data, d.groups.isEmpty, !searching {
                Section { ContentUnavailableView.search(text: query) }
            } else if let d = data {
                ForEach(d.groups) { g in
                    Section(g.title) {
                        ForEach(g.rows) { r in
                            Button {
                                Haptics.tap()
                                if r.external == true, let u = URL(string: r.url) { engine.web.openExternally(u) }
                                else { engine.openWeb(r.url, title: r.title) }
                            } label: {
                                HStack(spacing: 12) {
                                    Image(systemName: icon(g.title))
                                        .foregroundStyle(r.color.map { Color(hex: $0) } ?? Color.accentColor)
                                        .frame(width: 26)
                                    VStack(alignment: .leading, spacing: 2) {
                                        Text(r.title).lineLimit(2)
                                        if let s = r.sub, !s.isEmpty { Text(s).font(.footnote).foregroundStyle(.secondary).lineLimit(1) }
                                    }
                                }
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .overlay(alignment: .top) { if searching { ProgressView().padding(.top, 12) } }
        .navigationTitle("Search")
        .searchable(text: $query, placement: .navigationBarDrawer(displayMode: .always), prompt: "Courses, work, files, people")
        .onChange(of: query) { run() }
        .onSubmit(of: .search) { run(now: true) }
    }

    private func run(now: Bool = false) {
        task?.cancel()
        let q = query.trimmingCharacters(in: .whitespaces)
        guard !q.isEmpty else { data = nil; searching = false; return }
        task = Task {
            if !now { try? await Task.sleep(nanoseconds: 280_000_000) }
            guard !Task.isCancelled else { return }
            searching = true
            let d = try? await engine.call("search", ["q": q], as: SearchData.self)
            guard !Task.isCancelled else { return }
            searching = false
            withAnimation(.snappy) { data = d ?? SearchData(groups: []) }
        }
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

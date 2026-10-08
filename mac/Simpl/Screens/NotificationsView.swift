import SwiftUI

/// Notifications: what is overdue, due soon, graded, commented on, said and announced, by day, with a filter by kind.
/// A row opens its work and is marked read; under the pointer it offers Read and Clear, and its context menu has them
/// too. Read and cleared marks stay on this Mac; cleared ones can be brought back.
struct NotificationsView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<NotificationsData>()
    @State private var filter = "all"
    @State private var confirmClear = false

    var body: some View {
        Group {
            if let d = model.data {
                Page {
                    ScreenHeading(title: "Notifications", sub: "\(d.total) \(d.total == 1 ? "notification" : "notifications") · \(d.unread) unread")
                    filters(d)
                    let days = d.days
                        .map { day in NotifDay(title: day.title, rows: day.rows.filter { filter == "all" || $0.cat == filter }) }
                        .filter { !$0.rows.isEmpty }
                    if days.isEmpty {
                        ContentUnavailableView("Nothing Here", systemImage: "bell.slash", description: Text(d.total > 0 ? "Nothing in this category." : "Cleared notifications do not come back."))
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 30)
                    }
                    ForEach(days) { day in
                        CardSection(title: day.title) {
                            ForEach(Array(day.rows.enumerated()), id: \.element.id) { i, n in
                                if i > 0 { RowDivider(inset: 52) }
                                NotificationRow(row: n, open: { open(n) }, mark: { read in mark([n.id], read: read) }, clear: { mark([n.id], gone: true) })
                                    .transition(.opacity.combined(with: .move(edge: .leading)))
                            }
                        }
                    }
                    if let cleared = d.cleared, cleared > 0 {
                        Button("Restore \(cleared) Cleared \(cleared == 1 ? "Notification" : "Notifications")") { mark([], restore: true) }
                            .buttonStyle(.link)
                    }
                }
                .animation(Motion.gentle, value: filter)
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("Notifications")
        .toolbar {
            ToolbarItemGroup(placement: .primaryAction) {
                Button { mark("all", read: true) } label: { Label("Mark All Read", systemImage: "envelope.open") }
                    .help("Mark All Read")
                    .disabled((model.data?.unread ?? 0) == 0)
                Button { confirmClear = true } label: { Label("Clear All", systemImage: "trash") }
                    .help("Clear All")
                    .disabled((model.data?.total ?? 0) == 0)
            }
        }
        .confirmationDialog("Clear all notifications?", isPresented: $confirmClear) {
            Button("Clear All", role: .destructive) { mark("all", gone: true) }
        } message: {
            Text("They can be restored from the foot of the list.")
        }
        .task(id: engine.dataVersion) { await load() }
    }

    /// The kinds as a segmented control, folding to a menu when the window is too narrow for every one.
    private func filters(_ d: NotificationsData) -> some View {
        let picker = Picker("Show", selection: $filter) {
            Text(d.total > 0 ? "All \(d.total)" : "All").tag("all")
            ForEach(d.cats) { c in
                Text(c.count > 0 ? "\(c.label) \(c.count)" : c.label).tag(c.key)
            }
        }
        .labelsHidden()
        return ViewThatFits(in: .horizontal) {
            picker.pickerStyle(.segmented).fixedSize()
            HStack {
                picker.pickerStyle(.menu).fixedSize()
                Spacer()
            }
        }
    }

    private func open(_ n: NotifRow) {
        if !n.read { mark([n.id], read: true) }
        if let u = n.url { engine.openWeb(u, title: n.title) }
    }

    private func mark(_ ids: Any, read: Bool? = nil, gone: Bool = false, restore: Bool = false) {
        var args: [String: Any] = ["ids": ids]
        if let r = read { args["read"] = r }
        if gone { args["gone"] = true }
        if restore { args["restore"] = true }
        Task {
            await engine.act("notifMark", args)
            await load(animated: true)
            await engine.refreshSnapshot()
        }
    }

    private func load(animated: Bool = false) async {
        await model.load(engine, "notifications", animated: animated)
    }
}

/// A notification: its kind's symbol on its colour, its title (heavier while unread, with the blue dot), its line and
/// its kind; under the pointer, Read and Clear at its end.
private struct NotificationRow: View {
    let row: NotifRow
    let open: () -> Void
    let mark: (Bool) -> Void
    let clear: () -> Void
    @State private var hover = false

    var body: some View {
        RowLink(action: open) {
            HStack(alignment: .top, spacing: 12) {
                IconTile(symbol: NotificationRow.icon(row.cat), color: NotificationRow.tone(row.cat), size: 32)
                VStack(alignment: .leading, spacing: 3) {
                    Text(row.title)
                        .font(.body.weight(row.read ? .regular : .semibold))
                        .lineLimit(2)
                    if let sub = row.sub, !sub.isEmpty {
                        Text(sub).font(.callout).foregroundStyle(.secondary).lineLimit(3)
                    }
                    Text(row.catLabel).font(.caption.weight(.semibold)).foregroundStyle(NotificationRow.tone(row.cat))
                }
                Spacer(minLength: 8)
                ZStack(alignment: .topTrailing) {
                    if hover {
                        HStack(spacing: 2) {
                            Button { mark(!row.read) } label: {
                                Image(systemName: row.read ? "envelope.badge" : "envelope.open")
                            }
                            .help(row.read ? "Mark Unread" : "Mark Read")
                            .accessibilityLabel(row.read ? "Mark Unread" : "Mark Read")
                            Button(action: clear) { Image(systemName: "xmark") }
                                .help("Clear")
                                .accessibilityLabel("Clear")
                        }
                        .buttonStyle(.borderless)
                        .controlSize(.small)
                        .transition(.opacity)
                    } else if !row.read {
                        Circle().fill(Color.accentColor).frame(width: 8, height: 8)
                            .padding(.top, 6)
                            .accessibilityLabel("Unread")
                            .transition(.opacity)
                    }
                }
                .frame(width: 52, alignment: .topTrailing)
            }
        }
        .onHover { h in withAnimation(Motion.hover) { hover = h } }
        .contextMenu {
            Button("Open", action: open).disabled(row.url == nil)
            Button(row.read ? "Mark Unread" : "Mark Read") { mark(!row.read) }
            Divider()
            Button("Clear", role: .destructive, action: clear)
        }
    }

    static func icon(_ cat: String) -> String {
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

    static func tone(_ cat: String) -> Color {
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

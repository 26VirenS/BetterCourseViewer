import SwiftUI

/// (1.3.67) What native-app.js `externalTools` gives: every external tool in the student's current courses.
struct ExternalToolsData: Codable {
    struct Course: Codable, Identifiable, Hashable {
        let id: String
        let name: String
        let color: String
        /// How it opens in this course: `course` with `tool` (its tab), `assignment` or `moduleItem`.
        let open: [String: String]
    }
    struct Tool: Codable, Identifiable {
        let id: String
        let name: String
        let host: String
        let kinds: [String]
        let assignments: Int
        let courses: [Course]
    }
    var rows: [Tool]
    var sub: String?
    var empty: String?
}

/// (1.3.67) External Tools, in the sidebar: every tool the school provides in the student's courses — a course's own
/// (its navigation's) and those found only in its assignments and modules — a card a tool: its name, its site, where it
/// is found, and each course it is in, a press away (Knewton Alta and Macmillan in their own popups, the rest in the
/// tool window). A filter narrows them by name, site or course.
struct ExternalToolsView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<ExternalToolsData>()
    @State private var query = ""

    var body: some View {
        Group {
            if let d = model.data {
                if d.rows.isEmpty {
                    ContentUnavailableView("No External Tools", systemImage: "puzzlepiece.extension",
                                           description: Text(d.empty ?? "None of your courses has an external tool."))
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(PageGround())
                } else {
                    Page {
                        HStack(alignment: .bottom, spacing: 16) {
                            ScreenHeading(title: "External Tools", sub: d.sub ?? "")
                            Spacer(minLength: 12)
                            // (the filter on the page: the window's toolbar already has the app's own search field)
                            HStack(spacing: 6) {
                                Image(systemName: "line.3.horizontal.decrease").foregroundStyle(.secondary)
                                TextField("Filter tools", text: $query).textFieldStyle(.plain).frame(width: 180)
                            }
                            .padding(.horizontal, 12)
                            .frame(height: 32)
                            .background(Color.primary.opacity(0.06), in: Capsule())
                        }
                        let shown = filtered(d.rows)
                        if shown.isEmpty {
                            Text("No tool matches “\(query)”.").font(.sBody).foregroundStyle(.secondary)
                        } else {
                            LazyVGrid(columns: [GridItem(.adaptive(minimum: 320, maximum: 480), spacing: 16, alignment: .top)], alignment: .leading, spacing: 16) {
                                ForEach(shown) { t in card(t) }
                            }
                        }
                    }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("External Tools")
        .task(id: engine.dataVersion) { await load() }
    }

    private func load() async { await model.load(engine, "externalTools") }

    private func filtered(_ rows: [ExternalToolsData.Tool]) -> [ExternalToolsData.Tool] {
        let q = query.trimmingCharacters(in: .whitespaces).lowercased()
        guard !q.isEmpty else { return rows }
        return rows.filter { t in
            t.name.lowercased().contains(q) || t.host.lowercased().contains(q) || t.courses.contains { $0.name.lowercased().contains(q) }
        }
    }

    private func card(_ t: ExternalToolsData.Tool) -> some View {
        let tint = Color(hex: t.courses.first?.color ?? "#8e8e93")
        return VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top, spacing: 12) {
                Text(String(t.name.prefix(1)).uppercased())
                    .font(.system(size: 19, weight: .bold))
                    .foregroundStyle(.white)
                    .frame(width: 40, height: 40)
                    .background(tint.gradient, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                VStack(alignment: .leading, spacing: 2) {
                    Text(t.name).font(.sHeadline).lineLimit(2)
                    if !t.host.isEmpty {
                        Text(t.host).font(.sCaption).foregroundStyle(.secondary).lineLimit(1)
                    }
                    Text(whereFound(t)).font(.sCaption).foregroundStyle(.secondary).lineLimit(2)
                }
                Spacer(minLength: 0)
            }
            // (each course it is in, opened there)
            WrapLayout(spacing: 6) {
                ForEach(t.courses) { c in
                    Button { open(t, in: c) } label: {
                        HStack(spacing: 6) {
                            Circle().fill(Color(hex: c.color)).frame(width: 7, height: 7)
                            Text(c.name).font(.sCallout.weight(.medium)).lineLimit(1)
                            Image(systemName: "arrow.up.forward").font(.system(size: 10, weight: .semibold)).foregroundStyle(.secondary)
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 6)
                        .background(Color.primary.opacity(0.07), in: Capsule())
                        .contentShape(Capsule())
                    }
                    .buttonStyle(.plain)
                    .help("Open \(t.name) in \(c.name)")
                }
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 18)
    }

    /// Where it was found: a course's navigation, its assignments (how many), its modules.
    private func whereFound(_ t: ExternalToolsData.Tool) -> String {
        var parts: [String] = []
        if t.kinds.contains("Course navigation") { parts.append("Course navigation") }
        if t.assignments > 0 { parts.append(t.assignments == 1 ? "1 assignment" : "\(t.assignments) assignments") }
        if t.kinds.contains("Modules") { parts.append("Modules") }
        let courses = t.courses.count == 1 ? "1 course" : "\(t.courses.count) courses"
        return ([courses] + parts).joined(separator: " · ")
    }

    /// Opened in that course: Knewton Alta's and Macmillan's assignments in their own popups, the rest in the tool window.
    private func open(_ t: ExternalToolsData.Tool, in c: ExternalToolsData.Course) {
        let label = (t.name + " " + t.host).lowercased()
        if let a = c.open["assignment"], let course = c.open["course"] {
            if label.contains("knewton") {
                engine.alta = AltaLaunch(course: course, assignment: a, title: t.name)
                return
            }
            if label.contains("macmillan") || label.contains("sapling") || label.contains("achieve.") {
                engine.macmillan = AltaLaunch(course: course, assignment: a, title: t.name)
                return
            }
        }
        engine.openTool(ToolLaunch(title: t.name, args: c.open))
    }
}

/// Views laid left to right, onto a new line when the next would not fit.
private struct WrapLayout: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 320
        var x: CGFloat = 0, y: CGFloat = 0, line: CGFloat = 0, widest: CGFloat = 0
        for v in subviews {
            let s = v.sizeThatFits(.unspecified)
            if x > 0 && x + s.width > width {
                x = 0
                y += line + spacing
                line = 0
            }
            x += s.width + spacing
            line = max(line, s.height)
            widest = max(widest, x - spacing)
        }
        return CGSize(width: min(width, widest), height: y + line)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX, y = bounds.minY, line: CGFloat = 0
        for v in subviews {
            let s = v.sizeThatFits(.unspecified)
            if x > bounds.minX && x + s.width > bounds.maxX {
                x = bounds.minX
                y += line + spacing
                line = 0
            }
            v.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(s))
            x += s.width + spacing
            line = max(line, s.height)
        }
    }
}

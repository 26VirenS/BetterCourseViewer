import AppKit
import SwiftUI

// (1.3.211) Simpl Community's tools, in the tool's popup over the window (the Community section's Browse Community):
// the site's gallery (CommunityToolStore.load), searched by name, line and maker and filtered by the format's five
// groups, each tool a card with its preview working as it will once added (CommunityToolPreview) and Add — Added once it
// is, with Open. A tool this Mac refuses says why (one of a later format: it needs a newer Simpl) and cannot be added.
// While the gallery is read, a spinner; with no connection, or an answer that is not one, why and Try Again — the
// tools already added go on working either way.

struct CommunityBrowser: View {
    @ObservedObject private var store = CommunityToolStore.shared
    // (the screenshot suite: -SimplCommunitySearch "law", -SimplCommunityGroup science)
    @State private var search = UserDefaults.standard.string(forKey: "SimplCommunitySearch") ?? ""
    @State private var group = UserDefaults.standard.string(forKey: "SimplCommunityGroup").flatMap { ToolFormat.groups.contains($0) ? $0 : nil } ?? ""

    static func groupName(_ g: String) -> String {
        switch g {
        case "study": return "Study"
        case "math": return "Math"
        case "science": return "Science"
        case "money": return "Money"
        case "everyday": return "Everyday"
        default: return g.capitalized
        }
    }

    private var shown: [CommunityToolStore.Listed] {
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines)
        return store.gallery.filter { item in
            (group.isEmpty || item.group == group)
                && (q.isEmpty || [item.name, item.summary, item.maker].contains { $0.localizedCaseInsensitiveContains(q) })
        }
    }

    /// What a search or a group that finds nothing says.
    private var noneTitle: String {
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines)
        if !q.isEmpty { return "No tools match “\(q)”" }
        return group.isEmpty ? "No tools here yet" : "No \(Self.groupName(group)) tools yet"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            CommunityHeading(symbol: "person.2.fill", color: Theme.accent, title: "Simpl Community",
                             line: "Tools students made and shared. Add one to your Tools page.") {
                Button {
                    Task { await store.load(force: true) }
                } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                        .font(.sCallout.weight(.medium))
                        .labelStyle(.iconOnly)
                        .frame(minWidth: 16)
                }
                .glassButton()
                .controlSize(.large)
                .disabled(store.phase == .loading)
                .help("Read Simpl Community’s tools again")
                .accessibilityLabel("Refresh")
            }
            bar
                .padding(.horizontal, 40)
                .padding(.bottom, 12)
                .frame(maxWidth: 1480 + 80, alignment: .leading)
                .frame(maxWidth: .infinity)
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(PageGround())
        .task { await store.load() }
    }

    /// The search and the groups: side by side, or one over the other in a narrow popup.
    private var bar: some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 16) {
                searchField
                groupPicker
                Spacer(minLength: 0)
            }
            VStack(alignment: .leading, spacing: 10) {
                searchField
                groupPicker
            }
        }
    }

    private var searchField: some View {
        HStack(spacing: 6) {
            Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
            TextField("Search tools", text: $search)
                .textFieldStyle(.roundedBorder)
                .font(.sBody)
                .controlSize(.large)
                .accessibilityLabel("Search Simpl Community’s tools")
        }
        .frame(width: 260)
    }

    private var groupPicker: some View {
        Picker("Group", selection: $group) {
            Text("All").tag("")
            ForEach(ToolFormat.groups, id: \.self) { g in
                Text(Self.groupName(g)).tag(g)
            }
        }
        .pickerStyle(.segmented)
        .labelsHidden()
        .frame(width: 460)
        .accessibilityLabel("Group")
    }

    @ViewBuilder
    private var content: some View {
        switch store.phase {
        case .idle, .loading:
            VStack(spacing: 12) {
                ProgressView()
                Text("Reading Simpl Community…")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        case .offline:
            CommunityState(symbol: "wifi.slash", title: "Simpl Community can’t be reached",
                           text: "Check your connection, then try again. The tools you’ve added work without one.") {
                Button("Try Again") { Task { await store.load(force: true) } }
                    .glassButton(prominent: true)
                    .controlSize(.large)
            }
        case .failed(let why):
            CommunityState(symbol: "exclamationmark.triangle", title: "Simpl Community’s tools didn’t load", text: why) {
                Button("Try Again") { Task { await store.load(force: true) } }
                    .glassButton(prominent: true)
                    .controlSize(.large)
            }
        case .ready where store.gallery.isEmpty:
            CommunityState(symbol: "puzzlepiece.extension", title: "No tools shared yet",
                           text: "When students share tools in Simpl Community, they show here.") {
                Link("Open Simpl Community", destination: CommunityStore.site.appendingPathComponent("community"))
                    .font(.sBody)
            }
        case .ready:
            let list = shown
            if list.isEmpty {
                CommunityState(symbol: "magnifyingglass", title: noneTitle, text: "Try another word, or every group.") {
                    Button("Show Every Tool") {
                        search = ""
                        group = ""
                    }
                    .glassButton()
                    .controlSize(.large)
                }
            } else {
                ToolPage(spacing: 16) {
                    if let problem = store.problem {
                        ToolNote(text: problem)
                    }
                    Text(toolPlural(list.count, "tool"))
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 340), spacing: 16, alignment: .top)], alignment: .leading, spacing: 16) {
                        ForEach(list) { item in CommunityGalleryCard(item: item) }
                    }
                }
            }
        }
    }
}

/// A state of the browser with nothing to list: its symbol, what it is, and what to do.
private struct CommunityState<Action: View>: View {
    let symbol: String
    let title: String
    let text: String
    @ViewBuilder var action: Action

    var body: some View {
        VStack(spacing: 12) {
            Image(systemName: symbol)
                .font(.system(size: 34, weight: .regular))
                .foregroundStyle(.secondary)
            Text(title)
                .font(.sTitle3)
                .multilineTextAlignment(.center)
            Text(text)
                .font(.sBody)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: 440)
            action
                .padding(.top, 4)
        }
        .padding(30)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

/// A tool of the gallery: its tile, name, line, maker and group; its preview, working; and Add (Added, with Open, once
/// it is). One this Mac refuses says why in place of its preview.
struct CommunityGalleryCard: View {
    let item: CommunityToolStore.Listed
    @ObservedObject private var store = CommunityToolStore.shared
    @ObservedObject private var tools = ToolsCenter.shared
    @Environment(\.engineLink) private var link

    private var byline: String { item.maker.isEmpty ? "Simpl Community" : "By \(item.maker)" }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .top, spacing: 12) {
                IconTile(symbol: item.symbol, color: item.color, size: 40)
                VStack(alignment: .leading, spacing: 2) {
                    Text(item.name.isEmpty ? "A tool" : item.name)
                        .font(.sHeadline)
                        .lineLimit(2)
                    Text(item.summary)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                    Text(byline)
                        .font(.sCaption)
                        .foregroundStyle(.tertiary)
                        .lineLimit(1)
                }
                Spacer(minLength: 8)
                if ToolFormat.groups.contains(item.group) {
                    Text(CommunityBrowser.groupName(item.group))
                        .font(.sCaption.weight(.semibold))
                        .padding(.horizontal, 9)
                        .padding(.vertical, 4)
                        .glassCapsule(tint: item.color.opacity(0.3))
                }
            }
            if let tool = item.tool {
                CommunityToolPreview(tool: tool)
                    .padding(14)
                    .background(Theme.well, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            } else if let r = item.refusal {
                ToolNote(text: r.needsNewer ? "This tool is of a later kind than this Simpl draws. Update Simpl to use it." : "This tool can’t be used here: \(r.message)",
                         symbol: r.needsNewer ? "arrow.down.circle.fill" : "exclamationmark.triangle.fill")
            }
            footer
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .card(radius: 20)
    }

    @ViewBuilder
    private var footer: some View {
        if let key = item.tool?.siteID, store.isAdded(key) {
            HStack(spacing: 10) {
                Label("Added", systemImage: "checkmark.circle.fill")
                    .font(.sCallout.weight(.semibold))
                    .foregroundStyle(.green)
                Spacer(minLength: 8)
                Button("Open") { tools.popUpCommunity(.tool(key), engine: link.engine) }
                    .glassButton()
                    .controlSize(.large)
                    .help("Open \(item.name)")
            }
        } else if let key = item.tool?.siteID {
            HStack(spacing: 10) {
                Spacer(minLength: 0)
                if store.adding == key {
                    ProgressView().controlSize(.small)
                    Text("Adding…").font(.sCallout).foregroundStyle(.secondary)
                } else {
                    Button {
                        Task { await store.add(item) }
                    } label: {
                        Label("Add", systemImage: "plus")
                            .font(.sCallout.weight(.semibold))
                    }
                    .glassButton(prominent: true, tint: item.color)
                    .controlSize(.large)
                    .disabled(store.adding != nil)
                    .help("Add \(item.name) to your Tools page")
                }
            }
        } else if item.refusal?.needsNewer == true {
            HStack {
                Spacer(minLength: 0)
                Button("Needs a Newer Simpl") {}
                    .glassButton()
                    .controlSize(.large)
                    .disabled(true)
            }
        }
    }
}

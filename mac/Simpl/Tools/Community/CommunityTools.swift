import AppKit
import SwiftUI

// (1.3.211) Simpl Community tools (docs/community-tools.md): small tools students made and shared — inputs, results
// worked out by formulas, a table and a note, data and never code — browsed in Simpl Community's gallery
// (simplcourses.com/api/community/tools, read as Appearance's Community themes are: CommunityStore.site, no cookie),
// each with a live preview drawn natively, and added to the Tools page's Community section. Add downloads the tool by its
// id, checks it again on this Mac (Tools/Model/CommunityToolFormat.swift: the format's own copy, held to the reference's
// answers by the Models job) and keeps it — in a file of the app's own, Application Support ▸ Simpl ▸
// community-tools.json, with the values last put in each — so it works with no connection. One that fails here is never
// added. An added tool opens in the tool's popup over the window, as Simpl's own tools do (ToolsCenter.popUpCommunity),
// and is removed from its tile's menu. Community tools are not pinned: a pin is one of Simpl's own tools (ToolKind).

/// What of Simpl Community the tool's popup shows: the browser, or a tool added from it (by its key, the site's id).
enum CommunityPopup: Hashable {
    case browse
    case tool(String)

    @MainActor
    var title: String {
        switch self {
        case .browse: return "Simpl Community"
        case .tool(let key): return CommunityToolStore.shared.tool(key)?.name ?? "Community Tool"
        }
    }

    /// What its popup's size is kept under, as a tool's is (`tool.<key>`).
    var sizeKey: String {
        switch self {
        case .browse: return "tool.community"
        case .tool(let key): return "tool.community.\(key)"
        }
    }
}

/// The gallery as read, and the tools added from it with the values last put in each.
@MainActor
final class CommunityToolStore: ObservableObject {
    static let shared = CommunityToolStore()

    enum Phase: Equatable {
        case idle, loading, ready, offline
        case failed(String)
    }

    /// A tool of the gallery: as the site gave it, and as this Mac checked it.
    struct Listed: Identifiable {
        let id: String
        let raw: ToolJSON
        let checked: Result<ToolFormat.Tool, ToolFormat.Refusal>

        var tool: ToolFormat.Tool? { try? checked.get() }

        var refusal: ToolFormat.Refusal? {
            if case .failure(let r) = checked { return r }
            return nil
        }

        /// Whether it can be added: checked, and with the site's id to be kept by.
        var addable: Bool { tool?.siteID != nil }

        // (what a tool refused here still shows of itself, as the reference cleans it)
        var name: String { tool?.name ?? ToolFormat.cut(ToolFormat.clean(raw["name"]), ToolFormat.Limits.name) }
        var summary: String { tool?.summary ?? ToolFormat.cut(ToolFormat.clean(raw["summary"]), ToolFormat.Limits.summary) }
        var maker: String { tool?.maker ?? ToolFormat.cut(ToolFormat.clean(raw["maker"]), ToolFormat.Limits.maker) }
        var group: String { tool?.group ?? raw["group"]?.stringValue ?? "" }
        var symbol: String { ToolFormat.sfSymbol(tool?.symbol ?? raw["symbol"]?.stringValue ?? "") }
        var color: Color { Color(hex: tool?.color ?? raw["color"]?.stringValue) }
    }

    @Published private(set) var phase: Phase = .idle
    @Published private(set) var gallery: [Listed] = []
    @Published private(set) var added: [ToolFormat.Tool] = []
    /// The values last put in each added tool: by its key, then the input's id (a number, a choice's value, a switch).
    @Published private(set) var kept: [String: [String: ToolJSON]] = [:]
    /// The tool being added now (its key), if one is.
    @Published private(set) var adding: String?
    /// Why the last tool could not be added.
    @Published var problem: String?
    /// How many times a tool's values have been put back (Reset): its form is made afresh each time.
    @Published private(set) var resets = 0

    private var pending: DispatchWorkItem?
    private var launched = false

    /// Where the tools added are kept: Application Support ▸ Simpl ▸ community-tools.json. A picture's launch
    /// (-SimplShotFile) keeps them in a file of its own that starts empty, so a picture shows what its own launch adds
    /// and nothing a picture before it added.
    private static let file: URL = {
        let picture = UserDefaults.standard.string(forKey: "SimplShotFile") != nil
        let url = Vault.folder.appendingPathComponent(picture ? "community-tools-picture.json" : "community-tools.json")
        if picture { try? FileManager.default.removeItem(at: url) }
        return url
    }()

    init() {
        guard let data = try? Data(contentsOf: Self.file), let doc = try? JSONDecoder().decode(ToolJSON.self, from: data) else { return }
        // (each checked again as it is read: a tool this copy would not take now is not drawn)
        var seen = Set<String>()
        for raw in doc["tools"]?.arrayValue ?? [] {
            guard case .success(let t) = ToolFormat.check(raw), t.siteID != nil, seen.insert(t.key).inserted else { continue }
            added.append(t)
        }
        if case .object(let all)? = doc["values"] {
            for (key, v) in all where seen.contains(key) {
                if case .object(let values) = v { kept[key] = values }
            }
        }
    }

    func tool(_ key: String) -> ToolFormat.Tool? { added.first { $0.key == key } }

    func isAdded(_ key: String) -> Bool { added.contains { $0.key == key } }

    func values(for key: String) -> [String: ToolJSON] { kept[key] ?? [:] }

    /// The values put in a tool, kept (written a moment after the last change).
    func setValues(_ values: [String: ToolJSON], for key: String) {
        guard kept[key] ?? [:] != values else { return }
        kept[key] = values.isEmpty ? nil : values
        writeSoon()
    }

    /// A tool's own values put back (its Reset): what was put in it forgotten, its fields made afresh.
    func reset(_ key: String) {
        setValues([:], for: key)
        resets += 1
    }

    /// A tool taken off the Tools page, with the values put in it (its popup closed if it is open).
    func remove(_ key: String) {
        guard let t = tool(key) else { return }
        withAnimation(Motion.gentle) { added.removeAll { $0.key == key } }
        kept[key] = nil
        write()
        NSLog("community tools: removed %@ (%@)", key, t.name)
        if ToolsCenter.shared.community == .tool(key) { ToolsCenter.shared.closePopup() }
    }

    // MARK: - The gallery

    /// The gallery read (once each time the browser is opened afresh; again with Try Again): every tool in it checked as
    /// this Mac checks one, for its preview — and its Add, which checks it again.
    func load(force: Bool = false) async {
        if phase == .loading || (phase == .ready && !force) { return }
        phase = .loading
        problem = nil
        Shot.began() // (the screenshot suite's picture waits for the gallery)
        defer { Shot.ended() }
        var req = URLRequest(url: CommunityStore.site.appendingPathComponent("api/community/tools"), cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 20)
        req.httpShouldHandleCookies = false
        do {
            let (data, res) = try await URLSession.shared.data(for: req)
            let status = (res as? HTTPURLResponse)?.statusCode ?? 0
            let body = try? JSONDecoder().decode(ToolJSON.self, from: data)
            guard status == 200, body?["ok"] != .bool(false), let list = body?["tools"]?.arrayValue else {
                phase = .failed(body?["message"]?.stringValue ?? "Simpl Community’s tools could not be read just now. Try again in a while.")
                NSLog("community tools: the gallery was not read (status %ld)", status)
                return
            }
            var ids = Set<String>()
            var read: [Listed] = []
            for (i, raw) in list.prefix(200).enumerated() {
                let id = raw["id"]?.stringValue ?? "#\(i)"
                guard ids.insert(id).inserted else { continue }
                read.append(Listed(id: id, raw: raw, checked: ToolFormat.check(raw)))
            }
            gallery = read
            phase = .ready
            NSLog("community tools: the gallery read: %ld tools, %ld to add, %ld needing a newer Simpl", read.count,
                  read.filter(\.addable).count, read.filter { $0.refusal?.needsNewer == true }.count)
        } catch {
            let code = (error as? URLError)?.code
            let offline: [URLError.Code] = [.notConnectedToInternet, .networkConnectionLost, .cannotFindHost, .cannotConnectToHost, .timedOut,
                                            .dnsLookupFailed, .dataNotAllowed, .internationalRoamingOff]
            if let code, offline.contains(code) {
                phase = .offline
            } else {
                phase = .failed("Simpl Community could not be reached. Try again in a while.")
            }
            NSLog("community tools: Simpl Community not reached (code %ld)", code?.rawValue ?? 0)
        }
    }

    /// A tool as the site has it now, by its id: the tool, gone from the site, or nothing heard.
    private enum Fetched {
        case tool(ToolJSON)
        case gone
        case unheard
    }

    private func fetch(_ id: String) async -> Fetched {
        var req = URLRequest(url: CommunityStore.site.appendingPathComponent("api/community/tools/\(id)"), cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 20)
        req.httpShouldHandleCookies = false
        guard let got = try? await URLSession.shared.data(for: req) else { return .unheard }
        let status = (got.1 as? HTTPURLResponse)?.statusCode ?? 0
        if status == 404 { return .gone }
        guard status == 200, let body = try? JSONDecoder().decode(ToolJSON.self, from: got.0), body["ok"] == .bool(true), let tool = body["tool"] else {
            return .unheard
        }
        return .tool(tool)
    }

    /// A tool of the gallery added: downloaded afresh by its id (the gallery's copy, read moments ago, when the site does
    /// not answer now), checked again on this Mac, and kept — never one that fails here.
    func add(_ item: Listed) async {
        guard adding == nil, let id = item.tool?.siteID, !isAdded(id) else { return }
        adding = id
        problem = nil
        Shot.began()
        defer {
            adding = nil
            Shot.ended()
        }
        var raw = item.raw
        switch await fetch(id) {
        case .tool(let fresh): raw = fresh
        case .gone:
            problem = "“\(item.name)” is no longer in Simpl Community."
            NSLog("community tools: %@ is gone from the gallery", id)
            return
        case .unheard: break
        }
        switch ToolFormat.check(raw) {
        case .success(let t) where t.siteID == id:
            withAnimation(Motion.gentle) { added.append(t) }
            write()
            NSLog("community tools: added %@ (%@, version %@)", id, t.name, t.version.map { ToolFormat.jsString($0) } ?? "none")
        case .success:
            problem = "“\(item.name)” was not added: Simpl Community sent another tool in its place."
            NSLog("community tools: %@ not added (another id)", id)
        case .failure(let r):
            problem = "“\(item.name)” was not added: \(r.needsNewer ? "it needs a newer Simpl." : r.message)"
            NSLog("community tools: %@ not added (%@ at %@)", id, r.code, r.path)
        }
    }

    // MARK: - Kept

    private func writeSoon() {
        pending?.cancel()
        let work = DispatchWorkItem { [weak self] in
            Task { @MainActor in self?.write() }
        }
        pending = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5, execute: work)
    }

    /// The tools added, as checked, and their values: written at once (a typed value, a moment after the last key).
    private func write() {
        pending?.cancel()
        pending = nil
        let doc: ToolJSON = .object([
            "format": .number(Double(ToolFormat.format)),
            "tools": .array(added.map(\.json)),
            "values": .object(kept.mapValues { ToolJSON.object($0) }),
        ])
        guard let data = try? JSONEncoder().encode(doc) else { return }
        try? data.write(to: Self.file, options: .atomic)
    }

    // MARK: - The screenshot suite

    /// A tool's results as it opens, on the console (the screenshot suite's check that an added tool worked out):
    /// "community tools: mockohms worked out: Current 3 A · Power 36 W".
    func said(_ tool: ToolFormat.Tool) {
        let shown = ToolFormat.run(tool, given: values(for: tool.key)).filter(\.shown).map { a in a.unit.isEmpty ? "\(a.label) \(a.text)" : "\(a.label) \(a.text) \(a.unit)" }
        NSLog("community tools: %@ worked out: %@", tool.key, shown.joined(separator: " · "))
    }

    /// (the screenshot suite) At launch, once: -SimplCommunityToolsAdd "mockohms|mocktip" adds those tools of the gallery
    /// (by id or name) as their Add would; -SimplCommunityOpen browse opens the browser — or an added tool's id or name,
    /// that tool — in the tool's popup over the main window, (-SimplCommunityValues "bill=64.4,up=true") given those
    /// values; -SimplCommunityGroup and -SimplCommunitySearch are the browser's filter and search to begin with.
    func launch(_ engine: Engine) {
        guard !launched else { return }
        launched = true
        let d = UserDefaults.standard
        let names = (d.string(forKey: "SimplCommunityToolsAdd") ?? "").split(separator: "|").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        let open = d.string(forKey: "SimplCommunityOpen")
        let given = d.string(forKey: "SimplCommunityValues")
        guard !names.isEmpty || open != nil else { return }
        Shot.began()
        Task {
            if !names.isEmpty {
                await load()
                for n in names {
                    if let item = gallery.first(where: { $0.id == n || $0.name == n }) {
                        await add(item)
                    } else {
                        NSLog("community tools: no tool %@ in the gallery", n)
                    }
                }
            }
            if open == "browse" {
                ToolsCenter.shared.popUpCommunity(.browse, engine: engine)
            } else if let open, let t = added.first(where: { $0.key == open || $0.name == open }) {
                if let given { setValues(Self.launchValues(given, for: t), for: t.key) }
                ToolsCenter.shared.popUpCommunity(.tool(t.key), engine: engine)
            }
            Shot.ended()
        }
    }

    /// "bill=64.4,people=3,up=true": the values for a tool's inputs (a switch's true or false, else a number).
    private static func launchValues(_ text: String, for tool: ToolFormat.Tool) -> [String: ToolJSON] {
        var out: [String: ToolJSON] = [:]
        for part in text.split(separator: ",") {
            let kv = part.split(separator: "=", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) }
            guard kv.count == 2, let input = tool.inputs.first(where: { $0.id == kv[0] }) else { continue }
            if input.kind == .toggle {
                out[input.id] = .bool(["true", "yes", "on", "1"].contains(kv[1].lowercased()))
            } else if let v = StudyMath.number(kv[1]) {
                out[input.id] = .number(v)
            }
        }
        return out
    }
}

// MARK: - The Tools page's Community section

/// (1.3.211) The Tools page's Community section: the tools added from Simpl Community, each a tile in its colour, and a
/// tile to browse for more — that one alone until one is added. `-SimplToolsScroll community` scrolls to it.
struct CommunityToolsSection: View {
    @ObservedObject private var store = CommunityToolStore.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Community")
                    .font(.sTitle3)
                    .accessibilityAddTraits(.isHeader)
                Text(store.added.isEmpty
                     ? "Tools students made and shared in Simpl Community. Add one and it is here, working even offline."
                     : "Tools you added from Simpl Community. They work offline; remove one from its menu.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            LazyVGrid(columns: ToolTile.columns, spacing: 14) {
                ForEach(store.added, id: \.key) { t in CommunityToolTile(tool: t) }
                CommunityBrowseTile()
            }
        }
        .id("community")
    }
}

/// An added tool's tile: its symbol on a wash of its colour, its name, its line and who made it; a press opens it, its
/// menu opens or removes it (asked first).
struct CommunityToolTile: View {
    let tool: ToolFormat.Tool
    @ObservedObject private var tools = ToolsCenter.shared
    @Environment(\.engineLink) private var link
    @State private var asking = false

    private var byline: String { tool.maker.isEmpty ? "From Simpl Community" : "By \(tool.maker) · Simpl Community" }

    var body: some View {
        Button {
            tools.popUpCommunity(.tool(tool.key), engine: link.engine)
        } label: {
            HStack(alignment: .top, spacing: 12) {
                IconTile(symbol: ToolFormat.sfSymbol(tool.symbol), color: Color(hex: tool.color), size: 44)
                VStack(alignment: .leading, spacing: 3) {
                    Text(tool.name)
                        .font(.sHeadline)
                        .foregroundStyle(.primary)
                    Text(tool.summary)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                    Text(byline)
                        .font(.sCaption)
                        .foregroundStyle(.tertiary)
                        .lineLimit(1)
                }
                Spacer(minLength: 12)
            }
            .padding(16)
            .frame(maxWidth: .infinity, minHeight: 84, alignment: .topLeading)
        }
        .buttonStyle(CardButtonStyle(radius: 20))
        .contextMenu {
            Button("Open") { tools.popUpCommunity(.tool(tool.key), engine: link.engine) }
            Divider()
            Button("Remove from Tools…", role: .destructive) { asking = true }
        }
        .confirmationDialog("Remove “\(tool.name)”?", isPresented: $asking, titleVisibility: .visible) {
            Button("Remove", role: .destructive) { CommunityToolStore.shared.remove(tool.key) }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("It goes from your Tools page, with the values you put in. You can add it again from Simpl Community.")
        }
        .accessibilityLabel("\(tool.name). \(tool.summary). \(byline)")
    }
}

/// The section's last tile: Simpl Community's gallery opened, to add a tool from.
struct CommunityBrowseTile: View {
    @ObservedObject private var tools = ToolsCenter.shared
    @Environment(\.engineLink) private var link

    var body: some View {
        Button {
            tools.popUpCommunity(.browse, engine: link.engine)
        } label: {
            HStack(alignment: .top, spacing: 12) {
                IconTile(symbol: "plus.rectangle.on.rectangle", color: Theme.accent, size: 44)
                VStack(alignment: .leading, spacing: 3) {
                    Text("Browse Community")
                        .font(.sHeadline)
                        .foregroundStyle(.primary)
                    Text("Tools other students made: add one to this page.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 12)
            }
            .padding(16)
            .frame(maxWidth: .infinity, minHeight: 84, alignment: .topLeading)
        }
        .buttonStyle(CardButtonStyle(radius: 20))
        .help("Browse the tools shared in Simpl Community")
        .accessibilityLabel("Browse Community. Tools other students made: add one to this page.")
    }
}

// MARK: - In the tool's popup

/// What the tool's popup shows of Simpl Community (ToolsCenter.popUpCommunity): the browser or an added tool, switched
/// in place — a cross-fade — when another is opened in it; the last one still drawn as the popup goes.
struct CommunityPopupView: View {
    @ObservedObject private var tools = ToolsCenter.shared
    @State private var last: CommunityPopup?

    var body: some View {
        ZStack {
            if let c = tools.community ?? last {
                screen(c)
                    .id(c)
                    .transition(.opacity)
            }
        }
        .animation(Motion.gentle, value: tools.community)
        .onAppear { last = tools.community }
        .onChange(of: tools.community) { _, c in
            if let c { last = c }
        }
    }

    @ViewBuilder
    private func screen(_ c: CommunityPopup) -> some View {
        switch c {
        case .browse: CommunityBrowser()
        case .tool(let key): CommunityToolScreen(key: key)
        }
    }
}

/// The heading of what Simpl Community shows in the tool's popup, as a tool's heading is (ToolHeader): its tile, its
/// name and line, what it adds (`extra`) and — at its right end — the popup's ×. Dragged, the popup moves.
struct CommunityHeading<Extra: View>: View {
    let symbol: String
    let color: Color
    let title: String
    let line: String
    @ViewBuilder var extra: Extra
    @ObservedObject private var tools = ToolsCenter.shared

    var body: some View {
        HStack(alignment: .center, spacing: 14) {
            IconTile(symbol: symbol, color: color, size: 44)
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.sTitle)
                    .lineLimit(1)
                Text(line)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            .layoutPriority(1)
            Spacer(minLength: 12)
            extra
            Button {
                tools.closePopup()
            } label: {
                Label("Close", systemImage: "xmark")
                    .font(.sCallout.weight(.semibold))
                    .labelStyle(.iconOnly)
                    .frame(minWidth: 16)
            }
            .glassButton()
            .controlSize(.large)
            .help("Close \(title) (Esc)")
            .accessibilityLabel("Close \(title)")
        }
        .padding(.horizontal, 40)
        .padding(.top, 20)
        .padding(.bottom, 12)
        .frame(maxWidth: 1480 + 80)
        .frame(maxWidth: .infinity)
        .popupHeader()
        .zIndex(2)
    }
}

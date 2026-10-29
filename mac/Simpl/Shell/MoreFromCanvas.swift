import AppKit
import SwiftUI

// More from Canvas: what the school added to Canvas's own navigation that no list of Canvas's API gives — an account's
// tools with a place in that navigation ("My Materials", a library, Studio), History, Help, sometimes a link — shown as
// the sidebar's last section, under the app's own places, in Canvas's order, as the web's sidebar shows it (extras.js).
// The engine reads it from the page's own menu, or the last one it saw (native-app.js `moreFromCanvas`); a school that
// adds nothing has no section. History and Help open as popups over the window that never block it, listing the rows
// the web's sheets list (`moreHistory`, `moreHelp`), read afresh each time; a tool opens in the tool window on Canvas's
// launch page for it, without Canvas's page round it (light, as every tool page opens); a link opens its page in the
// app's own browser window. On Brightspace: the links its navbar offers that the app does not draw itself, as "More
// from Brightspace", or no section.

/// The sidebar's last section, as the engine answers it (native-app.js `moreFromCanvas`).
struct MoreNav: Codable, Equatable {
    /// The section's header: "More from Canvas" ("More from Brightspace").
    var title: String
    /// In Canvas's order.
    var items: [MoreItem]
    /// Where they were read: this page's own menu ("page"), the last one seen ("saved"), or nowhere ("none").
    var from: String?
}

/// An item of the section: History, Help, a tool of the school's, or a link.
struct MoreItem: Codable, Equatable, Identifiable {
    var id: String
    /// history, help, tool or link.
    var kind: String
    var label: String
    /// What a press does, for its tooltip ("Opens in its own window", "Opens library.example.edu").
    var note: String?
    var href: String?
    /// A tool's icon as Canvas gives it: an address, or a drawing of Canvas's as a data: address.
    var icon: String?
    /// The same icon drawn by the school's page as a 64-pixel PNG (a data: address), so it shows whatever it was — an
    /// SVG, a picture only the signed-in page may read — with nothing fetched here.
    var iconData: String?
    /// The colour a tool's icon is drawn on: the school's nav colour, as its icons are made for it (often white).
    var tile: String?
    /// A tool or a link: it opens in a window of its own (the mark at the row's end says so).
    var external: Bool?
    /// What a press does: history, help (their popups), tool (the tool window on `url`), browser (`url` in the app's
    /// browser window).
    var open: String
    var url: String?
}

/// History or Help as its popup lists them (native-app.js `moreHistory`, `moreHelp`).
struct MoreListData: Decodable {
    var title: String
    var note: String?
    var empty: String?
    /// (Help) What was left out and why — reporting a problem needs Canvas's own form — or nothing.
    var foot: String?
    var rows: [MoreListRow]
}

/// A row of History or Help.
struct MoreListRow: Decodable, Identifiable {
    var id: String
    var title: String
    /// Its second line: (History) the course as the sidebar names it, what it is, and when.
    var sub: String?
    /// (History) Canvas's name for what it is: assignment, quiz, discussion, page…
    var kind: String?
    /// (History) Its course's colour, when it is one of the student's.
    var color: String?
    var url: String
    /// place: the school's own address, opened as the app opens any (its screen here where it has one, else Canvas's
    /// page); browser: another site, in the app's browser window.
    var open: String
}

/// The two lists the section opens as popups.
enum MoreList: String, Identifiable {
    case history, help

    var id: String { rawValue }

    /// The engine's call that reads it.
    var call: String { self == .history ? "moreHistory" : "moreHelp" }

    /// Its title and line while it is read (the engine's own words replace them).
    var title: String { self == .history ? "History" : "Help" }
    var note: String { self == .history ? "What you opened lately." : "Your school’s help links." }
}

extension Engine {
    /// The popup History and Help share: one opened while the other is up takes its place, as the web's sheets do.
    static let morePopup = "more"

    /// An item of the section pressed: its popup, its tool's window or its page.
    func openMore(_ item: MoreItem) {
        switch item.open {
        case "history":
            openMore(.history)
        case "help":
            openMore(.help)
        case "tool":
            // (Canvas's launch page for the tool, borderless: the tool window loads it as it is)
            guard let raw = item.url ?? item.href, let u = absolute(raw) else { return }
            openTool(ToolLaunch(title: item.label, args: ["page": u.absoluteString]))
        default:
            guard let raw = item.url ?? item.href, let u = absolute(raw) else { return }
            if u.host?.lowercased() == web.baseURL.host?.lowercased() {
                openCanvasPage(u.absoluteString, title: item.label)
            } else {
                openBrowser(u, title: item.label)
            }
        }
    }

    /// History or Help in a popup over the window, never blocking it (Shell/FloatingPopup.swift): read afresh each time,
    /// and read before it comes up, so it is whole from its first frame — as the web's sheet and the Windows app's popup
    /// are — never growing from a spinner to its rows. Pressed again while it is up, it comes to the front as it is;
    /// pressed again while it is being read, once is enough.
    func openMore(_ list: MoreList) {
        if MoreReads.showing == list, popups.isOpen(Engine.morePopup) {
            popups.raise(Engine.morePopup)
            return
        }
        guard MoreReads.reading.insert(list).inserted else { return }
        Task {
            let read: MoreListData?
            let failure: String?
            do {
                read = try await call(list.call, as: MoreListData.self)
                failure = nil
            } catch {
                read = nil
                failure = error.localizedDescription // (the popup says so, with Try Again)
            }
            MoreReads.reading.remove(list)
            MoreReads.showing = list
            MoreReads.shown += 1
            let shown = MoreReads.shown
            popups.present(Engine.morePopup, title: list.title, light: true, onClose: { MoreReads.showing = nil }) { // (1.3.170: a list, closed by a press outside it)
                MorePopup(list: list, read: read, failure: failure)
                    .id(shown) // (each time what was just read, never what the popup held before)
            }
        }
    }

    /// A row of History or Help picked: an address of the school's as the app opens any, another site in the app's
    /// browser window.
    func openMore(_ row: MoreListRow) {
        if row.open == "place" {
            openWeb(row.url, title: row.title)
        } else if let u = absolute(row.url) {
            openBrowser(u, title: row.title)
        }
    }
}

/// History's and Help's popup as it stands: which list it shows, and which are being read for it.
@MainActor
enum MoreReads {
    /// The list the popup shows while it is up (nil once it is closed).
    static var showing: MoreList?
    /// The lists being read for the popup, about to come up.
    static var reading: Set<MoreList> = []
    /// How many times the popup has been shown: each time's content is new to SwiftUI, its state its own.
    static var shown = 0
}

// MARK: - The sidebar's section

/// The sidebar's last section, under the app's own places and the courses (Sidebar, MainShell.swift): each item a row
/// as the sidebar's others are, with its glyph — a tool's own icon on a tile of the school's nav colour — and, for a
/// tool or a link, the mark that says it opens in a window of its own.
struct MoreSection: View {
    let more: MoreNav

    var body: some View {
        Section(more.title) {
            ForEach(more.items) { item in
                MoreSidebarRow(item: item)
                    .id(item.id) // (the screenshot suite's -SimplSidebarScroll more scrolls to the last)
            }
        }
    }
}

private struct MoreSidebarRow: View {
    let item: MoreItem
    @EnvironmentObject private var engine: Engine
    @Environment(\.silentUp) private var silent

    var body: some View {
        HStack(spacing: 8) {
            Label {
                Text(item.label).lineLimit(1)
            } icon: {
                MoreGlyph(item: item)
            }
            Spacer(minLength: 4)
            if item.external == true, !silent { // (1.3.190: no glyphs while Silent mode is up)
                Image(systemName: "arrow.up.right.square")
                    .font(.system(size: 11.5, weight: .medium))
                    .foregroundStyle(.tertiary)
                    .accessibilityHidden(true)
            }
        }
        // (a row of the list, as the sidebar's others are — but one that opens something rather than a place to be
        // chosen: never selected, a press does what it says)
        .contentShape(Rectangle())
        .onTapGesture { engine.openMore(item) }
        .help(item.note ?? item.label)
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isButton)
        .accessibilityHint(item.note ?? "")
        .accessibilityAction { engine.openMore(item) }
        .contextMenu {
            Button("Open") { engine.openMore(item) }
            if item.kind == "tool" || item.kind == "link", let raw = item.href, let u = engine.absolute(raw) {
                Button("Copy Link") { copyToPasteboard(u.absoluteString) }
            }
        }
    }
}

/// An item's glyph: a clock for History, a question mark for Help; a tool's or a link's own icon on a small rounded
/// tile of the school's nav colour (the icon is drawn for Canvas's dark nav, often white), else the external-link mark
/// on that tile — as the web's sidebar draws them.
struct MoreGlyph: View {
    let item: MoreItem
    var size: CGFloat = 20

    var body: some View {
        switch item.kind {
        case "history":
            Image(systemName: "clock")
        case "help":
            Image(systemName: "questionmark.circle")
        default:
            RoundedRectangle(cornerRadius: size * 0.27, style: .continuous)
                .fill(Color(hex: item.tile ?? MoreIcons.tile))
                .frame(width: size, height: size)
                .overlay { picture }
                .accessibilityHidden(true)
        }
    }

    @ViewBuilder
    private var picture: some View {
        if let image = MoreIcons.image(for: item) {
            Image(nsImage: image)
                .resizable()
                .interpolation(.high)
                .scaledToFit()
                .padding(size * 0.14)
        } else {
            Image(systemName: "arrow.up.right")
                .font(.system(size: size * 0.46, weight: .bold))
                .foregroundStyle(.white)
        }
    }
}

/// A tool's icon as a picture, from what the engine sends: the PNG the school's page drew, else Canvas's inline drawing.
/// Never fetched from another site here (the school's page drew what it could; what it could not shows the mark).
@MainActor
enum MoreIcons {
    /// The tile without the school's colour: the web sidebar's (content/styles/app.css .bcv-nav__ic--tool).
    static let tile = "#2d3b45"
    /// Each icon decoded once (nil: it could not be), by its item and its data.
    private static var decoded: [String: NSImage?] = [:]

    static func image(for item: MoreItem) -> NSImage? {
        guard let raw = item.iconData ?? item.icon.flatMap({ $0.hasPrefix("data:") ? $0 : nil }) else { return nil }
        let key = "\(item.id)#\(raw.count)#\(raw.suffix(24))"
        if let known = decoded[key] { return known }
        let image = picture(raw)
        decoded[key] = image
        return image
    }

    /// A data: address as a picture: base64 (a PNG), or percent-escaped text (an SVG).
    private static func picture(_ raw: String) -> NSImage? {
        guard raw.hasPrefix("data:"), let comma = raw.firstIndex(of: ",") else { return nil }
        let head = raw[raw.index(raw.startIndex, offsetBy: 5)..<comma]
        let body = String(raw[raw.index(after: comma)...])
        let data: Data?
        if head.contains(";base64") {
            data = Data(base64Encoded: body, options: .ignoreUnknownCharacters)
        } else {
            data = body.removingPercentEncoding.map { Data($0.utf8) }
        }
        guard let data, let image = NSImage(data: data), image.size.width > 0 else { return nil }
        return image
    }
}

// MARK: - History and Help

/// History or Help in its popup: its title and line, the rows — each with its glyph, its two lines and where it leads
/// (a chevron for a screen here, the external mark for another site) — and the foot the engine sends. Read afresh each
/// time it opens (Canvas's list changes as the student moves), before it comes up (`Engine.openMore`); a row picked
/// closes the popup and opens where it leads.
struct MorePopup: View {
    let list: MoreList
    @EnvironmentObject private var engine: Engine
    @Environment(\.popup) private var popup
    @State private var data: MoreListData?
    @State private var error: String?

    /// `read`: the list as it was just read; else `failure`, why it could not be (shown with Try Again).
    init(list: MoreList, read: MoreListData?, failure: String?) {
        self.list = list
        _data = State(initialValue: read)
        _error = State(initialValue: read == nil ? failure ?? "\(list.title) could not be loaded." : nil)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            head
            Divider()
            content
            if let foot = data?.foot, !foot.isEmpty {
                Divider()
                Text(foot)
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, 20)
                    .padding(.vertical, 12)
            }
        }
        .frame(width: 440)
    }

    private var head: some View {
        HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                Text(data?.title ?? list.title)
                    .font(.sTitle3)
                    .accessibilityAddTraits(.isHeader)
                Text(data?.note ?? list.note)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 8)
            PopupCloseButton()
        }
        .padding(.horizontal, 20)
        .padding(.top, 18)
        .padding(.bottom, 14)
        .popupHeader() // (dragged by its head, the popup moves)
    }

    @ViewBuilder
    private var content: some View {
        if let data {
            if data.rows.isEmpty {
                Text(data.empty ?? "Nothing here.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, minHeight: 96)
            } else {
                CappedScroll(max: 420) {
                    VStack(spacing: 0) {
                        ForEach(data.rows) { row in
                            RowLink { pick(row) } label: { line(row) }
                                .help(row.open == "place" ? row.title : "Opens \(host(row.url))")
                                .contextMenu { menu(row) }
                        }
                    }
                    .padding(8)
                }
            }
        } else if let error {
            VStack(spacing: 10) {
                Text(error)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
                Button("Try Again") { Task { await load() } }
            }
            .frame(maxWidth: .infinity, minHeight: 120)
            .padding(.horizontal, 20)
        } else {
            ProgressView()
                .controlSize(.small)
                .frame(maxWidth: .infinity, minHeight: 120)
        }
    }

    /// A row: its glyph (what it is, in its course's colour), its title and line, and where it leads.
    private func line(_ row: MoreListRow) -> some View {
        HStack(spacing: 12) {
            IconTile(symbol: glyph(row), color: row.color.map { Color(hex: $0) } ?? .secondary, size: 30)
            VStack(alignment: .leading, spacing: 2) {
                Text(row.title)
                    .font(.sCallout.weight(.medium))
                    .lineLimit(1)
                if let sub = row.sub, !sub.isEmpty {
                    Text(sub)
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 6)
            Image(systemName: row.open == "place" ? "chevron.right" : "arrow.up.right")
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(.tertiary)
                .accessibilityHidden(true)
        }
    }

    private func glyph(_ row: MoreListRow) -> String {
        if list == .help { return row.open == "place" ? "questionmark.circle" : "arrow.up.right.square" }
        switch row.kind ?? "" {
        case "assignment": return "doc.text"
        case "quiz": return "questionmark.square"
        case "discussion": return "bubble.left.and.bubble.right"
        case "announcement": return "megaphone"
        case "document": return "doc"
        case "module": return "square.stack.3d.up"
        case "folder": return "folder"
        case "calendar-month": return "calendar"
        case "home": return "house"
        case "syllabus": return "list.bullet.rectangle"
        case "grades": return "chart.bar"
        case "group": return "person.2"
        case "user": return "person"
        default: return "doc.richtext"
        }
    }

    /// Another site's name, for a row's tooltip ("Opens it.example.edu").
    private func host(_ url: String) -> String {
        let h = URL(string: url)?.host ?? url
        return h.hasPrefix("www.") ? String(h.dropFirst(4)) : h
    }

    private func pick(_ row: MoreListRow) {
        popup.close()
        engine.openMore(row)
    }

    /// A row's menu, as the app's other rows of the school's pages have it (and the Windows app's History and Help).
    @ViewBuilder
    private func menu(_ row: MoreListRow) -> some View {
        Button("Open") { pick(row) }
        if row.open == "place" {
            Button("Open in \(engine.lmsName)") {
                popup.close()
                engine.openWebScreen(row.url, title: row.title)
            }
        }
        Button("Copy Link") { copyToPasteboard(row.url) }
    }

    /// Try Again: the list read afresh, in the popup as it is.
    private func load() async {
        error = nil
        do {
            let d = try await engine.call(list.call, as: MoreListData.self)
            withAnimation(Motion.gentle) { data = d }
        } catch {
            self.error = error.localizedDescription
        }
    }
}

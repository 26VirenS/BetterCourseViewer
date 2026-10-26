import AppKit
import SwiftUI

// (1.3.173, the web's 2.99.106) Simpl Community in Appearance: themes students made and shared, read from the site's
// gallery (simplcourses.com/api/community/themes, open to the apps), each worn in full with a click — its colour, its
// look, and its pictures, each where it was pinned: one of Simpl's drawings by name, or a photo read from the site.

/// A theme as the gallery gives it (site/src/community.js publicTheme).
struct CommunityTheme: Decodable, Identifiable, Hashable {
    struct Place: Decodable, Hashable {
        var scene: String?
        var photo: String?
        var x: Double?
        var y: Double?
        var z: Double?
        var ink: Bool?
    }
    var id: String
    var name: String
    var maker: String?
    var accent: String?
    var look: String?
    var places: [String: Place]?

    func place(_ slot: PhotoSlot) -> Place? { places?[slot.rawValue] }
}

private struct CommunityGallery: Decodable {
    var ok: Bool?
    var themes: [CommunityTheme]?
    var message: String?
}

/// The gallery as read, its photos' small copies for the tiles, and the theme being put on.
@MainActor
final class CommunityStore: ObservableObject {
    static let shared = CommunityStore()

    enum Phase: Equatable {
        case idle, loading, ready
        case failed(String)
    }

    @Published private(set) var phase: Phase = .idle
    @Published private(set) var themes: [CommunityTheme] = []
    /// The theme being put on now (its photos read), if one is.
    @Published private(set) var busy: String?
    /// The last theme worn from here (kept), and why the last one could not be.
    @Published private(set) var worn: String? = UserDefaults.standard.string(forKey: CommunityStore.wornKey)
    @Published var problem: String?
    @Published private(set) var thumbs: [String: NSImage] = [:]

    private static let wornKey = "communityWorn"

    /// The site (the screenshot suite's -SimplCommunityBase: the mock's own stand-in for it).
    static var site: URL {
        URL(string: UserDefaults.standard.string(forKey: "SimplCommunityBase") ?? "") ?? URL(string: "https://simplcourses.com")!
    }

    /// The gallery's own photos only (an approved theme's, by its id and place).
    static func photoURL(_ raw: String?) -> URL? {
        guard let raw, let url = URL(string: raw), let host = url.host,
              host == site.host || host == "simplcourses.com" || host == "www.simplcourses.com",
              url.path.range(of: #"^/api/community/photo/[a-z0-9]+/[a-z]+$"#, options: .regularExpression) != nil else { return nil }
        return url
    }

    /// The gallery read (once each time Community is opened afresh; again with Try Again).
    func load(force: Bool = false) async {
        if phase == .loading || (phase == .ready && !force) { return }
        phase = .loading
        problem = nil
        do {
            var req = URLRequest(url: Self.site.appendingPathComponent("api/community/themes"), cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 20)
            req.httpShouldHandleCookies = false
            let (data, res) = try await URLSession.shared.data(for: req)
            let g = try JSONDecoder().decode(CommunityGallery.self, from: data)
            guard (res as? HTTPURLResponse)?.statusCode == 200, g.ok != false, let list = g.themes else {
                phase = .failed(g.message ?? "Simpl Community could not be reached.")
                return
            }
            themes = Array(list.prefix(80))
            phase = .ready
            for t in themes { Task { await thumb(t) } }
        } catch {
            phase = .failed("Simpl Community could not be reached. Check your connection and try again.")
        }
    }

    /// A tile's pictures: the photo of each place a tile shows (the sidebar's, Due today's and Overdue's).
    private func thumb(_ t: CommunityTheme) async {
        for slot in [PhotoSlot.side, .today, .overdue] {
            let key = "\(t.id)/\(slot.rawValue)"
            guard thumbs[key] == nil, let url = Self.photoURL(t.place(slot)?.photo),
                  let got = try? await URLSession.shared.data(from: url), let image = NSImage(data: got.0) else { continue }
            thumbs[key] = image
        }
    }

    func thumb(_ t: CommunityTheme, _ slot: PhotoSlot) -> NSImage? { thumbs["\(t.id)/\(slot.rawValue)"] }

    /// A theme worn in full: its photos read from the site, then its colour, its look and its pictures put on
    /// (AppearanceStore.wearCommunity); what was worn before is replaced.
    func wear(_ t: CommunityTheme) async {
        guard busy == nil else { return }
        busy = t.id
        problem = nil
        defer { busy = nil }
        var photos: [PhotoSlot: NSImage] = [:]
        for slot in PhotoSlot.allCases {
            guard let p = t.place(slot), p.scene == nil, let raw = p.photo else { continue }
            guard let url = Self.photoURL(raw),
                  let got = try? await URLSession.shared.data(from: url), (got.1 as? HTTPURLResponse)?.statusCode == 200,
                  let image = NSImage(data: got.0) else {
                problem = "A photo of “\(t.name)” could not be read. Try again."
                return
            }
            photos[slot] = image
        }
        withAnimation(Motion.snappy) { AppearanceStore.shared.wearCommunity(t, photos: photos) }
        if t.look == "dark" { AppLook.set(.dark) } else if t.look == "light" { AppLook.set(.light) }
        worn = t.id
        UserDefaults.standard.set(t.id, forKey: Self.wornKey)
    }
}

/// A Community theme's tile picture: its sidebar and two of its counters, each its drawing inked in its colour, its photo,
/// or its colour alone (ThemeSwatch's shape).
struct CommunitySwatch: View {
    let theme: CommunityTheme
    @ObservedObject private var store = CommunityStore.shared
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let dark = theme.look == "dark" || (theme.look != "light" && scheme == .dark)
        let accent = theme.accent.flatMap(AppearanceStore.normalize).map { NSColor(Color(hex: $0)) }
        let base = dark ? Theme.darkPage : Theme.lightPage
        let paper = accent.map { Theme.mix(base, $0, dark ? 0.1 : 0.12) } ?? base
        let ink = accent.map { Theme.mix(paper, $0, dark ? 0.34 : 0.3) } ?? paper
        HStack(spacing: 4) {
            part(.side, paper: paper, ink: ink).frame(width: 28)
            VStack(spacing: 4) {
                part(.today, paper: paper, ink: ink)
                part(.overdue, paper: paper, ink: ink)
            }
        }
        .padding(5)
        .background(Color(nsColor: Theme.mix(paper, .black, dark ? 0.2 : 0.05)))
    }

    private func part(_ slot: PhotoSlot, paper: NSColor, ink: NSColor) -> some View {
        ZStack {
            Color(nsColor: paper)
            if let scene = theme.place(slot)?.scene, AppearanceStore.scenes.contains(scene) {
                Image("Theme\(scene)\(slot.isSide ? "Side" : "Card\(slot.variation)")")
                    .resizable()
                    .renderingMode(.template)
                    .aspectRatio(contentMode: .fill)
                    .foregroundStyle(Color(nsColor: ink))
            } else if let image = store.thumb(theme, slot) {
                Image(nsImage: image)
                    .resizable()
                    .aspectRatio(contentMode: .fill)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 3, style: .continuous))
    }
}

/// Appearance's Community: the gallery's themes side by side, each worn with a click (the one worn ringed); while the
/// gallery is read, a spinner; when it cannot be, why, and Try Again.
struct AppearanceCommunity: View {
    @ObservedObject private var store = CommunityStore.shared

    var body: some View {
        Group {
            switch store.phase {
            case .idle, .loading:
                HStack(spacing: 8) {
                    ProgressView().controlSize(.small)
                    Text("Reading Simpl Community…").foregroundStyle(.secondary)
                }
            case .failed(let why):
                HStack(spacing: 10) {
                    Text(why).foregroundStyle(.secondary).lineLimit(2)
                    Button("Try Again") { Task { await store.load(force: true) } }
                }
            case .ready where store.themes.isEmpty:
                HStack(spacing: 10) {
                    Text("No themes shared yet.").foregroundStyle(.secondary)
                    Link("Simpl Community", destination: CommunityStore.site.appendingPathComponent("community"))
                }
            case .ready:
                HStack(spacing: 12) {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 10) {
                            ForEach(store.themes) { t in tile(t) }
                        }
                        .padding(.vertical, 2)
                    }
                    if let problem = store.problem {
                        Text(problem).font(.sCaption).foregroundStyle(.orange).lineLimit(3).frame(maxWidth: 160, alignment: .leading)
                    }
                }
            }
        }
        .font(.sBody)
        .frame(maxWidth: .infinity, alignment: .leading)
        .task { await store.load() }
    }

    private func tile(_ t: CommunityTheme) -> some View {
        let on = store.worn == t.id
        let shape = RoundedRectangle(cornerRadius: 9, style: .continuous)
        return Button {
            Task { await store.wear(t) }
        } label: {
            VStack(spacing: 3) {
                CommunitySwatch(theme: t)
                    .frame(width: 74, height: 46)
                    .clipShape(shape)
                    .overlay(shape.strokeBorder(on ? Theme.accent : Color.primary.opacity(0.12), lineWidth: on ? 2.5 : 1))
                    .overlay {
                        if store.busy == t.id { ProgressView().controlSize(.small) }
                    }
                Text(t.name)
                    .font(.sCaption.weight(on ? .semibold : .regular))
                    .lineLimit(1)
                    .frame(maxWidth: 84)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(store.busy != nil)
        .help(t.maker.map { $0.isEmpty ? t.name : "\(t.name) · by \($0)" } ?? t.name)
        .accessibilityLabel(Text("\(t.name) theme\(t.maker.map { $0.isEmpty ? "" : ", by \($0)" } ?? "")"))
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

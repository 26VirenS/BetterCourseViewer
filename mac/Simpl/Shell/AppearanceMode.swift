import AppKit
import SwiftUI

// (1.3.113) Appearance, from the account's ⋯ menu (Settings ▸ Appearance is gone): the Dashboard shown, the whole window
// darkened but for the sidebar and the Dashboard's cards — each of those with a photo button on it — and a bar risen from
// the foot of the window, in its middle, for the theme's colours and the courses' colours. Done, or Escape, puts it away.

/// Whether Appearance is on.
@MainActor
final class AppearanceMode: ObservableObject {
    static let shared = AppearanceMode()
    @Published private(set) var on = false
    /// (1.3.166) The place whose photo is being placed where it is (Shell/PhotoInPlace.swift), if one is.
    @Published private(set) var editing: PhotoSlot?

    /// Turned on over the Dashboard (where its cards are). The screenshot suite's -SimplAppearanceMode YES turns it on
    /// as the window opens (MainShell).
    func begin(_ engine: Engine) {
        if engine.nav.current.place != .dashboard || !engine.nav.current.path.isEmpty { engine.go(.dashboard) }
        withAnimation(Motion.gentle) { on = true }
    }

    func end() {
        withAnimation(Motion.gentle) {
            editing = nil
            on = false
        }
    }

    /// (1.3.166) A place's photo opened where it is, to move and zoom it (uploaded, or Move and Zoom…).
    func edit(_ slot: PhotoSlot) {
        withAnimation(Motion.snappy) { editing = slot }
    }

    func endEdit() {
        withAnimation(Motion.snappy) { editing = nil }
    }

    static var launchOn: Bool { UserDefaults.standard.bool(forKey: "SimplAppearanceMode") }
}

// MARK: - A place's photo button

/// The photo button on the sidebar and on each Dashboard card while Appearance is on: a photo of your own, a drawing,
/// the photo moved and zoomed (1.3.166: where it is, its settings round it), or taken away.
struct PhotoBadge: View {
    let slot: PhotoSlot
    @ObservedObject private var store = AppearanceStore.shared
    @State private var shown = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Menu {
            // (1.3.166) a photo uploaded opens where it went, to place it
            Button("Upload Photo…") { if store.upload(to: slot) { AppearanceMode.shared.edit(slot) } }
            Menu("Drawing") {
                ForEach(AppearanceStore.scenes, id: \.self) { s in
                    Button(s) { store.setScene(s, in: slot) }
                }
            }
            if store.photos[slot] != nil {
                Divider()
                // (1.3.164) a photo of one's own: inked, or (off) shown as it is, the words on it white-bordered
                if case .upload? = store.photos[slot]?.source {
                    Toggle("Ink Photo", isOn: Binding(get: { !store.isPlain(slot) }, set: { store.setInk($0, in: slot) }))
                }
                Button("Move and Zoom…") { AppearanceMode.shared.edit(slot) }
                Button("Remove Photo", role: .destructive) { store.remove(slot) }
            }
        } label: {
            Image(systemName: store.photos[slot] == nil ? "photo.badge.plus" : "photo")
                .font(.system(size: 17, weight: .semibold))
                .foregroundStyle(.white)
                .frame(width: 42, height: 42)
                .background(Circle().fill(Theme.accent))
                .overlay(Circle().strokeBorder(.white.opacity(0.85), lineWidth: 2))
                .shadow(color: .black.opacity(0.3), radius: 8, y: 3)
                .contentShape(Circle())
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .fixedSize()
        .help(slot.isSide ? "Sidebar photo" : "\(slot.title) photo")
        .accessibilityLabel(Text(slot.isSide ? "Sidebar photo" : "\(slot.title) card photo"))
        .scaleEffect(shown || reduceMotion ? 1 : 0.6)
        .opacity(shown ? 1 : 0)
        .onAppear { withAnimation(Motion.snappy) { shown = true } }
    }
}

// MARK: - The veil and the bar

/// Over the whole window while Appearance is on: everything darkened but the sidebar and the Dashboard's cards (which
/// take their presses through it), and the bar at the foot. Nothing is drawn, and nothing held, while it is off.
struct AppearanceOverlay: View {
    @Binding var columns: NavigationSplitViewVisibility
    @EnvironmentObject private var engine: Engine
    @ObservedObject private var mode = AppearanceMode.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        GeometryReader { proxy in
            ZStack(alignment: .bottom) {
                TourProbe(spot: .overlay).allowsHitTesting(false)
                if mode.on {
                    TimelineView(.periodic(from: .now, by: 0.2)) { _ in
                        let holes = self.holes(size: proxy.size)
                        AppearanceVeil(holes: holes)
                            .contentShape(AppearanceHoles(holes: holes), eoFill: true)
                            .onTapGesture {} // (the darkened part takes presses, and does nothing with them)
                    }
                    .transition(.opacity)
                    if let slot = mode.editing {
                        // (1.3.166) a photo placed where it is: the place outlined, its settings round it, kept on it
                        TimelineView(.periodic(from: .now, by: 0.2)) { _ in
                            if let frame = editFrame(slot, size: proxy.size) {
                                PhotoEditLayer(slot: slot, frame: frame, bounds: proxy.size, done: { mode.endEdit() })
                            }
                        }
                        .frame(width: proxy.size.width, height: proxy.size.height, alignment: .topLeading)
                        .transition(.opacity)
                    } else {
                        AppearanceBar(done: { mode.end() })
                            .padding(.horizontal, 24)
                            .padding(.bottom, 22)
                            .transition(reduceMotion ? .opacity : .move(edge: .bottom).combined(with: .opacity))
                    }
                }
            }
            .frame(width: proxy.size.width, height: proxy.size.height)
        }
        .ignoresSafeArea()
        .allowsHitTesting(mode.on)
        .onChange(of: mode.on) { _, on in
            // (the sidebar shown, its photo button with it)
            if on && columns == .detailOnly { withAnimation(Motion.gentle) { columns = .all } }
        }
        .task {
            guard AppearanceMode.launchOn else { return }
            try? await Task.sleep(nanoseconds: 900_000_000)
            mode.begin(engine)
            // (the screenshot suite: -SimplPhotoEdit today opens that place's photo where it is)
            if let raw = UserDefaults.standard.string(forKey: "SimplPhotoEdit"), let slot = PhotoSlot(rawValue: raw) {
                try? await Task.sleep(nanoseconds: 900_000_000)
                mode.edit(slot)
            }
        }
    }

    /// (1.3.166) The place whose photo is being placed, where it is in this overlay.
    private func editFrame(_ slot: PhotoSlot, size: CGSize) -> CGRect? {
        guard let window = engine.hostView.window, let me = TourFrames.shared.rect(.overlay, in: window),
              let r = TourFrames.shared.rect(slot.isSide ? .sidebar : .photo(slot), in: window) else { return nil }
        let f = r.offsetBy(dx: -me.minX, dy: -me.minY)
        return CGRect(origin: .zero, size: size).intersects(f) ? f : nil
    }

    /// The sidebar and the Dashboard's cards, where they are in this overlay.
    private func holes(size: CGSize) -> [CGRect] {
        guard let window = engine.hostView.window, let me = TourFrames.shared.rect(.overlay, in: window) else { return [] }
        let bounds = CGRect(origin: .zero, size: size)
        // (1.3.166) a photo being placed: its place the only one lit (a card's, just outside its edge)
        if let slot = mode.editing {
            guard let f = editFrame(slot, size: size) else { return [] }
            return [slot.isSide ? f : f.insetBy(dx: -6, dy: -6)]
        }
        return [TourSpot.sidebar, .counters].compactMap { spot in
            TourFrames.shared.rect(spot, in: window)
                .map { $0.offsetBy(dx: -me.minX, dy: -me.minY).insetBy(dx: spot == .counters ? -8 : 0, dy: spot == .counters ? -8 : 0) }
                .flatMap { bounds.intersects($0) ? $0 : nil }
        }
    }
}

/// The window, less the lit places (for presses: even-odd, so the lit places let them through).
private struct AppearanceHoles: Shape {
    let holes: [CGRect]

    func path(in r: CGRect) -> Path {
        var p = Path(r)
        for h in holes { p.addRoundedRect(in: h, cornerSize: CGSize(width: 14, height: 14), style: .continuous) }
        return p
    }
}

/// The window darkened but for the lit places.
private struct AppearanceVeil: View {
    let holes: [CGRect]
    private static let dim = Theme.dynamic(light: NSColor(white: 0, alpha: 0.42), dark: NSColor(white: 0, alpha: 0.58))

    var body: some View {
        ZStack(alignment: .topLeading) {
            Rectangle().fill(AppearanceVeil.dim)
            ForEach(Array(holes.enumerated()), id: \.offset) { _, h in
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .frame(width: h.width, height: h.height)
                    .offset(x: h.minX, y: h.minY)
                    .blendMode(.destinationOut)
            }
        }
        .compositingGroup()
        .animation(Motion.gentle, value: holes)
    }
}

/// The bar at the foot of the window: Theme (light or dark, a ready-made theme, the colour), Courses (each course's
/// colour) or (1.3.173) Community (themes other students shared), and Done.
struct AppearanceBar: View {
    let done: () -> Void
    enum Tab: String, Hashable { case theme, courses, community }
    @State private var tab: Tab = Tab(rawValue: UserDefaults.standard.string(forKey: "SimplAppearanceTab") ?? "") ?? .theme
    @State private var look = AppLook(rawValue: UserDefaults.standard.string(forKey: "SimplAppearance") ?? "") ?? .system
    @State private var picking = UserDefaults.standard.bool(forKey: "SimplPickerOpen")
    @ObservedObject private var store = AppearanceStore.shared
    @EnvironmentObject private var engine: Engine

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 12) {
                Picker("", selection: $tab) {
                    Text("Theme").tag(Tab.theme)
                    Text("Courses").tag(Tab.courses)
                    Text("Community").tag(Tab.community)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
                Spacer(minLength: 8)
                Picker("", selection: $look) {
                    Label("Light", systemImage: "sun.max").tag(AppLook.light)
                    Label("Dark", systemImage: "moon").tag(AppLook.dark)
                    Label("System", systemImage: "circle.lefthalf.filled").tag(AppLook.system)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
                .onChange(of: look) { _, l in AppLook.set(l) }
                .help("Light, dark, or as the Mac is")
                Button("Done", action: done)
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.cancelAction)
            }
            Group {
                switch tab {
                case .theme: themeRow
                case .courses: AppearanceCourses()
                case .community: AppearanceCommunity()
                }
            }
            .frame(height: 74, alignment: .leading)
        }
        .padding(16)
        .frame(maxWidth: 760)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 22, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).strokeBorder(Color.primary.opacity(0.1), lineWidth: 1))
        .shadow(color: .black.opacity(0.3), radius: 28, y: 12)
        .tint(Theme.accent)
    }

    /// The ready-made themes, then the colours (Regular, the web's, any of your own).
    private var themeRow: some View {
        HStack(spacing: 14) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 10) {
                    ForEach(AppearanceStore.ready) { r in themeTile(r) }
                }
                .padding(.vertical, 2)
            }
            Divider().frame(height: 52)
            HStack(spacing: 4) {
                dot(nil, Color(nsColor: .controlAccentColor), "Regular")
                ForEach(AppearanceStore.colours) { c in dot(c.hex, Color(hex: c.hex), c.name) }
                customDot
            }
            .fixedSize()
        }
    }

    private func themeTile(_ r: AppearanceStore.Ready) -> some View {
        let on = store.wearing == r
        let shape = RoundedRectangle(cornerRadius: 9, style: .continuous)
        return Button {
            withAnimation(Motion.snappy) { store.wear(r) }
        } label: {
            VStack(spacing: 4) {
                ThemeSwatch(ready: r)
                    .frame(width: 74, height: 46)
                    .clipShape(shape)
                    .overlay(shape.strokeBorder(on ? Theme.accent : Color.primary.opacity(0.12), lineWidth: on ? 2.5 : 1))
                Text(r.name).font(.sCaption.weight(on ? .semibold : .regular))
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(Text("\(r.name) theme"))
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    private func dot(_ hex: String?, _ color: Color, _ name: String) -> some View {
        let on = store.accentHex == hex
        return Button {
            withAnimation(Motion.snappy) { store.setAccent(hex) }
        } label: {
            ZStack {
                Circle().fill(color).frame(width: 22, height: 22)
                if on { Circle().strokeBorder(Color.primary.opacity(0.85), lineWidth: 2).frame(width: 30, height: 30) }
            }
            .frame(width: 30, height: 30)
            .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .help(name)
        .accessibilityLabel(Text(name))
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    /// Any colour: the radial picker, in a popover over the bar.
    private var customDot: some View {
        Button {
            if !store.wearingCustom { store.setCustom(store.custom) }
            picking = true
        } label: {
            ZStack {
                Circle()
                    .fill(AngularGradient(colors: [.red, .yellow, .green, .cyan, .blue, .purple, .red], center: .center))
                    .frame(width: 22, height: 22)
                Circle().fill(Color(hex: store.custom.hex)).frame(width: 10, height: 10)
                if store.wearingCustom { Circle().strokeBorder(Color.primary.opacity(0.85), lineWidth: 2).frame(width: 30, height: 30) }
            }
            .frame(width: 30, height: 30)
            .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .help("Any colour")
        .accessibilityLabel(Text("Custom colour"))
        .popover(isPresented: $picking, arrowEdge: .top) {
            RadialColorPicker(value: store.custom, change: { store.setCustom($0) })
                .padding(20)
        }
    }
}

// MARK: - The courses' colours

/// Each course with its colour; pressed, a palette (and any colour of your own) — saved to the school as its own colour
/// for the course, as the web's and Canvas's are.
private struct AppearanceCourses: View {
    @EnvironmentObject private var engine: Engine
    @State private var editing: String?

    var body: some View {
        if engine.courses.isEmpty {
            Text("Your courses show here once they have loaded.")
                .font(.sCallout)
                .foregroundStyle(.secondary)
        } else {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(engine.courses) { c in chip(c) }
                }
                .padding(.vertical, 2)
            }
        }
    }

    private func chip(_ c: CourseRow) -> some View {
        Button {
            editing = c.id
        } label: {
            HStack(spacing: 8) {
                RoundedRectangle(cornerRadius: 5, style: .continuous)
                    .fill(Color(hex: c.color))
                    .frame(width: 22, height: 22)
                Text(c.code).font(.sCallout.weight(.medium)).lineLimit(1)
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(Color.primary.opacity(0.06), in: Capsule())
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .help("\(c.code)'s colour")
        .popover(isPresented: Binding(get: { editing == c.id }, set: { if !$0 { editing = nil } }), arrowEdge: .top) {
            CourseColourPicker(course: c) { if editing == c.id { editing = nil } } // (saved after a click out: another course's may be open by then)
        }
    }
}

/// A course's colour: the palette Canvas offers, or any colour from the radial picker; saved as it is picked.
private struct CourseColourPicker: View {
    let course: CourseRow
    let close: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var custom = AppearanceStore.Custom(h: 211, s: 1, depth: 40)
    @State private var any = false
    @State private var saving = false
    /// (1.3.149) The wheel turned and its colour not saved yet: clicking out saves it, as Use This Colour does.
    @State private var turned = false

    /// Canvas's own course colours.
    static let palette = ["#BD3C14", "#FF2717", "#E71F63", "#8F3E97", "#65499D", "#4554A4", "#1770AB", "#0B9BE3",
                          "#06A3B7", "#009688", "#009606", "#8D9900", "#D97900", "#FD5D10", "#F06291", "#111111"]

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text(course.code).font(.sHeadline)
                Spacer()
                if saving { ProgressView().controlSize(.small) }
            }
            LazyVGrid(columns: Array(repeating: GridItem(.fixed(30), spacing: 6), count: 8), spacing: 6) {
                ForEach(Self.palette, id: \.self) { hex in
                    Button { save(hex) } label: {
                        ZStack {
                            Circle().fill(Color(hex: hex)).frame(width: 24, height: 24)
                            if (course.color ?? "").lowercased() == hex.lowercased() {
                                Circle().strokeBorder(Color.primary.opacity(0.85), lineWidth: 2).frame(width: 30, height: 30)
                            }
                        }
                        .frame(width: 30, height: 30)
                        .contentShape(Circle())
                    }
                    .buttonStyle(.plain)
                    .help(hex)
                }
            }
            if any {
                RadialColorPicker(value: custom, change: { custom = $0; turned = true })
                    .frame(maxWidth: .infinity)
                    .padding(.top, 6)
                // (1.3.148: the wheel's middle is its code now; the course's colour is kept here)
                Button("Use This Colour") { save(custom.hex) }
                    .glassButton()
                    .keyboardShortcut(.defaultAction)
                    .frame(maxWidth: .infinity)
            } else {
                Button("Any Colour…") { withAnimation(Motion.gentle) { any = true } }
            }
        }
        .padding(16)
        .frame(width: any ? 320 : 280)
        .disabled(saving)
        .onDisappear { if turned { save(custom.hex) } }
    }

    private func save(_ hex: String) {
        turned = false
        saving = true
        let engine = engine // (held: clicking out saves after the popover is gone)
        Task {
            if await engine.act("setColor", ["id": course.id, "hex": hex]) {
                _ = try? await engine.call("refresh", as: OK.self)
                await engine.loadSidebar()
            }
            saving = false
            close()
        }
    }
}

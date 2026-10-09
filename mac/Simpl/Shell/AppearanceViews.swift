import AppKit
import SwiftUI

// Settings ▸ Appearance's parts (1.3.1): the web's radial colour picker, a place's photo editor (the photo dragged to
// move it, pinched or slid to zoom it — the web's Personalize does the same on its preview), and a theme's small picture.

// MARK: - The radial picker

/// The web's radial picker (content/app/personalize.js): a hue ring round the outside; inside it, saturation up the
/// left arc (grey to full) and depth down the right (light to deep); the colour in the middle, with how it reads as
/// words on a light ground and on a dark one. Dragged anywhere on a part, that part follows the pointer.
struct RadialColorPicker: View {
    let value: AppearanceStore.Custom
    let change: (AppearanceStore.Custom) -> Void
    @State private var mode: Mode?

    private enum Mode { case hue, sat, depth }
    private let size: CGFloat = 272
    private var c: CGFloat { size / 2 }

    private func pt(_ deg: Double, _ r: CGFloat) -> CGPoint {
        let a = deg * .pi / 180
        return CGPoint(x: c + r * sin(a), y: c - r * cos(a))
    }

    private var lightness: Double { (72 - value.depth * 0.4) / 100 }

    private func color(_ h: Double, _ s: Double, _ l: Double) -> Color { Color(hex: AppearanceStore.hex(h: h, s: s, l: l)) }

    var body: some View {
        let hex = value.hex
        ZStack {
            // the hue ring
            Circle()
                .stroke(AngularGradient(colors: stride(from: 0, through: 360, by: 30).map { color(Double($0), 1, 0.5) },
                                        center: .center, startAngle: .degrees(-90), endAngle: .degrees(270)), lineWidth: 34)
                .frame(width: 236, height: 236)
            arc(200, 340, AngularGradient(colors: [color(value.h, 0, lightness), color(value.h, 1, lightness)],
                                          center: .center, startAngle: .degrees(110), endAngle: .degrees(250)))
            arc(20, 160, AngularGradient(colors: [color(value.h, value.s, 0.72), color(value.h, value.s, 0.32)],
                                         center: .center, startAngle: .degrees(-70), endAngle: .degrees(70)))
            label("SATURATION", at: pt(270, 62), angle: -90)
            label("DEPTH", at: pt(90, 62), angle: 90)
            knob(at: pt(value.h, 118), fill: color(value.h, 1, 0.5), r: 11)
            knob(at: pt(200 + value.s * 140, 86), fill: Color(hex: hex), r: 9)
            knob(at: pt(20 + value.depth / 100 * 140, 86), fill: Color(hex: hex), r: 9)
            core(hex)
        }
        .frame(width: size, height: size)
        .contentShape(Rectangle())
        .gesture(
            DragGesture(minimumDistance: 0)
                .onChanged { g in turn(g.startLocation, g.location) }
                .onEnded { _ in mode = nil }
        )
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Colour")
        .accessibilityValue(Text(hex))
        .accessibilityAdjustableAction { dir in
            var v = value
            v.h = (v.h + (dir == .increment ? 10 : -10) + 360).truncatingRemainder(dividingBy: 360)
            change(v)
        }
    }

    private func arc(_ a0: Double, _ a1: Double, _ fill: AngularGradient) -> some View {
        Path { p in
            p.addArc(center: CGPoint(x: c, y: c), radius: 86, startAngle: .degrees(a0 - 90), endAngle: .degrees(a1 - 90), clockwise: false)
        }
        .stroke(fill, style: StrokeStyle(lineWidth: 14, lineCap: .round))
    }

    private func label(_ text: String, at p: CGPoint, angle: Double) -> some View {
        Text(text)
            .font(.system(size: 9, weight: .semibold))
            .tracking(1.2)
            .foregroundStyle(.secondary)
            .rotationEffect(.degrees(angle))
            .position(p)
    }

    private func knob(at p: CGPoint, fill: Color, r: CGFloat) -> some View {
        Circle()
            .fill(fill)
            .overlay(Circle().strokeBorder(.white, lineWidth: 3))
            .shadow(color: .black.opacity(0.25), radius: 2, y: 1)
            .frame(width: r * 2, height: r * 2)
            .position(p)
    }

    /// The colour, and how it reads as words: on white (left) and on the dark card (right), each stepped until it reads.
    private func core(_ hex: String) -> some View {
        let ns = NSColor(Color(hex: hex))
        let onLight = Color(nsColor: AppearanceStore.readable(ns, on: .white, ratio: 4.5))
        let onDark = Color(nsColor: AppearanceStore.readable(ns, on: NSColor(srgbRed: 28 / 255, green: 28 / 255, blue: 30 / 255, alpha: 1), ratio: 4.5))
        return ZStack {
            Circle().fill(Color(hex: hex))
            HStack(spacing: 0) {
                Text("Aa").font(.system(size: 15, weight: .bold)).foregroundStyle(onLight)
                    .frame(width: 44, height: 30).background(Color.white)
                Text("Aa").font(.system(size: 15, weight: .bold)).foregroundStyle(onDark)
                    .frame(width: 44, height: 30).background(Color(red: 28 / 255, green: 28 / 255, blue: 30 / 255))
            }
            .clipShape(Capsule())
            .offset(y: 22)
        }
        .frame(width: 112, height: 112)
        .help("How your colour reads as words, by day and by night")
    }

    /// A drag on the picker: the part it began on follows the pointer.
    private func turn(_ start: CGPoint, _ now: CGPoint) {
        func read(_ p: CGPoint) -> (a: Double, d: Double) {
            let dx = Double(p.x - c), dy = Double(p.y - c)
            var a = atan2(dx, -dy) * 180 / .pi
            if a < 0 { a += 360 }
            return (a, (dx * dx + dy * dy).squareRoot())
        }
        if mode == nil {
            let f = read(start)
            if f.d >= 100 && f.d <= 136 { mode = .hue } else if f.d >= 68 && f.d < 100 { mode = f.a >= 180 ? .sat : .depth } else { return }
        }
        let q = read(now)
        var v = value
        switch mode {
        case .hue: v.h = q.a
        case .sat: v.s = min(max((q.a - 200) / 140, 0), 1)
        case .depth: v.depth = min(max((q.a - 20) / 140, 0), 1) * 100
        case nil: return
        }
        change(v)
    }
}

// MARK: - A place's photo editor

/// A place's photo, to choose and to place: the place drawn as it is (frosted, as the window shows it), the photo
/// dragged to move it and pinched (or the slider) to zoom it; Upload Photo…, one of the drawings, Remove. A picture
/// file dropped on it is taken too.
struct PhotoSlotEditor: View {
    let slot: PhotoSlot
    @ObservedObject private var store = AppearanceStore.shared
    @State private var dragFrom: PhotoPlace?
    @State private var zoomFrom: Double?
    @State private var targeted = false

    private var photo: SlotPhoto? { store.photos[slot] }
    private var boxSize: CGSize { slot.isSide ? CGSize(width: 110, height: 110 / slot.aspect) : CGSize(width: 236, height: 236 / slot.aspect) }

    var body: some View {
        HStack(alignment: .top, spacing: 18) {
            preview
            VStack(alignment: .leading, spacing: 10) {
                Text(slot.isSide ? "Sidebar" : "\(slot.title) card")
                    .font(.sHeadline)
                Button("Upload Photo…") { store.upload(to: slot) }
                    .fixedSize()
                Menu("Drawing") {
                    ForEach(AppearanceStore.scenes, id: \.self) { s in
                        Button(s) { store.setScene(s, in: slot) }
                    }
                }
                .fixedSize()
                if photo != nil {
                    HStack(spacing: 8) {
                        Image(systemName: "minus.magnifyingglass").foregroundStyle(.secondary)
                        Slider(value: zoom, in: PhotoPlace.zoom) { editing in if !editing { store.commitPlace() } }
                            .frame(minWidth: 90, maxWidth: 160)
                        Image(systemName: "plus.magnifyingglass").foregroundStyle(.secondary)
                    }
                    .help("Zoom the photo (or pinch it)")
                    HStack(spacing: 8) {
                        Button("Reset") {
                            store.setPlace(slot.defaultPlace, in: slot)
                            store.commitPlace()
                        }
                        .help("Put the photo back where it started")
                        .disabled(photo?.place == slot.defaultPlace)
                        Button("Remove", role: .destructive) { store.remove(slot) }
                    }
                    .fixedSize()
                    Text("Drag the photo to move it.")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                } else {
                    Text("No photo. Upload one of your own, pick a drawing, or drop a picture on the box.")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 0)
        }
    }

    private var preview: some View {
        let shape = RoundedRectangle(cornerRadius: slot.isSide ? 14 : 16, style: .continuous)
        return ZStack {
            if photo != nil {
                SlotPhotoView(slot: slot)
            } else {
                Theme.page
                Image(systemName: "photo.badge.plus").font(.system(size: 22)).foregroundStyle(.tertiary)
            }
        }
        .frame(width: boxSize.width, height: boxSize.height)
        .clipShape(shape)
        .overlay(shape.strokeBorder(targeted ? Theme.accent : Color.primary.opacity(0.14), lineWidth: targeted ? 2 : 1))
        .contentShape(shape)
        .gesture(move.simultaneously(with: pinch))
        .dropDestination(for: URL.self) { urls, _ in
            guard let u = urls.first(where: \.isFileURL) else { return false }
            return store.take(u, into: slot)
        } isTargeted: { targeted = $0 }
        .onHover { inside in
            if photo != nil { if inside { NSCursor.openHand.push() } else { NSCursor.pop() } }
        }
        .accessibilityLabel(Text("\(slot.title) photo"))
    }

    private var zoom: Binding<Double> {
        Binding(get: { photo?.place.z ?? 1 }, set: { z in
            guard var p = photo?.place else { return }
            p.z = z
            store.setPlace(p, in: slot)
        })
    }

    /// The photo follows the pointer: the point pinned moves the other way across the place as the photo is pulled.
    private var move: some Gesture {
        DragGesture(minimumDistance: 1)
            .onChanged { g in
                guard let photo, let pic = store.image(photo, in: slot) else { return }
                let from = dragFrom ?? photo.place
                if dragFrom == nil { dragFrom = from }
                let iw = max(pic.image.size.width, 1), ih = max(pic.image.size.height, 1)
                let k = max(boxSize.width / iw, boxSize.height / ih) * from.z
                let spareX = boxSize.width - iw * k, spareY = boxSize.height - ih * k
                var p = from
                if abs(spareX) > 0.5 { p.x = from.x + Double(g.translation.width / spareX) * 100 }
                if abs(spareY) > 0.5 { p.y = from.y + Double(g.translation.height / spareY) * 100 }
                store.setPlace(p, in: slot)
            }
            .onEnded { _ in
                dragFrom = nil
                store.commitPlace()
            }
    }

    private var pinch: some Gesture {
        MagnifyGesture()
            .onChanged { g in
                guard let photo else { return }
                let from = zoomFrom ?? photo.place.z
                if zoomFrom == nil { zoomFrom = from }
                var p = photo.place
                p.z = from * Double(g.magnification)
                store.setPlace(p, in: slot)
            }
            .onEnded { _ in
                zoomFrom = nil
                store.commitPlace()
            }
    }
}

/// A place's photo editor as a sheet (a counter's context menu: Card Photo…).
struct PhotoSlotSheet: View {
    let slot: PhotoSlot
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            PhotoSlotEditor(slot: slot)
            HStack {
                Text("Every place's photo is in Settings ▸ Appearance.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Done") { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 560)
    }
}

// MARK: - A theme's picture

/// A ready-made theme, small: its sidebar and two of its counters, drawn in its own colour (Default: the page).
struct ThemeSwatch: View {
    let ready: AppearanceStore.Ready
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let dark = scheme == .dark
        let accent = ready.accent.map { NSColor(Color(hex: $0)) }
        let base = dark ? Theme.darkPage : Theme.lightPage
        let paper = accent.map { Theme.mix(base, $0, dark ? 0.1 : 0.12) } ?? base
        let ink = accent.map { Theme.mix(paper, $0, dark ? 0.34 : 0.3) } ?? paper
        HStack(spacing: 4) {
            part(ready.scene.map { "Theme\($0)Side" }, paper: paper, ink: ink)
                .frame(width: 28)
            VStack(spacing: 4) {
                part(ready.scene.map { "Theme\($0)Card0" }, paper: paper, ink: ink)
                part(ready.scene.map { "Theme\($0)Card3" }, paper: paper, ink: ink)
            }
        }
        .padding(5)
        .background(Color(nsColor: Theme.mix(paper, .black, dark ? 0.2 : 0.05)))
    }

    private func part(_ name: String?, paper: NSColor, ink: NSColor) -> some View {
        ZStack {
            Color(nsColor: paper)
            if let name {
                Image(name)
                    .resizable()
                    .renderingMode(.template)
                    .aspectRatio(contentMode: .fill)
                    .foregroundStyle(Color(nsColor: ink))
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))
    }
}

/// A place in the Photos row: its photo, small, in its own shape; picked, ringed in the accent.
struct PhotoSlotThumb: View {
    let slot: PhotoSlot
    let picked: Bool

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        VStack(spacing: 4) {
            ZStack {
                Theme.page
                SlotPhotoView(slot: slot, frost: false)
            }
            .frame(width: slot.isSide ? 34 : 70, height: slot.isSide ? 80 : 40)
            .clipShape(shape)
            .overlay(shape.strokeBorder(picked ? Theme.accent : Color.primary.opacity(0.14), lineWidth: picked ? 2.5 : 1))
            Text(slot.title)
                .font(.sCaption2)
                .foregroundStyle(picked ? .primary : .secondary)
                .lineLimit(1)
        }
    }
}

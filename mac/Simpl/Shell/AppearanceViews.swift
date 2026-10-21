import AppKit
import SwiftUI

// Settings ▸ Appearance's parts (1.3.1): the web's radial colour picker, a place's photo editor (the photo dragged to
// move it, pinched or slid to zoom it — the web's Personalize does the same on its preview), and a theme's small picture.

// MARK: - The radial picker

/// The web's radial picker (content/app/personalize.js, setup-css.js .pz__pk), drawn as the web draws it: a disc with a
/// thin hue band round its edge; inside it, saturation up the left arc (grey to full) and the shade down the right (light
/// to deep), each named along its arc; and at the middle the colour as words — "Aa" on white over "Aa" on the dark card,
/// each stepped until it reads — ringed in the colour, with its code on it. Dragged anywhere on a part, that part follows.
/// (onboarding-24) Its arcs' names are 11-point, the right one "Shade" (a word students use; it was "Depth"). (1.3.148) The
/// colour's code sits in the middle where Done was, to read or to type; a drag turns the wheel at once and hands the colour
/// on (`change`) as the pointer rests and as the drag ends — never on every step of it, which drew the whole window again
/// each time — so what is chosen is kept without Done.
struct RadialColorPicker: View {
    let value: AppearanceStore.Custom
    let change: (AppearanceStore.Custom) -> Void
    /// (1.3.76) On its way out: it shrinks back and fades as it grew in, before its row is taken away.
    var leaving = false
    @State private var mode: Mode?
    /// The colour as the wheel shows it mid-drag, before it is handed on.
    @State private var live: AppearanceStore.Custom?
    @State private var settle: Task<Void, Never>?
    @State private var grown = UserDefaults.standard.string(forKey: "SimplShotFile") != nil // (a screenshot: already in)
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private enum Mode { case hue, sat, depth }
    private let size: CGFloat = 272
    private var c: CGFloat { size / 2 }

    private func pt(_ deg: Double, _ r: CGFloat) -> CGPoint {
        let a = deg * .pi / 180
        return CGPoint(x: c + r * sin(a), y: c - r * cos(a))
    }

    /// What the wheel draws: the colour being dragged, else the one worn.
    private var shown: AppearanceStore.Custom { live ?? value }
    private var lightness: Double { (72 - shown.depth * 0.4) / 100 }

    private func color(_ h: Double, _ s: Double, _ l: Double) -> Color { Color(hex: AppearanceStore.hex(h: h, s: s, l: l)) }

    var body: some View {
        wheel
            .scaleEffect((grown && !leaving) || reduceMotion ? 1 : 0.92)
            .opacity(grown && !leaving ? 1 : 0)
            .onAppear { withAnimation(Motion.gentle) { grown = true } }
            .onDisappear { commit() } // (closed mid-drag: what was shown is kept)
            .allowsHitTesting(!leaving) // (put away: a click on it as it fades turns nothing)
    }

    /// A colour from a drag: shown at once on the wheel, handed on once the pointer has rested a moment.
    private func preview(_ v: AppearanceStore.Custom) {
        live = v
        settle?.cancel()
        settle = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 140_000_000)
            if !Task.isCancelled { commit() }
        }
    }

    /// The colour shown handed on, if it has not been yet.
    private func commit() {
        settle?.cancel()
        settle = nil
        guard let v = live else { return }
        live = nil
        if v != value { change(v) }
    }

    private var wheel: some View {
        let v0 = shown
        let hex = v0.hex
        return ZStack {
            Circle().fill(.regularMaterial)
            Circle().strokeBorder(Color.primary.opacity(0.1), lineWidth: 1)
            // the hue band: 108 to 127 from the middle, the web's
            Circle()
                .stroke(AngularGradient(colors: [.red, .yellow, .green, .cyan, .blue, Color(red: 1, green: 0, blue: 1), .red],
                                        center: .center, startAngle: .degrees(-90), endAngle: .degrees(270)), lineWidth: 19)
                .frame(width: 235, height: 235)
            arc(200, 340, LinearGradient(colors: [color(v0.h, 0, lightness), color(v0.h, 1, lightness)],
                                         startPoint: UnitPoint(x: 0.5, y: 232 / size), endPoint: UnitPoint(x: 0.5, y: 40 / size)))
            arc(20, 160, LinearGradient(colors: [color(v0.h, v0.s, 0.72), color(v0.h, v0.s, 0.32)],
                                        startPoint: UnitPoint(x: 0.5, y: 40 / size), endPoint: UnitPoint(x: 0.5, y: 232 / size)))
            curved("SATURATION", around: 270)
            curved("SHADE", around: 90)
            knob(at: pt(v0.h, 117.5), fill: color(v0.h, 1, 0.5), r: 11, line: 3.5)
            knob(at: pt(200 + v0.s * 140, 86), fill: Color(hex: hex), r: 9, line: 3)
            knob(at: pt(20 + v0.depth / 100 * 140, 86), fill: Color(hex: hex), r: 9, line: 3)
            core(hex)
        }
        .frame(width: size, height: size)
        .shadow(color: .black.opacity(0.28), radius: 24, y: 14)
        .contentShape(Circle())
        .gesture(
            DragGesture(minimumDistance: 0)
                .onChanged { g in turn(g.startLocation, g.location) }
                .onEnded { _ in
                    mode = nil
                    commit()
                }
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

    private func arc<S: ShapeStyle>(_ a0: Double, _ a1: Double, _ fill: S) -> some View {
        Path { p in
            p.addArc(center: CGPoint(x: c, y: c), radius: 86, startAngle: .degrees(a0 - 90), endAngle: .degrees(a1 - 90), clockwise: false)
        }
        .stroke(fill, style: StrokeStyle(lineWidth: 14, lineCap: .round))
    }

    /// A word set along its arc at 69 from the middle — between the core's ring (59) and the arcs (79 in), so 11-point
    /// letters clear both — each letter turned to the arc (the web's textPath): up the left side for saturation, down
    /// the right for the shade.
    private func curved(_ text: String, around centre: Double) -> some View {
        let letters = Array(text)
        let radius: CGFloat = 69
        // (each letter's own width at 11 points, with the web's .14em tracking, in degrees at this radius — a narrow I
        // leaves no gap)
        let widths = letters.map { ch -> Double in ("IJ1".contains(ch) ? 4.4 : ch == "M" || ch == "W" ? 9.8 : 8.0) + 1.55 }
        let total = widths.reduce(0, +)
        let toDeg = 180 / Double.pi / Double(radius)
        let centres = widths.indices.map { i in centre + (widths[..<i].reduce(0, +) + widths[i] / 2 - total / 2) * toDeg }
        return ZStack {
            ForEach(Array(letters.enumerated()), id: \.offset) { i, ch in
                let a = centres[i]
                Text(String(ch))
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(.secondary)
                    .rotationEffect(.degrees(a))
                    .position(pt(a, radius))
            }
        }
        .frame(width: size, height: size)
        .allowsHitTesting(false)
    }

    private func knob(at p: CGPoint, fill: Color, r: CGFloat, line: CGFloat) -> some View {
        Circle()
            .fill(fill)
            .overlay(Circle().strokeBorder(.white, lineWidth: line))
            .shadow(color: .black.opacity(0.3), radius: 2.5, y: 1)
            .frame(width: r * 2, height: r * 2)
            .position(p)
            .allowsHitTesting(false)
    }

    /// The colour as words, by day over by night, ringed in the colour; its code on it, in it (1.3.148: where Done was).
    private func core(_ hex: String) -> some View {
        let ns = NSColor(Color(hex: hex))
        let dark = NSColor(srgbRed: 28 / 255, green: 28 / 255, blue: 30 / 255, alpha: 1)
        let onLight = Color(nsColor: AppearanceStore.readable(ns, on: .white, ratio: 4.5))
        let onDark = Color(nsColor: AppearanceStore.readable(ns, on: dark, ratio: 4.5))
        let ink: Color = AppearanceStore.luminance(ns) > 0.36 ? Color(red: 28 / 255, green: 28 / 255, blue: 30 / 255) : .white
        return ZStack {
            VStack(spacing: 0) {
                Text("Aa").font(.system(size: 17, weight: .bold)).foregroundStyle(onLight)
                    .padding(.top, 14)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                    .background(Color.white)
                Text("Aa").font(.system(size: 17, weight: .bold)).foregroundStyle(onDark)
                    .padding(.bottom, 14)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
                    .background(Color(nsColor: dark))
            }
            .clipShape(Circle())
            HexField(hex: hex, ink: ink, wear: wear)
        }
        .frame(width: 112, height: 112)
        .overlay(Circle().strokeBorder(Color(hex: hex), lineWidth: 3).padding(-3))
        .shadow(color: .black.opacity(0.25), radius: 9, y: 6)
        .help("How your colour reads as words, by day and by night")
    }

    /// A drag on the picker: the part it began on follows the pointer (the core's code keeps its own presses).
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
        var v = shown
        switch mode {
        case .hue: v.h = q.a
        case .sat: v.s = min(max((q.a - 200) / 140, 0), 1)
        case .depth: v.depth = min(max((q.a - 20) / 140, 0), 1) * 100
        case nil: return
        }
        preview(v)
    }

    /// (onboarding-24) A colour code typed ("#2f7cf6", "2F7CF6", "#28f"): its hue, saturation and lightness, the
    /// lightness kept within the wheel's shades (0.72 light to 0.32 deep — never so pale or so dark that words in it
    /// stop reading), worn at once. False for what is not a code.
    private func wear(_ raw: String) -> Bool {
        guard let code = AppearanceStore.normalize(raw), let n = UInt32(code.dropFirst(), radix: 16) else { return false }
        let r = Double((n >> 16) & 0xff) / 255, g = Double((n >> 8) & 0xff) / 255, b = Double(n & 0xff) / 255
        let hi = max(r, g, b), lo = min(r, g, b), d = hi - lo
        let l = (hi + lo) / 2
        var v = shown
        if d > 0.0001 {
            v.s = min(max(d / (1 - abs(2 * l - 1)), 0), 1)
            var h: Double
            if hi == r {
                h = 60 * ((g - b) / d).truncatingRemainder(dividingBy: 6)
            } else if hi == g {
                h = 60 * ((b - r) / d + 2)
            } else {
                h = 60 * ((r - g) / d + 4)
            }
            if h < 0 { h += 360 }
            v.h = h
        } else {
            v.s = 0 // (a grey: the hue left where it was)
        }
        v.depth = min(max((72 - l * 100) / 0.4, 0), 100)
        live = nil
        settle?.cancel()
        change(v)
        return true
    }
}

/// (onboarding-24) The colour as a code, to read or to type — (1.3.148) in the wheel's middle, on a capsule of the colour,
/// where Done was. Six digits typed wear it as they come (a three-digit code once Return is pressed or the field is left);
/// a code that is not one goes back to the colour worn. (1.3.149) Only what was typed is worn: the field takes focus as
/// the wheel opens, and kept the code it opened with while the wheel turned — so clicking out wore that old code again.
private struct HexField: View {
    let hex: String
    let ink: Color
    let wear: (String) -> Bool
    @State private var text = ""
    /// Something typed that is not worn yet.
    @State private var typed = false
    @FocusState private var focused: Bool

    var body: some View {
        TextField("Colour code", text: $text, prompt: Text("#2f7cf6"))
            .labelsHidden()
            .textFieldStyle(.plain)
            .font(.system(size: 11.5, weight: .semibold).monospaced())
            .foregroundStyle(ink)
            .multilineTextAlignment(.center)
            .autocorrectionDisabled()
            .frame(width: 70)
            .padding(.horizontal, 6)
            .frame(height: 26)
            .background(Capsule().fill(Color(hex: hex)))
            .overlay(Capsule().strokeBorder(ink.opacity(focused ? 0.55 : 0), lineWidth: 1.5))
            .shadow(color: .black.opacity(0.28), radius: 4, y: 2)
            .focused($focused)
            .onSubmit(apply)
            .accessibilityLabel("Colour code")
            .help("The colour's code: type one to wear it")
            .onAppear { text = hex }
            .onChange(of: hex) { _, h in if !typed { text = h } } // (the wheel turned: its code, focused or not)
            .onChange(of: text) { _, t in
                guard focused, t != hex else { return }
                typed = true
                // (a whole six-digit code, worn as it is typed: the wheel and the window follow)
                let digits = t.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "#", with: "")
                if digits.count == 6, wear(t) { typed = false }
            }
            .onChange(of: focused) { _, on in if !on { apply() } }
    }

    private func apply() {
        guard typed else { return }
        typed = false
        if !wear(text) { text = hex }
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
                // (onboarding-24) a pop-up that says what the place wears ("Drawing: Ocean"), not a word that hid it
                HStack(spacing: 6) {
                    Text("Drawing")
                    Picker("Drawing", selection: drawing) {
                        if drawing.wrappedValue == PhotoSlotEditor.yours {
                            Text("Your Photo").tag(PhotoSlotEditor.yours)
                        }
                        Text("None").tag("")
                        Divider()
                        ForEach(AppearanceStore.scenes, id: \.self) { s in
                            Text(s).tag(s)
                        }
                    }
                    .labelsHidden()
                    .pickerStyle(.menu)
                    .fixedSize()
                }
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

    /// The drawing pop-up's tag for a photo of the student's own (no drawing is called that).
    private static let yours = "photo:own"

    /// What the place wears, as the Drawing pop-up says it: a drawing's name, the student's own photo, or none ("").
    /// Picked: that drawing, or nothing (Remove's job too); the photo's own row is only ever the one shown.
    private var drawing: Binding<String> {
        Binding(get: {
            switch photo?.source {
            case .some(.scene(let s)): return s
            case .some(.upload): return PhotoSlotEditor.yours
            case .none: return ""
            }
        }, set: { v in
            if v.isEmpty {
                if photo != nil { store.remove(slot) }
            } else if v != PhotoSlotEditor.yours {
                store.setScene(v, in: slot)
            }
        })
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

/// A place's photo editor in a popup over the window (a counter's context menu: Card Photo…; 1.3.79: never blocking it).
struct PhotoSlotSheet: View {
    let slot: PhotoSlot
    /// (1.3.79) Its popup, which Done closes.
    @Environment(\.popup) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            PhotoSlotEditor(slot: slot)
            HStack {
                Text("Every place's photo: the account's ⋯ menu ▸ Appearance.")
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
            // (home-17: a counter's name in full — "Graded this week" — on two lines where one is too narrow, never cut)
            Text(slot.title)
                .font(.sCaption2)
                .foregroundStyle(picked ? .primary : .secondary)
                .lineLimit(2)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: slot.isSide ? nil : 78)
        }
    }
}

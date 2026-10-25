import AppKit
import SwiftUI

// (1.3.166) A photo placed where it is, as the web has it since 2.99.108 (content/app/appearance.js edit): uploaded, or
// Move and Zoom… from its photo button, the place it went on is the only one lit, outlined; the photo is dragged on it to
// move it and pinched to zoom it. A tip hangs over it and its settings under it (over it where there is no room under;
// beside the sidebar): the zoom, Ink (a photo of one's own), Reset, Replace…, Remove and Done. The bar at the foot steps
// aside meanwhile. Done or Escape ends it, the photo kept as it was left.

/// The place being edited, outlined and taking the drag and the pinch; its settings and its tip round it.
struct PhotoEditLayer: View {
    let slot: PhotoSlot
    /// The place, in the overlay's own space.
    let frame: CGRect
    let bounds: CGSize
    let done: () -> Void
    @State private var barSize = CGSize(width: 560, height: 52)
    @State private var tipSize = CGSize(width: 220, height: 26)

    private static let margin: CGFloat = 12
    private static let gap: CGFloat = 10

    var body: some View {
        let at = placed()
        ZStack(alignment: .topLeading) {
            PhotoEditRing(slot: slot, frame: frame)
            PhotoEditBar(slot: slot, beside: slot.isSide, done: done)
                .fixedSize()
                .readSize { barSize = $0 }
                .offset(x: at.bar.x, y: at.bar.y)
                .transition(.opacity.combined(with: .scale(scale: 0.97, anchor: at.below ? .top : .bottom)))
            PhotoEditTip()
                .fixedSize()
                .readSize { tipSize = $0 }
                .offset(x: at.tip.x, y: at.tip.y)
                .allowsHitTesting(false)
                .transition(.opacity)
        }
        .frame(width: bounds.width, height: bounds.height, alignment: .topLeading)
    }

    /// Where the settings and the tip go: under the place, centred on it (over it where there is no room under);
    /// beside the sidebar, the tip over the settings — kept inside the window.
    private func placed() -> (bar: CGPoint, tip: CGPoint, below: Bool) {
        let m = Self.margin, g = Self.gap
        let bw = barSize.width, bh = barSize.height, tw = tipSize.width, th = tipSize.height
        let clampX = { (x: CGFloat, w: CGFloat) in min(max(x, m), max(m, bounds.width - w - m)) }
        let clampY = { (y: CGFloat, h: CGFloat) in min(max(y, m), max(m, bounds.height - h - m)) }
        if slot.isSide {
            let bx = clampX(frame.maxX + 16, bw)
            let by = clampY(frame.midY - bh / 2, bh)
            return (CGPoint(x: bx, y: by), CGPoint(x: clampX(bx, tw), y: clampY(by - 8 - th, th)), true)
        }
        let below = frame.maxY + g + bh + m <= bounds.height || frame.minY - g - bh - th - g < m
        let bx = clampX(frame.midX - bw / 2, bw)
        let by = clampY(below ? frame.maxY + g : frame.minY - g - bh, bh)
        let ty = below ? frame.minY - g - th : frame.maxY + g
        return (CGPoint(x: bx, y: by), CGPoint(x: clampX(frame.midX - tw / 2, tw), y: clampY(ty, th)), below)
    }
}

/// The place's outline (just outside a card; inside the sidebar's own edge): the photo under it follows the pointer as it
/// is dragged, and zooms as it is pinched — kept as each ends.
private struct PhotoEditRing: View {
    let slot: PhotoSlot
    let frame: CGRect
    @ObservedObject private var store = AppearanceStore.shared
    @State private var dragFrom: PhotoPlace?
    @State private var zoomFrom: Double?
    @State private var hovering = false

    var body: some View {
        let o: CGFloat = slot.isSide ? 0 : 3
        let shape = RoundedRectangle(cornerRadius: slot.isSide ? 0 : 21, style: .continuous)
        shape
            .fill(Color.white.opacity(0.001)) // (presses on the place are the ring's: the card under it does not open)
            .overlay(shape.strokeBorder(Theme.accent, lineWidth: 2))
            .background(slot.isSide ? nil : shape.inset(by: -4).stroke(Theme.accent.opacity(0.22), lineWidth: 6))
            .frame(width: frame.width + 2 * o, height: frame.height + 2 * o)
            .offset(x: frame.minX - o, y: frame.minY - o)
            .contentShape(shape)
            .gesture(move.simultaneously(with: pinch))
            .onHover { inside in
                guard inside != hovering else { return }
                hovering = inside
                if inside { NSCursor.openHand.push() } else { NSCursor.pop() }
            }
            .onDisappear { if hovering { NSCursor.pop() } }
            .accessibilityLabel(Text("\(slot.isSide ? "Sidebar" : slot.title) photo: drag to move it, pinch to zoom"))
    }

    /// The photo follows the pointer: the point pinned moves the other way across the place as the photo is pulled
    /// (the editor's own sum, PhotoSlotEditor, over the place as it is on screen).
    private var move: some Gesture {
        DragGesture(minimumDistance: 1)
            .onChanged { g in
                guard let photo = store.photos[slot], let pic = store.image(photo, in: slot) else { return }
                let from = dragFrom ?? photo.place
                if dragFrom == nil { dragFrom = from }
                let iw = max(pic.image.size.width, 1), ih = max(pic.image.size.height, 1)
                let k = max(frame.width / iw, frame.height / ih) * from.z
                let spareX = frame.width - iw * k, spareY = frame.height - ih * k
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
                guard let photo = store.photos[slot] else { return }
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

/// "Drag to move · pinch to zoom", over the place.
private struct PhotoEditTip: View {
    var body: some View {
        Label("Drag to move · pinch to zoom", systemImage: "arrow.up.and.down.and.arrow.left.and.right")
            .font(.system(size: 11.5, weight: .semibold))
            .foregroundStyle(.white)
            .padding(.horizontal, 11)
            .frame(height: 26)
            .background(Capsule().fill(Color(white: 0.11).opacity(0.86)))
            .shadow(color: .black.opacity(0.22), radius: 7, y: 3)
    }
}

/// The place's settings: its name and what it wears, the zoom, Ink (a photo of one's own), Reset, Replace…, Remove and
/// Done — in a row under a card, in a column beside the sidebar.
private struct PhotoEditBar: View {
    let slot: PhotoSlot
    let beside: Bool
    let done: () -> Void
    @ObservedObject private var store = AppearanceStore.shared

    private var photo: SlotPhoto? { store.photos[slot] }
    private var own: Bool { if case .upload? = photo?.source { return true } else { return false } }

    var body: some View {
        let layout = beside ? AnyLayout(VStackLayout(alignment: .leading, spacing: 10)) : AnyLayout(HStackLayout(spacing: 12))
        layout {
            VStack(alignment: .leading, spacing: 1) {
                Text(slot.isSide ? "Sidebar" : slot.title)
                    .font(.sCallout.weight(.semibold))
                Text(own ? (store.isPlain(slot) ? "Your photo, as it is" : "Your photo, inked") : "A drawing")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
            rule
            HStack(spacing: 6) {
                Button { nudge(1 / 1.15) } label: { Image(systemName: "minus.magnifyingglass") }
                    .buttonStyle(.borderless)
                    .help("Zoom out")
                Slider(value: zoom, in: PhotoPlace.zoom) { editing in if !editing { store.commitPlace() } }
                    .frame(width: beside ? 150 : 110)
                    .accessibilityLabel(Text("Zoom"))
                Button { nudge(1.15) } label: { Image(systemName: "plus.magnifyingglass") }
                    .buttonStyle(.borderless)
                    .help("Zoom in")
            }
            if own {
                rule
                Toggle("Ink", isOn: Binding(get: { !store.isPlain(slot) }, set: { store.setInk($0, in: slot) }))
                    .toggleStyle(.switch)
                    .controlSize(.small)
                    .help("On: the photo drawn in two tones of the page. Off: the photo as it is, the words on it bordered in white.")
            }
            rule
            HStack(spacing: 8) {
                Button("Reset") {
                    store.setPlace(slot.defaultPlace, in: slot)
                    store.commitPlace()
                }
                .disabled(photo?.place == slot.defaultPlace)
                .help("Put the photo back where it started")
                Button("Replace…") { store.upload(to: slot) }
                Button("Remove", role: .destructive) {
                    done()
                    store.remove(slot)
                }
            }
            .fixedSize()
            Button("Done", action: done)
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
                .frame(maxWidth: beside ? .infinity : nil, alignment: .trailing)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(Color.primary.opacity(0.1)))
        .shadow(color: .black.opacity(0.25), radius: 18, y: 8)
        .background {
            // (Escape ends it too, as the web's)
            Button("", action: done)
                .keyboardShortcut(.cancelAction)
                .opacity(0)
                .frame(width: 0, height: 0)
                .accessibilityHidden(true)
        }
    }

    private var rule: some View {
        Rectangle()
            .fill(Color(nsColor: .separatorColor))
            .frame(width: beside ? nil : 1, height: beside ? 1 : 24)
    }

    private var zoom: Binding<Double> {
        Binding(get: { photo?.place.z ?? 1 }, set: { z in
            guard var p = photo?.place else { return }
            p.z = z
            store.setPlace(p, in: slot)
        })
    }

    private func nudge(_ k: Double) {
        guard var p = photo?.place else { return }
        p.z = min(PhotoPlace.zoom.upperBound, max(PhotoPlace.zoom.lowerBound, p.z * k))
        store.setPlace(p, in: slot)
        store.commitPlace()
    }
}

private struct SizeKey: PreferenceKey {
    static let defaultValue: CGSize = .zero
    static func reduce(value: inout CGSize, nextValue: () -> CGSize) { value = nextValue() }
}

extension View {
    /// This view's size as it is laid out, to place it by.
    func readSize(_ got: @escaping (CGSize) -> Void) -> some View {
        background(GeometryReader { g in Color.clear.preference(key: SizeKey.self, value: g.size) })
            .onPreferenceChange(SizeKey.self) { s in MainActor.assumeIsolated { if s.width > 0, s.height > 0 { got(s) } } }
    }
}

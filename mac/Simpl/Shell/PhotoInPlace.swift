import AppKit
import SwiftUI

// (1.3.166) A photo placed where it is, as the web has it since 2.99.108 (content/app/appearance.js edit): uploaded, or
// Move and Zoom… from its photo button, the place it went on is the only one lit, outlined; the photo is dragged on it to
// move it and pinched to zoom it. (1.3.174, as the web's 2.99.117) Its controls surround it, each on a side of its own: a
// tip over it, Reset, Replace… and Remove down its left, the zoom standing up its right, Ink (a photo of one's own) and
// Done under it. A side with no room gives what it holds to the other side, further out (Ink and Done go beside the
// place's foot), or else to the inside of the place's edge (the sidebar's tip sits inside its top). The bar at the foot
// steps aside meanwhile. Done or Escape ends it, the photo kept as it was left.

/// The place being edited, outlined and taking the drag and the pinch; its controls round it.
struct PhotoEditLayer: View {
    let slot: PhotoSlot
    /// The place, in the overlay's own space.
    let frame: CGRect
    let bounds: CGSize
    let done: () -> Void
    @State private var tipSize = CGSize(width: 220, height: 26)
    @State private var actsSize = CGSize(width: 120, height: 104)
    @State private var zoomSize = CGSize(width: 42, height: 190)
    @State private var footSize = CGSize(width: 170, height: 40)

    private static let margin: CGFloat = 12
    private static let gap: CGFloat = 12
    private static let space: CGFloat = 8

    var body: some View {
        let at = placed()
        ZStack(alignment: .topLeading) {
            PhotoEditRing(slot: slot, frame: frame)
            PhotoEditTip()
                .fixedSize()
                .readSize { tipSize = $0 }
                .offset(x: at.tip.x, y: at.tip.y)
                .allowsHitTesting(false)
                .transition(.opacity.combined(with: .scale(scale: 0.94, anchor: .bottom)))
            PhotoEditActs(slot: slot, done: done)
                .fixedSize()
                .readSize { actsSize = $0 }
                .offset(x: at.acts.x, y: at.acts.y)
                .transition(.opacity.combined(with: .scale(scale: 0.94, anchor: .trailing)))
            PhotoEditZoom(slot: slot, length: trackLength)
                .fixedSize()
                .readSize { zoomSize = $0 }
                .offset(x: at.zoom.x, y: at.zoom.y)
                .transition(.opacity.combined(with: .scale(scale: 0.94, anchor: .leading)))
            PhotoEditFoot(slot: slot, done: done)
                .fixedSize()
                .readSize { footSize = $0 }
                .offset(x: at.foot.x, y: at.foot.y)
                .transition(.opacity.combined(with: .scale(scale: 0.94, anchor: .top)))
        }
        .frame(width: bounds.width, height: bounds.height, alignment: .topLeading)
    }

    /// The zoom's track, as long as the place's height allows (its buttons and its pill's padding take the rest).
    private var trackLength: CGFloat { min(max(frame.height - 96, 56), 120).rounded() }

    /// Where each control goes: the tip over the place (else inside its top); Reset, Replace… and Remove to its left and
    /// the zoom to its right, each centred on it (a side with no room gives its own to the other, further out; with room
    /// on neither, each sits inside the place's edge); Ink and Done under it (else beside its foot, past what stands
    /// there, else inside it) — all kept inside the window.
    private func placed() -> (tip: CGPoint, acts: CGPoint, zoom: CGPoint, foot: CGPoint) {
        let m = Self.margin, g = Self.gap, s = Self.space
        let o: CGFloat = slot.isSide ? 0 : 3 // (round a card, just outside it; the sidebar, inside its own edge)
        let w = bounds.width, h = bounds.height
        let clampX = { (x: CGFloat, width: CGFloat) in min(max(x, m), max(m, w - width - m)) }
        let clampY = { (y: CGFloat, height: CGFloat) in min(max(y, m), max(m, h - height - m)) }
        let left = frame.minX - o, right = frame.maxX + o, top = frame.minY - o, bottom = frame.maxY + o
        let aw = actsSize.width, zw = zoomSize.width
        let roomL = left - g - m, roomR = w - right - g - m
        var ax: CGFloat, zx: CGFloat
        if roomL >= aw && roomR >= zw { ax = left - g - aw; zx = right + g }
        else if roomR >= zw + s + aw { zx = right + g; ax = zx + zw + s }
        else if roomL >= aw + s + zw { ax = left - g - aw; zx = ax - s - zw }
        else { ax = frame.minX + g; zx = frame.maxX - g - zw }
        let acts = CGPoint(x: clampX(ax, aw), y: clampY(frame.midY - actsSize.height / 2, actsSize.height))
        let zoom = CGPoint(x: clampX(zx, zw), y: clampY(frame.midY - zoomSize.height / 2, zoomSize.height))
        let tw = tipSize.width, th = tipSize.height
        let tipOut = top - g - th >= m
        let tip = CGPoint(x: clampX(frame.midX - tw / 2, tw), y: clampY(tipOut ? top - g - th : frame.minY + g, th))
        let fw = footSize.width, fh = footSize.height
        var foot = CGPoint(x: frame.midX - fw / 2, y: bottom + g)
        if foot.y + fh > h - m {
            let fy = clampY(frame.maxY - fh, fh)
            let boxes = [CGRect(origin: acts, size: actsSize), CGRect(origin: zoom, size: zoomSize)].filter { $0.minY < fy + fh && $0.maxY > fy }
            let pastRight = boxes.filter { $0.maxX > right }.reduce(right + g) { max($0, $1.maxX + s) }
            let beforeLeft = boxes.filter { $0.minX < left }.reduce(left - g - fw) { min($0, $1.minX - s - fw) }
            if pastRight + fw <= w - m { foot = CGPoint(x: pastRight, y: fy) }
            else if beforeLeft >= m { foot = CGPoint(x: beforeLeft, y: fy) }
            else { foot.y = frame.maxY - g - fh }
        }
        return (tip, acts, zoom, CGPoint(x: clampX(foot.x, fw), y: clampY(foot.y, fh)))
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
            // (1.3.168) the place's shape, the drag, the pinch and the hand set on the ring where it is laid out, and only
            // then the ring moved onto the place: set after the move, they stayed where the ring was laid out — the
            // overlay's top left corner — and a drag or a pinch on the place reached nothing
            .contentShape(shape)
            .gesture(move.simultaneously(with: pinch))
            .onHover { inside in
                guard inside != hovering else { return }
                hovering = inside
                if inside { NSCursor.openHand.push() } else { NSCursor.pop() }
            }
            .onDisappear { if hovering { NSCursor.pop() } }
            .accessibilityLabel(Text("\(slot.isSide ? "Sidebar" : slot.title) photo: drag to move it, pinch to zoom"))
            .offset(x: frame.minX - o, y: frame.minY - o)
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

/// A glass pill round the place, holding some of its controls.
private struct EditPill<Content: View>: View {
    @ViewBuilder let content: Content

    var body: some View {
        content
            .padding(6)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).strokeBorder(Color.primary.opacity(0.1)))
            .shadow(color: .black.opacity(0.25), radius: 16, y: 7)
    }
}

/// Reset, Replace… and Remove, down the place's left.
private struct PhotoEditActs: View {
    let slot: PhotoSlot
    let done: () -> Void
    @ObservedObject private var store = AppearanceStore.shared

    var body: some View {
        EditPill {
            VStack(alignment: .leading, spacing: 2) {
                row("Reset", "arrow.counterclockwise") {
                    store.setPlace(slot.defaultPlace, in: slot)
                    store.commitPlace()
                }
                .disabled(store.photos[slot]?.place == slot.defaultPlace)
                .help("Put the photo back where it started")
                row("Replace…", "photo.on.rectangle") { _ = store.upload(to: slot) }
                row("Remove", "trash", destructive: true) {
                    done()
                    store.remove(slot)
                }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(Text("\(slot.isSide ? "Sidebar" : slot.title) photo"))
    }

    private func row(_ title: String, _ icon: String, destructive: Bool = false, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Label(title, systemImage: icon)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .buttonStyle(EditRowStyle(destructive: destructive))
    }
}

/// A row of the left's pill: its words and its sign, a fill under the pointer.
private struct EditRowStyle: ButtonStyle {
    let destructive: Bool

    func makeBody(configuration: Configuration) -> some View {
        EditRow(configuration: configuration, destructive: destructive)
    }

    private struct EditRow: View {
        let configuration: ButtonStyleConfiguration
        let destructive: Bool
        @Environment(\.isEnabled) private var enabled
        @State private var hovering = false

        var body: some View {
            configuration.label
                .font(.system(size: 12.5, weight: .semibold))
                .foregroundStyle(destructive ? Color.red : Color.primary)
                .padding(.horizontal, 10)
                .frame(height: 28)
                .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(Color.primary.opacity(hovering && enabled ? 0.08 : 0)))
                .contentShape(Rectangle())
                .opacity(enabled ? 1 : 0.45)
                .scaleEffect(configuration.isPressed ? 0.96 : 1)
                .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
                .onHover { hovering = $0 }
        }
    }
}

/// The zoom, standing up the place's right: +, a track dragged or pressed along (drawn as it goes, kept as it is let
/// go), −.
private struct PhotoEditZoom: View {
    let slot: PhotoSlot
    let length: CGFloat
    @ObservedObject private var store = AppearanceStore.shared
    @State private var dragging = false

    private var z: Double { store.photos[slot]?.place.z ?? 1 }

    var body: some View {
        let range = PhotoPlace.zoom
        EditPill {
            VStack(spacing: 6) {
                roundButton("plus", "Zoom in", off: z >= range.upperBound - 0.001) { nudge(1.15) }
                track(range)
                roundButton("minus", "Zoom out", off: z <= range.lowerBound + 0.001) { nudge(1 / 1.15) }
            }
        }
    }

    private func track(_ range: ClosedRange<Double>) -> some View {
        let f = CGFloat(min(max((z - range.lowerBound) / (range.upperBound - range.lowerBound), 0), 1))
        let run = max(length - 18, 1)
        return ZStack(alignment: .bottom) {
            Capsule().fill(Color.primary.opacity(0.18)).frame(width: 4).padding(.vertical, 9)
            Capsule().fill(Theme.accent).frame(width: 4, height: f * run).padding(.bottom, 9)
            Circle()
                .fill(Color.white)
                .overlay(Circle().strokeBorder(Color.black.opacity(0.12), lineWidth: 0.5))
                .shadow(color: .black.opacity(0.3), radius: 2, y: 1)
                .frame(width: 18, height: 18)
                .scaleEffect(dragging ? 1.12 : 1)
                .offset(y: -f * run)
        }
        .frame(width: 28, height: length)
        .contentShape(Rectangle())
        .gesture(
            DragGesture(minimumDistance: 0)
                .onChanged { g in
                    if !dragging { withAnimation(.easeOut(duration: 0.12)) { dragging = true } }
                    let at = Double(min(max((length - 9 - g.location.y) / run, 0), 1))
                    set(range.lowerBound + at * (range.upperBound - range.lowerBound))
                }
                .onEnded { _ in
                    withAnimation(.easeOut(duration: 0.12)) { dragging = false }
                    store.commitPlace()
                }
        )
        .accessibilityElement()
        .accessibilityLabel(Text("Zoom"))
        .accessibilityValue(Text("\(Int((z * 100).rounded()))%"))
        .accessibilityAdjustableAction { d in nudge(d == .increment ? 1.1 : 1 / 1.1) }
    }

    private func roundButton(_ icon: String, _ label: String, off: Bool, _ act: @escaping () -> Void) -> some View {
        Button(action: act) {
            Image(systemName: icon)
                .font(.system(size: 12, weight: .bold))
                .frame(width: 28, height: 28)
                .background(Circle().fill(Color.primary.opacity(0.09)))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .disabled(off)
        .opacity(off ? 0.4 : 1)
        .help(label)
        .accessibilityLabel(Text(label))
    }

    private func set(_ zoom: Double) {
        guard var p = store.photos[slot]?.place else { return }
        p.z = (zoom * 100).rounded() / 100
        store.setPlace(p, in: slot)
    }

    private func nudge(_ k: Double) {
        guard var p = store.photos[slot]?.place else { return }
        p.z = min(PhotoPlace.zoom.upperBound, max(PhotoPlace.zoom.lowerBound, p.z * k))
        store.setPlace(p, in: slot)
        store.commitPlace()
    }
}

/// Ink (a photo of one's own) and Done, under the place.
private struct PhotoEditFoot: View {
    let slot: PhotoSlot
    let done: () -> Void
    @ObservedObject private var store = AppearanceStore.shared

    private var own: Bool { if case .upload? = store.photos[slot]?.source { return true } else { return false } }

    var body: some View {
        EditPill {
            HStack(spacing: 12) {
                if own {
                    Toggle("Ink", isOn: Binding(get: { !store.isPlain(slot) }, set: { store.setInk($0, in: slot) }))
                        .toggleStyle(.switch)
                        .controlSize(.small)
                        .help("On: the photo drawn in two tones of the page. Off: the photo as it is, the words on it bordered in white.")
                        .padding(.leading, 8)
                }
                Button("Done", action: done)
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
            }
        }
        .background {
            // (Escape ends it too, as the web's)
            Button("", action: done)
                .keyboardShortcut(.cancelAction)
                .opacity(0)
                .frame(width: 0, height: 0)
                .accessibilityHidden(true)
        }
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

import AppKit
import SwiftUI

// (1.3.110) An element pressed in the Periodic Table — the whole tool's or its pin's — opens in a small window of its
// own beside the table: its square, name and kind, its mass and electrons, the numbers most asked for, a line about it,
// and the ways on (its full page, Atoms & Orbitals, the elements either side). One such window: an element pressed while
// it is open shows in it. It floats over the app, is moved by its ground, and closes with ✕ or Escape.

/// Words and ink in an element's colours that read: (1.3.110) the kinds' and the scales' colours are bright — a yellow,
/// a pale blue — and words in them, or white words on them, were hard to read.
enum PTInk {
    /// Words in the colour on the page: stepped darker by day, lighter by night, till they stand 4.5:1.
    static func text(_ c: Color) -> Color {
        let ns = NSColor(c)
        return Color(nsColor: NSColor(name: nil) { a in
            let dark = a.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            return AppearanceStore.readable(ns, on: dark ? Theme.darkPage : Theme.lightPage, ratio: 4.5)
        })
    }

    /// Ink on a solid fill of the colour: white, or near-black where black stands out more (a yellow, an orange).
    static func on(_ c: Color) -> Color { whiteReads(c) ? .white : Color(white: 0.08) }

    static func whiteReads(_ c: Color) -> Bool {
        let l = AppearanceStore.luminance(NSColor(c))
        let white = 1.05 / (l + 0.05), black = (l + 0.05) / 0.05
        return white >= 4.5 || white >= black
    }
}

/// The one mini window, and what it shows.
@MainActor
final class PTMini: ObservableObject {
    static let shared = PTMini()

    @Published var element: PTElement?
    /// The way to the element's full page, from the table that opened it.
    var openFull: ((PTElement) -> Void)?
    private var panel: PTMiniPanel?

    /// The element shown, the window opened beside the pointer (or left where it was put, if it is open already).
    func show(_ e: PTElement, openFull: @escaping (PTElement) -> Void) {
        self.openFull = openFull
        element = e
        let panel = self.panel ?? make()
        self.panel = panel
        if !panel.isVisible {
            let mouse = NSEvent.mouseLocation
            let size = panel.frame.size
            let screen = (NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main)?.visibleFrame ?? .zero
            // (to the pointer's right, its top at the pointer — the left where there is no room; on the screen)
            var x = mouse.x + 24
            if x + size.width > screen.maxX { x = mouse.x - 24 - size.width }
            let y = min(max(mouse.y - size.height + 40, screen.minY + 8), screen.maxY - size.height - 8)
            panel.setFrameOrigin(NSPoint(x: max(screen.minX + 8, x), y: y))
        }
        panel.orderFront(nil)
    }

    func close() { panel?.orderOut(nil) }

    func step(_ d: Int) {
        guard let e = element, let next = PeriodicTable.byNumber(e.number + d) else { return }
        withAnimation(Motion.gentle) { element = next }
    }

    private func make() -> PTMiniPanel {
        let panel = PTMiniPanel()
        let host = NSHostingView(rootView: PTMiniView().environmentObject(self))
        host.sizingOptions = []
        host.frame = NSRect(origin: .zero, size: panel.frame.size)
        host.autoresizingMask = [.width, .height]
        let holder = NSView(frame: host.frame)
        holder.addSubview(host)
        panel.contentView = holder
        return panel
    }
}

final class PTMiniPanel: NSPanel {
    static let size = NSSize(width: 340, height: 452)

    init() {
        super.init(contentRect: NSRect(origin: .zero, size: Self.size),
                   styleMask: [.borderless, .nonactivatingPanel, .fullSizeContentView], backing: .buffered, defer: false)
        level = .floating
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isMovableByWindowBackground = true
        isReleasedWhenClosed = false
        animationBehavior = .utilityWindow
        collectionBehavior = [.fullScreenAuxiliary, .moveToActiveSpace]
        hidesOnDeactivate = UserDefaults.standard.string(forKey: "SimplShotFile") == nil
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
    override func cancelOperation(_ sender: Any?) { PTMini.shared.close() }

    /// ← and →: the elements either side.
    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 123: PTMini.shared.step(-1)
        case 124: PTMini.shared.step(1)
        default: super.keyDown(with: event)
        }
    }
}

private struct PTMiniView: View {
    @EnvironmentObject private var mini: PTMini

    var body: some View {
        Group {
            if let e = mini.element { card(e).id(e.number).transition(.opacity) }
        }
        .frame(width: PTMiniPanel.size.width, height: PTMiniPanel.size.height, alignment: .top)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).strokeBorder(Color.primary.opacity(0.1)))
        .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        .animation(Motion.gentle, value: mini.element?.number)
    }

    private func card(_ e: PTElement) -> some View {
        let color = e.kind.color
        return VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 6) {
                Button { mini.close() } label: {
                    Image(systemName: "xmark").font(.system(size: 10, weight: .bold)).frame(width: 18, height: 18)
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .help("Close (Esc)")
                Spacer()
                Button { mini.step(-1) } label: { Image(systemName: "chevron.left") }
                    .buttonStyle(.borderless)
                    .disabled(e.number <= 1)
                    .help("The element before (←)")
                Button { mini.step(1) } label: { Image(systemName: "chevron.right") }
                    .buttonStyle(.borderless)
                    .disabled(e.number >= PeriodicTable.elements.count)
                    .help("The element after (→)")
            }
            HStack(alignment: .center, spacing: 14) {
                VStack(spacing: 0) {
                    Text("\(e.number)").font(.system(size: 12, weight: .semibold).monospacedDigit())
                        .frame(maxWidth: .infinity, alignment: .leading)
                    Text(e.symbol).font(.system(size: 34, weight: .bold)).minimumScaleFactor(0.5)
                    Text(e.massText).font(.system(size: 10.5).monospacedDigit()).lineLimit(1).minimumScaleFactor(0.6)
                }
                .padding(8)
                .frame(width: 84, height: 84)
                .foregroundStyle(PTInk.on(color))
                .background(color, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
                VStack(alignment: .leading, spacing: 4) {
                    Text(e.name).font(.system(size: 24, weight: .bold)).lineLimit(1).minimumScaleFactor(0.6)
                    Text(e.kind.label).font(.sCallout.weight(.semibold)).foregroundStyle(PTInk.text(color))
                    Text("Group \(e.group.map(String.init) ?? "—") · Period \(e.period) · \(e.block)-block")
                        .font(.sCaption).foregroundStyle(.secondary)
                }
                Spacer(minLength: 0)
            }
            Text(PTConfig.pretty(e))
                .font(.system(size: 12.5).monospaced())
                .lineLimit(1)
                .minimumScaleFactor(0.6)
            LazyVGrid(columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)], spacing: 8) {
                fact("State", e.stateWord)
                fact("Electroneg.", e.en.map { PTElement.trim($0) } ?? "—")
                fact("Density", e.densityText)
                fact("Melts", PTElement.celsius(e.melt))
                fact("Boils", PTElement.celsius(e.boil))
                fact("Valence e⁻", e.valenceElectrons.map(String.init) ?? "—")
            }
            Text(e.summary.isEmpty ? (e.found.map { "Discovered by \($0)." } ?? " ") : e.summary)
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .lineLimit(4)
                .frame(maxWidth: .infinity, minHeight: 58, alignment: .topLeading)
            Spacer(minLength: 0)
            HStack(spacing: 8) {
                Button {
                    mini.openFull?(e)
                    mini.close()
                } label: { Label("Full Page", systemImage: "arrow.up.left.and.arrow.down.right").frame(maxWidth: .infinity) }
                .glassButton(prominent: true)
                .help("\(e.name)'s whole page in the table")
                Button {
                    ToolsCenter.shared.popUp(.atoms, info: ["element": String(e.number)])
                    mini.close()
                } label: { Label("Atom in 3D", systemImage: ToolKind.atoms.symbol).frame(maxWidth: .infinity) }
                .glassButton()
                .help("\(e.name)'s atom, orbitals and electron cloud, in Atoms & Orbitals")
            }
            .controlSize(.large)
        }
        .padding(16)
    }

    private func fact(_ k: String, _ v: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(k).font(.sCaption2).foregroundStyle(.secondary).lineLimit(1)
            Text(v).font(.system(size: 13.5, weight: .semibold).monospacedDigit()).lineLimit(1).minimumScaleFactor(0.6)
        }
        .padding(8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }
}

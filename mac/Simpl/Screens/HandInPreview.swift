import AppKit
import SwiftUI

/// (1.3) A file chosen for Hand In, looked at before it goes: Quick Look's own view of it in a window of its own that
/// grows out of the sheet's right edge (on the house's strong ease-out; under Reduce Motion it fades in where it ends),
/// level with the sheet and moving with it. The same file pressed again, its window's close button, the file taken out
/// or the sheet closed puts it away, folding back into the sheet's edge. With no room on the screen to the right, it
/// grows to the left instead.
@MainActor
final class HandInPreview: NSObject, ObservableObject, NSWindowDelegate {
    static let shared = HandInPreview()

    /// The file being looked at, if one is.
    @Published private(set) var showing: UUID?
    private var panel: NSPanel?
    private var folder: URL?
    private static let width: CGFloat = 560
    private static let gap: CGFloat = 10
    private static let ease = CAMediaTimingFunction(controlPoints: 0.23, 1, 0.32, 1)

    private var reduceMotion: Bool { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }

    /// A file's row pressed: its preview opened beside the sheet, or — the one already open — put away.
    func toggle(_ f: PickedFile, beside sheet: NSWindow?) {
        if showing == f.id {
            close()
        } else {
            show(f, beside: sheet)
        }
    }

    private func show(_ f: PickedFile, beside sheet: NSWindow?) {
        guard let sheet, let file = write(f) else { return }
        let target = frame(beside: sheet)
        let p = panel ?? makePanel()
        let fresh = !p.isVisible
        p.title = f.name
        p.contentView = NSHostingView(rootView: QuickLookView(file: file).id(file))
        showing = f.id
        if fresh {
            // (it starts as a sliver at the sheet's edge, and grows away from it)
            let fromRight = target.minX >= sheet.frame.maxX
            let start = NSRect(x: fromRight ? target.minX : target.maxX - 60, y: target.minY, width: 60, height: target.height)
            p.setFrame(reduceMotion ? target : start, display: false)
            p.alphaValue = 0
            if p.parent !== sheet {
                p.parent?.removeChildWindow(p)
                sheet.addChildWindow(p, ordered: .above)
            }
            p.orderFront(nil)
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = reduceMotion ? 0.18 : 0.34
                ctx.timingFunction = Self.ease
                if !reduceMotion { p.animator().setFrame(target, display: true) }
                p.animator().alphaValue = 1
            }
        } else {
            p.setFrame(target, display: true, animate: false)
        }
    }

    /// The preview put away (folding back into the sheet's edge), and its copy of the file removed.
    func close(animated: Bool = true) {
        showing = nil
        guard let p = panel, p.isVisible else { clean(); return }
        let parent = p.parent
        let finish = { [weak self] in
            p.orderOut(nil)
            parent?.removeChildWindow(p)
            self?.clean()
        }
        guard animated else { finish(); return }
        var end = p.frame
        if !reduceMotion {
            let towardRight = (parent?.frame.maxX ?? end.minX) <= end.minX + 1
            end = NSRect(x: towardRight ? end.minX : end.maxX - 60, y: end.minY, width: 60, height: end.height)
        }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = reduceMotion ? 0.15 : 0.24
            ctx.timingFunction = Self.ease
            if !reduceMotion { p.animator().setFrame(end, display: true) }
            p.animator().alphaValue = 0
        }, completionHandler: { Task { @MainActor in finish() } })
    }

    /// A file taken out of the hand-in: its preview, if it is the one open, put away.
    func removed(_ f: PickedFile) {
        if showing == f.id { close() }
    }

    // MARK: - The window

    private func makePanel() -> NSPanel {
        let p = NSPanel(contentRect: NSRect(x: 0, y: 0, width: Self.width, height: 500),
                        styleMask: [.titled, .closable, .resizable, .fullSizeContentView],
                        backing: .buffered, defer: true)
        p.isReleasedWhenClosed = false
        p.becomesKeyOnlyIfNeeded = true
        p.titlebarAppearsTransparent = true
        p.animationBehavior = .none // (its own growing is the animation)
        p.minSize = NSSize(width: 280, height: 240)
        p.delegate = self
        panel = p
        return p
    }

    /// Level with the sheet, to its right — or to its left when the screen has no room for it there.
    private func frame(beside sheet: NSWindow) -> NSRect {
        let s = sheet.frame
        let screen = (sheet.screen ?? NSScreen.main)?.visibleFrame ?? .infinite
        let right = screen.maxX - (s.maxX + Self.gap)
        let left = (s.minX - Self.gap) - screen.minX
        if right >= 320 || right >= left {
            let w = min(Self.width, max(right, 280))
            return NSRect(x: s.maxX + Self.gap, y: s.minY, width: w, height: s.height)
        }
        let w = min(Self.width, max(left, 280))
        return NSRect(x: s.minX - Self.gap - w, y: s.minY, width: w, height: s.height)
    }

    /// The file written where Quick Look can read it, under its own name (a folder of its own each time).
    private func write(_ f: PickedFile) -> URL? {
        clean()
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("SimplHandInPreview-\(f.id.uuidString)", isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let name = f.name.replacingOccurrences(of: "/", with: "-")
            let url = dir.appendingPathComponent(name.isEmpty ? "File" : name)
            try f.data.write(to: url, options: .atomic)
            folder = dir
            return url
        } catch {
            return nil
        }
    }

    private func clean() {
        if let folder { try? FileManager.default.removeItem(at: folder) }
        folder = nil
    }

    // MARK: - NSWindowDelegate

    nonisolated func windowWillClose(_ notification: Notification) {
        Task { @MainActor in
            self.showing = nil
            if let p = self.panel { p.parent?.removeChildWindow(p) }
            self.clean()
        }
    }
}

/// The window a view is in, kept for whoever needs it (the Hand In sheet's own, for its preview to grow beside).
final class WindowRef: ObservableObject {
    weak var window: NSWindow?
}

struct WindowProbe: NSViewRepresentable {
    let ref: WindowRef

    func makeNSView(context: Context) -> NSView {
        let v = ProbeView()
        v.ref = ref
        return v
    }

    func updateNSView(_ v: NSView, context: Context) {
        (v as? ProbeView)?.ref = ref
        if let w = v.window { ref.window = w }
    }

    private final class ProbeView: NSView {
        var ref: WindowRef?
        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            ref?.window = window
        }
    }
}

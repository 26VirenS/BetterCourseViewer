import SwiftUI
import UIKit

/// The tab bar's glass, measured where UIKit draws it — on any iPhone, in either orientation — so a bar a
/// screen puts above it (Hand In) can take exactly its width and height. It looks for the bar's capsule
/// (the widest rounded view in it, with the Search circle beside it when Search stands apart) and falls back
/// to Apple's usual floating bar (about 21 pt in from each side, 62 pt tall) when it finds none.
struct TabBarProbe: UIViewRepresentable {
    let engine: Engine

    func makeUIView(context: Context) -> ProbeView {
        let v = ProbeView()
        v.engine = engine
        v.isUserInteractionEnabled = false
        v.backgroundColor = .clear
        return v
    }

    func updateUIView(_ v: ProbeView, context: Context) {}

    final class ProbeView: UIView {
        weak var engine: Engine?
        private var logged = false

        override func didMoveToWindow() {
            super.didMoveToWindow()
            measureSoon()
        }

        override func layoutSubviews() {
            super.layoutSubviews()
            measureSoon()
        }

        private func measureSoon() {
            DispatchQueue.main.async { [weak self] in self?.measure() }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { [weak self] in self?.measure() } // (after a rotation settles)
        }

        private func measure() {
            guard let window else { return }
            let size = TabBarProbe.barSize(in: window, log: !logged)
            logged = true
            engine?.setBarSize(size)
        }
    }

    /// A frame every number of which is a real one (a view mid-animation, or one with no size yet, can report
    /// infinity or NaN — never measured, never turned into a whole number).
    static func usable(_ r: CGRect) -> Bool {
        !r.isNull && !r.isInfinite && r.origin.x.isFinite && r.origin.y.isFinite && r.width.isFinite && r.height.isFinite
    }

    static func barSize(in window: UIWindow, log: Bool) -> CGSize {
        let fallback = CGSize(width: max(200, window.bounds.width - max(21, window.safeAreaInsets.left + 8) * 2), height: 62)
        guard let bar = find(UITabBar.self, in: window) else { return fallback }
        let barFrame = bar.convert(bar.bounds, to: window)
        guard usable(barFrame), barFrame.width > 0 else { return fallback }
        var capsules: [CGRect] = []
        var lines: [String] = []
        let n = { (x: CGFloat) -> String in x.isFinite ? String(format: "%.0f", Double(x)) : "?" }
        func walk(_ v: UIView, depth: Int) {
            guard depth < 9 else { return }
            for s in v.subviews where !s.isHidden && s.alpha > 0.01 {
                let f = s.convert(s.bounds, to: window)
                let name = String(describing: type(of: s))
                if log && depth < 6 {
                    lines.append("\(String(repeating: "  ", count: depth))\(name) \(n(f.minX)),\(n(f.minY)) \(n(f.width))x\(n(f.height)) r\(n(s.layer.cornerRadius))")
                }
                guard usable(f), s.layer.cornerRadius.isFinite else { walk(s, depth: depth + 1); continue }
                let rounded = s.layer.cornerRadius >= f.height / 2 - 3 || name.contains("Platter") || name.contains("Glass")
                if rounded && f.height >= 40 && f.height <= 100 && f.width >= f.height - 1 && f.width <= barFrame.width + 1 {
                    capsules.append(f)
                }
                walk(s, depth: depth + 1)
            }
        }
        walk(bar, depth: 0)
        var size = fallback
        if let main = capsules.filter({ $0.width >= window.bounds.width * 0.45 }).max(by: { $0.width < $1.width }) {
            // the tabs' capsule, and the Search circle beside it when it stands apart: the whole bar
            var whole = main
            for c in capsules where !c.intersects(main) && abs(c.midY - main.midY) < 4 && c.height >= main.height * 0.8 {
                whole = whole.union(c)
            }
            size = whole.size
        }
        if !size.width.isFinite || !size.height.isFinite || size.width < 100 || size.height < 30 { size = fallback }
        if log {
            print("[Simpl Courses] tab bar \(barFrame) → \(size)\n" + lines.joined(separator: "\n"))
        }
        return size
    }

    static func find<T: UIView>(_ type: T.Type, in v: UIView) -> T? {
        if let t = v as? T { return t }
        for s in v.subviews {
            if let t = find(type, in: s) { return t }
        }
        return nil
    }
}

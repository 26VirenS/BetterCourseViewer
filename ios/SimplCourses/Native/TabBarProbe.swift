import SwiftUI
import UIKit
import UIKit.UIGestureRecognizerSubclass

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
            if let window, let engine { PaneTouches.install(in: window, engine: engine) }
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
            engine?.setScreen(window.bounds)
            let bar = TabBarProbe.bar(in: window, log: !logged)
            logged = true
            engine?.setBar(bar)
        }
    }

    /// A frame every number of which is a real one (a view mid-animation, or one with no size yet, can report
    /// infinity or NaN — never measured, never turned into a whole number).
    static func usable(_ r: CGRect) -> Bool {
        !r.isNull && !r.isInfinite && r.origin.x.isFinite && r.origin.y.isFinite && r.width.isFinite && r.height.isFinite
    }

    /// The tab bar as drawn: its size, how far in from the screen's sides, and how far its foot sits below the
    /// bottom of the safe area (into the home indicator's strip) — so a bar in its place sits exactly where it was.
    struct Bar: Equatable {
        var size: CGSize
        var side: CGFloat
        var lift: CGFloat
    }

    static func bar(in window: UIWindow, log: Bool) -> Bar {
        let side = max(21, window.safeAreaInsets.left + 8)
        let safeBottom = window.safeAreaInsets.bottom
        // Apple's floating bar where nothing is found: about 21 pt in from each side, 62 pt tall, its foot 20 pt over
        // the screen's bottom edge on a phone with a home indicator (8 pt over the safe area's bottom without one)
        let fallback = Bar(size: CGSize(width: max(200, window.bounds.width - side * 2), height: 62), side: side, lift: safeBottom > 0 ? max(0, safeBottom - 20) : -8)
        guard let tabBar = find(UITabBar.self, in: window) else { return fallback }
        let whole = wholeFrame(tabBar, in: window, log: log)
        guard let whole, usable(whole), whole.width >= 100, whole.height >= 30 else { return fallback }
        return Bar(size: whole.size, side: max(0, whole.minX), lift: safeBottom - (window.bounds.maxY - whole.maxY))
    }

    /// The tabs' capsule, and the Search circle beside it when it stands apart, in window coordinates.
    private static func wholeFrame(_ bar: UITabBar, in window: UIWindow, log: Bool) -> CGRect? {
        let barFrame = bar.convert(bar.bounds, to: window)
        guard usable(barFrame), barFrame.width > 0 else { return nil }
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
        var found: CGRect?
        if let main = capsules.filter({ $0.width >= window.bounds.width * 0.45 }).max(by: { $0.width < $1.width }) {
            // the tabs' capsule, and the Search circle beside it when it stands apart: the whole bar
            var whole = main
            for c in capsules where !c.intersects(main) && abs(c.midY - main.midY) < 4 && c.height >= main.height * 0.8 {
                whole = whole.union(c)
            }
            found = whole
        }
        if log {
            print("[Simpl Courses] tab bar \(barFrame) → \(found.map { "\($0)" } ?? "not found")\n" + lines.joined(separator: "\n"))
        }
        return found
    }

    static func find<T: UIView>(_ type: T.Type, in v: UIView) -> T? {
        if let t = v as? T { return t }
        for s in v.subviews {
            if let t = find(type, in: s) { return t }
        }
        return nil
    }
}

/// (iPhone 1.8.1) Which half of the window a press was on, for the two pages (Engine.push): a recogniser on the window
/// that only looks — it never recognises, never delays a touch and never stops another recogniser — noting where each
/// press begins, and whether it is in a sheet (a sheet's press opens what it opens as before).
final class PaneTouches: UIGestureRecognizer, UIGestureRecognizerDelegate {
    weak var engine: Engine?

    static func install(in window: UIWindow, engine: Engine) {
        if let mine = window.gestureRecognizers?.first(where: { $0 is PaneTouches }) as? PaneTouches {
            mine.engine = engine
            return
        }
        let g = PaneTouches(target: nil, action: nil)
        g.engine = engine
        g.cancelsTouchesInView = false
        g.delaysTouchesBegan = false
        g.delaysTouchesEnded = false
        g.delegate = g
        window.addGestureRecognizer(g)
    }

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent) {
        if let t = touches.first, let w = view {
            let x = t.location(in: w).x
            engine?.touched(x: x, inSheet: PaneTouches.inSheet(t.view))
        }
        state = .failed
    }

    override func canPrevent(_ preventedGestureRecognizer: UIGestureRecognizer) -> Bool { false }
    override func canBePrevented(by preventingGestureRecognizer: UIGestureRecognizer) -> Bool { false }

    func gestureRecognizer(_ gestureRecognizer: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith otherGestureRecognizer: UIGestureRecognizer) -> Bool { true }

    /// Whether a view is in something presented over the app (a sheet, the search, a tool).
    static func inSheet(_ v: UIView?) -> Bool {
        var r: UIResponder? = v
        while let x = r {
            if let vc = x as? UIViewController, vc.presentingViewController != nil { return true }
            r = x.next
        }
        return false
    }
}

import AppKit
import Combine
import SceneKit
import SwiftUI

// The biology toolkit's 3D view (spec §3.4, §3.5, §9): a SceneKit view with the molecule builder's lights, a clear
// ground, and an orbit camera (drag to turn, scroll or pinch to zoom; (1.3.79) the model framed with a 40° field of view
// to fill 84% of its room — the height, or the width beside the label columns — on its first frame, on Reset view and
// on a new size; double-click empty space to reset, an object to zoom to it); a scene made off the main
// thread from values (BioSceneContent) and batched one geometry per material; a click picking an object by its id, a
// hover outlining it (the pointer a hand); the selected object glowing and the rest dimmed to 35%; labels with leader
// lines in 2D over it; arrow keys turning it 15° a press, + and − zooming, Escape letting go. Auto-turn is off unless
// asked (the "Turn" button), never under Reduce Motion, and waits three seconds after a touch; camera moves are 500 ms,
// or a jump under Reduce Motion.

/// How a tool hands its scene to the view: a closure that makes it, run off the main thread. Capture values (copy
/// what you need from your model first), never an ObservableObject.
struct BioSceneBuild: @unchecked Sendable {
    let make: () -> BioSceneContent
    init(_ make: @escaping () -> BioSceneContent) { self.make = make }
}

/// The scenes being made, by key. A view made again while its scene is on its way — SwiftUI re-creating a tool's page
/// as the window settles at its size, which the screenshot suite's pictures showed for every page — waits for that
/// scene rather than making it a second time beside it: a large cell is many seconds of work in a debug build, and two
/// at once took twice as long (the plant cell 39 s, pictured before it came).
@MainActor
enum BioSceneMaking {
    private static var making: [String: (serial: Int, task: Task<BioSceneContent, Never>)] = [:]
    private static var serial = 0

    /// The scene for `key`: the one already being made, or one made now off the main thread — its ball and outline
    /// worked out there too (`measured()`).
    static func content(_ key: String, _ build: BioSceneBuild) async -> BioSceneContent {
        if let on = making[key] { return await on.task.value }
        serial += 1
        let mine = serial
        let task = Task.detached(priority: .userInitiated) { build.make().measured() }
        making[key] = (mine, task)
        let made = await task.value
        if making[key]?.serial == mine { making[key] = nil }
        return made
    }
}

/// A request to frame an object (its id, as picking names it); a new `serial` asks again.
struct BioZoomRequest: Equatable {
    let id: String
    let serial: Int
}

/// What the view shows over the scene and tells its tool.
@MainActor
final class BioSceneLive: ObservableObject {
    @Published var labels: [BioLabelPlacement] = []
    /// Labels left out (more than the columns hold): "+N more".
    @Published var more = 0
    /// The object under the pointer (its id), if any.
    @Published var hovered: String?
    /// (1.3.79) The hover is a label's (the pointer over its capsule), not an object's in the scene: let go when that
    /// label moves or goes — a label laid out under a resting pointer, then moved away from it, kept its hover (and its
    /// part's tooltip) until the pointer moved.
    var hoverFromLabel = false
    /// A scene is being made (shown after a moment).
    @Published var building = false
}

/// A biology 3D scene.
struct BioSceneView: View {
    /// The scene is made again (off the main thread) when this changes: put in it everything the scene depends on —
    /// the page, the options, light or dark.
    let key: String
    let build: BioSceneBuild
    /// SceneKit nodes to add as they are (text, billboards), made on the main thread with each new scene. A node named
    /// "pick:<id>" is picked as that id.
    var extras: (() -> [SCNNode])? = nil
    /// The selected object (an id, or the start of ids: "organelle:mito" selects every "organelle:mito:…").
    @Binding var selection: String?
    var showLabels = true
    /// Auto-turn (the "Turn" button); never under Reduce Motion.
    var turning = false
    /// The camera frames the content afresh when this changes (nil: only the first scene) — a new cell, not a new option.
    var frameKey: String? = nil
    /// Raise to put the camera back ("Reset view").
    var reset = 0
    var zoom: BioZoomRequest? = nil
    /// Dim everything but the selection to 35%.
    var dimOthers = true
    /// What the view shows, for VoiceOver ("3D model of an animal cell, cut open; 16 kinds of parts; selected: …").
    var accessibility = "3D model"
    /// A click on an object or its label (nil: on nothing); by default it sets the selection.
    var onPick: ((String?) -> Void)? = nil
    var onDoubleClick: ((String?) -> Void)? = nil
    /// The object under the pointer (or its label) changed: its id, or nil — for a tool's tooltip.
    var onHover: ((String?) -> Void)? = nil
    /// Points on the screen per scene unit at the framed ball's centre, told when the camera's zoom changes it — for a
    /// scale bar.
    var onScale: ((Double) -> Void)? = nil
    /// Room kept clear of labels along the bottom (a scale bar over the scene's corner): the label columns end above it.
    var labelBottom: CGFloat = 0

    @StateObject private var live = BioSceneLive()
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ZStack(alignment: .topLeading) {
            BioSceneRepresentable(key: key, build: build, extras: extras, selection: selection, showLabels: showLabels,
                                  turning: turning && !reduceMotion, frameKey: frameKey, reset: reset, zoom: zoom, dimOthers: dimOthers,
                                  reduceMotion: reduceMotion, live: live, pick: pick, doubleClick: doubleClick, hover: onHover, scale: onScale,
                                  labelBottom: labelBottom)
            if showLabels {
                BioLabelsOverlay(live: live, reduceMotion: reduceMotion, pick: pick)
            }
            if live.building {
                ProgressView().controlSize(.small).padding(14).allowsHitTesting(false)
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(accessibility)
    }

    private func pick(_ id: String?) {
        if let onPick { onPick(id) } else { selection = id }
    }

    private func doubleClick(_ id: String?) { onDoubleClick?(id) }
}

/// The 3D view's card (spec §4.1 UI layout): a top bar (its title and badge on the left; Turn and Reset view on the
/// right, or what the tool puts there), the scene, and the hint under it.
struct BioSceneCard<Bar: View, Stage: View>: View {
    var hint = "Drag to turn · scroll to zoom · click a part to learn about it"
    @ViewBuilder var bar: Bar
    @ViewBuilder var scene: Stage

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 10) { bar }
                .padding(16)
            scene
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            Text(hint)
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity)
                .padding(12)
        }
        .card(radius: 20)
        // (1.3.79) laid out narrow, the card can be as short as 300 points: its model is framed to the room it has
        .preference(key: PTDrawingMinKey.self, value: 300)
    }
}

/// The scene's "Turn" button (off by default; unavailable under Reduce Motion) and "Reset view".
struct BioSceneButtons: View {
    @Binding var turning: Bool
    let reset: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        HStack(spacing: 8) {
            if !reduceMotion {
                Button {
                    withAnimation(Motion.snappy) { turning.toggle() }
                } label: {
                    Label(turning ? "Turning" : "Turn", systemImage: "rotate.3d")
                        .font(.sCallout.weight(.medium))
                }
                .glassButton(prominent: turning)
                .help(turning ? "Stop the model turning by itself" : "Let the model turn by itself")
            }
            Button(action: reset) {
                Label("Reset view", systemImage: "arrow.counterclockwise").font(.sCallout.weight(.medium))
            }
            .glassButton()
            .help("Put the camera back (or double-click empty space)")
        }
    }
}

// MARK: - The labels over the scene (§3.5)

/// A label's capsule width as it is drawn: its words measured at 12 pt semibold, its 10 pt padding a side, and a point
/// to spare each side — so no label is cut short ("Plasma membr…"), as the model's estimate by letter count let wide
/// words be. Measured once for each text.
enum BioLabelWidth {
    private static let cache = NSCache<NSString, NSNumber>()
    private static let font = NSFont.systemFont(ofSize: 12, weight: .semibold)

    static func of(_ text: String) -> Double {
        let key = text as NSString
        if let w = cache.object(forKey: key) { return w.doubleValue }
        let w = Double(ceil(key.size(withAttributes: [.font: font]).width)) + 22
        cache.setObject(NSNumber(value: w), forKey: key)
        return w
    }
}

struct BioLabelsOverlay: View {
    @ObservedObject var live: BioSceneLive
    let reduceMotion: Bool
    let pick: (String?) -> Void

    var body: some View {
        ZStack(alignment: .topLeading) {
            Canvas { ctx, _ in
                for l in live.labels {
                    var path = Path()
                    path.move(to: CGPoint(x: l.anchorX, y: l.anchorY))
                    path.addLine(to: CGPoint(x: l.elbowX, y: l.y))
                    path.addLine(to: CGPoint(x: l.edgeX, y: l.y))
                    ctx.stroke(path, with: .color(.primary.opacity(0.55)), lineWidth: 1)
                    ctx.fill(Path(ellipseIn: CGRect(x: l.anchorX - 1.5, y: l.anchorY - 1.5, width: 3, height: 3)), with: .color(.primary.opacity(0.8)))
                }
            }
            .allowsHitTesting(false)
            // (by id and words: two labels can name one object — a β-strand and the C-terminus in it — and a ForEach
            // given the same id twice drew one capsule, leaving the other's leader line pointing at nothing)
            ForEach(live.labels, id: \.key) { l in
                let f = l.frame
                Button { pick(l.id) } label: {
                    Text(l.text)
                        .font(.system(size: 12, weight: .semibold))
                        .lineLimit(1)
                        .padding(.horizontal, 10)
                        .frame(width: f.width, height: f.height)
                        .background(.regularMaterial, in: Capsule())
                        // (1.3.79) a hovered label's edge a little stronger, in its own grey: in the accent, a label
                        // under a resting pointer read as a part selected
                        .overlay(Capsule().strokeBorder(live.hovered.map { BioSceneContent.matches($0, l.id) || BioSceneContent.matches(l.id, $0) } == true
                                                        ? Color.primary.opacity(0.45) : Color.primary.opacity(0.12), lineWidth: 1))
                }
                .buttonStyle(.plain)
                .onHover { inside in
                    if inside {
                        live.hoverFromLabel = true
                        live.hovered = l.id
                    } else if live.hovered == l.id {
                        live.hoverFromLabel = false
                        live.hovered = nil
                    }
                }
                .position(x: f.x + f.width / 2, y: l.y)
                .transition(.opacity)
                .help(l.text)
            }
            if live.more > 0, let last = live.labels.max(by: { $0.y < $1.y }) {
                Text("+\(live.more) more")
                    .font(.sCaption.weight(.medium))
                    .foregroundStyle(.secondary)
                    .position(x: last.frame.x + last.frame.width / 2, y: last.y + 22)
                    .allowsHitTesting(false)
            }
        }
        .animation(reduceMotion ? nil : .easeInOut(duration: 0.12), value: live.labels.map(\.key))
    }
}

extension BioLabelPlacement {
    /// What tells one placed label from another: its object and its words.
    var key: String { id + "\u{1F}" + text }
}

// MARK: - The SceneKit view

/// The scene's NSView: telling its coordinator when it is handled (so auto-turn waits), its keys, and the pointer.
final class BioSCNView: SCNView {
    weak var coordinator: BioSceneCoordinator?
    private var tracking: NSTrackingArea?

    override var acceptsFirstResponder: Bool { true }

    /// (1.3.79) A new size: the camera framed for it, unless the model has been turned or zoomed since it was framed.
    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        coordinator?.resized()
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        coordinator?.handled()
        super.mouseDown(with: event)
    }
    override func mouseDragged(with event: NSEvent) { coordinator?.moved(); super.mouseDragged(with: event) }
    override func rightMouseDragged(with event: NSEvent) { coordinator?.moved(); super.rightMouseDragged(with: event) }
    override func scrollWheel(with event: NSEvent) { coordinator?.moved(); super.scrollWheel(with: event) }
    override func magnify(with event: NSEvent) { coordinator?.moved(); super.magnify(with: event) }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let t = NSTrackingArea(rect: .zero, options: [.mouseMoved, .mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self, userInfo: nil)
        addTrackingArea(t)
        tracking = t
    }

    override func mouseMoved(with event: NSEvent) {
        super.mouseMoved(with: event)
        coordinator?.hover(at: convert(event.locationInWindow, from: nil))
    }

    override func mouseExited(with event: NSEvent) {
        super.mouseExited(with: event)
        coordinator?.hover(at: nil)
    }

    override func keyDown(with event: NSEvent) {
        guard let c = coordinator else { return super.keyDown(with: event) }
        switch event.keyCode {
        case 123: c.turn(by: -15, vertical: false)
        case 124: c.turn(by: 15, vertical: false)
        case 126: c.turn(by: -15, vertical: true)
        case 125: c.turn(by: 15, vertical: true)
        case 53: c.pick(nil)
        default:
            switch event.charactersIgnoringModifiers ?? "" {
            case "+", "=": c.zoom(by: 0.85)
            case "-", "_": c.zoom(by: 1 / 0.85)
            default: super.keyDown(with: event)
            }
        }
    }
}

struct BioSceneRepresentable: NSViewRepresentable {
    let key: String
    let build: BioSceneBuild
    let extras: (() -> [SCNNode])?
    let selection: String?
    let showLabels: Bool
    let turning: Bool
    let frameKey: String?
    let reset: Int
    let zoom: BioZoomRequest?
    let dimOthers: Bool
    let reduceMotion: Bool
    let live: BioSceneLive
    let pick: (String?) -> Void
    let doubleClick: (String?) -> Void
    var hover: ((String?) -> Void)? = nil
    var scale: ((Double) -> Void)? = nil
    var labelBottom: CGFloat = 0

    func makeCoordinator() -> BioSceneCoordinator { BioSceneCoordinator(live: live) }

    func makeNSView(context: Context) -> BioSCNView {
        let v = BioSCNView(frame: .zero)
        v.coordinator = context.coordinator
        v.allowsCameraControl = true
        v.autoenablesDefaultLighting = false
        v.antialiasingMode = .multisampling4X
        v.backgroundColor = .clear
        v.isPlaying = true
        v.loops = true
        v.defaultCameraController.interactionMode = .orbitTurntable
        let scene = SCNScene()
        scene.background.contents = NSColor.clear
        let cam = SCNNode()
        cam.name = "camera"
        cam.camera = SCNCamera()
        cam.camera?.fieldOfView = 40
        scene.rootNode.addChildNode(cam)
        // (the molecule builder's lights: ambient 380, a key at 1000, a fill at 350)
        let ambient = SCNNode()
        ambient.light = SCNLight()
        ambient.light?.type = .ambient
        ambient.light?.intensity = 380
        scene.rootNode.addChildNode(ambient)
        let keyLight = SCNNode()
        keyLight.light = SCNLight()
        keyLight.light?.type = .directional
        keyLight.light?.intensity = 1000
        keyLight.eulerAngles = SCNVector3(-0.6, 0.5, 0)
        scene.rootNode.addChildNode(keyLight)
        let fill = SCNNode()
        fill.light = SCNLight()
        fill.light?.type = .directional
        fill.light?.intensity = 350
        fill.eulerAngles = SCNVector3(0.5, -2.4, 0)
        scene.rootNode.addChildNode(fill)
        let turn = SCNNode()
        turn.name = "turn"
        scene.rootNode.addChildNode(turn)
        for name in ["content", "selected", "extras", "hover"] {
            let n = SCNNode()
            n.name = name
            turn.addChildNode(n)
        }
        v.scene = scene
        v.pointOfView = cam
        let click = NSClickGestureRecognizer(target: context.coordinator, action: #selector(BioSceneCoordinator.click(_:)))
        v.addGestureRecognizer(click)
        let twice = NSClickGestureRecognizer(target: context.coordinator, action: #selector(BioSceneCoordinator.doubleClick(_:)))
        twice.numberOfClicksRequired = 2
        v.addGestureRecognizer(twice)
        context.coordinator.view = v
        context.coordinator.update(self)
        return v
    }

    func updateNSView(_ v: BioSCNView, context: Context) {
        context.coordinator.update(self)
    }

    static func dismantleNSView(_ v: BioSCNView, coordinator: BioSceneCoordinator) {
        coordinator.stop()
    }
}

/// The scene view's state: what is built, batched, selected, hovered and labelled, and the camera.
@MainActor
final class BioSceneCoordinator: NSObject {
    weak var view: BioSCNView?
    private let live: BioSceneLive
    private var config: BioSceneRepresentable?
    private var builtKey: String?
    private var buildingKey: String?
    private var content: BioSceneContent?
    private var batches: [(node: SCNNode, batch: BioMeshBatch)] = []
    private var appliedSelection: String?
    private var appliedDim = true
    private var hovered: String?
    private var framedKey: String??
    private var framing = BioSphere(centre: BioVec(0, 0, 0), radius: 1)
    /// (1.3.79) The scene's outline (BioSceneContent.outline()): what the camera frames and the labels sit beside.
    private var outlinePoints: [BioVec] = []
    /// The camera has been turned, zoomed or moved since it was last framed: a new size then leaves it be.
    private var touched = false
    /// The view's size when the camera was last framed.
    private var framedSize = CGSize.zero
    /// The label hovered and where it was when its hover began (a label that moves from under the pointer lets go).
    private var labelHoverAt: (id: String, x: Double, y: Double)?
    private var resetSeen = 0
    private var zoomSeen: BioZoomRequest?
    private var timer: Timer?
    private var lastSignature: [Float] = []
    private var stillSince = Date()
    private var rayVisible: [String: Bool] = [:]
    private var raysDone = false
    private var resume: Task<Void, Never>?
    private var lastHover = Date.distantPast
    private var hoverSink: AnyCancellable?
    private var lastScale = 0.0

    init(live: BioSceneLive) {
        self.live = live
        super.init()
        // (whatever is hovered — an object in the scene, or its label over it — outlined)
        hoverSink = live.$hovered.removeDuplicates().sink { [weak self] id in
            MainActor.assumeIsolated {
                self?.outline(id)
                self?.config?.hover?(id)
            }
        }
    }

    private var root: SCNNode? { view?.scene?.rootNode }
    private func node(_ name: String) -> SCNNode? { root?.childNode(withName: name, recursively: true) }

    // MARK: Updates

    func update(_ c: BioSceneRepresentable) {
        config = c
        if builtKey != c.key && buildingKey != c.key { startBuild(c) }
        if content != nil {
            if appliedSelection != c.selection || appliedDim != c.dimOthers { applySelection() }
            if c.reset != resetSeen {
                resetSeen = c.reset
                frameCamera(framing, animated: true)
            }
            if let z = c.zoom, z != zoomSeen {
                zoomSeen = z
                zoomTo(z.id)
            }
        }
        applyTurning(c.turning)
        // (the labels' timer also measures the scale, for a tool that asks)
        if c.showLabels || c.scale != nil { startLabels() }
        if !c.showLabels && (!live.labels.isEmpty || live.more != 0) {
            // (not while SwiftUI is updating the view: after it)
            Task { [weak self] in
                self?.live.labels = []
                self?.live.more = 0
                self?.letGoOfMovedLabel([])
            }
        }
    }

    private func startBuild(_ c: BioSceneRepresentable) {
        let key = c.key
        buildingKey = key
        let build = c.build
        // (a moment's grace before the spinner shows: most scenes are quicker)
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 150_000_000)
            guard let self, self.buildingKey == key else { return }
            self.live.building = true
        }
        // (the screenshot suite's picture waits for the scene, as it waits for the page's answers: a large cell takes
        // seconds in a debug build, and a picture taken sooner showed an empty card with its spinner — Support/Shot.swift)
        Shot.began()
        let started = Date()
        Task { [weak self] in
            let made = await BioSceneMaking.content(key, build)
            if OpenClock.verbose {
                NSLog("[Simpl] bio scene: %@ made in %d ms, %d objects", String(key.prefix(90)),
                      Int(Date().timeIntervalSince(started) * 1000), made.items.count)
            }
            Shot.ended()
            guard let self, self.buildingKey == key else { return }
            self.apply(made, key: key)
        }
    }

    private func apply(_ made: BioSceneContent, key: String) {
        buildingKey = nil
        builtKey = key
        live.building = false
        content = made
        framing = made.shape?.framing ?? made.framing
        outlinePoints = made.shape?.outline ?? []
        rayVisible = [:]
        raysDone = false
        lastSignature = []
        applySelection(force: true)
        if let extras = node("extras") {
            extras.childNodes.forEach { $0.removeFromParentNode() }
            for n in config?.extras?() ?? [] { extras.addChildNode(n) }
            // (1.3.79) the texts beside the model (a helix's measurements) in its outline too: framed with it, and the
            // labels' columns clear of them
            let corners = extrasCorners(extras)
            if !corners.isEmpty { outlinePoints = BioSceneContent.extremes(of: outlinePoints + corners) }
        }
        // (the camera frames the first scene, and a scene whose frame key is new)
        let fk = config?.frameKey
        if framedKey == nil || (fk != nil && framedKey != .some(fk)) {
            if framedKey == nil {
                // (1.3.79) a Reset view or Zoom to asked for before this view was made is not this view's to play: the
                // Biomolecule Builder's Reset is shared by its pages, and a page opened after one was pressed framed its
                // molecule, then slid the camera to frame it again as the page came
                resetSeen = config?.reset ?? 0
                zoomSeen = config?.zoom
            }
            framedKey = .some(fk)
            frameCamera(framing, animated: false)
        }
        // (a new scene starts with nothing hovered: what the pointer was over may no longer be there)
        live.hoverFromLabel = false
        labelHoverAt = nil
        hover(id: nil)
    }

    /// The corners of the extras' drawn parts (their texts' and meshes' boxes) in the scene's own space — as they stand
    /// before the camera has turned their billboards, facing it as it frames them.
    private func extrasCorners(_ holder: SCNNode) -> [BioVec] {
        var out: [BioVec] = []
        holder.enumerateChildNodes { n, _ in
            guard n.geometry != nil else { return }
            let (lo, hi) = n.boundingBox
            for x in [lo.x, hi.x] {
                for y in [lo.y, hi.y] {
                    let p = n.convertPosition(SCNVector3(x, y, (lo.z + hi.z) / 2), to: holder)
                    out.append(BioVec(Double(p.x), Double(p.y), Double(p.z)))
                }
            }
        }
        return out
    }

    /// The batches made again without the selected objects, which are drawn on their own, glowing; the rest dimmed.
    private func applySelection(force: Bool = false) {
        guard let content, let c = config, let holder = node("content"), let selectedHolder = node("selected") else { return }
        if !force && appliedSelection == c.selection && appliedDim == c.dimOthers { return }
        appliedSelection = c.selection
        appliedDim = c.dimOthers
        holder.childNodes.forEach { $0.removeFromParentNode() }
        selectedHolder.childNodes.forEach { $0.removeFromParentNode() }
        let sel = c.selection
        let isSelected: (String?) -> Bool = { id in sel.map { BioSceneContent.matches(id, $0) } ?? false }
        batches = BioSceneKit.batches(content.items, skip: isSelected)
        let dim = sel != nil && c.dimOthers && !content.items(matching: sel ?? "").isEmpty
        for b in batches {
            b.node.opacity = dim ? 0.35 : 1
            holder.addChildNode(b.node)
        }
        if let sel {
            for it in content.items(matching: sel) {
                var m = it.material
                m.emission = max(m.emission, 0.35)
                let n = BioSceneKit.node(it.mesh, m, order: it.order)
                n.name = "pick:\(it.id ?? sel)"
                selectedHolder.addChildNode(n)
            }
        }
        lastSignature = []
    }

    // MARK: Camera

    /// The camera framing the model: 40° field of view (up and down), looking along −z at the ball's centre, as far
    /// away as `frameDistance` says.
    private func frameCamera(_ s: BioSphere, animated: Bool) {
        guard let v = view, let cam = v.pointOfView else { return }
        let d = frameDistance(s)
        touched = false
        framedSize = v.bounds.size
        let c = SIMD3<Float>(Float(s.centre.x), Float(s.centre.y), Float(s.centre.z))
        cam.camera?.zNear = max(0.001, d * 0.01)
        cam.camera?.zFar = d * 20
        let move = animated && !(config?.reduceMotion ?? false)
        SCNTransaction.begin()
        SCNTransaction.animationDuration = move ? 0.5 : 0
        SCNTransaction.animationTimingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        cam.simdPosition = c + SIMD3<Float>(0, 0, Float(d))
        cam.simdOrientation = simd_quatf(angle: 0, axis: SIMD3<Float>(0, 1, 0))
        if let turn = node("turn"), !(config?.turning ?? false) { turn.simdOrientation = simd_quatf(angle: 0, axis: SIMD3<Float>(0, 1, 0)) }
        SCNTransaction.commit()
        v.defaultCameraController.target = SCNVector3(CGFloat(s.centre.x), CGFloat(s.centre.y), CGFloat(s.centre.z))
        lastSignature = []
    }

    /// The tangent of the half-angles the model may fill, across and up and down: 84% of the view's height, and 84% of
    /// the width left beside the label columns (each the widest label and 40 points; the model keeps 55% of the width
    /// at least) — `labels` false: of the whole width.
    private func fillLimits(labels: Bool) -> (across: Double, up: Double) {
        let tanV = tan(20 * Double.pi / 180), fill = 0.84
        let size = view?.bounds.size ?? .zero
        let w = Double(size.width), h = Double(size.height)
        guard w > 1, h > 1 else { return (fill * tanV, fill * tanV) }
        var room = w
        if labels, config?.showLabels == true, let texts = content?.labels.map(\.text), !texts.isEmpty {
            let widest = texts.map { BioLabelWidth.of($0) }.max() ?? 0
            room = max(w - 2 * (widest + 40), w * 0.55)
        }
        return (fill * tanV * room / h, fill * tanV)
    }

    /// (1.3.79) How far from the ball's centre the camera stands for the model to fill its room (`fillLimits`): worked
    /// out from the model's outline, each point at its own depth, and — where the tool asked for a ball of its own (a
    /// cell, as far apart as it is exploded) — no nearer than that ball needs; the ball alone while the model turns by
    /// itself (its outline turns with it). Before, the ball was framed to 85% of the height whatever the width: a narrow
    /// card cut a wide model short at its sides, and a helix, framed by the ball round its length, stood thin in the
    /// middle of a wide card.
    private func frameDistance(_ s: BioSphere) -> Double {
        let limit = fillLimits(labels: true)
        let r = max(s.radius, 1e-3)
        let ball = r / sin(atan(min(limit.across, limit.up)))
        guard !(config?.turning ?? false), !outlinePoints.isEmpty else { return ball }
        var d = 0.0
        for p in outlinePoints {
            let q = p - s.centre
            d = max(d, q.z + abs(q.x) / limit.across, q.z + abs(q.y) / limit.up, q.z + r * 0.05)
        }
        return content?.frame != nil ? max(d, ball) : d
    }

    /// (1.3.79) The view's new size: the camera framed for it at once, if nothing has moved it since it was last framed
    /// — a first show's scene can come before its view has its size, and a window can be made wider or narrower.
    func resized() {
        guard content != nil, !touched, let v = view else { return }
        let s = v.bounds.size
        guard abs(s.width - framedSize.width) > 1 || abs(s.height - framedSize.height) > 1 else { return }
        frameCamera(framing, animated: false)
    }

    /// The camera moved by hand (dragged, scrolled, pinched, a key): a new size no longer frames it afresh.
    func moved() {
        touched = true
        handled()
    }

    /// The camera brought to an object (its items' ball, a little room round it).
    private func zoomTo(_ id: String) {
        guard let content, let turn = node("turn") else { return }
        let items = content.items(matching: id)
        guard !items.isEmpty else { return }
        var pts: [BioVec] = []
        for it in items {
            let b = it.mesh.bounds
            pts += [b.centre + BioVec(b.radius, 0, 0), b.centre - BioVec(b.radius, 0, 0), b.centre + BioVec(0, b.radius, 0), b.centre - BioVec(0, b.radius, 0)]
        }
        let s = BioV.boundingSphere(pts)
        // (the object's centre where the turn has taken it)
        let w = turn.simdConvertPosition(SIMD3<Float>(Float(s.centre.x), Float(s.centre.y), Float(s.centre.z)), to: nil)
        guard let v = view, let cam = v.pointOfView else { return }
        // (its ball and a little room filling the view's height or width, whichever is less)
        let limit = fillLimits(labels: false)
        let d = Float(max(s.radius * 1.3, 1e-3) / sin(atan(min(limit.across, limit.up))))
        touched = true
        SCNTransaction.begin()
        SCNTransaction.animationDuration = (config?.reduceMotion ?? false) ? 0 : 0.5
        SCNTransaction.animationTimingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        cam.simdPosition = w - cam.simdWorldFront * d
        SCNTransaction.commit()
        v.defaultCameraController.target = SCNVector3(CGFloat(w.x), CGFloat(w.y), CGFloat(w.z))
        lastSignature = []
    }

    /// The model turned by the arrow keys: 15° a press about the camera's up (← →) or right (↑ ↓) axis.
    func turn(by degrees: Float, vertical: Bool) {
        guard let turn = node("turn"), let cam = view?.pointOfView else { return }
        moved()
        let axis = vertical ? cam.simdWorldRight : cam.simdWorldUp
        SCNTransaction.begin()
        SCNTransaction.animationDuration = (config?.reduceMotion ?? false) ? 0 : 0.25
        turn.simdOrientation = simd_quatf(angle: degrees * .pi / 180, axis: simd_normalize(axis)) * turn.simdOrientation
        SCNTransaction.commit()
    }

    /// + and −: the camera closer or further by a step.
    func zoom(by factor: Float) {
        guard let v = view, let cam = v.pointOfView else { return }
        moved()
        let t = v.defaultCameraController.target
        let target = SIMD3<Float>(Float(t.x), Float(t.y), Float(t.z))
        let offset = cam.simdPosition - target
        SCNTransaction.begin()
        SCNTransaction.animationDuration = (config?.reduceMotion ?? false) ? 0 : 0.2
        cam.simdPosition = target + offset * factor
        SCNTransaction.commit()
    }

    private func applyTurning(_ on: Bool) {
        guard let turn = node("turn") else { return }
        if on {
            if turn.action(forKey: "turn") == nil { turn.runAction(.repeatForever(.rotateBy(x: 0, y: .pi * 2, z: 0, duration: 30)), forKey: "turn") }
        } else {
            turn.removeAction(forKey: "turn")
        }
    }

    /// Handled (dragged, scrolled, a key): auto-turn waits three seconds after the last touch; labels wait for it to
    /// be still before testing what hides them.
    func handled() {
        stillSince = Date()
        raysDone = false
        guard let turn = node("turn"), turn.action(forKey: "turn") != nil else { return }
        turn.isPaused = true
        resume?.cancel()
        resume = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 3_000_000_000)
            guard !Task.isCancelled else { return }
            self?.node("turn")?.isPaused = false
        }
    }

    // MARK: Picking

    /// The object at a point of the view (its id), the nearest opaque one first (a translucent membrane does not hide
    /// what is inside it).
    private func object(at p: CGPoint) -> (id: String, distance: Float)? {
        guard let v = view else { return nil }
        let hits = v.hitTest(p, options: [SCNHitTestOption.searchMode: SCNHitTestSearchMode.all.rawValue, SCNHitTestOption.ignoreHiddenNodes: true])
        var translucent: (String, Float)?
        let eye = v.pointOfView?.simdWorldPosition ?? .zero
        for h in hits {
            guard let id = objectID(of: h) else { continue }
            let w = h.worldCoordinates
            let dist = simd_distance(eye, SIMD3<Float>(Float(w.x), Float(w.y), Float(w.z)))
            // (a translucent material, or a faded node — but not a batch dimmed round the selection)
            let clear = (h.node.geometry?.firstMaterial?.transparency ?? 1) < 0.999 || (h.node.opacity < 0.999 && h.node.name != "bio-batch")
            if clear { if translucent == nil { translucent = (id, dist) }; continue }
            return (id, dist)
        }
        return translucent
    }

    private func objectID(of h: SCNHitTestResult) -> String? {
        var n: SCNNode? = h.node
        while let x = n {
            if let name = x.name {
                if name.hasPrefix("pick:") { return String(name.dropFirst(5)) }
                if name == "bio-batch", let b = batches.first(where: { $0.node === x }) {
                    let found = b.batch.object(atTriangle: h.faceIndex)
                    return found?.isEmpty == false ? found : nil
                }
            }
            n = x.parent
        }
        return nil
    }

    @objc func click(_ g: NSClickGestureRecognizer) {
        guard let v = view else { return }
        pick(object(at: g.location(in: v))?.id)
    }

    @objc func doubleClick(_ g: NSClickGestureRecognizer) {
        guard let v = view else { return }
        if let id = object(at: g.location(in: v))?.id {
            config?.doubleClick(id)
        } else {
            frameCamera(framing, animated: true)
        }
    }

    func pick(_ id: String?) { config?.pick(id) }

    // MARK: Hover

    func hover(at p: CGPoint?) {
        guard let p else { hover(id: nil); return }
        // (a hit test at most every 50 ms)
        let now = Date()
        guard now.timeIntervalSince(lastHover) > 0.05 else { return }
        lastHover = now
        hover(id: object(at: p)?.id)
    }

    /// The object under the pointer in the scene: told to the view (which outlines it, as it does an object whose
    /// label is hovered) — not while the pointer is on a label, whose hover is its own.
    private func hover(id: String?) {
        guard id != live.hovered, !live.hoverFromLabel else { return }
        live.hovered = id
    }

    /// The hovered object outlined (an inverted hull in the accent colour) and the pointer a hand over it. (1.3.79) Only
    /// its opaque pieces: a hull shows as an outline only round something solid in front of it, and round a membrane or
    /// a cut face — translucent, drawn without depth — it was a solid shell of the accent colour behind everything
    /// inside the cell.
    private func outline(_ id: String?) {
        guard id != hovered else { return }
        hovered = id
        (id == nil ? NSCursor.arrow : NSCursor.pointingHand).set()
        guard let holder = node("hover") else { return }
        holder.childNodes.forEach { $0.removeFromParentNode() }
        guard let id, let content else { return }
        let items = content.items(matching: id).filter { !$0.material.isTranslucent }
        guard !items.isEmpty, items.reduce(0, { $0 + $1.mesh.triangleCount }) < 200_000 else { return }
        let push = Float(framing.radius * 0.006)
        let m = SCNMaterial()
        m.lightingModel = .constant
        m.diffuse.contents = NSColor.controlAccentColor
        m.cullMode = .front
        for it in items {
            var hull = it.mesh
            for i in 0..<hull.positions.count { hull.positions[i] += hull.normals[i] * push }
            let n = SCNNode(geometry: BioSceneKit.geometry(hull, m))
            n.renderingOrder = 50
            holder.addChildNode(n)
        }
    }

    // MARK: Labels (§3.5)

    private func startLabels() {
        guard timer == nil else { return }
        let t = Timer(timeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.tickScale()
                self?.tickLabels()
            }
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    /// For a tool's scale bar: how many points one scene unit spans at the framed ball's centre (a ruler one unit long
    /// along the camera's right, projected), told when it changes by more than half a percent.
    private func tickScale() {
        guard let report = config?.scale, content != nil, let v = view, let cam = v.pointOfView, let turn = node("turn"),
              let project = BioProjector(view: v, camera: cam) else { return }
        let c3 = BioDrawn(turn.presentation.simdWorldTransform).point(framing.centre)
        let e3 = c3 + BioDrawn(cam.presentation.simdWorldTransform).right
        let pc = project.point(c3), pe = project.point(e3)
        let s = hypot(pe.x - pc.x, pe.y - pc.y)
        guard s.isFinite, s > 0, abs(s - lastScale) > lastScale * 0.005 else { return }
        lastScale = s
        report(s)
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        resume?.cancel()
    }

    /// Each tick: while the camera, the model or the view's size is changing — and (1.3.79) for a second after, so a
    /// layout made as SceneKit settles (a camera's move, a first frame, a new size) is never the last word — the anchors
    /// projected and the labels laid out; once it has been still for 150 ms, what hides each anchor is tested with a
    /// ray. The signature is of the camera as drawn (its presentation): a framing's half-second move changed only the
    /// camera's target at its start, and its labels stayed where the move began, their leaders ending in empty space.
    private func tickLabels() {
        guard let v = view, let cam = v.pointOfView, let turn = node("turn"), let content, config?.showLabels == true else { return }
        guard !content.labels.isEmpty else {
            if !live.labels.isEmpty { live.labels = []; live.more = 0 }
            letGoOfMovedLabel([])
            return
        }
        guard let project = BioProjector(view: v, camera: cam) else { return }
        // (1.3.80) the camera and the model as drawn read once a tick, and every point placed by them here (a
        // presentation node read for each anchor and outline point waited on SceneKit's drawing each time)
        let pov = cam.presentation.simdWorldTransform, tt = turn.presentation.simdWorldTransform
        let model = BioDrawn(tt), camera = BioDrawn(pov)
        let columns: [SIMD4<Float>] = [pov.columns.0, pov.columns.1, pov.columns.2, pov.columns.3,
                                       tt.columns.0, tt.columns.1, tt.columns.2, tt.columns.3]
        var sig: [Float] = columns.flatMap { [$0.x, $0.y, $0.z, $0.w] }
        sig.append(Float(v.bounds.width))
        sig.append(Float(v.bounds.height))
        let now = Date()
        if sig != lastSignature {
            lastSignature = sig
            stillSince = now
            raysDone = false
        }
        let quiet = now.timeIntervalSince(stillSince)
        let still = quiet > 0.15
        if raysDone && quiet > 1.0 { return }
        let h = project.height, w = project.width
        let eye = camera.origin
        let testRays = still && !raysDone
        var inputs: [BioLabelInput] = []
        for l in content.labels {
            let a = model.point(l.anchor)
            let p = project.point(a)
            var visible = p.inFront
            if visible, let nrm = l.normal {
                let wn = model.vector(nrm)
                visible = simd_dot(wn, eye - a) > 0
            } else if visible {
                if testRays {
                    // (the hit test takes the view's own points: y up, unless the view is flipped)
                    let hit = object(at: CGPoint(x: p.x, y: v.isFlipped ? p.y : h - p.y))
                    let dist = simd_distance(eye, a)
                    let ok = hit.map { BioSceneContent.matches($0.id, l.id) || BioSceneContent.matches(l.id, $0.id) || $0.distance >= dist * 0.98 } ?? true
                    rayVisible[l.id] = ok
                }
                visible = rayVisible[l.id] ?? true
            }
            inputs.append(BioLabelInput(id: l.id, text: l.text, x: p.x, y: p.y, visible: visible, area: l.area, width: BioLabelWidth.of(l.text)))
        }
        if testRays { raysDone = true }
        // (the model's circle on the screen: its framing ball's centre and edge, projected; and its outline's leftmost
        // and rightmost points, the columns' edges beside them — inside the circle)
        let c3 = model.point(framing.centre)
        let e3 = c3 + camera.right * Float(framing.radius)
        let pc = project.point(c3), pe = project.point(e3)
        let r = hypot(pe.x - pc.x, pe.y - pc.y)
        var extent: (left: Double, right: Double)?
        if !outlinePoints.isEmpty {
            let xs = outlinePoints.map { project.point(model.point($0)) }
                .filter { $0.inFront }.map { $0.x }
            if let lo = xs.min(), let hi = xs.max() { extent = (left: max(lo, pc.x - r), right: min(hi, pc.x + r)) }
        }
        // (the columns end above any room the tool keeps clear at the bottom: its scale bar's)
        let out = BioLabelLayout.layout(inputs, cx: pc.x, cy: pc.y, r: min(r, w * 0.32), width: w,
                                        height: max(h - Double(config?.labelBottom ?? 0), h * 0.6), extent: extent)
        if out.placed != live.labels { live.labels = out.placed }
        if out.more != live.more { live.more = out.more }
        letGoOfMovedLabel(out.placed)
    }

    /// (1.3.79) A label's hover let go once that label has moved more than 6 points from where its hover began, or is
    /// gone: the pointer is no longer over it, though no pointer moved to say so.
    private func letGoOfMovedLabel(_ placed: [BioLabelPlacement]) {
        guard live.hoverFromLabel, let id = live.hovered else { labelHoverAt = nil; return }
        let release = {
            self.labelHoverAt = nil
            self.live.hoverFromLabel = false
            self.live.hovered = nil
        }
        guard let now = placed.first(where: { $0.id == id }) else { release(); return }
        if let at = labelHoverAt, at.id == id {
            if abs(now.edgeX - at.x) > 6 || abs(now.y - at.y) > 6 { release() }
        } else {
            labelHoverAt = (id: id, x: now.edgeX, y: now.y)
        }
    }
}

/// (1.3.80) A node as drawn (its presentation's world transform, read once): the scene's points and directions in its
/// space placed in the world's, its origin and its unit right — as SceneKit's simdConvertPosition, simdConvertVector,
/// simdWorldPosition and simdWorldRight would, without reading the presentation again for each.
struct BioDrawn {
    let transform: simd_float4x4

    init(_ transform: simd_float4x4) { self.transform = transform }

    func point(_ p: SIMD3<Float>) -> SIMD3<Float> {
        let w = transform * SIMD4<Float>(p, 1)
        return SIMD3<Float>(w.x, w.y, w.z)
    }

    func point(_ p: BioVec) -> SIMD3<Float> { point(SIMD3<Float>(Float(p.x), Float(p.y), Float(p.z))) }

    func vector(_ v: BioVec) -> SIMD3<Float> {
        let w = transform * SIMD4<Float>(Float(v.x), Float(v.y), Float(v.z), 0)
        return SIMD3<Float>(w.x, w.y, w.z)
    }

    var origin: SIMD3<Float> { SIMD3<Float>(transform.columns.3.x, transform.columns.3.y, transform.columns.3.z) }
    var right: SIMD3<Float> { simd_normalize(SIMD3<Float>(transform.columns.0.x, transform.columns.0.y, transform.columns.0.z)) }
}

/// (1.3.79) Where scene points fall on a SceneKit view, in the view's points — x right, y down, as the labels' overlay
/// draws — worked out from the camera's projection for the view's size in points and the camera as drawn. The labels
/// were placed by SceneKit's projectPoint, read as the view's points with y turned over by hand: on the owner's Mac
/// their leader lines ended in empty space and the labels sat in the card's top corners and at its edges, which the
/// CI's pictures (a 1× screen) never showed. Worked out here from the camera itself, the projection is in the very
/// points the overlay draws in, on any screen.
struct BioProjector {
    private let matrix: simd_float4x4
    let width: Double
    let height: Double

    @MainActor
    init?(view v: SCNView, camera cam: SCNNode) {
        guard let camera = cam.camera, v.bounds.width >= 1, v.bounds.height >= 1 else { return nil }
        let projection = simd_float4x4(camera.projectionTransform(withViewportSize: v.bounds.size))
        matrix = projection * cam.presentation.simdWorldTransform.inverse
        width = Double(v.bounds.width)
        height = Double(v.bounds.height)
    }

    /// A point in the scene's world on the view: x right and y down from the top left corner (points), and whether it
    /// is in front of the camera.
    func point(_ p: SIMD3<Float>) -> (x: Double, y: Double, inFront: Bool) {
        let c = matrix * SIMD4<Float>(p, 1)
        guard c.w > 1e-6 else { return (width / 2, height / 2, false) }
        return ((Double(c.x / c.w) + 1) / 2 * width, (1 - Double(c.y / c.w)) / 2 * height, true)
    }
}

import AppKit
import SceneKit
import SwiftUI

// (1.3.51) An element in 3D — on its page in the Periodic Table, and since 1.3.80 in Atoms & Orbitals, a tool of its own
// (Chemistry/AtomsOrbitals.swift): its atom (the nucleus's protons and neutrons, each shell a ring with its
// electrons going round) or the shapes of a subshell's orbitals, each with its electrons — turned and zoomed by dragging
// and scrolling (SceneKit's own camera control); (1.3.80) still, not going round, when the Mac is set to reduce motion.
// (1.3.76) Or its electron cloud: every electron as dots where it could be found, densest where it most likely is, and no
// paths (ElectronCloud.swift).

/// (1.3.76) The three views of the atom (1.3.80: Atoms & Orbitals').
enum PTModel: String { case atom, orbitals, cloud }

struct Atom3DView: NSViewRepresentable {
    let element: PTElement
    let model: PTModel
    /// The Orbitals view's subshell.
    let subshell: PTSubshell?
    /// The Cloud view's chosen subshell ("2p"); nil, the whole atom.
    let focus: String?
    /// The appearance: the cloud's colours are its own in light and in dark.
    let dark: Bool

    final class Coordinator { var key = "" }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> SCNView {
        let v = SCNView(frame: .zero)
        v.allowsCameraControl = true
        v.autoenablesDefaultLighting = true
        v.antialiasingMode = .multisampling4X
        v.backgroundColor = .clear
        v.isPlaying = true
        v.loops = true
        apply(v, context.coordinator)
        return v
    }

    func updateNSView(_ v: SCNView, context: Context) { apply(v, context.coordinator) }

    private func apply(_ v: SCNView, _ c: Coordinator) {
        // (1.3.76) the cloud made again for its chosen subshell and for light or dark; the atom and the orbitals only as
        // before, so a change of appearance leaves their camera where it was turned
        let key: String
        switch model {
        case .atom, .orbitals: key = "\(element.number)-\(model.rawValue)-\(subshell?.id ?? "")"
        case .cloud: key = "\(element.number)-cloud-\(focus ?? "*")-\(dark)"
        }
        guard key != c.key else { return }
        c.key = key
        let scene: SCNScene
        switch model {
        case .atom: scene = AtomScene.atom(element)
        case .orbitals: scene = AtomScene.orbitals(subshell ?? PTSubshell(n: 1, l: 0, count: 1))
        case .cloud: scene = AtomScene.cloud(element, focus: focus, dark: dark)
        }
        v.scene = scene
        v.pointOfView = scene.rootNode.childNode(withName: "camera", recursively: false)
    }
}

enum AtomScene {
    static let protonColor = NSColor(ptHex: "ff5a4f")
    static let neutronColor = NSColor(ptHex: "c7c7cc")
    static let electronColor = NSColor(ptHex: "64d2ff")

    static func material(_ color: NSColor, alpha: CGFloat = 1, glow: Bool = false) -> SCNMaterial {
        let m = SCNMaterial()
        m.lightingModel = .physicallyBased
        m.diffuse.contents = color
        m.metalness.contents = 0.05
        m.roughness.contents = 0.35
        if glow { m.emission.contents = color.withAlphaComponent(0.6) }
        m.transparency = alpha
        if alpha < 1 {
            m.blendMode = .alpha
            m.writesToDepthBuffer = false
            m.isDoubleSided = true
        }
        return m
    }

    private static func base(cameraAt distance: Float) -> SCNScene {
        let scene = SCNScene()
        scene.background.contents = NSColor.clear
        let cam = SCNNode()
        cam.name = "camera"
        cam.camera = SCNCamera()
        cam.camera?.fieldOfView = 38
        cam.camera?.zNear = 0.05
        cam.camera?.zFar = 200
        cam.position = SCNVector3(0, 0, CGFloat(distance))
        scene.rootNode.addChildNode(cam)
        let ambient = SCNNode()
        ambient.light = SCNLight()
        ambient.light?.type = .ambient
        ambient.light?.intensity = 420
        scene.rootNode.addChildNode(ambient)
        let key = SCNNode()
        key.light = SCNLight()
        key.light?.type = .directional
        key.light?.intensity = 900
        key.eulerAngles = SCNVector3(-0.7, 0.6, 0)
        scene.rootNode.addChildNode(key)
        return scene
    }

    /// The atom: its nucleus (protons red, neutrons gray — at most 72 drawn, in their proportion) and each shell a tilted
    /// ring with its electrons, going round, the outer ones slower.
    static func atom(_ e: PTElement) -> SCNScene {
        let shells = e.shells.isEmpty ? [e.number] : e.shells
        let outer = 0.9 + Double(shells.count - 1) * 0.55
        let scene = base(cameraAt: Float(outer * 2.9 + 1.2))
        let root = scene.rootNode

        let total = e.number + e.neutrons
        let count = min(total, 72)
        let protonShare = Double(e.number) / Double(max(total, 1))
        let nucleusRadius = 0.12 * cbrt(Double(count)) + 0.05
        let ball = SCNSphere(radius: 0.085)
        ball.segmentCount = 16
        let proton = ball.copy() as! SCNSphere
        proton.materials = [material(protonColor)]
        let neutron = ball.copy() as! SCNSphere
        neutron.materials = [material(neutronColor)]
        let nucleus = SCNNode()
        var protonsSoFar = 0.0
        for i in 0..<count {
            // (spread through a ball: a golden-angle spiral, each a little further out)
            let t = (Double(i) + 0.5) / Double(count)
            let r = nucleusRadius * cbrt(t)
            let y = 1 - 2 * t
            let ring = sqrt(max(0, 1 - y * y))
            let phi = Double(i) * 2.399963
            let node = SCNNode()
            let isProton = Double(i + 1) * protonShare - protonsSoFar >= 0.5
            if isProton { protonsSoFar += 1 }
            node.geometry = isProton ? proton : neutron
            node.position = SCNVector3(CGFloat(r * ring * cos(phi)), CGFloat(r * y), CGFloat(r * ring * sin(phi)))
            nucleus.addChildNode(node)
        }
        // (1.3.80) nothing going round when the Mac is set to reduce motion
        let still = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        if !still { nucleus.runAction(.repeatForever(.rotateBy(x: 0.2, y: .pi * 2, z: 0, duration: 24))) }
        root.addChildNode(nucleus)

        let electron = SCNSphere(radius: 0.055)
        electron.materials = [material(electronColor, glow: true)]
        for (i, n) in shells.enumerated() {
            let r = 0.9 + Double(i) * 0.55
            let tilt = SCNNode()
            tilt.eulerAngles = SCNVector3(CGFloat(0.35 + Double(i) * 0.47), CGFloat(Double(i) * 0.9), CGFloat(0.2 * Double(i % 2 == 0 ? 1 : -1)))
            let torus = SCNTorus(ringRadius: CGFloat(r), pipeRadius: 0.006)
            torus.ringSegmentCount = 96
            torus.materials = [material(.white, alpha: 0.28)]
            tilt.addChildNode(SCNNode(geometry: torus))
            let spin = SCNNode()
            for k in 0..<n {
                let a = Double(k) / Double(n) * 2 * .pi
                let node = SCNNode(geometry: electron)
                node.position = SCNVector3(CGFloat(r * cos(a)), 0, CGFloat(r * sin(a)))
                spin.addChildNode(node)
            }
            if !still { spin.runAction(.repeatForever(.rotateBy(x: 0, y: .pi * 2, z: 0, duration: 3 + Double(i) * 1.6))) }
            tilt.addChildNode(spin)
            root.addChildNode(tilt)
        }
        return scene
    }

    // MARK: Orbitals

    static func orbitalNames(_ l: Int) -> [String] {
        switch l {
        case 0: return ["s"]
        case 1: return ["px", "py", "pz"]
        case 2: return ["dxy", "dxz", "dyz", "dx²−y²", "dz²"]
        default: return ["fz³", "fxz²", "fyz²", "fxyz", "fz(x²−y²)", "fx(x²−3y²)", "fy(3x²−y²)"]
        }
    }

    /// A lobe's directions for each orbital of a subshell (an orbital as lobes pointing out from the nucleus; rings for
    /// dz²'s and fz³'s collars). The f shapes are drawn simply.
    private static func lobes(_ l: Int, _ i: Int) -> (dirs: [SIMD3<Double>], rings: [Double]) {
        let s = 0.7071
        switch l {
        case 1:
            let axis: [SIMD3<Double>] = [SIMD3(1, 0, 0), SIMD3(0, 1, 0), SIMD3(0, 0, 1)]
            return ([axis[i], -axis[i]], [])
        case 2:
            switch i {
            case 0: return ([SIMD3(s, s, 0), SIMD3(-s, -s, 0), SIMD3(s, -s, 0), SIMD3(-s, s, 0)], [])
            case 1: return ([SIMD3(s, 0, s), SIMD3(-s, 0, -s), SIMD3(s, 0, -s), SIMD3(-s, 0, s)], [])
            case 2: return ([SIMD3(0, s, s), SIMD3(0, -s, -s), SIMD3(0, s, -s), SIMD3(0, -s, s)], [])
            case 3: return ([SIMD3(1, 0, 0), SIMD3(-1, 0, 0), SIMD3(0, 1, 0), SIMD3(0, -1, 0)], [])
            default: return ([SIMD3(0, 0, 1), SIMD3(0, 0, -1)], [0])
            }
        case 3:
            switch i {
            case 0: return ([SIMD3(0, 0, 1), SIMD3(0, 0, -1)], [0.35, -0.35])
            case 1: return ([SIMD3(0.55, 0, 0.83), SIMD3(-0.55, 0, 0.83), SIMD3(0.55, 0, -0.83), SIMD3(-0.55, 0, -0.83), SIMD3(1, 0, 0), SIMD3(-1, 0, 0)], [])
            case 2: return ([SIMD3(0, 0.55, 0.83), SIMD3(0, -0.55, 0.83), SIMD3(0, 0.55, -0.83), SIMD3(0, -0.55, -0.83), SIMD3(0, 1, 0), SIMD3(0, -1, 0)], [])
            case 3:
                let c = 0.577
                return ([SIMD3(c, c, c), SIMD3(-c, -c, c), SIMD3(c, -c, -c), SIMD3(-c, c, -c), SIMD3(-c, c, c), SIMD3(c, -c, c), SIMD3(c, c, -c), SIMD3(-c, -c, -c)], [])
            case 4: return ([SIMD3(s, 0, s), SIMD3(-s, 0, s), SIMD3(s, 0, -s), SIMD3(-s, 0, -s), SIMD3(0, s, s), SIMD3(0, -s, s), SIMD3(0, s, -s), SIMD3(0, -s, -s)], [])
            case 5: return ((0..<6).map { k -> SIMD3<Double> in let a = Double(k) * .pi / 3; return SIMD3<Double>(cos(a), sin(a), 0) }, [])
            default: return ((0..<6).map { k -> SIMD3<Double> in let a = Double(k) * .pi / 3 + .pi / 6; return SIMD3<Double>(cos(a), sin(a), 0) }, [])
            }
        default:
            return ([], [])
        }
    }

    /// A subshell's orbitals side by side, each its lobes (the two phases in two colours; an empty orbital faint).
    static func orbitals(_ sub: PTSubshell) -> SCNScene {
        let names = orbitalNames(sub.l)
        let gap = 2.6
        let width = Double(names.count - 1) * gap
        // (1.3.57) far enough back that an s orbital's sphere, or the row of p, d or f orbitals, sits well inside the view
        let sRadius = 0.45 + 0.12 * Double(sub.n)
        let scene = base(cameraAt: Float(max(5.4, width * 0.95 + 4.6, sub.l == 0 ? sRadius * 6.2 : 0)))
        let root = scene.rootNode
        let filling = sub.filling
        let plus = NSColor(ptHex: "0a84ff"), minus = NSColor(ptHex: "ff9f0a")
        for i in 0..<names.count {
            let holder = SCNNode()
            holder.position = SCNVector3(CGFloat(Double(i) * gap - width / 2), 0, 0)
            let filled = i < filling.count ? filling[i] : 0
            let alpha: CGFloat = filled == 0 ? 0.16 : (filled == 1 ? 0.55 : 0.8)
            // (the nucleus, a dot at the centre)
            let core = SCNNode(geometry: SCNSphere(radius: 0.06))
            core.geometry?.materials = [material(protonColor)]
            holder.addChildNode(core)
            if sub.l == 0 {
                let sphere = SCNSphere(radius: CGFloat(sRadius))
                sphere.segmentCount = 48
                sphere.materials = [material(plus, alpha: alpha)]
                holder.addChildNode(SCNNode(geometry: sphere))
            } else {
                let (dirs, rings) = lobes(sub.l, i)
                let lobeLength = sub.l == 1 ? 0.75 : 0.62
                for (k, d) in dirs.enumerated() {
                    let lobe = SCNSphere(radius: 0.5)
                    lobe.segmentCount = 32
                    // (the phases alternate round the orbital)
                    lobe.materials = [material(k % 2 == 0 ? plus : minus, alpha: alpha)]
                    let node = SCNNode(geometry: lobe)
                    node.scale = SCNVector3(CGFloat(0.5), CGFloat(lobeLength), CGFloat(0.5))
                    let p = d * lobeLength * 0.92
                    node.simdPosition = SIMD3<Float>(Float(p.x), Float(p.y), Float(p.z))
                    node.simdLook(at: SIMD3<Float>(Float(d.x * 3), Float(d.y * 3), Float(d.z * 3)), up: abs(d.y) > 0.9 ? SIMD3<Float>(1, 0, 0) : SIMD3<Float>(0, 1, 0), localFront: SIMD3<Float>(0, 1, 0))
                    holder.addChildNode(node)
                }
                for z in rings {
                    let torus = SCNTorus(ringRadius: 0.42, pipeRadius: 0.12)
                    torus.materials = [material(minus, alpha: alpha)]
                    let node = SCNNode(geometry: torus)
                    node.eulerAngles = SCNVector3(CGFloat.pi / 2, 0, 0)
                    node.position = SCNVector3(0, 0, CGFloat(z))
                    holder.addChildNode(node)
                }
            }
            root.addChildNode(holder)
        }
        if !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            root.runAction(.repeatForever(.rotateBy(x: 0, y: .pi * 2, z: 0, duration: 40)))
        }
        return scene
    }

    // MARK: Electron cloud (1.3.76)

    /// A hair under opaque, for the cloud's parts that would otherwise be opaque (the dark glow, the nucleus): every part
    /// then goes in SceneKit's see-through pass, so the rendering order alone stacks them — the grey rest, then f, d, p,
    /// s, then the nucleus over all — however SceneKit orders its opaque pass against that one.
    private static let cloudNearlyOpaque: CGFloat = 0.999

    /// A cloud node's material: 0…3 the s, p, d or f dots, 4 the grey rest. In dark the chosen dots' light adds up (a
    /// glow, white where they crowd); in light they are ink. The rest is grey ink in both. (Starting values, to be set
    /// against Windows' pictures: SceneKit stacks n dots of opacity a as 1 − (1 − a)ⁿ, in linear light.)
    static func cloudMaterial(_ key: Int, dark: Bool) -> SCNMaterial {
        let m = SCNMaterial()
        m.lightingModel = .constant
        m.diffuse.contents = key == 4 ? ElectronCloud.restColor(dark: dark) : ElectronCloud.color(key, dark: dark)
        if key != 4 && dark {
            m.blendMode = .add
            m.diffuse.intensity = 0.45
            m.transparency = cloudNearlyOpaque
        } else {
            m.blendMode = .alpha
            m.transparency = key == 4 ? 0.35 : 0.55
        }
        m.writesToDepthBuffer = false
        m.readsFromDepthBuffer = false
        m.isDoubleSided = true
        return m
    }

    /// The electron cloud: the element's dots, to scale, framed by the chosen subshell (or the whole atom) so that its
    /// 95% sphere is a unit across the middle of the view; tipped (−1 about x, then −0.6 about y) so its z axis stands up
    /// and leans towards you; turning once in 40 seconds and drawing a few dots afresh ten times a second, where motion
    /// is wanted (not with Reduce Motion, nor in the screenshot suite's pictures); no rings, since the electrons follow
    /// no paths; and the nucleus marked on top, not to scale.
    static func cloud(_ e: PTElement, focus: String?, dark: Bool) -> SCNScene {
        let c = ElectronCloud.cloud(for: e)
        let focusIndex = c.subs.firstIndex { $0.id == focus }
        let frame = focusIndex.map { c.subs[$0].radius(0.95) } ?? c.frameAll
        let scene = base(cameraAt: 4)
        let spin = SCNNode()
        let pose = SCNNode()
        // (the right-hand turn is made first)
        pose.simdOrientation = simd_quatf(angle: -0.6, axis: SIMD3<Float>(0, 1, 0)) * simd_quatf(angle: -1.0, axis: SIMD3<Float>(1, 0, 0))
        spin.addChildNode(pose)
        scene.rootNode.addChildNode(spin)
        let state = CloudState(cloud: c, focus: focusIndex, frame: frame, dark: dark, holder: pose)
        // (the nucleus: a dot on a halo that parts it from a crowded core, drawn over everything)
        let marks: [(CGFloat, NSColor, Int)] = [(0.036, ElectronCloud.haloColor(dark: dark), 100), (0.024, ElectronCloud.nucleusColor(dark: dark), 101)]
        for (radius, color, order) in marks {
            let ball = SCNSphere(radius: radius)
            ball.segmentCount = 24
            let m = SCNMaterial()
            m.lightingModel = .constant
            m.diffuse.contents = color
            m.blendMode = .alpha
            m.transparency = cloudNearlyOpaque
            m.readsFromDepthBuffer = false
            m.writesToDepthBuffer = false
            ball.materials = [m]
            let node = SCNNode(geometry: ball)
            node.renderingOrder = order
            scene.rootNode.addChildNode(node)
        }
        let still = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion || UserDefaults.standard.string(forKey: "SimplShotFile") != nil
        if !still {
            spin.runAction(.repeatForever(.rotateBy(x: 0, y: .pi * 2, z: 0, duration: 40)))
            // (the action holds the state, and the scene the action: it goes with the scene)
            let tick = SCNAction.run({ _ in state.shimmer() }, queue: .main)
            pose.runAction(.repeatForever(.sequence([.wait(duration: 0.1), tick])))
        }
        return scene
    }
}

/// (1.3.76) The Cloud view's dots as SceneKit nodes — one for each kind of dot (s, p, d, f, or the grey rest), each a
/// geometry of points — and the shimmer that draws a few of them afresh now and then (main thread). Its dots are a copy
/// of the cached cloud's (arrays copy on write): the shimmer never reaches the cache.
final class CloudState {
    private var cloud: ElectronCloud.Cloud
    private let focus: Int?
    private let frame: Double
    private let dark: Bool
    /// Each kind's dots, by index.
    private var members: [Int: [Int]] = [:]
    private var nodes: [Int: SCNNode] = [:]
    /// Each kind's material, made once (the shimmer makes its points again, not its look).
    private var materials: [Int: SCNMaterial] = [:]
    /// The shimmer's own generator (the cloud's stream stays as both apps draw it).
    private var rng: ElectronCloud.Rng

    init(cloud: ElectronCloud.Cloud, focus: Int?, frame: Double, dark: Bool, holder: SCNNode) {
        self.cloud = cloud
        self.focus = focus
        self.frame = frame
        self.dark = dark
        rng = ElectronCloud.Rng(UInt64(cloud.z) ^ 0x5DEECE66D)
        for i in 0..<cloud.count { members[kind(i), default: []].append(i) }
        for key in members.keys.sorted() {
            let node = SCNNode()
            // (the grey rest first, then f, d, p and s over it: SceneKit stacks the kinds in turn where Windows averages
            // their ink by how many dots fall there, so the s — 1s, crowded at the centre — goes last, keeping the core
            // red as Windows has it, rather than browned by the p over it)
            node.renderingOrder = key == 4 ? 10 : 14 - key
            node.geometry = geometry(key)
            holder.addChildNode(node)
            nodes[key] = node
        }
    }

    /// What a dot is drawn as: its subshell's kind (0 s … 3 f), or 4 (the grey rest) when another subshell is chosen.
    private func kind(_ i: Int) -> Int {
        let s = Int(cloud.subIndex[i])
        if let f = focus, s != f { return 4 }
        return min(max(cloud.subs[s].l, 0), 3)
    }

    /// A kind's dots as points, in scene units (the frame's radius one); nil for none (a geometry is never made empty).
    private func geometry(_ key: Int) -> SCNGeometry? {
        guard let idx = members[key], !idx.isEmpty else { return nil }
        let k = 1.0 / frame
        let vs = idx.map { i in SCNVector3(CGFloat(cloud.x[i] * k), CGFloat(cloud.y[i] * k), CGFloat(cloud.zs[i] * k)) }
        let element = SCNGeometryElement(indices: Array(0..<Int32(vs.count)), primitiveType: .point)
        // (about 1.4 points across at the first zoom, more as it comes closer, 1 to 3 pixels' radius on the screen:
        // SceneKit takes pointSize as a radius — 0.012, the first try, drew dots twice Windows' width, a solid blob)
        element.pointSize = 0.006
        element.minimumPointScreenSpaceRadius = 1
        element.maximumPointScreenSpaceRadius = 3
        let g = SCNGeometry(sources: [SCNGeometrySource(vertices: vs)], elements: [element])
        let material = materials[key] ?? AtomScene.cloudMaterial(key, dark: dark)
        materials[key] = material
        g.materials = [material]
        return g
    }

    /// A few dots drawn afresh (0.5% of them a tick, ten ticks a second, each from its own subshell and orbital): where
    /// an electron could be found keeps changing, and the cloud keeps its shape.
    func shimmer() {
        let n = cloud.count
        guard n > 0 else { return }
        var dirty = Set<Int>()
        for _ in 0..<max(1, n / 200) {
            let i = min(n - 1, Int(rng.next() * Double(n)))
            let s = cloud.subs[Int(cloud.subIndex[i])]
            let p = ElectronCloud.sample(&rng, s, Int(cloud.orbital[i]))
            cloud.x[i] = p.0
            cloud.y[i] = p.1
            cloud.zs[i] = p.2
            dirty.insert(kind(i))
        }
        for key in dirty { nodes[key]?.geometry = geometry(key) }
    }
}

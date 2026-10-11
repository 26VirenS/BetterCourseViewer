import AppKit
import SceneKit
import SwiftUI

// (1.3.51) An element in 3D, for its page in the Periodic Table: its atom (the nucleus's protons and neutrons, each shell
// a ring with its electrons going round) or the shapes of a subshell's orbitals, each with its electrons — turned and
// zoomed by dragging and scrolling (SceneKit's own camera control).

struct Atom3DView: NSViewRepresentable {
    let element: PTElement
    let orbitals: Bool
    let subshell: PTSubshell?

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
        let key = "\(element.number)-\(orbitals)-\(subshell?.id ?? "")"
        guard key != c.key else { return }
        c.key = key
        let scene = orbitals ? AtomScene.orbitals(subshell ?? PTSubshell(n: 1, l: 0, count: 1)) : AtomScene.atom(element)
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
        nucleus.runAction(.repeatForever(.rotateBy(x: 0.2, y: .pi * 2, z: 0, duration: 24)))
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
            spin.runAction(.repeatForever(.rotateBy(x: 0, y: .pi * 2, z: 0, duration: 3 + Double(i) * 1.6)))
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
        root.runAction(.repeatForever(.rotateBy(x: 0, y: .pi * 2, z: 0, duration: 40)))
        return scene
    }
}

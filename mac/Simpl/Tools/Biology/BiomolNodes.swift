import AppKit
import SceneKit

// The Biomolecule Builder's SceneKit pieces, added to a scene as its extras (spec §4.4, §9): the chemistry builder's
// tables handed to the scenes (CPK colours and radii from MBChem), the atoms' symbols and a sugar's or a nucleotide's
// numbered carbons, charge marks, a motion's groups moving and fading once (a residue sliding in as water lifts off,
// a sugar split by water) on the app's strong ease-out, and ubiquitin's fold morphing into a random coil and back.

enum BiomolNodes {
    /// The chemistry builder's tables: CPK colours (MBChem.color), covalent and van der Waals radii.
    static let chem = BiomolChem(cpk: { BioMoleculeScene.cpkHex($0) }, covalent: { MBChem.covalent($0) }, vdw: { MBChem.vdw($0) })

    static func vec(_ v: BioVec) -> SCNVector3 { SCNVector3(CGFloat(v.x), CGFloat(v.y), CGFloat(v.z)) }

    /// Each atom's symbol on its near side, facing the camera (the chemistry builder's labels), or the text `text`
    /// gives it ("1" on a sugar's C1); none in sticks, none for hidden H.
    static func atomLabels(_ model: BioMoleculeModel, look: BiomolLook, text: (BioMoleculeModel.Atom) -> String? = { $0.element }) -> [SCNNode] {
        guard look.style != .sticks else { return [] }
        return model.atoms.compactMap { a in
            if !look.hydrogens && a.element == "H" { return nil }
            guard let s = text(a) else { return nil }
            let r = BioMoleculeMeshes.radius(style: look.style, covalent: MBChem.covalent(a.element), vdw: MBChem.vdw(a.element))
            let size = s.count > 1 ? max(0.26, r * 0.62) : max(0.32, r * 0.75)
            let n = MoleculeScene.label(s, size: size, radius: r, dark: ["H", "F", "Cl", "S"].contains(a.element))
            n.position = vec(a.pos)
            return n
        }
    }

    /// Short texts in the scene (charges), each facing the camera, drawn over what is near them.
    static func marks(_ marks: [BiomolMark]) -> [SCNNode] {
        marks.map { m in
            let text = SCNText(string: m.text, extrusionDepth: 0)
            text.font = NSFont.systemFont(ofSize: 1, weight: .heavy)
            text.flatness = 0.02
            let mat = SCNMaterial()
            mat.diffuse.contents = m.hex.map { NSColor(ptHex: $0) } ?? NSColor.white
            mat.lightingModel = .constant
            text.materials = [mat]
            let n = SCNNode(geometry: text)
            let (lo, hi) = n.boundingBox
            n.pivot = SCNMatrix4MakeTranslation((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, 0)
            n.scale = SCNVector3(CGFloat(m.size), CGFloat(m.size), CGFloat(m.size))
            n.renderingOrder = 12
            let holder = SCNNode()
            holder.addChildNode(n)
            holder.constraints = [SCNBillboardConstraint()]
            holder.position = vec(m.pos)
            return holder
        }
    }

    /// A motion's groups, each batched into a node that moves from its start to its end and fades, after its delay, on
    /// the app's strong ease-out (a pulse flashes in and out). They are not picked.
    static func motion(_ m: BiomolMotion) -> [SCNNode] {
        m.groups.compactMap { g in
            guard !g.items.isEmpty else { return nil }
            let holder = SCNNode()
            for b in BioSceneKit.batches(g.items) {
                b.node.name = nil
                holder.addChildNode(b.node)
            }
            holder.position = vec(g.from)
            holder.opacity = CGFloat(g.pulse ? 0 : g.fromAlpha)
            var steps: [SCNAction] = []
            if g.delay > 0 { steps.append(.wait(duration: g.delay)) }
            if g.pulse {
                let up = SCNAction.fadeOpacity(to: 1, duration: g.duration * 0.3)
                let down = SCNAction.fadeOpacity(to: 0, duration: g.duration * 0.7)
                down.timingMode = .easeIn
                steps.append(.sequence([up, down]))
            } else {
                let move = SCNAction.move(to: vec(g.to), duration: g.duration)
                let fade = SCNAction.fadeOpacity(to: CGFloat(g.toAlpha), duration: g.duration)
                move.timingFunction = { Float(BiomolEase.strongOut(Double($0))) }
                fade.timingFunction = { Float(BiomolEase.strongOut(Double($0))) }
                steps.append(.group([move, fade]))
            }
            if let out = g.fadeOutAt {
                let wait = out - (g.delay + g.duration)
                if wait > 0 { steps.append(.wait(duration: wait)) }
                steps.append(.fadeOpacity(to: 0, duration: 0.2))
            }
            holder.runAction(.sequence(steps))
            return holder
        }
    }

    /// Ubiquitin's fold and its random coil as one geometry each part, morphing between them (weight 0 the fold, 1 the
    /// coil): from `from` to `to` over 1.5 s (ease in and out) if `animate`, else held at `to`.
    static func morph(_ parts: [(hex: String, native: BioMesh, coil: BioMesh)], from: Double, to: Double, animate: Bool) -> [SCNNode] {
        parts.map { p in
            let mat = BioSceneKit.material(BioMaterial(p.hex, roughness: 0.45))
            let node = SCNNode(geometry: BioSceneKit.geometry(p.native, mat))
            let morpher = SCNMorpher()
            morpher.targets = [BioSceneKit.geometry(p.coil, mat)]
            node.morpher = morpher
            morpher.setWeight(CGFloat(animate ? from : to), forTargetAt: 0)
            if animate {
                node.runAction(.customAction(duration: 1.5) { n, elapsed in
                    let t = min(max(Double(elapsed) / 1.5, 0), 1)
                    let e = t < 0.5 ? 2 * t * t : 1 - pow(-2 * t + 2, 2) / 2
                    n.morpher?.setWeight(CGFloat(from + (to - from) * e), forTargetAt: 0)
                })
            }
            return node
        }
    }
}

import AppKit
import SceneKit

// The biology scenes' SceneKit side (spec §3.4): meshes made SceneKit geometry (float vertices and normals straight
// from the mesh's memory), materials as the chemistry scenes have them (physically based, metalness 0.05; translucent
// ones drawn after the opaque, not writing depth, both sides), a scene's items batched into one geometry per material
// with the map from triangles back to objects, and molecules drawn from a BioMoleculeModel with the chemistry
// builder's colours and radii.

enum BioSceneKit {
    /// A mesh as SceneKit geometry, with a material.
    static func geometry(_ mesh: BioMesh, _ material: SCNMaterial) -> SCNGeometry {
        let step = MemoryLayout<SIMD3<Float>>.stride
        let vertexData = mesh.positions.withUnsafeBufferPointer { Data(buffer: $0) }
        let normalData = mesh.normals.withUnsafeBufferPointer { Data(buffer: $0) }
        let vertices = SCNGeometrySource(data: vertexData, semantic: .vertex, vectorCount: mesh.positions.count, usesFloatComponents: true,
                                         componentsPerVector: 3, bytesPerComponent: MemoryLayout<Float>.size, dataOffset: 0, dataStride: step)
        let normals = SCNGeometrySource(data: normalData, semantic: .normal, vectorCount: mesh.normals.count, usesFloatComponents: true,
                                        componentsPerVector: 3, bytesPerComponent: MemoryLayout<Float>.size, dataOffset: 0, dataStride: step)
        let element = SCNGeometryElement(indices: mesh.indices, primitiveType: .triangles)
        let g = SCNGeometry(sources: [vertices, normals], elements: [element])
        g.materials = [material]
        return g
    }

    /// A material as the chemistry scenes make theirs (AtomScene.material), with the toolkit's extras.
    static func material(_ m: BioMaterial) -> SCNMaterial {
        let color = NSColor(ptHex: m.hex)
        let s = SCNMaterial()
        if m.unlit {
            s.lightingModel = .constant
        } else {
            s.lightingModel = .physicallyBased
            s.metalness.contents = 0.05
            s.roughness.contents = m.roughness
        }
        s.diffuse.contents = color
        if m.emission > 0 { s.emission.contents = color.withAlphaComponent(CGFloat(min(m.emission, 1))) }
        s.isDoubleSided = m.doubleSided
        if m.isTranslucent {
            s.transparency = CGFloat(m.alpha)
            s.blendMode = .alpha
            s.writesToDepthBuffer = false
            s.isDoubleSided = true
        }
        return s
    }

    /// A node for a mesh in a material (translucent ones drawn after the opaque: rendering order 100 + `order`).
    static func node(_ mesh: BioMesh, _ m: BioMaterial, order: Int = 0) -> SCNNode {
        let n = SCNNode(geometry: geometry(mesh, material(m)))
        n.renderingOrder = m.isTranslucent ? 100 + order : order
        return n
    }

    /// A scene's items batched: one node per material (and stacking order), each with the map from its triangles to
    /// the objects they came from. Items whose id `skip` names are left out (the selected, drawn on their own).
    static func batches(_ items: [BioSceneItem], skip: (String?) -> Bool = { _ in false }) -> [(node: SCNNode, batch: BioMeshBatch)] {
        var groups: [String: (material: BioMaterial, order: Int, batch: BioMeshBatch)] = [:]
        var keys: [String] = []
        for it in items where !skip(it.id) {
            let key = "\(String(describing: it.material))/\(it.order)"
            if groups[key] == nil {
                groups[key] = (it.material, it.order, BioMeshBatch())
                keys.append(key)
            }
            groups[key]?.batch.add(it.mesh, id: it.id ?? "")
        }
        return keys.compactMap { k -> (node: SCNNode, batch: BioMeshBatch)? in
            guard let g = groups[k], !g.batch.mesh.isEmpty else { return nil }
            let n = node(g.batch.mesh, g.material, order: g.order)
            n.name = "bio-batch"
            return (node: n, batch: g.batch)
        }
    }
}

/// Molecules in a biology scene: a BioMoleculeModel drawn with the chemistry builder's CPK colours and radii (MBChem),
/// batched, its atoms picked as "atom:<id>"; and the atoms' symbols as the chemistry builder labels them.
enum BioMoleculeScene {
    /// The style as the chemistry builder names it.
    static func style(_ s: MoleculeModel.Style) -> BioAtomStyle {
        switch s {
        case .ballStick: return .ballStick
        case .spaceFill: return .spaceFill
        case .sticks: return .sticks
        }
    }

    /// An atom's CPK colour as "#rrggbb" (H white, C grey, O red, N blue, P orange, S yellow).
    static func cpkHex(_ symbol: String) -> String {
        let c = (MBChem.color(symbol).usingColorSpace(.sRGB) ?? NSColor.gray)
        func byte(_ v: CGFloat) -> String {
            let s = String(min(max(Int((v * 255).rounded()), 0), 255), radix: 16)
            return s.count == 1 ? "0" + s : s
        }
        return "#" + byte(c.redComponent) + byte(c.greenComponent) + byte(c.blueComponent)
    }

    /// A molecule's items: CPK colours unless `color` gives an atom another (by part or class), H atoms left out when
    /// `hydrogens` is off.
    static func items(_ model: BioMoleculeModel, style: BioAtomStyle, hydrogens: Bool = true,
                      color: ((BioMoleculeModel.Atom) -> String?)? = nil) -> [BioSceneItem] {
        var cpk: [String: String] = [:]
        var radii: [String: Double] = [:]
        for a in model.atoms where cpk[a.element] == nil {
            cpk[a.element] = cpkHex(a.element)
            radii[a.element] = BioMoleculeMeshes.radius(style: style, covalent: MBChem.covalent(a.element), vdw: MBChem.vdw(a.element))
        }
        return BioMoleculeMeshes.items(model, style: style, radius: { radii[$0.element] ?? 0.3 },
                                       color: { color?($0) ?? cpk[$0.element] ?? "#c8c8c8" },
                                       skip: { !hydrogens && $0.element == "H" })
    }

    /// The atoms' symbols, facing the camera on each atom's near side (the chemistry builder's labels), as nodes to add
    /// to a scene's extras.
    static func symbolNodes(_ model: BioMoleculeModel, style: BioAtomStyle, hydrogens: Bool = true) -> [SCNNode] {
        guard style != .sticks else { return [] }
        return model.atoms.compactMap { a in
            if !hydrogens && a.element == "H" { return nil }
            let r = BioMoleculeMeshes.radius(style: style, covalent: MBChem.covalent(a.element), vdw: MBChem.vdw(a.element))
            let n = MoleculeScene.label(a.element, size: max(0.32, r * 0.75), radius: r, dark: a.element == "H" || a.element == "F" || a.element == "Cl")
            n.position = SCNVector3(CGFloat(a.pos.x), CGFloat(a.pos.y), CGFloat(a.pos.z))
            return n
        }
    }
}

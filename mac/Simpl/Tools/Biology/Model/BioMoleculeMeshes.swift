import Foundation

// Atoms and bonds as scene items (spec §3.1 Reuse, §3.4): every atomic biomolecule — amino acids, peptides,
// nucleotides, B-DNA, sugars, lipids — drawn the way the chemistry builder draws a molecule (its radii per style, its
// bonds by order, CPK colours), but batched: one item per atom ("atom:<id>", so a click names it) and bonds that are not
// picked, all merged by material in the view. Spheres are UV spheres up to 300 atoms and icospheres above (§4.4
// Rendering limits). The element colours and radii come from the chemistry tables, handed in by the caller
// (BioSceneKit.swift's `BioMoleculeScene`), so this stays plain arithmetic.

/// How atoms are drawn (the chemistry builder's three styles).
enum BioAtomStyle: String, CaseIterable, Sendable {
    case ballStick, spaceFill, sticks

    var label: String { self == .ballStick ? "Ball & stick" : (self == .spaceFill ? "Space filling" : "Sticks") }
}

enum BioMoleculeMeshes {
    /// A unit sphere for atoms: UV (14 × 28) for small models, an icosphere (2: 320 triangles) for big ones.
    static let smallSphere = BioShapes.sphere(radius: 1, rings: 14, segments: 28)
    static let bigSphere = BioShapes.icosphere(radius: 1, subdivisions: 2)

    /// The drawn radius of an atom (Å), as MoleculeScene.radius has it: ball and stick 0.24 + 0.36 × covalent radius,
    /// space filling 0.9 × van der Waals radius, sticks 0.13.
    static func radius(style: BioAtomStyle, covalent: Double, vdw: Double) -> Double {
        switch style {
        case .ballStick: return 0.24 + 0.36 * covalent
        case .spaceFill: return vdw * 0.9
        case .sticks: return 0.13
        }
    }

    /// The scene items for a model: its atoms (each "atom:<id>", coloured by `color`, its radius by `radius`) and its
    /// bonds (ball and stick: grey cylinders, double and triple side by side; sticks: each half in its atom's colour;
    /// space filling: none). `skip` leaves atoms out (the H atoms, when they are hidden).
    static func items(_ model: BioMoleculeModel, style: BioAtomStyle, radius: (BioMoleculeModel.Atom) -> Double,
                      color: (BioMoleculeModel.Atom) -> String, bondHex: String = "#9e9e9e", roughness: Double = 0.35,
                      skip: (BioMoleculeModel.Atom) -> Bool = { _ in false }) -> [BioSceneItem] {
        let unit = model.atoms.count > 300 ? bigSphere : smallSphere
        var out: [BioSceneItem] = []
        var drawn = Set<Int>()
        for a in model.atoms where !skip(a) {
            drawn.insert(a.id)
            let r = radius(a)
            let mesh = unit.transformed(BioFrame(origin: a.pos, x: BioVec(r, 0, 0), y: BioVec(0, r, 0), z: BioVec(0, 0, r)))
            out.append(BioSceneItem(id: "atom:\(a.id)", mesh: mesh, material: BioMaterial(color(a), roughness: roughness)))
        }
        guard style != .spaceFill else { return out }
        let segments = model.atoms.count > 300 ? 10 : 16
        for b in model.bonds where drawn.contains(b.a) && drawn.contains(b.b) {
            let x = model.atoms[b.a], y = model.atoms[b.b]
            let d = y.pos - x.pos
            let len = BioV.length(d)
            guard len > 1e-6 else { continue }
            let dir = d / len
            var ref = BioV.cross(dir, BioVec(0, 0, 1))
            if BioV.length(ref) < 0.1 { ref = BioV.cross(dir, BioVec(0, 1, 0)) }
            let perp = BioV.unit(ref)
            if style == .sticks {
                let mid = (x.pos + y.pos) / 2
                out.append(BioSceneItem(id: nil, mesh: BioShapes.cylinder(from: x.pos, to: mid, radius: 0.13, segments: segments, caps: false),
                                        material: BioMaterial(color(x), roughness: roughness)))
                out.append(BioSceneItem(id: nil, mesh: BioShapes.cylinder(from: mid, to: y.pos, radius: 0.13, segments: segments, caps: false),
                                        material: BioMaterial(color(y), roughness: roughness)))
                continue
            }
            let thick = b.order == 1 ? 0.09 : 0.06
            let offsets: [Double] = b.order == 1 ? [0] : (b.order == 2 ? [-0.11, 0.11] : [-0.16, 0, 0.16])
            for o in offsets {
                out.append(BioSceneItem(id: nil, mesh: BioShapes.cylinder(from: x.pos + perp * o, to: y.pos + perp * o, radius: thick, segments: segments, caps: false),
                                        material: BioMaterial(bondHex, roughness: roughness)))
            }
        }
        return out
    }

    /// A dashed line as short cylinders (an H-bond: radius 0.08 Å in atom views; §4.2), `dash` long with `gap` between.
    static func dashes(from a: BioVec, to b: BioVec, radius: Double = 0.08, dash: Double = 0.3, gap: Double = 0.2) -> BioMesh {
        let len = BioV.distance(a, b)
        guard len > 1e-6 else { return BioMesh() }
        let dir = (b - a) / len
        var m = BioMesh()
        var s = 0.0
        while s < len {
            let e = min(s + dash, len)
            m.append(BioShapes.cylinder(from: a + dir * s, to: a + dir * e, radius: radius, segments: 8))
            s = e + gap
        }
        return m
    }
}

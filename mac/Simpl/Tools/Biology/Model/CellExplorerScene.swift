import Foundation

// A built cell made into a scene the shared 3D view draws (BioSceneContent): the parts hidden in the parts list left
// out, one part alone when isolated (§4.1 item 3) or seen inside (a mitochondrion or chloroplast cut lengthwise, the
// nucleus cut open, with its insides labelled), every organelle moved out by the explode slider (§4.1 item 5:
// explode × 0.6 × cell radius × (dist / cell radius)^0.5 along the line from the cell's centre to its own, the nucleus
// moving least) while the membrane and wall fade to 15% between 0 and 0.3, each part's label at its anchor, and the
// light or dark colours. And the Compare page's three cells side by side at their true relative sizes (§4.1 item 8).
// Foundation only.

/// What the view asks of a built cell.
struct CEView: Hashable, Sendable {
    var explode = 0.0
    var hidden: Set<String> = []
    var isolate: String?
    var lookInside = false
    var dark = false
}

enum CEScene {
    /// How far an organelle moves when exploded (§4.1 item 5).
    static func displacement(_ inst: CEInstance, explode: Double, cellRadius rc: Double) -> BioVec {
        guard explode > 0, !inst.shell, !inst.fixed else { return BioVec(0, 0, 0) }
        let dist = BioV.length(inst.explodeFrom)
        guard dist > 1e-9 else { return BioVec(0, 0, 0) }
        return inst.explodeFrom / dist * (explode * 0.6 * rc * (dist / rc).squareRoot())
    }

    /// The shells' opacity, as a fraction of their own: 1 at explode 0, 15% from 0.3 on.
    static func shellFade(_ explode: Double) -> Double { 1 - 0.85 * min(max(explode, 0) / 0.3, 1) }

    /// Whether a part can be looked inside.
    static func canLookInside(_ part: String) -> Bool { ["mito", "chloroplast", "nucleus"].contains(part) }

    /// The parts drawn in clusters: there are hundreds or thousands of them, each a few triangles.
    static let clustered: Set<String> = ["ribosome", "pore", "actin", "fimbria"]

    /// The scene object an instance is drawn in: itself ("organelle:mito:3"), or, for a clustered part, the cluster of
    /// the cell's space it sits in ("organelle:ribosome:g2_-1_0"; cubes a third of the cell's radius across, by its
    /// unexploded centre, so a cluster stays one as the cell explodes).
    static func itemID(_ inst: CEInstance, in m: CEModel) -> String {
        guard clustered.contains(inst.part) else { return inst.id }
        let s = m.cellRadius / 3
        let c = inst.centre
        return "organelle:\(inst.part):g\(Int((c.x / s).rounded(.down)))_\(Int((c.y / s).rounded(.down)))_\(Int((c.z / s).rounded(.down)))"
    }

    /// The scene object a part's label points into (for Zoom to).
    static func anchorItemID(_ m: CEModel, part: String) -> String? {
        guard let a = m.anchors[part], a.instance < m.instances.count else { return nil }
        return itemID(m.instances[a.instance], in: m)
    }

    static func content(_ m: CEModel, _ v: CEView) -> BioSceneContent {
        let explode = v.isolate == nil ? min(max(v.explode, 0), 1) : 0
        let fade = shellFade(explode)
        var items: [BioSceneItem] = []
        var labels: [BioLabel3D] = []
        func material(_ p: CEPiece, shell: Bool) -> BioMaterial {
            BioMaterial(p.colour.hex(dark: v.dark), alpha: shell ? p.alpha * fade : p.alpha, roughness: p.roughness)
        }
        if let iso = v.isolate, v.lookInside, canLookInside(iso) {
            let inside = CEBuild.lookInsideScene(m, part: iso)
            for inst in inside.instances where !v.hidden.contains(inst.part) || inst.part == iso {
                for p in inst.pieces {
                    items.append(BioSceneItem(id: inst.id, mesh: p.mesh, material: material(p, shell: false), order: p.order))
                }
            }
            return BioSceneContent(items: items, labels: inside.labels, frame: nil)
        }
        var moved: [Int: BioVec] = [:]
        // (the parts of thousands of tiny pieces are drawn in clusters, each its own object: a selection then glows a
        // few dozen objects rather than thousands, and a click or Zoom to finds a cluster)
        var clusters: [String: Int] = [:]
        for (i, inst) in m.instances.enumerated() {
            if v.hidden.contains(inst.part) { continue }
            if let iso = v.isolate, inst.part != iso { continue }
            let d = displacement(inst, explode: explode, cellRadius: m.cellRadius)
            moved[i] = d
            let id = itemID(inst, in: m)
            for p in inst.pieces {
                let mat = material(p, shell: inst.shell)
                let mesh = CEGeo.moved(p.mesh, d)
                if p.round && mat.isTranslucent {
                    // (1.3.79) a membrane's or a wall's face, translucent: its far side drawn before the organelles
                    // and its near side after them (BioFaceSide) — drawn whole after them, its far side lay over them
                    // too, and the whole cell was one gold veil on a first look
                    for side in [BioFaceSide.far, .near] {
                        var m = mat
                        m.side = side
                        items.append(BioSceneItem(id: id, mesh: mesh.facing(outward: side == .near, from: BioVec(0, 0, 0)), material: m, order: p.order))
                    }
                    continue
                }
                if id != inst.id {
                    let key = id + "|" + String(describing: mat) + "|\(p.order)"
                    if let k = clusters[key] {
                        items[k].mesh.append(mesh)
                    } else {
                        clusters[key] = items.count
                        items.append(BioSceneItem(id: id, mesh: mesh, material: mat, order: p.order))
                    }
                } else {
                    items.append(BioSceneItem(id: id, mesh: mesh, material: mat, order: p.order))
                }
            }
        }
        for part in CEParts.all(m.options.kind) where !v.hidden.contains(part.id) {
            if let iso = v.isolate, iso != part.id { continue }
            guard let a = m.anchors[part.id], let d = moved[a.instance] else { continue }
            labels.append(BioLabel3D(id: "organelle:\(part.id)", text: part.label, anchor: a.point + d, normal: a.normal, area: part.priority))
        }
        let frame = v.isolate == nil ? BioSphere(centre: BioVec(0, 0, 0), radius: m.frameRadius * (1 + 0.45 * explode)) : nil
        return BioSceneContent(items: items, labels: labels, frame: frame)
    }

    /// "3D model of an animal cell, cut open; 17 kinds of parts; selected: mitochondria" (§9 Accessibility).
    static func accessibility(_ m: CEModel?, kind: CEKind, cutaway: Bool, hidden: Set<String>, selected: String?) -> String {
        let kinds = m.map { $0.partsShown.subtracting(hidden).count } ?? CEParts.all(kind).filter(\.defaultVisible).count
        var s = "3D model of \(kind == .animal ? "an animal cell" : (kind == .plant ? "a plant cell" : "a bacterial cell"))"
        if cutaway { s += ", cut open" }
        s += "; \(kinds) kinds of parts"
        if let selected, let p = CEParts.part(kind, selected) { s += "; selected: \(p.nameInSentence)" }
        return s
    }
}

extension CEBuild {
    /// Look inside as a scene: the instances, and labels for what is inside (the membranes, cristae and matrix of a
    /// mitochondrion; a chloroplast's envelope, grana, stroma, lamellae, starch and DNA; the nucleus's envelope, pores,
    /// nucleolus, chromatin and nucleoplasm).
    static func lookInsideScene(_ m: CEModel, part: String) -> (instances: [CEInstance], labels: [BioLabel3D]) {
        var b = CEBuilder(m.options)
        b.model = m
        let instances = b.lookInside(part)
        var labels: [BioLabel3D] = []
        let d = m.display
        switch part {
        case "mito", "chloroplast":
            guard let a = m.anchors[part], let f = m.instances[a.instance].frame else { break }
            let half = CEGeo.frame(y: f.y, towardZ: m.view, at: f.origin)
            // (each with the cut face's normal, toward the camera: the inside is one object, so a ray to a crista or the
            // matrix met another part of it first and hid the label — the picture inside a mitochondrion had neither
            // "Cristae" nor "Matrix")
            let facing = d.applyVector(half.applyVector(BioVec(0, 0, 1)))
            func label(_ id: String, _ text: String, _ local: BioVec, _ priority: Double) {
                labels.append(BioLabel3D(id: id, text: text, anchor: d.apply(half.apply(local)), normal: facing, area: priority))
            }
            if part == "mito" {
                label("organelle:mito", "Outer membrane", BioVec(-0.443, -0.35, -0.03), 9)
                label("inside:mito:inner", "Inner membrane", BioVec(0.39, -0.75, -0.02), 8)
                label("inside:mito:cristae", "Cristae", BioVec(0.12, 0.026, -0.15), 10)
                label("inside:mito:matrix", "Matrix", BioVec(0, 0.36, -0.37), 7)
            } else {
                label("organelle:chloroplast", "Envelope (two membranes)", BioVec(-2.47, 0, -0.02), 9)
                label("inside:chloroplast:granum", "Granum (thylakoid stack)", BioVec(0, 0.243, 0), 10)
                label("inside:chloroplast:stroma", "Stroma", BioVec(0.9, -0.62, -0.9), 8)
                if let lam = firstLamella() { label("inside:chloroplast:lamella", "Stroma lamella", lam, 6) }
                label("inside:chloroplast:starch", "Starch grain", BioVec(1.5, 0.15, -0.85), 5)
                label("inside:chloroplast:dna", "DNA", BioVec(-0.02, 0, -0.6), 4)
            }
        case "nucleus":
            var nb = CEBuilder(CEBuildOptions(kind: m.options.kind, trueScale: m.options.trueScale, cutaway: true, gramPositive: false))
            _ = nb.rng.unitVector()
            let N = m.options.kind == .animal ? BioVec(1.0, 0.5, -0.5) : BioVec(-9, 9.2, 0)
            let R = m.options.kind == .animal ? 3.2 : 3.0
            nb.nucleus(N, R)
            nb.anchors()
            let names: [(String, String, Double)] = [("nucleus", "Nuclear envelope", 10), ("pore", "Nuclear pore", 8),
                                                     ("nucleolus", "Nucleolus", 9), ("chromatin", "Chromatin", 7)]
            for (id, text, pr) in names {
                guard let a = nb.model.anchors[id] else { continue }
                labels.append(BioLabel3D(id: "organelle:\(id)", text: text, anchor: d.apply(a.point), normal: a.normal.map { d.applyVector($0) }, area: pr))
            }
            labels.append(BioLabel3D(id: "inside:nucleus:nucleoplasm", text: "Nucleoplasm",
                                     anchor: d.apply(N + BioVec(0.35 * R, 0.55 * R, 0)), normal: d.applyVector(BioVec(0, 0, 1)), area: 6))
        default:
            break
        }
        return (instances, labels)
    }

    /// The first stroma lamella's middle, in a chloroplast's frame (as CERecipes.chloroplast lays them).
    static func firstLamella() -> BioVec? {
        let kept = CERecipes.granaCentres.filter { $0.z <= 0.3 }
        for i in 0..<kept.count {
            for j in (i + 1)..<kept.count where abs(BioV.distance(kept[i], kept[j]) - 1.0) < 0.05 {
                return (kept[i] + kept[j]) / 2 + BioVec(0, 0.1, 0)
            }
        }
        return nil
    }

    // MARK: The Compare page's three cells (§4.1 item 8)

    /// The three cells side by side at their true relative sizes — a bacterium 2 µm long, an animal cell 20 µm, a
    /// plant cell 40 µm — each simplified (its outline, its nucleus or nucleoid, its larger organelles), picked as
    /// "cell:<kind>", labelled with its size.
    static func compareScene(dark: Bool) -> BioSceneContent {
        var items: [BioSceneItem] = []
        var labels: [BioLabel3D] = []
        let turn = BioFrame.rotation(axis: BioVec(0, 1, 0), degrees: -18).then(BioFrame.rotation(axis: BioVec(1, 0, 0), degrees: 14))
        func put(_ kind: CEKind, _ m: BioMesh, _ hex: BioHexPair, alpha: Double = 1, roughness: Double = 0.45, order: Int = 0,
                 scale: Double, at x: Double) {
            let f = BioFrame(origin: BioVec(x, 0, 0), x: BioVec(scale, 0, 0), y: BioVec(0, scale, 0), z: BioVec(0, 0, scale)).then(turn)
            items.append(BioSceneItem(id: "cell:\(kind.rawValue)", mesh: m.transformed(f),
                                      material: BioMaterial(hex.hex(dark: dark), alpha: alpha, roughness: roughness), order: order))
        }
        func colour(_ k: CEKind, _ id: String) -> BioHexPair { CEParts.part(k, id)?.colour ?? BioHexPair("#8e8e93", "#aeaeb2") }
        // (the bacterium, 2 µm, at x −34)
        let bx = -34.0
        put(.bacterium, BioShapes.radial(rings: 12, segments: 24, radius: CEGeo.capsuleXRadius(0.5, length: 1.0)), colour(.bacterium, "outer"), scale: 1, at: bx)
        put(.bacterium, BioShapes.radial(rings: 12, segments: 24, radius: CEGeo.capsuleXRadius(0.65, length: 1.0)), colour(.bacterium, "capsule"),
            alpha: 0.3, order: 9, scale: 1, at: bx)
        for k in 0..<3 {
            let a = Double(k) * 2 * .pi / 3
            let base = BioVec(-0.9, 0.35 * cos(a), 0.35 * sin(a))
            let pts = (0..<40).map { i -> BioVec in
                let t = Double(i) / 39 * 2 * .pi * 2.6
                return base + BioVec(-t * 2.3 / (2 * .pi), 0.2 * (cos(t) - 1) * cos(a), -0.2 * sin(t))
            }
            put(.bacterium, BioPath.tube(pts, radius: 0.04, sides: 6, samples: 2), colour(.bacterium, "flagellum"), scale: 1, at: bx)
        }
        // (the animal cell, 20 µm across, at x −19)
        let ax = -19.0, aScale = 20.0 / 22
        let blob = BioBlob(seed: CEKind.animal.seed, amplitude: 0.06)
        let ell = BioShapes.ellipsoid(11, 8, 9.5)
        put(.animal, BioShapes.radial(rings: 24, segments: 48, radius: { ell($0) * blob.factor($0) }), colour(.animal, "pm"), alpha: 0.35,
            roughness: 0.25, order: 8, scale: aScale, at: ax)
        put(.animal, BioShapes.sphere(radius: 3.2, rings: 16, segments: 32).translated(BioVec(1.0, 0.5, -0.5)), colour(.animal, "nucleus"),
            scale: aScale, at: ax)
        let animal = CEBuildCache.shared.model(CEBuildOptions(kind: .animal, cutaway: false))
        var mitos = BioMesh()
        let mito = BioShapes.capsule(radius: 0.45, length: 1.3, rings: 6, segments: 12)
        for inst in animal.instances where inst.part == "mito" { if let f = inst.frame { mitos.append(mito.transformed(f)) } }
        put(.animal, mitos, colour(.animal, "mito"), scale: aScale, at: ax)
        // (the plant cell, 40 µm long, at x 15)
        let px = 15.0, pScale = 40.0 / 44
        put(.plant, BioShapes.radial(rings: 24, segments: 48, radius: BioShapes.roundedBoxRadius(BioVec(22, 13, 13), corner: 3)),
            colour(.plant, "wall"), alpha: 0.4, roughness: 0.6, order: 8, scale: pScale, at: px)
        put(.plant, BioShapes.radial(rings: 20, segments: 40, radius: BioShapes.roundedBoxRadius(BioVec(18.5, 9.5, 9.5), corner: 6)),
            colour(.plant, "vacuole"), alpha: 0.35, roughness: 0.2, order: 4, scale: pScale, at: px)
        put(.plant, BioShapes.sphere(radius: 3.0, rings: 16, segments: 32).translated(BioVec(-9, 9.2, 0)), colour(.plant, "nucleus"),
            scale: pScale, at: px)
        let plant = CEBuildCache.shared.model(CEBuildOptions(kind: .plant, cutaway: false))
        var chloro = BioMesh()
        let lens = BioShapes.lens(diameter: 5.0, thickness: 2.4, rings: 6, segments: 12)
        for inst in plant.instances where inst.part == "chloroplast" { if let f = inst.frame { chloro.append(lens.transformed(f)) } }
        put(.plant, chloro, colour(.plant, "chloroplast"), scale: pScale, at: px)
        // (labels over each)
        for (k, x, top, text) in [(CEKind.bacterium, bx, 0.7, "Bacterium · about 2 µm"), (.animal, ax, 8.2 * aScale, "Animal cell · about 20 µm"),
                                  (.plant, px, 13.4 * pScale, "Plant cell · about 40 µm")] {
            labels.append(BioLabel3D(id: "cell:\(k.rawValue)", text: text, anchor: turn.apply(BioVec(x, top, 0)), area: 1))
        }
        return BioSceneContent(items: items, labels: labels, frame: BioSphere(centre: turn.apply(BioVec(0, 0, 0)), radius: 30))
    }
}

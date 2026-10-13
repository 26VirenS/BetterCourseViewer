import Foundation

// The Cell Explorer's Membrane page (spec §4.1.1): a square patch of plasma membrane 60 nm × 60 nm, in nanometres,
// cut on its two near sides to show the bilayer. Phospholipids on a hexagonal grid 0.8 nm apart in each leaflet
// (jittered ±0.08 nm), head centres at ±1.95 nm (a bilayer about 4 nm thick), each a head (r 0.45 nm; phosphatidylcholine
// #e8a33d, the others #e8c13d) and two wavy tails (r 0.22 nm, 1.75 nm long, a sine of 0.12 nm along them), half the
// lipids with one tail kinked 30° at its middle (unsaturated); 12% of the slots cholesterol (a rigid rod 0.5 × 0.2 ×
// 1.6 nm among the tails, its OH bead at the heads' level); six transmembrane proteins, each seven α-helix cylinders
// (r 0.5 nm, 4.5 nm long) round a circle of radius 1.2 nm — one a channel (an aquaporin's open pore), one a carrier (a
// closed bundle that rocks open to one side then the other), two glycoproteins (branched chains of hexagonal sugar beads
// on the outside); three peripheral proteins (blobs r 1.5 nm) on the inner face; four glycolipids (three sugar beads).
// Everything inside the patch is hidden under the heads, so tails and cholesterol rods are drawn only near the cut
// sides, where they show. Foundation only.

/// A part of the membrane, as its card tells it.
struct CEMembranePart: Sendable, Identifiable {
    let id: String
    let name: String
    let colour: BioHexPair
    let function: String
    let structure: String
    var mixup: String? = nil
}

enum CEMembrane {
    /// The patch's seed (SplitMix64): every launch the same patch.
    static let seed: UInt64 = 0x4D45_4D42

    static let pc = BioHexPair("#e8a33d", "#f0b257")
    static let otherHead = BioHexPair("#e8c13d", "#f0cd57")
    static let tail = BioHexPair("#d9cc9f", "#8f8463")
    static let cholesterolColour = BioHexPair("#c4a23a", "#e3c55c")
    static let hydroxyl = BioHexPair("#d63a34", "#ff6961")
    static let integralColour = BioHexPair("#4f7fd6", "#7aa7ff")
    static let channelColour = BioHexPair("#2f9fb8", "#5cc8de")
    static let carrierColour = BioHexPair("#9b6bd1", "#c39cf5")
    static let peripheralColour = BioHexPair("#d4884a", "#f0a868")
    static let sugar = BioHexPair("#3fae5f", "#6fd88f")
    static let water = BioHexPair("#3b82f6", "#6ea8ff")

    /// The parts with labels (§4.1.1's twelve), in the parts list's order.
    static let parts: [CEMembranePart] = [
        CEMembranePart(id: "bilayer", name: "Phospholipid bilayer", colour: pc,
                       function: "Two layers of phospholipids, heads outward and tails inward, form the barrier: small nonpolar molecules (O₂, CO₂) slip through, while ions and large polar molecules need proteins to cross.",
                       structure: "About 4–5 nm; up to about 10 nm counting the proteins that stick out.",
                       mixup: "It is fluid, not fixed: lipids and many proteins drift sideways (the fluid mosaic model)."),
        CEMembranePart(id: "bilayer:heads", name: "Hydrophilic head (polar)", colour: pc,
                       function: "Each phospholipid's head (a phosphate group, often with choline) is polar, so the heads face the water on both sides: outside the cell and the cytosol.",
                       structure: "Drawn as a bead 0.9 nm across; phosphatidylcholine darker, other phospholipids lighter."),
        CEMembranePart(id: "bilayer:tails", name: "Hydrophobic tails (nonpolar)", colour: tail,
                       function: "Two fatty-acid tails per phospholipid, nonpolar, so they hide from water in the middle of the bilayer. A kink from a cis double bond (an unsaturated tail) stops them packing tightly and keeps the membrane fluid.",
                       structure: "About 1.75 nm long; the hydrophobic core is about 3 nm thick.",
                       mixup: "Tails point inward and heads outward in both layers, never the other way round."),
        CEMembranePart(id: "cholesterol", name: "Cholesterol", colour: cholesterolColour,
                       function: "A rigid ring-shaped lipid tucked among the tails, its OH at the heads' level. It steadies fluidity: less fluid when warm, and stops the tails packing solid when cold.",
                       structure: "In animal membranes (plants have similar sterols). Here 12% of the lipid places.",
                       mixup: "Cholesterol sits inside the bilayer, among the tails, not on its surface."),
        CEMembranePart(id: "integral", name: "Integral (transmembrane) protein", colour: integralColour,
                       function: "Spans the whole bilayer. Its middle is nonpolar (it sits among the tails) and its ends are polar. Integral proteins work as channels, carriers, receptors and enzymes.",
                       structure: "Many cross the bilayer as a bundle of α-helices; here seven helices, each 4.5 nm long."),
        CEMembranePart(id: "integral:channel", name: "Channel protein", colour: channelColour,
                       function: "A pore that lets one kind of small molecule or ion through by facilitated diffusion, down its concentration gradient, with no energy used. Aquaporins like this one let water through: osmosis.",
                       structure: "Helices round an open centre.",
                       mixup: "A channel does not change shape to pass each molecule; a carrier does."),
        CEMembranePart(id: "integral:carrier", name: "Carrier protein", colour: carrierColour,
                       function: "Binds a particular molecule and changes shape to carry it across, open to one side then the other. Passive carriers move it down its gradient; pumps use ATP to move it against (active transport).",
                       structure: "A closed bundle that alternates its opening."),
        CEMembranePart(id: "peripheral", name: "Peripheral protein", colour: peripheralColour,
                       function: "Sits on one face of the membrane (here the cytoplasm side), held by the heads or by integral proteins; often an enzyme, or an anchor for the cytoskeleton.",
                       structure: "Does not enter the hydrophobic core."),
        CEMembranePart(id: "glycoprotein", name: "Glycoprotein", colour: sugar,
                       function: "A membrane protein with a chain of sugars on the outside face: cell recognition (self and non-self), signalling and cells sticking together.",
                       structure: "Branched chains of sugar rings on the extracellular side only."),
        CEMembranePart(id: "glycolipid", name: "Glycolipid", colour: sugar,
                       function: "A lipid with a short sugar chain on the outside face: cell recognition, like the A and B blood-group markers.",
                       structure: "Its sugars face outside the cell."),
        CEMembranePart(id: "side:outside", name: "Extracellular side", colour: BioHexPair("#8e8e93", "#aeaeb2"),
                       function: "Outside the cell. The sugar chains of glycoproteins and glycolipids face this side.",
                       structure: "Watery: the heads face it."),
        CEMembranePart(id: "side:inside", name: "Cytoplasm side", colour: BioHexPair("#8e8e93", "#aeaeb2"),
                       function: "Inside the cell, the cytosol. Peripheral proteins and the cytoskeleton attach on this side.",
                       structure: "Watery: the heads face it too."),
    ]

    static func part(_ id: String) -> CEMembranePart? { parts.first { $0.id == id } }

    /// The patch's turn for the view (its two cut sides, +x and +z, toward the camera; seen from a little above).
    static let display = BioFrame.rotation(axis: BioVec(0, 1, 0), degrees: -30).then(BioFrame.rotation(axis: BioVec(1, 0, 0), degrees: 26))

    /// The integral proteins' places in the patch (x, z in nm): the channel, the carrier, two glycoproteins, two more.
    static let proteins: [(role: String, x: Double, z: Double)] = [
        ("channel", 11, 22), ("carrier", -13, 20), ("glyco", -6, -9), ("glyco", 17, -13), ("plain", -21, -19), ("plain", 3, 5),
    ]
    static let peripheralPlaces: [(Double, Double)] = [(5, 26.5), (-19, 2), (15, -16)]
    static let glycolipidPlaces: [(Double, Double)] = [(-22, 5), (6, -21), (23, 7), (-1, 25.5)]
    static let half = 30.0
    static let headY = 1.95

    /// The built patch.
    struct Model: Sendable {
        var content = BioSceneContent()
        var lipids = 0
        var cholesterol = 0
        var tails = 0
        var triangles = 0
        /// The carrier's helices for the animation: each one's centre and its turn in the two states (open outside,
        /// open inside) as axis–angle, in the scene's frame.
        var carrier: [(centre: BioVec, outside: (axis: BioVec, angle: Double), inside: (axis: BioVec, angle: Double))] = []
        /// Where water beads enter and leave the channel; the carrier's solute path; the osmosis arrow (scene frame).
        var channelPath: (BioVec, BioVec) = (BioVec(0, 0, 0), BioVec(0, 0, 0))
        var carrierPath: [BioVec] = []
        var arrow: (BioVec, BioVec) = (BioVec(0, 0, 0), BioVec(0, 0, 0))
        /// The scene's up (the membrane's normal), for the beads' and arrow's orientation.
        var up = BioVec(0, 1, 0)
    }

    /// The patch, with the parts shown (`show`: "cholesterol", "glyco", "proteins") and, while the carrier is animated
    /// (as SceneKit nodes in the view), the carrier left out of the batched scene.
    static func build(dark: Bool, cholesterol showChol: Bool = true, glyco showGlyco: Bool = true, proteins showProteins: Bool = true,
                      carrierAnimated: Bool = false) -> Model {
        var rng = BioRandom(seed: seed)
        var out = Model()
        var items: [BioSceneItem] = []
        let d = display
        func put(_ id: String, _ m: BioMesh, _ c: BioHexPair, alpha: Double = 1, roughness: Double = 0.45, order: Int = 0) {
            guard !m.isEmpty else { return }
            items.append(BioSceneItem(id: id, mesh: m.transformed(d), material: BioMaterial(c.hex(dark: dark), alpha: alpha, roughness: roughness), order: order))
        }
        let edge = 1.7
        func nearCut(_ x: Double, _ z: Double) -> Bool { x > half - edge || z > half - edge }
        let head = BioShapes.icosphere(radius: 0.45, subdivisions: 0)
        let bead = BioShapes.icosphere(radius: 0.15, subdivisions: 0)
        var pcHeads = BioMesh(), otherHeads = BioMesh(), tails = BioMesh(), cholRods = BioMesh(), cholOH = BioMesh()
        var glycoLipids: [BioMesh] = Array(repeating: BioMesh(), count: glycolipidPlaces.count)
        var glycoSugar: [BioMesh] = Array(repeating: BioMesh(), count: glycolipidPlaces.count)
        // (the glycolipids: the top leaflet's slots nearest their places)
        var glycoSlot: [Int: Int] = [:]
        let rowStep = 0.8 * 3.0.squareRoot() / 2
        for (leaflet, sign) in [(0, 1.0), (1, -1.0)] {
            var j = 0
            var z = -half + 0.4
            while z < half - 0.2 {
                var x = -half + 0.4 + (j % 2 == 1 ? 0.4 : 0)
                while x < half - 0.2 {
                    let jx = rng.uniform(-0.08, 0.08), jz = rng.uniform(-0.08, 0.08)
                    let roll = rng.uniform()
                    let isPC = rng.uniform() < 0.5
                    let kinked = rng.uniform() < 0.5
                    let azimuth = rng.uniform(0, 2 * .pi)
                    let px = x + jx, pz = z + jz
                    defer { x += 0.8 }
                    // (no lipid where a transmembrane protein sits)
                    if proteins.contains(where: { hypot($0.x - px, $0.z - pz) < 2.3 }) { continue }
                    let y = headY * sign
                    if roll < 0.12 {
                        // (cholesterol: its OH bead at the heads' level, its rod among the tails)
                        out.cholesterol += 1
                        if showChol {
                            cholOH.append(bead.translated(BioVec(px, sign * 1.72, pz)))
                            if nearCut(px, pz) {
                                let rot = BioFrame.rotation(axis: BioVec(0, 1, 0), degrees: azimuth * 180 / .pi, at: BioVec(px, sign * (1.72 - 0.95), pz))
                                cholRods.append(CEGeo.box(half: BioVec(0.25, 0.8, 0.1)).transformed(rot))
                            }
                        }
                        continue
                    }
                    out.lipids += 1
                    let h = head.translated(BioVec(px, y, pz))
                    // (a glycolipid in the top leaflet, at the slot nearest each of its places)
                    var glyco: Int?
                    if leaflet == 0 {
                        for (k, g) in glycolipidPlaces.enumerated() where hypot(g.0 - px, g.1 - pz) < 0.5 && glycoSlot[k] == nil {
                            glyco = k
                            glycoSlot[k] = 1
                        }
                    }
                    if let g = glyco {
                        glycoLipids[g].append(h)
                        for s in 0..<3 {
                            let c = BioVec(px + 0.15 * Double(s % 2), y + 0.45 + 0.35 + 0.62 * Double(s), pz)
                            glycoSugar[g].append(hexagon(at: c, axis: BioVec(1, 0.2 * Double(s), 0.3)))
                        }
                    } else if isPC { pcHeads.append(h) } else { otherHeads.append(h) }
                    if nearCut(px, pz) || glyco != nil {
                        out.tails += 2
                        let t = lipidTails(at: BioVec(px, y, pz), sign: sign, azimuth: azimuth, kinked: kinked)
                        if let g = glyco { glycoLipids[g].append(t) } else { tails.append(t) }
                    }
                }
                z += rowStep
                j += 1
            }
        }
        put("part:bilayer:heads:pc", pcHeads, pc, roughness: 0.4)
        put("part:bilayer:heads:other", otherHeads, otherHead, roughness: 0.4)
        put("part:bilayer:tails", tails, tail, roughness: 0.55)
        if showChol {
            put("part:cholesterol", cholRods, cholesterolColour, roughness: 0.4)
            put("part:cholesterol", cholOH, hydroxyl, roughness: 0.4)
        }
        for k in glycolipidPlaces.indices {
            put("part:glycolipid:\(k)", glycoLipids[k], otherHead, roughness: 0.4)
            if showGlyco { put("part:glycolipid:\(k)", glycoSugar[k], sugar, roughness: 0.45) }
        }
        // (the transmembrane proteins: seven helices round a circle r 1.2 nm)
        var glycoIndex = 0
        for (k, p) in proteins.enumerated() {
            let c = BioVec(p.x, 0, p.z)
            let id: String, colour: BioHexPair
            switch p.role {
            case "channel": (id, colour) = ("part:integral:channel", channelColour)
            case "carrier": (id, colour) = ("part:integral:carrier", carrierColour)
            default: (id, colour) = ("part:integral:protein:\(k)", integralColour)
            }
            var bundle = BioMesh()
            for h in 0..<7 {
                let a = 2 * Double.pi * Double(h) / 7
                let at = c + BioVec(1.2 * cos(a), 0, 1.2 * sin(a))
                var helix = BioShapes.cylinder(from: BioVec(0, -2.25, 0), to: BioVec(0, 2.25, 0), radius: 0.5, segments: 12)
                // (a closed protein's helices lean in a little; the channel's stand straight round its pore; the carrier's
                // splay open toward the outside)
                let tilt: Double = p.role == "channel" ? 0 : (p.role == "carrier" ? 12 : 6)
                let tangent = BioVec(-sin(a), 0, cos(a))
                let sideways = p.role == "carrier" ? -tilt : tilt
                helix = helix.transformed(BioFrame.rotation(axis: tangent, degrees: sideways, at: at))
                bundle.append(helix)
                if p.role == "carrier" {
                    let outside = turn(axis: tangent, degrees: -tilt)
                    let inside = turn(axis: tangent, degrees: tilt)
                    out.carrier.append((d.apply(at), outside, inside))
                }
            }
            if p.role == "carrier" && carrierAnimated { continue }
            if p.role == "channel" {
                // (the pore's mouths: a ring at each face, standing out of the heads)
                let ring = BioShapes.torus(ring: 1.25, pipe: 0.5, ringSegments: 28, pipeSegments: 10)
                bundle.append(ring.translated(c + BioVec(0, 2.75, 0)))
                bundle.append(ring.translated(c + BioVec(0, -2.75, 0)))
            } else if p.role != "carrier" {
                // (a closed protein's loops and domains, outside and inside: they stick out past the heads)
                let cap = BioShapes.radial(rings: 12, segments: 24, radius: BioShapes.blob(BioShapes.ellipsoid(1.9, 1.0, 1.9),
                                                                                          BioBlob(seed: seed &+ UInt64(k), amplitude: 0.1)))
                bundle.append(cap.translated(c + BioVec(0, 2.95, 0)))
                bundle.append(cap.translated(c + BioVec(0, -2.95, 0)))
            }
            if showProteins || p.role == "channel" || p.role == "carrier" {
                put(id, bundle, colour, roughness: 0.4)
            }
            if p.role == "glyco" {
                if showGlyco { put("part:glycoprotein:\(glycoIndex)", glycoChain(from: c + BioVec(0.4, 4.1, 0.2), rng: &rng), sugar, roughness: 0.45) }
                glycoIndex += 1
            }
        }
        // (peripheral proteins: blobs r 1.5 nm touching the inner face)
        for (k, p) in peripheralPlaces.enumerated() {
            let m = BioShapes.radial(rings: 16, segments: 32, radius: BioShapes.blob(BioShapes.sphere(1.5), BioBlob(seed: seed &+ 10 &+ UInt64(k), amplitude: 0.1)))
            put("part:peripheral:\(k)", m.translated(BioVec(p.0, -headY - 0.45 - 1.45, p.1)), peripheralColour, roughness: 0.5)
        }
        // (the paths the view animates: water down the channel, the carrier's solute, the osmosis arrow)
        let ch = proteins[0], ca = proteins[1]
        out.channelPath = (d.apply(BioVec(ch.x, 4.2, ch.z)), d.apply(BioVec(ch.x, -4.2, ch.z)))
        out.carrierPath = [BioVec(ca.x, 4.5, ca.z), BioVec(ca.x, 0.4, ca.z), BioVec(ca.x, -0.4, ca.z), BioVec(ca.x, -4.5, ca.z)].map { d.apply($0) }
        out.arrow = (d.apply(BioVec(ch.x, 5.5, half + 1.2)), d.apply(BioVec(ch.x, -5.5, half + 1.2)))
        out.up = d.applyVector(BioVec(0, 1, 0))
        out.triangles = items.reduce(0) { $0 + $1.mesh.triangleCount }
        out.content = BioSceneContent(items: items, labels: labels(), frame: BioSphere(centre: BioVec(0, 0, 0), radius: 29))
        return out
    }

    /// What the view animates, without building the patch: the carrier's helices (each one's centre and its turn open
    /// outside and open inside), the water's path down the channel, the carrier's solute path, the osmosis arrow.
    struct Motion: Sendable {
        var carrier: [(centre: BioVec, outside: (axis: BioVec, angle: Double), inside: (axis: BioVec, angle: Double))] = []
        var channelPath: (BioVec, BioVec)
        var carrierPath: [BioVec]
        var arrow: (BioVec, BioVec)
        /// The membrane's normal in the scene (extracellular side up).
        var up: BioVec
        /// A helix's mesh about its centre, along y (before its turn).
        var helix: BioMesh
    }

    static func motion() -> Motion {
        let d = display
        let ch = proteins[0], ca = proteins[1]
        var m = Motion(channelPath: (d.apply(BioVec(ch.x, 4.2, ch.z)), d.apply(BioVec(ch.x, -4.2, ch.z))),
                       carrierPath: [BioVec(ca.x, 4.5, ca.z), BioVec(ca.x, 0.4, ca.z), BioVec(ca.x, -0.4, ca.z), BioVec(ca.x, -4.5, ca.z)].map { d.apply($0) },
                       arrow: (d.apply(BioVec(ch.x, 5.5, half + 1.2)), d.apply(BioVec(ch.x, -5.5, half + 1.2))),
                       up: d.applyVector(BioVec(0, 1, 0)),
                       helix: BioShapes.cylinder(from: BioVec(0, -2.25, 0), to: BioVec(0, 2.25, 0), radius: 0.5, segments: 12))
        for h in 0..<7 {
            let a = 2 * Double.pi * Double(h) / 7
            let tangent = BioVec(-sin(a), 0, cos(a))
            m.carrier.append((d.apply(BioVec(ca.x, 0, ca.z) + BioVec(1.2 * cos(a), 0, 1.2 * sin(a))),
                              turn(axis: tangent, degrees: -12), turn(axis: tangent, degrees: 12)))
        }
        return m
    }

    /// The axis–angle of the patch's turn after a helix's tilt (for the carrier's animation).
    static func turn(axis: BioVec, degrees: Double) -> (axis: BioVec, angle: Double) {
        let q = quaternion(display).times(BioQuat(axis: axis, degrees: degrees))
        return q.axisAngle
    }

    /// Two tails hanging from a head toward the bilayer's middle, wavy, one kinked 30° at its middle if asked.
    static func lipidTails(at h: BioVec, sign: Double, azimuth: Double, kinked: Bool) -> BioMesh {
        var m = BioMesh()
        let side = BioVec(cos(azimuth), 0, sin(azimuth))
        let across = BioVec(-sin(azimuth), 0, cos(azimuth))
        for (k, s) in [-1.0, 1.0].enumerated() {
            let top = h + side * (0.22 * s) - BioVec(0, sign * 0.4, 0)
            var dir = BioVec(0, -sign, 0)
            var pts: [BioVec] = []
            var p = top
            let n = 8
            for i in 0..<n {
                let t = Double(i) / Double(n - 1)
                if kinked && k == 1 && i == n / 2 { dir = BioV.rotate(dir, axis: across, degrees: 30 * s) }
                pts.append(p + across * (0.12 * sin(2 * .pi * 1.5 * t)))
                p = p + dir * (1.75 / Double(n - 1))
            }
            m.append(BioPath.tube(pts, radius: 0.22, sides: 5, samples: 1))
        }
        return m
    }

    /// A sugar ring: a flat hexagonal bead (r 0.35 nm).
    static func hexagon(at c: BioVec, axis: BioVec) -> BioMesh {
        BioShapes.cylinder(from: c - BioV.unit(axis) * 0.12, to: c + BioV.unit(axis) * 0.12, radius: 0.35, segments: 6)
    }

    /// A glycoprotein's sugar chain: 8 hexagon beads, a branch of 3 off the second.
    static func glycoChain(from base: BioVec, rng: inout BioRandom) -> BioMesh {
        var m = BioMesh()
        var p = base
        var dir = BioVec(0, 1, 0)
        var branchFrom = base
        for i in 0..<5 {
            m.append(hexagon(at: p, axis: CEGeo.cone(BioVec(1, 0, 0), 40, &rng)))
            if i == 1 { branchFrom = p }
            dir = CEGeo.cone(BioVec(0, 1, 0), 25, &rng)
            p = p + dir * 0.7
        }
        p = branchFrom + BioV.unit(BioVec(0.8, 0.6, 0.2)) * 0.7
        for _ in 0..<3 {
            m.append(hexagon(at: p, axis: CEGeo.cone(BioVec(0, 0, 1), 40, &rng)))
            p = p + BioV.unit(BioVec(0.7, 0.7, 0.1)) * 0.7
        }
        return m
    }

    /// The twelve labels (§4.1.1), at points the camera sees: the cut faces for the bilayer, a head and the tails; the
    /// proteins' tops; the sides in front of the near cut face.
    static func labels() -> [BioLabel3D] {
        let d = display
        func l(_ id: String, _ text: String, _ p: BioVec, _ area: Double, normal: BioVec? = nil) -> BioLabel3D {
            BioLabel3D(id: id, text: text, anchor: d.apply(p), normal: normal.map { d.applyVector($0) }, area: area)
        }
        let ch = proteins[0], ca = proteins[1], g = proteins[2], plain = proteins[5]
        return [
            l("part:bilayer", "Phospholipid bilayer", BioVec(-4, 0.2, half - 0.6), 12, normal: BioVec(0, 0, 1)),
            l("part:bilayer:heads", "Hydrophilic head (polar)", BioVec(-16, headY, half - 0.5), 11, normal: BioVec(0, 0.4, 1)),
            l("part:bilayer:tails", "Hydrophobic tails (nonpolar)", BioVec(half - 0.5, -0.9, -6), 10, normal: BioVec(1, 0, 0)),
            l("part:cholesterol", "Cholesterol", BioVec(half - 0.4, 1.0, 12), 7, normal: BioVec(1, 0, 0)),
            l("part:integral", "Integral (transmembrane) protein", BioVec(plain.x, 3.9, plain.z), 9),
            l("part:integral:channel", "Channel protein", BioVec(ch.x, 3.2, ch.z + 1.25), 9),
            l("part:integral:carrier", "Carrier protein", BioVec(ca.x, 2.4, ca.z), 9),
            l("part:peripheral", "Peripheral protein", BioVec(peripheralPlaces[0].0, -headY - 1.9, peripheralPlaces[0].1 + 1.4), 8),
            l("part:glycoprotein", "Glycoprotein", BioVec(g.x + 0.4, 5.6, g.z + 0.2), 8),
            l("part:glycolipid", "Glycolipid", BioVec(glycolipidPlaces[3].0, headY + 2.0, glycolipidPlaces[3].1), 7),
            l("side:outside", "Extracellular side", BioVec(-22, 6.5, half + 1), 6),
            l("side:inside", "Cytoplasm side", BioVec(-22, -7.5, half + 1), 6),
        ]
    }
}

// MARK: - Quaternions (the carrier's turns, as SceneKit takes them)

/// A unit quaternion (w, x, y, z).
struct BioQuat: Sendable {
    var w, x, y, z: Double

    init(w: Double, x: Double, y: Double, z: Double) {
        self.w = w
        self.x = x
        self.y = y
        self.z = z
    }

    init(axis: BioVec, degrees: Double) {
        let u = BioV.unit(axis), h = degrees * .pi / 360
        self.init(w: cos(h), x: u.x * sin(h), y: u.y * sin(h), z: u.z * sin(h))
    }

    func times(_ q: BioQuat) -> BioQuat {
        BioQuat(w: w * q.w - x * q.x - y * q.y - z * q.z,
                x: w * q.x + x * q.w + y * q.z - z * q.y,
                y: w * q.y - x * q.z + y * q.w + z * q.x,
                z: w * q.z + x * q.y - y * q.x + z * q.w)
    }

    func rotate(_ v: BioVec) -> BioVec {
        let q = BioVec(x, y, z)
        let t = BioV.cross(q, v) * 2
        return v + t * w + BioV.cross(q, t)
    }

    /// Its axis and angle (radians).
    var axisAngle: (axis: BioVec, angle: Double) {
        let ww = min(max(w, -1), 1)
        let angle = 2 * acos(ww)
        let s = (1 - ww * ww).squareRoot()
        return s < 1e-9 ? (BioVec(0, 1, 0), 0) : (BioVec(x / s, y / s, z / s), angle)
    }
}

/// A rotation frame's quaternion (its axes the turned x, y, z).
func quaternion(_ f: BioFrame) -> BioQuat {
    let m00 = f.x.x, m11 = f.y.y, m22 = f.z.z
    let m01 = f.y.x, m02 = f.z.x, m10 = f.x.y, m12 = f.z.y, m20 = f.x.z, m21 = f.y.z
    let tr = m00 + m11 + m22
    if tr > 0 {
        let s = (tr + 1).squareRoot() * 2
        return BioQuat(w: s / 4, x: (m21 - m12) / s, y: (m02 - m20) / s, z: (m10 - m01) / s)
    } else if m00 > m11 && m00 > m22 {
        let s = (1 + m00 - m11 - m22).squareRoot() * 2
        return BioQuat(w: (m21 - m12) / s, x: s / 4, y: (m01 + m10) / s, z: (m02 + m20) / s)
    } else if m11 > m22 {
        let s = (1 + m11 - m00 - m22).squareRoot() * 2
        return BioQuat(w: (m02 - m20) / s, x: (m01 + m10) / s, y: s / 4, z: (m12 + m21) / s)
    } else {
        let s = (1 + m22 - m00 - m11).squareRoot() * 2
        return BioQuat(w: (m10 - m01) / s, x: (m02 + m20) / s, y: (m12 + m21) / s, z: s / 4)
    }
}

import Foundation

// The three cells, recipe by recipe (spec §5.1.1–5.1.3, §5.1.5), in the order their SplitMix64 draws are made; where
// each part's label points (§3.5: an instance the camera can see — through the translucent membrane, or the cutaway);
// and Look inside. Foundation only; see CellExplorerBuild.swift for the placement rule.

extension CEBuilder {
    // MARK: Animal (§5.1.1)

    mutating func buildAnimal() {
        let R = 3.2, N = BioVec(1.0, 0.5, -0.5)
        let blob = BioBlob(seed: o.kind.seed, amplitude: 0.06)
        let ell = BioShapes.ellipsoid(11, 8, 9.5)
        let pm: (BioVec) -> Double = { u in ell(u) * blob.factor(u) }
        let pmIn: (BioVec) -> Double = { u in pm(u) - 0.08 }
        inside = { p, m in
            let d = BioV.length(p)
            return d < 1e-9 || d <= pmIn(p / d) - m
        }
        membraneInner = pmIn
        surface = { p in
            let d = BioV.length(p)
            return d < 1e-9 ? -1 : d - pm(p / d)
        }
        box = BioVec(11, 8, 9.5) * 1.07
        margin = 0.4
        model.cellRadius = pow(11 * 8 * 9.5, 1.0 / 3)
        model.frameRadius = 11.6
        let av = CEGeo.areaVolume(BioShapes.radial(rings: 48, segments: 96, radius: pm))
        (model.area, model.volume) = (av.area, av.volume)

        // (fixed placements: the Golgi side g, then the membrane, nucleus, rough ER, Golgi and centrosome)
        let g = rng.unitVector()
        let u = CEGeo.perpendicular(g)
        cellShell("pm", outer: pm, inner: pmIn, rings: 40, segments: 80, order: 8)
        nucleus(N, R)
        roughER(sheets: 5, golgi: g)
        golgiStack(at: N + g * (R + 2.4), axis: g, side: u)
        let M = centrosome(at: N + BioV.unit(g + u * 0.6) * (R + 1.1))
        // (then the free organelles, in §5.1.1's order)
        smoothER(sheets: 5, golgi: g)
        mitochondria(24)
        lysosomes(12)
        peroxisomes(8)
        freeVesicles(30)
        freeRibosomes(1500)
        microtubules(from: M, count: 40)
        actin(220, depth: 0.15...0.35)
        intermediateFilaments(30)
        animalFlagellum(pm: pm)
        trueScaleRibosomes()
        if cut {
            var s = CEShape()
            s.add(CERecipes.cutFaces(outer: pmIn, inner: nil), alpha: 0.35, roughness: 0.8, order: 5)
            add("cytoplasm", s, centre: BioVec(0, 0, 0), radius: 11, shell: true)
        } else {
            skip("cytoplasm")
        }
    }

    /// A flagellum (§5.1.1, Nice; off until ticked): 10 µm long, 0.25 µm across, beating in a plane, from a basal
    /// body under the membrane.
    mutating func animalFlagellum(pm: @escaping (BioVec) -> Double) {
        let d0 = BioV.unit(BioVec(-1, -0.25, 0.35))
        let base = d0 * pm(d0)
        let n = CEGeo.gradient(surface, at: base)
        let e = CEGeo.perpendicular(n)
        let pts = (0..<44).map { k -> BioVec in
            let s = 10 * Double(k) / 43
            return base - n * 0.05 + n * s + e * (0.9 * CEGeo.smoothstep(0, 3, s) * sin(2 * .pi * s / 4))
        }
        var sh = CEShape()
        sh.add(BioPath.tube(pts, radius: 0.125, sides: 10, samples: 2), roughness: 0.45)
        sh.add(BioShapes.cylinder(from: base - n * 0.55, to: base - n * 0.1, radius: 0.1, segments: 12), .dark, roughness: 0.45)
        add("flagellum", sh, centre: base + n * 5, radius: 5.2, fixed: true)
    }

    // MARK: Plant (§5.1.2)

    mutating func buildPlant() {
        let R = 3.0, N = BioVec(-9, 9.2, 0)
        let half = BioVec(22, 13, 13)
        let sdf: (BioVec) -> Double = { CEGeo.roundedBoxSDF($0, half: half, corner: 3) }
        let wallOut = BioShapes.roundedBoxRadius(half, corner: 3)
        let wallIn = BioShapes.roundedBoxRadius(half, corner: 3, level: -0.5)
        let pmOut = BioShapes.roundedBoxRadius(half, corner: 3, level: -0.52)
        let pmIn = BioShapes.roundedBoxRadius(half, corner: 3, level: -0.60)
        let vacBox = BioShapes.roundedBoxRadius(BioVec(18.5, 9.5, 9.5), corner: 6)
        let vacBlob = BioBlob(seed: o.kind.seed, amplitude: 0.03)
        let vac: (BioVec) -> Double = { u in vacBox(u) * vacBlob.factor(u) }
        inside = { p, m in sdf(p) <= -0.60 - m }
        outsideVacuole = { p, m in
            if BioV.distance(p, N) < R + 0.4 { return true }
            let d = BioV.length(p)
            return d < 1e-9 ? false : d >= vac(p / d) + m
        }
        membraneInner = pmIn
        surface = sdf
        box = half
        margin = 0.3
        model.cellRadius = pow(22 * 13 * 13, 1.0 / 3)
        model.frameRadius = 22.5
        let av = CEGeo.areaVolume(BioShapes.radial(rings: 48, segments: 96, radius: pmOut))
        (model.area, model.volume) = (av.area, av.volume)

        _ = rng.unitVector()  // (the "Golgi side" g: drawn as in every cell; the plant's stacks are free organelles)
        // (the wall and the membrane meshed on the box's own faces, so the rounded edges run along the grid; the
        // membrane's outer face, hidden behind the opaque wall, left out)
        let n = 32
        var wall = CEShape()
        wall.add(CERecipes.boxSurface(half: half, n: n, cut: cut, radius: wallOut),
                 .colour(CEGeo.darker(parts["wall"]?.colour ?? BioHexPair("#8fae5a", "#b3d17c"), 0.15)), roughness: 0.7)
        wall.add(CERecipes.boxSurface(half: half, n: n, cut: cut, radius: wallIn, inward: true), roughness: 0.6)
        if cut { wall.add(CERecipes.boxCaps(half: half, n: n, outer: wallOut, inner: wallIn), .cap, alpha: 1, roughness: 0.6) }
        add("wall", wall, centre: BioVec(0, 0, 0), radius: 28, shell: true)
        var membrane = CEShape()
        membrane.add(CERecipes.boxSurface(half: half, n: n, cut: cut, radius: pmIn, inward: true), roughness: 0.25, order: 8, round: true)
        if cut { membrane.add(CERecipes.boxCaps(half: half, n: n, outer: pmOut, inner: pmIn), .cap, alpha: 1, roughness: 0.6, order: 8) }
        add("pm", membrane, centre: BioVec(0, 0, 0), radius: 28, shell: true)
        // (the central vacuole first: a rounded box with a blob's ripple, dented by the nucleus — each tonoplast point
        // nearer than R + 0.4 µm to the nucleus's centre moved out to that distance, facing the nucleus)
        var v = CERecipes.boxSurface(half: BioVec(18.5, 9.5, 9.5), n: 24, cut: false, radius: vac)
        for i in 0..<v.positions.count {
            let p = CEGeo.vec(v.positions[i])
            if BioV.distance(p, N) < R + 0.4 {
                let q = N + BioV.unit(p - N) * (R + 0.4)
                let nrm = BioV.unit(N - q)
                v.positions[i] = SIMD3<Float>(Float(q.x), Float(q.y), Float(q.z))
                v.normals[i] = SIMD3<Float>(Float(nrm.x), Float(nrm.y), Float(nrm.z))
            }
        }
        var vs = CEShape()
        vs.add(v, roughness: 0.2, order: 4)
        add("vacuole", vs, centre: BioVec(0, 0, 0), radius: 19)
        // (then the nucleus against the wall, and the rest)
        nucleus(N, R)
        roughER(sheets: 3, golgi: nil)
        plasmodesmata(30, half: half)
        chloroplasts(40)
        mitochondria(18)
        smoothER(sheets: 3, golgi: nil)
        dictyosomes()
        peroxisomes(6)
        // (§5.1.2's 1,200 ribosomes: 300 on the rough ER's three sheets, 900 free)
        freeRibosomes(900)
        corticalMicrotubules(30, half: half)
        actin(120, depth: 0.3...1.5)
        amyloplasts(4)
        trueScaleRibosomes()
        if cut {
            var s = CEShape()
            s.add(CERecipes.cutFaces(outer: pmIn, inner: vac), alpha: 0.35, roughness: 0.8, order: 5)
            add("cytoplasm", s, centre: BioVec(0, 0, 0), radius: 20, shell: true)
        } else {
            skip("cytoplasm")
        }
    }

    /// Plasmodesmata: 30 channels (r 0.06 µm) through the wall, at seeded points on its mid-surface, along its normal.
    mutating func plasmodesmata(_ count: Int, half: BioVec) {
        let mid = BioShapes.roundedBoxRadius(half, corner: 3, level: -0.25)
        for _ in 0..<count {
            let u = rng.unitVector()
            let p = u * mid(u)
            let n = CEGeo.gradient({ CEGeo.roundedBoxSDF($0, half: half, corner: 3) }, at: p)
            if hidden(p) { skip("plasmodesma"); continue }
            var s = CEShape()
            s.add(BioShapes.cylinder(from: p - n * 0.36, to: p + n * 0.36, radius: 0.06, segments: 10), roughness: 0.5)
            add("plasmodesma", s, centre: p + n * 0.36, radius: 0.36, fixed: true)
        }
    }

    /// Chloroplasts (§5.1.2): 40 on the cytoplasm layer's mid-surface (a rounded box 20 × 11 × 11, corner 4.5), at
    /// least 5.2 µm apart and 6.0 µm from the nucleus, their thin axis along that surface's normal (two draws for the
    /// point; one for the turn in its plane once kept).
    mutating func chloroplasts(_ count: Int) {
        let mh = BioVec(20, 11, 11)
        let mid = BioShapes.roundedBoxRadius(mh, corner: 4.5)
        var centres: [BioVec] = []
        for _ in 0..<count {
            var found: (BioVec, BioVec)?
            for _ in 0..<200 {
                let u = rng.unitVector()
                let p = u * mid(u)
                if BioV.distance(p, nucleusCentre) < 6.0 { continue }
                if centres.contains(where: { BioV.distance($0, p) < 5.2 }) { continue }
                found = (p, CEGeo.gradient({ CEGeo.roundedBoxSDF($0, half: mh, corner: 4.5) }, at: p))
                break
            }
            guard let (p, n) = found else { model.gaveUp["chloroplast", default: 0] += 1; continue }
            centres.append(p)
            placer.add(p, 2.5, blocksSER: false)
            let angle = rng.uniform(0, 360)
            let x = BioV.rotate(CEGeo.perpendicular(n), axis: n, degrees: angle)
            let f = BioFrame(origin: p, x: x, y: n, z: BioV.cross(x, n))
            lenses.append(f)
            if hidden(p) { skip("chloroplast"); continue }
            let half = crossing(p, 2.5)
            let frame = half ? CEGeo.frame(y: n, towardZ: cameraDirection, at: p) : f
            add("chloroplast", CERecipes.chloroplast(detail: half, half: half, ribosomeRadius: o.trueScale ? 0.01 : 0.02).placed(frame),
                centre: p, radius: 2.5, frame: f)
        }
    }

    /// Cortical microtubules (§5.1.2): 30 hoops round the long axis, just inside the membrane — each at a position along
    /// it (one draw), from an angle (one) round an arc (one).
    mutating func corticalMicrotubules(_ count: Int, half: BioVec) {
        let r = filament("microtubule", teaching: 0.05)
        for k in 0..<count {
            let x = -20 + 40 * (Double(k) + 0.5) / Double(count) + rng.uniform(-0.4, 0.4)
            let a0 = rng.uniform(0, 2 * .pi)
            let span = rng.uniform(0.5 * .pi, 1.5 * .pi)
            let pts = (0..<28).map { i -> BioVec in
                let a = a0 + span * Double(i) / 27
                var lo = 0.0, hi = 15.0
                for _ in 0..<40 {
                    let mid = (lo + hi) / 2
                    if CEGeo.roundedBoxSDF(BioVec(x, mid * cos(a), mid * sin(a)), half: half, corner: 3) < -0.75 { lo = mid } else { hi = mid }
                }
                return BioVec(x, lo * cos(a), lo * sin(a))
            }
            filamentTube("microtubule", pts, radius: r)
        }
    }

    /// Amyloplasts (§5.1.2, Nice; off until ticked): starch-storing plastids, two membranes round three starch grains.
    mutating func amyloplasts(_ count: Int) {
        for _ in 0..<count {
            guard let p = placeFree("amyloplast", radius: 1.0) else { continue }
            let rot = rng.rotation()
            if hidden(p) { skip("amyloplast"); continue }
            var s = CEShape()
            s.add(BioShapes.spheroid(1.0, 0.7, 0.7, rings: 12, segments: 24), alpha: 0.45, roughness: 0.3, order: 3)
            s.add(BioShapes.spheroid(0.95, 0.66, 0.66, rings: 10, segments: 20), alpha: 0.4, roughness: 0.3, order: 2)
            for c in [BioVec(-0.45, 0.05, 0), BioVec(0.35, -0.1, 0.15), BioVec(0.1, 0.2, -0.25)] {
                s.add(BioShapes.spheroid(0.38, 0.3, 0.3, rings: 8, segments: 16).translated(c), .colour(BioHexPair("#f8f5ea", "#ece6d2")), roughness: 0.6)
            }
            let f = CEGeo.frame(rot, at: p)
            add("amyloplast", s.placed(f), centre: p, radius: 1.0, frame: f)
        }
    }

    // MARK: Bacterium (§5.1.3)

    mutating func buildBacterium() {
        let gramPositive = o.gramPositive
        let shape = { (r: Double) in CEGeo.capsuleXRadius(r, length: 1.0) }
        let pmR = gramPositive ? (0.43, 0.44) : (0.432, 0.442)
        let wallR = gramPositive ? (0.45, 0.5) : (0.46, 0.48)
        let pmIn = shape(pmR.0)
        inside = { p, m in CEGeo.capsuleXSDF(p, half: 0.5, r: pmR.0) <= -m }
        membraneInner = pmIn
        surface = { CEGeo.capsuleXSDF($0, half: 0.5, r: 0.5) }
        box = BioVec(1.0, 0.5, 0.5)
        margin = 0.01
        model.cellRadius = pow(1.0 * 0.5 * 0.5, 1.0 / 3)
        model.frameRadius = 1.2
        model.area = 2 * Double.pi * 0.5 * 1.0 + 4 * Double.pi * 0.25
        model.volume = Double.pi * 0.25 * 1.0 + 4.0 / 3 * Double.pi * 0.125

        cellShell("capsule", outer: shape(0.65), inner: shape(0.502), rings: 24, segments: 48, order: 9, capAlpha: 0.45)
        if !gramPositive { cellShell("outer", outer: shape(0.5), inner: shape(0.488), rings: 24, segments: 48, order: 0) }
        cellShell("wall", outer: shape(wallR.1), inner: shape(wallR.0), rings: 24, segments: 48, order: 0)
        cellShell("pm", outer: shape(pmR.1), inner: pmIn, rings: 24, segments: 48, order: 0)
        nucleoid()
        let a = 0.65, b = 0.32
        let outsideNucleoid = { (p: BioVec, m: Double) -> Bool in
            let x = p.x / (a + m), y = p.y / (b + m), z = p.z / (b + m)
            return x * x + y * y + z * z >= 1
        }
        // (plasmids: three loops, r 0.07–0.1 µm — the radius drawn first, then the place, then the turn)
        for _ in 0..<3 {
            let r = rng.uniform(0.07, 0.1)
            guard let p = placeFree("plasmid", radius: r + 0.02, margin: 0.01, accept: { outsideNucleoid($0, r + 0.02) }) else { continue }
            let rot = rng.rotation()
            if hidden(p) { skip("plasmid"); continue }
            let loop = (0..<24).map { i -> BioVec in
                let t = 2 * Double.pi * Double(i) / 24
                return BioVec(r * cos(t), 0.015 * sin(3 * t), r * sin(t))
            }
            var s = CEShape()
            s.add(BioPath.tube(loop, radius: 0.01, sides: 6, samples: 2, closed: true), roughness: 0.5)
            add("plasmid", s.placed(CEGeo.frame(rot, at: p)), centre: p, radius: r)
        }
        freeRibosomes(1500, accept: { outsideNucleoid($0, 0.025) }, margin: 0.005)
        flagella(6)
        fimbriae(120)
        sexPilus()
        trueScaleRibosomes(accept: { outsideNucleoid($0, 0.015) }, margin: 0.005)
        if cut {
            var s = CEShape()
            s.add(CERecipes.cutFaces(outer: pmIn, inner: nil), alpha: 0.35, roughness: 0.8, order: 5)
            add("cytoplasm", s, centre: BioVec(0, 0, 0), radius: 1, shell: true)
        } else {
            skip("cytoplasm")
        }
    }

    /// The nucleoid (§5.1.5): a random walk from the centre, 600 steps of 0.035 µm each turning at most 50° (two draws),
    /// reflecting off the ellipsoid 0.65 × 0.32 × 0.32 µm; a tube r 0.012 µm through a Catmull–Rom spline of its points.
    mutating func nucleoid() {
        let a = 0.65, b = 0.32
        func outside(_ p: BioVec) -> Bool { (p.x / a) * (p.x / a) + (p.y / b) * (p.y / b) + (p.z / b) * (p.z / b) > 1 }
        var p = BioVec(0, 0, 0)
        var d = rng.unitVector()
        var pts = [p]
        for _ in 0..<600 {
            var nd = CEGeo.cone(d, 50, &rng)
            var q = p + nd * 0.035
            if outside(q) {
                let n = BioV.unit(BioVec(p.x / (a * a), p.y / (b * b), p.z / (b * b)))
                nd = BioV.unit(nd - n * (2 * BioV.dot(nd, n)))
                q = p + nd * 0.035
                if outside(q) { q = p }
            }
            pts.append(q)
            p = q
            d = nd
        }
        var s = CEShape()
        s.add(BioPath.tube(pts, radius: 0.012, sides: 6, samples: 2), roughness: 0.55)
        add("nucleoid", s, centre: BioVec(0, 0, 0), radius: 0.65)
    }

    /// A point on the bacterium's outer surface (r 0.5) along a direction, and its outward normal.
    func envelopePoint(_ u: BioVec) -> (p: BioVec, n: BioVec) {
        let p = u * CEGeo.capsuleXRadius(0.5, length: 1.0)(u)
        return (p, CEGeo.gradient({ CEGeo.capsuleXSDF($0, half: 0.5, r: 0.5) }, at: p))
    }

    /// Flagella (§5.1.5): six, each from a point on the surface — a hook bending from the normal to a direction 70°
    /// toward the rear pole (−x), then a left-handed helix about it (radius 0.2 µm, pitch 2.3 µm, 6 µm long), and a
    /// basal-body disc at the plasma membrane.
    mutating func flagella(_ count: Int) {
        let r = filament("flagellum", teaching: 0.03)
        for _ in 0..<count {
            let (p, n) = envelopePoint(rng.unitVector())
            var t = BioVec(-1, 0, 0) - n * BioV.dot(BioVec(-1, 0, 0), n)
            t = BioV.length(t) < 1e-6 ? CEGeo.perpendicular(n) : BioV.unit(t)
            let d = BioV.unit(n * cos(70 * Double.pi / 180) + t * sin(70 * Double.pi / 180))
            let ph = p + n * 0.06 + d * 0.06
            let hook = CEGeo.bezier(p, p + n * 0.05, ph - d * 0.03, ph, points: 7)
            let helix = CEBuilder.flagellumHelix(from: ph, along: d)
            if hidden(p) { skip("flagellum"); continue }
            var s = CEShape()
            s.add(BioPath.tube(hook, radius: r * 1.15, sides: 6, samples: 2), roughness: 0.45)
            s.add(BioPath.tube(helix, radius: r, sides: 6, samples: 2), roughness: 0.45)
            s.add(BioShapes.cylinder(from: p - n * 0.068, to: p - n * 0.048, radius: 0.022, segments: 12), .dark, roughness: 0.45)
            s.add(BioShapes.cylinder(from: p - n * 0.058, to: p, radius: 0.008, segments: 6), .dark, roughness: 0.45)
            add("flagellum", s, centre: helix[helix.count / 3], radius: 3.2, fixed: true)
        }
    }

    /// A flagellum's filament (§5.1.5): a helix about the axis d from the hook's end, radius 0.2 µm (growing to it
    /// over the first half-turn, so it starts on the hook), pitch 2.3 µm, 6 µm long; left-handed, E. coli's swimming
    /// form (e₂ = d × e₁ would give right-handed: the sine's sign is swapped). 24 points a turn.
    static func flagellumHelix(from ph: BioVec, along d: BioVec) -> [BioVec] {
        let e1 = CEGeo.perpendicular(d), e2 = BioV.cross(d, e1)
        let turns = 6 / 2.3, end = 2 * Double.pi * turns
        let n = Int((turns * 24).rounded(.up))
        return (0...n).map { k -> BioVec in
            let a = end * Double(k) / Double(n)
            let rh = 0.2 * CEGeo.smoothstep(0, .pi, a)
            return ph + d * (a * 2.3 / (2 * .pi)) + e1 * (rh * (cos(a) - 1)) - e2 * (rh * sin(a))
        }
    }

    /// Fimbriae: 120 straight hairs 0.4–0.9 µm long, normal to the surface (two draws for where, one for the length).
    mutating func fimbriae(_ count: Int) {
        let r = filament("fimbria", teaching: 0.012)
        for _ in 0..<count {
            let (p, n) = envelopePoint(rng.unitVector())
            let len = rng.uniform(0.4, 0.9)
            if hidden(p) { skip("fimbria"); continue }
            var s = CEShape()
            s.add(BioShapes.cylinder(from: p - n * 0.005, to: p + n * len, radius: r, segments: 6), roughness: 0.5)
            add("fimbria", s, centre: p + n * (len / 2), radius: len / 2, fixed: true)
        }
    }

    /// The sex pilus (§5.1.3, Nice): one tube 2 µm long, slightly curved.
    mutating func sexPilus() {
        let r = filament("sexpilus", teaching: 0.0135)
        let (p, n) = envelopePoint(rng.unitVector())
        let side = CEGeo.perpendicular(n)
        let end = p + n * 1.95 + side * 0.35
        let pts = CEGeo.bezier(p, p + n * 0.7, p + n * 1.4 + side * 0.15, end, points: 16)
        if hidden(p) { skip("sexpilus"); return }
        var s = CEShape()
        s.add(BioPath.tube(pts, radius: r, sides: 8, samples: 2), roughness: 0.5)
        add("sexpilus", s, centre: pts[8], radius: 1.0, fixed: true)
    }

    // MARK: Where the labels point (§3.5)

    /// Whether a point inside the cell can be seen from the camera: not behind an opaque wall (unless the ray leaves
    /// through the cutaway), nor behind the nucleus (unless through its cut).
    func canSee(_ p: BioVec) -> Bool {
        let v = cameraDirection
        // (an opaque wall: the plant's, the bacterium's envelope)
        if o.kind != .animal {
            var t = 0.0
            while inside(p + v * t, 0) && t < 60 { t += 0.02 * model.cellRadius }
            if !(cut && CEGeo.inWedge(p + v * t)) { return false }
        }
        // (the nucleus, opaque but for its cut octant)
        if nucleusRadius > 0 {
            let N = nucleusCentre, R = nucleusRadius
            let w = p - N
            let b = BioV.dot(w, v), c = BioV.dot(w, w) - R * R
            let disc = b * b - c
            if disc > 0 {
                let t1 = -b - disc.squareRoot(), t2 = -b + disc.squareRoot()
                if c < 0 {
                    // (inside the nucleus: seen only through its cut, where the ray leaves it)
                    return cut && CEGeo.inWedge(p + v * t2, N)
                }
                if t1 > 0 && !(cut && CEGeo.inWedge(p + v * t1, N) && CEGeo.inWedge(p + v * t2, N)) { return false }
            }
        }
        return true
    }

    /// A point's score as a label's anchor: seen first, then nearer the camera.
    func score(_ p: BioVec) -> Double { (canSee(p) ? 100 : 0) + BioV.dot(p, cameraDirection) / model.cellRadius }

    /// Each part's anchor: for a convex outer shell, a point on it facing the camera (with its normal, §3.5 item 1);
    /// for what lies under an opaque wall, a point on its cut face; for the rest, the instance (or a point of it) the
    /// camera sees best.
    mutating func anchors() {
        let v = cameraDirection
        var best: [String: (score: Double, anchor: CEAnchor)] = [:]
        func offer(_ part: String, _ inst: Int, _ p: BioVec, normal: BioVec? = nil, score s: Double? = nil) {
            let sc = s ?? score(p)
            if best[part].map({ sc > $0.score }) ?? true { best[part] = (sc, CEAnchor(instance: inst, point: p, normal: normal)) }
        }
        /// A direction like the camera's, moved off the cut octant toward `away`.
        func facing(_ away: BioVec) -> BioVec {
            var d = v
            var n = 0
            while d.x > 0 && d.y > 0 && d.z > 0 && n < 40 { d = BioV.unit(d + away * 0.15); n += 1 }
            return BioV.unit(d + away * 0.25)
        }
        /// The vertex of an instance's meshes nearest the camera, and its normal.
        func front(_ inst: CEInstance) -> (BioVec, BioVec)? {
            var bestP: (BioVec, BioVec)?
            var bestD = -Double.infinity
            for piece in inst.pieces {
                for (k, q) in piece.mesh.positions.enumerated() {
                    let p = CEGeo.vec(q)
                    let dd = BioV.dot(p, v)
                    if dd > bestD { bestD = dd; bestP = (p, CEGeo.vec(piece.mesh.normals[k])) }
                }
            }
            return bestP
        }
        /// A point on a shell's cut face on the plane z = 0, at direction (cos a, sin a) between radii.
        func cutFace(_ part: String, _ i: Int, angle a: Double, _ rIn: (BioVec) -> Double, _ rOut: (BioVec) -> Double) {
            guard cut else { return }
            let d = BioVec(cos(a), sin(a), 0)
            offer(part, i, d * ((rIn(d) + rOut(d)) / 2), normal: BioVec(0, 0, 1), score: 1)
        }
        let bacteriumPM = o.gramPositive ? (0.43, 0.44) : (0.432, 0.442)
        let bacteriumWall = o.gramPositive ? (0.45, 0.5) : (0.46, 0.48)
        let capsuleX = { (r: Double) in CEGeo.capsuleXRadius(r, length: 1.0) }
        for (i, inst) in model.instances.enumerated() where !inst.pieces.isEmpty {
            let part = inst.part
            switch (o.kind, part) {
            case (.animal, "pm"):
                let d = facing(BioVec(-1, 0.2, 0))
                let p = d * (membraneInner(d) + 0.08)
                offer(part, i, p, normal: CEGeo.gradient(surface, at: p), score: 1)
            case (.plant, "pm"):
                cutFace(part, i, angle: 0.62, membraneInner, { self.membraneInner($0) + 0.08 })
            case (.bacterium, "pm"):
                cutFace(part, i, angle: 0.5, capsuleX(bacteriumPM.0), capsuleX(bacteriumPM.1))
            case (.bacterium, "wall") where !o.gramPositive:
                cutFace(part, i, angle: 0.95, capsuleX(bacteriumWall.0), capsuleX(bacteriumWall.1))
            case (.plant, "wall"), (.bacterium, "wall"), (.bacterium, "capsule"), (.bacterium, "outer"):
                let away: BioVec = part == "wall" ? BioVec(-1, -0.6, 0) : (part == "capsule" ? BioVec(-1, 0.5, 0) : BioVec(-0.4, -1, 0))
                let d = facing(away)
                let f: (BioVec) -> Double = o.kind == .plant ? BioShapes.roundedBoxRadius(BioVec(22, 13, 13), corner: 3)
                    : capsuleX(part == "capsule" ? 0.65 : 0.5)
                let p = d * f(d)
                let sdf: (BioVec) -> Double = o.kind == .plant ? { CEGeo.roundedBoxSDF($0, half: BioVec(22, 13, 13), corner: 3) }
                    : { CEGeo.capsuleXSDF($0, half: 0.5, r: part == "capsule" ? 0.65 : 0.5) }
                offer(part, i, p, normal: CEGeo.gradient(sdf, at: p), score: 1)
            case (_, "cytoplasm"):
                // (its cut face on the plane z = 0, between the membrane and the vacuole, or toward the centre)
                let d = BioV.unit(BioVec(0.62, 0.78, 0))
                let outer = membraneInner(d)
                offer(part, i, d * (o.kind == .plant ? outer - 1.5 : outer * 0.55), normal: BioVec(0, 0, 1), score: 1)
            case (_, "vacuole"):
                if let (p, n) = front(inst) { offer(part, i, p, normal: n, score: 1) }
            case (_, "nucleus"):
                let N = nucleusCentre, R = nucleusRadius
                if cut {
                    // (the back of the inner membrane, seen through the cut — with its normal, toward the camera: tested
                    // with a ray, the label was hidden by the nucleolus and chromatin in front of it, and the Cell
                    // Explorer's pictures had no "Nucleus" at all, nor the nucleus looked inside its "Nuclear envelope")
                    offer(part, i, N - v * (R - 0.065), normal: v, score: 1)
                } else {
                    let d = facing(BioVec(-1, 0, 0))
                    offer(part, i, N + d * R, normal: d, score: 1)
                }
            case (_, "pore"):
                let d = BioV.unit(inst.centre - nucleusCentre)
                // (the pores above the rough ER's sheets, facing the camera)
                offer(part, i, inst.centre, score: (acos(min(max(d.y, -1), 1)) < 0.22 * .pi ? 10 : 0) + BioV.dot(d, v))
            case (_, "rer"):
                for piece in inst.pieces {
                    let m = piece.mesh
                    for k in stride(from: 0, to: m.positions.count, by: max(1, m.positions.count / 60)) {
                        let p = CEGeo.vec(m.positions[k])
                        offer(part, i, p, score: score(p) + BioV.distance(p, nucleusCentre))
                    }
                }
            case (_, "ser"), (_, "chromatin"), (_, "microtubule"), (_, "intermediate"):
                for piece in inst.pieces {
                    let m = piece.mesh
                    for k in stride(from: 0, to: m.positions.count, by: max(1, m.positions.count / 12)) {
                        offer(part, i, CEGeo.vec(m.positions[k]))
                    }
                }
            case (_, "flagellum"), (_, "fimbria"), (_, "sexpilus"), (_, "plasmodesma"):
                offer(part, i, inst.centre, score: BioV.dot(BioV.unit(inst.centre), v))
            default:
                offer(part, i, inst.centre)
            }
        }
        model.anchors = best.mapValues(\.anchor)
    }

    // MARK: Look inside

    /// One mitochondrion or chloroplast (the one its label points to) cut lengthwise, its inside toward the camera; or
    /// the nucleus cut open with its pores, nucleolus and chromatin. In the scene's frame.
    mutating func lookInside(_ part: String) -> [CEInstance] {
        let d = model.display
        var out: [CEInstance] = []
        switch part {
        case "mito", "chloroplast":
            guard let a = model.anchors[part], let f = model.instances[a.instance].frame else { return [] }
            let half = CEGeo.frame(y: f.y, towardZ: cameraDirection, at: f.origin)
            let shape = part == "mito" ? CERecipes.mitochondrion(half: true, fine: true)
                : CERecipes.chloroplast(detail: true, half: true, ribosomeRadius: o.trueScale ? 0.01 : 0.02, fine: true)
            var b = CEBuilder(o)
            b.add(part, shape.placed(half), centre: f.origin, radius: model.instances[a.instance].radius, frame: f)
            out = b.model.instances
        case "nucleus":
            // (built again cut, from the same draws: the Golgi side first, as the cell did)
            var b = CEBuilder(CEBuildOptions(kind: o.kind, trueScale: o.trueScale, cutaway: true, gramPositive: o.gramPositive))
            _ = b.rng.unitVector()
            b.nucleus(o.kind == .animal ? BioVec(1.0, 0.5, -0.5) : BioVec(-9, 9.2, 0), o.kind == .animal ? 3.2 : 3.0)
            out = b.model.instances
        default:
            return []
        }
        for i in out.indices {
            out[i].centre = d.apply(out[i].centre)
            out[i].explodeFrom = out[i].centre
            for j in out[i].pieces.indices { out[i].pieces[j].mesh = out[i].pieces[j].mesh.transformed(d) }
        }
        return out
    }
}

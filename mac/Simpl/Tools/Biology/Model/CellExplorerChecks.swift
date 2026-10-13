import Foundation

// The Cell Explorer's checks of its geometry (spec §4.1, §4.1.1, §5.1.5, §10.2's "animal, plant and bacterium build
// deterministically"): each cell built from its seed has the counts §5.1 gives (or says why not), stays under the
// teaching-scale budget of 250k triangles, keeps every free organelle inside its membrane and clear of the others, leaves
// the cutaway's wedge empty, comes out the same twice; the explode rule and the shells' fade; the plant's chloroplasts
// and dented vacuole; the bacterium's nucleoid and left-handed flagella; the true scale's 20,000 ribosomes; the
// membrane patch's lipids, cholesterol and labels. Lines "bio-check cell.… <value> OK|FAIL".

extension BioSelfCheck {
    static func cellBuildChecks(_ r: inout BioCheckReport) {
        let animal = CEBuild.build(CEBuildOptions(kind: .animal))
        let plant = CEBuild.build(CEBuildOptions(kind: .plant))
        let bacterium = CEBuild.build(CEBuildOptions(kind: .bacterium))
        /// A scene point back in the cell's own frame.
        func cellFrame(_ m: CEModel, _ p: BioVec) -> BioVec { BioVec(BioV.dot(p, m.display.x), BioV.dot(p, m.display.y), BioV.dot(p, m.display.z)) }

        // (§5.1.1's counts: what was placed; free placements may give up, and the line says how many did)
        let a = animal.placed
        let want: [String: Int] = ["nucleus": 1, "pore": 110, "nucleolus": 1, "chromatin": 12, "rer": 5, "golgi": 1, "mito": 24, "lysosome": 12,
                                   "peroxisome": 8, "centrosome": 1, "microtubule": 40, "intermediate": 30, "flagellum": 1]
        let countsOK = want.allSatisfy { a[$0.key] == $0.value }
        r.check("cell.animal.counts", want.keys.sorted().map { "\($0)=\(a[$0] ?? 0)" }.joined(separator: ","), countsOK)
        let ribo = (animal.extra["ribosome.free"] ?? 0, animal.extra["ribosome.er"] ?? 0)
        r.check("cell.animal.ribosomes", "\(ribo.0)+\(ribo.1)", ribo.0 == 1500 && ribo.1 == 2500)
        r.check("cell.animal.vesicles", "\(animal.extra["vesicle.free"] ?? 0)+\(animal.extra["vesicle.golgi"] ?? 0)",
                animal.extra["vesicle.free"] == 30 && animal.extra["vesicle.golgi"] == 24)
        let ser = animal.extra["ser.segments"] ?? 0
        r.check("cell.animal.smooth-er", "\(ser)segments", ser > 40 && ser <= 160)
        r.check("cell.animal.actin", "\(a["actin"] ?? 0)", (a["actin"] ?? 0) + (animal.gaveUp["actin"] ?? 0) == 220 && (a["actin"] ?? 0) >= 200)
        r.check("cell.animal.gave-up", animal.gaveUp.isEmpty ? "none" : animal.gaveUp.sorted { $0.key < $1.key }.map { "\($0.key)=\($0.value)" }.joined(separator: ","),
                (animal.gaveUp["mito"] ?? 0) == 0 && (animal.gaveUp["ribosome"] ?? 0) == 0)

        // (the budget: fewer than 250k triangles in teaching scale, cut or whole)
        let whole = CEBuild.build(CEBuildOptions(kind: .animal, cutaway: false))
        for (name, m) in [("animal", animal), ("animal-whole", whole), ("plant", plant), ("bacterium", bacterium)] {
            r.check("cell.\(name).triangles", "\(m.triangles)", m.triangles < 250_000 && m.triangles > 20_000)
        }

        // (every free organelle inside the membrane, and the cutaway's wedge empty of organelles' centres)
        func freeInside(_ m: CEModel, _ test: (BioVec, Double) -> Bool) -> Bool {
            m.instances.filter { !$0.shell && !$0.fixed && ["mito", "lysosome", "peroxisome", "vesicle", "ribosome", "golgi", "plasmid", "amyloplast"].contains($0.part) }
                .allSatisfy { test(cellFrame(m, $0.centre), $0.radius) }
        }
        let ell = BioShapes.ellipsoid(11, 8, 9.5), blob = BioBlob(seed: 0xC311, amplitude: 0.06)
        r.check("cell.animal.inside", "yes", freeInside(animal) { p, rad in BioV.length(p) + rad <= ell(BioV.unit(p)) * blob.factor(BioV.unit(p)) - 0.08 })
        r.check("cell.plant.inside", "yes", freeInside(plant) { p, rad in CEGeo.roundedBoxSDF(p, half: BioVec(22, 13, 13), corner: 3) <= -0.6 - rad })
        r.check("cell.bacterium.inside", "yes", freeInside(bacterium) { p, rad in CEGeo.capsuleXSDF(p, half: 0.5, r: 0.432) <= -rad + 1e-9 })
        let wedge = [animal, plant, bacterium].allSatisfy { m in
            m.instances.filter { !$0.shell && !$0.fixed && !["rer", "ser", "chromatin", "microtubule", "intermediate", "nucleus", "pore", "vacuole", "nucleoid"].contains($0.part) }
                .allSatisfy { !CEGeo.inWedge(cellFrame(m, $0.centre)) }
        }
        r.check("cell.cutaway-wedge", wedge ? "empty" : "not-empty", wedge)

        // (no two mitochondria's bounding spheres overlap)
        let mitos = animal.instances.filter { $0.part == "mito" }.map { cellFrame(animal, $0.centre) }
        var closest = Double.infinity
        for i in 0..<mitos.count { for j in (i + 1)..<mitos.count { closest = min(closest, BioV.distance(mitos[i], mitos[j])) } }
        r.check("cell.animal.mito-spacing", String(format: "%.2f", closest), closest >= 2.2 * 1.05 - 1e-9 || mitos.count < 2)

        // (the same twice: built again from the seed, the same meshes)
        let again = CEBuild.build(CEBuildOptions(kind: .animal))
        let same = again.triangles == animal.triangles && again.instances.count == animal.instances.count
            && zip(again.instances, animal.instances).allSatisfy { BioV.distance($0.centre, $1.centre) < 1e-12 }
        r.check("cell.deterministic", "\(animal.instances.count)instances", same)

        // (explode: the nucleus moves least; an organelle at the cell's radius moves 0.6 of it; the shells fade to 15%)
        let nucleus = animal.instances.first { $0.part == "nucleus" }!
        let farMito = animal.instances.filter { $0.part == "mito" }.max { BioV.length($0.centre) < BioV.length($1.centre) }!
        let dn = BioV.length(CEScene.displacement(nucleus, explode: 1, cellRadius: animal.cellRadius))
        let dm = BioV.length(CEScene.displacement(farMito, explode: 1, cellRadius: animal.cellRadius))
        let probe = CEInstance(part: "mito", index: 0, centre: BioVec(animal.cellRadius, 0, 0), radius: 1, explodeFrom: BioVec(animal.cellRadius, 0, 0))
        r.near("cell.explode-rule", BioV.length(CEScene.displacement(probe, explode: 1, cellRadius: animal.cellRadius)) / animal.cellRadius, 0.6, 1e-9, digits: 3)
        r.check("cell.explode-nucleus-least", String(format: "%.2f<%.2f", dn, dm), dn < dm)
        r.check("cell.explode-fade", String(format: "%.2f,%.2f,%.2f", CEScene.shellFade(0), CEScene.shellFade(0.15), CEScene.shellFade(0.6)),
                abs(CEScene.shellFade(0) - 1) < 1e-12 && abs(CEScene.shellFade(0.3) - 0.15) < 1e-12 && abs(CEScene.shellFade(0.6) - 0.15) < 1e-12)

        // (the plant: 40 chloroplasts 5.2 µm apart and 6 µm from the nucleus; the vacuole dented round the nucleus)
        let chloro = plant.instances.filter { $0.part == "chloroplast" }.map { cellFrame(plant, $0.centre) }
        let placedChloro = plant.placed["chloroplast"] ?? 0
        var chloroGap = Double.infinity
        for i in 0..<chloro.count { for j in (i + 1)..<chloro.count { chloroGap = min(chloroGap, BioV.distance(chloro[i], chloro[j])) } }
        let N = BioVec(-9, 9.2, 0)
        r.check("cell.plant.chloroplasts", "\(placedChloro),gap\(String(format: "%.2f", chloroGap))",
                placedChloro == 40 && chloroGap >= 5.2 && chloro.allSatisfy { BioV.distance($0, N) >= 6.0 })
        if let vac = plant.instances.first(where: { $0.part == "vacuole" })?.pieces.first?.mesh {
            let nearest = vac.positions.map { BioV.distance(cellFrame(plant, CEGeo.vec($0)), N) }.min() ?? 0
            r.near("cell.plant.vacuole-dent", nearest, 3.4, 1e-3, digits: 3)
        }
        r.check("cell.plant.counts", "mito=\(plant.placed["mito"] ?? 0),golgi=\(plant.placed["golgi"] ?? 0),perox=\(plant.placed["peroxisome"] ?? 0),ribo=\(plant.extra["ribosome.free"] ?? 0)+\(plant.extra["ribosome.er"] ?? 0)",
                plant.placed["mito"] == 18 && plant.placed["golgi"] == 6 && plant.placed["peroxisome"] == 6
                    && plant.extra["ribosome.free"] == 900 && plant.extra["ribosome.er"] == 300
                    && plant.placed["plasmodesma"] == 30 && plant.placed["microtubule"] == 30)

        // (the bacterium: its nucleoid inside its ellipsoid; flagella left-handed; 1,500 ribosomes outside the nucleoid)
        r.check("cell.bacterium.counts", "flagella=\(bacterium.placed["flagellum"] ?? 0),fimbriae=\(bacterium.placed["fimbria"] ?? 0),ribo=\(bacterium.extra["ribosome.free"] ?? 0),plasmids=\(bacterium.placed["plasmid"] ?? 0)",
                bacterium.placed["flagellum"] == 6 && bacterium.placed["fimbria"] == 120 && bacterium.extra["ribosome.free"] == 1500 && bacterium.placed["plasmid"] == 3)
        let d = BioV.unit(BioVec(-0.4, 0.3, 0.2))
        let helix = CEBuilder.flagellumHelix(from: BioVec(0, 0, 0), along: d)
        // (left-handed: going along d, the filament turns about −d)
        let e1 = CEGeo.perpendicular(d)
        var turning = 0.0
        for k in 30..<(helix.count - 1) {
            let a = helix[k] - d * BioV.dot(helix[k], d) + e1 * 0.2, b = helix[k + 1] - d * BioV.dot(helix[k + 1], d) + e1 * 0.2
            turning += BioV.dot(BioV.cross(a, b), d)
        }
        let length = BioV.dot(helix.last!, d)
        r.check("cell.bacterium.flagellum", String(format: "%@,%.2fµm", turning < 0 ? "left-handed" : "right-handed", length), turning < 0 && abs(length - 6) < 0.05)

        // (true scale: 20,000 ribosomes at their true sizes, the rest of the cell as it was)
        let trueScale = CEBuild.build(CEBuildOptions(kind: .bacterium, trueScale: true))
        let total = (trueScale.extra["ribosome.free"] ?? 0) + (trueScale.extra["ribosome.er"] ?? 0)
        let plantTrue = CEBuild.build(CEBuildOptions(kind: .plant, trueScale: true))
        let plantCentres = { (m: CEModel) in m.instances.filter { $0.part != "ribosome" }.map(\.centre) }
        let sameRest = trueScale.placed["flagellum"] == bacterium.placed["flagellum"] && trueScale.placed["plasmid"] == bacterium.placed["plasmid"]
            && plantCentres(plantTrue).count == plantCentres(plant).count
            && zip(plantCentres(plantTrue), plantCentres(plant)).allSatisfy { BioV.distance($0, $1) < 1e-9 }
        r.check("cell.true-scale", "\(total)ribosomes", total >= 19_000 && total <= 20_000 && sameRest
                && abs((trueScale.instances.first { $0.part == "ribosome" }?.radius ?? 0) - 0.010) < 1e-12)

        // (every part's card says what the model has of it)
        let lines = [animal, plant, bacterium].flatMap { m in CEParts.all(m.options.kind).map { m.modelLine($0) } }
        r.check("cell.card-model-lines", "\(lines.filter { !$0.isEmpty }.count)/\(lines.count)", lines.allSatisfy { !$0.isEmpty })
        if let mito = CEParts.part(.animal, "mito"), let ribo = CEParts.part(.animal, "ribosome") {
            r.same("cell.card-mito", animal.modelLine(mito), "24 mitochondria, each 2.2 µm long and 0.9 µm wide. 2 of them are in the part cut away.")
            r.check("cell.card-ribosome", animal.modelLine(ribo),
                    animal.modelLine(ribo).hasPrefix("\(CEModel.count(1500)) free and \(CEModel.count(2500)) on the rough ER, each 0.12 µm across — drawn 4× their true size"))
        }
        let plantWhole = CEBuild.build(CEBuildOptions(kind: .plant, cutaway: false))
        r.check("cell.plant-whole.triangles", "\(plantWhole.triangles)", plantWhole.triangles < 250_000)

        // (labels: every part drawn has somewhere to point)
        let missing = [animal, plant, bacterium].flatMap { m in m.partsShown.filter { m.anchors[$0] == nil }.map { "\(m.options.kind.rawValue).\($0)" } }
        r.check("cell.label-anchors", missing.isEmpty ? "all" : missing.joined(separator: ","), missing.isEmpty)

        // (SA:V: the bacterium's far higher than the animal cell's — why cells are small)
        r.check("cell.sa-v", String(format: "%.2f/%.2f/%.2f", animal.area / animal.volume, plant.area / plant.volume, bacterium.area / bacterium.volume),
                bacterium.area / bacterium.volume > 4 && animal.area / animal.volume < 0.5 && plant.area / plant.volume < animal.area / animal.volume)

        // (a scene: isolate shows only its part; look inside builds a cut mitochondrion with labels)
        let iso = CEScene.content(animal, CEView(isolate: "mito"))
        let looked = CEScene.content(animal, CEView(isolate: "mito", lookInside: true))
        r.check("cell.isolate", "\(iso.items.count),\(looked.items.count)items,\(looked.labels.count)labels",
                iso.items.allSatisfy { $0.id?.hasPrefix("organelle:mito:") == true } && !looked.items.isEmpty && looked.labels.count == 4)
        let compare = CEBuild.compareScene(dark: false)
        r.check("cell.compare-scene", "\(compare.items.count)items", Set(compare.items.compactMap(\.id)) == ["cell:animal", "cell:plant", "cell:bacterium"]
                && compare.labels.count == 3)
    }

    static func cellMembraneChecks(_ r: inout BioCheckReport) {
        let m = CEMembrane.build(dark: false)
        let slots = Double(m.lipids + m.cholesterol)
        r.check("cell.membrane.lipids", "\(m.lipids)+\(m.cholesterol)chol", m.lipids > 9000 && abs(Double(m.cholesterol) / slots - 0.12) < 0.015)
        r.check("cell.membrane.triangles", "\(m.triangles)", m.triangles < 600_000)
        r.check("cell.membrane.labels", "\(m.content.labels.count)", m.content.labels.count == 12 && CEMembrane.parts.count == 12)
        let motion = CEMembrane.motion()
        let sameCarrier = zip(motion.carrier, m.carrier).allSatisfy { BioV.distance($0.centre, $1.centre) < 1e-9 && abs($0.inside.angle - $1.inside.angle) < 1e-9 }
        r.check("cell.membrane.carrier", "\(m.carrier.count)helices", m.carrier.count == 7 && motion.carrier.count == 7 && sameCarrier)
        // (the bilayer's thickness: head centres 3.9 nm apart, about 4.8 nm across the heads)
        r.near("cell.membrane.thickness", 2 * CEMembrane.headY + 0.9, 4.8, 1e-9, digits: 2)
        // (the quaternion of a frame turns as the frame does)
        let q = CEQuat.of(CEMembrane.display)
        let v = BioVec(0.3, -0.5, 0.8)
        r.near("cell.membrane.turn", BioV.distance(q.rotate(v), CEMembrane.display.applyVector(v)), 0, 1e-12, digits: 3)
    }
}

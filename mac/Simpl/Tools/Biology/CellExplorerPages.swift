import AppKit
import SceneKit
import SwiftUI

// The Cell Explorer's two other pages. Compare (spec §4.1 item 8): the three cells side by side at their true relative
// sizes (a bacterium 2 µm long, an animal cell 20 µm, a plant cell 40 µm — "how big is each"), then §5.1.4's table
// exactly, each row opening that structure in the cell that has it. Membrane (§4.1.1): a 60 × 60 nm patch of plasma
// membrane in nanometres, its twelve parts labelled, each with a card; cholesterol and the sugar chains shown or not; the
// carrier rocking open to one side then the other and water filing through the aquaporin (osmosis), as SceneKit nodes
// with repeating actions over the batched patch (still under Reduce Motion); and "See a real phospholipid", the
// Biomolecule Builder's POPC (§6.9) in a sheet.

// MARK: - Compare

struct CEComparePage: View {
    @Environment(\.colorScheme) private var scheme
    @State private var scale = 0.0
    @State private var turning = false
    @State private var reset = 0
    @State private var hoveredRow: Int?

    var body: some View {
        ToolPage(maxWidth: 1240, spacing: 18) {
            VStack(alignment: .leading, spacing: 4) {
                Text("How big is each?").font(.sTitle2).accessibilityAddTraits(.isHeader)
                Text("A bacterium, an animal cell and a plant cell at their true relative sizes: a bacterium is 10–20 times smaller than an animal cell. Click one to explore it.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            BioSceneCard(hint: "At their true relative sizes · drag to turn · click a cell to open it") {
                Text("Side by side").font(.system(size: 20, weight: .bold))
                CEScaleChip(trueScale: true)
                Spacer(minLength: 8)
                BioSceneButtons(turning: $turning) { reset += 1 }
            } scene: {
                ZStack(alignment: .bottomLeading) {
                    BioSceneView(key: "compare|\(scheme == .dark)",
                                 build: BioSceneBuild { [d = scheme == .dark] in CEBuild.compareScene(dark: d) },
                                 selection: .constant(nil),
                                 showLabels: true,
                                 turning: turning,
                                 frameKey: "compare",
                                 reset: reset,
                                 dimOthers: false,
                                 accessibility: "3D model of a bacterium, an animal cell and a plant cell side by side at their true relative sizes: about 2, 20 and 40 µm",
                                 onPick: { id in
                                     guard let id, id.hasPrefix("cell:"), let k = CEKind(rawValue: String(id.dropFirst(5))) else { return }
                                     BioNav.shared.set(.cell, k.rawValue)
                                 },
                                 onScale: { scale = $0 },
                                 labelBottom: CEScaleBar.room)
                    CEScaleBar(pointsPerUnit: scale).padding(14).allowsHitTesting(false)
                }
            }
            .frame(height: 380)
            table
            Text("✓ present · ✗ absent · ~ partly. Click a row to open that structure in a cell that has it.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
        }
    }

    private var table: some View {
        VStack(spacing: 0) {
            row(["Structure", "Animal", "Plant", "Bacterium"], header: true)
                .background(Color.primary.opacity(0.05))
            ForEach(CECompare.rows) { r in
                Divider()
                Button { open(r) } label: {
                    row([r.structure] + r.cells, header: false)
                        .background(hoveredRow == r.id && r.open != nil ? Theme.accent.opacity(0.08) : Color.clear)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .disabled(r.open == nil)
                .onHover { inside in hoveredRow = inside ? r.id : (hoveredRow == r.id ? nil : hoveredRow) }
                .help(r.open.map { "Open the \(CEParts.part($0.kind, $0.part)?.labelInSentence ?? CEPart.inSentence(r.structure)) in the \($0.kind.title.lowercased())" } ?? r.structure)
                .accessibilityLabel("\(r.structure): animal \(r.cells[0]); plant \(r.cells[1]); bacterium \(r.cells[2])")
            }
        }
        .card(radius: 16)
    }

    private func row(_ cells: [String], header: Bool) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text(cells[0])
                .font(header ? .sCaption.weight(.semibold) : .sCallout.weight(.semibold))
                .foregroundStyle(header ? Color.secondary : Color.primary)
                .frame(width: 210, alignment: .leading)
            ForEach(1..<4, id: \.self) { i in
                (header ? Text(cells[i]).font(.sCaption.weight(.semibold)).foregroundColor(.secondary) : mark(cells[i]))
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, header ? 9 : 10)
    }

    /// A Compare cell as §5.1.4 writes it, its mark coloured (never colour alone: the mark is there).
    private func mark(_ s: String) -> Text {
        let (m, _) = CEMark.read(s)
        let first = s.first.map(String.init) ?? ""
        guard ["✓", "✗", "~"].contains(first) else { return Text(s).font(.sCallout) }
        let colour: Color = m == .yes ? .green : (m == .no ? .secondary : .orange)
        return Text(first).font(.sCallout.weight(.bold)).foregroundColor(colour) + Text(String(s.dropFirst())).font(.sCallout)
    }

    private func open(_ r: CECompareRow) {
        guard let o = r.open else { return }
        CENav.shared.open(o.kind, part: o.part)
    }
}

// MARK: - Membrane

struct CEMembranePage: View {
    var state: BioLaunchState?

    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// The part selected ("part:<id>", or "side:outside" / "side:inside").
    @State private var selection: String?
    @State private var labels = BioPrefs.bool(.cell, "membrane.labels", default: true)
    @State private var cholesterol = BioPrefs.bool(.cell, "membrane.cholesterol", default: true)
    @State private var sugars = BioPrefs.bool(.cell, "membrane.sugars", default: true)
    @State private var transport = BioPrefs.bool(.cell, "membrane.transport", default: true)
    @State private var osmosis = BioPrefs.bool(.cell, "membrane.osmosis", default: false)
    @State private var turning = false
    @State private var reset = 0
    @State private var scale = 0.0
    @State private var hovered: String?
    @State private var showPOPC = false
    /// The launch's state has been told on the console (the screenshot suite's).
    @State private var launchTold = false

    /// (-SimplTool cell -SimplToolState membrane:select=integral:channel:osmosis …) The page starts as its launch asks —
    /// select=, osmosis, still (no carrier motion), nolabels, popc — from its first frame, in any page made for it (as the
    /// cells' pages do: CECellPage.init). Nothing a launch sets is remembered.
    init(state: BioLaunchState?) {
        self.state = state
        guard let s = state, s.page == "membrane" else { return }
        if s.has("osmosis") { _osmosis = State(initialValue: true) }
        if s.has("still") { _transport = State(initialValue: false) }
        if s.has("nolabels") { _labels = State(initialValue: false) }
        if s.has("popc") { _showPOPC = State(initialValue: true) }
        // ("select=integral:channel" arrives split at its colon: the parts after "select=" rejoined)
        if let i = s.parts.firstIndex(where: { $0.lowercased().hasPrefix("select=") }) {
            var id = String(s.parts[i].dropFirst("select=".count))
            if i + 1 < s.parts.count, CEMembrane.part(id + ":" + s.parts[i + 1]) != nil { id += ":" + s.parts[i + 1] }
            if CEMembrane.part(id) != nil { _selection = State(initialValue: id.hasPrefix("side:") ? id : "part:" + id) }
        }
    }

    private var dark: Bool { scheme == .dark }
    private var anim: Animation? { reduceMotion ? nil : Motion.gentle }
    /// The carrier rocks (as nodes) only when asked and motion is allowed; else it stays in the batched patch.
    private var carrierMoving: Bool { transport && !reduceMotion }
    private var key: String { "membrane|\(dark)|\(cholesterol)|\(sugars)|\(carrierMoving)|\(osmosis)|\(reduceMotion)" }

    /// A picked id's membrane part: "part:bilayer:heads:pc" → "bilayer:heads", "part:integral:protein:5" → "integral".
    static func partID(_ id: String) -> String? {
        if id.hasPrefix("side:") { return id }
        guard id.hasPrefix("part:") else { return nil }
        let rest = String(id.dropFirst(5))
        return CEMembrane.parts.map(\.id).filter { rest == $0 || rest.hasPrefix($0 + ":") }.max { $0.count < $1.count }
    }

    private var selectedPart: CEMembranePart? { selection.flatMap { CEMembranePage.partID($0) }.flatMap { CEMembrane.part($0) } }

    var body: some View {
        PTWorkLayout {
            left
        } centre: {
            sceneCard
        } right: {
            right.animation(anim, value: selectedPart?.id)
        }
        .onAppear(perform: applyLaunch)
        .onExitCommand { withAnimation(anim) { selection = nil } }
        .sheet(isPresented: $showPOPC) { CEPOPCSheet() }
    }

    private func saved(_ b: Binding<Bool>, _ field: String) -> Binding<Bool> {
        Binding(get: { b.wrappedValue }, set: { v in
            withAnimation(anim) { b.wrappedValue = v }
            BioPrefs.set(.cell, "membrane." + field, v)
        })
    }

    private var left: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Parts")
            VStack(alignment: .leading, spacing: 1) {
                ForEach(CEMembrane.parts) { p in
                    let selected = selectedPart?.id == p.id
                    Button { select(p.id) } label: {
                        HStack(spacing: 8) {
                            RoundedRectangle(cornerRadius: 3, style: .continuous)
                                .fill(p.colour.color)
                                .frame(width: 12, height: 12)
                                .overlay(RoundedRectangle(cornerRadius: 3, style: .continuous).strokeBorder(Color.primary.opacity(0.2), lineWidth: 0.5))
                            Text(p.name).lineLimit(1)
                            Spacer(minLength: 0)
                        }
                        .padding(.vertical, 4)
                        .padding(.horizontal, 6)
                        .background(selected ? Theme.accent.opacity(0.16) : Color.clear, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(selected ? .isSelected : [])
                }
            }
            BioHeading("Show")
            Toggle("Labels", isOn: saved($labels, "labels")).help("Name the parts at the sides, with lines to them")
            Toggle("Cholesterol", isOn: saved($cholesterol, "cholesterol")).help("Cholesterol among the tails, its OH at the heads' level")
            Toggle("Sugar chains", isOn: saved($sugars, "sugars")).help("The glycoproteins' and glycolipids' sugars, on the outside face")
            Toggle("Channel and carrier at work", isOn: saved($transport, "transport"))
                .help(reduceMotion ? "The carrier shown still (Reduce Motion is on)" : "The carrier protein rocking open to one side, then the other")
            Toggle("Osmosis", isOn: saved($osmosis, "osmosis")).help("Water moving through the aquaporin channel")
            BioHeading("Up close")
            Button { showPOPC = true } label: { Label("See a real phospholipid", systemImage: "atom").frame(maxWidth: .infinity) }
                .glassButton()
                .controlSize(.large)
                .help("POPC, atom by atom, from the Biomolecule Builder")
        }
        .font(.sCallout)
    }

    private var sceneCard: some View {
        BioSceneCard(hint: "Drag to turn · scroll to zoom · click a part to learn about it") {
            Text("Plasma membrane").font(.system(size: 22, weight: .bold))
            Text("60 × 60 nm").font(.sCaption.weight(.semibold)).padding(.horizontal, 9).padding(.vertical, 4)
                .background(Color.green.opacity(0.16), in: Capsule()).foregroundStyle(Color.green)
            Spacer(minLength: 8)
            BioSceneButtons(turning: $turning) { reset += 1 }
        } scene: {
            ZStack(alignment: .bottomLeading) {
                BioSceneView(key: key,
                             build: BioSceneBuild { [d = dark, c = cholesterol, s = sugars, m = carrierMoving] in
                                 CEMembrane.build(dark: d, cholesterol: c, glyco: s, carrierAnimated: m).content
                             },
                             extras: { [d = dark, m = carrierMoving, o = osmosis, still = reduceMotion] in
                                 CEMembraneNodes.nodes(dark: d, carrier: m, osmosis: o, still: still)
                             },
                             selection: $selection,
                             showLabels: labels,
                             turning: turning,
                             frameKey: "membrane",
                             reset: reset,
                             accessibility: "3D model of a patch of plasma membrane, 60 by 60 nanometres, cut on two sides to show the phospholipid bilayer"
                                 + (selectedPart.map { "; selected: \(CEPart.inSentence($0.name))" } ?? ""),
                             onPick: { id in withAnimation(anim) { selection = id.flatMap { CEMembranePage.partID($0) }.map { $0.hasPrefix("side:") ? $0 : "part:" + $0 } } },
                             onHover: { hovered = $0 },
                             onScale: { scale = $0 },
                             labelBottom: CEScaleBar.room)
                CEScaleBar(pointsPerUnit: scale, unit: "nm", preferred: [5, 10, 20], others: [1, 2, 50]).padding(14).allowsHitTesting(false)
                VStack {
                    // (1.3.79) the hovered part's name only while the labels are off: with them on, its label is lit
                    // already, and the name at the top said it a second time
                    if !labels, let h = hovered.flatMap({ CEMembranePage.partID($0) }).flatMap({ CEMembrane.part($0) }) {
                        Text(h.name)
                            .font(.system(size: 12, weight: .semibold))
                            .padding(.horizontal, 10)
                            .padding(.vertical, 4)
                            .background(.regularMaterial, in: Capsule())
                            .transition(.opacity)
                            .accessibilityHidden(true)
                    } else if osmosis {
                        Text("Water moves through the aquaporin from where there is more free water to where there is less: osmosis.")
                            .font(.sCaption)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 5)
                            .background(.regularMaterial, in: Capsule())
                    }
                    Spacer()
                }
                .frame(maxWidth: .infinity)
                .padding(.top, 6)
                .allowsHitTesting(false)
                .animation(reduceMotion ? nil : .easeOut(duration: 0.12), value: hovered)
            }
        }
    }

    @ViewBuilder
    private var right: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let p = selectedPart {
                BioCard(title: p.name, swatch: p.colour.color, subtitle: "Plasma membrane") {
                    CEFactRow(label: p.id.hasPrefix("side:") ? "Which side" : "What it does", markdown: p.function)
                    BioFactRow(label: p.id == "bilayer" ? "Thickness" : "Structure", text: p.structure)
                    if let mix = p.mixup { CEFactRow(label: "Common mix-up", markdown: mix, tint: .orange) }
                    if p.id.hasPrefix("bilayer") {
                        Button { showPOPC = true } label: { Label("See a real phospholipid", systemImage: "atom").frame(maxWidth: .infinity) }
                            .glassButton()
                    }
                    if p.id == "integral:channel" && !osmosis {
                        Button { osmosis = true } label: { Label("Show osmosis", systemImage: "drop").frame(maxWidth: .infinity) }
                            .glassButton()
                    }
                    Button { withAnimation(anim) { selection = nil } } label: { Label("Done", systemImage: "checkmark").frame(maxWidth: .infinity) }
                        .glassButton()
                        .help("Close the card (Escape)")
                }
                .id(p.id)
                .transition(.opacity)
            } else {
                BioCard(title: "The fluid mosaic model", subtitle: "A patch of plasma membrane") {
                    BioFactRow(label: "What it is", text: "A bilayer of phospholipids with proteins drifting in it, like tiles in a moving mosaic; cholesterol among the tails, sugar chains on the outside face.")
                    BioFactRow(label: "Thickness", text: "About 4–5 nm; up to about 10 nm counting the proteins that stick out.")
                    BioFactRow(label: "Crossing it", text: "Small nonpolar molecules (O₂, CO₂) diffuse straight through. Water crosses mostly through aquaporins (osmosis). Ions and sugars need channels or carriers (facilitated diffusion), and pumps that use ATP to move them against their gradient (active transport).")
                    BioFactRow(label: "In this model", text: "60 nm × 60 nm, cut on its two near sides; phospholipids 0.8 nm apart, 12% of their places cholesterol; six proteins that cross the bilayer, three on its inner face.")
                    Button { showPOPC = true } label: { Label("See a real phospholipid", systemImage: "atom").frame(maxWidth: .infinity) }
                        .glassButton()
                }
                .transition(.opacity)
            }
        }
    }

    private func select(_ id: String) {
        withAnimation(anim) { selection = id.hasPrefix("side:") ? id : "part:" + id }
    }

    /// (the screenshot suite) The launch's state told on the console as the page shows it (init sets it).
    private func applyLaunch() {
        guard OpenClock.verbose, !launchTold, let s = state else { return }
        launchTold = true
        NSLog("[Simpl] bio launch: cell %@ on the membrane page: osmosis %@, selected %@", s.raw, osmosis ? "on" : "off", selection ?? "none")
    }
}

/// The membrane's moving parts, as SceneKit nodes over the batched patch: the carrier's seven helices rocking between
/// open-outside and open-inside with a solute bead carried through (3.2 s a cycle), and water beads filing down the
/// aquaporin beside a pulsing arrow. Still (Reduce Motion): the beads in the channel and the arrow, nothing moving.
enum CEMembraneNodes {
    static func nodes(dark: Bool, carrier: Bool, osmosis: Bool, still: Bool) -> [SCNNode] {
        let m = CEMembrane.motion()
        var out: [SCNNode] = []
        func v(_ p: BioVec) -> SCNVector3 { SCNVector3(CGFloat(p.x), CGFloat(p.y), CGFloat(p.z)) }
        func material(_ pair: BioHexPair, roughness: Double = 0.4) -> SCNMaterial {
            BioSceneKit.material(BioMaterial(pair.hex(dark: dark), roughness: roughness))
        }
        if carrier {
            let group = SCNNode()
            group.name = "pick:part:integral:carrier"
            let geometry = BioSceneKit.geometry(m.helix, material(CEMembrane.carrierColour))
            for h in m.carrier {
                let n = SCNNode(geometry: geometry)
                n.position = v(h.centre)
                let outside = SCNVector4(CGFloat(h.outside.axis.x), CGFloat(h.outside.axis.y), CGFloat(h.outside.axis.z), CGFloat(h.outside.angle))
                let inside = SCNVector4(CGFloat(h.inside.axis.x), CGFloat(h.inside.axis.y), CGFloat(h.inside.axis.z), CGFloat(h.inside.angle))
                n.rotation = outside
                let open = SCNAction.rotate(toAxisAngle: inside, duration: 0.5)
                open.timingMode = .easeInEaseOut
                let close = SCNAction.rotate(toAxisAngle: outside, duration: 0.5)
                close.timingMode = .easeInEaseOut
                n.runAction(.repeatForever(.sequence([.wait(duration: 0.9), open, .wait(duration: 1.1), close, .wait(duration: 0.2)])))
                group.addChildNode(n)
            }
            out.append(group)
            let sphere = SCNSphere(radius: 0.34)
            sphere.firstMaterial = material(BioHexPair("#e0a21a", "#ffc94a"), roughness: 0.3)
            let solute = SCNNode(geometry: sphere)
            let p = m.carrierPath.map(v)
            solute.position = p[0]
            solute.opacity = 0
            let go: [SCNAction] = [.move(to: p[0], duration: 0), .fadeIn(duration: 0.2), .move(to: p[1], duration: 0.6), .wait(duration: 0.1),
                                   .move(to: p[2], duration: 0.5), .move(to: p[3], duration: 0.6), .fadeOut(duration: 0.2), .wait(duration: 1.0)]
            solute.runAction(.repeatForever(.sequence(go)))
            out.append(solute)
        }
        if osmosis {
            let (start, end) = m.channelPath
            let water = SCNSphere(radius: 0.16)
            water.firstMaterial = material(CEMembrane.water, roughness: 0.25)
            for k in 0..<5 {
                let n = SCNNode(geometry: water)
                if still {
                    n.position = v(start + (end - start) * (Double(k) + 0.5) / 5)
                } else {
                    n.position = v(start)
                    n.opacity = 0
                    let cycle = SCNAction.sequence([.move(to: v(start), duration: 0), .fadeIn(duration: 0.15), .move(to: v(end), duration: 2.5), .fadeOut(duration: 0.15)])
                    n.runAction(.sequence([.wait(duration: Double(k) * 0.56), .repeatForever(cycle)]))
                }
                out.append(n)
            }
            // (the arrow: in front of the near cut face, pointing from outside to inside)
            let (a, b) = m.arrow
            let len = BioV.distance(a, b), dir = BioV.unit(b - a)
            let arrow = SCNNode()
            let shaft = SCNNode(geometry: SCNCylinder(radius: 0.22, height: CGFloat(len * 0.72)))
            shaft.position = SCNVector3(0, CGFloat(len * 0.36), 0)
            let head = SCNNode(geometry: SCNCone(topRadius: 0, bottomRadius: 0.6, height: CGFloat(len * 0.28)))
            head.position = SCNVector3(0, CGFloat(len * 0.86), 0)
            let mat = material(CEMembrane.water, roughness: 0.3)
            shaft.geometry?.firstMaterial = mat
            head.geometry?.firstMaterial = mat
            arrow.addChildNode(shaft)
            arrow.addChildNode(head)
            arrow.position = v(a)
            arrow.simdOrientation = simd_quatf(from: SIMD3<Float>(0, 1, 0), to: SIMD3<Float>(Float(dir.x), Float(dir.y), Float(dir.z)))
            if !still {
                arrow.runAction(.repeatForever(.sequence([.fadeOpacity(to: 0.45, duration: 0.8), .fadeOpacity(to: 1, duration: 0.8)])))
            }
            out.append(arrow)
        }
        return out
    }
}

/// The quiz's wrong answer (§4.1 Nice: "wrong presses wobble the object"): the part pressed drawn on its own, glowing,
/// each instance shaking side to side about its own centre for half a second (the batched part is left out meanwhile).
enum CEWobble {
    static func nodes(_ m: CEModel, part: String, explode: Double, dark: Bool) -> [SCNNode] {
        var out: [SCNNode] = []
        for inst in m.instances where inst.part == part && !inst.pieces.isEmpty {
            let d = CEScene.displacement(inst, explode: explode, cellRadius: m.cellRadius)
            let c = inst.centre + d
            let holder = SCNNode()
            holder.position = SCNVector3(CGFloat(c.x), CGFloat(c.y), CGFloat(c.z))
            for p in inst.pieces {
                let mesh = CEGeo.moved(p.mesh, d - c)
                let n = BioSceneKit.node(mesh, BioMaterial(p.colour.hex(dark: dark), alpha: p.alpha, roughness: p.roughness, emission: 0.35), order: p.order)
                holder.addChildNode(n)
            }
            let step = CGFloat(max(0.04, min(inst.radius * 0.3, 0.6)))
            holder.runAction(.sequence([.moveBy(x: step, y: 0, z: 0, duration: 0.06), .moveBy(x: -2 * step, y: 0, z: 0, duration: 0.12),
                                        .moveBy(x: 2 * step, y: 0, z: 0, duration: 0.12), .moveBy(x: -2 * step, y: 0, z: 0, duration: 0.12),
                                        .moveBy(x: step, y: 0, z: 0, duration: 0.06)]))
            out.append(holder)
        }
        return out
    }
}

/// "See a real phospholipid": POPC (§6.9), atom by atom, as the Biomolecule Builder draws it.
struct CEPOPCSheet: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let molecule = BioData.molecule("POV")
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .firstTextBaseline) {
                VStack(alignment: .leading, spacing: 3) {
                    Text("POPC, a phospholipid").font(.sTitle3)
                    Text([molecule?.formula, molecule?.mass.map { String(halfUp: "%.1f g/mol", $0) }].compactMap { $0 }.joined(separator: " · "))
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Button("Open in Biomolecule Builder") {
                    dismiss()
                    BioNav.shared.set(.biomol, "lipids")
                    BioTools.open(.biomol, info: ["molecule": "POV"])
                }
                .glassButton()
                Button("Done") { dismiss() }
                    .glassButton(prominent: true)
                    .keyboardShortcut(.defaultAction)
            }
            BioSceneView(key: "popc|\(scheme == .dark)",
                         build: BioSceneBuild {
                             guard let mol = BioData.molecule("POV") else { return BioSceneContent() }
                             return BioSceneContent(items: BioMoleculeScene.items(mol.model(), style: .ballStick))
                         },
                         selection: .constant(nil),
                         showLabels: false,
                         accessibility: "3D model of POPC, a phospholipid, ball and stick")
                .frame(minHeight: 400)
                .card(radius: 16)
            Text("Its head (choline and phosphate) is polar and faces the water; its two fatty-acid tails are not: palmitate is straight, oleate bends at its cis double bond. In the membrane, two layers of these meet tail to tail. Atoms: carbon grey, hydrogen white, oxygen red, nitrogen blue, phosphorus orange.")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(22)
        .frame(width: 680, height: 620)
    }
}

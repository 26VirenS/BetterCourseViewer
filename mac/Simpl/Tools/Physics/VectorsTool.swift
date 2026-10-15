import SwiftUI

// (1.3.90) Vectors: up to six vectors, each typed as components (x, y, and z in 3D) or as a magnitude and an angle from
// +x (2D), each's length and direction listed, their resultant with its length and angle, and for any pair the dot
// product and the angle between, the cross product (in 2D its z component), the unit vectors and the projection of one
// onto the other. A drawing beside: in 2D the arrows from the origin, or tip to tail, with the resultant dashed; in 3D
// an isometric projection drawn with Paths (no SceneKit). Degrees or radians. The arithmetic is Model/PhysVectors.swift's,
// checked by the Models job.
//
// Launch: -SimplTool vectors -SimplVectors "3,4;1,2" -SimplVectorsMode 2d|3d. The pin: two 2D vectors to their
// resultant, Return opening the tool on them.

struct VectorsTool: View {
    @State private var rows: [VectorEntry]
    @State private var threeD: Bool
    @State private var unit: AngleUnit
    @State private var tipToTail: Bool
    /// The pair the products are of, by row.
    @State private var pairA = 0
    @State private var pairB = 1

    init() {
        let launch = Self.launchVectors
        if let launch {
            _rows = State(initialValue: launch.map { VectorEntry($0) })
        } else {
            let kept = VectorEntry.rows(PhysPrefs.string(.vectors, "rows") ?? "")
            _rows = State(initialValue: kept.isEmpty ? [VectorEntry(x: "3", y: "4"), VectorEntry(x: "1", y: "2")] : kept)
        }
        _threeD = State(initialValue: Self.launchMode ?? (launch.map { $0.contains { $0.z != 0 } } ?? PhysPrefs.bool(.vectors, "threeD", default: false)))
        _unit = State(initialValue: PhysPrefs.string(.vectors, "unit").flatMap { AngleUnit(rawValue: $0) } ?? .degrees)
        _tipToTail = State(initialValue: PhysPrefs.bool(.vectors, "tipToTail", default: false))
    }

    // MARK: Opening

    private static var launchedHere: Bool { PhysTools.launched == .vectors }

    private static var launchVectors: [Vec3]? {
        guard launchedHere, let s = UserDefaults.standard.string(forKey: "SimplVectors") else { return nil }
        let vs = VectorList.parse(s)
        return vs.isEmpty ? nil : Array(vs.prefix(VectorNames.most))
    }

    private static var launchMode: Bool? {
        guard launchedHere, let s = UserDefaults.standard.string(forKey: "SimplVectorsMode")?.lowercased() else { return nil }
        return s == "3d" ? true : (s == "2d" ? false : nil)
    }

    /// (the pin's panel opened full screen: its two vectors)
    private func takeHandOff() {
        guard let s = ToolsCenter.shared.takeInfo("vectors") else { return }
        let vs = VectorList.parse(s)
        guard !vs.isEmpty else { return }
        rows = vs.prefix(VectorNames.most).map { VectorEntry($0) }
        threeD = vs.contains { $0.z != 0 }
    }

    // MARK: What is typed

    private var vectors: [Vec3?] { rows.map { $0.vector(threeD: threeD, unit: unit) } }
    private var whole: [(index: Int, v: Vec3)] { vectors.enumerated().compactMap { i, v in v.map { (i, $0) } } }
    private var resultant: Vec3 { Vec3.sum(whole.map(\.v)) }

    var body: some View {
        ToolPage {
            HStack(spacing: 14) {
                Picker("Space", selection: $threeD) {
                    Text("2D").tag(false)
                    Text("3D").tag(true)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(width: 120)
                Picker("Angles", selection: $unit) {
                    Text("Degrees").tag(AngleUnit.degrees)
                    Text("Radians").tag(AngleUnit.radians)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(width: 180)
                if !threeD {
                    Toggle("Tip to tail", isOn: $tipToTail)
                        .toggleStyle(.switch)
                        .controlSize(.small)
                        .help("Draw each vector from the tip of the one before, the resultant from the origin to the last tip")
                }
                Spacer()
            }
            ToolColumns(sideWidth: 400, breakpoint: 1040) {
                VStack(alignment: .leading, spacing: 18) {
                    PhysHeading("The vectors")
                    VStack(spacing: 10) {
                        ForEach(rows.indices, id: \.self) { i in
                            VectorRowView(index: i, row: $rows[i], threeD: threeD, unit: unit, vector: vectors[i], canRemove: rows.count > 1) {
                                withAnimation(Motion.gentle) { rows.remove(at: i) }
                            }
                        }
                    }
                    HStack(spacing: 10) {
                        Button {
                            withAnimation(Motion.gentle) { rows.append(VectorEntry()) }
                        } label: {
                            Label("Add a vector", systemImage: "plus")
                        }
                        .glassButton()
                        .disabled(rows.count >= VectorNames.most)
                        .help(rows.count >= VectorNames.most ? "Six at most" : "Another vector (\(VectorNames.letters[min(rows.count, VectorNames.most - 1)]))")
                        Button {
                            withAnimation(Motion.gentle) { rows = [VectorEntry(), VectorEntry()] }
                        } label: {
                            Label("Clear", systemImage: "xmark.circle")
                        }
                        .glassButton()
                        .disabled(rows.allSatisfy(\.isBlank))
                    }
                    VectorDiagram(vectors: whole, resultant: resultant, threeD: threeD, tipToTail: tipToTail && !threeD)
                        .frame(height: 380)
                }
            } side: {
                results
            }
        }
        .onAppear(perform: takeHandOff)
        .onChange(of: rows) { _, r in PhysPrefs.set(.vectors, "rows", VectorEntry.text(r)) }
        .onChange(of: threeD) { _, on in PhysPrefs.set(.vectors, "threeD", on) }
        .onChange(of: unit) { _, u in PhysPrefs.set(.vectors, "unit", u.rawValue) }
        .onChange(of: tipToTail) { _, on in PhysPrefs.set(.vectors, "tipToTail", on) }
    }

    // MARK: Results

    private func direction(_ v: Vec3) -> String {
        threeD ? "\(unit.text(v.angle)) · \(unit.text(v.elevation)) up" : unit.text(v.angle)
    }

    private var results: some View {
        VStack(alignment: .leading, spacing: 18) {
            VStack(alignment: .leading, spacing: 10) {
                PhysHeading("Each vector")
                if whole.isEmpty {
                    Text("Type a vector's components — or its magnitude and angle — and its length and direction are listed here.")
                        .font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                        .padding(16).frame(maxWidth: .infinity, alignment: .leading).card(radius: 16)
                } else {
                    VStack(spacing: 0) {
                        ForEach(whole.indices, id: \.self) { k in
                            let w = whole[k]
                            PhysValueRow(label: "\(VectorNames.letters[w.index]) \(w.v.text)",
                                         value: "|\(VectorNames.letters[w.index])| = \(PhysMath.text(w.v.magnitude)) at \(direction(w.v))",
                                         color: Color(hex: VectorNames.hexes[w.index]), last: k == whole.count - 1)
                        }
                    }
                    .card(radius: 16)
                }
            }
            if whole.count > 1 {
                VStack(alignment: .leading, spacing: 10) {
                    PhysHeading("Resultant")
                    VStack(alignment: .leading, spacing: 4) {
                        Text("R = \(resultant.text)")
                            .font(.system(size: 26, weight: .bold, design: .rounded).monospacedDigit())
                            .lineLimit(1).minimumScaleFactor(0.5).textSelection(.enabled)
                            .contentTransition(.numericText())
                        Text(resultant.isZero ? "|R| = 0 — they cancel" : "|R| = \(PhysMath.text(resultant.magnitude)) at \(direction(resultant))")
                            .font(.sCallout.monospacedDigit()).foregroundStyle(.secondary)
                        Text("The sum of \(whole.map { VectorNames.letters[$0.index] }.joined(separator: " + "))")
                            .font(.sCaption).foregroundStyle(.secondary)
                    }
                    .padding(16).frame(maxWidth: .infinity, alignment: .leading).card(radius: 16)
                }
                pair
            }
            if !whole.isEmpty {
                Button {
                    copyToPasteboard(copyText)
                } label: {
                    Label("Copy", systemImage: "doc.on.doc")
                }
                .glassButton()
                .help("Copy every vector, the resultant and the pair's products as text")
            }
        }
        .animation(Motion.snappy, value: rows)
    }

    /// The products of two of them, chosen by letter.
    private var pair: some View {
        let a = whole.first { $0.index == pairA }?.v ?? whole[0].v
        let b = whole.first { $0.index == pairB }?.v ?? whole[whole.count > 1 ? 1 : 0].v
        let la = VectorNames.letters[whole.first { $0.index == pairA }?.index ?? whole[0].index]
        let lb = VectorNames.letters[whole.first { $0.index == pairB }?.index ?? whole[whole.count > 1 ? 1 : 0].index]
        let cross = a.cross(b)
        return VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                PhysHeading("A pair")
                Spacer()
                Picker("First", selection: $pairA) {
                    ForEach(whole, id: \.index) { w in Text(VectorNames.letters[w.index]).tag(w.index) }
                }
                .labelsHidden().frame(width: 60)
                Text("and").font(.sCaption).foregroundStyle(.secondary)
                Picker("Second", selection: $pairB) {
                    ForEach(whole, id: \.index) { w in Text(VectorNames.letters[w.index]).tag(w.index) }
                }
                .labelsHidden().frame(width: 60)
            }
            VStack(spacing: 0) {
                PhysValueRow(label: "\(la) · \(lb)", value: PhysMath.text(a.dot(b)))
                PhysValueRow(label: "Angle between", value: a.angle(to: b).map { unit.text($0) } ?? "— (a zero vector)")
                PhysValueRow(label: threeD ? "\(la) × \(lb)" : "\(la) × \(lb) (z)", value: threeD ? cross.text : PhysMath.text(cross.z))
                PhysValueRow(label: "Unit \(la)", value: a.unit?.text ?? "—")
                PhysValueRow(label: "Unit \(lb)", value: b.unit?.text ?? "—")
                PhysValueRow(label: "\(la) onto \(lb)", value: a.projection(onto: b).map { "\($0.text) · \(PhysMath.text(a.scalarProjection(onto: b) ?? 0)) long" } ?? "—", last: true)
            }
            .card(radius: 16)
        }
    }

    private var copyText: String {
        var lines = whole.map { "\(VectorNames.letters[$0.index]) = \($0.v.text): |\(VectorNames.letters[$0.index])| = \(PhysMath.text($0.v.magnitude)), \(direction($0.v))" }
        if whole.count > 1 {
            lines.append("R = \(resultant.text): |R| = \(PhysMath.text(resultant.magnitude)), \(direction(resultant))")
            let a = whole.first { $0.index == pairA }?.v ?? whole[0].v, b = whole.first { $0.index == pairB }?.v ?? whole[1].v
            lines.append("dot \(PhysMath.text(a.dot(b))), angle \(a.angle(to: b).map { unit.text($0) } ?? "—"), cross \(a.cross(b).text)")
        }
        return lines.joined(separator: "\n").replacingOccurrences(of: "−", with: "-")
    }
}

/// One vector's row: its letter on its colour, how it is typed (components or magnitude and angle), its fields, and ×.
private struct VectorRowView: View {
    let index: Int
    @Binding var row: VectorEntry
    let threeD: Bool
    let unit: AngleUnit
    let vector: Vec3?
    let canRemove: Bool
    let remove: () -> Void

    private var color: Color { Color(hex: VectorNames.hexes[index]) }

    var body: some View {
        HStack(alignment: .center, spacing: 10) {
            Text(VectorNames.letters[index])
                .font(.system(size: 15, weight: .bold, design: .rounded))
                .foregroundStyle(.white)
                .frame(width: 30, height: 30)
                .background(color, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                .accessibilityLabel("Vector \(VectorNames.letters[index])")
            if !threeD {
                Picker("How", selection: $row.polar) {
                    Text("x, y").tag(false)
                    Text("r, θ").tag(true)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(width: 100)
                .help("Type its components, or its magnitude and its angle from +x")
            }
            if row.polar && !threeD {
                field("r", $row.magnitude, "10")
                field("θ", $row.angle, unit == .degrees ? "30" : "0.52", unit: unit == .degrees ? "°" : "rad")
            } else {
                field("x", $row.x, "3")
                field("y", $row.y, "4")
                if threeD { field("z", $row.z, "0") }
            }
            Spacer(minLength: 0)
            if let vector {
                Text("|\(VectorNames.letters[index])| = \(PhysMath.text(vector.magnitude))")
                    .font(.sCaption.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            } else if !row.isBlank {
                Text("not whole yet").font(.sCaption).foregroundStyle(.secondary)
            }
            Button(action: remove) {
                Image(systemName: "xmark")
                    .font(.system(size: 11, weight: .semibold))
                    .frame(width: 22, height: 22)
            }
            .buttonStyle(.plain)
            .foregroundStyle(.secondary)
            .disabled(!canRemove)
            .opacity(canRemove ? 1 : 0.3)
            .help("Remove \(VectorNames.letters[index])")
            .accessibilityLabel("Remove vector \(VectorNames.letters[index])")
        }
        .padding(10)
        .card(radius: 14)
    }

    private func field(_ label: String, _ text: Binding<String>, _ placeholder: String, unit: String = "") -> some View {
        HStack(spacing: 4) {
            Text(label).font(.sCallout.weight(.semibold)).foregroundStyle(.secondary)
            TextField(placeholder, text: text)
                .textFieldStyle(.roundedBorder)
                .font(.sBody.monospacedDigit())
                .frame(width: 70)
            if !unit.isEmpty { Text(unit).font(.sCaption).foregroundStyle(.secondary) }
        }
    }
}

// MARK: - The drawing

/// The vectors drawn: in 2D on a grid from the origin (or tip to tail), each in its colour with its letter, the
/// resultant dashed; in 3D in an isometric projection, each with a dashed drop to the xy plane so its height reads.
struct VectorDiagram: View {
    let vectors: [(index: Int, v: Vec3)]
    let resultant: Vec3
    let threeD: Bool
    let tipToTail: Bool

    var body: some View {
        Canvas { ctx, size in
            if threeD { draw3D(ctx, size) } else { draw2D(ctx, size) }
        }
        .padding(6)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .card(radius: 20)
        .accessibilityLabel(vectors.isEmpty ? "No vectors drawn yet" : "\(vectors.count) vectors drawn\(vectors.count > 1 ? ", with their resultant" : "")")
    }

    private func arrow(_ ctx: GraphicsContext, from a: CGPoint, to b: CGPoint, color: Color, dashed: Bool = false, width: CGFloat = 2.5) {
        let dx = b.x - a.x, dy = b.y - a.y
        let len = (dx * dx + dy * dy).squareRoot()
        guard len > 0.5 else {
            ctx.fill(Path(ellipseIn: CGRect(x: a.x - 3, y: a.y - 3, width: 6, height: 6)), with: .color(color))
            return
        }
        var line = Path()
        line.move(to: a)
        line.addLine(to: b)
        ctx.stroke(line, with: .color(color), style: StrokeStyle(lineWidth: width, lineCap: .round, dash: dashed ? [6, 5] : []))
        let head: CGFloat = min(11, len * 0.4)
        let ux = dx / len, uy = dy / len
        var tip = Path()
        tip.move(to: b)
        tip.addLine(to: CGPoint(x: b.x - head * ux + head * 0.5 * uy, y: b.y - head * uy - head * 0.5 * ux))
        tip.addLine(to: CGPoint(x: b.x - head * ux - head * 0.5 * uy, y: b.y - head * uy + head * 0.5 * ux))
        tip.closeSubpath()
        ctx.fill(tip, with: .color(color))
    }

    private func label(_ ctx: GraphicsContext, _ s: String, at p: CGPoint, color: Color) {
        ctx.draw(Text(s).font(.system(size: 12, weight: .bold, design: .rounded)).foregroundStyle(color), at: p, anchor: .center)
    }

    private func draw2D(_ ctx: GraphicsContext, _ size: CGSize) {
        guard size.width > 60, size.height > 60 else { return }
        // (the arrows' tips, tip to tail or not, and the resultant, all in view with room round them; the origin
        // always in view)
        var tips: [Vec3] = [.zero]
        var at = Vec3.zero
        for w in vectors {
            at = tipToTail ? at + w.v : w.v
            tips.append(at)
        }
        if vectors.count > 1 { tips.append(resultant) }
        let xs = tips.map(\.x), ys = tips.map(\.y)
        var minX = min(xs.min() ?? 0, 0), maxX = max(xs.max() ?? 0, 0)
        var minY = min(ys.min() ?? 0, 0), maxY = max(ys.max() ?? 0, 0)
        if maxX - minX < 1e-9 { minX -= 1; maxX += 1 }
        if maxY - minY < 1e-9 { minY -= 1; maxY += 1 }
        let padX = (maxX - minX) * 0.15, padY = (maxY - minY) * 0.15
        minX -= padX; maxX += padX; minY -= padY; maxY += padY
        let inset = CGRect(x: 16, y: 16, width: size.width - 32, height: size.height - 32)
        let scale = min(inset.width / CGFloat(maxX - minX), inset.height / CGFloat(maxY - minY))
        guard scale > 0, scale.isFinite else { return }
        let originX = inset.midX - CGFloat((maxX + minX) / 2) * scale
        let originY = inset.midY + CGFloat((maxY + minY) / 2) * scale
        func pt(_ v: Vec3) -> CGPoint { CGPoint(x: originX + CGFloat(v.x) * scale, y: originY - CGFloat(v.y) * scale) }
        // the grid, a line each unit (or each 2, 5, 10 … where units would crowd)
        var step = 1.0
        while CGFloat(step) * scale < 24 { step *= (String(step).hasPrefix("2") ? 2.5 : 2) }
        var grid = Path()
        var gx = (minX / step).rounded(.down) * step
        while gx <= maxX { grid.move(to: pt(Vec3(gx, minY))); grid.addLine(to: pt(Vec3(gx, maxY))); gx += step }
        var gy = (minY / step).rounded(.down) * step
        while gy <= maxY { grid.move(to: pt(Vec3(minX, gy))); grid.addLine(to: pt(Vec3(maxX, gy))); gy += step }
        ctx.stroke(grid, with: .color(.secondary.opacity(0.12)), lineWidth: 1)
        var axes = Path()
        axes.move(to: pt(Vec3(minX, 0))); axes.addLine(to: pt(Vec3(maxX, 0)))
        axes.move(to: pt(Vec3(0, minY))); axes.addLine(to: pt(Vec3(0, maxY)))
        ctx.stroke(axes, with: .color(.secondary.opacity(0.55)), lineWidth: 1)
        ctx.draw(Text("x").font(.sCaption).foregroundStyle(.secondary), at: CGPoint(x: pt(Vec3(maxX, 0)).x - 8, y: pt(Vec3(maxX, 0)).y + 10), anchor: .center)
        ctx.draw(Text("y").font(.sCaption).foregroundStyle(.secondary), at: CGPoint(x: pt(Vec3(0, maxY)).x - 10, y: pt(Vec3(0, maxY)).y + 8), anchor: .center)
        ctx.draw(Text("grid \(PhysMath.text(step))").font(.sCaption2).foregroundStyle(.tertiary), at: CGPoint(x: inset.maxX, y: inset.maxY), anchor: .bottomTrailing)
        // the vectors
        var from = Vec3.zero
        for w in vectors {
            let start = tipToTail ? from : .zero
            let end = start + w.v
            let color = Color(hex: VectorNames.hexes[w.index])
            arrow(ctx, from: pt(start), to: pt(end), color: color)
            let mid = pt(Vec3((start.x + end.x) / 2, (start.y + end.y) / 2))
            let n = w.v.unit ?? Vec3(0, 1)
            label(ctx, VectorNames.letters[w.index], at: CGPoint(x: mid.x - CGFloat(n.y) * 12, y: mid.y - CGFloat(n.x) * 12), color: color)
            from = end
        }
        if vectors.count > 1 {
            arrow(ctx, from: pt(.zero), to: pt(resultant), color: .primary.opacity(0.8), dashed: true, width: 2)
            label(ctx, "R", at: CGPoint(x: pt(resultant).x + 10, y: pt(resultant).y - 10), color: .primary)
        }
        ctx.fill(Path(ellipseIn: CGRect(x: pt(.zero).x - 3, y: pt(.zero).y - 3, width: 6, height: 6)), with: .color(.secondary))
    }

    private func draw3D(_ ctx: GraphicsContext, _ size: CGSize) {
        guard size.width > 60, size.height > 60 else { return }
        // (an isometric view: +x to the lower right, +y to the lower left — the plane seen from above and in front —
        // and +z straight up)
        let c30 = cos(Double.pi / 6), s30 = sin(Double.pi / 6)
        func flat(_ v: Vec3) -> (Double, Double) { ((v.x - v.y) * c30, -(v.x + v.y) * s30 - v.z) }
        var all = vectors.map(\.v)
        if vectors.count > 1 { all.append(resultant) }
        let reach = max(all.map { max(abs($0.x), abs($0.y), abs($0.z)) }.max() ?? 1, 1e-9)
        let axisLen = reach * 1.15
        var pts = all.map(flat) + [flat(Vec3(axisLen, 0, 0)), flat(Vec3(0, axisLen, 0)), flat(Vec3(0, 0, axisLen)), flat(.zero)]
        pts += all.map { flat(Vec3($0.x, $0.y, 0)) }
        let minX = pts.map(\.0).min() ?? -1, maxX = pts.map(\.0).max() ?? 1
        let minY = pts.map(\.1).min() ?? -1, maxY = pts.map(\.1).max() ?? 1
        let inset = CGRect(x: 20, y: 20, width: size.width - 40, height: size.height - 40)
        let scale = min(inset.width / CGFloat(max(maxX - minX, 1e-9)), inset.height / CGFloat(max(maxY - minY, 1e-9)))
        let ox = inset.midX - CGFloat((maxX + minX) / 2) * scale, oy = inset.midY - CGFloat((maxY + minY) / 2) * scale
        func pt(_ v: Vec3) -> CGPoint {
            let f = flat(v)
            return CGPoint(x: ox + CGFloat(f.0) * scale, y: oy + CGFloat(f.1) * scale)
        }
        // the axes
        let axisColor = Color.secondary.opacity(0.6)
        for (axis, name) in [(Vec3(axisLen, 0, 0), "x"), (Vec3(0, axisLen, 0), "y"), (Vec3(0, 0, axisLen), "z")] {
            arrow(ctx, from: pt(.zero), to: pt(axis), color: axisColor, width: 1)
            let p = pt(axis)
            ctx.draw(Text(name).font(.sCaption).foregroundStyle(.secondary), at: CGPoint(x: p.x + 8, y: p.y - 6), anchor: .center)
        }
        // each vector, its drop to the plane dashed, and its shadow on the plane
        for w in vectors {
            let color = Color(hex: VectorNames.hexes[w.index])
            let foot = Vec3(w.v.x, w.v.y, 0)
            if abs(w.v.z) > 1e-9 {
                var drop = Path()
                drop.move(to: pt(foot)); drop.addLine(to: pt(w.v))
                ctx.stroke(drop, with: .color(color.opacity(0.5)), style: StrokeStyle(lineWidth: 1, dash: [3, 3]))
                var shadow = Path()
                shadow.move(to: pt(.zero)); shadow.addLine(to: pt(foot))
                ctx.stroke(shadow, with: .color(color.opacity(0.3)), style: StrokeStyle(lineWidth: 1, dash: [3, 3]))
            }
            arrow(ctx, from: pt(.zero), to: pt(w.v), color: color)
            let p = pt(w.v)
            label(ctx, VectorNames.letters[w.index], at: CGPoint(x: p.x + 10, y: p.y - 10), color: color)
        }
        if vectors.count > 1 {
            arrow(ctx, from: pt(.zero), to: pt(resultant), color: .primary.opacity(0.8), dashed: true, width: 2)
            let p = pt(resultant)
            label(ctx, "R", at: CGPoint(x: p.x + 10, y: p.y + 10), color: .primary)
        }
        ctx.fill(Path(ellipseIn: CGRect(x: pt(.zero).x - 3, y: pt(.zero).y - 3, width: 6, height: 6)), with: .color(.secondary))
        ctx.draw(Text("isometric").font(.sCaption2).foregroundStyle(.tertiary), at: CGPoint(x: inset.maxX, y: inset.maxY), anchor: .bottomTrailing)
    }
}

// MARK: - The pin

/// Vectors' pin, opened: two 2D vectors to their resultant; Return opens the tool on them.
struct VectorsCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var ax = "3"
    @State private var ay = "4"
    @State private var bx = "1"
    @State private var by = "2"

    private var a: Vec3? { if let x = PhysMath.number(ax), let y = PhysMath.number(ay) { return Vec3(x, y) }; return nil }
    private var b: Vec3? { if let x = PhysMath.number(bx), let y = PhysMath.number(by) { return Vec3(x, y) }; return nil }
    private var list: String { VectorList.text([a, b].compactMap { $0 }, threeD: false) }

    /// (1.3.83) What Open Full Screen carries to the whole tool, once anything is changed from how the panel opens.
    private var carried: [String: String] {
        guard ax != "3" || ay != "4" || bx != "1" || by != "2", a != nil || b != nil else { return [:] }
        return ["vectors": list]
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            row("A", Color(hex: VectorNames.hexes[0]), $ax, $ay)
            row("B", Color(hex: VectorNames.hexes[1]), $bx, $by)
            if let a, let b {
                let r = a + b
                VStack(alignment: .leading, spacing: 2) {
                    Text("A + B = \(r.text)")
                        .font(.system(size: 24, weight: .bold, design: .rounded).monospacedDigit())
                        .lineLimit(1).minimumScaleFactor(0.5).textSelection(.enabled)
                    Text(r.isZero ? "They cancel" : "\(PhysMath.text(r.magnitude)) long at \(PhysMath.withUnit(r.angle, "°")) · A · B = \(PhysMath.text(a.dot(b)))")
                        .font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
                }
            } else {
                Text("Two vectors' components, and their sum is here.").font(.sCallout).foregroundStyle(.secondary)
            }
        }
        .onSubmit { if !list.isEmpty { openFull(["vectors": list]) } }
        .quickCarry(carried)
    }

    private func row(_ name: String, _ color: Color, _ x: Binding<String>, _ y: Binding<String>) -> some View {
        HStack(spacing: 8) {
            Text(name).font(.system(size: 13, weight: .bold, design: .rounded)).foregroundStyle(.white)
                .frame(width: 24, height: 24).background(color, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
            Text("x").font(.sCaption).foregroundStyle(.secondary)
            TextField("0", text: x).textFieldStyle(.roundedBorder).font(.sCallout.monospacedDigit()).frame(width: 64)
            Text("y").font(.sCaption).foregroundStyle(.secondary)
            TextField("0", text: y).textFieldStyle(.roundedBorder).font(.sCallout.monospacedDigit()).frame(width: 64)
        }
    }
}

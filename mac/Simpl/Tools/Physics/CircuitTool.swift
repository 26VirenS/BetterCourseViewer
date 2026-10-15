import AppKit
import SwiftUI
import UniformTypeIdentifiers

// (1.3.90) Circuit Builder: a grid canvas (24 points a cell) with a palette of parts — battery, resistor, wire,
// switch, lamp, ammeter, voltmeter, ground — dragged onto it or added with a press, moved by dragging, turned with
// R, removed with ⌫, and wired by dragging from one terminal to another (parts set end to end join too). The circuit
// is solved as it changes by modified nodal analysis (Model/PhysCircuit.swift, checked by the Models job): every
// part's V, I and P are listed beside the canvas, arrows run along each part with the current, a lamp glows with its
// power, and what cannot be solved — a shorted battery, parts off on their own — is said in plain words. Quick-build
// chips (series, parallel, a voltage divider), Save and Open as JSON, Export PNG, and a calculator strip for Ohm's
// law and a series/parallel list. The circuit and the strip are kept (PhysPrefs circuit.doc, .ohm*, .list).
//
// Launch: -SimplTool circuit -SimplCircuit series|parallel|divider. The pin: the Ohm's law strip and the
// series/parallel list, Return opening the tool with them.

/// The grid, and where things are on it.
enum CircuitGeometry {
    static let cell: CGFloat = 24
    static let origin = CGPoint(x: 24, y: 24)

    static func point(_ x: Int, _ y: Int) -> CGPoint { CGPoint(x: origin.x + CGFloat(x) * cell, y: origin.y + CGFloat(y) * cell) }

    static func cellOf(_ p: CGPoint) -> (x: Int, y: Int) {
        (Int(((p.x - origin.x) / cell).rounded()), Int(((p.y - origin.y) / cell).rounded()))
    }

    /// The terminal under a point, within `radius` points.
    static func terminal(in doc: CircuitDoc, at p: CGPoint, radius: CGFloat = 11) -> TerminalRef? {
        var best: (TerminalRef, CGFloat)?
        for part in doc.parts {
            for (i, t) in part.terminals.enumerated() {
                let q = point(t.x, t.y)
                let d = hypot(q.x - p.x, q.y - p.y)
                if d <= radius, best.map({ d < $0.1 }) ?? true { best = (TerminalRef(part: part.id, index: i), d) }
            }
        }
        return best?.0
    }

    /// The part whose body is under a point: the last drawn first.
    static func part(in doc: CircuitDoc, at p: CGPoint) -> Int? {
        for part in doc.parts.reversed() {
            let c = point(part.x, part.y)
            let dx = p.x - c.x, dy = p.y - c.y
            let (lx, ly) = CircuitPart.turnedBack(dx, dy, by: part.rotation)
            if part.kind == .ground {
                if abs(lx) <= cell * 0.9 && ly >= -cell * 1.1 && ly <= cell * 0.6 { return part.id }
            } else if abs(lx) <= cell * 2.2 && abs(ly) <= cell * 0.8 {
                return part.id
            }
        }
        return nil
    }

    /// A free place for a part added with a press: left to right, row by row, two cells clear of every other.
    static func freeSpot(in doc: CircuitDoc) -> (x: Int, y: Int) {
        for y in stride(from: 2, through: 18, by: 4) {
            for x in stride(from: 4, through: 28, by: 5) where !doc.parts.contains(where: { abs($0.x - x) <= 3 && abs($0.y - y) <= 2 }) {
                return (x, y)
            }
        }
        return (4, 2)
    }

    /// The route of a wire between two terminals: straight when they line up, else one corner.
    static func route(_ a: CGPoint, _ b: CGPoint) -> [CGPoint] {
        if abs(a.x - b.x) < 0.5 || abs(a.y - b.y) < 0.5 { return [a, b] }
        return abs(b.x - a.x) >= abs(b.y - a.y) ? [a, CGPoint(x: b.x, y: a.y), b] : [a, CGPoint(x: a.x, y: b.y), b]
    }
}

extension CircuitPart {
    /// A point offset, turned back out of the part's frame (the inverse of `turned`).
    static func turnedBack(_ dx: CGFloat, _ dy: CGFloat, by rotation: Int) -> (CGFloat, CGFloat) {
        switch ((rotation % 360) + 360) % 360 {
        case 90: return (dy, -dx)
        case 180: return (-dx, -dy)
        case 270: return (-dy, dx)
        default: return (dx, dy)
        }
    }
}

// MARK: - The tool

struct CircuitTool: View {
    @State private var doc: CircuitDoc
    @State private var selected: Int?
    @State private var ohmV: String
    @State private var ohmI: String
    @State private var ohmR: String
    @State private var list: String
    /// What the last Save, Open or Export did.
    @State private var fileNote: String?

    init() {
        if let p = Self.launchPreset {
            _doc = State(initialValue: p)
        } else if let kept = PhysPrefs.string(.circuit, "doc").flatMap({ CircuitDoc(json: $0) }) {
            _doc = State(initialValue: kept)
        } else {
            _doc = State(initialValue: CircuitDoc.preset("series") ?? CircuitDoc())
        }
        _ohmV = State(initialValue: PhysPrefs.string(.circuit, "ohmV") ?? "")
        _ohmI = State(initialValue: PhysPrefs.string(.circuit, "ohmI") ?? "")
        _ohmR = State(initialValue: PhysPrefs.string(.circuit, "ohmR") ?? "")
        _list = State(initialValue: PhysPrefs.string(.circuit, "list") ?? "")
    }

    private static var launchPreset: CircuitDoc? {
        guard PhysTools.launched == .circuit, let s = UserDefaults.standard.string(forKey: "SimplCircuit") else { return nil }
        return CircuitDoc.preset(s.lowercased())
    }

    /// (the pin's panel opened full screen: its strip)
    private func takeHandOff() {
        let tools = ToolsCenter.shared
        if let v = tools.takeInfo("ohmV") { ohmV = v }
        if let i = tools.takeInfo("ohmI") { ohmI = i }
        if let r = tools.takeInfo("ohmR") { ohmR = r }
        if let l = tools.takeInfo("list") { list = l }
    }

    private var solution: CircuitSolution { CircuitSolver.solve(doc) }

    var body: some View {
        let solved = solution
        PTWorkLayout {
            CircuitPalette(doc: $doc, selected: $selected, ohmV: $ohmV, ohmI: $ohmI, ohmR: $ohmR, list: $list)
        } centre: {
            CircuitCanvas(doc: $doc, selected: $selected, solution: solved)
        } right: {
            CircuitSide(doc: $doc, selected: $selected, solution: solved, fileNote: $fileNote)
        }
        .onAppear(perform: takeHandOff)
        .onChange(of: doc) { _, d in PhysPrefs.set(.circuit, "doc", d.json) }
        .onChange(of: ohmV) { _, s in PhysPrefs.set(.circuit, "ohmV", s) }
        .onChange(of: ohmI) { _, s in PhysPrefs.set(.circuit, "ohmI", s) }
        .onChange(of: ohmR) { _, s in PhysPrefs.set(.circuit, "ohmR", s) }
        .onChange(of: list) { _, s in PhysPrefs.set(.circuit, "list", s) }
    }
}

// MARK: - The palette and the strip (left)

private struct CircuitPalette: View {
    @Binding var doc: CircuitDoc
    @Binding var selected: Int?
    @Binding var ohmV: String
    @Binding var ohmI: String
    @Binding var ohmR: String
    @Binding var list: String

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            PhysHeading("Parts")
            Text("Drag a part onto the grid, or click it to add one. Drag from a terminal to another to wire them.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            VStack(spacing: 6) {
                ForEach(CircuitPartKind.allCases, id: \.self) { kind in
                    HStack(spacing: 10) {
                        CircuitPartIcon(kind: kind)
                            .frame(width: 60, height: 28)
                        Text(kind.name).font(.sCallout)
                        Spacer(minLength: 0)
                        if let unit = kind.unit {
                            Text("\(PhysMath.text(kind.defaultValue)) \(unit)").font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
                        }
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .contentShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .onTapGesture { add(kind) }
                    .draggable(kind.rawValue)
                    .help("Add a \(kind.name.lowercased()) — click, or drag it onto the grid")
                    .accessibilityElement(children: .combine)
                    .accessibilityAddTraits(.isButton)
                    .accessibilityLabel("Add a \(kind.name.lowercased())")
                    .accessibilityAction { add(kind) }
                }
            }

            PhysHeading("Quick build")
            VStack(alignment: .leading, spacing: 6) {
                ForEach(CircuitDoc.presets, id: \.id) { p in
                    Button {
                        withAnimation(Motion.gentle) {
                            doc = CircuitDoc.preset(p.id) ?? CircuitDoc()
                            selected = nil
                        }
                    } label: {
                        Text(p.title).font(.sCallout).frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .glassButton()
                }
                Button {
                    withAnimation(Motion.gentle) {
                        doc = CircuitDoc()
                        selected = nil
                    }
                } label: {
                    Label("Clear the grid", systemImage: "xmark.circle").font(.sCallout).frame(maxWidth: .infinity, alignment: .leading)
                }
                .glassButton()
                .disabled(doc.parts.isEmpty)
            }

            PhysHeading("Ohm's law")
            Text("Fill in two of the three.").font(.sCaption).foregroundStyle(.secondary)
            OhmStrip(v: $ohmV, i: $ohmI, r: $ohmR)

            PhysHeading("Series and parallel")
            SeriesParallelStrip(list: $list)
        }
    }

    private func add(_ kind: CircuitPartKind) {
        let spot = CircuitGeometry.freeSpot(in: doc)
        withAnimation(Motion.snappy) {
            selected = doc.add(kind, x: spot.x, y: spot.y)
        }
    }
}

/// Ohm's law: V, I and R, the blank one found from the other two.
struct OhmStrip: View {
    @Binding var v: String
    @Binding var i: String
    @Binding var r: String

    private var answer: (text: String, warn: Bool)? {
        let nv = PhysMath.number(v), ni = PhysMath.number(i), nr = PhysMath.number(r)
        let given = [nv, ni, nr].compactMap { $0 }.count
        if given < 2 { return nil }
        if given == 3 { return ("Leave one blank, and it is found from the other two.", false) }
        guard let o = CircuitMath.ohm(v: nv, i: ni, r: nr) else { return ("Can't divide by 0.", true) }
        switch o.found {
        case "V": return ("V = I × R = \(CircuitMath.volts(o.v))", false)
        case "I": return ("I = V ÷ R = \(CircuitMath.amps(o.i))", false)
        default: return ("R = V ÷ I = \(CircuitMath.ohms(o.r))", false)
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                small("V", $v, "V")
                small("I", $i, "A")
                small("R", $r, "Ω")
            }
            if let a = answer {
                Text(a.text)
                    .font(.sCallout.weight(a.warn ? .regular : .semibold).monospacedDigit())
                    .foregroundStyle(a.warn ? Color.orange : Color.primary)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
        }
    }

    private func small(_ label: String, _ text: Binding<String>, _ unit: String) -> some View {
        HStack(spacing: 3) {
            Text(label).font(.sCallout.weight(.semibold)).foregroundStyle(.secondary)
            TextField(unit, text: text)
                .textFieldStyle(.roundedBorder)
                .font(.sCallout.monospacedDigit())
                .frame(minWidth: 50, maxWidth: 80)
        }
        .help("\(label) in \(unit)")
    }
}

/// A list of resistances, in series and in parallel.
struct SeriesParallelStrip: View {
    @Binding var list: String

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            TextField("100, 200, 1k", text: $list)
                .textFieldStyle(.roundedBorder)
                .font(.sCallout.monospacedDigit())
                .help("Resistances in ohms, separated by commas or spaces; k and M as in 2.2k")
            let rs = CircuitMath.resistances(list)
            if rs.count >= 2 {
                Text("Series \(CircuitMath.ohms(CircuitMath.series(rs)))  ·  Parallel \(CircuitMath.ohms(CircuitMath.parallel(rs) ?? 0))")
                    .font(.sCallout.weight(.semibold).monospacedDigit())
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            } else if rs.count == 1 {
                Text("One resistor: \(CircuitMath.ohms(rs[0])). Add another.").font(.sCaption).foregroundStyle(.secondary)
            } else {
                Text("Two or more resistances, and their equivalents are here.").font(.sCaption).foregroundStyle(.secondary)
            }
        }
    }
}

// MARK: - The readings and the part chosen (right)

private struct CircuitSide: View {
    @Binding var doc: CircuitDoc
    @Binding var selected: Int?
    let solution: CircuitSolution
    @Binding var fileNote: String?
    @State private var valueText = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let id = selected, let part = doc.part(id) {
                PhysHeading("\(doc.names[id] ?? part.kind.letter) · \(part.kind.name)")
                inspector(part)
            }
            PhysHeading("Readings")
            ForEach(solution.problems, id: \.self) { p in
                ToolNote(text: p)
            }
            if doc.parts.isEmpty {
                Text("Add parts and wire them, and each one's volts, amps and watts are listed here.")
                    .font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    .padding(14).frame(maxWidth: .infinity, alignment: .leading).card(radius: 14)
            } else {
                VStack(spacing: 0) {
                    ForEach(Array(doc.parts.enumerated()), id: \.element.id) { i, p in
                        readingRow(p, last: i == doc.parts.count - 1)
                    }
                }
                .card(radius: 14)
            }
            PhysHeading("File")
            HStack(spacing: 8) {
                Button("Save…") { save() }.glassButton().help("Save the circuit as a JSON file")
                Button("Open…") { open() }.glassButton().help("Open a circuit saved as JSON")
                Button("Export PNG…") { exportPNG() }.glassButton().disabled(doc.parts.isEmpty).help("Save a picture of the circuit")
            }
            if let fileNote {
                Text(fileNote).font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
        .onChange(of: selected) { _, id in valueText = id.flatMap { doc.part($0) }.map { PhysMath.text($0.value) } ?? "" }
        .onAppear { valueText = selected.flatMap { doc.part($0) }.map { PhysMath.text($0.value) } ?? "" }
    }

    private func readingRow(_ p: CircuitPart, last: Bool) -> some View {
        let r = solution.reading(p.id)
        let name = doc.names[p.id] ?? p.kind.letter
        var text: String
        if !r.connected {
            text = "not in a circuit"
        } else {
            var parts: [String] = []
            switch p.kind {
            case .battery: parts.append(CircuitMath.volts(p.value))
            case .resistor, .lamp: parts.append(CircuitMath.ohms(p.value))
            case .switch: parts.append(p.closed ? "closed" : "open")
            default: break
            }
            // (the volts across a resistor, lamp or open switch, what a voltmeter reads; the amps through the rest, what
            // an ammeter reads)
            if let v = r.voltage {
                if p.kind == .voltmeter { parts.append("reads \(CircuitMath.volts(v))") }
                else if p.kind == .resistor || p.kind == .lamp || (p.kind == .switch && !p.closed) { parts.append(CircuitMath.volts(v)) }
            }
            if let i = r.current, p.kind != .voltmeter { parts.append(p.kind == .ammeter ? "reads \(CircuitMath.amps(i))" : CircuitMath.amps(abs(i))) }
            if let w = r.power, p.kind == .battery || p.kind == .resistor || p.kind == .lamp { parts.append(CircuitMath.watts(w)) }
            text = parts.joined(separator: " · ")
        }
        return Button {
            withAnimation(Motion.snappy) { selected = selected == p.id ? nil : p.id }
        } label: {
            VStack(spacing: 0) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(name).font(.sCallout.weight(.semibold)).frame(width: 28, alignment: .leading)
                    Text(p.kind.name).font(.sCallout).foregroundStyle(.secondary)
                    Spacer(minLength: 6)
                    Text(text)
                        .font(.sCallout.monospacedDigit())
                        .foregroundStyle(r.connected ? Color.primary : Color.secondary)
                        .multilineTextAlignment(.trailing)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .background(selected == p.id ? Theme.accent.opacity(0.12) : Color.clear)
                if !last { Divider().padding(.leading, 12) }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("Select \(name)")
        .accessibilityLabel("\(name), \(p.kind.name): \(text)")
    }

    @ViewBuilder
    private func inspector(_ part: CircuitPart) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            if let unit = part.kind.unit {
                HStack(spacing: 6) {
                    TextField(PhysMath.text(part.kind.defaultValue), text: $valueText)
                        .textFieldStyle(.roundedBorder)
                        .font(.sCallout.monospacedDigit())
                        .frame(width: 90)
                        .onSubmit { commitValue(part) }
                        .onChange(of: valueText) { _, _ in commitValue(part) }
                    Text(unit).font(.sCallout).foregroundStyle(.secondary)
                    if let v = PhysMath.number(valueText), v < 0 || (part.kind != .battery && v == 0) {
                        Text(part.kind == .battery ? "turn it round instead" : "more than 0").font(.sCaption).foregroundStyle(.orange)
                    }
                }
            }
            if part.kind == .switch {
                Toggle("Closed", isOn: Binding(get: { part.closed }, set: { _ in withAnimation(Motion.snappy) { doc.toggle(part.id) } }))
                    .toggleStyle(.switch)
                    .controlSize(.small)
            }
            HStack(spacing: 8) {
                Button {
                    withAnimation(Motion.snappy) { doc.rotate(part.id) }
                } label: {
                    Label("Rotate", systemImage: "rotate.right")
                }
                .glassButton()
                .help("Turn it a quarter turn (R)")
                Button(role: .destructive) {
                    withAnimation(Motion.gentle) {
                        doc.remove(part.id)
                        selected = nil
                    }
                } label: {
                    Label("Delete", systemImage: "trash")
                }
                .glassButton()
                .help("Remove it and its wires (⌫)")
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 14)
    }

    private func commitValue(_ part: CircuitPart) {
        guard let v = PhysMath.number(valueText), v >= 0, part.kind == .battery || v > 0, v != part.value else { return }
        doc.set(part.id, value: v)
    }

    // MARK: Files

    private func save() {
        guard let url = ToolFiles.saveURL(name: "Circuit.json", type: .json, message: "Save the circuit as a file to open again here.") else { return }
        do {
            try doc.json.write(to: url, atomically: true, encoding: .utf8)
            fileNote = "Saved \(url.lastPathComponent)."
            ToolFiles.reveal([url])
        } catch {
            fileNote = "Couldn't save there: \(error.localizedDescription)"
        }
    }

    private func open() {
        guard let url = ToolFiles.choose(types: [.json], multiple: false, message: "A circuit saved from here.").first else { return }
        if let text = try? String(contentsOf: url, encoding: .utf8), let d = CircuitDoc(json: text) {
            withAnimation(Motion.gentle) {
                doc = d
                selected = nil
            }
            fileNote = "Opened \(url.lastPathComponent)."
        } else {
            fileNote = "Couldn't read \(url.lastPathComponent) as a circuit."
        }
    }

    private func exportPNG() {
        let picture = CircuitPicture(doc: doc, solution: solution, selected: nil, phase: 0.25, export: true)
        let size = picture.exportSize
        let renderer = ImageRenderer(content: picture.frame(width: size.width, height: size.height).background(Color.white))
        renderer.scale = 2
        guard let image = renderer.nsImage, let tiff = image.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff),
              let data = rep.representation(using: .png, properties: [:]) else {
            fileNote = "Couldn't draw the picture."
            return
        }
        guard let url = ToolFiles.saveURL(name: "Circuit.png", type: .png, message: "Save a picture of the circuit.") else { return }
        do {
            try data.write(to: url)
            fileNote = "Exported \(url.lastPathComponent)."
            ToolFiles.reveal([url])
        } catch {
            fileNote = "Couldn't save there: \(error.localizedDescription)"
        }
    }
}

// MARK: - The canvas

/// What a drag on the canvas is doing.
private enum CircuitDrag: Equatable {
    case move(Int, CGSize)
    case wire(TerminalRef, CGPoint)
    case nothing
}

/// The grid with its parts and wires, drawn and worked by the pointer and the keyboard.
private struct CircuitCanvas: View {
    @Binding var doc: CircuitDoc
    @Binding var selected: Int?
    let solution: CircuitSolution
    @State private var drag: CircuitDrag?
    @FocusState private var focused: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var hasCurrent: Bool { solution.readings.values.contains { abs($0.current ?? 0) > 1e-9 } }

    /// The document as shown mid-drag: the part being moved drawn where it would drop, its wires following.
    private var shown: CircuitDoc {
        guard case .move(let id, let offset) = drag, let p = doc.part(id) else { return doc }
        var d = doc
        let c = CircuitGeometry.point(p.x, p.y)
        let cell = CircuitGeometry.cellOf(CGPoint(x: c.x + offset.width, y: c.y + offset.height))
        d.move(id, to: max(cell.x, 2), max(cell.y, 1))
        return d
    }

    var body: some View {
        TimelineView(.animation(paused: reduceMotion || !hasCurrent)) { context in
            let phase = reduceMotion ? 0.25 : (context.date.timeIntervalSinceReferenceDate * 0.6).truncatingRemainder(dividingBy: 1)
            CircuitPicture(doc: shown, solution: solution, selected: selected, phase: phase, export: false, wiring: wiring)
        }
        .contentShape(Rectangle())
        .gesture(dragGesture)
        .dropDestination(for: String.self) { items, location in
            guard let raw = items.first, let kind = CircuitPartKind(rawValue: raw) else { return false }
            let cell = CircuitGeometry.cellOf(location)
            withAnimation(Motion.snappy) { selected = doc.add(kind, x: max(cell.x, 2), y: max(cell.y, 1)) }
            focused = true
            return true
        }
        .focusable()
        .focusEffectDisabled()
        .focused($focused)
        .onKeyPress(characters: CharacterSet(charactersIn: "rR")) { _ in
            guard let id = selected else { return .ignored }
            withAnimation(Motion.snappy) { doc.rotate(id) }
            return .handled
        }
        .onKeyPress(.delete) { deleteSelected() ? .handled : .ignored }
        .onKeyPress(.deleteForward) { deleteSelected() ? .handled : .ignored }
        .onDeleteCommand { _ = deleteSelected() }
        .overlay(alignment: .topTrailing) {
            Text(doc.parts.isEmpty ? "Drag parts here" : "R turns the part chosen · ⌫ removes it")
                .font(.sCaption2)
                .foregroundStyle(.tertiary)
                .padding(10)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .card(radius: 20)
        .overlay {
            RoundedRectangle(cornerRadius: 20, style: .continuous)
                .strokeBorder(Theme.accent.opacity(focused ? 0.35 : 0), lineWidth: 1.5)
                .allowsHitTesting(false)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(doc.parts.isEmpty ? "An empty grid" : "A circuit of \(toolPlural(doc.parts.count, "part")) and \(toolPlural(doc.links.count, "wire"))")
    }

    private var wiring: (from: CGPoint, to: CGPoint)? {
        guard case .wire(let t, let to) = drag, let p = doc.place(of: t) else { return nil }
        return (CircuitGeometry.point(p.x, p.y), to)
    }

    private var dragGesture: some Gesture {
        DragGesture(minimumDistance: 0)
            .onChanged { value in
                if drag == nil {
                    if let t = CircuitGeometry.terminal(in: doc, at: value.startLocation) {
                        drag = .wire(t, value.location)
                    } else if let id = CircuitGeometry.part(in: doc, at: value.startLocation) {
                        drag = .move(id, .zero)
                    } else {
                        drag = .nothing
                    }
                }
                switch drag {
                case .move(let id, _): drag = .move(id, value.translation)
                case .wire(let t, _): drag = .wire(t, value.location)
                default: break
                }
            }
            .onEnded { value in
                let moved = hypot(value.translation.width, value.translation.height) > 3
                switch drag {
                case .move(let id, _):
                    if moved {
                        let d = shown
                        if let p = d.part(id) { withAnimation(Motion.snappy) { doc.move(id, to: p.x, p.y) } }
                        selected = id
                    } else {
                        // (a click: the part chosen; a switch thrown as well)
                        withAnimation(Motion.snappy) {
                            selected = id
                            doc.toggle(id)
                        }
                    }
                case .wire(let from, let to):
                    if moved, let t = CircuitGeometry.terminal(in: doc, at: to, radius: 14), t != from {
                        withAnimation(Motion.snappy) { _ = doc.link(from, t) }
                    }
                    selected = from.part
                case .nothing:
                    if !moved { selected = nil }
                case nil:
                    break
                }
                drag = nil
                focused = true
            }
    }

    private func deleteSelected() -> Bool {
        guard let id = selected else { return false }
        withAnimation(Motion.gentle) {
            doc.remove(id)
            selected = nil
        }
        return true
    }
}

// MARK: - The drawing

/// The circuit drawn: the grid, the wires, each part with its name, value and reading, arrows along it with the
/// current (`phase` runs them), a lamp's glow, the part chosen outlined, and a wire being drawn. `export` draws it in
/// black on white for a PNG.
struct CircuitPicture: View {
    let doc: CircuitDoc
    let solution: CircuitSolution
    let selected: Int?
    let phase: Double
    var export = false
    var wiring: (from: CGPoint, to: CGPoint)? = nil

    private static let accent = Color(hex: "#0a84ff")
    private static let lampGlow = Color(hex: "#ffd60a")

    /// The size a PNG of it wants: the parts and their labels, with a margin.
    var exportSize: CGSize {
        let xs = doc.parts.map(\.x), ys = doc.parts.map(\.y)
        let w = CircuitGeometry.origin.x + CGFloat((xs.max() ?? 10) + 4) * CircuitGeometry.cell + 24
        let h = CircuitGeometry.origin.y + CGFloat((ys.max() ?? 8) + 3) * CircuitGeometry.cell + 24
        return CGSize(width: max(w, 320), height: max(h, 240))
    }

    private var ink: Color { export ? .black : .primary }
    private var faint: Color { export ? Color.black.opacity(0.5) : .secondary }

    var body: some View {
        Canvas { ctx, size in
            let cell = CircuitGeometry.cell
            // the grid
            var grid = Path()
            var x = CircuitGeometry.origin.x.truncatingRemainder(dividingBy: cell)
            while x <= size.width { grid.move(to: CGPoint(x: x, y: 0)); grid.addLine(to: CGPoint(x: x, y: size.height)); x += cell }
            var y = CircuitGeometry.origin.y.truncatingRemainder(dividingBy: cell)
            while y <= size.height { grid.move(to: CGPoint(x: 0, y: y)); grid.addLine(to: CGPoint(x: size.width, y: y)); y += cell }
            ctx.stroke(grid, with: .color(ink.opacity(export ? 0.06 : 0.07)), lineWidth: 0.5)
            // the wires
            for l in doc.links {
                guard let a = doc.place(of: l.a), let b = doc.place(of: l.b) else { continue }
                let pts = CircuitGeometry.route(CircuitGeometry.point(a.x, a.y), CircuitGeometry.point(b.x, b.y))
                var p = Path()
                p.move(to: pts[0])
                for q in pts.dropFirst() { p.addLine(to: q) }
                ctx.stroke(p, with: .color(ink.opacity(0.85)), style: StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round))
            }
            // the parts
            let names = doc.names
            for part in doc.parts {
                drawPart(ctx, part, name: names[part.id] ?? "", reading: solution.reading(part.id))
            }
            // the terminals: a dot each, filled where something is joined to it
            for part in doc.parts {
                for (i, t) in part.terminals.enumerated() {
                    let p = CircuitGeometry.point(t.x, t.y)
                    let joined = !doc.neighbours(of: TerminalRef(part: part.id, index: i)).isEmpty
                    let dot = Path(ellipseIn: CGRect(x: p.x - 3.5, y: p.y - 3.5, width: 7, height: 7))
                    if joined {
                        ctx.fill(dot, with: .color(ink))
                    } else {
                        ctx.fill(dot, with: .color(export ? .white : Theme.card))
                        ctx.stroke(dot, with: .color(ink.opacity(0.8)), lineWidth: 1.5)
                    }
                }
            }
            // a wire being drawn
            if let w = wiring {
                var p = Path()
                p.move(to: w.from)
                p.addLine(to: w.to)
                ctx.stroke(p, with: .color(Self.accent), style: StrokeStyle(lineWidth: 2, lineCap: .round, dash: [5, 4]))
                ctx.fill(Path(ellipseIn: CGRect(x: w.to.x - 4, y: w.to.y - 4, width: 8, height: 8)), with: .color(Self.accent))
            }
        }
    }

    /// One part in its own frame: its leads and symbol along x from −2 to +2 cells, its name and value beside it, its
    /// reading on the other side, its arrows, and its outline when chosen.
    private func drawPart(_ ctx: GraphicsContext, _ part: CircuitPart, name: String, reading: CircuitReading) {
        let cell = CircuitGeometry.cell
        let c = CircuitGeometry.point(part.x, part.y)
        var g = ctx
        g.translateBy(x: c.x, y: c.y)
        g.rotate(by: .degrees(Double(part.rotation)))
        let line = StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round)
        let stroke = GraphicsContext.Shading.color(ink.opacity(0.9))
        func lead(_ from: CGFloat, _ to: CGFloat) {
            var p = Path()
            p.move(to: CGPoint(x: from, y: 0))
            p.addLine(to: CGPoint(x: to, y: 0))
            g.stroke(p, with: stroke, style: line)
        }
        switch part.kind {
        case .wire:
            lead(-2 * cell, 2 * cell)
        case .resistor:
            lead(-2 * cell, -1.4 * cell)
            lead(1.4 * cell, 2 * cell)
            var zig = Path()
            zig.move(to: CGPoint(x: -1.4 * cell, y: 0))
            let n = 6
            for k in 0..<n {
                let x0 = -1.4 * cell + 2.8 * cell * CGFloat(k) / CGFloat(n)
                let x1 = -1.4 * cell + 2.8 * cell * (CGFloat(k) + 0.5) / CGFloat(n)
                zig.addLine(to: CGPoint(x: (x0 + x1) / 2, y: k % 2 == 0 ? -7 : 7))
            }
            zig.addLine(to: CGPoint(x: 1.4 * cell, y: 0))
            g.stroke(zig, with: stroke, style: line)
        case .battery:
            lead(-2 * cell, -0.3 * cell)
            lead(0.3 * cell, 2 * cell)
            var plates = Path()
            plates.move(to: CGPoint(x: -0.3 * cell, y: -7)); plates.addLine(to: CGPoint(x: -0.3 * cell, y: 7))
            g.stroke(plates, with: stroke, style: StrokeStyle(lineWidth: 4, lineCap: .butt))
            var long = Path()
            long.move(to: CGPoint(x: 0.3 * cell, y: -13)); long.addLine(to: CGPoint(x: 0.3 * cell, y: 13))
            g.stroke(long, with: stroke, style: StrokeStyle(lineWidth: 1.5, lineCap: .butt))
            g.draw(Text("+").font(.system(size: 11, weight: .bold)).foregroundStyle(faint), at: CGPoint(x: 0.9 * cell, y: -12), anchor: .center)
        case .lamp:
            lead(-2 * cell, -0.7 * cell)
            lead(0.7 * cell, 2 * cell)
            let bright = CircuitSolution.brightness(power: reading.power)
            if bright > 0.02 {
                g.fill(Path(ellipseIn: CGRect(x: -1.3 * cell, y: -1.3 * cell, width: 2.6 * cell, height: 2.6 * cell)), with: .color(Self.lampGlow.opacity(0.35 * bright)))
            }
            let bulb = Path(ellipseIn: CGRect(x: -0.7 * cell, y: -0.7 * cell, width: 1.4 * cell, height: 1.4 * cell))
            g.fill(bulb, with: .color(Self.lampGlow.opacity(0.1 + 0.8 * bright)))
            g.stroke(bulb, with: stroke, style: line)
            var cross = Path()
            let r = 0.7 * cell * 0.7071
            cross.move(to: CGPoint(x: -r, y: -r)); cross.addLine(to: CGPoint(x: r, y: r))
            cross.move(to: CGPoint(x: -r, y: r)); cross.addLine(to: CGPoint(x: r, y: -r))
            g.stroke(cross, with: stroke, style: StrokeStyle(lineWidth: 1.5, lineCap: .round))
        case .switch:
            lead(-2 * cell, -0.9 * cell)
            lead(0.9 * cell, 2 * cell)
            var blade = Path()
            blade.move(to: CGPoint(x: -0.9 * cell, y: 0))
            blade.addLine(to: part.closed ? CGPoint(x: 0.9 * cell, y: 0) : CGPoint(x: 0.75 * cell, y: -14))
            g.stroke(blade, with: stroke, style: line)
            g.fill(Path(ellipseIn: CGRect(x: -0.9 * cell - 3, y: -3, width: 6, height: 6)), with: stroke)
            g.fill(Path(ellipseIn: CGRect(x: 0.9 * cell - 3, y: -3, width: 6, height: 6)), with: stroke)
        case .ammeter, .voltmeter:
            lead(-2 * cell, -0.7 * cell)
            lead(0.7 * cell, 2 * cell)
            let ring = Path(ellipseIn: CGRect(x: -0.7 * cell, y: -0.7 * cell, width: 1.4 * cell, height: 1.4 * cell))
            g.fill(ring, with: .color(export ? .white : Theme.card))
            g.stroke(ring, with: stroke, style: line)
            // (the letter upright whatever the turn)
            var letterFrame = g
            letterFrame.rotate(by: .degrees(-Double(part.rotation)))
            letterFrame.draw(Text(part.kind == .ammeter ? "A" : "V").font(.system(size: 13, weight: .bold)).foregroundStyle(ink), at: .zero, anchor: .center)
        case .ground:
            var p = Path()
            p.move(to: CGPoint(x: 0, y: -cell)); p.addLine(to: CGPoint(x: 0, y: 0))
            p.move(to: CGPoint(x: -12, y: 0)); p.addLine(to: CGPoint(x: 12, y: 0))
            p.move(to: CGPoint(x: -8, y: 5)); p.addLine(to: CGPoint(x: 8, y: 5))
            p.move(to: CGPoint(x: -4, y: 10)); p.addLine(to: CGPoint(x: 4, y: 10))
            g.stroke(p, with: stroke, style: line)
        }
        // the current's arrows, above the part, running the way it flows
        if let i = reading.current, abs(i) > 1e-9, part.kind != .voltmeter, part.kind != .ground {
            let forward = i > 0
            for k in 0..<2 {
                var s = (phase + Double(k) * 0.5).truncatingRemainder(dividingBy: 1)
                if !forward { s = 1 - s }
                let x = -2 * cell + 4 * cell * CGFloat(s)
                let dir: CGFloat = forward ? 1 : -1
                var head = Path()
                head.move(to: CGPoint(x: x + 5 * dir, y: -13))
                head.addLine(to: CGPoint(x: x - 3 * dir, y: -17))
                head.addLine(to: CGPoint(x: x - 3 * dir, y: -9))
                head.closeSubpath()
                g.fill(head, with: .color(Self.accent.opacity(export ? 0.9 : 0.85)))
            }
        }
        // the outline when chosen
        if selected == part.id {
            let box = part.kind == .ground ? CGRect(x: -0.9 * cell, y: -1.2 * cell, width: 1.8 * cell, height: 1.8 * cell)
                                           : CGRect(x: -2.2 * cell, y: -0.9 * cell, width: 4.4 * cell, height: 1.8 * cell)
            g.stroke(Path(roundedRect: box, cornerRadius: 8), with: .color(Self.accent.opacity(0.9)), style: StrokeStyle(lineWidth: 1.5, dash: [4, 3]))
        }
        // the name and value on one side, the reading on the other — upright, beside a part on its side
        let upright = part.rotation == 0 || part.rotation == 180
        var label = name
        if part.kind.unit != nil { label += " \(part.kind == .battery ? CircuitMath.volts(part.value) : CircuitMath.ohms(part.value))" }
        if part.kind == .switch { label += part.closed ? " closed" : " open" }
        var readout: String?
        if reading.connected {
            switch part.kind {
            case .ammeter: readout = reading.current.map { CircuitMath.amps($0) }
            case .voltmeter: readout = reading.voltage.map { CircuitMath.volts($0) }
            case .battery: readout = reading.current.map { CircuitMath.amps($0) }
            case .resistor, .lamp:
                if let v = reading.voltage, let i = reading.current { readout = "\(CircuitMath.volts(v)) · \(CircuitMath.amps(abs(i)))" }
            case .wire, .switch:
                readout = reading.current.map { CircuitMath.amps(abs($0)) }
            case .ground: readout = nil
            }
        } else if part.kind != .ground {
            readout = "not in a circuit"
        }
        let labelText = Text(label).font(.system(size: 11, weight: .semibold)).foregroundStyle(ink.opacity(0.85))
        let readText = readout.map { Text($0).font(.system(size: 11, weight: .medium).monospacedDigit()).foregroundStyle(reading.connected ? Self.accent : faint) }
        if part.kind == .ground {
            ctx.draw(labelText, at: CGPoint(x: c.x + 18, y: c.y + 6), anchor: .leading)
        } else if upright {
            ctx.draw(labelText, at: CGPoint(x: c.x, y: c.y - 22), anchor: .bottom)
            if let readText { ctx.draw(readText, at: CGPoint(x: c.x, y: c.y + 20), anchor: .top) }
        } else {
            ctx.draw(labelText, at: CGPoint(x: c.x + 26, y: c.y - 7), anchor: .leading)
            if let readText { ctx.draw(readText, at: CGPoint(x: c.x + 26, y: c.y + 8), anchor: .leading) }
        }
    }
}

/// A part's symbol small, for the palette.
struct CircuitPartIcon: View {
    let kind: CircuitPartKind

    var body: some View {
        Canvas { ctx, size in
            var g = ctx
            g.translateBy(x: size.width / 2, y: size.height / 2 + (kind == .ground ? 6 : 0))
            g.scaleBy(x: 0.55, y: 0.55)
            CircuitPictureSymbols.draw(g, kind: kind)
        }
        .accessibilityHidden(true)
    }
}

/// The symbols alone (no labels, no readings), for the palette's icons.
enum CircuitPictureSymbols {
    static func draw(_ ctx: GraphicsContext, kind: CircuitPartKind) {
        let cell = CircuitGeometry.cell
        let g = ctx
        let stroke = GraphicsContext.Shading.color(.primary.opacity(0.9))
        let line = StrokeStyle(lineWidth: 3, lineCap: .round, lineJoin: .round)
        func lead(_ from: CGFloat, _ to: CGFloat) {
            var p = Path()
            p.move(to: CGPoint(x: from, y: 0))
            p.addLine(to: CGPoint(x: to, y: 0))
            g.stroke(p, with: stroke, style: line)
        }
        switch kind {
        case .wire: lead(-2 * cell, 2 * cell)
        case .resistor:
            lead(-2 * cell, -1.4 * cell); lead(1.4 * cell, 2 * cell)
            var zig = Path()
            zig.move(to: CGPoint(x: -1.4 * cell, y: 0))
            for k in 0..<6 {
                let x = -1.4 * cell + 2.8 * cell * (CGFloat(k) + 0.25) / 6
                zig.addLine(to: CGPoint(x: x, y: k % 2 == 0 ? -8 : 8))
            }
            zig.addLine(to: CGPoint(x: 1.4 * cell, y: 0))
            g.stroke(zig, with: stroke, style: line)
        case .battery:
            lead(-2 * cell, -0.3 * cell); lead(0.3 * cell, 2 * cell)
            var p = Path()
            p.move(to: CGPoint(x: -0.3 * cell, y: -8)); p.addLine(to: CGPoint(x: -0.3 * cell, y: 8))
            g.stroke(p, with: stroke, style: StrokeStyle(lineWidth: 6, lineCap: .butt))
            var q = Path()
            q.move(to: CGPoint(x: 0.3 * cell, y: -15)); q.addLine(to: CGPoint(x: 0.3 * cell, y: 15))
            g.stroke(q, with: stroke, style: StrokeStyle(lineWidth: 2.5, lineCap: .butt))
        case .lamp:
            lead(-2 * cell, -0.7 * cell); lead(0.7 * cell, 2 * cell)
            let bulb = Path(ellipseIn: CGRect(x: -0.7 * cell, y: -0.7 * cell, width: 1.4 * cell, height: 1.4 * cell))
            g.fill(bulb, with: .color(Color(hex: "#ffd60a").opacity(0.5)))
            g.stroke(bulb, with: stroke, style: line)
            let r = 0.7 * cell * 0.7071
            var cross = Path()
            cross.move(to: CGPoint(x: -r, y: -r)); cross.addLine(to: CGPoint(x: r, y: r))
            cross.move(to: CGPoint(x: -r, y: r)); cross.addLine(to: CGPoint(x: r, y: -r))
            g.stroke(cross, with: stroke, style: StrokeStyle(lineWidth: 2.5, lineCap: .round))
        case .switch:
            lead(-2 * cell, -0.9 * cell); lead(0.9 * cell, 2 * cell)
            var blade = Path()
            blade.move(to: CGPoint(x: -0.9 * cell, y: 0)); blade.addLine(to: CGPoint(x: 0.75 * cell, y: -15))
            g.stroke(blade, with: stroke, style: line)
            g.fill(Path(ellipseIn: CGRect(x: -0.9 * cell - 4, y: -4, width: 8, height: 8)), with: stroke)
            g.fill(Path(ellipseIn: CGRect(x: 0.9 * cell - 4, y: -4, width: 8, height: 8)), with: stroke)
        case .ammeter, .voltmeter:
            lead(-2 * cell, -0.7 * cell); lead(0.7 * cell, 2 * cell)
            let ring = Path(ellipseIn: CGRect(x: -0.7 * cell, y: -0.7 * cell, width: 1.4 * cell, height: 1.4 * cell))
            g.stroke(ring, with: stroke, style: line)
            g.draw(Text(kind == .ammeter ? "A" : "V").font(.system(size: 20, weight: .bold)).foregroundStyle(.primary), at: .zero, anchor: .center)
        case .ground:
            var p = Path()
            p.move(to: CGPoint(x: 0, y: -cell)); p.addLine(to: CGPoint(x: 0, y: 0))
            p.move(to: CGPoint(x: -14, y: 0)); p.addLine(to: CGPoint(x: 14, y: 0))
            p.move(to: CGPoint(x: -9, y: 7)); p.addLine(to: CGPoint(x: 9, y: 7))
            p.move(to: CGPoint(x: -4, y: 14)); p.addLine(to: CGPoint(x: 4, y: 14))
            g.stroke(p, with: stroke, style: line)
        }
    }
}

// MARK: - The pin

/// Circuit Builder's pin, opened: Ohm's law from two of V, I and R, and a list of resistances in series and in
/// parallel; Return opens the tool with them.
struct CircuitCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var v = "9"
    @State private var i = ""
    @State private var r = "300"
    @State private var list = "100, 200"

    /// (1.3.83) What Open Full Screen carries to the whole tool, once anything is changed from how the panel opens.
    private var carried: [String: String] {
        guard v != "9" || !i.isEmpty || r != "300" || list != "100, 200" else { return [:] }
        return all
    }

    private var all: [String: String] { ["ohmV": v, "ohmI": i, "ohmR": r, "list": list] }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            PhysHeading("Ohm's law")
            OhmStrip(v: $v, i: $i, r: $r)
            PhysHeading("Series and parallel")
            SeriesParallelStrip(list: $list)
        }
        .onSubmit { openFull(all) }
        .quickCarry(carried)
    }
}

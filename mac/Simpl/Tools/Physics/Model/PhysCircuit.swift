import Foundation

// (tools) The Circuit Builder's model and solver, Foundation only, so the Models job checks it
// (scripts/dev/swift-checks/main.swift): the parts on the grid with their terminals and the links between them
// (CircuitDoc, kept as JSON); modified nodal analysis of the DC circuit they make (CircuitSolver: node voltages and
// branch currents, every part's V, I and P, and what cannot be solved — a shorted battery, parts off on their own);
// the three quick-build presets; and the calculator strip's arithmetic (Ohm's law, series and parallel). The tool
// itself is Physics/CircuitTool.swift.

/// A kind of part. Two terminals each, two grid units either side of the centre, but the ground's one.
enum CircuitPartKind: String, Codable, CaseIterable {
    case battery, resistor, wire, `switch`, lamp, ammeter, voltmeter, ground

    var name: String {
        switch self {
        case .battery: return "Battery"
        case .resistor: return "Resistor"
        case .wire: return "Wire"
        case .switch: return "Switch"
        case .lamp: return "Lamp"
        case .ammeter: return "Ammeter"
        case .voltmeter: return "Voltmeter"
        case .ground: return "Ground"
        }
    }

    /// The letter its parts are numbered by (B1, R2, L1 …).
    var letter: String {
        switch self {
        case .battery: return "B"
        case .resistor: return "R"
        case .wire: return "W"
        case .switch: return "S"
        case .lamp: return "L"
        case .ammeter: return "A"
        case .voltmeter: return "V"
        case .ground: return "G"
        }
    }

    /// What its value is, if it has one: a battery's volts, a resistor's or lamp's ohms.
    var unit: String? {
        switch self {
        case .battery: return "V"
        case .resistor, .lamp: return "Ω"
        default: return nil
        }
    }

    var defaultValue: Double {
        switch self {
        case .battery: return 9
        case .resistor: return 100
        case .lamp: return 50
        default: return 0
        }
    }

    var terminalCount: Int { self == .ground ? 1 : 2 }

    /// Whether current flows through it with no resistance: its two ends are one node.
    var joins: Bool { self == .wire }
}

/// A terminal of a part, by the part's id and the terminal's index (0 the left end, 1 the right end at rotation 0;
/// a battery's 1 is its +).
struct TerminalRef: Codable, Hashable {
    var part: Int
    var index: Int
}

/// A part on the grid: its centre in grid units, its turn (0, 90, 180 or 270 degrees clockwise), its value, and for
/// a switch whether it is closed.
struct CircuitPart: Codable, Equatable, Identifiable {
    var id: Int
    var kind: CircuitPartKind
    var x: Int
    var y: Int
    var rotation: Int = 0
    var value: Double = 0
    var closed: Bool = true

    init(id: Int, kind: CircuitPartKind, x: Int, y: Int, rotation: Int = 0, value: Double? = nil, closed: Bool = true) {
        self.id = id
        self.kind = kind
        self.x = x
        self.y = y
        self.rotation = ((rotation % 360) + 360) % 360
        self.value = value ?? kind.defaultValue
        self.closed = closed
    }

    /// The terminals' places in grid units: two units either side of the centre, turned with the part; the ground's
    /// one unit above.
    var terminals: [(x: Int, y: Int)] {
        let offsets: [(Int, Int)] = kind == .ground ? [(0, -1)] : [(-2, 0), (2, 0)]
        return offsets.map { o in
            let (dx, dy) = Self.turned(o.0, o.1, by: rotation)
            return (x + dx, y + dy)
        }
    }

    /// A grid offset turned clockwise (y down, as on the screen).
    static func turned(_ dx: Int, _ dy: Int, by rotation: Int) -> (Int, Int) {
        switch ((rotation % 360) + 360) % 360 {
        case 90: return (-dy, dx)
        case 180: return (-dx, -dy)
        case 270: return (dy, -dx)
        default: return (dx, dy)
        }
    }

    /// Whether current can flow through it with no resistance just now (a wire, a closed switch).
    var joinsNow: Bool { kind.joins || (kind == .switch && closed) }
}

/// A wire drawn from one terminal to another.
struct CircuitLink: Codable, Equatable, Identifiable {
    var id: Int
    var a: TerminalRef
    var b: TerminalRef
}

/// The circuit as built: its parts and links, and the next id to give. Kept as JSON (the tool's state, and a saved
/// file).
struct CircuitDoc: Codable, Equatable {
    var parts: [CircuitPart] = []
    var links: [CircuitLink] = []
    var nextID = 1

    init(parts: [CircuitPart] = [], links: [CircuitLink] = []) {
        self.parts = parts
        self.links = links
        nextID = (parts.map(\.id) + links.map(\.id)).max().map { $0 + 1 } ?? 1
    }

    func part(_ id: Int) -> CircuitPart? { parts.first { $0.id == id } }

    /// Each part's name: its kind's letter and its number among its kind, in order of adding (B1, R1, R2 …).
    var names: [Int: String] {
        var counts: [CircuitPartKind: Int] = [:]
        var out: [Int: String] = [:]
        for p in parts {
            counts[p.kind, default: 0] += 1
            out[p.id] = "\(p.kind.letter)\(counts[p.kind] ?? 1)"
        }
        return out
    }

    @discardableResult
    mutating func add(_ kind: CircuitPartKind, x: Int, y: Int, rotation: Int = 0, value: Double? = nil) -> Int {
        let id = nextID
        nextID += 1
        parts.append(CircuitPart(id: id, kind: kind, x: x, y: y, rotation: rotation, value: value))
        return id
    }

    /// A link between two terminals, unless it is one already or both ends are the same terminal.
    @discardableResult
    mutating func link(_ a: TerminalRef, _ b: TerminalRef) -> Bool {
        guard a != b, part(a.part) != nil, part(b.part) != nil else { return false }
        guard !links.contains(where: { ($0.a == a && $0.b == b) || ($0.a == b && $0.b == a) }) else { return false }
        links.append(CircuitLink(id: nextID, a: a, b: b))
        nextID += 1
        return true
    }

    mutating func remove(_ id: Int) {
        parts.removeAll { $0.id == id }
        links.removeAll { $0.a.part == id || $0.b.part == id }
    }

    mutating func removeLink(_ id: Int) { links.removeAll { $0.id == id } }

    mutating func rotate(_ id: Int) {
        guard let i = parts.firstIndex(where: { $0.id == id }) else { return }
        parts[i].rotation = (parts[i].rotation + 90) % 360
    }

    mutating func move(_ id: Int, to x: Int, _ y: Int) {
        guard let i = parts.firstIndex(where: { $0.id == id }) else { return }
        parts[i].x = x
        parts[i].y = y
    }

    mutating func set(_ id: Int, value: Double) {
        guard let i = parts.firstIndex(where: { $0.id == id }) else { return }
        parts[i].value = value
    }

    mutating func toggle(_ id: Int) {
        guard let i = parts.firstIndex(where: { $0.id == id }), parts[i].kind == .switch else { return }
        parts[i].closed.toggle()
    }

    /// Where a terminal is, in grid units.
    func place(of t: TerminalRef) -> (x: Int, y: Int)? {
        guard let p = part(t.part), t.index < p.terminals.count else { return nil }
        return p.terminals[t.index]
    }

    /// The terminals directly joined to `t`: by a link, or by sitting on the same grid point (parts placed end to end).
    func neighbours(of t: TerminalRef) -> [TerminalRef] {
        var out: [TerminalRef] = []
        for l in links {
            if l.a == t { out.append(l.b) } else if l.b == t { out.append(l.a) }
        }
        if let here = place(of: t) {
            for p in parts where p.id != t.part {
                for (i, pos) in p.terminals.enumerated() where pos.x == here.x && pos.y == here.y {
                    out.append(TerminalRef(part: p.id, index: i))
                }
            }
        }
        return out
    }

    var json: String {
        let e = JSONEncoder()
        e.outputFormatting = [.sortedKeys]
        return (try? e.encode(self)).map { String(decoding: $0, as: UTF8.self) } ?? "{}"
    }

    init?(json: String) {
        guard let d = try? JSONDecoder().decode(CircuitDoc.self, from: Data(json.utf8)) else { return nil }
        self = d
        // (ids past any in it, whatever the file said)
        nextID = max(nextID, (parts.map(\.id) + links.map(\.id)).max().map { $0 + 1 } ?? 1)
        guard parts.allSatisfy({ $0.terminals.count == $0.kind.terminalCount }) else { return nil }
    }

    // MARK: Presets

    static let presets: [(id: String, title: String)] = [
        ("series", "Series: 9 V + 100 Ω + 200 Ω"),
        ("parallel", "Parallel: 9 V, 100 Ω ∥ 200 Ω"),
        ("divider", "Voltage divider"),
    ]

    /// The quick-build chips' circuits, laid out so every wire runs straight.
    static func preset(_ id: String) -> CircuitDoc? {
        var d = CircuitDoc()
        switch id {
        case "series":
            let b = d.add(.battery, x: 3, y: 6, rotation: 270, value: 9)
            let r1 = d.add(.resistor, x: 8, y: 4, value: 100)
            let r2 = d.add(.resistor, x: 13, y: 6, rotation: 90, value: 200)
            d.link(TerminalRef(part: b, index: 1), TerminalRef(part: r1, index: 0))
            d.link(TerminalRef(part: r1, index: 1), TerminalRef(part: r2, index: 0))
            d.link(TerminalRef(part: r2, index: 1), TerminalRef(part: b, index: 0))
        case "parallel":
            let b = d.add(.battery, x: 3, y: 6, rotation: 270, value: 9)
            let a = d.add(.ammeter, x: 7, y: 4)
            let r1 = d.add(.resistor, x: 12, y: 6, rotation: 90, value: 100)
            let r2 = d.add(.resistor, x: 17, y: 6, rotation: 90, value: 200)
            d.link(TerminalRef(part: b, index: 1), TerminalRef(part: a, index: 0))
            d.link(TerminalRef(part: a, index: 1), TerminalRef(part: r1, index: 0))
            d.link(TerminalRef(part: r1, index: 0), TerminalRef(part: r2, index: 0))
            d.link(TerminalRef(part: b, index: 0), TerminalRef(part: r1, index: 1))
            d.link(TerminalRef(part: r1, index: 1), TerminalRef(part: r2, index: 1))
        case "divider":
            let b = d.add(.battery, x: 3, y: 6, rotation: 270, value: 12)
            let r1 = d.add(.resistor, x: 8, y: 4, value: 1000)
            let r2 = d.add(.resistor, x: 13, y: 6, rotation: 90, value: 2000)
            let v = d.add(.voltmeter, x: 18, y: 6, rotation: 90)
            let g = d.add(.ground, x: 3, y: 10)
            d.link(TerminalRef(part: b, index: 1), TerminalRef(part: r1, index: 0))
            d.link(TerminalRef(part: r1, index: 1), TerminalRef(part: r2, index: 0))
            d.link(TerminalRef(part: r2, index: 1), TerminalRef(part: b, index: 0))
            d.link(TerminalRef(part: r2, index: 0), TerminalRef(part: v, index: 0))
            d.link(TerminalRef(part: r2, index: 1), TerminalRef(part: v, index: 1))
            d.link(TerminalRef(part: b, index: 0), TerminalRef(part: g, index: 0))
        default:
            return nil
        }
        return d
    }
}

// MARK: - The solver

/// One part's readings: the voltage across it (terminal 0 less terminal 1; a battery's its own), the current through
/// it from terminal 0 to terminal 1 (a battery's out of its +), and the power; nil where the part is not in a circuit
/// with a battery, or its current cannot be told (a wire not in a series path).
struct CircuitReading: Equatable {
    var voltage: Double?
    var current: Double?
    var power: Double? { if let voltage, let current { return abs(voltage * current) }; return nil }
    /// Whether it is in a circuit with a battery.
    var connected: Bool
}

/// The circuit solved: each part's readings by id, and what could not be solved, in plain words.
struct CircuitSolution: Equatable {
    var readings: [Int: CircuitReading] = [:]
    var problems: [String] = []

    func reading(_ id: Int) -> CircuitReading { readings[id] ?? CircuitReading(voltage: nil, current: nil, connected: false) }

    /// A lamp's brightness, 0 to 1, from its power: half a watt is bright.
    static func brightness(power: Double?) -> Double {
        guard let power, power > 0 else { return 0 }
        return 1 - exp(-power / 0.2)
    }
}

enum CircuitSolver {
    /// The ammeter's resistance: next to nothing, so its reading is the branch's current and it drops no volts to
    /// speak of.
    static let ammeterOhms = 1e-6

    static func solve(_ doc: CircuitDoc) -> CircuitSolution {
        var out = CircuitSolution()
        guard !doc.parts.isEmpty else { return out }

        // 1. The nodes: terminals joined by links, by sitting together, through wires and closed switches, and every
        //    ground with every other, each a set.
        var terminals: [TerminalRef] = []
        for p in doc.parts { for i in 0..<p.terminals.count { terminals.append(TerminalRef(part: p.id, index: i)) } }
        var parent: [TerminalRef: TerminalRef] = [:]
        for t in terminals { parent[t] = t }
        func find(_ t: TerminalRef) -> TerminalRef {
            var r = t
            while let p = parent[r], p != r { r = p }
            var c = t
            while let p = parent[c], p != r { parent[c] = r; c = p }
            return r
        }
        func unite(_ a: TerminalRef, _ b: TerminalRef) {
            let ra = find(a), rb = find(b)
            if ra != rb { parent[ra] = rb }
        }
        for t in terminals { for n in doc.neighbours(of: t) { unite(t, n) } }
        var groundRoot: TerminalRef?
        for p in doc.parts {
            if p.joinsNow, p.terminals.count == 2 { unite(TerminalRef(part: p.id, index: 0), TerminalRef(part: p.id, index: 1)) }
            if p.kind == .ground {
                let t = TerminalRef(part: p.id, index: 0)
                if let g = groundRoot { unite(t, g) } else { groundRoot = t }
            }
        }
        func node(_ t: TerminalRef) -> TerminalRef { find(t) }

        // 2. The elements that carry current between nodes, and the batteries that drive it.
        struct Branch { var part: Int; var a: TerminalRef; var b: TerminalRef; var ohms: Double }
        struct Source { var part: Int; var plus: TerminalRef; var minus: TerminalRef; var volts: Double }
        var branches: [Branch] = []
        var sources: [Source] = []
        for p in doc.parts where p.terminals.count == 2 {
            let t0 = node(TerminalRef(part: p.id, index: 0)), t1 = node(TerminalRef(part: p.id, index: 1))
            switch p.kind {
            case .resistor, .lamp:
                branches.append(Branch(part: p.id, a: t0, b: t1, ohms: max(p.value, 1e-9)))
            case .ammeter:
                branches.append(Branch(part: p.id, a: t0, b: t1, ohms: ammeterOhms))
            case .battery:
                sources.append(Source(part: p.id, plus: t1, minus: t0, volts: p.value))
            default:
                break
            }
        }

        // 3. The circuits: the nodes reached from each other through those elements. One with no battery is parts
        //    off on their own; one with a battery is solved on its own, its ground (or its first battery's −) at 0 V.
        var adjacency: [TerminalRef: Set<TerminalRef>] = [:]
        func join(_ a: TerminalRef, _ b: TerminalRef) {
            adjacency[a, default: []].insert(b)
            adjacency[b, default: []].insert(a)
        }
        for b in branches { join(b.a, b.b) }
        for s in sources { join(s.plus, s.minus) }
        var seen = Set<TerminalRef>()
        var voltage: [TerminalRef: Double] = [:]
        var sourceCurrent: [Int: Double] = [:]
        var floating = false
        for s in sources {
            let start = s.plus
            guard !seen.contains(start) else { continue }
            var nodes: [TerminalRef] = []
            var queue = [start]
            seen.insert(start)
            while let n = queue.first {
                queue.removeFirst()
                nodes.append(n)
                for m in adjacency[n] ?? [] where !seen.contains(m) {
                    seen.insert(m)
                    queue.append(m)
                }
            }
            let nodeSet = Set(nodes)
            let mine = sources.filter { nodeSet.contains($0.plus) }
            let myBranches = branches.filter { nodeSet.contains($0.a) }
            if let short = mine.first(where: { $0.plus == $0.minus }) {
                out.problems.append("\(doc.names[short.part] ?? "A battery") is shorted — a wire runs straight from its + to its −. Put something between them.")
                continue
            }
            let reference = groundRoot.map(node).flatMap { nodeSet.contains($0) ? $0 : nil } ?? s.minus
            let unknownNodes = nodes.filter { $0 != reference }
            var index: [TerminalRef: Int] = [:]
            for (i, n) in unknownNodes.enumerated() { index[n] = i }
            let n = unknownNodes.count, m = mine.count
            var A = Array(repeating: Array(repeating: 0.0, count: n + m), count: n + m)
            var z = Array(repeating: 0.0, count: n + m)
            for b in myBranches {
                let g = 1 / b.ohms
                if let i = index[b.a] { A[i][i] += g }
                if let j = index[b.b] { A[j][j] += g }
                if let i = index[b.a], let j = index[b.b] { A[i][j] -= g; A[j][i] -= g }
            }
            for (k, src) in mine.enumerated() {
                let row = n + k
                if let i = index[src.plus] { A[i][row] += 1; A[row][i] += 1 }
                if let j = index[src.minus] { A[j][row] -= 1; A[row][j] -= 1 }
                z[row] = src.volts
            }
            guard let x = gaussian(A, z) else {
                out.problems.append(mine.count > 1 ? "Can't solve this circuit: its batteries fight each other with nothing between them."
                                                   : "Can't solve this circuit — check how its parts are joined.")
                continue
            }
            voltage[reference] = 0
            for (nd, i) in index { voltage[nd] = x[i] }
            // (the source's unknown is the current into its +; out of its + is the other way)
            for (k, src) in mine.enumerated() { sourceCurrent[src.part] = -x[n + k] }
        }

        // 4. Each part's readings.
        for p in doc.parts {
            let t0 = node(TerminalRef(part: p.id, index: 0))
            let t1 = p.terminals.count == 2 ? node(TerminalRef(part: p.id, index: 1)) : t0
            let v0 = voltage[t0], v1 = voltage[t1]
            switch p.kind {
            case .resistor, .lamp, .ammeter:
                if let v0, let v1 {
                    let ohms = p.kind == .ammeter ? ammeterOhms : max(p.value, 1e-9)
                    let v = v0 - v1
                    out.readings[p.id] = CircuitReading(voltage: p.kind == .ammeter ? 0 : v, current: v / ohms, connected: true)
                } else {
                    out.readings[p.id] = CircuitReading(voltage: nil, current: nil, connected: false)
                    floating = true
                }
            case .battery:
                if let i = sourceCurrent[p.id] {
                    out.readings[p.id] = CircuitReading(voltage: p.value, current: i, connected: true)
                } else {
                    out.readings[p.id] = CircuitReading(voltage: p.value, current: nil, connected: v0 != nil)
                    if v0 == nil { floating = true }
                }
            case .voltmeter:
                if let v0, let v1 {
                    out.readings[p.id] = CircuitReading(voltage: v0 - v1, current: 0, connected: true)
                } else {
                    out.readings[p.id] = CircuitReading(voltage: nil, current: nil, connected: false)
                    floating = true
                }
            case .wire, .switch, .ground:
                if p.kind == .switch && !p.closed {
                    // (an open switch: the volts across its gap when both sides are in a circuit, and no current)
                    if let v0, let v1 {
                        out.readings[p.id] = CircuitReading(voltage: v0 - v1, current: 0, connected: true)
                    } else {
                        out.readings[p.id] = CircuitReading(voltage: nil, current: nil, connected: false)
                        floating = true
                    }
                } else if v0 != nil {
                    // (its current, if it is in a series path, is told below)
                    out.readings[p.id] = CircuitReading(voltage: 0, current: nil, connected: true)
                } else {
                    out.readings[p.id] = CircuitReading(voltage: nil, current: nil, connected: false)
                    floating = true
                }
            }
        }

        // 5. The current through a wire, a closed switch or a ground in a series path: what flows into one end from
        //    the one other terminal there, told until nothing more can be.
        var changed = true
        var rounds = 0
        while changed && rounds < doc.parts.count + 2 {
            changed = false
            rounds += 1
            for p in doc.parts where p.joinsNow && p.terminals.count == 2 && out.readings[p.id]?.current == nil && out.readings[p.id]?.connected == true {
                for end in 0..<2 {
                    let t = TerminalRef(part: p.id, index: end)
                    let others = doc.neighbours(of: t)
                    guard others.count == 1, let o = others.first, let op = doc.part(o.part), op.terminals.count == 2,
                          let into = out.readings[o.part]?.current else { continue }
                    // (into the neighbour from its terminal 0 to 1: so out of its terminal `o.index` when that is 1)
                    let flowOut = o.index == 1 ? into : -into
                    // (what leaves the neighbour at that end enters this part at `end`: through it from 0 to 1 when
                    // it came in at 0)
                    let through = end == 0 ? flowOut : -flowOut
                    out.readings[p.id]?.current = through
                    changed = true
                    break
                }
            }
        }

        if floating { out.problems.append("Some parts aren't in a circuit with a battery: join them with wires, from terminal to terminal.") }
        return out
    }

    /// Gaussian elimination with partial pivoting; nil when the system is singular.
    static func gaussian(_ matrix: [[Double]], _ rhs: [Double]) -> [Double]? {
        var a = matrix
        var b = rhs
        let n = b.count
        guard n > 0 else { return [] }
        for col in 0..<n {
            var pivot = col
            for r in (col + 1)..<max(n, col + 1) where abs(a[r][col]) > abs(a[pivot][col]) { pivot = r }
            guard abs(a[pivot][col]) > 1e-12 else { return nil }
            if pivot != col { a.swapAt(pivot, col); b.swapAt(pivot, col) }
            for r in (col + 1)..<max(n, col + 1) {
                let f = a[r][col] / a[col][col]
                guard f != 0 else { continue }
                for c in col..<n { a[r][c] -= f * a[col][c] }
                b[r] -= f * b[col]
            }
        }
        var x = Array(repeating: 0.0, count: n)
        for r in stride(from: n - 1, through: 0, by: -1) {
            var s = b[r]
            for c in (r + 1)..<max(n, r + 1) { s -= a[r][c] * x[c] }
            x[r] = s / a[r][r]
        }
        return x
    }
}

// MARK: - The calculator strip

enum CircuitMath {
    /// Ohm's law from any two of V, I and R; nil unless exactly two are given (or R from V and I with I = 0).
    static func ohm(v: Double?, i: Double?, r: Double?) -> (v: Double, i: Double, r: Double, found: String)? {
        switch (v, i, r) {
        case let (v?, i?, nil):
            guard i != 0 else { return nil }
            return (v, i, v / i, "R")
        case let (v?, nil, r?):
            guard r != 0 else { return nil }
            return (v, v / r, r, "I")
        case let (nil, i?, r?):
            return (i * r, i, r, "V")
        default:
            return nil
        }
    }

    /// A list of resistances as typed ("100, 200", "1k 2.2k", "1M"), in ohms.
    static func resistances(_ s: String) -> [Double] {
        s.replacingOccurrences(of: "Ω", with: "").split(whereSeparator: { $0 == "," || $0 == " " || $0 == ";" || $0 == "\n" }).compactMap { raw -> Double? in
            var t = String(raw).trimmingCharacters(in: .whitespaces)
            var mult = 1.0
            if t.hasSuffix("k") || t.hasSuffix("K") { mult = 1e3; t.removeLast() }
            else if t.hasSuffix("M") { mult = 1e6; t.removeLast() }
            else if t.hasSuffix("m") { mult = 1e-3; t.removeLast() }
            guard let v = PhysMath.number(t), v >= 0 else { return nil }
            return v * mult
        }
    }

    static func series(_ rs: [Double]) -> Double { rs.reduce(0, +) }

    /// The parallel equivalent (0 if any is 0; nil for none).
    static func parallel(_ rs: [Double]) -> Double? {
        guard !rs.isEmpty else { return nil }
        if rs.contains(0) { return 0 }
        return 1 / rs.reduce(0) { $0 + 1 / $1 }
    }

    /// Ohms written short: "66.67 Ω", "1 kΩ", "2.2 MΩ".
    static func ohms(_ r: Double) -> String {
        if abs(r) >= 1e6 { return "\(PhysMath.text(r / 1e6)) MΩ" }
        if abs(r) >= 1e3 { return "\(PhysMath.text(r / 1e3)) kΩ" }
        return "\(PhysMath.text(r)) Ω"
    }

    /// Amps written short: "0.03 A", "1.5 mA", "20 µA".
    static func amps(_ i: Double) -> String {
        let a = abs(i)
        if a > 0 && a < 1e-3 { return "\(PhysMath.text(i * 1e6)) µA" }
        if a > 0 && a < 0.01 { return "\(PhysMath.text(i * 1e3)) mA" }
        return "\(PhysMath.text(i)) A"
    }

    static func volts(_ v: Double) -> String { "\(PhysMath.text(v)) V" }

    static func watts(_ w: Double) -> String {
        let a = abs(w)
        if a > 0 && a < 1e-3 { return "\(PhysMath.text(w * 1e6)) µW" }
        if a > 0 && a < 0.01 { return "\(PhysMath.text(w * 1e3)) mW" }
        return "\(PhysMath.text(w)) W"
    }
}

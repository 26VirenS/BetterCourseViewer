import SwiftUI

// (tools) Kinematics & Projectiles: two pages. 1D motion — the five quantities of motion in a line (Δx, v₀, v, a, t),
// any three filled in, the other two found as you type, with the two equations that found them, and a position–time
// and a velocity–time graph drawn. Projectile — a launch's speed, angle, height and g to its time of flight, range,
// greatest height and how it lands, the path drawn with its apex and landing marked and a dot flown along it (1.5 s;
// still under Reduce Motion), and the two angles that hit a target a given distance off. SI throughout; a toggle shows
// the answers in feet and mph as well. The arithmetic is Model/PhysMotion.swift's, checked by the Models job.
//
// Launch: -SimplTool motion -SimplMotion 1d|projectile -SimplMotionValues "v0=20,angle=45,h=0" (also x, v, a, t, g,
// target, units=imperial). The pin: speed and angle to range and height, Return opening the tool on them.

/// The tool's two pages.
enum MotionTab: String, CaseIterable, Identifiable {
    case oneD = "1d"
    case projectile

    var id: String { rawValue }
    var title: String { self == .oneD ? "1D motion" : "Projectile" }
}

struct KinematicsTool: View {
    @State private var tab: MotionTab
    /// The 1D page's five fields, blank where nothing is given.
    @State private var fields: [KinematicQuantity: String]
    @State private var speed: String
    @State private var angle: String
    @State private var height: String
    @State private var gText: String
    @State private var target: String
    @State private var imperial: Bool

    init() {
        let launch = Self.launch
        let keep = { (field: String) in PhysPrefs.string(.motion, field) ?? "" }
        _tab = State(initialValue: Self.launchTab ?? MotionTab(rawValue: keep("tab")) ?? .projectile)
        var f: [KinematicQuantity: String] = [:]
        for q in KinematicQuantity.allCases {
            f[q] = launch == nil ? keep(q.rawValue) : (launch?[q.rawValue] ?? "")
        }
        _fields = State(initialValue: f)
        _speed = State(initialValue: launch?["v0"] ?? launch?["speed"] ?? (launch == nil ? keep("speed") : "") )
        _angle = State(initialValue: launch?["angle"] ?? (launch == nil ? keep("angle") : ""))
        _height = State(initialValue: launch?["h"] ?? launch?["height"] ?? (launch == nil ? keep("height") : ""))
        _gText = State(initialValue: launch?["g"] ?? (launch == nil ? keep("g") : ""))
        _target = State(initialValue: launch?["target"] ?? (launch == nil ? keep("target") : ""))
        _imperial = State(initialValue: launch.map { $0["units"] == "imperial" } ?? PhysPrefs.bool(.motion, "imperial", default: false))
    }

    // MARK: Opening

    /// Whether the launch opened this tool (-SimplTool motion): only then are its flags this tool's to read.
    private static var launchedHere: Bool { PhysTools.launched == .motion }

    private static var launchTab: MotionTab? {
        guard launchedHere, let s = UserDefaults.standard.string(forKey: "SimplMotion") else { return nil }
        return MotionTab(rawValue: s.lowercased())
    }

    /// The launch's values (-SimplMotionValues "v0=20,angle=45,h=0"), nil when the launch did not open this tool.
    private static var launch: [String: String]? {
        guard launchedHere, let s = UserDefaults.standard.string(forKey: "SimplMotionValues") else { return nil }
        return PhysMath.pairs(s)
    }

    /// (the pin's panel opened full screen: the projectile it had)
    private func takeHandOff() {
        let tools = ToolsCenter.shared
        guard let t = tools.takeInfo("tab") else { return }
        if let tab = MotionTab(rawValue: t) { self.tab = tab }
        if let s = tools.takeInfo("speed") { speed = s }
        if let a = tools.takeInfo("angle") { angle = a }
        if let h = tools.takeInfo("height") { height = h }
    }

    // MARK: The page

    var body: some View {
        ToolPage {
            HStack(spacing: 14) {
                Picker("Page", selection: $tab) {
                    ForEach(MotionTab.allCases) { t in Text(t.title).tag(t) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(width: 260)
                Spacer()
                Picker("Units", selection: $imperial) {
                    Text("m · m/s").tag(false)
                    Text("ft · mph").tag(true)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(width: 180)
                .help("Show the answers in feet and miles per hour as well (the fields stay in metres)")
            }
            switch tab {
            case .oneD: OneDMotionPage(fields: $fields, imperial: imperial)
            case .projectile: ProjectilePage(speed: $speed, angle: $angle, height: $height, gText: $gText, target: $target, imperial: imperial)
            }
        }
        .onAppear(perform: takeHandOff)
        .onChange(of: tab) { _, t in PhysPrefs.set(.motion, "tab", t.rawValue) }
        .onChange(of: fields) { _, f in for q in KinematicQuantity.allCases { PhysPrefs.set(.motion, q.rawValue, f[q]) } }
        .onChange(of: speed) { _, s in PhysPrefs.set(.motion, "speed", s) }
        .onChange(of: angle) { _, s in PhysPrefs.set(.motion, "angle", s) }
        .onChange(of: height) { _, s in PhysPrefs.set(.motion, "height", s) }
        .onChange(of: gText) { _, s in PhysPrefs.set(.motion, "g", s) }
        .onChange(of: target) { _, s in PhysPrefs.set(.motion, "target", s) }
        .onChange(of: imperial) { _, on in PhysPrefs.set(.motion, "imperial", on) }
    }
}

/// A number with its unit as the page shows it: SI, or feet and mph beside it when asked.
enum MotionText {
    static func show(_ v: Double, _ unit: String, imperial: Bool) -> String {
        let si = PhysMath.withUnit(v, unit)
        guard imperial else { return si }
        let c = ImperialDisplay.convert(v, unit: unit)
        return c.unit == unit ? si : "\(si) (\(PhysMath.withUnit(c.value, c.unit)))"
    }
}

// MARK: - 1D motion

/// The 1D page: the five fields, g and Clear, the two found with their equations, and the graphs beside.
private struct OneDMotionPage: View {
    @Binding var fields: [KinematicQuantity: String]
    let imperial: Bool

    private var given: [KinematicQuantity: Double] {
        var g: [KinematicQuantity: Double] = [:]
        for q in KinematicQuantity.allCases { if let v = PhysMath.number(fields[q] ?? "") { g[q] = v } }
        return g
    }

    private var outcome: Result<KinematicsResult, KinematicsProblem> { Kinematics.solve(given) }

    var body: some View {
        ToolColumns(sideWidth: 420, breakpoint: 1000) {
            VStack(alignment: .leading, spacing: 18) {
                PhysHeading("Any three of the five")
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 150, maximum: 220), spacing: 12, alignment: .top)], alignment: .leading, spacing: 12) {
                    ForEach(KinematicQuantity.allCases, id: \.self) { q in
                        PhysField(label: "\(q.label) · \(q.name)", text: binding(q), unit: q.unit, help: q.name)
                    }
                }
                HStack(spacing: 10) {
                    Button {
                        withAnimation(Motion.snappy) { fields[.a] = "−9.81" }
                    } label: {
                        Label("g: a = −9.81 m/s²", systemImage: "arrow.down.to.line")
                    }
                    .glassButton()
                    .help("Free fall: the acceleration set to −9.81 m/s² (up is positive)")
                    Button {
                        withAnimation(Motion.snappy) { for q in KinematicQuantity.allCases { fields[q] = "" } }
                    } label: {
                        Label("Clear", systemImage: "xmark.circle")
                    }
                    .glassButton()
                    .disabled(fields.values.allSatisfy(\.isEmpty))
                }
                results
            }
        } side: {
            VStack(alignment: .leading, spacing: 12) {
                PhysHeading("Graphs")
                if case .success(let r) = outcome, r[.t] > 0 {
                    let pts = Kinematics.samples(v0: r[.v0], a: r[.a], t: r[.t])
                    MotionGraph(title: "Position", unit: "m", points: pts.map { ($0.t, $0.x) }, color: Color(hex: "#0a84ff"))
                    MotionGraph(title: "Velocity", unit: "m/s", points: pts.map { ($0.t, $0.v) }, color: Color(hex: "#30b0c7"))
                } else {
                    Text(outcomeIsTimeless ? "Give a time, or three that find one, and the graphs are drawn from 0 to t." : "The position–time and velocity–time graphs are drawn once three of the five are in.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                        .padding(16)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .card(radius: 16)
                }
            }
        }
    }

    private var outcomeIsTimeless: Bool {
        if case .success(let r) = outcome { return r[.t] <= 0 }
        return false
    }

    private func binding(_ q: KinematicQuantity) -> Binding<String> {
        Binding(get: { fields[q] ?? "" }, set: { fields[q] = $0 })
    }

    @ViewBuilder
    private var results: some View {
        switch outcome {
        case .success(let r):
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .top, spacing: 24) {
                    ForEach(r.solved, id: \.self) { q in
                        VStack(alignment: .leading, spacing: 2) {
                            Text("\(q.label) = \(MotionText.show(r[q], q.unit, imperial: imperial))")
                                .font(.system(size: 30, weight: .bold, design: .rounded).monospacedDigit())
                                .lineLimit(1)
                                .minimumScaleFactor(0.5)
                                .textSelection(.enabled)
                                .contentTransition(.numericText())
                            Text(q.name).font(.sCaption).foregroundStyle(.secondary)
                        }
                    }
                }
                Text("Using \(r.equations.joined(separator: "  and  "))")
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Text(KinematicQuantity.allCases.map { "\($0.label) = \(MotionText.show(r[$0], $0.unit, imperial: imperial))" }.joined(separator: "   "))
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Button {
                    copyToPasteboard(copyText(r))
                } label: {
                    Label("Copy", systemImage: "doc.on.doc")
                }
                .glassButton()
                .help("Copy the five quantities and the equations used")
            }
            .padding(20)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card(radius: 20)
            .animation(Motion.snappy, value: r)
        case .failure(let p):
            // (too few given is the page's resting state, said quietly; three that fit no motion is a warning)
            ToolNote(text: p.text, symbol: Self.quiet(p) ? "info.circle" : "exclamationmark.triangle.fill", tint: Self.quiet(p) ? .secondary : .orange)
                .padding(.top, 4)
        }
    }

    private static func quiet(_ p: KinematicsProblem) -> Bool {
        if case .tooFew = p { return true }
        return false
    }

    private func copyText(_ r: KinematicsResult) -> String {
        let all = KinematicQuantity.allCases.map { "\($0.label) = \(PhysMath.withUnit(r[$0], $0.unit))" }.joined(separator: ", ")
        return (all + " — using " + r.equations.joined(separator: " and ")).replacingOccurrences(of: "−", with: "-")
    }
}

/// A small graph of one quantity against time: its axes with a zero line, the curve, the end value written at its end.
struct MotionGraph: View {
    let title: String
    let unit: String
    let points: [(Double, Double)]
    let color: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("\(title) · time").font(.sSubheadline.weight(.semibold))
                Spacer()
                if let last = points.last {
                    Text("\(PhysMath.withUnit(last.1, unit)) at \(PhysMath.withUnit(last.0, "s"))")
                        .font(.sCaption.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
            }
            Canvas { ctx, size in
                guard points.count > 1, let tMax = points.map(\.0).max(), tMax > 0 else { return }
                let ys = points.map(\.1)
                var lo = min(ys.min() ?? 0, 0), hi = max(ys.max() ?? 0, 0)
                if hi - lo < 1e-9 { hi = lo + 1 }
                let pad = (hi - lo) * 0.08
                lo -= pad; hi += pad
                let inset = CGRect(x: 10, y: 8, width: size.width - 20, height: size.height - 16)
                func pt(_ t: Double, _ y: Double) -> CGPoint {
                    CGPoint(x: inset.minX + inset.width * CGFloat(t / tMax), y: inset.maxY - inset.height * CGFloat((y - lo) / (hi - lo)))
                }
                // (the axes: time along the bottom, the zero line where the quantity is 0)
                let zero = pt(0, 0)
                var axes = Path()
                axes.move(to: CGPoint(x: inset.minX, y: inset.minY)); axes.addLine(to: CGPoint(x: inset.minX, y: inset.maxY))
                axes.move(to: CGPoint(x: inset.minX, y: zero.y)); axes.addLine(to: CGPoint(x: inset.maxX, y: zero.y))
                ctx.stroke(axes, with: .color(.secondary.opacity(0.5)), lineWidth: 1)
                var fill = Path()
                fill.move(to: pt(points[0].0, 0))
                for p in points { fill.addLine(to: pt(p.0, p.1)) }
                fill.addLine(to: pt(points[points.count - 1].0, 0))
                fill.closeSubpath()
                ctx.fill(fill, with: .color(color.opacity(0.12)))
                var line = Path()
                line.move(to: pt(points[0].0, points[0].1))
                for p in points.dropFirst() { line.addLine(to: pt(p.0, p.1)) }
                ctx.stroke(line, with: .color(color), style: StrokeStyle(lineWidth: 2.2, lineCap: .round, lineJoin: .round))
                let end = pt(points[points.count - 1].0, points[points.count - 1].1)
                ctx.fill(Path(ellipseIn: CGRect(x: end.x - 4, y: end.y - 4, width: 8, height: 8)), with: .color(color))
            }
            .frame(height: 150)
            .accessibilityLabel("\(title) against time, ending at \(points.last.map { PhysMath.withUnit($0.1, unit) } ?? "")")
        }
        .padding(14)
        .card(radius: 16)
    }
}

// MARK: - Projectile

/// The projectile page: the launch's four numbers, the path drawn and flown, a target to hit, and the results beside.
private struct ProjectilePage: View {
    @Binding var speed: String
    @Binding var angle: String
    @Binding var height: String
    @Binding var gText: String
    @Binding var target: String
    let imperial: Bool

    private var projectile: Projectile? {
        guard let v = PhysMath.number(speed), let a = PhysMath.number(angle) else { return nil }
        let h = PhysMath.number(height) ?? 0
        let g = PhysMath.number(gText) ?? Kinematics.g
        let p = Projectile(speed: v, angle: a, height: h, g: g)
        return p.flies ? p : nil
    }

    private var problem: String? {
        if speed.trimmingCharacters(in: .whitespaces).isEmpty || angle.trimmingCharacters(in: .whitespaces).isEmpty { return "Give a launch speed and an angle." }
        guard PhysMath.number(speed) != nil, PhysMath.number(angle) != nil else { return "The speed and angle need to be numbers." }
        if let h = PhysMath.number(height), h < 0 { return "The launch height can't be below the ground it lands on." }
        if let g = PhysMath.number(gText), g <= 0 { return "g has to be more than 0 — 9.81 m/s² on Earth." }
        if (PhysMath.number(speed) ?? 0) < 0 { return "The speed can't be negative — turn the angle round instead." }
        if (PhysMath.number(speed) ?? 0) == 0 && (PhysMath.number(height) ?? 0) == 0 { return "At rest on the ground it goes nowhere — give it a speed or a height." }
        return nil
    }

    var body: some View {
        ToolColumns(sideWidth: 380, breakpoint: 1000) {
            VStack(alignment: .leading, spacing: 18) {
                PhysHeading("The launch")
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 150, maximum: 220), spacing: 12, alignment: .top)], alignment: .leading, spacing: 12) {
                    PhysField(label: "v₀ · Launch speed", text: $speed, unit: "m/s", placeholder: "20")
                    PhysField(label: "θ · Angle", text: $angle, unit: "°", placeholder: "45", help: "Above the horizontal")
                    PhysField(label: "h · Launch height", text: $height, unit: "m", placeholder: "0", help: "Above where it lands")
                    PhysField(label: "g · Gravity", text: $gText, unit: "m/s²", placeholder: "9.81")
                }
                if let problem {
                    ToolNote(text: problem, symbol: projectile == nil && PhysMath.number(speed) == nil ? "info.circle" : "exclamationmark.triangle.fill",
                             tint: PhysMath.number(speed) == nil ? .secondary : .orange)
                }
                TrajectoryView(projectile: projectile, imperial: imperial)
                    .frame(height: 320)
                targetSolver
            }
        } side: {
            VStack(alignment: .leading, spacing: 12) {
                PhysHeading("Results")
                if let p = projectile {
                    VStack(spacing: 0) {
                        PhysValueRow(label: "Time of flight", value: PhysMath.withUnit(p.flightTime, "s"))
                        PhysValueRow(label: "Range", value: MotionText.show(p.range, "m", imperial: imperial))
                        PhysValueRow(label: "Greatest height", value: MotionText.show(p.maxHeight, "m", imperial: imperial))
                        PhysValueRow(label: "Highest at", value: "\(PhysMath.withUnit(p.apexTime, "s")) · \(MotionText.show(p.apexX, "m", imperial: imperial)) along")
                        PhysValueRow(label: "Impact speed", value: MotionText.show(p.impactSpeed, "m/s", imperial: imperial))
                        PhysValueRow(label: "Impact angle", value: "\(PhysMath.withUnit(p.impactAngle, "°")) below horizontal")
                        PhysValueRow(label: "Horizontal velocity", value: MotionText.show(p.vx, "m/s", imperial: imperial))
                        PhysValueRow(label: "Vertical velocity at launch", value: MotionText.show(p.vy, "m/s", imperial: imperial), last: true)
                    }
                    .card(radius: 16)
                    .animation(Motion.snappy, value: p)
                    Button {
                        copyToPasteboard(copyText(p))
                    } label: {
                        Label("Copy", systemImage: "doc.on.doc")
                    }
                    .glassButton()
                    .help("Copy the launch and its results as text")
                } else {
                    Text("The time of flight, range, greatest height and landing are listed here once the launch is in.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                        .padding(16)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .card(radius: 16)
                }
            }
        }
    }

    /// "Hit a target": a distance along the landing level, and the low and high angles that reach it at this speed.
    private var targetSolver: some View {
        VStack(alignment: .leading, spacing: 10) {
            PhysHeading("Hit a target")
            HStack(alignment: .firstTextBaseline, spacing: 14) {
                PhysField(label: "d · Distance to the target", text: $target, unit: "m", placeholder: "30", help: "Along the ground, at the landing level")
                    .frame(width: 240)
                targetAnswer
                    .font(.sBody.monospacedDigit())
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 22)
            }
        }
    }

    @ViewBuilder
    private var targetAnswer: some View {
        if let p = projectile, let d = PhysMath.number(target) {
            if d <= 0 {
                Text("A distance past the launch, please.").foregroundStyle(.secondary)
            } else if let angles = p.angles(toReach: d) {
                if angles.count == 2 {
                    Text("Aim at \(PhysMath.withUnit(angles[0], "°")) (low) or \(PhysMath.withUnit(angles[1], "°")) (high) at \(PhysMath.withUnit(p.speed, "m/s"))")
                } else {
                    Text("Aim at \(PhysMath.withUnit(angles[0], "°")) — that is as far as \(PhysMath.withUnit(p.speed, "m/s")) goes")
                }
            } else {
                Text("Out of reach: at \(PhysMath.withUnit(p.speed, "m/s")) the farthest is \(MotionText.show(p.farthest, "m", imperial: imperial)).")
                    .foregroundStyle(.orange)
            }
        } else {
            Text("The two launch angles that land it there, at this speed and height.").foregroundStyle(.secondary)
        }
    }

    private func copyText(_ p: Projectile) -> String {
        let lines = [
            "Launch \(PhysMath.withUnit(p.speed, "m/s")) at \(PhysMath.withUnit(p.angle, "°")) from \(PhysMath.withUnit(p.height, "m")), g = \(PhysMath.withUnit(p.g, "m/s²"))",
            "Time of flight \(PhysMath.withUnit(p.flightTime, "s")), range \(PhysMath.withUnit(p.range, "m")), greatest height \(PhysMath.withUnit(p.maxHeight, "m"))",
            "Impact \(PhysMath.withUnit(p.impactSpeed, "m/s")) at \(PhysMath.withUnit(p.impactAngle, "°")) below the horizontal",
        ]
        return lines.joined(separator: "\n").replacingOccurrences(of: "−", with: "-")
    }
}

/// The path drawn to fit its card, the ground under it, the apex and the landing marked with their numbers, and a dot
/// flown from launch to landing in 1.5 s each time the launch changes (still at the landing under Reduce Motion).
private struct TrajectoryView: View {
    let projectile: Projectile?
    let imperial: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var began: Date?
    @State private var flying = false

    static let flight: TimeInterval = 1.5

    var body: some View {
        ZStack {
            if let p = projectile {
                TimelineView(.animation(paused: !flying)) { context in
                    let elapsed = began.map { context.date.timeIntervalSince($0) } ?? Self.flight
                    let progress = reduceMotion || !flying ? 1 : min(max(elapsed / Self.flight, 0), 1)
                    TrajectoryCanvas(p: p, progress: progress, imperial: imperial)
                }
            } else {
                Text("The path is drawn here.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .card(radius: 20)
        .onAppear { fly() }
        .onChange(of: projectile) { _, _ in fly() }
        .accessibilityLabel(projectile.map { "The path: \(PhysMath.withUnit($0.range, "m")) along, \(PhysMath.withUnit($0.maxHeight, "m")) high" } ?? "No path yet")
    }

    /// The dot sent along the path again (not under Reduce Motion: it sits at the landing).
    private func fly() {
        guard projectile != nil, !reduceMotion else { flying = false; return }
        began = Date()
        flying = true
        let mine = began
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: UInt64((Self.flight + 0.1) * 1_000_000_000))
            if began == mine { flying = false }
        }
    }
}

/// The drawing itself: the launch at the left, a scale that fits the range and the greatest height both.
private struct TrajectoryCanvas: View {
    let p: Projectile
    let progress: Double
    let imperial: Bool

    var body: some View {
        Canvas { ctx, size in
            let path = p.path()
            let xMax = max(p.range, 1e-6), yMax = max(p.maxHeight, 1e-6)
            let inset = CGRect(x: 28, y: 30, width: size.width - 56, height: size.height - 58)
            // (one scale for both axes, so a parabola is drawn as it is — unless the flight is far flatter or far
            // taller than the card, when each axis fits on its own and the drawing says so)
            let sx = inset.width / CGFloat(xMax), sy = inset.height / CGFloat(yMax)
            let same = max(sx, sy) / min(sx, sy) < 2.5
            let scaleX = same ? min(sx, sy) : sx, scaleY = same ? min(sx, sy) : sy
            // (on one scale the flight may be narrower than the card: centred then; a straight drop sits in the middle)
            let drawnW = CGFloat(xMax) * scaleX
            let left = same ? inset.minX + max(0, (inset.width - drawnW) / 2) : inset.minX
            func pt(_ x: Double, _ y: Double) -> CGPoint {
                CGPoint(x: left + CGFloat(x) * scaleX, y: inset.maxY - CGFloat(y) * scaleY)
            }
            // the ground
            var ground = Path()
            ground.move(to: CGPoint(x: inset.minX - 10, y: inset.maxY)); ground.addLine(to: CGPoint(x: inset.maxX + 10, y: inset.maxY))
            ctx.stroke(ground, with: .color(.secondary.opacity(0.5)), lineWidth: 1)
            // the launch height, as a post
            if p.height > 0 {
                var post = Path()
                post.move(to: pt(0, 0)); post.addLine(to: pt(0, p.height))
                ctx.stroke(post, with: .color(.secondary.opacity(0.5)), style: StrokeStyle(lineWidth: 1, dash: [3, 3]))
            }
            // the path, faint whole and bright as far as the dot has flown
            let blue = Color(hex: "#0a84ff")
            var whole = Path()
            whole.move(to: pt(path[0].x, path[0].y))
            for q in path.dropFirst() { whole.addLine(to: pt(q.x, q.y)) }
            ctx.stroke(whole, with: .color(blue.opacity(0.25)), style: StrokeStyle(lineWidth: 2, dash: [4, 4]))
            let flownCount = max(1, Int((Double(path.count - 1) * progress).rounded()))
            var flown = Path()
            flown.move(to: pt(path[0].x, path[0].y))
            for q in path.prefix(flownCount + 1).dropFirst() { flown.addLine(to: pt(q.x, q.y)) }
            ctx.stroke(flown, with: .color(blue), style: StrokeStyle(lineWidth: 2.5, lineCap: .round, lineJoin: .round))
            // the apex and the landing
            let apex = pt(p.apexX, p.maxHeight), land = pt(p.range, 0)
            let marker = Color(hex: "#ff9f0a")
            ctx.fill(Path(ellipseIn: CGRect(x: apex.x - 4, y: apex.y - 4, width: 8, height: 8)), with: .color(marker))
            ctx.fill(Path(ellipseIn: CGRect(x: land.x - 4, y: land.y - 4, width: 8, height: 8)), with: .color(marker))
            let apexText = Text(MotionText.show(p.maxHeight, "m", imperial: imperial)).font(.sCaption.weight(.semibold).monospacedDigit()).foregroundStyle(.secondary)
            ctx.draw(apexText, at: CGPoint(x: apex.x, y: apex.y - 12), anchor: .bottom)
            let landText = Text(MotionText.show(p.range, "m", imperial: imperial)).font(.sCaption.weight(.semibold).monospacedDigit()).foregroundStyle(.secondary)
            ctx.draw(landText, at: CGPoint(x: land.x, y: land.y + 10), anchor: .top)
            let launch = pt(0, p.height)
            ctx.fill(Path(ellipseIn: CGRect(x: launch.x - 3.5, y: launch.y - 3.5, width: 7, height: 7)), with: .color(.secondary))
            // the dot in flight
            let now = p.at(p.flightTime * progress)
            let dot = pt(now.x, now.y)
            ctx.fill(Path(ellipseIn: CGRect(x: dot.x - 7, y: dot.y - 7, width: 14, height: 14)), with: .color(blue))
            ctx.stroke(Path(ellipseIn: CGRect(x: dot.x - 7, y: dot.y - 7, width: 14, height: 14)), with: .color(.white.opacity(0.9)), lineWidth: 1.5)
            if !same {
                let note = Text("Drawn to fit: the height is stretched").font(.sCaption2).foregroundStyle(.tertiary)
                ctx.draw(note, at: CGPoint(x: inset.maxX, y: inset.minY - 18), anchor: .topTrailing)
            }
        }
        .padding(4)
    }
}

// MARK: - The pin

/// Kinematics & Projectiles' pin, opened: a launch's speed and angle to its range and greatest height; Return opens the
/// tool on them.
struct MotionCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var speed = "20"
    @State private var angle = "45"

    private var projectile: Projectile? {
        guard let v = PhysMath.number(speed), let a = PhysMath.number(angle) else { return nil }
        let p = Projectile(speed: v, angle: a, height: 0)
        return p.flies ? p : nil
    }

    /// (1.3.83) What Open Full Screen carries to the whole tool, once anything is changed from how the panel opens.
    private var carried: [String: String] {
        guard speed != "20" || angle != "45" else { return [:] }
        return ["tab": MotionTab.projectile.rawValue, "speed": speed, "angle": angle, "height": "0"]
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                PhysField(label: "Speed", text: $speed, unit: "m/s", placeholder: "20")
                PhysField(label: "Angle", text: $angle, unit: "°", placeholder: "45")
            }
            .onSubmit { openFull(["tab": MotionTab.projectile.rawValue, "speed": speed, "angle": angle, "height": "0"]) }
            if let p = projectile {
                HStack(alignment: .firstTextBaseline, spacing: 16) {
                    VStack(alignment: .leading, spacing: 1) {
                        Text(PhysMath.withUnit(p.range, "m"))
                            .font(.system(size: 26, weight: .bold, design: .rounded).monospacedDigit())
                            .lineLimit(1)
                            .minimumScaleFactor(0.5)
                            .textSelection(.enabled)
                        Text("Range").font(.sCaption).foregroundStyle(.secondary)
                    }
                    VStack(alignment: .leading, spacing: 1) {
                        Text(PhysMath.withUnit(p.maxHeight, "m"))
                            .font(.system(size: 26, weight: .bold, design: .rounded).monospacedDigit())
                            .lineLimit(1)
                            .minimumScaleFactor(0.5)
                            .textSelection(.enabled)
                        Text("Greatest height").font(.sCaption).foregroundStyle(.secondary)
                    }
                }
                Text("\(PhysMath.withUnit(p.flightTime, "s")) in the air, from the ground · g = 9.81 m/s²")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            } else {
                Text("A speed and an angle, and the range and height are here.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        }
        .quickCarry(carried)
    }
}

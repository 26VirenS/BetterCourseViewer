import SwiftUI

// The Dashboard's grades at a glance and the week's load (1.2), as the web Dashboard draws them side by side.

/// The grades skyline (the web Dashboard's, 2.98.90): a tower per course, as tall as its score, every assignment a
/// window — lit in its grade's colour once marked and posted (A green, B yellow-green, C yellow, D orange, F red), dark
/// while it is to come, grey on the floors at the street where it does not count toward the total. A course with no
/// score yet stands as an outline. The towers rise out of the street the first time the Dashboard is shown (not under
/// Reduce Motion); a tower opens its course's grades.
struct DashSkyline: View {
    let courses: [DashSkyCourse]
    var height: CGFloat = 150
    @EnvironmentObject private var engine: Engine
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// The towers have risen once this launch: later visits show them standing.
    private static var risenOnce = false
    @State private var risen: Bool
    @State private var animateRise = false

    init(courses: [DashSkyCourse], height: CGFloat = 150) {
        self.courses = courses
        self.height = height
        _risen = State(initialValue: DashSkyline.risenOnce)
    }

    static func litColor(_ band: String) -> Color {
        switch band {
        case "A": return Color(hex: "#4cd964")
        case "B": return Color(hex: "#b5e036")
        case "C": return Color(hex: "#ffd426")
        case "D": return Color(hex: "#ff9f0a")
        default: return Color(hex: "#ff453a")
        }
    }

    private var towerWidth: CGFloat {
        switch courses.count {
        case ...5: return 54
        case 6: return 48
        case 7...8: return 40
        default: return 32
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            if courses.isEmpty {
                EmptyNote(text: "No courses chosen yet.", symbol: "building.2")
            } else {
                city
                legend
            }
        }
        .padding(.horizontal, 18)
        .padding(.top, 16)
        .padding(.bottom, 14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 18)
        .onAppear { rise() }
    }

    private var city: some View {
        VStack(spacing: 0) {
            HStack(alignment: .bottom, spacing: 0) {
                ForEach(Array(courses.enumerated()), id: \.element.id) { i, c in
                    column(c, index: i).frame(maxWidth: .infinity)
                }
            }
            .frame(height: height + 26, alignment: .bottom)
            Rectangle()
                .fill(Color.primary.opacity(0.14))
                .frame(height: 1)
            HStack(spacing: 0) {
                ForEach(courses) { c in
                    Text(c.name)
                        .font(.system(size: 11, weight: .semibold))
                        .tracking(0.5)
                        .textCase(.uppercase)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.7)
                        .padding(.horizontal, 2)
                        .frame(maxWidth: .infinity)
                }
            }
            .padding(.top, 8)
        }
    }

    private func column(_ c: DashSkyCourse, index i: Int) -> some View {
        let fraction = c.score.map { max(0.16, min(1, $0 / 100)) } ?? 0.42
        let th = (height * fraction).rounded()
        let delay = Double(min(i, 8)) * 0.07
        return VStack(spacing: 6) {
            Text(c.score.map { "\(Int($0.rounded()))%" } ?? "—")
                .font(.system(size: 13, weight: .bold).monospacedDigit())
                .foregroundStyle(c.score == nil ? .secondary : .primary)
                .opacity(risen ? 1 : 0)
                .animation(animateRise ? Motion.gentle.delay(0.3 + delay) : nil, value: risen)
            Button {
                engine.go(.section("courses/\(c.id)", "grades"))
            } label: {
                DashTower(course: c, width: towerWidth, height: th)
            }
            .buttonStyle(DashTowerStyle())
            .offset(y: risen ? 0 : th + 2)
            .animation(animateRise ? Motion.gentle.delay(0.08 + delay) : nil, value: risen)
            .frame(height: th, alignment: .bottom)
            .clipped()
            .help(summary(c))
            .accessibilityLabel("\(summary(c)). Open its grades.")
        }
    }

    private func summary(_ c: DashSkyCourse) -> String {
        let w = c.windows ?? []
        let lit = w.filter { $0.band != nil && $0.free != true }.count
        let free = w.filter { $0.free == true }.count
        let score = c.score.map { "\(Int($0.rounded()))%" } ?? "no score yet"
        guard c.windows != nil else { return "\(c.code ?? c.name): \(score)" }
        return "\(c.code ?? c.name): \(score) · \(lit) graded, \(w.count - lit - free) to come" + (free > 0 ? ", \(free) not counted" : "")
    }

    private var legend: some View {
        HStack(spacing: 12) {
            Text("Window colour = score")
                .font(.sCaption)
                .foregroundStyle(.secondary)
            HStack(spacing: 8) {
                ForEach(["A", "B", "C", "D", "F"], id: \.self) { b in
                    HStack(spacing: 3) {
                        RoundedRectangle(cornerRadius: 1.5, style: .continuous)
                            .fill(DashSkyline.litColor(b))
                            .frame(width: 8, height: 8)
                        Text(b)
                            .font(.sCaption2.weight(.semibold))
                            .foregroundStyle(.secondary)
                    }
                }
            }
            Spacer(minLength: 0)
        }
        .accessibilityElement(children: .combine)
    }

    private func rise() {
        guard !risen else { return }
        let play = !reduceMotion && !DashSkyline.risenOnce
        DashSkyline.risenOnce = true
        animateRise = play
        if play {
            DispatchQueue.main.async { risen = true }
        } else {
            risen = true
        }
    }
}

/// A tower: the course's colour darkening to the street (an outline while it has no score), its windows drawn in it.
private struct DashTower: View {
    let course: DashSkyCourse
    let width: CGFloat
    let height: CGFloat

    var body: some View {
        let shape = UnevenRoundedRectangle(topLeadingRadius: 7, topTrailingRadius: 7, style: .continuous)
        let color = Color(hex: course.color)
        let ghost = course.score == nil
        ZStack {
            if ghost {
                shape.fill(Color.primary.opacity(0.05))
                shape.stroke(Color.secondary.opacity(0.5), style: StrokeStyle(lineWidth: 3, dash: [4, 3]))
            } else {
                shape.fill(color)
                shape.fill(LinearGradient(colors: [.clear, .black.opacity(0.34)], startPoint: .top, endPoint: .bottom))
            }
            DashWindows(windows: course.windows ?? [], ghost: ghost)
                .opacity(course.windows == nil ? 0 : 1)
                .animation(.easeOut(duration: 0.45), value: course.windows == nil)
        }
        .frame(width: width, height: height)
        .clipShape(shape)
    }
}

/// A tower brightening under the pointer.
private struct DashTowerStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        DashTowerBody(configuration: configuration)
    }

    private struct DashTowerBody: View {
        let configuration: ButtonStyleConfiguration
        @State private var hover = false

        var body: some View {
            configuration.label
                .brightness(configuration.isPressed ? -0.05 : (hover ? 0.06 : 0))
                .animation(Motion.hover, value: hover)
                .onHover { hover = $0 }
        }
    }
}

/// How many windows across, and how large, to fit `count` in a space: four across while they fit, more (and smaller)
/// when they do not — the web skyline's rule.
enum DashSkyFit {
    static func fit(count n: Int, width w: CGFloat, height h: CGFloat) -> (cols: Int, size: CGFloat, gap: CGFloat) {
        let g: CGFloat = 3
        var bestCols = 4
        var bestSize: CGFloat = 0
        for cols in [4, 3, 5, 6, 7, 8] {
            let rows = max(1, (n + cols - 1) / cols)
            let across = (w - CGFloat(cols - 1) * g) / CGFloat(cols)
            let down = (h - CGFloat(rows - 1) * g) / CGFloat(rows)
            let s = min(7, across, down)
            if s > bestSize + 0.01 {
                bestCols = cols
                bestSize = s
            }
        }
        return (bestCols, max(2, (bestSize * 2).rounded(.down) / 2), g)
    }
}

/// A tower's windows: what counts from the top floor down, what does not on the floors at the street.
private struct DashWindows: View {
    let windows: [DashSkyWindow]
    let ghost: Bool

    private struct Pane {
        let rect: CGRect
        let color: Color
    }

    var body: some View {
        Canvas { ctx, size in
            for pane in panes(in: size) {
                ctx.fill(Path(roundedRect: pane.rect, cornerRadius: 1.5), with: .color(pane.color))
            }
        }
        .accessibilityHidden(true)
    }

    private func panes(in size: CGSize) -> [Pane] {
        guard !windows.isEmpty, size.width > 12, size.height > 12 else { return [] }
        let up = windows.filter { $0.free != true }
        let down = windows.filter { $0.free == true }
        let top: CGFloat = 7
        let bottom: CGFloat = down.isEmpty ? 0 : 7
        let between: CGFloat = up.isEmpty || down.isEmpty ? 0 : 6
        let fit = DashSkyFit.fit(count: windows.count, width: size.width - 10, height: size.height - top - bottom - between)
        let s = fit.size, g = fit.gap, cols = fit.cols
        let gridWidth = CGFloat(cols) * s + CGFloat(cols - 1) * g
        let x0 = ((size.width - gridWidth) / 2).rounded()
        var out: [Pane] = []
        for (k, w) in up.enumerated() {
            let r = CGFloat(k / cols), c = CGFloat(k % cols)
            out.append(Pane(rect: CGRect(x: x0 + c * (s + g), y: top + r * (s + g), width: s, height: s), color: color(w)))
        }
        let downRows = (down.count + cols - 1) / cols
        let y0 = size.height - bottom - CGFloat(downRows) * s - CGFloat(max(downRows - 1, 0)) * g
        for (k, w) in down.enumerated() {
            let r = CGFloat(k / cols), c = CGFloat(k % cols)
            out.append(Pane(rect: CGRect(x: x0 + c * (s + g), y: y0 + r * (s + g), width: s, height: s), color: color(w)))
        }
        return out
    }

    private func color(_ w: DashSkyWindow) -> Color {
        if let band = w.band {
            return w.free == true ? Color(white: 0.62) : DashSkyline.litColor(band)
        }
        if w.free == true { return Color(red: 92 / 255, green: 92 / 255, blue: 97 / 255).opacity(0.92) }
        return ghost ? Color.secondary.opacity(0.3) : Color.black.opacity(0.22)
    }
}

/// The week's load: each course's share of this week's work handed in, a bar in its colour; a course opens its
/// assignments.
struct DashWeekLoad: View {
    let load: [LoadRow]
    let idle: Int?
    @EnvironmentObject private var engine: Engine

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            if load.isEmpty {
                EmptyNote(text: "Nothing assigned this week.", symbol: "sun.max")
            }
            ForEach(load) { r in row(r) }
            if let idle, idle > 0 {
                Text("\(idle) \(idle == 1 ? "course" : "courses") with nothing assigned this week")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 18)
    }

    private func row(_ r: LoadRow) -> some View {
        Button { engine.go(.section("courses/\(r.id)", "assignments")) } label: {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 8) {
                    Circle().fill(Color(hex: r.color)).frame(width: 9, height: 9)
                    Text(r.code).font(.sCallout.weight(.semibold)).lineLimit(1)
                    Spacer(minLength: 8)
                    Text("\(r.done) of \(r.total)")
                        .font(.sCallout.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .contentTransition(.numericText(value: Double(r.done)))
                }
                DashBar(fraction: r.total > 0 ? Double(r.done) / Double(r.total) : 0, color: Color(hex: r.color))
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("\(r.code)’s assignments")
    }
}

/// A share handed in: a bar in a course's colour that fills on the house spring, from where it was.
struct DashBar: View {
    let fraction: Double
    let color: Color
    var height: CGFloat = 7
    @State private var shown = 0.0
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(color.opacity(0.16))
                Capsule().fill(color).frame(width: max(g.size.width * shown, shown > 0 ? height : 0))
            }
        }
        .frame(height: height)
        .onAppear {
            if reduceMotion { shown = fraction } else { withAnimation(Motion.fill.delay(0.05)) { shown = fraction } }
        }
        .onChange(of: fraction) { _, f in
            withAnimation(reduceMotion ? nil : Motion.gentle) { shown = f }
        }
        .accessibilityHidden(true)
    }
}

import SwiftUI

// The unit converter (1.2, the Mac's own: the web has none): a value in one unit, shown in another and beside it in
// every unit of its kind — lengths, weights, temperatures, volumes, areas, speeds, times, energy, pressure, data and
// angles — worked out by Foundation's own units.

struct UnitChoice: Identifiable, Hashable {
    let name: String
    let symbol: String
    let unit: Dimension

    var id: String { name }
}

struct UnitCategory: Identifiable, Hashable {
    let name: String
    let symbol: String
    let units: [UnitChoice]
    /// The pair it starts on.
    let from: Int
    let to: Int

    var id: String { name }

    private static func u(_ name: String, _ symbol: String, _ unit: Dimension) -> UnitChoice {
        UnitChoice(name: name, symbol: symbol, unit: unit)
    }

    static let all: [UnitCategory] = [
        UnitCategory(name: "Length", symbol: "ruler", units: [
            u("Millimetres", "mm", UnitLength.millimeters), u("Centimetres", "cm", UnitLength.centimeters), u("Metres", "m", UnitLength.meters),
            u("Kilometres", "km", UnitLength.kilometers), u("Inches", "in", UnitLength.inches), u("Feet", "ft", UnitLength.feet),
            u("Yards", "yd", UnitLength.yards), u("Miles", "mi", UnitLength.miles), u("Nautical miles", "nmi", UnitLength.nauticalMiles),
            u("Micrometres", "µm", UnitLength.micrometers), u("Nanometres", "nm", UnitLength.nanometers), u("Light years", "ly", UnitLength.lightyears),
        ], from: 2, to: 5),
        UnitCategory(name: "Mass", symbol: "scalemass", units: [
            u("Milligrams", "mg", UnitMass.milligrams), u("Grams", "g", UnitMass.grams), u("Kilograms", "kg", UnitMass.kilograms),
            u("Metric tons", "t", UnitMass.metricTons), u("Ounces", "oz", UnitMass.ounces), u("Pounds", "lb", UnitMass.pounds),
            u("Stones", "st", UnitMass.stones), u("Short tons", "ton", UnitMass.shortTons),
        ], from: 2, to: 5),
        UnitCategory(name: "Temperature", symbol: "thermometer.medium", units: [
            u("Celsius", "°C", UnitTemperature.celsius), u("Fahrenheit", "°F", UnitTemperature.fahrenheit), u("Kelvin", "K", UnitTemperature.kelvin),
        ], from: 0, to: 1),
        UnitCategory(name: "Volume", symbol: "drop", units: [
            u("Millilitres", "mL", UnitVolume.milliliters), u("Litres", "L", UnitVolume.liters), u("Cubic metres", "m³", UnitVolume.cubicMeters),
            u("Cubic centimetres", "cm³", UnitVolume.cubicCentimeters), u("Teaspoons", "tsp", UnitVolume.teaspoons), u("Tablespoons", "tbsp", UnitVolume.tablespoons),
            u("Fluid ounces", "fl oz", UnitVolume.fluidOunces), u("Cups", "cup", UnitVolume.cups), u("Pints", "pt", UnitVolume.pints),
            u("Quarts", "qt", UnitVolume.quarts), u("Gallons", "gal", UnitVolume.gallons),
        ], from: 1, to: 10),
        UnitCategory(name: "Area", symbol: "square.dashed", units: [
            u("Square metres", "m²", UnitArea.squareMeters), u("Square kilometres", "km²", UnitArea.squareKilometers), u("Square centimetres", "cm²", UnitArea.squareCentimeters),
            u("Hectares", "ha", UnitArea.hectares), u("Square feet", "ft²", UnitArea.squareFeet), u("Square inches", "in²", UnitArea.squareInches),
            u("Square miles", "mi²", UnitArea.squareMiles), u("Acres", "ac", UnitArea.acres),
        ], from: 0, to: 4),
        UnitCategory(name: "Speed", symbol: "speedometer", units: [
            u("Metres per second", "m/s", UnitSpeed.metersPerSecond), u("Kilometres per hour", "km/h", UnitSpeed.kilometersPerHour),
            u("Miles per hour", "mph", UnitSpeed.milesPerHour), u("Knots", "kn", UnitSpeed.knots),
        ], from: 1, to: 2),
        UnitCategory(name: "Time", symbol: "clock", units: [
            u("Milliseconds", "ms", UnitDuration.milliseconds), u("Seconds", "s", UnitDuration.seconds), u("Minutes", "min", UnitDuration.minutes),
            u("Hours", "h", UnitDuration.hours),
        ], from: 3, to: 2),
        UnitCategory(name: "Energy", symbol: "bolt", units: [
            u("Joules", "J", UnitEnergy.joules), u("Kilojoules", "kJ", UnitEnergy.kilojoules), u("Calories", "cal", UnitEnergy.calories),
            u("Kilocalories", "kcal", UnitEnergy.kilocalories), u("Kilowatt-hours", "kWh", UnitEnergy.kilowattHours),
        ], from: 3, to: 1),
        UnitCategory(name: "Pressure", symbol: "gauge.with.dots.needle.33percent", units: [
            u("Pascals", "Pa", UnitPressure.newtonsPerMetersSquared), u("Kilopascals", "kPa", UnitPressure.kilopascals), u("Bars", "bar", UnitPressure.bars),
            u("Millibars", "mbar", UnitPressure.millibars), u("Atmospheres", "atm", UnitPressure(symbol: "atm", converter: UnitConverterLinear(coefficient: 101_325))),
            u("Millimetres of mercury", "mmHg", UnitPressure.millimetersOfMercury), u("Pounds per square inch", "psi", UnitPressure.poundsForcePerSquareInch),
        ], from: 4, to: 1),
        UnitCategory(name: "Data", symbol: "externaldrive", units: [
            u("Bits", "b", UnitInformationStorage.bits), u("Bytes", "B", UnitInformationStorage.bytes), u("Kilobytes", "kB", UnitInformationStorage.kilobytes),
            u("Megabytes", "MB", UnitInformationStorage.megabytes), u("Gigabytes", "GB", UnitInformationStorage.gigabytes), u("Terabytes", "TB", UnitInformationStorage.terabytes),
            u("Kibibytes", "KiB", UnitInformationStorage.kibibytes), u("Mebibytes", "MiB", UnitInformationStorage.mebibytes), u("Gibibytes", "GiB", UnitInformationStorage.gibibytes),
        ], from: 4, to: 3),
        UnitCategory(name: "Angle", symbol: "angle", units: [
            u("Degrees", "°", UnitAngle.degrees), u("Radians", "rad", UnitAngle.radians), u("Gradians", "grad", UnitAngle.gradians),
            u("Revolutions", "rev", UnitAngle.revolutions), u("Arc minutes", "′", UnitAngle.arcMinutes), u("Arc seconds", "″", UnitAngle.arcSeconds),
        ], from: 0, to: 1),
    ]

    static func == (a: UnitCategory, b: UnitCategory) -> Bool { a.name == b.name }
    func hash(into h: inout Hasher) { h.combine(name) }
}

extension UnitChoice {
    static func == (a: UnitChoice, b: UnitChoice) -> Bool { a.name == b.name }
    func hash(into h: inout Hasher) { h.combine(name) }
}

enum UnitMath {
    /// A value from one unit in another of its kind.
    static func convert(_ v: Double, from: Dimension, to: Dimension) -> Double {
        to.converter.value(fromBaseUnitValue: from.converter.baseUnitValue(fromValue: v))
    }

    static func number(_ s: String) -> Double? {
        let t = s.replacingOccurrences(of: ",", with: "").replacingOccurrences(of: "−", with: "-").trimmingCharacters(in: .whitespaces)
        guard let v = Double(t), v.isFinite else { return nil }
        return v
    }

    static func text(_ v: Double) -> String {
        guard v.isFinite else { return "—" }
        let a = abs(v)
        if a != 0 && (a >= 1e15 || a < 1e-6) { return String(format: "%.6g", v).replacingOccurrences(of: "-", with: "−") }
        return (formatter.string(from: NSNumber(value: v)) ?? String(v)).replacingOccurrences(of: "-", with: "−")
    }

    /// (tools-27) A number with its unit: "212 °F", "3.28 ft" — the angle marks close up, as written: "90°", "30′".
    static func withUnit(_ v: Double, _ u: UnitChoice) -> String {
        ["°", "′", "″"].contains(u.symbol) ? text(v) + u.symbol : "\(text(v)) \(u.symbol)"
    }

    private static let formatter: NumberFormatter = {
        let f = NumberFormatter()
        f.locale = Locale(identifier: "en_US")
        f.numberStyle = .decimal
        f.usesSignificantDigits = true
        f.minimumSignificantDigits = 1
        f.maximumSignificantDigits = 10
        return f
    }()
}

/// Unit converter: the kind of unit across the top; the value and its unit, the unit wanted and the answer large; and
/// beside it on a wide window, the value in every unit of its kind (a press makes that the unit wanted).
struct UnitConverterTool: View {
    @State private var category = UnitCategory.all[0]
    @State private var from = UnitCategory.all[0].units[UnitCategory.all[0].from]
    @State private var to = UnitCategory.all[0].units[UnitCategory.all[0].to]
    @State private var value = "1"

    private var result: Double? { UnitMath.number(value).map { UnitMath.convert($0, from: from.unit, to: to.unit) } }

    var body: some View {
        ToolPage {
            ScrollView(.horizontal, showsIndicators: false) {
                GlassGroup(spacing: 8) {
                    HStack(spacing: 8) {
                        ForEach(UnitCategory.all) { c in
                            Button {
                                pick(c)
                            } label: {
                                Label(c.name, systemImage: c.symbol).font(.sCallout.weight(c == category ? .semibold : .regular))
                            }
                            .glassButton(prominent: c == category, tint: c == category ? Color(hex: "#64d2ff") : nil)
                            .controlSize(.large)
                        }
                    }
                    .padding(.vertical, 2)
                }
            }
            ToolColumns(sideWidth: 400, breakpoint: 960) {
                converter
            } side: {
                everyUnit
            }
        }
        .onAppear(perform: takeHandOff)
    }

    /// (1.3.83) What the pin's converter had, its panel opened full screen: its kind, its two units and its value.
    private func takeHandOff() {
        let tools = ToolsCenter.shared
        guard let id = tools.takeInfo("kind"), let c = UnitCategory.all.first(where: { $0.id == id }) else { return }
        let f = tools.takeInfo("from"), t = tools.takeInfo("to")
        category = c
        from = c.units.first { $0.id == f } ?? c.units[c.from]
        to = c.units.first { $0.id == t } ?? c.units[c.to]
        if let v = tools.takeInfo("value") { value = v }
    }

    /// (tools-27) One column — the value, From, Swap between From and To, To — and the answer with its unit large
    /// ("212 °F"), what it came from under it ("= 100 °C").
    private var converter: some View {
        VStack(alignment: .leading, spacing: 20) {
            VStack(alignment: .leading, spacing: 12) {
                ToolField(label: "Value", text: $value, placeholder: "1", width: 260)
                unitPicker("From", selection: $from)
                Button {
                    withAnimation(Motion.snappy) {
                        let f = from
                        from = to
                        to = f
                    }
                } label: {
                    Image(systemName: "arrow.up.arrow.down")
                        .font(.system(size: 13, weight: .semibold))
                        .frame(width: 20, height: 18)
                }
                .glassButton()
                .controlSize(.large)
                .help("Swap From and To")
                .accessibilityLabel("Swap From and To")
                unitPicker("To", selection: $to)
            }
            VStack(alignment: .leading, spacing: 6) {
                Text(result.map { UnitMath.withUnit($0, to) } ?? "—")
                    .font(.system(size: 56, weight: .bold, design: .rounded).monospacedDigit())
                    .lineLimit(1)
                    .minimumScaleFactor(0.4)
                    .textSelection(.enabled)
                    .contentTransition(.numericText())
                if let v = UnitMath.number(value) {
                    Text("= \(UnitMath.withUnit(v, from))")
                        .font(.sBody)
                        .foregroundStyle(.secondary)
                }
                HStack {
                    Button {
                        if let r = result { copyToPasteboard(UnitMath.withUnit(r, to).replacingOccurrences(of: "−", with: "-")) }
                    } label: {
                        Label("Copy", systemImage: "doc.on.doc")
                    }
                    .glassButton()
                    .disabled(result == nil)
                    .help(result.map { "Copy \(UnitMath.withUnit($0, to))" } ?? "Copy")
                }
            }
            .padding(24)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card(radius: 22)
            .animation(Motion.snappy, value: result ?? 0)
        }
    }

    private func unitPicker(_ label: String, selection: Binding<UnitChoice>) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(label).font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
            Picker(label, selection: selection) {
                ForEach(category.units) { u in Text("\(u.name) (\(u.symbol))").tag(u) }
            }
            .labelsHidden()
            .pickerStyle(.menu)
            .controlSize(.large)
            .frame(width: 260, alignment: .leading)
        }
    }

    private var everyUnit: some View {
        PageSection(title: "In every unit") {
            if let v = UnitMath.number(value) {
                VStack(spacing: 0) {
                    ForEach(Array(category.units.enumerated()), id: \.element.id) { i, u in
                        if i > 0 { RowDivider(inset: 14) }
                        RowLink {
                            to = u
                        } label: {
                            HStack {
                                Text(u.name).font(.sBody)
                                Spacer()
                                Text(UnitMath.withUnit(UnitMath.convert(v, from: from.unit, to: u.unit), u))
                                    .font(.sBody.monospacedDigit())
                                    .foregroundStyle(u == to ? Theme.accent : Color.secondary)
                            }
                        }
                    }
                }
                .padding(6)
                .card()
            } else {
                Text("Type a number to see it in every unit.").font(.sCallout).foregroundStyle(.secondary)
            }
        }
    }

    private func pick(_ c: UnitCategory) {
        withAnimation(Motion.snappy) {
            category = c
            from = c.units[c.from]
            to = c.units[c.to]
        }
    }
}

/// The unit converter's pin, opened: the kind, the value and its unit, the unit wanted, and the answer.
struct UnitConverterCompact: View {
    @State private var category = UnitCategory.all[0]
    @State private var from = UnitCategory.all[0].units[UnitCategory.all[0].from]
    @State private var to = UnitCategory.all[0].units[UnitCategory.all[0].to]
    @State private var value = "1"

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Picker("Kind", selection: Binding(get: { category }, set: { c in
                category = c
                from = c.units[c.from]
                to = c.units[c.to]
            })) {
                ForEach(UnitCategory.all) { c in Text(c.name).tag(c) }
            }
            .labelsHidden()
            HStack(spacing: 8) {
                TextField("1", text: $value)
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 90)
                Picker("From", selection: $from) {
                    ForEach(category.units) { u in Text(u.symbol).tag(u) }
                }
                .labelsHidden()
                Image(systemName: "arrow.right").foregroundStyle(.secondary)
                Picker("To", selection: $to) {
                    ForEach(category.units) { u in Text(u.symbol).tag(u) }
                }
                .labelsHidden()
            }
            Text(UnitMath.number(value).map { UnitMath.withUnit(UnitMath.convert($0, from: from.unit, to: to.unit), to) } ?? "—")
                .font(.system(size: 30, weight: .bold, design: .rounded).monospacedDigit())
                .lineLimit(1)
                .minimumScaleFactor(0.5)
                .textSelection(.enabled)
            if let v = UnitMath.number(value) {
                Text("= \(UnitMath.withUnit(v, from))")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        }
        .quickCarry(carried)
    }

    /// (1.3.83) What Open Full Screen carries to the whole converter, once anything is changed from how the panel opens:
    /// the kind, the two units and the value (UnitConverterTool.takeHandOff).
    private var carried: [String: String] {
        let start = UnitCategory.all[0]
        guard category != start || from != start.units[start.from] || to != start.units[start.to] || value != "1" else { return [:] }
        return ["kind": category.id, "from": from.id, "to": to.id, "value": value]
    }
}

import AppKit
import SwiftUI

/// (1.3.51) An element's own page, grown out of its square in the table: its tile, name and kinds; (1.3.80) its shells
/// drawn flat, with the way to its atom, orbitals and electron cloud in 3D — Atoms & Orbitals, a tool of its own, where
/// the 3D view on this page was; its facts; how it compares with every element; its successive ionization energies;
/// its electrons, shell by shell and orbital by orbital; where it sits in the table; its neighbours; and its story. ←
/// and → step through the elements; (tools-29) the tool's heading has the one Back, "‹ Table" (⌘[), that returns to the
/// table.
struct PTElementPage: View {
    let element: PTElement
    let close: () -> Void
    let go: (Int) -> Void
    @FocusState private var focused: Bool
    /// The page's width inside its margins, which picks the layout.
    @State private var width: CGFloat = 0
    /// (the screenshot suite's -SimplPTHandOff atoms: Open in Atoms & Orbitals pressed once, the first time a page is up)
    @MainActor private static var handedOff = false

    private var e: PTElement { element }
    private var valenceSubs: [PTSubshell] { PTConfig.valence(of: e) }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                header
                title
                columns
            }
            .padding(.horizontal, 40)
            .padding(.top, 8)
            .padding(.bottom, 40)
            .frame(maxWidth: 1480, alignment: .leading)
            .background(GeometryReader { g in
                Color.clear
                    .onAppear { width = g.size.width - 80 }
                    .onChange(of: g.size.width) { _, w in width = w - 80 }
            })
            .frame(maxWidth: .infinity)
            .id(e.number)
            .transition(.opacity)
        }
        .focusable()
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(.leftArrow) { step(-1); return .handled }
        .onKeyPress(.rightArrow) { step(1); return .handled }
        .onAppear {
            focused = true
            // (the screenshot suite: -SimplPTHandOff atoms, Atoms & Orbitals as a student comes to it from an element's
            // facts — the button pressed a moment after the page is up, the picture held until then)
            if !Self.handedOff, UserDefaults.standard.string(forKey: "SimplPTHandOff") == "atoms" {
                Self.handedOff = true
                Shot.began()
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                    openAtom()
                    Shot.ended()
                }
            }
        }
    }

    /// (1.3.56) By the page's width (measured, as text that wraps would make a fitting test think nothing fits): wide,
    /// the atom and its electrons in a column beside the facts; middle, the atom and its electrons side by side, as
    /// tall as each other, the facts below; narrow, one column.
    @ViewBuilder
    private var columns: some View {
        if width >= 1000 {
            HStack(alignment: .top, spacing: 18) {
                // (each card its own height: the cards stretch only to match each other side by side)
                VStack(spacing: 18) {
                    atomCard.fixedSize(horizontal: false, vertical: true)
                    configCard.fixedSize(horizontal: false, vertical: true)
                }
                .frame(width: 430)
                right
            }
        } else if width >= 620 {
            VStack(spacing: 18) {
                HStack(alignment: .top, spacing: 18) {
                    atomCard.frame(maxWidth: .infinity)
                    configCard.frame(maxWidth: .infinity)
                }
                .fixedSize(horizontal: false, vertical: true)
                right
            }
        } else {
            VStack(spacing: 18) {
                atomCard.fixedSize(horizontal: false, vertical: true)
                configCard.fixedSize(horizontal: false, vertical: true)
                right
            }
        }
    }

    private func step(_ d: Int) {
        let n = e.number + d
        if n >= 1 && n <= PeriodicTable.elements.count { go(n) }
    }

    // MARK: The top

    /// The element before and after, at the page's top right. (tools-29) No Back of its own here: the tool's heading's
    /// "‹ Table" is the one way back, so two look-alike chevron buttons no longer sit one over the other.
    private var header: some View {
        HStack {
            Spacer()
            GlassGroup(spacing: 6) {
                HStack(spacing: 6) {
                Button { step(-1) } label: { Image(systemName: "chevron.left") }
                    .glassButton()
                    .disabled(e.number <= 1)
                    .help("The element before (←)")
                Text("\(e.number) / \(PeriodicTable.elements.count)")
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 6)
                Button { step(1) } label: { Image(systemName: "chevron.right") }
                    .glassButton()
                    .disabled(e.number >= PeriodicTable.elements.count)
                    .help("The element after (→)")
                }
            }
            .controlSize(.large)
        }
    }

    private var title: some View {
        HStack(alignment: .center, spacing: 22) {
            VStack(alignment: .leading, spacing: 0) {
                Text("\(e.number)").font(.system(size: 15, weight: .semibold).monospacedDigit())
                Spacer(minLength: 0)
                Text(e.symbol).font(.system(size: 46, weight: .bold)).frame(maxWidth: .infinity)
                Spacer(minLength: 0)
                Text(e.massText).font(.system(size: 12.5).monospacedDigit()).frame(maxWidth: .infinity)
            }
            .padding(12)
            .frame(width: 112, height: 112)
            .foregroundStyle(PTInk.on(e.kind.color))
            .background(e.kind.color, in: RoundedRectangle(cornerRadius: 20, style: .continuous))
            VStack(alignment: .leading, spacing: 10) {
                Text(e.name).font(.system(size: 44, weight: .bold)).tracking(-0.6)
                HStack(spacing: 8) {
                    chip(e.kind.label, e.kind.color)
                    chip("\(e.block)-block", Color(hex: PTColouring.blockColors[e.block] ?? "#8e8e93"))
                    chip("Group \(e.group.map(String.init) ?? "—") · Period \(e.period)", .secondary)
                    chip(e.stateWord, Color(hex: PTColouring.stateColors[e.phase] ?? "#8e8e93"))
                }
            }
        }
    }

    private func chip(_ text: String, _ color: Color) -> some View {
        HStack(spacing: 6) {
            Circle().fill(color).frame(width: 8, height: 8)
            Text(text).font(.sCallout.weight(.medium))
        }
        .padding(.horizontal, 11)
        .padding(.vertical, 5)
        .background(Color.primary.opacity(0.07), in: Capsule())
    }

    // MARK: The atom (1.3.80)

    /// Where the element's 3D view was: its shells drawn flat, what its atom is made of, and the way to its atom, its
    /// orbitals' shapes and its electron cloud in 3D — Atoms & Orbitals, a tool of its own, opened on this element in the
    /// same popup.
    private var atomCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            sectionTitle("Atom")
            HStack(alignment: .center, spacing: 16) {
                PTBohrSketch(element: e)
                    .frame(width: 124, height: 124)
                VStack(alignment: .leading, spacing: 6) {
                    Text("Shells \(e.shells.map(String.init).joined(separator: " · "))")
                        .font(.sCallout.weight(.semibold).monospacedDigit())
                        .fixedSize(horizontal: false, vertical: true)
                    Text("\(e.number) protons · \(e.neutrons) neutrons · \(e.number) electrons")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                    Text("Turn its atom, its orbitals' shapes and its electron cloud in 3D.")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 0)
            }
            Button(action: openAtom) {
                Label("Open in Atoms & Orbitals", systemImage: ToolKind.atoms.symbol)
                    .frame(maxWidth: .infinity)
            }
            .glassButton(prominent: true)
            .controlSize(.large)
            .help("\(e.name)'s atom, orbitals and electron cloud in 3D, in Atoms & Orbitals")
        }
        .padding(16)
        .frame(maxHeight: .infinity, alignment: .top)
        .card(radius: 20)
        .tourTarget("ptAtoms")
    }

    /// Atoms & Orbitals opened on this element, in the popup the table is in (the tour's last step, done).
    private func openAtom() {
        PTTour.shared.did(.atoms)
        ToolsCenter.shared.popUp(.atoms, info: ["element": String(e.number)])
    }

    // MARK: Electrons

    private var configCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            sectionTitle("Electron configuration")
            Text(PTConfig.pretty(e)).font(.system(size: 22, weight: .semibold).monospaced()).textSelection(.enabled)
            Text(PTConfig.full(e)).font(.sCallout.monospaced()).foregroundStyle(.secondary).textSelection(.enabled)
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 6) {
                Text("Shells").font(.sCallout).foregroundStyle(.secondary)
                Text(e.shells.map(String.init).joined(separator: " · ")).font(.sCallout.weight(.semibold).monospacedDigit())
            }
            // (the outer subshells' orbitals as boxes, each electron an arrow: one up in every box before any pairs)
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(alignment: .top, spacing: 16) {
                    ForEach(valenceSubs) { s in
                        VStack(spacing: 4) {
                            HStack(spacing: 2) {
                                ForEach(Array(s.filling.enumerated()), id: \.offset) { _, f in
                                    Text(f == 2 ? "↑↓" : (f == 1 ? "↑" : " "))
                                        .font(.system(size: 13, weight: .bold))
                                        .foregroundStyle(Theme.accent)
                                        .frame(width: 26, height: 26)
                                        .overlay(RoundedRectangle(cornerRadius: 4).strokeBorder(Color.primary.opacity(0.35), lineWidth: 1))
                                }
                            }
                            Text(s.name).font(.sCaption.monospaced()).foregroundStyle(.secondary)
                        }
                    }
                }
            }
            Text("Arrows are electrons: each orbital takes one before any takes a second (Hund's rule), the second with the opposite spin.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .card(radius: 20)
    }

    // MARK: The right

    private var right: some View {
        VStack(spacing: 18) {
            stats
            compare
            if (e.extra?.ionization.count ?? 0) > 1 { ionizations }
            HStack(alignment: .top, spacing: 18) {
                whereItSits
                neighbours
            }
            about
        }
    }

    private var stats: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: 10)], spacing: 10) {
            Group {
            stat("Atomic mass", "\(e.massText) u")
            stat("Electronegativity", e.en.map { PTElement.trim($0) } ?? "—")
            stat("1st ionization", e.ionization.map { "\(PTColouring.short($0)) kJ/mol" } ?? "—")
            stat("Atomic radius", e.radius.map { "\(Int($0)) pm" } ?? "—")
            stat("Electron affinity", e.affinity.map { "\(PTColouring.short($0)) kJ/mol" } ?? "—")
            stat("Melting point", e.melt.map { "\(PTColouring.short($0)) K" } ?? "—", PTElement.celsius(e.melt))
            stat("Boiling point", e.boil.map { "\(PTColouring.short($0)) K" } ?? "—", PTElement.celsius(e.boil))
            stat("Density", e.densityText)
            }
            Group {
            stat("Protons", "\(e.number)")
            stat("Neutrons (common)", "\(e.neutrons)")
            stat("Electrons", "\(e.number)")
            stat("Valence electrons", e.valenceElectrons.map(String.init) ?? "—")
            stat("Covalent radius", e.extra?.covalent.map { "\(Int($0)) pm" } ?? "—")
            stat("Van der Waals radius", e.extra?.vanDerWaals.map { "\(Int($0)) pm" } ?? "—")
            stat("Molar heat", e.extra?.molarHeat.map { "\(PTColouring.short($0)) J/(mol·K)" } ?? "—")
            stat("Block · group · period", "\(e.block) · \(e.group.map(String.init) ?? "—") · \(e.period)")
            }
        }
    }

    private func stat(_ k: String, _ v: String, _ sub: String? = nil) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(k).font(.sCaption).foregroundStyle(.secondary)
            Text(v).font(.system(size: 19, weight: .semibold).monospacedDigit()).lineLimit(1).minimumScaleFactor(0.6)
            // (a line kept for the note under the value whether there is one or not, so every box is the same size)
            let note = sub.flatMap { $0 == "—" ? nil : $0 }
            Text(note ?? " ").font(.sCaption2).foregroundStyle(.secondary).opacity(note == nil ? 0 : 1)
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .card(radius: 14)
    }

    /// (tools-12) Each bar is the element's rank — the share of the elements with a known value that it is above — so it
    /// shows what the words beside it say ("above 59%"); it used to show the value's place between the lowest and the
    /// highest, a different measure. In the colour the table gives the element in that view.
    private var compare: some View {
        VStack(alignment: .leading, spacing: 10) {
            sectionTitle("Compared with every element")
            ForEach([PTColouring.electronegativity, .radius, .ionization, .affinity, .melting, .boiling, .density]) { c in
                let rank = PTRank.percentile(c, e)
                HStack(spacing: 14) {
                    Text(c.label).font(.sCallout).frame(width: 150, alignment: .leading)
                    GeometryReader { g in
                        ZStack(alignment: .leading) {
                            Capsule().fill(Color.primary.opacity(0.08))
                            if let r = rank {
                                Capsule().fill(c.color(e)).frame(width: max(6, g.size.width * r))
                            }
                        }
                    }
                    .frame(height: 7)
                    .help(rank.map { "Above \(Int(($0 * 100).rounded()))% of the elements with a known \(c.label.lowercased())" } ?? "Not known")
                    Text(compareText(c)).font(.sCaption.monospacedDigit()).foregroundStyle(.secondary).frame(width: 200, alignment: .trailing)
                }
            }
            Text("Each bar: the share of the elements with a known value that this one is above.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
        }
        .padding(16)
        .card(radius: 20)
    }

    private func compareText(_ c: PTColouring) -> String {
        guard c.value(e) != nil else { return "not known" }
        let v = "\(c.text(e))\(c.unit.isEmpty || c == .electronegativity ? "" : " \(c.unit)")"
        guard let p = PTRank.percentile(c, e) else { return v }
        return "\(v) · above \(Int((p * 100).rounded()))%"
    }

    /// Successive ionization energies, on a log scale: the jump after the outer electrons shows where the core starts.
    private var ionizations: some View {
        let ies = e.extra?.ionization ?? []
        let top = log10(ies.max() ?? 10)
        let bottom = log10(max((ies.min() ?? 1) * 0.5, 1))
        let jump = ies.indices.dropFirst().first { ies[$0] / ies[$0 - 1] > 2.8 }
        return VStack(alignment: .leading, spacing: 10) {
            sectionTitle("Successive ionization energies")
            HStack(alignment: .bottom, spacing: 10) {
                ForEach(Array(ies.enumerated()), id: \.offset) { i, v in
                    VStack(spacing: 4) {
                        Text(PTColouring.short(v)).font(.sCaption2.monospacedDigit()).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.6)
                        RoundedRectangle(cornerRadius: 5, style: .continuous)
                            .fill(jump.map { i >= $0 } == true ? Color.orange : Theme.accent)
                            .frame(height: max(6, CGFloat((log10(v) - bottom) / max(top - bottom, 0.01)) * 110))
                        Text(ordinal(i + 1)).font(.sCaption2).foregroundStyle(.secondary)
                    }
                    .frame(maxWidth: .infinity)
                }
            }
            .frame(height: 150, alignment: .bottom)
            Text(jump.map { "kJ/mol. The big jump after the \(ordinal($0)) electron: the first \($0) are its outer electrons; the rest are held in the core." } ?? "kJ/mol, on a log scale.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(16)
        .card(radius: 20)
    }

    private func ordinal(_ n: Int) -> String {
        let suffix = (11...13).contains(n % 100) ? "th" : (n % 10 == 1 ? "st" : (n % 10 == 2 ? "nd" : (n % 10 == 3 ? "rd" : "th")))
        return "\(n)\(suffix)"
    }

    /// The table in miniature, the element lit, its group and period washed.
    private var whereItSits: some View {
        VStack(alignment: .leading, spacing: 10) {
            sectionTitle("Where it sits")
            Canvas { ctx, size in
                let gap: CGFloat = 2
                let c = min((size.width - 17 * gap) / 18, (size.height - 9 * gap) / 10)
                for x in PeriodicTable.elements {
                    let row = x.y <= 7 ? CGFloat(x.y - 1) : CGFloat(x.y - 1) + 0.4
                    let r = CGRect(x: CGFloat(x.x - 1) * (c + gap), y: row * (c + gap), width: c, height: c)
                    let same = x.x == e.x || (x.y == e.y && e.y <= 7)
                    let color: Color = x == e ? Color.yellow : (same ? Theme.accent.opacity(0.7) : Color.primary.opacity(0.14))
                    ctx.fill(Path(roundedRect: r, cornerRadius: 1.5), with: .color(color))
                }
            }
            .frame(height: 120)
            Text(e.y <= 7 ? "Group \(e.group.map(String.init) ?? "—"), period \(e.period)" : "\(e.kind.label)s, under the table (period \(e.period))")
                .font(.sCaption)
                .foregroundStyle(.secondary)
        }
        .padding(16)
        .frame(width: 280, alignment: .leading)
        .card(radius: 20)
    }

    private var neighbours: some View {
        let above = PeriodicTable.step(from: e, dx: 0, dy: -1)
        let below = e.y < 7 ? PeriodicTable.step(from: e, dx: 0, dy: 1) : nil
        let before = PeriodicTable.byNumber(e.number - 1)
        let after = PeriodicTable.byNumber(e.number + 1)
        return VStack(alignment: .leading, spacing: 10) {
            sectionTitle("Neighbours")
            LazyVGrid(columns: [GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10)], spacing: 10) {
                if let x = above, x.y <= 7, e.y <= 7 { neighbour("Above", x) }
                if let x = below, x.y <= 7 { neighbour("Below", x) }
                if let x = before { neighbour("Before", x) }
                if let x = after { neighbour("After", x) }
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 20)
    }

    private func neighbour(_ word: String, _ x: PTElement) -> some View {
        Button { go(x.number) } label: {
            HStack(spacing: 10) {
                Text(x.symbol)
                    .font(.system(size: 15, weight: .bold))
                    .foregroundStyle(PTInk.on(x.kind.color))
                    .frame(width: 34, height: 34)
                    .background(x.kind.color, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                VStack(alignment: .leading, spacing: 1) {
                    Text(word).font(.sCaption2).foregroundStyle(.secondary)
                    Text(x.name).font(.sCallout.weight(.semibold))
                }
                Spacer(minLength: 0)
            }
            .padding(8)
            .background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("Open \(x.name)")
    }

    private var about: some View {
        VStack(alignment: .leading, spacing: 10) {
            sectionTitle("About \(e.name)")
            Text(e.summary).font(.sBody).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
            if let looks = e.extra?.appearance, !looks.isEmpty {
                Label("Looks: \(looks)", systemImage: "eye").font(.sCallout).foregroundStyle(.secondary)
            }
            HStack(spacing: 16) {
                if let found = e.found, !found.isEmpty {
                    Label("Found by \(found)", systemImage: "sparkle.magnifyingglass").font(.sCallout).foregroundStyle(.secondary)
                }
                if let named = e.extra?.namedBy, !named.isEmpty {
                    Label("Named by \(named)", systemImage: "character.book.closed").font(.sCallout).foregroundStyle(.secondary)
                }
                if let url = URL(string: "https://en.wikipedia.org/wiki/\(e.name)") {
                    Link(destination: url) { Label("Wikipedia", systemImage: "arrow.up.right.square") }.font(.sCallout)
                }
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 20)
    }

    private func sectionTitle(_ t: String) -> some View {
        Text(t.uppercased()).font(.system(size: 11.5, weight: .semibold)).tracking(0.6).foregroundStyle(.secondary)
    }
}

/// (1.3.80) An element's shells drawn flat, for its facts page: the nucleus, each shell a ring, its electrons spaced
/// round it — what its 3D atom in Atoms & Orbitals shows, at a glance, in its colours, and still.
struct PTBohrSketch: View {
    let element: PTElement

    private var shells: [Int] { element.shells.isEmpty ? [element.number] : element.shells }

    var body: some View {
        Canvas { ctx, size in
            let c = CGPoint(x: size.width / 2, y: size.height / 2)
            let outer = min(size.width, size.height) / 2 - 4
            let core = max(5, outer * 0.16)
            let ring = (outer - core) / CGFloat(max(shells.count, 1))
            let dot = max(1.6, min(3.2, ring * 0.3))
            ctx.fill(Path(ellipseIn: CGRect(x: c.x - core, y: c.y - core, width: core * 2, height: core * 2)),
                     with: .color(Color(nsColor: AtomScene.protonColor)))
            for (i, n) in shells.enumerated() {
                let r = core + ring * CGFloat(i + 1)
                ctx.stroke(Path(ellipseIn: CGRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2)),
                           with: .color(Color.primary.opacity(0.22)), lineWidth: 1)
                for k in 0..<n {
                    // (each shell's first electron a little round from the last's, so they do not line up in a spoke)
                    let a = Double(k) / Double(max(n, 1)) * 2 * .pi - .pi / 2 + Double(i) * 0.35
                    let p = CGPoint(x: c.x + r * CGFloat(cos(a)), y: c.y + r * CGFloat(sin(a)))
                    ctx.fill(Path(ellipseIn: CGRect(x: p.x - dot, y: p.y - dot, width: dot * 2, height: dot * 2)),
                             with: .color(Color(nsColor: AtomScene.electronColor)))
                }
            }
        }
        .accessibilityElement()
        .accessibilityLabel("\(element.name)'s electron shells: \(shells.map(String.init).joined(separator: ", "))")
    }
}

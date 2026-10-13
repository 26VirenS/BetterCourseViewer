import SwiftUI

// The pieces the biology tools draw their pages with (spec §9), in the chemistry tools' look: the side columns'
// headings, the card in the right column (a title with its swatch, short labelled rows, actions), a legend, a sequence
// in groups of ten with its 5′ and 3′ ends, the quick "Open" a pin shows for a tool without a quick version, and the
// placeholder a tool shows until it is built.

/// A side column's heading: small capitals, as the molecule builder's ("PRESET", "VIEW").
struct BioHeading: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text.uppercased())
            .font(.system(size: 11.5, weight: .semibold))
            .tracking(0.6)
            .foregroundStyle(.secondary)
            .accessibilityAddTraits(.isHeader)
    }
}

/// The right column's card (spec §9 Cards): its title (with a colour swatch) and its rows, then its actions; text
/// wraps, nothing scrolls sideways.
struct BioCard<Content: View>: View {
    let title: String
    var swatch: Color? = nil
    var subtitle: String? = nil
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                if let swatch {
                    RoundedRectangle(cornerRadius: 4, style: .continuous)
                        .fill(swatch)
                        .frame(width: 14, height: 14)
                        .alignmentGuide(.firstTextBaseline) { d in d[VerticalAlignment.center] + 5 }
                        .accessibilityHidden(true)
                }
                VStack(alignment: .leading, spacing: 2) {
                    Text(title).font(.sTitle3).fixedSize(horizontal: false, vertical: true)
                    if let subtitle { Text(subtitle).font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true) }
                }
            }
            content
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 16)
    }
}

/// A labelled row of a card ("Function", "Structure", "Common mix-up"): its label small over its text.
struct BioFactRow: View {
    let label: String
    let text: String
    var tint: Color? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(label).font(.sCaption.weight(.semibold)).foregroundStyle(tint ?? .secondary)
            Text(text).font(.sCallout).fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }
}

/// A row of a facts list: its name left, its value right (the molecule builder's facts card).
struct BioValueRow: View {
    let label: String
    let value: String
    var color: Color = .primary
    var last = false

    var body: some View {
        VStack(spacing: 0) {
            HStack(alignment: .firstTextBaseline) {
                Text(label).font(.sCallout).foregroundStyle(.secondary)
                Spacer(minLength: 8)
                Text(value).font(.sCallout.weight(.semibold).monospacedDigit()).foregroundStyle(color).multilineTextAlignment(.trailing)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
            if !last { Divider().padding(.leading, 14) }
        }
        .accessibilityElement(children: .combine)
    }
}

/// A legend: a swatch and its words for each colour used (never colour alone).
struct BioLegend: View {
    let items: [(color: Color, text: String)]

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(items.indices, id: \.self) { i in
                HStack(spacing: 8) {
                    Circle().fill(items[i].color).frame(width: 10, height: 10)
                        .overlay(Circle().strokeBorder(Color.primary.opacity(0.2), lineWidth: 0.5))
                    Text(items[i].text).font(.sCaption)
                }
                .accessibilityElement(children: .combine)
            }
        }
    }
}

/// A nucleic acid written as §3.7 has it: monospaced, in groups of ten, each line's first position numbered, the 5′
/// and 3′ ends labelled; each base in its colour if asked (its letter always shown).
struct BioSequenceText: View {
    let sequence: String
    /// Written 5′→3′ (false: a strand written 3′→5′ under another).
    var fivePrimeFirst = true
    var coloured = true
    var groupsPerLine = 6

    var body: some View {
        let lines = BioSeq.lines(sequence, perLine: groupsPerLine)
        VStack(alignment: .leading, spacing: 4) {
            ForEach(lines.indices, id: \.self) { i in
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text("\(lines[i].start)")
                        .font(.system(size: 11, design: .monospaced))
                        .foregroundStyle(.secondary)
                        .frame(width: 36, alignment: .trailing)
                    if i == 0 { end(fivePrimeFirst ? "5′" : "3′") }
                    ForEach(lines[i].groups.indices, id: \.self) { g in group(lines[i].groups[g]) }
                    if i == lines.count - 1 { end(fivePrimeFirst ? "3′" : "5′") }
                }
            }
        }
        .textSelection(.enabled)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(fivePrimeFirst ? "5 prime to 3 prime" : "3 prime to 5 prime"): \(sequence.map(String.init).joined(separator: " "))")
    }

    private func end(_ s: String) -> some View {
        Text(s).font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary)
    }

    private func group(_ g: String) -> some View {
        coloured
            ? g.reduce(Text("")) { $0 + Text(String($1)).foregroundColor(BioColor.base($1)) }.font(.system(size: 14, weight: .semibold, design: .monospaced))
            : Text(g).font(.system(size: 14, design: .monospaced))
    }
}

/// A pin's popover for a biology tool without a quick version: its tile, its line, and Open.
struct BioQuickOpen: View {
    let kind: ToolKind
    let open: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top, spacing: 12) {
                IconTile(symbol: kind.symbol, color: kind.color, size: 40)
                VStack(alignment: .leading, spacing: 3) {
                    Text(kind.name).font(.sHeadline)
                    Text(kind.note).font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
            }
            Button(action: open) {
                Text("Open \(kind.name)").frame(maxWidth: .infinity)
            }
            .glassButton(prominent: true)
            .controlSize(.large)
            .keyboardShortcut(.defaultAction)
            .help("Open \(kind.name) full size (Return)")
        }
    }
}

/// What a biology tool shows until it is built: its tile, its name, its line, and the page chosen in its header.
struct BioToolPlaceholder: View {
    let kind: ToolKind
    var state: BioLaunchState? = nil
    @ObservedObject private var nav = BioNav.shared

    var body: some View {
        let pages = BioTools.pages(kind)
        let page = pages.first { $0.id == nav.page(kind) }
        VStack(spacing: 14) {
            IconTile(symbol: kind.symbol, color: kind.color, size: 64)
            Text(kind.name).font(.sTitle2)
            Text(kind.note).font(.sBody).foregroundStyle(.secondary).multilineTextAlignment(.center)
            if let page, pages.count > 1 {
                Label(page.title, systemImage: page.symbol)
                    .font(.sCallout.weight(.medium))
                    .padding(.horizontal, 12)
                    .padding(.vertical, 6)
                    .glassCapsule(tint: kind.color.opacity(0.2))
            }
        }
        .padding(40)
        .frame(maxWidth: 520)
        .card(radius: 20)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(.horizontal, 40)
        .padding(.bottom, 40)
        .accessibilityElement(children: .combine)
    }
}

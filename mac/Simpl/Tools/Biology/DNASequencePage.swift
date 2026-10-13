import SwiftUI

// DNA & RNA's Sequence page (spec §4.2 Sequence, §3.7): a strand typed or pasted 5′→3′ (DNA or RNA; cleaned as §3.7
// says, a bad letter named with its place, U in DNA offered as a switch), its complement aligned under it and written
// 3′→5′, its reverse complement read 5′→3′ (each named for what it is), and its facts — length, GC content, counts with
// Chargaff's check, exact molar masses, and a rough melting temperature — with Show in 3D, Transcribe (Gene
// Expression, this as its coding strand) and Random.

struct DNASequencePage: View {
    @ObservedObject var store: DNAStore
    @ObservedObject private var nav = BioNav.shared
    @FocusState private var editing: Bool

    var body: some View {
        let input = store.input
        let facts = DNASequenceFacts(input.letters, kind: store.kind)
        PTWorkLayout {
            controls(input)
        } centre: {
            strands(facts, valid: input.isValid)
        } right: {
            numbers(facts, input: input)
        }
    }

    // MARK: Left: the strand and what to do with it

    private func controls(_ input: BioSequenceInput) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Strand, 5′ to 3′")
            Picker("Kind", selection: $store.kind) {
                Text("DNA").tag(BioSeqKind.dna)
                Text("RNA").tag(BioSeqKind.rna)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("DNA uses A, C, G, T; RNA uses A, C, G, U")
            TextEditor(text: $store.text)
                .font(.system(size: 14, design: .monospaced))
                .scrollContentBackground(.hidden)
                .focused($editing)
                .padding(8)
                .frame(height: 150)
                .card(radius: 12)
                .overlay(alignment: .topLeading) {
                    if store.text.isEmpty {
                        Text("Type or paste a strand, 5′ to 3′").font(.sCallout).foregroundStyle(.secondary).padding(14).allowsHitTesting(false)
                    }
                }
                .accessibilityLabel("Strand, 5 prime to 3 prime")
                .help("Spaces, numbers and a FASTA header line are ignored (⌘F)")
            messages(input)
            actions(input)
            Text("Up to 3,000 letters. N stands for an unknown base.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            // (⌘F puts the cursor in the strand)
            Button("") { editing = true }
                .keyboardShortcut("f", modifiers: .command)
                .opacity(0)
                .frame(width: 0, height: 0)
                .accessibilityHidden(true)
        }
        .font(.sCallout)
    }

    @ViewBuilder
    private func messages(_ input: BioSequenceInput) -> some View {
        if let s = input.suggestion {
            VStack(alignment: .leading, spacing: 6) {
                ToolNote(text: "This looks like \(s.label). Switch?", symbol: "questionmark.circle.fill", tint: .blue)
                Button("Switch to \(s.label)") { store.kind = s }
                    .glassButton(prominent: true)
            }
        } else if let m = input.message {
            ToolNote(text: m)
        }
    }

    private func actions(_ input: BioSequenceInput) -> some View {
        let dna = store.kind == .dna
        let letters = input.letters
        return VStack(alignment: .leading, spacing: 8) {
            Button {
                store.clearHelixSelection()
                withAnimation(Motion.gentle) { nav.set(.dna, "helix") }
            } label: {
                Label("Show in 3D", systemImage: "cube").frame(maxWidth: .infinity)
            }
            .glassButton(prominent: true)
            .disabled(store.helixSequence.isEmpty)
            .help(dna ? "This strand as a double helix (its first 40 base pairs)" : "The DNA this RNA is copied from, as a double helix")
            Button {
                BioTools.open(.gene, info: ["sequence": BioSeq.toDNA(letters)])
            } label: {
                Label("Transcribe in Gene Expression", systemImage: "text.badge.plus").frame(maxWidth: .infinity)
            }
            .glassButton()
            .disabled(!dna || input.sequence == nil)
            .help(dna ? "Open Gene Expression with this strand as the coding strand" : "Transcription starts from DNA: switch to DNA")
            Button {
                store.randomSequence()
            } label: {
                Label("Random 20 bp", systemImage: "dice").frame(maxWidth: .infinity)
            }
            .glassButton()
            .help("A random 20-base DNA strand")
        }
    }

    // MARK: Middle: the strands

    private func strands(_ f: DNASequenceFacts, valid: Bool) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                if f.sequence.isEmpty {
                    Text("Type a strand on the left to see its partner and its facts.")
                        .font(.sCallout).foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(18)
                        .card(radius: 16)
                } else {
                    aligned(f)
                    reverse(f)
                    explainer(f)
                }
            }
            .padding(.bottom, 8)
        }
    }

    /// The strand and its complement, aligned: each line of up to 50 the strand 5′→3′, a bar for each pair, and the
    /// complement 3′→5′ under it.
    private func aligned(_ f: DNASequenceFacts) -> some View {
        let lines = f.alignedLines()
        return VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Both strands").font(.sHeadline)
                Spacer()
                Button {
                    copyToPasteboard(f.complement)
                } label: {
                    Label("Copy complement", systemImage: "doc.on.doc").font(.sCaption.weight(.medium))
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .help("Copy the complement, written 3′→5′ as shown")
            }
            Text("Strand typed (5′→3′) on top; its complement under it, written 3′→5′ — the strands are antiparallel.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            LazyVStack(alignment: .leading, spacing: 12) {
                ForEach(lines.indices, id: \.self) { i in
                    alignedLine(lines[i], first: i == 0, last: i == lines.count - 1)
                }
            }
            .textSelection(.enabled)
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 16)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Strand 5 prime to 3 prime: \(f.sequence.map(String.init).joined(separator: " ")). Complement 3 prime to 5 prime: \(f.complement.map(String.init).joined(separator: " "))")
    }

    private func alignedLine(_ line: (start: Int, top: [String], bottom: [String]), first: Bool, last: Bool) -> some View {
        let mono = Font.system(size: 14, weight: .semibold, design: .monospaced)
        return HStack(alignment: .top, spacing: 8) {
            Text("\(line.start)")
                .font(.system(size: 11, design: .monospaced))
                .foregroundStyle(.secondary)
                .frame(width: 40, alignment: .trailing)
                .padding(.top, 2)
            VStack(alignment: .leading, spacing: 1) {
                row(line.top, lead: first ? "5′" : "  ", tail: last ? "3′" : "  ", font: mono)
                Text("   " + line.top.map { String(repeating: "|", count: $0.count) }.joined(separator: " "))
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(.tertiary)
                row(line.bottom, lead: first ? "3′" : "  ", tail: last ? "5′" : "  ", font: mono)
            }
        }
    }

    private func row(_ groups: [String], lead: String, tail: String, font: Font) -> some View {
        let body = groups.enumerated().reduce(Text("")) { acc, g in
            let letters = g.element.reduce(Text("")) { $0 + Text(String($1)).foregroundColor(BioColor.base($1)) }
            return acc + (g.offset > 0 ? Text(" ") : Text("")) + letters
        }
        return (Text(lead + " ").foregroundColor(.secondary) + body + Text(" " + tail).foregroundColor(.secondary)).font(font)
    }

    private func reverse(_ f: DNASequenceFacts) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Reverse complement").font(.sHeadline)
                Spacer()
                Button {
                    copyToPasteboard(f.reverseComplement)
                } label: {
                    Label("Copy", systemImage: "doc.on.doc").font(.sCaption.weight(.medium))
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .help("Copy the reverse complement, 5′→3′")
            }
            Text("The partner strand read the usual way, 5′→3′: the complement backwards.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            BioSequenceText(sequence: f.reverseComplement, groupsPerLine: 5)
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 16)
    }

    private func explainer(_ f: DNASequenceFacts) -> some View {
        let sample = String(f.sequence.prefix(6))
        let comp = BioSeq.complement(sample, rna: f.isRNA)
        let rev = BioSeq.reverseComplement(sample, rna: f.isRNA)
        return VStack(alignment: .leading, spacing: 8) {
            Text("Complement or reverse complement?").font(.sHeadline)
            Text("For 5′-\(sample)-3′, the complement is 3′-\(comp)-5′: the same partner bases, written under it. Read that partner 5′→3′ and you have the reverse complement, 5′-\(rev)-3′. They are one strand written two ways; databases and primers use the 5′→3′ form.")
                .font(.sCallout)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 16)
    }

    // MARK: Right: the facts

    private func numbers(_ f: DNASequenceFacts, input: BioSequenceInput) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Facts")
            VStack(spacing: 0) {
                BioValueRow(label: "Length", value: f.isRNA ? toolPlural(f.length, "nucleotide") : "\(f.length) nt · \(f.length) bp")
                BioValueRow(label: "GC content", value: String(format: "%.1f%%", f.gcPercent))
                BioValueRow(label: "A", value: "\(f.counts.a)", color: BioColor.base("A"))
                if f.isRNA {
                    BioValueRow(label: "U", value: "\(f.counts.u)", color: BioColor.base("U"))
                } else {
                    BioValueRow(label: "T", value: "\(f.counts.t)", color: BioColor.base("T"))
                }
                BioValueRow(label: "G", value: "\(f.counts.g)", color: BioColor.base("G"))
                BioValueRow(label: "C", value: "\(f.counts.c)", color: BioColor.base("C"), last: f.counts.n == 0)
                if f.counts.n > 0 { BioValueRow(label: "N (unknown)", value: "\(f.counts.n)", last: true) }
            }
            .card(radius: 14)
            if let c = f.chargaff { chargaff(c) } else if f.length > 0 {
                Text("RNA is usually a single strand, so Chargaff's rule (A = T, G = C) does not apply to it.")
                    .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            masses(f)
            if let tm = f.meltingTemperature { melting(tm, f) }
        }
    }

    private func chargaff(_ c: (counts: BioBaseCounts, purinePercent: Double)) -> some View {
        let ok = c.counts.a == c.counts.t && c.counts.g == c.counts.c
        return BioCard(title: "Chargaff's rule", swatch: ok ? Color.green : Color.orange, subtitle: "In the double strand this makes") {
            BioFactRow(label: "Counts", text: "A \(c.counts.a) = T \(c.counts.t) · G \(c.counts.g) = C \(c.counts.c)")
            BioFactRow(label: "Purines", text: String(format: "%%A + %%G = %.1f%%", c.purinePercent) + (ok ? " ✓" : ""))
            Text("Every A pairs with a T and every G with a C, so in double-stranded DNA A = T, G = C, and purines are half of all bases. One strand alone need not follow it.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }

    private func masses(_ f: DNASequenceFacts) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            BioHeading("Molar mass")
            VStack(spacing: 0) {
                BioValueRow(label: "This strand", value: DNAText.number(f.mass) + " g/mol")
                BioValueRow(label: "Its partner", value: DNAText.number(f.partnerMass) + " g/mol", last: f.duplexMass == nil)
                if let d = f.duplexMass {
                    BioValueRow(label: "Double strand", value: DNAText.number(d) + " g/mol")
                    BioValueRow(label: "Lab estimate", value: "≈ " + DNAText.number(f.labEstimate, digits: 0) + " g/mol", last: true)
                }
            }
            .card(radius: 14)
            Text(f.isRNA
                 ? "Each nucleotide's monophosphate (AMP 347.22, CMP 323.20, GMP 363.22, UMP 324.18 g/mol), less 18.02 for each bond between them."
                 : "Exact: each nucleotide's monophosphate (dAMP 331.22, dCMP 307.20, dGMP 347.22, dTMP 322.21 g/mol), less 18.02 (a water) for each phosphodiester bond, per strand. The lab estimate, about 650 g/mol per base pair, is for the sodium salt.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }

    private func melting(_ tm: (value: Double, rule: String), _ f: DNASequenceFacts) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            BioHeading("Melting temperature")
            VStack(spacing: 0) {
                BioValueRow(label: "Estimate", value: String(format: "%.1f °C", tm.value))
                BioValueRow(label: "Rule", value: tm.rule, last: true)
            }
            .card(radius: 14)
            Text("A rough estimate for a short primer in 50 mM salt: Wallace's rule up to 14 nt, the GC formula above. Real melting depends on salt, concentration and neighbouring bases.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }
}

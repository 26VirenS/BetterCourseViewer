import SwiftUI

// The Cell Explorer's smaller pieces: a card's row with its key term in bold (§9: "Bold the key term once per card"),
// the scale's chip and the scale bar (§4.1 item 7: 1, 5 or 10 µm, chosen so it is 60–160 pt long at the current zoom,
// always shown, with "Not to scale" beside it in teaching scale), and the quiz's prompt over the scene and its panel in
// the right column (§4.1 Nice: "Find the …" and "Name it", ten questions, then the score and the misses).

/// A card's labelled row whose text may bold a term (`**turgor pressure**`).
struct CEFactRow: View {
    let label: String
    let markdown: String
    var tint: Color? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(label).font(.sCaption.weight(.semibold)).foregroundStyle(tint ?? .secondary)
            Text(CEFactRow.attributed(markdown)).font(.sCallout).fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }

    static func attributed(_ s: String) -> AttributedString {
        (try? AttributedString(markdown: s, options: AttributedString.MarkdownParsingOptions(interpretedSyntax: .inlineOnlyPreservingWhitespace)))
            ?? AttributedString(s.replacingOccurrences(of: "**", with: ""))
    }
}

/// "Teaching scale" or "True scale", in the scene's top bar.
struct CEScaleChip: View {
    let trueScale: Bool

    var body: some View {
        Text(trueScale ? "True scale" : "Teaching scale")
            .font(.sCaption.weight(.semibold))
            .padding(.horizontal, 9)
            .padding(.vertical, 4)
            .background((trueScale ? Color.green : Color.orange).opacity(0.16), in: Capsule())
            .foregroundStyle(trueScale ? Color.green : Color.orange)
            .help(trueScale ? "Every part at its real size" : "Small parts drawn larger than they are, and fewer of them")
    }
}

/// A scale bar: the first of the preferred lengths (1, 5 or 10 µm) that comes out 60–160 pt long at this zoom, else
/// the nearest other length that does (a bacterium seen close needs 0.5 µm; the membrane is in nm).
struct CEScaleBar: View {
    /// Points per scene unit at the model's centre (BioSceneView's onScale).
    let pointsPerUnit: Double
    var unit = "µm"
    var preferred: [Double] = [1, 5, 10]
    var others: [Double] = [0.1, 0.2, 0.5, 2, 20, 50, 100]
    var badge: String? = nil

    /// The room the bar takes at the bottom of a scene, with its padding: kept clear of the scene's labels
    /// (BioSceneView's labelBottom), so a label in the left column never sits under it.
    static let room: CGFloat = 58

    var length: Double? {
        guard pointsPerUnit > 0, pointsPerUnit.isFinite else { return nil }
        if let l = preferred.first(where: { (60...160).contains($0 * pointsPerUnit) }) { return l }
        let all = (preferred + others).sorted()
        if let l = all.first(where: { (60...160).contains($0 * pointsPerUnit) }) { return l }
        return all.min { abs($0 * pointsPerUnit - 110) < abs($1 * pointsPerUnit - 110) }
    }

    private func text(_ l: Double) -> String {
        if unit == "µm" && l < 1 { return "\(Int((l * 1000).rounded())) nm" }
        return (l == l.rounded() ? "\(Int(l))" : String(halfUp: "%g", l)) + " " + unit
    }

    var body: some View {
        if let l = length {
            let w = min(max(l * pointsPerUnit, 8), 400)
            HStack(alignment: .bottom, spacing: 10) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(text(l)).font(.sCaption.weight(.semibold).monospacedDigit())
                    Canvas { ctx, size in
                        var p = Path()
                        p.move(to: CGPoint(x: 0.75, y: 0))
                        p.addLine(to: CGPoint(x: 0.75, y: size.height))
                        p.move(to: CGPoint(x: 0, y: size.height - 1))
                        p.addLine(to: CGPoint(x: size.width, y: size.height - 1))
                        p.move(to: CGPoint(x: size.width - 0.75, y: 0))
                        p.addLine(to: CGPoint(x: size.width - 0.75, y: size.height))
                        ctx.stroke(p, with: .color(.primary), lineWidth: 1.5)
                    }
                    .frame(width: w, height: 7)
                }
                if let badge {
                    Text(badge)
                        .font(.sCaption2.weight(.semibold))
                        .padding(.horizontal, 7)
                        .padding(.vertical, 3)
                        .background(Color.orange.opacity(0.18), in: Capsule())
                        .foregroundStyle(Color.orange)
                }
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("Scale bar: \(text(l))\(badge.map { ", \($0.lowercased())" } ?? "")")
        }
    }
}

/// A sideways wobble (a wrong answer), driven by a count that goes up by one each time.
struct CEShake: GeometryEffect {
    var animatableData: Double

    func effectValue(size: CGSize) -> ProjectionTransform {
        ProjectionTransform(CGAffineTransform(translationX: 7 * sin(animatableData * .pi * 4), y: 0))
    }
}

/// The quiz's question over the scene: "Find the Golgi apparatus" or "Name the glowing part", and what was said.
struct CEQuizPrompt: View {
    let quiz: CEQuiz
    let question: CEQuiz.Question
    let feedback: (right: Bool, text: String)?
    let shake: Double

    var body: some View {
        VStack(spacing: 4) {
            Text(quiz.mode == .find ? "Find the \(CEPart.inSentence(CEParts.part(quiz.kind, question.part)?.name ?? question.part))" : "Name the glowing part")
                .font(.sHeadline)
            if let f = feedback {
                Label(f.text, systemImage: f.right ? "checkmark.circle.fill" : "xmark.circle.fill")
                    .font(.sCallout.weight(.medium))
                    .foregroundStyle(f.right ? Color.green : Color.orange)
            } else {
                Text("Question \(quiz.index + 1) of \(quiz.questions.count)").font(.sCaption).foregroundStyle(.secondary)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 9)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        .modifier(CEShake(animatableData: shake))
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.updatesFrequently)
    }
}

/// The quiz in the right column: the question's choices ("Name it") or how to answer ("Find the …"), then the score
/// and the parts missed.
struct CEQuizPanel: View {
    let quiz: CEQuiz
    let kind: CEKind
    let feedback: (right: Bool, text: String)?
    let answer: (String) -> Void
    let skip: () -> Void
    let again: () -> Void
    let end: () -> Void

    var body: some View {
        BioCard(title: quiz.mode == .find ? "Find the part" : "Name it", subtitle: kind.title) {
            if quiz.done {
                Text("\(quiz.score) of \(quiz.questions.count)").font(.system(size: 34, weight: .bold).monospacedDigit())
                Text(quiz.score == quiz.questions.count ? "Every one right first time." : "Right first time. The parts below are worth a second look.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                if !quiz.misses.isEmpty {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("To look at again").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                        ForEach(Array(Set(quiz.misses)).sorted(), id: \.self) { id in
                            if let p = CEParts.part(kind, id) {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(p.name).font(.sCallout.weight(.semibold))
                                    Text(CEFactRow.attributed(p.function)).font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                                }
                            }
                        }
                    }
                }
                HStack(spacing: 8) {
                    Button(action: again) { Text("Try again").frame(maxWidth: .infinity) }.glassButton(prominent: true)
                    Button(action: end) { Text("Done").frame(maxWidth: .infinity) }.glassButton()
                }
                .controlSize(.large)
            } else if let q = quiz.current {
                Text("Question \(quiz.index + 1) of \(quiz.questions.count) · \(quiz.score) right").font(.sCallout).foregroundStyle(.secondary)
                if quiz.mode == .find {
                    Text("Press the \(CEPart.inSentence(CEParts.part(kind, q.part)?.name ?? q.part)) in the model. Turn it and zoom in if you need to; the labels are off.")
                        .font(.sCallout)
                        .fixedSize(horizontal: false, vertical: true)
                } else {
                    Text("Which part is glowing?").font(.sCallout)
                    VStack(spacing: 8) {
                        ForEach(q.choices, id: \.self) { name in
                            Button { answer(name) } label: { Text(name).frame(maxWidth: .infinity) }
                                .glassButton()
                                .controlSize(.large)
                                .disabled(feedback != nil)
                        }
                    }
                }
                if let f = feedback {
                    Label(f.text, systemImage: f.right ? "checkmark.circle.fill" : "xmark.circle.fill")
                        .font(.sCallout.weight(.medium))
                        .foregroundStyle(f.right ? Color.green : Color.orange)
                }
                HStack(spacing: 8) {
                    Button(action: skip) { Text("Skip").frame(maxWidth: .infinity) }.glassButton()
                    Button(action: end) { Text("End quiz").frame(maxWidth: .infinity) }.glassButton()
                }
            }
        }
    }
}

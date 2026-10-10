import AppKit
import SwiftUI
import WebKit

// (1.3.17) A Knewton Alta assignment, open in a tool window, drawn as a quiz is being taken in Simpl: its objectives in
// place of the question tiles, and a mastery bar in place of the progress bar. Alta's own page stays in the middle as
// Alta draws it — the question, its math and its graph untouched; nothing is answered, submitted or hidden for the
// student. (The reference: Knewton_Alta_Skin_Dev_Reference.docx and alta-skin-reference.xlsx, 2026-10-10.)

/// The page's side: a script in every frame of a tool window that, on an Alta page only, reads Alta's own `content`
/// answer as Alta's page receives it — a copy of it; the request and the page are left as they are — and passes the app
/// just the assignment, its objectives and how far each is mastered, and what kind of item is showing. Never the
/// answer key (`correct_answer`, `success_condition`), never who the student is (`userId`, `registrationId`,
/// `ltiEnrollment`): those are not read, so they cannot leave the page. It sends nothing anywhere else.
enum AltaHook {
    static let handler = "simplAlta"

    static var script: WKUserScript {
        WKUserScript(source: source, injectionTime: .atDocumentStart, forMainFrameOnly: false)
    }

    // ALTA-HOOK-BEGIN (scripts/dev/alta-test.mjs reads the script from here)
    static let source = #"""
    (function () {
      var host = location.hostname || '';
      var alta = /(^|\.)knewtonalta\.com$/i.test(host) || location.pathname.indexOf('/mock-alta/') === 0;
      if (!alta || window.__simplAlta) return;
      window.__simplAlta = true;
      var post = function (m) { try { window.webkit.messageHandlers.simplAlta.postMessage(JSON.stringify(m)); } catch (e) {} };
      var num = function (v) { if (v === null || v === undefined || v === '') return null; var n = Number(v); return isFinite(n) ? n : null; };
      var str = function (v) { return typeof v === 'string' && v ? v.slice(0, 300) : (typeof v === 'number' ? String(v) : null); };
      // Only these fields are read. The answer key and the student's identity are never touched.
      var pick = function (j) {
        var e = j.enrollment || {}, p = e.path || {}, d = e.dueDate || {}, a = j.analytics || {}, sp = a.statusAndProgress || {};
        var st = (j.states || [])[0] || {}, atom = st.atom || {}, ci = st.compoundInstance || {}, lo = atom.learningObjective || {};
        var c = (atom.data && atom.data.content) || {};
        var targets = (Array.isArray(sp.targets) ? sp.targets : []).map(function (t) {
          return { id: str(String(t.target_id || t.targetId || t.id || '').replace(/^lref-/, '')), progress: num(t.progress), status: str(t.status) };
        });
        var objectives = (Array.isArray(p.pathLearningObjectives) ? p.pathLearningObjectives : []).map(function (o) {
          return { id: str(o.learningObjectiveId || o.loId || o.id), name: str(o.description || o.name || o.title) };
        });
        var history = (j.history && Array.isArray(j.history.sequences) ? j.history.sequences : []).slice(0, 40).map(function (s) {
          return { right: num(s.numCorrectResponses) || 0, wrong: num(s.numIncorrectResponses) || 0, skipped: num(s.numSkippedAssessments) || 0, lessons: num(s.numInstructional) || 0 };
        });
        return {
          kind: 'content', path: location.pathname,
          name: str(p.name), pathType: str(p.type), threshold: num(p.masteryThreshold),
          objectives: objectives, targets: targets,
          percent: num(a.percentComplete), progress: num(sp.progress), status: str(sp.status),
          completed: !!e.completed, started: !!e.startedAt, due: num(d.effectiveDueDate), lateAllowed: !!d.lateSubmissionEnabled, ended: !!p.ended,
          current: {
            id: str(atom.learningObjectiveId), name: str(lo.description || atom.name),
            low: num(lo.estimatedQuestionsLow), high: num(lo.estimatedQuestionsHigh),
            source: str(ci.source), instance: str(ci.type), purpose: str(atom.purpose),
            item: str(c.type === 'custom' ? c.custom_type : c.type)
          },
          history: history, stuck: j.stuckLo !== null && j.stuckLo !== undefined
        };
      };
      var seen = function (url, text) {
        if (!/content/i.test(String(url || ''))) return;
        var j; try { j = typeof text === 'string' ? JSON.parse(text) : text; } catch (e) { return; }
        if (j && Array.isArray(j.states) && j.enrollment) post(pick(j));
      };
      var fetch0 = window.fetch;
      if (fetch0) {
        window.fetch = function (input) {
          var asked = typeof input === 'string' ? input : (input && input.url) || '';
          var p = fetch0.apply(this, arguments);
          p.then(function (r) {
            try {
              var at = (r && r.url) || asked;
              if (r && /json/i.test(r.headers.get('content-type') || '') && /content/i.test(at)) {
                r.clone().text().then(function (t) { seen(at, t); }, function () {});
              }
            } catch (e) {}
          }, function () {});
          return p;
        };
      }
      var open0 = XMLHttpRequest.prototype.open, send0 = XMLHttpRequest.prototype.send;
      XMLHttpRequest.prototype.open = function (m, u) { this.__simplUrl = u; return open0.apply(this, arguments); };
      XMLHttpRequest.prototype.send = function () {
        var x = this;
        if (/content/i.test(String(x.__simplUrl || ''))) {
          x.addEventListener('load', function () {
            try { seen(x.responseURL || x.__simplUrl, x.responseType === 'json' ? x.response : (x.responseType === '' || x.responseType === 'text' ? x.responseText : null)); } catch (e) {}
          });
        }
        return send0.apply(this, arguments);
      };
      // where the page is: an assignment's player, or another of Alta's pages (the window goes back to plain there)
      var where = function () { post({ kind: 'page', path: location.pathname, assignment: /\/assignment\//.test(location.pathname) }); };
      ['pushState', 'replaceState'].forEach(function (k) {
        var f = history[k];
        history[k] = function () { var r = f.apply(this, arguments); setTimeout(where, 0); return r; };
      });
      window.addEventListener('popstate', where);
      where();
    })();
    """#
    // ALTA-HOOK-END
}

/// What the page passed: the assignment and where the student stands in it.
struct AltaReport: Decodable, Equatable {
    struct Target: Decodable, Equatable { var id: String?; var progress: Double?; var status: String? }
    struct Objective: Decodable, Equatable { var id: String?; var name: String? }
    struct Current: Decodable, Equatable {
        var id: String?
        var name: String?
        var low: Double?
        var high: Double?
        var source: String?
        var instance: String?
        var purpose: String?
        var item: String?
    }
    struct Step: Decodable, Equatable { var right: Int; var wrong: Int; var skipped: Int; var lessons: Int }

    var path: String?
    var name: String?
    var pathType: String?
    var threshold: Double?
    var objectives: [Objective]
    var targets: [Target]
    var percent: Double?
    var progress: Double?
    var status: String?
    var completed: Bool
    var started: Bool
    var due: Double?
    var lateAllowed: Bool
    var ended: Bool
    var current: Current
    var history: [Step]
    var stuck: Bool
}

/// One objective as the frame shows it: its name, how far it is mastered (0…1), whether it is the one being worked on.
struct AltaObjective: Identifiable, Equatable {
    let id: String
    let number: Int
    let name: String
    let mastery: Double
    let current: Bool
    var mastered: Bool { mastery >= 0.999 }
}

/// The frame's state for one tool window: the last report, while the window shows an Alta assignment.
@MainActor
final class AltaState: ObservableObject {
    @Published private(set) var report: AltaReport?
    @Published private(set) var onAssignment = false
    /// Objective names seen so far, by id: Alta names only the objective on screen, so the others are learned as they
    /// come and kept (on this Mac) for the next visit.
    private var names: [String: String] = UserDefaults.standard.dictionary(forKey: AltaState.namesKey) as? [String: String] ?? [:]
    private static let namesKey = "SimplAltaObjectiveNames"
    lazy var messages: WKScriptMessageHandler = AltaMessages(state: self)

    /// The frame is drawn: an assignment's player, with what Alta said about it.
    var active: Bool { onAssignment && report != nil }

    func take(_ text: String) {
        guard let data = text.data(using: .utf8),
              let head = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }
        switch head["kind"] as? String {
        case "page":
            let on = head["assignment"] as? Bool ?? false
            if on != onAssignment { withAnimation(Motion.gentle) { onAssignment = on } }
            if !on { report = nil }
        case "content":
            guard let r = try? JSONDecoder().decode(AltaReport.self, from: data) else { return }
            if let id = r.current.id, let name = r.current.name, !name.isEmpty, names[id] != name {
                names[id] = name
                if names.count > 400 { names = Dictionary(uniqueKeysWithValues: names.suffix(300).map { ($0.key, $0.value) }) }
                UserDefaults.standard.set(names, forKey: Self.namesKey)
            }
            withAnimation(Motion.gentle) {
                onAssignment = true
                report = r
            }
        default:
            break
        }
    }

    /// The objectives in Alta's order (the assignment's list, else the progress targets, else the one on screen), each
    /// with its mastery: its target's progress, or full once its status says it is mastered.
    var objectives: [AltaObjective] {
        guard let r = report else { return [] }
        var ids: [String] = r.objectives.compactMap(\.id)
        if ids.isEmpty { ids = r.targets.compactMap(\.id) }
        if ids.isEmpty, let c = r.current.id { ids = [c] }
        var seen = Set<String>()
        ids = ids.filter { seen.insert($0).inserted }
        return ids.enumerated().map { k, id in
            let t = r.targets.first { $0.id == id }
            let done = (t?.status ?? "").lowercased()
            let mastered = ["complete", "completed", "mastered", "done"].contains(done)
            let p = mastered ? 1 : min(max(t?.progress ?? 0, 0), 1)
            let named = r.objectives.first { $0.id == id }?.name ?? (r.current.id == id ? r.current.name : nil) ?? names[id]
            return AltaObjective(id: id, number: k + 1, name: named ?? "Objective \(k + 1)", mastery: p, current: r.current.id == id)
        }
    }

    /// The whole assignment's mastery, 0…1: Alta's percent complete, else its progress, else the objectives' mean.
    var mastery: Double {
        guard let r = report else { return 0 }
        if r.completed || (r.status ?? "").lowercased() == "complete" { return 1 }
        if let p = r.percent { return min(max(p / 100, 0), 1) }
        if let p = r.progress { return min(max(p, 0), 1) }
        let list = objectives
        return list.isEmpty ? 0 : list.map(\.mastery).reduce(0, +) / Double(list.count)
    }
}

/// The page's messages, to the window's state (held weakly: the web view's controller keeps its handlers).
final class AltaMessages: NSObject, WKScriptMessageHandler {
    weak var state: AltaState?
    init(state: AltaState) { self.state = state }

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let text = message.body as? String else { return }
        Task { @MainActor [weak self] in self?.state?.take(text) }
    }
}

// MARK: - The frame

/// Alta's page with the quiz's frame round it: on a wide window the objectives down the left and the mastery down the
/// right, as the quiz sheet's rails are; on a narrow one both in a strip over the page. The page is always the same view
/// in the same place, so it is never loaded again as the frame comes and goes.
struct AltaFrame<Page: View>: View {
    @ObservedObject var alta: AltaState
    @ViewBuilder let page: () -> Page

    var body: some View {
        GeometryReader { g in
            let wide = g.size.width >= 1100
            let on = alta.active
            HStack(spacing: 0) {
                if on && wide {
                    AltaObjectivesRail(alta: alta)
                        .frame(width: 260)
                        .transition(.move(edge: .leading).combined(with: .opacity))
                    Divider()
                }
                VStack(spacing: 0) {
                    if on && !wide {
                        AltaStrip(alta: alta)
                            .transition(.move(edge: .top).combined(with: .opacity))
                        Divider()
                    }
                    page()
                }
                if on && wide {
                    Divider()
                    AltaMasteryRail(alta: alta)
                        .frame(width: 280)
                        .transition(.move(edge: .trailing).combined(with: .opacity))
                }
            }
            .animation(Motion.gentle, value: on)
            .animation(Motion.gentle, value: wide)
        }
    }
}

/// The objectives as the quiz's question card: every objective's number in a tile, the one being worked on filled,
/// a mastered one washed with a tick, the rest filling from the bottom as they are mastered; then each by name with its
/// own bar.
private struct AltaObjectivesRail: View {
    @ObservedObject var alta: AltaState

    var body: some View {
        let list = alta.objectives
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                AltaObjectiveCard(list: list, columns: 5)
                VStack(alignment: .leading, spacing: 12) {
                    ForEach(list) { o in AltaObjectiveRow(o: o) }
                }
                legend
            }
            .padding(.horizontal, 20)
            .padding(.top, 14)
            .padding(.bottom, 24)
        }
        .scrollIndicators(.never)
        .background(Theme.page)
    }

    private var legend: some View {
        VStack(alignment: .leading, spacing: 8) {
            key(Theme.accent, "Working on")
            key(Theme.accent.opacity(0.2), "Mastered")
            key(Color.primary.opacity(0.07), "Still to master")
        }
        .font(.sFootnote)
        .foregroundStyle(.secondary)
        .padding(.top, 4)
        .accessibilityElement(children: .combine)
    }

    private func key(_ fill: Color, _ text: String) -> some View {
        HStack(spacing: 10) {
            RoundedRectangle(cornerRadius: 3.5, style: .continuous).fill(fill).frame(width: 13, height: 13)
            Text(text)
        }
    }
}

/// The tiles in a small card, with how many are mastered at its head (the quiz's QuizNumberCard, for objectives).
private struct AltaObjectiveCard: View {
    let list: [AltaObjective]
    let columns: Int

    var body: some View {
        let mastered = list.filter(\.mastered).count
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                Text("Objectives").font(.sHeadline)
                Spacer(minLength: 6)
                Text("\(mastered) of \(list.count) mastered")
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText(value: Double(mastered)))
                    .animation(Motion.snappy, value: mastered)
            }
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: columns), spacing: 6) {
                ForEach(list) { o in AltaTile(o: o) }
            }
        }
        .padding(14)
        .card(radius: 16)
    }
}

/// One objective's tile: its number in a small rounded square, the one being worked on filled with the colour, one
/// mastered washed in it with a tick, one under way filled from the bottom as far as it is mastered. Not a button:
/// Alta chooses what comes next.
private struct AltaTile: View {
    let o: AltaObjective
    var width: CGFloat? = nil

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        let tint = Theme.accent
        ZStack {
            shape.fill(o.current ? tint : (o.mastered ? tint.opacity(0.18) : Color.primary.opacity(0.07)))
            if !o.current && !o.mastered && o.mastery > 0 {
                GeometryReader { g in
                    VStack(spacing: 0) {
                        Spacer(minLength: 0)
                        Rectangle().fill(tint.opacity(0.16)).frame(height: g.size.height * o.mastery)
                    }
                }
                .clipShape(shape)
            }
            if o.mastered && !o.current {
                Image(systemName: "checkmark")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(tint)
            } else {
                Text("\(o.number)")
                    .font(.sCallout.weight(.semibold).monospacedDigit())
                    .foregroundStyle(o.current ? Color.white : Color.secondary)
            }
        }
        .frame(width: width, height: 32)
        .frame(maxWidth: width == nil ? .infinity : nil)
        .animation(Motion.fill, value: o.mastery)
        .animation(Motion.snappy, value: o.current)
        .help("\(o.name) — \(Int((o.mastery * 100).rounded()))% mastered\(o.current ? ", working on it now" : "")")
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Objective \(o.number), \(o.name), \(Int((o.mastery * 100).rounded())) percent mastered\(o.current ? ", working on it now" : "")")
    }
}

/// An objective by name, with its own mastery bar.
private struct AltaObjectiveRow: View {
    let o: AltaObjective

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text("\(o.number)")
                    .font(.sFootnote.weight(.bold).monospacedDigit())
                    .foregroundStyle(o.current ? Theme.accent : .secondary)
                    .frame(width: 16, alignment: .leading)
                Text(o.name)
                    .font(.sCallout.weight(o.current ? .semibold : .regular))
                    .foregroundStyle(o.current ? .primary : .secondary)
                    .lineLimit(2)
                Spacer(minLength: 4)
                Text("\(Int((o.mastery * 100).rounded()))%")
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
            ProgressView(value: o.mastery)
                .progressViewStyle(.linear)
                .tint(Theme.accent)
                .padding(.leading, 24)
                .animation(Motion.fill, value: o.mastery)
                .accessibilityHidden(true)
        }
        .accessibilityElement(children: .combine)
    }
}

/// The mastery down the right, where the quiz has its progress: how much is mastered, toward what, what is being worked
/// on and about how many more questions it takes, how the last answers went, and when it is due.
private struct AltaMasteryRail: View {
    @ObservedObject var alta: AltaState

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                AltaMasteryCard(alta: alta)
                if let r = alta.report {
                    if let name = r.current.name, !name.isEmpty {
                        working(name, r)
                    }
                    if !r.history.isEmpty { AltaHistoryStrip(steps: r.history) }
                    facts(r)
                }
            }
            .padding(.horizontal, 20)
            .padding(.top, 14)
            .padding(.bottom, 24)
        }
        .scrollIndicators(.never)
        .background(Theme.page)
    }

    private func working(_ name: String, _ r: AltaReport) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Working on")
                .font(.sFootnote.weight(.semibold))
                .foregroundStyle(.secondary)
            Text(name)
                .font(.sCallout.weight(.semibold))
            if let low = r.current.low, let high = r.current.high, high > 0 {
                Text(low == high ? "About \(Int(low)) questions" : "About \(Int(low))–\(Int(high)) questions")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .card(radius: 16)
    }

    @ViewBuilder
    private func facts(_ r: AltaReport) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            if let due = r.due {
                let d = Date(timeIntervalSince1970: due / 1000)
                Label(d < Date() ? "Was due \(d.formatted(date: .abbreviated, time: .shortened))" : "Due \(d.formatted(date: .abbreviated, time: .shortened))", systemImage: "calendar")
                if d < Date() && !r.completed {
                    Label(r.lateAllowed ? "Late work is accepted" : "Past due", systemImage: r.lateAllowed ? "clock.badge.checkmark" : "clock.badge.exclamationmark")
                }
            }
            if (r.current.source ?? "").uppercased() == "PRACTICE" {
                Label("Practice — your score is kept", systemImage: "arrow.counterclockwise")
            }
            if r.stuck {
                Label("Alta may show a lesson next", systemImage: "lightbulb")
            }
        }
        .font(.sFootnote)
        .foregroundStyle(.secondary)
        .labelStyle(.titleAndIcon)
    }
}

/// The mastery, where the quiz shows how much is answered: its percent large, a bar toward the threshold, and the
/// objectives mastered.
private struct AltaMasteryCard: View {
    @ObservedObject var alta: AltaState

    var body: some View {
        let m = alta.mastery
        let list = alta.objectives
        let done = list.filter(\.mastered).count
        let complete = alta.report?.completed == true || m >= 0.999
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Text("Mastery").font(.sHeadline)
                Spacer(minLength: 6)
                if complete {
                    Label("Complete", systemImage: "checkmark.seal.fill")
                        .font(.sFootnote.weight(.semibold))
                        .foregroundStyle(Theme.accent)
                        .labelStyle(.titleAndIcon)
                }
            }
            Text("\(Int((m * 100).rounded()))%")
                .font(.system(size: 34, weight: .bold, design: .rounded).monospacedDigit())
                .contentTransition(.numericText(value: m))
                .animation(Motion.snappy, value: m)
            AltaMasteryBar(value: m)
            Text(list.isEmpty ? "Mastery toward this assignment" : "\(done) of \(list.count) objectives mastered")
                .font(.sFootnote.monospacedDigit())
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .card(radius: 16)
        .accessibilityElement(children: .combine)
    }
}

/// The mastery bar: the quiz's progress bar, filled to how much is mastered.
private struct AltaMasteryBar: View {
    let value: Double
    var height: CGFloat = 8

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.primary.opacity(0.08))
                Capsule()
                    .fill(Theme.accent)
                    .frame(width: max(value > 0 ? height : 0, g.size.width * min(max(value, 0), 1)))
            }
        }
        .frame(height: height)
        .animation(Motion.fill, value: value)
        .accessibilityLabel("Mastery")
        .accessibilityValue("\(Int((value * 100).rounded())) percent")
    }
}

/// The last answers, newest first: a dot for each — right, wrong, skipped, or a lesson.
private struct AltaHistoryStrip: View {
    let steps: [AltaReport.Step]

    var body: some View {
        let shown = Array(steps.prefix(20))
        let right = shown.filter { $0.right > 0 && $0.wrong == 0 }.count
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                Text("Recent answers").font(.sFootnote.weight(.semibold)).foregroundStyle(.secondary)
                Spacer(minLength: 6)
                Text("\(right) of \(shown.count) right first time")
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
            LazyVGrid(columns: Array(repeating: GridItem(.fixed(10), spacing: 5), count: 10), alignment: .leading, spacing: 5) {
                ForEach(Array(shown.reversed().enumerated()), id: \.offset) { _, s in
                    Circle()
                        .fill(color(s))
                        .frame(width: 10, height: 10)
                }
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Recent answers: \(right) of \(shown.count) right first time")
    }

    private func color(_ s: AltaReport.Step) -> Color {
        if s.right > 0 && s.wrong == 0 { return .green }
        if s.wrong > 0 { return .orange }
        if s.lessons > 0 { return Theme.accent.opacity(0.5) }
        return Color.primary.opacity(0.15)
    }
}

/// On a narrow window: the tiles and the mastery bar in one strip over the page, as the quiz's strip of numbers.
private struct AltaStrip: View {
    @ObservedObject var alta: AltaState

    var body: some View {
        let list = alta.objectives
        let m = alta.mastery
        HStack(spacing: 14) {
            HStack(spacing: 6) {
                ForEach(list) { o in AltaTile(o: o, width: 32) }
            }
            VStack(alignment: .leading, spacing: 6) {
                HStack(alignment: .firstTextBaseline) {
                    Text("Mastery").font(.sFootnote.weight(.semibold))
                    Spacer(minLength: 6)
                    Text("\(Int((m * 100).rounded()))% · \(list.filter(\.mastered).count) of \(list.count) objectives")
                        .font(.sFootnote.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
                AltaMasteryBar(value: m, height: 6)
            }
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 10)
        .background(Theme.page)
    }
}

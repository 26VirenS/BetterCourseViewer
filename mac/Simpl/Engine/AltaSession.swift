import AppKit
import SwiftUI
import WebKit

// (1.3.17) A Knewton Alta assignment taken in the app, as a quiz is: Open Tool on an Alta assignment opens a popup
// (AltaQuiz.swift) with the objectives and the total mastery down the left and the question in the rest, drawn by the
// app. Alta itself still sets each question and marks each answer: it is open, signed in through Canvas, on a page the
// popup keeps out of sight; the student's answer is put into Alta's own question there and Alta's own Check pressed,
// and what Alta says back is shown. A question the popup cannot draw itself (a graph, a formula, a lesson) shows Alta's
// own page in its place.

/// The page's side: a script in every frame of the hidden page that, on Alta's pages only, reads a copy of Alta's own
/// `content` answer as Alta's page receives it, and passes the app the assignment, its objectives and their mastery,
/// and the question on screen: what it asks and how it is answered. Never its answer key (`correct_answer`,
/// `success_condition`, `validation`, `valid_response`), never who the student is (`userId`, `registrationId`,
/// `ltiEnrollment`): those are not read. It also answers the app: an answer put into Alta's question as a click or
/// typing would, Alta's Check and Continue pressed, and what Alta then shows read back.
enum AltaHook {
    static let handler = "simplAlta"

    static var script: WKUserScript {
        WKUserScript(source: source, injectionTime: .atDocumentStart, forMainFrameOnly: false)
    }

    // ALTA-HOOK-BEGIN (scripts/dev/alta-test.mjs reads the script from here)
    static let source = #"""
    (function () {
      // (1.3.19: in every page of the popup's own hidden view, which holds nothing but Alta's launch — Alta's own
      // site names are not guessed at)
      if (window.__simplAltaOn || !/^https?:$/.test(location.protocol)) return;
      window.__simplAltaOn = true;
      var post = function (m) { try { window.webkit.messageHandlers.simplAlta.postMessage(JSON.stringify(m)); } catch (e) {} };
      var num = function (v) { if (v === null || v === undefined || v === '') return null; var n = Number(v); return isFinite(n) ? n : null; };
      var str = function (v) { return typeof v === 'string' && v ? v.slice(0, 600) : (typeof v === 'number' ? String(v) : null); };
      var html = function (v) { return typeof v === 'string' && v ? v.slice(0, 40000) : null; };

      // What the question asks and how it is answered. Its answer key is never read.
      var question = function (atom, ci) {
        var d = atom.data || {}, c = d.content || {};
        var parts = [d.question, c.stimulus].filter(function (x) { return typeof x === 'string' && x; });
        if (parts.length === 2 && parts[0] === parts[1]) parts.pop();
        var q = {
          key: [str(c.response_id), str(ci.id), num(ci.sequenceOrdinal)].join(':'),
          responseId: str(c.response_id), type: str(c.type), custom: c.type === 'custom' ? str(c.custom_type) : null,
          purpose: str(atom.purpose), instance: str(ci.type), dataType: str(atom.dataType),
          prompt: html(parts.join('')), multiple: !!c.multiple_responses, options: [], template: null, choices: [],
          media: Array.isArray(atom.media) ? atom.media.length : 0
        };
        if (Array.isArray(c.options)) q.options = c.options.map(function (o) { return { label: html(o && o.label) || '', value: str(o && o.value) }; });
        if (typeof c.template === 'string') q.template = html(c.template);
        if (Array.isArray(c.possible_responses) && c.possible_responses.every(Array.isArray)) {
          q.choices = c.possible_responses.map(function (r) { return r.map(function (x) { return str(typeof x === 'string' ? x : (x && (x.label || x.value))) || ''; }); });
        }
        return q;
      };
      var pick = function (j) {
        var e = j.enrollment || {}, p = e.path || {}, d = e.dueDate || {}, a = j.analytics || {}, sp = a.statusAndProgress || {};
        var st = (j.states || [])[0] || {}, atom = st.atom || {}, ci = st.compoundInstance || {}, lo = atom.learningObjective || {};
        return {
          kind: 'content', path: location.pathname,
          name: str(p.name), pathType: str(p.type), threshold: num(p.masteryThreshold),
          objectives: (Array.isArray(p.pathLearningObjectives) ? p.pathLearningObjectives : []).map(function (o) {
            return { id: str(o.learningObjectiveId || o.loId || o.id), name: str(o.description || o.name || o.title) };
          }),
          targets: (Array.isArray(sp.targets) ? sp.targets : []).map(function (t) {
            return { id: str(String(t.target_id || t.targetId || t.id || '').replace(/^lref-/, '')), progress: num(t.progress), status: str(t.status) };
          }),
          percent: num(a.percentComplete), progress: num(sp.progress), status: str(sp.status),
          completed: !!e.completed, started: !!e.startedAt, due: num(d.effectiveDueDate), lateAllowed: !!d.lateSubmissionEnabled, ended: !!p.ended,
          current: {
            id: str(atom.learningObjectiveId), name: str(lo.description || atom.name),
            low: num(lo.estimatedQuestionsLow), high: num(lo.estimatedQuestionsHigh), source: str(ci.source)
          },
          question: (j.states || []).length ? question(atom, ci) : null,
          history: (j.history && Array.isArray(j.history.sequences) ? j.history.sequences : []).slice(0, 40).map(function (s) {
            return { right: num(s.numCorrectResponses) || 0, wrong: num(s.numIncorrectResponses) || 0, skipped: num(s.numSkippedAssessments) || 0, lessons: num(s.numInstructional) || 0 };
          }),
          stuck: j.stuckLo !== null && j.stuckLo !== undefined
        };
      };
      var seen = function (url, text) {
        if (!/content/i.test(String(url || ''))) return;
        var j; try { j = typeof text === 'string' ? JSON.parse(text) : text; } catch (e) { return; }
        if (j && Array.isArray(j.states) && j.enrollment) post(pick(j));
      };
      // (the assignment's overview, before it is started or between questions: whatever Alta's page reads that names
      // the assignment, its objectives with their estimates and the mastery — found by those fields, wherever they sit;
      // the answer key and the student's identity are passed over, never read)
      var SKIP = { correct_answer: 1, success_condition: 1, validation: 1, valid_response: 1, ltiEnrollment: 1, userId: 1, registrationId: 1 };
      var scan = function (j) {
        var out = { kind: 'overview', path: location.pathname, name: null, due: null, threshold: null, percent: null, status: null, started: null, completed: null, objectives: [] };
        var had = {};
        var visit = function (o, depth) {
          if (!o || typeof o !== 'object' || depth > 7) return;
          if (Array.isArray(o)) { for (var i = 0; i < o.length && i < 500; i++) visit(o[i], depth + 1); return; }
          if (typeof o.estimatedQuestionsLow === 'number' || typeof o.estimatedQuestionsHigh === 'number') {
            var id = str(o.learningObjectiveId || o.loId || o.id), name = str(o.description || o.name || o.title);
            if (name && !had[id || name]) { had[id || name] = 1; out.objectives.push({ id: id || name, name: name, low: num(o.estimatedQuestionsLow), high: num(o.estimatedQuestionsHigh) }); }
          }
          if (o.path && typeof o.path === 'object' && typeof o.path.name === 'string') { out.name = out.name || str(o.path.name); out.threshold = out.threshold || num(o.path.masteryThreshold); }
          if (o.dueDate && typeof o.dueDate === 'object' && o.dueDate.effectiveDueDate) out.due = out.due || num(o.dueDate.effectiveDueDate);
          if (typeof o.percentComplete === 'number' && out.percent === null) out.percent = o.percentComplete;
          if (o.statusAndProgress && typeof o.statusAndProgress === 'object' && !out.status) out.status = str(o.statusAndProgress.status);
          if (typeof o.completed === 'boolean' && 'startedAt' in o && out.completed === null) { out.completed = o.completed; out.started = !!o.startedAt; }
          for (var k in o) { if (!SKIP[k] && Object.prototype.hasOwnProperty.call(o, k)) visit(o[k], depth + 1); }
        };
        visit(j, 0);
        return out;
      };
      var jsonSeen = [];
      var looked = function (url, text) {
        if (typeof text === 'string' && text.length > 3000000) return;
        var j; try { j = typeof text === 'string' ? JSON.parse(text) : text; } catch (e) { return; }
        if (!j || typeof j !== 'object') return;
        // (for Copy Alta Details: where each answer came from and the names of its fields — never their values)
        try { jsonSeen.push({ at: new URL(String(url || ''), location.href).pathname.slice(0, 120), keys: Object.keys(j).slice(0, 16) }); if (jsonSeen.length > 40) jsonSeen.shift(); } catch (e) {}
        if (/content/i.test(String(url || ''))) return seen(url, j);
        var o = scan(j);
        if (o.objectives.length || o.name) post(o);
      };
      var fetch0 = window.fetch;
      if (fetch0) {
        window.fetch = function (input) {
          var asked = typeof input === 'string' ? input : (input && input.url) || '';
          var p = fetch0.apply(this, arguments);
          p.then(function (r) {
            try {
              var at = (r && r.url) || asked;
              if (r && /json/i.test(r.headers.get('content-type') || '')) r.clone().text().then(function (t) { looked(at, t); }, function () {});
            } catch (e) {}
          }, function () {});
          return p;
        };
      }
      var open0 = XMLHttpRequest.prototype.open, send0 = XMLHttpRequest.prototype.send;
      XMLHttpRequest.prototype.open = function (m, u) { this.__simplUrl = u; return open0.apply(this, arguments); };
      XMLHttpRequest.prototype.send = function () {
        var x = this;
        x.addEventListener('load', function () {
          try {
            if (!/json/i.test(x.getResponseHeader('content-type') || '')) return;
            looked(x.responseURL || x.__simplUrl, x.responseType === 'json' ? x.response : (x.responseType === '' || x.responseType === 'text' ? x.responseText : null));
          } catch (e) {}
        });
        return send0.apply(this, arguments);
      };

      // ---- the app's hands in the page: the student's answer put in, Check and Continue pressed, the verdict read ----
      var norm = function (s) { return String(s || '').replace(/<[^>]+>/g, ' ').replace(/&nbsp;| /g, ' ').replace(/\s+/g, ' ').trim().toLowerCase(); };
      var shown = function (el) { var r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
      var scope = function (rid) {
        if (rid) {
          var e = null;
          try { e = document.querySelector('.question-' + CSS.escape(rid)) || document.getElementById(rid) || document.querySelector('[data-response-id="' + CSS.escape(rid) + '"]'); } catch (x) {}
          if (e) return e;
        }
        return document.querySelector('.learnosity-response, .lrn_widget, .lrn-question') || document.body;
      };
      var put = function (el, v) {
        var proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
        var d = Object.getOwnPropertyDescriptor(proto, 'value');
        el.focus();
        if (d && d.set) d.set.call(el, v); else el.value = v;
        ['input', 'change', 'keyup'].forEach(function (k) { el.dispatchEvent(new Event(k, { bubbles: true })); });
        el.blur();
      };
      // every element matching, in the page and inside any web component's own (open) tree
      var deepIn = function (start, sel) {
        var out = [];
        var walk = function (root) {
          try {
            out.push.apply(out, [].slice.call(root.querySelectorAll(sel)));
            [].slice.call(root.querySelectorAll('*')).forEach(function (el) { if (el.shadowRoot) walk(el.shadowRoot); });
          } catch (e) {}
        };
        walk(start);
        return out;
      };
      var deep = function (sel) {
        var out = [];
        var walk = function (root) {
          try {
            out.push.apply(out, [].slice.call(root.querySelectorAll(sel)));
            [].slice.call(root.querySelectorAll('*')).forEach(function (el) { if (el.shadowRoot) walk(el.shadowRoot); });
          } catch (e) {}
        };
        walk(document);
        return out;
      };
      var CLICKABLE = 'button, a, [role="button"], input[type="button"], input[type="submit"], [tabindex]';
      var buttons = function (re) {
        return deep(CLICKABLE).filter(function (b) {
          var t = norm(b.innerText || b.textContent || b.value || b.getAttribute('aria-label'));
          return shown(b) && !b.disabled && b.getAttribute('aria-disabled') !== 'true' && re.test(t);
        });
      };
      // (the overview as it reads on the page, when no answer of Alta's named it: its title, DUE DATE, STATUS, and each
      // "Estimated 4 - 9 questions" with the objective under it)
      var scrape = function () {
        var lines = String((document.body && document.body.innerText) || '').split('\n').map(function (l) { return l.trim(); }).filter(Boolean);
        var o = { kind: 'overview', path: location.pathname, name: null, dueText: null, statusText: null, objectives: [] };
        var h1 = deep('h1')[0];
        if (h1) o.name = str((h1.innerText || '').trim());
        lines.forEach(function (l, i) {
          if (/^due( date)?$/i.test(l)) o.dueText = str(lines.slice(i + 1, i + 3).filter(function (x) { return !/^status$/i.test(x); }).join(' '));
          if (/^status$/i.test(l)) o.statusText = str(lines[i + 1]);
          var m = /^estimated\s+(\d+)\s*[-\u2013]\s*(\d+)\s+questions?$/i.exec(l);
          var name = lines[i + 1];
          if (m && name && !/^estimated/i.test(name)) o.objectives.push({ id: name, name: str(name), low: +m[1], high: +m[2], topic: str(lines[i - 1]) });
        });
        return o;
      };
      var CHECK = /^(check|check answer|check my answer|submit|submit answer)$/;
      var NEXT = /^(next|next question|continue|keep going|next item|try another|try another question|keep practicing|practice more)$/;
      var START = /^(start|start assignment|continue|continue assignment|resume|keep going|practice|keep practicing|start practicing)$/;
      var answer = function (a) {
        var root = scope(a.responseId), done = 0;
        if (a.type === 'mcq') {
          var inputs = [].slice.call(root.querySelectorAll('input[type=radio], input[type=checkbox]'));
          var textOf = function (inp) {
            var lab = inp.id ? document.querySelector('label[for="' + CSS.escape(inp.id) + '"]') : null;
            var row = inp.closest('li, .lrn-mcq-option, label');
            return norm((lab && lab.innerText) || (row && row.innerText) || '');
          };
          var want = (a.labels || []).map(norm).filter(Boolean);
          var hit = inputs.map(function (inp) {
            var t = textOf(inp);
            return want.some(function (w) { return t === w || (w.length > 3 && t.indexOf(w) >= 0); }) || (a.values || []).indexOf(inp.value) >= 0;
          });
          if (!hit.some(Boolean)) hit = inputs.map(function (inp, k) { return (a.picks || []).indexOf(k) >= 0; });
          inputs.forEach(function (inp, k) { if (hit[k] !== inp.checked) { inp.click(); } if (hit[k]) done++; });
        }
        // (1.3.20) a maths answer (Learnosity's formula fields, MathQuill underneath): typed into each field as the keys
        // would — "/" a fraction, "^" a power — through MathQuill when the page offers it, else its own text box
        if (/formula/i.test(a.type || '') && Array.isArray(a.blanks) && a.blanks.length) {
          var mqs = deepIn(root, '.mq-editable-field');
          var MQ = null;
          try { MQ = window.MathQuill && window.MathQuill.getInterface ? window.MathQuill.getInterface(2) : null; } catch (e) {}
          a.blanks.forEach(function (v, k) {
            var f = mqs[k]; if (!f) return;
            var api = null;
            try { api = MQ && MQ.MathField ? MQ.MathField(f) : null; } catch (e) {}
            if (api && api.latex) { try { api.latex(''); api.typedText(String(v)); api.blur && api.blur(); done++; return; } catch (e) {} }
            var ta = f.querySelector('textarea'); if (!ta) return;
            ta.focus();
            var ok = false;
            try { ok = document.execCommand('insertText', false, String(v)); } catch (e) {}
            if (!ok) put(ta, String(v));
            ta.blur();
            done++;
          });
          return { ok: done > 0, filled: done };
        }
        if (Array.isArray(a.blanks) && a.blanks.length) {
          var fields = [].slice.call(root.querySelectorAll('input[type=text], input:not([type]), textarea'));
          a.blanks.forEach(function (v, k) { if (fields[k]) { put(fields[k], v); done++; } });
        }
        if (Array.isArray(a.drops) && a.drops.length) {
          var sels = [].slice.call(root.querySelectorAll('select'));
          a.drops.forEach(function (v, k) {
            var s = sels[k]; if (!s) return;
            var o = [].slice.call(s.options).find(function (x) { return norm(x.text) === norm(v) || x.value === v; });
            if (o) { s.value = o.value; s.dispatchEvent(new Event('change', { bubbles: true })); done++; }
          });
        }
        return { ok: done > 0, filled: done };
      };
      var verdictNow = function () {
        var any = function (sel) { return [].slice.call(document.querySelectorAll(sel)).some(shown); };
        var bad = any('.lrn_incorrect, .lrn-incorrect, [class*="incorrect" i]');
        var good = any('.lrn_correct, .lrn-correct');
        var boxes = [].slice.call(document.querySelectorAll('[class*="feedback" i], [role="alert"], [aria-live]')).filter(shown);
        var text = boxes.map(function (b) { return (b.innerText || '').trim(); }).filter(Boolean).join('\n').slice(0, 1200);
        var v = null;
        if (bad || /\b(incorrect|not quite|try again|not correct)\b/i.test(text)) v = 'incorrect';
        else if (good || /\b(correct|well done|great job|nice work)\b/i.test(text)) v = 'correct';
        return { verdict: v, text: text, next: buttons(NEXT).length > 0, check: buttons(CHECK).length > 0 };
      };
      var watching = null;
      var watch = function () {
        if (watching) clearInterval(watching);
        var t0 = Date.now();
        watching = setInterval(function () {
          var s = verdictNow();
          if (s.verdict || s.next || Date.now() - t0 > 12000) {
            clearInterval(watching); watching = null;
            post({ kind: 'feedback', verdict: s.verdict, text: s.text, next: s.next, timedOut: !s.verdict && !s.next });
          }
        }, 300);
      };
      window.__simplAlta = {
        // the assignment's overview: whether it offers to start (or go on), and pressing that
        peek: function () { var b = buttons(START)[0]; return { start: !!b, word: b ? (b.innerText || b.textContent || b.value || '').trim() : null, overview: scrape() }; },
        diag: function () {
          return {
            at: location.host + location.pathname, top: window.top === window, json: jsonSeen.slice(),
            buttons: deep(CLICKABLE).filter(shown).slice(0, 50).map(function (b) { return b.tagName.toLowerCase() + ': ' + norm(b.innerText || b.textContent || b.value || b.getAttribute('aria-label')).slice(0, 40); }),
            estimates: scrape().objectives.length, start: buttons(START).length, check: buttons(CHECK).length, next: buttons(NEXT).length
          };
        },
        begin: function () { var b = buttons(START)[0]; if (!b) return { ok: false }; b.click(); return { ok: true }; },
        answer: answer,
        check: function () { var b = buttons(CHECK)[0]; if (!b) return { ok: false }; b.click(); watch(); return { ok: true }; },
        next: function () { var b = buttons(NEXT)[0]; if (!b) return { ok: false }; b.click(); return { ok: true }; },
        state: verdictNow
      };

      // (1.3.20) Alta's welcome pop-ups (Welcome to your adaptive assignment!, Assignments adapt to you…): put away as they
      // come, by their own Got it — only a Got it inside a dialog, never anything of the assignment's
      var GOTIT = /^(got it|ok, got it|okay, got it)$/;
      var dismiss = function () {
        deep('[role="dialog"], [role="alertdialog"], [aria-modal="true"], dialog[open], .modal, [class*="modal" i]').filter(shown).forEach(function (d) {
          deepIn(d, CLICKABLE).filter(function (b) { return shown(b) && GOTIT.test(norm(b.innerText || b.textContent || b.value || b.getAttribute('aria-label'))); }).slice(0, 1).forEach(function (b) { b.click(); });
        });
      };
      setInterval(dismiss, 700);

      // where the page is: an assignment's player, or another of Alta's pages
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

// MARK: - What the page passes

struct AltaReport: Decodable, Equatable {
    struct Target: Decodable, Equatable { var id: String?; var progress: Double?; var status: String? }
    struct Objective: Decodable, Equatable { var id: String?; var name: String? }
    struct Current: Decodable, Equatable {
        var id: String?
        var name: String?
        var low: Double?
        var high: Double?
        var source: String?
    }
    struct Step: Decodable, Equatable { var right: Int; var wrong: Int; var skipped: Int; var lessons: Int }

    var name: String?
    var threshold: Double?
    var objectives: [Objective]
    var targets: [Target]
    var percent: Double?
    var progress: Double?
    var status: String?
    var completed: Bool
    var due: Double?
    var lateAllowed: Bool
    var ended: Bool
    var current: Current
    var question: AltaQuestion?
    var history: [Step]
    var stuck: Bool
}

/// The question on screen, as Learnosity describes it to Alta's page (its type, what it asks, its options, its blanks)
/// — never its answer key.
struct AltaQuestion: Decodable, Equatable {
    struct Option: Decodable, Equatable { var label: String; var value: String? }

    var key: String
    var responseId: String?
    var type: String?
    var custom: String?
    var purpose: String?
    var instance: String?
    var dataType: String?
    var prompt: String?
    var multiple: Bool
    var options: [Option]
    var template: String?
    var choices: [[String]]
    var media: Int

    /// How the popup answers it: the kinds it draws itself; anything else is Alta's own page in its place.
    enum Kind: Equatable { case choice, blanks, dropdowns, text, longText, page }

    var kind: Kind {
        if dataType != nil && dataType != "LEARNOSITY_GENERIC_QUESTION" { return .page } // (a lesson, a video: Alta's to show)
        if let p = purpose, p != "ASSESSES" { return .page }
        switch type ?? "" {
        case "mcq": return options.isEmpty ? .page : .choice
        case "clozetext": return blankCount > 0 ? .blanks : .page
        // (1.3.20) maths typed into blanks (k′(1) = ▢), or into one field: the student types it as on a keyboard
        case "clozeformula", "clozeformulaV2": return blankCount > 0 ? .blanks : .text
        case "formula", "formulaV2": return .text
        case "clozedropdown": return !choices.isEmpty && blankCount == choices.count ? .dropdowns : .page
        case "shorttext": return .text
        case "plaintext", "longtext", "longtextV2": return .longText
        default: return .page // (a graph, a formula, a drag and drop, a custom widget: Alta's own)
        }
    }

    /// (1.3.20) An answer in maths (Learnosity's formula fields): typed as on a keyboard, put in through MathQuill.
    var isMath: Bool { (type ?? "").lowercased().contains("formula") }

    /// The blanks in a cloze template ({{response}} each).
    var blankCount: Int { (template ?? "").components(separatedBy: "{{response}}").count - 1 }
}

/// (1.3.17) The assignment's overview, as Alta's page reads it before it is started (or between questions): its name,
/// when it is due, its objectives with Alta's estimate of the questions each takes, and how far it is mastered.
struct AltaOverview: Decodable, Equatable {
    struct Objective: Decodable, Equatable { var id: String; var name: String; var low: Double?; var high: Double? }
    var name: String?
    var due: Double?
    /// (1.3.19) As the page words them, when no answer of Alta's gave them ("Monday, Oct 12 11:59pm PDT", "Not started").
    var dueText: String?
    var statusText: String?
    var threshold: Double?
    var percent: Double?
    var status: String?
    var started: Bool?
    var completed: Bool?
    var objectives: [Objective]
}

/// What Alta said of an answer, as its page shows it.
struct AltaVerdict: Equatable {
    var correct: Bool?
    var text: String
    var canContinue: Bool
}

/// One objective as the popup shows it.
struct AltaObjective: Identifiable, Equatable {
    let id: String
    let number: Int
    let name: String
    let mastery: Double
    let current: Bool
    var mastered: Bool { mastery >= 0.999 }
}

/// What opens the popup: an Alta assignment (its course and id: the app asks Canvas for its launch), or a page of
/// Alta's own (the screenshot suite's mock).
struct AltaLaunch: Identifiable, Equatable {
    let id = UUID()
    let course: String
    let assignment: String
    let title: String
    var page: String? = nil
}

// MARK: - The session

/// One assignment taken in the popup: Alta's page out of sight, what it last said, and the answer being given.
@MainActor
final class AltaSession: ObservableObject {
    enum Phase: Equatable { case loading, start, answering, checking, feedback, moving, failed(String) }

    @Published private(set) var report: AltaReport?
    /// (1.3.17) The overview Alta opens on: the assignment and its objectives, before a question is asked.
    @Published private(set) var overview: AltaOverview?
    /// The word on Alta's own start button (Start, Continue…), for the popup's.
    @Published private(set) var startWord = "Start"
    @Published private(set) var phase: Phase = .loading
    @Published private(set) var verdict: AltaVerdict?
    /// Alta's own page shown in the question's place: a kind of question the popup does not draw, a lesson, or a step
    /// the popup could not take — and whenever the student asks to see it.
    @Published var showPage = false
    @Published var picks: Set<Int> = []
    @Published var blanks: [String] = []
    @Published var drops: [String] = []

    let launch: AltaLaunch
    let web: WKWebView
    private var frame: WKFrameInfo?
    /// Every page the hidden view has loaded (Canvas's launch, Alta's pages, any frame in them): asked for Start, and
    /// for Copy Alta Details.
    private var frames: [WKFrameInfo] = []
    private var questionKey: String?
    private var names: [String: String] = UserDefaults.standard.dictionary(forKey: AltaSession.namesKey) as? [String: String] ?? [:]
    private static let namesKey = "SimplAltaObjectiveNames"
    private var watchdog: Task<Void, Never>?
    private let relay: AltaRelay

    init(launch: AltaLaunch) {
        self.launch = launch
        CrossSiteCookies.apply()
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default() // (the Canvas session: Alta's launch finds the student signed in)
        config.mediaTypesRequiringUserActionForPlayback = []
        config.applicationNameForUserAgent = "Version/18.4 Safari/605.1.15"
        config.userContentController.addUserScript(AltaHook.script)
        let r = AltaRelay()
        config.userContentController.add(r, name: AltaHook.handler)
        relay = r
        web = WKWebView(frame: NSRect(x: 0, y: 0, width: 1000, height: 800), configuration: config)
        web.isInspectable = true
        r.session = self
    }

    /// Alta opened: the assignment's launch through Canvas (or the page asked for), and a watch on it — a page that says
    /// nothing in a while (a start screen, a sign-in) is shown as it is.
    func start(engine: Engine) async {
        guard web.url == nil else { return }
        phase = .loading
        if let p = launch.page, let u = engine.absolute(p) {
            web.load(URLRequest(url: u))
        } else {
            struct Answer: Decodable { var url: String }
            do {
                let a = try await engine.call("toolLaunch", ["course": launch.course, "assignment": launch.assignment], as: Answer.self)
                guard let u = URL(string: a.url) else { throw EngineError.unreadable }
                web.load(URLRequest(url: u))
            } catch {
                phase = .failed("Knewton Alta could not be opened. \(error.localizedDescription)")
                return
            }
        }
        watch(seconds: 20)
    }

    /// Nothing heard from Alta in this long: its own page is shown, so the student can go on there.
    private func watch(seconds: Double) {
        watchdog?.cancel()
        watchdog = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
            guard !Task.isCancelled, let self else { return }
            if self.phase == .loading || self.phase == .moving || self.phase == .checking {
                withAnimation(Motion.gentle) {
                    self.showPage = true
                    if self.phase == .checking { self.phase = .answering }
                }
            }
        }
    }

    // MARK: What the page says

    func take(_ text: String, frame: WKFrameInfo) {
        guard let data = text.data(using: .utf8),
              let head = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }
        switch head["kind"] as? String {
        case "page":
            if self.frame == nil { self.frame = frame }
            if !frames.contains(where: { $0 === frame }) { frames.append(frame); if frames.count > 12 { frames.removeFirst() } }
            if phase == .loading || phase == .start { probeStart(frame) }
        case "overview":
            guard let o = try? JSONDecoder().decode(AltaOverview.self, from: data) else { return }
            if self.frame == nil { self.frame = frame }
            for x in o.objectives where !x.name.isEmpty && names[x.id] != x.name { names[x.id] = x.name }
            UserDefaults.standard.set(names, forKey: Self.namesKey)
            withAnimation(Motion.gentle) { overview = merge(overview, o) }
            if phase == .loading { probeStart(frame) }
        case "content":
            guard let r = try? JSONDecoder().decode(AltaReport.self, from: data) else { return }
            self.frame = frame
            remember(r)
            let key = r.question?.key
            let isNew = key != questionKey
            withAnimation(Motion.gentle) {
                report = r
                if isNew {
                    questionKey = key
                    picks = []
                    let k = r.question?.kind
                    blanks = Array(repeating: "", count: k == .text || k == .longText ? 1 : (r.question?.blankCount ?? 0))
                    drops = Array(repeating: "", count: r.question?.choices.count ?? 0)
                    verdict = nil
                    phase = .answering
                    showPage = r.question.map { $0.kind == .page } ?? true
                }
            }
            watchdog?.cancel()
        case "feedback":
            guard phase == .checking else { return }
            watchdog?.cancel()
            let v = head["verdict"] as? String
            let timedOut = head["timedOut"] as? Bool ?? false
            withAnimation(Motion.gentle) {
                verdict = AltaVerdict(correct: v == "correct" ? true : (v == "incorrect" ? false : nil),
                                      text: (head["text"] as? String) ?? "", canContinue: head["next"] as? Bool ?? false)
                phase = .feedback
                if timedOut { showPage = true } // (Alta said nothing the popup could read: its page, as it is)
            }
        default:
            break
        }
    }

    /// A newer overview kept over the last, its blanks filled from the one before.
    private func merge(_ old: AltaOverview?, _ new: AltaOverview) -> AltaOverview {
        guard let old else { return new }
        return AltaOverview(name: new.name ?? old.name, due: new.due ?? old.due, dueText: new.dueText ?? old.dueText, statusText: new.statusText ?? old.statusText, threshold: new.threshold ?? old.threshold,
                            percent: new.percent ?? old.percent, status: new.status ?? old.status, started: new.started ?? old.started,
                            completed: new.completed ?? old.completed, objectives: new.objectives.isEmpty ? old.objectives : new.objectives)
    }

    /// The page is an assignment's overview with Alta's start (or continue) button on it: the popup's start screen,
    /// asked of the page a moment after it loads, and again in case it draws late.
    private func probeStart(_ frame: WKFrameInfo) {
        Task { [weak self] in
            for wait in [1.2, 3.0, 6.0] {
                try? await Task.sleep(nanoseconds: UInt64(wait * 1_000_000_000))
                guard let self, self.phase == .loading || self.phase == .start else { return }
                let r = await self.call("return window.__simplAlta ? window.__simplAlta.peek() : null", [:], in: frame) as? [String: Any]
                // (the overview as the page reads, when Alta's own answers did not name it)
                if let seen = r?["overview"] as? [String: Any], let data = try? JSONSerialization.data(withJSONObject: seen),
                   let o = try? JSONDecoder().decode(AltaOverview.self, from: data), !o.objectives.isEmpty || o.name != nil {
                    withAnimation(Motion.gentle) { self.overview = self.merge(self.overview, o) }
                }
                if r?["start"] as? Bool == true {
                    self.watchdog?.cancel()
                    let word = (r?["word"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines).capitalized
                    withAnimation(Motion.gentle) {
                        self.frame = frame
                        self.startWord = word.isEmpty ? "Start" : word
                        self.phase = .start
                        self.showPage = false
                    }
                    return
                }
            }
        }
    }

    /// (1.3.19) Copy Alta Details: what the page script saw in each page of the hidden view — its address (no query),
    /// the names of the fields in Alta's answers (never their values), the buttons' words, and which of Start, Check
    /// and Continue it found — for working out what a school's Alta does differently. Nothing of the student's.
    func diagnostics() async -> String {
        var out: [[String: Any]] = [["app": (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "?",
                                     "phase": "\(phase)", "showPage": showPage, "report": report != nil, "overviewObjectives": overview?.objectives.count ?? 0,
                                     "viewAt": (web.url.map { ($0.host ?? "") + $0.path }) ?? ""]]
        for f in frames + [nil] {
            if let d = await run("return window.__simplAlta ? window.__simplAlta.diag() : { script: 'not running', at: location.host + location.pathname }", [:], exactly: f) as? [String: Any] {
                out.append(d)
            }
        }
        let data = (try? JSONSerialization.data(withJSONObject: out, options: [.prettyPrinted, .sortedKeys])) ?? Data()
        return String(data: data, encoding: .utf8) ?? ""
    }

    /// Start (or Continue): Alta's own button pressed; its first question comes as Alta's page asks for it.
    func begin() {
        Task {
            let pressed = await call("return window.__simplAlta ? window.__simplAlta.begin() : null", [:])
            guard (pressed as? [String: Any])?["ok"] as? Bool == true else { return fail("Alta's Start button could not be found.") }
            withAnimation(Motion.gentle) { phase = .moving }
            watch(seconds: 25)
        }
    }

    private func remember(_ r: AltaReport) {
        guard let id = r.current.id, let name = r.current.name, !name.isEmpty, names[id] != name else { return }
        names[id] = name
        if names.count > 400 { names = Dictionary(uniqueKeysWithValues: names.suffix(300).map { ($0.key, $0.value) }) }
        UserDefaults.standard.set(names, forKey: Self.namesKey)
    }

    // MARK: The student's answer

    var question: AltaQuestion? { report?.question }

    var canCheck: Bool {
        guard phase == .answering, let q = question else { return false }
        switch q.kind {
        case .choice: return !picks.isEmpty
        case .blanks, .text, .longText: return !blanks.isEmpty && blanks.allSatisfy { !$0.trimmingCharacters(in: .whitespaces).isEmpty }
        case .dropdowns: return !drops.isEmpty && drops.allSatisfy { !$0.isEmpty }
        case .page: return false
        }
    }

    func toggle(_ k: Int) {
        guard phase == .answering, let q = question else { return }
        if q.multiple {
            if picks.contains(k) { picks.remove(k) } else { picks.insert(k) }
        } else {
            picks = [k]
        }
    }

    /// The answer put into Alta's question, then Alta's Check pressed; what Alta says comes back as feedback.
    func check() {
        guard canCheck, let q = question else { return }
        phase = .checking
        var a: [String: Any] = ["responseId": q.responseId ?? "", "type": q.type ?? ""]
        switch q.kind {
        case .choice:
            let chosen = picks.sorted()
            a["picks"] = chosen
            a["labels"] = chosen.compactMap { q.options.indices.contains($0) ? q.options[$0].label : nil }
            a["values"] = chosen.compactMap { q.options.indices.contains($0) ? q.options[$0].value : nil }
        case .blanks, .text, .longText:
            a["blanks"] = blanks
        case .dropdowns:
            a["drops"] = drops
        case .page:
            break
        }
        Task {
            let put = await call("return window.__simplAlta ? window.__simplAlta.answer(a) : null", ["a": a])
            guard (put as? [String: Any])?["ok"] as? Bool == true else { return fail("Your answer could not be put into Alta's question.") }
            let pressed = await call("return window.__simplAlta ? window.__simplAlta.check() : null", [:])
            guard (pressed as? [String: Any])?["ok"] as? Bool == true else { return fail("Alta's Check button could not be found.") }
            watch(seconds: 15)
        }
    }

    /// On to what Alta gives next: its Continue pressed; the next question comes as Alta's page asks for it.
    func next() {
        Task {
            let pressed = await call("return window.__simplAlta ? window.__simplAlta.next() : null", [:])
            guard (pressed as? [String: Any])?["ok"] as? Bool == true else { return fail("Alta's Continue button could not be found.") }
            withAnimation(Motion.gentle) { phase = .moving }
            watch(seconds: 15)
        }
    }

    /// A step the popup could not take: Alta's own page, where the student can take it, and why.
    private func fail(_ why: String) {
        withAnimation(Motion.gentle) {
            verdict = AltaVerdict(correct: nil, text: why + " Alta's own page is shown so you can go on there.", canContinue: false)
            phase = .answering
            showPage = true
        }
    }

    private func call(_ js: String, _ args: [String: Any], in at: WKFrameInfo? = nil) async -> Any? {
        await run(js, args, exactly: at ?? frame)
    }

    /// The script run in that frame exactly (nil: the main page).
    private func run(_ js: String, _ args: [String: Any], exactly target: WKFrameInfo?) async -> Any? {
        await withCheckedContinuation { (go: CheckedContinuation<Any?, Never>) in
            web.callAsyncJavaScript(js, arguments: args, in: target, in: .page) { result in
                switch result {
                case .success(let v): go.resume(returning: v)
                case .failure: go.resume(returning: nil)
                }
            }
        }
    }

    // MARK: The objectives and the mastery

    /// The objectives in Alta's order (the assignment's list, else the progress targets, else the one on screen), each
    /// with its mastery: its target's progress, or full once its status says it is mastered.
    var objectives: [AltaObjective] {
        guard let r = report else {
            // (before a question: the overview's objectives, each as mastered as the whole is said to be when it is done)
            let done = overview?.completed == true
            return (overview?.objectives ?? []).enumerated().map { k, o in
                AltaObjective(id: o.id, number: k + 1, name: o.name, mastery: done ? 1 : 0, current: false)
            }
        }
        var ids: [String] = r.objectives.compactMap(\.id)
        if ids.isEmpty { ids = r.targets.compactMap(\.id) }
        if ids.isEmpty, let c = r.current.id { ids = [c] }
        var seen = Set<String>()
        ids = ids.filter { seen.insert($0).inserted }
        return ids.enumerated().map { k, id in
            let t = r.targets.first { $0.id == id }
            let mastered = ["complete", "completed", "mastered", "done"].contains((t?.status ?? "").lowercased())
            let p = mastered ? 1 : min(max(t?.progress ?? 0, 0), 1)
            // (1.3.20) a name from the overview by its place when Alta's answers name only the objective on screen
            let byPlace = (overview?.objectives.count == ids.count) ? overview?.objectives[k].name : nil
            let named = r.objectives.first { $0.id == id }?.name ?? (r.current.id == id ? r.current.name : nil) ?? names[id] ?? byPlace
            return AltaObjective(id: id, number: k + 1, name: named ?? "Objective \(k + 1)", mastery: p, current: r.current.id == id)
        }
    }

    /// The whole assignment's mastery, 0…1.
    var mastery: Double {
        guard let r = report else {
            if overview?.completed == true { return 1 }
            return min(max((overview?.percent ?? 0) / 100, 0), 1)
        }
        if r.completed || (r.status ?? "").lowercased() == "complete" { return 1 }
        if let p = r.percent { return min(max(p / 100, 0), 1) }
        if let p = r.progress { return min(max(p, 0), 1) }
        let list = objectives
        return list.isEmpty ? 0 : list.map(\.mastery).reduce(0, +) / Double(list.count)
    }
}

/// The page's messages, to the session (held weakly: the web view's controller keeps its handlers).
final class AltaRelay: NSObject, WKScriptMessageHandler {
    weak var session: AltaSession?

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let text = message.body as? String else { return }
        let frame = message.frameInfo
        MainActor.assumeIsolated { session?.take(text, frame: frame) }
    }
}

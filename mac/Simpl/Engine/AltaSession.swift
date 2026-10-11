import AppKit
import SwiftUI
import WebKit

// (1.3.17) A Knewton Alta assignment taken in the app, as a quiz is: Open Tool on an Alta assignment opens a popup
// (AltaQuiz.swift) with the objectives and the total mastery down the left and the question in the rest, drawn by the
// app. Alta itself still sets each question and marks each answer: it is open, signed in through Canvas, on a page the
// popup keeps out of sight; the student's answer is put into Alta's own question there and Alta's own Check pressed,
// and what Alta says back is shown. A question of any other kind (a graph, a drag and drop, a widget) is Alta's own,
// shown alone in its place (1.3.21); a lesson, Alta's own page.

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
      // (1.3.34) which of the states Alta sends is a question to answer — not a lesson, a video, an example
      var isQuestion = function (s) {
        var a = (s && s.atom) || {}, c = (a.data && a.data.content) || {};
        return !!c.type && !/INSTRUCT|LESSON|VIDEO|EXAMPLE|TEACH|READING/.test(String((a.dataType || '') + ' ' + (a.purpose || '')).toUpperCase());
      };
      var pick = function (j) {
        var e = j.enrollment || {}, p = e.path || {}, d = e.dueDate || {}, a = j.analytics || {}, sp = a.statusAndProgress || {};
        // (1.3.34) Alta may send a lesson and its question together, the lesson first: the question is the one answered
        var states = Array.isArray(j.states) ? j.states : [];
        var st = states.filter(isQuestion)[0] || states[0] || {}, atom = st.atom || {}, ci = st.compoundInstance || {}, lo = atom.learningObjective || {};
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
          question: states.length ? question(atom, ci) : null,
          withLesson: states.some(function (s) { return s && s.atom && !isQuestion(s); }),
          history: (j.history && Array.isArray(j.history.sequences) ? j.history.sequences : []).slice(0, 40).map(function (s) {
            return { right: num(s.numCorrectResponses) || 0, wrong: num(s.numIncorrectResponses) || 0, skipped: num(s.numSkippedAssessments) || 0, lessons: num(s.numInstructional) || 0 };
          }),
          stuck: j.stuckLo !== null && j.stuckLo !== undefined
        };
      };
      // (1.3.34: whatever answer of Alta's carries states with atoms, at whatever address)
      var hasStates = function (j) { return !!j && Array.isArray(j.states) && j.states.some(function (s) { return s && s.atom; }); };
      var seen = function (url, text) {
        var j; try { j = typeof text === 'string' ? JSON.parse(text) : text; } catch (e) { return; }
        if (hasStates(j)) post(pick(j));
      };
      // (the assignment's overview, before it is started or between questions: whatever Alta's page reads that names
      // the assignment, its objectives with their estimates and the mastery — found by those fields, wherever they sit;
      // the answer key and the student's identity are passed over, never read)
      var SKIP = { correct_answer: 1, success_condition: 1, validation: 1, valid_response: 1, ltiEnrollment: 1, userId: 1, registrationId: 1 };
      var scan = function (j) {
        var out = { kind: 'overview', path: location.pathname, name: null, due: null, threshold: null, percent: null, progress: null, status: null, started: null, completed: null, objectives: [], targets: [] };
        var had = {}, hadT = {};
        // (1.3.21) each objective's mastery, in whatever answer of Alta's carries it (a checked answer's, not only
        // the question's): its progress target, by the objective's id
        var target = function (t) {
          var id = str(String(t.target_id || t.targetId || '').replace(/^lref-/, ''));
          if (id && !hadT[id] && typeof t.progress === 'number') { hadT[id] = 1; out.targets.push({ id: id, progress: t.progress, status: str(t.status) }); }
        };
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
          if (o.statusAndProgress && typeof o.statusAndProgress === 'object') {
            var sp = o.statusAndProgress;
            if (!out.status) out.status = str(sp.status);
            if (out.progress === null && typeof sp.progress === 'number') out.progress = sp.progress;
            if (Array.isArray(sp.targets)) sp.targets.forEach(function (t) { if (t && typeof t === 'object') target(t); });
          }
          if (o.target_id || o.targetId) target(o);
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
        // (1.3.34: and the kind of each state it carries — purpose / dataType / question type — never what is in it)
        try {
          jsonSeen.push({ at: new URL(String(url || ''), location.href).pathname.slice(0, 120), keys: Object.keys(j).slice(0, 16),
            states: Array.isArray(j.states) ? j.states.slice(0, 6).map(function (s) { var a = (s && s.atom) || {}, c = (a.data && a.data.content) || {}; return [str(a.purpose), str(a.dataType), str(c.type)].join(' / '); }) : undefined });
          if (jsonSeen.length > 40) jsonSeen.shift();
        } catch (e) {}
        if (hasStates(j)) return seen(url, j);
        var o = scan(j);
        if (o.objectives.length || o.name || o.targets.length || o.percent !== null || o.progress !== null) post(o);
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
      // (1.3.39) what Alta says of an answer: the newest word on the page — Alta keeps each attempt's verdict (Answer 1:
      // That's incorrect… Answer 2: Perfect…), so any "incorrect" in sight is no verdict on the last; and only what came
      // after Check was pressed (`was`: the marks and words there before, with their words then)
      var BADWORDS = /\b(incorrect|not quite|try again|not correct|wrong|mistakes? (are|is) part of learning)\b/i;
      var GOODWORDS = /\b(correct|perfect|well done|great job|nice work|excellent|paying off|nailed it|you got it|exactly right)\b/i;
      var verdictMarks = function () {
        var marks = [].slice.call(document.querySelectorAll('.lrn_incorrect, .lrn-incorrect, .lrn_correct, .lrn-correct, [class*="correct" i]')).filter(shown).map(function (e) {
          var c = String(e.getAttribute('class') || '');
          return { el: e, verdict: /incorrect|wrong/i.test(c) ? 'incorrect' : 'correct', text: '' };
        });
        var boxes = [].slice.call(document.querySelectorAll('[class*="feedback" i], [role="alert"], [aria-live]')).filter(function (b) { return shown(b) && !b.matches(CLICKABLE); }).map(function (b) {
          var t = (b.innerText || '').trim();
          return { el: b, verdict: BADWORDS.test(t) ? 'incorrect' : (GOODWORDS.test(t) ? 'correct' : null), text: t };
        }).filter(function (x) { return x.text; });
        return marks.concat(boxes);
      };
      var snapshot = function () { var m = new Map(); verdictMarks().forEach(function (x) { m.set(x.el, x.text); }); return m; };
      var verdictNow = function (was) {
        var all = verdictMarks().filter(function (x) { return !was || !was.has(x.el) || was.get(x.el) !== x.text; });
        all.sort(function (a, b) { return a.el === b.el ? 0 : (a.el.compareDocumentPosition(b.el) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1); });
        var said = all.filter(function (x) { return x.verdict; });
        var last = said[said.length - 1] || null;
        var words = all.filter(function (x) { return x.text; });
        var text = (last && last.text) || (words.length ? words[words.length - 1].text : '');
        return { verdict: last ? last.verdict : null, text: text.slice(0, 1200), next: buttons(NEXT).length > 0, check: buttons(CHECK).length > 0 };
      };
      var watching = null;
      var watch = function (before) {
        if (watching) clearInterval(watching);
        var t0 = Date.now(), was = before || snapshot();
        watching = setInterval(function () {
          if (focused) trim(false); // (Alta's verdict, in sight before it is read, when the question is shown alone)
          var s = verdictNow(was);
          if (s.verdict || s.next || Date.now() - t0 > 12000) {
            clearInterval(watching); watching = null;
            post({ kind: 'feedback', verdict: s.verdict, text: s.text, next: s.next, timedOut: !s.verdict && !s.next });
          }
        }, 300);
      };
      // (1.3.21) Alta's own Check pressed on its page (a question the popup does not draw): its verdict read back too
      document.addEventListener('click', function (e) {
        try {
          var path = e.composedPath ? e.composedPath() : [e.target];
          for (var i = 0; i < path.length && i < 8; i++) {
            var el = path[i];
            if (el && el.matches && el.matches(CLICKABLE)) {
              var said = norm(el.innerText || el.textContent || el.value || el.getAttribute('aria-label'));
              if (!watching && CHECK.test(said)) watch();
              if (NEXT.test(said)) { focusFirst = true; dirty = true; } // (the next question shown from its top)
              break;
            }
          }
        } catch (x) {}
      }, true);

      // (1.3.21) the mastery as Alta's page shows it: its progress bars (Mastery, each objective's) and a percentage by
      // the word Mastery — passed whenever it changes, so the popup's rail moves when Alta's does
      var meter = function () {
        var bars = deep('[role="progressbar"], progress, meter').filter(shown).map(function (b) {
          var now = num(b.getAttribute('aria-valuenow') !== null ? b.getAttribute('aria-valuenow') : b.value);
          var max = num(b.getAttribute('aria-valuemax') !== null ? b.getAttribute('aria-valuemax') : b.max) || 100;
          if (now === null) return null;
          var by = b.getAttribute('aria-labelledby'), named = by ? document.getElementById(by.split(' ')[0]) : null;
          var label = norm(b.getAttribute('aria-label') || (named && named.innerText) || b.title || '');
          return { label: label.slice(0, 160), value: Math.max(0, Math.min(1, now / max)) };
        }).filter(Boolean).slice(0, 30);
        var lines = String((document.body && document.body.innerText) || '').split('\n').map(function (l) { return l.trim(); }).filter(Boolean);
        var percent = null;
        lines.forEach(function (l, i) {
          if (percent !== null || !/^(total |overall |assignment )?mastery\b/i.test(l)) return;
          var m = /(\d{1,3}(?:\.\d+)?)\s*%/.exec(l) || /^(\d{1,3}(?:\.\d+)?)\s*%$/.exec(lines[i + 1] || '');
          if (m) percent = Math.min(100, +m[1]);
        });
        if (percent === null) bars.forEach(function (b) { if (percent === null && /mastery/.test(b.label)) percent = Math.round(b.value * 1000) / 10; });
        return { percent: percent, bars: bars };
      };
      var lastMeter = '';
      setInterval(function () {
        var m = meter();
        if (m.percent === null && !m.bars.length) return;
        var s = JSON.stringify(m);
        if (s !== lastMeter) { lastMeter = s; post({ kind: 'meter', percent: m.percent, bars: m.bars }); }
      }, 1500);

      // (1.3.21) Any kind of question in the popup: a question the popup does not draw itself (a graph, a drag and drop,
      // a widget of any kind) is Alta's own, shown alone — Alta's page with everything but the question, its buttons
      // (Check, Next, help) and what Alta says of it (its verdict, its pop-ups) put out of sight. Nothing is taken out
      // of the page; it is only hidden, and shown again as it was for Alta's Page.
      var QUESTION = '.learnosity-item, .lrn-assess-item, .lrn_widget, .learnosity-response, .lrn-question, .lrn_question';
      var HELPERS = /^(i don'?t know|help me solve this|view an example|review instruction|show me how|show me|hint|show hint|try again|explain|skip)$/;
      var hidden = [];
      var parentOf = function (el) { return el.parentElement || (el.parentNode && el.parentNode.host) || null; };
      // (each change kept with what it replaced, and put back exactly)
      var stash = function (el, props) {
        var h = { el: el, was: {}, set: props };
        Object.keys(props).forEach(function (k) { h.was[k] = [el.style.getPropertyValue(k), el.style.getPropertyPriority(k)]; el.style.setProperty(k, props[k], 'important'); });
        hidden.push(h);
      };
      var hide = function (el) { stash(el, { display: 'none' }); };
      // (1.3.31: put back only what is still as the popup set it — what the page has changed since is the page's)
      var unhide = function (h) {
        Object.keys(h.was).forEach(function (k) {
          if (h.el.style.getPropertyValue(k) !== h.set[k] || h.el.style.getPropertyPriority(k) !== 'important') return;
          var w = h.was[k];
          if (w[0]) h.el.style.setProperty(k, w[0], w[1]); else h.el.style.removeProperty(k);
        });
      };
      // (1.3.23) Alta's own Check and Next, pressed from the popup's bar: kept working, but off to the side
      var OFFSTAGE = { position: 'absolute', left: '-10000px', top: '0' };
      // (1.3.25) Alta's More Instruction, pressed from the popup's bar too; its Feedback (a report to Alta) put away
      var INSTRUCT = /^(more instruction)$/;
      var REPORT = /^(feedback)$/;
      var lift = function () { hidden.forEach(unhide); hidden = []; };
      // every button with these words on screen, greyed out or not
      var controls = function (re) {
        return deep(CLICKABLE).filter(function (b) { return shown(b) && re.test(norm(b.innerText || b.textContent || b.value || b.getAttribute('aria-label'))); });
      };
      // (1.3.22) where the page shows what the question asks: the words of it the app was given (a run of them between
      // its maths), found in the page's text
      var focusHint = '';
      // (1.3.42: within `root` — the question's card, when there is one — and never in the Current objective card, whose
      // words can begin as the question's do: "Find the derivative of…")
      var objectiveCard = function (el) {
        for (var a = el, i = 0; a && i < 4 && a !== document.body; a = parentOf(a), i++) {
          var t = a.innerText || '';
          if (t.length < 600 && /current objective/i.test(t)) return true;
        }
        return false;
      };
      var asked = function (root) {
        var want = norm(focusHint);
        if (want.length < 8 || !document.body) return null;
        try {
          var w = document.createTreeWalker(root || document.body, NodeFilter.SHOW_TEXT), n;
          while ((n = w.nextNode())) {
            if (norm(n.data).indexOf(want) >= 0 && n.parentElement && shown(n.parentElement) && !objectiveCard(n.parentElement)) return n.parentElement;
          }
        } catch (e) {}
        return null;
      };
      // the smallest element holding them all (across web components' own trees)
      var common = function (els) {
        if (!els.length) return null;
        var chain = [];
        for (var x = els[0]; x; x = parentOf(x)) chain.push(x);
        var best = 0;
        for (var i = 1; i < els.length; i++) {
          var at = -1;
          for (var y = els[i]; y; y = parentOf(y)) { at = chain.indexOf(y); if (at >= 0) break; }
          if (at < 0) return null;
          if (at > best) best = at;
        }
        return chain[best];
      };
      var label = function (el) {
        var c = typeof el.className === 'string' ? el.className.trim().split(/\s+/).slice(0, 4).join('.') : '';
        return el.tagName.toLowerCase() + (el.id ? '#' + el.id.slice(0, 30) : '') + (c ? '.' + c.slice(0, 80) : '');
      };
      var shape = function () {
        var q = deep(QUESTION).filter(shown)[0];
        if (!q) return [];
        var out = [];
        for (var x = q, i = 0; x && i < 14; x = parentOf(x), i++) {
          out.push({ el: label(x), kids: [].slice.call(x.children || []).slice(0, 12).map(function (k) { return label(k) + (shown(k) ? '' : ' (unseen)'); }) });
          if (x === document.body) break;
        }
        return out;
      };
      // (1.3.28) a lesson of Alta's (More Instruction ▸ View Instruction): no question in it, only what it teaches and
      // its Continue — the smallest part holding its words and Continue, else the nearest part round Continue with
      // something to read in it
      var lesson = function () {
        var go = controls(NEXT)[0];
        if (!go) return null;
        var asks = asked();
        if (asks) { var c = common([asks, go]); if (c && c !== document.body && c !== document.documentElement) return c; }
        for (var x = parentOf(go); x && x !== document.body && x !== document.documentElement; x = parentOf(x)) {
          if (norm(x.innerText || '').length > 400) return x;
        }
        return null;
      };
      // (1.3.30) what a page's typesetter keeps out of sight to measure its fonts by (MathJax's MathJax_Hidden,
      // MathJax_Font_Test…), and anything already invisible: never hidden — hidden, MathJax could not measure, waited
      // long and set the maths out in the wrong fonts and places
      var helper = function (el) {
        var names = (el.id || '') + ' ' + (typeof el.className === 'string' ? el.className : '');
        if (/mathjax|mjx/i.test(names)) return true;
        try { return getComputedStyle(el).visibility === 'hidden'; } catch (e) { return false; }
      };
      // (1.3.31) the part just before the question that holds what it asks ("Question", Let h(x) = …), when its words
      // were not found by the app's: the question's own card above its answer's
      // (1.3.38) the question's whole card, as Alta has it — a "Question" header, what it asks, the answer and its
      // buttons each in a part of its own: the nearest part round the answer that begins "Question" and holds no
      // other question
      var card = function (el) {
        for (var a = el, j = 0; a && j < 8 && a !== document.body && a !== document.documentElement; a = parentOf(a), j++) {
          if (/^\s*question\b/i.test(a.innerText || '') && a.querySelectorAll(QUESTION).length <= 8) return a;
        }
        return null;
      };
      var before = function (el) {
        var whole = card(parentOf(el));
        if (whole) return whole;
        for (var x = el, i = 0; x && i < 4 && x !== document.body; x = parentOf(x), i++) {
          var p = x.previousElementSibling;
          while (p && !shown(p)) p = p.previousElementSibling;
          if (!p) continue;
          if (p.querySelector(QUESTION)) return null;
          return /^\s*question\b/i.test(p.innerText || '') ? p : null;
        }
        return null;
      };
      // (1.3.31) a lesson left above the question on Alta's page (after its Continue, the question comes under it): the
      // part before the question, at any level up, with much to read and no question in it
      var above = function (el) {
        // (1.3.41: only close round the question's card — where Alta puts a lesson, in the same flow — never Alta's top:
        // the course, the title, the mastery, the Current objective card)
        for (var x = el, lv = 0; x && lv < 3 && x !== document.body && x !== document.documentElement; x = parentOf(x), lv++) {
          for (var p = x.previousElementSibling; p; p = p.previousElementSibling) {
            if (!shown(p) || p.querySelector(QUESTION)) continue;
            var t = p.innerText || '';
            // (1.3.33: never the question's own card — "Question", what it asks, long with its maths — taken for a lesson)
            if (/^\s*question\b/i.test(t)) continue;
            if (/current objective|assignment mastery|objective mastery/i.test(t)) continue;
            if (norm(t).length > 400) return p;
          }
        }
        return null;
      };
      var stacked = null, lessonAlone = false;
      var keepers = function (iframeToo) {
        stacked = null;
        var qs = deep(QUESTION).filter(shown), lessonOnly = false;
        if (!qs.length) { var l = lesson(); if (l) { qs = [l]; lessonOnly = true; } }
        lessonAlone = lessonOnly;
        if (!qs.length && iframeToo) {
          var big = deep('iframe').filter(shown).sort(function (a, b) {
            var ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect();
            return rb.width * rb.height - ra.width * ra.height;
          })[0];
          if (big) qs = [big];
        }
        if (!qs.length) return [];
        // (1.3.22) the question's words and its Check (greyed out until it is answered, but Alta's own to press) are kept
        // with it: the smallest part of the page holding the question, what it asks and its buttons — unless that is
        // the whole page, when each is kept on its own
        // (1.3.37) a lesson above the question (Alta's View Instruction: the question stays on the page under it) — found
        // first, so its own Continue and words are not taken for the question's
        // (looked for above the question's whole card, so what the question asks is never taken for a lesson)
        var qcard = lessonOnly ? null : card(common(qs) || qs[0]);
        stacked = lessonOnly ? null : above(qcard || common(qs) || qs[0]);
        var acts = controls(CHECK).concat(controls(NEXT)).filter(function (b) { return !(stacked && stacked.contains(b)); });
        var asks = qcard ? asked(qcard) : asked();
        if (asks && stacked && stacked.contains(asks)) asks = null;
        var anchors = qs.concat(acts, asks ? [asks] : []);
        var whole = common(anchors);
        if (whole && whole !== document.body && whole !== document.documentElement && anchors.length > qs.length && !(stacked && whole.contains(stacked))) qs = [whole];
        else qs = qs.concat(acts, asks ? [asks] : []);
        if (!lessonOnly && !asks) {
          var pre = before(common(qs) || qs[0]);
          if (pre) { var c = common(qs.concat([pre])); if (c && c !== document.body && !(stacked && c.contains(stacked))) qs = [c]; else qs = qs.concat([pre]); }
        }
        // (the lesson and the question under it, both kept, top to bottom: the student reads, then answers below)
        if (stacked) qs = [stacked].concat(qs);
        var more = buttons(HELPERS).concat(
          deep('[role="dialog"], [role="alertdialog"], [aria-modal="true"], dialog[open]').filter(shown),
          deep('[role="alert"], [class*="feedback" i]').filter(function (e) { return shown(e) && norm(e.innerText || ''); }));
        var all = qs.concat(more);
        // (the outermost of them: one inside another is kept with it)
        return all.filter(function (e, k) { return all.indexOf(e) === k && !all.some(function (o) { return o !== e && o.contains(e); }); });
      };
      var focused = null, focusFirst = true, focusIframe = false, dirty = true, kept = [], lastFound = false;
      // (worked out again whenever the page changes outside what is kept — a verdict, a pop-up, the next question may
      // come in a part that is hidden — from the page as it is, all in one go, so nothing flickers. (1.3.24) Changes
      // inside the question itself — its maths field's cursor blinking, the student typing — are left alone: worked out
      // again on each, the page jumped back to its top.)
      try {
        new MutationObserver(function (records) {
          if (dirty) return;
          for (var i = 0; i < records.length; i++) {
            var t = records[i].target;
            if (!kept.some(function (k) { return k === t || k.contains(t); })) { dirty = true; return; }
          }
        }).observe(document, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['hidden', 'class', 'open', 'aria-hidden', 'aria-modal', 'disabled', 'aria-disabled'] });
      } catch (e) {}
      var trim = function (force) {
        // (a button of Alta's still in sight that the popup's bar stands for — new content came inside what is kept, a
        // lesson in a question's place — is worked out again too)
        if (!force && !dirty && driven && lastFound && controls(CHECK).concat(controls(NEXT), controls(INSTRUCT), controls(REPORT)).some(function (b) { return b.style.getPropertyValue('left') !== '-10000px' && b.style.getPropertyValue('display') !== 'none'; })) dirty = true;
        if (!force && !dirty && hidden.length) { tell(lastFound); return true; }
        dirty = false;
        // (where the student has scrolled to, held through the working out)
        var spots = [], se = document.scrollingElement || document.documentElement;
        if (se && se.scrollTop > 0) spots.push([se, se.scrollTop]);
        kept.forEach(function (k) { for (var x = parentOf(k); x; x = parentOf(x)) if (x.scrollTop > 0 && x !== se) spots.push([x, x.scrollTop]); });
        lift();
        var keep = keepers(focusIframe);
        kept = keep;
        lastFound = keep.length > 0;
        if (!keep.length) { tell(false); return false; }
        var path = new Set();
        keep.forEach(function (e) { for (var x = e; x; x = parentOf(x)) path.add(x); });
        var want = new Set();
        path.forEach(function (x) {
          var p = parentOf(x);
          if (!p || x === document.body || x === document.documentElement) return;
          var kids = (x.parentNode && x.parentNode.host) ? x.parentNode.children : p.children;
          [].forEach.call(kids, function (s) {
            if (!path.has(s) && !/^(SCRIPT|STYLE|LINK|META|TEMPLATE)$/.test(s.tagName) && !helper(s)) want.add(s);
          });
        });
        want.forEach(hide);
        // (1.3.40) the room the page's parts round the question keep at their top for what is hidden (Alta's header, its
        // title, the objective card) taken in too: the question at the top, no empty band above it
        path.forEach(function (x) {
          if (x === document.body || x === document.documentElement || keep.indexOf(x) >= 0) return;
          try {
            var cs = getComputedStyle(x);
            if (parseFloat(cs.paddingTop) > 0 || parseFloat(cs.marginTop) > 0) stash(x, { 'padding-top': '0px', 'margin-top': '0px' });
          } catch (e) {}
        });
        if (driven) {
          controls(CHECK).concat(controls(NEXT), controls(INSTRUCT)).filter(function (b) { return !(stacked && stacked.contains(b)); }).forEach(function (b) { stash(b, OFFSTAGE); });
          controls(REPORT).forEach(hide);
        }
        if (focusFirst) { focusFirst = false; try { keep[0].scrollIntoView({ block: 'start' }); } catch (x) {} }
        else spots.forEach(function (s) { if (s[0].scrollTop !== s[1]) s[0].scrollTop = s[1]; });
        tell(true);
        return true;
      };
      // (1.3.29) raw maths on show: words of TeX in the page's own text, not in a typesetter's keeping (MathQuill's copy
      // of what is typed, MathJax's source, KaTeX's) and not hidden
      var RAWTEX = /\\(begin\{|frac\{|displaystyle|left\(|color\{)/;
      // (MathJax 2 still at work on the page)
      var mathjaxBusy = function () {
        try { var q = window.MathJax && window.MathJax.Hub && window.MathJax.Hub.queue; return !!(q && (q.running || q.pending)); } catch (e) { return false; }
      };
      var rawIn = function (root) {
        if (mathjaxBusy()) return true;
        try {
          var w = document.createTreeWalker(root, NodeFilter.SHOW_TEXT), n, seen = 0;
          while ((n = w.nextNode()) && seen++ < 20000) {
            if (!RAWTEX.test(n.data)) continue;
            var p = n.parentElement;
            if (!p || p.closest('.mq-math-mode, script, style, textarea, .katex, mjx-container')) continue;
            if (shown(p)) return true;
          }
        } catch (e) {}
        return false;
      };
      // (the popup told whether Alta's question is found, and whether its Check can be pressed yet)
      var told = '';
      var tell = function (found) {
        // (1.3.38: an inner page with no question in it — Learnosity's helper, Alta's chat — says nothing: its "not found"
        // came after the page's own "found", and the popup thought the question gone)
        if (!found && window.top !== window) return;
        var m = { kind: 'focus', found: found, check: controls(CHECK).some(function (b) { return !b.disabled && b.getAttribute('aria-disabled') !== 'true'; }), next: buttons(NEXT).length > 0, nextAny: controls(NEXT).length > 0, instruct: buttons(INSTRUCT).length > 0, hasCheck: controls(CHECK).length > 0,
          dialog: deep('[role="dialog"], [role="alertdialog"], [aria-modal="true"], dialog[open]').filter(shown).length > 0,
          lessonToo: !!stacked, showingLesson: lessonAlone,
          // (1.3.29) maths still in its raw words (\\frac{…}, \\begin{align}) in what is shown: its typesetter not done yet
          raw: found && kept.some(rawIn) };
        var said = JSON.stringify(m);
        if (said !== told) { told = said; post(m); }
      };
      // (1.3.23) Alta's question in the popup's own colours, light or dark: a style sheet of the popup's, put in while the
      // question is shown alone and taken out after
      var themeEl = null, driven = false;
      var theme = function (css) {
        if (!css) { if (themeEl && themeEl.parentNode) themeEl.parentNode.removeChild(themeEl); themeEl = null; return; }
        if (!themeEl) { themeEl = document.createElement('style'); themeEl.id = 'simpl-theme'; }
        if (themeEl.textContent !== css) themeEl.textContent = css;
        if (!themeEl.isConnected) (document.head || document.documentElement).appendChild(themeEl);
      };
      var focus = function (o) {
        o = o || {};
        if (!o.on) { if (focused) clearInterval(focused); focused = null; lift(); kept = []; theme(null); driven = false; told = ''; return { found: false }; }
        focusIframe = !!o.iframe;
        if (!!o.bar !== driven) { driven = !!o.bar; dirty = true; }
        // (the app's own View Instruction / Back to Question; otherwise the view stays as the page has it)
        theme(typeof o.css === 'string' ? o.css : null);
        if (typeof o.hint === 'string' && o.hint !== focusHint) { focusHint = o.hint; dirty = true; }
        if (!focused) { focusFirst = true; focused = setInterval(function () { trim(false); }, 500); }
        return { found: trim(true) };
      };

      window.__simplAlta = {
        focus: focus,
        meter: meter,
        // the assignment's overview: whether it offers to start (or go on), and pressing that
        peek: function () { var b = buttons(START)[0]; return { start: !!b, word: b ? (b.innerText || b.textContent || b.value || '').trim() : null, overview: scrape() }; },
        diag: function () {
          return {
            at: location.host + location.pathname, top: window.top === window, json: jsonSeen.slice(),
            buttons: deep(CLICKABLE).filter(shown).slice(0, 50).map(function (b) { return b.tagName.toLowerCase() + ': ' + norm(b.innerText || b.textContent || b.value || b.getAttribute('aria-label')).slice(0, 40) + (b.disabled || b.getAttribute('aria-disabled') === 'true' ? ' (greyed out)' : ''); }),
            estimates: scrape().objectives.length, start: buttons(START).length, check: buttons(CHECK).length, next: buttons(NEXT).length,
            // (1.3.22) how the page is built round the question — elements and class names only, never words — and
            // what the question shown alone keeps
            checkAny: controls(CHECK).length, asked: !!asked(), hidden: hidden.length, shape: shape(),
            // (1.3.41) what is taken for a lesson above the question, if anything (its element, its length — no words)
            lesson: stacked ? label(stacked) + ' (' + norm(stacked.innerText || '').length + ' chars)' : null
          };
        },
        begin: function () { var b = buttons(START)[0]; if (!b) return { ok: false }; b.click(); return { ok: true }; },
        answer: answer,
        check: function () { var b = buttons(CHECK)[0]; if (!b) return { ok: false }; var was = snapshot(); b.click(); watch(was); return { ok: true }; },
        next: function () { var b = buttons(NEXT)[0]; if (!b) return { ok: false }; focusFirst = true; dirty = true; b.click(); return { ok: true }; },
        instruct: function () { var b = buttons(INSTRUCT)[0]; if (!b) return { ok: false }; dirty = true; b.click(); return { ok: true }; },
        state: function () { return verdictNow(); }
      };

      // (1.3.20) Alta's welcome pop-ups (Welcome to your adaptive assignment!, Assignments adapt to you…): put away as they
      // come, by their own Got it — only a Got it inside a dialog, never anything of the assignment's
      var GOTIT = /^(got it|ok, got it|okay, got it)$/;
      var dismiss = function () {
        deep('[role="dialog"], [role="alertdialog"], [aria-modal="true"], dialog[open], .modal, [class*="modal" i]').filter(shown).forEach(function (d) {
          deepIn(d, CLICKABLE).filter(function (b) { return shown(b) && GOTIT.test(norm(b.innerText || b.textContent || b.value || b.getAttribute('aria-label'))); }).slice(0, 1).forEach(function (b) { b.click(); });
        });
        // (1.3.37) Alta's notices with a Dismiss (Awesome, you completed 1 of 3 objectives! Moving on to the next topic!):
        // their words passed to the popup, which shows them its own way, and the notice put away by its Dismiss
        deep(CLICKABLE).filter(function (b) { return shown(b) && /^dismiss$/.test(norm(b.innerText || b.textContent || b.value || b.getAttribute('aria-label'))); }).slice(0, 2).forEach(function (b) {
          var box = b.closest('[role="alert"], [role="status"], [class*="toast" i], [class*="snack" i], [class*="notif" i], [class*="banner" i], [class*="message" i]') || b.parentElement;
          var words = String((box && box.innerText) || '').replace(/\bdismiss\b/ig, '').replace(/\s+/g, ' ').trim().slice(0, 300);
          if (words) post({ kind: 'notice', text: words });
          b.click();
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
        // (1.3.22: a lesson, a video, an example is Alta's to show — by what Alta calls it; a question of a kind the popup
        // draws is drawn whatever else Alta's answer says of it)
        let said = ((dataType ?? "") + " " + (purpose ?? "")).uppercased()
        if ["INSTRUCT", "LESSON", "VIDEO", "EXAMPLE", "TEACH", "READING"].contains(where: { said.contains($0) }) { return .page }
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

    /// (1.3.22) A run of the words it asks in (the longest between its maths), for finding it on Alta's page.
    var hint: String {
        let text = (prompt ?? "")
            .replacingOccurrences(of: #"\$\$[\s\S]*?\$\$|\$_[\s\S]*?\$_|\\\([\s\S]*?\\\)|\\\[[\s\S]*?\\\]|<[^>]+>"#, with: "\n", options: .regularExpression)
            .replacingOccurrences(of: "&nbsp;", with: " ")
        let runs = text.components(separatedBy: "\n").map { $0.split(whereSeparator: \.isWhitespace).joined(separator: " ") }
        let longest = runs.max { $0.count < $1.count } ?? ""
        return longest.count >= 8 ? String(longest.prefix(36)) : ""
    }

    /// The blanks in a cloze template ({{response}} each).
    var blankCount: Int { (template ?? "").components(separatedBy: "{{response}}").count - 1 }
}

/// (1.3.17) The assignment's overview, as Alta's page reads it before it is started (or between questions): its name,
/// when it is due, its objectives with Alta's estimate of the questions each takes, and how far it is mastered.
struct AltaOverview: Decodable, Equatable {
    struct Objective: Codable, Equatable { var id: String; var name: String; var low: Double?; var high: Double? }
    var name: String?
    var due: Double?
    /// (1.3.19) As the page words them, when no answer of Alta's gave them ("Monday, Oct 12 11:59pm PDT", "Not started").
    var dueText: String?
    var statusText: String?
    var threshold: Double?
    var percent: Double?
    /// (1.3.21) The whole's progress (0…1) and each objective's, from any answer of Alta's that carries them.
    var progress: Double?
    var targets: [AltaReport.Target]?
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
    @Published var showPage = false { didSet { if showPage != oldValue { refocus() } } }
    /// (1.3.21) Alta's whole page, as it is (Alta's Page); otherwise, for a question the popup does not draw, Alta's
    /// own question shown alone.
    @Published var wholePage = false { didSet { if wholePage != oldValue { refocus() } } }
    /// (1.3.21) The mastery last heard, from wherever Alta said it (a question's answer, a checked answer's, the bars
    /// on its page), 0…1: the whole, and each objective's by its id.
    @Published private(set) var livePercent: Double?
    /// (1.3.23) Alta's own question, answered in its own answer box: whether it is found on the page, and whether its
    /// Check (pressed from the popup's bar) can be pressed yet. Drawn by the popup itself only when it is not found.
    @Published private(set) var altaFound = false
    @Published private(set) var altaCanCheck = false
    /// (1.3.25) Alta offers More Instruction (pressed from the popup's bar).
    @Published private(set) var altaCanInstruct = false
    /// (1.3.26) A pop-up of Alta's is open over its question (Having trouble?): the popup's bar is put away under it.
    @Published private(set) var altaDialog = false
    /// (1.3.28) What Alta's page offers to press: a Check (a question), or only Continue (a lesson).
    @Published private(set) var altaHasCheck = false
    @Published private(set) var altaCanNext = false
    /// (1.3.30) Alta's Continue is on the page, ready or greyed out (a lesson's, until it is read).
    @Published private(set) var altaHasNext = false
    /// (1.3.36) What is shown is a lesson alone on the page (no question under it): the bar offers its Continue, never
    /// Check. (1.3.37: a lesson with the question under it is shown with it, top to bottom.)
    @Published private(set) var altaShowingLesson = false
    /// (1.3.37) A notice of Alta's (an objective completed…), shown a few seconds at the top of the question.
    @Published private(set) var notice: String?
    private var noticeTask: Task<Void, Never>?
    /// (1.3.29) The maths on Alta's page is still in its raw words, its typesetter not done: covered meanwhile, for ten
    /// seconds at most.
    @Published private(set) var altaRaw = false
    @Published private(set) var rawGaveUp = false
    @Published private(set) var drawsItself = false
    private var css = ""
    @Published private(set) var liveTargets: [String: AltaReport.Target] = [:]
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
        web.setValue(false, forKey: "drawsBackground") // (1.3.23: Alta's question on the popup's own ground)
        r.session = self
    }

    /// Alta opened: the assignment's launch through Canvas (or the page asked for), and a watch on it — a page that says
    /// nothing in a while (a start screen, a sign-in) is shown as it is.
    func start(engine: Engine) async {
        guard web.url == nil else { return }
        if let data = UserDefaults.standard.data(forKey: knownKey), let list = try? JSONDecoder().decode([AltaOverview.Objective].self, from: data) { known = list }
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
                    self.wholePage = true
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
            if showPage && !wholePage { refocus() } // (a new page of Alta's: its question shown alone again)
        case "overview":
            guard let o = try? JSONDecoder().decode(AltaOverview.self, from: data) else { return }
            if self.frame == nil { self.frame = frame }
            for x in o.objectives where !x.name.isEmpty && names[x.id] != x.name { names[x.id] = x.name }
            UserDefaults.standard.set(names, forKey: Self.namesKey)
            withAnimation(Motion.gentle) {
                overview = merge(overview, o)
                hear(percent: o.percent.map { $0 / 100 } ?? o.progress, targets: o.targets ?? [])
                keep(o.objectives)
            }
            if phase == .loading && (!o.objectives.isEmpty || o.name != nil) { probeStart(frame) }
        case "content":
            guard var r = try? JSONDecoder().decode(AltaReport.self, from: data) else { return }
            // (1.3.34) an answer that carries the question but not the assignment's objectives: the last ones kept
            if let old = report {
                if r.objectives.isEmpty { r.objectives = old.objectives }
                if r.targets.isEmpty { r.targets = old.targets }
                if r.name == nil { r.name = old.name }
                if r.due == nil { r.due = old.due }
                if r.threshold == nil { r.threshold = old.threshold }
            }
            self.frame = frame
            remember(r)
            let key = r.question?.key
            let isNew = key != questionKey
            withAnimation(Motion.gentle) {
                report = r
                hear(percent: r.percent.map { $0 / 100 } ?? r.progress, targets: r.targets)
                if isNew {
                    questionKey = key
                    picks = []
                    let k = r.question?.kind
                    blanks = Array(repeating: "", count: k == .text || k == .longText ? 1 : (r.question?.blankCount ?? 0))
                    drops = Array(repeating: "", count: r.question?.choices.count ?? 0)
                    verdict = nil
                    phase = .answering
                    // (1.3.23) every question in Alta's own answer box, in the popup's colours; a lesson, Alta's page
                    drawsItself = false
                    altaFound = false
                    altaCanCheck = false
                    altaCanInstruct = false
                    wholePage = r.question == nil
                    showPage = true
                }
            }
            watchdog?.cancel()
            if isNew, let k = key, r.question.map({ $0.kind != .page }) == true { fallBack(k) }
            if isNew && altaDrawn { refocus() } // (1.3.24: the new question picked out afresh, its Check moved aside)
            // (1.3.28) back from a lesson to the same question: on with it
            if !isNew && phase == .moving {
                withAnimation(Motion.gentle) { phase = .answering }
                if altaDrawn { refocus() }
            }
        case "notice":
            guard let t = head["text"] as? String, !t.isEmpty else { return }
            withAnimation(Motion.gentle) { notice = t }
            noticeTask?.cancel()
            noticeTask = Task { [weak self] in
                try? await Task.sleep(nanoseconds: 6_000_000_000)
                guard !Task.isCancelled else { return }
                withAnimation(Motion.gentle) { self?.notice = nil }
            }
        case "focus":
            // (1.3.38) only the page that holds the question is heard on whether it is there: an inner page without it
            // (Learnosity's helper, a chat) never says it is gone
            if !(head["found"] as? Bool ?? false) && !frame.isMainFrame { return }
            withAnimation(Motion.gentle) {
                altaFound = head["found"] as? Bool ?? false
                altaCanCheck = head["check"] as? Bool ?? false
                altaCanInstruct = head["instruct"] as? Bool ?? false
                altaDialog = head["dialog"] as? Bool ?? false
                altaHasCheck = head["hasCheck"] as? Bool ?? false
                altaCanNext = head["next"] as? Bool ?? false
                altaHasNext = head["nextAny"] as? Bool ?? false
                altaShowingLesson = head["showingLesson"] as? Bool ?? false
                // (1.3.31) on from a lesson: the question comes under it on the same page, with no new question from Alta's
                // answers — the popup goes on with it as soon as it is there
                if phase == .moving && (head["found"] as? Bool ?? false) && (head["hasCheck"] as? Bool ?? false) {
                    watchdog?.cancel()
                    phase = .answering
                }
                let raw = head["raw"] as? Bool ?? false
                if raw && !altaRaw {
                    rawGaveUp = false
                    Task { [weak self] in
                        try? await Task.sleep(nanoseconds: 10_000_000_000)
                        guard let self, self.altaRaw else { return }
                        withAnimation(Motion.gentle) { self.rawGaveUp = true }
                    }
                }
                altaRaw = raw
            }
        case "feedback":
            let timedOut = head["timedOut"] as? Bool ?? false
            // (1.3.21: also when Alta's own Check was pressed, on a question shown as Alta's)
            guard phase == .checking || (phase == .answering && !timedOut) else { return }
            watchdog?.cancel()
            let v = head["verdict"] as? String
            withAnimation(Motion.gentle) {
                verdict = AltaVerdict(correct: v == "correct" ? true : (v == "incorrect" ? false : nil),
                                      text: (head["text"] as? String) ?? "", canContinue: head["next"] as? Bool ?? false)
                phase = .feedback
                if timedOut { wholePage = true; showPage = true } // (Alta said nothing the popup could read: its page, as it is)
            }
        case "meter":
            // (1.3.21) Alta's page's own mastery bars: the whole's, and each objective's by its name
            let percent = head["percent"] as? Double
            var found: [AltaReport.Target] = []
            let named = objectives
            for b in head["bars"] as? [[String: Any]] ?? [] {
                guard let label = b["label"] as? String, let value = b["value"] as? Double, !label.isEmpty else { continue }
                if let o = named.first(where: { o in
                    let n = o.name.lowercased()
                    return !o.name.hasPrefix("Objective ") && n.count > 3 && (label.contains(n) || n.contains(label))
                }) {
                    found.append(AltaReport.Target(id: o.id, progress: value, status: nil))
                }
            }
            withAnimation(Motion.gentle) { hear(percent: percent.map { $0 / 100 }, targets: found) }
        default:
            break
        }
    }

    /// (1.3.23) The assignment's objectives as its overview lists them — names and Alta's estimates — kept for this
    /// assignment, so a popup opened straight onto a question still has them.
    @Published private(set) var known: [AltaOverview.Objective] = []
    private var knownKey: String { "SimplAltaObjectives.\(launch.course).\(launch.assignment)" }

    private func keep(_ list: [AltaOverview.Objective]) {
        guard !list.isEmpty, list.count >= known.count, list != known else { return }
        known = list
        if let data = try? JSONEncoder().encode(list) { UserDefaults.standard.set(data, forKey: knownKey) }
    }

    private static func same(_ a: String?, _ b: String?) -> Bool {
        guard let a, let b else { return false }
        let x = a.trimmingCharacters(in: .whitespacesAndNewlines).lowercased(), y = b.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        return !x.isEmpty && x == y
    }

    /// (1.3.23) Alta's estimate of the questions the objective being worked on takes, as its overview says it ("Estimated
    /// 4 - 9 questions"), else as the question's answer has it.
    var estimate: (low: Double, high: Double)? {
        guard let c = report?.current else { return nil }
        if let o = known.first(where: { $0.id == c.id || Self.same($0.name, c.name) }), let l = o.low, let h = o.high, h > 0 { return (l, h) }
        if let l = c.low, let h = c.high, h > 0 { return (l, h) }
        return nil
    }

    /// A newer overview kept over the last, its blanks filled from the one before.
    private func merge(_ old: AltaOverview?, _ new: AltaOverview) -> AltaOverview {
        guard let old else { return new }
        return AltaOverview(name: new.name ?? old.name, due: new.due ?? old.due, dueText: new.dueText ?? old.dueText, statusText: new.statusText ?? old.statusText, threshold: new.threshold ?? old.threshold,
                            percent: new.percent ?? old.percent, progress: new.progress ?? old.progress, targets: new.targets ?? old.targets,
                            status: new.status ?? old.status, started: new.started ?? old.started,
                            completed: new.completed ?? old.completed, objectives: new.objectives.isEmpty ? old.objectives : new.objectives)
    }

    /// (1.3.21) Mastery heard: the newest word on it kept, whichever of Alta's answers or bars it came in (as a fraction,
    /// or as Alta's percent).
    private func hear(percent: Double?, targets: [AltaReport.Target]) {
        let unit = { (p: Double) in min(max(p > 1 ? p / 100 : p, 0), 1) }
        if let p = percent { livePercent = unit(p) }
        for t in targets {
            guard let id = t.id, !id.isEmpty else { continue }
            liveTargets[id] = AltaReport.Target(id: id, progress: t.progress.map(unit), status: t.status ?? liveTargets[id]?.status)
        }
    }

    // MARK: Alta's page in the popup

    /// Alta's whole page is on screen (Alta's Page), rather than the question.
    var seesWholePage: Bool { showPage && wholePage }

    /// (1.3.23) The question is answered in Alta's own answer box, shown alone in the popup's colours.
    var altaDrawn: Bool { showPage && !wholePage }

    /// Alta's answer box not found in a while (a page the popup cannot pick the question out of): the popup draws the
    /// question itself, as before, when it is a kind it can draw.
    private func fallBack(_ key: String) {
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 6_000_000_000)
            guard let self, self.questionKey == key, !self.altaFound, self.altaDrawn, self.phase == .answering else { return }
            withAnimation(Motion.gentle) { self.drawsItself = true; self.showPage = false }
        }
    }

    /// (1.3.23) The popup's colours for Alta's question: light or dark as the app is, with its accent.
    func appearance(dark: Bool) {
        // (the app's own colours as they are drawn now, light or dark: its accent, and its page — the ground of Alta's
        // pop-ups)
        func hex(_ c: NSColor) -> String {
            var out = "#000000"
            NSAppearance(named: dark ? .darkAqua : .aqua)?.performAsCurrentDrawingAppearance {
                let s = c.usingColorSpace(.sRGB) ?? .black
                out = String(format: "#%02X%02X%02X", Int((s.redComponent * 255).rounded()), Int((s.greenComponent * 255).rounded()), Int((s.blueComponent * 255).rounded()))
            }
            return out
        }
        let next = AltaTheme.css(dark: dark, accent: hex(AppearanceStore.accentNow ?? .controlAccentColor), ground: hex(NSColor(Theme.page)))
        guard next != css else { return }
        css = next
        if altaDrawn { refocus() }
    }

    /// Alta's Page, and back to the question: as the popup draws it, or as Alta's own, shown alone.
    func togglePage() {
        if seesWholePage {
            wholePage = false
            showPage = !drawsItself && phase != .start && phase != .loading
        } else {
            wholePage = true
            showPage = true
        }
    }

    /// (1.3.21) Alta's own question shown alone in Alta's page, or the page as it is: asked of each page of the hidden
    /// view. Where the question is in a page within the page, the outer page keeps only that inner page.
    private func refocus() {
        let on = showPage && !wholePage
        Task { [weak self] in await self?.applyFocus(on) }
    }

    private func applyFocus(_ on: Bool) async {
        let js = "return window.__simplAlta ? window.__simplAlta.focus(o) : null"
        var mainFound = false, innerFound = false
        var missed: [WKFrameInfo?] = []
        for f in frames + [nil] {
            let o: [String: Any] = ["on": on, "iframe": false, "hint": question?.hint ?? "", "css": css, "bar": true]
            let r = await run(js, ["o": o], exactly: f) as? [String: Any]
            let found = r?["found"] as? Bool ?? false
            if f?.isMainFrame ?? true { mainFound = mainFound || found } else { innerFound = innerFound || found }
            if !found { missed.append(f) }
        }
        guard on, innerFound, !mainFound else { return }
        for f in missed {
            let o: [String: Any] = ["on": true, "iframe": true, "hint": question?.hint ?? "", "css": css, "bar": true]
            _ = await run(js, ["o": o], exactly: f)
        }
    }

    /// (1.3.21) Alta's whole page as one picture, top to bottom, past what fits on screen: scrolled through a screenful
    /// at a time and put together — saved to Downloads and copied, to send at once. Alta's page is shown whole while it
    /// is taken, then put back as it was.
    func fullPageShot() async -> URL? {
        let wasShown = showPage, wasWhole = wholePage
        withAnimation(Motion.gentle) { wholePage = true; showPage = true }
        await applyFocus(false)
        defer {
            withAnimation(Motion.gentle) { wholePage = wasWhole; showPage = wasShown }
            refocus()
        }
        try? await Task.sleep(nanoseconds: 400_000_000)
        let measure = """
        var se = document.scrollingElement || document.documentElement, best = null, area = 0;
        if (se.scrollHeight > innerHeight + 8) best = se;
        else [].forEach.call(document.querySelectorAll('*'), function (el) {
          if (el.scrollHeight <= el.clientHeight + 8 || el.clientHeight < 120) return;
          if (!/(auto|scroll|overlay)/.test(getComputedStyle(el).overflowY)) return;
          var r = el.getBoundingClientRect(); if (r.width * r.height > area) { area = r.width * r.height; best = el; }
        });
        window.__simplShot = { el: best, was: best ? best.scrollTop : 0, fixed: [] };
        if (!best) return { total: 0, vh: innerHeight };
        var r = best === se ? { top: 0 } : best.getBoundingClientRect();
        return { total: best.scrollHeight, view: best === se ? innerHeight : best.clientHeight, top: Math.max(0, r.top), vh: innerHeight };
        """
        // (from the second screenful on, what stays put on screen — a header, a button — is hidden, so it shows once)
        let scroll = """
        var s = window.__simplShot; if (!s || !s.el) return 0;
        if (y > 0 && !s.fixed.length) [].forEach.call(document.querySelectorAll('*'), function (el) {
          var p = getComputedStyle(el).position;
          if (p === 'fixed' || p === 'sticky') { s.fixed.push([el, el.style.visibility]); el.style.visibility = 'hidden'; }
        });
        s.el.scrollTop = y; return s.el.scrollTop;
        """
        let restore = """
        var s = window.__simplShot; if (!s || !s.el) return 0;
        s.fixed.forEach(function (f) { f[0].style.visibility = f[1]; }); s.el.scrollTop = s.was; window.__simplShot = null; return 1;
        """
        guard let m = await run(measure, [:], exactly: nil) as? [String: Any] else { return nil }
        let vh = (m["vh"] as? Double) ?? Double(web.bounds.height)
        let total = (m["total"] as? Double) ?? 0
        let view = (m["view"] as? Double) ?? vh
        let top = (m["top"] as? Double) ?? 0
        var tiles: [(offset: Double, image: CGImage)] = []
        if total > view + 1 {
            var y = 0.0
            for _ in 0..<60 {
                let at = (await run(scroll, ["y": y], exactly: nil) as? Double) ?? y
                if let last = tiles.last, at <= last.offset { break }
                try? await Task.sleep(nanoseconds: 350_000_000)
                guard let image = await snapshot() else { break }
                tiles.append((at, image))
                if at + view >= total - 1 { break }
                y = at + view
            }
            _ = await run(restore, [:], exactly: nil)
        } else if let image = await snapshot() {
            tiles = [(0, image)]
        }
        guard let first = tiles.first?.image else { return nil }
        let scale = Double(first.width) / max(Double(web.bounds.width), 1)
        let px = { (v: Double) in Int((v * scale).rounded()) }
        // the first screenful to the foot of the scrolling part; each next screenful's new rows under it; the foot of
        // the last screenful below the scrolling part
        let foot = min(top + view, vh)
        let span = (tiles.last?.offset ?? 0) - (tiles.first?.offset ?? 0)
        let height = px(vh + span)
        guard let ctx = CGContext(data: nil, width: first.width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        ctx.setFillColor(CGColor(gray: 1, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: first.width, height: height))
        func place(_ image: CGImage, from y0: Double, to y1: Double, at yTop: Double) {
            let crop = CGRect(x: 0, y: px(y0), width: image.width, height: min(px(y1) - px(y0), image.height - px(y0)))
            guard crop.height > 0, let part = image.cropping(to: crop) else { return }
            ctx.draw(part, in: CGRect(x: 0, y: height - px(yTop) - part.height, width: part.width, height: part.height))
        }
        place(first, from: 0, to: foot, at: 0)
        for k in tiles.indices.dropFirst() {
            let d = tiles[k].offset - tiles[k - 1].offset
            place(tiles[k].image, from: foot - d, to: foot, at: foot + tiles[k - 1].offset - tiles[0].offset)
        }
        if let last = tiles.last { place(last.image, from: foot, to: vh, at: foot + span) }
        guard let whole = ctx.makeImage() else { return nil }
        let rep = NSBitmapImageRep(cgImage: whole)
        guard let png = rep.representation(using: .png, properties: [:]) else { return nil }
        let stamp: String = {
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.dateFormat = "yyyy-MM-dd 'at' HH.mm.ss"
            return f.string(from: Date())
        }()
        let downloads = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads", isDirectory: true)
        let file = downloads.appendingPathComponent("Simpl Alta Page \(stamp).png")
        do { try png.write(to: file) } catch { return nil }
        NSPasteboard.general.clearContents()
        let picture = NSImage(cgImage: whole, size: NSSize(width: Double(whole.width) / scale, height: Double(whole.height) / scale))
        NSPasteboard.general.writeObjects([file as NSURL, picture] as [NSPasteboardWriting])
        return file
    }

    /// What the hidden view shows now, at the screen's own sharpness.
    private func snapshot() async -> CGImage? {
        await withCheckedContinuation { (go: CheckedContinuation<CGImage?, Never>) in
            let config = WKSnapshotConfiguration()
            config.afterScreenUpdates = true
            web.takeSnapshot(with: config) { image, _ in
                go.resume(returning: image?.cgImage(forProposedRect: nil, context: nil, hints: nil))
            }
        }
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
                    withAnimation(Motion.gentle) { self.overview = self.merge(self.overview, o); self.keep(o.objectives) }
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
        var head: [String: Any] = [:]
        head["app"] = (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "?"
        head["phase"] = "\(phase)"
        head["showPage"] = showPage
        head["wholePage"] = wholePage
        head["report"] = report != nil
        head["overviewObjectives"] = overview?.objectives.count ?? 0
        head["viewAt"] = (web.url.map { ($0.host ?? "") + $0.path }) ?? ""
        // (1.3.22) what kind of question it is and how the popup took it, and the rail — never its answer
        if let q = question {
            var d: [String: Any] = [:]
            d["type"] = q.type ?? ""
            d["custom"] = q.custom ?? ""
            d["purpose"] = q.purpose ?? ""
            d["dataType"] = q.dataType ?? ""
            d["drawnAs"] = "\(q.kind)"
            d["blanks"] = q.blankCount
            d["options"] = q.options.count
            d["choices"] = q.choices.count
            d["template"] = q.template != nil
            d["promptLength"] = (q.prompt ?? "").count
            d["hintFound"] = !q.hint.isEmpty
            head["question"] = d
        }
        var rail: [[String: Any]] = []
        for o in objectives { rail.append(["named": !o.name.hasPrefix("Objective "), "mastery": o.mastery]) }
        head["objectives"] = rail
        head["mastery"] = mastery
        var out: [[String: Any]] = [head]
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
        if altaDrawn { return phase == .answering && altaFound && altaCanCheck }
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
        guard canCheck else { return }
        // (1.3.23) answered in Alta's own box: only its Check to press
        if altaDrawn {
            phase = .checking
            Task {
                let pressed = await call("return window.__simplAlta ? window.__simplAlta.check() : null", [:])
                guard (pressed as? [String: Any])?["ok"] as? Bool == true else { return fail("Alta's Check button could not be found.") }
                watch(seconds: 15)
            }
            return
        }
        guard let q = question else { return }
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

    /// (1.3.35) Done with a lesson alone on the page: its Continue pressed; the question comes under it.
    func lessonContinue() {
        Task {
            if altaCanNext { _ = await call("return window.__simplAlta ? window.__simplAlta.next() : null", [:]) }
            refocus()
        }
    }

    /// (1.3.25) Alta's More Instruction pressed: what it teaches comes as Alta's page shows it.
    func instruct() {
        Task {
            let pressed = await call("return window.__simplAlta ? window.__simplAlta.instruct() : null", [:])
            guard (pressed as? [String: Any])?["ok"] as? Bool == true else { return fail("Alta's More Instruction button could not be found.") }
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
            wholePage = true
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
                AltaObjective(id: o.id, number: k + 1, name: o.name, mastery: done ? 1 : Self.level(liveTargets[o.id]), current: false)
            }
        }
        var ids: [String] = r.objectives.compactMap(\.id)
        if ids.isEmpty { ids = r.targets.compactMap(\.id) }
        if ids.isEmpty, let c = r.current.id { ids = [c] }
        var seen = Set<String>()
        ids = ids.filter { seen.insert($0).inserted }
        let here = r.current.id.flatMap { ids.firstIndex(of: $0) }
        let there = known.firstIndex { Self.same($0.name, r.current.name) }
        let aligned = known.count == ids.count || (here != nil && here == there)
        return ids.enumerated().map { k, id in
            // (1.3.21) the newest word on it, from any of Alta's answers or its page's bars, over the question's
            let p = Self.level(liveTargets[id] ?? r.targets.first { $0.id == id })
            // (1.3.20) a name from the overview by its place when Alta's answers name only the objective on screen —
            // (1.3.23) the overview's list (kept for the assignment) lined up by the objective on screen when the two
            // lists differ in length
            let byPlace = aligned && known.indices.contains(k) ? known[k].name : nil
            let byId = known.first { $0.id == id }?.name
            let named = r.objectives.first { $0.id == id }?.name ?? (r.current.id == id ? r.current.name : nil) ?? names[id] ?? byId ?? byPlace
            return AltaObjective(id: id, number: k + 1, name: named ?? "Objective \(k + 1)", mastery: p, current: r.current.id == id)
        }
    }

    /// An objective's mastery, 0…1: its progress, or full once its status says it is mastered.
    private static func level(_ t: AltaReport.Target?) -> Double {
        if ["complete", "completed", "mastered", "done"].contains((t?.status ?? "").lowercased()) { return 1 }
        return min(max(t?.progress ?? 0, 0), 1)
    }

    /// (1.3.28) Alta's page shows a lesson: the bar offers Continue. (1.3.29: by what can be pressed — Continue, and no
    /// Check that can be — whatever else is in the page.)
    var altaLesson: Bool { altaDrawn && altaHasNext && !altaCanCheck }

    /// (1.3.29) The maths being set out: Alta's page covered meanwhile.
    var typesetting: Bool { altaDrawn && altaRaw && !rawGaveUp }

    /// The whole assignment's mastery, 0…1.
    var mastery: Double {
        guard let r = report else {
            if overview?.completed == true { return 1 }
            if let p = livePercent { return p }
            return min(max((overview?.percent ?? 0) / 100, 0), 1)
        }
        if r.completed || (r.status ?? "").lowercased() == "complete" { return 1 }
        if let p = livePercent { return p }
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

/// (1.3.23) Alta's question in the popup's own colours: its answer box (Learnosity's own — its maths field, its choices,
/// its blanks, its graph) kept as Alta draws it and works it, recoloured light or dark as the app is, with the app's
/// accent and type, on the popup's own ground. Only colours, borders and type are changed, never what Alta's page does.
enum AltaTheme {
    static func css(dark: Bool, accent: String, ground: String) -> String {
        let text = dark ? "#F2F2F7" : "#1D1D1F"
        let dim = dark ? "rgba(235,235,245,0.62)" : "rgba(60,60,67,0.62)"
        let field = dark ? "rgba(255,255,255,0.07)" : "#FFFFFF"
        let card = dark ? "rgba(255,255,255,0.05)" : "rgba(0,0,0,0.035)"
        let line = dark ? "rgba(255,255,255,0.16)" : "rgba(0,0,0,0.14)"
        let good = dark ? "#30D158" : "#248A3D"
        let bad = dark ? "#FF453A" : "#D70015"
        // (1.3.24) Alta's maths keypad is left as Alta draws it — only turned dark, in dark mode
        let pad = ":not([class*=\"keyboard\" i]):not([class*=\"keyboard\" i] *):not([class*=\"keypad\" i]):not([class*=\"keypad\" i] *)"
            // (1.3.36) and a video's player — its poster, its play button, its controls — as the player draws it
            + ":not([class*=\"video\" i]):not([class*=\"video\" i] *):not([class*=\"player\" i]):not([class*=\"player\" i] *):not([class*=\"kaltura\" i]):not([class*=\"kaltura\" i] *):not([class*=\"vjs\" i]):not([class*=\"vjs\" i] *)"
        // (everything but a graph, a picture or a video takes the popup's ground, lines and type; maths keeps its own type
        // — in :where(), weightless, so the answer box's own rules below win)
        let others = ":not(.dcg-container):not(.dcg-container *):not(svg):not(svg *):not(img):not(canvas):not(video):not(iframe)" + pad
        // (1.3.29) typeset maths is left wholly as its typesetter sets it — MathJax 2 (its MathJax_CHTML, mjx-chtml, mjx-char
        // spans) and 3 (mjx-container), KaTeX, MathQuill — its own fonts and rules; it only takes the text's colour
        let maths = ":not(.mq-math-mode):not(.mq-math-mode *):not(.katex):not(.katex *):not(mjx-container):not(mjx-container *):not([class*=\"MathJax\" i]):not([class*=\"MathJax\" i] *):not([class*=\"mjx\" i]):not([class*=\"mjx\" i] *):not([id*=\"MathJax\" i]):not([id*=\"MathJax\" i] *)"
        return """
        :root { color-scheme: \(dark ? "dark" : "light"); --s-text: \(text); --s-dim: \(dim); --s-field: \(field); --s-card: \(card); --s-line: \(line); --s-accent: \(accent); --s-accent-soft: color-mix(in srgb, \(accent) 22%, transparent); --s-good: \(good); --s-bad: \(bad); --s-ground: \(ground); }
        html, body { background: transparent !important; color: var(--s-text) !important; }
        body { margin: 0 !important; padding: 12px 28px 120px !important; font: 15px/1.55 -apple-system, BlinkMacSystemFont, "SF Pro Text", "Helvetica Neue", sans-serif !important; -webkit-font-smoothing: antialiased; }
        body :where(*\(others)\(maths)) { background-color: transparent !important; color: inherit !important; border-color: var(--s-line) !important; box-shadow: none !important; text-shadow: none !important; font-family: inherit !important; }
        html body a\(others) { color: var(--s-accent) !important; }
        html body ::selection { background: var(--s-accent-soft) !important; }
        html body input[type=text]\(pad), html body input:not([type])\(pad), html body input[type=number]\(pad), html body textarea:not(.mq-textarea *)\(pad), html body select\(pad), html body .mq-editable-field\(pad), html body [contenteditable="true"]\(pad) {
          background: var(--s-field) !important; color: var(--s-text) !important; border: 1px solid var(--s-line) !important; border-radius: 8px !important; padding: 4px 8px !important; min-height: 30px; outline: none !important; caret-color: var(--s-accent) !important; }
        html body input:not([type=radio]):not([type=checkbox]):focus, html body textarea:not(.mq-textarea *):focus, html body select:focus, html body .mq-editable-field.mq-focused, html body [contenteditable="true"]:focus {
          border-color: var(--s-accent) !important; box-shadow: 0 0 0 3px var(--s-accent-soft) !important; }
        html body .mq-editable-field .mq-cursor { border-left: 1.5px solid var(--s-accent) !important; }
        html body .mq-editable-field .mq-selection, html body .mq-editable-field .mq-selection * { background: var(--s-accent-soft) !important; }
        html body .mq-math-mode .mq-empty { background: var(--s-line) !important; }
        html body input[type=radio], html body input[type=checkbox] { accent-color: var(--s-accent) !important; background: none !important; box-shadow: none !important; }
        html body .lrn-mcq-option, html body .lrn_mcqgroup > li, html body .lrn-mcq-options > li {
          background: var(--s-card) !important; border: 1px solid var(--s-line) !important; border-radius: 12px !important; padding: 10px 14px !important; margin: 8px 0 !important; list-style: none !important; }
        html body .lrn-mcq-option:has(input:checked), html body .lrn_mcqgroup > li:has(input:checked), html body .lrn-mcq-options > li:has(input:checked) {
          border-color: var(--s-accent) !important; background: var(--s-accent-soft) !important; }
        html body button\(others), html body [role="button"]\(others), html body .lrn_btn\(others) {
          background: var(--s-card) !important; color: var(--s-text) !important; border: 1px solid var(--s-line) !important; border-radius: 999px !important; padding: 6px 14px !important; font-weight: 600 !important; }
        html body button\(others):hover, html body [role="button"]\(others):hover { border-color: var(--s-dim) !important; }
        html body .lrn_correct, html body .lrn-correct { box-shadow: 0 0 0 2px var(--s-good) !important; border-radius: 10px !important; background: color-mix(in srgb, var(--s-good) 12%, transparent) !important; }
        html body .lrn_incorrect, html body .lrn-incorrect { box-shadow: 0 0 0 2px var(--s-bad) !important; border-radius: 10px !important; background: color-mix(in srgb, var(--s-bad) 12%, transparent) !important; }
        html, body, html body * { overflow-anchor: none !important; }
        html body [role="dialog"], html body [role="alertdialog"], html body [aria-modal="true"], html body dialog[open] { background: var(--s-ground) !important; border-radius: 14px !important; box-shadow: 0 18px 60px rgba(0,0,0,0.35) !important; }
        \(dark ? "html body [class*=\"keyboard\" i]:not([class*=\"keyboard\" i] *), html body [class*=\"keypad\" i]:not([class*=\"keypad\" i] *) { filter: invert(0.9) hue-rotate(180deg) !important; }" : "")
        html body .dcg-container { border: 1px solid var(--s-line) !important; border-radius: 12px !important; overflow: hidden !important; }
        \(dark ? "html body img { background: #fff !important; border-radius: 6px !important; }" : "")
        """
    }
}

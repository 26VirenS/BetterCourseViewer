import AppKit
import SwiftUI
import WebKit

// (1.3.17) A Knewton Alta assignment opened in the app: Open Tool on an Alta assignment opens a popup (AltaQuiz.swift)
// with the objectives and the total mastery down the left and Alta's own page, as it is, in the rest. (1.3.45) The
// popup only reads Alta: the student works on Alta's page itself; the app never answers, presses or restyles anything.

/// The page's side: a script in every frame of the popup's page that reads a copy of Alta's own answers as its page
/// receives them, and passes the app the assignment, its objectives and their mastery, and the objective on screen —
/// never the question, its answer key (`correct_answer`, `success_condition`, `validation`, `valid_response`) or who the
/// student is (`userId`, `registrationId`, `ltiEnrollment`). It also reads Alta's own mastery bars and the overview's
/// words ("Estimated 4 - 9 questions").
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
      // (1.3.46) this page made known to the app, for Save Page Code (each frame's code is asked of it there)
      post({ kind: 'frame' });
      var num = function (v) { if (v === null || v === undefined || v === '') return null; var n = Number(v); return isFinite(n) ? n : null; };
      var str = function (v) { return typeof v === 'string' && v ? v.slice(0, 600) : (typeof v === 'number' ? String(v) : null); };

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
          }
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
      var looked = function (url, text) {
        if (typeof text === 'string' && text.length > 3000000) return;
        var j; try { j = typeof text === 'string' ? JSON.parse(text) : text; } catch (e) { return; }
        if (!j || typeof j !== 'object') return;
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

      // ---- what Alta's page itself shows: its overview's words, its mastery bars ----
      var norm = function (s) { return String(s || '').replace(/<[^>]+>/g, ' ').replace(/&nbsp;| /g, ' ').replace(/\s+/g, ' ').trim().toLowerCase(); };
      var shown = function (el) { var r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
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
      // (1.3.45) the overview as its words read, when it lists objectives with their estimates: passed when it changes
      var lastScrape = '';
      setInterval(function () {
        var o = scrape();
        if (!o.objectives.length) return;
        var s = JSON.stringify(o);
        if (s !== lastScrape) { lastScrape = s; post(o); }
      }, 1500);

      // ---- (1.3.47) Alta's question alone: the rest of Alta's page hidden ----
      // Alta's learn page as it is built (its saved page code): each part is a `section.compound__compound` in
      // `.learn-flow-body` — a question (a card headed "Question": what it asks, Learnosity's answer box, Alta's
      // Feedback / More Instruction / Submit) or (1.3.48) a lesson (More Instruction: a card headed with the lesson's
      // name, its Feedback / Continue). Those parts are kept — with Alta's pop-ups and MathJax's own helpers, which it
      // measures its fonts by — and everything else (Alta's header, title, mastery, objective card…) is hidden by a style
      // sheet, by marks on the elements kept and their ancestors (nothing is moved or restyled, so typing and scrolling
      // are left alone). Anything not recognised — no part, or not a learn page — gives Alta's page back as it is, after a
      // moment's grace (Alta between parts), and the app is told why, for its Error Logs.
      if (window.top === window) {
        var FOCUS = 'html[data-simpl-q] body *:not([data-simpl-path]):not([data-simpl-keep]):not([data-simpl-keep] *) { display: none !important; }\n'
          + 'html[data-simpl-q] [data-simpl-hide] { display: none !important; }\n'
          + 'html[data-simpl-q] [data-simpl-path]:not(html):not(body) { padding-top: 0 !important; margin-top: 0 !important; }\n'
          + 'html[data-simpl-q] body { padding: 18px 28px 40px !important; }\n'
          // (1.3.53) Alta's own button bars put away — the app shows their buttons as its own — and a gap between a
          // question's words and its answer, for the two glass panels behind them
          + 'html[data-simpl-q] [data-simpl-bar] { display: none !important; }\n'
          + 'html[data-simpl-q] [data-simpl-answer] { margin-top: 48px !important; }\n'
          + 'html[data-simpl-q] [data-simpl-thin] { height: 0 !important; min-height: 0 !important; max-height: 0 !important; padding: 0 !important; margin: 0 !important; border: 0 !important; overflow: hidden !important; }\n'
          // (1.3.49) laid on the popup's Liquid Glass: the page, the parts round the question and the question's own card
          // draw no ground, shadow or outline of their own — the glass behind the page is the card. Everything in it
          // (what it asks, its maths, Learnosity's answer box and keypad, Alta's buttons) is drawn as Alta draws it.
          + 'html[data-simpl-q], html[data-simpl-q] body, html[data-simpl-q] [data-simpl-path] { background: transparent !important; box-shadow: none !important; }\n'
          + 'html[data-simpl-q] section[data-simpl-keep], html[data-simpl-q] section[data-simpl-keep] > [class*="card"], html[data-simpl-q] section[data-simpl-keep] > [class*="card"]::before, html[data-simpl-q] section[data-simpl-keep] > [class*="card"]::after { background: transparent !important; box-shadow: none !important; border-color: transparent !important; }\n'
          // (1.3.50) in dark mode, on dark glass: each part's colours turned over — dark words on light become light on
          // dark, hues kept (inverted, then turned half round the colour wheel) — and its pictures, videos and graphs
          // turned back, so they keep their own colours. Alta still draws it all; only its colours are turned.
          + 'html[data-simpl-q][data-simpl-dark] section[data-simpl-keep] { filter: invert(1) hue-rotate(180deg) !important; }\n'
          + 'html[data-simpl-q][data-simpl-dark] section[data-simpl-keep] :is(img, picture, video, canvas, iframe, object, embed) { filter: invert(1) hue-rotate(180deg) !important; }\n';
        var txt = function (el) { return String((el && (el.innerText || el.textContent)) || '').replace(/\s+/g, ' ').trim(); };
        var all = function (sel) { return [].slice.call(document.querySelectorAll(sel)); };
        var title = function (sec) { var h = sec.querySelector('[class*="compound-header"] h2, [class*="compound-header"] h3, h2[class*="content-title"]'); return h ? txt(h) : ''; };
        var START = /^(start|start assignment|continue|continue assignment|resume|keep going|practice|keep practicing|start practicing)$/;
        // (read from the page as Alta shows it: the marks lifted for the moment it is read — no paint comes between —
        // so a part the focus itself hides is still seen)
        var read = function () {
          var on = document.documentElement.hasAttribute('data-simpl-q');
          if (on) document.documentElement.removeAttribute('data-simpl-q');
          try { return readNow(); } finally { if (on) document.documentElement.setAttribute('data-simpl-q', ''); }
        };
        var readNow = function () {
          var flows = all('[class*="learn-flow-body"]'); // (there, even empty: Alta's learn page with no part)
          var parts = all('section[class*="compound__compound"]').filter(shown);
          if (!flows.length && !parts.length) {
            var overview = /estimated\s+\d+\s*[-–]\s*\d+\s+questions?/i.test(txt(document.body))
              || all('button, a, [role="button"]').some(function (b) { return shown(b) && START.test(norm(b.innerText || b.textContent)); });
            return { state: overview ? 'overview' : 'none', reason: 'nothing on the page is a part of Alta\'s learn page' };
          }
          var titles = parts.map(title);
          // (1.3.48) every part of the learn page is kept — a question, or a lesson (More Instruction: a part headed with
          // the lesson's name, its Continue), or a lesson above the question — Alta's header and the rest hidden
          var qs = parts.filter(function (p, i) { return /^question$/i.test(titles[i]) && p.querySelector('.lrn_widget, .learnosity-item, .lrn_qr, [class*="learnosity__"]'); });
          if (parts.length) return { state: qs.length ? 'question' : 'lesson', keep: parts, titles: titles };
          return { state: 'unknown', titles: titles, reason: 'Alta\'s learn page shows no part' };
        };
        var lab = function (el) { var c = typeof el.className === 'string' ? el.className.trim().split(/\s+/).slice(0, 3).join('.') : ''; return el.tagName.toLowerCase() + (el.id ? '#' + el.id.slice(0, 40) : '') + (c ? '.' + c.slice(0, 90) : ''); };
        // (what the page holds, for the Error Logs: its parts' titles and makeup, its buttons' words — never an answer)
        var shape = function (r) {
          var flow = all('[class*="learn-flow-body"]')[0], main = document.querySelector('main');
          return {
            path: location.pathname, titles: r.titles || [],
            flow: flow ? [].slice.call(flow.children).slice(0, 14).map(function (k) { return lab(k) + (shown(k) ? '' : ' (unseen)'); }) : null,
            main: !flow && main ? [].slice.call(main.querySelectorAll('*')).filter(function (e) { return e.children.length > 1 && shown(e); }).slice(0, 12).map(lab) : null,
            buttons: all('button, [role="button"], a[href]').filter(shown).map(function (b) { return norm(b.innerText || b.textContent || b.getAttribute('aria-label')).slice(0, 40); }).filter(Boolean).slice(0, 30),
            dialogs: all('[role="dialog"], [role="alertdialog"], .MuiModal-root').filter(shown).length
          };
        };
        var marked = [], keptNow = [];
        var unmark = function () {
          marked.forEach(function (m) { m[0].removeAttribute(m[1]); });
          marked = []; keptNow = [];
          document.documentElement.removeAttribute('data-simpl-q');
        };
        var mark = function (el, a) { if (!el.hasAttribute(a)) { el.setAttribute(a, ''); marked.push([el, a]); } };
        var sprite = function (e) { return e.tagName && e.tagName.toLowerCase() === 'svg' && !!e.querySelector('symbol, defs') && !e.closest('section'); };
        var apply = function (keep) {
          if (keep.length === keptNow.length && keep.every(function (k, i) { return k === keptNow[i] && k.isConnected; }) && document.documentElement.hasAttribute('data-simpl-q')) return;
          unmark();
          keep.forEach(function (k) {
            mark(k, 'data-simpl-keep');
            for (var x = k.parentElement; x && x !== document.documentElement; x = x.parentElement) mark(x, 'data-simpl-path');
          });
          // (1.3.53) a sprite sheet kept only for the symbols it holds, and the parts round it that hold nothing else kept
          // (Alta's banner), drawn but taking no room
          var real = new Set();
          keep.forEach(function (k) { if (!sprite(k)) for (var x = k; x; x = x.parentElement) real.add(x); });
          keep.forEach(function (k) { if (sprite(k)) for (var x = k; x && !real.has(x); x = x.parentElement) mark(x, 'data-simpl-thin'); });
          keptNow = keep;
          gen++;
          var st = document.getElementById('simpl-focus');
          if (!st) { st = document.createElement('style'); st.id = 'simpl-focus'; (document.head || document.documentElement).appendChild(st); }
          if (st.textContent !== FOCUS) st.textContent = FOCUS;
          document.documentElement.setAttribute('data-simpl-q', '');
          dark();
        };
        // (1.3.50) light or dark as the app is: the app sets window.__simplDark (before the page, and again when it changes)
        var dark = function () { if (document.documentElement) document.documentElement.toggleAttribute('data-simpl-dark', !!window.__simplDark); };
        window.__simplFocusDark = dark;
        // ---- (1.3.53) the parts' panels and their buttons, for the app ----
        // Each part kept is a card: what a question asks (its heading and words) and its answer (Learnosity's box and
        // what follows) are told to the app as two panels — a lesson as one — in the page's own coordinates, with the
        // page's height, so the app lays a glass panel behind each and scrolls the page itself. And every button in the
        // parts' button bars, whatever it says, is told to the app — its words, whether Alta makes it the main one,
        // whether it can be pressed — and the bars put away: the app shows them as its own and presses Alta's.
        var gen = 0, lastPanels = '', lastButtons = '', buttonEls = [];
        var cards = function () { return keptNow.filter(function (k) { return k.matches && k.matches('section[class*="compound__compound"]'); }); };
        var cardOf = function (sec) { return sec.querySelector('[class*="content-card"], [class*="theme__card"]') || sec; };
        var visible = function (e) { var cs = getComputedStyle(e); return cs.display !== 'none' && cs.visibility !== 'hidden' && !e.hidden; };
        var bars = function () {
          var out = [];
          cards().forEach(function (sec) {
            [].forEach.call(sec.querySelectorAll('[class*="button-bar"]'), function (bar) {
              mark(bar, 'data-simpl-bar');
              [].forEach.call(bar.querySelectorAll('button, [role="button"], a[href]'), function (b) {
                var label = String(b.textContent || b.getAttribute('aria-label') || '').replace(/\s+/g, ' ').trim();
                if (!label || !visible(b) || /^feedback$/i.test(label)) return;
                out.push({ el: b, label: label.slice(0, 40), primary: /primary/i.test(String(b.className || '')),
                  enabled: !b.disabled && b.getAttribute('aria-disabled') !== 'true' });
              });
            });
          });
          return out;
        };
        var box = function (els) {
          var r = null;
          els.forEach(function (e) {
            var b = e.getBoundingClientRect();
            if (b.width === 0 && b.height === 0) return;
            r = r ? { l: Math.min(r.l, b.left), t: Math.min(r.t, b.top), r: Math.max(r.r, b.right), b: Math.max(r.b, b.bottom) } : { l: b.left, t: b.top, r: b.right, b: b.bottom };
          });
          return r;
        };
        var panels = function () {
          var out = [];
          cards().forEach(function (sec) {
            var kids = [].slice.call(cardOf(sec).children).filter(function (k) { return !k.hasAttribute('data-simpl-bar') && !k.hasAttribute('data-simpl-hide') && visible(k); });
            var at = -1;
            for (var i = 0; i < kids.length; i++) { if (kids[i].querySelector('.lrn_widget, .lrn_qr, .learnosity-item, [class*="learnosity"]')) { at = i; break; } }
            if (at > 0) {
              mark(kids[at], 'data-simpl-answer');
              var q = box(kids.slice(0, at)), a = box(kids.slice(at));
              if (q) out.push(['question', q]);
              if (a) out.push(['answer', a]);
            } else {
              var w = box(kids);
              if (w) out.push([at === 0 ? 'answer' : 'lesson', w]);
            }
          });
          var sx = window.scrollX || 0, sy = window.scrollY || 0;
          return out.map(function (p) {
            return { kind: p[0], x: Math.round(p[1].l + sx), y: Math.round(p[1].t + sy), w: Math.round(p[1].r - p[1].l), h: Math.round(p[1].b - p[1].t) };
          });
        };
        var publish = function () {
          var on = document.documentElement.hasAttribute('data-simpl-q');
          var bs = on ? bars() : [];
          buttonEls = bs.map(function (b) { return b.el; });
          var bsSaid = JSON.stringify(bs.map(function (b) { return { label: b.label, primary: b.primary, enabled: b.enabled }; }));
          if (bsSaid !== lastButtons) { lastButtons = bsSaid; post({ kind: 'buttons', items: JSON.parse(bsSaid) }); }
          var ps = on ? panels() : [];
          var bottom = ps.reduce(function (m, p) { return Math.max(m, p.y + p.h); }, 0);
          var psSaid = JSON.stringify({ panels: ps, height: ps.length ? bottom + 48 : 0, gen: gen });
          if (psSaid !== lastPanels) { lastPanels = psSaid; var m = JSON.parse(psSaid); m.kind = 'panels'; post(m); }
        };
        setInterval(publish, 350);
        window.addEventListener('resize', function () { publish(); });
        window.__simplButtons = {
          press: function (i) { var b = buttonEls[i]; if (!b || b.disabled || b.getAttribute('aria-disabled') === 'true') return false; b.click(); setTimeout(publish, 60); return true; }
        };
        var told = '', badSince = 0, timer = null, wasQuestion = false;
        var report = function (state, r) {
          if (state === told) return;
          told = state;
          post({ kind: 'focus', state: state, reason: (r && r.reason) || '', shape: /^(question|lesson|overview)$/.test(state) ? null : shape(r || {}) });
        };
        // ---- (1.3.51) Alta's pop-ups as the Mac's own: each one open passed to the app — its title, words and buttons,
        // and where it is, for a picture of it as Alta draws it — and hidden in the page while the app shows it; the
        // app's buttons press Alta's ----
        var dialogSig = '', dialogEl = null;
        var dialogNow = function () {
          var ds = all('[role="dialog"], [role="alertdialog"], dialog[open]').filter(function (d) {
            return shown(d) && (txt(d).length > 0 || d.querySelector('button, svg, img'));
          });
          return ds[ds.length - 1] || null;
        };
        var dialogButtons = function (d) {
          return [].slice.call(d.querySelectorAll('button, [role="button"], a[href]')).filter(shown).map(function (b) {
            return { el: b, label: String(b.innerText || b.textContent || b.getAttribute('aria-label') || '').replace(/\s+/g, ' ').trim() };
          }).filter(function (x) { return x.label; });
        };
        var DIALOG_CSS = 'html[data-simpl-native-dialog] [data-simpl-dialog] { visibility: hidden !important; }\n'
          // (1.3.54) its picture taken without its buttons — the app's sheet has them as its own
          + 'html[data-simpl-dialog-shot] [data-simpl-dialog] :is(button, [role="button"]) { visibility: hidden !important; }\n';
        var dialogStyle = function () {
          var st = document.getElementById('simpl-dialog');
          if (!st) { st = document.createElement('style'); st.id = 'simpl-dialog'; (document.head || document.documentElement).appendChild(st); }
          if (st.textContent !== DIALOG_CSS) st.textContent = DIALOG_CSS;
        };
        var dialogTick = function () {
          var d = dialogNow();
          if (!d) {
            if (dialogSig) {
              dialogSig = ''; dialogEl = null;
              document.documentElement.removeAttribute('data-simpl-native-dialog');
              post({ kind: 'dialog', open: false });
            }
            return;
          }
          var bs = dialogButtons(d);
          var title = '', by = d.getAttribute('aria-labelledby');
          if (by) { var t = document.getElementById(by.split(' ')[0]); if (t) title = txt(t); }
          var sig = txt(d).slice(0, 4000) + '|' + bs.map(function (x) { return x.label; }).join('|') + '|' + d.innerHTML.length;
          if (sig === dialogSig && d === dialogEl) return;
          dialogSig = sig; dialogEl = d;
          dialogStyle();
          var holder = d.closest('.MuiModal-root, [role="presentation"]') || d;
          holder.setAttribute('data-simpl-dialog', '');
          post({ kind: 'dialog', open: true, title: title.slice(0, 200), text: txt(d).slice(0, 2000), buttons: bs.map(function (x) { return x.label.slice(0, 60); }) });
        };
        setInterval(dialogTick, 600);
        window.__simplDialog = {
          // (shown in the page — for its picture, or given back — or hidden while the app shows it)
          show: function (on) { document.documentElement.toggleAttribute('data-simpl-native-dialog', !on); return true; },
          // (its picture: buttons hidden for the moment it is taken, and the row of them at its foot cropped off)
          shot: function (on) { document.documentElement.toggleAttribute('data-simpl-dialog-shot', !!on); return true; },
          rect: function () {
            if (!dialogEl) return null;
            var r = dialogEl.getBoundingClientRect(), bottom = r.bottom;
            var low = dialogButtons(dialogEl).map(function (b) { return b.el.getBoundingClientRect(); })
              .filter(function (b) { return b.height > 0 && b.top > r.top + r.height * 0.5; });
            if (low.length) bottom = Math.min.apply(null, low.map(function (b) { return b.top; })) - 6;
            return { x: r.left, y: r.top, w: r.width, h: Math.max(24, bottom - r.top) };
          },
          press: function (i) { if (!dialogEl) return false; var b = dialogButtons(dialogEl)[i]; if (!b) return false; b.el.click(); return true; }
        };
        var tick = function () {
          timer = null;
          dialogTick();
          var r = read();
          if (r.state === 'question' || r.state === 'lesson') {
            badSince = 0; wasQuestion = true;
            // (Alta's pop-ups and MathJax's helpers kept with it; its Feedback — a report to Alta — and its Content
            // attribution link put away)
            // (1.3.51: and the page's sprite sheets — an <svg> of symbols its pictures draw from, a pop-up's among them)
            var extra = all('[role="dialog"], [role="alertdialog"], body > .MuiModal-root, body > [role="presentation"], #MathJax_Hidden, #MathJax_Font_Test, #MathJax_Message')
              .concat(all('svg').filter(function (g) { return g.querySelector('symbol, defs'); }))
              .filter(function (e) { return !r.keep.some(function (k) { return k.contains(e); }); });
            apply(r.keep.concat(extra));
            r.keep.forEach(function (k) { [].forEach.call(k.querySelectorAll('button'), function (b) { if (/^(feedback|content attribution\b.*)$/.test(norm(b.innerText || b.textContent || b.getAttribute('aria-label')))) mark(b, 'data-simpl-hide'); }); });
            report(r.state, r);
            publish();
            return;
          }
          if (r.state === 'overview') { badSince = 0; wasQuestion = false; unmark(); report('overview', r); publish(); return; }
          // (not recognised: a moment's grace — Alta between one part and the next — then its page as it is)
          if (!badSince) badSince = Date.now();
          var wait = wasQuestion ? 1500 : 8000;
          if (Date.now() - badSince < wait) { schedule(wait - (Date.now() - badSince) + 20); return; }
          unmark();
          report(r.state, r);
          publish();
        };
        var schedule = function (ms) { if (timer) clearTimeout(timer); timer = setTimeout(tick, typeof ms === 'number' ? ms : 120); };
        try { new MutationObserver(function () { schedule(); }).observe(document, { childList: true, subtree: true, characterData: true }); } catch (e) {}
        schedule();
      }

      // (1.3.59) the assignment's cover — Alta's page before it is started: its name, its details (Due Date, Status,
      // Grade), its note, its Start (whatever it is called: Start, Continue, Resume) and Review Instruction, and its
      // objectives by topic, each topic's estimate and each objective's mastery — passed when it changes, for the
      // popup's own start screen; its Start and Review Instruction pressed in Alta's page when the popup's are
      if (window.top === window) {
        var cs = function (v) { return String(v || '').replace(/\s+/g, ' ').trim().slice(0, 300); };
        var vis = function (el) { if (!el) return false; var r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
        var GO = /^(start|start assignment|continue|continue assignment|resume|keep going|keep practicing|start practicing)$/i;
        var startEl = function () {
          var a = document.querySelector('[data-test="cover-header-button"]');
          if (vis(a)) return a;
          return [].slice.call(document.querySelectorAll('a, button, [role="button"]')).filter(function (b) { return vis(b) && GO.test(cs(b.innerText || b.textContent)); })[0] || null;
        };
        var reviewEl = function () {
          var a = document.querySelector('[data-bi="review-instruction-btn"]');
          if (vis(a)) return a;
          return [].slice.call(document.querySelectorAll('a, button')).filter(function (b) { return vis(b) && /^review instruction$/i.test(cs(b.innerText || b.textContent)); })[0] || null;
        };
        var coverRead = function () {
          var head = document.querySelector('[data-test="cover-header"]');
          var go = startEl();
          // (a cover: its header, or a Start beside the objectives' estimates — never Alta's learn page)
          if (document.querySelector('[class*="learn-flow-body"], section[class*="compound__compound"]')) return null;
          if (!head && !(go && /estimated\s+\d+/i.test(cs(document.body && document.body.innerText)))) return null;
          var o = { kind: 'cover', name: '', details: [], note: '', start: go ? cs(go.innerText || go.textContent) : '', review: !!reviewEl(), topics: [] };
          var h1 = (head && head.querySelector('h1')) || document.querySelector('h1');
          o.name = h1 ? cs(h1.innerText || h1.textContent) : '';
          if (head) {
            [].forEach.call(head.querySelectorAll('[class*="subheadingContainer"]'), function (c) {
              var k = c.querySelector('h2, h3'), v = c.querySelector('[class*="headingDetails"]');
              if (k && v) o.details.push({ label: cs(k.innerText || k.textContent), value: cs((v.innerText || v.textContent || '').split('\n').join(' ')) });
            });
            [].forEach.call(head.querySelectorAll('p'), function (q) {
              var t = cs(q.innerText || q.textContent);
              if (!o.note && /credit|complete by|late/i.test(t) && !o.details.some(function (d) { return d.value.indexOf(t) >= 0; })) o.note = t;
            });
          }
          var card = document.querySelector('[data-test="topics-los"]');
          if (card) {
            var topic = null;
            [].forEach.call(card.querySelectorAll('h3, p, [data-test="lo-list-item"]'), function (el) {
              if (el.tagName === 'H3') { topic = { name: cs(el.innerText || el.textContent), estimate: '', objectives: [] }; o.topics.push(topic); return; }
              if (!topic) return;
              if (el.matches('[data-test="lo-list-item"]')) {
                var d = el.querySelector('[class*="description"]') || el.querySelector('p');
                var m = el.querySelector('[data-test="lo-master-indicator"]');
                var pc = m && /(\d{1,3})\s*%/.exec(m.getAttribute('aria-label') || '');
                if (d) topic.objectives.push({ name: cs(d.innerText || d.textContent), mastery: pc ? Math.min(100, +pc[1]) / 100 : null });
                return;
              }
              if (el.closest('[data-test="lo-list-item"]')) return;
              var t = cs(el.innerText || el.textContent);
              var e = /^estimated\s+(\d+)\s*[-\u2013]\s*(\d+)\s+questions?$/i.exec(t);
              if (e) topic.estimate = e[1] + '\u2013' + e[2] + ' questions';
            });
          }
          return o;
        };
        var lastCover = '';
        var coverTick = function () {
          var o = coverRead();
          var s = o ? JSON.stringify(o) : 'none';
          if (s === lastCover) return;
          lastCover = s;
          post(o || { kind: 'cover', none: true });
        };
        setInterval(coverTick, 700);
        window.__simplCover = {
          start: function () { var a = startEl(); if (!a) return false; a.click(); return true; },
          review: function () { var a = reviewEl(); if (!a) return false; a.click(); return true; }
        };
      }
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

    var name: String?
    var threshold: Double?
    var objectives: [Objective]
    var targets: [Target]
    var percent: Double?
    var progress: Double?
    var status: String?
    var completed: Bool
    var due: Double?
    var current: Current
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

/// (1.3.59) The assignment's cover as Alta's page shows it before it is started: its name, its details, its note, what
/// its Start says, whether it offers Review Instruction, and its objectives by topic.
struct AltaCover: Decodable, Equatable {
    struct Detail: Decodable, Equatable, Hashable { let label: String; let value: String }
    struct Objective: Decodable, Equatable, Hashable { let name: String; let mastery: Double? }
    struct Topic: Decodable, Equatable, Hashable { let name: String; let estimate: String; let objectives: [Objective] }
    var name: String
    var details: [Detail]
    var note: String
    var start: String
    var review: Bool
    var topics: [Topic]
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
/// (1.3.53) Alta's page, its scrolling handed to the popup while the popup scrolls it (the page as tall as its question,
/// the glass panels behind its parts scrolling with it).
final class AltaWebView: WKWebView {
    var forwardsScroll = false

    override func scrollWheel(with event: NSEvent) {
        if forwardsScroll, let outer = enclosingScrollView { outer.scrollWheel(with: event) } else { super.scrollWheel(with: event) }
    }
}

/// (1.3.53) A part of Alta's question as the page lays it out (in its own coordinates): what it asks, its answer, or a
/// lesson — a glass panel behind each.
struct AltaPanel: Identifiable, Equatable {
    let id: Int
    let kind: String
    let rect: CGRect
}

/// (1.3.53) One of the buttons in Alta's button bars, whatever it says: shown as the app's own, pressing Alta's.
struct AltaButton: Identifiable, Equatable {
    let id: Int
    let label: String
    let primary: Bool
    let enabled: Bool
}

/// (1.3.51) A pop-up of Alta's, shown as the Mac's own: its title, words and buttons, and a picture of it as Alta draws it.
struct AltaDialog: Identifiable {
    let id = UUID()
    var title: String
    var text: String
    var buttons: [String]
    var picture: NSImage?
}

struct AltaLaunch: Identifiable, Equatable {
    let id = UUID()
    let course: String
    let assignment: String
    let title: String
    var page: String? = nil
}

// MARK: - The session

/// One assignment open in the popup: Alta's page, and what it last said of the objectives and their mastery.
@MainActor
final class AltaSession: ObservableObject {
    @Published private(set) var report: AltaReport?
    /// (1.3.17) The overview Alta opens on: the assignment and its objectives, before a question is asked.
    @Published private(set) var overview: AltaOverview?
    /// Alta's page still opening (the launch through Canvas): a spinner over it meanwhile.
    @Published private(set) var opening = true
    /// The launch could not be had from Canvas.
    @Published private(set) var failure: String?
    /// (1.3.21) The mastery last heard, from wherever Alta said it (a question's answer, a checked answer's, the bars
    /// on its page), 0…1: the whole, and each objective's by its id.
    @Published private(set) var livePercent: Double?
    @Published private(set) var liveTargets: [String: AltaReport.Target] = [:]

    let launch: AltaLaunch
    let web: WKWebView
    private var names: [String: String] = UserDefaults.standard.dictionary(forKey: AltaSession.namesKey) as? [String: String] ?? [:]
    private static let namesKey = "SimplAltaObjectiveNames"
    private let relay: AltaRelay
    private var loading: NSKeyValueObservation?
    /// (1.3.46) Each frame in Alta's page that has run the page script (the page itself is asked as the main frame).
    private var frames: [WKFrameInfo] = []
    /// (1.3.47) How Alta's page is shown: "question" or "lesson" (its parts alone), "overview", or — not recognised —
    /// "unknown" / "none", when it is shown as it is.
    @Published private(set) var focusState = ""
    /// (1.3.50) Light or dark as the app is, told to the page: before each page (a script ahead of the page script) and
    /// at once to the page shown; in dark mode the question's colours are turned over, for dark glass.
    func appearance(dark: Bool) {
        let ucc = web.configuration.userContentController
        ucc.removeAllUserScripts()
        ucc.addUserScript(WKUserScript(source: "window.__simplDark = \(dark);", injectionTime: .atDocumentStart, forMainFrameOnly: true))
        ucc.addUserScript(AltaHook.script)
        web.evaluateJavaScript("window.__simplDark = \(dark); if (window.__simplFocusDark) window.__simplFocusDark();", completionHandler: nil)
    }

    /// (1.3.49) Alta's question (or lesson) is shown alone: laid on Liquid Glass.
    var focused: Bool { focusState == "question" || focusState == "lesson" }
    /// (1.3.53) The parts' panels, the page's height, which question they are of (a new one scrolls back to the top),
    /// and the parts' buttons.
    @Published private(set) var panels: [AltaPanel] = []
    @Published private(set) var pageHeight: CGFloat = 0
    @Published private(set) var panelGen = 0
    @Published private(set) var buttons: [AltaButton] = []

    /// The question laid on glass panels, one behind each part, the popup scrolling the page.
    var split: Bool { focused && !panels.isEmpty && pageHeight > 0 }

    /// (1.3.59) The assignment's cover, while Alta's page shows it: the popup's start screen.
    @Published private(set) var cover: AltaCover?

    /// The cover's Start (or Continue, Resume), pressed in Alta's page.
    func startCover() {
        Task { _ = await run("return window.__simplCover ? window.__simplCover.start() : null", in: nil) }
    }

    /// The cover's Review Instruction, pressed in Alta's page.
    func reviewCover() {
        Task { _ = await run("return window.__simplCover ? window.__simplCover.review() : null", in: nil) }
    }

    func pressButton(_ index: Int) {
        Task { _ = await run("return window.__simplButtons ? window.__simplButtons.press(\(index)) : null", in: nil) }
    }

    /// (1.3.51) Alta's pop-up open, shown as the Mac's own sheet.
    @Published var dialog: AltaDialog?
    private var dialogTask: Task<Void, Never>?

    /// A picture of Alta's pop-up as it draws it, shown in the page for the moment it is taken.
    private func dialogPicture() async -> NSImage? {
        _ = await run("return window.__simplDialog ? (window.__simplDialog.show(true), window.__simplDialog.shot(true)) : null", in: nil)
        try? await Task.sleep(nanoseconds: 80_000_000)
        defer { Task { _ = await run("return window.__simplDialog ? window.__simplDialog.shot(false) : null", in: nil) } }
        guard let r = await run("return window.__simplDialog ? window.__simplDialog.rect() : null", in: nil) as? [String: Any],
              let x = (r["x"] as? NSNumber)?.doubleValue, let y = (r["y"] as? NSNumber)?.doubleValue,
              let w = (r["w"] as? NSNumber)?.doubleValue, let h = (r["h"] as? NSNumber)?.doubleValue, w > 4, h > 4 else { return nil }
        let rect = CGRect(x: x, y: y, width: w, height: h).intersection(web.bounds)
        guard !rect.isEmpty else { return nil }
        return await withCheckedContinuation { (go: CheckedContinuation<NSImage?, Never>) in
            let config = WKSnapshotConfiguration()
            config.rect = rect
            config.afterScreenUpdates = true
            web.takeSnapshot(with: config) { image, _ in go.resume(returning: image) }
        }
    }

    /// One of the pop-up's buttons, pressed in Alta's own.
    func pressDialog(_ index: Int) {
        Task { _ = await run("return window.__simplDialog ? window.__simplDialog.press(\(index)) : null", in: nil) }
    }

    /// The Mac's sheet put away without a button: Alta's pop-up given back to the page.
    func dialogDismissed() {
        dialog = nil
        Task { _ = await run("return window.__simplDialog ? window.__simplDialog.show(true) : null", in: nil) }
    }

    /// (1.3.47) Each time Alta's page was not recognised and shown as it is: when, why, and what the page held.
    @Published private(set) var errorLogs: [String] = []

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
        web = AltaWebView(frame: NSRect(x: 0, y: 0, width: 1000, height: 800), configuration: config)
        web.isInspectable = true
        web.setValue(false, forKey: "drawsBackground") // (1.3.49: the question laid on the popup's glass; see AltaQuiz)
        r.session = self
        loading = web.observe(\.isLoading, options: [.new]) { [weak self] w, _ in
            let done = !w.isLoading && w.url != nil
            Task { @MainActor in if done, self?.opening == true { withAnimation(Motion.gentle) { self?.opening = false } } }
        }
    }

    /// Alta opened: the assignment's launch through Canvas (or the page asked for).
    func start(engine: Engine) async {
        guard web.url == nil else { return }
        if let data = UserDefaults.standard.data(forKey: knownKey), let list = try? JSONDecoder().decode([AltaOverview.Objective].self, from: data) { known = list }
        if let p = launch.page, let u = engine.absolute(p) {
            web.load(URLRequest(url: u))
            return
        }
        struct Answer: Decodable { var url: String }
        do {
            let a = try await engine.call("toolLaunch", ["course": launch.course, "assignment": launch.assignment], as: Answer.self)
            guard let u = URL(string: a.url) else { throw EngineError.unreadable }
            web.load(URLRequest(url: u))
        } catch {
            withAnimation(Motion.gentle) { failure = "Knewton Alta could not be opened. \(error.localizedDescription)"; opening = false }
        }
    }

    // MARK: What the page says

    func take(_ text: String, frame: WKFrameInfo) {
        guard let data = text.data(using: .utf8),
              let head = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }
        switch head["kind"] as? String {
        case "frame":
            guard !frame.isMainFrame else { return }
            let key = frame.request.url?.absoluteString ?? ""
            frames.removeAll { ($0.request.url?.absoluteString ?? "") == key }
            frames.append(frame)
            if frames.count > 30 { frames.removeFirst() }
        case "panels":
            guard frame.isMainFrame else { return }
            let list = (head["panels"] as? [[String: Any]] ?? []).enumerated().compactMap { k, p -> AltaPanel? in
                guard let x = (p["x"] as? NSNumber)?.doubleValue, let y = (p["y"] as? NSNumber)?.doubleValue,
                      let w = (p["w"] as? NSNumber)?.doubleValue, let h = (p["h"] as? NSNumber)?.doubleValue, w > 0, h > 0 else { return nil }
                return AltaPanel(id: k, kind: p["kind"] as? String ?? "", rect: CGRect(x: x, y: y, width: w, height: h))
            }
            let height = CGFloat((head["height"] as? NSNumber)?.doubleValue ?? 0)
            let g = (head["gen"] as? NSNumber)?.intValue ?? 0
            if list != panels { panels = list }
            if height != pageHeight { pageHeight = height }
            if g != panelGen { panelGen = g }
        case "buttons":
            guard frame.isMainFrame else { return }
            let list = (head["items"] as? [[String: Any]] ?? []).enumerated().map { k, b in
                AltaButton(id: k, label: b["label"] as? String ?? "", primary: b["primary"] as? Bool ?? false, enabled: b["enabled"] as? Bool ?? true)
            }
            if list != buttons { withAnimation(Motion.snappy) { buttons = list } }
        case "dialog":
            guard frame.isMainFrame else { return }
            guard head["open"] as? Bool == true else {
                dialogTask?.cancel()
                withAnimation(Motion.gentle) { dialog = nil }
                return
            }
            let title = head["title"] as? String ?? ""
            let text = head["text"] as? String ?? ""
            let buttons = head["buttons"] as? [String] ?? []
            dialogTask?.cancel()
            dialogTask = Task { [weak self] in
                // (its opening fade done, a picture of it as Alta draws it — then hidden in the page, the Mac's shown)
                try? await Task.sleep(nanoseconds: 380_000_000)
                guard let self, !Task.isCancelled else { return }
                let picture = await self.dialogPicture()
                guard !Task.isCancelled else { return }
                _ = await self.run("return window.__simplDialog ? window.__simplDialog.show(false) : null", in: nil)
                if self.dialog != nil {
                    self.dialog?.title = title
                    self.dialog?.text = text
                    self.dialog?.buttons = buttons
                    self.dialog?.picture = picture
                } else {
                    self.dialog = AltaDialog(title: title, text: text, buttons: buttons, picture: picture)
                }
            }
        case "focus":
            guard frame.isMainFrame else { return }
            let state = head["state"] as? String ?? ""
            withAnimation(Motion.gentle) { focusState = state }
            guard !["question", "lesson", "overview"].contains(state) else { return }
            var entry = "\(Date().formatted(date: .abbreviated, time: .standard)) — Alta's page shown as it is: \(head["reason"] as? String ?? "not recognised") [\(state)]"
            if let sh = head["shape"], !(sh is NSNull), let d = try? JSONSerialization.data(withJSONObject: sh, options: [.prettyPrinted, .sortedKeys]),
               let t = String(data: d, encoding: .utf8) { entry += "\n" + t }
            withAnimation(Motion.gentle) {
                errorLogs.append(entry)
                if errorLogs.count > 50 { errorLogs.removeFirst() }
            }
        case "cover":
            guard frame.isMainFrame else { return }
            if head["none"] as? Bool == true {
                if cover != nil { withAnimation(Motion.gentle) { cover = nil } }
                return
            }
            guard let c = try? JSONDecoder().decode(AltaCover.self, from: data) else { return }
            withAnimation(Motion.gentle) { cover = c }
        case "overview":
            guard let o = try? JSONDecoder().decode(AltaOverview.self, from: data) else { return }
            for x in o.objectives where !x.name.isEmpty && names[x.id] != x.name { names[x.id] = x.name }
            UserDefaults.standard.set(names, forKey: Self.namesKey)
            withAnimation(Motion.gentle) {
                overview = merge(overview, o)
                hear(percent: o.percent.map { $0 / 100 } ?? o.progress, targets: o.targets ?? [])
                keep(o.objectives)
            }
        case "content":
            guard var r = try? JSONDecoder().decode(AltaReport.self, from: data) else { return }
            // (1.3.34) an answer that names only the objective on screen: the assignment's objectives kept from the last
            if let old = report {
                if r.objectives.isEmpty { r.objectives = old.objectives }
                if r.targets.isEmpty { r.targets = old.targets }
                if r.name == nil { r.name = old.name }
                if r.due == nil { r.due = old.due }
                if r.threshold == nil { r.threshold = old.threshold }
            }
            remember(r)
            withAnimation(Motion.gentle) {
                report = r
                hear(percent: r.percent.map { $0 / 100 } ?? r.progress, targets: r.targets)
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

    private func remember(_ r: AltaReport) {
        guard let id = r.current.id, let name = r.current.name, !name.isEmpty, names[id] != name else { return }
        names[id] = name
        if names.count > 400 { names = Dictionary(uniqueKeysWithValues: names.suffix(300).map { ($0.key, $0.value) }) }
        UserDefaults.standard.set(names, forKey: Self.namesKey)
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

    /// (1.3.47) The Error Logs as text, to copy.
    var errorLogText: String { errorLogs.joined(separator: "\n\n") }

    func clearErrorLogs() { withAnimation(Motion.gentle) { errorLogs = [] } }

    // MARK: Save Page Code

    /// (1.3.46) Every bit of code of Alta's page as it is now — the page and each frame in it: its HTML (with any web
    /// component's own tree), and the scripts and style sheets it loads — in one text file in Downloads, shown in Finder.
    /// Addresses are written without their queries (a launch's tokens ride in them).
    func savePageCode(withLogs: Bool = false) async -> URL? {
        let version = (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "?"
        var out = "Simpl for Mac \(version): Knewton Alta page code, saved \(Date().formatted(date: .complete, time: .standard))\n"
        // (1.3.47) with the Error Logs first, when saved from them
        if withLogs {
            out += "\n" + String(repeating: "=", count: 100) + "\nERROR LOGS (\(errorLogs.count))\n" + String(repeating: "=", count: 100) + "\n"
            out += errorLogs.joined(separator: "\n\n") + "\n"
        }
        var seen = Set<String>()
        var count = 0
        let targets: [WKFrameInfo?] = [nil] + frames.map { Optional($0) }
        for f in targets {
            guard let r = await run(Self.collect, in: f) as? [String: Any] else { continue }
            let url = r["url"] as? String ?? "?"
            let html = r["html"] as? String ?? ""
            guard seen.insert(url + "#" + String(html.count)).inserted else { continue }
            count += 1
            out += "\n\n" + String(repeating: "=", count: 100) + "\n"
            out += "PAGE \(count)\(f == nil ? " (the popup's page)" : " (a frame in it)"): \(url)\n"
            out += "Title: \(r["title"] as? String ?? "")\n" + String(repeating: "=", count: 100) + "\n"
            out += "\n---- HTML ----\n" + html + "\n"
            for sh in r["shadows"] as? [[String: Any]] ?? [] {
                out += "\n---- WEB COMPONENT'S OWN TREE: \(sh["host"] as? String ?? "") ----\n" + (sh["html"] as? String ?? "") + "\n"
            }
            for file in r["files"] as? [[String: Any]] ?? [] {
                let kind = (file["kind"] as? String ?? "file").uppercased()
                out += "\n---- \(kind): \(file["url"] as? String ?? "") ----\n"
                if let t = file["text"] as? String { out += t + "\n" } else { out += "(could not be read: \(file["error"] as? String ?? "unknown"))\n" }
            }
        }
        guard count > 0 else { return nil }
        let stamp: String = {
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.dateFormat = "yyyy-MM-dd 'at' HH.mm.ss"
            return f.string(from: Date())
        }()
        let downloads = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads", isDirectory: true)
        let file = downloads.appendingPathComponent("Simpl Alta \(withLogs ? "Error Logs" : "Code") \(stamp).txt")
        do { try out.write(to: file, atomically: true, encoding: .utf8) } catch { return nil }
        return file
    }

    /// Run in one page: its HTML, its web components' own trees, and the text of each script and style sheet it loads.
    private static let collect = #"""
    var strip = function (u) { try { var x = new URL(u, location.href); return x.origin + x.pathname; } catch (e) { return String(u || '').split('?')[0]; } };
    var shadows = [];
    var walk = function (root, path) {
      [].forEach.call(root.querySelectorAll('*'), function (el) {
        if (!el.shadowRoot) return;
        var p = path + ' > ' + el.tagName.toLowerCase() + (el.id ? '#' + el.id : '');
        shadows.push({ host: p, html: el.shadowRoot.innerHTML });
        walk(el.shadowRoot, p);
      });
    };
    walk(document, '');
    var want = [], had = {};
    var add = function (kind, u) { if (u && !had[u]) { had[u] = 1; want.push([kind, u]); } };
    [].forEach.call(document.querySelectorAll('script[src]'), function (s) { add('script', s.src); });
    [].forEach.call(document.querySelectorAll('link[rel~="stylesheet"][href]'), function (l) { add('style sheet', l.href); });
    var read = function (w) {
      var late = new Promise(function (_, no) { setTimeout(function () { no(new Error('timed out')); }, 15000); });
      return Promise.race([fetch(w[1]).then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); }), late])
        .then(function (t) { return { kind: w[0], url: strip(w[1]), text: t }; }, function (e) { return { kind: w[0], url: strip(w[1]), error: String((e && e.message) || e) }; });
    };
    var files = await Promise.all(want.map(read));
    // (style rules a script put in by hand, which no file holds)
    [].forEach.call(document.styleSheets, function (sh) {
      if (sh.href) return;
      try {
        var own = sh.ownerNode && (sh.ownerNode.textContent || '').trim();
        if (own) return;
        var t = [].map.call(sh.cssRules, function (r) { return r.cssText; }).join('\n');
        if (t) files.push({ kind: 'style rules put in by a script', url: strip(location.href), text: t });
      } catch (e) {}
    });
    return { url: strip(location.href), title: document.title, html: '<!DOCTYPE html>\n' + document.documentElement.outerHTML, shadows: shadows, files: files };
    """#

    /// The script run in that frame (nil: the main page); what it gives back, or nil.
    private func run(_ js: String, in frame: WKFrameInfo?) async -> Any? {
        await withCheckedContinuation { (go: CheckedContinuation<Any?, Never>) in
            web.callAsyncJavaScript(js, arguments: [:], in: frame, in: .page) { result in
                switch result {
                case .success(let v): go.resume(returning: v)
                case .failure: go.resume(returning: nil)
                }
            }
        }
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

import AppKit
import QuartzCore
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
      // (1.3.101) a post timed for the app's line on how long the open took: when this page began (`nav`, the clock's
      // time) and how long after that it was posted (`t`)
      var stamp = function (m) { try { m.t = Math.round(performance.now()); m.nav = Math.round(performance.timeOrigin); } catch (e) {} return m; };
      // (Alta's page, by its address — not the launch on the way to it)
      var altaHere = /knewton|alta/i.test(location.hostname + location.pathname);
      // (1.3.46) this page made known to the app, for Save Page Code (each frame's code is asked of it there) — (1.3.101)
      // and whether it is Alta's: the app's skeleton stays up until the first of Alta's own
      post(stamp({ kind: 'frame', alta: altaHere }));
      // (1.3.60) Alta's page kept out of sight from its first moment until it is laid out — its question on glass, its
      // cover as the start screen, or (not recognised) as it is — so it never flashes in before its question; the app
      // shows its own Loading meanwhile. A page that is not Alta's (the launch on the way to it) is shown at once.
      var reveal = function () {};
      var veilAgain = function () {};
      // (1.3.83) the page's look told again — light or dark, as the student picks it — once it is in sight
      var lookNow = function () {};
      // (1.3.83) a pop-up of Alta's given back to its page — what it holds the app cannot draw as its own, or the student
      // asked — shown there as Alta draws it: Alta's page as it is, in sight under it, until it closes
      var dialogInPage = false;
      // (1.3.106) the page's own reads of itself — its mastery bars, the overview's words, the cover — begun only once the
      // page has been shown or its cover read (`begun`), so they never take the page's time while Alta is still drawing
      // it; a frame inside the page reads from the start, as before
      var begun = window.top !== window;
      // (and three seconds in, whatever the page is doing: a page out of sight for longer — not recognised, kept back
      // for its grace — still has its mastery read for the popup's rail)
      if (!begun) setTimeout(function () { begun = true; }, 3000);
      // (1.3.106) changes to the page (elements, words) counted, for the reads that need not look again while nothing
      // has changed
      var pageChanges = 0;
      if (window.top === window) {
        var showing = false, firstVeil = 0;
        var veils = altaHere;
        // (1.3.101) `steady`: how long the part shown had been quiet, and whether a cap showed it before it was — with
        // when the page's DOMContentLoaded came, for the app's line
        reveal = function (how, steady) {
          begun = true;
          if (firstVeil) { clearTimeout(firstVeil); firstVeil = 0; }
          if (document.documentElement) document.documentElement.setAttribute('data-simpl-shown', '');
          lookNow();
          if (!showing) {
            showing = true;
            var nt = null;
            try { nt = performance.getEntriesByType('navigation')[0] || null; } catch (e) {}
            post(stamp({ kind: 'shown', how: how || '', quiet: steady ? steady.quiet : null, capped: !!(steady && steady.capped),
              dcl: nt && nt.domContentLoadedEventStart ? Math.round(nt.domContentLoadedEventStart) : null }));
          }
        };
        // (1.3.62) out of sight again — Alta between one part and the next, taking its time — until the next is laid out
        veilAgain = function () {
          if (!veils || !showing || dialogInPage || window.__simplBrowser) return;
          if (document.documentElement) document.documentElement.removeAttribute('data-simpl-shown');
          showing = false;
          post({ kind: 'hidden' });
        };
        if (veils) {
          // (see-through rather than hidden: still laid out and read as it shows — its words, their lines — just not seen;
          // 1.3.62: and the page's own ground cleared too, so a light one of Alta's never flashes through)
          var VEIL = 'html:not([data-simpl-shown]) body { opacity: 0 !important; }\nhtml:not([data-simpl-shown]), html:not([data-simpl-shown]) body { background: transparent !important; }';
          try {
            var veil = new CSSStyleSheet();
            veil.replaceSync(VEIL);
            document.adoptedStyleSheets = document.adoptedStyleSheets.concat([veil]);
          } catch (e) {
            var putVeil = function () {
              if (!document.documentElement) { setTimeout(putVeil, 10); return; }
              var st = document.createElement('style');
              st.id = 'simpl-veil';
              st.textContent = VEIL;
              document.documentElement.appendChild(st);
            };
            putVeil();
          }
          // (never longer than this, whatever happens — the first veil's: once the page has been shown it stands down,
          // and a veil put back between Alta's parts keeps its own time, never cut short by this one going off later)
          firstVeil = setTimeout(function () { firstVeil = 0; reveal('timeout'); }, 9000);
        } else {
          var revealSoon = function () { if (document.documentElement) reveal('not alta'); else setTimeout(revealSoon, 10); };
          revealSoon();
        }
      }
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
      // (1.3.106) the page's web components — each element with a tree of its own — listed once, and looked for again
      // only after the page has had an element put in or a tree made: before, every look in them (the mastery bars every
      // second and a half, the overview's words, the pop-ups every 1.2 s) walked every element of the page for them
      var hosts = [], hostsDirty = true;
      var shadowHosts = function () {
        if (hostsDirty) {
          hostsDirty = false;
          hosts = [];
          var walk = function (root) {
            try { [].forEach.call(root.querySelectorAll('*'), function (el) { if (el.shadowRoot) { hosts.push(el); walk(el.shadowRoot); } }); } catch (e) {}
          };
          walk(document);
        }
        return hosts;
      };
      try {
        new MutationObserver(function (list) {
          pageChanges++;
          if (hostsDirty) return;
          for (var i = 0; i < list.length; i++) {
            var added = list[i].addedNodes;
            for (var j = 0; j < added.length; j++) if (added[j].nodeType === 1) { hostsDirty = true; return; }
          }
        }).observe(document, { childList: true, subtree: true, characterData: true });
      } catch (e) {}
      try {
        var attach0 = Element.prototype.attachShadow;
        if (attach0) Element.prototype.attachShadow = function () { hostsDirty = true; return attach0.apply(this, arguments); };
      } catch (e) {}
      // (what matches in the page and in each of its web components' trees, in the page's order)
      var deep = function (sel) {
        var out = [];
        try { out.push.apply(out, [].slice.call(document.querySelectorAll(sel))); } catch (e) {}
        shadowHosts().forEach(function (h) {
          try { if (h.isConnected && h.shadowRoot) out.push.apply(out, [].slice.call(h.shadowRoot.querySelectorAll(sel))); } catch (e) {}
        });
        return out;
      };
      // (1.3.106) a keyword looked for in the page's words as they stand, with no layout — before the words as they show
      // (innerText, which lays the page out) are read for it; what shows is always among them
      var mentions = function (el, re) { return !!el && re.test(el.textContent || ''); };
      // (1.3.106) the question shown alone (the page script's own focus): the reads below need not look again while
      // nothing in the page has changed — every third time all the same
      var focusOn = function () { return !!document.documentElement && document.documentElement.hasAttribute('data-simpl-q'); };
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
        var lines = mentions(document.body, /mastery/i) ? String((document.body && document.body.innerText) || '').split('\n').map(function (l) { return l.trim(); }).filter(Boolean) : [];
        var percent = null;
        lines.forEach(function (l, i) {
          if (percent !== null || !/^(total |overall |assignment )?mastery\b/i.test(l)) return;
          var m = /(\d{1,3}(?:\.\d+)?)\s*%/.exec(l) || /^(\d{1,3}(?:\.\d+)?)\s*%$/.exec(lines[i + 1] || '');
          if (m) percent = Math.min(100, +m[1]);
        });
        if (percent === null) bars.forEach(function (b) { if (percent === null && /mastery/.test(b.label)) percent = Math.round(b.value * 1000) / 10; });
        return { percent: percent, bars: bars };
      };
      // (1.3.106) whether a read every so often need not look this time: not begun yet, or the question shown alone and
      // nothing changed in the page since it last looked (`at`: its own record; every third time it looks all the same)
      var idleRead = function (at) {
        if (!begun) return true;
        if (focusOn() && at.changes === pageChanges && ++at.skipped < 3) return true;
        at.changes = pageChanges; at.skipped = 0;
        return false;
      };
      var lastMeter = '', meterAt = { changes: -1, skipped: 0 };
      setInterval(function () {
        if (idleRead(meterAt)) return;
        var m = meter();
        if (m.percent === null && !m.bars.length) return;
        var s = JSON.stringify(m);
        if (s !== lastMeter) { lastMeter = s; post({ kind: 'meter', percent: m.percent, bars: m.bars }); }
      }, 1500);
      // (1.3.45) the overview as its words read, when it lists objectives with their estimates: passed when it changes
      var lastScrape = '', scrapeAt = { changes: -1, skipped: 0 };
      setInterval(function () {
        if (idleRead(scrapeAt) || !mentions(document.body, /estimated/i)) return;
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
        // (maths drawn whole) never a part of Alta's, though: one not kept yet — the next question, put in as the last
        // goes, before the page is looked at again — is laid out as it is and only made see-through until it is kept.
        // Learnosity's answer box (MathQuill) sizes its brackets and its √ to what they hold as it draws them, and one
        // drawn hidden (nothing in it has a size) had them flat — no brackets, no √, the bar over what it held alone —
        // until the page was loaded again. It is kept out of the flow, though, as wide as the flow it is in, so one Alta
        // puts in above the question (a lesson, More Instruction) never pushes the question down under its glass, a gap
        // over it, while it is still see-through: the two move together once it is kept, as they did when it was hidden.
        var FOCUS = 'html[data-simpl-q] body *:not([data-simpl-path]):not([data-simpl-keep]):not([data-simpl-keep] *):not(section[class*="compound__compound"]):not(section[class*="compound__compound"] *) { display: none !important; }\n'
          + 'html[data-simpl-q] section[class*="compound__compound"]:not([data-simpl-keep]) { opacity: 0 !important; pointer-events: none !important; position: absolute !important; left: 0 !important; right: 0 !important; max-width: 860px !important; margin-left: auto !important; margin-right: auto !important; box-sizing: border-box !important; }\n'
          + 'html[data-simpl-q] [data-simpl-path]:has(> section[class*="compound__compound"]:not([data-simpl-keep])) { position: relative !important; }\n'
          + 'html[data-simpl-q] [data-simpl-hide] { display: none !important; }\n'
          + 'html[data-simpl-q] [data-simpl-path]:not(html):not(body) { padding-top: 0 !important; margin-top: 0 !important; }\n'
          + 'html[data-simpl-q] body { padding: 18px 28px 40px !important; }\n'
          // (1.3.53) Alta's own button bars put away — the app shows their buttons as its own — and a gap between a
          // question's words and its answer, for the two glass panels behind them
          + 'html[data-simpl-q] [data-simpl-bar] { display: none !important; }\n'
          + 'html[data-simpl-q] [data-simpl-answer] { margin-top: 48px !important; }\n'
          // (1.3.60) each part a column of its own width, centred — narrower glass panels than the popup's whole width
          + 'html[data-simpl-q] section[data-simpl-keep] { max-width: 860px !important; margin-left: auto !important; margin-right: auto !important; box-sizing: border-box !important; }\n'
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
          + 'html[data-simpl-q][data-simpl-dark] section[data-simpl-keep] :is(img, picture, video, canvas, iframe, object, embed):not([data-simpl-pic]):not([data-simpl-pic] *) { filter: invert(1) hue-rotate(180deg) !important; }\n'
          // (1.3.62) and its graphs and drawings — a graph's box (Learnosity's, JSXGraph's) and any large SVG — turned back
          // whole, labels and all, so they read as Alta draws them
          // (1.3.70: only when asked for — the popup's Light button; by default a graph's colours are turned over with
          // the rest of the part, light lines on the dark glass)
          + 'html[data-simpl-q][data-simpl-dark][data-simpl-pics-light] section[data-simpl-keep] [data-simpl-pic] { filter: invert(1) hue-rotate(180deg) !important; }\n'
          // (1.3.68) on a white card of its own: a graph is drawn in dark ink for a white page, its ground left clear —
          // on dark glass its lines and numbers were lost
          + 'html[data-simpl-q][data-simpl-dark][data-simpl-pics-light] section[data-simpl-keep] [data-simpl-pic] { background-color: #fff !important; border-radius: 10px !important; box-shadow: 0 0 0 6px #fff !important; }\n'
          // (1.3.70) a graph in what a question asks drawn small — the popup's button by it, or a click on it, opens it
          // large in a window of its own
          + 'html[data-simpl-q] [data-simpl-mini] { zoom: var(--simpl-zoom, 1) !important; width: var(--simpl-w, fit-content) !important; cursor: zoom-in !important; border-radius: 10px !important; outline: 2px solid transparent !important; outline-offset: 6px !important; transition: outline-color .18s ease !important; }\n'
          + 'html[data-simpl-q] [data-simpl-mini]:hover { outline-color: rgba(10, 132, 255, .55) !important; }\n';
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
            var overview = (mentions(document.body, /estimated/i) && /estimated\s+\d+\s*[-–]\s*\d+\s+questions?/i.test(txt(document.body)))
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
            dialogs: all(DIALOG_SEL + ', .MuiModal-root').filter(shown).length
          };
        };
        var marked = [], keptNow = [];
        var unmark = function () {
          marked.forEach(function (m) { m[0].removeAttribute(m[1]); });
          marked = []; keptNow = [];
          watchKept(); // (1.3.106: no part watched)
          document.documentElement.removeAttribute('data-simpl-q');
          lookNow(); // (1.3.83: Alta's page as it is, dark too when Dark is picked, from the moment it is)
        };
        var mark = function (el, a) { if (!el.hasAttribute(a)) { el.setAttribute(a, ''); marked.push([el, a]); } };
        // (1.3.62) a graph or drawing in a part: a graph's box by its name, or an SVG at least 140 × 100 — the outermost
        // one, so its labels go with it (graphs are drawn after the part, so this is looked for at every pass)
        var PIC = /jxgbox|graphplotting|lrn_graph|lrn-graph|lrn_chart|lrn-chart|highcharts|desmos/i;
        var pics = function (keep) {
          keep.forEach(function (k) {
            [].forEach.call(k.querySelectorAll('div, svg, img, canvas'), function (el) {
              if (el.closest('[data-simpl-pic]')) return;
              var c = typeof el.className === 'string' ? el.className : ((el.className && el.className.baseVal) || '');
              var big = false;
              // (1.3.68: a large picture or canvas too — a graph is often an image)
              if (/^(svg|img|canvas)$/.test(el.tagName.toLowerCase())) { var r = el.getBoundingClientRect(); big = r.width >= 140 && r.height >= 100; }
              if (PIC.test(c) || big) mark(el, 'data-simpl-pic');
            });
          });
        };
        // (1.3.70) the small graphs: a graph in what a question asks (not one to answer on), larger than 600 × 440, drawn
        // (1.3.98: twice 1.3.70's 300 × 220 — the owner found the graphs too small to read; Enlarge is still beside them)
        // at a zoom that brings it down to that
        var MINI_W = 600, MINI_H = 440, picEls = [];
        var minis = function () {
          picEls = all('[data-simpl-keep] [data-simpl-mini]');
          all('[data-simpl-keep] [data-simpl-pic]').forEach(function (el) {
            if (el.hasAttribute('data-simpl-mini') || el.closest('[data-simpl-answer], [class*="lrn_response"]')) return;
            // (its drawing's size, not its box's: a graph's box is often a block as wide as the part, its drawing far less)
            var r = el.getBoundingClientRect();
            if (!/^(svg|img|canvas)$/i.test(el.tagName)) {
              var inner = [].slice.call(el.querySelectorAll('svg, canvas, img')).map(function (x) { return x.getBoundingClientRect(); }).filter(function (b) { return b.width > 0 && b.height > 0; });
              if (inner.length) r = { width: Math.max.apply(null, inner.map(function (b) { return b.width; })), height: Math.max.apply(null, inner.map(function (b) { return b.height; })) };
            }
            if (r.width < MINI_W + 60 && r.height < MINI_H + 60) return;
            var z = Math.min(MINI_W / r.width, MINI_H / r.height);
            el.style.setProperty('--simpl-zoom', String(Math.round(z * 1000) / 1000));
            // (Mac 1.3.133 / Windows 0.3.43) as wide as its drawing, not fit-content: a Desmos graph's box holds only parts as
            // wide as it (100%) and its drawing laid over them, so fit-content made it 0 wide, and Desmos drew nothing in it
            el.style.setProperty('--simpl-w', Math.round(r.width) + 'px');
            mark(el, 'data-simpl-mini');
            picEls.push(el);
          });
          picEls = picEls.filter(function (el) { return el.isConnected && el.hasAttribute('data-simpl-mini'); });
        };
        document.addEventListener('click', function (e) {
          if (!document.documentElement.hasAttribute('data-simpl-q')) return;
          var p = e.target && e.target.closest ? e.target.closest('[data-simpl-mini]') : null;
          if (!p) return;
          e.preventDefault(); e.stopPropagation();
          var i = picEls.indexOf(p);
          if (i >= 0) post({ kind: 'picOpen', i: i });
        }, true);
        window.__simplPic = {
          // (where a small graph is now, in the view, and how far it is drawn down — for its picture, large)
          rect: function (i) { var el = picEls[i]; if (!el || !el.isConnected) return null; var r = el.getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height, z: parseFloat(el.style.getPropertyValue('--simpl-zoom')) || 1 }; }
        };
        // (1.3.70) a loading page or part — a spinner, a skeleton, an indeterminate bar, something marked busy, or words
        // saying it is loading — is recognised as such: kept out of sight (the popup's skeleton) until it is done
        var BUSY = '[aria-busy="true"], [role="progressbar"]:not([aria-valuenow]), [class*="spinner" i], [class*="loader" i], [class*="skeleton" i], [class*="shimmer" i], [class*="loading" i], .MuiCircularProgress-root, .MuiLinearProgress-root, .MathJax_Processing';
        var busy = function (root) {
          if (!root) return false;
          if ([].some.call(root.querySelectorAll(BUSY), function (b) { return shown(b) && !b.closest('[data-simpl-pic]'); })) return true;
          // (1.3.106: its words as they show read only when its words as they stand say any of these)
          if (!mentions(root, /loading|please\s*wait|one\s*moment|just\s*a\s*moment|preparing/i)) return false;
          var t = norm(root.innerText || '');
          return t.length < 120 && /^(loading|please wait|one moment|just a moment|preparing)\b/.test(t);
        };
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
          watchKept(); // (1.3.106: the parts kept now told as they change)
          verdictDirty = true;
          var st = document.getElementById('simpl-focus');
          if (!st) { st = document.createElement('style'); st.id = 'simpl-focus'; (document.head || document.documentElement).appendChild(st); }
          if (st.textContent !== FOCUS) st.textContent = FOCUS;
          document.documentElement.setAttribute('data-simpl-q', '');
          dark();
        };
        // (1.3.50) light or dark as the app is: the app sets window.__simplDark (before the page, and again when it changes)
        // (1.3.83) as the student picks it above the objectives: light unless Dark is picked. Dark lays the parts kept on
        // dark glass, turned over, as 1.3.50 did; and draws Alta's page as it is (not recognised, or under a pop-up of
        // Alta's given back to it) dark as a tool's page is drawn dark (ToolDarkPage) — its colours turned round, its
        // pictures, videos and frames kept as they are — unless it has a dark look of its own (a dark ground, or a colour
        // scheme it says can be dark), which is told once the page is in sight and out of the focus, its own ground then
        // reading as it is
        var LOOK = 'html[data-simpl-dark][data-simpl-whole-dark]:not([data-simpl-q]) { filter: invert(.92) hue-rotate(180deg) !important; background: #fff !important; }\n'
          + 'html[data-simpl-dark][data-simpl-whole-dark]:not([data-simpl-q]) :is(img, video, canvas, embed, object, iframe, svg image, [style*="background-image"]) { filter: invert(1) hue-rotate(180deg) !important; }\n';
        var lum = function (c) { var m = String(c || '').match(/\d+(\.\d+)?/g); if (!m || m.length < 3 || (m.length > 3 && +m[3] === 0)) return null; return (0.2126 * m[0] + 0.7152 * m[1] + 0.0722 * m[2]) / 255; };
        var lightPage = function () {
          var meta = document.querySelector('meta[name="color-scheme"]');
          if (meta && /dark/i.test(meta.content || '')) return false;
          var b = document.body ? lum(getComputedStyle(document.body).backgroundColor) : null;
          var h = lum(getComputedStyle(document.documentElement).backgroundColor);
          var v = b !== null ? b : (h !== null ? h : 1);
          return v > 0.55;
        };
        var dark = function () {
          var de = document.documentElement;
          if (!de) return;
          de.toggleAttribute('data-simpl-dark', !!window.__simplDark);
          de.toggleAttribute('data-simpl-pics-light', !!window.__simplPicsLight); // (1.3.70)
          if (!window.__simplDark) { de.removeAttribute('data-simpl-whole-dark'); return; }
          if (!document.getElementById('simpl-look')) { var st = document.createElement('style'); st.id = 'simpl-look'; st.textContent = LOOK; (document.head || de).appendChild(st); }
          if (!de.hasAttribute('data-simpl-whole-dark') && de.hasAttribute('data-simpl-shown') && !de.hasAttribute('data-simpl-q') && lightPage()) de.setAttribute('data-simpl-whole-dark', '');
        };
        window.__simplFocusDark = dark;
        lookNow = dark;
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
        // (1.3.99) Alta's verdict on the question — its green "Yes that's right. Keep it up!" strip, its red "Not quite…"
        // one — read from the parts kept, for the app's own pill beside its buttons: 'right', 'wrong' or '' (none shown).
        // Only a banner, an alert or a box marked by its class is looked at (this runs every 350 ms), never the bars put
        // away; each is told by its words first — short ones, Alta's own phrases — else by its class, the first that
        // decides. It is said with the panels, so the app hears it as it comes and as it goes with the next question.
        // (1.3.100 / 0.3.20) told by Alta's words only, never by a class — a class with "right" or "status" in it is
        // as often layout ("text-right", "progress-status") as a verdict, and showed Correct on a question not yet answered —
        // and only from a strip a student can see: a box at least 40 × 14, not a screen reader's 1-pixel live region (which
        // keeps the last thing it announced into the next question)
        var VERDICT_SEL = '[role="alert"], [role="status"], [aria-live], [class*="feedback" i], [class*="alert" i], [class*="notice" i], [class*="banner" i]';
        var RIGHT_WORDS = /^(yes,? )?(that's|that is) (right|correct)|^correct\b|keep it up|well done|nicely done|nice work|great job/;
        var WRONG_WORDS = /^not quite|^incorrect\b|(that's|that is) not (right|correct)|^not correct|^try again|^(that's|that is) wrong/;
        var verdictOf = function (el) {
          var t = String(el.textContent || '').replace(/[‘’]/g, "'").replace(/\s+/g, ' ').trim().toLowerCase();
          if (!t || t.length >= 160) return '';
          if (WRONG_WORDS.test(t)) return 'wrong';
          if (RIGHT_WORDS.test(t)) return 'right';
          return '';
        };
        var onScreen = function (el) {
          if (!visible(el)) return false;
          var b = el.getBoundingClientRect();
          if (b.width < 40 || b.height < 14) return false;
          var cs = getComputedStyle(el);
          return Number(cs.opacity) > 0.05 && !/rect\(0/.test(cs.clip || '');
        };
        var verdict = function () {
          var secs = cards();
          for (var i = 0; i < secs.length; i++) {
            var els = secs[i].querySelectorAll(VERDICT_SEL);
            for (var j = 0; j < els.length; j++) {
              var el = els[j];
              if (el.closest('[data-simpl-bar], [data-simpl-hide]') || !onScreen(el)) continue;
              var v = verdictOf(el);
              if (v) return v;
            }
          }
          return '';
        };
        // (1.3.106) `lazy`: told on demand (publishSoon) — the verdict read again only when something in the parts kept
        // has changed since it was last read (`verdictDirty`), else as it was last read
        var verdictDirty = true, verdictSaid = '', pubAt = 0;
        var publish = function (lazy) {
          pubAt = Date.now();
          var on = document.documentElement.hasAttribute('data-simpl-q');
          var bs = on ? bars() : [];
          buttonEls = bs.map(function (b) { return b.el; });
          var bsSaid = JSON.stringify(bs.map(function (b) { return { label: b.label, primary: b.primary, enabled: b.enabled }; }));
          if (bsSaid !== lastButtons) { lastButtons = bsSaid; post({ kind: 'buttons', items: JSON.parse(bsSaid) }); }
          var ps = on ? panels() : [];
          if (on) minis(); else picEls = [];
          if (!on) { verdictSaid = ''; verdictDirty = true; }
          else if (!lazy || verdictDirty) { verdictSaid = verdict(); verdictDirty = false; }
          var bottom = ps.reduce(function (m, p) { return Math.max(m, p.y + p.h); }, 0);
          var sx0 = window.scrollX || 0, sy0 = window.scrollY || 0;
          var pr = picEls.map(function (el) { var r = el.getBoundingClientRect(); return { x: Math.round(r.left + sx0), y: Math.round(r.top + sy0), w: Math.round(r.width), h: Math.round(r.height) }; });
          var psSaid = JSON.stringify({ panels: ps, height: ps.length ? bottom + 48 : 0, gen: gen, pics: pr, verdict: on ? verdictSaid : '' });
          if (psSaid !== lastPanels) { lastPanels = psSaid; var m = JSON.parse(psSaid); m.kind = 'panels'; post(m); }
        };
        // (1.3.106) The panels, the buttons and the verdict told when something may have moved them, not every 350 ms
        // whatever the page did (1.3.53–1.3.101): a change in the parts kept (their elements and words, or a class, a
        // style, a button greyed or not — `keptWatch`), a part's size (`keptSize`) or the window's, at the next frame and
        // no oftener than every 200 ms however fast the page changes; once more 450 ms after a change in the parts (a
        // strip that fades in, a box sized after it is put in) and when a fade in them ends; and every second whatever
        // happens, as a last resort.
        var pubTimer = 0, pubLater = 0;
        var queuePublish = function () {
          if (pubTimer) return;
          pubTimer = setTimeout(function () {
            var done = false;
            var run = function () { if (done) return; done = true; pubTimer = 0; publish(true); };
            try { requestAnimationFrame(run); } catch (e) {}
            setTimeout(run, 100); // (no frame comes while the view is out of sight)
          }, Math.max(0, 200 - (Date.now() - pubAt)));
        };
        var publishSoon = function (inParts) {
          if (inParts) {
            verdictDirty = true;
            if (!pubLater) pubLater = setTimeout(function () { pubLater = 0; verdictDirty = true; queuePublish(); }, 450);
          }
          queuePublish();
        };
        // (but nothing told for a change in the answer box itself, which moves nothing that is told: the caret blinking in
        // a math box — MathQuill's `mq-blink`, every half second while it has the focus — or a choice lit as the pointer
        // passes. Only for a verdict in it, shown, hidden or no longer one by its class, or the buttons' bar; a size the
        // box changes is `keptSize`'s.)
        var ANSWER_SEL = '.lrn_widget, .lrn_qr, [class*="lrn_response"]';
        var matters = function (t, was) {
          if (!t || t.nodeType !== 1 || !t.closest(ANSWER_SEL)) return true;
          if (t.closest('[class*="button-bar"], ' + VERDICT_SEL) || t.querySelector('[class*="button-bar"], ' + VERDICT_SEL)) return true;
          return !!was && /feedback|alert|notice|banner/i.test(was);
        };
        var keptWatch = null, keptSize = null;
        try { keptWatch = new MutationObserver(function (rs) { if (rs.some(function (r) { return matters(r.target, r.attributeName === 'class' && r.oldValue); })) publishSoon(true); }); } catch (e) {}
        try { keptSize = new ResizeObserver(function () { publishSoon(false); }); } catch (e) {}
        // (the parts kept now watched, and no others)
        var watchKept = function () {
          if (keptWatch) keptWatch.disconnect();
          if (keptSize) keptSize.disconnect();
          cards().forEach(function (sec) {
            try { if (keptWatch) keptWatch.observe(sec, { attributes: true, subtree: true, attributeOldValue: true, attributeFilter: ['class', 'style', 'hidden', 'disabled', 'aria-disabled', 'aria-hidden', 'open'] }); } catch (e) {}
            try { if (keptSize) keptSize.observe(sec); } catch (e) {}
          });
        };
        // (and a fade or a movement in the parts kept come to its end: a strip faded in, a box slid open)
        var settledIn = function (e) { var t = e.target; if (t && t.closest && t.closest('[data-simpl-keep]') && matters(t)) publishSoon(true); };
        document.addEventListener('transitionend', settledIn, true);
        document.addEventListener('animationend', settledIn, true);
        setInterval(function () { publish(); }, 1000);
        // (maths drawn whole: a part kept gone or out of sight looked at again, and Alta's maths drawn flat sized again — see
        // `watch` and `remeasure` — still every 350 ms)
        setInterval(function () { watch(); remeasure(); }, 350);
        window.addEventListener('resize', function () { publishSoon(false); });
        window.__simplButtons = {
          press: function (i) { var b = buttonEls[i]; if (!b || b.disabled || b.getAttribute('aria-disabled') === 'true') return false; b.click(); setTimeout(publish, 60); return true; }
        };
        var told = '', badSince = 0, timer = null, dueAt = 0, changeSince = 0, wasQuestion = false, loadingSince = 0;
        var report = function (state, r) {
          if (state === told) return;
          told = state;
          post(stamp({ kind: 'focus', state: state, reason: (r && r.reason) || '', shape: /^(question|lesson|overview)$/.test(state) ? null : shape(r || {}) }));
        };
        // ---- (1.3.51) Alta's pop-ups as the Mac's own: each one open passed to the app and hidden in the page while the
        // app shows it, the app's buttons pressing Alta's ----
        // (1.3.83) Read whole, once it has settled. 1.3.51–1.3.82 passed a pop-up as soon as it was seen and the app showed
        // a picture of it taken in the page: one that came while Alta's page was still out of sight (its learn page
        // loading, or between parts: `body` see-through) was pictured see-through, and the app's sheet showed its title
        // and its buttons round an empty picture; once the app had hidden it, its words read as nothing and it was
        // passed again. Now a pop-up — a dialog by its role, one marked modal, or Material's, React Toolbox's or
        // react-modal's own box, in the page or in a web component's own tree, the innermost — is read with the app's
        // hiding lifted for that moment (no paint comes between), and passed once it has settled: nothing it says changed
        // for a moment, nothing in it still loading (a spinner, a picture not in yet), its fade done — three seconds at
        // most — and again whenever what it says changes (a next step). It is passed as its title (what names it, else a
        // heading it opens with), its body as plain HTML the app draws as its own words (paragraphs, headings, lists,
        // tables, links, bold and italic; each formula as its TeX — MathJax's own script or record, KaTeX's or MathML's
        // annotation, the formula's attribute — else as MathML; a picture by its address, a drawing as itself, a canvas
        // as its picture), and every one of its buttons, whatever it says: its words, whether Alta makes it the main one,
        // whether it can be pressed, a close button (an ×) told apart. A pop-up the app cannot draw as its own — it holds
        // a video, a form, a frame of another site's, a picture or formula that cannot be read — or one with nothing in
        // it to read, or still loading after three seconds, is never shown as an empty sheet: it is given back to Alta's
        // page, which the app shows as it is, in sight, until the pop-up closes.
        var DIALOG_SEL = '[role="dialog"], [role="alertdialog"], dialog[open], [aria-modal="true"], .MuiDialog-paper, [data-react-toolbox="dialog"], .ReactModal__Content, mat-dialog-container, .modal.show .modal-content, .modal.in .modal-content';
        var BTN = 'button, [role="button"], input[type="button"], input[type="submit"], input[type="reset"], a[href][class*="btn" i], a[href][class*="button" i]';
        var MATHS = '.MathJax_Display, .MathJax_SVG_Display, .MathJax, .MathJax_CHTML, .MathJax_SVG, mjx-container, .katex-display, .katex, math, .mq-math-mode, [data-latex], [data-tex]';
        var HEADING = 'h1, h2, h3, h4, h5, h6, [role="heading"], [class*="DialogTitle"], [class*="title" i]';
        var KEEP_TAGS = /^(p|h[1-6]|ul|ol|li|table|thead|tbody|tfoot|tr|td|th|b|strong|i|em|u|sup|sub|code|pre|blockquote|dl|dt|dd|s|small|mark|figure|figcaption)$/;
        var dialogEl = null, dialogHolder = null, dialogSaid = '', dialogRead = '', dialogDirty = 0, dialogWaiting = 0, dialogReadAt = 0, dialogTicks = 0;
        var dialogBtns = [], dialogLinks = [], dialogHosts = [];
        var esc = function (s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); };
        var styleOf = function (el) { return (el.ownerDocument.defaultView || window).getComputedStyle(el); };
        // (the app's own hiding lifted for the moment a pop-up is looked at and read, so one the app is showing reads as
        // it is — hidden, its words read as nothing)
        var unhidden = function (fn) {
          var de = document.documentElement, on = de.hasAttribute('data-simpl-native-dialog');
          if (on) de.removeAttribute('data-simpl-native-dialog');
          try { return fn(); } finally { if (on) de.setAttribute('data-simpl-native-dialog', ''); }
        };
        // (how see-through it is with what holds it: its fade, its holder's — never the page's veil, the app's own)
        var opacityOf = function (el) {
          var o = 1;
          for (var x = el; x && x !== document.body && x !== document.documentElement; x = x.parentElement || (x.parentNode && x.parentNode.host) || null) o *= parseFloat(styleOf(x).opacity);
          return isFinite(o) ? o : 1;
        };
        var fading = function (el) {
          try {
            return el.getAnimations({ subtree: true }).some(function (a) { var t = a.effect && a.effect.getComputedTiming ? a.effect.getComputedTiming() : null; return a.playState === 'running' && !!t && isFinite(t.endTime); });
          } catch (e) { return false; }
        };
        // (what holds a pop-up in the page — its backdrop with it — out of a web component's own tree to its host; a
        // wrapper of no kind of its own only when it is the page's own layer over the rest)
        var holderOf = function (d) {
          var x = d;
          while (x.getRootNode && x.getRootNode() !== document && x.getRootNode().host) x = x.getRootNode().host;
          var h = x.closest('.MuiModal-root, .MuiDialog-root, [data-react-toolbox="portal"], .ReactModalPortal, .cdk-overlay-container, .modal');
          if (h) return h;
          var p = x.closest('[role="presentation"]');
          return p && (p.parentElement === document.body || styleOf(p).position === 'fixed') ? p : x;
        };
        var dialogOpen = function (d) {
          var r = d.getBoundingClientRect();
          return r.width >= 60 && r.height >= 36 && styleOf(d).visibility !== 'hidden' && opacityOf(d) > 0.02;
        };
        // (the one open now: the last, the innermost of those nested; a web component's own trees looked in every fourth
        // time, or every time while one there is open)
        var dialogNow = function () {
          dialogTicks++;
          var inside = dialogTicks % 4 === 1 || !!(dialogEl && dialogEl.isConnected && dialogEl.getRootNode() !== document);
          var ds = inside ? deep(DIALOG_SEL) : all(DIALOG_SEL);
          if (inside) dialogHosts = ds.filter(function (d) { return d.getRootNode() !== document; }).map(holderOf);
          if (!ds.length) return null;
          ds = unhidden(function () { return ds.filter(dialogOpen); });
          ds = ds.filter(function (d) { return !ds.some(function (e) { return e !== d && d.contains(e); }); });
          return ds[ds.length - 1] || null;
        };
        // (Alta's pop-ups kept in sight with the parts the focus keeps, whatever the page is doing — before, one that came
        // between parts was hidden with the rest of the page until the page was given back as it is)
        var keepDialogs = function () {
          if (!document.documentElement.hasAttribute('data-simpl-q')) return;
          all(DIALOG_SEL).map(holderOf).concat(dialogHosts).forEach(function (h) {
            if (!h.isConnected || h === document.body || h === document.documentElement || h.closest('[data-simpl-keep]')) return;
            mark(h, 'data-simpl-keep');
            for (var x = h.parentElement; x && x !== document.documentElement; x = x.parentElement) mark(x, 'data-simpl-path');
          });
        };
        // (an element's words as they show, its buttons' left out)
        var wordsOf = function (el) {
          var w = document.createTreeWalker(el, NodeFilter.SHOW_TEXT), n, out = [];
          while ((n = w.nextNode())) {
            var p = n.parentElement;
            if (!n.data.trim() || !p || p.closest('script, style, template, noscript, ' + BTN) || !p.getClientRects().length) continue;
            out.push(n.data);
          }
          return out.join(' ').replace(/\s+/g, ' ').trim();
        };
        var firstWords = function (d) {
          var w = document.createTreeWalker(d, NodeFilter.SHOW_TEXT), n;
          while ((n = w.nextNode())) {
            var p = n.parentElement;
            if (!n.data.trim() || !p || p.closest('script, style, template, noscript, ' + BTN) || !p.getClientRects().length) continue;
            return p;
          }
          return null;
        };
        var titleOf = function (d) {
          var by = d.getAttribute('aria-labelledby'), root = d.getRootNode(), t = by && root.getElementById ? root.getElementById(by.split(' ')[0]) : null;
          if (t && wordsOf(t)) return { el: d.contains(t) ? t : null, text: wordsOf(t) };
          if ((d.getAttribute('aria-label') || '').trim()) return { el: null, text: d.getAttribute('aria-label').trim() };
          var h = d.querySelector(HEADING), first = firstWords(d);
          if (h && first && h.contains(first) && wordsOf(h).length <= 160) return { el: h, text: wordsOf(h) };
          return { el: null, text: '' };
        };
        // (every button of its own, in its order, in its web components' trees too: a close button — an icon named
        // Close, or a ×, with no words of its own — told apart)
        var buttonsOf = function (d) {
          var found = [];
          var visit = function (root) {
            [].forEach.call(root.querySelectorAll(BTN), function (b) { if (found.indexOf(b) < 0) found.push(b); });
            [].forEach.call(root.querySelectorAll('*'), function (el) { if (el.shadowRoot) visit(el.shadowRoot); });
          };
          visit(d);
          if (d.shadowRoot) visit(d.shadowRoot);
          var out = [];
          found.forEach(function (b) {
            if (!b.getClientRects().length || (b.parentElement && b.parentElement.closest(BTN))) return;
            var cs = styleOf(b);
            if (cs.visibility === 'hidden' || cs.display === 'none') return;
            var words = String(b.innerText || b.value || '').replace(/\s+/g, ' ').trim();
            var named = String(b.getAttribute('aria-label') || b.getAttribute('title') || '').replace(/\s+/g, ' ').trim();
            var close = (!words || /^[×✕✖xX]$/.test(words)) && /close|dismiss|×|✕/i.test(named + ' ' + words);
            var label = close ? 'Close' : (words || named);
            if (!label) return;
            var cls = String(b.className && b.className.baseVal !== undefined ? b.className.baseVal : b.className || '');
            out.push({ el: b, label: label.slice(0, 60), close: close,
              primary: /(contained|raised|button-primary|btn-primary|__primary|-primary)/i.test(cls) && !/flat|outlined/i.test(cls),
              enabled: !b.disabled && b.getAttribute('aria-disabled') !== 'true' });
          });
          return out;
        };
        // (a formula's TeX, from wherever it is kept beside what was drawn)
        var texOf = function (m) {
          var a = m.getAttribute('data-latex') || m.getAttribute('data-tex');
          if (a) return { tex: a, display: false };
          var shows = m.matches('.MathJax_Display, .MathJax_SVG_Display, .katex-display') || (m.tagName.toLowerCase() === 'math' && m.getAttribute('display') === 'block');
          // (MathJax 2: the formula's own script, beside what it drew)
          var f = m.matches('.MathJax_Display, .MathJax_SVG_Display') ? (m.querySelector('[id$="-Frame"]') || m) : m;
          var root = m.getRootNode().getElementById ? m.getRootNode() : m.ownerDocument;
          var s = f.id && /-Frame$/.test(f.id) ? root.getElementById(f.id.replace(/-Frame$/, '')) : null;
          if (s && /^math\/tex/i.test(s.type || '')) return { tex: s.textContent, display: shows || /mode\s*=\s*display/i.test(s.type) };
          // (MathJax 3: the formula it typeset there)
          try {
            var doc = window.MathJax && window.MathJax.startup && window.MathJax.startup.document;
            var items = doc && doc.getMathItemsWithin ? doc.getMathItemsWithin(m) : [];
            if (items && items.length && /tex/i.test((items[0].inputJax && items[0].inputJax.name) || '')) return { tex: items[0].math, display: !!items[0].display };
          } catch (e) {}
          // (KaTeX, and MathML with its TeX beside it)
          var ann = m.querySelector('annotation[encoding="application/x-tex"]');
          if (ann) return { tex: ann.textContent, display: shows };
          // (MathQuill's own copy of its LaTeX)
          var mq = m.matches('.mq-math-mode') ? m.querySelector('.mq-selectable') : null;
          if (mq) return { tex: mq.textContent.replace(/^\$+|\$+$/g, ''), display: false };
          return null;
        };
        // (its body: all of it but its title, its buttons left out — theirs are the app's; in a frame of the page's own
        // site, the frame's page read too)
        var readDialog = function (d) {
          var t = titleOf(d), btns = buttonsOf(d);
          var out = { title: t.text, words: 0, maths: 0, pics: 0, live: '', loading: false };
          var links = [];
          // (typeset by the app with the KaTeX it carries: \( \) in a line, \[ \] on a line of its own)
          var formula = function (tex, display) {
            out.maths++;
            tex = String(tex).trim();
            return display ? '<p>\\[' + esc(tex) + '\\]</p>' : '\\(' + esc(tex) + '\\)';
          };
          // (a copy with nothing in it that runs)
          var clean = function (c) {
            [].forEach.call(c.querySelectorAll('script, foreignObject, annotation, annotation-xml'), function (x) { x.remove(); });
            [c].concat([].slice.call(c.querySelectorAll('*'))).forEach(function (x) { [].slice.call(x.attributes).forEach(function (a) { if (/^on/i.test(a.name)) x.removeAttribute(a.name); }); });
            return c;
          };
          // (a drawing as itself, with what it draws from elsewhere in the page — a sprite sheet's symbol — brought in)
          var drawing = function (n, r) {
            var c = clean(n.cloneNode(true)), ids = {}, defs = null, root = n.getRootNode();
            [].forEach.call(c.querySelectorAll('[id]'), function (x) { ids[x.id] = 1; });
            [].forEach.call(c.querySelectorAll('use'), function (u) {
              var h = u.getAttribute('href') || u.getAttribute('xlink:href') || '';
              if (h.charAt(0) !== '#' || ids[h.slice(1)]) return;
              var src = (root.getElementById ? root.getElementById(h.slice(1)) : null) || document.getElementById(h.slice(1));
              if (!src) return;
              if (!defs) { defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs'); c.insertBefore(defs, c.firstChild); }
              defs.appendChild(clean(src.cloneNode(true)));
              ids[h.slice(1)] = 1;
            });
            c.setAttribute('width', String(Math.round(r.width)));
            c.setAttribute('height', String(Math.round(r.height)));
            if (r.width >= 40 && r.height >= 40) c.setAttribute('class', 'simpl-drawing');
            out.pics++;
            return c.outerHTML;
          };
          var kids = function (n) {
            var list = n.shadowRoot ? n.shadowRoot.childNodes : (n.tagName === 'SLOT' ? n.assignedNodes({ flatten: true }) : n.childNodes), s = '';
            for (var i = 0; i < list.length; i++) s += walk(list[i]);
            return s;
          };
          var walk = function (n) {
            if (n.nodeType === 3) { var w = n.data.trim(); if (w) out.words += w.split(/\s+/).length; return esc(n.data); }
            if (n.nodeType !== 1 || n === t.el) return '';
            var tag = n.tagName.toLowerCase();
            if (/^(script|style|template|noscript|link|meta|title)$/.test(tag)) {
              // (MathJax 2's formula not set yet: its script is all there is of it)
              var root = n.getRootNode().getElementById ? n.getRootNode() : n.ownerDocument;
              if (tag === 'script' && /^math\/tex/i.test(n.type || '') && !(n.id && root.getElementById(n.id + '-Frame'))) return formula(n.textContent, /mode\s*=\s*display/i.test(n.type));
              return '';
            }
            if (n.matches(BTN)) return '';
            var cs = styleOf(n);
            if (cs.display === 'none') return '';
            if (n.matches(MATHS)) {
              var f = texOf(n);
              if (f && f.tex.trim()) return formula(f.tex, f.display);
              var ml = n.matches('math') ? n : n.querySelector('math');
              if (ml) { out.maths++; return clean(ml.cloneNode(true)).outerHTML; }
              out.live = out.live || 'a formula that could not be read';
              return '';
            }
            if (cs.visibility === 'hidden' || +cs.opacity === 0 || n.matches('.MathJax_Preview, .MJX_Assistive_MathML, .sr-only, .visually-hidden, .screenreader-only')) return '';
            var r = n.getBoundingClientRect();
            if ((r.width <= 1 || r.height <= 1) && cs.position === 'absolute') return '';
            var big = r.width >= 2 && r.height >= 2;
            if (tag === 'img') {
              if (!n.complete) out.loading = true;
              var src = n.currentSrc || n.src || '';
              if (n.complete && !n.naturalWidth) return n.alt ? esc(n.alt) : '';
              if (!big) return '';
              if (!/^(https?:|data:image\/)/i.test(src)) { out.live = out.live || 'a picture that could not be read'; return ''; }
              out.pics++;
              return '<img src="' + esc(src) + '" width="' + Math.round(r.width) + '" height="' + Math.round(r.height) + '"' + (n.alt ? ' alt="' + esc(n.alt) + '"' : '') + (r.height <= 28 && cs.display.indexOf('inline') === 0 ? ' class="simpl-inline"' : '') + '>';
            }
            if (tag === 'svg') return big ? drawing(n, r) : '';
            if (tag === 'canvas') {
              if (!big) return '';
              try { var u = n.toDataURL('image/png'); out.pics++; return '<img class="simpl-drawing" src="' + u + '" width="' + Math.round(r.width) + '" height="' + Math.round(r.height) + '">'; }
              catch (e) { out.live = out.live || 'a drawing that could not be read'; return ''; }
            }
            if (/^(video|audio|object|embed)$/.test(tag)) { out.live = out.live || (tag === 'audio' ? 'a sound' : 'a video'); return ''; }
            if (/^(input|select|textarea)$/.test(tag)) { if (!/^hidden$/i.test(n.type || '')) out.live = out.live || 'a form'; return ''; }
            if (tag === 'iframe') {
              var body = null;
              try { body = n.contentDocument && n.contentDocument.body; } catch (e) {}
              if (!body) { if (big) out.live = out.live || 'a frame of another site\u2019s'; return ''; }
              return '<div>' + kids(body) + '</div>';
            }
            if (tag === 'br') return '<br>';
            if (tag === 'hr') return '<hr>';
            var inner = kids(n);
            if (tag === 'a' && n.getAttribute('href')) { links.push(n); return '<a href="simpl-link:' + (links.length - 1) + '">' + inner + '</a>'; }
            if (!inner.replace(/<[^>]*>|&nbsp;|\s/g, '') && !/<(img|hr|br|svg|math)\b/i.test(inner)) return /\s/.test(inner) ? ' ' : '';
            var name = KEEP_TAGS.test(tag) ? tag : cs.display === 'list-item' ? 'li' : /^(block|flex|grid|table|flow-root)/.test(cs.display) ? 'div' : 'span';
            // (bold and italic as Alta's own styles make them)
            var up = n.parentElement ? styleOf(n.parentElement) : null, flat = !/<(div|p|ul|ol|table|h[1-6]|blockquote|pre)\b/.test(inner);
            if (flat && !/^(b|strong|h[1-6]|th)$/.test(name) && +cs.fontWeight >= 600 && up && +up.fontWeight < 600) inner = '<b>' + inner + '</b>';
            if (flat && !/^(i|em)$/.test(name) && cs.fontStyle === 'italic' && up && up.fontStyle !== 'italic') inner = '<i>' + inner + '</i>';
            if (name === 'span') return inner;
            return '<' + name + ((name === 'td' || name === 'th') && n.colSpan > 1 ? ' colspan="' + n.colSpan + '"' : '') + '>' + inner + '</' + name + '>';
          };
          out.html = kids(d).trim();
          // (still loading: a spinner or a busy mark in it, or words saying so — never MathJax at work, whose TeX is read)
          out.busy = [].some.call(d.querySelectorAll(BUSY), function (b) { return shown(b) && !b.closest('.MathJax_Processing, .MathJax, mjx-container'); })
            || (function (w) { return w.length < 120 && /^(loading|please wait|one moment|just a moment|preparing)\b/.test(w); })(norm(d.innerText || ''));
          out.buttons = btns.map(function (b) { return { label: b.label, primary: b.primary, enabled: b.enabled, close: b.close }; });
          dialogBtns = btns.map(function (b) { return b.el; });
          dialogLinks = links;
          return out;
        };
        var DIALOG_CSS = 'html[data-simpl-native-dialog] [data-simpl-dialog] { visibility: hidden !important; }\n';
        var dialogStyle = function () {
          var st = document.getElementById('simpl-dialog');
          if (!st) { st = document.createElement('style'); st.id = 'simpl-dialog'; (document.head || document.documentElement).appendChild(st); }
          if (st.textContent !== DIALOG_CSS) st.textContent = DIALOG_CSS;
        };
        // (given back to the page: shown there as Alta draws it, Alta's page as it is, in sight, until it closes — the app
        // told, and why, for its Error Logs)
        var giveBack = function (why) {
          if (!dialogEl) return false;
          var first = !dialogInPage, d = dialogEl;
          dialogInPage = true;
          document.documentElement.removeAttribute('data-simpl-native-dialog');
          if (first) post({ kind: 'dialog', open: true, inPage: true, title: unhidden(function () { return titleOf(d).text; }).slice(0, 200), why: why });
          schedule(0);
          return true;
        };
        var dialogTick = function () {
          // (1.3.83) Show Browser: Alta's pop-ups are its page's own, shown there as Alta draws them — one the app was
          // showing given back
          if (window.__simplBrowser) {
            if (dialogEl || dialogSaid || dialogInPage) {
              if (dialogHolder && dialogHolder.isConnected) dialogHolder.removeAttribute('data-simpl-dialog');
              dialogEl = null; dialogHolder = null; dialogSaid = ''; dialogRead = ''; dialogWaiting = 0; dialogInPage = false;
              dialogBtns = []; dialogLinks = [];
              document.documentElement.removeAttribute('data-simpl-native-dialog');
              post({ kind: 'dialog', open: false });
            }
            return;
          }
          var d = dialogNow();
          if (!d) {
            if (dialogEl || dialogSaid || dialogInPage) {
              var was = dialogInPage;
              if (dialogHolder && dialogHolder.isConnected) dialogHolder.removeAttribute('data-simpl-dialog');
              dialogEl = null; dialogHolder = null; dialogSaid = ''; dialogRead = ''; dialogWaiting = 0; dialogInPage = false;
              dialogBtns = []; dialogLinks = [];
              document.documentElement.removeAttribute('data-simpl-native-dialog');
              post({ kind: 'dialog', open: false });
              if (was) schedule(); // (the page laid out again as it was before the pop-up)
            }
            return;
          }
          if (d !== dialogEl) {
            if (dialogHolder && dialogHolder.isConnected) dialogHolder.removeAttribute('data-simpl-dialog');
            dialogEl = d; dialogHolder = holderOf(d); dialogDirty = Date.now(); dialogSaid = ''; dialogRead = ''; dialogWaiting = 0; dialogReadAt = 0;
          }
          // (given back to the page: Alta's own there until it closes)
          if (dialogInPage) return;
          dialogStyle();
          if (!dialogHolder.hasAttribute('data-simpl-dialog')) dialogHolder.setAttribute('data-simpl-dialog', '');
          // (once passed, read again when the page has changed since — a next step — or every two seconds)
          if (dialogSaid && lastChange < dialogReadAt && Date.now() - dialogReadAt < 2000) return;
          dialogReadAt = Date.now();
          var r = unhidden(function () { return readDialog(d); });
          var sig = JSON.stringify([r.title, r.html, r.buttons, r.live]);
          if (sig !== dialogRead) { dialogRead = sig; dialogDirty = Date.now(); }
          if (sig === dialogSaid) { dialogWaiting = 0; return; }
          if (!dialogWaiting) dialogWaiting = Date.now();
          var settled = Date.now() - dialogDirty >= 350 && !r.busy && !r.loading && !unhidden(function () { return fading(dialogHolder) || opacityOf(d) < 0.97; });
          if (!settled && Date.now() - dialogWaiting < 3000) return;
          dialogSaid = sig;
          dialogWaiting = 0;
          var why = r.live ? 'it holds ' + r.live
            : !(r.words || r.maths || r.pics) ? 'nothing in it could be read'
            : !settled && (r.busy || r.loading) ? 'it was still loading after three seconds'
            : r.html.length > 400000 ? 'it is too long' : '';
          if (why) { giveBack(why); return; }
          post({ kind: 'dialog', open: true, title: r.title.slice(0, 200), html: r.html, buttons: r.buttons, words: r.words, maths: r.maths, pics: r.pics, ready: settled ? 'settled' : 'after 3 s' });
        };
        setInterval(dialogTick, 300);
        window.__simplDialog = {
          // (hidden in the page while the app shows it as its own)
          hide: function () { if (!dialogEl || dialogInPage) return false; document.documentElement.setAttribute('data-simpl-native-dialog', ''); return true; },
          // (given back to the page — the sheet's Show in Alta's Page, or the sheet put away without a button)
          giveBack: function (why) { return giveBack(why || 'the student asked for it'); },
          press: function (i) {
            var b = dialogBtns[i];
            if (!b || !b.isConnected || b.disabled || b.getAttribute('aria-disabled') === 'true') return false;
            b.click();
            dialogDirty = Date.now();
            dialogReadAt = 0;
            setTimeout(dialogTick, 60);
            return true;
          },
          // (a link in its body: one to another site or a window of its own is the app's to open, in the browser; any
          // other is followed in Alta's page, as Alta follows it)
          link: function (i) {
            var a = dialogLinks[i];
            if (!a || !a.isConnected) return null;
            if (a.target === '_blank' || (a.origin && a.origin !== location.origin)) return a.href;
            a.click();
            return '';
          }
        };
        var tick = function () {
          timer = null; changeSince = 0;
          dialogTick();
          // (1.3.83) a pop-up of Alta's given back to its page: Alta's page as it is, in sight, under it until it closes —
          // and so with Show Browser on: Knewton Alta's own page as Alta draws it, nothing hidden, never out of sight
          if (dialogInPage || window.__simplBrowser) {
            badSince = 0; loadingSince = 0; steadySince = 0;
            if (steadyTimer) { clearTimeout(steadyTimer); steadyTimer = null; }
            unmark();
            publish();
            reveal(window.__simplBrowser ? 'browser' : 'dialog');
            return;
          }
          keepDialogs();
          // (1.3.106) the page read afresh — its marks lifted for the moment, a style pass over the whole page each way —
          // only when something may have changed what it holds: anything but a change outside the parts shown alone (Alta's
          // header, its chat), after which the last reading still stands; and every two seconds all the same
          var fresh = readAgain || !lastRead || !document.documentElement.hasAttribute('data-simpl-q') || Date.now() - readAt > 2000
            || keptNow.some(function (k) { return !k.isConnected; });
          readAgain = false;
          var r = fresh ? read() : lastRead;
          if (fresh) { lastRead = r; readAt = Date.now(); }
          // (1.3.70) a loading page, or parts that are all still loading: out of sight, waited for (twenty seconds at
          // most, then shown as it is)
          var loadingNow = r.keep ? r.keep.every(function (k) { return busy(k) && !k.querySelector('.lrn_widget, .lrn_qr, .learnosity-item') && norm(k.innerText || '').length < 200; })
            : (r.state !== 'overview' && busy(document.body));
          if (loadingNow) {
            if (!loadingSince) loadingSince = Date.now();
            if (Date.now() - loadingSince < 20000) {
              if (steadyTimer) { clearTimeout(steadyTimer); steadyTimer = null; }
              steadySince = 0; badSince = 0;
              veilAgain();
              schedule(250);
              return;
            }
          } else loadingSince = 0;
          if (r.state === 'question' || r.state === 'lesson') {
            badSince = 0; wasQuestion = true;
            // (Alta's pop-ups and MathJax's helpers kept with it; its Feedback — a report to Alta — and its Content
            // attribution link put away)
            // (1.3.51: and the page's sprite sheets — an <svg> of symbols its pictures draw from, a pop-up's among them)
            // (1.3.83: each pop-up with what holds it — its backdrop, a web component's host — however it is built)
            var extra = all(DIALOG_SEL).map(holderOf).concat(dialogHosts, all('body > .MuiModal-root, body > [role="presentation"], #MathJax_Hidden, #MathJax_Font_Test, #MathJax_Message'))
              .concat(all('svg').filter(function (g) { return g.querySelector('symbol, defs'); }))
              .filter(function (e, i, list) { return list.indexOf(e) === i && e.isConnected && e !== document.body && e !== document.documentElement && !r.keep.some(function (k) { return k.contains(e); }); });
            apply(r.keep.concat(extra));
            pics(r.keep);
            r.keep.forEach(function (k) { [].forEach.call(k.querySelectorAll('button'), function (b) { if (/^(feedback|content attribution\b.*)$/.test(norm(b.innerText || b.textContent || b.getAttribute('aria-label')))) mark(b, 'data-simpl-hide'); }); });
            report(r.state, r);
            publish(!fresh);
            revealSteady(r.state);
            return;
          }
          if (r.state === 'overview') {
            badSince = 0; wasQuestion = false; unmark(); report('overview', r); publish();
            // (the cover read first, so the app's start screen is up before the page would show)
            if (window.__simplCoverNow) window.__simplCoverNow();
            revealSteady('overview');
            return;
          }
          // (not recognised: a moment's grace — Alta between one part and the next — then its page as it is)
          if (!badSince) badSince = Date.now();
          // (1.3.62) on Alta's learn page between parts, out of sight (the app's Loading) for as long as Alta may take,
          // never Alta's page as it is in the meantime
          var between = !!document.querySelector('[class*="learn-flow-body"]');
          // (a part about to be shown, gone again: not shown after all)
          if (steadyTimer) { clearTimeout(steadyTimer); steadyTimer = null; }
          steadySince = 0;
          var wait = between ? 8000 : (wasQuestion ? 1500 : 8000);
          // (maths drawn whole: and only while that grace lasts — once it is over and the page shown as it is, by its own end
          // or the app's last resort, it is not put out of sight again, then shown again, at each look until a part comes)
          if (between && Date.now() - badSince < wait) veilAgain();
          // (maths drawn whole: looked at again every 300 ms meanwhile — a part Alta shows by its class or its style alone is
          // seen as it comes, where before it waited out the grace)
          if (Date.now() - badSince < wait) { schedule(Math.min(wait - (Date.now() - badSince) + 20, 300)); return; }
          unmark();
          report(r.state, r);
          publish();
          reveal('as it is');
        };
        // (1.3.106) `outside`: for a change outside the parts shown alone — the page need not be read afresh for it
        var lastRead = null, readAt = 0, readAgain = true;
        var schedule = function (ms, outside) {
          if (!outside) readAgain = true;
          if (timer) clearTimeout(timer);
          var d = typeof ms === 'number' ? ms : 120;
          dueAt = Date.now() + d; timer = setTimeout(tick, d);
        };
        // (1.3.68) the question shown once Alta has finished drawing it — nothing changed for a moment (its maths set, its
        // answer box built, its pictures in) — or after two and a half seconds at most: the app's skeleton meanwhile.
        // (1.3.101) Shown on readiness rather than on a long silence: nothing in it still loading (its maths, its
        // pictures, Learnosity's box) and nothing in the part shown changed for 100 ms — a layout and two frames, where
        // 1.3.68–1.3.100 waited 350 ms — Alta's own round it (its header's bars, the chat button, a celebration) no longer
        // counted once a part is kept (`keptChange`; `lastChange` is every change, which a pop-up's next step is read by).
        // At most a second and a half when only that quiet is missing, two and a half while something in it is still
        // being drawn, four while it says it is loading (1.3.70) — counted from the first look, and never lowered by a later
        // one (`steadyCap`, 1.3.101): a box, formula or picture that comes in after the first second and a half is still
        // given its 100 ms of quiet, never shown at once, half drawn.
        var lastChange = Date.now(), keptChange = Date.now(), steadyTimer = null, steadySince = 0, steadyCap = 0;
        // (1.3.101) MathJax 3's typesetting watched: its startup promise (each typeset chains a new one onto it) settled
        var mj3 = null, mj3Done = true;
        var watchMathJax = function () {
          var p = window.MathJax && window.MathJax.startup && window.MathJax.startup.promise;
          if (!p || p === mj3 || typeof p.then !== 'function') return;
          mj3 = p; mj3Done = false;
          var done = function () { if (mj3 === p) mj3Done = true; };
          try { p.then(done, done); } catch (e) { mj3Done = true; }
        };
        // (1.3.101) the maths in the parts kept not set yet: a formula of MathJax 2's with nothing drawn beside its script
        // (not queued yet — it adds no MathJax_Processing until it is), MathJax 3 typesetting, or its TeX still in the words
        var mathsPending = function () {
          if ([].some.call(document.querySelectorAll('section[data-simpl-keep] script[type^="math/tex"]'), function (s) { return !(s.id && document.getElementById(s.id + '-Frame')); })) return true;
          if (!(window.MathJax && window.MathJax.startup)) return false;
          watchMathJax();
          if (!mj3Done) return true;
          return [].some.call(document.querySelectorAll('section[data-simpl-keep]'), function (k) { return /\\\(|\\\[/.test(k.textContent || ''); });
        };
        // (1.3.101) a question's answer box drawn: Learnosity's own widget in it, with a size — its embed alone
        // (`.learnosity-item`, an empty learnosity__ box) is there before Learnosity has fetched or drawn anything
        var answerBox = function () {
          return [].some.call(document.querySelectorAll('section[data-simpl-keep] .lrn_widget, section[data-simpl-keep] .lrn_qr, section[data-simpl-keep] [class*="lrn_response"]'), function (w) { var b = w.getBoundingClientRect(); return b.width > 0 && b.height > 0; });
        };
        var revealSteady = function (state) {
          if (!steadySince) { steadySince = Date.now(); steadyCap = 0; }
          var quiet = Date.now() - keptChange;
          var spinning = state === 'overview' ? busy(document.body) : [].some.call(document.querySelectorAll('[data-simpl-keep]'), busy);
          var pending = spinning || !!document.querySelector('.MathJax_Processing, .lrn_loading, [class*="lrn-loading"]')
            || [].some.call(document.querySelectorAll('[data-simpl-keep] img'), function (i) { return !i.complete; })
            || (state === 'question' && !answerBox()) || (state !== 'overview' && mathsPending());
          var ready = quiet >= 100 && !pending;
          steadyCap = Math.max(steadyCap, spinning ? 4000 : pending ? 2500 : 1500);
          if (ready || Date.now() - steadySince > steadyCap) {
            steadySince = 0;
            remeasure(); // (maths drawn whole)
            reveal(state, { quiet: quiet, capped: !ready });
            return;
          }
          if (steadyTimer) clearTimeout(steadyTimer);
          steadyTimer = setTimeout(function () { steadyTimer = null; revealSteady(state); }, pending ? 120 : Math.max(20, 100 - quiet));
        };
        // (1.3.101) a change in the part kept (or in Alta's learn flow round it, where the next part comes), or a kept
        // part taken away — the page itself, before a part is kept
        var inKept = function (list) {
          if (!keptNow.length || keptNow.some(function (k) { return !k.isConnected; })) return true;
          return list.some(function (m) {
            var n = m.target;
            if (n && n.nodeType !== 1) n = n.parentNode;
            return !!(n && n.closest && n.closest('[data-simpl-keep], [class*="learn-flow-body"]'));
          });
        };
        // (the page looked at again 120 ms after the last change, as before — put off by later changes in the part shown
        // for 300 ms at most, so a page that never stops changing is still looked at; a change elsewhere never puts it off,
        // and once a part is kept is looked at no oftener than every 400 ms — Alta's chrome changing all the time never has
        // the page read over and over while Alta draws its next part)
        var changed = function (mine) {
          var now = Date.now();
          lastChange = now;
          if (mine) keptChange = now;
          if (!changeSince) changeSince = now;
          watchMathJax();
          if (mine) { if (!timer || dueAt - now > 120 || now - changeSince < 300) schedule(); else readAgain = true; }
          else if (!timer || dueAt - now > 400) schedule(400, true);
          // (1.3.106) the panels and buttons told again for a change in the parts shown alone
          if (mine && keptNow.length) publishSoon(true);
        };
        // (maths drawn whole) a part of Alta's put in while the question is shown alone — the next one, before the page is
        // looked at again — laid out at once where it is (one Alta itself hides left as it is): what holds it marked as the
        // kept parts' ancestors are, in the same moment it is put in, before Alta or Learnosity can draw in it (the next look
        // marks the page afresh)
        var PART_SEL = 'section[class*="compound__compound"]', sections = document.getElementsByTagName('section');
        var layOut = function () {
          if (!document.documentElement.hasAttribute('data-simpl-q')) return;
          for (var i = 0; i < sections.length; i++) {
            var sec = sections[i];
            if (sec.hasAttribute('data-simpl-keep') || sec.hidden || !sec.matches(PART_SEL) || getComputedStyle(sec).display === 'none') continue;
            for (var x = sec.parentElement; x && x !== document.documentElement && !x.hasAttribute('data-simpl-path'); x = x.parentElement) mark(x, 'data-simpl-path');
          }
        };
        try {
          new MutationObserver(function (list) {
            if (list.some(function (m) { return m.addedNodes.length > 0; })) layOut();
            changed(inKept(list));
          }).observe(document, { childList: true, subtree: true, characterData: true });
        } catch (e) {}
        // (maths drawn whole) a part shown or put away by its class or its style alone — a part of Alta's, its learn flow,
        // what holds the flow, or a wrapper in the flow round a part — has the page looked at again 120 ms later (never put
        // off by more of them): before, only a part put in or taken out did, and a question Alta showed by its class waited
        // out the grace between parts (eight seconds), or for good while the last one was still kept
        var flowEl = null;
        var turned = function (t) {
          if (!t || t.nodeType !== 1) return false;
          if (t.matches(PART_SEL)) return true;
          if (!flowEl || !flowEl.isConnected) flowEl = document.querySelector('[class*="learn-flow-body"]');
          return !!flowEl && (t.contains(flowEl) || (flowEl.contains(t) && !t.closest(PART_SEL)));
        };
        try {
          new MutationObserver(function (list) {
            if (!list.some(function (m) { return turned(m.target); })) return;
            if (!timer || dueAt - Date.now() > 120) schedule();
          }).observe(document, { attributes: true, subtree: true, attributeFilter: ['class', 'style', 'hidden', 'aria-hidden'] });
        } catch (e) {}
        // (maths drawn whole) the page never left waiting: out of sight with nothing due to look at it again, or a part kept
        // gone or put out of sight by Alta (the next shown in its place by its class), it is looked at again at once — what
        // the student's Light then Dark did for it before
        var watch = function () {
          if (timer || steadyTimer) return;
          if ((veils && !showing && !dialogInPage && !window.__simplBrowser) || cards().some(function (k) { return !k.isConnected || !shown(k); })) schedule(0);
        };
        // (maths drawn whole) MathQuill's brackets and √ — Learnosity's answer box, its formulas — drawn flat: sized to what
        // they held as they were drawn, which out of sight was nothing (MathQuill sizes them once, as it draws them, and again
        // only as the student types in them). Each is sized again as MathQuill sizes it once what it holds is laid out — by
        // MathQuill itself where the page has it to hand (its reflow, for a formula drawn before it was in sight) — and the
        // app told, for its Error Logs: in the parts kept, and in Alta's page as it is (Show Browser) too.
        var mqFlat = function (el) {
          var sc = /scale\(\s*(-?[\d.]+(?:e-?\d+)?)\s*,\s*(-?[\d.]+(?:e-?\d+)?)\s*\)/.exec(el.style.transform || '');
          return !!sc && (+sc[1] <= 0.05 || +sc[2] <= 0.05);
        };
        var emsOf = function (el, h) { var f = parseFloat(getComputedStyle(el).fontSize); return f > 0 ? h / f : 0; };
        var to3 = function (v) { return Math.round(v * 1000) / 1000; };
        var remeasure = function () {
          var flat = [].filter.call(document.querySelectorAll('.mq-scaled'), mqFlat);
          if (!flat.length) return;
          var byMQ = 0, byUs = 0;
          try {
            var MQ = window.MathQuill && window.MathQuill.getInterface ? window.MathQuill.getInterface(2) : null;
            if (MQ) flat.forEach(function (sc) { var root = sc.closest('.mq-math-mode'), f = root ? MQ(root) : null; if (f && f.reflow) f.reflow(); });
          } catch (e) {}
          flat.forEach(function (sc) {
            if (!mqFlat(sc)) { byMQ++; return; }
            if (sc.classList.contains('mq-sqrt-prefix')) {
              // (a root's √: as tall as what it holds, less a tenth)
              var stem = sc.nextElementSibling, h = stem ? emsOf(stem, stem.clientHeight) : 0;
              if (h > 0) { sc.style.transform = 'scale(1, ' + to3(h - 0.1) + ')'; byUs++; }
              return;
            }
            // (a bracket, either side of what it holds: a fifth taller than it, and wider by a fifth of that, at most)
            var inner = sc.parentElement ? [].filter.call(sc.parentElement.children, function (k) { return !k.classList.contains('mq-scaled'); })[0] : null;
            var h2 = inner ? emsOf(inner, inner.offsetHeight) : 0;
            if (h2 > 0) { sc.style.transform = 'scale(' + to3(Math.min(1 + 0.2 * (h2 - 1), 1.2)) + ', ' + to3(1.2 * h2) + ')'; byUs++; }
          });
          if (byMQ + byUs) post({ kind: 'remeasured', count: byMQ + byUs, mathquill: byMQ > 0 });
        };
        // (1.3.83) Show Browser turned on or off: the page laid out again at once — as it is, or the app's way
        window.__simplBrowserNow = function () { dialogTick(); schedule(0); return true; };
        // (maths drawn whole) the app's last resort, its skeleton still over the page 26 seconds on (the page script's own
        // waits, between parts and then loading, can run back to back): Alta's page shown as it is by the page script
        // itself, as its own waits end — those waits counted as over, the focus lifted, the app told why — so that what
        // the page script knows (`showing`, the focus, the page's look) stays in step, and the page can still be put out
        // of sight between parts after
        window.__simplAsIsNow = function () {
          var past = Date.now() - 60000;
          badSince = past; loadingSince = past; steadySince = 0;
          if (steadyTimer) { clearTimeout(steadyTimer); steadyTimer = null; }
          unmark();
          told = ''; // (said again, whatever was said last: why it is shown as it is, and what it holds)
          report('unknown', { reason: 'still out of sight after 26 seconds' });
          publish();
          reveal('as it is');
          return true;
        };
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
          // (a cover: its header, or a Start beside the objectives' estimates — never Alta's learn page; 1.3.106: looked
          // for in that order, the cheapest first — the learn page, the header, the words, and only then a Start)
          if (document.querySelector('[class*="learn-flow-body"], section[class*="compound__compound"]')) return null;
          var head = document.querySelector('[data-test="cover-header"]');
          if (!head && !mentions(document.body, /estimated/i)) return null;
          var go = startEl();
          if (!head && !(go && /estimated\s+\d+/i.test(cs(document.body && document.body.innerText)))) return null;
          var o = { kind: 'cover', name: '', details: [], note: '', start: go ? cs(go.innerText || go.textContent) : '', review: !!reviewEl(), topics: [] };
          var h1 = (head && head.querySelector('h1')) || document.querySelector('h1');
          o.name = h1 ? cs(h1.innerText || h1.textContent) : '';
          if (head) {
            [].forEach.call(head.querySelectorAll('[class*="subheadingContainer"]'), function (c) {
              var k = c.querySelector('h2, h3'), v = c.querySelector('[class*="headingDetails"]');
              // (a detail's lines — Monday, Oct 5 / 11:59pm PDT — each its own paragraph, joined by a space)
              var ps = [].map.call(v.querySelectorAll('p'), function (q) { return cs(q.innerText || q.textContent); }).filter(Boolean);
              // (1.3.60) the date and time are one value; a line after them of another kind (Grade's "Last updated…") a
              // note of its own under it
              var two = /^\d{1,2}:\d{2}/.test(ps[1] || '');
              var main = ps.length ? ps.slice(0, two ? 2 : 1).join(' ') : cs((v.innerText || v.textContent || '').split('\n').join(' '));
              var rest = ps.length ? ps.slice(two ? 2 : 1).join(' ') : '';
              if (k && v) o.details.push({ label: cs(k.innerText || k.textContent), value: main, sub: rest });
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
              if (el.tagName === 'H3') { topic = { name: cs(el.innerText || el.textContent), estimate: '', low: null, high: null, objectives: [] }; o.topics.push(topic); return; }
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
              if (e) { topic.estimate = e[1] + '\u2013' + e[2] + ' questions'; topic.low = +e[1]; topic.high = +e[2]; }
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
          if (o) begun = true;
          post(stamp(o || { kind: 'cover', none: true }));
        };
        // (1.3.106) looked for every 0.7 s once the page has been shown or its cover read — while it loads, only when
        // Alta's cover header is there (the overview the page script recognises reads the cover itself, at once)
        setInterval(function () { if (begun || document.querySelector('[data-test="cover-header"]')) coverTick(); }, 700);
        window.__simplCoverNow = coverTick;
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
    struct Detail: Decodable, Equatable, Hashable { let label: String; let value: String; var sub: String? }
    struct Objective: Decodable, Equatable, Hashable { let name: String; let mastery: Double? }
    struct Topic: Decodable, Equatable, Hashable {
        let name: String
        let estimate: String
        var low: Double?
        var high: Double?
        let objectives: [Objective]
    }

    /// (1.3.60) Every objective, in order, each with its topic's estimate: the assignment's whole list.
    var list: [AltaOverview.Objective] {
        topics.flatMap { t in t.objectives.map { AltaOverview.Objective(id: $0.name, name: $0.name, low: t.low, high: t.high) } }
    }
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

/// (1.3.70) A small graph in what a question asks, where the page lays it (its own coordinates).
struct AltaPic: Identifiable, Equatable {
    let id: Int
    let rect: CGRect
}

/// (1.3.70) A question's graph, large, in a window of its own over the popup: moved and sized as the student likes,
/// its colours dark or as Alta draws them (the same choice as the question's) — (1.3.79) as Alta draws them, on white,
/// the question being light; (1.3.83) dark again, with its Light, when the student picks Dark for Alta's side — and put
/// away when the question is answered or gone, or the popup closed.
@MainActor
final class AltaGraphWindow: NSObject, NSWindowDelegate {
    static let shared = AltaGraphWindow()
    private var panel: NSPanel?
    private let model = Model()

    final class Model: ObservableObject {
        @Published var image: NSImage?
        @Published var light = false
        /// (1.3.83) Alta's side of the popup dark (the student's Dark): the window dark with it.
        @Published var dark = false
        weak var session: AltaSession?
    }

    /// (1.3.122) Macmillan's pictures open here too (no Alta session: on white, as Macmillan draws them).
    func show(_ image: NSImage, title: String, session: AltaSession?) {
        model.image = image
        model.session = session
        model.light = session?.picsLight ?? true
        model.dark = session?.pageDark ?? false
        if let panel {
            panel.title = title
            panel.orderFront(nil)
            return
        }
        let fit = NSScreen.main?.visibleFrame.size ?? CGSize(width: 1200, height: 800)
        var size = image.size
        let k = min(1, (fit.width * 0.6) / max(size.width, 1), (fit.height * 0.6) / max(size.height, 1))
        size = CGSize(width: max(320, size.width * k + 32), height: max(260, size.height * k + 80))
        let p = NSPanel(contentRect: NSRect(origin: .zero, size: size), styleMask: [.titled, .closable, .resizable, .utilityWindow, .fullSizeContentView], backing: .buffered, defer: false)
        p.title = title
        p.titlebarAppearsTransparent = true
        p.isFloatingPanel = true
        p.level = .floating
        p.hidesOnDeactivate = true
        p.isMovableByWindowBackground = true
        p.isReleasedWhenClosed = false
        p.delegate = self
        // (1.3.79) the graph as Alta draws it, on white, the question's page being light whatever the app's appearance
        // (its window's bar keeps the app's) — (1.3.83) or dark with it, when the student picks Dark
        p.contentView = NSHostingView(rootView: AltaGraphRoot(model: model))
        p.center()
        p.orderFront(nil)
        panel = p
    }

    func light(_ on: Bool) { model.light = on }

    func dark(_ on: Bool) { model.dark = on }

    func close() {
        guard let p = panel else { return }
        panel = nil
        p.delegate = nil
        p.close()
        model.image = nil
    }

    func windowWillClose(_ notification: Notification) {
        panel = nil
        model.session?.picWindowClosed()
    }
}

/// (1.3.83) The graph's window in the look of Alta's side of the popup: light (1.3.79), or dark when Dark is picked.
private struct AltaGraphRoot: View {
    @ObservedObject var model: AltaGraphWindow.Model

    var body: some View {
        AltaGraphView(model: model).environment(\.colorScheme, model.dark ? .dark : .light)
    }
}

private struct AltaGraphView: View {
    @ObservedObject var model: AltaGraphWindow.Model
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let light = model.light
        ZStack(alignment: .topTrailing) {
            (scheme == .dark && !light ? Color(white: 0.11) : Color.white)
                .ignoresSafeArea()
            if let image = model.image {
                Image(nsImage: image)
                    .resizable()
                    .interpolation(.high)
                    .aspectRatio(contentMode: .fit)
                    .padding(EdgeInsets(top: 36, leading: 14, bottom: 14, trailing: 14))
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            if scheme == .dark, let session = model.session {
                // (dark by default; Light brings back Alta's own colours on white, here and in the question)
                Button { session.setPicsLight(!light) } label: {
                    Label(light ? "Dark" : "Light", systemImage: light ? "moon" : "sun.max")
                }
                .glassButton()
                .controlSize(.small)
                .padding(.top, 6)
                .padding(.trailing, 10)
                .help(light ? "Show the graph in dark colours" : "Show the graph in its own light colours")
            }
        }
        .frame(minWidth: 280, minHeight: 220)
        .animation(Motion.gentle, value: light)
    }
}

/// (1.3.53) One of the buttons in Alta's button bars, whatever it says: shown as the app's own, pressing Alta's.
struct AltaButton: Identifiable, Equatable {
    let id: Int
    let label: String
    let primary: Bool
    let enabled: Bool
}

/// (1.3.51) A pop-up of Alta's, shown as the Mac's own. (1.3.83) Read whole once it has settled: its title, its body as
/// plain HTML drawn as the app's own words (its formulas typeset, its pictures by their addresses) and every one of its
/// buttons — updated in place as it changes (a next step).
struct AltaDialog: Identifiable {
    /// One of its buttons, whatever it says.
    struct Choice: Equatable {
        let label: String
        /// Alta's main one.
        let primary: Bool
        let enabled: Bool
        /// A close button with no words of its own (an ×), named Close.
        let close: Bool
    }

    let id = UUID()
    var title: String
    var html: String
    var buttons: [Choice]
    /// Alta's page's address, which its pictures and links are read against.
    var base: URL
    /// Counted up each time it is passed again (a next step): the sheet's buttons rest from a press until it is.
    var step = 0
}

struct AltaLaunch: Identifiable, Equatable {
    let id = UUID()
    let course: String
    let assignment: String
    let title: String
    var page: String? = nil
    /// (1.3.101) The Canvas assignment's due line ("Due Oct 12 at 11:59 PM"; "" with no due date) when the place that
    /// opened it had it — the Assignment page, the Tools list — so the popup's header asks Canvas for nothing beside the
    /// launch. Nil: asked once the launch is answered (AltaQuiz).
    var dueLine: String? = nil
}

/// (1.3.101) An Alta open, timed from the moment the popup was asked for: the launch asked of Canvas and answered,
/// Alta's first page begun, what it shows first, and the page in sight — one line on the console for every open
/// (`[Simpl] alta: view spare · asked 2 ms · launch 410 ms · page 655 ms · question 790 ms · shown 905 ms (question, quiet
/// 120 ms)`; 1.3.106: the web view's word first),
/// in the screenshot suite's consoles and the Mac's own log alike. The page's moments are its own clock's (`nav`, when
/// it began, and `t`, as the page script posts them), so the time the message takes to come is not counted.
struct AltaTimeline {
    let start = Date()
    /// (1.3.106) The popup's web view: the spare made ahead ("spare"), or made as the popup was asked for, and how long
    /// that took ("made 41 ms").
    var view: String?
    var asked: Double?
    var launch: Double?
    var given = false
    var page: Double?
    var first: (state: String, at: Double)?
    var told = false

    /// Milliseconds from the start to a moment of the page's (`head`: a post with `nav` and `t`), else to now.
    func ms(_ head: [String: Any]? = nil) -> Double {
        if let nav = (head?["nav"] as? NSNumber)?.doubleValue, let t = (head?["t"] as? NSNumber)?.doubleValue, nav > 0 {
            return nav + t - start.timeIntervalSince1970 * 1000
        }
        return Date().timeIntervalSince(start) * 1000
    }

    /// What is known so far, in the line's order.
    var parts: [String] {
        var out: [String] = []
        if let view { out.append("view \(view)") }
        if let asked { out.append("asked \(Int(asked.rounded())) ms") }
        if given { out.append("page given") } else if let launch { out.append("launch \(Int(launch.rounded())) ms") }
        if let page { out.append("page \(Int(page.rounded())) ms") }
        if let first { out.append("\(first.state) \(Int(first.at.rounded())) ms") }
        return out
    }

    /// The line for the page in sight (the page script's `shown`: how, how long its part had been quiet, whether a cap
    /// showed it first, when its DOMContentLoaded came).
    func shownLine(_ head: [String: Any]) -> String {
        let how = head["how"] as? String ?? ""
        var detail = [how.isEmpty ? "shown" : how]
        if let quiet = (head["quiet"] as? NSNumber)?.intValue {
            detail.append(head["capped"] as? Bool == true ? "capped, quiet \(quiet) ms" : "quiet \(quiet) ms")
        }
        if let dcl = (head["dcl"] as? NSNumber)?.doubleValue, let t = (head["t"] as? NSNumber)?.doubleValue, dcl > 0 {
            detail.append("\(Int((t - dcl).rounded())) ms after DOMContentLoaded")
        }
        return (parts + ["shown \(Int(ms(head).rounded())) ms (\(detail.joined(separator: ", ")))"]).joined(separator: " · ")
    }
}

// MARK: - (1.3.106) A web view made ahead

/// (1.3.106) One web view for Alta's popup made ahead, at a quiet moment once the Dashboard is up (or once an Alta
/// assignment's page or the Tools list has said so), for an account that uses Knewton Alta (Engine `hasAlta`): its
/// configuration, its page script and its relay in, and an empty page (about:blank) loaded, so WebKit's process for it is
/// already running when the student opens an assignment. The popup takes it (`take`) instead of making one then — a web
/// view made cold took 150–400 ms, much of it on the main thread as the popup went up — and the next is made only after
/// that popup has closed, at a quiet moment again, never while one is opening.
///
/// It only ever holds that empty page: a web view that has shown anything is never handed on, so no page, script or
/// state of one open (or one account) reaches the next — each open has a web view nobody has used. Its cookies are the
/// app's own web data's, as every Alta popup's always were. It is let go after ten minutes unused, and when the student
/// signs out (the next account makes its own, if it uses Alta); and it is for one school (`host`) only — let go too when
/// another school is chosen (Change School…, which does not sign out), so the new school's is made in its place.
@MainActor
final class AltaSpare {
    /// A web view for Alta's page and the relay its messages come by.
    struct View {
        let web: AltaWebView
        let relay: AltaRelay
    }

    static let shared = AltaSpare()
    /// How long one is kept unused.
    private static let keepFor: UInt64 = 600_000_000_000

    private var spare: (view: View, host: String)?
    /// Its empty page loaded: WebKit's process for it up.
    private var warm = false
    private var watch: NSKeyValueObservation?
    private var making: Task<Void, Never>?
    private var expiry: Task<Void, Never>?

    /// Made ahead at all: always, but in the screenshot suite only when a picture asks (-SimplAltaSpare YES), so the rest
    /// are taken as they were.
    private static var allowed: Bool {
        let d = UserDefaults.standard
        return d.string(forKey: "SimplShotFile") == nil || d.bool(forKey: "SimplAltaSpare")
    }

    /// The spare for an Alta popup about to open at this school, taken — nil when there is none (the popup makes its own).
    func take(host: String) -> View? {
        guard let s = spare, s.host == host else { return nil }
        spare = nil
        watch = nil
        expiry?.cancel()
        expiry = nil
        if OpenClock.verbose { NSLog("[Simpl] alta: the spare web view taken%@", warm ? "" : ", its empty page still loading") }
        warm = false
        return s.view
    }

    /// Made at the next quiet moment — no screen waiting on the school for `quiet` seconds (half a minute at most) and no
    /// Alta popup open — for an account that uses Alta; nothing when one is up or on its way already.
    func prepare(_ engine: Engine, quiet: Double = 1.5) {
        drop(unlessFor: engine.host)
        guard spare == nil, making == nil, Self.allowed else { return }
        making = Task { [weak self, weak engine] in
            var still = 0.0, waited = 0.0
            while still < quiet && waited < 30 {
                try? await Task.sleep(nanoseconds: 250_000_000)
                waited += 0.25
                guard let engine else { break }
                still = engine.waiting == 0 && !AltaSpare.popupOpen(engine) ? still + 0.25 : 0
            }
            guard let self else { return }
            self.making = nil
            guard !Task.isCancelled, let engine, engine.hasAlta, !AltaSpare.popupOpen(engine) else { return }
            self.make(host: engine.host)
        }
    }

    /// (the screenshot suite's -SimplAltaSpare YES) Made now, and waited for until its empty page is loaded (three
    /// seconds at most), before the popup it is for is asked for.
    func makeNow(host: String) async {
        making?.cancel()
        making = nil
        drop(unlessFor: host)
        if spare == nil { make(host: host) }
        for _ in 0..<30 where !warm { try? await Task.sleep(nanoseconds: 100_000_000) }
    }

    /// Let go when it is another school's: the student chose another (Change School…), which does not sign out, so its
    /// Engine never says signed out and the old school's web view would be kept, the new school's never made, for ten
    /// minutes.
    func drop(unlessFor host: String) {
        if let s = spare, s.host != host { drop("another school") }
    }

    /// Let go: signed out, another school, or ten minutes unused.
    func drop(_ why: String) {
        making?.cancel()
        making = nil
        expiry?.cancel()
        expiry = nil
        watch = nil
        guard let s = spare else { return }
        spare = nil
        warm = false
        s.view.web.stopLoading()
        if OpenClock.verbose { NSLog("[Simpl] alta: the spare web view let go (%@)", why) }
    }

    /// An Alta popup up (or on its way) in any window: its session not yet closed.
    private static func popupOpen(_ engine: Engine) -> Bool {
        engine.alta != nil || AltaSession.current?.isOpen == true
    }

    private func make(host: String) {
        let started = CACurrentMediaTime()
        let view = AltaSession.makeWeb()
        spare = (view, host)
        warm = false
        watch = view.web.observe(\.isLoading, options: [.new]) { [weak self] w, _ in
            let done = !w.isLoading && w.url != nil
            Task { @MainActor in if done { self?.warm = true } }
        }
        view.web.loadHTMLString("", baseURL: nil)
        expiry?.cancel()
        expiry = Task { [weak self] in
            try? await Task.sleep(nanoseconds: AltaSpare.keepFor)
            guard !Task.isCancelled else { return }
            self?.drop("unused for ten minutes")
        }
        if OpenClock.verbose { NSLog("[Simpl] alta: a spare web view made ahead (%d ms)", Int(((CACurrentMediaTime() - started) * 1000).rounded())) }
    }
}

// MARK: - The session

/// One assignment open in the popup: Alta's page, and what it last said of the objectives and their mastery.
@MainActor
final class AltaSession: ObservableObject {
    @Published private(set) var report: AltaReport?
    /// (1.3.17) The overview Alta opens on: the assignment and its objectives, before a question is asked.
    @Published private(set) var overview: AltaOverview?
    /// Alta's page still opening (the launch through Canvas): the popup's skeleton over it meanwhile — (1.3.101) until
    /// the first page of Alta's own begins (the page script says which are), so the Canvas page on the way to it is never
    /// shown, where 1.3.100 showed a spinner until that Canvas page had loaded.
    @Published private(set) var opening = true
    /// (1.3.101) A page on the way to Alta's said it was laid out while the skeleton was up: shown if it turns out to be
    /// where the launch ends (`landed`).
    private var shownEarly = false
    /// (1.3.101) This open, timed for the console's line.
    private var timeline = AltaTimeline()
    /// (1.3.101) The launch asked of Canvas — from the moment the popup is asked for (Engine's `alta`), not once its view
    /// is built — and the page loaded once it is answered.
    private var launching: Task<Void, Never>?
    /// (1.3.101) The popup closed (`popupClosed`): a launch answered after it is not loaded.
    private var closed = false
    /// (1.3.106) Its popup up, or on its way: no spare web view is made meanwhile.
    var isOpen: Bool { !closed }
    /// (1.3.101) The launch answered with Canvas's own page for the assignment, Alta in it (`go`).
    private var direct = false
    /// (1.3.101) Pages begun in the main frame (each `frame` from it): a page still the one `landed` saw, by this.
    private var documents = 0
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
    /// at once to the page shown; in dark mode the question's colours are turned over, for dark glass. (1.3.79) The popup
    /// tells it light, always: Alta's page is shown light whatever the app's appearance. (1.3.83) Light or dark as the
    /// student picks it above the objectives (light unless Dark is picked): the web view's own appearance with it (the
    /// page's colour scheme, its fields), and the graph window's.
    func appearance(dark: Bool) {
        web.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        pageDark = dark
        AltaGraphWindow.shared.dark(dark)
        tellPage()
    }
    /// (1.3.83) Alta's side of the popup dark: the student's Dark, above the objectives.
    private(set) var pageDark = false

    /// (1.3.83) Show Browser, beside Light / Dark: Knewton Alta's own page — the same assignment, signed in, where the
    /// student is in it — as Alta draws it, in place of the app's view of it: nothing of it hidden or laid on glass, never
    /// out of sight, its pop-ups its own; the objectives and the mastery still read from it for the rail.
    @Published private(set) var browser = false

    func setBrowser(_ on: Bool) {
        guard on != browser else { return }
        browser = on
        if on { dialog = nil } // (its pop-up, if one was the app's sheet, given back to the page by the hook)
        tellPage()
    }

    /// (1.3.83) How the page is to be shown — light or dark, its graphs' colours, Show Browser — told before each page (a
    /// script ahead of the page script) and at once to the page shown.
    private func tellPage() {
        let flags = "window.__simplDark = \(pageDark); window.__simplPicsLight = \(picsLight); window.__simplBrowser = \(browser);"
        let ucc = web.configuration.userContentController
        ucc.removeAllUserScripts()
        ucc.addUserScript(WKUserScript(source: flags, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        ucc.addUserScript(AltaHook.script)
        web.evaluateJavaScript(flags + " if (window.__simplFocusDark) window.__simplFocusDark(); if (window.__simplBrowserNow) window.__simplBrowserNow();", completionHandler: nil)
    }

    // MARK: (1.3.70) The question's graphs: small, opened large in a window of their own

    /// Where each small graph is in the page (its own coordinates), for the button by it.
    @Published private(set) var pics: [AltaPic] = []
    /// In dark mode a graph's colours are turned over with the question's (light lines on the dark glass) unless the
    /// student asks for them as Alta draws them (on a white card): kept for every assignment.
    @Published private(set) var picsLight = UserDefaults.standard.bool(forKey: AltaSession.picsLightKey)
    private static let picsLightKey = "SimplAltaGraphsLight"
    /// The graph open in its window, by its place among the small ones.
    private var picOpen: Int?

    func setPicsLight(_ on: Bool) {
        picsLight = on
        UserDefaults.standard.set(on, forKey: Self.picsLightKey)
        AltaGraphWindow.shared.light(on)
        appearance(dark: pageDark)
        if let i = picOpen { Task { try? await Task.sleep(nanoseconds: 120_000_000); await openPic(i) } }
    }

    /// The graph, large, in its window: a picture of it as the page draws it now, at its own size (WebKit draws it
    /// afresh at that scale, so it is sharp).
    func openPic(_ i: Int) async {
        guard let r = await run("return window.__simplPic ? window.__simplPic.rect(\(i)) : null", in: nil) as? [String: Any],
              let x = (r["x"] as? NSNumber)?.doubleValue, let y = (r["y"] as? NSNumber)?.doubleValue,
              let w = (r["w"] as? NSNumber)?.doubleValue, let h = (r["h"] as? NSNumber)?.doubleValue, w > 4, h > 4 else { return }
        let z = max(0.05, (r["z"] as? NSNumber)?.doubleValue ?? 1)
        let rect = CGRect(x: x - 4, y: y - 4, width: w + 8, height: h + 8).intersection(web.bounds)
        guard !rect.isEmpty else { return }
        let image: NSImage? = await withCheckedContinuation { (go: CheckedContinuation<NSImage?, Never>) in
            let config = WKSnapshotConfiguration()
            config.rect = rect
            config.snapshotWidth = NSNumber(value: Double(rect.width) / z)
            config.afterScreenUpdates = true
            web.takeSnapshot(with: config) { image, _ in go.resume(returning: image) }
        }
        guard let image else { return }
        picOpen = i
        AltaGraphWindow.shared.show(image, title: launch.title, session: self)
    }

    /// The question answered or gone: its graph's window goes with it.
    func closePic() {
        picOpen = nil
        AltaGraphWindow.shared.close()
    }
    fileprivate func picWindowClosed() { picOpen = nil }

    /// (1.3.49) Alta's question (or lesson) is shown alone: laid on Liquid Glass.
    var focused: Bool { focusState == "question" || focusState == "lesson" }
    /// (1.3.53) The parts' panels, the page's height, which question they are of (a new one scrolls back to the top),
    /// and the parts' buttons.
    @Published private(set) var panels: [AltaPanel] = []
    @Published private(set) var pageHeight: CGFloat = 0
    @Published private(set) var panelGen = 0
    @Published private(set) var buttons: [AltaButton] = []
    /// (1.3.99) Alta's verdict on the question, as the hook reads its strip under the answer: "right", "wrong" or ""
    /// (none shown) — the popup's own pill beside its buttons, gone with the next question.
    @Published private(set) var verdict = ""

    /// The question laid on glass panels, one behind each part, the popup scrolling the page.
    var split: Bool { !asIs && focused && !panels.isEmpty && pageHeight > 0 }

    /// (1.3.83) Alta's page shown as it is — edge to edge, in sight, scrolling itself, nothing of the app's over it —
    /// while a pop-up of Alta's is given back to it, and with Show Browser on.
    var asIs: Bool { browser || dialogInPage }

    /// (1.3.60) Whether Alta's page is laid out and in sight (it is kept out of sight while it loads, so it never flashes
    /// in before its question).
    @Published private(set) var pageShown = false

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
        // (1.3.70: the question answered — Submit, Continue — its graph's window put away)
        if buttons.first(where: { $0.id == index })?.primary == true { closePic() }
        Task { _ = await run("return window.__simplButtons ? window.__simplButtons.press(\(index)) : null", in: nil) }
    }

    /// (1.3.51) Alta's pop-up open, shown as the Mac's own sheet.
    @Published var dialog: AltaDialog?
    /// (1.3.83) Alta's pop-up given back to its page — what it holds the app cannot draw as its own, nothing in it could
    /// be read, or the student asked: Alta's page shown as it is, in sight, until the pop-up closes.
    @Published private(set) var dialogInPage = false

    /// One of the pop-up's buttons, pressed in Alta's own.
    func pressDialog(_ index: Int) {
        Task { _ = await run("return window.__simplDialog ? window.__simplDialog.press(\(index)) : null", in: nil) }
    }

    /// (1.3.83) A link in the pop-up's body: followed in Alta's page as Alta follows it, or — another site's, or one that
    /// opens a window of its own — opened in the browser.
    func followDialogLink(_ index: Int) {
        Task {
            guard let href = await run("return window.__simplDialog ? window.__simplDialog.link(\(index)) : null", in: nil) as? String,
                  let url = URL(string: href), ["http", "https"].contains(url.scheme?.lowercased() ?? "") else { return }
            NSWorkspace.shared.open(url)
        }
    }

    /// The Mac's sheet put away without a button, or its Show in Alta's Page: Alta's pop-up given back to the page, shown
    /// there as Alta draws it until it closes. (`empty`: it came with nothing the sheet could show.)
    func dialogDismissed(empty: Bool = false) {
        dialog = nil
        let why = empty ? "'nothing in it could be shown'" : ""
        Task { _ = await run("return window.__simplDialog ? window.__simplDialog.giveBack(\(why)) : null", in: nil) }
    }

    /// (maths drawn whole) The skeleton never left over Alta's page for good: while it is up once the page has begun (not
    /// the launch, no start screen, not Show Browser), the page is asked to look at itself again every few seconds — what
    /// the student's Light then Dark did by hand — and after 26 seconds, longer than any wait of the page script's own,
    /// it is shown as it is, said in the Error Logs. Begun again with each page and each time Alta goes between parts.
    private var skeletonWatch: Task<Void, Never>?

    private func watchSkeleton() {
        skeletonWatch?.cancel()
        let page = documents
        skeletonWatch = Task { [weak self] in
            for k in 1...6 {
                try? await Task.sleep(nanoseconds: k == 1 ? 6_000_000_000 : 4_000_000_000)
                guard !Task.isCancelled, let self, self.documents == page, !self.closed, !self.opening, !self.pageShown,
                      !self.asIs, self.cover == nil, self.failure == nil else { return }
                if k < 6 {
                    _ = await self.run("return window.__simplBrowserNow ? window.__simplBrowserNow() : null", in: nil)
                    continue
                }
                // (shown as it is by the page script itself, so its focus is lifted and what it knows stays in step —
                // it says why in the Error Logs, with what the page held; a page without it only has its veil lifted)
                let byPage = await self.run("if (window.__simplAsIsNow) return window.__simplAsIsNow(); if (document.documentElement) document.documentElement.setAttribute('data-simpl-shown', ''); return null", in: nil) as? Bool == true
                guard !Task.isCancelled, self.documents == page, !self.closed else { return }
                withAnimation(Motion.gentle) { self.pageShown = true }
                if !byPage { self.logShownAsIs("Alta's page shown as it is: it was still out of sight after 26 seconds") }
            }
        }
    }

    /// (1.3.83) Each time something of Alta's was shown as it is, for the Error Logs.
    private func logShownAsIs(_ text: String) {
        withAnimation(Motion.gentle) {
            errorLogs.append("\(Date().formatted(date: .abbreviated, time: .standard)) — \(text)")
            if errorLogs.count > 50 { errorLogs.removeFirst() }
        }
    }

    /// (1.3.72) The Alta assignment open now, for the Developer menu's Save Page Code.
    static weak var current: AltaSession?

    /// (1.3.47) Each time Alta's page was not recognised and shown as it is: when, why, and what the page held.
    @Published private(set) var errorLogs: [String] = []

    /// (1.3.106) `spare`: the web view made ahead for Alta's popup (AltaSpare), taken by this open; else one is made now.
    init(launch: AltaLaunch, spare: AltaSpare.View? = nil) {
        self.launch = launch
        let began = CACurrentMediaTime()
        let made: AltaSpare.View
        if let spare { made = spare } else { made = Self.makeWeb() }
        relay = made.relay
        web = made.web
        timeline.view = spare != nil ? "spare" : "made \(Int(((CACurrentMediaTime() - began) * 1000).rounded())) ms"
        made.relay.session = self
        loading = web.observe(\.isLoading, options: [.new]) { [weak self] w, _ in
            let done = !w.isLoading && w.url != nil
            Task { @MainActor in if done { self?.landed() } }
        }
        // (1.3.101) the page's look — light or dark, Show Browser — as the popup will tell it, told before its first page:
        // the launch may be answered before the popup's view is up to tell it
        pageDark = UserDefaults.standard.bool(forKey: AltaQuizScreen.pageDarkKey)
        browser = UserDefaults.standard.bool(forKey: AltaQuizScreen.showBrowserKey)
        web.appearance = NSAppearance(named: pageDark ? .darkAqua : .aqua)
        tellPage()
        AltaSession.current = self
    }

    /// A web view for Alta's page, with the page script in every frame and the relay its messages come by: signed in to
    /// the school as the app's own pages are, Alta's sounds and videos allowed, laid on the popup's glass (1.3.49), and
    /// (1.3.79) light whatever the app's appearance (its own colour scheme, its fields), as every tool's is — (1.3.83)
    /// until the student picks Dark (`appearance(dark:)`). (1.3.106) Made as the popup is asked for, or ahead (AltaSpare).
    static func makeWeb() -> AltaSpare.View {
        CrossSiteCookies.apply()
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default() // (the Canvas session: Alta's launch finds the student signed in)
        config.mediaTypesRequiringUserActionForPlayback = []
        config.applicationNameForUserAgent = "Version/18.4 Safari/605.1.15"
        config.userContentController.addUserScript(AltaHook.script)
        let r = AltaRelay()
        config.userContentController.add(r, name: AltaHook.handler)
        let web = AltaWebView(frame: NSRect(x: 0, y: 0, width: 1000, height: 800), configuration: config)
        web.isInspectable = true
        web.setValue(false, forKey: "drawsBackground") // (1.3.49: the question laid on the popup's glass; see AltaQuiz)
        web.appearance = NSAppearance(named: .aqua)
        return AltaSpare.View(web: web, relay: r)
    }

    /// Alta opened: the assignment's launch through Canvas (or the page asked for). (1.3.101) Asked at once, by the
    /// engine the moment the popup is asked for; asked again by the popup's view, it does nothing. (1.3.106: whatever
    /// address its web view has — a spare's is about:blank — it is the launch that says whether it was asked.)
    func start(engine: Engine) {
        guard launching == nil else { return }
        if let data = UserDefaults.standard.data(forKey: knownKey), let list = try? JSONDecoder().decode([AltaOverview.Objective].self, from: data) { known = list }
        if let p = launch.page, let u = engine.absolute(p) {
            timeline.asked = timeline.ms()
            timeline.given = true
            launching = Task {}
            web.load(URLRequest(url: u))
            return
        }
        struct Answer: Decodable { var url: String; var sessionless: Bool? }
        let course = launch.course, assignment = launch.assignment
        launching = Task { [weak self] in
            // (the moment the call goes to the engine, not when it was queued)
            self?.timeline.asked = self?.timeline.ms()
            do {
                let a = try await engine.call("toolLaunch", ["course": course, "assignment": assignment], as: Answer.self)
                guard let u = URL(string: a.url) else { throw EngineError.unreadable }
                self?.go(u, direct: a.sessionless == false)
            } catch {
                self?.couldNotOpen(error)
            }
        }
    }

    /// (1.3.101) The launch answered — Alta's page, through Canvas, loaded; never once the popup has closed (the launch's
    /// one-time address is not spent on a page nobody sees). `direct`: Canvas's own page for the assignment, with Alta in
    /// it, where Canvas would not give a launch of its own.
    private func go(_ url: URL, direct: Bool) {
        guard !closed else { return }
        timeline.launch = timeline.ms()
        self.direct = direct
        web.load(URLRequest(url: url))
    }

    private func couldNotOpen(_ error: Error) {
        withAnimation(Motion.gentle) { failure = "Knewton Alta could not be opened. \(error.localizedDescription)"; opening = false }
        guard !timeline.told else { return }
        timeline.told = true
        NSLog("[Simpl] alta: %@", (timeline.parts + ["could not be opened after \(Int(timeline.ms().rounded())) ms (\(error.localizedDescription))"]).joined(separator: " · "))
    }

    /// (1.3.101) Until the launch is answered and its page asked for (at once with the page given): the popup's header
    /// asks Canvas for its due line only then, so that call never waits beside the launch.
    func launched() async {
        await launching?.value
    }

    /// (1.3.101) A page finished loading while the skeleton is still up for Alta's first: if it stays where it is — a page
    /// that is not Alta's and goes nowhere (Canvas's error, the school's sign-in) — it is shown after a moment, as 1.3.100
    /// showed the launch's last page. Canvas's launch page sends its form on to Alta at once, and is never shown. Canvas's
    /// own page for the assignment (`direct`: no launch of its own was had) is where the launch ends: shown at once, as
    /// 1.3.100 showed it. Still there means no new page begun since (`documents`), whatever its address did meanwhile.
    private func landed() {
        // (1.3.106: never a spare web view's empty page, which it may still be finishing as the popup takes it)
        guard opening, let url = web.url, url.scheme != "about" else { return }
        let page = documents, wait: UInt64 = direct ? 0 : 2_500_000_000
        Task { [weak self] in
            if wait > 0 { try? await Task.sleep(nanoseconds: wait) }
            guard let self, self.opening, !self.web.isLoading, self.documents == page else { return }
            withAnimation(Motion.gentle) {
                self.opening = false
                if self.shownEarly { self.pageShown = true }
            }
            self.tellShown(["how": "not Alta's page"])
        }
    }

    /// (1.3.101) The page in sight: this open's line on the console, once; a later page's (Alta's learn page after its
    /// cover, the next part) in the screenshot suite and developer mode.
    private func tellShown(_ head: [String: Any]) {
        if !timeline.told {
            timeline.told = true
            NSLog("[Simpl] alta: %@", timeline.shownLine(head))
        } else if OpenClock.verbose, let t = (head["t"] as? NSNumber)?.intValue {
            let quiet = (head["quiet"] as? NSNumber).map { ", quiet \($0.intValue) ms\(head["capped"] as? Bool == true ? ", capped" : "")" } ?? ""
            NSLog("[Simpl] alta: shown again, %d ms into its page (%@%@)", t, head["how"] as? String ?? "", quiet)
        }
    }

    /// (1.3.101) The popup closed: an open never shown said so on the console, and a launch not yet answered never loaded.
    func popupClosed() {
        closePic()
        closed = true
        skeletonWatch?.cancel()
        launching?.cancel()
        guard !timeline.told else { return }
        timeline.told = true
        NSLog("[Simpl] alta: %@", (timeline.parts + ["closed after \(Int(timeline.ms().rounded())) ms, before it was shown"]).joined(separator: " · "))
    }

    // MARK: What the page says

    func take(_ text: String, frame: WKFrameInfo) {
        guard let data = text.data(using: .utf8),
              let head = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }
        switch head["kind"] as? String {
        case "frame":
            // (1.3.60) a new page: kept out of sight in it until it is laid out
            if frame.isMainFrame {
                // (1.3.101) the first of Alta's own pages: the skeleton is the page's Loading from here
                shownEarly = false
                documents += 1
                if head["alta"] as? Bool == true, opening {
                    timeline.page = timeline.ms(head)
                    opening = false
                }
                // (1.3.62: and the last page's glass panels and buttons put away with it)
                withAnimation(Motion.gentle) {
                    pageShown = false
                    panels = []
                    buttons = []
                    pics = []
                    verdict = ""
                    // (1.3.83: and the last page's pop-up, which can no longer say it has closed)
                    dialog = nil
                    dialogInPage = false
                }
                closePic()
                watchSkeleton()
                return
            }
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
            let ps = (head["pics"] as? [[String: Any]] ?? []).enumerated().compactMap { k, p -> AltaPic? in
                guard let x = (p["x"] as? NSNumber)?.doubleValue, let y = (p["y"] as? NSNumber)?.doubleValue,
                      let w = (p["w"] as? NSNumber)?.doubleValue, let h = (p["h"] as? NSNumber)?.doubleValue, w > 0, h > 0 else { return nil }
                return AltaPic(id: k, rect: CGRect(x: x, y: y, width: w, height: h))
            }
            if list != panels { panels = list }
            if height != pageHeight { pageHeight = height }
            if ps != pics { pics = ps }
            if g != panelGen { panelGen = g; closePic() } // (another question: the last one's graph window goes)
            // (1.3.99) Alta's verdict, as it comes and as it goes: the pill beside the buttons
            let v = head["verdict"] as? String ?? ""
            if v != verdict { withAnimation(Motion.snappy) { verdict = v } }
        case "buttons":
            guard frame.isMainFrame else { return }
            let list = (head["items"] as? [[String: Any]] ?? []).enumerated().map { k, b in
                AltaButton(id: k, label: b["label"] as? String ?? "", primary: b["primary"] as? Bool ?? false, enabled: b["enabled"] as? Bool ?? true)
            }
            if list != buttons { withAnimation(Motion.snappy) { buttons = list } }
        case "dialog":
            guard frame.isMainFrame else { return }
            guard head["open"] as? Bool == true else {
                withAnimation(Motion.gentle) {
                    dialog = nil
                    dialogInPage = false
                }
                return
            }
            let title = head["title"] as? String ?? ""
            // (1.3.83) given back to Alta's page — it holds what the app cannot draw as its own, nothing in it could be
            // read, or the student asked: shown there, Alta's page as it is, until it closes; said in the Error Logs
            if head["inPage"] as? Bool == true {
                withAnimation(Motion.gentle) {
                    dialog = nil
                    dialogInPage = true
                }
                logShownAsIs("Alta's pop-up\(title.isEmpty ? "" : " “\(title)”") shown in Alta's page: \(head["why"] as? String ?? "")")
                return
            }
            let html = head["html"] as? String ?? ""
            let choices = (head["buttons"] as? [[String: Any]] ?? []).map { b in
                AltaDialog.Choice(label: b["label"] as? String ?? "", primary: b["primary"] as? Bool ?? false,
                                  enabled: b["enabled"] as? Bool ?? true, close: b["close"] as? Bool ?? false)
            }
            // (never an empty sheet: a pop-up with nothing to show is Alta's page's to show)
            guard !html.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { dialogDismissed(empty: true); return }
            dialogInPage = false
            if dialog != nil {
                dialog?.title = title
                dialog?.html = html
                dialog?.buttons = choices
                dialog?.step += 1
            } else {
                dialog = AltaDialog(title: title, html: html, buttons: choices, base: web.url ?? URL(fileURLWithPath: "/"))
            }
            // (the sheet up: Alta's own hidden in the page meanwhile)
            Task { _ = await run("return window.__simplDialog ? window.__simplDialog.hide() : null", in: nil) }
        case "focus":
            guard frame.isMainFrame else { return }
            let state = head["state"] as? String ?? ""
            if timeline.first == nil, !opening { timeline.first = (state, timeline.ms(head)) }
            withAnimation(Motion.gentle) { focusState = state }
            guard !["question", "lesson", "overview"].contains(state) else { return }
            var entry = "\(Date().formatted(date: .abbreviated, time: .standard)) — Alta's page shown as it is: \(head["reason"] as? String ?? "not recognised") [\(state)]"
            if let sh = head["shape"], !(sh is NSNull), let d = try? JSONSerialization.data(withJSONObject: sh, options: [.prettyPrinted, .sortedKeys]),
               let t = String(data: d, encoding: .utf8) { entry += "\n" + t }
            withAnimation(Motion.gentle) {
                errorLogs.append(entry)
                if errorLogs.count > 50 { errorLogs.removeFirst() }
            }
        case "shown":
            guard frame.isMainFrame else { return }
            // (1.3.101) a page on the way to Alta's (Canvas's launch, which sends its form on at once): kept behind the
            // skeleton, shown only if the launch ends there (`landed`)
            if opening { shownEarly = true; return }
            withAnimation(Motion.gentle) { pageShown = true }
            tellShown(head)
        case "hidden":
            guard frame.isMainFrame else { return }
            withAnimation(Motion.gentle) { pageShown = false }
            closePic() // (Alta between parts: the question gone)
            watchSkeleton()
        case "remeasured":
            // (maths drawn whole) MathQuill's brackets or √ found drawn flat — drawn out of sight — and sized again
            guard frame.isMainFrame else { return }
            let n = (head["count"] as? NSNumber)?.intValue ?? 0
            logShownAsIs("Alta's maths drawn flat (\(n) bracket\(n == 1 ? "" : "s") or roots), sized again\(head["mathquill"] as? Bool == true ? " by MathQuill" : "")")
        case "picOpen":
            guard frame.isMainFrame, let i = (head["i"] as? NSNumber)?.intValue else { return }
            Task { await openPic(i) }
        case "cover":
            guard frame.isMainFrame else { return }
            if head["none"] as? Bool == true {
                if cover != nil { withAnimation(Motion.gentle) { cover = nil } }
                return
            }
            guard let c = try? JSONDecoder().decode(AltaCover.self, from: data) else { return }
            if timeline.first?.state != "cover", !opening, !timeline.told { timeline.first = ("cover", timeline.ms(head)) }
            withAnimation(Motion.gentle) {
                cover = c
                // (1.3.60) the cover's list is the whole of the assignment's objectives, every one named — the rail's,
                // and the names lined up with Alta's answers once a question is on
                keep(c.list)
            }
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
            // (1.3.60) on the cover: its objectives, all of them, each as mastered as the cover says
            if let c = cover, !c.list.isEmpty {
                let levels = c.topics.flatMap { $0.objectives.map(\.mastery) }
                return c.list.enumerated().map { k, o in
                    AltaObjective(id: o.id, number: k + 1, name: o.name, mastery: min(max(levels[k] ?? 0, 0), 1), current: false)
                }
            }
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

    /// The whole assignment's mastery, 0…1. (1.3.78) Before Alta has said it (the cover, before a question), the mean
    /// of the objectives' own — never 0% over a list with some of them mastered.
    var mastery: Double {
        guard let r = report else {
            if overview?.completed == true { return 1 }
            if let p = livePercent { return p }
            if let p = overview?.percent { return min(max(p / 100, 0), 1) }
            let list = objectives
            return list.isEmpty ? 0 : list.map(\.mastery).reduce(0, +) / Double(list.count)
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
    static let collect = #"""
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

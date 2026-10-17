namespace Simpl.Screens;

/// <summary>
/// The Knewton Alta popup's page script (the Mac's AltaHook, mac/Simpl/Engine/AltaSession.swift — the one to change; this
/// is its copy, word for word but for how it speaks to the app — WebView2's <c>chrome.webview.postMessage</c> where WebKit
/// has its message handler — and, since 0.3.5, for how it reads Alta's pop-ups (whole, once they have settled) and Show
/// browser (Alta's page left as it is), here first until the Mac's takes them up). In every frame of the popup's own web
/// view, which holds nothing but Alta's launch: it reads a copy of Alta's own answers as its page receives them and
/// passes the app the assignment, its objectives and their mastery and the objective on screen — never the question, its
/// answer key or who the student is; it lays Alta's question out alone (its parts, their panels and buttons told to the
/// app), keeps the page out of sight until it is laid out, and passes on Alta's pop-ups and its cover.
/// </summary>
internal static class AltaHook
{
    public const string Source = """
        (function () {
          // (1.3.19: in every page of the popup's own hidden view, which holds nothing but Alta's launch — Alta's own
          // site names are not guessed at)
          if (window.__simplAltaOn || !/^https?:$/.test(location.protocol)) return;
          window.__simplAltaOn = true;
          var post = function (m) { try { window.chrome.webview.postMessage(JSON.stringify(m)); } catch (e) {} };
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
          // (Windows 0.3.5) a pop-up of Alta's given back to the page, shown there as Alta draws it: the page stays in
          // sight under it until it closes
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
              if (!showing) {
                showing = true;
                var nt = null;
                try { nt = performance.getEntriesByType('navigation')[0] || null; } catch (e) {}
                post(stamp({ kind: 'shown', how: how || '', quiet: steady ? steady.quiet : null, capped: !!(steady && steady.capped),
                  dcl: nt && nt.domContentLoadedEventStart ? Math.round(nt.domContentLoadedEventStart) : null }));
              }
            };
            // (1.3.62) out of sight again — Alta between one part and the next, taking its time — until the next is laid out
            // (0.3.5: never while the student has asked for the browser)
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
            }
            var revealSoon = function (how) { if (document.documentElement) reveal(how); else setTimeout(function () { revealSoon(how); }, 10); };
            // (0.3.5: and at once while the student has asked for the browser — the app sets window.__simplBrowser before
            // the page, as __simplDark)
            if (!veils) revealSoon('not alta');
            else if (window.__simplBrowser) revealSoon('browser');
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
              + 'html[data-simpl-q] [data-simpl-mini] { zoom: var(--simpl-zoom, 1) !important; width: fit-content !important; cursor: zoom-in !important; border-radius: 10px !important; outline: 2px solid transparent !important; outline-offset: 6px !important; transition: outline-color .18s ease !important; }\n'
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
            // at a zoom that brings it down to that (0.3.18: twice the 300 × 220 before — the owner found the graphs too
            // small to read; Enlarge is still beside them)
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
            var dark = function () {
              if (!document.documentElement) return;
              document.documentElement.toggleAttribute('data-simpl-dark', !!window.__simplDark);
              document.documentElement.toggleAttribute('data-simpl-pics-light', !!window.__simplPicsLight); // (1.3.70)
            };
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
            // ---- (1.3.51) Alta's pop-ups as the app's own: each one open passed to the app and hidden in the page while
            // the app shows it, the app's buttons pressing Alta's ----
            // (Windows 0.3.5) Read whole, once it has settled. Before, a pop-up was read the moment it was seen — often
            // before its body was in — and pictured 380 ms later, as it faded in or with its body still empty (or blank,
            // under the page's veil); once the app had hidden it, a new reading of it lost its words' breaks and no new
            // picture could be taken (the app's dialog covers the web view), so the app's dialog showed its title and
            // its buttons round an empty body. Now a pop-up is a dialog by its role, one marked modal, or Material's,
            // React Toolbox's or react-modal's own box — in the page or in a web component's own tree — the innermost of
            // them. It is hidden in the page as soon as it is seen (the app shows it as its own, or gives it back) and
            // passed once it has settled — nothing it says changed for a moment, nothing in it still loading (a spinner,
            // a picture not in yet), its fade done; three seconds at most, five while it says nothing yet — and again
            // whenever what it says changes (a next step). It is read with the app's hiding lifted for that moment (no
            // paint comes between): its title (what names it, else a heading it opens with), its body as plain HTML the
            // app draws as its own words (paragraphs, headings, lists, tables, links; each formula as its TeX — MathJax's
            // own script, KaTeX's annotation, the formula's attribute), each picture, drawing or formula with no TeX made
            // into a picture here (drawn on a canvas; a picture of another site's that the page may not read back by its
            // address, for the app to fetch), and every one of its buttons, whatever it says — its words, whether Alta
            // makes it the main one, whether it can be pressed, a close button told apart. What the app cannot draw — a
            // video, a form, a frame of another site's — is said too, and an empty body: the app gives that pop-up back to
            // the page, shown there as Alta draws it.
            var DIALOG_SEL = '[role="dialog"], [role="alertdialog"], dialog[open], [aria-modal="true"], .MuiDialog-paper, [data-react-toolbox="dialog"], .ReactModal__Content, mat-dialog-container, .modal.show .modal-content, .modal.in .modal-content';
            var BTN = 'button, [role="button"], input[type="button"], input[type="submit"], input[type="reset"], a[href][class*="btn" i], a[href][class*="button" i]';
            var MATHS = '.MathJax_Display, .MathJax_SVG_Display, .MathJax, .MathJax_CHTML, .MathJax_SVG, mjx-container, .katex-display, .katex, math, .mq-math-mode, [data-latex], [data-tex]';
            var HEADING = 'h1, h2, h3, h4, h5, h6, [role="heading"], [class*="DialogTitle"], [class*="title" i]';
            var KEEP_TAGS = /^(p|h[1-6]|ul|ol|li|table|thead|tbody|tfoot|tr|td|th|b|strong|i|em|u|sup|sub|code|pre|blockquote|dl|dt|dd|s|small|mark|figure|figcaption)$/;
            var dialogEl = null, dialogSaid = '', dialogRead = '', dialogDirty = 0, dialogWaiting = 0, dialogSeen = 0, dialogTicks = 0, dialogGen = 0;
            var dialogBtns = [], dialogLinks = [], dialogPics = [], dialogHosts = [];
            var esc = function (s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); };
            var styleOf = function (el) { return (el.ownerDocument.defaultView || window).getComputedStyle(el); };
            // (the app's own hiding lifted for the moment a pop-up is looked for and read — no paint comes between — so
            // one the app is showing reads as it is)
            var unhidden = function (fn) {
              var de = document.documentElement, was = de.hasAttribute('data-simpl-native-dialog');
              if (was) de.removeAttribute('data-simpl-native-dialog');
              try { return fn(); } finally { if (was) de.setAttribute('data-simpl-native-dialog', ''); }
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
            // (still loading: a spinner or a bar that goes round, something marked busy, or words saying so — a bar that
            // shows how far something is, a mastery ring, is not)
            var dialogBusy = function (d) {
              if ([].some.call(d.querySelectorAll(BUSY), function (b) { return shown(b) && !b.hasAttribute('aria-valuenow') && !b.querySelector('[aria-valuenow]'); })) return true;
              var t = norm(d.innerText || '');
              return /^(loading|please wait|one moment|just a moment|preparing)\b/.test(t) && t.length < 120;
            };
            // (what holds a pop-up in the page — its backdrop with it — out of a web component's own tree to its host)
            var holderOf = function (d) {
              var x = d;
              while (x.getRootNode && x.getRootNode() !== document && x.getRootNode().host) x = x.getRootNode().host;
              return x.closest('.MuiModal-root, .MuiDialog-root, [data-react-toolbox="portal"], .ReactModalPortal, .cdk-overlay-container, .modal') || x.closest('[role="presentation"]') || x;
            };
            var dialogOpen = function (d) {
              var r = d.getBoundingClientRect();
              return r.width >= 60 && r.height >= 36 && styleOf(d).visibility !== 'hidden' && opacityOf(d) > 0.02;
            };
            // (the one open now: the last, the innermost of those nested; a web component's own trees looked in every
            // fourth time, or every time while one there is open — and its host kept in sight by the focus, below)
            var dialogNow = function () {
              dialogTicks++;
              var inside = dialogTicks % 4 === 1 || !!(dialogEl && dialogEl.isConnected && dialogEl.getRootNode() !== document);
              var ds = (inside ? deep(DIALOG_SEL) : all(DIALOG_SEL)).filter(function (d) { return !d.closest('#simpl-panels'); });
              if (inside) {
                var hosts = ds.filter(function (d) { return d.getRootNode() !== document; }).map(holderOf);
                if (hosts.length !== dialogHosts.length || hosts.some(function (h, i) { return h !== dialogHosts[i]; })) { dialogHosts = hosts; schedule(); }
              }
              ds = ds.filter(dialogOpen);
              ds = ds.filter(function (d) { return !ds.some(function (e) { return e !== d && d.contains(e); }); });
              return ds[ds.length - 1] || null;
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
            // Close, a ×, or an icon's own word for it, with no words of its own — told apart; its words as Alta writes
            // them, not as a style puts them in capitals)
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
                var seen = String(b.innerText || b.value || '').replace(/\s+/g, ' ').trim();
                var source = String(b.textContent || b.value || '').replace(/\s+/g, ' ').trim();
                var words = source && source.toLowerCase() === seen.toLowerCase() ? source : seen;
                var named = String(b.getAttribute('aria-label') || b.getAttribute('title') || '').replace(/\s+/g, ' ').trim();
                var icon = !!b.querySelector('svg, img, [class*="icon" i]');
                var close = ((!words || /^[×✕✖xX]$/.test(words)) && /close|dismiss|×|✕/i.test(named + ' ' + words)) || (icon && /^(close|clear)$/i.test(words));
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
            // (a formula's words for a reader, when it has no TeX and no picture can be made of it)
            var mathWords = function (m) {
              var said = m.getAttribute('aria-label') || m.getAttribute('alttext');
              var mml = m.querySelector('mjx-assistive-mml, .MJX_Assistive_MathML, math');
              return String(said || (mml && mml.textContent) || m.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 300);
            };
            // (its body: all of it but its title, its buttons left out — theirs are the app's; in a frame of the page's
            // own site, the frame's page read too)
            var readDialog = function (d) {
              var t = titleOf(d), btns = buttonsOf(d);
              var out = { title: t.text, words: 0, maths: 0, live: '', loading: false };
              var pics = [], links = [];
              var formula = function (tex, display) {
                out.maths++;
                var img = '<img class="equation_image" data-equation-content="' + esc(tex.trim()) + '">';
                return display ? '<p style="text-align:center">' + img + '</p>' : img;
              };
              var pic = function (el, inline, alt) {
                var r = el.getBoundingClientRect();
                if (r.width < 6 || r.height < 6) return '';
                pics.push({ el: el });
                return '<img data-simpl-pic="' + (pics.length - 1) + '" width="' + Math.round(r.width) + '" height="' + Math.round(r.height) + '"' + (inline ? ' class="equation"' : '') + (alt ? ' alt="' + esc(alt) + '"' : '') + '>';
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
                  // (MathJax 2's formula not drawn yet: its script is all there is of it)
                  var root = n.getRootNode().getElementById ? n.getRootNode() : n.ownerDocument;
                  if (tag === 'script' && /^math\/tex/i.test(n.type || '') && !(n.id && root.getElementById(n.id + '-Frame'))) return formula(n.textContent, /mode\s*=\s*display/i.test(n.type));
                  return '';
                }
                if (n.matches(BTN)) return '';
                var cs = styleOf(n);
                if (cs.display === 'none') return '';
                if (n.matches(MATHS)) { var f = texOf(n); return f && f.tex.trim() ? formula(f.tex, f.display) : pic(n, cs.display.indexOf('inline') === 0, mathWords(n) || 'A formula'); }
                if (n.getAttribute('aria-hidden') === 'true' || cs.visibility === 'hidden' || n.matches('.MathJax_Preview, .MJX_Assistive_MathML, .sr-only, .visually-hidden')) return '';
                var r = n.getBoundingClientRect();
                if ((r.width <= 1 || r.height <= 1) && cs.position === 'absolute') return '';
                var small = r.height <= 28 && cs.display.indexOf('inline') === 0;
                if (tag === 'img') { if (!n.complete) out.loading = true; return pic(n, small, n.alt || ''); }
                if (/^(svg|canvas|picture)$/.test(tag)) return pic(n, small, n.getAttribute('aria-label') || '');
                if (/^(video|audio|object|embed)$/.test(tag)) { out.live = out.live || (tag === 'audio' ? 'a sound' : 'a video'); return pic(n, false, ''); }
                if (/^(input|select|textarea)$/.test(tag)) { if (!/^hidden$/i.test(n.type || '')) out.live = out.live || 'a form'; return ''; }
                if (tag === 'iframe') {
                  var body = null;
                  try { body = n.contentDocument && n.contentDocument.body; } catch (e) {}
                  if (!body) { out.live = out.live || 'a frame of another site’s'; return ''; }
                  return '<div>' + kids(body) + '</div>';
                }
                if (tag === 'br') return '<br>';
                if (tag === 'hr') return '<hr>';
                var inner = kids(n);
                if (tag === 'a' && n.getAttribute('href')) { links.push(n); return '<a href="' + esc(n.href) + '">' + inner + '</a>'; }
                if (!inner.replace(/<[^>]*>|&nbsp;|\s/g, '') && !/<(img|hr|br)\b/.test(inner)) return /\s/.test(inner) ? ' ' : '';
                var name = KEEP_TAGS.test(tag) ? tag : cs.display === 'list-item' ? 'li' : /^(block|flex|grid|table|flow-root)/.test(cs.display) ? 'div' : 'span';
                // (bold and italic as Alta's own styles make them)
                var up = n.parentElement ? styleOf(n.parentElement) : null, flat = !/<(div|p|ul|ol|table|h[1-6]|blockquote|pre)\b/.test(inner);
                if (flat && !/^(b|strong|h[1-6]|th)$/.test(name) && +cs.fontWeight >= 600 && up && +up.fontWeight < 600) inner = '<b>' + inner + '</b>';
                if (flat && !/^(i|em)$/.test(name) && cs.fontStyle === 'italic' && up && up.fontStyle !== 'italic') inner = '<i>' + inner + '</i>';
                if (name === 'span') return inner;
                return '<' + name + ((name === 'td' || name === 'th') && n.colSpan > 1 ? ' colspan="' + n.colSpan + '"' : '') + '>' + inner + '</' + name + '>';
              };
              out.html = kids(d).trim().slice(0, 60000);
              out.busy = dialogBusy(d);
              out.buttons = btns.map(function (b) { return { label: b.label, primary: b.primary, enabled: b.enabled, close: b.close }; });
              dialogBtns = btns.map(function (b) { return b.el; });
              dialogLinks = links;
              dialogPics = pics;
              return out;
            };
            // (a picture, a drawing or a formula with no TeX in its body, as a picture the app can show: drawn on a canvas
            // at twice its size, so it is sharp — a picture of the page's own, a drawing (its looks written on it, the
            // symbols it draws from put in, its colour), a canvas — or, a picture of another site's the page may not read
            // back, its address; nothing if it cannot be had in two and a half seconds)
            var pictureOf = function (p) {
              var el = p.el;
              if (!el.isConnected) return Promise.resolve(null);
              var r = el.getBoundingClientRect(), w = Math.max(1, Math.round(r.width)), h = Math.max(1, Math.round(r.height));
              var scale = Math.max(2, Math.min(3, window.devicePixelRatio || 1));
              var paint = function (draw) {
                var c = document.createElement('canvas');
                c.width = Math.round(w * scale); c.height = Math.round(h * scale);
                var g = c.getContext('2d');
                g.scale(scale, scale);
                draw(g);
                return c.toDataURL('image/png');
              };
              var tag = el.tagName.toLowerCase();
              var made = new Promise(function (ok) {
                try {
                  if (tag === 'img' || tag === 'picture') {
                    var img = tag === 'picture' ? el.querySelector('img') : el;
                    var src = img ? img.currentSrc || img.src || '' : '';
                    if (!img) return ok(null);
                    if (/^data:image\/(png|jpe?g|gif|bmp)[;,]/i.test(src)) return ok(src);
                    try { if (img.complete && img.naturalWidth) return ok(paint(function (g) { g.drawImage(img, 0, 0, w, h); })); } catch (e) {}
                    return ok(/^https?:/i.test(src) ? src : null);
                  }
                  if (tag === 'canvas') { try { return ok(el.toDataURL('image/png')); } catch (e) { return ok(null); } }
                  var svg = tag === 'svg' ? el : el.querySelector('svg');
                  if (!svg) return ok(null);
                  var sr = svg.getBoundingClientRect();
                  if (svg !== el) { w = Math.max(1, Math.round(sr.width)); h = Math.max(1, Math.round(sr.height)); }
                  var copy = svg.cloneNode(true);
                  var from = [svg].concat([].slice.call(svg.querySelectorAll('*'))), to = [copy].concat([].slice.call(copy.querySelectorAll('*')));
                  var LOOKS = ['fill', 'fill-opacity', 'stroke', 'stroke-width', 'stroke-opacity', 'stroke-dasharray', 'stroke-linecap', 'stroke-linejoin', 'opacity', 'font-family', 'font-size', 'font-weight', 'font-style', 'text-anchor', 'dominant-baseline', 'visibility', 'color'];
                  for (var i = 0; i < from.length && i < 3000; i++) {
                    var cs = getComputedStyle(from[i]), s = '';
                    LOOKS.forEach(function (k) { var v = cs.getPropertyValue(k); if (v) s += k + ':' + v + ';'; });
                    if (to[i]) to[i].setAttribute('style', s);
                  }
                  // (the symbols it draws from, kept elsewhere in the page — MathJax's own, a sprite sheet's)
                  var defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs'), had = {};
                  var pull = function (node) {
                    [].forEach.call(node.querySelectorAll('use'), function (u) {
                      var ref = (u.getAttribute('href') || u.getAttribute('xlink:href') || '').replace(/^#/, '');
                      if (!ref || had[ref]) return;
                      had[ref] = 1;
                      if (copy.querySelector('[id="' + ref.replace(/"/g, '') + '"]')) return;
                      var at = svg.getRootNode().getElementById ? svg.getRootNode() : document;
                      var target = at.getElementById(ref) || document.getElementById(ref);
                      if (!target) return;
                      var c = target.cloneNode(true);
                      defs.appendChild(c);
                      pull(c);
                    });
                  };
                  pull(copy);
                  if (defs.childNodes.length) copy.insertBefore(defs, copy.firstChild);
                  copy.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
                  copy.setAttribute('xmlns:xlink', 'http://www.w3.org/1999/xlink');
                  copy.setAttribute('width', String(w));
                  copy.setAttribute('height', String(h));
                  var im = new Image();
                  im.onload = function () { try { ok(paint(function (g) { g.drawImage(im, 0, 0, w, h); })); } catch (e) { ok(null); } };
                  im.onerror = function () { ok(null); };
                  im.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(new XMLSerializer().serializeToString(copy));
                } catch (e) { ok(null); }
              });
              var late = new Promise(function (ok) { setTimeout(function () { ok(null); }, 2500); });
              return Promise.race([made, late]).then(function (src) { return src && src.length < 4000000 ? src : null; });
            };
            var DIALOG_CSS = 'html[data-simpl-native-dialog] [data-simpl-dialog] { visibility: hidden !important; }\n';
            var dialogStyle = function () {
              var st = document.getElementById('simpl-dialog');
              if (!st) { st = document.createElement('style'); st.id = 'simpl-dialog'; (document.head || document.documentElement).appendChild(st); }
              if (st.textContent !== DIALOG_CSS) st.textContent = DIALOG_CSS;
            };
            var dialogTick = function () {
              // (0.3.5) the browser asked for: Alta's pop-ups are its own, in its page — one the app was showing given back
              if (window.__simplBrowser) {
                if (dialogEl || dialogSaid || dialogInPage) {
                  dialogEl = null; dialogSaid = ''; dialogRead = ''; dialogWaiting = 0; dialogInPage = false; dialogGen++;
                  dialogBtns = []; dialogLinks = []; dialogPics = [];
                  document.documentElement.removeAttribute('data-simpl-native-dialog');
                  post({ kind: 'dialog', open: false });
                }
                return;
              }
              var d = unhidden(dialogNow);
              if (!d) {
                if (dialogEl || dialogSaid || dialogInPage) {
                  dialogEl = null; dialogSaid = ''; dialogRead = ''; dialogWaiting = 0; dialogInPage = false; dialogGen++;
                  dialogBtns = []; dialogLinks = []; dialogPics = [];
                  document.documentElement.removeAttribute('data-simpl-native-dialog');
                  post({ kind: 'dialog', open: false });
                }
                return;
              }
              var holder = holderOf(d);
              if (d !== dialogEl) {
                // (a pop-up seen: out of sight at once — the app shows it as its own once it has settled, or gives it back —
                // and the app told one is coming)
                dialogEl = d; dialogSeen = dialogDirty = Date.now(); dialogSaid = dialogRead = ''; dialogWaiting = 0; dialogInPage = false; dialogGen++;
                dialogStyle();
                document.documentElement.setAttribute('data-simpl-native-dialog', '');
                post({ kind: 'dialog', open: true, pending: true });
              }
              if (!holder.hasAttribute('data-simpl-dialog')) holder.setAttribute('data-simpl-dialog', '');
              var r;
              try { r = unhidden(function () { return readDialog(d); }); }
              catch (e) {
                // (never a pop-up of Alta's left out of sight: one the page script cannot read is given back to the page,
                // the app told why)
                if (!dialogInPage) {
                  dialogInPage = true;
                  dialogGen++;
                  document.documentElement.removeAttribute('data-simpl-native-dialog');
                  post({ kind: 'dialog', open: true, title: '', html: '', words: 0, maths: 0, pics: 0, missed: 0, buttons: [], ready: 'failed',
                    live: 'what the page script could not read (' + String((e && e.message) || e).slice(0, 160) + ')' });
                }
                return;
              }
              var sig = JSON.stringify([r.title, r.html, r.buttons, r.live]);
              if (sig !== dialogRead) { dialogRead = sig; dialogDirty = Date.now(); }
              // (given back to the page: Alta's own there, as Alta draws it, until it closes)
              if (dialogInPage || sig === dialogSaid) { dialogWaiting = 0; return; }
              if (!dialogWaiting) dialogWaiting = Date.now();
              var empty = !r.words && !r.maths && !dialogPics.length;
              var settled = Date.now() - dialogDirty >= 350 && !r.busy && !r.loading && !empty && !unhidden(function () { return fading(holder) || opacityOf(d) < 0.97; });
              if (!settled && !r.live && Date.now() - dialogWaiting < (empty ? 5000 : 3000)) return;
              dialogSaid = sig;
              dialogWaiting = 0;
              var gen = ++dialogGen, took = Date.now(), pics = dialogPics.slice();
              Promise.all(pics.map(pictureOf)).then(function (made) {
                if (gen !== dialogGen || d !== dialogEl) return;
                var missed = 0;
                var html = r.html.replace(/<img data-simpl-pic="(\d+)"([^>]*)>/g, function (all, i, rest) {
                  if (made[+i]) return '<img src="' + esc(made[+i]) + '"' + rest + '>';
                  missed++;
                  var alt = /\salt="([^"]*)"/.exec(rest);
                  return alt ? '<span>' + alt[1] + '</span>' : '';
                });
                post({ kind: 'dialog', open: true, title: r.title.slice(0, 200), html: html, words: r.words, maths: r.maths, pics: pics.length - missed, missed: missed,
                  live: r.live, ready: settled ? 'settled' : 'after ' + (Math.round((took - dialogSeen) / 100) / 10) + ' s', buttons: r.buttons });
              });
            };
            setInterval(dialogTick, 300);
            window.__simplDialog = {
              // (hidden in the page while the app shows it as its own)
              hide: function () { if (!dialogEl) return false; document.documentElement.setAttribute('data-simpl-native-dialog', ''); return true; },
              // (given back to the page — the app could not show it as its own, or the student asked — and shown there as
              // Alta draws it, the page in sight under it, until it closes)
              giveBack: function () {
                dialogInPage = !!dialogEl;
                document.documentElement.removeAttribute('data-simpl-native-dialog');
                if (dialogEl) reveal('dialog');
                return dialogInPage;
              },
              press: function (i) {
                var b = dialogBtns[i];
                if (!b || !b.isConnected || b.disabled || b.getAttribute('aria-disabled') === 'true') return false;
                b.click();
                dialogDirty = Date.now();
                return true;
              },
              // (a link in its body, followed as Alta's page follows it)
              link: function (href) { var a = dialogLinks.filter(function (x) { return x.href === href; })[0]; if (!a) return false; a.click(); return true; }
            };
            var tick = function () {
              timer = null; changeSince = 0;
              dialogTick();
              // (0.3.5) the browser asked for: Alta's page as it is, as in a browser — nothing of it laid out or hidden, no
              // buttons or panels for the app — until the student goes back to the question laid out
              if (window.__simplBrowser) {
                badSince = 0; loadingSince = 0; steadySince = 0;
                if (steadyTimer) { clearTimeout(steadyTimer); steadyTimer = null; }
                unmark();
                publish();
                reveal('browser');
                return;
              }
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
                // (0.3.5: each pop-up with what holds it — its backdrop, a web component's host — however it is built)
                var extra = all(DIALOG_SEL).map(holderOf).concat(dialogHosts, dialogEl && dialogEl.isConnected ? [holderOf(dialogEl)] : [])
                  .concat(all('body > .MuiModal-root, body > [role="presentation"], #MathJax_Hidden, #MathJax_Font_Test, #MathJax_Message'))
                  .concat(all('svg').filter(function (g) { return g.querySelector('symbol, defs'); }))
                  .filter(function (e, i, list) { return list.indexOf(e) === i && e !== document.body && e !== document.documentElement && !r.keep.some(function (k) { return k.contains(e); }); });
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
            // (0.3.5) the browser asked for, or put away: the page as it is, or laid out again, at once
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
        """;

    /// <summary>
    /// Run in one page (the Mac's AltaSession.collect, for Save Page Code): its HTML, its web components' own trees, and
    /// the text of each script and style sheet it loads — the body of an async function, as WebKit's
    /// callAsyncJavaScript takes it.
    /// </summary>
    public const string Collect = """
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
        """;
}

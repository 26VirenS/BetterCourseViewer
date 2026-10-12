namespace Simpl.Screens;

/// <summary>
/// The Knewton Alta popup's page script (the Mac's AltaHook, mac/Simpl/Engine/AltaSession.swift — the one to change; this
/// is its copy, word for word but for how it speaks to the app: WebView2's <c>chrome.webview.postMessage</c> where WebKit
/// has its message handler). In every frame of the popup's own web view, which holds nothing but Alta's launch: it reads
/// a copy of Alta's own answers as its page receives them and passes the app the assignment, its objectives and their
/// mastery and the objective on screen — never the question, its answer key or who the student is; it lays Alta's
/// question out alone (its parts, their panels and buttons told to the app), keeps the page out of sight until it is laid
/// out, and passes on Alta's pop-ups and its cover.
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
          // (1.3.46) this page made known to the app, for Save Page Code (each frame's code is asked of it there)
          post({ kind: 'frame' });
          // (1.3.60) Alta's page kept out of sight from its first moment until it is laid out — its question on glass, its
          // cover as the start screen, or (not recognised) as it is — so it never flashes in before its question; the app
          // shows its own Loading meanwhile. A page that is not Alta's (the launch on the way to it) is shown at once.
          var reveal = function () {};
          var veilAgain = function () {};
          if (window.top === window) {
            var showing = false;
            var veils = /knewton|alta/i.test(location.hostname + location.pathname);
            reveal = function (how) {
              if (document.documentElement) document.documentElement.setAttribute('data-simpl-shown', '');
              if (!showing) { showing = true; post({ kind: 'shown', how: how || '' }); }
            };
            // (1.3.62) out of sight again — Alta between one part and the next, taking its time — until the next is laid out
            veilAgain = function () {
              if (!veils || !showing) return;
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
              // (never longer than this, whatever happens)
              setTimeout(function () { reveal('timeout'); }, 9000);
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
            // (1.3.70) the small graphs: a graph in what a question asks (not one to answer on), larger than 300 × 220, drawn
            // at a zoom that brings it down to that
            var MINI_W = 300, MINI_H = 220, picEls = [];
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
            var publish = function () {
              var on = document.documentElement.hasAttribute('data-simpl-q');
              var bs = on ? bars() : [];
              buttonEls = bs.map(function (b) { return b.el; });
              var bsSaid = JSON.stringify(bs.map(function (b) { return { label: b.label, primary: b.primary, enabled: b.enabled }; }));
              if (bsSaid !== lastButtons) { lastButtons = bsSaid; post({ kind: 'buttons', items: JSON.parse(bsSaid) }); }
              var ps = on ? panels() : [];
              if (on) minis(); else picEls = [];
              var bottom = ps.reduce(function (m, p) { return Math.max(m, p.y + p.h); }, 0);
              var sx0 = window.scrollX || 0, sy0 = window.scrollY || 0;
              var pr = picEls.map(function (el) { var r = el.getBoundingClientRect(); return { x: Math.round(r.left + sx0), y: Math.round(r.top + sy0), w: Math.round(r.width), h: Math.round(r.height) }; });
              var psSaid = JSON.stringify({ panels: ps, height: ps.length ? bottom + 48 : 0, gen: gen, pics: pr });
              if (psSaid !== lastPanels) { lastPanels = psSaid; var m = JSON.parse(psSaid); m.kind = 'panels'; post(m); }
            };
            setInterval(publish, 350);
            window.addEventListener('resize', function () { publish(); });
            window.__simplButtons = {
              press: function (i) { var b = buttonEls[i]; if (!b || b.disabled || b.getAttribute('aria-disabled') === 'true') return false; b.click(); setTimeout(publish, 60); return true; }
            };
            var told = '', badSince = 0, timer = null, wasQuestion = false, loadingSince = 0;
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
                var extra = all('[role="dialog"], [role="alertdialog"], body > .MuiModal-root, body > [role="presentation"], #MathJax_Hidden, #MathJax_Font_Test, #MathJax_Message')
                  .concat(all('svg').filter(function (g) { return g.querySelector('symbol, defs'); }))
                  .filter(function (e) { return !r.keep.some(function (k) { return k.contains(e); }); });
                apply(r.keep.concat(extra));
                pics(r.keep);
                r.keep.forEach(function (k) { [].forEach.call(k.querySelectorAll('button'), function (b) { if (/^(feedback|content attribution\b.*)$/.test(norm(b.innerText || b.textContent || b.getAttribute('aria-label')))) mark(b, 'data-simpl-hide'); }); });
                report(r.state, r);
                publish();
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
              if (between) veilAgain();
              var wait = between ? 8000 : (wasQuestion ? 1500 : 8000);
              if (Date.now() - badSince < wait) { schedule(wait - (Date.now() - badSince) + 20); return; }
              unmark();
              report(r.state, r);
              publish();
              reveal('as it is');
            };
            var schedule = function (ms) { if (timer) clearTimeout(timer); timer = setTimeout(tick, typeof ms === 'number' ? ms : 120); };
            // (1.3.68) the question shown once Alta has finished drawing it — nothing changed for a moment (its maths set, its
            // answer box built, its pictures in) — or after two and a half seconds at most: the app's skeleton meanwhile
            var lastChange = Date.now(), steadyTimer = null, steadySince = 0;
            var revealSteady = function (state) {
              if (!steadySince) steadySince = Date.now();
              var quiet = Date.now() - lastChange;
              var spinning = state === 'overview' ? busy(document.body) : [].some.call(document.querySelectorAll('[data-simpl-keep]'), busy);
              var pending = spinning || !!document.querySelector('.MathJax_Processing, .lrn_loading, [class*="lrn-loading"]')
                || [].some.call(document.querySelectorAll('[data-simpl-keep] img'), function (i) { return !i.complete; });
              // (1.3.70: as long as four seconds while something in it still says it is loading)
              if ((quiet >= 350 && !pending) || Date.now() - steadySince > (spinning ? 4000 : 2500)) {
                steadySince = 0;
                reveal(state);
                return;
              }
              if (steadyTimer) clearTimeout(steadyTimer);
              steadyTimer = setTimeout(function () { steadyTimer = null; revealSteady(state); }, 120);
            };
            try { new MutationObserver(function () { lastChange = Date.now(); schedule(); }).observe(document, { childList: true, subtree: true, characterData: true }); } catch (e) {}
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
              post(o || { kind: 'cover', none: true });
            };
            setInterval(coverTick, 700);
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

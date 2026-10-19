/* (Mac 1.3.114, Windows 0.3.32) Macmillan Learning's assessment, skinned as Knewton Alta's is: run in every page and
 * frame of the Macmillan popup. In the Integration Hub page (the frame's host) the assessment's frame is made the whole
 * page. In the assessment player (SAC, assessments.macmillanlearning.com/sac/…) Macmillan's own chrome is put away — the
 * side list of questions and the grading settings, the bar with the score, Resources, AI Tutor, Solution and the main
 * button, the arrows and "Question 1 of 22" — and drawn by the app around the page instead (the rail, the header, the bar
 * at the foot), from what this script reads and reports; the question itself stays Macmillan's, its parts laid on cards.
 * The app's presses come back as window.__simplMacmillan(command, argument). Nothing is left out: every one of those
 * controls has its place in the app, and Show Browser puts the page back as Macmillan draws it.
 * Read by the app (mac/Simpl/Engine/MacmillanSession.swift, windows/SimplFluent/Screens/Alta/MacmillanView.cs); the
 * selectors are Macmillan's own data-test-id attributes (scripts/dev/mock-macmillan.mjs is laid out the same). */
(function () {
  'use strict';
  if (window.__simplMacmillanSkinOn || !/^https?:$/.test(location.protocol)) return;
  window.__simplMacmillanSkinOn = true;

  function post(m) {
    try {
      if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.simplMacmillan) window.webkit.messageHandlers.simplMacmillan.postMessage(JSON.stringify(m));
      else if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(JSON.stringify(Object.assign({ macmillan: true }, m)));
    } catch (e) { /* (no app around it) */ }
  }
  post({ kind: 'frame' });

  // (Mac 1.3.115, Windows 0.3.33) The assessment player is known by what is on it, not only by its address: Macmillan's
  // frame is posted into (an LTI launch), and frames of its own inside it (its widgets) sit under /sac/ too
  function isPlayer() { return !!(q('main#sac') || q('#question-nav') || q('#side-nav') || tid('btn-sac-main') || openButton()); }
  function openButton() { return qa('button').filter(function (b) { return /^\s*open assignment\s*$/i.test(words(b)); })[0] || null; }
  var skinned = window.__simplMacmillanSkinned !== false; // (Show Browser: false, set before the page by the app)

  function css(id, text) {
    var s = document.getElementById(id);
    if (!s) { s = document.createElement('style'); s.id = id; (document.head || document.documentElement).appendChild(s); }
    s.textContent = text;
  }
  function q(sel, root) { try { return (root || document).querySelector(sel); } catch (e) { return null; } }
  function qa(sel, root) { try { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); } catch (e) { return []; } }
  function words(el) { return el ? String(el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim() : ''; }
  function tid(id) { return q('[data-test-id="' + id + '"]'); }

  // ---- the Integration Hub page: the assessment's frame the whole page ----
  function hub() {
    // (Mac 1.3.115) the Integration Hub's own frame for the assessment: named launch-frame and posted into, with no /sac/
    // address of its own on the page — that, or one that names /sac/
    var f = qa('iframe').filter(function (x) { return x.getAttribute('name') === 'launch-frame' || x.getAttribute('data-test-id') === 'molecule-iframe' || /\/sac\/(?!static\/)/.test(x.getAttribute('src') || ''); })[0];
    if (!f) return false;
    if (!skinned) { css('simpl-mm-hub', ''); return true; }
    f.setAttribute('data-simpl-sac', '');
    // (Mac 1.3.116) laid on the popup's glass: the frame clear, and nothing of the hub's own page left to show through it
    css('simpl-mm-hub', 'html,body{overflow:hidden!important;background:transparent!important}body *{visibility:hidden!important}' +
      'iframe[data-simpl-sac]{visibility:visible!important;position:fixed!important;inset:0!important;width:100vw!important;height:100vh!important;border:0!important;z-index:2147483646!important;background:transparent!important}' +
      '[id^="_pendo"],[class*="_pendo"],[data-test-id="atom-hubbox"]>.hub_molecule_app_header{display:none!important}');
    return true;
  }

  // ---- the assessment player ----
  // (Mac 1.3.116, Windows 0.3.34) Laid on the popup's glass, light or dark as the app is (window.__simplMacmillanDark, set
  // before the page and told again by 'theme'): the page's grounds clear, the question one panel of the glass (1.3.117), words and fields
  // in the app's ink, a picture on a white plate of its own in dark (drawn for white paper). And as wide as the popup:
  // Macmillan's question is a 960-px sheet, set 25 px in, beside the side panel's 300 px kept even with the panel put
  // away, so it ran under the edge (a scroll bar along the foot) and its right column — the diagram, in a two-column
  // question — was cut off. The sheet fills the width now; the two columns kept side by side, the picture on the right,
  // and one column when narrow.
  var ROWS = ''; for (var n = 1; n <= 60; n++) ROWS += 'html.simpl-mm .parent-module-container[data-simpl-rows="' + n + '"]{grid-template-rows:repeat(' + n + ',auto) 1fr}';
  var SKIN =
    'html.simpl-mm{--mm-ink:#1c1c1e;--mm-ink2:rgba(60,60,67,.62);--mm-card:rgba(255,255,255,.74);--mm-line:rgba(0,0,0,.07);--mm-field:rgba(255,255,255,.92);--mm-good:#1a6b30;--mm-good-bg:rgba(52,199,89,.16);--mm-bad:#c01d43;--mm-bad-bg:rgba(255,59,48,.13);--mm-good-line:rgba(52,199,89,.35);--mm-bad-line:rgba(255,59,48,.3);--mm-on:#fff;--mm-accent:#0a84ff;--mm-accent-ring:rgba(10,132,255,.25);--mm-field2:rgba(0,0,0,.04);--mm-pop:#fff;--mm-rim:rgba(255,255,255,.7);color-scheme:light}' +
    'html.simpl-mm.simpl-mm-dark{--mm-ink:#f2f2f7;--mm-ink2:rgba(235,235,245,.62);--mm-card:rgba(255,255,255,.075);--mm-line:rgba(255,255,255,.11);--mm-field:rgba(255,255,255,.09);--mm-good:#5ddb7d;--mm-good-bg:rgba(52,199,89,.2);--mm-bad:#ff8098;--mm-bad-bg:rgba(255,69,58,.2);--mm-good-line:rgba(93,219,125,.45);--mm-bad-line:rgba(255,128,152,.4);--mm-on:#0b2a14;--mm-accent:#409cff;--mm-accent-ring:rgba(64,156,255,.3);--mm-field2:rgba(255,255,255,.08);--mm-pop:#2c2c2e;--mm-rim:rgba(255,255,255,.08);color-scheme:dark}' +
    'html.simpl-mm,html.simpl-mm body{background:transparent!important;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif!important}' +
    'html.simpl-mm [data-simpl-hide],html.simpl-mm #check-bar,html.simpl-mm #attempts-holder,html.simpl-mm #toggle-fullscreen,html.simpl-mm .nav-scroll-arrow-button,' +
    'html.simpl-mm [aria-label="Scroll to Bottom"],html.simpl-mm [aria-label="Scroll to Top"],html.simpl-mm [id^="_pendo"],html.simpl-mm [class*="_pendo"]{display:none!important}' +
    'html.simpl-mm #root,html.simpl-mm #sac,html.simpl-mm #main,html.simpl-mm .raptor-container,html.simpl-mm #raptor-content-container,html.simpl-mm .content-area,html.simpl-mm .authoring-area,html.simpl-mm .parent-module-container{background:transparent!important;max-width:none!important;box-shadow:none!important}' +
    // the width: the whole of it, never wider
    'html.simpl-mm #main{width:auto!important;flex:1 1 auto!important;min-width:0!important;overflow:hidden!important}' +
    'html.simpl-mm .raptor-container{padding:18px 26px 0!important;box-sizing:border-box!important;min-width:0!important;margin:0!important}' +
    'html.simpl-mm #raptor-content-container{overflow-x:hidden!important;overflow-y:auto!important;width:auto!important;padding-bottom:90px!important;box-sizing:border-box!important}' +
    'html.simpl-mm #raptor-content-container .content-area{position:relative!important;left:auto!important;right:auto!important;top:auto!important;width:auto!important;max-width:780px!important;height:auto!important;margin:0 auto!important;overflow:visible!important}' +
    'html.simpl-mm #raptor-content-container .content-area:has(.two_col_single_right){max-width:1080px!important}' +
    'html.simpl-mm .authoring-area{width:auto!important;height:auto!important;overflow:visible!important;margin:0!important;padding:0!important}' +
    'html.simpl-mm .parent-module-container{min-height:0!important;height:auto!important;padding:0!important;display:flow-root!important}' +
    'html.simpl-mm [copyrightmessagestyle]{position:static!important;transform:none!important;left:auto!important;top:auto!important}' +
    'html.simpl-mm [data-test-id="copyright-box"]{font-size:11px!important;color:var(--mm-ink2)!important;margin:0 0 6px!important}' +
    // (Mac 1.3.119) never wider than the view: nothing of the page runs past its right edge
    'html.simpl-mm,html.simpl-mm body{overflow-x:hidden!important;max-width:100vw!important}' +
    'html.simpl-mm :is(#root,#sac,#main,.raptor-container,#raptor-content-container,.content-area,.authoring-area,.parent-module-container){max-width:100%!important;box-sizing:border-box!important;min-width:0!important}' +
    'html.simpl-mm .parent-module-container{width:auto!important}' +
    // (Mac 1.3.117) the whole question one panel of the glass (its parts in it, not each a card of its own)
    'html.simpl-mm .parent-module-container{background:var(--mm-card)!important;border-radius:22px!important;padding:20px 24px 12px!important;box-sizing:border-box!important;box-shadow:0 1px 3px rgba(0,0,0,.06),0 8px 28px rgba(0,0,0,.06),0 0 0 1px var(--mm-line),inset 0 1px 0 var(--mm-rim)!important;-webkit-backdrop-filter:blur(24px) saturate(170%);backdrop-filter:blur(24px) saturate(170%)}' +
    // (Mac 1.3.119) on the Mac the app lays the page on a panel of real glass of its own (window.__simplMacmillanGlass):
    // the page's panel then clear, the question straight on that glass
    'html.simpl-mm.simpl-mm-glass .parent-module-container{background:transparent!important;box-shadow:none!important;-webkit-backdrop-filter:none!important;backdrop-filter:none!important;border-radius:0!important;padding:4px 6px 12px!important}' +
    'html.simpl-mm.simpl-mm-glass .raptor-container{padding:16px 20px 0!important}' +
    'html.simpl-mm article.module-container{background:transparent!important;border-radius:0!important;padding:6px 0!important;margin:0 0 10px!important;min-height:0!important;box-sizing:border-box!important;box-shadow:none!important;line-height:1.5;color:var(--mm-ink)!important}' +
    // (Mac 1.3.121) a picture a small preview that always fits: whatever fixed width Macmillan draws it (or its wrappers)
    // at, it is held to its column and to a short height, and enlarged on a press (below)
    'html.simpl-mm [data-simpl-pic]{max-width:100%!important;overflow:hidden}' +
    'html.simpl-mm [data-simpl-pic] *:not(img){width:auto!important;max-width:100%!important;min-width:0!important;box-sizing:border-box!important}' +
    'html.simpl-mm [data-simpl-pic] img{display:block!important;width:auto!important;max-width:100%!important;max-height:300px!important;height:auto!important;object-fit:contain;margin:0 auto!important}' +
    '@media (max-width:1100px){html.simpl-mm [data-simpl-pic] img{max-height:220px!important}}' +
    'html.simpl-mm .raw-html img,html.simpl-mm article.module-container img{max-width:100%!important;height:auto!important}' +
    'html.simpl-mm .raw-html :has(> img),html.simpl-mm article.module-container :has(> img){max-width:100%!important}' +
    // (Mac 1.3.120) the question drawn a little smaller, so more of it (and its pictures) fits
    'html.simpl-mm #raptor-content-container .content-area{zoom:.86}' +
    // (Mac 1.3.120) a picture enlarged on a press, as Alta's graphs are: a hint of it at its corner, then large over the page
    'html.simpl-mm :is([data-simpl-pic], article.module-container .raw-html) img{cursor:zoom-in}' +
    'html.simpl-mm [data-simpl-pic]{position:relative}' +
    'html.simpl-mm [data-simpl-pic]::after{content:"\\2922";position:absolute;top:12px;right:12px;width:26px;height:26px;border-radius:13px;display:grid;place-items:center;font-size:14px;line-height:1;color:#1c1c1e;background:rgba(255,255,255,.88);box-shadow:0 1px 4px rgba(0,0,0,.18);pointer-events:none}' +
    '#simpl-mm-zoom{position:fixed;inset:0;z-index:2147483647;display:grid;place-items:center;padding:24px;background:rgba(0,0,0,.6);-webkit-backdrop-filter:blur(8px);backdrop-filter:blur(8px);cursor:zoom-out;animation:simpl-mm-zoom-in .18s ease-out}' +
    '#simpl-mm-zoom img{max-width:100%;max-height:100%;object-fit:contain;background:#fff;border-radius:14px;padding:10px;box-sizing:border-box;box-shadow:0 20px 60px rgba(0,0,0,.4)}' +
    '@keyframes simpl-mm-zoom-in{from{opacity:0}}' +
    'html.simpl-mm article.module-container .raw-html p{margin:0 0 10px!important}html.simpl-mm article.module-container .raw-html p:last-child{margin-bottom:0!important}' +
    'html.simpl-mm article.module-container>.sl-module{padding-bottom:0!important;overflow:visible!important;max-width:100%!important}' +
    // (Mac 1.3.119) any question with a picture: the picture up the right, the words, the questions and their answers down
    // the left in their order (Macmillan's own two-column layouts and its one-column ones alike). A grid of the parts on
    // the left a row each, then a last row that takes what a tall picture needs (picture() counts the rows)
    'html.simpl-mm .authoring-area .module-container{display:block!important;width:auto!important;float:none!important;clear:none!important}' +
    'html.simpl-mm .parent-module-container[data-simpl-rows]{display:grid!important;grid-template-columns:minmax(0,1fr) minmax(0,36%);column-gap:24px;align-content:start}' +
    'html.simpl-mm .parent-module-container[data-simpl-rows] > *{grid-column:1;min-width:0}' +
    'html.simpl-mm .parent-module-container[data-simpl-rows] > [data-simpl-pic]{grid-column:2;grid-row:1 / -1;align-self:start;position:sticky;top:8px}' +
    ROWS +
    'html.simpl-mm #raptor-content-container .content-area:has([data-simpl-pic]){max-width:1080px!important}' +
    '@media (max-width:640px){html.simpl-mm .parent-module-container[data-simpl-rows]{display:block!important}html.simpl-mm .parent-module-container[data-simpl-rows] > [data-simpl-pic]{position:static}}' +
    'html.simpl-mm .raw-html img,html.simpl-mm article.module-container img{max-width:100%!important;height:auto!important;border-radius:10px}' +
    'html.simpl-mm.simpl-mm-dark article.module-container :is(.raw-html,figure) img{background:#fff!important;padding:6px!important;box-sizing:border-box!important}' +
    // words and fields in the app's ink (dark: Macmillan's black put to the light ink)
    'html.simpl-mm.simpl-mm-dark #raptor-content-container :is(.parent-module-container > div, article.module-container:not([data-simpl-plate])) :is(p,span,div,label,li,td,th,h1,h2,h3,h4,h5,h6,b,i,em,strong,sup,sub,a,mjx-container,legend,caption,figcaption){color:var(--mm-ink)!important}' +
    // (Mac 1.3.119) a part of Macmillan's that draws its own (a drag-and-drop labelling with its answer bank, a graph, a
    // table of its own): in dark kept as drawn, on a white plate — its words were made light on its own white boxes
    'html.simpl-mm article.module-container[data-simpl-plate]>.sl-module{overflow:auto!important}' +
    'html.simpl-mm #raptor-content-container .content-area:has([data-simpl-plate]){max-width:1080px!important}' +
    'html.simpl-mm.simpl-mm-dark article.module-container[data-simpl-plate]{background:#fff!important;color:#1c1c1e!important;border-radius:14px!important;padding:12px!important;margin-bottom:14px!important}' +
    'html.simpl-mm.simpl-mm-dark #raptor-content-container :is(table,td,th){border-color:var(--mm-line)!important}' +
    'html.simpl-mm.simpl-mm-dark #raptor-content-container th{background:rgba(255,255,255,.06)!important}' +
    'html.simpl-mm input[type=text]:not(.numeric-entry-input):not(.numeric-entry-sci-not-coeff),html.simpl-mm input:not([type]):not(.numeric-entry-input):not(.numeric-entry-sci-not-coeff),html.simpl-mm textarea,html.simpl-mm select{border-radius:8px!important;background:var(--mm-field)!important;color:var(--mm-ink)!important;border:1px solid var(--mm-line)!important}' +
    // (Mac 1.3.117) a number's answer, one field sized as a field (it stretched across the page): its frame the field, the
    // box inside it clear; ×10 and the power in the app's ink
    'html.simpl-mm .numeric-entry-table{width:auto!important;max-width:100%!important}' +
    'html.simpl-mm .numeric-entry-table .graded-equations-td{width:auto!important}' + // (Mac 1.3.120: its cells their own size — the unit beside the field, not across the page)
    'html.simpl-mm .numeric-entry-table .graded-equations-td.not-considered-in-grading-module-frame{width:auto!important}' +
    'html.simpl-mm .numeric-entry-border{width:15em!important;max-width:100%!important;box-sizing:border-box!important;background:var(--mm-field)!important;border:1px solid var(--mm-line)!important;border-radius:10px!important;box-shadow:0 1px 2px rgba(0,0,0,.06)!important}' +
    'html.simpl-mm .numeric-entry-border.focused{border-color:var(--mm-accent)!important;box-shadow:0 0 0 3px var(--mm-accent-ring)!important}' +
    'html.simpl-mm .numeric-entry-border.invalid{border-color:var(--mm-bad)!important;background:var(--mm-bad-bg)!important}' +
    'html.simpl-mm .numeric-entry-border.disabled{opacity:.85}' +
    'html.simpl-mm .numeric-entry-border :is(.numeric-entry-input,.numeric-entry-sci-not-coeff){background:transparent!important;border:0!important;box-shadow:none!important;color:var(--mm-ink)!important;outline:none!important}' +
    'html.simpl-mm .numeric-entry-border .numeric-entry-input-exponent{background:var(--mm-field2)!important;border:1px solid var(--mm-line)!important;border-radius:6px!important;color:var(--mm-ink)!important}' +
    'html.simpl-mm .numeric-entry-times10{color:var(--mm-ink)!important}' +
    // Macmillan's tools menu for a number (×10ʸ), a popover of the app's: in dark it was a white box with light words
    'html.simpl-mm .numeric-entry-toolmenu{background:var(--mm-pop)!important;border:1px solid var(--mm-line)!important;border-radius:12px!important;padding:6px!important;box-shadow:0 8px 24px rgba(0,0,0,.18)!important;font-family:inherit!important;width:180px!important}' +
    'html.simpl-mm .numeric-entry-toolmenu>span{background:transparent!important;color:var(--mm-ink2)!important;font-size:11px!important;font-weight:600!important;letter-spacing:.04em;height:auto!important;padding:2px 6px 4px!important}' +
    'html.simpl-mm .numeric-entry-toolmenu>button{background:var(--mm-field2)!important;color:var(--mm-ink)!important;border:1px solid var(--mm-line)!important;border-radius:8px!important;margin-top:2px!important;text-transform:none!important}' +
    'html.simpl-mm .numeric-entry-toolmenu>button *{color:inherit!important}' +
    // (Mac 1.3.117, Windows 0.3.35) Macmillan's Solved / Try Again along the foot put away: its verdict is the app's own
    // Correct or Incorrect pill in the bar, as Alta's is
    'html.simpl-mm #results{display:none!important}' +
    // (Mac 1.3.117) each answer's own Correct or Incorrect, a pill that reads in light and dark (Alta's: tick or cross,
    // the tone's ink on its tint) — in dark it was white words on Macmillan's pale green
    'html.simpl-mm #raptor-content-container div.grading-status.grading-status.grading-status.grading-status{display:inline-flex!important;align-items:center;gap:5px;margin-top:6px!important;padding:3px 11px 3px 8px!important;border-radius:999px!important;font:600 13px/20px -apple-system,BlinkMacSystemFont,"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif!important;background:var(--mm-good-bg)!important;color:var(--mm-good)!important;box-shadow:inset 0 0 0 1px var(--mm-good-line)!important}' +
    'html.simpl-mm #raptor-content-container div.grading-status.grading-status.grading-status.grading-status::before{content:"\\2713";display:inline-grid;place-items:center;width:15px;height:15px;border-radius:50%;background:var(--mm-good)!important;color:var(--mm-on)!important;font-size:10px;font-weight:800}' +
    'html.simpl-mm #raptor-content-container :is(.module-graded-incorrect,.module-graded-missed) div.grading-status.grading-status.grading-status.grading-status{background:var(--mm-bad-bg)!important;color:var(--mm-bad)!important;box-shadow:inset 0 0 0 1px var(--mm-bad-line)!important}' +
    'html.simpl-mm #raptor-content-container :is(.module-graded-incorrect,.module-graded-missed) div.grading-status.grading-status.grading-status.grading-status::before{content:"\\2715";background:var(--mm-bad)!important}';
  var dark = !!window.__simplMacmillanDark;

  function mark() {
    var nav = q('#question-nav');
    var side = q('#side-nav') || (nav && (nav.closest('nav') || nav.parentElement));
    if (side) side.setAttribute('data-simpl-hide', '');
  }

  // (Mac 1.3.119) The question's picture: a part that is a picture and next to no words. Marked, it goes up the right
  // (the skin's grid), the rest down the left; the left's parts counted for the grid's rows.
  // (Mac 1.3.122) the picture never past the right edge, whatever the page's widths and zoom do: measured as drawn, and
  // held to the room it has; on a narrower window it is a smaller preview still
  function fitPics() {
    qa('[data-simpl-pic] img').forEach(function (img) {
      var box = img.closest('[data-simpl-pic]'); if (!box) return;
      img.style.removeProperty('max-width');
      var r = img.getBoundingClientRect(); if (!r.width) return;
      var z = img.offsetWidth ? r.width / img.offsetWidth : 1;
      var edge = Math.min(document.documentElement.clientWidth, (box.closest('.parent-module-container') || document.documentElement).getBoundingClientRect().right) - 16;
      if (r.right > edge) img.style.setProperty('max-width', Math.max(80, Math.floor((r.width - (r.right - edge)) / (z || 1))) + 'px', 'important');
    });
  }
  var fitT = 0;
  window.addEventListener('resize', function () { clearTimeout(fitT); fitT = setTimeout(fitPics, 60); });
  document.addEventListener('load', function (e) { if (e.target && e.target.tagName === 'IMG') fitPics(); }, true);
  function picture() {
    // (a part of a kind the skin knows reads in the app's ink; any other kind keeps its own colours on a plate)
    qa('article.module-container').forEach(function (m) {
      var t = (m.querySelector('.sl-module[data-module-type], [data-module-type]') || {}).getAttribute ? m.querySelector('[data-module-type]').getAttribute('data-module-type') : '';
      var known = !t || /^(static_textfield|numeric_entry|multiple_choice|multiple_select|short_answer|text_entry)$/.test(t);
      if (!known && !m.hasAttribute('data-simpl-plate')) m.setAttribute('data-simpl-plate', '');
      if (known && m.hasAttribute('data-simpl-plate')) m.removeAttribute('data-simpl-plate');
    });
    qa('.parent-module-container').forEach(function (box) {
      var kids = Array.prototype.slice.call(box.children);
      var pic = kids.filter(function (k) {
        if (!k.matches || !k.matches('article.module-container')) return false;
        var img = k.querySelector('img');
        if (!img || img.closest('mjx-container')) return false;
        var w = img.naturalWidth || img.width || 0;
        if (w && w < 80) return false;
        return words(k).length < 40;
      })[0] || null;
      kids.forEach(function (k) { if (k !== pic && k.hasAttribute('data-simpl-pic')) k.removeAttribute('data-simpl-pic'); });
      if (!pic) { if (box.hasAttribute('data-simpl-rows')) box.removeAttribute('data-simpl-rows'); return; }
      if (!pic.hasAttribute('data-simpl-pic')) pic.setAttribute('data-simpl-pic', '');
      var rows = String(Math.min(60, Math.max(1, kids.length - 1)));
      if (box.getAttribute('data-simpl-rows') !== rows) box.setAttribute('data-simpl-rows', rows);
    });
    fitPics();
  }

  // (Mac 1.3.115) Macmillan keeps its side panel folded when it was last folded — and folded, it does not draw the list of
  // questions or the grading settings at all. Skinned, the panel is hidden anyway, so it is unfolded (once each) for the
  // app to read; Show Browser puts it back as Macmillan left it.
  var unfolded = { nav: false, policies: false };
  function unfold() {
    if (!skinned) return;
    if (!unfolded.nav && !q('#question-nav')) {
      var t = q('[data-test-id="side-nav-button-title"]');
      var btn = (t && t.closest('button')) || q('#side-nav button[aria-expanded="false"]');
      if (btn && btn.getAttribute('aria-expanded') === 'false') { unfolded.nav = true; btn.click(); }
    }
    var pol = tid('show-policy-details-button');
    if (!unfolded.policies && pol && pol.getAttribute('aria-expanded') === 'false') { unfolded.policies = true; pol.click(); }
  }
  function completedOf() {
    var text = words(q('[data-test-id="side-nav-button-title"]')) || ((q('[aria-description^="Completed"]') || {}).getAttribute ? q('[aria-description^="Completed"]').getAttribute('aria-description') : '');
    return String(text || '').match(/Completed (\d+) of (\d+)/i) || [];
  }

  function cover() {
    // (before Open Assignment: the assignment's name, its due line, how many questions, the grading settings)
    var open = openButton();
    if (!open || q('#question-nav') || tid('btn-sac-main')) return null;
    var name = words(q('h1')) || words(q('h2')) || document.title;
    var due = (words(document.body).match(/Due [A-Z][a-z]{2}, [A-Z][a-z]{2} \d{1,2}, \d{1,2}:\d{2} ?[ap]m/) || [])[0] || '';
    var count = (words(document.body).match(/(\d+) questions?/i) || [])[1];
    return { name: name, due: due, count: count ? Number(count) : null, policies: policies() };
  }

  function policies() {
    var list = qa('li[data-test-id^="policy-"]').map(words).filter(Boolean);
    if (list.length) return list;
    // (as the page words them, without test ids)
    var h = qa('h4,h3').filter(function (x) { return /grading settings/i.test(words(x)); })[0];
    var ul = h && h.parentElement && (h.parentElement.nextElementSibling || h.parentElement.querySelector('ul'));
    return ul ? qa('li', ul).map(words).filter(Boolean) : [];
  }

  function state() {
    var c = cover();
    if (c) return { kind: 'state', page: 'cover', dark: dark, cover: c };
    if (!q('#question-nav') && !tid('btn-sac-main')) return { kind: 'state', page: 'other', dark: dark, title: document.title };
    var header = words(tid('nav-question-header'));
    var m = header.match(/Question (\d+) of (\d+)/i) || [];
    var title = document.title.replace(/^Question \d+ of \d+ - /i, '').trim();
    var items = qa('#question-nav li[data-test-id$="-row"], [data-test-id="items"] li[data-test-id$="-row"]').filter(function (li, i, all) { return all.indexOf(li) === i; }).map(function (li) {
      var t = li.getAttribute('data-test-id') || '';
      var mm = t.match(/^item-(\d+)-(.+)-row$/) || [];
      var parts = qa(':scope > div > div, :scope > div', li).map(words).filter(Boolean);
      var label = words(q('[data-test-id="item-' + mm[1] + '-number"]', li)) || ('Q' + mm[1]);
      var note = (parts.filter(function (p) { return p !== label && !/^\+\d+$/.test(p) && p.indexOf(label) !== 0; })[0]) || '';
      return { n: Number(mm[1]) || 0, label: label, state: mm[2] || 'default', note: note, points: words(q('[data-test-id="item-' + mm[1] + '-points"]', li)) };
    });
    var scoreEl = tid('final-score-fraction');
    var sm = ((scoreEl && scoreEl.getAttribute('aria-label')) || '').match(/(\d+(?:\.\d+)?) out of (\d+(?:\.\d+)?)/);
    var done = completedOf();
    var main = tid('btn-sac-main');
    var att = tid('attempts-dropdown-button');
    var choices = qa('[data-test-id="attempts"] button').map(function (b) { return words(b); }).filter(Boolean);
    // (Mac 1.3.117) Macmillan's verdict on the question, from its bar along the foot: right, wrong, or none yet
    var bar = q('footer#results');
    var verdict = !bar ? '' : bar.classList.contains('correct') ? 'right' : bar.classList.contains('incorrect') ? 'wrong' : '';
    var results = qa('[data-test-id^="result-frame-"]').map(function (r) { return r.getAttribute('aria-label') || ''; }).filter(Boolean);
    return {
      kind: 'state', page: 'question', dark: dark, title: title, index: Number(m[1] || 0) - 1, count: Number(m[2] || items.length),
      completed: done[1] ? Number(done[1]) : items.filter(function (i) { return i.state === 'success'; }).length,
      score: sm ? [Number(sm[1]), Number(sm[2])] : null, policies: policies(), items: items,
      main: main ? (main.getAttribute('aria-label') || words(main)) : null, mainOn: !!(main && !main.disabled),
      tools: { resources: !!q('[aria-label="Open Resources"]'), tutor: !!q('[aria-label="Open AI Tutor"]'), solution: !!q('[aria-label="Open Solution"]'), layout: !!tid('switch-to-new-layout') },
      attempts: att ? { label: words(att), choices: choices } : null, results: results, verdict: verdict,
      first: !!(tid('nav-item-back') && tid('nav-item-back').disabled), last: !!(tid('nav-item-forward') && tid('nav-item-forward').disabled),
    };
  }

  var last = '';
  var timer = 0;
  function report() {
    clearTimeout(timer);
    timer = setTimeout(function () {
      // (Mac 1.3.115) only the player itself says what it shows: a frame of its own inside it (a widget) never does, or
      // its blank page would stand in for the question; a page that is not the player and holds its frame is the hub
      if (!isPlayer()) { if (hub()) post({ kind: 'hub' }); return; }
      if (skinned) { document.documentElement.classList.add('simpl-mm'); css('simpl-mm-skin', SKIN); mark(); picture(); unfold(); }
      else { document.documentElement.classList.remove('simpl-mm'); }
      document.documentElement.classList.toggle('simpl-mm-dark', skinned && dark);
      document.documentElement.classList.toggle('simpl-mm-glass', skinned && !!window.__simplMacmillanGlass);
      var s = state();
      var text = JSON.stringify(s);
      if (text !== last) { last = text; post(s); }
    }, 120);
  }

  function click(el) { if (!el) return false; el.click(); report(); return true; }

  // the app's presses
  window.__simplMacmillan = function (cmd, arg) {
    switch (cmd) {
      case 'open': return click(openButton());
      case 'go': return click(q('#question-nav li[data-test-id^="item-' + Number(arg) + '-"]'));
      case 'prev': return click(tid('nav-item-back'));
      case 'next': return click(tid('nav-item-forward'));
      case 'main': return click(tid('btn-sac-main'));
      case 'resources': return click(q('[aria-label="Open Resources"]'));
      case 'tutor': return click(q('[aria-label="Open AI Tutor"]'));
      case 'solution': return click(q('[aria-label="Open Solution"]'));
      case 'layout': return click(tid('switch-to-new-layout'));
      case 'attempt': {
        var list = qa('[data-test-id="attempts"] button');
        if (!list.length) { click(tid('attempts-dropdown-button')); list = qa('[data-test-id="attempts"] button'); }
        return click(list[Number(arg) || 0]);
      }
      case 'skin': skinned = !!arg; window.__simplMacmillanSkinned = skinned; last = ''; report(); if (!skinned && !isPlayer()) css('simpl-mm-hub', ''); return true;
      case 'state': last = ''; report(); return true;
      // (Mac 1.3.116) light or dark, as the app is now
      case 'theme': dark = !!arg; window.__simplMacmillanDark = dark; last = ''; report(); return true;
    }
    return false;
  };

  // (Mac 1.3.120) a press on a question's picture shows it large over the page; a press anywhere or Escape puts it away
  document.addEventListener('click', function (e) {
    if (!skinned || !document.documentElement.classList.contains('simpl-mm')) return;
    var z = document.getElementById('simpl-mm-zoom');
    if (z) { z.remove(); e.preventDefault(); e.stopPropagation(); return; }
    var img = e.target && e.target.closest && e.target.closest('[data-simpl-pic] img, article.module-container .raw-html img');
    if (!img || img.closest('a,button,[data-simpl-plate]')) return;
    e.preventDefault(); e.stopPropagation();
    // (Mac 1.3.122) in the Mac app it opens in a window of its own, as Knewton Alta's graphs do: its bytes as the page
    // has them (a canvas, or a fetch), else its address for the app to read
    if (window.__simplMacmillanGlass && window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.simplMacmillan) { popOut(img); return; }
    var box = document.createElement('div'); box.id = 'simpl-mm-zoom'; box.setAttribute('role', 'dialog'); box.setAttribute('aria-label', img.alt || 'Picture');
    var big = document.createElement('img'); big.src = img.currentSrc || img.src; big.alt = img.alt || '';
    box.appendChild(big); document.body.appendChild(box);
  }, true);
  function popOut(img) {
    var src = img.currentSrc || img.src;
    var title = (q('[data-test-id="question-title"], .question-title, h1') || {}).textContent || '';
    var send = function (b64) { post(b64 ? { kind: 'pic', data: b64, title: title.trim() } : { kind: 'pic', url: src, title: title.trim() }); };
    try {
      var c = document.createElement('canvas'); c.width = img.naturalWidth || img.width; c.height = img.naturalHeight || img.height;
      var g = c.getContext('2d'); g.fillStyle = '#fff'; g.fillRect(0, 0, c.width, c.height); g.drawImage(img, 0, 0, c.width, c.height);
      send(c.toDataURL('image/png').split(',')[1]); return;
    } catch (err) { /* (a picture from elsewhere: the canvas is not ours to read) */ }
    try {
      fetch(src, { credentials: 'include' }).then(function (r) { return r.blob(); }).then(function (b) { var fr = new FileReader(); fr.onload = function () { send(String(fr.result).split(',')[1]); }; fr.readAsDataURL(b); }).catch(function () { send(null); });
    } catch (err) { send(null); }
  }
  document.addEventListener('keydown', function (e) { var z = document.getElementById('simpl-mm-zoom'); if (z && e.key === 'Escape') { z.remove(); e.stopPropagation(); } }, true);

  function watch() {
    report();
    try { new MutationObserver(report).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-test-id', 'aria-label', 'disabled', 'value', 'class'] }); } catch (e) {}
    document.addEventListener('click', report, true);
    document.addEventListener('input', report, true);
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', watch); else watch();
})();

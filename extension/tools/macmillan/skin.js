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
  // before the page and told again by 'theme'): the page's grounds clear, each part a card of the glass, words and fields
  // in the app's ink, a picture on a white plate of its own in dark (drawn for white paper). And as wide as the popup:
  // Macmillan's question is a 960-px sheet, set 25 px in, beside the side panel's 300 px kept even with the panel put
  // away, so it ran under the edge (a scroll bar along the foot) and its right column — the diagram, in a two-column
  // question — was cut off. The sheet fills the width now; the two columns kept side by side, the picture on the right,
  // and one column when narrow.
  var SKIN =
    'html.simpl-mm{--mm-ink:#1c1c1e;--mm-ink2:rgba(60,60,67,.62);--mm-card:rgba(255,255,255,.74);--mm-line:rgba(0,0,0,.07);--mm-field:rgba(255,255,255,.92);--mm-good:#1a6b30;--mm-good-bg:rgba(52,199,89,.16);--mm-bad:#c01d43;--mm-bad-bg:rgba(255,59,48,.13);color-scheme:light}' +
    'html.simpl-mm.simpl-mm-dark{--mm-ink:#f2f2f7;--mm-ink2:rgba(235,235,245,.62);--mm-card:rgba(255,255,255,.075);--mm-line:rgba(255,255,255,.11);--mm-field:rgba(255,255,255,.09);--mm-good:#5ddb7d;--mm-good-bg:rgba(52,199,89,.2);--mm-bad:#ff8098;--mm-bad-bg:rgba(255,69,58,.2);color-scheme:dark}' +
    'html.simpl-mm,html.simpl-mm body{background:transparent!important;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif!important}' +
    'html.simpl-mm [data-simpl-hide],html.simpl-mm #check-bar,html.simpl-mm #attempts-holder,html.simpl-mm #toggle-fullscreen,html.simpl-mm .nav-scroll-arrow-button,' +
    'html.simpl-mm [aria-label="Scroll to Bottom"],html.simpl-mm [aria-label="Scroll to Top"],html.simpl-mm [id^="_pendo"],html.simpl-mm [class*="_pendo"]{display:none!important}' +
    'html.simpl-mm #root,html.simpl-mm #sac,html.simpl-mm #main,html.simpl-mm .raptor-container,html.simpl-mm #raptor-content-container,html.simpl-mm .content-area,html.simpl-mm .authoring-area,html.simpl-mm .parent-module-container{background:transparent!important;max-width:none!important;box-shadow:none!important}' +
    // the width: the whole of it, never wider
    'html.simpl-mm #main{width:auto!important;flex:1 1 auto!important;min-width:0!important;overflow:hidden!important}' +
    'html.simpl-mm .raptor-container{padding:18px 26px 0!important;box-sizing:border-box!important;min-width:0!important;margin:0!important}' +
    'html.simpl-mm #raptor-content-container{overflow-x:hidden!important;overflow-y:auto!important;width:auto!important;padding-bottom:90px!important;box-sizing:border-box!important}' +
    'html.simpl-mm #raptor-content-container .content-area{position:relative!important;left:auto!important;right:auto!important;top:auto!important;width:auto!important;max-width:1180px!important;height:auto!important;margin:0 auto!important;overflow:visible!important}' +
    'html.simpl-mm .authoring-area{width:auto!important;height:auto!important;overflow:visible!important;margin:0!important;padding:0!important}' +
    'html.simpl-mm .parent-module-container{min-height:0!important;height:auto!important;padding:0!important;display:flow-root!important}' +
    'html.simpl-mm [copyrightmessagestyle]{position:static!important;transform:none!important;left:auto!important;top:auto!important}' +
    'html.simpl-mm [data-test-id="copyright-box"]{font-size:11px!important;color:var(--mm-ink2)!important;margin:0 0 8px 4px!important}' +
    // the parts, cards of the glass
    'html.simpl-mm article.module-container{background:var(--mm-card)!important;border-radius:16px!important;padding:16px 20px!important;margin:0 0 12px!important;min-height:0!important;box-sizing:border-box!important;box-shadow:0 1px 2px rgba(0,0,0,.05),0 0 0 1px var(--mm-line)!important;line-height:1.5;color:var(--mm-ink)!important;-webkit-backdrop-filter:blur(20px) saturate(160%);backdrop-filter:blur(20px) saturate(160%)}' +
    'html.simpl-mm article.module-container>.sl-module{padding-bottom:0!important;overflow:visible!important;max-width:100%!important}' +
    // two columns (Macmillan's "single right"): the first part and all after it down the left, the second (the picture) up the right
    'html.simpl-mm .two_col_single_right .module-container{display:block!important;width:calc(50% - 6px)!important;float:none!important}' +
    'html.simpl-mm .two_col_single_right .module-container:nth-of-type(1){float:left!important}' +
    'html.simpl-mm .two_col_single_right .module-container:nth-of-type(2){float:right!important}' +
    'html.simpl-mm .two_col_single_right .module-container:nth-of-type(n+3){clear:left!important}' +
    'html.simpl-mm :is(.two_col_one_top,.one_col_two_col_alternate,.two_col_all) .module-container{box-sizing:border-box!important}' +
    '@media (max-width:640px){html.simpl-mm .authoring-area .module-container{width:auto!important;float:none!important;display:block!important;clear:both!important}}' +
    'html.simpl-mm .raw-html img,html.simpl-mm article.module-container img{max-width:100%!important;height:auto!important;border-radius:10px}' +
    'html.simpl-mm.simpl-mm-dark article.module-container :is(.raw-html,figure) img{background:#fff!important;padding:6px!important;box-sizing:border-box!important}' +
    // words and fields in the app's ink (dark: Macmillan's black put to the light ink)
    'html.simpl-mm.simpl-mm-dark #raptor-content-container :is(p,span,div,label,li,td,th,h1,h2,h3,h4,h5,h6,b,i,em,strong,sup,sub,a,mjx-container,legend,caption,figcaption){color:var(--mm-ink)!important}' +
    'html.simpl-mm.simpl-mm-dark #raptor-content-container :is(table,td,th){border-color:var(--mm-line)!important}' +
    'html.simpl-mm.simpl-mm-dark #raptor-content-container th{background:rgba(255,255,255,.06)!important}' +
    'html.simpl-mm input[type=text],html.simpl-mm input:not([type]),html.simpl-mm .numeric-entry-input,html.simpl-mm textarea,html.simpl-mm select{border-radius:8px!important;background:var(--mm-field)!important;color:var(--mm-ink)!important;border:1px solid var(--mm-line)!important}' +
    'html.simpl-mm.simpl-mm-dark .numeric-entry-border{background:transparent!important;border-color:var(--mm-line)!important}' +
    // Macmillan's Solved / Try Again along the foot, a glass bar of its own
    'html.simpl-mm #results{left:26px!important;right:26px!important;width:auto!important;bottom:14px!important;height:44px!important;min-height:44px!important;border:0!important;border-radius:14px!important;padding:0 18px!important;box-shadow:0 0 0 1px var(--mm-line),0 4px 16px rgba(0,0,0,.08)!important;font-weight:600!important;color:var(--mm-ink)!important;background:var(--mm-card)!important;-webkit-backdrop-filter:blur(20px) saturate(160%);backdrop-filter:blur(20px) saturate(160%)}' +
    'html.simpl-mm #results.correct{background:var(--mm-good-bg)!important;color:var(--mm-good)!important}' +
    'html.simpl-mm #results.incorrect{background:var(--mm-bad-bg)!important;color:var(--mm-bad)!important}' +
    'html.simpl-mm #results *{color:inherit!important}html.simpl-mm #results svg{fill:currentColor!important}';
  var dark = !!window.__simplMacmillanDark;

  function mark() {
    var nav = q('#question-nav');
    var side = q('#side-nav') || (nav && (nav.closest('nav') || nav.parentElement));
    if (side) side.setAttribute('data-simpl-hide', '');
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
    var results = qa('[data-test-id^="result-frame-"]').map(function (r) { return r.getAttribute('aria-label') || ''; }).filter(Boolean);
    return {
      kind: 'state', page: 'question', dark: dark, title: title, index: Number(m[1] || 0) - 1, count: Number(m[2] || items.length),
      completed: done[1] ? Number(done[1]) : items.filter(function (i) { return i.state === 'success'; }).length,
      score: sm ? [Number(sm[1]), Number(sm[2])] : null, policies: policies(), items: items,
      main: main ? (main.getAttribute('aria-label') || words(main)) : null, mainOn: !!(main && !main.disabled),
      tools: { resources: !!q('[aria-label="Open Resources"]'), tutor: !!q('[aria-label="Open AI Tutor"]'), solution: !!q('[aria-label="Open Solution"]'), layout: !!tid('switch-to-new-layout') },
      attempts: att ? { label: words(att), choices: choices } : null, results: results,
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
      if (skinned) { document.documentElement.classList.add('simpl-mm'); css('simpl-mm-skin', SKIN); mark(); unfold(); }
      else { document.documentElement.classList.remove('simpl-mm'); }
      document.documentElement.classList.toggle('simpl-mm-dark', skinned && dark);
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

  function watch() {
    report();
    try { new MutationObserver(report).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-test-id', 'aria-label', 'disabled', 'value', 'class'] }); } catch (e) {}
    document.addEventListener('click', report, true);
    document.addEventListener('input', report, true);
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', watch); else watch();
})();

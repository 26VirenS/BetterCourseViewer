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

  var isSac = /\/sac\//.test(location.pathname);
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
    var f = qa('iframe').filter(function (x) { return /\/sac\//.test(x.getAttribute('src') || x.src || ''); })[0];
    if (!f) return false;
    if (!skinned) { css('simpl-mm-hub', ''); return true; }
    f.setAttribute('data-simpl-sac', '');
    css('simpl-mm-hub', 'html,body{overflow:hidden!important;background:#f5f6f8!important}' +
      'iframe[data-simpl-sac]{position:fixed!important;inset:0!important;width:100vw!important;height:100vh!important;border:0!important;z-index:2147483646!important;background:#f5f6f8!important}' +
      '[id^="_pendo"],[class*="_pendo"]{display:none!important}');
    return true;
  }

  // ---- the assessment player ----
  var SKIN =
    'html.simpl-mm,html.simpl-mm body{background:#f5f6f8!important;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif!important}' +
    'html.simpl-mm [data-simpl-hide],html.simpl-mm #check-bar,html.simpl-mm #attempts-holder,html.simpl-mm #toggle-fullscreen,' +
    'html.simpl-mm [aria-label="Scroll to Bottom"],html.simpl-mm [aria-label="Scroll to Top"],html.simpl-mm [id^="_pendo"],html.simpl-mm [class*="_pendo"]{display:none!important}' +
    'html.simpl-mm #main,html.simpl-mm .raptor-container,html.simpl-mm #raptor-content-container,html.simpl-mm .content-area,html.simpl-mm .authoring-area{background:transparent!important;max-width:none!important}' +
    'html.simpl-mm .raptor-container{padding:18px 26px 120px!important}' +
    'html.simpl-mm article.module-container{background:#fff!important;border-radius:16px!important;padding:16px 20px!important;margin:0 0 12px!important;box-shadow:0 1px 2px rgba(0,0,0,.06),0 0 0 1px rgba(0,0,0,.05)!important;line-height:1.5}' +
    'html.simpl-mm [data-test-id="copyright-box"]{font-size:11px!important;opacity:.55;margin:0 0 8px 4px!important}' +
    'html.simpl-mm input[type=text],html.simpl-mm .numeric-entry-input{border-radius:8px!important}' +
    'html.simpl-mm img{max-width:100%;height:auto;border-radius:10px}';

  function mark() {
    var nav = q('#question-nav');
    var side = nav && (nav.closest('nav') || nav.parentElement);
    if (side) side.setAttribute('data-simpl-hide', '');
  }

  function cover() {
    // (before Open Assignment: the assignment's name, its due line, how many questions, the grading settings)
    var open = qa('button').filter(function (b) { return /^\s*open assignment\s*$/i.test(words(b)); })[0];
    if (!open || q('#question-nav')) return null;
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
    if (c) return { kind: 'state', page: 'cover', cover: c };
    if (!q('#question-nav') && !tid('btn-sac-main')) return { kind: 'state', page: 'other', title: document.title };
    var header = words(tid('nav-question-header'));
    var m = header.match(/Question (\d+) of (\d+)/i) || [];
    var title = document.title.replace(/^Question \d+ of \d+ - /i, '').trim();
    var items = qa('#question-nav li[data-test-id$="-row"]').map(function (li) {
      var t = li.getAttribute('data-test-id') || '';
      var mm = t.match(/^item-(\d+)-(.+)-row$/) || [];
      var parts = qa(':scope > div > div, :scope > div', li).map(words).filter(Boolean);
      var label = words(q('[data-test-id="item-' + mm[1] + '-number"]', li)) || ('Q' + mm[1]);
      var note = (parts.filter(function (p) { return p !== label && !/^\+\d+$/.test(p) && p.indexOf(label) !== 0; })[0]) || '';
      return { n: Number(mm[1]) || 0, label: label, state: mm[2] || 'default', note: note, points: words(q('[data-test-id="item-' + mm[1] + '-points"]', li)) };
    });
    var scoreEl = tid('final-score-fraction');
    var sm = ((scoreEl && scoreEl.getAttribute('aria-label')) || '').match(/(\d+(?:\.\d+)?) out of (\d+(?:\.\d+)?)/);
    var done = words(q('[data-test-id="side-nav-button-title"]')).match(/Completed (\d+) of (\d+)/i) || [];
    var main = tid('btn-sac-main');
    var att = tid('attempts-dropdown-button');
    var choices = qa('[data-test-id="attempts"] button').map(function (b) { return words(b); }).filter(Boolean);
    var results = qa('[data-test-id^="result-frame-"]').map(function (r) { return r.getAttribute('aria-label') || ''; }).filter(Boolean);
    return {
      kind: 'state', page: 'question', title: title, index: Number(m[1] || 0) - 1, count: Number(m[2] || items.length),
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
      if (!isSac) { if (hub()) post({ kind: 'hub' }); return; }
      if (skinned) { document.documentElement.classList.add('simpl-mm'); css('simpl-mm-skin', SKIN); mark(); }
      else { document.documentElement.classList.remove('simpl-mm'); }
      var s = state();
      var text = JSON.stringify(s);
      if (text !== last) { last = text; post(s); }
    }, 120);
  }

  function click(el) { if (!el) return false; el.click(); report(); return true; }

  // the app's presses
  window.__simplMacmillan = function (cmd, arg) {
    switch (cmd) {
      case 'open': return click(qa('button').filter(function (b) { return /^\s*open assignment\s*$/i.test(words(b)); })[0]);
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
      case 'skin': skinned = !!arg; window.__simplMacmillanSkinned = skinned; last = ''; report(); if (!skinned && !isSac) css('simpl-mm-hub', ''); return true;
      case 'state': last = ''; report(); return true;
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

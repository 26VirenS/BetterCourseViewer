// (Mac 1.3.198, Windows 0.3.93) Simpl's look for ALEKS, drawn over ALEKS's own page in the apps' tool window, as
// Macmillan Learning's is (extension/tools/macmillan/look.js): only styles — nothing of the page is taken out, moved in
// the page's code, or answered differently. Laid out as a Knewton Alta question is in the apps: a light glass bar over
// the page with the topic and its progress, the question on a sheet of its own, the answer box and its keys rounder and
// clearer, ALEKS's tools (Help, Calculator, Videos, eBook, …) in a glass column beside the sheet rather than over it, and
// the actions (Explanation, Check, Next, …) on a glass strip along the foot, the main one in the accent, under the
// answer. The chrome in the system's font; the question's own text and maths as ALEKS sets them. Its hooks come from the
// recorded page code of real ALEKS sessions (ids such as smt_bottomnav_button_input_checkAnswer, header.aleks-header,
// the answer editor's ansed_ elements), not from ALEKS's generated class names, which change.
//
// (Mac 1.3.199, Windows 0.3.94) In the apps' ALEKS popup — opened as a Knewton Alta assignment is — the app puts
// `window.__simplLookRail = true;` before it too: the topic, its progress and ALEKS's word on the last answer are then
// the popup's own rail (told to the app as they change, `rail: 'aleks'`, with the width ALEKS lays its page out at, for
// the popup to zoom the page to), so the bar over the page keeps only ALEKS's menu and the student's; the question lies
// on a sheet of liquid glass over a lit ground, the tools in a glass column, the actions in a glass dock along the foot;
// a new question eases in and ALEKS's verdict rises into place.
//
// (Mac 1.3.200, Windows 0.3.95) The popup's own front end over the page, as Macmillan's (extension/tools/macmillan/look.js):
// with `window.__simplLookFront` set ('glass' on the Mac, Liquid Glass drawn by the app behind a clear page; 'card' on
// Windows, the sheet drawn here) ALEKS's actions (Explanation, Check, Next, …) and its legal line are the popup's own
// bar — ALEKS's own out of sight but there, pressed by the popup's (`__simplAKDo({ press: id })`) — and the page keeps
// the question, its answer, ALEKS's verdict over it, its tools and its menu.
//
// The app puts `window.__simplAKOn = true|false;` before this file (the toolbar's Simpl Look, as last left) and turns
// it at once with `__simplAKSet(on)` in the top page, which passes it on to the page's frames; a frame loaded later
// asks its parent. It tells the app once what it found, for the log: a frame through the top page, the top page with
// webkit.messageHandlers.simplLook on the Mac and chrome.webview.postMessage({ simplLook }) on Windows.
(function () {
  'use strict';
  if (window.__simplAKSet) return;
  var host = location.hostname;
  var mine = /(^|\.)aleks\.com$/i.test(host) ||
    ((host === 'localhost' || host === '127.0.0.1') && location.pathname.indexOf('/mock-aleks/') === 0);
  if (!mine) return;
  var root = null; // (the page's <html>: not there yet when Windows runs this, before the page is read)
  var on = window.__simplAKOn !== false;
  var front = window.__simplLookFront === 'glass' || window.__simplLookFront === 'card' ? window.__simplLookFront : '';
  var rail = window.__simplLookRail === true || !!front; // (in the apps' popup, its rail beside the page)

  var A = 'html.simpl-ak ';
  var BAR = A + '[role="navigation"][aria-label="Actions"]';
  var TOOLS = A + '[role="complementary"][aria-label="Tools"]';
  var R = 'html.simpl-ak.simpl-rail ';
  var BAR_R = R + '[role="navigation"][aria-label="Actions"]';
  var TOOLS_R = R + '[role="complementary"][aria-label="Tools"]';
  var CSS = [
    'html.simpl-ak{--ak-ui:-apple-system,BlinkMacSystemFont,"SF Pro Text","Segoe UI Variable Text","Segoe UI",system-ui,"Helvetica Neue",Arial,sans-serif;',
    '--ak-ink:#1d1d1f;--ak-ink2:#6e6e73;--ak-line:rgba(15,23,42,.08);--ak-fill:rgba(120,120,128,.12);--ak-fill2:rgba(120,120,128,.2);',
    '--ak-accent:#007aff;--ak-accent-soft:rgba(0,122,255,.12);',
    '--ak-glass:linear-gradient(180deg,rgba(255,255,255,.86),rgba(255,255,255,.66));',
    '--ak-edge:inset 0 1px 0 rgba(255,255,255,.95),inset 0 0 0 1px rgba(255,255,255,.4);',
    '--ak-lift:0 1px 2px rgba(16,24,40,.05),0 10px 30px rgba(16,24,40,.08);',
    '--ak-ground:radial-gradient(1100px 620px at 0% 0%,rgba(0,122,255,.10),transparent 62%),radial-gradient(900px 560px at 100% 100%,rgba(175,82,222,.08),transparent 60%),#eef0f5;',
    // (the page is a column at most 1280 px wide, centred; the tools' column sits 16 px in from its right edge, the
    // sheet stops 14 px short of the tools)
    '--ak-tools:46px;--ak-gap:16px;--ak-sheet-r:calc(var(--ak-gap) + var(--ak-tools) + 14px)}',
    A + 'body{background:var(--ak-ground)!important;-webkit-font-smoothing:antialiased}',
    A + '#welcomePage,' + A + '#welcomePage>div,' + A + '.tooltiptarget_banner.absolute_print{background:transparent!important}',

    // ---- the bar over the page: the topic, its progress, the menu and the student's name, on light glass
    A + 'header.aleks-header{background:rgba(255,255,255,.82)!important;-webkit-backdrop-filter:blur(20px) saturate(1.8);backdrop-filter:blur(20px) saturate(1.8);border-bottom:1px solid var(--ak-line)!important;box-shadow:none!important;color:var(--ak-ink)!important}',
    A + 'header.aleks-header *{color:var(--ak-ink)!important}',
    A + 'header.aleks-header button svg{fill:currentColor!important}',
    A + 'header.aleks-header .slice-name{font:600 12px/1.3 var(--ak-ui)!important;letter-spacing:.01em;color:var(--ak-ink2)!important}',
    A + 'header.aleks-header .item-title{font:700 16px/1.3 var(--ak-ui)!important;letter-spacing:-.01em}',
    A + 'header.aleks-header .learning-status-container *{color:var(--ak-ink2)!important;font-family:var(--ak-ui)!important}',
    A + 'header.aleks-header .hamburger-menu-button,' + A + 'header.aleks-header .title-expand-button{background:var(--ak-fill)!important;border:0!important;transition:background-color .15s ease}',
    A + '#userProfile{height:34px!important;padding:0 14px!important;border:0!important;border-radius:999px!important;background:var(--ak-fill)!important;transition:background-color .15s ease}',
    A + '#userProfile,' + A + '#userProfile *{font:600 14px/1 var(--ak-ui)!important}',
    '@media (hover:hover) and (pointer:fine){' + A + 'header.aleks-header .hamburger-menu-button:hover,' + A + '#userProfile:hover{background:var(--ak-fill2)!important}}',
    // (the topics' tab under the bar, and Español)
    A + 'button.tooltiptarget_toggle_carousel{background:var(--ak-glass)!important;border-color:transparent!important;box-shadow:var(--ak-edge),0 2px 8px rgba(16,24,40,.12)!important}',
    A + '#smt_LanguageToggleButton_button_input_toggle_spanish{background:var(--ak-glass)!important;border-color:var(--ak-line)!important;box-shadow:0 1px 3px rgba(16,24,40,.08)}',
    A + '#smt_LanguageToggleButton_button_input_toggle_spanish *{font:600 12px/1 var(--ak-ui)!important}',

    // (My Classes: on the same ground, its headings in the system's font, Add/Change Class rounder)
    A + 'header.aleks-header .aleks-user-menu-button{border:0!important;border-radius:999px!important;background:var(--ak-fill)!important}',
    A + '#current-main-view.tooltiptarget_homepage>div,' + A + '#current-main-view.tooltiptarget_homepage>div>div{background:transparent!important}',
    A + '#current-main-view [role="heading"],' + A + '#current-main-view [role="heading"] *{font-family:var(--ak-ui)!important}',
    A + '[id$="ClassesTile"] [role="heading"]{background:transparent!important;border-bottom:1px solid var(--ak-line)!important}',
    A + '[id$="ClassesTile"] button[role="link"]{border-radius:16px!important;border:1px solid var(--ak-line)!important;box-shadow:var(--ak-lift)}',

    // ---- the question on a sheet of its own, the tools beside it
    A + '#current-main-view,' + A + '.mainview_item_page,' + A + '#current-main-view>div[style*="z-index: -1"],' + A + '#current-main-view>div[style*="z-index:-1"]{background:transparent!important}',
    A + '[id^="itemContainer_"]{width:auto!important;margin:40px var(--ak-sheet-r) 0 var(--ak-gap)!important;padding-right:40px!important;border-radius:22px!important;box-shadow:0 0 0 1px var(--ak-line),var(--ak-lift)!important}',
    TOOLS + '{right:calc(max(0px,(100% - 1280px) / 2) + var(--ak-gap))!important;margin-right:0!important}',
    TOOLS + '>table{border-collapse:separate;padding:3px 0;background:var(--ak-glass);border:1px solid var(--ak-line);border-radius:24px;box-shadow:var(--ak-edge),var(--ak-lift)}',
    TOOLS + ' button.iconButton{border-radius:50%!important;transition:background-color .15s ease}',
    TOOLS + ' button.iconButton.secondary,' + TOOLS + ' button.iconButton.standard{background:transparent!important}',
    TOOLS + ' button.iconButton.primary{background:var(--ak-accent)!important}',
    '@media (hover:hover) and (pointer:fine){' + TOOLS + ' button.iconButton.secondary:hover,' + TOOLS + ' button.iconButton.standard:hover{background:var(--ak-fill)!important}}',
    // (the verdict over the question — Try Again, Correct: rounder, on white)
    A + '#result_feedback_alert{border-radius:16px!important;background:#fff!important;box-shadow:0 4px 14px rgba(16,24,40,.08)}',
    A + '#result_feedback_alert *{font-family:var(--ak-ui)!important}',
    // (the answer: a rounder box with a clear ring while it is being typed in; its keys and Undo / Start Over rounder)
    // (the editor's own box only: the question's maths is drawn by the same editor, ansed_root too, with no box)
    A + '.aleks-editor-content .ansed_root{border:1px solid #b9b9c0!important;border-radius:12px!important;transition:border-color .15s ease,box-shadow .15s ease}',
    A + '.aleks-editor-content:focus-within .ansed_root{border-color:var(--ak-accent)!important;box-shadow:inset 0 0 0 2px rgba(0,122,255,.35)}',
    A + '.aleks-editor-menu [id^="menubar_"]>div[style]{border-radius:14px!important;border-color:var(--ak-line)!important;box-shadow:0 8px 24px rgba(16,24,40,.12)!important}',
    A + '[role="list"][aria-label="Editor menu"] button{border-radius:10px!important}',
    A + '#containerEditButton button{border-radius:10px!important;background:#48484a!important}',
    // (ALEKS's own windows — the calculator and the rest: a softer shadow)
    A + '[class^="dialog--"],' + A + '[class*=" dialog--"]{box-shadow:0 18px 50px rgba(16,24,40,.22)!important;border-color:var(--ak-line)!important}',

    // ---- the strip along the foot: the actions together at its left, under the answer (where ALEKS has them: its
    // calculator opens on the right, over that end of the strip), lined up with the question's text; the legal line under it
    BAR + '{justify-content:flex-start!important;height:64px!important;padding:0 var(--ak-sheet-r) 0 calc(var(--ak-gap) + 40px)!important;background:rgba(255,255,255,.78)!important;-webkit-backdrop-filter:blur(20px) saturate(1.8);backdrop-filter:blur(20px) saturate(1.8);border-top:1px solid var(--ak-line)!important}',
    BAR + ' button{height:40px!important;padding:0 20px!important;margin:0 8px 0 0!important;border:0!important;border-radius:999px!important;transition:transform .12s ease-out,filter .15s ease,background-color .15s ease}',
    BAR + ' button,' + BAR + ' button *{font:600 15px/1 var(--ak-ui)!important;letter-spacing:-.01em}',
    BAR + ' button.standard,' + BAR + ' button.secondary{background:var(--ak-fill)!important}',
    BAR + ' button.standard,' + BAR + ' button.standard *,' + BAR + ' button.secondary,' + BAR + ' button.secondary *{color:var(--ak-ink)!important}',
    BAR + ' button.primary{min-width:150px;background:var(--ak-accent)!important;box-shadow:inset 0 1px 0 rgba(255,255,255,.25),0 6px 16px rgba(0,122,255,.3),0 1px 3px rgba(0,0,0,.12)!important}',
    BAR + ' button.primary,' + BAR + ' button.primary *{color:#fff!important}',
    BAR + ' button:active{transform:scale(.97)}',
    BAR + ' button:focus-visible{outline:3px solid rgba(0,122,255,.45)!important;outline-offset:2px}',
    '@media (hover:hover) and (pointer:fine){' + BAR + ' button.primary:hover{filter:brightness(1.07)}' + BAR + ' button.standard:hover,' + BAR + ' button.secondary:hover{background:var(--ak-fill2)!important}}',
    A + '.aleks-legal-footer{background:rgba(255,255,255,.78)!important;-webkit-backdrop-filter:blur(20px) saturate(1.8);backdrop-filter:blur(20px) saturate(1.8)}',
    A + '.aleks-legal-footer,' + A + '.aleks-legal-footer *{color:var(--ak-ink2)!important;font-family:var(--ak-ui)!important}',

    '@media (prefers-reduced-motion:reduce){' + BAR + ' button:active{transform:none}}',
    '@media (prefers-reduced-transparency:reduce){html.simpl-ak{--ak-glass:#fff}' + A + 'header.aleks-header,' + BAR + ',' + A + '.aleks-legal-footer{-webkit-backdrop-filter:none;backdrop-filter:none;background:#fff!important}}',
    // ---- (1.3.199) in the popup: the topic and its progress are the rail's, the question on liquid glass, a dock
    'html.simpl-ak.simpl-rail{--ak-ground:radial-gradient(900px 620px at 6% -12%,rgba(0,122,255,.22),transparent 62%),radial-gradient(760px 560px at 104% 14%,rgba(175,82,222,.17),transparent 62%),radial-gradient(980px 640px at 46% 118%,rgba(48,176,199,.18),transparent 60%),#edf0f7;',
    '--ak-sheet-glass:linear-gradient(168deg,rgba(255,255,255,.9),rgba(255,255,255,.72) 55%,rgba(255,255,255,.8));',
    '--ak-glass-edge:inset 0 1px 0 rgba(255,255,255,.98),inset 0 0 0 1px rgba(255,255,255,.6),inset 0 -1px 0 rgba(255,255,255,.35);',
    '--ak-float:0 0 0 .5px rgba(15,23,42,.08),0 2px 6px rgba(30,41,82,.05),0 26px 60px -18px rgba(30,41,82,.26);--ak-ease:cubic-bezier(.23,1,.32,1)}',
    R + 'header.aleks-header{background:transparent!important;border:0!important;-webkit-backdrop-filter:none;backdrop-filter:none;box-shadow:none!important}',
    R + 'header.aleks-header .item-info,' + R + 'header.aleks-header .learning-status-container,' + R + 'header.aleks-header .title-expand-button{position:absolute!important;width:1px!important;height:1px!important;margin:-1px!important;padding:0!important;overflow:hidden!important;clip:rect(0 0 0 0)!important;white-space:nowrap!important;border:0!important}',
    R + 'header.aleks-header .hamburger-menu-button,' + R + '#userProfile{background:rgba(255,255,255,.74)!important;box-shadow:var(--ak-glass-edge),0 2px 8px rgba(30,41,82,.12)!important;transition:transform .12s var(--ak-ease),background-color .15s ease}',
    R + 'header.aleks-header .hamburger-menu-button:active,' + R + '#userProfile:active{transform:scale(.95)}',
    // the sheet: liquid glass — light through it from the ground, a bright rim, a deep soft shadow
    R + '.mainview_item_page,' + R + '#current-main-view{background:transparent!important}',
    R + '[id^="itemContainer_"]{background:var(--ak-sheet-glass)!important;border-radius:28px!important;box-shadow:var(--ak-glass-edge),var(--ak-float)!important;margin-top:30px!important;margin-bottom:120px!important}',
    TOOLS_R + '>table{background:rgba(255,255,255,.62);-webkit-backdrop-filter:blur(22px) saturate(1.9);backdrop-filter:blur(22px) saturate(1.9);border:0;box-shadow:var(--ak-glass-edge),var(--ak-float)}',
    TOOLS_R + ' button.iconButton:active{transform:scale(.92)}',
    // the dock: the actions together on glass, over the legal line (glass drawn under it, so nothing in it is held by a blur)
    BAR_R + '{left:16px!important;right:16px!important;bottom:36px!important;width:auto!important;height:64px!important;padding:0 12px!important;border:0!important;border-radius:22px!important;background:transparent!important;-webkit-backdrop-filter:none;backdrop-filter:none;box-shadow:var(--ak-float)!important;isolation:isolate}',
    BAR_R + '::before{content:"";position:absolute;inset:0;z-index:-1;border-radius:inherit;background:rgba(255,255,255,.62);-webkit-backdrop-filter:blur(24px) saturate(1.9);backdrop-filter:blur(24px) saturate(1.9);box-shadow:var(--ak-glass-edge);pointer-events:none}',
    BAR_R + ' button{height:44px!important}',
    R + '.aleks-legal-footer{background:transparent!important;-webkit-backdrop-filter:none;backdrop-filter:none}',
    R + '.aleks-legal-footer,' + R + '.aleks-legal-footer *{font-size:11px!important}',
    // ALEKS's word on the answer (Try Again, Correct) as glass, rising into place; a new question easing in
    R + '#result_feedback_alert{background:rgba(255,255,255,.86)!important;border-radius:18px!important;box-shadow:var(--ak-glass-edge),0 10px 28px -8px rgba(30,41,82,.22)!important}',
    '@keyframes simpl-ak-turn{from{opacity:0;transform:translateY(12px)}to{opacity:1;transform:none}}',
    '@keyframes simpl-ak-rise{from{opacity:0;transform:translateY(8px) scale(.98)}to{opacity:1;transform:none}}',
    '@keyframes simpl-ak-fade{from{opacity:0}to{opacity:1}}',
    '@media (prefers-reduced-motion:no-preference){' + R + '[id^="itemContainer_"].simpl-turn{animation:simpl-ak-turn .34s var(--ak-ease)}' + R + '#result_feedback_alert{animation:simpl-ak-rise .3s var(--ak-ease)}' + BAR_R + '{animation:simpl-ak-fade .3s var(--ak-ease)}}',
    '@media (prefers-reduced-motion:reduce){' + R + '[id^="itemContainer_"].simpl-turn{animation:simpl-ak-fade .2s ease}}',
    '@media (prefers-reduced-transparency:reduce){html.simpl-ak.simpl-rail{--ak-sheet-glass:#fff}' + BAR_R + '::before,' + TOOLS_R + '>table{-webkit-backdrop-filter:none;backdrop-filter:none;background:#fff}}',

    // ---- (1.3.200) the popup's front end: ALEKS's actions and legal line are the popup's own — out of sight but there
    BAR_R + '.simpl-hide,html.simpl-ak.simpl-front [role="navigation"][aria-label="Actions"],html.simpl-ak.simpl-front .aleks-legal-footer{opacity:0!important;pointer-events:none!important}',
    'html.simpl-ak.simpl-front [role="navigation"][aria-label="Actions"]::before{display:none}',
    R + '[id^="itemContainer_"]{margin-bottom:24px!important}',
    'html.simpl-ak.simpl-front .mainview_item_page{padding-bottom:116px!important}',
    // ('glass': no ground and no sheet of the page's own — the popup's Liquid Glass is under the clear page)
    'html.simpl-ak.simpl-front-glass,html.simpl-ak.simpl-front-glass body,html.simpl-ak.simpl-front-glass #welcomePage,html.simpl-ak.simpl-front-glass #welcomePage>div,html.simpl-ak.simpl-front-glass .tooltiptarget_mainview,html.simpl-ak.simpl-front-glass .tooltiptarget_banner,html.simpl-ak.simpl-front-glass #current-main-view,html.simpl-ak.simpl-front-glass .mainview_item_page{background:transparent!important}',
    'html.simpl-ak.simpl-front-glass [id^="itemContainer_"]{background:transparent!important;box-shadow:none!important}',
    'html.simpl-ak.simpl-front-glass .aleks-editor-content .ansed_root{background:rgba(255,255,255,.66)!important}',
    'html.simpl-ak.simpl-front-glass #result_feedback_alert{background:rgba(255,255,255,.6)!important}',
  ].join('\n');

  var style = document.createElement('style');
  style.id = 'simpl-ak-look';
  style.textContent = CSS;

  var tell = function (o) {
    try {
      if (window.parent !== window) window.top.postMessage({ simplAKTell: o }, '*');
      else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.simplLook) window.webkit.messageHandlers.simplLook.postMessage(o);
      else if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage({ simplLook: o });
    } catch (e) { /* (no app to tell) */ }
  };

  var text = function (e) { return e ? (e.textContent || '').replace(/\s+/g, ' ').trim() : ''; };

  // (1.3.199) For the popup's rail, said again whenever it changes: the topic's group and the topic, the topic's progress
  // (how many of its steps are done, and how full each is), ALEKS's word on the last answer, whether a question is up,
  // and the width ALEKS lays its page out at.
  var railSaid = '', lastItem = '';
  var report = function () {
    var head = document.querySelector('header.aleks-header');
    var st = head && head.querySelector('.learning-status-container');
    // (its spoken line, "Topic Progress 2 of 5", else the count drawn beside the bar, "2/5" — each read on its own)
    var m = null, parts = st ? st.querySelectorAll('[aria-live],div') : [];
    for (var k = 0; k < parts.length && !m; k++) {
      var t = text(parts[k]);
      m = /^Topic Progress\s+(\d+)\s+of\s+(\d+)$/i.exec(t) || (parts[k].children.length ? null : /^(\d+)\s*\/\s*(\d+)$/.exec(t));
    }
    var fills = [];
    if (st) {
      var segs = st.querySelectorAll('.tooltiptarget_randomWalk div[style*="height: 100%"],.tooltiptarget_randomWalk div[style*="height:100%"]');
      for (var i = 0; i < segs.length; i++) fills.push(Math.max(0, Math.min(1, (parseFloat(segs[i].style.width) || 0) / 100)));
    }
    var o = { rail: 'aleks', slice: text(head && head.querySelector('.slice-name')), topic: text(head && head.querySelector('.item-title')),
      done: m ? +m[1] : -1, total: m ? +m[2] : 0, fills: fills, status: text(st && st.querySelector('.status')),
      question: !!(document.querySelector('[role="navigation"][aria-label="Actions"]') && document.querySelector('.ansed_root')),
      wide: 1024 }; // (ALEKS lays its page out 1024 wide at the least)
    if (front) o.front = buttons();
    var said = JSON.stringify(o);
    if (rail && said !== railSaid) { railSaid = said; tell(o); }
  };

  // (1.3.200) For the popup's front end: ALEKS's actions as they stand — the ones shown, each named for the popup to press
  // it, its words, whether it is ALEKS's main one and whether it can be pressed.
  var shown = function (e) { return !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden' && getComputedStyle(e).display !== 'none'; };
  var buttons = function () {
    var f = { actions: [] };
    var bar = document.querySelector('[role="navigation"][aria-label="Actions"]');
    var list = bar ? bar.querySelectorAll('button') : [];
    for (var i = 0; i < list.length; i++) {
      var b = list[i];
      if (!shown(b)) continue;
      var id = 'act-' + (b.id || i).replace(/^smt_bottomnav_button_input_/, '').replace(/[^a-z0-9-]/gi, '');
      if (b.getAttribute('data-simpl-id') !== id) b.setAttribute('data-simpl-id', id);
      f.actions.push({ id: id, label: text(b), primary: b.classList.contains('primary'), enabled: !b.disabled && b.getAttribute('aria-disabled') !== 'true' });
    }
    return f;
  };
  // What the popup asks of the page: one of ALEKS's own actions pressed for the popup's.
  var act = function (c) {
    if (!c || typeof c !== 'object' || !c.press) return;
    var b = document.querySelector('[data-simpl-id="' + String(c.press).replace(/[^a-z0-9-]/gi, '') + '"]');
    if (b) b.click();
  };
  window.__simplAKDo = function (c) { act(c); };

  // What the look found, said once a page: the question's page (its actions bar and Check), or another of ALEKS's.
  var told = false, queued = false;
  var mark = function () {
    queued = false;
    report();
    // (a new question: eased in, as a page turned — not the first one drawn)
    var item = document.querySelector('[id^="itemContainer_"]');
    if (item && on && rail && lastItem && item.id !== lastItem) { item.classList.remove('simpl-turn'); void item.offsetWidth; item.classList.add('simpl-turn'); }
    if (item) lastItem = item.id;
    if (!on || told) return;
    root.classList.add('simpl-ak');
    var bar = document.querySelector('[role="navigation"][aria-label="Actions"]');
    var check = document.getElementById('smt_bottomnav_button_input_checkAnswer');
    var answer = document.querySelector('.ansed_root');
    if (!bar || !answer) return;
    told = true;
    tell({ look: 'aleks', said: 'drawn', host: location.host, detail: (check ? 'Check found' : 'no Check') + ', ' + document.querySelectorAll('[role="complementary"][aria-label="Tools"] button.iconButton').length + ' tools' });
  };
  var queue = function () { if (!queued) { queued = true; requestAnimationFrame(mark); } };
  var watching = false;
  var watch = function () {
    if (watching || !document.body) return;
    watching = true;
    if (document.head) document.head.appendChild(style);
    new MutationObserver(function () { if (!root.classList.contains('simpl-ak') && on) classes(); queue(); })
      .observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['style', 'class'] });
    queue();
  };
  var classes = function () {
    root.classList.toggle('simpl-ak', on);
    root.classList.toggle('simpl-rail', on && rail);
    root.classList.toggle('simpl-front', on && !!front);
    root.classList.toggle('simpl-front-glass', on && front === 'glass');
  };
  var draw = function () {
    if (!root) return;
    classes();
    if (on || rail) { watch(); queue(); } // (the rail is told of the topic with the look off too)
  };
  var relay = function () {
    for (var i = 0; i < window.frames.length; i++) { try { window.frames[i].postMessage({ simplAKLook: on }, '*'); } catch (e) { /* (gone) */ } }
  };
  window.__simplAKSet = function (v) { on = !!v; draw(); relay(); };
  addEventListener('message', function (e) {
    var d = e.data;
    if (!d || typeof d !== 'object') return;
    if (d.simplAKAsk === 1 && e.source) { try { e.source.postMessage({ simplAKLook: on }, '*'); } catch (x) { /* (gone) */ } return; }
    if (d.simplAKTell && typeof d.simplAKTell === 'object' && window.parent === window) { tell(d.simplAKTell); return; }
    if (typeof d.simplAKLook === 'boolean' && e.source === window.parent && window.parent !== window) window.__simplAKSet(d.simplAKLook);
  });
  if (window.parent !== window) { try { window.parent.postMessage({ simplAKAsk: 1 }, '*'); } catch (e) { /* (no parent to ask) */ } }

  // (in from the start, so ALEKS's page is never drawn first in its own look; moved to the head's end once there is one)
  var begin = function () {
    root = document.documentElement;
    root.appendChild(style);
    classes();
    if (document.body) draw(); else document.addEventListener('DOMContentLoaded', draw, { once: true });
  };
  if (document.documentElement) begin();
  else new MutationObserver(function (_, o) { if (document.documentElement) { o.disconnect(); begin(); } }).observe(document, { childList: true });
})();

// (Mac 1.3.197, Windows 0.3.92) Simpl's look for Macmillan Learning, drawn over Macmillan's own page in the apps' tool
// window: its assessment player (assessments.macmillanlearning.com/sac) and the Integration Hub page around it. Only
// styles: nothing of the page is taken out, moved in the page's code, or answered differently — the side list of
// questions, the score, Resources, AI Tutor, Solution, the main button, the arrows, the attempts menu and every answer
// field are Macmillan's own, still doing what they did. Laid out as a Knewton Alta question is in the apps: a glass rail
// of questions (the one open marked), a glass bar of tools, the question's number large over its sheet, and the main
// button (Submit Answer, Next Question) with the verdict beside it at the sheet's foot, in a strip of their own that
// nothing of the question sits under. The chrome in the system's font; the question's own text and maths as Macmillan
// sets them. The first answer of the old skin (removed in Mac 1.3.163) was a second page drawn from this one; this is
// the same page, restyled, so a change of Macmillan's at worst leaves a part looking as Macmillan draws it.
//
// (Mac 1.3.199, Windows 0.3.94) In the apps' Macmillan popup — opened as a Knewton Alta assignment is, over the main
// window — the app puts `window.__simplLookRail = true;` before it too: the questions, the score and how many are done
// are then the popup's own rail (told to the app as they change, `rail: 'macmillan'`), so Macmillan's side list is not
// drawn again beside them (a click on the rail's question clicks Macmillan's own row: `__simplMMDo({ go: n })`); the
// question lies on a sheet of liquid glass over a lit ground, its number large over it, and Resources, AI Tutor,
// Solution, the verdict and the main button float in a glass dock along the foot; a new question eases in.
//
// (Mac 1.3.200, Windows 0.3.95) The popup's own front end over the page: with `window.__simplLookFront` set ('glass': the
// Mac's popup, real Liquid Glass drawn by the app behind a clear page; 'card': Windows', the sheet drawn here) the page
// keeps only the question — its parts, its answer fields, the attempts menu — and the popup draws the rest itself: the
// question's number and the way to the others over it, Resources / AI Tutor / Solution and Submit Answer / Next Question
// in a bar of its own at its foot, and Macmillan's verdict as its own. Macmillan's own bar and buttons stay in the page,
// out of sight but there, and the popup's buttons press them (`__simplMMDo({ press: id })`): nothing is answered
// differently. The page tells the popup what they say and whether they can be pressed (`front` in what it tells the rail).
//
// The app puts `window.__simplMMOn = true|false;` before this file (the toolbar's Simpl Look, as last left) and turns
// it at once with `__simplMMSet(on)` in the top page, which passes it on to the page's frames; a frame loaded later
// asks its parent. It tells the app once what it found, for the log: a frame through the top page (a frame of another
// site's may have no way of its own to the app on Windows), the top page with webkit.messageHandlers.simplLook on the
// Mac and chrome.webview.postMessage({ simplLook }) on Windows.
(function () {
  'use strict';
  if (window.__simplMMSet) return;
  var host = location.hostname;
  var mine = /(^|\.)macmillanlearning\.com$/i.test(host) ||
    ((host === 'localhost' || host === '127.0.0.1') && location.pathname.indexOf('/mock-macmillan/') === 0);
  if (!mine) return;
  var root = null; // (the page's <html>: not there yet when Windows runs this, before the page is read)
  var on = window.__simplMMOn !== false;
  var front = window.__simplLookFront === 'glass' || window.__simplLookFront === 'card' ? window.__simplLookFront : '';
  var rail = window.__simplLookRail === true || !!front; // (in the apps' popup, its rail beside the page)
  var framed = window.parent !== window;

  var CSS = [
    'html.simpl-mm{--mm-ui:-apple-system,BlinkMacSystemFont,"SF Pro Text","Segoe UI Variable Text","Segoe UI",system-ui,"Helvetica Neue",Arial,sans-serif;',
    '--mm-ink:#1d1d1f;--mm-ink2:#6e6e73;--mm-line:rgba(15,23,42,.08);--mm-fill:rgba(120,120,128,.12);--mm-fill2:rgba(120,120,128,.2);',
    '--mm-accent:#007aff;--mm-accent-ink:#0062cc;--mm-accent-soft:rgba(0,122,255,.12);--mm-green:#1d7f37;--mm-red:#d70015;',
    '--mm-glass:linear-gradient(180deg,rgba(255,255,255,.86),rgba(255,255,255,.66));',
    '--mm-edge:inset 0 1px 0 rgba(255,255,255,.95),inset 0 0 0 1px rgba(255,255,255,.4);',
    '--mm-lift:0 1px 2px rgba(16,24,40,.05),0 10px 30px rgba(16,24,40,.08);',
    '--mm-ground:radial-gradient(1100px 620px at 0% 0%,rgba(0,122,255,.10),transparent 62%),radial-gradient(900px 560px at 100% 100%,rgba(175,82,222,.08),transparent 60%),#eef0f5;',
    '--mm-btn-w:184px;--mm-side:300px;--mm-sheet:960px}',
    'html.simpl-mm body{background:var(--mm-ground)!important;-webkit-font-smoothing:antialiased}',

    // ---- the Integration Hub's bar over the player
    'html.simpl-mm .hub_molecule_app_header{background:rgba(255,255,255,.8)!important;color:var(--mm-ink)!important;border-bottom:1px solid var(--mm-line)!important;box-shadow:none!important;-webkit-backdrop-filter:blur(20px) saturate(1.8);backdrop-filter:blur(20px) saturate(1.8);font-family:var(--mm-ui)!important}',
    'html.simpl-mm .hub_molecule_app_header>button{color:var(--mm-ink)!important;background:var(--mm-fill)!important;width:32px!important;height:32px!important;min-width:0!important;padding:0!important;margin:0 3px!important;border-radius:50%!important}',
    'html.simpl-mm .hub_molecule_app_header>button svg{fill:currentColor!important;width:18px;height:18px}',
    'html.simpl-mm .hub_molecule_app_header .app_header_title_txt{font:700 17px/1.2 var(--mm-ui)!important;letter-spacing:-.01em;color:var(--mm-ink)!important;margin-left:8px!important}',
    'html.simpl-mm [data-test-id="assign-due-div"]{display:inline-flex!important;align-items:center;gap:6px;height:auto!important;margin:0 12px 0 auto!important;padding:6px 12px!important;border-radius:999px!important;background:var(--mm-fill)!important;color:var(--mm-ink)!important;font:500 13px/1.2 var(--mm-ui)!important}',
    'html.simpl-mm [data-test-id="assign-due-div"] *{color:inherit!important;font-family:inherit!important;font-size:inherit!important}',
    'html.simpl-mm [data-test-id="assign-due-div"] strong{font-weight:600}',
    'html.simpl-mm [data-test-id="assign-due-div"] svg{fill:var(--mm-ink2)!important;width:16px;height:16px}',

    // ---- the player: a margin round it, the rail and the bar as glass on the ground
    // (spaced with margins, not gaps: Macmillan keeps empty helpers for screen readers among these, and a gap counts them)
    'html.simpl-mm #sac{box-sizing:border-box;padding:12px;background:transparent!important}',
    'html.simpl-mm #sac>#side-nav{margin-right:12px!important}',
    'html.simpl-mm #sac>#main{width:auto!important;flex:1 1 0;min-width:0;background:transparent!important}',
    'html.simpl-mm #side-nav.closed:not(.menu-selected)~#main{--mm-side:54px}',
    // (where the sheet ends on the right, as the strip's button and verdict line up with it: the sheet is centred in #main)
    'html.simpl-mm #main{--mm-right:max(24px,calc(12px + (100vw - 36px - var(--mm-side) - var(--mm-sheet)) / 2))}',

    // the rail: the questions
    'html.simpl-mm #side-nav{background:var(--mm-glass)!important;border:1px solid var(--mm-line)!important;border-radius:20px!important;box-shadow:var(--mm-edge),var(--mm-lift)!important;font-family:var(--mm-ui)!important;color:var(--mm-ink)}',
    'html.simpl-mm #side-nav .side-nav-item-container,html.simpl-mm #side-nav .toggle,html.simpl-mm #side-nav .nav-list,html.simpl-mm #side-nav .content{background:transparent!important;border:0!important}',
    'html.simpl-mm #side-nav [data-test-id="nav-toggle-item"]>button{font-family:var(--mm-ui)!important;color:var(--mm-ink)!important;text-transform:none!important;border-radius:16px!important}',
    'html.simpl-mm #side-nav [data-test-id="nav-toggle-item"] svg{fill:var(--mm-ink2)!important}',
    'html.simpl-mm #side-nav .panel-label,html.simpl-mm #side-nav [data-test-id="side-nav-button-title"]{font:700 15px/1.25 var(--mm-ui)!important;letter-spacing:-.01em;color:var(--mm-ink)!important}',
    // (the grading settings: a quiet row under the title)
    'html.simpl-mm #side-nav .side-nav-item-container>div:not(.toggle):not(.content){background:transparent!important;border:0!important;border-bottom:1px solid var(--mm-line)!important;margin:0 12px!important;font-family:var(--mm-ui)!important}',
    'html.simpl-mm #side-nav h4{font:600 13px/1.3 var(--mm-ui)!important;color:var(--mm-ink2)!important}',
    'html.simpl-mm #side-nav [data-test-id="show-policy-details-button"]{font:600 12px/1 var(--mm-ui)!important;text-transform:none!important;color:var(--mm-accent-ink)!important;background:var(--mm-accent-soft)!important;border:0!important;border-radius:999px!important;padding:5px 12px!important;min-width:0!important}',
    // (how much is done, as Alta's mastery bar: the share the title gives, "Completed 12 of 23 Questions")
    'html.simpl-mm[data-simpl-done] #question-nav::before{content:"";display:block;height:4px;margin:10px 6px 8px;border-radius:2px;background:linear-gradient(90deg,var(--mm-accent) 0 var(--simpl-done),var(--mm-fill2) var(--simpl-done) 100%)}',
    'html.simpl-mm #question-nav{padding:0 8px 12px!important}',
    'html.simpl-mm #question-nav>li[data-test-id]{margin:2px 0!important;border:0!important;border-radius:12px!important;font-family:var(--mm-ui)!important;transition:background-color .15s ease}',
    // (the question open: marked by the number in its header — Macmillan marks it with a class of its own that changes)
    'html.simpl-mm[data-simpl-here] #question-nav>li[data-test-id]{background:transparent!important}',
    'html.simpl-mm #question-nav>li[data-simpl-here]{background:var(--mm-accent-soft)!important;box-shadow:inset 0 0 0 1px rgba(0,122,255,.18)!important}',
    '@media (hover:hover) and (pointer:fine){html.simpl-mm #question-nav>li[data-test-id]:not([data-simpl-here]):hover{background:var(--mm-fill)!important}}',
    'html.simpl-mm #question-nav [data-test-id$="-number"]{font:600 15px/1.3 var(--mm-ui)!important;letter-spacing:-.01em;color:var(--mm-ink)!important}',
    'html.simpl-mm #question-nav>li[data-simpl-here] [data-test-id$="-number"]{color:var(--mm-accent-ink)!important}',
    'html.simpl-mm #question-nav [data-test-id$="-number"]+*{font:400 12px/1.35 var(--mm-ui)!important;color:var(--mm-ink2)!important}',
    'html.simpl-mm #question-nav [data-test-id$="-number"]+* *{font:inherit!important;color:inherit!important}',
    'html.simpl-mm #question-nav .successPill{background:rgba(52,199,89,.16)!important;color:var(--mm-green)!important;border:0!important;border-radius:999px!important;padding:2px 10px 2px 7px!important;box-shadow:none!important}',
    'html.simpl-mm #question-nav .successPill,html.simpl-mm #question-nav .successPill *{font:600 12px/18px var(--mm-ui)!important;color:var(--mm-green)!important}',
    'html.simpl-mm #question-nav .successPill svg{fill:currentColor!important}',

    // the bar: the score, Switch to New Layout, Resources, AI Tutor, Solution
    'html.simpl-mm #check-bar{display:flex!important;align-items:center;min-height:56px!important;height:auto!important;margin-bottom:12px!important;box-sizing:border-box;padding:0 8px 0 16px!important;background:var(--mm-glass)!important;border:1px solid var(--mm-line)!important;border-radius:18px!important;box-shadow:var(--mm-edge),var(--mm-lift)!important;font-family:var(--mm-ui)!important;color:var(--mm-ink)}',
    'html.simpl-mm #check-bar [data-test-id="final-score-fraction"],html.simpl-mm #check-bar [data-test-id="final-score-fraction"] *{font-family:var(--mm-ui)!important;color:var(--mm-ink)!important}',
    'html.simpl-mm #check-bar [data-test-id="switch-to-new-layout"]{font:500 13px/1 var(--mm-ui)!important;text-transform:none!important;color:var(--mm-ink2)!important;border-radius:999px!important;padding:7px 10px!important}',
    'html.simpl-mm #tools{display:flex!important;align-items:center;gap:6px;height:auto!important;color:var(--mm-ink)!important}',
    'html.simpl-mm #tools li{display:flex!important;align-items:center;border:0!important;min-width:0!important;width:auto!important;height:auto!important}',
    'html.simpl-mm #tools li>button{height:34px!important;min-width:0!important;padding:0 14px!important;border:0!important;border-radius:999px!important;background:var(--mm-fill)!important;color:var(--mm-ink)!important;box-shadow:inset 0 1px 0 rgba(255,255,255,.65)!important;font:600 13px/1 var(--mm-ui)!important;text-transform:none!important;letter-spacing:0!important;transition:background-color .15s ease}',
    'html.simpl-mm #tools li>button *{font:inherit!important;color:inherit!important}',
    '@media (hover:hover) and (pointer:fine){html.simpl-mm #tools li>button:hover{background:var(--mm-fill2)!important}}',
    'html.simpl-mm #check-bar .fullscreen-button{display:flex!important;align-items:center;justify-content:center;border:0!important;width:44px!important;height:auto!important;margin-left:4px}',
    'html.simpl-mm #check-bar .fullscreen-button>div{display:flex;width:auto!important;height:auto!important}',
    'html.simpl-mm #check-bar .fullscreen-button button{display:inline-flex!important;align-items:center;justify-content:center;width:34px!important;height:34px!important;padding:0!important;border-radius:50%!important;background:var(--mm-fill)!important;transition:background-color .15s ease}',
    'html.simpl-mm #check-bar .fullscreen-button svg{fill:var(--mm-ink)!important;width:18px;height:18px}',
    '@media (hover:hover) and (pointer:fine){html.simpl-mm #check-bar .fullscreen-button button:hover{background:var(--mm-fill2)!important}}',

    // ---- the question: its number large over its sheet, the arrows beside it, the sheet a card
    'html.simpl-mm .raptor-container{background:transparent!important;margin-top:0!important;box-sizing:border-box;padding-bottom:62px!important;min-height:0}',
    'html.simpl-mm .raptor-container .bottom-panel.show{bottom:62px!important}',
    'html.simpl-mm #attempts-holder{background:transparent!important;height:52px!important;min-height:52px!important;width:var(--mm-sheet);max-width:100%;margin:0 auto;box-sizing:border-box;font-family:var(--mm-ui)!important}',
    'html.simpl-mm #question-name{display:flex!important;align-items:center;gap:8px;width:auto!important;height:52px!important;line-height:normal!important;padding:0 0 0 4px!important;text-align:left!important}',
    'html.simpl-mm #question-name .left,html.simpl-mm #question-name .right{position:static!important}',
    'html.simpl-mm #question-name button{flex:none;display:inline-flex!important;align-items:center;justify-content:center;width:32px!important;height:32px!important;padding:0!important;border:1px solid var(--mm-line)!important;border-radius:50%!important;background:var(--mm-glass)!important;box-shadow:var(--mm-edge),0 1px 3px rgba(16,24,40,.1)!important;color:var(--mm-ink)!important;transition:transform .12s ease-out}',
    'html.simpl-mm #question-name button:active{transform:scale(.94)}',
    'html.simpl-mm #question-name button svg{fill:var(--mm-ink)!important}',
    'html.simpl-mm #question-name #nav-question-num{order:-1;width:auto!important;margin:0 6px 0 0!important;font:700 22px/1.2 var(--mm-ui)!important;letter-spacing:-.02em;color:var(--mm-ink)!important}',
    'html.simpl-mm #attempts-holder.dropdown #attempts-button{right:0!important;top:9px!important;height:34px;padding:0 14px!important;border:1px solid var(--mm-line)!important;border-radius:999px!important;background:var(--mm-glass)!important;box-shadow:var(--mm-edge),0 1px 3px rgba(16,24,40,.1)!important;color:var(--mm-ink)!important;font:600 13px/1 var(--mm-ui)!important}',
    'html.simpl-mm #attempts-holder.dropdown #attempts{right:0!important;top:48px!important;border:1px solid var(--mm-line)!important;border-radius:14px!important;overflow:hidden;background:#fff!important;box-shadow:0 12px 32px rgba(16,24,40,.16)!important;font-family:var(--mm-ui)!important}',
    'html.simpl-mm #raptor-content-container{background:transparent!important}',
    'html.simpl-mm #raptor-content-container .content-area{margin:0 auto!important}',
    'html.simpl-mm #raptor-content-container .authoring-area{box-shadow:none!important;background:transparent!important;margin-bottom:14px!important}',
    // (the sheet as tall as the room left between the bar and the strip — Macmillan sizes it to fill the page under its own
    // bars, 135 px of them, where the look's take 220)
    'html.simpl-mm #raptor-content-container .authoring-area>.parent-module-container,html.simpl-mm #sac .authoring-area .shared-module-container{min-height:calc(100vh - 220px)!important}',
    'html.simpl-mm #sac .authoring-area .shared-module-container.vertical{height:calc(100vh - 220px)!important}',
    'html.simpl-mm #raptor-content-container .authoring-area>.parent-module-container,html.simpl-mm #raptor-content-container .shared-module-container{border-radius:20px!important;box-shadow:0 0 0 1px var(--mm-line),var(--mm-lift)!important}',
    // (the answers: rounder, a clear ring on the one being typed in, Macmillan's own colours for right and wrong)
    'html.simpl-mm .numeric-entry-border{border-radius:10px!important;transition:border-color .15s ease,box-shadow .15s ease}',
    'html.simpl-mm .numeric-entry-border.focused{border-color:var(--mm-accent)!important;box-shadow:0 0 0 3px rgba(0,122,255,.22)!important}',
    'html.simpl-mm .numeric-entry-toolmenu{border-radius:12px!important;border:1px solid var(--mm-line)!important;box-shadow:0 10px 28px rgba(16,24,40,.16)!important;font-family:var(--mm-ui)!important}',
    'html.simpl-mm .grading-status{border-radius:999px!important;font-family:var(--mm-ui)!important}',
    'html.simpl-mm .module-graded-correct,html.simpl-mm .module-graded-incorrect{border-radius:12px}',
    'html.simpl-mm #raptor-content-container input[type=radio],html.simpl-mm #raptor-content-container input[type=checkbox]{accent-color:var(--mm-accent)}',
    'html.simpl-mm .math-palette{border-radius:14px!important;border-color:var(--mm-line)!important;box-shadow:0 16px 40px rgba(16,24,40,.18)!important}',

    // ---- the strip at the sheet's foot: the main button, the verdict beside it (both Macmillan's own, set there)
    'html.simpl-mm button[data-test-id="btn-sac-main"]{position:fixed!important;z-index:60;right:var(--mm-right)!important;bottom:calc(10px + 12px + var(--simpl-lift,0px))!important;left:auto!important;top:auto!important;width:var(--mm-btn-w)!important;min-width:0!important;height:42px!important;margin:0!important;padding:0 18px!important;border:0!important;border-radius:999px!important;background:var(--mm-accent)!important;color:#fff!important;font:600 15px/1 var(--mm-ui)!important;letter-spacing:-.01em;text-transform:none!important;white-space:nowrap;box-shadow:inset 0 1px 0 rgba(255,255,255,.25),0 6px 16px rgba(0,122,255,.3),0 1px 3px rgba(0,0,0,.12)!important;transition:transform .12s ease-out,filter .15s ease,bottom .25s cubic-bezier(.23,1,.32,1)}',
    '@media (hover:hover) and (pointer:fine){html.simpl-mm button[data-test-id="btn-sac-main"]:hover{filter:brightness(1.07)}}',
    'html.simpl-mm button[data-test-id="btn-sac-main"]:active{transform:scale(.97)}',
    'html.simpl-mm button[data-test-id="btn-sac-main"]:focus-visible{outline:3px solid rgba(0,122,255,.45)!important;outline-offset:2px}',
    'html.simpl-mm button[data-test-id="btn-sac-main"]:disabled,html.simpl-mm button[data-test-id="btn-sac-main"].Mui-disabled{background:var(--mm-fill2)!important;color:rgba(60,60,67,.5)!important;box-shadow:none!important}',
    'html.simpl-mm #results{position:fixed!important;z-index:60;right:calc(var(--mm-right) + var(--mm-btn-w) + 10px)!important;bottom:calc(10px + 12px + var(--simpl-lift,0px))!important;left:auto!important;top:auto!important;width:auto!important;max-width:44vw;height:42px!important;min-height:0!important;box-sizing:border-box;display:flex!important;align-items:center;gap:10px;padding:0 18px 0 14px!important;border:0!important;border-radius:999px!important;background:rgba(255,255,255,.72)!important;-webkit-backdrop-filter:blur(18px) saturate(1.8);backdrop-filter:blur(18px) saturate(1.8);box-shadow:var(--mm-edge),0 6px 18px rgba(16,24,40,.12)!important;color:var(--mm-ink)!important;font:600 14px/1 var(--mm-ui)!important;white-space:nowrap;overflow:hidden;transition:bottom .25s cubic-bezier(.23,1,.32,1)}',
    'html.simpl-mm #results *{font-family:var(--mm-ui)!important}',
    'html.simpl-mm #results.correct{background:rgba(232,248,236,.86)!important;color:var(--mm-green)!important;box-shadow:inset 0 0 0 1px rgba(52,199,89,.35),0 6px 18px rgba(52,199,89,.16)!important}',
    'html.simpl-mm #results.correct svg{fill:var(--mm-green)!important}',
    'html.simpl-mm #results.incorrect{background:rgba(255,237,235,.88)!important;color:var(--mm-red)!important;box-shadow:inset 0 0 0 1px rgba(255,59,48,.3),0 6px 18px rgba(255,59,48,.14)!important}',
    'html.simpl-mm #results.incorrect svg{fill:var(--mm-red)!important}',
    'html.simpl-mm #results .results-message{gap:6px}',
    'html.simpl-mm #results .results-message svg{margin:0!important}',
    'html.simpl-mm #results .attribute{background:rgba(255,255,255,.75)!important;border-radius:999px;padding:0 8px}',
    'html.simpl-mm #results .attribute-text{font-size:12px!important;font-weight:500!important;padding-right:0!important}',
    // (an empty verdict bar, as Macmillan keeps one before an answer is checked: not drawn)
    'html.simpl-mm:not([data-simpl-said]) #results{display:none!important}',
    '@media (prefers-reduced-motion:no-preference){html.simpl-mm #results{transition:opacity .2s cubic-bezier(.23,1,.32,1),transform .2s cubic-bezier(.23,1,.32,1),bottom .25s cubic-bezier(.23,1,.32,1)}@starting-style{html.simpl-mm #results{opacity:0;transform:translateY(6px) scale(.97)}}}',
    '@media (prefers-reduced-motion:reduce){html.simpl-mm button[data-test-id="btn-sac-main"]:active,html.simpl-mm #question-name button:active{transform:none}}',
    '@media (prefers-reduced-transparency:reduce){html.simpl-mm{--mm-glass:#fff}html.simpl-mm #results{-webkit-backdrop-filter:none;backdrop-filter:none;background:#fff!important}}',
    // ---- (1.3.199) in the popup: the rail is the app's, the question on liquid glass, the actions in a dock
    'html.simpl-mm.simpl-rail{--mm-ground:radial-gradient(900px 620px at 6% -12%,rgba(0,122,255,.22),transparent 62%),radial-gradient(760px 560px at 104% 14%,rgba(175,82,222,.17),transparent 62%),radial-gradient(980px 640px at 46% 118%,rgba(48,176,199,.18),transparent 60%),#edf0f7;',
    '--mm-sheet-glass:linear-gradient(168deg,rgba(255,255,255,.9),rgba(255,255,255,.72) 55%,rgba(255,255,255,.8));',
    '--mm-glass-edge:inset 0 1px 0 rgba(255,255,255,.98),inset 0 0 0 1px rgba(255,255,255,.6),inset 0 -1px 0 rgba(255,255,255,.35);',
    '--mm-float:0 0 0 .5px rgba(15,23,42,.08),0 2px 6px rgba(30,41,82,.05),0 26px 60px -18px rgba(30,41,82,.26);--mm-ease:cubic-bezier(.23,1,.32,1)}',
    // (a frame's page clear, so the hub's ground runs on under it with no seam)
    'html.simpl-mm.simpl-rail.simpl-framed,html.simpl-mm.simpl-rail.simpl-framed body,html.simpl-mm.simpl-rail #root{background:transparent!important}',
    // the hub's bar: its title and due date are the popup's, its buttons glass on the ground
    'html.simpl-mm.simpl-rail .hub_molecule_app_header{background:transparent!important;border:0!important;-webkit-backdrop-filter:none;backdrop-filter:none;padding:0 14px!important}',
    'html.simpl-mm.simpl-rail .hub_molecule_app_header>button{background:rgba(255,255,255,.72)!important;box-shadow:var(--mm-glass-edge),0 2px 8px rgba(30,41,82,.1)!important;transition:transform .12s var(--mm-ease),background-color .15s ease}',
    'html.simpl-mm.simpl-rail .hub_molecule_app_header>button:active{transform:scale(.94)}',
    // (the hub's help — its Resource Center, which floats in the window's corner — up by the bar, clear of the dock)
    'html.simpl-mm.simpl-rail [aria-label="Open Resource Center"]{top:8px!important;right:14px!important;bottom:auto!important;left:auto!important;width:36px!important;height:36px!important}',
    'html.simpl-mm.simpl-rail .hub_molecule_app_header .app_header_title_txt,html.simpl-mm.simpl-rail [data-test-id="assign-due-div"],html.simpl-mm.simpl-rail #check-bar [data-test-id="final-score-fraction"]{position:absolute!important;width:1px!important;height:1px!important;margin:-1px!important;padding:0!important;overflow:hidden!important;clip:rect(0 0 0 0)!important;white-space:nowrap!important;border:0!important}',
    // the player: no side list (the rail has it), room under the question for the dock
    'html.simpl-mm.simpl-rail #sac{box-sizing:border-box;height:100vh;padding:6px 24px 0!important;overflow:hidden}',
    'html.simpl-mm.simpl-rail #sac>#side-nav{display:none!important}',
    'html.simpl-mm.simpl-rail #main{--mm-side:0px}',
    // the dock: Resources, AI Tutor, Solution at its left; the verdict and the main button at its right (glass drawn
    // under it, not on it — a blur on the bar would hold the verdict, fixed, inside it)
    'html.simpl-mm.simpl-rail #check-bar{position:fixed!important;z-index:50;left:24px!important;right:24px!important;top:auto!important;bottom:calc(18px + var(--simpl-lift,0px))!important;width:auto!important;height:64px!important;min-height:0!important;margin:0!important;padding:0 11px 0 12px!important;display:flex!important;align-items:center;gap:8px;border:0!important;border-radius:22px!important;background:transparent!important;box-shadow:var(--mm-float)!important;isolation:isolate;transition:bottom .25s var(--mm-ease)}',
    'html.simpl-mm.simpl-rail #check-bar::before{content:"";position:absolute;inset:0;z-index:-1;border-radius:inherit;background:rgba(255,255,255,.62);-webkit-backdrop-filter:blur(24px) saturate(1.9);backdrop-filter:blur(24px) saturate(1.9);box-shadow:var(--mm-glass-edge);pointer-events:none}',
    'html.simpl-mm.simpl-rail #check-bar>.header-actions-container{display:flex!important;align-items:center;gap:8px;min-width:0;flex:1 1 auto}',
    'html.simpl-mm.simpl-rail #check-bar>.check-bar-actions{display:flex!important;align-items:center;margin-left:auto!important;flex:none}',
    'html.simpl-mm.simpl-rail #tools{width:auto!important;flex:none!important;margin:0 4px 0 0!important}',
    'html.simpl-mm.simpl-rail #tools li>button[data-test-id]{display:inline-flex!important;flex-direction:row!important;align-items:center!important;gap:7px;height:40px!important;padding:0 16px!important;background:rgba(120,120,128,.1)!important;transition:transform .12s var(--mm-ease),background-color .15s ease}',
    'html.simpl-mm.simpl-rail #tools li>button[data-test-id]:active{transform:scale(.96)}',
    'html.simpl-mm.simpl-rail #tools li>button[class*="pendo"]{background:transparent!important;box-shadow:none!important;padding:0!important;width:24px!important}',
    'html.simpl-mm.simpl-rail #tools li>button .MuiButton-startIcon{margin:0!important}',
    'html.simpl-mm.simpl-rail #tools li>button svg{width:18px!important;height:18px!important}',
    'html.simpl-mm.simpl-rail #check-bar [data-test-id="switch-to-new-layout"]{border:0!important;background:transparent!important;box-shadow:none!important}',
    'html.simpl-mm.simpl-rail button[data-test-id="btn-sac-main"]{position:static!important;height:44px!important;min-width:var(--mm-btn-w)!important;width:auto!important}',
    // (the verdict over the dock's right end, by the main button, the question scrolling under both)
    'html.simpl-mm.simpl-rail #results{right:30px!important;bottom:calc(18px + 64px + 10px + var(--simpl-lift,0px))!important;height:40px!important;max-width:min(60vw,520px)}',
    // (a narrow popup: Switch to New Layout said shorter, the tools closer, so the dock holds them all)
    '@media (max-width:900px){html.simpl-mm.simpl-rail #check-bar [data-test-id="switch-to-new-layout"]{font-size:0!important;padding:7px 6px!important}html.simpl-mm.simpl-rail #check-bar [data-test-id="switch-to-new-layout"]::before{content:"New Layout";font:500 12px/1 var(--mm-ui);margin-right:5px}html.simpl-mm.simpl-rail #check-bar [data-test-id="switch-to-new-layout"]>*{font-size:10px!important}html.simpl-mm.simpl-rail #tools li>button[data-test-id]{padding:0 12px!important}}',
    // the question: its number large, the arrows and attempts as glass beside it
    'html.simpl-mm.simpl-rail #attempts-holder{height:60px!important;min-height:60px!important}',
    'html.simpl-mm.simpl-rail #question-name{height:60px!important}',
    'html.simpl-mm.simpl-rail #question-name #nav-question-num{font:700 26px/1.15 var(--mm-ui)!important;letter-spacing:-.022em}',
    'html.simpl-mm.simpl-rail #question-name button{width:34px!important;height:34px!important;background:rgba(255,255,255,.72)!important;border:0!important;box-shadow:var(--mm-glass-edge),0 2px 8px rgba(30,41,82,.12)!important}',
    'html.simpl-mm.simpl-rail #attempts-holder.dropdown #attempts-button{top:12px!important;height:36px;border:0!important;background:rgba(255,255,255,.72)!important;box-shadow:var(--mm-glass-edge),0 2px 8px rgba(30,41,82,.12)!important}',
    // the sheet: liquid glass — light through it from the ground, a bright rim, a deep soft shadow
    'html.simpl-mm.simpl-rail #raptor-content-container .authoring-area>.parent-module-container,html.simpl-mm.simpl-rail #raptor-content-container .shared-module-container{background:var(--mm-sheet-glass)!important;border:0!important;border-radius:28px!important;box-shadow:var(--mm-glass-edge),var(--mm-float)!important}',
    // (the question scrolls from under its number down under the dock: as tall as the frame, room at its foot to scroll
    // its last line clear of the dock; Macmillan lays it out 960 wide — the popup zooms its page out to fit, as the
    // browser's own zoom would, told the width by the rail's `wide` — and centred when there is room to spare)
    'html.simpl-mm.simpl-rail .raptor-container{height:calc(100vh - 6px)!important;padding-bottom:0!important}',
    'html.simpl-mm.simpl-rail #raptor-content-container{width:auto!important;max-width:100%}',
    'html.simpl-mm.simpl-rail #raptor-content-container .content-area{margin:0!important;left:var(--simpl-left,0px)!important;padding-bottom:112px!important}',
    'html.simpl-mm.simpl-rail #raptor-content-container .authoring-area>.parent-module-container,html.simpl-mm.simpl-rail #sac .authoring-area .shared-module-container{min-height:calc(100vh - 190px)!important}',
    'html.simpl-mm.simpl-rail #sac .authoring-area .shared-module-container.vertical{height:calc(100vh - 190px)!important}',
    'html.simpl-mm.simpl-rail .labeling-answer-bank{border-radius:16px!important;overflow:hidden;background:rgba(255,255,255,.6)!important}',
    'html.simpl-mm.simpl-rail .numeric-entry-border{background:rgba(255,255,255,.92)!important}',
    'html.simpl-mm.simpl-rail .math-palette.as-popup{border-radius:18px!important;overflow:hidden}',
    // (a new question eases in where the last one was: up a little and clear, as a page turned)
    '@keyframes simpl-mm-turn{from{opacity:0;transform:translateY(12px)}to{opacity:1;transform:none}}',
    '@keyframes simpl-mm-fade{from{opacity:0}to{opacity:1}}',
    '@media (prefers-reduced-motion:no-preference){html.simpl-mm.simpl-rail #raptor-content-container.simpl-turn{animation:simpl-mm-turn .34s var(--mm-ease)}html.simpl-mm.simpl-rail #check-bar{animation:simpl-mm-fade .3s var(--mm-ease)}}',
    '@media (prefers-reduced-motion:reduce){html.simpl-mm.simpl-rail #raptor-content-container.simpl-turn{animation:simpl-mm-fade .2s ease}}',
    '@media (prefers-reduced-transparency:reduce){html.simpl-mm.simpl-rail{--mm-sheet-glass:#fff}html.simpl-mm.simpl-rail #check-bar::before{-webkit-backdrop-filter:none;backdrop-filter:none;background:#fff}}',

    // ---- (1.3.200) the popup's front end: only the question is the page's; the popup draws the rest
    // (Macmillan's chrome out of sight but there — its buttons pressed by the popup's own)
    'html.simpl-mm.simpl-front .hub_molecule_app_header{position:absolute!important;width:1px!important;height:1px!important;margin:-1px!important;padding:0!important;overflow:hidden!important;clip:rect(0 0 0 0)!important;border:0!important}',
    'html.simpl-mm.simpl-front #main-content{top:0!important}',
    'html.simpl-mm.simpl-front [aria-label="Open Resource Center"]{display:none!important}',
    'html.simpl-mm.simpl-front #check-bar,html.simpl-mm.simpl-front #results{opacity:0!important;pointer-events:none!important}',
    'html.simpl-mm.simpl-front #check-bar::before{display:none}',
    'html.simpl-mm.simpl-front #question-name{visibility:hidden!important}',
    'html.simpl-mm.simpl-front #attempts-holder{height:44px!important;min-height:44px!important}',
    'html.simpl-mm.simpl-front #attempts-holder.dropdown #attempts-button{top:4px!important}',
    'html.simpl-mm.simpl-front #sac{padding:6px 22px 0!important}',
    'html.simpl-mm.simpl-front .raptor-container{height:calc(100vh - 6px)!important}',
    'html.simpl-mm.simpl-front #raptor-content-container .content-area{padding-bottom:32px!important}',
    // ('glass': the popup's bar floats over the foot of the page — room under the question for it)
    'html.simpl-mm.simpl-front-glass #raptor-content-container .content-area{padding-bottom:124px!important}',
    // ('glass': no ground and no sheet of the page's own — the popup's Liquid Glass is under the clear page; its
    // answer fields and banks frosted over it)
    'html.simpl-mm.simpl-front-glass,html.simpl-mm.simpl-front-glass body,html.simpl-mm.simpl-front-glass #root,html.simpl-mm.simpl-front-glass #__next,html.simpl-mm.simpl-front-glass #main-content,html.simpl-mm.simpl-front-glass #sac,html.simpl-mm.simpl-front-glass #main{background:transparent!important}',
    'html.simpl-mm.simpl-front-glass #raptor-content-container .authoring-area>.parent-module-container,html.simpl-mm.simpl-front-glass #raptor-content-container .shared-module-container{background:transparent!important;box-shadow:none!important;border:0!important}',
    'html.simpl-mm.simpl-front-glass .numeric-entry-border{background:rgba(255,255,255,.66)!important;border-color:rgba(60,60,67,.22)!important}',
    'html.simpl-mm.simpl-front-glass .labeling-answer-bank{background:rgba(255,255,255,.42)!important;border:1px solid rgba(255,255,255,.7)!important}',
    'html.simpl-mm.simpl-front-glass #attempts-holder.dropdown #attempts-button{background:rgba(255,255,255,.58)!important}',
  ].join('\n');

  var style = document.createElement('style');
  style.id = 'simpl-mm-look';
  style.textContent = CSS;

  var tell = function (o) {
    try {
      if (window.parent !== window) window.top.postMessage({ simplMMTell: o }, '*');
      else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.simplLook) window.webkit.messageHandlers.simplLook.postMessage(o);
      else if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage({ simplLook: o });
    } catch (e) { /* (no app to tell) */ }
  };
  var flag = function (name, yes) { if (yes !== root.hasAttribute(name)) { if (yes) root.setAttribute(name, ''); else root.removeAttribute(name); } };

  // What the styles cannot see for themselves: the question open (the rail's row marked), how much is done, whether a
  // verdict is showing, and Macmillan's maths keyboard over the window's foot (the button and the verdict lifted over it).
  var told = false, queued = false;

  // (1.3.199) For the popup's rail, said again whenever it changes: each question (its number, Macmillan's word for its
  // state — success, default, … — the line under it and its points), the one open, how many are done, the score.
  var railSaid = '', opened = false, lastHere = 0;
  var text = function (e) { return e ? (e.textContent || '').replace(/\s+/g, ' ').trim() : ''; };
  // (1.3.202) The assignment's cover — its name, how many questions, when it is due, Macmillan's grading settings and its
  // Open Assignment — for the popup's own start screen, as Alta's (each setting's words in runs, bold where Macmillan's
  // are). Read by what it says, not by Macmillan's class names: the button that opens the work, the heading over it.
  var runsOf = function (e) {
    var runs = [], walk = function (n, bold) {
      if (n.nodeType === 3) {
        var t = n.nodeValue.replace(/\s+/g, ' ');
        if (!t) return;
        var last = runs[runs.length - 1];
        if (last && last.b === bold) last.t += t; else runs.push({ t: t, b: bold });
        return;
      }
      if (n.nodeType !== 1 || /^(svg|style|script|button)$/i.test(n.tagName)) return;
      var b = bold || /^(b|strong)$/i.test(n.tagName) || (parseInt(getComputedStyle(n).fontWeight, 10) || 400) >= 600;
      for (var c = n.firstChild; c; c = c.nextSibling) walk(c, b);
    };
    walk(e, false);
    if (runs.length) { runs[0].t = runs[0].t.replace(/^\s+/, ''); runs[runs.length - 1].t = runs[runs.length - 1].t.replace(/\s+$/, ''); }
    return runs.filter(function (r) { return r.t; });
  };
  var policyKind = function (w) {
    w = w.toLowerCase();
    return /attempt/.test(w) && !/incorrect/.test(w) ? 'attempts' : /partial credit|incorrect|penalt/.test(w) && !/late/.test(w) ? 'penalty' : /late/.test(w) ? 'late'
      : /solution|explanation/.test(w) ? 'solutions' : /resource/.test(w) ? 'resources' : /tutor|\bai\b/.test(w) ? 'tutor' : 'info';
  };
  var coverOf = function () {
    if (document.getElementById('question-nav') || document.getElementById('check-bar')) return null;
    var open = null, list = document.querySelectorAll('button, a[role="button"], a.btn');
    for (var i = 0; i < list.length && !open; i++) {
      if (shown(list[i]) && /^(open|start|begin|continue|resume|review)( (the )?(assignment|assessment|homework))?$/i.test(text(list[i]))) open = list[i];
    }
    if (!open) return null;
    var title = '', heads = document.querySelectorAll('h1, h2, h3');
    for (var h = 0; h < heads.length && !title; h++) {
      var ht = text(heads[h]);
      if (ht && shown(heads[h]) && !/^(assessment|grading settings)$/i.test(ht) && !heads[h].closest('.hub_molecule_app_header')) title = ht;
    }
    var count = 0, due = '', bits = document.querySelectorAll('p, span, div, li');
    for (var k = 0; k < bits.length; k++) {
      if (bits[k].children.length > 2) continue;
      var bt = text(bits[k]);
      if (!count && /^(\d+)\s+questions?$/i.test(bt)) count = +/^(\d+)/.exec(bt)[1];
      if (/^due:?\s+\S/i.test(bt) && bt.length < 60 && bt.length > due.length) due = bt.replace(/^due:?\s+/i, '');
    }
    var policies = [], gs = null;
    var hs = document.querySelectorAll('h1, h2, h3, h4, h5, h6, p, span');
    for (var m = 0; m < hs.length && !gs; m++) if (/^grading settings$/i.test(text(hs[m]))) gs = hs[m];
    if (gs) {
      var box = gs.parentElement, rows = [];
      for (var up = 0; up < 4 && box; up++) {
        rows = box.querySelectorAll('li');
        if (rows.length) break;
        box = box.parentElement;
      }
      if (!rows.length && gs.parentElement && gs.parentElement.nextElementSibling) rows = gs.parentElement.nextElementSibling.children;
      for (var r = 0; r < rows.length && policies.length < 12; r++) {
        var runs = runsOf(rows[r]), all = runs.map(function (x) { return x.t; }).join('');
        if (all && all.length < 200) policies.push({ kind: policyKind(all), runs: runs });
      }
    }
    return { title: title, count: count, due: due, start: { id: named(open, 'open'), label: text(open) }, policies: policies };
  };

  var report = function () {
    var rows = document.querySelectorAll('#question-nav > li[data-test-id^="item-"]');
    var cover = !rows.length && rail && coverOf();
    if (cover) {
      var oc = { rail: 'macmillan', rows: [], here: 0, done: 0, total: cover.count, score: '', title: '', policy: false, wide: 1024, cover: cover };
      var saidC = JSON.stringify(oc);
      if (saidC !== railSaid) { railSaid = saidC; tell(oc); }
      return 0;
    }
    if (!rows.length) {
      // (the side list folded: opened once, as its questions are only there while it is open)
      var nav = document.getElementById('side-nav'), open = nav && nav.querySelector('[data-test-id="nav-toggle-item"] > button');
      if (rail && !opened && nav && nav.classList.contains('closed') && open) { opened = true; open.click(); }
      return 0;
    }
    var list = [];
    for (var i = 0; i < rows.length; i++) {
      var m = /^item-(\d+)-([a-z-]*?)-?row$/i.exec(rows[i].getAttribute('data-test-id') || '');
      if (!m) continue;
      var num = rows[i].querySelector('[data-test-id$="-number"]');
      list.push({ n: +m[1], kind: m[2] || 'default', label: text(num) || ('Q' + m[1]), sub: text(num && num.nextElementSibling), points: text(rows[i].querySelector('[data-test-id$="-points"]')) });
    }
    var h = document.getElementById('nav-question-num') || document.querySelector('[data-test-id="nav-question-header"]');
    var at = /(\d+)\s+of\s+(\d+)/i.exec(text(h));
    var t = text(document.querySelector('[data-test-id="side-nav-button-title"]'));
    var done = /(\d+)\s+of\s+(\d+)/i.exec(t);
    var area = document.querySelector('#raptor-content-container .content-area');
    var o = { rail: 'macmillan', rows: list, here: at ? +at[1] : 0, done: done ? +done[1] : -1, total: done ? +done[2] : list.length,
      score: text(document.querySelector('[data-test-id="final-score-fraction"]')), title: t,
      policy: !!document.querySelector('[data-test-id="show-policy-details-button"]'),
      // (the width the question wants, in the page's own pixels: its sheet and the margins round it)
      // (1.3.200: Macmillan's own page holds its body 1024 wide at the least — the width the popup must fit, not the sheet's)
      wide: Math.ceil(Math.max((area ? parseFloat(getComputedStyle(area).width) || 960 : 960) + 48,
        (document.body && parseFloat(getComputedStyle(document.body).minWidth)) || 0)) };
    if (front) o.front = buttons();
    var said = JSON.stringify(o);
    if (rail && said !== railSaid) { railSaid = said; tell(o); }
    return o.here;
  };
  // (1.3.200) For the popup's front end: Macmillan's buttons as they stand — each named for the popup to press it, its
  // words, whether it can be pressed — and its verdict on the answer, if it has given one.
  var shown = function (e) { return !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden'; };
  var named = function (e, id) { if (e.getAttribute('data-simpl-id') !== id) e.setAttribute('data-simpl-id', id); return id; };
  var able = function (e) { return !e.disabled && !e.classList.contains('Mui-disabled') && e.getAttribute('aria-disabled') !== 'true'; };
  var buttons = function () {
    var f = { tools: [], main: null, verdict: null, layout: null };
    var main = document.querySelector('button[data-test-id="btn-sac-main"]');
    if (shown(main)) f.main = { id: named(main, 'main'), label: text(main), enabled: able(main) };
    var tools = document.querySelectorAll('#tools li > button[data-test-id]');
    for (var i = 0; i < tools.length; i++) {
      if (!shown(tools[i])) continue;
      f.tools.push({ id: named(tools[i], 'tool-' + i), label: text(tools[i]) || tools[i].getAttribute('aria-label') || '', kind: tools[i].getAttribute('data-test-id') || '', enabled: able(tools[i]) });
    }
    var layout = document.querySelector('[data-test-id="switch-to-new-layout"]');
    if (shown(layout)) f.layout = { id: named(layout, 'layout'), label: text(layout).replace(/\s*Beta$/i, '') };
    var said = document.getElementById('results'), words = text(said);
    if (words) f.verdict = { text: words, kind: said.classList.contains('correct') ? 'right' : said.classList.contains('incorrect') ? 'wrong' : 'said' };
    return f;
  };

  // What the popup asks of the page: a question opened (Macmillan's own row clicked), the grading settings shown, or
  // (1.3.200) one of Macmillan's own buttons pressed for the popup's.
  var act = function (c) {
    if (!c || typeof c !== 'object') return;
    var b = c.go ? document.querySelector('#question-nav > li[data-test-id^="item-' + (+c.go) + '-"]')
      : c.policy ? document.querySelector('[data-test-id="show-policy-details-button"]')
      : c.press ? document.querySelector('[data-simpl-id="' + String(c.press).replace(/[^a-z0-9-]/gi, '') + '"]') : null;
    if (b) b.click();
  };

  var mark = function () {
    queued = false;
    var now = report();
    // (a new question: eased in, as a page turned — not the first one drawn)
    if (on && rail && now && lastHere && now !== lastHere) {
      var turn = document.getElementById('raptor-content-container');
      if (turn) { turn.classList.remove('simpl-turn'); void turn.offsetWidth; turn.classList.add('simpl-turn'); }
    }
    if (now) lastHere = now;
    if (!on) return;
    root.classList.add('simpl-mm');
    var h = document.getElementById('nav-question-num') || document.querySelector('[data-test-id="nav-question-header"]');
    var at = h && /(\d+)\s+of\s+\d+/i.exec(h.textContent || '');
    var rows = document.querySelectorAll('#question-nav > li[data-test-id^="item-"]');
    var here = null;
    for (var i = 0; i < rows.length; i++) {
      var li = rows[i];
      var yes = !!at && (li.getAttribute('data-test-id') || '').indexOf('item-' + at[1] + '-') === 0;
      if (yes) here = li;
      if (yes !== li.hasAttribute('data-simpl-here')) { if (yes) li.setAttribute('data-simpl-here', ''); else li.removeAttribute('data-simpl-here'); }
    }
    flag('data-simpl-here', !!here);
    var t = document.querySelector('[data-test-id="side-nav-button-title"]');
    var done = t && /(\d+)\s+of\s+(\d+)/i.exec(t.textContent || '');
    if (done && +done[2] > 0) root.style.setProperty('--simpl-done', Math.min(100, Math.round(100 * done[1] / done[2])) + '%');
    flag('data-simpl-done', !!(done && +done[2] > 0));
    var said = document.getElementById('results');
    flag('data-simpl-said', !!(said && (said.textContent || '').trim()));
    var lift = 0, keys = document.querySelector('.math-palette.as-popup');
    if (keys) {
      var k = keys.getBoundingClientRect();
      var main = document.querySelector('button[data-test-id="btn-sac-main"]');
      var from = main ? main.getBoundingClientRect().left : innerWidth - 220;
      if (said && root.hasAttribute('data-simpl-said')) from = Math.min(from, said.getBoundingClientRect().left);
      if (k.width > 0 && k.height > 0 && k.right > from - 8) lift = Math.max(0, Math.round(innerHeight - k.top - 12));
    }
    root.style.setProperty('--simpl-lift', lift + 'px');
    if (rail) {
      // (1.3.199) the question centred in the room the frame has (the popup zooms the page out when it has less)
      var area = document.querySelector('#raptor-content-container .content-area'), box = document.getElementById('raptor-content-container');
      if (area && box) {
        var room = Math.min(box.clientWidth, root.clientWidth - 48) - 2;
        root.style.setProperty('--simpl-left', Math.max(0, Math.floor((room - (parseFloat(getComputedStyle(area).width) || 0)) / 2)) + 'px');
      }
    }
    if (!told && rows.length) {
      told = true;
      tell({ said: 'drawn', host: location.host, rows: rows.length, here: here ? here.getAttribute('data-test-id') : '' });
    }
  };
  var queue = function () { if (!queued) { queued = true; requestAnimationFrame(mark); } };
  var watching = false;
  var watch = function () {
    if (watching || !document.body) return;
    watching = true;
    if (document.head) document.head.appendChild(style);
    new MutationObserver(queue).observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class', 'style'] });
    addEventListener('resize', queue);
    queue();
  };

  var classes = function () {
    root.classList.toggle('simpl-mm', on);
    root.classList.toggle('simpl-rail', on && rail);
    root.classList.toggle('simpl-front', on && !!front);
    root.classList.toggle('simpl-front-glass', on && front === 'glass');
    root.classList.toggle('simpl-framed', framed);
  };
  var draw = function () {
    if (!root) return;
    classes();
    if (on || rail) { watch(); queue(); } // (the rail is told of the questions with the look off too)
  };
  var relay = function (d) {
    for (var i = 0; i < window.frames.length; i++) { try { window.frames[i].postMessage(d, '*'); } catch (e) { /* (gone) */ } }
  };
  window.__simplMMSet = function (v) { on = !!v; draw(); relay({ simplMMLook: on }); };
  window.__simplMMDo = function (c) { act(c); relay({ simplMMDo: c }); };
  addEventListener('message', function (e) {
    var d = e.data;
    if (!d || typeof d !== 'object') return;
    if (d.simplMMAsk === 1 && e.source) { try { e.source.postMessage({ simplMMLook: on }, '*'); } catch (x) { /* (gone) */ } return; }
    if (d.simplMMTell && typeof d.simplMMTell === 'object' && window.parent === window) { tell(d.simplMMTell); return; }
    if (typeof d.simplMMLook === 'boolean' && e.source === window.parent && window.parent !== window) window.__simplMMSet(d.simplMMLook);
    if (d.simplMMDo && typeof d.simplMMDo === 'object' && e.source === window.parent && window.parent !== window) window.__simplMMDo(d.simplMMDo);
  });
  // (a frame loaded after the look was turned: as its parent has it now)
  if (window.parent !== window) { try { window.parent.postMessage({ simplMMAsk: 1 }, '*'); } catch (e) { /* (no parent to ask) */ } }

  // (in from the start, so Macmillan's page is never drawn first in its own look; moved to the head's end once there is one)
  var begin = function () {
    root = document.documentElement;
    root.appendChild(style);
    classes();
    if (document.body) draw(); else document.addEventListener('DOMContentLoaded', draw, { once: true });
  };
  if (document.documentElement) begin();
  else new MutationObserver(function (_, o) { if (document.documentElement) { o.disconnect(); begin(); } }).observe(document, { childList: true });
})();
